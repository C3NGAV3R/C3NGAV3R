using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace C3NGAV3R.FixerUnity
{
    [InitializeOnLoad]
    internal static class FixerUnityRoomVisibilityRepair
    {
        private const string MaterialFolder = "Assets/FixerUnityGenerated/Materials";
        private static readonly string AutoRepairKey = "FixerUnity.StartRoomRepair.1.0.3." + Application.dataPath.GetHashCode();

        static FixerUnityRoomVisibilityRepair()
        {
            EditorApplication.delayCall += TryAutoRepairOnce;
        }

        [MenuItem("Tools/FIXER UNITY/REPAIR START ROOM + MENU")]
        private static void RepairFromMenu()
        {
            string report;
            if (RepairStartRoom(true, out report))
                EditorUtility.DisplayDialog("FIXER UNITY v1.0.3", "Start room/menu repaired.\n\n" + report, "OK");
            else
                EditorUtility.DisplayDialog("FIXER UNITY v1.0.3", report, "OK");
        }

        [MenuItem("Tools/FIXER UNITY/REPAIR INVISIBLE MENU ROOM")]
        private static void LegacyRepairFromMenu()
        {
            RepairFromMenu();
        }

        private static void TryAutoRepairOnce()
        {
            if (EditorPrefs.GetBool(AutoRepairKey, false))
                return;

            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !string.Equals(scene.name, "SampleScene", StringComparison.OrdinalIgnoreCase))
                return;

            if (FindInActiveScene("MenuRoom") == null)
                return;

            string ignored;
            if (RepairStartRoom(false, out ignored))
                EditorPrefs.SetBool(AutoRepairKey, true);
        }

        private static bool RepairStartRoom(bool force, out string report)
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                report = "No valid active scene. Open SampleScene first.";
                return false;
            }

            if (!string.Equals(scene.name, "SampleScene", StringComparison.OrdinalIgnoreCase))
            {
                report = "Open SampleScene first. This repair never creates or switches scenes.";
                return false;
            }

            int removedBadObjects = CleanupBadAiObjects();

            GameObject fixerCanvas = FindInActiveScene("FixerCanvas");
            if (fixerCanvas != null)
                fixerCanvas.SetActive(false);

            GameObject room = FindInActiveScene("MenuRoom");
            if (room == null)
            {
                room = new GameObject("MenuRoom");
                Undo.RegisterCreatedObjectUndo(room, "FIXER UNITY create MenuRoom");
            }
            room.SetActive(true);
            room.transform.localScale = Vector3.one;

            EnsureFolder("Assets/FixerUnityGenerated");
            EnsureFolder(MaterialFolder);

            Material floorMat = GetMaterial("Menu_Floor", new Color(0.035f, 0.055f, 0.095f, 1f));
            Material backMat = GetMaterial("Menu_BackWall", new Color(0.20f, 0.07f, 0.35f, 1f));
            Material leftMat = GetMaterial("Menu_LeftWall", new Color(0.04f, 0.30f, 0.38f, 1f));
            Material rightMat = GetMaterial("Menu_RightWall", new Color(0.28f, 0.055f, 0.12f, 1f));
            Material ceilingMat = GetMaterial("Menu_Ceiling", new Color(0.045f, 0.045f, 0.075f, 1f));
            Material cyan = GetMaterial("Menu_Cyan", new Color(0.10f, 0.90f, 1.00f, 1f));
            Material purple = GetMaterial("Menu_Purple", new Color(0.62f, 0.22f, 1.00f, 1f));
            Material red = GetMaterial("Menu_Red", new Color(1.00f, 0.16f, 0.24f, 1f));
            Material orange = GetMaterial("Menu_Orange", new Color(1.00f, 0.45f, 0.08f, 1f));
            Material green = GetMaterial("Menu_Green", new Color(0.16f, 1.00f, 0.42f, 1f));
            Material blue = GetMaterial("Menu_Blue", new Color(0.12f, 0.35f, 1.00f, 1f));

            ReplaceWithPrimitive(room.transform, "Floor", PrimitiveType.Cube, new Vector3(0f, -0.10f, 0f), new Vector3(10f, 0.20f, 10f), floorMat, true);
            ReplaceWithPrimitive(room.transform, "BackWall", PrimitiveType.Cube, new Vector3(0f, 2.50f, 5f), new Vector3(10f, 5f, 0.20f), backMat, true);
            ReplaceWithPrimitive(room.transform, "LeftWall", PrimitiveType.Cube, new Vector3(-5f, 2.50f, 0f), new Vector3(0.20f, 5f, 10f), leftMat, true);
            ReplaceWithPrimitive(room.transform, "RightWall", PrimitiveType.Cube, new Vector3(5f, 2.50f, 0f), new Vector3(0.20f, 5f, 10f), rightMat, true);
            ReplaceWithPrimitive(room.transform, "Ceiling", PrimitiveType.Cube, new Vector3(0f, 5.0f, 0f), new Vector3(10f, 0.20f, 10f), ceilingMat, true);

            ReplaceWithPrimitive(room.transform, "Decoration01", PrimitiveType.Sphere, new Vector3(-3.5f, 1.0f, 4.55f), new Vector3(0.65f, 0.65f, 0.65f), cyan, true);
            ReplaceWithPrimitive(room.transform, "Decoration02", PrimitiveType.Cube, new Vector3(3.4f, 1.0f, 4.55f), new Vector3(0.75f, 0.75f, 0.40f), orange, true);
            ReplaceWithPrimitive(room.transform, "Decoration03", PrimitiveType.Sphere, new Vector3(-4.45f, 3.6f, 1.4f), new Vector3(0.50f, 0.50f, 0.50f), green, true);
            ReplaceWithPrimitive(room.transform, "Decoration04", PrimitiveType.Cube, new Vector3(4.45f, 3.5f, -1.3f), new Vector3(0.50f, 1.0f, 0.50f), blue, true);
            ReplaceWithPrimitive(room.transform, "Decoration05", PrimitiveType.Sphere, new Vector3(0f, 4.35f, 4.45f), new Vector3(0.50f, 0.50f, 0.50f), red, true);
            ReplaceWithPrimitive(room.transform, "Decoration06", PrimitiveType.Cube, new Vector3(-3.7f, 0.45f, -3.9f), new Vector3(0.45f, 0.8f, 0.45f), purple, true);

            GameObject menuSpawn = EnsureEmptyChild(room.transform, "MenuSpawnPoint");
            menuSpawn.transform.localPosition = new Vector3(0f, 1.10f, -3.0f);
            menuSpawn.transform.localEulerAngles = Vector3.zero;
            menuSpawn.transform.localScale = Vector3.one;

            GameObject loadingCanvasObject = FindInActiveScene("LoadingCanvas");
            if (loadingCanvasObject != null)
            {
                loadingCanvasObject.SetActive(true);
                Canvas canvas = loadingCanvasObject.GetComponent<Canvas>();
                RectTransform rect = loadingCanvasObject.transform as RectTransform;

                if (canvas != null)
                {
                    canvas.enabled = true;
                    canvas.renderMode = RenderMode.WorldSpace;
                    canvas.sortingOrder = 100;
                }

                loadingCanvasObject.transform.SetParent(room.transform, false);
                loadingCanvasObject.transform.localPosition = new Vector3(0f, 2.15f, 4.35f);
                loadingCanvasObject.transform.localEulerAngles = new Vector3(0f, 180f, 0f);

                if (rect != null)
                {
                    rect.sizeDelta = new Vector2(1200f, 700f);
                    rect.localScale = new Vector3(0.003f, 0.003f, 0.003f);
                }
                else
                {
                    loadingCanvasObject.transform.localScale = Vector3.one;
                    Debug.LogWarning("FIXER UNITY: LoadingCanvas has no RectTransform. Position was repaired, but UI size could not be set safely.");
                }
            }

            GameObject loadingScreen = FindInActiveScene("LoadingScreen");
            if (loadingScreen != null)
                loadingScreen.SetActive(true);

            GameObject mainController = FindInActiveScene("MainMenuController");
            if (mainController != null)
                mainController.SetActive(true);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            report =
                "MenuRoom ACTIVE\n" +
                "LoadingCanvas ACTIVE + World Space\n" +
                "Real 10m room geometry rebuilt\n" +
                "MenuSpawnPoint repaired\n" +
                "FixerCanvas disabled\n" +
                "Bad AI duplicate objects removed: " + removedBadObjects;

            Debug.Log("FIXER UNITY v1.0.3: Start room + menu repair complete.\n" + report);
            return true;
        }

        private static int CleanupBadAiObjects()
        {
            string[] badNames =
            {
                "Create Floor",
                "Create BackWall",
                "Create LeftWall",
                "Create RightWall",
                "Create Ceiling",
                "Create MenuSpawnPoint",
                "Create LoadingScreen",
                "Create PlayButton",
                "Create LoadingCanvas",
                "Create MenuRoom"
            };

            int removed = 0;
            foreach (string name in badNames)
            {
                GameObject go;
                while ((go = FindInActiveScene(name)) != null)
                {
                    Undo.DestroyObjectImmediate(go);
                    removed++;
                }
            }
            return removed;
        }

        private static GameObject EnsureEmptyChild(Transform parent, string name)
        {
            GameObject existing = FindChild(parent, name);
            if (existing != null)
                return existing;

            GameObject go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "FIXER UNITY create " + name);
            go.transform.SetParent(parent, false);
            return go;
        }

        private static void ReplaceWithPrimitive(Transform parent, string name, PrimitiveType type, Vector3 localPosition, Vector3 localScale, Material material, bool force)
        {
            GameObject existing = FindChild(parent, name);
            bool needsRepair = existing == null || existing.GetComponent<Renderer>() == null || existing.GetComponent<MeshFilter>() == null;
            if (!needsRepair && !force)
                return;

            if (existing != null)
                Undo.DestroyObjectImmediate(existing);

            GameObject go = GameObject.CreatePrimitive(type);
            Undo.RegisterCreatedObjectUndo(go, "FIXER UNITY repair " + name);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = localScale;

            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer != null && material != null)
                renderer.sharedMaterial = material;

            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb != null)
                Undo.DestroyObjectImmediate(rb);

            go.SetActive(true);
            go.isStatic = true;
        }

        private static GameObject FindChild(Transform parent, string name)
        {
            if (parent == null)
                return null;

            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (string.Equals(child.name, name, StringComparison.OrdinalIgnoreCase))
                    return child.gameObject;
            }
            return null;
        }

        private static GameObject FindInActiveScene(string name)
        {
            Scene active = SceneManager.GetActiveScene();
            if (!active.IsValid())
                return null;

            foreach (GameObject root in active.GetRootGameObjects())
            {
                Transform[] all = root.GetComponentsInChildren<Transform>(true);
                foreach (Transform t in all)
                {
                    if (string.Equals(t.name, name, StringComparison.OrdinalIgnoreCase))
                        return t.gameObject;
                }
            }
            return null;
        }

        private static Material GetMaterial(string name, Color color)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
                if (shader == null) shader = Shader.Find("Unlit/Color");
                if (shader == null) throw new InvalidOperationException("No compatible shader found for FIXER UNITY menu materials.");

                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }

            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
                return;

            if (!AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);

            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
