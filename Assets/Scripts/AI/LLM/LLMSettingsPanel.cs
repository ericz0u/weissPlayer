using UnityEngine;

/// <summary>
/// Settings box for the LLM AI (AIVersion8), added to the Options screen by OptionsScreen.
/// Drawn with IMGUI so it needs no scene changes; drag the title bar to move it.
/// </summary>
public class LLMSettingsPanel : MonoBehaviour
{
	private const int WindowId = 0x4C4C53;

	private Rect rect;
	private string keyInput = "";
	private string message = "";
	private bool testing = false;

	private void Start(){
		rect = new Rect(PlayerPrefs.GetFloat("LLM_SettingsX", 20), PlayerPrefs.GetFloat("LLM_SettingsY", Screen.height - 300), 400, 280);
		// Keep it on screen if the window size changed since it was last moved.
		rect.x = Mathf.Clamp(rect.x, 0, Mathf.Max(0, Screen.width - rect.width));
		rect.y = Mathf.Clamp(rect.y, 0, Mathf.Max(0, Screen.height - rect.height));
	}

	private void OnGUI(){
		Rect moved = GUI.Window(WindowId, rect, DrawWindow, "Claude AI settings (LLM decks)");
		if(moved.position != rect.position){
			rect.position = moved.position;
			PlayerPrefs.SetFloat("LLM_SettingsX", rect.x);
			PlayerPrefs.SetFloat("LLM_SettingsY", rect.y);
		}
	}

	private void DrawWindow(int id){
		GUILayout.Label("API key: " + KeyStatus());

		GUILayout.BeginHorizontal();
		keyInput = GUILayout.PasswordField(keyInput, '*', GUILayout.ExpandWidth(true));
		GUI.enabled = keyInput.Trim().Length > 0 && testing == false;
		if(GUILayout.Button("Save", GUILayout.Width(50))){
			ClaudePlanner.SaveApiKey(keyInput);
			keyInput = "";
			message = "Saved. Press Test to check it.";
		}
		GUI.enabled = true;
		GUILayout.EndHorizontal();

		GUILayout.BeginHorizontal();
		GUI.enabled = testing == false && string.IsNullOrEmpty(ClaudePlanner.GetApiKey()) == false;
		if(GUILayout.Button(testing ? "Testing..." : "Test key")){
			testing = true;
			message = "";
			StartCoroutine(ClaudePlanner.TestApiKey(ClaudePlanner.GetApiKey(), (ok, result) => {
				testing = false;
				message = result;
			}));
		}
		GUI.enabled = ClaudePlanner.HasSavedKey();
		if(GUILayout.Button("Remove saved key")){
			ClaudePlanner.SaveApiKey("");
			message = "Saved key removed.";
		}
		GUI.enabled = true;
		GUILayout.EndHorizontal();

		GUILayout.Space(6);
		GUILayout.Label("Model");
		int modelIndex = System.Array.IndexOf(ClaudePlanner.Models, ClaudePlanner.model);
		int newModel = GUILayout.Toolbar(Mathf.Max(0, modelIndex), new[]{ "Opus 5.5", "Sonnet 5.5 (half price)" });
		if(newModel != modelIndex){
			ClaudePlanner.model = ClaudePlanner.Models[newModel];
		}

		GUILayout.Label("Thinking effort (higher is slower and costs more)");
		int effortIndex = System.Array.IndexOf(ClaudePlanner.Efforts, ClaudePlanner.effort);
		int newEffort = GUILayout.Toolbar(Mathf.Max(0, effortIndex), ClaudePlanner.Efforts);
		if(newEffort != effortIndex){
			ClaudePlanner.effort = ClaudePlanner.Efforts[newEffort];
		}

		GUILayout.Space(6);
		bool show = GUILayout.Toggle(LLMThoughtsWindow.ShowByDefault, " Show the AI's thinking during games (F8 toggles)");
		if(show != LLMThoughtsWindow.ShowByDefault){
			LLMThoughtsWindow.ShowByDefault = show;
		}

		if(message.Length > 0){
			GUILayout.Label(message);
		}
		GUI.DragWindow(new Rect(0, 0, 10000, 20));
	}

	private static string KeyStatus(){
		if(ClaudePlanner.HasSavedKey()){
			string key = ClaudePlanner.GetApiKey();
			return "saved (ends in " + key.Substring(Mathf.Max(0, key.Length - 4)) + ")";
		}
		if(string.IsNullOrEmpty(ClaudePlanner.GetApiKey()) == false){
			return "from environment variable or key file";
		}
		return "not set";
	}
}
