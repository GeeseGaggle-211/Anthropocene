using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    // One physical description of the local ecology available to managed
    // livestock. Capacities are animal counts for the owned herd's current
    // species mix over the requested horizon. They are physical bounds only;
    // the settlement supervisor separately supplies the labour bound.
    public readonly struct HerdSupportEnvelope
    {
        public readonly double ZeroDisplacementForageCapacity;
        public readonly double AbsoluteForageCapacity;
        public readonly double WaterCapacity;

        public HerdSupportEnvelope(double zeroDisplacementForageCapacity,
            double absoluteForageCapacity, double waterCapacity)
        {
            ZeroDisplacementForageCapacity = Math.Max(0, zeroDisplacementForageCapacity);
            AbsoluteForageCapacity = Math.Max(ZeroDisplacementForageCapacity,
                Math.Max(0, absoluteForageCapacity));
            WaterCapacity = Math.Max(0, waterCapacity);
        }

        public double ZeroDisplacementCapacity
            => Math.Min(ZeroDisplacementForageCapacity, WaterCapacity);
        public double AbsoluteCapacity
            => Math.Min(AbsoluteForageCapacity, WaterCapacity);
    }

    internal readonly struct HerdConsumptionProfile
    {
        public readonly double Animals, FoodPerAnimal, WaterPerAnimal;
        public bool Exists => Animals > 0;

        private HerdConsumptionProfile(double animals, double foodPerAnimal,
            double waterPerAnimal)
        {
            Animals = animals;
            FoodPerAnimal = Math.Max(0, foodPerAnimal);
            WaterPerAnimal = Math.Max(0, waterPerAnimal);
        }

        public static HerdConsumptionProfile Owned(WorldState world, Camp camp)
        {
            double animals = 0, food = 0, water = 0;
            foreach (var herd in world.Herds.Where(h => h.DomesticCamp == camp.Id && h.Animals > 0)
                .OrderBy(h => h.Id))
            {
                var species = world.Config.GetSpecies(herd.Species);
                animals += herd.Animals;
                food += herd.Animals * Math.Max(0, species.FoodConsumption);
                water += herd.Animals * Math.Max(0, species.FreshwaterConsumption);
            }
            return animals > 0
                ? new HerdConsumptionProfile(animals, food / animals, water / animals)
                : new HerdConsumptionProfile(0, 0, 0);
        }

        public static HerdConsumptionProfile Forecast(ManagedFoodForecast forecast)
            => forecast != null && forecast.Animals > 0
                ? new HerdConsumptionProfile(forecast.Animals,
                    forecast.FoodConsumption, forecast.WaterConsumption)
                : new HerdConsumptionProfile(0, 0, 0);

        // Pure biological ceiling for the next turn under the retained
        // breeding policy. Ecology, water and management remain separate
        // bounds in SettlementSiteEvaluation and the allocator; keeping them
        // out of this calculation prevents the current site from suppressing
        // the opportunity represented by a materially better candidate site.
        public static HerdConsumptionProfile BiologicalGrowth(WorldState world,
            Camp camp, double breeding, double years)
        {
            double animals = 0, food = 0, water = 0;
            double policy = Numbers.Clamp(breeding, 0, 1);
            foreach (var herd in world.Herds.Where(h =>
                    h.DomesticCamp == camp.Id && h.Animals > 0)
                .OrderBy(h => h.Id))
            {
                var species = world.Config.GetSpecies(herd.Species);
                double target = (herd.Animals +
                    Math.Max(0, herd.GrowthRemainder)) * Math.Exp(
                        species.GrowthRate * policy * Math.Max(0, years));
                double grown = Math.Max(herd.Animals, Math.Floor(target));
                animals += grown;
                food += grown * Math.Max(0, species.FoodConsumption);
                water += grown * Math.Max(0, species.FreshwaterConsumption);
            }
            return animals > 0
                ? new HerdConsumptionProfile(animals, food / animals,
                    water / animals)
                : new HerdConsumptionProfile(0, 0, 0);
        }
    }

    internal readonly struct LocalHerdSite
    {
        public readonly int Tile;
        public readonly bool Land, WaterSource;
        public readonly double NonhumanFood, HumanFood, Water;

        public LocalHerdSite(int tile, bool land, bool waterSource,
            double nonhumanFood, double humanFood, double water)
        {
            Tile = tile; Land = land; WaterSource = waterSource;
            NonhumanFood = Math.Max(0, nonhumanFood);
            HumanFood = Math.Max(0, humanFood);
            Water = Math.Max(0, water);
        }
    }

    internal sealed class LocalHerdSurvey
    {
        public readonly LocalHerdSite[] Sites;
        public readonly double NonhumanFood, HumanFood, Water;

        public LocalHerdSurvey(LocalHerdSite[] sites)
        {
            Sites = sites ?? Array.Empty<LocalHerdSite>();
            NonhumanFood = Sites.Sum(s => s.NonhumanFood);
            HumanFood = Sites.Sum(s => s.HumanFood);
            Water = Sites.Sum(s => s.Water);
        }
    }

    // The survey is shared by claim construction, planning and movement. It
    // projects latent ecology but never materializes or debits it. On land,
    // human food G is part of biomass B: livestock-first forage is B-G and the
    // absolute ceiling may additionally consume G. Water includes the same
    // bounded horizon recharge proxy used to route actual managed-herd claims.
    internal static class LocalHerdEcology
    {
        internal static LocalHerdSurvey Survey(WorldState world, int center, int radius,
            double durationDays, Func<int, EcologyView> ecology = null,
            ISet<int> visible = null)
        {
            var sites = new List<LocalHerdSite>();
            var exposure = new SeasonExposure(world.Days, Math.Max(0, durationDays),
                world.Config.BaseDays);
            foreach (int tileId in world.Map.Radius(center, Math.Max(0, radius)).OrderBy(id => id))
            {
                if (visible != null && !visible.Contains(tileId)) continue;
                var tile = world.Tiles[tileId];
                if (!tile.TraversableLand && tile.WaterBody != WaterKind.Freshwater) continue;
                var stock = ecology != null ? ecology(tileId)
                    : LazyEcology.ProjectEcology(world, tileId, world.Days);
                bool land = tile.TraversableLand;
                double nonhuman = 0, human = 0;
                if (land)
                {
                    double biomass = Math.Max(0, stock.PotentialFood);
                    human = Numbers.Clamp(stock.Food, 0, biomass);
                    nonhuman = biomass - human;
                }
                bool waterSource = tile.WaterBody != WaterKind.Saltwater &&
                    tile.FreshwaterSource != FreshwaterSource.None;
                double water = 0;
                if (waterSource)
                {
                    double recharge = 0;
                    for (int season = 0; season < 4; season++)
                    {
                        var profile = tile.Seasons[season];
                        recharge += Math.Max(0, profile.MaxFreshwater *
                            profile.WaterRecoveryPerDay) * exposure.At(season);
                    }
                    water = Math.Max(0, stock.Freshwater) + recharge;
                }
                sites.Add(new LocalHerdSite(tileId, land, waterSource,
                    nonhuman, human, water));
            }
            return new LocalHerdSurvey(sites.ToArray());
        }

        internal static HerdSupportEnvelope Support(LocalHerdSurvey survey,
            HerdConsumptionProfile herd, double intervals)
        {
            if (!herd.Exists || !(intervals > 0))
                return new HerdSupportEnvelope(0, 0, 0);
            double foodPerAnimal = herd.FoodPerAnimal * intervals;
            double waterPerAnimal = herd.WaterPerAnimal * intervals;
            double zero = foodPerAnimal > 0 ? survey.NonhumanFood / foodPerAnimal
                : double.PositiveInfinity;
            double absolute = foodPerAnimal > 0
                ? (survey.NonhumanFood + survey.HumanFood) / foodPerAnimal
                : double.PositiveInfinity;
            double water = waterPerAnimal > 0 ? survey.Water / waterPerAnimal
                : double.PositiveInfinity;
            return new HerdSupportEnvelope(zero, absolute, water);
        }
    }

    // Cheap, pure site terms shared by the movement observation and economic
    // planning. This is not a recursive production plan. Its work is bounded
    // by the fixed local footprint (normally seven tiles).
    internal readonly struct SettlementSiteEvaluation
    {
        public readonly int Tile, SurveyedTiles;
        public readonly double HumanFood, HumanFoodAfterAbsoluteHerd;
        public readonly double FixedHousing, RetainedFieldCapital;
        public readonly double RequiredRecurringLabor,
            AbsoluteRecurringLabor;
        public readonly double SustainableHerdCapacity, AbsoluteHerdCapacity;
        public readonly HerdSupportEnvelope Herd;
        internal readonly LocalHerdSurvey Survey;

        private SettlementSiteEvaluation(int tile, int surveyedTiles,
            double humanFood, double humanFoodAfterAbsoluteHerd,
            double fixedHousing, double retainedFieldCapital,
            double requiredRecurringLabor, double sustainableHerdCapacity,
            double absoluteRecurringLabor, double absoluteHerdCapacity,
            HerdSupportEnvelope herd,
            LocalHerdSurvey survey)
        {
            Tile = tile; SurveyedTiles = surveyedTiles;
            HumanFood = Math.Max(0, humanFood);
            HumanFoodAfterAbsoluteHerd = Math.Max(0, humanFoodAfterAbsoluteHerd);
            FixedHousing = Math.Max(0, fixedHousing);
            RetainedFieldCapital = Math.Max(0, retainedFieldCapital);
            RequiredRecurringLabor = Math.Max(0, requiredRecurringLabor);
            AbsoluteRecurringLabor = Math.Max(0, absoluteRecurringLabor);
            SustainableHerdCapacity = Math.Max(0, sustainableHerdCapacity);
            AbsoluteHerdCapacity = Math.Max(SustainableHerdCapacity,
                Math.Max(0, absoluteHerdCapacity));
            Herd = herd; Survey = survey;
        }

        internal static SettlementSiteEvaluation Evaluate(WorldState world,
            Camp camp, int site, int people, HerdConsumptionProfile profile,
            Func<int, EcologyView> ecology = null, ISet<int> visible = null)
        {
            double intervals = world.Config.GenerationIntervals;
            var survey = LocalHerdEcology.Survey(world, site, 1,
                world.Config.TurnDays, ecology, visible);
            var support = LocalHerdEcology.Support(survey, profile, intervals);
            double humanFood = Math.Max(0, camp.Food) + survey.HumanFood;
            // Coastal aquatic food is human-accessible but never livestock
            // forage or freshwater, matching the existing coverage contract.
            foreach (int id in world.Map.Radius(site, 1).OrderBy(id => id))
            {
                if (visible != null && !visible.Contains(id)) continue;
                if (world.Tiles[id].Terrain != Terrain.Shallows) continue;
                var stock = ecology != null ? ecology(id)
                    : LazyEcology.ProjectEcology(world, id, world.Days);
                humanFood += Math.Max(0, stock.Food);
            }
            double absoluteHerd = Math.Min(profile.Animals, support.AbsoluteCapacity);
            double forage = absoluteHerd * profile.FoodPerAnimal * intervals;
            double displacedHuman = Math.Max(0, forage - survey.NonhumanFood);
            double afterHerd = Math.Max(0, humanFood - displacedHuman);
            double fieldCapital = 0;
            foreach (int id in world.Map.Radius(site, 1).OrderBy(id => id))
            {
                if (visible != null && !visible.Contains(id)) continue;
                if (!world.Tiles[id].TraversableLand ||
                    world.Tiles[id].CultivationOperator != camp.Id) continue;
                var stock = ecology != null ? ecology(id)
                    : LazyEcology.ProjectEcology(world, id, world.Days);
                fieldCapital += Math.Max(0, stock.CultivationCapital);
            }
            double fixedHousing = NeolithicProduction.FixedHousingAt(world,
                camp.Id, site, world.Days);
            double years = world.Config.TurnDays / world.Config.DaysPerYear;
            // Movement reads the retained operating policy at the committed
            // boundary.  It does not assume every resident is free for either
            // fields or livestock, nor does it recursively invoke the economic
            // allocator for each candidate site.
            int assignedFarmers = site == camp.Tile
                ? camp.Workforce.Assignments.Where(a =>
                    a.Role == WorkforceRoles.Farmer).Sum(a => a.Population) : 0;
            double fieldLabor = site == camp.Tile
                ? Math.Min(assignedFarmers,
                    CultivationEconomy.MaintenanceFarmersForPlanning(world, camp,
                        years)) : 0;
            int availableHerders = Math.Max(0,
                Math.Max(0, people) - (int)Math.Ceiling(fieldLabor - 1e-9));
            double laborCapacity = HerdingTooling.AnimalsManagedBy(world, camp,
                years, availableHerders);
            double sustainableHerd = Math.Min(support.ZeroDisplacementCapacity,
                laborCapacity);
            double absoluteLaborHerd = Math.Min(support.AbsoluteCapacity,
                laborCapacity);
            absoluteHerd = Math.Min(profile.Animals, absoluteLaborHerd);
            forage = absoluteHerd * profile.FoodPerAnimal * intervals;
            displacedHuman = Math.Max(0, forage - survey.NonhumanFood);
            afterHerd = Math.Max(0, humanFood - displacedHuman);
            double retainedHerd = Math.Min(profile.Animals, sustainableHerd);
            double herdLabor = HerdingTooling.RequiredHerdersFor(world, camp,
                years, retainedHerd);
            double retainedAbsoluteHerd = Math.Min(profile.Animals,
                absoluteLaborHerd);
            double absoluteHerdLabor = HerdingTooling.RequiredHerdersFor(world,
                camp, years, retainedAbsoluteHerd);
            return new SettlementSiteEvaluation(site, survey.Sites.Length,
                humanFood, afterHerd, fixedHousing, fieldCapital,
                fieldLabor + herdLabor, sustainableHerd,
                fieldLabor + absoluteHerdLabor, absoluteLaborHerd,
                support, survey);
        }
    }

    internal sealed class RetainedSettlementPortfolio
    {
        public int FieldFarmers, HerdHerders;
        public double HCurrent, HSustainable, HAbsolute, HGrown, HRetained,
            HGrowthCeiling, RetainedFoodSupply, ManagedFoodPerAnimal,
            SlaughterFood, SelectedFieldOperation, SelectedPortfolioValue,
            SelectedPortfolioUnmet, SelectedFutureFieldMaintenance,
            SelectedFutureFieldHarvestWork;
        public int AlternativesEvaluated;
        public int StrategicLaborStep;
        public ManagedFoodForecast GrownForecast;
        public SettlementSiteEvaluation Site;
    }

    // One pure definition of the food services that may justify productive
    // capital at the next boundary. Consumption and maximum-growth food recur;
    // reserve refill is a one-time current service and only the retained stock
    // target receives terminal risk value. Future receipts never satisfy the
    // current buckets exposed separately here.
    internal readonly struct SettlementContinuationDemand
    {
        internal readonly double ConsumptionIncome, ReserveTarget, ReserveFill,
            GrowthFood, RecurringFoodTarget, TerminalFoodTarget;

        private SettlementContinuationDemand(double consumptionIncome,
            double reserveTarget, double reserveFill, double growthFood,
            double recurringFoodTarget, double terminalFoodTarget)
        {
            ConsumptionIncome = consumptionIncome;
            ReserveTarget = reserveTarget;
            ReserveFill = reserveFill;
            GrowthFood = growthFood;
            RecurringFoodTarget = recurringFoodTarget;
            TerminalFoodTarget = terminalFoodTarget;
        }

        internal static SettlementContinuationDemand For(WorldState world,
            Camp camp, CampProductionDemand demand)
        {
            double years = world.Config.TurnDays / world.Config.DaysPerYear;
            TurnResolver.FoodBucketTargets(world, camp,
                world.Config.GenerationIntervals, years, demand.Meals,
                camp.Food, out double consumption, out double reserveTarget,
                out double reserveFill, out double growthFood);
            double recurring = Math.Max(0, demand.Meals + growthFood);
            return new SettlementContinuationDemand(consumption, reserveTarget,
                reserveFill, growthFood, recurring,
                recurring + Math.Max(0, reserveTarget));
        }
    }

    // The settlement-level supervisor selects the retained capital portfolio;
    // DemandControl then supplies its recurring work. Fields and livestock are
    // alternatives in one bounded physical portfolio: neither is frozen before
    // the other is evaluated. Field-operation and herd-retention chains are
    // merged by comparable finite-horizon return.  There is no population x
    // field x herd product: the plan visits at most F + H prefix alternatives.
    internal static class SettlementPortfolioSupervisor
    {
        private const double Epsilon = 1e-9;

        private sealed class FieldOption
        {
            internal int Farmers;
            internal double Operation, CurrentLand, FutureLand,
                FutureMaintenance, FutureHarvestProductivity, Residual,
                RetainedCapitalValue, UncreditedContinuationValue;
            internal double MergeValue;
        }

        private sealed class HerdOption
        {
            internal int Herders;
            internal double Target, CurrentFood, SlaughterFood,
                FutureNetFood, ResidualFood, MergeValue;
        }

        private readonly struct Candidate
        {
            internal readonly FieldOption Field;
            internal readonly HerdOption Herd;
            internal readonly int Gatherers;
            internal readonly double Food, Unmet, Value, Retention;

            internal Candidate(FieldOption field, HerdOption herd,
                int gatherers, double food, double unmet, double value,
                double retention)
            {
                Field = field; Herd = herd; Gatherers = gatherers;
                Food = food; Unmet = unmet; Value = value;
                Retention = retention;
            }

            internal int Labor => Field.Farmers + Herd.Herders + Gatherers;
        }

        internal static RetainedSettlementPortfolio Select(WorldState world,
            Camp camp, CampProductionDemand demand, EconomicIntent settings,
            SettlementSiteEvaluation site)
        {
            var cfg = world.Config;
            double years = cfg.TurnDays / cfg.DaysPerYear;
            double intervals = cfg.GenerationIntervals;
            int residents = Math.Max(0, camp.Population);
            int strategicLaborStep = Math.Max(10,
                (int)Math.Ceiling(residents * .10));
            int assignedFarmers = camp.Workforce?.Assignments?
                .Where(a => a.Role == WorkforceRoles.Farmer)
                .Sum(a => a.Population) ?? 0;
            int assignedHerders = camp.Workforce?.Assignments?
                .Where(a => a.Role == WorkforceRoles.Herder)
                .Sum(a => a.Population) ?? 0;
            double farmRate = demand.HarvestProductivity * cfg.GatherPerPerson * intervals;
            double gatherRate = cfg.GatherPerPerson * intervals;
            double annualGatherRate = cfg.GatherPerPerson * cfg.DaysPerYear /
                cfg.BaseDays;
            var continuation = SettlementContinuationDemand.For(world, camp,
                demand);
            double survivalFood = continuation.ConsumptionIncome;

            var profile = HerdConsumptionProfile.Owned(world, camp);
            var grown = profile.Exists
                ? TurnResolver.ForecastGrownManagedAnimalFood(world, camp,
                    settings.ManagedBreedingIntensity, intervals, years, camp.Tile)
                : new ManagedFoodForecast();
            var biologicalGrowth = profile.Exists
                ? HerdConsumptionProfile.BiologicalGrowth(world, camp,
                    settings.ManagedBreedingIntensity, years)
                : profile;
            var result = new RetainedSettlementPortfolio
            {
                HCurrent = profile.Animals,
                HAbsolute = profile.Exists ? site.Herd.AbsoluteCapacity : 0,
                HGrown = biologicalGrowth.Exists
                    ? biologicalGrowth.Animals : profile.Animals,
                GrownForecast = grown, StrategicLaborStep = strategicLaborStep,
                Site = site
            };

            // All opening animals graze before controlled slaughter.  One
            // resolver-shaped forecast supplies that invariant ecological
            // pressure.  Candidate management changes only the management
            // factor, which is analytic and uses the same tooling helper.
            var currentHerd = profile.Exists
                ? TurnResolver.ForecastManagedAnimalFood(world, camp, residents,
                    intervals, years, camp.Tile)
                : new ManagedFoodForecast();
            double managementPower = Math.Pow(Math.Max(Epsilon,
                currentHerd.ManagementFulfillment), cfg.AnimalManagementExponent);
            double ecologyFoodAtFullManagement = profile.Exists &&
                managementPower > Epsilon
                ? currentHerd.AnimalFood / managementPower : 0;
            double managedFoodPerAnimal = profile.Animals > 0
                ? ecologyFoodAtFullManagement / profile.Animals : 0;
            result.ManagedFoodPerAnimal = managedFoodPerAnimal;
            double currentHumanDisplacement = currentHerd.HumanFoodDisplaced;

            // Actual field-operation fractions use the uncapped maintenance
            // requirement and the same finite Farm Tool coverage as resolution.
            double rawMaintenance = demand.RetainedFieldCapital
                ? CultivationEconomy.MaintenanceWorkerRequirement(world, camp) : 0;
            double toolWorkers = years > 0 ? Math.Max(0,
                camp.Inventory.FarmTools) * cfg.Neolithic.FarmToolFarmerYearsPerUnit /
                years : 0;
            double fieldEfficiency = CultivationEconomy.FarmToolFieldWorkEfficiency(
                cfg.Neolithic);
            double WorkersFor(double fraction)
            {
                double required = rawMaintenance * Numbers.Clamp(fraction, 0, 1);
                double covered = Math.Min(toolWorkers, required / fieldEfficiency);
                return covered + Math.Max(0, required - covered * fieldEfficiency);
            }
            double OperationFor(int farmers)
            {
                double covered = Math.Min(farmers,
                    Math.Min(toolWorkers, rawMaintenance / fieldEfficiency));
                return rawMaintenance > 0 ? Numbers.Clamp((covered * fieldEfficiency +
                    Math.Max(0, farmers - covered)) / rawMaintenance, 0, 1) : 0;
            }
            int maximumFarmers = rawMaintenance > Epsilon
                ? Math.Min(residents, (int)Math.Ceiling(WorkersFor(1) - Epsilon)) : 0;
            var farmerBreaks = new SortedSet<int> { 0, maximumFarmers };
            if (maximumFarmers > 0)
            {
                farmerBreaks.Add(Math.Min(maximumFarmers,
                    Math.Max(0, assignedFarmers)));
                // Field-operation alternatives depend only on physical land,
                // tools and food service.  A recipe exposed by a secondary
                // capability must not move this breakpoint when it has no
                // demanded use.
                farmerBreaks.Add(Math.Min(maximumFarmers,
                    Math.Max(0, (int)Math.Ceiling(Math.Min(site.Survey.HumanFood,
                        survivalFood) / Math.Max(Epsilon, farmRate) - Epsilon))));
                double toolKink = Math.Min(toolWorkers,
                    rawMaintenance / Math.Max(Epsilon, fieldEfficiency));
                int beforeKink = Math.Min(maximumFarmers, Math.Max(0,
                    (int)Math.Floor(toolKink + Epsilon)));
                farmerBreaks.Add(beforeKink);
                farmerBreaks.Add(Math.Min(maximumFarmers, beforeKink + 1));
                for (int workers = strategicLaborStep;
                    workers < maximumFarmers; workers += strategicLaborStep)
                    farmerBreaks.Add(workers);
            }

            var herderBreaks = new SortedSet<int> { 0, residents };
            void AddRequirement(double animals)
            {
                int requirement = Math.Min(residents,
                    HerdingTooling.RequiredHerdersFor(world, camp, years,
                        Math.Max(0, animals)));
                herderBreaks.Add(requirement);
                // A requirement is a discontinuity in the animal target. Its
                // immediate lower neighbour is a distinct liquidation/growth
                // portfolio and costs one bounded candidate, independent of
                // population. Coarse strategic steps must not erase it.
                if (requirement > 0) herderBreaks.Add(requirement - 1);
            }
            if (profile.Exists)
            {
                AddRequirement(profile.Animals);
                AddRequirement(site.Herd.ZeroDisplacementCapacity);
                AddRequirement(site.Herd.AbsoluteCapacity);
                AddRequirement(result.HGrown);
            }
            int toolBreak = (int)Math.Min(residents,
                Math.Max(0, camp.Inventory.HerdingTools) *
                cfg.Neolithic.HerdingToolCoverageYearsPerUnit / Math.Max(1e-12, years));
            herderBreaks.Add(toolBreak);
            herderBreaks.Add(Math.Min(residents, toolBreak + 1));
            herderBreaks.Add(Math.Min(residents,
                Math.Max(0, assignedHerders)));
            for (int workers = strategicLaborStep;
                workers < residents; workers += strategicLaborStep)
                herderBreaks.Add(workers);

            // Cache opening projections and the operation-independent local
            // land forecasts once.  Every field alternative uses the same
            // authoritative stock kernel as actual claims.
            var openings = demand.Eligible.Where(id => world.Tiles[id].Land)
                .OrderBy(id => id).ToDictionary(id => id,
                    id => LazyEcology.ProjectEcology(world, id, world.Days));
            var fixedLand = new Dictionary<(int, long), (double current,
                double future, double residual, double harvestWork)>();
            bool cropRate = cfg.CropHarvestingOnly && cfg.FarmerProductivity > 1;
            int season = (int)Math.Floor(world.Days / cfg.BaseDays) % 4;
            var footprint = new HashSet<int>(demand.Footprint);
            var fields = new List<FieldOption>();
            double futureYears = Math.Max(0,
                cfg.Neolithic.PlanningHorizonYears - years);
            double discount = cfg.Neolithic.DiscountRatePerYear;
            double futureLife = Math.Exp(-discount * years) *
                HerdLabour.DiscountedYears(discount, futureYears);
            double terminalDiscount = Math.Exp(-discount *
                cfg.Neolithic.PlanningHorizonYears) *
                cfg.Neolithic.FoodRiskWeight;
            foreach (int farmers in farmerBreaks.Where(f => f >= 0 &&
                f <= residents).OrderBy(f => f))
            {
                double operation = OperationFor(farmers);
                double currentLand = 0, futureLand = 0, residual = 0,
                    futureMaintenance = 0, futureFieldFood = 0,
                    futureFieldWork = 0, retainedCapitalWork = 0;
                foreach (var pair in openings)
                {
                    int id = pair.Key; var opening = pair.Value;
                    var tile = world.Tiles[id];
                    bool owned = demand.Operated.Contains(id);
                    double start = Math.Max(0, opening.CultivationCapital);
                    double op = owned ? operation : tile.CultivationOperatingFraction;
                    long keyBits = owned && start > Epsilon
                        ? BitConverter.DoubleToInt64Bits(op) : long.MinValue;
                    var key = (id, keyBits);
                    if (!fixedLand.TryGetValue(key, out var forecast))
                    {
                        double Mean(double duration)
                        {
                            double rate = Math.Log(2) /
                                cfg.Neolithic.DecayHalfLifeYears * (1 - op);
                            return rate * duration > 1e-10
                                ? start * (1 - Math.Exp(-rate * duration)) /
                                    (rate * duration) : start;
                        }
                        double end = CultivationEconomy.Decay(cfg, start, op, years);
                        double terminal = CultivationEconomy.Decay(cfg, start, op,
                            Math.Max(years, cfg.Neolithic.PlanningHorizonYears));
                        double request = Math.Max(0, opening.Food);
                        var current = FoodHorizonForecast.Tile(world, id, world.Days,
                            cfg.TurnDays, request > 0 ? new[] { new Demand
                            {
                                Tile = id, Entity = camp.Id, Kind = "Gathering",
                                Food = request
                            }} : Array.Empty<Demand>(), Mean(years), end, op,
                            false, opening);
                        var empty = opening;
                        empty.Food = 0;
                        empty.CultivationCapital = Mean(Math.Max(years,
                            cfg.Neolithic.PlanningHorizonYears));
                        empty.PotentialFood = Math.Max(0,
                            current.ClosingBiomass - current.ClosingFood);
                        var refill = FoodHorizonForecast.Tile(world, id, world.Days,
                            cfg.TurnDays, null, empty.CultivationCapital,
                            terminal, op, false, empty);
                        double harvestWork = 0;
                        if (cropRate && footprint.Contains(id))
                        {
                            var capacity = new EcologyCapacity(world, tile, empty,
                                season);
                            double crop = tile.CultivationOperator >= 0 &&
                                tile.CultivationOperator != camp.Id ? 0 :
                                Math.Max(0, capacity.Food - capacity.WildFood);
                            double share = capacity.Food > 0
                                ? crop / capacity.Food : 0;
                            harvestWork = refill.ClosingFood * (1 - share +
                                share / cfg.FarmerProductivity);
                        }
                        forecast = (current.HumanFoodReceipts,
                            refill.ClosingFood, refill.ClosingFood, harvestWork);
                        fixedLand[key] = forecast;
                    }
                    currentLand += forecast.current;
                    futureLand += forecast.future;
                    residual += forecast.residual;
                    if (cropRate && footprint.Contains(id))
                    {
                        futureFieldFood += forecast.future;
                        futureFieldWork += forecast.harvestWork;
                    }
                    if (owned && rawMaintenance > Epsilon)
                    {
                        CultivationEconomy.Factors(cfg, tile, out double build,
                            out double maintenance);
                        double terminal = CultivationEconomy.Decay(cfg, start, op,
                            Math.Max(years, cfg.Neolithic.PlanningHorizonYears));
                        futureMaintenance += cfg.Neolithic
                            .MaintenanceWorkersPerTileE(cfg.MaintenanceCoefficient) *
                            maintenance * op * .5 *
                            (CultivationEconomy.EffortForCapital(cfg, start) +
                             CultivationEconomy.EffortForCapital(cfg, terminal));
                        // Retained field capital is a real option: if it is
                        // allowed to decay, recreating the difference later
                        // consumes the same clearing/build effort charged by
                        // field expansion.  Price that avoided rebuild once at
                        // the existing discounted terminal weight.  This is a
                        // sunk-capital term, not crop output or a technology
                        // preference.
                        double abandoned = CultivationEconomy.Decay(cfg, start,
                            0, Math.Max(years,
                                cfg.Neolithic.PlanningHorizonYears));
                        retainedCapitalWork += cfg.Neolithic
                            .BuildWorkerYearsPerTileE * build *
                            CultivationEconomy.AdditionalEffort(cfg, abandoned,
                                terminal);
                    }
                }
                double futureProductivity = !demand.CultivationEnabled ? 1 :
                    !cropRate ? cfg.FarmerProductivity : futureFieldWork > 0
                        ? futureFieldFood / futureFieldWork : 1;
                var option = new FieldOption
                {
                    Farmers = farmers, Operation = operation,
                    CurrentLand = currentLand, FutureLand = futureLand,
                    FutureMaintenance = futureMaintenance,
                    FutureHarvestProductivity = futureProductivity,
                    Residual = residual,
                    RetainedCapitalValue = terminalDiscount *
                        retainedCapitalWork * annualGatherRate
                };
                double annualFutureLand = futureLand /
                    Math.Max(Epsilon, years);
                double futureHarvest = Math.Min(annualFutureLand,
                    futureMaintenance * annualGatherRate *
                    Math.Max(1, futureProductivity));
                double futureWorkers = futureMaintenance +
                    Math.Max(0, annualFutureLand - futureHarvest) /
                    Math.Max(Epsilon, annualGatherRate);
                option.UncreditedContinuationValue = futureLife *
                    (annualFutureLand - futureWorkers * annualGatherRate) +
                    terminalDiscount * Math.Min(residual,
                        continuation.TerminalFoodTarget);
                fields.Add(option);
            }

            // Reconstruction cost alone cannot make unproductive land worth
            // operating.  The retained-capital option is useful only up to the
            // demonstrated continuation surplus over abandonment; increasing a
            // build coefficient therefore cannot rescue a worthless field.
            double abandonedContinuation = fields[0].UncreditedContinuationValue;
            foreach (var field in fields)
            {
                double usefulContinuation = Math.Max(0,
                    field.UncreditedContinuationValue - abandonedContinuation);
                field.RetainedCapitalValue = Math.Min(
                    field.RetainedCapitalValue, usefulContinuation);
                field.MergeValue = field.UncreditedContinuationValue +
                    field.RetainedCapitalValue;
            }

            var ownedHerds = world.Herds.Where(h => h.DomesticCamp == camp.Id &&
                h.Animals > 0).OrderBy(h => h.Id).ToArray();
            var liquidation = new HerdLabour.Liquidation(
                ownedHerds.Select(h => h.Animals).ToArray(),
                ownedHerds.Select(h => HerdLabour.CarcassFood(world, h)).ToArray());
            var herdOptions = new List<HerdOption>();
            foreach (int proposed in herderBreaks.Where(h => h >= 0 &&
                h <= residents).OrderBy(h => h))
            {
                double target = profile.Exists ? Math.Min(profile.Animals,
                    Math.Min(site.Herd.AbsoluteCapacity,
                        HerdingTooling.AnimalsManagedBy(world, camp, years,
                            proposed))) : 0;
                int herders = profile.Exists
                    ? HerdingTooling.RequiredHerdersFor(world, camp, years,
                        target) : 0;
                if (herdOptions.Count > 0 && herders == herdOptions.Last().Herders &&
                    Math.Abs(target - herdOptions.Last().Target) <= Epsilon) continue;
                double fulfillment = profile.Exists
                    ? HerdingTooling.Plan(world, camp, years, herders,
                        profile.Animals).Fulfillment : 1;
                double milk = ecologyFoodAtFullManagement * Math.Pow(
                    Numbers.Clamp(fulfillment, 0, 1),
                    cfg.AnimalManagementExponent);
                int killed = Math.Max(0, (int)Math.Floor(profile.Animals -
                    target + Epsilon));
                double slaughter = liquidation.Food(killed);
                double displacement = Math.Max(0, target *
                    profile.FoodPerAnimal * intervals - site.Survey.NonhumanFood);
                double futureNet = target * managedFoodPerAnimal - displacement;
                double residualFood = liquidation.Food((int)Math.Floor(target +
                    Epsilon));
                var option = new HerdOption
                {
                    Herders = herders, Target = target, CurrentFood = milk,
                    SlaughterFood = slaughter, FutureNetFood = futureNet,
                    ResidualFood = residualFood
                };
                option.MergeValue = futureLife *
                    (futureNet / Math.Max(Epsilon, years) -
                     herders * annualGatherRate) +
                    terminalDiscount * residualFood;
                herdOptions.Add(option);
            }
            if (herdOptions.Count == 0) herdOptions.Add(new HerdOption());

            int retainedField = 0;
            for (int index = 1; index < fields.Count; index++)
            {
                double marginal = fields[index].MergeValue -
                    fields[index - 1].MergeValue;
                if (marginal >= -Epsilon) retainedField = index;
                else break;
            }
            double aquatic = Math.Max(0, demand.ForageSupply - demand.PlantSupply);
            double ContinuationValue(FieldOption field, HerdOption herd)
            {
                double futureDisplaced = Math.Max(0, herd.Target *
                    profile.FoodPerAnimal * intervals - site.Survey.NonhumanFood);
                double futureLand = Math.Max(0,
                    field.FutureLand - futureDisplaced);
                double futureHuman = futureLand + aquatic;
                double futureAnimal = herd.Target * managedFoodPerAnimal;
                double futureNeed = Math.Max(0,
                    continuation.RecurringFoodTarget - futureAnimal);
                double futureHarvest = Math.Min(futureNeed,
                    Math.Min(futureLand, field.FutureMaintenance *
                        annualGatherRate * Math.Max(1,
                            field.FutureHarvestProductivity) * years));
                double futureGather = Math.Min(Math.Max(0,
                    futureHuman - futureHarvest), Math.Max(0,
                        futureNeed - futureHarvest));
                double futureGatherers = futureGather /
                    Math.Max(Epsilon, gatherRate);
                double futureUseful = Math.Min(
                    continuation.RecurringFoodTarget,
                    futureAnimal + futureHarvest + futureGather);
                double futureWorkers = field.FutureMaintenance + herd.Herders +
                    futureGatherers;
                // Retained food stock is bounded by the continuation service;
                // the herd's liquidation value is productive-capital option
                // value, analogous to the separately priced avoided field
                // rebuild. Folding it into the food-stock cap makes an
                // established herd worthless as soon as fields fill that cap
                // and creates an agricultural tie-break by construction.
                double terminalUseful = Math.Min(
                    continuation.TerminalFoodTarget, field.Residual) +
                    herd.ResidualFood;
                return futureLife * (futureUseful /
                        Math.Max(Epsilon, years) -
                    futureWorkers * annualGatherRate) +
                    terminalDiscount * terminalUseful;
            }
            Candidate Evaluate(FieldOption field, HerdOption herd)
            {
                if (field.Farmers + herd.Herders > residents)
                    return new Candidate(field, herd, 0, 0,
                        survivalFood + field.Farmers + herd.Herders - residents,
                        double.NegativeInfinity, 0);
                double land = Math.Max(0,
                    field.CurrentLand - currentHumanDisplacement);
                double human = land + aquatic;
                double target = Math.Max(0, survivalFood - herd.CurrentFood -
                    herd.SlaughterFood);
                double harvest = Math.Min(target,
                    Math.Min(land, field.Farmers * farmRate));
                int wantedGatherers = gatherRate > 0
                    ? (int)Math.Ceiling(Math.Max(0, Math.Min(human, target) -
                        harvest) / gatherRate - Epsilon) : 0;
                int gatherers = Math.Min(Math.Max(0,
                    residents - field.Farmers - herd.Herders),
                    Math.Max(0, wantedGatherers));
                double gathered = Math.Min(Math.Max(0, human - harvest),
                    gatherers * gatherRate);
                double food = herd.CurrentFood + herd.SlaughterFood + harvest +
                    gathered;

                double currentUseful = Math.Min(survivalFood, food);
                // Current work has no invented shadow price here: after Survival
                // is feasible, the Elder may have no competing current service.
                // Recurring future work still pays the gathering opportunity
                // cost over the common finite horizon.
                double value = currentUseful + ContinuationValue(field, herd) +
                    field.RetainedCapitalValue;
                double retention = field.Operation + (profile.Animals > 0
                    ? herd.Target / profile.Animals : 0);
                return new Candidate(field, herd, gatherers, food,
                    Math.Max(0, survivalFood - food), value, retention);
            }

            Candidate best = Evaluate(fields[0], herdOptions[0]);
            bool Better(Candidate candidate, Candidate incumbent)
            {
                if (candidate.Unmet < incumbent.Unmet - Epsilon) return true;
                if (candidate.Unmet > incumbent.Unmet + Epsilon) return false;
                if (candidate.Value > incumbent.Value + Epsilon) return true;
                if (candidate.Value < incumbent.Value - Epsilon) return false;
                if (candidate.Retention > incumbent.Retention + Epsilon) return true;
                if (candidate.Retention < incumbent.Retention - Epsilon) return false;
                if (candidate.Labor != incumbent.Labor)
                    return candidate.Labor < incumbent.Labor;
                int candidateChange = Math.Abs(candidate.Field.Farmers -
                        assignedFarmers) +
                    Math.Abs(candidate.Herd.Herders - assignedHerders);
                int incumbentChange = Math.Abs(incumbent.Field.Farmers -
                        assignedFarmers) +
                    Math.Abs(incumbent.Herd.Herders - assignedHerders);
                if (candidateChange != incumbentChange)
                    return candidateChange < incumbentChange;
                // An exact economic tie preserves the less committed physical
                // portfolio.  The former Farmers-first tie-break made a newly
                // exposed cultivation recipe a strategic preference even when
                // its measured return was identical.
                if (candidate.Field.Farmers != incumbent.Field.Farmers)
                    return candidate.Field.Farmers < incumbent.Field.Farmers;
                return candidate.Herd.Target < incumbent.Herd.Target - Epsilon;
            }

            // Compare the next marginal Farmer and Herder portfolio increments
            // from one accepted prefix, and also sample pure and equal-work
            // 50/50 rays. The prefix admits asymmetric mixed economies (for
            // example, a small complementary herd beside substantial retained
            // fields) that three origin-only rays cannot represent. Coarse
            // budgets are max(10 people, 10% of residents), while exact tool,
            // maintenance, herd, water and forage kinks remain in the option
            // chains. This is O(F + H + 10), never an F x H search.
            var evaluated = new HashSet<(int field, int herd)>();
            result.AlternativesEvaluated = 0;
            void Consider(int fieldIndex, int herdIndex)
            {
                fieldIndex = Math.Max(0, Math.Min(fields.Count - 1, fieldIndex));
                herdIndex = Math.Max(0,
                    Math.Min(herdOptions.Count - 1, herdIndex));
                if (!evaluated.Add((fieldIndex, herdIndex))) return;
                var candidate = Evaluate(fields[fieldIndex],
                    herdOptions[herdIndex]);
                result.AlternativesEvaluated++;
                if (Better(candidate, best)) best = candidate;
            }
            int marginalField = 0, marginalHerd = 0;
            Consider(marginalField, marginalHerd);
            while (marginalField + 1 < fields.Count ||
                marginalHerd + 1 < herdOptions.Count)
            {
                double fieldMarginal = marginalField + 1 < fields.Count
                    ? (fields[marginalField + 1].MergeValue -
                        fields[marginalField].MergeValue) /
                      Math.Max(Epsilon,
                          fields[marginalField + 1].Farmers -
                          fields[marginalField].Farmers)
                    : double.NegativeInfinity;
                double herdMarginal = marginalHerd + 1 < herdOptions.Count
                    ? (herdOptions[marginalHerd + 1].MergeValue -
                        herdOptions[marginalHerd].MergeValue) /
                      Math.Max(Epsilon,
                          herdOptions[marginalHerd + 1].Herders -
                          herdOptions[marginalHerd].Herders)
                    : double.NegativeInfinity;
                if (fieldMarginal >= herdMarginal) marginalField++;
                else marginalHerd++;
                Consider(marginalField, marginalHerd);
            }
            for (int index = 0; index < fields.Count; index++)
                Consider(index, 0);
            for (int index = 0; index < herdOptions.Count; index++)
                Consider(0, index);
            for (int total = strategicLaborStep; total < residents;
                total += strategicLaborStep)
            {
                int channelBudget = total / 2;
                int fieldIndex = fields.FindLastIndex(f =>
                    f.Farmers <= channelBudget);
                int herdIndex = herdOptions.FindLastIndex(h =>
                    h.Herders <= channelBudget);
                Consider(fieldIndex, herdIndex);
            }
            if (residents > 0)
            {
                int channelBudget = residents / 2;
                Consider(fields.FindLastIndex(f => f.Farmers <= channelBudget),
                    herdOptions.FindLastIndex(h =>
                        h.Herders <= channelBudget));
            }

            // Always price the nearest persisted workforce mix.  This is an
            // inertia point, not a technology identity, and keeps an exact tie
            // from manufacturing a switch between productive channels.
            Consider(fields.FindLastIndex(f =>
                    f.Farmers <= Math.Max(0, assignedFarmers)),
                herdOptions.FindLastIndex(h =>
                    h.Herders <= Math.Max(0, assignedHerders)));

            // Existing field capital whose own finite continuation is
            // non-negative is a retained-capital portfolio, not a fresh
            // Agriculture commitment.  Give that physical portfolio a bounded
            // one-dimensional herd scan; distress can still beat it through the
            // hard Survival comparison.  This is independent of technology
            // order and does not form a field x herd search.
            if (retainedField > 0)
            {
                for (int index = 0; index < herdOptions.Count; index++)
                    Consider(retainedField, index);
            }

            // H_sustainable is still the zero-displacement ceiling under the
            // selected human portfolio. It is not the retained target and does
            // not reserve its prospective herders ahead of allocation.
            double LaborCapacity(FieldOption field, double ecologicalCapacity,
                bool displacesHuman)
            {
                double bestCapacity = 0;
                int maximumHerders = Math.Max(0,
                    residents - field.Farmers);
                // Strategic breakpoints are deliberately coarse, but this
                // method exports a physical/labour ceiling. Always include the
                // exact residual-labour endpoint (and its integer neighbour),
                // otherwise a step of ten can turn eight genuinely available
                // herders into a hard four-herder growth cap.
                var capacityBreaks = new SortedSet<int>(herderBreaks)
                    { maximumHerders };
                if (maximumHerders > 0)
                    capacityBreaks.Add(maximumHerders - 1);
                foreach (int herders in capacityBreaks.Where(h => h >= 0 &&
                    h <= maximumHerders))
                {
                    double capacity = Math.Min(ecologicalCapacity,
                        HerdingTooling.AnimalsManagedBy(world, camp, years, herders));
                    double animalFood = capacity * managedFoodPerAnimal;
                    double displaced = displacesHuman
                        ? Math.Max(0, capacity * profile.FoodPerAnimal * intervals -
                            site.Survey.NonhumanFood) : 0;
                    double humanAvailable = Math.Max(0,
                        field.CurrentLand + aquatic - displaced);
                    double plantAvailable = Math.Max(0,
                        field.CurrentLand - displaced);
                    double humanTarget = Math.Min(humanAvailable,
                        Math.Max(0, survivalFood - animalFood));
                    double harvest = Math.Min(humanTarget,
                        Math.Min(plantAvailable,
                            field.Farmers * farmRate));
                    int gatherers = gatherRate > 0
                        ? (int)Math.Ceiling(Math.Max(0, humanTarget - harvest) /
                            gatherRate - Epsilon) : 0;
                    if (field.Farmers + herders + gatherers <= residents)
                        bestCapacity = Math.Max(bestCapacity, capacity);
                }
                return Math.Max(0, bestCapacity);
            }

            double sustainable = profile.Exists
                ? LaborCapacity(best.Field,
                    site.Herd.ZeroDisplacementCapacity, false) : 0;

            // Initial retained-capital policy: when the selected human
            // portfolio can meet the same Survival service while managing the
            // current herd inside its zero-displacement envelope, evaluate and
            // select that complete portfolio as Continuity.  Distress may still
            // select liquidation because a retention candidate with more unmet
            // Survival never replaces the joint optimum.  This happens before
            // compilation, so food, labor, slaughter and target all describe
            // the same evaluated field/herd pair.
            if (profile.Exists && sustainable > Epsilon)
            {
                double retainedTarget = Math.Min(profile.Animals, sustainable);
                var retainedHerd = herdOptions
                    .Where(h => h.Target <= retainedTarget + Epsilon)
                    .OrderByDescending(h => h.Target)
                    .ThenBy(h => h.Herders).First();
                var retainedCandidate = Evaluate(best.Field, retainedHerd);
                int retainedFieldIndex = fields.IndexOf(best.Field);
                int retainedHerdIndex = herdOptions.IndexOf(retainedHerd);
                if (evaluated.Add((retainedFieldIndex, retainedHerdIndex)))
                    result.AlternativesEvaluated++;
                // This continuity preference may promote a complementary herd
                // up to the zero-displacement envelope, but it must never
                // replace a joint optimum that already retained more animals
                // profitably inside the absolute physical envelope. Doing so
                // silently turned the sustainable target into a hard pastoral
                // cap, forced liquidation, and erased the subsequent
                // Development comparison by setting its growth ceiling to the
                // reduced target.
                if (retainedHerd.Target > best.Herd.Target + Epsilon &&
                    retainedCandidate.Unmet <= best.Unmet + Epsilon)
                    best = retainedCandidate;
            }

            result.FieldFarmers = best.Field.Farmers;
            result.RetainedFoodSupply = best.Food;
            result.SelectedFieldOperation = best.Field.Operation;
            result.SelectedPortfolioValue = best.Value;
            result.SelectedPortfolioUnmet = best.Unmet;
            result.SelectedFutureFieldMaintenance =
                best.Field.FutureMaintenance;
            double selectedFutureDisplaced = Math.Max(0, best.Herd.Target *
                profile.FoodPerAnimal * intervals - site.Survey.NonhumanFood);
            double selectedFutureLand = Math.Max(0,
                best.Field.FutureLand - selectedFutureDisplaced);
            double selectedFutureAnimal = best.Herd.Target *
                managedFoodPerAnimal;
            double selectedFutureNeed = Math.Max(0,
                continuation.RecurringFoodTarget - selectedFutureAnimal);
            double selectedFutureHarvest = Math.Min(selectedFutureNeed,
                Math.Min(selectedFutureLand,
                    best.Field.FutureMaintenance * annualGatherRate *
                    Math.Max(1, best.Field.FutureHarvestProductivity) * years));
            result.SelectedFutureFieldHarvestWork = selectedFutureHarvest /
                Math.Max(Epsilon, annualGatherRate *
                    Math.Max(1, best.Field.FutureHarvestProductivity));
            result.HSustainable = profile.Exists
                ? LaborCapacity(best.Field,
                    site.Herd.ZeroDisplacementCapacity, false) : 0;
            double absoluteLabor = profile.Exists
                ? LaborCapacity(best.Field, site.Herd.AbsoluteCapacity, true) : 0;
            // Compile the same joint field/herd portfolio that was evaluated.
            // Replacing this target after selection would silently reintroduce
            // a field-first or herd-first mandate and make its food/labour
            // forecast inconsistent with the work passed to DemandControl.
            result.HRetained = best.Herd.Target;
            result.HerdHerders = best.Herd.Herders;
            result.SlaughterFood = best.Herd.SlaughterFood;
            // A portfolio already liquidating excess livestock cannot turn the
            // same excess into a breeding mandate. Otherwise absolute support
            // is only a physical Development ceiling: DemandControl's marginal
            // bands charge every unit of displaced human food and arbitrate it
            // against field and other Development work.
            double growthSupport = result.HCurrent > result.HRetained + Epsilon
                ? result.HRetained : absoluteLabor;
            result.HGrowthCeiling = Math.Min(growthSupport, result.HGrown);
            return result;
        }
    }
}
