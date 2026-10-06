using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace susaplay.SDK.Editor.Simulator
{
    /// <summary>One answer from the local backend.</summary>
    internal sealed class BackendResponse
    {
        public long Status;
        public Dictionary<string, object> Body;
        public string ErrorCode;
        public string ErrorMessage;

        /// <summary>
        /// Things the platform accepts but quietly loses, and anything else a developer should
        /// know that the response itself does not say.
        /// </summary>
        public List<string> Warnings = new List<string>();

        /// <summary>The body as the platform sends it: Python's json.dumps.</summary>
        public string BodyText => SimJson.SerializePython(Body);

        public bool Succeeded => Status >= 200 && Status < 300;
    }

    /// <summary>
    /// The 13 routes the SDK calls, answered from the simulated state and the game's configuration.
    ///
    /// Every rule here is taken from the platform's handlers and cites where: the order of checks,
    /// status codes, error codes and messages, and the response shapes, quirks included. A game
    /// tested against this must take the same paths it takes in production; a rule that is
    /// stricter or looser than the platform's makes the simulator a liar. Cosmetic differences —
    /// a float the platform writes as 150.0 written here as 150 — are allowed only where no
    /// SDK parser can tell them apart.
    /// </summary>
    internal sealed class LocalBackend
    {
        public const string TokenPrefix = "sim1.";

        /// <summary>The routes this backend answers, as the request log names them.</summary>
        public static readonly string[] Routes =
        {
            "GET /save/read",
            "POST /save/write",
            "POST /engagement/achievements/unlock",
            "POST /engagement/achievements/increment",
            "GET /engagement/achievements/list",
            "POST /analytics/event",
            "POST /webhooks/event",
            "GET /economy/store-items",
            "GET /economy/topup-packs",
            "GET /economy/platform-wallet",
            "POST /economy/platform-wallet/spend",
            "GET /economy/inventory",
            "POST /economy/consume",
        };
        private const int MaxSaveCharacters = 500_000;

        private readonly Func<SimulatorState> _state;
        private readonly Func<SimulatorConfig> _config;
        private readonly Action _save;
        private readonly Func<DateTime> _clock;

        public LocalBackend(Func<SimulatorState> state, Func<SimulatorConfig> config, Action save, Func<DateTime> clock = null)
        {
            _state = state;
            _config = config;
            _save = save;
            _clock = clock ?? (() => DateTime.UtcNow);
        }

        /// <summary>
        /// The game the session token is pinned to. A real game-session token names one game, and
        /// every route refuses another with 403; the local token does the same.
        /// </summary>
        public string SessionGameId { get; set; }

        private SimulatorState State => _state();
        private SimulatorConfig Config => _config();

        public static string TokenFor(string uid)
        {
            return TokenPrefix + uid;
        }

        public BackendResponse Handle(string method, string endpoint, string body, string authorization)
        {
            SplitEndpoint(endpoint, out var path, out var query);
            method = (method ?? "GET").ToUpperInvariant();
            var json = SimJson.TryParse(body) as Dictionary<string, object>;

            switch (method + " " + path)
            {
                case "GET /save/read": return SaveRead(authorization, query);
                case "POST /save/write": return SaveWrite(authorization, json);
                case "POST /engagement/achievements/unlock": return AchievementsUnlock(authorization, json);
                case "POST /engagement/achievements/increment": return AchievementsIncrement(authorization, json);
                case "GET /engagement/achievements/list": return AchievementsList(authorization, query);
                case "POST /analytics/event": return AnalyticsEvent(authorization, json);
                case "POST /webhooks/event": return WebhookEvent(authorization, json);
                case "GET /economy/store-items": return StoreItems(authorization, query);
                case "GET /economy/topup-packs": return TopupPacks(authorization, query);
                case "GET /economy/platform-wallet": return PlatformWallet(authorization, query);
                case "POST /economy/platform-wallet/spend": return PlatformWalletSpend(authorization, json);
                case "GET /economy/inventory": return Inventory(authorization, query);
                case "POST /economy/consume": return Consume(authorization, json);
                default:
                    // Reachable through SusaPlaySDK.Backend. Failing loudly beats pretending.
                    return Error("SIMULATOR_UNSUPPORTED_ROUTE",
                        "The SusaPlay Editor Simulator does not implement " + method + " " + path +
                        ". It answers only the routes the SDK's own modules call; test this one in a WebGL build.",
                        501);
            }
        }

        // ── Authentication (helpers.py: verify_token, resolve_caller, resolve_game_caller) ──────

        /// <summary>The caller's uid, or the 401 the platform gives.</summary>
        private BackendResponse Authenticate(string authorization, out string uid)
        {
            uid = null;
            // HTTP drops trailing whitespace from a header, so a guest's "Bearer " + null token
            // arrives as "Bearer" and fails the prefix check.
            var header = (authorization ?? "").Trim();
            if (!header.StartsWith("Bearer ", StringComparison.Ordinal))
            {
                return Error("UNAUTHENTICATED", "Missing or invalid Authorization header", 401);
            }
            var token = header.Substring("Bearer ".Length);
            if (token != TokenFor(State.uid))
            {
                return Error("UNAUTHENTICATED", "Invalid token", 401);
            }
            uid = State.uid;
            return null;
        }

        /// <summary>resolve_game_caller: the uid and the game the token is pinned to.</summary>
        private BackendResponse AuthenticateForGame(string authorization, object requestedGameId, out string uid, out string gameId)
        {
            gameId = null;
            var error = Authenticate(authorization, out uid);
            if (error != null)
            {
                return error;
            }
            var requested = PyStr(Truthy(requestedGameId) ? requestedGameId : "").Trim();
            if (requested.Length > 0 && requested != SessionGameId)
            {
                return Error("FORBIDDEN", "This session is scoped to a different game", 403);
            }
            // A scoped token supplies the game even when the request leaves it out.
            gameId = SessionGameId;
            return null;
        }

        // ── Saves (save.py) ─────────────────────────────────────────────────────────────────

        private BackendResponse SaveRead(string authorization, Dictionary<string, string> query)
        {
            var error = AuthenticateForGame(authorization, Arg(query, "gameId"), out _, out var gameId);
            if (error != null) return error;

            var slot = Arg(query, "slot");
            if (string.IsNullOrEmpty(gameId) || string.IsNullOrEmpty(slot))
            {
                return Error("INVALID_ARGUMENT", "Missing required params: gameId, slot", 400);
            }

            var record = State.FindSave(gameId, slot);
            if (record == null)
            {
                return Ok(SimJson.Obj("slot", slot, "data", "", "version", 0L, "savedAt", null, "exists", false));
            }
            return Ok(SimJson.Obj("slot", slot, "data", record.data, "version", record.version, "savedAt", record.savedAt, "exists", true));
        }

        private BackendResponse SaveWrite(string authorization, Dictionary<string, object> data)
        {
            var error = AuthenticateForGame(authorization, Get(data, "gameId"), out _, out _);
            if (error != null) return error;

            if (data == null || data.Count == 0 || !new[] { "gameId", "slot", "data", "version" }.All(data.ContainsKey))
            {
                return Error("INVALID_ARGUMENT", "Missing required fields: gameId, slot, data, version", 400);
            }
            var rawPayload = data["data"];
            if (rawPayload == null)
            {
                return Error("INVALID_ARGUMENT", "Field 'data' cannot be null", 400);
            }
            var payload = rawPayload as string ?? SimJson.SerializePython(rawPayload);
            if (PyLen(payload) > MaxSaveCharacters)
            {
                return Error("INVALID_ARGUMENT", "Data exceeds 500KB limit", 400);
            }
            // isinstance(version, int): a bool passes, a float does not.
            var rawVersion = data["version"];
            long version;
            if (rawVersion is long whole) version = whole;
            else if (rawVersion is bool flag) version = flag ? 1 : 0;
            else version = -1;
            if (version < 0)
            {
                return Error("INVALID_ARGUMENT", "Field 'version' must be a non-negative integer", 400);
            }

            var gameId = PyStr(data["gameId"]);
            var slot = PyStr(data["slot"]);
            var record = State.FindSave(gameId, slot);
            // A slot that does not exist yet accepts any version.
            if (record != null && record.version != version)
            {
                return Error("VERSION_CONFLICT", "Version mismatch — fetch latest and retry", 409);
            }
            if (record == null)
            {
                record = new SaveRecord { gameId = gameId, slot = slot };
                State.saves.Add(record);
            }
            record.version = version + 1;
            record.data = payload;
            record.savedAt = Now();
            _save();
            return Ok(SimJson.Obj("slot", data["slot"], "version", record.version));
        }

        // ── Achievements (engagement.py, progression.py) ─────────────────────────────────────

        private BackendResponse AchievementsList(string authorization, Dictionary<string, string> query)
        {
            var error = AuthenticateForGame(authorization, Arg(query, "gameId"), out _, out var gameId);
            if (error != null) return error;

            var achievements = new List<object>();
            foreach (var pair in Config.Achievements.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                var definition = pair.Value;
                var type = AchievementType(definition);
                var incremental = type == "incremental";
                var record = State.FindAchievement(gameId, pair.Key, false);
                achievements.Add(SimJson.Obj(
                    "achievementId", pair.Key,
                    "name", Get(definition, "name"),
                    "description", Get(definition, "description"),
                    "iconUrl", Get(definition, "iconUrl"),
                    "type", type,
                    "targetValue", incremental ? Get(definition, "targetValue") : null,
                    "currentValue", incremental ? (object)(record != null && record.hasProgress ? record.progress : 0L) : null,
                    "points", Truthy(Get(definition, "points")) ? Get(definition, "points") : 0L,
                    "unlocked", record != null && record.unlocked,
                    "unlockedAt", record != null && record.unlocked ? record.unlockedAt : null));
            }
            return Ok(SimJson.Obj("achievements", achievements));
        }

        private BackendResponse AchievementsUnlock(string authorization, Dictionary<string, object> data)
        {
            data ??= new Dictionary<string, object>();
            var error = AuthenticateForGame(authorization, Get(data, "gameId"), out _, out var gameId);
            if (error != null) return error;

            var achievementId = Get(data, "achievementId");
            if (string.IsNullOrEmpty(gameId) || !Truthy(achievementId))
            {
                return Error("INVALID_ARGUMENT", "Missing required fields: gameId, achievementId", 400);
            }
            var result = UnlockAchievement(gameId, PyStr(achievementId), out var unlockError);
            return unlockError ?? Ok(result);
        }

        /// <summary>
        /// engagement.unlock_achievement — also what a custom achievement_unlocked event runs.
        /// </summary>
        private Dictionary<string, object> UnlockAchievement(string gameId, string achievementId, out BackendResponse error)
        {
            error = null;
            if (!Config.Achievements.TryGetValue(achievementId, out var definition))
            {
                error = Error("NOT_FOUND",
                    "Achievement is not registered for this game. Add it in the developer portal before unlocking it.", 404);
                return null;
            }
            if (AchievementType(definition) != "oneshot")
            {
                error = Error("INVALID_ARGUMENT", "Achievement is not oneshot", 400);
                return null;
            }

            var points = PyIntOr(Truthy(Get(definition, "points")) ? Get(definition, "points") : 0L, 0);
            var record = State.FindAchievement(gameId, achievementId, true);
            if (record.unlocked)
            {
                return SimJson.Obj(
                    "achievementId", achievementId,
                    "unlockedAt", record.unlockedAt,
                    "newlyUnlocked", false,
                    "points", points,
                    "progression", Progression(State.xp));
            }

            record.unlocked = true;
            record.unlockedAt = Now();
            AwardXp(points);
            _save();
            return SimJson.Obj(
                "achievementId", achievementId,
                "unlockedAt", record.unlockedAt,
                "newlyUnlocked", true,
                "points", points,
                "progression", Progression(State.xp));
        }

        private BackendResponse AchievementsIncrement(string authorization, Dictionary<string, object> data)
        {
            data ??= new Dictionary<string, object>();
            var error = AuthenticateForGame(authorization, Get(data, "gameId"), out _, out var gameId);
            if (error != null) return error;

            var achievementId = Get(data, "achievementId");
            if (string.IsNullOrEmpty(gameId) || !Truthy(achievementId))
            {
                return Error("INVALID_ARGUMENT", "Missing required fields: gameId, achievementId", 400);
            }
            // data.get("incrementBy", 1): absent means 1, an explicit null is not a number.
            var rawIncrement = data.TryGetValue("incrementBy", out var given) ? given : 1L;
            if (!TryPyInt(rawIncrement, out var incrementBy))
            {
                return Error("INVALID_ARGUMENT", "incrementBy must be a number", 400);
            }
            if (incrementBy <= 0)
            {
                return Error("INVALID_ARGUMENT", "incrementBy must be > 0", 400);
            }

            var id = PyStr(achievementId);
            if (!Config.Achievements.TryGetValue(id, out var definition))
            {
                return Error("NOT_FOUND",
                    "Achievement is not registered for this game. Add it in the developer portal before unlocking it.", 404);
            }
            if (AchievementType(definition) != "incremental")
            {
                return Error("INVALID_ARGUMENT", "Achievement is not incremental", 400);
            }
            var targetValue = Get(definition, "targetValue");
            if (targetValue == null)
            {
                return Error("INVALID_ARGUMENT", "Achievement targetValue missing", 400);
            }

            var points = PyIntOr(Truthy(Get(definition, "points")) ? Get(definition, "points") : 0L, 0);
            var record = State.FindAchievement(gameId, id, true);
            var next = (record.hasProgress ? record.progress : 0) + incrementBy;
            record.progress = next;
            record.hasProgress = true;

            var justUnlocked = false;
            if (next >= PyIntOr(targetValue, long.MaxValue) && !record.unlocked)
            {
                record.unlocked = true;
                record.unlockedAt = Now();
                justUnlocked = true;
                // Points land once, when the counter crosses the target.
                AwardXp(points);
            }
            _save();
            return Ok(SimJson.Obj(
                "achievementId", achievementId,
                "currentValue", next,
                "targetValue", targetValue,
                "justUnlocked", justUnlocked,
                "points", justUnlocked ? points : 0L,
                "progression", Progression(State.xp)));
        }

        private static string AchievementType(Dictionary<string, object> definition)
        {
            var type = Get(definition, "type");
            return Truthy(type) ? PyStr(type) : "oneshot";
        }

        private void AwardXp(long points)
        {
            if (points > 0)
            {
                State.xp += points;
            }
        }

        /// <summary>progression.progression_for_xp: level n starts at 50·n·(n−1) XP.</summary>
        internal static Dictionary<string, object> Progression(long xp)
        {
            xp = Math.Max(0, xp);
            long level = 1;
            while (XpForLevel(level + 1) <= xp)
            {
                level++;
            }
            var current = XpForLevel(level);
            var next = XpForLevel(level + 1);
            return SimJson.Obj(
                "xp", xp,
                "level", level,
                "xpIntoLevel", xp - current,
                "xpForNextLevel", next - current,
                "nextLevelAtXp", next);
        }

        private static long XpForLevel(long level)
        {
            level = Math.Max(1, level);
            return 50 * level * (level - 1);
        }

        // ── Analytics (analytics.py: _analytics_event) ───────────────────────────────────────

        private BackendResponse AnalyticsEvent(string authorization, Dictionary<string, object> body)
        {
            body ??= new Dictionary<string, object>();
            var error = AuthenticateForGame(authorization, Get(body, "gameId"), out _, out var gameId);
            if (error != null) return error;

            var sessionId = Get(body, "sessionId");
            if (string.IsNullOrEmpty(gameId) || !Truthy(sessionId))
            {
                return Error("INVALID_ARGUMENT", "Missing required fields: gameId, sessionId", 400);
            }
            if (!(Get(body, "events") is List<object> events) || events.Count == 0)
            {
                return Error("INVALID_ARGUMENT", "Missing required field: events[]", 400);
            }
            if (events.Count > 500)
            {
                return Error("INVALID_ARGUMENT", "Maximum 500 events per batch", 400);
            }

            var accepted = 0;
            var dropped = 0;
            var droppedLevelUps = 0;
            var response = new BackendResponse();
            foreach (var entry in events)
            {
                var evt = entry as Dictionary<string, object> ?? new Dictionary<string, object>();
                if (!(Get(evt, "name") is string name) || name.Trim().Length == 0)
                {
                    dropped++;
                    continue;
                }
                if (name.Trim() == "level_up")
                {
                    var parameters = Get(evt, "params") ?? Get(evt, "parameters");
                    if (ParseLevel(Get(NormalizeParams(parameters), "level")) == null)
                    {
                        // Dropped by the platform since plan S7: the same rule as /webhooks/event,
                        // without refusing the rest of the batch.
                        droppedLevelUps++;
                        continue;
                    }
                }
                accepted++;
            }
            if (dropped > 0)
            {
                response.Warnings.Add(dropped + " analytics event(s) had no name. The platform drops them without an error.");
            }
            if (droppedLevelUps > 0)
            {
                response.Warnings.Add(droppedLevelUps + " analytics 'level_up' event(s) had no valid params.level (a non-negative integer). " +
                                      "The platform drops them without an error, so they never reach level reports.");
            }

            response.Status = 200;
            response.Body = SimJson.Obj("success", true, "data", SimJson.Obj("accepted", (long)accepted, "delivered", 0L));
            return response;
        }

        private static Dictionary<string, object> NormalizeParams(object parameters)
        {
            if (parameters is Dictionary<string, object> map)
            {
                return map;
            }
            if (parameters is string text && text.Trim().Length > 0)
            {
                return SimJson.TryParse(text) as Dictionary<string, object> ?? new Dictionary<string, object>();
            }
            return new Dictionary<string, object>();
        }

        /// <summary>analytics._parse_level: a non-negative integer however it was spelled.</summary>
        private static long? ParseLevel(object value)
        {
            if (value == null || value is bool)
            {
                return null;
            }
            if (!TryPyInt(value, out var level))
            {
                return null;
            }
            return level >= 0 ? level : (long?)null;
        }

        // ── Custom events (webhooks.py: _webhook_event) ─────────────────────────────────────

        private BackendResponse WebhookEvent(string authorization, Dictionary<string, object> body)
        {
            var error = Authenticate(authorization, out var uid);
            if (error != null) return error;

            body ??= new Dictionary<string, object>();
            var gameId = PyStr(Truthy(Get(body, "gameId")) ? Get(body, "gameId") : "").Trim();
            var sessionId = PyStr(Truthy(Get(body, "sessionId")) ? Get(body, "sessionId") : "").Trim();
            var rawName = Truthy(Get(body, "eventName")) ? Get(body, "eventName") : Get(body, "name");
            var eventName = PyStr(Truthy(rawName) ? rawName : "").Trim();
            var payloadValue = Get(body, "payload") ?? Get(body, "data");

            if (gameId.Length == 0 || sessionId.Length == 0)
            {
                return Error("INVALID_ARGUMENT", "Missing required fields: gameId, sessionId", 400);
            }
            if (eventName.Length == 0)
            {
                return Error("INVALID_ARGUMENT", "Missing required field: eventName", 400);
            }
            if (!(payloadValue is Dictionary<string, object> payload))
            {
                return Error("INVALID_ARGUMENT", "payload must be an object", 400);
            }
            // This route reads the game from the body and checks it against the token after
            // validating, rather than through resolve_game_caller.
            if (gameId != SessionGameId)
            {
                return Error("FORBIDDEN", "This session is scoped to a different game", 403);
            }

            if (eventName == "level_up" && ParseLevel(PayloadField(payload, "level", "exp_level")) == null)
            {
                return Error("INVALID_ARGUMENT", "level_up requires payload.level as a non-negative integer", 400);
            }

            Dictionary<string, object> achievement = null;
            if (eventName == "achievement_unlocked" && uid != null)
            {
                var rawId = PayloadField(payload, "achievementId", "achievement_id");
                var achievementId = PyStr(Truthy(rawId) ? rawId : "").Trim();
                if (achievementId.Length == 0)
                {
                    return Error("INVALID_ARGUMENT", "achievement_unlocked requires payload.achievementId", 400);
                }
                achievement = UnlockAchievement(gameId, achievementId, out var unlockError);
                if (unlockError != null)
                {
                    return unlockError;
                }
            }

            var data = SimJson.Obj("eventId", Guid.NewGuid().ToString("N"), "delivered", 0L);
            if (achievement != null)
            {
                data["achievement"] = achievement;
            }
            return Ok(data);
        }

        /// <summary>
        /// webhooks._payload_field: a reserved field at the top of the payload, inside
        /// "parameters" (where WebhooksModule puts it), or else the payload's "value".
        /// </summary>
        private static object PayloadField(Dictionary<string, object> payload, params string[] names)
        {
            var parameters = Get(payload, "parameters") as Dictionary<string, object> ?? new Dictionary<string, object>();
            foreach (var name in names)
            {
                if (Get(payload, name) != null) return payload[name];
                if (Get(parameters, name) != null) return parameters[name];
            }
            return Get(payload, "value");
        }

        // ── Store, wallet and inventory (economy_api/rewards.py, commerce_support.py) ───────

        private BackendResponse StoreItems(string authorization, Dictionary<string, string> query)
        {
            var error = AuthenticateForGame(authorization, Arg(query, "gameId"), out _, out var gameId);
            if (error != null) return error;
            if (string.IsNullOrEmpty(gameId))
            {
                return Error("INVALID_ARGUMENT", "Missing required field: gameId", 400);
            }

            // The platform's own answer, captured when the configuration was exported, so the
            // store a game shows in the Editor is the store players see.
            return Ok(Config.StoreItemsResponse ?? SimJson.Obj(
                "gameId", gameId,
                "currencies", new Dictionary<string, object>(),
                "currencyList", new List<object>(),
                "items", new List<object>()));
        }

        private BackendResponse TopupPacks(string authorization, Dictionary<string, string> query)
        {
            var error = AuthenticateForGame(authorization, Arg(query, "gameId"), out _, out _);
            if (error != null) return error;
            return Ok(Config.TopupPacksResponse ?? SimJson.Obj("topupPacks", new List<object>(), "paymentMode", "live"));
        }

        private BackendResponse PlatformWallet(string authorization, Dictionary<string, string> query)
        {
            var error = AuthenticateForGame(authorization, Arg(query, "gameId"), out _, out _);
            if (error != null) return error;
            return Ok(PlatformWalletSnapshot());
        }

        /// <summary>
        /// What GET /economy/platform-wallet returns, running the one-time migration the platform
        /// runs on a player's first read.
        /// </summary>
        public Dictionary<string, object> PlatformWalletSnapshot()
        {
            var wallet = EnsureMigrated();
            return SimJson.Obj(
                "walletId", "platform",
                "walletScope", "live",
                "coins", Num(wallet.coins),
                "gems", Num(wallet.gems),
                "version", wallet.version,
                "lastModified", wallet.lastModified,
                "migration", SimJson.Obj(
                    "status", "completed",
                    "completedAt", wallet.migratedAt,
                    "migratedWalletCount", 0L,
                    "unsupportedCurrencies", new List<object>()));
        }

        private PlatformWalletRecord EnsureMigrated()
        {
            var wallet = State.platformWallet;
            if (!wallet.migrated)
            {
                wallet.migrated = true;
                wallet.migratedAt = Now();
                wallet.version += 1;
                wallet.lastModified = wallet.migratedAt;
                _save();
            }
            return wallet;
        }

        private BackendResponse PlatformWalletSpend(string authorization, Dictionary<string, object> data)
        {
            data ??= new Dictionary<string, object>();
            var error = AuthenticateForGame(authorization, Get(data, "gameId"), out _, out var gameId);
            if (error != null) return error;

            var itemId = PyStr(Truthy(Get(data, "itemId")) ? Get(data, "itemId") : "").Trim();
            if (string.IsNullOrEmpty(gameId) || itemId.Length == 0)
            {
                return Error("INVALID_ARGUMENT", "Missing required fields: gameId, itemId", 400);
            }
            if (!(Get(Config.Items, itemId) is Dictionary<string, object> item))
            {
                return Error("ITEM_NOT_FOUND", "Item not found in economy config", 404);
            }
            if (Get(item, "walletPurchaseEnabled") is bool enabled && !enabled)
            {
                return Error("INVALID_STATE", "Item is not eligible for platform wallet purchases", 409);
            }
            var price = Get(item, "price") as Dictionary<string, object> ?? new Dictionary<string, object>();
            var currency = PyStr(Truthy(Get(price, "currency")) ? Get(price, "currency") : "").Trim();
            if (currency != "coins" && currency != "gems")
            {
                return Error("INVALID_STATE", "Item price is not in a supported platform wallet currency", 409);
            }
            var amount = ToIntAmount(Get(price, "amount"));
            if (amount <= 0)
            {
                return Error("INVALID_STATE", "Item price is invalid", 409);
            }

            var wallet = EnsureMigrated();
            // No requestId, no protection: defaulting it would block a second purchase of a consumable.
            var claimKey = ClaimKey(gameId, Get(data, "requestId"));
            var claim = claimKey != null ? State.FindClaim(claimKey) : null;
            if (claim != null)
            {
                var stored = SimJson.TryParse(claim.json) as Dictionary<string, object> ?? new Dictionary<string, object>();
                // The stored claim has no wallet snapshot, so a replay answers platformWallet: null.
                return Ok(SimJson.Obj(
                    "newBalance", Get(stored, "newBalance") ?? new Dictionary<string, object>(),
                    "walletId", "platform",
                    "walletScope", "live",
                    "platformWallet", null,
                    "duplicate", true));
            }

            var balance = currency == "coins" ? wallet.coins : wallet.gems;
            if (balance < amount)
            {
                return Error("INSUFFICIENT_BALANCE", "Not enough platform wallet balance to complete spend", 409);
            }

            // Granted before debiting, so an owned durable is refused with the balance untouched.
            var grant = GrantItem(gameId, itemId, item, "wallet_spend", null, out var grantError);
            if (grantError != null)
            {
                return grantError;
            }

            var next = balance - amount;
            if (currency == "coins") wallet.coins = next; else wallet.gems = next;
            wallet.version += 1;
            wallet.lastModified = Now();
            var newBalance = SimJson.Obj(currency, Num(next));
            if (claimKey != null)
            {
                State.requestClaims.Add(new RequestClaim
                {
                    key = claimKey,
                    json = SimJson.Serialize(SimJson.Obj("newBalance", newBalance)),
                });
            }
            _save();

            var response = SimJson.Obj(
                "newBalance", newBalance,
                "walletId", "platform",
                "walletScope", "live",
                "platformWallet", SimJson.Obj(
                    "walletId", "platform",
                    "walletScope", "live",
                    "coins", Num(wallet.coins),
                    "gems", Num(wallet.gems),
                    "version", wallet.version));
            if (grant.Durable)
            {
                response["inventory"] = CountMap(grant.Inventory.items);
                response["inventoryList"] = CountList(grant.Inventory.items);
            }
            else
            {
                response["consumables"] = CountMap(grant.Inventory.consumables);
                response["consumablesList"] = CountList(grant.Inventory.consumables);
            }
            return Ok(response);
        }

        private BackendResponse Inventory(string authorization, Dictionary<string, string> query)
        {
            var error = AuthenticateForGame(authorization, Arg(query, "gameId"), out _, out var gameId);
            if (error != null) return error;
            if (string.IsNullOrEmpty(gameId))
            {
                return Error("INVALID_ARGUMENT", "Missing required field: gameId", 400);
            }

            var record = State.FindInventory(gameId, false) ?? new InventoryRecord();
            var states = new Dictionary<string, object>();
            foreach (var state in record.itemStates)
            {
                states[state.itemId] = SimJson.Obj(
                    "reversible", true,
                    "equipped", false,
                    "consumed", false,
                    "exchanged", false,
                    "locked", false,
                    "source", state.source,
                    "lastOrderId", state.lastOrderId,
                    "grantedAt", state.grantedAt);
            }
            return Ok(SimJson.Obj(
                "gameId", gameId,
                "items", CountMap(record.items),
                "consumables", CountMap(record.consumables),
                // List forms as well: JsonUtility cannot read a map, so the SDK reads these.
                "itemsList", CountList(record.items),
                "consumablesList", CountList(record.consumables),
                "itemStates", states,
                "lastModified", record.lastModified));
        }

        private BackendResponse Consume(string authorization, Dictionary<string, object> data)
        {
            data ??= new Dictionary<string, object>();
            var error = AuthenticateForGame(authorization, Get(data, "gameId"), out _, out var gameId);
            if (error != null) return error;

            var itemId = PyStr(Truthy(Get(data, "itemId")) ? Get(data, "itemId") : "").Trim();
            // int(data.get("quantity") or 1): a missing or zero quantity means 1.
            var rawQuantity = Truthy(Get(data, "quantity")) ? Get(data, "quantity") : 1L;
            if (!TryPyInt(rawQuantity, out var quantity))
            {
                return Error("INVALID_ARGUMENT", "quantity must be a number", 400);
            }
            if (string.IsNullOrEmpty(gameId) || itemId.Length == 0)
            {
                return Error("INVALID_ARGUMENT", "Missing required fields: gameId, itemId", 400);
            }
            if (quantity < 1 || quantity > 1000)
            {
                return Error("INVALID_ARGUMENT", "quantity must be between 1 and 1000", 400);
            }

            // Spends and consumes share one claim space on the platform, so a requestId reused
            // across the two replays whichever ran first.
            var claimKey = ClaimKey(gameId, Get(data, "requestId"));
            var claim = claimKey != null ? State.FindClaim(claimKey) : null;
            if (claim != null)
            {
                var stored = SimJson.TryParse(claim.json) as Dictionary<string, object> ?? new Dictionary<string, object>();
                return Ok(SimJson.Obj(
                    "itemId", itemId,
                    "remaining", Get(stored, "remaining"),
                    "consumables", Get(stored, "consumables") ?? new Dictionary<string, object>(),
                    "duplicate", true));
            }

            var inventory = State.FindInventory(gameId, false);
            if (inventory == null)
            {
                return Error("ITEM_NOT_FOUND", "Nothing to consume", 404);
            }
            var owned = inventory.Count(inventory.consumables, itemId);
            if (owned < quantity)
            {
                return Error("INSUFFICIENT_ITEMS", "Not enough of that item to consume", 409);
            }

            var remaining = owned - quantity;
            if (remaining > 0)
            {
                inventory.Set(inventory.consumables, itemId, remaining);
            }
            else
            {
                inventory.consumables.RemoveAll(e => e.key == itemId);
            }
            inventory.lastModified = Now();
            if (claimKey != null)
            {
                State.requestClaims.Add(new RequestClaim
                {
                    key = claimKey,
                    json = SimJson.Serialize(SimJson.Obj("remaining", remaining, "consumables", CountMap(inventory.consumables))),
                });
            }
            _save();
            return Ok(SimJson.Obj(
                "itemId", itemId,
                "remaining", remaining,
                "consumables", CountMap(inventory.consumables),
                "consumablesList", CountList(inventory.consumables)));
        }

        private static string ClaimKey(string gameId, object requestId)
        {
            var key = PyStr(Truthy(requestId) ? requestId : "").Trim();
            return key.Length == 0 ? null : "spend:" + gameId + ":" + key;
        }

        // ── Grants the shell runs (the payment webhook and /economy/ad-reward) ─────────────

        internal sealed class GrantResult
        {
            public bool Durable;
            public InventoryRecord Inventory;
        }

        /// <summary>commerce_support._grant_inventory: a durable once, a consumable adds one.</summary>
        internal GrantResult GrantItem(string gameId, string itemId, Dictionary<string, object> item, string source, string orderId,
            out BackendResponse error)
        {
            error = null;
            var durable = IsDurable(item);
            var inventory = State.FindInventory(gameId, true);
            if (durable)
            {
                if (inventory.Count(inventory.items, itemId) > 0)
                {
                    error = Error("DUPLICATE_ITEM", "Durable item is already owned", 409);
                    return null;
                }
                inventory.Set(inventory.items, itemId, 1);
                inventory.itemStates.RemoveAll(s => s.itemId == itemId);
                inventory.itemStates.Add(new ItemStateRecord { itemId = itemId, source = source, lastOrderId = orderId, grantedAt = Now() });
            }
            else
            {
                inventory.Set(inventory.consumables, itemId, inventory.Count(inventory.consumables, itemId) + 1);
            }
            inventory.lastModified = Now();
            _save();
            return new GrantResult { Durable = durable, Inventory = inventory };
        }

        /// <summary>commerce_support._normalize_item_type: anything but "consumable" is durable.</summary>
        internal static bool IsDurable(Dictionary<string, object> item)
        {
            return PyStr(Truthy(Get(item, "type")) ? Get(item, "type") : "durable").Trim() != "consumable";
        }

        /// <summary>A paid top-up: the pack's amount, in its currency, into the platform wallet.</summary>
        internal void CreditTopup(string currency, double amount)
        {
            var wallet = EnsureMigrated();
            if (currency == "gems") wallet.gems += amount; else wallet.coins += amount;
            wallet.version += 1;
            wallet.lastModified = Now();
            _save();
        }

        /// <summary>What GET /economy/wallet returns for the game wallet a direct purchase reports.</summary>
        internal Dictionary<string, object> GameWalletSnapshot(string gameId)
        {
            var wallet = State.FindGameWallet(gameId) ?? new GameWalletRecord();
            return SimJson.Obj(
                "gameId", gameId,
                "coins", Num(wallet.coins),
                "gems", Num(wallet.gems),
                "custom", new Dictionary<string, object>(),
                "version", wallet.version,
                "lastModified", wallet.lastModified);
        }

        /// <summary>
        /// POST /economy/ad-reward as the play page calls it after a rewarded ad. Returns the
        /// response's data, or null with the refusal in <paramref name="error"/>.
        /// </summary>
        internal Dictionary<string, object> ClaimAdReward(string gameId, bool guest, out BackendResponse error)
        {
            error = null;
            var config = Config;
            if (!config.RewardedEnabled)
            {
                error = Error("ADS_DISABLED", "Rewarded ads are disabled for this game", 403);
                return null;
            }
            var amount = config.RewardAmount;
            if (guest)
            {
                // A guest's reward is held against a redemption token until they sign in.
                return SimJson.Obj(
                    "completionToken", Guid.NewGuid().ToString(),
                    "awarded", SimJson.Obj("currency", "coins", "amount", amount));
            }

            var now = _clock();
            var today = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var claim = State.FindAdClaim(gameId, today, true);
            if (claim.count >= config.MaxPerDay)
            {
                error = Error("AD_REWARD_LIMIT_REACHED", "Daily ad reward limit of " + config.MaxPerDay + " reached for this game", 429);
                return null;
            }
            if (!string.IsNullOrEmpty(claim.lastClaimAt) &&
                DateTime.TryParse(claim.lastClaimAt, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var last))
            {
                var elapsed = (now - last).TotalMinutes;
                if (elapsed < config.CooldownMinutes)
                {
                    var remaining = (long)(config.CooldownMinutes - elapsed) + 1;
                    error = Error("AD_REWARD_COOLDOWN", "Please wait " + remaining + " minute(s) before claiming another ad reward", 429);
                    return null;
                }
            }

            var wallet = State.platformWallet;
            wallet.coins += amount;
            wallet.version += 1;
            wallet.lastModified = Iso(now);
            claim.count += 1;
            claim.lastClaimAt = Iso(now);
            _save();
            return SimJson.Obj(
                "awarded", SimJson.Obj("currency", "coins", "amount", amount),
                "newBalance", SimJson.Obj("coins", Num(wallet.coins)));
        }

        // ── Helpers ─────────────────────────────────────────────────────────────────────────

        private string Now()
        {
            return Iso(_clock());
        }

        /// <summary>Python's datetime.isoformat() for a UTC time, as the platform writes timestamps.</summary>
        internal static string Iso(DateTime moment)
        {
            return moment.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'+00:00'", CultureInfo.InvariantCulture);
        }

        private static BackendResponse Ok(Dictionary<string, object> data)
        {
            return new BackendResponse { Status = 200, Body = SimJson.Obj("success", true, "data", data) };
        }

        private static BackendResponse Error(string code, string message, long status)
        {
            return new BackendResponse
            {
                Status = status,
                ErrorCode = code,
                ErrorMessage = message,
                Body = SimJson.Obj("success", false, "error", SimJson.Obj("code", code, "message", message)),
            };
        }

        private static void SplitEndpoint(string endpoint, out string path, out Dictionary<string, string> query)
        {
            query = new Dictionary<string, string>(StringComparer.Ordinal);
            endpoint ??= "";
            var mark = endpoint.IndexOf('?');
            path = "/" + (mark >= 0 ? endpoint.Substring(0, mark) : endpoint).Trim('/');
            if (mark < 0)
            {
                return;
            }
            foreach (var part in endpoint.Substring(mark + 1).Split('&'))
            {
                if (part.Length == 0) continue;
                var equals = part.IndexOf('=');
                var key = Decode(equals >= 0 ? part.Substring(0, equals) : part);
                var value = equals >= 0 ? Decode(part.Substring(equals + 1)) : "";
                // Flask's request.args.get returns the first value.
                if (!query.ContainsKey(key))
                {
                    query[key] = value;
                }
            }
        }

        private static string Decode(string text)
        {
            return Uri.UnescapeDataString(text.Replace('+', ' '));
        }

        private static string Arg(Dictionary<string, string> query, string key)
        {
            return query.TryGetValue(key, out var value) ? value : null;
        }

        private static object Get(Dictionary<string, object> map, string key)
        {
            return map != null && map.TryGetValue(key, out var value) ? value : null;
        }

        /// <summary>Python truthiness.</summary>
        internal static bool Truthy(object value)
        {
            switch (value)
            {
                case null: return false;
                case bool flag: return flag;
                case long whole: return whole != 0;
                case double real: return real != 0;
                case string text: return text.Length > 0;
                case System.Collections.ICollection collection: return collection.Count > 0;
                default: return true;
            }
        }

        /// <summary>Python's str() for a JSON value.</summary>
        internal static string PyStr(object value)
        {
            switch (value)
            {
                case null: return "None";
                case string text: return text;
                case bool flag: return flag ? "True" : "False";
                case long whole: return whole.ToString(CultureInfo.InvariantCulture);
                case double real:
                    return real == Math.Floor(real) && Math.Abs(real) < 1e16
                        ? ((long)real).ToString(CultureInfo.InvariantCulture) + ".0"
                        : real.ToString("R", CultureInfo.InvariantCulture);
                default: return SimJson.SerializePython(value);
            }
        }

        /// <summary>Python's int() for a JSON value: truncates a float, parses a whole-number string.</summary>
        internal static bool TryPyInt(object value, out long result)
        {
            result = 0;
            switch (value)
            {
                case bool flag: result = flag ? 1 : 0; return true;
                case long whole: result = whole; return true;
                case double real:
                    if (double.IsNaN(real) || double.IsInfinity(real)) return false;
                    result = (long)Math.Truncate(real);
                    return true;
                case string text:
                    return long.TryParse(text.Trim().Replace("_", ""), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out result);
                default:
                    return false;
            }
        }

        private static long PyIntOr(object value, long fallback)
        {
            return TryPyInt(value, out var result) ? result : fallback;
        }

        /// <summary>economy_api.common._to_int_amount: int(round(float(value))), Python's banker's rounding.</summary>
        private static long ToIntAmount(object value)
        {
            double number;
            switch (value)
            {
                case bool flag: number = flag ? 1 : 0; break;
                case long whole: number = whole; break;
                case double real: number = real; break;
                case string text when double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed):
                    number = parsed;
                    break;
                default: return 0;
            }
            return (long)Math.Round(number, MidpointRounding.ToEven);
        }

        /// <summary>len() of a Python str: code points, not UTF-16 units.</summary>
        private static int PyLen(string text)
        {
            var length = text.Length;
            foreach (var c in text)
            {
                if (char.IsLowSurrogate(c)) length--;
            }
            return length;
        }

        private static object Num(double value)
        {
            return value == Math.Floor(value) && Math.Abs(value) < 1e15 ? (object)(long)value : value;
        }

        private static Dictionary<string, object> CountMap(List<CountEntry> entries)
        {
            var map = new Dictionary<string, object>();
            foreach (var entry in entries)
            {
                map[entry.key] = entry.value;
            }
            return map;
        }

        private static List<object> CountList(List<CountEntry> entries)
        {
            return entries
                .OrderBy(e => e.key, StringComparer.Ordinal)
                .Select(e => (object)SimJson.Obj("key", e.key, "value", e.value))
                .ToList();
        }
    }
}
