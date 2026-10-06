using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;

namespace susaplay.SDK.Tests
{
    // A Play session with domain reload turned off keeps every static from the one before.
    // These tests stand in for the second Play: they leave state behind as a first session
    // would, run the reset Unity calls at SubsystemRegistration, and check nothing survived.
    public sealed class StaticStateResetTests
    {
        private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;

        [TearDown]
        public void TearDown()
        {
            SusaPlaySDK.ResetStaticState();
        }

        [Test]
        public void ResetUnsubscribesHandlersFromEarlierSessions()
        {
            var calls = 0;
            WebGLBridge.OnMessageReceived += _ => calls++;

            SusaPlaySDK.ResetStaticState();
            DeliverToBridge("{\"type\":\"SDK_READY\",\"payload\":\"{}\"}");

            // A stale handler here is what let one SDK_READY complete init sources from
            // sessions that no longer existed.
            Assert.AreEqual(0, calls);
        }

        [Test]
        public void ResetDropsTheEarlierSessionsInitTask()
        {
            SetPrivate("_initTask", Task.CompletedTask);

            SusaPlaySDK.ResetStaticState();

            // Initialize() hands back any cached task, so a leftover one meant the second
            // Play never initialized at all.
            Assert.IsNull(GetPrivate("_initTask"));
        }

        [Test]
        public void ResetClearsInitializationFlagsAndGameId()
        {
            SetPrivate("_isInitialized", true);
            SetPrivate("_didSendGameLoaded", true);
            typeof(SusaPlaySDK).GetProperty(nameof(SusaPlaySDK.GameId)).SetValue(null, "game-from-last-session");

            SusaPlaySDK.ResetStaticState();

            Assert.IsFalse((bool)GetPrivate("_isInitialized"));
            Assert.IsFalse((bool)GetPrivate("_didSendGameLoaded"));
            Assert.AreEqual("", SusaPlaySDK.GameId);
        }

        [Test]
        public void ResetLeavesEveryModuleUnset()
        {
            SusaPlaySDK.ResetStaticState();

            Assert.IsNull(SusaPlaySDK.Auth);
            Assert.IsNull(SusaPlaySDK.CloudSave);
            Assert.IsNull(SusaPlaySDK.Achievements);
            Assert.IsNull(SusaPlaySDK.Analytics);
            Assert.IsNull(SusaPlaySDK.Webhooks);
            Assert.IsNull(SusaPlaySDK.Purchases);
            Assert.IsNull(SusaPlaySDK.Ads);
            Assert.IsNull(SusaPlaySDK.Api);
            Assert.IsNull(SusaPlaySDK.Backend);
            Assert.IsNull(SusaPlaySDK.LiveOps);
        }

        private static void DeliverToBridge(string json)
        {
            typeof(WebGLBridge).GetMethod("HandleMessage", PrivateStatic).Invoke(null, new object[] { json });
        }

        private static object GetPrivate(string field)
        {
            return typeof(SusaPlaySDK).GetField(field, PrivateStatic).GetValue(null);
        }

        private static void SetPrivate(string field, object value)
        {
            typeof(SusaPlaySDK).GetField(field, PrivateStatic).SetValue(null, value);
        }
    }
}
