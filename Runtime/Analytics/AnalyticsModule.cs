using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
namespace susaplay.SDK
{
    public class AnalyticsModule
    {
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
                sb.Append(JsonUtility.ToJson(eventsToSend[i]));
                if (i < eventsToSend.Count - 1) sb.Append(",");
            }
            sb.Append("]}");
            var response = await _httpClient.Post("/analytics/event", sb.ToString());
            if (!response.Success)
            {
                Logger.Warn("Failed to send analytics event: " + response.Error);
            }
        }
    }
}