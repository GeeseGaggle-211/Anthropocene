using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Anthropocene.Simulation
{
    // Pure, deterministic policy. Projections are planning envelopes, never permission
    // to create food, materials, equipment, people or technology in the resolver.
    public static class EconomicPlanner
    {
        internal const double Epsilon = 1e-8;
        private const int SearchIterations = 6;
        private sealed class Proposal
        {
            public EconomicIntent Intent;
            public int Workers, FoodGatherers, OperatingFarmers;
            // Path position this proposal was evaluated at (for the reported labour price).
            public double S;
        }
        // The fulfilment rule used by automated plans. Alternatives are
        // evaluated through the rule parameter before they become defaults.
        internal static readonly IFulfilmentRule DefaultRule = UniformFulfilment.Instance;
        // Rule named by the configuration: Uniform stays the default; Value
        // builds the value rule from the configured curve steepness.
        internal static IFulfilmentRule ConfiguredRule(SimulationConfig cfg)
            => cfg.Neolithic.ValueFulfilmentSelected ? new ValueFulfilment(cfg.Neolithic.ValueCurveSteepness) : DefaultRule;
        public static EconomicIntent Plan(WorldState w, Camp c, EconomicIntent settings)
            => Plan(w, c, settings, ConfiguredRule(w.Config));
        internal static EconomicIntent Plan(WorldState w, Camp c, EconomicIntent settings, IFulfilmentRule rule)
            => Plan(w, c, settings, rule, out _);
        // laborPrice is the labour price w at the chosen path position (NaN for
        // rules without one). Transient diagnostic: never saved, observed or hashed.
        internal static EconomicIntent Plan(WorldState w, Camp c, EconomicIntent settings, IFulfilmentRule rule, out double laborPrice)
        {
            laborPrice = double.NaN;
            Interlocked.Increment(ref TurnDiagnostics.For(w).PlanCalls);
            var e = settings.Copy(); e.Assignments.Clear(); e.Labor = null;
            if (c.Population <= 0 || !CultivationEconomy.EconomyEnabled(c)) { e.AutomationTurns = 0; return e; }
            var cfg = w.Config; var n = cfg.Neolithic;
            double intervals = cfg.GenerationIntervals, years = cfg.TurnDays / cfg.DaysPerYear;
            // Demand pass: every fulfilment-invariant input, built once.
            var demand = CampProductionDemand.Build(w, c, e);
            bool cultivationEnabled = demand.CultivationEnabled, husbandryEnabled = demand.HusbandryEnabled, established = demand.Established;
            double upkeep = demand.Upkeep, supply = demand.Supply, biological = demand.Biological, nextPopulation = demand.NextPopulation;
            double forageAfterHerds = demand.ForageAfterHerds, terrestrialAfterHerds = demand.TerrestrialAfterHerds;
            int reservedHerdersForForecast = demand.ReservedHerders, consumers = demand.Consumers;
            var managedFood = demand.ManagedFood; var sources = demand.Sources; var accessible = demand.Accessible;
            var recipes = demand.Recipes; var goods = demand.Goods; var goals = demand.Goals; var preview = demand.Preview;
            double sparePlant = demand.SparePlant, plantEnvelope = demand.PlantEnvelope, animalEnvelope = demand.AnimalEnvelope;
            double foodRoomEnvelope = demand.FoodRoomEnvelope, housingMultiplier = demand.HousingMultiplier;
            bool HasRetainedExpansionGain() => demand.HasRetainedExpansionGain();
            var valueRule = rule as ValueFulfilment;
            var claimIndex = new Dictionary<CraftGood, int>();
            for (int i = 0; i < demand.Claims.Count; i++) claimIndex[demand.Claims[i].Good] = i;

            // Staffing pass: turns the rule's shares at path position s into one
            // resolver-shaped proposal and its full labor requirement.
            Proposal Evaluate(double s)
            {
                var shares = new double[demand.Claims.Count];
                rule.Shares(demand.Claims, s, shares);
                double Share(CraftGood good) => shares[claimIndex[good]];
                var intent = e.Copy(); intent.Labor = new EconomicLaborPlan();
                // Managed-herd work is a resident reservation, not an output
                // appended after the agricultural proposal.  Put it on the
                // resolver-shaped candidate before craft, food, material, and
                // field feasibility are evaluated so every later worker budget
                // sees the same bounded resident pool.
                int reservedHerders = reservedHerdersForForecast;
                if (reservedHerders > 0)
                    intent.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Herder, Population = reservedHerders });
                var candidate = Preview(c, intent, nextPopulation);
                NeolithicProduction.DepreciateHousing(w, candidate, years);
                double candidateLightHousing = candidate.Inventory.LightHousingUnits;
                if (husbandryEnabled) { candidate.PlantFood = plantEnvelope; candidate.AnimalFood = animalEnvelope; }
                if (husbandryEnabled && NeolithicProduction.Priority(preview, CraftGood.LightHousing) > 0 && c.Population > reservedHerders)
                    candidate.Workforce.Assignments.Add(new PopulationAssignment { Role = c.People.HasTech(NeolithicTech.Construction) ? WorkforceRoles.Builder : WorkforceRoles.Craftsperson, Population = 1 });
                var room = MaterialEconomy.Materials.ToDictionary(m => m, m => MaterialEconomy.Inventory(c, m) + accessible[m]);
                double initialWood = room[MaterialId.Wood], initialClay = room[MaterialId.Clay];
                double directWood = 0, directClay = 0;
                double housingWoodShare = 0, housingClayShare = 0;
                bool finalRolePass = false;
                double plant = plantEnvelope;
                double animal = animalEnvelope;
                double animalFabric = Math.Max(0, c.Inventory.AnimalFabric);
                double foodRoom = foodRoomEnvelope;
                var requested = recipes.ToDictionary(r => r.Good, r => 0d);
                var futureRoles = new Dictionary<string, double>();
                int proposedFarmers = 0;
                double proposedExpansion = 0;
                var farmRecipe = goods.FirstOrDefault(r => r.Good == CraftGood.FarmTools);
                string Role(CraftRecipe r) => c.People.HasTech(r.Track) ? r.Specialist : WorkforceRoles.Craftsperson;
                double Rate(CraftRecipe r) => r.Rate * (c.People.HasTech(r.Track) ? 1 : n.GeneralistTaskEfficiency);
                double RemainingGoodDemand(CraftRecipe r)
                {
                    if (NeolithicProduction.Priority(candidate, r.Good) <= 0) return 0;
                    if (r.Good == CraftGood.Cloth || r.Good == CraftGood.AnimalFabric)
                    {
                        candidate.Inventory.Cloth = Math.Max(0, c.Inventory.Cloth) + requested[CraftGood.Cloth];
                        candidate.Inventory.AnimalFabric = Math.Max(0, c.Inventory.AnimalFabric) + requested[CraftGood.AnimalFabric] - requested[CraftGood.LightHousing];
                        candidate.Inventory.LightHousingUnits = candidateLightHousing + requested[CraftGood.LightHousing];
                        return NeolithicProduction.Demand(w, candidate, r.Good, years, -1, false, false, sources);
                    }
                    return goals[r.Good];
                }
                void Add(Dictionary<string, double> roles, string role, double workers) { if (workers > Epsilon) roles[role] = (roles.TryGetValue(role, out double old) ? old : 0) + workers; }
                double Capped(CraftRecipe r, double amount, bool debit)
                {
                    if (r.SpecialistOnly && !c.People.HasTech(r.Track) || Rate(r) <= 0) return 0;
                    if (r.AgricultureOnly && !CultivationEconomy.Enabled(c) || r.HusbandryOnly && !CultivationEconomy.HusbandryEnabled(c)) return 0;
                    if (r.Stone > 0) amount = Math.Min(amount, room[MaterialId.Stone] / r.Stone);
                    if (r.Clay > 0) amount = Math.Min(amount, room[MaterialId.Clay] / r.Clay);
                    if (r.Wood > 0) amount = Math.Min(amount, room[MaterialId.Wood] / r.Wood);
                    if (r.ConstructionMaterial > 0)
                    {
                        double woodShare, clayShare;
                        if (finalRolePass) { woodShare = housingWoodShare; clayShare = housingClayShare; }
                        else MaterialEconomy.HousingShares(initialWood, initialClay, directWood, directClay, out woodShare, out clayShare);
                        if (woodShare + clayShare <= 0) amount = 0;
                        else
                        {
                            double freeWood = Math.Max(0, room[MaterialId.Wood] - (finalRolePass ? directWood : 0));
                            double freeClay = Math.Max(0, room[MaterialId.Clay] - (finalRolePass ? directClay : 0));
                            amount = Math.Min(amount, (freeWood + freeClay) / r.ConstructionMaterial);
                        }
                    }
                    if (r.PlantFood > 0) amount = Math.Min(amount, plant / r.PlantFood);
                    if (r.AnimalFood > 0) amount = Math.Min(amount, animal / r.AnimalFood);
                    if (husbandryEnabled && r.PlantFood + r.AnimalFood > 0) amount = Math.Min(amount, foodRoom / (r.PlantFood + r.AnimalFood));
                    if (r.AnimalFabric > 0) amount = Math.Min(amount, animalFabric / r.AnimalFabric);
                    if (debit)
                    {
                        room[MaterialId.Stone] -= amount * r.Stone; room[MaterialId.Clay] -= amount * r.Clay; room[MaterialId.Wood] -= amount * r.Wood;
                        if (r.ConstructionMaterial <= 0)
                        {
                            if (finalRolePass)
                            {
                                directWood = Math.Max(0, directWood - amount * r.Wood);
                                directClay = Math.Max(0, directClay - amount * r.Clay);
                            }
                            else
                            {
                                directWood += amount * r.Wood;
                                directClay += amount * r.Clay;
                            }
                        }
                        double construction = amount * r.ConstructionMaterial;
                        double currentWoodShare;
                        if (finalRolePass) currentWoodShare = housingWoodShare;
                        else MaterialEconomy.HousingShares(initialWood, initialClay, directWood, directClay, out currentWoodShare, out _);
                        double freeWood = Math.Max(0, room[MaterialId.Wood] - (finalRolePass ? directWood : 0));
                        double freeClay = Math.Max(0, room[MaterialId.Clay] - (finalRolePass ? directClay : 0));
                        double wood = Math.Min(freeWood, construction * currentWoodShare);
                        double clay = Math.Min(freeClay, construction - wood);
                        wood += Math.Min(freeWood - wood, construction - wood - clay);
                        clay += Math.Min(freeClay - clay, construction - wood - clay);
                        room[MaterialId.Wood] -= wood; room[MaterialId.Clay] -= clay; plant -= amount * r.PlantFood;
                        animal -= amount * r.AnimalFood;
                        foodRoom -= amount * (r.PlantFood + r.AnimalFood);
                        animalFabric += r.Good == CraftGood.AnimalFabric ? amount : 0;
                        animalFabric -= amount * r.AnimalFabric;
                    }
                    return Math.Max(0, amount);
                }
                // Material access order: priority under the uniform rule; under the
                // Value rule, marginal value V * exp(-lambda * c) at the current
                // share, so a scarce input goes to the good worth most per unit.
                // Goods without a valued claim follow after, by priority. Animal
                // Fabric always precedes Light Housing, which consumes it.
                IEnumerable<CraftRecipe> AccessOrder(IEnumerable<CraftRecipe> list)
                {
                    if (valueRule == null) return list.OrderByDescending(r => NeolithicProduction.Priority(candidate, r.Good)).ThenBy(r => r.Good);
                    double Marginal(CraftRecipe r)
                    {
                        var claim = demand.Claims[claimIndex[r.Good]];
                        return ValueFulfilment.IsNeed(claim) ? valueRule.MarginalValue(claim, shares[claimIndex[r.Good]]) : double.NaN;
                    }
                    var ordered = list.OrderBy(r => double.IsNaN(Marginal(r)) ? 1 : 0)
                        .ThenByDescending(r => double.IsNaN(Marginal(r)) ? NeolithicProduction.Priority(candidate, r.Good) : Marginal(r))
                        .ThenBy(r => r.Good).ToList();
                    int fabric = ordered.FindIndex(r => r.Good == CraftGood.AnimalFabric), shelter = ordered.FindIndex(r => r.Good == CraftGood.LightHousing);
                    if (fabric > shelter && shelter >= 0) { var item = ordered[fabric]; ordered.RemoveAt(fabric); ordered.Insert(shelter, item); }
                    return ordered;
                }
                // All direct-input work reserves its own material before flexible
                // construction. FarmTools remains deferred until field intent is
                // bounded, but no Housing priority can spend a Wood/Clay input
                // that another feasible recipe cannot substitute.
                foreach (var r in AccessOrder(goods.Where(r => r.Good != CraftGood.FarmTools && r.ConstructionMaterial <= 0)))
                {
                    double amount = Capped(r, RemainingGoodDemand(r) * Share(r.Good), true);
                    requested[r.Good] = amount;
                    double multiplier = (r.Good == CraftGood.Housing || r.Good == CraftGood.LightHousing) ? housingMultiplier : 1;
                    if (multiplier > 0) Add(futureRoles, Role(r), amount / (Rate(r) * multiplier * intervals));
                }
                // Demand probes carry virtual recipe output only for ordering.
                // Restore the actual candidate stock before equipment and raw
                // material replacement forecasts so they cannot mistake planned
                // output for inventory already on hand.
                candidate.Inventory.Cloth = c.Inventory.Cloth;
                candidate.Inventory.AnimalFabric = c.Inventory.AnimalFabric;
                candidate.Inventory.LightHousingUnits = candidateLightHousing;
                // Build one resolver-shaped candidate: material-feasible jobs first,
                // then the residual field workforce. The shared FarmTools demand
                // helper can now see both maintenance and intended expansion.
                if (cultivationEnabled && farmRecipe != null && NeolithicProduction.Priority(candidate, CraftGood.FarmTools) > 0)
                {
                    double projectedDemand = biological * nextPopulation / Math.Max(1, c.Population) + goals[CraftGood.Cloth] * n.PlantFoodPerFabricUnit;
                    bool intendedExpansion = projectedDemand > supply && HasRetainedExpansionGain();
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
                    double fieldWorkers = Math.Max(0, c.Population - reservedHerders - roleWorkers - foodReserve);
                    double maintenance = CultivationEconomy.PlannedMaintenanceFarmers(w, c, fieldWorkers);
                    double plannedFarmRate = cfg.FarmerProductivity * cfg.GatherPerPerson * intervals;
                    double plannedFoodTarget = Math.Min(terrestrialAfterHerds, biological + goals[CraftGood.Cloth] * n.PlantFoodPerFabricUnit);
                    double harvest = established && plannedFarmRate > 0 ? Math.Ceiling(Math.Max(maintenance, plannedFoodTarget / plannedFarmRate) - Epsilon) : 0;
                    double plannedOperating = established ? Math.Min(fieldWorkers, harvest) : 0;
                    double expansion = intendedExpansion ? Math.Max(0, fieldWorkers - plannedOperating) : 0;
                    proposedFarmers = (int)Math.Ceiling(plannedOperating + expansion - Epsilon);
                    if (proposedFarmers > 0)
                        candidate.Workforce.Assignments = intent.Assignments
                            .Concat(futureRoles.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new PopulationAssignment { Role = p.Key, Population = (int)Math.Ceiling(p.Value - Epsilon) }))
                            .Concat(new[] { new PopulationAssignment { Role = WorkforceRoles.Farmer, Population = proposedFarmers } }).ToList();
                    proposedExpansion = Math.Min(expansion, proposedFarmers);
                    intent.Labor.ExpansionFarmers = proposedExpansion;
                    candidate.EconomicLabor = intent.Labor;
                    // This is the sole field-tool need calculation. It includes
                    // current stock's expected wear and never uses FarmToolsTarget.
                    double desired = NeolithicProduction.Demand(w, candidate, CraftGood.FarmTools, years, -1, false, false, sources) * Share(CraftGood.FarmTools);
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
                foreach (var r in AccessOrder(goods.Where(r => r.Good != CraftGood.FarmTools && r.ConstructionMaterial > 0)))
                {
                    double amount = Capped(r, goals[r.Good] * Share(r.Good), true);
                    requested[r.Good] = amount;
                    double multiplier = (r.Good == CraftGood.Housing || r.Good == CraftGood.LightHousing) ? housingMultiplier : 1;
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
                candidate.Workforce.Assignments = intent.Assignments.Where(a => a.Role == WorkforceRoles.Herder)
                    .Select(a => new PopulationAssignment { Role = a.Role, Population = a.Population })
                    .Concat(futureRoles.OrderBy(p => p.Key, StringComparer.Ordinal)
                        .Select(p => new PopulationAssignment { Role = p.Key, Population = (int)Math.Ceiling(p.Value - Epsilon) })).ToList();
                if (proposedFarmers > 0)
                    candidate.Workforce.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Farmer, Population = proposedFarmers });
                var rolesNow = new Dictionary<string, double>();
                var coverage = new ToolCoverageBudget(w, c, intervals);
                room = MaterialEconomy.Materials.ToDictionary(m => m, m => MaterialEconomy.Inventory(c, m) + accessible[m]);
                // Rebuild the final role pass from fresh stock while carrying
                // the provisional direct reservation forward. It protects a
                // later specialist job without counting those inputs twice;
                // each realized direct receipt releases its reservation below.
                MaterialEconomy.HousingShares(initialWood, initialClay, directWood, directClay, out housingWoodShare, out housingClayShare);
                finalRolePass = true;
                plant = plantEnvelope;
                animal = animalEnvelope;
                animalFabric = Math.Max(0, c.Inventory.AnimalFabric);
                foodRoom = foodRoomEnvelope;
                // Match production's role/recipe ordering and share one tool-time budget.
                var orderedRecipes = recipes.OrderByDescending(r => NeolithicProduction.Priority(candidate, r.Good)).ThenBy(r => r.Good).ToArray();
                // The candidate is fixed through this pass: one schedule run
                // answers both Hand and Regular Tool replacement demand.
                (double HandTools, double Tools)? toolDemand = null;
                foreach (string role in WorkforceRoles.Specialists.Concat(new[] { WorkforceRoles.Craftsperson }))
                    foreach (var r in orderedRecipes)
                    {
                        if (Role(r) != role || NeolithicProduction.Priority(candidate, r.Good) <= 0) continue;
                        double wanted;
                        if (r.Good == CraftGood.HandTools || r.Good == CraftGood.Tools)
                        {
                            toolDemand ??= NeolithicProduction.ToolReplacementDemands(w, candidate, years, candidate.Population, false, false, sources);
                            wanted = r.Good == CraftGood.HandTools ? toolDemand.Value.HandTools : toolDemand.Value.Tools;
                        }
                        else wanted = requested[r.Good];
                        double amount = Capped(r, wanted, false);
                        if (r.Equipment) amount = Math.Min(amount, coverage.Capacity(r.Good, Rate(r), c.Population * intervals));
                        amount = Capped(r, amount, true);
                        double work = r.Equipment ? coverage.Spend(r.Good, Rate(r), amount) : Rate(r) > 0 ? amount / Rate(r) : 0;
                        Add(rolesNow, role, work / intervals);
                    }
                foreach (var job in rolesNow.OrderBy(p => p.Key, StringComparer.Ordinal))
                    intent.Assignments.Add(new PopulationAssignment { Role = job.Key, Population = (int)Math.Ceiling(job.Value - Epsilon) });
                if (husbandryEnabled)
                {
                    // Preserve task-derived craft roles when current shared
                    // equipment coverage blocks their output this turn. The
                    // resolver uses these ordinary assignments to expose
                    // downstream useful demand (for example, shelter demand
                    // from an assigned Builder); it still produces nothing
                    // without starting tool capacity. Count the reservation in
                    // the same resident budget as every other planned job.
                    foreach (var job in futureRoles.OrderBy(p => p.Key, StringComparer.Ordinal))
                    {
                        int plannedWorkers = (int)Math.Ceiling(job.Value - Epsilon);
                        var assigned = intent.Assignments.FirstOrDefault(a => a.Role == job.Key);
                        if (plannedWorkers <= 0 || assigned != null && assigned.Population >= plannedWorkers) continue;
                        if (assigned == null) intent.Assignments.Add(new PopulationAssignment { Role = job.Key, Population = plannedWorkers });
                        else assigned.Population = plannedWorkers;
                    }
                }
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
                var outputs = MaterialEconomy.Forecast(w, candidate, recipes, intervals, years, new ToolCoverageBudget(w, c, intervals).Remaining, !husbandryEnabled, -1, false, false, -1, -1, sources);
                var housing = MaterialEconomy.PlanHousingMaterials(w, c, recipes, outputs, years, w.Days, sources);
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
                double forecastPlantFeedstock = outputs[CraftGood.Cloth] * n.PlantFoodPerFabricUnit;
                double forecastAnimalFeedstock = outputs[CraftGood.AnimalFabric];
                if (husbandryEnabled)
                {
                    var typedFeedstock = NeolithicProduction.FabricFeedstockInputs(w, candidate, intervals, years, nextPopulation, false, false);
                    forecastPlantFeedstock = Math.Min(forecastPlantFeedstock, typedFeedstock.PlantFood);
                    forecastAnimalFeedstock = Math.Min(forecastAnimalFeedstock, typedFeedstock.AnimalFood);
                }
                double feedstock = Math.Max(0, forecastPlantFeedstock - sparePlant);
                if (husbandryEnabled)
                {
                    double reserve = Math.Min(NeolithicProduction.DesiredReserveFood(w, consumers, intervals), NeolithicProduction.StorageCapacityAfterPotDepreciation(w, c, intervals, years));
                    double spareFood = Math.Max(0, c.Food - consumers * cfg.Consumption * intervals - reserve);
                    double spareAnimal = Math.Min(c.AnimalFood, Math.Max(0, spareFood - sparePlant));
                    feedstock += Math.Max(0, forecastAnimalFeedstock - spareAnimal);
                }
                double foodTarget = Math.Min(forageAfterHerds, Math.Max(0, biological + feedstock - managedFood.AnimalFood));
                double farmRate = cfg.FarmerProductivity * cfg.GatherPerPerson * intervals;
                int nonFarmWorkers = intent.Assignments.Where(a => a.Role != WorkforceRoles.Farmer).Sum(a => a.Population);
                int availableFarmers = Math.Max(0, c.Population - nonFarmWorkers);
                double terrestrialFoodTarget = Math.Min(terrestrialAfterHerds, foodTarget);
                int requiredOperating = established && farmRate > 0
                    ? (int)Math.Ceiling(Math.Max(upkeep, cfg.FarmerProductivity >= 1 ? terrestrialFoodTarget / farmRate : 0) - Epsilon)
                    : 0;
                int operating = Math.Min(availableFarmers, Math.Max(0, requiredOperating));
                double farmerHarvest = Math.Min(terrestrialFoodTarget, operating * farmRate);
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
                int totalWorkers = s <= Epsilon ? actualWorkers : Math.Max(actualWorkers, laborEnvelope);
                return new Proposal { Intent = intent, OperatingFarmers = operating, FoodGatherers = foodGatherers, Workers = totalWorkers, S = s };
            }
            // Allocate as much useful manufacturing as fits alongside food and its
            // complete source chain. No separate craft-population percentage is imposed.
            // Evaluate is pure over its own previews, so the zero-fulfilment
            // fallback is only needed when full fulfilment does not fit.
            var best = Evaluate(1);
            if (best.Workers > c.Population)
            {
                int highWorkers = best.Workers;
                best = Evaluate(0);
                best = SearchPath(Evaluate, best, highWorkers, c.Population);
            }
            int spare = Math.Max(0, c.Population - best.Workers);
            // The capital scoring horizon is not a mandate to feed a hypothetical
            // century of exponential growth. Expand for the next demand forecast.
            double futureDemand = biological * nextPopulation / c.Population + (cultivationEnabled ? goals[CraftGood.Cloth] * n.PlantFoodPerFabricUnit : 0);
            bool expand = cultivationEnabled && futureDemand > supply && HasRetainedExpansionGain();
            if (spare > 0 && expand)
            {
                var farmer = best.Intent.Assignments.FirstOrDefault(a => a.Role == WorkforceRoles.Farmer);
                if (farmer == null) best.Intent.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Farmer, Population = spare }); else farmer.Population += spare;
                best.Intent.Labor.ExpansionFarmers = spare;
            }
            best.Intent.Assignments = best.Intent.Assignments.OrderBy(a => a.Role, StringComparer.Ordinal).ToList();
            if (valueRule != null) laborPrice = valueRule.LaborPrice(demand.Claims, best.S);
            return best.Intent;
        }
        // Largest staffable position on a fulfilment rule's path, to within
        // PathTolerance. Required workers are a nondecreasing step function of
        // the position, feasible at 0 (lowest) and infeasible at 1 (highWorkers).
        // Each round interpolates where the requirement crosses the residents,
        // then probes one tolerance step past that guess on the far side to
        // close the bracket. Rounds after the second halve instead, so a poor
        // interpolation cannot stall. Evaluations stay within SearchIterations.
        private const double PathTolerance = 1.0 / 64;
        private static Proposal SearchPath(Func<double, Proposal> evaluate, Proposal lowest, int highWorkers, int residents)
        {
            var best = lowest;
            if (best.Workers > residents) return best;
            double low = 0, high = 1; int lowWorkers = best.Workers;
            int evaluations = 0;
            bool Probe(double s)
            {
                evaluations++;
                var p = evaluate(s);
                if (p.Workers <= residents) { low = s; lowWorkers = p.Workers; best = p; return true; }
                high = s; highWorkers = p.Workers; return false;
            }
            for (int round = 0; evaluations < SearchIterations && high - low > PathTolerance; round++)
            {
                double guess = round < 2
                    ? low + (high - low) * (residents + .5 - lowWorkers) / Math.Max(1, highWorkers - lowWorkers)
                    : (low + high) / 2;
                guess = Math.Min(high - PathTolerance / 2, Math.Max(low + PathTolerance / 2, guess));
                bool feasible = Probe(guess);
                if (evaluations >= SearchIterations || high - low <= PathTolerance) break;
                Probe(feasible ? low + PathTolerance : high - PathTolerance);
            }
            return best;
        }
        internal static int ManagedHerderReservation(WorldState w, Camp c, double years)
        {
            if (!CultivationEconomy.HusbandryEnabled(c)) return 0;
            return HerdingTooling.RequiredHerders(w, c, years);
        }
        internal static double ExpansionGain(WorldState w, int id)
        {
            var cfg = w.Config; var opening = LazyEcology.ProjectEcology(w, id, w.Days);
            double before = opening.CultivationCapital;
            if (CultivationEconomy.Intensity(cfg, before) >= 1) return 0;
            double after = Math.Min(1 / (cfg.ReferenceIntensity * cfg.ReferenceIntensity),
                before + cfg.Neolithic.AllocationStepE);
            var investment = FoodHorizonForecast.Investment(w, id, w.Days,
                cfg.TurnDays, before, before + .5 * (after - before), after, 1);
            return Math.Max(0, investment.RetainedFoodGain);
        }
        internal static Camp Preview(Camp c, EconomicIntent intent, double population)
        {
            var result = new Camp { Id = c.Id, Tile = c.Tile, Population = (int)Math.Ceiling(population), PlantFood = c.PlantFood, AnimalFood = c.AnimalFood, AquaticFood = c.AquaticFood,
                Inventory = new CampInventory { HandTools = c.Inventory.HandTools, Tools = c.Inventory.Tools, CopperTools = c.Inventory.CopperTools, Cloth = c.Inventory.Cloth, AnimalFabric = c.Inventory.AnimalFabric, LightHousingUnits = c.Inventory.LightHousingUnits, HerdingTools = c.Inventory.HerdingTools, HousingUnits = c.Inventory.HousingUnits, Pots = c.Inventory.Pots, FarmTools = c.Inventory.FarmTools, Stone = c.Inventory.Stone, Clay = c.Inventory.Clay, Wood = c.Inventory.Wood } };
            result.Bind(c.People); EconomicIntents.Apply(result, intent); return result;
        }
    }
}
