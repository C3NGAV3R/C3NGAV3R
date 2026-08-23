using System;
using System.IO;
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
        private const string ScriptPath = "Assets/Scripts/JetpackController.cs";

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
                    "I could not safely identify an EXISTING player Rigidbody. Select your Gorilla/player root (or its Rigidbody object) in the Hierarchy, then run BUILD OR REPAIR VR JETPACK again. I did NOT add a second Rigidbody.",
                    "OK");
                return;
            }

            WriteControllerScript();
            SessionState.SetBool(PendingKey, true);
            SessionState.SetString(PendingKey + ".PlayerPath", HierarchyPath(rb.transform));
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog(
                "FIXER UNITY",
                "JetpackController.cs was created/updated. Unity will compile it now, then FIXER UNITY will automatically attach and wire it to your existing player Rigidbody.",
                "OK");
        }

        private static void TryFinishPending()
        {
            if (!SessionState.GetBool(PendingKey, false) || EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            Type jetpackType = FindType("JetpackController");
            if (jetpackType == null || !typeof(Component).IsAssignableFrom(jetpackType))
                return;

            Rigidbody rb = null;
            string savedPath = SessionState.GetString(PendingKey + ".PlayerPath", "");
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
                SessionState.SetBool(PendingKey, false);
                Debug.LogError("FIXER UNITY: Jetpack script compiled, but the existing player Rigidbody could no longer be found. Select the player and run the repair again.");
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
            SessionState.SetBool(PendingKey, false);
            SessionState.EraseString(PendingKey + ".PlayerPath");

            Debug.Log("FIXER UNITY: VR Jetpack wired successfully to existing Rigidbody on " + rb.gameObject.name + ". Right trigger=up, left stick=move, A=hover, keyboard SPACE/WASD/H=test.");
            EditorUtility.DisplayDialog(
                "FIXER UNITY",
                "VR JETPACK READY ✅\n\nAttached to: " + rb.gameObject.name +
                "\nExisting Rigidbody reused: YES" +
                "\nRight trigger: fly up" +
                "\nLeft joystick: move" +
                "\nA: toggle hover" +
                "\nSPACE/WASD/H: Editor test" +
                "\n\nJetpackAudio has no clip yet. Drag your own sound into its Audio Clip field when you want sound.",
                "OK");
        }

        private static void WriteControllerScript()
        {
            string folder = Path.GetDirectoryName(ScriptPath);
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            File.WriteAllText(ScriptPath, ControllerSource, new System.Text.UTF8Encoding(false));
        }

        private static Rigidbody FindBestPlayerRigidbody()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected != null && selected.scene.IsValid())
            {
                Rigidbody selectedRb = selected.GetComponent<Rigidbody>() ?? selected.GetComponentInParent<Rigidbody>() ?? selected.GetComponentInChildren<Rigidbody>(true);
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

                foreach (Component c in rb.GetComponents<Component>())
                {
                    if (c == null) continue;
                    string n = c.GetType().FullName.ToLowerInvariant();
                    if (n.Contains("gorillalocomotion")) score += 150;
                    if (n.EndsWith(".player") || n.Contains("gorillaplayer")) score += 100;
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
            Transform t = rbTransform;
            while (t != null)
            {
                string n = t.name.ToLowerInvariant();
                if (n.Contains("gorilla") || n.Contains("player") || n.Contains("xr origin") || n.Contains("xrorigin") || n.Contains("rig"))
                    best = t;
                t = t.parent;
            }
            return best;
        }

        private static Transform FindHead(Transform playerRoot, Transform fallback)
        {
            if (playerRoot != null)
            {
                Camera c = playerRoot.GetComponentInChildren<Camera>(true);
                if (c != null) return c.transform;
            }

            if (Camera.main != null)
                return Camera.main.transform;

            foreach (Camera c in Resources.FindObjectsOfTypeAll<Camera>())
            {
                if (c != null && c.gameObject.scene.IsValid() && c.gameObject.scene == SceneManager.GetActiveScene())
                    return c.transform;
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
            else go = child.gameObject;

            AudioSource audio = go.GetComponent<AudioSource>();
            if (audio == null) audio = Undo.AddComponent<AudioSource>(go);
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
            else go = child.gameObject;

            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            ParticleSystem ps = go.GetComponent<ParticleSystem>();
            if (ps == null) ps = Undo.AddComponent<ParticleSystem>(go);

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
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type direct = a.GetType(shortName, false, true);
                if (direct != null) return direct;
                try
                {
                    foreach (Type t in a.GetTypes())
                        if (string.Equals(t.Name, shortName, StringComparison.OrdinalIgnoreCase))
                            return t;
                }
                catch (ReflectionTypeLoadException) { }
            }
            return null;
        }

        private static void SetObject(SerializedObject so, string property, UnityEngine.Object value)
        {
            SerializedProperty p = so.FindProperty(property);
            if (p != null) p.objectReferenceValue = value;
        }

        private static void SetBool(SerializedObject so, string property, bool value)
        {
            SerializedProperty p = so.FindProperty(property);
            if (p != null) p.boolValue = value;
        }

        private static void SetFloat(SerializedObject so, string property, float value)
        {
            SerializedProperty p = so.FindProperty(property);
            if (p != null) p.floatValue = value;
        }

        private static GameObject FindByHierarchyPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    if (string.Equals(HierarchyPath(t), path, StringComparison.OrdinalIgnoreCase))
                        return t.gameObject;
            }
            return null;
        }

        private static string HierarchyPath(Transform t)
        {
            if (t == null) return "";
            string path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }
            return path;
        }

        private const string ControllerSource = @"using System;
using UnityEngine;
using UnityEngine.XR;

public class JetpackController : MonoBehaviour
{
    [Header("Existing player references")]
    public Rigidbody playerRigidbody;
    public Transform playerRoot;
    public Transform headTransform;

    [Header("Jetpack")]
    public bool jetpackEnabled = true;
    public float upwardForce = 12f;
    public float moveForce = 8f;
    public float maxSpeed = 12f;
    public float hoverDamping = 4f;

    [Header("Optional effects")]
    public AudioSource jetpackAudio;
    public ParticleSystem leftThruster;
    public ParticleSystem rightThruster;

    [Header("Runtime")]
    public bool hoverEnabled;

    private InputDevice leftController;
    private InputDevice rightController;
    private bool lastPrimaryButton;
    private bool upHeld;
    private Vector2 moveInput;
    private bool activeNow;

    private void Awake()
    {
        if (playerRigidbody == null) playerRigidbody = GetComponent<Rigidbody>();
        if (playerRoot == null) playerRoot = transform;
        if (headTransform == null && Camera.main != null) headTransform = Camera.main.transform;
        AcquireControllers();
        StopEffects();
    }

    private void OnEnable()
    {
        AcquireControllers();
    }

    private void Update()
    {
        if (!jetpackEnabled)
        {
            upHeld = false;
            moveInput = Vector2.zero;
            activeNow = false;
            hoverEnabled = false;
            StopEffects();
            return;
        }

        if (!leftController.isValid || !rightController.isValid) AcquireControllers();

        bool trigger = false;
        bool primary = false;
        Vector2 stick = Vector2.zero;
        if (rightController.isValid)
        {
            rightController.TryGetFeatureValue(CommonUsages.triggerButton, out trigger);
            rightController.TryGetFeatureValue(CommonUsages.primaryButton, out primary);
        }
        if (leftController.isValid)
            leftController.TryGetFeatureValue(CommonUsages.primary2DAxis, out stick);

        bool keyboardUp = SafeGetKey(KeyCode.Space);
        Vector2 keyboardMove = new Vector2(
            (SafeGetKey(KeyCode.D) ? 1f : 0f) - (SafeGetKey(KeyCode.A) ? 1f : 0f),
            (SafeGetKey(KeyCode.W) ? 1f : 0f) - (SafeGetKey(KeyCode.S) ? 1f : 0f));

        if ((primary && !lastPrimaryButton) || SafeGetKeyDown(KeyCode.H))
            hoverEnabled = !hoverEnabled;
        lastPrimaryButton = primary;

        upHeld = trigger || keyboardUp;
        moveInput = Vector2.ClampMagnitude(stick + keyboardMove, 1f);
        activeNow = upHeld || moveInput.sqrMagnitude > 0.0025f || hoverEnabled;
        UpdateEffects(activeNow);
    }

    private void FixedUpdate()
    {
        if (!jetpackEnabled || playerRigidbody == null) return;

        if (upHeld)
            playerRigidbody.AddForce(Vector3.up * upwardForce, ForceMode.Acceleration);

        if (moveInput.sqrMagnitude > 0.0025f)
        {
            Transform basis = headTransform != null ? headTransform : (playerRoot != null ? playerRoot : transform);
            Vector3 forward = Vector3.ProjectOnPlane(basis.forward, Vector3.up).normalized;
            Vector3 right = Vector3.ProjectOnPlane(basis.right, Vector3.up).normalized;
            Vector3 wish = forward * moveInput.y + right * moveInput.x;
            if (wish.sqrMagnitude > 1f) wish.Normalize();
            playerRigidbody.AddForce(wish * moveForce, ForceMode.Acceleration);
        }

        if (hoverEnabled)
        {
            playerRigidbody.AddForce(-Physics.gravity, ForceMode.Acceleration);
            Vector3 v = GetVelocity();
            playerRigidbody.AddForce(Vector3.up * (-v.y * hoverDamping), ForceMode.Acceleration);
        }

        if (activeNow && maxSpeed > 0.1f)
        {
            Vector3 v = GetVelocity();
            if (v.magnitude > maxSpeed)
                SetVelocity(v.normalized * maxSpeed);
        }
    }

    public void EnableJetpack()
    {
        jetpackEnabled = true;
    }

    public void DisableJetpack()
    {
        jetpackEnabled = false;
        hoverEnabled = false;
        upHeld = false;
        moveInput = Vector2.zero;
        activeNow = false;
        StopEffects();
    }

    public void ToggleJetpack()
    {
        if (jetpackEnabled) DisableJetpack();
        else EnableJetpack();
    }

    private void AcquireControllers()
    {
        leftController = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        rightController = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
    }

    private void UpdateEffects(bool active)
    {
        if (jetpackAudio != null)
        {
            if (active && !jetpackAudio.isPlaying && jetpackAudio.clip != null) jetpackAudio.Play();
            else if (!active && jetpackAudio.isPlaying) jetpackAudio.Stop();
        }

        SetParticle(leftThruster, active);
        SetParticle(rightThruster, active);
    }

    private static void SetParticle(ParticleSystem ps, bool active)
    {
        if (ps == null) return;
        if (active)
        {
            if (!ps.isPlaying) ps.Play();
        }
        else if (ps.isPlaying)
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    private void StopEffects()
    {
        if (jetpackAudio != null && jetpackAudio.isPlaying) jetpackAudio.Stop();
        SetParticle(leftThruster, false);
        SetParticle(rightThruster, false);
    }

    private void OnDisable()
    {
        StopEffects();
    }

    private static bool SafeGetKey(KeyCode key)
    {
        try { return Input.GetKey(key); }
        catch (InvalidOperationException) { return false; }
    }

    private static bool SafeGetKeyDown(KeyCode key)
    {
        try { return Input.GetKeyDown(key); }
        catch (InvalidOperationException) { return false; }
    }

    private Vector3 GetVelocity()
    {
#if UNITY_6000_0_OR_NEWER
        return playerRigidbody.linearVelocity;
#else
        return playerRigidbody.velocity;
#endif
    }

    private void SetVelocity(Vector3 value)
    {
#if UNITY_6000_0_OR_NEWER
        playerRigidbody.linearVelocity = value;
#else
        playerRigidbody.velocity = value;
#endif
    }
}
";
    }
}
