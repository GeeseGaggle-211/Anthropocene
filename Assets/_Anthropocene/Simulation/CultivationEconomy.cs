using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    public sealed class AgriculturalLabor
    {
        public double OperatingFarmers, ExpansionFarmers, Gatherers, FoodGatherers, GatheringWorkerIntervals;
        public double MaintenanceCoveredWorkerYears, EffectiveMaintenanceFarmers, RequiredMaintenance;
        public int[] Footprint = Array.Empty<int>();
    }

    // A turn-local budget for the Farm Tools that existed when agricultural work
    // began.  Production later in the same turn cannot backdate field coverage.
    public sealed class FarmToolWorkBudget
    {
        public double RemainingTools;
        public double CoveredWorkerYears;
        private readonly Camp camp;
        public FarmToolWorkBudget(WorldState w, Camp camp)
        {
            this.camp = camp;
            RemainingTools = Math.Max(0, camp.Inventory.FarmTools);
        }
        public double AvailableWorkerYears(double yearsPerUnit)
            => Math.Max(0, RemainingTools * yearsPerUnit);
        public double Consume(double workerYears, double yearsPerUnit)
        {
            double covered = Math.Min(Math.Max(0, workerYears), RemainingTools * yearsPerUnit);
            RemainingTools = Math.Max(0, RemainingTools - covered / Math.Max(1e-12, yearsPerUnit));
            camp.Inventory.FarmTools = Math.Max(0, camp.Inventory.FarmTools - covered / Math.Max(1e-12, yearsPerUnit));
            CoveredWorkerYears += covered;
            return covered;
        }
    }

    public static class CultivationEconomy
    {
        public static bool Enabled(Camp camp) => camp.Technology == Technology.Cultivation || camp.Technologies[(int)Technology.Cultivation];
        public static bool HusbandryEnabled(Camp camp) => camp.Technology == Technology.AnimalHusbandry || camp.Technologies != null && camp.Technologies[(int)Technology.AnimalHusbandry];
        public static bool EconomyEnabled(Camp camp) => Enabled(camp) || HusbandryEnabled(camp);
        public static double Intensity(SimulationConfig c, double capital) => Math.Min(1, c.ReferenceIntensity * Math.Sqrt(Math.Max(0, capital)));
        public static double Decay(SimulationConfig c, double capital, double operatingFraction, double years) => capital * Math.Exp(-Math.Log(2) / c.Neolithic.DecayHalfLifeYears * (1 - Numbers.Clamp(operatingFraction, 0, 1)) * years);
        public static void Factors(SimulationConfig c, Tile t, out double build, out double maintenance)
        {
            var n = c.Neolithic;
            build = !Vegetation.IsWooded(t) ? n.OpenBuild : t.CurrentVegetation == VegetationTier.High ? n.ForestBuild : n.WoodlandBuild;
            maintenance = !Vegetation.IsWooded(t) ? n.OpenMaintenance : t.CurrentVegetation == VegetationTier.High ? n.ForestMaintenance : n.WoodlandMaintenance;
        }
        private static double BuildMultiplier(SimulationConfig c, Tile t, VegetationTier vegetation)
        {
            var n = c.Neolithic;
            bool wooded = t.Land && vegetation >= VegetationTier.Medium &&
                (t.Environment == EnvironmentKind.Temperate || t.Environment == EnvironmentKind.Tropical || t.Environment == EnvironmentKind.Cold);
            return !wooded ? n.OpenBuild : vegetation == VegetationTier.High ? n.ForestBuild : n.WoodlandBuild;
        }
        private static double MaintenanceMultiplier(SimulationConfig c, Tile t, VegetationTier vegetation)
        {
            var n = c.Neolithic;
            bool wooded = t.Land && vegetation >= VegetationTier.Medium &&
                (t.Environment == EnvironmentKind.Temperate || t.Environment == EnvironmentKind.Tropical || t.Environment == EnvironmentKind.Cold);
            return !wooded ? n.OpenMaintenance : vegetation == VegetationTier.High ? n.ForestMaintenance : n.WoodlandMaintenance;
        }
        public static int Workers(Camp camp, string role) => camp.Workforce.Assignments.Where(a => a.Role == role).Sum(a => a.Population);
        public static int[] Footprint(WorldState w, Camp camp) => w.Map.Radius(camp.Tile, 1).Where(id => w.Tiles[id].TraversableLand && w.Tiles[id].Environment != EnvironmentKind.Ice && w.Tiles[id].Seasons.Any(s => s.MaxPotentialFood > 0)).OrderBy(id => id).ToArray();
        public static AgriculturalLabor Prepare(WorldState w, Camp camp, double start, FarmToolWorkBudget toolBudget = null, double segmentYears = 0)
        {
            var footprint = CommitSeasonBoundary(w, camp, start);
            var plan = CalculateLabor(w, camp, footprint, toolBudget, segmentYears);
            CommitOperatingFraction(w, camp, plan);
            return plan;
        }
        internal static int[] CommitSeasonBoundary(WorldState w, Camp camp, double start, IReadOnlyDictionary<int, Camp> campsById = null, int[] cachedFootprint = null)
        {
            if (!Enabled(camp) || camp.Population <= 0) return Array.Empty<int>();
            var footprint = cachedFootprint ?? Footprint(w, camp);
            foreach (int id in footprint)
            {
                var t = w.Tiles[id]; LazyEcology.MaterializeEcology(w, id, start);
                if (t.CultivationOperator >= 0)
                {
                    Camp owner;
                    if (campsById == null) owner = w.Camps.FirstOrDefault(c => c.Id == t.CultivationOperator);
                    else campsById.TryGetValue(t.CultivationOperator, out owner);
                    if (owner == null || owner.Population <= 0 || !Enabled(owner) || w.Map.Distance(owner.Tile, id) > 1) t.CultivationOperator = -1;
                }
                if (t.CultivationCapital > 0 && t.CultivationOperator < 0) t.CultivationOperator = camp.Id;
            }
            return footprint;
        }
        internal static AgriculturalLabor CalculateLabor(WorldState w, Camp camp, int[] footprint, FarmToolWorkBudget toolBudget = null, double segmentYears = 0)
        {
            var plan = new AgriculturalLabor { Gatherers = camp.GathererPopulation + Workers(camp, WorkforceRoles.Gatherer) };
            plan.FoodGatherers = Math.Max(0, plan.Gatherers - (camp.EconomicLabor?.MaterialGatherers ?? 0));
            if (!Enabled(camp) || camp.Population <= 0) return plan;
            plan.Footprint = footprint;
            double required = 0;
            foreach (int id in plan.Footprint)
            {
                var t = w.Tiles[id];
                if (t.CultivationOperator != camp.Id) continue;
                Factors(w.Config, t, out _, out double maint);
                required += w.Config.Neolithic.MaintenanceWorkersPerTileE(w.Config.MaintenanceCoefficient) * maint * t.CultivationCapital;
            }
            plan.RequiredMaintenance = required;
            double farmers = Workers(camp, WorkforceRoles.Farmer);
            double years = segmentYears > 0 ? segmentYears : w.Config.TurnDays / w.Config.DaysPerYear;
            double efficiency = FarmToolFieldWorkEfficiency(w.Config.Neolithic);
            double availableCovered = Math.Min(farmers, (toolBudget ?? new FarmToolWorkBudget(w, camp)).AvailableWorkerYears(w.Config.Neolithic.FarmToolFarmerYearsPerUnit) / years);
            double coveredMaintenance = Math.Min(availableCovered, required / efficiency);
            double uncoveredMaintenance = Math.Min(Math.Max(0, farmers - coveredMaintenance), Math.Max(0, required - coveredMaintenance * efficiency));
            double maintenanceFarmers = coveredMaintenance + uncoveredMaintenance;
            plan.MaintenanceCoveredWorkerYears = coveredMaintenance * years;
            plan.EffectiveMaintenanceFarmers = coveredMaintenance * efficiency + uncoveredMaintenance;
            plan.OperatingFarmers = Math.Min(farmers, maintenanceFarmers);
            plan.ExpansionFarmers = Math.Max(0, farmers - plan.OperatingFarmers);
            bool established = plan.Footprint.Any(id => w.Tiles[id].CultivationOperator == camp.Id && w.Tiles[id].CultivationCapital > 0);
            if (camp.EconomicLabor != null)
            {
                // Maintenance is a minimum, not a ceiling on harvesting employment.
                // Explicit expansion time cannot simultaneously harvest or do upkeep.
                plan.ExpansionFarmers = Math.Min(plan.ExpansionFarmers, camp.EconomicLabor.ExpansionFarmers);
                plan.OperatingFarmers = established ? farmers - plan.ExpansionFarmers : 0;
            }
            return plan;
        }
        internal static void CommitOperatingFraction(WorldState w, Camp camp, AgriculturalLabor plan)
        {
            foreach (int id in plan.Footprint.Where(id => w.Tiles[id].CultivationOperator == camp.Id))
                w.Tiles[id].CultivationOperatingFraction = plan.RequiredMaintenance > 0
                    ? Math.Min(1, plan.EffectiveMaintenanceFarmers / Math.Max(1e-12, plan.RequiredMaintenance))
                    : plan.EffectiveMaintenanceFarmers > 0 ? 1 : 0;
        }

        public static double FarmToolFieldWorkEfficiency(NeolithicConfig n)
            => Math.Pow(n.StoneFarmToolsSupportedIntensity / n.PrimitiveAgricultureSupportedIntensity, 2);

        public static double MaintenanceWorkerRequirement(WorldState w, Camp camp)
        {
            double required = 0;
            foreach (int id in Footprint(w, camp))
            {
                var t = w.Tiles[id];
                if (t.CultivationOperator >= 0 && t.CultivationOperator != camp.Id) continue;
                var projected = LazyEcology.ProjectEcology(w, id, Math.Max(w.Days, t.LastEcologyUpdateDays));
                double maintenance = MaintenanceMultiplier(w.Config, t, projected.CurrentVegetation);
                required += w.Config.Neolithic.MaintenanceWorkersPerTileE(w.Config.MaintenanceCoefficient) * maintenance * Math.Max(0, projected.CultivationCapital);
            }
            return required;
        }

        // Raw worker-years that can still produce physical expansion on the
        // current owned footprint. This keeps planning from reserving tools for
        // a nearly full field whose remaining E room is smaller than one step.
        public static double ExpandableWorkerYears(WorldState w, Camp camp)
        {
            if (!Enabled(camp) || camp.Population <= 0) return 0;
            double ceiling = 1 / (w.Config.ReferenceIntensity * w.Config.ReferenceIntensity);
            double total = 0;
            foreach (int id in Footprint(w, camp))
            {
                var t = w.Tiles[id];
                if (t.CultivationOperator >= 0 && t.CultivationOperator != camp.Id) continue;
                var projected = LazyEcology.ProjectEcology(w, id, Math.Max(w.Days, t.LastEcologyUpdateDays));
                double e = Math.Max(0, projected.CultivationCapital);
                if (e >= ceiling - 1e-12) continue;
                double bridgeCeiling = Math.Min(ceiling, Math.Pow(w.Config.GathererBridgeCeiling / w.Config.ReferenceIntensity, 2));
                double openRoom = Math.Max(0, bridgeCeiling - e);
                double woodedRoom = Math.Max(0, ceiling - Math.Max(e, bridgeCeiling));
                total += w.Config.Neolithic.BuildWorkerYearsPerTileE *
                    (openRoom * w.Config.Neolithic.OpenBuild + woodedRoom * BuildMultiplier(w.Config, t, projected.CurrentVegetation));
            }
            return total;
        }

        public static double PlannedMaintenanceFarmers(WorldState w, Camp camp, double availableFarmers)
            => Math.Min(Math.Max(0, availableFarmers), MaintenanceWorkerRequirement(w, camp) / FarmToolFieldWorkEfficiency(w.Config.Neolithic));

        public static double MaintenanceFarmersForPlanning(WorldState w, Camp camp, double years)
        {
            if (years <= 0) return 0;
            double required = MaintenanceWorkerRequirement(w, camp);
            double efficiency = FarmToolFieldWorkEfficiency(w.Config.Neolithic);
            double covered = Math.Min(camp.Inventory.FarmTools * w.Config.Neolithic.FarmToolFarmerYearsPerUnit / years, required / efficiency);
            double uncovered = Math.Max(0, required - covered * efficiency);
            return Math.Min(camp.Population, covered + uncovered);
        }

        public static double ForecastFarmToolWorkerYears(WorldState w, Camp camp, double years)
        {
            if (!Enabled(camp) || camp.Population <= 0 || years <= 0) return 0;
            double required = MaintenanceWorkerRequirement(w, camp);
            bool expansionEligible = false;
            foreach (int id in Footprint(w, camp))
            {
                var t = w.Tiles[id];
                if (t.CultivationOperator >= 0 && t.CultivationOperator != camp.Id) continue;
                var projected = LazyEcology.ProjectEcology(w, id, Math.Max(w.Days, t.LastEcologyUpdateDays));
                double capital = Math.Max(0, projected.CultivationCapital);
                if (capital < 1 / (w.Config.ReferenceIntensity * w.Config.ReferenceIntensity) - 1e-12) expansionEligible = true;
            }
            double farmers = Workers(camp, WorkforceRoles.Farmer);
            double maintenanceFarmers = Math.Min(farmers, required / FarmToolFieldWorkEfficiency(w.Config.Neolithic));
            double expansionFarmers = camp.EconomicLabor?.ExpansionFarmers ?? Math.Max(0, farmers - maintenanceFarmers);
            if (!expansionEligible) expansionFarmers = 0;
            expansionFarmers = Math.Min(Math.Max(0, farmers - maintenanceFarmers), Math.Max(0, expansionFarmers));
            // Expand reports effective physical work after tool efficiency. The
            // forecast is raw covered farmer-years, so reserve only the raw work
            // needed to reach the remaining physical E room.
            expansionFarmers = Math.Min(expansionFarmers, ExpandableWorkerYears(w, camp) /
                Math.Max(1e-12, FarmToolFieldWorkEfficiency(w.Config.Neolithic)) / years);
            return Math.Max(0, maintenanceFarmers + expansionFarmers) * years;
        }

        // Planner-only projection: the proposal may reserve field workers that
        // are not yet persisted on the camp.  It remains bounded by residents
        // and by the existing managed footprint.
        public static double ForecastPlannedFarmToolWorkerYears(WorldState w, Camp camp, double years, double availableFarmers)
        {
            if (!Enabled(camp) || camp.Population <= 0 || years <= 0) return 0;
            double maintenance = PlannedMaintenanceFarmers(w, camp, availableFarmers);
            return Math.Max(0, maintenance) * years;
        }
        private struct CandidateScoreCache
        {
            public EcologyView Ecology;
            public double Capital, LandCondition, Step, Score;
            public VegetationTier Vegetation;
            public bool HasEcology, HasScore;
        }

        // Each increment is scored against the current capital and upkeep costs.
        // Scores can be reused only while this call's projected tile state and
        // exact E step are unchanged. A selected tile is invalidated after its
        // authoritative write; a callback may inspect or mutate arbitrary world
        // state, so supplying one disables reuse after each callback.
        public static double Expand(WorldState w, Camp camp, double workerYears, bool bridge, TurnRecord record, FarmToolWorkBudget toolBudget = null, Action<int> selectedTile = null, int[] cachedFootprint = null)
        {
            if (!Enabled(camp) || camp.Population <= 0 || workerYears <= 0 || bridge && w.Config.GathererBridgeEfficiency <= 0) return 0;
            var c = w.Config; var n = c.Neolithic;
            double coveredWorkerYears = !bridge && toolBudget != null ? Math.Min(workerYears, toolBudget.RemainingTools * n.FarmToolFarmerYearsPerUnit) : 0;
            double efficiency = !bridge ? FarmToolFieldWorkEfficiency(n) : 1;
            double remaining = bridge ? workerYears * c.GathererBridgeEfficiency : workerYears - coveredWorkerYears + coveredWorkerYears * efficiency;
            double ceiling = Math.Pow((bridge ? c.GathererBridgeCeiling : 1) / c.ReferenceIntensity, 2);
            var candidates = (cachedFootprint ?? Footprint(w, camp)).Where(id => w.Tiles[id].CultivationOperator < 0 || w.Tiles[id].CultivationOperator == camp.Id).ToArray();
            var scores = new CandidateScoreCache[candidates.Length];
            while (remaining > 1e-10)
            {
                int best = -1, bestIndex = -1; double bestScore = -1, step = 0, cost = 0;
                for (int candidateIndex = 0; candidateIndex < candidates.Length; candidateIndex++)
                {
                    int id = candidates[candidateIndex];
                    var t = w.Tiles[id];
                    ref CandidateScoreCache cache = ref scores[candidateIndex];
                    var ecology = cache.HasEcology
                        ? cache.Ecology
                        : LazyEcology.ProjectEcology(w, id, Math.Max(w.Days, t.LastEcologyUpdateDays));
                    cache.Ecology = ecology;
                    cache.HasEcology = true;
                    double e = ecology.CultivationCapital;
                    if (e >= ceiling - 1e-12) continue;
                    double vb = BuildMultiplier(c, t, ecology.CurrentVegetation);
                    double vm = MaintenanceMultiplier(c, t, ecology.CurrentVegetation);
                    if (bridge || Intensity(c, e) < c.GathererBridgeCeiling) vb = n.OpenBuild;
                    double de = Math.Min(n.AllocationStepE, Math.Min(ceiling - e, remaining / (n.BuildWorkerYearsPerTileE * vb)));
                    if (!bridge && Intensity(c, e) < c.GathererBridgeCeiling) de = Math.Min(de, Math.Pow(c.GathererBridgeCeiling / c.ReferenceIntensity, 2) - e);
                    if (de <= 1e-12) continue;
                    bool scoreMatches = cache.HasScore && cache.Capital == e && cache.LandCondition == ecology.LandCondition &&
                        cache.Vegetation == ecology.CurrentVegetation && cache.Step == de;
                    double score;
                    if (scoreMatches)
                    {
                        score = cache.Score;
                    }
                    else
                    {
                        double gain = 0;
                        var beforeView = ecology;
                        var afterView = ecology;
                        afterView.CultivationCapital = e + de;
                        for (int season = 0; season < 4; season++)
                        {
                            var beforeCapacity = new EcologyCapacity(w, t, beforeView, season, 1);
                            var afterCapacity = new EcologyCapacity(w, t, afterView, season, 1);
                            double beforeFlow = Math.Min(beforeCapacity.Food * beforeCapacity.FoodRate + beforeCapacity.ManagedFlow,
                                EcologyMath.SustainableHarvestRate(beforeCapacity.Biomass, beforeCapacity.BiomassRate, c.SustainableProductionFraction, beforeCapacity.ManagedFlow) * c.SafeUseRatio);
                            double afterFlow = Math.Min(afterCapacity.Food * afterCapacity.FoodRate + afterCapacity.ManagedFlow,
                                EcologyMath.SustainableHarvestRate(afterCapacity.Biomass, afterCapacity.BiomassRate, c.SustainableProductionFraction, afterCapacity.ManagedFlow) * c.SafeUseRatio);
                            gain += (afterFlow - beforeFlow) * c.DaysPerYear / 4;
                        }
                        double buildCost = de * n.BuildWorkerYearsPerTileE * vb;
                        score = gain / Math.Max(1e-12, buildCost + n.PlanningHorizonYears * n.MaintenanceWorkersPerTileE(c.MaintenanceCoefficient) * vm * de);
                        cache.Capital = e;
                        cache.LandCondition = ecology.LandCondition;
                        cache.Vegetation = ecology.CurrentVegetation;
                        cache.Step = de;
                        cache.Score = score;
                        cache.HasScore = true;
                    }
                    double candidateCost = de * n.BuildWorkerYearsPerTileE * vb;
                    bool focused = id == camp.CultivationFocusTile;
                    if (score > 0 && (best < 0 || focused || best != camp.CultivationFocusTile && score > bestScore)) { best = id; bestIndex = candidateIndex; bestScore = score; step = de; cost = candidateCost; }
                }
                if (best < 0) break;
                selectedTile?.Invoke(best);
                if (selectedTile != null)
                    for (int i = 0; i < scores.Length; i++) { scores[i].HasEcology = false; scores[i].HasScore = false; }
                var target = w.Tiles[best];
                var beforeEcology = LazyEcology.MaterializeEcology(w, best, Math.Max(w.Days, target.LastEcologyUpdateDays));
                double oldQ = Intensity(c, beforeEcology.CultivationCapital);
                double oldRetention = EcologyMath.CultivationVegetationRetention(c, oldQ);
                target.CultivationOperator = camp.Id; target.CultivationCapital = beforeEcology.CultivationCapital + step;
                double newQ = Intensity(c, target.CultivationCapital);
                target.Cultivation = (float)newQ; target.Cultivated = target.CultivationCapital > 0;
                double newRetention = EcologyMath.CultivationVegetationRetention(c, newQ);
                if (newRetention < oldRetention)
                {
                    double woodCapacityLoss = MaterialEconomy.ClampWoodSource(w, best, beforeEcology, new EcologyView(target));
                    double displacedRetention = oldRetention - newRetention;
                    record.Pressure.Add(new PressureEntry
                    {
                        Tile = best, Entity = camp.Id, Kind = "Cultivation vegetation displacement", Pool = "Vegetation cover",
                        Requested = displacedRetention, Realized = displacedRetention, WoodCapacityLoss = woodCapacityLoss, ConditionDamage = 0
                    });
                }
                remaining -= cost;
                if (bestIndex >= 0) { scores[bestIndex].HasEcology = false; scores[bestIndex].HasScore = false; }
            }
            double effectiveUsed = (bridge ? workerYears * c.GathererBridgeEfficiency : workerYears - coveredWorkerYears + coveredWorkerYears * efficiency) - remaining;
            if (!bridge && toolBudget != null && effectiveUsed > 0)
            {
                double coveredUsed = Math.Min(coveredWorkerYears, effectiveUsed / efficiency);
                toolBudget.Consume(coveredUsed, n.FarmToolFarmerYearsPerUnit);
                double uncoveredUsed = Math.Max(0, effectiveUsed - coveredUsed * efficiency);
                return coveredUsed + uncoveredUsed;
            }
            return effectiveUsed / (bridge ? c.GathererBridgeEfficiency : 1);
        }
    }
}
