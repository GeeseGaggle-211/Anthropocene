using System;
using System.Linq;
using Anthropocene.Simulation;

internal static class AgentContractChecks
{
    private static int count;
    private static void Check(bool ok, string message) { count++; if (!ok) throw new Exception("Agent contract: " + message); }
    public static int Run()
    {
        count = 0;
        var w = new WorldGenerator().Generate(1729, SimulationConfig.Small(3));
        var c = w.Camps[0]; var rival = w.Camps[1];
        const int ridgeMask = 0x2D;
        w.Tiles[c.Tile].RidgeMask = ridgeMask;
        Check(w.Tiles[c.Tile].RidgeMask != 0, "ridge fixture starts from a nonzero source mask");
        var hunter = new Hunter { Id = w.NextId++, Player = c.Player, SourceCamp = c.Id, SocietyId = c.SocietyId, Population = 8, Tile = c.Tile };
        w.Hunters.Add(hunter); c.Population -= 8;
        var wild = new Herd { Id = w.NextId++, Animals = 12, Tile = c.Tile }; w.Herds.Add(wild);
        rival.Tile = w.Map.Neighbors(c.Tile).First(n => n != c.Tile);
        var builder = new AgentObservationBuilder(); var policy = new HeuristicTribePolicy(); var o = builder.Build(w, c.Player);
        string fingerprint = SaveCodec.Fingerprint(w), json = ObservationContract.Serialize(o, c.Mandate);
        var dto = ObservationContract.Deserialize(json);
        Check(o.Tiles[c.Tile].RidgeMask == ridgeMask && dto.Tiles.Single(t => t.Id == c.Tile).RidgeMask == ridgeMask, "nonzero source ridge mask reaches the filtered observation and serialized DTO exactly");
        var replay = SaveCodec.Load(SaveCodec.Save(w));
        var replayObservation = builder.Build(replay, c.Player);
        var replayDto = ObservationContract.Deserialize(ObservationContract.Serialize(replayObservation, c.Mandate));
        Check(replay.Tiles[c.Tile].RidgeMask == ridgeMask && replayObservation.Tiles[c.Tile].RidgeMask == ridgeMask && replayDto.Tiles.Single(t => t.Id == c.Tile).RidgeMask == ridgeMask && SaveCodec.Fingerprint(replay) == fingerprint, "ridge mask survives save/load replay through source, observation and DTO");
        Check(dto.Schema == AgentObservation.SchemaVersion && dto.Tiles.Length == o.Tiles.Count && dto.Hunters.Single().SourceCamp == c.Id && dto.Experience.SequenceEqual(c.Experience), "serializable observation retains the filtered policy inputs and source settlement");
        Check(dto.Herds.Single(h => h.Id == wild.Id).RepresentativeCount == 10 && !dto.Herds.Single(h => h.Id == wild.Id).ExactCount, "serialized wild counts remain estimates");
        Check(dto.LegalActions.All(m => m.Actions.All(a => OrderRules.Validate(o, a) == null && m.Allows(a))), "every enumerated action is accepted by the shared validator");
        Check(dto.LegalActions.First(m => m.Entity == c.Id).MaximumDispatch == Math.Min(c.GathererPopulation, c.Population - w.Config.MinimumCamp), "dispatch range exposes the actual available workforce");
        Check(OrderRules.Validate(o, new Order(OrderKind.NoOp, hunter.Id, destination: 0)) != null && OrderRules.Validate(o, new Order(OrderKind.Dispatch, c.Id, destination: 0, population: 1)) != null, "non-movement orders cannot smuggle an unvalidated destination");
        Check(SaveCodec.Fingerprint(w) == fingerprint, "observation and mask generation are read-only");
        rival.Population += 300; rival.Food += 100000; wild.Animals = 14;
        int hidden = Enumerable.Range(0, w.Tiles.Length).First(n => !o.Tiles.ContainsKey(n)); w.Tiles[hidden].Food += 123456;
        w.History.Add(new TurnRecord { Events = { "secret rival event" }, Orders = { "secret rival order" } });
        Check(ObservationContract.Serialize(builder.Build(w, c.Player), c.Mandate) == json, "hidden stores, populations, tiles, rival orders and within-band wild counts do not change the DTO");
        var original = policy.Decide(builder.Build(w, c.Player), c.Mandate).Orders.Select(a => a.ToString()).ToArray();
        c.Player = 77; hunter.Player = 77; w.Societies.Single(s => s.Id == c.SocietyId).Player = 77;
        var relabeled = policy.Decide(builder.Build(w, 77), c.Mandate).Orders.Select(a => a.ToString()).ToArray();
        Check(original.SequenceEqual(relabeled), "relabeling player identity does not change policy choices");
        var index = new PlanningIndex(w); var indexed = builder.Build(w, 77, index);
        Check(ObservationContract.Serialize(indexed, c.Mandate) == ObservationContract.Serialize(builder.Build(w, 77), c.Mandate), "shared spatial snapshot and standalone observation agree");
        Check(indexed.Tiles[c.Tile].RidgeMask == ridgeMask, "shared spatial snapshot retains the exact ridge mask");
        var preferences = Enumerable.Range(1, 200).Select(seed => PolicyState.Seeded(seed)).ToArray();
        Check(preferences.Any(p => p.HuntingInclination > p.GatheringInclination) && preferences.Any(p => p.FishingInclination > p.GatheringInclination) && preferences.Any(p => p.GatheringInclination > Math.Max(p.HuntingInclination, p.FishingInclination)), "seeded policy state supports all three preferences without player-ID cohorts");
        Console.WriteLine("PASS: " + count + " agent-contract assertions"); return count;
    }
}
