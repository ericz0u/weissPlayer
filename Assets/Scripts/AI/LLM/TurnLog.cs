using System.Collections.Generic;
using System.Linq;
using System.Text;

/// <summary>
/// Records what happens in the game, as a player watching the table would see it, so the strategist can read
/// what happened since its last plan. Polled every frame by AIVersion8: it compares each card's zone and battle
/// position with the last frame, and watches both players' attacks. Needs no hooks in the engine.
/// Moves through short-lived zones (trigger, damage, event resolution, revealed cards) are logged as one move
/// from where the card was to where it ends up. Moves between hidden zones (draws, stock payments into stock) are skipped.
/// </summary>
public class TurnLog
{
	private const int MaxLines = 150;

	private readonly Player me;
	private readonly Dictionary<DeckCard, CardZone> stableZone = new Dictionary<DeckCard, CardZone>();
	private readonly Dictionary<DeckCard, CardPosition> lastPosition = new Dictionary<DeckCard, CardPosition>();
	private readonly Dictionary<Player, DeckCard> lastAttacker = new Dictionary<Player, DeckCard>();
	private readonly Dictionary<Player, DeckCard> lastTrigger = new Dictionary<Player, DeckCard>();
	private readonly List<string> lines = new List<string>();
	private string pendingHeader = null;
	private int lastTurn = -1;
	private Phase lastPhase;
	private string lastMoveKey = null;

	/// <summary>Names of opponent cards that have been seen face up this game (stage, waiting room, climax, events used).</summary>
	public readonly HashSet<string> opponentCardsSeen = new HashSet<string>();

	public TurnLog(Player me){
		this.me = me;
		foreach(DeckCard card in AllCards()){
			if(card.location != null && IsTransient(card.location) == false){
				stableZone[card] = card.location;
			}
			lastPosition[card] = card.battlePosition;
		}
	}

	/// <summary>Everything logged since the last call, then starts a fresh log.</summary>
	public string TakeLog(){
		string text = lines.Count == 0 ? "nothing recorded" : string.Join("\n", lines);
		lines.Clear();
		lastMoveKey = null;
		return text;
	}

	public void Poll(){
		Player opp = me.opponent;
		if(opp == null || me.game == null){
			return;
		}
		if(me.game.turnCounter != lastTurn || me.game.phase != lastPhase){
			lastTurn = me.game.turnCounter;
			lastPhase = me.game.phase;
			pendingHeader = "Turn " + lastTurn + " (" + (me.game.turnPlayer == me ? "your" : "opponent's") + " turn), " + lastPhase + " phase:";
			lastMoveKey = null;
		}

		foreach(Player p in new[]{ me, opp }){
			DeckCard attacker = p.attackingCharacter;
			lastAttacker.TryGetValue(p, out DeckCard previous);
			if(attacker != null && attacker != previous){
				Add(Who(p) + ": " + DescribeAttack(p, attacker));
			}
			lastAttacker[p] = attacker;

			DeckCard triggered = p.trigger.topCard;
			lastTrigger.TryGetValue(p, out DeckCard previousTrigger);
			if(triggered != null && triggered != previousTrigger){
				TriggerIcon icon = triggered.GetIdentity().trigger;
				Add(Who(p) + ": trigger check revealed " + triggered.name + (triggered.IsClimax() ? " (climax)" : "") + (icon != TriggerIcon.None ? ", trigger " + icon : ""));
				if(p != me){
					opponentCardsSeen.Add(triggered.name);
				}
			}
			lastTrigger[p] = triggered;
		}

		foreach(DeckCard card in AllCards()){
			CardZone zone = card.location;
			if(zone != null && IsTransient(zone) == false){
				stableZone.TryGetValue(card, out CardZone from);
				if(from != zone){
					stableZone[card] = zone;
					if(from != null){
						RecordMove(card, from, zone);
					}
				}
			}
			lastPosition.TryGetValue(card, out CardPosition before);
			if(card.battlePosition != before){
				lastPosition[card] = card.battlePosition;
				if(card.battlePosition == CardPosition.Reverse && card.onStage){
					Add(Who(card.owner) + ": " + card.name + " was reversed");
				}
			}
		}
	}

	private IEnumerable<DeckCard> AllCards(){
		return me.myCards.Concat(me.opponent != null ? me.opponent.myCards : new List<DeckCard>());
	}

	private string DescribeAttack(Player p, DeckCard attacker){
		string who = attacker.name + " (" + attacker.GetPower() + "p " + attacker.GetSoul() + "s)";
		if(p.directAttacking){
			return who + " direct attacks";
		}
		if(p.battleOpponent != null){
			return who + " frontal attacks " + p.battleOpponent.name + " (" + p.battleOpponent.GetPower() + "p)";
		}
		DeckCard facing = p.GetCardsInLocation(GameLocation.Facing, attacker).FirstOrDefault();
		return who + " side attacks" + (facing != null ? " " + facing.name : "");
	}

	private void RecordMove(DeckCard card, CardZone from, CardZone to){
		Player owner = card.owner;
		bool hiddenFrom = IsHidden(owner, from);
		bool hiddenTo = IsHidden(owner, to);
		if(hiddenFrom && hiddenTo){
			return;// draws, searches into hand from deck order, stock moves: nothing visible worth logging
		}
		// Cards you can't see stay unnamed: an opponent's card going into their hand, deck or stock from a public zone is still named
		// (everyone saw it), but one that never left hidden zones can't reach here.
		bool mine = owner == me;
		string name = card.name;
		if(mine == false && (to == owner.waitingRoom || to == owner.climax || IsStage(owner, to) || to == owner.clock || to == owner.levelZone || to == owner.memory)){
			opponentCardsSeen.Add(card.name);
		}
		string tag = from == owner.hand && to == owner.waitingRoom
			? (card.IsEvent() ? " (event played)" : me.game.turnPlayer != owner && me.game.phase == Phase.Attack ? " (counter / backup or discard)" : " (discarded)")
			: "";
		if(from == owner.deck && to == owner.waitingRoom && me.game.phase == Phase.Attack && me.game.turnPlayer != owner){
			tag = card.IsClimax() ? " (damage cancelled by this climax)" : " (part of a cancelled damage instance, or milled)";
		}
		string key = Who(owner) + ": " + ZoneName(owner, from) + " -> " + ZoneName(owner, to);
		bool marker = IsStage(owner, to) && to.topCard != card;
		if(key == lastMoveKey && lines.Count > 0 && tag == "" && marker == false){
			lines[lines.Count - 1] += ", " + name;
			return;
		}
		lastMoveKey = tag == "" && marker == false ? key : null;
		if(IsStage(owner, to) && to.topCard != card){
			tag += " (as a marker under " + (to.topCard != null ? to.topCard.name : "a character") + ")";
		}
		Add(key + ": " + name + tag);
	}

	private void Add(string line){
		if(pendingHeader != null){
			lines.Add(pendingHeader);
			pendingHeader = null;
		}
		lines.Add("  " + line);
		if(lines.Count > MaxLines){
			lines.RemoveRange(0, lines.Count - MaxLines);
			lines.Insert(0, "(older events trimmed)");
		}
		if(line.Contains(" -> ") == false){
			lastMoveKey = null;
		}
	}

	private string Who(Player p){
		return p == me ? "You" : "Opponent";
	}

	private static bool IsStage(Player p, CardZone zone){
		return zone == p.leftCenterStage || zone == p.middleCenterStage || zone == p.rightCenterStage || zone == p.leftBackstage || zone == p.rightBackstage;
	}

	private static bool IsHidden(Player p, CardZone zone){
		return zone == p.hand || zone == p.deck || zone == p.stock;
	}

	/// <summary>Zones cards only pass through: logged as a single move from before to after.</summary>
	private bool IsTransient(CardZone zone){
		foreach(Player p in new[]{ me, me.opponent }){
			if(p == null){
				continue;
			}
			if(zone == p.damageZone || zone == p.trigger || zone == p.eventResolution || zone == p.scryArea || zone == p.flippedCards){
				return true;
			}
			if(zone == p.deck || zone == p.hand || zone == p.stock || zone == p.clock || zone == p.levelZone || zone == p.waitingRoom
				|| zone == p.memory || zone == p.climax || IsStage(p, zone)){
				return false;
			}
		}
		return true;// anything else (revealed cards and similar) is passed through too
	}

	private static string ZoneName(Player p, CardZone zone){
		if(zone == p.deck) return "deck";
		if(zone == p.hand) return "hand";
		if(zone == p.stock) return "stock";
		if(zone == p.clock) return "clock";
		if(zone == p.levelZone) return "level";
		if(zone == p.waitingRoom) return "waiting room";
		if(zone == p.memory) return "memory";
		if(zone == p.climax) return "climax area";
		if(IsStage(p, zone)) return MainPhaseActions.SlotName(p, zone);
		return zone.name;
	}
}
