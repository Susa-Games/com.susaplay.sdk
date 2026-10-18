using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;

namespace susaplay.SDK.Tests
{
    public sealed class CloudSaveModuleTests
    {
        [Test]
        public async Task ConcurrentSavesToOneSlotNeverOverlap()
        {
            var transport = new FakeTransport();
            var module = CreateModule(transport);
            transport.HoldNextRequests();

            var first = module.Save("slot1", "{\"coins\":1}");
            var second = module.Save("slot1", "{\"coins\":2}");
            transport.ReleaseHeldRequests();
            await Task.WhenAll(first, second);

            // The overlapping request is what produced VERSION_CONFLICT storms in production.
            Assert.AreEqual(1, transport.MaxConcurrentPosts);
        }

        [Test]
        public async Task SavesQueuedDuringAnInFlightRequestCoalesceToTheNewestPayload()
        {
            var transport = new FakeTransport();
            var module = CreateModule(transport);
            transport.HoldNextRequests();

            var first = module.Save("slot1", "{\"coins\":1}");
            var second = module.Save("slot1", "{\"coins\":2}");
            var third = module.Save("slot1", "{\"coins\":3}");
            transport.ReleaseHeldRequests();
            var results = await Task.WhenAll(first, second, third);

            Assert.AreEqual(2, transport.PostBodies.Count, "three callers must collapse into two writes");
            StringAssert.Contains("\"coins\":1", transport.PostBodies[0]);
            StringAssert.Contains("\"coins\":3", transport.PostBodies[1]);
            foreach (var result in results)
            {
                Assert.IsTrue(result.Success);
            }
        }

        [Test]
        public async Task SecondWriteSendsTheVersionTheServerAcknowledged()
        {
            var transport = new FakeTransport();
            var module = CreateModule(transport);

            await module.Save("slot1", "{}");
            await module.Save("slot1", "{}");

            StringAssert.Contains("\"version\":0", transport.PostBodies[0]);
            StringAssert.Contains("\"version\":1", transport.PostBodies[1]);
        }

        [Test]
        public async Task ConflictAdoptsTheServerVersionAndRetries()
        {
            var transport = new FakeTransport
            {
                PostResponder = index => index == 0
                    ? HttpResponse.Fail("HTTP/1.1 409 Conflict", 409)
                    : SaveOk(6),
                GetResponse = ReadOk(5),
            };
            var module = CreateModule(transport);

            var result = await module.Save("slot1", "{}");

            Assert.IsTrue(result.Success);
            Assert.AreEqual(6, result.Version);
            Assert.AreEqual(2, transport.PostBodies.Count);
            StringAssert.Contains("\"version\":5", transport.PostBodies[1]);
        }

        [Test]
        public async Task CoalescedCallersAllObserveATransportFailure()
        {
            var transport = new FakeTransport
            {
                PostResponder = _ => HttpResponse.Fail("offline", 0),
            };
            var module = CreateModule(transport);
            transport.HoldNextRequests();

            var first = module.Save("slot1", "{\"coins\":1}");
            var second = module.Save("slot1", "{\"coins\":2}");
            transport.ReleaseHeldRequests();
            var results = await Task.WhenAll(first, second);

            // Awaiters must never hang when a write fails.
            foreach (var result in results)
            {
                Assert.IsFalse(result.Success);
                Assert.AreEqual("offline", result.Error);
            }
        }

        [Test]
        public async Task DifferentSlotsAreNotSerialisedAgainstEachOther()
        {
            var transport = new FakeTransport();
            var module = CreateModule(transport);
            transport.HoldNextRequests();

            var first = module.Save("slot1", "{}");
            var second = module.Save("slot2", "{}");
            transport.ReleaseHeldRequests();
            await Task.WhenAll(first, second);

            Assert.AreEqual(2, transport.MaxConcurrentPosts);
        }

        [Test]
        public async Task EmptySlotAndEmptyPayloadFailWithoutARequest()
        {
            var transport = new FakeTransport();
            var module = CreateModule(transport);

            var noSlot = await module.Save("", "{}");
            var noData = await module.Save("slot1", "");

            Assert.IsFalse(noSlot.Success);
            Assert.IsFalse(noData.Success);
            Assert.AreEqual(0, transport.PostBodies.Count);
        }

        private static CloudSaveModule CreateModule(FakeTransport transport)
        {
            // No throttle in tests — the interval is production pacing, not part of the ordering
            // guarantee under test.
            return new CloudSaveModule(transport, "game-1", 0f);
        }

        private static HttpResponse SaveOk(int version)
        {
            return new HttpResponse
            {
                Success = true,
                StatusCode = 200,
                Data = "{\"success\":true,\"data\":{\"slot\":\"slot1\",\"version\":" + version + "}}",
            };
        }

        private static HttpResponse ReadOk(int version)
        {
            return new HttpResponse
            {
                Success = true,
                StatusCode = 200,
                Data = "{\"success\":true,\"data\":{\"slot\":\"slot1\",\"data\":\"{}\",\"version\":" + version + "}}",
            };
        }

        private sealed class FakeTransport : ICloudSaveTransport
        {
            public readonly List<string> PostBodies = new List<string>();
            public readonly List<string> GetEndpoints = new List<string>();
            public int MaxConcurrentPosts;
            public Func<int, HttpResponse> PostResponder;
            public HttpResponse GetResponse;

            private int _activePosts;
            private TaskCompletionSource<bool> _gate;

            public void HoldNextRequests()
            {
                _gate = new TaskCompletionSource<bool>();
            }

            public void ReleaseHeldRequests()
            {
                var gate = _gate;
                _gate = null;
                if (gate != null)
                {
                    gate.TrySetResult(true);
                }
            }

            public async Task<HttpResponse> Post(string endpoint, string body)
            {
                var index = PostBodies.Count;
                PostBodies.Add(body);
                _activePosts++;
                if (_activePosts > MaxConcurrentPosts)
                {
                    MaxConcurrentPosts = _activePosts;
                }

                var gate = _gate;
                if (gate != null)
                {
                    await gate.Task;
                }

                _activePosts--;
                return PostResponder != null ? PostResponder(index) : SaveOk(index + 1);
            }

            public Task<HttpResponse> Get(string endpoint)
            {
                GetEndpoints.Add(endpoint);
                return Task.FromResult(GetResponse ?? ReadOk(0));
            }
        }
    }
}
