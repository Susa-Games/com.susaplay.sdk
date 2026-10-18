using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace susaplay.SDK
{
    internal static class LiveOpsManifestParser
    {
        internal const int SchemaVersion = 1;

        internal static bool TryParse(
            string json,
            DateTimeOffset now,
            out LiveOpsSnapshot snapshot,
            out string error)
        {
            snapshot = null;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(json) || !json.TrimStart().StartsWith("{"))
            {
                error = "LiveOps manifest is not a JSON object.";
                return false;
            }

            LiveOpsManifestWire manifest;
            try
            {
                manifest = JsonUtility.FromJson<LiveOpsManifestWire>(json);
            }
            catch (Exception exception)
            {
                error = "LiveOps manifest JSON is invalid: " + exception.Message;
                return false;
            }

            if (manifest == null || manifest.schemaVersion != SchemaVersion)
            {
                error = "Unsupported or missing LiveOps manifest schemaVersion.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(manifest.version))
            {
                error = "LiveOps manifest version is missing.";
                return false;
            }
            if (!TryParseDate(manifest.generatedAt, false, out var generatedAt))
            {
                error = "LiveOps manifest generatedAt is invalid.";
                return false;
            }
            if (manifest.missions == null || manifest.discountOffers == null)
            {
                error = "LiveOps manifest campaign arrays are missing.";
                return false;
            }
            if (!manifest.enabled && (manifest.missions.Length != 0 || manifest.discountOffers.Length != 0))
            {
                error = "A disabled LiveOps manifest must not contain campaigns.";
                return false;
            }

            var missions = new List<LiveOpsMission>(manifest.missions.Length);
            for (var index = 0; index < manifest.missions.Length; index++)
            {
                if (!TryMapMission(manifest.missions[index], now, out var mission, out error))
                {
                    error = "Invalid mission at index " + index + ": " + error;
                    return false;
                }
                if (mission != null)
                {
                    missions.Add(mission);
                }
            }

            var offers = new List<DiscountOffer>(manifest.discountOffers.Length);
            for (var index = 0; index < manifest.discountOffers.Length; index++)
            {
                if (!TryMapOffer(manifest.discountOffers[index], now, out var offer, out error))
                {
                    error = "Invalid discount offer at index " + index + ": " + error;
                    return false;
                }
                if (offer != null)
                {
                    offers.Add(offer);
                }
            }

            snapshot = new LiveOpsSnapshot(manifest.enabled, manifest.version, generatedAt,
                missions, offers);
            return true;
        }

        private static bool TryMapMission(
            LiveOpsCampaignWire wire,
            DateTimeOffset now,
            out LiveOpsMission mission,
            out string error)
        {
            mission = null;
            if (!TryMapCommon(wire, now, out var startDate, out var endDate, out var expired, out error))
            {
                return false;
            }
            if (wire.type != "liveop")
            {
                error = "mission type must be 'liveop'.";
                return false;
            }
            if (wire.payload == null || string.IsNullOrWhiteSpace(wire.payload.missionID))
            {
                error = "liveop payload.missionID is required.";
                return false;
            }
            if (expired)
            {
                return true;
            }

            mission = new LiveOpsMission(
                wire.campaignId, wire.regionTags, startDate, endDate, wire.payload.missionID,
                wire.payload.headerText, wire.payload.messageText, wire.payload.spriteNames);
            return true;
        }

        private static bool TryMapOffer(
            LiveOpsCampaignWire wire,
            DateTimeOffset now,
            out DiscountOffer offer,
            out string error)
        {
            offer = null;
            if (!TryMapCommon(wire, now, out var startDate, out var endDate, out var expired, out error))
            {
                return false;
            }
            if (wire.payload == null)
            {
                error = "offer payload is required.";
                return false;
            }

            switch (wire.type)
            {
                case "SingleOffer":
                    if (string.IsNullOrWhiteSpace(wire.payload.productID))
                    {
                        error = "SingleOffer payload.productID is required.";
                        return false;
                    }
                    if (!expired)
                    {
                        offer = new SingleOffer(
                            wire.campaignId, wire.regionTags, startDate, endDate,
                            wire.payload.headerText, wire.payload.messageText, wire.payload.spriteNames,
                            wire.payload.productID, wire.payload.discountRatio,
                            MapMoneyValue(wire.payload.moneyValue));
                    }
                    return true;

                case "ChainOfOffers":
                    if (wire.payload.steps == null || wire.payload.steps.Length == 0)
                    {
                        error = "ChainOfOffers payload.steps must be a non-empty array.";
                        return false;
                    }
                    if (!expired)
                    {
                        var steps = new List<ChainOfferStep>(wire.payload.steps.Length);
                        foreach (var step in wire.payload.steps)
                        {
                            if (step == null)
                            {
                                error = "ChainOfOffers payload.steps contains a null step.";
                                return false;
                            }
                            steps.Add(new ChainOfferStep(step.productID, step.discountRatio,
                                MapMoneyValue(step.moneyValue)));
                        }
                        offer = new ChainOfOffers(
                            wire.campaignId, wire.regionTags, startDate, endDate,
                            wire.payload.headerText, wire.payload.messageText, wire.payload.spriteNames,
                            steps);
                    }
                    return true;

                case "InAppBoosted":
                    if (string.IsNullOrWhiteSpace(wire.payload.productID))
                    {
                        error = "InAppBoosted payload.productID is required.";
                        return false;
                    }
                    if (!expired)
                    {
                        offer = new InAppBoosted(
                            wire.campaignId, wire.regionTags, startDate, endDate,
                            wire.payload.headerText, wire.payload.messageText, wire.payload.spriteNames,
                            wire.payload.productID, wire.payload.amountIncreaseRatio);
                    }
                    return true;

                default:
                    error = "unknown campaign type '" + (wire.type ?? string.Empty) + "'.";
                    return false;
            }
        }

        private static bool TryMapCommon(
            LiveOpsCampaignWire wire,
            DateTimeOffset now,
            out DateTimeOffset? startDate,
            out DateTimeOffset? endDate,
            out bool expired,
            out string error)
        {
            startDate = null;
            endDate = null;
            expired = false;
            error = string.Empty;
            if (wire == null || string.IsNullOrWhiteSpace(wire.campaignId))
            {
                error = "campaignId is required.";
                return false;
            }
            if (wire.regionTags == null)
            {
                error = "regionTags is required.";
                return false;
            }
            if (!TryParseNullableDate(wire.startDate, out startDate) ||
                !TryParseNullableDate(wire.endDate, out endDate))
            {
                error = "startDate or endDate is invalid.";
                return false;
            }
            if (startDate.HasValue && endDate.HasValue && endDate.Value <= startDate.Value)
            {
                error = "endDate must be after startDate.";
                return false;
            }
            expired = endDate.HasValue && endDate.Value <= now;
            return true;
        }

        private static bool TryParseNullableDate(string value, out DateTimeOffset? parsed)
        {
            parsed = null;
            if (string.IsNullOrEmpty(value))
            {
                return true;
            }
            if (!TryParseDate(value, true, out var date))
            {
                return false;
            }
            parsed = date;
            return true;
        }

        private static bool TryParseDate(string value, bool allowEmpty, out DateTimeOffset parsed)
        {
            parsed = default(DateTimeOffset);
            if (string.IsNullOrWhiteSpace(value))
            {
                return allowEmpty;
            }
            return DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out parsed);
        }

        private static LiveOpsMoneyValue MapMoneyValue(LiveOpsMoneyValueWire wire)
        {
            if (wire == null)
            {
                return null;
            }
            var rewards = new List<LiveOpsReward>();
            if (wire.rewards != null)
            {
                foreach (var reward in wire.rewards)
                {
                    if (reward != null)
                    {
                        rewards.Add(new LiveOpsReward(
                            reward.type, reward.amount, reward.isUnlimited, reward.isGiveAway));
                    }
                }
            }
            return new LiveOpsMoneyValue(rewards);
        }
    }

    [Serializable]
    internal sealed class LiveOpsManifestWire
    {
        public int schemaVersion;
        public string version;
        public string generatedAt;
        public bool enabled;
        public LiveOpsCampaignWire[] missions;
        public LiveOpsCampaignWire[] discountOffers;
    }

    [Serializable]
    internal sealed class LiveOpsCampaignWire
    {
        public string campaignId;
        public string type;
        public string[] regionTags;
        public string startDate;
        public string endDate;
        public LiveOpsPayloadWire payload;
    }

    [Serializable]
    internal sealed class LiveOpsPayloadWire
    {
        public string missionID;
        public string productID;
        public float discountRatio;
        public float amountIncreaseRatio;
        public string headerText;
        public string messageText;
        public string[] spriteNames;
        public LiveOpsMoneyValueWire moneyValue;
        public ChainOfferStepWire[] steps;
    }

    [Serializable]
    internal sealed class ChainOfferStepWire
    {
        public string productID;
        public float discountRatio;
        public LiveOpsMoneyValueWire moneyValue;
    }

    [Serializable]
    internal sealed class LiveOpsMoneyValueWire
    {
        public LiveOpsRewardWire[] rewards;
    }

    [Serializable]
    internal sealed class LiveOpsRewardWire
    {
        public string type;
        public float amount;
        public bool isUnlimited;
        public bool isGiveAway;
    }
}
