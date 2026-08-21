# Primate Panic AI

Unity Editor AI helper for Primate Panic.

## Install from Git URL

In Unity open **Window > Package Manager**, click **+**, choose **Add package from git URL...**, then paste:

`https://github.com/C3NGAV3R/C3NGAV3R.git?path=/Packages/com.c3ngav3r.primatepanic-ai`

Because the repository is private, the computer running Unity must have GitHub access to this repository through Git credentials/credential manager.

## Open

After installation use **Tools > Primate Panic AI**.

## API setup

Paste an OpenAI API key into the package window. The key is stored locally in Unity EditorPrefs on that computer. The package uses the OpenAI Responses API. ChatGPT Plus login/session is not used as API authentication.

## Features in v0.1

- AI chat directly inside the Unity Editor
- Inspect the currently selected GameObject and include its components, Transform, Rigidbody, Collider, and Animator basics in the prompt
- Defaults to `gpt-5.6-luna`; model name can be changed in the window

Do not commit API keys into the Unity project or repository.
