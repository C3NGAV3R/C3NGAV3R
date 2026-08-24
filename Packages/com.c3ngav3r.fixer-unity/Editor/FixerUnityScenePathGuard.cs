#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace C3NGAV3R.FixerUnity
{
    /// <summary>
    /// Central scene-path validation used by FIXER UNITY repairs.
    /// Prevents extension-only paths such as ".unity" from reaching SaveScene.
    /// </summary>
    [InitializeOnLoad]
    public static class FixerUnityScenePathGuard
    {
        static FixerUnityScenePathGuard()
        {
            EditorApplication.delayCall += ValidateActiveScene;
        }

        public static string NormalizeSceneAssetPath(string rawPath, string fallbackName = "FixerScene")
        {
            string path = (rawPath ?? string.Empty).Trim().Replace('\\', '/');

            // Models sometimes return only an extension: ".unity".
            // That is never a valid Unity asset path.
            if (string.Equals(path, ".unity", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(path, "unity", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(path))
            {
                path = fallbackName;
            }

            if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                path = "Assets/Scenes/" + path.TrimStart('/');

            string file = Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(file) || file == ".unity")
                file = fallbackName;

            if (!file.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                file += ".unity";

            string directory = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(directory) || directory == ".")
                directory = "Assets/Scenes";

            if (!directory.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                directory = "Assets/Scenes";

            return directory.TrimEnd('/') + "/" + file;
        }

        public static bool TrySaveScene(Scene scene, string requestedPath, out string savedPath, out string error)
        {
            savedPath = NormalizeSceneAssetPath(requestedPath, string.IsNullOrWhiteSpace(scene.name) ? "FixerScene" : scene.name);
            error = null;

            if (!savedPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) ||
                savedPath.Equals(".unity", StringComparison.OrdinalIgnoreCase))
            {
                error = "Invalid scene asset path: " + savedPath;
                return false;
            }

            try
            {
                string full = Path.GetFullPath(Path.Combine(
                    Directory.GetParent(Application.dataPath).FullName,
                    savedPath.Replace('/', Path.DirectorySeparatorChar)));

                Directory.CreateDirectory(Path.GetDirectoryName(full));
                EditorSceneManager.SaveScene(scene, savedPath);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static void ValidateActiveScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid()) return;

            string path = scene.path;
            if (string.Equals(path, ".unity", StringComparison.OrdinalIgnoreCase))
                Debug.LogWarning("[FIXER UNITY] Detected invalid active scene path '.unity'. Future scene saves will be normalized.");
        }
    }
}
#endif
