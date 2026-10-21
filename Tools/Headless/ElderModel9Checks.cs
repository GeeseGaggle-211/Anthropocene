using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Anthropocene.Simulation;

// Native Elder contract 8 (model9) contract tests: the heuristic is an exact action, replay is exact, the encoder
// layout is stable and malformed actions are repaired with logged interventions.
internal static class ElderModel9Checks
{
    private static int checks;
    private static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception("ElderModel9: " + message); }

    // Contract 9 capital commitments: commit, hold (re-emitting the committed controls), expiry, forced break,
    // save round trip, the hold mask and exact replay.
    private static void CommitmentChecks(ElderModel9Environment acting, ElderModel9Step last)
    {
        int camp = last.Villages.First(v => v.Observation != null && !v.Terminated).Camp;
        ElderDirective Directive(int span, double field, double herd, double budget, double share) => new ElderDirective
        {
            SchemaVersion = ElderModel9Contract.ActionSchemaVersion, RetainedFieldOperation = field, RetainedHerdFraction = herd,
            DevelopmentBudget = budget, FieldDevelopmentShare = share, MovementCandidate = ElderModel9Contract.NativeMovement, CommitTurns = span
        };
        ElderInstitutionMemory Memory(ElderModel9Environment env) => env.World.Camps.Single(c => c.Id == camp).ElderMemory;
        bool Intervened(ElderModel9Step step, string reason) => step.Villages.Single(v => v.Camp == camp).Interventions
            .Any(i => i.Control == "commitment" && i.Reason.Contains(reason));

        var committed = acting.Step(new Dictionary<int, ElderDirective> { [camp] = Directive(3, .8, 1, .1, .5) });
        var memory = Memory(acting);
        Check(memory.HasCommitment && memory.CommitmentTurns == 3 && memory.CommitmentRemaining == 2 && memory.Commitment != null,
            "a three-turn commitment is saved with two holds left");
        var village = committed.Villages.Single(v => v.Camp == camp);
        Check(village.Observation.CommitmentHoldMask == ElderCommitments.CanHold(acting.World, acting.World.Camps.Single(c => c.Id == camp)),
            "the hold mask reports whether the commitment can be held");
        var reloaded = SaveCodec.Load(SaveCodec.Save(acting.World)).Camps.Single(c => c.Id == camp).ElderMemory;
        Check(reloaded.HasCommitment && reloaded.CommitmentRemaining == 2 && reloaded.Commitment.DevelopmentBudget == memory.Commitment.DevelopmentBudget
            && reloaded.SchemaVersion == ElderInstitutionMemory.StorageSchemaVersion, "a commitment survives a save round trip");

        // A hold with different submitted values compiles exactly like explicitly resubmitting the committed values.
        var mid = SaveCodec.Save(acting.World);
        if (ElderCommitments.CanHold(acting.World, acting.World.Camps.Single(c => c.Id == camp)))
        {
            var held = new ElderModel9Environment(); held.Reset(mid, true);
            var explicitly = new ElderModel9Environment(); explicitly.Reset(mid, true);
            var k = Memory(held).Commitment;
            var heldStep = held.Step(new Dictionary<int, ElderDirective> { [camp] = Directive(0, .1, .1, .6, .9) });
            var explicitStep = explicitly.Step(new Dictionary<int, ElderDirective> { [camp] = new ElderDirective
            {
                SchemaVersion = ElderModel9Contract.ActionSchemaVersion, RetainedFieldOperation = k.RetainedFieldOperation,
                RetainedHerdFraction = k.RetainedHerdFraction, DevelopmentBudget = k.DevelopmentBudget, FieldDevelopmentShare = k.FieldDevelopmentShare,
                FishingDevelopmentShare = k.FishingDevelopmentShare, ClothingRoute = k.ClothingRoute, ShelterRoute = k.ShelterRoute,
                MovementCandidate = ElderModel9Contract.NativeMovement, CommitTurns = 1 } });
            var vh = heldStep.Villages.Single(v => v.Camp == camp); var ve = explicitStep.Villages.Single(v => v.Camp == camp);
            Check(vh.Population == ve.Population && Math.Abs(vh.Welfare - ve.Welfare) <= 1e-9 * Math.Max(1, ve.Welfare),
                "a hold re-emits the committed capital controls");
            Check(Memory(held).CommitmentRemaining == 1 && Memory(held).HasCommitment && !Memory(explicitly).HasCommitment,
                "a hold uses one turn; deciding for one turn ends the commitment");
            var lastHold = held.Step(new Dictionary<int, ElderDirective> { [camp] = Directive(0, .1, .1, .6, .9) });
            bool expiredCleanly = !Memory(held).HasCommitment && Memory(held).CommitmentRemaining == 0;
            Check(expiredCleanly || Memory(held).LastCommitmentBreak != null, "the last hold expires the commitment");
            var afterExpiry = held.Step(new Dictionary<int, ElderDirective> { [camp] = Directive(0, .1, .1, .6, .9) });
            Check(Intervened(afterExpiry, "no active commitment"), "a hold without a commitment falls back to the submitted controls, logged");
            ElderModel9Environment.VerifyReplay(held.Replay); checks++;
        }

        // Forced break: a survival shortfall refuses the hold and records why.
        var breaking = new ElderModel9Environment(); breaking.Reset(mid, true);
        var breakingCamp = breaking.World.Camps.Single(c => c.Id == camp);
        // An earlier break reason (relocation, fission, capability change) takes precedence over the deficit; the
        // trajectory decides which applies, so the expected reason is the one BreakReason reports.
        string prior = ElderCommitments.BreakReason(breaking.World, breakingCamp, Memory(breaking));
        breakingCamp.DeficitTurns = 1;
        if (Memory(breaking).HasCommitment)
        {
            string reason = prior ?? "survival shortfall";
            Check(ElderCommitments.BreakReason(breaking.World, breakingCamp, Memory(breaking)) == reason, "a survival shortfall is a break reason");
            var broken = breaking.Step(new Dictionary<int, ElderDirective> { [camp] = Directive(0, .1, .1, .6, .9) });
            Check(Intervened(broken, reason) && Memory(breaking).LastCommitmentBreak == reason && !Memory(breaking).HasCommitment,
                "a survival shortfall force-breaks the commitment, logged and recorded");
        }
        // Spans outside 0-6 are repaired; schema-8 directives never hold.
        var odd = new ElderModel9Environment(); odd.Reset(mid, true);
        var repaired = odd.Step(new Dictionary<int, ElderDirective> { [camp] = Directive(9, .8, 1, .1, .5) });
        Check(Intervened(repaired, "commitment span"), "an out-of-range span is repaired and logged");
        var schema8 = Directive(0, .8, 1, .1, .5); schema8.SchemaVersion = ElderModel9Contract.PreviousActionSchemaVersion;
        var old = new ElderModel9Environment(); old.Reset(mid, true);
        var oldStep = old.Step(new Dictionary<int, ElderDirective> { [camp] = schema8 });
        Check(!Intervened(oldStep, "hold") && !Memory(old).HasCommitment, "a schema-8 directive decides for one turn only");
    }

    // Contract 10 movement: regional rows (deterministic, the same with or without learned movement, remembered rows
    // without live forecasts), a region choice sets an intent and steps one tile, keep-intent continues, native cancels,
    // the intent survives a save, schema-9 directives cannot name regions, and replay stays exact.
    private static void MovementChecks(byte[] snapshot)
    {
        const int set = ElderModel9Encoder.RegionSet, width = ElderMovement.RowWidth;
        var env = new ElderModel9Environment(); var step = env.Reset(snapshot, true);
        var twin = new ElderModel9Environment(); var twinStep = twin.Reset(snapshot, true);
        var off = new ElderModel9Environment(); var offStep = off.Reset(snapshot, false);
        Check(step.Villages.All(v => v.Observation.Sets[set].SequenceEqual(twinStep.Villages.Single(t => t.Camp == v.Camp).Observation.Sets[set])),
            "region rows are deterministic");
        Check(step.Villages.All(v => v.Observation.Sets[set].SequenceEqual(offStep.Villages.Single(t => t.Camp == v.Camp).Observation.Sets[set])),
            "region rows do not depend on whether movement is learned");
        Check(offStep.Villages.All(v => v.Observation.RegionMovementMask[0] && !v.Observation.RegionMovementMask.Skip(1).Any(x => x)),
            "without learned movement only native is legal");
        Check(step.Villages.All(v => v.Observation.RowPresent[set][0] && v.Observation.Sets[set][0] == 1), "region row 0 is the current tile");
        int remembered = 0; bool live = true;
        foreach (var v in step.Villages)
            for (int r = 0; r < ElderMovement.MaxRegions; r++)
            {
                if (!v.Observation.RowPresent[set][r] || v.Observation.Sets[set][r * width + 2] > .5) continue;
                remembered++;
                for (int c = 15; c <= 19; c++) live &= v.Observation.Sets[set][r * width + c] == 0;
            }
        Check(live, "remembered region rows carry no live forecast (" + remembered + " remembered rows)");

        // A legal region row away from the current tile, preferring a target two or more tiles away.
        var options = step.Villages.SelectMany(v => Enumerable.Range(0, ElderMovement.MaxRegions)
                .Where(r => v.Observation.RegionMovementMask[2 + r] && v.Observation.Sets[set][r * width] < .5)
                .Select(r => (v.Camp, Row: r, Distance: (int)v.Observation.Sets[set][r * width + 4])))
            .OrderByDescending(o => Math.Min(2, o.Distance)).ThenBy(o => o.Camp).ThenBy(o => o.Row).ToList();
        Check(options.Count > 0, "some village has a legal region move");
        var (camp, row, distance) = options[0];
        ElderDirective Move(int code, int schema = ElderModel9Contract.ActionSchemaVersion)
            => new ElderDirective { SchemaVersion = schema, MovementCandidate = code };
        Camp Of(ElderModel9Environment e) => e.World.Camps.Single(c => c.Id == camp);
        var mid = SaveCodec.Save(env.World);
        int origin = Of(env).Tile;
        var moved = env.Step(new Dictionary<int, ElderDirective> { [camp] = Move(ElderMovement.RegionBase + row) });
        var memory = Of(env).ElderMemory;
        Check(moved.Villages.Single(v => v.Camp == camp).Interventions.All(i => i.Control != "movement-candidate"), "a legal region choice is accepted");
        if (distance >= 2)
        {
            Check(memory.HasIntent && memory.IntentTurns == 1, "a region choice sets a destination intent");
            Check(env.World.Map.Distance(Of(env).Tile, memory.IntentTile) == distance - 1, "the camp steps one tile toward its target");
            var obs = moved.Villages.Single(v => v.Camp == camp).Observation;
            bool feasible = obs.RegionMovementMask[1];
            Check(!feasible || Enumerable.Range(0, ElderMovement.MaxRegions).Any(r => obs.RowPresent[set][r] && obs.Sets[set][r * width + 1] > .5),
                "a keepable intent is shown on its region row");
            var reloaded = SaveCodec.Load(SaveCodec.Save(env.World)).Camps.Single(c => c.Id == camp).ElderMemory;
            Check(reloaded.SchemaVersion == ElderInstitutionMemory.StorageSchemaVersion && reloaded.HasIntent && reloaded.IntentTile == memory.IntentTile
                && reloaded.IntentTurns == memory.IntentTurns, "an intent survives a save round trip");
            if (feasible)
            {
                int target = memory.IntentTile, before = env.World.Map.Distance(Of(env).Tile, target);
                env.Step(new Dictionary<int, ElderDirective> { [camp] = Move(ElderMovement.KeepIntent) });
                int after = env.World.Map.Distance(Of(env).Tile, target);
                Check(after == before - 1 && (after == 0 ? !Of(env).ElderMemory.HasIntent : Of(env).ElderMemory.IntentTurns == 2),
                    "keeping the intent continues toward the target");
            }
            env.Step(new Dictionary<int, ElderDirective> { [camp] = Move(ElderModel9Contract.NativeMovement) });
            Check(!Of(env).ElderMemory.HasIntent, "a native decision cancels the intent");
        }
        else Check(!memory.HasIntent && Of(env).Tile != origin && env.World.Map.Distance(origin, Of(env).Tile) == 1,
            "an adjacent region move arrives and leaves no intent");
        ElderModel9Environment.VerifyReplay(env.Replay); checks++;
        Console.WriteLine("  movement fixture: camp " + camp + ", region row " + row + " at distance " + distance + ", " + remembered + " remembered rows, "
            + step.Villages.Sum(v => v.Observation.RowPresent[set].Count(x => x)) + " rows over " + step.Villages.Count + " villages");

        var old = new ElderModel9Environment(); old.Reset(mid, true);
        var repaired = old.Step(new Dictionary<int, ElderDirective> { [camp] = Move(ElderMovement.RegionBase + row, ElderModel9Contract.CommitmentActionSchemaVersion) });
        Check(repaired.Villages.Single(v => v.Camp == camp).Interventions.Any(i => i.Reason.Contains("schema 10")) && !Of(old).ElderMemory.HasIntent,
            "a schema-9 directive naming a region row is repaired to stay, logged");
    }

    // Contract 11 trade posture: a native posture is exact, withholding never gives, a zero export share makes nothing
    // to order, relative values scale the trading valuation but never the true one, the village trade utility is the
    // recorded realised utility, and earlier schemas carry no posture.
    private static void TradePostureChecks(byte[] snapshot)
    {
        TradePosture Posture(double value = 1, int gate = ElderTrade.Make, double share = -1, SimulationConfig cfg = null)
            => new TradePosture { Value = Enumerable.Repeat(value, ElderTrade.Groups.Length).ToArray(),
                Gate = Enumerable.Repeat(gate, ElderTrade.Groups.Length).ToArray(), ExportShare = share >= 0 ? share : cfg?.TradeExportLabourShare ?? 0 };
        // Play the heuristic until the world trades, keeping the boundary state.
        var env = new ElderModel9Environment(); env.Reset(snapshot, false);
        WorldState w = null; TradePlan native = null;
        for (int t = 0; t < 12 && (native == null || native.Agreements.Count == 0); t++)
        {
            env.Step(new Dictionary<int, ElderDirective>());
            w = env.World; native = Trade.Plan(w);
        }
        Check(native != null && native.Agreements.Count > 0, "the seed-17 fixture trades within 12 turns");
        string Canon(TradePlan p) => string.Join(";", p.Agreements.Select(a => a.Requester + ">" + a.Responder + ":" + a.Want + "/" + a.Give + ":" + a.WantQuantity.ToString("R") + "/" + a.GiveQuantity.ToString("R")));
        var traders = native.Agreements.SelectMany(a => new[] { a.Requester, a.Responder }).Distinct().OrderBy(id => id).ToArray();
        var allNative = traders.ToDictionary(id => id, id => Posture(cfg: w.Config));
        Check(Canon(Trade.Plan(w, null, null, allNative)) == Canon(native), "a native posture reproduces the native handshake exactly");
        int giver = native.Agreements[0].Responder;
        var withheld = Trade.Plan(w, null, null, new Dictionary<int, TradePosture> { [giver] = Posture(gate: ElderTrade.Withhold, cfg: w.Config) });
        Check(withheld.Agreements.All(a => a.Responder != giver && a.Requester != giver || a.Requester == giver && a.GiveQuantity <= 0),
            "a village withholding every group gives nothing");
        var stockOnly = Trade.Plan(w, null, null, traders.ToDictionary(id => id, id => Posture(share: 0, cfg: w.Config)));
        Check(stockOnly.Agreements.All(a => a.WantMade <= 1e-12 && a.GiveMade <= 1e-12), "a zero export share makes nothing to order");
        var camp = w.Camps.Single(c => c.Id == giver);
        var plain = Trade.Value(w, camp); var raised = Trade.Value(w, camp, 0, null, null, Posture(4, cfg: w.Config));
        var good = Trade.Registry.First(g => plain.Active[(int)g.Id] && plain.Marginal(g.Id, plain.Position(g.Id)) > 0).Id;
        double x = plain.Position(good);
        Check(Math.Abs(raised.Marginal(good, x) - plain.Marginal(good, x)) <= 1e-12 &&
            ValueCurveMarginal(raised.TrueW(good), x) == ValueCurveMarginal(plain.W(good), x) &&
            Math.Abs(ValueCurveMarginal(raised.W(good), x) - 4 * ValueCurveMarginal(plain.TrueW(good), x)) <= 1e-9 * Math.Max(1, ValueCurveMarginal(plain.W(good), x)),
            "a relative value scales the trading valuation, never the true one realised utility is measured on");
        // The village trade utility reported for a step is the turn's recorded realised utility.
        var stepped = new ElderModel9Environment(); stepped.Reset(SaveCodec.Save(w), false);
        var s = stepped.Step(new Dictionary<int, ElderDirective>());
        var record = stepped.World.History.Last();
        Check(s.Villages.All(v => Math.Abs(v.TradeUtility - ElderTrade.RealisedUtility(record, v.Camp)) <= 1e-12)
            && s.Villages.Sum(v => Math.Abs(v.TradeUtility)) > 0, "villages report their realised trade utility");
        // Schema 10 carries no posture; out-of-range postures are repaired and logged.
        var old = new ElderDirective { SchemaVersion = ElderModel9Contract.MovementActionSchemaVersion, TradeValue = new[] { 4.0, 4, 4, 4, 4, 4, 4 } };
        Check(ElderTrade.From(old, w.Config) == null, "a schema-10 directive trades natively");
        var bad = new ElderDirective { SchemaVersion = ElderModel9Contract.ActionSchemaVersion, TradeValue = new[] { 9.0, .1, double.NaN, 1, 1, 1, 1 },
            TradeGate = new[] { 5, 0, 1, 2, 2, 2, 2 }, TradeExportShare = 3 };
        var interventions = new List<ElderIntervention>();
        var repaired = ElderDirectiveValidation.Validate(bad, new ElderObservation(), interventions);
        Check(repaired.TradeValue[0] == ElderTrade.MaxValue && repaired.TradeValue[1] == ElderTrade.MinValue && repaired.TradeValue[2] == 1
            && repaired.TradeGate[0] == ElderTrade.Make && repaired.TradeExportShare == 1 && interventions.Count(i => i.Control.StartsWith("trade")) >= 5,
            "out-of-range postures are repaired and logged");
        var posture = ElderTrade.From(bad, w.Config);
        Check(posture.Value[0] == ElderTrade.MaxValue && posture.Gate[0] == ElderTrade.Make && posture.ExportShare == 1,
            "the handshake applies the same clamps as the compiler");
        Console.WriteLine("  trade fixture: " + native.Agreements.Count + " agreements among " + traders.Length + " villages at turn " + w.Turn);
    }

    // Search forks: every branch restored from a fork replays the live path exactly, a branch never leaks into the
    // next, and a fork does not change the path (an unforked twin stays in step).
    private static void ForkChecks(byte[] snapshot)
    {
        var live = new ElderModel9Environment(); var twin = new ElderModel9Environment();
        live.Reset(snapshot, true); twin.Reset(snapshot, true);
        for (int t = 0; t < 2; t++) { live.Step(new Dictionary<int, ElderDirective>()); twin.Step(new Dictionary<int, ElderDirective>()); }
        var fork = live.Fork(out var decision);
        Check(!twin.Forked && live.Forked && decision.Villages.Count > 0 && decision.Villages.All(v => v.Observation != null),
            "a fork returns the decision step with every village observed");
        List<string> Run(ElderModel9Environment env, int turns, Dictionary<int, ElderDirective> first = null)
        {
            var prints = new List<string>();
            for (int t = 0; t < turns; t++) prints.Add(env.Step(t == 0 && first != null ? first : new Dictionary<int, ElderDirective>()).Fingerprint);
            return prints;
        }
        var path = Run(live, 3);
        Check(path.SequenceEqual(Run(twin, 3)), "a fork does not change the live path");
        var restored = live.Restore(fork);
        Check(restored.Fingerprint == decision.Fingerprint && restored.Villages.Count == decision.Villages.Count &&
              restored.Villages.All(v => v.Observation.Core.SequenceEqual(decision.Villages.Single(d => d.Camp == v.Camp).Observation.Core)),
            "a restore reproduces the fork's observations");
        Check(Run(live, 3).SequenceEqual(path), "a restored branch replays the live path exactly");
        var camp = decision.Villages.First(v => v.Observation.EconomyMixMask.Count(x => x) >= 2).Camp;
        var deviation = new Dictionary<int, ElderDirective>
        {
            [camp] = new ElderDirective { SchemaVersion = ElderModel9Contract.ActionSchemaVersion, DevelopmentBudget = 1, FieldDevelopmentShare = 0,
                FishingDevelopmentShare = 0, MovementCandidate = ElderModel9Contract.NativeMovement, ServiceLevels = new[] { 1.0, 1, 1, 1, 1, 1 } }
        };
        live.Restore(fork);
        Check(!Run(live, 1, deviation).SequenceEqual(path.Take(1)), "a deviating branch diverges");
        live.Restore(fork);
        Check(Run(live, 3).SequenceEqual(path), "a branch does not leak into the next restore");
    }

    // Specialist share (reporting only): a unit share weighted by realised goods output, zero in a food-only economy,
    // positive once crafts produce, zero for a nominal receipt without output, and invisible to the world's path.
    private static void SpecialistShareChecks(byte[] snapshot)
    {
        var cfg = SimulationConfig.Small(8);
        var empty = new TurnRecord();
        Check(ElderSpecialistShare.Measure(empty, cfg, 1, 20, 20, out double g0) == 0 && g0 == 0, "no receipts give a zero share");
        var nominal = new TurnRecord(); nominal.Production.Add(new ProductionReceipt { Camp = 1, Good = CraftGood.Pots, Output = 0, WorkerIntervals = 50 });
        Check(ElderSpecialistShare.Measure(nominal, cfg, 1, 20, 20, out _) == 0, "a specialist producing nothing does not count");
        var made = new TurnRecord();
        made.Production.Add(new ProductionReceipt { Camp = 1, Good = CraftGood.Pots, Output = 3, WorkerIntervals = 20 * cfg.DaysPerYear / cfg.BaseDays });
        made.Production.Add(new ProductionReceipt { Camp = 1, Good = CraftGood.HousingMaterial, Output = 2, WorkerIntervals = 20 * cfg.DaysPerYear / cfg.BaseDays });
        made.Production.Add(new ProductionReceipt { Camp = 2, Good = CraftGood.Pots, Output = 9, WorkerIntervals = 1e9 });
        double share = ElderSpecialistShare.Measure(made, cfg, 1, 20, 20, out double g1);
        Check(Math.Abs(share - 2.0 / 20) <= 1e-12 && g1 == 3, "two specialist-lifetimes in twenty residents: share .1, intermediate excluded from output");
        Check(ElderSpecialistShare.Measure(made, cfg, 2, 20, 20, out _) == 1, "the share is clamped to one");

        List<ElderModel9Step> Run(byte[] start, bool report, int turns, Action<WorldState> adjust = null)
        {
            bool before = ElderSpecialistShare.Report; ElderSpecialistShare.Report = report;
            try
            {
                var env = new ElderModel9Environment(); env.Reset(start, false); adjust?.Invoke(env.World);
                var steps = new List<ElderModel9Step>();
                for (int t = 0; t < turns; t++)
                {
                    var s = env.Step(new Dictionary<int, ElderDirective>());
                    var record = env.World.History.Last();
                    foreach (var v in s.Villages.Where(v => v.Years > 0))
                    {
                        bool produced = record.Production.Any(r => r.Camp == v.Camp && r.Output > 0);
                        Check(v.SpecialistShare >= 0 && v.SpecialistShare <= 1 && v.GoodsOutput >= 0, "the specialist share is a unit share (camp " + v.Camp + ")");
                        Check(!report || produced == (v.SpecialistShare > 0), "a share is positive exactly when the village's crafts produced (camp " + v.Camp + ")");
                    }
                    steps.Add(s);
                }
                return steps;
            }
            finally { ElderSpecialistShare.Report = before; }
        }
        var on = Run(snapshot, true, 4); var off = Run(snapshot, false, 4);
        Check(on.Select(s => s.Fingerprint).SequenceEqual(off.Select(s => s.Fingerprint)), "reporting the specialist share leaves every fingerprint unchanged");
        Check(off.All(s => s.Villages.All(v => v.SpecialistShare == 0 && v.GoodsOutput == 0)), "reporting off leaves the fields zero");
        var crafted = on.SelectMany(s => s.Villages).Where(v => v.SpecialistShare > 0).ToList();
        Check(crafted.Count > 0 && crafted.All(v => v.GoodsOutput > 0), "crafts that produce goods give a positive share");
        // Food-only economy: every recipe rate zero or negligible (set on the live fixture world), so no village can make goods.
        var foodSteps = Run(snapshot, true, 3, w =>
        {
            var n = w.Config.Neolithic;
            n.HandToolsOutput = n.PotsOutput = n.FarmToolsOutput = n.ToolsOutput = 0;
            // Validation requires these to be positive; at 1e-200 any staffed labour makes less than the 1e-12 receipt floor.
            n.WeaverFabricOutputPerBaseDays = n.BuilderHousingOutputPerBaseDays = n.LightHousingBuilderOutputPerBaseDays = 1e-200;
        });
        var rows = foodSteps.SelectMany(s => s.Villages).Where(v => v.Years > 0 && !v.Terminated).ToList();
        Check(rows.Count > 0 && rows.All(v => v.SpecialistShare == 0 && v.GoodsOutput == 0) && rows.Any(v => v.Nutrition > 0),
            "a pure food economy has a zero specialist share");
        Console.WriteLine("  specialist share: " + crafted.Count + " producing village-steps, mean share " +
            crafted.Average(v => v.SpecialistShare).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + ", max " +
            crafted.Max(v => v.SpecialistShare).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture));
    }

    private static double ValueCurveMarginal(ValueCurve.Segment[] segments, double x) => ValueCurve.Marginal(segments, x);

    public static int Run()
    {
        checks = 0;
        var snapshot = ElderModel9Tools.Opening(17, 8, out _);
        var teacher = new ElderModel9Environment(); var acting = new ElderModel9Environment();
        var reset = teacher.Reset(snapshot, true); acting.Reset(snapshot, true);
        Check(reset.Villages.Count > 0 && reset.Villages.All(v => v.Observation != null), "reset observes every resident-economy village");
        var first = reset.Villages[0].Observation;
        Check(first.Core.Length == ElderModel9Contract.CoreWidth && ElderModel9Encoder.CoreFeatureNames().Length == ElderModel9Contract.CoreWidth,
            "core width matches the registered feature names");
        for (int s = 0; s < ElderModel9Contract.Sets.Length; s++)
            Check(first.Sets[s].Length == ElderModel9Contract.Sets[s].Rows * ElderModel9Contract.Sets[s].Width && first.RowPresent[s].Length == ElderModel9Contract.Sets[s].Rows,
                "set " + ElderModel9Contract.Sets[s].Name + " has its declared shape");
        Check(first.MovementMask[0], "stay is always legal");

        ElderModel9Step lastActing = null;
        for (int t = 0; t < 3; t++)
        {
            var a = teacher.Step(new Dictionary<int, ElderDirective>());
            var labelled = a.Villages.Where(v => v.Teacher != null).ToList();
            Check(labelled.Count > 0, "teacher step labels its villages");
            Check(labelled.All(v => v.Teacher.Action.ServiceLevels.Length == ElderModel9Contract.GoodsServices.Length
                && v.Teacher.Action.ServiceLevels.All(x => x >= 0 && x <= 1)), "service-level labels are unit shares");
            var directives = labelled.ToDictionary(v => v.Camp, v =>
            {
                var d = v.Teacher.Action.Copy(); d.MovementCandidate = ElderModel9Contract.NativeMovement; d.FissionPolicy = -1; return d;
            });
            var b = acting.Step(directives); lastActing = b;
            Check(a.Villages.Count == b.Villages.Count, "teacher-as-action keeps the same villages at step " + t);
            foreach (var va in a.Villages)
            {
                var vb = b.Villages.Single(v => v.Camp == va.Camp);
                Check(va.Population == vb.Population && Math.Abs(va.Welfare - vb.Welfare) <= 1e-6 * Math.Max(1, va.Welfare),
                    "the heuristic's labels compile to the heuristic's outcome (camp " + va.Camp + ", step " + t + ")");
            }
            Check(teacher.World.Camps.Sum(teacher.World.LinkedPopulation) == acting.World.Camps.Sum(acting.World.LinkedPopulation), "worlds stay in step");
        }
        ElderModel9Environment.VerifyReplay(acting.Replay); checks++;
        ElderModel9Environment.VerifyReplay(teacher.Replay); checks++;

        // Contract 8: the fishing share of the Development budget staffs whole Fishers beyond the survival frontier.
        int fishingFeature = Array.IndexOf(ElderModel9Encoder.CoreFeatureNames(), "fishing-development-available");
        var fishingCamps = lastActing.Villages.Where(v => v.Observation != null && !v.Terminated && v.Observation.Core[fishingFeature] > .5
            && v.Observation.EconomyMixMask[2]).Select(v => v.Camp).ToList();
        Check(fishingCamps.Count > 0, "the seed-17 fixture has a village with resident fishing");
        int fishingCamp = fishingCamps[0];
        var fishingStep = acting.Step(new Dictionary<int, ElderDirective>
        {
            // Development is the lowest service class: lower the service levels (reserve gathering included) so
            // residents are left for it.
            [fishingCamp] = new ElderDirective { SchemaVersion = ElderModel9Contract.ActionSchemaVersion, DevelopmentBudget = .2,
                FieldDevelopmentShare = .5, FishingDevelopmentShare = 1, MovementCandidate = ElderModel9Contract.NativeMovement,
                ServiceLevels = new[] { .2, .2, .2, .2, .2, .2 } }
        });
        var fished = fishingStep.Villages.Single(v => v.Camp == fishingCamp);
        Check(fished.Teacher.Action.FishingDevelopmentShare > .5 && fished.Staffing.Contains(WorkforceRoles.Fisher),
            "a fishing-only Development directive staffs development Fishers (staffing " + fished.Staffing + ")");
        CommitmentChecks(acting, fishingStep);
        MovementChecks(snapshot);
        TradePostureChecks(snapshot);
        ForkChecks(snapshot);
        SpecialistShareChecks(snapshot);
        // Capability fixtures: a withheld technology is never granted, and the training-only mask is neither saved
        // nor hashed.
        int herdingOnly = 1 << (int)Technology.Cultivation | 1 << (int)Technology.Watercraft;
        var pastoral = SaveCodec.Load(ElderModel9Environment.GenerateOpening(17, 8, out _, 40, herdingOnly));
        Check(pastoral.Societies.All(s => !s.Technologies[(int)Technology.Cultivation] && !s.Technologies[(int)Technology.Watercraft])
            && pastoral.Societies.Any(s => s.Technologies[(int)Technology.AnimalHusbandry]), "a herding-only fixture grants husbandry and nothing else");
        Check(pastoral.Config.TrainingWithheldTechnologies == 0, "the fixture mask is never saved");
        // Animal textile need: herding villages in portable shelter pay tent fabric with their clothing; the lever
        // survives a save, switching it off restores the clothing-only rule and villages with fixed housing are exempt.
        var herders = pastoral.Camps.Where(c => c.Population > 0 && CultivationEconomy.HusbandryEnabled(c)).ToList();
        Check(pastoral.Config.AnimalTextileNeedFirst && herders.Count > 0 && herders.All(c => NeolithicProduction.AnimalTextileNeedApplies(pastoral, c)),
            "herding villages in portable shelter carry the animal textile need");
        double years = pastoral.Config.TurnDays / pastoral.Config.DaysPerYear;
        Check(herders.All(c => NeolithicProduction.PortableShelterFabricNeed(pastoral, c, c.Population, years, pastoral.Days + pastoral.Config.TurnDays) >= 0),
            "the portable shelter fabric need is never negative");
        pastoral.Config.AnimalTextileNeedFirst = false;
        Check(herders.All(c => !NeolithicProduction.AnimalTextileNeedApplies(pastoral, c)), "the lever off restores the clothing-only need");
        pastoral.Config.AnimalTextileNeedFirst = true;
        Check(pastoral.Config.HerdCullLimitPerTurn == .5, "new worlds limit a herd cull to half the herd per turn");
        var settled = herders[0]; settled.IsVillage = true;
        Check(!NeolithicProduction.AnimalTextileNeedApplies(pastoral, settled), "a village with fixed settlement is exempt");
        settled.IsVillage = false;
        var hashed = SimulationConfig.Small(8); string before = SaveCodec.ConfigurationHash(hashed);
        hashed.TrainingWithheldTechnologies = herdingOnly;
        Check(SaveCodec.ConfigurationHash(hashed) == before, "the fixture mask does not enter the configuration hash");
        var schema7 = ElderDirectiveValidation.Validate(new ElderDirective { SchemaVersion = 7, DevelopmentBudget = .2, FishingDevelopmentShare = 1 },
            new ElderObservation(), new List<ElderIntervention>());
        Check(schema7.FishingDevelopmentShare == 0, "schema-7 directives have no fishing development");
        ElderModel9Environment.VerifyReplay(acting.Replay); checks++;

        // Malformed actions are repaired with logged interventions, never rejected silently.
        var bad = new ElderDirective { SchemaVersion = ElderModel9Contract.ActionSchemaVersion, ServiceLevels = new[] { 2.0, -1, double.NaN, .5, .5, .5 },
            ClothingRoute = 9, MovementCandidate = 99 };
        var interventions = new List<ElderIntervention>();
        var legal = ElderDirectiveValidation.Validate(bad, new ElderObservation(), interventions);
        Check(legal.ServiceLevels.All(x => x >= 0 && x <= 1) && legal.ClothingRoute == -1 && legal.MovementCandidate == -1 && interventions.Count >= 4,
            "out-of-range service levels, routes and movement are repaired and logged");
        var shortVector = ElderDirectiveValidation.Validate(new ElderDirective { SchemaVersion = 7, ServiceLevels = new[] { .5 } }, new ElderObservation(), new List<ElderIntervention>());
        Check(shortVector.ServiceLevels == null, "a wrong-length service vector falls back to native levels");

        // NPY shards round-trip their header.
        string dir = Path.Combine(Path.GetTempPath(), "anthropocene-model9-npy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "x.npy");
            NpyWriter.Write(path, new float[] { 1, 2, 3, 4, 5, 6 }, 2, 3);
            var bytes = File.ReadAllBytes(path);
            int headerLength = bytes[8] | bytes[9] << 8;
            string header = System.Text.Encoding.ASCII.GetString(bytes, 10, headerLength);
            Check(bytes[0] == 0x93 && (10 + headerLength) % 64 == 0 && header.Contains("'shape': (2, 3)") && bytes.Length == 10 + headerLength + 24,
                "NPY header is aligned and declares the shape");
        }
        finally { Directory.Delete(dir, true); }
        Console.WriteLine("PASS: " + checks + " model9 contract-12 assertions");
        return checks;
    }
}
