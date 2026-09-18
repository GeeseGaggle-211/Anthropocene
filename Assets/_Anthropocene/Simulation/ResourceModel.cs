using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Anthropocene.Simulation
{
    public enum HostRock : byte { Basement, Granite, Mafic, Ultramafic, Carbonate, Clastic, Metamorphic, Alkaline }
    [Flags] public enum GeologicalHistory : ushort
    { None = 0, Ancient = 1, Basin = 2, Intrusive = 4, Mineralized = 8, Reduced = 16, Weathered = 32, Preserved = 64, Peat = 128, Mature = 256, Evaporite = 512, MarineFacies = 1024, Hydrothermal = 2048, Sorted = 4096, Source = 8192, Reservoir = 16384, SealTrap = 32768 }
    public enum MarineSetting : byte { Land, Lake, Shelf, Slope, AbyssalPlain, Seamount, HydrothermalRidge }
    public enum BurialDepth : byte { Surface, Shallow, Deep }
    public enum DepositShape : byte { Lens, Vein, Bed, Seam, Reservoir, Field }
    [Serializable]
    public sealed class ResourceGenerationOptions
    {
        public bool Enabled = true;
        public int MaximumQuantity = ResourceCatalog.MaximumQuantity;
        public double Density = 1, WoodRecoveryPerYear = .05;
        public int[] DisabledProfiles = Array.Empty<int>();
        public void Validate()
        {
            if (MaximumQuantity < 1000 || MaximumQuantity > ResourceCatalog.MaximumQuantity || !Finite(Density) || Density < 0 || Density > 4 || !Finite(WoodRecoveryPerYear) || WoodRecoveryPerYear < 0 || WoodRecoveryPerYear > 1 || DisabledProfiles == null || DisabledProfiles.Any(id => !GeologicalProfiles.All.Any(p => p.Id == id)))
                throw new ArgumentException("Invalid resource-generation options.");
        }
        internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        public string Hash => ResourceCatalog.Digest(MaximumQuantity + ":" + Density.ToString("R", CultureInfo.InvariantCulture) + ":" + WoodRecoveryPerYear.ToString("R", CultureInfo.InvariantCulture) + ":" + string.Join(",", DisabledProfiles.Distinct().OrderBy(x => x)));
    }
    [Serializable]
    public sealed class GeologicalProvince
    {
        public int Id;
        public double X, Y, RadiusX, RadiusY, Angle;
        public HostRock Host;
        public GeologicalHistory History;
    }
    [Serializable]
    public struct DepositComponent
    {
        public MaterialId Material;
        public ushort PartsPerTenThousand;
        public DepositComponent(MaterialId material, int parts) { Material = material; PartsPerTenThousand = (ushort)parts; }
    }
    [Serializable]
    public struct DepositSite
    {
        public int Tile;
        public ushort Share, Exposure;
        public BurialDepth Burial;
        public MarineSetting Setting;
    }
    [Serializable]
    public sealed class DepositBody
    {
        public long Id, SourceBody;
        public int Profile, OriginTile;
        public double InitialQuantity, Quantity;
        public DepositShape Shape;
        public byte Hardness, Quality;
        public DepositComponent[] Components;
        public DepositSite[] Sites;
        // Secondary bodies retain the exact modern drainage/coastal transport path.
        public int[] TransportPath = Array.Empty<int>();
    }
    [Serializable]
    public sealed class WorldResourceState
    {
        public const int CurrentVersion = 1, CurrentGenerationVersion = 1;
        public int Version = CurrentVersion, GenerationVersion = CurrentGenerationVersion, CoreGeneratorVersion = WorldState.GeneratorVersion;
        public string CatalogHash, ConfigurationHash, ProfileHash;
        public int MaximumQuantity;
        public double WoodRecoveryPerYear;
        // Freeze catalog-dependent access and discovery semantics with the generated world.
        public ushort[] AccessibleParts = ResourceCatalog.Materials.Aggregate(new ushort[69], (a, m) => { a[(int)m.Id] = (ushort)Math.Round(m.BaseAccessibleFraction * 10000); return a; });
        public string[] DiscoveryTechnologies = ResourceCatalog.Materials.Aggregate(Enumerable.Repeat("", 69).ToArray(), (a, m) => { a[(int)m.Id] = m.DiscoveryTechnology; return a; });
        public GeologicalProvince[] Provinces;
        public ushort[] Province, History, Stone, Clay, Wood;
        public byte[] Host, Marine;
        public DepositBody[] Bodies = Array.Empty<DepositBody>();
        // Surface stocks are stored in ten-unit increments; 10,000 means 100,000 units.
        public const int SurfaceUnit = 10;
        public double SurfaceTotal(int tile, MaterialId material) => SurfaceUnit * (material == MaterialId.Stone ? Stone[tile] : material == MaterialId.Clay ? Clay[tile] : material == MaterialId.Wood ? Wood[tile] : 0);
        public double AccessibleFraction(MaterialId material) => AccessibleParts[(int)material] / 10000.0;
        public double SurfaceAccessible(int tile, MaterialId material) => SurfaceTotal(tile, material) * AccessibleFraction(material);
        public double ComponentQuantity(DepositBody body, MaterialId material) => body.Quantity * body.Components.Where(c => c.Material == material).Sum(c => (int)c.PartsPerTenThousand) / 10000;
        public double AccessibleAt(DepositBody body, DepositSite site, MaterialId material) => ComponentQuantity(body, material) * site.Share / 10000 * site.Exposure / 10000 * AccessibleFraction(material);
        public long PackedBytes => Province.LongLength * 12 + Provinces.LongLength * 48 + Bodies.Sum(b => 64L + b.Components.LongLength * 4 + b.Sites.LongLength * 10 + b.TransportPath.LongLength * 4);
    }
    public static class ResourceValidation
    {
        public static void Validate(WorldState w)
        {
            var r = w.Resources; if (r == null) return;
            int count = w.Tiles.Length;
            if (r.GenerationVersion < 1 || r.GenerationVersion > WorldResourceState.CurrentGenerationVersion) throw new InvalidOperationException("Unsupported resource generator.");
            if (r.Version != WorldResourceState.CurrentVersion || r.CoreGeneratorVersion < 7 || r.CoreGeneratorVersion > WorldState.GeneratorVersion || r.MaximumQuantity < 1000 || r.MaximumQuantity > ResourceCatalog.MaximumQuantity || !ResourceGenerationOptions.Finite(r.WoodRecoveryPerYear) || r.WoodRecoveryPerYear < 0 || r.WoodRecoveryPerYear > 1 || string.IsNullOrEmpty(r.CatalogHash) || string.IsNullOrEmpty(r.ProfileHash) || string.IsNullOrEmpty(r.ConfigurationHash)) throw new InvalidOperationException("Invalid resource metadata.");
            if (r.Provinces == null || r.Provinces.Length > ushort.MaxValue || r.Bodies == null || r.Province?.Length != count || r.History?.Length != count || r.Host?.Length != count || r.Marine?.Length != count || r.Stone?.Length != count || r.Clay?.Length != count || r.Wood?.Length != count) throw new InvalidOperationException("Invalid resource raster dimensions.");
            if (r.AccessibleParts?.Length != 69 || r.DiscoveryTechnologies?.Length != 69 || r.AccessibleParts.Any(v => v > 10000) || ResourceCatalog.Materials.Any(m => r.DiscoveryTechnologies[(int)m.Id] == null)) throw new InvalidOperationException("Invalid saved material catalog.");
            for (int i = 0; i < r.Provinces.Length; i++)
            {
                var p = r.Provinces[i];
                if (p == null || p.Id != i + 1 || !ResourceGenerationOptions.Finite(p.X) || !ResourceGenerationOptions.Finite(p.Y) || !ResourceGenerationOptions.Finite(p.RadiusX) || !ResourceGenerationOptions.Finite(p.RadiusY) || !ResourceGenerationOptions.Finite(p.Angle) || p.X < 0 || p.X >= 1 || p.Y < 0 || p.Y >= 1 || p.RadiusX <= 0 || p.RadiusY <= 0 || p.Host > HostRock.Alkaline) throw new InvalidOperationException("Invalid geological province.");
            }
            for (int i = 0; i < count; i++)
            {
                if (r.Province[i] > r.Provinces.Length || r.Host[i] > (byte)HostRock.Alkaline || r.Marine[i] > (byte)MarineSetting.HydrothermalRidge || r.Stone[i] * 10 > r.MaximumQuantity || r.Clay[i] * 10 > r.MaximumQuantity || r.Wood[i] * 10 > r.MaximumQuantity || (!w.Tiles[i].Land && (r.Stone[i] != 0 || r.Clay[i] != 0 || r.Wood[i] != 0))) throw new InvalidOperationException("Invalid resource tile " + i);
            }
            var ids = new HashSet<long>();
            foreach (var b in r.Bodies)
            {
                if (b == null || b.Id <= 0 || !ids.Add(b.Id) || !GeologicalProfiles.All.Any(p => p.Id == b.Profile) || b.OriginTile < 0 || b.OriginTile >= count || !ResourceGenerationOptions.Finite(b.Quantity) || !ResourceGenerationOptions.Finite(b.InitialQuantity) || b.Quantity < 0 || b.Quantity > b.InitialQuantity || b.InitialQuantity > r.MaximumQuantity || b.InitialQuantity <= 0 || b.Components == null || b.Components.Length == 0 || b.Sites == null || b.Sites.Length == 0 || b.TransportPath == null) throw new InvalidOperationException("Invalid deposit body.");
                if (b.Shape > DepositShape.Field || b.Components.Select(c => c.Material).Distinct().Count() != b.Components.Length || b.Components.Sum(c => (int)c.PartsPerTenThousand) > 10000 || b.Components.Any(c => c.PartsPerTenThousand == 0 || ResourceCatalog.Get(c.Material).Reserved)) throw new InvalidOperationException("Invalid deposit composition.");
                if (b.Sites.Sum(s => (int)s.Share) != 10000 || b.Sites.Select(s => s.Tile).Distinct().Count() != b.Sites.Length || b.Sites.Any(s => s.Tile < 0 || s.Tile >= count || s.Exposure > 10000 || s.Share == 0 || s.Burial > BurialDepth.Deep || (byte)s.Setting != r.Marine[s.Tile])) throw new InvalidOperationException("Invalid deposit access sites.");
            }
            var bodies = r.Bodies.ToDictionary(b => b.Id);
            foreach (var b in r.Bodies.Where(b => b.SourceBody != 0))
            {
                if (!bodies.TryGetValue(b.SourceBody, out var source) || source.SourceBody != 0 || b.TransportPath.Length < 2 || b.TransportPath[0] != source.OriginTile || b.TransportPath[b.TransportPath.Length - 1] != b.OriginTile || b.TransportPath.Any(t => t < 0 || t >= count)) throw new InvalidOperationException("Invalid secondary provenance.");
                for (int i = 1; i < b.TransportPath.Length; i++)
                    if (!w.Map.Neighbors(b.TransportPath[i - 1]).Contains(b.TransportPath[i]) || w.Tiles[b.TransportPath[i - 1]].WaterBody != WaterKind.Saltwater && w.Tiles[b.TransportPath[i - 1]].Downstream != b.TransportPath[i]) throw new InvalidOperationException("Disconnected secondary provenance.");
            }
            foreach (var group in r.Bodies.Where(b => b.SourceBody != 0).GroupBy(b => b.SourceBody))
            {
                var source = bodies[group.Key];
                if (source.Quantity + group.Sum(b => b.InitialQuantity) > source.InitialQuantity + 1e-6 || group.Any(b => !b.Components.SequenceEqual(source.Components))) throw new InvalidOperationException("Secondary material exceeds its source budget.");
            }
        }
    }
}
