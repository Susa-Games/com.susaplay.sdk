using System;
using System.Collections;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using susaplay.SDK;

namespace susaplay.SDK.Samples
{
    /// <summary>
    /// Minimal end-to-end smoke tester for SusaPlay SDK.
    /// Attach to any GameObject and run in WebGL build on platform shell.
    /// </summary>
    public class SusaPlaySdkSmokeTester : MonoBehaviour
    {
        [Header("Execution")]
        [SerializeField] private bool runOnStart = true;
        [SerializeField] private bool repeatRun = false;
        [SerializeField] private float repeatEverySeconds = 30f;

        [Header("Cloud Save")]
        [SerializeField] private string slotPrefix = "smoke_slot";

        [Header("Analytics")]
        [SerializeField] private int analyticsEventCount = 3;

        [Header("Coverage")]
        [SerializeField] private bool runNegativeCases = true;
        [SerializeField] private bool runStressCases = true;
        [SerializeField] private int concurrentCloudSaveOps = 4;
        [SerializeField] private int stressIterations = 5;
        [SerializeField] private int defaultTimeoutMs = 15000;

        private bool _isRunning;
        private Coroutine _repeatCoroutine;
        private int _passCount;
        private int _failCount;
        private int _observeCount;

        private async void Start()
        {
            if (!runOnStart)
            {
                Log("runOnStart is disabled. Use context menu: Run Smoke Test Now");
                return;
            }

            await RunSmokeTestAsync();

            if (repeatRun)
            {
                _repeatCoroutine = StartCoroutine(RepeatLoop());
            }
        }

        [ContextMenu("Run Smoke Test Now")]
        public void RunSmokeTestNow()
        {
            _ = RunSmokeTestAsync();
        }

        [ContextMenu("Stop Repeat Loop")]
        public void StopRepeatLoop()
        {
            if (_repeatCoroutine != null)
            {
                StopCoroutine(_repeatCoroutine);
                _repeatCoroutine = null;
                Log("Repeat loop stopped.");
            }
        }

        private IEnumerator RepeatLoop()
        {
            while (true)
            {
                yield return new WaitForSeconds(repeatEverySeconds);
                _ = RunSmokeTestAsync();
            }
        }

        public async Task RunSmokeTestAsync()
        {
            if (_isRunning)
            {
                Warn("Smoke test is already running. Skipping this run.");
                return;
            }

            _isRunning = true;
            var runId = BuildRunId();
            var slot = slotPrefix + "_" + runId;
            var startedAt = DateTime.UtcNow;
            ResetStats();

            try
            {
                Log("====================================");
                Log("SMOKE TEST START runId=" + runId);
                Log("Unity version=" + Application.unityVersion + " platform=" + Application.platform);
                Log("slot=" + slot);

                await SusaPlaySDK.Initialize();
                Log("Initialize() completed.");
                LogAuthState();

                await RunPositiveCasesAsync(runId, slot);

                if (runNegativeCases)
                {
                    await RunNegativeCasesAsync(runId);
                }

                if (runStressCases)
                {
                    await RunStressCasesAsync(runId);
                }

                await RunFinalAnalyticsAsync(runId, startedAt);

                Log(
                    "SMOKE TEST SUMMARY runId=" + runId +
                    " pass=" + _passCount +
                    " fail=" + _failCount +
                    " observe=" + _observeCount
                );

                if (_failCount == 0)
                {
                    Log("SMOKE TEST PASSED runId=" + runId);
                }
                else
                {
                    Error("SMOKE TEST FAILED runId=" + runId + " failingCases=" + _failCount);
                }
                Log("====================================");
            }
            catch (Exception ex)
            {
                Error("SMOKE TEST FAILED runId=" + runId + " error=" + ex);
            }
            finally
            {
                _isRunning = false;
            }
        }

        private void LogAuthState()
        {
            if (SusaPlaySDK.Auth == null)
            {
                Error("Auth module is null after Initialize.");
                return;
            }

            Log("Auth.IsGuest=" + SusaPlaySDK.Auth.IsGuest);
            Log("Auth.IsAuthenticated=" + SusaPlaySDK.Auth.IsAuthenticated);
            Log("Auth.Uid=" + Safe(SusaPlaySDK.Auth.Uid));
            Log("Auth.DisplayName=" + Safe(SusaPlaySDK.Auth.DisplayName));
        }

        private async Task RunPositiveCasesAsync(string runId, string slot)
        {
            Log("Running positive cases...");
            await RunAnalyticsHappyPathAsync(runId);
            await RunCloudSaveHappyPathAsync(runId, slot);
            await RunStoreCatalogHappyPathAsync();
        }

        private async Task RunNegativeCasesAsync(string runId)
        {
            Log("Running negative / malformed input cases...");

            await ExecuteCase(
                "CloudSave.Save empty slot",
                ExpectedOutcome.Failure,
                async () =>
                {
                    var r = await WithTimeout(
                        SusaPlaySDK.CloudSave.Save("", "{\"runId\":\"" + runId + "\"}"),
                        defaultTimeoutMs
                    );
                    return r.Success;
                }
            );

            await ExecuteCase(
                "CloudSave.Load empty slot",
                ExpectedOutcome.Failure,
                async () =>
                {
                    var r = await WithTimeout(SusaPlaySDK.CloudSave.Load(""), defaultTimeoutMs);
                    return r.Success;
                }
            );

            await ExecuteCase(
                "CloudSave.Save null slot",
                ExpectedOutcome.Failure,
                async () =>
                {
                    var r = await WithTimeout(
                        SusaPlaySDK.CloudSave.Save(null, "{\"runId\":\"" + runId + "\"}"),
                        defaultTimeoutMs
                    );
                    return r.Success;
                }
            );

            await ExecuteCase(
                "CloudSave.Save malformed JSON body",
                ExpectedOutcome.Failure,
                async () =>
                {
                    var r = await WithTimeout(SusaPlaySDK.CloudSave.Save("bad_json_slot", "{"), defaultTimeoutMs);
                    return r.Success;
                }
            );

            await ExecuteCase(
                "CloudSave.Save empty JSON body",
                ExpectedOutcome.Failure,
                async () =>
                {
                    var r = await WithTimeout(SusaPlaySDK.CloudSave.Save("empty_json_slot", ""), defaultTimeoutMs);
                    return r.Success;
                }
            );

            await ExecuteCase(
                "CloudSave.Save wrong-type payload",
                ExpectedOutcome.Observe,
                async () =>
                {
                    var payload =
                        "{\"runId\":123,\"score\":\"not-number\",\"alive\":\"yes\",\"arr\":[1,2,3],\"obj\":{\"x\":true}}";
                    var r = await WithTimeout(SusaPlaySDK.CloudSave.Save("wrong_type_slot", payload), defaultTimeoutMs);
                    return r.Success;
                }
            );

            await ExecuteCase(
                "CloudSave.Save large payload (~100KB)",
                ExpectedOutcome.Observe,
                async () =>
                {
                    var payload = BuildLargePayload(runId, 100 * 1024);
                    var r = await WithTimeout(SusaPlaySDK.CloudSave.Save("large_payload_slot", payload), defaultTimeoutMs);
                    return r.Success;
                }
            );

            await ExecuteCase(
                "CloudSave.Load unicode slot",
                ExpectedOutcome.Observe,
                async () =>
                {
                    var r = await WithTimeout(SusaPlaySDK.CloudSave.Load("slot_تست_🚀"), defaultTimeoutMs);
                    return r.Success;
                }
            );

            await ExecuteCase(
                "Analytics empty event name",
                ExpectedOutcome.Observe,
                async () =>
                {
                    SusaPlaySDK.Analytics.LogEvent("", "{\"runId\":\"" + runId + "\",\"case\":\"empty_name\"}");
                    await WithTimeout(SusaPlaySDK.Analytics.Flush(), defaultTimeoutMs);
                    return true;
                }
            );

            await ExecuteCase(
                "Analytics malformed params",
                ExpectedOutcome.Observe,
                async () =>
                {
                    SusaPlaySDK.Analytics.LogEvent("malformed_params", "{");
                    await WithTimeout(SusaPlaySDK.Analytics.Flush(), defaultTimeoutMs);
                    return true;
                }
            );

            await ExecuteCase(
                "B2B analytics payload",
                ExpectedOutcome.Success,
                async () =>
                {
                    var payload = new B2BAnalyticsPayload
                    {
                        runId = runId,
                        webhookType = "smoke_test",
                        score = UnityEngine.Random.Range(1000, 9999),
                        timestampUtc = DateTime.UtcNow.ToString("o")
                    };

                    SusaPlaySDK.Analytics.LogB2BEvent(payload);
                    await WithTimeout(SusaPlaySDK.Analytics.Flush(), defaultTimeoutMs);
                    return true;
                }
            );
        }

        private async Task RunStressCasesAsync(string runId)
        {
            Log("Running stress / concurrency cases...");

            await ExecuteCase(
                "Concurrent CloudSave Save ops",
                ExpectedOutcome.Observe,
                async () =>
                {
                    var tasks = new Task<SaveResult>[Mathf.Max(1, concurrentCloudSaveOps)];
                    for (var i = 0; i < tasks.Length; i++)
                    {
                        var payload = "{\"runId\":\"" + runId + "\",\"i\":" + i + "}";
                        tasks[i] = SusaPlaySDK.CloudSave.Save("concurrent_slot_" + runId, payload);
                    }

                    var results = await WithTimeout(Task.WhenAll(tasks), defaultTimeoutMs);
                    var successCount = 0;
                    for (var i = 0; i < results.Length; i++)
                    {
                        if (results[i] != null && results[i].Success) successCount++;
                    }

                    Log("Concurrent Save successCount=" + successCount + "/" + results.Length);
                    return successCount > 0;
                }
            );

            await ExecuteCase(
                "Rapid analytics queue + flush",
                ExpectedOutcome.Observe,
                async () =>
                {
                    for (var i = 0; i < Mathf.Max(1, stressIterations); i++)
                    {
                        SusaPlaySDK.Analytics.LogEvent(
                            "stress_evt_" + i,
                            "{\"runId\":\"" + runId + "\",\"iteration\":" + i + "}"
                        );
                    }

                    await WithTimeout(SusaPlaySDK.Analytics.Flush(), defaultTimeoutMs);
                    return true;
                }
            );

            await ExecuteCase(
                "Initialize called again after ready",
                ExpectedOutcome.Observe,
                async () =>
                {
                    await WithTimeout(SusaPlaySDK.Initialize(), defaultTimeoutMs);
                    return true;
                }
            );
        }

        private async Task RunAnalyticsHappyPathAsync(string runId)
        {
            await ExecuteCase(
                "Analytics happy path",
                ExpectedOutcome.Success,
                async () =>
                {
                    if (SusaPlaySDK.Analytics == null)
                    {
                        throw new Exception("Analytics module is null.");
                    }

                    for (var i = 0; i < analyticsEventCount; i++)
                    {
                        var payload = new AnalyticsPayload
                        {
                            runId = runId,
                            sequence = i + 1,
                            randomValue = UnityEngine.Random.Range(1000, 9999),
                            timestampUtc = DateTime.UtcNow.ToString("o")
                        };
                        var payloadJson = JsonUtility.ToJson(payload);
                        SusaPlaySDK.Analytics.LogEvent("smoke_event_" + (i + 1), payloadJson);
                        Log("Queued analytics event " + (i + 1) + " payload=" + payloadJson);
                    }

                    await WithTimeout(SusaPlaySDK.Analytics.Flush(), defaultTimeoutMs);
                    return true;
                }
            );
        }

        private async Task RunCloudSaveHappyPathAsync(string runId, string slot)
        {
            await ExecuteCase(
                "CloudSave happy path",
                ExpectedOutcome.Success,
                async () =>
                {
                    if (SusaPlaySDK.CloudSave == null)
                    {
                        throw new Exception("CloudSave module is null.");
                    }

                    var preLoad = await WithTimeout(SusaPlaySDK.CloudSave.Load(slot), defaultTimeoutMs);
                    Log("Load(before save): success=" + preLoad.Success + " version=" + preLoad.Version + " error=" + Safe(preLoad.Error));

                    var saveData = new SmokeSavePayload
                    {
                        runId = runId,
                        score = UnityEngine.Random.Range(10, 5000),
                        level = UnityEngine.Random.Range(1, 30),
                        hp = (float)Math.Round(UnityEngine.Random.Range(1f, 100f), 2),
                        timestampUtc = DateTime.UtcNow.ToString("o")
                    };
                    var saveJson = JsonUtility.ToJson(saveData);
                    Log("Save payload=" + saveJson);

                    var saveResult = await WithTimeout(SusaPlaySDK.CloudSave.Save(slot, saveJson), defaultTimeoutMs);
                    Log("Save result: success=" + saveResult.Success + " version=" + saveResult.Version + " error=" + Safe(saveResult.Error));

                    var loadResult = await WithTimeout(SusaPlaySDK.CloudSave.Load(slot), defaultTimeoutMs);
                    Log("Load(after save): success=" + loadResult.Success + " version=" + loadResult.Version + " error=" + Safe(loadResult.Error));
                    Log("Load(after save) data=" + Safe(loadResult.Data));

                    return loadResult.Success && !string.IsNullOrEmpty(loadResult.Data) && loadResult.Data.Contains(runId);
                }
            );
        }

        private async Task RunStoreCatalogHappyPathAsync()
        {
            await ExecuteCase(
                "Purchases.GetStoreItems",
                ExpectedOutcome.Success,
                async () =>
                {
                    var result = await WithTimeout(
                        SusaPlaySDK.Purchases.GetStoreItems(),
                        defaultTimeoutMs
                    );
                    if (result.Success)
                    {
                        Log("Store items fetched=" + result.Items.Length);
                    }
                    return result.Success;
                }
            );
        }

        private async Task ExecuteCase(string name, ExpectedOutcome expected, Func<Task<bool>> action)
        {
            var success = false;
            var threw = false;
            var exception = "";

            try
            {
                success = await action();
            }
            catch (Exception ex)
            {
                threw = true;
                exception = ex.Message;
            }

            var passed = EvaluateOutcome(expected, success, threw);
            var expectedText = expected.ToString().ToUpperInvariant();
            var actual = threw ? "EXCEPTION" : (success ? "SUCCESS" : "FAILURE");

            if (expected == ExpectedOutcome.Observe)
            {
                _observeCount++;
                Log("CASE [" + name + "] expected=" + expectedText + " actual=" + actual + " (observation)");
                if (threw) Warn("CASE [" + name + "] exception=" + exception);
                return;
            }

            if (passed)
            {
                _passCount++;
                Log("CASE PASS [" + name + "] expected=" + expectedText + " actual=" + actual);
            }
            else
            {
                _failCount++;
                Error("CASE FAIL [" + name + "] expected=" + expectedText + " actual=" + actual);
                if (threw) Error("CASE [" + name + "] exception=" + exception);
            }
        }

        private static bool EvaluateOutcome(ExpectedOutcome expected, bool success, bool threw)
        {
            if (expected == ExpectedOutcome.Success)
            {
                return !threw && success;
            }

            if (expected == ExpectedOutcome.Failure)
            {
                return threw || !success;
            }

            return true;
        }

        private static async Task<T> WithTimeout<T>(Task<T> task, int timeoutMs)
        {
            var completed = await Task.WhenAny(task, Task.Delay(timeoutMs));
            if (completed != task)
            {
                throw new TimeoutException("Operation timed out after " + timeoutMs + "ms.");
            }
            return await task;
        }

        private static async Task WithTimeout(Task task, int timeoutMs)
        {
            var completed = await Task.WhenAny(task, Task.Delay(timeoutMs));
            if (completed != task)
            {
                throw new TimeoutException("Operation timed out after " + timeoutMs + "ms.");
            }
            await task;
        }

        private static string BuildLargePayload(string runId, int approxBytes)
        {
            var sb = new StringBuilder();
            sb.Append("{\"runId\":\"").Append(runId).Append("\",\"blob\":\"");
            var current = sb.Length;
            var target = Mathf.Max(current + 10, approxBytes);
            while (sb.Length < target)
            {
                sb.Append("x");
            }
            sb.Append("\"}");
            return sb.ToString();
        }

        private void ResetStats()
        {
            _passCount = 0;
            _failCount = 0;
            _observeCount = 0;
        }

        private enum ExpectedOutcome
        {
            Success,
            Failure,
            Observe
        }

        private async Task RunFinalAnalyticsAsync(string runId, DateTime startedAt)
        {
            if (SusaPlaySDK.Analytics == null)
            {
                Warn("Final analytics skipped because Analytics module is null.");
                return;
            }

            var durationMs = (int)(DateTime.UtcNow - startedAt).TotalMilliseconds;
            var payload = new FinalPayload
            {
                runId = runId,
                durationMs = durationMs,
                status = _failCount == 0 ? "completed" : "completed_with_failures"
            };
            SusaPlaySDK.Analytics.LogEvent("smoke_test_complete", JsonUtility.ToJson(payload));
            await WithTimeout(SusaPlaySDK.Analytics.Flush(), defaultTimeoutMs);
            Log("Final analytics sent.");
        }

        private static string BuildRunId()
        {
            var shortGuid = Guid.NewGuid().ToString("N").Substring(0, 8);
            return DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + shortGuid;
        }

        private static string Safe(string value)
        {
            return string.IsNullOrEmpty(value) ? "<null>" : value;
        }

        private static void Log(string msg)
        {
            Debug.Log("[SusaPlay SmokeTest] " + msg);
        }

        private static void Warn(string msg)
        {
            Debug.LogWarning("[SusaPlay SmokeTest] " + msg);
        }

        private static void Error(string msg)
        {
            Debug.LogError("[SusaPlay SmokeTest] " + msg);
        }

        [Serializable]
        private class SmokeSavePayload
        {
            public string runId;
            public int score;
            public int level;
            public float hp;
            public string timestampUtc;
        }

        [Serializable]
        private class AnalyticsPayload
        {
            public string runId;
            public int sequence;
            public int randomValue;
            public string timestampUtc;
        }

        [Serializable]
        private class B2BAnalyticsPayload
        {
            public string runId;
            public string webhookType;
            public int score;
            public string timestampUtc;
        }

        [Serializable]
        private class FinalPayload
        {
            public string runId;
            public int durationMs;
            public string status;
        }
    }
}
