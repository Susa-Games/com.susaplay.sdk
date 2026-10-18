using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace susaplay.SDK
{
    /// <summary>
    /// Requests and caches Firebase App Check tokens via the shell bridge.
    /// The shell calls Firebase App Check on behalf of the SDK and returns
    /// the token via SDK_APP_CHECK_TOKEN_RESPONSE.
    /// </summary>
    public class AppCheckManager
    {
        // Must exceed the shell's own 5s timeout on SDK_GET_APP_CHECK_TOKEN, otherwise a slow
        // first load races: the shell replies null at ~5s while we've already given up and
        // written App Check off for the session.
        private const int RequestTimeoutMs = 8000;

        // One timeout can be transient (cold start, main-thread jank). Only stop asking after
        // the shell has stayed silent twice — that's an old shell with no handler at all.
        private const int MaxTimeoutsBeforeUnsupported = 2;

        private Dictionary<string, TaskCompletionSource<string>> _pendingRequests = new Dictionary<string, TaskCompletionSource<string>>();
        private string _cachedToken;
        private DateTime _tokenExpiry;
        private bool _supported = true;
        private int _timeoutCount;

        public void Initialize()
        {
            // Unsubscribe first so a retried Initialize() can't double-subscribe, which would
            // make HandleMessage call SetResult twice on the same TaskCompletionSource.
            WebGLBridge.OnMessageReceived -= HandleMessage;
            WebGLBridge.OnMessageReceived += HandleMessage;
        }

        /// <summary>
        /// Returns a cached App Check token or requests a new one from the shell.
        /// Returns null if the shell does not support App Check (older shells).
        /// </summary>
        public async Task<string> GetTokenAsync()
        {
            if (!_supported)
                return null;

            if (_cachedToken != null && DateTime.UtcNow < _tokenExpiry)
                return _cachedToken;

            string requestId = Guid.NewGuid().ToString();
            var tcs = new TaskCompletionSource<string>();
            _pendingRequests[requestId] = tcs;

            WebGLBridge.SendMessage(new BridgeMessage
            {
                type = "SDK_GET_APP_CHECK_TOKEN",
                payload = "{\"requestId\":\"" + requestId + "\"}"
            });

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(RequestTimeoutMs));
            _pendingRequests.Remove(requestId);

            if (completed != tcs.Task)
            {
                _timeoutCount++;
                if (_timeoutCount >= MaxTimeoutsBeforeUnsupported)
                {
                    Logger.Warn("App Check token request timed out again — shell does not support App Check. Proceeding without for this session.");
                    _supported = false;
                }
                else
                {
                    Logger.Warn("App Check token request timed out — will retry on the next request.");
                }
                return null;
            }

            // A reply (even a null token) proves the shell handles the message.
            _timeoutCount = 0;
            return await tcs.Task;
        }

        private void HandleMessage(string json)
        {
            var message = JsonUtility.FromJson<BridgeMessage>(json);
            if (message.type != "SDK_APP_CHECK_TOKEN_RESPONSE")
                return;

            string requestId = null;
            string token = null;

            if (!string.IsNullOrEmpty(message.payload))
            {
                var payload = JsonUtility.FromJson<AppCheckTokenPayload>(message.payload);
                if (payload != null)
                {
                    requestId = payload.requestId;
                    token = payload.token;
                }
            }

            if (string.IsNullOrEmpty(requestId))
            {
                Logger.Warn("SDK_APP_CHECK_TOKEN_RESPONSE missing requestId");
                return;
            }

            if (_pendingRequests.TryGetValue(requestId, out var tcs))
            {
                if (!string.IsNullOrEmpty(token))
                {
                    _cachedToken = token;
                    // App Check tokens expire in 1 hour — cache for 50 minutes
                    _tokenExpiry = DateTime.UtcNow.AddMinutes(50);
                }
                tcs.SetResult(token);
                _pendingRequests.Remove(requestId);
            }
        }
    }

    [System.Serializable]
    class AppCheckTokenPayload
    {
        public string requestId;
        public string token;
    }
}
