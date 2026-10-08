using System;
using System.Collections.Generic;

namespace Anthropocene.Simulation
{
    // Framework-neutral contracts for the Village Elder experiment. The
    // production simulation remains authoritative for legality, feasibility,
    // integer staffing, resolution and replay. An adapter may encode these
    // records for an ML framework but must preserve all schema versions.
    public static class ElderRLContract
    {
        public const int ObservationSchemaVersion = 1;
        public const int ActionSchemaVersion = 1;
        public const int EnvironmentSchemaVersion = 1;
        public const int SchemaVersion = ObservationSchemaVersion;
        public const int TensorFeatureCount = 512;
        public const int MaxLocalSites = 7;
        public const int MaxFishingGrounds = 3;
        public const int MaxHerdGroups = 8;
        public const int MaxCommoditySlots = 11;
        public const int MaxKnownContacts = 8;

        public static bool Finite(double value)
            => !double.IsNaN(value) && !double.IsInfinity(value);

        public static double Unit(double value)
            => !Finite(value) || value <= 0 ? 0 : value >= 1 ? 1 : value;
    }

    [Serializable]
    public sealed class ElderObservation
    {
        public int SchemaVersion = ElderRLContract.ObservationSchemaVersion;
        public ElderCoreObservation Core = new ElderCoreObservation();
        public ElderFieldObservation Fields = new ElderFieldObservation();
        public ElderHerdObservation Herd = new ElderHerdObservation();
        public ElderFissionObservation Fission = new ElderFissionObservation();
        public ElderPreviousDecisionObservation Previous = new ElderPreviousDecisionObservation();
        public ElderSiteObservation[] Sites = Array.Empty<ElderSiteObservation>();
        public ElderFishingObservation[] Fishing = Array.Empty<ElderFishingObservation>();
        public ElderHerdGroupObservation[] HerdGroups = Array.Empty<ElderHerdGroupObservation>();
        public ElderCapabilityObservation[] Capabilities = Array.Empty<ElderCapabilityObservation>();
        public ElderCommodityObservation[] Commodities = Array.Empty<ElderCommodityObservation>();
        public ElderContactObservation[] Contacts = Array.Empty<ElderContactObservation>();
        public ElderPresenceObservation Presence = new ElderPresenceObservation();
        public ElderActionMask Mask = new ElderActionMask();
    }

    [Serializable]
    public sealed class ElderCoreObservation
    {
        // Raw camp/player IDs are intentionally absent. They live only in
        // ElderObservationMetadata and are not part of the policy tensor.
        public int Population, ConnectedConsumers, DeficitTurns;
        public double ElapsedDays, HorizonYears;
        public double QoL, FoodNeedsMet, WaterNeedsMet, ClothingCoverage,
            HousingCoverage, FoodReserveYears;
        public double PopulationTrend, QoLTrend, NetMigrationTrend;
        public bool IsVillage, RecentlyMoved;
    }

    [Serializable]
    public sealed class ElderFieldObservation
    {
        public int RetainedTiles;
        public double CurrentOperation, MaintenanceWorkerYears;
        public double CurrentFoodSupport, FutureFoodSupport, RebuildBurden,
            RetainedCapitalService;
    }

    [Serializable]
    public sealed class ElderHerdObservation
    {
        public int Animals, Groups;
        public double Nutrition, ManagementFulfilment;
        public double ZeroDisplacementCapacity, AbsoluteCapacity, WaterCapacity;
        public double CurrentManagementWorkerYears, CurrentAnimalFood,
            HumanFoodDisplaced;
        public double BirthTrend, MortalityTrend, SlaughterTrend;
    }

    [Serializable]
    public sealed class ElderHerdGroupObservation
    {
        // A bounded physical group summary. Species/entity identity is kept out
        // of the default tensor; physiology is represented by observed rates.
        public int Animals;
        public double Nutrition, ManagementFulfilment, FoodPerAnimal,
            WaterPerAnimal, AnimalFoodPerAnimal, BirthRate, MortalityRate;
    }

    [Serializable]
    public sealed class ElderFissionObservation
    {
        public bool CandidatePresent, RecentSplit;
        public double Pressure, ExpectedStayerGain, ExpectedDaughterQoL,
            ExitOpportunity;
        public int RecentDaughterPopulation;
    }

    [Serializable]
    public sealed class ElderPreviousDecisionObservation
    {
        public bool Present, MovementAccepted;
        public double RequestedFieldRetention, AcceptedFieldRetention,
            RealizedFieldOperation;
        public double RequestedHerdRetention, AcceptedHerdRetention,
            RealizedHerdRetention;
        public double RequestedDevelopmentBudget, AcceptedDevelopmentBudget,
            RealizedDevelopmentBudget;
        public double RequestedFieldShare, AcceptedFieldShare,
            RealizedFieldShare;
        public int InterventionCount;
    }

    [Serializable]
    public sealed class ElderSiteObservation
    {
        // Candidate is the stable index used by the categorical action. The
        // corresponding raw tile is available only in observation metadata.
        public int Candidate;
        public bool Current, Known;
        public double HumanFood, HumanFoodAfterHerd;
        public double HerdZeroDisplacement, HerdAbsolute, HerdWater;
        public double FixedShelter, RecurringLabor, AbsoluteRecurringLabor;
        public double RetainedFieldService, LostRebuildBurden;
    }

    [Serializable]
    public sealed class ElderFishingObservation
    {
        public int Candidate, Distance;
        public bool Incumbent, Known;
        public double YieldPerFisherYear, FoodPotential, Biomass, Condition;
        public double OpeningCapital, OperationFraction;
        public double MaintenanceWorkerYears, BuildWorkerYears;
    }

    [Serializable]
    public sealed class ElderCapabilityObservation
    {
        // A semantic opportunity, never the camp's primary technology identity.
        public string Key;
        public bool Available;
        public double Experience, Progress;
    }

    [Serializable]
    public sealed class ElderCommodityObservation
    {
        public string Key;
        public double Stock, LocalNeed, ProtectedFloor;
        public double ProductionCapacity, MarginalValue;
        public double RecentImport, RecentExport, ExternalOpportunity;
        public bool CanProduce, CanTrade;
    }

    [Serializable]
    public sealed class ElderContactObservation
    {
        // Raw foreign IDs and live foreign stores are deliberately absent.
        public int Distance, AgeTurns;
        public double Affinity, RelativeAttractiveness;
        public double RecentTradeValue, RecentMigration;
        public bool Kin, CurrentlyVisible;
    }

    [Serializable]
    public sealed class ElderPresenceObservation
    {
        public ElderPresenceBlock Sites = new ElderPresenceBlock();
        public ElderPresenceBlock Fishing = new ElderPresenceBlock();
        public ElderPresenceBlock Herds = new ElderPresenceBlock();
        public ElderPresenceBlock Commodities = new ElderPresenceBlock();
        public ElderPresenceBlock Contacts = new ElderPresenceBlock();
    }

    [Serializable]
    public sealed class ElderPresenceBlock
    {
        public int Present, Omitted;
        public bool Overflow => Omitted > 0;
    }

    [Serializable]
    public sealed class ElderActionMask
    {
        public bool FieldRetention = true;
        public bool HerdRetention = true;
        public bool Development = true;
        public bool DevelopmentSplit = true;
        public bool Movement;
        // -1 is stay. Other values are exact indexes into Observation.Sites.
        public int[] LegalMovementCandidates = new[] { -1 };

        public bool AllowsMovement(int candidate)
        {
            if (candidate == -1) return true;
            if (!Movement || LegalMovementCandidates == null) return false;
            for (int i = 0; i < LegalMovementCandidates.Length; i++)
                if (LegalMovementCandidates[i] == candidate) return true;
            return false;
        }
    }

    [Serializable]
    public sealed class ElderDirective
    {
        public int SchemaVersion = ElderRLContract.ActionSchemaVersion;
        public double RetainedFieldOperation = 1;
        public double RetainedHerdFraction = 1;
        public double DevelopmentBudget;
        public double FieldDevelopmentShare = .5;
        public int MovementCandidate = -1;

        public ElderDirective Copy() => new ElderDirective
        {
            SchemaVersion = SchemaVersion,
            RetainedFieldOperation = RetainedFieldOperation,
            RetainedHerdFraction = RetainedHerdFraction,
            DevelopmentBudget = DevelopmentBudget,
            FieldDevelopmentShare = FieldDevelopmentShare,
            MovementCandidate = MovementCandidate
        };
    }

    [Serializable]
    public sealed class ElderRewardVector
    {
        // Scalar reward is fixed by contract, not a weighted sum of diagnostics.
        public int PriorPopulation;
        public double RealizedQoL, ElapsedYears, RawWelfareFlow,
            NormalizedWelfare;
        public double FoodSecurity, WaterSecurity, ServiceReliability;
        public double PopulationContinuity, EcologicalViability,
            SettlementViability, CapitalContinuity;
        public double CatastrophicLoss;
    }

    public enum ElderInterventionKind
    {
        None = 0,
        PhysicalConstraint = 1,
        LegalConstraint = 2,
        IntegerWorkforce = 3,
        ResourceConstraint = 4,
        PlacementConflict = 5,
        InvalidActionFallback = 6,
        SchemaMismatch = 7
    }

    [Serializable]
    public sealed class ElderIntervention
    {
        public ElderInterventionKind Kind;
        public string Control, Reason;
        public double Requested, Accepted;
    }

    [Serializable]
    public sealed class ElderCompiledDecision
    {
        public ElderDirective Requested;
        public ElderDirective Accepted;
        public ElderDirective Realized;
        public ElderIntervention[] Interventions = Array.Empty<ElderIntervention>();
        // Debug/replay sidecar. It is never copied into ElderObservation.
        public EconomicIntent Intent;
        public ProductionOrder Production;
        public double RequestedDevelopmentPeople, AcceptedDevelopmentPeople;
        public double AcceptedFieldDevelopmentPeople,
            AcceptedHerdDevelopmentPeople;
        // Transient compiler/resolver witness. Raw identities never enter the
        // policy observation or replay payload.
        [NonSerialized] internal int ControlledCampId, PlanningPopulation,
            AcceptedTile, AcceptedRetainedFieldPeople,
            AcceptedRetainedHerdPeople, StartingHerdAnimals;
        [NonSerialized] internal int[] RetainedFieldTiles = Array.Empty<int>();
        [NonSerialized] internal double ResolvedRetainedFieldOperation,
            ResolvedFieldDevelopmentPeople, ResolvedHerdDevelopmentPeople;
    }

    [Serializable]
    public sealed class ElderObservationMetadata
    {
        public int ControlledCampId = -1, ControlledPlayer = -1;
        public int[] SiteTiles = Array.Empty<int>();
        public int[] FishingTiles = Array.Empty<int>();
        public string PermissionModel = "local-game-knowledge";
    }

    [Serializable]
    public sealed class ElderStepResult
    {
        public ElderObservation Observation;
        public ElderObservationMetadata ObservationMetadata;
        public ElderRewardVector Reward = new ElderRewardVector();
        public double ScalarReward;
        public double ElapsedYears;
        public bool Terminated, Truncated;
        public ElderCompiledDecision Decision;
        public ElderShockEvent[] ExogenousEvents = Array.Empty<ElderShockEvent>();
        public string StateFingerprint;
        public string[] Events = Array.Empty<string>();
    }

    [Serializable]
    public sealed class ElderShockEvent
    {
        public int EpisodeStep;
        public string Family;
        public double Severity, DurationYears;
        public bool KnownBeforeImpact;
        public int RandomStream;
    }

    public enum ElderBackgroundPolicy
    {
        FrozenHeuristic = 0
    }

    [Serializable]
    public sealed class ElderEnvironmentOptions
    {
        public int EnvironmentSchemaVersion = ElderRLContract.EnvironmentSchemaVersion;
        public int EpisodeDecisionLimit = 32;
        public bool MovementEnabled;
        // Reserved for separately gated v2. Native v1 rejects true rather than
        // silently enabling a mock disaster system.
        public bool SyntheticShocksEnabled;
    }

    [Serializable]
    public sealed class ElderResetRequest
    {
        public int Seed = 1, RandomStream;
        // A valid SaveCodec payload. Seed alone is never considered replay.
        public byte[] Snapshot;
        public string FixtureIdentity;
        public int ControlledCampId = -1, ControlledPlayer = -1;
        public ElderBackgroundPolicy BackgroundPolicy =
            ElderBackgroundPolicy.FrozenHeuristic;
        public ElderEnvironmentOptions Options = new ElderEnvironmentOptions();
        public string ExpectedSimulatorVersion, ExpectedConfigurationHash;
        public int ExpectedObservationSchemaVersion = ElderRLContract.ObservationSchemaVersion;
        public int ExpectedActionSchemaVersion = ElderRLContract.ActionSchemaVersion;
        public int ExpectedEnvironmentSchemaVersion = ElderRLContract.EnvironmentSchemaVersion;
    }

    [Serializable]
    public sealed class ElderResetResult
    {
        public ElderObservation Observation;
        public ElderObservationMetadata ObservationMetadata;
        public int ObservationSchemaVersion, ActionSchemaVersion,
            EnvironmentSchemaVersion;
        public string SimulatorVersion, BuildHash, ConfigurationHash,
            InitialStateFingerprint;
        public int ControlledCampId, ControlledPlayer;
        public string FixtureIdentity;
        public int Seed, RandomStream;
    }

    [Serializable]
    public sealed class ElderReplayStep
    {
        public int EpisodeStep;
        public ElderDirective Requested, Accepted;
        public ElderIntervention[] Interventions = Array.Empty<ElderIntervention>();
        public Dictionary<int, List<Order>> BackgroundOrders =
            new Dictionary<int, List<Order>>();
        public ElderShockEvent[] ExogenousEvents = Array.Empty<ElderShockEvent>();
        public string StateFingerprint;
    }

    [Serializable]
    public sealed class ElderReplayLog
    {
        public byte[] InitialSnapshot;
        public ElderResetRequest Reset;
        public string BuildHash, ConfigurationHash, InitialStateFingerprint;
        public int ObservationSchemaVersion, ActionSchemaVersion,
            EnvironmentSchemaVersion;
        public List<ElderReplayStep> Steps = new List<ElderReplayStep>();
    }

    // Internal simulation inputs use one immutable context so there is one
    // production compiler. Framework policies never receive this context.
    public sealed class ElderCompilationContext
    {
        internal readonly WorldState World;
        internal readonly Camp CommittedCamp, PlanningCamp;
        internal readonly FishingAccessPlan FishingAccess;
        internal readonly EconomicIntent Settings;
        public readonly ElderObservation Observation;
        public readonly int AcceptedTile;

        internal ElderCompilationContext(WorldState world, Camp committedCamp,
            Camp planningCamp, FishingAccessPlan fishingAccess,
            ElderObservation observation, int acceptedTile,
            EconomicIntent settings = null)
        {
            World = world; CommittedCamp = committedCamp;
            PlanningCamp = planningCamp; FishingAccess = fishingAccess;
            Observation = observation; AcceptedTile = acceptedTile;
            Settings = settings?.Copy();
        }
    }

    public interface IElderPolicy
    {
        ElderDirective Decide(ElderObservation observation);
    }

    public interface IElderDirectiveCompiler
    {
        ElderCompiledDecision Compile(ElderCompilationContext context,
            ElderDirective directive);
    }

    public interface IElderEnvironment
    {
        ElderResetResult Reset(ElderResetRequest request);
        ElderStepResult Step(ElderDirective directive);
        ElderReplayLog ReplayLog { get; }
    }
}
