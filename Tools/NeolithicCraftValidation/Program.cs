// Deterministic issue #33 laboratory: controlled finite-input craft witnesses
// plus natural per-society post-unlock policy continuations. No tuning is changed.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Anthropocene.Simulation;

internal static class Program
{
    static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true };
    sealed class Args
    {
        public string Repo = ".", Out = "TestResults/NeolithicCraftValidation", SourceLabel = "working-tree", SourceRevision = "", SimulationSource = "", InputUnlockSaves = "";
        public int SeedLimit = 3, MaxDiscoveryTurns = 60;
        public bool SkipWitnesses;
    }

    static int Main(string[] args)
    {
        try { Run(Parse(args)); return 0; }
        catch (Exception ex) { Console.Error.WriteLine("CRAFT_VALIDATION_FAILURE " + ex.GetType().Name + ": " + ex.Message); return 1; }
    }

    static Args Parse(string[] args)
    {
        var result = new Args();
        for (int i = 0; i < args.Length; i++)
        {
            string Value() => i + 1 < args.Length ? args[++i] : throw new ArgumentException("Missing value for " + args[i]);
            if (args[i] == "--repo") result.Repo = Value();
            else if (args[i] == "--out") result.Out = Value();
            else if (args[i] == "--source-label") result.SourceLabel = Value();
            else if (args[i] == "--source-revision") result.SourceRevision = Value();
            else if (args[i] == "--simulation-source") result.SimulationSource = Value();
            else if (args[i] == "--input-unlock-saves") result.InputUnlockSaves = Value();
            else if (args[i] == "--seed-limit") result.SeedLimit = int.Parse(Value(), CultureInfo.InvariantCulture);
            else if (args[i] == "--max-discovery-turns") result.MaxDiscoveryTurns = int.Parse(Value(), CultureInfo.InvariantCulture);
            else if (args[i] == "--skip-witnesses") result.SkipWitnesses = true;
            else throw new ArgumentException("Unknown option: " + args[i]);
        }
        return result;
    }

    static void Run(Args args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string root = Path.GetFullPath(args.Repo), output = Path.GetFullPath(args.Out);
        if (string.IsNullOrWhiteSpace(args.SourceRevision)) throw new ArgumentException("--source-revision is required to label the measured simulation source.");
        string simulationSource = Path.GetFullPath(string.IsNullOrWhiteSpace(args.SimulationSource)
            ? Path.Combine(root, "Assets/_Anthropocene/Simulation") : args.SimulationSource);
        Directory.CreateDirectory(output);
        var configPath = Path.Combine(root, "Assets/_Anthropocene/Resources/OpeningConfig.json");
        var resourcePath = Path.Combine(root, "Assets/_Anthropocene/Resources/ResourceGenerationConfig.json");
        var config = JsonSerializer.Deserialize<SimulationConfig>(File.ReadAllText(configPath), Json);
        var resources = JsonSerializer.Deserialize<ResourceGenerationOptions>(File.ReadAllText(resourcePath), Json);
        config.Width = 128; config.Height = 64; config.Players = 8; config.StartSeparation = 10; config.Validate();
        File.WriteAllText(Path.Combine(output, "configuration.json"), JsonSerializer.Serialize(config, Json));
        File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(new
        {
            sourceLabel = args.SourceLabel, simulationRevision = args.SourceRevision,
            hostCheckoutHead = Git(root, "rev-parse HEAD"), dirtyPaths = Git(root, "status --short"),
            simulationSourcePath = simulationSource, configHash = SaveCodec.ConfigurationHash(config), sourceHash = HashSources(simulationSource),
            runnerHash = HashFile(typeof(Program).Assembly.Location), configFileHash = HashFile(configPath),
            resourceConfigHash = HashFile(resourcePath), config.Width, config.Height, config.Players,
            seeds = Enumerable.Range(1, args.SeedLimit).ToArray(), postUnlockTurns = 25,
            commandLine = Environment.GetCommandLineArgs(),
            note = "Natural cohorts use each society's first live Cultivation or AnimalHusbandry unlock. The unlock resolution is turn zero; exactly 25 later turn resolutions are measured."
        }, Json));

        if (!args.SkipWitnesses) PhysicalChainWitnesses.Write(output, config, resources);
#if ISSUE33_DIAGNOSTICS
        ResolutionMetrics.CaptureCraftWorkDiagnostics = true;
#endif
        var summaryRows = new List<string> { "seed,camp,player,route,unlock_rule,unlock_turn,unlock_tile,unlock_habitat,branch,window_turn,source_counts,mobility_at_unlock,mobility_after_turn,linked_population,residents,start_tile,end_tile,is_village,field_q_start,field_q_end,field_e_start,field_e_end,land_condition_start,land_condition_end,food_satisfaction,water_satisfaction,clothing_satisfaction,housing_satisfaction,qol,food_meals,food_manufacturing,food_reserves_start,food_reserves_end,reserve_target,reserve_fill_target,growth_target,growth_used,assigned_worker_years,craft_worker_years,material_worker_years,gather_worker_years,farmer_reservation_worker_years,farmer_maintenance_worker_years,farmer_harvest_equiv_worker_years,farmer_expansion_worker_years,herder_assigned_worker_years,herder_service_actual_worker_years,herder_tool_covered_worker_years,productive_worker_years,unattributed_worker_years,bridge_credit_worker_years,farm_work_diagnostic_status,labor_audit,useful_demand_goods,forecast_goods,realized_goods,unmet_useful_goods,constraints,moves,deaths,first_useful_output,mastery_events,specialist_productive_roles,cohort_class,unlock_save_sha256,control_action_status" };
        var unlockRows = new List<string> { "seed,camp,player,route,unlock_rule,unlock_turn,tile,habitat,river,moisture,vegetation_low,vegetation_medium,vegetation_high,source_stone,source_clay,source_wood,source_counts,moves,mobile_since_start,population,initial_food,cohort_class,initial_habitat,unlock_save_sha256" };
        var windowSummaryRows = new List<string> { "seed,camp,player,route,unlock_rule,unlock_turn,unlock_tile,unlock_habitat,branch,first_useful_output_turns,first_mastery_turns,first_specialist_assignment_turns,productive_specialist_turns,productive_specialist_extraction_worker_years,longest_sustained_specialist_streak,labor_budget_audit,cohort_class,unlock_save_sha256" };
        var censoredRows = new List<string> { "seed,camp,player,final_population,turns,technology,era,completion_rule,cultivation_unlocked,husbandry_unlocked,craft_tracks,progress_experience,classification" };
        var replayRows = new List<string> { "seed,camp,route,unlock_turn,status,details" };
        var checkpointRows = new List<string>();
        var trace = new List<string>();
        if (!string.IsNullOrWhiteSpace(args.InputUnlockSaves))
        {
            string checkpointPath = Path.Combine(Path.GetFullPath(args.InputUnlockSaves), "unlock-saves.jsonl");
            foreach (string line in File.ReadAllLines(checkpointPath))
            {
                using (var doc = JsonDocument.Parse(line))
                {
                    var item = doc.RootElement;
                    int seed = item.GetProperty("seed").GetInt32(), campId = item.GetProperty("camp").GetInt32();
                    int unlockTurn = item.GetProperty("unlockTurn").GetInt32(), tile = item.GetProperty("tile").GetInt32();
                    string route = item.GetProperty("route").GetString(), rule = item.GetProperty("rule").GetString();
                    string habitat = item.GetProperty("habitat").GetString(), sourceCounts = item.GetProperty("sourceCounts").GetString();
                    string mobility = item.GetProperty("mobility").GetString(), cohortClass = item.GetProperty("cohortClass").GetString();
                    byte[] state = Convert.FromBase64String(item.GetProperty("saveBase64").GetString());
                    string saveHash = HashBytes(state);
                    if (!string.Equals(saveHash, item.GetProperty("unlockSaveSha256").GetString(), StringComparison.Ordinal)) throw new InvalidOperationException("Unlock checkpoint hash mismatch.");
                    var pairedWorld = SaveCodec.Load(state); var pairedCamp = pairedWorld.Camps.Single(c => c.Id == campId);
                    var sources = MaterialEconomy.KnownSources(pairedWorld, pairedCamp, pairedWorld.Days).ToArray();
                    unlockRows.Add(Csv(seed, campId, pairedCamp.Player, route, rule, unlockTurn, tile, habitat,
                        "unavailable", "unavailable", "unavailable", "unavailable", "unavailable",
                        sources.Where(s => s.Material == MaterialId.Stone).Sum(s => s.Available),
                        sources.Where(s => s.Material == MaterialId.Clay).Sum(s => s.Available),
                        sources.Where(s => s.Material == MaterialId.Wood).Sum(s => s.Available), sourceCounts,
                        pairedCamp.Moves, mobility, pairedCamp.Population, pairedCamp.Food, cohortClass, habitat, saveHash));
                    replayRows.Add(Csv(seed, campId, route, unlockTurn, "PASS", "loaded-identical-unlock-save sha256=" + saveHash));
                    checkpointRows.Add(line);
                    foreach (string branch in Branches(cohortClass))
                        RunWindow(state, campId, seed, route, rule, unlockTurn, tile, habitat, sourceCounts, mobility,
                            branch, cohortClass, saveHash, summaryRows, windowSummaryRows, trace);
                }
            }
            goto CohortsComplete;
        }
        for (int seed = 1; seed <= args.SeedLimit; seed++)
        {
            Console.WriteLine("GENERATE seed=" + seed);
            var world = new WorldGenerator().Generate(seed, Clone(config), resources);
            var captured = new HashSet<int>();
            var resolver = new TurnResolver(new PhaseExecution());
            for (int turn = 0; turn < args.MaxDiscoveryTurns && world.Camps.Any(c => world.LinkedPopulation(c) > 0); turn++)
            {
                var prior = world.Camps.ToDictionary(c => c.Id, c => new
                {
                    Cultivation = c.Technologies[(int)Technology.Cultivation],
                    Husbandry = c.Technologies[(int)Technology.AnimalHusbandry]
                });
                var planning = AiPlanning.Decide(world, new PhaseExecution());
                var prepared = TurnPlanner.Prepare(world, planning.Orders, planning.Traces, new PhaseExecution());
                var record = resolver.Resolve(world, prepared);
                foreach (var camp in world.Camps.OrderBy(c => c.Id))
                {
                    if (captured.Contains(camp.Id) || !prior.TryGetValue(camp.Id, out var was)) continue;
                    bool cultivation = !was.Cultivation && camp.Technologies[(int)Technology.Cultivation];
                    bool husbandry = !was.Husbandry && camp.Technologies[(int)Technology.AnimalHusbandry];
                    if (!cultivation && !husbandry) continue;
                    captured.Add(camp.Id);
                    string route = cultivation && husbandry ? "Cultivation+AnimalHusbandry" : cultivation ? "Cultivation" : "AnimalHusbandry";
                    string rule = camp.People.Progression.CompletionRule ?? "none";
                    var sources = MaterialEconomy.KnownSources(world, camp, world.Days);
                    int tile = camp.Tile;
                    var habitat = DescribeHabitat(world, camp);
                    string sourceCounts = "Stone=" + sources.Count(s => s.Material == MaterialId.Stone) + ";Clay=" + sources.Count(s => s.Material == MaterialId.Clay) + ";Wood=" + sources.Count(s => s.Material == MaterialId.Wood);
                    byte[] state = SaveCodec.Save(world);
                    string saveHash = HashBytes(state);
                    unlockRows.Add(Csv(seed, camp.Id, camp.Player, route, rule, record.Turn, tile, habitat.Name,
                        habitat.River, habitat.Moisture, habitat.Low, habitat.Medium, habitat.High,
                        sources.Where(s => s.Material == MaterialId.Stone).Sum(s => s.Available),
                        sources.Where(s => s.Material == MaterialId.Clay).Sum(s => s.Available),
                        sources.Where(s => s.Material == MaterialId.Wood).Sum(s => s.Available), sourceCounts,
                        camp.Moves, camp.Moves > 0 ? "mobile" : "stationary-at-unlock", camp.Population, camp.Food, "natural-activity-discovery", habitat.Name, saveHash));
                    checkpointRows.Add(JsonSerializer.Serialize(new { seed, camp = camp.Id, player = camp.Player, route, rule,
                        unlockTurn = record.Turn, tile, habitat = habitat.Name, sourceCounts,
                        mobility = camp.Moves > 0 ? "mobile" : "stationary-at-unlock", cohortClass = "natural-activity-discovery",
                        unlockSaveSha256 = saveHash, saveBase64 = Convert.ToBase64String(state) }, Json));
                    replayRows.Add(Csv(seed, camp.Id, route, record.Turn, "PASS", CraftReplayChecks.Verify(state, camp.Id)));
                    foreach (string branch in Branches("natural-activity-discovery"))
                    {
                        Console.WriteLine("WINDOW seed=" + seed + " camp=" + camp.Id + " route=" + route + " branch=" + branch);
                        RunWindow(state, camp.Id, seed, route, rule, record.Turn, tile, habitat.Name, sourceCounts,
                            camp.Moves > 0 ? "mobile" : "stationary-at-unlock", branch, "natural-activity-discovery", saveHash, summaryRows, windowSummaryRows, trace);
                    }
                }
                if (captured.Count == world.Camps.Count) break;
            }
            foreach (var camp in world.Camps.OrderBy(c => c.Id).Where(c => !captured.Contains(c.Id)))
                censoredRows.Add(Csv(seed, camp.Id, camp.Player, world.LinkedPopulation(camp), world.Turn - 1,
                    camp.Technology, camp.People.Progression.Era, camp.People.Progression.CompletionRule ?? "none",
                    camp.Technologies[(int)Technology.Cultivation], camp.Technologies[(int)Technology.AnimalHusbandry],
                    string.Join("|", camp.People.NeolithicTechnologies.OrderBy(x => x, StringComparer.Ordinal)),
                    string.Join("|", camp.People.CraftExperience.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Key + ":" + x.Value.ToString("R", CultureInfo.InvariantCulture))),
                    world.LinkedPopulation(camp) <= 0 ? "dead-before-first-craft-route-unlock" : "unresolved-no-craft-route-unlock-within-cap"));
            File.WriteAllLines(Path.Combine(output, "unlock.csv"), unlockRows);
            File.WriteAllLines(Path.Combine(output, "trajectory.csv"), summaryRows);
            File.WriteAllLines(Path.Combine(output, "trajectory.jsonl"), trace);
            File.WriteAllLines(Path.Combine(output, "window-summary.csv"), windowSummaryRows);
            File.WriteAllLines(Path.Combine(output, "censored.csv"), censoredRows);
            File.WriteAllLines(Path.Combine(output, "replay.csv"), replayRows);
        }
        var generatedControls = GeneratedSiteControls.Capture(Clone(config), resources, Math.Min(args.MaxDiscoveryTurns, 24));
        foreach (var boundary in generatedControls)
        {
            if (boundary.Save == null)
            {
                censoredRows.Add(Csv(boundary.Seed, boundary.CampId, 0, boundary.Population, Math.Min(args.MaxDiscoveryTurns, 24), boundary.Route, "Paleolithic", boundary.Rule, false, false, "", "", boundary.Status));
                continue;
            }
            string boundarySaveHash = HashBytes(boundary.Save);
            unlockRows.Add(Csv(boundary.Seed, boundary.CampId, 0, boundary.Route, boundary.Rule, boundary.UnlockTurn, boundary.Tile,
                boundary.Habitat, boundary.River, boundary.Moisture, boundary.Low, boundary.Medium, boundary.High,
                boundary.StoneAvailable, boundary.ClayAvailable, boundary.WoodAvailable, boundary.SourceCounts, boundary.Moves,
                boundary.Moves > 0 ? "mobile" : "stationary-from-start", boundary.Population, boundary.Food,
                boundary.Scenario, boundary.InitialHabitat, boundarySaveHash));
            replayRows.Add(Csv(boundary.Seed, boundary.CampId, boundary.Route, boundary.UnlockTurn, "PASS", CraftReplayChecks.Verify(boundary.Save, boundary.CampId)));
            foreach (string branch in Branches(boundary.Scenario))
            {
                RunWindow(boundary.Save, boundary.CampId, boundary.Seed, boundary.Route, boundary.Rule, boundary.UnlockTurn,
                    boundary.Tile, boundary.Habitat, boundary.SourceCounts, boundary.Mobility, branch, boundary.Scenario, boundarySaveHash,
                    summaryRows, windowSummaryRows, trace);
            }
            checkpointRows.Add(JsonSerializer.Serialize(new { seed = boundary.Seed, camp = boundary.CampId, player = 0,
                route = boundary.Route, rule = boundary.Rule, unlockTurn = boundary.UnlockTurn, tile = boundary.Tile,
                habitat = boundary.Habitat, sourceCounts = boundary.SourceCounts, mobility = boundary.Mobility,
                cohortClass = boundary.Scenario, unlockSaveSha256 = boundarySaveHash, saveBase64 = Convert.ToBase64String(boundary.Save) }, Json));
        }
        var scarcitySeed = SelectScarcityCheckpoint(checkpointRows);
        byte[] scarcityBase = Convert.FromBase64String(scarcitySeed.GetProperty("saveBase64").GetString());
        int scarcityCamp = scarcitySeed.GetProperty("camp").GetInt32(), scarcitySeedId = scarcitySeed.GetProperty("seed").GetInt32();
        int scarcityTurn = scarcitySeed.GetProperty("unlockTurn").GetInt32(), scarcityTile = scarcitySeed.GetProperty("tile").GetInt32();
        string scarcityRoute = scarcitySeed.GetProperty("route").GetString(), scarcityRule = scarcitySeed.GetProperty("rule").GetString();
        string scarcityHabitat = scarcitySeed.GetProperty("habitat").GetString(), scarcitySources = scarcitySeed.GetProperty("sourceCounts").GetString();
        string scarcityMobility = scarcitySeed.GetProperty("mobility").GetString();
        foreach (MaterialId material in new[] { MaterialId.Stone, MaterialId.Clay, MaterialId.Wood })
        {
            var shock = ScarcityControls.Apply(scarcityBase, scarcityCamp, material);
            string shockHash = HashBytes(shock.Save), shockClass = "one-time-source-scarcity-shock-" + material;
            var shockWorld = SaveCodec.Load(shock.Save); var shockCamp = shockWorld.Camps.Single(c => c.Id == scarcityCamp);
            var shockSources = MaterialEconomy.KnownSources(shockWorld, shockCamp, shockWorld.Days).ToArray();
            var shockAvailable = shockSources.Where(s => s.Material == material).Sum(s => s.Available);
            if (shockAvailable > 1e-8) throw new InvalidOperationException("One-time " + material + " scarcity shock left accessible stock at its opening boundary.");
            string shockSourceCounts = "Stone=" + shockSources.Count(s => s.Material == MaterialId.Stone) + ";Clay=" +
                shockSources.Count(s => s.Material == MaterialId.Clay) + ";Wood=" +
                shockSources.Count(s => s.Material == MaterialId.Wood);
            unlockRows.Add(Csv(scarcitySeedId, scarcityCamp, shockCamp.Player, scarcityRoute, scarcityRule, scarcityTurn,
                scarcityTile, scarcityHabitat, "unavailable", "unavailable", "unavailable", "unavailable", "unavailable",
                shockSources.Where(s => s.Material == MaterialId.Stone).Sum(s => s.Available),
                shockSources.Where(s => s.Material == MaterialId.Clay).Sum(s => s.Available),
                shockSources.Where(s => s.Material == MaterialId.Wood).Sum(s => s.Available), shockSourceCounts, shockCamp.Moves, scarcityMobility,
                shockCamp.Population, shockCamp.Food, shockClass, scarcityHabitat, shockHash));
            checkpointRows.Add(JsonSerializer.Serialize(new { seed = scarcitySeedId, camp = scarcityCamp, player = shockCamp.Player,
                route = scarcityRoute, rule = scarcityRule, unlockTurn = scarcityTurn, tile = scarcityTile, habitat = scarcityHabitat,
                sourceCounts = shockSourceCounts, mobility = scarcityMobility, cohortClass = shockClass, shock = shock.Material.ToString(),
                intervention = shock.InterventionId, scenario = shock.Scenario, availableBefore = shock.AvailableBefore,
                depleted = shock.Depleted, availableAfter = shock.AvailableAfter, sourceReceipts = shock.Receipts,
                unlockSaveSha256 = shockHash, saveBase64 = Convert.ToBase64String(shock.Save) }, Json));
            replayRows.Add(Csv(scarcitySeedId, scarcityCamp, scarcityRoute, scarcityTurn, "PASS", shock.InterventionId + "; available=" + F(shock.AvailableBefore) + "->" + F(shock.AvailableAfter)));
            foreach (string branch in Branches(shockClass))
                RunWindow(shock.Save, scarcityCamp, scarcitySeedId, scarcityRoute, scarcityRule, scarcityTurn, scarcityTile,
                    scarcityHabitat, scarcitySources, scarcityMobility, branch, shockClass, shockHash, summaryRows, windowSummaryRows, trace);
        }
CohortsComplete:
#if ISSUE33_DIAGNOSTICS
        ResolutionMetrics.CaptureCraftWorkDiagnostics = false;
#endif
        ValidateCsv("trajectory.csv", summaryRows);
        ValidateCsv("unlock.csv", unlockRows);
        ValidateCsv("window-summary.csv", windowSummaryRows);
        ValidateCsv("censored.csv", censoredRows);
        ValidateCsv("replay.csv", replayRows);
        foreach (var group in summaryRows.Skip(1).Select(ParseCsv).GroupBy(r => string.Join("/", r[0], r[1], r[3], r[8], r[r.Count - 3])))
            if (group.Count() != 25 || group.Select(r => int.Parse(r[9], CultureInfo.InvariantCulture)).Distinct().Count() != 25)
                throw new InvalidOperationException("Trajectory CSV does not contain one complete 25-turn window: " + group.Key);
        File.WriteAllLines(Path.Combine(output, "unlock.csv"), unlockRows);
        File.WriteAllLines(Path.Combine(output, "trajectory.csv"), summaryRows);
        File.WriteAllLines(Path.Combine(output, "trajectory.jsonl"), trace);
        File.WriteAllLines(Path.Combine(output, "window-summary.csv"), windowSummaryRows);
        File.WriteAllLines(Path.Combine(output, "censored.csv"), censoredRows);
        File.WriteAllLines(Path.Combine(output, "replay.csv"), replayRows);
        File.WriteAllLines(Path.Combine(output, "unlock-saves.jsonl"), checkpointRows);
        File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(new
        {
            sourceLabel = args.SourceLabel, simulationRevision = args.SourceRevision,
            hostCheckoutHead = Git(root, "rev-parse HEAD"), dirtyPaths = Git(root, "status --short"),
            simulationSourcePath = simulationSource, sourceHash = HashSources(simulationSource),
            runnerHash = HashFile(typeof(Program).Assembly.Location), configHash = SaveCodec.ConfigurationHash(config),
            configFileHash = HashFile(configPath), resourceConfigHash = HashFile(resourcePath),
            config.Width, config.Height, config.Players, seeds = Enumerable.Range(1, args.SeedLimit).ToArray(),
            postUnlockTurns = 25, commandLine = Environment.GetCommandLineArgs(),
            craftWorkDiagnostics =
#if ISSUE33_DIAGNOSTICS
                "compiled-and-captured",
#else
                "not-compiled-farmer-and-field-work-unavailable",
#endif
            unlocks = unlockRows.Count - 1,
            windowsByCohortClass = windowSummaryRows.Skip(1).Select(ParseCsv).GroupBy(r => r[16]).ToDictionary(g => g.Key, g => g.Count()),
            trajectoryRows = summaryRows.Count - 1, censored = censoredRows.Count - 1,
            replayCheckpoints = replayRows.Count - 1,
            habitatCoverage = unlockRows.Skip(1).Select(ParseCsv).GroupBy(r => r[7]).ToDictionary(g => g.Key, g => g.Count()),
            routeCoverage = unlockRows.Skip(1).Select(ParseCsv).GroupBy(r => r[3]).ToDictionary(g => g.Key, g => g.Count()),
            recipeWitnessRows = File.Exists(Path.Combine(output, "recipe-witnesses.csv")) ? File.ReadAllLines(Path.Combine(output, "recipe-witnesses.csv")).Length - 1 : 0
        }, Json));
        WriteSummary(summaryRows, unlockRows, windowSummaryRows, censoredRows, output);
        Console.WriteLine("PASS windows=" + (windowSummaryRows.Count - 1) + " boundaries=" + (windowSummaryRows.Count - 1) / 3 + " output=" + output);
    }

    static void RunWindow(byte[] state, int campId, int seed, string route, string rule, int unlockTurn,
        int tile, string habitat, string sourceCounts, string mobilityAtUnlock, string branch, string cohortClass, string unlockSaveSha256, List<string> rows,
        List<string> windowSummaries, List<string> trace)
    {
        var world = SaveCodec.Load(state); var execution = new PhaseExecution();
        var resolver = new TurnResolver(execution);
        var firstUseful = new Dictionary<CraftGood, int>();
        var firstMastery = new Dictionary<string, int>(StringComparer.Ordinal);
        var firstSpecialistAssignment = new Dictionary<string, int>(StringComparer.Ordinal);
        var specialistStreak = new Dictionary<string, int>(StringComparer.Ordinal);
        var specialistLongest = new Dictionary<string, int>(StringComparer.Ordinal);
        var specialistProductiveTurns = new Dictionary<string, int>(StringComparer.Ordinal);
        double specialistExtractionTotalYears = 0;
        var laborAuditIssues = new List<string>();
        int windowRowStart = rows.Count;
        for (int index = 1; index <= 25; index++)
        {
            var camp = world.Camps.Single(c => c.Id == campId);
            int startingPopulation = camp.Population;
            int startingTile = camp.Tile;
            double startLandCondition = FieldLandCondition(world, camp), startQ = FieldIntensity(world, camp), startE = FieldCapital(world, camp);
            var tracksBefore = new HashSet<string>(camp.People.NeolithicTechnologies, StringComparer.Ordinal);
            var startingTools = new { camp.Inventory.HandTools, camp.Inventory.Tools, camp.Inventory.FarmTools, camp.Inventory.HerdingTools };
            var decisions = AiPlanning.Decide(world, execution);
            bool policyBranch = branch == "policy" || branch.EndsWith("-policy", StringComparison.Ordinal);
            string requestedControlAction = policyBranch ? "unmodified-policy" : "unresolved";
            if (!policyBranch)
            {
                var orders = decisions.Orders[camp.Player];
                bool stationaryControl = branch.EndsWith("stationary", StringComparison.Ordinal);
                var proposedMove = branch.Contains("move-auto-economy", StringComparison.Ordinal)
                    ? orders.FirstOrDefault(o => o.Entity == camp.Id && o.Kind == OrderKind.Move) : null;
                if (proposedMove == null && branch.Contains("move-auto-economy", StringComparison.Ordinal))
                {
                    var observation = new AgentObservationBuilder().Build(world, camp.Player, new PlanningIndex(world), includeCandidateForecasts: false);
                    proposedMove = LegalActions.Build(observation).First(m => m.Entity == camp.Id).Actions
                        .Where(o => o.Kind == OrderKind.Move && o.Destination != camp.Tile).OrderBy(o => o.Destination).FirstOrDefault();
                    if (proposedMove == null) throw new InvalidOperationException("The mobile control has no legal non-stationary Move action for camp " + camp.Id + ".");
                }
                orders.RemoveAll(o => o.Entity == camp.Id);
                if (proposedMove != null)
                {
                    proposedMove.RequestAutomaticEconomy = true;
                    orders.Add(proposedMove);
                    requestedControlAction = "single-legal-move-with-automatic-economy-hybrid";
                }
                else
                {
                    orders.Add(new Order(OrderKind.Economy, camp.Id) { Economy = ExplicitSchedule(world, camp) });
                    requestedControlAction = stationaryControl ? "single-explicit-economy-stationary" : "single-explicit-economy-no-policy-move-available";
                }
            }
            var prepared = TurnPlanner.Prepare(world, decisions.Orders, decisions.Traces, execution);
            var acceptedFocalOrders = prepared.Orders.Where(o => o.Entity == campId).ToArray();
            bool acceptedControl = policyBranch || requestedControlAction.StartsWith("single-legal-move", StringComparison.Ordinal)
                ? policyBranch || acceptedFocalOrders.Length == 2 && acceptedFocalOrders.Any(o => o.Kind == OrderKind.Move && o.RequestAutomaticEconomy) && acceptedFocalOrders.Any(o => o.Kind == OrderKind.Economy)
                : acceptedFocalOrders.Length == 1 && acceptedFocalOrders[0].Kind == OrderKind.Economy;
            string controlActionStatus = acceptedControl ? "accepted:" + (policyBranch ? "policy" : string.Join("+", acceptedFocalOrders.Select(o => o.Kind.ToString()).OrderBy(x => x, StringComparer.Ordinal)))
                : "INVALID-REJECTED-requested=" + requestedControlAction + ";accepted=" + string.Join("|", acceptedFocalOrders.Select(o => o.Kind.ToString()));
            if (!acceptedControl) throw new InvalidOperationException("Controlled schedule order was rejected: " + controlActionStatus);
            var acceptedEconomy = prepared.Orders.LastOrDefault(o => o.Entity == campId && o.Kind == OrderKind.Economy)?.Economy;
            var acceptedAssignments = (acceptedEconomy?.Assignments ?? camp.Workforce.Assignments)
                .Where(a => a != null && a.Population > 0).GroupBy(a => a.Role, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Sum(a => a.Population), StringComparer.Ordinal);
            foreach (var specialist in acceptedAssignments.Where(x => WorkforceRoles.Specialists.Contains(x.Key) && x.Value > 0))
            {
                string track = EconomicIntents.RequiredTechnology(specialist.Key) ?? NeolithicProduction.Recipes(world.Config).FirstOrDefault(r => r.Specialist == specialist.Key)?.Track;
                if (!string.IsNullOrEmpty(track) && !firstSpecialistAssignment.ContainsKey(track)) firstSpecialistAssignment[track] = index;
            }
            var need = prepared.Needs.TryGetValue(camp.Id, out var frozen) ? frozen : null;
            var plannedGoods = need?.Production == null ? new Dictionary<CraftGood, double>() : need.Production.ToDictionary(k => k.Key, v => v.Value);
            var usefulDemandGoods = need?.Goods == null ? new Dictionary<CraftGood, double>() : need.Goods.ToDictionary(k => k.Key, v => v.Value);
            int acceptedHerders = acceptedAssignments.TryGetValue(WorkforceRoles.Herder, out int scheduledHerders) ? scheduledHerders : 0;
            double years = world.Config.TurnDays / world.Config.DaysPerYear;
            var priorHerdingPlan = HerdingTooling.Plan(world, camp, years, acceptedHerders);
            var beforeMaterials = new { Stone = camp.Inventory.Stone, Clay = camp.Inventory.Clay, Wood = camp.Inventory.Wood };
            var beforeFood = new { camp.PlantFood, camp.AnimalFood, camp.AquaticFood };
            var beforeSources = MaterialEconomy.KnownSources(world, camp, world.Days).Select(s => new { s.Id, s.Tile, Material = s.Material.ToString(), s.Available, s.RecoveryPerYear }).ToArray();
            var beforeFieldResources = FieldResources(world, camp);
            double fixedHousingStart = NeolithicProduction.FixedHousingAt(world, camp.Id, camp.Tile, world.Days);
            double portableHousingStart = camp.Inventory.LightHousingUnits;
            double farmToolsStart = camp.Inventory.FarmTools;
            var acceptedPriorityCamp = new Camp { CraftingPriorities = acceptedEconomy?.Priorities ?? camp.CraftingPriorities };
            var priorPriorities = Enum.GetValues(typeof(CraftGood)).Cast<CraftGood>().ToDictionary(g => g, g => NeolithicProduction.Priority(acceptedPriorityCamp, g));
            bool priorCultivation = CultivationEconomy.Enabled(camp), priorHusbandry = CultivationEconomy.HusbandryEnabled(camp);
            var priorTechnologies = new HashSet<string>(camp.People.NeolithicTechnologies, StringComparer.Ordinal);
            var record = resolver.Resolve(world, prepared);
            var after = world.Camps.Single(c => c.Id == campId);
            var managedHerdReports = record.ManagedHerds.Where(h => h.Camp == campId).ToArray();
            double realizedCoveredHerderYears = managedHerdReports.Sum(h => h.CoveredHerderYears);
            double realizedHerdingTools = managedHerdReports.Sum(h => h.HerdingToolsConsumed);
            if (Math.Abs(realizedCoveredHerderYears - priorHerdingPlan.CoveredHerderYears) > 1e-6 ||
                Math.Abs(realizedHerdingTools - priorHerdingPlan.ToolsConsumed) > 1e-6)
                throw new InvalidOperationException("Managed herd receipts differ from prior locked management plan.");
            var demographic = record.Demographics.Single(d => d.Camp == campId);
            var goodsOut = Enum.GetValues(typeof(CraftGood)).Cast<CraftGood>().ToDictionary(g => g,
                g => record.Production.Where(p => p.Camp == campId && p.Good == g).Sum(p => p.Output));
            var craftYears = record.Production.Where(p => p.Camp == campId).Sum(p => p.WorkerIntervals) * world.Config.BaseDays / world.Config.DaysPerYear;
            var materialYears = record.MaterialTransfers.Where(m => m.Camp == campId).Sum(m => m.WorkerIntervals) * world.Config.BaseDays / world.Config.DaysPerYear;
            var gatheringFood = record.Transfers.Where(t => t.Entity == campId && (t.Kind == "Gathering" || t.Kind == "Marine gathering" || t.Kind == "Aquatic gathering")).Sum(t => t.Food);
            double gatherYears = gatheringFood / Math.Max(1e-12, world.Config.GatherPerPerson * world.Config.TurnDays / world.Config.BaseDays) * world.Config.TurnDays / world.Config.DaysPerYear;
            double farmerUsed = 0, operatingReservation = 0, maintenanceYears = 0, harvestEquivalent = 0,
                expansionYears = 0, bridgeCredit = 0, reserveTarget = 0, reserveFillTarget = 0,
                growthTarget = 0, growthUsed = 0;
            bool farmWorkDiagnosticsAvailable = false;
#if ISSUE33_DIAGNOSTICS
            farmWorkDiagnosticsAvailable = true;
            var field = resolver.LastMetrics.CraftFieldWork != null && resolver.LastMetrics.CraftFieldWork.TryGetValue(campId, out var f) ? f : null;
            var foodFlow = resolver.LastMetrics.CraftFoodFlow != null && resolver.LastMetrics.CraftFoodFlow.TryGetValue(campId, out var ff) ? ff : null;
            farmerUsed = field?.ReconciledFarmerWorkerYears ?? 0;
            operatingReservation = field?.OperatingFarmerReservationWorkerYears ?? 0;
            maintenanceYears = field?.MaintenanceWorkerYears ?? 0;
            harvestEquivalent = field?.HarvestEquivalentWorkerYears ?? 0;
            expansionYears = field?.ExpansionWorkUsedWorkerYears ?? 0;
            bridgeCredit = field?.GatheringBridgeCreditWorkerYears ?? 0;
            reserveTarget = foodFlow?.ReserveTarget ?? 0;
            reserveFillTarget = foodFlow?.ReserveFillTarget ?? 0;
            growthTarget = foodFlow?.GrowthFoodTarget ?? 0;
            growthUsed = foodFlow?.GrowthFoodUsed ?? 0;
#endif
            int priorCohortResidents = need?.Residents ?? startingPopulation;
            double assigned = priorCohortResidents * record.Duration / world.Config.DaysPerYear;
            double intervalYears = world.Config.BaseDays / world.Config.DaysPerYear;
            var roleReceipts = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var receipt in record.Production.Where(p => p.Camp == campId))
                roleReceipts[receipt.Role] = (roleReceipts.TryGetValue(receipt.Role, out double prior) ? prior : 0) + receipt.WorkerIntervals * intervalYears;
            foreach (var receipt in record.MaterialTransfers.Where(m => m.Camp == campId))
                roleReceipts[receipt.Role] = (roleReceipts.TryGetValue(receipt.Role, out double prior) ? prior : 0) + receipt.WorkerIntervals * intervalYears;
            double gathererPool = Math.Max(0, priorCohortResidents - acceptedAssignments.Where(x => x.Key != WorkforceRoles.Gatherer).Sum(x => x.Value)) * years;
            double materialGathererYears = roleReceipts.TryGetValue(WorkforceRoles.Gatherer, out double mg) ? mg : 0;
            double gatheringWork = gatherYears + materialGathererYears;
            int acceptedHerderCount = acceptedAssignments.TryGetValue(WorkforceRoles.Herder, out int acceptedHerderValue) ? acceptedHerderValue : 0;
            double herderAssignedYears = acceptedHerderCount * years;
            double uncoveredHerdCapacity = Math.Max(0, priorHerdingPlan.Capacity -
                priorHerdingPlan.CoveredHerderYears / Math.Max(1e-12, years) * world.Config.Neolithic.StoneHerdingToolAnimalsPerHerder);
            double actualHerderServiceYears = priorHerdingPlan.CoveredHerderYears +
                uncoveredHerdCapacity / Math.Max(1e-12, world.Config.AnimalsPerHerder) * years;
            double otherRoleWork = roleReceipts.Where(x => x.Key != WorkforceRoles.Gatherer && x.Key != WorkforceRoles.Farmer && x.Key != WorkforceRoles.Herder).Sum(x => x.Value);
            // Harvest and maintenance use the same farmer reservation in the
            // production kernel; farmerUsed has already reconciled them with
            // max(maintenance, realized harvest) + distinct expansion work.
            double knownProductive = gatheringWork + actualHerderServiceYears + otherRoleWork;
            double productive = knownProductive + (farmWorkDiagnosticsAvailable ? farmerUsed : 0);
            var budgetIssues = new List<string>();
            if (acceptedAssignments.Values.Sum() > priorCohortResidents)
                budgetIssues.Add("accepted assignment count exceeds prepared prior cohort");
            if (gatheringWork > gathererPool + 1e-7) budgetIssues.Add("Gatherer:" + F(gatheringWork) + ">" + F(gathererPool));
            foreach (var roleReceipt in roleReceipts.Where(x => x.Key != WorkforceRoles.Gatherer && x.Key != WorkforceRoles.Farmer && x.Key != WorkforceRoles.Herder))
            {
                int assignedRole = acceptedAssignments.TryGetValue(roleReceipt.Key, out int count) ? count : 0;
                double roleBudget = assignedRole * years;
                if (roleReceipt.Value > roleBudget + 1e-7) budgetIssues.Add(roleReceipt.Key + ":" + F(roleReceipt.Value) + ">" + F(roleBudget));
            }
            int farmerCount = acceptedAssignments.TryGetValue(WorkforceRoles.Farmer, out int farmers) ? farmers : 0;
            if (farmerUsed > farmerCount * years + 1e-7) budgetIssues.Add("Farmer:" + F(farmerUsed) + ">" + F(farmerCount * years));
            if (actualHerderServiceYears > herderAssignedYears + 1e-7) budgetIssues.Add("Herder actual service exceeds accepted assignment");
            if (productive > assigned + 1e-7) budgetIssues.Add("Total:" + F(productive) + ">" + F(assigned));
            string laborAudit = !farmWorkDiagnosticsAvailable ? "diagnostics-not-compiled" : budgetIssues.Count == 0 ? "within-prior-cohort-budget" : "OVERLAP_OR_BUDGET_VIOLATION:" + string.Join("|", budgetIssues);
            if (budgetIssues.Count > 0) laborAuditIssues.Add("window-turn=" + index + ";" + laborAudit);
            double reserveEnd = demographic.FinalReserves;
            double mealFood = demographic.FoodConsumed;
            double q = FieldIntensity(world, after), e = FieldCapital(world, after);
            var afterFieldResources = FieldResources(world, after);
            var afterSources = MaterialEconomy.KnownSources(world, after, world.Days).Select(s => new { s.Id, s.Tile, Material = s.Material.ToString(), s.Available, s.RecoveryPerYear }).ToArray();
            double fixedHousingEnd = NeolithicProduction.FixedHousingAt(world, after.Id, after.Tile, world.Days);
            double portableHousingEnd = after.Inventory.LightHousingUnits;
            double farmToolsMade = record.Production.Where(p => p.Camp == campId && p.Good == CraftGood.FarmTools).Sum(p => p.Output);
            double farmToolsConsumed = farmToolsStart + farmToolsMade - after.Inventory.FarmTools;
            string constraints = string.Join(";", Enum.GetValues(typeof(CraftGood)).Cast<CraftGood>().Select(g => Constraint(world, after,
                g, usefulDemandGoods.TryGetValue(g, out double demand) ? demand : 0,
                plannedGoods.TryGetValue(g, out double scheduled) ? scheduled : 0, goodsOut[g],
                acceptedAssignments, priorPriorities, priorCultivation, priorHusbandry, priorTechnologies, beforeMaterials,
                record.MaterialTransfers.Where(m => m.Camp == campId).ToArray(), beforeFood))
                .Where(s => !string.IsNullOrEmpty(s)));
            var outputs = record.Production.Where(p => p.Camp == campId && p.Output > 0).ToArray();
            var firstOutputs = new List<string>();
            foreach (var good in outputs.Select(p => p.Good).Distinct()) if (!firstUseful.ContainsKey(good)) { firstUseful[good] = index; firstOutputs.Add(good + ":" + index); }
            foreach (string tech in after.People.NeolithicTechnologies.Except(tracksBefore)) if (!firstMastery.ContainsKey(tech)) firstMastery[tech] = index;
            string mastery = string.Join("|", firstMastery.Where(x => x.Value == index).OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Key + ":" + index));
            var productiveSpecialistReceipts = outputs.Where(p => p.Role != WorkforceRoles.Craftsperson).Select(p => p.Role)
                .Concat(record.MaterialTransfers.Where(m => m.Camp == campId && WorkforceRoles.Specialists.Contains(m.Role)).Select(m => m.Role))
                .Distinct().ToArray();
            double specialistExtractionYears = record.MaterialTransfers.Where(m => m.Camp == campId && WorkforceRoles.Specialists.Contains(m.Role))
                .Sum(m => m.WorkerIntervals) * intervalYears;
            specialistExtractionTotalYears += specialistExtractionYears;
            foreach (string role in WorkforceRoles.Specialists)
            {
                if (productiveSpecialistReceipts.Contains(role))
                {
                    specialistStreak[role] = (specialistStreak.TryGetValue(role, out int priorStreak) ? priorStreak : 0) + 1;
                    specialistProductiveTurns[role] = (specialistProductiveTurns.TryGetValue(role, out int turns) ? turns : 0) + 1;
                    specialistLongest[role] = Math.Max(specialistLongest.TryGetValue(role, out int longest) ? longest : 0, specialistStreak[role]);
                }
                else specialistStreak[role] = 0;
            }
            string specialistRoles = string.Join("|", productiveSpecialistReceipts.OrderBy(s => s, StringComparer.Ordinal));
            string firstOutput = string.Join("|", firstOutputs);
            rows.Add(Csv(seed, campId, after.Player, route, rule, unlockTurn, tile, habitat, branch, index,
                sourceCounts, mobilityAtUnlock, after.Moves > 0 ? "mobile" : "stationary", world.LinkedPopulation(after), after.Population, startingTile, after.Tile, after.IsVillage,
                startQ, q, startE, e, startLandCondition, FieldLandCondition(world, after), after.FoodNeedsMetRatio, after.WaterNeedsMetRatio, after.ClothPerCapita, after.HousingPerCapita, after.QoL,
                mealFood, demographic.ManufacturingFood, demographic.InitialReserves, reserveEnd,
                farmWorkDiagnosticsAvailable ? (object)reserveTarget : "unavailable",
                farmWorkDiagnosticsAvailable ? (object)reserveFillTarget : "unavailable",
                farmWorkDiagnosticsAvailable ? (object)growthTarget : "unavailable",
                farmWorkDiagnosticsAvailable ? (object)growthUsed : "unavailable",
                assigned, craftYears, materialYears, gatherYears,
                farmWorkDiagnosticsAvailable ? (object)operatingReservation : "unavailable",
                farmWorkDiagnosticsAvailable ? (object)maintenanceYears : "unavailable",
                farmWorkDiagnosticsAvailable ? (object)harvestEquivalent : "unavailable",
                farmWorkDiagnosticsAvailable ? (object)expansionYears : "unavailable",
                herderAssignedYears, actualHerderServiceYears, priorHerdingPlan.CoveredHerderYears,
                farmWorkDiagnosticsAvailable ? (object)productive : "partial-known-worker-years",
                farmWorkDiagnosticsAvailable ? (object)(assigned - productive) : "unavailable",
                bridgeCredit, farmWorkDiagnosticsAvailable ? "captured" : "not-compiled", laborAudit,
                Goods(usefulDemandGoods), Goods(plannedGoods), Goods(goodsOut), Goods(Differences(usefulDemandGoods, goodsOut)), constraints,
                after.Moves, demographic.Deaths, firstOutput, mastery, specialistRoles, cohortClass, unlockSaveSha256, controlActionStatus));
            trace.Add(JsonSerializer.Serialize(new
            {
                seed, camp = campId, player = after.Player, route, unlockRule = rule, unlockTurn,
                branch, cohortClass, unlockSaveSha256, requestedControlAction, controlActionStatus, acceptedFocalOrders = acceptedFocalOrders.Select(o => new { kind = o.Kind.ToString(), automaticEconomy = o.RequestAutomaticEconomy }),
                windowTurn = index, worldTurn = record.Turn, unlockTile = tile, startTile = startingTile, tile = after.Tile,
                movement = after.Moves, mobilityControl = branch, population = world.LinkedPopulation(after), residents = after.Population,
                priorCohortResidents, prePrepareResidents = startingPopulation, assignedWorkerYears = assigned,
                assignments = acceptedAssignments, materials = new { before = beforeMaterials,
                    extracted = record.MaterialTransfers.Where(m => m.Camp == campId).Select(m => new { source = m.Source, tile = m.Tile, material = m.Material.ToString(), m.Amount, m.WorkerIntervals, m.Role }),
                    after = new { after.Inventory.Stone, after.Inventory.Clay, after.Inventory.Wood } },
                tools = new { opening = startingTools,
                    made = record.Production.Where(p => p.Camp == campId).Select(p => new { good = p.Good.ToString(), p.Output, p.HandToolsConsumed, p.ToolsConsumed, p.CopperToolsConsumed }),
                    managedHerdReceipts = managedHerdReports.Select(h => new { h.Herd, h.StartingAnimals, h.CoveredHerderYears, h.HerdingToolsConsumed, h.AcceptedFood, h.PotentialFood }),
                    ending = new { after.Inventory.HandTools, after.Inventory.Tools, after.Inventory.FarmTools, after.Inventory.HerdingTools } },
                usefulDemandGoods = usefulDemandGoods.ToDictionary(k => k.Key.ToString(), v => v.Value),
                forecastGoods = plannedGoods.ToDictionary(k => k.Key.ToString(), v => v.Value),
                production = record.Production.Where(p => p.Camp == campId).Select(p => new { good = p.Good.ToString(), p.Role, p.Output, p.WorkerIntervals, p.Stone, p.Clay, p.Wood, p.PlantFood, p.AnimalFood, p.AnimalFabric, p.HandToolsConsumed, p.ToolsConsumed }),
                work = new { craftYears, materialYears, gatherYears, operatingReservation = farmWorkDiagnosticsAvailable ? (double?)operatingReservation : null, maintenanceYears = farmWorkDiagnosticsAvailable ? (double?)maintenanceYears : null, harvestEquivalent = farmWorkDiagnosticsAvailable ? (double?)harvestEquivalent : null, expansionYears = farmWorkDiagnosticsAvailable ? (double?)expansionYears : null, farmerUsed = farmWorkDiagnosticsAvailable ? (double?)farmerUsed : null, herderAssignedWorkerYears = herderAssignedYears, herderServiceEquivalentWorkerYears = actualHerderServiceYears, hypotheticalToolServiceDemandWorkerYears = priorHerdingPlan.ToolServiceDemandYears, productiveWorkerYears = farmWorkDiagnosticsAvailable ? (double?)productive : null, unattributedWorkerYears = farmWorkDiagnosticsAvailable ? (double?)(assigned - productive) : null, gatheringBridgeCreditWorkerYears = bridgeCredit },
                food = new { d = demographic, meals = mealFood, discardedInventory = demographic.DiscardedInventory,
                    cumulativeCapacityClipping = "unavailable-no-exact-kernel-cumulative-clipping-counter",
                    growthUsed, reserveTarget, reserveFillTarget, growthTarget, opening = beforeFood,
                    endingFood = after.Food, plant = after.PlantFood, animal = after.AnimalFood, aquatic = after.AquaticFood },
                field = new { q, e, opening = beforeFieldResources, closing = afterFieldResources, landCondition = FieldLandCondition(world, after) },
                shelter = new { fixedOpening = fixedHousingStart, portableOpening = portableHousingStart,
                    fixedEnding = fixedHousingEnd, portableEnding = portableHousingEnd,
                    combinedOpening = fixedHousingStart + portableHousingStart, combinedEnding = fixedHousingEnd + portableHousingEnd },
                finiteSources = new { opening = beforeSources, ending = afterSources },
                farmTools = new { opening = farmToolsStart, made = farmToolsMade, consumedByBalance = farmToolsConsumed, ending = after.Inventory.FarmTools },
                constraints, firstOutput, mastery, specialistRoles
            }, Json));
        }
        windowSummaries.Add(Csv(seed, campId, world.Camps.Single(c => c.Id == campId).Player, route, rule, unlockTurn,
            tile, habitat, branch,
            string.Join("|", firstUseful.OrderBy(x => x.Key).Select(x => x.Key + ":" + x.Value)),
                string.Join("|", firstMastery.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Key + ":" + x.Value)),
                string.Join("|", firstSpecialistAssignment.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Key + ":" + x.Value)),
                string.Join("|", specialistProductiveTurns.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Key + ":" + x.Value)),
                specialistExtractionTotalYears,
            specialistLongest.Count == 0 ? 0 : specialistLongest.Values.Max(),
            laborAuditIssues.Count == 0 ? "PASS" : string.Join("|", laborAuditIssues), cohortClass, unlockSaveSha256));
        if (rows.Count - windowRowStart != 25) throw new InvalidOperationException("Expected exactly 25 post-unlock rows.");
    }

    static EconomicIntent ExplicitSchedule(WorldState world, Camp camp)
    {
        var intent = EconomicIntent.From(camp);
        intent.Assignments = new List<PopulationAssignment>();
        bool agriculture = CultivationEconomy.Enabled(camp), husbandry = CultivationEconomy.HusbandryEnabled(camp);
        intent.Priorities = new CraftingPriorities
        {
            HandTools = 1, Pots = 1, Cloth = agriculture ? 1 : 0, FarmTools = agriculture ? 1 : 0,
            Housing = agriculture ? 1 : 0, Tools = 1, AnimalFabric = husbandry ? 1 : 0,
            LightHousing = husbandry ? 1 : 0, HerdingTools = husbandry ? 1 : 0
        };
        intent.AutomationTurns = 0;
        intent.Labor = null;
        // Keep the schedule control focused on explicit recipe priorities while
        // the existing planner allocates real residents, material gatherers,
        // tools, food and fields against this exact world boundary.
        return EconomicPlanner.Plan(world, camp, intent);
    }

    static string Constraint(WorldState world, Camp camp, CraftGood good, double usefulDemand, double scheduled, double actual,
        Dictionary<string, int> assignments, Dictionary<CraftGood, double> priorPriorities, bool priorCultivation, bool priorHusbandry,
        HashSet<string> priorTechnologies, object beforeMaterials,
        MaterialReceipt[] materialTransfers, object beforeFood)
    {
        if (usefulDemand <= 1e-9 || actual + 1e-8 >= usefulDemand) return "";
        var recipe = NeolithicProduction.Recipes(world.Config).Single(r => r.Good == good);
        if (!priorPriorities.TryGetValue(good, out double priority) || priority <= 0) return good + ":priority-off-at-resolution-start";
        if (recipe.AgricultureOnly && !priorCultivation || recipe.HusbandryOnly && !priorHusbandry) return good + ":route-disabled-at-resolution-start";
        bool mastered = !string.IsNullOrEmpty(recipe.Track) && priorTechnologies.Contains(recipe.Track);
        if (recipe.SpecialistOnly && !mastered) return good + ":specialist-technology-missing-at-resolution-start";
        int generalists = assignments.TryGetValue(WorkforceRoles.Craftsperson, out int g) ? g : 0;
        int specialist = assignments.TryGetValue(recipe.Specialist, out int s) ? s : 0;
        if (generalists <= 0 && (specialist <= 0 || !mastered)) return good + ":no-eligible-worker-at-resolution-start";
        if (actual > 0) return good + ":partial-output-constraint-unresolved";
        var material = beforeMaterials.GetType();
        double openingStone = (double)material.GetProperty("Stone").GetValue(beforeMaterials);
        double openingClay = (double)material.GetProperty("Clay").GetValue(beforeMaterials);
        double openingWood = (double)material.GetProperty("Wood").GetValue(beforeMaterials);
        if (recipe.Stone > 0 && openingStone + materialTransfers.Where(m => m.Material == MaterialId.Stone).Sum(m => m.Amount) < recipe.Stone - 1e-9 ||
            recipe.Clay > 0 && openingClay + materialTransfers.Where(m => m.Material == MaterialId.Clay).Sum(m => m.Amount) < recipe.Clay - 1e-9 ||
            recipe.Wood > 0 && openingWood + materialTransfers.Where(m => m.Material == MaterialId.Wood).Sum(m => m.Amount) < recipe.Wood - 1e-9)
            return good + ":material-input-unavailable-or-unresolved";
        var food = beforeFood.GetType();
        double openingPlant = (double)food.GetProperty("PlantFood").GetValue(beforeFood);
        double openingAnimal = (double)food.GetProperty("AnimalFood").GetValue(beforeFood);
        if (recipe.PlantFood > 0 && openingPlant + 1e-9 < recipe.PlantFood || recipe.AnimalFood > 0 && openingAnimal + 1e-9 < recipe.AnimalFood)
            return good + ":opening-food-shortfall-cause-unresolved";
        if (recipe.ConstructionMaterial > 0 &&
            openingWood + openingClay + materialTransfers.Where(m => m.Material == MaterialId.Wood || m.Material == MaterialId.Clay).Sum(m => m.Amount) < recipe.ConstructionMaterial - 1e-9)
            return good + ":material-input-unavailable-or-unresolved";
        if (recipe.AnimalFabric > 0) return good + ":animal-fabric-input-provenance-unresolved";
        if (recipe.HousingMaterial > 0) return good + ":housing-material-input-provenance-unresolved";
        if (actual <= 1e-9 && scheduled <= 1e-9) return good + ":useful-demand-unscheduled-classification-unresolved";
        return good + ":unresolved-capacity-or-allocation";
    }

    static SimulationConfig Clone(SimulationConfig config) => JsonSerializer.Deserialize<SimulationConfig>(JsonSerializer.Serialize(config, Json), Json);
    static string Goods(Dictionary<CraftGood, double> goods) => string.Join("|", goods.OrderBy(x => x.Key).Select(x => x.Key + ":" + x.Value.ToString("R", CultureInfo.InvariantCulture)));
    static string F(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    static Dictionary<CraftGood, double> Differences(Dictionary<CraftGood, double> demand, Dictionary<CraftGood, double> actual)
        => Enum.GetValues(typeof(CraftGood)).Cast<CraftGood>().ToDictionary(g => g,
            g => Math.Max(0, (demand.TryGetValue(g, out double d) ? d : 0) - (actual.TryGetValue(g, out double a) ? a : 0)));

    sealed class Habitat
    {
        public string Name; public bool River; public double Moisture; public int Low, Medium, High;
    }
    static Habitat DescribeHabitat(WorldState w, Camp c)
    {
        var ids = w.Map.Radius(c.Tile, 1).Where(i => w.Tiles[i].TraversableLand).OrderBy(i => i).ToArray();
        var tiles = ids.Select(i => w.Tiles[i]).ToArray();
        var result = new Habitat { River = tiles.Any(t => t.River || t.Oasis), Moisture = tiles.Length == 0 ? 0 : tiles.Average(t => t.Seasons.Average(s => s.Moisture)),
            Low = ids.Count(id => LazyEcology.ProjectEcology(w, id, w.Days).CurrentVegetation == VegetationTier.Low),
            Medium = ids.Count(id => LazyEcology.ProjectEcology(w, id, w.Days).CurrentVegetation == VegetationTier.Medium),
            High = ids.Count(id => LazyEcology.ProjectEcology(w, id, w.Days).CurrentVegetation == VegetationTier.High) };
        var center = w.Tiles[c.Tile];
        result.Name = result.River ? "river" : center.Environment == EnvironmentKind.Desert || result.Moisture < .16 ? "dry-marginal"
            : result.High >= 4 || result.Medium >= 4 ? "woodland-forest" : "open-fertile";
        return result;
    }
    static double FieldIntensity(WorldState w, Camp c)
    {
        var ids = CultivationEconomy.Footprint(w, c); return ids.Length == 0 ? 0 : ids.Average(i => CultivationEconomy.Intensity(w.Config, w.Tiles[i].CultivationCapital));
    }
    static double FieldCapital(WorldState w, Camp c)
    {
        var ids = CultivationEconomy.Footprint(w, c); return ids.Length == 0 ? 0 : ids.Average(i => w.Tiles[i].CultivationCapital);
    }
    static double FieldLandCondition(WorldState w, Camp c)
    {
        var ids = CultivationEconomy.Footprint(w, c).Where(i => w.Tiles[i].Land).ToArray(); return ids.Length == 0 ? 0 : ids.Average(i => w.Tiles[i].LandCondition);
    }
    static object FieldResources(WorldState w, Camp c)
    {
        var ids = CultivationEconomy.Footprint(w, c).OrderBy(i => i).ToArray();
        double food = 0, potentialFood = 0;
        foreach (int id in ids)
        {
            var view = LazyEcology.ProjectEcology(w, id, w.Days);
            food += view.Food; potentialFood += view.PotentialFood;
        }
        return new { operatedTiles = ids.Length, projectedFood = food, projectedBiomassPotential = potentialFood };
    }
    static string Csv(params object[] values) => string.Join(",", values.Select(value => "\"" + Convert.ToString(value, CultureInfo.InvariantCulture).Replace("\"", "\"\"") + "\""));
    static string Git(string root, string arguments)
    {
        try
        {
            var info = new System.Diagnostics.ProcessStartInfo("git", arguments) { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            using (var process = System.Diagnostics.Process.Start(info)) { string output = process.StandardOutput.ReadToEnd(); process.WaitForExit(); return output.Trim(); }
        }
        catch { return "unavailable"; }
    }
    static string HashFile(string path)
    {
        using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path))
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }
    static string HashBytes(byte[] bytes)
    {
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }
    static string[] Branches(string cohortClass) => cohortClass.StartsWith("generated-site-stationary-discovery", StringComparison.Ordinal)
        ? new[] { "site-control-policy", "site-control-legal-move-auto-economy-mobile", "site-control-source-aware-schedule-stationary" }
        : new[] { "policy", "legal-move-auto-economy-mobile", "source-aware-schedule-stationary" };
    static JsonElement SelectScarcityCheckpoint(List<string> checkpointRows)
    {
        foreach (string line in checkpointRows)
        {
            using (var doc = JsonDocument.Parse(line))
            {
                var item = doc.RootElement;
                if (item.GetProperty("cohortClass").GetString() != "natural-activity-discovery") continue;
                byte[] state = Convert.FromBase64String(item.GetProperty("saveBase64").GetString());
                var world = SaveCodec.Load(state); int campId = item.GetProperty("camp").GetInt32();
                var camp = world.Camps.Single(c => c.Id == campId);
                var sources = MaterialEconomy.KnownSources(world, camp, world.Days);
                bool supportsEveryShock = new[] { MaterialId.Stone, MaterialId.Clay, MaterialId.Wood }
                    .All(material => sources.Where(s => s.Material == material).Sum(s => s.Available) > 1e-9);
                if (supportsEveryShock) return item.Clone();
            }
        }
        throw new InvalidOperationException("No natural activity-driven unlock checkpoint has all three locally known source materials for the bounded matched scarcity shocks.");
    }
    static string HashSources(string simulationSource)
    {
        using (var sha = SHA256.Create())
        {
            foreach (string path in Directory.GetFiles(simulationSource, "*.cs").OrderBy(x => x, StringComparer.Ordinal))
            {
                byte[] name = System.Text.Encoding.UTF8.GetBytes(Path.GetFileName(path)); sha.TransformBlock(name, 0, name.Length, name, 0);
                byte[] bytes = File.ReadAllBytes(path); sha.TransformBlock(bytes, 0, bytes.Length, bytes, 0);
            }
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
        }
    }
    static List<string> ParseCsv(string line)
    {
        var values = new List<string>(); var value = new System.Text.StringBuilder(); bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (quoted && ch == '"' && i + 1 < line.Length && line[i + 1] == '"') { value.Append('"'); i++; }
            else if (ch == '"') quoted = !quoted;
            else if (ch == ',' && !quoted) { values.Add(value.ToString()); value.Clear(); }
            else value.Append(ch);
        }
        values.Add(value.ToString()); return values;
    }
    static void ValidateCsv(string name, List<string> lines)
    {
        int expected = ParseCsv(lines[0]).Count;
        for (int i = 1; i < lines.Count; i++)
            if (ParseCsv(lines[i]).Count != expected)
                throw new InvalidOperationException(name + " row " + (i + 1) + " has " + ParseCsv(lines[i]).Count + " cells; expected " + expected + ".");
    }
    static void WriteSummary(List<string> rows, List<string> unlock, List<string> windows, List<string> censored, string output)
    {
        var summary = new List<string> { "# Neolithic craft validation", "", "Natural activity-driven unlocks are identified after resolution. Each captured boundary is followed by exactly 25 later resolutions for policy and source-aware schedule mobile/stationary controls. Four disclosed generated-site stationary discovery controls are reported separately by cohort_class. Controlled recipe witnesses are separated in recipe-witnesses.csv.", "", "| Route | Habitat | Unlocks | 25-turn windows | Coverage |", "|---|---|---:|---:|---|" };
        var unlockValues = unlock.Skip(1).Select(ParseCsv).Where(p => p[21] == "natural-activity-discovery").ToArray();
        foreach (string route in new[] { "Cultivation", "AnimalHusbandry", "Cultivation+AnimalHusbandry" })
        foreach (string habitat in new[] { "river", "open-fertile", "woodland-forest", "dry-marginal" })
        {
            int found = unlockValues.Count(p => p[3] == route && p[7] == habitat);
            summary.Add("| " + route + " | " + habitat + " | " + found + " | " + found * 3 + " | " + (found == 0 ? "GAP" : "observed") + " |");
        }
        summary.Add("");
        summary.Add("Unlocks: " + unlockValues.Length + "; natural 25-turn branch windows: " + (windows.Count - 1) + "; censored/dead cohorts: " + (censored.Count - 1) + ".");
        summary.Add("See unlock.csv for local material source counts and actual movement state at unlock. Zero cells are measured coverage gaps.");
        foreach (int column in new[] { 13, 14, 15 })
        {
            int absent = unlockValues.Count(p => p.Count > column && double.TryParse(p[column], NumberStyles.Float, CultureInfo.InvariantCulture, out double amount) && amount <= 1e-9);
            summary.Add("- No local " + new[] { "Stone", "Clay", "Wood" }[column - 13] + " source at unlock: " + absent + " / " + unlockValues.Length + ".");
        }
        summary.Add("Use trajectory.csv and trajectory.jsonl for per-turn useful demand, forecast/realized production, labor, food provenance, tools, q/E, ecological condition, movement, satisfaction and constraints. unlock-saves.jsonl stores byte-identical checkpoints; --input-unlock-saves replays candidate boundaries under a second SimulationSource. Missing cohorts remain missing; the runner does not fabricate an unlock or tune thresholds.");
        File.WriteAllLines(Path.Combine(output, "summary.md"), summary);
    }
}
