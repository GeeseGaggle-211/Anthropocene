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
        [System.Runtime.Serialization.OptionalField] public double AnimalFood;
        // Controlled herd slaughter (Docs/HerdLabour.md): animals slaughtered this turn and their carcass food (included in FoodProduced).
        [System.Runtime.Serialization.OptionalField] public int Slaughtered;
        [System.Runtime.Serialization.OptionalField] public double SlaughterFood;
        public double FoodRequired, FoodConsumed, InitialReserves, FinalReserves, DiscardedInventory, FractionalProgress, NetPercent;
#pragma warning disable SYSLIB0050 // OptionalField permits older DataContractSerializer metadata.
        [System.Runtime.Serialization.OptionalField] public double ManufacturingFood;
        // Growth-bucket food actually debited after consumption/reserve priority;
        // distinct from maintenance consumption and manufacturing feedstock.
        [System.Runtime.Serialization.OptionalField] public double GrowthFoodAccepted;
        // People who left this camp at the end of the turn to found a fission settlement (WP10a). EndingPopulation is before that split.
        [System.Runtime.Serialization.OptionalField] public int SplitOff;
        // People who left for, and arrived from, other known settlements at the end of the turn (WP10b). EndingPopulation is before both.
        [System.Runtime.Serialization.OptionalField] public int Emigrated, Immigrated;
        // QoL-dependent mortality (Docs/QoLMortality.md): the annual growth ceiling r(1) - r(Q) that deaths at the camp's committed QoL removed, and that QoL.
        // Zero (and not shown) when the lever is off or the camp has no goods economy. Absent in older saves.
        [System.Runtime.Serialization.OptionalField] public double QoLMortalityReduction, QoL;
        // Fishing village support at the close boundary. Absent in older saves: zero.
        [System.Runtime.Serialization.OptionalField] public double FishingVillageMaturity, FishingVillageMealSupport;
        // PhysicalMortalityV1 diagnostics. Gross integer births/deaths are authoritative turn counters; continuous
        // causes and person-years expose why the settlement changed without deriving survival from composite QoL.
        [System.Runtime.Serialization.OptionalField] public DemographicModel Model;
        [System.Runtime.Serialization.OptionalField] public int GrossBirths, GrossDeaths;
        [System.Runtime.Serialization.OptionalField] public double ContinuousBirths, ContinuousDeaths;
        [System.Runtime.Serialization.OptionalField] public double ExposureDeaths, CrowdingDeaths, DiseaseDeaths, StarvationDeaths, AdditionalDeaths;
        [System.Runtime.Serialization.OptionalField] public double PersonYears, Nutrition, ClothingCoverage, HousingCoverage, AverageSanitationCoverage;
        [System.Runtime.Serialization.OptionalField] public double EffectiveFertilityRate, EffectiveMortalityRate;
        [System.Runtime.Serialization.OptionalField] public double ExposureMortalityRate, CrowdingMortalityRate, DiseaseMortalityRate, StarvationMortalityRate;
        [System.Runtime.Serialization.OptionalField] public double EquilibriumPopulation, CauseCorrection, ConservationError;
        // A terminal integer convention, not a mortality hazard: when a physically declining cohort ends below half a
        // continuous resident, the remaining residual is discarded and reported instead of creating an immortal singleton.
        [System.Runtime.Serialization.OptionalField] public double IntegerExtinctionResidual;
        [System.Runtime.Serialization.OptionalField] public bool IntegerExtinction;
        [System.Runtime.Serialization.OptionalField] public int RootEvaluations, BandsCrossed, HazardEvaluations, QuadratureEvaluations, ServiceIterations;
        // Joint expedition-density extension diagnostics. Exact scalar reductions retain the selected v1.1 analytical
        // propagator; heterogeneous service/history cohorts use a bounded adaptive log-vector solve.
        [System.Runtime.Serialization.OptionalField] public int CoupledPools, CoupledCohorts, CoupledRateEvaluations;
        [System.Runtime.Serialization.OptionalField] public int CoupledAcceptedSteps, CoupledRejectedSteps, CoupledServiceEvaluations, CoupledScalarReductions;
        [System.Runtime.Serialization.OptionalField] public bool HasPositiveEquilibrium, DeclineToExtinction;
        // Net food paid to (+) or by (-) this camp through trade after production (Docs/Trade.md: food is paid on delivery,
        // refunds included). Absent in older saves: zero.
        [System.Runtime.Serialization.OptionalField] public double TradeFood;
#pragma warning restore SYSLIB0050
        public string LimitingFactor;
        public string Explanation => "Population " + StartingPopulation + " + " + Additions + " additions - " + Deaths + " deaths = " + EndingPopulation + " (" + NetPercent.ToString("+0.0;-0.0;0.0", System.Globalization.CultureInfo.InvariantCulture) + "%). " + LimitingFactor + "." + (SplitOff > 0 ? " " + SplitOff + " of them then left to found a new settlement." : "") + (Emigrated > 0 ? " " + Emigrated + " then moved to other settlements." : "") + (Immigrated > 0 ? " " + Immigrated + " more arrived from other settlements." : "") + (QoLMortalityReduction > 1e-9 ? " Quality of life " + QoL.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " raised deaths and lowered the annual growth ceiling by " + (100 * QoLMortalityReduction).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " points." : "");
    }

    public static class PassiveGathering
    {
        public static bool Eligible(WorldState w, Camp camp, int tile) => w.Map.Distance(camp.Tile, tile) <= w.Config.GatherRadius && w.Tiles[tile].Terrain != Terrain.Ocean && w.Tiles[tile].ElevationLevel < 10;
        public static string Kind(Tile tile) => tile.WaterBody == WaterKind.Saltwater ? "Marine gathering" : tile.Aquatic ? "Aquatic gathering" : "Gathering";
    }
}
