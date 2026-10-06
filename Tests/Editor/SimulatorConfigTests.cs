using System;
using NUnit.Framework;
using susaplay.SDK.Editor.Simulator;

namespace susaplay.SDK.Editor.Tests
{
    public sealed class SimulatorConfigTests
    {
        [Test]
        public void TheExportedFileIsRead()
        {
            var config = SimulatorConfig.FromJson(SimulatorFixture.ConfigJson);

            Assert.IsTrue(config.Loaded);
            Assert.AreEqual("game_1", config.GameId);
            Assert.AreEqual("gk_test", config.GameKey);
            Assert.AreEqual(2, config.Achievements.Count);
            Assert.AreEqual(3, config.Items.Count);
            Assert.AreEqual(1, config.TopupPacks().Count);
            Assert.AreEqual(25, config.RewardAmount);
        }

        [Test]
        public void AnotherFormatIsRefused()
        {
            var exception = Assert.Throws<FormatException>(() => SimulatorConfig.FromJson("{\"format\":2,\"game\":{\"gameId\":\"g\"}}"));
            StringAssert.Contains("format 2", exception.Message);
        }

        [Test]
        public void AFileWithoutAGameIsRefused()
        {
            Assert.Throws<FormatException>(() => SimulatorConfig.FromJson("{\"format\":1}"));
        }

        [Test]
        public void AdSettingsAreNormalizedAsThePlatformDoes()
        {
            // normalize_game_ad_settings: a non-bool enabled means enabled, a non-positive limit
            // means the default.
            var config = SimulatorConfig.FromJson(
                "{\"format\":1,\"game\":{\"gameId\":\"g\"},\"adSettings\":{\"rewarded\":{\"enabled\":\"no\",\"rewardAmount\":0,\"maxPerDay\":\"4\"}}}");

            Assert.IsTrue(config.RewardedEnabled);
            Assert.AreEqual(SimulatorConfig.DefaultRewardAmount, config.RewardAmount);
            Assert.AreEqual(4, config.MaxPerDay);
            Assert.AreEqual(SimulatorConfig.DefaultCooldownMinutes, config.CooldownMinutes);
        }

        [Test]
        public void AMissingFileIsNoConfigurationAndNoError()
        {
            var config = SimulatorConfig.LoadFile("/nonexistent/SimulatorConfig.json");

            Assert.IsFalse(config.Loaded);
            Assert.IsNull(config.LoadError);
        }
    }
}
