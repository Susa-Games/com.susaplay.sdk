using System;
using System.Globalization;
using UnityEngine;

namespace susaplay.SDK
{
    internal interface ILiveOpsCacheStore
    {
        bool TryLoad(out string json, out long cachedAtUnixSeconds);
        void Save(string json, long cachedAtUnixSeconds);
    }

    internal sealed class LiveOpsCache : ILiveOpsCacheStore
    {
        private readonly string _jsonKey;
        private readonly string _timestampKey;

        internal LiveOpsCache(string contentBaseUrl, string gameId, int schemaVersion)
        {
            var identity = (contentBaseUrl ?? string.Empty).TrimEnd('/') + "|" +
                (gameId ?? string.Empty) + "|" + schemaVersion.ToString(CultureInfo.InvariantCulture);
            var suffix = StableHash(identity);
            _jsonKey = "susaplay.liveops." + suffix + ".json";
            _timestampKey = "susaplay.liveops." + suffix + ".cachedAt";
        }

        public bool TryLoad(out string json, out long cachedAtUnixSeconds)
        {
            json = string.Empty;
            cachedAtUnixSeconds = 0;
            if (!PlayerPrefs.HasKey(_jsonKey) || !PlayerPrefs.HasKey(_timestampKey))
            {
                return false;
            }

            json = PlayerPrefs.GetString(_jsonKey, string.Empty);
            var timestamp = PlayerPrefs.GetString(_timestampKey, string.Empty);
            return !string.IsNullOrWhiteSpace(json) &&
                long.TryParse(timestamp, NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out cachedAtUnixSeconds);
        }

        public void Save(string json, long cachedAtUnixSeconds)
        {
            PlayerPrefs.SetString(_jsonKey, json);
            PlayerPrefs.SetString(_timestampKey,
                cachedAtUnixSeconds.ToString(CultureInfo.InvariantCulture));
            PlayerPrefs.Save();
        }

        private static string StableHash(string value)
        {
            unchecked
            {
                const ulong offset = 14695981039346656037UL;
                const ulong prime = 1099511628211UL;
                var hash = offset;
                for (var i = 0; i < value.Length; i++)
                {
                    hash ^= value[i];
                    hash *= prime;
                }
                return hash.ToString("x16", CultureInfo.InvariantCulture);
            }
        }
    }
}
