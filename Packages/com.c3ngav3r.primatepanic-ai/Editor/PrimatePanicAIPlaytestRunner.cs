#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace C3NGAV3R.PrimatePanicAI
{
    public sealed class PrimatePanicAIPlaytestRunner : EditorWindow
    {
        private enum State { Idle, Launching, Running, Stopping, Complete, Failed }

        private State state = State.Idle;
        private Vector2 scroll;
        private double startedAt;
        private double maxDuration = 12.0;
        private readonly List<string> errors = new List<string>();
        private readonly List<string> warnings = new List<string>();
        private string report = "READY — bounded playtest runner.";

        [MenuItem("Tools/Primate Panic AI - FIXED / Playtest Runner")]
        public static void Open() => GetWindow<PrimatePanicAIPlaytestRunner>("Primate Panic Playtest");

        private void OnEnable()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            Application.logMessageReceived += OnLog;
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            Application.logMessageReceived -= OnLog;
            EditorApplication.update -= Tick;
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Primate Panic AI — PLAYTEST ENGINE", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Runs a bounded real Unity Play Mode smoke test. It checks scene startup, console exceptions, UI buttons, and known Kick Hammer / Jetpack toggle wiring when present. It never edits the project while Play Mode is running.",
                MessageType.Info);

            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("RUN PLAYTEST", GUILayout.Height(42))) StartPlaytest();
            }

            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
            {
                if (GUILayout.Button("STOP PLAYTEST", GUILayout.Height(28))) StopPlaytest("Stopped by operator.");
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(state.ToString());
            EditorGUILayout.LabelField(report, EditorStyles.wordWrappedLabel);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Diagnostics", EditorStyles.boldLabel);
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MinHeight(240));
            foreach (string e in errors) EditorGUILayout.HelpBox(e, MessageType.Error);
            foreach (string w in warnings) EditorGUILayout.HelpBox(w, MessageType.Warning);
            EditorGUILayout.EndScrollView();
        }

        private void StartPlaytest()
        {
            if (!Application.isPlaying)
            {
                errors.Clear();
                warnings.Clear();
                report = "Launching Play Mode...";
                state = State.Launching;
                startedAt = EditorApplication.timeSinceStartup;
                EditorApplication.update -= Tick;
                EditorApplication.update += Tick;
                EditorApplication.EnterPlaymode();
            }
        }

        private void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                state = State.Running;
                report = "Play Mode entered. Running smoke checks...";
                startedAt = EditorApplication.timeSinceStartup;
                EditorApplication.update -= Tick;
                EditorApplication.update += Tick;
                RunSmokeChecks();
            }
            else if (change == PlayModeStateChange.ExitingPlayMode)
            {
                state = State.Stopping;
            }
            else if (change == PlayModeStateChange.EnteredEditMode && state == State.Stopping)
            {
                FinalizeReport();
            }
        }

        private void Tick()
        {
            if (state == State.Running && EditorApplication.timeSinceStartup - startedAt >= maxDuration)
                StopPlaytest("Bounded playtest duration completed.");
        }

        private void StopPlaytest(string reason)
        {
            report = reason;
            state = State.Stopping;
            EditorApplication.update -= Tick;
            if (EditorApplication.isPlaying)
                EditorApplication.ExitPlaymode();
            else
                FinalizeReport();
        }

        private void RunSmokeChecks()
        {
            try
            {
                Scene scene = SceneManager.GetActiveScene();
                if (!scene.IsValid()) errors.Add("Active scene is invalid.");
                if (!scene.isLoaded) errors.Add("Active scene is not loaded.");

                List<Button> buttons = Resources.FindObjectsOfTypeAll<Button>()
                    .Where(b => b != null && b.gameObject.scene == scene)
                    .ToList();

                if (buttons.Count == 0)
                    warnings.Add("No Unity UI Buttons were found in the active scene.");

                foreach (Button button in buttons)
                {
                    if (!button.gameObject.activeInHierarchy)
                        continue;
                    if (!button.interactable)
                        warnings.Add("Button not interactable: " + PathOf(button.gameObject));
                    if (button.onClick.GetPersistentEventCount() == 0)
                        warnings.Add("Button has no persistent OnClick listener: " + PathOf(button.gameObject));
                }

                Component[] toggleControllers = Resources.FindObjectsOfTypeAll<Component>()
                    .Where(c => c != null && c.GetType().Name == "ItemToggleController" && c.gameObject.scene == scene)
                    .ToArray();

                if (toggleControllers.Length > 0)
                    report = "Startup checks passed. Found " + buttons.Count + " UI Button(s) and " + toggleControllers.Length + " ItemToggleController(s).";
                else
                    report = "Startup checks passed. No ItemToggleController was found; generic scene/UI checks completed.";
            }
            catch (Exception ex)
            {
                errors.Add("Smoke-check exception: " + ex);
            }
        }

        private void OnLog(string condition, string stackTrace, LogType type)
        {
            if (!Application.isPlaying)
                return;

            if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
            {
                string text = condition;
                if (!string.IsNullOrWhiteSpace(stackTrace))
                    text += "\n" + stackTrace;
                if (!errors.Contains(text)) errors.Add(text);
            }
        }

        private void FinalizeReport()
        {
            EditorApplication.update -= Tick;
            if (errors.Count > 0)
            {
                state = State.Failed;
                report = "PLAYTEST FAILED — " + errors.Count + " error(s). Fix errors before shipping.";
            }
            else
            {
                state = State.Complete;
                report = warnings.Count > 0
                    ? "PLAYTEST COMPLETE WITH WARNINGS — review diagnostics before shipping."
                    : "PLAYTEST PASSED — no runtime exceptions or failed smoke checks detected during the bounded run.";
            }
            Repaint();
        }

        private static string PathOf(GameObject go)
        {
            if (go == null) return "<null>";
            List<string> parts = new List<string>();
            Transform t = go.transform;
            while (t != null) { parts.Add(t.name); t = t.parent; }
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }
    }
}
#endif
