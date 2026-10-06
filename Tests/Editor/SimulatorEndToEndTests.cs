using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using susaplay.SDK.Editor.Simulator;
using UnityEngine;
using UnityEngine.TestTools;

namespace susaplay.SDK.Editor.Tests
{
    // The real SDK modules, over the real HttpClient and bridge, answered by a simulator. Nothing
    // here touches the network: this is the path a game takes in Play Mode.
    public sealed class SimulatorEndToEndTests
    {
        private SimulatorFixture _sim;
        private Action<string> _savedOutbound;
        private Func<EditorHttpRequest, Task<EditorHttpAnswer>> _savedTransport;

        [SetUp]
        public void SetUp()
        {
            _savedOutbound = WebGLBridge.EditorOutbound;
            _savedTransport = HttpClient.EditorTransport;
            SusaPlaySDK.ResetStaticState();

            _sim = new SimulatorFixture();
            _sim.Shell.Deliver = WebGLBridge.DeliverFromEditor;
            WebGLBridge.EditorOutbound = _sim.Shell.OnOutbound;
            HttpClient.EditorTransport = _sim.Shell.OnHttp;
            _sim.Send("SDK_INIT", "{\"gameKey\":\"gk_test\"}");
        }

        [TearDown]
        public void TearDown()
        {
            WebGLBridge.EditorOutbound = _savedOutbound;
            HttpClient.EditorTransport = _savedTransport;
            SusaPlaySDK.ResetStaticState();
        }

        private HttpClient CreateClient()
        {
            var tokens = new TokenManager();
            tokens.Initialize();
            return new HttpClient(ScriptableObject.CreateInstance<SDKConfig>(), tokens);
        }

        // Waits on real time, not frames: in an EditMode test an await may resume on the thread
        // pool rather than the main thread, and batch-mode frames are far shorter than its delay.
        private IEnumerator Run(Task task)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (!task.IsCompleted && clock.Elapsed.TotalSeconds < 10)
            {
                _sim.Advance(0.05);
                yield return null;
            }
            Assert.IsTrue(task.IsCompleted, "the simulator never answered");
            if (task.IsFaulted)
            {
                throw task.Exception;
            }
        }

        [UnityTest]
        public IEnumerator ASaveRoundTripsAndAStaleWriteConflicts()
        {
            var http = CreateClient();
            var saves = new CloudSaveModule(http, SimulatorFixture.GameId);

            var save = saves.Save("slot1", "{\"coins\":5}");
            yield return Run(save);
            Assert.IsTrue(save.Result.Success);

            var load = saves.Load("slot1");
            yield return Run(load);
            Assert.IsTrue(load.Result.Success);
            StringAssert.Contains("\"coins\": 5", load.Result.Data);

            var stale = http.Post("/save/write", "{\"gameId\":\"game_1\",\"slot\":\"slot1\",\"data\":{},\"version\":0}");
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("VERSION_CONFLICT"));
            yield return Run(stale);
            Assert.AreEqual(409, stale.Result.StatusCode);
            StringAssert.StartsWith("HTTP/1.1 409 Conflict — ", stale.Result.Error);
        }

        [UnityTest]
        public IEnumerator AnAchievementUnlocksThroughTheModule()
        {
            var achievements = new AchievementsModule(CreateClient(), SimulatorFixture.GameId);

            var unlock = achievements.Unlock("first_win");
            yield return Run(unlock);

            Assert.IsTrue(unlock.Result.Success, unlock.Result.Error);
            Assert.IsTrue(_sim.State.FindAchievement(SimulatorFixture.GameId, "first_win", false).unlocked);
        }

        [UnityTest]
        public IEnumerator APaidPurchaseShowsInTheInventory()
        {
            var purchases = new PurchasesModule(CreateClient(), SimulatorFixture.GameId);
            purchases.Initialize();

            var purchase = purchases.StartDirectItemPurchase("sword");
            yield return Run(purchase);
            Assert.IsTrue(purchase.Result.Success, purchase.Result.ErrorMessage);
            Assert.AreEqual("paid", purchase.Result.Status);

            var inventory = purchases.GetInventory();
            yield return Run(inventory);
            Assert.AreEqual("sword", inventory.Result.Items[0].key);
        }

        [UnityTest]
        public IEnumerator ARewardedAdReportsRewarded()
        {
            var ads = new AdsModule();
            ads.Initialize();

            var ad = ads.ShowRewarded();
            yield return Run(ad);

            Assert.IsTrue(ad.Result.Success);
            Assert.IsTrue(ad.Result.Rewarded);
        }
    }

    public sealed class SimulatorStateStoreTests
    {
        private string _directory;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "susaplay-sim-" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, true);
            }
        }

        [Test]
        public void TheStateSurvivesASaveAndLoad()
        {
            var store = new SimulatorStateStore(_directory);
            var state = store.Load();
            state.saves.Add(new SaveRecord { gameId = "g", slot = "a", data = "{}", version = 3 });

            store.Save(state);
            var loaded = new SimulatorStateStore(_directory).Load();

            Assert.AreEqual(state.uid, loaded.uid);
            Assert.AreEqual(3, loaded.FindSave("g", "a").version);
        }

        [Test]
        public void AnUnreadableFileIsSetAside()
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(Path.Combine(_directory, "state.json"), "{ not json");
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("set aside"));

            var state = new SimulatorStateStore(_directory).Load();

            StringAssert.StartsWith("sim_", state.uid);
            Assert.IsTrue(File.Exists(Path.Combine(_directory, "state.json.bak")));
        }
    }

    public sealed class SimJsonTests
    {
        [Test]
        public void WholeNumbersAndFractionsStayApart()
        {
            Assert.IsInstanceOf<long>(SimJson.Parse("1"));
            Assert.IsInstanceOf<double>(SimJson.Parse("1.0"));
        }

        [Test]
        public void PythonOutputMatchesJsonDumps()
        {
            var value = SimJson.Parse("{\"a\":[1,2.5,true,null],\"b\":\"ü\",\"c\":2.0}");

            Assert.AreEqual("{\"a\": [1, 2.5, true, null], \"b\": \"\\u00fc\", \"c\": 2.0}", SimJson.SerializePython(value));
        }

        [Test]
        public void InvalidJsonIsNullLikeFlasksSilentParse()
        {
            Assert.IsNull(SimJson.TryParse("{\"a\":"));
        }
    }
}
