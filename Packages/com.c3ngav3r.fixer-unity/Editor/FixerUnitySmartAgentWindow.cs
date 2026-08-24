#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Networking;

namespace C3NGAV3R.FixerUnity
{
    public sealed class FixerUnitySmartAgentWindow : EditorWindow
    {
        private const string EndpointPref = "FixerUnity.Smart.Endpoint";
        private const string ModelPref = "FixerUnity.Smart.Model";
        private string endpoint = "http://127.0.0.1:11434/api/generate";
        private string model = "qwen2.5-coder:3b";
        private string request = "";
        private string report = "FIXER UNITY SMART AGENT v2 ready.\n";
        private Vector2 scroll;
        private bool busy;
        private bool playtesting;
        private double playtestStarted;
        private double playtestSeconds = 15;
        private int repairCycles;
        private const int MaxRepairCycles = 5;
        private readonly List<string> runtimeErrors = new List<string>();

        [MenuItem("Tools/FIXER UNITY Console & Builder/SMART AGENT")]
        public static void Open() => GetWindow<FixerUnitySmartAgentWindow>("FIXER SMART AGENT");

        private void OnEnable()
        {
            endpoint = EditorPrefs.GetString(EndpointPref, endpoint);
            model = EditorPrefs.GetString(ModelPref, model);
            EditorApplication.playModeStateChanged += OnPlayMode;
            Application.logMessageReceived += OnLog;
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayMode;
            Application.logMessageReceived -= OnLog;
            EditorApplication.update -= TickPlaytest;
        }

        private void OnLog(string condition, string stack, LogType type)
        {
            if (!playtesting) return;
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                string msg = condition + (string.IsNullOrWhiteSpace(stack) ? "" : "\n" + stack);
                if (!runtimeErrors.Contains(msg)) runtimeErrors.Add(msg);
                Repaint();
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("FIXER UNITY — SMART AGENT v2", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("INSPECT → PLAN → MODIFY → COMPILE → PLAYTEST → DIAGNOSE → REPAIR → VERIFY.\nThe agent must never guess an existing GameObject or claim success without verification.", MessageType.Info);

            endpoint = EditorGUILayout.TextField("Local AI URL", endpoint);
            model = EditorGUILayout.TextField("Model", model);
            EditorPrefs.SetString(EndpointPref, endpoint);
            EditorPrefs.SetString(ModelPref, model);

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Current Scene", SceneManager.GetActiveScene().path);
            EditorGUILayout.LabelField("Compile Errors", HasCompileErrors() ? "BLOCKED" : "CLEAR");
            EditorGUILayout.LabelField("Play Mode", EditorApplication.isPlaying ? "RUNNING" : "STOPPED");
            EditorGUILayout.LabelField("Repair Cycles", repairCycles + "/" + MaxRepairCycles);

            EditorGUILayout.Space(6);
            request = EditorGUILayout.TextArea(request, GUILayout.MinHeight(90));

            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.enabled = !busy && !EditorApplication.isPlaying;
                if (GUILayout.Button("SCAN PROJECT", GUILayout.Height(30))) report = BuildSceneReport();
                if (GUILayout.Button("PLAYTEST", GUILayout.Height(30))) StartPlaytest();
                if (GUILayout.Button("AI FIX + TEST", GUILayout.Height(30))) RunAgent();
                GUI.enabled = true;
            }

            if (playtesting)
                EditorGUILayout.HelpBox("Playtest running… errors are being captured.", MessageType.None);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Result", EditorStyles.boldLabel);
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MinHeight(220));
            EditorGUILayout.TextArea(report, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private string BuildSceneReport()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("SCENE: " + SceneManager.GetActiveScene().path);
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects()) Scan(root, root.name, sb);
            Button[] buttons = Resources.FindObjectsOfTypeAll<Button>().Where(b => b.gameObject.scene.IsValid()).ToArray();
            sb.AppendLine("\nBUTTONS: " + buttons.Length);
            foreach (Button b in buttons)
                sb.AppendLine("BUTTON " + Path(b.transform) + " active=" + b.gameObject.activeSelf + " interactable=" + b.interactable + " listeners=" + b.onClick.GetPersistentEventCount());
            return sb.ToString();
        }

        private void Scan(GameObject go, string path, StringBuilder sb)
        {
            sb.AppendLine(path + " | active=" + go.activeSelf + " | components=" + string.Join(",", go.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().FullName)));
            foreach (Transform child in go.transform) Scan(child.gameObject, path + "/" + child.name, sb);
        }

        private static string Path(Transform t)
        {
            Stack<string> parts = new Stack<string>();
            while (t != null) { parts.Push(t.name); t = t.parent; }
            return string.Join("/", parts.ToArray());
        }

        private void StartPlaytest()
        {
            if (EditorApplication.isPlaying) return;
            if (HasCompileErrors()) { report = "BLOCKED: Unity has compiler errors. Fix compilation before playtesting.\n" + BuildSceneReport(); return; }
            runtimeErrors.Clear();
            playtesting = true;
            playtestStarted = EditorApplication.timeSinceStartup;
            EditorApplication.update += TickPlaytest;
            EditorApplication.isPlaying = true;
            report = "PLAYTEST STARTED.\n";
        }

        private void TickPlaytest()
        {
            if (!playtesting) return;
            if (EditorApplication.timeSinceStartup - playtestStarted >= playtestSeconds)
            {
                EditorApplication.isPlaying = false;
                EditorApplication.update -= TickPlaytest;
                playtesting = false;
                report = runtimeErrors.Count == 0
                    ? "PLAYTEST PASS ✅\nNo Error/Exception/Assert messages captured during the bounded smoke test."
                    : "PLAYTEST FAIL ❌\n" + string.Join("\n\n", runtimeErrors);
                Repaint();
            }
        }

        private void OnPlayMode(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode) Repaint();
            if (state == PlayModeStateChange.ExitingPlayMode && playtesting)
            {
                playtesting = false;
                EditorApplication.update -= TickPlaytest;
            }
        }

        private bool HasCompileErrors()
        {
            return UnityEditor.Compilation.CompilationPipeline.GetAssemblies( UnityEditor.Compilation.AssembliesType.Player ).Any(a => a.sourceFiles == null);
        }

        private void RunAgent()
        {
            if (string.IsNullOrWhiteSpace(request)) { report = "BLOCKED: Enter a request first."; return; }
            if (HasCompileErrors()) { report = "BLOCKED: Unity has compiler errors.\n" + BuildSceneReport(); return; }
            busy = true;
            repairCycles = 0;
            string context = BuildSceneReport();
            string prompt = "You are FIXER UNITY SMART AGENT. Return JSON only. Analyze the user's request against the REAL scene context. Never guess objects. Prefer existing objects. If a change is needed, describe the minimum safe editor operations. Then the editor will playtest. User request:\n" + request + "\nSCENE:\n" + context;
            Send(prompt, reply =>
            {
                busy = false;
                report = "AI ANALYSIS:\n" + reply + "\n\nNEXT: run PLAYTEST to verify runtime behavior.\n";
                Repaint();
            });
        }

        private void Send(string prompt, Action<string> done)
        {
            string json = "{\"model\":\"" + Escape(model) + "\",\"prompt\":\"" + Escape(prompt) + "\",\"stream\":false}";
            UnityWebRequest req = new UnityWebRequest(endpoint, "POST");
            byte[] body = Encoding.UTF8.GetBytes(json);
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            var op = req.SendWebRequest();
            op.completed += _ =>
            {
                string text = req.result == UnityWebRequest.Result.Success ? req.downloadHandler.text : "AI ERROR: " + req.error;
                done(text);
                req.Dispose();
            };
        }

        private static string Escape(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n");
    }
}
#endif
