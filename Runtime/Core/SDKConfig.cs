using UnityEngine;
namespace susaplay.SDK
{
    public class SDKConfig : ScriptableObject
    {
        private const string LiveUrl = "https://europe-west1-susaplay-2e8e3.cloudfunctions.net";
        private const string EmulatorUrl = "http://localhost:5001";
        [SerializeField] private string _gameKey;
        [SerializeField] private bool _isEmulatorMode;
        [SerializeField] private string _liveUrlOverride;
        public string GameKey => _gameKey;

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
    }
}
