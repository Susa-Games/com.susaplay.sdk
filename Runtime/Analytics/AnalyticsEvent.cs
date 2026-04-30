using System;
using UnityEngine;
namespace susaplay.SDK
{
    [Serializable]
    public class AnalyticsEvent
    {
        public string name;
        public string parameters;
        public string clientTimestamp;
        [NonSerialized] public bool parametersAsJsonObject;

    }
}
