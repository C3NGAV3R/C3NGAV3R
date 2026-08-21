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

            if (value.Length > 1200)
                value = value.Substring(0, 1200) + " ...";

            Entries.Add(value);
            while (Entries.Count > 24)
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
            EditorGUILayout.LabelField("Primate Panic AI - LOCAL v0.8", EditorStyles.boldLabel);

            int m = GUILayout.Toolbar((int)mode, new[] { "AGENT", "PLAN", "PICTURE → 3D" }, GUILayout.Height(30));
            if (m != (int)mode)
            {
                mode = (Mode)m;
                EditorPrefs.SetInt(ModePref, m);
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
                    ? "PLAN MODE: creates a structured plan only. Nothing changes until APPLY LAST PLAN."
                    : "AGENT MODE v0.8: creates real UI/components/scripts and repairs common imperfect AI actions automatically instead of failing on missing component names or paths.",
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
                    ? "Nothing selected. That's fine for CREATE requests; the Agent creates its own roots."
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
                    num_ctx = fastMode ? 5000 : 9000,
                    num_predict = fastMode ? 4200 : 7000,
                    temperature = .02f
                }
            };
            Send(JsonUtility.ToJson(request), 360, text => HandleAgent(text, apply));
        }

        private string BuildContext()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("USER REQUEST:\n" + prompt.Trim());

            GameObject go = Selection.activeGameObject;
            if (go == null)
            {
                sb.AppendLine("\nSELECTED GAMEOBJECT: NONE");
                sb.AppendLine("For new creation requests this is NOT a blocker. Create roots/components/UI as needed.");
            }
            else
            {
                sb.AppendLine("\nSELECTED GAMEOBJECT:");
                sb.AppendLine("Path: " + GetHierarchyPath(go.transform));
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

                    string src = File.ReadAllText(full);
                    int take = Mathf.Min(src.Length, maxChars - chars);
                    sb.AppendLine("SCRIPT " + asset + ":\n" + src.Substring(0, take));
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
                "You are an action-taking Unity Editor agent. Return ONLY valid JSON, never markdown. " +
                "DO the user's request instead of explaining manual steps when an action can do it. A missing selection is never a blocker for creating a NEW feature. " +
                "JSON schema: {\"message\":\"short summary\",\"actions\":[{\"type\":\"action\",\"name\":\"name\",\"parentPath\":\"ROOT or parent name/path\",\"targetPath\":\"SELECTED or object name/path; for set_component_field this may also be Canvas, CanvasScaler, RectTransform, Button, Image, Text, Slider or GraphicRaycaster to mean the most recently created object\",\"uiType\":\"canvas|panel|background|image|text|title|button|slider|eventsystem\",\"text\":\"visible text\",\"color\":\"#RRGGBB or #RRGGBBAA\",\"components\":[\"TypeName\"],\"componentType\":\"TypeName\",\"field\":\"field\",\"value\":\"value/reference\",\"path\":\"Assets/...\",\"content\":\"complete file content\",\"primitive\":\"Cube/Sphere/Capsule/Cylinder/Plane/Quad\",\"boolValue\":true,\"x\":0,\"y\":0,\"z\":0,\"width\":400,\"height\":80,\"fontSize\":36}]}. " +
                "SUPPORTED ACTIONS: create_ui, create_gameobject, create_or_replace_file, add_component, remove_component, set_active, set_local_position, set_local_rotation, set_local_scale, set_component_field. " +
                "CRITICAL UI RULE: NEVER build visible UI as empty create_gameobject placeholders. For Canvas, images, panels, backgrounds, titles, labels, buttons, sliders, loading text and EventSystem ALWAYS use create_ui. " +
                "Use uiType=image for a normal sized Image and uiType=background for a full-screen stretched Image. create_ui installs real components automatically. " +
                "For a main menu/loading screen create a Screen Space Overlay Canvas, full background, title, real PLAY/SETTINGS/QUIT Buttons, SettingsPanel, Slider, Back button, LoadingScreen/LoadingText and exactly one EventSystem. " +
                "Use width/height/x/y for layout instead of follow-up RectTransform edits whenever possible. Recommended: title y=300 width=900 height=120; play y=80 width=420 height=90; settings y=-30; quit y=-140. " +
                "If you DO use set_component_field, ALWAYS include componentType whenever possible. For Vector2 values use format x,y. For Vector3 use x,y,z. " +
                "For CanvasScaler use componentType CanvasScaler and fields uiScaleMode=ScaleWithScreenSize, referenceResolution=1920,1080, matchWidthOrHeight=0.5. Canvas renderMode should be ScreenSpaceOverlay. " +
                "create_gameobject is for NON-UI objects. Do not leave meaningless empty objects. " +
                "When runtime behavior is requested, create a COMPLETE compiling C# controller under Assets/Scripts/Name.cs. ALWAYS supply path. If you accidentally omit path the executor can repair it from name, but you should still provide it. " +
                "A brand-new custom MonoBehaviour cannot be attached until Unity recompiles; create it now and state one short follow-up run to attach/wire it. Built-in components may be attached immediately. " +
                "For existing broken scripts replace the exact path with a COMPLETE compiling file. Never use ellipses. Do not create duplicate Rigidbodies. Preserve Gorilla locomotion/XR unless explicitly asked. " +
                "Do not emit fake action types like message/note. Put explanations only in the top-level message. Prefer complete concrete plans over generic advice.";
        }

        private void HandleAgent(string text, bool apply)
        {
            try
            {
                AgentPlan plan = JsonUtility.FromJson<AgentPlan>(ExtractJson(text));
                if (plan == null)
                {
                    result = "No usable plan.\n" + text;
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
            catch (Exception ex)
            {
                result = "Agent plan parse failed: " + ex.Message + "\n\n" + text;
            }
        }

        private string ApplyPlan(AgentPlan plan)
        {
            StringBuilder sb = new StringBuilder("APPLYING:\n");
            bool files = false;
            GameObject lastCreated = null;
            GameObject lastCanvas = FindFirstSceneObjectWithComponent("Canvas");

            foreach (AgentAction a in plan.actions)
            {
                if (a == null)
                    continue;

                try
                {
                    string type = (a.type ?? "").Trim().ToLowerInvariant();
                    string line;

                    if (type == "message" || type == "note" || type == "explain")
                        continue;

                    if (type == "create_ui")
                    {
                        lastCreated = CreateUI(a, lastCanvas, out line);
                        if (string.Equals((a.uiType ?? "").Trim(), "canvas", StringComparison.OrdinalIgnoreCase))
                            lastCanvas = lastCreated;
                    }
                    else if (type == "create_gameobject")
                    {
                        lastCreated = CreateGameObject(a, out line);
                    }
                    else if (type == "create_or_replace_file")
                    {
                        line = WriteFile(a);
                        files = true;
                    }
                    else
                    {
                        GameObject target = ResolveActionTarget(a, lastCreated);
                        if (type == "add_component")
                            line = AddComponent(target, InferComponentType(a));
                        else if (type == "remove_component")
                            line = RemoveComponent(target, InferComponentType(a));
                        else if (type == "set_active")
                            line = SetActive(target, a.boolValue);
                        else if (type == "set_local_position")
                            line = SetTransform(target, "position", a);
                        else if (type == "set_local_rotation")
                            line = SetTransform(target, "rotation", a);
                        else if (type == "set_local_scale")
                            line = SetTransform(target, "scale", a);
                        else if (type == "set_component_field")
                            line = SetComponentField(target, a);
                        else
                            line = "Skipped unknown action " + a.type;
                    }

                    sb.AppendLine("✅ " + line);
                }
                catch (Exception ex)
                {
                    sb.AppendLine("❌ " + Describe(a) + " -> " + ex.Message);
                }
            }

            if (files)
                AssetDatabase.Refresh();

            GameObject dirty = Selection.activeGameObject ?? lastCreated;
            if (dirty != null && dirty.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(dirty.scene);

            sb.AppendLine("Done. v0.8 repairs common missing action fields automatically. If a new custom C# script was created, let Unity compile before asking Agent to attach that custom script.");
            return sb.ToString();
        }

        private static GameObject ResolveActionTarget(AgentAction a, GameObject lastCreated)
        {
            GameObject target = Resolve(a.targetPath);
            if (target != null)
                return target;

            if (LooksLikeComponentName(a.targetPath))
                return lastCreated ?? Selection.activeGameObject;

            return Selection.activeGameObject ?? lastCreated;
        }

        private static string InferComponentType(AgentAction a)
        {
            if (!string.IsNullOrWhiteSpace(a.componentType))
                return a.componentType.Trim();

            if (LooksLikeComponentName(a.targetPath))
                return a.targetPath.Trim();

            if (LooksLikeComponentName(a.name))
                return a.name.Trim();

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
                "EventSystem", "StandaloneInputModule", "InputSystemUIInputModule", "Rigidbody", "Collider", "BoxCollider",
                "SphereCollider", "CapsuleCollider", "AudioSource", "Animator", "Camera"
            };

            for (int i = 0; i < common.Length; i++)
                if (string.Equals(v, common[i], StringComparison.OrdinalIgnoreCase))
                    return true;

            return v.StartsWith("UnityEngine.", StringComparison.OrdinalIgnoreCase);
        }

        private static GameObject CreateGameObject(AgentAction a, out string message)
        {
            string name = CleanName(string.IsNullOrWhiteSpace(a.name) ? "AI_Object" : a.name);
            GameObject go;

            if (!string.IsNullOrWhiteSpace(a.primitive))
            {
                PrimitiveType pt;
                if (!TryPrimitive(a.primitive, out pt))
                    pt = PrimitiveType.Cube;
                go = GameObject.CreatePrimitive(pt);
                go.name = name;
            }
            else
            {
                go = new GameObject(name);
            }

            Undo.RegisterCreatedObjectUndo(go, "AI create GameObject");
            GameObject parent = Resolve(a.parentPath);
            if (parent != null)
                go.transform.SetParent(parent.transform, false);

            go.transform.localPosition = new Vector3(a.x, a.y, a.z);

            if (a.components != null)
            {
                foreach (string c in a.components)
                    if (!string.IsNullOrWhiteSpace(c))
                        TryAddComponent(go, c);
            }

            Selection.activeGameObject = go;
            message = "Created " + go.name + (a.components == null ? "" : " with components");
            return go;
        }

        private static GameObject CreateUI(AgentAction a, GameObject fallbackCanvas, out string message)
        {
            string kind = (a.uiType ?? "").Trim().ToLowerInvariant();
            if (kind == "label") kind = "text";
            if (kind == "menu") kind = "panel";
            if (kind == "img") kind = "image";

            string name = CleanName(string.IsNullOrWhiteSpace(a.name) ? ("AI_" + kind) : a.name);

            if (kind == "eventsystem")
            {
                GameObject existing = FindFirstSceneObjectWithComponent("EventSystem");
                if (existing != null)
                {
                    Selection.activeGameObject = existing;
                    message = "Reused existing EventSystem '" + existing.name + "'";
                    return existing;
                }

                GameObject es = new GameObject(name);
                Undo.RegisterCreatedObjectUndo(es, "AI create EventSystem");
                TryAddComponent(es, "UnityEngine.EventSystems.EventSystem");
                if (!TryAddComponent(es, "UnityEngine.InputSystem.UI.InputSystemUIInputModule"))
                    TryAddComponent(es, "UnityEngine.EventSystems.StandaloneInputModule");
                Selection.activeGameObject = es;
                message = "Created functional EventSystem";
                return es;
            }

            GameObject go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "AI create UI");

            GameObject parent = Resolve(a.parentPath);
            if (parent == null && kind != "canvas")
                parent = fallbackCanvas;
            if (parent != null)
                go.transform.SetParent(parent.transform, false);

            RectTransform rt = (RectTransform)go.transform;
            rt.localScale = Vector3.one;

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
                message = "Created Canvas + CanvasScaler + GraphicRaycaster";
                return go;
            }

            if (kind == "background")
            {
                StretchFull(rt);
                AddImage(go, string.IsNullOrWhiteSpace(a.color) ? "#101014FF" : a.color);
            }
            else if (kind == "image")
            {
                if ((a.width <= 0 && a.height <= 0) && name.IndexOf("background", StringComparison.OrdinalIgnoreCase) >= 0)
                    StretchFull(rt);
                else
                    SetRect(rt, a, 600, 320);
                AddImage(go, string.IsNullOrWhiteSpace(a.color) ? "#202028FF" : a.color);
            }
            else if (kind == "panel")
            {
                SetRect(rt, a, 760, 500);
                AddImage(go, string.IsNullOrWhiteSpace(a.color) ? "#17171DEB" : a.color);
            }
            else if (kind == "text" || kind == "title")
            {
                SetRect(rt, a, kind == "title" ? 900 : 600, kind == "title" ? 120 : 60);
                AddText(go,
                    string.IsNullOrWhiteSpace(a.text) ? name : a.text,
                    a.fontSize > 0 ? a.fontSize : (kind == "title" ? 64 : 30),
                    a.color);
            }
            else if (kind == "button")
            {
                SetRect(rt, a, 420, 90);
                Component image = AddImage(go, string.IsNullOrWhiteSpace(a.color) ? "#292933FF" : a.color);
                Component button = AddByName(go, "UnityEngine.UI.Button");
                if (button != null && image != null)
                    SetMember(button, "targetGraphic", image);
                CreateButtonLabel(go,
                    string.IsNullOrWhiteSpace(a.text) ? name.Replace("Button", "") : a.text,
                    a.fontSize > 0 ? a.fontSize : 34);
            }
            else if (kind == "slider")
            {
                SetRect(rt, a, 520, 50);
                BuildSlider(go, a.color);
            }
            else
            {
                throw new InvalidOperationException("Unknown uiType: " + a.uiType + ". Supported: canvas, background, image, panel, text, title, button, slider, eventsystem");
            }

            if (a.components != null)
            {
                foreach (string extra in a.components)
                    if (!string.IsNullOrWhiteSpace(extra))
                        TryAddComponent(go, extra);
            }

            Selection.activeGameObject = go;
            message = "Created real UI " + kind + " '" + go.name + "' with components";
            return go;
        }

        private static void StretchFull(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(.5f, .5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;
        }

        private static void SetRect(RectTransform rt, AgentAction a, float defaultW, float defaultH)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(.5f, .5f);
            rt.pivot = new Vector2(.5f, .5f);
            rt.anchoredPosition = new Vector2(a.x, a.y);
            rt.sizeDelta = new Vector2(a.width > 0 ? a.width : defaultW, a.height > 0 ? a.height : defaultH);
        }

        private static Component AddImage(GameObject go, string color)
        {
            Component c = AddByName(go, "UnityEngine.UI.Image");
            if (c != null)
                SetMember(c, "color", ParseColor(color, new Color(.15f, .15f, .18f, 1f)));
            return c;
        }

        private static Component AddText(GameObject go, string text, int size, string color)
        {
            Component c = AddByName(go, "UnityEngine.UI.Text");
            if (c == null)
                return null;

            SetMember(c, "text", text);
            SetMember(c, "fontSize", size);
            SetEnumMember(c, "alignment", "MiddleCenter");
            SetMember(c, "color", ParseColor(color, Color.white));

            try
            {
                Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (font != null)
                    SetMember(c, "font", font);
            }
            catch
            {
            }

            return c;
        }

        private static void CreateButtonLabel(GameObject button, string label, int size)
        {
            GameObject textGo = new GameObject("Text", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(textGo, "AI button label");
            textGo.transform.SetParent(button.transform, false);
            RectTransform rt = (RectTransform)textGo.transform;
            StretchFull(rt);
            AddText(textGo, label, size, "#FFFFFFFF");
        }

        private static void BuildSlider(GameObject root, string accent)
        {
            Component slider = AddByName(root, "UnityEngine.UI.Slider");

            GameObject bg = CreateUiChild(root, "Background");
            RectTransform bgr = (RectTransform)bg.transform;
            bgr.anchorMin = new Vector2(0, .25f);
            bgr.anchorMax = new Vector2(1, .75f);
            bgr.offsetMin = Vector2.zero;
            bgr.offsetMax = Vector2.zero;
            AddImage(bg, "#3A3A42FF");

            GameObject fillArea = CreateUiChild(root, "Fill Area");
            RectTransform far = (RectTransform)fillArea.transform;
            far.anchorMin = new Vector2(0, .25f);
            far.anchorMax = new Vector2(1, .75f);
            far.offsetMin = new Vector2(8, 0);
            far.offsetMax = new Vector2(-8, 0);

            GameObject fill = CreateUiChild(fillArea, "Fill");
            RectTransform fr = (RectTransform)fill.transform;
            StretchFull(fr);
            AddImage(fill, string.IsNullOrWhiteSpace(accent) ? "#61D7FFFF" : accent);

            GameObject handleArea = CreateUiChild(root, "Handle Slide Area");
            RectTransform har = (RectTransform)handleArea.transform;
            har.anchorMin = Vector2.zero;
            har.anchorMax = Vector2.one;
            har.offsetMin = new Vector2(10, 0);
            har.offsetMax = new Vector2(-10, 0);

            GameObject handle = CreateUiChild(handleArea, "Handle");
            RectTransform hr = (RectTransform)handle.transform;
            hr.sizeDelta = new Vector2(28, 42);
            Component handleImage = AddImage(handle, "#FFFFFFFF");

            if (slider != null)
            {
                SetMember(slider, "fillRect", fr);
                SetMember(slider, "handleRect", hr);
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
            Type t = FindType(typeName);
            if (t == null)
                throw new InvalidOperationException("Type not found: " + typeName);
            Component c = target.GetComponent(t);
            if (c == null)
                return target.name + " did not have " + typeName;
            Undo.DestroyObjectImmediate(c);
            return "Removed " + typeName;
        }

        private static bool TryAddComponent(GameObject go, string typeName)
        {
            Type t = FindType(typeName);
            if (t == null || !typeof(Component).IsAssignableFrom(t))
                return false;
            if (go.GetComponent(t) != null)
                return true;
            try
            {
                Undo.AddComponent(go, t);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static Component AddByName(GameObject go, string typeName)
        {
            Type t = FindType(typeName);
            if (t == null || !typeof(Component).IsAssignableFrom(t))
                return null;
            Component existing = go.GetComponent(t);
            if (existing != null)
                return existing;
            try
            {
                return Undo.AddComponent(go, t);
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

            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type direct = asm.GetType(trimmed, false, true) ?? asm.GetType(expanded, false, true);
                if (direct != null)
                    return direct;

                try
                {
                    foreach (Type t in asm.GetTypes())
                    {
                        if (string.Equals(t.Name, trimmed, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(t.FullName, expanded, StringComparison.OrdinalIgnoreCase))
                            return t;
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

        private static GameObject FindFirstSceneObjectWithComponent(string typeName)
        {
            Type t = FindType(typeName);
            if (t == null)
                return null;

            foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go == null || !go.scene.IsValid())
                    continue;
                if (go.GetComponent(t) != null)
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

        private static string SetTransform(GameObject target, string kind, AgentAction a)
        {
            Require(target);
            Undo.RecordObject(target.transform, "AI transform");
            Vector3 v = new Vector3(a.x, a.y, a.z);
            if (kind == "position") target.transform.localPosition = v;
            else if (kind == "rotation") target.transform.localEulerAngles = v;
            else target.transform.localScale = v;
            EditorUtility.SetDirty(target.transform);
            return "Set " + kind + " on " + target.name;
        }

        private static string SetComponentField(GameObject target, AgentAction a)
        {
            Require(target);

            string componentType = InferComponentType(a);
            if (string.IsNullOrWhiteSpace(componentType))
                throw new InvalidOperationException("Component type missing. Use componentType or targetPath such as RectTransform/Canvas/CanvasScaler.");

            Type type = FindType(componentType);
            if (type == null)
                throw new InvalidOperationException("Type not found: " + componentType);

            Component comp = target.GetComponent(type);
            if (comp == null)
                throw new InvalidOperationException("Component missing on '" + target.name + "': " + componentType);

            string fieldName = a.field ?? "";
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            FieldInfo field = type.GetField(fieldName, flags);
            PropertyInfo property = field == null ? type.GetProperty(fieldName, flags) : null;

            if (field == null && (property == null || !property.CanWrite))
                throw new InvalidOperationException("Writable field/property not found: " + componentType + "." + fieldName);

            Type valueType = field != null ? field.FieldType : property.PropertyType;
            object value = ConvertValue(a.value, valueType);

            Undo.RecordObject(comp, "AI set field");
            if (field != null)
                field.SetValue(comp, value);
            else
                property.SetValue(comp, value, null);

            EditorUtility.SetDirty(comp);
            return "Set " + componentType + "." + fieldName + " on " + target.name;
        }

        private static object ConvertValue(string raw, Type t)
        {
            raw = raw ?? "";
            string cleaned = raw.Trim();

            if (t == typeof(string)) return raw;
            if (t == typeof(bool)) return bool.Parse(cleaned);
            if (t == typeof(int)) return int.Parse(cleaned, CultureInfo.InvariantCulture);
            if (t == typeof(float)) return float.Parse(cleaned, CultureInfo.InvariantCulture);
            if (t == typeof(double)) return double.Parse(cleaned, CultureInfo.InvariantCulture);

            if (t == typeof(Vector2))
            {
                string[] p = cleaned.Trim('(', ')', '[', ']').Split(',');
                if (p.Length != 2)
                    throw new FormatException("Vector2 value must be x,y");
                return new Vector2(
                    float.Parse(p[0], CultureInfo.InvariantCulture),
                    float.Parse(p[1], CultureInfo.InvariantCulture));
            }

            if (t == typeof(Vector3))
            {
                string[] p = cleaned.Trim('(', ')', '[', ']').Split(',');
                if (p.Length != 3)
                    throw new FormatException("Vector3 value must be x,y,z");
                return new Vector3(
                    float.Parse(p[0], CultureInfo.InvariantCulture),
                    float.Parse(p[1], CultureInfo.InvariantCulture),
                    float.Parse(p[2], CultureInfo.InvariantCulture));
            }

            if (t == typeof(Color))
                return ParseColor(cleaned, Color.white);

            if (t.IsEnum)
            {
                string enumValue = cleaned.Replace(" ", "");
                return Enum.Parse(t, enumValue, true);
            }

            if (typeof(UnityEngine.Object).IsAssignableFrom(t))
            {
                if (string.Equals(cleaned, "NONE", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(cleaned, "NULL", StringComparison.OrdinalIgnoreCase))
                    return null;

                GameObject go = Resolve(cleaned);
                if (go == null)
                    throw new InvalidOperationException("Reference not found: " + cleaned);
                if (t == typeof(GameObject)) return go;
                if (t == typeof(Transform)) return go.transform;

                Component found = go.GetComponent(t);
                if (found == null)
                    throw new InvalidOperationException("Reference object '" + go.name + "' has no " + t.Name);
                return found;
            }

            throw new InvalidOperationException("Unsupported value type " + t.Name);
        }

        private static void SetMember(object obj, string name, object value)
        {
            if (obj == null)
                return;

            Type t = obj.GetType();
            FieldInfo f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null)
            {
                try { f.SetValue(obj, value); } catch { }
                return;
            }

            PropertyInfo p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null && p.CanWrite)
            {
                try { p.SetValue(obj, value, null); } catch { }
            }
        }

        private static void SetEnumMember(object obj, string name, string enumName)
        {
            if (obj == null)
                return;

            Type t = obj.GetType();
            FieldInfo f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            PropertyInfo p = f == null ? t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) : null;
            Type et = f != null ? f.FieldType : p != null ? p.PropertyType : null;
            if (et == null || !et.IsEnum)
                return;

            object v = Enum.Parse(et, enumName, true);
            if (f != null) f.SetValue(obj, v);
            else if (p != null && p.CanWrite) p.SetValue(obj, v, null);
        }

        private static Color ParseColor(string hex, Color fallback)
        {
            Color c;
            return !string.IsNullOrWhiteSpace(hex) && ColorUtility.TryParseHtmlString(hex.Trim(), out c) ? c : fallback;
        }

        private static string WriteFile(AgentAction a)
        {
            string path = a.path;

            if (string.IsNullOrWhiteSpace(path))
            {
                string candidate = !string.IsNullOrWhiteSpace(a.name) ? a.name.Trim() : a.targetPath;
                if (string.IsNullOrWhiteSpace(candidate))
                    throw new InvalidOperationException("Missing path and filename");

                candidate = Path.GetFileName(candidate.Replace('\\', '/'));
                if (!candidate.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) &&
                    !candidate.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
                    !candidate.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                {
                    candidate += ".cs";
                }

                path = "Assets/Scripts/" + candidate;
            }

            path = path.Replace('\\', '/').Trim();
            if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                path = "Assets/Scripts/" + Path.GetFileName(path);

            if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unsupported file type: " + path);

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
                File.Copy(
                    full,
                    Path.Combine(backupDir, DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + Path.GetFileName(full)),
                    true);
            }

            File.WriteAllText(full, a.content ?? "", new UTF8Encoding(false));
            return "Wrote " + path;
        }

        private static GameObject Resolve(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || string.Equals(path, "ROOT", StringComparison.OrdinalIgnoreCase))
                return null;
            if (string.Equals(path, "SELECTED", StringComparison.OrdinalIgnoreCase))
                return Selection.activeGameObject;

            string wanted = path.Trim().Trim('/');
            foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go == null || !go.scene.IsValid())
                    continue;
                if (string.Equals(go.name, wanted, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(GetHierarchyPath(go.transform).Trim('/'), wanted, StringComparison.OrdinalIgnoreCase))
                    return go;
            }
            return null;
        }

        private static void Require(GameObject go)
        {
            if (go == null)
                throw new InvalidOperationException("Target GameObject not found");
        }

        private static string GetHierarchyPath(Transform t)
        {
            List<string> parts = new List<string>();
            while (t != null)
            {
                parts.Add(t.name);
                t = t.parent;
            }
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        private static string AssetToFull(string asset)
        {
            return Path.GetFullPath(Path.Combine(
                Directory.GetParent(Application.dataPath).FullName,
                asset.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static string CleanName(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "AI_Object" : value.Replace('/', '_').Replace('\\', '_').Trim();
        }

        private static string Describe(AgentAction a)
        {
            if (a == null) return "null action";
            string s = a.type ?? "unknown";
            if (!string.IsNullOrWhiteSpace(a.uiType)) s += " " + a.uiType;
            if (!string.IsNullOrWhiteSpace(a.name)) s += " " + a.name;
            if (!string.IsNullOrWhiteSpace(a.componentType)) s += " " + a.componentType;
            else if (LooksLikeComponentName(a.targetPath)) s += " " + a.targetPath;
            if (!string.IsNullOrWhiteSpace(a.field)) s += "." + a.field;
            return s;
        }

        private static bool TryPrimitive(string value, out PrimitiveType t)
        {
            switch ((value ?? "").ToLowerInvariant())
            {
                case "sphere": t = PrimitiveType.Sphere; return true;
                case "capsule": t = PrimitiveType.Capsule; return true;
                case "cylinder": t = PrimitiveType.Cylinder; return true;
                case "plane": t = PrimitiveType.Plane; return true;
                case "quad": t = PrimitiveType.Quad; return true;
                case "cube": t = PrimitiveType.Cube; return true;
                default: t = PrimitiveType.Cube; return false;
            }
        }

        private void PickPicture()
        {
            string p = EditorUtility.OpenFilePanel("Pick reference picture", "", "png,jpg,jpeg");
            if (string.IsNullOrEmpty(p))
                return;

            byte[] data = File.ReadAllBytes(p);
            Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(data))
            {
                DestroyImmediate(tex);
                result = "Could not load picture";
                return;
            }

            if (preview != null)
                DestroyImmediate(preview);
            preview = tex;
            imagePath = p;
            result = "Picture loaded: " + Path.GetFileName(p);
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
            OllamaVisionRequest r = new OllamaVisionRequest
            {
                model = visionModel,
                prompt = "Reply exactly: VISION CONNECTED",
                stream = false,
                images = null,
                format = "",
                keep_alive = "15m",
                options = new OllamaOptions { num_ctx = 2048, num_predict = 40, temperature = 0 }
            };
            Send(JsonUtility.ToJson(r), 300, t => result = t);
        }

        private void SendPicture()
        {
            string b64 = ImageBase64(preview, 896);
            OllamaVisionRequest r = new OllamaVisionRequest
            {
                model = visionModel,
                stream = false,
                format = "json",
                images = new[] { b64 },
                keep_alive = "15m",
                options = new OllamaOptions { num_ctx = 8192, num_predict = 6000, temperature = .12f },
                prompt = "Analyze the image and return ONLY JSON. Rebuild the main visible object from Unity primitives. Ignore Unity UI/gizmos/bones. Schema: {\"message\":\"summary\",\"rootName\":\"AI_Recreation\",\"objects\":[{\"id\":\"p1\",\"parentId\":\"\",\"name\":\"Part\",\"primitive\":\"Cube\",\"position\":{\"x\":0,\"y\":1,\"z\":0},\"rotation\":{\"x\":0,\"y\":0,\"z\":0},\"scale\":{\"x\":1,\"y\":1,\"z\":1},\"color\":\"#808080\"}]}. Maximum 60 parts. USER: " + picturePrompt
            };
            Send(JsonUtility.ToJson(r), 600, HandlePicture);
        }

        private void HandlePicture(string text)
        {
            try
            {
                RecreationPlan p = JsonUtility.FromJson<RecreationPlan>(ExtractJson(text));
                if (p == null || p.objects == null)
                {
                    result = "No usable 3D plan";
                    return;
                }

                GameObject root = new GameObject(string.IsNullOrWhiteSpace(p.rootName) ? "AI_Recreation" : CleanName(p.rootName));
                Undo.RegisterCreatedObjectUndo(root, "AI recreation");
                Dictionary<string, GameObject> made = new Dictionary<string, GameObject>();
                int n = 0;

                foreach (RecreationObject o in p.objects)
                {
                    if (o == null || n >= 60)
                        break;

                    PrimitiveType pt;
                    if (!TryPrimitive(o.primitive, out pt))
                        pt = PrimitiveType.Cube;

                    GameObject go = GameObject.CreatePrimitive(pt);
                    go.name = CleanName(o.name);
                    Transform par = root.transform;
                    GameObject parent;
                    if (!string.IsNullOrEmpty(o.parentId) && made.TryGetValue(o.parentId, out parent))
                        par = parent.transform;

                    go.transform.SetParent(par, false);
                    go.transform.localPosition = V(o.position, Vector3.zero);
                    go.transform.localEulerAngles = V(o.rotation, Vector3.zero);
                    go.transform.localScale = V(o.scale, Vector3.one);

                    Renderer rr = go.GetComponent<Renderer>();
                    Color cc;
                    if (rr != null && ColorUtility.TryParseHtmlString(o.color, out cc))
                    {
                        Shader s = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                        if (s != null)
                        {
                            Material mat = new Material(s);
                            mat.color = cc;
                            rr.sharedMaterial = mat;
                        }
                    }

                    made[o.id ?? ("p" + n)] = go;
                    n++;
                }

                Selection.activeGameObject = root;
                result = "✅ Recreated " + n + " parts.";
            }
            catch (Exception ex)
            {
                result = "Picture plan failed: " + ex.Message + "\n" + text;
            }
        }

        private static Vector3 V(Vec3 v, Vector3 fallback)
        {
            return v == null ? fallback : new Vector3(v.x, v.y, v.z);
        }

        private static string ImageBase64(Texture2D src, int max)
        {
            float scale = Mathf.Min(1f, (float)max / Mathf.Max(src.width, src.height));
            int w = Mathf.Max(1, Mathf.RoundToInt(src.width * scale));
            int h = Mathf.Max(1, Mathf.RoundToInt(src.height * scale));

            RenderTexture rt = RenderTexture.GetTemporary(w, h);
            RenderTexture old = RenderTexture.active;
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;

            Texture2D t = new Texture2D(w, h, TextureFormat.RGB24, false);
            t.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            t.Apply();
            RenderTexture.active = old;
            RenderTexture.ReleaseTemporary(rt);

            byte[] jpg = t.EncodeToJPG(82);
            DestroyImmediate(t);
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

            UnityWebRequest req = new UnityWebRequest(endpoint.Trim(), "POST");
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = timeout;

            UnityWebRequestAsyncOperation op = req.SendWebRequest();
            op.completed += _ =>
            {
                waiting = false;
                if (req.result != UnityWebRequest.Result.Success)
                    result = "OLLAMA FAILED\nHTTP " + req.responseCode + "\n" + req.error + "\n" + req.downloadHandler.text;
                else
                    success(ExtractOllama(req.downloadHandler.text));

                req.Dispose();
                Repaint();
            };
        }

        private static string ExtractOllama(string json)
        {
            OllamaResponse r = JsonUtility.FromJson<OllamaResponse>(json);
            if (r == null) return "Empty response";
            if (!string.IsNullOrEmpty(r.error)) return "OLLAMA ERROR: " + r.error;
            return r.response ?? "";
        }

        private static string ExtractJson(string text)
        {
            string t = text.Trim();
            int s = t.IndexOf('{');
            int e = t.LastIndexOf('}');
            if (s < 0 || e <= s)
                throw new InvalidOperationException("No JSON object found");
            return t.Substring(s, e - s + 1);
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
