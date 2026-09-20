using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    // Pure, deterministic policy. Projections are planning envelopes, never permission
    // to create food, materials, equipment, people or technology in the resolver.
    public static class EconomicPlanner
    {
        private const double Epsilon = 1e-8;
        private const int SearchIterations = 24; // Numerical convergence, not a balance coefficient.
        private sealed class Proposal
        {
            public EconomicIntent Intent;
            public int Workers, FoodGatherers, OperatingFarmers;
        }
        public static EconomicIntent Plan(WorldState w, Camp c, EconomicIntent settings)
        {
            var e = settings.Copy(); e.Assignments.Clear(); e.Labor = null;
            if (!CultivationEconomy.Enabled(c) || c.Population <= 0) { e.AutomationTurns = 0; return e; }
            var cfg = w.Config; var n = cfg.Neolithic;
            double intervals = cfg.GenerationIntervals, years = cfg.TurnDays / cfg.DaysPerYear;
            var visible = Knowledge.Visible(w, c.Player);
            var footprint = CultivationEconomy.Footprint(w, c).Where(visible.Contains).ToArray();
            if (!footprint.Contains(e.Focus)) e.Focus = -1;
            var operated = footprint.Where(id => w.Tiles[id].CultivationOperator < 0 || w.Tiles[id].CultivationOperator == c.Id).ToArray();
            double upkeep = operated.Sum(id => { CultivationEconomy.Factors(cfg, w.Tiles[id], out _, out double m); return LazyEcology.ProjectEcology(w, id, w.Days).CultivationCapital * n.MaintenanceWorkersPerTileE(cfg.MaintenanceCoefficient) * m; });
            bool established = operated.Any(id => LazyEcology.ProjectEcology(w, id, w.Days).CultivationCapital > 0);
            var eligible = w.Map.Radius(c.Tile, cfg.GatherRadius).Where(id => visible.Contains(id) && PassiveGathering.Eligible(w, c, id)).OrderBy(id => id).ToArray();
            double FoodAvailability(int id) => LazyEcology.AccessibleAvailability(w, id, w.Days, cfg.TurnDays, operated.Contains(id) ? 1 : 0);
            double supply = eligible.Sum(FoodAvailability), plantSupply = eligible.Where(id => w.Tiles[id].Land).Sum(FoodAvailability);
            double biological = TurnResolver.PlanningFoodDemand(w, c);
            double nextPopulation = TurnResolver.PlanningResidentPopulation(w, c, Math.Min(biological, supply));
            var sources = MaterialEconomy.KnownSources(w, c, w.Days);
            double Priority(MaterialId m) => m == MaterialId.Stone ? e.StonePriority : m == MaterialId.Clay ? e.ClayPriority : e.WoodPriority;
            var accessible = MaterialEconomy.Materials.ToDictionary(m => m, m => Priority(m) <= 0 || n.SpecialistMaterialExtractionRate <= 0 ? 0 : sources.Where(s => s.Material == m).GroupBy(s => s.Id).Sum(g => g.Sum(s => s.Available)));
            var recipes = NeolithicProduction.Recipes(cfg);
            var preview = Preview(c, e, nextPopulation);
            NeolithicProduction.DepreciateHousing(w, preview, years);
            var goods = recipes.Where(r => r.Good != CraftGood.HandTools && r.Good != CraftGood.Tools).ToArray();
            var goals = goods.ToDictionary(r => r.Good, r => NeolithicProduction.Priority(preview, r.Good) > 0 ? NeolithicProduction.Demand(w, preview, r.Good, years) : 0);
            double sparePlant = Math.Min(c.PlantFood, Math.Max(0, c.Food - c.Population * cfg.Consumption * intervals * (1 + cfg.GrowthReserveTurns)));
            int consumers = c.Population + w.Hunters.Where(h => h.SourceCamp == c.Id && Supply.Connected(w, h)).Sum(h => h.Population);
            double plantEnvelope = Math.Min(plantSupply + sparePlant, Math.Max(0, c.Food + supply - consumers * cfg.Consumption * intervals));

            Proposal Evaluate(double fulfillment)
            {
                var intent = e.Copy(); intent.Labor = new EconomicLaborPlan();
                var candidate = Preview(c, intent, nextPopulation);
                NeolithicProduction.DepreciateHousing(w, candidate, years);
                var room = MaterialEconomy.Materials.ToDictionary(m => m, m => MaterialEconomy.Inventory(c, m) + accessible[m]);
                double plant = plantEnvelope;
                var requested = recipes.ToDictionary(r => r.Good, r => 0d);
                var futureRoles = new Dictionary<string, double>();
                string Role(CraftRecipe r) => c.People.HasTech(r.Track) ? r.Specialist : WorkforceRoles.Craftsperson;
                double Rate(CraftRecipe r) => r.Rate * (c.People.HasTech(r.Track) ? 1 : n.GeneralistTaskEfficiency);
                void Add(Dictionary<string, double> roles, string role, double workers) { if (workers > Epsilon) roles[role] = (roles.TryGetValue(role, out double old) ? old : 0) + workers; }
                double Capped(CraftRecipe r, double amount, bool debit)
                {
                    if (r.SpecialistOnly && !c.People.HasTech(r.Track) || Rate(r) <= 0) return 0;
                    if (r.Stone > 0) amount = Math.Min(amount, room[MaterialId.Stone] / r.Stone);
                    if (r.Clay > 0) amount = Math.Min(amount, room[MaterialId.Clay] / r.Clay);
                    if (r.Wood > 0) amount = Math.Min(amount, room[MaterialId.Wood] / r.Wood);
                    if (r.ConstructionMaterial > 0) amount = Math.Min(amount, (room[MaterialId.Wood] + room[MaterialId.Clay]) / r.ConstructionMaterial);
                    if (r.PlantFood > 0) amount = Math.Min(amount, plant / r.PlantFood);
                    if (debit)
                    {
                        room[MaterialId.Stone] -= amount * r.Stone; room[MaterialId.Clay] -= amount * r.Clay; room[MaterialId.Wood] -= amount * r.Wood;
                        double construction = amount * r.ConstructionMaterial, wood = Math.Min(room[MaterialId.Wood], construction);
                        room[MaterialId.Wood] -= wood; room[MaterialId.Clay] -= construction - wood; plant -= amount * r.PlantFood;
                    }
                    return Math.Max(0, amount);
                }
                // Size next-cycle equipment against useful, source-feasible jobs, not
                // against the population or a fixed craft quota. Housing keeps its own rate.
                foreach (var r in goods.OrderByDescending(r => NeolithicProduction.Priority(candidate, r.Good)).ThenBy(r => r.Good))
                {
                    double amount = Capped(r, goals[r.Good] * fulfillment, true);
                    requested[r.Good] = amount;
                    double multiplier = r.Good == CraftGood.Housing ? NeolithicProduction.HousingMultiplier(w, c, c.Population) : 1;
                    if (multiplier > 0) Add(futureRoles, Role(r), amount / (Rate(r) * multiplier * intervals));
                }
                intent.Labor.EquipmentTarget = Math.Min(c.Population, futureRoles.Values.Sum(v => Math.Ceiling(v - Epsilon)));
                candidate.EconomicLabor = intent.Labor;
                var rolesNow = new Dictionary<string, double>();
                var coverage = new ToolCoverageBudget(w, c, intervals);
                room = MaterialEconomy.Materials.ToDictionary(m => m, m => MaterialEconomy.Inventory(c, m) + accessible[m]); plant = plantEnvelope;
                // Match production's role/recipe ordering and share one tool-time budget.
                foreach (string role in WorkforceRoles.Specialists.Concat(new[] { WorkforceRoles.Craftsperson }))
                    foreach (var r in recipes.OrderByDescending(r => NeolithicProduction.Priority(candidate, r.Good)).ThenBy(r => r.Good))
                    {
                        if (Role(r) != role || NeolithicProduction.Priority(candidate, r.Good) <= 0) continue;
                        double wanted = r.Good == CraftGood.HandTools || r.Good == CraftGood.Tools ? NeolithicProduction.Demand(w, candidate, r.Good, years) : requested[r.Good];
                        // Prefer improved coverage once unlocked, without making redundant hand tools.
                        if (r.Good == CraftGood.HandTools && c.People.HasTech(NeolithicTech.Toolmaking) && e.Priorities.Tools > 0 && accessible[MaterialId.Wood] + c.Inventory.Wood > 0) wanted = 0;
                        double amount = Capped(r, wanted, false);
                        if (r.Equipment) amount = Math.Min(amount, coverage.Capacity(r.Good, Rate(r), c.Population * intervals));
                        amount = Capped(r, amount, true);
                        double work = r.Equipment ? coverage.Spend(r.Good, Rate(r), amount) : Rate(r) > 0 ? amount / Rate(r) : 0;
                        Add(rolesNow, role, work / intervals);
                    }
                foreach (var job in rolesNow.OrderBy(p => p.Key, StringComparer.Ordinal))
                    intent.Assignments.Add(new PopulationAssignment { Role = job.Key, Population = (int)Math.Ceiling(job.Value - Epsilon) });
                candidate.Workforce.Assignments = intent.Assignments;
                // Reforecast rounded staffing through the actual recipe planner. This
                // catches shared generalist time, mixed inputs and reserve replenishment.
                var outputs = MaterialEconomy.Forecast(w, candidate, recipes, intervals, years, new ToolCoverageBudget(w, c, intervals).Remaining, true);
                var raw = MaterialEconomy.Materials.ToDictionary(m => m, m => 0d);
                double constructionDemand = 0;
                foreach (var r in recipes)
                {
                    raw[MaterialId.Stone] += outputs[r.Good] * r.Stone;
                    raw[MaterialId.Clay] += outputs[r.Good] * r.Clay;
                    raw[MaterialId.Wood] += outputs[r.Good] * r.Wood;
                    constructionDemand += outputs[r.Good] * r.ConstructionMaterial;
                }
                double horizon = 1 + n.MaterialReserveHorizonTurns;
                foreach (var m in MaterialEconomy.Materials) raw[m] = Math.Max(0, raw[m] * horizon - MaterialEconomy.Inventory(c, m));
                // Existing wood/clay reserves substitute once, never once per source.
                double spareWood = Math.Max(0, c.Inventory.Wood - outputs.Sum(p => recipes.First(r => r.Good == p.Key).Wood * p.Value) * horizon);
                double spareClay = Math.Max(0, c.Inventory.Clay - outputs.Sum(p => recipes.First(r => r.Good == p.Key).Clay * p.Value) * horizon);
                double materials = Math.Max(0, constructionDemand * horizon - spareWood - spareClay);
                foreach (var m in new[] { MaterialId.Wood, MaterialId.Clay }.OrderByDescending(m => c.People.HasTech(EconomicIntents.RequiredTechnology(MaterialEconomy.Specialist(m)))).ThenByDescending(Priority).ThenBy(m => m))
                { double take = Math.Min(materials, Math.Max(0, accessible[m] - raw[m])); raw[m] += take; materials -= take; }
                double generalistMaterials = 0;
                foreach (var m in MaterialEconomy.Materials)
                {
                    double amount = Math.Min(accessible[m], raw[m]);
                    if (amount <= Epsilon || n.SpecialistMaterialExtractionRate <= 0) continue;
                    string role = MaterialEconomy.Specialist(m); bool specialist = c.People.HasTech(EconomicIntents.RequiredTechnology(role));
                    double workers = amount / (intervals * n.SpecialistMaterialExtractionRate * (specialist ? 1 : n.GeneralistTaskEfficiency));
                    if (specialist) intent.Assignments.Add(new PopulationAssignment { Role = role, Population = (int)Math.Ceiling(workers - Epsilon) }); else generalistMaterials += workers;
                }
                double feedstock = Math.Max(0, outputs[CraftGood.Cloth] * n.PlantFoodPerFabricUnit - sparePlant);
                double foodTarget = Math.Min(supply, biological + feedstock);
                double farmRate = cfg.FarmerProductivity * cfg.GatherPerPerson * intervals;
                int operating = established ? Math.Min(c.Population, (int)Math.Ceiling(Math.Max(upkeep, cfg.FarmerProductivity >= 1 ? foodTarget / farmRate : 0) - Epsilon)) : 0;
                double farmerHarvest = Math.Min(foodTarget, operating * farmRate);
                int foodGatherers = Math.Min(c.Population - operating, (int)Math.Ceiling(Math.Max(0, foodTarget - farmerHarvest) / (cfg.GatherPerPerson * intervals) - Epsilon));
                int gatherers = (int)Math.Ceiling(foodGatherers + generalistMaterials - Epsilon);
                intent.Labor.MaterialGatherers = generalistMaterials;
                if (operating > 0) intent.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Farmer, Population = operating });
                return new Proposal { Intent = intent, OperatingFarmers = operating, FoodGatherers = foodGatherers, Workers = intent.Assignments.Sum(a => a.Population) + Math.Max(0, gatherers) };
            }
            // Allocate as much useful manufacturing as fits alongside food and its
            // complete source chain. No separate craft-population percentage is imposed.
            var best = Evaluate(0); var full = Evaluate(1);
            if (full.Workers <= c.Population) best = full;
            else
            {
                double low = 0, high = 1;
                for (int i = 0; i < SearchIterations; i++) { double mid = (low + high) / 2; var p = Evaluate(mid); if (p.Workers <= c.Population) { low = mid; best = p; } else high = mid; }
            }
            int spare = Math.Max(0, c.Population - best.Workers);
            // The capital scoring horizon is not a mandate to feed a hypothetical
            // century of exponential growth. Expand for the next demand forecast.
            double futureDemand = biological * nextPopulation / c.Population + goals[CraftGood.Cloth] * n.PlantFoodPerFabricUnit;
            bool expand = futureDemand > supply && operated.Any(id => ExpansionGain(w, id) > Epsilon);
            if (spare > 0 && expand)
            {
                var farmer = best.Intent.Assignments.FirstOrDefault(a => a.Role == WorkforceRoles.Farmer);
                if (farmer == null) best.Intent.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Farmer, Population = spare }); else farmer.Population += spare;
                best.Intent.Labor.ExpansionFarmers = spare;
            }
            best.Intent.Assignments = best.Intent.Assignments.OrderBy(a => a.Role, StringComparer.Ordinal).ToList();
            return best.Intent;
        }
        private static double ExpansionGain(WorldState w, int id)
        {
            var cfg = w.Config; var n = cfg.Neolithic; var tile = w.Tiles[id];
            var before = LazyEcology.ProjectEcology(w, id, w.Days); var after = before;
            double q = CultivationEconomy.Intensity(cfg, before.CultivationCapital);
            if (q >= 1) return 0;
            after.CultivationCapital = Math.Min(1 / (cfg.ReferenceIntensity * cfg.ReferenceIntensity), before.CultivationCapital + n.AllocationStepE);
            double next = CultivationEconomy.Intensity(cfg, after.CultivationCapital);
            if (Vegetation.IsWooded(tile) && next > cfg.GathererBridgeCeiling)
            {
                double pressure = Math.Max(0, next - Math.Max(q, cfg.GathererBridgeCeiling)) * n.ClearingConversionPressure;
                after.LandCondition = EcologyMath.Condition(after.LandCondition, 1, 0, pressure, 1);
                after.CurrentVegetation = Vegetation.Update(tile.NaturalVegetationPotential, after.CurrentVegetation, after.LandCondition, cfg);
            }
            double gain = 0;
            for (int season = 0; season < 4; season++)
            {
                double Flow(EcologyView v)
                {
                    var k = new EcologyCapacity(w, tile, v, season, 1);
                    // Existing ecological thresholds bound the sustainable benefit of
                    // further investment; this is not a hard q cap or a new yield bonus.
                    double sustainable = k.Biomass * k.BiomassRate * cfg.SustainableProductionFraction * cfg.SafeUseRatio;
                    return Math.Min(k.Food * k.FoodRate + k.ManagedFlow, sustainable);
                }
                gain += Flow(after) - Flow(before);
            }
            return gain;
        }
        private static Camp Preview(Camp c, EconomicIntent intent, double population)
        {
            var result = new Camp { Id = c.Id, Tile = c.Tile, Population = (int)Math.Ceiling(population), PlantFood = c.PlantFood, AnimalFood = c.AnimalFood, AquaticFood = c.AquaticFood,
                Inventory = new CampInventory { HandTools = c.Inventory.HandTools, Tools = c.Inventory.Tools, CopperTools = c.Inventory.CopperTools, Cloth = c.Inventory.Cloth, HousingUnits = c.Inventory.HousingUnits, Pots = c.Inventory.Pots, FarmTools = c.Inventory.FarmTools, Stone = c.Inventory.Stone, Clay = c.Inventory.Clay, Wood = c.Inventory.Wood } };
            result.Bind(c.People); EconomicIntents.Apply(result, intent); return result;
        }
        public static void ResolveEnabled(WorldState w, TurnRecord record)
        {
            foreach (var c in w.Camps.Where(c => c.Population > 0 && c.EconomyAutomationTurns != 0 && CultivationEconomy.Enabled(c)).OrderBy(c => c.Id))
            {
                var intent = Plan(w, c, EconomicIntent.From(c));
                var observation = new AgentObservationBuilder().Build(w, c.Player);
                var order = new Order(OrderKind.Economy, c.Id) { Economy = intent };
                string error = OrderRules.Validate(observation, order);
                if (error != null) throw new InvalidOperationException("Invalid automatic economic plan: " + error);
                EconomicIntents.Apply(c, intent); record.Orders.Add(c.Player + ":automation:" + order + ":accepted");
                if (c.EconomyAutomationTurns > 0) c.EconomyAutomationTurns--;
            }
        }
    }
}
