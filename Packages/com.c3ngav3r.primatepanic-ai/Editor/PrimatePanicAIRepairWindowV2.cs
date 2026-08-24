using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace C3NGAV3R.PrimatePanicAI
{
    public sealed class PrimatePanicAIRepairWindowV2 : EditorWindow
    {
        private string request = "";
        private string status = "READY — scene-aware repair engine online.";
        private Vector2 scroll;

        [MenuItem("Tools/Primate Panic AI - FIXED / Repair Agent")]
        public static void Open() => GetWindow<PrimatePanicAIRepairWindowV2>("Primate Panic AI FIXED");

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Primate Panic AI — FIXED REPAIR ENGINE v2", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Inspect first. Resolve real hierarchy paths. Modify existing objects. Verify after changes. Never create replacement UI when the requested object already exists.", MessageType.Info);
            if (GUILayout.Button("SCAN CURRENT SCENE", GUILayout.Height(28))) status = Inventory();
            if (GUILayout.Button("VERIFY BUTTONS", GUILayout.Height(28))) status = VerifyButtons();
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Request", EditorStyles.boldLabel);
            request = EditorGUILayout.TextArea(request, GUILayout.MinHeight(90));
            if (GUILayout.Button("RUN SAFE REPAIR", GUILayout.Height(42))) RunRepair();
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Result", EditorStyles.boldLabel);
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MinHeight(260));
            EditorGUILayout.TextArea(status, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private void RunRepair()
        {
            string q = (request ?? "").ToLowerInvariant();
            if (q.Contains("kick hammer") && q.Contains("jetpack")) { RepairBoth(); return; }
            if (q.Contains("kick hammer") || q.Contains("kickhammer")) { RepairOne("Kick Hammer", new[] { "kick hammer", "kickhammer", "hammer" }, true); return; }
            if (q.Contains("jetpack") || q.Contains("jet pack")) { RepairOne("Jetpack", new[] { "jetpack", "jet pack", "boost" }, false); return; }
            status = "No safe repair matched. Scan first; this engine will not guess an unrelated object.";
        }

        private void RepairBoth()
        {
            GameObject hammer = FindObject(new[] { "kick hammer", "kickhammer", "hammer" });
            GameObject jetpack = FindObject(new[] { "jetpack", "jet pack", "boost" });
            Button hb = FindButton(new[] { "kick hammer", "kickhammer", "hammer" });
            Button jb = FindButton(new[] { "jetpack", "jet pack" });
            if (hammer == null || jetpack == null || hb == null || jb == null)
            {
                status = "REPAIR BLOCKED — target resolution failed.\n\n" + Missing(hammer, jetpack, hb, jb);
                return;
            }
            PPAIItemToggleController c = GetOrCreateController();
            SetField(c, "kickHammer", hammer);
            SetField(c, "jetpack", jetpack);
            ReplacePersistent(hb, c.ToggleKickHammer);
            ReplacePersistent(jb, c.ToggleJetpack);
            Save();
            status = "REPAIR COMPLETE\n\n" +
                "Kick Hammer: " + PathOf(hammer) + "\n" +
                "Kick Hammer Button: " + PathOf(hb.gameObject) + "\n" +
                "Jetpack: " + PathOf(jetpack) + "\n" +
                "Jetpack Button: " + PathOf(jb.gameObject) + "\n\n" +
                "Existing buttons were wired with persistent UnityEvent listeners. No replacement Canvas/Button was created.";
        }

        private void RepairOne(string label, string[] keys, bool hammer)
        {
            GameObject item = FindObject(keys);
            Button button = FindButton(keys);
            if (item == null || button == null)
            {
                status = "REPAIR BLOCKED — I will not guess.\n\n" + Inventory();
                return;
            }
            PPAIItemToggleController c = GetOrCreateController();
            SetField(c, hammer ? "kickHammer" : "jetpack", item);
            if (hammer) ReplacePersistent(button, c.ToggleKickHammer); else ReplacePersistent(button, c.ToggleJetpack);
            Save();
            status = "REPAIRED " + label + "\nObject: " + PathOf(item) + "\nButton: " + PathOf(button.gameObject) + "\nScene saved.";
        }

        private PPAIItemToggleController GetOrCreateController()
        {
            PPAIItemToggleController existing = Resources.FindObjectsOfTypeAll<PPAIItemToggleController>()
                .FirstOrDefault(x => x != null && x.gameObject.scene == SceneManager.GetActiveScene());
            if (existing != null) return existing;
            GameObject go = new GameObject("PrimatePanic_AI_ItemToggleController");
            Undo.RegisterCreatedObjectUndo(go, "Create Primate Panic AI toggle controller");
            return go.AddComponent<PPAIItemToggleController>();
        }

        private static void SetField(PPAIItemToggleController c, string name, GameObject value)
        {
            FieldInfo f = typeof(PPAIItemToggleController).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (f == null) throw new InvalidOperationException("Controller field missing: " + name);
            f.SetValue(c, value);
            EditorUtility.SetDirty(c);
        }

        private static void ReplacePersistent(Button b, UnityEngine.Events.UnityAction action)
        {
            for (int i = b.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
                UnityEventTools.RemovePersistentListener(b.onClick, i);
            UnityEventTools.AddPersistentListener(b.onClick, action);
            EditorUtility.SetDirty(b);
        }

        private static GameObject FindObject(string[] keys)
        {
            Scene scene = SceneManager.GetActiveScene();
            return Resources.FindObjectsOfTypeAll<GameObject>()
                .Where(x => x != null && x.scene == scene)
                .Select(x => new { go = x, score = Score(x, keys, false) })
                .Where(x => x.score > 0)
                .OrderByDescending(x => x.score)
                .Select(x => x.go)
                .FirstOrDefault();
        }

        private static Button FindButton(string[] keys)
        {
            Scene scene = SceneManager.GetActiveScene();
            return Resources.FindObjectsOfTypeAll<Button>()
                .Where(x => x != null && x.gameObject.scene == scene)
                .Select(x => new { b = x, score = Score(x.gameObject, keys, true) })
                .Where(x => x.score > 0)
                .OrderByDescending(x => x.score)
                .Select(x => x.b)
                .FirstOrDefault();
        }

        private static int Score(GameObject go, string[] keys, bool button)
        {
            string n = go.name.ToLowerInvariant();
            string p = PathOf(go).ToLowerInvariant();
            int s = button ? 25 : 0;
            foreach (string raw in keys)
            {
                string k = raw.ToLowerInvariant();
                if (n == k) s += 100;
                else if (n.Contains(k)) s += 60;
                if (p.Contains(k)) s += 20;
            }
            if (button && !go.activeInHierarchy) s -= 15;
            return s;
        }

        private static string VerifyButtons()
        {
            Scene scene = SceneManager.GetActiveScene();
            List<Button> buttons = Resources.FindObjectsOfTypeAll<Button>().Where(x => x != null && x.gameObject.scene == scene).ToList();
            StringBuilder sb = new StringBuilder("BUTTON VERIFICATION\nScene: " + scene.name + "\n");
            foreach (Button b in buttons)
                sb.AppendLine("- " + PathOf(b.gameObject) + " | active=" + b.gameObject.activeSelf + " interactable=" + b.interactable + " persistentOnClick=" + b.onClick.GetPersistentEventCount());
            sb.AppendLine("Buttons found: " + buttons.Count);
            return sb.ToString();
        }

        private static string Inventory()
        {
            Scene scene = SceneManager.GetActiveScene();
            StringBuilder sb = new StringBuilder("SCENE INVENTORY\nScene: " + scene.name + "\nPath: " + scene.path + "\n");
            foreach (GameObject root in scene.GetRootGameObjects()) Dump(root.transform, sb, 0);
            return sb.ToString();
        }

        private static void Dump(Transform t, StringBuilder sb, int depth)
        {
            sb.Append(new string(' ', depth * 2)).Append("- ").Append(t.name).Append(" [");
            sb.Append(string.Join(", ", t.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().Name).ToArray()));
            sb.Append("] active=").AppendLine(t.gameObject.activeSelf.ToString());
            foreach (Transform child in t) Dump(child, sb, depth + 1);
        }

        private static string Missing(GameObject h, GameObject j, Button hb, Button jb)
        {
            return "Kick Hammer object: " + (h == null ? "NOT FOUND" : PathOf(h)) + "\n" +
                   "Jetpack object: " + (j == null ? "NOT FOUND" : PathOf(j)) + "\n" +
                   "Kick Hammer button: " + (hb == null ? "NOT FOUND" : PathOf(hb.gameObject)) + "\n" +
                   "Jetpack button: " + (jb == null ? "NOT FOUND" : PathOf(jb.gameObject)) + "\n\n" + Inventory();
        }

        private static void Save()
        {
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
        }

        private static string PathOf(GameObject go) => go == null ? "<null>" : PathOf(go.transform);
        private static string PathOf(Transform t)
        {
            List<string> p = new List<string>();
            while (t != null) { p.Add(t.name); t = t.parent; }
            p.Reverse();
            return string.Join("/", p.ToArray());
        }
    }

    public sealed class PPAIItemToggleController : MonoBehaviour
    {
        [SerializeField] private GameObject kickHammer;
        [SerializeField] private GameObject jetpack;
        public void ToggleKickHammer() => Toggle(kickHammer, "Kick Hammer");
        public void ToggleJetpack() => Toggle(jetpack, "Jetpack");
        private static void Toggle(GameObject target, string label)
        {
            if (target == null) { Debug.LogError("[Primate Panic AI] " + label + " reference is missing."); return; }
            target.SetActive(!target.activeSelf);
            Debug.Log("[Primate Panic AI] " + label + " -> " + (target.activeSelf ? "ENABLED" : "DISABLED"));
        }
    }
}
