# Primate Panic AI

Local Unity Editor AI helper for Primate Panic, powered by Ollama.

## Install / update from Git URL

In Unity open **Window > Package Manager**, click **+**, choose **Add package from git URL...**, then paste:

`https://github.com/C3NGAV3R/C3NGAV3R.git?path=/Packages/com.c3ngav3r.primatepanic-ai`

## Open

After installation use **Tools > Primate Panic AI**.

## Local AI setup

Install Ollama and make sure it is running on the same PC as Unity.

Recommended model:

`ollama run qwen2.5-coder:7b`

The Unity package talks to:

`http://127.0.0.1:11434/api/generate`

No OpenAI API key is required.

## Features in v0.2.0

- Local AI chat directly inside the Unity Editor
- No paid API key or API credits
- Test Ollama connection button
- Inspect the currently selected GameObject
- Includes Transform, Rigidbody, Collider, Animator and missing-script basics in the prompt
- Defaults to `qwen2.5-coder:7b`
- Model name and Ollama endpoint can be changed in the window

Keep Ollama running while using the Unity assistant.
