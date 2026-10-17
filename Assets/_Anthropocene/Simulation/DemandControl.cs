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
            => Plan(w, c, settings, null, out production);

        // TurnPlanning may supply the access plan it already built for this accepted destination. The normal path keeps
        // the same single physical-demand pass, which creates the access plan only when no reusable one was supplied.
        internal static EconomicIntent Plan(WorldState w, Camp c, EconomicIntent settings, FishingAccessPlan fishingAccess, out ProductionOrder production)
            => Plan(w, c, settings, fishingAccess, out production, out _);
        internal static EconomicIntent Plan(WorldState w, Camp c, EconomicIntent settings, out ProductionOrder production,
            out DemandController.Allocation allocation)
            => Plan(w, c, settings, null, out production, out allocation);
        internal static EconomicIntent Plan(WorldState w, Camp c, EconomicIntent settings, FishingAccessPlan fishingAccess,
            out ProductionOrder production, out DemandController.Allocation allocation)
        {
            allocation = null;
            if (IsDemandControl(w.Config)) return DemandController.Plan(w, c, settings, fishingAccess, out production, out allocation);
            return EconomicPlanner.Plan(w, c, settings, EconomicPlanner.ConfiguredRule(w.Config), out _, out production);
        }
    }

    // DemandControl v0.1, slice 1 (Docs/EconomicControlContract.md with Addendum A).
    //
    // Pure and per camp: it reads the committed camp (a planning view) and returns a legal EconomicIntent plus a production
    // order whose ceilings the forecast and resolver honour. Single-service controllers make requests:
    //   Survival     food for the prepared cohort through the supervisor's retained field/herd portfolio and a shared
    //                gatherer/Fisher frontier;
    //   Continuity   this turn's clothing consumption, retained shelter replacement, and the field/herd/equipment work
    //                authorized by that portfolio, including Fishing Tools for staffed Fishers;
    //   Recovery     the clothing reserve, the shelter deficit, Pots for the storage gap.
    // Development covers explicitly authorized field and herd increments. With the kernel-scored Elder split
    // (SimulationConfig.ElderKernelSplit, Docs/ClothingCoverageAndSurplusBirths.md) the current clothing and part of the
    // shelter backlog become a Protected class ahead of every other class: the Elder scores a grid of clothing and shelter
    // levels with the demographic kernel and commits the best. Each request is a complete bundle:
    // craft time, made intermediates (Animal Fabric for tents, Housing Material for Housing), raw extraction beyond stock,
    // feedstock and its gathering, and tool service (toolmaking at the urgency of the work it enables). The Elder grants
    // Continuity and Recovery use bounded equal fulfilment; Development admits
    // complete marginal increments in a common finite-horizon return order. Then
    // integer staffing is compiled per role with at most one repair pass. Cost is
    // O(requests x bisection) per camp, one physical demand pass, and no Value search.
    internal static class DemandController
    {
        // Protected holds the kernel-scored clothing and shelter split; it is arbitrated before Continuity.
        internal enum ServiceClass { Protected = 0, Survival = 1, Continuity = 2, Recovery = 3, Development = 4 }
        private const double Epsilon = 1e-9;
        // Scalar headless root-cause ablations only; ordinary planning leaves these false.
        [ThreadStatic] internal static bool ConsumptionFirstDiagnostic;
        [ThreadStatic] internal static bool ExcludeGrowthFromSurvivalDiagnostic;
        [ThreadStatic] internal static bool NoFieldDevelopmentDiagnostic;
        // Diagnostic overrides of the configured Elder split (headless probes). They can only switch the split on; the
        // authoritative setting is SimulationConfig.ElderKernelSplit / ElderSplitObjective / ElderSplitLexTolerance /
        // ElderMixedRoutes. Process-wide so parallel preparation sees them. Objectives: 0 person-years, 1 QoL-weighted
        // person-years, 2 lexicographic (person-years within the tolerance of the best, then QoL; the default), 3 terminal-
        // population welfare, 4 deaths averted.
        internal static volatile bool KernelSplitDiagnostic;
        internal static volatile int KernelSplitObjective = 2;
        internal static double KernelLexTolerance = .05;
        // Hybrid counterfactual: score and commit only the shelter split; clothing keeps the ordinary requests.
        internal static volatile bool KernelShelterOnlyDiagnostic;
        internal static long KernelSplitCalls, KernelSplitTicks, KernelSplitCamps;
        internal static long KernelServiceIterations;
        // Candidates are scored with PhysicalDemography.PlanningBudget (the fast solver). Exact scoring uses the
        // authoritative AdvanceBudget instead; verify scores both and counts decisions whose winner differs.
        internal static volatile bool KernelExactScoringDiagnostic, KernelVerifyDiagnostic;
        internal static readonly object KernelVerifyLock = new object();
        internal static long KernelVerifyDecisions, KernelVerifyMismatches;
        internal static double KernelVerifyMaxPersonYearsError, KernelVerifyMaxQoLError;
        internal static string KernelVerifyWorst = "";
        internal static readonly double[] KernelClothingGrid = { 0, .25, .5, .75, 1 };
        internal static readonly double[] KernelShelterGrid = { 0, .5, 1 };
        internal static readonly long[] KernelChosenClothing = new long[5], KernelChosenShelter = new long[3];
        // Mixed-use routes: a farming and herding camp also scores the other clothing route (Cloth / Animal Fabric) and
        // the other shelter route (fixed Housing / Light Housing). Herd-versus-field investment is the settlement
        // portfolio supervisor's decision; the earlier kernel posture search lost population and is retired, so
        // KernelPostureMode is kept only for source compatibility and must stay 0.
        internal static volatile bool KernelMixedDiagnostic;
        internal static volatile int KernelPostureMode;
        internal static long KernelMixedCamps, KernelMixedOtherClothing, KernelMixedOtherShelter;
        internal static readonly long[] KernelChosenPosture = new long[3];
        internal static void ResetKernelSplitCounters()
        {
            KernelMixedCamps = KernelMixedOtherClothing = KernelMixedOtherShelter = 0;
            Array.Clear(KernelChosenPosture, 0, KernelChosenPosture.Length);
            KernelSplitCalls = KernelSplitTicks = KernelSplitCamps = KernelServiceIterations = 0;
            KernelVerifyDecisions = KernelVerifyMismatches = 0; KernelVerifyMaxPersonYearsError = KernelVerifyMaxQoLError = 0; KernelVerifyWorst = "";
            Array.Clear(KernelChosenClothing, 0, KernelChosenClothing.Length);
            Array.Clear(KernelChosenShelter, 0, KernelChosenShelter.Length);
        }
        internal static bool KernelSplitOn(SimulationConfig cfg) => KernelSplitDiagnostic || cfg.ElderKernelSplit;
        private static int SplitObjective(SimulationConfig cfg) => KernelSplitDiagnostic ? KernelSplitObjective : cfg.ElderSplitObjective;
        private static double SplitTolerance(SimulationConfig cfg) => KernelSplitDiagnostic ? KernelLexTolerance : cfg.ElderSplitLexTolerance;
        private static bool MixedRoutesOn(SimulationConfig cfg) => KernelMixedDiagnostic || cfg.ElderMixedRoutes;

        // Retained for source compatibility of diagnostics; only HerdFirst (the class order) is ever committed.
        internal enum LabourPosture { HerdFirst = 0, FieldFirst = 1, NoInvestment = 2 }

        // The Elder's committed labour/food split for one camp-turn: the action a Village Elder policy would emit (routes
        // and grid levels are discrete; units/people are the levels' physical amounts), plus the kernel's score of it.
        // Transient diagnostics, never saved.
        internal sealed class ElderAction
        {
            public CraftGood ClothingRoute, ShelterRoute;
            public int ClothingLevel, ShelterLevel;
            public double ClothingUnits, ShelterPeople;
            public LabourPosture Posture;
            public double PersonYears, QoL;
        }

        // Complete per-unit cost of one clothing or shelter route (shelter per person sheltered).
        private sealed class RouteCost { public CraftGood Good; public double Persons, Food, Plant, Animal, Stone, WoodOrClay; }

        // One scored (clothing, shelter) split.
        private sealed class KernelCandidate
        {
            public CraftGood ClothingRoute, ShelterRoute;
            public int ClothingLevel, ShelterLevel;
            public double ClothingUnits, ShelterPeople, Persons, ProtectedFood, Food;
            public DemographicResult Result;
            public double PersonYears, QoL, Value;
        }

        // Per-unit requirements of a request's complete bundle.
        internal sealed class Bundle
        {
            public readonly Dictionary<string, double> RoleYears = new Dictionary<string, double>();   // craft worker-years per unit, by role
            public readonly Dictionary<CraftGood, double> Outputs = new Dictionary<CraftGood, double>(); // goods made per unit, intermediates included
            public double ToolOutputs, Stone, Clay, Wood, Construction, Plant,
                Animal, Fabric, Material, FoodService;
            public void Add(Bundle b, double scale)
            {
                foreach (var p in b.RoleYears) RoleYears[p.Key] = (RoleYears.TryGetValue(p.Key, out double v) ? v : 0) + p.Value * scale;
                foreach (var p in b.Outputs) Outputs[p.Key] = (Outputs.TryGetValue(p.Key, out double v) ? v : 0) + p.Value * scale;
                ToolOutputs += b.ToolOutputs * scale; Stone += b.Stone * scale; Clay += b.Clay * scale; Wood += b.Wood * scale; Construction += b.Construction * scale;
                Plant += b.Plant * scale; Animal += b.Animal * scale;
                Fabric += b.Fabric * scale; Material += b.Material * scale;
                FoodService += b.FoodService * scale;
            }
        }
        // One service request and what the Elder granted (transient diagnostics, never saved).
        internal sealed class Request
        {
            // A goods request names its Good; a labour request (herd growth, field expansion) its Role, in persons.
            public string Service; public ServiceClass Class; public CraftGood? Good; public string Role; public double Units, Granted; public string Unmet = "";
            // Development requests are ordered by physical retained food per
            // recurring worker-year. WholeUnits marks livestock heads; a
            // fractional grant is not an economic authorization to breed.
            internal Bundle Bundle; internal bool Frozen, WholeUnits,
                DevelopmentPlant, DevelopmentAnimal, HerdDevelopment;
            internal double PriorityScore, DevelopmentFood,
                DevelopmentFoodPerUnit, TypedDevelopmentNeed,
                SubstitutionFood, PolicyDevelopmentUnits;
            // Opening intermediates this request's bundle drew from stock, returned if the request is withdrawn.
            internal double ReservedFabric, ReservedMaterial;
            public double Fraction => Units > Epsilon ? Granted / Units : 1;
        }
        internal sealed class Allocation
        {
            public readonly List<Request> Requests = new List<Request>();
            public readonly Dictionary<string, int> Staff = new Dictionary<string, int>();
            public int Herders, Farmers, Fishers, FoodGatherers;
            public double FishingFood, FeedstockGatherers,
                MaterialGatherers, ToolsMade;
            public double HCurrent, HSustainable, HAbsolute, HGrown, HRetained;
            public double RetainedFoodSupply, DevelopmentFoodGap;
            public double RetainedFarmerPlantService,
                RetainedFarmerFoodService;
            public double SelectedFieldOperation, SelectedPortfolioValue,
                SelectedPortfolioUnmet;
            public int PortfolioAlternatives, StrategicLaborStep,
                DevelopmentAlternatives;
            public double FieldMarginalReturn, HerdMarginalReturn,
                FarmerRayReturn, HerderRayReturn, BalancedMarginalReturn,
                DefaultLaborReturn, SelectedDevelopmentUseful,
                SelectedDevelopmentWork, SelectedFieldBudget,
                SelectedHerdBudget, FinalDevelopmentUseful,
                FinalDevelopmentWork, FinalFieldBudget, FinalHerdBudget,
                FieldOpportunityCrossingBudget;
            public string SelectedDevelopmentRay = "none";
            public readonly List<InvestmentTrace> InvestmentTraces =
                new List<InvestmentTrace>();
            public readonly Dictionary<CraftGood, double> Ceilings = new Dictionary<CraftGood, double>();
            public string FoodUnmet = ""; public int RepairPasses, TrimPasses;
            // Prepared-boundary diagnostics (transient).
            public double ResidentGoodsBudget, FinalCompiledPersons;
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
            // Contract 7 teacher data: native request and grant totals per goods service, route feasibility and choices.
            public readonly double[] ServiceRequested = new double[ElderModel9Contract.GoodsServices.Length];
            public readonly double[] ServiceGranted = new double[ElderModel9Contract.GoodsServices.Length];
            public readonly bool[] ClothingRouteFeasible = new bool[2], ShelterRouteFeasible = new bool[2];
            public CraftGood NativeClothingRoute = CraftGood.Cloth, NativeShelterRoute = CraftGood.Housing;
            public CraftGood ChosenClothingRoute = CraftGood.Cloth, ChosenShelterRoute = CraftGood.Housing;
            public double HerdDevelopmentAnimals;
            // The committed Elder split and (CaptureDemandAllocations) every scored split.
            public ElderAction ElderAction;
            public List<ElderAction> KernelCandidates;
        }

        // Diagnostic account of one physically forecast investment band.  This
        // is transient and deliberately records the pre-admission alternative
        // as well as the selected and post-compilation grants.  It is never an
        // authoritative input and is not serialized.
        internal sealed class InvestmentTrace
        {
            public string Channel, Rejection = "";
            public int Band;
            public double Units, CurrentWorkerYears, RecurringWorkerYears,
                ForecastFood, UsefulServiceCap, FiniteAlternativeFood,
                FiniteAlternativeWorkerYears, Return, AdmissionHurdle,
                SelectedUnits, GrantedUnits;
            public bool PhysicallyFeasible, DefaultAdmitted, Admitted;
        }

        // Headless-only causal seam. Runtime callers always pass null and retain
        // the production admission policy. The option can expose otherwise
        // physical field bands to the same Elder, compiler and resolver without
        // changing their scores, requests, or resource constraints.
        internal sealed class DevelopmentDiagnostics
        {
            public bool ExposePhysicalFieldBands;
            public bool UseFiniteFieldOpportunity;
            public bool UseLegacyUnboundedFieldHurdle;
            public bool CaptureInvestmentTrace;
        }

        // Diagnostic-only strategy seam. Production calls Plan without a
        // selector and therefore keeps the bounded coarse search. Headless
        // audits may enumerate a slow reference frontier through Evaluate;
        // every returned choice still uses this planner's exact request,
        // feasibility, useful-service and downstream compilation paths.
        internal sealed class DevelopmentChoice
        {
            internal readonly Dictionary<Request, double> Grants;
            public readonly double FieldBudget, HerdBudget, Useful,
                PricedWork;
            public readonly bool Feasible;

            internal DevelopmentChoice(Dictionary<Request, double> grants,
                double fieldBudget, double herdBudget, double useful,
                double pricedWork, bool feasible)
            {
                Grants = grants ?? new Dictionary<Request, double>();
                FieldBudget = fieldBudget; HerdBudget = herdBudget;
                Useful = useful; PricedWork = pricedWork;
                Feasible = feasible;
            }

            public double Return => PricedWork > Epsilon
                ? Useful / PricedWork : Useful > Epsilon
                    ? double.PositiveInfinity : 0;
        }

        internal sealed class DevelopmentSearchContext
        {
            public readonly int Residents, StrategicLaborStep;
            public readonly IReadOnlyList<Request> FieldRequests,
                HerdRequests;
            private readonly Func<double, double, DevelopmentChoice> evaluate;

            internal DevelopmentSearchContext(int residents,
                int strategicLaborStep, IReadOnlyList<Request> fieldRequests,
                IReadOnlyList<Request> herdRequests,
                Func<double, double, DevelopmentChoice> evaluate)
            {
                Residents = residents;
                StrategicLaborStep = strategicLaborStep;
                FieldRequests = fieldRequests; HerdRequests = herdRequests;
                this.evaluate = evaluate;
            }

            public DevelopmentChoice Evaluate(double fieldPeople,
                double herdPeople) => evaluate(fieldPeople, herdPeople);
        }

        internal delegate DevelopmentChoice DevelopmentSearch(
            DevelopmentSearchContext context);

        internal static EconomicIntent Plan(WorldState w, Camp c, EconomicIntent settings, out ProductionOrder production, out Allocation allocation)
            => Plan(w, c, settings, null, out production, out allocation);

        internal static EconomicIntent Plan(WorldState w, Camp c, EconomicIntent settings, FishingAccessPlan fishingAccess,
            out ProductionOrder production, out Allocation allocation,
            DevelopmentSearch developmentSearch = null,
            DevelopmentDiagnostics diagnostics = null,
            ElderDirective elderDirective = null,
            List<ElderIntervention> elderInterventions = null)
        {
            production = null; var alloc = new Allocation(); allocation = alloc;
            bool captureInvestmentTrace = diagnostics?
                .CaptureInvestmentTrace == true;
            var e = settings.Copy(); e.Assignments.Clear(); e.Labor = null;
            if (c.Population <= 0 || !CultivationEconomy.EconomyEnabled(w, c)) { e.AutomationTurns = 0; return e; }
            var cfg = w.Config; var n = cfg.Neolithic;
            double years = cfg.TurnDays / cfg.DaysPerYear, intervals = cfg.GenerationIntervals, perYear = cfg.DaysPerYear / cfg.BaseDays;
            int residents = c.Population;
            // Start without an automatic current-herd reservation. The
            // settlement supervisor selects a retained physical portfolio from
            // fields, local support and bounded labour; only then is the herd
            // forecast applied to the shared food ledger.
            var d = CampProductionDemand.BuildPhysical(w, c, e, false, true,
                0, fishingAccess);
            var herdProfile = HerdConsumptionProfile.Owned(w, c);
            var site = SettlementSiteEvaluation.Evaluate(w, c, c.Tile,
                residents, herdProfile);
            var portfolio = SettlementPortfolioSupervisor.Select(w, c, d, e,
                site, elderDirective, elderInterventions);
            d.ApplyManagedHerd(w, c, portfolio.HerdHerders);
            alloc.HCurrent = portfolio.HCurrent;
            alloc.HSustainable = portfolio.HSustainable;
            alloc.HAbsolute = portfolio.HAbsolute;
            alloc.HGrown = portfolio.HGrown;
            alloc.HRetained = portfolio.HRetained;
            alloc.SelectedFieldOperation = portfolio.SelectedFieldOperation;
            alloc.SelectedPortfolioValue = portfolio.SelectedPortfolioValue;
            alloc.SelectedPortfolioUnmet = portfolio.SelectedPortfolioUnmet;
            alloc.PortfolioAlternatives = portfolio.AlternativesEvaluated;
            alloc.StrategicLaborStep = portfolio.StrategicLaborStep;
            var recipes = d.Recipes; var byGood = recipes.ToDictionary(r => r.Good);
            double gatherRate = cfg.GatherPerPerson * intervals;  // food one gatherer brings in over the turn
            var continuation = SettlementContinuationDemand.For(w, c, d);
            double survivalFood = continuation.ConsumptionIncome;
            double reserveFood = continuation.ReserveFill;
            double growthFood = continuation.GrowthFood;
            // ResolveNeeds protects typed manufacturing stock that fits after
            // meals. Credit that same inventory exactly once when pricing and
            // staffing plant-feedstock routes; only the remaining deficit has
            // to be gathered from the local plant envelope.
            double availablePlantInventory = Math.Min(Math.Max(0, c.PlantFood),
                Math.Max(0, c.Food - d.Meals));

            // ---- Retained portfolio and current food work (no expansion) ----
            int farmers = Math.Min(residents, portfolio.FieldFarmers);
            int herders = Math.Min(Math.Max(0, residents - farmers),
                portfolio.HerdHerders);
            double animalReceipts = d.ManagedFood.AnimalFood +
                portfolio.SlaughterFood;
            double animalEnvelope = Math.Max(d.AnimalEnvelope,
                d.AnimalEnvelopeClothing) + portfolio.SlaughterFood;
            double foodNeed = Math.Max(0, survivalFood - animalReceipts);
            double terrestrial = Math.Min(d.TerrestrialAfterHerds, foodNeed);
            double farmRate = d.HarvestProductivity * cfg.GatherPerPerson * intervals;
            // Operating farmers keep the fields up (and harvest while they do); the rest of the food is gathered. Gathering on
            // the footprint also builds field capital through the resolver's gatherer bridge (up to GathererBridgeCeiling),
            // which harvesting farmers do not, so food beyond the upkeep crew is staffed as gathering (slice 2).
            double harvest = Math.Min(terrestrial, farmers * farmRate);
            int foodLabor = Math.Max(0, residents - herders - farmers);
            double selectedPassive = Math.Min(d.SelectedFishingPassiveSupply, d.AquaticAfterHerds);
            double fishingTools = Math.Max(0, c.Inventory.FishingTools);
            double Catch(int fishers) => d.FishingEnabled && d.FishingAccess != null && fishers > 0
                ? d.FishingAccess.CatchEnvelope(fishers, fishingTools) : 0;
            double marginalAt(int fishers) => d.FishingEnabled && d.FishingAccess != null && fishers > 0
                ? d.FishingAccess.MarginalCatchPerFisher(fishers, fishingTools) : 0;
            (int Fishers, int Gatherers, int WantedGatherers, double Catch, double Target, double Food) bestFood = default;
            bool hasBestFood = false;
            void ConsiderFoodStaffing(int fishers)
            {
                fishers = Math.Max(0, Math.Min(foodLabor, fishers));
                double catchFood = Catch(fishers);
                // Selected coastal tiles are already in the passive-gathering envelope. Fishing only adds food beyond
                // that overlapping stock; the same fish cannot satisfy both channels in the physical-demand forecast.
                double target = Math.Min(foodNeed, d.ForageAfterHerds + Math.Max(0, catchFood - selectedPassive));
                int wanted = gatherRate > 0
                    ? (int)Math.Ceiling(Math.Max(0, target - harvest - catchFood) / gatherRate - Epsilon) : 0;
                int gatherers = Math.Min(Math.Max(0, foodLabor - fishers), wanted);
                double gatherable = Math.Max(0, d.ForageAfterHerds - Math.Min(catchFood, selectedPassive) - harvest);
                double food = Math.Min(target, harvest + catchFood + Math.Min(gatherers * gatherRate, gatherable));
                if (!hasBestFood || food > bestFood.Food + Epsilon ||
                    Math.Abs(food - bestFood.Food) <= Epsilon && (fishers + gatherers < bestFood.Fishers + bestFood.Gatherers ||
                        fishers + gatherers == bestFood.Fishers + bestFood.Gatherers && fishers < bestFood.Fishers))
                {
                    bestFood = (fishers, gatherers, wanted, catchFood, target, food);
                    hasBestFood = true;
                }
            }
            ConsiderFoodStaffing(0);
            if (d.FishingEnabled && d.FishingAccess != null && d.FishingAccess.Grounds.Length > 0 && foodLabor > 0)
            {
                // The survival frontier has diminishing catch. Its optimum against the flat gatherer rate is at the
                // Fisher count where marginal catch crosses one gatherer's turn yield, at the food-cap crossing, or an
                // endpoint. Search those bounded breakpoints instead of scanning the resident population.
                var counts = new SortedSet<int> { 0, foodLabor };
                int lo = 0, hi = foodLabor;
                while (lo < hi)
                {
                    int mid = lo + (int)(((long)hi - lo + 1) / 2);
                    if (marginalAt(mid) >= gatherRate - Epsilon) lo = mid; else hi = mid - 1;
                }
                void Around(int value)
                {
                    for (int delta = -2; delta <= 2; delta++)
                    {
                        long count = (long)value + delta;
                        if (count >= 0 && count <= foodLabor) counts.Add((int)count);
                    }
                }
                Around(lo); Around(Math.Min(foodLabor, lo + 1));
                void AroundCatch(double targetCatch)
                {
                    if (!(targetCatch > 0) || Catch(foodLabor) < targetCatch - Epsilon) return;
                    lo = 0; hi = foodLabor;
                    for (int i = 0; i < 32 && lo < hi; i++)
                    {
                        int mid = lo + (hi - lo) / 2;
                        if (Catch(mid) >= targetCatch - Epsilon) hi = mid; else lo = mid + 1;
                    }
                    Around(lo);
                }
                // Catch changes both the mixed foraging envelope and the direct
                // meal receipts. Search both discontinuities: filling the
                // envelope and meeting the meal target after farm harvest.
                AroundCatch(selectedPassive + Math.Max(0, foodNeed - d.ForageAfterHerds));
                AroundCatch(Math.Max(0, foodNeed - harvest));
                foreach (int count in counts) if (count > 0) ConsiderFoodStaffing(count);
            }
            int fishers = bestFood.Fishers, foodGatherers = bestFood.Gatherers;
            double fishingFood = bestFood.Catch;
            double gatheredFood = Math.Min(foodGatherers * gatherRate,
                Math.Max(0, d.ForageAfterHerds -
                    Math.Min(fishingFood, selectedPassive) - harvest));
            if (foodGatherers < bestFood.WantedGatherers ||
                bestFood.Food + Epsilon < bestFood.Target)
                alloc.FoodUnmet = "labor rationing";
            alloc.Herders = herders; alloc.Farmers = farmers; alloc.Fishers = fishers;
            alloc.FoodGatherers = foodGatherers; alloc.FishingFood = fishingFood;
            d.Supply = d.ForageAfterHerds +
                Math.Max(0, fishingFood - selectedPassive) +
                d.ManagedFood.AnimalFood;
            double budget = Math.Max(0, residents - herders - farmers - fishers - foodGatherers);   // persons left for goods
            alloc.ResidentGoodsBudget = budget;
            alloc.ExpectedSupply = d.Supply; alloc.Meals = d.Meals; alloc.OperatingReserve = d.OperatingReserve;
            alloc.ExpectedForageAfterHerds = d.ForageAfterHerds; alloc.ExpectedManagedAnimalFood = d.ManagedFood.AnimalFood;
            alloc.ConsumptionIncomeTarget = survivalFood; alloc.ReserveFillTarget = reserveFood; alloc.MaximumGrowthFood = growthFood;
            alloc.HusbandryEnabled = d.HusbandryEnabled;
            alloc.PlantEnvelope = d.PlantEnvelope; alloc.AnimalEnvelope = d.AnimalEnvelope; alloc.FoodRoomEnvelope = d.FoodRoomEnvelope;
            alloc.PlantEnvelopeClothing = d.PlantEnvelopeClothing; alloc.AnimalEnvelopeClothing = d.AnimalEnvelopeClothing;
            alloc.FoodRoomEnvelopeClothing = d.FoodRoomEnvelopeClothing;
            alloc.AccessibleStone = d.Accessible.TryGetValue(MaterialId.Stone, out double accessibleStone) ? accessibleStone : 0;
            alloc.AccessibleClay = d.Accessible.TryGetValue(MaterialId.Clay, out double accessibleClay) ? accessibleClay : 0;
            alloc.AccessibleWood = d.Accessible.TryGetValue(MaterialId.Wood, out double accessibleWood) ? accessibleWood : 0;
            // Kernel-scored Elder split state: set once a split is committed.
            bool protectedSplit = false, kernelEnacted = false;
            // The budget raise covers only the persons the Protected split is actually granted (set after its arbitration).
            double kernelBudgetBase = 0, kernelCommittedPersons = 0; int kernelWorkers = 0; bool kernelRoutesSet = false;
            double protectedClothingUnits = -1;

            // The resolver-shaped candidate the tool demands read (herders and farmers staffed).
            var staffed = e.Copy();
            if (herders > 0) staffed.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Herder, Population = herders });
            if (farmers > 0) staffed.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Farmer, Population = farmers });
            if (fishers > 0) staffed.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Fisher, Population = fishers });
            var candidate = EconomicPlanner.Preview(c, staffed, residents);
            NeolithicProduction.DepreciateHousing(w, candidate, years);

            // ---- Bundles ----
            string Role(CraftRecipe r) => c.People.HasTech(r.Track) ? r.Specialist : WorkforceRoles.Craftsperson;
            bool Usable(CraftRecipe r)
                => r != null && NeolithicProduction.RecipeAvailable(w, c, r)
                   && !(r.SpecialistOnly && !c.People.HasTech(r.Track))
                   && SpecialistProductivity.CraftRate(w, c, r, Role(r)) > 0;
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
            Bundle WithInput(Bundle b, double perUnit, CraftGood input, double units, ref double stock, bool fabric, out double reserved)
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
            Request Request(string service, ServiceClass cls, CraftGood good, double units)
            {
                if (!(units > Epsilon) || !byGood.ContainsKey(good)) return null;
                var r = byGood[good];
                var req = new Request { Service = service, Class = cls, Good = good, Units = units };
                if (!Usable(r) || NeolithicProduction.Priority(candidate, good) <= 0) { req.Unmet = "missing capability"; alloc.Requests.Add(req); return req; }
                var b = Craft(good);
                if (r.AnimalFabric > 0) b = WithInput(b, r.AnimalFabric, CraftGood.AnimalFabric, units, ref freeFabric, true, out req.ReservedFabric);
                if (b != null && r.HousingMaterial > 0) b = WithInput(b, r.HousingMaterial, CraftGood.HousingMaterial, units, ref freeMaterial, false, out req.ReservedMaterial);
                if (b == null)
                {
                    freeFabric += req.ReservedFabric; freeMaterial += req.ReservedMaterial;
                    req.ReservedFabric = req.ReservedMaterial = 0;
                    req.Unmet = "missing capability"; alloc.Requests.Add(req); return req;
                }
                req.Bundle = b; alloc.Requests.Add(req); return req;
            }
            // Clothing is one service with capability-gated routes. Compare
            // complete feasible bundles against the actual typed envelope after
            // meals; a newly learned recipe with no usable feedstock is inert.
            // Shelter strategy follows retained physical settlement capital.
            // A Farmer assignment can be a small or incidental bridge field;
            // it is not by itself authority to replace a mobile camp with a
            // fixed-housing policy.
            bool fixedCapital = site.FixedHousing > Epsilon || c.IsVillage ||
                d.RetainedFieldCapital;
            bool plantTextileCapital = farmers > 0 ||
                c.Inventory.Cloth > Epsilon;
            bool portableCapital = c.Inventory.LightHousingUnits > Epsilon ||
                c.Inventory.AnimalFabric > Epsilon || portfolio.HCurrent > 0;
            (bool feasible, double units, double labor) ClothingRoutePlan(
                CraftGood good)
            {
                if (!byGood.TryGetValue(good, out var recipe) || !Usable(recipe) ||
                    NeolithicProduction.Priority(candidate, good) <= 0)
                    return (false, 0, double.PositiveInfinity);
                double units = Math.Max(0, NeolithicProduction.Demand(w,
                    candidate, good, years, residents, false, false, d.Sources));
                if (!(units > Epsilon)) return (true, 0, 0);
                var bundle = Craft(good);
                if (bundle == null) return (false, units, double.PositiveInfinity);
                double plant = units * bundle.Plant;
                double animal = units * bundle.Animal;
                double foodRoom = Math.Max(0, c.Food + d.Supply +
                    portfolio.SlaughterFood - d.Meals);
                double plantEnvelope = Math.Max(d.PlantEnvelope,
                    d.PlantEnvelopeClothing);
                double plantFromLand = Math.Max(0, plant -
                    availablePlantInventory);
                bool feasible = plantFromLand <= Math.Max(0, plantEnvelope -
                        availablePlantInventory) + Epsilon &&
                    animal <= animalEnvelope + Epsilon &&
                    plant + animal <= foodRoom + Epsilon;
                double labor = units * bundle.RoleYears.Values.Sum() / years;
                labor += gatherRate > 0 ? plantFromLand / gatherRate :
                    plantFromLand > Epsilon ? double.PositiveInfinity : 0;
                return (feasible, units, labor);
            }
            var clothPlan = ClothingRoutePlan(CraftGood.Cloth);
            var animalPlan = ClothingRoutePlan(CraftGood.AnimalFabric);
            bool clothPossible = clothPlan.feasible;
            bool animalFabricPossible = animalPlan.feasible;
            CraftGood clothingRoute;
            // Preserve an operating material chain when it remains feasible.
            // This preference follows retained field/fixed or portable capital,
            // not the technology that happened to expose the other recipe.
            if (plantTextileCapital && clothPossible)
                clothingRoute = CraftGood.Cloth;
            else if (portableCapital && animalFabricPossible)
                clothingRoute = CraftGood.AnimalFabric;
            else if (clothPossible && animalFabricPossible)
            {
                if (animalPlan.labor < clothPlan.labor - Epsilon)
                    clothingRoute = CraftGood.AnimalFabric;
                else if (clothPlan.labor < animalPlan.labor - Epsilon)
                    clothingRoute = CraftGood.Cloth;
                else clothingRoute = c.Inventory.AnimalFabric > c.Inventory.Cloth
                    ? CraftGood.AnimalFabric : CraftGood.Cloth;
            }
            else clothingRoute = clothPossible ? CraftGood.Cloth :
                animalFabricPossible ? CraftGood.AnimalFabric :
                Usable(byGood[CraftGood.Cloth]) ? CraftGood.Cloth :
                CraftGood.AnimalFabric;
            alloc.ClothingRouteFeasible[0] = clothPossible; alloc.ClothingRouteFeasible[1] = animalFabricPossible;
            alloc.NativeClothingRoute = clothingRoute;
            // Contract 7: a learned clothing route replaces the native choice when that route is feasible this turn.
            bool forcedClothingRoute = false;
            if (elderDirective != null && elderDirective.ClothingRoute >= 0)
            {
                var wanted = elderDirective.ClothingRoute == 0 ? CraftGood.Cloth : CraftGood.AnimalFabric;
                bool feasible = wanted == CraftGood.Cloth ? clothPossible : animalFabricPossible;
                if (feasible) { clothingRoute = wanted; forcedClothingRoute = true; }
                else elderInterventions?.Add(new ElderIntervention { Kind = ElderInterventionKind.PhysicalConstraint, Control = "clothing-route",
                    Reason = "requested clothing route cannot be fed this turn; native route kept", Requested = elderDirective.ClothingRoute,
                    Accepted = clothingRoute == CraftGood.Cloth ? 0 : 1 });
            }
            double clothingUseful = clothingRoute == CraftGood.Cloth
                ? clothPlan.units : animalPlan.units;
            if (!(clothingUseful > Epsilon))
                clothingUseful = Math.Max(0, NeolithicProduction.Demand(w,
                    candidate, clothingRoute, years, residents, false, false,
                    d.Sources));
            double clothingNow = Math.Min(clothingUseful, Math.Max(0, d.ClothingNeedUnits));
            if (n.SubsistenceNeedsCovered)
            {
                clothingUseful = Math.Min(clothingUseful, NeolithicProduction.Demand(w, candidate,
                    clothingRoute, years, residents, false, false, d.Sources));
                double need = NeolithicProduction.ClothingNeedUnits(n, SubsistenceNeeds.GoodsResidents(w, candidate, residents), years,
                    candidate.Inventory.Cloth - candidate.TradeExport(CraftGood.Cloth), candidate.Inventory.AnimalFabric - candidate.TradeExport(CraftGood.AnimalFabric));
                clothingNow = Math.Min(clothingUseful, need);
            }
            Request("clothing", ServiceClass.Continuity, clothingRoute, clothingNow);
            Request("clothing reserve", ServiceClass.Recovery, clothingRoute, Math.Max(0, clothingUseful - clothingNow));
            double plantFeedstock = clothingRoute == CraftGood.Cloth
                ? clothingUseful * n.PlantFoodPerFabricUnit : 0;

            // Operating farmers are already paid for retained field service.
            // Resolution uses their complete harvest capacity for typed plant
            // input first and then biological food.  Account for that capacity
            // once here: survival remains the staffing priority above, while
            // the unused capacity can supply the higher service buckets without
            // summoning a second set of Gatherers for the same physical work.
            double farmerSpareCapacity = Math.Min(
                Math.Max(0, d.TerrestrialAfterHerds - harvest),
                Math.Max(0, farmers * farmRate - harvest));
            double plannedPlantFromLand = Math.Max(0,
                plantFeedstock - availablePlantInventory);
            double farmerPlantService = Math.Min(farmerSpareCapacity,
                plannedPlantFromLand);
            double farmerBiologicalCapacity = Math.Max(0,
                farmerSpareCapacity - farmerPlantService);
            double farmerFoodService = Math.Min(farmerBiologicalCapacity,
                Math.Max(0, survivalFood + reserveFood + growthFood -
                    animalReceipts - harvest - fishingFood - gatheredFood));
            alloc.RetainedFarmerPlantService = farmerPlantService;
            alloc.RetainedFarmerFoodService = farmerFoodService;
            double retainedFoodSupply = animalReceipts + harvest + fishingFood +
                gatheredFood + farmerFoodService;
            alloc.RetainedFoodSupply = retainedFoodSupply;
            double retainedSurplus = Math.Max(0,
                retainedFoodSupply - survivalFood);
            double survivalFoodGap = Math.Max(0,
                survivalFood - retainedFoodSupply);
            double reserveFoodGap = Math.Max(0, reserveFood - retainedSurplus);
            retainedSurplus = Math.Max(0, retainedSurplus - reserveFood);
            double growthFoodGap = Math.Max(0, growthFood - retainedSurplus);
            double remainingForage = Math.Max(0,
                d.ForageAfterHerds - Math.Min(fishingFood, selectedPassive) -
                harvest - gatheredFood - farmerFoodService -
                plannedPlantFromLand);
            double reserveGatherFood = Math.Min(reserveFoodGap,
                remainingForage);
            double growthGatherFood = Math.Min(growthFoodGap,
                Math.Max(0, remainingForage - reserveGatherFood));
            // These are current-turn receipt gaps. Future field stock and
            // newborn-herd output are tracked separately below and cannot erase
            // them before the resolver actually receives food.
            double currentDevelopmentFoodGap = survivalFoodGap + reserveFoodGap +
                growthFoodGap;
            alloc.DevelopmentFoodGap = currentDevelopmentFoodGap;
            // Fabric this turn's clothing consumption will use (beyond the Cloth held) is not free for tents.
            double consumption = residents * n.FabricConsumptionPerPersonTurn * years / n.ReferenceTurnYears;
            freeFabric = Math.Max(0, freeFabric - Math.Max(0, consumption - Math.Max(0, c.Inventory.Cloth - c.TradeExport(CraftGood.Cloth))));
            // Shelter is one service. Retained fixed and portable capital each
            // request their own replacement; the remaining deficit follows the
            // settlement's retained physical policy, with one bounded feasible
            // fallback. Husbandry only exposes the portable recipe.
            double endDays = w.Days + years * cfg.DaysPerYear;
            double gapPeople = NeolithicProduction.HousingGapPeople(w, candidate, residents, endDays);
            double totalGapPeople = gapPeople;
            bool fixedPossible = Usable(byGood[CraftGood.Housing]) && NeolithicProduction.Priority(candidate, CraftGood.Housing) > 0;
            bool tentsPossible = CultivationEconomy.HusbandryEnabled(c) && Usable(byGood[CraftGood.LightHousing]) && NeolithicProduction.Priority(candidate, CraftGood.LightHousing) > 0;
            double PerUnit(CraftGood g) => g == CraftGood.Housing ? n.HousingPeoplePerUnit : n.LightHousingPeoplePerUnit;
            bool RouteFeasible(CraftGood good, double people)
            {
                if (!(people > Epsilon)) return true;
                if (good == CraftGood.Housing)
                {
                    if (!fixedPossible) return false;
                    double units = people / n.HousingPeoplePerUnit;
                    double missing = Math.Max(0, units * byGood[CraftGood.Housing].HousingMaterial - freeMaterial);
                    if (!(missing > Epsilon)) return true;
                    if (!Usable(byGood[CraftGood.HousingMaterial])) return false;
                    double construction = missing * byGood[CraftGood.HousingMaterial].ConstructionMaterial;
                    return construction <= Inventory(MaterialId.Wood) + Accessible(MaterialId.Wood) +
                        Inventory(MaterialId.Clay) + Accessible(MaterialId.Clay) + Epsilon;
                }
                if (!tentsPossible) return false;
                double lightUnits = people / n.LightHousingPeoplePerUnit;
                double missingFabric = Math.Max(0,
                    lightUnits * byGood[CraftGood.LightHousing].AnimalFabric - freeFabric);
                if (!(missingFabric > Epsilon)) return true;
                return Usable(byGood[CraftGood.AnimalFabric]) &&
                    missingFabric *
                    byGood[CraftGood.AnimalFabric].AnimalFood <=
                    animalEnvelope + Epsilon;
            }
            CraftGood ShelterRoute(CraftGood preferred, double people)
            {
                CraftGood fallback = preferred == CraftGood.Housing
                    ? CraftGood.LightHousing : CraftGood.Housing;
                if (RouteFeasible(preferred, people)) return preferred;
                return RouteFeasible(fallback, people) ? fallback : preferred;
            }
            Request ShelterRequest(string service, ServiceClass cls,
                CraftGood preferred, double people)
            {
                if (!(people > Epsilon)) return null;
                var route = ShelterRoute(preferred, people);
                return Request(service, cls, route, people / PerUnit(route));
            }
            double fixedNow = NeolithicProduction.FixedHousingAt(w, c, w.Days);
            double fixedEnd = NeolithicProduction.FixedHousingAt(w, c, endDays);
            double fixedReplacementPeople = Math.Min(gapPeople,
                Math.Max(0, fixedNow - fixedEnd) * n.HousingPeoplePerUnit);
            ShelterRequest("shelter replacement", ServiceClass.Continuity,
                CraftGood.Housing, fixedReplacementPeople);
            gapPeople -= fixedReplacementPeople;
            double lightNow = Math.Max(0, c.Inventory.LightHousingUnits);
            double lightEnd = Math.Max(0, candidate.Inventory.LightHousingUnits);
            double lightReplacementPeople = Math.Min(gapPeople,
                Math.Max(0, lightNow - lightEnd) * n.LightHousingPeoplePerUnit);
            ShelterRequest("portable shelter replacement", ServiceClass.Continuity,
                CraftGood.LightHousing, lightReplacementPeople);
            gapPeople -= lightReplacementPeople;
            // Demonstrated tenure describes the current physical operating
            // policy; no capability, first breakthrough or lifetime movement
            // flag does. A recently relocated camp has zero current-site
            // tenure in its planning view and therefore keeps its portable
            // route. Once the same site has been retained for the existing
            // settlement-expectation horizon, fixed shelter becomes the
            // preferred deficit route even if this society moved in the past.
            bool demonstratedFixedTenure = c.StationaryYears + Epsilon >=
                n.RudimentaryExpectationRampYears;
            bool mobilePolicy = !fixedCapital && portableCapital &&
                !demonstratedFixedTenure;
            CraftGood deficitPreferred = mobilePolicy ? CraftGood.LightHousing : CraftGood.Housing;
            alloc.ShelterRouteFeasible[0] = RouteFeasible(CraftGood.Housing, Math.Max(1, gapPeople));
            alloc.ShelterRouteFeasible[1] = RouteFeasible(CraftGood.LightHousing, Math.Max(1, gapPeople));
            alloc.NativeShelterRoute = ShelterRoute(deficitPreferred, Math.Max(1, gapPeople));
            // Contract 7: a learned shelter route sets the deficit route when it is feasible this turn.
            bool forcedShelterRoute = false;
            if (elderDirective != null && elderDirective.ShelterRoute >= 0)
            {
                var wanted = elderDirective.ShelterRoute == 0 ? CraftGood.Housing : CraftGood.LightHousing;
                if (alloc.ShelterRouteFeasible[elderDirective.ShelterRoute == 0 ? 0 : 1]) { deficitPreferred = wanted; forcedShelterRoute = true; }
                else elderInterventions?.Add(new ElderIntervention { Kind = ElderInterventionKind.PhysicalConstraint, Control = "shelter-route",
                    Reason = "requested shelter route is infeasible this turn; native route kept", Requested = elderDirective.ShelterRoute,
                    Accepted = deficitPreferred == CraftGood.Housing ? 0 : 1 });
            }
            ShelterRequest("shelter deficit", ServiceClass.Recovery, deficitPreferred, gapPeople);
            double shelterDeficitPeople = gapPeople;
            // Field and herd equipment for the work staffed (continuity of retained assets).
            Request("farm tools", ServiceClass.Continuity, CraftGood.FarmTools, Math.Max(0, NeolithicProduction.Demand(w, candidate, CraftGood.FarmTools, years, residents, false, false, d.Sources)));
            Request("herding tools", ServiceClass.Continuity, CraftGood.HerdingTools, Math.Max(0, NeolithicProduction.Demand(w, candidate, CraftGood.HerdingTools, years, residents, false, false, d.Sources)));
            // Fishing Tools continuity is sized only after the shared survival frontier fixes Fisher staffing.
            Request("fishing tools", ServiceClass.Continuity, CraftGood.FishingTools,
                fishers > 0 ? Math.Max(0, NeolithicProduction.FishingToolDemand(w, candidate, years)) : 0);
            // Storage: the Pots the food reserve needs beyond natural storage.
            Request("storage", ServiceClass.Recovery, CraftGood.Pots, Math.Max(0, NeolithicProduction.PotDemand(w, c, years, residents)));
            Request LabourRequest(string service, ServiceClass cls, string role, double persons)
            {
                if (!(persons > Epsilon)) return null;
                var b = new Bundle(); b.RoleYears[role] = years;
                var request = new Request { Service = service, Class = cls,
                    Role = role, Units = persons, Bundle = b };
                alloc.Requests.Add(request); return request;
            }
            // Consumption labour is the frozen Survival work above. Reserve
            // refill is Recovery; food for maximum cohort growth is
            // Development and competes on the same physical-return scale as
            // herd and field increments. This prevents the biological maximum
            // from being silently promoted to mandatory field continuity.
            var reserveGathering = LabourRequest("food reserve recovery",
                ServiceClass.Recovery,
                WorkforceRoles.Gatherer, gatherRate > 0
                    ? reserveGatherFood / gatherRate : 0);
            if (reserveGathering != null)
                reserveGathering.Bundle.FoodService = reserveGatherFood /
                    Math.Max(Epsilon, reserveGathering.Units);
            var growthGathering = LabourRequest("food growth gathering",
                ServiceClass.Development, WorkforceRoles.Gatherer,
                gatherRate > 0 ? growthGatherFood / gatherRate : 0);
            if (growthGathering != null)
            {
                growthGathering.PriorityScore = gatherRate /
                    Math.Max(Epsilon, years);
                growthGathering.DevelopmentFood = growthGatherFood;
                growthGathering.DevelopmentFoodPerUnit = gatherRate;
                growthGathering.Bundle.FoodService = growthGatherFood /
                    Math.Max(Epsilon, growthGathering.Units);
            }
            // These commitments were selected by the settlement supervisor
            // before bundle arbitration and already removed from `budget`.
            // Record them as frozen continuity requests so the allocation
            // ledger does not misdescribe retained capital as an unclassified
            // survival mandate or charge the labour a second time.
            void RetainedLabour(string service, string role, int persons)
            {
                if (persons <= 0) return;
                alloc.Requests.Add(new Request
                {
                    Service = service, Class = ServiceClass.Continuity,
                    Role = role, Units = persons, Granted = persons,
                    Frozen = true
                });
            }
            RetainedLabour("retained field operation", WorkforceRoles.Farmer,
                farmers);
            RetainedLabour("retained herd management", WorkforceRoles.Herder,
                herders);
            // Herd continuity is only the supervisor-selected retained herd.
            // A larger end-of-turn target is Development even when an assigned
            // herder has spare integer capacity. Crop and herd increments use
            // the same useful-food ledger and are ordered below by their local
            // retained return, rather than privileging a capability identity.
            double herdTarget = portfolio.HCurrent > 0
                ? Math.Floor(portfolio.HRetained + Epsilon) : -1;
            var herdDevelopment = new List<Request>();
            var investmentByRequest = captureInvestmentTrace
                ? new Dictionary<Request, InvestmentTrace>() : null;
            double integerGrowthCeiling = Math.Floor(portfolio.HGrowthCeiling + Epsilon);
            double animalFeedstock = clothingRoute == CraftGood.AnimalFabric &&
                byGood.ContainsKey(CraftGood.AnimalFabric)
                ? clothingUseful * byGood[CraftGood.AnimalFabric].AnimalFood : 0;
            double animalFeedstockShortfall = Math.Max(0, animalFeedstock -
                animalEnvelope);
            double plantFeedstockShortfall = Math.Max(0, plantFeedstock -
                Math.Max(d.PlantEnvelope, d.PlantEnvelopeClothing));
            // An unresolved authorized food service after accounting for the
            // retained farmers' complete harvest capacity is a continuation
            // demand signal. It may authorize Development alternatives, but it
            // is not itself a current receipt and spare labor alone creates no
            // such signal.
            double strategicDevelopmentFoodGap = currentDevelopmentFoodGap;
            bool elderPhysicalDevelopment = elderDirective != null;
            double fieldGapPerYear = years > 0
                ? (strategicDevelopmentFoodGap + plantFeedstockShortfall) /
                    years : 0;
            double fieldWorkerYearCap = residents * years * Math.Max(1,
                CultivationEconomy.FarmToolFieldWorkEfficiency(n));
            var fieldLadder = CultivationEconomy.Enabled(c) &&
                d.Footprint.Length > 0 && (elderPhysicalDevelopment ||
                    fieldGapPerYear > Epsilon)
                ? CultivationEconomy.ExpansionLadder(w, c, years,
                    d.Footprint, elderPhysicalDevelopment
                        ? double.PositiveInfinity : fieldGapPerYear,
                    operatingFraction: portfolio.SelectedFieldOperation,
                    workerYearCap: fieldWorkerYearCap)
                : Array.Empty<CultivationEconomy.ExpansionStep>();
            // Value only the authoritative next-boundary retained-food change.
            // Multiplying that single-transition gain through the full planning
            // horizon invented repeated harvests on marginal land. Charge its
            // build work plus one bounded transition of recurring maintenance
            // and harvest work, preserving the resolver's maintenance/harvest
            // overlap.
            double IncrementalFieldRecurringWork(
                double annualGain, double maintenancePerYear,
                double harvestWorkerYears, double priorMaintenance,
                double priorHarvestWorkerYears)
            {
                double before = Math.Max(
                    portfolio.SelectedFutureFieldMaintenance * years +
                        priorMaintenance * years,
                    portfolio.SelectedFutureFieldHarvestWork +
                        priorHarvestWorkerYears);
                double after = Math.Max(
                    portfolio.SelectedFutureFieldMaintenance * years +
                        (priorMaintenance + maintenancePerYear) * years,
                    portfolio.SelectedFutureFieldHarvestWork +
                        priorHarvestWorkerYears + harvestWorkerYears);
                return Math.Max(0, after - before);
            }
            double ComparableFieldReturn(double workerYears,
                double annualGain, double maintenancePerYear,
                double harvestWorkerYears, double priorMaintenance,
                double priorHarvestWorkerYears)
            {
                double food = Math.Max(0, annualGain * years);
                double recurringWork = IncrementalFieldRecurringWork(
                    annualGain, maintenancePerYear, harvestWorkerYears,
                    priorMaintenance, priorHarvestWorkerYears);
                double work = Math.Max(0, workerYears) + recurringWork;
                return work > Epsilon && !double.IsInfinity(work)
                    ? food / work : 0;
            }
            // Expansion scores are sampled in population-independent chunks.
            // Exact ladder boundaries still define the physics, but strategic
            // alternatives advance by max(10 people, 10% of residents) worth
            // of labor instead of one resident at a time.
            var fieldBands = new List<(double WorkerYears, double AnnualGain,
                double MaintenancePerYear, double HarvestWorkerYears,
                double RecurringWorkerYears, double Score)>();
            double fieldBandWork = 0, fieldBandGain = 0,
                fieldBandMaintenance = 0, fieldBandHarvestWork = 0;
            double priorFieldMaintenance = 0,
                priorFieldHarvestWork = 0;
            double fieldBandYears = portfolio.StrategicLaborStep * years;
            double annualGatherProductivity = cfg.GatherPerPerson * perYear;
            for (int index = 0; index < fieldLadder.Length; index++)
            {
                var step = fieldLadder[index];
                if (!(step.AnnualGain > 0) || !(step.WorkerYears > 0))
                    continue;
                fieldBandWork += step.WorkerYears;
                fieldBandGain += step.AnnualGain;
                fieldBandMaintenance += Math.Max(0,
                    step.MaintenancePerYear);
                double stepFood = step.AnnualGain * years;
                double stepHarvestRate = Math.Max(1,
                    step.HarvestProductivity) * annualGatherProductivity;
                fieldBandHarvestWork += stepHarvestRate > Epsilon
                    ? stepFood / stepHarvestRate
                    : double.PositiveInfinity;
                bool settlementKink = step.VillageAverage + Epsilon >=
                    CultivationEconomy.VillageIntensity &&
                    (index == 0 || fieldLadder[index - 1].VillageAverage +
                        Epsilon < CultivationEconomy.VillageIntensity);
                bool close = elderPhysicalDevelopment ||
                    fieldBandWork + Epsilon >= fieldBandYears ||
                    settlementKink || index == fieldLadder.Length - 1;
                if (!close) continue;
                double recurring = IncrementalFieldRecurringWork(
                    fieldBandGain, fieldBandMaintenance,
                    fieldBandHarvestWork, priorFieldMaintenance,
                    priorFieldHarvestWork);
                fieldBands.Add((fieldBandWork, fieldBandGain,
                    fieldBandMaintenance, fieldBandHarvestWork, recurring,
                    ComparableFieldReturn(fieldBandWork, fieldBandGain,
                        fieldBandMaintenance, fieldBandHarvestWork,
                        priorFieldMaintenance, priorFieldHarvestWork)));
                priorFieldMaintenance += fieldBandMaintenance;
                priorFieldHarvestWork += fieldBandHarvestWork;
                fieldBandWork = fieldBandGain = fieldBandMaintenance =
                    fieldBandHarvestWork = 0;
            }
            if (fieldBandWork > Epsilon)
            {
                double recurring = IncrementalFieldRecurringWork(
                    fieldBandGain, fieldBandMaintenance,
                    fieldBandHarvestWork, priorFieldMaintenance,
                    priorFieldHarvestWork);
                fieldBands.Add((fieldBandWork, fieldBandGain,
                    fieldBandMaintenance, fieldBandHarvestWork, recurring,
                    ComparableFieldReturn(fieldBandWork, fieldBandGain,
                        fieldBandMaintenance, fieldBandHarvestWork,
                        priorFieldMaintenance, priorFieldHarvestWork)));
            }
            double nextFieldReturn = fieldBands.Count > 0
                ? fieldBands[0].Score : double.NaN;
            alloc.FieldMarginalReturn = fieldBands
                .Select(band => band.Score).DefaultIfEmpty(0).Max();
            alloc.DevelopmentAlternatives += fieldBands.Count;
            double currentFieldReceipts = harvest + farmerPlantService +
                farmerFoodService;
            double currentFieldReturn = farmers > 0 &&
                currentFieldReceipts > Epsilon
                ? currentFieldReceipts / (farmers * years)
                : double.PositiveInfinity;
            double currentGatherReturn = foodGatherers > 0 && gatheredFood > Epsilon
                ? gatheredFood / (foodGatherers * years) : double.PositiveInfinity;
            // Price future recurring work from the camp's demonstrated
            // marginal human-food route. The configured gather rate is only a
            // fallback when this boundary did not employ a gatherer; local
            // finite stocks may make the realized opportunity materially lower.
            double futureLaborOpportunityReturn =
                !double.IsInfinity(currentGatherReturn)
                    ? currentGatherReturn : annualGatherProductivity;
            alloc.DefaultLaborReturn = futureLaborOpportunityReturn;
            var demonstratedAlternatives = new List<double>();
            if (!double.IsNaN(nextFieldReturn) &&
                !double.IsInfinity(nextFieldReturn))
                demonstratedAlternatives.Add(nextFieldReturn);
            if (!double.IsInfinity(currentFieldReturn))
                demonstratedAlternatives.Add(currentFieldReturn);
            if (!double.IsInfinity(currentGatherReturn))
                demonstratedAlternatives.Add(currentGatherReturn);
            // Substitution displaces the marginal current human-food route,
            // not the settlement's single best route. Requiring a herd band
            // to beat the maximum made a high-return first field step shield
            // every later, weaker field increment from competition.
            double alternativeFoodReturn = demonstratedAlternatives
                .DefaultIfEmpty(double.PositiveInfinity).Min();
            double substitutableHumanFood = currentFieldReceipts + gatheredFood;
            if (portfolio.HCurrent > 0 &&
                integerGrowthCeiling > herdTarget + Epsilon)
            {
                double remainingNeed = strategicDevelopmentFoodGap +
                    animalFeedstockShortfall;
                double remainingSubstitutionAuthorization =
                    substitutableHumanFood;
                int startTarget = Math.Max(0,
                    (int)Math.Floor(herdTarget + Epsilon));
                int endTarget = Math.Max(startTarget,
                    (int)Math.Floor(integerGrowthCeiling + Epsilon));
                double zero = site.Herd.ZeroDisplacementCapacity;
                var edges = new SortedSet<int> { startTarget, endTarget,
                    Math.Max(startTarget, Math.Min(endTarget,
                        (int)Math.Floor(zero + Epsilon))),
                    Math.Max(startTarget, Math.Min(endTarget,
                        (int)Math.Ceiling(zero - Epsilon))),
                    Math.Max(startTarget, Math.Min(endTarget,
                        (int)Math.Floor(portfolio.HSustainable + Epsilon))),
                    Math.Max(startTarget, Math.Min(endTarget,
                        (int)Math.Ceiling(portfolio.HSustainable - Epsilon))) };
                int retainedHerders = HerdingTooling.RequiredHerdersFor(w, c,
                    years, startTarget);
                for (int workers = retainedHerders +
                        portfolio.StrategicLaborStep;
                    workers < residents; workers +=
                        portfolio.StrategicLaborStep)
                {
                    int supported = (int)Math.Floor(Math.Min(endTarget,
                        HerdingTooling.AnimalsManagedBy(w, c, years,
                            workers)) + Epsilon);
                    if (supported > startTarget) edges.Add(supported);
                    if (supported >= endTarget) break;
                }
                int prior = startTarget;
                int herdBand = 0;
                foreach (int edge in edges.Where(x => x > startTarget)
                    .OrderBy(x => x))
                {
                    herdBand++;
                    int bandAnimals = edge - prior;
                    if (bandAnimals <= 0) { prior = edge; continue; }
                    double needBeforeBand = remainingNeed;
                    double substitutionBeforeBand =
                        remainingSubstitutionAuthorization;
                    double Displaced(int animals) => Math.Max(0,
                        animals * herdProfile.FoodPerAnimal * intervals -
                        site.Survey.NonhumanFood);
                    double netFood = Math.Max(0,
                        bandAnimals * portfolio.ManagedFoodPerAnimal -
                        (Displaced(edge) - Displaced(prior)));
                    double foodPerAnimal = netFood / bandAnimals;
                    int bandHerders = HerdingTooling.RequiredHerdersFor(w, c,
                        years, edge);
                    int priorHerders = HerdingTooling.RequiredHerdersFor(w, c,
                        years, prior);
                    int addedHerdersForBand = Math.Max(0,
                        bandHerders - priorHerders);
                    double score = addedHerdersForBand > 0
                        ? netFood / (addedHerdersForBand * years)
                        : netFood > Epsilon ? double.PositiveInfinity : 0;
                    // Future labor is not free merely because this turn has
                    // spare residents. Growth still needs a real service gap or
                    // the bounded sustainable-growth policy below, and then
                    // competes against the finite gathering and field requests
                    // in the Elder's marginal-return order. Do not price it
                    // against an imaginary unlimited gathering route after the
                    // local forage envelope is exhausted.
                    bool laborCompetitive = addedHerdersForBand == 0
                        ? netFood > Epsilon
                        : score > Epsilon;
                    int directAnimals = laborCompetitive &&
                        foodPerAnimal > Epsilon
                        ? Math.Min(bandAnimals, Math.Max(0,
                            (int)Math.Ceiling(remainingNeed / foodPerAnimal -
                                Epsilon))) : 0;
                    remainingNeed = Math.Max(0, remainingNeed -
                        directAnimals * foodPerAnimal);
                    int substitutionAnimals = 0;
                    if (foodPerAnimal > Epsilon &&
                        score > alternativeFoodReturn + Epsilon &&
                        remainingSubstitutionAuthorization > Epsilon)
                    {
                        substitutionAnimals = Math.Min(
                            bandAnimals - directAnimals, Math.Max(0,
                                (int)Math.Ceiling(
                                    remainingSubstitutionAuthorization /
                                    foodPerAnimal - Epsilon)));
                        remainingSubstitutionAuthorization = Math.Max(0,
                            remainingSubstitutionAuthorization -
                            substitutionAnimals * foodPerAnimal);
                    }
                    // Natural growth inside the supervisor's retained,
                    // zero-displacement envelope is an explicit Development
                    // demand even when this turn's food buckets are already
                    // full. It still competes in return order and must fit the
                    // real staffing bundle; spare labor by itself creates no
                    // request. Beyond that envelope, only direct or genuine
                    // substitution demand authorizes expansion.
                    int sustainableAnimals = laborCompetitive &&
                        foodPerAnimal > Epsilon
                        ? Math.Max(0, Math.Min(edge,
                            (int)Math.Floor(portfolio.HSustainable + Epsilon)) -
                            prior) : 0;
                    int usefulAnimals = elderPhysicalDevelopment
                        ? bandAnimals : Math.Max(sustainableAnimals,
                            directAnimals + substitutionAnimals);
                    InvestmentTrace trace = null;
                    if (captureInvestmentTrace)
                    {
                        trace = new InvestmentTrace
                        {
                            Channel = "herd",
                            Band = herdBand,
                            Units = bandAnimals,
                            CurrentWorkerYears = addedHerdersForBand * years,
                            RecurringWorkerYears = addedHerdersForBand * years,
                            ForecastFood = netFood,
                            UsefulServiceCap = Math.Max(0, needBeforeBand) +
                                Math.Max(0, substitutionBeforeBand) +
                                sustainableAnimals * foodPerAnimal,
                            FiniteAlternativeFood = Math.Max(0,
                                substitutionBeforeBand),
                            FiniteAlternativeWorkerYears =
                                alternativeFoodReturn > Epsilon &&
                                !double.IsInfinity(alternativeFoodReturn)
                                    ? Math.Max(0,
                                        substitutionBeforeBand) /
                                        alternativeFoodReturn : 0,
                            Return = score,
                            AdmissionHurdle = alternativeFoodReturn,
                            PhysicallyFeasible = laborCompetitive &&
                                foodPerAnimal > Epsilon,
                            DefaultAdmitted = usefulAnimals > 0,
                            Admitted = usefulAnimals > 0,
                            Rejection = usefulAnimals > 0 ? "" :
                                !laborCompetitive ||
                                    !(foodPerAnimal > Epsilon)
                                    ? "non-positive physical return"
                                    : "no authorized service"
                        };
                        alloc.InvestmentTraces.Add(trace);
                    }
                    if (usefulAnimals > 0)
                    {
                        int authorizedEnd = prior + usefulAnimals;
                        int authorizedHerders = HerdingTooling.RequiredHerdersFor(
                            w, c, years, authorizedEnd);
                        int addedHerders = Math.Max(0,
                            authorizedHerders - priorHerders);
                        var b = new Bundle();
                        b.RoleYears[WorkforceRoles.Herder] = years *
                            addedHerders / usefulAnimals;
                        var request = new Request
                        {
                            Service = "herd development",
                            Class = ServiceClass.Development,
                            Role = WorkforceRoles.Herder,
                            Units = usefulAnimals,
                            Bundle = b,
                            WholeUnits = true,
                            HerdDevelopment = true,
                            DevelopmentAnimal = true,
                            PriorityScore = addedHerders > 0
                                ? usefulAnimals * foodPerAnimal /
                                    (addedHerders * years)
                                : double.PositiveInfinity,
                            DevelopmentFood = usefulAnimals * foodPerAnimal,
                            DevelopmentFoodPerUnit = foodPerAnimal,
                            TypedDevelopmentNeed = animalFeedstockShortfall,
                            SubstitutionFood = substitutionAnimals *
                                foodPerAnimal,
                            PolicyDevelopmentUnits = sustainableAnimals
                        };
                        herdDevelopment.Add(request);
                        if (trace != null)
                            investmentByRequest[request] = trace;
                        double authorizedScore = request.PriorityScore;
                        alloc.DevelopmentAlternatives++;
                        if (double.IsPositiveInfinity(authorizedScore) ||
                            authorizedScore > alloc.HerdMarginalReturn)
                            alloc.HerdMarginalReturn = authorizedScore;
                    }
                    prior = edge;
                }
            }
            // Field development is authorized by the same projected food gap,
            // plus only the typed plant-feedstock shortfall that current stock
            // and receipts cannot cover. Counting the whole textile input on
            // top of d.Supply was the marginal-grassland ratchet: it requested
            // new fields even while the existing plant envelope covered Cloth.
            var fieldDevelopment = new List<Request>();
            // Field construction is an investment, not an alternate spelling
            // for spare labor. Price the demonstrated gather route only over
            // the finite food it can actually collect. A field band that beats
            // gathering remains wholly eligible; otherwise the alternative
            // consumes only its bounded overlap and the residual investment is
            // exposed to the Elder. This is one analytical horizon account,
            // not an unlimited marginal-food hurdle.
            double fieldLaborHurdle = remainingForage > Epsilon
                ? futureLaborOpportunityReturn : 0;
            // Current construction labor is the request unit consumed by the
            // resolver. Strategic comparison also prices the incremental
            // recurring maintenance/harvest work forecast above. Keep that
            // future cost beside the request instead of pretending one builder
            // is one complete unit of field labor in the cross-channel rays.
            var fieldStrategicPeoplePerUnit = new Dictionary<Request, double>();
            var fieldFiniteAlternativeFoodPerUnit =
                new Dictionary<Request, double>();
            bool finiteFieldOpportunityPolicy = diagnostics?
                .UseFiniteFieldOpportunity == true ||
                diagnostics?.UseLegacyUnboundedFieldHurdle != true &&
                diagnostics?.ExposePhysicalFieldBands != true;
            if (!NoFieldDevelopmentDiagnostic && CultivationEconomy.Enabled(c) && d.Footprint.Length > 0)
            {
                if (ResolutionMetrics.CaptureDemandAllocations)
                {
                    alloc.FieldExpansionEligible = true;
                    alloc.FieldPotentialSteps = 0;
                    foreach (var step in CultivationEconomy.ExpansionLadder(w, c, years, d.Footprint))
                    {
                        if (!(step.AnnualGain > 0) || !(step.WorkerYears > 0)) continue;
                        alloc.FieldPotentialSteps++; alloc.FieldPotentialGainPerYear += step.AnnualGain; alloc.FieldPotentialWorkerYears += step.WorkerYears;
                    }
                }
                if (elderPhysicalDevelopment || fieldGapPerYear > Epsilon)
                {
                    double gain = 0;
                    double finiteAlternativeRemaining = remainingForage;
                    int fieldBand = 0;
                    foreach (var band in fieldBands)
                    {
                        fieldBand++;
                        if (!elderPhysicalDevelopment &&
                            gain >= fieldGapPerYear - Epsilon) break;
                        double fieldPeople = band.WorkerYears / years;
                        double pricedWorkerYears = band.WorkerYears +
                            band.RecurringWorkerYears;
                        bool physical = fieldPeople > Epsilon &&
                            band.Score > Epsilon &&
                            !double.IsInfinity(pricedWorkerYears);
                        bool defaultAdmitted = physical &&
                            band.Score > fieldLaborHurdle + Epsilon;
                        // A higher-return field increment dominates this
                        // gathering alternative and leaves that finite route
                        // available to challenge later, weaker increments.
                        double finiteAlternativeFood = physical &&
                            !defaultAdmitted ? Math.Min(
                            finiteAlternativeRemaining,
                            Math.Max(0, pricedWorkerYears) *
                                futureLaborOpportunityReturn) : 0;
                        if (finiteAlternativeFood > Epsilon)
                            finiteAlternativeRemaining = Math.Max(0,
                                finiteAlternativeRemaining -
                                finiteAlternativeFood);
                        double finiteAlternativeWorkerYears =
                            futureLaborOpportunityReturn > Epsilon
                                ? finiteAlternativeFood /
                                    futureLaborOpportunityReturn : 0;
                        bool admitted = physical &&
                            (elderPhysicalDevelopment || defaultAdmitted ||
                             diagnostics?.ExposePhysicalFieldBands == true ||
                             finiteFieldOpportunityPolicy);
                        InvestmentTrace trace = null;
                        if (captureInvestmentTrace)
                        {
                            trace = new InvestmentTrace
                            {
                                Channel = "field",
                                Band = fieldBand,
                                Units = Math.Max(0, fieldPeople),
                                CurrentWorkerYears = Math.Max(0,
                                    band.WorkerYears),
                                RecurringWorkerYears = Math.Max(0,
                                    band.RecurringWorkerYears),
                                ForecastFood = Math.Max(0,
                                    band.AnnualGain * years),
                                UsefulServiceCap = Math.Max(0,
                                    (fieldGapPerYear - gain) * years),
                                FiniteAlternativeFood =
                                    finiteAlternativeFood,
                                FiniteAlternativeWorkerYears =
                                    finiteAlternativeWorkerYears,
                                Return = band.Score,
                                AdmissionHurdle = fieldLaborHurdle,
                                PhysicallyFeasible = physical,
                                DefaultAdmitted = defaultAdmitted,
                                Admitted = admitted,
                                Rejection = admitted ? "" : !physical
                                    ? "non-positive physical return"
                                    : finiteFieldOpportunityPolicy
                                        ? "finite alternative fills band"
                                        : "below unbounded forage hurdle"
                            };
                            alloc.InvestmentTraces.Add(trace);
                        }
                        if (!admitted)
                            continue;
                        var fieldBundle = new Bundle();
                        fieldBundle.RoleYears[WorkforceRoles.Farmer] = years;
                        var request = new Request
                        {
                            Service = "field expansion",
                            Class = ServiceClass.Development,
                            Role = WorkforceRoles.Farmer,
                            Units = fieldPeople,
                            Bundle = fieldBundle,
                            PriorityScore = band.Score,
                            DevelopmentFood = Math.Max(0,
                                band.AnnualGain * years),
                            DevelopmentFoodPerUnit = fieldPeople > Epsilon
                                ? Math.Max(0, band.AnnualGain * years) /
                                    fieldPeople : 0,
                            TypedDevelopmentNeed = plantFeedstockShortfall,
                            DevelopmentPlant = true
                        };
                        fieldDevelopment.Add(request);
                        if (trace != null)
                            investmentByRequest[request] = trace;
                        if (finiteFieldOpportunityPolicy &&
                            finiteAlternativeFood > Epsilon)
                            fieldFiniteAlternativeFoodPerUnit[request] =
                                finiteAlternativeFood / fieldPeople;
                        fieldStrategicPeoplePerUnit[request] =
                            band.Score > Epsilon && years > Epsilon
                                ? request.DevelopmentFoodPerUnit /
                                    (band.Score * years)
                                : double.PositiveInfinity;
                        gain += band.AnnualGain;
                    }
                }
            }

            // Keep every physically authorized coarse band available until the
            // Elder has reserved Continuity and Recovery.  Only then can the
            // three Development rays be compared against the same residual
            // labor and input ledger.  `developmentRayLimits` is filled by that
            // bounded comparison below; requests outside its selected prefix
            // remain diagnostic only and receive no grant.
            foreach (var request in fieldDevelopment)
                alloc.Requests.Add(request);
            foreach (var request in herdDevelopment)
                alloc.Requests.Add(request);
            var developmentRayLimits = new Dictionary<Request, double>();

            // A finite alternative can make the first coarse field step have
            // zero incremental value even though a larger sequential prefix
            // crosses that bounded opportunity. Find that one physical kink
            // in a single pass. The strategic walk may jump to it once; it
            // does not scan residents or combine field and herd grids.
            double fieldOpportunityCrossingBudget = 0;
            if (finiteFieldOpportunityPolicy && fieldDevelopment.Count > 0)
            {
                double usefulCap = strategicDevelopmentFoodGap +
                    plantFeedstockShortfall;
                double unitsBefore = 0, grossBefore = 0,
                    alternativeBefore = 0;
                foreach (var request in fieldDevelopment)
                {
                    double grossPerUnit = Math.Max(0,
                        request.DevelopmentFoodPerUnit);
                    fieldFiniteAlternativeFoodPerUnit.TryGetValue(request,
                        out double alternativePerUnit);
                    double NetAt(double units) => Math.Min(usefulCap,
                        grossBefore + units * grossPerUnit) -
                        (alternativeBefore + units * alternativePerUnit);
                    if (NetAt(0) <= Epsilon &&
                        NetAt(request.Units) > Epsilon)
                    {
                        double low = 0, high = request.Units;
                        for (int iteration = 0; iteration < 24; iteration++)
                        {
                            double middle = (low + high) * .5;
                            if (NetAt(middle) > Epsilon) high = middle;
                            else low = middle;
                        }
                        fieldOpportunityCrossingBudget = unitsBefore + high;
                        break;
                    }
                    unitsBefore += request.Units;
                    grossBefore += request.Units * grossPerUnit;
                    alternativeBefore += request.Units *
                        alternativePerUnit;
                }
            }
            alloc.FieldOpportunityCrossingBudget =
                fieldOpportunityCrossingBudget;

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

            // Food the committed Protected clothing may draw before meals.
            double protectedPlantRoom = Math.Min(d.TerrestrialAfterHerds + Math.Max(0, c.PlantFood), Math.Max(0, c.Food + d.Supply));
            double protectedAnimalRoom = d.HusbandryEnabled
                ? Math.Min(Math.Max(0, c.AnimalFood) + d.ManagedFood.AnimalFood + portfolio.SlaughterFood,
                    Math.Max(0, c.Food + d.Supply + portfolio.SlaughterFood)) : 0;
            void ProtectedFeedstock(Func<Request, double> grant, out double plant, out double animal)
            {
                plant = animal = 0;
                if (!protectedSplit) return;
                foreach (var q in alloc.Requests)
                    if (q.Class == ServiceClass.Protected && q.Service == "clothing" && q.Bundle != null)
                    { double g = grant(q); plant += q.Bundle.Plant * g; animal += q.Bundle.Animal * g; }
            }

            // Totals for a set of grants: role worker-years (extraction and gathering included), and the binding check.
            string Binding(Func<Request, double> grant)
            {
                var total = new Bundle();
                foreach (var q in alloc.Requests) if (q.Bundle != null) total.Add(q.Bundle, grant(q));
                // Herd-development requests carry average role years for
                // diagnostics, but integer heads have a nonlinear exact staffing
                // boundary. Replace their approximation with the authoritative
                // tooling requirement before every feasibility test.
                double approximateDevelopmentHerderYears = alloc.Requests
                    .Where(q => q.HerdDevelopment && q.Bundle != null &&
                        q.Bundle.RoleYears.TryGetValue(WorkforceRoles.Herder,
                            out _))
                    .Sum(q => q.Bundle.RoleYears[WorkforceRoles.Herder] * grant(q));
                if (approximateDevelopmentHerderYears > 0 &&
                    total.RoleYears.TryGetValue(WorkforceRoles.Herder,
                        out double totalHerderYears))
                    total.RoleYears[WorkforceRoles.Herder] = Math.Max(0,
                        totalHerderYears - approximateDevelopmentHerderYears);
                if (herdDevelopment.Count > 0)
                {
                    double authorized = Math.Min(portfolio.HGrowthCeiling,
                        herdTarget + herdDevelopment.Sum(grant));
                    int exactAdded = Math.Max(0,
                        HerdingTooling.RequiredHerdersFor(w, c, years,
                            authorized) - herders);
                    if (exactAdded > 0)
                        total.RoleYears[WorkforceRoles.Herder] =
                            (total.RoleYears.TryGetValue(
                                WorkforceRoles.Herder, out double existing)
                                ? existing : 0) + exactAdded * years;
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
                foreach (var role in total.RoleYears)
                    persons += Math.Ceiling(role.Value / years - Epsilon);
                // All food uses one finite post-meal ledger. Typed recipe input,
                // granted reserve refill and granted growth food cannot each
                // spend the same accessible stock.
                // The committed Protected clothing is paid before meals (the
                // resolver honours ProtectedClothingUnits), bounded by the food
                // that can exist this turn; it reserves no gatherer time because
                // the kernel already debited it from the meal budget.
                ProtectedFeedstock(grant, out double protectedPlant, out double protectedAnimal);
                // Only this cohort's current clothing input may displace the
                // reserve refill. Recovery clothing and tent feedstock remain
                // ordinary manufacturing: using max(normal, clothing) for the
                // aggregate bundle would let an unrelated input borrow the
                // clothing exception and promise work the resolver cannot fund.
                double currentPlant = 0, currentAnimal = 0;
                foreach (var q in alloc.Requests)
                    if (q.Service == "clothing" && q.Bundle != null)
                    { double g = grant(q); currentPlant += q.Bundle.Plant * g; currentAnimal += q.Bundle.Animal * g; }
                double plantClothingEnvelope = protectedSplit ? Math.Max(d.PlantEnvelopeClothing, protectedPlantRoom) : d.PlantEnvelopeClothing;
                double animalClothingEnvelope = (protectedSplit ? Math.Max(d.AnimalEnvelopeClothing, protectedAnimalRoom) : d.AnimalEnvelopeClothing)
                    + portfolio.SlaughterFood;
                double plantLimit = Math.Min(plantClothingEnvelope, d.PlantEnvelope + currentPlant);
                double animalLimit = Math.Min(animalClothingEnvelope, d.AnimalEnvelope + portfolio.SlaughterFood + currentAnimal);
                if (total.Plant > plantLimit + Epsilon) return "plant feedstock";
                if (total.Animal > animalLimit + Epsilon) return "animal feedstock";
                double foodRoom = Math.Max(0, c.Food + d.Supply +
                    portfolio.SlaughterFood - d.Meals);
                if (protectedSplit)
                    foodRoom = Math.Min(Math.Max(0, c.Food + d.Supply + portfolio.SlaughterFood),
                        foodRoom + protectedPlant + protectedAnimal);
                if (total.Plant + total.Animal + total.FoodService >
                    foodRoom + Epsilon) return "food room";
                if (total.Fabric > Math.Max(0, c.Inventory.AnimalFabric) + Epsilon) return "fabric";
                if (total.Material > Math.Max(0, c.Inventory.HousingMaterial) + Epsilon) return "housing material";
                double plantFromLand = Math.Max(0, total.Plant - protectedPlant -
                    availablePlantInventory);
                // Retained operating farmers and explicit Gatherers draw from
                // one land-food work budget.  The farmer capacity left after
                // Survival is available to the resolver's feedstock-first
                // claim order and must not be charged again as gathering.
                double plantAfterFarmers = Math.Max(0,
                    plantFromLand - farmerSpareCapacity);
                double feedstockGatherers = gatherRate > 0
                    ? plantAfterFarmers / gatherRate : 0;
                persons += gathererYears / years + feedstockGatherers;
                return persons > budget + Epsilon ? "labor" : null;
            }

            // ---- Kernel-scored Elder split (Docs/ClothingCoverageAndSurplusBirths.md) ----
            // Score a grid of (clothing, shelter) levels with the demographic kernel over the turn and commit the best as
            // the Protected class. Mixed farming/herding camps also score the other clothing and shelter route.
            if (KernelSplitOn(cfg) && PhysicalDemography.Enabled(cfg))
            {
                long started = System.Diagnostics.Stopwatch.GetTimestamp();
                int calls = 0;
                int objective = SplitObjective(cfg); double tolerance = SplitTolerance(cfg);
                CraftGood durable = forcedShelterRoute ? deficitPreferred : ShelterRoute(deficitPreferred, totalGapPeople);
                double clothingStock = Math.Max(0, c.Inventory.Cloth - c.TradeExport(CraftGood.Cloth))
                    + Math.Max(0, c.Inventory.AnimalFabric - c.TradeExport(CraftGood.AnimalFabric));
                double effective = Math.Max(0, c.Population + c.GrowthRemainder - c.DeathRemainder + (d.Consumers - c.Population));
                double maximumYears = PhysicalDemography.BirthOnlyPersonYears(effective, cfg.BaseBirthRate, years);
                RouteCost ClothingCost(CraftGood good)
                {
                    if (!byGood.ContainsKey(good) || !Usable(byGood[good]) || !(NeolithicProduction.Priority(candidate, good) > 0)) return null;
                    var b = Craft(good);
                    return b == null ? null : new RouteCost { Good = good, Persons = b.RoleYears.Values.Sum() / years, Food = b.Plant + b.Animal, Plant = b.Plant, Animal = b.Animal,
                        Stone = b.Stone, WoodOrClay = b.Wood + b.Clay + b.Construction };
                }
                RouteCost ShelterCost(CraftGood good)
                {
                    if (!byGood.ContainsKey(good) || !Usable(byGood[good]) || !(NeolithicProduction.Priority(candidate, good) > 0)) return null;
                    if (good == CraftGood.LightHousing && !tentsPossible) return null;
                    var r = byGood[good]; var b = Craft(good);
                    if (b == null) return null;
                    double persons = b.RoleYears.Values.Sum() / years, food = b.Plant + b.Animal, plant = b.Plant, animal = b.Animal;
                    double wood = b.Wood + b.Construction, clay = b.Clay, stone = b.Stone;
                    foreach (var (input, perUnit) in new[] { (CraftGood.HousingMaterial, r.HousingMaterial), (CraftGood.AnimalFabric, r.AnimalFabric) })
                    {
                        if (!(perUnit > 0)) continue;
                        var m = Usable(byGood[input]) ? Craft(input) : null;
                        if (m == null) return null;
                        persons += perUnit * m.RoleYears.Values.Sum() / years; food += perUnit * (m.Plant + m.Animal);
                        plant += perUnit * m.Plant; animal += perUnit * m.Animal;
                        wood += perUnit * (m.Wood + m.Construction); clay += perUnit * m.Clay; stone += perUnit * m.Stone;
                    }
                    double extraction = ExtractionYears(MaterialId.Wood, wood) + ExtractionYears(MaterialId.Clay, clay) + ExtractionYears(MaterialId.Stone, stone);
                    if (double.IsInfinity(extraction)) return null;
                    persons += extraction / years;
                    double per = PerUnit(good);
                    return new RouteCost { Good = good, Persons = persons / per, Food = food / per, Plant = plant / per, Animal = animal / per,
                        Stone = stone / per, WoodOrClay = (wood + clay) / per };
                }
                // Clothing is sized against the birth-only person-year envelope (not the opening count), capped at useful demand.
                double ClothingMax(RouteCost cost, double useful) => cost == null ? 0 : Math.Min(useful,
                    Math.Max(0, PhysicalDemography.ClothingPerPersonYear(cfg) * maximumYears - clothingStock));
                var clothCost = ClothingCost(clothingRoute);
                var shelterCost = ShelterCost(durable);
                double clothingMax = ClothingMax(clothCost, clothingUseful);
                double shelterMax = shelterCost == null ? 0 : totalGapPeople;
                var capacityNow = NeolithicProduction.HousingCapacity(w, candidate, endDays);
                double capacityEnd = capacityNow.Total;
                int workers = Math.Max(0, residents - herders - farmers - fishers);
                double terrestrialRoom = Math.Max(0, d.ForageAfterHerds - Math.Min(fishingFood, selectedPassive));
                bool mixed = MixedRoutesOn(cfg) && d.CultivationEnabled && d.HusbandryEnabled;
                double stoneRoom = Inventory(MaterialId.Stone) + Accessible(MaterialId.Stone);
                double woodClayRoom = Inventory(MaterialId.Wood) + Accessible(MaterialId.Wood) + Inventory(MaterialId.Clay) + Accessible(MaterialId.Clay);
                double Use(double qc, RouteCost cloth, double qh, RouteCost shelter, Func<RouteCost, double> per)
                    => (qc > 0 ? qc * per(cloth) : 0) + (qh > 0 ? qh * per(shelter) : 0);
                KernelCandidate Candidate(int ci, int hi, double qc, double qh, RouteCost cloth, RouteCost shelter)
                {
                    double persons = (qc > 0 ? qc * cloth.Persons : 0) + (qh > 0 ? qh * shelter.Persons : 0);
                    if (persons > workers + Epsilon) return null;
                    if (mixed && (Use(qc, cloth, qh, shelter, r => r.Plant) > protectedPlantRoom + Epsilon || Use(qc, cloth, qh, shelter, r => r.Animal) > protectedAnimalRoom + Epsilon
                        || Use(qc, cloth, qh, shelter, r => r.Stone) > stoneRoom + Epsilon || Use(qc, cloth, qh, shelter, r => r.WoodOrClay) > woodClayRoom + Epsilon)) return null;
                    double gatherers = Math.Max(0, workers - persons);
                    double income = animalReceipts + fishingFood + Math.Min(terrestrialRoom, harvest + farmerSpareCapacity + gatherers * gatherRate);
                    // Only clothing feedstock is paid before meals (ProtectedClothingUnits); shelter feedstock is ordinary
                    // manufacturing that may reduce newborn food but never current meals.
                    double protectedFood = qc * (cloth?.Food ?? 0), shelterFood = qh * (shelter?.Food ?? 0);
                    double totalFood = Math.Max(0, c.Food) + income, afterClothing = Math.Max(0, totalFood - protectedFood);
                    return new KernelCandidate
                    {
                        ClothingLevel = ci, ShelterLevel = hi, ClothingUnits = qc, ShelterPeople = qh, Persons = persons, ProtectedFood = protectedFood,
                        ClothingRoute = cloth?.Good ?? clothingRoute, ShelterRoute = shelter?.Good ?? durable,
                        Food = Math.Min(TurnResolver.PhysicalDemographicFoodBudget(totalFood, d.Meals, growthFood, protectedFood), Math.Max(Math.Min(Math.Max(0, d.Meals), afterClothing), afterClothing - shelterFood))
                    };
                }
                DemographicResult Kernel(bool exact, double population, double food, double clothing, double capacity, double deficit)
                {
                    calls++;
                    var result = exact
                        ? PhysicalDemography.AdvanceBudget(cfg, population, years, food, clothing, capacity, deficit, true)
                        : PhysicalDemography.PlanningBudget(cfg, population, years, food, clothing, capacity, deficit, true);
                    System.Threading.Interlocked.Add(ref KernelServiceIterations, result.ServiceIterations);
                    return result;
                }
                double QoL(DemographicResult result, CraftGood shelterRoute, double qh)
                {
                    double people = years > 0 ? result.PersonYears / years : effective;
                    double clothingTerm = 1 - c.RudimentaryExpectation * (1 - result.ClothingCoverage);
                    double housingTerm = 1 - c.RudimentaryExpectation * (1 - result.AverageHousingCoverage);
                    var shelter = new NeolithicProduction.ShelterCapacity(capacityNow.Fixed + (shelterRoute == CraftGood.Housing ? qh : 0), capacityNow.Light + (shelterRoute == CraftGood.Housing ? 0 : qh));
                    return result.Nutrition * c.WaterNeedsMetRatio * (n.RudimentaryWeightClothing * clothingTerm + n.RudimentaryWeightHousing * housingTerm)
                        * NeolithicProduction.SocialCohesion(n, NeolithicProduction.HousingShare(n, shelter, people), people);
                }
                // Objectives: 0 person-years; 1 QoL-weighted person-years; 2 lexicographic (person-years within the
                // tolerance of the best, then QoL; the default); 3 terminal-population welfare; 4 deaths averted.
                double Value(double py, double qol, double end, double deaths)
                    => objective == 2 ? qol
                    : objective == 1 ? py * qol
                    : objective == 3 ? (py + years * end) * qol
                    : objective == 4 ? py - years * deaths
                    : py;
                var scoredInputs = new Dictionary<(double, double, double, bool), DemographicResult>();
                void Score(KernelCandidate k, bool exact)
                {
                    if (k.Result != null) return;
                    var key = (k.Food, k.ClothingUnits, k.ShelterPeople, exact);
                    if (!scoredInputs.TryGetValue(key, out k.Result))
                        scoredInputs[key] = k.Result = Kernel(exact, effective, k.Food, clothingStock + k.ClothingUnits, capacityEnd + k.ShelterPeople, c.FoodDeficitYears);
                    k.PersonYears = k.Result.PersonYears; k.QoL = QoL(k.Result, k.ShelterRoute, k.ShelterPeople);
                    k.Value = Value(k.PersonYears, k.QoL, k.Result.FinalPopulation, k.Result.Deaths);
                }
                // The winner in list order; ties keep the earlier (cheaper) option.
                KernelCandidate Best(List<KernelCandidate> pool)
                {
                    double maxPersonYears = pool.Count > 0 ? pool.Max(k => k.PersonYears) : 0;
                    double bestScore = double.NegativeInfinity; KernelCandidate best = null;
                    foreach (var k in pool)
                    {
                        double score = objective == 2
                            ? (k.PersonYears >= maxPersonYears * (1 - tolerance) - 1e-12 ? k.Value : double.MinValue)
                            : k.Value;
                        if (best == null || score > bestScore + Math.Abs(bestScore) * 1e-9 + 1e-12) { bestScore = score; best = k; }
                    }
                    return best;
                }
                (KernelCandidate split, List<KernelCandidate> pool) Decide(bool exact)
                {
                    // Stage 1: the (clothing, shelter) grid on the default routes, in grid order.
                    var pool = new List<KernelCandidate>();
                    for (int ci = 0; ci < KernelClothingGrid.Length; ci++)
                    {
                        if (KernelShelterOnlyDiagnostic && ci > 0) break;
                        double qc = clothingMax * KernelClothingGrid[ci];
                        if (ci > 0 && !(qc > Epsilon)) continue;
                        for (int hi = 0; hi < KernelShelterGrid.Length; hi++)
                        {
                            double qh = shelterMax * KernelShelterGrid[hi];
                            if (hi > 0 && !(qh > Epsilon)) continue;
                            var k = Candidate(ci, hi, qc, qh, clothCost, shelterCost);
                            if (k != null) { Score(k, exact); pool.Add(k); }
                        }
                    }
                    var best = Best(pool);
                    if (!mixed || best == null) return (best, pool);
                    // Stage 2: the other clothing route at the chosen shelter.
                    var otherCloth = KernelShelterOnlyDiagnostic || forcedClothingRoute ? null : ClothingCost(clothingRoute == CraftGood.Cloth ? CraftGood.AnimalFabric : CraftGood.Cloth);
                    if (otherCloth != null)
                    {
                        double otherMax = ClothingMax(otherCloth, Math.Max(0, NeolithicProduction.Demand(w, candidate, otherCloth.Good, years, residents, false, false, d.Sources)));
                        var shelterOf = best.ShelterPeople > 0 ? ShelterCost(best.ShelterRoute) : shelterCost;
                        for (int ci = 1; ci < KernelClothingGrid.Length && otherMax > Epsilon; ci++)
                        {
                            var k = Candidate(ci, best.ShelterLevel, otherMax * KernelClothingGrid[ci], best.ShelterPeople, otherCloth, shelterOf);
                            if (k != null) { Score(k, exact); pool.Add(k); }
                        }
                        best = Best(pool);
                    }
                    // Stage 3: the other shelter route at the chosen clothing.
                    // The alternative shelter route stays inside the settlement policy: a mobile camp without retained fixed
                    // or field capital does not acquire fixed Housing merely because a capability exposes the recipe.
                    var otherShelter = forcedShelterRoute || durable == CraftGood.LightHousing && mobilePolicy ? null
                        : ShelterCost(durable == CraftGood.Housing ? CraftGood.LightHousing : CraftGood.Housing);
                    if (otherShelter != null && totalGapPeople > Epsilon)
                    {
                        var clothOf = best.ClothingUnits > 0 ? ClothingCost(best.ClothingRoute) : clothCost;
                        for (int hi = 1; hi < KernelShelterGrid.Length; hi++)
                        {
                            var k = Candidate(best.ClothingLevel, hi, best.ClothingUnits, totalGapPeople * KernelShelterGrid[hi], clothOf, otherShelter);
                            if (k != null) { Score(k, exact); pool.Add(k); }
                        }
                        best = Best(pool);
                    }
                    return (best, pool);
                }
                var decision = Decide(KernelExactScoringDiagnostic);
                if (KernelVerifyDiagnostic && !KernelExactScoringDiagnostic && decision.split != null)
                {
                    var exactDecision = Decide(true);
                    lock (KernelVerifyLock)
                    {
                        KernelVerifyDecisions++;
                        var a = decision.split; var b = exactDecision.split;
                        if (b == null || a.ClothingRoute != b.ClothingRoute || a.ClothingLevel != b.ClothingLevel || a.ShelterRoute != b.ShelterRoute
                            || a.ShelterLevel != b.ShelterLevel) KernelVerifyMismatches++;
                        foreach (var fastK in decision.pool)
                        {
                            var exactK = exactDecision.pool.FirstOrDefault(k => k.ClothingRoute == fastK.ClothingRoute && k.ClothingLevel == fastK.ClothingLevel && k.ShelterRoute == fastK.ShelterRoute && k.ShelterLevel == fastK.ShelterLevel);
                            if (exactK == null) continue;
                            double err = Math.Abs(fastK.PersonYears - exactK.PersonYears) / Math.Max(1, exactK.PersonYears);
                            if (err > KernelVerifyMaxPersonYearsError)
                                KernelVerifyWorst = string.Join(",", new[] { effective, years, fastK.Food, clothingStock + fastK.ClothingUnits, capacityEnd + fastK.ShelterPeople, c.FoodDeficitYears, fastK.PersonYears, exactK.PersonYears }.Select(v => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
                            KernelVerifyMaxPersonYearsError = Math.Max(KernelVerifyMaxPersonYearsError, err);
                            KernelVerifyMaxQoLError = Math.Max(KernelVerifyMaxQoLError, Math.Abs(fastK.QoL - exactK.QoL));
                        }
                    }
                }
                var chosenSplit = decision.split;
                double bestC = chosenSplit?.ClothingUnits ?? 0, bestH = chosenSplit?.ShelterPeople ?? 0;
                int bestCi = chosenSplit?.ClothingLevel ?? 0, bestHi = chosenSplit?.ShelterLevel ?? 0;
                CraftGood chosenClothing = chosenSplit?.ClothingRoute ?? clothingRoute, chosenShelter = chosenSplit?.ShelterRoute ?? durable;
                System.Threading.Interlocked.Add(ref KernelSplitCalls, calls);
                System.Threading.Interlocked.Add(ref KernelSplitTicks, System.Diagnostics.Stopwatch.GetTimestamp() - started);
                System.Threading.Interlocked.Increment(ref KernelSplitCamps);
                System.Threading.Interlocked.Increment(ref KernelChosenClothing[bestCi]);
                System.Threading.Interlocked.Increment(ref KernelChosenShelter[bestHi]);
                if (mixed)
                {
                    System.Threading.Interlocked.Increment(ref KernelMixedCamps);
                    if (chosenClothing != clothingRoute) System.Threading.Interlocked.Increment(ref KernelMixedOtherClothing);
                    if (chosenShelter != durable) System.Threading.Interlocked.Increment(ref KernelMixedOtherShelter);
                }
                alloc.ChosenClothingRoute = chosenClothing; alloc.ChosenShelterRoute = chosenShelter; kernelRoutesSet = true;
                // The committed action (the shape a Village Elder policy would emit).
                alloc.ElderAction = new ElderAction
                {
                    ClothingRoute = chosenClothing, ClothingLevel = bestCi, ClothingUnits = bestC,
                    ShelterRoute = chosenShelter, ShelterLevel = bestHi, ShelterPeople = bestH,
                    Posture = LabourPosture.HerdFirst, PersonYears = chosenSplit?.PersonYears ?? 0, QoL = chosenSplit?.QoL ?? 0
                };
                if (ResolutionMetrics.CaptureDemandAllocations)
                    alloc.KernelCandidates = decision.pool.Select(k => new ElderAction
                    {
                        ClothingRoute = k.ClothingRoute, ClothingLevel = k.ClothingLevel, ClothingUnits = k.ClothingUnits,
                        ShelterRoute = k.ShelterRoute, ShelterLevel = k.ShelterLevel, ShelterPeople = k.ShelterPeople,
                        PersonYears = k.PersonYears, QoL = k.QoL
                    }).ToList();

                // Enact: replace the clothing and shelter requests with the committed Protected split.
                var replaced = new HashSet<string> { "shelter replacement", "portable shelter replacement", "shelter deficit" };
                if (!KernelShelterOnlyDiagnostic) { replaced.Add("clothing"); replaced.Add("clothing reserve"); }
                foreach (var q in alloc.Requests.Where(q => replaced.Contains(q.Service)).ToList())
                {
                    freeFabric += q.ReservedFabric; freeMaterial += q.ReservedMaterial;
                    alloc.Requests.Remove(q);
                }
                protectedSplit = !KernelShelterOnlyDiagnostic; kernelEnacted = true;
                Request protectedClothing = null;
                if (!KernelShelterOnlyDiagnostic)
                {
                    double usefulOfChosen = chosenClothing == clothingRoute ? clothingUseful
                        : Math.Max(0, NeolithicProduction.Demand(w, candidate, chosenClothing, years, residents, false, false, d.Sources));
                    protectedClothing = Request("clothing", ServiceClass.Protected, chosenClothing, bestC);
                    Request("clothing reserve", ServiceClass.Recovery, chosenClothing, Math.Max(0, usefulOfChosen - bestC));
                }
                if (bestH > Epsilon) Request("shelter protected", ServiceClass.Protected, chosenShelter, bestH / PerUnit(chosenShelter));
                // Protected shelter serves fixed replacement first, then portable replacement, then the deficit; the
                // remainders keep their ordinary classes and routes.
                double protectedLeft = bestH;
                double Serve(double people) { double s = Math.Min(people, protectedLeft); protectedLeft -= s; return people - s; }
                ShelterRequest("shelter replacement", ServiceClass.Continuity, CraftGood.Housing, Serve(fixedReplacementPeople));
                ShelterRequest("portable shelter replacement", ServiceClass.Continuity, CraftGood.LightHousing, Serve(lightReplacementPeople));
                shelterDeficitPeople = Serve(shelterDeficitPeople);
                ShelterRequest("shelter deficit", ServiceClass.Recovery, deficitPreferred, shelterDeficitPeople);
                // The committed persons may come out of the gatherer reservation; lower classes keep only the original slack.
                double committedPersons = chosenSplit?.Persons ?? 0;
                kernelBudgetBase = budget; kernelCommittedPersons = committedPersons; kernelWorkers = workers;
                budget = Math.Max(budget, Math.Min(workers, committedPersons));
                alloc.ResidentGoodsBudget = budget;
                protectedClothingUnits = protectedClothing != null ? 0 : -1;
            }

            // ---- Contract 7 service levels ----
            // Each goods service's native request total is recorded for teacher labels; a learned level caps that total,
            // filling higher service classes first. A level never adds units beyond the native physical request.
            for (int s = 0; s < ElderModel9Contract.GoodsServices.Length; s++)
            {
                var group = alloc.Requests.Where(q => q.Bundle != null && ElderModel9Contract.ServiceOf(q.Service) == s)
                    .OrderBy(q => q.Class).ThenBy(q => alloc.Requests.IndexOf(q)).ToList();
                double total = group.Sum(q => Math.Max(0, q.Units));
                alloc.ServiceRequested[s] = total;
                double[] levels = elderDirective?.ServiceLevels;
                if (levels == null || s >= levels.Length || !ElderRLContract.Finite(levels[s]) || levels[s] < 0) continue;
                double remaining = ElderRLContract.Unit(levels[s]) * total;
                foreach (var q in group)
                {
                    double units = Math.Min(Math.Max(0, q.Units), remaining);
                    remaining -= units;
                    if (units < q.Units - Epsilon) q.Unmet = "elder service level";
                    q.Units = units;
                }
            }

            // ---- Elder arbitration ----
            // Protected (when a kernel split is committed), Continuity and Recovery
            // retain capped equal fulfilment. Development is different: complete
            // marginal increments are admitted in physical retained-return order,
            // and both channels debit one shared food gap.
            foreach (ServiceClass cls in kernelEnacted
                ? new[] { ServiceClass.Protected, ServiceClass.Continuity, ServiceClass.Recovery }
                : new[] { ServiceClass.Continuity, ServiceClass.Recovery })
            {
                // Protected requests are arbitrated one at a time in request order (clothing before shelter), so an
                // infeasible shelter route cannot freeze the clothing the kernel committed.
                var classRequests = alloc.Requests.Where(q => q.Class == cls && q.Bundle != null).ToList();
                foreach (var group in cls == ServiceClass.Protected ? classRequests.Select(q => new List<Request> { q }).ToList() : new List<List<Request>> { classRequests })
                {
                var active = group;
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
                if (cls == ServiceClass.Protected)
                {
                    // An ungranted protected split must not leave its labour as slack for Development.
                    double requested = 0, granted = 0;
                    foreach (var q in alloc.Requests.Where(q => q.Class == ServiceClass.Protected && q.Bundle != null))
                    { double perUnit = q.Bundle.RoleYears.Values.Sum(); requested += q.Units * perUnit; granted += q.Granted * perUnit; }
                    double fraction = requested > Epsilon ? Numbers.Clamp(granted / requested, 0, 1) : 0;
                    budget = Math.Max(kernelBudgetBase, Math.Min(kernelWorkers, kernelCommittedPersons * fraction));
                    alloc.ResidentGoodsBudget = budget;
                }
            }

            // Compare equal-work Development forecasts after the retained
            // services above have claimed their real resources. Strategic
            // resolution is deliberately coarse: max(10 residents, 10% of the
            // population), hence at most ten budgets and three rays. Exact
            // field kinks and whole-animal/herder boundaries remain inside the
            // cached prefixes. This is O(F + H + 30), not a population scan or
            // a field x herd Cartesian search.
            var rayRequests = new HashSet<Request>(fieldDevelopment.Concat(
                herdDevelopment));
            Dictionary<Request, double> RayPrefix(double fieldPeople,
                double herdPeople)
            {
                var grants = new Dictionary<Request, double>();
                double fieldLeft = Math.Max(0, fieldPeople);
                foreach (var request in fieldDevelopment)
                {
                    if (!(fieldLeft > Epsilon)) break;
                    double units = Math.Min(request.Units, fieldLeft);
                    if (units > Epsilon) grants[request] = units;
                    fieldLeft -= units;
                }

                int additionalHerders = Math.Max(0,
                    (int)Math.Floor(herdPeople + Epsilon));
                // Any positive herd budget admits the animals the existing herders can already manage (free growth).
                if (herdPeople > Epsilon && herdDevelopment.Count > 0)
                {
                    int supportedTarget = (int)Math.Floor(Math.Min(
                        portfolio.HGrowthCeiling,
                        HerdingTooling.AnimalsManagedBy(w, c, years,
                            herders + additionalHerders)) + Epsilon);
                    int animalsLeft = Math.Max(0, supportedTarget -
                        (int)Math.Floor(Math.Max(0, herdTarget) + Epsilon));
                    foreach (var request in herdDevelopment)
                    {
                        if (animalsLeft <= 0) break;
                        int units = Math.Min(
                            (int)Math.Floor(request.Units + Epsilon),
                            animalsLeft);
                        if (units > 0) grants[request] = units;
                        animalsLeft -= units;
                    }
                }
                return grants;
            }

            (double Field, double Herd) RayCurrentBudgets(
                Dictionary<Request, double> grants)
            {
                double fieldPeople = fieldDevelopment.Sum(q =>
                    grants.TryGetValue(q, out double units)
                        ? units : 0);
                double animals = herdDevelopment.Sum(q =>
                    grants.TryGetValue(q, out double units) ? units : 0);
                int targetHerders = animals > Epsilon
                    ? HerdingTooling.RequiredHerdersFor(w, c, years,
                        Math.Min(portfolio.HGrowthCeiling,
                            herdTarget + animals)) : herders;
                return (fieldPeople,
                    Math.Max(0, targetHerders - herders));
            }

            double RayPricedWork(Dictionary<Request, double> grants)
            {
                double fieldPeople = fieldDevelopment.Sum(q =>
                    grants.TryGetValue(q, out double units)
                        ? units * fieldStrategicPeoplePerUnit[q] : 0);
                var current = RayCurrentBudgets(grants);
                return fieldPeople + current.Herd;
            }

            double RayUsefulFood(Dictionary<Request, double> grants)
            {
                double strategic = strategicDevelopmentFoodGap;
                double plant = plantFeedstockShortfall;
                double animal = animalFeedstockShortfall;
                double substitution = substitutableHumanFood;
                double useful = 0, fieldUseful = 0,
                    finiteFieldAlternative = 0;
                foreach (var request in grants.Keys
                    .OrderByDescending(q => q.PriorityScore)
                    .ThenBy(q => alloc.Requests.IndexOf(q)))
                {
                    double granted = grants[request];
                    double food = granted * request.DevelopmentFoodPerUnit;
                    double common = 0;
                    if (request.DevelopmentPlant)
                    {
                        double typed = Math.Min(plant, food);
                        plant -= typed; food -= typed; common += typed;
                    }
                    if (request.DevelopmentAnimal)
                    {
                        double typed = Math.Min(animal, food);
                        animal -= typed; food -= typed; common += typed;
                    }
                    double service = Math.Min(strategic, food);
                    strategic -= service; food -= service; common += service;
                    double requestSubstitution = request.Units > Epsilon
                        ? request.SubstitutionFood * granted / request.Units : 0;
                    double displaced = Math.Min(substitution,
                        Math.Min(requestSubstitution, food));
                    substitution -= displaced; common += displaced;
                    double policy = Math.Min(granted,
                        request.PolicyDevelopmentUnits) *
                        request.DevelopmentFoodPerUnit;
                    // Sustainable natural growth and a shared food/feedstock
                    // service are alternative authorizations for the same
                    // output, never additive values for one animal.
                    double contribution = Math.Max(common, policy);
                    if (request.DevelopmentPlant)
                    {
                        fieldUseful += contribution;
                        if (fieldFiniteAlternativeFoodPerUnit.TryGetValue(
                            request, out double alternativePerUnit))
                            finiteFieldAlternative += granted *
                                alternativePerUnit;
                    }
                    else useful += contribution;
                }
                // A finite gathering route is an alternative to the whole
                // sequential field prefix, not permission to delete its first
                // bands and build later capital for free. Charge only the
                // bounded food it could supply on the same priced-work horizon.
                return useful + Math.Max(0,
                    fieldUseful - finiteFieldAlternative);
            }

            bool RayFeasible(Dictionary<Request, double> grants)
                => Binding(q => rayRequests.Contains(q)
                    ? grants.TryGetValue(q, out double units) ? units : 0
                    : q.Class == ServiceClass.Development ? 0 : q.Granted) == null;

            DevelopmentChoice EvaluateAbsolute(double fieldBudget,
                double herdBudget)
            {
                fieldBudget = Math.Max(0, fieldBudget);
                herdBudget = Math.Max(0, herdBudget);
                var proposed = RayPrefix(fieldBudget, herdBudget);
                if (!RayFeasible(proposed))
                {
                    double low = 0, high = 1;
                    for (int iteration = 0; iteration < 20; iteration++)
                    {
                        double middle = (low + high) * .5;
                        if (RayFeasible(RayPrefix(fieldBudget * middle,
                            herdBudget * middle))) low = middle;
                        else high = middle;
                    }
                    proposed = RayPrefix(fieldBudget * low,
                        herdBudget * low);
                }
                bool feasible = RayFeasible(proposed);
                var budgets = RayCurrentBudgets(proposed);
                return new DevelopmentChoice(proposed, budgets.Field,
                    budgets.Herd, RayUsefulFood(proposed),
                    RayPricedWork(proposed), feasible);
            }

            // Walk one bounded strategic prefix. At each coarse increment the
            // Elder compares Farmer, Herder and real 50/50 additions from the
            // same already accepted portfolio. Ranking incremental useful
            // service over incremental complete work avoids both the former
            // one-tranche cap and the full-ray average that hid weak marginal
            // grassland behind productive first fields. With a step of
            // max(10, 10% of residents), this performs at most ten rounds and
            // never searches a field x herd Cartesian product.
            var acceptedRay = new Dictionary<Request, double>();
            double acceptedFieldBudget = 0, acceptedHerdBudget = 0;
            double acceptedUseful = 0, acceptedPricedWork = 0;
            if (elderDirective != null)
            {
                // This is the strategic policy seam, separate from the
                // headless-only DevelopmentSearch diagnostic hook. The
                // requested budget selects a physical prefix; Binding and the
                // ordinary compiler retain sole authority over feasibility.
                double requestedPeople = ElderRLContract.Unit(
                    elderDirective.DevelopmentBudget) * residents;
                double fieldShare = ElderRLContract.Unit(
                    elderDirective.FieldDevelopmentShare);
                var selected = EvaluateAbsolute(requestedPeople * fieldShare,
                    requestedPeople * (1 - fieldShare));
                var budgets = RayCurrentBudgets(selected.Grants);
                acceptedRay = new Dictionary<Request, double>(selected.Grants);
                acceptedFieldBudget = budgets.Field;
                acceptedHerdBudget = budgets.Herd;
                acceptedUseful = RayUsefulFood(acceptedRay);
                acceptedPricedWork = RayPricedWork(acceptedRay);
                double acceptedPeople = acceptedFieldBudget +
                    acceptedHerdBudget;
                if (Math.Abs(acceptedPeople - requestedPeople) > Epsilon)
                    elderInterventions?.Add(new ElderIntervention
                    {
                        Kind = ElderInterventionKind.ResourceConstraint,
                        Control = "development-budget",
                        Reason = "physical option, resource or integer-workforce limit",
                        Requested = requestedPeople,
                        Accepted = acceptedPeople
                    });
            }
            else if (developmentSearch != null)
            {
                var context = new DevelopmentSearchContext(residents,
                    portfolio.StrategicLaborStep,
                    fieldDevelopment.AsReadOnly(),
                    herdDevelopment.AsReadOnly(), EvaluateAbsolute);
                var selected = developmentSearch(context) ??
                    EvaluateAbsolute(0, 0);
                bool valid = selected.Feasible &&
                    selected.Grants.All(pair => rayRequests.Contains(pair.Key) &&
                        pair.Value >= -Epsilon &&
                        pair.Value <= pair.Key.Units + Epsilon) &&
                    RayFeasible(selected.Grants);
                if (!valid)
                    throw new InvalidOperationException(
                        "Diagnostic Development search returned an invalid choice.");
                var budgets = RayCurrentBudgets(selected.Grants);
                acceptedRay = new Dictionary<Request, double>(
                    selected.Grants);
                acceptedFieldBudget = budgets.Field;
                acceptedHerdBudget = budgets.Herd;
                acceptedUseful = RayUsefulFood(acceptedRay);
                acceptedPricedWork = RayPricedWork(acceptedRay);
            }
            else
            {
                int strategicRounds = residents > 0
                    ? Math.Max(1, (int)Math.Ceiling((double)residents /
                        portfolio.StrategicLaborStep - Epsilon)) : 0;
                for (int round = 0; round < strategicRounds; round++)
                {
                    double remaining = Math.Max(0, residents -
                        acceptedFieldBudget - acceptedHerdBudget);
                    if (!(remaining > Epsilon)) break;
                    double increment = Math.Min(
                        portfolio.StrategicLaborStep, remaining);
                    var candidates = new List<(string Ray,
                        Dictionary<Request, double> Grants,
                        double FieldBudget, double HerdBudget, double Useful,
                        double PricedWork, double MarginalReturn)>();

                    void ConsiderIncrement(string ray, double addField,
                        double addHerd)
                    {
                        double fieldBudget = acceptedFieldBudget + addField;
                        double herdBudget = acceptedHerdBudget + addHerd;
                        var grants = RayPrefix(fieldBudget, herdBudget);
                        bool changed = grants.Any(pair =>
                            !acceptedRay.TryGetValue(pair.Key,
                                out double prior) ||
                            pair.Value > prior + Epsilon);
                        if (!changed) return;
                        if (!RayFeasible(grants))
                        {
                            Dictionary<Request, double> Interpolate(
                                double share)
                                => RayPrefix(acceptedFieldBudget +
                                        (fieldBudget - acceptedFieldBudget) *
                                            share,
                                    acceptedHerdBudget +
                                        (herdBudget - acceptedHerdBudget) *
                                            share);
                            double low = 0, high = 1;
                            for (int iteration = 0; iteration < 16;
                                iteration++)
                            {
                                double middle = (low + high) * .5;
                                var clipped = Interpolate(middle);
                                if (RayFeasible(clipped)) low = middle;
                                else high = middle;
                            }
                            grants = Interpolate(low);
                            changed = grants.Any(pair =>
                                !acceptedRay.TryGetValue(pair.Key,
                                    out double prior) ||
                                pair.Value > prior + Epsilon);
                            if (!changed || !RayFeasible(grants)) return;
                        }
                        var currentBudgets = RayCurrentBudgets(grants);
                        fieldBudget = currentBudgets.Field;
                        herdBudget = currentBudgets.Herd;
                        double useful = RayUsefulFood(grants);
                        double pricedWork = RayPricedWork(grants);
                        double addedUseful = useful - acceptedUseful;
                        double addedWork = pricedWork - acceptedPricedWork;
                        if (!(addedUseful > Epsilon) || addedWork < -Epsilon)
                            return;
                        double value = addedWork > Epsilon
                            ? addedUseful / (addedWork * years)
                            : double.PositiveInfinity;
                        alloc.DevelopmentAlternatives++;
                        if (ray == "farmers")
                            alloc.FarmerRayReturn = Math.Max(
                                alloc.FarmerRayReturn, value);
                        else if (ray == "herders")
                            alloc.HerderRayReturn = Math.Max(
                                alloc.HerderRayReturn, value);
                        else
                            alloc.BalancedMarginalReturn = Math.Max(
                                alloc.BalancedMarginalReturn, value);
                        candidates.Add((ray, grants, fieldBudget, herdBudget,
                            useful, pricedWork, value));
                    }

                    double fieldIncrement = increment;
                    if (fieldOpportunityCrossingBudget >
                            acceptedFieldBudget + increment + Epsilon)
                        fieldIncrement = Math.Min(remaining,
                            fieldOpportunityCrossingBudget -
                                acceptedFieldBudget);
                    ConsiderIncrement("farmers", fieldIncrement, 0);
                    ConsiderIncrement("herders", 0, increment);
                    ConsiderIncrement("balanced", increment * .5,
                        increment * .5);
                    var selected = candidates
                        .OrderByDescending(candidate =>
                            candidate.MarginalReturn)
                        .ThenByDescending(candidate =>
                            candidate.Useful - acceptedUseful)
                        .ThenBy(candidate => candidate.PricedWork -
                            acceptedPricedWork)
                        .ThenBy(candidate => candidate.Ray,
                            StringComparer.Ordinal)
                        .FirstOrDefault();
                    if (selected.Grants == null) break;
                    acceptedRay = selected.Grants;
                    acceptedFieldBudget = selected.FieldBudget;
                    acceptedHerdBudget = selected.HerdBudget;
                    acceptedUseful = selected.Useful;
                    acceptedPricedWork = selected.PricedWork;
                }
            }
            alloc.SelectedDevelopmentUseful = acceptedUseful;
            alloc.SelectedDevelopmentWork = acceptedPricedWork;
            alloc.SelectedFieldBudget = acceptedFieldBudget;
            alloc.SelectedHerdBudget = acceptedHerdBudget;
            foreach (var pair in acceptedRay)
            {
                developmentRayLimits[pair.Key] = pair.Value;
                if (investmentByRequest != null &&
                    investmentByRequest.TryGetValue(pair.Key,
                    out var investment))
                    investment.SelectedUnits = pair.Value;
            }

            string UnmetCause(string binding)
                => binding == "labor" ? "labor rationing"
                    : binding == "equipment" ? "equipment shortage"
                    : binding != null && binding.StartsWith("material")
                        ? "material shortage" : binding ?? "labor rationing";
            double MaximumDevelopmentGrant(Request request, double wanted,
                out string binding)
            {
                binding = null;
                wanted = Math.Max(0, Math.Min(request.Units, wanted));
                if (!(wanted > Epsilon)) return 0;
                string FullBinding() => Binding(q => q == request ? wanted : q.Granted);
                binding = FullBinding();
                if (binding == null) return wanted;
                if (request.WholeUnits)
                {
                    int low = 0, high = Math.Max(0,
                        (int)Math.Floor(wanted + Epsilon)) + 1;
                    while (low + 1 < high)
                    {
                        int middle = low + (high - low) / 2;
                        if (Binding(q => q == request ? middle : q.Granted) == null)
                            low = middle;
                        else high = middle;
                    }
                    return low;
                }
                double lo = 0, hi = wanted;
                for (int i = 0; i < 40; i++)
                {
                    double middle = (lo + hi) / 2;
                    if (Binding(q => q == request ? middle : q.Granted) == null)
                        lo = middle;
                    else hi = middle;
                }
                return lo;
            }

            double grantedReserveFood = reserveGathering == null ? 0 :
                reserveGathering.Granted * reserveGathering.Bundle.FoodService;
            double remainingCurrentDevelopmentFood = survivalFoodGap + growthFoodGap +
                Math.Max(0, reserveFoodGap - grantedReserveFood);
            // Strategic investment may answer a future production deficit, but
            // it cannot satisfy this turn's food buckets. A granted current
            // gathering route can serve both ledgers; a field or newborn-herd
            // return debits only the continuation ledger.
            double remainingStrategicDevelopmentFood =
                strategicDevelopmentFoodGap;
            alloc.DevelopmentFoodGap = remainingCurrentDevelopmentFood;
            double remainingPlantDevelopment = plantFeedstockShortfall;
            double remainingAnimalDevelopment = animalFeedstockShortfall;
            double remainingSubstitutionFood = substitutableHumanFood;
            var development = alloc.Requests
                .Where(q => q.Class == ServiceClass.Development && q.Bundle != null)
                // A learned directive has already selected a jointly feasible
                // physical field/herd prefix. Reserve that prefix before the
                // ordinary food-for-growth request consumes residual labour;
                // otherwise Option A's return ranking would silently override
                // the action after compilation.
                .OrderBy(q => elderPhysicalDevelopment &&
                    !rayRequests.Contains(q) ? 1 : 0)
                .ThenByDescending(q => elderPhysicalDevelopment ? 0 :
                    q.PriorityScore)
                .ThenBy(q => alloc.Requests.IndexOf(q)).ToList();
            foreach (var request in development)
            {
                bool elderRayRequest = elderPhysicalDevelopment &&
                    rayRequests.Contains(request);
                if (rayRequests.Contains(request))
                {
                    if (!developmentRayLimits.TryGetValue(request,
                            out double rayLimit) || !(rayLimit > Epsilon))
                    {
                        request.Unmet = "lower-return ray";
                        continue;
                    }
                    request.Units = Math.Min(request.Units, rayLimit);
                }
                double typedNeed = request.DevelopmentPlant
                    ? remainingPlantDevelopment : request.DevelopmentAnimal
                        ? remainingAnimalDevelopment : 0;
                double substitutionNeed = Math.Min(remainingSubstitutionFood,
                    request.SubstitutionFood);
                bool currentFoodService = request.Bundle.FoodService > Epsilon;
                double wanted = request.Units;
                if (!elderRayRequest &&
                    request.DevelopmentFoodPerUnit > Epsilon)
                {
                    double usefulFood = (currentFoodService
                            ? remainingCurrentDevelopmentFood
                            : remainingStrategicDevelopmentFood) + typedNeed +
                        substitutionNeed;
                    double usefulUnits = usefulFood /
                        request.DevelopmentFoodPerUnit;
                    wanted = request.WholeUnits
                        ? Math.Ceiling(usefulUnits - Epsilon) : usefulUnits;
                    wanted = Math.Min(request.Units, Math.Max(0, wanted));
                }
                if (!elderRayRequest)
                    wanted = Math.Max(wanted, Math.Min(request.Units,
                        request.PolicyDevelopmentUnits));
                request.Granted = MaximumDevelopmentGrant(request, wanted,
                    out string binding);
                double food = request.Granted *
                    request.DevelopmentFoodPerUnit;
                double typed = Math.Min(typedNeed, food);
                if (request.DevelopmentPlant)
                    remainingPlantDevelopment = Math.Max(0,
                        remainingPlantDevelopment - typed);
                if (request.DevelopmentAnimal)
                    remainingAnimalDevelopment = Math.Max(0,
                        remainingAnimalDevelopment - typed);
                food -= typed;
                double service;
                if (currentFoodService)
                {
                    double currentFood = request.Granted *
                        request.Bundle.FoodService;
                    service = Math.Min(remainingCurrentDevelopmentFood,
                        currentFood);
                    remainingCurrentDevelopmentFood = Math.Max(0,
                        remainingCurrentDevelopmentFood - service);
                    // Current gathering is also a demonstrated recurring route,
                    // so it removes the same amount of strategic need.  Future
                    // investments deliberately do not perform the inverse.
                    remainingStrategicDevelopmentFood = Math.Max(0,
                        remainingStrategicDevelopmentFood - service);
                    food = Math.Max(0, food - service);
                }
                else
                {
                    service = Math.Min(remainingStrategicDevelopmentFood, food);
                    remainingStrategicDevelopmentFood = Math.Max(0,
                        remainingStrategicDevelopmentFood - service);
                    food -= service;
                }
                double substitution = Math.Min(substitutionNeed, food);
                remainingSubstitutionFood = Math.Max(0,
                    remainingSubstitutionFood - substitution);
                if (request.Granted < request.Units - Epsilon)
                    request.Unmet = binding == null
                        ? "service filled" : UnmetCause(binding);
            }
            alloc.DevelopmentFoodGap = remainingCurrentDevelopmentFood;

            // Livestock heads are indivisible. A fractional class-4 grant that
            // cannot raise the integer target is not a meaningful expansion
            // authorization and must not summon an extra whole herder.
            void QuantizeHerdDevelopment()
            {
                if (herdDevelopment.Count == 0) return;
                int retainedTarget = (int)Math.Floor(Math.Max(0, herdTarget) + Epsilon);
                double granted = herdDevelopment.Sum(q => q.Granted);
                int authorizedTarget = (int)Math.Floor(Math.Min(
                    herdTarget + granted,
                    portfolio.HGrowthCeiling) + Epsilon);
                int remaining = Math.Max(0, authorizedTarget - retainedTarget);
                foreach (var request in herdDevelopment
                    .OrderByDescending(q => elderPhysicalDevelopment ? 0 :
                        q.PriorityScore)
                    .ThenBy(q => alloc.Requests.IndexOf(q)))
                {
                    double kept = Math.Min(Math.Floor(request.Granted + Epsilon),
                        remaining);
                    request.Granted = kept;
                    remaining -= (int)kept;
                    if (!(kept > Epsilon) && request.Units > Epsilon)
                        request.Unmet = "integer target";
                }
            }
            QuantizeHerdDevelopment();

            // ---- Integer staffing, compiled per role after aggregation; at most one repair pass ----
            // Pass 2 only recompiles staffing, extraction and ceilings from grants trimmed in pass 1.
            for (int repair = 0; repair < 3; repair++)
            {
                QuantizeHerdDevelopment();
                var total = new Bundle();
                foreach (var q in alloc.Requests) if (q.Bundle != null) total.Add(q.Bundle, q.Granted);
                double approximateDevelopmentHerderYears = herdDevelopment
                    .Where(q => q.Bundle != null && q.Bundle.RoleYears
                        .ContainsKey(WorkforceRoles.Herder))
                    .Sum(q => q.Bundle.RoleYears[WorkforceRoles.Herder] *
                        q.Granted);
                if (approximateDevelopmentHerderYears > 0 &&
                    total.RoleYears.TryGetValue(WorkforceRoles.Herder,
                        out double totalHerderYears))
                    total.RoleYears[WorkforceRoles.Herder] = Math.Max(0,
                        totalHerderYears - approximateDevelopmentHerderYears);
                if (herdDevelopment.Count > 0)
                {
                    double authorized = Math.Min(portfolio.HGrowthCeiling,
                        herdTarget + herdDevelopment.Sum(q => q.Granted));
                    int exactAdded = Math.Max(0,
                        HerdingTooling.RequiredHerdersFor(w, c, years,
                            authorized) - herders);
                    if (exactAdded > 0)
                        total.RoleYears[WorkforceRoles.Herder] =
                            (total.RoleYears.TryGetValue(
                                WorkforceRoles.Herder, out double existing)
                                ? existing : 0) + exactAdded * years;
                }
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
                ProtectedFeedstock(q => q.Granted, out double protectedPlant, out _);
                double plantFromLand = Math.Max(0, total.Plant - protectedPlant -
                    availablePlantInventory);
                double feedstockGatherers = gatherRate > 0
                    ? plantFromLand / gatherRate : 0;
                double needed = staff.Values.Sum() + materialGatherers + feedstockGatherers;
                alloc.FinalCompiledPersons = needed;
                if (needed <= budget + Epsilon || repair >= 1)
                {
                    alloc.Staff.Clear(); alloc.Ceilings.Clear();
                    alloc.PlannedStoneExtraction = Math.Max(0, raw[MaterialId.Stone] - Inventory(MaterialId.Stone));
                    alloc.PlannedClayExtraction = Math.Max(0, raw[MaterialId.Clay] - Inventory(MaterialId.Clay));
                    alloc.PlannedWoodExtraction = Math.Max(0, raw[MaterialId.Wood] - Inventory(MaterialId.Wood));
                    foreach (var p in staff) alloc.Staff[p.Key] = p.Value;
                    alloc.MaterialGatherers = materialGatherers; alloc.FeedstockGatherers = feedstockGatherers; alloc.ToolsMade = toolsMade;
                    foreach (var p in total.Outputs) alloc.Ceilings[p.Key] = p.Value;
                    if (toolsMade > 0) alloc.Ceilings[toolGood] = (alloc.Ceilings.TryGetValue(toolGood, out double t) ? t : 0) + toolsMade;
                    // A final over-budget (rounding after the repair) drops the lowest-class work in stable order.
                    if (repair == 1 && needed > budget + Epsilon)
                    {
                        alloc.TrimPasses++; TrimToBudget(alloc, budget, needed, years);
                        continue; // recompile from the trimmed grants so no trimmed request keeps idle staff
                    }
                    break;
                }
                // Repair integer rounding from the lowest service class. Within
                // Development, remove the lowest-return increment first; never
                // fractionally erase a higher-return whole-animal grant while a
                // lower-return field increment remains.
                alloc.RepairPasses++;
                double excess = needed - budget;
                foreach (var cls in new[] { ServiceClass.Development, ServiceClass.Recovery, ServiceClass.Continuity })
                {
                    var group = alloc.Requests.Where(q => q.Class == cls &&
                        !q.Frozen && q.Bundle != null && q.Granted > Epsilon)
                        .OrderBy(q => cls == ServiceClass.Development &&
                                elderPhysicalDevelopment &&
                                rayRequests.Contains(q)
                            ? 1 : 0)
                        .ThenBy(q => cls == ServiceClass.Development &&
                                !elderPhysicalDevelopment
                            ? q.PriorityScore : 0)
                        .ThenByDescending(q => alloc.Requests.IndexOf(q)).ToList();
                    foreach (var q in group)
                    {
                        if (excess <= Epsilon) break;
                        if (q.WholeUnits && q.HerdDevelopment)
                        {
                            int before = Math.Max(0,
                                HerdingTooling.RequiredHerdersFor(w, c, years,
                                    herdTarget + herdDevelopment.Sum(r =>
                                        r.Granted)) - herders);
                            int wanted = Math.Max(0, before -
                                (int)Math.Ceiling(excess - Epsilon));
                            int otherAnimals = (int)Math.Floor(
                                herdDevelopment.Where(r => r != q).Sum(r =>
                                    r.Granted) + Epsilon);
                            int low = 0, high = (int)Math.Floor(q.Granted +
                                Epsilon) + 1;
                            while (low + 1 < high)
                            {
                                int middle = low + (high - low) / 2;
                                int workers = Math.Max(0,
                                    HerdingTooling.RequiredHerdersFor(w, c,
                                        years, herdTarget + otherAnimals +
                                        middle) - herders);
                                if (workers <= wanted) low = middle;
                                else high = middle;
                            }
                            if (low < q.Granted - Epsilon)
                                q.Unmet = "integer staffing";
                            q.Granted = low;
                            int after = Math.Max(0,
                                HerdingTooling.RequiredHerdersFor(w, c, years,
                                    herdTarget + otherAnimals + low) - herders);
                            excess -= Math.Max(0, before - after);
                            continue;
                        }
                        if (q.WholeUnits) continue;
                        double persons = q.Granted *
                            q.Bundle.RoleYears.Values.Sum() / years;
                        if (!(persons > Epsilon)) continue;
                        double remove = Math.Min(persons,
                            Math.Max(1, Math.Ceiling(excess - Epsilon)));
                        q.Granted *= Math.Max(0, 1 - remove / persons);
                        q.Unmet = "integer staffing";
                        excess -= remove;
                        if (excess <= 0) break;
                    }
                    if (excess <= 0) break;
                }
            }
            var finalRay = rayRequests.Where(q => q.Granted > Epsilon)
                .ToDictionary(q => q, q => q.Granted);
            var finalBudgets = RayCurrentBudgets(finalRay);
            alloc.FinalDevelopmentUseful = RayUsefulFood(finalRay);
            alloc.FinalDevelopmentWork = RayPricedWork(finalRay);
            alloc.FinalFieldBudget = finalBudgets.Field;
            alloc.FinalHerdBudget = finalBudgets.Herd;
            if (captureInvestmentTrace)
            {
                foreach (var pair in investmentByRequest)
                    pair.Value.GrantedUnits = pair.Key.Granted;
            }
            bool grantedFieldDevelopment = fieldDevelopment.Any(q =>
                q.Granted > Epsilon);
            bool grantedHerdDevelopment = herdDevelopment.Any(q =>
                q.Granted > Epsilon);
            alloc.SelectedDevelopmentRay = grantedFieldDevelopment &&
                grantedHerdDevelopment ? "balanced" : grantedFieldDevelopment
                    ? "farmers" : grantedHerdDevelopment ? "herders" : "none";

            // ---- Compile the intent ----
            var intent = e;
            intent.Labor = new EconomicLaborPlan();
            if (herders > 0) intent.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Herder, Population = herders });
            if (farmers > 0) intent.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Farmer, Population = farmers });
            if (fishers > 0) intent.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Fisher, Population = fishers });
            foreach (var p in alloc.Staff.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                var existing = intent.Assignments.FirstOrDefault(a => a.Role == p.Key);
                if (existing != null) existing.Population += p.Value; else intent.Assignments.Add(new PopulationAssignment { Role = p.Key, Population = p.Value });
            }
            intent.Assignments = intent.Assignments.Where(a => a.Population > 0).OrderBy(a => a.Role, StringComparer.Ordinal).ToList();
            int nonGatherers = intent.Assignments.Sum(a => a.Population);
            intent.Labor.MaterialGatherers = Math.Min(Math.Max(0, residents - nonGatherers), alloc.MaterialGatherers);
            intent.Labor.EquipmentTarget = Math.Min(residents, alloc.Staff.Where(p => !WorkforceRoles.Extractors.Contains(p.Key) && p.Key != WorkforceRoles.Herder && p.Key != WorkforceRoles.Farmer).Sum(p => p.Value));
            // Build farmers are the field-expansion staff, on top of the operating farmers.
            intent.Labor.ExpansionFarmers = alloc.Staff.TryGetValue(WorkforceRoles.Farmer, out int builders) ? builders : 0;
            // The herd is held at what the staffed herders manage, within the policy's target; breeding is capped to it.
            if (herdTarget >= 0)
            {
                int staffedHerders = intent.Assignments.Where(a => a.Role == WorkforceRoles.Herder).Sum(a => a.Population);
                double authorized = herdTarget +
                    herdDevelopment.Sum(q => q.Granted);
                authorized = Math.Min(authorized, portfolio.HGrowthCeiling);
                int target = (int)Math.Floor(Math.Min(authorized,
                    HerdingTooling.AnimalsManagedBy(w, c, years, staffedHerders)) + Epsilon);
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
            for (int s = 0; s < ElderModel9Contract.GoodsServices.Length; s++)
                alloc.ServiceGranted[s] = alloc.Requests.Where(q => q.Bundle != null && ElderModel9Contract.ServiceOf(q.Service) == s).Sum(q => Math.Max(0, q.Granted));
            if (!kernelRoutesSet) { alloc.ChosenClothingRoute = clothingRoute; alloc.ChosenShelterRoute = deficitPreferred; }
            alloc.HerdDevelopmentAnimals = herdDevelopment.Sum(q => q.Granted);
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
                case "food room": return b.Plant + b.Animal + b.FoodService > 0;
                case "fabric": return b.Fabric > 0;
                case "housing material": return b.Material > 0;
                case "material Stone": return b.Stone > 0;
                case "material Clay": return b.Clay > 0 || b.Construction > 0;
                case "material Wood": return b.Wood > 0 || b.Construction > 0;
                default: return true;
            }
        }

        // Removes the lowest-class granted work, last request first, until the compiled staffing fits.
        private static void TrimToBudget(Allocation a, double budget, double needed, double years)
        {
            foreach (var q in a.Requests.Where(q => !q.Frozen && q.Bundle != null &&
                q.Granted > Epsilon).OrderByDescending(q => q.Class)
                .ThenBy(q => q.Class == ServiceClass.Development
                    ? q.PriorityScore : double.PositiveInfinity)
                .ThenByDescending(q => a.Requests.IndexOf(q)))
            {
                if (needed <= budget + Epsilon) break;
                // A trimmed request releases the workers it was staffed with (at least one per role), not just one:
                // its grant and ceiling go to zero, so any staff kept for it would be idle.
                foreach (var role in q.Bundle.RoleYears)
                    if (a.Staff.TryGetValue(role.Key, out int s) && s > 0)
                    {
                        int release = Math.Min(s, Math.Max(1, (int)Math.Ceiling(q.Granted * role.Value / Math.Max(Epsilon, years) - Epsilon)));
                        a.Staff[role.Key] = s - release; needed -= release; if (a.Staff[role.Key] == 0) a.Staff.Remove(role.Key);
                    }
                foreach (var g in q.Bundle.Outputs) if (a.Ceilings.ContainsKey(g.Key)) a.Ceilings[g.Key] = Math.Max(0, a.Ceilings[g.Key] - g.Value * q.Granted);
                q.Granted = 0; q.Unmet = "integer staffing";
            }
        }
    }
}
