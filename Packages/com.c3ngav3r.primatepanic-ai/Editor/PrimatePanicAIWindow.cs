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
    public class PrimatePanicAIWindow : EditorWindow
    {
        private const string ModelPref = "PrimatePanicAI.OllamaModel";
        private const string EndpointPref = "PrimatePanicAI.OllamaEndpoint";
        private const string AutoApplyPref = "PrimatePanicAI.AutoApply";
        private const string IncludeLogPref = "PrimatePanicAI.IncludeLog";

        private string model = "qwen2.5-coder:7b";
        private string endpoint = "http://127.0.0.1:11434/api/generate";
        private string prompt = "";
        private string responseText = "";
        private Vector2 scroll;
        private bool waiting;
        private bool autoApply = true;
        private bool includeEditorLog = true;
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
            includeEditorLog = EditorPrefs.GetBool(IncludeLogPref, true);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Primate Panic AI - LOCAL AGENT", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Runs through Ollama on your own PC. Agent Mode can directly edit scripts under Assets and modify the currently selected GameObject. Script replacements are backed up under Library/PrimatePanicAIBackups.",
                MessageType.Info
            );

            EditorGUI.BeginChangeCheck();
            model = EditorGUILayout.TextField("Ollama Model", model);
            endpoint = EditorGUILayout.TextField("Ollama URL", endpoint);
            autoApply = EditorGUILayout.ToggleLeft("Apply AI actions automatically", autoApply);
            includeEditorLog = EditorGUILayout.ToggleLeft("Include recent Unity Editor log", includeEditorLog);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetString(ModelPref, model);
                EditorPrefs.SetString(EndpointPref, endpoint);
                EditorPrefs.SetBool(AutoApplyPref, autoApply);
                EditorPrefs.SetBool(IncludeLogPref, includeEditorLog);
            }

            if (autoApply)
            {
                EditorGUILayout.HelpBox(
                    "AUTO APPLY IS ON: Run Agent can change your project immediately. Use source control/backups for anything important.",
                    MessageType.Warning
                );
            }

            EditorGUILayout.Space(8);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.enabled = !waiting;
                if (GUILayout.Button("Test Ollama", GUILayout.Height(28)))
                    SendChatRequest("Reply with exactly: OLLAMA CONNECTED", true);

                if (GUILayout.Button("Inspect Selected", GUILayout.Height(28)))
                    prompt = BuildSelectedObjectContext(true);

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
            prompt = EditorGUILayout.TextArea(prompt, GUILayout.MinHeight(110));

            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.enabled = !waiting && !string.IsNullOrWhiteSpace(prompt) && !string.IsNullOrWhiteSpace(model);

                if (GUILayout.Button(waiting ? "Working..." : "Ask Only", GUILayout.Height(34)))
                    SendChatRequest(BuildUserRequestWithContext(), false);

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
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MinHeight(240));
            EditorGUILayout.TextArea(responseText, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private string BuildUserRequestWithContext()
        {
            var sb = new StringBuilder();
            sb.AppendLine("USER REQUEST:");
            sb.AppendLine(prompt);
            sb.AppendLine();
            sb.AppendLine(BuildSelectedObjectContext(false));

            if (includeEditorLog)
            {
                sb.AppendLine();
                sb.AppendLine("RECENT UNITY EDITOR LOG:");
                sb.AppendLine(ReadEditorLogTail(9000));
            }

            return sb.ToString();
        }

        private string BuildSelectedObjectContext(bool standalone)
        {
            GameObject go = Selection.activeGameObject;
            if (go == null)
                return standalone
                    ? "No GameObject is selected. Select the object you want the AI to inspect or change."
                    : "SELECTED GAMEOBJECT: NONE";

            var sb = new StringBuilder();
            if (standalone)
                sb.AppendLine("Use this inspection as context for your request:");

            sb.AppendLine("SELECTED GAMEOBJECT:");
            sb.AppendLine("Name: " + go.name);
            sb.AppendLine("Hierarchy path: " + GetHierarchyPath(go.transform));
            sb.AppendLine("Active self: " + go.activeSelf);
            sb.AppendLine("Active in hierarchy: " + go.activeInHierarchy);
            sb.AppendLine("Layer: " + LayerMask.LayerToName(go.layer));
            sb.AppendLine("Tag: " + go.tag);
            sb.AppendLine("Local position: " + go.transform.localPosition);
            sb.AppendLine("Local rotation: " + go.transform.localEulerAngles);
            sb.AppendLine("Local scale: " + go.transform.localScale);
            sb.AppendLine("Parent: " + (go.transform.parent != null ? go.transform.parent.name : "NONE"));
            sb.AppendLine("Components:");

            int scriptCharacters = 0;
            const int maxScriptCharacters = 32000;

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
                {
                    sb.AppendLine("  mass=" + rb.mass +
                                  ", useGravity=" + rb.useGravity +
                                  ", isKinematic=" + rb.isKinematic +
                                  ", constraints=" + rb.constraints +
                                  ", interpolation=" + rb.interpolation +
                                  ", collisionDetection=" + rb.collisionDetectionMode);
                }
                else if (c is Collider col)
                {
                    sb.AppendLine("  enabled=" + col.enabled +
                                  ", isTrigger=" + col.isTrigger +
                                  ", material=" + (col.sharedMaterial != null ? col.sharedMaterial.name : "NONE"));
                }
                else if (c is Animator animator)
                {
                    sb.AppendLine("  enabled=" + animator.enabled +
                                  ", applyRootMotion=" + animator.applyRootMotion +
                                  ", updateMode=" + animator.updateMode);
                }

                MonoBehaviour behaviour = c as MonoBehaviour;
                if (behaviour != null && scriptCharacters < maxScriptCharacters)
                {
                    MonoScript script = MonoScript.FromMonoBehaviour(behaviour);
                    if (script != null)
                    {
                        string assetPath = AssetDatabase.GetAssetPath(script);
                        sb.AppendLine("  Script path: " + assetPath);

                        string fullPath = AssetPathToFullPath(assetPath);
                        if (!string.IsNullOrEmpty(fullPath) && File.Exists(fullPath))
                        {
                            try
                            {
                                string source = File.ReadAllText(fullPath);
                                int remaining = maxScriptCharacters - scriptCharacters;
                                if (source.Length > remaining)
                                    source = source.Substring(0, remaining) + "\n// SOURCE TRUNCATED BY AGENT";

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
                    }
                }
            }

            return sb.ToString();
        }

        private void SendChatRequest(string text, bool connectionTest)
        {
            SendOllama(
                JsonUtility.ToJson(new OllamaRequest
                {
                    model = model.Trim(),
                    prompt = text,
                    stream = false
                }),
                connectionTest,
                false
            );
        }

        private void SendAgentRequest()
        {
            string context = BuildUserRequestWithContext();
            string system = BuildAgentSystemPrompt();

            string json = JsonUtility.ToJson(new OllamaAgentRequest
            {
                model = model.Trim(),
                system = system,
                prompt = context,
                stream = false,
                format = "json"
            });

            SendOllama(json, false, true);
        }

        private void SendOllama(string json, bool connectionTest, bool agentMode)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                responseText = "Ollama URL is empty.";
                return;
            }

            waiting = true;
            responseText = connectionTest
                ? "Testing Ollama..."
                : agentMode ? "Agent is inspecting and planning changes..." : "Sending to local Ollama...";
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
                    responseText =
                        "OLLAMA CONNECTION FAILED\n\n" +
                        "Make sure Ollama is running and the model is installed.\n" +
                        "URL: " + endpoint + "\n" +
                        "Model: " + model + "\n\n" +
                        "HTTP: " + request.responseCode + "\n" +
                        request.error + "\n\n" +
                        request.downloadHandler.text;
                }
                else
                {
                    string modelText = ExtractOllamaText(request.downloadHandler.text);

                    if (agentMode)
                        HandleAgentResponse(modelText);
                    else
                    {
                        responseText = modelText;
                        if (connectionTest && !responseText.StartsWith("OLLAMA"))
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
                sb.AppendLine();
                sb.AppendLine("Planned actions: " + actionCount);

                if (autoApply && actionCount > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine(ApplyPlan(plan));
                    lastPlan = null;
                }
                else if (actionCount > 0)
                {
                    sb.AppendLine("Auto Apply is OFF. Review this result, then click Apply Last Agent Plan.");
                    sb.AppendLine();
                    for (int i = 0; i < plan.actions.Length; i++)
                        sb.AppendLine((i + 1) + ". " + DescribeAction(plan.actions[i]));
                }

                responseText = sb.ToString();
            }
            catch (Exception ex)
            {
                responseText =
                    "Could not parse the AI agent plan.\n" +
                    ex.Message + "\n\nMODEL RESPONSE:\n" + modelText;
            }
        }

        private string ApplyPlan(AgentPlan plan)
        {
            if (plan.actions == null || plan.actions.Length == 0)
                return "No project changes were requested.";

            var sb = new StringBuilder();
            sb.AppendLine("APPLYING AGENT ACTIONS:");

            GameObject selected = Selection.activeGameObject;
            bool wroteFiles = false;

            for (int i = 0; i < plan.actions.Length; i++)
            {
                AgentAction action = plan.actions[i];
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
                            result = ApplyAddComponent(selected, action);
                            break;

                        case "remove_component":
                            result = ApplyRemoveComponent(selected, action);
                            break;

                        case "set_active":
                            result = ApplySetActive(selected, action);
                            break;

                        case "set_local_position":
                            result = ApplyTransform(selected, action, "position");
                            break;

                        case "set_local_rotation":
                            result = ApplyTransform(selected, action, "rotation");
                            break;

                        case "set_local_scale":
                            result = ApplyTransform(selected, action, "scale");
                            break;

                        case "set_component_field":
                            result = ApplyComponentField(selected, action);
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

            if (selected != null && selected.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(selected.scene);

            sb.AppendLine();
            sb.AppendLine("Done. If Unity recompiles scripts, wait for compilation to finish before testing.");
            return sb.ToString();
        }

        private static string ApplyFileAction(AgentAction action)
        {
            if (string.IsNullOrWhiteSpace(action.path))
                throw new InvalidOperationException("File action has no path.");

            string normalized = action.path.Replace('\\', '/').Trim();
            if (!normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Agent file writes are restricted to Assets/.");

            if (!normalized.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) &&
                !normalized.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
                !normalized.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Only .cs, .json and .txt files may be written by Agent Mode.");

            string fullPath = AssetPathToFullPath(normalized);
            if (string.IsNullOrEmpty(fullPath))
                throw new InvalidOperationException("Could not resolve project file path.");

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string allowedRoot = Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar;
            string checkedPath = Path.GetFullPath(fullPath);
            if (!checkedPath.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Blocked path outside Assets/.");

            string directory = Path.GetDirectoryName(checkedPath);
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            if (File.Exists(checkedPath))
            {
                string backupRoot = Path.Combine(projectRoot, "Library", "PrimatePanicAIBackups", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                string relative = normalized.Substring("Assets/".Length).Replace('/', Path.DirectorySeparatorChar);
                string backupPath = Path.Combine(backupRoot, relative);
                string backupDir = Path.GetDirectoryName(backupPath);
                if (!Directory.Exists(backupDir))
                    Directory.CreateDirectory(backupDir);
                File.Copy(checkedPath, backupPath, true);
            }

            File.WriteAllText(checkedPath, action.content ?? "", new UTF8Encoding(false));
            return "Wrote " + normalized;
        }

        private static string ApplyAddComponent(GameObject selected, AgentAction action)
        {
            EnsureSelected(selected);
            Type type = FindType(action.componentType);
            if (type == null || !typeof(Component).IsAssignableFrom(type))
                throw new InvalidOperationException("Component type not found: " + action.componentType);

            if (type == typeof(Transform))
                throw new InvalidOperationException("Transform cannot be added manually.");

            Component existing = selected.GetComponent(type);
            if (existing != null)
                return selected.name + " already has " + type.Name;

            Undo.AddComponent(selected, type);
            return "Added " + type.Name + " to " + selected.name;
        }

        private static string ApplyRemoveComponent(GameObject selected, AgentAction action)
        {
            EnsureSelected(selected);
            Type type = FindType(action.componentType);
            if (type == null || !typeof(Component).IsAssignableFrom(type))
                throw new InvalidOperationException("Component type not found: " + action.componentType);

            Component component = selected.GetComponent(type);
            if (component == null)
                return selected.name + " does not have " + type.Name;

            if (component is Transform)
                throw new InvalidOperationException("Transform cannot be removed.");

            Undo.DestroyObjectImmediate(component);
            return "Removed " + type.Name + " from " + selected.name;
        }

        private static string ApplySetActive(GameObject selected, AgentAction action)
        {
            EnsureSelected(selected);
            Undo.RecordObject(selected, "Primate Panic AI - Set Active");
            selected.SetActive(action.boolValue);
            return "Set " + selected.name + " active=" + action.boolValue;
        }

        private static string ApplyTransform(GameObject selected, AgentAction action, string mode)
        {
            EnsureSelected(selected);
            Undo.RecordObject(selected.transform, "Primate Panic AI - Transform");
            Vector3 value = new Vector3(action.x, action.y, action.z);

            switch (mode)
            {
                case "position":
                    selected.transform.localPosition = value;
                    break;
                case "rotation":
                    selected.transform.localEulerAngles = value;
                    break;
                case "scale":
                    selected.transform.localScale = value;
                    break;
            }

            return "Set local " + mode + " of " + selected.name + " to " + value;
        }

        private static string ApplyComponentField(GameObject selected, AgentAction action)
        {
            EnsureSelected(selected);
            Type type = FindType(action.componentType);
            if (type == null || !typeof(Component).IsAssignableFrom(type))
                throw new InvalidOperationException("Component type not found: " + action.componentType);

            Component component = selected.GetComponent(type);
            if (component == null)
                throw new InvalidOperationException(selected.name + " has no " + type.Name + " component.");

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            FieldInfo field = type.GetField(action.fieldName, flags);
            PropertyInfo property = type.GetProperty(action.fieldName, flags);

            if (field == null && property == null)
                throw new InvalidOperationException("Member not found: " + type.Name + "." + action.fieldName);

            Type valueType = field != null ? field.FieldType : property.PropertyType;
            object converted = ConvertAgentValue(valueType, action.value, action.objectName);

            Undo.RecordObject(component, "Primate Panic AI - Set Component Field");

            if (field != null)
                field.SetValue(component, converted);
            else
            {
                if (!property.CanWrite)
                    throw new InvalidOperationException("Property is read-only: " + action.fieldName);
                property.SetValue(component, converted, null);
            }

            EditorUtility.SetDirty(component);
            return "Set " + type.Name + "." + action.fieldName + " on " + selected.name;
        }

        private static object ConvertAgentValue(Type targetType, string value, string objectName)
        {
            if (typeof(UnityEngine.Object).IsAssignableFrom(targetType))
            {
                GameObject go = FindSceneObjectByName(objectName);
                if (go == null)
                    throw new InvalidOperationException("Scene object not found: " + objectName);

                if (targetType == typeof(GameObject))
                    return go;
                if (targetType == typeof(Transform))
                    return go.transform;
                if (typeof(Component).IsAssignableFrom(targetType))
                {
                    Component component = go.GetComponent(targetType);
                    if (component == null)
                        throw new InvalidOperationException(objectName + " has no " + targetType.Name + " component.");
                    return component;
                }
            }

            if (targetType == typeof(string)) return value ?? "";
            if (targetType == typeof(bool)) return bool.Parse(value);
            if (targetType == typeof(int)) return int.Parse(value, CultureInfo.InvariantCulture);
            if (targetType == typeof(float)) return float.Parse(value, CultureInfo.InvariantCulture);
            if (targetType == typeof(double)) return double.Parse(value, CultureInfo.InvariantCulture);
            if (targetType.IsEnum) return Enum.Parse(targetType, value, true);

            throw new InvalidOperationException("Unsupported field type: " + targetType.FullName);
        }

        private static GameObject FindSceneObjectByName(string objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName))
                return null;

            GameObject[] objects = Resources.FindObjectsOfTypeAll<GameObject>();
            foreach (GameObject go in objects)
            {
                if (go == null || !go.scene.IsValid())
                    continue;
                if (string.Equals(go.name, objectName, StringComparison.OrdinalIgnoreCase))
                    return go;
            }
            return null;
        }

        private static Type FindType(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return null;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type direct = assembly.GetType(typeName, false, true);
                if (direct != null)
                    return direct;

                try
                {
                    foreach (Type type in assembly.GetTypes())
                    {
                        if (string.Equals(type.Name, typeName, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(type.FullName, typeName, StringComparison.OrdinalIgnoreCase))
                            return type;
                    }
                }
                catch (ReflectionTypeLoadException)
                {
                    // Ignore assemblies with partially unloadable types.
                }
            }

            return null;
        }

        private static void EnsureSelected(GameObject selected)
        {
            if (selected == null)
                throw new InvalidOperationException("No GameObject is selected.");
        }

        private static string GetHierarchyPath(Transform transform)
        {
            string path = transform.name;
            Transform current = transform.parent;
            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }
            return path;
        }

        private static string AssetPathToFullPath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath) || !assetPath.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
                return null;

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static string ReadEditorLogTail(int maxChars)
        {
            try
            {
                string path = Application.consoleLogPath;
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    return "Editor log unavailable.";

                string text = File.ReadAllText(path);
                if (text.Length <= maxChars)
                    return text;
                return text.Substring(text.Length - maxChars);
            }
            catch (Exception ex)
            {
                return "Could not read Editor log: " + ex.Message;
            }
        }

        private static string BuildAgentSystemPrompt()
        {
            return
                "You are a Unity Editor coding agent for a Gorilla-style VR project. " +
                "You can directly modify the user's project only through the allowed actions below. " +
                "Return ONE valid JSON object and no markdown, no code fences, and no prose outside JSON. " +
                "Schema: {\"message\":\"short explanation\",\"actions\":[{\"type\":\"...\",\"path\":\"Assets/...\",\"content\":\"full file contents\",\"componentType\":\"TypeName\",\"fieldName\":\"field\",\"value\":\"value\",\"objectName\":\"SceneObjectName\",\"boolValue\":true,\"x\":0,\"y\":0,\"z\":0}]}. " +
                "Allowed action types: create_or_replace_file, add_component, remove_component, set_active, set_local_position, set_local_rotation, set_local_scale, set_component_field. " +
                "For create_or_replace_file, path MUST begin Assets/ and content MUST be the complete replacement file. " +
                "For set_component_field, componentType is a short or full component type name. Use value for primitive/string/enum fields and objectName for GameObject/Transform/Component references. " +
                "All GameObject actions apply only to the currently selected GameObject. " +
                "When source code for a selected MonoBehaviour is provided and the user asks to fix it, prefer replacing that exact existing script path rather than inventing a duplicate script. " +
                "Do not create a second Rigidbody if one already exists. Do not modify XR/Gorilla locomotion unless needed for the user's request. " +
                "If you are not confident a change is correct, return an empty actions array and explain what information is missing in message. " +
                "Keep the plan minimal and directly useful.";
        }

        private static string ExtractJsonObject(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException("Empty model response.");

            int first = text.IndexOf('{');
            int last = text.LastIndexOf('}');
            if (first < 0 || last < first)
                throw new InvalidOperationException("No JSON object found in model response.");

            return text.Substring(first, last - first + 1);
        }

        private static string DescribeAction(AgentAction action)
        {
            if (action == null) return "null action";
            string type = action.type ?? "unknown";
            if (type == "create_or_replace_file") return type + " " + action.path;
            if (type == "add_component" || type == "remove_component") return type + " " + action.componentType;
            if (type == "set_component_field") return type + " " + action.componentType + "." + action.fieldName;
            return type;
        }

        private static string ExtractOllamaText(string json)
        {
            try
            {
                OllamaResponse result = JsonUtility.FromJson<OllamaResponse>(json);
                if (result == null)
                    return "Ollama returned an empty response.";
                if (!string.IsNullOrEmpty(result.error))
                    return "OLLAMA ERROR: " + result.error;
                if (!string.IsNullOrEmpty(result.response))
                    return result.response;
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
        }

        [Serializable]
        private class OllamaAgentRequest
        {
            public string model;
            public string system;
            public string prompt;
            public bool stream;
            public string format;
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
            public string path;
            public string content;
            public string componentType;
            public string fieldName;
            public string value;
            public string objectName;
            public bool boolValue;
            public float x;
            public float y;
            public float z;
        }
    }
}
