using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Anthropocene.Simulation;

// Cost attribution of one player turn on saved snapshots (Docs/PlanningCostAttribution.md).
//
//   --attribution=<save>[,<save>...]  [--reps=N] [--workers=N] [--attribution-output=<file.json>]
//
// Each repetition loads the snapshot fresh and runs the player's path exactly as OpeningGame does: the draft
// (TurnPlanner.Draft for every player except the human, run while the player thinks), then EndTurn (the draft's orders plus
// the human's queued orders, none here; Prepare reusing the draft; Resolve), then the UI refresh of the human's observation.
// The strategy and settlement-economy levers are the snapshot's own, printed as loaded; nothing is overridden.
//
// Two passes per snapshot:
//   - wall: profiler off, the given worker count (default 4, as PhaseExecution.Desktop), mean per phase;
//   - attribution: profiler on, one worker, so every scope nests on one thread and the exclusive times add up to the root
//     scopes' inclusive time with nothing counted twice. Draft work that Prepare reuses is attributed to the draft only.
internal static class Attribution
{
    private static string Option(string[] args, string key, string fallback)
    {
        string prefix = key + "=";
        var match = args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal));
        return match == null ? fallback : match.Substring(prefix.Length);
    }

    private sealed class Turn { public double Draft, Prepare, Resolve, Refresh; public int Settlements; }

    private static Turn RunTurn(byte[] bytes, PhaseExecution execution, out WorldState world)
    {
        world = SaveCodec.Load(bytes);
        var turn = new Turn { Settlements = world.Camps.Count(c => c.Population > 0) };
        var resolver = new TurnResolver(execution);
        var clock = Stopwatch.StartNew();
        var draft = TurnPlanner.Draft(world, 0, execution);
        turn.Draft = clock.Elapsed.TotalMilliseconds; clock.Restart();
        // EndTurn: copy the draft's orders, then the human's queued orders (none) for player 0.
        var ai = new PlanningResult { Traces = new List<DecisionTrace>(draft.Ai.Traces) };
        foreach (var pair in draft.Ai.Orders) ai.Orders[pair.Key] = pair.Value;
        ai.Orders[0] = new List<Order>();
        var prepared = TurnPlanner.Prepare(world, ai.Orders, ai.Traces, execution, draft);
        turn.Prepare = clock.Elapsed.TotalMilliseconds; clock.Restart();
        resolver.Resolve(world, prepared);
        turn.Resolve = clock.Elapsed.TotalMilliseconds; clock.Restart();
        using (PhaseProfile.Scope("Refresh")) new AgentObservationBuilder().Build(world, 0);
        turn.Refresh = clock.Elapsed.TotalMilliseconds;
        return turn;
    }

    // --replay-turns=N: instead of timing, play N turns from each snapshot along the same player path (4 workers) and print
    // the world fingerprint after each turn plus the turn's record fingerprint, so two builds can be compared for exactness.
    private static void Replay(string[] saves, int turns, int workers)
    {
        foreach (string save in saves)
        {
            var world = SaveCodec.Load(File.ReadAllBytes(save)); var execution = new PhaseExecution(workers); var resolver = new TurnResolver(execution);
            for (int i = 0; i < turns; i++)
            {
                var draft = TurnPlanner.Draft(world, 0, execution);
                var ai = new PlanningResult { Traces = new List<DecisionTrace>(draft.Ai.Traces) };
                foreach (var pair in draft.Ai.Orders) ai.Orders[pair.Key] = pair.Value;
                ai.Orders[0] = new List<Order>();
                var prepared = TurnPlanner.Prepare(world, ai.Orders, ai.Traces, execution, draft);
                var record = resolver.Resolve(world, prepared);
                Console.WriteLine($"REPLAY {Path.GetFileName(save)} turn={world.Turn} settlements={world.Camps.Count(c => c.Population > 0)} " +
                    $"accepted={prepared.Orders.Count} rejected={prepared.Rejections.Count} blocked={prepared.Placement.Blocked.Count} trades={record.Trades.Count} " +
                    $"fingerprint={SaveCodec.Fingerprint(world)}");
            }
        }
    }

    // --placement-scaling=1: camp placement at increasing settlement counts on the Standard 1024 x 512 map. Camps at random
    // tiles (2% moving 1-3 hexes per turn's worth of orders), the shipped spacing 3. Times the local enumeration against the
    // all-pairs reference (a copy of the former CampPlacement.Resolve; Tools/Headless/CampSpacingChecks.cs checks equality),
    // and reports pairs examined. Same inputs for both, best of three.
    private static void PlacementScaling()
    {
        Console.WriteLine($"{"camps",7} {"movers",7} {"local ms",9} {"ref ms",9} {"local pairs",12} {"ref pairs",12} {"passes",7} {"same",5}");
        foreach (int n in new[] { 100, 300, 1000, 3000, 10000 })
        {
            var random = new Random(n);
            var w = new WorldState { Config = new SimulationConfig { Width = 1024, Height = 512 } };
            for (int i = 0; i < n; i++) w.Camps.Add(new Camp { Id = i + 1, Tile = random.Next(1024 * 512), Population = 50 });
            var orders = new List<Order>();
            foreach (var c in w.Camps)
                if (random.NextDouble() < .5) { var options = w.Map.Radius(c.Tile, 1 + random.Next(3)).ToArray(); orders.Add(new Order(OrderKind.Move, c.Id, destination: options[random.Next(options.Length)])); }
            double Best(Func<CampPlacement> f, out CampPlacement result) { result = null; double best = double.MaxValue; for (int r = 0; r < 3; r++) { var clock = Stopwatch.StartNew(); result = f(); best = Math.Min(best, clock.Elapsed.TotalMilliseconds); } return best; }
            PhaseProfile.Enabled = true; PhaseProfile.Reset();
            CampPlacement.Resolve(w, orders);
            long localPairs = PhaseProfile.Counters().Where(k => k.Key == "Placement.PairsChecked").Sum(k => k.Value);
            long passes = PhaseProfile.Counters().Where(k => k.Key == "Placement.Passes").Sum(k => k.Value);
            PhaseProfile.Enabled = false;
            double local = Best(() => CampPlacement.Resolve(w, orders), out var a);
            double reference = n <= 3000 ? Best(() => ReferenceResolve(w, orders), out var b) : double.NaN;
            string same = n <= 3000 ? (Key(a) == Key(ReferenceResolve(w, orders)) ? "yes" : "NO") : "-";
            Console.WriteLine($"{n,7} {orders.Count,7} {local,9:F2} {reference,9:F2} {localPairs,12} {(long)n * (n - 1) / 2 * passes,12} {passes,7} {same,5}");
        }
    }
    private static string Key(CampPlacement p) =>
        string.Join(",", p.Destinations.OrderBy(d => d.Key).Select(d => d.Key + ">" + d.Value)) + "|" + string.Join(",", p.Blocked.OrderBy(b => b));
    private static CampPlacement ReferenceResolve(WorldState world, IEnumerable<Order> orders)
    {
        var result = new CampPlacement();
        foreach (var camp in world.Camps.OrderBy(c => c.Id)) result.Destinations[camp.Id] = camp.Tile;
        foreach (var order in orders.Where(o => o.Destination >= 0).OrderBy(o => o.Entity))
            if (result.Destinations.ContainsKey(order.Entity)) result.Destinations[order.Entity] = order.Destination;
        int spacing = world.Config.MinimumCampSpacing;
        var camps = world.Camps.OrderBy(c => c.Id).ToArray();
        bool changed;
        do
        {
            changed = false;
            foreach (var group in world.Camps.GroupBy(c => result.Destinations[c.Id]).OrderBy(g => g.Key).Where(g => g.Count() > 1).ToArray())
            {
                var winner = group.OrderBy(c => c.Tile == group.Key ? 0 : 1).ThenBy(c => c.Id).First();
                foreach (var loser in group.Where(c => c != winner).OrderBy(c => c.Id))
                    if (result.Destinations[loser.Id] != loser.Tile) { result.Destinations[loser.Id] = loser.Tile; result.Blocked.Add(loser.Id); changed = true; }
            }
            if (spacing > 1)
                for (int i = 0; i < camps.Length; i++)
                    for (int j = i + 1; j < camps.Length; j++)
                    {
                        Camp a = camps[i], b = camps[j];
                        int da = result.Destinations[a.Id], db = result.Destinations[b.Id];
                        bool aMoved = da != a.Tile, bMoved = db != b.Tile;
                        if (!aMoved && !bMoved || world.Map.Distance(da, db) >= spacing) continue;
                        var loser = !bMoved ? a : b;
                        result.Destinations[loser.Id] = loser.Tile; result.Blocked.Add(loser.Id); changed = true;
                    }
        } while (changed);
        return result;
    }

    public static void Run(string[] args)
    {
        if (Option(args, "--placement-scaling", "0") != "0") { PlacementScaling(); return; }
        var saves = Option(args, "--attribution", "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        int replayTurns = int.Parse(Option(args, "--replay-turns", "0"), CultureInfo.InvariantCulture);
        if (replayTurns > 0) { Replay(saves, replayTurns, int.Parse(Option(args, "--workers", "4"), CultureInfo.InvariantCulture)); return; }
        int reps = int.Parse(Option(args, "--reps", "3"), CultureInfo.InvariantCulture);
        int workers = int.Parse(Option(args, "--workers", "4"), CultureInfo.InvariantCulture);
        string jsonPath = Option(args, "--attribution-output", "");
        var json = new StringBuilder("[");
        foreach (string save in saves)
        {
            byte[] bytes = File.ReadAllBytes(save);
            var probe = SaveCodec.Load(bytes); var cfg = probe.Config;
            int settlements = probe.Camps.Count(c => c.Population > 0);
            string configuration = $"EconomicAllocation={cfg.EconomicAllocation} Fission={cfg.FissionEnabled} Immigration={cfg.ImmigrationEnabled} Trade={cfg.TradeEnabled} " +
                $"TradeExportLabourShare={cfg.TradeExportLabourShare.ToString(CultureInfo.InvariantCulture)} QoLMortality={cfg.QoLMortalityEnabled} HerdLabour={cfg.HerdLabourEnabled} " +
                $"SharedAllocation={cfg.SharedCropHerdAllocationEnabled} GoodsStorage={cfg.GoodsStorageEnabled} PlannerFulfilment={cfg.Neolithic.PlannerFulfilment}";
            Console.WriteLine($"== {Path.GetFileName(save)}: turn {probe.Turn}, {settlements} settlements, {probe.LivePlayers} live players, {probe.FixedHousing?.Count ?? 0} fixed-housing records");
            Console.WriteLine("   loaded configuration: " + configuration);
            probe = null;

            // Wall pass: profiler off, the desktop worker count.
            PhaseProfile.Enabled = false;
            var wallExecution = new PhaseExecution(workers);
            RunTurn(bytes, wallExecution, out _);   // warm-up
            var wall = Enumerable.Range(0, reps).Select(i => RunTurn(bytes, wallExecution, out _)).ToArray();
            double Mean(Func<Turn, double> f) => wall.Average(f);
            double draftMs = Mean(t => t.Draft), prepareMs = Mean(t => t.Prepare), resolveMs = Mean(t => t.Resolve), refreshMs = Mean(t => t.Refresh);
            double total = draftMs + prepareMs + resolveMs + refreshMs;
            Console.WriteLine($"   wall, {workers} workers, mean of {reps}: draft {draftMs:F0} ms, prepare {prepareMs:F0} ms, resolve {resolveMs:F0} ms, refresh {refreshMs:F0} ms; " +
                $"total {total:F0} ms = {total / settlements:F3} ms per settlement (planning: draft + prepare {(draftMs + prepareMs) / settlements:F3} ms per settlement)");

            // Attribution pass: profiler on, scalar.
            var scalar = new PhaseExecution(1);
            PhaseProfile.Enabled = true; PhaseProfile.Reset();
            RunTurn(bytes, scalar, out _);          // warm-up, discarded
            PhaseProfile.Reset();
            var profiled = Enumerable.Range(0, reps).Select(i => RunTurn(bytes, scalar, out _)).ToArray();
            PhaseProfile.Enabled = false;
            var rows = PhaseProfile.Rows(); var counters = PhaseProfile.Counters();
            double roots = rows.Where(r => !r.Path.Contains("/")).Sum(r => r.InclusiveMs) / reps;
            double exclusiveSum = rows.Sum(r => r.ExclusiveMs) / reps;
            double profiledTotal = profiled.Average(t => t.Draft + t.Prepare + t.Resolve + t.Refresh);
            Console.WriteLine($"   attribution, 1 worker, mean of {reps}: root scopes {roots:F0} ms (exclusive sum {exclusiveSum:F0} ms; harness total {profiledTotal:F0} ms)");
            Console.WriteLine($"   {"scope path",-62} {"incl ms",9} {"excl ms",9} {"calls",9} {"excl ms/setl",12}");
            foreach (var r in rows)
                Console.WriteLine($"   {r.Path,-62} {r.InclusiveMs / reps,9:F2} {r.ExclusiveMs / reps,9:F2} {r.Calls / (double)reps,9:F1} {r.ExclusiveMs / reps / settlements,12:F4}");
            Console.WriteLine("   counters per turn: " + string.Join(", ", counters.Select(c => $"{c.Key}={c.Value / (double)reps:F0}")));
            if (json.Length > 1) json.Append(',');
            json.Append("{\"save\":\"").Append(Path.GetFileName(save)).Append("\",\"settlements\":").Append(settlements)
                .Append(",\"configuration\":\"").Append(configuration).Append("\",\"workers\":").Append(workers).Append(",\"reps\":").Append(reps)
                .Append(",\"wall\":{\"draft\":").Append(Num(draftMs)).Append(",\"prepare\":").Append(Num(prepareMs)).Append(",\"resolve\":").Append(Num(resolveMs)).Append(",\"refresh\":").Append(Num(refreshMs)).Append('}')
                .Append(",\"scopes\":[").Append(string.Join(",", rows.Select(r => $"{{\"path\":\"{r.Path}\",\"inclusive\":{Num(r.InclusiveMs / reps)},\"exclusive\":{Num(r.ExclusiveMs / reps)},\"calls\":{Num(r.Calls / (double)reps)}}}")))
                .Append("],\"counters\":{").Append(string.Join(",", counters.Select(c => $"\"{c.Key}\":{Num(c.Value / (double)reps)}"))).Append("}}");
        }
        json.Append(']');
        if (jsonPath.Length > 0) File.WriteAllText(jsonPath, json.ToString());
    }

    private static string Num(double v) => v.ToString("R", CultureInfo.InvariantCulture);
}
