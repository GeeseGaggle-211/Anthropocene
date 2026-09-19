using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Anthropocene.Simulation
{
    // Persisted IDs: append new values; never infer identity from catalog order.
    public enum MaterialId : ushort
    {
        Stone = 1, Clay = 2, Wood = 3, Copper = 10, Tin = 11, Gold = 12, Lead = 13, Zinc = 14, Silver = 15,
        Salt = 16, Limestone = 17, Silica = 18, Gypsum = 19, Iron = 20, Sulfur = 21, Nitrate = 22,
        Coal = 30, Petroleum = 31, NaturalGas = 32, Bauxite = 33, Phosphate = 34, Potash = 35, Manganese = 36,
        Nickel = 40, Chromium = 41, Cobalt = 42, Tungsten = 43, Titanium = 44, Uranium = 45, Lithium = 46,
        Graphite = 47, RareEarths = 48, Molybdenum = 50, Rhenium = 51, Selenium = 52, Tellurium = 53, Gallium = 54,
        Germanium = 60, Indium = 61, PlatinumGroup = 62, Niobium = 63, Tantalum = 64, Borate = 65, Gemstones = 66,
        MethaneHydrate = 67, RareEarthMud = 68
    }
    public sealed class MaterialDefinition
    {
        public readonly MaterialId Id;
        public readonly string Name, DiscoveryTechnology;
        public readonly double BaseAccessibleFraction, Concentration;
        public readonly bool Reserved;
        public MaterialDefinition(MaterialId id, string technology, double access, double concentration = 1.0, bool reserved = false)
        { Id = id; Name = id.ToString(); DiscoveryTechnology = technology; BaseAccessibleFraction = access; Concentration = concentration; Reserved = reserved; }
    }
    public static class ResourceCatalog
    {
        public const int Version = 1, DefaultMaximumQuantity = 100000;
        public static readonly IReadOnlyDictionary<MaterialId, double> Concentrations = new Dictionary<MaterialId, double>
        {
            [MaterialId.Stone] = 1.0, [MaterialId.Clay] = 1.0, [MaterialId.Wood] = 1.0,
            [MaterialId.Copper] = 1.0, [MaterialId.Tin] = 1.0, [MaterialId.Gold] = 1.0, [MaterialId.Lead] = 1.0, [MaterialId.Zinc] = 1.0, [MaterialId.Silver] = 1.0,
            [MaterialId.Salt] = 1.0, [MaterialId.Limestone] = 1.0, [MaterialId.Silica] = 1.0, [MaterialId.Gypsum] = 1.0,
            [MaterialId.Iron] = 1.0, [MaterialId.Sulfur] = 1.0, [MaterialId.Nitrate] = 1.0,
            [MaterialId.Coal] = 10.0, [MaterialId.Petroleum] = 1.0, [MaterialId.NaturalGas] = 1.0,
            [MaterialId.Bauxite] = 1.0, [MaterialId.Phosphate] = 1.0, [MaterialId.Potash] = 1.0, [MaterialId.Manganese] = 1.0,
            [MaterialId.Nickel] = 1.0, [MaterialId.Chromium] = 1.0, [MaterialId.Cobalt] = 1.0, [MaterialId.Tungsten] = 1.0, [MaterialId.Titanium] = 1.0, [MaterialId.Uranium] = 1.0,
            [MaterialId.Lithium] = 1.0, [MaterialId.Graphite] = 1.0, [MaterialId.RareEarths] = 1.0,
            [MaterialId.Molybdenum] = 1.0, [MaterialId.Rhenium] = 1.0, [MaterialId.Selenium] = 1.0, [MaterialId.Tellurium] = 1.0, [MaterialId.Gallium] = 1.0,
            [MaterialId.Germanium] = 1.0, [MaterialId.Indium] = 1.0, [MaterialId.PlatinumGroup] = 1.0, [MaterialId.Niobium] = 1.0, [MaterialId.Tantalum] = 1.0, [MaterialId.Borate] = 1.0,
            [MaterialId.Gemstones] = 1.0, [MaterialId.MethaneHydrate] = 1.0, [MaterialId.RareEarthMud] = 1.0
        };
        public static double Concentration(MaterialId id) => Concentrations.TryGetValue(id, out double value) ? value : 1.0;
        private static MaterialDefinition M(MaterialId id, string tech, double fraction, bool reserved = false) => new MaterialDefinition(id, tech, fraction, Concentration(id), reserved);
        public static readonly IReadOnlyList<MaterialDefinition> Materials = Array.AsReadOnly(new[] {
            M(MaterialId.Stone, "", 1), M(MaterialId.Clay, "", 1), M(MaterialId.Wood, "", .25),
            M(MaterialId.Copper, "CopperWorking", .10), M(MaterialId.Tin, "BronzeWorking", .10), M(MaterialId.Gold, "GoldWorking", .10),
            M(MaterialId.Lead, "LeadWorking", .10), M(MaterialId.Zinc, "ZincWorking", .10), M(MaterialId.Silver, "SilverWorking", .10),
            M(MaterialId.Salt, "SaltWorking", .10), M(MaterialId.Limestone, "LimeMaking", .10), M(MaterialId.Silica, "Glassmaking", .10), M(MaterialId.Gypsum, "PlasterMaking", .10),
            M(MaterialId.Iron, "IronWorking", .05), M(MaterialId.Sulfur, "SulfurProcessing", .05), M(MaterialId.Nitrate, "NitrateProcessing", .05),
            M(MaterialId.Coal, "CoalMining", .02), M(MaterialId.Petroleum, "PetroleumExtraction", .02), M(MaterialId.NaturalGas, "GasExtraction", .02),
            M(MaterialId.Bauxite, "AluminiumRefining", .02), M(MaterialId.Phosphate, "PhosphateProcessing", .02), M(MaterialId.Potash, "PotashProcessing", .02), M(MaterialId.Manganese, "ManganeseProcessing", .02),
            M(MaterialId.Nickel, "NickelProcessing", .005), M(MaterialId.Chromium, "ChromiumProcessing", .005), M(MaterialId.Cobalt, "CobaltProcessing", .005),
            M(MaterialId.Tungsten, "TungstenProcessing", .005), M(MaterialId.Titanium, "TitaniumProcessing", .005), M(MaterialId.Uranium, "UraniumProcessing", .005),
            M(MaterialId.Lithium, "LithiumProcessing", .005), M(MaterialId.Graphite, "GraphiteProcessing", .005), M(MaterialId.RareEarths, "RareEarthProcessing", .005),
            M(MaterialId.Molybdenum, "MolybdenumProcessing", .001), M(MaterialId.Rhenium, "RheniumRecovery", .001), M(MaterialId.Selenium, "SeleniumRecovery", .001),
            M(MaterialId.Tellurium, "TelluriumRecovery", .001), M(MaterialId.Gallium, "GalliumRecovery", .001),
            M(MaterialId.Germanium, "GermaniumRecovery", .001, true), M(MaterialId.Indium, "IndiumRecovery", .001, true), M(MaterialId.PlatinumGroup, "PlatinumProcessing", .001, true),
            M(MaterialId.Niobium, "NiobiumProcessing", .001, true), M(MaterialId.Tantalum, "TantalumProcessing", .001, true), M(MaterialId.Borate, "BorateProcessing", .001, true),
            M(MaterialId.Gemstones, "GemWorking", .001, true), M(MaterialId.MethaneHydrate, "HydrateExtraction", .001, true), M(MaterialId.RareEarthMud, "DeepSeaMudProcessing", .001, true)
        });
        private static readonly Dictionary<MaterialId, MaterialDefinition> ById = Materials.ToDictionary(m => m.Id);
        public static MaterialDefinition Get(MaterialId id) => ById.TryGetValue(id, out var value) ? value : throw new ArgumentOutOfRangeException(nameof(id));
        // A capability contract only: does not add research, observations, or knowledge to the opening.
        public static bool Discoverable(MaterialId id, ISet<string> technologies)
        { var m = Get(id); return !m.Reserved && (m.DiscoveryTechnology.Length == 0 || technologies != null && technologies.Contains(m.DiscoveryTechnology)); }
        public static string Hash => Digest(string.Join(";", Materials.Select(m => ((int)m.Id) + ":" + m.Name + ":" + m.DiscoveryTechnology + ":" + m.BaseAccessibleFraction.ToString("R", CultureInfo.InvariantCulture) + ":" + m.Concentration.ToString("R", CultureInfo.InvariantCulture) + ":" + m.Reserved)));
        internal static string Digest(string value)
        { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", ""); }
    }
}
