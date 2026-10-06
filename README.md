# weissPlayer

An LLM-planned AI opponent for the Weiss Schwarz Simulator (Unity 2020.3.48f1).

This repository is a mod: it contains only the new AI code and a small patch for the simulator's own files. It does not include the simulator itself. You need your own copy of the simulator's Unity project.

## What it does

At the start of each AI turn (after the draw phase, before clocking), Claude reads the board and the deck list and writes a gameplan for the turn. The plan steers the smaller decisions until the AI's next turn:

- whether to clock, and which card
- which cards to hold and which to discard first
- salvage and search targets
- counter policy, and which characters not to encore

Main phase and attacks still use the simulator's scripted AI (AIVersion6), which also takes over any decision the plan doesn't cover, or the whole turn if the API call fails.

During a game, a window shows the AI's thinking as it streams in, its plan, each decision and the cost per turn.

## Install

From the root of your simulator project (the folder containing `Assets/`):

1. Copy this repository's `Assets` folder over the project's `Assets` folder. It only adds new files.
2. Apply the patch to the simulator's files:
   ```bash
   patch -p1 < path/to/weissPlayer/patches/engine-hooks.patch
   ```
   Or `git apply path/to/weissPlayer/patches/engine-hooks.patch` if your project is in git. The patch changes five files:
   - `Assets/Scripts/AI/AI.cs` adds an `OnTurnStartRoutine()` hook that can wait for a slow call
   - `Assets/Scripts/GameManager.cs` runs that hook between the draw and clock phases
   - `Assets/Scripts/AI/AIManager.cs` registers AI `Version 8`
   - `Assets/Scripts/OptionsScreen.cs` adds the settings box to the Options screen
   - `Assets/StreamingAssets/AIData/AIList.txt` adds the `AI_ShionAqua_LLM` deck

   If the patch doesn't apply, your simulator version differs from the one this was made for; the changes are small enough to make by hand from the patch file.

## Run

1. Open the project in Unity Hub with Unity **2020.3.48f1**.
2. Open `Assets/Scenes/MainMenu.unity` and press Play.
3. Go to **Options** and paste your Anthropic API key into the **Claude AI settings** box, then press **Test key**. You can pick the model (Opus 5.5 or Sonnet 5.5) and thinking effort there too.
4. Back on the main menu choose **VS Computer** and pick **AI_ShionAqua_LLM** as the AI deck.

The thinking window can be dragged, resized from its bottom-right corner, and shown or hidden with **F8**. It shows the AI's hand, so hide it if you want a fair game. Per-game logs go to `~/Library/Application Support/DefaultCompany/Weiss Schwarz/LLMAgentLogs/` on macOS.

Expect roughly $0.40-$1.00 per game on Opus 5.5 at medium effort, or about half that on Sonnet 5.5.

To give another deck the LLM AI, copy its deck file in `Assets/StreamingAssets/DefaultDecks/` under a new name and add an entry with `Version 8` to `Assets/StreamingAssets/AIData/AIList.txt`, pointing `File` at that deck's existing AI file.

## Developing

This repo is meant to live inside a simulator project; `.gitignore` ignores everything except the mod's files. After editing any of the patched simulator files, regenerate the patch:

```bash
bash patches/update-patch.sh
```

To start changing another simulator file, run `bash patches/update-patch.sh --add <path>` before editing it, then run the script again afterwards.
