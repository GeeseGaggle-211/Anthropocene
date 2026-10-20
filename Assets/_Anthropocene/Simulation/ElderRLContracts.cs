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
        public const int ObservationSchemaVersion = 2;
        public const int ActionSchemaVersion = 1;
        public const int EnvironmentSchemaVersion = 2;
        public const int CriticSidecarSchemaVersion = 1;
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
        public ElderMemoryObservation Memory = new ElderMemoryObservation();
        public ElderPresenceObservation Presence = new ElderPresenceObservation();
        public ElderActionMask Mask = new ElderActionMask();
        // Contract 7 additions (filled by ElderModel9Observation; absent from the v2 tensor).
        public ElderDemographyObservation Demography = new ElderDemographyObservation();
        public int CurrentFissionPolicy;
        // Contract 10 regional movement candidates (ElderMovement; absent from the v2 tensor).
        [System.Runtime.Serialization.OptionalField] public ElderRegionObservation[] Regions = Array.Empty<ElderRegionObservation>();
    }

    [Serializable]
    public sealed class ElderDemographyObservation
    {
        // Own committed physical-demography state. Rates are annual hazards; ratios are per resident.
        public double BirthRate, MortalityRate, ExposureRate, CrowdingRate, DiseaseRate, StarvationRate;
        public double FoodSurplusPerResident, BirthFundingFraction, FedCapacityRatio, HousingPerResident, ClothingPerResident;
        public bool SurplusFundedBirths;
    }

    [Serializable]
    public sealed class ElderCoreObservation
    {
        // Raw camp/player IDs are intentionally absent. They live only in
        // ElderObservationMetadata and are not part of the policy tensor.
        public int Population, ConnectedConsumers;
        public double PreviousIntervalYears, NextIntervalYears;
        public bool HasPreviousInterval, HasDeficitAge, HasMovementAge,
            HasFissionAge;
        public double DeficitAgeYears, MovementAgeYears, FissionAgeYears;
        public double QoL, FoodNeedsMet, WaterNeedsMet, ClothingCoverage,
            HousingCoverage, FoodReserveYears;
        public double RealizedMigrationPerCapitaYear,
            RealizedTradeUtilityPerCapitaYear;
        public bool IsVillage;
    }

    [Serializable]
    public sealed class ElderFieldObservation
    {
        public int RetainedTiles;
        public double CurrentOperation, MaintenanceWorkerYears;
        public double CurrentFoodSupport, FutureFoodSupport, RebuildBurden,
            RetainedCapitalService, CurrentIntensity;
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
            ExitOpportunityPressure;
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
        public double ElapsedYears;
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
        // Contract 8: a legal water source next to the site, and the yield of the known fishing grounds within
        // FishingEconomy.GroundRange weighted by DistanceFactor (observation productivity bands).
        public bool FishingSource;
        public double FishingAccess;
    }

    [Serializable]
    public sealed class ElderFishingObservation
    {
        public int Candidate, Distance;
        public bool Incumbent, Known;
        public double YieldPerFisherYear, FoodPotential, Biomass, Condition;
        public double CurrentCapital, CurrentIntensity, OperationFraction;
        public double MaintenanceWorkerYears;
        public bool MaintenancePresent;
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
        public double ProductionCapacity;
        public double RecentImport, RecentExport;
        public bool CanProduce, CanTrade;
    }

    [Serializable]
    public sealed class ElderContactObservation
    {
        // Raw foreign IDs and live foreign stores are deliberately absent.
        public int Distance;
        public double AgeYears, Affinity;
        public double RecentTradeUtilityPerCapitaYear,
            RecentTradeFlowPerCapitaYear,
            RecentMigrationPerCapitaYear;
        public bool Kin, CurrentlyVisible;
        // Contract 7: coarse public population band of the remembered contact (log2 of size, 0 when unknown).
        public double PopulationBand;
    }

    [Serializable]
    public sealed class ElderMemoryObservation
    {
        public bool Present;
        public ElderMemoryTraceObservation[] Traces =
            Array.Empty<ElderMemoryTraceObservation>();
        public double[] Maturity = new double[3];
    }

    [Serializable]
    public sealed class ElderMemoryTraceObservation
    {
        public string Key;
        public double Short, Medium, Long;
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
        // Primitive structural availability only. Temporary labor/resource
        // scarcity remains an environment consequence handled by the compiler.
        public bool FieldRetention;
        public bool HerdRetention;
        public bool FieldDevelopment;
        public bool HerdDevelopment;
        // Contract 8: whole extra Fishers beyond the survival frontier (resident fishing with a legal source).
        public bool FishingDevelopment;
        public bool Movement;
        // Contract 9: an active, unbroken capital commitment can be held this turn.
        [System.Runtime.Serialization.OptionalField] public bool CommitmentHold;

        public bool AllowsDevelopment()
            => FieldDevelopment || HerdDevelopment;

        public bool AllowsDevelopmentSplit()
            => FieldDevelopment && HerdDevelopment;
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
        // Contract 8 (action schema 8): the fishing share of DevelopmentBudget, staffed as whole Fishers beyond the
        // survival frontier; FieldDevelopmentShare splits the remainder between fields and herds. The economy mix
        // (cultivation, herding, fishing) is ((1 - f) s, (1 - f)(1 - s), f). Earlier schemas ignore it.
        [System.Runtime.Serialization.OptionalField] public double FishingDevelopmentShare;
        public int MovementCandidate = -1;
        // Contract 7 (model9) extensions; absent (null / -1) keeps the native heuristic choice. ServiceLevels follow
        // ElderModel9Contract.GoodsServices; routes are 0/1 (Cloth/Animal Fabric, fixed Housing/Light Housing);
        // FissionPolicy is the FissionPolicy ordinal.
        public double[] ServiceLevels;
        public int ClothingRoute = -1, ShelterRoute = -1, FissionPolicy = -1;
        // Contract 9 (action schema 9): capital commitment span. 1 (the default) decides the capital controls for
        // this turn only, exactly as before; 2-6 commits them (retained field and herd, Development budget and shares,
        // clothing and shelter routes) for that many turns; 0 holds the active commitment, re-emitting its values.
        // ElderCommitments resolves holds and forced breaks. Earlier schemas are always 1.
        [System.Runtime.Serialization.OptionalField] public int CommitTurns = 1;
        // Contract 10: the target tile of a region-row or keep-intent movement (ElderMovement). The environment fills it
        // from the observation sidecar; the policy names rows, never tiles.
        [System.Runtime.Serialization.OptionalField] public int IntentTile = -1;

        public ElderDirective Copy() => new ElderDirective
        {
            SchemaVersion = SchemaVersion,
            RetainedFieldOperation = RetainedFieldOperation,
            RetainedHerdFraction = RetainedHerdFraction,
            DevelopmentBudget = DevelopmentBudget,
            FieldDevelopmentShare = FieldDevelopmentShare,
            FishingDevelopmentShare = FishingDevelopmentShare,
            MovementCandidate = MovementCandidate,
            ServiceLevels = (double[])ServiceLevels?.Clone(),
            ClothingRoute = ClothingRoute, ShelterRoute = ShelterRoute, FissionPolicy = FissionPolicy,
            CommitTurns = CommitTurns, IntentTile = IntentTile
        };
    }

    [Serializable]
    public sealed class ElderRewardVector
    {
        // Scalar reward is fixed by contract, not a weighted sum of diagnostics.
        public int PriorPopulation;
        public double RealizedQoL, ElapsedYears, WelfareRate,
            UndiscountedIntervalWelfare;
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
        [System.Runtime.Serialization.OptionalField] public double AcceptedFishingDevelopmentPeople;
        // Contract 9: what the decision did to the capital commitment (ElderCommitmentAction), its span and, for a
        // refused hold, why.
        [System.Runtime.Serialization.OptionalField] public int CommitmentAction, CommitTurns;
        [System.Runtime.Serialization.OptionalField] public string CommitmentBreak;
        // The validated capital controls a Commit stores (the request after legality checks, not the compiled
        // outcome, so a turn's integer or resource repair does not ratchet the commitment down).
        [System.Runtime.Serialization.OptionalField] public ElderDirective CommitmentValues;
        // Transient compiler/resolver witness. Raw identities never enter the
        // policy observation or replay payload.
        // Contract 7: the exact allocation the compiler produced (teacher labels and realized service levels).
        [NonSerialized] internal DemandController.Allocation Allocation;
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
        // Contract 10: each region row's tile, the first tile of its known path, and the row holding the intent (-1 none).
        public int[] RegionTiles = Array.Empty<int>(), RegionFirstSteps = Array.Empty<int>();
        public int IntentRow = -1;
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
        public ElderTrainingCriticSidecar TrainingCriticSidecar;
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
        // Reserved for a separately gated curriculum. Native v2 rejects true rather than
        // silently enabling a mock disaster system.
        public bool SyntheticShocksEnabled;
        // Explicit adapter request. The result is separate from
        // ElderObservation and cannot be tensor-encoded.
        public bool IncludeTrainingCriticSidecar;
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
        public ElderTrainingCriticSidecar TrainingCriticSidecar;
    }

    [Serializable]
    public sealed class ElderReplayStep
    {
        public int EpisodeStep;
        public ElderDirective Requested, Accepted, Realized;
        public double ElapsedYears;
        public ElderIntervention[] Interventions = Array.Empty<ElderIntervention>();
        public Dictionary<int, List<Order>> BackgroundOrders =
            new Dictionary<int, List<Order>>();
        public ElderShockEvent[] ExogenousEvents = Array.Empty<ElderShockEvent>();
        public string PostStepFingerprint;
        public ElderTrainingCriticSidecar TrainingCriticSidecar;
    }

    // Training-only ground truth for optional asymmetric-critic ablations.
    // It is intentionally not reachable from ElderObservation or its encoder.
    [Serializable]
    public sealed class ElderTrainingCriticSidecar
    {
        public int SchemaVersion = ElderRLContract.CriticSidecarSchemaVersion;
        public double EffectiveCultivationEffortKneeQ,
            EffectiveFishingEffortKneeQ;
        public string CultivationFrontierIdentity,
            FishingFrontierIdentity;
        public bool CultivationRegimeChanged,
            FishingRegimeChanged;
        public bool HasCultivationRegimeChange,
            HasFishingRegimeChange;
        public double CultivationRegimeChangeDays,
            FishingRegimeChangeDays;

        public ElderTrainingCriticSidecar Copy()
            => (ElderTrainingCriticSidecar)MemberwiseClone();
    }

    [Serializable]
    public sealed class ElderReplayLog
    {
        public byte[] InitialSnapshot;
        public ElderResetRequest Reset;
        public string BuildHash, ConfigurationHash, InitialStateFingerprint;
        public ElderTrainingCriticSidecar InitialTrainingCriticSidecar;
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
