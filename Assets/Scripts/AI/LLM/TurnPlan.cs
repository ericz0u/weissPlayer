using System.Collections.Generic;

/// <summary>
/// The gameplan the LLM writes once per turn (after draw, before clock).
/// Field names match the JSON schema below so JsonUtility can parse the response directly.
/// The plan stays active through the opponent's next turn, so it also covers defensive choices (counters, encores, discards).
/// </summary>
[System.Serializable]
public class TurnPlan
{
	public string summary = "";
	public List<string> considerations = new List<string>();
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

	public bool Holds(DeckCard card){
		return card != null && hold_cards.Contains(card.name);
	}

	/// <summary>Position of the card's name in the list, or int.MaxValue when absent. Lower is better.</summary>
	public static int RankIn(List<string> list, DeckCard card){
		int index = card == null ? -1 : list.IndexOf(card.name);
		return index < 0 ? int.MaxValue : index;
	}

	/// <summary>
	/// JSON schema sent as output_config.format so the API only returns valid plans.
	/// Keep in sync with the fields above.
	/// </summary>
	public const string JsonSchema = @"{
  ""type"": ""object"",
  ""additionalProperties"": false,
  ""required"": [""summary"", ""considerations"", ""should_clock"", ""clock_card"", ""hold_cards"", ""discard_priority"", ""search_priority"", ""counter_policy"", ""protect_cards"", ""skip_encore"", ""stock_to_reserve""],
  ""properties"": {
    ""summary"": {""type"": ""string"", ""description"": ""One or two sentences: the plan for this turn and the opponent's next turn.""},
    ""considerations"": {""type"": ""array"", ""items"": {""type"": ""string""}, ""description"": ""Key risks and reasons behind the plan.""},
    ""should_clock"": {""type"": ""boolean""},
    ""clock_card"": {""type"": ""string"", ""description"": ""Exact name of a card in hand to clock, or empty string.""},
    ""hold_cards"": {""type"": ""array"", ""items"": {""type"": ""string""}, ""description"": ""Exact card names to keep in hand.""},
    ""discard_priority"": {""type"": ""array"", ""items"": {""type"": ""string""}, ""description"": ""Exact card names to discard first, best discard first.""},
    ""search_priority"": {""type"": ""array"", ""items"": {""type"": ""string""}, ""description"": ""Exact card names to salvage or search for, best first.""},
    ""counter_policy"": {""type"": ""string"", ""enum"": [""normal"", ""protect_listed_only"", ""never""]},
    ""protect_cards"": {""type"": ""array"", ""items"": {""type"": ""string""}, ""description"": ""Exact names of characters worth countering for.""},
    ""skip_encore"": {""type"": ""array"", ""items"": {""type"": ""string""}, ""description"": ""Exact names of characters not to encore.""},
    ""stock_to_reserve"": {""type"": ""integer"", ""description"": ""Stock to keep for the opponent's turn and next turn.""}
  }
}";
}
