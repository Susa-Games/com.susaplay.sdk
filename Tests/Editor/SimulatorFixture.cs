using System;
using System.Collections.Generic;
using susaplay.SDK.Editor.Simulator;

namespace susaplay.SDK.Editor.Tests
{
    /// <summary>A local backend and Editor Shell over in-memory state, with a clock the test moves.</summary>
    internal sealed class SimulatorFixture
    {
        public const string GameId = "game_1";
        public const string GameKey = "gk_test";

        public const string ConfigJson = @"{
          ""format"": 1,
          ""exportedAt"": ""2026-10-06T09:30:00Z"",
          ""game"": { ""gameId"": ""game_1"", ""gameKey"": ""gk_test"", ""name"": ""Test Game"" },
          ""achievements"": [
            { ""achievementId"": ""first_win"", ""name"": ""First win"", ""description"": ""Win once"", ""iconUrl"": null,
              ""type"": ""oneshot"", ""targetValue"": null, ""points"": 100 },
            { ""achievementId"": ""collector"", ""name"": ""Collector"", ""description"": ""Collect 3"", ""iconUrl"": null,
              ""type"": ""incremental"", ""targetValue"": 3, ""points"": 50 }
          ],
          ""items"": {
            ""sword"": { ""name"": ""Sword"", ""type"": ""durable"", ""price"": { ""currency"": ""coins"", ""amount"": 30 } },
            ""potion"": { ""name"": ""Potion"", ""type"": ""consumable"", ""price"": { ""currency"": ""coins"", ""amount"": 10 } },
            ""skin"": { ""name"": ""Skin"", ""type"": ""durable"", ""price"": { ""currency"": ""gold"", ""amount"": 5 } }
          },
          ""adSettings"": { ""rewarded"": { ""enabled"": true, ""rewardAmount"": 25, ""maxPerDay"": 2, ""cooldownMinutes"": 30 } },
          ""responses"": {
            ""storeItems"": { ""gameId"": ""game_1"", ""currencies"": {}, ""currencyList"": [], ""items"": [ { ""itemId"": ""sword"" } ] },
            ""topupPacks"": { ""topupPacks"": [ { ""topupPackId"": ""coins_100"", ""currency"": ""coins"", ""amount"": 100.0, ""active"": true } ],
                              ""paymentMode"": ""live"" }
          }
        }";

        public SimulatorState State = SimulatorState.CreateNew();
        public SimulatorConfig Config;
        public SimulatorSettings Settings = new SimulatorSettings();
        public DateTime Now = new DateTime(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);
        public readonly RequestLog Log = new RequestLog();
        public readonly LocalBackend Backend;
        public readonly EditorShell Shell;
        public readonly List<Dictionary<string, object>> Delivered = new List<Dictionary<string, object>>();
        public int Saves;
        public double Seconds;

        public SimulatorFixture(bool withConfig = true)
        {
            Config = withConfig ? SimulatorConfig.FromJson(ConfigJson) : SimulatorConfig.Empty();
            Backend = new LocalBackend(() => State, () => Config, () => Saves++, () => Now)
            {
                SessionGameId = withConfig ? GameId : GameKey,
            };
            Shell = new EditorShell(Backend, () => State, () => Config, () => Settings, Log)
            {
                Deliver = json => Delivered.Add((Dictionary<string, object>)SimJson.Parse(json)),
            };
        }

        public string Token => "Bearer " + LocalBackend.TokenFor(State.uid);

        public BackendResponse Get(string endpoint, string authorization = null)
        {
            return Backend.Handle("GET", endpoint, null, authorization ?? Token);
        }

        public BackendResponse Post(string endpoint, string body, string authorization = null)
        {
            return Backend.Handle("POST", endpoint, body, authorization ?? Token);
        }

        /// <summary>Advances the shell's clock and delivers whatever is due.</summary>
        public void Advance(double seconds = 0)
        {
            Seconds += seconds;
            Shell.Pump(Seconds);
        }

        /// <summary>The SDK sending a message, as WebGLBridge.SendMessage does.</summary>
        public void Send(string type, string payloadJson)
        {
            Shell.OnOutbound(SimJson.Serialize(SimJson.Obj("type", type, "payload", payloadJson)));
        }

        public Dictionary<string, object> LastDelivered(string type)
        {
            return Delivered.FindLast(m => (string)m["type"] == type);
        }

        public static Dictionary<string, object> Data(BackendResponse response)
        {
            return (Dictionary<string, object>)response.Body["data"];
        }

        public static Dictionary<string, object> Payload(Dictionary<string, object> message)
        {
            return (Dictionary<string, object>)SimJson.Parse((string)message["payload"]);
        }
    }
}
