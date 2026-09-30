using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using Anthropocene.Simulation;

// Minimum camp spacing (WP10 step 1): CampPlacement.Resolve keeps every pair of resolved camp
// destinations at least MinimumCampSpacing apart, and order masks/AI never offer a destination
// within spacing of an observed camp. Owns its worlds and counters (lanes run checks concurrently).
internal static class CampSpacingChecks
{
    private static int checks;
    private static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception("Camp spacing: " + message); }

    private static Camp AddCamp(WorldState w, int player, int tile)
    {
        var c = new Camp { Id = w.NextId++, Player = player, Tile = tile, Population = 100, PlantFood = 12000 };
        w.Camps.Add(c); return c;
    }
    // Camps at (q, 5) of a 24x16 wrapped fixture; the first camp belongs to player 0.
    private static WorldState World(int[] players, params int[] columns)
    {
        var w = NeolithicChecks.Fixture(false); w.Camps.Clear();
        for (int i = 0; i < columns.Length; i++) AddCamp(w, players[i], w.Map.At(columns[i], 5));
        w.Config.Players = Math.Max(1, players.Max() + 1);
        Ownership.Bind(w); return w;
    }
    private static int At(WorldState w, int q) => w.Map.At(q, 5);
    private static int Shift(WorldState w, int tile, int dq) => w.Map.At(tile % w.Map.Width + dq, tile / w.Map.Width);
    private static Order Move(WorldState w, Camp c, int q) => new Order(OrderKind.Move, c.Id, destination: At(w, q));

    private static string Key(CampPlacement p) =>
        string.Join(",", p.Destinations.OrderBy(d => d.Key).Select(d => d.Key + ">" + d.Value)) + "|" + string.Join(",", p.Blocked.OrderBy(b => b));

    public static int Run()
    {
        checks = 0; Config(); Pairs(); Chains(); Wrapping(); OrderIndependence(); Masks(); Hidden(); Policy(); Replay();
        Console.WriteLine("PASS: " + checks + " camp spacing assertions"); return checks;
    }

    private static void Config()
    {
        var config = new SimulationConfig();
        Check(config.MinimumCampSpacing == 3, "default spacing is three hexes");
        config.MinimumCampSpacing = 0;
        bool rejected = false; try { config.Validate(); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "spacing below one hex is rejected");
        config.MinimumCampSpacing = 1; config.Validate(); Check(true, "spacing one is valid");
        // A serialized configuration without the field (an older save) loads with the default.
        var serializer = new DataContractSerializer(typeof(SimulationConfig));
        string xml;
        using (var stream = new MemoryStream()) { serializer.WriteObject(stream, new SimulationConfig { MinimumCampSpacing = 5 }); xml = Encoding.UTF8.GetString(stream.ToArray()); }
        Check(xml.Contains("<MinimumCampSpacing>5</MinimumCampSpacing>"), "spacing is serialized");
        string legacy = System.Text.RegularExpressions.Regex.Replace(xml, "<MinimumCampSpacing>5</MinimumCampSpacing>", "");
        Check(legacy != xml, "legacy fixture omits spacing");
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(legacy))) Check(((SimulationConfig)serializer.ReadObject(stream)).MinimumCampSpacing == 3, "older saves load the default spacing");
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml))) Check(((SimulationConfig)serializer.ReadObject(stream)).MinimumCampSpacing == 5, "a saved spacing round trips");
        var w = World(new[] { 0 }, 5); w.Config.MinimumCampSpacing = 4;
        Check(SaveCodec.Load(SaveCodec.Save(w)).Config.MinimumCampSpacing == 4, "spacing persists through save and load");
    }

    private static void Pairs()
    {
        // Distance exactly three is allowed; two is rejected.
        var w = World(new[] { 0, 1 }, 5, 10); var a = w.Camps[0]; var b = w.Camps[1];
        var p = CampPlacement.Resolve(w, new[] { Move(w, a, 7) });
        Check(p.Destinations[a.Id] == At(w, 7) && p.Blocked.Count == 0, "a move ending exactly three hexes from a stationary camp is allowed");
        w = World(new[] { 0, 1 }, 5, 9); a = w.Camps[0]; b = w.Camps[1];
        p = CampPlacement.Resolve(w, new[] { Move(w, a, 7) });
        Check(p.Destinations[a.Id] == a.Tile && p.Blocked.SetEquals(new[] { a.Id }), "a mover is blocked two hexes from a stationary camp");
        Check(p.Destinations[b.Id] == b.Tile, "the stationary camp is unaffected");
        // Staying is always allowed, including a pre-existing sub-spacing pair.
        w = World(new[] { 0, 1 }, 5, 7); a = w.Camps[0];
        p = CampPlacement.Resolve(w, new Order[0]);
        Check(p.Blocked.Count == 0 && p.Destinations.All(d => w.Camps.Single(c => c.Id == d.Key).Tile == d.Value), "a pre-existing close pair may stay");
        p = CampPlacement.Resolve(w, new[] { Move(w, a, 4) });
        Check(p.Blocked.Count == 0 && p.Destinations[a.Id] == At(w, 4), "a camp of a close pair may move away to spacing");
        p = CampPlacement.Resolve(w, new[] { Move(w, a, 5) });
        Check(p.Blocked.Count == 0, "a move onto its own tile is a stay");
        // Two movers two hexes apart: the lower ID keeps its move.
        w = World(new[] { 0, 1 }, 5, 10); a = w.Camps[0]; b = w.Camps[1];
        p = CampPlacement.Resolve(w, new[] { Move(w, a, 6), Move(w, b, 8) });
        Check(p.Destinations[a.Id] == At(w, 6) && p.Destinations[b.Id] == b.Tile && p.Blocked.SetEquals(new[] { b.Id }), "of two movers ending two hexes apart the lower ID keeps its move");
        // The spacing is configurable; spacing one only forbids shared tiles.
        w.Config.MinimumCampSpacing = 1;
        p = CampPlacement.Resolve(w, new[] { Move(w, a, 6), Move(w, b, 8) });
        Check(p.Blocked.Count == 0, "spacing one permits neighbouring destinations");
        w.Config.MinimumCampSpacing = 5;
        p = CampPlacement.Resolve(w, new[] { Move(w, a, 6), Move(w, b, 11) });
        Check(p.Blocked.Count == 0, "five hexes apart satisfies spacing five");
        p = CampPlacement.Resolve(w, new[] { Move(w, a, 6), Move(w, b, 10) });
        Check(p.Blocked.SetEquals(new[] { a.Id }), "four hexes from a stationary camp violates spacing five");
    }

    private static void Chains()
    {
        // A stays at 10; B (7 -> 8) is blocked two from A; B's rollback then leaves C (4 -> 5) two from B, so C is blocked too.
        var w = World(new[] { 0, 1, 2 }, 10, 7, 4); var a = w.Camps[0]; var b = w.Camps[1]; var c = w.Camps[2];
        var p = CampPlacement.Resolve(w, new[] { Move(w, b, 8), Move(w, c, 5) });
        Check(p.Destinations[a.Id] == a.Tile && p.Destinations[b.Id] == b.Tile && p.Destinations[c.Id] == c.Tile && p.Blocked.SetEquals(new[] { b.Id, c.Id }), "a rollback propagates to the next mover to a fixed point");
        // With B unblocked the chain does not start: C at 5 is three from B at 8.
        w = World(new[] { 0, 1, 2 }, 11, 7, 4); a = w.Camps[0]; b = w.Camps[1]; c = w.Camps[2];
        p = CampPlacement.Resolve(w, new[] { Move(w, b, 8), Move(w, c, 5) });
        Check(p.Blocked.Count == 0, "moves at exactly spacing from each other all stand");
        // Result obeys the invariant: every pair that involves a mover satisfies spacing.
        Invariant(w, p);
    }

    private static void Invariant(WorldState w, CampPlacement p)
    {
        foreach (var x in w.Camps) foreach (var y in w.Camps)
            if (x.Id < y.Id && (p.Destinations[x.Id] != x.Tile || p.Destinations[y.Id] != y.Tile))
                Check(w.Map.Distance(p.Destinations[x.Id], p.Destinations[y.Id]) >= w.Config.MinimumCampSpacing, "resolved destinations honour spacing");
    }

    private static void Wrapping()
    {
        // Width 24: columns 22 and 1 are three apart across the seam, 22 and 0 are two apart.
        var w = World(new[] { 0, 1 }, 22, 2); var a = w.Camps[0]; var b = w.Camps[1];
        Check(w.Map.Distance(At(w, 22), At(w, 1)) == 3 && w.Map.Distance(At(w, 22), At(w, 0)) == 2, "fixture seam distances");
        var p = CampPlacement.Resolve(w, new[] { Move(w, b, 1) });
        Check(p.Blocked.Count == 0 && p.Destinations[b.Id] == At(w, 1), "three hexes across the longitude seam is allowed");
        w = World(new[] { 0, 1 }, 22, 1); a = w.Camps[0]; b = w.Camps[1];
        p = CampPlacement.Resolve(w, new[] { Move(w, b, 0) });
        Check(p.Blocked.SetEquals(new[] { b.Id }) && p.Destinations[b.Id] == b.Tile, "two hexes across the longitude seam is rejected");
        p = CampPlacement.Resolve(w, new[] { Move(w, a, 23) });
        Check(p.Blocked.SetEquals(new[] { a.Id }), "a mover toward a camp across the seam is blocked");
    }

    private static void OrderIndependence()
    {
        var baseline = World(new[] { 0, 1, 2, 3 }, 3, 7, 11, 14);
        var camps = baseline.Camps;
        var orders = new List<Order> { Move(baseline, camps[0], 4), Move(baseline, camps[1], 6), Move(baseline, camps[2], 10), Move(baseline, camps[3], 13) };
        string reference = Key(CampPlacement.Resolve(baseline, orders));
        Check(reference.Contains("|"), "order independence fixture resolves");
        var rng = new Random(7);
        for (int i = 0; i < 24; i++)
        {
            var w = World(new[] { 0, 1, 2, 3 }, 3, 7, 11, 14);
            var shuffledOrders = orders.OrderBy(_ => rng.Next()).ToList();
            w.Camps.Sort((x, y) => rng.Next(3) - 1);
            // Sort with a random comparer may leave any order; also force reversal on odd runs.
            if (i % 2 == 1) w.Camps.Reverse();
            Check(Key(CampPlacement.Resolve(w, shuffledOrders)) == reference, "collection order does not change the placement (run " + i + ")");
        }
        Check(CampPlacement.Resolve(baseline, orders).Blocked.Count >= 1, "the order fixture contains at least one rollback");
    }

    private static AgentObservation Observe(WorldState w, int player) => new AgentObservationBuilder().Build(w, player);

    private static void Masks()
    {
        // Two own camps three apart: the eastward step from A would end two from B.
        var w = World(new[] { 0, 0 }, 5, 8); var a = w.Camps[0]; var b = w.Camps[1];
        var o = Observe(w, 0);
        Check(o.MinimumCampSpacing == 3, "observation exposes the spacing");
        string blocked = OrderRules.Validate(o, Move(w, a, 6));
        Check(blocked != null && blocked.Contains("spacing"), "a destination within spacing of another own camp is illegal");
        Check(OrderRules.Validate(o, Move(w, a, 4)) == null, "a destination at spacing from own camps stays legal");
        Check(OrderRules.Validate(o, new Order(OrderKind.NoOp, a.Id)) == null, "waiting is always legal");
        var mask = LegalActions.Build(o).Single(m => m.Entity == a.Id);
        Check(!mask.Allows(Move(w, a, 6)) && mask.Allows(Move(w, a, 4)), "the action mask omits the close destination");
        Check(mask.Actions.Where(x => x.Kind == OrderKind.Move).All(x => w.Map.Distance(x.Destination, b.Tile) >= 3), "no masked move is within spacing of an own camp");
        // A visible foreign camp two hexes east (inside vision radius 2).
        w = World(new[] { 0, 1 }, 5, 7); a = w.Camps[0]; b = w.Camps[1];
        o = Observe(w, 0);
        Check(o.Rivals.Any(r => r.Tile == b.Tile), "the foreign camp is visible");
        mask = LegalActions.Build(o).Single(m => m.Entity == a.Id);
        Check(!mask.Allows(Move(w, a, 6)) && mask.Allows(Move(w, a, 4)), "a visible foreign camp limits destinations");
        Check(mask.Allows(new Order(OrderKind.NoOp, a.Id)), "a camp of a pre-existing close pair may still wait");
        // Hunters are not camps: their moves are unaffected.
        var hunter = new Hunter { Id = w.NextId++, Player = 0, SourceCamp = a.Id, Tile = a.Tile, Population = 8 };
        w.Hunters.Add(hunter); Ownership.Bind(w); o = Observe(w, 0);
        Check(OrderRules.Validate(o, new Order(OrderKind.Move, hunter.Id, destination: At(w, 6))) == null, "hunter groups ignore camp spacing");
    }

    private static void Hidden()
    {
        // A foreign camp three hexes east is outside vision radius 2: masks cannot use it, resolution still enforces it.
        var w = World(new[] { 0, 1 }, 5, 8); var a = w.Camps[0]; var b = w.Camps[1];
        var o = Observe(w, 0);
        Check(o.Rivals.Count == 0 && !o.Tiles.ContainsKey(b.Tile), "the hidden camp is not observed");
        var mask = LegalActions.Build(o).Single(m => m.Entity == a.Id);
        Check(mask.Allows(Move(w, a, 6)), "an unseen camp does not restrict the mask");
        var p = CampPlacement.Resolve(w, new[] { Move(w, a, 6) });
        Check(p.Blocked.SetEquals(new[] { a.Id }) && p.Destinations[a.Id] == a.Tile, "resolution enforces spacing against the unseen camp");
        var prepared = TurnPlanner.Prepare(w, new Dictionary<int, List<Order>> { { 0, new List<Order> { Move(w, a, 6) } } });
        Check(prepared.Placement.Blocked.Contains(a.Id), "a prepared turn carries the blocked placement");
        new TurnResolver().Resolve(w, prepared);
        Check(w.Camps.First(c => c.Id == a.Id).Tile == At(w, 5), "the blocked camp did not move");
    }

    private static void Policy()
    {
        // The heuristic policy never plans a camp move within spacing of a camp it observes.
        for (int seed = 1; seed <= 3; seed++)
        {
            var w = new WorldGenerator().Generate(50 + seed, SimulationConfig.Small(4));
            for (int turn = 0; turn < 6 && !w.Finished; turn++)
            {
                var plan = AiPlanning.Decide(w);
                foreach (var camp in w.Camps)
                {
                    var o = Observe(w, camp.Player);
                    foreach (var order in plan.Orders.SelectMany(p => p.Value).Where(x => x.Entity == camp.Id && x.Destination >= 0))
                        Check(!OrderRules.CampSpacingBlocked(o, camp.Id, camp.Tile, order.Destination), "policy destination honours observed spacing");
                }
                new TurnResolver().Resolve(w, plan.Orders, plan.Traces);
                foreach (var x in w.Camps) foreach (var y in w.Camps) if (x.Id < y.Id) Check(w.Map.Distance(x.Tile, y.Tile) >= w.Config.MinimumCampSpacing, "resolved camps keep spacing");
            }
        }
        // The policy really does use the filter: a camp hemmed by a visible rival skips the close tile.
        var f = World(new[] { 0, 1 }, 5, 7); var me = f.Camps[0];
        var decided = AiPlanning.Decide(f);
        foreach (var order in decided.Orders.SelectMany(p => p.Value).Where(x => x.Entity == me.Id && x.Destination >= 0))
            Check(f.Map.Distance(order.Destination, f.Camps[1].Tile) >= 3, "hemmed camp avoids destinations near the visible rival");
    }

    private static void Replay()
    {
        // Simultaneous approaches are legal when planned and rolled back at resolution; replay must not depend on workers or order.
        WorldState Make() => World(new[] { 0, 1, 2 }, 5, 9, 13);
        var worlds = new[] { Make(), Make(), Make() };
        var workers = new[] { new PhaseExecution(), new PhaseExecution(4, 3, true, 1), null };
        worlds[1].Camps.Reverse();
        int blocked = 0;
        for (int turn = 0; turn < 4; turn++)
        {
            for (int i = 0; i < 2; i++)
            {
                var w = worlds[i]; var c = w.Camps.OrderBy(x => x.Id).ToArray();
                // Turns alternate inward and outward steps so some moves collide and some do not.
                int step = turn % 2 == 0 ? 1 : -1;
                var orders = new Dictionary<int, List<Order>>();
                for (int k = 0; k < c.Length; k++)
                {
                    int dq = k == 0 ? step : k == 1 ? -step : 0;
                    if (dq == 0) continue;
                    int q = Shift(w, c[k].Tile, dq);
                    orders[c[k].Player] = new List<Order> { new Order(OrderKind.Move, c[k].Id, destination: q) };
                }
                var prepared = TurnPlanner.Prepare(w, orders, null, workers[i]);
                if (i == 0) blocked += prepared.Placement.Blocked.Count;
                new TurnResolver(workers[i]).Resolve(w, prepared);
            }
            if (turn == 1) worlds[2] = SaveCodec.Load(SaveCodec.Save(worlds[0]));
            else if (turn >= 2)
            {
                var w = worlds[2]; var c = w.Camps.OrderBy(x => x.Id).ToArray(); int step = turn % 2 == 0 ? 1 : -1; var orders = new Dictionary<int, List<Order>>();
                for (int k = 0; k < 2; k++) orders[c[k].Player] = new List<Order> { new Order(OrderKind.Move, c[k].Id, destination: Shift(w, c[k].Tile, k == 0 ? step : -step)) };
                new TurnResolver().Resolve(w, orders);
            }
            Check(SaveCodec.Fingerprint(worlds[0]) == SaveCodec.Fingerprint(worlds[1]), "scalar and reversed 4-worker replay agree after turn " + turn);
            if (turn >= 2) Check(SaveCodec.Fingerprint(worlds[0]) == SaveCodec.Fingerprint(worlds[2]), "save continuation agrees after turn " + turn);
        }
        Check(blocked >= 1, "the replay exercises at least one spacing rollback");
    }
}
