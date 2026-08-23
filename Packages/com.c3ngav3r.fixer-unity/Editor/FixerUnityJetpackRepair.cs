using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace C3NGAV3R.FixerUnity
{
    [InitializeOnLoad]
    internal static class FixerUnityJetpackRepair
    {
        private const string PendingKey = "FixerUnity.Jetpack.Pending";
        private const string PlayerPathKey = PendingKey + ".PlayerPath";
        private const string ScriptPath = "Assets/Scripts/JetpackController.cs";
        private const string TemplatePath = "Packages/com.c3ngav3r.fixer-unity/Editor/JetpackControllerTemplate.txt";

        static FixerUnityJetpackRepair()
        {
            AssemblyReloadEvents.afterAssemblyReload += TryFinishPending;
            EditorApplication.delayCall += TryFinishPending;
        }

        [MenuItem("Tools/FIXER UNITY/BUILD OR REPAIR VR JETPACK")]
        private static void BuildOrRepair()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("FIXER UNITY", "Exit Play Mode first, then run BUILD OR REPAIR VR JETPACK again.", "OK");
                return;
            }

            Rigidbody rb = FindBestPlayerRigidbody();
            if (rb == null)
            {
                EditorUtility.DisplayDialog(
                    "FIXER UNITY",
                    "I could not safely identify an EXISTING player Rigidbody. Select your Gorilla/player root or its Rigidbody object, then run this command again. I will NOT add a second Rigidbody.",
                    "OK");
                return;
            }

            if (!WriteControllerScript())
                return;

            SessionState.SetBool(PendingKey, true);
            SessionState.SetString(PlayerPathKey, HierarchyPath(rb.transform));
            AssetDatabase.ImportAsset(ScriptPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog(
                "FIXER UNITY",
                "JetpackController.cs was repaired. Unity will compile it now. After compilation, FIXER UNITY will automatically attach and wire it to the EXISTING player Rigidbody.",
                "OK");
        }

        private static bool WriteControllerScript()
        {
            TextAsset template = AssetDatabase.LoadAssetAtPath<TextAsset>(TemplatePath);
            if (template == null || string.IsNullOrWhiteSpace(template.text))
            {
                EditorUtility.DisplayDialog(
                    "FIXER UNITY",
                    "Jetpack template is missing from the package. Reimport/update FIXER UNITY before running the repair.",
                    "OK");
                Debug.LogError("FIXER UNITY: Missing " + TemplatePath);
                return false;
            }

            try
            {
                System.IO.Directory.CreateDirectory("Assets/Scripts");
                System.IO.File.WriteAllText(ScriptPath, template.text, new System.Text.UTF8Encoding(false));
                return true;
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("FIXER UNITY", "Could not write JetpackController.cs:\n\n" + ex.Message, "OK");
                Debug.LogException(ex);
                return false;
            }
        }

        private static void TryFinishPending()
        {
            if (!SessionState.GetBool(PendingKey, false))
                return;

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            Type jetpackType = FindType("JetpackController");
            if (jetpackType == null || !typeof(Component).IsAssignableFrom(jetpackType))
                return;

            Rigidbody rb = null;
            string savedPath = SessionState.GetString(PlayerPathKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(savedPath))
            {
                GameObject saved = FindByHierarchyPath(savedPath);
                if (saved != null)
                    rb = saved.GetComponent<Rigidbody>() ?? saved.GetComponentInParent<Rigidbody>();
            }

            if (rb == null)
                rb = FindBestPlayerRigidbody();

            if (rb == null)
            {
                ClearPending();
                Debug.LogError("FIXER UNITY: Jetpack compiled, but the existing player Rigidbody could not be found.");
                return;
            }

            CleanupFailedAiObjects(rb.gameObject);

            Component controller = rb.gameObject.GetComponent(jetpackType);
            if (controller == null)
                controller = Undo.AddComponent(rb.gameObject, jetpackType);

            Transform playerRoot = FindBestPlayerRoot(rb.transform);
            Transform head = FindHead(playerRoot, rb.transform);
            AudioSource audio = EnsureAudio(rb.transform);
            ParticleSystem left = EnsureThruster(rb.transform, "LeftThruster", new Vector3(-0.22f, -0.28f, -0.15f));
            ParticleSystem right = EnsureThruster(rb.transform, "RightThruster", new Vector3(0.22f, -0.28f, -0.15f));

            SerializedObject so = new SerializedObject(controller);
            SetObject(so, "playerRigidbody", rb);
            SetObject(so, "playerRoot", playerRoot);
            SetObject(so, "headTransform", head);
            SetObject(so, "jetpackAudio", audio);
            SetObject(so, "leftThruster", left);
            SetObject(so, "rightThruster", right);
            SetBool(so, "jetpackEnabled", true);
            SetFloat(so, "upwardForce", 12f);
            SetFloat(so, "moveForce", 8f);
            SetFloat(so, "maxSpeed", 12f);
            SetFloat(so, "hoverDamping", 4f);
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(rb.gameObject);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            Selection.activeGameObject = rb.gameObject;
            ClearPending();

            Debug.Log("FIXER UNITY: VR Jetpack wired successfully to existing Rigidbody on " + rb.gameObject.name + ".");
            EditorUtility.DisplayDialog(
                "FIXER UNITY",
                "VR JETPACK READY ✅\n\nAttached to: " + rb.gameObject.name +
                "\nExisting Rigidbody reused: YES" +
                "\nRight trigger: fly up" +
                "\nLeft joystick: move" +
                "\nA: toggle hover" +
                "\nSPACE/WASD/H: Editor test" +
                "\n\nJetpackAudio has no clip yet. Add your own sound when ready.",
                "OK");
        }

        private static void ClearPending()
        {
            SessionState.SetBool(PendingKey, false);
            SessionState.EraseString(PlayerPathKey);
        }

        private static Rigidbody FindBestPlayerRigidbody()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected != null && selected.scene.IsValid())
            {
                Rigidbody selectedRb = selected.GetComponent<Rigidbody>() ??
                                       selected.GetComponentInParent<Rigidbody>() ??
                                       selected.GetComponentInChildren<Rigidbody>(true);
                if (selectedRb != null)
                    return selectedRb;
            }

            Scene active = SceneManager.GetActiveScene();
            Rigidbody best = null;
            int bestScore = int.MinValue;

            foreach (Rigidbody rb in Resources.FindObjectsOfTypeAll<Rigidbody>())
            {
                if (rb == null || !rb.gameObject.scene.IsValid() || rb.gameObject.scene != active)
                    continue;

                string path = HierarchyPath(rb.transform).ToLowerInvariant();
                int score = 0;
                if (path.Contains("gorilla")) score += 100;
                if (path.Contains("player")) score += 80;
                if (path.Contains("locomotion")) score += 60;
                if (path.Contains("xr origin") || path.Contains("xrorigin")) score += 45;
                if (path.Contains("rig")) score += 25;
                if (path.Contains("body")) score += 15;
                if (path.Contains("monster")) score -= 100;
                if (path.Contains("menu")) score -= 80;
                if (path.Contains("button")) score -= 80;
                if (path.Contains("prop")) score -= 30;

                foreach (Component component in rb.GetComponents<Component>())
                {
                    if (component == null)
                        continue;

                    string typeName = component.GetType().FullName.ToLowerInvariant();
                    if (typeName.Contains("gorillalocomotion")) score += 150;
                    if (typeName.Contains("gorillaplayer")) score += 100;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = rb;
                }
            }

            return bestScore >= 20 ? best : null;
        }

        private static Transform FindBestPlayerRoot(Transform rbTransform)
        {
            Transform best = rbTransform;
            Transform current = rbTransform;
            while (current != null)
            {
                string name = current.name.ToLowerInvariant();
                if (name.Contains("gorilla") || name.Contains("player") || name.Contains("xr origin") || name.Contains("xrorigin") || name.Contains("rig"))
                    best = current;
                current = current.parent;
            }
            return best;
        }

        private static Transform FindHead(Transform playerRoot, Transform fallback)
        {
            if (playerRoot != null)
            {
                Camera childCamera = playerRoot.GetComponentInChildren<Camera>(true);
                if (childCamera != null)
                    return childCamera.transform;
            }

            if (Camera.main != null)
                return Camera.main.transform;

            foreach (Camera camera in Resources.FindObjectsOfTypeAll<Camera>())
            {
                if (camera != null && camera.gameObject.scene.IsValid() && camera.gameObject.scene == SceneManager.GetActiveScene())
                    return camera.transform;
            }

            return fallback;
        }

        private static AudioSource EnsureAudio(Transform parent)
        {
            Transform child = parent.Find("JetpackAudio");
            GameObject go;
            if (child == null)
            {
                go = new GameObject("JetpackAudio");
                Undo.RegisterCreatedObjectUndo(go, "FIXER UNITY JetpackAudio");
                go.transform.SetParent(parent, false);
            }
            else
            {
                go = child.gameObject;
            }

            AudioSource audio = go.GetComponent<AudioSource>();
            if (audio == null)
                audio = Undo.AddComponent<AudioSource>(go);

            audio.playOnAwake = false;
            audio.loop = true;
            audio.spatialBlend = 1f;
            audio.volume = 0.6f;
            return audio;
        }

        private static ParticleSystem EnsureThruster(Transform parent, string name, Vector3 localPosition)
        {
            Transform child = parent.Find(name);
            GameObject go;
            if (child == null)
            {
                go = new GameObject(name);
                Undo.RegisterCreatedObjectUndo(go, "FIXER UNITY " + name);
                go.transform.SetParent(parent, false);
            }
            else
            {
                go = child.gameObject;
            }

            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            ParticleSystem ps = go.GetComponent<ParticleSystem>();
            if (ps == null)
                ps = Undo.AddComponent<ParticleSystem>(go);

            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = 0.22f;
            main.startSpeed = 2.2f;
            main.startSize = 0.08f;
            main.maxParticles = 40;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 22f;

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 10f;
            shape.radius = 0.03f;

            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return ps;
        }

        private static void CleanupFailedAiObjects(GameObject actualPlayer)
        {
            foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go == null || !go.scene.IsValid() || go.scene != SceneManager.GetActiveScene() || go == actualPlayer)
                    continue;

                if (!string.Equals(go.name, "JetpackController", StringComparison.OrdinalIgnoreCase))
                    continue;

                Component[] components = go.GetComponents<Component>();
                if (components.Length <= 1)
                    Undo.DestroyObjectImmediate(go);
            }
        }

        private static Type FindType(string shortName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type direct = assembly.GetType(shortName, false, true);
                if (direct != null)
                    return direct;

                try
                {
                    foreach (Type type in assembly.GetTypes())
                    {
                        if (string.Equals(type.Name, shortName, StringComparison.OrdinalIgnoreCase))
                            return type;
                    }
                }
                catch (ReflectionTypeLoadException)
                {
                }
            }

            return null;
        }

        private static void SetObject(SerializedObject so, string property, UnityEngine.Object value)
        {
            SerializedProperty p = so.FindProperty(property);
            if (p != null)
                p.objectReferenceValue = value;
        }

        private static void SetBool(SerializedObject so, string property, bool value)
        {
            SerializedProperty p = so.FindProperty(property);
            if (p != null)
                p.boolValue = value;
        }

        private static void SetFloat(SerializedObject so, string property, float value)
        {
            SerializedProperty p = so.FindProperty(property);
            if (p != null)
                p.floatValue = value;
        }

        private static GameObject FindByHierarchyPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
                {
                    if (string.Equals(HierarchyPath(transform), path, StringComparison.OrdinalIgnoreCase))
                        return transform.gameObject;
                }
            }

            return null;
        }

        private static string HierarchyPath(Transform transform)
        {
            if (transform == null)
                return string.Empty;

            string path = transform.name;
            Transform current = transform.parent;
            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }
            return path;
        }
    }
}
