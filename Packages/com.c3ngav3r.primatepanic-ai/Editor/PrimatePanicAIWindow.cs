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
            var sb = new StringBuilder();
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
        private const string ModelPref = "PrimatePanicAI.OllamaModel";
        private const string EndpointPref = "PrimatePanicAI.OllamaEndpoint";
        private const string AutoApplyPref = "PrimatePanicAI.AutoApply";
        private const string IncludeConsolePref = "PrimatePanicAI.IncludeConsole";
        private const string FastModePref = "PrimatePanicAI.FastMode";

        private string model = "qwen2.5-coder:7b";
        private string endpoint = "http://127.0.0.1:11434/api/generate";
        private string prompt = "";
        private string responseText = "";
        private Vector2 scroll;
        private bool waiting;
        private bool autoApply = true;
        private bool includeConsoleErrors = true;
        private bool fastMode = true;
        private AgentPlan lastPlan;

        [MenuItem("Tools/Primate Panic AI")]
        public static void Open()
        {
            GetWindow<PrimatePanicAIWindow>("Primate Panic AI");
        }

        private void OnEnable()
        {
            model = EditorPrefs.GetString(ModelPref, "qwen2.5-coder:7b");
            endpoint = EditorPrefs.GetString(EndpointPref, "http://127.0.0.1:11434/api/generate");
            autoApply = EditorPrefs.GetBool(AutoApplyPref, true);
            includeConsoleErrors = EditorPrefs.GetBool(IncludeConsolePref, true);
            fastMode = EditorPrefs.GetBool(FastModePref, true);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Primate Panic AI - LOCAL AGENT", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "v0.3.1: Fast Mode uses smaller prompts and recent Console errors are captured in memory. It no longer reads Unity's Editor.log file, so the old file-permission error is gone.",
                MessageType.Info
            );

            EditorGUI.BeginChangeCheck();
            model = EditorGUILayout.TextField("Ollama Model", model);
            endpoint = EditorGUILayout.TextField("Ollama URL", endpoint);
            fastMode = EditorGUILayout.ToggleLeft("Fast Mode (recommended)", fastMode);
            includeConsoleErrors = EditorGUILayout.ToggleLeft("Include recent Console errors", includeConsoleErrors);
            autoApply = EditorGUILayout.ToggleLeft("Apply AI actions automatically", autoApply);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetString(ModelPref, model);
                EditorPrefs.SetString(EndpointPref, endpoint);
                EditorPrefs.SetBool(FastModePref, fastMode);
                EditorPrefs.SetBool(IncludeConsolePref, includeConsoleErrors);
                EditorPrefs.SetBool(AutoApplyPref, autoApply);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Use Fast 3B", GUILayout.Height(24)))
                {
                    model = "qwen2.5-coder:3b";
                    EditorPrefs.SetString(ModelPref, model);
                    responseText = "Fast model selected: qwen2.5-coder:3b\nIf it is not installed yet, run this once in CMD:\nollama run qwen2.5-coder:3b";
                }

                if (GUILayout.Button("Use Better 7B", GUILayout.Height(24)))
                {
                    model = "qwen2.5-coder:7b";
                    EditorPrefs.SetString(ModelPref, model);
                }
            }

            if (autoApply)
                EditorGUILayout.HelpBox("AUTO APPLY IS ON: RUN AGENT may edit scripts and the selected GameObject immediately. Script replacements are backed up under Library/PrimatePanicAIBackups.", MessageType.Warning);

            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.enabled = !waiting;
                if (GUILayout.Button("Test Ollama", GUILayout.Height(28)))
                    SendChatRequest("Reply with exactly: OLLAMA CONNECTED", true);

                if (GUILayout.Button("Inspect Selected", GUILayout.Height(28)))
                    prompt = BuildSelectedObjectContext(false, true);

                if (GUILayout.Button("Clear", GUILayout.Height(28)))
                {
                    prompt = "";
                    responseText = "";
                    lastPlan = null;
                }
                GUI.enabled = true;
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("What do you want changed?", EditorStyles.boldLabel);
            prompt = EditorGUILayout.TextArea(prompt, GUILayout.MinHeight(100));

            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.enabled = !waiting && !string.IsNullOrWhiteSpace(prompt) && !string.IsNullOrWhiteSpace(model);
                if (GUILayout.Button(waiting ? "Working..." : "Ask Only", GUILayout.Height(34)))
                    SendChatRequest(BuildUserRequestWithContext(false), false);

                if (GUILayout.Button(waiting ? "Working..." : "RUN AGENT", GUILayout.Height(34)))
                    SendAgentRequest();
                GUI.enabled = true;
            }

            if (!autoApply && lastPlan != null && lastPlan.actions != null && lastPlan.actions.Length > 0)
            {
                EditorGUILayout.Space(4);
                if (GUILayout.Button("Apply Last Agent Plan", GUILayout.Height(30)))
                {
                    responseText += "\n\n" + ApplyPlan(lastPlan);
                    lastPlan = null;
                }
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Result", EditorStyles.boldLabel);
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MinHeight(220));
            EditorGUILayout.TextArea(responseText, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private string BuildUserRequestWithContext(bool includeScriptSource)
        {
            var sb = new StringBuilder();
            sb.AppendLine("USER REQUEST:");
            sb.AppendLine(prompt.Trim());
            sb.AppendLine();
            sb.AppendLine(BuildSelectedObjectContext(includeScriptSource, false));

            if (includeConsoleErrors)
            {
                sb.AppendLine();
                sb.AppendLine("RECENT UNITY CONSOLE ERRORS (IN-MEMORY, BOUNDED):");
                sb.AppendLine(PrimatePanicAIRecentLogs.GetRecent(fastMode ? 6 : 12));
            }

            return sb.ToString();
        }

        private string BuildSelectedObjectContext(bool includeScriptSource, bool standalone)
        {
            GameObject go = Selection.activeGameObject;
            if (go == null)
                return standalone ? "No GameObject is selected." : "SELECTED GAMEOBJECT: NONE";

            var sb = new StringBuilder();
            if (standalone)
                sb.AppendLine("Selected object inspection:");

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
            int maxScriptCharacters = fastMode ? 10000 : 32000;
            int maxSingleScript = fastMode ? 8000 : 20000;

            Component[] components = go.GetComponents<Component>();
            foreach (Component c in components)
            {
                if (c == null)
                {
                    sb.AppendLine("- MISSING SCRIPT / NULL COMPONENT");
                    continue;
                }

                Type componentType = c.GetType();
                sb.AppendLine("- " + componentType.FullName);

                if (c is Rigidbody rb)
                {
                    sb.AppendLine("  mass=" + rb.mass + ", gravity=" + rb.useGravity + ", kinematic=" + rb.isKinematic + ", constraints=" + rb.constraints);
                }
                else if (c is Collider col)
                {
                    sb.AppendLine("  collider enabled=" + col.enabled + ", trigger=" + col.isTrigger);
                }
                else if (c is Animator animator)
                {
                    sb.AppendLine("  animator enabled=" + animator.enabled + ", rootMotion=" + animator.applyRootMotion);
                }

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

        private void SendChatRequest(string text, bool connectionTest)
        {
            var request = new OllamaRequest
            {
                model = model.Trim(),
                prompt = text,
                stream = false,
                keep_alive = "15m",
                options = BuildOptions(false)
            };
            SendOllama(JsonUtility.ToJson(request), connectionTest, false);
        }

        private void SendAgentRequest()
        {
            string context = BuildUserRequestWithContext(true);
            var request = new OllamaAgentRequest
            {
                model = model.Trim(),
                system = BuildAgentSystemPrompt(),
                prompt = context,
                stream = false,
                format = "json",
                keep_alive = "15m",
                options = BuildOptions(true)
            };
            SendOllama(JsonUtility.ToJson(request), false, true);
        }

        private OllamaOptions BuildOptions(bool agentMode)
        {
            return new OllamaOptions
            {
                temperature = 0.1f,
                num_ctx = fastMode ? 4096 : 8192,
                num_predict = agentMode ? (fastMode ? 2600 : 5000) : (fastMode ? 900 : 1800)
            };
        }

        private void SendOllama(string json, bool connectionTest, bool agentMode)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                responseText = "Ollama URL is empty.";
                return;
            }

            waiting = true;
            responseText = connectionTest ? "Testing Ollama..." : agentMode ? "Fast agent is inspecting and planning changes..." : "Sending to local Ollama...";
            Repaint();

            UnityWebRequest request = new UnityWebRequest(endpoint.Trim(), "POST");
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = 300;

            var op = request.SendWebRequest();
            op.completed += _ =>
            {
                waiting = false;
                if (request.result != UnityWebRequest.Result.Success)
                {
                    responseText = "OLLAMA CONNECTION FAILED\nHTTP: " + request.responseCode + "\n" + request.error + "\n\n" + request.downloadHandler.text;
                }
                else
                {
                    string modelText = ExtractOllamaText(request.downloadHandler.text);
                    if (agentMode)
                        HandleAgentResponse(modelText);
                    else
                    {
                        responseText = modelText;
                        if (connectionTest && !responseText.StartsWith("OLLAMA", StringComparison.OrdinalIgnoreCase))
                            responseText = "OLLAMA CONNECTED ✅\n\nModel reply:\n" + responseText;
                    }
                }

                request.Dispose();
                Repaint();
            };
        }

        private void HandleAgentResponse(string modelText)
        {
            try
            {
                string json = ExtractJsonObject(modelText);
                AgentPlan plan = JsonUtility.FromJson<AgentPlan>(json);
                if (plan == null)
                {
                    responseText = "The local model did not return a usable agent plan.\n\n" + modelText;
                    return;
                }

                lastPlan = plan;
                var sb = new StringBuilder();
                sb.AppendLine(string.IsNullOrWhiteSpace(plan.message) ? "Agent returned a plan." : plan.message);
                int actionCount = plan.actions != null ? plan.actions.Length : 0;
                sb.AppendLine("Planned actions: " + actionCount);

                if (autoApply && actionCount > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine(ApplyPlan(plan));
                    lastPlan = null;
                }
                else if (actionCount > 0)
                {
                    sb.AppendLine("Auto Apply is OFF. Click Apply Last Agent Plan when ready.");
                    for (int i = 0; i < plan.actions.Length; i++)
                        sb.AppendLine((i + 1) + ". " + DescribeAction(plan.actions[i]));
                }

                responseText = sb.ToString();
            }
            catch (Exception ex)
            {
                responseText = "Could not parse the AI agent plan.\n" + ex.Message + "\n\nMODEL RESPONSE:\n" + modelText;
            }
        }

        private string ApplyPlan(AgentPlan plan)
        {
            if (plan.actions == null || plan.actions.Length == 0)
                return "No project changes were requested.";

            var sb = new StringBuilder();
            sb.AppendLine("APPLYING AGENT ACTIONS:");
            bool wroteFiles = false;

            for (int i = 0; i < plan.actions.Length; i++)
            {
                AgentAction action = plan.actions[i];
                GameObject target = ResolveTarget(action.targetPath) ?? Selection.activeGameObject;
                try
                {
                    string result;
                    switch ((action.type ?? "").Trim().ToLowerInvariant())
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

            sb.AppendLine("Done. If Unity recompiles scripts, wait for compilation before testing or running the agent again.");
            return sb.ToString();
        }

        private static string ApplyFileAction(AgentAction action)
        {
            if (string.IsNullOrWhiteSpace(action.path))
                throw new InvalidOperationException("File action has no path.");

            string normalized = action.path.Replace('\\', '/').Trim();
            if (!normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("File writes are restricted to Assets/.");
            if (!normalized.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) && !normalized.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && !normalized.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
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
                throw new InvalidOperationException("Component type not found: " + action.componentType + ". If this script was just created, wait for Unity to compile and run the agent again.");
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

            if (type == typeof(Vector3))
            {
                string[] p = raw.Replace("(", "").Replace(")", "").Split(',');
                if (p.Length != 3) throw new FormatException("Vector3 must be x,y,z");
                return new Vector3(float.Parse(p[0], CultureInfo.InvariantCulture), float.Parse(p[1], CultureInfo.InvariantCulture), float.Parse(p[2], CultureInfo.InvariantCulture));
            }

            if (typeof(UnityEngine.Object).IsAssignableFrom(type))
            {
                GameObject go = ResolveTarget(raw);
                if (go == null)
                {
                    if (string.Equals(raw, "NONE", StringComparison.OrdinalIgnoreCase) || string.Equals(raw, "NULL", StringComparison.OrdinalIgnoreCase))
                        return null;
                    throw new InvalidOperationException("Could not find referenced GameObject: " + raw);
                }
                if (type == typeof(GameObject)) return go;
                if (type == typeof(Transform)) return go.transform;
                Component c = go.GetComponent(type);
                if (c == null) throw new InvalidOperationException(go.name + " has no " + type.Name + " component for reference assignment.");
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
                if (go == null || !go.scene.IsValid()) continue;
                if (string.Equals(GetHierarchyPath(go.transform).Trim('/'), wanted, StringComparison.OrdinalIgnoreCase) || string.Equals(go.name, wanted, StringComparison.OrdinalIgnoreCase))
                    return go;
            }
            return null;
        }

        private static Type FindComponentType(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(name, false, true);
                if (type != null && typeof(Component).IsAssignableFrom(type)) return type;
                try
                {
                    Type[] types = assembly.GetTypes();
                    for (int i = 0; i < types.Length; i++)
                        if (typeof(Component).IsAssignableFrom(types[i]) && string.Equals(types[i].Name, name, StringComparison.OrdinalIgnoreCase))
                            return types[i];
                }
                catch (ReflectionTypeLoadException) { }
            }
            return null;
        }

        private static void RequireTarget(GameObject target)
        {
            if (target == null)
                throw new InvalidOperationException("No target GameObject was found. Select the object or provide targetPath.");
        }

        private static string BuildAgentSystemPrompt()
        {
            return
                "You are an action-taking Unity Editor agent for a Gorilla-style VR game. " +
                "Do not give generic troubleshooting when a concrete action can be taken. Inspect the supplied selected-object data and script source, then return ONLY valid JSON. " +
                "Schema: {\"message\":\"short result\",\"actions\":[{\"type\":\"...\",\"targetPath\":\"SELECTED or hierarchy path\",\"path\":\"Assets/...\",\"content\":\"full file content\",\"componentType\":\"TypeName\",\"field\":\"fieldName\",\"value\":\"value or referenced GameObject path/name\",\"boolValue\":true,\"x\":0,\"y\":0,\"z\":0}]} . " +
                "Supported action types: create_or_replace_file, add_component, remove_component, set_active, set_local_position, set_local_rotation, set_local_scale, set_component_field. " +
                "For an existing script that is wrong, replace that exact Script path with a COMPLETE compiling C# file. Never use ellipses or partial code. " +
                "Do not add a second Rigidbody when one already exists. Preserve Gorilla locomotion unless the user explicitly requests otherwise. " +
                "For UnityEngine.Object references, set_component_field.value must be the target GameObject name or hierarchy path. " +
                "If creating a brand-new MonoBehaviour script, create the file now but do not add the component in the same plan because Unity must compile first. " +
                "Never request shell commands, PowerShell, executables, registry changes, files outside Assets, or deletion of arbitrary project files. " +
                "If there is not enough evidence to safely edit something, return actions:[] and state the ONE specific missing selection/reference in message.";
        }

        private static string DescribeAction(AgentAction a)
        {
            if (a == null) return "null action";
            return (a.type ?? "unknown") + (string.IsNullOrEmpty(a.path) ? "" : " " + a.path) + (string.IsNullOrEmpty(a.componentType) ? "" : " " + a.componentType) + (string.IsNullOrEmpty(a.field) ? "" : "." + a.field);
        }

        private static string GetHierarchyPath(Transform t)
        {
            if (t == null) return "";
            var names = new List<string>();
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
            if (string.IsNullOrWhiteSpace(text)) return "{}";
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
            if (start >= 0 && end >= start)
                return trimmed.Substring(start, end - start + 1);
            return trimmed;
        }

        private static string ExtractOllamaText(string json)
        {
            try
            {
                OllamaResponse result = JsonUtility.FromJson<OllamaResponse>(json);
                if (result == null) return "Ollama returned an empty response.";
                if (!string.IsNullOrEmpty(result.error)) return "OLLAMA ERROR: " + result.error;
                if (!string.IsNullOrEmpty(result.response)) return result.response;
                return "Ollama returned no readable text.\n\nRaw response:\n" + json;
            }
            catch (Exception ex)
            {
                return "Could not parse Ollama response: " + ex.Message + "\n\nRaw response:\n" + json;
            }
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
        private class OllamaOptions
        {
            public float temperature;
            public int num_ctx;
            public int num_predict;
        }

        [Serializable]
        private class OllamaResponse
        {
            public string model;
            public string response;
            public bool done;
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
    }
}
