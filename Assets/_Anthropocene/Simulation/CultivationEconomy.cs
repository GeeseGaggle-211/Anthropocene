using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Anthropocene.Simulation
{
    public sealed class AgriculturalLabor
    {
        public double OperatingFarmers, ExpansionFarmers, Gatherers, FoodGatherers, GatheringWorkerIntervals;
        public double MaintenanceCoveredWorkerYears, EffectiveMaintenanceFarmers, RequiredMaintenance;
        // Exact workforce time reserved for the existing maintenance task.
        // Operating farmers also supply harvest capacity, so callers must treat
        // these as overlapping measurements rather than additive work.
        public double MaintenanceFarmerWorkerYears;
        public double HarvestProductivity;
        public int[] Footprint = Array.Empty<int>();
    }

    // A proposed whole-turn investment. The starting and mean capital are read by
    // the resource pass; only EndCapital is committed after its receipts. A bridge
    // estimate may be replaced once with realized terrestrial gathering, without
    // changing the efficacy already used for this turn's claims.
    public sealed class HorizonCultivationAllocation
    {
        public int CampId;
        public int[] Tiles = Array.Empty<int>();
        public double[] StartCapital = Array.Empty<double>();
        public double[] AverageCapital = Array.Empty<double>();
        public double[] EndCapital = Array.Empty<double>();
        public double FarmerWorkerYearsUsed, BridgeWorkerYearsUsed, CoveredFarmerWorkerYears;
        internal EcologyView[] StartEcology = Array.Empty<EcologyView>();
        internal int[] StartOperators = Array.Empty<int>();
        internal double[] StartOperatingFractions = Array.Empty<double>();
        internal double Years, FarmerWorkerYearsRequested, BridgeWorkerYearsRequested;

        public bool TryGetTile(int tile, out double averageCapital, out double endCapital)
        {
            int index = Array.BinarySearch(Tiles, tile);
            if (index < 0) { averageCapital = 0; endCapital = 0; return false; }
            averageCapital = AverageCapital[index]; endCapital = EndCapital[index]; return true;
        }

        public int[] NewFieldTiles()
        {
            var selected = new List<int>();
            for (int i = 0; i < Tiles.Length; i++)
                if (StartOperators[i] < 0 && EndCapital[i] > StartCapital[i] + 1e-12) selected.Add(Tiles[i]);
            return selected.ToArray();
        }
    }

    // A turn-local budget for the Farm Tools that existed when agricultural work
    // began.  Production later in the same turn cannot backdate field coverage.
    public sealed class FarmToolWorkBudget
    {
        public double RemainingTools;
        public double CoveredWorkerYears;
        // Horizon preparation reserves this work without debiting inventory. The
        // maintenance and expansion service is debited once at endpoint commit.
        public double ReservedMaintenanceWorkerYears;
        public bool HorizonCommitted;
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

    // Cultivation and Fisheries share the same bounded effort-to-capital
    // transform. Keeping these equations neutral avoids making either food
    // system depend on the other's ecological or planning rules.
    public static class NeolithicCapital
    {
        public static double Intensity(SimulationConfig config, double capital)
            => Math.Min(1, config.ReferenceIntensity * Math.Sqrt(Math.Max(0, capital)));

        public static double EffortForCapital(SimulationConfig config, double capital)
        {
            double e = Math.Max(0, capital);
            double factor = config.Neolithic.CultivationMarginalEffortMultiplierPerTenth;
            if (factor <= 1 || e <= 1) return e;
            double rate = Math.Log(factor) / .1;
            double q = Intensity(config, e);
            return 1 + 2 / (config.ReferenceIntensity * rate) *
                (Math.Exp(rate * (q - config.ReferenceIntensity)) - 1);
        }

        public static double AdditionalEffort(SimulationConfig config,
            double fromCapital, double toCapital)
        {
            if (toCapital <= fromCapital) return 0;
            if (config.Neolithic.CultivationMarginalEffortMultiplierPerTenth <= 1)
                return toCapital - fromCapital;
            return Math.Max(0, EffortForCapital(config, toCapital) -
                EffortForCapital(config, fromCapital));
        }

        public static double CapitalForEffort(SimulationConfig config, double effort)
        {
            double weight = Math.Max(0, effort);
            double factor = config.Neolithic.CultivationMarginalEffortMultiplierPerTenth;
            if (factor <= 1 || weight <= 1) return weight;
            double rate = Math.Log(factor) / .1;
            double q = config.ReferenceIntensity + Math.Log(1 +
                (weight - 1) * config.ReferenceIntensity * rate / 2) / rate;
            return Math.Pow(Math.Min(1, q) / config.ReferenceIntensity, 2);
        }

        public static double Decay(SimulationConfig config, double capital,
            double operatingFraction, double years)
            => capital * Math.Exp(-Math.Log(2) / config.Neolithic.DecayHalfLifeYears *
                (1 - Numbers.Clamp(operatingFraction, 0, 1)) * years);

        public static double MeanDecayedCapital(SimulationConfig config, double capital,
            double operatingFraction, double years)
        {
            double rate = Math.Log(2) / config.Neolithic.DecayHalfLifeYears *
                (1 - Numbers.Clamp(operatingFraction, 0, 1));
            return Math.Max(0, capital) * EcologyMath.MeanExponential(rate, years);
        }
    }

    public static class CultivationEconomy
    {
        public static bool Enabled(Camp camp) => camp.Technology == Technology.Cultivation || camp.Technologies[(int)Technology.Cultivation];
        public static bool HusbandryEnabled(Camp camp) => camp.Technology == Technology.AnimalHusbandry || camp.Technologies != null && camp.Technologies[(int)Technology.AnimalHusbandry];
        public static bool EconomyEnabled(Camp camp) => Enabled(camp) || HusbandryEnabled(camp);
        public static bool EconomyEnabled(WorldState world, Camp camp)
            => EconomyEnabled(camp) || NeolithicCapabilities.FeatureFishingEnabled(world, camp);
        public static double Intensity(SimulationConfig c, double capital) => NeolithicCapital.Intensity(c, capital);
        // Capital remains the saved physical field state. This derived labor
        // weight leaves all q <= qt unchanged, then raises marginal effort by
        // the configured factor for every additional .1 intensity. Value and
        // first derivative agree with the legacy quadratic curve at qt.
        public static double EffortForCapital(SimulationConfig c, double capital)
            => NeolithicCapital.EffortForCapital(c, capital);
        public static double AdditionalEffort(SimulationConfig c, double fromCapital, double toCapital)
            => NeolithicCapital.AdditionalEffort(c, fromCapital, toCapital);
        public static double CapitalForEffort(SimulationConfig c, double effort)
            => NeolithicCapital.CapitalForEffort(c, effort);
        public static double Decay(SimulationConfig c, double capital, double operatingFraction, double years)
            => NeolithicCapital.Decay(c, capital, operatingFraction, years);
        public static double MeanDecayedCapital(SimulationConfig c, double capital,
            double operatingFraction, double years)
            => NeolithicCapital.MeanDecayedCapital(c, capital, operatingFraction, years);
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
        internal static bool Cultivable(Tile tile) => tile.TraversableLand && tile.Environment != EnvironmentKind.Ice && tile.Seasons.Any(s => s.MaxPotentialFood > 0);
        public static int[] Footprint(WorldState w, Camp camp) => w.Map.Radius(camp.Tile, 1).Where(id => Cultivable(w.Tiles[id])).OrderBy(id => id).ToArray();
        // Infer the crop portion of the current accessible stock from the shared
        // capacity. Wild and cultivated food remain one conserved physical pool.
        // Proportional claims have the same mix, so their effective rate is the
        // stock-weighted harmonic rate of wild gathering and crop harvesting.
        // One opening-profile query per local field; no future/yearly loop.
        public static double HarvestProductivity(WorldState w, Camp camp, int[] footprint = null)
        {
            var cfg = w.Config;
            if (!cfg.CropHarvestingOnly || cfg.FarmerProductivity <= 1) return cfg.FarmerProductivity;
            double food = 0, work = 0;
            int season = (int)Math.Floor(w.Days / cfg.BaseDays) % 4;
            foreach (int id in footprint ?? Footprint(w, camp))
            {
                var tile = w.Tiles[id];
                var view = LazyEcology.ProjectEcology(w, id, Math.Max(w.Days, tile.LastEcologyUpdateDays));
                var capacity = new EcologyCapacity(w, tile, view, season);
                double available = Math.Min(Math.Max(0, view.Food), capacity.Food);
                double crop = tile.CultivationOperator >= 0 && tile.CultivationOperator != camp.Id
                    ? 0 : Math.Max(0, capacity.Food - capacity.WildFood);
                double cropShare = capacity.Food > 0 ? crop / capacity.Food : 0;
                food += available;
                work += available * (1 - cropShare + cropShare / cfg.FarmerProductivity);
            }
            return work > 0 ? food / work : 1;
        }
        // Village status is an endpoint decision, never an input to this turn's
        // claim or field plan. Count every nearby land tile in the original
        // footprint average, with only this camp's fields contributing q.
        internal static void UpdateVillageAtBoundary(WorldState w, Camp camp, TurnRecord record)
            => UpdateVillageAtBoundary(w, camp, record, 0, 0, false);

        // Agriculture and Fishing share one endpoint transition. Agriculture
        // retains its original footprint threshold; a Fishing village needs
        // both mature worked grounds and enough realized catch to support meals.
        internal static void UpdateVillageAtBoundary(WorldState w, Camp camp, TurnRecord record,
            double fishingMaturity, double fishingMealSupport, bool departing)
        {
            bool agriculture = Enabled(camp);
            bool fishing = NeolithicCapabilities.FeatureFishingEnabled(w, camp);
            if ((!agriculture && !fishing) || camp.Population <= 0 || departing) return;

            double average = 0;
            if (agriculture)
            {
                var land = w.Map.Radius(camp.Tile, 1).Where(id => w.Tiles[id].TraversableLand).OrderBy(id => id).ToArray();
                if (land.Length > 0)
                    average = land.Sum(id => w.Tiles[id].CultivationOperator == camp.Id
                        ? Intensity(w.Config, w.Tiles[id].CultivationCapital) : 0) / land.Length;
            }
            bool agriculturalQualification = agriculture && average + 1e-10 >= VillageIntensity;
            bool fishingQualification = fishing && fishingMaturity + 1e-10 >= .5 && fishingMealSupport + 1e-10 >= .5;
            if (!agriculturalQualification && !fishingQualification)
            {
                if (camp.IsVillage) camp.VillageTurns++;
                return;
            }
            if (!camp.IsVillage)
            {
                camp.IsVillage = true;
                camp.VillageTurns = 0;
                string reason = agriculturalQualification
                    ? $"Avg Cultivation {average:F2}"
                    : $"Fishing maturity {fishingMaturity:F2}, meal support {fishingMealSupport:F2}";
                record.Log($"Camp {camp.Id} (Player {camp.Player}) has transitioned into a Village ({reason}).");
            }
            camp.VillageTurns++;
        }
        public static AgriculturalLabor Prepare(WorldState w, Camp camp, double start, FarmToolWorkBudget toolBudget = null, double segmentYears = 0)
        {
            var footprint = CommitSeasonBoundary(w, camp, start);
            var plan = CalculateLabor(w, camp, footprint, toolBudget, segmentYears);
            CommitOperatingFraction(w, camp, plan);
            return plan;
        }

        // The horizon resolver calls this once after movement. Ownership and the
        // operating regime are fixed before claims; the tool debit waits until
        // the endpoint so newly produced tools cannot cover this turn's work.
        public static AgriculturalLabor PrepareHorizon(WorldState w, Camp camp, double startDays, double years,
            FarmToolWorkBudget toolBudget = null, int[] cachedFootprint = null)
        {
            if (double.IsNaN(years) || double.IsInfinity(years) || years < 0)
                throw new ArgumentOutOfRangeException(nameof(years));
            if (years == 0)
            {
                var idle = new AgriculturalLabor
                {
                    Gatherers = camp.GathererPopulation + Workers(camp, WorkforceRoles.Gatherer),
                    Footprint = cachedFootprint ?? Footprint(w, camp)
                };
                idle.FoodGatherers = Math.Max(0, idle.Gatherers - (camp.EconomicLabor?.MaterialGatherers ?? 0));
                if (toolBudget != null) toolBudget.ReservedMaintenanceWorkerYears = 0;
                return idle;
            }
            var footprint = CommitSeasonBoundary(w, camp, startDays, null, cachedFootprint);
            var plan = CalculateLabor(w, camp, footprint, toolBudget, years);
            CommitOperatingFraction(w, camp, plan);
            if (toolBudget != null) toolBudget.ReservedMaintenanceWorkerYears = plan.MaintenanceCoveredWorkerYears;
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
            plan.HarvestProductivity = HarvestProductivity(w, camp, footprint);
            double required = 0;
            foreach (int id in plan.Footprint)
            {
                var t = w.Tiles[id];
                if (t.CultivationOperator != camp.Id) continue;
                Factors(w.Config, t, out _, out double maint);
                required += w.Config.Neolithic.MaintenanceWorkersPerTileE(w.Config.MaintenanceCoefficient) * maint * EffortForCapital(w.Config, t.CultivationCapital);
            }
            plan.RequiredMaintenance = required;
            double farmers = Workers(camp, WorkforceRoles.Farmer);
            // The shared portfolio may deliberately let existing fields decay
            // while building a more productive local field. Its explicit build
            // work cannot first be spent maintaining the abandoned capital.
            double buildFarmers = w.Config.SharedCropHerdAllocationEnabled
                ? Math.Min(farmers, Math.Max(0, camp.EconomicLabor?.ExpansionFarmers ?? 0)) : 0;
            double upkeepFarmers = farmers - buildFarmers;
            double years = segmentYears > 0 ? segmentYears : w.Config.TurnDays / w.Config.DaysPerYear;
            double efficiency = FarmToolFieldWorkEfficiency(w.Config.Neolithic);
            double availableCovered = Math.Min(upkeepFarmers, (toolBudget ?? new FarmToolWorkBudget(w, camp)).AvailableWorkerYears(w.Config.Neolithic.FarmToolFarmerYearsPerUnit) / years);
            double coveredMaintenance = Math.Min(availableCovered, required / efficiency);
            double uncoveredMaintenance = Math.Min(Math.Max(0, upkeepFarmers - coveredMaintenance), Math.Max(0, required - coveredMaintenance * efficiency));
            double maintenanceFarmers = coveredMaintenance + uncoveredMaintenance;
            plan.MaintenanceFarmerWorkerYears = maintenanceFarmers * years;
            plan.MaintenanceCoveredWorkerYears = coveredMaintenance * years;
            plan.EffectiveMaintenanceFarmers = coveredMaintenance * efficiency + uncoveredMaintenance;
            plan.OperatingFarmers = Math.Min(farmers, maintenanceFarmers);
            plan.ExpansionFarmers = Math.Max(0, farmers - plan.OperatingFarmers);
            bool established = plan.Footprint.Any(id => w.Tiles[id].CultivationOperator == camp.Id && w.Tiles[id].CultivationCapital > 0);
            if (camp.EconomicLabor != null)
            {
                // Maintenance is a minimum, not a ceiling on harvesting employment.
                // Explicit expansion time cannot simultaneously harvest or do upkeep.
                plan.ExpansionFarmers = w.Config.SharedCropHerdAllocationEnabled ? buildFarmers
                    : Math.Min(plan.ExpansionFarmers, camp.EconomicLabor.ExpansionFarmers);
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
                required += w.Config.Neolithic.MaintenanceWorkersPerTileE(w.Config.MaintenanceCoefficient) * maintenance * EffortForCapital(w.Config, projected.CultivationCapital);
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
                double openRoom = AdditionalEffort(w.Config, e, Math.Max(e, bridgeCeiling));
                double woodedRoom = AdditionalEffort(w.Config, Math.Max(e, bridgeCeiling), ceiling);
                total += w.Config.Neolithic.BuildWorkerYearsPerTileE *
                    (openRoom * w.Config.Neolithic.OpenBuild + woodedRoom * BuildMultiplier(w.Config, t, projected.CurrentVegetation));
            }
            return total;
        }

        public static double PlannedMaintenanceFarmers(WorldState w, Camp camp, double availableFarmers)
            => Math.Min(Math.Max(0, availableFarmers), MaintenanceFarmersForPlanning(w, camp, w.Config.TurnDays / w.Config.DaysPerYear));

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

        private sealed class HorizonCandidate
        {
            public int Tile;
            public EcologyView Ecology;
            public double Start, Decayed, Capital, OperatingFraction;
            public int StartOperator;
            public bool Eligible = true;
        }

        // The camp's fields at the start of the turn, in tile order: the footprint
        // tiles it may operate, each with its projected ecology. Shared by the
        // resolver's horizon proposal and the planner's expansion ladder.
        private static HorizonCandidate[] StartCandidates(WorldState w, Camp camp, int[] cachedFootprint, PlanningKernelCache cache = null)
        {
            int[] ids = !Enabled(camp) || camp.Population <= 0
                ? Array.Empty<int>()
                : (cachedFootprint ?? Footprint(w, camp))
                    .Where(id => w.Tiles[id].CultivationOperator < 0 || w.Tiles[id].CultivationOperator == camp.Id)
                    .OrderBy(id => id).ToArray();
            var candidates = new HorizonCandidate[ids.Length];
            for (int i = 0; i < ids.Length; i++)
            {
                int id = ids[i]; var tile = w.Tiles[id];
                var view = cache != null ? cache.View(id, Math.Max(w.Days, tile.LastEcologyUpdateDays))
                    : LazyEcology.ProjectEcology(w, id, Math.Max(w.Days, tile.LastEcologyUpdateDays));
                candidates[i] = new HorizonCandidate
                {
                    Tile = id, Ecology = view, Start = Math.Max(0, view.CultivationCapital),
                    OperatingFraction = tile.CultivationOperatingFraction, StartOperator = tile.CultivationOperator
                };
            }
            return candidates;
        }

        // One proposal over the whole turn. The fixed 32 intensity bands are
        // capital-allocation resolution, not time steps: their count does not
        // depend on turn length, BaseDays, or the amount of available work.
        public static HorizonCultivationAllocation PlanHorizonExpansion(WorldState w, Camp camp, double years,
            double farmerWorkerYears, double bridgeWorkerYears, FarmToolWorkBudget toolBudget,
            int[] cachedFootprint = null)
        {
            var candidates = StartCandidates(w, camp, cachedFootprint);
            return BuildHorizonAllocation(w, camp, years, farmerWorkerYears, bridgeWorkerYears, toolBudget, candidates);
        }

        // One step of the allocator's greedy ladder (WP4b): the field it raises,
        // its marginal score (annualised retained food per worker-year, as the
        // allocator compares steps), the build effort it costs in worker-years
        // (what the allocator charges, cost-per-effort applied) and the annualised
        // retained food it adds. Transient; never saved.
        internal struct ExpansionStep
        {
            public int Tile;
            public double Score, WorkerYears, AnnualGain, MaintenancePerYear;
            // Village-footprint average field intensity after this step (the quantity UpdateVillageAtBoundary compares with
            // VillageIntensity). Recorded only for the planner's ladder; 0 elsewhere.
            public double VillageAverage;
        }
        // A camp becomes a village when the average field intensity over its radius-one land reaches this value.
        internal const double VillageIntensity = .25;

        // The steps the resolver's farmer allocation would take from the
        // start-of-turn committed state given unlimited effort, in its order: the
        // same candidates, decay, operator guards and greedy loop, recorded rather
        // than capped. It ends when no step has a positive score (or at the pass
        // guard). Effort is worker-years; Farm Tool efficiency and the bridge pass
        // are ignored (the ladder is the plain-farmer marginal-product curve).
        // annualGainCap stops recording once the cumulative annual food reaches it (the
        // step that crosses it is kept): the valuation never reads beyond the total food gap.
        // Pure: reads the projected ecology only.
        // cache (optional, one plan only) memoizes the pure opening projections
        // and marginal investments; it never changes a returned step.
        [ThreadStatic] private static List<ExpansionStep> ladderRecord;
        internal static ExpansionStep[] ExpansionLadder(WorldState w, Camp camp, double years, int[] cachedFootprint = null, double annualGainCap = double.PositiveInfinity, double? operatingFraction = null,
            PlanningKernelCache cache = null)
        {
            if (!(years > 0) || double.IsInfinity(years)) return Array.Empty<ExpansionStep>();
            if (ResolutionMetrics.CaptureDetailedCounters) Interlocked.Increment(ref TurnDiagnostics.For(w).ExpansionLadderBuilds);
            var candidates = StartCandidates(w, camp, cachedFootprint, cache);
            if (cache == null && ResolutionMetrics.CaptureDetailedCounters) Interlocked.Add(ref TurnDiagnostics.For(w).PlannerTileProjections, candidates.Length);
            if (operatingFraction.HasValue) foreach (var field in candidates) field.OperatingFraction = operatingFraction.Value;
            foreach (var field in candidates) { field.Decayed = Decay(w.Config, field.Start, field.OperatingFraction, years); field.Capital = field.Decayed; }
            // One recording buffer per worker thread, reused by every ladder; the
            // returned array is the ladder's own. Sized to the greedy loop's pass guard.
            var record = ladderRecord ?? (ladderRecord = new List<ExpansionStep>()); record.Clear();
            if (record.Capacity < candidates.Length * 35) record.Capacity = candidates.Length * 35;
            AllocateHorizonEffort(w, camp, candidates, double.PositiveInfinity, false, years, record, annualGainCap, cache);
            return record.ToArray();
        }

        // The ladder that ExpansionLadder would have recorded with annualGainCap = cap:
        // the same running sum and test, so a capped ladder is a prefix of a longer one.
        internal static ExpansionStep[] CutLadder(ExpansionStep[] steps, double cap)
        {
            double recordedGain = 0;
            for (int i = 0; i < steps.Length; i++)
                if ((recordedGain += steps[i].AnnualGain) >= cap)
                {
                    if (i + 1 == steps.Length) return steps;
                    var cut = new ExpansionStep[i + 1]; Array.Copy(steps, cut, i + 1); return cut;
                }
            return steps;
        }

        // The preclaim bridge estimate can be corrected from realized terrestrial
        // receipts once. Reuse the saved starting views, so endpoint integration
        // cannot change the field scores or move a decision backward in time.
        public static HorizonCultivationAllocation ReconcileHorizonBridge(WorldState w, Camp camp,
            HorizonCultivationAllocation preclaim, double realizedBridgeWorkerYears, FarmToolWorkBudget toolBudget)
        {
            if (preclaim == null || preclaim.CampId != camp.Id) throw new ArgumentException("Invalid horizon cultivation proposal.");
            var candidates = new HorizonCandidate[preclaim.Tiles.Length];
            for (int i = 0; i < candidates.Length; i++)
                candidates[i] = new HorizonCandidate
                {
                    Tile = preclaim.Tiles[i], Ecology = preclaim.StartEcology[i],
                    Start = preclaim.StartCapital[i], OperatingFraction = preclaim.StartOperatingFractions[i],
                    StartOperator = preclaim.StartOperators[i],
                    // A final receipt cannot reserve a new field that another
                    // camp was allowed to plan against in this same horizon.
                    Eligible = preclaim.StartOperators[i] >= 0 ||
                        preclaim.EndCapital[i] > preclaim.StartCapital[i] + 1e-12
                };
            return BuildHorizonAllocation(w, camp, preclaim.Years, preclaim.FarmerWorkerYearsRequested,
                realizedBridgeWorkerYears, toolBudget, candidates);
        }

        private static HorizonCultivationAllocation BuildHorizonAllocation(WorldState w, Camp camp, double years,
            double farmerWorkerYears, double bridgeWorkerYears, FarmToolWorkBudget toolBudget, HorizonCandidate[] candidates)
        {
            if (double.IsNaN(years) || double.IsInfinity(years) || years < 0)
                throw new ArgumentOutOfRangeException(nameof(years));
            if (double.IsNaN(farmerWorkerYears) || double.IsInfinity(farmerWorkerYears) || farmerWorkerYears < 0)
                throw new ArgumentOutOfRangeException(nameof(farmerWorkerYears));
            if (double.IsNaN(bridgeWorkerYears) || double.IsInfinity(bridgeWorkerYears) || bridgeWorkerYears < 0)
                throw new ArgumentOutOfRangeException(nameof(bridgeWorkerYears));
            var c = w.Config; var n = c.Neolithic;
            var result = new HorizonCultivationAllocation
            {
                CampId = camp.Id, Years = years,
                FarmerWorkerYearsRequested = years > 0 ? Math.Max(0, farmerWorkerYears) : 0,
                BridgeWorkerYearsRequested = years > 0 ? Math.Max(0, bridgeWorkerYears) : 0,
                Tiles = new int[candidates.Length], StartCapital = new double[candidates.Length],
                AverageCapital = new double[candidates.Length], EndCapital = new double[candidates.Length],
                StartEcology = new EcologyView[candidates.Length], StartOperators = new int[candidates.Length],
                StartOperatingFractions = new double[candidates.Length]
            };
            for (int i = 0; i < candidates.Length; i++)
            {
                var field = candidates[i];
                field.Decayed = Decay(c, field.Start, field.OperatingFraction, result.Years);
                field.Capital = field.Decayed;
                result.Tiles[i] = field.Tile; result.StartCapital[i] = field.Start;
                result.StartEcology[i] = field.Ecology; result.StartOperators[i] = field.StartOperator;
                result.StartOperatingFractions[i] = field.OperatingFraction;
            }
            double bridgeEffective = result.BridgeWorkerYearsRequested * c.GathererBridgeEfficiency;
            double bridgeRemaining = AllocateHorizonEffort(w, camp, candidates, bridgeEffective, true, years);
            result.BridgeWorkerYearsUsed = c.GathererBridgeEfficiency > 0
                ? (bridgeEffective - bridgeRemaining) / c.GathererBridgeEfficiency : 0;
            double toolWorkerYears = toolBudget == null ? 0 : Math.Max(0,
                toolBudget.AvailableWorkerYears(n.FarmToolFarmerYearsPerUnit) - toolBudget.ReservedMaintenanceWorkerYears);
            double covered = Math.Min(result.FarmerWorkerYearsRequested, toolWorkerYears);
            double efficiency = FarmToolFieldWorkEfficiency(n);
            double farmerEffective = result.FarmerWorkerYearsRequested - covered + covered * efficiency;
            double farmerRemaining = AllocateHorizonEffort(w, camp, candidates, farmerEffective, false, years);
            double farmerEffectiveUsed = Math.Max(0, farmerEffective - farmerRemaining);
            result.CoveredFarmerWorkerYears = Math.Min(covered, farmerEffectiveUsed / Math.Max(1e-12, efficiency));
            result.FarmerWorkerYearsUsed = result.CoveredFarmerWorkerYears +
                Math.Max(0, farmerEffectiveUsed - result.CoveredFarmerWorkerYears * efficiency);
            for (int i = 0; i < candidates.Length; i++)
            {
                var field = candidates[i];
                result.EndCapital[i] = field.Capital;
                double rate = Math.Log(2) / n.DecayHalfLifeYears * (1 - Numbers.Clamp(field.OperatingFraction, 0, 1));
                double meanDecay = rate * result.Years > 1e-10
                    ? field.Start * (1 - Math.Exp(-rate * result.Years)) / (rate * result.Years)
                    : field.Start;
                // Investment is spread over the turn for mean efficacy, but only
                // its endpoint is an authoritative state write.
                result.AverageCapital[i] = Math.Max(0, meanDecay + .5 * (field.Capital - field.Decayed));
            }
            return result;
        }

        // record (ladder mode, WP4b): when given, each step taken is appended; an
        // infinite effort takes every positive-score step whole. Null record and a
        // finite effort are the resolver's allocation, unchanged.
        private static double AllocateHorizonEffort(WorldState w, Camp camp, HorizonCandidate[] candidates,
            double effectiveWorkerYears, bool bridge, double years, List<ExpansionStep> record = null, double annualGainCap = double.PositiveInfinity,
            PlanningKernelCache cache = null)
        {
            if (effectiveWorkerYears <= 1e-10 || candidates.Length == 0) return Math.Max(0, effectiveWorkerYears);
            var c = w.Config; var n = c.Neolithic;
            double ceiling = Math.Pow((bridge ? c.GathererBridgeCeiling : 1) / c.ReferenceIntensity, 2);
            double bridgeCeiling = Math.Pow(c.GathererBridgeCeiling / c.ReferenceIntensity, 2);
            double remaining = effectiveWorkerYears;
            var cached = new bool[candidates.Length];
            var scores = new double[candidates.Length];
            var steps = new double[candidates.Length];
            var costsPerEffort = new double[candidates.Length];
            var gains = new double[candidates.Length];
            bool unbounded = double.IsPositiveInfinity(effectiveWorkerYears);
            double recordedGain = 0;
            // Ladder mode also tracks the village-footprint average: fields the camp operates at the start or has stepped.
            int villageLand = record != null ? w.Map.Radius(camp.Tile, 1).Count(id => w.Tiles[id].TraversableLand) : 0;
            var touched = record != null ? new bool[candidates.Length] : null;
            // Thirty-two intensity bands and at most one bridge-ceiling split per
            // field. The guard also makes unusual saved field values harmless.
            for (int pass = 0; pass < candidates.Length * 35 && remaining > 1e-10; pass++)
            {
                int best = -1; double bestScore = 0, bestStep = 0, bestCostPerEffort = 0, bestGain = 0;
                for (int i = 0; i < candidates.Length; i++)
                {
                    var field = candidates[i];
                    if (!cached[i])
                    {
                        cached[i] = true;
                        NextStep(w, field, bridge, years, out scores[i], out steps[i], out costsPerEffort[i], out gains[i], cache);
                    }
                    double score = scores[i];
                    bool focused = field.Tile == camp.CultivationFocusTile;
                    if (score > 0 && (best < 0 || focused && candidates[best].Tile != camp.CultivationFocusTile ||
                        !focused && candidates[best].Tile != camp.CultivationFocusTile && score > bestScore + 1e-12))
                    { best = i; bestScore = score; bestStep = steps[i]; bestCostPerEffort = costsPerEffort[i]; bestGain = gains[i]; }
                }
                if (best < 0) break;
                double currentE = candidates[best].Capital;
                double usedE = unbounded ? bestStep
                    : c.Neolithic.CultivationMarginalEffortMultiplierPerTenth <= 1
                    ? Math.Min(bestStep, remaining / bestCostPerEffort)
                    : Math.Min(bestStep, Math.Max(0, CapitalForEffort(c,
                        EffortForCapital(c, currentE) + remaining / bestCostPerEffort) - currentE));
                if (usedE <= 1e-12) break;
                candidates[best].Capital = Math.Min(ceiling, currentE + usedE);
                double chargedEffort = c.Neolithic.CultivationMarginalEffortMultiplierPerTenth <= 1
                    ? usedE : AdditionalEffort(c, currentE, candidates[best].Capital);
                remaining = Math.Max(0, remaining - chargedEffort * bestCostPerEffort);
                if (record != null)
                {
                    touched[best] = true; double sum = 0;
                    for (int i = 0; i < candidates.Length; i++)
                        if (touched[i] || candidates[i].StartOperator == camp.Id) sum += Intensity(c, candidates[i].Capital);
                    double upkeep = MaintenanceMultiplier(c, w.Tiles[candidates[best].Tile], candidates[best].Ecology.CurrentVegetation);
                    record.Add(new ExpansionStep { Tile = candidates[best].Tile, Score = bestScore, WorkerYears = chargedEffort * bestCostPerEffort, AnnualGain = bestGain,
                        MaintenancePerYear = chargedEffort * n.MaintenanceWorkersPerTileE(c.MaintenanceCoefficient) * upkeep,
                        VillageAverage = villageLand > 0 ? sum / villageLand : 0 });
                }
                cached[best] = false;
                if (record != null && (recordedGain += bestGain) >= annualGainCap) break;
            }
            return remaining;
        }

        // The next capital step one field would take and its marginal score
        // (annualised retained food per worker-year, build effort and maintenance
        // charged): the single definition the allocator and the planner's
        // expansion ladder share. score is 0 when the field cannot step;
        // annualGain is the step's annualised retained food (for that score).
        private static void NextStep(WorldState w, HorizonCandidate field, bool bridge, double years,
            out double score, out double step, out double costPerEffort, out double annualGain, PlanningKernelCache cache = null)
        {
            score = 0; step = 0; costPerEffort = 0; annualGain = 0;
            var c = w.Config; var n = c.Neolithic;
            double ceiling = Math.Pow((bridge ? c.GathererBridgeCeiling : 1) / c.ReferenceIntensity, 2);
            double bridgeCeiling = Math.Pow(c.GathererBridgeCeiling / c.ReferenceIntensity, 2);
            double e = field.Capital;
            if (!(field.Eligible && e < ceiling - 1e-12)) return;
            double q = Intensity(c, e);
            double nextBandQ = Math.Min(bridge ? c.GathererBridgeCeiling : 1,
                (Math.Floor(q * 32 + 1e-10) + 1) / 32);
            double nextE = Math.Pow(nextBandQ / c.ReferenceIntensity, 2);
            double de = Math.Min(ceiling - e, Math.Max(0, nextE - e));
            if (!bridge && e < bridgeCeiling - 1e-12) de = Math.Min(de, bridgeCeiling - e);
            if (de <= 1e-12) return;
            var tile = w.Tiles[field.Tile];
            double build = bridge || q < c.GathererBridgeCeiling
                ? n.OpenBuild : BuildMultiplier(c, tile, field.Ecology.CurrentVegetation);
            double cost = n.BuildWorkerYearsPerTileE * build;
            if (cost <= 0) return;
            double maintenance = MaintenanceMultiplier(c, tile, field.Ecology.CurrentVegetation);
            if (cache != null) cache.Investments++;
            var investment = HorizonMarginalInvestment(w, tile, field.Ecology, e, de, years, cost, maintenance);
            score = Math.Max(0, investment.AnnualizedFoodPerWorkerYear);
            step = de; costPerEffort = cost; annualGain = investment.AnnualizedRetainedFoodGain;
        }

        internal static double HorizonMarginalScore(WorldState w, Tile tile, EcologyView opening,
            double capital, double step, double years, double costPerEffort, double maintenance)
            => Math.Max(0, HorizonMarginalInvestment(w, tile, opening, capital, step, years, costPerEffort, maintenance).AnnualizedFoodPerWorkerYear);

        internal static FoodInvestmentForecast HorizonMarginalInvestment(WorldState w, Tile tile, EcologyView opening,
            double capital, double step, double years, double costPerEffort, double maintenance)
        {
            var c = w.Config; var n = c.Neolithic;
            // Field investment is spread across the horizon for efficacy, while
            // its endpoint becomes authoritative only after current harvest.
            // Compare actual retained next-boundary food from the shared kernel;
            // gross managed flow that overfills the same pool has zero value.
            double chargedEffort = n.CultivationMarginalEffortMultiplierPerTenth <= 1
                ? step : AdditionalEffort(c, capital, capital + step);
            return FoodHorizonForecast.Investment(w, tile.Id, opening.Days,
                years * c.DaysPerYear, capital, capital + .5 * step,
                capital + step, 1, chargedEffort * costPerEffort,
                chargedEffort * n.PlanningHorizonYears *
                    n.MaintenanceWorkersPerTileE(c.MaintenanceCoefficient) * maintenance,
                opening);
        }

        // Ecology integration has already committed the preclaim endpoint. Only
        // the once-reconciled bridge difference and tool service are written here.
        // The mean efficacy that served claims remains the preclaim estimate.
        public static void CommitHorizonExpansion(WorldState w, Camp camp, HorizonCultivationAllocation plan,
            TurnRecord record, FarmToolWorkBudget toolBudget, HorizonCultivationAllocation preclaim = null)
        {
            if (plan == null || plan.CampId != camp.Id) throw new ArgumentException("Invalid horizon cultivation proposal.");
            if (preclaim != null && (preclaim.CampId != camp.Id || !plan.Tiles.SequenceEqual(preclaim.Tiles)))
                throw new ArgumentException("Bridge reconciliation changed the field set.");
            if (toolBudget != null)
            {
                if (toolBudget.HorizonCommitted) throw new InvalidOperationException("Horizon farm tool service was already committed.");
                double covered = toolBudget.ReservedMaintenanceWorkerYears + plan.CoveredFarmerWorkerYears;
                double actual = toolBudget.Consume(covered, w.Config.Neolithic.FarmToolFarmerYearsPerUnit);
                if (actual + 1e-8 < covered) throw new InvalidOperationException("Farm tool horizon budget changed after planning.");
                toolBudget.HorizonCommitted = true;
            }
            Dictionary<int, EcologyBalance> balances = null;
            for (int i = 0; i < plan.Tiles.Length; i++)
            {
                int id = plan.Tiles[i]; var tile = w.Tiles[id];
                double endpoint = Math.Max(0, plan.EndCapital[i]);
                if (tile.CultivationOperator >= 0 && tile.CultivationOperator != camp.Id &&
                    endpoint > plan.StartCapital[i] + 1e-12)
                    throw new InvalidOperationException("Horizon field ownership changed after claim preparation.");
                // Adjacent camps can share a footprint tile. When another camp
                // operates it and this camp neither operated it at the start nor
                // planned expansion on it, the operator's endpoint was integrated
                // and committed for that tile; this camp has nothing to write.
                if (tile.CultivationOperator >= 0 && tile.CultivationOperator != camp.Id &&
                    plan.StartOperators[i] != camp.Id && endpoint <= plan.StartCapital[i] + 1e-12)
                    continue;
                double beforeCapital = Math.Max(0, tile.CultivationCapital);
                if (preclaim != null && Math.Abs(beforeCapital - preclaim.EndCapital[i]) > 1e-7)
                    throw new InvalidOperationException("Horizon ecology did not integrate the planned field endpoint.");
                if (endpoint <= 0 && beforeCapital <= 0)
                {
                    if (plan.StartOperators[i] < 0 && tile.CultivationOperator == camp.Id) tile.CultivationOperator = -1;
                    tile.Cultivation = 0; tile.Cultivated = false;
                    continue;
                }
                if (Math.Abs(endpoint - beforeCapital) <= 1e-10)
                {
                    if (endpoint > 0 && tile.CultivationOperator < 0) tile.CultivationOperator = camp.Id;
                    tile.Cultivation = (float)Intensity(w.Config, endpoint); tile.Cultivated = endpoint > 0;
                    continue;
                }
                var before = new EcologyView(tile);
                if (endpoint > beforeCapital + 1e-12) tile.CultivationOperator = camp.Id;
                var after = before; after.CultivationCapital = endpoint;
                LazyEcology.ClampCapacity(w, tile, ref after);
                after.Commit(tile);
                double q = Intensity(w.Config, endpoint);
                tile.Cultivation = (float)q; tile.Cultivated = endpoint > 0;
                if (endpoint <= 0 && plan.StartOperators[i] < 0 && tile.CultivationOperator == camp.Id)
                    tile.CultivationOperator = -1;
                if (balances == null) balances = MaterialEconomy.GetOrCreateBalanceIndex(record);
                if (balances.TryGetValue(id, out var balance))
                {
                    balance.BiomassOverflow += before.PotentialFood - after.PotentialFood;
                    balance.FoodOverflow += before.Food - after.Food;
                    balance.EndingBiomass = after.PotentialFood;
                    balance.EndingFood = after.Food;
                }
                double displaced = EcologyMath.CultivationVegetationRetention(w.Config, Intensity(w.Config, beforeCapital)) -
                    EcologyMath.CultivationVegetationRetention(w.Config, q);
                if (displaced > 1e-12)
                {
                    double woodLoss = MaterialEconomy.ClampWoodSource(w, id, before, after);
                    record.Pressure.Add(new PressureEntry
                    {
                        Tile = id, Entity = camp.Id, Kind = "Cultivation vegetation displacement", Pool = "Vegetation cover",
                        Requested = displaced, Realized = displaced, WoodCapacityLoss = woodLoss, ConditionDamage = 0
                    });
                }
            }
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
                    double affordableE = n.CultivationMarginalEffortMultiplierPerTenth <= 1
                        ? remaining / (n.BuildWorkerYearsPerTileE * vb)
                        : Math.Max(0, CapitalForEffort(c, EffortForCapital(c, e) +
                            remaining / (n.BuildWorkerYearsPerTileE * vb)) - e);
                    double de = Math.Min(n.AllocationStepE, Math.Min(ceiling - e, affordableE));
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
                                EcologyMath.SustainableHarvestRate(beforeCapacity.Biomass, beforeCapacity.BiomassRate, c.SustainableProductionFraction, beforeCapacity.ManagedFlow) * c.LandSafeUseRatio);
                            double afterFlow = Math.Min(afterCapacity.Food * afterCapacity.FoodRate + afterCapacity.ManagedFlow,
                                EcologyMath.SustainableHarvestRate(afterCapacity.Biomass, afterCapacity.BiomassRate, c.SustainableProductionFraction, afterCapacity.ManagedFlow) * c.LandSafeUseRatio);
                            gain += (afterFlow - beforeFlow) * c.DaysPerYear / 4;
                        }
                        double addedEffort = n.CultivationMarginalEffortMultiplierPerTenth <= 1
                            ? de : AdditionalEffort(c, e, e + de);
                        double buildCost = addedEffort * n.BuildWorkerYearsPerTileE * vb;
                        score = gain / Math.Max(1e-12, buildCost + n.PlanningHorizonYears * n.MaintenanceWorkersPerTileE(c.MaintenanceCoefficient) * vm * addedEffort);
                        cache.Capital = e;
                        cache.LandCondition = ecology.LandCondition;
                        cache.Vegetation = ecology.CurrentVegetation;
                        cache.Step = de;
                        cache.Score = score;
                        cache.HasScore = true;
                    }
                    double candidateEffort = n.CultivationMarginalEffortMultiplierPerTenth <= 1
                        ? de : AdditionalEffort(c, e, e + de);
                    double candidateCost = candidateEffort * n.BuildWorkerYearsPerTileE * vb;
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
