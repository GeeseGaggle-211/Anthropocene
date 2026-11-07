using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Anthropocene.Simulation;

// Read-only diagnostics plus explicit configuration counterfactuals. Never used
// by play or training; links the real simulation and writes only its own output.
internal static class Program
{
    private static string Option(string[] args, string key, string fallback)
        => args.FirstOrDefault(a => a.StartsWith(key + "=", StringComparison.Ordinal))?.Substring(key.Length + 1) ?? fallback;

    private static void Main(string[] args)
    {
        var json = new JsonSerializerOptions { IncludeFields = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
        string output = Option(args, "--output", "TestResults/FishingRegression/probe.jsonl");
        string load = Option(args, "--load", "");
        int seed = int.Parse(Option(args, "--seed", "23")), end = int.Parse(Option(args, "--end-turn", "48"));
        int postOpeningTurns = int.Parse(Option(args, "--post-opening-turns", "-1"));
        if (postOpeningTurns < -1 || postOpeningTurns >= 0 && args.Any(a => a.StartsWith("--end-turn=")))
            throw new ArgumentException("Use either --end-turn or nonnegative --post-opening-turns.");
        var saveTurns = new HashSet<int>(Option(args, "--save-turns", "").Split(',').Where(s => s.Length > 0).Select(int.Parse));
        if (File.Exists(output) || File.Exists(output + ".metadata.json")) throw new IOException("Refusing to overwrite evidence: " + output);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
        WorldState w;
        string inputHash = null;
        if (load.Length > 0)
        {
            byte[] bytes = File.ReadAllBytes(load);
            inputHash = Convert.ToHexString(SHA256.HashData(bytes));
            w = SaveCodec.Load(bytes);
        }
        else
        {
            var cfg = JsonSerializer.Deserialize<SimulationConfig>(File.ReadAllText("Assets/_Anthropocene/Resources/OpeningConfig.json"), json);
            cfg.Width = 1024; cfg.Height = 512; cfg.Players = 80;
            cfg.Shape = Enum.Parse<WorldShape>(Option(args, "--shape", "Continents"));
            cfg.NeolithicFishingEnabled = Option(args, "--fishing", "1") == "1";
            cfg.CorrectedAquaticSeasonalPressureEnabled = Option(args, "--pressure", "1") == "1";
            cfg.NeolithicSailingEnabled = false;
            var resources = JsonSerializer.Deserialize<ResourceGenerationOptions>(File.ReadAllText("Assets/_Anthropocene/Resources/ResourceGenerationConfig.json"), json);
            w = new WorldGenerator().Generate(seed, cfg, resources);
        }
        string initialFingerprint = SaveCodec.Fingerprint(w);
        int? openingCompletedTurn = null;
        bool alreadyFinished = w.Finished;
        int measuredPostOpeningTurns = 0;
        object openingCensus = null;
        var census = new List<object>();
        object Census() => w.Camps.GroupBy(c => c.Technology).OrderBy(g => g.Key).Select(g => new {
            technology = g.Key.ToString(), settlements = g.Count(), residents = g.Sum(c => c.Population),
            founders = g.Count(c => c.People.ParentSociety <= 0),
            weightedQoL = g.Sum(c => c.Population * c.QoL) / Math.Max(1, g.Sum(c => c.Population))
        }).ToArray();
        var checkpoints = new Dictionary<int, string>();
        var execution = new PhaseExecution(4); var resolver = new TurnResolver(execution);
        ResolutionMetrics.CaptureDemandAllocations = true;
        using (var writer = new StreamWriter(output))
        while (postOpeningTurns >= 0 ? !w.Finished || measuredPostOpeningTurns < postOpeningTurns : w.Turn <= end)
        {
            bool postOpening = w.Finished;
            var before = w.Camps.Where(c => c.Technology == Technology.Watercraft).ToDictionary(c => c.Id,
                c => new { c.Population, c.Tile, c.PlantFood, c.AquaticFood, c.Food, inventory = c.Inventory.Copy(), practice = c.People.NeolithicExperience.ToArray() });
            var ai = AiPlanning.Decide(w, execution);
            var prepared = TurnPlanner.Prepare(w, ai.Orders, ai.Traces, execution);
            Fission.Diagnostic = new Dictionary<int, Fission.Diag>();
            var record = resolver.Resolve(w, prepared);
            if (postOpening) measuredPostOpeningTurns++;
            if (!alreadyFinished && !openingCompletedTurn.HasValue && w.Finished)
            {
                openingCompletedTurn = record.Turn;
                openingCensus = Census();
            }
            if (postOpeningTurns >= 0 && !w.Finished && record.Turn >= 12)
                throw new InvalidOperationException("Opening did not complete within twelve turns.");
            census.Add(new { turn = record.Turn, postOpening, cohorts = Census() });
            foreach (var c in w.Camps.Where(c => c.Technology == Technology.Watercraft))
            {
                prepared.DemandAllocations.TryGetValue(c.Id, out var allocation);
                before.TryGetValue(c.Id, out var prior); Fission.Diagnostic.TryGetValue(c.Id, out var fission);
                writer.WriteLine(JsonSerializer.Serialize(new {
                    turn = record.Turn, c.Id, c.SocietyId, parentSociety = c.People.ParentSociety, prior, c.Population, c.Tile, c.QoL, c.PlantFood, c.AquaticFood,
                    inventory = c.Inventory, practice = c.People.NeolithicExperience, technologies = c.People.Technologies,
                    craft = c.People.CraftExperience, allocation, assignments = c.Workforce.Assignments,
                    materials = record.MaterialTransfers.Where(t => t.Camp == c.Id),
                    production = record.Production.Where(p => p.Camp == c.Id),
                    demography = record.Demographics.FirstOrDefault(d => d.Camp == c.Id), fission,
                    receipts = record.Transfers.Where(t => t.Entity == c.Id).GroupBy(t => t.Kind).ToDictionary(g => g.Key, g => g.Sum(t => t.Food))
                }, json));
            }
            writer.Flush(); Fission.Diagnostic = null;
            if (saveTurns.Contains(record.Turn))
            {
                string path = output + ".turn-" + record.Turn + ".anthro";
                if (File.Exists(path)) throw new IOException("Refusing to overwrite checkpoint: " + path);
                byte[] bytes = SaveCodec.Save(w); File.WriteAllBytes(path, bytes);
                checkpoints[record.Turn] = Convert.ToHexString(SHA256.HashData(bytes));
            }
            Console.WriteLine("Completed turn " + record.Turn + "; settlements " + w.Camps.Count);
        }
        ResolutionMetrics.CaptureDemandAllocations = false;
        File.WriteAllText(output + ".metadata.json", JsonSerializer.Serialize(new {
            revision = Option(args, "--revision", "unspecified"), seed = w.Seed, inputHash,
            shape = w.Config.Shape.ToString(), w.Config.Width, w.Config.Height, w.Config.Players,
            initialFingerprint, finalFingerprint = SaveCodec.Fingerprint(w), configurationHash = SaveCodec.ConfigurationHash(w.Config),
            endTurn = w.Turn - 1, openingCompletedTurn, checkpoints, worldPopulation = w.Camps.Sum(c => c.Population) + w.Hunters.Sum(h => h.Population),
            requestedPostOpeningTurns = postOpeningTurns, measuredPostOpeningTurns, openingCensus, census,
            w.Config.NeolithicFishingEnabled, w.Config.CorrectedAquaticSeasonalPressureEnabled,
            w.Config.NeolithicSailingEnabled, w.Config.EconomicAllocation,
            artifactSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(output)))
        }, json));
    }
}
