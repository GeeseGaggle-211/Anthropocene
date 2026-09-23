using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Security.Cryptography;
using Anthropocene.Simulation;

// Measurement only.  The live resolver, ecology and economic planner are never retuned here.
internal static class Program
{
    static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true };
    const int ProbeTurns = 8, MainTurns = 25, ProbeBracketCap = 512;
    sealed class Candidate { public int Tile; public double HerdProxy, HumanProxy, Score; public int[] Footprint; }
    sealed class Support { public int Limit; public bool AtLimit, AtNext; public string Failure; public object First, Final, Tail, NextFirst, NextFinal, NextTail; }
    sealed class TurnAudit { public bool PopulationConserved, FoodConserved, EcologyConserved; public double PopulationResidual, FoodResidual, MaxBiomassResidual, MaxFoodResidual, MaxWaterResidual; }
    static void Main(string[] args) { try { RunMain(args); } catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; } }
    static void RunMain(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string Value(string name, string fallback) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback; }
        int seed = int.Parse(Value("--seed", "18")); bool pilot = args.Contains("--pilot");
        string root = Path.GetFullPath(Value("--repo", ".")), output = Path.GetFullPath(Value("--out", "TestResults/Issue20/Issue18-Pilot")); Directory.CreateDirectory(output);
        var cfg = JsonSerializer.Deserialize<SimulationConfig>(File.ReadAllText(Path.Combine(root, "Assets/_Anthropocene/Resources/OpeningConfig.json")), Json);
        var resources = JsonSerializer.Deserialize<ResourceGenerationOptions>(File.ReadAllText(Path.Combine(root, "Assets/_Anthropocene/Resources/ResourceGenerationConfig.json")), Json);
        cfg.Width = 128; cfg.Height = 64; cfg.Players = 8;
        var generated = new WorldGenerator().Generate(seed, cfg, resources); string sourceHash = SourceManifest(root);
        AssertLevers(generated.Config);
        var candidates = Candidates(generated);
        // The reference anchors are comparison values, not a criterion for discarding
        // usable pasture.  First choose the richest deterministic occupied-tile
        // forage proxy, then retain the closest human-gather proxy within that set.
        var positivePasture = candidates.Where(c => c.HerdProxy > 0).ToArray();
        if (positivePasture.Length == 0) throw new InvalidOperationException("No eligible footprint has positive occupied-tile herd proxy.");
        var shortList = positivePasture.OrderByDescending(c => c.HerdProxy).ThenBy(c => Math.Abs(c.HumanProxy - 55.1)).ThenBy(c => c.Tile).Take(pilot ? 2 : 12).ToArray();
        File.WriteAllText(Path.Combine(output, "selection-proxies.json"), JsonSerializer.Serialize(new { seed, dimensions = new { cfg.Width, cfg.Height, cfg.Players }, compactFixture = true, configHash = SaveCodec.ConfigurationHash(cfg), sourceManifestHash = sourceHash, candidates, shortlist = shortList, probeBracketCap = ProbeBracketCap }, Json));
        byte[] pristine = SaveCodec.Save(generated);
        var supports = shortList.Select(c => new { candidate = c, herd = HerdSupport(pristine, c), human = HumanSupport(pristine, c) }).ToArray();
        var stressCandidate = candidates.OrderBy(c => Math.Abs(c.HumanProxy - 55.1)).ThenBy(c => c.Tile).First();
        var stressSupport = new { candidate = stressCandidate, herd = HerdSupport(pristine, stressCandidate), human = HumanSupport(pristine, stressCandidate) };
        File.WriteAllText(Path.Combine(output, "support-probes.json"), JsonSerializer.Serialize(new { seed, shortlist = supports, humanAnchorStress = stressSupport }, Json));
        var viable = supports.Where(s => s.herd.Limit > 0).ToArray();
        if (viable.Length == 0) throw new InvalidOperationException("Pasture-first shortlist had no finite eight-generation managed-herd support; see selection evidence.");
        var chosen = viable.OrderBy(s => Math.Abs(s.herd.Limit - 200) / 200d + Math.Abs(s.human.Limit - 55.1) / 55.1).ThenBy(s => s.candidate.Tile).First();
        var antelope = cfg.GetSpecies("antelope");
        File.WriteAllText(Path.Combine(output, "selection.json"), JsonSerializer.Serialize(new { seed, dimensions = new { cfg.Width, cfg.Height, cfg.Players }, sourceManifestHash = sourceHash, levers = new { cfg.AnimalFoodPerManagedAnimal, cfg.AnimalsPerHerder, cfg.AnimalFoodNutritionExponent, cfg.AnimalManagementExponent, cfg.Consumption, cfg.GatherPerPerson, cfg.BaseDays, cfg.TurnDays, cfg.DaysPerYear, cfg.PracticeDays, cfg.StarvationGraceTurns, cfg.Mortality, antelope = new { antelope.Id, antelope.GrowthRate, antelope.FoodConsumption, antelope.FreshwaterConsumption, antelope.FoodYield } }, shortlistRule = "positive occupied-tile herd proxy, descending pasture proxy, then nearest radius-one human-gather anchor; finite eight-generation probes choose the closest nonzero herd/human support pair", candidates, shortlist = supports, selected = chosen, humanAnchorStress = stressSupport }, Json));
        using var trace = new StreamWriter(Path.Combine(output, "trajectory.jsonl")); using var matrix = new StreamWriter(Path.Combine(output, "matrix.jsonl"));
        foreach (double b in pilot ? new[] { .4, .8 } : new[] { .4, .5, .6, .7, .8 })
        {
            matrix.WriteLine(JsonSerializer.Serialize(Run(pristine, chosen.candidate, b, "live-pasture-b" + b.ToString("F2", CultureInfo.InvariantCulture), trace, MainTurns, null), Json));
            matrix.WriteLine(JsonSerializer.Serialize(Run(pristine, stressCandidate, b, "live-human-anchor-stress-b" + b.ToString("F2", CultureInfo.InvariantCulture), trace, MainTurns, null), Json));
        }
        foreach (int animals in new[] { 100, 200, 400 }) matrix.WriteLine(JsonSerializer.Serialize(Run(pristine, chosen.candidate, 0, "fixed-forage-b0-" + animals, trace, 2, animals), Json));
        var b0Duration = new List<object>(); foreach (int years in new[] { 20, 10, 5 }) { var result = Run(pristine, chosen.candidate, 0, "duration-ample-b0-" + years, trace, 20 / years, 100, years); b0Duration.Add(result); matrix.WriteLine(JsonSerializer.Serialize(result, Json)); }
        var positiveDuration = new List<object>(); foreach (int years in new[] { 20, 10, 5 }) { var result = Run(pristine, chosen.candidate, .6, "duration-ample-b060-" + years, trace, 20 / years, 100, years); positiveDuration.Add(result); matrix.WriteLine(JsonSerializer.Serialize(result, Json)); }
        var scarceDuration = new List<object>(); foreach (int years in new[] { 20, 10, 5 }) { var result = Run(pristine, chosen.candidate, 0, "duration-scarce-b0-" + years, trace, 20 / years, 100, years); scarceDuration.Add(result); matrix.WriteLine(JsonSerializer.Serialize(result, Json)); }
        AssertDurationContracts(b0Duration, positiveDuration, scarceDuration);
        if (!pilot) Replay(pristine, chosen.candidate, trace, matrix);
        Console.WriteLine("DONE seed=" + seed + " selected=" + chosen.candidate.Tile + " herdSupport=" + chosen.herd.Limit + " humanSupport=" + chosen.human.Limit);
    }
    static string SourceManifest(string root)
    {
        using var sha = SHA256.Create();
        var files = Directory.GetFiles(Path.Combine(root, "Assets/_Anthropocene/Simulation"), "*.cs", SearchOption.AllDirectories)
            .Append(Path.Combine(root, "Tools/NeolithicHusbandryValidation/Program.cs"))
            .OrderBy(path => Path.GetRelativePath(root, path), StringComparer.Ordinal);
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(string.Join("\n", files.Select(path => Path.GetRelativePath(root, path).Replace('\\', '/') + "\n" + File.ReadAllText(path))));
        return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
    }
    static void AssertLevers(SimulationConfig c)
    {
        if (Math.Abs(c.AnimalFoodPerManagedAnimal - 1) > 1e-12 || Math.Abs(c.AnimalsPerHerder - 2) > 1e-12 || Math.Abs(c.AnimalFoodNutritionExponent - 1) > 1e-12 || Math.Abs(c.AnimalManagementExponent - 1) > 1e-12) throw new InvalidOperationException("Live husbandry calibration differs from Issue 18 anchors.");
        var antelope = c.GetSpecies("antelope"); if (Math.Abs(antelope.GrowthRate - .0201) > 1e-12 || Math.Abs(antelope.FoodConsumption - .4) > 1e-12 || Math.Abs(antelope.FreshwaterConsumption - .45) > 1e-12) throw new InvalidOperationException("Live antelope interface differs from Issue 18 reference.");
    }
    static double Metric(object value, string name) => JsonDocument.Parse(JsonSerializer.Serialize(value, Json)).RootElement.GetProperty(name).GetDouble();
    static void Near(double actual, double expected, string description) { if (Math.Abs(actual - expected) > 1e-6 * Math.Max(1, Math.Abs(expected))) throw new InvalidOperationException("Duration contract failed: " + description + " actual=" + actual + " expected=" + expected); }
    static void AssertDurationContracts(IReadOnlyList<object> b0, IReadOnlyList<object> positive, IReadOnlyList<object> scarce)
    {
        foreach (string metric in new[] { "acceptedAnimalProduct", "potentialAnimalProduct", "managedFoodRequested", "managedFoodRealized", "managedWaterRequested", "managedWaterRealized" }) { Near(Metric(b0[1], metric), Metric(b0[0], metric), "b0 2x10=" + metric); Near(Metric(b0[2], metric), Metric(b0[0], metric), "b0 4x5=" + metric); }
        double positiveTotal = Metric(positive[0], "finalAnimals") + Metric(positive[0], "finalGrowthRemainder");
        Near(Metric(positive[1], "finalAnimals") + Metric(positive[1], "finalGrowthRemainder"), positiveTotal, "healthy b positive 2x10 animal+remainder");
        Near(Metric(positive[2], "finalAnimals") + Metric(positive[2], "finalGrowthRemainder"), positiveTotal, "healthy b positive 4x5 animal+remainder");
        if (!(Metric(scarce[0], "finalDeficitTurns") > 0 && Metric(scarce[2], "finalDeficitTurns") > Metric(scarce[0], "finalDeficitTurns") && Metric(scarce[2], "finalAnimals") <= Metric(scarce[0], "finalAnimals"))) throw new InvalidOperationException("Scarce duration fixture did not expose existing decision-grace/remainder behavior.");
    }
    static Candidate[] Candidates(WorldState w)
    {
        var result = new List<Candidate>();
        foreach (var t in w.Tiles.Where(t => t.TraversableLand && t.Environment != EnvironmentKind.Ice))
        {
            int[] ids = w.Map.Radius(t.Id, 1).OrderBy(n => n).ToArray(); if (ids.Length != 7 || ids.Any(n => !w.Tiles[n].TraversableLand)) continue;
            double human = ids.Sum(n => w.Tiles[n].Seasons.Average(s => s.MaxFood * s.FoodRecoveryPerDay * w.Config.BaseDays / Math.Max(1e-12, w.Config.Consumption)));
            // Domestic herds forage at their occupied tile; freshwater remains the source resolved for that tile.
            int water = FreshwaterAccess.Source(w, t.Id);
            double herd = Math.Min(t.Seasons.Average(s => s.MaxPotentialFood * s.BiomassRecoveryPerDay * w.Config.BaseDays / .4), w.Tiles[water].Seasons.Average(s => s.MaxFreshwater * s.WaterRecoveryPerDay * w.Config.BaseDays / .45));
            result.Add(new Candidate { Tile = t.Id, Footprint = ids, HerdProxy = herd, HumanProxy = human, Score = Math.Abs(herd - 200) / 200d + Math.Abs(human - 55.1) / 55.1 });
        }
        return result.OrderBy(c => c.Tile).ToArray();
    }
    static WorldState Setup(byte[] bytes, Candidate candidate, int animals = 10, int durationYears = 20)
    {
        var w = SaveCodec.Load(bytes); w.Camps.Clear(); w.Societies.Clear(); w.Hunters.Clear(); w.Improvements.Clear(); w.Herds.RemoveAll(h => h.DomesticCamp >= 0);
        w.Config.TurnDays = durationYears * w.Config.DaysPerYear;
        var c = new Camp { Id = w.NextId++, Player = 0, Tile = candidate.Tile, Population = 24, Technology = Technology.AnimalHusbandry, Workforce = new PopulationLedger(), ManagedBreedingIntensity = .55 };
        c.Technologies[(int)Technology.AnimalHusbandry] = true; c.People.Progression.Era = Era.Neolithic; w.Camps.Add(c); Ownership.Bind(w);
        c.PlantFood = c.Population * w.Config.Consumption * w.Config.GenerationIntervals * w.Config.StartingReserveGenerations;
        w.Herds.Add(new Herd { Id = w.NextId++, Tile = c.Tile, Species = "antelope", Animals = animals, DomesticCamp = c.Id, OwnerSociety = c.SocietyId });
        return w;
    }
    static Support HerdSupport(byte[] bytes, Candidate c) => Binary(bytes, c, true);
    static Support HumanSupport(byte[] bytes, Candidate c) => Binary(bytes, c, false);
    static Support Binary(byte[] bytes, Candidate c, bool herd)
    {
        bool Stable(int n, out object first, out object final, out object tail, out string fail)
        {
            var w = Setup(bytes, c, herd ? n : 0); var camp = w.Camps.Single(); w.Config.AnnualHumanGrowthRate = 0; camp.ManagedBreedingIntensity = 0;
            if (herd) { camp.Population = Math.Max(1, (int)Math.Ceiling(n / 2d)); camp.Workforce.Assign(WorkforceRoles.Herder, camp.Population, camp.Population); }
            else { camp.Population = n; camp.Workforce.Normalize(n); }
            camp.PlantFood = camp.Population * w.Config.Consumption * w.Config.GenerationIntervals * w.Config.StartingReserveGenerations;
            first = Stocks(w, c); final = tail = null; fail = null;
            for (int i = 0; i < ProbeTurns; i++)
            {
                var intent = EconomicIntents.Automatic(w, camp, true); intent.ManagedBreedingIntensity = 0; intent.AutomationTurns = 0;
                var r = Resolve(w, camp, intent); var h = w.Herds.LastOrDefault(x => x.DomesticCamp == camp.Id); final = Stocks(w, c); tail = new { turn = i + 1, food = camp.FoodNeedsMetRatio, water = camp.WaterNeedsMetRatio, herdNutrition = h?.LastNutrition ?? 1, herdWater = h?.LastWaterNutrition ?? 1, animalDeaths = h?.StarvationDeaths ?? 0, humanDeaths = r.Deaths, ecology = r.EcologyBalances.Select(x => new { x.Tile, x.BiomassRecharge, x.BiomassHarvest, x.BiomassOverflow, x.FoodRecharge, x.FoodHarvest, x.FoodOverflow, x.WaterRecharge, x.WaterHarvest, x.WaterOverflow }).ToArray(), pressure = r.Pressure.Where(x => c.Footprint.Contains(x.Tile) || x.Tile == FreshwaterAccess.Source(w, c.Tile)).ToArray() };
                if (r.Deaths > 0 || camp.FoodNeedsMetRatio < .999999 || herd && (h.LastNutrition < .999999 || h.StarvationDeaths > 0)) { fail = "finite-eight-turn nutrition/death condition"; return false; }
            }
            return true;
        }
        object first = null, final = null, tail = null; string failure = null;
        if (!Stable(1, out first, out final, out tail, out failure)) return new Support { Limit = 0, Failure = failure, First = first, Final = final, Tail = tail };
        int low = 1, high = 2; while (Stable(high, out first, out final, out tail, out failure)) { low = high; high *= 2; if (high > ProbeBracketCap) return new Support { Limit = low, AtLimit = true, Failure = "bracket-cap-" + ProbeBracketCap, First = first, Final = final, Tail = tail }; }
        while (low < high) { int mid = (low + high + 1) / 2; if (Stable(mid, out first, out final, out tail, out failure)) low = mid; else high = mid - 1; }
        bool at = Stable(low, out first, out final, out tail, out failure); bool next = Stable(low + 1, out var nf, out var nl, out var nt, out var nfail);
        return new Support { Limit = low, AtLimit = at, AtNext = next, Failure = nfail, First = first, Final = final, Tail = tail, NextFirst = nf, NextFinal = nl, NextTail = nt };
    }
    static object Stocks(WorldState w, Candidate c) => new { occupied = c.Tile, waterSource = FreshwaterAccess.Source(w, c.Tile), tiles = c.Footprint.Select(id => { var v = LazyEcology.ProjectEcology(w, id, w.Days); var k = new EcologyCapacity(w, w.Tiles[id], v, (int)Math.Floor(w.Days / w.Config.BaseDays) % 4); return new { id, B = v.PotentialFood, G = v.Food, W = v.Freshwater, v.LandCondition, v.CurrentVegetation, capacityB = k.Biomass, capacityG = k.Food, capacityW = k.Water, rechargeB = w.Tiles[id].Seasons.Sum(s => s.BiomassRecoveryPerDay), rechargeG = w.Tiles[id].Seasons.Sum(s => s.FoodRecoveryPerDay), rechargeW = w.Tiles[id].Seasons.Sum(s => s.WaterRecoveryPerDay) }; }).ToArray() };
    static TurnRecord Resolve(WorldState w, Camp c, EconomicIntent intent, PhaseExecution execution = null)
    {
        var order = new Order(OrderKind.Economy, c.Id) { Economy = intent }; var resolver = new TurnResolver(execution); string error = resolver.Validate(w, c.Player, order); if (error != null) throw new InvalidOperationException(error);
        return resolver.Resolve(w, new Dictionary<int, List<Order>> { [c.Player] = new List<Order> { order } });
    }
    static TurnAudit Audit(TurnRecord record, DemographicReport demographic)
    {
        var audit = new TurnAudit();
        audit.PopulationResidual = demographic.StartingPopulation + demographic.Additions - demographic.Deaths - demographic.EndingPopulation;
        audit.FoodResidual = demographic.InitialReserves + demographic.FoodReceived - demographic.FoodConsumed - demographic.ManufacturingFood - demographic.DiscardedInventory - demographic.FinalReserves;
        foreach (var e in record.EcologyBalances)
        {
            audit.MaxBiomassResidual = Math.Max(audit.MaxBiomassResidual, Math.Abs(e.StartingBiomass + e.BiomassRecharge - e.BiomassHarvest - e.BiomassOverflow - e.EndingBiomass));
            audit.MaxFoodResidual = Math.Max(audit.MaxFoodResidual, Math.Abs(e.StartingFood + e.FoodRecharge - e.FoodHarvest - e.FoodOverflow - e.EndingFood));
            audit.MaxWaterResidual = Math.Max(audit.MaxWaterResidual, Math.Abs(e.StartingWater + e.WaterRecharge - e.WaterHarvest - e.WaterOverflow - e.EndingWater));
        }
        audit.PopulationConserved = Math.Abs(audit.PopulationResidual) < 1e-9;
        audit.FoodConserved = Math.Abs(audit.FoodResidual) < 1e-7;
        audit.EcologyConserved = audit.MaxBiomassResidual < 1e-7 && audit.MaxFoodResidual < 1e-7 && audit.MaxWaterResidual < 1e-7;
        if (!audit.PopulationConserved || !audit.FoodConserved || !audit.EcologyConserved) throw new InvalidOperationException("Human/ecological conservation audit failed.");
        return audit;
    }
    static object Run(byte[] bytes, Candidate candidate, double b, string id, TextWriter trace, int turns, int? animals = null, int durationYears = 20, PhaseExecution execution = null, bool saveMidway = false)
    {
        var w = Setup(bytes, candidate, animals ?? 10, durationYears); var c = w.Camps.Single(); c.ManagedBreedingIntensity = b;
        bool durationFixture = id.StartsWith("duration-ample", StringComparison.Ordinal);
        bool scarceDurationFixture = id.StartsWith("duration-scarce", StringComparison.Ordinal);
        bool fixedForageFixture = id.StartsWith("fixed-forage", StringComparison.Ordinal);
        if (durationFixture) { c.Population = 100; c.Workforce.Assign(WorkforceRoles.Herder, 50, c.Population); c.PlantFood = c.AnimalFood = c.AquaticFood = 0; w.Config.AnnualHumanGrowthRate = 0; ForceAmple(w, candidate); }
        if (scarceDurationFixture) { c.Population = 100; c.Workforce.Assign(WorkforceRoles.Herder, 50, c.Population); c.PlantFood = c.AnimalFood = c.AquaticFood = 0; w.Config.AnnualHumanGrowthRate = 0; ForceScarce(w, candidate); }
        if (fixedForageFixture) { c.Population = 200; c.Workforce.Assign(WorkforceRoles.Herder, (int)Math.Ceiling((animals ?? 10) / 2d), c.Population); c.PlantFood = c.AnimalFood = c.AquaticFood = 0; w.Config.AnnualHumanGrowthRate = 0; ForceFixedForage(w, candidate); }
        int initialResidents = c.Population, initialAnimals = w.Herds.Single(h => h.DomesticCamp == c.Id).Animals;
        double totalAnimalBirths = 0, totalAnimalDeaths = 0, accepted = 0, potential = 0, managedFoodRequested = 0, managedFoodRealized = 0, managedWaterRequested = 0, managedWaterRealized = 0; string error = null;
        trace.WriteLine(JsonSerializer.Serialize(new { id, @event = "initial", durationFixture, scarceDurationFixture, fixedForageFixture, initialResidents, initialAnimals, b, stocks = Stocks(w, candidate), camp = new { c.Id, c.PlantFood, c.AnimalFood, c.AquaticFood, c.Food, c.FoodNeedsMetRatio, c.WaterNeedsMetRatio, workforce = c.Workforce.Assignments }, herds = w.Herds.Where(h => h.DomesticCamp == c.Id).ToArray() }, Json));
        try { for (int i = 0; i < turns; i++) { int residentBefore = c.Population; var herdBeforeState = w.Herds.Single(h => h.DomesticCamp == c.Id); var herdBefore = new { herdBeforeState.Animals, herdBeforeState.GrowthRemainder, herdBeforeState.DeathRemainder, herdBeforeState.DeficitTurns }; int herdTileBefore = herdBeforeState.Tile, waterSourceBefore = FreshwaterAccess.Source(w, herdTileBefore); var startingAssignments = c.Workforce.Assignments.ToDictionary(a => a.Role, a => a.Population); var intent = EconomicIntents.Automatic(w, c, true); intent.ManagedBreedingIntensity = b; intent.AutomationTurns = 0; var submittedAssignments = intent.Assignments.ToDictionary(a => a.Role, a => a.Population); var r = Resolve(w, c, intent, execution); var herd = w.Herds.Single(h => h.DomesticCamp == c.Id); var d = r.Demographics.Single(x => x.Camp == c.Id); var report = r.ManagedHerds.Single(x => x.Herd == herd.Id); totalAnimalBirths += report.NewbornAnimals; totalAnimalDeaths += report.StarvationDeaths; accepted += report.AcceptedFood; potential += report.PotentialFood;
            var audit = Audit(r, d); var assigned = c.Workforce.Assignments.ToDictionary(a => a.Role, a => a.Population); double gathered = r.Transfers.Where(t => t.Entity == c.Id && (t.Kind == "Gathering" || t.Kind == "Aquatic gathering" || t.Kind == "Marine gathering")).Sum(t => t.Food); double materialGathererIntervals = r.MaterialTransfers.Where(t => t.Camp == c.Id && t.Role == WorkforceRoles.Gatherer).Sum(t => t.WorkerIntervals);
            var species = w.Config.GetSpecies(herd.Species); double intervals = r.Duration / w.Config.BaseDays; var herdPressure = r.Pressure.Where(p => p.Entity == herd.Id && p.Kind == "Herd").ToArray();
            managedFoodRequested += herdPressure.Where(p => p.Pool == "Biomass").Sum(p => p.Requested); managedFoodRealized += herdPressure.Where(p => p.Pool == "Biomass").Sum(p => p.Realized); managedWaterRequested += herdPressure.Where(p => p.Pool == "Freshwater").Sum(p => p.Requested); managedWaterRealized += herdPressure.Where(p => p.Pool == "Freshwater").Sum(p => p.Realized);
            bool animalConserved = herdBefore.Animals + report.NewbornAnimals - report.StarvationDeaths == herd.Animals;
            bool managedClaims = herdBefore.Animals <= 0
                ? herdPressure.All(p => Math.Abs(p.Requested) <= 1e-9 && Math.Abs(p.Realized) <= 1e-9)
                : herdPressure.Count(p => p.Pool == "Biomass" && p.Tile == herdTileBefore) == 1 && herdPressure.Count(p => p.Pool == "Freshwater" && p.Tile == waterSourceBefore) == 1 && Math.Abs(herdPressure.Where(p => p.Pool == "Biomass").Sum(p => p.Requested) - herdBefore.Animals * species.FoodConsumption * intervals) < 1e-6 && Math.Abs(herdPressure.Where(p => p.Pool == "Freshwater").Sum(p => p.Requested) - herdBefore.Animals * species.FreshwaterConsumption * intervals) < 1e-6;
            trace.WriteLine(JsonSerializer.Serialize(new { id, turn = i + 1, year = w.Days / w.Config.DaysPerYear, intervals, b, durationFixture, scarceDurationFixture, fixedForageFixture, campId = c.Id, herdId = herd.Id, herdTileBefore, waterSourceBefore, residentBefore, resident = c.Population, linked = w.LinkedPopulation(c), herdBefore, herd = herd.Animals, animalRatio = c.Population == 0 ? 0 : herd.Animals / (double)c.Population, herdAfterState = new { herd.GrowthRemainder, herd.DeathRemainder, herd.DeficitTurns }, report, submittedIntent = intent, startingAssignments, submittedAssignments, endingAssignments = assigned, residualGatherers = c.GathererPopulation, gatheringFood = gathered, gatheringWorkerEquivalent = gathered / Math.Max(1e-12, w.Config.GatherPerPerson * w.Config.GenerationIntervals), materialGathererIntervals, nonHerderUnreservedUpperBound = Math.Max(0, c.Population - assigned.GetValueOrDefault(WorkforceRoles.Herder)), campFood = new { c.PlantFood, c.AnimalFood, c.AquaticFood, c.Food, c.FoodNeedsMetRatio, c.WaterNeedsMetRatio }, demographic = d, conservation = audit, transfers = r.Transfers.Where(t => t.Entity == c.Id || t.Entity == herd.Id).ToArray(), ecology = r.EcologyBalances, pressure = r.Pressure, managedPressure = herdPressure, production = r.Production.Where(p => p.Camp == c.Id).ToArray(), materials = r.MaterialTransfers.Where(m => m.Camp == c.Id).ToArray(), changes = r.PopulationChanges.Where(p => p.Camp == c.Id).ToArray(), moves = c.Moves, decisions = r.Decisions, animalConserved, managedClaims, endStocks = Stocks(w, candidate) }, Json)); trace.Flush(); if (!animalConserved || !managedClaims) throw new InvalidOperationException("Managed herd conservation or source-claim audit failed."); if (saveMidway && i + 1 == turns / 2) { w = SaveCodec.Load(SaveCodec.Save(w)); c = w.Camps.Single(); }
        } } catch (Exception ex) { error = ex.GetType().Name + ": " + ex.Message; trace.WriteLine(JsonSerializer.Serialize(new { id, @event = "error", error }, Json)); throw; }
        var finalHerd = w.Herds.Single(h => h.DomesticCamp == c.Id); return new { id, b, turns, durationYears, durationFixture, scarceDurationFixture, fixedForageFixture, initialResidents, finalResidents = c.Population, initialAnimals, finalAnimals = finalHerd.Animals, finalGrowthRemainder = finalHerd.GrowthRemainder, finalDeathRemainder = finalHerd.DeathRemainder, finalDeficitTurns = finalHerd.DeficitTurns, totalAnimalBirths, totalAnimalDeaths, acceptedAnimalProduct = accepted, potentialAnimalProduct = potential, managedFoodRequested, managedFoodRealized, managedWaterRequested, managedWaterRealized, error, fingerprint = SaveCodec.Fingerprint(w) };
    }
    static void ForceAmple(WorldState w, Candidate c)
    {
        foreach (int id in c.Footprint.Append(FreshwaterAccess.Source(w, c.Tile)).Distinct()) { w.Tiles[id].PotentialFood = 1e9; w.Tiles[id].Freshwater = 1e9; w.Tiles[id].Food = 0; for (int season = 0; season < w.Tiles[id].Seasons.Length; season++) { w.Tiles[id].Seasons[season].MaxPotentialFood = MathF.Max(w.Tiles[id].Seasons[season].MaxPotentialFood, 1e9f); w.Tiles[id].Seasons[season].MaxFreshwater = MathF.Max(w.Tiles[id].Seasons[season].MaxFreshwater, 1e9f); w.Tiles[id].Seasons[season].MaxFood = 0; } }
    }
    // A disclosed external range fixture.  It sets one common forage capacity and
    // recovery curve once, rather than replenishing a stock on every decision.
    // The raw ledger records the measured receipts; the nominal capacity/rate is
    // deliberately not a claim of a 200-animal sustainable carrying capacity.
    static void ForceFixedForage(WorldState w, Candidate c)
    {
        var species = w.Config.GetSpecies("antelope");
        double flow = 200 * species.FoodConsumption * w.Config.GenerationIntervals;
        double capacity = flow * 2;
        double rate = -Math.Log(1 - flow / capacity) / w.Config.TurnDays;
        foreach (int id in c.Footprint) { w.Tiles[id].Food = 0; for (int season = 0; season < w.Tiles[id].Seasons.Length; season++) w.Tiles[id].Seasons[season].MaxFood = 0; }
        var pasture = w.Tiles[c.Tile]; pasture.PotentialFood = capacity; pasture.LandCondition = 1;
        for (int season = 0; season < pasture.Seasons.Length; season++) { pasture.Seasons[season].MaxPotentialFood = (float)capacity; pasture.Seasons[season].BiomassRecoveryPerDay = (float)rate; }
        var water = w.Tiles[FreshwaterAccess.Source(w, c.Tile)]; water.Freshwater = 1e9;
        for (int season = 0; season < water.Seasons.Length; season++) water.Seasons[season].MaxFreshwater = 1e9f;
    }
    // A one-time zero-stock/capacity fixture for observing existing grace and
    // duration-scaled starvation remainder semantics.  It changes no live rule.
    static void ForceScarce(WorldState w, Candidate c)
    {
        foreach (int id in c.Footprint.Append(FreshwaterAccess.Source(w, c.Tile)).Distinct())
        {
            w.Tiles[id].Food = w.Tiles[id].PotentialFood = w.Tiles[id].Freshwater = 0;
            for (int season = 0; season < w.Tiles[id].Seasons.Length; season++)
            {
                w.Tiles[id].Seasons[season].MaxFood = 0;
                w.Tiles[id].Seasons[season].MaxPotentialFood = 0;
                w.Tiles[id].Seasons[season].MaxFreshwater = 0;
            }
        }
    }
    static void Replay(byte[] bytes, Candidate c, TextWriter trace, TextWriter matrix)
    {
        var scalar = Run(bytes, c, .6, "replay-scalar", trace, MainTurns);
        var reversed = Run(bytes, c, .6, "replay-reversed-two-worker", trace, MainTurns, null, 20, new PhaseExecution(2, 1, true, 1));
        var saved = Run(bytes, c, .6, "replay-save-continuation", trace, MainTurns, null, 20, null, true);
        string a = JsonDocument.Parse(JsonSerializer.Serialize(scalar, Json)).RootElement.GetProperty("fingerprint").GetString();
        string b = JsonDocument.Parse(JsonSerializer.Serialize(reversed, Json)).RootElement.GetProperty("fingerprint").GetString();
        string d = JsonDocument.Parse(JsonSerializer.Serialize(saved, Json)).RootElement.GetProperty("fingerprint").GetString();
        if (a != b || a != d) throw new InvalidOperationException("Scalar/reversed-two-worker/save-continuation fingerprints differ.");
        matrix.WriteLine(JsonSerializer.Serialize(new { id = "replay", scalar, reversedTwoWorker = reversed, saveContinuation = saved, fingerprint = a, equal = true }, Json)); matrix.Flush();
    }
}
