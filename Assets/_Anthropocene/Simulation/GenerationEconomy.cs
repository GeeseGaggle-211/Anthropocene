using System;

namespace Anthropocene.Simulation
{
    public static class PracticeEffects
    {
        public static double Progress(SimulationConfig config, Camp camp, Domain domain) => Numbers.Clamp(camp.Experience[(int)domain] / Math.Max(1e-9, config.ExperienceThreshold), 0, 1);
        public static double Ramp(SimulationConfig config, Camp camp, Domain domain)
        {
            double x = Numbers.Clamp((Progress(config, camp, domain) - config.PracticeBonusStart) / (config.PracticeBonusFull - config.PracticeBonusStart), 0, 1);
            return x * x * (3 - 2 * x);
        }
        public static double GatheringImpact(SimulationConfig config, Camp camp) => 1 - config.GatheringImpactReduction * Ramp(config, camp, Domain.Gathering);
        public static double FishingImpact(SimulationConfig config, Camp camp) => 1 - config.FishingImpactReduction * Ramp(config, camp, Domain.Fishing);
        public static double HuntingYield(SimulationConfig config, Camp camp) => config.HuntingFoodYieldScale * (1 + config.HuntingYieldBonus * Ramp(config, camp, Domain.Hunting));
    }

    [Serializable]
    public sealed class DemographicReport
    {
        public int Camp, StartingPopulation, Additions, Deaths, EndingPopulation, SurplusSteps, DeficitSteps, RecoverySteps;
        public double Years, FoodProduced, FoodReceived, TerrestrialFood, MarineFood, LakeFood, HuntFood, FishFood, ForageFood;
        public double FoodRequired, FoodConsumed, InitialReserves, FinalReserves, DiscardedInventory, FractionalProgress, NetPercent;
#pragma warning disable SYSLIB0050 // OptionalField permits older DataContractSerializer metadata.
        [System.Runtime.Serialization.OptionalField] public double ManufacturingFood;
#pragma warning restore SYSLIB0050
        public string LimitingFactor;
        public string Explanation => "Population " + StartingPopulation + " + " + Additions + " additions - " + Deaths + " deaths = " + EndingPopulation + " (" + NetPercent.ToString("+0.0;-0.0;0.0", System.Globalization.CultureInfo.InvariantCulture) + "%). " + LimitingFactor + ".";
    }

    public static class PassiveGathering
    {
        public static bool Eligible(WorldState w, Camp camp, int tile) => w.Map.Distance(camp.Tile, tile) <= w.Config.GatherRadius && w.Tiles[tile].Terrain != Terrain.Ocean && w.Tiles[tile].ElevationLevel < 10;
        public static string Kind(Tile tile) => tile.WaterBody == WaterKind.Saltwater ? "Marine gathering" : tile.Aquatic ? "Aquatic gathering" : "Gathering";
    }
}
