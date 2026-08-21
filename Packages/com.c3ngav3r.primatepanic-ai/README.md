# Primate Panic AI

Local picture-to-Unity reconstruction tool powered by Ollama.

## Install / update

In Unity open **Window > Package Manager**, click **+**, choose **Add package from git URL...**, then paste:

`https://github.com/C3NGAV3R/C3NGAV3R.git?path=/Packages/com.c3ngav3r.primatepanic-ai`

Open with **Tools > Primate Panic AI**.

## Vision model

Install the recommended local vision model:

`ollama run qwen2.5vl:3b`

The plugin talks to:

`http://127.0.0.1:11434/api/generate`

Qwen2.5-VL accepts image input. No OpenAI API key is required.

## v0.4.0 Picture -> Unity

The old selected-GameObject inspector has been removed from the main workflow.

1. Click **Pick Reference Picture**.
2. Choose a PNG/JPG reference image.
3. The plugin shows a preview and downsizes the image before sending it locally to reduce lag.
4. Describe what should be recreated.
5. Click **RECREATE PICTURE IN UNITY**.
6. The vision model returns a bounded structured 3D plan and Unity creates the reconstruction directly in the current scene.

The reconstruction is a 3D blockout made from Unity primitives (Cube, Sphere, Capsule, Cylinder, Plane and Quad), with hierarchy, transforms and approximate colors. It cannot recover the exact original mesh, rig, animation or hidden geometry from one flat screenshot.

The vision prompt tells the model to ignore Unity editor UI, scene gizmos, transform handles and rig/bone lines unless requested.

Recreations are grouped under one root object and can be undone with Ctrl+Z.
