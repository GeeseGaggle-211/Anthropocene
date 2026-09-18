using System;
using System.Globalization;

namespace Anthropocene.Simulation
{
    // These settings belong to one generation request. They are never read from a saved world:
    // the resulting resource state and its configuration identity are persisted instead.
    [Serializable]
    public sealed class ProvinceTuning
    {
        public int Count = 144;
        public double BoundaryWarp = .12, RadiusXMinimum = .045, RadiusXRange = .055, RadiusYMinimum = .06, RadiusYRange = .10;
        public double SourceChance = .7, MatureChance = .7, ReservoirChance = .7, SealChance = .6, EvaporiteChance = .35, PeatChance = .45, SortedChance = .7;
        public double AncientChance = .7, MineralizedChance = .6, ReducedChance = .5, WeatheredChance = .5, HydrothermalChance = .4;
        internal void Validate()
        {
            if (Count < 1 || Count > 4096 || !GeologicalTuning.Valid(BoundaryWarp, 0, 1) || !GeologicalTuning.Valid(RadiusXMinimum, .001, .5) || !GeologicalTuning.Valid(RadiusXRange, 0, .5) || !GeologicalTuning.Valid(RadiusYMinimum, .001, .5) || !GeologicalTuning.Valid(RadiusYRange, 0, .5) || RadiusXMinimum + RadiusXRange > .5 || RadiusYMinimum + RadiusYRange > .5 ||
                !GeologicalTuning.Probability(SourceChance, MatureChance, ReservoirChance, SealChance, EvaporiteChance, PeatChance, SortedChance, AncientChance, MineralizedChance, ReducedChance, WeatheredChance, HydrothermalChance))
                throw new ArgumentException("Invalid geological province tuning.");
        }
        internal string Stamp => GeologicalTuning.Join(Count, BoundaryWarp, RadiusXMinimum, RadiusXRange, RadiusYMinimum, RadiusYRange, SourceChance, MatureChance, ReservoirChance, SealChance, EvaporiteChance, PeatChance, SortedChance, AncientChance, MineralizedChance, ReducedChance, WeatheredChance, HydrothermalChance);
    }

    [Serializable]
    public sealed class DepositTuning
    {
        public double OrdinaryChance = .085, BedChance = .13, RareBasinChance = .025, CopperChanceMultiplier = 1.6, TinChanceMultiplier = 2.5;
        public double ScatteredChance = .18, ScatteredStockMinimum = .01, ScatteredStockRange = .07, ScatteredSiteStockDivisor = 1000;
        public double BroadRadius = .012, NarrowRadius = .006, ScatteredRadius = .035, VeinWidth = .25, OtherWidth = .8, ScatteredWidth = .65;
        public double ScatteredSiteBaseChance = .04, ScatteredSiteCenterBonus = .18, CoreFloor = .025, CoreDecay = 9;
        public double RichStockMinimum = .55, RichStockRange = .45;
        internal void Validate()
        {
            if (!GeologicalTuning.Valid(OrdinaryChance, 0, 1) || !GeologicalTuning.Valid(BedChance, 0, 1) || !GeologicalTuning.Valid(RareBasinChance, 0, 1) || !GeologicalTuning.Valid(CopperChanceMultiplier, 0, 10) || !GeologicalTuning.Valid(TinChanceMultiplier, 0, 10) ||
                !GeologicalTuning.Valid(ScatteredChance, 0, 1) || !GeologicalTuning.Valid(ScatteredStockMinimum, 0, .08) || !GeologicalTuning.Valid(ScatteredStockRange, 0, .08) || ScatteredStockMinimum + ScatteredStockRange > .08 || !GeologicalTuning.Valid(ScatteredSiteStockDivisor, 100, 100000) ||
                !GeologicalTuning.Valid(BroadRadius, .00001, .2) || !GeologicalTuning.Valid(NarrowRadius, .00001, .2) || !GeologicalTuning.Valid(ScatteredRadius, .00001, .2) || !GeologicalTuning.Valid(VeinWidth, .01, 2) || !GeologicalTuning.Valid(OtherWidth, .01, 2) || !GeologicalTuning.Valid(ScatteredWidth, .01, 2) ||
                !GeologicalTuning.Valid(ScatteredSiteBaseChance, 0, 1) || !GeologicalTuning.Valid(ScatteredSiteCenterBonus, 0, 1) || !GeologicalTuning.Valid(CoreFloor, .00001, 10) || !GeologicalTuning.Valid(CoreDecay, 0, 100) ||
                !GeologicalTuning.Valid(RichStockMinimum, .01, 1) || !GeologicalTuning.Valid(RichStockRange, 0, 1) || RichStockMinimum + RichStockRange > 1)
                throw new ArgumentException("Invalid mineral deposit tuning.");
        }
        internal string Stamp => GeologicalTuning.Join(OrdinaryChance, BedChance, RareBasinChance, CopperChanceMultiplier, TinChanceMultiplier, ScatteredChance, ScatteredStockMinimum, ScatteredStockRange, ScatteredSiteStockDivisor, BroadRadius, NarrowRadius, ScatteredRadius, VeinWidth, OtherWidth, ScatteredWidth, ScatteredSiteBaseChance, ScatteredSiteCenterBonus, CoreFloor, CoreDecay, RichStockMinimum, RichStockRange);
    }

    [Serializable]
    public sealed class SurfaceTuning
    {
        public double StoneMinimum = .55, StoneRange = .45, ClasticStoneChance = .45, OtherStoneChance = .88;
        public double WeatheredClayChance = .28, BackgroundClayChance = .02, FineSedimentClayBonus = .27, RiverClayBonus = .18, ClayMinimum = .45, ClayRange = .55;
        public double HighWood = 1, MediumWood = .55, LowWood = .05, WoodMinimum = .8, WoodRange = .2;
        internal void Validate()
        {
            if (!GeologicalTuning.UnitSum(StoneMinimum, StoneRange) || !GeologicalTuning.UnitSum(ClayMinimum, ClayRange) || !GeologicalTuning.UnitSum(WoodMinimum, WoodRange) ||
                !GeologicalTuning.Probability(ClasticStoneChance, OtherStoneChance, WeatheredClayChance, BackgroundClayChance, FineSedimentClayBonus, RiverClayBonus, HighWood, MediumWood, LowWood))
                throw new ArgumentException("Invalid surface material tuning.");
        }
        internal string Stamp => GeologicalTuning.Join(StoneMinimum, StoneRange, ClasticStoneChance, OtherStoneChance, WeatheredClayChance, BackgroundClayChance, FineSedimentClayBonus, RiverClayBonus, ClayMinimum, ClayRange, HighWood, MediumWood, LowWood, WoodMinimum, WoodRange);
    }

    [Serializable]
    public sealed class PlacerTuning
    {
        public double TransferMinimum = .05, TransferRange = .10;
        public int MaximumSites = 6, ShelfSteps = 3;
        internal void Validate()
        {
            if (!GeologicalTuning.UnitSum(TransferMinimum, TransferRange) || MaximumSites < 1 || MaximumSites > 64 || ShelfSteps < 0 || ShelfSteps > 64)
                throw new ArgumentException("Invalid placer tuning.");
        }
        internal string Stamp => GeologicalTuning.Join(TransferMinimum, TransferRange, MaximumSites, ShelfSteps);
    }

    internal static class GeologicalTuning
    {
        internal static bool Valid(double value, double min, double max) => ResourceGenerationOptions.Finite(value) && value >= min && value <= max;
        internal static bool Probability(params double[] values)
        {
            foreach (double value in values) if (!Valid(value, 0, 1)) return false;
            return true;
        }
        internal static bool UnitRange(double min, double max) => Valid(min, 0, 1) && Valid(max, 0, 1) && min <= max;
        internal static bool UnitSum(double min, double range) => Valid(min, 0, 1) && Valid(range, 0, 1) && min + range <= 1;
        internal static string Join(params object[] values)
        {
            var parts = new string[values.Length];
            for (int i = 0; i < values.Length; i++) parts[i] = values[i] is double value ? value.ToString("R", CultureInfo.InvariantCulture) : Convert.ToString(values[i], CultureInfo.InvariantCulture);
            return string.Join(":", parts);
        }
    }
}
