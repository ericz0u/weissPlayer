using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Adds the Claude AI settings (API key, model, effort, thinking window) to the Options screen, built at runtime
/// from copies of the screen's own labels and buttons so it matches the existing style without editing the scene.
/// Like the rest of the screen, a choice that is currently active shows as a greyed-out button.
/// </summary>
public static class LLMSettingsUI
{
	private static readonly string[] ModelLabels = { "Opus 5.5", "Sonnet 5.5" };

	// Layout state, measured from the existing screen in Build so the section scales with it.
	private static float k;// pixels per unit of a 640-wide two-column layout
	private static float buttonHeight;
	private static float textSize;

	public static void Build(OptionsScreen screen){
		Button templateButton = screen.unlitShaderOnButton;
		RectTransform parent = (RectTransform)templateButton.transform.parent;
		TextMeshProUGUI[] labels = parent.GetComponentsInChildren<TextMeshProUGUI>(true);
		TextMeshProUGUI titleTemplate = labels.FirstOrDefault(t => t.text.StartsWith("Use Unlit Shaders"));
		TextMeshProUGUI noteTemplate = labels.FirstOrDefault(t => t.text.StartsWith("Removes glare"));
		if(titleTemplate == null || noteTemplate == null){
			Debug.LogWarning("LLMSettingsUI: Options screen layout changed, Claude settings not added.");
			return;
		}

		// Each existing setting sits in a column as wide as its title label; the screen is two columns.
		float width = titleTemplate.rectTransform.sizeDelta.x * 2;
		k = width / 640;
		buttonHeight = ((RectTransform)templateButton.transform).sizeDelta.y;
		textSize = noteTemplate.fontSize + 1;
		float gap = 8 * k;

		// Start below the lowest existing text, measuring wrapped text rather than its rect, which it can overflow.
		float y = 0;
		foreach(TextMeshProUGUI t in labels){
			if(t.GetComponentInParent<Button>() != null){
				continue;
			}
			RectTransform r = t.rectTransform;
			float height = Mathf.Max(r.sizeDelta.y * 0.6f, t.GetPreferredValues(t.text, r.sizeDelta.x, 0).y);
			y = Mathf.Min(y, r.anchoredPosition.y - height);
		}
		y -= 22 * k;

		// Thin divider separating the Claude section from the simulator's own settings.
		Image divider = new GameObject("LLM Divider", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
		divider.rectTransform.SetParent(parent, false);
		Place(divider.rectTransform, 20 * k, y, width - 40 * k, 1);
		divider.color = new Color(0, 0, 0, 0.2f);
		divider.raycastTarget = false;
		y -= 14 * k;

		Label(titleTemplate, parent, "Claude AI (for LLM opponent decks)", 0, y, width);
		y -= 24 * k;
		Label(noteTemplate, parent, "Used by AI decks set to Version 8, like AI_ShionAqua_LLM. Your key is saved on this computer only.", 0, y, width);
		y -= 26 * k;

		// API key row: [ key field ][Save][Test][Remove], centred
		float fieldWidth = 330 * k;
		float smallButton = 70 * k;
		float x = (width - (fieldWidth + 3 * (gap + smallButton))) / 2;
		TMP_InputField keyField = KeyField(parent, templateButton, titleTemplate.font, x, y, fieldWidth);
		x += fieldWidth + gap;
		Button save = MakeButton(templateButton, parent, "Save", x, y, smallButton);
		x += smallButton + gap;
		Button test = MakeButton(templateButton, parent, "Test", x, y, smallButton);
		x += smallButton + gap;
		Button remove = MakeButton(templateButton, parent, "Remove", x, y, smallButton);
		y -= buttonHeight + 4 * k;
		TextMeshProUGUI status = Label(noteTemplate, parent, "", 0, y, width);
		y -= 28 * k;

		// Model (left column) and effort (right column)
		Label(noteTemplate, parent, "Model", 0, y, width / 2);
		Label(noteTemplate, parent, "Thinking effort (higher is slower and costs more)", width / 2, y, width / 2);
		y -= 18 * k;
		Button[] modelButtons = ButtonRow(templateButton, parent, ModelLabels, 0, y, 100 * k, width / 2, gap);
		Button[] effortButtons = ButtonRow(templateButton, parent, ClaudePlanner.Efforts, width / 2, y, 56 * k, width / 2, gap);
		y -= buttonHeight + 16 * k;

		Label(noteTemplate, parent, "Show the AI's thinking window during games (F8 also toggles it in game)", 0, y, width);
		y -= 18 * k;
		Button[] showButtons = ButtonRow(templateButton, parent, new[]{ "turn on", "turn off" }, 0, y, 60 * k, width, gap);
		y -= buttonHeight + 24 * k;

		// Move Main Menu below the new section; it keeps its x, which centres it on the screen.
		RectTransform mainMenu = parent.GetComponentsInChildren<Button>(true)
			.Select(b => (RectTransform)b.transform)
			.FirstOrDefault(rt => rt.name == "MainMenu");
		if(mainMenu != null){
			mainMenu.anchoredPosition = new Vector2(mainMenu.anchoredPosition.x, y);
		}

		// ---- Behaviour ----
		bool testing = false;
		string message = "";
		UnityAction refresh = () => {
			bool hasTyped = keyField.text.Trim().Length > 0;
			bool hasKey = string.IsNullOrEmpty(ClaudePlanner.GetApiKey()) == false;
			save.interactable = hasTyped;
			test.interactable = (hasTyped || hasKey) && testing == false;
			remove.interactable = ClaudePlanner.HasSavedKey();
			test.GetComponentInChildren<TextMeshProUGUI>().text = testing ? "Testing..." : "Test";
			status.text = "API key: " + KeyStatus() + (message.Length > 0 ? "   -   " + message : "");
			int model = System.Array.IndexOf(ClaudePlanner.Models, ClaudePlanner.model);
			for(int i = 0; i < modelButtons.Length; i++){
				modelButtons[i].interactable = i != model;
			}
			int effort = System.Array.IndexOf(ClaudePlanner.Efforts, ClaudePlanner.effort);
			for(int i = 0; i < effortButtons.Length; i++){
				effortButtons[i].interactable = i != effort;
			}
			showButtons[0].interactable = LLMThoughtsWindow.ShowByDefault == false;
			showButtons[1].interactable = LLMThoughtsWindow.ShowByDefault;
		};

		UnityAction saveKey = () => {
			string key = CleanKey(keyField.text);
			if(key.Length == 0){
				return;
			}
			ClaudePlanner.SaveApiKey(key);
			keyField.text = "";
			message = key.StartsWith("sk-ant-") ? "Saved. Press Test to check it." : "Saved, but Anthropic keys usually start with sk-ant-.";
			refresh();
		};
		save.onClick.AddListener(saveKey);
		keyField.onSubmit.AddListener(_ => saveKey());
		keyField.onValueChanged.AddListener(_ => refresh());

		test.onClick.AddListener(() => {
			// Test what's typed if there is something, otherwise the saved key.
			string key = keyField.text.Trim().Length > 0 ? CleanKey(keyField.text) : ClaudePlanner.GetApiKey();
			testing = true;
			message = "";
			refresh();
			screen.StartCoroutine(ClaudePlanner.TestApiKey(key, (ok, result) => {
				testing = false;
				message = result;
				refresh();
			}));
		});
		remove.onClick.AddListener(() => {
			ClaudePlanner.SaveApiKey("");
			message = "Saved key removed.";
			refresh();
		});
		for(int i = 0; i < modelButtons.Length; i++){
			int index = i;
			modelButtons[i].onClick.AddListener(() => { ClaudePlanner.model = ClaudePlanner.Models[index]; refresh(); });
		}
		for(int i = 0; i < effortButtons.Length; i++){
			int index = i;
			effortButtons[i].onClick.AddListener(() => { ClaudePlanner.effort = ClaudePlanner.Efforts[index]; refresh(); });
		}
		showButtons[0].onClick.AddListener(() => { LLMThoughtsWindow.ShowByDefault = true; refresh(); });
		showButtons[1].onClick.AddListener(() => { LLMThoughtsWindow.ShowByDefault = false; refresh(); });

		refresh();
	}

	/// <summary>Pasted keys often pick up spaces, line breaks or quotes; none are valid in a key.</summary>
	private static string CleanKey(string s){
		return new string(s.Where(c => char.IsWhiteSpace(c) == false && c != '"' && c != '\'').ToArray());
	}

	private static string KeyStatus(){
		if(ClaudePlanner.HasSavedKey()){
			string key = ClaudePlanner.GetApiKey();
			return "saved (ends in " + key.Substring(Mathf.Max(0, key.Length - 4)) + ")";
		}
		if(string.IsNullOrEmpty(ClaudePlanner.GetApiKey()) == false){
			return "from the ANTHROPIC_API_KEY variable or key file";
		}
		return "not set";
	}

	// ---- Building blocks ----

	private static void Place(RectTransform rt, float x, float y, float width, float height){
		rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
		rt.pivot = new Vector2(0, 1);
		rt.anchoredPosition = new Vector2(x, y);
		rt.sizeDelta = new Vector2(width, height);
	}

	private static TextMeshProUGUI Label(TextMeshProUGUI template, RectTransform parent, string text, float x, float y, float width){
		TextMeshProUGUI label = Object.Instantiate(template, parent);
		label.name = "LLM " + (text.Length > 20 ? text.Substring(0, 20) : text);
		label.text = text;
		label.enableAutoSizing = false;
		label.fontSize = template.fontSize;
		label.enableWordWrapping = true;
		Place(label.rectTransform, x, y, width, template.fontSize * 1.5f);
		return label;
	}

	private static Button MakeButton(Button template, RectTransform parent, string text, float x, float y, float width){
		Button button = Object.Instantiate(template, parent);
		button.name = "LLM " + text;
		// The copy keeps the template's click handlers from the scene, so replace them.
		button.onClick = new Button.ButtonClickedEvent();
		button.interactable = true;
		Place((RectTransform)button.transform, x, y, width, buttonHeight);
		TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>();
		label.text = text;
		label.enableAutoSizing = false;
		label.fontSize = textSize;
		label.enableWordWrapping = false;
		label.alignment = TextAlignmentOptions.Center;
		RectTransform labelRect = label.rectTransform;
		labelRect.anchorMin = Vector2.zero;
		labelRect.anchorMax = Vector2.one;
		labelRect.pivot = new Vector2(0.5f, 0.5f);
		labelRect.anchoredPosition = Vector2.zero;
		labelRect.sizeDelta = Vector2.zero;
		return button;
	}

	/// <summary>Buttons side by side, centred in the area that starts at x.</summary>
	private static Button[] ButtonRow(Button template, RectTransform parent, string[] texts, float x, float y, float buttonWidth, float areaWidth, float gap){
		float rowWidth = texts.Length * buttonWidth + (texts.Length - 1) * gap;
		float start = x + (areaWidth - rowWidth) / 2;
		Button[] buttons = new Button[texts.Length];
		for(int i = 0; i < texts.Length; i++){
			buttons[i] = MakeButton(template, parent, texts[i], start + i * (buttonWidth + gap), y, buttonWidth);
		}
		return buttons;
	}

	/// <summary>
	/// A single-line password field. The text scrolls inside the field, so a long pasted key can't change the layout.
	/// </summary>
	private static TMP_InputField KeyField(RectTransform parent, Button styleFrom, TMP_FontAsset font, float x, float y, float width){
		// Built inactive so TMP_InputField's OnEnable sees its text components and creates the caret.
		GameObject go = new GameObject("LLM API Key Field", typeof(RectTransform), typeof(Image));
		go.SetActive(false);
		RectTransform rt = (RectTransform)go.transform;
		rt.SetParent(parent, false);
		Place(rt, x, y, width, buttonHeight);
		Image background = go.GetComponent<Image>();
		Image buttonImage = styleFrom.GetComponent<Image>();
		background.sprite = buttonImage.sprite;
		background.type = buttonImage.type;
		background.color = Color.white;

		RectTransform area = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
		area.SetParent(rt, false);
		area.anchorMin = Vector2.zero;
		area.anchorMax = Vector2.one;
		area.offsetMin = new Vector2(8 * k, 2);
		area.offsetMax = new Vector2(-8 * k, -2);

		TextMeshProUGUI placeholder = FieldText(area, "Placeholder", font);
		placeholder.text = "Paste your API key here (sk-ant-...)";
		placeholder.fontStyle = FontStyles.Italic;
		placeholder.color = new Color(0.5f, 0.5f, 0.5f, 1);
		TextMeshProUGUI text = FieldText(area, "Text", font);
		text.color = new Color(0.196f, 0.196f, 0.196f, 1);

		TMP_InputField field = go.AddComponent<TMP_InputField>();
		field.textViewport = area;
		field.textComponent = text;
		field.placeholder = placeholder;
		field.fontAsset = font;
		field.pointSize = textSize;
		field.targetGraphic = background;
		field.lineType = TMP_InputField.LineType.SingleLine;
		field.contentType = TMP_InputField.ContentType.Password;
		field.characterLimit = 0;
		field.onFocusSelectAll = true;
		go.SetActive(true);
		return field;
	}

	private static TextMeshProUGUI FieldText(RectTransform area, string name, TMP_FontAsset font){
		TextMeshProUGUI t = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
		t.rectTransform.SetParent(area, false);
		t.rectTransform.anchorMin = Vector2.zero;
		t.rectTransform.anchorMax = Vector2.one;
		t.rectTransform.offsetMin = Vector2.zero;
		t.rectTransform.offsetMax = Vector2.zero;
		t.font = font;
		t.fontSize = textSize;
		t.enableWordWrapping = false;
		t.overflowMode = TextOverflowModes.Overflow;
		t.alignment = TextAlignmentOptions.MidlineLeft;
		t.extraPadding = true;
		return t;
	}
}
