using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Networking;

namespace C3NGAV3R.PrimatePanicAI
{
    public class PrimatePanicAIWindow : EditorWindow
    {
        private const string ModelPref = "PrimatePanicAI.VisionModel";
        private const string EndpointPref = "PrimatePanicAI.OllamaEndpoint";
        private const string AutoApplyPref = "PrimatePanicAI.PictureAutoApply";

        private string model = "qwen2.5vl:3b";
        private string endpoint = "http://127.0.0.1:11434/api/generate";
        private string imagePath = "";
        private string prompt = "Recreate the main object in this reference image as a clean 3D Unity blockout. Ignore Unity editor gizmos, rig/bone lines, transform handles, grids and UI. Match the visible proportions, major shapes and colors as closely as possible.";
        private string resultText = "";
        private Texture2D preview;
        private Vector2 scroll;
        private bool waiting;
        private bool autoApply = true;
        private RecreationPlan lastPlan;

        [MenuItem("Tools/Primate Panic AI")]
        public static void Open()
        {
            GetWindow<PrimatePanicAIWindow>("Primate Panic AI");
        }

        private void OnEnable()
        {
            model = EditorPrefs.GetString(ModelPref, "qwen2.5vl:3b");
            endpoint = EditorPrefs.GetString(EndpointPref, "http://127.0.0.1:11434/api/generate");
            autoApply = EditorPrefs.GetBool(AutoApplyPref, true);
        }

        private void OnDisable()
        {
            if (preview != null)
                DestroyImmediate(preview);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Primate Panic AI - PICTURE → UNITY", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Pick a reference picture. A local Ollama vision model analyzes it and builds a 3D Unity blockout directly in your scene using primitives. One flat image cannot recover the exact original mesh, rig or hidden geometry.",
                MessageType.Info
            );

            EditorGUI.BeginChangeCheck();
            model = EditorGUILayout.TextField("Vision Model", model);
            endpoint = EditorGUILayout.TextField("Ollama URL", endpoint);
            autoApply = EditorGUILayout.ToggleLeft("Create recreation automatically", autoApply);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetString(ModelPref, model);
                EditorPrefs.SetString(EndpointPref, endpoint);
                EditorPrefs.SetBool(AutoApplyPref, autoApply);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.enabled = !waiting;
                if (GUILayout.Button("Test Vision Model", GUILayout.Height(28)))
                    SendTextTest();

                if (GUILayout.Button("Pick Reference Picture", GUILayout.Height(28)))
                    PickImage();

                if (GUILayout.Button("Clear Picture", GUILayout.Height(28)))
                    ClearImage();
                GUI.enabled = true;
            }

            if (!string.IsNullOrEmpty(imagePath))
            {
                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("Reference", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(Path.GetFileName(imagePath));

                if (preview != null)
                {
                    float maxWidth = Mathf.Max(160f, position.width - 28f);
                    float aspect = preview.height > 0 ? (float)preview.width / preview.height : 1f;
                    float width = Mathf.Min(maxWidth, 440f);
                    float height = Mathf.Clamp(width / Mathf.Max(0.01f, aspect), 120f, 320f);
                    Rect r = GUILayoutUtility.GetRect(width, height, GUILayout.ExpandWidth(false));
                    EditorGUI.DrawPreviewTexture(r, preview, null, ScaleMode.ScaleToFit);
                }
            }
            else
            {
                EditorGUILayout.HelpBox("No reference picture selected yet.", MessageType.Warning);
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("What should it recreate?", EditorStyles.boldLabel);
            prompt = EditorGUILayout.TextArea(prompt, GUILayout.MinHeight(90));

            GUI.enabled = !waiting && preview != null && !string.IsNullOrWhiteSpace(prompt) && !string.IsNullOrWhiteSpace(model);
            if (GUILayout.Button(waiting ? "VISION AI IS BUILDING..." : "RECREATE PICTURE IN UNITY", GUILayout.Height(38)))
                SendRecreationRequest();
            GUI.enabled = true;

            if (!autoApply && lastPlan != null)
            {
                if (GUILayout.Button("CREATE LAST PLAN IN SCENE", GUILayout.Height(32)))
                {
                    resultText += "\n\n" + ApplyPlan(lastPlan);
                    lastPlan = null;
                }
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Result", EditorStyles.boldLabel);
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MinHeight(170));
            EditorGUILayout.TextArea(resultText, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private void PickImage()
        {
            string path = EditorUtility.OpenFilePanel("Pick reference picture", "", "png,jpg,jpeg");
            if (string.IsNullOrEmpty(path))
                return;

            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(bytes))
                {
                    DestroyImmediate(tex);
                    resultText = "Could not load that image.";
                    return;
                }

                if (preview != null)
                    DestroyImmediate(preview);

                preview = tex;
                imagePath = path;
                resultText = "Reference picture loaded. Ready to recreate.";
                Repaint();
            }
            catch (Exception ex)
            {
                resultText = "Could not open picture: " + ex.Message;
            }
        }

        private void ClearImage()
        {
            imagePath = "";
            if (preview != null)
            {
                DestroyImmediate(preview);
                preview = null;
            }
            lastPlan = null;
            resultText = "Picture cleared.";
        }

        private void SendTextTest()
        {
            OllamaVisionRequest req = new OllamaVisionRequest
            {
                model = model.Trim(),
                prompt = "Reply with exactly: VISION MODEL CONNECTED",
                stream = false,
                format = "",
                images = null,
                options = new OllamaOptions { num_ctx = 2048, num_predict = 32, temperature = 0f }
            };
            SendOllama(JsonUtility.ToJson(req), false, true);
        }

        private void SendRecreationRequest()
        {
            try
            {
                string imageBase64 = BuildOptimizedImageBase64(preview, 896);
                string fullPrompt = BuildVisionPrompt(prompt);

                OllamaVisionRequest req = new OllamaVisionRequest
                {
                    model = model.Trim(),
                    prompt = fullPrompt,
                    stream = false,
                    format = "json",
                    images = new[] { imageBase64 },
                    options = new OllamaOptions
                    {
                        num_ctx = 8192,
                        num_predict = 6000,
                        temperature = 0.15f
                    }
                };

                SendOllama(JsonUtility.ToJson(req), true, false);
            }
            catch (Exception ex)
            {
                resultText = "Could not prepare reference picture: " + ex.Message;
            }
        }

        private string BuildVisionPrompt(string userRequest)
        {
            return
                "You are a Unity 3D reconstruction agent. Analyze the attached reference image and return ONLY valid JSON. " +
                "Create a recognizable 3D BLOCKOUT from Unity primitives. Ignore editor UI, scene gizmos, red/blue rig handles, bone lines, transform arrows, grids, cameras and lights unless the user explicitly asks for them. " +
                "Focus on the actual visible object. Use meters and keep the complete reconstruction roughly 1 to 2.5 meters tall. " +
                "Prefer 8-35 meaningful parts; maximum 60. Use symmetry when appropriate. Available primitives: Cube, Sphere, Capsule, Cylinder, Plane, Quad. " +
                "Each object needs a unique id. parentId may be empty or the id of an earlier object. position/rotation/scale are LOCAL to the parent. " +
                "Use hexadecimal colors like #3A3A3A. Unless collision is visually/functionally important, keepCollider=false. " +
                "JSON schema: {\"message\":\"short summary\",\"rootName\":\"AI_Recreation\",\"objects\":[{" +
                "\"id\":\"part1\",\"parentId\":\"\",\"name\":\"Body\",\"primitive\":\"Capsule\",\"position\":{\"x\":0,\"y\":1,\"z\":0}," +
                "\"rotation\":{\"x\":0,\"y\":0,\"z\":0},\"scale\":{\"x\":1,\"y\":1,\"z\":1},\"color\":\"#808080\",\"keepCollider\":false}]}. " +
                "Do not include markdown fences or explanations outside JSON. USER REQUEST: " + userRequest;
        }

        private void SendOllama(string json, bool recreationMode, bool connectionTest)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                resultText = "Ollama URL is empty.";
                return;
            }

            waiting = true;
            resultText = connectionTest ? "Testing vision model..." : "Vision model is analyzing the picture...";
            Repaint();

            UnityWebRequest request = new UnityWebRequest(endpoint.Trim(), "POST");
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = 600;

            var op = request.SendWebRequest();
            op.completed += _ =>
            {
                waiting = false;
                if (request.result != UnityWebRequest.Result.Success)
                {
                    resultText = "OLLAMA REQUEST FAILED\nHTTP " + request.responseCode + "\n" + request.error + "\n\n" + request.downloadHandler.text;
                }
                else
                {
                    string text = ExtractOllamaText(request.downloadHandler.text);
                    if (connectionTest)
                    {
                        resultText = text.Contains("VISION MODEL CONNECTED")
                            ? "VISION MODEL CONNECTED ✅"
                            : "VISION MODEL REPLIED:\n" + text;
                    }
                    else if (recreationMode)
                    {
                        HandleRecreationResponse(text);
                    }
                    else
                    {
                        resultText = text;
                    }
                }

                request.Dispose();
                Repaint();
            };
        }

        private void HandleRecreationResponse(string modelText)
        {
            try
            {
                string json = ExtractJsonObject(modelText);
                RecreationPlan plan = JsonUtility.FromJson<RecreationPlan>(json);
                if (plan == null || plan.objects == null || plan.objects.Length == 0)
                {
                    resultText = "Vision model did not return a usable 3D plan.\n\n" + modelText;
                    return;
                }

                if (plan.objects.Length > 60)
                {
                    RecreationObject[] bounded = new RecreationObject[60];
                    Array.Copy(plan.objects, bounded, 60);
                    plan.objects = bounded;
                }

                lastPlan = plan;
                resultText = (string.IsNullOrWhiteSpace(plan.message) ? "3D recreation planned." : plan.message) +
                             "\nParts: " + plan.objects.Length;

                if (autoApply)
                {
                    resultText += "\n\n" + ApplyPlan(plan);
                    lastPlan = null;
                }
                else
                {
                    resultText += "\nAuto-create is OFF. Click CREATE LAST PLAN IN SCENE.";
                }
            }
            catch (Exception ex)
            {
                resultText = "Could not parse the vision model plan: " + ex.Message + "\n\nMODEL RESPONSE:\n" + modelText;
            }
        }

        private string ApplyPlan(RecreationPlan plan)
        {
            string rootName = SanitizeName(string.IsNullOrWhiteSpace(plan.rootName) ? "AI_Recreation" : plan.rootName);
            GameObject root = new GameObject(rootName);
            Undo.RegisterCreatedObjectUndo(root, "AI Picture Recreation");

            Dictionary<string, GameObject> created = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
            int made = 0;

            foreach (RecreationObject item in plan.objects)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.id))
                    continue;

                PrimitiveType primitive;
                if (!TryPrimitive(item.primitive, out primitive))
                    primitive = PrimitiveType.Cube;

                GameObject go = GameObject.CreatePrimitive(primitive);
                go.name = SanitizeName(string.IsNullOrWhiteSpace(item.name) ? item.id : item.name);

                Transform parent = root.transform;
                if (!string.IsNullOrWhiteSpace(item.parentId) && created.TryGetValue(item.parentId, out GameObject parentObject))
                    parent = parentObject.transform;

                go.transform.SetParent(parent, false);
                go.transform.localPosition = ToVector(item.position, Vector3.zero);
                go.transform.localEulerAngles = ToVector(item.rotation, Vector3.zero);
                go.transform.localScale = ClampScale(ToVector(item.scale, Vector3.one));

                ApplyColor(go, item.color);

                if (!item.keepCollider)
                {
                    Collider c = go.GetComponent<Collider>();
                    if (c != null)
                        DestroyImmediate(c);
                }

                created[item.id] = go;
                made++;
            }

            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);
            if (root.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(root.scene);

            return "✅ Created " + made + " 3D parts under '" + root.name + "'.\nYou can move/scale the root as one object. Ctrl+Z will undo the recreation.";
        }

        private static void ApplyColor(GameObject go, string hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
                return;

            Color color;
            if (!ColorUtility.TryParseHtmlString(hex.Trim(), out color))
                return;

            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer == null)
                return;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");
            if (shader == null)
                return;

            Material material = new Material(shader);
            material.name = "AI_" + go.name + "_Mat";
            material.color = color;
            renderer.sharedMaterial = material;
        }

        private static string BuildOptimizedImageBase64(Texture2D source, int maxDimension)
        {
            if (source == null)
                throw new InvalidOperationException("No picture selected.");

            int width = source.width;
            int height = source.height;
            float scale = Mathf.Min(1f, (float)maxDimension / Mathf.Max(width, height));
            int targetW = Mathf.Max(1, Mathf.RoundToInt(width * scale));
            int targetH = Mathf.Max(1, Mathf.RoundToInt(height * scale));

            RenderTexture rt = RenderTexture.GetTemporary(targetW, targetH, 0, RenderTextureFormat.ARGB32);
            RenderTexture old = RenderTexture.active;
            Graphics.Blit(source, rt);
            RenderTexture.active = rt;

            Texture2D resized = new Texture2D(targetW, targetH, TextureFormat.RGB24, false);
            resized.ReadPixels(new Rect(0, 0, targetW, targetH), 0, 0);
            resized.Apply();

            RenderTexture.active = old;
            RenderTexture.ReleaseTemporary(rt);

            byte[] jpg = resized.EncodeToJPG(82);
            DestroyImmediate(resized);
            return Convert.ToBase64String(jpg);
        }

        private static string ExtractOllamaText(string json)
        {
            OllamaResponse response = JsonUtility.FromJson<OllamaResponse>(json);
            if (response == null)
                return "Ollama returned an empty response.";
            if (!string.IsNullOrEmpty(response.error))
                return "OLLAMA ERROR: " + response.error;
            return response.response ?? "";
        }

        private static string ExtractJsonObject(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException("The model returned an empty response.");

            string trimmed = text.Trim();
            if (trimmed.StartsWith("```"))
            {
                int firstNewline = trimmed.IndexOf('\n');
                int lastFence = trimmed.LastIndexOf("```");
                if (firstNewline >= 0 && lastFence > firstNewline)
                    trimmed = trimmed.Substring(firstNewline + 1, lastFence - firstNewline - 1).Trim();
            }

            int start = trimmed.IndexOf('{');
            int end = trimmed.LastIndexOf('}');
            if (start < 0 || end <= start)
                throw new InvalidOperationException("No JSON object was found.");
            return trimmed.Substring(start, end - start + 1);
        }

        private static bool TryPrimitive(string value, out PrimitiveType type)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "sphere": type = PrimitiveType.Sphere; return true;
                case "capsule": type = PrimitiveType.Capsule; return true;
                case "cylinder": type = PrimitiveType.Cylinder; return true;
                case "plane": type = PrimitiveType.Plane; return true;
                case "quad": type = PrimitiveType.Quad; return true;
                case "cube": type = PrimitiveType.Cube; return true;
                default: type = PrimitiveType.Cube; return false;
            }
        }

        private static Vector3 ToVector(Vector3Data v, Vector3 fallback)
        {
            return v == null ? fallback : new Vector3(v.x, v.y, v.z);
        }

        private static Vector3 ClampScale(Vector3 v)
        {
            return new Vector3(
                Mathf.Clamp(Mathf.Abs(v.x), 0.01f, 10f),
                Mathf.Clamp(Mathf.Abs(v.y), 0.01f, 10f),
                Mathf.Clamp(Mathf.Abs(v.z), 0.01f, 10f));
        }

        private static string SanitizeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "AI_Part";
            return value.Replace('/', '_').Replace('\\', '_').Trim();
        }

        [Serializable]
        private class OllamaVisionRequest
        {
            public string model;
            public string prompt;
            public bool stream;
            public string format;
            public string[] images;
            public OllamaOptions options;
        }

        [Serializable]
        private class OllamaOptions
        {
            public int num_ctx;
            public int num_predict;
            public float temperature;
        }

        [Serializable]
        private class OllamaResponse
        {
            public string response;
            public string error;
        }

        [Serializable]
        private class RecreationPlan
        {
            public string message;
            public string rootName;
            public RecreationObject[] objects;
        }

        [Serializable]
        private class RecreationObject
        {
            public string id;
            public string parentId;
            public string name;
            public string primitive;
            public Vector3Data position;
            public Vector3Data rotation;
            public Vector3Data scale;
            public string color;
            public bool keepCollider;
        }

        [Serializable]
        private class Vector3Data
        {
            public float x;
            public float y;
            public float z;
        }
    }
}
