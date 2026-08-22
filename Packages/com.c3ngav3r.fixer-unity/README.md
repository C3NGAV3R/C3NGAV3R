# FIXER UNITY — Console & Builder

A zero-credit local Unity Editor assistant with three tools in one window.

## Console Fixer
- Captures Editor errors in memory (does not read `Editor.log`).
- Detects `Assets/...cs(line,column)` source locations.
- Deterministic quick fixes for common ambiguous `Debug`, `CommonUsages`, and `Random` errors.
- AI Fix sends the selected error plus the relevant C# source to a local Ollama coding model.
- Makes backups under `Library/FixerUnityBackups` before overwriting scripts.

## AI Builder
- Creates scenes, scripts, GameObjects and real Unity UI.
- Adds components to selected objects.
- Supports screen-space and world-space canvases.
- Blocks writes outside `Assets/` and blocks fake binary asset generation.

## Game Builder
- Sync all project scenes into Build Settings.
- Build Android APKs.
- Build Windows x64 executables.

## Free local AI
The default model is `qwen2.5-coder:3b` through Ollama. No API key or usage credits are required.

Inside the window press **INSTALL FREE AI** to run:

`ollama pull qwen2.5-coder:3b`

Then press **TEST AI**.

Open the tool from:

`Tools > FIXER UNITY Console & Builder`
