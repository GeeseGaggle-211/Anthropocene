using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    public sealed class GeologicalProfile
    {
        public readonly int Id;
        public readonly string Name;
        public readonly HostRock[] Hosts;
        public readonly GeologicalHistory Required;
        public readonly DepositShape Shape;
        public readonly DepositComponent[] Composition;
        public readonly int Domain; // 0 land, 1 either, 2 shelf/slope, 3 abyssal, 4 rocky seamount, 5 hydrothermal, 6 land/margins/seamounts, 7 wetland/freshwater lowlands.
        public readonly bool Secondary;
        public GeologicalProfile(int id, string name, HostRock[] hosts, GeologicalHistory required, DepositShape shape, int domain, bool secondary, params DepositComponent[] composition)
        { Id = id; Name = name; Hosts = hosts; Required = required; Shape = shape; Domain = domain; Secondary = secondary; Composition = composition; }
        public bool Eligible(WorldState w, int tile)
        {
            var r = w.Resources; var t = w.Tiles[tile]; var marine = (MarineSetting)r.Marine[tile];
            if (t.Environment == EnvironmentKind.Lake || Domain == 0 && !t.Land || Domain == 2 && marine != MarineSetting.Shelf && marine != MarineSetting.Slope || Domain == 3 && marine != MarineSetting.AbyssalPlain || Domain == 4 && marine != MarineSetting.Seamount || Domain == 5 && marine != MarineSetting.HydrothermalRidge) return false;
            if (Domain == 6 && !t.Land && marine != MarineSetting.Shelf && marine != MarineSetting.Slope && marine != MarineSetting.Seamount) return false;
            if (Domain == 7 && (!t.Land || t.Environment == EnvironmentKind.Desert || t.Environment == EnvironmentKind.Ice || !(t.River || t.FreshwaterSource == FreshwaterSource.River || t.FreshwaterSource == FreshwaterSource.Lake || t.Flow > .05f || t.DepressionDepth > 0 || (t.Relief == Relief.Lowland && t.Seasons.Any(s => s.Moisture > .45f))))) return false;
            return (Hosts.Length == 0 || Hosts.Contains((HostRock)r.Host[tile])) && (((GeologicalHistory)r.History[tile] & Required) == Required);
        }
    }
    public static class GeologicalProfiles
    {
        private static DepositComponent C(MaterialId material, int parts) => new DepositComponent(material, parts);
        private static readonly HostRock[] Any = Array.Empty<HostRock>();
        private static HostRock[] H(params HostRock[] hosts) => hosts;
        private static GeologicalProfile P(int id, string name, HostRock[] hosts, GeologicalHistory history, DepositShape shape, int domain, params DepositComponent[] components) => new GeologicalProfile(id, name, hosts, history, shape, domain, false, components);
        // These are game profiles, not calibrated geological frequencies or tonnes of ore.
        public static readonly IReadOnlyList<GeologicalProfile> All = Array.AsReadOnly(new[] {
            P(1, "Porphyry copper", H(HostRock.Granite, HostRock.Alkaline), GeologicalHistory.Intrusive | GeologicalHistory.Mineralized, DepositShape.Lens, 0, C(MaterialId.Copper, 2200), C(MaterialId.Molybdenum, 200), C(MaterialId.Gold, 30), C(MaterialId.Silver, 50), C(MaterialId.Rhenium, 2), C(MaterialId.Selenium, 5), C(MaterialId.Tellurium, 3)),
            P(2, "Sediment-hosted copper", H(HostRock.Clastic), GeologicalHistory.Basin | GeologicalHistory.Reduced | GeologicalHistory.Mineralized, DepositShape.Bed, 0, C(MaterialId.Copper, 2400), C(MaterialId.Cobalt, 150), C(MaterialId.Silver, 40)),
            P(3, "Granite tin", H(HostRock.Granite), GeologicalHistory.Intrusive | GeologicalHistory.Mineralized, DepositShape.Vein, 0, C(MaterialId.Tin, 2600)),
            P(4, "Granite tungsten", H(HostRock.Granite, HostRock.Metamorphic), GeologicalHistory.Intrusive | GeologicalHistory.Mineralized, DepositShape.Vein, 0, C(MaterialId.Tungsten, 2200)),
            P(5, "Carbonate lead-zinc", H(HostRock.Carbonate), GeologicalHistory.Basin | GeologicalHistory.Mineralized, DepositShape.Lens, 0, C(MaterialId.Lead, 1700), C(MaterialId.Zinc, 2200), C(MaterialId.Silver, 60), C(MaterialId.Gallium, 5)),
            P(6, "Clastic lead-zinc", H(HostRock.Clastic), GeologicalHistory.Basin | GeologicalHistory.Reduced, DepositShape.Bed, 0, C(MaterialId.Lead, 1400), C(MaterialId.Zinc, 2400), C(MaterialId.Silver, 60)),
            P(7, "Primary gold", H(HostRock.Basement, HostRock.Metamorphic, HostRock.Granite), GeologicalHistory.Ancient | GeologicalHistory.Mineralized, DepositShape.Vein, 0, C(MaterialId.Gold, 1200)),
            P(8, "Ancient iron formation", H(HostRock.Basement, HostRock.Metamorphic), GeologicalHistory.Ancient, DepositShape.Bed, 0, C(MaterialId.Iron, 7000)),
            P(9, "Magmatic nickel sulfide", H(HostRock.Mafic, HostRock.Ultramafic), GeologicalHistory.Intrusive | GeologicalHistory.Mineralized, DepositShape.Lens, 0, C(MaterialId.Nickel, 2800), C(MaterialId.Copper, 700), C(MaterialId.Cobalt, 150), C(MaterialId.Sulfur, 600)),
            P(10, "Chromite", H(HostRock.Ultramafic), GeologicalHistory.Intrusive, DepositShape.Bed, 0, C(MaterialId.Chromium, 4000)),
            P(11, "Nickel laterite", H(HostRock.Ultramafic), GeologicalHistory.Weathered | GeologicalHistory.Preserved, DepositShape.Bed, 0, C(MaterialId.Nickel, 3200), C(MaterialId.Cobalt, 200)),
            P(12, "Bauxite", H(HostRock.Granite, HostRock.Clastic), GeologicalHistory.Weathered | GeologicalHistory.Preserved, DepositShape.Bed, 0, C(MaterialId.Bauxite, 8500), C(MaterialId.Gallium, 5)),
            P(13, "Coal seam", H(HostRock.Clastic), GeologicalHistory.Basin | GeologicalHistory.Peat | GeologicalHistory.Mature, DepositShape.Seam, 0, C(MaterialId.Coal, 9000), C(MaterialId.Sulfur, 300)),
            P(14, "Petroleum system", H(HostRock.Clastic, HostRock.Carbonate), GeologicalHistory.Basin | GeologicalHistory.Source | GeologicalHistory.Mature | GeologicalHistory.Reservoir | GeologicalHistory.SealTrap | GeologicalHistory.Preserved, DepositShape.Reservoir, 1, C(MaterialId.Petroleum, 5500), C(MaterialId.NaturalGas, 3500), C(MaterialId.Sulfur, 200)),
            P(15, "Evaporite salt", H(HostRock.Clastic, HostRock.Carbonate), GeologicalHistory.Basin | GeologicalHistory.Evaporite | GeologicalHistory.Preserved, DepositShape.Bed, 0, C(MaterialId.Salt, 7500), C(MaterialId.Gypsum, 1800)),
            P(16, "Potash evaporite", H(HostRock.Clastic, HostRock.Carbonate), GeologicalHistory.Basin | GeologicalHistory.Evaporite | GeologicalHistory.Reduced, DepositShape.Bed, 0, C(MaterialId.Potash, 3500), C(MaterialId.Salt, 5000)),
            P(17, "Limestone formation", H(HostRock.Carbonate), GeologicalHistory.MarineFacies, DepositShape.Bed, 0, C(MaterialId.Limestone, 10000)),
            P(18, "Quartz-rich sediment", H(HostRock.Clastic), GeologicalHistory.Sorted, DepositShape.Bed, 0, C(MaterialId.Silica, 9000)),
            P(19, "Vein quartz", H(HostRock.Granite, HostRock.Metamorphic), GeologicalHistory.Intrusive, DepositShape.Vein, 0, C(MaterialId.Silica, 9500)),
            P(20, "Marine phosphorite", H(HostRock.Clastic, HostRock.Carbonate), GeologicalHistory.MarineFacies | GeologicalHistory.Preserved, DepositShape.Bed, 6, C(MaterialId.Phosphate, 6500)),
            P(21, "Manganese sediment", H(HostRock.Clastic, HostRock.Metamorphic), GeologicalHistory.Ancient | GeologicalHistory.MarineFacies, DepositShape.Bed, 0, C(MaterialId.Manganese, 4500)),
            P(22, "Native sulfur", Any, GeologicalHistory.Hydrothermal | GeologicalHistory.Mineralized, DepositShape.Lens, 0, C(MaterialId.Sulfur, 7500)),
            P(23, "Lithium pegmatite", H(HostRock.Granite), GeologicalHistory.Intrusive | GeologicalHistory.Mineralized, DepositShape.Vein, 0, C(MaterialId.Lithium, 2200)),
            P(24, "Relict lithium brine", H(HostRock.Clastic, HostRock.Carbonate), GeologicalHistory.Basin | GeologicalHistory.Evaporite | GeologicalHistory.Source, DepositShape.Reservoir, 0, C(MaterialId.Lithium, 1800), C(MaterialId.Salt, 5000)),
            P(25, "Altered lithium clay", H(HostRock.Clastic), GeologicalHistory.Weathered | GeologicalHistory.Mineralized, DepositShape.Bed, 0, C(MaterialId.Lithium, 1700), C(MaterialId.Clay, 6000)),
            P(26, "Alkaline rare earths", H(HostRock.Alkaline), GeologicalHistory.Intrusive | GeologicalHistory.Mineralized, DepositShape.Lens, 0, C(MaterialId.RareEarths, 2300)),
            P(27, "Weathered rare earths", H(HostRock.Granite, HostRock.Alkaline), GeologicalHistory.Weathered | GeologicalHistory.Mineralized | GeologicalHistory.Preserved, DepositShape.Bed, 0, C(MaterialId.RareEarths, 1600)),
            P(28, "Metamorphic graphite", H(HostRock.Metamorphic), GeologicalHistory.Ancient | GeologicalHistory.Reduced, DepositShape.Bed, 0, C(MaterialId.Graphite, 6000)),
            P(29, "Sandstone uranium", H(HostRock.Clastic), GeologicalHistory.Basin | GeologicalHistory.Reduced | GeologicalHistory.Source, DepositShape.Lens, 0, C(MaterialId.Uranium, 1400)),
            P(30, "Basement uranium", H(HostRock.Basement, HostRock.Metamorphic), GeologicalHistory.Ancient | GeologicalHistory.Mineralized, DepositShape.Vein, 0, C(MaterialId.Uranium, 1400)),
            P(31, "Igneous titanium", H(HostRock.Mafic), GeologicalHistory.Intrusive, DepositShape.Lens, 0, C(MaterialId.Titanium, 4000)),
            P(32, "Shelf aggregate", H(HostRock.Clastic, HostRock.Carbonate), GeologicalHistory.Sorted, DepositShape.Bed, 2, C(MaterialId.Stone, 8000)),
            P(33, "Polymetallic nodules", H(HostRock.Clastic), GeologicalHistory.Preserved | GeologicalHistory.MarineFacies, DepositShape.Field, 3, C(MaterialId.Manganese, 3500), C(MaterialId.Nickel, 1500), C(MaterialId.Copper, 1000), C(MaterialId.Cobalt, 250)),
            P(34, "Ferromanganese crust", H(HostRock.Mafic, HostRock.Ultramafic), GeologicalHistory.Preserved, DepositShape.Field, 4, C(MaterialId.Manganese, 3000), C(MaterialId.Cobalt, 1500), C(MaterialId.Nickel, 700)),
            P(35, "Seafloor massive sulfide", H(HostRock.Mafic, HostRock.Ultramafic, HostRock.Alkaline), GeologicalHistory.Hydrothermal | GeologicalHistory.Mineralized, DepositShape.Lens, 5, C(MaterialId.Copper, 2600), C(MaterialId.Zinc, 2200), C(MaterialId.Gold, 50), C(MaterialId.Silver, 100)),
            P(36, "Relict nitrate basin", H(HostRock.Clastic), GeologicalHistory.Evaporite | GeologicalHistory.Preserved | GeologicalHistory.Source, DepositShape.Bed, 0, C(MaterialId.Nitrate, 4000)),
            P(37, "Sedimentary ironstone", H(HostRock.Clastic, HostRock.Carbonate), GeologicalHistory.Basin, DepositShape.Bed, 0, C(MaterialId.Iron, 3800)),
            P(38, "Weathered iron ore", H(HostRock.Mafic, HostRock.Ultramafic, HostRock.Basement), GeologicalHistory.Weathered | GeologicalHistory.Preserved, DepositShape.Bed, 0, C(MaterialId.Iron, 4500)),
            P(39, "Titaniferous magnetite", H(HostRock.Mafic, HostRock.Ultramafic), GeologicalHistory.Intrusive, DepositShape.Lens, 0, C(MaterialId.Iron, 5200), C(MaterialId.Titanium, 1200)),
            P(40, "Bog iron", Any, GeologicalHistory.None, DepositShape.Bed, 7, C(MaterialId.Iron, 2800)),
            new GeologicalProfile(100, "Source-derived placer", Any, GeologicalHistory.None, DepositShape.Bed, 1, true)
        });
        public static string Hash => ResourceCatalog.Digest(string.Join(";", All.OrderBy(p => p.Id).Select(p => p.Id + ":" + p.Name + ":" + string.Join(",", p.Hosts.Select(h => (int)h)) + ":" + (int)p.Required + ":" + (int)p.Shape + ":" + p.Domain + ":" + p.Secondary + ":" + string.Join(",", p.Composition.Select(c => (int)c.Material + "=" + c.PartsPerTenThousand)))));
    }
}
