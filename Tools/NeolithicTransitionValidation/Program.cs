// Measurement-only Paleo -> Agriculture transition matrix.  It deliberately
// contains no production tuning or alternate economy policy.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Anthropocene.Simulation;

internal static class Program
{
    static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true };
    sealed class Candidate
    {
        public string Habitat;
        public int Seed, CampId, Tile, Rank;
        public double Viability, RenewableFoodPerYear, RenewableWaterPerYear, CenterMoisture;
        public int Low, Medium, High;
        public string CenterEnvironment, PreflightTechnology, PreflightRule;
    }
    sealed class Case
    {
        public string Id, Habitat, Scarcity;
        public Candidate Candidate;
        public double PopulationMultiplier = 1, ReserveMultiplier = 1;
        public int Support;
        public bool UseSupportPopulation, FixedSite, SettlementStress;
    }
    sealed class SupportProbe { public int Population; public bool StableAtLimit, StableAtNext; }
    sealed class Result
    {
        public string Id, Habitat, Scarcity, Error, Technology, Rule, Fingerprint;
        public int Seed, Tile, TransitionTile = -1, Support, TransitionSupport, InitialPopulation, TransitionPopulation, FinalPopulation, Turns, PostTransitionTurns, Moves, Deaths, InvariantFailures;
        public double PopulationMultiplier, ReserveMultiplier, TransitionSupportRatio, InitialFood, FinalFood, MinFoodSatisfaction, FinalQ, FinalE, FinalQoL, TotalDiscarded, MaxFoodBalanceError, MaxEcologyBalanceError, MaxBiomassBalanceError, MaxWaterBalanceError, MaxMaterialBalanceError, MaxManufacturingFoodError;
        public string TransitionHabitat, ScarcityBinding;
        public bool FixedSite, SettlementStress;
        public int PreludeTransitionPopulation, ControlledPopulationBefore, ControlledPopulationAfter;
        public long CampBoundaries, OperatingChanges, FieldSteps, FieldSwitches, TargetCompletions, ToolExhaustions, StockDepletions, TileIntegrations;
        public double ControlledFoodBefore, ControlledFoodAfter, ControlledPlantBefore, ControlledAnimalBefore, ControlledAquaticBefore;
        public string[] ScarceSourceIds = Array.Empty<string>();
    }

    static int Main(string[] args)
    {
        try { Run(args); return 0; }
        catch (Exception ex) { Console.Error.WriteLine("TRANSITION_VALIDATION_FAILURE " + ex.GetType().Name + ": " + ex.Message); return 1; }
    }
    static void Run(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string Value(string name, string fallback) { int i = Array.IndexOf(args, name); return i < 0 ? fallback : args[i + 1]; }
        string root = Path.GetFullPath(Value("--repo", "."));
        string output = Path.GetFullPath(Value("--out", "TestResults/NeolithicTransitionValidation"));
        int seedLimit = int.Parse(Value("--seed-limit", "18"));
        bool quick = args.Contains("--quick");
        Directory.CreateDirectory(output);
        var config = JsonSerializer.Deserialize<SimulationConfig>(File.ReadAllText(Path.Combine(root, "Assets/_Anthropocene/Resources/OpeningConfig.json")), Json);
        var resources = JsonSerializer.Deserialize<ResourceGenerationOptions>(File.ReadAllText(Path.Combine(root, "Assets/_Anthropocene/Resources/ResourceGenerationConfig.json")), Json);
        string landM = Value("--land-m", ""), landSafe = Value("--land-safe", ""), landDamage = Value("--land-damage", "");
        if (landM.Length > 0) config.LandBiomassMultiplier = double.Parse(landM, CultureInfo.InvariantCulture);
        if (landSafe.Length > 0) config.LandSafeUseRatio = double.Parse(landSafe, CultureInfo.InvariantCulture);
        if (landDamage.Length > 0) config.LandUseDamagePerYear = double.Parse(landDamage, CultureInfo.InvariantCulture);
        config.Width = 128; config.Height = 64; config.Players = 8; config.StartSeparation = 10; config.Validate();
        File.WriteAllText(Path.Combine(output, "configuration.json"), JsonSerializer.Serialize(config, Json));

        var worlds = new Dictionary<int, byte[]>();
        var candidates = new List<Candidate>(); var placedCandidates = new List<Candidate>();
        for (int seed = 1; seed <= seedLimit; seed++)
        {
            Console.WriteLine("GENERATE seed=" + seed);
            var world = new WorldGenerator().Generate(seed, CloneConfig(config), resources);
            worlds[seed] = SaveCodec.Save(world);
            candidates.AddRange(DescribeCandidates(world, seed));
            placedCandidates.AddRange(DescribePlacedCandidates(world, seed));
        }
        var selectionRows = new List<string> { "habitat,seed,camp,tile,rank,viability,renewableFoodPerYear,renewableWaterPerYear,centerEnvironment,centerMoisture,low,medium,high,preflightTechnology,preflightRule,selected" };
        string siteSelection = Value("--site-selection", "");
        var forcedSites = string.IsNullOrEmpty(siteSelection)
            ? new Dictionary<string, (int Seed, int Tile)>()
            : File.ReadAllLines(siteSelection).Skip(1).Select(line => line.Split(','))
                .Where(parts => parts.Length >= 16 && parts[15].Trim('"').Equals("True", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(parts => parts[0].Trim('"'), parts =>
                    (Seed: int.Parse(parts[1].Trim('"'), CultureInfo.InvariantCulture),
                        Tile: int.Parse(parts[3].Trim('"'), CultureInfo.InvariantCulture)));
        var chosen = new Dictionary<string, Candidate>();
        foreach (string habitat in Habitats)
        {
            int rank = 0;
            foreach (var candidate in candidates.Where(c => c.Habitat == habitat)
                .OrderBy(c => forcedSites.TryGetValue(habitat, out var site) && site.Seed == c.Seed && site.Tile == c.Tile ? 0 : 1)
                .ThenBy(c => habitat == "open-fertile" && c.CenterEnvironment != EnvironmentKind.Desert.ToString() ? 0 : 1)
                .ThenByDescending(c => Score(c)).ThenBy(c => c.Seed).ThenBy(c => c.Tile).Take(12))
            {
                candidate.Rank = ++rank;
                var preflight = RunPreflight(worlds[candidate.Seed], candidate);
                candidate.PreflightTechnology = preflight.Technology.ToString(); candidate.PreflightRule = preflight.People.Progression.CompletionRule ?? "none";
                if (!chosen.ContainsKey(habitat) && preflight.Technology == Technology.Cultivation
                    && (!forcedSites.TryGetValue(habitat, out var site) || site.Seed == candidate.Seed && site.Tile == candidate.Tile))
                    chosen.Add(habitat, candidate);
                selectionRows.Add(Csv(candidate.Habitat, candidate.Seed, candidate.CampId, candidate.Tile, candidate.Rank, candidate.Viability, candidate.RenewableFoodPerYear, candidate.RenewableWaterPerYear, candidate.CenterEnvironment, candidate.CenterMoisture, candidate.Low, candidate.Medium, candidate.High, candidate.PreflightTechnology, candidate.PreflightRule, chosen.TryGetValue(habitat, out var s) && ReferenceEquals(s, candidate)));
                File.WriteAllLines(Path.Combine(output, "selection.csv"), selectionRows);
                // Selection is reported, including every rejected natural outcome;
                // the chosen row is never a quiet technology filter.
            }
            if (!chosen.ContainsKey(habitat)) throw new InvalidOperationException("No live Cultivation transition found for " + habitat + " in the reported preflight cohort.");
        }
        Candidate placedControl = null;
        foreach (var candidate in placedCandidates.OrderBy(c => c.Seed).ThenBy(c => c.CampId))
        {
            var preflight = RunPreflight(worlds[candidate.Seed], candidate);
            candidate.PreflightTechnology = preflight.Technology.ToString(); candidate.PreflightRule = preflight.People.Progression.CompletionRule ?? "none";
            if (placedControl == null && preflight.Technology == Technology.Cultivation) placedControl = candidate;
            selectionRows.Add(Csv(candidate.Habitat, candidate.Seed, candidate.CampId, candidate.Tile, 0, candidate.Viability, candidate.RenewableFoodPerYear, candidate.RenewableWaterPerYear, candidate.CenterEnvironment, candidate.CenterMoisture, candidate.Low, candidate.Medium, candidate.High, candidate.PreflightTechnology, candidate.PreflightRule, ReferenceEquals(placedControl, candidate)));
            File.WriteAllLines(Path.Combine(output, "selection.csv"), selectionRows);
        }
        if (placedControl == null) throw new InvalidOperationException("No original placed camp reached Cultivation in the reported control cohort.");
        File.WriteAllLines(Path.Combine(output, "selection.csv"), selectionRows);

        var supports = chosen.Values.ToDictionary(c => c.Habitat, c => PaleoSupport(worlds[c.Seed], c));
        File.WriteAllLines(Path.Combine(output, "paleo-support.csv"), new[] { "habitat,population,stableAtLimit,stableAtNext" }.Concat(supports.OrderBy(x => x.Key).Select(x => Csv(x.Key, x.Value.Population, x.Value.StableAtLimit, x.Value.StableAtNext))));
        var cases = new List<Case>();
        cases.Add(new Case { Id = "placed-camp-control", Habitat = "placed-camp-control", Candidate = placedControl });
        foreach (string habitat in Habitats) cases.Add(new Case { Id = habitat + "-normal", Habitat = habitat, Candidate = chosen[habitat], Support = supports[habitat].Population });
        var river = chosen["productive-river"];
        foreach (string material in new[] { "Clay", "Stone", "Wood" }) cases.Add(new Case { Id = "scarce-" + material.ToLowerInvariant(), Habitat = "productive-river", Candidate = river, Support = supports[river.Habitat].Population, Scarcity = material });
        var open = chosen["open-fertile"];
        foreach (double multiplier in new[] { 1.2, 1.4, 1.5 }) cases.Add(new Case { Id = "mobile-" + multiplier.ToString("0.0", CultureInfo.InvariantCulture), Habitat = "open-fertile", Candidate = open, Support = supports[open.Habitat].Population, PopulationMultiplier = multiplier, UseSupportPopulation = true });
        foreach (double reserve in new[] { .5, 2d }) cases.Add(new Case { Id = "reserve-" + reserve.ToString("0.0", CultureInfo.InvariantCulture), Habitat = "productive-river", Candidate = river, Support = supports[river.Habitat].Population, ReserveMultiplier = reserve });
        foreach (string habitat in new[] { "open-fertile", "medium-woodland", "dry-marginal" }) cases.Add(new Case { Id = "fixed-" + habitat, Habitat = habitat, Candidate = chosen[habitat], Support = supports[habitat].Population, FixedSite = true });
        foreach (double multiplier in new[] { 1.2, 1.4, 1.5 }) cases.Add(new Case { Id = "fixed-settlement-" + multiplier.ToString("0.0", CultureInfo.InvariantCulture), Habitat = "open-fertile", Candidate = open, Support = supports[open.Habitat].Population, PopulationMultiplier = multiplier, FixedSite = true, SettlementStress = true });

        bool fixedPilot = args.Contains("--fixed-pilot");
        if (fixedPilot) cases = cases.Where(c => c.FixedSite).ToList();
        string requestedCases = Value("--case", "");
        if (!string.IsNullOrWhiteSpace(requestedCases))
        {
            var wanted = requestedCases.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToHashSet(StringComparer.Ordinal);
            var allCases = cases;
            cases = cases.Where(c => wanted.Contains(c.Id)).ToList();
            if (cases.Count != wanted.Count) throw new ArgumentException("Unknown or unavailable --case selection: " + requestedCases);
            // A full focused rerun still proves the required deterministic replay
            // against its documented common river baseline.
            if (!quick && !fixedPilot && !cases.Any(c => c.Id == "productive-river-normal")) cases.Add(allCases.Single(c => c.Id == "productive-river-normal"));
        }
        var results = new List<Result>();
        ResolutionMetrics.CaptureDetailedCounters = args.Contains("--activity");
        using var trace = new StreamWriter(Path.Combine(output, "trajectory.jsonl"));
        foreach (var item in cases)
        {
            Console.WriteLine("RUN " + item.Id + " seed=" + item.Candidate.Seed + " tile=" + item.Candidate.Tile);
            results.Add(Run(worlds[item.Candidate.Seed], item, trace, quick ? 13 : 37, false, new PhaseExecution()));
            // Preserve already-completed measurements if a subsequent case or replay fails.
            trace.Flush();
            File.WriteAllLines(Path.Combine(output, "matrix.csv"), MatrixCsv(results));
            if (ResolutionMetrics.CaptureDetailedCounters)
                File.WriteAllLines(Path.Combine(output, "activity.csv"),
                    new[] { "case,campBoundaries,operatingChanges,fieldSteps,fieldSwitches,targetCompletions,toolExhaustions,stockDepletions,tileIntegrations" }
                    .Concat(results.Select(r => Csv(r.Id, r.CampBoundaries, r.OperatingChanges, r.FieldSteps, r.FieldSwitches,
                        r.TargetCompletions, r.ToolExhaustions, r.StockDepletions, r.TileIntegrations))));
            File.WriteAllText(Path.Combine(output, "summary.md"), Summary(results, chosen, supports.ToDictionary(x => x.Key, x => x.Value.Population), null, null, null, false));
        }
        Result scalar = null, parallel = null, saved = null;
        bool replayVerified = !quick && !fixedPilot;
        if (replayVerified)
        {
            var replayCase = cases.Single(c => c.Id == "productive-river-normal");
            scalar = Run(worlds[replayCase.Candidate.Seed], replayCase, TextWriter.Null, 37, false, new PhaseExecution());
            parallel = Run(worlds[replayCase.Candidate.Seed], replayCase, TextWriter.Null, 37, false, new PhaseExecution(2, 1, true, 1));
            saved = Run(worlds[replayCase.Candidate.Seed], replayCase, TextWriter.Null, 37, true, new PhaseExecution());
        }
        bool Healthy(Result r) => r.Error == null && r.InvariantFailures == 0 && r.MaxFoodBalanceError <= 1e-5 && r.MaxEcologyBalanceError <= 1e-5 && r.MaxBiomassBalanceError <= 1e-5 && r.MaxWaterBalanceError <= 1e-5 && r.MaxMaterialBalanceError <= 1e-5 && r.MaxManufacturingFoodError <= 1e-5 && r.PostTransitionTurns == 25;
        if (replayVerified && (!Healthy(scalar) || !Healthy(parallel) || !Healthy(saved) || scalar.Fingerprint != parallel.Fingerprint || scalar.Fingerprint != saved.Fingerprint)) throw new InvalidOperationException("Transition replay/save continuation did not complete a healthy 25-post-Cultivation continuation.");
        File.WriteAllLines(Path.Combine(output, "matrix.csv"), MatrixCsv(results));
        File.WriteAllText(Path.Combine(output, "summary.md"), Summary(results, chosen, supports.ToDictionary(x => x.Key, x => x.Value.Population), scalar, parallel, saved, replayVerified));
        if (results.Any(r => r.Error != null || r.InvariantFailures != 0 || r.MaxFoodBalanceError > 1e-5 || r.MaxEcologyBalanceError > 1e-5 || r.MaxBiomassBalanceError > 1e-5 || r.MaxWaterBalanceError > 1e-5 || r.MaxMaterialBalanceError > 1e-5 || r.MaxManufacturingFoodError > 1e-5)) throw new InvalidOperationException("Matrix contains an invariant or conservation failure; see matrix.csv and trajectory.jsonl.");
        Console.WriteLine("PASS cases=" + results.Count + " output=" + output);
    }

    static readonly string[] Habitats = { "productive-river", "open-fertile", "medium-woodland", "high-forest", "dry-marginal" };
    static SimulationConfig CloneConfig(SimulationConfig value) => JsonSerializer.Deserialize<SimulationConfig>(JsonSerializer.Serialize(value, Json), Json);
    static IEnumerable<Candidate> DescribeCandidates(WorldState world, int seed)
    {
        int templateCamp = world.Camps.OrderBy(c => c.Id).First().Id;
        foreach (var center in world.Tiles.Where(t => t.TraversableLand).OrderBy(t => t.Id))
        {
            var ids = world.Map.Radius(center.Id, 1).OrderBy(x => x).ToArray();
            if (ids.Length != 7 || ids.Any(id => !world.Tiles[id].TraversableLand)) continue;
            if (PlacementStage.Viability(world, center.Id) < world.Config.StartViability) continue;
            var tiles = ids.Select(id => world.Tiles[id]).ToArray();
            int low = tiles.Count(t => t.CurrentVegetation == VegetationTier.Low), medium = tiles.Count(t => t.CurrentVegetation == VegetationTier.Medium), high = tiles.Count(t => t.CurrentVegetation == VegetationTier.High);
            double food = tiles.Sum(t => t.Seasons.Average(s => s.MaxFood * s.FoodRecoveryPerDay * world.Config.DaysPerYear));
            double water = tiles.Sum(t => t.Seasons.Average(s => s.MaxFreshwater * s.WaterRecoveryPerDay * world.Config.DaysPerYear));
            foreach (string habitat in Habitats.Where(h => Matches(h, tiles, center, low, medium, high)))
                yield return new Candidate { Habitat = habitat, Seed = seed, CampId = templateCamp, Tile = center.Id, Viability = PlacementStage.Viability(world, center.Id), RenewableFoodPerYear = food, RenewableWaterPerYear = water, CenterEnvironment = center.Environment.ToString(), CenterMoisture = center.Seasons.Average(s => s.Moisture), Low = low, Medium = medium, High = high };
        }
    }
    static IEnumerable<Candidate> DescribePlacedCandidates(WorldState world, int seed)
    {
        foreach (var camp in world.Camps.OrderBy(c => c.Id))
        {
            var ids = world.Map.Radius(camp.Tile, 1).OrderBy(x => x).ToArray();
            if (ids.Length != 7 || ids.Any(id => !world.Tiles[id].TraversableLand)) continue;
            var tiles = ids.Select(id => world.Tiles[id]).ToArray();
            yield return new Candidate { Habitat = "placed-camp-control", Seed = seed, CampId = camp.Id, Tile = camp.Tile, Viability = PlacementStage.Viability(world, camp.Tile), RenewableFoodPerYear = tiles.Sum(t => t.Seasons.Average(s => s.MaxFood * s.FoodRecoveryPerDay * world.Config.DaysPerYear)), RenewableWaterPerYear = tiles.Sum(t => t.Seasons.Average(s => s.MaxFreshwater * s.WaterRecoveryPerDay * world.Config.DaysPerYear)), CenterEnvironment = world.Tiles[camp.Tile].Environment.ToString(), CenterMoisture = world.Tiles[camp.Tile].Seasons.Average(s => s.Moisture), Low = tiles.Count(t => t.CurrentVegetation == VegetationTier.Low), Medium = tiles.Count(t => t.CurrentVegetation == VegetationTier.Medium), High = tiles.Count(t => t.CurrentVegetation == VegetationTier.High) };
        }
    }
    static bool Matches(string habitat, Tile[] tiles, Tile center, int low, int medium, int high)
    {
        double moisture = tiles.Average(t => t.Seasons.Average(s => s.Moisture));
        if (habitat == "productive-river") return tiles.Any(t => t.River || t.Oasis);
        if (habitat == "open-fertile") return high <= 1 && medium <= 2 && moisture >= .20 && !tiles.Any(t => t.River || t.Oasis);
        if (habitat == "medium-woodland") return medium >= 3 && high <= 3 && !tiles.Any(t => t.River || t.Oasis);
        if (habitat == "high-forest") return high >= 3 && !tiles.Any(t => t.River || t.Oasis);
        return (moisture < .20 || center.Environment == EnvironmentKind.Desert) && !tiles.Any(t => t.River || t.Oasis);
    }
    static double Score(Candidate c) => c.Habitat == "dry-marginal" ? -c.Viability : c.RenewableFoodPerYear + c.RenewableWaterPerYear * .05;

    static Camp RunPreflight(byte[] worldBytes, Candidate candidate)
    {
        var w = Isolate(worldBytes, candidate); var c = w.Camps.Single(); var policy = new HeuristicTribePolicy();
        for (int turn = 0; turn < 12 && w.LinkedPopulation(c) > 0 && c.Technology == Technology.None; turn++) ResolvePolicy(w, c, policy, new PhaseExecution());
        return c;
    }
    static SupportProbe PaleoSupport(byte[] worldBytes, Candidate candidate)
    {
        bool Stable(int population)
        {
            var w = Isolate(worldBytes, candidate); var c = w.Camps.Single();
            // This estimator only freezes demography/progression. It keeps the
            // production, ecology, q=0 labor and food/water equations intact.
            w.Config.AnnualHumanGrowthRate = 0; w.Config.MajorityEligibilityYears = 1000000000; w.Config.ForcedTransitionYears = 1000000001;
            w.Config.ExperienceThreshold = 1e300;
            c.People.Progression.Era = Era.Paleolithic; c.People.Progression.EraStartDays = w.Days; c.People.Progression.MajorityEligibilityDays = 1e12; c.People.Progression.ForcedTransitionDays = 1e12 + 1;
            c.People.Technology = Technology.None; c.People.Technologies = new bool[4];
            foreach (int id in w.Map.Radius(c.Tile, 1)) { w.Tiles[id].CultivationCapital = 0; w.Tiles[id].Cultivation = 0; w.Tiles[id].CultivationOperator = -1; w.Tiles[id].Cultivated = false; }
            c.Population = population; c.Workforce.Normalize(population); c.Food = population * w.Config.Consumption * w.Config.GenerationIntervals;
            double initial = c.Food;
            for (int turn = 0; turn < 8; turn++)
            {
                var r = new TurnResolver().Resolve(w, new Dictionary<int, List<Order>> { [c.Player] = new List<Order> { new Order(OrderKind.NoOp, c.Id) } });
                var d = r.Demographics.Single();
                if (d.Deaths > 0 || (turn >= 5 && c.FoodNeedsMetRatio < .999999)) return false;
                if (c.People.Progression.Era != Era.Paleolithic || CultivationEconomy.Enabled(c)) throw new InvalidOperationException("Paleo support estimator left q=0 Paleolithic state.");
            }
            return c.Food >= initial - 1e-6;
        }
        if (!Stable(1)) return new SupportProbe { Population = 0, StableAtLimit = false, StableAtNext = false };
        int low = 1, high = 2;
        while (Stable(high)) { low = high; high *= 2; if (high > 1048576) throw new InvalidOperationException("Paleo support probe did not bracket a local capacity."); }
        while (low < high)
        {
            int p = (low + high + 1) / 2;
            if (Stable(p)) low = p; else high = p - 1;
        }
        return new SupportProbe { Population = low, StableAtLimit = Stable(low), StableAtNext = Stable(low + 1) };
    }
    static WorldState Isolate(byte[] worldBytes, Candidate candidate)
    {
        var w = SaveCodec.Load(worldBytes); var c = w.Camps.Single(x => x.Id == candidate.CampId);
        w.Camps = new List<Camp> { c }; w.Hunters.Clear(); w.Societies = w.Societies.Where(s => s.Id == c.SocietyId).ToList(); c.Tile = candidate.Tile; c.Moves = 0; c.StationaryTurns = 0; c.StationaryYears = 0;
        Ownership.Bind(w); return w;
    }
    static void ResolvePolicy(WorldState world, Camp camp, HeuristicTribePolicy policy, PhaseExecution execution)
    {
        var planned = AiPlanning.Decide(world, execution);
        new TurnResolver(execution).Resolve(world, planned.Orders, planned.Traces);
    }
    static Result Run(byte[] worldBytes, Case item, TextWriter trace, int maxTurns, bool saveMidway, PhaseExecution execution)
    {
        var w = Isolate(worldBytes, item.Candidate); var c = w.Camps.Single(); var policy = new HeuristicTribePolicy();
        if (item.UseSupportPopulation) { c.Population = Math.Max(1, (int)Math.Round(item.Support * item.PopulationMultiplier)); c.Workforce.Normalize(c.Population); }
        c.Food = c.Population * w.Config.Consumption * w.Config.GenerationIntervals * item.ReserveMultiplier;
        var result = new Result { Id = item.Id, Habitat = item.Habitat, Scarcity = item.Scarcity ?? "none", Seed = item.Candidate.Seed, Tile = item.Candidate.Tile, Support = item.Support, InitialPopulation = c.Population, PopulationMultiplier = item.PopulationMultiplier, ReserveMultiplier = item.ReserveMultiplier, InitialFood = c.Food, MinFoodSatisfaction = 1, FixedSite = item.FixedSite, SettlementStress = item.SettlementStress };
        bool scarcityApplied = false, settlementInitialized = false; int transitionTurn = -1, resolutions = 0;
        try
        {
            for (int turn = 0; turn < maxTurns && w.LinkedPopulation(c) > 0; turn++)
            {
                if (!settlementInitialized && item.SettlementStress && transitionTurn >= 0)
                {
                    ApplySettlementStress(w, c, result, item.PopulationMultiplier);
                    settlementInitialized = true;
                    trace.WriteLine(JsonSerializer.Serialize(new { id = item.Id, @event = "controlledSettlementInitialization", year = w.Days / w.Config.DaysPerYear, preludeTransitionPopulation = result.PreludeTransitionPopulation, targetPopulation = result.ControlledPopulationAfter, actualRatio = result.TransitionSupportRatio, foodBefore = result.ControlledFoodBefore, foodAfter = result.ControlledFoodAfter, foodProvenanceBefore = new { plant = result.ControlledPlantBefore, animal = result.ControlledAnimalBefore, aquatic = result.ControlledAquaticBefore }, foodProvenanceAfter = new { plant = c.PlantFood, animal = c.AnimalFood, aquatic = c.AquaticFood }, note = "Controlled settlement initialization; excluded from demographic flows. Subsequent generations use normal food conservation." }, Json));
                }
                if (!scarcityApplied && c.Technology == Technology.Cultivation && item.Scarcity != null) { result.ScarceSourceIds = ApplyScarcity(w, c, item.Scarcity); scarcityApplied = true; }
                var before = new CampInventory { Stone = c.Inventory.Stone, Clay = c.Inventory.Clay, Wood = c.Inventory.Wood };
                var openingRadius = w.Map.Radius(c.Tile, 1).Where(id => w.Tiles[id].Land).ToArray();
                double openingRadiusFood = openingRadius.Sum(id => LazyEcology.ProjectEcology(w, id, w.Days).Food);
                double openingRadiusBiomass = openingRadius.Sum(id => LazyEcology.ProjectEcology(w, id, w.Days).PotentialFood);
                var startingAssignments = c.Workforce.Assignments.ToDictionary(x => x.Role, x => x.Population);
                var decision = item.FixedSite ? FixedSiteDecision(w, c) : PlannedDecision(w, c, execution);
                var submittedAssignments = decision.Orders.Where(x => x.Kind == OrderKind.Economy && x.Economy != null).SelectMany(x => x.Economy.Assignments).GroupBy(x => x.Role).ToDictionary(x => x.Key, x => x.Sum(a => a.Population));
                var resolver = new TurnResolver(execution);
                var record = resolver.Resolve(w, new Dictionary<int, List<Order>> { [c.Player] = decision.Orders }, new[] { decision.Trace });
                result.CampBoundaries += resolver.LastMetrics.SeasonalCampBoundaries;
                result.OperatingChanges += resolver.LastMetrics.OperatingRegimeChanges;
                result.FieldSteps += resolver.LastMetrics.FieldWorkSteps;
                result.FieldSwitches += resolver.LastMetrics.FieldChoiceSwitches;
                result.TargetCompletions += resolver.LastMetrics.TargetCompletions;
                result.ToolExhaustions += resolver.LastMetrics.ToolExhaustions;
                result.StockDepletions += resolver.LastMetrics.StockDepletions;
                result.TileIntegrations += resolver.LastMetrics.TileIntegrations;
                resolutions++;
                if (transitionTurn < 0 && c.Technology == Technology.Cultivation)
                {
                    transitionTurn = resolutions; result.TransitionTile = c.Tile; result.TransitionHabitat = HabitatAt(w, c.Tile); result.TransitionPopulation = w.LinkedPopulation(c); result.PreludeTransitionPopulation = result.TransitionPopulation;
                    var local = new Candidate { CampId = c.Id, Tile = c.Tile }; result.TransitionSupport = PaleoSupport(SaveCodec.Save(w), local).Population;
                    result.TransitionSupportRatio = result.TransitionPopulation / Math.Max(1.0, result.TransitionSupport);
                }
                Capture(trace, item, w, c, record, before, startingAssignments, submittedAssignments,
                    openingRadius, openingRadiusFood, openingRadiusBiomass,
                    turn + 1, transitionTurn, scarcityApplied, result);
                if (saveMidway && turn == maxTurns / 2) { w = SaveCodec.Load(SaveCodec.Save(w)); c = w.Camps.Single(); }
                // Agriculture cases get a comparable full 25 generations after the
                // actual live Cultivation transition. Other Neolithic outcomes are
                // retained through the 37-resolution discovery horizon as censored rows.
                if (transitionTurn >= 0 && resolutions - transitionTurn >= 25) break;
            }
        }
        catch (Exception ex) { result.Error = ex.GetType().Name + ": " + ex.Message; }
        result.FinalPopulation = w.LinkedPopulation(c); result.FinalFood = c.Food; result.Turns = resolutions; result.PostTransitionTurns = transitionTurn < 0 ? 0 : Math.Max(0, resolutions - transitionTurn); result.Moves = c.Moves; result.Technology = c.Technology.ToString(); result.Rule = c.People.Progression.CompletionRule ?? "none"; result.FinalQoL = c.QoL; result.Fingerprint = SaveCodec.Fingerprint(w);
        var fields = CultivationEconomy.Footprint(w, c); result.FinalE = fields.Length == 0 ? 0 : fields.Average(id => w.Tiles[id].CultivationCapital); result.FinalQ = fields.Length == 0 ? 0 : fields.Average(id => CultivationEconomy.Intensity(w.Config, w.Tiles[id].CultivationCapital));
        return result;
    }
    static PolicyDecision FixedSiteDecision(WorldState world, Camp camp)
    {
        var decision = new PolicyDecision { Trace = new DecisionTrace { Player = camp.Player, Turn = world.Turn } };
        if (camp.Technology == Technology.Cultivation)
        {
            var intent = EconomicIntents.Automatic(world, camp, true); intent.AutomationTurns = -1;
            decision.Orders.Add(new Order(OrderKind.Economy, camp.Id) { Economy = intent });
        }
        else decision.Orders.Add(new Order(OrderKind.NoOp, camp.Id));
        return decision;
    }
    static PolicyDecision PlannedDecision(WorldState world, Camp camp, PhaseExecution execution)
    {
        var planned = AiPlanning.Decide(world, execution);
        return new PolicyDecision { Orders = planned.Orders[camp.Player], Trace = planned.Traces.Single(t => t.Player == camp.Player) };
    }
    static void ApplySettlementStress(WorldState world, Camp camp, Result result, double multiplier)
    {
        result.ControlledPopulationBefore = world.LinkedPopulation(camp); result.ControlledFoodBefore = camp.Food; result.ControlledPlantBefore = camp.PlantFood; result.ControlledAnimalBefore = camp.AnimalFood; result.ControlledAquaticBefore = camp.AquaticFood;
        int target = Math.Max(1, (int)Math.Round(result.TransitionSupport * multiplier));
        camp.Population = target; camp.Workforce.Normalize(target); camp.PlantFood = target * world.Config.Consumption * world.Config.GenerationIntervals; camp.AnimalFood = 0; camp.AquaticFood = 0;
        result.ControlledPopulationAfter = target; result.ControlledFoodAfter = camp.Food; result.TransitionPopulation = target; result.TransitionSupportRatio = target / Math.Max(1.0, result.TransitionSupport);
    }
    static string[] ApplyScarcity(WorldState w, Camp c, string material)
    {
        var id = (MaterialId)Enum.Parse(typeof(MaterialId), material); var sources = MaterialEconomy.KnownSources(w, c, w.Days).Where(s => s.Material == id).OrderBy(s => s.Id).ToArray();
        if (id == MaterialId.Wood)
        {
            // Wood recovery makes a zeroed sparse ledger non-binding by the next
            // twenty-year resolution. This explicit controlled fixture removes
            // every local surface/body Wood source at the transition footprint.
            foreach (int tile in w.Map.Radius(c.Tile, 1)) w.Resources.Wood[tile] = 0;
            foreach (var body in w.Resources.Bodies.Where(b => b.Sites.Any(s => w.Map.Distance(c.Tile, s.Tile) <= 1)))
                for (int i = 0; i < body.Components.Length; i++) if (body.Components[i].Material == MaterialId.Wood) { var component = body.Components[i]; component.AccessibleQuantity = 0; body.Components[i] = component; }
        }
        foreach (var source in sources)
        {
            var state = w.MaterialSources.FirstOrDefault(s => s.Id == source.Id);
            if (state == null) w.MaterialSources.Add(new MaterialSourceState { Id = source.Id, Material = id, Initial = source.Initial, Remaining = 0, LastDays = w.Days });
            else { state.Remaining = 0; state.LastDays = w.Days; }
        }
        return sources.Select(s => s.Id).ToArray();
    }
    static string HabitatAt(WorldState world, int tile)
    {
        var ids = world.Map.Radius(tile, 1).OrderBy(x => x).ToArray();
        if (ids.Length != 7 || ids.Any(id => !world.Tiles[id].TraversableLand)) return "nonlocal";
        var tiles = ids.Select(id => world.Tiles[id]).ToArray(); int low = tiles.Count(t => t.CurrentVegetation == VegetationTier.Low), medium = tiles.Count(t => t.CurrentVegetation == VegetationTier.Medium), high = tiles.Count(t => t.CurrentVegetation == VegetationTier.High);
        var labels = Habitats.Where(h => Matches(h, tiles, world.Tiles[tile], low, medium, high)).ToArray();
        return labels.Length == 0 ? "mixed" : string.Join("|", labels);
    }
    static void Capture(TextWriter trace, Case item, WorldState w, Camp c, TurnRecord r, CampInventory before, Dictionary<string, int> startingAssignments, Dictionary<string, int> submittedAssignments, int[] openingRadius, double openingRadiusFood, double openingRadiusBiomass, int turn, int transitionTurn, bool scarcityApplied, Result result)
    {
        var d = r.Demographics.Single(x => x.Camp == c.Id); double foodBalance = d.InitialReserves + d.FoodReceived - d.FoodConsumed - d.ManufacturingFood - d.DiscardedInventory - d.FinalReserves;
        double FoodBalance(EcologyBalance b) => b.StartingFood + b.FoodRecharge - b.FoodHarvest - b.FoodOverflow - b.EndingFood;
        double BiomassBalance(EcologyBalance b) => b.StartingBiomass + b.BiomassRecharge - b.BiomassHarvest - b.BiomassOverflow - b.EndingBiomass;
        double WaterBalance(EcologyBalance b) => b.StartingWater + b.WaterRecharge - b.WaterHarvest - b.WaterOverflow - b.EndingWater;
        double ecology = r.EcologyBalances.Select(FoodBalance).DefaultIfEmpty(0).Select(Math.Abs).DefaultIfEmpty(0).Max();
        double biomass = r.EcologyBalances.Select(BiomassBalance).DefaultIfEmpty(0).Select(Math.Abs).DefaultIfEmpty(0).Max();
        double water = r.EcologyBalances.Select(WaterBalance).DefaultIfEmpty(0).Select(Math.Abs).DefaultIfEmpty(0).Max();
        double Debit(MaterialId material) => r.Production.Sum(p => material == MaterialId.Stone ? p.Stone : material == MaterialId.Clay ? p.Clay : p.Wood);
        double Receipt(MaterialId material) => r.MaterialTransfers.Where(x => x.Material == material).Sum(x => x.Amount);
        double After(MaterialId material) => material == MaterialId.Stone ? c.Inventory.Stone : material == MaterialId.Clay ? c.Inventory.Clay : c.Inventory.Wood;
        double Before(MaterialId material) => material == MaterialId.Stone ? before.Stone : material == MaterialId.Clay ? before.Clay : before.Wood;
        double material = new[] { MaterialId.Stone, MaterialId.Clay, MaterialId.Wood }.Select(x => Math.Abs(Before(x) + Receipt(x) - Debit(x) - After(x))).Max();
        double receipt = r.MaterialTransfers.Select(x => Math.Abs(x.Before - x.After - x.Amount)).DefaultIfEmpty(0).Max(); material = Math.Max(material, receipt);
        double manufacturedFood = Math.Abs(d.ManufacturingFood - r.Production.Sum(x => x.PlantFood));
        result.MaxFoodBalanceError = Math.Max(result.MaxFoodBalanceError, Math.Abs(foodBalance)); result.MaxEcologyBalanceError = Math.Max(result.MaxEcologyBalanceError, ecology); result.MaxBiomassBalanceError = Math.Max(result.MaxBiomassBalanceError, biomass); result.MaxWaterBalanceError = Math.Max(result.MaxWaterBalanceError, water); result.MaxMaterialBalanceError = Math.Max(result.MaxMaterialBalanceError, material); result.MaxManufacturingFoodError = Math.Max(result.MaxManufacturingFoodError, manufacturedFood); result.TotalDiscarded += d.DiscardedInventory; result.Deaths += d.Deaths; result.MinFoodSatisfaction = Math.Min(result.MinFoodSatisfaction, c.FoodNeedsMetRatio);
        if (Math.Abs(foodBalance) > 1e-5 || ecology > 1e-5 || biomass > 1e-5 || water > 1e-5 || material > 1e-5 || manufacturedFood > 1e-5 || c.Workforce.Assigned > c.Population) result.InvariantFailures++;
        var fields = CultivationEconomy.Footprint(w, c); double q = fields.Length == 0 ? 0 : fields.Average(id => CultivationEconomy.Intensity(w.Config, w.Tiles[id].CultivationCapital)); double e = fields.Length == 0 ? 0 : fields.Average(id => w.Tiles[id].CultivationCapital);
        int farmers = CultivationEconomy.Workers(c, WorkforceRoles.Farmer); int specialists = WorkforceRoles.Specialists.Sum(role => CultivationEconomy.Workers(c, role)); double materialGeneralists = c.EconomicLabor?.MaterialGatherers ?? 0; int materialSpecialists = WorkforceRoles.Extractors.Sum(role => CultivationEconomy.Workers(c, role)); double foodGatherers = Math.Max(0, c.GathererPopulation - materialGeneralists);
        double scarcityAvailable = item.Scarcity == null ? 0 : MaterialEconomy.KnownSources(w, c, w.Days).Where(x => x.Material.ToString() == item.Scarcity).Sum(x => x.Available);
        double scarceDebit = item.Scarcity == "Wood" ? r.Production.Sum(x => x.Wood) : item.Scarcity == "Clay" ? r.Production.Sum(x => x.Clay) : item.Scarcity == "Stone" ? r.Production.Sum(x => x.Stone) : 0;
        if (scarcityApplied && transitionTurn >= 0 && turn > transitionTurn && result.ScarcityBinding == null) result.ScarcityBinding = "next-turn-available=" + scarcityAvailable.ToString("R", CultureInfo.InvariantCulture) + "; selected-material-debit=" + scarceDebit.ToString("R", CultureInfo.InvariantCulture);
        // Fields are land-only. For aquatic condition, use the adjacent Shallows
        // and Lakes that can support the camp's local aquatic claims.
        var nearbyAquatic = w.Map.Radius(c.Tile, 1).Where(id => w.Tiles[id].Aquatic).ToArray();
        double? footprintMeanLandCondition = fields.Length == 0 ? (double?)null : fields.Average(id => w.Tiles[id].LandCondition);
        double? footprintMeanAquaticCondition = nearbyAquatic.Length == 0 ? (double?)null : nearbyAquatic.Average(id => w.Tiles[id].AquaticCondition);
        var endingTools = new { c.Inventory.HandTools, c.Inventory.Tools, c.Inventory.FarmTools, c.Inventory.HerdingTools };
        var footprint = fields.Select(id => new { id, environment = w.Tiles[id].Environment.ToString(), vegetation = w.Tiles[id].CurrentVegetation.ToString(), moisture = w.Tiles[id].Seasons.Average(s => s.Moisture), river = w.Tiles[id].River, q = CultivationEconomy.Intensity(w.Config, w.Tiles[id].CultivationCapital), e = w.Tiles[id].CultivationCapital, landCondition = w.Tiles[id].LandCondition });
        trace.WriteLine(JsonSerializer.Serialize(new { id = item.Id, turn, openingRadiusTiles = openingRadius, openingRadiusFood, openingRadiusBiomass, endingRadiusFood = openingRadius.Sum(id => w.Tiles[id].Food), endingRadiusBiomass = openingRadius.Sum(id => w.Tiles[id].PotentialFood), year = w.Days / w.Config.DaysPerYear, fixedSite = item.FixedSite, settlementStress = item.SettlementStress, postTransitionTurns = transitionTurn < 0 ? 0 : turn - transitionTurn, technology = c.Technology.ToString(), transitionRule = c.People.Progression.CompletionRule, experience = c.Experience, population = w.LinkedPopulation(c), residentPopulation = c.Population, linkedHunters = w.Hunters.Where(h => h.SourceCamp == c.Id).Sum(h => h.Population), q, e, footprint, footprintMeanLandCondition, footprintMeanAquaticCondition, endingTools, foodSatisfaction = c.FoodNeedsMetRatio, waterSatisfaction = c.WaterNeedsMetRatio, campReserves = c.Food, linkedReserves = d.FinalReserves, reserveTurns = d.FinalReserves / Math.Max(1e-9, w.LinkedPopulation(c) * w.Config.Consumption * w.Config.GenerationIntervals), demographic = new { d.StartingPopulation, d.InitialReserves, d.FoodProduced, d.FoodReceived, d.FoodConsumed, d.ManufacturingFood, d.DiscardedInventory, d.FinalReserves, d.Additions, d.Deaths, d.EndingPopulation }, farmers, foodGatherers, materialGeneralists, materialSpecialists, materialAssigned = materialGeneralists + materialSpecialists, specialists, startingAssignments, submittedAssignments, endingAssignments = c.Workforce.Assignments.ToDictionary(x => x.Role, x => x.Population), orders = r.Orders, decisions = r.Decisions.Select(x => new { x.Player, x.Turn, candidates = x.Candidates.Select(y => new { y.Plan, y.Components, y.Score, y.Selected }) }), materialWorkerIntervals = r.MaterialTransfers.Sum(x => x.WorkerIntervals), materials = new { before = new { before.Stone, before.Clay, before.Wood }, after = new { c.Inventory.Stone, c.Inventory.Clay, c.Inventory.Wood }, receipts = r.MaterialTransfers.Select(x => new { material = x.Material.ToString(), x.Role, x.Amount, x.Before, x.After, x.WorkerIntervals }) }, production = r.Production.Select(x => new { good = x.Good.ToString(), x.Role, x.Output, x.WorkerIntervals, x.Stone, x.Clay, x.Wood, x.PlantFood }), qols = new { qol = c.QoL, cloth = c.ClothPerCapita, housing = c.HousingPerCapita }, moves = c.Moves, deaths = d.Deaths, discarded = d.DiscardedInventory, foodBalance, ecologyFoodBalanceMax = ecology, ecologyBiomassBalanceMax = biomass, ecologyWaterBalanceMax = water, ecology = r.EcologyBalances.Select(x => new { x.Tile, x.StartingBiomass, x.BiomassRecharge, x.BiomassHarvest, x.BiomassOverflow, x.EndingBiomass, x.StartingFood, x.FoodRecharge, x.FoodHarvest, x.FoodOverflow, x.EndingFood, x.StartingWater, x.WaterRecharge, x.WaterHarvest, x.WaterOverflow, x.EndingWater }), materialBalanceMax = material, manufacturingFoodBalance = manufacturedFood, limiting = d.LimitingFactor, scarcity = item.Scarcity ?? "none", scarcitySources = result.ScarceSourceIds, scarcityAvailable, scarceDebit, unlocked = c.People.NeolithicTechnologies }, Json));
    }
    static IEnumerable<string> MatrixCsv(IEnumerable<Result> values)
    {
        yield return "id,habitat,scarcity,fixedSite,settlementStress,seed,tile,transitionTile,transitionHabitat,support,transitionSupport,transitionPopulation,transitionSupportRatio,controlledPopulationBefore,controlledPopulationAfter,controlledFoodBefore,controlledFoodAfter,initialPopulation,finalPopulation,populationMultiplier,reserveMultiplier,initialFood,finalFood,technology,rule,turns,postTransitionTurns,moves,deaths,minFoodSatisfaction,finalQ,finalE,finalQoL,totalDiscarded,maxFoodBalanceError,maxEcologyFoodBalanceError,maxEcologyBiomassBalanceError,maxEcologyWaterBalanceError,maxMaterialBalanceError,maxManufacturingFoodError,invariantFailures,scarceSourceCount,scarcityBinding,error";
        foreach (var r in values) yield return Csv(r.Id, r.Habitat, r.Scarcity, r.FixedSite, r.SettlementStress, r.Seed, r.Tile, r.TransitionTile, r.TransitionHabitat, r.Support, r.TransitionSupport, r.TransitionPopulation, r.TransitionSupportRatio, r.ControlledPopulationBefore, r.ControlledPopulationAfter, r.ControlledFoodBefore, r.ControlledFoodAfter, r.InitialPopulation, r.FinalPopulation, r.PopulationMultiplier, r.ReserveMultiplier, r.InitialFood, r.FinalFood, r.Technology, r.Rule, r.Turns, r.PostTransitionTurns, r.Moves, r.Deaths, r.MinFoodSatisfaction, r.FinalQ, r.FinalE, r.FinalQoL, r.TotalDiscarded, r.MaxFoodBalanceError, r.MaxEcologyBalanceError, r.MaxBiomassBalanceError, r.MaxWaterBalanceError, r.MaxMaterialBalanceError, r.MaxManufacturingFoodError, r.InvariantFailures, r.ScarceSourceIds.Length, r.ScarcityBinding, r.Error);
    }
    static string Csv(params object[] values) => string.Join(",", values.Select(value => "\"" + Convert.ToString(value, CultureInfo.InvariantCulture).Replace("\"", "\"\"") + "\""));
    static string Summary(IEnumerable<Result> results, IDictionary<string, Candidate> sites, IDictionary<string, int> support, Result scalar, Result parallel, Result saved, bool replayVerified)
    {
        var lines = new List<string> { "# Paleo to Agriculture transition validation", "", "Generated-site, live-policy measurement only. Replay is recorded only when the full scalar/parallel/save trio completes.", "", "| Case | Technology/rule | People | q / E | minimum food satisfaction | QoL | discarded | moves |", "|---|---:|---:|---:|---:|---:|---:|---:|" };
        foreach (var r in results) lines.Add("| " + r.Id + " | " + r.Technology + " / " + r.Rule + " | " + r.InitialPopulation + " → " + r.FinalPopulation + " | " + r.FinalQ.ToString("F3") + " / " + r.FinalE.ToString("F3") + " | " + r.MinFoodSatisfaction.ToString("F3") + " | " + r.FinalQoL.ToString("F3") + " | " + r.TotalDiscarded.ToString("F3") + " | " + r.Moves + " |");
        lines.Add(""); lines.Add("Support estimator: isolated q=0 Paleo hold-order clone, annual growth disabled, largest population with no deaths, full satisfaction in the final three of eight generations, and final food at least its one-generation initial reserve. Supports: " + string.Join(", ", support.OrderBy(x => x.Key).Select(x => x.Key + "=" + x.Value)) + ".");
        lines.Add(!replayVerified ? "Replay is pending: this pilot records matrix measurements only and does not claim a scalar/parallel/save replay check." : "Replay fingerprints (scalar / reversed two-worker / save continuation): " + scalar.Fingerprint + " / " + parallel.Fingerprint + " / " + saved.Fingerprint + ".");
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }
}
