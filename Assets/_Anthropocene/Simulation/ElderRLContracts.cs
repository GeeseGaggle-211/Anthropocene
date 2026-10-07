using System;

namespace Anthropocene.Simulation
{
    // Model-neutral contracts for the learned Village Elder experiment.
    //
    // These types deliberately contain no dependency on a Python/RL framework.
    // The production simulation remains authoritative for legal actions, physical
    // feasibility, allocation, resolution and replay. Framework adapters may
    // flatten/encode these records, but they must preserve SchemaVersion.
    public static class ElderRLContract
    {
        public const int SchemaVersion = 1;

        // Current production bounds, not ML limits. A later schema may raise
        // these after an explicit migration/compatibility review.
        public const int MaxLocalSites = 7;       // CampMovement == 1
        public const int MaxFishingGrounds = 3;  // FishingEconomy ranked selection
        public const int MaxKnownContacts = 8;
        public const int MaxCommoditySlots = 64;
        public const int MaxObligations = 32;

        public static double Unit(double x)
            => double.IsNaN(x) ? 0 : x <= 0 ? 0 : x >= 1 ? 1 : x;

        public static int ClampIndex(int index, int count)
            => index < -1 ? -1 : index >= count ? count - 1 : index;
    }

    [Serializable]
    public sealed class ElderObservation
    {
        public int SchemaVersion = ElderRLContract.SchemaVersion;
        public ElderCoreObservation Core = new ElderCoreObservation();
        public ElderFieldObservation Fields = new ElderFieldObservation();
        public ElderHerdObservation Herd = new ElderHerdObservation();
        public ElderSiteObservation[] Sites = Array.Empty<ElderSiteObservation>();
        public ElderFishingObservation[] Fishing = Array.Empty<ElderFishingObservation>();
        public ElderCapabilityObservation[] Capabilities = Array.Empty<ElderCapabilityObservation>();
        public ElderCommodityObservation[] Commodities = Array.Empty<ElderCommodityObservation>();
        public ElderContactObservation[] Contacts = Array.Empty<ElderContactObservation>();
        public ElderObligationObservation[] Obligations = Array.Empty<ElderObligationObservation>();
        public ElderShockObservation Shock = new ElderShockObservation();
        public ElderActionMask Mask = new ElderActionMask();
    }

    [Serializable]
    public sealed class ElderCoreObservation
    {
        public int Camp, Population, LinkedPopulation, DeficitTurns;
        public double ElapsedDays, HorizonYears;
        public double QoL, FoodNeedsMet, WaterNeedsMet, ClothingCoverage, HousingCoverage;
        public double FoodReserveYears, PopulationTrend, QoLTrend, NetMigrationTrend;
        public bool IsVillage, RecentlyMoved;
    }

    [Serializable]
    public sealed class ElderFieldObservation
    {
        public int RetainedTiles;
        public double CurrentOperation;
        public double MaintenanceWorkerYears;
        public double CurrentFoodSupport;
        public double FutureFoodSupport;
        public double RebuildBurden;
        public double RetainedCapitalService;
    }

    [Serializable]
    public sealed class ElderHerdObservation
    {
        public int Animals;
        public double Nutrition, ManagementFulfilment;
        public double ZeroDisplacementCapacity, AbsoluteCapacity, WaterCapacity;
        public double CurrentManagementWorkerYears;
        public double CurrentAnimalFood, HumanFoodDisplaced;
        public double BirthTrend, MortalityTrend, SlaughterTrend;
    }

    [Serializable]
    public sealed class ElderSiteObservation
    {
        public int Tile;
        public bool Current;
        public double HumanFood, HumanFoodAfterHerd;
        public double HerdZeroDisplacement, HerdAbsolute, HerdWater;
        public double FixedShelter;
        public double RecurringLabor, AbsoluteRecurringLabor;
        public double RetainedFieldService, LostRebuildBurden;
    }

    [Serializable]
    public sealed class ElderFishingObservation
    {
        public int Tile, Distance;
        public bool Incumbent, Known;
        public double YieldPerFisherYear;
        public double FoodPotential, Biomass, Condition;
        public double OpeningCapital, OperationFraction;
        public double MaintenanceWorkerYears, BuildWorkerYears;
    }

    [Serializable]
    public sealed class ElderCapabilityObservation
    {
        // Stable semantic key (for example "Cultivation", "AnimalHusbandry",
        // "CopperWorking"). Framework adapters may embed/hash this key; the
        // simulation contract must not assume fixed feature offsets per tech.
        public string Key;
        public bool Available;
        public double Experience, Progress;
    }

    [Serializable]
    public sealed class ElderCommodityObservation
    {
        // Commodity keys remain semantic rather than position-defined so later
        // goods can enter without rewriting the core Elder contract.
        public string Key;
        public double Stock, LocalNeed, ProtectedFloor;
        public double ProductionCapacity, MarginalValue;
        public double RecentImport, RecentExport, ExternalOpportunity;
        public bool CanProduce, CanTrade;
    }

    [Serializable]
    public sealed class ElderContactObservation
    {
        public int Society, Distance;
        public double Affinity, RelativeAttractiveness;
        public double RecentTradeValue, RecentMigration;
        public bool Kin, Known;
    }

    [Serializable]
    public sealed class ElderObligationObservation
    {
        // Generic future hook for taxes, tribute, requisitions, military levies,
        // player/governor commitments and other externally imposed service needs.
        public string Key, CommodityOrService, Authority;
        public double Quantity, Fulfilled, DueYears;
        public double Priority, FailurePenalty;
        public bool Binding;
    }

    [Serializable]
    public sealed class ElderShockObservation
    {
        // v1 leaves Active false. v2 synthetic curriculum events populate only
        // effects/information the actor is permitted to know.
        public bool Active, Known;
        public string Family;
        public double Severity, RemainingYears;
        public double FoodImpact, WaterImpact, HousingImpact, CapitalImpact, HerdImpact, FishingImpact;
    }

    [Serializable]
    public sealed class ElderActionMask
    {
        public bool FieldRetention = true;
        public bool HerdRetention = true;
        public bool Development = true;
        public bool DevelopmentSplit = true;
        public bool Movement;
        // -1 means stay. Legal movement choices are indexes into Observation.Sites.
        public int[] LegalMovementCandidates = Array.Empty<int>();
    }

    [Serializable]
    public sealed class ElderDirective
    {
        public int SchemaVersion = ElderRLContract.SchemaVersion;

        // Fraction of already-existing retainable productive service to keep.
        // These are strategic targets, not worker assignments.
        public double RetainedFieldOperation = 1;
        public double RetainedHerdFraction = 1;

        // Fraction of physically available discretionary worker-years to admit
        // as Development after higher-priority service allocation.
        public double DevelopmentBudget;

        // Of the admitted Development budget, share offered to field expansion.
        // The v1 remainder is offered to herd development.
        public double FieldDevelopmentShare = .5;

        // -1 = stay; otherwise index into ElderObservation.Sites.
        public int MovementCandidate = -1;

        public ElderDirective Normalized(ElderObservation observation)
        {
            int sites = observation?.Sites?.Length ?? 0;
            return new ElderDirective
            {
                SchemaVersion = SchemaVersion,
                RetainedFieldOperation = ElderRLContract.Unit(RetainedFieldOperation),
                RetainedHerdFraction = ElderRLContract.Unit(RetainedHerdFraction),
                DevelopmentBudget = ElderRLContract.Unit(DevelopmentBudget),
                FieldDevelopmentShare = ElderRLContract.Unit(FieldDevelopmentShare),
                MovementCandidate = ElderRLContract.ClampIndex(MovementCandidate, sites)
            };
        }
    }

    [Serializable]
    public sealed class ElderRewardVector
    {
        // Keep components explicit and separately logged. Scalarisation belongs
        // to training configuration, not to the authoritative simulation contract.
        public double Survival;
        public double FoodSecurity, WaterSecurity, ServiceReliability;
        public double PopulationContinuity;
        public double EcologicalViability, SettlementViability, CapitalContinuity;
        public double ObligationOutcome;
        public double CatastrophicLoss;
    }

    public enum ElderInterventionKind
    {
        None = 0,
        PhysicalConstraint = 1,
        LegalConstraint = 2,
        IntegerWorkforce = 3,
        ResourceConstraint = 4,
        PlacementConflict = 5
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
        public ElderIntervention[] Interventions = Array.Empty<ElderIntervention>();
        public EconomicIntent Intent;
        public int AcceptedTile = -1;
    }

    [Serializable]
    public sealed class ElderStepResult
    {
        public ElderObservation Observation;
        public ElderRewardVector Reward = new ElderRewardVector();
        public double ScalarReward;
        public double ElapsedYears;
        public bool Terminated, Truncated;
        public ElderCompiledDecision Decision;
        public ElderShockEvent[] ExogenousEvents = Array.Empty<ElderShockEvent>();
        public string StateFingerprint;
    }

    [Serializable]
    public sealed class ElderShockEvent
    {
        // Training-side event description for deterministic replay. Production
        // disasters may later map onto this without changing Reset/Step.
        public int EpisodeStep;
        public string Family;
        public double Severity, DurationYears;
        public bool KnownBeforeImpact;
        public int Seed;
    }

    [Serializable]
    public sealed class ElderEnvironmentOptions
    {
        public int SchemaVersion = ElderRLContract.SchemaVersion;
        public int EpisodeDecisionLimit = 32;
        public bool MovementEnabled;
        public bool SyntheticShocksEnabled;

        // v2 curriculum knobs. They intentionally do not claim final gameplay
        // disaster frequencies or severities.
        public double ShockEpisodeProbability;
        public double ShockMinimumSeverity, ShockMaximumSeverity;
        public int ShockMinimumStep = 1, ShockMaximumStep = 31;
    }

    // Policy implementations may be heuristic, scripted, human-controlled or
    // learned. Every implementation sees the same filtered observation and mask.
    public interface IElderPolicy
    {
        ElderDirective Decide(ElderObservation observation);
    }

    // Pure compiler boundary: the supervisor proposes strategy; the compiler
    // converts it into legal simulation work without applying a separate
    // profitability veto. Physical/legal clipping must be reported.
    public interface IElderDirectiveCompiler
    {
        ElderCompiledDecision Compile(WorldState world, Camp camp,
            ElderObservation observation, ElderDirective directive);
    }

    // Framework-neutral Reset/Step seam. The first production implementation
    // should call the real TurnPlanning/DemandControl/resolver path, not a Python
    // reimplementation of ecology or economics.
    public interface IElderEnvironment
    {
        ElderObservation Reset(int seed, ElderEnvironmentOptions options);
        ElderStepResult Step(ElderDirective directive);
    }
}
