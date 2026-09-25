using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    public static class NeolithicTech
    {
        public const string Toolmaking = "Toolmaking", Pottery = "Pottery", Weaving = "Weaving", Carpentry = "Carpentry", Construction = "Construction";
        public const string KilnFiring = "KilnFiring", Plough = "Plough", Irrigation = "Irrigation", Sailing = "Sailing", WheelAndAxle = "WheelAndAxle", CopperWorking = "CopperWorking";
        public static readonly string[] FirstTier = { Toolmaking, Pottery, Weaving, Carpentry, Construction };
        public static readonly string[] SecondTier = { KilnFiring, Plough, Irrigation, Sailing };
        public static readonly string[] Convergence = { WheelAndAxle, CopperWorking };
    }
    // Append new values so existing saved/history enum ordinals remain stable.
    public enum CraftGood { HandTools, Pots, Cloth, FarmTools, Housing, Tools, AnimalFabric, LightHousing, HerdingTools }
    public sealed class CraftRecipe
    {
        public CraftGood Good;
        public string Specialist, Track;
        public double Rate, Stone, Clay, Wood, PlantFood, AnimalFood, AnimalFabric, ConstructionMaterial;
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
        public double Remaining => remaining[0] + remaining[1] + remaining[2];
        public double HandToolsConsumed => consumed[0];
        public double ToolsConsumed => consumed[1];
        public double CopperToolsConsumed => consumed[2];
        public ToolCoverageBudget(WorldState w, Camp c, double intervals, double limit = double.PositiveInfinity)
        {
            var n = w.Config.Neolithic;
            remaining = new[] { c.Inventory.HandTools, c.Inventory.Tools * 4, c.People.HasTech(NeolithicTech.CopperWorking) ? c.Inventory.CopperTools * intervals / n.ToolCoveragePerWorker : 0 }.ToArray();
            consumed = new double[3];
            // The old argument was a worker-time ceiling.  Physical inventory is
            // now the authoritative budget; callers keep the argument only for
            // source compatibility and it must not reintroduce worker-count wear.
            housingMultipliers = new[] { n.HandToolsHousingMultiplier, n.ImprovedToolsHousingMultiplier, n.CopperToolsHousingMultiplier };
        }
        public double Capacity(CraftGood good, double rate, double labor)
        {
            bool housing = good == CraftGood.Housing || good == CraftGood.LightHousing;
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
            bool housing = good == CraftGood.Housing || good == CraftGood.LightHousing;
            double used = 0;
            foreach (int grade in housing ? new[] { 2, 1, 0 } : new[] { 0, 1, 2 })
            {
                double multiplier = housing ? housingMultipliers[grade] : 1;
                if (multiplier <= 0) continue;
                if (grade == 2)
                {
                    double time = Math.Min(remaining[grade], Math.Max(0, output) / (rate * multiplier));
                    remaining[grade] -= time; output -= time * rate * multiplier; used += time;
                }
                else
                {
                    double units = Math.Min(remaining[grade], Math.Max(0, output));
                    remaining[grade] -= units; output -= units;
                    consumed[grade] += units / (grade == 0 ? 1 : 4);
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
    }
    public static class HerdingTooling
    {
        public static double DomesticAnimals(WorldState w, Camp c)
            => w.Herds.Where(h => h.DomesticCamp == c.Id && h.Animals > 0).OrderBy(h => h.Id).Sum(h => h.Animals);
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
        public static HerdingManagementPlan Plan(WorldState w, Camp c, double years, int herders = -1)
        {
            var n = w.Config.Neolithic;
            int assigned = herders < 0 ? c.Workforce.Assignments.Where(a => a.Role == WorkforceRoles.Herder).Sum(a => a.Population) : herders;
            double animals = DomesticAnimals(w, c), herderYears = Math.Max(0, assigned * years);
            double coveredHerders = Math.Min(assigned, Math.Min(Math.Max(0, c.Inventory.HerdingTools) * n.HerdingToolCoverageYearsPerUnit / Math.Max(1e-12, years), animals / n.StoneHerdingToolAnimalsPerHerder));
            double uncoveredHerders = Math.Min(Math.Max(0, assigned - coveredHerders), Math.Max(0, animals - coveredHerders * n.StoneHerdingToolAnimalsPerHerder) / Math.Max(1e-12, w.Config.AnimalsPerHerder));
            double coveredHerderYears = coveredHerders * Math.Max(0, years);
            double capacity = coveredHerders * n.StoneHerdingToolAnimalsPerHerder + uncoveredHerders * w.Config.AnimalsPerHerder;
            return new HerdingManagementPlan
            {
                Herders = assigned, Animals = animals, HerderYears = herderYears, CoveredHerderYears = coveredHerderYears,
                Capacity = capacity, Fulfillment = animals <= 0 ? 1 : Numbers.Clamp(capacity / animals, 0, 1),
                ToolsConsumed = coveredHerderYears / n.HerdingToolCoverageYearsPerUnit
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
                new CraftRecipe { Good = CraftGood.Housing, Specialist = WorkforceRoles.Builder, Track = NeolithicTech.Construction, Rate = n.BuilderHousingOutputPerBaseDays, ConstructionMaterial = n.HousingMaterialUnitsPerHousing, Equipment = true, AgricultureOnly = true },
                new CraftRecipe { Good = CraftGood.Tools, Specialist = WorkforceRoles.Toolmaker, Track = NeolithicTech.Toolmaking, Rate = n.ToolsOutput, Stone = n.StonePerTool, Wood = n.WoodPerTool, SpecialistOnly = true },
                new CraftRecipe { Good = CraftGood.AnimalFabric, Specialist = WorkforceRoles.Weaver, Track = NeolithicTech.Weaving, Rate = n.WeaverFabricOutputPerBaseDays, AnimalFood = 1, Equipment = true, HusbandryOnly = true },
                new CraftRecipe { Good = CraftGood.LightHousing, Specialist = WorkforceRoles.Builder, Track = NeolithicTech.Construction, Rate = n.BuilderHousingOutputPerBaseDays, AnimalFabric = 1, Equipment = true, HusbandryOnly = true },
                new CraftRecipe { Good = CraftGood.HerdingTools, Specialist = WorkforceRoles.Carpenter, Track = NeolithicTech.Carpentry, Rate = n.FarmToolsOutput, Wood = 1, Stone = 1, Equipment = true, HusbandryOnly = true }
            };
        }
        public static double Stock(Camp c, CraftGood good) => good == CraftGood.HandTools ? c.Inventory.HandTools : good == CraftGood.Pots ? c.Inventory.Pots : good == CraftGood.Cloth ? c.Inventory.Cloth : good == CraftGood.FarmTools ? c.Inventory.FarmTools : good == CraftGood.Tools ? c.Inventory.Tools : good == CraftGood.AnimalFabric ? c.Inventory.AnimalFabric : good == CraftGood.LightHousing ? c.Inventory.LightHousingUnits : good == CraftGood.HerdingTools ? c.Inventory.HerdingTools : c.Inventory.HousingUnits;
        public static double Priority(Camp c, CraftGood good) => good == CraftGood.HandTools ? c.CraftingPriorities.HandTools : good == CraftGood.Pots ? c.CraftingPriorities.Pots : good == CraftGood.Cloth ? c.CraftingPriorities.Cloth : good == CraftGood.FarmTools ? c.CraftingPriorities.FarmTools : good == CraftGood.Tools ? c.CraftingPriorities.Tools : good == CraftGood.AnimalFabric ? c.CraftingPriorities.AnimalFabric : good == CraftGood.LightHousing ? c.CraftingPriorities.LightHousing : good == CraftGood.HerdingTools ? c.CraftingPriorities.HerdingTools : c.CraftingPriorities.Housing;
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
        private static bool AgricultureOnly(CraftGood good) => good == CraftGood.Cloth || good == CraftGood.FarmTools || good == CraftGood.Housing;
        // Compatibility wrapper for callers that resolve both agricultural work
        // and pot depreciation before forecasting production.
        public static double Demand(WorldState w, Camp c, CraftGood good, double years, double residentPopulation = -1, bool postAgriculturalResolution = false)
            => Demand(w, c, good, years, residentPopulation, postAgriculturalResolution, postAgriculturalResolution);

        // Field work is resolved before growth planning, while Pots depreciate
        // later in ResolveCampNeolithic. Keep those chronology boundaries separate.
        public static double Demand(WorldState w, Camp c, CraftGood good, double years, double residentPopulation, bool fieldWorkAlreadyResolved, bool potsAlreadyDecayed, KnownMaterialSource[] knownSources = null)
        {
            var n = w.Config.Neolithic;
            double residents = residentPopulation < 0 ? c.Population : residentPopulation;
            if (good == CraftGood.HandTools || good == CraftGood.Tools)
                return ToolReplacementDemand(w, c, good, years, residents, fieldWorkAlreadyResolved, potsAlreadyDecayed, knownSources);
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
        private static double ToolReplacementDemand(WorldState w, Camp c, CraftGood good, double years, double residents, bool fieldWorkAlreadyResolved, bool potsAlreadyDecayed, KnownMaterialSource[] knownSources)
        {
            var n = w.Config.Neolithic;
            var recipes = Recipes(w.Config).Where(r => r.Equipment && (!r.AgricultureOnly || CultivationEconomy.Enabled(c)) && (!r.HusbandryOnly || CultivationEconomy.HusbandryEnabled(c)))
                .OrderByDescending(r => Priority(c, r.Good)).ThenBy(r => r.Good).ToArray();
            double MaterialPriority(MaterialId material) => material == MaterialId.Stone ? c.StonePriority : material == MaterialId.Clay ? c.ClayPriority : c.WoodPriority;
            var sources = knownSources ?? MaterialEconomy.KnownSources(w, c, w.Days);
            var room = MaterialEconomy.Materials.ToDictionary(m => m, m => MaterialEconomy.Inventory(c, m) +
                (MaterialPriority(m) > 0 && n.SpecialistMaterialExtractionRate > 0 ? sources.Where(s => s.Material == m).Sum(s => s.Available) : 0));
            double plant = c.PlantFood, animalFood = c.AnimalFood, animalFabric = c.Inventory.AnimalFabric, useful = 0, copperOutput = 0;
            double copperLabor = c.People.HasTech(NeolithicTech.CopperWorking) ? c.Inventory.CopperTools * w.Config.GenerationIntervals / n.ToolCoveragePerWorker : 0;
            var labor = WorkforceRoles.Specialists.Concat(new[] { WorkforceRoles.Craftsperson }).ToDictionary(role => role, role => c.Workforce.Assignments.Where(a => a.Role == role).Sum(a => a.Population) * w.Config.GenerationIntervals);
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
                labor[WorkforceRoles.Craftsperson] += missingEquipmentWorkers * w.Config.GenerationIntervals;
            }
            // First retain the direct material inputs that the normal
            // priority-ordered workforce schedule can actually use.  This is a
            // material reservation pass only: it does not reorder workers, and
            // it still lets Housing consume its place in each role's priority
            // schedule.  The reservation is then applied to the same schedule
            // so a Builder cannot consume Wood needed by a later Farm Tool job
            // without moving that job ahead of Housing.
            var directReserve = MaterialEconomy.Materials.ToDictionary(m => m, _ => 0d);
            var probeRoom = new Dictionary<MaterialId, double>(room);
            var probePlant = plant;
            var probeAnimalFood = animalFood;
            var probeAnimalFabric = animalFabric;
            var probePlanned = recipes.ToDictionary(r => r.Good, r => 0d);
            var probeLabor = labor.ToDictionary(p => p.Key, p => p.Value);
            foreach (var role in WorkforceRoles.Specialists.Concat(new[] { WorkforceRoles.Craftsperson }))
            {
                if (!probeLabor.TryGetValue(role, out double availableLabor)) continue;
                foreach (var recipe in recipes)
                {
                    if (Priority(c, recipe.Good) <= 0) continue;
                    bool specialist = c.People.HasTech(recipe.Track);
                    if (role != WorkforceRoles.Craftsperson && (!specialist || role != recipe.Specialist)) continue;
                    if (role == WorkforceRoles.Craftsperson && recipe.SpecialistOnly) continue;
                    double rate = recipe.Rate * (role == recipe.Specialist && specialist ? 1 : n.GeneralistTaskEfficiency);
                    double wanted = Math.Max(0, DemandWithoutToolReplacement(w, c, recipe.Good, years, residents, fieldWorkAlreadyResolved, potsAlreadyDecayed) - probePlanned[recipe.Good]);
                    double amount = Math.Min(wanted, availableLabor * rate);
                    if (recipe.Stone > 0) amount = Math.Min(amount, probeRoom[MaterialId.Stone] / recipe.Stone);
                    if (recipe.Clay > 0) amount = Math.Min(amount, probeRoom[MaterialId.Clay] / recipe.Clay);
                    if (recipe.Wood > 0) amount = Math.Min(amount, probeRoom[MaterialId.Wood] / recipe.Wood);
                    if (recipe.PlantFood > 0) amount = Math.Min(amount, probePlant / recipe.PlantFood);
                    if (recipe.AnimalFood > 0) amount = Math.Min(amount, probeAnimalFood / recipe.AnimalFood);
                    if (recipe.AnimalFabric > 0) amount = Math.Min(amount, probeAnimalFabric / recipe.AnimalFabric);
                    if (amount <= 1e-12) continue;
                    availableLabor -= amount / rate;
                    probePlanned[recipe.Good] += amount;
                    probeRoom[MaterialId.Stone] -= amount * recipe.Stone;
                    probeRoom[MaterialId.Clay] -= amount * recipe.Clay;
                    probeRoom[MaterialId.Wood] -= amount * recipe.Wood;
                    directReserve[MaterialId.Stone] += amount * recipe.Stone;
                    directReserve[MaterialId.Clay] += amount * recipe.Clay;
                    directReserve[MaterialId.Wood] += amount * recipe.Wood;
                    probePlant -= amount * recipe.PlantFood;
                    probeAnimalFood -= amount * recipe.AnimalFood;
                    probeAnimalFabric += recipe.Good == CraftGood.AnimalFabric ? amount : 0;
                    probeAnimalFabric -= amount * recipe.AnimalFabric;
                }
                probeLabor[role] = availableLabor;
            }

            var planned = recipes.ToDictionary(r => r.Good, r => 0d);
            double availableWood = room[MaterialId.Wood], availableClay = room[MaterialId.Clay];
            MaterialEconomy.HousingShares(availableWood, availableClay, directReserve[MaterialId.Wood], directReserve[MaterialId.Clay], out double housingWoodShare, out double housingClayShare);
            foreach (var role in WorkforceRoles.Specialists.Concat(new[] { WorkforceRoles.Craftsperson }))
            {
                if (!labor.TryGetValue(role, out double availableLabor)) continue;
                foreach (var recipe in recipes)
                {
                    if (Priority(c, recipe.Good) <= 0) continue;
                    bool specialist = c.People.HasTech(recipe.Track);
                    if (role != WorkforceRoles.Craftsperson && (!specialist || role != recipe.Specialist)) continue;
                    if (role == WorkforceRoles.Craftsperson && recipe.SpecialistOnly) continue;
                    double rate = recipe.Rate * (role == recipe.Specialist && specialist ? 1 : n.GeneralistTaskEfficiency);
                    double wanted = Math.Max(0, DemandWithoutToolReplacement(w, c, recipe.Good, years, residents, fieldWorkAlreadyResolved, potsAlreadyDecayed) - planned[recipe.Good]);
                    double amount = Math.Min(wanted, availableLabor * rate);
                    if (recipe.Stone > 0) amount = Math.Min(amount, room[MaterialId.Stone] / recipe.Stone);
                    if (recipe.Clay > 0) amount = Math.Min(amount, room[MaterialId.Clay] / recipe.Clay);
                    if (recipe.Wood > 0) amount = Math.Min(amount, room[MaterialId.Wood] / recipe.Wood);
                    if (recipe.ConstructionMaterial > 0)
                    {
                        if (housingWoodShare + housingClayShare <= 0) amount = 0;
                        else
                        {
                            double freeWood = Math.Max(0, room[MaterialId.Wood] - directReserve[MaterialId.Wood]);
                            double freeClay = Math.Max(0, room[MaterialId.Clay] - directReserve[MaterialId.Clay]);
                            amount = Math.Min(amount, (freeWood + freeClay) / recipe.ConstructionMaterial);
                        }
                    }
                    if (recipe.PlantFood > 0) amount = Math.Min(amount, plant / recipe.PlantFood);
                    if (recipe.AnimalFood > 0) amount = Math.Min(amount, animalFood / recipe.AnimalFood);
                    if (recipe.AnimalFabric > 0) amount = Math.Min(amount, animalFabric / recipe.AnimalFabric);
                    if (amount <= 1e-12) continue;
                    availableLabor -= amount / rate;
                    planned[recipe.Good] += amount;
                    useful += amount;
                    double copperRate = rate * (recipe.Good == CraftGood.Housing || recipe.Good == CraftGood.LightHousing ? n.CopperToolsHousingMultiplier : 1);
                    double copperAmount = Math.Min(amount, copperLabor * copperRate);
                    copperOutput += copperAmount;
                    copperLabor -= copperRate > 0 ? copperAmount / copperRate : 0;
                    room[MaterialId.Stone] -= amount * recipe.Stone; room[MaterialId.Clay] -= amount * recipe.Clay; room[MaterialId.Wood] -= amount * recipe.Wood;
                    directReserve[MaterialId.Stone] = Math.Max(0, directReserve[MaterialId.Stone] - amount * recipe.Stone);
                    directReserve[MaterialId.Clay] = Math.Max(0, directReserve[MaterialId.Clay] - amount * recipe.Clay);
                    directReserve[MaterialId.Wood] = Math.Max(0, directReserve[MaterialId.Wood] - amount * recipe.Wood);
                    if (recipe.ConstructionMaterial > 0)
                    {
                        double used = amount * recipe.ConstructionMaterial;
                        double freeWood = Math.Max(0, room[MaterialId.Wood] - directReserve[MaterialId.Wood]);
                        double freeClay = Math.Max(0, room[MaterialId.Clay] - directReserve[MaterialId.Clay]);
                        double wood = Math.Min(freeWood, used * housingWoodShare);
                        double clay = Math.Min(freeClay, used - wood);
                        wood += Math.Min(freeWood - wood, used - wood - clay);
                        clay += Math.Min(freeClay - clay, used - wood - clay);
                        room[MaterialId.Wood] -= wood; room[MaterialId.Clay] -= clay;
                    }
                    plant -= amount * recipe.PlantFood;
                    animalFood -= amount * recipe.AnimalFood;
                    animalFabric += recipe.Good == CraftGood.AnimalFabric ? amount : 0;
                    animalFabric -= amount * recipe.AnimalFabric;
                }
                labor[role] = availableLabor;
            }
            double capacity = c.Inventory.HandTools + c.Inventory.Tools * 4;
            // Copper remains the legacy worker-time hook. Its contribution above
            // follows each useful recipe's actual rate and Housing multiplier;
            // there is no Copper output-capacity multiplier or wear.
            capacity += copperOutput;
            double deficit = Math.Max(0, useful - capacity);
            var regularRecipe = Recipes(w.Config).Single(r => r.Good == CraftGood.Tools);
            bool regularFeasible = c.People.HasTech(NeolithicTech.Toolmaking) && c.CraftingPriorities.Tools > 0 &&
                (regularRecipe.Stone <= 0 || room[MaterialId.Stone] > 1e-12) &&
                (regularRecipe.Wood <= 0 || room[MaterialId.Wood] > 1e-12);
            if (good == CraftGood.Tools && regularFeasible) return deficit / 4;
            if (good == CraftGood.HandTools && !regularFeasible) return deficit;
            return 0;
        }
        private static double DemandWithoutToolReplacement(WorldState w, Camp c, CraftGood good, double years, double residents, bool fieldWorkAlreadyResolved, bool potsAlreadyDecayed)
        {
            if (good == CraftGood.FarmTools) return Math.Max(0, FarmToolDemand(w, c, years, fieldWorkAlreadyResolved));
            return Demand(w, c, good, years, residents, fieldWorkAlreadyResolved, potsAlreadyDecayed);
        }
        private static double IntervalsForYears(WorldState w, double years) => Math.Max(0, years) * w.Config.DaysPerYear / w.Config.BaseDays;
        public static double NaturalStorageCapacity(WorldState w, Camp c, double intervals, double residents = -1)
        {
            double population = NaturalStoragePopulation(w, c, residents);
            return Math.Max(0, population * w.Config.Consumption * intervals * TurnResolver.StorageGenerations(w, c.Tile));
        }
        public static double NaturalStoragePopulation(WorldState w, Camp c, double residents = -1)
        {
            double projectedDelta = residents < 0 ? 0 : residents - c.Population;
            return Math.Max(0, w.LinkedPopulation(c) + projectedDelta);
        }
        public static double ReservePopulation(WorldState w, Camp c, double residents = -1)
            => Math.Max(0, (residents < 0 ? c.Population : residents) + w.Hunters.Where(h => h.SourceCamp == c.Id && Supply.Connected(w, h)).Sum(h => h.Population));
        public static double DesiredReserveFood(WorldState w, double population, double intervals)
            => Math.Max(0, population * w.Config.Consumption * intervals * w.Config.GrowthReserveTurns);
        public static double TargetPots(WorldState w, Camp c, double years, double residents = -1)
        {
            double reservePopulation = ReservePopulation(w, c, residents);
            double intervals = IntervalsForYears(w, years);
            double natural = NaturalStorageCapacity(w, c, intervals, residents);
            double desired = DesiredReserveFood(w, reservePopulation, intervals);
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
            return NaturalStorageCapacity(w, c, intervals) + retainedPots * n.PotStorageFoodPerUnit;
        }
        public static double FabricFeedstockNeed(WorldState w, Camp c, double intervals, double years, double residentPopulation = -1, bool fieldWorkAlreadyResolved = false, bool potsAlreadyDecayed = false)
        {
            return FabricFeedstockInputs(w, c, intervals, years, residentPopulation, fieldWorkAlreadyResolved, potsAlreadyDecayed).Total;
        }
        public static ManufacturingFoodPlan FabricFeedstockInputs(WorldState w, Camp c, double intervals, double years, double residentPopulation = -1, bool fieldWorkAlreadyResolved = false, bool potsAlreadyDecayed = false, double plantFoodBudget = -1, double animalFoodBudget = -1)
        {
            var result = new ManufacturingFoodPlan();
            if (!CultivationEconomy.EconomyEnabled(c) || c.Population <= 0) return result;
            var coverage = new ToolCoverageBudget(w, c, intervals);
            var planned = MaterialEconomy.Forecast(w, c, Recipes(w.Config), intervals, years, coverage.Remaining, true, residentPopulation, fieldWorkAlreadyResolved, potsAlreadyDecayed, plantFoodBudget, animalFoodBudget);
            result.PlantFood = planned[CraftGood.Cloth] * w.Config.Neolithic.PlantFoodPerFabricUnit;
            result.AnimalFood = planned[CraftGood.AnimalFabric];
            return result;
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
        public static double Produce(WorldState w, Camp c, CraftRecipe recipe, string role, double workerIntervals, double intervals, double usefulDemand, ref double equipmentIntervals, TurnRecord record, ToolCoverageBudget coverage = null, HousingMaterialPlan housing = null)
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
                housing = MaterialEconomy.PlanHousingMaterials(w, c, Recipes(w.Config), planned, intervals * w.Config.BaseDays / w.Config.DaysPerYear, w.Days);
            }
            // The shared budget limits finished output.  The legacy equipment
            // interval argument is retained for callers but is no longer a
            // second, worker-count capacity gate.
            double labor = workerIntervals;
            double amount = Math.Min(usefulDemand, recipe.Equipment ? coverage.Capacity(recipe.Good, rate, labor) : labor * rate);
            var inv = c.Inventory;
            if (recipe.Stone > 0) amount = Math.Min(amount, inv.Stone / recipe.Stone);
            if (recipe.Clay > 0) amount = Math.Min(amount, inv.Clay / recipe.Clay);
            if (recipe.Wood > 0) amount = Math.Min(amount, inv.Wood / recipe.Wood);
            if (recipe.PlantFood > 0) amount = Math.Min(amount, c.PlantFood / recipe.PlantFood);
            if (recipe.AnimalFood > 0) amount = Math.Min(amount, c.AnimalFood / recipe.AnimalFood);
            if (recipe.AnimalFabric > 0) amount = Math.Min(amount, Math.Max(0, inv.AnimalFabric) / recipe.AnimalFabric);
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
            double used = recipe.Equipment ? coverage.Spend(recipe.Good, rate, amount) : amount / rate;
            // Spend reports cumulative budget use. Debit only this receipt's
            // delta while keeping the budget shared across all production calls.
            double consumedHand = recipe.Equipment ? coverage.HandToolsConsumed - handBeforeBudget : 0;
            double consumedTools = recipe.Equipment ? coverage.ToolsConsumed - toolsBeforeBudget : 0;
            inv.HandTools -= consumedHand; inv.Tools -= consumedTools;
            inv.Stone -= amount * recipe.Stone; inv.Wood -= wood; inv.Clay -= clay; c.PlantFood -= amount * recipe.PlantFood; c.AnimalFood -= amount * recipe.AnimalFood; inv.AnimalFabric -= amount * recipe.AnimalFabric;
            if (housing != null) housing.CompleteDirect(recipe, amount);
            switch (recipe.Good) { case CraftGood.HandTools: inv.HandTools += amount; break; case CraftGood.Pots: inv.Pots += amount; break; case CraftGood.Cloth: inv.Cloth += amount; break; case CraftGood.FarmTools: inv.FarmTools += amount; break; case CraftGood.Housing: AddFixedHousing(w, c, amount, w.Days + intervals * w.Config.BaseDays); break; case CraftGood.Tools: inv.Tools += amount; break; case CraftGood.AnimalFabric: inv.AnimalFabric += amount; break; case CraftGood.LightHousing: inv.LightHousingUnits += amount; break; case CraftGood.HerdingTools: inv.HerdingTools += amount; break; }
            // Finished equipment becomes available at the next production interval;
            // it cannot supply worker-time retroactively to the interval that made it.
            record.Production.Add(new ProductionReceipt { Camp = c.Id, Good = recipe.Good, Role = role, Output = amount, WorkerIntervals = used, Stone = amount * recipe.Stone, Clay = clay, Wood = wood, PlantFood = amount * recipe.PlantFood, AnimalFood = amount * recipe.AnimalFood, AnimalFabric = amount * recipe.AnimalFabric, EquipmentWorkerIntervals = recipe.Equipment ? used : 0, HandToolsConsumed = consumedHand, ToolsConsumed = consumedTools });
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
                foreach (string role in WorkforceRoles.Specialists.Concat(new[] { WorkforceRoles.Craftsperson }))
                {
                    double labor = CultivationEconomy.Workers(c, role) * intervals;
                    foreach (var recipe in recipes.OrderByDescending(r => Priority(c, r.Good)).ThenBy(r => r.Good))
                    {
                        if (recipe.HusbandryOnly && !CultivationEconomy.HusbandryEnabled(c) || recipe.AgricultureOnly && !CultivationEconomy.Enabled(c)) continue;
                        if (Priority(c, recipe.Good) <= 0) continue;
                        double target = needs?.Production != null
                            ? Math.Max(0, needs.Production[recipe.Good] - (produced.TryGetValue(recipe.Good, out double made) ? made : 0))
                            : Demand(w, c, recipe.Good, years, -1, true, true);
                        int before = record.Production.Count;
                        labor -= Produce(w, c, recipe, role, labor, intervals, target, ref equipment, record, coverage, housing);
                        if (record.Production.Count > before)
                            produced[recipe.Good] = (produced.TryGetValue(recipe.Good, out double prior) ? prior : 0) + record.Production[record.Production.Count - 1].Output;
                    }
                }
            }
            ResolveNeeds(w, c, years, residents);
        }
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
            c.QoL = c.FoodNeedsMetRatio * c.WaterNeedsMetRatio * (n.RudimentaryWeightClothing * clothing + n.RudimentaryWeightHousing * housing);
            c.FissionWilling = 0;
        }
    }
}
