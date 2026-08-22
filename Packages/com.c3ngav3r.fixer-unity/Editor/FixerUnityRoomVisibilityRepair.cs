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
        private static readonly string AutoRepairKey = "FixerUnity.RoomVisibilityRepair.1.0.2." + Application.dataPath.GetHashCode();

        static FixerUnityRoomVisibilityRepair()
        {
            EditorApplication.delayCall += TryAutoRepairOnce;
        }

        [MenuItem("Tools/FIXER UNITY/REPAIR INVISIBLE MENU ROOM")]
        private static void RepairFromMenu()
        {
            if (RepairMenuRoom(true))
                EditorUtility.DisplayDialog("FIXER UNITY", "MenuRoom repaired. The floor, walls, ceiling and decorations are now real visible Unity primitives with colliders/materials.", "OK");
            else
                EditorUtility.DisplayDialog("FIXER UNITY", "No MenuRoom was found in the active scene. Open SampleScene first, then run this again.", "OK");
        }

        private static void TryAutoRepairOnce()
        {
            if (EditorPrefs.GetBool(AutoRepairKey, false))
                return;

            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !string.Equals(scene.name, "SampleScene", StringComparison.OrdinalIgnoreCase))
                return;

            GameObject room = GameObject.Find("MenuRoom");
            if (room == null)
                return;

            if (RepairMenuRoom(false))
                EditorPrefs.SetBool(AutoRepairKey, true);
        }

        private static bool RepairMenuRoom(bool force)
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
                return false;

            GameObject room = FindInActiveScene("MenuRoom");
            if (room == null)
                return false;

            EnsureFolder("Assets/FixerUnityGenerated");
            EnsureFolder(MaterialFolder);

            Material dark = GetMaterial("Menu_Dark", new Color(0.035f, 0.04f, 0.07f, 1f));
            Material cyan = GetMaterial("Menu_Cyan", new Color(0.10f, 0.90f, 1.00f, 1f));
            Material purple = GetMaterial("Menu_Purple", new Color(0.62f, 0.22f, 1.00f, 1f));
            Material red = GetMaterial("Menu_Red", new Color(1.00f, 0.16f, 0.24f, 1f));
            Material orange = GetMaterial("Menu_Orange", new Color(1.00f, 0.45f, 0.08f, 1f));
            Material green = GetMaterial("Menu_Green", new Color(0.16f, 1.00f, 0.42f, 1f));
            Material blue = GetMaterial("Menu_Blue", new Color(0.12f, 0.35f, 1.00f, 1f));

            ReplaceWithPrimitive(room.transform, "Floor", PrimitiveType.Cube, new Vector3(0f, 0f, 0f), new Vector3(8f, 0.25f, 8f), dark, force);
            ReplaceWithPrimitive(room.transform, "BackWall", PrimitiveType.Cube, new Vector3(0f, 2.55f, 4f), new Vector3(8f, 5f, 0.25f), purple, force);
            ReplaceWithPrimitive(room.transform, "LeftWall", PrimitiveType.Cube, new Vector3(-4f, 2.55f, 0f), new Vector3(0.25f, 5f, 8f), cyan, force);
            ReplaceWithPrimitive(room.transform, "RightWall", PrimitiveType.Cube, new Vector3(4f, 2.55f, 0f), new Vector3(0.25f, 5f, 8f), red, force);
            ReplaceWithPrimitive(room.transform, "Ceiling", PrimitiveType.Cube, new Vector3(0f, 5.05f, 0f), new Vector3(8f, 0.20f, 8f), dark, force);

            ReplaceWithPrimitive(room.transform, "Decoration01", PrimitiveType.Sphere, new Vector3(-2.8f, 1.0f, 3.55f), new Vector3(0.55f, 0.55f, 0.55f), cyan, force);
            ReplaceWithPrimitive(room.transform, "Decoration02", PrimitiveType.Cube, new Vector3(2.7f, 1.15f, 3.55f), new Vector3(0.7f, 0.7f, 0.35f), orange, force);
            ReplaceWithPrimitive(room.transform, "Decoration03", PrimitiveType.Sphere, new Vector3(-3.55f, 3.4f, 0.8f), new Vector3(0.45f, 0.45f, 0.45f), green, force);
            ReplaceWithPrimitive(room.transform, "Decoration04", PrimitiveType.Cube, new Vector3(3.55f, 3.3f, -0.8f), new Vector3(0.45f, 0.9f, 0.45f), blue, force);
            ReplaceWithPrimitive(room.transform, "Decoration05", PrimitiveType.Sphere, new Vector3(0f, 4.2f, 3.5f), new Vector3(0.45f, 0.45f, 0.45f), red, force);

            GameObject spawn = FindChild(room.transform, "MenuSpawnPoint");
            if (spawn == null)
            {
                spawn = new GameObject("MenuSpawnPoint");
                Undo.RegisterCreatedObjectUndo(spawn, "FIXER UNITY create MenuSpawnPoint");
                spawn.transform.SetParent(room.transform, false);
                spawn.transform.localPosition = new Vector3(0f, 1.2f, -2.2f);
                spawn.transform.localEulerAngles = new Vector3(0f, 0f, 0f);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("FIXER UNITY: MenuRoom visibility repair complete.");
            return true;
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
