using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>
/// LLM-planned AI. Once per turn (after draw, before clock) Claude writes a TurnPlan; the small decisions
/// (clock, discard, salvage/search, counter, encore) follow that plan. Anything the plan doesn't cover,
/// and every decision when no plan is available, falls back to AIVersion6 using the deck's normal AI file.
/// </summary>
public class AIVersion8 : AIVersion6
{
	private TurnPlan plan = null;
	private string systemPrompt = null;
	private string deckNotes = "";
	private string logPath = null;
	private float gameCost = 0;

	private const string RulesPrimer =
@"You are the strategist for a Weiss Schwarz deck in a 1v1 game. Each of your turns, after the draw phase and before the clock phase, you write the gameplan.
A separate rules engine plays the cards. Your plan steers these decisions until your next turn:
- Clocking this turn (should_clock, clock_card).
- Which cards to keep in hand (hold_cards) and which to discard first (discard_priority). This applies to hand-size discards at end of turn and discard costs.
- What to take with salvage and search effects (search_priority).
- Countering during the opponent's attacks (counter_policy, protect_cards, stock_to_reserve).
- Which characters not to encore (skip_encore).
Main-phase plays and attacks are still handled by the engine.

Use exact card names from the deck list. Only name cards that are actually in the zone the decision draws from.
Think about damage race, level timing, clock-kill range, the opponent's likely climax count, and the stock you need for finishers and counters.";

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
