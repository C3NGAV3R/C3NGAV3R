using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace C3NGAV3R.FixerUnity
{
    /// <summary>
    /// Static editor-only AI repair entry point. Never enters Play Mode.
    /// It delegates actual console repair to the existing FIXER UNITY repair pipeline.
    /// </summary>
    public sealed class FixerUnityAIFixWindow : EditorWindow
    {
        private const string EndpointPref = "FixerUnity.Endpoint";
        private const string ModelPref = "FixerUnity.Model";

        private string endpoint;
        private string model;
        private string manualError = "";
        private string result = "AI FIX ready. Static/editor-only mode — Play Mode will never be started.";
        private Vector2 scroll;
        private bool working;

        [MenuItem("Tools/FIXER UNITY/AI FIX (No Playtest)", priority = 10)]
        public static void Open()
        {
            GetWindow<FixerUnityAIFixWindow>("FIXER UNITY AI FIX");
        }

        private void OnEnable()
        {
            endpoint = EditorPrefs.GetString(EndpointPref, "http://127.0.0.1:11434/api/generate");
            model = EditorPrefs.GetString(ModelPref, "qwen2.5-coder:3b");
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("FIXER UNITY — AI FIX", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "STATIC REPAIR ONLY. This tool inspects the editor state and uses the existing FIXER UNITY repair pipeline. It NEVER enters Play Mode or runs a playtest.",
                MessageType.Info);

            endpoint = EditorGUILayout.TextField("Local AI URL", endpoint);
            model = EditorGUILayout.TextField("AI Model", model);
            EditorPrefs.SetString(EndpointPref, endpoint);
            EditorPrefs.SetString(ModelPref, model);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Current Scene", SceneManager.GetActiveScene().path);
            EditorGUILayout.LabelField("Selected Object", Selection.activeGameObject == null ? "NONE" : GetPath(Selection.activeGameObject.transform));

            EditorGUILayout.Space(6);
            if (GUILayout.Button("AI FIX CURRENT PROJECT", GUILayout.Height(48)))
                RunAiFix();

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Optional error / request", EditorStyles.boldLabel);
            manualError = EditorGUILayout.TextArea(manualError, GUILayout.MinHeight(90));

            GUI.enabled = !working && !string.IsNullOrWhiteSpace(manualError);
            if (GUILayout.Button("AI FIX THIS ERROR", GUILayout.Height(36)))
                RunAiFix(manualError.Trim());
            GUI.enabled = true;

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Result", EditorStyles.boldLabel);
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MinHeight(180));
            EditorGUILayout.TextArea(result, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private void RunAiFix(string suppliedError = null)
        {
            if (working)
                return;

            working = true;
            try
            {
                string error = suppliedError;
                if (string.IsNullOrWhiteSpace(error))
                {
                    error = "Static editor repair requested. Inspect the current Unity project, current scene hierarchy, selected object, and any currently captured console errors. Fix the concrete issue with the smallest safe change. Do not enter Play Mode.";
                }

                EditorPrefs.SetString(EndpointPref, endpoint);
                EditorPrefs.SetString(ModelPref, model);

                FixerUnityWindow main = GetWindow<FixerUnityWindow>("FIXER UNITY");
                if (main == null)
                    throw new Exception("Could not open the existing FIXER UNITY repair window.");

                MethodInfo method = typeof(FixerUnityWindow).GetMethod(
                    "RunConsoleAiFix",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                if (method == null)
                    throw new MissingMethodException("FIXER UNITY repair pipeline method RunConsoleAiFix was not found.");

                // Delegates to the existing repair pipeline. No Play Mode API is called here.
                method.Invoke(main, new object[] { error, "" });

                result = "AI FIX dispatched to the existing FIXER UNITY repair pipeline.\n\n" +
                         "PLAYTEST: NOT RUN\n" +
                         "PLAY MODE: NOT STARTED\n" +
                         "SCENE: " + SceneManager.GetActiveScene().path;
            }
            catch (Exception ex)
            {
                result = "AI FIX FAILED\n\n" + ex.Message;
                if (ex.InnerException != null)
                    result += "\n" + ex.InnerException.Message;
            }
            finally
            {
                working = false;
                Repaint();
            }
        }

        private static string GetPath(Transform t)
        {
            var parts = new List<string>();
            while (t != null)
            {
                parts.Add(t.name);
                t = t.parent;
            }
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }
    }
}
