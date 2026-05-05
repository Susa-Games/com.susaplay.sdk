using UnityEngine;
namespace susaplay.SDK
{
    public class SDKConfig : ScriptableObject
    {
        private const string LiveUrl = "https://europe-west1-susaplay-2e8e3.cloudfunctions.net";
        private const string EmulatorUrl = "http://localhost:5001";
        private const float MinimumAnalyticsFlushIntervalSeconds = 10f;
        [SerializeField] private string _gameKey;
        [SerializeField] private bool _isEmulatorMode;
        [SerializeField] private string _liveUrlOverride;
        [SerializeField] private bool _automaticAnalyticsFlushEnabled = true;
        [SerializeField] private float _analyticsFlushIntervalSeconds = 300f;
        [SerializeField] private bool _flushAnalyticsOnInitialize = true;
        [SerializeField] private bool _flushAnalyticsOnPause = true;
        [SerializeField] private bool _flushAnalyticsOnQuit = true;
        public string GameKey => _gameKey;
        public bool AutomaticAnalyticsFlushEnabled => _automaticAnalyticsFlushEnabled;
        public float AnalyticsFlushIntervalSeconds => Mathf.Max(MinimumAnalyticsFlushIntervalSeconds, _analyticsFlushIntervalSeconds);
        public bool FlushAnalyticsOnInitialize => _flushAnalyticsOnInitialize;
        public bool FlushAnalyticsOnPause => _flushAnalyticsOnPause;
        public bool FlushAnalyticsOnQuit => _flushAnalyticsOnQuit;

        public string ApiBaseUrl
        {
            get
            {
                if (!_isEmulatorMode && !string.IsNullOrWhiteSpace(_liveUrlOverride))
                {
                    return _liveUrlOverride.TrimEnd('/');
                }
                return _isEmulatorMode ? EmulatorUrl : LiveUrl;
            }
        }

        public static SDKConfig Load()
        {
            return Resources.Load<SDKConfig>("PlatformConfig");
        }
        public void SetGameKey(string key)
        {
            _gameKey = key;
        }

        public void SetAutomaticAnalyticsFlushEnabled(bool enabled)
        {
            _automaticAnalyticsFlushEnabled = enabled;
        }

        public void SetAnalyticsFlushIntervalSeconds(float seconds)
        {
            _analyticsFlushIntervalSeconds = Mathf.Max(MinimumAnalyticsFlushIntervalSeconds, seconds);
        }

        public void SetAnalyticsLifecycleFlushes(bool onInitialize, bool onPause, bool onQuit)
        {
            _flushAnalyticsOnInitialize = onInitialize;
            _flushAnalyticsOnPause = onPause;
            _flushAnalyticsOnQuit = onQuit;
        }
    }
}
