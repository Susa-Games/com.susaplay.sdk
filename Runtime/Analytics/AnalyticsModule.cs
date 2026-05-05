using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
namespace susaplay.SDK
{
    public class AnalyticsModule
    {
        private HttpClient _httpClient;
        private string _gameId;
        private string _sessionId;
        private List<AnalyticsEvent> _eventQueue;
        public AnalyticsModule(HttpClient httpClient, string gameId = null, string sessionId = null)
        {
            _httpClient = httpClient;
            _gameId = gameId;
            _sessionId = sessionId;
            _eventQueue = new List<AnalyticsEvent>();
        }

        public void LogEvent(string name, string parameters = "{}")
        {
            _eventQueue.Add(new AnalyticsEvent
            {
                name = name,
                parameters = parameters,
                clientTimestamp = DateTime.UtcNow.ToString("o")
            });
            Logger.Log("Queued analytics event: " + name);

        }

        [Obsolete("Use SusaPlaySDK.Webhooks.SendEvent instead.")]
        public void LogB2BEvent(string payloadJson)
        {
            Logger.Warn("Analytics.LogB2BEvent is deprecated. Use SusaPlaySDK.Webhooks.SendEvent instead.");
            if (SusaPlaySDK.Webhooks == null)
            {
                Logger.Warn("Webhook module is not initialized. B2B event was not sent.");
                return;
            }
            SusaPlaySDK.Webhooks.SendEvent("b2b_event", payloadJson);
        }

        [Obsolete("Use SusaPlaySDK.Webhooks.SendEvent instead.")]
        public void LogB2BEvent(object payload)
        {
            Logger.Warn("Analytics.LogB2BEvent is deprecated. Use SusaPlaySDK.Webhooks.SendEvent instead.");
            if (SusaPlaySDK.Webhooks == null)
            {
                Logger.Warn("Webhook module is not initialized. B2B event was not sent.");
                return;
            }

            SusaPlaySDK.Webhooks.SendEvent("b2b_event", payload == null ? "{}" : JsonUtility.ToJson(payload));
        }

        public async Task Flush()
        {
            if (_eventQueue.Count == 0)
            {
                return;
            }
            var eventsToSend = new List<AnalyticsEvent>(_eventQueue);
            _eventQueue.Clear();
            var sb = new System.Text.StringBuilder();
            AppendAnalyticsBatchJson(sb, eventsToSend, _gameId, _sessionId);
#if UNITY_WEBGL && !UNITY_EDITOR
            WebGLBridge.SendMessage(new BridgeMessage
            {
                type = "SDK_LOG_EVENT",
                payload = sb.ToString()
            });
            await Task.CompletedTask;
#else
            var response = await _httpClient.Post("/analytics/event", sb.ToString());
            if (!response.Success)
            {
                Logger.Warn("Failed to send analytics event: " + response.Error);
            }
#endif
        }

        private static void AppendAnalyticsBatchJson(System.Text.StringBuilder sb, List<AnalyticsEvent> eventsToSend, string gameId, string sessionId)
        {
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
            sb.Append("\"events\":[");
            for (int i = 0; i < eventsToSend.Count; i++)
            {
                AppendEventJson(sb, eventsToSend[i]);
                if (i < eventsToSend.Count - 1) sb.Append(",");
            }
            sb.Append("]}");
        }

        private static void AppendEventJson(System.Text.StringBuilder sb, AnalyticsEvent analyticsEvent)
        {
            if (!analyticsEvent.parametersAsJsonObject)
            {
                sb.Append(JsonUtility.ToJson(analyticsEvent));
                return;
            }

            sb.Append("{\"name\":");
            AppendJsonString(sb, analyticsEvent.name);
            sb.Append(",\"parameters\":");
            sb.Append(analyticsEvent.parameters);
            sb.Append(",\"clientTimestamp\":");
            AppendJsonString(sb, analyticsEvent.clientTimestamp);
            sb.Append("}");
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
