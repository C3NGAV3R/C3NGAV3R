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
    public class PrimatePanicAISmartWindow : EditorWindow
    {
        private const string EndpointPref = "PrimatePanicAI.Smart.Endpoint";
        private const string ModelPref = "PrimatePanicAI.Smart.Model";
        private const string PlanOnlyPref = "PrimatePanicAI.Smart.PlanOnly";

        private string endpoint = "http://127.0.0.1:11434/api/generate";
        private string model = "qwen3:8b";
        private bool planOnly;
        private bool working;
        private string prompt = "";
        private string result = "SMART BRAIN ready.";
        private Vector2 scroll;

        [MenuItem("Tools/Primate Panic AI - SMART BRAIN")]
        public static void Open()
        {
            GetWindow<PrimatePanicAISmartWindow>("Primate Panic AI SMART");
        }

        private void OnEnable()
        {
            endpoint = EditorPrefs.GetString(EndpointPref, endpoint);
            model = EditorPrefs.GetString(ModelPref, model);
            planOnly = EditorPrefs.GetBool(PlanOnlyPref, false);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Primate Panic AI - SMART BRAIN v1.0.3", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Planner -> reviewer -> executor. v1.0.3 prevents useful plans being wiped to zero actions and adds direct World Space Canvas support.",
                MessageType.Info);

            EditorGUI.BeginChangeCheck();
            endpoint = EditorGUILayout.TextField("Ollama URL", endpoint);
            model = EditorGUILayout.TextField("Reasoning Model", model);
            planOnly = EditorGUILayout.ToggleLeft("PLAN ONLY (do not change Unity)", planOnly);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetString(EndpointPref, endpoint);
                EditorPrefs.SetString(ModelPref, model);
                EditorPrefs.SetBool(PlanOnlyPref, planOnly);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Use Qwen3 8B", GUILayout.Height(26)))
                {
                    model = "qwen3:8b";
                    EditorPrefs.SetString(ModelPref, model);
                }

                if (GUILayout.Button("Test Brain", GUILayout.Height(26)))
                    TestBrain();
            }

            GameObject selected = Selection.activeGameObject;
            EditorGUILayout.HelpBox(
                selected == null
                    ? "Nothing selected. Creation and rebuild requests can still run."
                    : "Selected: " + GetHierarchyPath(selected.transform),
                MessageType.None);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("What should the SMART agent do?", EditorStyles.boldLabel);
            prompt = EditorGUILayout.TextArea(prompt, GUILayout.MinHeight(130));

            GUI.enabled = !working && !string.IsNullOrWhiteSpace(prompt) && !string.IsNullOrWhiteSpace(model);
            if (GUILayout.Button(working ? "SMART BRAIN IS THINKING..." : planOnly ? "BUILD REVIEWED PLAN" : "RUN SMART AGENT", GUILayout.Height(42)))
                RunSmartAgent();
            GUI.enabled = true;

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Result", EditorStyles.boldLabel);
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MinHeight(220));
            EditorGUILayout.TextArea(result, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private void TestBrain()
        {
            GenerateRequest request = NewRequest(
                "Return JSON only.",
                "Return exactly this JSON object: {\"message\":\"SMART BRAIN CONNECTED\",\"actions\":[]}",
                64);

            Send(request, 300, text =>
            {
                try
                {
                    AgentPlan p = ParsePlan(text);
                    result = p != null && (p.message ?? "").Contains("SMART BRAIN CONNECTED")
                        ? "SMART BRAIN CONNECTED ✅"
                        : "Brain replied:\n" + text;
                }
                catch (Exception ex)
                {
                    result = "Brain test parse failed: " + ex.Message + "\n\n" + text;
                }
            });
        }

        private void RunSmartAgent()
        {
            string context = BuildContext();
            result = "1/3 SMART PLANNER is reasoning...";
            Repaint();

            GenerateRequest planner = NewRequest(BuildPlannerSystem(), context, 5000);
            Send(planner, 600, plannerText =>
            {
                AgentPlan first;
                try
                {
                    first = ParsePlan(plannerText);
                }
                catch (Exception ex)
                {
                    result = "Planner output could not be parsed: " + ex.Message + "\n\n" + plannerText;
                    return;
                }

                if (HasActions(first))
                {
                    ReviewAndExecute(first, context, false);
                    return;
                }

                result = "Planner returned 0 actions. SMART RECOVERY is forcing an executable plan...";
                Repaint();

                string rescuePrompt = context +
                    "\n\nIMPORTANT RECOVERY: The previous planner returned zero actions. The user gave an actionable Unity create/modify/rebuild request. " +
                    "Zero actions is invalid. Produce concrete supported actions that actually perform the request. Do not merely explain it.";

                GenerateRequest rescue = NewRequest(BuildPlannerSystem() +
                    " For any actionable create, modify, rebuild, fix, add, remove, scene, UI, script, or setup request, actions MUST contain at least one executable action.",
                    rescuePrompt,
                    5000);

                Send(rescue, 600, rescueText =>
                {
                    AgentPlan recovered;
                    try
                    {
                        recovered = ParsePlan(rescueText);
                    }
                    catch (Exception ex)
                    {
                        result = "Recovery plan could not be parsed: " + ex.Message + "\n\n" + rescueText;
                        return;
                    }

                    if (!HasActions(recovered))
                    {
                        result = "SMART BRAIN still returned 0 actions after recovery. Try a shorter request or select the object you want changed.";
                        return;
                    }

                    ReviewAndExecute(recovered, context, true);
                });
            });
        }

        private void ReviewAndExecute(AgentPlan first, string context, bool recovered)
        {
            int firstCount = first.actions == null ? 0 : first.actions.Length;
            result = "2/3 SMART REVIEWER is checking " + firstCount + " actions...";
            Repaint();

            string reviewPrompt =
                "ORIGINAL USER REQUEST:\n" + prompt.Trim() +
                "\n\nUNITY CONTEXT:\n" + context +
                "\n\nPLANNER PLAN TO REVIEW:\n" + JsonUtility.ToJson(first, true) +
                "\n\nReturn ONLY corrected JSON in the same schema. Fix bad actions instead of deleting the whole plan. " +
                "If the incoming plan has executable actions and the user requested a change, your final actions array MUST NOT be empty.";

            GenerateRequest reviewer = NewRequest(BuildReviewerSystem(), reviewPrompt, 5000);
            Send(reviewer, 600, reviewedText =>
            {
                AgentPlan finalPlan = null;
                string fallbackReason = "";

                try
                {
                    finalPlan = ParsePlan(reviewedText);
                }
                catch (Exception ex)
                {
                    fallbackReason = "Reviewer JSON failed (" + ex.Message + "). Using planner plan.";
                }

                if (!HasActions(finalPlan) && HasActions(first))
                {
                    if (string.IsNullOrEmpty(fallbackReason))
                        fallbackReason = "Reviewer tried to erase all actions. Using the valid planner plan instead.";
                    finalPlan = first;
                }

                if (!HasActions(finalPlan))
                {
                    result = "No executable actions survived planning/review.";
                    return;
                }

                int count = finalPlan.actions.Length;
                StringBuilder sb = new StringBuilder();
                sb.AppendLine(string.IsNullOrWhiteSpace(finalPlan.message) ? "Reviewed plan ready." : finalPlan.message);
                sb.AppendLine("Reviewed actions: " + count);
                if (recovered)
                    sb.AppendLine("SMART RECOVERY: planner was automatically regenerated because the first plan was empty.");
                if (!string.IsNullOrEmpty(fallbackReason))
                    sb.AppendLine("SMART SAFETY FALLBACK: " + fallbackReason);

                if (planOnly)
                {
                    sb.AppendLine("\nPLAN ONLY — nothing changed.");
                    for (int i = 0; i < finalPlan.actions.Length; i++)
                        sb.AppendLine((i + 1) + ". " + Describe(finalPlan.actions[i]));
                    result = sb.ToString();
                    return;
                }

                result = "3/3 EXECUTOR is applying " + count + " actions...";
                Repaint();
                sb.AppendLine();
                sb.AppendLine(ApplyPlan(finalPlan));
                result = sb.ToString();
            });
        }

        private static bool HasActions(AgentPlan plan)
        {
            return plan != null && plan.actions != null && plan.actions.Length > 0;
        }

        private GenerateRequest NewRequest(string system, string userPrompt, int numPredict)
        {
            return new GenerateRequest
            {
                model = model.Trim(),
                system = system,
                prompt = userPrompt,
                stream = false,
                format = "json",
                keep_alive = "15m",
                options = new OllamaOptions
                {
                    num_ctx = 8192,
                    num_predict = numPredict,
                    temperature = 0.04f,
                    seed = 42,
                    repeat_penalty = 1.06f
                }
            };
        }

        private string BuildContext()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("USER REQUEST:");
            sb.AppendLine(prompt.Trim());
            sb.AppendLine();
            sb.AppendLine("CURRENT SCENE: " + SceneManager.GetActiveScene().path);

            GameObject go = Selection.activeGameObject;
            if (go == null)
            {
                sb.AppendLine("SELECTED GAMEOBJECT: NONE");
                sb.AppendLine("Creation requests must continue without selection.");
            }
            else
            {
                sb.AppendLine("SELECTED GAMEOBJECT: " + GetHierarchyPath(go.transform));
                sb.AppendLine("Active: " + go.activeSelf);
                sb.AppendLine("Local position: " + go.transform.localPosition);
                sb.AppendLine("Local rotation: " + go.transform.localEulerAngles);
                sb.AppendLine("Local scale: " + go.transform.localScale);
                sb.AppendLine("Components:");
                foreach (Component c in go.GetComponents<Component>())
                    sb.AppendLine(c == null ? "- MISSING SCRIPT" : "- " + c.GetType().FullName);
            }

            sb.AppendLine();
            sb.AppendLine("PROJECT RULES:");
            sb.AppendLine("- Do not alter Gorilla locomotion, XR Origin, gameplay Main Camera, Photon, or player Rigidbody unless explicitly requested.");
            sb.AppendLine("- A separate loading/menu scene may have its own Camera if a World Space Canvas needs one; never replace the gameplay camera.");
            sb.AppendLine("- New visible UI must use real Unity UI components, never 3D fake buttons.");
            sb.AppendLine("- Never fabricate binary PNG/JPG/FBX/font assets by writing text into them.");
            sb.AppendLine("- New custom C# scripts may require a second run after Unity recompiles before they can be attached/wired.");
            return sb.ToString();
        }

        private static string BuildPlannerSystem()
        {
            return
                "You are the planning brain of a Unity Editor agent. Return ONLY valid JSON and no markdown. " +
                "Schema: {\"message\":\"short summary\",\"actions\":[{\"type\":\"create_scene|open_scene|create_ui|create_gameobject|create_or_replace_file|add_component|remove_component|set_active|set_transform|set_component_field\",\"name\":\"optional\",\"scenePath\":\"optional Assets/Scenes/X.unity\",\"parentPath\":\"optional\",\"targetPath\":\"optional\",\"uiType\":\"canvas|background|image|panel|title|text|button|slider|eventsystem\",\"text\":\"optional\",\"color\":\"#RRGGBBAA\",\"path\":\"optional Assets/...\",\"content\":\"complete file content\",\"componentType\":\"optional\",\"field\":\"optional\",\"value\":\"optional\",\"primitive\":\"Cube|Sphere|Capsule|Cylinder|Plane|Quad\",\"boolValue\":true,\"worldSpace\":false,\"renderMode\":\"ScreenSpaceOverlay|WorldSpace\",\"x\":0,\"y\":0,\"z\":0,\"rotX\":0,\"rotY\":0,\"rotZ\":0,\"scale\":1,\"width\":400,\"height\":80,\"fontSize\":36,\"components\":[\"optional component types\"]}]}. " +
                "Use only those action types. For any actionable create/modify/rebuild/fix request, actions MUST NOT be empty. " +
                "When the user asks for a NEW scene, actually use create_scene first and give later actions the correct scenePath. " +
                "Visible UI must use create_ui. create_ui automatically adds real Canvas/Image/Text/Button/Slider/EventSystem components. " +
                "For a World Space Canvas set worldSpace=true or renderMode=WorldSpace and include width,height,x,y,z,rotX,rotY,rotZ and scale. Typical VR loading board: width=1600 height=900 scale=0.003. " +
                "For child UI prefer x,y,width,height,fontSize,color directly instead of many field edits. " +
                "For scripts use create_or_replace_file with a real Assets/Scripts/Name.cs path and complete compiling source. " +
                "Never create fake .png, .jpg, .fbx, .spriteasset or font files. Never replace the gameplay Camera, XR Origin, Gorilla Rig, player Rigidbody or Photon object unless explicitly requested. " +
                "A separate loading scene may contain a new Camera when required to view World Space UI. " +
                "Keep the plan under 40 useful actions. Do not repeat JSON keys. Do not output explanation outside the JSON.";
        }

        private static string BuildReviewerSystem()
        {
            return
                "You are the senior reviewer for a Unity Editor agent. Return ONLY valid corrected JSON in the same schema as the planner. " +
                "Check that every action is supported, necessary, safe and executable. Fix bad actions rather than deleting the user's requested feature. " +
                "IMPORTANT: if the incoming plan contains executable actions for an actionable user request, the final actions array MUST NOT be empty. " +
                "Fix scene routing, UI hierarchy, dimensions, World Space canvas settings, script paths and compile problems. " +
                "Remove fake binary asset writes, duplicate gameplay Cameras/XR rigs/Rigidbodies/EventSystems, meaningless empty objects, repeated work and unrelated actions. " +
                "For requested separate scenes ensure create_scene exists before objects targeting that scene. Prefer strong create_ui actions over brittle field edits. " +
                "Preserve Gorilla locomotion/XR/Photon unless explicitly targeted.";
        }

        private string ApplyPlan(AgentPlan plan)
        {
            if (!HasActions(plan))
                return "No actions to apply.";

            StringBuilder sb = new StringBuilder("APPLYING REVIEWED ACTIONS:\n");
            bool wroteFiles = false;
            GameObject lastCreated = null;

            foreach (AgentAction a in plan.actions)
            {
                if (a == null || string.IsNullOrWhiteSpace(a.type))
                    continue;

                try
                {
                    string type = a.type.Trim().ToLowerInvariant();
                    string line;

                    if (type == "create_scene")
                    {
                        line = CreateScene(a);
                    }
                    else if (type == "open_scene")
                    {
                        line = OpenScene(a);
                    }
                    else
                    {
                        EnsureActionScene(a);

                        if (type == "create_ui")
                            lastCreated = CreateUI(a, out line);
                        else if (type == "create_gameobject")
                            lastCreated = CreateGameObject(a, out line);
                        else if (type == "create_or_replace_file")
                        {
                            line = WriteFile(a);
                            wroteFiles = true;
                        }
                        else if (type == "add_component")
                            line = AddComponent(ResolveTarget(a.targetPath, lastCreated), a.componentType);
                        else if (type == "remove_component")
                            line = RemoveComponent(ResolveTarget(a.targetPath, lastCreated), a.componentType);
                        else if (type == "set_active")
                            line = SetActive(ResolveTarget(a.targetPath, lastCreated), a.boolValue);
                        else if (type == "set_transform")
                            line = SetTransform(ResolveTarget(a.targetPath, lastCreated), a);
                        else if (type == "set_component_field")
                            line = SetComponentField(ResolveTarget(a.targetPath, lastCreated), a);
                        else
                            line = "Skipped unsupported action: " + a.type;
                    }

                    sb.AppendLine("✅ " + line);
                }
                catch (Exception ex)
                {
                    sb.AppendLine("❌ " + Describe(a) + " -> " + ex.Message);
                }
            }

            EditorSceneManager.SaveOpenScenes();
            if (wroteFiles)
                AssetDatabase.Refresh();

            sb.AppendLine("Done. If a new custom C# script was created, let Unity compile and run SMART BRAIN again to attach/wire that custom component.");
            return sb.ToString();
        }

        private static string CreateScene(AgentAction a)
        {
            string path = NormalizeScenePath(!string.IsNullOrWhiteSpace(a.path) ? a.path : a.scenePath, a.name);
            Directory.CreateDirectory(Path.GetDirectoryName(AssetToFull(path)));
            EditorSceneManager.SaveOpenScenes();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, path);
            return "Created scene " + path;
        }

        private static string OpenScene(AgentAction a)
        {
            string path = NormalizeScenePath(!string.IsNullOrWhiteSpace(a.path) ? a.path : a.scenePath, a.name);
            if (!File.Exists(AssetToFull(path)))
                throw new FileNotFoundException("Scene does not exist: " + path);

            EditorSceneManager.SaveOpenScenes();
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            return "Opened scene " + path;
        }

        private static void EnsureActionScene(AgentAction a)
        {
            if (string.IsNullOrWhiteSpace(a.scenePath))
                return;

            string path = NormalizeScenePath(a.scenePath, "Scene");
            Scene active = SceneManager.GetActiveScene();
            if (string.Equals(active.path, path, StringComparison.OrdinalIgnoreCase))
                return;

            if (!File.Exists(AssetToFull(path)))
                throw new FileNotFoundException("Target scene was not created yet: " + path);

            EditorSceneManager.SaveOpenScenes();
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        }

        private static string NormalizeScenePath(string path, string name)
        {
            string p = path;
            if (string.IsNullOrWhiteSpace(p))
            {
                string n = string.IsNullOrWhiteSpace(name) ? "AI_Scene" : CleanName(name);
                if (!n.EndsWith("Scene", StringComparison.OrdinalIgnoreCase))
                    n += "Scene";
                p = "Assets/Scenes/" + n + ".unity";
            }

            p = p.Replace('\\', '/');
            if (!p.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                p = "Assets/Scenes/" + Path.GetFileName(p);
            if (!p.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                p += ".unity";
            return p;
        }

        private static GameObject CreateUI(AgentAction a, out string message)
        {
            string kind = (a.uiType ?? "").Trim().ToLowerInvariant();
            if (kind == "label") kind = "text";
            if (kind == "img") kind = "image";
            if (kind == "menu") kind = "panel";

            string name = CleanName(string.IsNullOrWhiteSpace(a.name) ? "AI_" + kind : a.name);

            if (kind == "eventsystem")
            {
                GameObject existing = FindSceneObjectWithComponent("EventSystem");
                if (existing != null)
                {
                    message = "Reused EventSystem " + existing.name;
                    return existing;
                }

                GameObject es = new GameObject(name);
                Undo.RegisterCreatedObjectUndo(es, "SMART create EventSystem");
                TryAddComponent(es, "UnityEngine.EventSystems.EventSystem");
                if (!TryAddComponent(es, "UnityEngine.InputSystem.UI.InputSystemUIInputModule"))
                    TryAddComponent(es, "UnityEngine.EventSystems.StandaloneInputModule");
                Selection.activeGameObject = es;
                message = "Created EventSystem";
                return es;
            }

            GameObject go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "SMART create UI");

            GameObject parent = Resolve(a.parentPath);
            if (parent == null && kind != "canvas")
                parent = FindSceneObjectWithComponent("Canvas");
            if (parent != null)
                go.transform.SetParent(parent.transform, false);

            RectTransform rt = (RectTransform)go.transform;
            rt.localScale = Vector3.one;

            if (kind == "canvas")
            {
                Canvas canvas = go.AddComponent<Canvas>();
                bool world = a.worldSpace || string.Equals(a.renderMode, "WorldSpace", StringComparison.OrdinalIgnoreCase);
                canvas.renderMode = world ? RenderMode.WorldSpace : RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 100;

                Component scaler = AddByName(go, "UnityEngine.UI.CanvasScaler");
                AddByName(go, "UnityEngine.UI.GraphicRaycaster");

                if (world)
                {
                    rt.sizeDelta = new Vector2(a.width > 0 ? a.width : 1600f, a.height > 0 ? a.height : 900f);
                    float s = a.scale > 0 ? a.scale : 0.003f;
                    rt.localScale = new Vector3(s, s, s);
                    rt.localPosition = new Vector3(a.x, a.y, a.z);
                    rt.localEulerAngles = new Vector3(a.rotX, a.rotY, a.rotZ);
                    if (scaler != null)
                        SetMember(scaler, "dynamicPixelsPerUnit", 10f);
                    Selection.activeGameObject = go;
                    message = "Created World Space Canvas " + rt.sizeDelta + " scale=" + s.ToString(CultureInfo.InvariantCulture);
                    return go;
                }

                if (scaler != null)
                {
                    SetEnumMember(scaler, "uiScaleMode", "ScaleWithScreenSize");
                    SetMember(scaler, "referenceResolution", new Vector2(1920, 1080));
                    SetMember(scaler, "matchWidthOrHeight", .5f);
                }

                Selection.activeGameObject = go;
                message = "Created Screen Space Overlay Canvas";
                return go;
            }

            if (kind == "background")
            {
                Stretch(rt);
                AddImage(go, string.IsNullOrWhiteSpace(a.color) ? "#08090DFF" : a.color);
            }
            else if (kind == "image")
            {
                SetRect(rt, a, 240, 80);
                AddImage(go, string.IsNullOrWhiteSpace(a.color) ? "#FFFFFFFF" : a.color);
            }
            else if (kind == "panel")
            {
                SetRect(rt, a, 760, 520);
                AddImage(go, string.IsNullOrWhiteSpace(a.color) ? "#11131BEF" : a.color);
            }
            else if (kind == "title" || kind == "text")
            {
                SetRect(rt, a, kind == "title" ? 1000 : 650, kind == "title" ? 150 : 70);
                AddText(go,
                    string.IsNullOrWhiteSpace(a.text) ? name : a.text,
                    a.fontSize > 0 ? a.fontSize : (kind == "title" ? 82 : 36),
                    a.color);
            }
            else if (kind == "button")
            {
                SetRect(rt, a, 440, 90);
                Component image = AddImage(go, string.IsNullOrWhiteSpace(a.color) ? "#242730FF" : a.color);
                Component button = AddByName(go, "UnityEngine.UI.Button");
                if (button != null && image != null)
                    SetMember(button, "targetGraphic", image);
                CreateButtonText(go,
                    string.IsNullOrWhiteSpace(a.text) ? name.Replace("Button", "") : a.text,
                    a.fontSize > 0 ? a.fontSize : 36);
            }
            else if (kind == "slider")
            {
                SetRect(rt, a, 600, 34);
                BuildSlider(go, a.color);
            }
            else
            {
                throw new InvalidOperationException("Unsupported uiType: " + a.uiType);
            }

            Selection.activeGameObject = go;
            message = "Created real UI " + kind + " '" + go.name + "'";
            return go;
        }

        private static GameObject CreateGameObject(AgentAction a, out string message)
        {
            string name = CleanName(string.IsNullOrWhiteSpace(a.name) ? "AI_Object" : a.name);
            GameObject go;
            PrimitiveType pt;

            if (!string.IsNullOrWhiteSpace(a.primitive) && TryPrimitive(a.primitive, out pt))
            {
                go = GameObject.CreatePrimitive(pt);
                go.name = name;
            }
            else
            {
                go = new GameObject(name);
            }

            Undo.RegisterCreatedObjectUndo(go, "SMART create GameObject");
            GameObject parent = Resolve(a.parentPath);
            if (parent != null)
                go.transform.SetParent(parent.transform, false);

            go.transform.localPosition = new Vector3(a.x, a.y, a.z);
            go.transform.localEulerAngles = new Vector3(a.rotX, a.rotY, a.rotZ);
            if (a.scale > 0)
                go.transform.localScale = Vector3.one * a.scale;

            if (a.components != null)
            {
                foreach (string c in a.components)
                    if (!string.IsNullOrWhiteSpace(c))
                        TryAddComponent(go, c);
            }

            Selection.activeGameObject = go;
            message = "Created GameObject " + go.name;
            return go;
        }

        private static string WriteFile(AgentAction a)
        {
            string path = a.path;
            if (string.IsNullOrWhiteSpace(path))
            {
                string n = string.IsNullOrWhiteSpace(a.name) ? "AIController.cs" : Path.GetFileName(a.name);
                if (!n.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    n += ".cs";
                path = "Assets/Scripts/" + n;
            }

            path = path.Replace('\\', '/');
            if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                path = "Assets/Scripts/" + Path.GetFileName(path);

            if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Blocked fake/binary asset write: " + path);

            string full = AssetToFull(path);
            string allowed = Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(full).StartsWith(allowed, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Write outside Assets is blocked.");

            Directory.CreateDirectory(Path.GetDirectoryName(full));

            if (File.Exists(full))
            {
                string backupDir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Library", "PrimatePanicAIBackups");
                Directory.CreateDirectory(backupDir);
                File.Copy(full, Path.Combine(backupDir, DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + Path.GetFileName(full)), true);
            }

            File.WriteAllText(full, a.content ?? "", new UTF8Encoding(false));
            return "Wrote " + path;
        }

        private static string AddComponent(GameObject target, string typeName)
        {
            Require(target);
            if (!TryAddComponent(target, typeName))
                throw new InvalidOperationException("Could not add component " + typeName);
            return "Added " + typeName + " to " + target.name;
        }

        private static string RemoveComponent(GameObject target, string typeName)
        {
            Require(target);
            Type t = FindType(typeName);
            if (t == null)
                throw new InvalidOperationException("Component type not found: " + typeName);

            Component c = target.GetComponent(t);
            if (c == null)
                return target.name + " did not have " + typeName;

            Undo.DestroyObjectImmediate(c);
            return "Removed " + typeName + " from " + target.name;
        }

        private static string SetActive(GameObject target, bool active)
        {
            Require(target);
            Undo.RecordObject(target, "SMART set active");
            target.SetActive(active);
            return "Set " + target.name + " active=" + active;
        }

        private static string SetTransform(GameObject target, AgentAction a)
        {
            Require(target);
            Undo.RecordObject(target.transform, "SMART transform");
            target.transform.localPosition = new Vector3(a.x, a.y, a.z);
            target.transform.localEulerAngles = new Vector3(a.rotX, a.rotY, a.rotZ);
            if (a.scale > 0)
                target.transform.localScale = Vector3.one * a.scale;

            RectTransform rt = target.transform as RectTransform;
            if (rt != null && (a.width > 0 || a.height > 0))
                rt.sizeDelta = new Vector2(a.width > 0 ? a.width : rt.sizeDelta.x, a.height > 0 ? a.height : rt.sizeDelta.y);

            return "Updated transform on " + target.name;
        }

        private static string SetComponentField(GameObject target, AgentAction a)
        {
            Require(target);
            Type t = FindType(a.componentType);
            if (t == null)
                throw new InvalidOperationException("Component type not found: " + a.componentType);

            Component c = target.GetComponent(t);
            if (c == null)
                throw new InvalidOperationException(target.name + " has no " + a.componentType);

            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            FieldInfo field = t.GetField(a.field ?? "", flags);
            PropertyInfo prop = field == null ? t.GetProperty(a.field ?? "", flags) : null;
            Type valueType = field != null ? field.FieldType : prop != null && prop.CanWrite ? prop.PropertyType : null;
            if (valueType == null)
                throw new InvalidOperationException("Writable field not found: " + a.field);

            object value = ConvertValue(a.value, valueType);
            Undo.RecordObject(c, "SMART set component field");
            if (field != null)
                field.SetValue(c, value);
            else
                prop.SetValue(c, value, null);

            EditorUtility.SetDirty(c);
            return "Set " + a.componentType + "." + a.field;
        }

        private static object ConvertValue(string raw, Type t)
        {
            string v = raw ?? "";
            if (t == typeof(string)) return v;
            if (t == typeof(bool)) return bool.Parse(v);
            if (t == typeof(int)) return int.Parse(v, CultureInfo.InvariantCulture);
            if (t == typeof(float)) return float.Parse(v, CultureInfo.InvariantCulture);
            if (t == typeof(double)) return double.Parse(v, CultureInfo.InvariantCulture);

            if (t == typeof(Vector2))
            {
                string[] p = v.Trim('(', ')', '[', ']').Split(',');
                if (p.Length != 2) throw new FormatException("Vector2 must be x,y");
                return new Vector2(float.Parse(p[0], CultureInfo.InvariantCulture), float.Parse(p[1], CultureInfo.InvariantCulture));
            }

            if (t == typeof(Vector3))
            {
                string[] p = v.Trim('(', ')', '[', ']').Split(',');
                if (p.Length != 3) throw new FormatException("Vector3 must be x,y,z");
                return new Vector3(float.Parse(p[0], CultureInfo.InvariantCulture), float.Parse(p[1], CultureInfo.InvariantCulture), float.Parse(p[2], CultureInfo.InvariantCulture));
            }

            if (t == typeof(Color))
                return ParseColor(v, Color.white);
            if (t.IsEnum)
                return Enum.Parse(t, v.Replace(" ", ""), true);

            if (typeof(UnityEngine.Object).IsAssignableFrom(t))
            {
                GameObject go = Resolve(v);
                if (go == null) return null;
                if (t == typeof(GameObject)) return go;
                if (t == typeof(Transform)) return go.transform;
                return go.GetComponent(t);
            }

            throw new InvalidOperationException("Unsupported value type: " + t.Name);
        }

        private void Send(GenerateRequest request, int timeout, Action<string> success)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                result = "Ollama URL is empty.";
                return;
            }

            working = true;
            Repaint();

            string json = JsonUtility.ToJson(request);
            UnityWebRequest req = new UnityWebRequest(endpoint.Trim(), "POST");
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = timeout;

            UnityWebRequestAsyncOperation op = req.SendWebRequest();
            op.completed += _ =>
            {
                working = false;

                if (req.result != UnityWebRequest.Result.Success)
                {
                    result = "OLLAMA FAILED\nHTTP " + req.responseCode + "\n" + req.error + "\n\n" + req.downloadHandler.text;
                }
                else
                {
                    OllamaResponse response = JsonUtility.FromJson<OllamaResponse>(req.downloadHandler.text);
                    if (response == null)
                        result = "Empty Ollama response.";
                    else if (!string.IsNullOrWhiteSpace(response.error))
                        result = "OLLAMA ERROR: " + response.error;
                    else
                        success(response.response ?? "");
                }

                req.Dispose();
                Repaint();
            };
        }

        private static AgentPlan ParsePlan(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException("Empty model response.");

            int s = text.IndexOf('{');
            int e = text.LastIndexOf('}');
            if (s < 0 || e <= s)
                throw new InvalidOperationException("No JSON object found.");

            AgentPlan plan = JsonUtility.FromJson<AgentPlan>(text.Substring(s, e - s + 1));
            if (plan == null)
                throw new InvalidOperationException("JSON did not match the agent schema.");
            return plan;
        }

        private static GameObject ResolveTarget(string path, GameObject fallback)
        {
            return Resolve(path) ?? fallback ?? Selection.activeGameObject;
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

        private static GameObject FindSceneObjectWithComponent(string typeName)
        {
            Type t = FindType(typeName);
            if (t == null)
                return null;

            Scene active = SceneManager.GetActiveScene();
            foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go == null || !go.scene.IsValid() || go.scene != active)
                    continue;
                if (go.GetComponent(t) != null)
                    return go;
            }
            return null;
        }

        private static Type FindType(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            string expanded = ExpandType(name.Trim());
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type direct = asm.GetType(name, false, true) ?? asm.GetType(expanded, false, true);
                if (direct != null)
                    return direct;

                try
                {
                    foreach (Type t in asm.GetTypes())
                    {
                        if (string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase) ||
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

        private static string ExpandType(string name)
        {
            switch ((name ?? "").ToLowerInvariant())
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
                case "camera": return "UnityEngine.Camera";
                default: return name;
            }
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

        private static Component AddImage(GameObject go, string color)
        {
            Component c = AddByName(go, "UnityEngine.UI.Image");
            if (c != null)
                SetMember(c, "color", ParseColor(color, Color.white));
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

        private static void CreateButtonText(GameObject button, string label, int fontSize)
        {
            GameObject child = new GameObject("Text", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(child, "SMART button text");
            child.transform.SetParent(button.transform, false);
            Stretch((RectTransform)child.transform);
            AddText(child, label, fontSize, "#FFFFFFFF");
        }

        private static void BuildSlider(GameObject root, string accent)
        {
            Component slider = AddByName(root, "UnityEngine.UI.Slider");

            GameObject bg = CreateUIChild(root, "Background");
            RectTransform br = (RectTransform)bg.transform;
            br.anchorMin = new Vector2(0, .25f);
            br.anchorMax = new Vector2(1, .75f);
            br.offsetMin = Vector2.zero;
            br.offsetMax = Vector2.zero;
            AddImage(bg, "#30333DFF");

            GameObject fillArea = CreateUIChild(root, "Fill Area");
            RectTransform far = (RectTransform)fillArea.transform;
            far.anchorMin = new Vector2(0, .25f);
            far.anchorMax = new Vector2(1, .75f);
            far.offsetMin = new Vector2(8, 0);
            far.offsetMax = new Vector2(-8, 0);

            GameObject fill = CreateUIChild(fillArea, "Fill");
            RectTransform fr = (RectTransform)fill.transform;
            Stretch(fr);
            AddImage(fill, string.IsNullOrWhiteSpace(accent) ? "#53D8FFFF" : accent);

            GameObject handleArea = CreateUIChild(root, "Handle Slide Area");
            RectTransform har = (RectTransform)handleArea.transform;
            har.anchorMin = Vector2.zero;
            har.anchorMax = Vector2.one;
            har.offsetMin = new Vector2(10, 0);
            har.offsetMax = new Vector2(-10, 0);

            GameObject handle = CreateUIChild(handleArea, "Handle");
            RectTransform hr = (RectTransform)handle.transform;
            hr.sizeDelta = new Vector2(28, 40);
            Component hi = AddImage(handle, "#FFFFFFFF");

            if (slider != null)
            {
                SetMember(slider, "fillRect", fr);
                SetMember(slider, "handleRect", hr);
                if (hi != null)
                    SetMember(slider, "targetGraphic", hi);
                SetMember(slider, "minValue", 0f);
                SetMember(slider, "maxValue", 1f);
                SetMember(slider, "value", 0f);
            }
        }

        private static GameObject CreateUIChild(GameObject parent, string name)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "SMART UI child");
            go.transform.SetParent(parent.transform, false);
            return go;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;
        }

        private static void SetRect(RectTransform rt, AgentAction a, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(.5f, .5f);
            rt.pivot = new Vector2(.5f, .5f);
            rt.anchoredPosition = new Vector2(a.x, a.y);
            rt.sizeDelta = new Vector2(a.width > 0 ? a.width : w, a.height > 0 ? a.height : h);
            if (Mathf.Abs(a.rotZ) > 0.001f)
                rt.localEulerAngles = new Vector3(0, 0, a.rotZ);
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
            Type enumType = f != null ? f.FieldType : p != null ? p.PropertyType : null;
            if (enumType == null || !enumType.IsEnum)
                return;

            object value = Enum.Parse(enumType, enumName.Replace(" ", ""), true);
            if (f != null)
                f.SetValue(obj, value);
            else if (p != null && p.CanWrite)
                p.SetValue(obj, value, null);
        }

        private static Color ParseColor(string value, Color fallback)
        {
            Color c;
            return !string.IsNullOrWhiteSpace(value) && ColorUtility.TryParseHtmlString(value.Trim(), out c) ? c : fallback;
        }

        private static void Require(GameObject go)
        {
            if (go == null)
                throw new InvalidOperationException("Target GameObject was not found.");
        }

        private static string CleanName(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "AI_Object" : value.Replace('/', '_').Replace('\\', '_').Trim();
        }

        private static string AssetToFull(string asset)
        {
            return Path.GetFullPath(Path.Combine(Directory.GetParent(Application.dataPath).FullName, asset.Replace('/', Path.DirectorySeparatorChar)));
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

        private static string Describe(AgentAction a)
        {
            if (a == null)
                return "null action";

            return (a.type ?? "unknown") +
                   (string.IsNullOrWhiteSpace(a.uiType) ? "" : " " + a.uiType) +
                   (string.IsNullOrWhiteSpace(a.name) ? "" : " " + a.name) +
                   (string.IsNullOrWhiteSpace(a.scenePath) ? "" : " @ " + a.scenePath);
        }

        private static bool TryPrimitive(string value, out PrimitiveType t)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
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

        [Serializable]
        private class OllamaOptions
        {
            public int num_ctx;
            public int num_predict;
            public float temperature;
            public int seed;
            public float repeat_penalty;
        }

        [Serializable]
        private class GenerateRequest
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
            public string scenePath;
            public string parentPath;
            public string targetPath;
            public string uiType;
            public string text;
            public string color;
            public string path;
            public string content;
            public string componentType;
            public string field;
            public string value;
            public string primitive;
            public string renderMode;
            public bool boolValue;
            public bool worldSpace;
            public float x;
            public float y;
            public float z;
            public float rotX;
            public float rotY;
            public float rotZ;
            public float scale;
            public float width;
            public float height;
            public int fontSize;
            public string[] components;
        }
    }
}
