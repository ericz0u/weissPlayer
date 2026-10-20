# weissPlayer

An LLM-planned AI opponent for the Weiss Schwarz Simulator (Unity 2020.3.48f1). Only up to version 6.3 cause thats what's released

This repository is a mod: it contains only the new AI code and a small patch for the simulator's own files. It does not include the simulator itself. You need your own copy of the simulator's Unity project.

## What it does

The AI works in two tiers:

1. **Strategist (Opus 5.5 by default), once per turn.** After the draw phase and before clocking, it reads the board, the deck list and the opponent's position (damage, levels, climaxes left in their deck, board threats) and writes a gameplan: posture (aggro, setup, control, defensive), how much damage to push, whether to wipe or trade with the opponent's board, what to field in each slot, what to play, how much stock to spend, and whether to play a climax.
2. **Executor (Sonnet 5.5 or Haiku 4.5), during the main phase.** It plays the main phase one action at a time. Each step it gets the plan, the board and a numbered list of every legal action from the engine (play a character to a slot, play an event, use an act ability, move a character, or end the phase) and picks one. Plays that would go over the plan's stock budget aren't offered.

The plan also decides directly, until the AI's next turn:

- whether to clock, and which card
- which climax to play, if any
- which cards to hold and which to discard first
- salvage and search targets
- counter policy, and which characters not to encore

Attacks and effect choices that come up mid-action (targets, searches triggered by a card) still use the simulator's scripted AI (AIVersion6). AIVersion6 also takes over the main phase if the executor gives two unusable answers, and the whole turn if the strategist call fails.

During a game, a window shows the AI's thinking as it streams in, its plan, each decision and the cost per turn.

## Install

From the root of your simulator project (the folder containing `Assets/`):

1. Copy this repository's `Assets` folder over the project's `Assets` folder. It only adds new files.
2. Apply the patch to the simulator's files:
   ```bash
   patch -p1 < path/to/weissPlayer/patches/engine-hooks.patch
   ```
   Or `git apply path/to/weissPlayer/patches/engine-hooks.patch` if your project is in git. The patch changes six files:
   - `Assets/Scripts/AI/AI.cs` adds an `OnTurnStartRoutine()` hook that can wait for a slow call
   - `Assets/Scripts/AI/AIVersion6.cs` moves its per-turn resets into `ResetTurnPlans()` so AIVersion8 can run the main phase itself
   - `Assets/Scripts/GameManager.cs` runs that hook between the draw and clock phases
   - `Assets/Scripts/AI/AIManager.cs` registers AI `Version 8`
   - `Assets/Scripts/OptionsScreen.cs` adds the Claude AI section to the Options screen
   - `Assets/StreamingAssets/AIData/AIList.txt` adds the `AI_ShionAqua_LLM` and `ALL` decks

   If the patch doesn't apply, your simulator version differs from the one this was made for; the changes are small enough to make by hand from the patch file.

## Run

1. Open the project in Unity Hub with Unity **2020.3.48f1**.
2. Open `Assets/Scenes/MainMenu.unity` and press Play.
3. Go to **Options**. In the **Claude AI** section below the other settings, paste your Anthropic API key and press **Save**, then **Test**. You can also pick the gameplan model (Opus 5.5 or Sonnet 5.5), its thinking effort, and the main-phase model (Sonnet 5.5, Haiku 4.5, or Off to let the scripted AI play the main phase).
4. Back on the main menu choose **VS Computer** and pick an LLM deck as the AI deck:
   - **AI_ShionAqua_LLM**: Hololive Shion / Aqua.
   - **ALL**: Assault Lily (green / red / blue <Lily>), Shenlin + Breath After Breath at level 1, Pure White Shining Lily Yuyu + Count the Important Things burn at level 3. Its strategy notes are at the top of `Assets/StreamingAssets/AIData/ALL AI.txt`.

The thinking window can be dragged, resized from its bottom-right corner, and shown or hidden with **F8**. It shows the AI's hand, so hide it if you want a fair game. Per-game logs go to `~/Library/Application Support/DefaultCompany/Weiss Schwarz/LLMAgentLogs/` on macOS.

Rough cost per game: $0.40-$0.80 for the gameplan on Opus 5.5 at medium effort, plus $0.60-$1.20 for the main phase on Sonnet 5.5 or $0.10-$0.20 on Haiku 4.5. The thinking window shows the actual cost per turn.

To give another deck the LLM AI, copy its deck file in `Assets/StreamingAssets/DefaultDecks/` under a new name and add an entry with `Version 8` to `Assets/StreamingAssets/AIData/AIList.txt`, pointing `File` at that deck's existing AI file.

## Developing

This repo is meant to live inside a simulator project; `.gitignore` ignores everything except the mod's files. After editing any of the patched simulator files, regenerate the patch:

```bash
bash patches/update-patch.sh
```

To start changing another simulator file, run `bash patches/update-patch.sh --add <path>` before editing it, then run the script again afterwards.
