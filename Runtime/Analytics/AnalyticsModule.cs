using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
namespace susaplay.SDK
{
    public class AnalyticsModule
    {
        private const string B2BEventName = "b2b_webhook";
        private HttpClient _httpClient;
        private List<AnalyticsEvent> _eventQueue;
        public AnalyticsModule(HttpClient httpClient)
        {
            _httpClient = httpClient;
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

        public void LogB2BEvent(string payloadJson)
        {
            if (string.IsNullOrWhiteSpace(payloadJson))
            {
                Logger.Warn("B2B analytics payload is empty. Queuing an empty JSON object.");
                payloadJson = "{}";
            }

            _eventQueue.Add(new AnalyticsEvent
            {
                name = B2BEventName,
                parameters = payloadJson,
                clientTimestamp = DateTime.UtcNow.ToString("o"),
                parametersAsJsonObject = true
            });
            Logger.Log("Queued B2B analytics event.");
        }

        public void LogB2BEvent(object payload)
        {
            if (payload == null)
            {
                LogB2BEvent("{}");
                return;
            }

            LogB2BEvent(JsonUtility.ToJson(payload));
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
            sb.Append("{\"events\":[");
            for (int i = 0; i < eventsToSend.Count; i++)
            {
                AppendEventJson(sb, eventsToSend[i]);
                if (i < eventsToSend.Count - 1) sb.Append(",");
            }
            sb.Append("]}");
            var response = await _httpClient.Post("/analytics/event", sb.ToString());
            if (!response.Success)
            {
                Logger.Warn("Failed to send analytics event: " + response.Error);
            }
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
