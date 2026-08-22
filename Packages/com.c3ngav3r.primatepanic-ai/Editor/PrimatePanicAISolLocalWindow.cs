using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace C3NGAV3R.PrimatePanicAI
{
    public class PrimatePanicAISolLocalWindow : EditorWindow
    {
        private const string EndpointPref = "PrimatePanicAI.SolLocal.Endpoint";
        private const string ModelPref = "PrimatePanicAI.SolLocal.Model";
        private const string DeepPref = "PrimatePanicAI.SolLocal.Deep";
        private const string VerifyPref = "PrimatePanicAI.SolLocal.Verify";
        private const string PlanOnlyPref = "PrimatePanicAI.SolLocal.PlanOnly";

        private string endpoint = "http://127.0.0.1:11434/api/generate";
        private string model = "qwen3:4b";
        private bool deepReasoning = true;
        private bool autoVerify = true;
        private bool planOnly;
        private bool working;
        private string prompt = "";
        private string result = "SOL LOCAL ready.";
        private Vector2 scroll;

        private static readonly Queue<string> RecentErrors = new Queue<string>();
        private const int MaxErrors = 12;

        [MenuItem("Tools/Primate Panic AI - SOL LOCAL")]
        public static void Open()
        {
            GetWindow<PrimatePanicAISolLocalWindow>("Primate Panic AI SOL LOCAL");
        }

        private void OnEnable()
        {
            endpoint = EditorPrefs.GetString(EndpointPref, endpoint);
            model = EditorPrefs.GetString(ModelPref, model);
            deepReasoning = EditorPrefs.GetBool(DeepPref, true);
            autoVerify = EditorPrefs.GetBool(VerifyPref, true);
            planOnly = EditorPrefs.GetBool(PlanOnlyPref, false);
            Application.logMessageReceived -= CaptureLog;
            Application.logMessageReceived += CaptureLog;
        }

        private void OnDisable()
        {
            Application.logMessageReceived -= CaptureLog;
        }

        private static void CaptureLog(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)
                return;

            string line = condition ?? "Unknown error";
            if (line.Length > 600)
                line = line.Substring(0, 600);

            RecentErrors.Enqueue(line);
            while (RecentErrors.Count > MaxErrors)
                RecentErrors.Dequeue();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Primate Panic AI - SOL LOCAL v1.1", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "100% local / no credits. SOL LOCAL uses multiple reasoning passes: understand -> plan -> review -> execute -> verify -> one repair pass. It does not contain OpenAI model weights; it uses your local Ollama model.",
                MessageType.Info);

            EditorGUI.BeginChangeCheck();
            endpoint = EditorGUILayout.TextField("Ollama URL", endpoint);
            model = EditorGUILayout.TextField("Local Brain", model);
            deepReasoning = EditorGUILayout.ToggleLeft("Deep reasoning (architect + reviewer)", deepReasoning);
            autoVerify = EditorGUILayout.ToggleLeft("Verify work and auto-repair once", autoVerify);
            planOnly = EditorGUILayout.ToggleLeft("PLAN ONLY (do not change Unity)", planOnly);

            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetString(EndpointPref, endpoint);
                EditorPrefs.SetString(ModelPref, model);
                EditorPrefs.SetBool(DeepPref, deepReasoning);
                EditorPrefs.SetBool(VerifyPref, autoVerify);
                EditorPrefs.SetBool(PlanOnlyPref, planOnly);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Use Qwen3 4B", GUILayout.Height(27)))
                {
                    model = "qwen3:4b";
                    EditorPrefs.SetString(ModelPref, model);
                }

                if (GUILayout.Button("Test Local Brain", GUILayout.Height(27)))
                    TestBrain();

                if (GUILayout.Button("Clear Errors", GUILayout.Height(27)))
                    RecentErrors.Clear();
            }

            GameObject selected = Selection.activeGameObject;
            EditorGUILayout.HelpBox(
                selected == null
                    ? "Selected: NONE. Creation requests are still allowed."
                    : "Selected: " + HierarchyPath(selected.transform),
                MessageType.None);

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Tell SOL LOCAL what you want", EditorStyles.boldLabel);
            prompt = EditorGUILayout.TextArea(prompt, GUILayout.MinHeight(150));

            GUI.enabled = !working && !string.IsNullOrWhiteSpace(prompt) && !string.IsNullOrWhiteSpace(model);
            if (GUILayout.Button(working ? "SOL LOCAL IS WORKING..." : planOnly ? "BUILD SOL PLAN" : "RUN SOL LOCAL AGENT", GUILayout.Height(44)))
                RunAgent();
            GUI.enabled = true;

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Result", EditorStyles.boldLabel);
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MinHeight(250));
            EditorGUILayout.TextArea(result, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private void TestBrain()
        {
            OllamaRequest req = MakeRequest(
                "Return JSON only.",
                "Return exactly: {\"message\":\"SOL LOCAL CONNECTED\",\"actions\":[]}",
                128);

            Send(req, 240, text =>
            {
                LocalPlan p = TryParsePlan(text);
                result = p != null && (p.message ?? "").Contains("SOL LOCAL CONNECTED")
                    ? "SOL LOCAL CONNECTED ✅\nModel: " + model
                    : "Brain replied:\n" + text;
            });
        }

        private void RunAgent()
        {
            string userRequest = prompt.Trim();
            string context = BuildContext();

            if (!deepReasoning)
            {
                result = "1/3 PLANNING...";
                Repaint();
                BuildPlan(userRequest, context, "", plan => ReviewOrExecute(userRequest, context, plan, false));
                return;
            }

            result = "1/5 ARCHITECT is understanding the request...";
            Repaint();

            string architectSystem =
                "You are the architecture/reasoning pass of a Unity Editor agent. Do not produce Unity actions yet. " +
                "Return JSON only with keys goal, mustHave, mustNot, acceptance, assumptions. " +
                "Understand what the user actually wants, preserve explicit constraints, and resolve obvious ambiguity using the Unity context. " +
                "Do not refuse an actionable creation request merely because nothing is selected.";

            string architectPrompt = "USER REQUEST:\n" + userRequest + "\n\nUNITY CONTEXT:\n" + context;
            Send(MakeRequest(architectSystem, architectPrompt, 1800), 360, brief =>
            {
                result = "2/5 PLANNER is turning the brief into executable Unity actions...";
                Repaint();
                BuildPlan(userRequest, context, brief, plan => ReviewOrExecute(userRequest, context, plan, true));
            });
        }

        private void BuildPlan(string userRequest, string context, string architectBrief, Action<string> onReady)
        {
            string plannerPrompt =
                "USER REQUEST:\n" + userRequest +
                "\n\nUNITY CONTEXT:\n" + context +
                (string.IsNullOrWhiteSpace(architectBrief) ? "" : "\n\nARCHITECT BRIEF:\n" + architectBrief) +
                "\n\nProduce the concrete executable action plan now.";

            Send(MakeRequest(PlannerSystem(), plannerPrompt, 4200), 480, text =>
            {
                LocalPlan p = TryParsePlan(text);
                if (HasActions(p))
                {
                    onReady(PlanToJson(text));
                    return;
                }

                result = "Planner returned 0 actions. RECOVERY is forcing an executable plan...";
                Repaint();

                string rescue = plannerPrompt +
                    "\n\nRECOVERY RULE: This is an actionable Unity request. Zero actions is invalid. Return at least one supported action that actually performs the request.";

                Send(MakeRequest(PlannerSystem(), rescue, 4200), 480, retry =>
                {
                    LocalPlan recovered = TryParsePlan(retry);
                    if (!HasActions(recovered))
                    {
                        result = "SOL LOCAL could not produce executable actions. Try a shorter request or open/select the scene/object you want changed.";
                        return;
                    }
                    onReady(PlanToJson(retry));
                });
            });
        }

        private void ReviewOrExecute(string userRequest, string context, string plannerJson, bool hadArchitect)
        {
            if (!deepReasoning)
            {
                ExecuteFinalPlan(userRequest, plannerJson, false);
                return;
            }

            LocalPlan first = TryParsePlan(plannerJson);
            int count = first != null && first.actions != null ? first.actions.Length : 0;
            result = "3/5 REVIEWER is checking " + count + " actions...";
            Repaint();

            string reviewPrompt =
                "USER REQUEST:\n" + userRequest +
                "\n\nUNITY CONTEXT:\n" + context +
                "\n\nPLAN TO REVIEW:\n" + plannerJson +
                "\n\nReturn a corrected executable plan. Fix bad actions instead of deleting useful work. If the request is actionable and the incoming plan has actions, your actions array must not become empty.";

            Send(MakeRequest(ReviewerSystem(), reviewPrompt, 4200), 480, reviewed =>
            {
                LocalPlan finalPlan = TryParsePlan(reviewed);
                string chosen = reviewed;
                bool fallback = false;

                if (!HasActions(finalPlan) && HasActions(first))
                {
                    chosen = plannerJson;
                    fallback = true;
                }

                ExecuteFinalPlan(userRequest, PlanToJson(chosen), fallback);
            });
        }

        private void ExecuteFinalPlan(string userRequest, string planJson, bool reviewerFallback)
        {
            LocalPlan plan = TryParsePlan(planJson);
            if (!HasActions(plan))
            {
                result = "No executable actions survived planning.";
                return;
            }

            if (planOnly)
            {
                StringBuilder preview = new StringBuilder();
                preview.AppendLine(plan.message ?? "Plan ready.");
                preview.AppendLine("Actions: " + plan.actions.Length);
                if (reviewerFallback)
                    preview.AppendLine("Reviewer fallback: planner plan kept because reviewer erased the actions.");
                for (int i = 0; i < plan.actions.Length; i++)
                    preview.AppendLine((i + 1) + ". " + (plan.actions[i].type ?? "unknown") + " " + (plan.actions[i].name ?? ""));
                result = preview.ToString();
                return;
            }

            result = deepReasoning ? "4/5 EXECUTOR is changing Unity..." : "2/3 EXECUTOR is changing Unity...";
            Repaint();

            string report;
            try
            {
                report = ExecuteThroughSmartExecutor(planJson);
            }
            catch (Exception ex)
            {
                result = "EXECUTOR FAILED: " + ex.Message;
                return;
            }

            if (!autoVerify)
            {
                result = (reviewerFallback ? "Reviewer fallback used.\n" : "") + report;
                return;
            }

            result = deepReasoning ? "5/5 VERIFIER is inspecting what Unity actually created..." : "3/3 VERIFIER is inspecting the result...";
            Repaint();

            string after = BuildContext();
            string verifyPrompt =
                "ORIGINAL USER REQUEST:\n" + userRequest +
                "\n\nEXECUTION REPORT:\n" + report +
                "\n\nUNITY STATE AFTER EXECUTION:\n" + after +
                "\n\nVerify the request against the actual state. If the result is good enough, return {\"message\":\"VERIFIED\",\"actions\":[]}. " +
                "If something concrete is missing or broken, return ONLY a small repair plan using the supported action schema. Do not recreate working parts.";

            Send(MakeRequest(VerifierSystem(), verifyPrompt, 2800), 420, verified =>
            {
                LocalPlan repair = TryParsePlan(verified);
                if (!HasActions(repair))
                {
                    result = (reviewerFallback ? "Reviewer fallback used.\n" : "") + report + "\n\n✅ SOL VERIFY: " + (repair != null ? repair.message : "Verified with no repair actions.");
                    return;
                }

                string repairReport;
                try
                {
                    repairReport = ExecuteThroughSmartExecutor(PlanToJson(verified));
                }
                catch (Exception ex)
                {
                    result = report + "\n\nVerifier proposed a repair, but repair execution failed: " + ex.Message;
                    return;
                }

                result = (reviewerFallback ? "Reviewer fallback used.\n" : "") +
                    report +
                    "\n\n🔧 SOL AUTO-REPAIR APPLIED:\n" + repairReport +
                    "\n\nVerification is bounded to one repair pass to avoid loops.";
            });
        }

        private string ExecuteThroughSmartExecutor(string planJson)
        {
            Type smartType = typeof(PrimatePanicAISmartWindow);
            MethodInfo parse = smartType.GetMethod("ParsePlan", BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo apply = smartType.GetMethod("ApplyPlan", BindingFlags.NonPublic | BindingFlags.Instance);

            if (parse == null || apply == null)
                throw new InvalidOperationException("The shared bounded Unity executor was not found. Reinstall the full Primate Panic AI package.");

            object executorPlan;
            try
            {
                executorPlan = parse.Invoke(null, new object[] { planJson });
            }
            catch (TargetInvocationException ex)
            {
                throw new InvalidOperationException(ex.InnerException != null ? ex.InnerException.Message : ex.Message);
            }

            PrimatePanicAISmartWindow hidden = ScriptableObject.CreateInstance<PrimatePanicAISmartWindow>();
            try
            {
                object raw;
                try
                {
                    raw = apply.Invoke(hidden, new object[] { executorPlan });
                }
                catch (TargetInvocationException ex)
                {
                    throw new InvalidOperationException(ex.InnerException != null ? ex.InnerException.Message : ex.Message);
                }
                return raw as string ?? "Executor finished.";
            }
            finally
            {
                if (hidden != null)
                    DestroyImmediate(hidden);
            }
        }

        private string BuildContext()
        {
            StringBuilder sb = new StringBuilder();
            Scene scene = SceneManager.GetActiveScene();
            sb.AppendLine("ACTIVE SCENE: " + (string.IsNullOrWhiteSpace(scene.path) ? scene.name : scene.path));
            sb.AppendLine("PLAY MODE: " + EditorApplication.isPlaying);

            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                sb.AppendLine("SELECTED: NONE");
            }
            else
            {
                sb.AppendLine("SELECTED: " + HierarchyPath(selected.transform));
                sb.AppendLine("SELECTED ACTIVE: " + selected.activeSelf);
                sb.AppendLine("SELECTED LOCAL POSITION: " + selected.transform.localPosition);
                sb.AppendLine("SELECTED LOCAL ROTATION: " + selected.transform.localEulerAngles);
                sb.AppendLine("SELECTED LOCAL SCALE: " + selected.transform.localScale);
                sb.AppendLine("SELECTED COMPONENTS:");
                foreach (Component c in selected.GetComponents<Component>())
                    sb.AppendLine(c == null ? "- MISSING SCRIPT" : "- " + c.GetType().FullName);

                AppendSelectedScriptSources(sb, selected, 8000);
            }

            sb.AppendLine();
            sb.AppendLine("SCENE HIERARCHY SNAPSHOT:");
            int remaining = 90;
            if (scene.IsValid() && scene.isLoaded)
            {
                GameObject[] roots = scene.GetRootGameObjects();
                for (int i = 0; i < roots.Length && remaining > 0; i++)
                    AppendHierarchy(sb, roots[i].transform, 0, ref remaining);
            }

            if (RecentErrors.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("RECENT UNITY ERRORS:");
                foreach (string e in RecentErrors)
                    sb.AppendLine("- " + e);
            }

            sb.AppendLine();
            sb.AppendLine("HARD PROJECT RULES:");
            sb.AppendLine("- Never require a selection for a from-scratch creation request.");
            sb.AppendLine("- Do not modify Gorilla locomotion, XR Origin, gameplay Main Camera, Photon, or player Rigidbody unless the user explicitly asks.");
            sb.AppendLine("- Use real Unity UI components for visible UI.");
            sb.AppendLine("- Never fabricate binary PNG/JPG/FBX/font files by writing text.");
            sb.AppendLine("- New custom scripts may need Unity to compile before a later run can attach them.");
            sb.AppendLine("- Prefer fixing existing work over duplicating entire systems.");
            return sb.ToString();
        }

        private static void AppendHierarchy(StringBuilder sb, Transform t, int depth, ref int remaining)
        {
            if (t == null || remaining <= 0)
                return;

            remaining--;
            sb.Append(new string(' ', depth * 2));
            sb.Append("- ");
            sb.Append(t.name);

            Component[] comps = t.GetComponents<Component>();
            if (comps.Length > 1)
            {
                sb.Append(" [");
                int shown = 0;
                for (int i = 0; i < comps.Length && shown < 5; i++)
                {
                    if (comps[i] == null)
                        continue;
                    if (shown > 0) sb.Append(", ");
                    sb.Append(comps[i].GetType().Name);
                    shown++;
                }
                sb.Append("]");
            }
            sb.AppendLine();

            for (int i = 0; i < t.childCount && remaining > 0; i++)
                AppendHierarchy(sb, t.GetChild(i), depth + 1, ref remaining);
        }

        private static void AppendSelectedScriptSources(StringBuilder sb, GameObject selected, int totalLimit)
        {
            int used = 0;
            MonoBehaviour[] behaviours = selected.GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length && used < totalLimit; i++)
            {
                MonoBehaviour mb = behaviours[i];
                if (mb == null)
                    continue;

                MonoScript script = MonoScript.FromMonoBehaviour(mb);
                if (script == null)
                    continue;

                string path = AssetDatabase.GetAssetPath(script);
                if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                    continue;

                string full = Path.GetFullPath(Path.Combine(Directory.GetParent(Application.dataPath).FullName, path));
                if (!File.Exists(full))
                    continue;

                string source;
                try { source = File.ReadAllText(full); }
                catch { continue; }

                int room = totalLimit - used;
                if (source.Length > room)
                    source = source.Substring(0, room);

                sb.AppendLine();
                sb.AppendLine("ATTACHED SCRIPT SOURCE: " + path);
                sb.AppendLine(source);
                used += source.Length;
            }
        }

        private static string PlannerSystem()
        {
            return
                "You are the execution planner for a Unity Editor agent. Return ONLY valid JSON, never markdown. " +
                "Output shape: {\"message\":\"short summary\",\"actions\":[...]} . " +
                "Supported action types: create_scene, open_scene, create_ui, create_gameobject, create_or_replace_file, add_component, remove_component, set_active, set_transform, set_component_field. " +
                "Action fields available: type,name,scenePath,parentPath,targetPath,uiType,text,color,path,content,componentType,field,value,primitive,boolValue,worldSpace,renderMode,x,y,z,rotX,rotY,rotZ,scale,width,height,fontSize,components. " +
                "uiType values: canvas,background,image,panel,title,text,button,slider,eventsystem. " +
                "For new scenes, create_scene must come before actions targeting that scene. For World Space Canvas set worldSpace=true or renderMode=WorldSpace and include size/position/rotation/scale. " +
                "For scripts use create_or_replace_file with a real Assets/Scripts/Name.cs path and COMPLETE compiling C# source. " +
                "Never invent fake PNG/JPG/FBX/font files. Never replace the gameplay Camera/XR Origin/Gorilla Rig/Photon/player Rigidbody unless explicitly requested. " +
                "For actionable create/fix/rebuild requests actions must not be empty. Keep the plan focused and usually under 30 actions. Prefer strong create_ui actions over many brittle component field edits.";
        }

        private static string ReviewerSystem()
        {
            return
                "You are a senior Unity engineer reviewing an executable JSON action plan. Return ONLY corrected JSON in the exact same {message,actions} shape. " +
                "Preserve the user's actual goal. Fix unsupported actions, bad scene routing, wrong parent paths, broken UI dimensions, unsafe duplicates, incomplete script paths, and obvious compile mistakes. " +
                "Do not erase a useful actionable plan to zero actions. Remove fake binary asset writes and unrelated work. Prefer minimal corrections rather than rebuilding everything.";
        }

        private static string VerifierSystem()
        {
            return
                "You are the post-execution verifier for a Unity Editor agent. Return ONLY JSON {\"message\":\"verification result\",\"actions\":[...]}. " +
                "Compare the original request with the execution report and actual Unity state. If good enough, actions must be empty. " +
                "If something concrete is missing/broken, return only a SMALL supported repair plan. Do not recreate working systems or loop endlessly.";
        }

        private OllamaRequest MakeRequest(string system, string user, int numPredict)
        {
            return new OllamaRequest
            {
                model = model.Trim(),
                system = system,
                prompt = user,
                stream = false,
                format = "json",
                keep_alive = "0",
                options = new OllamaOptions
                {
                    num_ctx = 4096,
                    num_predict = numPredict,
                    temperature = 0.08f,
                    seed = 42,
                    repeat_penalty = 1.05f
                }
            };
        }

        private void Send(OllamaRequest request, int timeoutSeconds, Action<string> onSuccess)
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
            req.timeout = timeoutSeconds;

            UnityWebRequestAsyncOperation op = req.SendWebRequest();
            op.completed += _ =>
            {
                working = false;
                try
                {
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
                            onSuccess(response.response ?? "");
                    }
                }
                finally
                {
                    req.Dispose();
                    Repaint();
                }
            };
        }

        private static LocalPlan TryParsePlan(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            int start = text.IndexOf('{');
            int end = text.LastIndexOf('}');
            if (start < 0 || end <= start)
                return null;

            try
            {
                return JsonUtility.FromJson<LocalPlan>(text.Substring(start, end - start + 1));
            }
            catch
            {
                return null;
            }
        }

        private static bool HasActions(LocalPlan plan)
        {
            return plan != null && plan.actions != null && plan.actions.Length > 0;
        }

        private static string PlanToJson(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;
            int start = text.IndexOf('{');
            int end = text.LastIndexOf('}');
            return start >= 0 && end > start ? text.Substring(start, end - start + 1) : text;
        }

        private static string HierarchyPath(Transform t)
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
        private class OllamaRequest
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
        private class LocalPlan
        {
            public string message;
            public LocalAction[] actions;
        }

        [Serializable]
        private class LocalAction
        {
            public string type;
            public string name;
        }
    }
}
