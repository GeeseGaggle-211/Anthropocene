using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    public enum Terrain { Ocean, Shallows, Lake, Grassland, Woodland, Jungle, Desert, Tundra, Mountain, Ice }
    public enum Domain { Hunting, Gathering, Fishing }
    public enum Technology { None, AnimalHusbandry, Cultivation, Watercraft }
    public enum OrderKind { NoOp, Move, Dispatch, Hunt, Fish, Reintegrate }
    public enum WorldShape { Continents, Archipelagos, Pangaea }
    public enum Relief { Lowland, Hills, Highland, Mountain }
    public enum WaterKind { None, Freshwater, Saltwater }
    public enum FreshwaterSource { None, RainPool, Lake, River, Oasis }

    [Serializable]
    public sealed class SimulationConfig
    {
        public int Version = 5;
        public int Width = 1024, Height = 512, Players = 32, PlateCount = 18;
        public WorldShape Shape = WorldShape.Continents;
        public double BaseDays = 91.25, TurnDays = 7300, MinDays = 1, MaxDays = 36500;
        public double DaysPerYear = 365, PracticeDays = 7300, AnnualHumanGrowthRate = .01145, GrowthSurplusRatio = .25;
        public double HoverDelay = 2, HoverFadeIn = .2, HoverFadeOut = .15, HoverMotionPixels = 5;
        public double[] ElevationQuantiles = { .12, .25, .40, .55, .69, .81, .90, .96, .99 };
        public int HerdSmallCountThreshold = 10, HerdCountRounding = 10, HerdBroadCountSize = 100;
        public int CampMovement = 1, HunterMovement = 2, HerdMovement = 1, WaterMovement = 1;
        public int VisionRadius = 2, GatherRadius = 1, MinimumCamp = 6, StartingPopulation = 24;
        public int StartSeparation = 12, StarvationGraceTurns = 2;
        public double StartingFood = 48, StartingReserveGenerations = 1, Consumption = 1, Mortality = .18;
        public double GatherFraction = .22, GatherPerPerson = 1.8, PopulationScale = 24;
        public double RichGatherableFraction = .25, GrasslandGatherableFraction = .10, AridGatherableFraction = .05, TundraGatherableFraction = .08, GatherableRecoveryBias = 1;
        public double HuntEfficiency = .22, HuntVariance = .35, HuntBreedingReserveFraction = .2, SustainableHerdGrowthRetention = .25, FishPerPerson = 2.6;
        public int SustainableHuntPopulation = 16;
        public double WanderChance = .05;
        public double ExperienceThreshold = 225, UnproductivePracticeFraction = .1, CarryoverMultiplier = 1.25, CarryoverFloor = 100;
        public double PracticeBonusStart = .25, PracticeBonusFull = .5;
        public double GatheringImpactReduction = .5, FishingImpactReduction = .5, HuntingFoodYieldScale = 2.5, HuntingYieldBonus = .5, HuntingPracticeMultiplier = 3.25, PassiveAquaticFishingPracticeShare = .25;
        public double LandDepletionPerGeneration = .08, AquaticDepletionPerGeneration = .08, EcologicalConditionRecovery = .06;
        public double CultivationMultiplier = 1.4, CultivationMoisture = .25, RelocationInertia = .18;
        public int InitialDomesticAnimals = 5;
        public double HabitableZonePosition = .5, InnerTemperature = 12, OuterTemperature = -12;
        public double SeaLevel = .45, Uplift = .2, ElevationNoise = .11, Erosion = .12;
        public double SeasonalAmplitude = 13, LatitudeGradient = 48, LapseRate = 28, OceanModeration = .45;
        public double Evaporation = .2, MoistureDecay = .97, OrographicRain = 2.5, Monsoon = .12;
        public double RiverThreshold = 28, WaterRecovery = .35, FoodRegeneration = .4;
        public int Hotspots = 8, HotspotChainLength = 4;
        public double StartViability = 85;
        public int GenerationBudgetSeconds = 90, GenerationBudgetMegabytes = 1000;
        public double[] BandThresholds = { .03, .25, .5, .8 };
        public int SupplyRange = 3;
        public double CarryingCapacityPerHunter = 5, HunterCarryGenerations = 2.5, ExpeditionProvisionTurns = 3, CampProvisionReserveTurns = .5;
        public double MinimumStorageGenerations = 1, MaximumStorageGenerations = 4, HunterForagePerPerson = 1;
        public double GrowthReserveTurns = 1.5;
        public double HillsProductivity = .95, HighlandProductivity = .8, MountainProductivity = .5;
        public int HerdRegionSize = 32, MinimumOpeningHerds = 1;
        public double HerdsPerLandTile = .008, HerdRegionVariation = .35;
        public bool WildHerdFissionEnabled;
        public double StartFreshwaterMinimum = 6, RainPoolRainThreshold = .035, RainPoolDrainageMinimum = 1;
        public int OasisRegionSize = 32, OasisSpacing = 7, OasisSurfaceWaterSeparation = 3, OasisMinimumRegionArea = 100;
        public double OasisDensity = .003, OasisViabilityThreshold = .18, OasisFreshwater = 70, OasisFood = 38, OasisFoodRegeneration = .3, OasisWaterRecovery = .14, OasisNeighborFalloff = .3;
        public List<SpeciesDefinition> Species = SpeciesDefinition.Defaults();
        public SpeciesDefinition GetSpecies(string id) => Species.FirstOrDefault(s => s.Id == id) ?? throw new InvalidOperationException("Unknown species: " + id);

        public void Validate()
        {
            if (Width < 12 || Height < 8 || Width > 2048 || Height > 1024 || Players < 1 || Players > MaximumPlayersFor(Width, Height))
                throw new ArgumentException("Unsupported map dimensions or player count.");
            if (PlateCount < 2 || PlateCount > 64 || TurnDays < MinDays || TurnDays > MaxDays || BaseDays <= 0)
                throw new ArgumentException("Invalid generation or clock configuration.");
            if (CampMovement != 1 || HunterMovement != 2 || HerdMovement != 1 || VisionRadius != 2 || GatherRadius != 1)
                throw new ArgumentException("Paleolithic movement and vision must be 1/2/1 and radius 2/1.");
            if (StartingPopulation <= MinimumCamp || MinimumCamp < 1 || Consumption <= 0 || PopulationScale <= 0)
                throw new ArgumentException("Invalid population configuration.");
            if (SupplyRange < 0 || CarryingCapacityPerHunter <= 0 || HunterCarryGenerations <= 0 || StartingReserveGenerations <= 0 || MinimumStorageGenerations <= 0 || MaximumStorageGenerations < MinimumStorageGenerations || HunterForagePerPerson < 0 || HuntBreedingReserveFraction <= 0 || HuntBreedingReserveFraction >= 1 || SustainableHuntPopulation < 1 || SustainableHerdGrowthRetention <= 0 || SustainableHerdGrowthRetention > 1 || UnproductivePracticeFraction < 0 || UnproductivePracticeFraction > 1 || PracticeBonusStart < 0 || PracticeBonusFull <= PracticeBonusStart || PracticeBonusFull > 1 || GatheringImpactReduction < 0 || GatheringImpactReduction > 1 || FishingImpactReduction < 0 || FishingImpactReduction > 1 || HuntingFoodYieldScale <= 0 || HuntingYieldBonus < 0 || HuntingPracticeMultiplier < 1 || PassiveAquaticFishingPracticeShare < 0 || PassiveAquaticFishingPracticeShare > 1 || LandDepletionPerGeneration < 0 || AquaticDepletionPerGeneration < 0 || EcologicalConditionRecovery < 0 || EcologicalConditionRecovery > 1 || RichGatherableFraction <= 0 || RichGatherableFraction > 1 || GrasslandGatherableFraction <= 0 || GrasslandGatherableFraction > RichGatherableFraction || AridGatherableFraction <= 0 || AridGatherableFraction > GrasslandGatherableFraction || TundraGatherableFraction <= 0 || TundraGatherableFraction > RichGatherableFraction || GatherableRecoveryBias < 0 || AnnualHumanGrowthRate < 0 || GrowthReserveTurns <= 0 || HerdRegionSize < 4 || MinimumOpeningHerds < 1 || HerdsPerLandTile < 0 || OasisSpacing < 1 || OasisRegionSize < 4 || DaysPerYear <= 0 || PracticeDays <= 0 || GrowthSurplusRatio <= 0 || HoverDelay < 0 || HoverFadeIn <= 0 || HoverFadeOut <= 0)
                throw new ArgumentException("Invalid logistics, demographic or ecological configuration.");
            if (ElevationQuantiles == null || ElevationQuantiles.Length != 9 || ElevationQuantiles[0] <= 0 || ElevationQuantiles[8] >= 1 || !ElevationQuantiles.SequenceEqual(ElevationQuantiles.OrderBy(x => x).Distinct())) throw new ArgumentException("Elevation requires nine increasing quantile boundaries.");
            if (HerdSmallCountThreshold < 1 || HerdCountRounding < 1 || HerdBroadCountSize < HerdSmallCountThreshold) throw new ArgumentException("Invalid observable herd count thresholds.");
            if (Species == null || Species.Count == 0 || Species.Select(s => s.Id).Distinct().Count() != Species.Count || Species.Any(s => string.IsNullOrWhiteSpace(s.Id) || s.FoodConsumption < 0 || s.FreshwaterConsumption <= 0 || s.GrowthRate < 0 || s.FoodYield <= 0 || s.MaximumHerdSize < s.MaximumAnimals))
                throw new ArgumentException("Species definitions must have unique stable IDs and valid ecological rates.");
        }
        public static int MaximumPlayersFor(int width, int height)
        {
            long tiles = (long)width * height;
            return tiles <= 48L * 24 ? 8 : tiles <= 96L * 48 ? 16 : 80;
        }
        public double GenerationIntervals => TurnDays / BaseDays;
        public double StartingReserveFood => StartingPopulation * Consumption * GenerationIntervals * StartingReserveGenerations;
        public static SimulationConfig Small(int players = 4) => new SimulationConfig { Width = 96, Height = 48, Players = players, StartSeparation = 9 };
    }

    [Serializable]
    public struct SeasonData
    {
        public float Temperature, Rain, Snow, WindX, Runoff, Moisture, AccessibleFreshwater;
        public float MaxFood, MaxPotentialFood, MaxFreshwater, MaxSaltwater, FoodRate, PotentialFoodRate, FreshwaterRate, Habitat;
    }
    [Serializable]
    public sealed class Tile
    {
        public int Id, Plate, Downstream = -1;
        public Terrain Terrain;
        public Relief Relief;
        public int ElevationLevel;
        public int RidgeMask;
        public WaterKind WaterBody;
        public FreshwaterSource FreshwaterSource;
        public bool Continental, Cultivated, Fishery, Oasis;
        public float Elevation, Volcanism, Flow, DepressionDepth, OasisInfluence;
        public double Food, PotentialFood, Freshwater, Saltwater;
        public double LandCondition = 1, AquaticCondition = 1;
        public int[] RiverEdges;
        public bool River => RiverEdges != null && RiverEdges.Any(e => e >= 0);
        public double DrinkableWater => WaterBody != WaterKind.Saltwater && FreshwaterSource != FreshwaterSource.None ? Freshwater : 0;
        public SeasonData[] Seasons = new SeasonData[4];
        public bool Aquatic => Terrain == Terrain.Shallows || Terrain == Terrain.Lake;
        public bool Land => Terrain != Terrain.Ocean && !Aquatic;
        public bool TraversableLand => Land && ElevationLevel < 10;
    }
    [Serializable]
    public sealed class PopulationAssignment
    {
        public string Role;
        public int Population;
    }
    [Serializable]
    public sealed class PopulationLedger
    {
        public List<PopulationAssignment> Assignments = new List<PopulationAssignment>();
        public int Assigned => Assignments.Where(a => a != null && a.Population > 0).Sum(a => a.Population);
        public int Gatherers(int population) => Math.Max(0, population - Assigned);
        public void Normalize(int population)
        {
            Assignments = Assignments.Where(a => a != null && !string.IsNullOrWhiteSpace(a.Role) && a.Population > 0)
                .GroupBy(a => a.Role, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new PopulationAssignment { Role = g.Key, Population = g.Sum(a => a.Population) }).ToList();
            int excess = Math.Max(0, Assigned - population);
            for (int i = Assignments.Count - 1; i >= 0 && excess > 0; i--)
            {
                int removed = Math.Min(excess, Assignments[i].Population); Assignments[i].Population -= removed; excess -= removed;
                if (Assignments[i].Population == 0) Assignments.RemoveAt(i);
            }
        }
    }
    [Serializable]
    public sealed class Camp
    {
        public int Id, Player, Tile, Population, DeficitTurns;
        public double Food, DeficitSeverity, FoodDeficit, LastConsumed, DemographicGrowthMultiplier = 1;
        public double[] Experience = new double[3];
        public List<SpeciesPractice> SpeciesExperience = new List<SpeciesPractice>();
        public double GrowthRemainder, DeathRemainder, StepFoodIncome;
        public Technology Technology;
        public int TransitionTurn, SecondaryDomain = -1, Moves, StationaryTurns;
        public bool ForcedTransition;
        public bool[] Technologies = new bool[4];
        public Mandate Mandate = new Mandate();
        public PopulationLedger Workforce = new PopulationLedger();
        public int GathererPopulation => (Workforce ?? (Workforce = new PopulationLedger())).Gatherers(Population);
    }
    [Serializable]
    public sealed class Hunter
    {
        public int Id, Player, SourceCamp, Tile, Population;
        public double CarriedFood, LastConsumed, FoodDeficit, DeficitSeverity, DeathRemainder;
        public int DeficitTurns;
    }
    [Serializable]
    public sealed class Herd
    {
        public int Id, Tile, Animals, Capacity = 40, DomesticCamp = -1;
        public string Species = "antelope";
        public double GrowthRemainder, UnmetFood, UnmetFreshwater;
    }
    [Serializable]
    public sealed class Mandate
    {
        public double Risk = .5, Mobility = .5, Exploration = .5, Reserve = .5, Hunting = 1, Gathering = 1, Fishing = 1, Persistence = .1;
    }
    [Serializable]
    public sealed class Order
    {
        public OrderKind Kind;
        public int Entity, Target = -1, Destination = -1, Population;
        public Order() { }
        public Order(OrderKind kind, int entity, int target = -1, int destination = -1, int population = 0)
        { Kind = kind; Entity = entity; Target = target; Destination = destination; Population = population; }
        public Order Copy() => new Order(Kind, Entity, Target, Destination, Population);
        public override string ToString() => Kind + ":" + Entity + ":" + Target + ":" + Destination + ":" + Population;
    }
    [Serializable]
    public sealed class Transfer
    {
        public string Kind;
        public int Tile, Entity;
        public double Food, PotentialFood, Freshwater;
    }
    [Serializable]
    public sealed class TileChange
    {
        public int Tile;
        public double BeforeFood, BeforePotentialFood, BeforeFreshwater, BeforeLandCondition, BeforeAquaticCondition;
        public double AfterFood, AfterPotentialFood, AfterFreshwater, AfterLandCondition, AfterAquaticCondition;
    }
    [Serializable]
    public sealed class TurnRecord
    {
        public int Turn, Seed, Deaths, Growth;
        public double StartDays, Duration;
        public List<string> Events = new List<string>();
        public List<string> Orders = new List<string>();
        public List<Transfer> Transfers = new List<Transfer>();
        public List<TileChange> Changes = new List<TileChange>();
        public List<DecisionTrace> Decisions = new List<DecisionTrace>();
        public List<LogisticsRecord> Logistics = new List<LogisticsRecord>();
        public List<PopulationChange> PopulationChanges = new List<PopulationChange>();
        public List<DemographicReport> Demographics = new List<DemographicReport>();
        public void Log(string message) => Events.Add(message);
    }
    [Serializable]
    public sealed class WorldState
    {
        public const int GeneratorVersion = 5;
        public int Version = 5, Seed, Turn = 1, NextId = 1;
        public double Days;
        public SimulationConfig Config;
        public Tile[] Tiles;
        public List<Camp> Camps = new List<Camp>();
        public List<Hunter> Hunters = new List<Hunter>();
        public List<Herd> Herds = new List<Herd>();
        public List<RiverEdge> Rivers = new List<RiverEdge>();
        public List<TurnRecord> History = new List<TurnRecord>();
        public GenerationReport Generation;
        public int Season => (int)(Days / Config.BaseDays) % 4;
        public HexMap Map => new HexMap(Config.Width, Config.Height);
        public SeasonData Ecology(int tile) => Tiles[tile].Seasons[Season];
        public bool Finished => Camps.All(c => c.Technology != Technology.None || LinkedPopulation(c) == 0);
        public int LinkedPopulation(Camp camp) => camp.Population + Hunters.Where(h => h.SourceCamp == camp.Id).Sum(h => h.Population);
        public bool CanEnter(int tile, bool hunter, Camp camp = null) => tile >= 0 && tile < Tiles.Length &&
            Tiles[tile].ElevationLevel < 10 && (Tiles[tile].Land || (hunter && camp != null && camp.Technologies[(int)Technology.Watercraft] && Tiles[tile].Terrain == Terrain.Shallows));
    }
    public static class Numbers
    {
        public static double Clamp(double n, double low, double high) => Math.Max(low, Math.Min(high, n));
        public static double Fraction(double fraction, double time) => Math.Abs(time - 1) < 1e-10 ? Clamp(fraction, 0, 1) : 1 - Math.Pow(1 - Clamp(fraction, 0, 1), time);
        // Stateless named streams: new random work cannot perturb other systems or entities.
        public static uint Hash(int seed, int stream, int entity, int turn = 0)
        {
            unchecked
            {
                uint x = (uint)seed ^ (uint)stream * 0x9E3779B9u ^ (uint)entity * 0x85EBCA6Bu ^ (uint)turn * 0xC2B2AE35u;
                x ^= x >> 16; x *= 0x7FEB352Du; x ^= x >> 15; x *= 0x846CA68Bu; return x ^ (x >> 16);
            }
        }
        public static double Random(int seed, int stream, int entity, int turn = 0) => Hash(seed, stream, entity, turn) / 4294967296.0;
    }
}
