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
        Console.WriteLine("PASS: " + checks + " model9 contract-8 assertions");
        return checks;
    }
}
