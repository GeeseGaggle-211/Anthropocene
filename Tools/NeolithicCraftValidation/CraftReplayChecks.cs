using System;
using System.Collections.Generic;
using System.Linq;
using Anthropocene.Simulation;

// Small exact-replay audit anchored to a naturally captured Neolithic unlock.
// The inputs are saved authoritative states; no tuning or state is injected.
internal static class CraftReplayChecks
{
    const int ContinuationTurns = 3;

    public static string Verify(byte[] unlockSave, int unlockCampId = -1)
    {
        if (unlockSave == null || unlockSave.Length == 0)
            throw new ArgumentException("Natural unlock save is empty.", nameof(unlockSave));

        var scalar = SaveCodec.Load(unlockSave);
        var parallel = SaveCodec.Load(unlockSave);
        var restored = SaveCodec.Load(unlockSave);
        string initial = SaveCodec.Fingerprint(scalar);
        if (initial != SaveCodec.Fingerprint(parallel) || initial != SaveCodec.Fingerprint(restored))
            throw new InvalidOperationException("Natural unlock save did not restore to one authoritative fingerprint.");

        int campId = unlockCampId >= 0 ? unlockCampId : scalar.Camps.OrderBy(c => c.Id).First().Id;
        var unlockCamp = scalar.Camps.SingleOrDefault(c => c.Id == campId);
        if (unlockCamp == null) throw new InvalidOperationException("Natural unlock camp is absent from its saved state: " + campId);
        bool hasCultivation = CultivationEconomy.Enabled(unlockCamp);
        int turns = ContinuationTurns;
        for (int index = 0; index < turns; index++)
        {
            var scalarPlan = AiPlanning.Decide(scalar);
            ReverseEntities(parallel);
            var parallelExecution = new PhaseExecution(4, index == 1 ? 7 : 3, index % 2 == 0, 1);
            var parallelPlan = AiPlanning.Decide(parallel, parallelExecution);
            if (!SameOrders(scalarPlan.Orders, parallelPlan.Orders))
                throw new InvalidOperationException("Natural unlock continuation chose different orders under reversed collections at step " + (index + 1) + ".");

            new TurnResolver(new PhaseExecution()).Resolve(scalar, scalarPlan.Orders, scalarPlan.Traces);
            new TurnResolver(parallelExecution).Resolve(parallel, parallelPlan.Orders, parallelPlan.Traces);
            AssertSame(scalar, parallel, "scalar/4-worker continuation", index + 1);

            // Restore at a live continuation boundary, then prove the next
            // resolution remains identical to both uninterrupted references.
            if (index == 0)
            {
                restored = SaveCodec.Load(SaveCodec.Save(scalar));
                AssertSame(scalar, restored, "save/load checkpoint", index + 1);
            }
            else
            {
                var restoredPlan = AiPlanning.Decide(restored);
                new TurnResolver().Resolve(restored, restoredPlan.Orders, restoredPlan.Traces);
                AssertSame(scalar, restored, "save/load continuation", index + 1);
            }
        }

#if ISSUE33_DIAGNOSTICS
        VerifyDiagnostics(unlockSave, campId, hasCultivation);
#endif
        return "turns=" + turns + "; workers=4; checkpoint=1; diagnostics=" +
#if ISSUE33_DIAGNOSTICS
            "controlled-diagnostic-parity";
#else
            "not-compiled";
#endif
    }

#if ISSUE33_DIAGNOSTICS
    static void VerifyDiagnostics(byte[] unlockSave, int campId, bool hasCultivation)
    {
        bool oldSetting = ResolutionMetrics.CaptureCraftWorkDiagnostics;
        try
        {
            ResolutionMetrics.CaptureCraftWorkDiagnostics = false;
            var off = SaveCodec.Load(unlockSave);
            var offMetrics = ResolveControlledStay(off, campId).LastMetrics;
            if (offMetrics.CraftFieldWork != null || offMetrics.CraftFoodFlow != null)
                throw new InvalidOperationException("Craft diagnostics-off allocated a transient diagnostic dictionary.");
            string offFingerprint = SaveCodec.Fingerprint(off);

            ResolutionMetrics.CaptureCraftWorkDiagnostics = true;
            var on = SaveCodec.Load(unlockSave);
            var onResolver = ResolveControlledStay(on, campId);
            var onMetrics = onResolver.LastMetrics;
            if (SaveCodec.Fingerprint(on) != offFingerprint)
                throw new InvalidOperationException("Craft diagnostic capture changed the authoritative fingerprint.");
            if (onMetrics.CraftFoodFlow == null || !onMetrics.CraftFoodFlow.TryGetValue(campId, out var food) ||
                double.IsNaN(food.ConsumptionTarget) || double.IsInfinity(food.ConsumptionTarget) ||
                (on.Camps.Single(c => c.Id == campId).Population > 0 && food.ConsumptionTarget <= 0))
                throw new InvalidOperationException("Craft diagnostics-on did not capture a finite camp food-flow record.");
            if (hasCultivation && (onMetrics.CraftFieldWork == null || !onMetrics.CraftFieldWork.TryGetValue(campId, out var field) ||
                double.IsNaN(field.ResidentWorkerYears) || double.IsInfinity(field.ResidentWorkerYears) || field.ResidentWorkerYears <= 0))
                throw new InvalidOperationException("Craft diagnostics-on did not capture a finite cultivated-field work record.");
        }
        finally { ResolutionMetrics.CaptureCraftWorkDiagnostics = oldSetting; }
    }

    static TurnResolver ResolveControlledStay(WorldState world, int campId)
    {
        var resolver = new TurnResolver();
        var plan = AiPlanning.Decide(world);
        var camp = world.Camps.Single(c => c.Id == campId);
        var orders = plan.Orders[camp.Player];
        orders.RemoveAll(order => order.Entity == campId && order.Kind == OrderKind.Move);
        if (!orders.Any(order => order.Entity == campId && order.Kind == OrderKind.NoOp))
            orders.Add(new Order(OrderKind.NoOp, campId));
        resolver.Resolve(world, plan.Orders, plan.Traces);
        return resolver;
    }
#endif

    static void ReverseEntities(WorldState world)
    {
        world.Camps.Reverse();
        world.Hunters.Reverse();
        world.Herds.Reverse();
    }

    static bool SameOrders(IReadOnlyDictionary<int, List<Order>> left, IReadOnlyDictionary<int, List<Order>> right)
    {
        var a = left.OrderBy(p => p.Key).SelectMany(p => p.Value.Select(o => p.Key + ":" + o)).ToArray();
        var b = right.OrderBy(p => p.Key).SelectMany(p => p.Value.Select(o => p.Key + ":" + o)).ToArray();
        return a.SequenceEqual(b, StringComparer.Ordinal);
    }

    static void AssertSame(WorldState expected, WorldState actual, string label, int step)
    {
        if (SaveCodec.Fingerprint(expected) != SaveCodec.Fingerprint(actual))
            throw new InvalidOperationException("Natural unlock " + label + " diverged at continuation step " + step + ".");
    }
}
