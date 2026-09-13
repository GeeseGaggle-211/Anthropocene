using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

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
    }
    public sealed class WorldGenerator
    {
        public WorldState Generate(int seed, SimulationConfig config)
        {
            config.Validate();
            var world = new WorldState { Seed = seed, Config = config, Tiles = new Tile[config.Width * config.Height], Generation = new GenerationReport() };
            // Hash all effective tuning, not just the preset name.
            world.Generation.ConfigurationHash = SaveCodec.ConfigurationHash(config);
            var context = new GenerationContext { World = world, Map = world.Map };
            IWorldGenerationStage[] stages = { new GeometryStage(), new PlateStage(), new ElevationStage(), new ClimateStage(), new HydrologyStage(), new BoundaryRiverStage(), new ElevationLevelStage(), new EcologyStage(), new OasisStage(), new VegetationResourceStage(), new PlacementStage() };
            var total = Stopwatch.StartNew();
            foreach (var stage in stages)
            {
                var timer = Stopwatch.StartNew(); stage.Generate(context);
                world.Generation.Stages.Add(stage.Name); world.Generation.Milliseconds.Add(timer.Elapsed.TotalMilliseconds);
            }
            if (total.Elapsed.TotalSeconds > config.GenerationBudgetSeconds) world.Generation.Warnings.Add("Generation exceeded configured time budget.");
            if (GC.GetTotalMemory(false) / 1048576 > config.GenerationBudgetMegabytes) world.Generation.Warnings.Add("Managed memory exceeds configured generation budget.");
            Ownership.Bind(world);
            Validate(world);
            return world;
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
                    if (float.IsNaN(season.MaxFood) || season.MaxFood < 0 || float.IsNaN(season.MaxPotentialFood) || season.MaxPotentialFood < season.MaxFood || season.MaxFreshwater < 0 || season.Moisture < 0)
                        throw new InvalidOperationException("Invalid generated ecology at " + tile.Id);
                if (tile.PotentialFood + 1e-6 < tile.Food) throw new InvalidOperationException("Gatherable food exceeds total potential food at " + tile.Id);
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
    internal sealed class PlateStage : IWorldGenerationStage
    {
        public string Name => "Tectonic plates"; public string Requires => "Geometry"; public string Produces => "Crust, plates, movement vectors";
        public void Generate(GenerationContext c)
        {
            int count = c.Config.PlateCount; c.PlateCenters = new int[count]; c.PlateX = new double[count]; c.PlateY = new double[count]; c.Continental = new bool[count];
            for (int p = 0; p < count; p++)
            {
                c.PlateCenters[p] = (int)(Numbers.Random(c.World.Seed, 10, p) * c.World.Tiles.Length);
                double angle = Numbers.Random(c.World.Seed, 11, p) * Math.PI * 2;
                c.PlateX[p] = Math.Cos(angle); c.PlateY[p] = Math.Sin(angle);
                c.Continental[p] = Numbers.Random(c.World.Seed, 12, p) < (c.Config.Shape == WorldShape.Archipelagos ? .28 : .55);
            }
            c.Continental[0] = true; c.Continental[1] = false;
            if (c.Config.Shape == WorldShape.Pangaea)
            {
                // Cluster continental crust on neighboring plates; no handcrafted continent silhouette.
                var connected = Enumerable.Range(0, count).OrderBy(p => GeographicDistance(c, c.PlateCenters[p], c.PlateCenters[0])).ThenBy(p => p).Take(Math.Max(1, (int)(count * .55))).ToHashSet();
                for (int p = 0; p < count; p++) c.Continental[p] = connected.Contains(p);
            }
            foreach (var tile in c.World.Tiles)
            {
                int best = 0; double bestDistance = double.MaxValue;
                for (int p = 0; p < count; p++)
                {
                    double d = GeographicDistance(c, tile.Id, c.PlateCenters[p]) * (0.75 + Numbers.Random(c.World.Seed, 13, p) * .5);
                    if (d < bestDistance) { bestDistance = d; best = p; }
                }
                tile.Plate = best; tile.Continental = c.Continental[best];
            }
        }
        internal static double GeographicDistance(GenerationContext c, int a, int b)
        {
            double dx = Math.Abs(a % c.Config.Width - b % c.Config.Width) / (double)c.Config.Width; dx = Math.Min(dx, 1 - dx);
            double dy = (a / c.Config.Width - b / c.Config.Width) / (double)c.Config.Height;
            return Math.Sqrt(dx * dx * 4 + dy * dy);
        }
    }
    internal sealed class ElevationStage : IWorldGenerationStage
    {
        public string Name => "Relief, volcanism, erosion"; public string Requires => "Plates"; public string Produces => "Elevation, coastline, volcanic chains";
        public void Generate(GenerationContext c)
        {
            var w = c.World; var cfg = c.Config;
            foreach (var tile in w.Tiles)
            {
                double x = tile.Id % cfg.Width / (double)cfg.Width, y = tile.Id / cfg.Width / (double)cfg.Height;
                double noise = Math.Sin(x * Math.PI * 6 + w.Seed) * Math.Cos(y * Math.PI * 5 + w.Seed * .37) * cfg.ElevationNoise;
                double height = tile.Continental ? .57 : .23;
                if (cfg.Shape == WorldShape.Archipelagos) height -= .09;
                double boundary = 0;
                foreach (int n in c.Map.Neighbors(tile.Id))
                {
                    int other = w.Tiles[n].Plate; if (other == tile.Plate) continue;
                    int a = c.PlateCenters[tile.Plate], b = c.PlateCenters[other];
                    double dx = b % cfg.Width - a % cfg.Width;
                    if (dx > cfg.Width / 2.0) dx -= cfg.Width; if (dx < -cfg.Width / 2.0) dx += cfg.Width;
                    double dy = b / cfg.Width - a / cfg.Width;
                    double norm = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
                    double convergence = ((c.PlateX[tile.Plate] - c.PlateX[other]) * dx + (c.PlateY[tile.Plate] - c.PlateY[other]) * dy) / norm;
                    boundary = Math.Max(boundary, Math.Abs(convergence) * cfg.Uplift);
                    if (convergence > .1) { height += tile.Continental ? boundary : -boundary * .5; tile.Volcanism = (float)boundary; }
                    else if (convergence < -.1) height += tile.Continental ? -boundary * .4 : boundary * .65;
                    else height += cfg.Uplift * .1;
                }
                tile.Elevation = (float)Numbers.Clamp(height + noise, .01, 1.2);
            }
            // Spread boundary relief into mountain belts, preserving the broad plate signal.
            for (int pass = 0; pass < 2; pass++)
            {
                var heights = new float[w.Tiles.Length];
                foreach (var t in w.Tiles) heights[t.Id] = (float)(t.Elevation * (1 - cfg.Erosion) + c.Map.Neighbors(t.Id).Average(n => w.Tiles[n].Elevation) * cfg.Erosion);
                foreach (var t in w.Tiles) t.Elevation = heights[t.Id];
            }
            for (int h = 0; h < cfg.Hotspots; h++)
            {
                int origin = (int)(Numbers.Random(w.Seed, 20, h) * w.Tiles.Length);
                for (int age = 0; age < cfg.HotspotChainLength; age++)
                {
                    int center = c.Map.At(origin % cfg.Width + age * 2, origin / cfg.Width + age / 2); if (center < 0) continue;
                    foreach (int n in c.Map.Radius(center, 2))
                    {
                        double lift = .35 * Math.Exp(-age * .25) / (1 + c.Map.Distance(center, n));
                        w.Tiles[n].Elevation += (float)lift; w.Tiles[n].Volcanism += (float)lift;
                    }
                }
            }
            foreach (var t in w.Tiles)
                t.Terrain = t.Elevation < cfg.SeaLevel - .045 ? Terrain.Ocean : t.Elevation < cfg.SeaLevel ? Terrain.Shallows : Terrain.Grassland;
            Geography.Classify(w);
        }
    }
    internal sealed class ClimateStage : IWorldGenerationStage
    {
        public string Name => "Stellar climate, winds, moisture transport"; public string Requires => "Elevation, land/ocean"; public string Produces => "Seasonal temperature, precipitation, snowfall, wind";
        public void Generate(GenerationContext c)
        {
            var cfg = c.Config; var tiles = c.World.Tiles;
            double baseline = cfg.InnerTemperature + (cfg.OuterTemperature - cfg.InnerTemperature) * cfg.HabitableZonePosition;
            for (int s = 0; s < 4; s++)
            {
                for (int row = 0; row < cfg.Height; row++)
                {
                    double latitude = row / (double)(cfg.Height - 1) * 2 - 1, absLat = Math.Abs(latitude);
                    int direction = absLat < .32 || absLat > .68 ? -1 : 1;
                    double moisture = .4;
                    // Multiple circuits remove a seam at the arbitrary starting longitude.
                    for (int step = 0; step < cfg.Width * 3; step++)
                    {
                        int col = direction > 0 ? step % cfg.Width : cfg.Width - 1 - step % cfg.Width;
                        var tile = tiles[row * cfg.Width + col];
                        var upstream = tiles[c.Map.At(col - direction, row)];
                        double season = Math.Sin(s * Math.PI / 2) * latitude * cfg.SeasonalAmplitude;
                        double seaInfluence = tile.Land ? 1 : 1 - cfg.OceanModeration;
                        double temp = 29 + baseline - absLat * cfg.LatitudeGradient - Math.Max(0, tile.Elevation - cfg.SeaLevel) * cfg.LapseRate + season * seaInfluence;
                        if (!tile.Land) moisture = Math.Min(1, moisture + cfg.Evaporation);
                        double uplift = Math.Max(0, tile.Elevation - upstream.Elevation);
                        double rainFraction = Numbers.Clamp(.035 + uplift * cfg.OrographicRain + (1 - absLat) * .045 + Math.Max(0, season) * cfg.Monsoon * .01, .01, .85);
                        double rain = moisture * rainFraction; moisture = Math.Max(0, (moisture - rain) * cfg.MoistureDecay);
                        // Leeward descent accelerates drying after windward extraction.
                        if (tile.Elevation < upstream.Elevation) moisture *= Math.Exp((tile.Elevation - upstream.Elevation) * .5);
                        tile.Seasons[s] = new SeasonData { Temperature = (float)temp, Rain = (float)(temp > 0 ? rain : 0), Snow = (float)(temp <= 0 ? rain : 0), WindX = direction, Runoff = (float)rain };
                    }
                }
            }
        }
    }
    internal sealed class HydrologyStage : IWorldGenerationStage
    {
        public string Name => "Hydrology and spillways"; public string Requires => "Elevation, seasonal runoff"; public string Produces => "Acyclic drainage, lakes, rivers";
        public void Generate(GenerationContext c)
        {
            var tiles = c.World.Tiles; var heap = new MinHeap(); var visited = new bool[tiles.Length]; var order = new List<int>(tiles.Length);
            foreach (var t in tiles)
                if (!t.Land) { heap.Add(t.Id, t.Elevation); visited[t.Id] = true; }
            if (heap.Count == 0) throw new InvalidOperationException("No ocean drainage outlet.");
            while (heap.Count > 0)
            {
                int id = heap.Pop(); order.Add(id); var current = tiles[id];
                foreach (int n in c.Map.Neighbors(id))
                {
                    if (visited[n]) continue; visited[n] = true;
                    var next = tiles[n]; next.Downstream = id;
                    if (next.Elevation < current.Elevation)
                    { next.DepressionDepth = current.Elevation - next.Elevation; if (next.DepressionDepth > .018) next.Terrain = Terrain.Lake; next.Elevation = current.Elevation; }
                    heap.Add(n, next.Elevation);
                }
            }
            foreach (var t in tiles) t.Flow = t.Seasons.Sum(s => s.Runoff) * 4;
            for (int i = order.Count - 1; i >= 0; i--)
            {
                var tile = tiles[order[i]];
                if (tile.Downstream >= 0) tiles[tile.Downstream].Flow += tile.Flow;
            }
        }
        // Stable height/TileId priority queue; equal-height parent links follow discovery order.
        private sealed class MinHeap
        {
            private readonly List<KeyValuePair<int, float>> items = new List<KeyValuePair<int, float>>();
            public int Count => items.Count;
            private static bool Less(KeyValuePair<int, float> a, KeyValuePair<int, float> b) => a.Value < b.Value || (a.Value == b.Value && a.Key < b.Key);
            public void Add(int id, float priority)
            {
                var item = new KeyValuePair<int, float>(id, priority); items.Add(item); int i = items.Count - 1;
                while (i > 0) { int p = (i - 1) / 2; if (!Less(item, items[p])) break; items[i] = items[p]; i = p; } items[i] = item;
            }
            public int Pop()
            {
                int result = items[0].Key; var last = items[items.Count - 1]; items.RemoveAt(items.Count - 1); if (items.Count == 0) return result;
                int i = 0;
                while (i * 2 + 1 < items.Count)
                {
                    int child = i * 2 + 1; if (child + 1 < items.Count && Less(items[child + 1], items[child])) child++;
                    if (!Less(items[child], last)) break; items[i] = items[child]; i = child;
                }
                items[i] = last; return result;
            }
        }
    }
    internal sealed class EcologyStage : IWorldGenerationStage
    {
        public string Name => "Soil and environment"; public string Requires => "Climate, drainage"; public string Produces => "Environment, soil moisture and freshwater sources";
        public void Generate(GenerationContext c)
        {
            foreach (var tile in c.World.Tiles)
            {
                double soil = .3;
                for (int cycle = 0; cycle < 3; cycle++)
                    for (int s = 0; s < 4; s++)
                    {
                        var climate = tile.Seasons[s];
                        double freshwater = tile.River || tile.Terrain == Terrain.Lake ? .65 : 0;
                        double snowmelt = climate.Temperature > 0 ? tile.Seasons[(s + 3) % 4].Snow * 2 : 0;
                        soil = Numbers.Clamp(soil * .35 + climate.Rain * 7 + snowmelt + freshwater - Math.Max(0, climate.Temperature) * .002, 0, 1);
                        climate.Moisture = (float)soil;
                        tile.Seasons[s] = climate;
                    }
                double temperature = tile.Seasons.Average(s => s.Temperature), moisture = tile.Seasons.Average(s => s.Moisture);
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
                tile.NaturalVegetationPotential = tile.CurrentVegetation = Vegetation.Potential(tile, c.Config);
                double averageRain = tile.Seasons.Average(s => s.Rain);
                for (int s = 0; s < 4; s++)
                {
                    var data = tile.Seasons[s];
                    double warmth = Numbers.Clamp((data.Temperature + 8) / 30, .015, 1);
                    double productivity = tile.Aquatic ? .65 * warmth : (.12 + data.Moisture) * warmth;
                    if (tile.Terrain == Terrain.Ocean) productivity = .005;
                    if (tile.Environment == EnvironmentKind.Ice) productivity = 0;
                    if (tile.Land) productivity *= tile.Relief == Relief.Mountain ? c.Config.MountainProductivity : tile.Relief == Relief.Highland ? c.Config.HighlandProductivity : tile.Relief == Relief.Hills ? c.Config.HillsProductivity : 1;
                    double fresh = tile.FreshwaterSource == FreshwaterSource.Lake ? 120 : tile.FreshwaterSource == FreshwaterSource.River ? Math.Min(150, 70 + Math.Sqrt(tile.Flow) * 4) : tile.FreshwaterSource == FreshwaterSource.RainPool ? 25 * Numbers.Clamp(data.Rain / Math.Max(.001, averageRain), .25, 1.5) : 0;
                    data.MaxFood = Math.Max(data.MaxFood, (float)(90 * productivity)); data.MaxFreshwater = Math.Max(data.MaxFreshwater, (float)fresh); data.AccessibleFreshwater = (float)fresh;
                    data.MaxSaltwater = tile.WaterBody == WaterKind.Saltwater ? 100 : 0;
                    data.FoodRecoveryPerDay = Math.Max(data.FoodRecoveryPerDay, (float)EcologyMath.DailyRate(c.Config.FoodRegeneration * (.2 + data.Moisture) * warmth, c.Config.BaseDays));
                    if (tile.FreshwaterSource != FreshwaterSource.Oasis) data.WaterRecoveryPerDay = (float)EcologyMath.DailyRate(c.Config.WaterRecovery, c.Config.BaseDays); data.Habitat = tile.Land ? (float)productivity : 0;
                    tile.Seasons[s] = data;
                }
                tile.Food = tile.Seasons[0].MaxFood * .9; FoodEcology.ConfigurePotential(tile, c.Config, true);
                tile.Freshwater = tile.Seasons[0].MaxFreshwater * .9; tile.Saltwater = tile.Seasons[0].MaxSaltwater;
            }
        }
    }
    internal sealed class PlacementStage : IWorldGenerationStage
    {
        public string Name => "Viable asymmetric starts and herds"; public string Requires => "Ecology"; public string Produces => "Camps, herds, mandates";
        public static double Viability(WorldState world, int tile)
        {
            double sum = 0, freshwater = 0;
            foreach (int n in world.Map.Radius(tile, 1))
            {
                var t = world.Tiles[n]; if (t.Terrain == Terrain.Ocean) continue;
                sum += t.Food + t.Seasons[0].MaxFood * (1 - Math.Exp(-t.Seasons[0].FoodRecoveryPerDay * world.Config.BaseDays));
                freshwater += t.DrinkableWater;
            }
            return freshwater >= world.Config.StartFreshwaterMinimum ? sum : 0;
        }
        public void Generate(GenerationContext c)
        {
            var w = c.World;
            WildHerdPlacement.Populate(w);
            // Exponential weighted sampling chooses viable starts without flattening geographic asymmetry.
            var candidates = new List<KeyValuePair<int, double>>();
            foreach (var tile in w.Tiles)
            {
                if (!tile.TraversableLand || !c.Map.Neighbors(tile.Id).Any(n => w.Tiles[n].TraversableLand && w.Config.Species.Any(s => WildHerdPlacement.Weight(w, w.Tiles[n], s) > 0))) continue;
                double score = Viability(w, tile.Id); if (score < c.Config.StartViability) continue;
                double key = -Math.Log(Math.Max(1e-12, Numbers.Random(w.Seed, 50, tile.Id))) / Math.Sqrt(score);
                candidates.Add(new KeyValuePair<int, double>(tile.Id, key));
            }
            foreach (var entry in candidates.OrderBy(e => e.Value).ThenBy(e => e.Key))
            {
                if (w.Camps.Any(camp => c.Map.Distance(camp.Tile, entry.Key) < c.Config.StartSeparation)) continue;
                int player = w.Camps.Count;
                var camp = new Camp { Id = w.NextId++, Player = player, Tile = entry.Key, Population = c.Config.StartingPopulation, Food = Math.Max(c.Config.StartingFood, c.Config.StartingReserveFood) };
                camp.Mandate = new Mandate { Risk = Numbers.Random(w.Seed, 51, entry.Key), Mobility = Numbers.Random(w.Seed, 52, entry.Key), Exploration = Numbers.Random(w.Seed, 53, entry.Key), Reserve = Numbers.Random(w.Seed, 54, entry.Key), Hunting = .85 + Numbers.Random(w.Seed, 55, entry.Key) * .3, Gathering = .85 + Numbers.Random(w.Seed, 56, entry.Key) * .3, Fishing = .85 + Numbers.Random(w.Seed, 57, entry.Key) * .3 };
                w.Camps.Add(camp); w.Generation.MeanStartViability += Viability(w, entry.Key);
                if (w.Camps.Count == c.Config.Players) break;
            }
            if (w.Camps.Count < c.Config.Players) throw new InvalidOperationException("Not enough separated viable starts. Try another seed or lower player count/separation.");
            w.Generation.MeanStartViability /= w.Camps.Count;
            WildHerdPlacement.EnsureOpeningHerds(w);
        }
    }
}
