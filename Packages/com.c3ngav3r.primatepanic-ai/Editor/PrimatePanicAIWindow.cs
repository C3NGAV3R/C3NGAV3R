using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace C3NGAV3R.PrimatePanicAI
{
    [InitializeOnLoad]
    internal static class RecentConsoleErrors
    {
        private static readonly List<string> Entries = new List<string>();

        static RecentConsoleErrors()
        {
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
        }

        private static void OnLog(string condition, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)
                return;

            string value = type + ": " + condition;
            if (!string.IsNullOrEmpty(stack))
            {
                string[] lines = stack.Split('\n');
                if (lines.Length > 0 && !string.IsNullOrWhiteSpace(lines[0]))
                    value += "\n" + lines[0].Trim();
            }

            if (value.Length > 1400)
                value = value.Substring(0, 1400) + " ...";

            Entries.Add(value);
            while (Entries.Count > 30)
                Entries.RemoveAt(0);
        }

        public static string Get(int count)
        {
            if (Entries.Count == 0)
                return "No recent Console errors captured.";

            StringBuilder sb = new StringBuilder();
            int start = Mathf.Max(0, Entries.Count - Mathf.Max(1, count));
            for (int i = start; i < Entries.Count; i++)
                sb.AppendLine("---\n" + Entries[i]);
            return sb.ToString();
        }
    }

    public class PrimatePanicAIWindow : EditorWindow
    {
        private enum Mode
        {
            Agent,
            Plan,
            PictureTo3D
        }

        private const string EndpointPref = "PrimatePanicAI.Endpoint";
        private const string TextModelPref = "PrimatePanicAI.TextModel";
        private const string VisionModelPref = "PrimatePanicAI.VisionModel";
        private const string ModePref = "PrimatePanicAI.Mode";
        private const string FastPref = "PrimatePanicAI.Fast";
        private const string ErrorsPref = "PrimatePanicAI.Errors";

        private string endpoint = "http://127.0.0.1:11434/api/generate";
        private string textModel = "qwen2.5-coder:7b";
        private string visionModel = "qwen2.5vl:3b";
        private Mode mode;
        private bool fastMode = true;
        private bool includeErrors = true;
        private bool waiting;
        private string prompt = "";
        private string result = "";
        private Vector2 scroll;
        private AgentPlan lastPlan;

        private string imagePath = "";
        private Texture2D preview;
        private string picturePrompt = "Recreate the main visible object as a clean 3D Unity blockout. Ignore editor gizmos, bones, handles and UI.";

        [MenuItem("Tools/Primate Panic AI")]
        public static void Open()
        {
            GetWindow<PrimatePanicAIWindow>("Primate Panic AI");
        }

        private void OnEnable()
        {
            endpoint = EditorPrefs.GetString(EndpointPref, endpoint);
            textModel = EditorPrefs.GetString(TextModelPref, textModel);
            visionModel = EditorPrefs.GetString(VisionModelPref, visionModel);
            mode = (Mode)Mathf.Clamp(EditorPrefs.GetInt(ModePref, 0), 0, 2);
            fastMode = EditorPrefs.GetBool(FastPref, true);
            includeErrors = EditorPrefs.GetBool(ErrorsPref, true);
        }

        private void OnDisable()
        {
            if (preview != null)
                DestroyImmediate(preview);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(7);
            EditorGUILayout.LabelField("Primate Panic AI - LOCAL v0.9", EditorStyles.boldLabel);

            int newMode = GUILayout.Toolbar((int)mode, new[] { "AGENT", "PLAN", "PICTURE → 3D" }, GUILayout.Height(30));
            if (newMode != (int)mode)
            {
                mode = (Mode)newMode;
                EditorPrefs.SetInt(ModePref, newMode);
                result = "Switched mode.";
            }

            endpoint = EditorGUILayout.TextField("Ollama URL", endpoint);
            EditorPrefs.SetString(EndpointPref, endpoint);

            if (mode == Mode.PictureTo3D)
                DrawPictureMode();
            else
                DrawAgentMode(mode == Mode.Plan);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Result", EditorStyles.boldLabel);
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MinHeight(180));
            EditorGUILayout.TextArea(result, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private void DrawAgentMode(bool planOnly)
        {
            EditorGUILayout.HelpBox(
                planOnly
                    ? "PLAN MODE: builds a structured plan only. Nothing changes until APPLY LAST PLAN."
                    : "AGENT MODE v0.9: supports real UI, scripts, separate scene creation, and automatic retry when the local model returns broken JSON.",
                planOnly ? MessageType.Info : MessageType.Warning);

            textModel = EditorGUILayout.TextField("Coding Model", textModel);
            fastMode = EditorGUILayout.ToggleLeft("Fast Mode", fastMode);
            includeErrors = EditorGUILayout.ToggleLeft("Include recent Console errors", includeErrors);
            EditorPrefs.SetString(TextModelPref, textModel);
            EditorPrefs.SetBool(FastPref, fastMode);
            EditorPrefs.SetBool(ErrorsPref, includeErrors);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Use Fast 3B"))
                {
                    textModel = "qwen2.5-coder:3b";
                    EditorPrefs.SetString(TextModelPref, textModel);
                }

                if (GUILayout.Button("Use Better 7B"))
                {
                    textModel = "qwen2.5-coder:7b";
                    EditorPrefs.SetString(TextModelPref, textModel);
                }

                if (GUILayout.Button("Test"))
                    SendSimple("Reply exactly: CODING MODEL CONNECTED");
            }

            GameObject selected = Selection.activeGameObject;
            EditorGUILayout.HelpBox(
                selected == null
                    ? "Nothing selected. That's fine for CREATE requests."
                    : "Auto-inspection: " + GetHierarchyPath(selected.transform),
                MessageType.None);

            EditorGUILayout.LabelField(planOnly ? "What should it plan?" : "What should it do?", EditorStyles.boldLabel);
            prompt = EditorGUILayout.TextArea(prompt, GUILayout.MinHeight(110));

            GUI.enabled = !waiting && !string.IsNullOrWhiteSpace(prompt);
            if (GUILayout.Button(waiting ? "WORKING..." : planOnly ? "MAKE PLAN" : "RUN AGENT", GUILayout.Height(38)))
                SendAgent(!planOnly);
            GUI.enabled = true;

            if (planOnly && lastPlan != null && lastPlan.actions != null && lastPlan.actions.Length > 0)
            {
                if (GUILayout.Button("APPLY LAST PLAN", GUILayout.Height(31)))
                {
                    result += "\n\n" + ApplyPlan(lastPlan);
                    lastPlan = null;
                }
            }
        }

        private void DrawPictureMode()
        {
            EditorGUILayout.HelpBox("Choose a reference image and let the local vision model build a primitive 3D blockout.", MessageType.Info);
            visionModel = EditorGUILayout.TextField("Vision Model", visionModel);
            EditorPrefs.SetString(VisionModelPref, visionModel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Test Vision")) SendVisionTest();
                if (GUILayout.Button("Pick Picture")) PickPicture();
                if (GUILayout.Button("Clear")) ClearPicture();
            }

            if (preview != null)
            {
                float w = Mathf.Min(position.width - 28f, 440f);
                float h = Mathf.Clamp(w / Mathf.Max(.01f, (float)preview.width / preview.height), 110f, 300f);
                Rect r = GUILayoutUtility.GetRect(w, h, GUILayout.ExpandWidth(false));
                EditorGUI.DrawPreviewTexture(r, preview, null, ScaleMode.ScaleToFit);
            }
            else
            {
                EditorGUILayout.HelpBox("No picture selected.", MessageType.Warning);
            }

            picturePrompt = EditorGUILayout.TextArea(picturePrompt, GUILayout.MinHeight(85));
            GUI.enabled = !waiting && preview != null;
            if (GUILayout.Button(waiting ? "BUILDING..." : "RECREATE PICTURE IN UNITY", GUILayout.Height(38)))
                SendPicture();
            GUI.enabled = true;
        }

        private void SendSimple(string text)
        {
            OllamaRequest request = new OllamaRequest
            {
                model = textModel.Trim(),
                prompt = text,
                stream = false,
                keep_alive = "15m",
                options = new OllamaOptions { num_ctx = 2048, num_predict = 40, temperature = 0f }
            };
            Send(JsonUtility.ToJson(request), 300, t => result = t);
        }

        private void SendAgent(bool apply)
        {
            OllamaAgentRequest request = new OllamaAgentRequest
            {
                model = textModel.Trim(),
                system = BuildSystemPrompt(),
                prompt = BuildContext(),
                stream = false,
                format = "json",
                keep_alive = "15m",
                options = new OllamaOptions
                {
                    num_ctx = fastMode ? 6000 : 10000,
                    num_predict = fastMode ? 4200 : 6800,
                    temperature = .01f
                }
            };

            Send(JsonUtility.ToJson(request), 420, text => HandleAgent(text, apply, false));
        }

        private string BuildContext()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("USER REQUEST:");
            sb.AppendLine(prompt.Trim());

            GameObject go = Selection.activeGameObject;
            if (go == null)
            {
                sb.AppendLine("\nSELECTED GAMEOBJECT: NONE");
                sb.AppendLine("For new creation requests this is NOT a blocker.");
            }
            else
            {
                sb.AppendLine("\nSELECTED GAMEOBJECT:");
                sb.AppendLine("Path: " + GetHierarchyPath(go.transform));
                sb.AppendLine("Scene: " + go.scene.name);
                sb.AppendLine("Active: " + go.activeSelf);
                sb.AppendLine("Position: " + go.transform.localPosition);
                sb.AppendLine("Rotation: " + go.transform.localEulerAngles);
                sb.AppendLine("Scale: " + go.transform.localScale);
                sb.AppendLine("Components:");

                int chars = 0;
                int maxChars = fastMode ? 9000 : 26000;
                foreach (Component c in go.GetComponents<Component>())
                {
                    if (c == null)
                    {
                        sb.AppendLine("- MISSING SCRIPT");
                        continue;
                    }

                    sb.AppendLine("- " + c.GetType().FullName);
                    MonoBehaviour mb = c as MonoBehaviour;
                    if (mb == null || chars >= maxChars)
                        continue;

                    MonoScript ms = MonoScript.FromMonoBehaviour(mb);
                    if (ms == null)
                        continue;

                    string asset = AssetDatabase.GetAssetPath(ms);
                    if (string.IsNullOrEmpty(asset) || !asset.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string full = AssetToFull(asset);
                    if (!File.Exists(full))
                        continue;

                    string source = File.ReadAllText(full);
                    int take = Mathf.Min(source.Length, maxChars - chars);
                    sb.AppendLine("SCRIPT " + asset + ":\n" + source.Substring(0, take));
                    chars += take;
                }
            }

            if (includeErrors)
                sb.AppendLine("\nRECENT CONSOLE ERRORS:\n" + RecentConsoleErrors.Get(fastMode ? 5 : 10));

            return sb.ToString();
        }

        private static string BuildSystemPrompt()
        {
            return
                "You are an action-taking Unity Editor agent. Return ONLY one valid JSON object. Never markdown. " +
                "The exact root schema is {\"message\":\"short summary\",\"actions\":[...]} and nothing else. " +
                "IMPORTANT JSON RULES: every action object must use each key AT MOST ONCE. NEVER repeat field/value/componentType/path/content keys inside one object. " +
                "If multiple component properties must be changed, output multiple set_component_field actions, one property per action. Maximum 32 actions. Keep plans concise. " +
                "SUPPORTED ACTIONS: create_scene, create_ui, create_gameobject, create_or_replace_file, add_component, remove_component, set_active, set_local_position, set_local_rotation, set_local_scale, set_component_field. " +
                "Action fields available: type,name,sceneName,parentPath,targetPath,uiType,text,color,components,componentType,field,value,path,content,primitive,boolValue,x,y,z,width,height,fontSize. " +
                "SCENE RULES: when the user asks for separate scenes, first use create_scene for each scene. Then EVERY action belonging to one of those scenes must include sceneName. " +
                "Example: {\"type\":\"create_scene\",\"name\":\"MainMenuScene\"}, {\"type\":\"create_ui\",\"sceneName\":\"MainMenuScene\",\"uiType\":\"canvas\",\"name\":\"MainMenuCanvas\",\"parentPath\":\"ROOT\"}. " +
                "Do not create a new Camera for a Screen Space Overlay UI scene unless the user explicitly asks for a camera. Do not create, move, replace, or configure Gorilla Rig, XR Origin, Main Camera, or player Rigidbody unless explicitly asked. " +
                "UI RULES: visible UI must use create_ui, not empty create_gameobject. Supported uiType values: canvas,background,image,panel,text,title,button,slider,eventsystem. " +
                "create_ui canvas automatically creates Screen Space Overlay Canvas + CanvasScaler 1920x1080 + GraphicRaycaster. Do NOT waste extra actions re-setting those defaults unless specifically needed. " +
                "create_ui button automatically creates Image + Button + centered child Text. create_ui slider automatically creates a real Slider hierarchy. Use x,y,width,height,fontSize directly on create_ui for layout. z is UI rotation in degrees. " +
                "Use uiType=background for full-screen backgrounds and uiType=image for decorative bars/slashes/blocks. Prefer UnityEngine.UI.Text, not TextMeshPro, unless explicitly requested. " +
                "FILE RULES: create_or_replace_file may ONLY create .cs, .json, or .txt files under Assets/. NEVER invent placeholder .png, .jpg, .spriteasset, .fbx, .obj, font, audio, or binary files by writing text into them. " +
                "If a requested visual asset does not exist, approximate it with real Unity UI Images/colors/shapes instead. For runtime behavior create complete compiling C# scripts under Assets/Scripts/. Always include path and complete content. " +
                "Scripts using SceneManager must include using UnityEngine.SceneManagement;. Scripts using IEnumerator/coroutines must include using System.Collections;. " +
                "A brand-new custom MonoBehaviour cannot be attached until Unity recompiles; create the script now and leave attachment/wiring for one follow-up run. " +
                "MAIN MENU GUIDANCE: for a new menu, create a dedicated menu scene if requested, one overlay canvas, full-screen background, large title, real PLAY/SETTINGS/QUIT buttons, SettingsPanel, volume slider, Back button, and exactly one EventSystem. " +
                "If a separate loading scene is requested, create that scene separately with its own overlay canvas, visual background/title/loading text/progress slider and a LoadingScreenController script. PLAY should load the loading scene. The loading scene controller should asynchronously load the gameplay scene. " +
                "Do the user's request instead of giving manual instructions. A missing selection is never a blocker for creating a new feature. Only return zero actions if the request clearly targets an existing object that truly cannot be identified.";
        }

        private static string BuildRetrySystemPrompt()
        {
            return
                "Return ONLY valid JSON for the Unity agent. Root schema: {\"message\":\"short\",\"actions\":[...]}. " +
                "Regenerate the whole plan concisely from the user request. Maximum 28 actions. Never duplicate a JSON key inside an action. One set_component_field action may set exactly ONE field. " +
                "Allowed types: create_scene,create_ui,create_gameobject,create_or_replace_file,add_component,remove_component,set_active,set_local_position,set_local_rotation,set_local_scale,set_component_field. " +
                "Allowed uiType: canvas,background,image,panel,text,title,button,slider,eventsystem. Use sceneName on actions for separate scenes. Do not create cameras/XR/player objects for menu scenes. " +
                "Only write .cs/.json/.txt under Assets/. Never create fake png/spriteasset/font/binary files. Use create_ui defaults instead of many redundant component-field actions. Complete C# scripts must compile.";
        }

        private void HandleAgent(string text, bool apply, bool isRetry)
        {
            AgentPlan plan;
            string parseError;
            if (!TryParseAgentPlan(text, out plan, out parseError))
            {
                if (!isRetry)
                {
                    RetryInvalidAgentPlan(apply, parseError);
                    return;
                }

                result = "Agent plan parse failed after automatic retry: " + parseError + "\n\n" + text;
                return;
            }

            lastPlan = plan;
            int count = plan.actions == null ? 0 : plan.actions.Length;
            result = (string.IsNullOrEmpty(plan.message) ? "Plan ready." : plan.message) + "\nPlanned actions: " + count;

            if (apply && count > 0)
            {
                result += "\n\n" + ApplyPlan(plan);
                lastPlan = null;
            }
            else if (!apply && count > 0)
            {
                result += "\nPLAN MODE: nothing changed.";
                foreach (AgentAction a in plan.actions)
                    result += "\n- " + Describe(a);
            }
        }

        private void RetryInvalidAgentPlan(bool apply, string parseError)
        {
            result = "The model returned broken JSON (" + parseError + "). Regenerating a shorter valid plan automatically...";
            Repaint();

            OllamaAgentRequest retry = new OllamaAgentRequest
            {
                model = textModel.Trim(),
                system = BuildRetrySystemPrompt(),
                prompt = "USER REQUEST AND PROJECT CONTEXT:\n" + BuildContext() + "\n\nThe previous attempt was invalid JSON. Regenerate the entire plan from scratch. Keep it shorter and valid.",
                stream = false,
                format = "json",
                keep_alive = "15m",
                options = new OllamaOptions
                {
                    num_ctx = fastMode ? 6000 : 10000,
                    num_predict = fastMode ? 3800 : 6200,
                    temperature = 0f
                }
            };

            Send(JsonUtility.ToJson(retry), 420, text => HandleAgent(text, apply, true));
        }

        private static bool TryParseAgentPlan(string text, out AgentPlan plan, out string error)
        {
            plan = null;
            error = "";
            try
            {
                string json = ExtractJson(text);
                plan = JsonUtility.FromJson<AgentPlan>(json);
                if (plan == null)
                {
                    error = "Parsed object was null";
                    return false;
                }

                if (plan.actions == null)
                    plan.actions = new AgentAction[0];

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private string ApplyPlan(AgentPlan plan)
        {
            StringBuilder sb = new StringBuilder("APPLYING:\n");
            bool filesChanged = false;
            Dictionary<string, GameObject> lastCreatedByScene = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, GameObject> lastCanvasByScene = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> touchedNamedScenes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (AgentAction action in plan.actions)
            {
                if (action == null)
                    continue;

                try
                {
                    string type = (action.type ?? "").Trim().ToLowerInvariant();
                    if (type == "message" || type == "note" || type == "explain")
                        continue;

                    if (type == "create_scene")
                    {
                        Scene scene = EnsureScene(action.name);
                        touchedNamedScenes.Add(scene.name);
                        sb.AppendLine("✅ Created/opened scene '" + scene.name + "'");
                        continue;
                    }

                    string sceneKey = GetSceneKey(action.sceneName);
                    Scene targetScene;
                    if (!string.IsNullOrWhiteSpace(action.sceneName))
                    {
                        targetScene = EnsureScene(action.sceneName);
                        touchedNamedScenes.Add(targetScene.name);
                        sceneKey = targetScene.name;
                    }
                    else
                    {
                        targetScene = SceneManager.GetActiveScene();
                        sceneKey = targetScene.IsValid() ? targetScene.name : "";
                    }

                    GameObject lastCreated = null;
                    lastCreatedByScene.TryGetValue(sceneKey, out lastCreated);

                    GameObject lastCanvas = null;
                    if (!lastCanvasByScene.TryGetValue(sceneKey, out lastCanvas) || lastCanvas == null)
                        lastCanvas = FindFirstSceneObjectWithComponent("Canvas", sceneKey);

                    string line;

                    if (type == "create_ui")
                    {
                        GameObject created = CreateUI(action, lastCanvas, sceneKey, out line);
                        lastCreatedByScene[sceneKey] = created;
                        if (string.Equals((action.uiType ?? "").Trim(), "canvas", StringComparison.OrdinalIgnoreCase))
                            lastCanvasByScene[sceneKey] = created;
                    }
                    else if (type == "create_gameobject")
                    {
                        GameObject created = CreateGameObject(action, sceneKey, out line);
                        lastCreatedByScene[sceneKey] = created;
                    }
                    else if (type == "create_or_replace_file")
                    {
                        line = WriteFile(action);
                        filesChanged = true;
                    }
                    else
                    {
                        GameObject target = ResolveActionTarget(action, lastCreated, sceneKey);
                        if (type == "add_component")
                            line = AddComponent(target, InferComponentType(action));
                        else if (type == "remove_component")
                            line = RemoveComponent(target, InferComponentType(action));
                        else if (type == "set_active")
                            line = SetActive(target, action.boolValue);
                        else if (type == "set_local_position")
                            line = SetTransform(target, "position", action);
                        else if (type == "set_local_rotation")
                            line = SetTransform(target, "rotation", action);
                        else if (type == "set_local_scale")
                            line = SetTransform(target, "scale", action);
                        else if (type == "set_component_field")
                            line = SetComponentField(target, action, sceneKey);
                        else
                            line = "Skipped unknown action " + action.type;
                    }

                    sb.AppendLine("✅ " + line);
                }
                catch (Exception ex)
                {
                    sb.AppendLine("❌ " + Describe(action) + " -> " + ex.Message);
                }
            }

            if (filesChanged)
                AssetDatabase.Refresh();

            foreach (string sceneName in touchedNamedScenes)
            {
                Scene scene = SceneManager.GetSceneByName(sceneName);
                if (scene.IsValid() && scene.isLoaded && !string.IsNullOrWhiteSpace(scene.path))
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
            }

            GameObject selected = Selection.activeGameObject;
            if (selected != null && selected.scene.IsValid() && !touchedNamedScenes.Contains(selected.scene.name))
                EditorSceneManager.MarkSceneDirty(selected.scene);

            sb.AppendLine("Done. Separate named scenes were saved automatically. If a new custom C# script was created, let Unity compile before asking Agent to attach/wire that custom script.");
            return sb.ToString();
        }

        private static Scene EnsureScene(string rawName)
        {
            string sceneName = CleanSceneName(rawName);
            if (string.IsNullOrWhiteSpace(sceneName))
                throw new InvalidOperationException("Scene name is missing.");

            Scene loaded = SceneManager.GetSceneByName(sceneName);
            if (loaded.IsValid() && loaded.isLoaded)
            {
                SceneManager.SetActiveScene(loaded);
                return loaded;
            }

            string folder = "Assets/Scenes";
            if (!AssetDatabase.IsValidFolder(folder))
            {
                if (!AssetDatabase.IsValidFolder("Assets"))
                    throw new InvalidOperationException("Assets folder not found.");
                AssetDatabase.CreateFolder("Assets", "Scenes");
            }

            string assetPath = folder + "/" + sceneName + ".unity";
            string fullPath = AssetToFull(assetPath);

            Scene scene;
            if (File.Exists(fullPath))
            {
                scene = EditorSceneManager.OpenScene(assetPath, OpenSceneMode.Additive);
            }
            else
            {
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                if (!EditorSceneManager.SaveScene(scene, assetPath))
                    throw new InvalidOperationException("Could not save new scene: " + assetPath);
                scene = SceneManager.GetSceneByPath(assetPath);
            }

            if (!scene.IsValid())
                throw new InvalidOperationException("Could not create/open scene: " + sceneName);

            SceneManager.SetActiveScene(scene);
            return scene;
        }

        private static string CleanSceneName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";
            string name = Path.GetFileNameWithoutExtension(value.Trim());
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c.ToString(), "");
            return name.Trim();
        }

        private static string GetSceneKey(string sceneName)
        {
            if (!string.IsNullOrWhiteSpace(sceneName))
                return CleanSceneName(sceneName);

            Scene active = SceneManager.GetActiveScene();
            return active.IsValid() ? active.name : "";
        }

        private static GameObject ResolveActionTarget(AgentAction action, GameObject lastCreated, string sceneName)
        {
            GameObject target = Resolve(action.targetPath, sceneName);
            if (target != null)
                return target;

            if (LooksLikeComponentName(action.targetPath))
                return lastCreated ?? Selection.activeGameObject;

            return Selection.activeGameObject ?? lastCreated;
        }

        private static string InferComponentType(AgentAction action)
        {
            if (!string.IsNullOrWhiteSpace(action.componentType))
                return action.componentType.Trim();
            if (LooksLikeComponentName(action.targetPath))
                return action.targetPath.Trim();
            if (LooksLikeComponentName(action.name))
                return action.name.Trim();
            return "";
        }

        private static bool LooksLikeComponentName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string v = value.Trim();
            string[] common =
            {
                "Canvas", "CanvasScaler", "GraphicRaycaster", "RectTransform", "Image", "Text", "Button", "Slider",
                "EventSystem", "StandaloneInputModule", "InputSystemUIInputModule", "Rigidbody", "Collider",
                "BoxCollider", "SphereCollider", "CapsuleCollider", "AudioSource", "Animator", "Camera"
            };

            for (int i = 0; i < common.Length; i++)
                if (string.Equals(v, common[i], StringComparison.OrdinalIgnoreCase))
                    return true;

            return v.StartsWith("UnityEngine.", StringComparison.OrdinalIgnoreCase);
        }

        private static GameObject CreateGameObject(AgentAction action, string sceneName, out string message)
        {
            string name = CleanName(string.IsNullOrWhiteSpace(action.name) ? "AI_Object" : action.name);
            GameObject go;

            if (!string.IsNullOrWhiteSpace(action.primitive))
            {
                PrimitiveType primitiveType;
                if (!TryPrimitive(action.primitive, out primitiveType))
                    primitiveType = PrimitiveType.Cube;
                go = GameObject.CreatePrimitive(primitiveType);
                go.name = name;
            }
            else
            {
                go = new GameObject(name);
            }

            Undo.RegisterCreatedObjectUndo(go, "AI create GameObject");

            GameObject parent = Resolve(action.parentPath, sceneName);
            if (parent != null)
                go.transform.SetParent(parent.transform, false);

            go.transform.localPosition = new Vector3(action.x, action.y, action.z);

            if (action.components != null)
            {
                foreach (string componentName in action.components)
                    if (!string.IsNullOrWhiteSpace(componentName))
                        TryAddComponent(go, componentName);
            }

            Selection.activeGameObject = go;
            message = "Created " + go.name + (action.components == null ? "" : " with components");
            return go;
        }

        private static GameObject CreateUI(AgentAction action, GameObject fallbackCanvas, string sceneName, out string message)
        {
            string kind = (action.uiType ?? "").Trim().ToLowerInvariant();
            if (kind == "label") kind = "text";
            if (kind == "menu") kind = "panel";
            if (kind == "img" || kind == "decor" || kind == "slash" || kind == "bar") kind = "image";

            string name = CleanName(string.IsNullOrWhiteSpace(action.name) ? "AI_" + kind : action.name);

            if (kind == "eventsystem")
            {
                GameObject existing = FindFirstSceneObjectWithComponent("EventSystem", sceneName);
                if (existing != null)
                {
                    Selection.activeGameObject = existing;
                    message = "Reused existing EventSystem '" + existing.name + "'";
                    return existing;
                }

                GameObject eventSystem = new GameObject(name);
                Undo.RegisterCreatedObjectUndo(eventSystem, "AI create EventSystem");
                TryAddComponent(eventSystem, "UnityEngine.EventSystems.EventSystem");
                if (!TryAddComponent(eventSystem, "UnityEngine.InputSystem.UI.InputSystemUIInputModule"))
                    TryAddComponent(eventSystem, "UnityEngine.EventSystems.StandaloneInputModule");

                Selection.activeGameObject = eventSystem;
                message = "Created functional EventSystem";
                return eventSystem;
            }

            GameObject go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "AI create UI");

            GameObject parent = Resolve(action.parentPath, sceneName);
            if (parent == null && kind != "canvas")
                parent = fallbackCanvas;
            if (parent != null)
                go.transform.SetParent(parent.transform, false);

            RectTransform rect = (RectTransform)go.transform;
            rect.localScale = Vector3.one;

            if (kind == "canvas")
            {
                Canvas canvas = go.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 100;

                Component scaler = AddByName(go, "UnityEngine.UI.CanvasScaler");
                AddByName(go, "UnityEngine.UI.GraphicRaycaster");
                if (scaler != null)
                {
                    SetEnumMember(scaler, "uiScaleMode", "ScaleWithScreenSize");
                    SetMember(scaler, "referenceResolution", new Vector2(1920, 1080));
                    SetMember(scaler, "matchWidthOrHeight", .5f);
                }

                Selection.activeGameObject = go;
                message = "Created Screen Space Overlay Canvas + CanvasScaler + GraphicRaycaster";
                return go;
            }

            if (kind == "background")
            {
                StretchFull(rect);
                AddImage(go, string.IsNullOrWhiteSpace(action.color) ? "#08090DFF" : action.color);
            }
            else if (kind == "image")
            {
                if (action.width <= 0 && action.height <= 0 && name.IndexOf("background", StringComparison.OrdinalIgnoreCase) >= 0)
                    StretchFull(rect);
                else
                    SetRect(rect, action, 500, 180);
                AddImage(go, string.IsNullOrWhiteSpace(action.color) ? "#FFFFFFFF" : action.color);
            }
            else if (kind == "panel")
            {
                SetRect(rect, action, 760, 500);
                AddImage(go, string.IsNullOrWhiteSpace(action.color) ? "#17171DEB" : action.color);
            }
            else if (kind == "text" || kind == "title")
            {
                SetRect(rect, action, kind == "title" ? 900 : 600, kind == "title" ? 120 : 60);
                AddText(go, string.IsNullOrWhiteSpace(action.text) ? name : action.text, action.fontSize > 0 ? action.fontSize : (kind == "title" ? 72 : 30), action.color);
            }
            else if (kind == "button")
            {
                SetRect(rect, action, 420, 90);
                Component image = AddImage(go, string.IsNullOrWhiteSpace(action.color) ? "#292933FF" : action.color);
                Component button = AddByName(go, "UnityEngine.UI.Button");
                if (button != null && image != null)
                    SetMember(button, "targetGraphic", image);

                CreateButtonLabel(go, string.IsNullOrWhiteSpace(action.text) ? name.Replace("Button", "") : action.text, action.fontSize > 0 ? action.fontSize : 34);
            }
            else if (kind == "slider")
            {
                SetRect(rect, action, 520, 50);
                BuildSlider(go, action.color);
            }
            else
            {
                throw new InvalidOperationException("Unknown uiType: " + action.uiType + ". Supported: canvas, background, image, panel, text, title, button, slider, eventsystem");
            }

            if (action.components != null)
            {
                foreach (string extra in action.components)
                    if (!string.IsNullOrWhiteSpace(extra))
                        TryAddComponent(go, extra);
            }

            Selection.activeGameObject = go;
            message = "Created real UI " + kind + " '" + go.name + "'";
            return go;
        }

        private static void StretchFull(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(.5f, .5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;
        }

        private static void SetRect(RectTransform rect, AgentAction action, float defaultWidth, float defaultHeight)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = new Vector2(action.x, action.y);
            rect.sizeDelta = new Vector2(action.width > 0 ? action.width : defaultWidth, action.height > 0 ? action.height : defaultHeight);
            rect.localEulerAngles = new Vector3(0f, 0f, action.z);
        }

        private static Component AddImage(GameObject go, string color)
        {
            Component image = AddByName(go, "UnityEngine.UI.Image");
            if (image != null)
                SetMember(image, "color", ParseColor(color, new Color(.15f, .15f, .18f, 1f)));
            return image;
        }

        private static Component AddText(GameObject go, string text, int size, string color)
        {
            Component component = AddByName(go, "UnityEngine.UI.Text");
            if (component == null)
                return null;

            SetMember(component, "text", text);
            SetMember(component, "fontSize", size);
            SetEnumMember(component, "alignment", "MiddleCenter");
            SetMember(component, "color", ParseColor(color, Color.white));

            try
            {
                Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (font != null)
                    SetMember(component, "font", font);
            }
            catch
            {
            }

            return component;
        }

        private static void CreateButtonLabel(GameObject button, string label, int size)
        {
            GameObject textGo = new GameObject("Text", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(textGo, "AI button label");
            textGo.transform.SetParent(button.transform, false);
            RectTransform rect = (RectTransform)textGo.transform;
            StretchFull(rect);
            AddText(textGo, label, size, "#FFFFFFFF");
        }

        private static void BuildSlider(GameObject root, string accent)
        {
            Component slider = AddByName(root, "UnityEngine.UI.Slider");

            GameObject background = CreateUiChild(root, "Background");
            RectTransform backgroundRect = (RectTransform)background.transform;
            backgroundRect.anchorMin = new Vector2(0, .25f);
            backgroundRect.anchorMax = new Vector2(1, .75f);
            backgroundRect.offsetMin = Vector2.zero;
            backgroundRect.offsetMax = Vector2.zero;
            AddImage(background, "#3A3A42FF");

            GameObject fillArea = CreateUiChild(root, "Fill Area");
            RectTransform fillAreaRect = (RectTransform)fillArea.transform;
            fillAreaRect.anchorMin = new Vector2(0, .25f);
            fillAreaRect.anchorMax = new Vector2(1, .75f);
            fillAreaRect.offsetMin = new Vector2(8, 0);
            fillAreaRect.offsetMax = new Vector2(-8, 0);

            GameObject fill = CreateUiChild(fillArea, "Fill");
            RectTransform fillRect = (RectTransform)fill.transform;
            StretchFull(fillRect);
            AddImage(fill, string.IsNullOrWhiteSpace(accent) ? "#61D7FFFF" : accent);

            GameObject handleArea = CreateUiChild(root, "Handle Slide Area");
            RectTransform handleAreaRect = (RectTransform)handleArea.transform;
            handleAreaRect.anchorMin = Vector2.zero;
            handleAreaRect.anchorMax = Vector2.one;
            handleAreaRect.offsetMin = new Vector2(10, 0);
            handleAreaRect.offsetMax = new Vector2(-10, 0);

            GameObject handle = CreateUiChild(handleArea, "Handle");
            RectTransform handleRect = (RectTransform)handle.transform;
            handleRect.sizeDelta = new Vector2(28, 42);
            Component handleImage = AddImage(handle, "#FFFFFFFF");

            if (slider != null)
            {
                SetMember(slider, "fillRect", fillRect);
                SetMember(slider, "handleRect", handleRect);
                if (handleImage != null)
                    SetMember(slider, "targetGraphic", handleImage);
                SetMember(slider, "minValue", 0f);
                SetMember(slider, "maxValue", 1f);
                SetMember(slider, "value", .8f);
            }
        }

        private static GameObject CreateUiChild(GameObject parent, string name)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "AI UI child");
            go.transform.SetParent(parent.transform, false);
            return go;
        }

        private static string AddComponent(GameObject target, string typeName)
        {
            Require(target);
            if (string.IsNullOrWhiteSpace(typeName))
                throw new InvalidOperationException("Component type was missing and could not be inferred.");
            if (!TryAddComponent(target, typeName))
                throw new InvalidOperationException("Component type not found or could not be added: " + typeName);
            return "Added " + typeName + " to " + target.name;
        }

        private static string RemoveComponent(GameObject target, string typeName)
        {
            Require(target);
            Type type = FindType(typeName);
            if (type == null)
                throw new InvalidOperationException("Type not found: " + typeName);
            Component component = target.GetComponent(type);
            if (component == null)
                return target.name + " did not have " + typeName;
            Undo.DestroyObjectImmediate(component);
            return "Removed " + typeName + " from " + target.name;
        }

        private static bool TryAddComponent(GameObject go, string typeName)
        {
            Type type = FindType(typeName);
            if (type == null || !typeof(Component).IsAssignableFrom(type))
                return false;
            if (go.GetComponent(type) != null)
                return true;

            try
            {
                Undo.AddComponent(go, type);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static Component AddByName(GameObject go, string typeName)
        {
            Type type = FindType(typeName);
            if (type == null || !typeof(Component).IsAssignableFrom(type))
                return null;

            Component existing = go.GetComponent(type);
            if (existing != null)
                return existing;

            try
            {
                return Undo.AddComponent(go, type);
            }
            catch
            {
                return null;
            }
        }

        private static Type FindType(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            string trimmed = name.Trim();
            string expanded = ExpandCommonTypeName(trimmed);

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type direct = assembly.GetType(trimmed, false, true) ?? assembly.GetType(expanded, false, true);
                if (direct != null)
                    return direct;

                try
                {
                    foreach (Type type in assembly.GetTypes())
                    {
                        if (string.Equals(type.Name, trimmed, StringComparison.OrdinalIgnoreCase) || string.Equals(type.FullName, expanded, StringComparison.OrdinalIgnoreCase))
                            return type;
                    }
                }
                catch (ReflectionTypeLoadException)
                {
                }
            }

            return null;
        }

        private static string ExpandCommonTypeName(string name)
        {
            switch ((name ?? "").Trim().ToLowerInvariant())
            {
                case "canvasscaler": return "UnityEngine.UI.CanvasScaler";
                case "graphicraycaster": return "UnityEngine.UI.GraphicRaycaster";
                case "image": return "UnityEngine.UI.Image";
                case "text": return "UnityEngine.UI.Text";
                case "button": return "UnityEngine.UI.Button";
                case "slider": return "UnityEngine.UI.Slider";
                case "eventsystem": return "UnityEngine.EventSystems.EventSystem";
                case "standaloneinputmodule": return "UnityEngine.EventSystems.StandaloneInputModule";
                case "inputsystemuiinputmodule": return "UnityEngine.InputSystem.UI.InputSystemUIInputModule";
                case "recttransform": return "UnityEngine.RectTransform";
                case "canvas": return "UnityEngine.Canvas";
                default: return name;
            }
        }

        private static GameObject FindFirstSceneObjectWithComponent(string typeName, string sceneName)
        {
            Type type = FindType(typeName);
            if (type == null)
                return null;

            foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go == null || !go.scene.IsValid())
                    continue;
                if (!string.IsNullOrWhiteSpace(sceneName) && !string.Equals(go.scene.name, sceneName, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (go.GetComponent(type) != null)
                    return go;
            }

            return null;
        }

        private static string SetActive(GameObject target, bool value)
        {
            Require(target);
            Undo.RecordObject(target, "AI active");
            target.SetActive(value);
            EditorUtility.SetDirty(target);
            return "Set " + target.name + " active=" + value;
        }

        private static string SetTransform(GameObject target, string kind, AgentAction action)
        {
            Require(target);
            Undo.RecordObject(target.transform, "AI transform");
            Vector3 value = new Vector3(action.x, action.y, action.z);

            if (kind == "position")
                target.transform.localPosition = value;
            else if (kind == "rotation")
                target.transform.localEulerAngles = value;
            else
                target.transform.localScale = value;

            EditorUtility.SetDirty(target.transform);
            return "Set " + kind + " on " + target.name;
        }

        private static string SetComponentField(GameObject target, AgentAction action, string sceneName)
        {
            Require(target);

            string componentType = InferComponentType(action);
            if (string.IsNullOrWhiteSpace(componentType))
                throw new InvalidOperationException("Component type missing.");

            Type type = FindType(componentType);
            if (type == null)
                throw new InvalidOperationException("Type not found: " + componentType);

            Component component = target.GetComponent(type);
            if (component == null)
                throw new InvalidOperationException("Component missing on '" + target.name + "': " + componentType);

            string fieldName = action.field ?? "";
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            FieldInfo field = type.GetField(fieldName, flags);
            PropertyInfo property = field == null ? type.GetProperty(fieldName, flags) : null;

            if (field == null && (property == null || !property.CanWrite))
                throw new InvalidOperationException("Writable field/property not found: " + componentType + "." + fieldName);

            Type valueType = field != null ? field.FieldType : property.PropertyType;
            object value = ConvertValue(action.value, valueType, sceneName);

            Undo.RecordObject(component, "AI set field");
            if (field != null)
                field.SetValue(component, value);
            else
                property.SetValue(component, value, null);

            EditorUtility.SetDirty(component);
            return "Set " + componentType + "." + fieldName + " on " + target.name;
        }

        private static object ConvertValue(string raw, Type type, string sceneName)
        {
            raw = raw ?? "";
            string cleaned = raw.Trim();

            if (type == typeof(string)) return raw;
            if (type == typeof(bool)) return bool.Parse(cleaned);
            if (type == typeof(int)) return int.Parse(cleaned, CultureInfo.InvariantCulture);
            if (type == typeof(float)) return float.Parse(cleaned, CultureInfo.InvariantCulture);
            if (type == typeof(double)) return double.Parse(cleaned, CultureInfo.InvariantCulture);

            if (type == typeof(Vector2))
            {
                string[] parts = cleaned.Trim('(', ')', '[', ']').Split(',');
                if (parts.Length != 2)
                    throw new FormatException("Vector2 value must be x,y");
                return new Vector2(float.Parse(parts[0], CultureInfo.InvariantCulture), float.Parse(parts[1], CultureInfo.InvariantCulture));
            }

            if (type == typeof(Vector3))
            {
                string[] parts = cleaned.Trim('(', ')', '[', ']').Split(',');
                if (parts.Length != 3)
                    throw new FormatException("Vector3 value must be x,y,z");
                return new Vector3(float.Parse(parts[0], CultureInfo.InvariantCulture), float.Parse(parts[1], CultureInfo.InvariantCulture), float.Parse(parts[2], CultureInfo.InvariantCulture));
            }

            if (type == typeof(Color))
                return ParseColor(cleaned, Color.white);

            if (type.IsEnum)
                return Enum.Parse(type, cleaned.Replace(" ", ""), true);

            if (typeof(UnityEngine.Object).IsAssignableFrom(type))
            {
                if (string.Equals(cleaned, "NONE", StringComparison.OrdinalIgnoreCase) || string.Equals(cleaned, "NULL", StringComparison.OrdinalIgnoreCase))
                    return null;

                GameObject go = Resolve(cleaned, sceneName);
                if (go == null)
                    throw new InvalidOperationException("Reference not found: " + cleaned);

                if (type == typeof(GameObject)) return go;
                if (type == typeof(Transform)) return go.transform;

                Component found = go.GetComponent(type);
                if (found == null)
                    throw new InvalidOperationException("Reference object '" + go.name + "' has no " + type.Name);
                return found;
            }

            throw new InvalidOperationException("Unsupported value type " + type.Name);
        }

        private static void SetMember(object obj, string name, object value)
        {
            if (obj == null)
                return;

            Type type = obj.GetType();
            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                try { field.SetValue(obj, value); } catch { }
                return;
            }

            PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.CanWrite)
            {
                try { property.SetValue(obj, value, null); } catch { }
            }
        }

        private static void SetEnumMember(object obj, string name, string enumName)
        {
            if (obj == null)
                return;

            Type type = obj.GetType();
            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            PropertyInfo property = field == null ? type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) : null;
            Type enumType = field != null ? field.FieldType : property != null ? property.PropertyType : null;
            if (enumType == null || !enumType.IsEnum)
                return;

            object value = Enum.Parse(enumType, enumName, true);
            if (field != null)
                field.SetValue(obj, value);
            else if (property != null && property.CanWrite)
                property.SetValue(obj, value, null);
        }

        private static Color ParseColor(string hex, Color fallback)
        {
            Color color;
            return !string.IsNullOrWhiteSpace(hex) && ColorUtility.TryParseHtmlString(hex.Trim(), out color) ? color : fallback;
        }

        private static string WriteFile(AgentAction action)
        {
            string path = action.path;

            if (string.IsNullOrWhiteSpace(path))
            {
                string candidate = !string.IsNullOrWhiteSpace(action.name) ? action.name.Trim() : action.targetPath;
                if (string.IsNullOrWhiteSpace(candidate))
                    throw new InvalidOperationException("Missing path and filename");

                candidate = Path.GetFileName(candidate.Replace('\\', '/'));
                if (!candidate.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) && !candidate.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && !candidate.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                    candidate += ".cs";

                path = "Assets/Scripts/" + candidate;
            }

            path = path.Replace('\\', '/').Trim();
            if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                path = "Assets/Scripts/" + Path.GetFileName(path);

            if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Blocked fake/binary file write: " + path + ". Agent may only write .cs/.json/.txt.");

            string full = AssetToFull(path);
            string root = Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(full).StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Blocked path outside Assets/");

            Directory.CreateDirectory(Path.GetDirectoryName(full));

            if (File.Exists(full))
            {
                string project = Directory.GetParent(Application.dataPath).FullName;
                string backupDir = Path.Combine(project, "Library", "PrimatePanicAIBackups");
                Directory.CreateDirectory(backupDir);
                File.Copy(full, Path.Combine(backupDir, DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + Path.GetFileName(full)), true);
            }

            File.WriteAllText(full, action.content ?? "", new UTF8Encoding(false));
            return "Wrote " + path;
        }

        private static GameObject Resolve(string path, string sceneName)
        {
            if (string.IsNullOrWhiteSpace(path) || string.Equals(path, "ROOT", StringComparison.OrdinalIgnoreCase))
                return null;

            if (string.Equals(path, "SELECTED", StringComparison.OrdinalIgnoreCase))
            {
                GameObject selected = Selection.activeGameObject;
                if (selected == null)
                    return null;
                if (!string.IsNullOrWhiteSpace(sceneName) && !string.Equals(selected.scene.name, sceneName, StringComparison.OrdinalIgnoreCase))
                    return null;
                return selected;
            }

            string wanted = path.Trim().Trim('/');
            foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go == null || !go.scene.IsValid())
                    continue;
                if (!string.IsNullOrWhiteSpace(sceneName) && !string.Equals(go.scene.name, sceneName, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (string.Equals(go.name, wanted, StringComparison.OrdinalIgnoreCase) || string.Equals(GetHierarchyPath(go.transform).Trim('/'), wanted, StringComparison.OrdinalIgnoreCase))
                    return go;
            }

            return null;
        }

        private static void Require(GameObject go)
        {
            if (go == null)
                throw new InvalidOperationException("Target GameObject not found");
        }

        private static string GetHierarchyPath(Transform transform)
        {
            List<string> parts = new List<string>();
            while (transform != null)
            {
                parts.Add(transform.name);
                transform = transform.parent;
            }
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        private static string AssetToFull(string asset)
        {
            return Path.GetFullPath(Path.Combine(Directory.GetParent(Application.dataPath).FullName, asset.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static string CleanName(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "AI_Object" : value.Replace('/', '_').Replace('\\', '_').Trim();
        }

        private static string Describe(AgentAction action)
        {
            if (action == null)
                return "null action";

            string description = action.type ?? "unknown";
            if (!string.IsNullOrWhiteSpace(action.sceneName)) description += " [" + action.sceneName + "]";
            if (!string.IsNullOrWhiteSpace(action.uiType)) description += " " + action.uiType;
            if (!string.IsNullOrWhiteSpace(action.name)) description += " " + action.name;
            if (!string.IsNullOrWhiteSpace(action.componentType)) description += " " + action.componentType;
            if (!string.IsNullOrWhiteSpace(action.field)) description += "." + action.field;
            return description;
        }

        private static bool TryPrimitive(string value, out PrimitiveType type)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "sphere": type = PrimitiveType.Sphere; return true;
                case "capsule": type = PrimitiveType.Capsule; return true;
                case "cylinder": type = PrimitiveType.Cylinder; return true;
                case "plane": type = PrimitiveType.Plane; return true;
                case "quad": type = PrimitiveType.Quad; return true;
                case "cube": type = PrimitiveType.Cube; return true;
                default: type = PrimitiveType.Cube; return false;
            }
        }

        private void PickPicture()
        {
            string path = EditorUtility.OpenFilePanel("Pick reference picture", "", "png,jpg,jpeg");
            if (string.IsNullOrEmpty(path))
                return;

            byte[] data = File.ReadAllBytes(path);
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(data))
            {
                DestroyImmediate(texture);
                result = "Could not load picture";
                return;
            }

            if (preview != null)
                DestroyImmediate(preview);

            preview = texture;
            imagePath = path;
            result = "Picture loaded: " + Path.GetFileName(path);
        }

        private void ClearPicture()
        {
            imagePath = "";
            if (preview != null)
                DestroyImmediate(preview);
            preview = null;
        }

        private void SendVisionTest()
        {
            OllamaVisionRequest request = new OllamaVisionRequest
            {
                model = visionModel,
                prompt = "Reply exactly: VISION CONNECTED",
                stream = false,
                images = null,
                format = "",
                keep_alive = "15m",
                options = new OllamaOptions { num_ctx = 2048, num_predict = 40, temperature = 0 }
            };
            Send(JsonUtility.ToJson(request), 300, t => result = t);
        }

        private void SendPicture()
        {
            string base64 = ImageBase64(preview, 896);
            OllamaVisionRequest request = new OllamaVisionRequest
            {
                model = visionModel,
                stream = false,
                format = "json",
                images = new[] { base64 },
                keep_alive = "15m",
                options = new OllamaOptions { num_ctx = 8192, num_predict = 6000, temperature = .12f },
                prompt = "Analyze the image and return ONLY JSON. Rebuild the main visible object from Unity primitives. Ignore Unity UI/gizmos/bones. Schema: {\"message\":\"summary\",\"rootName\":\"AI_Recreation\",\"objects\":[{\"id\":\"p1\",\"parentId\":\"\",\"name\":\"Part\",\"primitive\":\"Cube\",\"position\":{\"x\":0,\"y\":1,\"z\":0},\"rotation\":{\"x\":0,\"y\":0,\"z\":0},\"scale\":{\"x\":1,\"y\":1,\"z\":1},\"color\":\"#808080\"}]}. Maximum 60 parts. USER: " + picturePrompt
            };

            Send(JsonUtility.ToJson(request), 600, HandlePicture);
        }

        private void HandlePicture(string text)
        {
            try
            {
                RecreationPlan plan = JsonUtility.FromJson<RecreationPlan>(ExtractJson(text));
                if (plan == null || plan.objects == null)
                {
                    result = "No usable 3D plan";
                    return;
                }

                GameObject root = new GameObject(string.IsNullOrWhiteSpace(plan.rootName) ? "AI_Recreation" : CleanName(plan.rootName));
                Undo.RegisterCreatedObjectUndo(root, "AI recreation");

                Dictionary<string, GameObject> made = new Dictionary<string, GameObject>();
                int count = 0;

                foreach (RecreationObject item in plan.objects)
                {
                    if (item == null || count >= 60)
                        break;

                    PrimitiveType primitiveType;
                    if (!TryPrimitive(item.primitive, out primitiveType))
                        primitiveType = PrimitiveType.Cube;

                    GameObject go = GameObject.CreatePrimitive(primitiveType);
                    go.name = CleanName(item.name);

                    Transform parentTransform = root.transform;
                    GameObject parent;
                    if (!string.IsNullOrEmpty(item.parentId) && made.TryGetValue(item.parentId, out parent))
                        parentTransform = parent.transform;

                    go.transform.SetParent(parentTransform, false);
                    go.transform.localPosition = ToVector3(item.position, Vector3.zero);
                    go.transform.localEulerAngles = ToVector3(item.rotation, Vector3.zero);
                    go.transform.localScale = ToVector3(item.scale, Vector3.one);

                    Renderer renderer = go.GetComponent<Renderer>();
                    Color color;
                    if (renderer != null && ColorUtility.TryParseHtmlString(item.color, out color))
                    {
                        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                        if (shader != null)
                        {
                            Material material = new Material(shader);
                            material.color = color;
                            renderer.sharedMaterial = material;
                        }
                    }

                    made[item.id ?? "p" + count] = go;
                    count++;
                }

                Selection.activeGameObject = root;
                result = "✅ Recreated " + count + " parts.";
            }
            catch (Exception ex)
            {
                result = "Picture plan failed: " + ex.Message + "\n" + text;
            }
        }

        private static Vector3 ToVector3(Vec3 value, Vector3 fallback)
        {
            return value == null ? fallback : new Vector3(value.x, value.y, value.z);
        }

        private static string ImageBase64(Texture2D source, int max)
        {
            float scale = Mathf.Min(1f, (float)max / Mathf.Max(source.width, source.height));
            int width = Mathf.Max(1, Mathf.RoundToInt(source.width * scale));
            int height = Mathf.Max(1, Mathf.RoundToInt(source.height * scale));

            RenderTexture renderTexture = RenderTexture.GetTemporary(width, height);
            RenderTexture old = RenderTexture.active;
            Graphics.Blit(source, renderTexture);
            RenderTexture.active = renderTexture;

            Texture2D texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            texture.Apply();

            RenderTexture.active = old;
            RenderTexture.ReleaseTemporary(renderTexture);

            byte[] jpg = texture.EncodeToJPG(82);
            DestroyImmediate(texture);
            return Convert.ToBase64String(jpg);
        }

        private void Send(string json, int timeout, Action<string> success)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                result = "Ollama URL is empty";
                return;
            }

            waiting = true;
            result = "Working locally...";
            Repaint();

            UnityWebRequest request = new UnityWebRequest(endpoint.Trim(), "POST");
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = timeout;

            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            operation.completed += _ =>
            {
                waiting = false;

                if (request.result != UnityWebRequest.Result.Success)
                    result = "OLLAMA FAILED\nHTTP " + request.responseCode + "\n" + request.error + "\n" + request.downloadHandler.text;
                else
                    success(ExtractOllama(request.downloadHandler.text));

                request.Dispose();
                Repaint();
            };
        }

        private static string ExtractOllama(string json)
        {
            OllamaResponse response = JsonUtility.FromJson<OllamaResponse>(json);
            if (response == null) return "Empty response";
            if (!string.IsNullOrEmpty(response.error)) return "OLLAMA ERROR: " + response.error;
            return response.response ?? "";
        }

        private static string ExtractJson(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException("Model returned an empty response.");

            string trimmed = text.Trim();
            int start = trimmed.IndexOf('{');
            int end = trimmed.LastIndexOf('}');

            if (start < 0 || end <= start)
                throw new InvalidOperationException("No complete JSON object found.");

            return trimmed.Substring(start, end - start + 1);
        }

        [Serializable]
        private class OllamaOptions
        {
            public int num_ctx;
            public int num_predict;
            public float temperature;
        }

        [Serializable]
        private class OllamaRequest
        {
            public string model;
            public string prompt;
            public bool stream;
            public string keep_alive;
            public OllamaOptions options;
        }

        [Serializable]
        private class OllamaAgentRequest
        {
            public string model;
            public string system;
            public string prompt;
            public bool stream;
            public string format;
            public string keep_alive;
            public OllamaOptions options;
        }

        [Serializable]
        private class OllamaVisionRequest
        {
            public string model;
            public string prompt;
            public bool stream;
            public string format;
            public string[] images;
            public string keep_alive;
            public OllamaOptions options;
        }

        [Serializable]
        private class OllamaResponse
        {
            public string response;
            public string error;
        }

        [Serializable]
        private class AgentPlan
        {
            public string message;
            public AgentAction[] actions;
        }

        [Serializable]
        private class AgentAction
        {
            public string type;
            public string name;
            public string sceneName;
            public string parentPath;
            public string targetPath;
            public string uiType;
            public string text;
            public string color;
            public string[] components;
            public string componentType;
            public string field;
            public string value;
            public string path;
            public string content;
            public string primitive;
            public bool boolValue;
            public float x;
            public float y;
            public float z;
            public float width;
            public float height;
            public int fontSize;
        }

        [Serializable]
        private class RecreationPlan
        {
            public string message;
            public string rootName;
            public RecreationObject[] objects;
        }

        [Serializable]
        private class RecreationObject
        {
            public string id;
            public string parentId;
            public string name;
            public string primitive;
            public Vec3 position;
            public Vec3 rotation;
            public Vec3 scale;
            public string color;
        }

        [Serializable]
        private class Vec3
        {
            public float x;
            public float y;
            public float z;
        }
    }
}
