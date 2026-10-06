using System.Collections.Generic;
using NUnit.Framework;
using susaplay.SDK.Editor.Simulator;
using UnityEngine.TestTools;

namespace susaplay.SDK.Editor.Tests
{
    // The play page's half of the bridge, as the Editor Shell plays it (Appendix A of the plan).
    public sealed class EditorShellTests
    {
        private SimulatorFixture _sim;

        [SetUp]
        public void SetUp()
        {
            _sim = new SimulatorFixture();
        }

        private void Init()
        {
            _sim.Send("SDK_INIT", "{\"gameKey\":\"gk_test\",\"sdkVersion\":\"1.9.0\"}");
            _sim.Advance();
        }

        [Test]
        public void NoAnswerArrivesInsideTheCallThatCausedIt()
        {
            _sim.Send("SDK_INIT", "{\"gameKey\":\"gk_test\",\"sdkVersion\":\"1.9.0\"}");

            Assert.AreEqual(0, _sim.Delivered.Count);

            _sim.Advance();
            Assert.AreEqual(1, _sim.Delivered.Count);
        }

        [Test]
        public void ReadyCarriesTheSignedInPlayerInBothForms()
        {
            Init();

            var ready = _sim.LastDelivered("SDK_READY");
            var payload = SimulatorFixture.Payload(ready);
            var player = (Dictionary<string, object>)payload["playerData"];

            Assert.AreEqual("authenticated", payload["mode"]);
            Assert.AreEqual(_sim.State.uid, player["uid"]);
            Assert.AreEqual("Simulated Player", player["displayName"]);
            Assert.AreEqual("game_1", player["gameId"]);
            Assert.IsNotEmpty((string)player["sessionId"]);
            // The shells repeat every field at the top level; ApiModule reads it there.
            Assert.AreEqual("authenticated", ready["mode"]);
        }

        [Test]
        public void WithoutAConfigurationTheGameIsKnownByItsKey()
        {
            _sim = new SimulatorFixture(withConfig: false);
            Init();

            var player = (Dictionary<string, object>)SimulatorFixture.Payload(_sim.LastDelivered("SDK_READY"))["playerData"];
            Assert.AreEqual("gk_test", player["gameId"]);
        }

        [Test]
        public void AGuestGetsTheGuestFormAndNoToken()
        {
            _sim.Settings.guest = true;
            Init();
            _sim.Send("SDK_GET_TOKEN", "{\"requestId\":\"t1\"}");
            _sim.Advance();

            var player = (Dictionary<string, object>)SimulatorFixture.Payload(_sim.LastDelivered("SDK_READY"))["playerData"];
            var token = SimulatorFixture.Payload(_sim.LastDelivered("SDK_TOKEN_RESPONSE"));
            Assert.AreEqual("guest", player["mode"]);
            Assert.IsFalse(player.ContainsKey("uid"));
            Assert.AreEqual("t1", token["requestId"]);
            Assert.IsNull(token["token"]);
        }

        [Test]
        public void ASignedInPlayerGetsTheLocalToken()
        {
            Init();
            _sim.Send("SDK_GET_TOKEN", "{\"requestId\":\"t1\"}");
            _sim.Advance();

            Assert.AreEqual("sim1." + _sim.State.uid, SimulatorFixture.Payload(_sim.LastDelivered("SDK_TOKEN_RESPONSE"))["token"]);
        }

        [Test]
        public void ThePartnerApiIsUnavailable()
        {
            Init();
            _sim.Send("SDK_API_REQUEST", "{\"target\":\"partner\",\"requestId\":\"a1\",\"method\":\"GET\",\"endpoint\":\"/x\"}");
            _sim.Advance();

            var response = _sim.LastDelivered("SDK_API_RESPONSE");
            Assert.AreEqual("a1", response["requestId"]);
            Assert.AreEqual("UNAVAILABLE", ((Dictionary<string, object>)response["error"])["code"]);
        }

        [Test]
        public void HttpAnswersTakeTheSimulatedLatency()
        {
            Init();
            var answer = _sim.Shell.OnHttp(new EditorHttpRequest
            {
                Method = "GET",
                Endpoint = "/save/read?gameId=game_1&slot=a",
                Authorization = _sim.Token,
            });

            _sim.Advance();
            Assert.IsFalse(answer.IsCompleted);

            _sim.Advance(EditorShell.HttpLatencySeconds);
            Assert.IsTrue(answer.IsCompleted);
            Assert.AreEqual(200, answer.Result.StatusCode);
        }

        [Test]
        public void ARefusedRequestIsAConsoleWarningAndAnErrorInTheLog()
        {
            Init();
            _sim.Shell.OnHttp(new EditorHttpRequest { Method = "GET", Endpoint = "/save/read?slot=a", Authorization = "Bearer " });
            LogAssert.Expect(UnityEngine.LogType.Warning, new System.Text.RegularExpressions.Regex("401 UNAUTHENTICATED"));

            _sim.Advance(EditorShell.HttpLatencySeconds);

            Assert.IsTrue(_sim.Log.Entries[_sim.Log.Entries.Count - 1].IsError);
        }

        // ── Purchases ───────────────────────────────────────────────────────────────────────

        [Test]
        public void APaidItemPurchaseLandsInTheInventory()
        {
            Init();
            _sim.Send("SDK_PURCHASE", "{\"requestId\":\"p1\",\"intent\":\"direct_item\",\"gameId\":\"game_1\",\"itemId\":\"sword\",\"sandbox\":false}");
            _sim.Advance();

            var response = SimulatorFixture.Payload(_sim.LastDelivered("SDK_PURCHASE_RESPONSE"));
            Assert.AreEqual(true, response["success"]);
            Assert.AreEqual("paid", response["status"]);
            Assert.IsNotNull(response["wallet"], "a direct purchase reports the game wallet");
            Assert.IsNull(response["platformWallet"]);
            Assert.AreEqual(1, _sim.State.FindInventory("game_1", false).Count(_sim.State.FindInventory("game_1", false).items, "sword"));
        }

        [Test]
        public void APaidTopupCreditsThePlatformWallet()
        {
            Init();
            _sim.Send("SDK_PURCHASE", "{\"requestId\":\"p1\",\"intent\":\"wallet_topup\",\"topupPackId\":\"coins_100\"}");
            _sim.Advance();

            var response = SimulatorFixture.Payload(_sim.LastDelivered("SDK_PURCHASE_RESPONSE"));
            var wallet = (Dictionary<string, object>)response["platformWallet"];
            Assert.AreEqual(100L, wallet["coins"]);
            Assert.IsNull(response["wallet"]);
        }

        [Test]
        public void AnUnknownItemFailsAsCheckoutDoes()
        {
            Init();
            _sim.Send("SDK_PURCHASE", "{\"requestId\":\"p1\",\"intent\":\"direct_item\",\"itemId\":\"swrod\"}");
            _sim.Advance();

            var response = SimulatorFixture.Payload(_sim.LastDelivered("SDK_PURCHASE_RESPONSE"));
            var error = (Dictionary<string, object>)response["error"];
            Assert.AreEqual("token-failed", response["status"]);
            Assert.AreEqual("UPSTREAM_ERROR", error["code"]);
            Assert.AreEqual("Item not found in economy config", error["message"]);
        }

        [Test]
        public void ACanceledPurchaseGrantsNothing()
        {
            _sim.Settings.purchaseOutcome = PurchaseOutcome.Canceled;
            Init();
            _sim.Send("SDK_PURCHASE", "{\"requestId\":\"p1\",\"intent\":\"direct_item\",\"itemId\":\"sword\"}");
            _sim.Advance();

            var response = SimulatorFixture.Payload(_sim.LastDelivered("SDK_PURCHASE_RESPONSE"));
            Assert.AreEqual("canceled", response["status"]);
            Assert.AreEqual("PURCHASE_CANCELED", ((Dictionary<string, object>)response["error"])["code"]);
            Assert.IsNull(_sim.State.FindInventory("game_1", false));
        }

        [Test]
        public void AskWaitsForTheDevelopersAnswer()
        {
            _sim.Settings.purchaseOutcome = PurchaseOutcome.Ask;
            Init();
            _sim.Send("SDK_PURCHASE", "{\"requestId\":\"p1\",\"intent\":\"direct_item\",\"itemId\":\"sword\"}");
            _sim.Advance();
            Assert.IsNull(_sim.LastDelivered("SDK_PURCHASE_RESPONSE"));

            _sim.Shell.AnswerPurchase(_sim.Shell.Pending[0], PurchaseOutcome.Closed);
            _sim.Advance();

            var response = SimulatorFixture.Payload(_sim.LastDelivered("SDK_PURCHASE_RESPONSE"));
            Assert.AreEqual("dismissed", response["status"]);
            Assert.AreEqual("PURCHASE_CLOSED", ((Dictionary<string, object>)response["error"])["code"]);
        }

        [Test]
        public void AGuestSignsInAndThePurchaseCarriesOn()
        {
            _sim.Settings.guest = true;
            Init();
            _sim.Send("SDK_PURCHASE", "{\"requestId\":\"p1\",\"intent\":\"direct_item\",\"itemId\":\"sword\"}");
            _sim.Advance();
            Assert.IsTrue(_sim.Shell.Pending[0].AwaitingSignIn);

            _sim.Shell.SignInAndContinue(_sim.Shell.Pending[0]);
            _sim.Advance();

            var auth = SimulatorFixture.Payload(_sim.LastDelivered("SDK_AUTH_COMPLETE"));
            Assert.AreEqual("authenticated", auth["mode"], "the SDK reads the payload as the new PlayerData");
            Assert.AreEqual("paid", SimulatorFixture.Payload(_sim.LastDelivered("SDK_PURCHASE_RESPONSE"))["status"]);
            Assert.IsFalse(_sim.Shell.IsGuest);
        }

        [Test]
        public void AGuestWhoDismissesSignInGetsAuthRequired()
        {
            _sim.Settings.guest = true;
            Init();
            _sim.Send("SDK_PURCHASE", "{\"requestId\":\"p1\",\"intent\":\"direct_item\",\"itemId\":\"sword\"}");
            _sim.Advance();

            _sim.Shell.DismissSignIn(_sim.Shell.Pending[0]);
            _sim.Advance();

            var response = SimulatorFixture.Payload(_sim.LastDelivered("SDK_PURCHASE_RESPONSE"));
            Assert.IsNotNull(_sim.LastDelivered("SDK_AUTH_DISMISSED"));
            Assert.AreEqual("auth-dismissed", response["status"]);
            Assert.AreEqual("AUTH_REQUIRED", ((Dictionary<string, object>)response["error"])["code"]);
        }

        [Test]
        public void PendingRequestsEndWithThePlaySession()
        {
            _sim.Settings.purchaseOutcome = PurchaseOutcome.Ask;
            Init();
            _sim.Send("SDK_PURCHASE", "{\"requestId\":\"p1\",\"intent\":\"direct_item\",\"itemId\":\"sword\"}");
            _sim.Advance();

            _sim.Shell.EndSession();

            Assert.AreEqual(0, _sim.Shell.Pending.Count);
        }

        // ── Ads ─────────────────────────────────────────────────────────────────────────────

        [Test]
        public void ACompletedRewardedAdCreditsTheReward()
        {
            Init();
            _sim.Send("SDK_AD_SHOW", "{\"requestId\":\"ad1\",\"adType\":\"rewarded\"}");
            _sim.Advance();

            var complete = SimulatorFixture.Payload(_sim.LastDelivered("SDK_AD_COMPLETE"));
            var data = (Dictionary<string, object>)complete["data"];
            Assert.AreEqual(true, complete["rewarded"]);
            Assert.AreEqual(25L, ((Dictionary<string, object>)data["awarded"])["amount"]);
        }

        [Test]
        public void ARewardOverTheDailyMaximumCompletesWithoutAReward()
        {
            _sim.Config = SimulatorConfig.FromJson(SimulatorFixture.ConfigJson.Replace("\"cooldownMinutes\": 30", "\"cooldownMinutes\": 1"));
            Init();
            for (var i = 0; i < 2; i++)
            {
                _sim.Send("SDK_AD_SHOW", "{\"requestId\":\"ad" + i + "\",\"adType\":\"rewarded\"}");
                _sim.Advance();
                _sim.Now = _sim.Now.AddMinutes(2);
            }
            LogAssert.Expect(UnityEngine.LogType.Warning, new System.Text.RegularExpressions.Regex("AD_REWARD_LIMIT_REACHED"));

            _sim.Send("SDK_AD_SHOW", "{\"requestId\":\"ad9\",\"adType\":\"rewarded\"}");
            _sim.Advance();

            var complete = SimulatorFixture.Payload(_sim.LastDelivered("SDK_AD_COMPLETE"));
            Assert.AreEqual("ad9", complete["requestId"]);
            Assert.AreEqual(false, complete["rewarded"]);
            Assert.IsNull(complete["data"]);
        }

        [Test]
        public void DisabledAdsRefuseEveryAdType()
        {
            _sim.Config = SimulatorConfig.FromJson(SimulatorFixture.ConfigJson.Replace("\"enabled\": true", "\"enabled\": false"));
            Init();
            _sim.Send("SDK_AD_SHOW", "{\"requestId\":\"ad1\",\"adType\":\"interstitial\"}");
            _sim.Advance();

            Assert.AreEqual("ADS_DISABLED", SimulatorFixture.Payload(_sim.LastDelivered("SDK_AD_FAILED"))["reason"]);
        }

        [Test]
        public void ACompletedInterstitialIsNeverRewarded()
        {
            Init();
            _sim.Send("SDK_AD_SHOW", "{\"requestId\":\"ad1\",\"adType\":\"interstitial\"}");
            _sim.Advance();

            Assert.AreEqual(false, SimulatorFixture.Payload(_sim.LastDelivered("SDK_AD_COMPLETE"))["rewarded"]);
        }

        [Test]
        public void ASkippedAdFails()
        {
            _sim.Settings.adOutcome = AdOutcome.Skip;
            Init();
            _sim.Send("SDK_AD_SHOW", "{\"requestId\":\"ad1\",\"adType\":\"rewarded\"}");
            _sim.Advance();

            Assert.AreEqual("dismissed", SimulatorFixture.Payload(_sim.LastDelivered("SDK_AD_FAILED"))["reason"]);
        }
    }
}
