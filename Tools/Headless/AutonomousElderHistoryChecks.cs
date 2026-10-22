using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Xml;
using Anthropocene.Simulation;

internal static class AutonomousElderHistoryChecks
{
    private static int checks, steps, replaySteps;
    private static void Check(bool ok, string message)
    { checks++; if (!ok) throw new Exception("Autonomous history: " + message); }
    private static void Reject(Action action, string message)
    { bool rejected = false; try { action(); } catch (ArgumentException) { rejected = true; }
        catch (InvalidOperationException) { rejected = true; } catch (InvalidDataException) { rejected = true; }
        Check(rejected, message); }
    private static AutonomousElderAction Policy(AutonomousElderObservation o, int role = 1)
    { var a = AutonomousElderCompiler.Uniform(o); Array.Clear(a.Workforce, 0, 13); a.Workforce[role] = 1;
        a.FieldExpansionFraction = o.ControlMask[0] ? .2 : 0; return a; }
    private static AutonomousElderHistoryStep Step(AutonomousElderHistoryEnvironment e, Dictionary<int, AutonomousElderHistoryObservation> o)
    { steps++; return e.Step(o.ToDictionary(p => p.Key, p => Policy(p.Value))); }
    private static void Replay(AutonomousElderHistoryEnvironment e)
    { replaySteps += e.Replay.Steps.Count; AutonomousElderHistoryEnvironment.VerifyReplay(e.Replay, new PhaseExecution(4, 1, true, 1)); checks++; }

    public static void Run()
    {
        checks = steps = replaySteps = 0;
        var w = AutonomousElderChecks.Fixture(true); int[] ids = w.Camps.Select(c => c.Id).ToArray();
        var original = SaveCodec.Save(w); string initial = SaveCodec.Fingerprint(w);
        var env = new AutonomousElderHistoryEnvironment(); var obs = env.Reset(original, ids, 8);
        var opened = SaveCodec.Load(env.CaptureSnapshot());
        Check(AutonomousElderHistoryContract.FeatureNames.Length == 300, "named history width");
        Check(opened.Camps.All(c => c.ElderMemory?.Initialized == true), "persistent memory initialized");
        Check(opened.AutonomousReceipts.Count == 2 && obs.Values.All(o => o.History[0] == 0 && o.History[298] == 1), "explicit absent action and institution presence");
        Check(obs.Values.All(o => o.Context.Skip(117).Take(3).All(x => x == 0)), "cold maturity remains honest");
        Check(SaveCodec.Fingerprint(SaveCodec.Load(original)) == initial && w.Camps.All(c => c.ElderMemory == null), "original input unchanged");
        Reject(() => new AutonomousElderEnvironment().Reset(env.CaptureSnapshot(), ids), "old autonomous mode rejects new receipt snapshot");
        var invalid = obs.ToDictionary(p => p.Key, p => Policy(p.Value)); invalid[ids[1]].Breeding = double.NaN;
        string before = SaveCodec.Fingerprint(opened);
        Reject(() => env.Step(invalid), "whole WEGO invalid action rejected");
        Check(SaveCodec.Fingerprint(SaveCodec.Load(env.CaptureSnapshot())) == before, "invalid action does not mutate history");

        var r = Step(env, obs); obs = r.Observations;
        var end = SaveCodec.Load(env.CaptureSnapshot());
        Check(obs.Values.All(o => o.Context[117] > 0 && o.History[0] == 1 && o.History[1] == 20 && o.History[2] == 20), "physical maturity and decision duration");
        Check(obs.Values.All(o => o.Context.Skip(40).Take(16).All(x => x == 0) && o.Context.Skip(114).Take(3).All(x => x == 0)), "obsolete heads never masquerade as autonomous action");
        foreach (int id in ids)
        {
            var receipt = end.AutonomousReceipts[id];
            Check(receipt.Requested.Workforce[1] == 1 && receipt.Requested.FieldExpansionFraction == .2, "requested labor/control persisted");
            Check(receipt.CompiledStaffing.SequenceEqual(r.Staffing[id]), "compiled allocation equals native plan");
            Check(receipt.EndingPopulation == r.Telemetry.Population[id] && receipt.QoL == ElderRLContract.Unit(r.Telemetry.Quality[id]), "actual outcomes recorded locally");
            double welfare = receipt.OpeningPopulation * receipt.QoL * 20;
            Check(r.Rewards[id] == welfare, "reward exclusively village-local");
        }
        var saved = env.CaptureSnapshot(); var resumed = new AutonomousElderHistoryEnvironment(); var resObs = resumed.Reset(saved, ids, 8);
        Check(obs.All(p => p.Value.History.SequenceEqual(resObs[p.Key].History) && p.Value.Context.SequenceEqual(resObs[p.Key].Context)), "save/reload keeps history and maturity");
        var next = Step(env, obs); var resNext = Step(resumed, resObs);
        Check(next.Fingerprint == resNext.Fingerprint && next.CompletedHistory.All(p => p.Value.SequenceEqual(resNext.CompletedHistory[p.Key])), "next physical transition exact after reset/resume");
        Replay(env); Replay(resumed);
        var mutation = resNext.Observations[ids[0]].History; mutation[0] = 99;
        Check(resumed.Replay.Steps.Last().CompletedHistory[ids[0]][0] == 1, "caller edits do not mutate committed replay");
        var tampered = resumed.Replay; tampered.Steps[0].CompletedHistory[ids[0]][1] += 1;
        Reject(() => AutonomousElderHistoryEnvironment.VerifyReplay(tampered), "tampered action receipt replay rejected"); replaySteps++;
        EqualCurrentHistory(); DirectPreparation(); Quotes(); Daughters(); Terminal(); Corruption(saved); Corruption(SaveCodec.Save(SettlingGraceChecks.WithoutSave15Rules(SaveCodec.Load(saved))));
        Console.WriteLine(JsonSerializer.Serialize(new { assertions = checks, validationResolves = steps, replayResolves = replaySteps,
            schema = 5, historyWidth = 300, result = "PASS" }));
    }

    private static void EqualCurrentHistory()
    {
        var a = AutonomousElderChecks.Fixture(); var b = SaveCodec.Load(SaveCodec.Save(a));
        int id = a.Camps[0].Id; AutonomousElderHistory.Initialize(a, new[] { id }); AutonomousElderHistory.Initialize(b, new[] { id });
        a.Camps[0].ElderMemory.CurrentSignals[0] = .1; b.Camps[0].ElderMemory.CurrentSignals[0] = .9;
        a.Days = b.Days = 20 * a.Config.DaysPerYear;
        foreach (var w in new[] { a, b })
        { var m = w.Camps[0].ElderMemory; var signals = (double[])m.CurrentSignals.Clone(); signals[0] = .5; m.Observe(w.Days, w.Config.DaysPerYear, signals); }
        var oa = AutonomousElderHistoryEnvironment.Observe(a, id); var ob = AutonomousElderHistoryEnvironment.Observe(b, id);
        Check(oa.Context[5] == ob.Context[5] && oa.Stocks.SequenceEqual(ob.Stocks), "synthetic pair has equal current economic state");
        Check(oa.Context[84] != ob.Context[84] && oa.Context[84] != oa.Context[85], "distinct permitted histories survive observation");
        var m0 = a.Camps[0].ElderMemory; double old = m0.ExponentialMeans[0];
        m0.AdvanceTo(a.Days + 10 * a.Config.DaysPerYear, a.Config.DaysPerYear);
        Check(Math.Abs(m0.ExponentialMeans[0] - (Math.Exp(-.5) * old + (1-Math.Exp(-.5)) * .5)) < 1e-12, "exact physical-time decay");
        Reject(() => m0.Observe(0, a.Config.DaysPerYear, m0.CurrentSignals), "backward memory chronology rejected");
    }

    private static void DirectPreparation()
    {
        var w = AutonomousElderChecks.Fixture(); var c = w.Camps[0]; AutonomousElderHistory.Initialize(w, new[] { c.Id });
        var obs = new Dictionary<int, AutonomousElderObservation> { [c.Id] = AutonomousElderHistoryEnvironment.Observe(w, c.Id) };
        var actions = new Dictionary<int, AutonomousElderAction> { [c.Id] = Policy(obs[c.Id]) };
        var orders = new Dictionary<int, List<Order>> { [c.Player] = new List<Order> { new Order(OrderKind.Economy, c.Id) { Economy = new EconomicIntent() } } };
        string before = SaveCodec.Fingerprint(w);
        Reject(() => TurnPlanner.PrepareAutonomous(w, orders, actions, obs), "conflicting direct economy order rejected");
        Check(SaveCodec.Fingerprint(w) == before, "direct invalid submission is pure");
        orders[c.Player].Clear(); var prepared = TurnPlanner.PrepareAutonomous(w, orders, actions, obs);
        Check(!w.AutonomousReceipts[c.Id].HasDecision, "prepare publishes no outcome");
        steps++; var record = new TurnResolver().Resolve(w, prepared);
        Check(w.AutonomousReceipts[c.Id].HasDecision && w.AutonomousReceipts[c.Id].EndDays == record.StartDays + record.Duration, "direct resolver commits common close receipt");
    }

    private static void Quotes()
    {
        foreach (int variant in new[] { 0, 1, 2 })
        {
            bool compatible = variant != 1; bool partial = variant == 2;
            var w = AutonomousElderChecks.Fixture(true); var ids = w.Camps.Select(c => c.Id).ToArray();
            if (partial) w.Camps[0].Inventory.Wood = 3;
            var e = new AutonomousElderHistoryEnvironment(); var o = e.Reset(SaveCodec.Save(w), ids);
            var a = Policy(o[ids[0]]); var b = Policy(o[ids[1]]);
            a.Quote = new ElderStockQuote { Counterparty = 0, Give = TradeGood.Wood, Want = TradeGood.Clay, GiveQuantity = 10, WantQuantity = 20 };
            b.Quote = new ElderStockQuote { Counterparty = 0, Give = TradeGood.Clay, Want = TradeGood.Wood, GiveQuantity = 20, WantQuantity = compatible ? 10 : 100 };
            steps++; var r = e.Step(new Dictionary<int, AutonomousElderAction> { [ids[1]] = b, [ids[0]] = a });
            var end = SaveCodec.Load(e.CaptureSnapshot()); var ar = end.AutonomousReceipts[ids[0]]; var br = end.AutonomousReceipts[ids[1]];
            Check(ar.Requested.Quote.WantQuantity == 20 && br.Requested.Quote.WantQuantity == b.Quote.WantQuantity, "both requested quote terms persisted");
            Check(ar.SettledTrades == (compatible ? 1 : 0) && Math.Abs(ar.SettledGive - (partial ? 3 : compatible ? 10 : 0)) < 1e-9 &&
                Math.Abs(ar.SettledWant - (partial ? 6 : compatible ? 20 : 0)) < 1e-9, "compatible/incompatible/partial physical settlement receipt");
            Check(ar.SettledGive == br.SettledWant && ar.SettledWant == br.SettledGive, "gross settled legs conserved");
            Replay(e);
            var perturbed = SaveCodec.Load(e.CaptureSnapshot()); var own = AutonomousElderHistoryEnvironment.Observe(perturbed, ids[0]);
            perturbed.Camps.Single(c => c.Id == ids[1]).Inventory.Clay += 9876;
            var other = AutonomousElderHistoryEnvironment.Observe(perturbed, ids[0]);
            Check(own.Context.SequenceEqual(other.Context) && own.History.SequenceEqual(other.History) && own.Stocks.SequenceEqual(other.Stocks), "hidden peer stock cannot leak into own history");
        }
    }

    private static void Daughters()
    {
        var w = FissionChecks.HistoryFixture(); int id = w.Camps[0].Id;
        var e = new AutonomousElderHistoryEnvironment(); var o = e.Reset(SaveCodec.Save(w), new[] { id });
        var r = Step(e, o); var end = SaveCodec.Load(e.CaptureSnapshot());
        Check(end.History.Last().Fissions.Count > 0, "actual daughter fixture fissions");
        var daughter = end.Camps.Single(c => c.Id == end.History.Last().Fissions[0].DaughterCamp);
        Check(daughter.ElderMemory == null && !end.AutonomousReceipts.ContainsKey(daughter.Id), "daughter inherits no institution/action receipt");
        var parentReceipt = end.AutonomousReceipts[id];
        var d = new AutonomousElderHistoryEnvironment(); var od = d.Reset(e.CaptureSnapshot(), new[] { daughter.Id });
        Check(od[daughter.Id].History[0] == 0 && od[daughter.Id].Context[117] == 0, "independent daughter starts honest cold history");
        Check(SaveCodec.Load(d.CaptureSnapshot()).AutonomousReceipts[id].EndDays == parentReceipt.EndDays, "daughter opt-in preserves parent institution");
        Replay(e);
    }

    private static void Terminal()
    {
        var w = AutonomousElderChecks.Fixture(); var c = w.Camps[0]; c.Population = 1;
        c.Workforce.Assignments.Clear(); c.PlantFood = c.AnimalFood = c.AquaticFood = 0;
        w.Config.Mortality = 1; w.Config.StarvationGraceTurns = 0; w.Config.FissionEnabled = false;
        foreach (var tile in w.Tiles)
        { tile.Food = tile.PotentialFood = tile.Freshwater = 0; tile.CultivationCapital = 0;
            for (int i = 0; i < tile.Seasons.Length; i++) { var s = tile.Seasons[i];
                s.MaxFood = s.MaxPotentialFood = s.MaxFreshwater = 0;
                s.FoodRecoveryPerDay = s.BiomassRecoveryPerDay = s.WaterRecoveryPerDay = 0;
                tile.Seasons[i] = s; } }
        var e = new AutonomousElderHistoryEnvironment(); var o = e.Reset(SaveCodec.Save(w), new[] { c.Id });
        var r = Step(e, o);
        Check(r.Terminated && r.Observations.Count == 0, "real scarcity terminates controlled village");
        Check(r.CompletedHistory.ContainsKey(c.Id) && r.CompletedHistory[c.Id][0] == 1, "terminal closed receipt remains available");
        var loaded = SaveCodec.Load(e.CaptureSnapshot());
        Check(loaded.AutonomousReceipts[c.Id].EndingPopulation == 0, "terminal outcome persists in save");
        Replay(e);
    }

    private static byte[] CorruptExtension(byte[] snapshot, Action<AutonomousElderHistorySave> corrupt)
    {
        using var plain = new MemoryStream(); using (var zip = new GZipStream(new MemoryStream(snapshot), CompressionMode.Decompress)) zip.CopyTo(plain);
        plain.Position = 0; using var reader = new BinaryReader(plain);
        // Save15 (settling grace / plant tents on) carries the history as extension flag 1 after the envelope.
        bool save15 = SaveCodec.Load(snapshot).Config.UsesSave15();
        string envelope = reader.ReadString();
        Check(save15 ? envelope == "ANTHROPOCENE-15" && (reader.ReadByte() & 1) != 0 : envelope == "ANTHROPOCENE-12", "explicit new save envelope");
        int header = reader.ReadInt32(); plain.Position += header; int tiles = reader.ReadInt32(); plain.Position += 365L * tiles;
        Check(!reader.ReadBoolean(), "corruption fixture has no resource block");
        long at = plain.Position; int length = reader.ReadInt32(); var bytes = reader.ReadBytes(length); long rest = plain.Position;
        var serializer = new DataContractSerializer(typeof(AutonomousElderHistorySave)); AutonomousElderHistorySave state;
        using (var xml = XmlDictionaryReader.CreateBinaryReader(bytes, XmlDictionaryReaderQuotas.Max)) state = (AutonomousElderHistorySave)serializer.ReadObject(xml);
        corrupt(state); using var block = new MemoryStream();
        using (var xml = XmlDictionaryWriter.CreateBinaryWriter(block, null, null, false)) serializer.WriteObject(xml, state);
        using var output = new MemoryStream(); using (var zip = new GZipStream(output, CompressionLevel.Fastest, true))
        { zip.Write(plain.ToArray(), 0, (int)at); using var writer = new BinaryWriter(zip); writer.Write((int)block.Length); writer.Write(block.ToArray()); zip.Write(plain.ToArray(), (int)rest, (int)(plain.Length - rest)); }
        return output.ToArray();
    }
    private static void Corruption(byte[] snapshot)
    {
        foreach (Action<AutonomousElderHistorySave> change in new Action<AutonomousElderHistorySave>[] {
            x => x.SchemaVersion = 99, x => x.Receipts.Add(x.Receipts[0]),
            x => x.Receipts[0].EndDays += 1e9, x => x.Receipts[0].InstitutionInitializedDays += 1,
            x => x.Receipts[0].Production[0] = double.NaN, x => x.Receipts[0].CompiledStaffing = new int[2],
            x => x.Receipts[0].Requested.Movement = 99 })
            Reject(() => SaveCodec.Load(CorruptExtension(snapshot, change)), "corrupt extension rejected on load");
    }

    public static void Benchmark(bool growth = false, bool subsistence = false)
    {
        var samples = new List<object>(); int resolves = 0;
        foreach (int n in growth ? new[] { 1, 80 } : new[] { 1, 8, 80 }) foreach (string mode in growth ? new[] { "heuristic", "history6" } : new[] { "heuristic", "autonomous4", "history5" })
        {
            var times = new List<double>(); var allocated = new List<long>(); var observations = new List<double>();
            for (int run = 0; run < 4; run++)
            {
                var w = AutonomousElderChecks.BenchmarkWorld(n); w.Config.Neolithic.SubsistenceNeedsCovered = subsistence;
                var snapshot = SaveCodec.Save(w); var ids = w.Camps.Select(c => c.Id).ToArray();
                var legacy = new AutonomousElderEnvironment(); var history = new AutonomousElderHistoryEnvironment(growthAuthority: growth);
                Dictionary<int, AutonomousElderAction> actions;
                if (mode == "history5" || mode == "history6") actions = history.Reset(snapshot, ids).ToDictionary(p => p.Key, p => Policy(p.Value));
                else actions = legacy.Reset(snapshot, ids).ToDictionary(p => p.Key, p => Policy(p.Value));
                long start = GC.GetTotalAllocatedBytes(true); var timer = Stopwatch.StartNew();
                if (mode == "history5" || mode == "history6") history.Step(actions);
                else if (mode == "autonomous4") legacy.Step(actions);
                else { var orders = AiPlanning.Decide(w).Orders; new TurnResolver().Resolve(w, TurnPlanner.Prepare(w, orders));
                    foreach (var c in w.Camps) AutonomousElderCompiler.Observe(w, c); SaveCodec.Fingerprint(w); }
                timer.Stop(); resolves++; long bytes = GC.GetTotalAllocatedBytes(false) - start;
                var state = mode == "history5" || mode == "history6" ? SaveCodec.Load(history.CaptureSnapshot()) : mode == "autonomous4" ? SaveCodec.Load(legacy.CaptureSnapshot()) : w;
                var obsTimer = Stopwatch.StartNew();
                foreach (var c in state.Camps.Where(c => ids.Contains(c.Id) && c.Population > 0))
                { if (mode == "history6") { var source = AutonomousElderCompiler.Observe(state, c); source.SchemaVersion = 6;
                    AutonomousElderHistoryEnvironment.Observe(state, c.Id, source); }
                    else if (mode == "history5") AutonomousElderHistoryEnvironment.Observe(state, c.Id); else AutonomousElderCompiler.Observe(state, c); }
                obsTimer.Stop();
                if (run > 0) { times.Add(timer.Elapsed.TotalMilliseconds); allocated.Add(bytes); observations.Add(obsTimer.Elapsed.TotalMilliseconds); }
            }
            samples.Add(new { villages = n, mode, medianMs = times.OrderBy(t => t).ElementAt(1),
                minMs = times.Min(), maxMs = times.Max(), allocatedBytes = allocated.Average(), observationMedianMs = observations.OrderBy(t => t).ElementAt(1) });
        }
        Console.WriteLine(JsonSerializer.Serialize(new { resolves, samples }));
    }
}
