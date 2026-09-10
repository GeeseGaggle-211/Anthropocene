using System;

namespace Anthropocene.Simulation
{
    [Serializable]
    public sealed class DemographicReport
    {
        public int Camp, StartingPopulation, Additions, Deaths, EndingPopulation, SurplusSteps, DeficitSteps, RecoverySteps;
        public double Years, FoodProduced, FoodReceived, TerrestrialFood, MarineFood, LakeFood, HuntFood, FishFood, ForageFood;
        public double FoodRequired, FoodConsumed, InitialReserves, FinalReserves, DiscardedInventory, FractionalProgress, NetPercent;
        public string LimitingFactor;
        public string Explanation => "Population " + StartingPopulation + " + " + Additions + " additions - " + Deaths + " deaths = " + EndingPopulation + " (" + NetPercent.ToString("+0.0;-0.0;0.0", System.Globalization.CultureInfo.InvariantCulture) + "%). " + LimitingFactor + ".";
    }

    public static class PassiveGathering
    {
        public static bool Eligible(WorldState w, Camp camp, int tile) => w.Map.Distance(camp.Tile, tile) <= w.Config.GatherRadius && w.Tiles[tile].Terrain != Terrain.Ocean && w.Tiles[tile].ElevationLevel < 10;
        public static string Kind(Tile tile) => tile.WaterBody == WaterKind.Saltwater ? "Marine gathering" : tile.Aquatic ? "Aquatic gathering" : "Gathering";
    }
}
