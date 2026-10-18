using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace susaplay.SDK
{
    /// <summary>
    /// Asks the page hosting the game to show an ad.
    ///
    /// The SDK never loads an ad itself. Which network runs, whether a house ad
    /// or a placeholder stands in for it, and whether a reward is credited are
    /// all decided by the platform from the game's own ad settings — so this
    /// module is a request and an answer, nothing more.
    /// </summary>
    public class AdsModule
    {
        private readonly Dictionary<string, TaskCompletionSource<AdResult>> _pendingRequests =
            new Dictionary<string, TaskCompletionSource<AdResult>>();

        /// <summary>
        /// A safety net, not the ad's own limit. The page times a network ad out
        /// on its own and answers; a placeholder ad waits for the player, who may
        /// leave it open. Without this a dropped message would leave the caller
        /// awaiting forever.
        /// </summary>
        private const int AdTimeoutMs = 180000;

        public void Initialize()
        {
            // Unsubscribe first — a retried SDK init constructs a new module, and the old one
            // would otherwise stay subscribed to the bridge for the life of the page.
            WebGLBridge.OnMessageReceived -= HandleMessage;
            WebGLBridge.OnMessageReceived += HandleMessage;
        }

        /// <summary>
        /// Shows a rewarded ad. When <see cref="AdResult.Rewarded"/> is true the
        /// player watched it to the end: grant your game's reward (an extra life, a
        /// doubled score).
        ///
        /// Separately, the platform may credit a signed-in player some platform
        /// coins for the ad, reported in <see cref="AdResult.CoinsAwarded"/>. That is
        /// the platform's reward, not your game's: it is limited per day and by a
        /// cooldown, and a guest gets none, but none of that changes
        /// <see cref="AdResult.Rewarded"/>.
        /// </summary>
        public Task<AdResult> ShowRewarded()
        {
            return ShowInternal("rewarded");
        }

        /// <summary>
        /// Shows an interstitial. Nothing is credited, so <see cref="AdResult.Rewarded"/>
        /// is always false and <see cref="AdResult.Success"/> is the only answer
        /// that matters.
        ///
        /// Refused with <c>ADS_DISABLED</c> while interstitials are turned off in the
        /// game's ad settings. Rewarded ads have their own switch.
        /// </summary>
        public Task<AdResult> ShowInterstitial()
        {
            return ShowInternal("interstitial");
        }

        private async Task<AdResult> ShowInternal(string adType)
        {
            var requestId = Guid.NewGuid().ToString();
            var tcs = new TaskCompletionSource<AdResult>();
            _pendingRequests[requestId] = tcs;

            WebGLBridge.SendMessage(new BridgeMessage
            {
                type = "SDK_AD_SHOW",
                payload = JsonUtility.ToJson(
                    new AdRequestPayload
                    {
                        requestId = requestId,
                        adType = adType,
                    }
                )
            });

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(AdTimeoutMs));
            if (completed != tcs.Task)
            {
                _pendingRequests.Remove(requestId);
                return new AdResult
                {
                    Success = false,
                    RequestId = requestId,
                    AdType = adType,
                    Reason = "TIMEOUT",
                };
            }

            return await tcs.Task;
        }

        private void HandleMessage(string json)
        {
            var message = JsonUtility.FromJson<BridgeMessage>(json);
            if (message == null)
            {
                return;
            }

            var completed = message.type == "SDK_AD_COMPLETE";
            if (!completed && message.type != "SDK_AD_FAILED")
            {
                return;
            }

            AdResponsePayload payload = null;
            if (!string.IsNullOrEmpty(message.payload))
            {
                payload = JsonUtility.FromJson<AdResponsePayload>(message.payload);
            }

            if (payload == null || string.IsNullOrEmpty(payload.requestId))
            {
                Logger.Warn(message.type + " missing requestId");
                return;
            }

            if (!_pendingRequests.TryGetValue(payload.requestId, out var tcs))
            {
                return;
            }

            _pendingRequests.Remove(payload.requestId);

            // TrySetResult, not SetResult: this runs inside a callback from the
            // browser, and throwing here takes down the whole Unity instance
            // rather than failing one call.
            tcs.TrySetResult(new AdResult
            {
                Success = completed,
                Rewarded = completed && payload.rewarded,
                CoinsAwarded = completed && payload.data != null && payload.data.awarded != null
                    ? payload.data.awarded.amount
                    : 0,
                RequestId = payload.requestId,
                AdType = payload.adType,
                Reason = completed ? null : payload.reason,
            });
        }
    }

    [Serializable]
    public class AdResult
    {
        /// <summary>The ad played to the end. Says nothing about a reward.</summary>
        public bool Success;

        /// <summary>
        /// A rewarded ad was watched to the end: grant your game's reward. Always
        /// false for an interstitial, and for an ad that was skipped or failed.
        ///
        /// Until SDK 1.10.0 this meant the platform had credited coins, and was
        /// false at the platform's daily limit or in its cooldown. The platform now
        /// reports the watched ad either way; its coins are in
        /// <see cref="CoinsAwarded"/>.
        /// </summary>
        public bool Rewarded;

        /// <summary>
        /// Platform coins credited to the player for this ad, or 0 — a guest, the
        /// platform's daily limit or cooldown, or an interstitial. Informational:
        /// the platform has already put them in the wallet; never add them yourself.
        /// </summary>
        public int CoinsAwarded;

        /// <summary>Correlation id the page echoes back. Useful for analytics.</summary>
        public string RequestId;

        public string AdType;

        /// <summary>
        /// Why it failed, or null on success. <c>ADS_DISABLED</c> means the game's
        /// own settings turned ads off; <c>TIMEOUT</c> is this SDK giving up on an
        /// answer. Everything else comes from the network the platform chose.
        /// </summary>
        public string Reason;
    }

    [Serializable]
    class AdRequestPayload
    {
        public string requestId;
        public string adType;
    }

    [Serializable]
    class AdResponsePayload
    {
        public string requestId;
        public string adType;
        public bool rewarded;
        public string reason;
        public AdRewardData data;
    }

    [Serializable]
    class AdRewardData
    {
        public AdAwarded awarded;
    }

    [Serializable]
    class AdAwarded
    {
        public string currency;
        public int amount;
    }
}
