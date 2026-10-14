using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using Anthropocene.Simulation;

internal static class AutonomousGrowthChecks
{
    private static int checks, steps, replaySteps;
    private static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception("Growth authority: " + message); }
    private static void Reject(Action action, string message)
    { bool rejected = false; try { action(); } catch (ArgumentException) { rejected = true; }
        catch (InvalidOperationException) { rejected = true; } catch (InvalidDataException) { rejected = true; }
        Check(rejected, message); }
    private static AutonomousElderAction Action(AutonomousElderObservation o, double breeding = 1, double retention = 1, int role = 2)
    { var a = AutonomousElderCompiler.Uniform(o); Array.Clear(a.Workforce, 0, 13); a.Workforce[role] = 1;
        a.Breeding = o.ControlMask[2] ? breeding : 0; a.RetainedHerdFraction = o.ControlMask[1] ? retention : 0; return a; }
    private static AutonomousElderHistoryStep Step(AutonomousElderHistoryEnvironment e, Dictionary<int, AutonomousElderHistoryObservation> o, double breeding = 1, double retention = 1, int role = 2)
    { if (++steps > 128) throw new Exception("Repair validation cap"); return e.Step(o.ToDictionary(p => p.Key, p => Action(p.Value, breeding, retention, role))); }
    private static void Replay(AutonomousElderHistoryEnvironment e)
    { replaySteps += e.Replay.Steps.Count; if (replaySteps > 128) throw new Exception("Repair replay cap");
        AutonomousElderHistoryEnvironment.VerifyReplay(e.Replay, new PhaseExecution(4, 1, true, 1)); checks++; }
    private static WorldState Fixture()
    { var w = HusbandryChecks.Fixture(); w.Config.HerdLabourEnabled = true; w.Config.AnnualHumanGrowthRate = 0;
        w.Camps[0].Inventory.HerdingTools = 1000; w.Camps[0].PlantFood = 100000; return w; }
    private static string Magic(byte[] bytes)
    { using var zip = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress); using var reader = new BinaryReader(zip); return reader.ReadString(); }

    public static void HeuristicPanel(string bank, string[] entryIds)
    {
        var timer = Stopwatch.StartNew(); int resolves = 0; var episodes = new List<object>();
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(bank, "manifest.json")));
        foreach (string id in entryIds)
        {
            var entry = manifest.RootElement.GetProperty("entries").EnumerateArray().Single(e => e.GetProperty("id").GetString() == id);
            if (entry.GetProperty("partition").GetString() != "development") throw new ArgumentException("Heuristic panel is DEV only.");
            var bytes = File.ReadAllBytes(Path.Combine(bank, entry.GetProperty("snapshot").GetString()));
            using var sha = System.Security.Cryptography.SHA256.Create();
            if (Convert.ToHexString(sha.ComputeHash(bytes)).ToLowerInvariant() != entry.GetProperty("sha256").GetString()) throw new Exception("Changed source.");
            var w = SaveCodec.Load(bytes); int focal = entry.GetProperty("focal").GetInt32();
            double welfare = 0, time = 0, populationYears = 0, qolYears = 0; int count = 0; var trace = new List<object>();
            for (int i = 0; i < 25; i++)
            {
                var before = w.Camps.FirstOrDefault(c => c.Id == focal); if (before == null || before.Population <= 0) break;
                int opening = before.Population; var orders = AiPlanning.Decide(w).Orders;
                var record = new TurnResolver().Resolve(w, TurnPlanner.Prepare(w, orders)); resolves++; count++;
                var after = w.Camps.FirstOrDefault(c => c.Id == focal); double q = after == null ? 0 : ElderRLContract.Unit(after.QoL);
                double years = record.Duration / w.Config.DaysPerYear;
                double reward = ElderRewardContract.Physical(opening, q, years).UndiscountedIntervalWelfare;
                welfare += reward; time += years; populationYears += opening * years; qolYears += q * years;
                trace.Add(new { populationStart = opening, populationEnd = after?.Population ?? 0, qol = q, elapsedYears = years, reward });
            }
            episodes.Add(new { id, steps = count, integratedWelfare = welfare, annualWelfare = welfare / time,
                averagePopulation = populationYears / time, averageQoL = qolYears / time,
                finalPopulation = w.Camps.FirstOrDefault(c => c.Id == focal)?.Population ?? 0, trace });
        }
        Console.WriteLine(JsonSerializer.Serialize(new { version = "native6-heuristic-dev-panel", resolves, wallSeconds = timer.Elapsed.TotalSeconds,
            protocol = "native heuristic for every village; diagnostic unequal-authority/peer comparator", episodes }));
    }

    public static void Run()
    {
        var timer = Stopwatch.StartNew();
        try { RunCore(); }
        catch
        { Console.WriteLine(JsonSerializer.Serialize(new { assertions = checks, validationResolves = steps,
            replayResolves = replaySteps, wallSeconds = timer.Elapsed.TotalSeconds, result = "FAILED" })); throw; }
    }
    private static void RunCore()
    {
        checks = steps = replaySteps = 0; var timer = Stopwatch.StartNew();
        var births = new List<int>();
        foreach (double b in new[] { 0d, .5, 1 })
        {
            var w = Fixture(); int id = w.Camps[0].Id;
            var e = new AutonomousElderHistoryEnvironment(growthAuthority: true); var o = e.Reset(SaveCodec.Save(w), new[] { id }, 3);
            Check(o[id].SchemaVersion == 6 && o[id].History.Length == 301 && o[id].History[300] == 0, "new absent history discriminator");
            var r = Step(e, o, b); var saved = e.CaptureSnapshot(); var end = SaveCodec.Load(saved);
            int born = end.History.Last().ManagedHerds.Sum(h => h.NewbornAnimals); births.Add(born);
            Check(end.History.Last().ManagedHerds.All(h => h.Slaughtered == 0), "retention1 never slaughters newborns");
            Check(end.Herds.Sum(h => h.Animals) == 100 + born, "retained post-growth animals conserve cohorts");
            Check(end.Camps[0].ManagedBreedingIntensity == b, "full breeding coefficient respected");
            Check(end.Camps.All(c => c.EconomicLabor == null || !c.EconomicLabor.PolicyAuthority && !c.EconomicLabor.PostGrowthHerdRetentionSet && c.EconomicLabor.PostGrowthHerdRetention == 0), "transients cleared");
            Check(!end.Camps[0].HerdTargetSet && end.Camps[0].HerdTarget == 0, "relinquished control has no stale opening target");
            Check(Magic(saved) == "ANTHROPOCENE-13" && end.AutonomousReceipts[id].SchemaVersion == 2 && r.CompletedHistory[id][300] == 4, "Save13/action4 receipt discriminator");
            Reject(() => new AutonomousElderHistoryEnvironment().Reset(saved, new[] { id }), "action3 refuses downgrade");
            var resumed = new AutonomousElderHistoryEnvironment(growthAuthority: true); var ro = resumed.Reset(saved, new[] { id }, 3);
            var a = Step(e, r.Observations, b); var cstep = Step(resumed, ro, b);
            Check(a.Fingerprint == cstep.Fingerprint && a.CompletedHistory[id].SequenceEqual(cstep.CompletedHistory[id]), "save/resume exact");
            Replay(e); Replay(resumed);
        }
        Check(births[0] == 0 && births[1] > 0 && births[2] > births[1], "zero/intermediate/full breeding produce ordered births");
        foreach (double keep in new[] { 0d, .5, 1 })
        {
            var w = Fixture(); var first = w.Herds[0]; first.Animals = 60;
            w.Herds.Add(new Herd { Id = w.NextId++, Tile = first.Tile, Species = first.Species, Animals = 40, Capacity = 1, DomesticCamp = first.DomesticCamp, OwnerSociety = first.OwnerSociety });
            var e = new AutonomousElderHistoryEnvironment(growthAuthority: true); var o = e.Reset(SaveCodec.Save(w), new[] { w.Camps[0].Id });
            Step(e, o, 0, keep); var end = SaveCodec.Load(e.CaptureSnapshot()); var reports = end.History.Last().ManagedHerds;
            Check(end.Herds.Sum(h => h.Animals) == (int)Math.Floor(100 * keep), "aggregate post-growth retention");
            Check(reports.Sum(h => h.Slaughtered) + end.Herds.Sum(h => h.Animals) == 100, "slaughter conservation");
            Check(end.Herds.OrderBy(h => h.Id).Select(h => h.Animals).SequenceEqual(new[] { (int)(60 * keep), (int)(40 * keep) }), "stable proportional multiple-herd partition");
            Replay(e);
        }
        var oldWorld = Fixture(); int oldId = oldWorld.Camps[0].Id;
        var old = new AutonomousElderHistoryEnvironment(); var oldObs = old.Reset(SaveCodec.Save(oldWorld), new[] { oldId });
        var oldResult = Step(old, oldObs); var oldEnd = SaveCodec.Load(old.CaptureSnapshot());
        Check(oldEnd.Herds.Sum(h => h.Animals) == 100 && oldEnd.History.Last().ManagedHerds.Sum(h => h.Slaughtered) > 0, "action3 opening-target behavior preserved"); Replay(old);
        Check(Magic(old.CaptureSnapshot()) == "ANTHROPOCENE-12", "old Save12 retained");
        var migrate = new AutonomousElderHistoryEnvironment(growthAuthority: true); var migrated = migrate.Reset(old.CaptureSnapshot(), new[] { oldId });
        Check(migrated[oldId].History.Take(300).SequenceEqual(oldResult.Observations[oldId].History) && migrated[oldId].History[300] == 3, "legacy receipt explicitly distinguished without deleting memory");
        Step(migrate, migrated); Replay(migrate);
        var mixedWorld = AutonomousElderChecks.Fixture(true); var ids = mixedWorld.Camps.Select(c => c.Id).ToArray();
        var mixedOld = new AutonomousElderHistoryEnvironment(); var mo = mixedOld.Reset(SaveCodec.Save(mixedWorld), ids); var mr = Step(mixedOld, mo, role: 1); Replay(mixedOld);
        var mixed = new AutonomousElderHistoryEnvironment(growthAuthority: true); var m = mixed.Reset(mixedOld.CaptureSnapshot(), ids.Take(1).ToArray());
        var bad = Action(m[ids[0]], role: 1); bad.SchemaVersion = 3;
        Reject(() => mixed.Step(new Dictionary<int, AutonomousElderAction> { [ids[0]] = bad }), "new boundary rejects action3 before resolve");
        Step(mixed, m, role: 1); var mixedEnd = SaveCodec.Load(mixed.CaptureSnapshot());
        Check(mixedEnd.AutonomousReceipts[ids[0]].SchemaVersion == 2 && mixedEnd.AutonomousReceipts[ids[1]].SchemaVersion == 1, "mixed-version receipts roundtrip explicitly"); Replay(mixed);

        var hungry = Fixture(); int hungryId = hungry.Camps[0].Id; hungry.Config.StarvationGraceTurns = 0;
        foreach (var t in hungry.Tiles) { t.Food = t.PotentialFood = t.Freshwater = 0;
            for (int i = 0; i < t.Seasons.Length; i++) { var s = t.Seasons[i]; s.MaxFood = s.MaxPotentialFood = s.MaxFreshwater = 0;
                s.FoodRecoveryPerDay = s.BiomassRecoveryPerDay = s.WaterRecoveryPerDay = 0; t.Seasons[i] = s; } }
        var he = new AutonomousElderHistoryEnvironment(growthAuthority: true); var ho = he.Reset(SaveCodec.Save(hungry), new[] { hungryId });
        Step(he, ho); var hr = SaveCodec.Load(he.CaptureSnapshot()).History.Last().ManagedHerds;
        Check(hr.Sum(h => h.NewbornAnimals) == 0 && hr.Sum(h => h.StarvationDeaths) > 0, "nutrition and starvation remain authoritative"); Replay(he);
        var terminal = AutonomousElderChecks.Fixture(); var tc = terminal.Camps[0]; tc.Population = 1; tc.Workforce.Assignments.Clear();
        tc.PlantFood = tc.AnimalFood = tc.AquaticFood = 0; terminal.Config.Mortality = 1; terminal.Config.StarvationGraceTurns = 0; terminal.Config.FissionEnabled = false;
        foreach (var t in terminal.Tiles) { t.Food = t.PotentialFood = t.Freshwater = 0; t.CultivationCapital = 0;
            for (int i = 0; i < t.Seasons.Length; i++) { var s = t.Seasons[i]; s.MaxFood = s.MaxPotentialFood = s.MaxFreshwater = 0;
                s.FoodRecoveryPerDay = s.BiomassRecoveryPerDay = s.WaterRecoveryPerDay = 0; t.Seasons[i] = s; } }
        var te = new AutonomousElderHistoryEnvironment(growthAuthority: true); var to = te.Reset(SaveCodec.Save(terminal), new[] { tc.Id });
        var tr = Step(te, to, role: 1); var tend = SaveCodec.Load(te.CaptureSnapshot());
        Check(tr.Terminated && tr.CompletedHistory[tc.Id][300] == 4 && tend.AutonomousReceipts[tc.Id].EndingPopulation == 0, "terminal action4 receipt survives save"); Replay(te);
        var fission = FissionChecks.HistoryFixture(); int fid = fission.Camps[0].Id;
        var fe = new AutonomousElderHistoryEnvironment(growthAuthority: true); var fo = fe.Reset(SaveCodec.Save(fission), new[] { fid });
        var fr = Step(fe, fo, role: 1); var fend = SaveCodec.Load(fe.CaptureSnapshot());
        Check(fend.History.Last().Fissions.Any(), "new-authority daughter fixture fissions");
        Check(fend.Camps.Where(c => c.Id != fid).All(c => c.ElderMemory == null &&
            (c.EconomicLabor == null || !c.EconomicLabor.PolicyAuthority && !c.EconomicLabor.PostGrowthHerdRetentionSet)), "daughters inherit no authority or reward history");
        Check(fr.Rewards[fid] == fend.AutonomousReceipts[fid].OpeningPopulation * fend.AutonomousReceipts[fid].QoL * 20, "reward remains parent-local"); Replay(fe);
        Console.WriteLine(JsonSerializer.Serialize(new { assertions = checks, validationResolves = steps, replayResolves = replaySteps, wallSeconds = timer.Elapsed.TotalSeconds,
            physicalGrowthProved = true, births, schema = 6, result = "PASS" }));
    }
}
