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
            SelectedPortfolioUnmet;
        public int AlternativesEvaluated;
        public ManagedFoodForecast GrownForecast;
        public SettlementSiteEvaluation Site;
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
            double farmRate = demand.HarvestProductivity * cfg.GatherPerPerson * intervals;
            double gatherRate = cfg.GatherPerPerson * intervals;
            bool plough = AgriculturalTechnology.Plough(world, camp);
            double ploughHarvest = plough ? cfg.Agriculture.PloughHarvest : 1;
            CultivationEconomy.HarvestProductivityRates(world, camp, demand.Footprint, out double uncoveredFarmProd, out double coveredFarmProd);
            double annualGatherRate = cfg.GatherPerPerson * cfg.DaysPerYear /
                cfg.BaseDays;
            TurnResolver.FoodBucketTargets(world, camp, intervals, years,
                demand.Meals, camp.Food, out double survivalFood, out _,
                out _, out _);

            var profile = HerdConsumptionProfile.Owned(world, camp);
            var grown = profile.Exists
                ? TurnResolver.ForecastGrownManagedAnimalFood(world, camp,
                    settings.ManagedBreedingIntensity, intervals, years, camp.Tile)
                : new ManagedFoodForecast();
            var result = new RetainedSettlementPortfolio
            {
                HCurrent = profile.Animals,
                HAbsolute = profile.Exists ? site.Herd.AbsoluteCapacity : 0,
                HGrown = grown.Animals > 0 ? grown.Animals : profile.Animals,
                GrownForecast = grown,
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
            double rawMaintenance = demand.Established
                ? CultivationEconomy.MaintenanceWorkerRequirement(world, camp) : 0;
            double toolWorkers = years > 0 ? Math.Max(0,
                camp.Inventory.FarmTools) * cfg.Neolithic.FarmToolFarmerYearsPerUnit /
                years : 0;
            double fieldEfficiency = AgriculturalTechnology.MaintenanceEfficiency(world, camp);
            double FarmHarvestCapacity(int count)
            {
                if (count <= 0) return 0;
                if (!plough) return count * farmRate;
                double cov = Math.Min(count, toolWorkers);
                return (cov * coveredFarmProd + Math.Max(0, count - cov) * uncoveredFarmProd) * gatherRate;
            }
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
                int assigned = camp.Workforce?.Assignments?
                    .Where(a => a.Role == WorkforceRoles.Farmer)
                    .Sum(a => a.Population) ?? 0;
                farmerBreaks.Add(Math.Min(maximumFarmers, Math.Max(0, assigned)));
                // Field-operation alternatives depend only on physical land,
                // tools and food service.  A recipe exposed by a secondary
                // capability must not move this breakpoint when it has no
                // demanded use.
                int survivalBreak = 0;
                if (gatherRate > Epsilon)
                {
                    double need = Math.Min(site.Survey.HumanFood, survivalFood);
                    if (plough && cfg.Agriculture.PloughHarvest > 1 + 1e-9 && toolWorkers > Epsilon)
                    {
                        double covYield = toolWorkers * coveredFarmProd * gatherRate;
                        double take = Math.Min(need, covYield);
                        double needed = take / Math.Max(Epsilon, coveredFarmProd * gatherRate)
                            + Math.Max(0, need - take) / Math.Max(Epsilon, uncoveredFarmProd * gatherRate);
                        survivalBreak = (int)Math.Ceiling(needed - Epsilon);
                    }
                    else survivalBreak = (int)Math.Ceiling(need / Math.Max(Epsilon, farmRate) - Epsilon);
                }
                farmerBreaks.Add(Math.Min(maximumFarmers, Math.Max(0, survivalBreak)));
                double toolKink = Math.Min(toolWorkers,
                    rawMaintenance / Math.Max(Epsilon, fieldEfficiency));
                int beforeKink = Math.Min(maximumFarmers, Math.Max(0,
                    (int)Math.Floor(toolKink + Epsilon)));
                farmerBreaks.Add(beforeKink);
                farmerBreaks.Add(Math.Min(maximumFarmers, beforeKink + 1));
            }

            var herderBreaks = new SortedSet<int> { 0, residents };
            void AddRequirement(double animals)
                => herderBreaks.Add(Math.Min(residents,
                    HerdingTooling.RequiredHerdersFor(world, camp, years,
                        Math.Max(0, animals))));
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
            for (int band = 1; band < 16; band++)
                herderBreaks.Add((int)Math.Ceiling(residents * band / 16.0));

            // Cache opening projections and the operation-independent local
            // land forecasts once.  Every field alternative uses the same
            // authoritative stock kernel as actual claims.
            var openings = demand.Eligible.Where(id => world.Tiles[id].Land)
                .OrderBy(id => id).ToDictionary(id => id,
                    id => LazyEcology.ProjectEcology(world, id, world.Days));
            var fixedLand = new Dictionary<(int, long), (double current,
                double future, double residual, double harvestWork)>();
            bool cropRate = (cfg.CropHarvestingOnly && cfg.FarmerProductivity > 1) || ploughHarvest > 1 + 1e-9;
            double futureWildRate = cfg.CropHarvestingOnly ? 1 : cfg.FarmerProductivity;
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
                        double request = cfg.AnnualLandFoodEnabled
                            ? LazyEcology.AccessibleAvailability(world, id, world.Days, cfg.TurnDays, op, Mean(years))
                            : Math.Max(0, opening.Food);
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
                        double refillFood = FoodHorizonForecast.RefillSupply(world, id, refill);
                        double harvestWork = 0;
                        if (cropRate && footprint.Contains(id))
                        {
                            var capacity = EcologyCapacity.Boundary(world, tile, empty,
                                season);
                            double crop = tile.CultivationOperator >= 0 &&
                                tile.CultivationOperator != camp.Id ? 0 :
                                Math.Max(0, capacity.Food - capacity.WildFood);
                            double share = capacity.Food > 0
                                ? crop / capacity.Food : 0;
                            harvestWork = refillFood * ((1 - share) / futureWildRate +
                                share / (cfg.FarmerProductivity * ploughHarvest));
                        }
                        forecast = (current.HumanFoodReceipts,
                            refillFood, refillFood, harvestWork);
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
                        ? futureFieldFood / futureFieldWork : futureWildRate;
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
                    terminalDiscount * Math.Min(residual, demand.Meals);
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
            HerdOption BuildHerdOption(double target)
            {
                target = profile.Exists ? Math.Max(0, Math.Min(
                    profile.Animals, Math.Min(site.Herd.AbsoluteCapacity,
                        target))) : 0;
                int herders = profile.Exists
                    ? HerdingTooling.RequiredHerdersFor(world, camp, years,
                        target) : 0;
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
                return option;
            }
            foreach (int proposed in herderBreaks.Where(h => h >= 0 &&
                h <= residents).OrderBy(h => h))
            {
                double target = profile.Exists ? Math.Min(profile.Animals,
                    Math.Min(site.Herd.AbsoluteCapacity,
                        HerdingTooling.AnimalsManagedBy(world, camp, years,
                            proposed))) : 0;
                var option = BuildHerdOption(target);
                if (herdOptions.Count > 0 && option.Herders == herdOptions.Last().Herders &&
                    Math.Abs(option.Target - herdOptions.Last().Target) <= Epsilon) continue;
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
                    Math.Min(land, FarmHarvestCapacity(field.Farmers)));
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

                double futureDisplaced = Math.Max(0, herd.Target *
                    profile.FoodPerAnimal * intervals - site.Survey.NonhumanFood);
                double futureLand = Math.Max(0,
                    field.FutureLand - futureDisplaced);
                double futureHuman = futureLand + aquatic;
                double futureAnimal = herd.Target * managedFoodPerAnimal;
                double futureNeed = Math.Max(0, demand.Meals - futureAnimal);
                double futureHarvest = Math.Min(futureNeed,
                    Math.Min(futureLand, field.FutureMaintenance *
                        annualGatherRate * Math.Max(1,
                            field.FutureHarvestProductivity) * years));
                double futureGather = Math.Min(Math.Max(0,
                    futureHuman - futureHarvest), Math.Max(0,
                        futureNeed - futureHarvest));
                double futureGatherers = futureGather /
                    Math.Max(Epsilon, gatherRate);
                double futureUseful = Math.Min(demand.Meals,
                    futureAnimal + futureHarvest + futureGather);
                double futureWorkers = field.FutureMaintenance + herd.Herders +
                    futureGatherers;
                double currentUseful = Math.Min(survivalFood, food);
                // Current work has no invented shadow price here: after Survival
                // is feasible, the Elder may have no competing current service.
                // Recurring future work still pays the gathering opportunity
                // cost over the common finite horizon.
                double value = currentUseful +
                    futureLife * (futureUseful / Math.Max(Epsilon, years) -
                        futureWorkers * annualGatherRate) +
                    terminalDiscount * (Math.Min(field.Residual, demand.Meals) +
                        herd.ResidualFood) + field.RetainedCapitalValue;
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
                if (candidate.Field.Farmers != incumbent.Field.Farmers)
                    return candidate.Field.Farmers > incumbent.Field.Farmers;
                return candidate.Herd.Target > incumbent.Herd.Target + Epsilon;
            }

            // Merge the two prefix chains by their next finite-horizon marginal
            // return.  This visits F + H states, not F x H combinations.
            int fi = 0, hi = 0;
            result.AlternativesEvaluated = 1;
            while (fi + 1 < fields.Count || hi + 1 < herdOptions.Count)
            {
                double fieldMarginal = fi + 1 < fields.Count
                    ? (fields[fi + 1].MergeValue - fields[fi].MergeValue) /
                        Math.Max(Epsilon, fields[fi + 1].Farmers -
                            fields[fi].Farmers) : double.NegativeInfinity;
                double herdMarginal = hi + 1 < herdOptions.Count
                    ? (herdOptions[hi + 1].MergeValue -
                        herdOptions[hi].MergeValue) /
                        Math.Max(Epsilon, herdOptions[hi + 1].Herders -
                            herdOptions[hi].Herders) : double.NegativeInfinity;
                if (fieldMarginal >= herdMarginal) fi++; else hi++;
                var candidate = Evaluate(fields[fi], herdOptions[hi]);
                result.AlternativesEvaluated++;
                if (Better(candidate, best)) best = candidate;
            }

            // Existing field capital whose own finite continuation is
            // non-negative is a retained-capital portfolio, not a fresh
            // Agriculture commitment.  Give that physical portfolio a bounded
            // one-dimensional herd scan; distress can still beat it through the
            // hard Survival comparison.  This is independent of technology
            // order and does not form a field x herd search.
            if (retainedField > 0)
            {
                foreach (var herd in herdOptions)
                {
                    var candidate = Evaluate(fields[retainedField], herd);
                    result.AlternativesEvaluated++;
                    if (Better(candidate, best)) best = candidate;
                }
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
                foreach (int herders in herderBreaks.Where(h => h >= 0 &&
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
                result.AlternativesEvaluated++;
                if (retainedCandidate.Unmet <= best.Unmet + Epsilon)
                    best = retainedCandidate;
            }

            // Seed herd (Docs/HerdLabour.md): with herd labour on, the settlement keeps at least the whole animals
            // SimulationConfig.MinimumHerdHerders can manage, whatever their value, so a herd is never slaughtered to
            // nothing and can regrow. It never exceeds the herd, the site's physical capacity or the residents left
            // after fields.
            if (profile.Exists && cfg.HerdLabourEnabled && cfg.MinimumHerdHerders > 0)
            {
                double seedTarget = Math.Min(profile.Animals, Math.Min(site.Herd.AbsoluteCapacity,
                    Math.Floor(HerdingTooling.AnimalsManagedBy(world, camp, years, cfg.MinimumHerdHerders) + Epsilon)));
                if (seedTarget >= 1 && best.Herd.Target + Epsilon < seedTarget)
                {
                    var seed = BuildHerdOption(seedTarget);
                    if (best.Field.Farmers + seed.Herders <= residents)
                    {
                        best = Evaluate(best.Field, seed);
                        result.AlternativesEvaluated++;
                    }
                }
            }

            // Herd transition limit (SimulationConfig.HerdCullLimitPerTurn): keep at least (1 - limit) of the herd
            // when doing so leaves Survival no worse; a distressed camp may still liquidate.
            if (profile.Exists && cfg.HerdLabourEnabled && cfg.HerdCullLimitPerTurn < 1)
            {
                double keepTarget = Math.Min(site.Herd.AbsoluteCapacity,
                    Math.Ceiling(profile.Animals * (1 - Math.Max(0, cfg.HerdCullLimitPerTurn)) - Epsilon));
                if (keepTarget >= 1 && best.Herd.Target + Epsilon < keepTarget)
                {
                    var keep = BuildHerdOption(keepTarget);
                    if (best.Field.Farmers + keep.Herders <= residents)
                    {
                        var kept = Evaluate(best.Field, keep);
                        result.AlternativesEvaluated++;
                        if (kept.Unmet <= best.Unmet + Epsilon) best = kept;
                    }
                }
            }

            result.FieldFarmers = best.Field.Farmers;
            result.RetainedFoodSupply = best.Food;
            result.SelectedFieldOperation = best.Field.Operation;
            result.SelectedPortfolioValue = best.Value;
            result.SelectedPortfolioUnmet = best.Unmet;
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
            // The ceiling never cuts below the herd this portfolio retained: a labour-short camp whose absolute
            // envelope fits no further herder still keeps the seed or retained herd it was evaluated with.
            result.HGrowthCeiling = Math.Max(result.HRetained, Math.Min(growthSupport, result.HGrown));
            return result;
        }
    }
}
