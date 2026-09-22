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
        [System.Runtime.Serialization.OptionalField] public double HandToolsConsumed, ToolsConsumed, CopperToolsConsumed;
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
            double output = 0;
            foreach (int grade in good == CraftGood.Housing ? new[] { 2, 1, 0 } : new[] { 0, 1, 2 })
            {
                double multiplier = good == CraftGood.Housing ? housingMultipliers[grade] : 1;
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
            double used = 0;
            foreach (int grade in good == CraftGood.Housing ? new[] { 2, 1, 0 } : new[] { 0, 1, 2 })
            {
                double multiplier = good == CraftGood.Housing ? housingMultipliers[grade] : 1;
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
        // Compatibility wrapper for callers that resolve both agricultural work
        // and pot depreciation before forecasting production.
        public static double Demand(WorldState w, Camp c, CraftGood good, double years, double residentPopulation = -1, bool postAgriculturalResolution = false)
            => Demand(w, c, good, years, residentPopulation, postAgriculturalResolution, postAgriculturalResolution);

        // Field work is resolved before growth planning, while Pots depreciate
        // later in ResolveCampNeolithic. Keep those chronology boundaries separate.
        public static double Demand(WorldState w, Camp c, CraftGood good, double years, double residentPopulation, bool fieldWorkAlreadyResolved, bool potsAlreadyDecayed)
        {
            var n = w.Config.Neolithic;
            double residents = residentPopulation < 0 ? c.Population : residentPopulation;
            if (good == CraftGood.HandTools || good == CraftGood.Tools)
                return ToolReplacementDemand(w, c, good, years, residents, fieldWorkAlreadyResolved, potsAlreadyDecayed);
            if (good == CraftGood.Pots)
                return potsAlreadyDecayed ? PotDemandFromCurrentStock(w, c, years, residents) : PotDemand(w, c, years, residents);
            double target = good == CraftGood.Cloth ? residents * n.FabricConsumptionPerPersonTurn * ((years + n.UsefulFinishedGoodsReserveYears) / n.ReferenceTurnYears) :
                good == CraftGood.Housing ? residents / n.HousingPeoplePerUnit :
                good == CraftGood.FarmTools ? FarmToolDemand(w, c, years, fieldWorkAlreadyResolved) + Stock(c, good) :
                c.FarmToolsTarget;
            return Math.Max(0, target - Stock(c, good));
        }
        public static double FarmToolDemand(WorldState w, Camp c, double years, bool fieldWorkAlreadyResolved = false)
        {
            double forecast = CultivationEconomy.ForecastFarmToolWorkerYears(w, c, years) / w.Config.Neolithic.FarmToolFarmerYearsPerUnit;
            double stock = Math.Max(0, c.Inventory.FarmTools);
            if (fieldWorkAlreadyResolved) return Math.Max(0, forecast - stock);
            double expectedUse = Math.Min(stock, forecast);
            return Math.Max(0, forecast - Math.Max(0, stock - expectedUse));
        }
        private static double ToolReplacementDemand(WorldState w, Camp c, CraftGood good, double years, double residents, bool fieldWorkAlreadyResolved, bool potsAlreadyDecayed)
        {
            var n = w.Config.Neolithic;
            var recipes = Recipes(w.Config).Where(r => r.Equipment).OrderByDescending(r => Priority(c, r.Good)).ThenBy(r => r.Good).ToArray();
            double MaterialPriority(MaterialId material) => material == MaterialId.Stone ? c.StonePriority : material == MaterialId.Clay ? c.ClayPriority : c.WoodPriority;
            var room = MaterialEconomy.Materials.ToDictionary(m => m, m => MaterialEconomy.Inventory(c, m) +
                (MaterialPriority(m) > 0 && n.SpecialistMaterialExtractionRate > 0 ? MaterialEconomy.KnownSources(w, c, w.Days).Where(s => s.Material == m).Sum(s => s.Available) : 0));
            double plant = c.PlantFood, useful = 0, copperOutput = 0;
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
            var planned = recipes.ToDictionary(r => r.Good, r => 0d);
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
                    if (recipe.ConstructionMaterial > 0) amount = Math.Min(amount, (room[MaterialId.Wood] + room[MaterialId.Clay]) / recipe.ConstructionMaterial);
                    if (recipe.PlantFood > 0) amount = Math.Min(amount, plant / recipe.PlantFood);
                    if (amount <= 1e-12) continue;
                    availableLabor -= amount / rate;
                    planned[recipe.Good] += amount;
                    useful += amount;
                    double copperRate = rate * (recipe.Good == CraftGood.Housing ? n.CopperToolsHousingMultiplier : 1);
                    double copperAmount = Math.Min(amount, copperLabor * copperRate);
                    copperOutput += copperAmount;
                    copperLabor -= copperRate > 0 ? copperAmount / copperRate : 0;
                    room[MaterialId.Stone] -= amount * recipe.Stone; room[MaterialId.Clay] -= amount * recipe.Clay; room[MaterialId.Wood] -= amount * recipe.Wood;
                    if (recipe.ConstructionMaterial > 0)
                    {
                        double construction = amount * recipe.ConstructionMaterial;
                        double wood = Math.Min(room[MaterialId.Wood], construction);
                        room[MaterialId.Wood] -= wood; room[MaterialId.Clay] -= construction - wood;
                    }
                    plant -= amount * recipe.PlantFood;
                }
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
            var n = w.Config.Neolithic;
            if (good == CraftGood.Pots) return potsAlreadyDecayed ? PotDemandFromCurrentStock(w, c, years, residents) : PotDemand(w, c, years, residents);
            double target = good == CraftGood.Cloth ? residents * n.FabricConsumptionPerPersonTurn * ((years + n.UsefulFinishedGoodsReserveYears) / n.ReferenceTurnYears) :
                good == CraftGood.Housing ? residents / n.HousingPeoplePerUnit :
                good == CraftGood.FarmTools ? FarmToolDemand(w, c, years, fieldWorkAlreadyResolved) + Stock(c, good) :
                c.FarmToolsTarget;
            return Math.Max(0, target - Stock(c, good));
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
        public static double FabricFeedstockNeed(WorldState w, Camp c, double intervals, double years, double residentPopulation = -1, bool fieldWorkAlreadyResolved = false, bool potsAlreadyDecayed = false)
        {
            if (!CultivationEconomy.Enabled(c) || c.Population <= 0 || c.CraftingPriorities.Cloth <= 0) return 0;
            var coverage = new ToolCoverageBudget(w, c, intervals);
            var planned = MaterialEconomy.Forecast(w, c, Recipes(w.Config), intervals, years, coverage.Remaining, true, residentPopulation, fieldWorkAlreadyResolved, potsAlreadyDecayed);
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
            if (recipe.ConstructionMaterial > 0) amount = Math.Min(amount, (inv.Wood + inv.Clay) / recipe.ConstructionMaterial);
            if (amount <= 1e-12) return 0;
            double wood = amount * recipe.Wood, clay = amount * recipe.Clay;
            if (recipe.ConstructionMaterial > 0) { double materials = amount * recipe.ConstructionMaterial; wood += Math.Min(inv.Wood, materials); clay += materials - Math.Min(inv.Wood, materials); }
            double handBeforeBudget = recipe.Equipment ? coverage.HandToolsConsumed : 0;
            double toolsBeforeBudget = recipe.Equipment ? coverage.ToolsConsumed : 0;
            double used = recipe.Equipment ? coverage.Spend(recipe.Good, rate, amount) : amount / rate;
            // Spend reports cumulative budget use. Debit only this receipt's
            // delta while keeping the budget shared across all production calls.
            double consumedHand = recipe.Equipment ? coverage.HandToolsConsumed - handBeforeBudget : 0;
            double consumedTools = recipe.Equipment ? coverage.ToolsConsumed - toolsBeforeBudget : 0;
            inv.HandTools -= consumedHand; inv.Tools -= consumedTools;
            inv.Stone -= amount * recipe.Stone; inv.Wood -= wood; inv.Clay -= clay; c.PlantFood -= amount * recipe.PlantFood;
            switch (recipe.Good) { case CraftGood.HandTools: inv.HandTools += amount; break; case CraftGood.Pots: inv.Pots += amount; break; case CraftGood.Cloth: inv.Cloth += amount; break; case CraftGood.FarmTools: inv.FarmTools += amount; break; case CraftGood.Housing: inv.HousingUnits += amount; break; case CraftGood.Tools: inv.Tools += amount; break; }
            // Finished equipment becomes available at the next production interval;
            // it cannot supply worker-time retroactively to the interval that made it.
            record.Production.Add(new ProductionReceipt { Camp = c.Id, Good = recipe.Good, Role = role, Output = amount, WorkerIntervals = used, Stone = amount * recipe.Stone, Clay = clay, Wood = wood, PlantFood = amount * recipe.PlantFood, EquipmentWorkerIntervals = recipe.Equipment ? used : 0, HandToolsConsumed = consumedHand, ToolsConsumed = consumedTools });
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
            DepreciatePots(w, c, years);
            if (CultivationEconomy.Enabled(c))
            {
                var recipes = Recipes(w.Config);
                double equipment = (c.Inventory.HandTools + c.Inventory.Tools + (c.People.HasTech(NeolithicTech.CopperWorking) ? c.Inventory.CopperTools : 0)) * intervals / w.Config.Neolithic.ToolCoveragePerWorker;
                var coverage = new ToolCoverageBudget(w, c, intervals);
                MaterialEconomy.Gather(w, c, recipes, intervals, years, gatheringWorkerIntervals, ref equipment, record, true, true);
                foreach (string role in WorkforceRoles.Specialists.Concat(new[] { WorkforceRoles.Craftsperson }))
                {
                    double labor = CultivationEconomy.Workers(c, role) * intervals;
                    foreach (var recipe in recipes.OrderByDescending(r => Priority(c, r.Good)).ThenBy(r => r.Good))
                    {
                        if (Priority(c, recipe.Good) <= 0) continue;
                        labor -= Produce(w, c, recipe, role, labor, intervals, Demand(w, c, recipe.Good, years, -1, true, true), ref equipment, record, coverage);
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
