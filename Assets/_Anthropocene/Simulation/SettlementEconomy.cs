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
        public readonly double RequiredRecurringLabor;
        public readonly HerdSupportEnvelope Herd;
        internal readonly LocalHerdSurvey Survey;

        private SettlementSiteEvaluation(int tile, int surveyedTiles,
            double humanFood, double humanFoodAfterAbsoluteHerd,
            double fixedHousing, double retainedFieldCapital,
            double requiredRecurringLabor, HerdSupportEnvelope herd,
            LocalHerdSurvey survey)
        {
            Tile = tile; SurveyedTiles = surveyedTiles;
            HumanFood = Math.Max(0, humanFood);
            HumanFoodAfterAbsoluteHerd = Math.Max(0, humanFoodAfterAbsoluteHerd);
            FixedHousing = Math.Max(0, fixedHousing);
            RetainedFieldCapital = Math.Max(0, retainedFieldCapital);
            RequiredRecurringLabor = Math.Max(0, requiredRecurringLabor);
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
            double fieldLabor = site == camp.Tile
                ? CultivationEconomy.MaintenanceFarmersForPlanning(world, camp, years) : 0;
            double retainedHerd = Math.Min(profile.Animals,
                support.ZeroDisplacementCapacity);
            double herdLabor = HerdingTooling.RequiredHerdersFor(world, camp,
                years, retainedHerd);
            return new SettlementSiteEvaluation(site, survey.Sites.Length,
                humanFood, afterHerd, fixedHousing, fieldCapital,
                fieldLabor + herdLabor, support, survey);
        }
    }

    internal sealed class RetainedSettlementPortfolio
    {
        public int FieldFarmers, HerdHerders;
        public double HCurrent, HSustainable, HAbsolute, HGrown, HRetained,
            HGrowthCeiling;
        public ManagedFoodForecast GrownForecast;
        public SettlementSiteEvaluation Site;
    }

    // The settlement-level supervisor selects the retained capital portfolio;
    // DemandControl then supplies its recurring work. Existing operated fields
    // receive a strong initial retention preference, while herd support is
    // limited by nonhuman forage, water and a bounded labour-feasibility scan.
    // The scan has fewer than 40 breakpoints, independent of population size.
    internal static class SettlementPortfolioSupervisor
    {
        internal static RetainedSettlementPortfolio Select(WorldState world,
            Camp camp, CampProductionDemand demand, EconomicIntent settings,
            SettlementSiteEvaluation site)
        {
            var cfg = world.Config;
            double years = cfg.TurnDays / cfg.DaysPerYear;
            double intervals = cfg.GenerationIntervals;
            int residents = Math.Max(0, camp.Population);
            int farmers = demand.Established
                ? Math.Min(residents, Math.Max(0,
                    (int)Math.Ceiling(demand.Upkeep - 1e-9))) : 0;
            double farmRate = demand.HarvestProductivity * cfg.GatherPerPerson * intervals;
            bool distressed = camp.DeficitTurns > 0 &&
                camp.Food + demand.ForageSupply < demand.Meals - 1e-9;
            if (distressed)
            {
                int productive = farmRate > 0
                    ? (int)Math.Ceiling(Math.Min(demand.PlantSupply, demand.Meals) /
                        farmRate - 1e-9) : 0;
                farmers = Math.Min(farmers, Math.Max(0, productive));
            }

            var profile = HerdConsumptionProfile.Owned(world, camp);
            var grown = profile.Exists
                ? TurnResolver.ForecastGrownManagedAnimalFood(world, camp,
                    settings.ManagedBreedingIntensity, intervals, years, camp.Tile)
                : new ManagedFoodForecast();
            var result = new RetainedSettlementPortfolio
            {
                FieldFarmers = farmers,
                HCurrent = profile.Animals,
                HAbsolute = profile.Exists ? site.Herd.AbsoluteCapacity : 0,
                HGrown = grown.Animals > 0 ? grown.Animals : profile.Animals,
                GrownForecast = grown,
                Site = site
            };
            if (!profile.Exists || residents <= farmers)
            {
                result.HSustainable = result.HRetained = result.HGrowthCeiling = 0;
                return result;
            }

            int maximumHerders = Math.Max(0, residents - farmers);
            var breaks = new SortedSet<int> { 0, maximumHerders };
            void AddRequirement(double animals)
                => breaks.Add(Math.Min(maximumHerders,
                    HerdingTooling.RequiredHerdersFor(world, camp, years,
                        Math.Max(0, animals))));
            AddRequirement(profile.Animals);
            AddRequirement(site.Herd.ZeroDisplacementCapacity);
            AddRequirement(result.HGrown);
            int toolBreak = (int)Math.Min(maximumHerders,
                Math.Max(0, camp.Inventory.HerdingTools) *
                cfg.Neolithic.HerdingToolCoverageYearsPerUnit / Math.Max(1e-12, years));
            breaks.Add(toolBreak); breaks.Add(Math.Min(maximumHerders, toolBreak + 1));
            for (int band = 1; band < 32; band++)
                breaks.Add((int)Math.Ceiling(maximumHerders * band / 32.0));

            double gatherRate = cfg.GatherPerPerson * intervals;
            double fullAnimalYield = cfg.AnimalFoodPerManagedAnimal *
                cfg.Consumption * intervals;
            double bestCapacity = 0;
            int bestHerders = 0;
            foreach (int herders in breaks.Where(h => h >= 0 && h <= maximumHerders))
            {
                double capacity = Math.Min(site.Herd.ZeroDisplacementCapacity,
                    HerdingTooling.AnimalsManagedBy(world, camp, years, herders));
                double currentRetained = Math.Min(profile.Animals, capacity);
                double animalFood = currentRetained * fullAnimalYield;
                double foodTarget = Math.Min(demand.ForageSupply,
                    Math.Max(0, demand.Biological - animalFood));
                double terrestrial = Math.Min(demand.PlantSupply, foodTarget);
                double harvest = Math.Min(terrestrial, farmers * farmRate);
                int gatherers = gatherRate > 0
                    ? (int)Math.Ceiling(Math.Max(0, foodTarget - harvest) /
                        gatherRate - 1e-9) : 0;
                if (farmers + herders + gatherers > residents) continue;
                if (capacity > bestCapacity + 1e-9 ||
                    Math.Abs(capacity - bestCapacity) <= 1e-9 && herders < bestHerders)
                {
                    bestCapacity = capacity; bestHerders = herders;
                }
            }
            result.HSustainable = Math.Max(0, bestCapacity);
            result.HRetained = Math.Min(result.HCurrent, result.HSustainable);
            result.HGrowthCeiling = Math.Min(result.HSustainable, result.HGrown);
            result.HerdHerders = Math.Min(maximumHerders,
                HerdingTooling.RequiredHerdersFor(world, camp, years,
                    result.HRetained));
            return result;
        }
    }
}
