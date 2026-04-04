using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace susaplay.SDK
{
    public class PurchasesModule
    {
        private readonly string _gameId;
        private readonly Dictionary<string, TaskCompletionSource<XsollaPurchaseResult>> _pendingRequests =
            new Dictionary<string, TaskCompletionSource<XsollaPurchaseResult>>();

        public PurchasesModule(string gameId)
        {
            _gameId = gameId;
        }

        public void Initialize()
        {
            WebGLBridge.OnMessageReceived += HandleMessage;
        }

        public async Task<XsollaPurchaseResult> StartXsollaPurchase(bool sandbox = false)
        {
            var requestId = Guid.NewGuid().ToString();
            var tcs = new TaskCompletionSource<XsollaPurchaseResult>();
            _pendingRequests[requestId] = tcs;

            WebGLBridge.SendMessage(new BridgeMessage
            {
                type = "SDK_XSOLLA_PURCHASE",
                payload =
                    "{\"requestId\":\"" + requestId + "\",\"gameId\":\"" + _gameId +
                    "\",\"sandbox\":" + (sandbox ? "true" : "false") + "}"
            });

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(180000));
            if (completed != tcs.Task)
            {
                _pendingRequests.Remove(requestId);
                return new XsollaPurchaseResult
                {
                    Success = false,
                    Status = "timeout",
                    ErrorCode = "TIMEOUT",
                    ErrorMessage = "Xsolla purchase request timed out."
                };
            }

            return await tcs.Task;
        }

        private void HandleMessage(string json)
        {
            var message = JsonUtility.FromJson<BridgeMessage>(json);
            if (message.type != "SDK_XSOLLA_PURCHASE_RESPONSE")
            {
                return;
            }

            XsollaPurchaseResponsePayload payload = null;
            if (!string.IsNullOrEmpty(message.payload))
            {
                payload = JsonUtility.FromJson<XsollaPurchaseResponsePayload>(message.payload);
            }

            if (payload == null || string.IsNullOrEmpty(payload.requestId))
            {
                Logger.Warn("SDK_XSOLLA_PURCHASE_RESPONSE missing requestId");
                return;
            }

            if (!_pendingRequests.TryGetValue(payload.requestId, out var tcs))
            {
                return;
            }

            _pendingRequests.Remove(payload.requestId);
            tcs.SetResult(new XsollaPurchaseResult
            {
                Success = payload.success,
                Status = payload.status,
                Wallet = payload.wallet,
                ErrorCode = payload.error != null ? payload.error.code : null,
                ErrorMessage = payload.error != null ? payload.error.message : null
            });
        }
    }

    [Serializable]
    public class XsollaPurchaseResult
    {
        public bool Success;
        public string Status;
        public XsollaWalletSnapshot Wallet;
        public string ErrorCode;
        public string ErrorMessage;
    }

    [Serializable]
    public class XsollaWalletSnapshot
    {
        public string gameId;
        public float coins;
        public float gems;
        public int version;
    }

    [Serializable]
    class XsollaPurchaseResponsePayload
    {
        public string requestId;
        public bool success;
        public string status;
        public XsollaWalletSnapshot wallet;
        public XsollaPurchaseError error;
    }

    [Serializable]
    class XsollaPurchaseError
    {
        public string code;
        public string message;
    }
}
