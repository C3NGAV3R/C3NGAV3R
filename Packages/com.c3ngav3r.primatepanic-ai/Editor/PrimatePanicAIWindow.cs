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
    internal static class PrimatePanicAIRecentLogs
    {
        private static readonly List<string> Entries = new List<string>();
        private const int MaxEntries = 30;

        static PrimatePanicAIRecentLogs()
        {
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)
                return;

            string text = type + ": " + condition;
            if (!string.IsNullOrEmpty(stackTrace))
            {
                string[] lines = stackTrace.Split('\n');
                if (lines.Length > 0 && !string.IsNullOrWhiteSpace(lines[0]))
                    text += "\n" + lines[0].Trim();
            }

            if (text.Length > 1400)
                text = text.Substring(0, 1400) + " ...";

            Entries.Add(text);
            while (Entries.Count > MaxEntries)
                Entries.RemoveAt(0);
        }

        public static string GetRecent(int maxCount)
        {
            if (Entries.Count == 0)
                return "No recent Unity Console errors captured since the last script reload.";

            int start = Mathf.Max(0, Entries.Count - Mathf.Max(1, maxCount));
            StringBuilder sb = new StringBuilder();
            for (int i = start; i < Entries.Count; i++)
            {
                sb.AppendLine("---");
                sb.AppendLine(Entries[i]);
            }
            return sb.ToString();
        }
    }

    public class PrimatePanicAIWindow : EditorWindow
    {
        private enum WorkspaceMode
        {
            Agent = 0,
            Plan = 1,
            PictureTo3D = 2
        }

        private const string ModePref = "PrimatePanicAI.Mode";
        private const string TextModelPref = "PrimatePanicAI.TextModel";
        private const string VisionModelPref = "PrimatePanicAI.VisionModel";
        private const string EndpointPref = "PrimatePanicAI.OllamaEndpoint";
        private const string FastModePref = "PrimatePanicAI.FastMode";
        private const string IncludeConsolePref = "PrimatePanicAI.IncludeConsole";
        private const string PictureAutoPref = "PrimatePanicAI.PictureAutoApply";

        private WorkspaceMode mode = WorkspaceMode.Agent;
        private string textModel = "qwen2.5-coder:7b";
        private string visionModel = "qwen2.5vl:3b";
        private string endpoint = "http://127.0.0.1:11434/api/generate";
        private bool fastMode = true;
        private bool includeConsoleErrors = true;
        private bool pictureAutoCreate = true;

        private string textPrompt = "";
        private string picturePrompt = "Recreate the main object in this reference image as a clean 3D Unity blockout. Ignore Unity editor gizmos, rig/bone lines, transform handles, grids and UI. Match visible proportions, major shapes and colors as closely as possible.";
        private string resultText = "";
        private Vector2 scroll;
        private bool waiting;

        private string imagePath = "";
        private Texture2D preview;

        private AgentPlan lastAgentPlan;
        private RecreationPlan lastRecreationPlan;

        [MenuItem("Tools/Primate Panic AI")]
        public static void Open()
        {
            GetWindow<PrimatePanicAIWindow>("Primate Panic AI");
        }

        private void OnEnable()
        {
            mode = (WorkspaceMode)Mathf.Clamp(EditorPrefs.GetInt(ModePref, 0), 0, 2);
            textModel = EditorPrefs.GetString(TextModelPref, "qwen2.5-coder:7b");
            visionModel = EditorPrefs.GetString(VisionModelPref, "qwen2.5vl:3b");
            endpoint = EditorPrefs.GetString(EndpointPref, "http://127.0.0.1:11434/api/generate");
            fastMode = EditorPrefs.GetBool(FastModePref, true);
            includeConsoleErrors = EditorPrefs.GetBool(IncludeConsolePref, true);
            pictureAutoCreate = EditorPrefs.GetBool(PictureAutoPref, true);
        }

        private void OnDisable()
        {
            if (preview != null)
                DestroyImmediate(preview);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(7);
            EditorGUILayout.LabelField("Primate Panic AI - LOCAL v0.6", EditorStyles.boldLabel);

            int newMode = GUILayout.Toolbar((int)mode, new[] { "AGENT", "PLAN", "PICTURE → 3D" }, GUILayout.Height(30));
            if (newMode != (int)mode)
            {
                mode = (WorkspaceMode)newMode;
                EditorPrefs.SetInt(ModePref, newMode);
                resultText = "Switched to " + GetModeName(mode) + ".";
            }

            EditorGUILayout.Space(6);
            EditorGUI.BeginChangeCheck();
            endpoint = EditorGUILayout.TextField("Ollama URL", endpoint);
            if (EditorGUI.EndChangeCheck())
                EditorPrefs.SetString(EndpointPref, endpoint);

            if (mode == WorkspaceMode.PictureTo3D)
                DrawPictureMode();
            else
                DrawTextMode(mode == WorkspaceMode.Plan);

            DrawResult();
        }

        private static string GetModeName(WorkspaceMode value)
        {
            if (value == WorkspaceMode.Agent) return "Agent Mode";
            if (value == WorkspaceMode.Plan) return "Plan Mode";
            return "Picture → 3D Mode";
        }

        private void DrawTextMode(bool planOnly)
        {
            EditorGUILayout.HelpBox(
                planOnly
                    ? "PLAN MODE: AI inspects automatically and builds a plan. Nothing changes until you press APPLY LAST PLAN. Creation requests do NOT require a selection."
                    : "AGENT MODE: AI can fix selected objects OR create brand-new systems from nothing. If no selection is needed, it creates its own root GameObject and continues automatically.",
                planOnly ? MessageType.Info : MessageType.Warning
            );

            EditorGUI.BeginChangeCheck();
            textModel = EditorGUILayout.TextField("Coding Model", textModel);
            fastMode = EditorGUILayout.ToggleLeft("Fast Mode (recommended)", fastMode);
            includeConsoleErrors = EditorGUILayout.ToggleLeft("Include recent Console errors", includeConsoleErrors);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetString(TextModelPref, textModel);
                EditorPrefs.SetBool(FastModePref, fastMode);
                EditorPrefs.SetBool(IncludeConsolePref, includeConsoleErrors);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Use Fast 3B", GUILayout.Height(24)))
                {
                    textModel = "qwen2.5-coder:3b";
                    EditorPrefs.SetString(TextModelPref, textModel);
                    resultText = "Selected qwen2.5-coder:3b. If needed run once in CMD: ollama run qwen2.5-coder:3b";
                }

                if (GUILayout.Button("Use Better 7B", GUILayout.Height(24)))
                {
                    textModel = "qwen2.5-coder:7b";
                    EditorPrefs.SetString(TextModelPref, textModel);
                }
            }

            GameObject selected = Selection.activeGameObject;
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Auto inspection", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                selected != null
                    ? "Selected: " + GetHierarchyPath(selected.transform) + "\nAgent/Plan reads it automatically. If your request is unrelated to this object, the AI may create a new root instead."
                    : "Nothing selected. That's OK. Creation requests like menus, loading screens, managers, systems, portals, buttons and new setups will create new GameObjects automatically.",
                MessageType.None
            );

            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.enabled = !waiting;
                if (GUILayout.Button("Test Coding Model", GUILayout.Height(27)))
                    SendTextTest();

                if (GUILayout.Button("Clear", GUILayout.Height(27)))
                {
                    textPrompt = "";
                    resultText = "";
                    lastAgentPlan = null;
                }
                GUI.enabled = true;
            }

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField(planOnly ? "What should it plan?" : "What should it do?", EditorStyles.boldLabel);
            textPrompt = EditorGUILayout.TextArea(textPrompt, GUILayout.MinHeight(105));

            GUI.enabled = !waiting && !string.IsNullOrWhiteSpace(textPrompt) && !string.IsNullOrWhiteSpace(textModel);
            if (GUILayout.Button(waiting ? "WORKING..." : planOnly ? "MAKE PLAN" : "RUN AGENT", GUILayout.Height(38)))
                SendAgentRequest(!planOnly);
            GUI.enabled = true;

            if (planOnly && lastAgentPlan != null && lastAgentPlan.actions != null && lastAgentPlan.actions.Length > 0)
            {
                EditorGUILayout.Space(4);
                if (GUILayout.Button("APPLY LAST PLAN", GUILayout.Height(31)))
                {
                    resultText += "\n\n" + ApplyAgentPlan(lastAgentPlan);
                    lastAgentPlan = null;
                }
            }
        }

        private void DrawPictureMode()
        {
            EditorGUILayout.HelpBox(
                "PICTURE → 3D MODE: choose a reference image. The local vision model analyzes the visible object and builds a bounded 3D Unity blockout from primitives.",
                MessageType.Info
            );

            EditorGUI.BeginChangeCheck();
            visionModel = EditorGUILayout.TextField("Vision Model", visionModel);
            pictureAutoCreate = EditorGUILayout.ToggleLeft("Create recreation automatically", pictureAutoCreate);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetString(VisionModelPref, visionModel);
                EditorPrefs.SetBool(PictureAutoPref, pictureAutoCreate);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.enabled = !waiting;
                if (GUILayout.Button("Test Vision Model", GUILayout.Height(27)))
                    SendVisionTest();
                if (GUILayout.Button("Pick Picture", GUILayout.Height(27)))
                    PickImage();
                if (GUILayout.Button("Clear Picture", GUILayout.Height(27)))
                    ClearImage();
                GUI.enabled = true;
            }

            if (!string.IsNullOrEmpty(imagePath))
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Reference: " + Path.GetFileName(imagePath));
                if (preview != null)
                {
                    float maxWidth = Mathf.Max(160f, position.width - 28f);
                    float aspect = preview.height > 0 ? (float)preview.width / preview.height : 1f;
                    float width = Mathf.Min(maxWidth, 440f);
                    float height = Mathf.Clamp(width / Mathf.Max(0.01f, aspect), 110f, 300f);
                    Rect r = GUILayoutUtility.GetRect(width, height, GUILayout.ExpandWidth(false));
                    EditorGUI.DrawPreviewTexture(r, preview, null, ScaleMode.ScaleToFit);
                }
            }
            else
            {
                EditorGUILayout.HelpBox("No reference picture selected.", MessageType.Warning);
            }

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("What should it recreate?", EditorStyles.boldLabel);
            picturePrompt = EditorGUILayout.TextArea(picturePrompt, GUILayout.MinHeight(88));

            GUI.enabled = !waiting && preview != null && !string.IsNullOrWhiteSpace(picturePrompt) && !string.IsNullOrWhiteSpace(visionModel);
            if (GUILayout.Button(waiting ? "VISION AI IS BUILDING..." : "RECREATE PICTURE IN UNITY", GUILayout.Height(38)))
                SendRecreationRequest();
            GUI.enabled = true;

            if (!pictureAutoCreate && lastRecreationPlan != null)
            {
                if (GUILayout.Button("CREATE LAST PICTURE PLAN IN SCENE", GUILayout.Height(31)))
                {
                    resultText += "\n\n" + ApplyRecreationPlan(lastRecreationPlan);
                    lastRecreationPlan = null;
                }
            }
        }

        private void DrawResult()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Result", EditorStyles.boldLabel);
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MinHeight(180));
            EditorGUILayout.TextArea(resultText, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private void SendTextTest()
        {
            OllamaRequest req = new OllamaRequest
            {
                model = textModel.Trim(),
                prompt = "Reply with exactly: CODING MODEL CONNECTED",
                stream = false,
                keep_alive = "15m",
                options = new OllamaOptions { num_ctx = 2048, num_predict = 32, temperature = 0f }
            };

            SendOllama(JsonUtility.ToJson(req), "Testing coding model...", 300, text =>
            {
                resultText = text.Contains("CODING MODEL CONNECTED") ? "CODING MODEL CONNECTED ✅" : "CODING MODEL REPLIED:\n" + text;
            });
        }

        private void SendVisionTest()
        {
            OllamaVisionRequest req = new OllamaVisionRequest
            {
                model = visionModel.Trim(),
                prompt = "Reply with exactly: VISION MODEL CONNECTED",
                stream = false,
                format = "",
                images = null,
                keep_alive = "15m",
                options = new OllamaOptions { num_ctx = 2048, num_predict = 32, temperature = 0f }
            };

            SendOllama(JsonUtility.ToJson(req), "Testing vision model...", 300, text =>
            {
                resultText = text.Contains("VISION MODEL CONNECTED") ? "VISION MODEL CONNECTED ✅" : "VISION MODEL REPLIED:\n" + text;
            });
        }

        private void SendAgentRequest(bool applyImmediately)
        {
            string context = BuildUserRequestWithContext(true);
            OllamaAgentRequest req = new OllamaAgentRequest
            {
                model = textModel.Trim(),
                system = BuildAgentSystemPrompt(),
                prompt = context,
                stream = false,
                format = "json",
                keep_alive = "15m",
                options = new OllamaOptions
                {
                    temperature = 0.05f,
                    num_ctx = fastMode ? 4096 : 8192,
                    num_predict = fastMode ? 3200 : 5600
                }
            };

            SendOllama(
                JsonUtility.ToJson(req),
                applyImmediately ? "Agent is building/fixing it now..." : "AI is building a safe plan...",
                300,
                text => HandleAgentResponse(text, applyImmediately)
            );
        }

        private string BuildUserRequestWithContext(bool includeScriptSource)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("USER REQUEST:");
            sb.AppendLine(textPrompt.Trim());
            sb.AppendLine();
            sb.AppendLine(BuildSelectedObjectContext(includeScriptSource));

            if (includeConsoleErrors)
            {
                sb.AppendLine();
                sb.AppendLine("RECENT UNITY CONSOLE ERRORS (IN MEMORY, BOUNDED):");
                sb.AppendLine(PrimatePanicAIRecentLogs.GetRecent(fastMode ? 6 : 12));
            }

            return sb.ToString();
        }

        private string BuildSelectedObjectContext(bool includeScriptSource)
        {
            GameObject go = Selection.activeGameObject;
            if (go == null)
            {
                return
                    "SELECTED GAMEOBJECT: NONE\n" +
                    "IMPORTANT: No selection is NOT a blocker for creation requests. If the user asks to make/create/add a new menu, loading screen, manager, system, portal, button setup, environment object, or other new feature, create a new root GameObject and continue. Only require a selection when the request clearly targets a specific existing object.";
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("SELECTED GAMEOBJECT:");
            sb.AppendLine("Name: " + go.name);
            sb.AppendLine("Hierarchy path: " + GetHierarchyPath(go.transform));
            sb.AppendLine("Active self: " + go.activeSelf);
            sb.AppendLine("Layer: " + LayerMask.LayerToName(go.layer));
            sb.AppendLine("Tag: " + go.tag);
            sb.AppendLine("Local position: " + go.transform.localPosition);
            sb.AppendLine("Local rotation: " + go.transform.localEulerAngles);
            sb.AppendLine("Local scale: " + go.transform.localScale);
            sb.AppendLine("Parent: " + (go.transform.parent != null ? go.transform.parent.name : "NONE"));
            sb.AppendLine("Components:");

            int scriptCharacters = 0;
            int maxScriptCharacters = fastMode ? 10000 : 30000;
            int maxSingleScript = fastMode ? 8000 : 18000;

            foreach (Component c in go.GetComponents<Component>())
            {
                if (c == null)
                {
                    sb.AppendLine("- MISSING SCRIPT / NULL COMPONENT");
                    continue;
                }

                Type componentType = c.GetType();
                sb.AppendLine("- " + componentType.FullName);

                if (c is Rigidbody rb)
                    sb.AppendLine("  mass=" + rb.mass + ", gravity=" + rb.useGravity + ", kinematic=" + rb.isKinematic + ", constraints=" + rb.constraints);
                else if (c is Collider col)
                    sb.AppendLine("  collider enabled=" + col.enabled + ", trigger=" + col.isTrigger);
                else if (c is Animator animator)
                    sb.AppendLine("  animator enabled=" + animator.enabled + ", rootMotion=" + animator.applyRootMotion);

                MonoBehaviour behaviour = c as MonoBehaviour;
                if (behaviour == null)
                    continue;

                MonoScript script = MonoScript.FromMonoBehaviour(behaviour);
                if (script == null)
                    continue;

                string assetPath = AssetDatabase.GetAssetPath(script);
                sb.AppendLine("  Script path: " + assetPath);

                if (!includeScriptSource || scriptCharacters >= maxScriptCharacters || string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                    continue;

                string fullPath = AssetPathToFullPath(assetPath);
                if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
                    continue;

                try
                {
                    string source = File.ReadAllText(fullPath);
                    int remaining = maxScriptCharacters - scriptCharacters;
                    int allowed = Mathf.Min(maxSingleScript, remaining);
                    if (source.Length > allowed)
                        source = source.Substring(0, allowed) + "\n// SOURCE TRUNCATED BY FAST AGENT";

                    scriptCharacters += source.Length;
                    sb.AppendLine("  BEGIN SCRIPT SOURCE");
                    sb.AppendLine(source);
                    sb.AppendLine("  END SCRIPT SOURCE");
                }
                catch (Exception ex)
                {
                    sb.AppendLine("  Could not read script source: " + ex.Message);
                }
            }

            return sb.ToString();
        }

        private void HandleAgentResponse(string modelText, bool applyImmediately)
        {
            try
            {
                AgentPlan plan = JsonUtility.FromJson<AgentPlan>(ExtractJsonObject(modelText));
                if (plan == null)
                {
                    resultText = "The model did not return a usable agent plan.\n\n" + modelText;
                    return;
                }

                lastAgentPlan = plan;
                StringBuilder sb = new StringBuilder();
                sb.AppendLine(string.IsNullOrWhiteSpace(plan.message) ? "AI returned a plan." : plan.message);
                int count = plan.actions != null ? plan.actions.Length : 0;
                sb.AppendLine("Planned actions: " + count);

                if (applyImmediately && count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine(ApplyAgentPlan(plan));
                    lastAgentPlan = null;
                }
                else if (count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("PLAN MODE: nothing has been changed.");
                    for (int i = 0; i < plan.actions.Length; i++)
                        sb.AppendLine((i + 1) + ". " + DescribeAction(plan.actions[i]));
                }
                else if (applyImmediately)
                {
                    sb.AppendLine();
                    sb.AppendLine("The model returned zero actions. For a creation request, try wording it as: CREATE this from scratch and make all needed GameObjects automatically.");
                }

                resultText = sb.ToString();
            }
            catch (Exception ex)
            {
                resultText = "Could not parse the agent plan: " + ex.Message + "\n\nMODEL RESPONSE:\n" + modelText;
            }
        }

        private string ApplyAgentPlan(AgentPlan plan)
        {
            if (plan.actions == null || plan.actions.Length == 0)
                return "No project changes were requested.";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("APPLYING AGENT ACTIONS:");
            bool wroteFiles = false;

            for (int i = 0; i < plan.actions.Length; i++)
            {
                AgentAction action = plan.actions[i];

                try
                {
                    string result;
                    string type = (action.type ?? "").Trim().ToLowerInvariant();

                    if (type == "create_gameobject")
                    {
                        result = ApplyCreateGameObject(action);
                    }
                    else
                    {
                        GameObject target = ResolveTarget(action.targetPath) ?? Selection.activeGameObject;

                        switch (type)
                        {
                            case "create_or_replace_file":
                                result = ApplyFileAction(action);
                                wroteFiles = true;
                                break;
                            case "add_component":
                                result = ApplyAddComponent(target, action);
                                break;
                            case "remove_component":
                                result = ApplyRemoveComponent(target, action);
                                break;
                            case "set_active":
                                result = ApplySetActive(target, action);
                                break;
                            case "set_local_position":
                                result = ApplyTransform(target, action, "position");
                                break;
                            case "set_local_rotation":
                                result = ApplyTransform(target, action, "rotation");
                                break;
                            case "set_local_scale":
                                result = ApplyTransform(target, action, "scale");
                                break;
                            case "set_component_field":
                                result = ApplyComponentField(target, action);
                                break;
                            default:
                                result = "SKIPPED unknown action: " + action.type;
                                break;
                        }
                    }

                    sb.AppendLine("✅ " + result);
                }
                catch (Exception ex)
                {
                    sb.AppendLine("❌ " + DescribeAction(action) + " -> " + ex.Message);
                }
            }

            if (wroteFiles)
                AssetDatabase.Refresh();

            GameObject selected = Selection.activeGameObject;
            if (selected != null && selected.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(selected.scene);

            sb.AppendLine("Done. If Unity recompiles a newly-created script, wait for compilation before asking the agent to attach that new custom script.");
            return sb.ToString();
        }

        private static string ApplyCreateGameObject(AgentAction action)
        {
            string objectName = SanitizeName(string.IsNullOrWhiteSpace(action.name) ? "AI_CreatedObject" : action.name);
            GameObject go;

            if (!string.IsNullOrWhiteSpace(action.primitive))
            {
                PrimitiveType primitiveType;
                if (!TryPrimitive(action.primitive, out primitiveType))
                    throw new InvalidOperationException("Unknown primitive for create_gameobject: " + action.primitive);
                go = GameObject.CreatePrimitive(primitiveType);
                go.name = objectName;
            }
            else if (string.Equals(action.componentType, "RectTransform", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(action.componentType, "UnityEngine.RectTransform", StringComparison.OrdinalIgnoreCase))
            {
                go = new GameObject(objectName, typeof(RectTransform));
            }
            else
            {
                go = new GameObject(objectName);
            }

            Undo.RegisterCreatedObjectUndo(go, "Primate Panic AI create GameObject");

            if (!string.IsNullOrWhiteSpace(action.parentPath) &&
                !string.Equals(action.parentPath, "NONE", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(action.parentPath, "ROOT", StringComparison.OrdinalIgnoreCase))
            {
                GameObject parent = ResolveTarget(action.parentPath);
                if (parent != null)
                    go.transform.SetParent(parent.transform, false);
            }

            go.transform.localPosition = new Vector3(action.x, action.y, action.z);

            bool active = !string.Equals(action.value, "false", StringComparison.OrdinalIgnoreCase);
            go.SetActive(active);

            Selection.activeGameObject = go;
            EditorGUIUtility.PingObject(go);
            if (go.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(go.scene);

            return "Created GameObject '" + go.name + "'" + (go.transform.parent != null ? " under '" + go.transform.parent.name + "'" : "");
        }

        private static string ApplyFileAction(AgentAction action)
        {
            if (string.IsNullOrWhiteSpace(action.path))
                throw new InvalidOperationException("File action has no path.");

            string normalized = action.path.Replace('\\', '/').Trim();
            if (!normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("File writes are restricted to Assets/.");
            if (!normalized.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) &&
                !normalized.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
                !normalized.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Only .cs, .json and .txt files may be written.");

            string fullPath = AssetPathToFullPath(normalized);
            string allowedRoot = Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar;
            string checkedPath = Path.GetFullPath(fullPath);
            if (!checkedPath.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Blocked path outside Assets/.");

            string directory = Path.GetDirectoryName(checkedPath);
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            if (File.Exists(checkedPath))
            {
                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                string backupRoot = Path.Combine(projectRoot, "Library", "PrimatePanicAIBackups");
                Directory.CreateDirectory(backupRoot);
                string safeName = normalized.Replace('/', '_').Replace('\\', '_');
                string backup = Path.Combine(backupRoot, DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + safeName);
                File.Copy(checkedPath, backup, true);
            }

            File.WriteAllText(checkedPath, action.content ?? "", new UTF8Encoding(false));
            return "Wrote " + normalized;
        }

        private static string ApplyAddComponent(GameObject target, AgentAction action)
        {
            RequireTarget(target);
            Type type = FindComponentType(action.componentType);
            if (type == null)
                throw new InvalidOperationException("Component type not found: " + action.componentType + ". If a custom script was just created, wait for Unity to compile and run Agent once more.");
            if (target.GetComponent(type) != null)
                return target.name + " already has " + type.Name;
            Undo.AddComponent(target, type);
            return "Added " + type.Name + " to " + target.name;
        }

        private static string ApplyRemoveComponent(GameObject target, AgentAction action)
        {
            RequireTarget(target);
            Type type = FindComponentType(action.componentType);
            if (type == null)
                throw new InvalidOperationException("Component type not found: " + action.componentType);
            Component component = target.GetComponent(type);
            if (component == null)
                return target.name + " does not have " + type.Name;
            Undo.DestroyObjectImmediate(component);
            return "Removed " + type.Name + " from " + target.name;
        }

        private static string ApplySetActive(GameObject target, AgentAction action)
        {
            RequireTarget(target);
            Undo.RecordObject(target, "Primate Panic AI set active");
            target.SetActive(action.boolValue);
            EditorUtility.SetDirty(target);
            return "Set " + target.name + " active=" + action.boolValue;
        }

        private static string ApplyTransform(GameObject target, AgentAction action, string kind)
        {
            RequireTarget(target);
            Undo.RecordObject(target.transform, "Primate Panic AI transform");
            Vector3 v = new Vector3(action.x, action.y, action.z);
            if (kind == "position") target.transform.localPosition = v;
            else if (kind == "rotation") target.transform.localEulerAngles = v;
            else target.transform.localScale = v;
            EditorUtility.SetDirty(target.transform);
            return "Set " + target.name + " local " + kind + " to " + v;
        }

        private static string ApplyComponentField(GameObject target, AgentAction action)
        {
            RequireTarget(target);
            Type type = FindComponentType(action.componentType);
            if (type == null)
                throw new InvalidOperationException("Component type not found: " + action.componentType);

            Component component = target.GetComponent(type);
            if (component == null)
                throw new InvalidOperationException(target.name + " has no " + type.Name + " component.");

            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            FieldInfo field = type.GetField(action.field ?? "", flags);
            PropertyInfo property = field == null ? type.GetProperty(action.field ?? "", flags) : null;
            Type valueType = field != null ? field.FieldType : property != null && property.CanWrite ? property.PropertyType : null;
            if (valueType == null)
                throw new InvalidOperationException("Writable field/property not found: " + action.field);

            object converted = ConvertValue(action.value, valueType);
            Undo.RecordObject(component, "Primate Panic AI set field");
            if (field != null) field.SetValue(component, converted);
            else property.SetValue(component, converted, null);
            EditorUtility.SetDirty(component);
            return "Set " + type.Name + "." + action.field + " = " + action.value;
        }

        private static object ConvertValue(string raw, Type type)
        {
            raw = raw ?? "";
            if (type == typeof(string)) return raw;
            if (type == typeof(bool)) return bool.Parse(raw);
            if (type == typeof(int)) return int.Parse(raw, CultureInfo.InvariantCulture);
            if (type == typeof(float)) return float.Parse(raw, CultureInfo.InvariantCulture);
            if (type == typeof(double)) return double.Parse(raw, CultureInfo.InvariantCulture);
            if (type.IsEnum) return Enum.Parse(type, raw, true);

            if (type == typeof(Vector2))
            {
                string[] p = raw.Replace("(", "").Replace(")", "").Split(',');
                if (p.Length != 2) throw new FormatException("Vector2 must be x,y");
                return new Vector2(
                    float.Parse(p[0], CultureInfo.InvariantCulture),
                    float.Parse(p[1], CultureInfo.InvariantCulture));
            }

            if (type == typeof(Vector3))
            {
                string[] p = raw.Replace("(", "").Replace(")", "").Split(',');
                if (p.Length != 3) throw new FormatException("Vector3 must be x,y,z");
                return new Vector3(
                    float.Parse(p[0], CultureInfo.InvariantCulture),
                    float.Parse(p[1], CultureInfo.InvariantCulture),
                    float.Parse(p[2], CultureInfo.InvariantCulture));
            }

            if (type == typeof(Color))
            {
                Color c;
                if (!ColorUtility.TryParseHtmlString(raw, out c))
                    throw new FormatException("Color must be HTML hex, e.g. #FFFFFF");
                return c;
            }

            if (typeof(UnityEngine.Object).IsAssignableFrom(type))
            {
                if (string.Equals(raw, "NONE", StringComparison.OrdinalIgnoreCase) || string.Equals(raw, "NULL", StringComparison.OrdinalIgnoreCase))
                    return null;

                GameObject go = ResolveTarget(raw);
                if (go == null)
                    throw new InvalidOperationException("Could not find referenced GameObject: " + raw);
                if (type == typeof(GameObject)) return go;
                if (type == typeof(Transform)) return go.transform;

                Component c = go.GetComponent(type);
                if (c == null)
                    throw new InvalidOperationException(go.name + " has no " + type.Name + " component for reference assignment.");
                return c;
            }

            throw new InvalidOperationException("Unsupported field type: " + type.FullName);
        }

        private static GameObject ResolveTarget(string pathOrName)
        {
            if (string.IsNullOrWhiteSpace(pathOrName) || string.Equals(pathOrName, "SELECTED", StringComparison.OrdinalIgnoreCase))
                return Selection.activeGameObject;

            string wanted = pathOrName.Trim().Trim('/');
            GameObject[] all = Resources.FindObjectsOfTypeAll<GameObject>();
            foreach (GameObject go in all)
            {
                if (go == null || !go.scene.IsValid())
                    continue;

                if (string.Equals(GetHierarchyPath(go.transform).Trim('/'), wanted, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(go.name, wanted, StringComparison.OrdinalIgnoreCase))
                    return go;
            }
            return null;
        }

        private static Type FindComponentType(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type direct = assembly.GetType(name, false, true);
                if (direct != null && typeof(Component).IsAssignableFrom(direct))
                    return direct;

                try
                {
                    Type[] types = assembly.GetTypes();
                    for (int i = 0; i < types.Length; i++)
                    {
                        if (typeof(Component).IsAssignableFrom(types[i]) && string.Equals(types[i].Name, name, StringComparison.OrdinalIgnoreCase))
                            return types[i];
                    }
                }
                catch (ReflectionTypeLoadException)
                {
                }
            }
            return null;
        }

        private static void RequireTarget(GameObject target)
        {
            if (target == null)
                throw new InvalidOperationException("No target GameObject was found. This action needs a specific targetPath. Creation requests should use create_gameobject first.");
        }

        private static string BuildAgentSystemPrompt()
        {
            return
                "You are an action-taking Unity Editor agent for a Gorilla-style VR game. Return ONLY valid JSON. " +
                "Your job is to DO the user's request, not ask them to manually build ordinary Unity objects. " +
                "CRITICAL CREATION RULE: A missing selection is NOT a reason to stop when the request asks to create/make/add/build a NEW feature. For new menus, loading screens, managers, portals, buttons, UI, systems, environment pieces, audio managers, spawners, etc., create a root GameObject and all useful child GameObjects automatically. " +
                "Only return actions:[] for a missing selection when the user clearly asks to modify/fix a specific existing object and you cannot identify it. " +
                "If an unrelated GameObject happens to be selected but the user asks for a new standalone system, DO NOT attach the new system to that unrelated selection. Create a new root. " +
                "Schema: {\"message\":\"short result\",\"actions\":[{" +
                "\"type\":\"create_gameobject|create_or_replace_file|add_component|remove_component|set_active|set_local_position|set_local_rotation|set_local_scale|set_component_field\"," +
                "\"targetPath\":\"SELECTED or object name/hierarchy path\",\"name\":\"new object name\",\"parentPath\":\"parent name/path or ROOT\",\"primitive\":\"optional Cube/Sphere/Capsule/Cylinder/Plane/Quad\"," +
                "\"path\":\"Assets/...\",\"content\":\"full file content\",\"componentType\":\"TypeName (or RectTransform on create_gameobject)\",\"field\":\"fieldName\",\"value\":\"value/reference; for create_gameobject use false only if object must start inactive\",\"boolValue\":true,\"x\":0,\"y\":0,\"z\":0}]} . " +
                "CREATE_GAMEOBJECT RULES: use create_gameobject whenever a needed object does not already exist. Actions are applied in order, so later actions can target a GameObject created earlier by its name. Use componentType=RectTransform when creating UI objects that should start with a RectTransform. " +
                "For a loading screen/menu, normally create a dedicated root/Canvas hierarchy instead of demanding a selection. Add built-in components such as Canvas, CanvasScaler, GraphicRaycaster when available. " +
                "For an existing broken script, replace that exact Script path with a COMPLETE compiling C# file. Never use ellipses or partial code. " +
                "If creating a brand-new custom MonoBehaviour, create the file. Do not try to attach that new custom class until Unity has compiled it; built-in components can be added in the same plan. " +
                "Do not add a second Rigidbody when one already exists. Preserve Gorilla locomotion unless explicitly asked otherwise. " +
                "For UnityEngine.Object references, set_component_field.value must be the target GameObject name or hierarchy path. " +
                "Never request shell commands, PowerShell, executables, registry changes, files outside Assets, or arbitrary project deletion. " +
                "Prefer concrete actions over explanation. message should be short.";
        }

        private static string DescribeAction(AgentAction a)
        {
            if (a == null) return "null action";
            string description = a.type ?? "unknown";
            if (!string.IsNullOrEmpty(a.name)) description += " " + a.name;
            if (!string.IsNullOrEmpty(a.path)) description += " " + a.path;
            if (!string.IsNullOrEmpty(a.componentType)) description += " " + a.componentType;
            if (!string.IsNullOrEmpty(a.field)) description += "." + a.field;
            return description;
        }

        private void PickImage()
        {
            string path = EditorUtility.OpenFilePanel("Pick reference picture", "", "png,jpg,jpeg");
            if (string.IsNullOrEmpty(path))
                return;

            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(bytes))
                {
                    DestroyImmediate(tex);
                    resultText = "Could not load that image.";
                    return;
                }

                if (preview != null)
                    DestroyImmediate(preview);

                preview = tex;
                imagePath = path;
                lastRecreationPlan = null;
                resultText = "Reference picture loaded. Ready to recreate.";
                Repaint();
            }
            catch (Exception ex)
            {
                resultText = "Could not open picture: " + ex.Message;
            }
        }

        private void ClearImage()
        {
            imagePath = "";
            if (preview != null)
            {
                DestroyImmediate(preview);
                preview = null;
            }
            lastRecreationPlan = null;
            resultText = "Picture cleared.";
        }

        private void SendRecreationRequest()
        {
            try
            {
                string imageBase64 = BuildOptimizedImageBase64(preview, 896);
                OllamaVisionRequest req = new OllamaVisionRequest
                {
                    model = visionModel.Trim(),
                    prompt = BuildVisionPrompt(picturePrompt),
                    stream = false,
                    format = "json",
                    images = new[] { imageBase64 },
                    keep_alive = "15m",
                    options = new OllamaOptions
                    {
                        num_ctx = 8192,
                        num_predict = 6000,
                        temperature = 0.15f
                    }
                };

                SendOllama(JsonUtility.ToJson(req), "Vision model is analyzing the picture...", 600, HandleRecreationResponse);
            }
            catch (Exception ex)
            {
                resultText = "Could not prepare reference picture: " + ex.Message;
            }
        }

        private static string BuildVisionPrompt(string userRequest)
        {
            return
                "You are a Unity 3D reconstruction agent. Analyze the attached reference image and return ONLY valid JSON. " +
                "Create a recognizable 3D BLOCKOUT from Unity primitives. Ignore editor UI, scene gizmos, red/blue rig handles, bone lines, transform arrows, grids, cameras and lights unless explicitly requested. " +
                "Focus on the actual visible object. Use meters and keep the reconstruction roughly 1 to 2.5 meters tall. " +
                "Prefer 8-35 meaningful parts; maximum 60. Available primitives: Cube, Sphere, Capsule, Cylinder, Plane, Quad. " +
                "Each object needs a unique id. parentId may be empty or the id of an earlier object. position/rotation/scale are LOCAL to the parent. " +
                "Use hexadecimal colors like #3A3A3A. Unless collision is important, keepCollider=false. " +
                "JSON schema: {\"message\":\"short summary\",\"rootName\":\"AI_Recreation\",\"objects\":[{" +
                "\"id\":\"part1\",\"parentId\":\"\",\"name\":\"Body\",\"primitive\":\"Capsule\",\"position\":{\"x\":0,\"y\":1,\"z\":0}," +
                "\"rotation\":{\"x\":0,\"y\":0,\"z\":0},\"scale\":{\"x\":1,\"y\":1,\"z\":1},\"color\":\"#808080\",\"keepCollider\":false}]}. " +
                "Do not include markdown fences or explanations outside JSON. USER REQUEST: " + userRequest;
        }

        private void HandleRecreationResponse(string modelText)
        {
            try
            {
                RecreationPlan plan = JsonUtility.FromJson<RecreationPlan>(ExtractJsonObject(modelText));
                if (plan == null || plan.objects == null || plan.objects.Length == 0)
                {
                    resultText = "Vision model did not return a usable 3D plan.\n\n" + modelText;
                    return;
                }

                if (plan.objects.Length > 60)
                {
                    RecreationObject[] bounded = new RecreationObject[60];
                    Array.Copy(plan.objects, bounded, 60);
                    plan.objects = bounded;
                }

                lastRecreationPlan = plan;
                resultText = (string.IsNullOrWhiteSpace(plan.message) ? "3D recreation planned." : plan.message) + "\nParts: " + plan.objects.Length;

                if (pictureAutoCreate)
                {
                    resultText += "\n\n" + ApplyRecreationPlan(plan);
                    lastRecreationPlan = null;
                }
                else
                {
                    resultText += "\nAuto-create is OFF. Click CREATE LAST PICTURE PLAN IN SCENE.";
                }
            }
            catch (Exception ex)
            {
                resultText = "Could not parse the vision plan: " + ex.Message + "\n\nMODEL RESPONSE:\n" + modelText;
            }
        }

        private string ApplyRecreationPlan(RecreationPlan plan)
        {
            string rootName = SanitizeName(string.IsNullOrWhiteSpace(plan.rootName) ? "AI_Recreation" : plan.rootName);
            GameObject root = new GameObject(rootName);
            Undo.RegisterCreatedObjectUndo(root, "AI Picture Recreation");

            Dictionary<string, GameObject> created = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
            int made = 0;

            foreach (RecreationObject item in plan.objects)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.id))
                    continue;

                PrimitiveType primitive;
                if (!TryPrimitive(item.primitive, out primitive))
                    primitive = PrimitiveType.Cube;

                GameObject go = GameObject.CreatePrimitive(primitive);
                go.name = SanitizeName(string.IsNullOrWhiteSpace(item.name) ? item.id : item.name);

                Transform parent = root.transform;
                GameObject parentObject;
                if (!string.IsNullOrWhiteSpace(item.parentId) && created.TryGetValue(item.parentId, out parentObject))
                    parent = parentObject.transform;

                go.transform.SetParent(parent, false);
                go.transform.localPosition = ToVector(item.position, Vector3.zero);
                go.transform.localEulerAngles = ToVector(item.rotation, Vector3.zero);
                go.transform.localScale = ClampScale(ToVector(item.scale, Vector3.one));
                ApplyColor(go, item.color);

                if (!item.keepCollider)
                {
                    Collider c = go.GetComponent<Collider>();
                    if (c != null)
                        DestroyImmediate(c);
                }

                created[item.id] = go;
                made++;
            }

            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);
            if (root.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(root.scene);

            return "✅ Created " + made + " 3D parts under '" + root.name + "'. Ctrl+Z will undo the recreation.";
        }

        private static void ApplyColor(GameObject go, string hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
                return;

            Color color;
            if (!ColorUtility.TryParseHtmlString(hex.Trim(), out color))
                return;

            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer == null)
                return;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) return;

            Material material = new Material(shader);
            material.name = "AI_" + go.name + "_Mat";
            material.color = color;
            renderer.sharedMaterial = material;
        }

        private static string BuildOptimizedImageBase64(Texture2D source, int maxDimension)
        {
            if (source == null)
                throw new InvalidOperationException("No picture selected.");

            int width = source.width;
            int height = source.height;
            float scale = Mathf.Min(1f, (float)maxDimension / Mathf.Max(width, height));
            int targetW = Mathf.Max(1, Mathf.RoundToInt(width * scale));
            int targetH = Mathf.Max(1, Mathf.RoundToInt(height * scale));

            RenderTexture rt = RenderTexture.GetTemporary(targetW, targetH, 0, RenderTextureFormat.ARGB32);
            RenderTexture old = RenderTexture.active;
            Graphics.Blit(source, rt);
            RenderTexture.active = rt;

            Texture2D resized = new Texture2D(targetW, targetH, TextureFormat.RGB24, false);
            resized.ReadPixels(new Rect(0, 0, targetW, targetH), 0, 0);
            resized.Apply();

            RenderTexture.active = old;
            RenderTexture.ReleaseTemporary(rt);

            byte[] jpg = resized.EncodeToJPG(82);
            DestroyImmediate(resized);
            return Convert.ToBase64String(jpg);
        }

        private void SendOllama(string json, string workingMessage, int timeoutSeconds, Action<string> onSuccess)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                resultText = "Ollama URL is empty.";
                return;
            }

            waiting = true;
            resultText = workingMessage;
            Repaint();

            UnityWebRequest request = new UnityWebRequest(endpoint.Trim(), "POST");
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = timeoutSeconds;

            UnityWebRequestAsyncOperation op = request.SendWebRequest();
            op.completed += _ =>
            {
                waiting = false;
                if (request.result != UnityWebRequest.Result.Success)
                {
                    resultText = "OLLAMA REQUEST FAILED\nHTTP " + request.responseCode + "\n" + request.error + "\n\n" + request.downloadHandler.text;
                }
                else
                {
                    string text = ExtractOllamaText(request.downloadHandler.text);
                    onSuccess(text);
                }

                request.Dispose();
                Repaint();
            };
        }

        private static string GetHierarchyPath(Transform t)
        {
            if (t == null) return "";
            List<string> names = new List<string>();
            while (t != null)
            {
                names.Add(t.name);
                t = t.parent;
            }
            names.Reverse();
            return string.Join("/", names.ToArray());
        }

        private static string AssetPathToFullPath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath)) return null;
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static string ExtractJsonObject(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException("The model returned an empty response.");

            string trimmed = text.Trim();
            if (trimmed.StartsWith("```"))
            {
                int firstNewline = trimmed.IndexOf('\n');
                int lastFence = trimmed.LastIndexOf("```");
                if (firstNewline >= 0 && lastFence > firstNewline)
                    trimmed = trimmed.Substring(firstNewline + 1, lastFence - firstNewline - 1).Trim();
            }

            int start = trimmed.IndexOf('{');
            int end = trimmed.LastIndexOf('}');
            if (start < 0 || end <= start)
                throw new InvalidOperationException("No JSON object was found.");
            return trimmed.Substring(start, end - start + 1);
        }

        private static string ExtractOllamaText(string json)
        {
            try
            {
                OllamaResponse response = JsonUtility.FromJson<OllamaResponse>(json);
                if (response == null) return "Ollama returned an empty response.";
                if (!string.IsNullOrEmpty(response.error)) return "OLLAMA ERROR: " + response.error;
                return response.response ?? "";
            }
            catch (Exception ex)
            {
                return "Could not parse Ollama response: " + ex.Message + "\n\nRaw response:\n" + json;
            }
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

        private static Vector3 ToVector(Vector3Data v, Vector3 fallback)
        {
            return v == null ? fallback : new Vector3(v.x, v.y, v.z);
        }

        private static Vector3 ClampScale(Vector3 v)
        {
            return new Vector3(
                Mathf.Clamp(Mathf.Abs(v.x), 0.01f, 10f),
                Mathf.Clamp(Mathf.Abs(v.y), 0.01f, 10f),
                Mathf.Clamp(Mathf.Abs(v.z), 0.01f, 10f));
        }

        private static string SanitizeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "AI_Part";
            return value.Replace('/', '_').Replace('\\', '_').Trim();
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
        private class OllamaOptions
        {
            public int num_ctx;
            public int num_predict;
            public float temperature;
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
            public string targetPath;
            public string name;
            public string parentPath;
            public string primitive;
            public string path;
            public string content;
            public string componentType;
            public string field;
            public string value;
            public bool boolValue;
            public float x;
            public float y;
            public float z;
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
            public Vector3Data position;
            public Vector3Data rotation;
            public Vector3Data scale;
            public string color;
            public bool keepCollider;
        }

        [Serializable]
        private class Vector3Data
        {
            public float x;
            public float y;
            public float z;
        }
    }
}
