using System;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace C3NGAV3R.FixerUnity
{
    [InitializeOnLoad]
    internal static class FixerUnityJetpackRepairFixed
    {
        private const string PendingKey = "FixerUnity.Jetpack.Fixed.Pending";
        private const string PlayerPathKey = PendingKey + ".PlayerPath";
        private const string ScriptPath = "Assets/Scripts/JetpackController.cs";
        private const string ControllerBase64 = "dXNpbmcgVW5pdHlFbmdpbmU7CnVzaW5nIFVuaXR5RW5naW5lLlhSOwoKcHVibGljIGNsYXNzIEpldHBhY2tDb250cm9sbGVyIDogTW9ub0JlaGF2aW91cgp7CiAgICBwdWJsaWMgUmlnaWRib2R5IHBsYXllclJpZ2lkYm9keTsKICAgIHB1YmxpYyBUcmFuc2Zvcm0gcGxheWVyUm9vdDsKICAgIHB1YmxpYyBUcmFuc2Zvcm0gaGVhZFRyYW5zZm9ybTsKICAgIHB1YmxpYyBib29sIGpldHBhY2tFbmFibGVkID0gdHJ1ZTsKICAgIHB1YmxpYyBmbG9hdCB1cHdhcmRGb3JjZSA9IDEyZjsKICAgIHB1YmxpYyBmbG9hdCBtb3ZlRm9yY2UgPSA4ZjsKICAgIHB1YmxpYyBmbG9hdCBtYXhTcGVlZCA9IDEyZjsKICAgIHB1YmxpYyBmbG9hdCBob3ZlckRhbXBpbmcgPSA0ZjsKICAgIHB1YmxpYyBBdWRpb1NvdXJjZSBqZXRwYWNrQXVkaW87CiAgICBwdWJsaWMgUGFydGljbGVTeXN0ZW0gbGVmdFRocnVzdGVyOwogICAgcHVibGljIFBhcnRpY2xlU3lzdGVtIHJpZ2h0VGhydXN0ZXI7CiAgICBwdWJsaWMgYm9vbCBob3ZlckVuYWJsZWQ7CgogICAgSW5wdXREZXZpY2UgbGVmdENvbnRyb2xsZXIsIHJpZ2h0Q29udHJvbGxlcjsKICAgIGJvb2wgbGFzdFByaW1hcnksIHVwSGVsZCwgYWN0aXZlOwogICAgVmVjdG9yMiBtb3ZlSW5wdXQ7CgogICAgdm9pZCBBd2FrZSgpCiAgICB7CiAgICAgICAgaWYgKHBsYXllclJpZ2lkYm9keSA9PSBudWxsKSBwbGF5ZXJSaWdpZGJvZHkgPSBHZXRTb21wb25lbnQ8UmlnaWRpZCBCb2R5PigpOwogICAgICAgIGlmIChwbGF5ZXJSb290ID09IG51bGwpIHBsYXllclJvb3QgPSB0cmFuc2Zvcm07CiAgICAgICAgaWYgKGhlYWRUcmFuc2Zvcm0gPT0gbnVsbCAmJiBDYW1lcmEubWFpbiAhPSBudWxsKSBoZWFkVHJhbnNmb3JtID0gQ2FtZXJhLm1haW4udHJhbnNmb3JtOwogICAgICAgIEFjcXVpcmVDb250cm9sbGVycygpOwogICAgICAgIFN0b3BFZmZlY3RzKCk7CiAgICB9CgogICAgdm9pZCBPbkVuYWJsZSgpIHsgQWNxdWlyZUNvbnRyb2xsZXJzKCk7IH0KCiAgICB2b2lkIFVwZGF0ZSgpCiAgICB7CiAgICAgICAgaWYgKCFqZXRwYWNrRW5hYmxlZCkKICAgICAgICB7CiAgICAgICAgICAgIHVwSGVsZCA9IGZhbHNlOyBtb3ZlSW5wdXQgPSBWZWN0b3IyLnplcm87IGhvdmVyRW5hYmxlZCA9IGZhbHNlOwogICAgICAgICAgICBVcGRhdGVFZmZlY3RzKGZhbHNlKTsgcmV0dXJuOwogICAgICAgIH0KCiAgICAgICAgaWYgKCFsZWZ0Q29udHJvbGxlci5pc1ZhbGlkIHx8ICFyaWdodENvbnRyb2xsZXIuaXNWYWxpZCkgQWNxdWlyZUNvbnRyb2xsZXJzKCk7CgogICAgICAgIGJvb2wgdHJpZ2dlciA9IGZhbHNlLCBwcmltYXJ5ID0gZmFsc2U7CiAgICAgICAgVmVjdG9yMiBzdGljayA9IFZlY3RvcjIuemVybyA7CiAgICAgICAgaWYgKHJpZ2h0Q29udHJvbGxlci5pc1ZhbGlkKQogICAgICAgIHsKICAgICAgICAgICAgcmlnaHRDb250cm9sbGVyLlRyeUdldEZlYXR1cmVWYWx1ZShDb21tb25VdGlsaXMudHJpZ2dlckJ1dHRvbiwgb3V0IHRyaWdnZXIpOwogICAgICAgICAgICByaWdodENvbnRyb2xsZXIuVHJ5R2V0RmVhdHVyZVZhbHVlKENvbW1vblV0aWxzLnByaW1hcnlCdXR0b24sIG91dCBwcmltYXJ5KTsKICAgICAgICB9CiAgICAgICAgaWYgKGxlZnRDb250cm9sbGVyLmlzVmFsaWQpIGxlZnRDb250cm9sbGVyLlRyeUdldEZlYXR1cmVWYWx1ZShDb21tb25VdGlsaXMucHJpbWFyeTJEQXhpcywgb3V0IHN0aWNrKTsKCiAgICAgICAgaWYgKChwcmltYXJ5ICYmICFsYXN0UHJpbWFyeSkgfHwgSW5wdXQuR2V0S2V5RG93bihLZXlDb2RlLkgpKSBob3ZlckVuYWJsZWQgPSAhaG92ZXJFbmFibGVkOwogICAgICAgIGxhc3RQcmltYXJ5ID0gcHJpbWFyeTsKICAgICAgICBib29sIGtleWJvYXJkVXAgPSBJbnB1dC5HZXRLZXkoS2V5Q29kZS5TcGFjZSk7CiAgICAgICAgVmVjdG9yMiBrZXlib2FyZE1vdmUgPSBuZXcgVmVjdG9yMigKICAgICAgICAgICAgKElucHV0LkdldEtleShLZXlDb2RlLkQpID8gMWYgOiAwZikgLSAoSW5wdXQuR2V0S2V5KEtleUNvZGUuQSkgPyAxZiA6IDBmKSwKICAgICAgICAgICAgKElucHV0LkdldEtleShLZXlDb2RlLlcpID8gMWYgOiAwZikgLSAoSW5wdXQuR2V0S2V5KEtleUNvZGUuUykgPyAxZiA6IDBmKSk7CiAgICAgICAgdXBIZWxkID0gdHJpZ2dlciB8fCBrZXlib2FyZFVwOwogICAgICAgIG1vdmVJbnB1dCA9IFZlY3RvcjIuQ2xhbXBNYWduaXR1ZGUoc3RpY2sgKyBrZXlib2FyZE1vdmUsIDEuMGYpOwogICAgICAgIGFjdGl2ZSA9IHVwSGVsZCB8fCBtb3ZlSW5wdXQuc3F1YXJlTWFnbml0dWRlID4gMC4wMDI1ZiB8fCBob3ZlckVuYWJsZWQ7CiAgICAgICAgVXBkYXRlRWZmZWN0cyhhY3RpdmUpOwogICAgfQoKICAgIHZvaWQgRml4ZWRVcGRhdGUoKQogICAgIHsKICAgICAgICBpZiAoIWhldHBhY2tFbmFibGVkIHx8IHBsYXllclJpZ2lkYm9keSA9PSBudWxsKSByZXR1cm47CgogICAgICAgIGlmICh1cEhlbGQpIHBsYXllclJpZ2lkYm9keS5BZGRGb3JjZShWZWN0b3IudXAgKiB1cHdhcmRGb3JjZSwgRm9yY2VNb2RlLkFjY2VsZXJhdGlvbik7CgogICAgICAgIGlmIChtb3ZlSW5wdXQuc3F1YXJlTWFnbml0dWRlID4gMC4wMDI1ZikKICAgICAgICB7CiAgICAgICAgICAgIFRyYW5zZm9ybSBiYXNpcyA9IGhlYWRUcmFuc2Zvcm0gIT0gbnVsbCA/IGhlYWRUcmFuc2Zvcm0gOiBwbGF5ZXJSb290OwogICAgICAgICAgICBWZWN0b3IgZm9yd2FyZCA9IGJhc2lzICE9IG51bGwgPyBiYXNpcy5mb3J3YXJkIDogdHJhbnNmb3JtLmZvcndhcmQ7CiAgICAgICAgICAgIFZlY3RvciByaWdodCA9IGJhc2lzICE9IG51bGwgPyBiYXNpcy5yaWdodCA6IHRyYW5zZm9ybS5yaWdodDsKICAgICAgICAgICAgZm9yd2FyZC55ID0gMDtmIHJpZ2h0LnkgPSAwOwogICAgICAgICAgICBpZiAoZm9yd2FyZC5zcXVhcmVNYWd0aXR1ZGUgPiAwLjAwMSBmKSBmb3J3YXJkLk5vcm1hbGl6ZSgpOwogICAgICAgICAgICBpZiAocmlnaHQuc3F1YXJlTWFnbml0dWRlID4gMC4wMDAxZiKSBpZ2h0Lk5vcm1hbGl6ZSgpOwogICAgICAgICAgICBWZWN0b3IgZGlyZWN0aW9uID0gZm9yd2FyZCAqIG1vdmVJbnB1dC55ICsgcmlnaHQgKiBtb3ZlSW5wdXQueDsKICAgICAgICAgICAgaWYgKGRpcmVjdGlvbi5zcXVhcmVNYWd0aXR1ZGUgPiAwLjAwMDFmKSBwbGF5ZXJSaWdpZGJvZHkuQWRkRm9yY2UoZGlyZWN0aW9uLm5vcm1hbGl6ZWQgKiBtb3ZlRm9yY2UgKiBtb3ZlSW5wdXQubWFnbml0dWRlLCBGb3JjZU1vZGUuQWNjZWxlcmF0aW9uKTsKICAgICAgICB9CgogICAgICAgIGlmIChob3ZlckVuYWJsZWQpIHBsYXllclJpZ2lkYm9keS5BZGRGb3JjZShWZWN0b3IuZG93biAqIHBsYXllclJpZ2lkYm9keS52ZWxvY2l0eS55ICogaG92ZXJEYW1waW5nLCBGb3JjZU1vZGUuQWNjZWxlcmF0aW9uKTsKICAgICAgICBpZiAocGxheWVyUmlnaWRib2R5LnZlbG9jaXR5Lm1hZ25pdHVkZSA+IG1heFNwZWVkKSBwbGF5ZXJSaWdpZGJvZHkudmVsb2NpdHkgPSBwbGF5ZXJSaWdpZGJvZHkudmVsb2NpdHkubm9ybWFsaXplZCAqIG1heFNwZWVkOwogICAgfQoKICAgIHB1YmxpYyB2b2lkIEVuYWJsZUpldHBhY2soKSB7IGpldHBhY2tFbmFibGVkID0gdHJ1ZTsgfQogICAgcHVibGljIHZvaWQgRGlzYWJsZUpldHBhY2soKSB7IGpldHBhY2tFbmFibGVkID0gZmFsc2U7IGhvdmVyRW5hYmxlZCA9IGZhbHNlOyB1cEhlbGQgPSBmYWxzZTsgbW92ZUlucHV0ID0gVmVjdG9yMi56ZXJvOyBVcGRhdGVFZmZlY3RzKGZhbHNlKTsgfQogICAgcHVibGljIHZvaWQgVG9nZ2xlSmV0cGFjaygpIHsgaWYgKGpldHBhY2tFbmFibGVkKSBEaXNhYmxlSmV0cGFjaygpOyBlbHNlIEVuYWJsZUpldHBhY2soKTsgfQoKICAgIHZvaWQgQWNxdWlyZUNvbnRyb2xsZXJzKCkgeyBsZWZ0Q29udHJvbGxlciA9IElucHV0RGV2aWNlcy5HZXREZXZpY2VBdFhSTm9kZShYUk5vZGUuTGVmdEhhbmQpOyByaWdodENvbnRyb2xsZXIgPSBJbnB1dERldmljZXMuR2V0RGV2aWNlQXRYUk5vZGUoWFJOb2RlLlJpZ2h0SGFuZCk7IH0KICAgIHZvaWQgVXBkYXRlRWZmZWN0cyhib29sIHNob3VsZFBsYXkpIHsgaWYgKHNob3VsZFBsYXkpIHsgRW5zdXJlUGxheWluZygpOyB9IGVsc2UgU3RvcEVmZmVjdHMoKTsgfQogICAgdm9pZCBFbnN1cmVQbGF5aW5nKCkgeyBpZiAoaGV0cGFja0F1ZGlvICE9IG51bGwgJiYgIWhldHBhY2tBdWRpby5pc1BsYXlpbmcpIGhldHBhY2tBdWRpby5QbGF5KCk7IGlmIChsZWZ0VGhydXN0ZXIgIT0gbnVsbCAmJiAhbGVmdFRocnVzdGVyLmlzUGxheWluZykgbGVmdFRocnVzdGVyLlBsYXkoKTsgIGlmIChyaWdodFRocnVzdGVyICE9IG51bGwgJiYgIXJpZ2h0VGhydXN0ZXIuaXNQbGF5aW5nKSByaWdodFRocnVzdGVyLlBsYXkoKTsgfQogICAgdm9pZCBTdG9wRWZmZWN0cygpIHsgaWYgKGpldHBhY2tBdWRpbyAhPSBudWxsKSBoZXRwYWNrQXVkaW8uU3RvcCgpOyBpZiAobGVmdFRocnVzdGVyICE9IG51bGwpIGxlZnRUaHJ1c3Rlci5TdG9wKHRydWUsIFBhcnRpY2xlU3lzdGVtU3RvcEJlaGF2aW9yLlN0b3BFbWl0dGluZ0FuZENsZWFyKTsgIGlmIChyaWdodFRocnVzdGVyICE9IG51bGwpIHJpZ2h0VGhydXN0ZXIuU3RvcCh0cnVlLCBQYXJ0aWNsZVN5c3RlbVN0b3BCZWhhdmlvci5TdG9wRW1pdHRpbmdBbmRDbGVhcik7IH0KfQo=";

        static FixerUnityJetpackRepairFixed()
        {
            AssemblyReloadEvents.afterAssemblyReload += TryFinish;
            EditorApplication.delayCall += TryFinish;
        }

        [MenuItem("Tools/FIXER UNITY/BUILD OR REPAIR VR JETPACK (FIXED)")]
        private static void Build()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("FIXER UNITY", "Exit Play Mode first.", "OK");
                return;
            }

            Rigidbody rb = FindPlayerRigidbody();
            if (rb == null)
            {
                EditorUtility.DisplayDialog("FIXER UNITY", "Select your existing Gorilla/player GameObject or its Rigidbody first. I will NOT create another Rigidbody.", "OK");
                return;
            }

            try
            {
                System.IO.Directory.CreateDirectory("Assets/Scripts");
                string source = Encoding.UTF8.GetString(Convert.FromBase64String(ControllerBase64));
                System.IO.File.WriteAllText(ScriptPath, source, new UTF8Encoding(false));
                AssetDatabase.ImportAsset(ScriptPath, ImportAssetOptions.ForceUpdate);
                AssetDatabase.Refresh();
                SessionState.SetBool(PendingKey, true);
                SessionState.SetString(PlayerPathKey, Path(rb.transform));
                EditorUtility.DisplayDialog("FIXER UNITY", "Jetpack script written. Unity is compiling it; FIXER will wire it after compilation.", "OK");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorUtility.DisplayDialog("FIXER UNITY", "Jetpack build failed:\n\n" + ex.Message, "OK");
            }
        }

        private static void TryFinish()
        {
            if (!SessionState.GetBool(PendingKey, false) || EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            Type type = FindType("JetpackController");
            if (type == null || !typeof(Component).IsAssignableFrom(type)) return;

            Rigidbody rb = FindByPath(SessionState.GetString(PlayerPathKey, ""));
            if (rb == null) rb = FindPlayerRigidbody();
            if (rb == null) { ClearPending(); Debug.LogError("FIXER UNITY: Existing player Rigidbody disappeared before wiring."); return; }

            Component controller = rb.gameObject.GetComponent(type) ?? Undo.AddComponent(rb.gameObject, type);
            Transform root = rb.transform;
            Transform head = FindHead(root);
            AudioSource audio = EnsureAudio(root);
            ParticleSystem left = EnsureThruster(root, "LeftThruster", new Vector3(-0.22f, -0.28f, -0.15f));
            ParticleSystem right = EnsureThruster(root, "RightThruster", new Vector3(0.22f, -0.28f, -0.15f));

            SerializedObject so = new SerializedObject(controller);
            Set(so, "playerRigidbody", rb); Set(so, "playerRoot", root); Set(so, "headTransform", head);
            Set(so, "jetpackAudio", audio); Set(so, "leftThruster", left); Set(so, "rightThruster", right);
            so.FindProperty("jetpackEnabled").boolValue = true;
            so.FindProperty("upwardForce").floatValue = 12f;
            so.FindProperty("moveForce").floatValue = 8f;
            so.FindProperty("maxSpeed").floatValue = 12f;
            so.FindProperty("hoverDamping").floatValue = 4f;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(controller); EditorUtility.SetDirty(rb.gameObject);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            Selection.activeGameObject = rb.gameObject;
            ClearPending();
            EditorUtility.DisplayDialog("FIXER UNITY", "VR JETPACK READY ✅\n\nExisting Rigidbody reused: YES\nRight trigger = fly\nLeft joystick = move\nA = hover\nSPACE/WASD/H = PC test", "OK");
        }

        private static void ClearPending() { SessionState.SetBool(PendingKey, false); SessionState.EraseString(PlayerPathKey); }

        private static Rigidbody FindPlayerRigidbody()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected != null && selected.scene.IsValid())
            {
                Rigidbody r = selected.GetComponent<Rigidbody>() ?? selected.GetComponentInParent<Rigidbody>() ?? selected.GetComponentInChildren<Rigidbody>(true);
                if (r != null) return r;
            }

            Rigidbody best = null; int scoreBest = 20;
            foreach (Rigidbody r in Resources.FindObjectsOfTypeAll<Rigidbody>())
            {
                if (r == null || !r.gameObject.scene.IsValid() || r.gameObject.scene != SceneManager.GetActiveScene()) continue;
                string n = Path(r.transform).ToLowerInvariant(); int s = 0;
                if (n.Contains("gorilla")) s += 100; if (n.Contains("player")) s += 80; if (n.Contains("locomotion")) s += 60; if (n.Contains("xr origin") || n.Contains("xrorigin")) s += 40; if (n.Contains("monster")) s -= 100; if (n.Contains("menu")) s -= 80;
                if (s > scoreBest) { scoreBest = s; best = r; }
            }
            return best;
        }

        private static Rigidbody FindByPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    if (string.Equals(Path(t), path, StringComparison.OrdinalIgnoreCase)) return t.GetComponent<Rigidbody>() ?? t.GetComponentInParent<Rigidbody>();
            return null;
        }

        private static Transform FindHead(Transform root)
        {
            Camera c = root.GetComponentInChildren<Camera>(true); if (c != null) return c.transform;
            return Camera.main != null ? Camera.main.transform : root;
        }

        private static AudioSource EnsureAudio(Transform parent)
        {
            Transform t = parent.Find("JetpackAudio"); GameObject g = t != null ? t.gameObject : new GameObject("JetpackAudio");
            if (t == null) { Undo.RegisterCreatedObjectUndo(g, "FIXER JetpackAudio"); g.transform.SetParent(parent, false); }
            AudioSource a = g.GetComponent<AudioSource>() ?? Undo.AddComponent<AudioSource>(g); a.playOnAwake = false; a.loop = true; a.spatialBlend = 1f; a.volume = 0.6f; return a;
        }

        private static ParticleSystem EnsureThruster(Transform parent, string name, Vector3 pos)
        {
            Transform t = parent.Find(name); GameObject g = t != null ? t.gameObject : new GameObject(name);
            if (t == null) { Undo.RegisterCreatedObjectUndo(g, "FIXER " + name); g.transform.SetParent(parent, false); }
            g.transform.localPosition = pos; ParticleSystem p = g.GetComponent<ParticleSystem>() ?? Undo.AddComponent<ParticleSystem>(g);
            var main = p.main; main.loop = true; main.playOnAwake = false; main.startLifetime = 0.22f; main.startSpeed = 2.2f; main.startSize = 0.08f; main.maxParticles = 40;
            var emission = p.emission; emission.rateOverTime = 22f; p.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); return p;
        }

        private static void Set(SerializedObject so, string name, UnityEngine.Object value) { SerializedProperty p = so.FindProperty(name); if (p != null) p.objectReferenceValue = value; }

        private static Type FindType(string name)
        {
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = a.GetType(name, false, true); if (t != null) return t;
                try { foreach (Type x in a.GetTypes()) if (string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)) return x; } catch (ReflectionTypeLoadException) { }
            }
            return null;
        }

        private static string Path(Transform t)
        {
            if (t == null) return ""; string p = t.name; while (t.parent != null) { t = t.parent; p = t.name + "/" + p; } return p;
        }
    }
}
