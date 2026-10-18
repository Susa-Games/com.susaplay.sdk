using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace susaplay.SDK
{
    public sealed class LiveOpsModule
    {
        private const int CacheTtlSeconds = 15 * 60;
        private readonly string _manifestUrl;
        private readonly ILiveOpsTransport _transport;
        private readonly ILiveOpsCacheStore _cache;
        private readonly SemaphoreSlim _refreshLock = new SemaphoreSlim(1, 1);

        internal LiveOpsModule(string contentBaseUrl, string gameId)
            : this(contentBaseUrl, gameId, new UnityLiveOpsTransport(),
                new LiveOpsCache(contentBaseUrl, gameId, LiveOpsManifestParser.SchemaVersion))
        {
        }

        internal LiveOpsModule(
            string contentBaseUrl,
            string gameId,
            ILiveOpsTransport transport,
            ILiveOpsCacheStore cache)
        {
            _transport = transport;
            _cache = cache;
            if (!string.IsNullOrWhiteSpace(contentBaseUrl) && !string.IsNullOrWhiteSpace(gameId))
            {
                _manifestUrl = contentBaseUrl.TrimEnd('/') + "/liveops/" +
                    UnityWebRequest.EscapeURL(gameId) + "/manifest.json";
            }
        }

        public async Task<LiveOpsLoadResult> RefreshAsync(bool forceOrigin = false)
        {
            if (string.IsNullOrEmpty(_manifestUrl))
            {
                return new LiveOpsLoadResult(
                    LiveOpsLoadStatus.NotConfigured,
                    LiveOpsSnapshot.Empty(),
                    "LiveOpsContentBaseUrl or gameId is not configured.",
                    false);
            }

            await _refreshLock.WaitAsync();
            try
            {
                var now = DateTimeOffset.UtcNow;
                var hasCache = TryLoadCached(now, out var cachedSnapshot, out var cachedAt);
                if (!forceOrigin && hasCache && IsFresh(cachedAt, now))
                {
                    return new LiveOpsLoadResult(
                        LiveOpsLoadStatus.Cached, cachedSnapshot, string.Empty, true);
                }

                var requestUrl = forceOrigin ? AddOriginCacheBuster(_manifestUrl) : _manifestUrl;
                var response = await _transport.GetAsync(requestUrl);
                if (response.Success)
                {
                    if (LiveOpsManifestParser.TryParse(
                        response.Data, now, out var snapshot, out var parseError))
                    {
                        _cache.Save(response.Data, now.ToUnixTimeSeconds());
                        return new LiveOpsLoadResult(
                            LiveOpsLoadStatus.Success, snapshot, string.Empty, false);
                    }
                    return CachedOrFailed(hasCache, cachedSnapshot,
                        "LiveOps manifest was malformed: " + parseError);
                }

                if (response.StatusCode == 404 && !hasCache)
                {
                    return new LiveOpsLoadResult(
                        LiveOpsLoadStatus.NotConfigured,
                        LiveOpsSnapshot.Empty(),
                        "LiveOps manifest was not found.",
                        false);
                }

                var reason = response.StatusCode > 0
                    ? "LiveOps request failed with HTTP " + response.StatusCode + "."
                    : "LiveOps request failed: " + (response.Error ?? "unknown network error");
                return CachedOrFailed(hasCache, cachedSnapshot, reason);
            }
            finally
            {
                _refreshLock.Release();
            }
        }

        private bool TryLoadCached(
            DateTimeOffset now,
            out LiveOpsSnapshot snapshot,
            out long cachedAtUnixSeconds)
        {
            snapshot = null;
            cachedAtUnixSeconds = 0;
            if (!_cache.TryLoad(out var json, out cachedAtUnixSeconds))
            {
                return false;
            }
            return LiveOpsManifestParser.TryParse(json, now, out snapshot, out _);
        }

        private static bool IsFresh(long cachedAtUnixSeconds, DateTimeOffset now)
        {
            var ageSeconds = now.ToUnixTimeSeconds() - cachedAtUnixSeconds;
            return ageSeconds >= 0 && ageSeconds < CacheTtlSeconds;
        }

        private static LiveOpsLoadResult CachedOrFailed(
            bool hasCache,
            LiveOpsSnapshot cachedSnapshot,
            string error)
        {
            return hasCache
                ? new LiveOpsLoadResult(
                    LiveOpsLoadStatus.Degraded, cachedSnapshot, error, true)
                : new LiveOpsLoadResult(
                    LiveOpsLoadStatus.Failed, LiveOpsSnapshot.Empty(), error, false);
        }

        private static string AddOriginCacheBuster(string url)
        {
            var separator = url.Contains("?") ? "&" : "?";
            return url + separator + "sdkRefresh=" +
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + "-" + Guid.NewGuid().ToString("N");
        }
    }
}
