using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>
/// LLM-planned AI in two tiers. Once per turn (after draw, before clock) a strategist model writes a TurnPlan:
/// the overall gameplan plus the small decisions (clock, discard, salvage/search, counter, encore, climax).
/// A cheaper executor model then plays the main phase one action at a time, choosing from the engine's legal
/// actions with the plan in mind. Anything the plan doesn't cover, and every decision when no plan is available
/// or the executor keeps failing, falls back to AIVersion6 using the deck's normal AI file.
/// </summary>
public class AIVersion8 : AIVersion6
{
	private TurnPlan plan = null;
	private string systemPrompt = null;
	private string deckNotes = "";
	private string logPath = null;
	private float gameCost = 0;
	private string executorSystemPrompt = null;

	/// <summary>Most actions the executor may take in one main phase, so a confused model can't loop forever.</summary>
	private const int MaxMainPhaseActions = 15;
	/// <summary>After this many unusable answers in one main phase, AIVersion6 finishes the phase.</summary>
	private const int MaxExecutorFailures = 2;

	private const string RulesPrimer =
@"You are the strategist for a Weiss Schwarz deck in a 1v1 game. Each of your turns, after the draw phase and before the clock phase, you write the gameplan.

Start from a read of the game: both players' damage and levels, how close each side is to losing, the opponent's board and its threats, how many climaxes are left in their deck (how likely your damage is to be cancelled), and what the opponent is likely to do next turn. From that, decide this turn's posture, how much damage to push, whether to wipe or trade with their board, what to field in each slot, and whether to play a climax.

A cheaper model plays your main phase action by action from your plan, so make field_plan, play_order, acts_and_events, stock_budget and climax_plan concrete. Your plan also directly decides, until your next turn:
- Clocking this turn (should_clock, clock_card).
- Which cards to keep in hand (hold_cards) and which to discard first (discard_priority), for hand-size discards and discard costs.
- What to take with salvage and search effects (search_priority).
- Whether to play a climax and which (climax_plan).
- Countering during the opponent's attacks (counter_policy, protect_cards, stock_to_reserve).
- Which characters not to encore (skip_encore).
Attacks are still chosen by the rules engine; attack_plan is guidance for now.

Use exact card names from the deck list. Only name cards that are actually in the zone the decision draws from. Remember the clock phase comes before the main phase: if you clock, you draw 2 first.";

	private const string ExecutorPrimer =
@"You play the main phase of a Weiss Schwarz game for one player, following a gameplan written by a stronger strategist model at the start of the turn.
Each step you get the plan, the current game state, what you've already done this main phase, and a numbered list of every legal action right now. Pick exactly one option number.
Follow the plan: field the characters it asks for in the slots it asks for, use the acts and events it names, stay within its stock budget, and end the main phase once the plan is carried out or nothing useful is left. Adapt if the board changed from what the plan expected, for example after an on-play effect drew or searched cards.
Placing a character on an occupied slot sends the current one to the waiting room. Center stage slots attack; back stage slots support.";

	public override void AfterDeckCreated() {
		// AIVersion6 strips comments from AIFileLines, so keep the strategy comments at the top of the file first.
		// They are written for human readers, but they make useful deck notes for the LLM too.
		var notes = new List<string>();
		foreach(string line in AIFileLines){
			string trimmed = line.Trim();
			if(trimmed.StartsWith("Section")){
				break;
			}
			if(trimmed.StartsWith("//")){
				notes.Add(trimmed.Substring(2).Trim());
			}
		}
		deckNotes = string.Join("\n", notes);
		base.AfterDeckCreated();

		string dir = Path.Combine(Application.persistentDataPath, "LLMAgentLogs");
		Directory.CreateDirectory(dir);
		logPath = Path.Combine(dir, "game_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + (player.isPlayer2 ? "_p2" : "_p1") + ".log");
	}

	public override IEnumerator OnTurnStartRoutine() {
		if(systemPrompt == null){
			systemPrompt = RulesPrimer + "\n\n# Your deck\n" + GameStateSerializer.DeckList(player)
				+ (deckNotes.Length > 0 ? "\n# Deck notes from the AI file author\n" + deckNotes : "");
		}
		string state = GameStateSerializer.CurrentState(player);
		if(plan != null){
			state += "\nYour plan from last turn was: " + plan.summary;
		}

		LLMThoughtsWindow window = LLMThoughtsWindow.Instance;
		window.BeginTurn("Turn " + player.game.turnCounter + " - " + ClaudePlanner.model + ", " + ClaudePlanner.effort + " effort");

		TurnPlan newPlan = null;
		string raw = null;
		float started = Time.realtimeSinceStartup;
		yield return StartCoroutine(ClaudePlanner.RequestPlan(systemPrompt, state, (p, r) => { newPlan = p; raw = r; },
			onThinking: window.AppendThinking,
			onText: delta => window.WritingPlan()));
		float seconds = Time.realtimeSinceStartup - started;

		Log("=== Turn " + player.game.turnCounter + " state ===\n" + state);
		Log("Plan (" + seconds.ToString("0.0") + "s): " + raw);
		string footer = seconds.ToString("0") + "s";
		ClaudePlanner.Usage usage = ClaudePlanner.lastUsage;
		if(usage != null){
			float cost = ClaudePlanner.EstimateCost(usage, ClaudePlanner.model);
			gameCost += cost;
			Log("Usage: input " + usage.input_tokens + ", cache write " + usage.cache_creation_input_tokens + ", cache read " + usage.cache_read_input_tokens
				+ ", output " + usage.output_tokens + " | about $" + cost.ToString("0.000") + " this turn, $" + gameCost.ToString("0.00") + " this game");
			footer += ", " + (usage.input_tokens + usage.cache_creation_input_tokens + usage.cache_read_input_tokens) + " in / " + usage.output_tokens
				+ " out tokens, ~$" + cost.ToString("0.000") + " (game ~$" + gameCost.ToString("0.00") + ")";
		}
		plan = newPlan;// null means every decision falls back to AIVersion6 this turn
		if(plan != null){
			window.ShowPlan(plan, footer);
			Say("Plan: " + plan.summary);
		}else{
			window.ShowError(raw ?? "No plan returned.");
		}
	}

	public override IEnumerator AIMainPhase() {
		ResetTurnPlans();
		string executor = ClaudePlanner.executorModel;
		if(plan == null || string.IsNullOrEmpty(executor)){
			yield return StartCoroutine(base.AIMainPhase());
			yield break;
		}
		if(executorSystemPrompt == null){
			executorSystemPrompt = ExecutorPrimer + "\n\n# Your deck\n" + GameStateSerializer.DeckList(player);
		}

		LLMThoughtsWindow window = LLMThoughtsWindow.Instance;
		window.BeginSection("Main phase - " + executor);
		int startStock = player.stock.size;
		int failures = 0;
		List<string> history = new List<string>();
		for(int step = 0; step < MaxMainPhaseActions && player.game.phase == Phase.Main; step++){
			int stockLeft = plan.stock_budget - (startStock - player.stock.size);
			List<MainPhaseActions.Option> options = MainPhaseActions.Build(player, stockLeft, out int hidden);
			if(options.Count == 1){
				Decision("Main: nothing left to do");
				break;
			}

			string prompt = "# Gameplan\n" + plan.DescribeForMainPhase()
				+ "\n# Game state\n" + GameStateSerializer.CurrentState(player)
				+ "\n# Done so far this main phase\n" + (history.Count > 0 ? string.Join("\n", history) : "nothing yet")
				+ "\n\n# Stock budget left: " + stockLeft + (hidden > 0 ? " (" + hidden + " cards in hand cost more and aren't listed)" : "")
				+ "\n\n# Legal actions\n" + MainPhaseActions.Numbered(options);

			string json = null;
			string error = null;
			float started = Time.realtimeSinceStartup;
			yield return StartCoroutine(ClaudePlanner.Request(executor, "low", MainPhaseActions.ChoiceSchema, executorSystemPrompt, prompt, 4000,
				(j, e) => { json = j; error = e; }));
			string cost = AddCost(executor, Time.realtimeSinceStartup - started);

			MainPhaseActions.Choice choice = null;
			if(json != null){
				try{
					choice = JsonUtility.FromJson<MainPhaseActions.Choice>(json);
				}catch(System.Exception){
					choice = null;
				}
			}
			if(choice == null || choice.option < 0 || choice.option >= options.Count){
				failures++;
				Decision("Main: unusable answer (" + (error ?? json) + ")");
				if(failures >= MaxExecutorFailures){
					break;
				}
				continue;
			}

			MainPhaseActions.Option chosen = options[choice.option];
			Decision("Main: " + chosen.description + " - " + choice.reason + " [" + cost + "]");
			if(chosen.run == null){
				yield break;// ended the main phase
			}
			string before = GameStateSerializer.CurrentState(player);
			yield return StartCoroutine(chosen.run());
			if(GameStateSerializer.CurrentState(player) == before){
				failures++;
				history.Add("- Tried: " + chosen.description + " (it had no effect)");
				Decision("Main: that action had no effect");
				if(failures >= MaxExecutorFailures){
					break;
				}
			}else{
				history.Add("- " + chosen.description);
			}
		}

		if(failures >= MaxExecutorFailures && player.game.phase == Phase.Main){
			Decision("Main: handing the rest of the main phase to AIVersion6");
			yield return StartCoroutine(base.AIMainPhase());
		}
	}

	public override IEnumerator AIClimaxPhase() {
		if(plan == null || player.game.firstTurn){
			yield return StartCoroutine(base.AIClimaxPhase());
			yield break;
		}
		if(plan.climax_plan.play == false){
			Decision("Climax: none, per plan (" + plan.climax_plan.reason + ")");
			yield break;
		}
		DeckCard climax = player.hand.GetContents().FirstOrDefault(c => c.IsClimax() && c.name == plan.climax_plan.climax && c.CanPlayFromHand());
		if(climax == null){
			Decision("Climax: planned " + plan.climax_plan.climax + " isn't playable, using the regular AI");
			yield return StartCoroutine(base.AIClimaxPhase());
			yield break;
		}
		Decision("Climax: " + climax.name + " per plan");
		yield return StartCoroutine(player.PlayClimax(climax));
	}

	/// <summary>Adds a request's cost to the game total and returns a short "time, cost" note.</summary>
	private string AddCost(string forModel, float seconds){
		ClaudePlanner.Usage usage = ClaudePlanner.lastUsage;
		if(usage == null){
			return seconds.ToString("0.0") + "s";
		}
		float cost = ClaudePlanner.EstimateCost(usage, forModel);
		gameCost += cost;
		Log("Usage (" + forModel + "): input " + usage.input_tokens + ", cache write " + usage.cache_creation_input_tokens + ", cache read "
			+ usage.cache_read_input_tokens + ", output " + usage.output_tokens + " | $" + cost.ToString("0.000") + ", game $" + gameCost.ToString("0.00"));
		return seconds.ToString("0.0") + "s, $" + cost.ToString("0.000");
	}

	public override DeckCard ChooseCardToClock(List<DeckCard> options, bool clockPhase) {
		// Clocking as a cost (clockPhase false) must return a card, so only the clock phase follows the plan.
		if(plan == null || clockPhase == false){
			return base.ChooseCardToClock(options, clockPhase);
		}
		if(plan.should_clock == false){
			Decision("Clock: skipped per plan");
			return null;
		}
		DeckCard named = options.FirstOrDefault(c => c.name == plan.clock_card);
		if(named != null){
			Decision("Clock: " + named.name + " per plan");
			return named;
		}
		// Plan wants to clock but didn't name a usable card: let V6 pick, avoiding held cards.
		DeckCard v6Choice = base.ChooseCardToClock(options, clockPhase);
		if(v6Choice == null || plan.Holds(v6Choice) == false){
			return v6Choice;
		}
		return options.FirstOrDefault(c => plan.Holds(c) == false) ?? v6Choice;
	}

	public override List<DeckCard> ChooseCardsToDiscard(List<DeckCard> options, int amount) {
		if(plan == null){
			return base.ChooseCardsToDiscard(options, amount);
		}
		// Ask V6 to rank every option, then let the plan reorder: listed discards first, held cards last.
		List<DeckCard> v6Order = base.ChooseCardsToDiscard(options, options.Count);
		List<DeckCard> chosen = v6Order
			.OrderBy(c => TurnPlan.RankIn(plan.discard_priority, c))
			.ThenBy(c => plan.Holds(c) ? 1 : 0)
			.ThenBy(c => v6Order.IndexOf(c))
			.Take(amount)
			.ToList();
		Decision("Discard: " + string.Join(", ", chosen.Select(c => c.name)));
		return chosen;
	}

	public override DeckCard ChooseCardToAddToHand(List<DeckCard> choices) {
		DeckCard planned = PlannedSearchPick(choices);
		if(planned != null){
			Decision("Add to hand: " + planned.name + " per plan");
			return planned;
		}
		return base.ChooseCardToAddToHand(choices);
	}

	public override void ChooseEffectTargets(List<DeckCard> options, AIChoiceSettings settings, bool optional, int amount, DeckCard effectOwner) {
		base.ChooseEffectTargets(options, settings, optional, amount, effectOwner);
		if(plan == null || settings == null || settings.context != AIChoiceContext.AddToHand){
			return;
		}
		// Salvage / search: put the plan's priority cards first, keep V6's picks as filler.
		var planned = options
			.Where(c => TurnPlan.RankIn(plan.search_priority, c) != int.MaxValue)
			.OrderBy(c => TurnPlan.RankIn(plan.search_priority, c))
			.ToList();
		if(planned.Count == 0){
			return;
		}
		int count = optional ? Mathf.Max(player.selectedCards.Count, Mathf.Min(amount, planned.Count)) : Mathf.Min(amount, options.Count);
		var result = planned.Concat(player.selectedCards.Where(c => planned.Contains(c) == false))
			.Concat(options.Where(c => planned.Contains(c) == false && player.selectedCards.Contains(c) == false))
			.Take(count)
			.ToList();
		player.ClearSelectedCards();
		foreach(DeckCard card in result){
			player.selectedCards.Add(card);
		}
		Decision("Add to hand (effect): " + string.Join(", ", result.Select(c => c.name)));
	}

	public override IEnumerator AICounterStep(DeckCard attacker, DeckCard defender) {
		if(plan != null){
			if(plan.counter_policy == "never"){
				Decision("Counter: skipped, policy never");
				yield break;
			}
			if(plan.counter_policy == "protect_listed_only" && plan.protect_cards.Contains(defender.name) == false){
				Decision("Counter: skipped, " + defender.name + " not protected");
				yield break;
			}
		}
		int stockBefore = player.stock.size;
		yield return StartCoroutine(base.AICounterStep(attacker, defender));
		if(plan != null && player.stock.size < plan.stock_to_reserve && player.stock.size < stockBefore){
			// V6 picks the counter card itself, so the reserve can't be enforced yet; log it for prompt tuning.
			Decision("Counter: spent below stock reserve (" + player.stock.size + " < " + plan.stock_to_reserve + ")");
		}
	}

	public override bool ShouldUseEncore(DeckCard card, CardEffect effect) {
		if(plan != null && plan.skip_encore.Contains(card.name)){
			Decision("Encore: skipped " + card.name + " per plan");
			return false;
		}
		return base.ShouldUseEncore(card, effect);
	}

	/// <summary>Highest-priority card from the plan's search list among the choices, or null.</summary>
	private DeckCard PlannedSearchPick(List<DeckCard> choices){
		if(plan == null || choices == null){
			return null;
		}
		return choices
			.Where(c => TurnPlan.RankIn(plan.search_priority, c) != int.MaxValue)
			.OrderBy(c => TurnPlan.RankIn(plan.search_priority, c))
			.FirstOrDefault();
	}

	/// <summary>A decision made from the plan: logged and shown in the thoughts window.</summary>
	private void Decision(string message){
		Log(message);
		LLMThoughtsWindow.Instance.AddDecision(message);
	}

	private void Log(string message){
		Debug.Log("[AIVersion8] " + message);
		if(logPath != null){
			try{
				File.AppendAllText(logPath, message + "\n");
			}catch(System.Exception){
				// Logging must never break a game.
			}
		}
	}

	/// <summary>Show a message in the in-game chat when AI messages are enabled.</summary>
	private void Say(string message){
		Log(message);
		if(printAIMessages && player.opponent.ui && player.opponent.ui.chat && player.opponent.isAI == false){
			player.opponent.ui.chat.PrintMessage(message);
		}
	}
}
