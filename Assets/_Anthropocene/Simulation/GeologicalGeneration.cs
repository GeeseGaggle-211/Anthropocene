using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    public sealed class GeologicalProvinceStage : IWorldGenerationStage
    {
        public string Name => "Latent geological provinces";
        public string Requires => "Completed environment";
        public string Produces => "Independent host, history and marine setting";
        // Resource streams 12000-12999 are independent of existing 1-445 core streams.
        internal static double Roll(int seed, int purpose, int key, int detail = 0) => Numbers.Random(seed, 12000 + purpose, key, detail);
        public void Generate(GenerationContext c)
        {
            var options = c.ResourceOptions; options.Validate(); if (!options.Enabled) return;
            int count = c.World.Tiles.Length, seed = c.World.Seed, width = c.Config.Width, height = c.Config.Height;
            var r = new WorldResourceState { CatalogHash = ResourceCatalog.Hash, ProfileHash = GeologicalProfiles.Hash, ConfigurationHash = options.Hash, MaximumQuantity = options.MaximumQuantity, WoodRecoveryPerYear = options.WoodRecoveryPerYear,
                Province = new ushort[count], Host = new byte[count], History = new ushort[count], Marine = new byte[count], Stone = new ushort[count], Clay = new ushort[count], Wood = new ushort[count] };
            c.World.Resources = r;
            foreach (var t in c.World.Tiles)
            {
                bool marine = t.WaterBody == WaterKind.Saltwater;
                r.Host[t.Id] = (byte)(marine ? HostRock.Mafic : HostRock.Basement);
                r.History[t.Id] = (ushort)(GeologicalHistory.Ancient | GeologicalHistory.Preserved | (marine ? GeologicalHistory.MarineFacies : GeologicalHistory.None));
            }
            var provinces = new List<GeologicalProvince>(); var weight = new float[count]; var boundaryWarp = new float[count];
            for (int id = 0; id < count; id++) boundaryWarp[id] = (float)(.12 * TerrainNoise.Fractal(seed, 12100, id % width / (double)width, id / width / (double)height, 32, 2));
            for (int key = 1; key <= 144; key++)
            {
                var host = (HostRock)(int)(Roll(seed, 1, key) * 8);
                var history = GeologicalHistory.Preserved;
                if (host == HostRock.Clastic || host == HostRock.Carbonate)
                {
                    history |= GeologicalHistory.Basin | GeologicalHistory.MarineFacies;
                    if (Roll(seed, 2, key) < .7) history |= GeologicalHistory.Source;
                    if (Roll(seed, 3, key) < .7) history |= GeologicalHistory.Mature;
                    if (Roll(seed, 4, key) < .7) history |= GeologicalHistory.Reservoir;
                    if (Roll(seed, 5, key) < .6) history |= GeologicalHistory.SealTrap;
                    if (Roll(seed, 6, key) < .35) history |= GeologicalHistory.Evaporite;
                    if (Roll(seed, 7, key) < .45) history |= GeologicalHistory.Peat;
                    if (Roll(seed, 8, key) < .7) history |= GeologicalHistory.Sorted;
                }
                else if (host != HostRock.Basement && host != HostRock.Metamorphic) history |= GeologicalHistory.Intrusive;
                if (Roll(seed, 9, key) < .7) history |= GeologicalHistory.Ancient;
                if (Roll(seed, 10, key) < .6) history |= GeologicalHistory.Mineralized;
                if (Roll(seed, 11, key) < .5) history |= GeologicalHistory.Reduced;
                if (Roll(seed, 12, key) < .5) history |= GeologicalHistory.Weathered;
                if (Roll(seed, 13, key) < .4) history |= GeologicalHistory.Hydrothermal;
                var p = new GeologicalProvince { Id = key, Host = host, History = history, X = Roll(seed, 14, key), Y = Roll(seed, 15, key), RadiusX = .045 + .055 * Roll(seed, 16, key), RadiusY = .06 + .10 * Roll(seed, 17, key), Angle = Math.PI * Roll(seed, 18, key) };
                provinces.Add(p);
                double cos = Math.Cos(p.Angle), sin = Math.Sin(p.Angle), bound = Math.Max(p.RadiusX, p.RadiusY) * 1.2;
                int minRow = Math.Max(0, (int)((p.Y - bound) * height)), maxRow = Math.Min(height - 1, (int)((p.Y + bound) * height));
                int minCol = (int)Math.Floor((p.X - bound) * width), maxCol = (int)Math.Ceiling((p.X + bound) * width);
                for (int y = minRow; y <= maxRow; y++) for (int q = minCol; q <= maxCol; q++)
                {
                    int x = (q % width + width) % width, id = y * width + x;
                    double dx = TerrainNoise.LongitudeDelta(x / (double)width, p.X), dy = y / (double)height - p.Y;
                    double a = (dx * cos + dy * sin) / p.RadiusX, b = (-dx * sin + dy * cos) / p.RadiusY;
                    double warp = boundaryWarp[id];
                    double strength = 1 - a * a - b * b + warp;
                    if (strength <= 0) continue;
                    // Histories overlap; the dominant soft field determines exposed host rock.
                    r.History[id] |= (ushort)history;
                    if (strength > weight[id]) { weight[id] = (float)strength; r.Province[id] = (ushort)key; r.Host[id] = (byte)host; }
                }
            }
            r.Provinces = provinces.ToArray();
            foreach (var t in c.World.Tiles)
            {
                MarineSetting setting;
                if (t.Land) setting = MarineSetting.Land;
                else if (t.Environment == EnvironmentKind.Lake) setting = MarineSetting.Lake;
                else
                {
                    var host = (HostRock)r.Host[t.Id]; var history = (GeologicalHistory)r.History[t.Id];
                    double relativeHeight = t.Elevation / Math.Max(.001, c.Config.SeaLevel);
                    bool rocky = host == HostRock.Mafic || host == HostRock.Ultramafic || host == HostRock.Alkaline;
                    bool boundary = c.BoundaryDistance != null && c.BoundaryDistance[t.Id] < .04;
                    setting = t.Environment == EnvironmentKind.Shallows ? MarineSetting.Shelf : relativeHeight > .65 ? MarineSetting.Slope : MarineSetting.AbyssalPlain;
                    if (rocky && relativeHeight > .3 && (t.Volcanism > .05 || relativeHeight > .65)) setting = MarineSetting.Seamount;
                    if (rocky && boundary && (history & GeologicalHistory.Hydrothermal) != 0) setting = MarineSetting.HydrothermalRidge;
                }
                r.Marine[t.Id] = (byte)setting;
            }
        }
    }
    public sealed class MineralDepositStage : IWorldGenerationStage
    {
        public string Name => "Finite geological deposits";
        public string Requires => "Geological provinces";
        public string Produces => "Shared deposit bodies and local access fractions";
        private readonly IEnumerable<GeologicalProfile> profiles;
        public MineralDepositStage() : this(GeologicalProfiles.All) { }
        public MineralDepositStage(IEnumerable<GeologicalProfile> profiles) { this.profiles = profiles; }
        public void Generate(GenerationContext c)
        {
            var r = c.World.Resources; if (r == null) return;
            var bodies = new List<DepositBody>(); int seed = c.World.Seed;
            foreach (var profile in profiles.Where(p => !p.Secondary && !c.ResourceOptions.DisabledProfiles.Contains(p.Id)))
            {
                // Fixed normalized candidate lattice: increasing resolution does not multiply reservoirs.
                for (int domain = 0; domain < 2; domain++) for (int key = 0; key < 2048; key++)
                {
                    int semantic = profile.Id * 8192 + domain * 2048 + key;
                    double x = (key % 64 + GeologicalProvinceStage.Roll(seed, 201, semantic)) / 64;
                    double y = (key / 64 + GeologicalProvinceStage.Roll(seed, 202, semantic)) / 32;
                    int tile = Math.Min(c.Config.Height - 1, (int)(y * c.Config.Height)) * c.Config.Width + Math.Min(c.Config.Width - 1, (int)(x * c.Config.Width));
                    bool marine = c.World.Tiles[tile].WaterBody == WaterKind.Saltwater;
                    if (marine != (domain == 1) || !profile.Eligible(c.World, tile)) continue;
                    double chance = profile.Id == 36 ? .025 : profile.Shape == DepositShape.Bed ? .13 : .085;
                    if (GeologicalProvinceStage.Roll(seed, 203, semantic) >= chance * c.ResourceOptions.Density) continue;
                    var sites = Sites(c, profile, tile, semantic); if (sites.Length == 0) continue;
                    double grade = .65 + .35 * GeologicalProvinceStage.Roll(seed, 204, semantic);
                    var composition = profile.Composition.Select(v => new DepositComponent(v.Material, Math.Max(1, (int)(v.PartsPerTenThousand * grade)))).ToArray();
                    // Optional co-products are sampled within their parent's private key space.
                    composition = composition.Where((v, i) => i == 0 || v.PartsPerTenThousand >= 500 || GeologicalProvinceStage.Roll(seed, 205, semantic, (int)v.Material) < .6).ToArray();
                    if (profile.Id == 14)
                    {
                        double oilShare = .1 + .8 * GeologicalProvinceStage.Roll(seed, 206, semantic);
                        composition = new[] { new DepositComponent(MaterialId.Petroleum, (int)(9000 * oilShare)), new DepositComponent(MaterialId.NaturalGas, (int)(9000 * (1 - oilShare))), new DepositComponent(MaterialId.Sulfur, 200) };
                    }
                    double quantity = Math.Round(r.MaximumQuantity * (.55 + .45 * GeologicalProvinceStage.Roll(seed, 207, semantic)) / 10) * 10;
                    bodies.Add(new DepositBody { Id = (long)profile.Id * 1000000 + domain * 100000 + key + 1, Profile = profile.Id, OriginTile = tile, InitialQuantity = quantity, Quantity = quantity, Shape = profile.Shape,
                        Hardness = (byte)(1 + (int)(GeologicalProvinceStage.Roll(seed, 208, semantic) * 5)), Quality = (byte)(25 + GeologicalProvinceStage.Roll(seed, 209, semantic) * 76), Components = composition, Sites = sites });
                }
            }
            r.Bodies = bodies.OrderBy(b => b.Id).ToArray();
        }
        private static DepositSite[] Sites(GenerationContext c, GeologicalProfile profile, int origin, int key)
        {
            int width = c.Config.Width, height = c.Config.Height;
            double radius = profile.Shape == DepositShape.Reservoir || profile.Shape == DepositShape.Field || profile.Shape == DepositShape.Bed ? .012 : .006;
            double rx = radius * (1 + GeologicalProvinceStage.Roll(c.World.Seed, 210, key)), ry = profile.Shape == DepositShape.Vein ? radius * .25 : radius * .8;
            int province = c.World.Resources.Province[origin];
            double angle = (province > 0 ? c.World.Resources.Provinces[province - 1].Angle : Math.PI * GeologicalProvinceStage.Roll(c.World.Seed, 213, key)) + .5 * (GeologicalProvinceStage.Roll(c.World.Seed, 214, key) - .5);
            double cos = Math.Cos(angle), sin = Math.Sin(angle), bound = Math.Max(rx, ry) * 1.15;
            int ox = origin % width, oy = origin / width, bx = Math.Max(1, (int)Math.Ceiling(bound * width)), by = Math.Max(1, (int)Math.Ceiling(bound * height));
            var sites = new List<DepositSite>();
            for (int y = Math.Max(0, oy - by); y <= Math.Min(height - 1, oy + by); y++) for (int x = ox - bx; x <= ox + bx; x++)
            {
                int tile = y * width + (x % width + width) % width;
                double dx = (x - ox) / (double)width, dy = (y - oy) / (double)height;
                double a = (dx * cos + dy * sin) / rx, b = (-dx * sin + dy * cos) / ry;
                double edge = 1 + .18 * TerrainNoise.Fractal(c.World.Seed, 12250, (x % width + width) % width / (double)width, y / (double)height, 128, 2);
                if ((tile != origin && a * a + b * b > edge) || !profile.Eligible(c.World, tile)) continue;
                bool buried = profile.Shape == DepositShape.Reservoir || profile.Shape == DepositShape.Seam;
                double cover = GeologicalProvinceStage.Roll(c.World.Seed, 211, key) + (c.World.Tiles[origin].Elevation - c.World.Tiles[tile].Elevation) * 3;
                var depth = buried ? (cover < .65 ? BurialDepth.Deep : BurialDepth.Shallow) : cover < .4 ? BurialDepth.Surface : BurialDepth.Shallow;
                if (profile.Id == 33 || profile.Id == 34) depth = BurialDepth.Surface;
                // Access is a fraction of local material, not discovery permission or equipment authorization.
                sites.Add(new DepositSite { Tile = tile, Burial = depth, Setting = (MarineSetting)c.World.Resources.Marine[tile], Exposure = (ushort)(depth == BurialDepth.Surface ? 10000 : depth == BurialDepth.Shallow ? 2000 : 200) });
            }
            // Keep only the connected piece containing the origin after geological clipping.
            var available = new HashSet<int>(sites.Select(s => s.Tile)); var connected = new HashSet<int> { origin }; var queue = new Queue<int>(); queue.Enqueue(origin);
            while (queue.Count > 0) foreach (int n in c.Map.Neighbors(queue.Dequeue())) if (available.Contains(n) && connected.Add(n)) queue.Enqueue(n);
            sites = sites.Where(s => connected.Contains(s.Tile)).OrderBy(s => s.Tile).ToList();
            for (int i = 0; i < sites.Count; i++) { var s = sites[i]; s.Share = (ushort)(10000 / sites.Count + (i < 10000 % sites.Count ? 1 : 0)); sites[i] = s; }
            return sites.ToArray();
        }
    }
    public sealed class SurfaceMaterialStage : IWorldGenerationStage
    {
        public string Name => "Surface materials and woody capacity";
        public string Requires => "Geological bodies and vegetation";
        public string Produces => "Finite Stone/Clay, renewable Wood potential and sourced placers";
        public void Generate(GenerationContext c)
        {
            var r = c.World.Resources; if (r == null) return;
            foreach (var t in c.World.Tiles)
            {
                if (!t.Land) continue;
                int id = t.Id; var host = (HostRock)r.Host[id]; var history = (GeologicalHistory)r.History[id];
                double amount = .55 + .45 * GeologicalProvinceStage.Roll(c.World.Seed, 301, id);
                // Local absence is allowed. Neither placement nor food/water viability is changed.
                double stoneChance = host == HostRock.Clastic ? .45 : .88;
                if (GeologicalProvinceStage.Roll(c.World.Seed, 302, id) < stoneChance) r.Stone[id] = Pack(r, amount);
                bool weathering = (history & GeologicalHistory.Weathered) != 0 && host != HostRock.Ultramafic;
                bool fineSediment = (history & GeologicalHistory.Basin) != 0 && (host == HostRock.Clastic || host == HostRock.Carbonate);
                double clayChance = (weathering ? .28 : .02) + (fineSediment ? .27 : 0) + (t.River ? .18 : 0);
                if (GeologicalProvinceStage.Roll(c.World.Seed, 303, id) < clayChance) r.Clay[id] = Pack(r, .45 + .55 * GeologicalProvinceStage.Roll(c.World.Seed, 304, id));
                // Low = woody brush, not forest. No trees/wood on ice or water.
                double wood = t.Environment == EnvironmentKind.Ice ? 0 : t.NaturalVegetationPotential == VegetationTier.High ? 1 : t.NaturalVegetationPotential == VegetationTier.Medium ? .55 : .05;
                r.Wood[id] = Pack(r, wood * (.8 + .2 * GeologicalProvinceStage.Roll(c.World.Seed, 305, id)));
            }
            if (!c.ResourceOptions.DisabledProfiles.Contains(100)) AddPlacers(c);
        }
        private static ushort Pack(WorldResourceState r, double fraction) => (ushort)Math.Min(r.MaximumQuantity / 10, Math.Round(r.MaximumQuantity * fraction / 10));
        private static void AddPlacers(GenerationContext c)
        {
            var r = c.World.Resources; var result = r.Bodies.ToList(); var map = c.Map;
            foreach (var source in r.Bodies.Where(b => b.Profile == 3 || b.Profile == 7 || b.Profile == 31 || b.Profile == 26).OrderBy(b => b.Id))
            {
                var path = new List<int> { source.OriginTile }; int current = source.OriginTile;
                // The drainage graph is acyclic; no river-edge substitutions or material replication.
                int limit = Math.Max(8, c.Config.Width / 12);
                for (int step = 0; step < limit; step++)
                {
                    int next = c.World.Tiles[current].Downstream; if (next < 0) break;
                    path.Add(next); current = next;
                    if (c.World.Tiles[current].WaterBody == WaterKind.Saltwater)
                    {
                        // Bounded shelf sorting proxy continues the same finite source seaward.
                        for (int shelf = 0; shelf < 3; shelf++)
                        {
                            int n = map.Neighbors(current).Where(id => c.World.Tiles[id].WaterBody == WaterKind.Saltwater && !path.Contains(id) && (MarineSetting)r.Marine[id] == MarineSetting.Shelf)
                                .OrderBy(id => GeologicalProvinceStage.Roll(c.World.Seed, 306, (int)source.Id, id)).ThenBy(id => id).DefaultIfEmpty(-1).First();
                            if (n < 0) break; path.Add(n); current = n;
                        }
                        break;
                    }
                    if (step > 1 && ((GeologicalHistory)r.History[current] & GeologicalHistory.Sorted) != 0 && c.World.Tiles[current].Relief == Relief.Lowland) break;
                }
                if (path.Count < 2 || c.World.Tiles[current].Environment == EnvironmentKind.Lake) continue;
                double mass = source.InitialQuantity * (.05 + .10 * GeologicalProvinceStage.Roll(c.World.Seed, 307, (int)source.Id));
                source.Quantity -= mass;
                result.Add(new DepositBody { Id = 10000000000L + source.Id, SourceBody = source.Id, Profile = 100, OriginTile = current, InitialQuantity = mass, Quantity = mass, Shape = DepositShape.Bed, Hardness = 1, Quality = source.Quality,
                    Components = (DepositComponent[])source.Components.Clone(), TransportPath = path.ToArray(), Sites = new[] { new DepositSite { Tile = current, Share = 10000, Exposure = 10000, Burial = BurialDepth.Surface, Setting = (MarineSetting)r.Marine[current] } } });
            }
            r.Bodies = result.OrderBy(b => b.Id).ToArray();
        }
    }
    public sealed class ResourceAccessAuditStage : IWorldGenerationStage
    {
        public string Name => "Resource opportunity audit";
        public string Requires => "Placement and resources";
        public string Produces => "Diagnostic warnings only";
        public void Generate(GenerationContext c)
        {
            if (c.World.Resources == null) return;
            foreach (var material in new[] { MaterialId.Stone, MaterialId.Clay, MaterialId.Wood })
            {
                int absent = c.World.Camps.Count(camp => !c.Map.Radius(camp.Tile, 2).Any(tile => c.World.Resources.SurfaceTotal(tile, material) > 0 && c.Map.Reachable(camp.Tile, tile, 2, n => c.World.Tiles[n].TraversableLand)));
                if (absent > 0) c.World.Generation.Warnings.Add(absent + " starts lack " + material + " within two traversable hexes (developer opportunity audit; no collection economy is implemented).");
            }
            ResourceValidation.Validate(c.World);
        }
    }
}
