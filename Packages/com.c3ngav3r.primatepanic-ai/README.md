# Primate Panic AI

Local Unity Editor AI agent for Primate Panic, powered by Ollama.

## Install / update from Git URL

In Unity open **Window > Package Manager**, click **+**, choose **Add package from git URL...**, then paste:

`https://github.com/C3NGAV3R/C3NGAV3R.git?path=/Packages/com.c3ngav3r.primatepanic-ai`

## Open

After installation use **Tools > Primate Panic AI**.

## Local AI setup

Install Ollama and keep it running on the same PC as Unity.

Recommended model:

`ollama run qwen2.5-coder:7b`

Default endpoint:

`http://127.0.0.1:11434/api/generate`

No OpenAI API key is required.

## Agent Mode in v0.3.0

The **RUN AGENT** button can now inspect the selected GameObject, include the source code of attached MonoBehaviours, optionally include the recent Unity Editor log, and execute a bounded set of Unity project changes directly.

Allowed direct actions include:

- Create or replace `.cs`, `.json`, and `.txt` files under `Assets/`
- Add a component to the selected GameObject
- Remove a component from the selected GameObject
- Enable or disable the selected GameObject
- Change the selected GameObject local position, rotation, or scale
- Set primitive, enum, string, and scene-object-reference fields/properties on a selected component

Existing files replaced by Agent Mode are backed up under:

`Library/PrimatePanicAIBackups/`

The agent is intentionally not given arbitrary shell/PowerShell access or unrestricted access outside the Unity project's `Assets/` directory.

## Controls

- **Test Ollama**: verifies the local model connection
- **Inspect Selected**: fills the prompt with information about the selected GameObject
- **Ask Only**: asks the local model without applying project changes
- **RUN AGENT**: asks the model for a structured action plan and applies it when Auto Apply is enabled
- **Apply AI actions automatically**: when enabled, RUN AGENT edits the project immediately
- **Include recent Unity Editor log**: gives the model recent Unity Editor log context

Keep source control or project backups for important work even though script replacements also receive local backups.
