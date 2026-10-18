using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NUnit.Framework;
using susaplay.SDK.Editor.Simulator;

namespace susaplay.SDK.Editor.Tests
{
    // The platform's contract for the 13 routes, run against the local backend. The platform
    // runs the same file against its real handlers, so a case that passes there and fails here
    // is a place the simulator would lie to a game. See README.md beside cases.json.
    public sealed class SdkContractTests
    {
        private static Dictionary<string, object> _contract;

        private static Dictionary<string, object> Contract
        {
            get
            {
                if (_contract == null)
                {
                    var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(LocalBackend).Assembly);
                    var path = Path.Combine(package.resolvedPath, "Tests", "Editor", "Contract", "cases.json");
                    _contract = (Dictionary<string, object>)SimJson.Parse(File.ReadAllText(path));
                }
                return _contract;
            }
        }

        private static IEnumerable<TestCaseData> Cases()
        {
            foreach (Dictionary<string, object> entry in (List<object>)Contract["cases"])
            {
                yield return new TestCaseData(entry).SetName("Contract: " + (string)entry["name"]);
            }
        }

        [Test]
        public void TheContractFileIsAFormatThisRunnerReads()
        {
            Assert.AreEqual(1L, Contract["format"]);
            Assert.IsNotEmpty((List<object>)Contract["cases"]);
        }

        [Test]
        public void EveryRouteTheBackendAnswersHasACase()
        {
            var covered = new HashSet<string>(
                ((List<object>)Contract["cases"]).Cast<Dictionary<string, object>>()
                .SelectMany(c => ((List<object>)c["steps"]).Cast<Dictionary<string, object>>())
                .Select(s => (string)s["method"] + " " + ((string)s["path"]).Split('?')[0]));

            CollectionAssert.AreEquivalent(LocalBackend.Routes, covered);
        }

        [TestCaseSource(nameof(Cases))]
        public void Case(Dictionary<string, object> contractCase)
        {
            var uid = (string)((Dictionary<string, object>)Contract["player"])["uid"];
            var config = SimulatorConfig.FromJson(SimJson.Serialize(Contract["simulatorConfig"]));
            var state = SimulatorState.CreateNew();
            state.uid = uid;
            Seed(state, config.GameId, contractCase.TryGetValue("given", out var given) ? (Dictionary<string, object>)given : null);
            var backend = new LocalBackend(() => state, () => config, () => { }) { SessionGameId = config.GameId };

            var problems = new List<string>();
            var steps = (List<object>)contractCase["steps"];
            for (var index = 0; index < steps.Count; index++)
            {
                var step = (Dictionary<string, object>)steps[index];
                var guest = step.TryGetValue("as", out var role) && (string)role == "guest";
                // Python-style, so a 1.0 in the case stays a float on the way in.
                var body = step.TryGetValue("body", out var rawBody) ? SimJson.SerializePython(Expand(rawBody)) : null;
                var response = backend.Handle(
                    (string)step["method"],
                    (string)step["path"],
                    body,
                    guest ? "Bearer " : "Bearer " + LocalBackend.TokenFor(uid));

                var expect = (Dictionary<string, object>)step["expect"];
                var label = "step " + (index + 1) + " (" + step["method"] + " " + step["path"] + "): ";
                if (response.Status != (long)expect["status"])
                {
                    problems.Add(label + "status: expected " + expect["status"] + ", got " + response.Status + " " + response.BodyText);
                }
                if (expect.TryGetValue("error", out var error))
                {
                    response.Body.TryGetValue("error", out var actualError);
                    problems.AddRange(Mismatches(error, actualError, "error").Select(p => label + p));
                }
                if (expect.TryGetValue("data", out var data))
                {
                    response.Body.TryGetValue("data", out var actualData);
                    problems.AddRange(Mismatches(data, actualData, "data").Select(p => label + p));
                }
            }

            if (problems.Count > 0)
            {
                Assert.Fail(string.Join("\n", problems));
            }
        }

        /// <summary>"given": what no SDK route can create, seeded as the platform test seeds Firestore.</summary>
        private static void Seed(SimulatorState state, string gameId, Dictionary<string, object> given)
        {
            if (given == null)
            {
                return;
            }
            if (given.TryGetValue("platformWallet", out var walletValue))
            {
                var wallet = (Dictionary<string, object>)walletValue;
                state.platformWallet.coins = Number(wallet, "coins");
                state.platformWallet.gems = Number(wallet, "gems");
            }
            if (given.TryGetValue("inventory", out var inventoryValue))
            {
                var source = (Dictionary<string, object>)inventoryValue;
                var inventory = state.FindInventory(gameId, true);
                foreach (var list in new[] { "items", "consumables" })
                {
                    if (!source.TryGetValue(list, out var entries)) continue;
                    foreach (var pair in (Dictionary<string, object>)entries)
                    {
                        inventory.Set(list == "items" ? inventory.items : inventory.consumables, pair.Key, Convert.ToInt64(pair.Value));
                    }
                }
            }
        }

        private static double Number(Dictionary<string, object> map, string key)
        {
            return map.TryGetValue(key, out var value) && value != null ? Convert.ToDouble(value, CultureInfo.InvariantCulture) : 0;
        }

        /// <summary>The file's shorthands for bodies too large to write out.</summary>
        private static object Expand(object value)
        {
            switch (value)
            {
                case Dictionary<string, object> map when map.ContainsKey("$repeat"):
                    return string.Concat(Enumerable.Repeat((string)map["$repeat"], (int)(long)map["count"]));
                case Dictionary<string, object> map when map.ContainsKey("$repeatItem"):
                    return Enumerable.Range(0, (int)(long)map["count"])
                        .Select(_ => SimJson.Parse(SimJson.SerializePython(map["$repeatItem"])))
                        .ToList();
                case Dictionary<string, object> map:
                    return map.ToDictionary(pair => pair.Key, pair => Expand(pair.Value));
                case List<object> list:
                    return list.Select(Expand).ToList();
                default:
                    return value;
            }
        }

        /// <summary>The platform test's subset match: listed keys must match, lists in full, numbers by value.</summary>
        private static IEnumerable<string> Mismatches(object expected, object actual, string where)
        {
            switch (expected)
            {
                case Dictionary<string, object> expectedMap:
                    if (!(actual is Dictionary<string, object> actualMap))
                    {
                        yield return where + ": expected an object, got " + SimJson.Serialize(actual);
                        yield break;
                    }
                    foreach (var pair in expectedMap)
                    {
                        if (!actualMap.TryGetValue(pair.Key, out var value))
                        {
                            yield return where + "." + pair.Key + ": missing";
                            continue;
                        }
                        foreach (var problem in Mismatches(pair.Value, value, where + "." + pair.Key))
                        {
                            yield return problem;
                        }
                    }
                    yield break;
                case List<object> expectedList:
                    if (!(actual is List<object> actualList) || actualList.Count != expectedList.Count)
                    {
                        yield return where + ": expected " + expectedList.Count + " entries, got " + SimJson.Serialize(actual);
                        yield break;
                    }
                    for (var i = 0; i < expectedList.Count; i++)
                    {
                        foreach (var problem in Mismatches(expectedList[i], actualList[i], where + "[" + i + "]"))
                        {
                            yield return problem;
                        }
                    }
                    yield break;
                case long or double when actual is long or double:
                    if (Convert.ToDouble(expected) != Convert.ToDouble(actual))
                    {
                        yield return where + ": expected " + expected + ", got " + actual;
                    }
                    yield break;
                default:
                    if (!Equals(expected, actual))
                    {
                        yield return where + ": expected " + SimJson.Serialize(expected) + ", got " + SimJson.Serialize(actual);
                    }
                    yield break;
            }
        }
    }
}
