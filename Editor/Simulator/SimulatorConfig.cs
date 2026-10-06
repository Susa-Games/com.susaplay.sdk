using System;
using System.Collections.Generic;
using System.IO;

namespace susaplay.SDK.Editor.Simulator
{
    /// <summary>
    /// The game's configuration as the platform exported it: achievement definitions, economy
    /// items, ad settings and the store's own responses. Read-only here; it changes only when the
    /// developer imports a newer file.
    /// </summary>
    internal sealed class SimulatorConfig
    {
        public const string DefaultPath = "ProjectSettings/Packages/com.susaplay.sdk/SimulatorConfig.json";

        // The platform's defaults for a game with no ad settings (helpers.py).
        public const long DefaultRewardAmount = 50;
        public const long DefaultMaxPerDay = 10;
        public const long DefaultCooldownMinutes = 30;

        /// <summary>False when there is no file or it could not be read.</summary>
        public bool Loaded { get; private set; }

        /// <summary>Why <see cref="Loaded"/> is false: null when there is simply no file.</summary>
        public string LoadError { get; private set; }

        public string GameId { get; private set; }
        public string GameKey { get; private set; }
        public string GameName { get; private set; }
        public string ExportedAt { get; private set; }

        /// <summary>Achievement definitions by id.</summary>
        public Dictionary<string, Dictionary<string, object>> Achievements { get; } =
            new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);

        /// <summary>Economy item definitions by id, as stored in the game's economy configuration.</summary>
        public Dictionary<string, object> Items { get; private set; } = new Dictionary<string, object>();

        public bool RewardedEnabled { get; private set; } = true;
        public long RewardAmount { get; private set; } = DefaultRewardAmount;
        public long MaxPerDay { get; private set; } = DefaultMaxPerDay;
        public long CooldownMinutes { get; private set; } = DefaultCooldownMinutes;

        /// <summary>What GET /economy/store-items returned at export, or null.</summary>
        public Dictionary<string, object> StoreItemsResponse { get; private set; }

        /// <summary>What GET /economy/topup-packs returned at export, or null.</summary>
        public Dictionary<string, object> TopupPacksResponse { get; private set; }

        public static SimulatorConfig Empty()
        {
            return new SimulatorConfig();
        }

        public static SimulatorConfig LoadFile(string path = DefaultPath)
        {
            if (!File.Exists(path))
            {
                return Empty();
            }
            try
            {
                return FromJson(File.ReadAllText(path));
            }
            catch (Exception exception)
            {
                // Treated as no configuration, with the reason shown (an edge case in the plan).
                return new SimulatorConfig { LoadError = exception.Message };
            }
        }

        public static SimulatorConfig FromJson(string json)
        {
            if (!(SimJson.Parse(json) is Dictionary<string, object> root))
            {
                throw new FormatException("The configuration is not a JSON object");
            }
            var format = root.TryGetValue("format", out var formatValue) && formatValue is long number ? number : 0;
            if (format != 1)
            {
                throw new FormatException("Unsupported configuration format " + format + "; this SDK reads format 1");
            }

            var config = new SimulatorConfig { Loaded = true };
            config.ExportedAt = root.TryGetValue("exportedAt", out var exportedAt) ? exportedAt as string : null;

            if (root.TryGetValue("game", out var gameValue) && gameValue is Dictionary<string, object> game)
            {
                config.GameId = Text(game, "gameId");
                config.GameKey = Text(game, "gameKey");
                config.GameName = Text(game, "name");
            }
            if (string.IsNullOrEmpty(config.GameId))
            {
                throw new FormatException("The configuration has no game.gameId");
            }

            if (root.TryGetValue("achievements", out var achievementsValue) && achievementsValue is List<object> achievements)
            {
                foreach (var entry in achievements)
                {
                    if (entry is Dictionary<string, object> definition && Text(definition, "achievementId") is string id && id.Length > 0)
                    {
                        config.Achievements[id] = definition;
                    }
                }
            }

            if (root.TryGetValue("items", out var itemsValue) && itemsValue is Dictionary<string, object> items)
            {
                config.Items = items;
            }

            if (root.TryGetValue("adSettings", out var adValue) && adValue is Dictionary<string, object> adSettings &&
                adSettings.TryGetValue("rewarded", out var rewardedValue) && rewardedValue is Dictionary<string, object> rewarded)
            {
                // Normalized the way normalize_game_ad_settings does it, so a hand-written file
                // behaves as the platform would.
                config.RewardedEnabled = !(rewarded.TryGetValue("enabled", out var enabled) && enabled is bool flag) || flag;
                config.RewardAmount = PositiveOr(rewarded, "rewardAmount", DefaultRewardAmount);
                config.MaxPerDay = PositiveOr(rewarded, "maxPerDay", DefaultMaxPerDay);
                config.CooldownMinutes = PositiveOr(rewarded, "cooldownMinutes", DefaultCooldownMinutes);
            }

            if (root.TryGetValue("responses", out var responsesValue) && responsesValue is Dictionary<string, object> responses)
            {
                config.StoreItemsResponse = responses.TryGetValue("storeItems", out var store) ? store as Dictionary<string, object> : null;
                config.TopupPacksResponse = responses.TryGetValue("topupPacks", out var packs) ? packs as Dictionary<string, object> : null;
            }

            return config;
        }

        /// <summary>The active top-up packs, as the store route lists them.</summary>
        public List<Dictionary<string, object>> TopupPacks()
        {
            var result = new List<Dictionary<string, object>>();
            if (TopupPacksResponse != null && TopupPacksResponse.TryGetValue("topupPacks", out var value) && value is List<object> packs)
            {
                foreach (var pack in packs)
                {
                    if (pack is Dictionary<string, object> entry)
                    {
                        result.Add(entry);
                    }
                }
            }
            return result;
        }

        private static string Text(Dictionary<string, object> map, string key)
        {
            return map.TryGetValue(key, out var value) ? value as string : null;
        }

        private static long PositiveOr(Dictionary<string, object> map, string key, long fallback)
        {
            if (!map.TryGetValue(key, out var value) || value == null || value is bool)
            {
                return fallback;
            }
            long parsed;
            switch (value)
            {
                case long whole: parsed = whole; break;
                case double real: parsed = (long)real; break;
                case string text when long.TryParse(text.Trim(), out var fromText): parsed = fromText; break;
                default: return fallback;
            }
            return parsed > 0 ? parsed : fallback;
        }
    }
}
