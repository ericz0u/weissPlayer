using System.Collections.Generic;
using System.Text;

/// <summary>
/// The gameplan the strategist model writes once per turn (after draw, before clock).
/// Field names match the JSON schema below so JsonUtility can parse the response directly.
/// The plan stays active through the opponent's next turn, so it also covers defensive choices (counters, encores, discards).
/// The main-phase executor reads it as text (Describe) and the engine reads the decision fields directly.
/// </summary>
[System.Serializable]
public class TurnPlan
{
	// ---- Read of the game ----
	public string opponent_read = "";
	/// <summary>"aggro", "setup", "control" or "defensive"</summary>
	public string posture = "setup";
	public string damage_goal = "";
	/// <summary>"wipe", "trade", "ignore_board" or "protect_stage"</summary>
	public string board_goal = "trade";
	/// <summary>Opponent characters to remove or reverse this turn, by name.</summary>
	public List<string> removal_targets = new List<string>();
	public string summary = "";
	public List<string> considerations = new List<string>();

	// ---- This turn's main phase ----
	public FieldPlan field_plan = new FieldPlan();
	/// <summary>Cards to play this turn, in order.</summary>
	public List<string> play_order = new List<string>();
	/// <summary>Act abilities and events to use this turn, described in a few words each.</summary>
	public List<string> acts_and_events = new List<string>();
	/// <summary>Most stock to spend during this main phase. Plays that would go past it aren't offered.</summary>
	public int stock_budget = 0;
	public ClimaxPlan climax_plan = new ClimaxPlan();
	public string attack_plan = "";

	// ---- Smaller decisions (clock, hand, salvage, opponent's turn) ----
	public bool should_clock = false;
	/// <summary>Exact name of the card in hand to clock, or "" to let the AI pick when should_clock is true.</summary>
	public string clock_card = "";
	/// <summary>Never discard or clock these unless there is no other choice.</summary>
	public List<string> hold_cards = new List<string>();
	/// <summary>Discard these first, in order.</summary>
	public List<string> discard_priority = new List<string>();
	/// <summary>Salvage / search targets, best first.</summary>
	public List<string> search_priority = new List<string>();
	/// <summary>"normal", "protect_listed_only" or "never"</summary>
	public string counter_policy = "normal";
	public List<string> protect_cards = new List<string>();
	public List<string> skip_encore = new List<string>();
	public int stock_to_reserve = 0;

	[System.Serializable]
	public class FieldPlan
	{
		public string left_center = "";
		public string middle_center = "";
		public string right_center = "";
		public string left_back = "";
		public string right_back = "";
	}

	[System.Serializable]
	public class ClimaxPlan
	{
		public bool play = false;
		/// <summary>Exact name of the climax to play, or "" for none.</summary>
		public string climax = "";
		public string reason = "";
	}

	public bool Holds(DeckCard card){
		return card != null && hold_cards.Contains(card.name);
	}

	/// <summary>Position of the card's name in the list, or int.MaxValue when absent. Lower is better.</summary>
	public static int RankIn(List<string> list, DeckCard card){
		int index = card == null ? -1 : list.IndexOf(card.name);
		return index < 0 ? int.MaxValue : index;
	}

	/// <summary>The parts of the plan the main-phase executor needs, as text.</summary>
	public string DescribeForMainPhase(){
		StringBuilder sb = new StringBuilder();
		sb.AppendLine("Summary: " + summary);
		sb.AppendLine("Posture: " + posture + ". Damage goal: " + damage_goal + ". Board goal: " + board_goal
			+ (removal_targets.Count > 0 ? " (remove " + string.Join(", ", removal_targets) + ")" : "") + ".");
		sb.AppendLine("Opponent read: " + opponent_read);
		sb.AppendLine("Field plan at end of main phase: left center " + field_plan.left_center + "; middle center " + field_plan.middle_center
			+ "; right center " + field_plan.right_center + "; left back " + field_plan.left_back + "; right back " + field_plan.right_back);
		sb.AppendLine("Play order: " + (play_order.Count > 0 ? string.Join(", ", play_order) : "none"));
		sb.AppendLine("Acts and events: " + (acts_and_events.Count > 0 ? string.Join("; ", acts_and_events) : "none"));
		sb.AppendLine("Stock budget for this main phase: " + stock_budget + ". Keep " + stock_to_reserve + " for the opponent's turn.");
		sb.AppendLine("Climax: " + (climax_plan.play ? "play " + climax_plan.climax : "don't play one") + " (" + climax_plan.reason + ")");
		sb.AppendLine("Attack plan: " + attack_plan);
		if(hold_cards.Count > 0){
			sb.AppendLine("Keep in hand: " + string.Join(", ", hold_cards));
		}
		return sb.ToString();
	}

	/// <summary>
	/// JSON schema sent as output_config.format so the API only returns valid plans. Keep in sync with the fields above.
	/// Strict structured outputs need every property listed in "required" and additionalProperties false.
	/// </summary>
	public const string JsonSchema = @"{
  ""type"": ""object"",
  ""additionalProperties"": false,
  ""required"": [""opponent_read"", ""posture"", ""damage_goal"", ""board_goal"", ""removal_targets"", ""summary"", ""considerations"", ""field_plan"", ""play_order"", ""acts_and_events"", ""stock_budget"", ""climax_plan"", ""attack_plan"", ""should_clock"", ""clock_card"", ""hold_cards"", ""discard_priority"", ""search_priority"", ""counter_policy"", ""protect_cards"", ""skip_encore"", ""stock_to_reserve""],
  ""properties"": {
    ""opponent_read"": {""type"": ""string"", ""description"": ""The opponent's position: damage, level, likely next turn, threats on their board, climaxes left in their deck and how likely damage is to be cancelled.""},
    ""posture"": {""type"": ""string"", ""enum"": [""aggro"", ""setup"", ""control"", ""defensive""]},
    ""damage_goal"": {""type"": ""string"", ""description"": ""How much damage to push this turn and why, e.g. put them to 2-0, or hold back to avoid their clock-kill range.""},
    ""board_goal"": {""type"": ""string"", ""enum"": [""wipe"", ""trade"", ""ignore_board"", ""protect_stage""]},
    ""removal_targets"": {""type"": ""array"", ""items"": {""type"": ""string""}, ""description"": ""Exact names of opponent characters to reverse or remove this turn.""},
    ""summary"": {""type"": ""string"", ""description"": ""One or two sentences: the plan for this turn and the opponent's next turn.""},
    ""considerations"": {""type"": ""array"", ""items"": {""type"": ""string""}, ""description"": ""Key risks and reasons behind the plan.""},
    ""field_plan"": {
      ""type"": ""object"",
      ""additionalProperties"": false,
      ""required"": [""left_center"", ""middle_center"", ""right_center"", ""left_back"", ""right_back""],
      ""description"": ""What each stage slot should hold at the end of the main phase: an exact card name, keep, or empty."",
      ""properties"": {
        ""left_center"": {""type"": ""string""},
        ""middle_center"": {""type"": ""string""},
        ""right_center"": {""type"": ""string""},
        ""left_back"": {""type"": ""string""},
        ""right_back"": {""type"": ""string""}
      }
    },
    ""play_order"": {""type"": ""array"", ""items"": {""type"": ""string""}, ""description"": ""Exact names of cards to play from hand this turn, in order.""},
    ""acts_and_events"": {""type"": ""array"", ""items"": {""type"": ""string""}, ""description"": ""Act abilities and events to use this turn, a few words each.""},
    ""stock_budget"": {""type"": ""integer"", ""description"": ""Most stock to spend in this main phase.""},
    ""climax_plan"": {
      ""type"": ""object"",
      ""additionalProperties"": false,
      ""required"": [""play"", ""climax"", ""reason""],
      ""properties"": {
        ""play"": {""type"": ""boolean""},
        ""climax"": {""type"": ""string"", ""description"": ""Exact name of the climax in hand to play, or empty.""},
        ""reason"": {""type"": ""string""}
      }
    },
    ""attack_plan"": {""type"": ""string"", ""description"": ""Per center slot: front, side or direct attack and against what.""},
    ""should_clock"": {""type"": ""boolean""},
    ""clock_card"": {""type"": ""string"", ""description"": ""Exact name of a card in hand to clock, or empty string.""},
    ""hold_cards"": {""type"": ""array"", ""items"": {""type"": ""string""}, ""description"": ""Exact card names to keep in hand.""},
    ""discard_priority"": {""type"": ""array"", ""items"": {""type"": ""string""}, ""description"": ""Exact card names to discard first, best discard first.""},
    ""search_priority"": {""type"": ""array"", ""items"": {""type"": ""string""}, ""description"": ""Exact card names to salvage or search for, best first.""},
    ""counter_policy"": {""type"": ""string"", ""enum"": [""normal"", ""protect_listed_only"", ""never""]},
    ""protect_cards"": {""type"": ""array"", ""items"": {""type"": ""string""}, ""description"": ""Exact names of characters worth countering for.""},
    ""skip_encore"": {""type"": ""array"", ""items"": {""type"": ""string""}, ""description"": ""Exact names of characters not to encore.""},
    ""stock_to_reserve"": {""type"": ""integer"", ""description"": ""Stock to keep for the opponent's turn.""}
  }
}";
}
