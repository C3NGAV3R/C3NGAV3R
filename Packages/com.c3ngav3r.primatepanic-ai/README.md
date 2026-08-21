# Primate Panic AI

Local multi-mode Unity AI tool powered by Ollama.

## Install / update

In Unity open **Window > Package Manager**, click **+**, choose **Add package from git URL...**, then paste:

`https://github.com/C3NGAV3R/C3NGAV3R.git?path=/Packages/com.c3ngav3r.primatepanic-ai`

Open with **Tools > Primate Panic AI**.

## Local models

Coding model:

`ollama run qwen2.5-coder:7b`

Faster coding model:

`ollama run qwen2.5-coder:3b`

Vision model:

`ollama run qwen2.5vl:3b`

Default Ollama endpoint:

`http://127.0.0.1:11434/api/generate`

No OpenAI API key is required.

## v0.5.0 modes

Use the three tabs at the top of the window:

### AGENT

The AI automatically inspects the currently selected GameObject, attached script paths/source, Rigidbody/Collider/Animator basics and recent bounded in-memory Console errors. It returns a structured action plan and immediately applies supported Unity edits.

Supported bounded actions include creating/replacing files under `Assets/`, adding/removing components, enabling/disabling objects, changing local transforms and setting supported component fields/references. Replaced files are backed up under `Library/PrimatePanicAIBackups/`.

There is no separate Inspect Selected button; selecting an object and pressing **RUN AGENT** is enough.

### PLAN

Uses the same automatic inspection as Agent Mode, but **does not change the project**. Press **MAKE PLAN** to review the actions first. If you want those exact actions afterward, press **APPLY LAST PLAN**.

### PICTURE -> 3D

Pick a PNG/JPG reference image, describe what should be recreated and press **RECREATE PICTURE IN UNITY**. The vision model returns a bounded 3D blockout plan and Unity creates it from primitives with hierarchy, transforms and approximate colors.

The vision prompt ignores editor UI, scene gizmos, transform handles and rig/bone lines unless requested. A single flat picture cannot recover the exact original hidden mesh, rig or animation.

## Performance

Fast Mode keeps coding prompts smaller. Recent Console errors are captured in memory; the plugin does not read Unity's `Editor.log` file.
