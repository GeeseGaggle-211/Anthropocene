using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using Anthropocene.Simulation;

internal static class AutonomousElderChecks
{
    private static int checks;
    private static void Check(bool x, string message) { checks++; if (!x) throw new Exception("Autonomous: " + message); }
    private static void Near(double a, double b, string message) => Check(Math.Abs(a - b) < 1e-7 * Math.Max(1, Math.Abs(b)), message + $" ({a} vs {b})");
    internal static WorldState Fixture(bool two = false)
    {
        var w = EconomicSettlementChecks.DiagnosticProductiveVillage(0);
        w.Config.TradeEnabled = two; w.Config.TradeFoodEnabled = true;
        var a = w.Camps[0];
        a.PlantFood = 100000; a.Inventory.Pots = 10000;
        foreach (string tech in new[] { NeolithicTech.Toolmaking, NeolithicTech.Pottery, NeolithicTech.Weaving, NeolithicTech.Carpentry }) a.People.NeolithicTechnologies.Add(tech);
        if (two)
        {
            var b = new Camp { Id = w.NextId++, Player = 1, Tile = w.Map.At(10, 7), Population = 120, PlantFood = 100000, IsVillage = true, EconomyAutomationTurns = -1 };
            w.Camps.Add(b); w.PlayerCount = w.Config.Players = 2; Ownership.Bind(w);
            b.Technology = Technology.Cultivation; b.Technologies[(int)Technology.Cultivation] = true;
            b.People.Progression.Era = Era.Neolithic;
            b.People.NeolithicTechnologies = new List<string>(a.People.NeolithicTechnologies);
            b.Inventory.Clay = 1000; b.Inventory.Stone = 1000; b.Inventory.Wood = 1000; b.Inventory.HandTools = 240; b.Inventory.Pots = 10000;
            ContactKnowledge.MemoryOf(w, a.People).Upsert(b.Id, b.Player, b.Tile, w.Turn, ContactSource.Sighting);
            ContactKnowledge.MemoryOf(w, b.People).Upsert(a.Id, a.Player, a.Tile, w.Turn, ContactSource.Sighting);
        }
        return w;
    }
    private static AutonomousElderAction Policy(AutonomousElderObservation o, string role, CraftGood? recipe = null)
    {
        var a = AutonomousElderCompiler.Uniform(o);
        Array.Clear(a.Workforce, 0, a.Workforce.Length); a.Workforce[Array.IndexOf(WorkforceRoles.All, role)] = 1;
        if (recipe.HasValue)
        {
            int r = Array.IndexOf(WorkforceRoles.All, role); Array.Clear(a.Activities[r], 0, a.Activities[r].Length); a.Activities[r][4 + (int)recipe.Value] = 1;
        }
        if (role == WorkforceRoles.Gatherer) { Array.Clear(a.Activities[0], 0, a.Activities[0].Length); a.Activities[0][0] = 1; }
        return a;
    }
    private static (WorldState world, AutonomousElderEnvironment env, AutonomousElderStep step) Run(WorldState w, string role, CraftGood? recipe = null)
    {
        var env = new AutonomousElderEnvironment(); var o = env.Reset(SaveCodec.Save(w), new[] { w.Camps[0].Id });
        var step = env.Step(o.ToDictionary(x => x.Key, x => Policy(x.Value, role, recipe)));
        return (SaveCodec.Load(env.CaptureSnapshot()), env, step);
    }
    private static double Output(WorldState w, CraftGood good) => w.History.Last().Production.Where(p => p.Good == good).Sum(p => p.Output);
    public static int Run()
    {
        checks = 0;
        Check(AutonomousElderCompiler.Apportion(new[] { .5, .5, 0 }, 3).SequenceEqual(new[] { 2, 1, 0 }), "stable largest-remainder ties");
        Check(AutonomousElderCompiler.Apportion(new[] { .1, .2, .7 }, 137).Sum() == 137, "exact workforce conservation");
        var w = Fixture(); var c = w.Camps[0]; var o = AutonomousElderCompiler.Observe(w, c);
        var action = Policy(o, WorkforceRoles.Potter, CraftGood.Pots);
        action.Workforce[0] = double.NaN;
        bool rejected = false; try { AutonomousElderCompiler.Validate(action, o); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "nonfinite action rejected");
        Check(!o.WorkforceMask[3], "Fishing structurally absent");
        var noStock = SaveCodec.Load(SaveCodec.Save(w)); noStock.Camps[0].Inventory.Clay = 0; noStock.Camps[0].Inventory.HandTools = 0;
        Check(AutonomousElderCompiler.Observe(noStock, noStock.Camps[0]).WorkforceMask.SequenceEqual(o.WorkforceMask), "shortages do not become economic masks");
        var idle = Run(w, WorkforceRoles.Farmer);
        var pots = Run(w, WorkforceRoles.Potter, CraftGood.Pots);
        Check(Output(pots.world, CraftGood.Pots) > Output(idle.world, CraftGood.Pots), "staffing changes actual native output");
        Check(Output(pots.world, CraftGood.Pots) > 0, "production above local use demand (10,000 opening Pots)");
        var empty = Run(noStock, WorkforceRoles.Potter, CraftGood.Pots);
        Near(Output(empty.world, CraftGood.Pots), 0, "missing inputs and tools cause idle labor");
        var noMaterials = Fixture(); noMaterials.Camps[0].Inventory.Clay = 0;
        Near(Output(Run(noMaterials, WorkforceRoles.Potter, CraftGood.Pots).world, CraftGood.Pots), 0, "clay shortage blocks output even with tools and workforce");
        var noTools = Fixture(); noTools.Camps[0].Inventory.HandTools = noTools.Camps[0].Inventory.Tools = noTools.Camps[0].Inventory.CopperTools = 0;
        Near(Output(Run(noTools, WorkforceRoles.Potter, CraftGood.Pots).world, CraftGood.Pots), 0, "no tools blocks equipped recipe");
        Near(Output(Run(noTools, WorkforceRoles.Builder, CraftGood.HousingMaterial).world, CraftGood.HousingMaterial), 0, "Housing Material retains tool coverage gate");
        var limitedTools = Fixture(); limitedTools.Camps[0].Inventory.HandTools = 1;
        var limitedMaterial = Run(limitedTools, WorkforceRoles.Builder, CraftGood.HousingMaterial).world;
        Check(Output(limitedMaterial, CraftGood.HousingMaterial) <= ToolCoverageBudget.OutputsPerHandTool + 1e-8, "Housing Material output bounded by one Hand Tool");
        Check(limitedMaterial.History.Last().Production.All(p => p.Output <= 0 || p.WorkerIntervals > 0), "positive output requires positive worker-time");
        var low = Fixture(); low.Camps[0].Population = 1; low.Camps[0].Workforce.Assignments.Clear();
        Check(Output(Run(low, WorkforceRoles.Potter, CraftGood.Pots).world, CraftGood.Pots) < Output(pots.world, CraftGood.Pots), "workforce scarcity limits output");
        var hungry = Fixture(); hungry.Camps[0].PlantFood = hungry.Camps[0].AnimalFood = 0;
        var starvation = Run(hungry, WorkforceRoles.Potter, CraftGood.Pots).world;
        Check(starvation.Camps[0].FoodNeedsMetRatio < 1 || starvation.Camps[0].Population < hungry.Camps[0].Population, "no food workforce causes real hunger");
        var cloth = Run(w, WorkforceRoles.Weaver, CraftGood.Cloth);
        Check(Output(cloth.world, CraftGood.Cloth) > 0 && Output(cloth.world, CraftGood.AnimalFabric) == 0, "conditional recipe allocation");
        AutonomousElderEnvironment.VerifyReplay(pots.env.Replay, new PhaseExecution(2, 1, true, 1)); checks++;
        Check(SaveCodec.Fingerprint(SaveCodec.Load(pots.env.CaptureSnapshot())) == pots.step.Fingerprint, "exact save/load endpoint");
        MovementAndEscrow();
        MaterialsAndDependencies();
        TradeAndWego();
        RandomizedLaborAndReplay();
        Console.WriteLine("PASS: " + checks + " autonomous Elder assertions");
        Console.WriteLine(JsonSerializer.Serialize(new { potsOutput = Output(pots.world, CraftGood.Pots), idlePotsOutput = Output(idle.world, CraftGood.Pots), clothOutput = Output(cloth.world, CraftGood.Cloth), hungryFoodRatio = starvation.Camps[0].FoodNeedsMetRatio }));
        return checks;
    }
    private static void MovementAndEscrow()
    {
        var w = Fixture(true); var ids = w.Camps.Select(c => c.Id).ToArray();
        foreach (var c in w.Camps) foreach (int tile in w.Map.Radius(c.Tile, w.Config.VisionRadius)) c.People.Memory.Explored.Add(tile, w.Turn);
        var env = new AutonomousElderEnvironment(); var obs = env.Reset(SaveCodec.Save(w), ids);
        int movement = Enumerable.Range(1, 7).First(i => obs[ids[0]].MovementMask[i]);
        var a = Policy(obs[ids[0]], WorkforceRoles.Farmer); var b = Policy(obs[ids[1]], WorkforceRoles.Farmer);
        a.Movement = movement;
        a.Quote = new ElderStockQuote { Counterparty = 0, Give = TradeGood.Wood, Want = TradeGood.Clay, GiveQuantity = 10, WantQuantity = 20 };
        b.Quote = new ElderStockQuote { Counterparty = 0, Give = TradeGood.Clay, Want = TradeGood.Wood, GiveQuantity = 20, WantQuantity = 10 };
        var result = env.Step(new Dictionary<int, AutonomousElderAction> { [ids[1]] = b, [ids[0]] = a });
        var end = SaveCodec.Load(env.CaptureSnapshot());
        Check(end.Camps.Single(c => c.Id == ids[0]).Tile != w.Camps[0].Tile, "masked movement reaches native placement");
        Check(result.Telemetry.Moved[ids[0]] == 1, "movement telemetry matches physical movement");
        Check(result.Telemetry.SettledTrades[ids[0]] == 1, "opening-stock quote settles with simultaneous movement");
        Check(end.History.Last().Trades.Count == 1, "native settlement receipt remains authoritative");
        Near(end.Camps.Sum(c => c.Inventory.Wood), w.Camps.Sum(c => c.Inventory.Wood), "moving trade conserves wood");
        Near(end.Camps.Sum(c => c.Inventory.Clay), w.Camps.Sum(c => c.Inventory.Clay), "moving trade conserves clay");
        AutonomousElderEnvironment.VerifyReplay(env.Replay, new PhaseExecution(2, 1, true, 1)); checks++;
        a.Movement = 8; bool invalid = false;
        try { AutonomousElderCompiler.Validate(a, obs[ids[0]]); } catch (ArgumentException) { invalid = true; }
        Check(invalid, "out-of-range movement rejected");
        var copy = obs[ids[0]].MovementMask.ToArray(); obs[ids[0]].MovementMask[0] = false;
        Check(copy[0], "stay is always legal");
    }
    private static void MaterialsAndDependencies()
    {
        var w = Fixture(true); int count = w.Tiles.Length;
        w.Resources = new WorldResourceState { MaximumQuantity = 100000, CatalogHash = ResourceCatalog.Hash, ProfileHash = ResourceCatalog.Hash,
            ConfigurationHash = ResourceCatalog.Hash, Provinces = Array.Empty<GeologicalProvince>(), Province = new ushort[count], History = new ushort[count],
            Host = new byte[count], Marine = new byte[count], Stone = new ushort[count], Clay = new ushort[count], Wood = new ushort[count] };
        foreach (var c in w.Camps) { w.Resources.Stone[c.Tile] = 100; w.Resources.Clay[c.Tile] = 100; c.Inventory.Stone = c.Inventory.Clay = 0; }
        var ids = w.Camps.Select(c => c.Id).ToArray(); var env = new AutonomousElderEnvironment(); var obs = env.Reset(SaveCodec.Save(w), ids);
        var result = env.Step(new Dictionary<int, AutonomousElderAction> { [ids[0]] = Policy(obs[ids[0]], WorkforceRoles.Stonecutter), [ids[1]] = Policy(obs[ids[1]], WorkforceRoles.ClayMiner) });
        var end = SaveCodec.Load(env.CaptureSnapshot());
        Check(end.Camps[0].Inventory.Stone > 0 && end.Camps[1].Inventory.Clay > 0, "surplus extraction accumulates with no local craft demand");
        foreach (var receipt in end.History.Last().MaterialTransfers)
            Near(receipt.Before - receipt.After, receipt.Amount, "finite source debit equals material receipt");
        double stone = end.Camps.Sum(c => c.Inventory.Stone), clay = end.Camps.Sum(c => c.Inventory.Clay);
        var a = Policy(result.Observations[ids[0]], WorkforceRoles.Farmer); var b = Policy(result.Observations[ids[1]], WorkforceRoles.Farmer);
        a.Quote = new ElderStockQuote { Counterparty = 0, Give = TradeGood.Stone, Want = TradeGood.Clay, GiveQuantity = 10, WantQuantity = 10 };
        b.Quote = new ElderStockQuote { Counterparty = 0, Give = TradeGood.Clay, Want = TradeGood.Stone, GiveQuantity = 10, WantQuantity = 10 };
        env.Step(new Dictionary<int, AutonomousElderAction> { [ids[0]] = a, [ids[1]] = b });
        var traded = SaveCodec.Load(env.CaptureSnapshot());
        Near(traded.Camps.Sum(c => c.Inventory.Stone), stone, "produced surplus trades next boundary with stone conservation");
        Near(traded.Camps.Sum(c => c.Inventory.Clay), clay, "produced surplus trades next boundary with clay conservation");
        Check(traded.History.Last().Trades.Count == 1, "surplus production-to-trade vertical slice");
        AutonomousElderEnvironment.VerifyReplay(env.Replay); checks++;
        // Dependent recipe budgets do not conjure their feedstock.
        var housing = Fixture(); housing.Camps[0].Inventory.HousingMaterial = 0;
        Near(Output(Run(housing, WorkforceRoles.Builder, CraftGood.Housing).world, CraftGood.Housing), 0, "assembly-only without Housing Material is idle");
        var henv = new AutonomousElderEnvironment(); var ho = henv.Reset(SaveCodec.Save(housing), new[] { housing.Camps[0].Id });
        var ha = Policy(ho.First().Value, WorkforceRoles.Builder, CraftGood.Housing);
        int builder = Array.IndexOf(WorkforceRoles.All, WorkforceRoles.Builder);
        ha.Activities[builder][4 + (int)CraftGood.Housing] = .5; ha.Activities[builder][4 + (int)CraftGood.HousingMaterial] = .5;
        henv.Step(new Dictionary<int, AutonomousElderAction> { [housing.Camps[0].Id] = ha });
        var built = SaveCodec.Load(henv.CaptureSnapshot());
        Check(Output(built, CraftGood.HousingMaterial) > 0 && Output(built, CraftGood.Housing) > 0, "dependency order permits material then housing assembly");
        foreach (var group in built.History.Last().Production.GroupBy(p => p.Role))
            Check(group.Sum(p => p.WorkerIntervals) <= 120 * housing.Config.TurnDays / housing.Config.BaseDays + 1e-7, "shared profession worker-time conserved");
        var storage = Fixture(); storage.Config.GoodsStorageEnabled = true; storage.Config.GoodsBaseStoragePerResident = 0;
        storage.Camps[0].Inventory.Pots = 0; storage.Camps[0].Inventory.Cloth = 100000;
        Near(Output(Run(storage, WorkforceRoles.Weaver, CraftGood.Cloth).world, CraftGood.Cloth), 0, "full native storage blocks surplus cloth");
    }
    private static void TradeAndWego()
    {
        var w = Fixture(true); var ids = w.Camps.Select(c => c.Id).ToArray();
        var obs = w.Camps.ToDictionary(c => c.Id, c => AutonomousElderCompiler.Observe(w, c));
        var actions = obs.ToDictionary(x => x.Key, x => Policy(x.Value, WorkforceRoles.Farmer));
        actions[ids[0]].Quote = new ElderStockQuote { Counterparty = 0, Give = TradeGood.Wood, Want = TradeGood.Clay, GiveQuantity = 10, WantQuantity = 20 };
        actions[ids[1]].Quote = new ElderStockQuote { Counterparty = 0, Give = TradeGood.Clay, Want = TradeGood.Wood, GiveQuantity = 20, WantQuantity = 10 };
        foreach (var a in actions) AutonomousElderCompiler.Validate(a.Value, obs[a.Key]);
        var invalidQuote = actions[ids[0]].Copy(); invalidQuote.Quote.GiveQuantity = 1e-300;
        bool tinyRejected = false; try { AutonomousElderCompiler.Validate(invalidQuote, obs[ids[0]]); } catch (ArgumentException) { tinyRejected = true; }
        Check(tinyRejected, "bounded quote ratios cannot overflow or underflow");
        var plan = AutonomousElderTrade.Plan(w, actions, obs);
        Check(plan.Agreements.Count == 1 && plan.Agreements[0].AllStock, "compatible reciprocal quotes match");
        double wood = w.Camps.Sum(c => c.Inventory.Wood), clay = w.Camps.Sum(c => c.Inventory.Clay);
        Trade.Settle(w, plan, new TurnRecord());
        Near(w.Camps.Sum(c => c.Inventory.Wood), wood, "wood conservation through authoritative escrow");
        Near(w.Camps.Sum(c => c.Inventory.Clay), clay, "clay conservation through authoritative escrow");
        Near(w.Camps[0].Inventory.Wood, 990, "exact quote delivery");
        Near(w.Camps[0].Inventory.Clay, 1020, "exact reciprocal receipt");
        actions[ids[1]].Quote.WantQuantity = 11;
        Check(AutonomousElderTrade.Plan(w, actions, obs).Agreements.Count == 0, "incompatible exchange condition fails");
        actions[ids[1]].Quote.WantQuantity = 10;
        int oldTile = w.Camps[1].Tile; w.Camps[1].Tile = w.Map.At(10, 8);
        Check(AutonomousElderTrade.Plan(w, actions, obs).Agreements.Count == 0, "stale relocated remembered contact cannot settle");
        w.Camps[1].Tile = oldTile;
        var env = new AutonomousElderEnvironment(); var initial = SaveCodec.Save(w); env.Reset(initial, ids);
        string fingerprint = SaveCodec.Fingerprint(w); bool rejected = false;
        try { env.Step(new Dictionary<int, AutonomousElderAction> { [ids[0]] = actions[ids[0]] }); } catch (ArgumentException) { rejected = true; }
        Check(rejected && SaveCodec.Fingerprint(SaveCodec.Load(env.CaptureSnapshot())) == fingerprint, "missing simultaneous action rejects without mutation");
        var step = env.Step(actions);
        AutonomousElderEnvironment.VerifyReplay(env.Replay); checks++;
        var json = new JsonSerializerOptions { IncludeFields = true };
        var persisted = JsonSerializer.Deserialize<AutonomousElderReplay>(JsonSerializer.Serialize(env.Replay, json), json);
        AutonomousElderEnvironment.VerifyReplay(persisted); checks++;
        var reversed = new AutonomousElderEnvironment(new PhaseExecution(2, 1, true, 1)); reversed.Reset(initial, ids.Reverse().ToArray());
        var other = reversed.Step(actions.Reverse().ToDictionary(x => x.Key, x => x.Value));
        Check(other.Fingerprint == step.Fingerprint, "submission order and worker partition invariance");
        var hidden = Fixture(true); var before = AutonomousElderCompiler.Observe(hidden, hidden.Camps[0]);
        hidden.Camps[1].Inventory.Clay += 12345;
        var after = AutonomousElderCompiler.Observe(hidden, hidden.Camps[0]);
        Check(before.Context.SequenceEqual(after.Context) && before.Stocks.SequenceEqual(after.Stocks), "remote inventory hidden from actor");
    }
    private static void RandomizedLaborAndReplay()
    {
        // Independent review probe promoted into the regression suite.
        var random = new Random(1729);
        for (int trial = 0; trial < 30; trial++)
        {
            var w = Fixture(); var c = w.Camps[0]; var env = new AutonomousElderEnvironment();
            var obs = env.Reset(SaveCodec.Save(w), new[] { c.Id });
            var action = AutonomousElderCompiler.Uniform(obs[c.Id]);
            void Randomize(double[] row)
            {
                for (int i = 0; i < row.Length; i++) if (row[i] > 0) row[i] = random.NextDouble();
                double sum = row.Sum(); if (sum > 0) for (int i = 0; i < row.Length; i++) row[i] /= sum;
            }
            Randomize(action.Workforce); foreach (var row in action.Activities) Randomize(row);
            action.FieldExpansionFraction = random.NextDouble();
            var step = env.Step(new Dictionary<int, AutonomousElderAction> { [c.Id] = action });
            var record = SaveCodec.Load(env.CaptureSnapshot()).History.Last();
            for (int r = 0; r < action.Workforce.Length; r++)
            {
                string role = WorkforceRoles.All[r];
                double spent = record.Production.Where(p => p.Role == role).Sum(p => p.WorkerIntervals) +
                    record.MaterialTransfers.Where(p => p.Role == role).Sum(p => p.WorkerIntervals);
                Check(spent <= step.Staffing[c.Id][r] * record.Duration / w.Config.BaseDays + 1e-7, "randomized native role labor budget");
            }
            AutonomousElderEnvironment.VerifyReplay(env.Replay); checks++;
        }
    }
    private static WorldState BenchmarkWorld(int count)
    {
        if (count <= 2) return Fixture(count == 2);
        var w = Fixture(); var template = w.Tiles[0];
        w.Config.Width = 48; w.Config.Height = 40; w.Config.BenchmarkScale = true; w.Tiles = new Tile[w.Config.Width * w.Config.Height];
        for (int i = 0; i < w.Tiles.Length; i++)
            w.Tiles[i] = new Tile { Id = i, ElevationLevel = template.ElevationLevel, Terrain = template.Terrain,
                FreshwaterSource = template.FreshwaterSource, Freshwater = template.Freshwater, Food = template.Food,
                PotentialFood = template.PotentialFood, LastEcologyUpdateDays = w.Days, Seasons = (SeasonData[])template.Seasons.Clone() };
        var first = w.Camps[0]; first.Tile = w.Map.At(3, 3);
        for (int i = 1; i < count; i++)
            w.Camps.Add(new Camp { Id = w.NextId++, Player = i, Tile = w.Map.At(3 + i % 10 * 4, 3 + i / 10 * 4), Population = 120,
                PlantFood = 100000, IsVillage = true, EconomyAutomationTurns = -1 });
        w.PlayerCount = w.Config.Players = count; w.Config.TradeEnabled = true; Ownership.Bind(w);
        foreach (var c in w.Camps)
        {
            c.Technology = Technology.Cultivation; c.Technologies[(int)Technology.Cultivation] = true; c.People.Progression.Era = Era.Neolithic;
            c.People.NeolithicTechnologies = new List<string>(first.People.NeolithicTechnologies);
            c.Inventory.Wood = c.Inventory.Stone = c.Inventory.Clay = 1000; c.Inventory.HandTools = 240; c.Inventory.Pots = 10000;
            foreach (int tile in w.Map.Radius(c.Tile, 1)) { w.Tiles[tile].CultivationOperator = c.Id; w.Tiles[tile].CultivationCapital = .16; }
            foreach (var other in w.Camps.Where(o => o.Id != c.Id && w.Map.Distance(c.Tile, o.Tile) <= w.Config.TradeRange))
                ContactKnowledge.MemoryOf(w, c.People).Upsert(other.Id, other.Player, other.Tile, w.Turn, ContactSource.Sighting);
        }
        return w;
    }
    public static void Benchmark()
    {
        var samples = new List<object>();
        foreach (int camps in new[] { 1, 2, 8, 32, 80 }) foreach (bool autonomous in new[] { false, true })
        {
            var ms = new List<double>(); var bytes = new List<long>();
            for (int run = 0; run < 9; run++)
            {
                var w = BenchmarkWorld(camps); var snapshot = SaveCodec.Save(w);
                var ids = w.Camps.Select(c => c.Id).ToArray();
                var env = new AutonomousElderEnvironment(); var obs = env.Reset(snapshot, ids);
                var actions = obs.ToDictionary(x => x.Key, x => Policy(x.Value, WorkforceRoles.Potter, CraftGood.Pots));
                long startBytes = GC.GetTotalAllocatedBytes(true); var timer = Stopwatch.StartNew();
                if (autonomous) env.Step(actions);
                else
                {
                    var orders = AiPlanning.Decide(w).Orders;
                    new TurnResolver().Resolve(w, TurnPlanner.Prepare(w, orders));
                    foreach (var c in w.Camps.Where(c => ids.Contains(c.Id))) AutonomousElderCompiler.Observe(w, c);
                    SaveCodec.Fingerprint(w);
                }
                timer.Stop();
                if (run > 1) { ms.Add(timer.Elapsed.TotalMilliseconds); bytes.Add(GC.GetTotalAllocatedBytes(false) - startBytes); }
            }
            samples.Add(new { camps, mode = autonomous ? "autonomous" : "heuristic", samples = ms.Count, medianMs = ms.OrderBy(x => x).ElementAt(ms.Count / 2), minMs = ms.Min(), maxMs = ms.Max(), meanAllocatedBytes = bytes.Average() });
        }
        Console.WriteLine(JsonSerializer.Serialize(samples));
    }
}
