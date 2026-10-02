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
            // Herders this proposal staffed (the reservation under Uniform; the labour-price staffing under the Value rule's herd ladder).
            public int Herders;
            // Expansion farmers the Value rule staffed in this proposal (already in Workers).
            public int ExpansionWorkers;
            // Path position this proposal was evaluated at (for the reported labour price).
            public double S;
            public SharedAllocation.Choice Joint;
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
            => Plan(w, c, settings, rule, out laborPrice, out _);
        // productionOrder is the Value rule's fixed production order for this turn
        // (null under Uniform): the resolver reads the same order (WP3b).
        internal static EconomicIntent Plan(WorldState w, Camp c, EconomicIntent settings, IFulfilmentRule rule, out double laborPrice, out ProductionOrder productionOrder, bool expansionClaim = true)
            => Plan(w, c, settings, rule, out laborPrice, out productionOrder, out _, expansionClaim);
        // farmLabourPrice (WP4b) is the value per worker-year of the first expansion ladder step
        // the plan's expansion farmers (staffed plus leftover) do not cover: what a freed farmer-year
        // is worth to the camp this turn. 0 when the ladder is exhausted or no claim exists (and under Uniform).
        internal static EconomicIntent Plan(WorldState w, Camp c, EconomicIntent settings, IFulfilmentRule rule, out double laborPrice, out ProductionOrder productionOrder, out double farmLabourPrice, bool expansionClaim = true)
        {
            laborPrice = double.NaN; productionOrder = null; farmLabourPrice = 0;
            Interlocked.Increment(ref TurnDiagnostics.For(w).PlanCalls);
            long planStart = ResolutionMetrics.CaptureDetailedCounters ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            var e = settings.Copy(); e.Assignments.Clear(); e.Labor = null;
            if (c.Population <= 0 || !CultivationEconomy.EconomyEnabled(c)) { e.AutomationTurns = 0; return e; }
            var cfg = w.Config; var n = cfg.Neolithic;
            double intervals = cfg.GenerationIntervals, years = cfg.TurnDays / cfg.DaysPerYear;
            // Demand pass: every fulfilment-invariant input, built once.
            var demand = CampProductionDemand.Build(w, c, e, expansionClaim, rule is ValueFulfilment);
            bool cultivationEnabled = demand.CultivationEnabled, husbandryEnabled = demand.HusbandryEnabled, established = demand.Established;
            // Legacy grown-herd food closes the field gap; capital mode values the offspring in the herd asset instead (credit 0).
            double upkeep = demand.Upkeep, supply = demand.Supply, gapSupply = demand.Supply + demand.HerdGrowthFood, biological = demand.Biological, nextPopulation = demand.NextPopulation;
            double forageAfterHerds = demand.ForageAfterHerds, terrestrialAfterHerds = demand.TerrestrialAfterHerds;
            int reservedHerdersForForecast = demand.ReservedHerders, consumers = demand.Consumers;
            var managedFood = demand.ManagedFood; var sources = demand.Sources; var accessible = demand.Accessible;
            var recipes = demand.Recipes; var goods = demand.Goods; var goals = demand.Goals; var preview = demand.Preview;
            double housingMultiplier = demand.HousingMultiplier;
            // Clothing-need feedstock alone is funded from food after this turn's meals (before the reserve).
            bool HasRetainedExpansionGain() => demand.HasRetainedExpansionGain();
            var valueRule = rule as ValueFulfilment;
            // Herd-labour coupling (Docs/HerdLabour.md): under the Value rule herders compete for residents on the labour price like every other claim
            // instead of being reserved first. Null (and the unconditional reservation below) otherwise.
            var herdLadder = valueRule != null ? demand.HerdLadder : null;
            long sharedStart = ResolutionMetrics.CaptureDetailedCounters ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            var jointAllocation = valueRule != null && cfg.SharedCropHerdAllocationEnabled
                ? new SharedAllocation(w, c, demand, e.ManagedBreedingIntensity) : null;
            if (jointAllocation != null && ResolutionMetrics.CaptureDetailedCounters)
                Interlocked.Add(ref TurnDiagnostics.For(w).SharedAllocationTicks, System.Diagnostics.Stopwatch.GetTimestamp() - sharedStart);
            bool spareCurrentWork = false;
            int jointBudget = c.Population;
            double futureWorkPrice = double.NaN;
            int StaffedHerders(double s)
            {
                // At the path's start (s = 0), minimize net work subject to the seed floor; at all other positions use
                // the same labour price w(s) as the craft claims. No valued claim at all means labour is uncontested.
                if (s <= 0) return Math.Min(c.Population, herdLadder.Staffed(double.PositiveInfinity));
                double price = valueRule.LaborPrice(demand.Claims, demand.Expansion, s);
                if (double.IsNaN(price)) price = 0;
                return Math.Min(c.Population, herdLadder.Staffed(price));
            }
            var order = demand.Order;
            productionOrder = order;
            var claimIndex = new Dictionary<CraftGood, int>();
            for (int i = 0; i < demand.Claims.Count; i++) claimIndex[demand.Claims[i].Good] = i;

            // Staffing pass: turns the rule's shares at path position s into one
            // resolver-shaped proposal and its full labor requirement.
            Proposal Evaluate(double s)
            {
                if (ResolutionMetrics.CaptureDetailedCounters) Interlocked.Increment(ref TurnDiagnostics.For(w).PlannerEvaluations);
                double pathPrice = valueRule == null ? 0 : valueRule.LaborPrice(demand.Claims, demand.Expansion, s);
                var joint = jointAllocation?.Select(spareCurrentWork ? 0 : pathPrice,
                    spareCurrentWork ? futureWorkPrice : pathPrice, jointBudget);
                int selectedHerders = joint != null ? joint.Herders : herdLadder == null ? reservedHerdersForForecast : StaffedHerders(s);
                double animalReceipts = joint != null ? joint.AnimalFood : herdLadder == null ? managedFood.AnimalFood
                    : herdLadder.AnimalFoodAt(selectedHerders) + herdLadder.SlaughterFoodAt(selectedHerders);
                var candidateDemand = joint != null ? joint.Demand : herdLadder == null ? demand : demand.WithAnimalFood(w, c, animalReceipts);
                double plantEnvelope = candidateDemand.PlantEnvelope, animalEnvelope = candidateDemand.AnimalEnvelope;
                double foodRoomEnvelope = candidateDemand.FoodRoomEnvelope;
                double clothingPlantEnvelope = candidateDemand.PlantEnvelopeClothing, clothingAnimalEnvelope = candidateDemand.AnimalEnvelopeClothing, clothingRoomEnvelope = candidateDemand.FoodRoomEnvelopeClothing;
                var shares = new double[demand.Claims.Count];
                // Under the Value rule the expansion ladder (WP4b), when present, buys build worker-years on the price path.
                double expansionWorkerYears = 0;
                if (valueRule != null && demand.Expansion != null) valueRule.Shares(demand.Claims, demand.Expansion, s, shares, out expansionWorkerYears);
                else rule.Shares(demand.Claims, s, shares);
                if (herdLadder != null)
                {
                    expansionWorkerYears = 0;
                    double price = valueRule.LaborPrice(demand.Claims, demand.Expansion, s);
                    if (s > 0 && candidateDemand.Expansion != null)
                        for (int i = 0; i < candidateDemand.Expansion.Value.Length && (double.IsNaN(price) || candidateDemand.Expansion.Value[i] >= price); i++)
                            expansionWorkerYears += candidateDemand.Expansion.WorkerYears[i];
                }
                if (joint != null) expansionWorkerYears = joint.ExpansionYears;
                double Share(CraftGood good) => shares[claimIndex[good]];
                var intent = e.Copy(); intent.Labor = new EconomicLaborPlan();
                // Managed-herd work is a resident reservation, not an output
                // appended after the agricultural proposal.  Put it on the
                // resolver-shaped candidate before craft, food, material, and
                // field feasibility are evaluated so every later worker budget
                // sees the same bounded resident pool.
                int reservedHerders = selectedHerders;
                if (reservedHerders > 0)
                    intent.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Herder, Population = reservedHerders });
                var candidate = Preview(c, intent, nextPopulation);
                NeolithicProduction.DepreciateHousing(w, candidate, years);
                double candidateLightHousing = candidate.Inventory.LightHousingUnits;
                if (husbandryEnabled) { candidate.PlantFood = Math.Max(plantEnvelope, clothingPlantEnvelope); candidate.AnimalFood = Math.Max(animalEnvelope, clothingAnimalEnvelope); }
                if (husbandryEnabled && NeolithicProduction.Priority(preview, CraftGood.LightHousing) > 0 && c.Population > reservedHerders)
                    candidate.Workforce.Assignments.Add(new PopulationAssignment { Role = c.People.HasTech(NeolithicTech.Construction) ? WorkforceRoles.Builder : WorkforceRoles.Craftsperson, Population = 1 });
                var room = MaterialEconomy.Materials.ToDictionary(m => m, m => MaterialEconomy.Inventory(c, m) + accessible[m]);
                // Housing Material's plan for the call in progress: built at its
                // access-order position in the capped loop, and one plan for the
                // whole resolver-shaped final role pass.
                HousingMaterialPlan materialPlan = null;
                double plant = plantEnvelope;
                double animal = animalEnvelope;
                double animalFabric = Math.Max(0, c.Inventory.AnimalFabric);
                double housingMaterial = Math.Max(0, c.Inventory.HousingMaterial);
                double foodRoom = foodRoomEnvelope;
                double clothingPlant = clothingPlantEnvelope, clothingAnimal = clothingAnimalEnvelope, clothingRoom = clothingRoomEnvelope, clothingLeft = demand.ClothingNeedUnits;
                var requested = recipes.ToDictionary(r => r.Good, r => 0d);
                var futureRoles = new Dictionary<string, double>();
                int proposedFarmers = 0;
                double proposedExpansion = 0;
                // Expansion farmers staffed by the Value rule's expansion claim: workers for the turn.
                int staffedExpansion = 0;
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
                    if (herdLadder != null && r.Good == CraftGood.HerdingTools) return NeolithicProduction.Demand(w, candidate, r.Good, years, -1, false, false, sources);
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
                    if (materialPlan != null && r.ConstructionMaterial <= 0 && (r.Wood > 0 || r.Clay > 0))
                        amount = materialPlan.LimitDirect(room[MaterialId.Wood], room[MaterialId.Clay], r, amount);
                    if (r.ConstructionMaterial > 0)
                        amount = materialPlan == null ? 0 : materialPlan.LimitHousing(room[MaterialId.Wood], room[MaterialId.Clay], amount, r.ConstructionMaterial, out _, out _);
                    double unfunded = amount;
                    if (r.PlantFood > 0) amount = Math.Min(amount, plant / r.PlantFood);
                    if (r.AnimalFood > 0) amount = Math.Min(amount, animal / r.AnimalFood);
                    if (husbandryEnabled && r.PlantFood + r.AnimalFood > 0) amount = Math.Min(amount, foodRoom / (r.PlantFood + r.AnimalFood));
                    bool fabric = r.Good == CraftGood.Cloth || r.Good == CraftGood.AnimalFabric;
                    if (fabric && clothingLeft > Epsilon)
                    {
                        // Fabric that covers this turn's clothing need is paid before the reserve refill.
                        double clothes = Math.Min(unfunded, clothingLeft);
                        if (r.PlantFood > 0) clothes = Math.Min(clothes, clothingPlant / r.PlantFood);
                        if (r.AnimalFood > 0) clothes = Math.Min(clothes, clothingAnimal / r.AnimalFood);
                        if (husbandryEnabled && r.PlantFood + r.AnimalFood > 0) clothes = Math.Min(clothes, clothingRoom / (r.PlantFood + r.AnimalFood));
                        amount = Math.Max(amount, clothes);
                    }
                    if (r.AnimalFabric > 0) amount = Math.Min(amount, animalFabric / r.AnimalFabric);
                    if (r.HousingMaterial > 0) amount = Math.Min(amount, housingMaterial / r.HousingMaterial);
                    if (debit)
                    {
                        room[MaterialId.Stone] -= amount * r.Stone; room[MaterialId.Clay] -= amount * r.Clay; room[MaterialId.Wood] -= amount * r.Wood;
                        if (r.ConstructionMaterial > 0 && materialPlan != null)
                        {
                            materialPlan.LimitHousing(room[MaterialId.Wood], room[MaterialId.Clay], amount, r.ConstructionMaterial, out double wood, out double clay);
                            room[MaterialId.Wood] -= wood; room[MaterialId.Clay] -= clay;
                        }
                        materialPlan?.CompleteDirect(r, amount);
                        plant = Math.Max(0, plant - amount * r.PlantFood);
                        animal = Math.Max(0, animal - amount * r.AnimalFood);
                        foodRoom = Math.Max(0, foodRoom - amount * (r.PlantFood + r.AnimalFood));
                        clothingPlant = Math.Max(0, clothingPlant - amount * r.PlantFood);
                        clothingAnimal = Math.Max(0, clothingAnimal - amount * r.AnimalFood);
                        clothingRoom = Math.Max(0, clothingRoom - amount * (r.PlantFood + r.AnimalFood));
                        if (fabric) clothingLeft = Math.Max(0, clothingLeft - amount);
                        animalFabric += r.Good == CraftGood.AnimalFabric ? amount : 0;
                        animalFabric -= amount * r.AnimalFabric;
                        housingMaterial += r.Good == CraftGood.HousingMaterial ? amount : 0;
                        housingMaterial -= amount * r.HousingMaterial;
                    }
                    return Math.Max(0, amount);
                }
                // Material access order: crafting priority under the uniform rule; under
                // the Value rule the camp's fixed production order (start-of-turn
                // marginal value, tools priced through the outputs they cover, goods
                // without a value after). Animal Fabric precedes Light Housing and
                // Housing Material precedes the Housing assembly in either.
                IEnumerable<CraftRecipe> AccessOrder(IEnumerable<CraftRecipe> list)
                    => ProductionOrder.Sort(list, good => NeolithicProduction.Priority(candidate, good), order);
                // Housing Material is an ordinary member of the material-access order.
                // Work ranked above it has already taken its inputs when it is reached;
                // the remaining planned Wood/Clay demand of work ranked below it is
                // only a soft reservation that it prefers to leave alone and may
                // draw on if the unreserved rest falls short. FarmTools remains
                // deferred until field intent is bounded, so its job (ranked above
                // Housing Material: a hard reservation; below: a soft one) is
                // reserved from an estimate of its demand.
                var materialRecipe = recipes.Single(x => x.Good == CraftGood.HousingMaterial);
                bool housingClaimed = goods.Any(x => x.Good == CraftGood.Housing);
                var accessList = AccessOrder(housingClaimed ? goods.Concat(new[] { materialRecipe }) : goods).ToList();
                int materialIndex = accessList.FindIndex(x => x.Good == CraftGood.HousingMaterial);
                // When FarmTools outranks Housing (a non-default priority) its bounded
                // field intent, which needs every other good's staffing first, must
                // precede Housing Material, so the planner keeps Housing Material
                // after all direct work in that case (the resolver still uses rank).
                int farmIndex = accessList.FindIndex(x => x.Good == CraftGood.FarmTools);
                bool materialAfterFarm = housingClaimed && farmIndex >= 0 && farmIndex < materialIndex;
                void AllocateHousingMaterial()
                {
                    double wantedHousing = goals[CraftGood.Housing] * Share(CraftGood.Housing);
                    double wantedMaterial = Math.Max(0, wantedHousing - housingMaterial);
                    var later = new Dictionary<CraftGood, double> { [CraftGood.HousingMaterial] = wantedMaterial };
                    if (!materialAfterFarm)
                        for (int i = materialIndex + 1; i < accessList.Count; i++)
                        {
                            var l = accessList[i];
                            if (l.Good == CraftGood.Housing || l.Wood <= 0 && l.Clay <= 0) continue;
                            later[l.Good] = l.Good == CraftGood.FarmTools
                                ? (cultivationEnabled && NeolithicProduction.Priority(candidate, CraftGood.FarmTools) > 0
                                    ? Capped(l, NeolithicProduction.Demand(w, candidate, CraftGood.FarmTools, years, -1, false, false, sources) * Share(CraftGood.FarmTools), false) : 0)
                                : Capped(l, RemainingGoodDemand(l) * Share(l.Good), false);
                        }
                    materialPlan = MaterialEconomy.BuildHousingPlan(accessList.ToArray(), later, room[MaterialId.Stone], room[MaterialId.Clay], room[MaterialId.Wood]);
                    requested[CraftGood.HousingMaterial] = Capped(materialRecipe, wantedMaterial, true);
                    materialPlan = null;
                }
                foreach (var r in accessList)
                {
                    if (r.Good == CraftGood.FarmTools) continue;
                    if (r.Good == CraftGood.HousingMaterial)
                    {
                        if (!materialAfterFarm) AllocateHousingMaterial();
                        continue;
                    }
                    // Housing labour is added with its material after the field proposal.
                    if (r.Good == CraftGood.Housing) { if (!materialAfterFarm) requested[r.Good] = Capped(r, goals[r.Good] * Share(r.Good), true); continue; }
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
                    bool intendedExpansion = joint != null ? joint.ExpansionYears > Epsilon : projectedDemand > gapSupply && HasRetainedExpansionGain();
                    double roleWorkers = futureRoles.Values.Sum(v => Math.Ceiling(v - Epsilon));
                    // Reserve the food-gathering share before proposing field
                    // expansion. Otherwise a full resident ledger can appear
                    // to support both expansion and the food/material chain,
                    // and fulfillment search cannot distinguish the infeasible
                    // manufacturing case.
                    double fieldFoodTarget = Math.Min(candidateDemand.Supply, biological + goals[CraftGood.Cloth] * n.PlantFoodPerFabricUnit);
                    double gatherRate = cfg.GatherPerPerson * intervals;
                    int foodReserve = !established && gatherRate > 0
                        ? (int)Math.Ceiling(Math.Max(0, fieldFoodTarget) / gatherRate - Epsilon)
                        : 0;
                    double fieldWorkers = Math.Max(0, c.Population - reservedHerders - roleWorkers - foodReserve);
                    double maintenance = joint != null ? Math.Min(joint.Farmers, fieldWorkers) : CultivationEconomy.PlannedMaintenanceFarmers(w, c, fieldWorkers);
                    double plannedFarmRate = demand.HarvestProductivity * cfg.GatherPerPerson * intervals;
                    double plannedFoodTarget = Math.Min(candidateDemand.TerrestrialAfterHerds, biological + goals[CraftGood.Cloth] * n.PlantFoodPerFabricUnit);
                    double harvest = established && plannedFarmRate > 0 ? Math.Ceiling(Math.Max(maintenance, plannedFoodTarget / plannedFarmRate) - Epsilon) : 0;
                    double plannedOperating = joint != null ? maintenance : established ? Math.Min(fieldWorkers, harvest) : 0;
                    double expansion = intendedExpansion ? Math.Max(0, fieldWorkers - plannedOperating) : 0;
                    if (joint != null) expansion = Math.Min(expansion, expansionWorkerYears / years);
                    proposedFarmers = (int)Math.Ceiling(plannedOperating + expansion - Epsilon);
                    // Value rule: the ladder's worker-years at this price staff that many field
                    // workers for the turn (at most the field workers left). The
                    // intended proposal below still sees every remaining field worker, so
                    // the Farm Tools demand helper is sized as before; leftover residents
                    // join expansion after the search exactly as before.
                    if (intendedExpansion && expansionWorkerYears > 0 && candidateDemand.Expansion != null)
                        staffedExpansion = (int)Math.Max(0, Math.Ceiling(Math.Min(expansionWorkerYears / years, expansion) - Epsilon));
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
                            // Producers leave fewer field workers to staff.
                            staffedExpansion = (int)Math.Min(staffedExpansion, Math.Max(0, proposedFarmers - Math.Ceiling(plannedOperating - Epsilon)));
                        }
                    }
                }
                // The construction stage is Housing Material (the flexible Wood/Clay
                // consumer, allocated above at its access-order position) followed by
                // the Housing assembly that consumes it plus stock. Both steps use
                // the Builder tool multiplier; their labour is summed per role before
                // rounding, as one Housing step was.
                if (materialAfterFarm)
                {
                    AllocateHousingMaterial();
                    var housingRecipe = goods.Single(x => x.Good == CraftGood.Housing);
                    requested[CraftGood.Housing] = Capped(housingRecipe, goals[CraftGood.Housing] * Share(CraftGood.Housing), true);
                }
                if (housingClaimed && housingMultiplier > 0)
                {
                    var assembly = goods.Single(x => x.Good == CraftGood.Housing);
                    Add(futureRoles, Role(materialRecipe), requested[CraftGood.HousingMaterial] / (Rate(materialRecipe) * housingMultiplier * intervals));
                    Add(futureRoles, Role(assembly), requested[CraftGood.Housing] / (Rate(assembly) * housingMultiplier * intervals));
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
                plant = plantEnvelope;
                animal = animalEnvelope;
                animalFabric = Math.Max(0, c.Inventory.AnimalFabric);
                housingMaterial = Math.Max(0, c.Inventory.HousingMaterial);
                foodRoom = foodRoomEnvelope;
                clothingPlant = clothingPlantEnvelope; clothingAnimal = clothingAnimalEnvelope; clothingRoom = clothingRoomEnvelope; clothingLeft = demand.ClothingNeedUnits;
                // Match production's role/recipe ordering and share one tool-time budget.
                var orderedRecipes = ProductionOrder.Sort(recipes, good => NeolithicProduction.Priority(candidate, good), order);
                // The candidate is fixed through this pass: one schedule run
                // answers both Hand and Regular Tool replacement demand.
                (double HandTools, double Tools)? toolDemand = null;
                // Rebuild the final role pass from fresh stock with one plan, as the
                // resolver does: work ranked above Housing Material keeps its
                // inputs, work ranked below is softly reserved and must leave
                // Housing Material's claim. Each realized receipt releases its
                // reservation below.
                var finalPlanned = recipes.ToDictionary(r => r.Good, r => requested[r.Good]);
                if (NeolithicProduction.Priority(candidate, CraftGood.HandTools) > 0 || NeolithicProduction.Priority(candidate, CraftGood.Tools) > 0)
                {
                    toolDemand = NeolithicProduction.ToolReplacementDemands(w, candidate, years, candidate.Population, false, false, sources, order);
                    finalPlanned[CraftGood.HandTools] = toolDemand.Value.HandTools; finalPlanned[CraftGood.Tools] = toolDemand.Value.Tools;
                }
                materialPlan = MaterialEconomy.BuildHousingPlan(orderedRecipes, finalPlanned, room[MaterialId.Stone], room[MaterialId.Clay], room[MaterialId.Wood]);
                // The resolver-shaped role pass: roles in turn under the uniform rule; under
                // the Value order every recipe in value order, its role being the one that
                // makes it, so shared coverage goes to the more valuable recipe.
                var finalSteps = order != null ? orderedRecipes.Select(r => (Role: Role(r), Recipe: r))
                    : ProductionOrder.Roles.SelectMany(role => orderedRecipes.Where(r => Role(r) == role).Select(r => (Role: role, Recipe: r)));
                foreach (var (role, r) in finalSteps)
                    {
                        if (NeolithicProduction.Priority(candidate, r.Good) <= 0) continue;
                        double wanted;
                        if (r.Good == CraftGood.HandTools || r.Good == CraftGood.Tools)
                        {
                            toolDemand ??= NeolithicProduction.ToolReplacementDemands(w, candidate, years, candidate.Population, false, false, sources, order);
                            wanted = r.Good == CraftGood.HandTools ? toolDemand.Value.HandTools : toolDemand.Value.Tools;
                        }
                        else wanted = requested[r.Good];
                        double amount = Capped(r, wanted, false);
                        if (r.Equipment || r.Good == CraftGood.HousingMaterial) amount = Math.Min(amount, coverage.Capacity(r.Good, Rate(r), c.Population * intervals));
                        amount = Capped(r, amount, true);
                        double work = r.Equipment ? coverage.Spend(r.Good, Rate(r), amount) : r.Good == CraftGood.HousingMaterial ? coverage.Time(r.Good, Rate(r), amount) : Rate(r) > 0 ? amount / Rate(r) : 0;
                        Add(rolesNow, role, work / intervals);
                        materialPlan.Release(r);
                    }
                materialPlan = null;
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
                var outputs = MaterialEconomy.Forecast(w, candidate, recipes, intervals, years, new ToolCoverageBudget(w, c, intervals).Remaining, !husbandryEnabled, -1, false, false, -1, -1, sources, order);
                var housing = MaterialEconomy.PlanHousingMaterials(w, c, recipes, outputs, years, w.Days, sources, order);
                var raw = MaterialEconomy.Materials.ToDictionary(m => m, m => 0d);
                foreach (var r in recipes)
                {
                    if (r.ConstructionMaterial > 0) continue;
                    raw[MaterialId.Stone] += outputs[r.Good] * r.Stone;
                    raw[MaterialId.Clay] += outputs[r.Good] * r.Clay;
                    raw[MaterialId.Wood] += outputs[r.Good] * r.Wood;
                }
                double horizon = n.MaterialReserveMultiplier(years);
                double construction = outputs[CraftGood.HousingMaterial] * recipes.Single(r => r.Good == CraftGood.HousingMaterial).ConstructionMaterial;
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
                    var typedFeedstock = NeolithicProduction.FabricFeedstockInputs(w, candidate, intervals, years, nextPopulation, false, false, -1, -1, order);
                    forecastPlantFeedstock = Math.Min(forecastPlantFeedstock, typedFeedstock.PlantFood);
                    forecastAnimalFeedstock = Math.Min(forecastAnimalFeedstock, typedFeedstock.AnimalFood);
                }
                var clothingFeedstock = NeolithicProduction.ClothingNeedFeedstock(n, preview.Population, years,
                    c.Inventory.Cloth, c.Inventory.AnimalFabric, forecastPlantFeedstock, forecastAnimalFeedstock);
                double feedstockReserve = Math.Min(NeolithicProduction.DesiredReserveFood(w, consumers, intervals), NeolithicProduction.StorageCapacityAfterPotDepreciation(w, c, intervals, years));
                NeolithicProduction.FeedstockGaps(c.Food, c.PlantFood, husbandryEnabled ? c.AnimalFood : 0, demand.Meals, feedstockReserve,
                    forecastPlantFeedstock, husbandryEnabled ? forecastAnimalFeedstock : 0,
                    new NeolithicProduction.ClothingFeedstock { Plant = clothingFeedstock.Plant, Animal = husbandryEnabled ? clothingFeedstock.Animal : 0 },
                    out double plantGap, out double animalGap);
                double feedstock = plantGap + animalGap;
                double foodTarget = Math.Min(candidateDemand.ForageAfterHerds, Math.Max(0, biological + feedstock - animalReceipts));
                double farmRate = demand.HarvestProductivity * cfg.GatherPerPerson * intervals;
                int nonFarmWorkers = intent.Assignments.Where(a => a.Role != WorkforceRoles.Farmer).Sum(a => a.Population);
                int availableFarmers = Math.Max(0, c.Population - nonFarmWorkers);
                double terrestrialFoodTarget = Math.Min(candidateDemand.TerrestrialAfterHerds, foodTarget);
                int requiredOperating = joint != null ? joint.Farmers : established && farmRate > 0
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
                // Expansion workers compete with crafts for residents (WP4b); they are in
                // neither actualWorkers nor the envelope, so they are added on top.
                int totalWorkers = (s <= Epsilon ? actualWorkers : Math.Max(actualWorkers, laborEnvelope)) + staffedExpansion;
                return new Proposal { Intent = intent, OperatingFarmers = operating, FoodGatherers = foodGatherers, Workers = totalWorkers, S = s, ExpansionWorkers = staffedExpansion, Herders = reservedHerders, Joint = joint };
            }
            // Allocate as much useful manufacturing as fits alongside food and its
            // complete source chain. No separate craft-population percentage is imposed.
            // Evaluate is pure over its own previews, so the zero-fulfilment
            // fallback is only needed when full fulfilment does not fit.
            var best = Evaluate(1);
            if (best.Workers > c.Population)
            {
                var full = best;
                best = Evaluate(0);
                if (herdLadder != null || jointAllocation != null)
                {
                    // Net work is monotone in price on the portfolio envelope,
                    // but present staffing can switch in either direction. Search
                    // every constant-choice interval instead of discarding a
                    // possibly feasible interval on a global monotonicity assumption.
                    var boundaries = (jointAllocation?.PriceCrossings ?? herdLadder.PriceCrossings).Select(price => valueRule.PositionAtPrice(demand.Claims, demand.Expansion, price))
                        .Where(position => position > 0 && position < 1).Concat(new[] { 0.0, 1.0 }).Distinct().OrderBy(position => position).ToArray();
                    for (int i = 1; i < boundaries.Length; i++)
                    {
                        double low = boundaries[i - 1] + 1e-9, high = boundaries[i] - 1e-9;
                        if (!(high > low)) continue;
                        var upper = Evaluate(high);
                        if (upper.Workers <= c.Population) { if (upper.S > best.S) best = upper; continue; }
                        var lower = Evaluate(low);
                        if (lower.Workers <= c.Population)
                        {
                            var feasible = SearchPath(Evaluate, lower, low, high, upper.Workers, c.Population, p => p.Workers);
                            if (feasible.S > best.S) best = feasible;
                        }
                    }
                }
                else if (full.ExpansionWorkers == 0)
                    best = SearchPath(Evaluate, best, 0, 1, full.Workers, c.Population, p => p.Workers);
                else
                {
                    // WP4b: staffed expansion can ask for many times the residents at
                    // the path's end, so interpolating on the total requirement drags
                    // every guess towards 0. First find the craft plan's position on
                    // the requirement without staffed expansion, exactly as before the
                    // claim existed. If expansion is not staffed there, that plan
                    // stands. Otherwise the answer lies between the position where the
                    // price reaches the ladder's first value (below it nothing is
                    // staffed, so that plan is feasible) and the craft position.
                    var lowest = best;
                    var craft = SearchPath(Evaluate, lowest, 0, 1, full.Workers - full.ExpansionWorkers, c.Population, p => p.Workers - p.ExpansionWorkers);
                    if (craft.Workers <= c.Population) best = craft;
                    else
                    {
                        double start = Math.Min(craft.S, valueRule.PositionAtPrice(demand.Claims, demand.Expansion, demand.Expansion.Value[0]));
                        // Just above the price where the first step is staffed.
                        start = Math.Max(0, start - 1e-9);
                        var from = start <= Epsilon ? lowest : Evaluate(start);
                        best = from.Workers <= c.Population
                            ? SearchPath(Evaluate, from, start, craft.S, craft.Workers, c.Population, p => p.Workers)
                            : lowest;
                    }
                }
            }
            if (jointAllocation != null)
            {
                spareCurrentWork = true;
                futureWorkPrice = valueRule.LaborPrice(demand.Claims, demand.Expansion, best.S);
                int nonSubsistence = best.Intent.Assignments.Where(a => a.Role != WorkforceRoles.Farmer && a.Role != WorkforceRoles.Herder && a.Role != WorkforceRoles.Gatherer).Sum(a => a.Population);
                jointBudget = Math.Max(0, c.Population - nonSubsistence - (int)Math.Ceiling(best.Intent.Labor.MaterialGatherers - Epsilon));
                var spareProposal = Evaluate(best.S);
                if (spareProposal.Workers <= c.Population) best = spareProposal;
            }
            int spare = Math.Max(0, c.Population - best.Workers);
            // The capital scoring horizon is not a mandate to feed a hypothetical
            // century of exponential growth. Expand for the next demand forecast.
            double futureDemand = biological * nextPopulation / c.Population + (cultivationEnabled ? goals[CraftGood.Cloth] * n.PlantFoodPerFabricUnit : 0);
            double selectedSupply = best.Joint != null ? best.Joint.Demand.Supply : herdLadder == null ? gapSupply : forageAfterHerds + herdLadder.AnimalFoodAt(best.Herders) + herdLadder.SlaughterFoodAt(best.Herders);
            var finalDemand = best.Joint != null ? best.Joint.Demand : herdLadder == null ? demand : demand.WithAnimalFood(w, c, selectedSupply - forageAfterHerds);
            bool expand = best.Joint != null ? best.ExpansionWorkers > 0 : cultivationEnabled && futureDemand > selectedSupply && HasRetainedExpansionGain();
            // The staffed expansion claim (Value rule) plus the leftover residents.
            int expanders = (jointAllocation == null ? spare : 0) + (expand ? best.ExpansionWorkers : 0);
            if (best.Joint != null)
            {
                expanders = Math.Min(expanders, (int)Math.Floor(best.Joint.ExpansionYears / years + Epsilon));
                best.Intent.Labor.ExpansionFarmers = 0;
            }
            // A husbandry-primary camp that settling would not pay for stops short of the village threshold (Docs/HusbandrySettling.md).
            if (expand && finalDemand.ExpansionEffortCap < double.PositiveInfinity)
                expanders = Math.Min(expanders, (int)Math.Floor(finalDemand.ExpansionEffortCap / years + 1e-9));
            if (expanders > 0 && expand)
            {
                var farmer = best.Intent.Assignments.FirstOrDefault(a => a.Role == WorkforceRoles.Farmer);
                if (farmer == null) best.Intent.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Farmer, Population = expanders }); else farmer.Population += expanders;
                best.Intent.Labor.ExpansionFarmers = expanders;
            }
            best.Intent.Assignments = best.Intent.Assignments.OrderBy(a => a.Role, StringComparer.Ordinal).ToList();
            if (herdLadder != null || best.Joint?.Herd != null)
            {
                // The herd the staffed herders manage (never beyond what the pasture grows): animals above it are slaughtered after breeding,
                // and breeding is held back so the herd is not bred past it only to be slaughtered.
                int target = best.Joint != null ? best.Joint.Target : herdLadder.Target(best.Herders);
                best.Intent.HerdTargetSet = true; best.Intent.HerdTarget = target;
                best.Intent.ManagedBreedingIntensity = HerdLabour.CapBreeding(w, c, best.Intent.ManagedBreedingIntensity, target, years);
            }
            if (valueRule != null) laborPrice = valueRule.LaborPrice(demand.Claims, demand.Expansion, best.S);
            if (finalDemand.Expansion != null) farmLabourPrice = finalDemand.Expansion.ValueAfter((best.Intent.Labor?.ExpansionFarmers ?? 0) * years);
            if (ResolutionMetrics.CaptureDetailedCounters)
                Interlocked.Add(ref TurnDiagnostics.For(w).PlanTicks, System.Diagnostics.Stopwatch.GetTimestamp() - planStart);
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
        // The search runs on [lowS, highS] (lowest is the proposal at lowS,
        // highWorkers the requirement at highS) with `required` as the
        // requirement both for feasibility and interpolation.
        private static Proposal SearchPath(Func<double, Proposal> evaluate, Proposal lowest, double lowS, double highS, int highWorkers, int residents, Func<Proposal, int> required)
        {
            var best = lowest;
            if (required(best) > residents) return best;
            double low = lowS, high = highS; int lowWorkers = required(best);
            int evaluations = 0;
            bool Probe(double s)
            {
                evaluations++;
                var p = evaluate(s);
                int workers = required(p);
                if (workers <= residents) { low = s; lowWorkers = workers; best = p; return true; }
                high = s; highWorkers = workers; return false;
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
                Inventory = new CampInventory { HandTools = c.Inventory.HandTools, Tools = c.Inventory.Tools, CopperTools = c.Inventory.CopperTools, Cloth = c.Inventory.Cloth, AnimalFabric = c.Inventory.AnimalFabric, LightHousingUnits = c.Inventory.LightHousingUnits, HousingMaterial = c.Inventory.HousingMaterial, HerdingTools = c.Inventory.HerdingTools, HousingUnits = c.Inventory.HousingUnits, Pots = c.Inventory.Pots, FarmTools = c.Inventory.FarmTools, Stone = c.Inventory.Stone, Clay = c.Inventory.Clay, Wood = c.Inventory.Wood } };
            result.QoL = c.QoL; result.Bind(c.People); EconomicIntents.Apply(result, intent); return result;
        }
    }
}
