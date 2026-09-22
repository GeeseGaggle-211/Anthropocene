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
    public enum CraftGood { HandTools, Pots, Cloth, FarmTools, Housing, Tools }
    public sealed class CraftRecipe
    {
        public CraftGood Good;
        public string Specialist, Track;
        public double Rate, Stone, Clay, Wood, PlantFood, ConstructionMaterial;
        public bool Equipment, SpecialistOnly;
    }
    [Serializable]
    public sealed class ProductionReceipt
    {
        public int Camp;
        public CraftGood Good;
        public string Role;
        public double Output, WorkerIntervals, Stone, Clay, Wood, PlantFood, EquipmentWorkerIntervals;
    }
    public sealed class ToolCoverageBudget
    {
        private readonly double[] remaining, housingMultipliers;
        public double Remaining => remaining.Sum();
        public ToolCoverageBudget(WorldState w, Camp c, double intervals, double limit = double.PositiveInfinity)
        {
            var n = w.Config.Neolithic;
            remaining = new[] { c.Inventory.HandTools, c.Inventory.Tools, c.People.HasTech(NeolithicTech.CopperWorking) ? c.Inventory.CopperTools : 0 }.Select(v => v * intervals / n.ToolCoveragePerWorker).ToArray();
            double scale = Remaining > 0 ? Math.Min(1, limit / Remaining) : 0;
            for (int i = 0; i < remaining.Length; i++) remaining[i] *= scale;
            housingMultipliers = new[] { n.HandToolsHousingMultiplier, n.ImprovedToolsHousingMultiplier, n.CopperToolsHousingMultiplier };
        }
        public double Capacity(CraftGood good, double rate, double labor)
        {
            double output = 0;
            foreach (int grade in good == CraftGood.Housing ? new[] { 2, 1, 0 } : new[] { 0, 1, 2 })
            {
                double multiplier = good == CraftGood.Housing ? housingMultipliers[grade] : 1;
                if (multiplier <= 0) continue;
                double time = Math.Min(remaining[grade], labor); output += time * rate * multiplier; labor -= time;
            }
            return output;
        }
        public double Spend(CraftGood good, double rate, double output)
        {
            double used = 0;
            foreach (int grade in good == CraftGood.Housing ? new[] { 2, 1, 0 } : new[] { 0, 1, 2 })
            {
                double multiplier = good == CraftGood.Housing ? housingMultipliers[grade] : 1;
                if (multiplier <= 0) continue;
                double time = Math.Min(remaining[grade], Math.Max(0, output) / (rate * multiplier));
                remaining[grade] -= time; output -= time * rate * multiplier; used += time;
            }
            return used;
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
                new CraftRecipe { Good = CraftGood.Cloth, Specialist = WorkforceRoles.Weaver, Track = NeolithicTech.Weaving, Rate = n.WeaverFabricOutputPerBaseDays, PlantFood = n.PlantFoodPerFabricUnit, Equipment = true },
                new CraftRecipe { Good = CraftGood.FarmTools, Specialist = WorkforceRoles.Carpenter, Track = NeolithicTech.Carpentry, Rate = n.FarmToolsOutput, Wood = n.WoodPerFarmTool, Equipment = true },
                new CraftRecipe { Good = CraftGood.Housing, Specialist = WorkforceRoles.Builder, Track = NeolithicTech.Construction, Rate = n.BuilderHousingOutputPerBaseDays, ConstructionMaterial = n.HousingMaterialUnitsPerHousing, Equipment = true },
                new CraftRecipe { Good = CraftGood.Tools, Specialist = WorkforceRoles.Toolmaker, Track = NeolithicTech.Toolmaking, Rate = n.ToolsOutput, Stone = n.StonePerTool, Wood = n.WoodPerTool, SpecialistOnly = true }
            };
        }
        public static double Stock(Camp c, CraftGood good) => good == CraftGood.HandTools ? c.Inventory.HandTools : good == CraftGood.Pots ? c.Inventory.Pots : good == CraftGood.Cloth ? c.Inventory.Cloth : good == CraftGood.FarmTools ? c.Inventory.FarmTools : good == CraftGood.Housing ? c.Inventory.HousingUnits : c.Inventory.Tools;
        public static double Priority(Camp c, CraftGood good) => good == CraftGood.HandTools ? c.CraftingPriorities.HandTools : good == CraftGood.Pots ? c.CraftingPriorities.Pots : good == CraftGood.Cloth ? c.CraftingPriorities.Cloth : good == CraftGood.FarmTools ? c.CraftingPriorities.FarmTools : good == CraftGood.Housing ? c.CraftingPriorities.Housing : c.CraftingPriorities.Tools;
        public static double Demand(WorldState w, Camp c, CraftGood good, double years, double residentPopulation = -1)
        {
            var n = w.Config.Neolithic;
            double residents = residentPopulation < 0 ? c.Population : residentPopulation;
            double craftWorkers = c.Workforce.Assignments.Where(a => a.Role == WorkforceRoles.Craftsperson || a.Role == WorkforceRoles.Potter || a.Role == WorkforceRoles.Weaver || a.Role == WorkforceRoles.Carpenter || a.Role == WorkforceRoles.Builder).Sum(a => a.Population);
            // Automation may bootstrap equipment for the next feasible production plan;
            // manufacturing still cannot use newly made tools retroactively.
            craftWorkers = Math.Max(craftWorkers, Math.Min(c.Population, c.EconomicLabor?.EquipmentTarget ?? 0));
            double usableCopper = c.People.HasTech(NeolithicTech.CopperWorking) ? c.Inventory.CopperTools : 0;
            double target = good == CraftGood.Cloth ? residents * n.FabricConsumptionPerPersonTurn * (years / n.ReferenceTurnYears + n.UsefulFinishedGoodsReserveTurns) :
                good == CraftGood.Housing ? residents / n.HousingPeoplePerUnit :
                good == CraftGood.HandTools ? Math.Max(0, craftWorkers * n.ToolCoveragePerWorker - c.Inventory.Tools - usableCopper) :
                good == CraftGood.Tools ? Math.Max(0, craftWorkers * n.ToolCoveragePerWorker - usableCopper) :
                good == CraftGood.Pots ? c.PotsTarget : c.FarmToolsTarget;
            return Math.Max(0, target - Stock(c, good));
        }
        public static double StorageCapacityMultiplier(Camp c) => 1;
        public static double FabricFeedstockNeed(WorldState w, Camp c, double intervals, double years, double residentPopulation = -1)
        {
            if (!CultivationEconomy.Enabled(c) || c.Population <= 0 || c.CraftingPriorities.Cloth <= 0) return 0;
            var coverage = new ToolCoverageBudget(w, c, intervals);
            var planned = MaterialEconomy.Forecast(w, c, Recipes(w.Config), intervals, years, coverage.Remaining, true, residentPopulation);
            return planned[CraftGood.Cloth] * w.Config.Neolithic.PlantFoodPerFabricUnit;
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
        public static void DepreciateHousing(WorldState w, Camp c, double years)
        {
            var n = w.Config.Neolithic; double before = c.Inventory.HousingUnits;
            c.Inventory.HousingUnits *= Math.Pow(1 - n.HousingDecayPerTurn, years / n.ReferenceTurnYears);
            c.HousingDemand = Math.Max(0, c.Population / n.HousingPeoplePerUnit - c.Inventory.HousingUnits);
            c.HousingReplacementDemand = Math.Min(before - c.Inventory.HousingUnits, c.HousingDemand);
            c.HousingGrowthDemand = Math.Max(0, c.HousingDemand - c.HousingReplacementDemand);
        }
        public static double Produce(WorldState w, Camp c, CraftRecipe recipe, string role, double workerIntervals, double intervals, double usefulDemand, ref double equipmentIntervals, TurnRecord record, ToolCoverageBudget coverage = null)
        {
            if (!CultivationEconomy.Enabled(c) || workerIntervals <= 0 || recipe.Rate <= 0 || usefulDemand <= 0 || intervals <= 0) return 0;
            bool specialist = role == recipe.Specialist && c.People.HasTech(recipe.Track);
            if (!specialist && (role != WorkforceRoles.Craftsperson || recipe.SpecialistOnly)) return 0;
            double rate = recipe.Rate * (specialist ? 1 : w.Config.Neolithic.GeneralistTaskEfficiency);
            if (rate <= 0) return 0;
            coverage = coverage ?? new ToolCoverageBudget(w, c, intervals, equipmentIntervals);
            double labor = recipe.Equipment ? Math.Min(workerIntervals, equipmentIntervals) : workerIntervals;
            double amount = Math.Min(usefulDemand, recipe.Equipment ? coverage.Capacity(recipe.Good, rate, labor) : labor * rate);
            var inv = c.Inventory;
            if (recipe.Stone > 0) amount = Math.Min(amount, inv.Stone / recipe.Stone);
            if (recipe.Clay > 0) amount = Math.Min(amount, inv.Clay / recipe.Clay);
            if (recipe.Wood > 0) amount = Math.Min(amount, inv.Wood / recipe.Wood);
            if (recipe.PlantFood > 0) amount = Math.Min(amount, c.PlantFood / recipe.PlantFood);
            if (recipe.ConstructionMaterial > 0) amount = Math.Min(amount, (inv.Wood + inv.Clay) / recipe.ConstructionMaterial);
            if (amount <= 1e-12) return 0;
            double wood = amount * recipe.Wood, clay = amount * recipe.Clay;
            if (recipe.ConstructionMaterial > 0) { double materials = amount * recipe.ConstructionMaterial; wood += Math.Min(inv.Wood, materials); clay += materials - Math.Min(inv.Wood, materials); }
            double used = recipe.Equipment ? coverage.Spend(recipe.Good, rate, amount) : amount / rate;
            inv.Stone -= amount * recipe.Stone; inv.Wood -= wood; inv.Clay -= clay; c.PlantFood -= amount * recipe.PlantFood;
            switch (recipe.Good) { case CraftGood.HandTools: inv.HandTools += amount; break; case CraftGood.Pots: inv.Pots += amount; break; case CraftGood.Cloth: inv.Cloth += amount; break; case CraftGood.FarmTools: inv.FarmTools += amount; break; case CraftGood.Housing: inv.HousingUnits += amount; break; case CraftGood.Tools: inv.Tools += amount; break; }
            if (recipe.Equipment) equipmentIntervals -= used;
            // Finished equipment becomes available at the next production interval;
            // it cannot supply worker-time retroactively to the interval that made it.
            record.Production.Add(new ProductionReceipt { Camp = c.Id, Good = recipe.Good, Role = role, Output = amount, WorkerIntervals = used, Stone = amount * recipe.Stone, Clay = clay, Wood = wood, PlantFood = amount * recipe.PlantFood, EquipmentWorkerIntervals = recipe.Equipment ? used : 0 });
            var demographic = record.Demographics.FirstOrDefault(d => d.Camp == c.Id);
            if (demographic != null) demographic.ManufacturingFood += amount * recipe.PlantFood;
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
        public static void ResolveCampNeolithic(WorldState w, Camp c, double intervals, double years, TurnRecord record, double gatheringWorkerIntervals = 0)
        {
            if (c.Population <= 0) return;
            DepreciateHousing(w, c, years);
            if (CultivationEconomy.Enabled(c))
            {
                var recipes = Recipes(w.Config);
                double equipment = (c.Inventory.HandTools + c.Inventory.Tools + (c.People.HasTech(NeolithicTech.CopperWorking) ? c.Inventory.CopperTools : 0)) * intervals / w.Config.Neolithic.ToolCoveragePerWorker;
                var coverage = new ToolCoverageBudget(w, c, intervals);
                MaterialEconomy.Gather(w, c, recipes, intervals, years, gatheringWorkerIntervals, ref equipment, record);
                foreach (string role in WorkforceRoles.Specialists.Concat(new[] { WorkforceRoles.Craftsperson }))
                {
                    double labor = CultivationEconomy.Workers(c, role) * intervals;
                    foreach (var recipe in recipes.OrderByDescending(r => Priority(c, r.Good)).ThenBy(r => r.Good))
                    {
                        if (Priority(c, recipe.Good) <= 0) continue;
                        labor -= Produce(w, c, recipe, role, labor, intervals, Demand(w, c, recipe.Good, years), ref equipment, record, coverage);
                    }
                }
            }
            ResolveNeeds(w, c, years);
        }
        public static void ResolveNeeds(WorldState w, Camp c, double years)
        {
            var n = w.Config.Neolithic;
            double required = c.Population * n.FabricConsumptionPerPersonTurn * years / n.ReferenceTurnYears;
            c.FabricConsumed = Math.Min(required, c.Inventory.Cloth); c.Inventory.Cloth -= c.FabricConsumed;
            c.ClothPerCapita = required > 0 ? c.FabricConsumed / required : 1;
            c.HousingPerCapita = c.Population > 0 ? Math.Min(1, c.Inventory.HousingUnits * n.HousingPeoplePerUnit / c.Population) : 1;
            c.StationaryYears += years;
            c.RudimentaryExpectation = n.RudimentaryExpectationFloor + (1 - n.RudimentaryExpectationFloor) * Numbers.Clamp(c.StationaryYears / n.RudimentaryExpectationRampYears, 0, 1);
            double clothing = 1 - c.RudimentaryExpectation * (1 - c.ClothPerCapita), housing = 1 - c.RudimentaryExpectation * (1 - c.HousingPerCapita);
            c.QoL = c.FoodNeedsMetRatio * c.WaterNeedsMetRatio * (n.RudimentaryWeightClothing * clothing + n.RudimentaryWeightHousing * housing);
            c.FissionWilling = 0;
        }
    }
}
