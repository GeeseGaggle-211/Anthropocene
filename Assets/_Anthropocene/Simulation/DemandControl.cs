using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    // The automatic economic allocation strategy (Docs/EconomicControlContract.md §15): one dispatch point for
    // TurnPlanner.AutomaticIntent and EconomicIntents.Automatic. "Legacy" is the Value/Uniform planner exactly as before.
    public static class EconomicAllocationStrategy
    {
        public const string Legacy = "Legacy", DemandControl = "DemandControl";
        public static bool IsKnown(string name) => name == Legacy || name == DemandControl;
        public static bool IsDemandControl(SimulationConfig cfg) => cfg != null && cfg.EconomicAllocation == DemandControl;
        internal static EconomicIntent Plan(WorldState w, Camp c, EconomicIntent settings, out ProductionOrder production)
            => Plan(w, c, settings, out production, out _);
        internal static EconomicIntent Plan(WorldState w, Camp c, EconomicIntent settings, out ProductionOrder production,
            out DemandController.Allocation allocation)
        {
            allocation = null;
            if (IsDemandControl(w.Config)) return DemandController.Plan(w, c, settings, out production, out allocation);
            return EconomicPlanner.Plan(w, c, settings, EconomicPlanner.ConfiguredRule(w.Config), out _, out production);
        }
    }

    // DemandControl v0.1, slice 1 (Docs/EconomicControlContract.md with Addendum A).
    //
    // Pure and per camp: it reads the committed camp (a planning view) and returns a legal EconomicIntent plus a production
    // order whose ceilings the forecast and resolver honour. Single-service controllers make requests:
    //   Survival     herders for the current herd, operating farmers and food gatherers (the existing physical rules);
    //   Continuity   this turn's clothing consumption, shelter replacement (fixed Housing for farming camps, tents for
    //                herding ones), Farm and Herding Tools for the work staffed;
    //   Recovery     the food/clothing reserve, the shelter deficit, Pots for the storage gap;
    //   Development bounded field expansion for a demonstrated food-security gap.
    // Maximum-birth food is not a labour reservation: physical births use whatever
    // food remains after the authorized services. Each request is a complete bundle:
    // craft time, made intermediates (Animal Fabric for tents, Housing Material for Housing), raw extraction beyond stock,
    // feedstock and its gathering, and tool service (toolmaking at the urgency of the work it enables). The Elder grants
    // whole classes in order, within a class an equal fulfilment fraction, freezing requests whose own input saturates; then
    // integer staffing is compiled per role with at most one repair pass. Cost: O(requests x bisection) per camp, one
    // physical demand pass, no production forecast and no Value search.
    internal static class DemandController
    {
        // Protected is used only by the KernelSplitDiagnostic prototype.
        internal enum ServiceClass { Protected = 0, Survival = 1, Continuity = 2, Recovery = 3, Development = 4 }
        private const double Epsilon = 1e-9;
        // Scalar headless root-cause ablations only. Ordinary planning always
        // leaves these false; the recovery harness uses the default one-worker
        // planner and scopes them around matched continuations. Thread-static
        // values deliberately do not define a parallel simulation mode. The two
        // food flags are retained only so older diagnostic invocations remain
        // source-compatible; the contract-correct path is now consumption-first.
        [ThreadStatic] internal static bool ConsumptionFirstDiagnostic;
        [ThreadStatic] internal static bool ExcludeGrowthFromSurvivalDiagnostic;
        [ThreadStatic] internal static bool NoFieldDevelopmentDiagnostic;
        // Option-3 prototype (diagnostic only; never set by ordinary planning). When on, the Elder scores a small grid of
        // (current clothing units, shelter backlog people) splits with the authoritative PhysicalDemography.AdvanceBudget
        // kernel, commits the best one as a Protected class ahead of meals, and the resolver honours that clothing
        // feedstock before consumption (TurnNeeds.ProtectedClothing). Process-wide (not thread-static) so parallel
        // preparation sees it. Objectives: 0 person-years, 1 QoL-weighted person-years, 2 lexicographic (person-years
        // within KernelLexTolerance of the best, then QoL; the recommended candidate with SurplusFundedBirthsDiagnostic and a
        // 5% tolerance), 3 terminal-population welfare, 4 deaths averted. See Docs/ClothingCoverageAndSurplusBirths.md.
        internal static volatile bool KernelSplitDiagnostic;
        internal static volatile int KernelSplitObjective;
        internal static double KernelLexTolerance = .01;
        // Hybrid counterfactual: score and commit only the shelter split; clothing keeps the ordinary meals-first requests.
        internal static volatile bool KernelShelterOnlyDiagnostic;
        internal static long KernelSplitCalls, KernelSplitTicks, KernelSplitCamps;
        internal static readonly double[] KernelClothingGrid = { 0, .25, .5, .75, 1 };
        internal static readonly double[] KernelShelterGrid = { 0, .5, 1 };
        internal static readonly long[] KernelChosenClothing = new long[5], KernelChosenShelter = new long[3];
        internal static void ResetKernelSplitCounters()
        {
            KernelSplitCalls = KernelSplitTicks = KernelSplitCamps = 0;
            Array.Clear(KernelChosenClothing, 0, KernelChosenClothing.Length);
            Array.Clear(KernelChosenShelter, 0, KernelChosenShelter.Length);
        }

        // Per-unit requirements of a request's complete bundle.
        internal sealed class Bundle
        {
            public readonly Dictionary<string, double> RoleYears = new Dictionary<string, double>();   // craft worker-years per unit, by role
            public readonly Dictionary<CraftGood, double> Outputs = new Dictionary<CraftGood, double>(); // goods made per unit, intermediates included
            public double ToolOutputs, Stone, Clay, Wood, Construction, Plant, Animal, Fabric, Material;
            public void Add(Bundle b, double scale)
            {
                foreach (var p in b.RoleYears) RoleYears[p.Key] = (RoleYears.TryGetValue(p.Key, out double v) ? v : 0) + p.Value * scale;
                foreach (var p in b.Outputs) Outputs[p.Key] = (Outputs.TryGetValue(p.Key, out double v) ? v : 0) + p.Value * scale;
                ToolOutputs += b.ToolOutputs * scale; Stone += b.Stone * scale; Clay += b.Clay * scale; Wood += b.Wood * scale; Construction += b.Construction * scale;
                Plant += b.Plant * scale; Animal += b.Animal * scale; Fabric += b.Fabric * scale; Material += b.Material * scale;
            }
        }
        // One service request and what the Elder granted (transient diagnostics, never saved).
        internal sealed class Request
        {
            // A goods request names its Good; a labour request (herd growth, field expansion) its Role, in persons.
            public string Service; public ServiceClass Class; public CraftGood? Good; public string Role; public double Units, Granted; public string Unmet = "";
            internal Bundle Bundle;
            // Opening intermediates represented by this request's per-unit
            // bundle. They are tentatively reserved while the bundle is built,
            // then reduced to the amount represented by the actual grant before
            // a fallback or lower service class may claim the remainder.
            internal double ReservedFabric, ReservedMaterial;
            internal bool ReservationSettled;
            public double Fraction => Units > Epsilon ? Granted / Units : 1;
        }
        internal sealed class Allocation
        {
            public readonly List<Request> Requests = new List<Request>();
            public readonly Dictionary<string, int> Staff = new Dictionary<string, int>();
            public int Herders, Farmers, FoodGatherers; public double FeedstockGatherers, MaterialGatherers, ToolsMade;
            public readonly Dictionary<CraftGood, double> Ceilings = new Dictionary<CraftGood, double>();
            public string FoodUnmet = ""; public int RepairPasses, TrimPasses;
            public double ResidentGoodsBudget, FinalCompiledPersons;
            // Prepared-boundary diagnostics. The normal envelopes preserve the
            // operating reserve; the Clothing variants stop after current meals.
            public double ExpectedSupply, Meals, OperatingReserve;
            public double ExpectedForageAfterHerds, ExpectedManagedAnimalFood;
            public double ConsumptionIncomeTarget, ReserveFillTarget, MaximumGrowthFood;
            public int ConsumptionOnlyFoodGatherers, ConsumptionReserveFoodGatherers, ConsumptionGrowthFoodGatherers;
            public int FieldExpansionSteps;
            public double FieldExpansionGapPerYear, FieldExpansionGainPerYear;
            public double FieldExpansionWorkerYears, FieldExpansionMinimumScore = double.PositiveInfinity;
            // Diagnostic only (CaptureDemandAllocations): the uncapped ladder — all positive steps from the committed state.
            public double FieldPotentialGainPerYear, FieldPotentialWorkerYears; public int FieldPotentialSteps = -1;
            public bool FieldExpansionEligible;
            public bool HusbandryEnabled;
            public double PlantEnvelope, AnimalEnvelope, FoodRoomEnvelope;
            public double PlantEnvelopeClothing, AnimalEnvelopeClothing, FoodRoomEnvelopeClothing;
            public double AccessibleStone, AccessibleClay, AccessibleWood;
            public double PlannedStoneExtraction, PlannedClayExtraction, PlannedWoodExtraction;
        }

        internal static EconomicIntent Plan(WorldState w, Camp c, EconomicIntent settings, out ProductionOrder production, out Allocation allocation)
        {
            production = null; var alloc = new Allocation(); allocation = alloc;
            var e = settings.Copy(); e.Assignments.Clear(); e.Labor = null;
            if (c.Population <= 0 || !CultivationEconomy.EconomyEnabled(c)) { e.AutomationTurns = 0; return e; }
            var cfg = w.Config; var n = cfg.Neolithic;
            double years = cfg.TurnDays / cfg.DaysPerYear, intervals = cfg.GenerationIntervals, perYear = cfg.DaysPerYear / cfg.BaseDays;
            var d = CampProductionDemand.BuildPhysical(w, c, e, false);
            bool protectedSplit = false;   // KernelSplitDiagnostic prototype: set once a split is committed
            bool kernelEnacted = false;
            double protectedClothingUnits = -1;
            // DemandControl closes every normal manufacturing input behind the
            // same attainable operating-reserve target used by resolution.
            // CampProductionDemand's compatibility envelopes are also used by
            // Legacy/Value and remain unchanged.  Correct this strategy's copy
            // here: otherwise reserve refill and reserve-clothing feedstock can
            // claim the same finite harvest, and resolution must cut Cloth below
            // the Elder's ceiling.  Current clothing retains the separate
            // meals-only envelope built by BuildPhysical.
            double attainableReserve = Math.Min(NeolithicProduction.DesiredReserveFood(w, d.Consumers, intervals),
                NeolithicProduction.StorageCapacityAfterPotDepreciation(w, c, intervals, years));
            d.OperatingReserve = attainableReserve;
            double normalFoodRoom = Math.Max(0, c.Food + d.Supply - d.Meals - attainableReserve);
            d.SparePlant = Math.Min(c.PlantFood, Math.Max(0, c.Food - d.Meals - attainableReserve));
            d.PlantEnvelope = Math.Min(d.TerrestrialAfterHerds + d.SparePlant, normalFoodRoom);
            d.AnimalEnvelope = d.HusbandryEnabled
                ? Math.Min(Math.Max(0, c.AnimalFood) + d.ManagedFood.AnimalFood,
                    Math.Max(0, normalFoodRoom - d.SparePlant))
                : 0;
            d.FoodRoomEnvelope = d.HusbandryEnabled ? normalFoodRoom : 0;
            alloc.ExpectedSupply = d.Supply; alloc.Meals = d.Meals; alloc.OperatingReserve = d.OperatingReserve;
            alloc.ExpectedForageAfterHerds = d.ForageAfterHerds;
            alloc.ExpectedManagedAnimalFood = d.ManagedFood.AnimalFood;
            alloc.HusbandryEnabled = d.HusbandryEnabled;
            alloc.PlantEnvelope = d.PlantEnvelope; alloc.AnimalEnvelope = d.AnimalEnvelope;
            alloc.FoodRoomEnvelope = d.FoodRoomEnvelope;
            alloc.PlantEnvelopeClothing = d.PlantEnvelopeClothing;
            alloc.AnimalEnvelopeClothing = d.AnimalEnvelopeClothing;
            alloc.FoodRoomEnvelopeClothing = d.FoodRoomEnvelopeClothing;
            alloc.AccessibleStone = d.Accessible.TryGetValue(MaterialId.Stone, out double accessibleStone) ? accessibleStone : 0;
            alloc.AccessibleClay = d.Accessible.TryGetValue(MaterialId.Clay, out double accessibleClay) ? accessibleClay : 0;
            alloc.AccessibleWood = d.Accessible.TryGetValue(MaterialId.Wood, out double accessibleWood) ? accessibleWood : 0;
            var recipes = d.Recipes; var byGood = recipes.ToDictionary(r => r.Good);
            int residents = c.Population;
            double gatherRate = cfg.GatherPerPerson * intervals;  // food one gatherer brings in over the turn

            // Food is split at the controller boundary. Current consumption is
            // Survival; reserve refill is Recovery. Maximum-growth food remains
            // a physical opportunity for whatever gatherers are left after the
            // authorized services, not an unconditional labor reservation.
            TurnResolver.FoodBucketTargets(w, c, intervals, years, d.Meals, c.Food,
                out double consumptionIncomeTarget, out _, out double reserveFillTarget,
                out double maximumGrowthFood);
            alloc.ConsumptionIncomeTarget = consumptionIncomeTarget;
            alloc.ReserveFillTarget = reserveFillTarget;
            alloc.MaximumGrowthFood = maximumGrowthFood;

            // ---- Survival: the current herd, field operation and food gathering (fixed directives: no expansion) ----
            int herders = Math.Min(residents, d.ReservedHerders);
            double animalReceipts = d.ManagedFood.AnimalFood;
            double survivalFoodDemand = consumptionIncomeTarget;
            double foodTarget = Math.Min(d.ForageAfterHerds, Math.Max(0, survivalFoodDemand - animalReceipts));
            double terrestrial = Math.Min(d.TerrestrialAfterHerds, foodTarget);
            double farmRate = d.HarvestProductivity * cfg.GatherPerPerson * intervals;
            // Operating farmers keep the fields up (and harvest while they do); the rest of the food is gathered. Gathering on
            // the footprint also builds field capital through the resolver's gatherer bridge (up to GathererBridgeCeiling),
            // which harvesting farmers do not, so food beyond the upkeep crew is staffed as gathering (slice 2).
            int requiredOperating = d.Established && farmRate > 0 ? (int)Math.Ceiling(d.Upkeep - Epsilon) : 0;
            int farmers = Math.Min(Math.Max(0, residents - herders), Math.Max(0, requiredOperating));
            double harvest = Math.Min(terrestrial, farmers * farmRate);
            int wantedGatherers = gatherRate > 0 ? (int)Math.Ceiling(Math.Max(0, foodTarget - harvest) / gatherRate - Epsilon) : 0;
            int foodGatherers = Math.Min(Math.Max(0, residents - herders - farmers), wantedGatherers);
            int DiagnosticGatherers(double target)
            {
                double boundedTarget = Math.Min(d.ForageAfterHerds, Math.Max(0, target - animalReceipts));
                double boundedTerrestrial = Math.Min(d.TerrestrialAfterHerds, boundedTarget);
                double boundedHarvest = Math.Min(boundedTerrestrial, farmers * farmRate);
                return gatherRate > 0
                    ? Math.Min(Math.Max(0, residents - herders - farmers),
                        (int)Math.Ceiling(Math.Max(0, boundedTarget - boundedHarvest) / gatherRate - Epsilon))
                    : 0;
            }
            alloc.ConsumptionOnlyFoodGatherers = DiagnosticGatherers(consumptionIncomeTarget);
            alloc.ConsumptionReserveFoodGatherers = DiagnosticGatherers(consumptionIncomeTarget + reserveFillTarget);
            alloc.ConsumptionGrowthFoodGatherers = DiagnosticGatherers(consumptionIncomeTarget + maximumGrowthFood);
            if (farmers < requiredOperating || foodGatherers < wantedGatherers) alloc.FoodUnmet = "labor rationing";
            alloc.Herders = herders; alloc.Farmers = farmers; alloc.FoodGatherers = foodGatherers;
            double budget = Math.Max(0, residents - herders - farmers - foodGatherers);   // persons left for goods
            alloc.ResidentGoodsBudget = budget;

            // The resolver-shaped candidate the tool demands read (herders and farmers staffed).
            var staffed = e.Copy();
            if (herders > 0) staffed.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Herder, Population = herders });
            if (farmers > 0) staffed.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Farmer, Population = farmers });
            var candidate = EconomicPlanner.Preview(c, staffed, residents);
            NeolithicProduction.DepreciateHousing(w, candidate, years);

            // ---- Bundles ----
            string Role(CraftRecipe r) => c.People.HasTech(r.Track) ? r.Specialist : WorkforceRoles.Craftsperson;
            bool Usable(CraftRecipe r)
                => r != null && !(r.SpecialistOnly && !c.People.HasTech(r.Track)) && !(r.AgricultureOnly && !CultivationEconomy.Enabled(c))
                   && !(r.HusbandryOnly && !CultivationEconomy.HusbandryEnabled(c)) && SpecialistProductivity.CraftRate(w, c, r, Role(r)) > 0;
            double craftMultiplier = d.HousingMultiplier;
            Bundle Craft(CraftGood good)
            {
                var r = byGood[good]; var b = new Bundle();
                double rate = SpecialistProductivity.CraftRate(w, c, r, Role(r));
                double m = ToolCoverageBudget.IsHousingWork(good) ? craftMultiplier : 1;
                if (!(rate > 0) || !(m > 0)) return null;
                b.RoleYears[Role(r)] = 1 / (rate * perYear * m);
                b.Outputs[good] = 1;
                if (r.Equipment) b.ToolOutputs = 1;
                b.Stone = r.Stone; b.Clay = r.Clay; b.Wood = r.Wood; b.Construction = r.ConstructionMaterial;
                b.Plant = r.PlantFood; b.Animal = r.AnimalFood;
                return b;
            }
            // Stock of made intermediates credited once, before any is made.
            double freeFabric = Math.Max(0, c.Inventory.AnimalFabric - c.TradeExport(CraftGood.AnimalFabric));
            double freeMaterial = Math.Max(0, c.Inventory.HousingMaterial - c.TradeExport(CraftGood.HousingMaterial));
            Bundle WithInput(Bundle b, double perUnit, CraftGood input, double units, ref double stock,
                bool fabric, out double reserved)
            {
                reserved = 0;
                if (b == null || !(perUnit > 0)) return b;
                double need = units * perUnit, fromStock = Math.Min(stock, need); stock -= fromStock;
                double madeShare = need > Epsilon ? (need - fromStock) / need : 0;
                if (fabric) b.Fabric += perUnit * (1 - madeShare); else b.Material += perUnit * (1 - madeShare);
                if (madeShare > Epsilon)
                {
                    var made = Usable(byGood[input]) ? Craft(input) : null;
                    if (made == null) { stock += fromStock; return null; }
                    b.Add(made, perUnit * madeShare);
                }
                reserved = fromStock;
                return b;
            }

            // ---- Requests ----
            Request GoodsRequest(string service, ServiceClass cls, CraftGood good, double units)
            {
                if (!(units > Epsilon) || !byGood.ContainsKey(good)) return null;
                var r = byGood[good];
                var req = new Request { Service = service, Class = cls, Good = good, Units = units };
                if (!Usable(r) || NeolithicProduction.Priority(candidate, good) <= 0) { req.Unmet = "missing capability"; alloc.Requests.Add(req); return req; }
                var b = Craft(good);
                if (r.AnimalFabric > 0)
                    b = WithInput(b, r.AnimalFabric, CraftGood.AnimalFabric, units, ref freeFabric, true,
                        out req.ReservedFabric);
                if (b != null && r.HousingMaterial > 0)
                    b = WithInput(b, r.HousingMaterial, CraftGood.HousingMaterial, units, ref freeMaterial, false,
                        out req.ReservedMaterial);
                if (b == null)
                {
                    freeFabric += req.ReservedFabric; freeMaterial += req.ReservedMaterial;
                    req.ReservedFabric = req.ReservedMaterial = 0;
                    req.Unmet = "missing capability"; alloc.Requests.Add(req); return req;
                }
                req.Bundle = b; alloc.Requests.Add(req); return req;
            }
            // Clothing: one service, this turn's consumption (continuity) and the useful reserve (recovery). Route: Cloth
            // where agriculture allows it, Animal Fabric for herding camps.
            var clothingRoute = CultivationEconomy.Enabled(c) && Usable(byGood[CraftGood.Cloth]) && NeolithicProduction.Priority(candidate, CraftGood.Cloth) > 0 ? CraftGood.Cloth : CraftGood.AnimalFabric;
            double clothingUseful = Math.Max(0, NeolithicProduction.Demand(w, candidate, clothingRoute, years, residents, false, false, d.Sources));
            double clothingNow = Math.Min(clothingUseful, Math.Max(0, d.ClothingNeedUnits));
            GoodsRequest("clothing", ServiceClass.Continuity, clothingRoute, clothingNow);
            GoodsRequest("clothing reserve", ServiceClass.Recovery, clothingRoute, Math.Max(0, clothingUseful - clothingNow));
            // Fabric this turn's clothing consumption will use (beyond the Cloth held) is not free for tents.
            double consumption = residents * n.FabricConsumptionPerPersonTurn * years / n.ReferenceTurnYears;
            freeFabric = Math.Max(0, freeFabric - Math.Max(0, consumption - Math.Max(0, c.Inventory.Cloth - c.TradeExport(CraftGood.Cloth))));
            // Shelter: unhoused people, replacement of this turn's decay first. Route: fixed Housing for farming camps,
            // tents for herding ones; one route claims the whole shelter service.
            double endDays = w.Days + years * cfg.DaysPerYear;
            double gapPeople = NeolithicProduction.HousingGapPeople(w, candidate, residents, endDays);
            double decayed = Math.Max(0, NeolithicProduction.HousingCapacity(w, c, w.Days).Total - NeolithicProduction.HousingCapacity(w, candidate, endDays).Total);
            double replacementPeople = Math.Min(gapPeople, decayed);
            // Permitted shelter form (v0.1 §6), one service with two routes and a fixed fallback order: the durable route
            // (fixed Housing, where the camp can build) replaces this turn's decay, keeping the housing share that social
            // cohesion rewards; the quick route (tents, from herd fabric) covers the remaining deficit. A camp with only one
            // possible route uses it for both. Each person is claimed by one route only.
            bool fixedPossible = CultivationEconomy.Enabled(c) && Usable(byGood[CraftGood.Housing]) && NeolithicProduction.Priority(candidate, CraftGood.Housing) > 0;
            bool tentsPossible = CultivationEconomy.HusbandryEnabled(c) && Usable(byGood[CraftGood.LightHousing]) && NeolithicProduction.Priority(candidate, CraftGood.LightHousing) > 0;
            CraftGood durable = fixedPossible ? CraftGood.Housing : tentsPossible ? CraftGood.LightHousing : CraftGood.Housing;
            CraftGood quick = tentsPossible ? CraftGood.LightHousing : fixedPossible ? CraftGood.Housing : CraftGood.LightHousing;
            double PerUnit(CraftGood g) => g == CraftGood.Housing ? n.HousingPeoplePerUnit : n.LightHousingPeoplePerUnit;
            var shelterFallbacks = new List<(Request Primary, CraftGood Fallback, double PeoplePerUnit)>();
            Request ShelterRequest(string service, ServiceClass cls, CraftGood primary, CraftGood fallback, double people)
            {
                var request = GoodsRequest(service, cls, primary, people / PerUnit(primary));
                if (request != null && people > Epsilon && primary != fallback)
                    shelterFallbacks.Add((request, fallback, PerUnit(primary)));
                return request;
            }
            ShelterRequest("shelter replacement", ServiceClass.Continuity, durable, quick, replacementPeople);
            // Construct the lower-priority deficit bundle only when Recovery is
            // reached. Otherwise it can reserve opening Fabric/Material before
            // Continuity has had a chance to form its alternate shelter route.
            double shelterDeficitPeople = Math.Max(0, gapPeople - replacementPeople);
            // Field and herd equipment for the work staffed (continuity of retained assets).
            GoodsRequest("farm tools", ServiceClass.Continuity, CraftGood.FarmTools, Math.Max(0, NeolithicProduction.Demand(w, candidate, CraftGood.FarmTools, years, residents, false, false, d.Sources)));
            GoodsRequest("herding tools", ServiceClass.Continuity, CraftGood.HerdingTools, Math.Max(0, NeolithicProduction.Demand(w, candidate, CraftGood.HerdingTools, years, residents, false, false, d.Sources)));
            // Storage: the Pots the food reserve needs beyond natural storage.
            GoodsRequest("storage", ServiceClass.Recovery, CraftGood.Pots, Math.Max(0, NeolithicProduction.PotDemand(w, c, years, residents)));
            void LabourRequest(string service, ServiceClass cls, string role, double persons)
            {
                if (!(persons > Epsilon)) return;
                var b = new Bundle(); b.RoleYears[role] = years;
                alloc.Requests.Add(new Request { Service = service, Class = cls, Role = role, Units = persons, Bundle = b });
            }
            // The extra workers needed to restore the operating food reserve
            // compete in Recovery with the other deficit-repair services. They
            // are explicit Gatherer assignments: this reserves resident time
            // without changing the resolver's total gatherer semantics
            // (unassigned residents plus explicitly assigned Gatherers).
            LabourRequest("food reserve", ServiceClass.Recovery, WorkforceRoles.Gatherer,
                Math.Max(0, alloc.ConsumptionReserveFoodGatherers - foodGatherers));
            // Herd service (slice 2; Addendum A §A4: continuity of a retained asset, so other work cannot displace it). The
            // herd policy grows the herd by one turn of breeding but never beyond the local pasture's forage and water; it
            // requests the herders that herd needs beyond the current herd's (survival). An oversized herd is held at the
            // pasture, the excess slaughtered for food (HerdTarget).
            double herdTarget = -1;
            if (d.HusbandryEnabled && HerdingTooling.DomesticAnimals(w, c) > 0)
            {
                var grown = TurnResolver.ForecastGrownManagedAnimalFood(w, c, e.ManagedBreedingIntensity, intervals, years, c.Tile);
                HerdLabour.Pasture(w, c, grown, intervals, out double pasture, out _);
                double current = HerdingTooling.DomesticAnimals(w, c);
                herdTarget = Math.Max(0, Math.Min(Math.Max(current, grown.Animals), pasture));
                LabourRequest("herd growth", ServiceClass.Continuity, WorkforceRoles.Herder, Math.Max(0, HerdingTooling.RequiredHerdersFor(w, c, years, herdTarget) - herders));
            }
            // Field development (slice 2; v0.1 §13): authorized by a projected
            // food-security gap, never by spare labour or by a maximum-birth
            // target. Consumption, reserve recovery and accepted Cloth feedstock
            // beyond the current supply are closed by the expansion ladder's
            // physical steps, best first; build work remains Development.
            if (!NoFieldDevelopmentDiagnostic && CultivationEconomy.Enabled(c) && d.Footprint.Length > 0)
            {
                if (ResolutionMetrics.CaptureDemandAllocations)
                {
                    alloc.FieldExpansionEligible = true;
                    var potential = CultivationEconomy.ExpansionLadder(w, c, years, d.Footprint);
                    alloc.FieldPotentialSteps = 0;
                    foreach (var step in potential)
                    {
                        if (!(step.AnnualGain > 0) || !(step.WorkerYears > 0)) continue;
                        alloc.FieldPotentialSteps++; alloc.FieldPotentialGainPerYear += step.AnnualGain; alloc.FieldPotentialWorkerYears += step.WorkerYears;
                    }
                }
                double feedstock = clothingRoute == CraftGood.Cloth ? clothingNow * n.PlantFoodPerFabricUnit : 0;
                double gapPerYear = Math.Max(0, consumptionIncomeTarget + reserveFillTarget + feedstock - d.Supply) / years;
                if (gapPerYear > Epsilon)
                {
                    double gain = 0, buildYears = 0;
                    double workerYearBudget = budget * years;
                    foreach (var step in CultivationEconomy.ExpansionLadder(w, c, years, d.Footprint, gapPerYear))
                    {
                        if (gain >= gapPerYear - Epsilon || buildYears >= workerYearBudget - Epsilon) break;
                        if (!(step.AnnualGain > 0) || !(step.WorkerYears > 0)) continue;
                        double acceptedYears = Math.Min(step.WorkerYears, workerYearBudget - buildYears);
                        double acceptedFraction = acceptedYears / step.WorkerYears;
                        gain += step.AnnualGain * acceptedFraction; buildYears += acceptedYears;
                        if (ResolutionMetrics.CaptureDemandAllocations)
                        {
                            alloc.FieldExpansionSteps++;
                            alloc.FieldExpansionMinimumScore = Math.Min(alloc.FieldExpansionMinimumScore, step.Score);
                        }
                    }
                    if (ResolutionMetrics.CaptureDemandAllocations)
                    {
                        alloc.FieldExpansionGapPerYear = gapPerYear;
                        alloc.FieldExpansionGainPerYear = gain;
                        alloc.FieldExpansionWorkerYears = buildYears;
                    }
                    LabourRequest("field expansion", ServiceClass.Development, WorkforceRoles.Farmer, buildYears / years);
                }
            }

            // ---- Toolmaking: the route with the least labour per tool output ----
            Bundle toolmaking = null; CraftGood toolGood = CraftGood.HandTools; double outputsPerTool = ToolCoverageBudget.OutputsPerHandTool;
            foreach (var (good, outputs) in new[] { (CraftGood.Tools, ToolCoverageBudget.OutputsPerTool), (CraftGood.HandTools, ToolCoverageBudget.OutputsPerHandTool) })
            {
                if (!Usable(byGood[good])) continue;
                var b = Craft(good); if (b == null) continue;
                double years1 = b.RoleYears.Values.Sum() / outputs;
                if (toolmaking == null || years1 < toolmaking.RoleYears.Values.Sum() / outputsPerTool) { toolmaking = b; toolGood = good; outputsPerTool = outputs; }
            }
            double coverage = Math.Max(0, c.Inventory.HandTools) * ToolCoverageBudget.OutputsPerHandTool + Math.Max(0, c.Inventory.Tools) * ToolCoverageBudget.OutputsPerTool;

            // ---- Resources ----
            double Inventory(MaterialId m) => Math.Max(0, MaterialEconomy.Inventory(c, m));
            double Accessible(MaterialId m) => d.Accessible.TryGetValue(m, out double a) ? Math.Max(0, a) : 0;
            string ExtractionRole(MaterialId m)
            {
                string role = MaterialEconomy.Specialist(m);
                return c.People.HasTech(EconomicIntents.RequiredTechnology(role)) ? role : WorkforceRoles.Gatherer;
            }
            double ExtractionYears(MaterialId m, double amount)
            {
                if (!(amount > Epsilon)) return 0;
                double rate = n.SpecialistMaterialExtractionRate > 0 ? SpecialistProductivity.ExtractionRate(w, c, m, ExtractionRole(m)) * perYear : 0;
                return rate > 0 ? amount / rate : double.PositiveInfinity;
            }

            double FeedstockGatherers(Bundle total, Bundle currentClothing)
            {
                // Credit opening Plant Food once. Current clothing may use the
                // stock left after meals; every other recipe may use only stock
                // left after meals and the operating reserve. Any remainder is
                // an incoming terrestrial claim and therefore reserves gatherer
                // time.
                double currentOpeningPlant = Math.Min(currentClothing.Plant, d.SparePlantClothing);
                double normalPlant = Math.Max(0, total.Plant - currentClothing.Plant);
                double normalOpeningPlant = Math.Min(normalPlant, Math.Max(0, d.SparePlant - currentOpeningPlant));
                // Prototype: the committed clothing split is paid from food the
                // kernel already debited (it displaces meals), so it reserves no
                // extra gatherer time.
                double incomingPlant = (protectedSplit ? 0 : Math.Max(0, currentClothing.Plant - currentOpeningPlant))
                    + Math.Max(0, normalPlant - normalOpeningPlant);
                return gatherRate > 0 ? incomingPlant / gatherRate : 0;
            }

            // Totals for a set of grants: role worker-years (extraction and gathering included), and the binding check.
            string Binding(Func<Request, double> grant)
            {
                var total = new Bundle(); var currentClothing = new Bundle();
                foreach (var q in alloc.Requests) if (q.Bundle != null)
                {
                    double granted = grant(q); total.Add(q.Bundle, granted);
                    if (q.Service == "clothing") currentClothing.Add(q.Bundle, granted);
                }
                // Tool service beyond start coverage is made, at its own labour and stone/wood.
                double shortfall = Math.Max(0, total.ToolOutputs - coverage);
                if (shortfall > Epsilon) { if (toolmaking == null) return "equipment"; total.Add(toolmaking, shortfall / outputsPerTool); }
                // Construction material for Housing Material: Wood first, then Clay.
                double wood = total.Wood, clay = total.Clay;
                double woodRoom = Inventory(MaterialId.Wood) + Accessible(MaterialId.Wood);
                double constructionWood = Math.Min(total.Construction, Math.Max(0, woodRoom - wood));
                wood += constructionWood; clay += total.Construction - constructionWood;
                var raw = new Dictionary<MaterialId, double> { [MaterialId.Stone] = total.Stone, [MaterialId.Clay] = clay, [MaterialId.Wood] = wood };
                double persons = 0, gathererYears = 0;
                foreach (var m in MaterialEconomy.Materials)
                {
                    double extract = Math.Max(0, raw[m] - Inventory(m));
                    if (extract > Accessible(m) + Epsilon) return "material " + m;
                    double y = ExtractionYears(m, extract);
                    if (double.IsInfinity(y)) return "material " + m;
                    if (ExtractionRole(m) == WorkforceRoles.Gatherer) gathererYears += y; else persons += Math.Ceiling(y / years - Epsilon);
                }
                foreach (var role in total.RoleYears) persons += role.Value / years;
                // Only this cohort's current clothing input may displace the
                // reserve refill. Recovery clothing and tent feedstock remain
                // ordinary manufacturing. Using max(normal, clothing) for the
                // aggregate bundle lets an unrelated input borrow the clothing
                // exception (notably Animal Food for tents when clothing uses
                // Plant Food), promising work the resolver cannot fund.
                double BeforeReserveLimit(double normal, double clothing, double protectedInput)
                    => Math.Min(clothing, normal + protectedInput);
                // Prototype: committed clothing may displace meals, bounded by the food that can exist this turn.
                double plantClothingEnvelope = protectedSplit ? Math.Min(d.TerrestrialAfterHerds + Math.Max(0, c.PlantFood), Math.Max(0, c.Food + d.Supply)) : d.PlantEnvelopeClothing;
                double animalClothingEnvelope = protectedSplit ? (d.HusbandryEnabled ? Math.Min(Math.Max(0, c.AnimalFood) + d.ManagedFood.AnimalFood, Math.Max(0, c.Food + d.Supply)) : 0) : d.AnimalEnvelopeClothing;
                double roomClothingEnvelope = protectedSplit ? Math.Max(0, c.Food + d.Supply) : d.FoodRoomEnvelopeClothing;
                if (total.Plant > BeforeReserveLimit(d.PlantEnvelope, plantClothingEnvelope, currentClothing.Plant) + Epsilon) return "plant feedstock";
                if (total.Animal > BeforeReserveLimit(d.AnimalEnvelope, animalClothingEnvelope, currentClothing.Animal) + Epsilon) return "animal feedstock";
                if (d.HusbandryEnabled && total.Plant + total.Animal > BeforeReserveLimit(d.FoodRoomEnvelope,
                    roomClothingEnvelope, currentClothing.Plant + currentClothing.Animal) + Epsilon) return "food room";
                if (total.Fabric > Math.Max(0, c.Inventory.AnimalFabric) + Epsilon) return "fabric";
                if (total.Material > Math.Max(0, c.Inventory.HousingMaterial) + Epsilon) return "housing material";
                double feedstockGatherers = FeedstockGatherers(total, currentClothing);
                persons += gathererYears / years + feedstockGatherers;
                return persons > budget + Epsilon ? "labor" : null;
            }

            // ---- Option-3 prototype: score (clothing, shelter) splits with the demographic kernel ----
            if (KernelSplitDiagnostic && PhysicalDemography.Enabled(cfg))
            {
                long started = System.Diagnostics.Stopwatch.GetTimestamp();
                int calls = 0;
                double clothingStock = Math.Max(0, c.Inventory.Cloth - c.TradeExport(CraftGood.Cloth))
                    + Math.Max(0, c.Inventory.AnimalFabric - c.TradeExport(CraftGood.AnimalFabric));
                double effective = Math.Max(0, c.Population + c.GrowthRemainder - c.DeathRemainder + (d.Consumers - c.Population));
                double maximumYears = PhysicalDemography.BirthOnlyPersonYears(effective, cfg.BaseBirthRate, years);
                // Clothing: sized against the birth-only person-year envelope (not the opening count), capped at useful demand.
                var clothBundle = Usable(byGood[clothingRoute]) && NeolithicProduction.Priority(candidate, clothingRoute) > 0 ? Craft(clothingRoute) : null;
                double clothPersons = clothBundle != null ? clothBundle.RoleYears.Values.Sum() / years : double.NaN;
                double clothFood = clothBundle != null ? clothBundle.Plant + clothBundle.Animal : 0;
                double clothingMax = clothBundle == null ? 0 : Math.Min(clothingUseful,
                    Math.Max(0, PhysicalDemography.ClothingPerPersonYear(cfg) * maximumYears - clothingStock));
                // Shelter: complete per-person cost of the durable route (craft, made intermediates, extraction).
                double shelterPersons = double.NaN, shelterFood = 0;
                if (byGood.ContainsKey(durable) && Usable(byGood[durable]) && NeolithicProduction.Priority(candidate, durable) > 0)
                {
                    var r = byGood[durable]; var b = Craft(durable);
                    if (b != null)
                    {
                        double persons = b.RoleYears.Values.Sum() / years, food = b.Plant + b.Animal;
                        double wood = b.Wood + b.Construction, clay = b.Clay, stone = b.Stone;
                        bool ok = true;
                        foreach (var (input, perUnit) in new[] { (CraftGood.HousingMaterial, r.HousingMaterial), (CraftGood.AnimalFabric, r.AnimalFabric) })
                        {
                            if (!(perUnit > 0)) continue;
                            var m = Usable(byGood[input]) ? Craft(input) : null;
                            if (m == null) { ok = false; break; }
                            persons += perUnit * m.RoleYears.Values.Sum() / years; food += perUnit * (m.Plant + m.Animal);
                            wood += perUnit * (m.Wood + m.Construction); clay += perUnit * m.Clay; stone += perUnit * m.Stone;
                        }
                        double extraction = ExtractionYears(MaterialId.Wood, wood) + ExtractionYears(MaterialId.Clay, clay) + ExtractionYears(MaterialId.Stone, stone);
                        if (ok && !double.IsInfinity(extraction))
                        {
                            persons += extraction / years;
                            shelterPersons = persons / PerUnit(durable); shelterFood = food / PerUnit(durable);
                        }
                    }
                }
                double shelterMax = double.IsNaN(shelterPersons) ? 0 : gapPeople;
                var capacityNow = NeolithicProduction.HousingCapacity(w, candidate, endDays);
                double capacityEnd = capacityNow.Total;
                int workers = Math.Max(0, residents - herders - farmers);
                double harvestNow = Math.Min(d.TerrestrialAfterHerds, farmers * farmRate);
                double bestScore = double.NegativeInfinity; double bestC = 0, bestH = 0; int bestCi = 0, bestHi = 0;
                var scored = new List<(int ci, int hi, double qc, double qh, double py, double qol, double end, double deaths)>();
                for (int ci = 0; ci < KernelClothingGrid.Length; ci++)
                {
                    if (KernelShelterOnlyDiagnostic && ci > 0) break;
                    double qc = clothingMax * KernelClothingGrid[ci];
                    if (ci > 0 && !(qc > Epsilon)) continue;
                    for (int hi = 0; hi < KernelShelterGrid.Length; hi++)
                    {
                        double qh = shelterMax * KernelShelterGrid[hi];
                        if (hi > 0 && !(qh > Epsilon)) continue;
                        double persons = (qc > 0 ? qc * clothPersons : 0) + (qh > 0 ? qh * shelterPersons : 0);
                        if (persons > workers + Epsilon) continue;
                        double gatherers = Math.Max(0, workers - Math.Max(persons, 0));
                        double income = Math.Min(d.ForageAfterHerds, harvestNow + gatherers * gatherRate) + d.ManagedFood.AnimalFood;
                        double protectedFood = qc * clothFood + qh * shelterFood;
                        double food = TurnResolver.PhysicalDemographicFoodBudget(Math.Max(0, c.Food) + income, d.Meals, maximumGrowthFood, protectedFood);
                        var result = PhysicalDemography.AdvanceBudget(cfg, effective, years, food, clothingStock + qc, capacityEnd + qh, c.FoodDeficitYears, true);
                        calls++;
                        double people = years > 0 ? result.PersonYears / years : effective;
                        double clothingTerm = 1 - c.RudimentaryExpectation * (1 - result.ClothingCoverage);
                        double housingTerm = 1 - c.RudimentaryExpectation * (1 - result.AverageHousingCoverage);
                        var shelter = new NeolithicProduction.ShelterCapacity(capacityNow.Fixed + (durable == CraftGood.Housing ? qh : 0), capacityNow.Light + (durable == CraftGood.Housing ? 0 : qh));
                        double qol = result.Nutrition * c.WaterNeedsMetRatio * (n.RudimentaryWeightClothing * clothingTerm + n.RudimentaryWeightHousing * housingTerm)
                            * NeolithicProduction.SocialCohesion(n, NeolithicProduction.HousingShare(n, shelter, people), people);
                        scored.Add((ci, hi, qc, qh, result.PersonYears, qol, result.FinalPopulation, result.Deaths));
                    }
                }
                // Objectives (diagnostic): 0 person-years; 1 QoL-weighted person-years; 2 lexicographic (person-years
                // within KernelLexTolerance of the best, then QoL); 3 welfare with a terminal-population term,
                // (Y + T*N_end)*Q; 4 deaths averted, Y - T*Deaths. Ties keep the cheaper (earlier) candidate.
                double Score((int ci, int hi, double qc, double qh, double py, double qol, double end, double deaths) s)
                    => KernelSplitObjective == 1 ? s.py * s.qol
                    : KernelSplitObjective == 3 ? (s.py + years * s.end) * s.qol
                    : KernelSplitObjective == 4 ? s.py - years * s.deaths
                    : s.py;
                double maxPersonYears = scored.Count > 0 ? scored.Max(s => s.py) : 0;
                foreach (var s in scored)
                {
                    double score = KernelSplitObjective == 2
                        ? (s.py >= maxPersonYears * (1 - KernelLexTolerance) - 1e-12 ? s.qol : double.MinValue)
                        : Score(s);
                    if (score > bestScore + Math.Abs(bestScore) * 1e-9 + 1e-12 || double.IsNegativeInfinity(bestScore))
                    { bestScore = score; bestC = s.qc; bestH = s.qh; bestCi = s.ci; bestHi = s.hi; }
                }
                System.Threading.Interlocked.Add(ref KernelSplitCalls, calls);
                System.Threading.Interlocked.Add(ref KernelSplitTicks, System.Diagnostics.Stopwatch.GetTimestamp() - started);
                System.Threading.Interlocked.Increment(ref KernelSplitCamps);
                System.Threading.Interlocked.Increment(ref KernelChosenClothing[bestCi]);
                System.Threading.Interlocked.Increment(ref KernelChosenShelter[bestHi]);

                // Enact: replace the clothing and shelter requests with the committed Protected split.
                void Drop(Request q)
                {
                    if (q == null) return;
                    freeFabric += q.ReservedFabric; freeMaterial += q.ReservedMaterial;
                    alloc.Requests.Remove(q); shelterFallbacks.RemoveAll(x => x.Primary == q);
                }
                foreach (var q in alloc.Requests.Where(q => (!KernelShelterOnlyDiagnostic && (q.Service == "clothing" || q.Service == "clothing reserve")) || q.Service == "shelter replacement").ToList()) Drop(q);
                protectedSplit = !KernelShelterOnlyDiagnostic; kernelEnacted = true;
                Request protectedClothing = null;
                if (!KernelShelterOnlyDiagnostic)
                {
                    protectedClothing = GoodsRequest("clothing", ServiceClass.Protected, clothingRoute, bestC);
                    GoodsRequest("clothing reserve", ServiceClass.Recovery, clothingRoute, Math.Max(0, clothingUseful - bestC));
                }
                if (bestH > Epsilon) ShelterRequest("shelter protected", ServiceClass.Protected, durable, quick, bestH);
                if (replacementPeople - bestH > Epsilon) ShelterRequest("shelter replacement", ServiceClass.Continuity, durable, quick, replacementPeople - bestH);
                shelterDeficitPeople = Math.Max(0, gapPeople - Math.Max(bestH, replacementPeople));
                // The committed persons may come out of the gatherer reservation; lower classes keep only the original slack.
                double committedPersons = (bestC > 0 ? bestC * clothPersons : 0) + (bestH > 0 ? bestH * shelterPersons : 0);
                budget = Math.Max(budget, Math.Min(workers, committedPersons));
                alloc.ResidentGoodsBudget = budget;
                protectedClothingUnits = protectedClothing != null ? 0 : -1;
            }

            // ---- Elder arbitration: classes in order, an equal fraction within a class, freezing saturated inputs ----
            void Arbitrate(List<Request> active)
            {
                if (active.Count == 0) return;
                for (int pass = 0; pass < 8 && active.Count > 0; pass++)
                {
                    double Grant(Request q, double f) => active.Contains(q) ? q.Units * f : q.Granted;
                    if (Binding(q => Grant(q, 1)) == null) { foreach (var q in active) q.Granted = q.Units; break; }
                    double lo = 0, hi = 1;
                    for (int i = 0; i < 40; i++) { double mid = (lo + hi) / 2; if (Binding(q => Grant(q, mid)) == null) lo = mid; else hi = mid; }
                    string binding = Binding(q => Grant(q, hi)) ?? "labor";
                    foreach (var q in active) q.Granted = q.Units * lo;
                    // Labour and equipment bind every request alike; an input binds only the requests that use it.
                    if (binding == "labor" || binding == "equipment") { foreach (var q in active) q.Unmet = binding == "labor" ? "labor rationing" : "equipment shortage"; break; }
                    var users = active.Where(q => Uses(q.Bundle, binding)).ToList();
                    if (users.Count == 0) users = active.ToList();
                    foreach (var q in users) { q.Unmet = binding.StartsWith("material") ? "material shortage" : binding; active.Remove(q); }
                }
            }
            void SettleReservation(Request q)
            {
                if (q == null || q.ReservationSettled) return;
                double fraction = q.Units > Epsilon ? Numbers.Clamp(q.Granted / q.Units, 0, 1) : 0;
                freeFabric += q.ReservedFabric * (1 - fraction);
                freeMaterial += q.ReservedMaterial * (1 - fraction);
                q.ReservedFabric *= fraction; q.ReservedMaterial *= fraction;
                q.ReservationSettled = true;
            }
            foreach (ServiceClass cls in protectedSplit || kernelEnacted
                ? new[] { ServiceClass.Protected, ServiceClass.Continuity, ServiceClass.Recovery, ServiceClass.Development }
                : new[] { ServiceClass.Continuity, ServiceClass.Recovery, ServiceClass.Development })
            {
                if (cls == ServiceClass.Recovery)
                    ShelterRequest("shelter deficit", ServiceClass.Recovery, quick, durable, shelterDeficitPeople);
                var primary = alloc.Requests.Where(q => q.Class == cls && q.Bundle != null).ToList();
                // Arbitration removes input-bound requests from its working set
                // as each saturated input freezes. Keep the complete class list
                // so even a zero-granted frozen request returns its tentative
                // opening-intermediate reservation.
                Arbitrate(primary.ToList());
                foreach (var request in primary) SettleReservation(request);
                // Route fallback is one bounded repair inside the already chosen
                // shelter service. Move only the primary route's unserved people
                // to the alternate route, so the two requests never claim the
                // same person. Existing grants and every higher-priority debit
                // stay frozen; no forecast or strategy search is rerun.
                var fallbacks = new List<Request>();
                foreach (var route in shelterFallbacks.Where(x => x.Primary.Class == cls))
                {
                    double unservedPeople = Math.Max(0, route.Primary.Units - route.Primary.Granted) * route.PeoplePerUnit;
                    if (!(unservedPeople > Epsilon)) continue;
                    route.Primary.Units = route.Primary.Granted;
                    var fallback = GoodsRequest(route.Primary.Service, cls, route.Fallback,
                        unservedPeople / PerUnit(route.Fallback));
                    if (fallback?.Bundle != null) fallbacks.Add(fallback);
                }
                Arbitrate(fallbacks.ToList());
                foreach (var fallback in fallbacks) SettleReservation(fallback);
            }

            // ---- Integer staffing, compiled per role after aggregation; at most one repair pass ----
            for (int repair = 0; repair < 2; repair++)
            {
                var total = new Bundle();
                foreach (var q in alloc.Requests) if (q.Bundle != null) total.Add(q.Bundle, q.Granted);
                double shortfall = Math.Max(0, total.ToolOutputs - coverage);
                double toolsMade = shortfall > Epsilon && toolmaking != null ? shortfall / outputsPerTool : 0;
                if (toolsMade > 0) total.Add(toolmaking, toolsMade);
                double wood = total.Wood, clay = total.Clay;
                double constructionWood = Math.Min(total.Construction, Math.Max(0, Inventory(MaterialId.Wood) + Accessible(MaterialId.Wood) - wood));
                wood += constructionWood; clay += total.Construction - constructionWood;
                var raw = new Dictionary<MaterialId, double> { [MaterialId.Stone] = total.Stone, [MaterialId.Clay] = clay, [MaterialId.Wood] = wood };
                var staff = new Dictionary<string, int>(); double materialGatherers = 0;
                foreach (var role in total.RoleYears.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    int people = (int)Math.Ceiling(role.Value / years - Epsilon);
                    if (people > 0) staff[role.Key] = (staff.TryGetValue(role.Key, out int s) ? s : 0) + people;
                }
                foreach (var m in MaterialEconomy.Materials)
                {
                    double y = ExtractionYears(m, Math.Max(0, raw[m] - Inventory(m)));
                    if (!(y > 0) || double.IsInfinity(y)) continue;
                    if (ExtractionRole(m) == WorkforceRoles.Gatherer) materialGatherers += y / years;
                    else { int people = (int)Math.Ceiling(y / years - Epsilon); if (people > 0) staff[ExtractionRole(m)] = (staff.TryGetValue(ExtractionRole(m), out int s) ? s : 0) + people; }
                }
                var currentClothing = new Bundle();
                foreach (var q in alloc.Requests.Where(q => q.Service == "clothing" && q.Bundle != null))
                    currentClothing.Add(q.Bundle, q.Granted);
                double feedstockGatherers = FeedstockGatherers(total, currentClothing);
                double needed = staff.Values.Sum() + materialGatherers + feedstockGatherers;
                alloc.FinalCompiledPersons = needed;
                if (needed <= budget + Epsilon || repair == 1)
                {
                    alloc.PlannedStoneExtraction = Math.Max(0, raw[MaterialId.Stone] - Inventory(MaterialId.Stone));
                    alloc.PlannedClayExtraction = Math.Max(0, raw[MaterialId.Clay] - Inventory(MaterialId.Clay));
                    alloc.PlannedWoodExtraction = Math.Max(0, raw[MaterialId.Wood] - Inventory(MaterialId.Wood));
                    foreach (var p in staff) alloc.Staff[p.Key] = p.Value;
                    alloc.MaterialGatherers = materialGatherers; alloc.FeedstockGatherers = feedstockGatherers; alloc.ToolsMade = toolsMade;
                    foreach (var p in total.Outputs) alloc.Ceilings[p.Key] = p.Value;
                    if (toolsMade > 0) alloc.Ceilings[toolGood] = (alloc.Ceilings.TryGetValue(toolGood, out double t) ? t : 0) + toolsMade;
                    // A final over-budget (rounding after the repair) drops the lowest-class work in stable order.
                    if (needed > budget + Epsilon) { alloc.TrimPasses++; TrimToBudget(alloc, budget, needed); }
                    break;
                }
                // Repair: rounding cost `needed - budget` persons; scale the lowest classes down by that share and recompile.
                alloc.RepairPasses++;
                double excess = needed - budget;
                foreach (var cls in new[] { ServiceClass.Development, ServiceClass.Recovery, ServiceClass.Continuity })
                {
                    var group = alloc.Requests.Where(q => q.Class == cls && q.Granted > Epsilon).ToList();
                    double groupPersons = group.Sum(q => q.Granted * q.Bundle.RoleYears.Values.Sum() / years);
                    if (!(groupPersons > Epsilon)) continue;
                    double cut = Math.Min(1, (excess + 1) / groupPersons);
                    foreach (var q in group) { q.Granted *= 1 - cut; q.Unmet = "integer staffing"; }
                    excess -= groupPersons * cut;
                    if (excess <= 0) break;
                }
            }

            // Integer repair/trim happens after there are no more routes to
            // construct. Refresh the diagnostic reservation amounts to the
            // final grants; any resulting slack is simply left in inventory.
            foreach (var q in alloc.Requests.Where(q => q.Bundle != null && q.ReservationSettled))
            {
                q.ReservedFabric = q.Bundle.Fabric * q.Granted;
                q.ReservedMaterial = q.Bundle.Material * q.Granted;
            }

            // ---- Compile the intent ----
            var intent = e;
            intent.Labor = new EconomicLaborPlan();
            if (herders > 0) intent.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Herder, Population = herders });
            if (farmers > 0) intent.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Farmer, Population = farmers });
            foreach (var p in alloc.Staff.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                var existing = intent.Assignments.FirstOrDefault(a => a.Role == p.Key);
                if (existing != null) existing.Population += p.Value; else intent.Assignments.Add(new PopulationAssignment { Role = p.Key, Population = p.Value });
            }
            intent.Assignments = intent.Assignments.Where(a => a.Population > 0).OrderBy(a => a.Role, StringComparer.Ordinal).ToList();
            int nonGatherers = intent.Assignments.Sum(a => a.Population);
            intent.Labor.MaterialGatherers = Math.Min(Math.Max(0, residents - nonGatherers), alloc.MaterialGatherers);
            intent.Labor.EquipmentTarget = Math.Min(residents, alloc.Staff.Where(p => !WorkforceRoles.Extractors.Contains(p.Key)
                && p.Key != WorkforceRoles.Gatherer && p.Key != WorkforceRoles.Herder && p.Key != WorkforceRoles.Farmer).Sum(p => p.Value));
            // Build farmers are the field-expansion staff, on top of the operating farmers.
            intent.Labor.ExpansionFarmers = alloc.Staff.TryGetValue(WorkforceRoles.Farmer, out int builders) ? builders : 0;
            // The herd is held at what the staffed herders manage, within the policy's target; breeding is capped to it.
            if (herdTarget >= 0)
            {
                int staffedHerders = intent.Assignments.Where(a => a.Role == WorkforceRoles.Herder).Sum(a => a.Population);
                int target = (int)Math.Floor(Math.Min(herdTarget, HerdingTooling.AnimalsManagedBy(w, c, years, staffedHerders)) + Epsilon);
                intent.HerdTargetSet = true; intent.HerdTarget = Math.Max(0, target);
                intent.ManagedBreedingIntensity = HerdLabour.CapBreeding(w, c, intent.ManagedBreedingIntensity, intent.HerdTarget, years);
            }
            // Trade's labour price (Addendum A §A2), lagged like the Value planner's: the value of a spare resident's
            // gathering. The marginal-bundle price under binding labour is a later slice.
            if (cfg.TradeEnabled)
            {
                double w0 = Math.Max(0, ExpansionValuation.FoodValue(cfg, c)) * cfg.GatherPerPerson * perYear;
                if (w0 > 0 && !double.IsInfinity(w0)) { intent.Labor.LabourPriceSet = true; intent.Labor.LabourPrice = w0; }
            }
            // Production order: toolmaking, then continuity and recovery goods with intermediates before their consumers.
            var order = new List<CraftGood> { CraftGood.Tools, CraftGood.HandTools };
            foreach (var q in alloc.Requests.OrderBy(q => q.Class))
            {
                if (q.Bundle == null) continue;
                if (!q.Good.HasValue) continue;
                foreach (var g in new[] { CraftGood.AnimalFabric, CraftGood.HousingMaterial, q.Good.Value })
                    if (q.Bundle.Outputs.ContainsKey(g) && !order.Contains(g)) order.Add(g);
            }
            foreach (var r in recipes) if (!order.Contains(r.Good)) order.Add(r.Good);
            foreach (var r in recipes) if (!alloc.Ceilings.ContainsKey(r.Good)) alloc.Ceilings[r.Good] = 0;
            production = new ProductionOrder(order.ToArray(), alloc.Ceilings);
            if (protectedSplit && protectedClothingUnits >= 0)
                production.ProtectedClothingUnits = alloc.Requests.Where(q => q.Service == "clothing" && q.Class == ServiceClass.Protected).Sum(q => q.Granted);
            return intent;
        }

        private static bool Uses(Bundle b, string binding)
        {
            if (b == null) return false;
            switch (binding)
            {
                case "plant feedstock": return b.Plant > 0;
                case "animal feedstock": return b.Animal > 0;
                case "food room": return b.Plant + b.Animal > 0;
                case "fabric": return b.Fabric > 0;
                case "housing material": return b.Material > 0;
                case "material Stone": return b.Stone > 0;
                case "material Clay": return b.Clay > 0 || b.Construction > 0;
                case "material Wood": return b.Wood > 0 || b.Construction > 0;
                default: return true;
            }
        }

        // Removes the lowest-class granted work, last request first, until the compiled staffing fits.
        private static void TrimToBudget(Allocation a, double budget, double needed)
        {
            foreach (var q in a.Requests.Where(q => q.Granted > Epsilon).OrderByDescending(q => q.Class).ThenByDescending(q => a.Requests.IndexOf(q)))
            {
                if (needed <= budget + Epsilon) break;
                foreach (var role in q.Bundle.RoleYears)
                    if (a.Staff.TryGetValue(role.Key, out int s) && s > 0) { a.Staff[role.Key] = s - 1; needed -= 1; if (a.Staff[role.Key] == 0) a.Staff.Remove(role.Key); }
                foreach (var g in q.Bundle.Outputs) if (a.Ceilings.ContainsKey(g.Key)) a.Ceilings[g.Key] = Math.Max(0, a.Ceilings[g.Key] - g.Value * q.Granted);
                q.Granted = 0; q.Unmet = "integer staffing";
            }
        }
    }
}
