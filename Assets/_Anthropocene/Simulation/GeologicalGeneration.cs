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
            var tuning = options.Province;
            int count = c.World.Tiles.Length, seed = c.World.Seed, width = c.Config.Width, height = c.Config.Height;
            var r = new WorldResourceState { CatalogHash = ResourceCatalog.Hash, ProfileHash = GeologicalProfiles.Hash, ConfigurationHash = options.Hash, MaximumQuantity = options.MaximumQuantity, WoodRecoveryPerYear = options.WoodRecoveryPerYear,
                DeepReserveMultiplier = options.DeepReserveMultiplier, DeepMiningTechnology = options.DeepReserveMultiplier > 0 ? "AdvancedMining" : "",
                Province = new ushort[count], Host = new byte[count], History = new ushort[count], Marine = new byte[count], Stone = new ushort[count], Clay = new ushort[count], Wood = new ushort[count] };
            c.World.Resources = r;
            // Keep default and older-range worlds byte-compatible with schema 2.
            r.Version = options.DeepReserveMultiplier > 0 ? 4 : options.MaximumQuantity <= WorldResourceState.LegacyMaximumQuantity ? 2 : 3;
            r.GenerationVersion = options.DeepReserveMultiplier > 0 ? 3 : 2;
            r.SurfaceUnit = WorldResourceState.SurfaceUnitFor(options.MaximumQuantity);
            foreach (var t in c.World.Tiles)
            {
                bool marine = t.WaterBody == WaterKind.Saltwater;
                r.Host[t.Id] = (byte)(marine ? HostRock.Mafic : HostRock.Basement);
                bool cratonic = t.Continental && !marine && (c.BoundaryDistance == null || c.BoundaryDistance[t.Id] >= .045f) && t.Volcanism < .05f &&
                    TerrainNoise.Fractal(seed, 12050, (t.Id % width) / (double)width, (t.Id / width) / (double)height, 4, 3) > -.05;
                if (!marine && t.Continental && c.AncientOrogenStrength != null && c.AncientOrogenStrength[t.Id] > .35f && c.AncientOrogenAge != null && c.AncientOrogenAge[t.Id] >= 300) cratonic = true;
                r.History[t.Id] = (ushort)(GeologicalHistory.Preserved | (cratonic ? GeologicalHistory.Ancient : GeologicalHistory.None) | (marine ? GeologicalHistory.MarineFacies : GeologicalHistory.None));
                if (!marine && t.Continental && c.AncientOrogenStrength != null && c.AncientOrogenStrength[t.Id] > .55f) r.Host[t.Id] = (byte)HostRock.Metamorphic;
            }
            var provinces = new List<GeologicalProvince>(); var weight = new float[count]; var boundaryWarp = new float[count];
            for (int id = 0; id < count; id++) boundaryWarp[id] = (float)(tuning.BoundaryWarp * TerrainNoise.Fractal(seed, 12100, id % width / (double)width, id / width / (double)height, 32, 2));
            for (int key = 1; key <= tuning.Count; key++)
            {
                var host = (HostRock)(int)(Roll(seed, 1, key) * 8);
                var history = GeologicalHistory.Preserved;
                if (host == HostRock.Clastic || host == HostRock.Carbonate)
                {
                    history |= GeologicalHistory.Basin | GeologicalHistory.MarineFacies;
                    if (Roll(seed, 2, key) < tuning.SourceChance) history |= GeologicalHistory.Source;
                    if (Roll(seed, 3, key) < tuning.MatureChance) history |= GeologicalHistory.Mature;
                    if (Roll(seed, 4, key) < tuning.ReservoirChance) history |= GeologicalHistory.Reservoir;
                    if (Roll(seed, 5, key) < tuning.SealChance) history |= GeologicalHistory.SealTrap;
                    if (Roll(seed, 6, key) < tuning.EvaporiteChance) history |= GeologicalHistory.Evaporite;
                    if (Roll(seed, 7, key) < tuning.PeatChance) history |= GeologicalHistory.Peat;
                    if (Roll(seed, 8, key) < tuning.SortedChance) history |= GeologicalHistory.Sorted;
                }
                else if (host != HostRock.Basement && host != HostRock.Metamorphic) history |= GeologicalHistory.Intrusive;
                if (Roll(seed, 9, key) < tuning.AncientChance) history |= GeologicalHistory.Ancient;
                if (Roll(seed, 10, key) < tuning.MineralizedChance) history |= GeologicalHistory.Mineralized;
                if (Roll(seed, 11, key) < tuning.ReducedChance) history |= GeologicalHistory.Reduced;
                if (Roll(seed, 12, key) < tuning.WeatheredChance) history |= GeologicalHistory.Weathered;
                if (Roll(seed, 13, key) < tuning.HydrothermalChance) history |= GeologicalHistory.Hydrothermal;
                var p = new GeologicalProvince { Id = key, Host = host, History = history, X = Roll(seed, 14, key), Y = Roll(seed, 15, key), RadiusX = tuning.RadiusXMinimum + tuning.RadiusXRange * Roll(seed, 16, key), RadiusY = tuning.RadiusYMinimum + tuning.RadiusYRange * Roll(seed, 17, key), Angle = Math.PI * Roll(seed, 18, key) };
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
                    var tileHistory = history;
                    if (!c.World.Tiles[id].Continental || c.World.Tiles[id].WaterBody == WaterKind.Saltwater) tileHistory &= ~GeologicalHistory.Ancient;
                    r.History[id] |= (ushort)tileHistory;
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
}
