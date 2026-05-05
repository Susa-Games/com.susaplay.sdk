using System;
using System.Threading.Tasks;
using UnityEngine;

namespace susaplay.SDK
{
    public class WebhooksModule
    {
        private readonly HttpClient _httpClient;
        private readonly string _gameId;
        private readonly string _sessionId;

        public WebhooksModule(HttpClient httpClient, string gameId = null, string sessionId = null)
        {
            _httpClient = httpClient;
            _gameId = gameId;
            _sessionId = sessionId;
        }

        public void SendEvent(string eventName, string payloadJson = "{}")
        {
            if (string.IsNullOrWhiteSpace(eventName))
            {
                Logger.Warn("Webhook event name is empty. Event was not sent.");
                return;
            }

            if (string.IsNullOrWhiteSpace(payloadJson))
            {
                payloadJson = "{}";
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            var bridgePayload = BuildBridgePayload(eventName, payloadJson, DateTime.UtcNow.ToString("o"));
            WebGLBridge.SendMessage(new BridgeMessage
            {
                type = "SDK_B2B_EVENT",
                payload = bridgePayload
            });
#else
            _ = SendEventAsync(eventName, payloadJson);
#endif
        }

        public void SendEvent(string eventName, object payload)
        {
            SendEvent(eventName, payload == null ? "{}" : JsonUtility.ToJson(payload));
        }

        public async Task<HttpResponse> SendEventAsync(string eventName, string payloadJson = "{}")
        {
            if (string.IsNullOrWhiteSpace(eventName))
            {
                return HttpResponse.Fail("Webhook event name is empty.");
            }

            if (string.IsNullOrWhiteSpace(payloadJson))
            {
                payloadJson = "{}";
            }

            var body = BuildDirectRequestBody(eventName, payloadJson, DateTime.UtcNow.ToString("o"), _gameId, _sessionId);
            return await _httpClient.Post("/webhooks/event", body);
        }

        public Task<HttpResponse> SendEventAsync(string eventName, object payload)
        {
            return SendEventAsync(eventName, payload == null ? "{}" : JsonUtility.ToJson(payload));
        }

        private static string BuildBridgePayload(string eventName, string payloadJson, string clientTimestamp)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("{\"eventName\":");
            AppendJsonString(sb, eventName);
            sb.Append(",\"payloadJson\":");
            AppendJsonString(sb, payloadJson);
            sb.Append(",\"clientTimestamp\":");
            AppendJsonString(sb, clientTimestamp);
            sb.Append("}");
            return sb.ToString();
        }

        private static string BuildDirectRequestBody(string eventName, string payloadJson, string clientTimestamp, string gameId, string sessionId)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("{");
            if (!string.IsNullOrEmpty(gameId))
            {
                sb.Append("\"gameId\":");
                AppendJsonString(sb, gameId);
                sb.Append(",");
            }
            if (!string.IsNullOrEmpty(sessionId))
            {
                sb.Append("\"sessionId\":");
                AppendJsonString(sb, sessionId);
                sb.Append(",");
            }
            sb.Append("\"eventName\":");
            AppendJsonString(sb, eventName);
            sb.Append(",\"payload\":");
            sb.Append(payloadJson);
            sb.Append(",\"clientTimestamp\":");
            AppendJsonString(sb, clientTimestamp);
            sb.Append("}");
            return sb.ToString();
        }

        private static void AppendJsonString(System.Text.StringBuilder sb, string value)
        {
            sb.Append("\"");
            if (!string.IsNullOrEmpty(value))
            {
                for (var i = 0; i < value.Length; i++)
                {
                    var c = value[i];
                    switch (c)
                    {
                        case '\\':
                            sb.Append("\\\\");
                            break;
                        case '"':
                            sb.Append("\\\"");
                            break;
                        case '\n':
                            sb.Append("\\n");
                            break;
                        case '\r':
                            sb.Append("\\r");
                            break;
                        case '\t':
                            sb.Append("\\t");
                            break;
                        default:
                            sb.Append(c);
                            break;
                    }
                }
            }
            sb.Append("\"");
        }
    }
}
