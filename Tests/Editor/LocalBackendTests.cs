using System.Collections.Generic;
using NUnit.Framework;
using susaplay.SDK.Editor.Simulator;

namespace susaplay.SDK.Editor.Tests
{
    // The platform's rules, as the local backend reproduces them. Each expectation is what the
    // platform's own handler does for the same request; the comment names the handler when the
    // behaviour is surprising.
    public sealed class LocalBackendTests
    {
        private SimulatorFixture _sim;

        [SetUp]
        public void SetUp()
        {
            _sim = new SimulatorFixture();
        }

        // ── Authentication ──────────────────────────────────────────────────────────────────

        [Test]
        public void AGuestGets401OnEveryRoute()
        {
            // HttpClient sends "Bearer " + null for a guest.
            var response = _sim.Get("/save/read?gameId=game_1&slot=a", "Bearer ");

            Assert.AreEqual(401, response.Status);
            Assert.AreEqual("UNAUTHENTICATED", response.ErrorCode);
            Assert.AreEqual("Missing or invalid Authorization header", response.ErrorMessage);
        }

        [Test]
        public void ATokenFromAnEarlierPlayerIsRefused()
        {
            var response = _sim.Get("/save/read?gameId=game_1&slot=a", "Bearer sim1.sim_someoneelse");

            Assert.AreEqual(401, response.Status);
            Assert.AreEqual("Invalid token", response.ErrorMessage);
        }

        [Test]
        public void AnotherGamesIdIsForbidden()
        {
            var response = _sim.Get("/save/read?gameId=other_game&slot=a");

            Assert.AreEqual(403, response.Status);
            Assert.AreEqual("FORBIDDEN", response.ErrorCode);
        }

        [Test]
        public void TheTokenSuppliesTheGameWhenTheRequestLeavesItOut()
        {
            var response = _sim.Get("/economy/inventory");

            Assert.AreEqual(200, response.Status);
            Assert.AreEqual("game_1", SimulatorFixture.Data(response)["gameId"]);
        }

        [Test]
        public void AnUnknownRouteFailsVisibly()
        {
            var response = _sim.Get("/economy/wallet?gameId=game_1");

            Assert.AreEqual(501, response.Status);
            Assert.AreEqual("SIMULATOR_UNSUPPORTED_ROUTE", response.ErrorCode);
            StringAssert.Contains("GET /economy/wallet", response.ErrorMessage);
        }

        [Test]
        public void ResponsesAreWrittenAsThePlatformWritesThem()
        {
            var response = _sim.Get("/save/read?gameId=game_1&slot=a");

            Assert.AreEqual(
                "{\"success\": true, \"data\": {\"slot\": \"a\", \"data\": \"\", \"version\": 0, \"savedAt\": null, \"exists\": false}}",
                response.BodyText);
        }

        // ── Saves ───────────────────────────────────────────────────────────────────────────

        [Test]
        public void ANewSlotAcceptsAnyVersionAndStoresTheNextOne()
        {
            var response = _sim.Post("/save/write", "{\"gameId\":\"game_1\",\"slot\":\"a\",\"data\":{\"coins\":5},\"version\":7}");

            Assert.AreEqual(200, response.Status);
            Assert.AreEqual(8L, SimulatorFixture.Data(response)["version"]);
        }

        [Test]
        public void AStaleVersionIsAConflict()
        {
            _sim.Post("/save/write", "{\"gameId\":\"game_1\",\"slot\":\"a\",\"data\":{},\"version\":0}");

            var response = _sim.Post("/save/write", "{\"gameId\":\"game_1\",\"slot\":\"a\",\"data\":{},\"version\":0}");

            Assert.AreEqual(409, response.Status);
            Assert.AreEqual("VERSION_CONFLICT", response.ErrorCode);
        }

        [Test]
        public void AnObjectIsStoredAsPythonSerializesIt()
        {
            _sim.Post("/save/write", "{\"gameId\":\"game_1\",\"slot\":\"a\",\"data\":{\"coins\":5,\"name\":\"é\"},\"version\":0}");

            var data = SimulatorFixture.Data(_sim.Get("/save/read?gameId=game_1&slot=a"));

            // json.dumps: ", " and ": " separators and non-ASCII escaped.
            Assert.AreEqual("{\"coins\": 5, \"name\": \"\\u00e9\"}", data["data"]);
            Assert.AreEqual(1L, data["version"]);
            Assert.AreEqual(true, data["exists"]);
        }

        [Test]
        public void AFloatVersionIsRefused()
        {
            var response = _sim.Post("/save/write", "{\"gameId\":\"game_1\",\"slot\":\"a\",\"data\":{},\"version\":1.0}");

            Assert.AreEqual(400, response.Status);
            Assert.AreEqual("Field 'version' must be a non-negative integer", response.ErrorMessage);
        }

        [Test]
        public void DataOverTheLimitIsRefused()
        {
            var big = new string('x', 500_001);

            var response = _sim.Post("/save/write", "{\"gameId\":\"game_1\",\"slot\":\"a\",\"data\":\"" + big + "\",\"version\":0}");

            Assert.AreEqual(400, response.Status);
            Assert.AreEqual("Data exceeds 500KB limit", response.ErrorMessage);
        }

        [Test]
        public void NullDataIsRefused()
        {
            var response = _sim.Post("/save/write", "{\"gameId\":\"game_1\",\"slot\":\"a\",\"data\":null,\"version\":0}");

            Assert.AreEqual("Field 'data' cannot be null", response.ErrorMessage);
        }

        // ── Achievements ────────────────────────────────────────────────────────────────────

        [Test]
        public void AnUnregisteredAchievementIsNotFound()
        {
            var response = _sim.Post("/engagement/achievements/unlock", "{\"gameId\":\"game_1\",\"achievementId\":\"frist_win\"}");

            Assert.AreEqual(404, response.Status);
            Assert.AreEqual("NOT_FOUND", response.ErrorCode);
        }

        [Test]
        public void WithoutAConfigurationEveryAchievementIsNotFound()
        {
            var sim = new SimulatorFixture(withConfig: false);

            var response = sim.Post("/engagement/achievements/unlock", "{\"gameId\":\"gk_test\",\"achievementId\":\"first_win\"}");

            Assert.AreEqual(404, response.Status);
        }

        [Test]
        public void UnlockingAwardsPointsOnceAndRaisesTheLevel()
        {
            var first = SimulatorFixture.Data(_sim.Post("/engagement/achievements/unlock", "{\"gameId\":\"game_1\",\"achievementId\":\"first_win\"}"));
            var second = SimulatorFixture.Data(_sim.Post("/engagement/achievements/unlock", "{\"gameId\":\"game_1\",\"achievementId\":\"first_win\"}"));

            Assert.AreEqual(true, first["newlyUnlocked"]);
            Assert.AreEqual(false, second["newlyUnlocked"]);
            Assert.AreEqual(first["unlockedAt"], second["unlockedAt"]);
            // Level 2 starts at 100 XP.
            Assert.AreEqual(2L, ((Dictionary<string, object>)second["progression"])["level"]);
            Assert.AreEqual(100L, _sim.State.xp);
        }

        [Test]
        public void UnlockRefusesAnIncrementalAchievement()
        {
            var response = _sim.Post("/engagement/achievements/unlock", "{\"gameId\":\"game_1\",\"achievementId\":\"collector\"}");

            Assert.AreEqual(400, response.Status);
            Assert.AreEqual("Achievement is not oneshot", response.ErrorMessage);
        }

        [Test]
        public void IncrementUnlocksAtTheTarget()
        {
            _sim.Post("/engagement/achievements/increment", "{\"gameId\":\"game_1\",\"achievementId\":\"collector\",\"incrementBy\":2}");
            var data = SimulatorFixture.Data(_sim.Post("/engagement/achievements/increment", "{\"gameId\":\"game_1\",\"achievementId\":\"collector\"}"));

            Assert.AreEqual(3L, data["currentValue"]);
            Assert.AreEqual(true, data["justUnlocked"]);
            Assert.AreEqual(50L, data["points"]);
        }

        [Test]
        public void IncrementRefusesANonPositiveAmount()
        {
            var response = _sim.Post("/engagement/achievements/increment", "{\"gameId\":\"game_1\",\"achievementId\":\"collector\",\"incrementBy\":0}");

            Assert.AreEqual("incrementBy must be > 0", response.ErrorMessage);
        }

        [Test]
        public void TheListShowsDefinitionsWithProgress()
        {
            _sim.Post("/engagement/achievements/increment", "{\"gameId\":\"game_1\",\"achievementId\":\"collector\"}");

            var list = (List<object>)SimulatorFixture.Data(_sim.Get("/engagement/achievements/list?gameId=game_1"))["achievements"];
            var collector = (Dictionary<string, object>)list[0];
            var firstWin = (Dictionary<string, object>)list[1];

            Assert.AreEqual("collector", collector["achievementId"]);
            Assert.AreEqual(1L, collector["currentValue"]);
            Assert.AreEqual(false, collector["unlocked"]);
            Assert.IsNull(firstWin["currentValue"], "a oneshot achievement has no progress");
        }

        // ── Analytics and custom events ─────────────────────────────────────────────────────

        [Test]
        public void UnnamedAnalyticsEventsAreDroppedWithAWarning()
        {
            var response = _sim.Post("/analytics/event",
                "{\"gameId\":\"game_1\",\"sessionId\":\"s\",\"events\":[{\"name\":\"a\"},{\"name\":\"  \"},{\"params\":{}}]}");

            Assert.AreEqual(200, response.Status);
            Assert.AreEqual(1L, SimulatorFixture.Data(response)["accepted"]);
            Assert.AreEqual(0L, SimulatorFixture.Data(response)["delivered"]);
            Assert.AreEqual(1, response.Warnings.Count);
        }

        [Test]
        public void AnAnalyticsLevelUpWithoutALevelIsDroppedWithAWarning()
        {
            var response = _sim.Post("/analytics/event",
                "{\"gameId\":\"game_1\",\"sessionId\":\"s\",\"events\":[{\"name\":\"level_up\",\"params\":\"{}\"},{\"name\":\"level_up\",\"params\":{\"level\":2}}]}");

            Assert.AreEqual(200, response.Status);
            Assert.AreEqual(1L, SimulatorFixture.Data(response)["accepted"]);
            Assert.AreEqual(1, response.Warnings.Count);
        }

        [Test]
        public void AnAnalyticsBatchWithNoEventsIsRefused()
        {
            var response = _sim.Post("/analytics/event", "{\"gameId\":\"game_1\",\"sessionId\":\"s\",\"events\":[]}");

            Assert.AreEqual("Missing required field: events[]", response.ErrorMessage);
        }

        [Test]
        public void ACustomLevelUpWithoutALevelIsRefused()
        {
            var response = _sim.Post("/webhooks/event",
                "{\"gameId\":\"game_1\",\"sessionId\":\"s\",\"eventName\":\"level_up\",\"payload\":{\"parameters\":{}}}");

            Assert.AreEqual(400, response.Status);
            Assert.AreEqual("level_up requires payload.level as a non-negative integer", response.ErrorMessage);
        }

        [Test]
        public void ACustomLevelUpReadsTheLevelFromValue()
        {
            // WebhooksModule.SendEvent("level_up", 4) puts the level in "value".
            var response = _sim.Post("/webhooks/event",
                "{\"gameId\":\"game_1\",\"sessionId\":\"s\",\"eventName\":\"level_up\",\"payload\":{\"value\":4,\"parameters\":{}}}");

            Assert.AreEqual(200, response.Status);
        }

        [Test]
        public void ACustomAchievementUnlockedEventUnlocks()
        {
            var response = _sim.Post("/webhooks/event",
                "{\"gameId\":\"game_1\",\"sessionId\":\"s\",\"eventName\":\"achievement_unlocked\",\"payload\":{\"parameters\":{\"achievementId\":\"first_win\"}}}");

            var achievement = (Dictionary<string, object>)SimulatorFixture.Data(response)["achievement"];
            Assert.AreEqual(true, achievement["newlyUnlocked"]);
        }

        // ── Store, wallet and inventory ─────────────────────────────────────────────────────

        [Test]
        public void TheStoreIsThePlatformsOwnAnswer()
        {
            var data = SimulatorFixture.Data(_sim.Get("/economy/store-items?gameId=game_1"));

            Assert.AreEqual("sword", ((Dictionary<string, object>)((List<object>)data["items"])[0])["itemId"]);
        }

        [Test]
        public void WithoutAConfigurationTheStoreIsEmpty()
        {
            var sim = new SimulatorFixture(withConfig: false);

            var data = SimulatorFixture.Data(sim.Get("/economy/store-items?gameId=gk_test"));

            Assert.AreEqual(0, ((List<object>)data["items"]).Count);
        }

        [Test]
        public void TheFirstWalletReadRunsTheMigration()
        {
            var data = SimulatorFixture.Data(_sim.Get("/economy/platform-wallet"));

            Assert.AreEqual("platform", data["walletId"]);
            Assert.AreEqual("live", data["walletScope"]);
            Assert.AreEqual(1L, data["version"]);
            Assert.AreEqual("completed", ((Dictionary<string, object>)data["migration"])["status"]);
        }

        [Test]
        public void SpendingMoreThanTheBalanceIsRefused()
        {
            var response = _sim.Post("/economy/platform-wallet/spend", "{\"gameId\":\"game_1\",\"itemId\":\"sword\",\"requestId\":\"r1\"}");

            Assert.AreEqual(409, response.Status);
            Assert.AreEqual("INSUFFICIENT_BALANCE", response.ErrorCode);
        }

        [Test]
        public void ADurableIsBoughtOnceAndTheSecondTryLeavesTheBalance()
        {
            _sim.State.platformWallet.coins = 100;

            var first = _sim.Post("/economy/platform-wallet/spend", "{\"gameId\":\"game_1\",\"itemId\":\"sword\",\"requestId\":\"r1\"}");
            var second = _sim.Post("/economy/platform-wallet/spend", "{\"gameId\":\"game_1\",\"itemId\":\"sword\",\"requestId\":\"r2\"}");

            Assert.AreEqual(200, first.Status);
            Assert.AreEqual(70L, ((Dictionary<string, object>)SimulatorFixture.Data(first)["newBalance"])["coins"]);
            Assert.IsTrue(SimulatorFixture.Data(first).ContainsKey("inventoryList"));
            Assert.AreEqual("DUPLICATE_ITEM", second.ErrorCode);
            Assert.AreEqual(70, _sim.State.platformWallet.coins);
        }

        [Test]
        public void ARepeatedRequestIdReplaysTheFirstAnswer()
        {
            _sim.State.platformWallet.coins = 100;

            _sim.Post("/economy/platform-wallet/spend", "{\"gameId\":\"game_1\",\"itemId\":\"potion\",\"requestId\":\"r1\"}");
            var replay = SimulatorFixture.Data(_sim.Post("/economy/platform-wallet/spend", "{\"gameId\":\"game_1\",\"itemId\":\"potion\",\"requestId\":\"r1\"}"));

            Assert.AreEqual(true, replay["duplicate"]);
            Assert.IsNull(replay["platformWallet"], "the platform's stored claim has no wallet snapshot");
            Assert.AreEqual(90, _sim.State.platformWallet.coins);
        }

        [Test]
        public void AnItemPricedInAGameCurrencyCannotBeBoughtFromThePlatformWallet()
        {
            var response = _sim.Post("/economy/platform-wallet/spend", "{\"gameId\":\"game_1\",\"itemId\":\"skin\"}");

            Assert.AreEqual("INVALID_STATE", response.ErrorCode);
        }

        [Test]
        public void ConsumeFollowsWhatThePlayerOwns()
        {
            var nothing = _sim.Post("/economy/consume", "{\"gameId\":\"game_1\",\"itemId\":\"potion\",\"quantity\":1}");
            Assert.AreEqual(404, nothing.Status, "no inventory at all");

            _sim.State.platformWallet.coins = 100;
            _sim.Post("/economy/platform-wallet/spend", "{\"gameId\":\"game_1\",\"itemId\":\"potion\"}");
            var tooMany = _sim.Post("/economy/consume", "{\"gameId\":\"game_1\",\"itemId\":\"potion\",\"quantity\":2}");
            var one = _sim.Post("/economy/consume", "{\"gameId\":\"game_1\",\"itemId\":\"potion\",\"quantity\":1}");

            Assert.AreEqual("INSUFFICIENT_ITEMS", tooMany.ErrorCode);
            Assert.AreEqual(0L, SimulatorFixture.Data(one)["remaining"]);
        }

        [Test]
        public void AZeroQuantityConsumesOne()
        {
            // int(data.get("quantity") or 1)
            _sim.State.platformWallet.coins = 100;
            _sim.Post("/economy/platform-wallet/spend", "{\"gameId\":\"game_1\",\"itemId\":\"potion\"}");

            var response = _sim.Post("/economy/consume", "{\"gameId\":\"game_1\",\"itemId\":\"potion\",\"quantity\":0}");

            Assert.AreEqual(200, response.Status);
        }

        // ── Rewarded ads ────────────────────────────────────────────────────────────────────

        [Test]
        public void AdRewardsKeepTheCooldownAndTheDailyMaximum()
        {
            Assert.IsNotNull(_sim.Backend.ClaimAdReward("game_1", false, out _));

            _sim.Now = _sim.Now.AddMinutes(10);
            Assert.IsNull(_sim.Backend.ClaimAdReward("game_1", false, out var cooldown));
            Assert.AreEqual("AD_REWARD_COOLDOWN", cooldown.ErrorCode);

            _sim.Now = _sim.Now.AddMinutes(30);
            Assert.IsNotNull(_sim.Backend.ClaimAdReward("game_1", false, out _));

            _sim.Now = _sim.Now.AddMinutes(31);
            Assert.IsNull(_sim.Backend.ClaimAdReward("game_1", false, out var limit));
            Assert.AreEqual("AD_REWARD_LIMIT_REACHED", limit.ErrorCode);

            // The day is the UTC date.
            _sim.Now = _sim.Now.Date.AddDays(1);
            Assert.IsNotNull(_sim.Backend.ClaimAdReward("game_1", false, out _));
            Assert.AreEqual(75, _sim.State.platformWallet.coins);
        }
    }
}
