using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    public sealed class NativeElderEnvironment : IElderEnvironment
    {
        private readonly Func<string, int, WorldState> fixtureLoader;
        private readonly PhaseExecution execution;
        private readonly IElderDirectiveCompiler compiler;
        private readonly ElderObservationBuilder observationBuilder =
            new ElderObservationBuilder();
        private WorldState world;
        private ElderEnvironmentOptions options;
        private ElderObservation observation;
        private ElderObservationMetadata observationMetadata;
        private int controlledCampId = -1, controlledPlayer = -1;
        private int episodeStep;
        private bool ended;
        private Dictionary<int, List<Order>> replayBackgroundOverride;

        public ElderReplayLog ReplayLog { get; private set; }

        public static string SimulatorVersion => "world-7/generator-" +
            WorldState.GeneratorVersion + "/elder-" +
            ElderRLContract.EnvironmentSchemaVersion;

        public static string BuildHash => typeof(NativeElderEnvironment).Assembly
            .ManifestModule.ModuleVersionId.ToString("N");

        public NativeElderEnvironment(
            Func<string, int, WorldState> fixtureLoader = null,
            PhaseExecution execution = null,
            IElderDirectiveCompiler compiler = null)
        {
            this.fixtureLoader = fixtureLoader;
            this.execution = execution ?? new PhaseExecution();
            this.compiler = compiler ?? new NativeElderDirectiveCompiler();
        }

        public byte[] CaptureSnapshot()
        {
            if (world == null) throw new InvalidOperationException(
                "Reset must precede snapshot capture.");
            return SaveCodec.Save(world);
        }

        public ElderResetResult Reset(ElderResetRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var requestedOptions = request.Options ?? throw new ArgumentException(
                "Environment options are required.", nameof(request));
            options = new ElderEnvironmentOptions
            {
                EnvironmentSchemaVersion = requestedOptions.EnvironmentSchemaVersion,
                EpisodeDecisionLimit = requestedOptions.EpisodeDecisionLimit,
                MovementEnabled = requestedOptions.MovementEnabled,
                SyntheticShocksEnabled = requestedOptions.SyntheticShocksEnabled,
                IncludeTrainingCriticSidecar =
                    requestedOptions.IncludeTrainingCriticSidecar
            };
            ValidateVersions(request);
            if (request.RandomStream != 0)
                throw new NotSupportedException(
                    "Native Elder v2 supports only RandomStream 0; snapshot state owns keyed simulation randomness.");
            if (options.EpisodeDecisionLimit < 1)
                throw new ArgumentException("EpisodeDecisionLimit must be positive.",
                    nameof(request));
            if (options.SyntheticShocksEnabled)
                throw new NotSupportedException("Synthetic shocks remain a separately gated curriculum and are not active in native v2.");
            if (request.BackgroundPolicy != ElderBackgroundPolicy.FrozenHeuristic)
                throw new NotSupportedException("Unsupported background policy.");
            bool hasSnapshot = request.Snapshot != null &&
                request.Snapshot.Length > 0;
            bool hasFixture = !string.IsNullOrWhiteSpace(request.FixtureIdentity);
            if (hasSnapshot == hasFixture)
                throw new ArgumentException("Reset requires exactly one snapshot or fixture identity.", nameof(request));

            if (hasSnapshot)
                world = SaveCodec.Load((byte[])request.Snapshot.Clone());
            else if (fixtureLoader != null)
                world = fixtureLoader(request.FixtureIdentity, request.Seed)
                    ?? throw new InvalidOperationException("Fixture loader returned no world.");
            else if (string.Equals(request.FixtureIdentity,
                    "generated-small", StringComparison.Ordinal))
                world = new WorldGenerator().Generate(request.Seed,
                    SimulationConfig.Small(1));
            else
                throw new ArgumentException("Unknown Elder fixture identity: " +
                    request.FixtureIdentity, nameof(request));

            // Own a canonical clone even when a fixture callback returns a live
            // test object. Reset never mutates its caller's snapshot.
            world = SaveCodec.Load(SaveCodec.Save(world));
            string configurationHash = SaveCodec.ConfigurationHash(world.Config);
            if (!string.IsNullOrWhiteSpace(request.ExpectedConfigurationHash) &&
                !string.Equals(request.ExpectedConfigurationHash,
                    configurationHash, StringComparison.Ordinal))
                throw new InvalidOperationException("Configuration hash mismatch.");

            var controlled = SelectControlledCamp(world, request);
            if (!CultivationEconomy.EconomyEnabled(world, controlled))
                throw new InvalidOperationException("The controlled camp has no resident Elder economy.");
            controlledCampId = controlled.Id;
            controlledPlayer = controlled.Player;
            episodeStep = 0; ended = false;
            replayBackgroundOverride = null;
            ElderInstitutionMemorySystem.EnsureInitialized(world, controlled);
            observation = observationBuilder.Build(world, controlledCampId,
                options.MovementEnabled, out observationMetadata);
            var critic = options.IncludeTrainingCriticSidecar
                ? ElderTrainingCriticSidecarBuilder.Build(world, controlled)
                : null;
            ElderObservationSchema.Validate();
            string initialFingerprint = SaveCodec.Fingerprint(world);
            byte[] initialSnapshot = SaveCodec.Save(world);
            var replayReset = CopyRequest(request);
            replayReset.Snapshot = (byte[])initialSnapshot.Clone();
            replayReset.FixtureIdentity = null;
            replayReset.ControlledCampId = controlledCampId;
            replayReset.ControlledPlayer = controlledPlayer;
            ReplayLog = new ElderReplayLog
            {
                InitialSnapshot = initialSnapshot,
                Reset = replayReset,
                BuildHash = BuildHash,
                ConfigurationHash = configurationHash,
                InitialStateFingerprint = initialFingerprint,
                InitialTrainingCriticSidecar = critic?.Copy(),
                ObservationSchemaVersion = ElderRLContract.ObservationSchemaVersion,
                ActionSchemaVersion = ElderRLContract.ActionSchemaVersion,
                EnvironmentSchemaVersion = ElderRLContract.EnvironmentSchemaVersion
            };
            return new ElderResetResult
            {
                Observation = observation,
                ObservationMetadata = observationMetadata,
                ObservationSchemaVersion = ElderRLContract.ObservationSchemaVersion,
                ActionSchemaVersion = ElderRLContract.ActionSchemaVersion,
                EnvironmentSchemaVersion = ElderRLContract.EnvironmentSchemaVersion,
                SimulatorVersion = SimulatorVersion,
                BuildHash = BuildHash,
                ConfigurationHash = configurationHash,
                InitialStateFingerprint = initialFingerprint,
                ControlledCampId = controlledCampId,
                ControlledPlayer = controlledPlayer,
                FixtureIdentity = request.FixtureIdentity,
                Seed = world.Seed,
                RandomStream = request.RandomStream,
                TrainingCriticSidecar = critic?.Copy()
            };
        }

        public ElderStepResult Step(ElderDirective directive)
        {
            if (world == null || observation == null)
                throw new InvalidOperationException("Reset must precede Step.");
            if (ended) throw new InvalidOperationException(
                "The episode has terminated or truncated; Reset is required.");
            var controlled = world.Camps.FirstOrDefault(camp =>
                camp.Id == controlledCampId);
            if (controlled == null || controlled.Population <= 0)
                throw new InvalidOperationException("The controlled settlement is not active.");

            var validationInterventions = new List<ElderIntervention>();
            var legalDirective = ElderDirectiveValidation.Validate(directive,
                observation, validationInterventions);
            Dictionary<int, List<Order>> background;
            List<DecisionTrace> traces;
            if (replayBackgroundOverride != null)
            {
                background = CopyOrders(replayBackgroundOverride);
                traces = new List<DecisionTrace>();
                replayBackgroundOverride = null;
            }
            else
            {
                var planned = AiPlanning.Decide(world, execution,
                    controlledPlayer);
                background = CopyOrders(planned.Orders);
                // Decisions are policy diagnostics, not authoritative inputs.
                // Replay carries the accepted background orders themselves.
                traces = new List<DecisionTrace>();
            }
            if (!background.TryGetValue(controlledPlayer, out var controlledOrders))
                background[controlledPlayer] = controlledOrders = new List<Order>();
            int movement = legalDirective.MovementCandidate;
            if (movement >= 0)
            {
                if (movement >= observationMetadata.SiteTiles.Length)
                    throw new InvalidOperationException("Validated movement metadata is inconsistent.");
                controlledOrders.Add(new Order(OrderKind.Move,
                    controlledCampId, destination:
                        observationMetadata.SiteTiles[movement]));
            }
            else controlledOrders.Add(new Order(OrderKind.NoOp,
                controlledCampId));

            int priorPopulation = Math.Max(0, controlled.Population);
            var prepared = TurnPlanner.PrepareWithElderDirectives(world,
                background,
                new Dictionary<int, ElderDirective>
                {
                    [controlledCampId] = legalDirective
                },
                new Dictionary<int, ElderObservation>
                {
                    [controlledCampId] = observation
                }, compiler, traces, execution);
            if (!prepared.ElderDecisions.TryGetValue(controlledCampId,
                    out var decision))
                throw new InvalidOperationException("The Elder directive was not compiled.");
            decision.Requested = directive?.Copy() ?? new ElderDirective();
            if (validationInterventions.Count > 0)
                decision.Interventions = validationInterventions.Concat(
                    decision.Interventions ?? Array.Empty<ElderIntervention>())
                    .ToArray();
            var record = new TurnResolver(execution).Resolve(world, prepared);
            controlled = world.Camps.FirstOrDefault(camp =>
                camp.Id == controlledCampId);
            double years = record.Duration / world.Config.DaysPerYear;
            var reward = Reward(controlled, decision, priorPopulation, years);
            episodeStep++;
            bool terminated = controlled == null || controlled.Population <= 0;
            bool truncated = !terminated && episodeStep >=
                options.EpisodeDecisionLimit;
            ended = terminated || truncated;
            if (!terminated)
                observation = observationBuilder.Build(world, controlledCampId,
                    options.MovementEnabled, out observationMetadata, decision);
            else
            {
                observation = null;
                observationMetadata = new ElderObservationMetadata
                {
                    ControlledCampId = controlledCampId,
                    ControlledPlayer = controlledPlayer
                };
            }
            string fingerprint = SaveCodec.Fingerprint(world);
            var critic = options.IncludeTrainingCriticSidecar && controlled != null
                ? ElderTrainingCriticSidecarBuilder.Build(world, controlled)
                : null;
            var replayBackground = CopyOrders(background);
            replayBackground.Remove(controlledPlayer);
            ReplayLog.Steps.Add(new ElderReplayStep
            {
                EpisodeStep = episodeStep,
                Requested = decision.Requested?.Copy(),
                Accepted = decision.Accepted?.Copy(),
                Realized = decision.Realized?.Copy(),
                ElapsedYears = years,
                Interventions = decision.Interventions?.Select(Copy).ToArray()
                    ?? Array.Empty<ElderIntervention>(),
                BackgroundOrders = replayBackground,
                ExogenousEvents = Array.Empty<ElderShockEvent>(),
                PostStepFingerprint = fingerprint,
                TrainingCriticSidecar = critic?.Copy()
            });
            return new ElderStepResult
            {
                Observation = observation,
                ObservationMetadata = observationMetadata,
                Reward = reward,
                ScalarReward = reward.UndiscountedIntervalWelfare,
                ElapsedYears = years,
                Terminated = terminated,
                Truncated = truncated,
                Decision = decision,
                ExogenousEvents = Array.Empty<ElderShockEvent>(),
                StateFingerprint = fingerprint,
                TrainingCriticSidecar = critic?.Copy(),
                Events = record.Events.ToArray()
            };
        }

        public static void VerifyReplay(ElderReplayLog replay,
            PhaseExecution execution = null)
        {
            if (replay?.InitialSnapshot == null || replay.Reset == null)
                throw new ArgumentException("Replay is missing its initial boundary.",
                    nameof(replay));
            if (!string.Equals(replay.BuildHash, BuildHash,
                    StringComparison.Ordinal))
                throw new InvalidOperationException("Replay build hash mismatch.");
            if (replay.ObservationSchemaVersion !=
                    ElderRLContract.ObservationSchemaVersion ||
                replay.ActionSchemaVersion !=
                    ElderRLContract.ActionSchemaVersion ||
                replay.EnvironmentSchemaVersion !=
                    ElderRLContract.EnvironmentSchemaVersion)
                throw new InvalidOperationException("Replay schema version mismatch.");
            var environment = new NativeElderEnvironment(null, execution);
            var request = CopyRequest(replay.Reset);
            request.Snapshot = (byte[])replay.InitialSnapshot.Clone();
            request.FixtureIdentity = null;
            var reset = environment.Reset(request);
            if (!string.Equals(reset.InitialStateFingerprint,
                    replay.InitialStateFingerprint, StringComparison.Ordinal))
                throw new InvalidOperationException("Replay initial fingerprint mismatch.");
            if (!string.Equals(reset.ConfigurationHash,
                    replay.ConfigurationHash, StringComparison.Ordinal))
                throw new InvalidOperationException("Replay configuration hash mismatch.");
            if (!SameCritic(reset.TrainingCriticSidecar,
                    replay.InitialTrainingCriticSidecar))
                throw new InvalidOperationException(
                    "Replay initial training-only critic sidecar mismatch.");
            foreach (var expected in replay.Steps.OrderBy(step =>
                step.EpisodeStep))
            {
                environment.replayBackgroundOverride =
                    CopyOrders(expected.BackgroundOrders);
                var actual = environment.Step(expected.Requested);
                if (!SameDirective(actual.Decision.Requested,
                        expected.Requested))
                    throw new InvalidOperationException("Replay requested action diverged at Elder step " +
                        expected.EpisodeStep + ".");
                if (!SameDirective(actual.Decision.Accepted,
                        expected.Accepted))
                    throw new InvalidOperationException("Replay accepted action diverged at Elder step " +
                        expected.EpisodeStep + ".");
                if (!SameDirective(actual.Decision.Realized,
                        expected.Realized))
                    throw new InvalidOperationException("Replay realized action diverged at Elder step " +
                        expected.EpisodeStep + ".");
                if (!SameInterventions(actual.Decision.Interventions,
                        expected.Interventions))
                    throw new InvalidOperationException("Replay intervention evidence diverged at Elder step " +
                        expected.EpisodeStep + ".");
                if (Math.Abs(actual.ElapsedYears - expected.ElapsedYears) >
                        1e-12 * Math.Max(1, Math.Abs(expected.ElapsedYears)))
                    throw new InvalidOperationException("Replay elapsed physical interval diverged at Elder step " +
                        expected.EpisodeStep + ".");
                if (!SameCritic(actual.TrainingCriticSidecar,
                        expected.TrainingCriticSidecar))
                    throw new InvalidOperationException("Replay training-only critic sidecar diverged at Elder step " +
                        expected.EpisodeStep + ".");
                if (!string.Equals(actual.StateFingerprint,
                        expected.PostStepFingerprint,
                        StringComparison.Ordinal))
                    throw new InvalidOperationException("Replay diverged at Elder step " +
                        expected.EpisodeStep + ".");
            }
        }

        private static bool SameDirective(ElderDirective left,
            ElderDirective right)
            => left != null && right != null &&
                left.SchemaVersion == right.SchemaVersion &&
                left.MovementCandidate == right.MovementCandidate &&
                SameNumber(left.RetainedFieldOperation,
                    right.RetainedFieldOperation) &&
                SameNumber(left.RetainedHerdFraction,
                    right.RetainedHerdFraction) &&
                SameNumber(left.DevelopmentBudget,
                    right.DevelopmentBudget) &&
                SameNumber(left.FieldDevelopmentShare,
                    right.FieldDevelopmentShare);

        private static bool SameNumber(double left, double right)
            => left == right || double.IsNaN(left) && double.IsNaN(right);

        private static bool SameCritic(ElderTrainingCriticSidecar left,
            ElderTrainingCriticSidecar right)
        {
            if (left == null || right == null) return left == right;
            return left.SchemaVersion == right.SchemaVersion &&
                SameNumber(left.EffectiveCultivationEffortKneeQ,
                    right.EffectiveCultivationEffortKneeQ) &&
                SameNumber(left.EffectiveFishingEffortKneeQ,
                    right.EffectiveFishingEffortKneeQ) &&
                string.Equals(left.CultivationFrontierIdentity,
                    right.CultivationFrontierIdentity,
                    StringComparison.Ordinal) &&
                string.Equals(left.FishingFrontierIdentity,
                    right.FishingFrontierIdentity,
                    StringComparison.Ordinal) &&
                left.CultivationRegimeChanged ==
                    right.CultivationRegimeChanged &&
                left.FishingRegimeChanged == right.FishingRegimeChanged &&
                left.HasCultivationRegimeChange ==
                    right.HasCultivationRegimeChange &&
                left.HasFishingRegimeChange ==
                    right.HasFishingRegimeChange &&
                SameNumber(left.CultivationRegimeChangeDays,
                    right.CultivationRegimeChangeDays) &&
                SameNumber(left.FishingRegimeChangeDays,
                    right.FishingRegimeChangeDays);
        }

        private static bool SameInterventions(ElderIntervention[] left,
            ElderIntervention[] right)
        {
            left ??= Array.Empty<ElderIntervention>();
            right ??= Array.Empty<ElderIntervention>();
            if (left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++)
                if (left[i]?.Kind != right[i]?.Kind ||
                    !string.Equals(left[i]?.Control, right[i]?.Control,
                        StringComparison.Ordinal) ||
                    !string.Equals(left[i]?.Reason, right[i]?.Reason,
                        StringComparison.Ordinal) ||
                    !SameNumber(left[i]?.Requested ?? double.NaN,
                        right[i]?.Requested ?? double.NaN) ||
                    !SameNumber(left[i]?.Accepted ?? double.NaN,
                        right[i]?.Accepted ?? double.NaN))
                    return false;
            return true;
        }

        private ElderRewardVector Reward(Camp controlled,
            ElderCompiledDecision decision, int priorPopulation,
            double elapsedYears)
        {
            double quality = controlled == null ? 0 :
                ElderRLContract.Unit(controlled.QoL);
            var reward = ElderRewardContract.Physical(priorPopulation,
                quality, elapsedYears);
            double continuity = controlled == null || priorPopulation <= 0
                ? 0 : Numbers.Clamp(controlled.Population /
                    (double)priorPopulation, 0, 2);
            reward.FoodSecurity = controlled == null ? 0 :
                ElderRLContract.Unit(controlled.FoodNeedsMetRatio);
            reward.WaterSecurity = controlled == null ? 0 :
                ElderRLContract.Unit(controlled.WaterNeedsMetRatio);
            reward.ServiceReliability = controlled == null ? 0 :
                Math.Min(ElderRLContract.Unit(controlled.ClothPerCapita),
                    ElderRLContract.Unit(controlled.HousingPerCapita));
            reward.PopulationContinuity = continuity;
            reward.EcologicalViability = controlled == null ? 0 :
                1 - ElderRLContract.Unit(controlled.DeficitSeverity);
            reward.SettlementViability = controlled != null &&
                controlled.Population > 0 ? 1 : 0;
            reward.CapitalContinuity = decision?.Realized == null ? 0 :
                .5 * (decision.Realized.RetainedFieldOperation +
                    decision.Realized.RetainedHerdFraction);
            reward.CatastrophicLoss = controlled == null ||
                controlled.Population <= 0 ? 1 : 0;
            return reward;
        }

        private static Camp SelectControlledCamp(WorldState state,
            ElderResetRequest request)
        {
            Camp selected = null;
            if (request.ControlledCampId >= 0)
                selected = state.Camps.FirstOrDefault(camp =>
                    camp.Id == request.ControlledCampId);
            else if (request.ControlledPlayer >= 0)
                selected = state.Camps.Where(camp =>
                        camp.Player == request.ControlledPlayer)
                    .OrderBy(camp => camp.Id).FirstOrDefault();
            else
                selected = state.Camps.Where(camp => camp.Population > 0 &&
                        CultivationEconomy.EconomyEnabled(state, camp))
                    .OrderBy(camp => camp.Id).FirstOrDefault();
            return selected ?? throw new ArgumentException(
                "Reset could not resolve the controlled camp.", nameof(request));
        }

        private static void ValidateVersions(ElderResetRequest request)
        {
            if (request.ExpectedObservationSchemaVersion !=
                    ElderRLContract.ObservationSchemaVersion ||
                request.ExpectedActionSchemaVersion !=
                    ElderRLContract.ActionSchemaVersion ||
                request.ExpectedEnvironmentSchemaVersion !=
                    ElderRLContract.EnvironmentSchemaVersion)
                throw new InvalidOperationException("Elder schema version mismatch.");
            if (request.Options == null ||
                request.Options.EnvironmentSchemaVersion !=
                    ElderRLContract.EnvironmentSchemaVersion)
                throw new InvalidOperationException(
                    "Elder options environment schema version mismatch.");
            if (!string.IsNullOrWhiteSpace(request.ExpectedSimulatorVersion) &&
                !string.Equals(request.ExpectedSimulatorVersion,
                    SimulatorVersion, StringComparison.Ordinal))
                throw new InvalidOperationException("Simulator version mismatch.");
        }

        private static ElderResetRequest CopyRequest(ElderResetRequest source)
            => new ElderResetRequest
            {
                Seed = source.Seed,
                RandomStream = source.RandomStream,
                Snapshot = source.Snapshot == null ? null :
                    (byte[])source.Snapshot.Clone(),
                FixtureIdentity = source.FixtureIdentity,
                ControlledCampId = source.ControlledCampId,
                ControlledPlayer = source.ControlledPlayer,
                BackgroundPolicy = source.BackgroundPolicy,
                Options = new ElderEnvironmentOptions
                {
                    EnvironmentSchemaVersion = source.Options?
                        .EnvironmentSchemaVersion ??
                        ElderRLContract.EnvironmentSchemaVersion,
                    EpisodeDecisionLimit = source.Options?
                        .EpisodeDecisionLimit ?? 32,
                    MovementEnabled = source.Options?.MovementEnabled ?? false,
                    SyntheticShocksEnabled = source.Options?
                        .SyntheticShocksEnabled ?? false,
                    IncludeTrainingCriticSidecar = source.Options?
                        .IncludeTrainingCriticSidecar ?? false
                },
                ExpectedSimulatorVersion = source.ExpectedSimulatorVersion,
                ExpectedConfigurationHash = source.ExpectedConfigurationHash,
                ExpectedObservationSchemaVersion =
                    source.ExpectedObservationSchemaVersion,
                ExpectedActionSchemaVersion = source.ExpectedActionSchemaVersion,
                ExpectedEnvironmentSchemaVersion =
                    source.ExpectedEnvironmentSchemaVersion
            };

        private static Dictionary<int, List<Order>> CopyOrders(
            IReadOnlyDictionary<int, List<Order>> source)
        {
            var copy = new Dictionary<int, List<Order>>();
            if (source == null) return copy;
            foreach (var pair in source.OrderBy(item => item.Key))
                copy[pair.Key] = pair.Value?.Where(order => order != null)
                    .Select(order => order.Copy()).ToList() ?? new List<Order>();
            return copy;
        }

        private static ElderIntervention Copy(ElderIntervention source)
            => new ElderIntervention
            {
                Kind = source.Kind, Control = source.Control,
                Reason = source.Reason, Requested = source.Requested,
                Accepted = source.Accepted
            };
    }
}
