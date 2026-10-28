using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

/// <summary>
/// Lists the legal main-phase actions for the executor model as numbered options, and runs the one it picks.
/// Legality comes from the engine's own DeckCard.GetActionList, the same source the human player's UI uses.
/// </summary>
public static class MainPhaseActions
{
	public class Option
	{
		public string description;
		/// <summary>Runs the action. Null for "end the main phase".</summary>
		public System.Func<IEnumerator> run;
	}

	/// <summary>The executor's answer. Field names match ChoiceSchema.</summary>
	[System.Serializable]
	public class Choice
	{
		public string reason = "";
		public int option = -1;
	}

	public const string ChoiceSchema = @"{
  ""type"": ""object"",
  ""additionalProperties"": false,
  ""required"": [""reason"", ""option""],
  ""properties"": {
    ""reason"": {""type"": ""string"", ""description"": ""One or two sentences on why this action fits the plan.""},
    ""option"": {""type"": ""integer"", ""description"": ""The number of the chosen option.""}
  }
}";

	/// <summary>
	/// Every legal action right now, with "End the main phase" first. Plays that cost more than stockLeft are left out
	/// and counted in hiddenOverBudget, so the executor can't overspend the plan's stock budget.
	/// </summary>
	public static List<Option> Build(Player player, int stockLeft, out int hiddenOverBudget){
		List<Option> options = new List<Option>{ new Option{ description = "End the main phase", run = null } };
		HashSet<string> seen = new HashSet<string>();
		hiddenOverBudget = 0;

		// Hand: characters (one option per slot), events, hand abilities. Copies of the same card give the same options.
		foreach(DeckCard card in player.hand.GetContents()){
			foreach(CardAction action in card.GetActionList(false, player)){
				int cost = card.GetCost();
				switch(action.actionType){
					case CardActionType.PlayFromHand:
						foreach(CardZone slot in player.GetStageSlots(false, true, true, true)){
							DeckCard occupant = slot.topCard;
							string text = "Play " + Describe(card) + " to " + SlotName(player, slot)
								+ (occupant != null ? " (sends " + occupant.name + " to the waiting room)" : "") + ", cost " + cost;
							if(cost > stockLeft){
								hiddenOverBudget += seen.Add("over:" + card.name) ? 1 : 0;
								continue;
							}
							DeckCard c = card;
							CardZone s = slot;
							Add(options, seen, text, () => PlayCharacter(player, c, s));
						}
					break;
					case CardActionType.PlayEvent:
						if(cost > stockLeft){
							hiddenOverBudget += seen.Add("over:" + card.name) ? 1 : 0;
							break;
						}
						DeckCard eventCard = card;
						Add(options, seen, "Play event " + card.name + ", cost " + cost + ": " + Flatten(action.actEffect?.text), () => player.PlayEvent(eventCard));
					break;
					case CardActionType.HandAction:
						DeckCard handCard = card;
						CardEffect handEffect = action.actEffect;
						Add(options, seen, "Use the ability of " + card.name + " in hand: " + Flatten(handEffect.text), () => player.UseHandAction(handCard, handEffect));
					break;
				}
			}
		}

		// Stage: act abilities and moves.
		List<CardZone> slots = player.GetStageSlots(false, true, true, true);
		foreach(CardZone zone in slots){
			DeckCard card = zone.topCard;
			if(card == null){
				continue;
			}
			foreach(CardAction action in card.GetActionList(false, player)){
				if(action.actionType == CardActionType.UseAct){
					DeckCard actCard = card;
					CardEffect actEffect = action.actEffect;
					Add(options, seen, "Use the act ability of " + card.name + " (" + SlotName(player, zone) + "): " + Flatten(actEffect.text),
						() => player.UseAct(actCard, actEffect));
				}
			}
		}
		for(int a = 0; a < slots.Count; a++){
			for(int b = a + 1; b < slots.Count; b++){
				DeckCard first = slots[a].topCard;
				DeckCard second = slots[b].topCard;
				if(first == null && second == null){
					continue;
				}
				if((first != null && CanMove(player, first) == false) || (second != null && CanMove(player, second) == false)){
					continue;
				}
				string text = first != null && second != null
					? "Swap " + first.name + " (" + SlotName(player, slots[a]) + ") with " + second.name + " (" + SlotName(player, slots[b]) + ")"
					: "Move " + (first ?? second).name + " from " + SlotName(player, first != null ? slots[a] : slots[b]) + " to " + SlotName(player, first != null ? slots[b] : slots[a]);
				CardZone za = slots[a];
				CardZone zb = slots[b];
				Add(options, seen, text, () => Swap(player, za, zb));
			}
		}
		return options;
	}

	/// <summary>
	/// Every attack the engine allows right now, one option per attacker and attack type, with "End the attack phase" first.
	/// </summary>
	public static List<Option> BuildAttacks(Player player){
		List<Option> options = new List<Option>{ new Option{ description = "End the attack phase (no more attacks this turn)", run = null } };
		foreach(DeckCard card in player.GetCardsInLocation(GameLocation.CenterStage)){
			if(card.battlePosition != CardPosition.Stand){
				continue;
			}
			string attacker = card.name + " (" + SlotName(player, card.location) + ", " + card.GetPower() + "p " + card.GetSoul() + "s)";
			foreach(CardAction action in card.GetActionList(false, player)){
				DeckCard a = card;
				DeckCard target = action.attackTarget;
				switch(action.actionType){
					case CardActionType.DirectAttack:
						options.Add(new Option{ description = "Direct attack with " + attacker + ": +1 soul, no battle", run = () => player.DirectAttack(a) });
					break;
					case CardActionType.SideAttack:
						if(target != null){
							options.Add(new Option{ description = "Side attack with " + attacker + " into " + DescribeDefender(target)
								+ ": soul -" + target.GetLevel() + ", no battle", run = () => player.SideAttack(a, target) });
						}
					break;
					case CardActionType.FrontalAttack:
					case CardActionType.FrontalAttackLeftCenterStage:
					case CardActionType.FrontalAttackMiddleCenterStage:
					case CardActionType.FrontalAttackRightCenterStage:
					case CardActionType.FrontalAttackLeftBackstage:
					case CardActionType.FrontalAttackRightBackstage:
						if(target != null){
							options.Add(new Option{ description = "Frontal attack with " + attacker + " into " + DescribeDefender(target) + ": battle",
								run = () => player.FrontalAttack(a, target) });
						}
					break;
				}
			}
		}
		return options;
	}

	private static string DescribeDefender(DeckCard target){
		return target.name + " (L" + target.GetLevel() + " " + target.GetPower() + "p, " + target.battlePosition + ")";
	}

	public static string Numbered(List<Option> options){
		StringBuilder sb = new StringBuilder();
		for(int i = 0; i < options.Count; i++){
			sb.Append(i).Append(": ").AppendLine(options[i].description);
		}
		return sb.ToString();
	}

	private static void Add(List<Option> options, HashSet<string> seen, string text, System.Func<IEnumerator> run){
		if(seen.Add(text)){
			options.Add(new Option{ description = text, run = run });
		}
	}

	private static bool CanMove(Player player, DeckCard card){
		return card.GetActionList(false, player).Any(a => a.actionType == CardActionType.MoveCard);
	}

	/// <summary>Same steps as AIVersion6 uses to play a character: pay, place (overplaying any occupant), resolve triggers.</summary>
	private static IEnumerator PlayCharacter(Player player, DeckCard card, CardZone slot){
		if(card.CanPlayFromHand() == false || card.location != player.hand){
			yield break;
		}
		int cost = card.GetCost();
		if(cost > 0){
			yield return player.StartCoroutine(player.PayStock(cost));
		}
		yield return player.StartCoroutine(player.PlaceCardOnStage(card, slot));
		yield return player.StartCoroutine(player.game.CheckQueue());
	}

	private static IEnumerator Swap(Player player, CardZone a, CardZone b){
		yield return player.StartCoroutine(CardZone.SwapContents(a, b));
		player.game.UpdateStageStats();
	}

	public static string SlotName(Player player, CardZone zone){
		if(zone == player.leftCenterStage) return "left center";
		if(zone == player.middleCenterStage) return "middle center";
		if(zone == player.rightCenterStage) return "right center";
		if(zone == player.leftBackstage) return "left back";
		if(zone == player.rightBackstage) return "right back";
		return zone.name;
	}

	private static string Describe(DeckCard card){
		return card.name + " (L" + card.GetLevel() + " " + card.GetPower() + "p " + card.GetSoul() + "s)";
	}

	private static string Flatten(string text){
		if(string.IsNullOrEmpty(text)){
			return "";
		}
		text = text.Replace("\r", " ").Replace("\n", " ");
		return text.Length > 220 ? text.Substring(0, 220) + "..." : text;
	}
}
