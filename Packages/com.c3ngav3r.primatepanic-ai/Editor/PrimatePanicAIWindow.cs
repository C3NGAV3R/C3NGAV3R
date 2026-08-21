using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace C3NGAV3R.PrimatePanicAI
{
    public class PrimatePanicAIWindow : EditorWindow
    {
        private const string ApiKeyPref = "PrimatePanicAI.ApiKey";
        private const string ModelPref = "PrimatePanicAI.Model";
        private const string Endpoint = "https://api.openai.com/v1/responses";

        private string apiKey = "";
        private string model = "gpt-5.6-luna";
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
            apiKey = EditorPrefs.GetString(ApiKeyPref, "");
            model = EditorPrefs.GetString(ModelPref, "gpt-5.6-luna");
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Primate Panic AI", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Uses your OpenAI API key. Your normal ChatGPT login is not used by this Unity package.", MessageType.Info);

            EditorGUI.BeginChangeCheck();
            apiKey = EditorGUILayout.PasswordField("OpenAI API Key", apiKey);
            model = EditorGUILayout.TextField("Model", model);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetString(ApiKeyPref, apiKey);
                EditorPrefs.SetString(ModelPref, model);
            }

            EditorGUILayout.Space(8);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Inspect Selected GameObject", GUILayout.Height(28)))
                {
                    prompt = BuildSelectedObjectPrompt();
                }

                if (GUILayout.Button("Clear", GUILayout.Height(28)))
                {
                    prompt = "";
                    responseText = "";
                }
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Ask", EditorStyles.boldLabel);
            prompt = EditorGUILayout.TextArea(prompt, GUILayout.MinHeight(100));

            GUI.enabled = !waiting && !string.IsNullOrWhiteSpace(apiKey) && !string.IsNullOrWhiteSpace(prompt);
            if (GUILayout.Button(waiting ? "Thinking..." : "Send to AI", GUILayout.Height(34)))
            {
                SendRequest();
            }
            GUI.enabled = true;

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Answer", EditorStyles.boldLabel);
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MinHeight(180));
            EditorGUILayout.TextArea(responseText, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private string BuildSelectedObjectPrompt()
        {
            GameObject go = Selection.activeGameObject;
            if (go == null)
                return "Help me debug my Unity scene. I do not currently have a GameObject selected.";

            var sb = new StringBuilder();
            sb.AppendLine("Help me debug this Unity GameObject in a VR Gorilla-style project.");
            sb.AppendLine("Selected GameObject: " + go.name);
            sb.AppendLine("Active in hierarchy: " + go.activeInHierarchy);
            sb.AppendLine("Layer: " + LayerMask.LayerToName(go.layer));
            sb.AppendLine("Tag: " + go.tag);
            sb.AppendLine("Transform local position: " + go.transform.localPosition);
            sb.AppendLine("Transform local rotation: " + go.transform.localEulerAngles);
            sb.AppendLine("Transform local scale: " + go.transform.localScale);
            sb.AppendLine("Components:");

            foreach (Component c in go.GetComponents<Component>())
            {
                if (c == null) continue;
                sb.AppendLine("- " + c.GetType().FullName);

                if (c is Rigidbody rb)
                {
                    sb.AppendLine("  Rigidbody mass=" + rb.mass + ", useGravity=" + rb.useGravity + ", isKinematic=" + rb.isKinematic + ", constraints=" + rb.constraints);
                }
                else if (c is Collider col)
                {
                    sb.AppendLine("  Collider enabled=" + col.enabled + ", isTrigger=" + col.isTrigger);
                }
                else if (c is Animator animator)
                {
                    sb.AppendLine("  Animator enabled=" + animator.enabled + ", applyRootMotion=" + animator.applyRootMotion);
                }
            }

            sb.AppendLine();
            sb.AppendLine("Tell me what looks wrong and give exact Unity steps to fix it. Do not assume files or settings that are not listed.");
            return sb.ToString();
        }

        private void SendRequest()
        {
            waiting = true;
            responseText = "Sending request...";
            Repaint();

            string json = JsonUtility.ToJson(new ResponseRequest
            {
                model = model,
                input = prompt
            });

            UnityWebRequest request = new UnityWebRequest(Endpoint, "POST");
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + apiKey.Trim());

            var op = request.SendWebRequest();
            op.completed += _ =>
            {
                waiting = false;

                if (request.result != UnityWebRequest.Result.Success)
                {
                    responseText = "Request failed: " + request.responseCode + "\n" + request.error + "\n\n" + request.downloadHandler.text;
                }
                else
                {
                    responseText = ExtractOutputText(request.downloadHandler.text);
                }

                request.Dispose();
                Repaint();
            };
        }

        private static string ExtractOutputText(string json)
        {
            try
            {
                ResponseEnvelope envelope = JsonUtility.FromJson<ResponseEnvelope>(json);
                if (envelope != null && envelope.output != null)
                {
                    foreach (ResponseOutputItem item in envelope.output)
                    {
                        if (item == null || item.content == null) continue;
                        foreach (ResponseContent content in item.content)
                        {
                            if (content != null && !string.IsNullOrEmpty(content.text))
                                return content.text;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return "Could not parse the AI response: " + ex.Message + "\n\nRaw response:\n" + json;
            }

            return "The API returned no readable text. Raw response:\n" + json;
        }

        [Serializable]
        private class ResponseRequest
        {
            public string model;
            public string input;
        }

        [Serializable]
        private class ResponseEnvelope
        {
            public ResponseOutputItem[] output;
        }

        [Serializable]
        private class ResponseOutputItem
        {
            public string type;
            public ResponseContent[] content;
        }

        [Serializable]
        private class ResponseContent
        {
            public string type;
            public string text;
        }
    }
}
