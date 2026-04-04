using System.Threading.Tasks;
using UnityEngine;


namespace susaplay.SDK
{
    public static class SusaPlaySDK
    {
        private static SDKConfig _config;
        private static TokenManager _tokenManager;
        private static HttpClient _httpClient;
        private static TaskCompletionSource<string> _initTcs;
        private static AuthModule _auth;
        public static AuthModule Auth => _auth;
        private static CloudSaveModule _cloudSave;
        public static CloudSaveModule CloudSave => _cloudSave;
        private static AnalyticsModule _analytics;
        private static AnalyticsFlusher _flusher;
        public static AnalyticsModule Analytics => _analytics;
        private static PurchasesModule _purchases;
        public static PurchasesModule Purchases => _purchases;
        private const string SdkVersion = "1.0.0";
        private const int InitTimeoutMs = 15000;

        public static async Task Initialize()
        {
            _config = SDKConfig.Load();
            if (_config == null)
            {
                Logger.Error("PlatformConfig asset not found. Run susaplay > Create Config Asset first.");
                return;
            }
            WebGLBridge.Initialize();
            _tokenManager = new TokenManager();
            _tokenManager.Initialize();
            _httpClient = new HttpClient(_config, _tokenManager);
            _initTcs = new TaskCompletionSource<string>();
            WebGLBridge.OnMessageReceived += HandleInitMessage;
            WebGLBridge.SendMessage(new BridgeMessage
            {
                type = "SDK_INIT",
                payload = "{\"gameKey\":\"" + _config.GameKey + "\",\"sdkVersion\":\"" + SdkVersion + "\"}"
            });

            var completed = await Task.WhenAny(_initTcs.Task, Task.Delay(InitTimeoutMs));
            if (completed != _initTcs.Task)
            {
                WebGLBridge.OnMessageReceived -= HandleInitMessage;
                Logger.Error("SusaPlay SDK init timed out waiting for SDK_READY.");
                return;
            }
            await _initTcs.Task;
        }

        private static void HandleInitMessage(string json)
        {
            var message = JsonUtility.FromJson<BridgeMessage>(json);
            if (message.type != "SDK_READY")
            {
                return;
            }

            PlayerData playerData = null;

            if (!string.IsNullOrEmpty(message.payload))
            {
                // Preferred envelope format: payload = {"mode":"...", "playerData": {...}}
                var envelopePayload = JsonUtility.FromJson<SdkReadyEnvelope>(message.payload);
                if (envelopePayload != null && envelopePayload.playerData != null)
                {
                    playerData = envelopePayload.playerData;
                    if (!string.IsNullOrEmpty(envelopePayload.mode))
                    {
                        playerData.mode = envelopePayload.mode;
                    }
                }
                else
                {
                    // Backward compatibility: payload may be raw PlayerData object.
                    playerData = JsonUtility.FromJson<PlayerData>(message.payload);
                }
            }

            // Backward compatibility: some shells send top-level mode/playerData without payload.
            if (playerData == null || string.IsNullOrEmpty(playerData.gameId))
            {
                var envelopeFlat = JsonUtility.FromJson<SdkReadyEnvelope>(json);
                if (envelopeFlat != null && envelopeFlat.playerData != null)
                {
                    playerData = envelopeFlat.playerData;
                    if (!string.IsNullOrEmpty(envelopeFlat.mode))
                    {
                        playerData.mode = envelopeFlat.mode;
                    }
                }
            }

            if (playerData == null)
            {
                playerData = new PlayerData();
            }

            _auth = new AuthModule();
            _auth.Initialize(playerData);
            _cloudSave = new CloudSaveModule(_httpClient, playerData.gameId);
            _analytics = new AnalyticsModule(_httpClient);
            _purchases = new PurchasesModule(playerData.gameId);
            _purchases.Initialize();
            var flusherGO = new GameObject("SusaPlayAnalyticsFlusher");
            GameObject.DontDestroyOnLoad(flusherGO);
            _flusher = flusherGO.AddComponent<AnalyticsFlusher>();
            _flusher.Initialize(_analytics);
            WebGLBridge.OnMessageReceived -= HandleInitMessage;
            Logger.Log("SusaPlay SDK Ready.");
            WebGLBridge.SendMessage(new BridgeMessage
            {
                type = "SDK_GAME_LOADED",
                payload = "{}"
            });
            Logger.Log("SusaPlay SDK Ready.");
            _initTcs.SetResult(message.payload);
        }
    }

    [System.Serializable]
    class SdkReadyEnvelope
    {
        public string mode;
        public PlayerData playerData;
    }
}
