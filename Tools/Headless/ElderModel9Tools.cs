using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Anthropocene.Simulation;

// Headless tools for native Elder contract 7 (model9): opening snapshots, the teacher-as-action fidelity probe,
// native throughput and NPY teacher-data shards. Shipped configuration (fission, immigration, trade, surplus births,
// kernel split) unless a tool says otherwise.
internal static class ElderModel9Tools
{
    private static string I(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    internal static byte[] Opening(int seed, int players, out int openingTurns) => ElderModel9Environment.GenerateOpening(seed, players, out openingTurns);

    // --model9-probe=<seed>,<turns>: lockstep teacher run (A) and teacher-labels-as-actions run (B) from one snapshot.
    public static int Probe(int seed, int turns, int players = 8)
    {
        var snapshot = Opening(seed, players, out int opening);
        var a = new ElderModel9Environment(); var b = new ElderModel9Environment();
        a.Reset(snapshot, true); b.Reset(snapshot, true);
        int exact = 0, steps = 0, unrepresentable = 0, moves = 0; bool diverged = false; double welfareA = 0, welfareB = 0, decisions = 0, msA = 0, msB = 0, repairs = 0, labelled = 0;
        for (int t = 0; t < turns; t++)
        {
            var stepA = a.Step(new Dictionary<int, ElderDirective>());
            // Economic fidelity: movement and fission stay native (contract-7 stages 1-3).
            var directives = stepA.Villages.Where(v => v.Teacher != null).ToDictionary(v => v.Camp, v => { var d = v.Teacher.Action.Copy(); d.MovementCandidate = ElderModel9Contract.NativeMovement; d.FissionPolicy = -1; return d; });
            var stepB = b.Step(directives);
            if (!diverged)
                foreach (var va in stepA.Villages)
                {
                    var vb = stepB.Villages.FirstOrDefault(v => v.Camp == va.Camp);
                    if (vb != null && va.Population == vb.Population && Math.Abs(va.Welfare - vb.Welfare) <= 1e-6 * Math.Max(1, va.Welfare)) continue;
                    diverged = true;
                    string L(ElderModel9Label l) => l == null ? "none" : "f" + I(l.Action.RetainedFieldOperation) + " h" + I(l.Action.RetainedHerdFraction) + " d" + I(l.Action.DevelopmentBudget)
                        + " s" + I(l.Action.FieldDevelopmentShare) + " lv" + string.Join(",", l.Action.ServiceLevels.Select(I)) + " r" + l.Action.ClothingRoute + l.Action.ShelterRoute;
                    Console.WriteLine("MODEL9_DIVERGE turn=" + stepA.Turn + " camp=" + va.Camp + " pop=" + va.Population + "/" + (vb?.Population ?? -1) + " welfare=" + I(va.Welfare) + "/" + I(vb?.Welfare ?? 0)
                        + " labelA=" + L(va.Teacher) + " realizedB=" + L(vb?.Teacher) + " staffA=" + va.Staffing + " staffB=" + vb?.Staffing
                        + " interventions=" + string.Join(";", (vb?.Interventions ?? Array.Empty<ElderIntervention>()).Select(i => i.Control + ":" + I(i.Requested) + ">" + I(i.Accepted))));
                    break;
                }
            steps++; if (stepA.Fingerprint == stepB.Fingerprint) exact++;
            welfareA += stepA.Villages.Sum(v => v.Welfare); welfareB += stepB.Villages.Sum(v => v.Welfare);
            decisions += stepA.Villages.Count(v => v.Teacher != null); msA += stepA.NativeMilliseconds; msB += stepB.NativeMilliseconds;
            repairs += stepB.Villages.Sum(v => v.Interventions.Length); labelled += directives.Count;
            unrepresentable += stepA.Villages.Count(v => v.Teacher != null && !v.Teacher.MovementMask); moves += stepA.Villages.Count(v => v.Teacher != null && (v.Teacher.Action.MovementCandidate >= 0 || !v.Teacher.MovementMask));
            Console.WriteLine("MODEL9_PROBE seed=" + seed + " turn=" + stepA.Turn + " villages=" + stepA.Villages.Count + " decisions=" + directives.Count
                + " exact=" + (stepA.Fingerprint == stepB.Fingerprint) + " welfareA=" + I(stepA.Villages.Sum(v => v.Welfare)) + " welfareB=" + I(stepB.Villages.Sum(v => v.Welfare))
                + " popA=" + a.World.Camps.Sum(a.World.LinkedPopulation) + " popB=" + b.World.Camps.Sum(b.World.LinkedPopulation)
                + " msA=" + I(stepA.NativeMilliseconds) + " msB=" + I(stepB.NativeMilliseconds));
        }
        ElderModel9Environment.VerifyReplay(b.Replay);
        Console.WriteLine("MODEL9_PROBE_SUMMARY seed=" + seed + " openingTurns=" + opening + " steps=" + steps + " exactSteps=" + exact
            + " welfareRatio=" + I(welfareA > 0 ? welfareB / welfareA : 0) + " decisions=" + decisions
            + " teacherDecisionsPerSecond=" + I(decisions / Math.Max(1e-9, msA / 1000)) + " directiveDecisionsPerSecond=" + I(labelled / Math.Max(1e-9, msB / 1000))
            + " heuristicMoves=" + moves + " unrepresentableMoves=" + unrepresentable + " interventionsPerDecision=" + I(labelled > 0 ? repairs / labelled : 0) + " replay=verified");
        return 0;
    }

    // --model9-profile=<seed>,<turns>[,players]: phase timing of teacher steps (heuristic for every village).
    public static int Profile(int seed, int turns, int players = 8)
    {
        var env = new ElderModel9Environment();
        env.Reset(Opening(seed, players, out _), true);
        PhaseProfile.Reset(); PhaseProfile.Enabled = true;
        var sum = new double[9]; long decisions = 0; double villagesSum = 0;
        for (int t = 0; t < turns; t++)
        {
            var s = env.Step(new Dictionary<int, ElderDirective>());
            var parts = new[] { s.DecideMs, s.PrepareMs, s.LabelMs, s.ResolveMs, s.FingerprintMs, s.ObserveMs, s.EncodeMs, s.PrivilegedMs, s.NativeMilliseconds };
            for (int i = 0; i < parts.Length; i++) sum[i] += parts[i];
            decisions += s.Villages.Count(v => v.Teacher != null); villagesSum += s.Villages.Count;
            Console.WriteLine("MODEL9_FINGERPRINT turn=" + s.Turn + " villages=" + s.Villages.Count + " " + s.Fingerprint);
        }
        PhaseProfile.Enabled = false;
        foreach (var counter in PhaseProfile.Counters())
            Console.WriteLine("MODEL9_COUNT " + counter.Key + "/turn=" + I(counter.Value / (double)turns));
        Console.WriteLine("MODEL9_BOUND evaluations=" + SocialRelocationForecastCache.BoundEvaluations
            + " pruned=" + SocialRelocationForecastCache.BoundPruned + " audit=" + SocialRelocationForecastCache.AuditBounds);
        foreach (var row in PhaseProfile.Rows().OrderByDescending(r => r.ExclusiveMs).Take(24))
            Console.WriteLine("MODEL9_PHASE exclusiveMsPerTurn=" + I(row.ExclusiveMs / turns) + " share=" + I(100 * row.ExclusiveMs / Math.Max(1e-9, sum[8])) + "% calls/turn=" + I(row.Calls / (double)turns) + " " + row.Path);
        string[] names = { "decide", "prepare", "labels", "resolve", "fingerprint", "observe", "encode", "privileged", "total" };
        Console.WriteLine("MODEL9_PROFILE seed=" + seed + " players=" + players + " turns=" + turns + " meanVillages=" + I(villagesSum / turns) + " decisions=" + decisions
            + " decisionsPerSecond=" + I(decisions / Math.Max(1e-9, sum[8] / 1000)) + " " + string.Join(" ", names.Select((n, i) => n + "Ms=" + I(sum[i] / turns) + "(" + I(100 * sum[i] / Math.Max(1e-9, sum[8])) + "%)")));
        return 0;
    }

    // --model9-teacher=<outDir>,<firstSeed>,<lastSeed>,<turns>[,players]: heuristic rollouts written as NPY shards, one per world.
    public static int Teacher(string outDir, int firstSeed, int lastSeed, int turns, int players = 8)
    {
        Directory.CreateDirectory(outDir);
        long totalDecisions = 0; double nativeMs = 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        for (int seed = firstSeed; seed <= lastSeed; seed++)
        {
            var snapshot = Opening(seed, players, out _);
            var env = new ElderModel9Environment();
            var reset = env.Reset(snapshot, true);
            var rows = new List<(int Turn, ElderModel9Village Decision, ElderModel9Tensor Obs, float[] Privileged)>();
            var pending = reset.Villages.Where(v => v.Observation != null).ToDictionary(v => v.Camp, v => (v.Observation, v.Privileged));
            int turn0 = env.World.Turn;
            var outcomes = new List<(int Turn, ElderModel9Village V)>();
            for (int t = 0; t < turns; t++)
            {
                int turn = env.World.Turn;
                var step = env.Step(new Dictionary<int, ElderDirective>());
                nativeMs += step.NativeMilliseconds;
                foreach (var v in step.Villages)
                {
                    if (pending.TryGetValue(v.Camp, out var obs) && v.Teacher != null) rows.Add((turn, v, obs.Observation, obs.Privileged));
                }
                totalDecisions += step.Villages.Count(v => v.Teacher != null);
                pending = step.Villages.Where(v => v.Observation != null && !v.Terminated).ToDictionary(v => v.Camp, v => (v.Observation, v.Privileged));
            }
            Write(Path.Combine(outDir, "world-" + seed.ToString("D5")), seed, rows);
            Console.WriteLine("MODEL9_TEACHER seed=" + seed + " rows=" + rows.Count + " elapsed=" + I(clock.Elapsed.TotalSeconds));
        }
        Console.WriteLine("MODEL9_TEACHER_SUMMARY worlds=" + (lastSeed - firstSeed + 1) + " decisions=" + totalDecisions
            + " nativeDecisionsPerSecond=" + I(totalDecisions / Math.Max(1e-9, nativeMs / 1000))
            + " wallDecisionsPerSecond=" + I(totalDecisions / Math.Max(1e-9, clock.Elapsed.TotalSeconds)));
        return 0;
    }

    private static void Write(string dir, int seed, List<(int Turn, ElderModel9Village V, ElderModel9Tensor Obs, float[] Privileged)> rows)
    {
        Directory.CreateDirectory(dir);
        int n = rows.Count, core = ElderModel9Contract.CoreWidth, services = ElderModel9Contract.GoodsServices.Length;
        NpyWriter.Write(Path.Combine(dir, "core.npy"), rows.SelectMany(r => r.Obs.Core).ToArray(), n, core);
        NpyWriter.Write(Path.Combine(dir, "core_present.npy"), rows.SelectMany(r => r.Obs.CorePresent).ToArray(), n, core);
        NpyWriter.Write(Path.Combine(dir, "privileged.npy"), rows.SelectMany(r => r.Privileged).ToArray(), n, ElderModel9Contract.PrivilegedFeatures.Length);
        for (int s = 0; s < ElderModel9Contract.Sets.Length; s++)
        {
            var set = ElderModel9Contract.Sets[s];
            NpyWriter.Write(Path.Combine(dir, set.Name + ".npy"), rows.SelectMany(r => r.Obs.Sets[s]).ToArray(), n, set.Rows, set.Width);
            NpyWriter.Write(Path.Combine(dir, set.Name + "_present.npy"), rows.SelectMany(r => r.Obs.RowPresent[s]).ToArray(), n, set.Rows);
        }
        NpyWriter.Write(Path.Combine(dir, "movement_mask.npy"), rows.SelectMany(r => r.Obs.MovementMask).ToArray(), n, ElderModel9Contract.MovementSlots);
        NpyWriter.Write(Path.Combine(dir, "clothing_route_mask.npy"), rows.SelectMany(r => r.Obs.ClothingRouteMask).ToArray(), n, 2);
        NpyWriter.Write(Path.Combine(dir, "shelter_route_mask.npy"), rows.SelectMany(r => r.Obs.ShelterRouteMask).ToArray(), n, 2);
        var labels = rows.Select(r => r.V.Teacher).ToArray();
        NpyWriter.Write(Path.Combine(dir, "strategic.npy"), labels.SelectMany(l => new[] { (float)l.Action.RetainedFieldOperation, (float)l.Action.RetainedHerdFraction,
            (float)l.Action.DevelopmentBudget, (float)l.Action.FieldDevelopmentShare }).ToArray(), n, 4);
        NpyWriter.Write(Path.Combine(dir, "strategic_mask.npy"), labels.SelectMany(l => new[] { l.StrategicFieldMask, l.StrategicHerdMask, l.DevelopmentMask, l.DevelopmentShareMask }).ToArray(), n, 4);
        NpyWriter.Write(Path.Combine(dir, "service_levels.npy"), labels.SelectMany(l => l.Action.ServiceLevels.Select(x => (float)x)).ToArray(), n, services);
        NpyWriter.Write(Path.Combine(dir, "service_mask.npy"), labels.SelectMany(l => l.ServiceMask).ToArray(), n, services);
        NpyWriter.Write(Path.Combine(dir, "clothing_route.npy"), labels.Select(l => l.Action.ClothingRoute).ToArray(), n);
        NpyWriter.Write(Path.Combine(dir, "clothing_route_label_mask.npy"), labels.Select(l => l.ClothingRouteMask).ToArray(), n);
        NpyWriter.Write(Path.Combine(dir, "shelter_route.npy"), labels.Select(l => l.Action.ShelterRoute).ToArray(), n);
        NpyWriter.Write(Path.Combine(dir, "shelter_route_label_mask.npy"), labels.Select(l => l.ShelterRouteMask).ToArray(), n);
        NpyWriter.Write(Path.Combine(dir, "movement.npy"), labels.Select(l => l.Action.MovementCandidate + 1).ToArray(), n);
        NpyWriter.Write(Path.Combine(dir, "movement_label_mask.npy"), labels.Select(l => l.MovementMask).ToArray(), n);
        NpyWriter.Write(Path.Combine(dir, "fission_policy.npy"), labels.Select(l => l.Action.FissionPolicy).ToArray(), n);
        NpyWriter.Write(Path.Combine(dir, "repair.npy"), labels.Select(l => (float)l.RepairSize).ToArray(), n);
        NpyWriter.Write(Path.Combine(dir, "camp.npy"), rows.Select(r => r.V.Camp).ToArray(), n);
        NpyWriter.Write(Path.Combine(dir, "turn.npy"), rows.Select(r => r.Turn).ToArray(), n);
        NpyWriter.Write(Path.Combine(dir, "society.npy"), rows.Select(r => r.V.Society).ToArray(), n);
        NpyWriter.Write(Path.Combine(dir, "parent_society.npy"), rows.Select(r => r.V.ParentSociety).ToArray(), n);
        NpyWriter.Write(Path.Combine(dir, "prior_population.npy"), rows.Select(r => r.V.PriorPopulation).ToArray(), n);
        NpyWriter.Write(Path.Combine(dir, "population.npy"), rows.Select(r => r.V.Population).ToArray(), n);
        NpyWriter.Write(Path.Combine(dir, "qol.npy"), rows.Select(r => (float)r.V.QoL).ToArray(), n);
        NpyWriter.Write(Path.Combine(dir, "nutrition.npy"), rows.Select(r => (float)r.V.Nutrition).ToArray(), n);
        NpyWriter.Write(Path.Combine(dir, "years.npy"), rows.Select(r => (float)r.V.Years).ToArray(), n);
        NpyWriter.Write(Path.Combine(dir, "welfare.npy"), rows.Select(r => (float)r.V.Welfare).ToArray(), n);
        NpyWriter.Write(Path.Combine(dir, "terminated.npy"), rows.Select(r => r.V.Terminated).ToArray(), n);
        File.WriteAllText(Path.Combine(dir, "meta.json"), "{\"seed\": " + seed + ", \"rows\": " + n + ", \"contract\": " + ElderModel9Contract.NativeContractVersion
            + ", \"coreWidth\": " + core + ", \"buildHash\": \"" + ElderModel9Environment.BuildHash + "\"}\n");
    }
}
