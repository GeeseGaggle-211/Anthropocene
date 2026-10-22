using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using System.IO.Compression;

internal static class FlagOffCompatibilityChecks
{
    private sealed class Campaign
    {
        public string Name { get; set; }
        public string Semantic { get; set; }
        public string MaterialReceipts { get; set; }
        public string WorldFingerprint { get; set; }
        public string CompleteSavePayload { get; set; }
        public int Turns { get; set; }
        public int ReceiptCount { get; set; }
    }

    private static object Field(object value, string name)
    {
        if (value == null) return null;
        var type = value.GetType();
        return type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(value)
            ?? type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(value);
    }

    private static string CanonicalNumber(object value)
    {
        if (value == null) return "null";
        if (value is double d) return d.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        if (value is float f) return f.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    internal static string CampSemantic(object camp)
    {
        var fields = new List<string>();
        foreach (string name in new[] { "Id", "Tile", "SocietyId", "Population", "PlantFood", "AnimalFood", "AquaticFood", "Food", "GathererPopulation", "HunterPopulation", "HerdTarget", "HerdTargetSet", "DaysAtSettlement", "QoL", "FoodNeedsMetRatio", "WaterNeedsMetRatio", "ClothPerCapita", "HousingPerCapita", "FabricConsumed", "HousingDemand", "HousingReplacementDemand", "HousingGrowthDemand", "Technology", "StonePriority", "ClayPriority", "WoodPriority" })
            fields.Add(name + "=" + CanonicalNumber(Field(camp, name)));
        var society = Field(camp, "People");
        var tracks = Field(society, "NeolithicTechnologies") as IEnumerable;
        fields.Add("NeolithicTechnologies=" + (tracks == null ? "" : string.Join(",", tracks.Cast<object>().Select(CanonicalNumber).OrderBy(x => x, StringComparer.Ordinal))));
        var crafting = Field(camp, "CraftingPriorities");
        if (crafting != null)
            fields.Add("Crafting=" + string.Join(",", crafting.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public)
                .OrderBy(x => x.Name, StringComparer.Ordinal).Select(f => f.Name + "=" + CanonicalNumber(f.GetValue(crafting)))));
        var inventory = Field(camp, "Inventory");
        foreach (var f in inventory.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public).OrderBy(x => x.Name, StringComparer.Ordinal))
            if (f.FieldType == typeof(double) || f.FieldType == typeof(int)) fields.Add("Inventory." + f.Name + "=" + CanonicalNumber(f.GetValue(inventory)));
        var workforce = Field(camp, "Workforce");
        var assignments = Field(workforce, "Assignments") as IEnumerable;
        if (assignments != null)
            fields.Add("Workforce=" + string.Join(",", assignments.Cast<object>().Select(a => CanonicalNumber(Field(a, "Role")) + ":" + CanonicalNumber(Field(a, "Population"))).OrderBy(x => x, StringComparer.Ordinal)));
        return string.Join(";", fields);
    }

    private static Campaign RunScenario(Assembly assembly, string name, int seed, int configSize, string allocation, int turns)
    {
        var simulationConfig = assembly.GetType("Anthropocene.Simulation.SimulationConfig", true);
        var config = simulationConfig.GetMethod("Small", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { configSize });
        var allocationField = simulationConfig.GetField("EconomicAllocation");
        allocationField?.SetValue(config, allocation);
        var configType = config.GetType();
        configType.GetField("SettlingGraceOnce")?.SetValue(config, false);
        configType.GetField("PlantTentsEnabled")?.SetValue(config, false);
        var neolithic = Field(config, "Neolithic");
        neolithic?.GetType().GetField("ClothPerLightHousing")?.SetValue(neolithic, 0d);
        configType.GetField("ElderRawMaterialsEnabled")?.SetValue(config, false);
        var generatorType = assembly.GetType("Anthropocene.Simulation.WorldGenerator", true);
        var generator = Activator.CreateInstance(generatorType);
        var generate = generatorType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(m => m.Name == "Generate" && m.GetParameters().Length >= 2 && m.GetParameters()[0].ParameterType == typeof(int));
        var generateArgs = generate.GetParameters().Select((p, i) => i == 0 ? (object)seed : i == 1 ? config : null).ToArray();
        var world = generate.Invoke(generator, generateArgs);
        var worldType = world.GetType();
        var ai = assembly.GetType("Anthropocene.Simulation.AiPlanning", true);
        var decide = ai.GetMethods(BindingFlags.Public | BindingFlags.Static).Single(m => m.Name == "Decide" && m.GetParameters().Length == 3);
        var resolverType = assembly.GetType("Anthropocene.Simulation.TurnResolver", true);
        var resolver = Activator.CreateInstance(resolverType, new object[] { null });
        var resolve = resolverType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(m => m.Name == "Resolve" && m.GetParameters().Length == 3 && m.GetParameters()[0].ParameterType == worldType);
        var semanticTurns = new List<string>();
        var materialTurns = new List<string>();
        int receiptCount = 0;
        for (int turn = 0; turn < turns; turn++)
        {
            var plan = decide.Invoke(null, new object[] { world, null, -1 });
            var orders = Field(plan, "Orders"); var traces = Field(plan, "Traces");
            var record = resolve.Invoke(resolver, new[] { world, orders, traces });
            var camps = ((IEnumerable)Field(world, "Camps")).Cast<object>()
                .OrderBy(c => Convert.ToInt32(Field(c, "Id"))).Select(CampSemantic);
            semanticTurns.Add("turn=" + CanonicalNumber(Field(world, "Turn")) + ";days=" + CanonicalNumber(Field(world, "Days")) + ";" + string.Join("|", camps));
            var transfers = Field(record, "MaterialTransfers") as IEnumerable;
            var receipts = transfers == null ? Array.Empty<string>() : transfers.Cast<object>().Select(r => string.Join(";", new[]
            {
                CanonicalNumber(Field(r, "Camp")), CanonicalNumber(Field(r, "Society")), CanonicalNumber(Field(r, "Tile")),
                CanonicalNumber(Field(r, "Source")), CanonicalNumber(Field(r, "Material")), CanonicalNumber(Field(r, "Role")),
                CanonicalNumber(Field(r, "Amount")), CanonicalNumber(Field(r, "WorkerIntervals")), CanonicalNumber(Field(r, "Phase")),
                CanonicalNumber(Field(r, "RequestId"))
            })).ToArray();
            receiptCount += receipts.Length;
            materialTurns.Add("turn=" + CanonicalNumber(Field(record, "Turn")) + ";" + string.Join("|", receipts));
        }
        var sources = Field(world, "MaterialSources") as IEnumerable;
        string sourceSummary = sources == null ? "" : string.Join("|", sources.Cast<object>().OrderBy(s => (string)Field(s, "Id"), StringComparer.Ordinal)
            .Select(s => string.Join(":", new[] { CanonicalNumber(Field(s, "Id")), CanonicalNumber(Field(s, "Material")),
                CanonicalNumber(Field(s, "Initial")), CanonicalNumber(Field(s, "Remaining")), CanonicalNumber(Field(s, "LastDays")) })));
        return new Campaign { Name = name, Turns = turns, ReceiptCount = receiptCount,
            Semantic = string.Join("\n", semanticTurns) + "\nSOURCES=" + sourceSummary,
            MaterialReceipts = string.Join("\n", materialTurns),
            WorldFingerprint = (string)assembly.GetType("Anthropocene.Simulation.SaveCodec", true)
                .GetMethod("Fingerprint", BindingFlags.Public | BindingFlags.Static).Invoke(null, new[] { world }),
            CompleteSavePayload = CanonicalSavePayload(assembly, world) };
    }

    private static Campaign RunModel9(Assembly assembly, int seed, int players, int turns)
    {
        var environmentType = assembly.GetType("Anthropocene.Simulation.ElderModel9Environment", true);
        var generate = environmentType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == "GenerateOpening" && m.GetParameters().Length == 6);
        var snapshot = (byte[])generate.Invoke(null, new object[] { seed, players, 0, 40, 0, null });
        var environment = Activator.CreateInstance(environmentType, new object[] { null });
        environmentType.GetMethod("Reset").Invoke(environment, new object[] { snapshot, true });
        var villageIds = ((IEnumerable)environmentType.GetMethod("Villages").Invoke(environment, null)).Cast<object>().Select(Convert.ToInt32).ToArray();
        var directiveType = assembly.GetType("Anthropocene.Simulation.ElderDirective", true);
        var genericMap = typeof(Dictionary<,>).MakeGenericType(typeof(int), directiveType);
        var semanticTurns = new List<string>(); var materialTurns = new List<string>(); int count = 0;
        for (int turn = 0; turn < turns; turn++)
        {
            var submitted = Activator.CreateInstance(genericMap);
            foreach (int id in villageIds)
            {
                var directive = Activator.CreateInstance(directiveType);
                directiveType.GetField("ServiceLevels")?.SetValue(directive, new double[6]);
                directiveType.GetField("MovementCandidate")?.SetValue(directive, -2);
                genericMap.GetMethod("Add").Invoke(submitted, new[] { (object)id, directive });
            }
            environmentType.GetMethod("Step").Invoke(environment, new[] { submitted });
            var world = Field(environment, "World");
            var history = ((IEnumerable)Field(world, "History")).Cast<object>().ToArray();
            var record = history.Length == 0 ? null : history[history.Length - 1];
            var camps = ((IEnumerable)Field(world, "Camps")).Cast<object>()
                .OrderBy(c => Convert.ToInt32(Field(c, "Id"))).Select(CampSemantic);
            var sources = Field(world, "MaterialSources") as IEnumerable;
            string sourceSummary = sources == null ? "" : string.Join("|", sources.Cast<object>().OrderBy(s => (string)Field(s, "Id"), StringComparer.Ordinal)
                .Select(s => string.Join(":", new[] { CanonicalNumber(Field(s, "Id")), CanonicalNumber(Field(s, "Material")),
                    CanonicalNumber(Field(s, "Initial")), CanonicalNumber(Field(s, "Remaining")), CanonicalNumber(Field(s, "LastDays")) })));
            semanticTurns.Add("turn=" + CanonicalNumber(Field(world, "Turn")) + ";days=" + CanonicalNumber(Field(world, "Days")) + ";" + string.Join("|", camps) + ";SOURCES=" + sourceSummary);
            var transfers = record == null ? null : Field(record, "MaterialTransfers") as IEnumerable;
            var receipts = transfers == null ? Array.Empty<string>() : transfers.Cast<object>().Select(r => string.Join(";", new[]
            {
                CanonicalNumber(Field(r, "Camp")), CanonicalNumber(Field(r, "Society")), CanonicalNumber(Field(r, "Tile")),
                CanonicalNumber(Field(r, "Source")), CanonicalNumber(Field(r, "Material")), CanonicalNumber(Field(r, "Role")),
                CanonicalNumber(Field(r, "Amount")), CanonicalNumber(Field(r, "WorkerIntervals")), CanonicalNumber(Field(r, "Phase")),
                CanonicalNumber(Field(r, "RequestId"))
            })).ToArray();
            count += receipts.Length;
            materialTurns.Add("turn=" + (record == null ? "none" : CanonicalNumber(Field(record, "Turn"))) + ";" + string.Join("|", receipts));
        }
        return new Campaign { Name = "Model9DemandControl", Turns = turns, ReceiptCount = count,
            Semantic = string.Join("\n", semanticTurns), MaterialReceipts = string.Join("\n", materialTurns),
            WorldFingerprint = (string)assembly.GetType("Anthropocene.Simulation.SaveCodec", true)
                .GetMethod("Fingerprint", BindingFlags.Public | BindingFlags.Static).Invoke(null, new[] { Field(environment, "World") }),
            CompleteSavePayload = CanonicalSavePayload(assembly, Field(environment, "World")) };
    }

    private static string Digest(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string FileDigest(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static string PayloadPartDigest(string payload, string marker)
    {
        int start = payload.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return "missing";
        start += marker.Length;
        int end = marker == ":header=" ? payload.IndexOf(":tail=", start, StringComparison.Ordinal) : payload.Length;
        return Digest(payload.Substring(start, end < 0 ? payload.Length - start : end - start));
    }
    private static string FirstHeaderDifference(string left, string right)
    {
        string Extract(string payload)
        {
            int start = payload.IndexOf(":header=", StringComparison.Ordinal) + 8;
            int end = payload.IndexOf(":tail=", start, StringComparison.Ordinal);
            return payload.Substring(start, end - start);
        }
        var a = XDocument.Parse(Extract(left)).Root?.DescendantsAndSelf().ToArray() ?? Array.Empty<XElement>();
        var b = XDocument.Parse(Extract(right)).Root?.DescendantsAndSelf().ToArray() ?? Array.Empty<XElement>();
        List<string> Flatten(XElement[] elements) => elements.SelectMany(e =>
        {
            string path = string.Join("/", e.AncestorsAndSelf().Reverse().Select(x => x.Name.LocalName));
            var values = e.Attributes().Select(x => path + "/@" + x.Name.LocalName + "=" + x.Value).ToList();
            if (!e.Elements().Any() && e.Value.Length != 0) values.Add(path + "=" + e.Value);
            return values;
        }).ToList();
        var av = Flatten(a); var bv = Flatten(b); int n = Math.Min(av.Count, bv.Count);
        for (int i = 0; i < n; i++)
            if (!string.Equals(av[i], bv[i], StringComparison.Ordinal))
                return "index=" + i + " old=" + av[i].Substring(0, Math.Min(400, av[i].Length)) + " new=" + bv[i].Substring(0, Math.Min(400, bv[i].Length));
        return av.Count == bv.Count ? "unlocated" : "node-count old=" + av.Count + " new=" + bv.Count + " next=" + (av.Count > n ? av[n] : "") + " | " + (bv.Count > n ? bv[n] : "");
    }

    internal static string CanonicalSavePayload(Assembly assembly, object world)
    {
        var save = assembly.GetType("Anthropocene.Simulation.SaveCodec", true)
            .GetMethod("Save", BindingFlags.Public | BindingFlags.Static).Invoke(null, new[] { world }) as byte[];
        using var compressed = new GZipStream(new MemoryStream(save), CompressionMode.Decompress);
        using var reader = new BinaryReader(compressed, Encoding.UTF8, true);
        string magic = reader.ReadString();
        byte flags = (magic == "ANTHROPOCENE-14" || magic == "ANTHROPOCENE-15") ? reader.ReadByte() : (byte)0;
        int headerLength = reader.ReadInt32();
        byte[] header = reader.ReadBytes(headerLength);
        if (header.Length != headerLength) throw new InvalidDataException("Truncated save header in compatibility projection.");
        XDocument document;
        using (var xml = XmlDictionaryReader.CreateBinaryReader(header, XmlDictionaryReaderQuotas.Max))
            document = XDocument.Load(xml, LoadOptions.None);
        XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";
        foreach (var element in document.Descendants().ToArray())
        {
            bool addedFlag = element.Name.LocalName == "ElderRawMaterialsEnabled";
            bool addedTuning = new[] { "ElderRawMaterialUnitsPerResident", "ElderRawMaterialMinimumUnits", "ElderRawMaterialMaximumUnits" }
                .Contains(element.Name.LocalName);
            bool nullReceiptTag = (element.Name.LocalName == "Phase" || element.Name.LocalName == "RequestId") &&
                element.Parent?.Name.LocalName == "MaterialReceipt" &&
                ((string)element.Attribute(xsi + "nil") == "true" || (string)element.Attribute(xsi + "nil") == "1");
            // ConfigurationHash is derived from the serialized SimulationConfig. The new
            // flag and three tuning fields above are intentionally additive and differ
            // between schemas even when raw materials are OFF. Keep every config field
            // comparison (after stripping only those four additions), but omit this
            // derived digest so it cannot make a schema-only difference look physical.
            bool derivedConfigHash = element.Name.LocalName == "ConfigurationHash" &&
                element.Parent?.Name.LocalName == "Generation";
            // Generation stage timings are sampled wall-clock diagnostics, not
            // authoritative state (SaveCodec.Fingerprint already excludes the
            // GenerationReport for this reason). Remove only this exact member;
            // generation identity, stage list, warnings and all other metadata
            // remain part of the complete-header comparison.
            bool elapsedTimings = element.Name.LocalName == "Milliseconds" &&
                element.Parent?.Name.LocalName == "Generation";
            if (addedFlag || addedTuning || nullReceiptTag || derivedConfigHash || elapsedTimings) element.Remove();
        }
        string canonicalHeader = document.ToString(SaveOptions.DisableFormatting);
        byte[] tail;
        using (var remainder = new MemoryStream()) { compressed.CopyTo(remainder); tail = remainder.ToArray(); }
        return magic + ":flags=" + flags.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            ":header=" + canonicalHeader + ":tail=" + Convert.ToBase64String(tail);
    }

    internal static int Run(string referenceAssemblyPath, string reportPath, string baselineCommit, string currentCommit)
    {
        string currentAssemblyPath = Assembly.GetExecutingAssembly().Location;
        var currentContext = new AssemblyLoadContext("flagoff-current", true);
        var baselineContext = new AssemblyLoadContext("flagoff-547d9c4", true);
        try
        {
            var current = currentContext.LoadFromAssemblyPath(Path.GetFullPath(currentAssemblyPath));
            var baseline = baselineContext.LoadFromAssemblyPath(Path.GetFullPath(referenceAssemblyPath));
            var scenarios = new[]
            {
                (Name: "GeneratedDemandControl13", Seed: 11, Config: 6, Allocation: "DemandControl", Turns: 13),
                (Name: "GeneratedDemandControl23", Seed: 23, Config: 6, Allocation: "DemandControl", Turns: 13),
                (Name: "GeneratedDemandControl", Seed: 101, Config: 1, Allocation: "DemandControl", Turns: 6)
            };
            var rows = scenarios.Select(s =>
            {
                var oldRun = RunScenario(baseline, s.Name, s.Seed, s.Config, s.Allocation, s.Turns);
                var newRun = RunScenario(current, s.Name, s.Seed, s.Config, s.Allocation, s.Turns);
                return new { Name = s.Name, Turns = s.Turns, Receipts = oldRun.ReceiptCount,
                    CurrentReceipts = newRun.ReceiptCount,
                    BaselineWorldFingerprint = oldRun.WorldFingerprint, CurrentWorldFingerprint = newRun.WorldFingerprint,
                    SemanticMatch = oldRun.Semantic == newRun.Semantic,
                    MaterialReceiptMatch = oldRun.MaterialReceipts == newRun.MaterialReceipts,
                    CompleteSaveMatch = oldRun.CompleteSavePayload == newRun.CompleteSavePayload,
                    BaselineHeaderSha256 = PayloadPartDigest(oldRun.CompleteSavePayload, ":header="),
                    CurrentHeaderSha256 = PayloadPartDigest(newRun.CompleteSavePayload, ":header="),
                    BaselineTailSha256 = PayloadPartDigest(oldRun.CompleteSavePayload, ":tail="),
                    CurrentTailSha256 = PayloadPartDigest(newRun.CompleteSavePayload, ":tail="),
                    FirstHeaderDifference = FirstHeaderDifference(oldRun.CompleteSavePayload, newRun.CompleteSavePayload),
                    BaselineSaveSha256 = Digest(oldRun.CompleteSavePayload), CurrentSaveSha256 = Digest(newRun.CompleteSavePayload),
                    BaselineSemanticSha256 = Digest(oldRun.Semantic), CurrentSemanticSha256 = Digest(newRun.Semantic),
                    BaselineReceiptsSha256 = Digest(oldRun.MaterialReceipts), CurrentReceiptsSha256 = Digest(newRun.MaterialReceipts) };
            }).ToList();
            var model9Old = RunModel9(baseline, 17, 8, 3);
            var model9New = RunModel9(current, 17, 8, 3);
            rows.Add(new { Name = model9Old.Name, Turns = model9Old.Turns, Receipts = model9Old.ReceiptCount,
                CurrentReceipts = model9New.ReceiptCount, BaselineWorldFingerprint = model9Old.WorldFingerprint,
                CurrentWorldFingerprint = model9New.WorldFingerprint, SemanticMatch = model9Old.Semantic == model9New.Semantic,
                MaterialReceiptMatch = model9Old.MaterialReceipts == model9New.MaterialReceipts,
                CompleteSaveMatch = model9Old.CompleteSavePayload == model9New.CompleteSavePayload,
                BaselineHeaderSha256 = PayloadPartDigest(model9Old.CompleteSavePayload, ":header="),
                CurrentHeaderSha256 = PayloadPartDigest(model9New.CompleteSavePayload, ":header="),
                BaselineTailSha256 = PayloadPartDigest(model9Old.CompleteSavePayload, ":tail="),
                CurrentTailSha256 = PayloadPartDigest(model9New.CompleteSavePayload, ":tail="),
                FirstHeaderDifference = FirstHeaderDifference(model9Old.CompleteSavePayload, model9New.CompleteSavePayload),
                BaselineSaveSha256 = Digest(model9Old.CompleteSavePayload), CurrentSaveSha256 = Digest(model9New.CompleteSavePayload),
                BaselineSemanticSha256 = Digest(model9Old.Semantic), CurrentSemanticSha256 = Digest(model9New.Semantic),
                BaselineReceiptsSha256 = Digest(model9Old.MaterialReceipts), CurrentReceiptsSha256 = Digest(model9New.MaterialReceipts) });
            bool passed = rows.All(r => r.SemanticMatch && r.MaterialReceiptMatch && r.CompleteSaveMatch) &&
                rows.Single(r => r.Name == "GeneratedDemandControl13").Receipts > 0;
            var payload = new { BaselineSourceCommit = baselineCommit, CurrentSourceCommit = currentCommit,
                BaselineAssembly = Path.GetFullPath(referenceAssemblyPath), CurrentAssembly = Path.GetFullPath(currentAssemblyPath),
                BaselineAssemblySha256 = FileDigest(referenceAssemblyPath), CurrentAssemblySha256 = FileDigest(currentAssemblyPath),
                BaselineBuildMvid = baseline.ManifestModule.ModuleVersionId.ToString("N"),
                CurrentBuildMvid = current.ManifestModule.ModuleVersionId.ToString("N"),
                RawMaterialsFlag = false, Passed = passed, Campaigns = rows };
            string json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(reportPath, json + Environment.NewLine);
            Console.WriteLine(json);
            Console.WriteLine("Evidence SHA-256: " + Digest(json));
            return passed ? 0 : 1;
        }
        finally { currentContext.Unload(); baselineContext.Unload(); }
    }
}
