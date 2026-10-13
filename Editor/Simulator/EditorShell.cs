using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace susaplay.SDK.Editor.Simulator
{
    /// <summary>A purchase or an ad waiting for the developer's answer.</summary>
    internal sealed class PendingRequest
    {
        public string RequestId;
        /// <summary>"purchase" or "ad".</summary>
        public string Kind;
        /// <summary>True while a guest's purchase waits for sign-in, before checkout opens.</summary>
        public bool AwaitingSignIn;
        public string Intent;
        public string GameId;
        public string ItemId;
        public string TopupPackId;
        public string AdType;
        public double StartedAt;
        /// <summary>True once checkout opened; only then does the shell know the wallet scope.</summary>
        public bool CheckoutOpened;

        /// <summary>The SDK gives up on a purchase or an ad after 180 seconds.</summary>
        public const double SdkTimeoutSeconds = 180;

        public string Describe()
        {
            if (Kind == "ad") return AdType + " ad";
            var target = Intent == "wallet_topup" ? "top-up " + TopupPackId : "item " + (ItemId ?? "(none)");
            return (AwaitingSignIn ? "Sign-in for purchase of " : "Purchase of ") + target;
        }
    }

    internal enum InjectedFailure
    {
        Unauthenticated,
        Internal,
        Network,
    }

    /// <summary>
    /// "Next request fails": applies once, to the next request matching <see cref="Route"/>.
    /// Without a server nothing ever fails, and a game's error handling still has to be tested.
    /// </summary>
    internal sealed class FailureInjection
    {
        /// <summary>"POST /save/write", or null for any route.</summary>
        public string Route;
        public InjectedFailure Failure;

        public bool Matches(string route)
        {
            return Route == null || Route == route;
        }

        public string Describe()
        {
            var what = Failure == InjectedFailure.Unauthenticated ? "401 UNAUTHENTICATED"
                : Failure == InjectedFailure.Internal ? "500 INTERNAL"
                : "a network error";
            return "The next " + (Route ?? "request") + " fails with " + what;
        }
    }

    /// <summary>
    /// The browser shell's half of the bridge, played in the Editor: what the player portal's play
    /// page answers to each message the SDK sends (Appendix A of the plan). Where the two browser
    /// shells differ, it follows the play page.
    ///
    /// Answers are never delivered inside the call that caused them. postMessage is always
    /// asynchronous, and the SDK was written for that; an answer delivered inline would let code
    /// that only works by accident pass here and fail in production. Everything is queued and
    /// delivered by <see cref="Pump"/>, which the Editor calls on the main thread every update.
    /// </summary>
    internal sealed class EditorShell
    {
        /// <summary>How long an HTTP answer takes, so code that assumes an instant answer fails here too.</summary>
        public const double HttpLatencySeconds = 0.1;

        private readonly LocalBackend _backend;
        private readonly Func<SimulatorState> _state;
        private readonly Func<SimulatorConfig> _config;
        private readonly Func<SimulatorSettings> _settings;
        private readonly RequestLog _log;
        private readonly List<(double due, Action action)> _queue = new List<(double, Action)>();
        private readonly List<PendingRequest> _pending = new List<PendingRequest>();
        private double _now;

        public EditorShell(LocalBackend backend, Func<SimulatorState> state, Func<SimulatorConfig> config,
            Func<SimulatorSettings> settings, RequestLog log)
        {
            _backend = backend;
            _state = state;
            _config = config;
            _settings = settings;
            _log = log;
        }

        /// <summary>Hands a message to the SDK. Set to WebGLBridge.DeliverFromEditor.</summary>
        public Action<string> Deliver;

        /// <summary>Called at SDK_INIT, before the session is answered; reloads the configuration.</summary>
        public Action BeforeSession;

        public event Action Changed;

        public bool InSession { get; private set; }
        public bool IsGuest { get; private set; }
        public string SessionGameId { get; private set; }
        public string SessionId { get; private set; }
        public string SdkGameKey { get; private set; }
        public IReadOnlyList<PendingRequest> Pending => _pending;

        /// <summary>The Editor time the shell last pumped at, for the pending countdowns.</summary>
        public double Now => _now;

        /// <summary>Set by the window; cleared when it fires or the developer cancels it.</summary>
        public FailureInjection NextFailure { get; set; }

        // ── Inbound: the SDK's side ─────────────────────────────────────────────────────────

        /// <summary>Every message the SDK sends. Set as WebGLBridge.EditorOutbound.</summary>
        public void OnOutbound(string json)
        {
            var message = SimJson.TryParse(json) as Dictionary<string, object>;
            var type = message != null && message.TryGetValue("type", out var typeValue) ? typeValue as string : null;
            var payload = ReadPayload(message);
            _log.Add(new RequestLogEntry { Time = DateTime.UtcNow, Name = "→ " + (type ?? "(unreadable message)"), Detail = json });

            switch (type)
            {
                case "SDK_INIT":
                    StartSession(Text(payload, "gameKey"));
                    break;
                case "SDK_GET_TOKEN":
                    Send("SDK_TOKEN_RESPONSE", SimJson.Obj(
                        "requestId", Text(payload, "requestId"),
                        "token", IsGuest ? null : LocalBackend.TokenFor(_state().uid)));
                    break;
                case "SDK_GET_APP_CHECK_TOKEN":
                    // The play page has no App Check yet and answers null at once; silence would
                    // cost the SDK an 8-second timeout per request.
                    Send("SDK_APP_CHECK_TOKEN_RESPONSE", SimJson.Obj("requestId", Text(payload, "requestId"), "token", null));
                    break;
                case "SDK_API_REQUEST":
                    // The partner API exists only inside a partner's embed.
                    Send("SDK_API_RESPONSE", SimJson.Obj(
                        "requestId", Text(payload, "requestId"),
                        "success", false,
                        "data", null,
                        "error", SimJson.Obj("code", "UNAVAILABLE", "message", "Partner API is only available for partner embeds")));
                    break;
                case "SDK_PURCHASE":
                    StartPurchase(
                        Text(payload, "requestId"),
                        Text(payload, "intent"),
                        Text(payload, "gameId"),
                        Text(payload, "itemId"),
                        Text(payload, "topupPackId"));
                    break;
                case "SDK_AD_SHOW":
                    StartAd(Text(payload, "requestId"), Text(payload, "adType"));
                    break;
                case "SDK_GAME_LOADED":
                    break;
                case "SDK_LOG_EVENT":
                case "SDK_CUSTOM_WEBHOOK_EVENT":
                    // In the Editor these modules use HTTP; the bridge path is WebGL-only.
                    Debug.LogWarning("[SusaPlay Simulator] Received " + type + ", which the SDK only sends in a WebGL build. It was not processed.");
                    break;
                default:
                    // The play page ignores a message it does not know, and so does this.
                    break;
            }
        }

        /// <summary>Every HTTP request the SDK makes. Set as HttpClient.EditorTransport.</summary>
        public Task<EditorHttpAnswer> OnHttp(EditorHttpRequest request)
        {
            var completion = new TaskCompletionSource<EditorHttpAnswer>(TaskCreationOptions.RunContinuationsAsynchronously);
            var started = DateTime.UtcNow;
            Schedule(HttpLatencySeconds, () =>
            {
                var name = request.Method + " " + PathOf(request.Endpoint);
                if (NextFailure != null && NextFailure.Matches(name))
                {
                    var injected = NextFailure;
                    NextFailure = null;
                    completion.TrySetResult(Inject(injected, name, started, request.Body));
                    Changed?.Invoke();
                    return;
                }

                BackendResponse response;
                try
                {
                    response = _backend.Handle(request.Method, request.Endpoint, request.Body, request.Authorization);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    response = new BackendResponse
                    {
                        Status = 500,
                        ErrorCode = "INTERNAL",
                        ErrorMessage = "Internal server error",
                        Body = SimJson.Obj("success", false, "error", SimJson.Obj("code", "INTERNAL", "message", "Internal server error")),
                    };
                }

                _log.Add(new RequestLogEntry
                {
                    Time = started,
                    Name = name,
                    Status = response.Status,
                    DurationMs = (DateTime.UtcNow - started).TotalMilliseconds,
                    ErrorCode = response.ErrorCode,
                    ErrorMessage = response.ErrorMessage,
                    IsError = !response.Succeeded || response.Warnings.Count > 0,
                    Detail = request.Body,
                });
                if (!response.Succeeded)
                {
                    Debug.LogWarning("[SusaPlay Simulator] " + name + " → " + response.Status + " " + response.ErrorCode + ": " + response.ErrorMessage);
                }
                foreach (var warning in response.Warnings)
                {
                    Debug.LogWarning("[SusaPlay Simulator] " + name + ": " + warning);
                }

                completion.TrySetResult(new EditorHttpAnswer { StatusCode = response.Status, Body = response.BodyText });
            });
            return completion.Task;
        }

        private EditorHttpAnswer Inject(FailureInjection injection, string name, DateTime started, string body)
        {
            var entry = new RequestLogEntry
            {
                Time = started,
                Name = name + " (injected)",
                DurationMs = (DateTime.UtcNow - started).TotalMilliseconds,
                IsError = true,
                Detail = body,
            };
            EditorHttpAnswer answer;
            switch (injection.Failure)
            {
                case InjectedFailure.Unauthenticated:
                    entry.Status = 401;
                    entry.ErrorCode = "UNAUTHENTICATED";
                    entry.ErrorMessage = "Token expired";
                    answer = ErrorAnswer(401, entry.ErrorCode, entry.ErrorMessage);
                    break;
                case InjectedFailure.Internal:
                    entry.Status = 500;
                    entry.ErrorCode = "INTERNAL";
                    entry.ErrorMessage = "Internal server error";
                    answer = ErrorAnswer(500, entry.ErrorCode, entry.ErrorMessage);
                    break;
                default:
                    // What UnityWebRequest reports when the request never gets a response.
                    entry.ErrorMessage = "Cannot connect to destination host";
                    answer = new EditorHttpAnswer { NetworkError = entry.ErrorMessage };
                    break;
            }
            _log.Add(entry);
            Debug.LogWarning("[SusaPlay Simulator] " + name + " failed on purpose (injected): " +
                             (entry.ErrorCode ?? "network error") + " — " + entry.ErrorMessage);
            return answer;
        }

        private static EditorHttpAnswer ErrorAnswer(long status, string code, string message)
        {
            return new EditorHttpAnswer
            {
                StatusCode = status,
                Body = SimJson.SerializePython(SimJson.Obj("success", false, "error", SimJson.Obj("code", code, "message", message))),
            };
        }

        // ── Session ─────────────────────────────────────────────────────────────────────────

        private void StartSession(string gameKey)
        {
            BeforeSession?.Invoke();
            var config = _config();
            SdkGameKey = gameKey ?? "";
            if (config.Loaded && !string.IsNullOrEmpty(config.GameKey) && config.GameKey != SdkGameKey)
            {
                Debug.LogWarning("[SusaPlay Simulator] The project's gameKey \"" + SdkGameKey + "\" does not match the imported configuration, " +
                                 "which is for \"" + config.GameName + "\" (" + config.GameKey + "). One of them is wrong; the configuration is still used.");
            }

            // Without a configuration the game is known only by its key — the shells fall back
            // the same way when player-init fails.
            SessionGameId = config.Loaded ? config.GameId : SdkGameKey;
            SessionId = Guid.NewGuid().ToString();
            IsGuest = _settings().guest;
            InSession = true;
            _backend.SessionGameId = SessionGameId;

            Send("SDK_READY", ReadyPayload());
            Changed?.Invoke();
        }

        private Dictionary<string, object> ReadyPayload()
        {
            if (IsGuest)
            {
                return SimJson.Obj(
                    "mode", "guest",
                    "playerData", SimJson.Obj("mode", "guest", "gameId", SessionGameId, "sessionId", SessionId));
            }
            return SimJson.Obj("mode", "authenticated", "playerData", PlayerData());
        }

        private Dictionary<string, object> PlayerData()
        {
            var state = _state();
            return SimJson.Obj(
                "mode", "authenticated",
                "uid", state.uid,
                "displayName", state.displayName,
                "avatarUrl", "",
                "isAnonymous", false,
                "gameId", SessionGameId,
                "sessionId", SessionId);
        }

        /// <summary>
        /// Signs the guest in mid-session, as the shell does after its sign-in prompt. The SDK
        /// reads SDK_AUTH_COMPLETE's payload as the new PlayerData, so that is what is sent — the
        /// browser shells send {token, uid} today, which the SDK cannot read (plan S5).
        /// </summary>
        public void SignIn()
        {
            if (!InSession || !IsGuest)
            {
                return;
            }
            IsGuest = false;
            var auth = PlayerData();
            auth["token"] = LocalBackend.TokenFor(_state().uid);
            Send("SDK_AUTH_COMPLETE", auth);
            Changed?.Invoke();
        }

        /// <summary>Called when Play Mode ends: pending purchases and ads are dropped with the session.</summary>
        public void EndSession()
        {
            _queue.Clear();
            _pending.Clear();
            InSession = false;
            IsGuest = false;
            SessionGameId = null;
            SessionId = null;
            _backend.SessionGameId = null;
            Changed?.Invoke();
        }

        // ── Checkout (packages/payments/src/usePurchaseFlow.ts) ────────────────────────────

        private void StartPurchase(string requestId, string intent, string gameId, string itemId, string topupPackId)
        {
            var pending = new PendingRequest
            {
                RequestId = requestId,
                Kind = "purchase",
                Intent = string.IsNullOrEmpty(intent) ? "direct_item" : intent,
                GameId = string.IsNullOrEmpty(gameId) ? SessionGameId : gameId,
                ItemId = itemId,
                TopupPackId = topupPackId,
                StartedAt = _now,
            };

            if (IsGuest)
            {
                // Held, not refused: the purchase resumes once the player signs in.
                pending.AwaitingSignIn = true;
                AddPending(pending);
                return;
            }
            OpenCheckout(pending);
        }

        private void OpenCheckout(PendingRequest pending)
        {
            var failure = ValidateCheckout(pending);
            if (failure != null)
            {
                // What the shells answer when create-payment-token refuses.
                RespondPurchase(pending, false, "token-failed", "UPSTREAM_ERROR", failure);
                return;
            }

            pending.CheckoutOpened = true;
            var outcome = _settings().purchaseOutcome;
            if (outcome == PurchaseOutcome.Ask)
            {
                pending.AwaitingSignIn = false;
                AddPending(pending);
                return;
            }
            FinishPurchase(pending, outcome);
        }

        /// <summary>create-payment-token's checks, with its messages; null when checkout can open.</summary>
        private string ValidateCheckout(PendingRequest pending)
        {
            var config = _config();
            if (pending.Intent == "direct_item")
            {
                if (string.IsNullOrEmpty(pending.ItemId))
                {
                    // Without an item id the platform picks the game's only item, if it has one.
                    var candidates = config.Items.Where(p => p.Value is Dictionary<string, object>).Select(p => p.Key).ToList();
                    if (candidates.Count != 1)
                    {
                        return "Missing required field: itemId";
                    }
                    pending.ItemId = candidates[0];
                }
                if (!(config.Items.TryGetValue(pending.ItemId, out var value) && value is Dictionary<string, object> item))
                {
                    return "Item not found in economy config";
                }
                // Refused before checkout, as create-payment-token does since plan S8.
                var inventory = _state().FindInventory(pending.GameId, false);
                if (LocalBackend.IsDurable(item) && inventory != null && inventory.Count(inventory.items, pending.ItemId) > 0)
                {
                    return "Durable item is already owned";
                }
                return null;
            }

            if (string.IsNullOrEmpty(pending.TopupPackId))
            {
                return "Missing required field: topupPackId";
            }
            return FindPack(pending.TopupPackId) != null ? null : "Top-up pack not found";
        }

        private Dictionary<string, object> FindPack(string topupPackId)
        {
            return _config().TopupPacks().FirstOrDefault(p => p.TryGetValue("topupPackId", out var id) && id as string == topupPackId);
        }

        /// <summary>Answers a pending purchase with the developer's choice.</summary>
        public void AnswerPurchase(PendingRequest pending, PurchaseOutcome outcome)
        {
            if (!_pending.Remove(pending) || outcome == PurchaseOutcome.Ask)
            {
                return;
            }
            FinishPurchase(pending, outcome);
            Changed?.Invoke();
        }

        /// <summary>The guest signs in from the prompt; the held purchase carries on to checkout.</summary>
        public void SignInAndContinue(PendingRequest pending)
        {
            if (!_pending.Remove(pending))
            {
                return;
            }
            SignIn();
            OpenCheckout(pending);
            Changed?.Invoke();
        }

        /// <summary>The guest dismisses the sign-in prompt the purchase needed.</summary>
        public void DismissSignIn(PendingRequest pending)
        {
            if (!_pending.Remove(pending))
            {
                return;
            }
            Send("SDK_AUTH_DISMISSED", new Dictionary<string, object>());
            RespondPurchase(pending, false, "auth-dismissed", "AUTH_REQUIRED", "Player dismissed sign-in required for purchase");
            Changed?.Invoke();
        }

        private void FinishPurchase(PendingRequest pending, PurchaseOutcome outcome)
        {
            switch (outcome)
            {
                case PurchaseOutcome.Paid:
                    GrantPurchase(pending);
                    RespondPurchase(pending, true, "paid", null, null);
                    break;
                case PurchaseOutcome.Canceled:
                    RespondPurchase(pending, false, "canceled", "PURCHASE_CANCELED", "Purchase was canceled before completion");
                    break;
                default:
                    RespondPurchase(pending, false, "dismissed", "PURCHASE_CLOSED", "Checkout was closed by the player");
                    break;
            }
        }

        /// <summary>The payment webhook's grant: an item into the inventory, or a top-up into the platform wallet.</summary>
        private void GrantPurchase(PendingRequest pending)
        {
            if (pending.Intent == "wallet_topup")
            {
                var pack = FindPack(pending.TopupPackId);
                var currency = pack != null && pack.TryGetValue("currency", out var c) ? c as string : "coins";
                var amount = pack != null && pack.TryGetValue("amount", out var a) && LocalBackend.TryPyInt(a, out _) ? Convert.ToDouble(a) : 0;
                _backend.CreditTopup(currency, amount);
                return;
            }

            var item = (Dictionary<string, object>)_config().Items[pending.ItemId];
            _backend.GrantItem(pending.GameId, pending.ItemId, item, "direct_purchase", "sim-" + pending.RequestId, out var error);
            if (error != null)
            {
                // Checkout refuses an owned durable before payment, so this means the item was
                // granted while the developer left this purchase waiting under Ask.
                Debug.LogWarning("[SusaPlay Simulator] Purchase of \"" + pending.ItemId + "\" was paid, but the grant was refused: " +
                                 error.ErrorCode + " — " + error.ErrorMessage + ".");
            }
        }

        private void RespondPurchase(PendingRequest pending, bool success, string status, string errorCode, string errorMessage)
        {
            // The flow refreshes the wallet the purchase touched, whatever the outcome.
            var topup = pending.Intent == "wallet_topup";
            Send("SDK_PURCHASE_RESPONSE", SimJson.Obj(
                "requestId", pending.RequestId,
                "success", success,
                "status", status,
                // Once checkout opened, the scope create-payment-token chose: a player always
                // transacts live. Before that the shells have none to send.
                "walletScope", pending.CheckoutOpened ? "live" : null,
                "wallet", topup ? null : _backend.GameWalletSnapshot(pending.GameId),
                "platformWallet", topup ? _backend.PlatformWalletSnapshot() : null,
                "error", errorCode == null ? null : SimJson.Obj("code", errorCode, "message", errorMessage)));
        }

        // ── Ads (the play page's handleAdShow) ──────────────────────────────────────────────

        private void StartAd(string requestId, string adType)
        {
            var pending = new PendingRequest
            {
                RequestId = requestId,
                Kind = "ad",
                AdType = string.IsNullOrEmpty(adType) ? "rewarded" : adType,
                StartedAt = _now,
            };

            // The play page checks the rewarded setting for every ad type (plan S9).
            if (!_config().RewardedEnabled)
            {
                SendAdFailed(pending, "ADS_DISABLED");
                return;
            }

            var outcome = _settings().adOutcome;
            if (outcome == AdOutcome.Ask)
            {
                AddPending(pending);
                return;
            }
            FinishAd(pending, outcome);
        }

        public void AnswerAd(PendingRequest pending, AdOutcome outcome)
        {
            if (!_pending.Remove(pending) || outcome == AdOutcome.Ask)
            {
                return;
            }
            FinishAd(pending, outcome);
            Changed?.Invoke();
        }

        private void FinishAd(PendingRequest pending, AdOutcome outcome)
        {
            if (outcome == AdOutcome.Skip)
            {
                SendAdFailed(pending, "dismissed");
                return;
            }
            if (outcome == AdOutcome.NoAd)
            {
                SendAdFailed(pending, "notReady");
                return;
            }

            if (pending.AdType != "rewarded")
            {
                Send("SDK_AD_COMPLETE", SimJson.Obj("requestId", pending.RequestId, "adType", pending.AdType, "rewarded", false, "data", null));
                return;
            }

            // As on the play page: the game's reward follows the watched ad, and the
            // platform's coins travel alongside. A guest earns no coins, so the page
            // does not ask for them.
            Dictionary<string, object> data = null;
            BackendResponse refusal = null;
            if (!IsGuest)
            {
                data = _backend.ClaimAdReward(SessionGameId, out refusal);
            }
            if (refusal != null)
            {
                Debug.Log("[SusaPlay Simulator] Rewarded ad watched; no platform coins this time: " + refusal.ErrorCode + " — " +
                          refusal.ErrorMessage + ". The game still receives Rewarded = true.");
                _log.Add(new RequestLogEntry
                {
                    Time = DateTime.UtcNow,
                    Name = "POST /economy/ad-reward",
                    Status = refusal.Status,
                    ErrorCode = refusal.ErrorCode,
                    ErrorMessage = refusal.ErrorMessage,
                    IsError = true,
                });
            }
            Send("SDK_AD_COMPLETE", SimJson.Obj(
                "requestId", pending.RequestId,
                "adType", pending.AdType,
                "rewarded", true,
                "data", data));
        }

        private void SendAdFailed(PendingRequest pending, string reason)
        {
            Send("SDK_AD_FAILED", SimJson.Obj("requestId", pending.RequestId, "adType", pending.AdType, "reason", reason));
        }

        // ── Delivery ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Delivers what is due. Called every Editor update; never from inside a SendMessage, which
        /// is what keeps answers asynchronous.
        /// </summary>
        public void Pump(double now)
        {
            _now = now;
            if (_pending.RemoveAll(p => now - p.StartedAt > PendingRequest.SdkTimeoutSeconds) > 0)
            {
                // The SDK has already answered the game with TIMEOUT.
                Changed?.Invoke();
            }

            if (_queue.Count == 0)
            {
                return;
            }
            var due = _queue.Where(entry => entry.due <= now).ToList();
            if (due.Count == 0)
            {
                return;
            }
            _queue.RemoveAll(entry => entry.due <= now);
            foreach (var entry in due)
            {
                try
                {
                    entry.action();
                }
                catch (Exception exception)
                {
                    // An exception from the game's own handler must not stop the next answer.
                    Debug.LogException(exception);
                }
            }
        }

        public void Schedule(double delaySeconds, Action action)
        {
            _queue.Add((_now + delaySeconds, action));
        }

        /// <summary>
        /// Sends a message as both browser shells do: the fields as a JSON string in "payload" and
        /// again at the top level. Most modules read payload; ApiModule reads the top level.
        /// </summary>
        private void Send(string type, Dictionary<string, object> fields)
        {
            var message = new Dictionary<string, object> { ["type"] = type, ["payload"] = SimJson.Serialize(fields) };
            foreach (var pair in fields)
            {
                message[pair.Key] = pair.Value;
            }
            var json = SimJson.Serialize(message);
            Schedule(0, () =>
            {
                _log.Add(new RequestLogEntry { Time = DateTime.UtcNow, Name = "← " + type, Detail = json });
                Deliver?.Invoke(json);
            });
        }

        private void AddPending(PendingRequest pending)
        {
            _pending.Add(pending);
            Changed?.Invoke();
        }

        private static Dictionary<string, object> ReadPayload(Dictionary<string, object> message)
        {
            if (message == null || !message.TryGetValue("payload", out var payload))
            {
                return new Dictionary<string, object>();
            }
            return payload is string text
                ? SimJson.TryParse(text) as Dictionary<string, object> ?? new Dictionary<string, object>()
                : payload as Dictionary<string, object> ?? new Dictionary<string, object>();
        }

        private static string Text(Dictionary<string, object> map, string key)
        {
            return map.TryGetValue(key, out var value) ? value as string : null;
        }

        private static string PathOf(string endpoint)
        {
            var mark = (endpoint ?? "").IndexOf('?');
            return mark >= 0 ? endpoint.Substring(0, mark) : endpoint;
        }
    }
}
