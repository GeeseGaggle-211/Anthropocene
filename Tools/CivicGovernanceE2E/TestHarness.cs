using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Anthropocene.Simulation;

namespace Anthropocene.CivicGovernanceE2E
{
    internal sealed class E2EAssertionException : Exception
    {
        public E2EAssertionException(string message) : base(message) { }
    }

    internal enum M2Status
    {
        PassOnM2,
        FailUntilM3,
        FailUntilM4
    }

    internal enum TestOutcome
    {
        Pass,
        Fail,
        Crash
    }

    internal sealed class E2ETestCase
    {
        public readonly string Id;
        public readonly int Tier;
        public readonly string Feature;
        public readonly string Name;
        public readonly M2Status ExpectedOnM2;
        public readonly Action Run;

        public E2ETestCase(string id, int tier, string feature, string name, M2Status expectedOnM2, Action run)
        {
            Id = id;
            Tier = tier;
            Feature = feature;
            Name = name;
            ExpectedOnM2 = expectedOnM2;
            Run = run;
        }
    }

    internal sealed class TestResult
    {
        public int Index;
        public string Id;
        public int Tier;
        public string Feature;
        public string Name;
        public M2Status ExpectedOnM2;
        public TestOutcome Outcome;
        public double ElapsedMs;
        public string Message;
    }

    internal static class Assert
    {
        public static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        public static void Check(bool condition, string message)
        {
            if (!condition)
                throw new E2EAssertionException(message);
        }

        public static void Fail(string message)
        {
            throw new E2EAssertionException(message);
        }

        public static void NearRelative(double actual, double expected, double relTolerance, string message)
        {
            if (!IsFinite(actual) || !IsFinite(expected) || !IsFinite(relTolerance) || relTolerance < 0.0)
                throw new E2EAssertionException($"{message} (non-finite or invalid tolerance: actual={actual.ToString("R", CultureInfo.InvariantCulture)}, expected={expected.ToString("R", CultureInfo.InvariantCulture)}, relTol={relTolerance.ToString("R", CultureInfo.InvariantCulture)})");
            if (expected == 0.0)
                throw new E2EAssertionException($"{message} (NearRelative requires non-zero expected; use NearAbs for zero)");
            double diff = Math.Abs(actual - expected);
            double limit = relTolerance * Math.Abs(expected);
            if (diff > limit)
                throw new E2EAssertionException($"{message} (actual={actual.ToString("G17", CultureInfo.InvariantCulture)}, expected={expected.ToString("G17", CultureInfo.InvariantCulture)}, relErr={(diff / Math.Abs(expected)).ToString("G6", CultureInfo.InvariantCulture)} > {relTolerance.ToString("G6", CultureInfo.InvariantCulture)})");
        }

        public static void NearAbs(double actual, double expected, double absTolerance, string message)
        {
            if (!IsFinite(actual) || !IsFinite(expected) || !IsFinite(absTolerance) || absTolerance < 0.0)
                throw new E2EAssertionException($"{message} (non-finite or invalid tolerance: actual={actual.ToString("R", CultureInfo.InvariantCulture)}, expected={expected.ToString("R", CultureInfo.InvariantCulture)}, absTol={absTolerance.ToString("R", CultureInfo.InvariantCulture)})");
            double diff = Math.Abs(actual - expected);
            if (diff > absTolerance)
                throw new E2EAssertionException($"{message} (actual={actual.ToString("G17", CultureInfo.InvariantCulture)}, expected={expected.ToString("G17", CultureInfo.InvariantCulture)}, diff={diff.ToString("G6", CultureInfo.InvariantCulture)} > {absTolerance.ToString("G6", CultureInfo.InvariantCulture)})");
        }

        public static void ExpectThrows<TException>(Action action, string message) where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }
            catch (Exception ex)
            {
                throw new E2EAssertionException($"{message} (expected {typeof(TException).Name}, got {ex.GetType().Name}: {ex.Message})");
            }
            throw new E2EAssertionException($"{message} (expected {typeof(TException).Name}, but no exception was thrown)");
        }

        public static void ExpectThrowsAny(Action action, string message)
        {
            try
            {
                action();
            }
            catch (E2EAssertionException)
            {
                throw;
            }
            catch (Exception)
            {
                return;
            }
            throw new E2EAssertionException($"{message} (expected an exception, but none was thrown)");
        }
    }

    internal static class ConfigLoader
    {
        public static string CliResourcesRoot;
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { IncludeFields = true };

        public static string ResolveResourcesRoot(string explicitOverride = null)
        {
            string candidate = explicitOverride ?? CliResourcesRoot;
            if (!string.IsNullOrEmpty(candidate))
            {
                if (!Directory.Exists(candidate))
                    throw new DirectoryNotFoundException($"Explicit resources root directory does not exist: '{candidate}'.");
                return candidate;
            }

            string env = Environment.GetEnvironmentVariable("ANTHROPOCENE_RESOURCES_ROOT");
            if (!string.IsNullOrEmpty(env) && Directory.Exists(env))
                return env;

            string outRes = Path.Combine(AppContext.BaseDirectory, "Resources");
            if (Directory.Exists(outRes))
                return outRes;

            string baked = Assembly.GetExecutingAssembly()
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "ResourcesRoot")?.Value;
            if (!string.IsNullOrEmpty(baked) && Directory.Exists(baked))
                return baked;

            string dir = Directory.GetCurrentDirectory();
            for (int i = 0; i < 8 && dir != null; i++)
            {
                string found = Path.Combine(dir, "Assets", "_Anthropocene", "Resources");
                if (Directory.Exists(found))
                    return found;
                dir = Directory.GetParent(dir)?.FullName;
            }

            throw new DirectoryNotFoundException("Could not locate Assets/_Anthropocene/Resources.");
        }

        public static string ReadProfileText(string fileName, string resourcesRoot = null)
        {
            string path = Path.Combine(ResolveResourcesRoot(resourcesRoot), fileName);
            return File.ReadAllText(path);
        }

        public static SimulationConfig LoadProfile(string fileName = "CivicGovernanceOpeningConfig.json", string resourcesRoot = null)
        {
            string text = ReadProfileText(fileName, resourcesRoot);
            var cfg = JsonSerializer.Deserialize<SimulationConfig>(text, JsonOptions);
            Assert.Check(cfg != null, $"Deserializing {fileName} returned null SimulationConfig.");
            cfg.Validate();
            return cfg;
        }

        public static ResourceGenerationOptions LoadResourceOptions(string resourcesRoot = null)
        {
            string text = ReadProfileText("ResourceGenerationConfig.json", resourcesRoot);
            var opts = JsonSerializer.Deserialize<ResourceGenerationOptions>(text, JsonOptions);
            Assert.Check(opts != null, "Deserializing ResourceGenerationConfig.json returned null ResourceGenerationOptions.");
            opts.Validate();
            return opts;
        }
    }

    internal static class ProductionReceiptHelpers
    {
        // M2-compile-safe CraftGood ordinals (CraftGood enum ends at CanalMaintenance = 12 in M2; M3/M4 append 13..16).
        public const int OrdinalCivicConstruction = 13;
        public const int OrdinalCivicMaintenance = 14;
        public const int OrdinalSanitationConstruction = 15;
        public const int OrdinalSanitationMaintenance = 16;

        public static bool IsCivicConstruction(ProductionReceipt r)
            => r != null && ((int)r.Good == OrdinalCivicConstruction || string.Equals(r.Good.ToString(), "CivicConstruction", StringComparison.Ordinal));

        public static bool IsCivicMaintenance(ProductionReceipt r)
            => r != null && ((int)r.Good == OrdinalCivicMaintenance || string.Equals(r.Good.ToString(), "CivicMaintenance", StringComparison.Ordinal));

        public static bool IsSanitationConstruction(ProductionReceipt r)
            => r != null && ((int)r.Good == OrdinalSanitationConstruction || string.Equals(r.Good.ToString(), "SanitationConstruction", StringComparison.Ordinal));

        public static bool IsSanitationMaintenance(ProductionReceipt r)
            => r != null && ((int)r.Good == OrdinalSanitationMaintenance || string.Equals(r.Good.ToString(), "SanitationMaintenance", StringComparison.Ordinal));
    }

    // Structural observation-leak checks: exact numeric matches on parsed JSON values (never substring matches, which
    // collide with unrelated digits such as 1.3338... containing "333") and civic property names inside rival entries.
    internal static class ObservationLeak
    {
        private static readonly string[] CivicNameFragments = { "Civic", "Sanitation", "Steward", "ImmigrationPolicy" };

        public static bool ContainsNumber(string json, double value, double tolerance = 1e-12)
        {
            using (var doc = JsonDocument.Parse(json))
                return ContainsNumber(doc.RootElement, value, tolerance);
        }

        private static bool ContainsNumber(JsonElement e, double value, double tolerance)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Number:
                    return e.TryGetDouble(out double d) && Math.Abs(d - value) <= tolerance * Math.Max(1.0, Math.Abs(value));
                case JsonValueKind.Object:
                    foreach (var p in e.EnumerateObject()) if (ContainsNumber(p.Value, value, tolerance)) return true;
                    return false;
                case JsonValueKind.Array:
                    foreach (var x in e.EnumerateArray()) if (ContainsNumber(x, value, tolerance)) return true;
                    return false;
                default:
                    return false;
            }
        }

        // True when any element of the named top-level arrays carries a civic-named property.
        public static bool RivalEntriesHaveCivicFields(string json, params string[] arrays)
        {
            using (var doc = JsonDocument.Parse(json))
            {
                foreach (var name in arrays)
                {
                    if (!doc.RootElement.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array) continue;
                    foreach (var item in arr.EnumerateArray())
                        if (HasCivicName(item)) return true;
                }
            }
            return false;
        }

        private static bool HasCivicName(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in e.EnumerateObject())
                {
                    foreach (var f in CivicNameFragments)
                        if (p.Name.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    if (HasCivicName(p.Value)) return true;
                }
            }
            else if (e.ValueKind == JsonValueKind.Array)
                foreach (var x in e.EnumerateArray()) if (HasCivicName(x)) return true;
            return false;
        }

        // Ids of the top-level "Settlements" array (the observer's own settlements).
        public static int[] SettlementIds(string json)
        {
            using (var doc = JsonDocument.Parse(json))
            {
                if (!doc.RootElement.TryGetProperty("Settlements", out var arr) || arr.ValueKind != JsonValueKind.Array) return Array.Empty<int>();
                return arr.EnumerateArray().Where(x => x.TryGetProperty("Id", out _)).Select(x => x.GetProperty("Id").GetInt32()).ToArray();
            }
        }
    }

    internal static class SaveXmlTools
    {
        public static readonly HashSet<string> AllCivicElementNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Civic",
            "CivicComplexity",
            "CivicStratification",
            "CivicCapital",
            "SanitationCapital",
            "ImmigrationPolicy",
            "CivicFocus",
            "RecentTradeVolume",
            "SanitationWaterRatio",
            "LastEnvironmentalComplexity",
            "LastEnvironmentalStratification",
            "LastCircumscription",
            "CivicExperience",
            "CivicPrecedents",
            "CivicReports",
            "SanitationWater"
        };

        public static byte[] Unzip(byte[] compressed)
        {
            using (var src = new MemoryStream(compressed))
            using (var zip = new GZipStream(src, CompressionMode.Decompress))
            using (var dst = new MemoryStream())
            {
                zip.CopyTo(dst);
                return dst.ToArray();
            }
        }

        public static byte[] Zip(byte[] raw)
        {
            using (var dst = new MemoryStream())
            {
                using (var zip = new GZipStream(dst, CompressionLevel.Fastest, true))
                    zip.Write(raw, 0, raw.Length);
                return dst.ToArray();
            }
        }

        private static void UnpackEnvelope(byte[] savedBytes, out string magic, out bool extended64, out byte[] metaBytes, out byte[] tail)
        {
            byte[] raw = Unzip(savedBytes);
            using (var input = new MemoryStream(raw))
            using (var reader = new BinaryReader(input))
            {
                magic = reader.ReadString();
                extended64 = string.Equals(magic, "ANTHROPOCENE-12", StringComparison.Ordinal);
                long metaLen = extended64 ? reader.ReadInt64() : reader.ReadInt32();
                if (metaLen < 0 || metaLen > int.MaxValue)
                    throw new InvalidDataException("Unexpected save metadata length: " + metaLen);
                metaBytes = reader.ReadBytes((int)metaLen);
                tail = reader.ReadBytes((int)(input.Length - input.Position));
            }
        }

        private static byte[] PackEnvelope(string magic, bool extended64, byte[] newMeta, byte[] tail)
        {
            using (var outPayload = new MemoryStream())
            {
                using (var writer = new BinaryWriter(outPayload))
                {
                    writer.Write(magic);
                    if (extended64) writer.Write((long)newMeta.Length);
                    else writer.Write(newMeta.Length);
                    writer.Write(newMeta);
                    writer.Write(tail);
                    writer.Flush();
                }
                return Zip(outPayload.ToArray());
            }
        }

        private static readonly HashSet<string> ContainerElementNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "WorldState",
            "Config",
            "Camps",
            "Camp",
            "Societies",
            "Society",
            "History",
            "TurnRecord"
        };

        private static void CopyFilteredBinaryXml(XmlDictionaryReader reader, XmlDictionaryWriter writer, HashSet<string> targets)
        {
            while (!reader.EOF)
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    if (targets.Contains(reader.LocalName))
                    {
                        reader.Skip();
                        continue;
                    }
                    if (!ContainerElementNames.Contains(reader.LocalName))
                    {
                        writer.WriteNode(reader, false);
                        continue;
                    }

                    bool isEmpty = reader.IsEmptyElement;
                    writer.WriteStartElement(reader.Prefix, reader.LocalName, reader.NamespaceURI);
                    if (reader.HasAttributes)
                    {
                        for (int i = 0; i < reader.AttributeCount; i++)
                        {
                            reader.MoveToAttribute(i);
                            writer.WriteAttributeString(reader.Prefix, reader.LocalName, reader.NamespaceURI, reader.Value);
                        }
                        reader.MoveToElement();
                    }
                    reader.Read();
                    if (!isEmpty)
                    {
                        while (!reader.EOF && reader.NodeType != XmlNodeType.EndElement)
                            CopyFilteredBinaryXml(reader, writer, targets);
                        if (reader.NodeType == XmlNodeType.EndElement)
                            reader.Read();
                    }
                    writer.WriteEndElement();
                }
                else if (reader.NodeType == XmlNodeType.EndElement)
                {
                    break;
                }
                else
                {
                    reader.Read();
                }
            }
        }

        public static byte[] StripCivicXmlElements(byte[] savedBytes, HashSet<string> elementNames = null)
        {
            var targets = elementNames ?? AllCivicElementNames;
            UnpackEnvelope(savedBytes, out string magic, out bool extended64, out byte[] metaBytes, out byte[] tail);

            byte[] newMeta;
            using (var xmlReader = XmlDictionaryReader.CreateBinaryReader(metaBytes, XmlDictionaryReaderQuotas.Max))
            using (var metaOut = new MemoryStream())
            {
                using (var xmlWriter = XmlDictionaryWriter.CreateBinaryWriter(metaOut, null, null, false))
                {
                    xmlReader.MoveToContent();
                    CopyFilteredBinaryXml(xmlReader, xmlWriter, targets);
                    xmlWriter.Flush();
                }
                newMeta = metaOut.ToArray();
            }
            return PackEnvelope(magic, extended64, newMeta, tail);
        }

        public static byte[] MutateCampXmlElement(byte[] savedBytes, string elementLocalName, string newValue)
        {
            UnpackEnvelope(savedBytes, out string magic, out bool extended64, out byte[] metaBytes, out byte[] tail);

            XDocument doc;
            using (var xmlReader = XmlDictionaryReader.CreateBinaryReader(metaBytes, XmlDictionaryReaderQuotas.Max))
                doc = XDocument.Load(xmlReader);

            var elem = doc.Descendants().FirstOrDefault(e => string.Equals(e.Name.LocalName, elementLocalName, StringComparison.Ordinal));
            Assert.Check(elem != null, $"Expected XML element '{elementLocalName}' in saved WorldState metadata.");
            elem.Value = newValue;

            byte[] newMeta;
            using (var metaOut = new MemoryStream())
            {
                using (var xmlWriter = XmlDictionaryWriter.CreateBinaryWriter(metaOut, null, null, false))
                    doc.Save(xmlWriter);
                newMeta = metaOut.ToArray();
            }
            return PackEnvelope(magic, extended64, newMeta, tail);
        }

        public static string DecodeMetadataXml(byte[] savedBytes)
        {
            UnpackEnvelope(savedBytes, out _, out _, out byte[] metaBytes, out _);
            using (var xmlReader = XmlDictionaryReader.CreateBinaryReader(metaBytes, XmlDictionaryReaderQuotas.Max))
                return XDocument.Load(xmlReader).ToString();
        }

        public static int MeasureTileSectionBytes(byte[] savedBytes, out int tileCount)
        {
            UnpackEnvelope(savedBytes, out _, out _, out _, out byte[] tail);
            using (var ms = new MemoryStream(tail))
            using (var reader = new BinaryReader(ms))
            {
                tileCount = reader.ReadInt32();
                // Remaining tail contains tileCount * TileRecordBytes + 1 byte (hasResources = false when world.Resources == null).
                int remaining = tail.Length - 4;
                return remaining;
            }
        }
    }
}
