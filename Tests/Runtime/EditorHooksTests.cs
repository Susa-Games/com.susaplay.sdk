using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace susaplay.SDK.Tests
{
    // The two seams the Editor Simulator plugs into. What the simulator answers is its own
    // concern; these tests pin down that the SDK hands it everything a page or the platform
    // would receive, and reads its answers exactly as it reads theirs.
    public sealed class EditorHooksTests
    {
        private readonly List<EditorHttpRequest> _requests = new List<EditorHttpRequest>();

        [TearDown]
        public void TearDown()
        {
            WebGLBridge.EditorOutbound = null;
            HttpClient.EditorTransport = null;
            SusaPlaySDK.ResetStaticState();
            _requests.Clear();
        }

        [Test]
        public void SentMessagesReachTheEditorOutbound()
        {
            var sent = new List<string>();
            WebGLBridge.EditorOutbound = sent.Add;

            WebGLBridge.SendMessage(new BridgeMessage { type = "SDK_GAME_LOADED", payload = "{}" });

            Assert.AreEqual(1, sent.Count);
            Assert.AreEqual("SDK_GAME_LOADED", JsonUtility.FromJson<BridgeMessage>(sent[0]).type);
        }

        [Test]
        public void EditorAnswersReachSubscribersAsPageMessagesDo()
        {
            string received = null;
            WebGLBridge.OnMessageReceived += json => received = json;

            WebGLBridge.DeliverFromEditor("{\"type\":\"SDK_READY\",\"payload\":\"{}\"}");

            Assert.AreEqual("{\"type\":\"SDK_READY\",\"payload\":\"{}\"}", received);
        }

        [Test]
        public void ANewSessionKeepsTheEditorHooks()
        {
            WebGLBridge.EditorOutbound = _ => { };
            HttpClient.EditorTransport = _ => Task.FromResult(new EditorHttpAnswer { StatusCode = 200 });

            SusaPlaySDK.ResetStaticState();

            // The hooks are set once per domain reload; clearing them at Play would leave every
            // Play after the first talking to nobody.
            Assert.IsNotNull(WebGLBridge.EditorOutbound);
            Assert.IsNotNull(HttpClient.EditorTransport);
        }

        [Test]
        public void IsSimulatedNeedsBothHooks()
        {
            Assert.IsFalse(SusaPlaySDK.IsSimulated);

            WebGLBridge.EditorOutbound = _ => { };
            Assert.IsFalse(SusaPlaySDK.IsSimulated);

            HttpClient.EditorTransport = _ => Task.FromResult(new EditorHttpAnswer { StatusCode = 200 });
            Assert.IsTrue(SusaPlaySDK.IsSimulated);
        }

        [Test]
        public async Task PostHandsTheTransportWhatThePlatformWouldReceive()
        {
            var client = CreateClient(_ => new EditorHttpAnswer { StatusCode = 200, Body = "{}" });

            await client.Post("/save/write", "{\"slot\":\"a\"}");

            Assert.AreEqual(1, _requests.Count);
            Assert.AreEqual("POST", _requests[0].Method);
            Assert.AreEqual("/save/write", _requests[0].Endpoint);
            Assert.AreEqual("{\"slot\":\"a\"}", _requests[0].Body);
            Assert.AreEqual("Bearer local-token", _requests[0].Authorization);
        }

        [Test]
        public async Task GetSendsNoBody()
        {
            var client = CreateClient(_ => new EditorHttpAnswer { StatusCode = 200, Body = "{}" });

            await client.Get("/economy/inventory?gameId=g");

            Assert.AreEqual("GET", _requests[0].Method);
            Assert.AreEqual("/economy/inventory?gameId=g", _requests[0].Endpoint);
            Assert.IsNull(_requests[0].Body);
        }

        [Test]
        public async Task A2xxAnswerSucceedsWithItsBody()
        {
            var client = CreateClient(_ => new EditorHttpAnswer { StatusCode = 201, Body = "{\"success\":true}" });

            var response = await client.Post("/analytics/event", "{}");

            Assert.IsTrue(response.Success);
            Assert.AreEqual(201, response.StatusCode);
            Assert.AreEqual("{\"success\":true}", response.Data);
        }

        [Test]
        public async Task AnHttpErrorReadsAsUnityWebRequestReportsIt()
        {
            var client = CreateClient(_ => new EditorHttpAnswer
            {
                StatusCode = 409,
                Body = "{\"success\":false,\"error\":\"VERSION_CONFLICT\"}",
            });

            var response = await client.Post("/save/write", "{}");

            // CloudSaveModule and the rest match on this text and code, so it has to be the
            // same text a real 409 produces.
            Assert.IsFalse(response.Success);
            Assert.AreEqual(409, response.StatusCode);
            Assert.AreEqual("HTTP/1.1 409 Conflict — {\"success\":false,\"error\":\"VERSION_CONFLICT\"}", response.Error);
            Assert.IsNull(response.Data);
        }

        [Test]
        public async Task ANetworkFailureHasNoStatus()
        {
            var client = CreateClient(_ => new EditorHttpAnswer { NetworkError = "Cannot connect to destination host" });

            var response = await client.Get("/save/read?gameId=g&slot=a");

            Assert.IsFalse(response.Success);
            Assert.AreEqual(0, response.StatusCode);
            Assert.AreEqual("Cannot connect to destination host", response.Error);
        }

        [Test]
        public async Task ASimulatorBugFailsTheRequestInsteadOfThrowing()
        {
            var client = CreateClient(_ => throw new System.InvalidOperationException("boom"));
            LogAssert.Expect(LogType.Error, new Regex("Simulator failed to answer POST /save/write"));

            var response = await client.Post("/save/write", "{}");

            Assert.IsFalse(response.Success);
            Assert.AreEqual(0, response.StatusCode);
        }

        // An HttpClient whose token comes from the Editor outbound and whose requests go to
        // the given answer, the way it is wired when the Editor Simulator is running.
        private HttpClient CreateClient(System.Func<EditorHttpRequest, EditorHttpAnswer> answer)
        {
            WebGLBridge.EditorOutbound = json =>
            {
                var message = JsonUtility.FromJson<BridgeMessage>(json);
                if (message.type != "SDK_GET_TOKEN")
                {
                    return;
                }
                var requestId = JsonUtility.FromJson<TokenResponsePayload>(message.payload).requestId;
                WebGLBridge.DeliverFromEditor(JsonUtility.ToJson(new BridgeMessage
                {
                    type = "SDK_TOKEN_RESPONSE",
                    payload = JsonUtility.ToJson(new TokenResponsePayload { requestId = requestId, token = "local-token" }),
                }));
            };
            HttpClient.EditorTransport = request =>
            {
                _requests.Add(request);
                return Task.FromResult(answer(request));
            };

            var tokenManager = new TokenManager();
            tokenManager.Initialize();
            return new HttpClient(ScriptableObject.CreateInstance<SDKConfig>(), tokenManager);
        }
    }
}
