using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Serialization;

namespace Anthropocene.Simulation
{
    [Serializable]
    public sealed class GenerationReport
    {
        public int GeneratorVersion = WorldState.GeneratorVersion, LandTiles, Islands, Continents, Rivers;
        public double MinElevation, MaxElevation, MeanTemperature, MeanMoisture, CoastFraction, MeanStartViability;
        public string ConfigurationHash;
        public List<string> Stages = new List<string>();
        public List<double> Milliseconds = new List<double>();
        public List<string> Warnings = new List<string>();
        public int[] BiomeCounts = new int[10];
        public int[] ReliefCounts = new int[4];
        public int[] ElevationCounts = new int[11];
        public int OasisCount, WildHerds, CampsWithVisibleHerds, FreshwaterTiles, SaltwaterTiles, RiverEdges, CamelHerds, CamelsNearOases;
        [IgnoreDataMember]
        public float[] AncientOrogenUplift;
        [IgnoreDataMember]
        public float[] AncientOrogenAge;
        [IgnoreDataMember]
        public float[] AncientOrogenStrength;
    }
    public interface IWorldGenerationStage
    {
        string Name { get; }
        string Requires { get; }
        string Produces { get; }
        void Generate(GenerationContext context);
    }
    public sealed class GenerationContext
    {
        public WorldState World;
        public HexMap Map;
        public SimulationConfig Config => World.Config;
        public int[] PlateCenters;
        public double[] PlateX, PlateY;
        public bool[] Continental;
        internal int[] OtherPlate;
        internal float[] BoundaryDistance;
        public float[] AncientOrogenStrength;
        public float[] AncientOrogenAge;
        public ResourceGenerationOptions ResourceOptions = new ResourceGenerationOptions();
    }
    public sealed class WorldGenerator
    {
        public WorldState Generate(int seed, SimulationConfig config, ResourceGenerationOptions resources = null)
        {
            config.Validate();
            var world = new WorldState { Seed = seed, Config = config, Tiles = new Tile[config.Width * config.Height], Generation = new GenerationReport() };
            // Hash all effective tuning, not just the preset name.
            world.Generation.ConfigurationHash = SaveCodec.ConfigurationHash(config);
            var context = new GenerationContext
            {
                World = world, Map = world.Map, ResourceOptions = resources ?? new ResourceGenerationOptions(),
                AncientOrogenStrength = new float[world.Tiles.Length], AncientOrogenAge = new float[world.Tiles.Length]
            };
            context.ResourceOptions.Validate();
            var stages = CreateStages();
            var total = Stopwatch.StartNew();
            foreach (var stage in stages)
            {
                var timer = Stopwatch.StartNew(); stage.Generate(context);
                world.Generation.Stages.Add(stage.Name); world.Generation.Milliseconds.Add(timer.Elapsed.TotalMilliseconds);
            }
            Ownership.Bind(world);
            Validate(world);
            if (world.Resources != null) MaterialAccessIndex.For(world);
            if (total.Elapsed.TotalSeconds > config.GenerationBudgetSeconds) world.Generation.Warnings.Add("Generation exceeded configured time budget.");
            if (GC.GetTotalMemory(false) / 1048576 > config.GenerationBudgetMegabytes) world.Generation.Warnings.Add("Managed memory exceeds configured generation budget.");
            return world;
        }
        public static IWorldGenerationStage[] CreateStages(bool placement = true)
        {
            var stages = new List<IWorldGenerationStage> { new GeometryStage(), new PlateStage(), new ElevationStage(), new ClimateStage(), new HydrologyStage(), new BoundaryRiverStage(), new ElevationLevelStage(), new EcologyStage(), new OasisStage(), new VegetationResourceStage(), new GeologicalProvinceStage(), new MineralDepositStage(), new SurfaceMaterialStage() };
            if (placement) stages.Add(new PlacementStage());
            stages.Add(new ResourceAccessAuditStage());
            return stages.ToArray();
        }
        private static void Validate(WorldState world)
        {
            var report = world.Generation;
            report.MinElevation = world.Tiles.Min(t => t.Elevation); report.MaxElevation = world.Tiles.Max(t => t.Elevation);
            report.MeanTemperature = world.Tiles.Average(t => t.Seasons.Average(s => (double)s.Temperature));
            report.MeanMoisture = world.Tiles.Average(t => t.Seasons.Average(s => (double)s.Moisture));
            foreach (var tile in world.Tiles)
            {
                report.BiomeCounts[(int)tile.Terrain]++; report.ElevationCounts[tile.ElevationLevel]++;
                if (tile.Land) report.ReliefCounts[(int)tile.Relief]++;
                if (tile.Oasis) report.OasisCount++;
                if (tile.DrinkableWater > 0) report.FreshwaterTiles++;
                if (tile.Saltwater > 0) report.SaltwaterTiles++;
                if (tile.Land) report.LandTiles++;
                if (tile.River) report.Rivers++;
                foreach (var season in tile.Seasons)
                    if (float.IsNaN(season.MaxFood) || float.IsInfinity(season.MaxFood) || season.MaxFood < 0 ||
                        float.IsNaN(season.MaxPotentialFood) || float.IsInfinity(season.MaxPotentialFood) || season.MaxPotentialFood < season.MaxFood || season.MaxFreshwater < 0 ||
                        float.IsNaN(season.RegionalMoisture) || season.RegionalMoisture < 0 || season.RegionalMoisture > 1 ||
                        float.IsNaN(season.Moisture) || season.Moisture < season.RegionalMoisture || season.Moisture > 1)
                        throw new InvalidOperationException("Invalid generated ecology at " + tile.Id);
                if (double.IsNaN(tile.Food) || double.IsInfinity(tile.Food) || double.IsNaN(tile.PotentialFood) || double.IsInfinity(tile.PotentialFood) || tile.Food < 0 || tile.PotentialFood < 0 || tile.PotentialFood + 1e-6 < tile.Food)
                    throw new InvalidOperationException("Invalid generated food stock at " + tile.Id);
                if (tile.Downstream >= 0 && world.Tiles[tile.Downstream].Elevation > tile.Elevation + .00001)
                    throw new InvalidOperationException("River flows uphill at " + tile.Id);
            }
            var map = world.Map; var visited = new bool[world.Tiles.Length]; int coast = 0;
            foreach (var tile in world.Tiles)
            {
                if (!tile.Land) continue;
                if (map.Neighbors(tile.Id).Any(n => !world.Tiles[n].Land)) coast++;
                if (visited[tile.Id]) continue;
                int count = 0; var queue = new Queue<int>(); queue.Enqueue(tile.Id); visited[tile.Id] = true;
                while (queue.Count > 0)
                {
                    int n = queue.Dequeue(); count++;
                    foreach (int other in map.Neighbors(n))
                        if (!visited[other] && world.Tiles[other].Land) { visited[other] = true; queue.Enqueue(other); }
                }
                if (count >= Math.Max(20, world.Tiles.Length / 100)) report.Continents++; else report.Islands++;
            }
            report.CoastFraction = coast / (double)Math.Max(1, report.LandTiles);
            report.RiverEdges = world.Rivers.Count; report.WildHerds = world.Herds.Count(h => h.DomesticCamp < 0);
            report.CampsWithVisibleHerds = world.Camps.Count(c => world.Herds.Any(h => h.DomesticCamp < 0 && map.Reachable(c.Tile, h.Tile, world.Config.VisionRadius, n => world.Tiles[n].TraversableLand)));
            report.CamelHerds = world.Herds.Count(h => h.Species == "camel");
            report.CamelsNearOases = world.Herds.Count(h => h.Species == "camel" && map.Radius(h.Tile, 2).Any(n => world.Tiles[n].Oasis));
            ElevationLevelStage.Validate(world); Geography.Validate(world); OasisStage.Validate(world); BoundaryRiverStage.Validate(world);
            if (report.LandTiles == 0 || report.LandTiles == world.Tiles.Length) throw new InvalidOperationException("World must contain both land and ocean.");
        }
    }
    internal sealed class GeometryStage : IWorldGenerationStage
    {
        public string Name => "Geometry"; public string Requires => "Configuration"; public string Produces => "Tiles, hex topology";
        public void Generate(GenerationContext c) { for (int i = 0; i < c.World.Tiles.Length; i++) c.World.Tiles[i] = new Tile { Id = i }; }
    }
    internal sealed class EcologyStage : IWorldGenerationStage
    {
        public string Name => "Soil and environment"; public string Requires => "Climate, drainage"; public string Produces => "Regional moisture, local soil moisture, environment and freshwater sources";
        public void Generate(GenerationContext c)
        {
            foreach (var tile in c.World.Tiles)
            {
                // Boundary-edge flow is the river's water, unlike the separate tile drainage proxy.
                double riverFlow = 0;
                if (tile.RiverEdges != null)
                    foreach (int edge in tile.RiverEdges)
                        if (edge >= 0) riverFlow = Math.Max(riverFlow, c.World.Rivers[edge].Flow);
                double subsidy = .65 * (tile.Environment == EnvironmentKind.Lake ? 1 : Numbers.Clamp(riverFlow / Math.Max(.001, c.Config.RiverThreshold), 0, 1));
                double regional = .3, soil = .3;
                for (int cycle = 0; cycle < 3; cycle++)
                    for (int s = 0; s < 4; s++)
                    {
                        var climate = tile.Seasons[s];
                        double snowmelt = climate.Temperature > 0 ? tile.Seasons[(s + 3) % 4].Snow * 2 : 0;
                        double forcing = climate.Rain * 7 + snowmelt - Math.Max(0, climate.Temperature) * .002;
                        regional = Numbers.Clamp(regional * .35 + forcing, 0, 1);
                        soil = Numbers.Clamp(soil * .35 + forcing + subsidy, 0, 1);
                        climate.RegionalMoisture = (float)regional;
                        climate.Moisture = (float)soil;
                        tile.Seasons[s] = climate;
                    }
                double temperature = tile.Seasons.Average(s => s.Temperature), moisture = tile.Seasons.Average(s => s.RegionalMoisture);
                if (tile.Land)
                    tile.Environment = Vegetation.Classify(temperature, moisture, c.Config);
                double averageRain = tile.Seasons.Average(s => s.Rain);
                tile.FreshwaterSource = tile.WaterBody == WaterKind.Saltwater ? FreshwaterSource.None : tile.Terrain == Terrain.Lake ? FreshwaterSource.Lake : tile.River ? FreshwaterSource.River : averageRain >= c.Config.RainPoolRainThreshold && tile.Flow >= c.Config.RainPoolDrainageMinimum ? FreshwaterSource.RainPool : FreshwaterSource.None;
            }
        }
    }
    internal sealed class VegetationResourceStage : IWorldGenerationStage
    {
        public string Name => "Vegetation and seasonal resources"; public string Requires => "Environment, oases"; public string Produces => "Vegetation potential and renewable capacities";
        public void Generate(GenerationContext c)
        {
            foreach (var tile in c.World.Tiles)
            {
                double averageRain = tile.Seasons.Average(s => s.Rain);
                double upwelling = AquaticEcology.UpwellingStrength(c.World, tile);
                for (int s = 0; s < 4; s++)
                {
                    var data = tile.Seasons[s];
                    double warmth = Numbers.Clamp((data.Temperature + 8) / 30, .015, 1);
                    bool water = tile.Aquatic || tile.Environment == EnvironmentKind.Ocean;
                    double productivity = water ? AquaticEcology.Productivity(c.World, tile, s) : (.12 + data.Moisture) * warmth;
                    if (tile.Environment == EnvironmentKind.Ice) productivity = 0;
                    if (tile.Land) productivity *= tile.Relief == Relief.Mountain ? c.Config.MountainProductivity : tile.Relief == Relief.Highland ? c.Config.HighlandProductivity : tile.Relief == Relief.Hills ? c.Config.HillsProductivity : 1;
                    double fresh = tile.FreshwaterSource == FreshwaterSource.Lake ? 120 : tile.FreshwaterSource == FreshwaterSource.River ? Math.Min(150, 70 + Math.Sqrt(tile.Flow) * 4) : tile.FreshwaterSource == FreshwaterSource.RainPool ? 25 * Numbers.Clamp(data.Rain / Math.Max(.001, averageRain), .25, 1.5) : 0;
                    double accessibleAquaticBase = tile.Environment == EnvironmentKind.Shallows ? AquaticEcology.ShallowsBaseBiomass * AquaticEcology.ShallowsHarvestableShare : 90;
                    data.MaxFood = water ? (float)(accessibleAquaticBase * productivity * (1 + AquaticEcology.UpwellingBiomassBonus * upwelling)) : Math.Max(data.MaxFood, (float)(90 * productivity));
                    if (tile.Land) data.MaxFood *= (float)c.Config.LandBiomassMultiplier;
                    data.MaxFreshwater = Math.Max(data.MaxFreshwater, (float)fresh); data.AccessibleFreshwater = (float)fresh;
                    data.MaxSaltwater = tile.WaterBody == WaterKind.Saltwater ? 100 : 0;
                    double landFraction = c.Config.LandFoodRegenerationInitialized
                        ? c.Config.LandFoodRegeneration : c.Config.FoodRegeneration;
                    double recovery = water ? AquaticEcology.RenewalPerDay(c.Config, tile, productivity, upwelling) : EcologyMath.DailyRate(landFraction * (.2 + data.Moisture) * warmth, c.Config.BaseDays);
                    data.FoodRecoveryPerDay = water ? (float)recovery : Math.Max(data.FoodRecoveryPerDay, (float)recovery);
                    if (tile.FreshwaterSource != FreshwaterSource.Oasis) data.WaterRecoveryPerDay = (float)EcologyMath.DailyRate(c.Config.WaterRecovery, c.Config.BaseDays); data.Habitat = tile.Land ? (float)productivity : 0;
                    tile.Seasons[s] = data;
                }
                // Potential consumes completed local water capacities, before gatherable shares are configured.
                tile.NaturalVegetationPotential = tile.CurrentVegetation = Vegetation.Potential(tile, c.Config);
                tile.Food = tile.Seasons[0].MaxFood * .9; FoodEcology.ConfigurePotential(tile, c.Config, true);
                tile.Freshwater = tile.Seasons[0].MaxFreshwater * .9; tile.Saltwater = tile.Seasons[0].MaxSaltwater;
            }
        }
    }
    internal sealed class PlacementStage : IWorldGenerationStage
    {
        public string Name => "Viable asymmetric starts and herds"; public string Requires => "Ecology"; public string Produces => "Camps, herds, mandates";
        // A one-time stock bonanza is not a viable twenty-year camp. The
        // conservative repeatable stock assumes opening G was fully taken,
        // then applies the same four-profile recovery used by resolution.
        private static double RepeatableLandFood(WorldState world, int tile, double[] cache)
        {
            if (cache != null && !double.IsNaN(cache[tile])) return cache[tile];
            var t = world.Tiles[tile];
            var view = new EcologyView(t);
            view.PotentialFood = Math.Max(0, view.PotentialFood - view.Food);
            view.Food = 0;
            LazyEcology.RecoverLandAfterHarvest(world, t, ref view, world.Days,
                world.Config.TurnDays, null, null, null);
            LazyEcology.ClampCapacity(world, t, ref view);
            double repeatable = Math.Min(t.Food, view.Food);
            if (cache != null) cache[tile] = repeatable;
            return repeatable;
        }
        public static double Viability(WorldState world, int tile) => Viability(world, tile, null);
        private static double Viability(WorldState world, int tile, double[] repeatableLand)
        {
            if (!world.Tiles[tile].TraversableLand) return 0;
            var cfg = world.Config;
            if (!Footprint(world, tile, n => RepeatableLandFood(world, n, repeatableLand), out double repeatableLandFood, out double aquaticRenewal, out _)) return 0;
            return 100 * Harvest(world, cfg.StartingPopulation, repeatableLandFood, aquaticRenewal) / (cfg.StartingPopulation * cfg.Consumption * cfg.GenerationIntervals);
        }
        // The radius-one food and freshwater evaluation shared by start viability and fission destinations (WP10a).
        // `repeatableOf` supplies each land tile's conservative repeatable stock. Returns false when freshwater
        // fails in any season or on mean recharge. `annualWater` is the four-profile recharge sum.
        internal static bool Footprint(WorldState world, int tile, Func<int, double> repeatableOf, out double repeatableLandFood, out double aquaticRenewal, out double annualWater)
        {
            var cfg = world.Config;
            annualWater = 0; repeatableLandFood = 0; aquaticRenewal = 0;
            Span<double> seasonalWater = stackalloc double[4]; seasonalWater.Clear();
            foreach (int n in world.Map.Radius(tile, 1))
            {
                var t = world.Tiles[n]; if (t.Environment == EnvironmentKind.Ocean || t.ElevationLevel >= 10) continue;
                for (int s = 0; s < 4; s++)
                {
                    var profile = t.Seasons[s];
                    // Land extraction precedes renewal: only opening G can
                    // satisfy this turn. Aquatic food keeps its flow envelope.
                    if (!t.Land) aquaticRenewal += profile.MaxFood * profile.FoodRecoveryPerDay
                        * cfg.BaseDays * cfg.SustainableProductionFraction;
                    if (t.WaterBody == WaterKind.Saltwater || t.FreshwaterSource == FreshwaterSource.None) continue;
                    seasonalWater[s] += profile.MaxFreshwater;
                    annualWater += profile.MaxFreshwater * profile.WaterRecoveryPerDay * cfg.BaseDays;
                }
                if (t.Land) repeatableLandFood += repeatableOf(n);
            }
            if (annualWater / 4 < cfg.StartFreshwaterMinimum) return false;
            for (int s = 0; s < 4; s++) if (seasonalWater[s] < cfg.StartFreshwaterMinimum) return false;
            return true;
        }
        // Same working share and land/coastal labor ceiling as passive gathering, for a cohort of `people`.
        internal static double Harvest(WorldState world, int people, double repeatableLandFood, double aquaticRenewal)
        {
            var cfg = world.Config;
            return Math.Min((repeatableLandFood + aquaticRenewal) * WorkingShare(world, people),
                people * cfg.GatherPerPerson * cfg.GenerationIntervals);
        }
        // Share of the footprint's repeatable stock a cohort of this size can work.
        internal static double WorkingShare(WorldState world, int people)
        {
            var cfg = world.Config;
            return Math.Min(1, Numbers.Fraction(cfg.GatherFraction, cfg.GenerationIntervals) * people / cfg.PopulationScale);
        }
        public void Generate(GenerationContext c)
        {
            var w = c.World;
            WildHerdPlacement.Populate(w);
            var repeatableLand = new double[w.Tiles.Length];
            for (int i = 0; i < repeatableLand.Length; i++) repeatableLand[i] = double.NaN;
            // Exponential weighted sampling chooses viable starts without flattening geographic asymmetry.
            var candidates = new List<KeyValuePair<int, double>>();
            foreach (var tile in w.Tiles)
            {
                if (!tile.TraversableLand || !c.Map.Neighbors(tile.Id).Any(n => w.Tiles[n].TraversableLand && w.Config.Species.Any(s => WildHerdPlacement.Weight(w, w.Tiles[n], s) > 0))) continue;
                double score = Viability(w, tile.Id, repeatableLand); if (score < c.Config.StartViability) continue;
                double key = -Math.Log(Math.Max(1e-12, Numbers.Random(w.Seed, 50, tile.Id))) / Math.Sqrt(score);
                candidates.Add(new KeyValuePair<int, double>(tile.Id, key));
            }
            foreach (var entry in candidates.OrderBy(e => e.Value).ThenBy(e => e.Key))
            {
                if (w.Camps.Any(camp => c.Map.Distance(camp.Tile, entry.Key) < c.Config.StartSeparation)) continue;
                int player = w.Camps.Count;
                var camp = new Camp { Id = w.NextId++, Player = player, Tile = entry.Key, Population = c.Config.StartingPopulation, Food = Math.Max(c.Config.StartingFood, c.Config.StartingReserveFood) };
                camp.Mandate = new Mandate { Risk = Numbers.Random(w.Seed, 51, entry.Key), Mobility = Numbers.Random(w.Seed, 52, entry.Key), Exploration = Numbers.Random(w.Seed, 53, entry.Key), Reserve = Numbers.Random(w.Seed, 54, entry.Key), Hunting = .85 + Numbers.Random(w.Seed, 55, entry.Key) * .3, Gathering = .85 + Numbers.Random(w.Seed, 56, entry.Key) * .3, Fishing = .85 + Numbers.Random(w.Seed, 57, entry.Key) * .3 };
                w.Camps.Add(camp); w.Generation.MeanStartViability += Viability(w, entry.Key, repeatableLand);
                if (w.Camps.Count == c.Config.Players) break;
            }
            if (w.Camps.Count < c.Config.Players) throw new InvalidOperationException("Not enough separated viable starts. Try another seed or lower player count/separation.");
            w.Generation.MeanStartViability /= w.Camps.Count; w.PlayerCount = w.Camps.Count;
            WildHerdPlacement.EnsureOpeningHerds(w);
        }
    }
}
