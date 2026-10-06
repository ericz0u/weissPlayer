using System.Collections.Generic;
using System.Linq;
using System.Text;

/// <summary>
/// Turns the game into plain text for the LLM. Only includes information the AI player is allowed to know:
/// the opponent's hand, deck order and face-down cards are hidden.
/// </summary>
public static class GameStateSerializer
{
	/// <summary>
	/// Every unique card in the deck with its full text. Stays the same all game, so it goes in the cached system prompt.
	/// </summary>
	public static string DeckList(Player player){
		StringBuilder sb = new StringBuilder();
		foreach(var group in player.myCards.GroupBy(c => c.name).OrderBy(g => g.Key)){
			WeissCard id = group.First().GetIdentity();
			sb.Append(group.Count()).Append("x ").Append(CardStats(id)).AppendLine();
			string text = CardText(id);
			if(text.Length > 0){
				sb.Append("    ").AppendLine(text.Replace("\n", "\n    "));
			}
		}
		return sb.ToString();
	}

	public static string CurrentState(Player player){
		Player opp = player.opponent;
		StringBuilder sb = new StringBuilder();
		sb.AppendLine("Turn " + player.game.turnCounter + (player.game.turnPlayer == player ? " (your turn)" : " (opponent's turn)"));
		sb.AppendLine();
		sb.AppendLine("== YOU ==");
		AppendSide(sb, player, true);
		int myClimaxInDeck = player.deck.GetContents().Count(c => c.IsClimax());
		sb.AppendLine("Deck: " + player.deck.size + " cards, " + myClimaxInDeck + " climax remaining");
		sb.AppendLine("Hand (" + player.hand.size + "): " + Names(player.hand.GetContents(), true));
		sb.AppendLine();
		sb.AppendLine("== OPPONENT ==");
		AppendSide(sb, opp, false);
		sb.AppendLine("Deck: " + opp.deck.size + " cards, about " + AI.OpponentsClimaxCountInDeck(player) + " climax not yet seen");
		sb.AppendLine("Hand: " + opp.hand.size + " cards (hidden)");
		return sb.ToString();
	}

	private static void AppendSide(StringBuilder sb, Player p, bool mine){
		sb.AppendLine("Level " + p.level + ", clock " + p.clock.size + "/7: " + Names(p.clock.GetContents(), false));
		sb.AppendLine("Level zone: " + Names(p.levelZone.GetContents(), false));
		sb.AppendLine("Stock: " + p.stock.size);
		sb.AppendLine("Stage:");
		AppendSlot(sb, "  Left center", p.leftCenterStage);
		AppendSlot(sb, "  Middle center", p.middleCenterStage);
		AppendSlot(sb, "  Right center", p.rightCenterStage);
		AppendSlot(sb, "  Left back", p.leftBackstage);
		AppendSlot(sb, "  Right back", p.rightBackstage);
		if(p.climax.size > 0){
			sb.AppendLine("Climax zone: " + Names(p.climax.GetContents(), false));
		}
		sb.AppendLine("Waiting room (" + p.waitingRoom.size + "): " + Names(p.waitingRoom.GetContents(), false));
		if(p.memory.size > 0){
			sb.AppendLine("Memory: " + Names(p.memory.GetContents().Where(c => c.faceUp || mine), false));
		}
	}

	private static void AppendSlot(StringBuilder sb, string label, CardZone zone){
		DeckCard card = zone.GetContents().FirstOrDefault(c => c.IsCharacter());
		if(card == null){
			sb.AppendLine(label + ": empty");
			return;
		}
		sb.AppendLine(label + ": " + card.name + " [L" + card.GetLevel() + " " + card.GetPower() + "p " + card.GetSoul() + "s " + card.battlePosition + "]");
	}

	private static string Names(IEnumerable<DeckCard> cards, bool withStats){
		var list = cards.Select(c => withStats ? CardStats(c.GetIdentity()) : c.name).ToList();
		return list.Count == 0 ? "none" : string.Join("; ", list);
	}

	private static string CardStats(WeissCard id){
		if(id.category == CardCategory.Climax){
			return id.name + " (Climax, " + id.color + ", trigger " + id.trigger + ")";
		}
		if(id.category == CardCategory.Event){
			return id.name + " (Event, " + id.color + ", L" + id.level + " C" + id.cost + ")";
		}
		return id.name + " (" + id.color + ", L" + id.level + " C" + id.cost + " " + id.power + "p " + id.soul + "s, trigger " + id.trigger + ")";
	}

	private static string CardText(WeissCard id){
		if(string.IsNullOrWhiteSpace(id.text) == false){
			return id.text.Trim();
		}
		return string.Join("\n", id.cardEffectList.Select(e => e.text).Where(t => string.IsNullOrWhiteSpace(t) == false));
	}
}
