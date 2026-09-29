using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Anthropocene.Simulation
{
    public static class Knowledge
    {
        public static readonly string[] Bands = { "Depleted", "Poor", "Decent", "Ample", "Abundant" };
        public static int Band(double value, double capacity, SimulationConfig config)
        {
            double ratio = value / Math.Max(1, capacity);
            for (int i = 0; i < config.BandThresholds.Length; i++) if (ratio < config.BandThresholds[i]) return i;
            return 4;
        }
        public static HashSet<int> Visible(WorldState world, int player)
        {
            var result = new HashSet<int>();
            foreach (var camp in world.Camps.Where(c => c.Player == player && world.LinkedPopulation(c) > 0)) result.UnionWith(world.Map.Radius(camp.Tile, world.Config.VisionRadius));
            foreach (var hunter in world.Hunters.Where(h => h.Player == player && h.Population > 0)) result.UnionWith(world.Map.Radius(hunter.Tile, world.Config.VisionRadius));
            return result;
        }
    }
    public sealed class TileObservation
    {
        public readonly int Id, Food, PotentialFood, Freshwater, Productivity, Moisture, Condition, ElevationLevel, RidgeMask, FoodMaximum, PotentialMaximum, GatherablePercent;
        public readonly Terrain Terrain;
        public readonly EnvironmentKind Environment;
        public readonly VegetationTier NaturalVegetationPotential, CurrentVegetation, EffectiveVegetation;
        public readonly bool River, Cultivated, Fishery, Oasis, Irrigated, Cultivable;
        public readonly double Cultivation, NaturalVegetationRetention, EffectiveNaturalVegetationService;
        public readonly Relief Relief;
        public readonly float Elevation;
        public readonly WaterKind WaterBody;
        public readonly FreshwaterSource FreshwaterSource;
        public readonly IReadOnlyList<RiverObservation> Rivers;
        public bool Land => Terrain != Terrain.Ocean && Terrain != Terrain.Shallows && Terrain != Terrain.Lake;
        public bool Aquatic => Terrain == Terrain.Shallows || Terrain == Terrain.Lake;
        public TileObservation(Tile tile, SeasonData ecology, SimulationConfig config, IReadOnlyList<RiverEdge> rivers, EcologyView? projected = null)
            : this(tile, ecology, config, rivers, projected, null)
        {
        }
        internal TileObservation(WorldState world, Tile tile, IReadOnlyList<RiverEdge> rivers, EcologyView projected)
            : this(tile, tile.Seasons[world.Season], world.Config, rivers, projected, new EcologyCapacity(world, tile, projected, world.Season))
        {
        }
        private TileObservation(Tile tile, SeasonData ecology, SimulationConfig config, IReadOnlyList<RiverEdge> rivers, EcologyView? projected, EcologyCapacity? projectedCapacity)
        {
            var view = projected ?? new EcologyView(tile);
            Cultivable = tile.TraversableLand && tile.Environment != EnvironmentKind.Ice && AnyPotentialFood(tile);
            Environment = tile.Environment; NaturalVegetationPotential = tile.NaturalVegetationPotential; CurrentVegetation = view.CurrentVegetation;
            Cultivation = CultivationEconomy.Intensity(config, view.CultivationCapital);
            NaturalVegetationRetention = EcologyMath.CultivationVegetationRetention(config, Cultivation);
            // Low vegetation has brush/grass service too; enum zero is a label,
            // not an ecological zero. Keep the tier's full service positive
            // before applying the common q-dependent retention factor.
            EffectiveNaturalVegetationService = ((int)view.CurrentVegetation + 1) * NaturalVegetationRetention;
            EffectiveVegetation = tile.Land ? Vegetation.RetainedCover(view.CurrentVegetation, NaturalVegetationRetention) : view.CurrentVegetation;
            Id = tile.Id; Terrain = Vegetation.LegacyTerrain(tile.Environment, view.CurrentVegetation); River = tile.River; Cultivated = tile.Cultivated; Fishery = tile.Fishery; ElevationLevel = tile.ElevationLevel; RidgeMask = tile.RidgeMask; Irrigated = tile.Irrigated;
            double condition = tile.Aquatic || tile.WaterBody == WaterKind.Saltwater ? view.AquaticCondition : view.LandCondition;
            double biomassCapacity, foodCapacity, foodRate, managedFlow;
            if (projectedCapacity.HasValue)
            {
                // The live observation path consumes the same authoritative
                // ecology capacity used by resolution and planning.
                var capacity = projectedCapacity.Value;
                biomassCapacity = capacity.Biomass; foodCapacity = capacity.Food; foodRate = capacity.FoodRate; managedFlow = capacity.ManagedFlow;
            }
            else
            {
                // Lightweight compatibility path for isolated presentation
                // fixtures that do not have a WorldState composition root.
                double openingMultiplier = config.OpeningCompatibilityEffects && tile.Fishery ? config.OpeningFoodMultiplier : 1;
                biomassCapacity = ecology.MaxPotentialFood * openingMultiplier * condition;
                double wildFoodEcological = Math.Min(biomassCapacity, ecology.MaxFood * openingMultiplier * condition *
                    FoodEcology.GatherableFraction(tile, config, view.CurrentVegetation) / FoodEcology.GatherableFraction(tile, config, tile.NaturalVegetationPotential));
                foodCapacity = EcologyMath.EffectiveWildFoodCapacity(wildFoodEcological, biomassCapacity, Cultivation, config);
                foodRate = ecology.FoodRecoveryPerDay;
                managedFlow = EcologyMath.ManagedRegenerationFlow(config, Cultivation, biomassCapacity, ecology.BiomassRecoveryPerDay, tile.CultivationOperatingFraction);
            }
            double accessibility = biomassCapacity > 1e-9 ? foodCapacity / biomassCapacity : 0;
            FoodMaximum = Knowledge.Band(foodCapacity, 70, config);
            PotentialFood = Knowledge.Band(view.PotentialFood, Math.Max(1, ecology.MaxPotentialFood), config); PotentialMaximum = Knowledge.Band(ecology.MaxPotentialFood, 700, config); GatherablePercent = (int)Math.Round(100 * accessibility);
            Oasis = tile.Oasis; Relief = tile.Relief; Elevation = tile.Elevation; WaterBody = tile.WaterBody; FreshwaterSource = tile.FreshwaterSource;
            Rivers = Array.AsReadOnly(tile.RiverEdges == null ? new RiverObservation[0] : tile.RiverEdges.Select((id, side) => new { id, side }).Where(x => x.id >= 0).Select(x => new RiverObservation(x.side, rivers[x.id], tile.Id, new HexCorners(config.Width, config.Height))).ToArray());
            // Food bands describe absolute usable yield; a nearly full barren tile must not appear abundant.
            Food = Knowledge.Band(view.Food, 70, config); Freshwater = Knowledge.Band(view.Freshwater, 70, config);
            Condition = Knowledge.Band(condition, 1, config);
            Productivity = Knowledge.Band((foodCapacity * foodRate + managedFlow) * config.BaseDays, 30, config); Moisture = Knowledge.Band(ecology.Moisture, 1, config);
        }
        private static bool AnyPotentialFood(Tile tile)
        {
            foreach (var season in tile.Seasons) if (season.MaxPotentialFood > 0) return true;
            return false;
        }
    }
    public sealed class RiverObservation
    {
        public readonly int Side, FlowBand;
        public readonly bool Forward, Mouth;
        public readonly float Width;
        public RiverObservation(int side, RiverEdge river, int tile, HexCorners corners) { Side = side; FlowBand = river.Width < 1.8 ? 1 : river.Width < 3 ? 2 : 3; Width = river.Width; Mouth = river.Mouth; Forward = river.FromVertex == corners.Corner(tile, side); }
    }
    public sealed class UnitObservation
    {
        public readonly int Id, Tile, Population, Player, SourceCamp;
        public readonly bool InSupply;
        public readonly int Provisions, DeficitTurns;
        public UnitObservation(int id, int tile, int population, int player, bool inSupply = false, int provisions = 0, int deficitTurns = 0, int sourceCamp = -1) { SourceCamp = sourceCamp; Id = id; Tile = tile; Population = population; Player = player; InSupply = inSupply; Provisions = provisions; DeficitTurns = deficitTurns; }
    }
    // A deliberately coarse, local estimate for a prospective camp site.
    // Ratios are rounded to twentieths and capped; policies never receive
    // exact per-tile food, biomass, or freshwater stocks.
    public sealed class SiteCoverageObservation
    {
        public readonly int Tile;
        public readonly double HumanFood, HerdForage, HerdWater;
        public SiteCoverageObservation(int tile, double humanFood, double herdForage, double herdWater)
        { Tile = tile; HumanFood = humanFood; HerdForage = herdForage; HerdWater = herdWater; }
    }
    // Expected three-turn benefit from retained own visible fields, and the
    // effort needed to restore them after a candidate leaves the footprint.
    // Both are normalized and rounded; rival field ownership is not read.
    public sealed class AgriculturalSiteObservation
    {
        public readonly int Tile, RetainedFields;
        public readonly double ManagedRecoveryCoverage, LostRebuildBurden;
        public AgriculturalSiteObservation(int tile, int retainedFields, double managedRecoveryCoverage, double lostRebuildBurden)
        { Tile = tile; RetainedFields = retainedFields; ManagedRecoveryCoverage = managedRecoveryCoverage; LostRebuildBurden = lostRebuildBurden; }
    }
    public sealed class HerdObservation
    {
        public readonly int Id, Tile, Size, Owner, DomesticCamp, DeficitTurns, StartingAnimals, SurvivingAnimals, NewbornAnimals, StarvationDeaths;
        public readonly string Species, Name;
        public readonly bool Domestic, OwnedDomestic;
        public readonly double DeficitSeverity, DeathRemainder, FoodNutrition, WaterNutrition, Nutrition, ManagementFulfillment, PotentialFood, AcceptedFood;
        public readonly HerdCountEstimate Count;
        public HerdObservation(Herd herd, SimulationConfig config, int owner = -1, int viewer = -2)
        {
            Id = herd.Id; Tile = herd.Tile; Species = herd.Species; Name = config.GetSpecies(Species).Name; Domestic = herd.DomesticCamp >= 0; Owner = owner;
            OwnedDomestic = Domestic && owner == viewer; DomesticCamp = OwnedDomestic ? herd.DomesticCamp : -1;
            DeficitTurns = OwnedDomestic ? herd.DeficitTurns : 0; DeficitSeverity = OwnedDomestic ? herd.DeficitSeverity : 0; DeathRemainder = OwnedDomestic ? herd.DeathRemainder : 0;
            StartingAnimals = OwnedDomestic ? herd.StartingAnimals : 0; SurvivingAnimals = OwnedDomestic ? herd.SurvivingAnimals : 0; NewbornAnimals = OwnedDomestic ? herd.NewbornAnimals : 0; StarvationDeaths = OwnedDomestic ? herd.StarvationDeaths : 0;
            FoodNutrition = OwnedDomestic ? herd.LastFoodNutrition : 0; WaterNutrition = OwnedDomestic ? herd.LastWaterNutrition : 0; Nutrition = OwnedDomestic ? herd.LastNutrition : 0; ManagementFulfillment = OwnedDomestic ? herd.ManagementFulfillment : 0;
            PotentialFood = OwnedDomestic ? herd.AnimalFoodPotential : 0; AcceptedFood = OwnedDomestic ? herd.AnimalFoodProduced : 0;
            Count = HerdCountKnowledge.Observe(herd.Animals, config, OwnedDomestic);
            Size = Count.Representative < config.HerdSmallCountThreshold ? 1 : Count.Representative <= 20 ? 2 : 3;
        }
    }
    public sealed class AgentObservation
    {
        public const int SchemaVersion = 15;
        public readonly int Player, CampId, CampTile, CampPopulation, GathererPopulation, LinkedPopulation, FoodCondition, DeficitTurns, Turn, PolicySeed;
        public readonly double HuntingFieldcraft, HuntingInclination, GatheringInclination, FishingInclination, Days;
        public readonly IReadOnlyList<SettlementObservation> Settlements;
        public readonly bool Watercraft;
        public readonly IReadOnlyDictionary<int, TileObservation> Tiles;
        public readonly IReadOnlyDictionary<int, double> RecentOwnExtraction;
        public readonly IReadOnlyList<UnitObservation> Hunters, Rivals, ForeignHunters;
        public readonly IReadOnlyList<HerdObservation> Herds;
        public readonly IReadOnlyList<SiteCoverageObservation> SiteCoverage;
        public readonly IReadOnlyList<AgriculturalSiteObservation> AgriculturalSites;
        public readonly IReadOnlyDictionary<int, int> LikelyHerdDestinations;
        public readonly IReadOnlyList<double> Experience;
        public readonly IReadOnlyList<TechnologyStatus> TechnologyStatuses;
        public readonly HexMap Map;
        public readonly int MinimumCamp, HunterMovement, CampMovement, WaterMovementCost, SupplyRange, SustainableHuntPopulation;
        public readonly double Inertia;
        internal AgentObservation(WorldState w, Camp camp, PlanningIndex index, bool recommendEconomy,
            bool includeCandidateForecasts = true)
        {
            var visible = index.Visible(camp.Player);
            Days = w.Days; Settlements = Array.AsReadOnly(index.OwnCamps(camp.Player).Select(c => new SettlementObservation(c, index, recommendEconomy)).ToArray());
            var preferences = camp.People.Policy; HuntingInclination = preferences.HuntingInclination; GatheringInclination = preferences.GatheringInclination; FishingInclination = preferences.FishingInclination;
            Player = camp.Player; CampId = camp.Id; CampTile = camp.Tile; CampPopulation = camp.Population; GathererPopulation = camp.GathererPopulation; LinkedPopulation = index.Population(camp);
            FoodCondition = Knowledge.Band(camp.Food, Math.Max(1, LinkedPopulation * w.Config.Consumption * w.Config.GenerationIntervals), w.Config);
            DeficitTurns = camp.DeficitTurns; Turn = w.Turn; PolicySeed = camp.People.PolicySeed;
            Watercraft = camp.Technologies[(int)Technology.Watercraft]; Map = w.Map;
            WaterMovementCost = Math.Max(1, w.Config.HunterMovement / w.Config.WaterMovement); MinimumCamp = w.Config.MinimumCamp; HunterMovement = w.Config.HunterMovement; CampMovement = w.Config.CampMovement; Inertia = w.Config.RelocationInertia;
            SupplyRange = w.Config.SupplyRange; SustainableHuntPopulation = w.Config.SustainableHuntPopulation;
            Tiles = new ReadOnlyDictionary<int, TileObservation>(visible.OrderBy(n => n).ToDictionary(n => n, n => new TileObservation(w, w.Tiles[n], w.Rivers, index.Ecology(n))));
            Hunters = Array.AsReadOnly(index.OwnHunters(Player).Select(h => new UnitObservation(h.Id, h.Tile, h.Population, h.Player, Supply.Connected(w, h), Knowledge.Band(h.CarriedFood, Supply.Capacity(w, h), w.Config), h.DeficitTurns, h.SourceCamp)).ToArray());
            Rivals = Array.AsReadOnly(index.VisibleCamps(visible).Where(c => c.Player != Player).Select(c => new UnitObservation(c.Id, c.Tile, 0, c.Player)).ToArray());
            ForeignHunters = Array.AsReadOnly(index.VisibleHunters(visible).Where(h => h.Player != Player).Select(h => new UnitObservation(h.Id, h.Tile, 0, h.Player)).ToArray());
            Herds = Array.AsReadOnly(index.VisibleHerds(visible).Select(h => new HerdObservation(h, w.Config, h.DomesticCamp < 0 ? -1 : index.Camps[h.DomesticCamp].Player, Player)).ToArray());
            SiteCoverage = Array.AsReadOnly(includeCandidateForecasts && CultivationEconomy.HusbandryEnabled(camp)
                ? w.Map.Radius(camp.Tile, CampMovement).Where(visible.Contains).OrderBy(id => id)
                    .Select(id => Coverage(w, camp, index, visible, id, Hunters, Herds)).ToArray()
                : Array.Empty<SiteCoverageObservation>());
            AgriculturalSites = Array.AsReadOnly(includeCandidateForecasts &&
                (!camp.IsVillage || FoodCondition <= 1 || DeficitTurns > 0) &&
                CultivationEconomy.Enabled(camp) && !CultivationEconomy.HusbandryEnabled(camp)
                ? AgriculturalCandidates(w, camp, index, visible)
                : Array.Empty<AgriculturalSiteObservation>());
            HuntingFieldcraft = PracticeEffects.Ramp(w.Config, camp, Domain.Hunting);
            var observedHunters = Hunters.Concat(ForeignHunters).ToArray();
            LikelyHerdDestinations = new ReadOnlyDictionary<int, int>(HuntingFieldcraft <= 0 ? new Dictionary<int, int>() : Herds.Where(h => !h.Domestic).ToDictionary(h => h.Id, h => PredictHerdDestination(h, Tiles, observedHunters, Map)));
            var ownIds = new HashSet<int>(Hunters.Select(h => h.Id)) { CampId };
            var recent = w.History.LastOrDefault();
            if (recent != null) ownIds.UnionWith(recent.Logistics.Where(l => l.Camp == CampId).Select(l => l.Hunter));
            RecentOwnExtraction = new ReadOnlyDictionary<int, double>(index.Extraction(ownIds).Where(t => visible.Contains(t.Tile)).GroupBy(t => t.Tile).ToDictionary(g => g.Key, g => g.Sum(t => t.Food)));
            Experience = Array.AsReadOnly((double[])camp.Experience.Clone());
            TechnologyStatuses = Array.AsReadOnly(TechnologyDiscovery.Statuses(w, camp.People));
        }
        private static SiteCoverageObservation Coverage(WorldState world, Camp camp, PlanningIndex index, HashSet<int> visible, int site,
            IReadOnlyList<UnitObservation> ownHunters, IReadOnlyList<HerdObservation> observedHerds)
        {
            int people = camp.Population + ownHunters.Where(h => h.SourceCamp == camp.Id &&
                world.Map.Distance(site, h.Tile) <= world.Config.SupplyRange).Sum(h => h.Population);
            double forageNeed = 0, waterNeed = 0;
            foreach (var herd in observedHerds.Where(h => h.OwnedDomestic && h.DomesticCamp == camp.Id && h.Count.Representative > 0))
            {
                var species = world.Config.GetSpecies(herd.Species);
                forageNeed += herd.Count.Representative * species.FoodConsumption * world.Config.GenerationIntervals;
                waterNeed += herd.Count.Representative * species.FreshwaterConsumption * world.Config.GenerationIntervals;
            }
            return LocalCoverage(world, camp, index, visible, site, people, forageNeed, waterNeed);
        }
        internal static SiteCoverageObservation LocalCoverage(WorldState world, Camp camp, PlanningIndex index,
            HashSet<int> visible, int site, int people, double forageNeed, double waterNeed)
        {
            double food = Math.Max(0, camp.Food), forage = 0, water = 0;
            var exposure = new SeasonExposure(world.Days, world.Config.TurnDays, world.Config.BaseDays);
            foreach (int id in world.Map.Radius(site, 1).Where(visible.Contains).OrderBy(id => id))
            {
                var tile = world.Tiles[id]; var stock = index.Ecology(id);
                if (tile.TraversableLand)
                {
                    food += Math.Max(0, stock.Food);
                    forage += Math.Max(0, stock.PotentialFood);
                }
                else if (tile.Terrain == Terrain.Shallows) food += Math.Max(0, stock.Food);
                if (tile.ElevationLevel >= 10 || tile.WaterBody == WaterKind.Saltwater || tile.FreshwaterSource == FreshwaterSource.None) continue;
                double recharge = 0;
                for (int season = 0; season < 4; season++)
                    recharge += Math.Max(0, tile.Seasons[season].MaxFreshwater * tile.Seasons[season].WaterRecoveryPerDay)
                        * exposure.At(season);
                water += Math.Max(0, stock.Freshwater) + recharge;
            }
            double humanNeed = Math.Max(1, people * world.Config.Consumption * world.Config.GenerationIntervals);
            double Quantize(double ratio) => Math.Round(Numbers.Clamp(ratio, 0, 3) * 20, MidpointRounding.AwayFromZero) / 20;
            return new SiteCoverageObservation(site, Quantize(food / humanNeed),
                forageNeed <= 0 ? 3 : Quantize(forage / forageNeed),
                waterNeed <= 0 ? 3 : Quantize(water / waterNeed));
        }
        private static AgriculturalSiteObservation[] AgriculturalCandidates(WorldState world, Camp camp,
            PlanningIndex index, HashSet<int> visible)
        {
            var config = world.Config;
            var owned = world.Map.Radius(camp.Tile, 1).Where(visible.Contains)
                .Where(id => world.Tiles[id].CultivationOperator == camp.Id && world.Tiles[id].TraversableLand)
                .OrderBy(id => id).ToArray();
            // This is the same pure staffing/tool calculation used by field
            // preparation. A saved operating fraction is only last turn's
            // outcome and can be stale after workforce or tool changes.
            var labor = CultivationEconomy.CalculateLabor(world, camp, owned,
                new FarmToolWorkBudget(world, camp), config.TurnDays / config.DaysPerYear);
            double operating = labor.RequiredMaintenance > 0
                ? Numbers.Clamp(labor.EffectiveMaintenanceFarmers / labor.RequiredMaintenance, 0, 1)
                : labor.EffectiveMaintenanceFarmers > 0 ? 1 : 0;
            var values = new Dictionary<int, (double recovery, double rebuild)>();
            double foodNeed = Math.Max(1, camp.Population * config.Consumption * config.GenerationIntervals * 3);
            double expectedClaim = Math.Max(0, foodNeed - camp.Food) / 3 / Math.Max(1, owned.Length);
            double horizonYears = 3 * config.TurnDays / config.DaysPerYear;
            foreach (int id in owned)
            {
                var tile = world.Tiles[id];
                var view = index.Ecology(id);
                double capital = Math.Max(0, view.CultivationCapital);
                if (capital <= 1e-12) continue;
                double recovery = 0, fieldStock = view.Food, naturalStock = view.Food;
                var natural = view; natural.CultivationCapital = 0;
                double projectedCapital = capital;
                for (int turn = 0; turn < 3; turn++)
                {
                    double start = world.Days + turn * config.TurnDays;
                    var field = view; field.CultivationCapital = projectedCapital;
                    var cultivated = new HorizonEcologyCapacity(world, tile, field, start, config.TurnDays, operating);
                    var uncultivated = new HorizonEcologyCapacity(world, tile, natural, start, config.TurnDays, 0);
                    fieldStock = Math.Min(Math.Max(0, fieldStock), Math.Max(0, cultivated.Food));
                    naturalStock = Math.Min(Math.Max(0, naturalStock), Math.Max(0, uncultivated.Food));
                    // Claims settle against opening stock. Recovery can only
                    // improve a later turn, so a full and unworked field does
                    // not receive credit for impossible gross recharge.
                    double fieldHarvest = Math.Min(expectedClaim, fieldStock);
                    double naturalHarvest = Math.Min(expectedClaim, naturalStock);
                    recovery += Math.Max(0, fieldHarvest - naturalHarvest);
                    fieldStock = EcologyMath.ManagedStock(fieldStock - fieldHarvest,
                        cultivated.Food, cultivated.FoodRate, cultivated.ManagedFlow, 0, config.TurnDays).Stock;
                    naturalStock = EcologyMath.Stock(naturalStock - naturalHarvest,
                        uncultivated.Food, uncultivated.FoodRate, 0, config.TurnDays).Stock;
                    projectedCapital = CultivationEconomy.Decay(config, projectedCapital, operating,
                        config.TurnDays / config.DaysPerYear);
                }
                // Only the difference between maintained and abandoned
                // capital is a relocation loss. A field can be reclaimed, and
                // without current labor it would decay even if camp stayed.
                double maintained = CultivationEconomy.Decay(config, capital, operating, horizonYears);
                double abandoned = CultivationEconomy.Decay(config, capital, 0, horizonYears);
                double bridgeCapital = Math.Pow(config.GathererBridgeCeiling / config.ReferenceIntensity, 2);
                double openEffort = CultivationEconomy.AdditionalEffort(config,
                    Math.Min(abandoned, bridgeCapital), Math.Min(maintained, bridgeCapital));
                double woodedEffort = CultivationEconomy.AdditionalEffort(config,
                    Math.Max(abandoned, bridgeCapital), Math.Max(maintained, bridgeCapital));
                bool wooded = view.CurrentVegetation >= VegetationTier.Medium &&
                    (tile.Environment == EnvironmentKind.Temperate || tile.Environment == EnvironmentKind.Tropical || tile.Environment == EnvironmentKind.Cold);
                double buildFactor = !wooded ? config.Neolithic.OpenBuild :
                    view.CurrentVegetation == VegetationTier.High ? config.Neolithic.ForestBuild : config.Neolithic.WoodlandBuild;
                double rebuild = config.Neolithic.BuildWorkerYearsPerTileE *
                    (openEffort * config.Neolithic.OpenBuild + woodedEffort * buildFactor);
                values[id] = (operating <= 1e-12 ? 0 : recovery, rebuild);
            }
            // Express lost effort in turns of this camp's entire labor force.
            // Dividing by three would underprice a rebuild whose farmers must
            // compete with immediate food work after a move.
            double laborYears = Math.Max(1, camp.Population * config.TurnDays / config.DaysPerYear);
            double Quantize(double value, double cap) => Math.Round(Numbers.Clamp(value, 0, cap) * 20, MidpointRounding.AwayFromZero) / 20;
            return world.Map.Radius(camp.Tile, config.CampMovement).Where(visible.Contains).OrderBy(id => id)
                .Select(site =>
                {
                    var retained = values.Where(p => world.Map.Distance(site, p.Key) <= 1).ToArray();
                    double lost = values.Where(p => world.Map.Distance(site, p.Key) > 1).Sum(p => p.Value.rebuild);
                    return new AgriculturalSiteObservation(site, retained.Length,
                        Quantize(retained.Sum(p => p.Value.recovery) / foodNeed, 10),
                        Quantize(lost / laborYears, 10));
                }).ToArray();
        }
        private static int PredictHerdDestination(HerdObservation herd, IReadOnlyDictionary<int, TileObservation> tiles, IReadOnlyList<UnitObservation> hunters, HexMap map)
        {
            var candidates = map.Radius(herd.Tile, 1).Where(n => tiles.TryGetValue(n, out var tile) && tile.Land && tile.ElevationLevel < 10).ToArray();
            if (candidates.Length == 0) return herd.Tile;
            bool threatened = hunters.Any(h => map.Distance(herd.Tile, h.Tile) <= 1);
            if (threatened)
                return candidates.OrderByDescending(n => hunters.Min(h => map.Distance(n, h.Tile))).ThenByDescending(n => tiles[n].Freshwater).ThenByDescending(n => tiles[n].PotentialFood).ThenBy(n => n).First();
            var current = tiles[herd.Tile];
            if (current.Freshwater >= 2 && current.PotentialFood >= 2) return herd.Tile;
            return candidates.OrderByDescending(n => tiles[n].Freshwater + tiles[n].PotentialFood).ThenBy(n => n == herd.Tile ? 0 : 1).ThenBy(n => n).First();
        }
    }
    public sealed class AgentObservationBuilder
    {
        public AgentObservation Build(WorldState world, int player, PlanningIndex index = null,
            bool recommendEconomy = false, bool includeCandidateForecasts = true)
        { index = index ?? new PlanningIndex(world); return new AgentObservation(world, index.OwnCamps(player).First(), index, recommendEconomy, includeCandidateForecasts); }
    }
    [Serializable]
    public sealed class CandidateTrace
    {
        public string Plan, Components;
        public double Score;
        public bool Selected;
    }
    [Serializable]
    public sealed class DecisionTrace
    {
        public int Player, Turn;
        public List<CandidateTrace> Candidates = new List<CandidateTrace>();
    }
    public sealed class PolicyDecision
    {
        public List<Order> Orders = new List<Order>();
        public DecisionTrace Trace = new DecisionTrace();
    }
    public interface IAgentPolicy { PolicyDecision Decide(AgentObservation observation, Mandate mandate); }
    public static class ManagedBreedingPolicy
    {
        public static double Recommend(AgentObservation o, SettlementObservation settlement, int? campTile = null)
        {
            var herds = o.Herds.Where(h => h.OwnedDomestic && h.DomesticCamp == settlement.Id && (h.Count.Representative > 0 || h.StartingAnimals > 0)).ToArray();
            if (herds.Length == 0) return .55;
            double starting = herds.Sum(h => Math.Max(0, h.StartingAnimals));
            double loss = starting <= 0 ? 0 : herds.Sum(h => Math.Max(0, h.StartingAnimals - h.SurvivingAnimals)) / starting;
            double nutrition = herds.Min(h => h.Nutrition), management = herds.Min(h => h.ManagementFulfillment);
            double burden = settlement.Population <= 0 ? 1 : settlement.Economy.Assignments.Where(a => a.Role == WorkforceRoles.Herder).Sum(a => a.Population) / (double)settlement.Population;
            // Use the same radius-one support represented in the movement
            // observation; a depleted occupied tile alone is not a shortage
            // when neighboring pastures and springs can feed the herd.
            var coverage = o.SiteCoverage.FirstOrDefault(s => s.Tile == (campTile ?? settlement.Tile));
            var neighbors = o.Map.Radius(campTile ?? settlement.Tile, 1).Where(id => id != (campTile ?? settlement.Tile) &&
                o.Tiles.TryGetValue(id, out var t) && t.Land && t.ElevationLevel < 10).Select(id => o.Tiles[id]).ToArray();
            bool room = neighbors.Count(t => t.PotentialFood >= 2 && t.Freshwater >= 2) >= 3;
            bool adequate = coverage != null && coverage.HerdForage >= 1 && coverage.HerdWater >= 1 && room;
            bool ample = coverage != null && coverage.HerdForage >= 1.25 && coverage.HerdWater >= 1.25 && room;
            return Choose(loss, nutrition, management, burden, adequate, ample, settlement.RecentlyMoved);
        }
        internal static double Recommend(WorldState world, Camp camp, int campTile, PlanningIndex index)
        {
            // Interturn automation needs only this camp's own herd diagnostics
            // and visible radius-one support. Rebuilding a full observation
            // per camp would repeat an empire's entire visibility footprint.
            var herds = index.CampHerds(camp.Id).Where(h => h.Animals > 0 || h.StartingAnimals > 0).ToArray();
            if (herds.Length == 0) return .55;
            double starting = herds.Sum(h => Math.Max(0, h.StartingAnimals));
            double loss = starting <= 0 ? 0 : herds.Sum(h => Math.Max(0, h.StartingAnimals - h.SurvivingAnimals)) / starting;
            double nutrition = herds.Min(h => h.LastNutrition), management = herds.Min(h => h.ManagementFulfillment);
            double burden = camp.Population <= 0 ? 1 : camp.Workforce.Assignments
                .Where(a => a.Role == WorkforceRoles.Herder).Sum(a => a.Population) / (double)camp.Population;
            var visible = index.VisibleCached(camp.Player);
            double forageNeed = 0, waterNeed = 0;
            foreach (var herd in herds.Where(h => h.Animals > 0))
            {
                var species = world.Config.GetSpecies(herd.Species);
                forageNeed += herd.Animals * species.FoodConsumption * world.Config.GenerationIntervals;
                waterNeed += herd.Animals * species.FreshwaterConsumption * world.Config.GenerationIntervals;
            }
            int people = camp.Population + index.CampHunters(camp.Id)
                .Where(h => world.Map.Distance(campTile, h.Tile) <= world.Config.SupplyRange).Sum(h => h.Population);
            var coverage = AgentObservation.LocalCoverage(world, camp, index, visible, campTile, people, forageNeed, waterNeed);
            int room = 0;
            foreach (int id in world.Map.Radius(campTile, 1).Where(id => id != campTile && visible.Contains(id)))
            {
                var tile = world.Tiles[id];
                if (!tile.TraversableLand) continue;
                var stock = index.Ecology(id);
                if (Knowledge.Band(stock.PotentialFood, Math.Max(1, tile.Seasons[world.Season].MaxPotentialFood), world.Config) >= 2 &&
                    Knowledge.Band(stock.Freshwater, 70, world.Config) >= 2) room++;
            }
            bool adequate = coverage.HerdForage >= 1 && coverage.HerdWater >= 1 && room >= 3;
            bool ample = coverage.HerdForage >= 1.25 && coverage.HerdWater >= 1.25 && room >= 3;
            return Choose(loss, nutrition, management, burden, adequate, ample, camp.Moves > 0 && camp.StationaryTurns == 0);
        }
        private static double Choose(double loss, double nutrition, double management, double burden,
            bool adequate, bool ample, bool recentlyMoved)
        {
            if (management >= .8 && burden <= .6 && ample && (loss >= .25 || recentlyMoved)) return .65;
            if (nutrition < .95 || management < .8 || burden > .6 || !adequate) return .45;
            return .55;
        }
    }
    public sealed class ScriptedTestPolicy : IAgentPolicy
    {
        private readonly Func<AgentObservation, List<Order>> script;
        public ScriptedTestPolicy(Func<AgentObservation, List<Order>> script) { this.script = script; }
        public PolicyDecision Decide(AgentObservation observation, Mandate mandate) => new PolicyDecision { Orders = script(observation) };
    }
    public sealed class HeuristicTribePolicy : IAgentPolicy
    {
        private sealed class Candidate
        {
            public Order Order;
            public double Score;
            public string Components;
        }
        private static double Gathering(AgentObservation o, int tile)
        {
            double score = 0;
            foreach (int n in o.Map.Radius(tile, 1))
                if (o.Tiles.TryGetValue(n, out var t) && t.Terrain != Terrain.Ocean) score += t.Food * .22 + t.Productivity * .12 + t.Condition * .04;
            return score;
        }
        private static double HerdSupport(AgentObservation o, int tile) => o.Herds.Where(h => !h.Domestic).Select(h => h.Size * .9 + Math.Max(0, 4 - o.Map.Distance(tile, h.Tile)) * .65).DefaultIfEmpty(0).Max();
        private static double FishingSupport(AgentObservation o, int tile) => o.Map.Radius(tile, 1).Where(n => o.Tiles.TryGetValue(n, out var observed) && observed.Aquatic).Select(n => o.Tiles[n].Food * .8 + o.Tiles[n].Productivity * .2 + o.Tiles[n].Condition * .1).DefaultIfEmpty(0).Max();
        private static bool Enter(AgentObservation o, int tile, bool hunter) => o.Tiles.TryGetValue(tile, out var t) && t.ElevationLevel < 10 && (t.Land || hunter && o.Watercraft && t.Terrain == Terrain.Shallows);
        private static bool Reach(AgentObservation o, int start, int target, bool hunter) => o.Map.Reachable(start, target, hunter ? o.HunterMovement : o.CampMovement, n => Enter(o, n, hunter), n => o.Tiles[n].Aquatic ? 2 : 1);
        private static double Practice(AgentObservation o, Domain domain, Mandate m) => o.Experience[(int)domain] / Math.Max(1, o.Experience.Sum()) * m.Persistence;
        public PolicyDecision Decide(AgentObservation o, Mandate m)
        {
            var result = new PolicyDecision { Trace = new DecisionTrace { Player = o.Player, Turn = o.Turn } };
            if (o.LinkedPopulation <= 0) return result;
            bool knownFishingWater = o.Tiles.Values.Any(tile => tile.Aquatic && tile.Food >= 1);
            double huntPreference = m.Hunting * o.HuntingInclination + Practice(o, Domain.Hunting, m);
            double gatherPreference = m.Gathering * o.GatheringInclination + Practice(o, Domain.Gathering, m);
            double fishPreference = knownFishingWater ? m.Fishing * o.FishingInclination + Practice(o, Domain.Fishing, m) : 0;
            bool huntingFocus = huntPreference > Math.Max(gatherPreference, fishPreference);
            bool fishingFocus = fishPreference > Math.Max(huntPreference, gatherPreference);
            double need = 1 + o.DeficitTurns * .5 + (o.FoodCondition <= 1 ? .5 : 0);
            var economicCamp = o.Settlements.FirstOrDefault(c => c.Id == o.CampId && (c.Agriculture || c.Husbandry));
            var campCandidates = new List<Candidate>();
            double current = Gathering(o, o.CampTile);
            if (economicCamp != null && economicCamp.Agriculture && !economicCamp.Husbandry)
            {
                foreach (var site in o.AgriculturalSites.OrderBy(s => s.Tile))
                {
                    int n = site.Tile;
                    if (!Reach(o, o.CampTile, n, false) || o.Rivals.Any(r => r.Tile == n)) continue;
                    // Departure from an established village abandons its
                    // current field work and fixed housing. AI considers it
                    // only in a food emergency with a materially better site;
                    // a player can still choose the move at any time.
                    if (economicCamp.IsVillage && n != o.CampTile &&
                        (o.FoodCondition > 1 && o.DeficitTurns == 0 ||
                         Gathering(o, n) < current * 1.25)) continue;
                    double food = Gathering(o, n) * need * m.Gathering;
                    double continuity = n == o.CampTile ? Math.Max(.25, current * o.Inertia) : -(1 - m.Mobility) * .1;
                    double herdSupport = HerdSupport(o, n) * m.Hunting * (huntingFocus ? 4 : .35 + .85 * o.HuntingFieldcraft);
                    double fishingSupport = FishingSupport(o, n) * m.Fishing * (fishingFocus ? 3 : .2);
                    // Three forthcoming turns of managed recovery are a
                    // forward benefit of retained own fields. Rebuilding
                    // abandoned capital is charged using the same soft-effort
                    // curve as field construction, normalized to local labor.
                    double fieldPayoff = 4 * site.ManagedRecoveryCoverage - 3 * site.LostRebuildBurden;
                    campCandidates.Add(new Candidate { Order = n == o.CampTile ? new Order(OrderKind.NoOp, o.CampId) : new Order(OrderKind.Move, o.CampId, destination: n),
                        Score = food + continuity + herdSupport + fishingSupport + fieldPayoff,
                        Components = "GatheringOpportunity + ManagedFieldRecovery - LostFieldRebuildEffort + SurvivalNeed + RelocationInertia" });
                }
            }
            else if (economicCamp != null && economicCamp.Husbandry)
            {
                // The observation contains only rounded support ratios for
                // visible local sites. The weakest need leads the decision;
                // once all needs are covered, require a meaningful gain before
                // relocating so a camp can let its pasture rest.
                var currentCoverage = o.SiteCoverage.First(s => s.Tile == o.CampTile);
                double Support(SiteCoverageObservation s) => Math.Min(s.HumanFood, Math.Min(s.HerdForage, s.HerdWater));
                double currentSupport = Support(currentCoverage);
                double currentTotal = currentCoverage.HumanFood + currentCoverage.HerdForage + currentCoverage.HerdWater;
                double margin = currentCoverage.HumanFood < 1 || currentCoverage.HerdForage < 1 || currentCoverage.HerdWater < 1 ? 0 : .05;
                foreach (var site in o.SiteCoverage.OrderBy(s => s.Tile))
                {
                    int n = site.Tile;
                    if (!Reach(o, o.CampTile, n, false) || o.Rivals.Any(r => r.Tile == n)) continue;
                    double support = Support(site);
                    double total = site.HumanFood + site.HerdForage + site.HerdWater;
                    if (n != o.CampTile && (margin == 0
                        ? support + 1e-9 < currentSupport || support <= currentSupport + 1e-9 && total <= currentTotal + 1e-9
                        : support + 1e-9 < currentSupport + margin)) continue;
                    // Small secondary weights distinguish equal weakest-need
                    // sites without permitting a worse bottleneck to win.
                    double score = support + .001 * total;
                    if (n == o.CampTile) score += margin;
                    campCandidates.Add(new Candidate { Order = n == o.CampTile ? new Order(OrderKind.NoOp, o.CampId) : new Order(OrderKind.Move, o.CampId, destination: n), Score = score,
                        Components = "FilteredHumanFoodCoverage + FilteredHerdForageCoverage + FilteredHerdWaterCoverage + RelocationInertia" });
                }
            }
            else
            {
                foreach (int n in o.Map.Radius(o.CampTile, o.CampMovement).OrderBy(n => n))
                {
                    if (!Reach(o, o.CampTile, n, false) || o.Rivals.Any(r => r.Tile == n)) continue;
                    double food = Gathering(o, n) * need * m.Gathering;
                    double continuity = n == o.CampTile ? Math.Max(.25, current * o.Inertia) : -(1 - m.Mobility) * .1;
                    double herdSupport = HerdSupport(o, n) * m.Hunting * (huntingFocus ? 4 : .35 + .85 * o.HuntingFieldcraft);
                    double fishingSupport = FishingSupport(o, n) * m.Fishing * (fishingFocus ? 3 : .2);
                    campCandidates.Add(new Candidate { Order = n == o.CampTile ? new Order(OrderKind.NoOp, o.CampId) : new Order(OrderKind.Move, o.CampId, destination: n), Score = food + continuity + herdSupport + fishingSupport, Components = "GatheringOpportunity + HerdSupport + FishingSupport + SurvivalNeed + RelocationInertia" });
                }
            }
            var campChoice = Select(o, campCandidates, result.Trace);
            int plannedCamp = campChoice.Destination >= 0 ? campChoice.Destination : o.CampTile;
            bool huntOpportunity = o.Herds.Any(h => !h.Domestic && o.Map.Distance(o.CampTile, h.Tile) <= 2);
            bool fishingOpportunity = o.Map.Radius(o.CampTile, 2).Any(n => o.Tiles.TryGetValue(n, out var t) && t.Aquatic && t.Food >= 1);
            bool opportunity = huntingFocus || fishingFocus || huntOpportunity || fishingOpportunity;
            int assignedHunters = o.Hunters.Sum(h => h.Population);
            double hunterShare = huntingFocus ? .55 + m.Risk * .1 : fishingFocus ? .30 : huntOpportunity ? .35 + m.Risk * .15 : .25 + m.Risk * .1;
            int desiredHunters = opportunity ? Math.Max(8, (int)Math.Round(o.LinkedPopulation * hunterShare)) : 0;
            int dispatchable = Math.Min(o.GathererPopulation, o.CampPopulation - o.MinimumCamp - (huntingFocus ? 0 : 4));
            int allocation = Math.Min(8, Math.Min(Math.Max(0, desiredHunters - assignedHunters), dispatchable));
            if (economicCamp == null && (campChoice.Kind == OrderKind.NoOp || huntingFocus || fishingFocus && fishingOpportunity) && allocation >= 4 && opportunity && o.DeficitTurns == 0 && o.FoodCondition >= 1)
            { campChoice = new Order(OrderKind.Dispatch, o.CampId, population: allocation); result.Trace.Candidates.Add(new CandidateTrace { Plan = campChoice.ToString(), Components = "Growing population and visible subsistence opportunity; hunter share expands while preserving the camp reserve", Score = 1, Selected = true }); }
            // AI economic preparation is a distinct pass, even when its camp
            // movement is the submitted action. The persisted automation-Off
            // setting remains authoritative for player-issued movement.
            if (economicCamp != null) campChoice.RequestAutomaticEconomy = true;
            result.Orders.Add(campChoice);
            var observedHunters = o.Hunters.Concat(o.ForeignHunters).ToArray();
            var wildHerds = o.Herds.Where(h => !h.Domestic).OrderByDescending(h => h.Size).ThenBy(h => o.Map.Distance(o.CampTile, h.Tile)).ThenBy(h => h.Id).ToArray();
            int preferredHerd = wildHerds.Select(h => h.Id).DefaultIfEmpty(-1).First();
            int hunterIndex = 0;
            foreach (var hunter in o.Hunters)
            {
                int distributedHerd = wildHerds.Length == 0 ? -1 : wildHerds[hunterIndex++ % wildHerds.Length].Id;
                double logisticsUrgency = !hunter.InSupply ? hunter.Provisions <= 1 || hunter.DeficitTurns > 0 ? 4 : hunter.Provisions >= 3 ? 2.5 : .8 : 0;
                var candidates = new List<Candidate> { new Candidate { Order = new Order(OrderKind.NoOp, hunter.Id), Score = -.5, Components = "Wait" } };
                foreach (var herd in o.Herds.Where(h => !h.Domestic))
                {
                    // Hunters choose positions, not a separate attack order. Sharing a post-movement tile with wild prey triggers hunting automatically.
                    // Score every visible one-step herd destination so multiple groups can cover a migration rather than betting on one opaque prediction.
                    bool threatened = observedHunters.Any(h => o.Map.Distance(h.Tile, herd.Tile) <= 1);
                    int maximumEscape = threatened ? o.Map.Radius(herd.Tile, 1).Where(n => Enter(o, n, false)).Max(n => observedHunters.Min(h => o.Map.Distance(h.Tile, n))) : 0;
                    foreach (int target in o.Map.Radius(herd.Tile, 1).Where(n => Enter(o, n, false)).OrderBy(n => n))
                    {
                        if (!Reach(o, hunter.Tile, target, true)) continue;
                        double likely = threatened ? observedHunters.Min(h => o.Map.Distance(h.Tile, target)) == maximumEscape ? 2 + o.Tiles[target].Freshwater * .3 + o.Tiles[target].PotentialFood * .05 - target * 1e-6 : 0 : (target == herd.Tile ? .55 : .05) + o.Tiles[target].Freshwater * .035 + o.Tiles[target].PotentialFood * .025;
                        if (o.LikelyHerdDestinations.TryGetValue(herd.Id, out int tracked) && tracked == target) likely += .8 + 1.2 * o.HuntingFieldcraft;
                        var pursuit = target == hunter.Tile ? new Order(OrderKind.NoOp, hunter.Id) : new Order(OrderKind.Move, hunter.Id, destination: target);
                        double concentration = assignedHunters > o.SustainableHuntPopulation ? herd.Id == preferredHerd ? 2.5 : 0 : huntingFocus && wildHerds.Length > 1 && herd.Id == distributedHerd ? 2.5 : 0;
                        double huntingPreference = huntingFocus ? 1.6 : fishingFocus ? .15 : 1;
                        candidates.Add(new Candidate { Order = pursuit, Score = (2.3 + herd.Size * .35 + likely + concentration) * m.Hunting * huntingPreference + Practice(o, Domain.Hunting, m) - o.Map.Distance(hunter.Tile, target) * .15 - need * (1 - m.Risk) * .25, Components = "HuntOpportunity + MigrationCoverage + GroupConcentration + SpecializationContinuity - TravelCost - PopulationRisk" });
                    }
                }
                if (!huntingFocus)
                    foreach (int shore in o.Map.Radius(hunter.Tile, o.HunterMovement).OrderBy(n => n))
                    {
                        if (!Reach(o, hunter.Tile, shore, true)) continue;
                        foreach (int fish in o.Map.Radius(shore, 1))
                            if (o.Tiles.TryGetValue(fish, out var tile) && tile.Aquatic)
                                candidates.Add(new Candidate { Order = new Order(OrderKind.Fish, hunter.Id, fish, shore), Score = (tile.Food * .8 + tile.Productivity * .15) * m.Fishing * need * (fishingFocus ? 5 : 1) + Practice(o, Domain.Fishing, m) - o.Map.Distance(hunter.Tile, shore) * .12, Components = "FishingOpportunity + SurvivalNeed + SpecializationContinuity - TravelCost" });
                    }
                if (hunter.Tile == o.CampTile && plannedCamp == o.CampTile)
                    candidates.Add(new Candidate { Order = new Order(OrderKind.Reintegrate, hunter.Id, o.CampId), Score = o.DeficitTurns > 0 ? 3.3 : huntingFocus ? -1 : 1.4, Components = "ReintegrationValue + PopulationRisk" });
                foreach (int n in o.Map.Radius(hunter.Tile, o.HunterMovement).OrderBy(n => n))
                {
                    if (n == hunter.Tile || !Reach(o, hunter.Tile, n, true)) continue;
                    double returnValue = o.Map.Distance(hunter.Tile, plannedCamp) - o.Map.Distance(n, plannedCamp);
                    int unknown = o.Map.Radius(n, 2).Count(t => !o.Tiles.ContainsKey(t));
                    bool nomadicScouting = huntingFocus && wildHerds.Length == 0 && (hunter.InSupply || hunter.Provisions >= 2);
                    double returnWeight = nomadicScouting ? .1 : .5 + logisticsUrgency;
                    double explorationWeight = nomadicScouting ? .18 : .035;
                    double waterSearch = fishingFocus ? FishingSupport(o, n) * 1.5 : 0;
                    candidates.Add(new Candidate { Order = new Order(OrderKind.Move, hunter.Id, destination: n), Score = returnValue * returnWeight + unknown * explorationWeight * m.Exploration + waterSearch - need * .15, Components = "SupplyReturn + ReintegrationValue + ExplorationValue + FishingSupport - SurvivalNeed" });
                }
                result.Orders.Add(Select(o, candidates, result.Trace));
            }
            return result;
        }
        private static Order Select(AgentObservation o, List<Candidate> candidates, DecisionTrace trace)
        {
            candidates = candidates.Where(c => OrderRules.Validate(o, c.Order) == null).ToList();
            if (candidates.Count == 0) return new Order(OrderKind.NoOp, o.CampId);
            var best = candidates.OrderByDescending(c => c.Score).ThenBy(c => Numbers.Hash(o.PolicySeed, 91, c.Order.Entity ^ c.Order.Target ^ c.Order.Destination, o.Turn)).ThenBy(c => c.Order.ToString(), StringComparer.Ordinal).First();
            foreach (var c in candidates) trace.Candidates.Add(new CandidateTrace { Plan = c.Order.ToString(), Score = c.Score, Components = c.Components, Selected = c == best });
            return best.Order;
        }
    }
}
