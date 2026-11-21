using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Anthropocene.Simulation;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true };
    private static readonly List<object> stages = new List<object>();
    private static string output;
    private static object State(WorldState w) => new { w.Seed, w.Turn, w.Days, history = w.History.Count,
        population = w.Camps.Sum(c => (long)c.Population) + w.Hunters.Sum(h => (long)h.Population),
        camps = w.Camps.Count, canals = w.Canals?.Count ?? 0, fields = w.IrrigatedFields?.Count ?? 0 };
    private static string HashFile(string path)
    { using var file = File.OpenRead(path); using var hash = SHA256.Create(); return Convert.ToHexString(hash.ComputeHash(file)); }
    private static (string magic, long bytes) Header(string path)
    {
        using var file = File.OpenRead(path); using var zip = new GZipStream(file, CompressionMode.Decompress); using var reader = new BinaryReader(zip);
        string magic = reader.ReadString(); return (magic, magic == "ANTHROPOCENE-12" ? reader.ReadInt64() : reader.ReadInt32());
    }
    private static void Record(string name, Stopwatch watch, WorldState w, object detail)
    {
        var row = new { name, seconds = watch.Elapsed.TotalSeconds, state = State(w), detail,
            managedBytes = GC.GetTotalMemory(false), peakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64 };
        stages.Add(row); File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(stages, Json));
        Console.WriteLine(name + " seconds=" + watch.Elapsed.TotalSeconds.ToString("F2") + " turn=" + w.Turn); Console.Out.Flush();
    }
    private static void Step(WorldState w)
    { var ai = AiPlanning.Decide(w); new TurnResolver().Resolve(w, TurnPlanner.Prepare(w, ai.Orders, ai.Traces)); }
    private static void Save(string path, WorldState w)
    { using var file = File.Create(path); SaveCodec.Save(w, file); }
    private static WorldState Load(string path)
    { using var file = File.OpenRead(path); return SaveCodec.Load(file); }
    private static WorldState RoundTrip(WorldState w, string name)
    {
        string path = Path.Combine(output, name + ".anthro"); var timer = Stopwatch.StartNew();
        string before = SaveCodec.Fingerprint(w);
        Record(name + "-fingerprint", timer, w, new { fingerprint = before });
        timer.Restart(); Save(path, w); var header = Header(path);
        Record(name + "-save", timer, w, new { envelope = header.magic, metadataBytes = header.bytes, compressedBytes = new FileInfo(path).Length, sha256 = HashFile(path) });
        timer.Restart(); var restored = Load(path);
        if (SaveCodec.Fingerprint(restored) != before) throw new Exception("Roundtrip fingerprint mismatch: " + name);
        Record(name + "-load", timer, restored, new { exactFingerprint = true, envelope = header.magic });
        timer.Restart(); Step(w); string next = SaveCodec.Fingerprint(w); Step(restored);
        if (SaveCodec.Fingerprint(restored) != next) throw new Exception("Next-turn replay mismatch: " + name);
        Record(name + "-continuation", timer, restored, new { exactNextTurn = true, fingerprint = next });
        return w;
    }
    private static void Main(string[] args)
    {
        string Arg(string key, string fallback) => args.FirstOrDefault(a => a.StartsWith(key + "="))?.Substring(key.Length + 1) ?? fallback;
        string source = Arg("--source", "TestResults/AgriculturalGameplay80/natural-final.anthro");
        output = Arg("--output", "TestResults/SaveAudit"); Directory.CreateDirectory(output);
        string extendedSource = Arg("--extended-source", null), extendedExpected = Arg("--extended-expected", null);
        string growthManifest = Arg("--growth-manifest", null);
        var sources = Directory.GetFiles("Assets/_Anthropocene/Simulation", "*.cs").OrderBy(x => x).ToDictionary(x => x, HashFile);
        var header = Header(source);
        File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(new { source, sourceSha256 = HashFile(source),
            envelope = header.magic, metadataBytes = header.bytes, maximumMetadataBytes = SaveCodec.MaximumMetadataBytes, sources,
            extendedInput = extendedSource == null ? null : new { path = extendedSource, sha256 = HashFile(extendedSource), expectedFingerprint = extendedExpected },
            growthProvenance = growthManifest == null ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(growthManifest)) }, Json));
        var timer = Stopwatch.StartNew(); var w = Load(source);
        Record("original-load", timer, w, new { envelope = header.magic, metadataBytes = header.bytes });
        timer.Restart(); string fingerprint = SaveCodec.Fingerprint(w);
        string expected = Arg("--expected", "8BD1670A9268D9DE3F72E0D9FE3E94FA8EE367529639B06C1C1E06BBE6A576F0");
        if (!string.Equals(fingerprint, expected, StringComparison.OrdinalIgnoreCase)) throw new Exception("Historical authoritative fingerprint changed");
        Record("historical-fingerprint", timer, w, new { fingerprint, exactHistoricalFingerprint = true });
        w = RoundTrip(w, "recovered"); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        if (extendedSource != null)
        {
            timer.Restart(); w = Load(extendedSource);
            if (!string.Equals(SaveCodec.Fingerprint(w), extendedExpected, StringComparison.OrdinalIgnoreCase))
                throw new Exception("Extended input differs from the native pre-save fingerprint");
            Record("extended-input", timer, w, new { exactNativeFingerprint = true, fingerprint = extendedExpected });
            w = RoundTrip(w, "extended");
        }
        else if (args.Contains("--grow"))
        {
            string probe = Path.Combine(output, "growth.anthro"); bool extended = false;
            for (int batch = 0; batch < 8 && !extended; batch++)
            {
                timer.Restart(); for (int i = 0; i < 5; i++) Step(w);
                Record("native-growth", timer, w, new { turns = 5 });
                timer.Restart(); Save(probe, w); header = Header(probe); extended = header.magic == "ANTHROPOCENE-12";
                Record("growth-save", timer, w, new { envelope = header.magic, metadataBytes = header.bytes, compressedBytes = new FileInfo(probe).Length });
            }
            if (!extended) throw new Exception("Native continuation did not cross the Int32 metadata boundary");
            w = RoundTrip(w, "extended");
        }
        Console.WriteLine("PASS: recovered native save, exact roundtrip and deterministic continuation");
    }
}
