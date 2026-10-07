using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Anthropocene.Simulation;

// Two separately dispatched final scenarios over the canonical five-world
// Standard/80 census that produced the historical Agriculture and Husbandry
// population anchors. Candidate scenarios share the same Fishing-enabled
// post-opening checkpoints; only FissionEnabled and ImmigrationEnabled differ.
internal static class FishingLongRunChecks
{
    private const int Turns = 50;
    private const double HistoricalAgricultureMedian = 480;
    private const double HistoricalHusbandryMedian = 383;
    private const double AllowedReduction = .20;
    private const double AgriculturePopulationFloor = HistoricalAgricultureMedian * (1 - AllowedReduction);
    private const double HusbandryPopulationFloor = HistoricalHusbandryMedian * (1 - AllowedReduction);
    private static readonly string[] Branches = { "Agriculture", "Husbandry", "Fishing" };
    private static readonly WorldSpec[] Census =
    {
        new WorldSpec("C1", 1, WorldShape.Continents),
        new WorldSpec("C2", 2, WorldShape.Continents),
        new WorldSpec("C3", 3, WorldShape.Continents),
        new WorldSpec("P1", 1, WorldShape.Pangaea),
        new WorldSpec("A1", 1, WorldShape.Archipelagos)
    };
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        IncludeFields = true,
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private static Checkpoint[] candidateCheckpoints;
    private static Checkpoint[] controlCheckpoints;

    private sealed class WorldSpec
    {
        public readonly string Name;
        public readonly int Seed;
        public readonly WorldShape Shape;

        public WorldSpec(string name, int seed, WorldShape shape)
        {
            Name = name;
            Seed = seed;
            Shape = shape;
        }
    }

    private sealed class Checkpoint
    {
        public WorldSpec Spec;
        public byte[] Bytes;
        public int OpeningFinishedTurn;
        public string Identity;
    }

    private sealed class Statistics
    {
        public double? Median, P25, P75, Minimum, Maximum;
    }

    private sealed class BranchSnapshot
    {
        public string Branch;
        public int Count;
        public Statistics ResidentPopulation, QoL;
    }

    private sealed class TurnSnapshot
    {
        public int Turn;
        public BranchSnapshot[] Branches;
    }

    private sealed class ScenarioConfiguration
    {
        public bool NeolithicFishingEnabled, CorrectedAquaticSeasonalPressureEnabled, TradeEnabled;
        public bool FissionEnabled, ImmigrationEnabled;
        public string EconomicAllocation;
    }

    private sealed class WorldEvidence
    {
        public string Name, Shape, CheckpointIdentity, FinalFingerprint;
        public int Seed, OpeningFinishedTurn, FirstMeasuredAbsoluteTurn, LastMeasuredAbsoluteTurn;
    }

    private sealed class PopulationGate
    {
        public string Basis;
        public double AgricultureFloor, HusbandryFloor;
    }

    private sealed class Artifact
    {
        public int Schema = 3, WorldCount, CompletedTurnsPerWorld;
        public string Scenario, Cohort, ExecutionMode, QuantileConvention;
        public ScenarioConfiguration Configuration;
        public PopulationGate Gate;
        public long FissionEvents, ImmigrationEvents;
        public long FisherPersonTurns, FisherySiteTurns;
        public double ResidentFisheryHarvest, ToolCoveredFisherYears, FisheryCapitalWorkerYears;
        public WorldEvidence[] Worlds, ReferenceWorlds;
        public TurnSnapshot[] Turns;
        public BranchSnapshot[] Turn50, ReferenceTurn50;
    }

    private sealed class RunResult
    {
        public Artifact Artifact;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("Fishing 50-turn system test: " + message);
    }

    private static string BranchName(Technology technology)
        => technology == Technology.Cultivation ? "Agriculture"
            : technology == Technology.AnimalHusbandry ? "Husbandry"
            : technology == Technology.Watercraft ? "Fishing" : null;

    // Deterministic type-7 linear interpolation: h=(n-1)p. This helper is
    // shared by both separately registered scenarios and does not depend on
    // framework/library percentile semantics.
    private static double Quantile(double[] sorted, double probability)
    {
        if (sorted.Length == 0) throw new ArgumentException("Quantile requires a non-empty sample.");
        double position = (sorted.Length - 1) * probability;
        int lower = (int)Math.Floor(position), upper = (int)Math.Ceiling(position);
        double fraction = position - lower;
        return sorted[lower] + (sorted[upper] - sorted[lower]) * fraction;
    }

    private static Statistics Summarize(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(value => value).ToArray();
        if (sorted.Length == 0) return null;
        return new Statistics
        {
            Median = Quantile(sorted, .5),
            P25 = Quantile(sorted, .25),
            P75 = Quantile(sorted, .75),
            Minimum = sorted[0],
            Maximum = sorted[sorted.Length - 1]
        };
    }

    private static string Format(double? value)
        => value?.ToString("R", CultureInfo.InvariantCulture) ?? "null";

    private static string FormatStatistics(string name, Statistics stats)
        => name + "Min=" + Format(stats?.Minimum) + " " + name + "P25=" + Format(stats?.P25) +
            " " + name + "Median=" + Format(stats?.Median) + " " + name + "P75=" + Format(stats?.P75) +
            " " + name + "Max=" + Format(stats?.Maximum);

    private static BranchSnapshot Snapshot(string branch, IReadOnlyList<Camp> camps)
    {
        return new BranchSnapshot
        {
            Branch = branch,
            Count = camps.Count,
            ResidentPopulation = Summarize(camps.Select(camp => (double)camp.Population)),
            QoL = Summarize(camps.Select(camp => camp.QoL))
        };
    }

    private static BranchSnapshot[] BranchesAtTurn(IReadOnlyList<WorldState> worlds)
    {
        var byBranch = Branches.ToDictionary(branch => branch, _ => new List<Camp>());
        foreach (var world in worlds)
        {
            var societies = world.Societies.ToDictionary(society => society.Id);
            foreach (var camp in world.Camps.Where(camp => camp.Population > 0).OrderBy(camp => camp.Id))
            {
                if (!societies.TryGetValue(camp.SocietyId, out var society)) continue;
                string branch = BranchName(society.Technology);
                if (branch != null) byBranch[branch].Add(camp);
            }
        }
        return Branches.Select(branch => Snapshot(branch, byBranch[branch])).ToArray();
    }

    private static void ValidateSnapshot(TurnSnapshot snapshot)
    {
        Check(snapshot.Branches.Length == Branches.Length && snapshot.Branches.Select(b => b.Branch).SequenceEqual(Branches),
            "every turn reports each first-branch cohort in canonical order");
        foreach (var branch in snapshot.Branches)
        {
            if (branch.Count == 0)
            {
                Check(branch.ResidentPopulation == null && branch.QoL == null,
                    "empty branch has absent statistics rather than fabricated zero values");
                continue;
            }
            Check(branch.ResidentPopulation != null && branch.QoL != null,
                "non-empty branch has population and QoL statistics");
            ValidateStatistics(branch.ResidentPopulation, branch.Branch + " population");
            ValidateStatistics(branch.QoL, branch.Branch + " QoL");
            Check(branch.ResidentPopulation.Minimum >= 0, branch.Branch + " resident population is nonnegative");
            Check(branch.QoL.Minimum >= 0 && branch.QoL.Maximum <= 1 + 1e-10,
                branch.Branch + " QoL stays within authoritative bounds");
        }
    }

    private static void ValidateStatistics(Statistics stats, string label)
    {
        var values = new[] { stats.Minimum.Value, stats.P25.Value, stats.Median.Value, stats.P75.Value, stats.Maximum.Value };
        Check(values.All(double.IsFinite), label + " statistics are finite");
        Check(values[0] <= values[1] && values[1] <= values[2] && values[2] <= values[3] && values[3] <= values[4],
            label + " statistics are ordered min <= p25 <= median <= p75 <= max");
    }

    private static ScenarioConfiguration ConfigView(SimulationConfig config) => new ScenarioConfiguration
    {
        NeolithicFishingEnabled = config.NeolithicFishingEnabled,
        CorrectedAquaticSeasonalPressureEnabled = config.CorrectedAquaticSeasonalPressureEnabled,
        TradeEnabled = config.TradeEnabled,
        FissionEnabled = config.FissionEnabled,
        ImmigrationEnabled = config.ImmigrationEnabled,
        EconomicAllocation = config.EconomicAllocation
    };

    private static Checkpoint[] CreateCheckpoints(bool fishingEnabled, bool correctedEcologyEnabled)
    {
        var checkpoints = new List<Checkpoint>(Census.Length);
        foreach (var spec in Census)
        {
            var config = new SimulationConfig { Players = 80, Shape = spec.Shape };
            config.NeolithicFishingEnabled = fishingEnabled;
            config.CorrectedAquaticSeasonalPressureEnabled = correctedEcologyEnabled;
            config.EconomicAllocation = EconomicAllocationStrategy.DemandControl;
            config.TradeEnabled = true;
            // Every measured scenario starts from an opening completed without
            // population movement; the scenario switches apply only afterward.
            config.FissionEnabled = false;
            config.ImmigrationEnabled = false;
            config.Validate();
            var world = new WorldGenerator().Generate(spec.Seed, config);
            var resolver = new TurnResolver(new PhaseExecution(1));
            while (!world.Finished && world.Turn <= 1000)
            {
                var orders = AiPlanning.Decide(world, new PhaseExecution(1)).Orders;
                var record = resolver.Resolve(world, orders);
                Check(!record.Events.Any(message => message.StartsWith("Rejected", StringComparison.Ordinal)),
                    spec.Name + " opening checkpoint rejected an internal order/plan at turn " + record.Turn);
            }
            Check(world.Finished, spec.Name + " checkpoint completes the opening before the measured horizon");
            checkpoints.Add(new Checkpoint
            {
                Spec = spec,
                Bytes = SaveCodec.Save(world),
                OpeningFinishedTurn = Math.Max(1, world.Turn - 1),
                Identity = SaveCodec.Fingerprint(world)
            });
        }
        return checkpoints.ToArray();
    }

    private static Checkpoint[] CandidateCheckpoints()
        => candidateCheckpoints ??= CreateCheckpoints(true, true);

    private static Checkpoint[] ControlCheckpoints()
        => controlCheckpoints ??= CreateCheckpoints(false, false);

    private static RunResult RunOnce(Checkpoint[] checkpoints, string scenario, bool fishingEnabled,
        bool correctedEcologyEnabled, bool fission, bool immigration)
    {
        var worlds = checkpoints.Select(checkpoint => SaveCodec.Load(checkpoint.Bytes)).ToArray();
        var startTurns = worlds.Select(world => world.Turn).ToArray();
        var resolvers = worlds.Select(_ => new TurnResolver(new PhaseExecution(1))).ToArray();
        foreach (var world in worlds)
        {
            Check(world.Finished, scenario + " starts from a post-opening checkpoint");
            world.Config.NeolithicFishingEnabled = fishingEnabled;
            world.Config.CorrectedAquaticSeasonalPressureEnabled = correctedEcologyEnabled;
            world.Config.EconomicAllocation = EconomicAllocationStrategy.DemandControl;
            world.Config.TradeEnabled = true;
            world.Config.FissionEnabled = fission;
            world.Config.ImmigrationEnabled = immigration;
            world.Config.Validate();
        }

        var builder = new AgentObservationBuilder();
        var turnSnapshots = new List<TurnSnapshot>(Turns);
        long fissions = 0, migrations = 0, fisherPersonTurns = 0, fisherySiteTurns = 0;
        double residentFisheryHarvest = 0, toolCoveredFisherYears = 0, fisheryCapitalWorkerYears = 0;
        for (int turnIndex = 0; turnIndex < Turns; turnIndex++)
        {
            for (int worldIndex = 0; worldIndex < worlds.Length; worldIndex++)
            {
                var world = worlds[worldIndex];
                var orders = AiPlanning.Decide(world, new PhaseExecution(1)).Orders;
                foreach (var playerOrders in orders.OrderBy(pair => pair.Key))
                {
                    var observation = builder.Build(world, playerOrders.Key);
                    Check(playerOrders.Value.All(order => OrderRules.Validate(observation, order) == null),
                        scenario + " AI produced a rejected order at measured turn " + (turnIndex + 1) +
                        ", world " + checkpoints[worldIndex].Spec.Name + ", player " + playerOrders.Key);
                }

                var prepared = TurnPlanner.Prepare(world, orders, null, new PhaseExecution(1));
                foreach (var plan in prepared.FishingPlans.Values)
                {
                    fisherPersonTurns += plan.FisherCount;
                    toolCoveredFisherYears += plan.Grounds.Sum(ground =>
                        Math.Max(0, ground.ToolCoveredHarvestWorkerYears));
                    fisheryCapitalWorkerYears += plan.Grounds.Sum(ground =>
                        Math.Max(0, ground.CapitalWorkerYears));
                }
                var record = resolvers[worldIndex].Resolve(world, prepared);
                Check(record.Turn == startTurns[worldIndex] + turnIndex && world.Turn == startTurns[worldIndex] + turnIndex + 1,
                    scenario + " committed exactly one turn boundary at index " + turnIndex +
                    " in world " + checkpoints[worldIndex].Spec.Name);
                Check(!record.Events.Any(message => message.StartsWith("Rejected", StringComparison.Ordinal)),
                    scenario + " rejected an internal order/plan at measured turn " + (turnIndex + 1) +
                    " in world " + checkpoints[worldIndex].Spec.Name);
                fissions += record.Fissions?.Count ?? 0;
                migrations += record.Migrations?.Count ?? 0;
                residentFisheryHarvest += record.Transfers.Where(transfer =>
                    transfer.Kind == "FisheryHarvest").Sum(transfer => Math.Max(0, transfer.Food));
                fisherySiteTurns += world.FisherySites?.Count ?? 0;
            }

            var snapshot = new TurnSnapshot { Turn = turnIndex + 1, Branches = BranchesAtTurn(worlds) };
            ValidateSnapshot(snapshot);
            turnSnapshots.Add(snapshot);
        }

        Check(turnSnapshots.Count == Turns && worlds.All(world => world.History.Count >= Turns),
            scenario + " completed all 50 committed turns in every census world");
        // One diagnostic whole-world audit after each completed run exercises
        // every lazy endpoint; regular per-turn invariants stay on touched tiles.
        foreach (var world in worlds) TurnResolver.Invariants(world);

        var evidence = checkpoints.Select((checkpoint, index) => new WorldEvidence
        {
            Name = checkpoint.Spec.Name,
            Seed = checkpoint.Spec.Seed,
            Shape = checkpoint.Spec.Shape.ToString(),
            OpeningFinishedTurn = checkpoint.OpeningFinishedTurn,
            FirstMeasuredAbsoluteTurn = startTurns[index],
            LastMeasuredAbsoluteTurn = worlds[index].Turn - 1,
            CheckpointIdentity = checkpoint.Identity,
            FinalFingerprint = SaveCodec.Fingerprint(worlds[index])
        }).ToArray();
        return new RunResult
        {
            Artifact = new Artifact
            {
                WorldCount = worlds.Length,
                CompletedTurnsPerWorld = turnSnapshots.Count,
                Scenario = scenario,
                Cohort = "Standard/80 pooled census: C1,C2,C3,P1,A1",
                ExecutionMode = "serial; PhaseExecution workers=1",
                QuantileConvention = "type-7 linear interpolation; h=(n-1)p",
                Configuration = ConfigView(worlds[0].Config),
                FissionEvents = fissions,
                ImmigrationEvents = migrations,
                FisherPersonTurns = fisherPersonTurns,
                FisherySiteTurns = fisherySiteTurns,
                ResidentFisheryHarvest = residentFisheryHarvest,
                ToolCoveredFisherYears = toolCoveredFisherYears,
                FisheryCapitalWorkerYears = fisheryCapitalWorkerYears,
                Worlds = evidence,
                Turns = turnSnapshots.ToArray(),
                Turn50 = turnSnapshots[Turns - 1].Branches
            }
        };
    }

    private static string Serialize(Artifact artifact) => JsonSerializer.Serialize(artifact, JsonOptions);

    private static BranchSnapshot Branch(BranchSnapshot[] branches, string name)
        => branches.Single(branch => branch.Branch == name);

    private static void Run(bool fission, bool immigration, string scenario, string artifactName)
    {
        RunResult reference = null;
        double agricultureFloor = AgriculturePopulationFloor;
        double husbandryFloor = HusbandryPopulationFloor;
        string gateBasis = "historical all-features medians minus 20%: Agriculture 480, Husbandry 383";
        if (!fission && !immigration)
        {
            reference = RunOnce(ControlCheckpoints(), scenario + "-feature-off-reference", false, false, false, false);
            agricultureFloor = Branch(reference.Artifact.Turn50, "Agriculture").ResidentPopulation.Median.Value * (1 - AllowedReduction);
            husbandryFloor = Branch(reference.Artifact.Turn50, "Husbandry").ResidentPopulation.Median.Value * (1 - AllowedReduction);
            gateBasis = "matched feature-off no-movement census medians minus 20%; historical 480/383 anchors require the all-features cohort";
        }

        var first = RunOnce(CandidateCheckpoints(), scenario, true, true, fission, immigration);
        var replay = RunOnce(CandidateCheckpoints(), scenario, true, true, fission, immigration);
        foreach (var result in new[] { first, replay })
        {
            result.Artifact.Gate = new PopulationGate
            {
                Basis = gateBasis,
                AgricultureFloor = agricultureFloor,
                HusbandryFloor = husbandryFloor
            };
            result.Artifact.ReferenceWorlds = reference?.Artifact.Worlds;
            result.Artifact.ReferenceTurn50 = reference?.Artifact.Turn50;
        }

        string firstJson = Serialize(first.Artifact);
        string replayJson = Serialize(replay.Artifact);
        Check(firstJson == replayJson,
            scenario + " per-turn and turn-50 artifacts reproduce exactly from the same census checkpoints and execution mode");
        Check(first.Artifact.Worlds.Select(world => world.CheckpointIdentity).SequenceEqual(
                replay.Artifact.Worlds.Select(world => world.CheckpointIdentity)) &&
            first.Artifact.Worlds.Select(world => world.FinalFingerprint).SequenceEqual(
                replay.Artifact.Worlds.Select(world => world.FinalFingerprint)),
            scenario + " checkpoint and final fingerprints reproduce exactly in every census world");

        string directory = Path.Combine("TestResults", "Fishing50");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, artifactName);
        File.WriteAllText(path, firstJson + Environment.NewLine);
        Console.WriteLine("FISHING_50 scenario=" + scenario + " worlds=" + first.Artifact.WorldCount +
            " turnsPerWorld=" + first.Artifact.CompletedTurnsPerWorld + " fissions=" + first.Artifact.FissionEvents +
            " migrations=" + first.Artifact.ImmigrationEvents + " fisherPersonTurns=" + first.Artifact.FisherPersonTurns +
            " harvest=" + first.Artifact.ResidentFisheryHarvest.ToString("R", CultureInfo.InvariantCulture) +
            " toolCoveredYears=" + first.Artifact.ToolCoveredFisherYears.ToString("R", CultureInfo.InvariantCulture) +
            " capitalWorkerYears=" + first.Artifact.FisheryCapitalWorkerYears.ToString("R", CultureInfo.InvariantCulture) +
            " siteTurns=" + first.Artifact.FisherySiteTurns + " artifact=" + path);
        if (reference != null)
        {
            foreach (var branch in reference.Artifact.Turn50)
                Console.WriteLine("FISHING_50_REFERENCE scenario=" + scenario + " branch=" + branch.Branch + " count=" + branch.Count +
                    " " + FormatStatistics("population", branch.ResidentPopulation) +
                    " " + FormatStatistics("qol", branch.QoL));
        }
        foreach (var branch in first.Artifact.Turn50)
            Console.WriteLine("FISHING_50_TURN50 scenario=" + scenario + " branch=" + branch.Branch + " count=" + branch.Count +
                " " + FormatStatistics("population", branch.ResidentPopulation) +
                " " + FormatStatistics("qol", branch.QoL));

        // Emit evidence before evaluating population/fission acceptance so a
        // failed hard gate still leaves the exact reproducible distributions.
        var agriculture = Branch(first.Artifact.Turn50, "Agriculture");
        var husbandry = Branch(first.Artifact.Turn50, "Husbandry");
        Check(agriculture.Count > 0 && agriculture.ResidentPopulation?.Median >= agricultureFloor,
            scenario + " turn-50 Agriculture median meets floor " + agricultureFloor.ToString("R", CultureInfo.InvariantCulture));
        Check(husbandry.Count > 0 && husbandry.ResidentPopulation?.Median >= husbandryFloor,
            scenario + " turn-50 Husbandry median meets floor " + husbandryFloor.ToString("R", CultureInfo.InvariantCulture));
        Check(first.Artifact.FisherPersonTurns > 0 && first.Artifact.ResidentFisheryHarvest > 0,
            scenario + " exercises resident Fisher staffing and realized FisheryHarvest receipts");
        Check(first.Artifact.ToolCoveredFisherYears > 0,
            scenario + " exercises finite opening Fishing Tool service");
        Check(first.Artifact.FisheryCapitalWorkerYears > 0 && first.Artifact.FisherySiteTurns > 0,
            scenario + " exercises fishery capital work and committed sparse sites");
        if (fission)
        {
            Check(first.Artifact.FissionEvents > 0, scenario + " records actual fission activity with the feature enabled");
            Check(first.Artifact.ImmigrationEvents > 0, scenario + " records actual immigration activity with the feature enabled");
        }
    }

    public static void RunNoMovement()
        => Run(false, false, "trade-no-population-movement", "no-population-movement.json");

    public static void RunAllFeatures()
        => Run(true, true, "trade-fission-immigration", "all-social-features.json");
}
