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
    public enum MineralConcentration : byte { Deposit, Scattered }
    [Serializable]
    public sealed class ResourceGenerationOptions
    {
        // Historical generator-2 defaults must retain their original configuration identity.
        private const string LegacyTuningStamp = "144:0.12:0.045:0.055:0.06:0.1:0.7:0.7:0.7:0.6:0.35:0.45:0.7:0.7:0.6:0.5:0.5:0.4|0.085:0.13:0.025:1.6:2.5:0.18:0.01:0.07:1000:0.012:0.006:0.035:0.25:0.8:0.65:0.04:0.18:0.025:9:0.55:0.45|0.55:0.45:0.45:0.88:0.28:0.02:0.27:0.18:0.45:0.55:1:0.55:0.05:0.8:0.2|0.05:0.1:6:3";
        public bool Enabled = true;
        public int MaximumQuantity = ResourceCatalog.DefaultMaximumQuantity;
        public const double MaximumDeepReserveMultiplier = 1000000;
        public double DeepReserveMultiplier;
        public double Density = 1, ScatteredDensity = 1, WoodRecoveryPerYear = .05;
        public int[] DisabledProfiles = Array.Empty<int>();
        public ProvinceTuning Province = new ProvinceTuning();
        public DepositTuning Deposit = new DepositTuning();
        public SurfaceTuning Surface = new SurfaceTuning();
        public PlacerTuning Placer = new PlacerTuning();
        public void Validate()
        {
            if (MaximumQuantity < 1000 || !Finite(DeepReserveMultiplier) || DeepReserveMultiplier < 0 || DeepReserveMultiplier > MaximumDeepReserveMultiplier || !Finite(Density) || Density < 0 || Density > 4 || !Finite(ScatteredDensity) || ScatteredDensity < 0 || ScatteredDensity > 4 || !Finite(WoodRecoveryPerYear) || WoodRecoveryPerYear < 0 || WoodRecoveryPerYear > 1 || DisabledProfiles == null || DisabledProfiles.Any(id => !GeologicalProfiles.All.Any(p => p.Id == id)) || Province == null || Deposit == null || Surface == null || Placer == null)
                throw new ArgumentException("Invalid resource-generation options.");
            Province.Validate(); Deposit.Validate(); Surface.Validate(); Placer.Validate();
        }
        internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        public string Hash
        {
            get
            {
                string legacy = MaximumQuantity + ":" + Density.ToString("R", CultureInfo.InvariantCulture) + ":" + ScatteredDensity.ToString("R", CultureInfo.InvariantCulture) + ":" + WoodRecoveryPerYear.ToString("R", CultureInfo.InvariantCulture) + ":" + string.Join(",", DisabledProfiles.Distinct().OrderBy(x => x));
                string tuning = Province.Stamp + "|" + Deposit.Stamp + "|" + Surface.Stamp + "|" + Placer.Stamp;
                string identity = tuning == LegacyTuningStamp ? legacy : legacy + "|tuning-v1|" + tuning;
                if (DeepReserveMultiplier > 0) identity += "|deep-v1|" + DeepReserveMultiplier.ToString("R", CultureInfo.InvariantCulture);
                return ResourceCatalog.Digest(identity);
            }
        }
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
        public double InitialAccessibleQuantity, AccessibleQuantity, InitialDeepQuantity, DeepQuantity;
        public DepositComponent(MaterialId material, int parts) { this = default; Material = material; PartsPerTenThousand = (ushort)parts; }
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
        public double SiteExposureWeightTotal;
        public double InitialDeepQuantity => Components?.Sum(c => c.InitialDeepQuantity) ?? 0;
        public double DeepQuantity => Components?.Sum(c => c.DeepQuantity) ?? 0;
        public DepositShape Shape;
        public MineralConcentration Concentration;
        public byte Hardness, Quality;
        public DepositComponent[] Components;
        public DepositSite[] Sites;
        // Secondary bodies retain the exact modern drainage/coastal transport path.
        public int[] TransportPath = Array.Empty<int>();
    }
    [Serializable]
    public sealed class WorldResourceState
    {
        public const int CurrentVersion = 4, CurrentGenerationVersion = 3;
        // Schema 1/2 used ten-unit surface stocks and rejected maxima above 100,000.
        public const int LegacyMaximumQuantity = 100000, LegacySurfaceUnit = 10;
        public int Version = 3, GenerationVersion = 2, CoreGeneratorVersion = WorldState.GeneratorVersion;
        public string CatalogHash, ConfigurationHash, ProfileHash;
        public int MaximumQuantity;
        public double DeepReserveMultiplier;
        public string DeepMiningTechnology = "";
        public double WoodRecoveryPerYear;
        // Freeze catalog-dependent access and discovery semantics with the generated world.
        public ushort[] AccessibleParts = ResourceCatalog.Materials.Aggregate(new ushort[69], (a, m) => { a[(int)m.Id] = (ushort)Math.Round(m.BaseAccessibleFraction * 10000); return a; });
        public double[] Concentrations;
        public string[] DiscoveryTechnologies = ResourceCatalog.Materials.Aggregate(Enumerable.Repeat("", 69).ToArray(), (a, m) => { a[(int)m.Id] = m.DiscoveryTechnology; return a; });
        public GeologicalProvince[] Provinces;
        public ushort[] Province, History, Stone, Clay, Wood, DeepStone, DeepClay;
        public byte[] Host, Marine;
        public DepositBody[] Bodies = Array.Empty<DepositBody>();
        // Scale the compact raster, not its memory footprint, for large configured stocks.
        public int SurfaceUnit = LegacySurfaceUnit;
        public static int SurfaceUnitFor(int maximum) => Math.Max(LegacySurfaceUnit, (int)Math.Ceiling(maximum / (double)ushort.MaxValue));
        public double BaseSurfaceTotal(int tile, MaterialId material) => (double)SurfaceUnit * (material == MaterialId.Stone ? Stone[tile] : material == MaterialId.Clay ? Clay[tile] : material == MaterialId.Wood ? Wood[tile] : 0);
        public double SurfaceDeepTotal(int tile, MaterialId material) => Version >= 4 ? SurfaceUnit * DeepReserveMultiplier * (material == MaterialId.Stone ? DeepStone[tile] : material == MaterialId.Clay ? DeepClay[tile] : 0) : 0;
        public double SurfaceTotal(int tile, MaterialId material) => BaseSurfaceTotal(tile, material) + SurfaceDeepTotal(tile, material);
        public double AccessibleFraction(MaterialId material) => AccessibleParts[(int)material] / 10000.0;
        public double Concentration(MaterialId material) => Concentrations != null && (int)material < Concentrations.Length && Concentrations[(int)material] > 0 ? Concentrations[(int)material] : 1.0;
        public double SurfaceAccessible(int tile, MaterialId material) => BaseSurfaceTotal(tile, material) * AccessibleFraction(material);
        public double ComponentQuantity(DepositBody body, MaterialId material) => body.Quantity * body.Components.Where(c => c.Material == material).Sum(c => (int)c.PartsPerTenThousand) / 10000.0;
        public double DeepComponentQuantity(DepositBody body, MaterialId material) => Version >= 4 ? body.Components.Where(c => c.Material == material).Sum(c => c.DeepQuantity) : 0;
        public double TotalComponentQuantity(DepositBody body, MaterialId material) => Version >= 4 ? body.Components.Where(c => c.Material == material).Sum(c => c.AccessibleQuantity + c.DeepQuantity) : ComponentQuantity(body, material);
        private static double SiteWeight(DepositBody body, DepositSite site)
        {
            double sum = body.SiteExposureWeightTotal;
            return sum == 0 ? 0 : site.Share * (double)site.Exposure / sum;
        }
        public double AccessibleAt(DepositBody body, DepositSite site, MaterialId material) => Version >= 4
            ? body.Components.Where(c => c.Material == material).Sum(c => c.AccessibleQuantity) * SiteWeight(body, site)
            : ComponentQuantity(body, material) * site.Share / 10000 * site.Exposure / 10000 * AccessibleFraction(material);
        public double DeepAt(DepositBody body, DepositSite site, MaterialId material) => Version >= 4 ? DeepComponentQuantity(body, material) * SiteWeight(body, site) : 0;
        public double TotalAt(DepositBody body, DepositSite site, MaterialId material) => Version >= 4 ? AccessibleAt(body, site, material) + DeepAt(body, site, material) : ComponentQuantity(body, material) * site.Share / 10000;
        public long PackedBytes => Province.LongLength * (Version >= 4 ? 16 : 12) + Provinces.LongLength * 48 + Bodies.Sum(b => 65L + b.Components.LongLength * (Version >= 4 ? 36 : 4) + b.Sites.LongLength * 10 + b.TransportPath.LongLength * 4);
    }
    public static class ResourceValidation
    {
        public static void Validate(WorldState w)
        {
            var r = w.Resources; if (r == null) return;
            int count = w.Tiles.Length;
            if (r.SurfaceUnit != WorldResourceState.SurfaceUnitFor(r.MaximumQuantity) || r.Version < 3 && (r.MaximumQuantity > WorldResourceState.LegacyMaximumQuantity || r.SurfaceUnit != WorldResourceState.LegacySurfaceUnit))
                throw new InvalidOperationException("Invalid resource quantity encoding.");
            if (!ResourceGenerationOptions.Finite(r.DeepReserveMultiplier) || r.DeepReserveMultiplier < 0 || r.DeepReserveMultiplier > ResourceGenerationOptions.MaximumDeepReserveMultiplier ||
                (r.Version < 4 && (r.DeepReserveMultiplier != 0 || !string.IsNullOrEmpty(r.DeepMiningTechnology))) ||
                (r.Version >= 4 && (r.DeepReserveMultiplier <= 0 || r.DeepMiningTechnology != "AdvancedMining" || r.GenerationVersion != 3)))
                throw new InvalidOperationException("Invalid deep resource metadata.");
            if (r.Version >= 4 && (r.DeepStone?.Length != count || r.DeepClay?.Length != count || r.AccessibleParts[(int)MaterialId.Stone] != 10000 || r.AccessibleParts[(int)MaterialId.Clay] != 10000) || r.Version < 4 && (r.DeepStone != null || r.DeepClay != null))
                throw new InvalidOperationException("Invalid deep surface dimensions.");
            if (r.GenerationVersion < 1 || r.GenerationVersion > WorldResourceState.CurrentGenerationVersion) throw new InvalidOperationException("Unsupported resource generator.");
            if ((r.Version < 1 || r.Version > WorldResourceState.CurrentVersion) || r.CoreGeneratorVersion < 7 || r.CoreGeneratorVersion > WorldState.GeneratorVersion || r.MaximumQuantity < 1000 || !ResourceGenerationOptions.Finite(r.WoodRecoveryPerYear) || r.WoodRecoveryPerYear < 0 || r.WoodRecoveryPerYear > 1 || string.IsNullOrEmpty(r.CatalogHash) || string.IsNullOrEmpty(r.ProfileHash) || string.IsNullOrEmpty(r.ConfigurationHash)) throw new InvalidOperationException("Invalid resource metadata.");
            if (r.Provinces == null || r.Provinces.Length > ushort.MaxValue || r.Bodies == null || r.Province?.Length != count || r.History?.Length != count || r.Host?.Length != count || r.Marine?.Length != count || r.Stone?.Length != count || r.Clay?.Length != count || r.Wood?.Length != count) throw new InvalidOperationException("Invalid resource raster dimensions.");
            if (r.AccessibleParts?.Length != 69 || r.DiscoveryTechnologies?.Length != 69 || r.AccessibleParts.Any(v => v > 10000) || ResourceCatalog.Materials.Any(m => r.DiscoveryTechnologies[(int)m.Id] == null)) throw new InvalidOperationException("Invalid saved material catalog.");
            if (r.Version >= 4 && r.Concentrations == null || r.Concentrations != null && (r.Concentrations.Length != 69 || ResourceCatalog.Materials.Any(m => !ResourceGenerationOptions.Finite(r.Concentrations[(int)m.Id]) || r.Concentrations[(int)m.Id] <= 0))) throw new InvalidOperationException("Invalid saved material concentrations.");
            for (int i = 0; i < r.Provinces.Length; i++)
            {
                var p = r.Provinces[i];
                if (p == null || p.Id != i + 1 || !ResourceGenerationOptions.Finite(p.X) || !ResourceGenerationOptions.Finite(p.Y) || !ResourceGenerationOptions.Finite(p.RadiusX) || !ResourceGenerationOptions.Finite(p.RadiusY) || !ResourceGenerationOptions.Finite(p.Angle) || p.X < 0 || p.X >= 1 || p.Y < 0 || p.Y >= 1 || p.RadiusX <= 0 || p.RadiusY <= 0 || p.Host > HostRock.Alkaline) throw new InvalidOperationException("Invalid geological province.");
            }
            for (int i = 0; i < count; i++)
            {
                if (r.Province[i] > r.Provinces.Length || r.Host[i] > (byte)HostRock.Alkaline || r.Marine[i] > (byte)MarineSetting.HydrothermalRidge || r.Stone[i] * (double)r.SurfaceUnit > r.MaximumQuantity || r.Clay[i] * (double)r.SurfaceUnit > r.MaximumQuantity || r.Wood[i] * (double)r.SurfaceUnit > r.MaximumQuantity || (!w.Tiles[i].Land && (r.Stone[i] != 0 || r.Clay[i] != 0 || r.Wood[i] != 0))) throw new InvalidOperationException("Invalid resource tile " + i);
                if (r.Version >= 4 && (r.DeepStone[i] * (double)r.SurfaceUnit > r.MaximumQuantity || r.DeepClay[i] * (double)r.SurfaceUnit > r.MaximumQuantity || (!w.Tiles[i].Land && (r.DeepStone[i] != 0 || r.DeepClay[i] != 0)))) throw new InvalidOperationException("Invalid deep resource tile " + i);
            }
            var ids = new HashSet<long>();
            foreach (var b in r.Bodies)
            {
                if (b == null || b.Id <= 0 || !ids.Add(b.Id) || !GeologicalProfiles.All.Any(p => p.Id == b.Profile) || b.OriginTile < 0 || b.OriginTile >= count || !ResourceGenerationOptions.Finite(b.Quantity) || !ResourceGenerationOptions.Finite(b.InitialQuantity) || b.Quantity < 0 || b.Quantity > b.InitialQuantity || b.InitialQuantity > r.MaximumQuantity || b.InitialQuantity <= 0 || b.Components == null || b.Components.Length == 0 || b.Sites == null || b.Sites.Length == 0 || b.TransportPath == null) throw new InvalidOperationException("Invalid deposit body.");
                foreach (var component in b.Components)
                {
                    double upper = b.InitialQuantity * component.PartsPerTenThousand / 10000.0 * r.AccessibleFraction(component.Material) * r.Concentration(component.Material);
                    bool valid = ResourceGenerationOptions.Finite(component.InitialAccessibleQuantity) && ResourceGenerationOptions.Finite(component.AccessibleQuantity) &&
                        ResourceGenerationOptions.Finite(component.InitialDeepQuantity) && ResourceGenerationOptions.Finite(component.DeepQuantity) &&
                        component.InitialAccessibleQuantity >= 0 && component.InitialAccessibleQuantity <= upper + Math.Max(1e-6, upper * 1e-10) && component.AccessibleQuantity >= 0 && component.AccessibleQuantity <= component.InitialAccessibleQuantity + 1e-6 &&
                        component.InitialDeepQuantity >= 0 && component.DeepQuantity >= 0 && component.DeepQuantity <= component.InitialDeepQuantity + 1e-6;
                    if (r.Version >= 4) valid &= Math.Abs(component.InitialDeepQuantity - component.InitialAccessibleQuantity * r.DeepReserveMultiplier) <= Math.Max(1e-6, component.InitialDeepQuantity * 1e-12);
                    else valid &= component.InitialAccessibleQuantity == 0 && component.AccessibleQuantity == 0 && component.InitialDeepQuantity == 0 && component.DeepQuantity == 0;
                    if (!valid) throw new InvalidOperationException("Invalid component resource pools.");
                }
                if (r.Version >= 4 && (!ResourceGenerationOptions.Finite(b.SiteExposureWeightTotal) || b.SiteExposureWeightTotal <= 0 || Math.Abs(b.SiteExposureWeightTotal - b.Sites.Sum(s => (double)s.Share * s.Exposure)) > 1e-6)) throw new InvalidOperationException("Invalid site exposure weights.");
                if (b.Concentration > MineralConcentration.Scattered || r.Version == 1 && b.Concentration != MineralConcentration.Deposit)
                    throw new InvalidOperationException("Invalid resource concentration kind.");
                if (b.Concentration == MineralConcentration.Scattered && (b.SourceBody != 0 || b.Shape != DepositShape.Field || b.InitialQuantity > r.MaximumQuantity * .08 + 1e-6 || b.Sites.Any(s => s.Setting != MarineSetting.Land || s.Burial != BurialDepth.Surface)))
                    throw new InvalidOperationException("Invalid scattered occurrence field.");
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
                if (source.Quantity + group.Sum(b => b.InitialQuantity) > source.InitialQuantity + 1e-6 || group.Any(b => b.Components.Length != source.Components.Length || b.Components.Any(c => !source.Components.Any(s => s.Material == c.Material && s.PartsPerTenThousand == c.PartsPerTenThousand)))) throw new InvalidOperationException("Secondary material exceeds its source budget.");
                if (r.Version >= 4)
                    foreach (var part in source.Components)
                    {
                        double childAccessible = group.Sum(b => b.Components.Single(c => c.Material == part.Material).InitialAccessibleQuantity);
                        double childDeep = group.Sum(b => b.Components.Single(c => c.Material == part.Material).InitialDeepQuantity);
                        if (part.AccessibleQuantity + childAccessible > part.InitialAccessibleQuantity + 1e-6 ||
                            part.DeepQuantity + childDeep > part.InitialDeepQuantity + 1e-6)
                            throw new InvalidOperationException("Secondary accessible or deep material exceeds its source pool.");
                    }
            }
        }
    }
}
