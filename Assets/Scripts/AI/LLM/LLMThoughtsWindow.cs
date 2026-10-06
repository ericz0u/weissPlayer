using System.Text;
using UnityEngine;

/// <summary>
/// Movable, resizable on-screen window that shows the LLM's streamed thinking, its plan, and the decisions made from it.
/// Drag the title bar to move, drag the bottom-right corner to resize, F8 to show or hide.
/// Position, size, font size and visibility are saved in PlayerPrefs.
/// Note: it shows what the AI knows, including its own hand, so it gives away information when you play against it.
/// </summary>
public class LLMThoughtsWindow : MonoBehaviour
{
	private const string VisiblePref = "LLM_ShowPanel";
	private const int MaxChars = 40000;
	private const float HandleSize = 18;
	private const int WindowId = 0x4C4C4D;

	private static LLMThoughtsWindow instance;

	private Rect rect;
	private bool visible;
	private bool minimized = false;
	private bool resizing = false;
	private bool autoScroll = true;
	private int fontSize;
	private Vector2 scroll;
	private string status = "Waiting for the AI's turn";

	private readonly StringBuilder content = new StringBuilder();
	private GUIStyle textStyle;

	public static bool ShowByDefault{
		get{ return PlayerPrefs.GetInt(VisiblePref, 1) == 1; }
		set{ PlayerPrefs.SetInt(VisiblePref, value ? 1 : 0); PlayerPrefs.Save(); }
	}

	/// <summary>The window for the current scene, created on first use.</summary>
	public static LLMThoughtsWindow Instance{
		get{
			if(instance == null){
				instance = new GameObject("LLM Thoughts Window").AddComponent<LLMThoughtsWindow>();
			}
			return instance;
		}
	}

	private void Awake(){
		rect = new Rect(PlayerPrefs.GetFloat("LLM_PanelX", 20), PlayerPrefs.GetFloat("LLM_PanelY", 60),
			PlayerPrefs.GetFloat("LLM_PanelW", 420), PlayerPrefs.GetFloat("LLM_PanelH", 360));
		fontSize = PlayerPrefs.GetInt("LLM_PanelFont", 13);
		visible = ShowByDefault;
	}

	private void Update(){
		if(Input.GetKeyDown(KeyCode.F8)){
			visible = !visible;
			ShowByDefault = visible;
		}
	}

	// ---- Content ----

	public void BeginTurn(string header){
		AppendRaw("\n<b><color=#7FB2FF>== " + Escape(header) + " ==</color></b>\n<color=#9A9A9A><i>Thinking...</i></color>\n");
		status = "Planning...";
	}

	public void AppendThinking(string delta){
		AppendRaw("<color=#B9B9B9>" + Escape(delta) + "</color>");
	}

	public void WritingPlan(){
		if(status != "Writing plan..."){
			AppendRaw("\n<color=#9A9A9A><i>Writing plan...</i></color>\n");
			status = "Writing plan...";
		}
	}

	public void ShowPlan(TurnPlan plan, string footer){
		StringBuilder sb = new StringBuilder("\n<b><color=#8BE08B>Plan</color></b>: " + Escape(plan.summary) + "\n");
		foreach(string c in plan.considerations){
			sb.Append("  - ").Append(Escape(c)).Append('\n');
		}
		sb.Append("  Clock: ").Append(plan.should_clock ? (plan.clock_card.Length > 0 ? Escape(plan.clock_card) : "yes") : "no").Append('\n');
		AppendList(sb, "Hold", plan.hold_cards);
		AppendList(sb, "Discard first", plan.discard_priority);
		AppendList(sb, "Search for", plan.search_priority);
		sb.Append("  Counters: ").Append(plan.counter_policy);
		if(plan.protect_cards.Count > 0){
			sb.Append(" (protect ").Append(Escape(string.Join(", ", plan.protect_cards))).Append(')');
		}
		sb.Append(", keep ").Append(plan.stock_to_reserve).Append(" stock\n");
		AppendList(sb, "Don't encore", plan.skip_encore);
		if(footer != null){
			sb.Append("<color=#9A9A9A>").Append(Escape(footer)).Append("</color>\n");
		}
		AppendRaw(sb.ToString());
		status = "Plan ready";
	}

	public void ShowError(string message){
		AppendRaw("\n<color=#FF8A80>" + Escape(message) + " Using the regular AI this turn.</color>\n");
		status = "Fell back to AIVersion6";
	}

	public void AddDecision(string line){
		AppendRaw("<color=#E6C46B>> " + Escape(line) + "</color>\n");
	}

	private static void AppendList(StringBuilder sb, string label, System.Collections.Generic.List<string> items){
		if(items.Count > 0){
			sb.Append("  ").Append(label).Append(": ").Append(Escape(string.Join(", ", items))).Append('\n');
		}
	}

	private void AppendRaw(string text){
		content.Append(text);
		if(content.Length > MaxChars){
			// Cut at a line break so a rich-text tag isn't split.
			int cut = content.ToString().IndexOf('\n', content.Length - MaxChars);
			content.Remove(0, cut < 0 ? content.Length - MaxChars : cut + 1);
		}
		if(autoScroll){
			scroll.y = float.MaxValue;
		}
	}

	/// <summary>Stops card text or model output from being read as rich-text tags.</summary>
	private static string Escape(string s){
		return s == null ? "" : s.Replace("<", "(").Replace(">", ")");
	}

	// ---- Drawing ----

	private void OnGUI(){
		if(visible == false){
			return;
		}
		if(textStyle == null || textStyle.fontSize != fontSize){
			textStyle = new GUIStyle(GUI.skin.label){ richText = true, wordWrap = true, fontSize = fontSize };
			textStyle.normal.textColor = Color.white;
		}

		Rect drawRect = minimized ? new Rect(rect.x, rect.y, rect.width, 44) : rect;
		Rect moved = GUI.Window(WindowId, drawRect, DrawWindow, "Claude AI - " + status);
		if(moved.position != drawRect.position){
			rect.position = moved.position;
			SaveLayout();
		}

		// Resizing is handled outside the window so the drag keeps working when the mouse leaves it.
		Event e = Event.current;
		if(resizing){
			if(e.type == EventType.MouseDrag){
				rect.width = Mathf.Max(240, e.mousePosition.x - rect.x);
				rect.height = Mathf.Max(120, e.mousePosition.y - rect.y);
				e.Use();
			}else if(e.type == EventType.MouseUp){
				resizing = false;
				SaveLayout();
				e.Use();
			}
		}
	}

	private void DrawWindow(int id){
		GUILayout.BeginHorizontal();
		if(GUILayout.Button(minimized ? "+" : "-", GUILayout.Width(24))){
			minimized = !minimized;
		}
		if(minimized == false){
			if(GUILayout.Button("A-", GUILayout.Width(30))){
				fontSize = Mathf.Max(8, fontSize - 1);
				SaveLayout();
			}
			if(GUILayout.Button("A+", GUILayout.Width(30))){
				fontSize = Mathf.Min(32, fontSize + 1);
				SaveLayout();
			}
			autoScroll = GUILayout.Toggle(autoScroll, "Follow", GUILayout.Width(64));
			if(GUILayout.Button("Clear", GUILayout.Width(50))){
				content.Clear();
			}
		}
		GUILayout.FlexibleSpace();
		if(GUILayout.Button("Hide (F8)", GUILayout.Width(76))){
			visible = false;
			ShowByDefault = false;
		}
		GUILayout.EndHorizontal();

		if(minimized == false){
			scroll = GUILayout.BeginScrollView(scroll);
			GUILayout.Label(content.Length > 0 ? content.ToString() : "<color=#9A9A9A>Nothing yet. The AI plans at the start of each of its turns.</color>", textStyle);
			GUILayout.EndScrollView();

			Rect handle = new Rect(rect.width - HandleSize, rect.height - HandleSize, HandleSize, HandleSize);
			GUI.Label(handle, "//");
			Event e = Event.current;
			if(e.type == EventType.MouseDown && handle.Contains(e.mousePosition)){
				resizing = true;
				e.Use();
			}
		}
		GUI.DragWindow(new Rect(0, 0, 10000, 20));
	}

	private void SaveLayout(){
		PlayerPrefs.SetFloat("LLM_PanelX", rect.x);
		PlayerPrefs.SetFloat("LLM_PanelY", rect.y);
		PlayerPrefs.SetFloat("LLM_PanelW", rect.width);
		PlayerPrefs.SetFloat("LLM_PanelH", rect.height);
		PlayerPrefs.SetInt("LLM_PanelFont", fontSize);
		PlayerPrefs.Save();
	}
}
