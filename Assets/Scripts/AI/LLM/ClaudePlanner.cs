using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Calls the Claude Messages API over raw HTTP (the official C# SDK targets newer .NET than Unity 2020 supports).
/// Runs as a coroutine and streams the response, so the game keeps rendering and the thinking can be shown live.
/// Settings (API key, model, effort) are stored in PlayerPrefs and edited from the Options screen.
/// </summary>
public static class ClaudePlanner
{
	public static readonly string[] Models = { "claude-opus-5-5", "claude-sonnet-5-5" };
	/// <summary>Higher thinks longer per turn and costs more.</summary>
	public static readonly string[] Efforts = { "low", "medium", "high", "xhigh", "max" };
	public static int timeoutSeconds = 300;

	private const string Url = "https://api.anthropic.com/v1/messages";
	private const string KeyPref = "LLM_AnthropicApiKey";
	private const string ModelPref = "LLM_Model";
	private const string EffortPref = "LLM_Effort";
	private const string WorkspacePref = "LLM_WorkspaceId";

	public static string model{
		get{ return PlayerPrefs.GetString(ModelPref, Models[0]); }
		set{ PlayerPrefs.SetString(ModelPref, value); PlayerPrefs.Save(); }
	}

	public static string effort{
		get{ return PlayerPrefs.GetString(EffortPref, "medium"); }
		set{ PlayerPrefs.SetString(EffortPref, value); PlayerPrefs.Save(); }
	}

	[System.Serializable] public class Usage { public int input_tokens; public int output_tokens; public int cache_creation_input_tokens; public int cache_read_input_tokens; }

	// One class covers every streamed event type; JsonUtility leaves fields that aren't present at their defaults.
	[System.Serializable] private class StreamDelta { public string type; public string text; public string thinking; public string stop_reason; }
	[System.Serializable] private class StreamBlock { public string type; }
	[System.Serializable] private class StreamMessage { public Usage usage; }
	[System.Serializable] private class StreamError { public string type; public string message; }
	[System.Serializable] private class StreamEvent {
		public string type;
		public StreamDelta delta;
		public StreamBlock content_block;
		public StreamMessage message;
		public Usage usage;
		public StreamError error;
	}

	/// <summary>Token usage of the most recent request, or null.</summary>
	public static Usage lastUsage = null;

	/// <summary>
	/// Approximate cost in dollars at list prices. Thinking tokens are included in output_tokens.
	/// Opus 5.5: $4 in / $20 out per million. Sonnet 5.5: $2 / $10. Cache writes are 1.25x input, cache reads $0.20.
	/// </summary>
	public static float EstimateCost(Usage u, string forModel){
		if(u == null){
			return 0;
		}
		float input = forModel.Contains("sonnet") ? 2f : 4f;
		return (u.input_tokens * input + u.cache_creation_input_tokens * input * 1.25f + u.cache_read_input_tokens * 0.2f + u.output_tokens * input * 5f) / 1000000f;
	}

	/// <summary>
	/// Key from the Options screen first, then the ANTHROPIC_API_KEY environment variable,
	/// then anthropic_api_key.txt in Application.persistentDataPath.
	/// </summary>
	public static string GetApiKey(){
		string key = PlayerPrefs.GetString(KeyPref, "");
		if(string.IsNullOrEmpty(key)){
			key = System.Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
		}
		if(string.IsNullOrEmpty(key)){
			string path = Path.Combine(Application.persistentDataPath, "anthropic_api_key.txt");
			if(File.Exists(path)){
				key = File.ReadAllText(path).Trim();
			}
		}
		return key;
	}

	public static bool HasSavedKey(){
		return string.IsNullOrEmpty(PlayerPrefs.GetString(KeyPref, "")) == false;
	}

	/// <summary>Stores the key in PlayerPrefs (on macOS a plain-text plist in ~/Library/Preferences). Empty clears it.</summary>
	public static void SaveApiKey(string key){
		key = key == null ? "" : key.Trim();
		if(key.Length == 0){
			PlayerPrefs.DeleteKey(KeyPref);
		}else{
			PlayerPrefs.SetString(KeyPref, key);
		}
		PlayerPrefs.Save();
	}

	/// <summary>
	/// Workspace ID (wrkspc_...) for keys that aren't scoped to a workspace. The API then requires it
	/// in an anthropic-workspace-id header. Empty means the key's own workspace is used.
	/// </summary>
	public static string GetWorkspaceId(){
		return PlayerPrefs.GetString(WorkspacePref, "");
	}

	public static void SaveWorkspaceId(string id){
		id = id == null ? "" : id.Trim();
		if(id.Length == 0){
			PlayerPrefs.DeleteKey(WorkspacePref);
		}else{
			PlayerPrefs.SetString(WorkspacePref, id);
		}
		PlayerPrefs.Save();
	}

	private static void SetHeaders(UnityWebRequest req, string key, string workspaceId){
		req.SetRequestHeader("content-type", "application/json");
		req.SetRequestHeader("x-api-key", key);
		req.SetRequestHeader("anthropic-version", "2023-06-01");
		if(string.IsNullOrEmpty(workspaceId) == false){
			req.SetRequestHeader("anthropic-workspace-id", workspaceId);
		}
	}

	[System.Serializable] private class ApiError { public StreamError error; }

	/// <summary>The "message" from an API error body, or null.</summary>
	private static string ErrorMessage(string body){
		try{
			ApiError e = JsonUtility.FromJson<ApiError>(body);
			return e?.error?.message;
		}catch(System.Exception){
			return null;
		}
	}

	/// <summary>
	/// Checks a key (and workspace ID) with a token count request, which is free but goes through the same
	/// checks as a real request. done(ok, message).
	/// </summary>
	public static IEnumerator TestApiKey(string key, string workspaceId, System.Action<bool, string> done){
		string body = "{\"model\":" + Quote(model) + ",\"messages\":[{\"role\":\"user\",\"content\":\"hi\"}]}";
		using(UnityWebRequest req = new UnityWebRequest("https://api.anthropic.com/v1/messages/count_tokens", "POST")){
			req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
			req.downloadHandler = new DownloadHandlerBuffer();
			req.timeout = 20;
			SetHeaders(req, key, workspaceId);
			yield return req.SendWebRequest();
			if(req.result == UnityWebRequest.Result.Success){
				done(true, "Key works.");
			}else{
				string message = ErrorMessage(req.downloadHandler.text);
				done(false, (req.responseCode == 401 ? "Key was rejected. " : "Error " + req.responseCode + ": ") + (message ?? req.error));
			}
		}
	}

	/// <summary>
	/// Requests a TurnPlan. Calls done(plan, rawText) with plan null on any failure, so the caller can fall back to AIVersion6.
	/// onThinking and onText receive streamed text as it arrives (thinking is a summary, not the raw reasoning).
	/// </summary>
	public static IEnumerator RequestPlan(string systemPrompt, string userPrompt, System.Action<TurnPlan, string> done,
		System.Action<string> onThinking = null, System.Action<string> onText = null){
		lastUsage = null;
		string key = GetApiKey();
		if(string.IsNullOrEmpty(key)){
			Debug.LogWarning("ClaudePlanner: no API key. Add one in Options.");
			done(null, "No API key. Add one in Options.");
			yield break;
		}

		// The system prompt holds the rules primer, deck list and deck notes, which never change during a game,
		// so it is cached and later turns only pay full price for the current game state.
		string body = "{"
			+ "\"model\":" + Quote(model) + ","
			+ "\"max_tokens\":32000,"
			+ "\"stream\":true,"
			+ "\"fallbacks\":\"default\","
			+ "\"thinking\":{\"type\":\"adaptive\",\"display\":\"summarized\"},"
			+ "\"output_config\":{\"effort\":" + Quote(effort) + ",\"format\":{\"type\":\"json_schema\",\"schema\":" + TurnPlan.JsonSchema + "}},"
			+ "\"system\":[{\"type\":\"text\",\"text\":" + Quote(systemPrompt) + ",\"cache_control\":{\"type\":\"ephemeral\"}}],"
			+ "\"messages\":[{\"role\":\"user\",\"content\":" + Quote(userPrompt) + "}]"
			+ "}";

		Usage usage = new Usage();
		StringBuilder planText = new StringBuilder();
		string stopReason = null;
		string streamError = null;

		SseDownloadHandler handler = new SseDownloadHandler(data => {
			StreamEvent ev;
			try{
				ev = JsonUtility.FromJson<StreamEvent>(data);
			}catch(System.Exception){
				return;
			}
			if(ev == null){
				return;
			}
			switch(ev.type){
				case "message_start":
					if(ev.message != null && ev.message.usage != null){
						usage = ev.message.usage;
					}
				break;
				case "content_block_delta":
					if(ev.delta == null){
						break;
					}
					if(ev.delta.type == "thinking_delta" && string.IsNullOrEmpty(ev.delta.thinking) == false){
						onThinking?.Invoke(ev.delta.thinking);
					}else if(ev.delta.type == "text_delta" && ev.delta.text != null){
						planText.Append(ev.delta.text);
						onText?.Invoke(ev.delta.text);
					}
				break;
				case "message_delta":
					if(ev.delta != null && string.IsNullOrEmpty(ev.delta.stop_reason) == false){
						stopReason = ev.delta.stop_reason;
					}
					if(ev.usage != null){
						usage.output_tokens = ev.usage.output_tokens;
					}
				break;
				case "error":
					streamError = ev.error != null ? ev.error.type + ": " + ev.error.message : data;
				break;
			}
		});

		using(UnityWebRequest req = new UnityWebRequest(Url, "POST")){
			req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
			req.downloadHandler = handler;
			req.timeout = timeoutSeconds;
			SetHeaders(req, key, GetWorkspaceId());
			req.SetRequestHeader("anthropic-beta", "server-side-fallback-2026-07-01");

			yield return req.SendWebRequest();

			if(req.result != UnityWebRequest.Result.Success || streamError != null){
				// A non-200 response is plain JSON rather than a stream, so show whatever came back.
				string detail = streamError ?? (req.responseCode + ": " + (ErrorMessage(handler.RawText) ?? req.error + " " + handler.RawText));
				Debug.LogWarning("ClaudePlanner: request failed: " + detail);
				done(null, "Request failed: " + detail);
				yield break;
			}
		}

		lastUsage = usage;
		if(stopReason == "refusal" || stopReason == "max_tokens"){
			Debug.LogWarning("ClaudePlanner: unusable response, stop_reason=" + stopReason);
			done(null, "Unusable response (stop_reason " + stopReason + ")");
			yield break;
		}

		string planJson = planText.ToString();
		TurnPlan plan = null;
		try{
			plan = planJson.Length == 0 ? null : JsonUtility.FromJson<TurnPlan>(planJson);
		}catch(System.Exception e){
			Debug.LogWarning("ClaudePlanner: could not parse plan: " + e.Message);
		}
		done(plan, planJson);
	}

	/// <summary>
	/// Splits a server-sent event stream into the JSON payload of each "data:" line.
	/// Unity calls ReceiveData on the main thread, so the callback can touch game objects.
	/// </summary>
	private class SseDownloadHandler : DownloadHandlerScript
	{
		private readonly System.Action<string> onData;
		private readonly Decoder decoder = Encoding.UTF8.GetDecoder();
		private readonly StringBuilder pending = new StringBuilder();
		private readonly StringBuilder raw = new StringBuilder();

		public string RawText{ get{ return raw.ToString() + pending.ToString(); } }

		public SseDownloadHandler(System.Action<string> onData) : base(new byte[16384]){
			this.onData = onData;
		}

		protected override bool ReceiveData(byte[] data, int dataLength){
			char[] chars = new char[decoder.GetCharCount(data, 0, dataLength)];
			decoder.GetChars(data, 0, dataLength, chars, 0);
			pending.Append(chars);
			string text = pending.ToString();
			int newline;
			while((newline = text.IndexOf('\n')) >= 0){
				string line = text.Substring(0, newline).TrimEnd('\r');
				text = text.Substring(newline + 1);
				if(raw.Length < 4000){
					raw.AppendLine(line);
				}
				if(line.StartsWith("data:")){
					onData(line.Substring(5).Trim());
				}
			}
			pending.Clear();
			pending.Append(text);
			return true;
		}
	}

	public static string Quote(string s){
		if(s == null){
			return "null";
		}
		StringBuilder sb = new StringBuilder(s.Length + 2);
		sb.Append('"');
		foreach(char c in s){
			switch(c){
				case '"': sb.Append("\\\""); break;
				case '\\': sb.Append("\\\\"); break;
				case '\n': sb.Append("\\n"); break;
				case '\r': sb.Append("\\r"); break;
				case '\t': sb.Append("\\t"); break;
				default:
					if(c < 0x20){
						sb.Append("\\u").Append(((int)c).ToString("x4"));
					}else{
						sb.Append(c);
					}
				break;
			}
		}
		sb.Append('"');
		return sb.ToString();
	}
}
