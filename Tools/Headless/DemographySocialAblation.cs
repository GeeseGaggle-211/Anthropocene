using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Anthropocene.Simulation;

// Opt-in same-physics social-flow attribution. Each seed completes one PhysicalMortalityV1
// opening with both social transfer systems disabled. Four branches then load that exact save
// and run the same continuation horizon with fission/immigration off/off, on/off, off/on and
// on/on. The tool changes no production calibration or authoritative state.
internal static class DemographySocialAblation
{
    private const int RapidReversalWindow = 3;

    private sealed class Variant
    {
        public string Name;
        public bool Fission, Immigration;
    }

    private sealed class Calibration
    {
        public string Name;
        public double Deadband, MaxFraction, ReturnPenalty;
        public int ReturnCooldown;

        public void Apply(SimulationConfig config)
        {
            config.ImmigrationDeadband = Deadband;
            config.ImmigrationMaxFraction = MaxFraction;
            config.ImmigrationReturnCooldownTurns = ReturnCooldown;
            config.ImmigrationReturnPenalty = ReturnPenalty;
        }
    }

    private sealed class Snapshot
    {
        public bool Exists;
        public int Population, LinkedPopulation, Workforce;
        public double Food, Clothing, Housing, QoL, Expectation, DeficitYears, BirthRate, DeathRate, NetRate;

        public bool Alive => Exists && LinkedPopulation > 0;
        public bool Healthy => Alive && Food >= .8 && Clothing >= .8 && Housing >= .8;
    }

    private sealed class Follow
    {
        public string Kind;
        public int EventTurn, Source, Target;
        public bool InitialNegativeNet;
        public double ExpectedQoL;
        public Snapshot Target0, Source0, Target1, Source1, Target3, Source3;

        public void Observe(int turn, Dictionary<int, Snapshot> snapshots)
        {
            int age = turn - EventTurn;
            if (age == 1)
            {
                Target1 = Get(snapshots, Target); Source1 = Get(snapshots, Source);
            }
            else if (age == 3)
            {
                Target3 = Get(snapshots, Target); Source3 = Get(snapshots, Source);
            }
        }
    }

    private sealed class Flow
    {
        public MigrationReport Report;
        public Snapshot Source, Destination;
        public double SourceLineageMean, DestinationLineageMean;
        public int SourceLineagePopulation, DestinationLineagePopulation;
        public Follow Follow;
    }

    private sealed class Reversal
    {
        public Flow First, Return;
        public bool InvolvesDaughter;
        public int YoungestDaughterAge = -1;
    }

    private sealed class FollowSummary
    {
        public int Events, ObservedAt1, ExtinctAt1, ObservedAt3, ExtinctAt3, Healthy0, Healthy3, Recovered3, NegativeNet0, NegativeNet1, NegativeNet3;
        public double ExpectedQoLError1, FoodDelta1, ClothingDelta1, HousingDelta1, DeficitDelta1;
        public double PopulationRatio3, FoodDelta3, ClothingDelta3, HousingDelta3, QoLDelta3, DeficitDelta3;
    }

    private sealed class Outcome
    {
        public int Seed, Turns, StartCamps, EndCamps, StartPopulation, EndPopulation, Survivors;
        public string Variant;
        public int Births, Deaths, Migrations, Migrants, PenalizedMigrations, Fissions, SplitPopulation, Reversals;
        public int ReversalsAt1, ReversalsAt2, ReversalsAt3;
        public int NegativeNetDestinations, NegativeNetDaughters, HigherQolWorseMortality;
        public int YoungDaughterMigrations, YoungDaughterReversals, Viable, Target, MinPopulation, MedianPopulation, MaxPopulation;
        public int DaughterParentFissions, DaughterParentFissionsAt1, DaughterParentFissionsAt2, DaughterParentFissionsAt3, DaughterParentFissionsOlder;
        public int CrossLineageMigrants, TransferAffectedSettlementTurns;
        public int KinMigrations;
        public long CampTurns, PopulationTurns;
        public long FissionProposals, FissionDropped, FissionCrowding, FissionDistress, FissionCooldownBlocked;
        public long ImmigrationIntents, ImmigrationAccepted, ImmigrationPartial, ImmigrationRejected;
        public long SocialForecasts, SocialCacheBuilds, SocialCacheHits, SocialCandidates;
        public long SocialMoverRejected, SocialAggregateRejected, SocialSourceRejected, SocialIncumbentRejected;
        public double Exposure, Crowding, Disease, Starvation, WorstConservation;
        public double MeanFood, MeanClothing, MeanHousing, LocalWeightedQoL, MeanLineageQoL;
        public double Milliseconds, SocialMilliseconds;
        public readonly List<Follow> Follows = new List<Follow>();
        public readonly List<Reversal> ReversalDetails = new List<Reversal>();
        public readonly List<SourceOpportunity> SourceOpportunities = new List<SourceOpportunity>();
        public readonly HashSet<int> TerminalNewDaughters = new HashSet<int>();
        public readonly HashSet<int> TerminalSocialTransfers = new HashSet<int>();
    }

    private sealed class SourceOpportunity
    {
        public int Population, Migrants;
        public bool IsVillage;
        public double QoL, Food, Clothing, Housing;
    }

    private sealed class Aggregate
    {
        public string Variant;
        public int Seeds, Turns, StartCamps, EndCamps, StartPopulation, EndPopulation, Survivors;
        public int Births, Deaths, Migrations, Migrants, PenalizedMigrations, Fissions, SplitPopulation, Reversals;
        public int ReversalsAt1, ReversalsAt2, ReversalsAt3;
        public int NegativeNetDestinations, NegativeNetDaughters, HigherQolWorseMortality;
        public int YoungDaughterMigrations, YoungDaughterReversals, Viable, Target;
        public int DaughterParentFissions, DaughterParentFissionsAt1, DaughterParentFissionsAt2, DaughterParentFissionsAt3, DaughterParentFissionsOlder;
        public int CrossLineageMigrants, TransferAffectedSettlementTurns;
        public int KinMigrations;
        public long CampTurns, PopulationTurns, FissionProposals, FissionDropped, FissionCrowding, FissionDistress, FissionCooldownBlocked;
        public long ImmigrationIntents, ImmigrationAccepted, ImmigrationPartial, ImmigrationRejected;
        public long SocialForecasts, SocialCacheBuilds, SocialCacheHits, SocialCandidates;
        public long SocialMoverRejected, SocialAggregateRejected, SocialSourceRejected, SocialIncumbentRejected;
        public double Exposure, Crowding, Disease, Starvation, WorstConservation;
        public double FoodWeighted, ClothingWeighted, HousingWeighted, LocalQoLWeighted, LineageQoLSum;
        public double Milliseconds, SocialMilliseconds;
        public FollowSummary DaughterFollow, DaughterParentFollow, ReceiverFollow;
    }

    private static readonly Variant[] Variants =
    {
        new Variant { Name = "off-off", Fission = false, Immigration = false },
        new Variant { Name = "fission-only", Fission = true, Immigration = false },
        new Variant { Name = "immigration-only", Fission = false, Immigration = true },
        new Variant { Name = "both", Fission = true, Immigration = true }
    };

    // Hysteresis-only candidates leave the authoritative QoL welfare projection, deadband, group size,
    // fission thresholds and every physical-demography parameter untouched. The deadband and
    // group-size rows are controls that tell us whether a broad flow reduction merely hides the
    // same reversal problem.
    private static readonly Calibration[] Calibrations =
    {
        new Calibration { Name = "hysteresis-off", Deadband = .075, MaxFraction = .10, ReturnCooldown = 0, ReturnPenalty = 0 },
        new Calibration { Name = "current-default-0.10", Deadband = .075, MaxFraction = .10, ReturnCooldown = 3, ReturnPenalty = .10 },
        new Calibration { Name = "penalty-0.15", Deadband = .075, MaxFraction = .10, ReturnCooldown = 3, ReturnPenalty = .15 },
        new Calibration { Name = "candidate-0.20", Deadband = .075, MaxFraction = .10, ReturnCooldown = 3, ReturnPenalty = .20 },
        new Calibration { Name = "penalty-0.25", Deadband = .075, MaxFraction = .10, ReturnCooldown = 3, ReturnPenalty = .25 },
        new Calibration { Name = "penalty-0.30", Deadband = .075, MaxFraction = .10, ReturnCooldown = 3, ReturnPenalty = .30 },
        new Calibration { Name = "cooldown-5-penalty-0.15", Deadband = .075, MaxFraction = .10, ReturnCooldown = 5, ReturnPenalty = .15 },
        new Calibration { Name = "cooldown-5-penalty-0.20", Deadband = .075, MaxFraction = .10, ReturnCooldown = 5, ReturnPenalty = .20 },
        new Calibration { Name = "deadband-0.10-control", Deadband = .10, MaxFraction = .10, ReturnCooldown = 3, ReturnPenalty = .10 },
        new Calibration { Name = "maxfraction-0.075-control", Deadband = .075, MaxFraction = .075, ReturnCooldown = 3, ReturnPenalty = .10 }
    };

    private static int assertions;
    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new Exception("Demography social ablation: " + message);
    }

    public static int Run(int continuations = 30)
    {
        if (continuations < 25 || continuations > 50)
            throw new ArgumentOutOfRangeException(nameof(continuations), "The v1.1 social ablation requires 25-50 continuation decisions.");
        assertions = 0;
        CheckSnapshotClassification();
        var outcomes = new List<Outcome>();
        foreach (int seed in new[] { 17, 53, 991 })
        {
            byte[] boundary = OpeningBoundary(seed, out int openingTurns, out string fingerprint,
                out int boundaryCamps, out int boundaryPopulation);
            Console.WriteLine("DEMOGRAPHY_SOCIAL_BOUNDARY seed=" + seed + " openingTurns=" + openingTurns
                + " camps=" + boundaryCamps + " population=" + boundaryPopulation
                + " fingerprint=" + fingerprint);
            foreach (Variant variant in Variants)
            {
                WorldState probe = SaveCodec.Load(boundary);
                Check(SaveCodec.Fingerprint(probe) == fingerprint,
                    "seed " + seed + " variant " + variant.Name + " loads the identical boundary save");
                Check(probe.Camps.Count == boundaryCamps && probe.Camps.Sum(probe.LinkedPopulation) == boundaryPopulation,
                    "seed " + seed + " variant " + variant.Name + " starts with identical people and camps");
                outcomes.Add(RunVariant(probe, seed, variant, continuations));
            }
        }

        foreach (Outcome outcome in outcomes)
        {
            PrintOutcome(outcome);
            foreach (Reversal reversal in outcome.ReversalDetails) PrintReversal(outcome, reversal);
        }

        var totals = Variants.Select(v => AggregateOf(v.Name, outcomes.Where(o => o.Variant == v.Name))).ToDictionary(a => a.Variant);
        foreach (Variant variant in Variants) PrintAggregate(totals[variant.Name]);
        PrintEffects(totals);

        Check(totals["off-off"].Migrations == 0 && totals["off-off"].Fissions == 0,
            "off/off control commits no social transfers");
        Check(totals["fission-only"].Migrations == 0 && totals["fission-only"].Fissions > 0,
            "fission-only branch isolates fission");
        Check(totals["immigration-only"].Fissions == 0,
            "immigration-only branch isolates immigration even when no eligible local contact produces a flow");
        Check(totals["both"].Fissions > 0 && totals["both"].Migrations > 0,
            "combined branch exercises both mechanics");
        Console.WriteLine("PASS: " + assertions + " same-physics social-ablation assertions");
        return assertions;
    }

    public static int RunCalibrationSweep(int continuations = 30)
    {
        if (continuations < 25 || continuations > 50)
            throw new ArgumentOutOfRangeException(nameof(continuations), "The immigration calibration sweep requires 25-50 continuation decisions.");
        assertions = 0;
        CheckSnapshotClassification();
        var outcomes = new List<Outcome>();
        foreach (int seed in new[] { 17, 53, 991 })
        {
            byte[] boundary = OpeningBoundary(seed, out int openingTurns, out string fingerprint,
                out int boundaryCamps, out int boundaryPopulation);
            Console.WriteLine("DEMOGRAPHY_IMMIGRATION_BOUNDARY seed=" + seed + " openingTurns=" + openingTurns
                + " camps=" + boundaryCamps + " population=" + boundaryPopulation
                + " fingerprint=" + fingerprint);
            foreach (Calibration calibration in Calibrations)
            {
                WorldState probe = SaveCodec.Load(boundary);
                Check(SaveCodec.Fingerprint(probe) == fingerprint,
                    "seed " + seed + " calibration " + calibration.Name + " loads the identical boundary save");
                Check(probe.Camps.Count == boundaryCamps && probe.Camps.Sum(probe.LinkedPopulation) == boundaryPopulation,
                    "seed " + seed + " calibration " + calibration.Name + " starts with identical people and camps");
                outcomes.Add(RunVariant(probe, seed, new Variant
                {
                    Name = calibration.Name,
                    Fission = true,
                    Immigration = true
                }, continuations, calibration));
            }
        }

        var totals = Calibrations.Select(c => AggregateOf(c.Name, outcomes.Where(o => o.Variant == c.Name)))
            .ToDictionary(a => a.Variant);
        foreach (Calibration calibration in Calibrations) PrintCalibration(calibration, totals[calibration.Name]);

        Aggregate off = totals["hysteresis-off"], current = totals["current-default-0.10"], candidate = totals["candidate-0.20"];
        Check(off.Migrations > 0 && off.Reversals > 0, "the no-hysteresis control exercises migration and rapid reversals");
        Check(current.Reversals < off.Reversals,
            "the shipped return hysteresis reduces rapid reversals against its matched disabled control");
        Check(current.ReceiverFollow.ExtinctAt1 == 0 && current.ReceiverFollow.ExtinctAt3 == 0,
            "the shipped run has no receiving-settlement extinction through the three-turn follow-up");
        Check(candidate.Reversals <= current.Reversals,
            "the exploratory stronger penalty does not increase observed pair returns");
        string candidateReason = candidate.Reversals < current.Reversals
            ? "pair-return-reduction-without-eliminating-one-turn-returns-or-demonstrating-receiver-welfare-benefit"
            : "no-marginal-reversal-benefit-and-no-demonstrated-receiver-welfare-benefit";
        Console.WriteLine("DEMOGRAPHY_IMMIGRATION_ASSESSMENT shipped=current-default-0.10 candidate=candidate-0.20 status=NOT_PROMOTED"
            + " rapidReversals=" + current.Reversals + "->" + candidate.Reversals
            + " reversalAges=" + current.ReversalsAt1 + "/" + current.ReversalsAt2 + "/" + current.ReversalsAt3
            + "->" + candidate.ReversalsAt1 + "/" + candidate.ReversalsAt2 + "/" + candidate.ReversalsAt3
            + " migrations=" + current.Migrations + "->" + candidate.Migrations
            + " migrants=" + current.Migrants + "->" + candidate.Migrants
            + " fissions=" + current.Fissions + "->" + candidate.Fissions
            + " receiverNegativeNet3=" + current.ReceiverFollow.NegativeNet3 + "->" + candidate.ReceiverFollow.NegativeNet3
            + " receiverExtinct3=" + current.ReceiverFollow.ExtinctAt3 + "->" + candidate.ReceiverFollow.ExtinctAt3
            + " oneTurnReturns=" + current.ReversalsAt1 + "->" + candidate.ReversalsAt1
            + " reason=" + candidateReason);
        Console.WriteLine("DEMOGRAPHY_IMMIGRATION_HYSTERESIS off=" + off.Reversals
            + " shipped=" + current.Reversals + " candidate=" + candidate.Reversals
            + " shippedPer100=" + F(100.0 * current.Reversals / Math.Max(1, current.Migrations))
            + " candidatePer100=" + F(100.0 * candidate.Reversals / Math.Max(1, candidate.Migrations))
            + " result=" + (current.Reversals < off.Reversals ? "SHIPPED_HYSTERESIS_MITIGATES" : "NO_MEASURED_REDUCTION"));
        Console.WriteLine("PASS: " + assertions + " immigration calibration-sweep assertions");
        return assertions;
    }

    public static int RunFissionCooldownSweep(int continuations = 30)
    {
        if (continuations < 25 || continuations > 50)
            throw new ArgumentOutOfRangeException(nameof(continuations), "The fission cooldown sweep requires 25-50 continuation decisions.");
        assertions = 0;
        CheckSnapshotClassification();
        int[] cooldowns = { 0, 1, 2 };
        var outcomes = new List<Outcome>();
        foreach (int seed in new[] { 17, 53, 991 })
        {
            byte[] boundary = OpeningBoundary(seed, out int openingTurns, out string fingerprint,
                out int boundaryCamps, out int boundaryPopulation);
            Console.WriteLine("DEMOGRAPHY_FISSION_COOLDOWN_BOUNDARY seed=" + seed + " openingTurns=" + openingTurns
                + " camps=" + boundaryCamps + " population=" + boundaryPopulation
                + " fingerprint=" + fingerprint);
            foreach (int cooldown in cooldowns)
            {
                WorldState probe = SaveCodec.Load(boundary);
                string name = "cooldown-" + cooldown;
                Check(SaveCodec.Fingerprint(probe) == fingerprint,
                    "seed " + seed + " " + name + " loads the identical boundary save");
                Check(probe.Camps.Count == boundaryCamps && probe.Camps.Sum(probe.LinkedPopulation) == boundaryPopulation,
                    "seed " + seed + " " + name + " starts with identical people and camps");
                outcomes.Add(RunVariant(probe, seed, new Variant
                {
                    Name = name, Fission = true, Immigration = true
                }, continuations, fissionDaughterCooldownTurns: cooldown));
            }
        }

        var totals = cooldowns.Select(c => AggregateOf("cooldown-" + c,
            outcomes.Where(o => o.Variant == "cooldown-" + c))).ToDictionary(a => a.Variant);
        foreach (int cooldown in cooldowns) PrintFissionCooldown(cooldown, totals["cooldown-" + cooldown]);

        Aggregate control = totals["cooldown-0"], current = totals["cooldown-1"], longer = totals["cooldown-2"];
        Check(control.Fissions > 0 && current.Fissions > 0 && longer.Fissions > 0,
            "every cooldown branch exercises fission on matched native trajectories");
        Check(control.FissionCooldownBlocked == 0,
            "the zero-cooldown control never records a maturity block");
        Check(current.FissionCooldownBlocked > 0 && longer.FissionCooldownBlocked >= current.FissionCooldownBlocked,
            "positive cooldowns exercise and extend the daughter maturity gate");
        Check(current.DaughterParentFissionsAt1 == 0 && longer.DaughterParentFissionsAt1 == 0
            && longer.DaughterParentFissionsAt2 == 0,
            "the observed recursive fissions respect each configured eligibility boundary");

        Console.WriteLine("DEMOGRAPHY_FISSION_COOLDOWN_ASSESSMENT default=1 control=0 longer=2"
            + " fissions=" + control.Fissions + "/" + current.Fissions + "/" + longer.Fissions
            + " daughterParentFissions=" + control.DaughterParentFissions + "/" + current.DaughterParentFissions + "/" + longer.DaughterParentFissions
            + " age1=" + control.DaughterParentFissionsAt1 + "/" + current.DaughterParentFissionsAt1 + "/" + longer.DaughterParentFissionsAt1
            + " camps=" + control.EndCamps + "/" + current.EndCamps + "/" + longer.EndCamps
            + " population=" + control.EndPopulation + "/" + current.EndPopulation + "/" + longer.EndPopulation
            + " viable=" + control.Viable + "/" + current.Viable + "/" + longer.Viable
            + " localQoL=" + F(control.LocalQoLWeighted / Math.Max(1, control.EndPopulation)) + "/"
                + F(current.LocalQoLWeighted / Math.Max(1, current.EndPopulation)) + "/"
                + F(longer.LocalQoLWeighted / Math.Max(1, longer.EndPopulation))
            + " status=MEASURED_NOT_A_GO_GATE");
        Console.WriteLine("PASS: " + assertions + " fission cooldown-sweep assertions");
        return assertions;
    }

    public static int RunQoLHistogram(int continuations = 30)
    {
        if (continuations < 25 || continuations > 50)
            throw new ArgumentOutOfRangeException(nameof(continuations), "The terminal QoL histogram requires 25-50 continuation decisions.");
        assertions = 0;
        CheckSnapshotClassification();
        CheckSourceEligibility();
        Console.WriteLine("DEMOGRAPHY_QOL_META continuations=" + continuations);
        var opportunities = new List<SourceOpportunity>();
        foreach (int seed in new[] { 17, 53, 991 })
        {
            byte[] boundary = OpeningBoundary(seed, out int openingTurns, out string fingerprint,
                out int boundaryCamps, out int boundaryPopulation);
            Console.WriteLine("DEMOGRAPHY_QOL_BOUNDARY seed=" + seed + " openingTurns=" + openingTurns
                + " camps=" + boundaryCamps + " population=" + boundaryPopulation
                + " fingerprint=" + fingerprint);
            WorldState world = SaveCodec.Load(boundary);
            Check(SaveCodec.Fingerprint(world) == fingerprint,
                "seed " + seed + " QoL histogram loads the identical boundary save");
            Outcome outcome = RunVariant(world, seed, new Variant
            {
                Name = "both", Fission = true, Immigration = true
            }, continuations, captureSourceOpportunities: true);
            Check(outcome.SourceOpportunities.Count(o => o.Migrants > 0) == outcome.Migrations,
                "seed " + seed + " QoL source rows contain every committed immigration flow");
            Check(outcome.SourceOpportunities.Sum(o => o.Migrants) == outcome.Migrants,
                "seed " + seed + " QoL source rows contain every committed migrant");
            opportunities.AddRange(outcome.SourceOpportunities);
            foreach (Camp camp in world.Camps.Where(c => world.LinkedPopulation(c) > 0).OrderBy(c => c.Id))
            {
                int population = world.LinkedPopulation(camp);
                double cohesion = NeolithicProduction.SocialCohesion(world, camp, population);
                Console.WriteLine("DEMOGRAPHY_QOL_TERMINAL seed=" + seed + " camp=" + camp.Id
                    + " village=" + (camp.IsVillage ? 1 : 0)
                    + " freshDaughter=" + (outcome.TerminalNewDaughters.Contains(camp.Id) ? 1 : 0)
                    + " socialTransfer=" + (outcome.TerminalSocialTransfers.Contains(camp.Id) ? 1 : 0)
                    + " population=" + population
                    + " qol=" + D(camp.QoL) + " popQol=" + D(population * camp.QoL)
                    + " food=" + D(camp.FoodNeedsMetRatio)
                    + " clothing=" + D(camp.ClothPerCapita)
                    + " housing=" + D(camp.HousingPerCapita)
                    + " expectation=" + D(camp.RudimentaryExpectation)
                    + " cohesion=" + D(cohesion));
            }
        }

        foreach (bool village in new[] { false, true })
        foreach (string band in new[] { "adequate", "clothing-gap", "housing-gap", "both-gaps" })
        {
            SourceOpportunity[] group = opportunities.Where(o => o.IsVillage == village
                && ProtectionBand(o.Clothing, o.Housing) == band).ToArray();
            Console.WriteLine("DEMOGRAPHY_QOL_SOURCE_RATE village=" + (village ? 1 : 0)
                + " condition=" + band + " sourceCapableCampTurns=" + group.Length
                + " flows=" + group.Count(o => o.Migrants > 0)
                + " migrants=" + group.Sum(o => o.Migrants)
                + " meanQoL=" + D(group.Length == 0 ? 0 : group.Average(o => o.QoL))
                + " meanPopulation=" + D(group.Length == 0 ? 0 : group.Average(o => o.Population)));
        }
        Console.WriteLine("PASS: " + assertions + " terminal QoL-histogram assertions");
        return assertions;
    }

    private static byte[] OpeningBoundary(int seed, out int openingTurns, out string fingerprint,
        out int camps, out int population)
    {
        var cfg = SimulationConfig.Small(8);
        cfg.DemographicModel = DemographicModel.PhysicalMortalityV1;
        cfg.QoLMortalityEnabled = false;
        cfg.FissionEnabled = false; cfg.ImmigrationEnabled = false; cfg.TradeEnabled = true;
        cfg.EconomicAllocation = EconomicAllocationStrategy.DemandControl;
        cfg.Validate();
        var world = new WorldGenerator().Generate(seed, cfg); var resolver = new TurnResolver();
        openingTurns = 0;
        while (!world.Finished && openingTurns < 30)
        {
            int before = world.Camps.Sum(world.LinkedPopulation);
            PlanningResult plan = AiPlanning.Decide(world);
            TurnRecord record = resolver.Resolve(world, plan.Orders, plan.Traces);
            Check(world.Camps.Sum(world.LinkedPopulation) == before + record.Growth - record.Deaths,
                "seed " + seed + " opening turn " + record.Turn + " conserves population");
            Check(record.Migrations.Count == 0 && record.Fissions.Count == 0,
                "seed " + seed + " social-free opening has no transfers");
            openingTurns++;
        }
        Check(world.Finished, "seed " + seed + " completes its social-free opening within thirty turns");
        byte[] bytes = SaveCodec.Save(world);
        WorldState replay = SaveCodec.Load(bytes);
        fingerprint = SaveCodec.Fingerprint(world);
        Check(SaveCodec.Fingerprint(replay) == fingerprint, "seed " + seed + " opening boundary round trips exactly");
        camps = world.Camps.Count; population = world.Camps.Sum(world.LinkedPopulation);
        return bytes;
    }

    private static Outcome RunVariant(WorldState world, int seed, Variant variant, int continuations,
        Calibration calibration = null, bool captureSourceOpportunities = false,
        int? fissionDaughterCooldownTurns = null)
    {
        world.Config.FissionEnabled = variant.Fission;
        world.Config.ImmigrationEnabled = variant.Immigration;
        calibration?.Apply(world.Config);
        if (fissionDaughterCooldownTurns.HasValue)
            world.Config.FissionDaughterCooldownTurns = fissionDaughterCooldownTurns.Value;
        world.Config.Validate();
        var result = new Outcome
        {
            Seed = seed, Variant = variant.Name, StartCamps = world.Camps.Count,
            StartPopulation = world.Camps.Sum(world.LinkedPopulation)
        };
        var resolver = new TurnResolver();
        var lastFlow = new Dictionary<(int Source, int Destination), Flow>();
        var daughterTurns = new Dictionary<int, int>();
        var expectedStartingPopulation = world.Camps.ToDictionary(c => c.Id, world.LinkedPopulation);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        TurnRecord lastRecord = null;
        for (int i = 0; i < continuations; i++)
        {
            int before = world.Camps.Sum(world.LinkedPopulation);
            PlanningResult plan = AiPlanning.Decide(world);
            TurnRecord record = resolver.Resolve(world, plan.Orders, plan.Traces);
            lastRecord = record;
            int after = world.Camps.Sum(world.LinkedPopulation);
            Check(after == before + record.Growth - record.Deaths,
                "seed " + seed + " variant " + variant.Name + " turn " + record.Turn + " conserves global population");
            Check(record.Demographics.Sum(d => d.GrossBirths) == record.Growth
                && record.Demographics.Sum(d => d.GrossDeaths) == record.Deaths,
                "seed " + seed + " variant " + variant.Name + " turn " + record.Turn + " closes integer demographic ledgers");
            foreach (DemographicReport demographic in record.Demographics)
            {
                Check(expectedStartingPopulation.TryGetValue(demographic.Camp, out int expected)
                    && demographic.StartingPopulation == expected,
                    "seed " + seed + " variant " + variant.Name + " turn " + record.Turn
                    + " starts demography from the preceding Close boundary");
                Check(demographic.EndingPopulation == demographic.StartingPopulation
                    + demographic.GrossBirths - demographic.GrossDeaths,
                    "seed " + seed + " variant " + variant.Name + " turn " + record.Turn
                    + " ends demography before social transfers");
            }

            var snapshots = Snapshots(world);
            foreach (Follow follow in result.Follows) follow.Observe(record.Turn, snapshots);

            foreach (MigrationReport migration in record.Migrations)
            {
                var flow = CaptureFlow(record, migration, snapshots);
                var follow = new Follow
                {
                    Kind = "receiver", EventTurn = record.Turn, Source = migration.SourceCamp,
                    Target = migration.DestinationCamp, Target0 = flow.Destination, Source0 = flow.Source,
                    InitialNegativeNet = !migration.DestinationNetNonnegative, ExpectedQoL = migration.PostInflowQoL
                };
                flow.Follow = follow; result.Follows.Add(follow);
                var reverse = (migration.DestinationCamp, migration.SourceCamp);
                if (lastFlow.TryGetValue(reverse, out Flow previous)
                    && record.Turn - previous.Report.Turn <= RapidReversalWindow)
                {
                    int gap = record.Turn - previous.Report.Turn;
                    int age = YoungestDaughterAge(daughterTurns, record.Turn, migration.SourceCamp, migration.DestinationCamp);
                    var reversal = new Reversal
                    {
                        First = previous, Return = flow, InvolvesDaughter = age >= 0, YoungestDaughterAge = age
                    };
                    result.ReversalDetails.Add(reversal); result.Reversals++;
                    if (gap == 1) result.ReversalsAt1++;
                    else if (gap == 2) result.ReversalsAt2++;
                    else if (gap == 3) result.ReversalsAt3++;
                    if (reversal.InvolvesDaughter) result.YoungDaughterReversals++;
                }
                lastFlow[(migration.SourceCamp, migration.DestinationCamp)] = flow;
                int daughterAge = YoungestDaughterAge(daughterTurns, record.Turn, migration.SourceCamp, migration.DestinationCamp);
                if (daughterAge >= 0) result.YoungDaughterMigrations++;
                if (!migration.DestinationNetNonnegative) result.NegativeNetDestinations++;
                if (migration.HigherQoLWorseMortality) result.HigherQolWorseMortality++;
                if (migration.Kin) result.KinMigrations++;
                if (migration.Penalty > 1e-12) result.PenalizedMigrations++;
            }

            foreach (int campId in record.Migrations.SelectMany(m => new[] { m.SourceCamp, m.DestinationCamp }).Distinct())
            {
                DemographicReport demographic = record.Demographics.Single(d => d.Camp == campId);
                Camp camp = world.Camps.Single(c => c.Id == campId);
                Check(world.LinkedPopulation(camp) == demographic.EndingPopulation
                    - demographic.Emigrated + demographic.Immigrated,
                    "seed " + seed + " variant " + variant.Name + " turn " + record.Turn
                    + " applies immigration only after the current demographic result");
            }

            foreach (FissionReport fission in record.Fissions)
            {
                if (daughterTurns.TryGetValue(fission.ParentCamp, out int parentFoundedTurn))
                {
                    int age = record.Turn - parentFoundedTurn;
                    result.DaughterParentFissions++;
                    if (age == 1) result.DaughterParentFissionsAt1++;
                    else if (age == 2) result.DaughterParentFissionsAt2++;
                    else if (age == 3) result.DaughterParentFissionsAt3++;
                    else result.DaughterParentFissionsOlder++;
                    Check(age >= world.Config.FissionDaughterCooldownTurns + 1,
                        "seed " + seed + " variant " + variant.Name + " turn " + record.Turn
                        + " never commits fission before a daughter's maturity boundary");
                }
                daughterTurns[fission.DaughterCamp] = record.Turn;
                Snapshot daughter = Get(snapshots, fission.DaughterCamp), parent = Get(snapshots, fission.ParentCamp);
                Check(daughter.Exists && parent.Exists, "seed " + seed + " committed fission camps remain represented at the boundary");
                Check(Math.Abs(daughter.Expectation - parent.Expectation) <= 1e-9,
                    "seed " + seed + " fission daughter " + fission.DaughterCamp + " inherits expectation without reset");
                result.Follows.Add(new Follow
                {
                    Kind = "daughter", EventTurn = record.Turn, Source = fission.ParentCamp,
                    Target = fission.DaughterCamp, Target0 = daughter, Source0 = parent,
                    InitialNegativeNet = !fission.DaughterNetNonnegative, ExpectedQoL = fission.DaughterQoL
                });
                if (!fission.DaughterNetNonnegative) result.NegativeNetDaughters++;
            }

            if (captureSourceOpportunities)
            {
                var migrationsBySource = record.Migrations.ToDictionary(m => m.SourceCamp);
                foreach (DemographicReport demographic in record.Demographics)
                {
                    Camp camp = world.Camps.Single(c => c.Id == demographic.Camp);
                    // Immigration evaluates resident camp population at the frozen post-demography boundary, not
                    // linked expeditions. Reconstruct it after Close by reversing this turn's social transfers.
                    int residents = PreTransferResidents(camp, demographic);
                    Check(residents >= 0, "seed " + seed + " variant " + variant.Name
                        + " turn " + record.Turn + " reconstructs a nonnegative pre-transfer resident population");
                    if (!ImmigrationSourceEligible(world.Config, residents)) continue;
                    migrationsBySource.TryGetValue(camp.Id, out MigrationReport migration);
                    Check(migration == null || migration.SourceBefore == residents,
                        "seed " + seed + " variant " + variant.Name + " turn " + record.Turn
                        + " reconstructs an accepted source's exact pre-transfer residents");
                    result.SourceOpportunities.Add(new SourceOpportunity
                    {
                        Population = residents,
                        Migrants = migration?.Migrants ?? 0, IsVillage = camp.IsVillage,
                        QoL = camp.QoL, Food = camp.FoodNeedsMetRatio,
                        Clothing = camp.ClothPerCapita, Housing = camp.HousingPerCapita
                    });
                }
            }

            foreach (DemographicReport d in record.Demographics)
            {
                double causes = d.ExposureDeaths + d.CrowdingDeaths + d.DiseaseDeaths
                    + d.StarvationDeaths + d.AdditionalDeaths;
                double error = Math.Abs(d.ContinuousDeaths - causes);
                Check(error <= 1e-7 * Math.Max(1, d.ContinuousDeaths),
                    "seed " + seed + " variant " + variant.Name + " turn " + record.Turn + " attributes every death cause");
                result.WorstConservation = Math.Max(result.WorstConservation, Math.Abs(d.ConservationError));
                result.Exposure += d.ExposureDeaths; result.Crowding += d.CrowdingDeaths;
                result.Disease += d.DiseaseDeaths; result.Starvation += d.StarvationDeaths;
            }
            Check(result.WorstConservation <= 1e-7,
                "seed " + seed + " variant " + variant.Name + " continuous demography remains conserved");
            Check(!variant.Immigration && record.Migrations.Count == 0 || variant.Immigration,
                "seed " + seed + " variant " + variant.Name + " respects the immigration switch");
            Check(!variant.Fission && record.Fissions.Count == 0 || variant.Fission,
                "seed " + seed + " variant " + variant.Name + " respects the fission switch");

            result.Births += record.Growth; result.Deaths += record.Deaths;
            result.Migrations += record.Migrations.Count; result.Migrants += record.Migrations.Sum(m => m.Migrants);
            result.Fissions += record.Fissions.Count; result.SplitPopulation += record.Fissions.Sum(f => f.Daughter);
            result.FissionCrowding += record.Fissions.Count(f => f.Trigger == FissionTrigger.Crowding);
            result.FissionDistress += record.Fissions.Count(f => f.Trigger == FissionTrigger.Distress);
            result.CrossLineageMigrants += record.LineageWelfare.Sum(w => w.Emigrated);
            result.TransferAffectedSettlementTurns += record.SettlementWelfare.Count(w => w.TransferAffected);
            result.FissionProposals += resolver.LastMetrics.FissionProposals;
            result.FissionDropped += resolver.LastMetrics.FissionDropped;
            result.FissionCooldownBlocked += resolver.LastMetrics.FissionCooldownBlocked;
            result.ImmigrationIntents += resolver.LastMetrics.ImmigrationIntents;
            result.ImmigrationAccepted += resolver.LastMetrics.ImmigrationAccepted;
            result.ImmigrationPartial += resolver.LastMetrics.ImmigrationPartial;
            result.ImmigrationRejected += resolver.LastMetrics.ImmigrationRejected;
            result.SocialForecasts += resolver.LastMetrics.SocialForecasts;
            result.SocialCacheBuilds += resolver.LastMetrics.SocialForecastCacheBuilds;
            result.SocialCacheHits += resolver.LastMetrics.SocialForecastCacheHits;
            result.SocialCandidates += resolver.LastMetrics.SocialCandidatesEvaluated;
            result.SocialMoverRejected += resolver.LastMetrics.SocialMoverRejected;
            result.SocialAggregateRejected += resolver.LastMetrics.SocialAggregateRejected;
            result.SocialSourceRejected += resolver.LastMetrics.SocialSourceRejected;
            result.SocialIncumbentRejected += resolver.LastMetrics.SocialIncumbentRejected;
            result.SocialMilliseconds += resolver.LastMetrics.ImmigrationMilliseconds + resolver.LastMetrics.FissionMilliseconds;
            result.CampTurns += world.Camps.Count; result.PopulationTurns += after;
            result.Turns++;
            expectedStartingPopulation = world.Camps.ToDictionary(c => c.Id, world.LinkedPopulation);
        }
        timer.Stop(); result.Milliseconds = timer.Elapsed.TotalMilliseconds;
        if (lastRecord != null)
        {
            foreach (FissionReport fission in lastRecord.Fissions)
            {
                result.TerminalNewDaughters.Add(fission.DaughterCamp);
                result.TerminalSocialTransfers.Add(fission.ParentCamp);
                result.TerminalSocialTransfers.Add(fission.DaughterCamp);
            }
            foreach (MigrationReport migration in lastRecord.Migrations)
            {
                result.TerminalSocialTransfers.Add(migration.SourceCamp);
                result.TerminalSocialTransfers.Add(migration.DestinationCamp);
            }
        }

        Camp[] living = world.Camps.Where(c => world.LinkedPopulation(c) > 0).OrderBy(c => c.Id).ToArray();
        int[] populations = living.Select(world.LinkedPopulation).OrderBy(n => n).ToArray();
        result.EndCamps = world.Camps.Count; result.Survivors = living.Length;
        result.EndPopulation = populations.Sum();
        result.MinPopulation = populations.Length == 0 ? 0 : populations[0];
        result.MedianPopulation = Percentile(populations, .5);
        result.MaxPopulation = populations.Length == 0 ? 0 : populations[^1];
        result.MeanFood = living.Length == 0 ? 0 : living.Average(c => c.FoodNeedsMetRatio);
        result.MeanClothing = living.Length == 0 ? 0 : living.Average(c => c.ClothPerCapita);
        result.MeanHousing = living.Length == 0 ? 0 : living.Average(c => c.HousingPerCapita);
        result.Viable = living.Count(c => c.FoodNeedsMetRatio >= .8 && c.ClothPerCapita >= .8 && c.HousingPerCapita >= .8);
        result.Target = living.Count(c => world.LinkedPopulation(c) >= 600 && world.LinkedPopulation(c) <= 1000
            && c.FoodNeedsMetRatio >= .8 && c.ClothPerCapita >= .8 && c.HousingPerCapita >= .8);
        result.LocalWeightedQoL = result.EndPopulation == 0 ? 0
            : living.Sum(c => world.LinkedPopulation(c) * c.QoL) / result.EndPopulation;
        result.MeanLineageQoL = lastRecord == null || lastRecord.LineageWelfare.Count == 0 ? 0
            : lastRecord.LineageWelfare.Average(w => w.MeanQoL);
        Check(living.All(c => c.Workforce.Assigned <= c.Population),
            "seed " + seed + " variant " + variant.Name + " terminal workforce remains bounded");
        return result;
    }

    private static Dictionary<int, Snapshot> Snapshots(WorldState world)
    {
        var result = new Dictionary<int, Snapshot>();
        foreach (Camp camp in world.Camps)
        {
            DemographicRates rates = PhysicalDemography.ProjectedSettlementRates(world, camp, camp.Population,
                camp.FoodNeedsMetRatio, camp.ClothPerCapita);
            result[camp.Id] = new Snapshot
            {
                Exists = true, Population = camp.Population, LinkedPopulation = world.LinkedPopulation(camp),
                Workforce = camp.Workforce.Assigned, Food = camp.FoodNeedsMetRatio,
                Clothing = camp.ClothPerCapita, Housing = camp.HousingPerCapita, QoL = camp.QoL,
                Expectation = camp.RudimentaryExpectation, DeficitYears = camp.FoodDeficitYears,
                BirthRate = rates.Birth, DeathRate = rates.Death, NetRate = rates.Net
            };
        }
        return result;
    }

    private static Snapshot Get(Dictionary<int, Snapshot> snapshots, int camp)
        => snapshots.TryGetValue(camp, out Snapshot value) ? value : new Snapshot();

    private static void CheckSnapshotClassification()
    {
        var snapshots = new Dictionary<int, Snapshot>
        {
            [1] = new Snapshot { Exists = true, LinkedPopulation = 0, Food = 1, Clothing = 1, Housing = 1 },
            [2] = new Snapshot { Exists = true, LinkedPopulation = 1, Food = 1, Clothing = 1, Housing = 1 }
        };
        Check(Get(snapshots, 1).Exists && !Get(snapshots, 1).Alive && !Get(snapshots, 1).Healthy,
            "a retained zero-population camp is extinct, not healthy");
        Check(!Get(snapshots, 99).Exists && !Get(snapshots, 99).Alive,
            "an absent camp is extinct");
        Check(Get(snapshots, 2).Alive && Get(snapshots, 2).Healthy,
            "a populated fully served camp is alive and healthy");
        var summary = Summarize(new[]
        {
            new Follow { Kind = "fixture", Target0 = snapshots[2], Target1 = snapshots[1], Target3 = Get(snapshots, 99) }
        }, "fixture");
        Check(summary.ExtinctAt1 == 1 && summary.ExtinctAt3 == 1,
            "follow-up counts retained-zero and absent camps as extinct");
    }

    private static Flow CaptureFlow(TurnRecord record, MigrationReport report, Dictionary<int, Snapshot> snapshots)
    {
        SettlementWelfareReport source = record.SettlementWelfare.FirstOrDefault(w => w.Camp == report.SourceCamp);
        SettlementWelfareReport destination = record.SettlementWelfare.FirstOrDefault(w => w.Camp == report.DestinationCamp);
        LineageWelfareReport sourceLineage = source == null ? null : record.LineageWelfare.FirstOrDefault(w => w.Lineage == source.Lineage);
        LineageWelfareReport destinationLineage = destination == null ? null : record.LineageWelfare.FirstOrDefault(w => w.Lineage == destination.Lineage);
        return new Flow
        {
            Report = report, Source = Get(snapshots, report.SourceCamp), Destination = Get(snapshots, report.DestinationCamp),
            SourceLineageMean = sourceLineage?.MeanQoL ?? 0, DestinationLineageMean = destinationLineage?.MeanQoL ?? 0,
            SourceLineagePopulation = sourceLineage?.Population ?? 0, DestinationLineagePopulation = destinationLineage?.Population ?? 0
        };
    }

    private static int YoungestDaughterAge(Dictionary<int, int> daughters, int turn, params int[] camps)
    {
        int age = int.MaxValue;
        foreach (int camp in camps)
            if (daughters.TryGetValue(camp, out int born)) age = Math.Min(age, turn - born);
        return age == int.MaxValue || age > 3 ? -1 : age;
    }

    private static FollowSummary Summarize(IEnumerable<Follow> follows, string kind, bool source = false)
    {
        var result = new FollowSummary();
        foreach (Follow follow in follows.Where(f => f.Kind == kind))
        {
            Snapshot initial = (source ? follow.Source0 : follow.Target0) ?? new Snapshot();
            Snapshot one = source ? follow.Source1 : follow.Target1;
            Snapshot three = source ? follow.Source3 : follow.Target3;
            result.Events++;
            if (initial?.Healthy == true) result.Healthy0++;
            if (source ? initial != null && initial.NetRate < 0 : follow.InitialNegativeNet) result.NegativeNet0++;
            if (one != null)
            {
                result.ObservedAt1++;
                if (!one.Alive) result.ExtinctAt1++;
                else
                {
                    if (one.NetRate < 0) result.NegativeNet1++;
                    result.ExpectedQoLError1 += one.QoL - (source ? initial.QoL : follow.ExpectedQoL);
                    result.FoodDelta1 += one.Food - initial.Food;
                    result.ClothingDelta1 += one.Clothing - initial.Clothing;
                    result.HousingDelta1 += one.Housing - initial.Housing;
                    result.DeficitDelta1 += one.DeficitYears - initial.DeficitYears;
                }
            }
            if (three == null) continue;
            result.ObservedAt3++;
            if (!three.Alive) { result.ExtinctAt3++; continue; }
            if (three.Healthy) result.Healthy3++;
            if (!initial.Healthy && three.Healthy) result.Recovered3++;
            if (three.NetRate < 0) result.NegativeNet3++;
            result.PopulationRatio3 += initial.LinkedPopulation > 0
                ? three.LinkedPopulation / (double)initial.LinkedPopulation : 0;
            result.FoodDelta3 += three.Food - initial.Food;
            result.ClothingDelta3 += three.Clothing - initial.Clothing;
            result.HousingDelta3 += three.Housing - initial.Housing;
            result.QoLDelta3 += three.QoL - initial.QoL;
            result.DeficitDelta3 += three.DeficitYears - initial.DeficitYears;
        }
        return result;
    }

    private static Aggregate AggregateOf(string variant, IEnumerable<Outcome> source)
    {
        var result = new Aggregate { Variant = variant };
        var outcomes = source.ToArray();
        foreach (Outcome o in outcomes)
        {
            result.Seeds++; result.Turns += o.Turns; result.StartCamps += o.StartCamps; result.EndCamps += o.EndCamps;
            result.StartPopulation += o.StartPopulation; result.EndPopulation += o.EndPopulation; result.Survivors += o.Survivors;
            result.Births += o.Births; result.Deaths += o.Deaths; result.Migrations += o.Migrations; result.Migrants += o.Migrants;
            result.PenalizedMigrations += o.PenalizedMigrations;
            result.Fissions += o.Fissions; result.SplitPopulation += o.SplitPopulation; result.Reversals += o.Reversals;
            result.ReversalsAt1 += o.ReversalsAt1; result.ReversalsAt2 += o.ReversalsAt2; result.ReversalsAt3 += o.ReversalsAt3;
            result.NegativeNetDestinations += o.NegativeNetDestinations; result.NegativeNetDaughters += o.NegativeNetDaughters;
            result.HigherQolWorseMortality += o.HigherQolWorseMortality;
            result.YoungDaughterMigrations += o.YoungDaughterMigrations; result.YoungDaughterReversals += o.YoungDaughterReversals;
            result.DaughterParentFissions += o.DaughterParentFissions;
            result.DaughterParentFissionsAt1 += o.DaughterParentFissionsAt1; result.DaughterParentFissionsAt2 += o.DaughterParentFissionsAt2;
            result.DaughterParentFissionsAt3 += o.DaughterParentFissionsAt3; result.DaughterParentFissionsOlder += o.DaughterParentFissionsOlder;
            result.Viable += o.Viable; result.Target += o.Target; result.CrossLineageMigrants += o.CrossLineageMigrants;
            result.TransferAffectedSettlementTurns += o.TransferAffectedSettlementTurns;
            result.KinMigrations += o.KinMigrations;
            result.CampTurns += o.CampTurns; result.PopulationTurns += o.PopulationTurns;
            result.FissionProposals += o.FissionProposals; result.FissionDropped += o.FissionDropped;
            result.FissionCrowding += o.FissionCrowding; result.FissionDistress += o.FissionDistress;
            result.FissionCooldownBlocked += o.FissionCooldownBlocked;
            result.ImmigrationIntents += o.ImmigrationIntents; result.ImmigrationAccepted += o.ImmigrationAccepted;
            result.ImmigrationPartial += o.ImmigrationPartial; result.ImmigrationRejected += o.ImmigrationRejected;
            result.SocialForecasts += o.SocialForecasts; result.SocialCacheBuilds += o.SocialCacheBuilds;
            result.SocialCacheHits += o.SocialCacheHits; result.SocialCandidates += o.SocialCandidates;
            result.SocialMoverRejected += o.SocialMoverRejected; result.SocialAggregateRejected += o.SocialAggregateRejected;
            result.SocialSourceRejected += o.SocialSourceRejected; result.SocialIncumbentRejected += o.SocialIncumbentRejected;
            result.SocialMilliseconds += o.SocialMilliseconds;
            result.Exposure += o.Exposure; result.Crowding += o.Crowding; result.Disease += o.Disease;
            result.Starvation += o.Starvation; result.WorstConservation = Math.Max(result.WorstConservation, o.WorstConservation);
            result.FoodWeighted += o.MeanFood * o.Survivors; result.ClothingWeighted += o.MeanClothing * o.Survivors;
            result.HousingWeighted += o.MeanHousing * o.Survivors; result.LocalQoLWeighted += o.LocalWeightedQoL * o.EndPopulation;
            result.LineageQoLSum += o.MeanLineageQoL; result.Milliseconds += o.Milliseconds;
        }
        result.DaughterFollow = Summarize(outcomes.SelectMany(o => o.Follows), "daughter");
        result.DaughterParentFollow = Summarize(outcomes.SelectMany(o => o.Follows), "daughter", source: true);
        result.ReceiverFollow = Summarize(outcomes.SelectMany(o => o.Follows), "receiver");
        return result;
    }

    private static void PrintOutcome(Outcome o)
    {
        Console.WriteLine("DEMOGRAPHY_SOCIAL_VARIANT seed=" + o.Seed + " variant=" + o.Variant
            + " turns=" + o.Turns + " camps=" + o.StartCamps + "->" + o.EndCamps
            + " population=" + o.StartPopulation + "->" + o.EndPopulation + " survivors=" + o.Survivors
            + " populationMinMedianMax=" + o.MinPopulation + "/" + o.MedianPopulation + "/" + o.MaxPopulation
            + " births=" + o.Births + " deaths=" + o.Deaths + " migrations=" + o.Migrations
            + " migrants=" + o.Migrants + " penalizedMigrations=" + o.PenalizedMigrations
            + " fissions=" + o.Fissions + " splitPopulation=" + o.SplitPopulation
            + " reversals=" + o.Reversals + " reversalAges=" + o.ReversalsAt1 + "/" + o.ReversalsAt2 + "/" + o.ReversalsAt3
            + " youngDaughterMigrations=" + o.YoungDaughterMigrations
            + " youngDaughterReversals=" + o.YoungDaughterReversals
            + " daughterParentFissions=" + o.DaughterParentFissions
            + " daughterParentFissionAges=" + o.DaughterParentFissionsAt1 + "/" + o.DaughterParentFissionsAt2
                + "/" + o.DaughterParentFissionsAt3 + "/" + o.DaughterParentFissionsOlder
            + " fissionCooldownBlocked=" + o.FissionCooldownBlocked
            + " viable=" + o.Viable + " target=" + o.Target
            + " FCH=" + F(o.MeanFood) + "/" + F(o.MeanClothing) + "/" + F(o.MeanHousing)
            + " localQoL=" + F(o.LocalWeightedQoL) + " lineageMeanQoL=" + F(o.MeanLineageQoL)
            + " fissionFunnel=" + o.FissionProposals + "/" + o.Fissions + "/" + o.FissionDropped
            + " fissionTriggers=" + o.FissionCrowding + "/" + o.FissionDistress
            + " immigrationFunnel=" + o.ImmigrationIntents + "/" + o.ImmigrationAccepted + "/"
            + o.ImmigrationPartial + "/" + o.ImmigrationRejected
            + " negativeNet=" + o.NegativeNetDaughters + "/" + o.NegativeNetDestinations
            + " higherQolWorseMortality=" + o.HigherQolWorseMortality
            + " kinMigrations=" + o.KinMigrations + " crossLineageMigrants=" + o.CrossLineageMigrants
            + " socialForecast/cacheBuild/hit/candidates=" + o.SocialForecasts + "/" + o.SocialCacheBuilds
                + "/" + o.SocialCacheHits + "/" + o.SocialCandidates
            + " socialRejectM/A/S/I=" + o.SocialMoverRejected + "/" + o.SocialAggregateRejected
                + "/" + o.SocialSourceRejected + "/" + o.SocialIncumbentRejected
            + " socialMs=" + o.SocialMilliseconds.ToString("F1", CultureInfo.InvariantCulture)
            + " worstConservation=" + o.WorstConservation.ToString("G6", CultureInfo.InvariantCulture)
            + " elapsedMs=" + o.Milliseconds.ToString("F1", CultureInfo.InvariantCulture));
    }

    private static void PrintReversal(Outcome outcome, Reversal reversal)
    {
        Flow a = reversal.First, b = reversal.Return;
        Snapshot a3 = a.Follow.Target3, b3 = b.Follow.Target3;
        Console.WriteLine("DEMOGRAPHY_SOCIAL_REVERSAL seed=" + outcome.Seed + " variant=" + outcome.Variant
            + " pair=" + a.Report.SourceCamp + ">" + a.Report.DestinationCamp + ">" + b.Report.DestinationCamp
            + " turns=" + a.Report.Turn + ">" + b.Report.Turn + " gap=" + (b.Report.Turn - a.Report.Turn)
            + " migrants=" + a.Report.Migrants + "/" + b.Report.Migrants
            + " utility=" + F(a.Report.Utility) + "/" + F(b.Report.Utility)
            + " penalty=" + F(a.Report.Penalty) + "/" + F(b.Report.Penalty)
            + " qolGain=" + F(a.Report.PostInflowQoL - a.Report.SourceQoL) + "/" + F(b.Report.PostInflowQoL - b.Report.SourceQoL)
            + " mortalityGap=" + F(a.Report.MortalityGap) + "/" + F(b.Report.MortalityGap)
            + " sourceFCH=" + Triple(b.Source) + " destinationFCH=" + Triple(b.Destination)
            + " sourceDeficit=" + F(b.Source.DeficitYears) + " destinationDeficit=" + F(b.Destination.DeficitYears)
            + " sourceNet=" + F(b.Source.NetRate) + " destinationNet=" + F(b.Destination.NetRate)
            + " sourceExpectation=" + F(b.Source.Expectation) + " destinationExpectation=" + F(b.Destination.Expectation)
            + " lineageMean=" + F(b.SourceLineageMean) + "/" + F(b.DestinationLineageMean)
            + " lineagePopulation=" + b.SourceLineagePopulation + "/" + b.DestinationLineagePopulation
            + " youngDaughter=" + reversal.InvolvesDaughter + " daughterAge=" + reversal.YoungestDaughterAge
            + " firstReceiver3=" + FollowText(a3) + " returnReceiver3=" + FollowText(b3));
    }

    private static void PrintAggregate(Aggregate a)
    {
        double camps = Math.Max(1, a.Survivors), people = Math.Max(1, a.EndPopulation);
        Console.WriteLine("DEMOGRAPHY_SOCIAL_TOTAL variant=" + a.Variant + " seeds=" + a.Seeds + " turns=" + a.Turns
            + " camps=" + a.StartCamps + "->" + a.EndCamps + " population=" + a.StartPopulation + "->" + a.EndPopulation
            + " births=" + a.Births + " deaths=" + a.Deaths + " migrations=" + a.Migrations + " migrants=" + a.Migrants
            + " penalizedMigrations=" + a.PenalizedMigrations
            + " fissions=" + a.Fissions + " splitPopulation=" + a.SplitPopulation + " reversals=" + a.Reversals
            + " reversalAges=" + a.ReversalsAt1 + "/" + a.ReversalsAt2 + "/" + a.ReversalsAt3
            + " reversalsPer100Migrations=" + F(100.0 * a.Reversals / Math.Max(1, a.Migrations))
            + " fissionsPer1000CampTurns=" + F(1000.0 * a.Fissions / Math.Max(1, a.CampTurns))
            + " migrantsPer1000PersonTurns=" + F(1000.0 * a.Migrants / Math.Max(1, a.PopulationTurns))
            + " youngDaughterMigrations=" + a.YoungDaughterMigrations + " youngDaughterReversals=" + a.YoungDaughterReversals
            + " daughterParentFissions=" + a.DaughterParentFissions
            + " daughterParentFissionAges=" + a.DaughterParentFissionsAt1 + "/" + a.DaughterParentFissionsAt2
                + "/" + a.DaughterParentFissionsAt3 + "/" + a.DaughterParentFissionsOlder
            + " fissionCooldownBlocked=" + a.FissionCooldownBlocked
            + " viable=" + a.Viable + " target=" + a.Target
            + " FCH=" + F(a.FoodWeighted / camps) + "/" + F(a.ClothingWeighted / camps) + "/" + F(a.HousingWeighted / camps)
            + " localQoL=" + F(a.LocalQoLWeighted / people) + " lineageMeanQoL=" + F(a.LineageQoLSum / Math.Max(1, a.Seeds))
            + " fissionFunnel=" + a.FissionProposals + "/" + a.Fissions + "/" + a.FissionDropped
            + " fissionTriggers=" + a.FissionCrowding + "/" + a.FissionDistress
            + " immigrationFunnel=" + a.ImmigrationIntents + "/" + a.ImmigrationAccepted + "/"
            + a.ImmigrationPartial + "/" + a.ImmigrationRejected
            + " negativeNet=" + a.NegativeNetDaughters + "/" + a.NegativeNetDestinations
            + " higherQolWorseMortality=" + a.HigherQolWorseMortality
            + " kinMigrations=" + a.KinMigrations + " crossLineageMigrants=" + a.CrossLineageMigrants
            + " socialForecast/cacheBuild/hit/candidates=" + a.SocialForecasts + "/" + a.SocialCacheBuilds
                + "/" + a.SocialCacheHits + "/" + a.SocialCandidates
            + " socialCacheHitRate=" + F(a.SocialCacheHits / (double)Math.Max(1, a.SocialCacheBuilds + a.SocialCacheHits))
            + " socialRejectM/A/S/I=" + a.SocialMoverRejected + "/" + a.SocialAggregateRejected
                + "/" + a.SocialSourceRejected + "/" + a.SocialIncumbentRejected
            + " socialMs=" + a.SocialMilliseconds.ToString("F1", CultureInfo.InvariantCulture)
            + " worstConservation=" + a.WorstConservation.ToString("G6", CultureInfo.InvariantCulture)
            + " elapsedMs=" + a.Milliseconds.ToString("F1", CultureInfo.InvariantCulture));
        PrintFollow(a.Variant, "daughter", a.DaughterFollow);
        PrintFollow(a.Variant, "fission-parent", a.DaughterParentFollow);
        PrintFollow(a.Variant, "receiver", a.ReceiverFollow);
    }

    private static void PrintFissionCooldown(int cooldown, Aggregate a)
    {
        double camps = Math.Max(1, a.Survivors), people = Math.Max(1, a.EndPopulation);
        FollowSummary daughter = a.DaughterFollow, parent = a.DaughterParentFollow;
        int daughter3 = Math.Max(1, daughter.ObservedAt3 - daughter.ExtinctAt3);
        int parent3 = Math.Max(1, parent.ObservedAt3 - parent.ExtinctAt3);
        Console.WriteLine("DEMOGRAPHY_FISSION_COOLDOWN cooldown=" + cooldown
            + " fissions=" + a.Fissions + " daughterParentFissions=" + a.DaughterParentFissions
            + " daughterParentFissionAges=" + a.DaughterParentFissionsAt1 + "/" + a.DaughterParentFissionsAt2
                + "/" + a.DaughterParentFissionsAt3 + "/" + a.DaughterParentFissionsOlder
            + " blockedCampTurns=" + a.FissionCooldownBlocked
            + " migrations=" + a.Migrations + " migrants=" + a.Migrants + " reversals=" + a.Reversals
            + " camps=" + a.StartCamps + "->" + a.EndCamps + " population=" + a.StartPopulation + "->" + a.EndPopulation
            + " viable=" + a.Viable + " target=" + a.Target
            + " FCH=" + F(a.FoodWeighted / camps) + "/" + F(a.ClothingWeighted / camps) + "/" + F(a.HousingWeighted / camps)
            + " localQoL=" + F(a.LocalQoLWeighted / people)
            + " daughterExtinct3=" + daughter.ExtinctAt3 + " parentExtinct3=" + parent.ExtinctAt3
            + " daughterDeltaFCH3=" + F(daughter.FoodDelta3 / daughter3) + "/" + F(daughter.ClothingDelta3 / daughter3) + "/" + F(daughter.HousingDelta3 / daughter3)
            + " parentDeltaFCH3=" + F(parent.FoodDelta3 / parent3) + "/" + F(parent.ClothingDelta3 / parent3) + "/" + F(parent.HousingDelta3 / parent3)
            + " daughterDeltaQoL3=" + F(daughter.QoLDelta3 / daughter3)
            + " parentDeltaQoL3=" + F(parent.QoLDelta3 / parent3)
            + " negativeNetDaughters=" + a.NegativeNetDaughters
            + " conservation=" + a.WorstConservation.ToString("G6", CultureInfo.InvariantCulture));
    }

    private static void PrintCalibration(Calibration calibration, Aggregate a)
    {
        FollowSummary receiver = a.ReceiverFollow;
        int observed1 = receiver.ObservedAt1 - receiver.ExtinctAt1;
        int observed3 = receiver.ObservedAt3 - receiver.ExtinctAt3;
        int n1 = Math.Max(1, observed1);
        int n3 = Math.Max(1, observed3);
        double camps = Math.Max(1, a.Survivors), people = Math.Max(1, a.EndPopulation);
        Console.WriteLine("DEMOGRAPHY_IMMIGRATION_SWEEP candidate=" + calibration.Name
            + " deadband=" + F(calibration.Deadband) + " maxFraction=" + F(calibration.MaxFraction)
            + " cooldown=" + calibration.ReturnCooldown + " returnPenalty=" + F(calibration.ReturnPenalty)
            + " migrations=" + a.Migrations + " migrants=" + a.Migrants
            + " penalizedMigrations=" + a.PenalizedMigrations
            + " rapidReversals=" + a.Reversals
            + " reversalAges=" + a.ReversalsAt1 + "/" + a.ReversalsAt2 + "/" + a.ReversalsAt3
            + " reversalsPer100=" + F(100.0 * a.Reversals / Math.Max(1, a.Migrations))
            + " fissions=" + a.Fissions + " camps=" + a.StartCamps + "->" + a.EndCamps
            + " population=" + a.StartPopulation + "->" + a.EndPopulation
            + " viable=" + a.Viable + " target=" + a.Target
            + " FCH=" + F(a.FoodWeighted / camps) + "/" + F(a.ClothingWeighted / camps) + "/" + F(a.HousingWeighted / camps)
            + " localQoL=" + F(a.LocalQoLWeighted / people)
            + " receiverFollowEvents=" + receiver.Events
            + " receiverObservedNonExtinct1/3=" + observed1 + "/" + observed3
            + " receiverExtinct1/3=" + receiver.ExtinctAt1 + "/" + receiver.ExtinctAt3
            + " receiverNegativeNet0/1/3=" + receiver.NegativeNet0 + "/" + receiver.NegativeNet1 + "/" + receiver.NegativeNet3
            + " receiverExpectedQoLError1=" + F(receiver.ExpectedQoLError1 / n1)
            + " receiverDeltaFCH1=" + F(receiver.FoodDelta1 / n1) + "/" + F(receiver.ClothingDelta1 / n1) + "/" + F(receiver.HousingDelta1 / n1)
            + " receiverDeltaDeficit1=" + F(receiver.DeficitDelta1 / n1)
            + " receiverDeltaFCH3=" + F(receiver.FoodDelta3 / n3) + "/" + F(receiver.ClothingDelta3 / n3) + "/" + F(receiver.HousingDelta3 / n3)
            + " receiverDeltaQoL3=" + F(receiver.QoLDelta3 / n3)
            + " receiverDeltaDeficit3=" + F(receiver.DeficitDelta3 / n3)
            + " conservation=" + a.WorstConservation.ToString("G6", CultureInfo.InvariantCulture));
    }

    private static void PrintFollow(string variant, string kind, FollowSummary f)
    {
        int n1 = Math.Max(1, f.ObservedAt1 - f.ExtinctAt1);
        int n = Math.Max(1, f.ObservedAt3 - f.ExtinctAt3);
        Console.WriteLine("DEMOGRAPHY_SOCIAL_FOLLOW variant=" + variant + " kind=" + kind
            + " events=" + f.Events + " observed1=" + f.ObservedAt1 + " extinct1=" + f.ExtinctAt1
            + " observed3=" + f.ObservedAt3 + " extinct3=" + f.ExtinctAt3
            + " healthy0=" + f.Healthy0 + " healthy3=" + f.Healthy3 + " recovered3=" + f.Recovered3
            + " negativeNet0=" + f.NegativeNet0 + " negativeNet1=" + f.NegativeNet1 + " negativeNet3=" + f.NegativeNet3
            + " expectedQoLError1=" + F(f.ExpectedQoLError1 / n1)
            + " deltaFCH1=" + F(f.FoodDelta1 / n1) + "/" + F(f.ClothingDelta1 / n1) + "/" + F(f.HousingDelta1 / n1)
            + " deltaDeficitYears1=" + F(f.DeficitDelta1 / n1)
            + " meanPopulationRatio3=" + F(f.PopulationRatio3 / n)
            + " deltaFCH3=" + F(f.FoodDelta3 / n) + "/" + F(f.ClothingDelta3 / n) + "/" + F(f.HousingDelta3 / n)
            + " deltaQoL3=" + F(f.QoLDelta3 / n) + " deltaDeficitYears3=" + F(f.DeficitDelta3 / n));
    }

    private static void PrintEffects(Dictionary<string, Aggregate> totals)
    {
        Aggregate none = totals["off-off"], fission = totals["fission-only"], immigration = totals["immigration-only"], both = totals["both"];
        double FissionRate(Aggregate a) => 1000.0 * a.Fissions / Math.Max(1, a.CampTurns);
        double MigrationRate(Aggregate a) => 1000.0 * a.Migrants / Math.Max(1, a.PopulationTurns);
        double ReversalRate(Aggregate a) => 100.0 * a.Reversals / Math.Max(1, a.Migrations);
        double fragmentationInteraction = (both.EndCamps - immigration.EndCamps) - (fission.EndCamps - none.EndCamps);
        string diagnosis;
        if (immigration.Reversals == 0 && both.Reversals > 0) diagnosis = "FISSION_IMMIGRATION_INTERACTION";
        else if (immigration.Reversals > 0 && ReversalRate(both) > ReversalRate(immigration) * 1.25) diagnosis = "IMMIGRATION_WITH_FISSION_AMPLIFICATION";
        else if (immigration.Reversals > 0) diagnosis = "IMMIGRATION_INTRINSIC";
        else diagnosis = "NO_RAPID_REVERSAL_SIGNAL";
        Console.WriteLine("DEMOGRAPHY_SOCIAL_EFFECT fissionRateOnly=" + F(FissionRate(fission))
            + " fissionRateBoth=" + F(FissionRate(both))
            + " migrantRateOnly=" + F(MigrationRate(immigration)) + " migrantRateBoth=" + F(MigrationRate(both))
            + " reversalRateOnly=" + F(ReversalRate(immigration)) + " reversalRateBoth=" + F(ReversalRate(both))
            + " fissionCampEffect=" + (fission.EndCamps - none.EndCamps)
            + " fissionCampEffectWithImmigration=" + (both.EndCamps - immigration.EndCamps)
            + " fragmentationInteraction=" + F(fragmentationInteraction)
            + " populationEffectsF/I/B=" + (fission.EndPopulation - none.EndPopulation) + "/"
            + (immigration.EndPopulation - none.EndPopulation) + "/" + (both.EndPopulation - none.EndPopulation)
            + " diagnosis=" + diagnosis);
        Console.WriteLine("DEMOGRAPHY_SOCIAL_GATE samePhysics=PASS identicalBoundary=PASS conservation=PASS"
            + " socialFlowAttribution=MEASURED immigrationReturnPenalty=0.10 nativePromotion=REVIEW");
    }

    private static string FollowText(Snapshot snapshot)
        => snapshot == null ? "NA" : !snapshot.Alive ? "EXTINCT" : snapshot.LinkedPopulation + ":" + Triple(snapshot) + ":" + F(snapshot.NetRate);
    private static string Triple(Snapshot snapshot)
        => snapshot == null || !snapshot.Alive ? "NA" : F(snapshot.Food) + "/" + F(snapshot.Clothing) + "/" + F(snapshot.Housing);
    private static string ProtectionBand(double clothing, double housing)
        => clothing >= .8 ? housing >= .8 ? "adequate" : "housing-gap"
            : housing >= .8 ? "clothing-gap" : "both-gaps";
    private static int PreTransferResidents(Camp camp, DemographicReport demographic)
        => checked(camp.Population + demographic.Emigrated - demographic.Immigrated + demographic.SplitOff);
    private static bool ImmigrationSourceEligible(SimulationConfig config, int residents)
        => residents >= config.ImmigrationSourceFloor + config.ImmigrationMinimumGroup
            && Immigration.MaxGroup(config, residents) >= config.ImmigrationMinimumGroup;
    private static void CheckSourceEligibility()
    {
        var config = SimulationConfig.Small(8);
        Check(!ImmigrationSourceEligible(config, 16), "a 16-resident camp cannot send the four-person minimum at the ten-percent cap");
        Check(!ImmigrationSourceEligible(config, 39), "a 39-resident camp remains below the four-person capped group");
        Check(ImmigrationSourceEligible(config, 40), "a 40-resident camp can send the four-person minimum");
        var camp = new Camp { Population = 39 };
        var linked = new DemographicReport { EndingPopulation = 139 };
        Check(PreTransferResidents(camp, linked) == 39 && !ImmigrationSourceEligible(config, PreTransferResidents(camp, linked)),
            "linked expedition population cannot make an ineligible resident camp an immigration source");
        camp.Population = 36;
        var transferred = new DemographicReport { Emigrated = 4, EndingPopulation = 90 };
        Check(PreTransferResidents(camp, transferred) == 40 && ImmigrationSourceEligible(config, PreTransferResidents(camp, transferred)),
            "post-Close reconstruction restores the exact pre-transfer source population");
    }
    private static string D(double value) => value.ToString("F6", CultureInfo.InvariantCulture);
    private static string F(double value) => value.ToString("F3", CultureInfo.InvariantCulture);
    private static int Percentile(IReadOnlyList<int> sorted, double fraction)
        => sorted.Count == 0 ? 0 : sorted[(int)Math.Round((sorted.Count - 1) * fraction)];
}
