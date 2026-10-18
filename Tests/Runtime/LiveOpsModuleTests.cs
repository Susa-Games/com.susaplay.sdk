using System;
using System.Threading.Tasks;
using NUnit.Framework;

namespace susaplay.SDK.Tests
{
    public sealed class LiveOpsModuleTests
    {
        private const string EmptyManifest =
            "{\"schemaVersion\":1,\"version\":\"123\",\"generatedAt\":\"2026-08-07T10:00:00Z\"," +
            "\"enabled\":true,\"missions\":[],\"discountOffers\":[]}";

        [Test]
        public async Task FreshCacheShortCircuitsWithoutNetworkRequest()
        {
            var transport = new FakeTransport();
            var cache = new FakeCache(EmptyManifest, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            var module = CreateModule(transport, cache);

            var result = await module.RefreshAsync();

            Assert.AreEqual(LiveOpsLoadStatus.Cached, result.Status);
            Assert.IsTrue(result.IsFromCache);
            Assert.AreEqual(0, transport.CallCount);
        }

        [Test]
        public async Task ForceOriginBypassesFreshCacheAndAddsUniqueQuery()
        {
            var transport = new FakeTransport { Response = Success(EmptyManifest) };
            var cache = new FakeCache(EmptyManifest, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            var module = CreateModule(transport, cache);

            var result = await module.RefreshAsync(true);

            Assert.AreEqual(LiveOpsLoadStatus.Success, result.Status);
            Assert.AreEqual(1, transport.CallCount);
            StringAssert.Contains("?sdkRefresh=", transport.LastUrl);
        }

        [Test]
        public async Task First404ReturnsNotConfigured()
        {
            var transport = new FakeTransport { Response = Failure(404, "not found") };
            var module = CreateModule(transport, new FakeCache());

            var result = await module.RefreshAsync();

            Assert.AreEqual(LiveOpsLoadStatus.NotConfigured, result.Status);
            Assert.IsFalse(result.IsFromCache);
            Assert.IsTrue(result.Success);
        }

        [Test]
        public async Task StaleCacheIsKeptOn404()
        {
            var transport = new FakeTransport { Response = Failure(404, "not found") };
            var cache = StaleCache(EmptyManifest);

            var result = await CreateModule(transport, cache).RefreshAsync();

            Assert.AreEqual(LiveOpsLoadStatus.Degraded, result.Status);
            Assert.IsTrue(result.IsFromCache);
        }

        [TestCase(500)]
        [TestCase(0)]
        public async Task StaleCacheIsKeptOnServerOrNetworkFailure(long statusCode)
        {
            var transport = new FakeTransport { Response = Failure(statusCode, "offline") };

            var result = await CreateModule(transport, StaleCache(EmptyManifest)).RefreshAsync();

            Assert.AreEqual(LiveOpsLoadStatus.Degraded, result.Status);
            Assert.IsTrue(result.IsFromCache);
        }

        [Test]
        public async Task InvalidJsonDoesNotReplaceValidCache()
        {
            var transport = new FakeTransport { Response = Success("{invalid") };
            var cache = StaleCache(EmptyManifest);

            var result = await CreateModule(transport, cache).RefreshAsync();

            Assert.AreEqual(LiveOpsLoadStatus.Degraded, result.Status);
            Assert.AreEqual(0, cache.SaveCount);
            Assert.AreEqual("123", result.Snapshot.Version);
        }

        [Test]
        public async Task FailureWithoutCacheReturnsEmptyFailure()
        {
            var transport = new FakeTransport { Response = Failure(500, "server") };

            var result = await CreateModule(transport, new FakeCache()).RefreshAsync();

            Assert.AreEqual(LiveOpsLoadStatus.Failed, result.Status);
            Assert.IsFalse(result.Success);
            Assert.AreEqual(0, result.Snapshot.Missions.Count);
        }

        [Test]
        public async Task SuccessfulEmptyManifestReplacesStaleCache()
        {
            var oldManifest = EmptyManifest.Replace("\"123\"", "\"old\"");
            var cache = StaleCache(oldManifest);
            var transport = new FakeTransport { Response = Success(EmptyManifest) };

            var result = await CreateModule(transport, cache).RefreshAsync();

            Assert.AreEqual(LiveOpsLoadStatus.Success, result.Status);
            Assert.AreEqual(1, cache.SaveCount);
            Assert.AreEqual("123", result.Snapshot.Version);
            Assert.AreEqual(0, result.Snapshot.DiscountOffers.Count);
        }

        [Test]
        public async Task MalformedPersistentCacheIsIgnoredSafely()
        {
            var cache = new FakeCache("{bad", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            var transport = new FakeTransport { Response = Failure(404, "not found") };

            var result = await CreateModule(transport, cache).RefreshAsync();

            Assert.AreEqual(LiveOpsLoadStatus.NotConfigured, result.Status);
            Assert.IsFalse(result.IsFromCache);
        }

        [Test]
        public void ParserModelsAllFourTypesAndFiltersExpiredCampaigns()
        {
            var json =
                "{\"schemaVersion\":1,\"version\":\"all-types\",\"generatedAt\":\"2026-08-07T10:00:00Z\"," +
                "\"enabled\":true," +
                "\"missions\":[" +
                Campaign("m1", "liveop", "{\"missionID\":\"Win_Levels\",\"headerText\":\"Win\"}", "2026-08-08T00:00:00Z") + "," +
                Campaign("expired", "liveop", "{\"missionID\":\"Old\"}", "2026-08-07T09:00:00Z") +
                "],\"discountOffers\":[" +
                Campaign("o1", "SingleOffer", "{\"productID\":\"pack\",\"discountRatio\":0.25,\"moneyValue\":{\"rewards\":[{\"type\":\"Diamond\",\"amount\":100}]}}", "2026-08-08T00:00:00Z") + "," +
                Campaign("o2", "ChainOfOffers", "{\"steps\":[{\"productID\":\"free\"},{\"productID\":\"pack\",\"discountRatio\":0.1}]}", "2026-08-08T00:00:00Z") + "," +
                Campaign("o3", "InAppBoosted", "{\"productID\":\"diamonds\",\"amountIncreaseRatio\":0.5}", "2026-08-08T00:00:00Z") +
                "]}";

            var valid = LiveOpsManifestParser.TryParse(
                json,
                new DateTimeOffset(2026, 8, 7, 10, 0, 0, TimeSpan.Zero),
                out var snapshot,
                out var error);

            Assert.IsTrue(valid, error);
            Assert.AreEqual(1, snapshot.Missions.Count);
            Assert.AreEqual(3, snapshot.DiscountOffers.Count);
            Assert.IsInstanceOf<SingleOffer>(snapshot.DiscountOffers[0]);
            Assert.IsInstanceOf<ChainOfOffers>(snapshot.DiscountOffers[1]);
            Assert.IsInstanceOf<InAppBoosted>(snapshot.DiscountOffers[2]);
        }

        [Test]
        public async Task MalformedKnownTypePayloadDoesNotReplaceCache()
        {
            var malformed = EmptyManifest.Replace(
                "\"discountOffers\":[]",
                "\"discountOffers\":[" +
                Campaign("o1", "SingleOffer", "{}", "2026-08-08T00:00:00Z") + "]");
            var transport = new FakeTransport { Response = Success(malformed) };
            var cache = StaleCache(EmptyManifest);

            var result = await CreateModule(transport, cache).RefreshAsync();

            Assert.AreEqual(LiveOpsLoadStatus.Degraded, result.Status);
            Assert.AreEqual(0, cache.SaveCount);
        }

        private static string Campaign(string id, string type, string payload, string endDate)
        {
            return "{\"campaignId\":\"" + id + "\",\"type\":\"" + type +
                "\",\"regionTags\":[\"GLOBAL\"],\"startDate\":\"2026-08-07T00:00:00Z\"," +
                "\"endDate\":\"" + endDate + "\",\"payload\":" + payload + "}";
        }

        private static LiveOpsModule CreateModule(FakeTransport transport, FakeCache cache)
        {
            return new LiveOpsModule("https://storage.example", "game-1", transport, cache);
        }

        private static FakeCache StaleCache(string json)
        {
            return new FakeCache(json, DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds());
        }

        private static LiveOpsTransportResponse Success(string json)
        {
            return new LiveOpsTransportResponse { Success = true, StatusCode = 200, Data = json };
        }

        private static LiveOpsTransportResponse Failure(long statusCode, string error)
        {
            return new LiveOpsTransportResponse
                { Success = false, StatusCode = statusCode, Error = error };
        }

        private sealed class FakeTransport : ILiveOpsTransport
        {
            internal LiveOpsTransportResponse Response = Failure(500, "No response configured");
            internal int CallCount;
            internal string LastUrl;

            public Task<LiveOpsTransportResponse> GetAsync(string url)
            {
                CallCount++;
                LastUrl = url;
                return Task.FromResult(Response);
            }
        }

        private sealed class FakeCache : ILiveOpsCacheStore
        {
            private string _json;
            private long _cachedAt;

            internal FakeCache(string json = null, long cachedAt = 0)
            {
                _json = json;
                _cachedAt = cachedAt;
            }

            internal int SaveCount { get; private set; }

            public bool TryLoad(out string json, out long cachedAtUnixSeconds)
            {
                json = _json;
                cachedAtUnixSeconds = _cachedAt;
                return !string.IsNullOrEmpty(_json);
            }

            public void Save(string json, long cachedAtUnixSeconds)
            {
                SaveCount++;
                _json = json;
                _cachedAt = cachedAtUnixSeconds;
            }
        }
    }
}
