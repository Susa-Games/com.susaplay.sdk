using System;
using System.Collections.Generic;

namespace susaplay.SDK
{
    public enum LiveOpsCampaignType
    {
        LiveOp,
        SingleOffer,
        ChainOfOffers,
        InAppBoosted
    }

    public sealed class LiveOpsSnapshot
    {
        public static LiveOpsSnapshot Empty(bool enabled = true)
        {
            return new LiveOpsSnapshot(enabled, string.Empty, default(DateTimeOffset),
                Array.Empty<LiveOpsMission>(), Array.Empty<DiscountOffer>());
        }

        internal LiveOpsSnapshot(
            bool enabled,
            string version,
            DateTimeOffset generatedAt,
            IReadOnlyList<LiveOpsMission> missions,
            IReadOnlyList<DiscountOffer> discountOffers)
        {
            Enabled = enabled;
            Version = version;
            GeneratedAt = generatedAt;
            Missions = missions;
            DiscountOffers = discountOffers;
        }

        public bool Enabled { get; }
        public string Version { get; }
        public DateTimeOffset GeneratedAt { get; }
        public IReadOnlyList<LiveOpsMission> Missions { get; }
        public IReadOnlyList<DiscountOffer> DiscountOffers { get; }
    }

    public abstract class LiveOpsCampaign
    {
        protected LiveOpsCampaign(
            string campaignId,
            LiveOpsCampaignType type,
            string[] regionTags,
            DateTimeOffset? startDate,
            DateTimeOffset? endDate)
        {
            CampaignId = campaignId;
            Type = type;
            RegionTags = regionTags ?? Array.Empty<string>();
            StartDate = startDate;
            EndDate = endDate;
        }

        public string CampaignId { get; }
        public LiveOpsCampaignType Type { get; }
        public IReadOnlyList<string> RegionTags { get; }
        public DateTimeOffset? StartDate { get; }
        public DateTimeOffset? EndDate { get; }
    }

    public sealed class LiveOpsMission : LiveOpsCampaign
    {
        internal LiveOpsMission(
            string campaignId,
            string[] regionTags,
            DateTimeOffset? startDate,
            DateTimeOffset? endDate,
            string missionId,
            string headerText,
            string messageText,
            string[] spriteNames)
            : base(campaignId, LiveOpsCampaignType.LiveOp, regionTags, startDate, endDate)
        {
            MissionId = missionId;
            HeaderText = headerText ?? string.Empty;
            MessageText = messageText ?? string.Empty;
            SpriteNames = spriteNames ?? Array.Empty<string>();
        }

        public string MissionId { get; }
        public string HeaderText { get; }
        public string MessageText { get; }
        public IReadOnlyList<string> SpriteNames { get; }
    }

    public abstract class DiscountOffer : LiveOpsCampaign
    {
        protected DiscountOffer(
            string campaignId,
            LiveOpsCampaignType type,
            string[] regionTags,
            DateTimeOffset? startDate,
            DateTimeOffset? endDate,
            string headerText,
            string messageText,
            string[] spriteNames)
            : base(campaignId, type, regionTags, startDate, endDate)
        {
            HeaderText = headerText ?? string.Empty;
            MessageText = messageText ?? string.Empty;
            SpriteNames = spriteNames ?? Array.Empty<string>();
        }

        public string HeaderText { get; }
        public string MessageText { get; }
        public IReadOnlyList<string> SpriteNames { get; }
    }

    public sealed class SingleOffer : DiscountOffer
    {
        internal SingleOffer(
            string campaignId, string[] regionTags, DateTimeOffset? startDate, DateTimeOffset? endDate,
            string headerText, string messageText, string[] spriteNames, string productId,
            float discountRatio, LiveOpsMoneyValue moneyValue)
            : base(campaignId, LiveOpsCampaignType.SingleOffer, regionTags, startDate, endDate,
                headerText, messageText, spriteNames)
        {
            ProductId = productId;
            DiscountRatio = discountRatio;
            MoneyValue = moneyValue;
        }

        public string ProductId { get; }
        public float DiscountRatio { get; }
        public LiveOpsMoneyValue MoneyValue { get; }
    }

    public sealed class ChainOfOffers : DiscountOffer
    {
        internal ChainOfOffers(
            string campaignId, string[] regionTags, DateTimeOffset? startDate, DateTimeOffset? endDate,
            string headerText, string messageText, string[] spriteNames, IReadOnlyList<ChainOfferStep> steps)
            : base(campaignId, LiveOpsCampaignType.ChainOfOffers, regionTags, startDate, endDate,
                headerText, messageText, spriteNames)
        {
            Steps = steps;
        }

        public IReadOnlyList<ChainOfferStep> Steps { get; }
    }

    public sealed class InAppBoosted : DiscountOffer
    {
        internal InAppBoosted(
            string campaignId, string[] regionTags, DateTimeOffset? startDate, DateTimeOffset? endDate,
            string headerText, string messageText, string[] spriteNames, string productId,
            float amountIncreaseRatio)
            : base(campaignId, LiveOpsCampaignType.InAppBoosted, regionTags, startDate, endDate,
                headerText, messageText, spriteNames)
        {
            ProductId = productId;
            AmountIncreaseRatio = amountIncreaseRatio;
        }

        public string ProductId { get; }
        public float AmountIncreaseRatio { get; }
    }

    public sealed class ChainOfferStep
    {
        internal ChainOfferStep(string productId, float discountRatio, LiveOpsMoneyValue moneyValue)
        {
            ProductId = productId ?? string.Empty;
            DiscountRatio = discountRatio;
            MoneyValue = moneyValue;
        }

        public string ProductId { get; }
        public float DiscountRatio { get; }
        public LiveOpsMoneyValue MoneyValue { get; }
    }

    public sealed class LiveOpsMoneyValue
    {
        internal LiveOpsMoneyValue(IReadOnlyList<LiveOpsReward> rewards)
        {
            Rewards = rewards;
        }

        public IReadOnlyList<LiveOpsReward> Rewards { get; }
    }

    public sealed class LiveOpsReward
    {
        internal LiveOpsReward(string type, float amount, bool isUnlimited, bool isGiveAway)
        {
            Type = type ?? string.Empty;
            Amount = amount;
            IsUnlimited = isUnlimited;
            IsGiveAway = isGiveAway;
        }

        public string Type { get; }
        public float Amount { get; }
        public bool IsUnlimited { get; }
        public bool IsGiveAway { get; }
    }
}
