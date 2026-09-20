using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Anthropocene.Simulation;

// Measurement only: uses production constants, economic intents and resolver unchanged.
internal static class Program
{
    static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true };
    sealed class Variant
    {
        public string Id, Family;
        public Dictionary<string, double> Overrides = new Dictionary<string, double>();
        public Variant(string id, string family, params (string, double)[] values)
        { Id = id; Family = family; foreach (var v in values) Overrides.Add(v.Item1, v.Item2); }
        public void Apply(SimulationConfig c)
        {
            foreach (var v in Overrides)
            {
                var f = typeof(NeolithicConfig).GetField(v.Key);
                if (f != null) f.SetValue(c.Neolithic, v.Value); else typeof(SimulationConfig).GetField(v.Key).SetValue(c, v.Value);
            }
            c.Validate();
        }
    }
    static IEnumerable<Variant> Variants(bool pilot)
    {
        yield return new Variant("baseline", "baseline");
        if (pilot) yield break;
        foreach (double f in new[] { .25, .5, 2, 4 }) yield return new Variant("fabric-" + f, "fabric", ("FabricConsumptionPerPersonTurn", f));
        foreach (double r in new[] { 3d, 4, 5, 6 }) foreach (double d in new[] { .05, .1, .15, .2 })
            if (r != 4 || d != .1) yield return new Variant("housing-" + r + "-" + d, "housing", ("HousingPeoplePerUnit", r), ("HousingDecayPerTurn", d));
        foreach (double f in new[] { .25, .5, 2, 4 }) foreach (double r in new[] { 3d, 4, 5, 6 }) foreach (double d in new[] { .05, .1, .15, .2 })
            if (r != 4 || d != .1) yield return new Variant("joint-" + f + "-" + r + "-" + d, "joint", ("FabricConsumptionPerPersonTurn", f), ("HousingPeoplePerUnit", r), ("HousingDecayPerTurn", d));
        foreach (double x in new[] { 1.25, 1.75, 2 }) yield return new Variant("farmer-" + x, "farmer", ("FarmerProductivity", x));
        foreach (double x in new[] { .5, .8 }) yield return new Variant("flux-" + x, "flux", ("CultivationRegenFlux", x));
        foreach (double x in new[] { 40d, 160 }) yield return new Variant("decay-" + x, "decay", ("DecayHalfLifeYears", x));
        foreach (double x in new[] { 1.5, 4 }) yield return new Variant("builder-" + x, "builder", ("BuilderHousingOutputPerBaseDays", x / 80));
        foreach (double x in new[] { 1.25, 1.75 }) yield return new Variant("improved-" + x, "improved", ("ImprovedToolsHousingMultiplier", x));
        foreach (double x in new[] { 1.5, 2.5 }) yield return new Variant("copper-" + x, "copper", ("CopperToolsHousingMultiplier", x));
        foreach (double x in new[] { .5, 2 }) yield return new Variant("extraction-" + x, "extraction", ("SpecialistMaterialExtractionRate", x));
        foreach (double f in new[] { 0d, .25, .4 }) foreach (double y in new[] { 60d, 100, 160 })
            if (f != .25 || y != 100) yield return new Variant("expectation-" + f + "-" + y, "expectation", ("RudimentaryExpectationFloor", f), ("RudimentaryExpectationRampYears", y));
        yield return new Variant("bridge-off", "bridge", ("GathererBridgeEfficiency", 0));
    }
    static void Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string Value(string name, string fallback) { int i = Array.IndexOf(args, name); return i >= 0 ? args[i + 1] : fallback; }
        int seed = int.Parse(Value("--seed", "11")); bool pilot = args.Contains("--pilot");
        string root = Path.GetFullPath(Value("--repo", ".")), output = Path.GetFullPath(Value("--out", "TestResults/NeolithicSweeps"));
        Directory.CreateDirectory(output);
        var cfg = JsonSerializer.Deserialize<SimulationConfig>(File.ReadAllText(Path.Combine(root, "Assets/_Anthropocene/Resources/OpeningConfig.json")), Json);
        var resources = JsonSerializer.Deserialize<ResourceGenerationOptions>(File.ReadAllText(Path.Combine(root, "Assets/_Anthropocene/Resources/ResourceGenerationConfig.json")), Json);
        cfg.Width = 256; cfg.Height = 128; cfg.Players = 16;
        File.WriteAllText(Path.Combine(output, "config-" + seed + ".json"), JsonSerializer.Serialize(cfg, Json));
        Console.WriteLine("GENERATING seed=" + seed);
        var world = new WorldGenerator().Generate(seed, cfg, resources); var sites = SelectSites(world);
        world.Camps.Clear(); world.Societies.Clear(); world.Hunters.Clear(); world.Herds.Clear(); world.Improvements.Clear();
        var original = SaveCodec.Save(world); var variants = Variants(pilot).Where(v => !args.Contains("--replicate") || v.Family == "baseline" || v.Family == "fabric" || v.Family == "farmer").Where(v => !args.Contains("--managed") || new[] { "baseline", "fabric", "farmer", "flux", "decay", "builder", "extraction" }.Contains(v.Family)).ToArray();
        File.WriteAllText(Path.Combine(output, "variants-" + seed + ".json"), JsonSerializer.Serialize(variants, Json));
        using var trajectories = new StreamWriter(Path.Combine(output, "trajectories-" + seed + ".jsonl"));
        using var finals = new StreamWriter(Path.Combine(output, "runs-" + seed + ".jsonl"));
        using var siteFile = new StreamWriter(Path.Combine(output, "sites-" + seed + ".jsonl"));
        foreach (var site in sites.Where(s => Value("--habitat", "all") == "all" || s.Habitat == Value("--habitat", "all")))
        {
            var template = SaveCodec.Load(original);
            var c = new Camp { Id = template.NextId++, Player = 0, Tile = site.Tile, Population = 250, PlantFood = 250 * cfg.Consumption * cfg.GenerationIntervals * cfg.StartingReserveGenerations };
            template.Camps.Add(c); Ownership.Bind(template);
            c.Technology = Technology.Cultivation; c.Technologies[(int)Technology.Cultivation] = true; c.People.Progression.Era = Era.Neolithic; c.IsVillage = true;
            var known = MaterialEconomy.KnownSources(template, c, 0);
            siteFile.WriteLine(JsonSerializer.Serialize(new { seed, site, initialPopulation = c.Population, initialFood = c.Food, initialQ = 0, initialManufacturedStocks = 0, sources = known.Select(s => new { s.Id, s.Material, s.Tile, s.Available }) }, Json)); siteFile.Flush();
            var baseline = SaveCodec.Save(template);
            foreach (var v in variants)
            {
                if (v.Family == "joint" && site.Habitat != "river") continue;
                if (args.Contains("--managed")) { Run(baseline, v, "managed-control", site.Habitat, seed, trajectories, finals); continue; }
                Run(baseline, v, "stock", site.Habitat, seed, trajectories, finals);
                if (v.Family == "baseline" || v.Family == "fabric" || v.Family == "farmer") Run(baseline, v, "replan", site.Habitat, seed, trajectories, finals);
            }
        }
        ToolEffects(output, seed, cfg);
        Console.WriteLine("DONE seed=" + seed + " sites=" + sites.Length + " variants=" + variants.Length);
    }
    sealed class Site
    {
        public int Tile, WoodedTiles; public string Habitat;
        public double WildCapacity, BiomassCapacity, InitialRenewalPerYear;
    }
    static Site[] SelectSites(WorldState w)
    {
        var candidates = new List<Site>();
        foreach (var t in w.Tiles.Where(t => t.TraversableLand && t.Environment != EnvironmentKind.Ice))
        {
            var ids = w.Map.Radius(t.Id, 1).ToArray();
            if (ids.Length != 7 || ids.Any(id => !w.Tiles[id].TraversableLand || w.Tiles[id].Environment == EnvironmentKind.Ice)) continue;
            if (t.Seasons.Min(s => s.MaxFreshwater) < w.Config.StartFreshwaterMinimum) continue;
            int woods = ids.Count(id => Vegetation.IsWooded(w.Tiles[id])); bool river = ids.Any(id => w.Tiles[id].River || w.Tiles[id].Oasis);
            string habitat = river ? "river" : woods >= 4 ? "wooded" : woods == 0 ? "open" : null;
            if (habitat == null) continue;
            candidates.Add(new Site { Tile = t.Id, Habitat = habitat, WoodedTiles = woods,
                WildCapacity = ids.Sum(id => w.Tiles[id].Seasons.Average(s => s.MaxFood)),
                BiomassCapacity = ids.Sum(id => w.Tiles[id].Seasons.Average(s => s.MaxPotentialFood)),
                InitialRenewalPerYear = ids.Sum(id => w.Tiles[id].Seasons.Average(s => s.MaxFood * s.FoodRecoveryPerDay)) * w.Config.DaysPerYear });
        }
        // Explicitly optimistic productive exemplars, not a random worldwide census.
        return new[] { "river", "wooded", "open" }.Select(h => candidates.Where(s => s.Habitat == h).OrderByDescending(s => s.InitialRenewalPerYear).ThenBy(s => s.Tile).First()).ToArray();
    }
    static void Run(byte[] baseline, Variant variant, string policy, string habitat, int seed, StreamWriter trajectories, StreamWriter finals)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var w = SaveCodec.Load(baseline); variant.Apply(w.Config); var c = w.Camps.Single();
        string run = seed + ":" + habitat + ":" + policy + ":" + variant.Id;
        var unlock = new Dictionary<string, double>(); double? majority = null, gatheringGone = null;
        int deaths = 0, failures = 0; double waste = 0; object last = null; string error = null;
        string hash = SaveCodec.ConfigurationHash(w.Config);
        try
        {
            for (int turn = 1; turn <= 25 && c.Population > 0; turn++)
            {
                int population = c.Population; var intent = policy == "managed-control" ? ManagedControl(w, c) : EconomicIntents.Automatic(w, c, policy == "replan");
                var order = new Order(OrderKind.Economy, c.Id) { Economy = intent }; var resolver = new TurnResolver();
                string validation = resolver.Validate(w, c.Player, order);
                if (validation != null) throw new Exception("Invalid audit order: " + validation);
                var assigned = intent.Assignments.ToDictionary(a => a.Role, a => a.Population);
                double Assigned(string role) => assigned.TryGetValue(role, out int n) ? n : 0;
                double crafts = Assigned(WorkforceRoles.Craftsperson), specialists = WorkforceRoles.Specialists.Sum(Assigned), farmers = Assigned(WorkforceRoles.Farmer);
                var r = resolver.Resolve(w, new Dictionary<int, List<Order>> { [c.Player] = new List<Order> { order } });
                double year = w.Days / w.Config.DaysPerYear;
                foreach (string tech in c.People.NeolithicTechnologies) if (!unlock.ContainsKey(tech)) unlock[tech] = year;
                var d = r.Demographics.Single(); deaths += r.Deaths; waste += d.DiscardedInventory;
                double farmFood = r.Transfers.Where(t => t.Kind == "Cultivation").Sum(t => t.Food);
                double gathered = r.Transfers.Where(t => t.Kind == "Gathering" || t.Kind == "Aquatic gathering" || t.Kind == "Marine gathering").Sum(t => t.Food), food = farmFood + gathered;
                if (majority == null && food > 0 && farmFood > food / 2) majority = year;
                if (gatheringGone == null && food > 0 && gathered <= food * .01) gatheringGone = year;
                var fields = CultivationEconomy.Footprint(w, c);
                double q = fields.Average(id => CultivationEconomy.Intensity(w.Config, w.Tiles[id].CultivationCapital));
                var production = r.Production.GroupBy(p => p.Good).ToDictionary(g => g.Key.ToString(), g => g.Sum(p => p.Output));
                double craftWork = r.Production.Sum(p => p.WorkerIntervals), materialWork = r.MaterialTransfers.Sum(p => p.WorkerIntervals);
                bool conserved = r.Production.All(p => Math.Abs(p.PlantFood - (p.Good == CraftGood.Cloth ? p.Output * w.Config.Neolithic.PlantFoodPerFabricUnit : 0)) < 1e-7 && (p.Good != CraftGood.Housing || Math.Abs(p.Wood + p.Clay - p.Output * w.Config.Neolithic.HousingMaterialUnitsPerHousing) < 1e-7)) && r.MaterialTransfers.All(t => Math.Abs(t.Before - t.After - t.Amount) < 1e-6);
                double gatheringCapacity = (population - assigned.Values.Sum() + Assigned(WorkforceRoles.Gatherer)) * w.Config.GenerationIntervals;
                bool labor = c.Workforce.Assigned <= c.Population && materialWork + gathered / w.Config.GatherPerPerson <= gatheringCapacity + 1e-6;
                if (!conserved || !labor || d.DiscardedInventory > 1e-6) failures++;
                last = new { run, seed, habitat, policy, variant = variant.Id, family = variant.Family, turn, year, startPopulation = population, population = c.Population, deaths = r.Deaths,
                    craftAssignedPct = 100 * crafts / population, specialistAssignedPct = 100 * specialists / population, farmerAssignedPct = 100 * farmers / population,
                    craftUsedPct = 100 * craftWork / (population * w.Config.GenerationIntervals), materialUsedPct = 100 * materialWork / (population * w.Config.GenerationIntervals),
                    craftAssignedCount = crafts, farmerAssignedCount = farmers, qMean = q, qMax = fields.Max(id => CultivationEconomy.Intensity(w.Config, w.Tiles[id].CultivationCapital)),
                    farmerFoodShare = food > 0 ? farmFood / food : 0, foodHarvest = food, farmFood, gatheredFood = gathered,
                    foodReserves = c.Food, foodReserveTurns = c.Population > 0 ? c.Food / (c.Population * w.Config.Consumption * w.Config.GenerationIntervals) : 0,
                    foodSatisfaction = c.FoodNeedsMetRatio, clothing = c.ClothPerCapita, housing = c.HousingPerCapita, qol = c.QoL, expectation = c.RudimentaryExpectation,
                    fabricConsumed = c.FabricConsumed, housingStock = c.Inventory.HousingUnits, housingDemand = c.HousingDemand, housingReplacementDemand = c.HousingReplacementDemand, housingGrowthDemand = c.HousingGrowthDemand,
                    clothStock = c.Inventory.Cloth, handTools = c.Inventory.HandTools, improvedTools = c.Inventory.Tools, stoneStock = c.Inventory.Stone, clayStock = c.Inventory.Clay, woodStock = c.Inventory.Wood,
                    production, progress = c.People.CraftExperience.ToDictionary(x => x.Key, x => x.Value), unlocked = c.People.NeolithicTechnologies.ToArray(),
                    discardedFood = d.DiscardedInventory, conserved, labor, materialReceipts = r.MaterialTransfers.Count, limitingFactor = d.LimitingFactor };
                trajectories.WriteLine(JsonSerializer.Serialize(last, Json));
            }
        }
        catch (Exception ex) { error = ex.GetType().Name + ": " + ex.Message; }
        finals.WriteLine(JsonSerializer.Serialize(new { run, seed, habitat, policy, variant = variant.Id, family = variant.Family, overrides = variant.Overrides, configurationHash = hash,
            completedYears = w.Days / w.Config.DaysPerYear, population = c.Population, deaths, waste, invariantFailures = failures,
            firstFarmerMajorityYear = majority, gatheringBelowOnePercentYear = gatheringGone, breakthroughYears = unlock, error, last }, Json));
        trajectories.Flush(); finals.Flush();
        Console.WriteLine("RUN " + run + " pop=" + c.Population + " tech=" + string.Join(",", unlock.Keys) + " checks=" + failures + " seconds=" + timer.Elapsed.TotalSeconds.ToString("F2") + (error == null ? "" : " ERROR=" + error));
    }
    // Explicit experimental player policy, not a change to the shipped AI:
    // 20% Farmers, 10% craft slots, remainder gatherers. Within craft slots,
    // assign unlocked Weavers/Builders to actual current needs; generalists fill
    // the remainder. No technology, stocks, materials or food are granted.
    static EconomicIntent ManagedControl(WorldState w, Camp c)
    {
        var e = EconomicIntent.From(c); e.Assignments.Clear(); var n = w.Config.Neolithic;
        int farmers = (int)Math.Floor(c.Population * .2), slots = (int)Math.Floor(c.Population * .1);
        if (farmers > 0) e.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Farmer, Population = farmers });
        void Assign(string role, string tech, double need, double rate)
        {
            if (!c.People.HasTech(tech)) return;
            int workers = Math.Min(slots, (int)Math.Ceiling(Math.Max(0, need) / rate));
            if (workers > 0) { e.Assignments.Add(new PopulationAssignment { Role = role, Population = workers }); slots -= workers; }
        }
        Assign(WorkforceRoles.Weaver, NeolithicTech.Weaving, c.Population * n.FabricConsumptionPerPersonTurn * (1 + n.UsefulFinishedGoodsReserveTurns) - c.Inventory.Cloth, w.Config.GenerationIntervals * n.WeaverFabricOutputPerBaseDays);
        Assign(WorkforceRoles.Builder, NeolithicTech.Construction, c.Population / n.HousingPeoplePerUnit - c.Inventory.HousingUnits * (1 - n.HousingDecayPerTurn), w.Config.GenerationIntervals * n.BuilderHousingOutputPerBaseDays);
        if (slots > 0) e.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Craftsperson, Population = slots });
        return e;
    }
    static void ToolEffects(string output, int seed, SimulationConfig baseConfig)
    {
        using var file = new StreamWriter(Path.Combine(output, "housing-tool-probes-" + seed + ".jsonl"));
        // Supplied-stock conditional probes, not emergent Copper Working claims.
        foreach (string grade in new[] { "hand", "improved", "copper" })
        foreach (double multiplier in grade == "hand" ? new[] { 1d } : grade == "improved" ? new[] { 1.25, 1.5, 1.75 } : new[] { 1.5, 2d, 2.5 })
        {
            var cfg = JsonSerializer.Deserialize<SimulationConfig>(JsonSerializer.Serialize(baseConfig, Json), Json);
            if (grade == "improved") cfg.Neolithic.ImprovedToolsHousingMultiplier = multiplier;
            if (grade == "copper") cfg.Neolithic.CopperToolsHousingMultiplier = multiplier;
            var w = new WorldState { Config = cfg }; var c = new Camp { Population = 1000, Technology = Technology.Cultivation };
            c.People.NeolithicTechnologies.Add(NeolithicTech.Construction);
            if (grade == "hand") c.Inventory.HandTools = 1000;
            if (grade == "improved") c.Inventory.Tools = 1000;
            if (grade == "copper") { c.Inventory.CopperTools = 1000; c.People.NeolithicTechnologies.Add(NeolithicTech.CopperWorking); }
            c.Inventory.Wood = 1000; var r = new TurnRecord(); double coverage = 1000 * cfg.GenerationIntervals;
            var recipe = NeolithicProduction.Recipes(cfg).Single(x => x.Good == CraftGood.Housing);
            double used = NeolithicProduction.Produce(w, c, recipe, WorkforceRoles.Builder, cfg.GenerationIntervals, cfg.GenerationIntervals, 1000, ref coverage, r);
            file.WriteLine(JsonSerializer.Serialize(new { grade, multiplier, outputPerBuilderTurn = c.Inventory.HousingUnits, workerIntervals = used, materialDebit = 1000 - c.Inventory.Wood,
                steadyMaintenanceLaborPct = 100 * cfg.Neolithic.HousingDecayPerTurn / (cfg.Neolithic.HousingPeoplePerUnit * c.Inventory.HousingUnits), conditionalFixture = true }, Json));
        }
    }
}
