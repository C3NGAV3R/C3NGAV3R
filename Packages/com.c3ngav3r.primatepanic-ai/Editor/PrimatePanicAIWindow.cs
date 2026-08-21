using System;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace C3NGAV3R.PrimatePanicAI
{
    public class PrimatePanicAIWindow : EditorWindow
    {
        private const string ModelPref = "PrimatePanicAI.OllamaModel";
        private const string EndpointPref = "PrimatePanicAI.OllamaEndpoint";

        private string model = "qwen2.5-coder:7b";
        private string endpoint = "http://127.0.0.1:11434/api/generate";
        private string prompt = "";
        private string responseText = "";
        private Vector2 scroll;
        private bool waiting;

        [MenuItem("Tools/Primate Panic AI")]
        public static void Open()
        {
            GetWindow<PrimatePanicAIWindow>("Primate Panic AI");
        }

        private void OnEnable()
        {
            model = EditorPrefs.GetString(ModelPref, "qwen2.5-coder:7b");
            endpoint = EditorPrefs.GetString(EndpointPref, "http://127.0.0.1:11434/api/generate");
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Primate Panic AI - LOCAL", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Runs through Ollama on your own PC. No OpenAI API key, no API credits. Keep Ollama running and make sure the selected model is installed.",
                MessageType.Info
            );

            EditorGUI.BeginChangeCheck();
            model = EditorGUILayout.TextField("Ollama Model", model);
            endpoint = EditorGUILayout.TextField("Ollama URL", endpoint);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetString(ModelPref, model);
                EditorPrefs.SetString(EndpointPref, endpoint);
            }

            EditorGUILayout.Space(8);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.enabled = !waiting;
                if (GUILayout.Button("Test Ollama", GUILayout.Height(28)))
                {
                    SendRequest("Reply with exactly: OLLAMA CONNECTED", true);
                }

                if (GUILayout.Button("Inspect Selected GameObject", GUILayout.Height(28)))
                {
                    prompt = BuildSelectedObjectPrompt();
                }

                if (GUILayout.Button("Clear", GUILayout.Height(28)))
                {
                    prompt = "";
                    responseText = "";
                }
                GUI.enabled = true;
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Ask", EditorStyles.boldLabel);
            prompt = EditorGUILayout.TextArea(prompt, GUILayout.MinHeight(110));

            GUI.enabled = !waiting && !string.IsNullOrWhiteSpace(prompt) && !string.IsNullOrWhiteSpace(model);
            if (GUILayout.Button(waiting ? "Thinking locally..." : "Send to Ollama", GUILayout.Height(34)))
            {
                SendRequest(prompt, false);
            }
            GUI.enabled = true;

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Answer", EditorStyles.boldLabel);
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MinHeight(220));
            EditorGUILayout.TextArea(responseText, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private string BuildSelectedObjectPrompt()
        {
            GameObject go = Selection.activeGameObject;
            if (go == null)
                return "Help me debug my Unity VR project. I do not currently have a GameObject selected.";

            var sb = new StringBuilder();
            sb.AppendLine("You are helping debug a Unity Gorilla-style VR horror game.");
            sb.AppendLine("Use only the information below. Give simple exact Unity steps. If code must change, give the full replacement script.");
            sb.AppendLine();
            sb.AppendLine("Selected GameObject: " + go.name);
            sb.AppendLine("Active self: " + go.activeSelf);
            sb.AppendLine("Active in hierarchy: " + go.activeInHierarchy);
            sb.AppendLine("Layer: " + LayerMask.LayerToName(go.layer));
            sb.AppendLine("Tag: " + go.tag);
            sb.AppendLine("Transform local position: " + go.transform.localPosition);
            sb.AppendLine("Transform local rotation: " + go.transform.localEulerAngles);
            sb.AppendLine("Transform local scale: " + go.transform.localScale);
            sb.AppendLine("Parent: " + (go.transform.parent != null ? go.transform.parent.name : "NONE"));
            sb.AppendLine("Components:");

            foreach (Component c in go.GetComponents<Component>())
            {
                if (c == null)
                {
                    sb.AppendLine("- MISSING SCRIPT / NULL COMPONENT");
                    continue;
                }

                sb.AppendLine("- " + c.GetType().FullName);

                if (c is Rigidbody rb)
                {
                    sb.AppendLine("  Rigidbody mass=" + rb.mass +
                                  ", useGravity=" + rb.useGravity +
                                  ", isKinematic=" + rb.isKinematic +
                                  ", constraints=" + rb.constraints +
                                  ", interpolation=" + rb.interpolation +
                                  ", collisionDetection=" + rb.collisionDetectionMode);
                }
                else if (c is Collider col)
                {
                    sb.AppendLine("  Collider enabled=" + col.enabled +
                                  ", isTrigger=" + col.isTrigger +
                                  ", material=" + (col.sharedMaterial != null ? col.sharedMaterial.name : "NONE"));
                }
                else if (c is Animator animator)
                {
                    sb.AppendLine("  Animator enabled=" + animator.enabled +
                                  ", applyRootMotion=" + animator.applyRootMotion +
                                  ", updateMode=" + animator.updateMode);
                }
            }

            sb.AppendLine();
            sb.AppendLine("Tell me: 1) what looks wrong, 2) why, 3) the exact Inspector/hierarchy fix, and 4) any full replacement script needed.");
            return sb.ToString();
        }

        private void SendRequest(string text, bool connectionTest)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                responseText = "Ollama URL is empty.";
                return;
            }

            waiting = true;
            responseText = connectionTest ? "Testing Ollama..." : "Sending to local Ollama...";
            Repaint();

            string json = JsonUtility.ToJson(new OllamaRequest
            {
                model = model.Trim(),
                prompt = text,
                stream = false
            });

            UnityWebRequest request = new UnityWebRequest(endpoint.Trim(), "POST");
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = 300;

            var op = request.SendWebRequest();
            op.completed += _ =>
            {
                waiting = false;

                if (request.result != UnityWebRequest.Result.Success)
                {
                    responseText =
                        "OLLAMA CONNECTION FAILED\n\n" +
                        "Make sure Ollama is running and the model is installed.\n" +
                        "URL: " + endpoint + "\n" +
                        "Model: " + model + "\n\n" +
                        "HTTP: " + request.responseCode + "\n" +
                        request.error + "\n\n" +
                        request.downloadHandler.text;
                }
                else
                {
                    responseText = ExtractOllamaText(request.downloadHandler.text);
                    if (connectionTest && !responseText.StartsWith("OLLAMA"))
                        responseText = "OLLAMA CONNECTED ✅\n\nModel reply:\n" + responseText;
                }

                request.Dispose();
                Repaint();
            };
        }

        private static string ExtractOllamaText(string json)
        {
            try
            {
                OllamaResponse result = JsonUtility.FromJson<OllamaResponse>(json);

                if (result == null)
                    return "Ollama returned an empty response.";

                if (!string.IsNullOrEmpty(result.error))
                    return "OLLAMA ERROR: " + result.error;

                if (!string.IsNullOrEmpty(result.response))
                    return result.response;

                return "Ollama returned no readable text.\n\nRaw response:\n" + json;
            }
            catch (Exception ex)
            {
                return "Could not parse Ollama response: " + ex.Message + "\n\nRaw response:\n" + json;
            }
        }

        [Serializable]
        private class OllamaRequest
        {
            public string model;
            public string prompt;
            public bool stream;
        }

        [Serializable]
        private class OllamaResponse
        {
            public string model;
            public string response;
            public bool done;
            public string error;
        }
    }
}
