using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace susaplay.SDK
{
    public class WebhooksModule
    {
        private readonly HttpClient _httpClient;
        private readonly string _gameId;
        private readonly string _sessionId;
        private readonly string _playerId;

        public WebhooksModule(HttpClient httpClient, string gameId = null, string sessionId = null, string playerId = null)
        {
            _httpClient = httpClient;
            _gameId = gameId;
            _sessionId = sessionId;
            _playerId = playerId;
        }

        public void SendEvent(string eventName, object value = null, Dictionary<string, object> parameters = null)
        {
            if (string.IsNullOrWhiteSpace(eventName))
            {
                Logger.Warn("Webhook event name is empty. Event was not sent.");
                return;
            }

            var payloadJson = BuildEventPayloadJson(eventName, value, parameters, _playerId);

#if UNITY_WEBGL && !UNITY_EDITOR
            var bridgePayload = BuildBridgePayload(eventName, payloadJson, DateTime.UtcNow.ToString("o"));
            WebGLBridge.SendMessage(new BridgeMessage
            {
                type = "SDK_CUSTOM_WEBHOOK_EVENT",
                payload = bridgePayload
            });
#else
            _ = SendPayloadJsonAsync(eventName, payloadJson);
#endif
        }

        public async Task<HttpResponse> SendEventAsync(string eventName, object value, Dictionary<string, object> parameters = null)
        {
            if (string.IsNullOrWhiteSpace(eventName))
            {
                return HttpResponse.Fail("Webhook event name is empty.");
            }

            var payloadJson = BuildEventPayloadJson(eventName, value, parameters, _playerId);
            return await SendPayloadJsonAsync(eventName, payloadJson);
        }

        private async Task<HttpResponse> SendPayloadJsonAsync(string eventName, string payloadJson)
        {
            var body = BuildDirectRequestBody(eventName, payloadJson, DateTime.UtcNow.ToString("o"), _gameId, _sessionId);
            return await _httpClient.Post("/webhooks/event", body);
        }

        private static string BuildBridgePayload(string eventName, string payloadJson, string clientTimestamp)
        {
            var sb = new StringBuilder();
            sb.Append("{\"eventName\":");
            AppendJsonString(sb, eventName);
            sb.Append(",\"payloadJson\":");
            AppendJsonString(sb, payloadJson);
            sb.Append(",\"clientTimestamp\":");
            AppendJsonString(sb, clientTimestamp);
            sb.Append("}");
            return sb.ToString();
        }

        private static string BuildEventPayloadJson(string eventName, object value, Dictionary<string, object> parameters, string playerId)
        {
            var sb = new StringBuilder(192);
            sb.Append("{\"playerID\":");
            AppendJsonValue(sb, playerId);
            sb.Append(",\"eventname\":");
            AppendJsonString(sb, eventName);
            sb.Append(",\"value\":");
            AppendJsonValue(sb, value);

            if (parameters != null && parameters.Count > 0)
            {
                sb.Append(",\"parameters\":");
                AppendObjectDictionary(sb, parameters);
            }

            sb.Append("}");
            return sb.ToString();
        }

        private static string BuildDirectRequestBody(string eventName, string payloadJson, string clientTimestamp, string gameId, string sessionId)
        {
            var sb = new StringBuilder();
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

        private static void AppendJsonValue(StringBuilder sb, object value)
        {
            if (value == null)
            {
                sb.Append("null");
                return;
            }

            switch (value)
            {
                case string s:
                    AppendJsonString(sb, s);
                    return;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    return;
                case int i:
                    sb.Append(i.ToString(CultureInfo.InvariantCulture));
                    return;
                case long l:
                    sb.Append(l.ToString(CultureInfo.InvariantCulture));
                    return;
                case float f:
                    sb.Append(f.ToString("R", CultureInfo.InvariantCulture));
                    return;
                case double d:
                    sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                    return;
                case decimal m:
                    sb.Append(m.ToString(CultureInfo.InvariantCulture));
                    return;
                case IDictionary<string, object> typedDictionary:
                    AppendObjectDictionary(sb, typedDictionary);
                    return;
                case IDictionary dictionary:
                    AppendDictionary(sb, dictionary);
                    return;
                case IEnumerable enumerable:
                    AppendEnumerable(sb, enumerable);
                    return;
                default:
                    var json = JsonUtility.ToJson(value);
                    if (!string.IsNullOrWhiteSpace(json) && json != "{}")
                    {
                        sb.Append(json);
                        return;
                    }

                    AppendJsonString(sb, value.ToString());
                    return;
            }
        }

        private static void AppendObjectDictionary(StringBuilder sb, IDictionary<string, object> dictionary)
        {
            sb.Append("{");
            var first = true;
            foreach (var kvp in dictionary)
            {
                if (string.IsNullOrEmpty(kvp.Key))
                    continue;

                if (!first)
                    sb.Append(",");
                first = false;

                AppendJsonString(sb, kvp.Key);
                sb.Append(":");
                AppendJsonValue(sb, kvp.Value);
            }
            sb.Append("}");
        }

        private static void AppendDictionary(StringBuilder sb, IDictionary dictionary)
        {
            sb.Append("{");
            var first = true;
            foreach (DictionaryEntry entry in dictionary)
            {
                if (entry.Key == null)
                    continue;

                if (!first)
                    sb.Append(",");
                first = false;

                AppendJsonString(sb, entry.Key.ToString());
                sb.Append(":");
                AppendJsonValue(sb, entry.Value);
            }
            sb.Append("}");
        }

        private static void AppendEnumerable(StringBuilder sb, IEnumerable enumerable)
        {
            sb.Append("[");
            var first = true;
            foreach (var item in enumerable)
            {
                if (!first)
                    sb.Append(",");
                first = false;

                AppendJsonValue(sb, item);
            }
            sb.Append("]");
        }

        private static void AppendJsonString(StringBuilder sb, string value)
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
