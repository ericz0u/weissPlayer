using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Adds the Claude AI settings (API key, model, effort, thinking window) to the Options screen, built at runtime
/// from copies of the screen's own labels and buttons so it matches the existing style without editing the scene.
/// The screen positions everything as fractions of the screen size (UIObjectScalerType2) and scales text with the
/// screen height (ScaleTextSizeWithScreenHeight); the new elements use the same components so they scale the same way.
/// Like the rest of the screen, the active choice shows as a greyed-out button.
/// </summary>
public static class LLMSettingsUI
{
	private static readonly string[] ModelLabels = { "Opus 5.5", "Sonnet 5.5" };

	// Layout, in fractions of the screen. The section fills the empty right column under "Use Unlit Shaders",
	// level with "Custom Sleeve Images Visible to Opponent" on the left.
	private const float Left = 0.5f;
	private const float ColumnWidth = 0.5f;
	private const float RowHeight = 0.05f;
	private const float LabelWidth = 0.11f;// row labels like "Model" sit to the left of their buttons
	private const float ButtonsLeft = Left + LabelWidth + 0.01f;

	public static void Build(OptionsScreen screen){
		Button templateButton = screen.unlitShaderOnButton;
		RectTransform parent = (RectTransform)templateButton.transform.parent;
		TextMeshProUGUI[] labels = parent.GetComponentsInChildren<TextMeshProUGUI>(true);
		TextMeshProUGUI titleTemplate = labels.FirstOrDefault(t => t.text.StartsWith("Use Unlit Shaders"));
		TextMeshProUGUI noteTemplate = labels.FirstOrDefault(t => t.text.StartsWith("Removes glare"));
		if(titleTemplate == null || noteTemplate == null || templateButton.GetComponent<UIObjectScalerType2>() == null){
			Debug.LogWarning("LLMSettingsUI: Options screen layout changed, Claude settings not added.");
			return;
		}

		Divider(parent, Left + 0.05f, 0.465f, ColumnWidth - 0.1f);
		Label(titleTemplate, parent, "Claude AI (LLM opponent decks)", Left, 0.49f, ColumnWidth, 0.06f, 20);
		Label(noteTemplate, parent, "Used by AI decks set to Version 8, like AI_ShionAqua_LLM.\nYour API key is saved on this computer only.", Left, 0.54f, ColumnWidth, 0.07f, 13);

		// API key row: [ key field ][Save][Test][Remove], with the optional workspace ID field under the key field
		float y = 0.60f;
		TMP_InputField keyField = TextField(parent, templateButton, titleTemplate.font, Left + 0.02f, y, 0.22f,
			"Paste your API key (sk-ant-...)", true);
		Button save = MakeButton(templateButton, parent, "Save", 0.75f, y, 0.06f);
		Button test = MakeButton(templateButton, parent, "Test", 0.82f, y, 0.06f);
		Button remove = MakeButton(templateButton, parent, "Remove", 0.89f, y, 0.06f);
		y += RowHeight + 0.005f;
		TMP_InputField workspaceField = TextField(parent, templateButton, titleTemplate.font, Left + 0.02f, y, 0.22f,
			"Workspace ID, if needed", false);
		y += RowHeight + 0.005f;
		TextMeshProUGUI status = Label(noteTemplate, parent, "", Left, y, ColumnWidth, 0.04f, 12);
		status.overflowMode = TextOverflowModes.Ellipsis;

		y = 0.75f;
		RowLabel(noteTemplate, parent, "Model", y);
		Button[] modelButtons = ButtonRow(templateButton, parent, ModelLabels, y, 0.09f);
		y += RowHeight + 0.005f;
		RowLabel(noteTemplate, parent, "Thinking effort", y);
		Button[] effortButtons = ButtonRow(templateButton, parent, ClaudePlanner.Efforts, y, 0.06f);
		y += RowHeight + 0.005f;
		RowLabel(noteTemplate, parent, "Thinking window", y);
		Button[] showButtons = ButtonRow(templateButton, parent, new[]{ "turn on", "turn off" }, y, 0.07f);

		// Main Menu sat where the new rows go; move it to the bottom centre.
		UIObjectScalerType2 mainMenu = parent.GetComponentsInChildren<UIObjectScalerType2>(true).FirstOrDefault(s => s.name == "MainMenu");
		if(mainMenu != null){
			mainMenu.topLeftCorner = new Vector2(mainMenu.topLeftCorner.x, 0.93f);
		}

		// ---- Behaviour ----
		bool testing = false;
		string message = "";
		UnityAction refresh = () => {
			bool hasTyped = keyField.text.Trim().Length > 0;
			bool hasKey = string.IsNullOrEmpty(ClaudePlanner.GetApiKey()) == false;
			save.interactable = hasTyped || workspaceField.text.Trim().Length > 0;
			test.interactable = (hasTyped || hasKey) && testing == false;
			remove.interactable = ClaudePlanner.HasSavedKey() || ClaudePlanner.GetWorkspaceId().Length > 0;
			test.GetComponentInChildren<TextMeshProUGUI>().text = testing ? "Testing..." : "Test";
			status.text = message.Length > 0 ? message : "API key: " + KeyStatus();
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
			string workspace = CleanKey(workspaceField.text);
			if(key.Length == 0 && workspace.Length == 0){
				return;
			}
			message = "";
			if(key.Length > 0){
				ClaudePlanner.SaveApiKey(key);
				keyField.text = "";
				message = key.StartsWith("sk-ant-") ? "Key saved (ends in " + key.Substring(key.Length - 4) + "). "
					: "Saved, but Anthropic keys usually start with sk-ant-. ";
			}
			if(workspace.Length > 0){
				ClaudePlanner.SaveWorkspaceId(workspace);
				workspaceField.text = "";
				message += "Workspace saved. ";
			}
			message += "Press Test to check it.";
			refresh();
		};
		save.onClick.AddListener(saveKey);
		keyField.onSubmit.AddListener(_ => saveKey());
		workspaceField.onSubmit.AddListener(_ => saveKey());
		keyField.onValueChanged.AddListener(_ => refresh());
		workspaceField.onValueChanged.AddListener(_ => refresh());

		test.onClick.AddListener(() => {
			// Test what's typed if there is something, otherwise the saved key.
			string key = keyField.text.Trim().Length > 0 ? CleanKey(keyField.text) : ClaudePlanner.GetApiKey();
			string workspace = workspaceField.text.Trim().Length > 0 ? CleanKey(workspaceField.text) : ClaudePlanner.GetWorkspaceId();
			testing = true;
			message = "";
			refresh();
			screen.StartCoroutine(ClaudePlanner.TestApiKey(key, workspace, (ok, result) => {
				testing = false;
				message = result;
				refresh();
			}));
		});
		remove.onClick.AddListener(() => {
			ClaudePlanner.SaveApiKey("");
			ClaudePlanner.SaveWorkspaceId("");
			message = "Saved key and workspace ID removed.";
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
		string workspace = ClaudePlanner.GetWorkspaceId();
		string workspaceText = workspace.Length > 0 ? ", workspace " + workspace : "";
		if(ClaudePlanner.HasSavedKey()){
			string key = ClaudePlanner.GetApiKey();
			return "saved (ends in " + key.Substring(Mathf.Max(0, key.Length - 4)) + ")" + workspaceText;
		}
		if(string.IsNullOrEmpty(ClaudePlanner.GetApiKey()) == false){
			return "from the ANTHROPIC_API_KEY variable or key file" + workspaceText;
		}
		return "not set";
	}

	// ---- Building blocks ----

	/// <summary>Positions an element in screen fractions, the same way the rest of the Options screen does.</summary>
	private static void Place(GameObject go, float x, float y, float width, float height){
		UIObjectScalerType2 scaler = go.GetComponent<UIObjectScalerType2>();
		if(scaler == null){
			scaler = go.AddComponent<UIObjectScalerType2>();
		}
		scaler.topLeftCorner = new Vector2(x, y);
		scaler.size = new Vector2(width, height);
		scaler.keepHeightRatio = false;
	}

	/// <summary>Text sized like the screen's own: baseSize at a 600px tall screen, scaled with the screen height.</summary>
	private static void ScaleText(GameObject go, float baseSize){
		ScaleTextSizeWithScreenHeight scaler = go.GetComponent<ScaleTextSizeWithScreenHeight>();
		if(scaler == null){
			scaler = go.AddComponent<ScaleTextSizeWithScreenHeight>();
		}
		scaler.baseSize = baseSize;
	}

	private static TextMeshProUGUI Label(TextMeshProUGUI template, RectTransform parent, string text, float x, float y, float width, float height, float baseSize){
		TextMeshProUGUI label = Object.Instantiate(template, parent);
		label.name = "LLM " + (text.Length > 20 ? text.Substring(0, 20) : text);
		label.text = text;
		label.enableAutoSizing = false;
		Place(label.gameObject, x, y, width, height);
		ScaleText(label.gameObject, baseSize);
		return label;
	}

	private static void RowLabel(TextMeshProUGUI template, RectTransform parent, string text, float y){
		TextMeshProUGUI label = Label(template, parent, text, Left, y, LabelWidth, RowHeight, 13);
		label.alignment = TextAlignmentOptions.MidlineRight;
		label.enableWordWrapping = false;
	}

	private static void Divider(RectTransform parent, float x, float y, float width){
		GameObject go = new GameObject("LLM Divider", typeof(RectTransform), typeof(Image));
		go.transform.SetParent(parent, false);
		Image image = go.GetComponent<Image>();
		image.color = new Color(0, 0, 0, 0.2f);
		image.raycastTarget = false;
		Place(go, x, y, width, 0.0015f);
	}

	private static Button MakeButton(Button template, RectTransform parent, string text, float x, float y, float width){
		Button button = Object.Instantiate(template, parent);
		button.name = "LLM " + text;
		// The copy keeps the template's click handlers from the scene, so replace them.
		button.onClick = new Button.ButtonClickedEvent();
		button.interactable = true;
		Place(button.gameObject, x, y, width, RowHeight);
		// The label keeps the template's UIObjectScaler, which fits it inside the button.
		TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>();
		label.text = text;
		// The scene's button labels auto-size to fill the button, which makes short words like "low" larger than
		// longer ones. Cap them at one screen-scaled size so a row of buttons reads evenly.
		label.enableAutoSizing = true;
		label.fontSizeMin = 6;
		label.gameObject.AddComponent<CappedButtonText>();
		return button;
	}

	/// <summary>Buttons side by side, starting right of the row label.</summary>
	private static Button[] ButtonRow(Button template, RectTransform parent, string[] texts, float y, float buttonWidth){
		Button[] buttons = new Button[texts.Length];
		for(int i = 0; i < texts.Length; i++){
			buttons[i] = MakeButton(template, parent, texts[i], ButtonsLeft + i * (buttonWidth + 0.01f), y, buttonWidth);
		}
		return buttons;
	}

	/// <summary>
	/// A single-line text field. The text scrolls inside the field, so a long pasted key can't change the layout.
	/// </summary>
	private static TMP_InputField TextField(RectTransform parent, Button styleFrom, TMP_FontAsset font, float x, float y, float width,
		string placeholderText, bool password){
		// Built inactive so TMP_InputField's OnEnable sees its text components and creates the caret.
		GameObject go = new GameObject("LLM " + (password ? "API Key" : "Workspace") + " Field", typeof(RectTransform), typeof(Image));
		go.SetActive(false);
		go.transform.SetParent(parent, false);
		Image background = go.GetComponent<Image>();
		Image buttonImage = styleFrom.GetComponent<Image>();
		background.sprite = buttonImage.sprite;
		background.type = buttonImage.type;
		background.color = Color.white;

		RectTransform area = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
		area.SetParent(go.transform, false);
		area.anchorMin = Vector2.zero;
		area.anchorMax = Vector2.one;
		area.offsetMin = new Vector2(10, 2);
		area.offsetMax = new Vector2(-10, -2);

		TextMeshProUGUI placeholder = FieldText(area, "Placeholder", font);
		placeholder.text = placeholderText;
		placeholder.fontStyle = FontStyles.Italic;
		placeholder.color = new Color(0.5f, 0.5f, 0.5f, 1);
		TextMeshProUGUI text = FieldText(area, "Text", font);
		text.color = new Color(0.196f, 0.196f, 0.196f, 1);

		TMP_InputField field = go.AddComponent<TMP_InputField>();
		field.textViewport = area;
		field.textComponent = text;
		field.placeholder = placeholder;
		field.fontAsset = font;
		field.targetGraphic = background;
		field.lineType = TMP_InputField.LineType.SingleLine;
		field.contentType = password ? TMP_InputField.ContentType.Password : TMP_InputField.ContentType.Standard;
		field.characterLimit = 0;
		field.onFocusSelectAll = true;
		Place(go, x, y, width, RowHeight);
		ScaleText(go, 12);// sets the field's point size, which applies to the text and placeholder
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
		t.enableWordWrapping = false;
		t.overflowMode = TextOverflowModes.Overflow;
		t.alignment = TextAlignmentOptions.MidlineLeft;
		t.extraPadding = true;
		return t;
	}

	/// <summary>Keeps an auto-sizing button label at most 14pt on a 600px tall screen, scaled with the screen.</summary>
	private class CappedButtonText : MonoBehaviour
	{
		private TMP_Text text;

		private void Update(){
			if(text == null){
				text = GetComponent<TMP_Text>();
			}
			text.fontSizeMax = 14f * Screen.height / 600f;
		}
	}
}
