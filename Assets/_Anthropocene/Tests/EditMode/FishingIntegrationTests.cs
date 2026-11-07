using Anthropocene.Simulation;
using NUnit.Framework;
using UnityEngine;

namespace Anthropocene.Tests.EditMode
{
    public sealed class FishingIntegrationTests
    {
        [Test]
        public void ValidationProfile_LoadsWithBothFishingGatesAndSailing_WithoutChangingDefault()
        {
            var opening = Resources.Load<TextAsset>("OpeningConfig");
            var profile = Resources.Load<TextAsset>("FishingValidationConfig");
            Assert.That(opening, Is.Not.Null);
            Assert.That(profile, Is.Not.Null);
            var config = new SimulationConfig();
            JsonUtility.FromJsonOverwrite(opening.text, config);
            Assert.That(config.NeolithicFishingEnabled, Is.False);
            Assert.That(config.NeolithicSailingEnabled, Is.False);
            JsonUtility.FromJsonOverwrite(profile.text, config);
            config.Validate();
            Assert.That(config.NeolithicFishingEnabled && config.CorrectedAquaticSeasonalPressureEnabled, Is.True);
            Assert.That(config.NeolithicSailingEnabled, Is.True);
            Assert.That(config.EconomicAllocation, Is.EqualTo(EconomicAllocationStrategy.DemandControl));
        }
    }
}
