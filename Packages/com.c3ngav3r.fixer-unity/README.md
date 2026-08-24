# FIXER UNITY — Console & Builder

A zero-credit local Unity Editor assistant with Console Fixer, AI Builder, Game Builder and a Smart Agent safety/playtest layer.

## Console Fixer
- Captures Editor errors in memory (does not read `Editor.log`).
- Detects `Assets/...cs(line,column)` source locations.
- Deterministic quick fixes for common ambiguous `Debug`, `CommonUsages`, and `Random` errors.
- AI Fix sends the selected error plus relevant C# source to a local Ollama coding model.
- Makes backups under `Library/FixerUnityBackups` before overwriting scripts.

## AI Builder
- Creates scenes, scripts, GameObjects and real Unity UI.
- Adds components to selected objects.
- Supports screen-space and world-space canvases.
- Blocks writes outside `Assets/` and blocks fake binary asset generation.

## Game Builder
- Syncs project scenes into Build Settings.
- Builds Android APKs.
- Builds Windows x64 executables.

## Smart Agent
Open:

`Tools > FIXER UNITY Console & Builder > SMART AGENT`

The Smart Agent is a verification layer for the existing Builder. It:

- Recursively inspects the active scene instead of assuming hierarchy paths.
- Lists real GameObjects and their components.
- Finds existing Unity UI Buttons and reports their actual persistent OnClick listener count.
- Detects missing Canvas/UI infrastructure without automatically creating replacement UI.
- Enters real Unity Play Mode for a bounded smoke test.
- Captures runtime `Error`, `Exception` and `Assert` messages.
- Produces an explicit PASS/FAIL report instead of treating "Play Mode started" as success.
- Saves open scenes before playtesting.

This version does not claim to simulate human VR input; it is a deterministic smoke/health test and should be used together with manual VR playtesting for input, locomotion and headset-specific behavior.

## Free local AI
The default model is `qwen2.5-coder:3b` through Ollama. No API key or usage credits are required.

Inside the window press **INSTALL FREE AI** to run:

`ollama pull qwen2.5-coder:3b`

Then press **TEST AI**.

Open the main tool from:

`Tools > FIXER UNITY Console & Builder`
