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
        ElderDirective Directive(int span, double field, double herd, double budget, double share) => new ElderDirective
        {
            SchemaVersion = ElderModel9Contract.ActionSchemaVersion, RetainedFieldOperation = field, RetainedHerdFraction = herd,
            DevelopmentBudget = budget, FieldDevelopmentShare = share, MovementCandidate = ElderModel9Contract.NativeMovement, CommitTurns = span
        };
        // The first live village whose three-turn commitment can then be held (probed on a fork), so the hold, expiry
        // and forced-break assertions fire whatever the trajectory (the Save15 rules change which villages relocate).
        var live = last.Villages.Where(v => v.Observation != null && !v.Terminated).Select(v => v.Camp).ToList();
        var origin = SaveCodec.Save(acting.World);
        int camp = live.FirstOrDefault(id =>
        {
            var probe = new ElderModel9Environment(); probe.Reset(origin, true);
            probe.Step(new Dictionary<int, ElderDirective> { [id] = Directive(3, .8, 1, .1, .5) });
            var pc = probe.World.Camps.SingleOrDefault(c => c.Id == id);
            return pc != null && ElderCommitments.CanHold(probe.World, pc);
        }, live[0]);
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
        bool canHold = ElderCommitments.CanHold(acting.World, acting.World.Camps.Single(c => c.Id == camp));
        Check(canHold, "fixture: a village can hold its commitment");
        if (canHold)
        {
            var held = new ElderModel9Environment(); held.Reset(mid, true);
            var explicitly = new ElderModel9Environment(); explicitly.Reset(mid, true);
            var k = Memory(held).Commitment;
            var heldStep = held.Step(new Dictionary<int, ElderDirective> { [camp] = Directive(0, .1, .1, .6, .9) });
            var heldDiagnostic = heldStep.DecisionDiagnostics[camp];
            Check(heldDiagnostic.ActiveMask.Strategic.All(x => !x) && !heldDiagnostic.ActiveMask.ClothingRoute &&
                  !heldDiagnostic.ActiveMask.ShelterRoute && heldDiagnostic.ActiveMask.Commit,
                "commitment hold suppresses capital-control activity while preserving the hold action mask");
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
            // Rules off: the fixture as before, where the deficit is the only break reason.
            if (!rulesOn) Check(prior == null, "rules off: the deficit is the only break reason");
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
        var immediate = new ElderModel9Environment(); var immediateReset = immediate.Reset(snapshot, true);
        int immediateCamp = immediateReset.Villages.First(v => !v.Terminated).Camp;
        var immediateStep = immediate.Step(new Dictionary<int, ElderDirective>
            { [immediateCamp] = new ElderDirective { MovementCandidate = ElderModel9Contract.NativeMovement } });
        var immediateFork = immediate.Fork(out var immediateForkStep);
        Check(immediateStep.Villages.Single(v => v.Camp == immediateCamp).DecisionDiagnostics != null &&
              immediateForkStep.Villages.Single(v => v.Camp == immediateCamp).DecisionDiagnostics?.TrajectoryId ==
              immediateStep.Villages.Single(v => v.Camp == immediateCamp).DecisionDiagnostics.TrajectoryId,
            "an immediate fork preserves same-step per-village decision diagnostics");
        Check(immediateForkStep.DiagnosticSchemaVersion == 2 &&
              immediateForkStep.DecisionDiagnostics.Count > 0,
            "fork-returned steps retain the schema version for their restored diagnostic sidecars");
        var preservedTrajectory = immediateFork.DecisionDiagnostics[immediateCamp].TrajectoryId;
        var replayTrajectory = immediate.Replay.Steps.Last().DecisionDiagnostics[immediateCamp].TrajectoryId;
        immediateStep.DecisionDiagnostics[immediateCamp].TrajectoryId = "mutated-step";
        immediateForkStep.DecisionDiagnostics[immediateCamp].TrajectoryId = "mutated-result";
        immediateForkStep.Villages.Single(v => v.Camp == immediateCamp).DecisionDiagnostics.TrajectoryId = "mutated-village";
        var isolatedRestore = immediate.Restore(immediateFork);
        Check(immediateFork.DecisionDiagnostics[immediateCamp].TrajectoryId == preservedTrajectory &&
              isolatedRestore.DecisionDiagnostics[immediateCamp].TrajectoryId == preservedTrajectory &&
              immediate.Replay.Steps.Last().DecisionDiagnostics[immediateCamp].TrajectoryId == replayTrajectory,
            "step, village and fork-result mutation cannot alter saved fork/replay evidence");

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
        Check(restored.DecisionDiagnostics.Keys.OrderBy(x => x).SequenceEqual(decision.DecisionDiagnostics.Keys.OrderBy(x => x)) &&
              restored.DecisionDiagnostics.All(p => p.Value.TrajectoryId == decision.DecisionDiagnostics[p.Key].TrajectoryId) &&
              restored.Villages.All(v => v.DecisionDiagnostics?.TrajectoryId == decision.Villages.Single(d => d.Camp == v.Camp).DecisionDiagnostics?.TrajectoryId),
            "restore preserves the prior-decision sidecar alignment");
        Check(restored.DiagnosticSchemaVersion == 2,
            "restored step carries the schema version for its diagnostic sidecars (schema=" + restored.DiagnosticSchemaVersion + ")");
        Check(Run(live, 3).SequenceEqual(path), "a restored branch replays the live path exactly");
        var camp = decision.Villages.First(v => v.Observation.EconomyMixMask.Count(x => x) >= 2).Camp;
        var branchDirective = new Dictionary<int, ElderDirective> { [camp] = new ElderDirective { MovementCandidate = ElderModel9Contract.NativeMovement } };
        live.Restore(fork); var branchOne = live.Step(branchDirective).DecisionDiagnostics[camp].TrajectoryId;
        live.Restore(fork); var branchTwo = live.Step(branchDirective).DecisionDiagnostics[camp].TrajectoryId;
        Check(branchOne != branchTwo, "each repeated restore receives a distinct deterministic branch trajectory identity");
        live.Restore(fork);
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

    // The whole contract runs twice: with the Save15 rules off (the fixture and every assertion as before
    // Docs/SettlingGrace.md) and with the new-world defaults (settling grace and plant tents on).
    public static int Run()
    {
        checks = 0;
        int legacy = RunWith(false);
        Console.WriteLine("  rules off: " + legacy + " assertions");
        int current = RunWith(true) - legacy;
        Console.WriteLine("  rules on: " + current + " assertions");
        Console.WriteLine("PASS: " + checks + " model9 contract-12 assertions");
        return checks;
    }
    private static bool rulesOn;
    private static void EconomicDiagnosticsChecks(byte[] snapshot)
    {
        bool originalCapture = ResolutionMetrics.CaptureDemandAllocations;
        var on = new ElderModel9Environment { EconomicDiagnosticsEnabled = true };
        var off = new ElderModel9Environment();
        var reset = on.Reset(snapshot, true); off.Reset(snapshot, true);
        Check(reset.DiagnosticSchemaVersion == 0 && reset.EconomicDiagnostics.Count == 0, "economic reset has no preceding receipts");
        Check(ResolutionMetrics.CaptureDemandAllocations == originalCapture, "model9 capture does not change the process-global demand switch");
        var actions = reset.Villages.ToDictionary(v => v.Camp, v => new ElderDirective { MovementCandidate = ElderModel9Contract.NativeMovement });
        var a = on.Step(actions); var b = off.Step(actions);
        Check(a.Fingerprint == b.Fingerprint && b.DiagnosticSchemaVersion == 2 && b.EconomicDiagnostics.Count == 0,
            "economic capture preserves physics and disabled envelope two");
        Check(a.DiagnosticSchemaVersion == 3 && a.EconomicDiagnostics.Count > 0, "economic envelope three carries schema-one records");
        foreach (var p in a.EconomicDiagnostics)
        {
            var d = p.Value;
            Check(d.CampId == p.Key && d.DecisionTurn == a.Turn - 1 && d.TrajectoryId == a.TrajectoryId && d.SchemaVersion == 1,
                "economic record binds camp/turn/trajectory/schema");
            Check(d.Goods.Select(g => (string)g["good"]).SequenceEqual(Enum.GetNames(typeof(CraftGood))), "economic goods use stable enum order");
            Check(d.Materials.Select(g => (string)g["material"]).SequenceEqual(new[] { "Stone", "Clay", "Wood" }), "economic materials use stable order");
            Check(d.Herds.Select(h => (int)h["herdId"]).Distinct().Count() == d.Herds.Count, "managed groups are counted once by herd ID");
            Check(d.Conservation["foodResidual"] == null && d.Unavailable.ContainsKey("conservation.foodResidual"),
                "unclosed stock ledger is explicitly unavailable");
            if (d.Conservation.TryGetValue("populationResidual", out var residual)) Check(Convert.ToDouble(residual) == 0, "linked population reconciles at committed close");
        }
        foreach (var v in a.Villages.Where(v => v.Observation != null))
        {
            var twin = b.Villages.Single(x => x.Camp == v.Camp);
            Check(v.Observation.Core.SequenceEqual(twin.Observation.Core) && v.Welfare == twin.Welfare && v.TradeUtility == twin.TradeUtility,
                "economic capture cannot leak into actor observations or rewards");
            Check(v.Teacher == null && twin.Teacher == null || v.Teacher.Action.ServiceLevels.SequenceEqual(twin.Teacher.Action.ServiceLevels), "teacher labels are capture invariant");
        }
        ElderModel9Environment.VerifyReplay(on.Replay); checks++;
        var replayEconomic = on.Replay.Steps[0].EconomicDiagnostics.First().Value;
        var population = (Dictionary<string, object>)replayEconomic.Domains["population"];
        var original = population["endingLinked"]; population["endingLinked"] = -7.0;
        bool rejected = false;
        try { ElderModel9Environment.VerifyReplay(on.Replay); } catch (InvalidOperationException e) { rejected = e.Message.Contains("economic diagnostics diverged"); }
        Check(rejected, "economic replay mismatch is rejected"); population["endingLinked"] = original;
        var fork = on.Fork(out var restored);
        Check(restored.RestoredObservation && restored.EconomicDiagnostics.First().Value.TransitionId == a.EconomicDiagnostics.First().Value.TransitionId,
            "restore preserves original transition identity and is labelled observation only");
        var saved = fork.EconomicDiagnostics.First().Value;
        ((Dictionary<string, object>)restored.EconomicDiagnostics.First().Value.Domains["population"])["endingLinked"] = -9.0;
        Check(Convert.ToDouble(((Dictionary<string, object>)saved.Domains["population"])["endingLinked"]) >= 0, "fork economic trees do not alias returned observations");
        var firstBranch = on.Step(actions);
        ElderModel9Environment.VerifyReplay(on.Replay); checks++;
        on.Restore(fork); var secondBranch = on.Step(actions);
        Check(firstBranch.Fingerprint == secondBranch.Fingerprint && firstBranch.TrajectoryId != secondBranch.TrajectoryId,
            "independently named fork continuations preserve paired physics");
        Check(firstBranch.EconomicDiagnostics.Values.All(d => d.ForkId != null && d.BranchId != null && d.ParentTransitionId != null),
            "new fork transitions retain branch provenance");
        foreach (var diagnostic in firstBranch.EconomicDiagnostics.Values)
        {
            if (fork.EconomicDiagnostics.TryGetValue(diagnostic.CampId, out var preceding))
                Check(diagnostic.ParentTransitionId == preceding.TransitionId,
                    "existing camp branch points to its own preceding transition");
            else
            {
                var parents = fork.EconomicDiagnostics.Values.Where(d => d.Transfers.Any(t =>
                    (string)t["kind"] == "fission" && (int)t["destinationCamp"] == diagnostic.CampId)).ToArray();
                Check(parents.Length == 1 && diagnostic.ParentTransitionId == parents[0].TransitionId,
                    "new daughter branch points to its witnessed parent transition");
            }
        }
        var worker = new ElderModel9Environment(new PhaseExecution(2, 1, true, 1)) { EconomicDiagnosticsEnabled = true };
        worker.Reset(snapshot, true); var workerStep = worker.Step(actions);
        Check(workerStep.Fingerprint == a.Fingerprint, "economic capture preserves scalar/worker physics");
        Check(workerStep.EconomicDiagnostics.All(p => p.Value.Goods.Select(g => (double)g["output"]).SequenceEqual(a.EconomicDiagnostics[p.Key].Goods.Select(g => (double)g["output"]))),
            "economic physical receipts agree across worker partitions");
        ElderModel9Environment.VerifyReplay(worker.Replay, new PhaseExecution(2, 1, true, 1)); checks++;
    }
    private static void Rules(SimulationConfig cfg) { cfg.SettlingGraceOnce = rulesOn; cfg.PlantTentsEnabled = rulesOn; }
    private static int RunWith(bool rules)
    {
        rulesOn = rules;
        var snapshot = ElderModel9Environment.GenerateOpening(17, 8, out _, 40, 0, Rules);
        EconomicDiagnosticsChecks(snapshot);
        Check(SaveCodec.Load(snapshot).Config.SettlingGraceOnce == rules, "fixture rules as requested");
        var teacher = new ElderModel9Environment(); var acting = new ElderModel9Environment();
        var reset = teacher.Reset(snapshot, true); acting.Reset(snapshot, true);
        Check(reset.Villages.Count > 0 && reset.Villages.All(v => v.Observation != null), "reset observes every resident-economy village");
        Check(reset.Villages.All(v => v.DecisionDiagnostics == null), "reset has no preceding decision sidecar");
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
            Check(b.DecisionDiagnostics.Count == directives.Count && directives.Keys.All(id => b.Villages.Single(v => v.Camp == id).DecisionDiagnostics != null),
                "submitted village decisions have aligned sidecars");
            Check(b.DecisionDiagnostics.Values.All(d => d.SchemaVersion == ElderModel9DecisionDiagnostics.CurrentSchemaVersion && d.CampId >= 0 &&
                d.Requested.Strategic.Length == 5 && d.Requested.Service.Length == ElderModel9Contract.GoodsServices.Length &&
                d.Requested.TradeValue.Length == 7 && d.Requested.TradeGate.Length == 7), "decision sidecar has schema-v1 head shapes");
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

        // Raw materials use the delayed shared baseline plan and attribute only
        // matching tagged receipts; actor tensor/action widths remain unchanged.
        var rawEnv = new ElderModel9Environment(); rawEnv.Reset(snapshot, true);
        rawEnv.World.Config.ElderRawMaterialsEnabled = true;
        rawEnv.World.Config.EconomicAllocation = EconomicAllocationStrategy.DemandControl;
        rawEnv.Replay.InitialSnapshot = SaveCodec.Save(rawEnv.World);
        rawEnv.Replay.InitialFingerprint = SaveCodec.Fingerprint(rawEnv.World);
        rawEnv.Replay.ConfigurationHash = SaveCodec.ConfigurationHash(rawEnv.World.Config);
        var rawDirectives = rawEnv.World.Camps.Where(c => c.Population > 0 && CultivationEconomy.EconomyEnabled(rawEnv.World, c))
            .ToDictionary(c => c.Id, c => new ElderDirective { SchemaVersion = ElderModel9Contract.ActionSchemaVersion,
                MovementCandidate = ElderModel9Contract.NativeMovement, ServiceLevels = new[] { 0d, 0, 0, 0, 0, 1 } });
        var rawStep = rawEnv.Step(rawDirectives);
        var rawRecord = rawEnv.World.History.Last();
        Check(rawStep.DiagnosticSchemaVersion == 2 &&
              rawStep.DecisionDiagnostics.Values.All(d => d.SchemaVersion == ElderModel9DecisionDiagnostics.CurrentSchemaVersion) &&
              rawStep.RawMaterialDiagnostics.Values.All(d => d.SchemaVersion == 2),
            "diagnostic envelope and per-head/raw sidecars use independent schema versions");
        Check(rawStep.RawMaterialDiagnostics.Count > 0, "flag-on materials service creates typed raw requests when residual residents and sources exist");
        Check(rawStep.RawMaterialDiagnostics.Values.All(d => d.AcceptedUnits <= d.RequestedUnits + 1e-9 && d.RealisedUnits <= d.AcceptedUnits + 1e-9),
            "raw material acceptance and realised receipts never exceed their requests/grants");
        Check(rawStep.RawMaterialDiagnostics.All(pair => Math.Abs(pair.Value.RealisedUnits - rawRecord.MaterialTransfers
            .Where(m => m.Camp == pair.Value.CampId && m.Material == pair.Value.Material && m.Phase == "elder-raw" &&
                pair.Value.RequestId.Split('+').Contains(m.RequestId)).Sum(m => Math.Max(0, m.Amount))) <= 1e-9),
            "raw realised diagnostics sum only receipts with matching phase, request id, camp and material");
        var parityOff = new ElderModel9Environment(); parityOff.Reset(snapshot, true);
        var parityOn = new ElderModel9Environment(); parityOn.Reset(snapshot, true);
        parityOff.World.Config.EconomicAllocation = parityOn.World.Config.EconomicAllocation = EconomicAllocationStrategy.DemandControl;
        parityOff.World.Config.ElderRawMaterialsEnabled = false; parityOn.World.Config.ElderRawMaterialsEnabled = true;
        var parityActions = rawDirectives.Keys.ToDictionary(id => id, id => new ElderDirective
        { SchemaVersion = ElderModel9Contract.ActionSchemaVersion, MovementCandidate = ElderModel9Contract.NativeMovement,
          ServiceLevels = new[] { 1d, 1, 1, 1, 1, 0 } });
        var parityOffStep = parityOff.Step(parityActions.ToDictionary(p => p.Key, p => p.Value.Copy()));
        var parityOnStep = parityOn.Step(parityActions.ToDictionary(p => p.Key, p => p.Value.Copy()));
        var parityOffRecord = parityOff.World.History.Last(); var parityOnRecord = parityOn.World.History.Last();
        string OrderedMaterials(TurnRecord record) => string.Join("|", record.MaterialTransfers.Select(m =>
            string.Join(":", m.Camp, m.Society, m.Tile, m.Source, (int)m.Material, m.Role,
                m.Amount.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                m.Before.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                m.After.ToString("R", System.Globalization.CultureInfo.InvariantCulture), m.Phase ?? "", m.RequestId ?? "")));
        string OrderedProduction(TurnRecord record) => string.Join("|", record.Production.Select(p =>
            string.Join(":", p.Camp, (int)p.Good, p.Role, p.Output.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                p.WorkerIntervals.ToString("R", System.Globalization.CultureInfo.InvariantCulture), p.Stone, p.Clay, p.Wood,
                p.PlantFood, p.AnimalFood, p.AnimalFabric, p.HousingMaterial)));
        string canonicalOff = FlagOffCompatibilityChecks.CanonicalSavePayload(typeof(SaveCodec).Assembly, parityOff.World);
        string canonicalOn = FlagOffCompatibilityChecks.CanonicalSavePayload(typeof(SaveCodec).Assembly, parityOn.World);
        bool parityZeroRaw = parityOnStep.RawMaterialDiagnostics.Count > 0 &&
            parityOnStep.RawMaterialDiagnostics.Values.All(d => d.AcceptedUnits == 0 && d.RealisedUnits == 0) &&
            !parityOnRecord.MaterialTransfers.Any(m => m.Phase == "elder-raw");
        bool paritySave = canonicalOff == canonicalOn;
        bool parityMaterials = OrderedMaterials(parityOffRecord) == OrderedMaterials(parityOnRecord);
        bool parityProduction = OrderedProduction(parityOffRecord) == OrderedProduction(parityOnRecord);
        bool parityIntegration = parityOff.LastResolutionMetrics.TileIntegrations == parityOn.LastResolutionMetrics.TileIntegrations &&
            parityOff.LastResolutionMetrics.TileIntegrations > 0 && parityOffRecord.Changes.Count == parityOnRecord.Changes.Count;
        int saveDiff = Enumerable.Range(0, Math.Min(canonicalOff.Length, canonicalOn.Length)).FirstOrDefault(i => canonicalOff[i] != canonicalOn[i]);
        string saveDiffText = paritySave ? "equal" : "idx=" + saveDiff + " off=" + canonicalOff.Substring(Math.Max(0, saveDiff - 50), Math.Min(140, canonicalOff.Length - Math.Max(0, saveDiff - 50))) + " on=" + canonicalOn.Substring(Math.Max(0, saveDiff - 50), Math.Min(140, canonicalOn.Length - Math.Max(0, saveDiff - 50)));
        Check(parityZeroRaw && paritySave && parityMaterials && parityProduction && parityIntegration,
            "paired full-turn flag-off/on fallback-only run has zero raw authority and exact save, production, needs/world, endpoint source/Wood, transfer and integration parity (raw=" + parityZeroRaw + ", save=" + paritySave + ", " + saveDiffText + ", materials=" + parityMaterials + ", production=" + parityProduction + ", integrations=" + parityOff.LastResolutionMetrics.TileIntegrations + "/" + parityOn.LastResolutionMetrics.TileIntegrations + ", parity=" + parityIntegration + ", changes=" + parityOffRecord.Changes.Count + "/" + parityOnRecord.Changes.Count + ")");
        var terminalTrade = new ElderModel9Environment(); terminalTrade.Reset(snapshot, true);
        var terminalCamp = terminalTrade.World.Camps.First(c => c.Player > 0 && c.Population > 0 && CultivationEconomy.EconomyEnabled(terminalTrade.World, c));
        int terminalCampId = terminalCamp.Id;
        terminalCamp.Population = 1; terminalCamp.Workforce.Assignments.Clear(); terminalCamp.DeficitTurns = 1;
        terminalCamp.Food = 0; terminalTrade.World.Config.Mortality = 1; terminalTrade.World.Config.StarvationGraceTurns = 0;
        terminalTrade.World.Config.FissionEnabled = false;
        foreach (var tile in terminalTrade.World.Tiles)
        {
            tile.Food = tile.PotentialFood = tile.Freshwater = 0;
            for (int i = 0; i < tile.Seasons.Length; i++)
            { var season = tile.Seasons[i]; season.MaxFood = season.MaxPotentialFood = season.MaxFreshwater = 0;
              season.FoodRecoveryPerDay = season.BiomassRecoveryPerDay = season.WaterRecoveryPerDay = 0; tile.Seasons[i] = season; }
        }
        var terminalDecision = new ElderDirective { SchemaVersion = ElderModel9Contract.ActionSchemaVersion,
            MovementCandidate = ElderModel9Contract.NativeMovement };
        Check(Trade.Eligible(terminalTrade.World, terminalCamp), "terminal trade fixture begins eligible at decision time");
        var terminalStep = terminalTrade.Step(new Dictionary<int, ElderDirective> { [terminalCampId] = terminalDecision });
        var terminalVillage = terminalStep.Villages.Single(v => v.Camp == terminalCampId);
        Check(terminalVillage.Terminated && terminalVillage.DecisionDiagnostics.ActiveMask.TradeValue.All(x => x) &&
              terminalVillage.DecisionDiagnostics.ActiveMask.TradeGate.All(x => x) && terminalVillage.DecisionDiagnostics.ActiveMask.TradeShare,
            "trade activity remains decision-time active when the eligible camp terminates during resolution");
        var nativeRawEnv = new ElderModel9Environment(); nativeRawEnv.Reset(snapshot, true);
        nativeRawEnv.World.Config.ElderRawMaterialsEnabled = true;
        nativeRawEnv.World.Config.EconomicAllocation = EconomicAllocationStrategy.DemandControl;
        var nativeRawStep = nativeRawEnv.Step(new Dictionary<int, ElderDirective>());
        Check(nativeRawStep.RawMaterialDiagnostics.Count > 0,
            "native teacher preparation executes and reports flag-on raw-material work without submitted actor decisions");
        var transitionSnapshot = ElderModel9Environment.GenerateOpening(1000, 8, out _, 40, 0);
        var transitionOff = new ElderModel9Environment(); transitionOff.Reset(transitionSnapshot, true);
        var transitionOn = new ElderModel9Environment(); transitionOn.Reset(transitionSnapshot, true);
        transitionOff.World.Config.EconomicAllocation = transitionOn.World.Config.EconomicAllocation = EconomicAllocationStrategy.DemandControl;
        transitionOff.World.Config.ElderRawMaterialsEnabled = false; transitionOn.World.Config.ElderRawMaterialsEnabled = true;
        var transitionPlayers = transitionOn.World.Camps.Where(c => c.Population > 0 && c.Player > 0 && CultivationEconomy.EconomyEnabled(transitionOn.World, c)).ToArray();
        var transitionPriorSpecialists = transitionPlayers.ToDictionary(c => c.Id, c => c.Workforce.Assignments
            .Where(a => a.Role != WorkforceRoles.Gatherer).Sum(a => a.Population));
        var transitionActions = transitionPlayers.ToDictionary(c => c.Id, c => new ElderDirective
        { SchemaVersion = ElderModel9Contract.ActionSchemaVersion, MovementCandidate = ElderModel9Contract.NativeMovement,
          ServiceLevels = new[] { 0d, 0, 0, 0, 0, 1 } });
        bool priorCraftDiagnosticCapture = ResolutionMetrics.CaptureCraftWorkDiagnostics;
        ResolutionMetrics.CaptureCraftWorkDiagnostics = true;
        ElderModel9Step transitionOffStep, transitionOnStep;
        try
        {
            transitionOffStep = transitionOff.Step(transitionActions.ToDictionary(p => p.Key, p => p.Value.Copy()));
            transitionOnStep = transitionOn.Step(transitionActions.ToDictionary(p => p.Key, p => p.Value.Copy()));
        }
        finally { ResolutionMetrics.CaptureCraftWorkDiagnostics = priorCraftDiagnosticCapture; }
        int reducedPriorStaff = transitionPriorSpecialists.Count(pair => transitionOn.World.Camps.FirstOrDefault(c => c.Id == pair.Key) is Camp camp &&
            camp.Workforce.Assignments.Where(a => a.Role != WorkforceRoles.Gatherer).Sum(a => a.Population) < pair.Value);
        var transitionOnRecord = transitionOn.World.History.Last();
        bool transitionZero = transitionOnStep.RawMaterialDiagnostics.Count > 0 &&
            transitionOnStep.RawMaterialDiagnostics.Values.All(d => d.AcceptedUnits == 0 && d.RealisedUnits == 0) &&
            !transitionOnRecord.MaterialTransfers.Any(m => m.Phase == "elder-raw");
        string PhysicalSave(ElderModel9Environment env)
        {
            var copy = SaveCodec.Load(SaveCodec.Save(env.World));
            // The raw service is intentionally a teacher/diagnostic label today;
            // normalize only the corresponding persisted previous-label coordinate.
            foreach (var camp in copy.Camps)
            {
                var memory = camp.ElderMemory;
                foreach (var action in new[] { memory?.LastRequested, memory?.LastAccepted, memory?.LastRealized })
                    if (action?.ServiceLevels != null && action.ServiceLevels.Length > 5) action.ServiceLevels[5] = 0;
            }
            return FlagOffCompatibilityChecks.CanonicalSavePayload(typeof(SaveCodec).Assembly, copy);
        }
        string transitionPhysicalOff = PhysicalSave(transitionOff), transitionPhysicalOn = PhysicalSave(transitionOn);
        int transitionSaveDiff = Enumerable.Range(0, Math.Min(transitionPhysicalOff.Length, transitionPhysicalOn.Length))
            .FirstOrDefault(i => transitionPhysicalOff[i] != transitionPhysicalOn[i]);
        Check(transitionPriorSpecialists.Values.Any(x => x > 0) && reducedPriorStaff > 0 && transitionZero &&
              transitionPhysicalOff == transitionPhysicalOn &&
              transitionOff.LastResolutionMetrics.TileIntegrations == transitionOn.LastResolutionMetrics.TileIntegrations,
            "seed-1000 positive-service full turn releases prior specialists only to fallback and preserves physical source/Wood, production, needs, receipts and integration parity (prior=" + transitionPriorSpecialists.Values.Sum() + ", reduced=" + reducedPriorStaff + ", diagnostics=" + transitionOnStep.RawMaterialDiagnostics.Count + ", zero=" + transitionZero + ", save=" + (transitionPhysicalOff == transitionPhysicalOn) + ", diff=" + transitionSaveDiff + ", off=" + transitionPhysicalOff.Substring(Math.Max(0, transitionSaveDiff - 50), Math.Min(120, transitionPhysicalOff.Length - Math.Max(0, transitionSaveDiff - 50))) + ", on=" + transitionPhysicalOn.Substring(Math.Max(0, transitionSaveDiff - 50), Math.Min(120, transitionPhysicalOn.Length - Math.Max(0, transitionSaveDiff - 50))) + ", integrations=" + transitionOff.LastResolutionMetrics.TileIntegrations + "/" + transitionOn.LastResolutionMetrics.TileIntegrations + ")");
        bool endpointCapitalWitness = transitionOff.LastResolutionMetrics.CraftFieldWork != null &&
            transitionOff.LastResolutionMetrics.CraftFieldWork.Values.Any(d =>
                Math.Abs(d.ProvisionalGatheringBridgeCreditWorkerYears - d.GatheringBridgeCreditWorkerYears) > 1e-8);
        Check(endpointCapitalWitness && transitionOff.World.Resources != null && transitionOff.World.Resources.Wood.Sum(x => (long)x) > 0 &&
              transitionOff.World.Resources.Wood.SequenceEqual(transitionOn.World.Resources.Wood) &&
              transitionOff.LastResolutionMetrics.TileIntegrations == transitionOn.LastResolutionMetrics.TileIntegrations,
            "full resolver fixture witnesses provisional-versus-realised cultivation bridge divergence and exact endpoint Wood/integration parity");
        var transitionOffStep2 = transitionOff.Step(transitionActions.ToDictionary(p => p.Key, p => p.Value.Copy()));
        var transitionOnStep2 = transitionOn.Step(transitionActions.ToDictionary(p => p.Key, p => p.Value.Copy()));
        bool SameTensorExceptMaterials(ElderModel9Tensor a, ElderModel9Tensor b)
        {
            return a.Core.Length == b.Core.Length && a.CorePresent.SequenceEqual(b.CorePresent) &&
                a.Core.Where((_, i) => i != 200).SequenceEqual(b.Core.Where((_, i) => i != 200)) &&
                a.Sets.Length == b.Sets.Length && a.Sets.Zip(b.Sets, (x, y) => x.SequenceEqual(y)).All(x => x) &&
                a.RowPresent.Zip(b.RowPresent, (x, y) => x.SequenceEqual(y)).All(x => x) &&
                a.MovementMask.SequenceEqual(b.MovementMask) && a.ClothingRouteMask.SequenceEqual(b.ClothingRouteMask) &&
                a.ShelterRouteMask.SequenceEqual(b.ShelterRouteMask) && a.EconomyMixMask.SequenceEqual(b.EconomyMixMask) &&
                a.CommitmentHoldMask == b.CommitmentHoldMask && a.RegionMovementMask.SequenceEqual(b.RegionMovementMask);
        }
        bool transitionObservationParity = transitionOffStep2.Villages.OrderBy(v => v.Camp).Zip(transitionOnStep2.Villages.OrderBy(v => v.Camp), (a, b) =>
        {
            return a.Camp == b.Camp && SameTensorExceptMaterials(a.Observation, b.Observation);
        }).All(x => x);
        Check(transitionObservationParity && PhysicalSave(transitionOff) == PhysicalSave(transitionOn),
            "second fixed-directive turn preserves physical world and all actor observations except the explicitly new materials-service prior-request coordinate");
        bool rawTeacherTiedToRequest = transitionOnStep.RawMaterialDiagnostics.Values.GroupBy(d => d.CampId).All(group =>
        {
            var offVillage = transitionOffStep.Villages.First(v => v.Camp == group.Key);
            var onVillage = transitionOnStep.Villages.First(v => v.Camp == group.Key);
            double equivalent = group.Sum(d =>
            {
                var camp = transitionOn.World.Camps.First(c => c.Id == d.CampId);
                string role = camp.People.HasTech(EconomicIntents.RequiredTechnology(MaterialEconomy.Specialist(d.Material)))
                    ? MaterialEconomy.Specialist(d.Material) : WorkforceRoles.Gatherer;
                double annualRate = SpecialistProductivity.ExtractionRate(transitionOn.World, camp, d.Material, role) *
                    transitionOn.World.Config.DaysPerYear / transitionOn.World.Config.BaseDays;
                return annualRate > 0 ? d.RequestedUnits / annualRate : 0;
            });
            var left = offVillage.Teacher.Action; var right = onVillage.Teacher.Action;
            bool sameOtherAction = left.RetainedFieldOperation == right.RetainedFieldOperation &&
                left.RetainedHerdFraction == right.RetainedHerdFraction && left.DevelopmentBudget == right.DevelopmentBudget &&
                left.FieldDevelopmentShare == right.FieldDevelopmentShare && left.FishingDevelopmentShare == right.FishingDevelopmentShare &&
                left.ServiceLevels.Take(5).SequenceEqual(right.ServiceLevels.Take(5)) && left.ClothingRoute == right.ClothingRoute &&
                left.ShelterRoute == right.ShelterRoute && left.MovementCandidate == right.MovementCandidate && left.IntentTile == right.IntentTile &&
                left.FissionPolicy == right.FissionPolicy && left.CommitTurns == right.CommitTurns &&
                left.TradeValue.SequenceEqual(right.TradeValue) && left.TradeGate.SequenceEqual(right.TradeGate) &&
                left.TradeExportShare == right.TradeExportShare;
            bool sameOtherMasks = offVillage.Teacher.ServiceMask.Take(5).SequenceEqual(onVillage.Teacher.ServiceMask.Take(5)) &&
                offVillage.Teacher.StrategicFieldMask == onVillage.Teacher.StrategicFieldMask &&
                offVillage.Teacher.StrategicHerdMask == onVillage.Teacher.StrategicHerdMask &&
                offVillage.Teacher.DevelopmentMask == onVillage.Teacher.DevelopmentMask &&
                offVillage.Teacher.DevelopmentShareMask == onVillage.Teacher.DevelopmentShareMask &&
                offVillage.Teacher.FishingShareMask == onVillage.Teacher.FishingShareMask &&
                offVillage.Teacher.ClothingRouteMask == onVillage.Teacher.ClothingRouteMask &&
                offVillage.Teacher.ShelterRouteMask == onVillage.Teacher.ShelterRouteMask &&
                offVillage.Teacher.MovementMask == onVillage.Teacher.MovementMask && offVillage.Teacher.TradeMask == onVillage.Teacher.TradeMask;
            return onVillage.Teacher.ServiceMask[5] && Math.Abs(onVillage.Teacher.Action.ServiceLevels[5]) < 1e-9 &&
                Math.Abs((onVillage.Teacher.RepairSize - offVillage.Teacher.RepairSize) - equivalent) < 1e-6 * Math.Max(1, equivalent) &&
                sameOtherAction && sameOtherMasks;
        });
        Check(rawTeacherTiedToRequest,
            "materials teacher target/mask and RepairSize delta equal the raw-request service equivalent while every other label mask remains unchanged");
        var transitionFork = transitionOn.Fork(out var transitionForkStep);
        var transitionRestored = transitionOn.Restore(transitionFork);
        bool rawSidecarPreserved = transitionForkStep.DiagnosticSchemaVersion == 2 &&
            transitionForkStep.RawMaterialDiagnostics.Keys.OrderBy(k => k, StringComparer.Ordinal)
                .SequenceEqual(transitionOnStep2.RawMaterialDiagnostics.Keys.OrderBy(k => k, StringComparer.Ordinal)) &&
            transitionRestored.RawMaterialDiagnostics.All(p => transitionForkStep.RawMaterialDiagnostics.TryGetValue(p.Key, out var saved) &&
                saved.RequestedUnits == p.Value.RequestedUnits && saved.AcceptedUnits == p.Value.AcceptedUnits && saved.RealisedUnits == p.Value.RealisedUnits);
        string restoredTrajectory = transitionRestored.TrajectoryId;
        var branchContinuation = transitionOn.Step(transitionActions.ToDictionary(p => p.Key, p => p.Value.Copy()));
        Check(rawSidecarPreserved && branchContinuation.TrajectoryId != restoredTrajectory &&
              branchContinuation.RawMaterialDiagnostics.Values.All(d => d.AcceptedUnits == 0 && d.RealisedUnits == 0),
            "staffing-transition raw diagnostics survive fork/restore and branch continuation receives a distinct trajectory with zero authority");
        var transitionReloaded = new ElderModel9Environment(); transitionReloaded.Reset(SaveCodec.Save(transitionOff.World), true);
        transitionOff.Step(transitionActions.ToDictionary(p => p.Key, p => p.Value.Copy()));
        transitionReloaded.Step(transitionActions.ToDictionary(p => p.Key, p => p.Value.Copy()));
        Check(PhysicalSave(transitionReloaded) == PhysicalSave(transitionOff) &&
              transitionReloaded.LastResolutionMetrics.TileIntegrations == transitionOff.LastResolutionMetrics.TileIntegrations,
            "fixed-directive save/load continuation after staffing reduction preserves complete physical state and integration work");
        var rawOnReloaded = new ElderModel9Environment(); rawOnReloaded.Reset(SaveCodec.Save(transitionOn.World), true);
        var rawOnLiveStep = transitionOn.Step(transitionActions.ToDictionary(p => p.Key, p => p.Value.Copy()));
        var rawOnReloadStep = rawOnReloaded.Step(transitionActions.ToDictionary(p => p.Key, p => p.Value.Copy()));
        Check(rawOnLiveStep.RawMaterialDiagnostics.Values.All(d => d.AcceptedUnits == 0 && d.RealisedUnits == 0) &&
              rawOnReloadStep.RawMaterialDiagnostics.Values.All(d => d.AcceptedUnits == 0 && d.RealisedUnits == 0) &&
              PhysicalSave(rawOnReloaded) == PhysicalSave(transitionOn) &&
              rawOnReloaded.LastResolutionMetrics.TileIntegrations == transitionOn.LastResolutionMetrics.TileIntegrations,
            "flag-on fixed-directive save/load continuation after staffing reduction preserves zero-authority physical state and integration work");
        bool acceptedRawLabelDelta = transitionOffStep.Villages.Where(v => v.DecisionDiagnostics != null).Any(v =>
                transitionOnStep.Villages.First(x => x.Camp == v.Camp).DecisionDiagnostics != null &&
                v.DecisionDiagnostics.Accepted.Service[5] != transitionOnStep.Villages.First(x => x.Camp == v.Camp).DecisionDiagnostics.Accepted.Service[5]);
        bool priorRequestFeatureTied = transitionOffStep2.Villages.OrderBy(v => v.Camp).Zip(transitionOnStep2.Villages.OrderBy(v => v.Camp), (a, b) =>
                  a.Camp == b.Camp && b.Population > 0 &&
                  Math.Abs((b.Observation.Core[200] - a.Observation.Core[200]) -
                    transitionOnStep2.RawMaterialDiagnostics.Values.Where(d => d.CampId == b.Camp).Sum(d =>
                    {
                        var camp = transitionOn.World.Camps.First(c => c.Id == d.CampId);
                        string role = camp.People.HasTech(EconomicIntents.RequiredTechnology(MaterialEconomy.Specialist(d.Material)))
                            ? MaterialEconomy.Specialist(d.Material) : WorkforceRoles.Gatherer;
                        double annualRate = SpecialistProductivity.ExtractionRate(transitionOn.World, camp, d.Material, role) *
                            transitionOn.World.Config.DaysPerYear / transitionOn.World.Config.BaseDays;
                        return annualRate > 0 ? d.RequestedUnits / annualRate / b.Population : 0;
                    })) < 1e-6).Any(x => x);
        Check(acceptedRawLabelDelta && priorRequestFeatureTied,
            "flag-on zero-authority raw service changes only the expected accepted service label and next-turn prior-requested observation feature (label=" + acceptedRawLabelDelta + ", requestFeature=" + priorRequestFeatureTied + ")");
        var transitionFractionOff = new ElderModel9Environment(); transitionFractionOff.Reset(transitionSnapshot, true);
        var transitionFractionOn = new ElderModel9Environment(); transitionFractionOn.Reset(transitionSnapshot, true);
        transitionFractionOff.World.Config.EconomicAllocation = transitionFractionOn.World.Config.EconomicAllocation = EconomicAllocationStrategy.DemandControl;
        transitionFractionOff.World.Config.ElderRawMaterialsEnabled = false; transitionFractionOn.World.Config.ElderRawMaterialsEnabled = true;
        var transitionFractionActions = transitionFractionOn.World.Camps.Where(c => c.Population > 0 && c.Player > 0 && CultivationEconomy.EconomyEnabled(transitionFractionOn.World, c))
            .ToDictionary(c => c.Id, c => new ElderDirective { SchemaVersion = ElderModel9Contract.ActionSchemaVersion,
                MovementCandidate = ElderModel9Contract.NativeMovement, ServiceLevels = new[] { 0d, 0, 0, 0, 0, .35 } });
        var transitionFractionPrior = transitionFractionOn.World.Camps.Where(c => transitionFractionActions.ContainsKey(c.Id))
            .ToDictionary(c => c.Id, c => c.Workforce.Assignments.Where(a => a.Role != WorkforceRoles.Gatherer).Sum(a => a.Population));
        transitionFractionOff.Step(transitionFractionActions.ToDictionary(p => p.Key, p => p.Value.Copy()));
        var transitionFractionStep = transitionFractionOn.Step(transitionFractionActions.ToDictionary(p => p.Key, p => p.Value.Copy()));
        int fractionReduced = transitionFractionPrior.Count(pair => transitionFractionOn.World.Camps.FirstOrDefault(c => c.Id == pair.Key) is Camp camp &&
            camp.Workforce.Assignments.Where(a => a.Role != WorkforceRoles.Gatherer).Sum(a => a.Population) < pair.Value);
        Check(transitionFractionPrior.Values.Any(x => x > 0) && fractionReduced > 0 && transitionFractionStep.RawMaterialDiagnostics.Count > 0 &&
              transitionFractionStep.RawMaterialDiagnostics.Values.All(d => d.AcceptedUnits == 0 && d.RealisedUnits == 0) &&
              !transitionFractionOn.World.History.Last().MaterialTransfers.Any(m => m.Phase == "elder-raw") &&
              PhysicalSave(transitionFractionOff) == PhysicalSave(transitionFractionOn) &&
              transitionFractionOff.LastResolutionMetrics.TileIntegrations == transitionFractionOn.LastResolutionMetrics.TileIntegrations,
            "fractional-service staffing reduction keeps all residual labor committed to native fallback with zero raw grant/receipt and flag-off physical parity");
        Check(rawDirectives.Values.All(d => d.ServiceLevels.Length == ElderModel9Contract.GoodsServices.Length),
            "materials service uses the existing action slot without changing tensor width");
        int malformedCamp = rawStep.RawMaterialDiagnostics.Values.OrderBy(d => d.CampId).First().CampId;
        ElderModel9Environment RunRawAction(ElderDirective action)
        {
            var env = new ElderModel9Environment(); env.Reset(snapshot, true);
            env.World.Config.ElderRawMaterialsEnabled = true;
            env.World.Config.EconomicAllocation = EconomicAllocationStrategy.DemandControl;
            env.Step(new Dictionary<int, ElderDirective> { [malformedCamp] = action });
            return env;
        }
        var zeroServiceAction = new ElderDirective { SchemaVersion = ElderModel9Contract.ActionSchemaVersion,
            MovementCandidate = ElderModel9Contract.NativeMovement, ServiceLevels = new[] { 0d, 0, 0, 0, 0, 0 } };
        var fractionalRawEnv = new ElderModel9Environment(); fractionalRawEnv.Reset(snapshot, true);
        fractionalRawEnv.World.Config.ElderRawMaterialsEnabled = true;
        fractionalRawEnv.World.Config.EconomicAllocation = EconomicAllocationStrategy.DemandControl;
        var fractionalActions = fractionalRawEnv.World.Camps.Where(c => c.Population > 0 && CultivationEconomy.EconomyEnabled(fractionalRawEnv.World, c))
            .ToDictionary(c => c.Id, c => new ElderDirective { SchemaVersion = ElderModel9Contract.ActionSchemaVersion,
                MovementCandidate = ElderModel9Contract.NativeMovement, ServiceLevels = new[] { 0d, 0, 0, 0, 0, .35 } });
        var fractionalStep = fractionalRawEnv.Step(fractionalActions);
        Check(fractionalStep.RawMaterialDiagnostics.Count > 0 &&
              fractionalStep.RawMaterialDiagnostics.Values.All(d => d.AcceptedUnits == 0 && d.RealisedUnits == 0),
            "fractional service cannot manufacture Phase-B labor when all residual residents are committed to native fallback gathering");
        var zeroServiceRaw = RunRawAction(zeroServiceAction);
        var zeroServiceLegacy = new ElderModel9Environment(); zeroServiceLegacy.Reset(snapshot, true);
        zeroServiceLegacy.World.Config.ElderRawMaterialsEnabled = false;
        zeroServiceLegacy.World.Config.EconomicAllocation = EconomicAllocationStrategy.DemandControl;
        zeroServiceLegacy.Step(new Dictionary<int, ElderDirective> { [malformedCamp] = zeroServiceAction });
        string TransferLedger(ElderModel9Environment env) => string.Join("|", env.World.History.Last().MaterialTransfers.Select(m =>
            string.Join(":", m.Camp, m.Source, (int)m.Material, m.Role, m.Amount.ToString("R", System.Globalization.CultureInfo.InvariantCulture), m.Phase ?? "", m.RequestId ?? "")));
        Check(zeroServiceRaw.Replay.Steps[0].RawMaterialDiagnostics.Values.All(d => d.AcceptedUnits == 0 && d.RealisedUnits == 0) &&
              zeroServiceRaw.World.Camps.Select(c => (c.Id, c.Population, c.Food, c.Inventory.Stone, c.Inventory.Clay, c.Inventory.Wood))
                  .SequenceEqual(zeroServiceLegacy.World.Camps.Select(c => (c.Id, c.Population, c.Food, c.Inventory.Stone, c.Inventory.Clay, c.Inventory.Wood))) &&
              TransferLedger(zeroServiceRaw) == TransferLedger(zeroServiceLegacy),
            "flag-on zero service preserves legacy food gathering, outcomes and ordered material transfers");
        var nonfiniteService = RunRawAction(new ElderDirective { SchemaVersion = ElderModel9Contract.ActionSchemaVersion,
            MovementCandidate = ElderModel9Contract.NativeMovement, ServiceLevels = new[] { 1d, 1, 1, 1, 1, double.NaN } });
        var unsupportedService = RunRawAction(new ElderDirective { SchemaVersion = 999,
            ServiceLevels = new[] { 0d, 0, 0, 0, 0, 0 } });
        var shortService = RunRawAction(new ElderDirective { SchemaVersion = ElderModel9Contract.ActionSchemaVersion,
            ServiceLevels = new[] { 0d } });
        double AcceptedRaw(ElderModel9Environment env) => env.Replay.Steps[0].RawMaterialDiagnostics.Values
            .Where(d => d.CampId == malformedCamp).Sum(d => d.AcceptedUnits);
        double AcceptedService(ElderModel9Environment env) => env.Replay.Steps[0].DecisionDiagnostics[malformedCamp].Accepted.Service[5];
        Check(nonfiniteService.Replay.Steps[0].DiagnosticSchemaVersion > 0 && AcceptedRaw(nonfiniteService) == 0 && AcceptedService(nonfiniteService) == 0 &&
              nonfiniteService.Replay.Steps[0].DecisionDiagnostics[malformedCamp].AcceptedMask.Service[5],
            "a nonfinite materials service is repaired to zero before raw grants are finalized");
        Check(unsupportedService.Replay.Steps[0].Directives[malformedCamp].SchemaVersion == 999 &&
              !unsupportedService.Replay.Steps[0].DecisionDiagnostics[malformedCamp].AcceptedMask.Service[5] &&
              shortService.Replay.Steps[0].Directives[malformedCamp].ServiceLevels.Length == 1 &&
              !shortService.Replay.Steps[0].DecisionDiagnostics[malformedCamp].AcceptedMask.Service[5],
            "unsupported-schema and wrong-length service vectors retain native/null semantics, distinct from repaired zero");
        var rawFork = rawEnv.Fork(out var rawForkStep);
        if (rawForkStep.RawMaterialDiagnostics.Count > 0)
        {
            var rawKey = rawForkStep.RawMaterialDiagnostics.Keys.OrderBy(x => x, StringComparer.Ordinal).First();
            double savedRequested = rawFork.RawMaterialDiagnostics[rawKey].RequestedUnits;
            rawForkStep.RawMaterialDiagnostics[rawKey].RequestedUnits = -99;
            rawForkStep.DecisionDiagnostics.Values.First().Requested.Service[0] = -99;
            var rawRestored = rawEnv.Restore(rawFork);
            Check(rawFork.RawMaterialDiagnostics[rawKey].RequestedUnits == savedRequested &&
                  rawRestored.RawMaterialDiagnostics[rawKey].RequestedUnits == savedRequested &&
                  rawRestored.DecisionDiagnostics.Values.All(d => d.Requested.Service[0] >= 0),
                "raw and generic diagnostics are deep-copied across step, fork and restore");
        }
        ElderModel9Environment.VerifyReplay(rawEnv.Replay); checks++;
        Check(true, "raw-on schema-2 replay verifies against its stored decision-time sidecars");
        var schema1RawOn = rawEnv.Replay;
        schema1RawOn.Steps[0].DiagnosticSchemaVersion = 1;
        bool rawOnSchema1Rejected = false;
        try { ElderModel9Environment.VerifyReplay(schema1RawOn); }
        catch (InvalidOperationException ex) { rawOnSchema1Rejected = ex.Message.Contains("Schema-1 replay"); }
        Check(rawOnSchema1Rejected,
            "schema-1 replay fails closed when its captured configuration enables raw settlement");
        var rawOffLegacy = new ElderModel9Environment(); rawOffLegacy.Reset(snapshot, true);
        rawOffLegacy.Step(new Dictionary<int, ElderDirective>());
        rawOffLegacy.Replay.Steps[0].DiagnosticSchemaVersion = 1;
        ElderModel9Environment.VerifyReplay(rawOffLegacy.Replay); checks++;
        Check(true, "raw-off schema-1 replay remains valid under its legacy fingerprint");
        rawOffLegacy.Replay.Steps[0].DiagnosticSchemaVersion = ElderModel9DiagnosticEnvelope.CurrentSchemaVersion + 1;
        bool futureEnvelopeRejected = false;
        try { ElderModel9Environment.VerifyReplay(rawOffLegacy.Replay); }
        catch (InvalidOperationException ex) { futureEnvelopeRejected = ex.Message.Contains("outside the supported schema allowlist"); }
        Check(futureEnvelopeRejected, "replay rejects diagnostic envelopes newer than the supported version");
        rawOffLegacy.Replay.Steps[0].DiagnosticSchemaVersion = -1;
        bool negativeEnvelopeRejected = false;
        try { ElderModel9Environment.VerifyReplay(rawOffLegacy.Replay); }
        catch (InvalidOperationException ex) { negativeEnvelopeRejected = ex.Message.Contains("outside the supported schema allowlist"); }
        Check(negativeEnvelopeRejected, "replay rejects negative diagnostic envelope versions instead of treating them as schema zero");

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
        var pastoral = SaveCodec.Load(ElderModel9Environment.GenerateOpening(17, 8, out _, 40, herdingOnly, Rules));
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
        return checks;
    }
}
