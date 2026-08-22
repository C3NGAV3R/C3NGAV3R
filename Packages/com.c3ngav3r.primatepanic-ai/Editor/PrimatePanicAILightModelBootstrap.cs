using UnityEditor;
using UnityEngine;

namespace C3NGAV3R.PrimatePanicAI
{
    [InitializeOnLoad]
    internal static class PrimatePanicAILightModelBootstrap
    {
        private const string ModelPref = "PrimatePanicAI.Smart.Model";
        private const string LightModel = "qwen3:4b";

        static PrimatePanicAILightModelBootstrap()
        {
            string current = EditorPrefs.GetString(ModelPref, "");
            if (string.IsNullOrWhiteSpace(current) || current == "qwen3:8b")
                EditorPrefs.SetString(ModelPref, LightModel);
        }

        [MenuItem("Tools/Primate Panic AI - Use Qwen3 4B (Low Memory)")]
        private static void UseLightModel()
        {
            EditorPrefs.SetString(ModelPref, LightModel);
            Debug.Log("Primate Panic AI Smart Brain model set to qwen3:4b. Reopen the Smart Brain window if it is already open.");
        }
    }
}
