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
        breaking.World.Camps.Single(c => c.Id == camp).DeficitTurns = 1;
        if (Memory(breaking).HasCommitment)
        {
            var broken = breaking.Step(new Dictionary<int, ElderDirective> { [camp] = Directive(0, .1, .1, .6, .9) });
            Check(Intervened(broken, "survival shortfall") && Memory(breaking).LastCommitmentBreak == "survival shortfall" && !Memory(breaking).HasCommitment,
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
        Console.WriteLine("PASS: " + checks + " model9 contract-9 assertions");
        return checks;
    }
}
