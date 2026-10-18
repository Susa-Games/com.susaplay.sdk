using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace susaplay.SDK
{
    public class CloudSaveModule
    {
        private ICloudSaveTransport _transport;
        private Dictionary<string, int> _slotVersions = new Dictionary<string, int>();
        private Dictionary<string, float> _lastSaveTime = new Dictionary<string, float>();
        private Dictionary<string, SlotWriteQueue> _slotQueues = new Dictionary<string, SlotWriteQueue>();
        private string _gameId;
        private float _minSaveIntervalSeconds;

        private const int MaxRetries = 3;
        private const float MinSaveIntervalSeconds = 2f;

        /// <summary>Pending write state for one slot. Only one request per slot is ever in
        /// flight; callers that arrive while it runs share the next one.</summary>
        private class SlotWriteQueue
        {
            public string PendingData;
            public TaskCompletionSource<SaveResult> PendingCompletion;
            public bool IsDraining;
        }

        public CloudSaveModule(HttpClient httpClient, string gameId)
            : this(new HttpClientCloudSaveTransport(httpClient), gameId, MinSaveIntervalSeconds)
        {
        }

        internal CloudSaveModule(ICloudSaveTransport transport, string gameId, float minSaveIntervalSeconds)
        {
            _transport = transport;
            _gameId = gameId;
            _minSaveIntervalSeconds = minSaveIntervalSeconds;
        }

        /// <summary>
        /// Queues a write for <paramref name="slot"/>. Writes to the same slot are serialised,
        /// so the cached version is always the one the server last acknowledged — overlapping
        /// callers no longer race each other into 409 VERSION_CONFLICT storms.
        ///
        /// Calls that pile up while a request is in flight are coalesced: they all await the
        /// next write, which carries the newest payload. That is a deliberate last-write-wins —
        /// superseded snapshots of the same slot have no value, and sending each one in turn is
        /// what produced the request bursts in the first place.
        /// </summary>
        public Task<SaveResult> Save(string slot, string data)
        {
            if (string.IsNullOrEmpty(slot))
            {
                return Task.FromResult(SaveResult.Fail("Slot must not be empty"));
            }

            if (!_slotQueues.TryGetValue(slot, out var queue))
            {
                queue = new SlotWriteQueue();
                _slotQueues[slot] = queue;
            }

            queue.PendingData = data;
            if (queue.PendingCompletion == null)
            {
                queue.PendingCompletion = new TaskCompletionSource<SaveResult>();
            }
            var completion = queue.PendingCompletion;

            if (!queue.IsDraining)
            {
                queue.IsDraining = true;
                // Fire-and-forget on purpose: every caller's result arrives through its own
                // completion source, so nothing is dropped by not awaiting the drain loop.
                _ = DrainSlotQueue(slot, queue);
            }

            return completion.Task;
        }

        private async Task DrainSlotQueue(string slot, SlotWriteQueue queue)
        {
            try
            {
                while (queue.PendingCompletion != null)
                {
                    var data = queue.PendingData;
                    var completion = queue.PendingCompletion;
                    // Detach before awaiting so callers arriving mid-request get a fresh
                    // completion source and are guaranteed a write that includes their payload.
                    queue.PendingCompletion = null;
                    queue.PendingData = null;

                    SaveResult result;
                    try
                    {
                        result = await WriteSlot(slot, data);
                    }
                    catch (Exception e)
                    {
                        result = SaveResult.Fail(e.Message);
                    }
                    completion.TrySetResult(result);
                }
            }
            finally
            {
                queue.IsDraining = false;
                // A caller can only have enqueued during an await above, which would have kept
                // the loop going; re-check anyway so a queued payload is never left stranded
                // (for example if WriteSlot threw before the loop re-tested the condition).
                if (queue.PendingCompletion != null)
                {
                    queue.IsDraining = true;
                    _ = DrainSlotQueue(slot, queue);
                }
            }
        }

        private async Task<SaveResult> WriteSlot(string slot, string data)
        {
            if (string.IsNullOrEmpty(data))
            {
                return SaveResult.Fail("Save data must not be empty");
            }

            // Only reached from the drain loop, so this throttle now sees a true picture of the
            // last request for this slot instead of being read concurrently by racing callers.
            float now = Time.realtimeSinceStartup;
            if (_lastSaveTime.TryGetValue(slot, out float lastTime))
            {
                float elapsed = now - lastTime;
                if (elapsed < _minSaveIntervalSeconds)
                {
                    int delayMs = (int)((_minSaveIntervalSeconds - elapsed) * 1000);
                    await Task.Delay(delayMs);
                }
            }

            for (int attempt = 0; attempt < MaxRetries; attempt++)
            {
                var version = _slotVersions.ContainsKey(slot) ? _slotVersions[slot] : 0;
                var body = "{\"gameId\":\"" + _gameId + "\",\"slot\":\"" + slot + "\",\"data\":" + data + ",\"version\":" + version + "}";

                var response = await _transport.Post("/save/write", body);
                // Stamp every attempt, not just successes: a failing slot used to bypass the
                // throttle entirely and hammer the endpoint.
                _lastSaveTime[slot] = Time.realtimeSinceStartup;

                if (response.Success)
                {
                    var envelope = JsonUtility.FromJson<SaveWriteEnvelope>(response.Data);
                    if (envelope == null || !envelope.success || envelope.data == null)
                    {
                        return SaveResult.Fail("Malformed save response");
                    }

                    var saveResult = new SaveResult
                    {
                        Success = true,
                        Version = envelope.data.version,
                        Error = null
                    };
                    _slotVersions[slot] = saveResult.Version;
                    return saveResult;
                }

                if (response.StatusCode == 409 && attempt < MaxRetries - 1)
                {
                    // Local writes are serialised now, so a conflict means another client (other
                    // device, other tab) moved the slot forward. Re-read to adopt its version.
                    Logger.Warn("Save conflict on slot " + slot + " — re-fetching and retrying (attempt " + (attempt + 1) + ")");
                    var refreshed = await Load(slot);
                    if (!refreshed.Success)
                    {
                        return SaveResult.Fail("Failed to refresh after conflict: " + refreshed.Error);
                    }
                    continue;
                }

                LogFailure("Save", slot, response);
                return SaveResult.Fail(response.Error);
            }

            Logger.Warn("Cloud save failed on slot " + slot + " after " + MaxRetries + " conflict retries");
            return SaveResult.Fail("Save failed after " + MaxRetries + " retries");
        }

        public async Task<LoadResult> Load(string slot)
        {
            var response = await _transport.Get("/save/read?gameId=" + _gameId + "&slot=" + slot);
            if (!response.Success)
            {
                LogFailure("Load", slot, response);
                return LoadResult.Fail(response.Error);
            }
            var envelope = JsonUtility.FromJson<SaveReadEnvelope>(response.Data);
            if (envelope == null || !envelope.success || envelope.data == null)
            {
                return LoadResult.Fail("Malformed load response");
            }

            var loadResult = new LoadResult
            {
                Success = true,
                Data = envelope.data.data,
                Version = envelope.data.version,
                Error = null
            };
            _slotVersions[slot] = loadResult.Version;
            return loadResult;
        }

        /// <summary>
        /// Status 0 means the request never got an HTTP answer: offline, or the browser blocked
        /// it (CORS). The platform has no record of such a request at all, so this console line
        /// is the only place the failure shows up.
        /// </summary>
        private static void LogFailure(string operation, string slot, HttpResponse response)
        {
            var cause = response.StatusCode == 0
                ? " (no HTTP response — offline, or blocked by the browser)"
                : "";
            Logger.Warn("Cloud " + operation.ToLowerInvariant() + " failed on slot " + slot +
                        ": status " + response.StatusCode + cause + " — " + response.Error);
        }
    }

    [System.Serializable]
    class SaveWriteEnvelope
    {
        public bool success;
        public SaveWriteData data;
    }

    [System.Serializable]
    class SaveWriteData
    {
        public string slot;
        public int version;
    }

    [System.Serializable]
    class SaveReadEnvelope
    {
        public bool success;
        public SaveReadData data;
    }

    [System.Serializable]
    class SaveReadData
    {
        public string slot;
        public string data;
        public int version;
        public string savedAt;
    }
}
