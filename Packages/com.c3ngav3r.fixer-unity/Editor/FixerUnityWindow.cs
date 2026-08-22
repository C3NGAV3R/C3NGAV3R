using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace C3NGAV3R.FixerUnity
{
    public class FixerUnityWindow : EditorWindow
    {
        private const string EndpointPref = "FixerUnity.Endpoint";
        private const string ModelPref = "FixerUnity.Model";
        private const string DevBuildPref = "FixerUnity.DevBuild";

        private string endpoint = "http://127.0.0.1:11434/api/generate";
        private string model = "qwen2.5-coder:3b";
        private bool developmentBuild;
        private bool working;
        private int tab;
        private int selectedError = -1;
        private string manualError = "";
        private string builderPrompt = "";
        private string result = "FIXER UNITY v1.0.1 ready.";
        private Vector2 errorListScroll;
        private Vector2 resultScroll;
        private Vector2 builderScroll;
        private readonly List<ConsoleEntry> errors = new List<ConsoleEntry>();

        [MenuItem("Tools/FIXER UNITY Console & Builder")]
        public static void Open()
        {
            GetWindow<FixerUnityWindow>("FIXER UNITY");
        }

        private void OnEnable()
        {
            endpoint = EditorPrefs.GetString(EndpointPref, endpoint);
            model = EditorPrefs.GetString(ModelPref, model);
            developmentBuild = EditorPrefs.GetBool(DevBuildPref, false);
            Application.logMessageReceived += OnLog;
        }

        private void OnDisable()
        {
            Application.logMessageReceived -= OnLog;
        }

        private void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)
                return;
            if (string.IsNullOrWhiteSpace(condition))
                return;

            ConsoleEntry entry = new ConsoleEntry
            {
                condition = condition,
                stackTrace = stackTrace ?? "",
                type = type.ToString(),
                time = DateTime.Now.ToString("HH:mm:ss")
            };

            ExtractSourceLocation(condition + "\n" + stackTrace, entry);
            errors.Insert(0, entry);
            if (errors.Count > 200)
                errors.RemoveAt(errors.Count - 1);
            Repaint();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(7);
            EditorGUILayout.LabelField("FIXER UNITY — CONSOLE & BUILDER v1.0.1", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Local zero-credit Unity helper. v1.0.1 auto-recovers missing UI types/canvases and blocks accidental deactivation of newly created objects.",
                MessageType.Info);

            DrawAiBar();

            EditorGUILayout.Space(6);
            tab = GUILayout.Toolbar(tab, new[] { "CONSOLE FIXER", "AI BUILDER", "GAME BUILDER" });
            EditorGUILayout.Space(7);

            if (tab == 0) DrawConsoleFixer();
            else if (tab == 1) DrawAiBuilder();
            else DrawGameBuilder();

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Result", EditorStyles.boldLabel);
            resultScroll = EditorGUILayout.BeginScrollView(resultScroll, GUILayout.MinHeight(170));
            EditorGUILayout.TextArea(result, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private void DrawAiBar()
        {
            EditorGUI.BeginChangeCheck();
            endpoint = EditorGUILayout.TextField("Local AI URL", endpoint);
            model = EditorGUILayout.TextField("Built-in AI Model", model);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetString(EndpointPref, endpoint);
                EditorPrefs.SetString(ModelPref, model);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.enabled = !working;
                if (GUILayout.Button("TEST AI", GUILayout.Height(25))) TestAi();
                if (GUILayout.Button("INSTALL FREE AI", GUILayout.Height(25))) LaunchCommand("ollama pull qwen2.5-coder:3b");
                if (GUILayout.Button("START OLLAMA", GUILayout.Height(25))) LaunchCommand("ollama serve");
                GUI.enabled = true;
            }

            if (working)
                EditorGUILayout.HelpBox("Local AI is working...", MessageType.None);
        }

        private void DrawConsoleFixer()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Clear Captured Errors"))
                {
                    errors.Clear();
                    selectedError = -1;
                }

                if (GUILayout.Button("Quick Fix Common Error"))
                    QuickFixSelected();
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Captured Console Errors", EditorStyles.boldLabel);

            if (errors.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No errors captured yet. Keep this window open and Unity errors will appear here. You can also paste an error below.",
                    MessageType.None);
            }
            else
            {
                errorListScroll = EditorGUILayout.BeginScrollView(errorListScroll, GUILayout.Height(165));
                for (int i = 0; i < errors.Count; i++)
                {
                    ConsoleEntry e = errors[i];
                    string label = "[" + e.time + "] " + FirstLine(e.condition);
                    if (!string.IsNullOrWhiteSpace(e.assetPath))
                        label += " — " + e.assetPath + ":" + e.line;
                    if (GUILayout.Toggle(selectedError == i, label, "Button"))
                        selectedError = i;
                }
                EditorGUILayout.EndScrollView();
            }

            if (selectedError >= 0 && selectedError < errors.Count)
            {
                ConsoleEntry e = errors[selectedError];
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Selected Error", EditorStyles.boldLabel);
                EditorGUILayout.TextArea(
                    e.condition + (string.IsNullOrWhiteSpace(e.stackTrace) ? "" : "\n" + e.stackTrace),
                    GUILayout.MinHeight(90));

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUI.enabled = !working;
                    if (GUILayout.Button("QUICK FIX SELECTED", GUILayout.Height(30))) QuickFixSelected();
                    if (GUILayout.Button("AI FIX SELECTED", GUILayout.Height(30)))
                        RunConsoleAiFix(e.condition + "\n" + e.stackTrace, e.assetPath);
                    GUI.enabled = true;
                }
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Paste Console Error Manually", EditorStyles.boldLabel);
            manualError = EditorGUILayout.TextArea(manualError, GUILayout.MinHeight(65));
            GUI.enabled = !working && !string.IsNullOrWhiteSpace(manualError);
            if (GUILayout.Button("AI FIX PASTED ERROR", GUILayout.Height(30)))
                RunConsoleAiFix(manualError, "");
            GUI.enabled = true;
        }

        private void DrawAiBuilder()
        {
            GameObject selected = Selection.activeGameObject;
            string selectedText = selected == null ? "NONE" : GetHierarchyPath(selected.transform);

            EditorGUILayout.HelpBox(
                "Current scene: " + SceneManager.GetActiveScene().path +
                "\nSelected object: " + selectedText +
                "\nThe executor can recover a missing uiType and can auto-create a Canvas if the local AI forgets one.",
                MessageType.None);

            EditorGUILayout.LabelField("What should FIXER build or fix?", EditorStyles.boldLabel);
            builderScroll = EditorGUILayout.BeginScrollView(builderScroll, GUILayout.Height(165));
            builderPrompt = EditorGUILayout.TextArea(builderPrompt, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();

            GUI.enabled = !working && !string.IsNullOrWhiteSpace(builderPrompt);
            if (GUILayout.Button("RUN AI BUILDER", GUILayout.Height(40)))
                RunBuilder();
            GUI.enabled = true;

            EditorGUILayout.Space(5);
            EditorGUILayout.HelpBox(
                "For loading screens, the fallback Canvas is World Space automatically when the active scene name contains 'Loading'.",
                MessageType.Info);
        }

        private void DrawGameBuilder()
        {
            EditorGUILayout.LabelField("Scenes in Build Settings", EditorStyles.boldLabel);
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            if (scenes.Length == 0)
                EditorGUILayout.HelpBox("No scenes are currently in Build Settings.", MessageType.Warning);
            else
                foreach (EditorBuildSettingsScene s in scenes)
                    EditorGUILayout.LabelField((s.enabled ? "✓ " : "○ ") + s.path);

            if (GUILayout.Button("SYNC ALL PROJECT SCENES TO BUILD SETTINGS"))
                SyncAllScenes();

            EditorGUI.BeginChangeCheck();
            developmentBuild = EditorGUILayout.ToggleLeft("Development Build", developmentBuild);
            if (EditorGUI.EndChangeCheck())
                EditorPrefs.SetBool(DevBuildPref, developmentBuild);

            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("BUILD ANDROID APK", GUILayout.Height(34))) BuildAndroid();
                if (GUILayout.Button("BUILD WINDOWS EXE", GUILayout.Height(34))) BuildWindows();
            }

            EditorGUILayout.HelpBox(
                "Android requires Unity Android Build Support. Windows builds use the enabled Build Settings scenes.",
                MessageType.None);
        }

        private void TestAi()
        {
            SendAi(
                "Return JSON only. Schema: {\"message\":\"text\",\"actions\":[]}.",
                "Return exactly {\"message\":\"FIXER AI READY\",\"actions\":[]}.",
                128,
                plan =>
                {
                    result = plan != null && (plan.message ?? "").Contains("FIXER AI READY")
                        ? "FIXER AI CONNECTED ✅\nModel: " + model
                        : "AI replied but the test did not match.";
                });
        }

        private void RunConsoleAiFix(string errorText, string knownPath)
        {
            string assetPath = NormalizeAssetPath(knownPath);
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                ConsoleEntry temp = new ConsoleEntry();
                ExtractSourceLocation(errorText, temp);
                assetPath = temp.assetPath;
            }

            string source = string.IsNullOrWhiteSpace(assetPath)
                ? ""
                : ReadAssetText(assetPath, 30000);

            string system =
                "You are FIXER UNITY CONSOLE AI. Return ONLY valid JSON, no markdown. " +
                "Schema: {\"message\":\"short summary\",\"actions\":[{\"type\":\"create_or_replace_file\",\"path\":\"Assets/...cs\",\"content\":\"COMPLETE C# FILE\"}]}. " +
                "Fix the concrete Unity compile/runtime error with the smallest safe change. Preserve existing behavior. " +
                "If source code is supplied, return the COMPLETE corrected file, never a partial snippet. " +
                "Do not invent packages, DLLs, binary assets or APIs that are not already present. Only write inside Assets/.";

            StringBuilder user = new StringBuilder();
            user.AppendLine("UNITY ERROR:");
            user.AppendLine(errorText);
            user.AppendLine();
            user.AppendLine("SOURCE PATH: " + (string.IsNullOrWhiteSpace(assetPath) ? "UNKNOWN" : assetPath));

            if (!string.IsNullOrWhiteSpace(source))
            {
                user.AppendLine();
                user.AppendLine("CURRENT SOURCE:");
                user.AppendLine(source);
            }

            SendAi(system, user.ToString(), 3500, plan =>
            {
                if (plan == null)
                {
                    result = "AI returned no usable plan.";
                    return;
                }

                result = (plan.message ?? "AI fix ready.") + "\n\n" + ApplyPlan(plan);
            });
        }

        private void RunBuilder()
        {
            StringBuilder context = new StringBuilder();
            context.AppendLine("USER REQUEST:");
            context.AppendLine(builderPrompt.Trim());
            context.AppendLine();
            context.AppendLine("CURRENT SCENE: " + SceneManager.GetActiveScene().path);

            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                context.AppendLine("SELECTED GAMEOBJECT: NONE");
                context.AppendLine("Creation requests must continue without a selected object.");
            }
            else
            {
                context.AppendLine("SELECTED GAMEOBJECT: " + GetHierarchyPath(selected.transform));
                context.AppendLine("COMPONENTS:");

                foreach (Component c in selected.GetComponents<Component>())
                    context.AppendLine(c == null ? "- MISSING SCRIPT" : "- " + c.GetType().FullName);

                MonoBehaviour[] scripts = selected.GetComponents<MonoBehaviour>();
                foreach (MonoBehaviour script in scripts)
                {
                    if (script == null) continue;
                    MonoScript mono = MonoScript.FromMonoBehaviour(script);
                    string path = AssetDatabase.GetAssetPath(mono);
                    string source = ReadAssetText(path, 12000);

                    if (!string.IsNullOrWhiteSpace(source))
                    {
                        context.AppendLine();
                        context.AppendLine("ATTACHED SCRIPT " + path + ":");
                        context.AppendLine(source);
                    }
                }
            }

            string system =
                "You are FIXER UNITY BUILDER, a local Unity Editor agent. Return ONLY valid JSON and no markdown. " +
                "Schema: {\"message\":\"summary\",\"actions\":[{" +
                "\"type\":\"create_scene|create_ui|create_gameobject|create_or_replace_file|add_component|set_component_field|set_active|set_transform\"," +
                "\"name\":\"optional\",\"scenePath\":\"optional Assets/Scenes/X.unity\",\"parentPath\":\"optional\",\"targetPath\":\"optional\"," +
                "\"uiType\":\"canvas|background|image|panel|title|text|button|slider|eventsystem\",\"text\":\"optional\",\"color\":\"#RRGGBBAA\"," +
                "\"path\":\"optional Assets/...\",\"content\":\"complete file content\",\"componentType\":\"optional\",\"field\":\"optional\",\"value\":\"optional\"," +
                "\"boolValue\":true,\"worldSpace\":false,\"x\":0,\"y\":0,\"z\":0,\"rotX\":0,\"rotY\":0,\"rotZ\":0,\"scale\":1,\"width\":400,\"height\":80,\"fontSize\":36}]}. " +
                "CRITICAL: EVERY create_ui action MUST include uiType. Create a canvas BEFORE its child UI. " +
                "For loading screens use one World Space canvas when requested. " +
                "Use uiType=background for backgrounds, panel for framed panels, title for main title, text for labels, slider for progress bars, image for scratches/decorations. " +
                "Do NOT set a newly created object inactive unless the user explicitly asks for it to start disabled. " +
                "For AudioSource settings use set_component_field with componentType=AudioSource and fields such as loop, playOnAwake, spatialBlend, volume. " +
                "For actionable create/fix requests actions must not be empty. Use create_ui for visible Unity UI. " +
                "For scripts write complete compiling C# source under Assets/Scripts/. Do not fabricate PNG/FBX/font/binary assets. " +
                "Do not write outside Assets/. If no object is selected and the user asks to create something, create required root objects yourself. " +
                "Keep the plan compact, ordered and executable. Maximum 35 actions.";

            SendAi(system, context.ToString(), 4200, plan =>
            {
                if (plan == null)
                {
                    result = "AI Builder returned no usable plan.";
                    return;
                }

                result = (plan.message ?? "Builder plan ready.") + "\n\n" + ApplyPlan(plan);
            });
        }

        private string ApplyPlan(AgentPlan plan)
        {
            if (plan.actions == null || plan.actions.Length == 0)
                return "No actions to apply.";

            StringBuilder sb = new StringBuilder("APPLYING:\n");
            GameObject lastCreated = null;
            bool wroteFiles = false;
            HashSet<string> createdThisRun = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (AgentAction action in plan.actions)
            {
                if (action == null || string.IsNullOrWhiteSpace(action.type))
                    continue;

                try
                {
                    string type = action.type.Trim().ToLowerInvariant();
                    string line;

                    if (type == "create_or_replace_file" || type == "create_file" || type == "replace_file")
                    {
                        line = WriteAssetFile(action);
                        wroteFiles = true;
                    }
                    else if (type == "create_scene")
                    {
                        line = CreateScene(action);
                    }
                    else
                    {
                        EnsureActionScene(action);

                        if (type == "create_gameobject")
                        {
                            lastCreated = CreateGameObject(action, out line);
                            createdThisRun.Add(lastCreated.name);
                        }
                        else if (type == "create_ui")
                        {
                            lastCreated = CreateUi(action, out line);
                            if (lastCreated != null)
                                createdThisRun.Add(lastCreated.name);
                        }
                        else if (type == "add_component")
                        {
                            line = AddComponent(ResolveTarget(action.targetPath, lastCreated), action.componentType);
                        }
                        else if (type == "set_component_field")
                        {
                            line = SetComponentField(ResolveTarget(action.targetPath, lastCreated), action);
                        }
                        else if (type == "set_active")
                        {
                            GameObject target = ResolveTarget(action.targetPath, lastCreated);

                            if (!action.boolValue &&
                                target != null &&
                                createdThisRun.Contains(target.name) &&
                                !UserExplicitlyRequestedDisabled())
                            {
                                line = "Ignored unintended deactivate for newly created " + target.name;
                            }
                            else
                            {
                                line = SetActive(target, action.boolValue);
                            }
                        }
                        else if (type == "set_transform")
                        {
                            line = SetTransform(ResolveTarget(action.targetPath, lastCreated), action);
                        }
                        else
                        {
                            line = "Skipped unsupported action: " + action.type;
                        }
                    }

                    sb.AppendLine("✅ " + line);
                }
                catch (Exception ex)
                {
                    sb.AppendLine("❌ " + Describe(action) + " -> " + ex.Message);
                }
            }

            EditorSceneManager.SaveOpenScenes();

            if (wroteFiles)
                AssetDatabase.Refresh();

            sb.AppendLine("Done.");
            return sb.ToString();
        }

        private bool UserExplicitlyRequestedDisabled()
        {
            string p = (builderPrompt ?? "").ToLowerInvariant();
            return p.Contains("disable") ||
                   p.Contains("inactive") ||
                   p.Contains("turn off") ||
                   p.Contains("start off") ||
                   p.Contains("start disabled");
        }

        private void QuickFixSelected()
        {
            if (selectedError < 0 || selectedError >= errors.Count)
            {
                result = "Select a captured console error first.";
                return;
            }

            ConsoleEntry e = errors[selectedError];
            if (string.IsNullOrWhiteSpace(e.assetPath))
            {
                result = "I could not find a C# file path in that error. Use AI FIX SELECTED.";
                return;
            }

            string path = NormalizeAssetPath(e.assetPath);
            string full = AssetToFullPath(path);

            if (!File.Exists(full))
            {
                result = "Source file not found: " + path;
                return;
            }

            string source = File.ReadAllText(full);
            string fixedSource = source;
            string reason;

            if (e.condition.IndexOf("Debug", StringComparison.OrdinalIgnoreCase) >= 0 &&
                e.condition.IndexOf("ambiguous", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                fixedSource = Regex.Replace(fixedSource, @"(?<![\w.])Debug\.", "UnityEngine.Debug.");
                reason = "Qualified Debug as UnityEngine.Debug.";
            }
            else if (e.condition.IndexOf("CommonUsages", StringComparison.OrdinalIgnoreCase) >= 0 &&
                     e.condition.IndexOf("ambiguous", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                fixedSource = Regex.Replace(fixedSource, @"(?<![\w.])CommonUsages\.", "UnityEngine.XR.CommonUsages.");
                reason = "Qualified CommonUsages as UnityEngine.XR.CommonUsages.";
            }
            else if (e.condition.IndexOf("Random", StringComparison.OrdinalIgnoreCase) >= 0 &&
                     e.condition.IndexOf("ambiguous", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                fixedSource = Regex.Replace(fixedSource, @"(?<![\w.])Random\.", "UnityEngine.Random.");
                reason = "Qualified Random as UnityEngine.Random.";
            }
            else
            {
                result = "No deterministic quick fix exists for this error. Use AI FIX SELECTED.";
                return;
            }

            if (fixedSource == source)
            {
                result = "Quick-fix pattern matched but no source change was needed. Use AI FIX SELECTED.";
                return;
            }

            BackupFile(full);
            File.WriteAllText(full, fixedSource, new UTF8Encoding(false));
            AssetDatabase.Refresh();
            result = "QUICK FIX APPLIED ✅\n" + path + "\n" + reason;
        }

        private static void ExtractSourceLocation(string text, ConsoleEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(text))
                return;

            Match m = Regex.Match(
                text,
                @"(?<path>Assets[\\/][^\r\n:(]+\.cs)\((?<line>\d+),(?<col>\d+)\)",
                RegexOptions.IgnoreCase);

            if (!m.Success)
                m = Regex.Match(
                    text,
                    @"(?<path>Assets[\\/][^\r\n:]+\.cs):(?<line>\d+)",
                    RegexOptions.IgnoreCase);

            if (!m.Success)
                return;

            entry.assetPath = NormalizeAssetPath(m.Groups["path"].Value);

            int line;
            if (int.TryParse(m.Groups["line"].Value, out line))
                entry.line = line;
        }

        private void SendAi(string system, string promptText, int numPredict, Action<AgentPlan> onPlan)
        {
            if (working)
                return;

            if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(model))
            {
                result = "AI URL/model is empty.";
                return;
            }

            GenerateRequest payload = new GenerateRequest
            {
                model = model.Trim(),
                system = system,
                prompt = promptText,
                stream = false,
                format = "json",
                keep_alive = "5m",
                options = new OllamaOptions
                {
                    num_ctx = 4096,
                    num_predict = numPredict,
                    temperature = 0.04f,
                    seed = 42,
                    repeat_penalty = 1.05f
                }
            };

            string json = JsonUtility.ToJson(payload);
            working = true;
            Repaint();

            UnityWebRequest req = new UnityWebRequest(endpoint.Trim(), "POST");
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = 600;

            UnityWebRequestAsyncOperation operation = req.SendWebRequest();
            operation.completed += _ =>
            {
                working = false;

                try
                {
                    if (req.result != UnityWebRequest.Result.Success)
                    {
                        result =
                            "LOCAL AI FAILED\nHTTP " + req.responseCode +
                            "\n" + req.error +
                            "\n\n" + req.downloadHandler.text;
                    }
                    else
                    {
                        OllamaResponse response = JsonUtility.FromJson<OllamaResponse>(req.downloadHandler.text);

                        if (response == null)
                            result = "Local AI returned an empty response.";
                        else if (!string.IsNullOrWhiteSpace(response.error))
                            result = "LOCAL AI ERROR: " + response.error;
                        else
                            onPlan(ParsePlan(response.response));
                    }
                }
                catch (Exception ex)
                {
                    result =
                        "AI response could not be applied:\n" +
                        ex.Message +
                        "\n\n" +
                        req.downloadHandler.text;
                }
                finally
                {
                    req.Dispose();
                    Repaint();
                }
            };
        }

        private static AgentPlan ParsePlan(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException("Model returned no text.");

            int start = text.IndexOf('{');
            int end = text.LastIndexOf('}');

            if (start < 0 || end <= start)
                throw new InvalidOperationException("Model did not return a JSON object.");

            AgentPlan plan = JsonUtility.FromJson<AgentPlan>(text.Substring(start, end - start + 1));

            if (plan == null)
                throw new InvalidOperationException("JSON did not match FIXER UNITY action schema.");

            return plan;
        }

        private static string WriteAssetFile(AgentAction action)
        {
            string path = NormalizeAssetPath(action.path);

            if (string.IsNullOrWhiteSpace(path))
            {
                string name = string.IsNullOrWhiteSpace(action.name)
                    ? "FixerGenerated.cs"
                    : Path.GetFileName(action.name);

                if (!name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    name += ".cs";

                path = "Assets/Scripts/" + name;
            }

            if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                path = "Assets/Scripts/" + Path.GetFileName(path);

            if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Blocked non-text/binary asset write: " + path);

            string full = AssetToFullPath(path);
            string assetsRoot = Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar;

            if (!Path.GetFullPath(full).StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Writing outside Assets is blocked.");

            Directory.CreateDirectory(Path.GetDirectoryName(full));

            if (File.Exists(full))
                BackupFile(full);

            File.WriteAllText(full, action.content ?? "", new UTF8Encoding(false));
            return "Wrote " + path;
        }

        private static string CreateScene(AgentAction action)
        {
            string path = NormalizeAssetPath(
                !string.IsNullOrWhiteSpace(action.scenePath)
                    ? action.scenePath
                    : action.path);

            if (string.IsNullOrWhiteSpace(path))
            {
                string name = string.IsNullOrWhiteSpace(action.name)
                    ? "FixerScene"
                    : CleanName(action.name);

                if (!name.EndsWith("Scene", StringComparison.OrdinalIgnoreCase))
                    name += "Scene";

                path = "Assets/Scenes/" + name + ".unity";
            }

            if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                path = "Assets/Scenes/" + Path.GetFileName(path);

            if (!path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                path += ".unity";

            string full = AssetToFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            EditorSceneManager.SaveOpenScenes();

            if (File.Exists(full))
            {
                EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                return "Opened existing scene " + path;
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, path);
            return "Created scene " + path;
        }

        private static void EnsureActionScene(AgentAction action)
        {
            if (string.IsNullOrWhiteSpace(action.scenePath))
                return;

            string path = NormalizeAssetPath(action.scenePath);

            if (!path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                path += ".unity";

            Scene active = SceneManager.GetActiveScene();

            if (string.Equals(active.path, path, StringComparison.OrdinalIgnoreCase))
                return;

            if (!File.Exists(AssetToFullPath(path)))
                throw new FileNotFoundException("Target scene does not exist yet: " + path);

            EditorSceneManager.SaveOpenScenes();
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        }

        private static GameObject CreateGameObject(AgentAction action, out string message)
        {
            string name = CleanName(
                string.IsNullOrWhiteSpace(action.name)
                    ? "FixerObject"
                    : action.name);

            GameObject existing = FindActiveSceneObjectByName(name);
            if (existing != null)
                Undo.DestroyObjectImmediate(existing);

            GameObject go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "FIXER UNITY create object");

            GameObject parent = Resolve(action.parentPath);
            if (parent != null)
                go.transform.SetParent(parent.transform, false);

            go.transform.localPosition = new Vector3(action.x, action.y, action.z);
            go.transform.localEulerAngles = new Vector3(action.rotX, action.rotY, action.rotZ);

            if (action.scale > 0)
                go.transform.localScale = Vector3.one * action.scale;

            Selection.activeGameObject = go;
            message = "Created GameObject " + go.name;
            return go;
        }

        private static GameObject CreateUi(AgentAction action, out string message)
        {
            string kind = InferUiType(action);

            if (kind == "eventsystem")
            {
                GameObject existingEventSystem =
                    FindSceneObjectWithComponent("UnityEngine.EventSystems.EventSystem");

                if (existingEventSystem != null)
                {
                    message = "Reused existing EventSystem";
                    return existingEventSystem;
                }

                string eventName = CleanName(
                    string.IsNullOrWhiteSpace(action.name)
                        ? "EventSystem"
                        : action.name);

                GameObject es = new GameObject(eventName);
                Undo.RegisterCreatedObjectUndo(es, "FIXER UNITY EventSystem");
                TryAddComponent(es, "UnityEngine.EventSystems.EventSystem");

                if (!TryAddComponent(es, "UnityEngine.InputSystem.UI.InputSystemUIInputModule"))
                    TryAddComponent(es, "UnityEngine.EventSystems.StandaloneInputModule");

                Selection.activeGameObject = es;
                message = "Created EventSystem";
                return es;
            }

            string name = CleanName(
                string.IsNullOrWhiteSpace(action.name)
                    ? "Fixer_" + kind
                    : action.name);

            if (kind == "canvas")
            {
                GameObject existingCanvas = FindActiveSceneObjectByName(name);

                if (existingCanvas != null && existingCanvas.GetComponent<Canvas>() != null)
                {
                    ConfigureCanvas(existingCanvas, action);
                    Selection.activeGameObject = existingCanvas;
                    message = "Reused/configured Canvas " + name + " (inferred uiType=" + kind + ")";
                    return existingCanvas;
                }
            }

            GameObject parent = Resolve(action.parentPath);

            if (kind != "canvas" && parent == null)
            {
                parent = FindSceneObjectWithComponent("UnityEngine.Canvas");

                if (parent == null)
                    parent = CreateFallbackCanvas();
            }

            GameObject existing = FindActiveSceneObjectByName(name);

            if (existing != null)
            {
                bool isParent = parent != null && existing == parent;
                if (!isParent)
                    Undo.DestroyObjectImmediate(existing);
            }

            GameObject go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "FIXER UNITY create UI");

            if (parent != null)
                go.transform.SetParent(parent.transform, false);

            RectTransform rt = (RectTransform)go.transform;
            rt.localScale = Vector3.one;

            if (kind == "canvas")
            {
                go.AddComponent<Canvas>();
                ConfigureCanvas(go, action);
                Selection.activeGameObject = go;
                message =
                    (action.worldSpace ? "Created World Space Canvas " : "Created Screen Space Canvas ") +
                    name +
                    (string.IsNullOrWhiteSpace(action.uiType) ? " (uiType inferred)" : "");
                return go;
            }

            if (kind == "background")
            {
                if (action.width > 0 || action.height > 0)
                    SetRect(rt, action, 1100, 620);
                else
                    Stretch(rt);

                AddImage(go, string.IsNullOrWhiteSpace(action.color) ? "#090A0FFF" : action.color);
            }
            else if (kind == "image")
            {
                SetRect(rt, action, 180, 40);
                AddImage(go, string.IsNullOrWhiteSpace(action.color) ? "#FFFFFFFF" : action.color);
            }
            else if (kind == "panel")
            {
                SetRect(rt, action, 760, 520);
                AddImage(go, string.IsNullOrWhiteSpace(action.color) ? "#12141BEF" : action.color);
            }
            else if (kind == "title" || kind == "text")
            {
                SetRect(
                    rt,
                    action,
                    kind == "title" ? 900 : 650,
                    kind == "title" ? 130 : 70);

                AddText(
                    go,
                    string.IsNullOrWhiteSpace(action.text) ? name : action.text,
                    action.fontSize > 0 ? action.fontSize : (kind == "title" ? 80 : 34),
                    action.color);
            }
            else if (kind == "button")
            {
                SetRect(rt, action, 440, 90);
                Component image = AddImage(
                    go,
                    string.IsNullOrWhiteSpace(action.color) ? "#262A33FF" : action.color);

                Component button = AddByName(go, "UnityEngine.UI.Button");

                if (button != null && image != null)
                    SetMember(button, "targetGraphic", image);

                CreateButtonText(
                    go,
                    string.IsNullOrWhiteSpace(action.text)
                        ? name.Replace("Button", "")
                        : action.text,
                    action.fontSize > 0 ? action.fontSize : 34);
            }
            else if (kind == "slider")
            {
                SetRect(rt, action, 650, 35);
                BuildSlider(go, action.color);
            }
            else
            {
                SetRect(rt, action, 180, 40);
                AddImage(go, string.IsNullOrWhiteSpace(action.color) ? "#FFFFFFFF" : action.color);
                kind = "image";
            }

            Selection.activeGameObject = go;
            message =
                "Created real UI " + kind + " " + go.name +
                (string.IsNullOrWhiteSpace(action.uiType) ? " (uiType auto-inferred)" : "");

            return go;
        }

        private static string InferUiType(AgentAction action)
        {
            string kind = (action.uiType ?? "").Trim().ToLowerInvariant();

            if (kind == "label") kind = "text";
            if (kind == "img") kind = "image";
            if (kind == "menu") kind = "panel";

            if (!string.IsNullOrWhiteSpace(kind))
                return kind;

            string probe =
                ((action.name ?? "") + " " + (action.text ?? ""))
                .Trim()
                .ToLowerInvariant();

            if (probe.Contains("canvas")) return "canvas";
            if (probe.Contains("eventsystem") || probe.Contains("event system")) return "eventsystem";
            if (probe.Contains("progress") || probe.Contains("slider") || probe.Contains("loadingbar") || probe.Contains("loading bar")) return "slider";
            if (probe.Contains("button")) return "button";
            if (probe.Contains("title") || probe.Contains("heading")) return "title";
            if (probe.Contains("loadingtext") || probe.Contains("loading text") || probe.Contains("label") || probe.Contains("status") || probe.EndsWith("text")) return "text";
            if (probe.Contains("background") || probe.Contains("backdrop")) return "background";
            if (probe.Contains("panel") || probe.Contains("frame")) return "panel";
            if (probe.Contains("scratch") || probe.Contains("decoration") || probe.Contains("deco") || probe.Contains("accent") || probe.Contains("icon") || probe.Contains("image")) return "image";

            return "image";
        }

        private static GameObject CreateFallbackCanvas()
        {
            GameObject existing = FindSceneObjectWithComponent("UnityEngine.Canvas");

            if (existing != null)
                return existing;

            bool loadingScene =
                SceneManager.GetActiveScene().name.IndexOf("loading", StringComparison.OrdinalIgnoreCase) >= 0;

            AgentAction canvasAction = new AgentAction
            {
                type = "create_ui",
                name = loadingScene ? "LoadingScreenCanvas" : "FixerCanvas",
                uiType = "canvas",
                worldSpace = loadingScene,
                width = loadingScene ? 1200f : 1920f,
                height = loadingScene ? 700f : 1080f,
                scale = loadingScene ? 0.003f : 1f,
                x = 0f,
                y = loadingScene ? 1.7f : 0f,
                z = loadingScene ? 4f : 0f,
                rotX = 0f,
                rotY = 0f,
                rotZ = 0f
            };

            string ignored;
            return CreateUi(canvasAction, out ignored);
        }

        private static void ConfigureCanvas(GameObject go, AgentAction action)
        {
            RectTransform rt = go.transform as RectTransform;

            if (rt == null)
                throw new InvalidOperationException("Canvas object has no RectTransform.");

            Canvas canvas = go.GetComponent<Canvas>();

            if (canvas == null)
                canvas = go.AddComponent<Canvas>();

            bool world =
                action.worldSpace ||
                SceneManager.GetActiveScene().name.IndexOf("loading", StringComparison.OrdinalIgnoreCase) >= 0;

            canvas.renderMode = world ? RenderMode.WorldSpace : RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            Component scaler = AddByName(go, "UnityEngine.UI.CanvasScaler");
            AddByName(go, "UnityEngine.UI.GraphicRaycaster");

            if (world)
            {
                rt.sizeDelta = new Vector2(
                    action.width > 0 ? action.width : 1200f,
                    action.height > 0 ? action.height : 700f);

                float s = action.scale > 0 ? action.scale : 0.003f;
                rt.localScale = new Vector3(s, s, s);
                rt.localPosition = new Vector3(
                    action.x,
                    Mathf.Abs(action.y) > 0.0001f ? action.y : 1.7f,
                    Mathf.Abs(action.z) > 0.0001f ? action.z : 4f);

                rt.localEulerAngles = new Vector3(action.rotX, action.rotY, action.rotZ);

                if (scaler != null)
                    SetMember(scaler, "dynamicPixelsPerUnit", 10f);
            }
            else if (scaler != null)
            {
                SetEnumMember(scaler, "uiScaleMode", "ScaleWithScreenSize");
                SetMember(scaler, "referenceResolution", new Vector2(1920, 1080));
                SetMember(scaler, "matchWidthOrHeight", 0.5f);
            }
        }

        private static string AddComponent(GameObject target, string componentType)
        {
            RequireTarget(target);

            if (!TryAddComponent(target, componentType))
                throw new InvalidOperationException("Could not find/add component type: " + componentType);

            return "Added " + componentType + " to " + target.name;
        }

        private static string SetComponentField(GameObject target, AgentAction action)
        {
            RequireTarget(target);

            Type type = FindType(action.componentType);
            if (type == null)
                throw new InvalidOperationException("Component type not found: " + action.componentType);

            Component component = target.GetComponent(type);
            if (component == null)
                throw new InvalidOperationException(target.name + " has no " + action.componentType);

            BindingFlags flags =
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic;

            FieldInfo field = type.GetField(action.field ?? "", flags);
            PropertyInfo property =
                field == null
                    ? type.GetProperty(action.field ?? "", flags)
                    : null;

            Type valueType =
                field != null
                    ? field.FieldType
                    : property != null && property.CanWrite
                        ? property.PropertyType
                        : null;

            if (valueType == null)
                throw new InvalidOperationException("Writable field/property not found: " + action.field);

            object value = ConvertValue(action.value, valueType);
            Undo.RecordObject(component, "FIXER UNITY set component field");

            if (field != null)
                field.SetValue(component, value);
            else
                property.SetValue(component, value, null);

            EditorUtility.SetDirty(component);
            return "Set " + action.componentType + "." + action.field + " on " + target.name;
        }

        private static object ConvertValue(string raw, Type type)
        {
            string value = raw ?? "";

            if (type == typeof(string)) return value;
            if (type == typeof(bool)) return bool.Parse(value);
            if (type == typeof(int)) return int.Parse(value, CultureInfo.InvariantCulture);
            if (type == typeof(float)) return float.Parse(value, CultureInfo.InvariantCulture);
            if (type == typeof(double)) return double.Parse(value, CultureInfo.InvariantCulture);
            if (type == typeof(Color)) return ParseColor(value, Color.white);

            if (type == typeof(Vector2))
            {
                string[] p = value.Trim('(', ')', '[', ']').Split(',');
                if (p.Length != 2) throw new FormatException("Vector2 must be x,y");
                return new Vector2(
                    float.Parse(p[0], CultureInfo.InvariantCulture),
                    float.Parse(p[1], CultureInfo.InvariantCulture));
            }

            if (type == typeof(Vector3))
            {
                string[] p = value.Trim('(', ')', '[', ']').Split(',');
                if (p.Length != 3) throw new FormatException("Vector3 must be x,y,z");
                return new Vector3(
                    float.Parse(p[0], CultureInfo.InvariantCulture),
                    float.Parse(p[1], CultureInfo.InvariantCulture),
                    float.Parse(p[2], CultureInfo.InvariantCulture));
            }

            if (type.IsEnum)
                return Enum.Parse(type, value.Replace(" ", ""), true);

            if (typeof(UnityEngine.Object).IsAssignableFrom(type))
            {
                GameObject go = Resolve(value);

                if (go == null)
                    return null;

                if (type == typeof(GameObject))
                    return go;

                if (type == typeof(Transform))
                    return go.transform;

                return go.GetComponent(type);
            }

            throw new InvalidOperationException("Unsupported field value type: " + type.Name);
        }

        private static string SetActive(GameObject target, bool active)
        {
            RequireTarget(target);
            Undo.RecordObject(target, "FIXER UNITY set active");
            target.SetActive(active);
            return "Set " + target.name + " active=" + active;
        }

        private static string SetTransform(GameObject target, AgentAction action)
        {
            RequireTarget(target);
            Undo.RecordObject(target.transform, "FIXER UNITY transform");

            target.transform.localPosition = new Vector3(action.x, action.y, action.z);
            target.transform.localEulerAngles = new Vector3(action.rotX, action.rotY, action.rotZ);

            if (action.scale > 0)
                target.transform.localScale = Vector3.one * action.scale;

            RectTransform rt = target.transform as RectTransform;

            if (rt != null && (action.width > 0 || action.height > 0))
            {
                rt.sizeDelta = new Vector2(
                    action.width > 0 ? action.width : rt.sizeDelta.x,
                    action.height > 0 ? action.height : rt.sizeDelta.y);
            }

            return "Updated transform on " + target.name;
        }

        private void SyncAllScenes()
        {
            string[] guids = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" });
            List<string> paths = new List<string>();

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                if (!string.IsNullOrWhiteSpace(path) &&
                    path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                    paths.Add(path);
            }

            paths.Sort(StringComparer.OrdinalIgnoreCase);
            EditorBuildSettingsScene[] scenes = new EditorBuildSettingsScene[paths.Count];

            for (int i = 0; i < paths.Count; i++)
                scenes[i] = new EditorBuildSettingsScene(paths[i], true);

            EditorBuildSettings.scenes = scenes;
            result = "Build Settings synced ✅\nScenes: " + scenes.Length;
        }

        private void BuildAndroid()
        {
            string[] scenes = GetEnabledBuildScenes();

            if (scenes.Length == 0)
            {
                result = "No enabled scenes in Build Settings.";
                return;
            }

            string path = EditorUtility.SaveFilePanel(
                "Build Android APK",
                "",
                PlayerSettings.productName + ".apk",
                "apk");

            if (string.IsNullOrWhiteSpace(path))
                return;

            try
            {
                EditorSceneManager.SaveOpenScenes();
                EditorUserBuildSettings.SwitchActiveBuildTarget(
                    BuildTargetGroup.Android,
                    BuildTarget.Android);

                BuildPlayerOptions options = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = path,
                    target = BuildTarget.Android,
                    options = developmentBuild
                        ? BuildOptions.Development
                        : BuildOptions.None
                };

                result = BuildReportText(BuildPipeline.BuildPlayer(options), path);
            }
            catch (Exception ex)
            {
                result = "ANDROID BUILD FAILED\n" + ex;
            }
        }

        private void BuildWindows()
        {
            string[] scenes = GetEnabledBuildScenes();

            if (scenes.Length == 0)
            {
                result = "No enabled scenes in Build Settings.";
                return;
            }

            string folder = EditorUtility.SaveFolderPanel(
                "Build Windows Game",
                "",
                "");

            if (string.IsNullOrWhiteSpace(folder))
                return;

            string exe =
                Path.Combine(
                    folder,
                    CleanFileName(PlayerSettings.productName) + ".exe");

            try
            {
                EditorSceneManager.SaveOpenScenes();
                EditorUserBuildSettings.SwitchActiveBuildTarget(
                    BuildTargetGroup.Standalone,
                    BuildTarget.StandaloneWindows64);

                BuildPlayerOptions options = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = exe,
                    target = BuildTarget.StandaloneWindows64,
                    options = developmentBuild
                        ? BuildOptions.Development
                        : BuildOptions.None
                };

                result = BuildReportText(BuildPipeline.BuildPlayer(options), exe);
            }
            catch (Exception ex)
            {
                result = "WINDOWS BUILD FAILED\n" + ex;
            }
        }

        private static string BuildReportText(BuildReport report, string path)
        {
            if (report == null)
                return "Build returned no report.";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("BUILD RESULT: " + report.summary.result);
            sb.AppendLine("Output: " + path);
            sb.AppendLine("Size: " + report.summary.totalSize + " bytes");
            sb.AppendLine("Time: " + report.summary.totalTime);
            sb.AppendLine("Warnings: " + report.summary.totalWarnings);
            sb.AppendLine("Errors: " + report.summary.totalErrors);
            return sb.ToString();
        }

        private static string[] GetEnabledBuildScenes()
        {
            List<string> list = new List<string>();

            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled && !string.IsNullOrWhiteSpace(scene.path))
                    list.Add(scene.path);
            }

            return list.ToArray();
        }

        private void LaunchCommand(string command)
        {
            try
            {
#if UNITY_EDITOR_WIN
                System.Diagnostics.ProcessStartInfo info =
                    new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = "/k " + command,
                        UseShellExecute = true,
                        CreateNoWindow = false
                    };

                System.Diagnostics.Process.Start(info);
                result = "Opened command:\n" + command;
#else
                result =
                    "Automatic Ollama command launcher is implemented for Windows. Run manually:\n" +
                    command;
#endif
            }
            catch (Exception ex)
            {
                result =
                    "Could not launch Ollama command.\n" +
                    ex.Message +
                    "\n\nInstall Ollama first, then run:\n" +
                    command;
            }
        }

        private static GameObject ResolveTarget(string path, GameObject fallback)
        {
            return Resolve(path) ?? fallback ?? Selection.activeGameObject;
        }

        private static GameObject Resolve(string path)
        {
            if (string.IsNullOrWhiteSpace(path) ||
                string.Equals(path, "ROOT", StringComparison.OrdinalIgnoreCase))
                return null;

            if (string.Equals(path, "SELECTED", StringComparison.OrdinalIgnoreCase))
                return Selection.activeGameObject;

            string wanted = path.Trim().Trim('/');

            foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go == null || !go.scene.IsValid())
                    continue;

                if (go.scene != SceneManager.GetActiveScene())
                    continue;

                if (string.Equals(go.name, wanted, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        GetHierarchyPath(go.transform).Trim('/'),
                        wanted,
                        StringComparison.OrdinalIgnoreCase))
                    return go;
            }

            return null;
        }

        private static GameObject FindActiveSceneObjectByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            Scene active = SceneManager.GetActiveScene();

            foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go == null || !go.scene.IsValid() || go.scene != active)
                    continue;

                if (string.Equals(go.name, name, StringComparison.OrdinalIgnoreCase))
                    return go;
            }

            return null;
        }

        private static GameObject FindSceneObjectWithComponent(string typeName)
        {
            Type type = FindType(typeName);

            if (type == null)
                return null;

            Scene active = SceneManager.GetActiveScene();

            foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go == null || !go.scene.IsValid() || go.scene != active)
                    continue;

                if (go.GetComponent(type) != null)
                    return go;
            }

            return null;
        }

        private static Type FindType(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            string expanded = ExpandType(name.Trim());

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type direct =
                    assembly.GetType(name, false, true) ??
                    assembly.GetType(expanded, false, true);

                if (direct != null)
                    return direct;

                try
                {
                    foreach (Type type in assembly.GetTypes())
                    {
                        if (string.Equals(type.Name, name, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(type.FullName, expanded, StringComparison.OrdinalIgnoreCase))
                            return type;
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
            switch ((name ?? "").Trim().ToLowerInvariant())
            {
                case "rigidbody": return "UnityEngine.Rigidbody";
                case "boxcollider": return "UnityEngine.BoxCollider";
                case "spherecollider": return "UnityEngine.SphereCollider";
                case "capsulecollider": return "UnityEngine.CapsuleCollider";
                case "audio source":
                case "audiosource": return "UnityEngine.AudioSource";
                case "canvas": return "UnityEngine.Canvas";
                case "canvasscaler": return "UnityEngine.UI.CanvasScaler";
                case "graphicraycaster": return "UnityEngine.UI.GraphicRaycaster";
                case "image": return "UnityEngine.UI.Image";
                case "text": return "UnityEngine.UI.Text";
                case "button": return "UnityEngine.UI.Button";
                case "slider": return "UnityEngine.UI.Slider";
                case "eventsystem": return "UnityEngine.EventSystems.EventSystem";
                case "standaloneinputmodule": return "UnityEngine.EventSystems.StandaloneInputModule";
                case "inputsystemuiinputmodule": return "UnityEngine.InputSystem.UI.InputSystemUIInputModule";
                default: return name;
            }
        }

        private static bool TryAddComponent(GameObject go, string typeName)
        {
            if (go == null)
                return false;

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

        private static Component AddImage(GameObject go, string color)
        {
            Component image = AddByName(go, "UnityEngine.UI.Image");

            if (image != null)
                SetMember(image, "color", ParseColor(color, Color.white));

            return image;
        }

        private static Component AddText(
            GameObject go,
            string text,
            int fontSize,
            string color)
        {
            Component label = AddByName(go, "UnityEngine.UI.Text");

            if (label == null)
                return null;

            SetMember(label, "text", text);
            SetMember(label, "fontSize", fontSize);
            SetEnumMember(label, "alignment", "MiddleCenter");
            SetMember(label, "color", ParseColor(color, Color.white));

            try
            {
                Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

                if (font != null)
                    SetMember(label, "font", font);
            }
            catch
            {
            }

            return label;
        }

        private static void CreateButtonText(
            GameObject button,
            string text,
            int fontSize)
        {
            GameObject child = new GameObject("Text", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(child, "FIXER UNITY button text");
            child.transform.SetParent(button.transform, false);
            Stretch((RectTransform)child.transform);
            AddText(child, text, fontSize, "#FFFFFFFF");
        }

        private static void BuildSlider(GameObject root, string accent)
        {
            Component slider = AddByName(root, "UnityEngine.UI.Slider");

            GameObject background = CreateUiChild(root, "Background");
            RectTransform backgroundRect = (RectTransform)background.transform;
            backgroundRect.anchorMin = new Vector2(0, 0.25f);
            backgroundRect.anchorMax = new Vector2(1, 0.75f);
            backgroundRect.offsetMin = Vector2.zero;
            backgroundRect.offsetMax = Vector2.zero;
            AddImage(background, "#30333DFF");

            GameObject fillArea = CreateUiChild(root, "Fill Area");
            RectTransform fillAreaRect = (RectTransform)fillArea.transform;
            fillAreaRect.anchorMin = new Vector2(0, 0.25f);
            fillAreaRect.anchorMax = new Vector2(1, 0.75f);
            fillAreaRect.offsetMin = new Vector2(8, 0);
            fillAreaRect.offsetMax = new Vector2(-8, 0);

            GameObject fill = CreateUiChild(fillArea, "Fill");
            RectTransform fillRect = (RectTransform)fill.transform;
            Stretch(fillRect);
            AddImage(
                fill,
                string.IsNullOrWhiteSpace(accent)
                    ? "#53D8FFFF"
                    : accent);

            GameObject handleArea = CreateUiChild(root, "Handle Slide Area");
            RectTransform handleAreaRect = (RectTransform)handleArea.transform;
            Stretch(handleAreaRect);
            handleAreaRect.offsetMin = new Vector2(10, 0);
            handleAreaRect.offsetMax = new Vector2(-10, 0);

            GameObject handle = CreateUiChild(handleArea, "Handle");
            RectTransform handleRect = (RectTransform)handle.transform;
            handleRect.sizeDelta = new Vector2(28, 40);
            Component handleImage = AddImage(handle, "#FFFFFFFF");

            if (slider != null)
            {
                SetMember(slider, "fillRect", fillRect);
                SetMember(slider, "handleRect", handleRect);

                if (handleImage != null)
                    SetMember(slider, "targetGraphic", handleImage);

                SetMember(slider, "minValue", 0f);
                SetMember(slider, "maxValue", 1f);
                SetMember(slider, "value", 0f);
            }
        }

        private static GameObject CreateUiChild(GameObject parent, string name)
        {
            GameObject child = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(child, "FIXER UNITY UI child");
            child.transform.SetParent(parent.transform, false);
            return child;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;
        }

        private static void SetRect(
            RectTransform rt,
            AgentAction action,
            float defaultWidth,
            float defaultHeight)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(action.x, action.y);
            rt.sizeDelta = new Vector2(
                action.width > 0 ? action.width : defaultWidth,
                action.height > 0 ? action.height : defaultHeight);

            if (Mathf.Abs(action.rotZ) > 0.001f)
                rt.localEulerAngles = new Vector3(0, 0, action.rotZ);
        }

        private static void SetMember(object obj, string name, object value)
        {
            if (obj == null)
                return;

            Type type = obj.GetType();
            BindingFlags flags =
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic;

            FieldInfo field = type.GetField(name, flags);

            if (field != null)
            {
                try
                {
                    field.SetValue(obj, value);
                }
                catch
                {
                }

                return;
            }

            PropertyInfo property = type.GetProperty(name, flags);

            if (property != null && property.CanWrite)
            {
                try
                {
                    property.SetValue(obj, value, null);
                }
                catch
                {
                }
            }
        }

        private static void SetEnumMember(
            object obj,
            string name,
            string enumName)
        {
            if (obj == null)
                return;

            Type type = obj.GetType();
            BindingFlags flags =
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic;

            FieldInfo field = type.GetField(name, flags);
            PropertyInfo property =
                field == null
                    ? type.GetProperty(name, flags)
                    : null;

            Type enumType =
                field != null
                    ? field.FieldType
                    : property != null
                        ? property.PropertyType
                        : null;

            if (enumType == null || !enumType.IsEnum)
                return;

            object value =
                Enum.Parse(
                    enumType,
                    enumName.Replace(" ", ""),
                    true);

            if (field != null)
                field.SetValue(obj, value);
            else if (property != null && property.CanWrite)
                property.SetValue(obj, value, null);
        }

        private static Color ParseColor(string value, Color fallback)
        {
            Color color;

            return !string.IsNullOrWhiteSpace(value) &&
                   ColorUtility.TryParseHtmlString(value.Trim(), out color)
                ? color
                : fallback;
        }

        private static string ReadAssetText(string path, int maxChars)
        {
            path = NormalizeAssetPath(path);

            if (string.IsNullOrWhiteSpace(path))
                return "";

            string full = AssetToFullPath(path);

            if (!File.Exists(full))
                return "";

            string text = File.ReadAllText(full);

            if (text.Length > maxChars)
                text =
                    text.Substring(0, maxChars) +
                    "\n// [FIXER UNITY truncated remaining source for local AI context]";

            return text;
        }

        private static void BackupFile(string fullPath)
        {
            if (!File.Exists(fullPath))
                return;

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string backupRoot = Path.Combine(
                projectRoot,
                "Library",
                "FixerUnityBackups");

            Directory.CreateDirectory(backupRoot);

            string backupName =
                DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") +
                "_" +
                Path.GetFileName(fullPath);

            File.Copy(
                fullPath,
                Path.Combine(backupRoot, backupName),
                true);
        }

        private static string AssetToFullPath(string assetPath)
        {
            string projectRoot =
                Directory.GetParent(Application.dataPath).FullName;

            return Path.GetFullPath(
                Path.Combine(
                    projectRoot,
                    NormalizeAssetPath(assetPath)
                        .Replace('/', Path.DirectorySeparatorChar)));
        }

        private static string NormalizeAssetPath(string path)
        {
            return string.IsNullOrWhiteSpace(path)
                ? ""
                : path.Trim().Replace('\\', '/');
        }

        private static string CleanName(string name)
        {
            return string.IsNullOrWhiteSpace(name)
                ? "FixerObject"
                : name.Replace('/', '_')
                      .Replace('\\', '_')
                      .Trim();
        }

        private static string CleanFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "Game";

            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');

            return name;
        }

        private static string GetHierarchyPath(Transform transform)
        {
            List<string> parts = new List<string>();
            Transform current = transform;

            while (current != null)
            {
                parts.Add(current.name);
                current = current.parent;
            }

            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        private static string FirstLine(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "";

            int newline = text.IndexOf('\n');
            string line = newline >= 0 ? text.Substring(0, newline) : text;

            return line.Length > 120
                ? line.Substring(0, 120) + "..."
                : line;
        }

        private static string Describe(AgentAction action)
        {
            if (action == null)
                return "null action";

            return (action.type ?? "unknown") +
                   (string.IsNullOrWhiteSpace(action.name) ? "" : " " + action.name) +
                   (string.IsNullOrWhiteSpace(action.path) ? "" : " " + action.path);
        }

        private static void RequireTarget(GameObject target)
        {
            if (target == null)
                throw new InvalidOperationException("Target GameObject was not found.");
        }

        private class ConsoleEntry
        {
            public string condition;
            public string stackTrace;
            public string type;
            public string time;
            public string assetPath;
            public int line;
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
        }
    }
}
