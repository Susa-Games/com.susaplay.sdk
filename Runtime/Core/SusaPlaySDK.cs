using System.Threading.Tasks;
using UnityEngine;


namespace susaplay.SDK
{
    public static class SusaPlaySDK
    {
        private static SDKConfig _config;
        private static TokenManager _tokenManager;
        private static AppCheckManager _appCheckManager;
        private static HttpClient _httpClient;
        private static TaskCompletionSource<string> _initTcs;
        private static AuthModule _auth;
        public static AuthModule Auth => _auth;
        private static CloudSaveModule _cloudSave;
        public static CloudSaveModule CloudSave => _cloudSave;
        private static AchievementsModule _achievements;
        public static AchievementsModule Achievements => _achievements;
        private static AnalyticsModule _analytics;
        private static AnalyticsFlusher _flusher;
        public static AnalyticsModule Analytics => _analytics;
        private static WebhooksModule _webhooks;
        public static WebhooksModule Webhooks => _webhooks;
        private static PurchasesModule _purchases;
        public static PurchasesModule Purchases => _purchases;
        private static AdsModule _ads;

        /// <summary>Rewarded and interstitial ads, shown by the page. See <see cref="AdsModule"/>.</summary>
        public static AdsModule Ads => _ads;
        private static ApiModule _api;
        public static ApiModule Api => _api;
        private static BackendModule _backend;

        /// <summary>Authenticated calls to the platform's own API. See <see cref="BackendModule"/>.</summary>
        public static BackendModule Backend => _backend;
        private static LiveOpsModule _liveOps;
        public static LiveOpsModule LiveOps => _liveOps;
        /// <summary>Canonical platform game id for this build. Empty until initialization completes.</summary>
        public static string GameId { get; private set; } = "";

        private const string SdkVersion = "1.8.0";
        private const int InitTimeoutMs = 15000;
        private static bool _isInitialized;
        private static bool _didSendGameLoaded;
        private static Task _initTask;

        /// <summary>
        /// Brings the SDK up. Safe to call from more than one place.
        ///
        /// `_isInitialized` is only set once SDK_READY has come back, so two
        /// components calling this in the same frame both saw it false and both
        /// ran the whole body: the bridge handler was subscribed twice, the
        /// second run replaced `_initTcs`, and SDK_READY then completed the same
        /// source twice and threw out of a native callback — which surfaces as
        /// Unity's fatal "error running the Unity content on this page" rather
        /// than as a normal exception. The first caller, meanwhile, awaited a
        /// source nothing would ever complete.
        ///
        /// Handing every caller the same Task is what makes a second call free
        /// instead of destructive. A failed run clears the field so a retry can
        /// still happen.
        /// </summary>
        public static Task Initialize()
        {
            if (_initTask != null)
            {
                return _initTask;
            }

            _initTask = InitializeInternal();
            return _initTask;
        }

        private static async Task InitializeInternal()
        {
            _config = SDKConfig.Load();
            if (_config == null)
            {
                Logger.Error("PlatformConfig asset not found. Run susaplay > Create Config Asset first.");
                _initTask = null;
                return;
            }
            WebGLBridge.Initialize();
            _tokenManager = new TokenManager();
            _tokenManager.Initialize();
            _appCheckManager = new AppCheckManager();
            _appCheckManager.Initialize();
            _httpClient = new HttpClient(_config, _tokenManager, _appCheckManager);
            _initTcs = new TaskCompletionSource<string>();
            // Unsubscribe first, the same way TokenManager.Initialize does: a
            // handler left over from an earlier attempt would be invoked again
            // and complete this source a second time.
            WebGLBridge.OnMessageReceived -= HandleInitMessage;
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
                // Dropped so a later call can try again rather than being handed
                // this failed run for the lifetime of the process.
                _initTask = null;
                return;
            }
            await _initTcs.Task;
        }

        public static void MarkGameLoaded()
        {
            if (!_isInitialized)
            {
                Logger.Error("SusaPlay SDK must be initialized before MarkGameLoaded.");
                return;
            }

            if (_didSendGameLoaded)
            {
                return;
            }

            WebGLBridge.SendMessage(new BridgeMessage
            {
                type = "SDK_GAME_LOADED",
                payload = "{}"
            });
            _didSendGameLoaded = true;
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
            _auth.Initialize(playerData, _tokenManager);
            _cloudSave = new CloudSaveModule(_httpClient, playerData.gameId);
            _achievements = new AchievementsModule(_httpClient, playerData.gameId);
            _analytics = new AnalyticsModule(_httpClient, playerData.gameId, playerData.sessionId);
            // Emit session_start automatically. Nothing else in the SDK queues an event, so
            // without this a game that never calls LogEvent produces no analytics at all and
            // DAU/MAU/retention stay empty for it. Queued here, shipped by the flusher below
            // when FlushAnalyticsOnInitialize is on (the default).
            _analytics.LogEvent("session_start");
            _webhooks = new WebhooksModule(_httpClient, playerData.gameId, playerData.sessionId, playerData.PlayerIdOrUid());
            _purchases = new PurchasesModule(_httpClient, playerData.gameId);
            _purchases.Initialize();
            _ads = new AdsModule();
            _ads.Initialize();
            _api = new ApiModule();
            _backend = new BackendModule(_httpClient);
            _liveOps = new LiveOpsModule(_config.LiveOpsContentBaseUrl, playerData.gameId);

            // Surfaced so a game can attribute its own platform calls without hardcoding an id.
            // This is the canonical id the platform sent with SDK_READY, not a value the build
            // guessed, which is why it is worth exposing rather than letting each game invent one.
            GameId = playerData.gameId;
            if (_config.AutomaticAnalyticsFlushEnabled)
            {
                var flusherGO = new GameObject("SusaPlayAnalyticsFlusher");
                GameObject.DontDestroyOnLoad(flusherGO);
                _flusher = flusherGO.AddComponent<AnalyticsFlusher>();
                _flusher.Initialize(
                    _analytics,
                    _config.AnalyticsFlushIntervalSeconds,
                    _config.FlushAnalyticsOnInitialize,
                    _config.FlushAnalyticsOnPause,
                    _config.FlushAnalyticsOnQuit);
            }
            WebGLBridge.OnMessageReceived -= HandleInitMessage;
            Logger.Log("SusaPlay SDK Ready.");
            _isInitialized = true;
            // TrySetResult, not SetResult: this runs inside a callback from the
            // browser, and throwing here takes down the whole Unity instance
            // rather than failing one call.
            _initTcs.TrySetResult(message.payload);
        }
    }

    [System.Serializable]
    class SdkReadyEnvelope
    {
        public string mode;
        public PlayerData playerData;
    }
}
