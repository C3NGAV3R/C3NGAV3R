using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace C3NGAV3R.FixerUnity
{
    public sealed class FixerUnitySmartAgent : EditorWindow
    {
        private const double DefaultPlaytestSeconds = 8.0;
        private bool playtestRunning;
        private double playtestStarted;
        private int runtimeErrors;
        private int runtimeExceptions;
        private int runtimeAsserts;
        private readonly List<string> runtimeMessages = new List<string>();
        private Vector2 scroll;
        private string report = "SMART AGENT ready.\n";
        private float playtestSeconds = 8f;

        [MenuItem("Tools/FIXER UNITY Console & Builder/SMART AGENT")]
        public static void Open()
        {
            GetWindow<FixerUnitySmartAgent>("FIXER UNITY SMART AGENT");
        }

        private void OnEnable()
        {
            Application.logMessageReceived += OnRuntimeLog;
            EditorApplication.update += Tick;
        }

        private void OnDisable()
        {
            Application.logMessageReceived -= OnRuntimeLog;
            EditorApplication.update -= Tick;
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("FIXER UNITY — SMART AGENT", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "This layer sits beside the existing Console/Builder. It inspects the real scene before acting, validates existing UI, and can run a bounded Play Mode smoke test. It never creates replacement buttons during inspection.",
                MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("SCAN CURRENT SCENE", GUILayout.Height(32)))
                    report = ScanScene();
                if (GUILayout.Button("VALIDATE UI", GUILayout.Height(32)))
                    report = ValidateUi();
            }

            EditorGUILayout.Space(5);
            playtestSeconds = EditorGUILayout.Slider("Playtest seconds", playtestSeconds, 2f, 60f);

            GUI.enabled = !playtestRunning && !EditorApplication.isPlayingOrWillChangePlaymode;
            if (GUILayout.Button("▶ PLAYTEST GAME", GUILayout.Height(42)))
                StartPlaytest();
            GUI.enabled = true;

            if (playtestRunning)
                EditorGUILayout.HelpBox("PLAYTEST RUNNING — collecting runtime errors and assertions...", MessageType.Warning);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Agent Report", EditorStyles.boldLabel);
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MinHeight(280));
            EditorGUILayout.TextArea(report, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private string ScanScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("SCENE INSPECTION");
            sb.AppendLine("Scene: " + scene.path);
            sb.AppendLine("Loaded: " + scene.isLoaded);
            sb.AppendLine("Dirty: " + scene.isDirty);
            sb.AppendLine();

            GameObject[] roots = scene.GetRootGameObjects();
            sb.AppendLine("Root objects: " + roots.Length);

            int count = 0;
            foreach (GameObject root in roots)
                ScanObject(root.transform, root.name, sb, ref count);

            sb.AppendLine();
            sb.AppendLine("Total scene objects: " + count);
            return sb.ToString();
        }

        private void ScanObject(Transform t, string path, StringBuilder sb, ref int count)
        {
            if (t == null) return;
            count++;

            GameObject go = t.gameObject;
            Component[] components = go.GetComponents<Component>();
            bool interesting = go.GetComponent<Button>() != null ||
                               go.GetComponent<Canvas>() != null ||
                               go.GetComponent<MonoBehaviour>() != null ||
                               NameLooksRelevant(go.name);

            if (interesting)
            {
                sb.AppendLine("- " + path + (go.activeSelf ? " [ACTIVE]" : " [INACTIVE]"));
                foreach (Component c in components)
                    sb.AppendLine("    " + (c == null ? "<MISSING SCRIPT>" : c.GetType().FullName));
            }

            foreach (Transform child in t)
                ScanObject(child, path + "/" + child.name, sb, ref count);
        }

        private string ValidateUi()
        {
            Scene scene = SceneManager.GetActiveScene();
            Button[] buttons = FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("UI VALIDATION");
            sb.AppendLine("Scene: " + scene.path);
            sb.AppendLine("Buttons found: " + buttons.Length);
            sb.AppendLine("Canvases found: " + canvases.Length);
            sb.AppendLine();

            foreach (Button button in buttons)
            {
                string path = GetPath(button.transform);
                int listeners = button.onClick == null ? 0 : button.onClick.GetPersistentEventCount();
                sb.AppendLine("BUTTON: " + path);
                sb.AppendLine("  active=" + button.gameObject.activeSelf + " interactable=" + button.interactable);
                sb.AppendLine("  persistent OnClick listeners=" + listeners);
                if (listeners == 0)
                    sb.AppendLine("  WARNING: button has no persistent OnClick listener.");
            }

            if (buttons.Length == 0)
                sb.AppendLine("WARNING: no UnityEngine.UI.Button components found in the scene.");

            if (canvases.Length == 0)
                sb.AppendLine("WARNING: no Canvas components found in the scene.");

            return sb.ToString();
        }

        private void StartPlaytest()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            runtimeErrors = 0;
            runtimeExceptions = 0;
            runtimeAsserts = 0;
            runtimeMessages.Clear();
            playtestStarted = EditorApplication.timeSinceStartup;
            playtestRunning = true;
            report = "PLAYTEST STARTING...\nScene: " + SceneManager.GetActiveScene().path + "\n";

            EditorSceneManager.SaveOpenScenes();
            EditorApplication.isPlaying = true;
        }

        private void Tick()
        {
            if (!playtestRunning)
                return;

            if (!EditorApplication.isPlaying && EditorApplication.timeSinceStartup - playtestStarted > 1.0)
            {
                FinishPlaytest("Play Mode exited before the bounded test completed.");
                return;
            }

            if (EditorApplication.timeSinceStartup - playtestStarted < playtestSeconds)
                return;

            EditorApplication.isPlaying = false;
            FinishPlaytest("Bounded playtest duration completed.");
        }

        private void FinishPlaytest(string reason)
        {
            playtestRunning = false;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("PLAYTEST REPORT");
            sb.AppendLine("Result: " + (runtimeErrors == 0 && runtimeExceptions == 0 && runtimeAsserts == 0 ? "PASS" : "FAIL"));
            sb.AppendLine("Reason: " + reason);
            sb.AppendLine("Errors: " + runtimeErrors);
            sb.AppendLine("Exceptions: " + runtimeExceptions);
            sb.AppendLine("Asserts: " + runtimeAsserts);
            sb.AppendLine();

            if (runtimeMessages.Count == 0)
                sb.AppendLine("No runtime Error/Exception/Assert messages captured.");
            else
            {
                sb.AppendLine("Runtime failures:");
                foreach (string message in runtimeMessages)
                    sb.AppendLine("- " + message);
            }

            report = sb.ToString();
            Repaint();
        }

        private void OnRuntimeLog(string condition, string stackTrace, LogType type)
        {
            if (!playtestRunning)
                return;

            if (type == LogType.Error) runtimeErrors++;
            else if (type == LogType.Exception) runtimeExceptions++;
            else if (type == LogType.Assert) runtimeAsserts++;
            else return;

            string message = condition ?? "<empty runtime message>";
            if (!string.IsNullOrWhiteSpace(stackTrace))
                message += "\n  " + stackTrace.Trim().Replace("\n", "\n  ");

            runtimeMessages.Add(message);
            Repaint();
        }

        private static bool NameLooksRelevant(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            string n = name.ToLowerInvariant();
            return n.Contains("button") || n.Contains("jetpack") || n.Contains("hammer") || n.Contains("kick") || n.Contains("menu") || n.Contains("player");
        }

        private static string GetPath(Transform t)
        {
            if (t == null) return "<null>";
            List<string> parts = new List<string>();
            while (t != null)
            {
                parts.Add(t.name);
                t = t.parent;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}
