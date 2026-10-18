using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace susaplay.SDK
{
    public class TokenManager
    {
        // Without this the await below never returns if the shell drops or never sends
        // SDK_TOKEN_RESPONSE, and since every HTTP call goes through GetTokenAsync the whole
        // SDK hangs silently. Timing out returns null instead: the request then fails with a
        // 401 the game can actually observe and retry.
        private const int RequestTimeoutMs = 10000;

        private Dictionary<string, TaskCompletionSource<string>> _pendingRequests = new Dictionary<string, TaskCompletionSource<string>>();
        private string _cachedToken;
        private DateTime _tokenExpiry;

        public void Initialize()
        {
            // Unsubscribe first so a retried Initialize() can't double-subscribe, which would
            // make HandleMessage call SetResult twice on the same TaskCompletionSource.
            WebGLBridge.OnMessageReceived -= HandleMessage;
            WebGLBridge.OnMessageReceived += HandleMessage;
        }
        public async Task<string> GetTokenAsync()
        {
            if (_cachedToken != null && DateTime.UtcNow < _tokenExpiry)
            {
                return _cachedToken;
            }
            string requestId = Guid.NewGuid().ToString();
            TaskCompletionSource<string> tcs = new TaskCompletionSource<string>();
            _pendingRequests[requestId] = tcs;
            WebGLBridge.SendMessage(new BridgeMessage
            {
                type = "SDK_GET_TOKEN",
                payload = "{\"requestId\":\"" + requestId + "\"}"
            });

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(RequestTimeoutMs));
            if (completed != tcs.Task)
            {
                _pendingRequests.Remove(requestId);
                Logger.Warn("Auth token request timed out — no SDK_TOKEN_RESPONSE from shell. Request will be unauthenticated.");
                return null;
            }

            _cachedToken = await tcs.Task;
            return _cachedToken;
        }
        private void HandleMessage(string json)
        {
            var message = JsonUtility.FromJson<BridgeMessage>(json);
            if (message.type != "SDK_TOKEN_RESPONSE")
            {
                return;
            }

            string requestId = null;
            string token = null;

            if (!string.IsNullOrEmpty(message.payload))
            {
                var payload = JsonUtility.FromJson<TokenResponsePayload>(message.payload);
                if (payload != null)
                {
                    requestId = payload.requestId;
                    token = payload.token;
                }
            }

            // Backward compatibility: accept flat fields if payload is missing.
            if (string.IsNullOrEmpty(requestId))
            {
                var flat = JsonUtility.FromJson<TokenResponseEnvelope>(json);
                if (flat != null)
                {
                    requestId = flat.requestId;
                    token = flat.token;
                }
            }

            if (string.IsNullOrEmpty(requestId))
            {
                Logger.Warn("SDK_TOKEN_RESPONSE missing requestId");
                return;
            }

            if (_pendingRequests.TryGetValue(requestId, out var tcs))
            {
                _cachedToken = token;
                _tokenExpiry = DateTime.UtcNow.AddMinutes(55);
                tcs.SetResult(token);

                _pendingRequests.Remove(requestId);
            }
        }

    }
    [Serializable]
    class TokenResponsePayload
    {
        public string requestId;
        public string token;
    }

    [Serializable]
    class TokenResponseEnvelope
    {
        public string requestId;
        public string token;
    }
}
