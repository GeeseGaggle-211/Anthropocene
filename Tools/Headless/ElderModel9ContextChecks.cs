using System;
using System.Collections.Generic;
using System.Linq;
using Anthropocene.Simulation;

// The per-village model9 context (previous-decision route feasibility, service requests and fishing block) must
// describe the decision that executed at the preceding boundary, whoever made it: a native allocation, an action-12
// actor-labor decision, or a native step with teacher labels switched off. It used to update only inside the
// labelled native branch, so action-12 villages kept their last native values (still marked present) forever.
internal static class ElderModel9ContextChecks
{
    private static int checks;
    private static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception("ElderModel9Context: " + message); }

    public static int Run()
    {
        checks = 0;
        LabelIndependence();
        ActionTwelveUpdates();
        Console.WriteLine("PASS: " + checks + " model9 context assertions");
        return checks;
    }

    private static int Index(string name)
    {
        int index = Array.IndexOf(ElderModel9Encoder.CoreFeatureNames(), name);
        if (index < 0) throw new Exception("ElderModel9Context: missing core feature " + name);
        return index;
    }

    // Native heuristic steps: the context (and hence every encoded tensor) is identical with labels on or off, and the
    // previous-decision blocks are present after the first decision.
    private static void LabelIndependence()
    {
        var snapshot = ElderModel9Environment.GenerateOpening(17, 8, out _);
        var labelled = new ElderModel9Environment();
        var unlabelled = new ElderModel9Environment { TeacherLabels = false };
        var a = labelled.Reset(snapshot, true); var b = unlabelled.Reset(snapshot, true);
        int request = Index("previous-request-food-per-resident"), route = Index("cloth-route-feasible"), catch_ = Index("frontier-marginal-catch-over-gathering");
        foreach (var v in a.Villages) Check(!v.Observation.CorePresent[request] && !v.Observation.CorePresent[route], "no previous-decision evidence at reset");
        for (int turn = 0; turn < 3; turn++)
        {
            a = labelled.Step(new Dictionary<int, ElderDirective>()); b = unlabelled.Step(new Dictionary<int, ElderDirective>());
            Check(a.Fingerprint == b.Fingerprint, "label emission does not change the world");
            var rows = b.Villages.Where(v => v.Observation != null).ToDictionary(v => v.Camp);
            int decided = 0;
            foreach (var v in a.Villages.Where(v => v.Observation != null))
            {
                var w = rows[v.Camp];
                Check(v.Observation.Core.SequenceEqual(w.Observation.Core) && v.Observation.CorePresent.SequenceEqual(w.Observation.CorePresent) &&
                    v.Observation.ClothingRouteMask.SequenceEqual(w.Observation.ClothingRouteMask) && v.Observation.ShelterRouteMask.SequenceEqual(w.Observation.ShelterRouteMask),
                    "the context is updated whether or not teacher labels are emitted");
                Check(w.Teacher == null, "an unlabelled environment emits no teacher label");
                if (v.Teacher != null)
                {
                    decided++;
                    Check(w.Observation.CorePresent[request] && w.Observation.CorePresent[route] && w.Observation.CorePresent[catch_],
                        "a native decision defines every previous-decision block");
                }
            }
            Check(decided > 0, "the fixture has native decisions");
        }
    }

    private static ElderDirective LaborAction(ElderActorLaborCatalog catalog, Camp camp, SimulationConfig config, double fisherShare, double developmentShare)
    {
        int gatherer = Array.IndexOf(WorkforceRoles.All, WorkforceRoles.Gatherer), fisher = Array.IndexOf(WorkforceRoles.All, WorkforceRoles.Fisher);
        var specialty = new double[ElderActorLaborContract.SpecialtyRows];
        bool fishing = fisherShare > 0 && catalog.SpecialtyMask[fisher + 1] && catalog.ActivityMask[fisher][9];
        specialty[gatherer + 1] = fishing ? 1 - fisherShare : 1;
        if (fishing) specialty[fisher + 1] = fisherShare;
        var activity = ElderActorLaborAttempt.Matrix();
        for (int role = 0; role < ElderActorLaborContract.RoleRows; role++) activity[role][0] = 1;
        activity[gatherer][0] = 0; activity[gatherer][1] = 1;
        if (fishing)
        {
            bool develop = developmentShare > 0 && catalog.ActivityMask[fisher][10];
            activity[fisher][0] = 0; activity[fisher][9] = develop ? 1 - developmentShare : 1;
            if (develop) activity[fisher][10] = developmentShare;
        }
        return new ElderDirective
        {
            SchemaVersion = ElderActorLaborContract.ActionSchemaVersion, LaborSpecialtyAllocation = specialty, LaborActivityAllocation = activity,
            MovementCandidate = -1, CommitTurns = 1, FissionPolicy = (int)camp.FissionPolicy,
            TradeValue = Enumerable.Repeat(1d, ElderTrade.Groups.Length).ToArray(),
            TradeGate = Enumerable.Repeat(ElderTrade.Make, ElderTrade.Groups.Length).ToArray(),
            TradeExportShare = config.TradeExportLabourShare
        };
    }

    // Native decision, then action-12 decisions with different fishing staffing: the native-only blocks become
    // explicitly absent (zero, not present) instead of keeping the native values, and the fishing staffing block
    // follows each executed action.
    private static void ActionTwelveUpdates()
    {
        int request = Index("previous-request-food-per-resident"), route = Index("cloth-route-feasible"), catch_ = Index("frontier-marginal-catch-over-gathering"),
            food = Index("fishing-food-over-meals"), fishers = Index("frontier-fishers-per-resident"), share = Index("previous-fishing-development-share"),
            development = Index("previous-fishing-development-per-resident");
        int fisher = Array.IndexOf(WorkforceRoles.All, WorkforceRoles.Fisher);
        ElderModel9Environment environment = null; ElderModel9Step step = null; int controlled = -1;
        foreach (int seed in new[] { 17, 23, 31, 1101 })
        {
            environment = new ElderModel9Environment { ActorLaborEnabled = true };
            step = environment.Reset(ElderModel9Environment.GenerateOpening(seed, 8, out _), true);
            step = environment.StepLabor(new Dictionary<int, ElderDirective>(), null);
            var candidate = step.Villages.Where(v => v.LaborCatalog != null && v.Observation != null && v.Teacher != null)
                .OrderByDescending(v => v.LaborCatalog.SpecialtyMask[fisher + 1] && v.LaborCatalog.ActivityMask[fisher][9] && v.LaborCatalog.ActivityMask[fisher][10])
                .ThenBy(v => v.Camp).FirstOrDefault();
            if (candidate == null) continue;
            controlled = candidate.Camp;
            if (candidate.LaborCatalog.SpecialtyMask[fisher + 1] && candidate.LaborCatalog.ActivityMask[fisher][9] && candidate.LaborCatalog.ActivityMask[fisher][10]) break;
        }
        Check(controlled >= 0, "an actor-labor village exists");
        var native = step.Villages.First(v => v.Camp == controlled).Observation;
        Check(native.CorePresent[request] && native.CorePresent[route] && native.CorePresent[catch_] && native.CorePresent[fishers],
            "a native decision under the actor-labor environment defines every previous-decision block");
        var plans = new[] { (fisher: .5, develop: .5), (fisher: .25, develop: 0.0), (fisher: 0.0, develop: 0.0) };
        double? previousFishers = null; int changed = 0;
        foreach (var plan in plans)
        {
            var village = step.Villages.First(v => v.Camp == controlled);
            var camp = environment.World.Camps.First(c => c.Id == controlled);
            var action = LaborAction(village.LaborCatalog, camp, environment.World.Config, plan.fisher, plan.develop);
            step = environment.StepLabor(new Dictionary<int, ElderDirective> { [controlled] = action }, null,
                new Dictionary<int, string> { [controlled] = village.LaborCatalog.Digest });
            var next = step.Villages.First(v => v.Camp == controlled);
            if (next.Observation == null) break;
            var executed = next.LaborPrevious.FinalSampledRequest;
            Check(executed.Valid && !next.LaborPrevious.Fallback, "the action-12 fixture action is legal");
            double harvest = executed.AcceptedSpecialtyResidents[fisher + 1] * executed.RequestedActivityAllocation[fisher][9];
            double developing = executed.AcceptedSpecialtyResidents[fisher + 1] * executed.RequestedActivityAllocation[fisher][10];
            double population = Math.Max(1, environment.World.Camps.First(c => c.Id == controlled).Population);
            var o = next.Observation;
            Check(!o.CorePresent[request] && !o.CorePresent[route] && !o.CorePresent[catch_] && !o.CorePresent[food],
                "an action-12 decision has no native routes, requests or catch forecast (absent, not carried over)");
            Check(Enumerable.Range(request, ElderModel9Contract.GoodsServices.Length).All(i => o.Core[i] == 0) && o.Core[catch_] == 0 && o.Core[food] == 0,
                "absent blocks are zero, never the last native values");
            Check(o.ClothingRouteMask.All(x => x) && o.ShelterRouteMask.All(x => x), "absent route feasibility leaves both routes open");
            Check(o.CorePresent[fishers] && o.CorePresent[share] && o.CorePresent[development], "the action-12 fishing staffing block is present");
            Check(Math.Abs(o.Core[fishers] - (float)(harvest / population)) < 1e-6 && Math.Abs(o.Core[development] - (float)(developing / population)) < 1e-6,
                "fishing staffing is the executed action's accepted harvest and development Fishers per resident");
            Check(Math.Abs(o.Core[share] - (float)(developing > 1e-9 ? developing / developing : 0)) < 1e-6,
                "the fishing development share is fishing over all accepted development workers");
            if (previousFishers.HasValue && Math.Abs(previousFishers.Value - o.Core[fishers]) > 1e-9) changed++;
            previousFishers = o.Core[fishers];
        }
        Check(changed > 0 || !step.Villages.Any(v => v.Camp == controlled && v.LaborCatalog != null && v.LaborCatalog.SpecialtyMask[fisher + 1]),
            "the fishing context changes across action-12 decisions");
        Console.WriteLine("ElderModel9Context: controlled camp " + controlled + ", fishing context changed " + changed + " times");
    }
}
