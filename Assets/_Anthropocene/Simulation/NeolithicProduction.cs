using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    /// <summary>
    /// Shared conversions for food stocks whose target is a physical duration.
    /// Actual turn consumption still uses the captured TurnHorizon intervals.
    /// </summary>
    public static class PhysicalFoodTimebase
    {
        public static double DailyPerPerson(WorldState world)
            => world.Config.Consumption / world.Config.BaseDays;

        public static double StockForYears(WorldState world, double population, double years)
        {
            var config = world.Config;
            double referenceTurns = Math.Max(0, years) / config.Neolithic.ReferenceTurnYears;
            // Keep the established multiplication order at the shipped 20-year
            // settings so receipts remain numerically identical to the old rule.
            return StockForReferenceHorizon(world, population, referenceTurns);
        }

        public static double StockForReferenceHorizon(WorldState world, double population, double referenceTurns)
        {
            var config = world.Config;
            double referenceIntervals = config.Neolithic.ReferenceTurnYears * config.DaysPerYear / config.BaseDays;
            return Math.Max(0, population) * config.Consumption * referenceIntervals * Math.Max(0, referenceTurns);
        }

        public static double NaturalStorageYears(WorldState world, int tile)
            => TurnResolver.StorageGenerations(world, tile) * world.Config.Neolithic.ReferenceTurnYears;
    }

    public static class NeolithicTech
    {
        public const string Toolmaking = "Toolmaking", Pottery = "Pottery", Weaving = "Weaving", Carpentry = "Carpentry", Construction = "Construction";
        public const string KilnFiring = "KilnFiring", Plough = "Plough", Irrigation = "Irrigation", Sailing = "Sailing", WheelAndAxle = "WheelAndAxle", CopperWorking = "CopperWorking";
        public static readonly string[] FirstTier = { Toolmaking, Pottery, Weaving, Carpentry, Construction };
        public static readonly string[] SecondTier = { KilnFiring, Plough, Irrigation, Sailing };
        public static readonly string[] Convergence = { WheelAndAxle, CopperWorking };
    }
    // Append new values so existing saved/history enum ordinals remain stable.
    public enum CraftGood { HandTools, Pots, Cloth, FarmTools, Housing, Tools, AnimalFabric, LightHousing, HerdingTools, HousingMaterial }
    public sealed class CraftRecipe
    {
        public CraftGood Good;
        public string Specialist, Track;
        public double Rate, Stone, Clay, Wood, PlantFood, AnimalFood, AnimalFabric, ConstructionMaterial, HousingMaterial;
        public bool Equipment, SpecialistOnly, HusbandryOnly, AgricultureOnly;
    }
    [Serializable]
    public sealed class ProductionReceipt
    {
        public int Camp;
        public CraftGood Good;
        public string Role;
        public double Output, WorkerIntervals, Stone, Clay, Wood, PlantFood, EquipmentWorkerIntervals;
        [System.Runtime.Serialization.OptionalField] public double HandToolsConsumed, ToolsConsumed, CopperToolsConsumed;
        [System.Runtime.Serialization.OptionalField] public double AnimalFood, AnimalFabric;
        // Portable Housing Material consumed by a Housing assembly receipt.
        [System.Runtime.Serialization.OptionalField] public double HousingMaterial;
    }
    public sealed class ManufacturingFoodPlan
    {
        public double PlantFood, AnimalFood;
        public double Total => PlantFood + AnimalFood;
    }
    public sealed class ToolCoverageBudget
    {
        // Hand and regular tools are consumable output capacity.  Copper retains
        // the old worker-coverage hook until its own durability contract exists.
        private readonly double[] remaining, housingMultipliers, consumed;
        // Equipment outputs one Hand Tool or one Regular Tool covers.
        public const double OutputsPerHandTool = 1, OutputsPerTool = 4;
        public double Remaining => remaining[0] + remaining[1] + remaining[2];
        public double HandToolsConsumed => consumed[0];
        public double ToolsConsumed => consumed[1];
        public double CopperToolsConsumed => consumed[2];
        public ToolCoverageBudget(WorldState w, Camp c, double intervals, double limit = double.PositiveInfinity)
        {
            var n = w.Config.Neolithic;
            remaining = new[] { c.Inventory.HandTools, c.Inventory.Tools * OutputsPerTool, c.People.HasTech(NeolithicTech.CopperWorking) ? c.Inventory.CopperTools * intervals / n.ToolCoveragePerWorker : 0 }.ToArray();
            consumed = new double[3];
            // The old argument was a worker-time ceiling.  Physical inventory is
            // now the authoritative budget; callers keep the argument only for
            // source compatibility and it must not reintroduce worker-count wear.
            housingMultipliers = new[] { n.HandToolsHousingMultiplier, n.ImprovedToolsHousingMultiplier, n.CopperToolsHousingMultiplier };
        }
        public ToolCoverageBudget(WorldState w, Camp c, TurnHorizon horizon, double limit = double.PositiveInfinity)
            : this(w, c, horizon.ReferenceIntervals, limit) { }
        // Toolmaking is a non-equipment input to the same turn's aggregate
        // manufacturing balance. Credit only realized output; Spend later debits
        // the shared capacity ledger and the camp's physical inventory.
        public void Credit(CraftGood good, double output)
        {
            if (output <= 0) return;
            if (good == CraftGood.HandTools) remaining[0] += output;
            else if (good == CraftGood.Tools) remaining[1] += output * OutputsPerTool;
        }
        // Housing Material and the assembly steps share the Builder tool multiplier.
        internal static bool IsHousingWork(CraftGood good) => good == CraftGood.Housing || good == CraftGood.LightHousing || good == CraftGood.HousingMaterial;
        // Pure: the worker time Spend would charge for `output`, without debiting
        // tools. Housing Material takes the time its assembly step would take.
        public double Time(CraftGood good, double rate, double output)
        {
            bool housing = IsHousingWork(good);
            double used = 0; output = Math.Max(0, output);
            foreach (int grade in housing ? new[] { 2, 1, 0 } : new[] { 0, 1, 2 })
            {
                double multiplier = housing ? housingMultipliers[grade] : 1;
                if (multiplier <= 0 || output <= 0) continue;
                double covered = grade == 2 ? Math.Min(remaining[grade] * rate * multiplier, output) : Math.Min(remaining[grade], output);
                used += covered / (rate * multiplier); output -= covered;
            }
            return used;
        }
        public double Capacity(CraftGood good, double rate, double labor)
        {
            bool housing = IsHousingWork(good);
            double output = 0;
            foreach (int grade in housing ? new[] { 2, 1, 0 } : new[] { 0, 1, 2 })
            {
                double multiplier = housing ? housingMultipliers[grade] : 1;
                if (multiplier <= 0) continue;
                double time;
                if (grade == 2)
                {
                    time = Math.Min(remaining[grade], labor);
                    output += time * rate * multiplier;
                }
                else
                {
                    double amount = Math.Min(remaining[grade], labor * rate * multiplier);
                    output += amount;
                    time = amount <= 0 ? 0 : amount / (rate * multiplier);
                }
                labor -= time;
            }
            return output;
        }
        public double Spend(CraftGood good, double rate, double output)
        {
            bool housing = IsHousingWork(good);
            double used = 0;
            foreach (int grade in housing ? new[] { 2, 1, 0 } : new[] { 0, 1, 2 })
            {
                double multiplier = housing ? housingMultipliers[grade] : 1;
                if (multiplier <= 0) continue;
                if (grade == 2)
                {
                    double time = Math.Min(remaining[grade], Math.Max(0, output) / (rate * multiplier));
                    remaining[grade] = Math.Max(0, remaining[grade] - time); output -= time * rate * multiplier; used += time;
                }
                else
                {
                    double units = Math.Min(remaining[grade], Math.Max(0, output));
                    remaining[grade] = Math.Max(0, remaining[grade] - units); output -= units;
                    consumed[grade] += units / (grade == 0 ? OutputsPerHandTool : OutputsPerTool);
                    used += units / (rate * multiplier);
                }
            }
            return used;
        }
    }
    public sealed class HerdingManagementPlan
    {
        public int Herders;
        public double Animals, HerderYears, CoveredHerderYears, Capacity, Fulfillment, ToolsConsumed;
        public double ToolServiceDemandYears, ToolServiceSupplyYears, ToolCoverage;
    }
    public static class HerdingTooling
    {
        public static double DomesticAnimals(WorldState w, Camp c)
        {
            // Integer counts sum exactly in any order; this runs inside planner loops.
            int animals = 0;
            foreach (var h in w.Herds)
                if (h.DomesticCamp == c.Id && h.Animals > 0) animals = checked(animals + h.Animals);
            return animals;
        }
        public static int RequiredHerders(WorldState w, Camp c, double years)
        {
            double animals = DomesticAnimals(w, c);
            if (animals <= 0 || years <= 0) return 0;
            var n = w.Config.Neolithic;
            double availableCoveredHerders = Math.Max(0, c.Inventory.HerdingTools) * n.HerdingToolCoverageYearsPerUnit / years;
            double covered = Math.Min(availableCoveredHerders, animals / n.StoneHerdingToolAnimalsPerHerder);
            double requiredHerders = covered + Math.Max(0, animals - covered * n.StoneHerdingToolAnimalsPerHerder) / Math.Max(1e-12, w.Config.AnimalsPerHerder);
            return Math.Min(c.Population, Math.Max(0, (int)Math.Ceiling(requiredHerders - 1e-10)));
        }
        public static int RequiredHerders(WorldState w, Camp c, TurnHorizon horizon)
            => RequiredHerders(w, c, horizon.Years);
        public static HerdingManagementPlan Plan(WorldState w, Camp c, TurnHorizon horizon, int herders = -1)
            => Plan(w, c, horizon.Years, herders);
        public static HerdingManagementPlan Plan(WorldState w, Camp c, double years, int herders = -1)
        {
            var n = w.Config.Neolithic;
            int assigned = herders < 0 ? c.Workforce.Assignments.Where(a => a.Role == WorkforceRoles.Herder).Sum(a => a.Population) : herders;
            double animals = DomesticAnimals(w, c), herderYears = Math.Max(0, assigned * years);
            // A tool provides a fixed number of herder-years over the whole
            // horizon. The ratio is average coverage, never a breakage date.
            double serviceDemand = Math.Min(Math.Max(0, assigned), animals / n.StoneHerdingToolAnimalsPerHerder) * Math.Max(0, years);
            double serviceSupply = Math.Max(0, c.Inventory.HerdingTools) * n.HerdingToolCoverageYearsPerUnit;
            double coveredHerderYears = Math.Min(serviceDemand, serviceSupply);
            if (years <= 0) return new HerdingManagementPlan
            {
                Herders = assigned, Animals = animals, Capacity = animals, Fulfillment = 1,
                ToolServiceSupplyYears = serviceSupply, ToolCoverage = 1
            };
            double coveredHerders = coveredHerderYears / years;
            double uncoveredHerders = Math.Min(Math.Max(0, assigned - coveredHerders), Math.Max(0, animals - coveredHerders * n.StoneHerdingToolAnimalsPerHerder) / Math.Max(1e-12, w.Config.AnimalsPerHerder));
            double capacity = coveredHerders * n.StoneHerdingToolAnimalsPerHerder + uncoveredHerders * w.Config.AnimalsPerHerder;
            return new HerdingManagementPlan
            {
                Herders = assigned, Animals = animals, HerderYears = herderYears, CoveredHerderYears = coveredHerderYears,
                Capacity = capacity, Fulfillment = animals <= 0 ? 1 : Numbers.Clamp(capacity / animals, 0, 1),
                ToolsConsumed = coveredHerderYears / n.HerdingToolCoverageYearsPerUnit,
                ToolServiceDemandYears = serviceDemand, ToolServiceSupplyYears = serviceSupply,
                ToolCoverage = serviceDemand <= 0 ? 1 : coveredHerderYears / serviceDemand
            };
        }
    }
    public static class NeolithicProduction
    {
        public static CraftRecipe[] Recipes(SimulationConfig config)
        {
            var n = config.Neolithic;
            return new[] {
                new CraftRecipe { Good = CraftGood.HandTools, Specialist = WorkforceRoles.Toolmaker, Track = NeolithicTech.Toolmaking, Rate = n.HandToolsOutput, Stone = n.StonePerHandTool },
                new CraftRecipe { Good = CraftGood.Pots, Specialist = WorkforceRoles.Potter, Track = NeolithicTech.Pottery, Rate = n.PotsOutput, Clay = n.ClayPerPot, Equipment = true },
                new CraftRecipe { Good = CraftGood.Cloth, Specialist = WorkforceRoles.Weaver, Track = NeolithicTech.Weaving, Rate = n.WeaverFabricOutputPerBaseDays, PlantFood = n.PlantFoodPerFabricUnit, Equipment = true, AgricultureOnly = true },
                new CraftRecipe { Good = CraftGood.FarmTools, Specialist = WorkforceRoles.Carpenter, Track = NeolithicTech.Carpentry, Rate = n.FarmToolsOutput, Wood = n.WoodPerFarmTool, Equipment = true, AgricultureOnly = true },
                // Housing is split into two Builder steps at twice today's rate each, so
                // labour, tool wear (assembly only) and material per Housing are unchanged.
                new CraftRecipe { Good = CraftGood.Housing, Specialist = WorkforceRoles.Builder, Track = NeolithicTech.Construction, Rate = 2 * n.BuilderHousingOutputPerBaseDays, HousingMaterial = 1, Equipment = true, AgricultureOnly = true },
                new CraftRecipe { Good = CraftGood.Tools, Specialist = WorkforceRoles.Toolmaker, Track = NeolithicTech.Toolmaking, Rate = n.ToolsOutput, Stone = n.StonePerTool, Wood = n.WoodPerTool, SpecialistOnly = true },
                new CraftRecipe { Good = CraftGood.AnimalFabric, Specialist = WorkforceRoles.Weaver, Track = NeolithicTech.Weaving, Rate = n.WeaverFabricOutputPerBaseDays, AnimalFood = 1, Equipment = true, HusbandryOnly = true },
                new CraftRecipe { Good = CraftGood.LightHousing, Specialist = WorkforceRoles.Builder, Track = NeolithicTech.Construction, Rate = n.BuilderHousingOutputPerBaseDays, AnimalFabric = 1, Equipment = true, HusbandryOnly = true },
                new CraftRecipe { Good = CraftGood.HerdingTools, Specialist = WorkforceRoles.Carpenter, Track = NeolithicTech.Carpentry, Rate = n.FarmToolsOutput, Wood = 1, Stone = 1, Equipment = true, HusbandryOnly = true },
                new CraftRecipe { Good = CraftGood.HousingMaterial, Specialist = WorkforceRoles.Builder, Track = NeolithicTech.Construction, Rate = 2 * n.BuilderHousingOutputPerBaseDays, ConstructionMaterial = n.HousingMaterialUnitsPerHousing, AgricultureOnly = true }
            };
        }
        public static double Stock(Camp c, CraftGood good) => good == CraftGood.HandTools ? c.Inventory.HandTools : good == CraftGood.Pots ? c.Inventory.Pots : good == CraftGood.Cloth ? c.Inventory.Cloth : good == CraftGood.FarmTools ? c.Inventory.FarmTools : good == CraftGood.Tools ? c.Inventory.Tools : good == CraftGood.AnimalFabric ? c.Inventory.AnimalFabric : good == CraftGood.LightHousing ? c.Inventory.LightHousingUnits : good == CraftGood.HerdingTools ? c.Inventory.HerdingTools : good == CraftGood.HousingMaterial ? c.Inventory.HousingMaterial : c.Inventory.HousingUnits;
        public static double Priority(Camp c, CraftGood good) => good == CraftGood.HandTools ? c.CraftingPriorities.HandTools : good == CraftGood.Pots ? c.CraftingPriorities.Pots : good == CraftGood.Cloth ? c.CraftingPriorities.Cloth : good == CraftGood.FarmTools ? c.CraftingPriorities.FarmTools : good == CraftGood.Tools ? c.CraftingPriorities.Tools : good == CraftGood.AnimalFabric ? c.CraftingPriorities.AnimalFabric : good == CraftGood.LightHousing ? c.CraftingPriorities.LightHousing : good == CraftGood.HerdingTools ? c.CraftingPriorities.HerdingTools : c.CraftingPriorities.Housing; // HousingMaterial shares Housing's priority.
        // Schedule position: Housing Material sits immediately before the Housing assembly that consumes it.
        internal static double ScheduleKey(CraftGood good) => good == CraftGood.HousingMaterial ? (int)CraftGood.Housing - .5 : (int)good;
        // One Builder pool makes Housing Material and assembles it in the same turn.
        // Largest material amount m <= demand whose making plus the assembly of
        // min(m + stock, tool capacity) fits in `labor`. Both steps have the same
        // rate and tool multiplier, so with no stock this is half of today's
        // single-step Housing capacity. Pure and deterministic.
        internal static double HousingMaterialLimit(ToolCoverageBudget coverage, double rate, double labor, double demand, double stock)
        {
            if (!(demand > 0) || !(labor > 0) || !(rate > 0)) return 0;
            double ceiling = coverage.Capacity(CraftGood.Housing, rate, double.PositiveInfinity);
            stock = Math.Max(0, stock);
            double hi = Math.Min(demand, ceiling - stock);
            if (!(hi > 0)) return 0;
            if (stock <= 0) return Math.Min(hi, coverage.Capacity(CraftGood.Housing, rate, labor / 2));
            double Need(double m) => coverage.Time(CraftGood.Housing, rate, m) + coverage.Time(CraftGood.Housing, rate, Math.Min(m + stock, ceiling));
            if (Need(hi) <= labor) return hi;
            double lo = 0;
            for (int i = 0; i < 48; i++) { double mid = (lo + hi) / 2; if (Need(mid) <= labor) lo = mid; else hi = mid; }
            return lo;
        }
        // The same limit for schedules that use a flat rate (no tool-grade waterfall).
        internal static double FlatHousingMaterialLimit(double rate, double labor, double demand, double stock)
            => Math.Max(0, Math.Min(demand, (labor * rate - Math.Max(0, stock)) / 2));
        public static double FixedHousingAt(WorldState w, int campId, int tile, double days)
        {
            double units = (w.FixedHousing ?? new List<FixedHousingStock>()).Where(s => s.CampId == campId && s.Tile == tile)
                .Sum(s => ProjectFixedHousing(w, s, days));
            var legacy = w.Camps.FirstOrDefault(c => c.Id == campId);
            if (legacy != null && legacy.Tile == tile && legacy.Inventory != null && legacy.Inventory.HousingUnits > 0 &&
                !(w.FixedHousing ?? new List<FixedHousingStock>()).Any(s => s.CampId == campId && s.Tile == tile))
                units += legacy.Inventory.HousingUnits * Math.Pow(1 - w.Config.Neolithic.HousingDecayPerTurn,
                    Math.Max(0, days - w.Days) / (w.Config.DaysPerYear * w.Config.Neolithic.ReferenceTurnYears));
            return units;
        }
        private static double ProjectFixedHousing(WorldState w, FixedHousingStock stock, double days)
        {
            var n = w.Config.Neolithic;
            double elapsedYears = Math.Max(0, days - stock.LastUpdatedDays) / w.Config.DaysPerYear;
            return Math.Max(0, stock.Units) * Math.Pow(1 - n.HousingDecayPerTurn, elapsedYears / n.ReferenceTurnYears);
        }
        public static double HousingSupply(WorldState w, Camp c, double days)
            => FixedHousingAt(w, c.Id, c.Tile, days) + Math.Max(0, c.Inventory.LightHousingUnits);
        public static void MigrateLegacyFixedHousing(WorldState w, Camp c)
        {
            w.FixedHousing ??= new List<FixedHousingStock>();
            if (c.Inventory == null) c.Inventory = new CampInventory();
            if (c.Inventory.HousingUnits <= 0) return;
            var stock = w.FixedHousing.FirstOrDefault(s => s.CampId == c.Id && s.Tile == c.Tile);
            if (stock == null) w.FixedHousing.Add(new FixedHousingStock { CampId = c.Id, Tile = c.Tile, Units = c.Inventory.HousingUnits, LastUpdatedDays = w.Days });
            else stock.Units += c.Inventory.HousingUnits;
            c.Inventory.HousingUnits = 0;
        }
        public static void AddFixedHousing(WorldState w, Camp c, double amount, double atDays)
        {
            if (amount <= 0) return;
            MigrateLegacyFixedHousing(w, c);
            w.FixedHousing ??= new List<FixedHousingStock>();
            var stock = w.FixedHousing.FirstOrDefault(s => s.CampId == c.Id && s.Tile == c.Tile);
            if (stock == null) w.FixedHousing.Add(new FixedHousingStock { CampId = c.Id, Tile = c.Tile, Units = amount, LastUpdatedDays = atDays });
            else { stock.Units = ProjectFixedHousing(w, stock, atDays) + amount; stock.LastUpdatedDays = atDays; }
        }
        public static void NormalizeFixedHousing(WorldState w)
        {
            w.FixedHousing ??= new List<FixedHousingStock>();
            if (w.FixedHousing.Any(s => s == null || s.CampId < 0 || !w.Camps.Any(c => c.Id == s.CampId) || s.Tile < 0 || w.Tiles == null || s.Tile >= w.Tiles.Length ||
                double.IsNaN(s.Units) || double.IsInfinity(s.Units) || s.Units < 0 || double.IsNaN(s.LastUpdatedDays) || double.IsInfinity(s.LastUpdatedDays) || s.LastUpdatedDays < 0 || s.LastUpdatedDays > w.Days + 1e-8))
                throw new InvalidOperationException("Invalid fixed housing stock.");
            if (w.FixedHousing.GroupBy(s => (s.CampId, s.Tile)).Any(g => g.Count() > 1))
                throw new InvalidOperationException("Duplicate fixed housing stock for a camp site.");
            w.FixedHousing = w.FixedHousing.OrderBy(s => s.CampId).ThenBy(s => s.Tile).ToList();
        }
        private static bool HusbandryOnly(CraftGood good) => good == CraftGood.AnimalFabric || good == CraftGood.LightHousing || good == CraftGood.HerdingTools;
        private static bool AgricultureOnly(CraftGood good) => good == CraftGood.Cloth || good == CraftGood.FarmTools || good == CraftGood.Housing || good == CraftGood.HousingMaterial;
        // Compatibility wrapper for callers that resolve both agricultural work
        // and pot depreciation before forecasting production.
        public static double Demand(WorldState w, Camp c, CraftGood good, double years, double residentPopulation = -1, bool postAgriculturalResolution = false)
            => Demand(w, c, good, years, residentPopulation, postAgriculturalResolution, postAgriculturalResolution);

        // Field work is resolved before growth planning, while Pots depreciate
        // later in ResolveCampNeolithic. Keep those chronology boundaries separate.
        public static double Demand(WorldState w, Camp c, CraftGood good, double years, double residentPopulation, bool fieldWorkAlreadyResolved, bool potsAlreadyDecayed, KnownMaterialSource[] knownSources = null, ProductionOrder order = null)
        {
            var n = w.Config.Neolithic;
            double residents = residentPopulation < 0 ? c.Population : residentPopulation;
            // Housing Material is made only against the Housing gap, never stockpiled beyond it.
            if (good == CraftGood.HousingMaterial)
                return Math.Max(0, Demand(w, c, CraftGood.Housing, years, residentPopulation, fieldWorkAlreadyResolved, potsAlreadyDecayed, knownSources, order) - Math.Max(0, c.Inventory.HousingMaterial));
            if (good == CraftGood.HandTools || good == CraftGood.Tools)
            {
                var tools = ToolReplacementDemands(w, c, years, residents, fieldWorkAlreadyResolved, potsAlreadyDecayed, knownSources, order);
                return good == CraftGood.HandTools ? tools.HandTools : tools.Tools;
            }
            if (good == CraftGood.Pots)
                return potsAlreadyDecayed ? PotDemandFromCurrentStock(w, c, years, residents) : PotDemand(w, c, years, residents);
            double clothingTarget = residents * n.FabricConsumptionPerPersonTurn * ((years + n.UsefulFinishedGoodsReserveYears) / n.ReferenceTurnYears);
            double clothGap = Math.Max(0, clothingTarget - Math.Max(0, c.Inventory.Cloth) - Math.Max(0, c.Inventory.AnimalFabric));
            double lightHousingTarget = Math.Max(0, residents / n.HousingPeoplePerUnit - HousingSupply(w, c, w.Days + years * w.Config.DaysPerYear));
            bool lightHousingUseful = Priority(c, CraftGood.LightHousing) > 0 && CultivationEconomy.Workers(c, WorkforceRoles.Builder) + CultivationEconomy.Workers(c, WorkforceRoles.Craftsperson) > 0;
            double target = good == CraftGood.Cloth ? Math.Max(0, clothingTarget - Math.Max(0, c.Inventory.AnimalFabric)) :
                good == CraftGood.AnimalFabric ? Math.Max(0, clothingTarget - Math.Max(0, c.Inventory.Cloth)) + (lightHousingUseful ? lightHousingTarget : 0) :
                good == CraftGood.Housing || good == CraftGood.LightHousing ? residents / n.HousingPeoplePerUnit :
                good == CraftGood.FarmTools ? FarmToolDemand(w, c, years, fieldWorkAlreadyResolved) + Stock(c, good) :
                good == CraftGood.HerdingTools ? HerdingToolDemand(w, c, years, fieldWorkAlreadyResolved) + Stock(c, good) : c.FarmToolsTarget;
            if (good == CraftGood.Housing) return Math.Max(0, target - HousingSupply(w, c, w.Days + years * w.Config.DaysPerYear));
            if (good == CraftGood.LightHousing) return Math.Max(0, target - HousingSupply(w, c, w.Days + years * w.Config.DaysPerYear));
            if (good == CraftGood.AnimalFabric) return Math.Max(0, target - Math.Max(0, c.Inventory.AnimalFabric));
            return Math.Max(0, target - Stock(c, good));
        }
        private static double HerdingToolDemand(WorldState w, Camp c, double years, bool managementAlreadyResolved)
        {
            double animals = HerdingTooling.DomesticAnimals(w, c);
            if (animals <= 0) return 0;
            double stock = Math.Max(0, c.Inventory.HerdingTools);
            var n = w.Config.Neolithic;
            double assigned = Math.Max(0, c.Workforce.Assignments.Where(a => a.Role == WorkforceRoles.Herder).Sum(a => a.Population));
            double usefulNextPeriod = Math.Min(assigned, animals / Math.Max(1e-12, n.StoneHerdingToolAnimalsPerHerder)) * Math.Max(0, years) / n.HerdingToolCoverageYearsPerUnit;
            double expectedUse = managementAlreadyResolved ? 0 : HerdingTooling.Plan(w, c, years).ToolsConsumed;
            double availableAfterUse = Math.Max(0, stock - expectedUse);
            return Math.Max(0, usefulNextPeriod - availableAfterUse);
        }
        public static double FarmToolDemand(WorldState w, Camp c, double years, bool fieldWorkAlreadyResolved = false)
        {
            double forecast = CultivationEconomy.ForecastFarmToolWorkerYears(w, c, years) / w.Config.Neolithic.FarmToolFarmerYearsPerUnit;
            double stock = Math.Max(0, c.Inventory.FarmTools);
            if (fieldWorkAlreadyResolved) return Math.Max(0, forecast - stock);
            double expectedUse = Math.Min(stock, forecast);
            return Math.Max(0, forecast - Math.Max(0, stock - expectedUse));
        }
        // Hand and Regular Tool replacement share one equipment schedule; one run
        // answers both goods for the same camp state.
        internal static (double HandTools, double Tools) ToolReplacementDemands(WorldState w, Camp c, double years, double residents, bool fieldWorkAlreadyResolved, bool potsAlreadyDecayed, KnownMaterialSource[] knownSources, ProductionOrder order = null)
        {
            var n = w.Config.Neolithic;
            // Both schedule passes read the same unmodified camp, so each recipe's
            // pure useful demand is evaluated at most once per call.
            var baseDemand = new Dictionary<CraftGood, double>();
            double BaseDemand(CraftGood good)
            {
                if (!baseDemand.TryGetValue(good, out double value))
                    baseDemand[good] = value = DemandWithoutToolReplacement(w, c, good, years, residents, fieldWorkAlreadyResolved, potsAlreadyDecayed);
                return value;
            }
            var recipes = ProductionOrder.Sort(Recipes(w.Config).Where(r => (r.Equipment || r.Good == CraftGood.HousingMaterial) && (!r.AgricultureOnly || CultivationEconomy.Enabled(c)) && (!r.HusbandryOnly || CultivationEconomy.HusbandryEnabled(c))),
                good => Priority(c, good), order);
            // Under the Value order every recipe claims coverage and material in value
            // order across roles; otherwise the roles run in turn.
            var steps = ProductionOrder.Steps(recipes, order != null).ToArray();
            double MaterialPriority(MaterialId material) => material == MaterialId.Stone ? c.StonePriority : material == MaterialId.Clay ? c.ClayPriority : c.WoodPriority;
            var sources = knownSources ?? MaterialEconomy.KnownSources(w, c, w.Days);
            var room = MaterialEconomy.Materials.ToDictionary(m => m, m => MaterialEconomy.Inventory(c, m) +
                (MaterialPriority(m) > 0 && n.SpecialistMaterialExtractionRate > 0 ? sources.Where(s => s.Material == m).Sum(s => s.Available) : 0));
            double plant = c.PlantFood, animalFood = c.AnimalFood, animalFabric = c.Inventory.AnimalFabric, housingMaterial = Math.Max(0, c.Inventory.HousingMaterial), useful = 0, copperOutput = 0;
            double generalistUseful = 0, generalistEffort = 0;
            double referenceIntervals = IntervalsForYears(w, years);
            double copperLabor = c.People.HasTech(NeolithicTech.CopperWorking) ? c.Inventory.CopperTools * referenceIntervals / n.ToolCoveragePerWorker : 0;
            var labor = WorkforceRoles.Specialists.Concat(new[] { WorkforceRoles.Craftsperson }).ToDictionary(role => role, role => c.Workforce.Assignments.Where(a => a.Role == role).Sum(a => a.Population) * referenceIntervals);
            // Automation persists EquipmentTarget as the number of workers
            // reserved for the next feasible equipment plan.  A depleted camp
            // may temporarily have only Toolmakers assigned, so retain that
            // reservation as prospective Craftsperson labor while sizing the
            // replacement budget.  It is a worker count, never tool capacity.
            if (c.EconomicLabor != null && c.EconomicLabor.EquipmentTarget > 0)
            {
                var equipmentRoles = new HashSet<string>(recipes.Select(r => r.Specialist).Where(role => c.People.HasTech(EconomicIntents.RequiredTechnology(role))));
                equipmentRoles.Add(WorkforceRoles.Craftsperson);
                double assignedEquipmentWorkers = c.Workforce.Assignments.Where(a => equipmentRoles.Contains(a.Role)).Sum(a => a.Population);
                double missingEquipmentWorkers = Math.Max(0, Math.Min(c.Population, c.EconomicLabor.EquipmentTarget) - assignedEquipmentWorkers);
                labor[WorkforceRoles.Craftsperson] += missingEquipmentWorkers * referenceIntervals;
            }
            // First find the direct work the normal priority-ordered workforce
            // schedule can actually use.  This is a planning pass only: it does not
            // reorder workers.  The plan built from it gives Housing Material its
            // ordinary place in the material-access order (direct work ranked above
            // it keeps its inputs; work ranked below is only softly reserved and
            // must leave Housing Material's claim), then the same schedule runs
            // with that plan, as the resolver does.
            var probeRoom = new Dictionary<MaterialId, double>(room);
            var probePlant = plant;
            var probeAnimalFood = animalFood;
            var probeAnimalFabric = animalFabric;
            var probeHousingMaterial = housingMaterial;
            var probePlanned = recipes.ToDictionary(r => r.Good, r => 0d);
            var probeLabor = labor.ToDictionary(p => p.Key, p => p.Value);
            foreach (var (role, recipe) in steps)
            {
                if (!probeLabor.TryGetValue(role, out double availableLabor)) continue;
                if (Priority(c, recipe.Good) <= 0) continue;
                bool specialist = c.People.HasTech(recipe.Track);
                if (role != WorkforceRoles.Craftsperson && (!specialist || role != recipe.Specialist)) continue;
                if (role == WorkforceRoles.Craftsperson && recipe.SpecialistOnly) continue;
                double rate = recipe.Rate * (role == recipe.Specialist && specialist ? 1 : n.GeneralistTaskEfficiency);
                double wanted = Math.Max(0, BaseDemand(recipe.Good) - probePlanned[recipe.Good]);
                double amount = Math.Min(wanted, availableLabor * rate);
                if (recipe.Stone > 0) amount = Math.Min(amount, probeRoom[MaterialId.Stone] / recipe.Stone);
                if (recipe.Clay > 0) amount = Math.Min(amount, probeRoom[MaterialId.Clay] / recipe.Clay);
                if (recipe.Wood > 0) amount = Math.Min(amount, probeRoom[MaterialId.Wood] / recipe.Wood);
                if (recipe.PlantFood > 0) amount = Math.Min(amount, probePlant / recipe.PlantFood);
                if (recipe.AnimalFood > 0) amount = Math.Min(amount, probeAnimalFood / recipe.AnimalFood);
                if (recipe.AnimalFabric > 0) amount = Math.Min(amount, probeAnimalFabric / recipe.AnimalFabric);
                if (recipe.HousingMaterial > 0) amount = Math.Min(amount, probeHousingMaterial / recipe.HousingMaterial);
                if (recipe.Good == CraftGood.HousingMaterial) amount = Math.Min(wanted, FlatHousingMaterialLimit(rate, availableLabor, wanted, probeHousingMaterial));
                if (amount <= 1e-12) continue;
                availableLabor -= amount / rate;
                probeLabor[role] = availableLabor;
                probePlanned[recipe.Good] += amount;
                probeRoom[MaterialId.Stone] -= amount * recipe.Stone;
                probeRoom[MaterialId.Clay] -= amount * recipe.Clay;
                probeRoom[MaterialId.Wood] -= amount * recipe.Wood;
                probePlant -= amount * recipe.PlantFood;
                probeAnimalFood -= amount * recipe.AnimalFood;
                probeAnimalFabric += recipe.Good == CraftGood.AnimalFabric ? amount : 0;
                probeAnimalFabric -= amount * recipe.AnimalFabric;
                probeHousingMaterial += recipe.Good == CraftGood.HousingMaterial ? amount : 0;
                probeHousingMaterial -= amount * recipe.HousingMaterial;
            }

            var planned = recipes.ToDictionary(r => r.Good, r => 0d);
            double housingWoodDrawn = 0;
            var housingPlan = MaterialEconomy.BuildHousingPlan(recipes, good => Priority(c, good), probePlanned, room[MaterialId.Stone], room[MaterialId.Clay], room[MaterialId.Wood], order);
            foreach (var (role, recipe) in steps)
            {
                if (!labor.TryGetValue(role, out double availableLabor)) continue;
                if (Priority(c, recipe.Good) <= 0) continue;
                bool specialist = c.People.HasTech(recipe.Track);
                if (role != WorkforceRoles.Craftsperson && (!specialist || role != recipe.Specialist)) continue;
                if (role == WorkforceRoles.Craftsperson && recipe.SpecialistOnly) continue;
                double rate = recipe.Rate * (role == recipe.Specialist && specialist ? 1 : n.GeneralistTaskEfficiency);
                double wanted = Math.Max(0, BaseDemand(recipe.Good) - planned[recipe.Good]);
                double amount = Math.Min(wanted, availableLabor * rate);
                if (recipe.Stone > 0) amount = Math.Min(amount, room[MaterialId.Stone] / recipe.Stone);
                if (recipe.Clay > 0) amount = Math.Min(amount, room[MaterialId.Clay] / recipe.Clay);
                if (recipe.Wood > 0) amount = Math.Min(amount, room[MaterialId.Wood] / recipe.Wood);
                if (recipe.ConstructionMaterial <= 0 && (recipe.Wood > 0 || recipe.Clay > 0))
                    amount = housingPlan.LimitDirect(room[MaterialId.Wood], room[MaterialId.Clay], recipe, amount);
                if (recipe.ConstructionMaterial > 0)
                    amount = housingPlan.LimitHousing(room[MaterialId.Wood], room[MaterialId.Clay], amount, recipe.ConstructionMaterial, out _, out _);
                if (recipe.PlantFood > 0) amount = Math.Min(amount, plant / recipe.PlantFood);
                if (recipe.AnimalFood > 0) amount = Math.Min(amount, animalFood / recipe.AnimalFood);
                if (recipe.AnimalFabric > 0) amount = Math.Min(amount, animalFabric / recipe.AnimalFabric);
                if (recipe.HousingMaterial > 0) amount = Math.Min(amount, housingMaterial / recipe.HousingMaterial);
                if (recipe.Good == CraftGood.HousingMaterial) amount = Math.Min(amount, FlatHousingMaterialLimit(rate, availableLabor, wanted, housingMaterial));
                if (amount <= 1e-12) { housingPlan.Settle(recipe, role); continue; }
                availableLabor -= amount / rate;
                labor[role] = availableLabor;
                planned[recipe.Good] += amount;
                // Material making is labour, not equipment output: it adds no
                // useful output but is part of the generalist's effort.
                if (recipe.Equipment) useful += amount;
                if (role == WorkforceRoles.Craftsperson)
                {
                    if (recipe.Equipment) generalistUseful += amount;
                    generalistEffort += amount / rate;
                }
                if (recipe.Equipment)
                {
                    double copperRate = rate * (recipe.Good == CraftGood.Housing || recipe.Good == CraftGood.LightHousing ? n.CopperToolsHousingMultiplier : 1);
                    double copperAmount = Math.Min(amount, copperLabor * copperRate);
                    copperOutput += copperAmount;
                    copperLabor -= copperRate > 0 ? copperAmount / copperRate : 0;
                }
                room[MaterialId.Stone] -= amount * recipe.Stone; room[MaterialId.Clay] -= amount * recipe.Clay; room[MaterialId.Wood] -= amount * recipe.Wood;
                if (recipe.ConstructionMaterial > 0)
                {
                    housingPlan.LimitHousing(room[MaterialId.Wood], room[MaterialId.Clay], amount, recipe.ConstructionMaterial, out double wood, out double clay);
                    room[MaterialId.Wood] -= wood; room[MaterialId.Clay] -= clay; housingWoodDrawn += wood;
                }
                housingPlan.CompleteDirect(recipe, amount);
                plant -= amount * recipe.PlantFood;
                animalFood -= amount * recipe.AnimalFood;
                animalFabric += recipe.Good == CraftGood.AnimalFabric ? amount : 0;
                animalFabric -= amount * recipe.AnimalFabric;
                housingMaterial += recipe.Good == CraftGood.HousingMaterial ? amount : 0;
                housingMaterial -= amount * recipe.HousingMaterial;
                housingPlan.Settle(recipe, role);
            }
            double capacity = c.Inventory.HandTools + c.Inventory.Tools * ToolCoverageBudget.OutputsPerTool;
            // Copper remains the legacy worker-time hook. Its contribution above
            // follows each useful recipe's actual rate and Housing multiplier;
            // there is no Copper output-capacity multiplier or wear.
            capacity += copperOutput;
            double deficit = Math.Max(0, useful - capacity);
            var regularRecipe = Recipes(w.Config).Single(r => r.Good == CraftGood.Tools);
            // Regular Tools hold their place in the same access order: Wood that
            // Housing Material drew is unavailable to them only if it outranks them.
            bool toolsOutrankHousing = order != null ? order.Outranks(CraftGood.Tools, CraftGood.HousingMaterial) :
                Priority(c, CraftGood.Tools) > Priority(c, CraftGood.HousingMaterial) ||
                Priority(c, CraftGood.Tools) == Priority(c, CraftGood.HousingMaterial) && ScheduleKey(CraftGood.Tools) < ScheduleKey(CraftGood.HousingMaterial);
            double toolWoodRoom = room[MaterialId.Wood] + (toolsOutrankHousing ? housingWoodDrawn : 0);
            bool regularFeasible = c.People.HasTech(NeolithicTech.Toolmaking) && c.CraftingPriorities.Tools > 0 &&
                (regularRecipe.Stone <= 0 || room[MaterialId.Stone] > 1e-12) &&
                (regularRecipe.Wood <= 0 || toolWoodRoom > 1e-12);
            // Feasible Regular Tools carry the whole deficit; otherwise Hand Tools do.
            if (regularFeasible) return (0, deficit / ToolCoverageBudget.OutputsPerTool);
            double specialistSupply = c.People.HasTech(NeolithicTech.Toolmaking)
                ? CultivationEconomy.Workers(c, WorkforceRoles.Toolmaker) * referenceIntervals * n.HandToolsOutput : 0;
            double specialistOutput = Math.Min(deficit, specialistSupply);
            double generalistRate = n.HandToolsOutput * n.GeneralistTaskEfficiency;
            double competingRate = generalistEffort <= 0 ? 0 : generalistUseful / generalistEffort;
            double spareGeneralistLabor = labor.TryGetValue(WorkforceRoles.Craftsperson, out double remainingLabor)
                ? Math.Max(0, remainingLabor) : 0;
            // When one Craftsperson allocation makes both tools and goods,
            // use spare labor first. Only further tool output displaces
            // equipment output. Solve that one linear service balance.
            double residual = Math.Max(0, deficit - specialistOutput);
            double freeOutput = Math.Min(residual, spareGeneralistLabor * generalistRate);
            double generalistOutput = freeOutput + (residual - freeOutput) /
                (1 + competingRate / Math.Max(1e-12, generalistRate));
            return (specialistOutput + generalistOutput, 0);
        }
        private static double DemandWithoutToolReplacement(WorldState w, Camp c, CraftGood good, double years, double residents, bool fieldWorkAlreadyResolved, bool potsAlreadyDecayed)
        {
            if (good == CraftGood.FarmTools) return Math.Max(0, FarmToolDemand(w, c, years, fieldWorkAlreadyResolved));
            return Demand(w, c, good, years, residents, fieldWorkAlreadyResolved, potsAlreadyDecayed);
        }
        private static double IntervalsForYears(WorldState w, double years) => Math.Max(0, years) * w.Config.DaysPerYear / w.Config.BaseDays;
        // `intervals` remains in the signature for existing callers; the stored
        // capacity is fixed by site and physical storage years, never this turn.
        public static double NaturalStorageCapacity(WorldState w, Camp c, double intervals, double residents = -1)
        {
            double population = NaturalStoragePopulation(w, c, residents);
            return PhysicalFoodTimebase.StockForReferenceHorizon(w, population, TurnResolver.StorageGenerations(w, c.Tile));
        }
        public static double NaturalStoragePopulation(WorldState w, Camp c, double residents = -1)
        {
            double projectedDelta = residents < 0 ? 0 : residents - c.Population;
            return Math.Max(0, w.LinkedPopulation(c) + projectedDelta);
        }
        public static double ReservePopulation(WorldState w, Camp c, double residents = -1)
            => Math.Max(0, (residents < 0 ? c.Population : residents) + w.Hunters.Where(h => h.SourceCamp == c.Id && Supply.Connected(w, h)).Sum(h => h.Population));
        // Compatibility overload; reserve stock is a physical-year target.
        public static double DesiredReserveFood(WorldState w, double population, double intervals)
            => PhysicalFoodTimebase.StockForYears(w, population, w.Config.GrowthReserveYears);
        public static double DesiredReserveFood(WorldState w, double population)
            => PhysicalFoodTimebase.StockForYears(w, population, w.Config.GrowthReserveYears);
        public static double TargetPots(WorldState w, Camp c, double years, double residents = -1)
        {
            double reservePopulation = ReservePopulation(w, c, residents);
            double natural = NaturalStorageCapacity(w, c, 0, residents);
            double desired = DesiredReserveFood(w, reservePopulation);
            return Math.Max(0, desired - natural) / w.Config.Neolithic.PotStorageFoodPerUnit;
        }
        public static double PotDemand(WorldState w, Camp c, double years, double residents = -1)
        {
            var n = w.Config.Neolithic;
            double retained = Math.Max(0, c.Inventory.Pots) * Math.Pow(1 - n.PotDecayPerReferenceTurn, Math.Max(0, years) / n.ReferenceTurnYears);
            return Math.Max(0, TargetPots(w, c, years, residents) - retained);
        }
        public static double PotDemandFromCurrentStock(WorldState w, Camp c, double years, double residents = -1)
            => Math.Max(0, TargetPots(w, c, years, residents) - Math.Max(0, c.Inventory.Pots));
        public static void DepreciatePots(WorldState w, Camp c, double years)
        {
            var n = w.Config.Neolithic;
            c.Inventory.Pots = Math.Max(0, c.Inventory.Pots) * Math.Pow(1 - n.PotDecayPerReferenceTurn, Math.Max(0, years) / n.ReferenceTurnYears);
        }
        public static double StorageCapacity(WorldState w, Camp c, double intervals)
            => NaturalStorageCapacity(w, c, intervals) + Math.Max(0, c.Inventory.Pots) * w.Config.Neolithic.PotStorageFoodPerUnit;
        // Managed AnimalFood is received before ResolveCampNeolithic depreciates
        // existing Pots.  Project that one scheduled depreciation without mutating
        // the camp so the common food budget cannot accept inventory that will be
        // discarded at the end of the same generation.
        public static double StorageCapacityAfterPotDepreciation(WorldState w, Camp c, double intervals, double years)
        {
            var n = w.Config.Neolithic;
            double retainedPots = Math.Max(0, c.Inventory.Pots) * Math.Pow(1 - n.PotDecayPerReferenceTurn, Math.Max(0, years) / n.ReferenceTurnYears);
            return NaturalStorageCapacity(w, c, 0) + retainedPots * n.PotStorageFoodPerUnit;
        }
        public static double FabricFeedstockNeed(WorldState w, Camp c, double intervals, double years, double residentPopulation = -1, bool fieldWorkAlreadyResolved = false, bool potsAlreadyDecayed = false)
        {
            return FabricFeedstockInputs(w, c, intervals, years, residentPopulation, fieldWorkAlreadyResolved, potsAlreadyDecayed).Total;
        }
        public static ManufacturingFoodPlan FabricFeedstockInputs(WorldState w, Camp c, double intervals, double years, double residentPopulation = -1, bool fieldWorkAlreadyResolved = false, bool potsAlreadyDecayed = false, double plantFoodBudget = -1, double animalFoodBudget = -1, ProductionOrder order = null)
        {
            var result = new ManufacturingFoodPlan();
            if (!CultivationEconomy.EconomyEnabled(c) || c.Population <= 0) return result;
            var coverage = new ToolCoverageBudget(w, c, intervals);
            var planned = MaterialEconomy.Forecast(w, c, Recipes(w.Config), intervals, years, coverage.Remaining, true, residentPopulation, fieldWorkAlreadyResolved, potsAlreadyDecayed, plantFoodBudget, animalFoodBudget, null, order);
            result.PlantFood = planned[CraftGood.Cloth] * w.Config.Neolithic.PlantFoodPerFabricUnit;
            result.AnimalFood = planned[CraftGood.AnimalFabric];
            return result;
        }
        // The food input of the fabric that covers this turn's clothing requirement.
        // Requirement units are what ResolveNeeds consumes, less existing Cloth and
        // Animal Fabric; planned output meets them in consumption order (Cloth, then
        // Animal Fabric) and caps the result. This single definition is shared by the
        // planner's envelopes and the resolver's needs, harvest claims and growth plan:
        // it is paid after consumption and ahead of the operating-reserve refill.
        public struct ClothingFeedstock
        {
            public double Plant, Animal;
            public double Total => Plant + Animal;
        }
        public static double ClothingNeedUnits(NeolithicConfig n, double residents, double years, double clothStock, double animalFabricStock)
            => Math.Max(0, residents * n.FabricConsumptionPerPersonTurn * years / n.ReferenceTurnYears
                - Math.Max(0, clothStock) - Math.Max(0, animalFabricStock));
        public static ClothingFeedstock ClothingNeedFeedstock(NeolithicConfig n, double residents, double years,
            double clothStock, double animalFabricStock, double plannedPlantFeedstock, double plannedAnimalFeedstock)
        {
            double units = ClothingNeedUnits(n, residents, years, clothStock, animalFabricStock);
            double perCloth = n.PlantFoodPerFabricUnit;
            double clothUnits = perCloth > 0 ? Math.Min(units, Math.Max(0, plannedPlantFeedstock) / perCloth) : 0;
            double animalUnits = Math.Min(units - clothUnits, Math.Max(0, plannedAnimalFeedstock));
            return new ClothingFeedstock { Plant = clothUnits * perCloth, Animal = animalUnits };
        }
        // Harvest still required for the planned feedstock. Clothing feedstock may
        // draw on inventory beyond this turn's meals; every other feedstock only on
        // inventory beyond meals, the clothing already paid and the operating reserve.
        public static void FeedstockGaps(double food, double plantStock, double animalStock, double meals, double reserve,
            double plantFeedstock, double animalFeedstock, ClothingFeedstock clothing, out double plantGap, out double animalGap)
        {
            plantStock = Math.Max(0, plantStock); animalStock = Math.Max(0, animalStock);
            double afterMeals = Math.Max(0, food - meals);
            double plantInventory = Math.Min(plantStock, afterMeals);
            double animalInventory = Math.Min(animalStock, Math.Max(0, afterMeals - plantInventory));
            double clothingPlantInventory = Math.Min(clothing.Plant, plantInventory);
            double clothingAnimalInventory = Math.Min(clothing.Animal, animalInventory);
            double spareFood = Math.Max(0, food - meals - clothingPlantInventory - clothingAnimalInventory - reserve);
            double sparePlant = Math.Min(plantStock - clothingPlantInventory, spareFood);
            double spareAnimal = Math.Min(animalStock - clothingAnimalInventory, Math.Max(0, spareFood - sparePlant));
            plantGap = Math.Max(0, clothing.Plant - clothingPlantInventory)
                + Math.Max(0, plantFeedstock - clothing.Plant - sparePlant);
            animalGap = Math.Max(0, clothing.Animal - clothingAnimalInventory)
                + Math.Max(0, animalFeedstock - clothing.Animal - spareAnimal);
        }
        public static double FarmerMaintenanceMultiplier(Camp c) => 1;
        public static double DomesticExpectationFactor(Camp c) => c.RudimentaryExpectation;
        public static bool CanIrrigateTile(WorldState w, Camp c, int tile, out string reason) { reason = "Irrigation construction and effects are deferred."; return false; }
        public static double HousingMultiplier(WorldState w, Camp c, double workers)
        {
            var n = w.Config.Neolithic;
            double covered = Math.Min(workers, (c.Inventory.HandTools + c.Inventory.Tools + (c.People.HasTech(NeolithicTech.CopperWorking) ? c.Inventory.CopperTools : 0)) / n.ToolCoveragePerWorker);
            if (covered <= 0) return n.HandToolsHousingMultiplier;
            double copper = c.People.HasTech(NeolithicTech.CopperWorking) ? Math.Min(covered, c.Inventory.CopperTools / n.ToolCoveragePerWorker) : 0;
            double improved = Math.Min(covered - copper, c.Inventory.Tools / n.ToolCoveragePerWorker);
            return (copper * n.CopperToolsHousingMultiplier + improved * n.ImprovedToolsHousingMultiplier + (covered - copper - improved) * n.HandToolsHousingMultiplier) / covered;
        }
        public static void DepreciateHousing(WorldState w, Camp c, double years, int residents = -1)
        {
            var n = w.Config.Neolithic;
            double endDays = w.Days + Math.Max(0, years) * w.Config.DaysPerYear;
            double before = HousingSupply(w, c, w.Days);
            bool authoritativeCamp = ReferenceEquals(w.Camps.FirstOrDefault(x => x.Id == c.Id), c);
            if (authoritativeCamp)
            {
                MigrateLegacyFixedHousing(w, c);
                foreach (var stock in (w.FixedHousing ?? new List<FixedHousingStock>()).Where(s => s.CampId == c.Id && s.Tile == c.Tile).OrderBy(s => s.Tile))
                {
                    stock.Units = ProjectFixedHousing(w, stock, endDays); stock.LastUpdatedDays = endDays;
                }
            }
            c.Inventory.LightHousingUnits = Math.Max(0, c.Inventory.LightHousingUnits) * Math.Pow(1 - n.HousingDecayPerTurn, Math.Max(0, years) / n.ReferenceTurnYears);
            double after = HousingSupply(w, c, endDays);
            c.HousingDemand = Math.Max(0, (residents < 0 ? c.Population : residents) / n.HousingPeoplePerUnit - after);
            c.HousingReplacementDemand = Math.Min(Math.Max(0, before - after), c.HousingDemand);
            c.HousingGrowthDemand = Math.Max(0, c.HousingDemand - c.HousingReplacementDemand);
        }
        public static double Produce(WorldState w, Camp c, CraftRecipe recipe, string role, double workerIntervals, double intervals, double usefulDemand, ref double equipmentIntervals, TurnRecord record, ToolCoverageBudget coverage = null, HousingMaterialPlan housing = null, ProductionOrder order = null)
        {
            if (!CultivationEconomy.EconomyEnabled(c) || workerIntervals <= 0 || recipe.Rate <= 0 || usefulDemand <= 0 || intervals <= 0) return 0;
            if (recipe.AgricultureOnly && !CultivationEconomy.Enabled(c) || recipe.HusbandryOnly && !CultivationEconomy.HusbandryEnabled(c)) return 0;
            bool specialist = role == recipe.Specialist && c.People.HasTech(recipe.Track);
            if (!specialist && (role != WorkforceRoles.Craftsperson || recipe.SpecialistOnly)) return 0;
            double rate = recipe.Rate * (specialist ? 1 : w.Config.Neolithic.GeneralistTaskEfficiency);
            if (rate <= 0) return 0;
            coverage = coverage ?? new ToolCoverageBudget(w, c, intervals, equipmentIntervals);
            // Direct callers retain the public production entry point.  Give a
            // standalone Housing receipt the same source-aware policy; the
            // resolver supplies one shared plan so sequential role calls do not
            // recompute it after stock changes.
            if (recipe.ConstructionMaterial > 0 && housing == null)
            {
                var planned = Recipes(w.Config).ToDictionary(r => r.Good, r => 0d);
                planned[recipe.Good] = Math.Min(usefulDemand, recipe.Equipment ? coverage.Capacity(recipe.Good, rate, workerIntervals) : workerIntervals * rate);
                housing = MaterialEconomy.PlanHousingMaterials(w, c, Recipes(w.Config), planned, intervals * w.Config.BaseDays / w.Config.DaysPerYear, w.Days, null, order);
            }
            // The shared budget limits finished output.  The legacy equipment
            // interval argument is retained for callers but is no longer a
            // second, worker-count capacity gate.
            double labor = workerIntervals;
            // Housing Material shares its Builder pool with the assembly that will
            // consume it, so it never takes labour the assembly needs this turn.
            double amount = recipe.Good == CraftGood.HousingMaterial
                ? HousingMaterialLimit(coverage, rate, labor, usefulDemand, c.Inventory.HousingMaterial)
                : Math.Min(usefulDemand, recipe.Equipment ? coverage.Capacity(recipe.Good, rate, labor) : labor * rate);
            var inv = c.Inventory;
            if (recipe.Stone > 0) amount = Math.Min(amount, inv.Stone / recipe.Stone);
            if (recipe.Clay > 0) amount = Math.Min(amount, inv.Clay / recipe.Clay);
            if (recipe.Wood > 0) amount = Math.Min(amount, inv.Wood / recipe.Wood);
            if (recipe.PlantFood > 0) amount = Math.Min(amount, c.PlantFood / recipe.PlantFood);
            if (recipe.AnimalFood > 0) amount = Math.Min(amount, c.AnimalFood / recipe.AnimalFood);
            if (recipe.AnimalFabric > 0) amount = Math.Min(amount, Math.Max(0, inv.AnimalFabric) / recipe.AnimalFabric);
            if (recipe.HousingMaterial > 0) amount = Math.Min(amount, Math.Max(0, inv.HousingMaterial) / recipe.HousingMaterial);
            // A recipe ranked below Housing Material leaves the Wood/Clay its
            // unfinished claim needs, whichever role happens to run first.
            if (housing != null && recipe.ConstructionMaterial <= 0 && (recipe.Wood > 0 || recipe.Clay > 0))
                amount = housing.LimitDirect(inv.Wood, inv.Clay, recipe, amount);
            if (recipe.ConstructionMaterial > 0)
            {
                if (housing == null) amount = Math.Min(amount, (inv.Wood + inv.Clay) / recipe.ConstructionMaterial);
                else amount = housing.LimitHousing(c, amount, recipe.ConstructionMaterial, out _, out _);
            }
            if (amount <= 1e-12) return 0;
            double wood = amount * recipe.Wood, clay = amount * recipe.Clay;
            if (recipe.ConstructionMaterial > 0)
            {
                if (housing == null)
                {
                    double materials = amount * recipe.ConstructionMaterial;
                    wood += Math.Min(inv.Wood, materials); clay += materials - Math.Min(inv.Wood, materials);
                }
                else housing.LimitHousing(c, amount, recipe.ConstructionMaterial, out wood, out clay);
            }
            double handBeforeBudget = recipe.Equipment ? coverage.HandToolsConsumed : 0;
            double toolsBeforeBudget = recipe.Equipment ? coverage.ToolsConsumed : 0;
            double used = recipe.Equipment ? coverage.Spend(recipe.Good, rate, amount) : recipe.Good == CraftGood.HousingMaterial ? coverage.Time(recipe.Good, rate, amount) : amount / rate;
            // Spend reports cumulative budget use. Debit only this receipt's
            // delta while keeping the budget shared across all production calls.
            double consumedHand = recipe.Equipment ? coverage.HandToolsConsumed - handBeforeBudget : 0;
            double consumedTools = recipe.Equipment ? coverage.ToolsConsumed - toolsBeforeBudget : 0;
            inv.HandTools -= consumedHand; inv.Tools -= consumedTools;
            inv.Stone -= amount * recipe.Stone; inv.Wood -= wood; inv.Clay -= clay; c.PlantFood -= amount * recipe.PlantFood; c.AnimalFood -= amount * recipe.AnimalFood; inv.AnimalFabric -= amount * recipe.AnimalFabric; inv.HousingMaterial -= amount * recipe.HousingMaterial;
            if (housing != null) housing.CompleteDirect(recipe, amount);
            switch (recipe.Good) { case CraftGood.HandTools: inv.HandTools += amount; break; case CraftGood.Pots: inv.Pots += amount; break; case CraftGood.Cloth: inv.Cloth += amount; break; case CraftGood.FarmTools: inv.FarmTools += amount; break; case CraftGood.Housing: AddFixedHousing(w, c, amount, record.Duration > 0 ? record.StartDays + record.Duration : w.Days + intervals * w.Config.BaseDays); break; case CraftGood.Tools: inv.Tools += amount; break; case CraftGood.AnimalFabric: inv.AnimalFabric += amount; break; case CraftGood.LightHousing: inv.LightHousingUnits += amount; break; case CraftGood.HerdingTools: inv.HerdingTools += amount; break; case CraftGood.HousingMaterial: inv.HousingMaterial += amount; break; }
            coverage.Credit(recipe.Good, amount);
            record.Production.Add(new ProductionReceipt { Camp = c.Id, Good = recipe.Good, Role = role, Output = amount, WorkerIntervals = used, Stone = amount * recipe.Stone, Clay = clay, Wood = wood, PlantFood = amount * recipe.PlantFood, AnimalFood = amount * recipe.AnimalFood, AnimalFabric = amount * recipe.AnimalFabric, HousingMaterial = amount * recipe.HousingMaterial, EquipmentWorkerIntervals = recipe.Equipment ? used : 0, HandToolsConsumed = consumedHand, ToolsConsumed = consumedTools });
            var demographic = record.Demographics.FirstOrDefault(d => d.Camp == c.Id);
            if (demographic != null) demographic.ManufacturingFood += amount * (recipe.PlantFood + recipe.AnimalFood);
            double work = amount / recipe.Rate;
            c.People.CraftExperience.TryGetValue(recipe.Track, out double experience); c.People.CraftExperience[recipe.Track] = experience + work;
            if (experience + work >= w.Config.Neolithic.CraftMasteryThreshold && !c.People.HasTech(recipe.Track))
            {
                c.People.NeolithicTechnologies.Add(recipe.Track);
                foreach (string unlocked in WorkforceRoles.All.Where(role => EconomicIntents.RequiredTechnology(role) == recipe.Track))
                    if (!c.People.UnlockedSpecialists.Contains(unlocked)) c.People.UnlockedSpecialists.Add(unlocked);
            }
            return used;
        }
        public static void ResolveCampNeolithic(WorldState w, Camp c, TurnHorizon horizon, TurnRecord record, double gatheringWorkerIntervals = 0, TurnNeeds needs = null)
            => ResolveCampNeolithic(w, c, horizon.ReferenceIntervals, horizon.Years, record, gatheringWorkerIntervals, needs);
        public static void ResolveCampNeolithic(WorldState w, Camp c, double intervals, double years, TurnRecord record, double gatheringWorkerIntervals = 0, TurnNeeds needs = null)
        {
            if (c.Population <= 0) return;
            int residents = needs?.Residents ?? c.Population;
            DepreciateHousing(w, c, years, residents);
            DepreciatePots(w, c, years);
            if (CultivationEconomy.EconomyEnabled(c))
            {
                var recipes = Recipes(w.Config);
                double equipment = (c.Inventory.HandTools + c.Inventory.Tools + (c.People.HasTech(NeolithicTech.CopperWorking) ? c.Inventory.CopperTools : 0)) * intervals / w.Config.Neolithic.ToolCoveragePerWorker;
                var coverage = new ToolCoverageBudget(w, c, intervals);
                var housing = MaterialEconomy.Gather(w, c, recipes, intervals, years, gatheringWorkerIntervals, ref equipment, record, true, true, needs);
                var produced = new Dictionary<CraftGood, double>();
                var roles = WorkforceRoles.Specialists.Concat(new[] { WorkforceRoles.Craftsperson }).ToArray();
                var laborByRole = roles.ToDictionary(role => role, role => CultivationEconomy.Workers(c, role) * intervals);
                // The Value order (when this camp has one) fixes the order in which
                // recipes claim shared tool coverage and material; the planner and
                // forecast walk the same sequence.
                var order = needs?.Order;
                var orderedRecipes = ProductionOrder.Sort(recipes, good => Priority(c, good), order);
                var steps = ProductionOrder.Steps(orderedRecipes, order != null).ToArray();
                // Resolve an acyclic aggregate input balance: non-equipment
                // toolmaking supplies the same turn's equipment service ledger.
                // The two sweeps are dependency order, not production dates.
                for (int balanceLayer = 0; balanceLayer < 2; balanceLayer++)
                {
                    foreach (var (role, recipe) in steps)
                    {
                        bool toolInput = recipe.Good == CraftGood.HandTools || recipe.Good == CraftGood.Tools;
                        if (toolInput != (balanceLayer == 0)) continue;
                        if (recipe.HusbandryOnly && !CultivationEconomy.HusbandryEnabled(c) || recipe.AgricultureOnly && !CultivationEconomy.Enabled(c)) continue;
                        if (Priority(c, recipe.Good) <= 0) continue;
                        double labor = laborByRole[role];
                        double target = needs?.Production != null
                            ? Math.Max(0, needs.Production[recipe.Good] - (produced.TryGetValue(recipe.Good, out double made) ? made : 0))
                            : Demand(w, c, recipe.Good, years, -1, true, true, null, order);
                        int before = record.Production.Count;
                        labor -= Produce(w, c, recipe, role, labor, intervals, target, ref equipment, record, coverage, housing, order);
                        laborByRole[role] = labor;
                        housing?.Settle(recipe, role);
                        if (record.Production.Count > before)
                            produced[recipe.Good] = (produced.TryGetValue(recipe.Good, out double prior) ? prior : 0) + record.Production[record.Production.Count - 1].Output;
                    }
                }
            }
            ResolveNeeds(w, c, years, residents);
        }
        // Regime cohesion scale N_s, selected from the camp's current capability.
        public static double CohesionScale(NeolithicConfig n, Camp c)
            => c.Technology == Technology.None ? n.CohesionScalePaleolithic
             : CultivationEconomy.Enabled(c) ? n.CohesionScaleAgricultural
             : n.CohesionScaleNomadic;
        // S(N) = exp(-(N / N_s)^beta): the single shared social-cohesion multiplier.
        public static double SocialCohesion(NeolithicConfig n, Camp c, double population)
            => Math.Exp(-Math.Pow(Math.Max(0, population) / CohesionScale(n, c), n.SocialCohesionExponent));
        public static void ResolveNeeds(WorldState w, Camp c, double years, int residents = -1)
        {
            var n = w.Config.Neolithic;
            int cohort = residents < 0 ? c.Population : residents;
            double required = cohort * n.FabricConsumptionPerPersonTurn * years / n.ReferenceTurnYears;
            double cloth = Math.Min(required, c.Inventory.Cloth); c.Inventory.Cloth -= cloth;
            double animal = Math.Min(Math.Max(0, required - cloth), c.Inventory.AnimalFabric); c.Inventory.AnimalFabric -= animal;
            c.FabricConsumed = cloth + animal;
            c.ClothPerCapita = required > 0 ? c.FabricConsumed / required : 1;
            // The resolver calls this after DepreciateHousing has already aged
            // existing fixed and portable shelter through the generation.
            c.HousingPerCapita = cohort > 0 ? Math.Min(1, HousingSupply(w, c, w.Days) * n.HousingPeoplePerUnit / cohort) : 1;
            c.StationaryYears += years;
            c.RudimentaryExpectation = n.RudimentaryExpectationFloor + (1 - n.RudimentaryExpectationFloor) * Numbers.Clamp(c.StationaryYears / n.RudimentaryExpectationRampYears, 0, 1);
            double clothing = 1 - c.RudimentaryExpectation * (1 - c.ClothPerCapita), housing = 1 - c.RudimentaryExpectation * (1 - c.HousingPerCapita);
            c.QoL = c.FoodNeedsMetRatio * c.WaterNeedsMetRatio * (n.RudimentaryWeightClothing * clothing + n.RudimentaryWeightHousing * housing) * SocialCohesion(n, c, cohort);
            c.FissionWilling = 0;
        }
    }
}
