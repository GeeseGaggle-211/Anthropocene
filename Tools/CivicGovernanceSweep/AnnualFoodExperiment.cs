using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Anthropocene.Simulation;

internal static class AnnualFoodExperiment
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true };
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static TurnRecord Step(WorldState w)
    {
        var ai = AiPlanning.Decide(w);
        return new TurnResolver().Resolve(w, TurnPlanner.Prepare(w, ai.Orders, ai.Traces));
    }
    public static void Run(string[] args)
    {
        string Arg(string key, string fallback) => args.FirstOrDefault(a => a.StartsWith(key + "="))?.Substring(key.Length + 1) ?? fallback;
        string output = Arg("--output", "TestResults/AnnualLandFood");
        var seeds = Arg("--seeds", "11,23,47,101,1729").Split(',').Select(int.Parse).ToArray();
        var arms = Arg("--arms", "legacy,annual,annual-fixed").Split(',');
        foreach (var arm in arms) if (!new[] { "legacy", "annual", "annual-fixed" }.Contains(arm)) throw new ArgumentException(arm);
        Directory.CreateDirectory(output);
        string manifest = Path.Combine(output, "manifest.json");
        if (File.Exists(manifest)) throw new IOException("Use a fresh output directory.");
        File.WriteAllText(manifest, JsonSerializer.Serialize(new {
            createdUtc = DateTime.UtcNow, seeds, arms, runtime = Environment.Version.ToString(),
            binarySha256 = Hash(File.ReadAllBytes(typeof(AnnualFoodExperiment).Assembly.Location)),
            sourceSha256 = Hash(Encoding.UTF8.GetBytes(string.Join("\n", Directory.GetFiles("Assets/_Anthropocene/Simulation", "*.cs")
                .OrderBy(p => p, StringComparer.Ordinal).Select(p => p + ":" + Hash(File.ReadAllBytes(p))))))
        }, Json));
        foreach (int seed in seeds)
        {
            string directory = seed == 11 || seed == 23 || seed == 47 ? "extended-a" : "extended-b";
            string root = Path.Combine(Arg("--audit-root", "TestResults/CivicAudit"), directory);
            byte[] checkpoint = File.ReadAllBytes(Path.Combine(root, $"seed-{seed}-full-1000.anthro"));
            var reference = SaveCodec.Load(File.ReadAllBytes(Path.Combine(root, $"seed-{seed}-full-final.anthro")));
            foreach (var arm in arms)
            {
                var w = SaveCodec.Load(checkpoint);
                if (w.Config.AnnualLandFoodEnabled) throw new Exception("Expected legacy checkpoint");
                w.Config.AnnualLandFoodEnabled = arm != "legacy";
                if (arm == "annual-fixed") w.Config.SmoothCalendar = null;
                w.Config.Validate();
                double start = w.Days; int turns = 0; var timer = Stopwatch.StartNew();
                using var rows = new StreamWriter(Path.Combine(output, $"seed-{seed}-{arm}.jsonl"));
                void Row(TurnRecord r) => rows.WriteLine(JsonSerializer.Serialize(new {
                    years = (w.Days - start) / w.Config.DaysPerYear,
                    largest = w.Camps.Where(c => c.IsVillage).Select(c => c.Population).DefaultIfEmpty(0).Max(),
                    population = w.Camps.Sum(c => (long)c.Population) + w.Hunters.Sum(h => (long)h.Population),
                    fields = w.IrrigatedFields?.Count ?? 0, canals = w.Canals?.Count ?? 0,
                    personYears = r?.Demographics.Sum(d => d.PersonYears) ?? 0,
                    foodConsumed = r?.Demographics.Sum(d => d.FoodConsumed) ?? 0,
                    foodHarvest = r?.Transfers.Sum(t => t.Food) ?? 0
                }, Json));
                Row(null);
                while (w.Days + 1e-7 < reference.Days)
                {
                    var record = Step(w); turns++; Row(record);
                }
                TurnResolver.Invariants(w); IrrigationEconomy.Validate(w); CivicGovernance.ValidateInvariants(w);
                bool? legacyParity = arm == "legacy" ? SaveCodec.Fingerprint(w) == SaveCodec.Fingerprint(reference) : (bool?)null;
                if (legacyParity == false) throw new Exception("Legacy continuation changed for seed " + seed);
                byte[] final = SaveCodec.Save(w);
                File.WriteAllBytes(Path.Combine(output, $"seed-{seed}-{arm}-final.anthro"), final);
                var replica = SaveCodec.Load(final);
                Step(w); Step(replica);
                if (SaveCodec.Fingerprint(w) != SaveCodec.Fingerprint(replica)) throw new Exception("Save continuation mismatch");
                File.WriteAllText(Path.Combine(output, $"seed-{seed}-{arm}-result.json"), JsonSerializer.Serialize(new {
                    seed, arm, checkpointSha256 = Hash(checkpoint), finalSha256 = Hash(final),
                    legacyParity, saveContinuation = true, turns, seconds = timer.Elapsed.TotalSeconds
                }, Json));
                Console.WriteLine($"PASS seed={seed} arm={arm} turns={turns} seconds={timer.Elapsed.TotalSeconds:F1}");
            }
        }
    }
}
