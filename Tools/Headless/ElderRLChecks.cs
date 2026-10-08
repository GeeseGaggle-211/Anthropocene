using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Anthropocene.Simulation;

internal static class ElderRLChecks
{
    private static int checks;
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
    {
        IncludeFields = true
    };

    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception("ElderRL: " + message);
    }

    private static void Near(double actual, double expected, string message,
        double tolerance = 1e-8)
        => Check(Math.Abs(actual - expected) <= tolerance *
            Math.Max(1, Math.Abs(expected)), message + " (" + actual +
            " vs " + expected + ")");

    public static int Run()
    {
        checks = 0;
        Schema(); ObservationBoundary(); DirectiveCausality(); OptionAParity();
        EnvironmentAndReplay();
        Console.WriteLine("PASS: " + checks + " Elder RL assertions");
        return checks;
    }

    private static WorldState Fixture()
    {
        var world = EconomicSettlementChecks.DiagnosticProductiveVillage(24);
        var camp = world.Camps[0];
        world.Config.FissionEnabled = false;
        world.Config.ImmigrationEnabled = false;
        world.Config.TradeEnabled = false;
        world.Config.AnnualHumanGrowthRate = 0;
        return world;
    }

    private static ElderCompiledDecision Compile(WorldState world,
        ElderDirective directive)
    {
        var camp = world.Camps.OrderBy(item => item.Id).First();
        var observation = new ElderObservationBuilder().Build(world, camp.Id,
            true);
        var context = new ElderCompilationContext(world, camp, camp, null,
            observation, camp.Tile, EconomicIntent.From(camp));
        return new NativeElderDirectiveCompiler().Compile(context, directive);
    }

    private static void Schema()
    {
        ElderObservationSchema.Validate(); checks++;
        var encoded = ElderObservationSchema.Encode(new ElderObservation());
        Check(encoded.Values.Length == 512 && encoded.Present.Length == 512,
            "the checked-in tensor registry is exactly 512 slots");
        Check(ElderObservationSchema.Registry.All(item =>
                !string.IsNullOrWhiteSpace(item.Source) &&
                !string.IsNullOrWhiteSpace(item.Units) &&
                !string.IsNullOrWhiteSpace(item.MissingValue) &&
                !string.IsNullOrWhiteSpace(item.Permission)),
            "every registry block declares source, units, missing semantics and permission");
        Check(typeof(ElderCoreObservation).GetField("Camp") == null &&
            typeof(ElderSiteObservation).GetField("Tile") == null &&
            typeof(ElderContactObservation).GetField("Society") == null,
            "raw camp, tile and society IDs stay out of policy-visible records");
        var observation = new ElderObservation();
        Check(!observation.Mask.AllowsMovement(0) &&
            observation.Mask.AllowsMovement(-1),
            "the default categorical mask permits only stay");

        observation.Mask.Movement = true;
        observation.Mask.LegalMovementCandidates = new[] { -1, 2, 5 };
        observation.Commodities = new[]
        {
            new ElderCommodityObservation
            {
                Key = ElderObservationSchema.CommodityRows[0],
                CanProduce = true, CanTrade = false
            },
            new ElderCommodityObservation
            {
                Key = ElderObservationSchema.CommodityRows[1],
                CanProduce = false, CanTrade = true
            }
        };
        encoded = ElderObservationSchema.Encode(observation);
        Check(encoded.Values[482] == 1 && encoded.Values[483] == 0 &&
            encoded.Values[485] == 1 && encoded.Values[488] == 1 &&
            encoded.Values[484] == 0 && encoded.Values[489] == 0,
            "the tensor carries the exact stay/site movement mask, not only its count");
        Check(encoded.Values[318] == 1 && encoded.Values[319] == 0 &&
            encoded.Values[326] == 0 && encoded.Values[327] == 1,
            "commodity rows expose exact production and trade capabilities");
    }

    private static void ObservationBoundary()
    {
        var world = Fixture(); var camp = world.Camps[0];
        string before = SaveCodec.Fingerprint(world);
        var builder = new ElderObservationBuilder();
        var first = builder.Build(world, camp.Id, true, out var metadata);
        var again = builder.Build(world, camp.Id, true, out var againMetadata);
        Check(SaveCodec.Fingerprint(world) == before,
            "observation construction is pure");
        Check(JsonSerializer.Serialize(first, Json) ==
                JsonSerializer.Serialize(again, Json) &&
            metadata.SiteTiles.SequenceEqual(againMetadata.SiteTiles),
            "the same authorized state yields the same observation and candidate ordering");
        Check(first.Sites.Length <= ElderRLContract.MaxLocalSites &&
            first.Fishing.Length <= ElderRLContract.MaxFishingGrounds &&
            first.HerdGroups.Length <= ElderRLContract.MaxHerdGroups &&
            first.Contacts.Length <= ElderRLContract.MaxKnownContacts,
            "all set-valued policy blocks obey encoding caps");
        Check(first.Presence.Sites.Present == first.Sites.Length &&
            first.Presence.Herds.Present == first.HerdGroups.Length,
            "presence summaries describe encoded records");
        Check(metadata.ControlledCampId == camp.Id &&
            metadata.SiteTiles.Length == first.Sites.Length,
            "raw compilation identities are present only in metadata");
        var tensor = ElderObservationSchema.Encode(first);
        Check(tensor.Present[64] && tensor.Present[80] &&
            tensor.Values[80] == 1 && tensor.Values[81] == 1 &&
            tensor.Values[82] == 1 && tensor.Values[83] == 1,
            "the PPO tensor carries legal capabilities and every continuous action mask");
        Check(first.Capabilities.Select(item => item.Key).SequenceEqual(
                ElderObservationSchema.CapabilityRows) &&
            first.Commodities.Select(item => item.Key).SequenceEqual(
                ElderObservationSchema.CommodityRows) &&
            !tensor.Present[41],
            "checked-in row keys and missing previous-decision presence are stable");

        var foreign = new Camp
        {
            Id = world.NextId++, Player = 1,
            Tile = world.Map.At(18, 12), Population = 400,
            PlantFood = 123456, AnimalFood = 789
        };
        world.Camps.Add(foreign); Ownership.Bind(world);
        var hiddenA = builder.Build(world, camp.Id, true);
        foreign.Population = 2; foreign.PlantFood = 0;
        foreign.Inventory.Tools = 999999;
        var hiddenB = builder.Build(world, camp.Id, true);
        Check(JsonSerializer.Serialize(hiddenA, Json) ==
                JsonSerializer.Serialize(hiddenB, Json),
            "unseen foreign population, stocks and tools cannot leak into the Elder observation");

        var orderingWorld = Fixture(); var orderingCamp = orderingWorld.Camps[0];
        orderingWorld.Herds.Add(new Herd
        {
            Id = orderingWorld.NextId++, Tile = orderingCamp.Tile,
            DomesticCamp = orderingCamp.Id,
            OwnerSociety = orderingCamp.SocietyId,
            Species = orderingWorld.Herds[0].Species, Animals = 3,
            Capacity = 1000, LastNutrition = .4,
            ManagementFulfillment = .6, StartingAnimals = 4,
            NewbornAnimals = 1, StarvationDeaths = 1
        });
        var orderedA = ElderObservationSchema.Encode(builder.Build(
            orderingWorld, orderingCamp.Id, true));
        var owned = orderingWorld.Herds.Where(herd =>
                herd.DomesticCamp == orderingCamp.Id).ToArray();
        int firstId = owned[0].Id; owned[0].Id = owned[1].Id;
        owned[1].Id = firstId;
        var orderedB = ElderObservationSchema.Encode(builder.Build(
            orderingWorld, orderingCamp.Id, true));
        Check(orderedA.Values.Skip(248).Take(64).SequenceEqual(
                orderedB.Values.Skip(248).Take(64)) &&
            orderedA.Present.Skip(248).Take(64).SequenceEqual(
                orderedB.Present.Skip(248).Take(64)),
            "owned-herd tensor ordering is invariant to raw entity-ID reassignment");
    }

    private static void DirectiveCausality()
    {
        var fieldWorld = Fixture();
        var noField = Compile(fieldWorld, new ElderDirective
        {
            RetainedFieldOperation = 0, RetainedHerdFraction = 1,
            DevelopmentBudget = 0
        });
        var fullField = Compile(fieldWorld, new ElderDirective
        {
            RetainedFieldOperation = 1, RetainedHerdFraction = 1,
            DevelopmentBudget = 0
        });
        Check(fullField.Realized.RetainedFieldOperation >
                noField.Realized.RetainedFieldOperation + 1e-9,
            "field-retention head actuates a real retained-operation choice");

        var herdWorld = Fixture();
        var noHerd = Compile(herdWorld, new ElderDirective
        {
            RetainedFieldOperation = 0, RetainedHerdFraction = 0,
            DevelopmentBudget = 0
        });
        var fullHerd = Compile(herdWorld, new ElderDirective
        {
            RetainedFieldOperation = 0, RetainedHerdFraction = 1,
            DevelopmentBudget = 0
        });
        Check(fullHerd.Realized.RetainedHerdFraction >
                noHerd.Realized.RetainedHerdFraction + 1e-9 &&
            fullHerd.Intent.HerdTarget > noHerd.Intent.HerdTarget,
            "herd-retention head actuates the authoritative herd target");

        var developmentWorld = Fixture();
        var none = Compile(developmentWorld, new ElderDirective
        {
            RetainedFieldOperation = 0, RetainedHerdFraction = 0,
            DevelopmentBudget = 0, FieldDevelopmentShare = 1
        });
        var admitted = Compile(developmentWorld, new ElderDirective
        {
            RetainedFieldOperation = 0, RetainedHerdFraction = 0,
            DevelopmentBudget = 1, FieldDevelopmentShare = 1
        });
        Check(admitted.AcceptedDevelopmentPeople >
                none.AcceptedDevelopmentPeople + 1e-9,
            "development-budget head admits real compiled Development when feasible (" +
                none.AcceptedDevelopmentPeople + " -> " +
                admitted.AcceptedDevelopmentPeople + ", requested=" +
                admitted.RequestedDevelopmentPeople + ")");

        var gatedWorld = EconomicSettlementChecks.DiagnosticMobileMixed(
            cultivation: true, productive: false);
        var gatedCamp = gatedWorld.Camps[0];
        DemandController.Plan(gatedWorld, gatedCamp,
            EconomicIntent.From(gatedCamp), null, out _, out var gatedBaseline);
        var physicalField = Compile(gatedWorld, new ElderDirective
        {
            RetainedFieldOperation = 0,
            RetainedHerdFraction = 1,
            DevelopmentBudget = .5,
            FieldDevelopmentShare = 1
        });
        Check(gatedBaseline.SelectedFieldBudget <= 1e-9 &&
            physicalField.AcceptedFieldDevelopmentPeople > 1e-9,
            "learned field Development selects physical options without inheriting Option A profitability gates");

        var competitionWorld = Fixture();
        competitionWorld.Config.AnnualHumanGrowthRate = .05;
        var competitionCamp = competitionWorld.Camps[0];
        DemandController.Plan(competitionWorld, competitionCamp,
            EconomicIntent.From(competitionCamp), null, out _,
            out var competitionAllocation,
            elderDirective: new ElderDirective
            {
                RetainedFieldOperation = 0,
                RetainedHerdFraction = 1,
                DevelopmentBudget = .5,
                FieldDevelopmentShare = 1
            }, elderInterventions: new List<ElderIntervention>());
        var competitionGrowth = competitionAllocation.Requests
            .FirstOrDefault(request => request.Service ==
                "food growth gathering");
        Check(competitionGrowth != null && competitionGrowth.Units > 1 &&
            competitionGrowth.Granted > 0 &&
            competitionAllocation.SelectedFieldBudget > 1 &&
            Math.Abs(competitionAllocation.FinalFieldBudget -
                competitionAllocation.SelectedFieldBudget) <= 1e-8,
            "a selected learned field prefix is reserved before competing food-for-growth gathering uses residual labor");

        var exactWorld = Fixture();
        var exact = Compile(exactWorld, new ElderDirective
        {
            RetainedFieldOperation = .37,
            RetainedHerdFraction = .37,
            DevelopmentBudget = 0
        });
        Check(Math.Abs(exact.Accepted.RetainedFieldOperation - .37) < .08 &&
            Math.Abs(exact.Accepted.RetainedHerdFraction - .37) < .08,
            "retention targets use adjacent physical/integer options instead of the coarse Option A search grid");

        var splitWorld = Fixture();
        var herdSplit = Compile(splitWorld, new ElderDirective
        {
            RetainedFieldOperation = 0, RetainedHerdFraction = 1,
            DevelopmentBudget = 1, FieldDevelopmentShare = 0
        });
        var fieldSplit = Compile(splitWorld, new ElderDirective
        {
            RetainedFieldOperation = 0, RetainedHerdFraction = 1,
            DevelopmentBudget = 1, FieldDevelopmentShare = 1
        });
        Check(fieldSplit.AcceptedFieldDevelopmentPeople >
                herdSplit.AcceptedFieldDevelopmentPeople + 1e-9 &&
            herdSplit.AcceptedHerdDevelopmentPeople >
                fieldSplit.AcceptedHerdDevelopmentPeople + 1e-9,
            "development-split head routes the common budget between physical channels (herd split=" +
                herdSplit.AcceptedFieldDevelopmentPeople + "/" +
                herdSplit.AcceptedHerdDevelopmentPeople + ", field split=" +
                fieldSplit.AcceptedFieldDevelopmentPeople + "/" +
                fieldSplit.AcceptedHerdDevelopmentPeople + ")");

        var invalid = Compile(Fixture(), new ElderDirective
        {
            MovementCandidate = 999,
            RetainedFieldOperation = double.NaN,
            RetainedHerdFraction = 2
        });
        Check(invalid.Accepted.MovementCandidate == -1 &&
            invalid.Interventions.Any(item => item.Kind ==
                ElderInterventionKind.InvalidActionFallback &&
                item.Control == "movement-candidate"),
            "an invalid categorical action explicitly falls back to stay");
        Check(invalid.Interventions.Any(item => item.Control ==
                "retained-field-operation") &&
            invalid.Interventions.Any(item => item.Control ==
                "retained-herd-fraction"),
            "continuous defensive clipping is explicit");
    }

    private static void OptionAParity()
    {
        foreach (var fixture in new[]
        {
            ("A", EconomicSettlementChecks.DiagnosticVillage(0,
                husbandry: false, housing: 15)),
            ("B", EconomicSettlementChecks.DiagnosticVillage(8)),
            ("C", EconomicSettlementChecks.DiagnosticVillage(400)),
            ("D", EconomicSettlementChecks.DiagnosticVillage(4000)),
            ("E", EconomicSettlementChecks.DiagnosticPastoralOpportunity(
                Technology.AnimalHusbandry)),
            ("F", EconomicSettlementChecks.DiagnosticVillage(80,
                primary: Technology.Cultivation)),
            ("G", EconomicSettlementChecks.DiagnosticPastoralOpportunity(
                Technology.Cultivation)),
            ("H", EconomicSettlementChecks.DiagnosticVillage(0,
                husbandry: true, housing: 15)),
            ("I", EconomicSettlementChecks.DiagnosticProductiveVillage(24))
        })
        {
            var world = fixture.Item2;
            var camp = world.Camps[0]; var settings = EconomicIntent.From(camp);
            var baseline = DemandController.Plan(world, camp, settings, null,
                out var production, out var allocation);
            var observation = new ElderObservationBuilder().Build(world,
                camp.Id, false);
            var directive = OptionAElderPolicy.FromCommittedState(world,
                camp.Id).Decide(observation);
            var compiled = new NativeElderDirectiveCompiler().Compile(
                new ElderCompilationContext(world, camp, camp, null,
                    observation, camp.Tile, settings), directive);
            Check(compiled.Intent.Stamp == baseline.Stamp,
                "Option A's strategic directive reproduces its compiled intent on fixture " +
                    fixture.Item1 + " (baseline=" + baseline.Stamp +
                    ", compiled=" + compiled.Intent.Stamp + ")");
            Check((compiled.Production?.ToString() ?? "") ==
                    (production?.ToString() ?? ""),
                "Option A's strategic directive reproduces its production order on fixture " +
                    fixture.Item1);

            var baselineWorld = SaveCodec.Load(SaveCodec.Save(world));
            var elderWorld = SaveCodec.Load(SaveCodec.Save(world));
            var baselineCamp = baselineWorld.Camps[0];
            var elderCamp = elderWorld.Camps[0];
            var baselineOrders = new Dictionary<int, List<Order>>
            {
                [baselineCamp.Player] = new List<Order>
                {
                    new Order(OrderKind.NoOp, baselineCamp.Id)
                }
            };
            var elderOrders = new Dictionary<int, List<Order>>
            {
                [elderCamp.Player] = new List<Order>
                {
                    new Order(OrderKind.NoOp, elderCamp.Id)
                }
            };
            var elderObservation = new ElderObservationBuilder().Build(
                elderWorld, elderCamp.Id, false);
            var elderDirective = OptionAElderPolicy.FromCommittedState(
                elderWorld, elderCamp.Id).Decide(elderObservation);
            var baselinePrepared = TurnPlanner.Prepare(baselineWorld,
                baselineOrders);
            var elderPrepared = TurnPlanner.PrepareWithElderDirectives(
                elderWorld, elderOrders,
                new Dictionary<int, ElderDirective>
                {
                    [elderCamp.Id] = elderDirective
                },
                new Dictionary<int, ElderObservation>
                {
                    [elderCamp.Id] = elderObservation
                }, new NativeElderDirectiveCompiler());
            new TurnResolver().Resolve(baselineWorld, baselinePrepared);
            new TurnResolver().Resolve(elderWorld, elderPrepared);
            Check(SaveCodec.Fingerprint(elderWorld) ==
                    SaveCodec.Fingerprint(baselineWorld),
                "Option A through the Elder seam reproduces the resolved world on fixture " +
                    fixture.Item1);
        }
    }

    private static ElderResetRequest Request(byte[] snapshot,
        int camp, bool movement = false, int limit = 4)
        => new ElderResetRequest
        {
            Snapshot = snapshot,
            ControlledCampId = camp,
            Options = new ElderEnvironmentOptions
            {
                EpisodeDecisionLimit = limit,
                MovementEnabled = movement
            },
            ExpectedSimulatorVersion = NativeElderEnvironment.SimulatorVersion
        };

    private static void EnvironmentAndReplay()
    {
        var source = Fixture(); int campId = source.Camps[0].Id;
        byte[] snapshot = SaveCodec.Save(source);
        var scalar = new NativeElderEnvironment(execution:
            new PhaseExecution());
        var parallel = new NativeElderEnvironment(execution:
            new PhaseExecution(4, 2, true, 1));
        var a0 = scalar.Reset(Request(snapshot, campId));
        var b0 = parallel.Reset(Request(snapshot, campId));
        Check(a0.InitialStateFingerprint == b0.InitialStateFingerprint &&
            JsonSerializer.Serialize(a0.Observation, Json) ==
                JsonSerializer.Serialize(b0.Observation, Json),
            "scalar and parallel Reset produce the same first observation");
        var action = new ElderDirective
        {
            RetainedFieldOperation = .75,
            RetainedHerdFraction = .8,
            DevelopmentBudget = .4,
            FieldDevelopmentShare = .6
        };
        var a1 = scalar.Step(action); var b1 = parallel.Step(action);
        Check(a1.StateFingerprint == b1.StateFingerprint,
            "scalar and reversed parallel Step resolve identically");
        Near(a1.Reward.RawWelfareFlow,
            a1.Reward.PriorPopulation * a1.Reward.RealizedQoL *
                a1.Reward.ElapsedYears,
            "scalar reward diagnostics reconcile to realized local welfare flow");
        Near(a1.ScalarReward, a1.Reward.RawWelfareFlow /
            (Math.Max(1, source.Camps[0].Population) *
             Math.Max(1e-12, a1.ElapsedYears)),
            "normalized scalar uses the fixed initial-cohort scale");
        Check(a1.Decision.Intent != null &&
            a1.Decision.Interventions != null &&
            a1.Observation.Previous.Present,
            "Step reports compiled intent, interventions and previous realized outcome");
        Near(a1.Decision.Realized.RetainedFieldOperation,
            a1.Decision.Accepted.RetainedFieldOperation,
            "resolved field operation reconciles to authoritative labor");
        Near(a1.Decision.Realized.DevelopmentBudget,
            a1.Decision.Accepted.DevelopmentBudget,
            "resolved Development budget reconciles to authoritative labor");
        Check(a1.Decision.Interventions.Any(item =>
                item.Control == "retained-field-operation") &&
            a1.Decision.Interventions.Any(item =>
                item.Control == "retained-herd-fraction") &&
            a1.Decision.Interventions.Any(item =>
                item.Control == "development-budget") &&
            a1.Decision.Interventions.Any(item =>
                item.Control == "field-development-share"),
            "every repaired strategic control has an explicit intervention");

        NativeElderEnvironment.VerifyReplay(scalar.ReplayLog,
            new PhaseExecution(4, 2, true, 1));
        checks++;
        double recordedAcceptance = scalar.ReplayLog.Steps[0].Accepted
            .DevelopmentBudget;
        scalar.ReplayLog.Steps[0].Accepted.DevelopmentBudget =
            recordedAcceptance == 0 ? .125 : 0;
        bool rejectedTamper = false;
        try
        {
            NativeElderEnvironment.VerifyReplay(scalar.ReplayLog);
        }
        catch (InvalidOperationException error)
        {
            rejectedTamper = error.Message.Contains("accepted action");
        }
        scalar.ReplayLog.Steps[0].Accepted.DevelopmentBudget =
            recordedAcceptance;
        Check(rejectedTamper,
            "replay verification rejects a changed recorded accepted action");
        var continuedSnapshot = scalar.CaptureSnapshot();
        var restored = new NativeElderEnvironment();
        restored.Reset(Request(continuedSnapshot, campId, limit: 3));
        var a2 = scalar.Step(action); var restored2 = restored.Step(action);
        Check(a2.StateFingerprint == restored2.StateFingerprint,
            "save continuation reaches the uninterrupted state fingerprint");

        var movement = new NativeElderEnvironment();
        var moveReset = movement.Reset(Request(snapshot, campId,
            movement: true, limit: 2));
        int candidate = moveReset.Observation.Mask.LegalMovementCandidates
            .FirstOrDefault(value => value >= 0, -1);
        Check(candidate >= 0,
            "movement fixture exposes at least one masked local candidate");
        var moved = movement.Step(new ElderDirective
        {
            RetainedFieldOperation = 0,
            RetainedHerdFraction = 1,
            MovementCandidate = candidate
        });
        int current = Array.FindIndex(moved.Observation.Sites,
            site => site.Current);
        Check(moved.Decision.Accepted.MovementCandidate == candidate &&
            current >= 0 && moved.ObservationMetadata.SiteTiles[current] ==
                moveReset.ObservationMetadata.SiteTiles[candidate],
            "movement head passes through ordinary placement and changes the accepted camp tile");

        var invalid = new NativeElderEnvironment();
        invalid.Reset(Request(snapshot, campId, movement: true, limit: 1));
        var rejected = invalid.Step(new ElderDirective
        {
            MovementCandidate = int.MaxValue
        });
        Check(rejected.Decision.Accepted.MovementCandidate == -1 &&
            rejected.Decision.Interventions.Any(intervention =>
                intervention.Kind ==
                    ElderInterventionKind.InvalidActionFallback),
            "Step logs categorical rejection instead of clamping to another site");

        var badStream = Request(snapshot, campId); badStream.RandomStream = 1;
        bool streamRejected = false;
        try { new NativeElderEnvironment().Reset(badStream); }
        catch (NotSupportedException) { streamRejected = true; }
        Check(streamRejected,
            "Reset rejects an unsupported random stream instead of treating it as an inert label");

        var badOptions = Request(snapshot, campId);
        badOptions.Options.EnvironmentSchemaVersion++;
        bool optionsRejected = false;
        try { new NativeElderEnvironment().Reset(badOptions); }
        catch (InvalidOperationException) { optionsRejected = true; }
        Check(optionsRejected,
            "Reset validates the environment schema carried by its options payload");
    }
}
