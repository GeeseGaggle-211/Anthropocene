using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Anthropocene.Simulation
{
    [Serializable]
    public sealed class ObservedTileDto
    {
        public int Id, Food, Biomass, Freshwater, Productivity, Moisture, Condition, ElevationLevel, RidgeMask, FoodMaximum, BiomassMaximum, GatherablePercent;
        public Terrain Terrain; public EnvironmentKind Environment; public VegetationTier NaturalVegetation, CurrentVegetation, EffectiveVegetation;
        public double NaturalVegetationRetention, EffectiveNaturalVegetationService;
        public Relief Relief; public float Elevation; public WaterKind WaterBody; public FreshwaterSource FreshwaterSource;
        public bool River, Cultivated, Fishery, Oasis;
        public ObservedRiverDto[] Rivers;
        internal static ObservedTileDto From(TileObservation t) => new ObservedTileDto { Id = t.Id, Food = t.Food, Biomass = t.PotentialFood, Freshwater = t.Freshwater, Productivity = t.Productivity, Moisture = t.Moisture, Condition = t.Condition, ElevationLevel = t.ElevationLevel, RidgeMask = t.RidgeMask, FoodMaximum = t.FoodMaximum, BiomassMaximum = t.PotentialMaximum, GatherablePercent = t.GatherablePercent, Terrain = t.Terrain, Environment = t.Environment, NaturalVegetation = t.NaturalVegetationPotential, CurrentVegetation = t.CurrentVegetation, EffectiveVegetation = t.EffectiveVegetation, NaturalVegetationRetention = t.NaturalVegetationRetention, EffectiveNaturalVegetationService = t.EffectiveNaturalVegetationService, Relief = t.Relief, Elevation = t.Elevation, WaterBody = t.WaterBody, FreshwaterSource = t.FreshwaterSource, River = t.River, Cultivated = t.Cultivated, Fishery = t.Fishery, Oasis = t.Oasis, Rivers = t.Rivers.Select(r => new ObservedRiverDto { Side = r.Side, FlowBand = r.FlowBand, Forward = r.Forward, Mouth = r.Mouth, Width = r.Width }).ToArray() };
    }
    [Serializable] public sealed class ObservedRiverDto { public int Side, FlowBand; public bool Forward, Mouth; public float Width; }
[Serializable] public sealed class ObservedSettlementDto { public int Id, Tile, Population, Gatherers, LinkedPopulation; public double AnimalFabric, LightHousing, HerdingTools, FixedHousing; public bool Agriculture, Husbandry, IsVillage, RecentlyMoved; public EconomicIntent Economy; public MaterialSourceObservation[] MaterialSources; }
    [Serializable]
    public sealed class ObservedUnitDto
    {
        public int Id, Tile, Population, Player, SourceCamp, Provisions, DeficitTurns; public bool InSupply;
        internal static ObservedUnitDto From(UnitObservation u) => new ObservedUnitDto { Id = u.Id, Tile = u.Tile, Population = u.Population, Player = u.Player, SourceCamp = u.SourceCamp, Provisions = u.Provisions, DeficitTurns = u.DeficitTurns, InSupply = u.InSupply };
    }
    [Serializable]
    public sealed class ObservedHerdDto
    {
        public int Id, Tile, Size, Owner, DomesticCamp, DeficitTurns, StartingAnimals, SurvivingAnimals, NewbornAnimals, StarvationDeaths, RepresentativeCount;
        public string Species, Name, CountLabel;
        public bool Domestic, OwnedDomestic, ExactCount;
        public double DeficitSeverity, DeathRemainder, FoodNutrition, WaterNutrition, Nutrition, ManagementFulfillment, PotentialFood, AcceptedFood;
    }
    [Serializable] public sealed class ObservedExtractionDto { public int Tile; public double Food; }
    [Serializable] public sealed class ObservedPredictionDto { public int Herd, Tile; }
    [Serializable] public sealed class ObservedSiteCoverageDto { public int Tile; public double HumanFood, HerdForage, HerdWater; }
    [Serializable] public sealed class ObservedAgriculturalSiteDto { public int Tile, RetainedFields; public double ManagedRecoveryCoverage, LostRebuildBurden; }
    [Serializable]
    public sealed class ObservationDto
    {
        public int Schema = AgentObservation.SchemaVersion, Player, Turn, PolicySeed, CampId, CampTile, CampPopulation, GathererPopulation, LinkedPopulation, FoodCondition, DeficitTurns;
        public int Width, Height, MinimumCamp, HunterMovement, CampMovement, WaterMovementCost, SupplyRange, SustainableHuntPopulation;
        public double Days, Inertia, HuntingFieldcraft, HuntingInclination, GatheringInclination, FishingInclination;
        public bool Watercraft;
        public double[] Experience;
        public Mandate Mandate;
        public ObservedSettlementDto[] Settlements;
        public ObservedTileDto[] Tiles;
        public ObservedUnitDto[] Hunters, Rivals, ForeignHunters;
        public ObservedHerdDto[] Herds;
        public ObservedExtractionDto[] RecentOwnExtraction;
        public ObservedPredictionDto[] LikelyHerdDestinations;
        public ObservedSiteCoverageDto[] SiteCoverage;
        public ObservedAgriculturalSiteDto[] AgriculturalSites;
        public EntityActionMask[] LegalActions;
        public static ObservationDto From(AgentObservation o, Mandate mandate) => new ObservationDto
        {
            Player = o.Player, Turn = o.Turn, PolicySeed = o.PolicySeed, CampId = o.CampId, CampTile = o.CampTile, CampPopulation = o.CampPopulation, GathererPopulation = o.GathererPopulation, LinkedPopulation = o.LinkedPopulation, FoodCondition = o.FoodCondition, DeficitTurns = o.DeficitTurns, Width = o.Map.Width, Height = o.Map.Height, MinimumCamp = o.MinimumCamp, HunterMovement = o.HunterMovement, CampMovement = o.CampMovement, WaterMovementCost = o.WaterMovementCost, SupplyRange = o.SupplyRange, SustainableHuntPopulation = o.SustainableHuntPopulation,
            Days = o.Days, Inertia = o.Inertia, HuntingFieldcraft = o.HuntingFieldcraft, HuntingInclination = o.HuntingInclination, GatheringInclination = o.GatheringInclination, FishingInclination = o.FishingInclination, Watercraft = o.Watercraft, Experience = o.Experience.ToArray(),
            Mandate = new Mandate { Risk = mandate.Risk, Mobility = mandate.Mobility, Exploration = mandate.Exploration, Reserve = mandate.Reserve, Hunting = mandate.Hunting, Gathering = mandate.Gathering, Fishing = mandate.Fishing, Persistence = mandate.Persistence },
            Settlements = o.Settlements.Select(c => new ObservedSettlementDto { Id = c.Id, Tile = c.Tile, Population = c.Population, Gatherers = c.Gatherers, LinkedPopulation = c.LinkedPopulation, AnimalFabric = c.AnimalFabric, LightHousing = c.LightHousing, HerdingTools = c.HerdingTools, FixedHousing = c.FixedHousing, Agriculture = c.Agriculture, Husbandry = c.Husbandry, IsVillage = c.IsVillage, RecentlyMoved = c.RecentlyMoved, Economy = c.Economy.Copy(), MaterialSources = c.MaterialSources }).ToArray(),
            Tiles = o.Tiles.OrderBy(p => p.Key).Select(p => ObservedTileDto.From(p.Value)).ToArray(), Hunters = o.Hunters.Select(ObservedUnitDto.From).ToArray(), Rivals = o.Rivals.Select(ObservedUnitDto.From).ToArray(), ForeignHunters = o.ForeignHunters.Select(ObservedUnitDto.From).ToArray(),
            Herds = o.Herds.Select(h => new ObservedHerdDto { Id = h.Id, Tile = h.Tile, Size = h.Size, Owner = h.Owner, DomesticCamp = h.DomesticCamp, DeficitTurns = h.DeficitTurns, StartingAnimals = h.StartingAnimals, SurvivingAnimals = h.SurvivingAnimals, NewbornAnimals = h.NewbornAnimals, StarvationDeaths = h.StarvationDeaths, RepresentativeCount = h.Count.Representative, Species = h.Species, Name = h.Name, CountLabel = h.Count.Label, Domestic = h.Domestic, OwnedDomestic = h.OwnedDomestic, ExactCount = h.Count.Exact, DeficitSeverity = h.DeficitSeverity, DeathRemainder = h.DeathRemainder, FoodNutrition = h.FoodNutrition, WaterNutrition = h.WaterNutrition, Nutrition = h.Nutrition, ManagementFulfillment = h.ManagementFulfillment, PotentialFood = h.PotentialFood, AcceptedFood = h.AcceptedFood }).ToArray(),
            RecentOwnExtraction = o.RecentOwnExtraction.OrderBy(p => p.Key).Select(p => new ObservedExtractionDto { Tile = p.Key, Food = p.Value }).ToArray(), LikelyHerdDestinations = o.LikelyHerdDestinations.OrderBy(p => p.Key).Select(p => new ObservedPredictionDto { Herd = p.Key, Tile = p.Value }).ToArray(),
            SiteCoverage = o.SiteCoverage.OrderBy(s => s.Tile).Select(s => new ObservedSiteCoverageDto { Tile = s.Tile, HumanFood = s.HumanFood, HerdForage = s.HerdForage, HerdWater = s.HerdWater }).ToArray(),
            AgriculturalSites = o.AgriculturalSites.OrderBy(s => s.Tile).Select(s => new ObservedAgriculturalSiteDto
                { Tile = s.Tile, RetainedFields = s.RetainedFields, ManagedRecoveryCoverage = s.ManagedRecoveryCoverage, LostRebuildBurden = s.LostRebuildBurden }).ToArray(),
            LegalActions = Simulation.LegalActions.Build(o)
        };
    }
    public static class ObservationContract
    {
        public static string Serialize(AgentObservation observation, Mandate mandate)
        {
            using (var stream = new MemoryStream()) { new DataContractJsonSerializer(typeof(ObservationDto)).WriteObject(stream, ObservationDto.From(observation, mandate)); return Encoding.UTF8.GetString(stream.ToArray()); }
        }
        public static ObservationDto Deserialize(string json)
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                var dto = (ObservationDto)new DataContractJsonSerializer(typeof(ObservationDto)).ReadObject(stream);
                if (dto.Schema != AgentObservation.SchemaVersion) throw new InvalidDataException("Unsupported observation schema."); return dto;
            }
        }
    }
}
