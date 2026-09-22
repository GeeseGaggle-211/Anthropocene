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
            double upkeep = CultivationEconomy.MaintenanceFarmersForPlanning(w, c, years);
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
            // FarmTools are sized only after this proposal has a bounded field
            // workforce.  The empty preview cannot answer that question without
            // inventing labor, so the normal FarmTools demand helper is called
            // below on the resolver-shaped candidate.
            if (goals.ContainsKey(CraftGood.FarmTools)) goals[CraftGood.FarmTools] = 0;
            double sparePlant = Math.Min(c.PlantFood, Math.Max(0, c.Food - c.Population * cfg.Consumption * intervals * (1 + cfg.GrowthReserveTurns)));
            int consumers = c.Population + w.Hunters.Where(h => h.SourceCamp == c.Id && Supply.Connected(w, h)).Sum(h => h.Population);
            double plantEnvelope = Math.Min(plantSupply + sparePlant, Math.Max(0, c.Food + supply - consumers * cfg.Consumption * intervals));

            Proposal Evaluate(double fulfillment)
            {
                var intent = e.Copy(); intent.Labor = new EconomicLaborPlan();
                var candidate = Preview(c, intent, nextPopulation);
                NeolithicProduction.DepreciateHousing(w, candidate, years);
                var room = MaterialEconomy.Materials.ToDictionary(m => m, m => MaterialEconomy.Inventory(c, m) + accessible[m]);
                double initialWood = room[MaterialId.Wood], initialClay = room[MaterialId.Clay];
                double directWood = 0, directClay = 0;
                double plant = plantEnvelope;
                var requested = recipes.ToDictionary(r => r.Good, r => 0d);
                var futureRoles = new Dictionary<string, double>();
                int proposedFarmers = 0;
                double proposedExpansion = 0;
                var farmRecipe = goods.FirstOrDefault(r => r.Good == CraftGood.FarmTools);
                string Role(CraftRecipe r) => c.People.HasTech(r.Track) ? r.Specialist : WorkforceRoles.Craftsperson;
                double Rate(CraftRecipe r) => r.Rate * (c.People.HasTech(r.Track) ? 1 : n.GeneralistTaskEfficiency);
                void Add(Dictionary<string, double> roles, string role, double workers) { if (workers > Epsilon) roles[role] = (roles.TryGetValue(role, out double old) ? old : 0) + workers; }
                double Capped(CraftRecipe r, double amount, bool debit)
                {
                    if (r.SpecialistOnly && !c.People.HasTech(r.Track) || Rate(r) <= 0) return 0;
                    if (r.Stone > 0) amount = Math.Min(amount, room[MaterialId.Stone] / r.Stone);
                    if (r.Clay > 0) amount = Math.Min(amount, room[MaterialId.Clay] / r.Clay);
                    if (r.Wood > 0) amount = Math.Min(amount, room[MaterialId.Wood] / r.Wood);
                    if (r.ConstructionMaterial > 0)
                    {
                        MaterialEconomy.HousingShares(initialWood, initialClay, directWood, directClay, out double woodShare, out double clayShare);
                        if (woodShare + clayShare <= 0) amount = 0;
                        else amount = Math.Min(amount, (room[MaterialId.Wood] + room[MaterialId.Clay]) / r.ConstructionMaterial);
                    }
                    if (r.PlantFood > 0) amount = Math.Min(amount, plant / r.PlantFood);
                    if (debit)
                    {
                        room[MaterialId.Stone] -= amount * r.Stone; room[MaterialId.Clay] -= amount * r.Clay; room[MaterialId.Wood] -= amount * r.Wood;
                        if (r.ConstructionMaterial <= 0)
                        {
                            directWood += amount * r.Wood;
                            directClay += amount * r.Clay;
                        }
                        double construction = amount * r.ConstructionMaterial;
                        MaterialEconomy.HousingShares(initialWood, initialClay, directWood, directClay, out double housingWoodShare, out double housingClayShare);
                        double wood = Math.Min(room[MaterialId.Wood], construction * housingWoodShare);
                        double clay = Math.Min(room[MaterialId.Clay], construction - wood);
                        wood += Math.Min(room[MaterialId.Wood] - wood, construction - wood - clay);
                        room[MaterialId.Wood] -= wood; room[MaterialId.Clay] -= clay; plant -= amount * r.PlantFood;
                    }
                    return Math.Max(0, amount);
                }
                // All direct-input work reserves its own material before flexible
                // construction. FarmTools remains deferred until field intent is
                // bounded, but no Housing priority can spend a Wood/Clay input
                // that another feasible recipe cannot substitute.
                foreach (var r in goods.Where(r => r.Good != CraftGood.FarmTools && r.ConstructionMaterial <= 0).OrderByDescending(r => NeolithicProduction.Priority(candidate, r.Good)).ThenBy(r => r.Good))
                {
                    double amount = Capped(r, goals[r.Good] * fulfillment, true);
                    requested[r.Good] = amount;
                    double multiplier = r.Good == CraftGood.Housing ? NeolithicProduction.HousingMultiplier(w, c, c.Population) : 1;
                    if (multiplier > 0) Add(futureRoles, Role(r), amount / (Rate(r) * multiplier * intervals));
                }
                // Build one resolver-shaped candidate: material-feasible jobs first,
                // then the residual field workforce. The shared FarmTools demand
                // helper can now see both maintenance and intended expansion.
                if (farmRecipe != null && NeolithicProduction.Priority(candidate, CraftGood.FarmTools) > 0)
                {
                    double projectedDemand = biological * nextPopulation / Math.Max(1, c.Population) + goals[CraftGood.Cloth] * n.PlantFoodPerFabricUnit;
                    bool intendedExpansion = projectedDemand > supply && operated.Any(id => ExpansionGain(w, id) > Epsilon);
                    double roleWorkers = futureRoles.Values.Sum(v => Math.Ceiling(v - Epsilon));
                    // Reserve the food-gathering share before proposing field
                    // expansion. Otherwise a full resident ledger can appear
                    // to support both expansion and the food/material chain,
                    // and fulfillment search cannot distinguish the infeasible
                    // manufacturing case.
                    double fieldFoodTarget = Math.Min(supply, biological + goals[CraftGood.Cloth] * n.PlantFoodPerFabricUnit);
                    double gatherRate = cfg.GatherPerPerson * intervals;
                    int foodReserve = !established && gatherRate > 0
                        ? (int)Math.Ceiling(Math.Max(0, fieldFoodTarget) / gatherRate - Epsilon)
                        : 0;
                    double fieldWorkers = Math.Max(0, c.Population - roleWorkers - foodReserve);
                    double maintenance = CultivationEconomy.PlannedMaintenanceFarmers(w, c, fieldWorkers);
                    double plannedFarmRate = cfg.FarmerProductivity * cfg.GatherPerPerson * intervals;
                    double plannedFoodTarget = Math.Min(supply, biological + goals[CraftGood.Cloth] * n.PlantFoodPerFabricUnit);
                    double harvest = established && plannedFarmRate > 0 ? Math.Ceiling(Math.Max(maintenance, plannedFoodTarget / plannedFarmRate) - Epsilon) : 0;
                    double plannedOperating = established ? Math.Min(fieldWorkers, harvest) : 0;
                    double expansion = intendedExpansion ? Math.Max(0, fieldWorkers - plannedOperating) : 0;
                    proposedFarmers = (int)Math.Ceiling(plannedOperating + expansion - Epsilon);
                    if (proposedFarmers > 0)
                        candidate.Workforce.Assignments = futureRoles.OrderBy(p => p.Key, StringComparer.Ordinal)
                            .Select(p => new PopulationAssignment { Role = p.Key, Population = (int)Math.Ceiling(p.Value - Epsilon) })
                            .Concat(new[] { new PopulationAssignment { Role = WorkforceRoles.Farmer, Population = proposedFarmers } }).ToList();
                    proposedExpansion = Math.Min(expansion, proposedFarmers);
                    intent.Labor.ExpansionFarmers = proposedExpansion;
                    candidate.EconomicLabor = intent.Labor;
                    // This is the sole field-tool need calculation. It includes
                    // current stock's expected wear and never uses FarmToolsTarget.
                    double desired = NeolithicProduction.Demand(w, candidate, CraftGood.FarmTools, years) * fulfillment;
                    // Reserve the source-feasible prospective job before applying
                    // the current tool budget. This lets the shared replacement
                    // helper bootstrap Hand Tools for a first FarmTools cycle.
                    double prospective = Capped(farmRecipe, desired, false);
                    double manufacturingCapacity = new ToolCoverageBudget(w, c, intervals).Capacity(CraftGood.FarmTools, Rate(farmRecipe), c.Population * intervals);
                    double additional = Math.Min(prospective, manufacturingCapacity);
                    additional = Capped(farmRecipe, additional, true);
                    requested[CraftGood.FarmTools] = additional;
                    // Keep a prospective, material-feasible producer visible to
                    // shared Hand/Regular replacement demand before coverage is
                    // resolved. Actual output is still limited by that coverage.
                    if (prospective > Epsilon)
                    {
                        double producerWorkers = prospective / (Rate(farmRecipe) * intervals);
                        Add(futureRoles, Role(farmRecipe), producerWorkers);
                        if (intendedExpansion)
                        {
                            int producerReservation = (int)Math.Ceiling(producerWorkers - Epsilon);
                            proposedFarmers = Math.Max(0, proposedFarmers - producerReservation);
                            proposedExpansion = Math.Max(0, Math.Min(proposedExpansion, proposedFarmers));
                        }
                    }
                }
                // Construction is the final flexible material consumer.  Its
                // provisional mix uses this same post-direct room; final
                // gathering and resolver debits use MaterialEconomy's fixed plan.
                foreach (var r in goods.Where(r => r.Good != CraftGood.FarmTools && r.ConstructionMaterial > 0).OrderByDescending(r => NeolithicProduction.Priority(candidate, r.Good)).ThenBy(r => r.Good))
                {
                    double amount = Capped(r, goals[r.Good] * fulfillment, true);
                    requested[r.Good] = amount;
                    double multiplier = r.Good == CraftGood.Housing ? NeolithicProduction.HousingMultiplier(w, c, c.Population) : 1;
                    if (multiplier > 0) Add(futureRoles, Role(r), amount / (Rate(r) * multiplier * intervals));
                }
                // Keep the prospective producer reservation visible while the
                // shared Hand/Regular replacement demand is forecast.  It is
                // replaced with the actual rounded jobs below before returning
                // the proposal.
                intent.Labor.EquipmentTarget = Math.Min(c.Population, futureRoles.Values.Sum(v => Math.Ceiling(v - Epsilon)));
                candidate.EconomicLabor = intent.Labor;
                // The equipment target is a staffing reservation, not a second
                // authority.  Seed the pure preview with the already source- and
                // demand-limited future roles so replacement demand can bootstrap
                // the first tool cycle without changing persisted assignments.
                candidate.Workforce.Assignments = futureRoles.OrderBy(p => p.Key, StringComparer.Ordinal)
                    .Select(p => new PopulationAssignment { Role = p.Key, Population = (int)Math.Ceiling(p.Value - Epsilon) }).ToList();
                if (proposedFarmers > 0)
                    candidate.Workforce.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Farmer, Population = proposedFarmers });
                var rolesNow = new Dictionary<string, double>();
                var coverage = new ToolCoverageBudget(w, c, intervals);
                room = MaterialEconomy.Materials.ToDictionary(m => m, m => MaterialEconomy.Inventory(c, m) + accessible[m]);
                // The provisional output pass above only sizes field and
                // construction intent. Rebuild the final role pass from fresh
                // stock so direct inputs are reserved exactly once.
                directWood = directClay = 0;
                plant = plantEnvelope;
                // Match production's role/recipe ordering and share one tool-time budget.
                foreach (string role in WorkforceRoles.Specialists.Concat(new[] { WorkforceRoles.Craftsperson }))
                    foreach (var r in recipes.OrderByDescending(r => NeolithicProduction.Priority(candidate, r.Good)).ThenBy(r => r.Good))
                    {
                        if (Role(r) != role || NeolithicProduction.Priority(candidate, r.Good) <= 0) continue;
                        double wanted = r.Good == CraftGood.HandTools || r.Good == CraftGood.Tools ? NeolithicProduction.Demand(w, candidate, r.Good, years) : requested[r.Good];
                        double amount = Capped(r, wanted, false);
                        if (r.Equipment) amount = Math.Min(amount, coverage.Capacity(r.Good, Rate(r), c.Population * intervals));
                        amount = Capped(r, amount, true);
                        double work = r.Equipment ? coverage.Spend(r.Good, Rate(r), amount) : Rate(r) > 0 ? amount / Rate(r) : 0;
                        Add(rolesNow, role, work / intervals);
                    }
                foreach (var job in rolesNow.OrderBy(p => p.Key, StringComparer.Ordinal))
                    intent.Assignments.Add(new PopulationAssignment { Role = job.Key, Population = (int)Math.Ceiling(job.Value - Epsilon) });
                // Keep EquipmentTarget prospective through the shared replacement
                // forecast. A zero current Hand/Regular-tool budget can suppress
                // this turn's manufacture, while the material-feasible role still
                // needs to bootstrap the next useful cycle.
                // Forecast against the actual manufacturing jobs plus the
                // bounded field proposal. Keep this candidate list separate so
                // adding material/food jobs below cannot mutate the returned
                // intent or duplicate its farmers.
                candidate.Workforce.Assignments = intent.Assignments
                    .Select(a => new PopulationAssignment { Role = a.Role, Population = a.Population }).ToList();
                int candidateNonFarmWorkers = candidate.Workforce.Assignments.Sum(a => a.Population);
                int candidateFieldWorkers = Math.Min(Math.Max(0, c.Population - candidateNonFarmWorkers), Math.Max(0, proposedFarmers));
                if (candidateFieldWorkers > 0)
                    candidate.Workforce.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Farmer, Population = candidateFieldWorkers });
                // Reforecast rounded staffing through the actual recipe planner. This
                // catches shared generalist time, mixed inputs and reserve replenishment.
                var outputs = MaterialEconomy.Forecast(w, candidate, recipes, intervals, years, new ToolCoverageBudget(w, c, intervals).Remaining, true);
                var housing = MaterialEconomy.PlanHousingMaterials(w, c, recipes, outputs, years, w.Days);
                var raw = MaterialEconomy.Materials.ToDictionary(m => m, m => 0d);
                foreach (var r in recipes)
                {
                    if (r.ConstructionMaterial > 0) continue;
                    raw[MaterialId.Stone] += outputs[r.Good] * r.Stone;
                    raw[MaterialId.Clay] += outputs[r.Good] * r.Clay;
                    raw[MaterialId.Wood] += outputs[r.Good] * r.Wood;
                }
                double horizon = n.MaterialReserveMultiplier(years);
                double construction = outputs[CraftGood.Housing] * recipes.Single(r => r.Good == CraftGood.Housing).ConstructionMaterial;
                raw[MaterialId.Wood] += construction * housing.WoodShare;
                raw[MaterialId.Clay] += construction * housing.ClayShare;
                foreach (var m in MaterialEconomy.Materials) raw[m] = Math.Max(0, raw[m] * horizon - MaterialEconomy.Inventory(c, m));
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
                int nonFarmWorkers = intent.Assignments.Where(a => a.Role != WorkforceRoles.Farmer).Sum(a => a.Population);
                int availableFarmers = Math.Max(0, c.Population - nonFarmWorkers);
                int requiredOperating = established && farmRate > 0
                    ? (int)Math.Ceiling(Math.Max(upkeep, cfg.FarmerProductivity >= 1 ? foodTarget / farmRate : 0) - Epsilon)
                    : 0;
                int operating = Math.Min(availableFarmers, Math.Max(0, requiredOperating));
                double farmerHarvest = Math.Min(foodTarget, operating * farmRate);
                int availableGatherers = Math.Max(0, c.Population - nonFarmWorkers - operating);
                int requiredFoodGatherers = cfg.GatherPerPerson * intervals > 0
                    ? (int)Math.Ceiling(Math.Max(0, foodTarget - farmerHarvest) / (cfg.GatherPerPerson * intervals) - Epsilon)
                    : 0;
                int foodGatherers = Math.Min(availableGatherers, Math.Max(0, requiredFoodGatherers));
                // Material gathering shares the remaining unassigned gatherer
                // time with food.  Never leave a fractional reservation after
                // manufacturing has consumed all residents.
                double materialGatherers = Math.Min(Math.Max(0, generalistMaterials),
                    Math.Max(0, availableGatherers - foodGatherers));
                int gatherers = Math.Min(availableGatherers,
                    (int)Math.Ceiling(foodGatherers + materialGatherers - Epsilon));
                intent.Labor.MaterialGatherers = materialGatherers;
                if (operating > 0) intent.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Farmer, Population = operating });
                // Keep the same bounded field proposal in the material and food
                // forecast. Dropping it here would make FarmTool demand vanish
                // and would omit the corresponding Wood/Hand-tool chain.
                int finalNonFarmWorkers = intent.Assignments.Where(a => a.Role != WorkforceRoles.Farmer).Sum(a => a.Population);
                int proposedFieldWorkers = Math.Min(Math.Max(0, c.Population - finalNonFarmWorkers), Math.Max(0, proposedFarmers));
                var forecastLabor = intent.Labor.Copy();
                forecastLabor.ExpansionFarmers = Math.Min(Math.Max(0, proposedExpansion), proposedFieldWorkers);
                candidate.EconomicLabor = forecastLabor;
                // Match the returned reservation to the final resident farmer
                // assignment. The outer spare-worker expansion may increase it
                // later, but a rejected proposal cannot leave phantom labor.
                int finalFarmers = intent.Assignments.Where(a => a.Role == WorkforceRoles.Farmer).Sum(a => a.Population);
                intent.Labor.ExpansionFarmers = Math.Min(Math.Max(0, proposedExpansion), finalFarmers);
                // Score the full labor envelope during fulfillment search. The
                // final assignment remains resident-bounded, but a partially
                // fulfilled manufacturing plan must not become feasible merely
                // by dropping required maintenance, food, or material work.
                int requiredGatherers = Math.Max(0,
                    (int)Math.Ceiling(Math.Max(0, requiredFoodGatherers + generalistMaterials) - Epsilon));
                int laborEnvelope = nonFarmWorkers + Math.Max(0, requiredOperating) + requiredGatherers;
                int actualWorkers = intent.Assignments.Sum(a => a.Population) + Math.Max(0, gatherers);
                int totalWorkers = fulfillment <= Epsilon ? actualWorkers : Math.Max(actualWorkers, laborEnvelope);
                return new Proposal { Intent = intent, OperatingFarmers = operating, FoodGatherers = foodGatherers, Workers = totalWorkers };
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
