using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Anthropocene.Simulation;

// Experimental q_k stress harness. Physics, demographics, fission, reward,
// and Elder compilation all remain production code; this file constructs
// matched fixtures, schedules counterfactual configuration changes, and emits
// measurements. It deliberately contains no substitute production model.
internal static class TechnologyStress
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
    {
        IncludeFields = true,
        WriteIndented = true
    };
    private static readonly int[] DefaultSeeds = { 61169, 74843, 91781 };
    private static readonly double[] OpeningQ = { .38, .50, .65 };
    private static readonly string[] Scopes =
        { "cultivation", "fishing", "shared" };
    private const int Population = 320;
    private const double ReserveTurns = 2;
    private const double DefaultKnee = .4;

    private sealed class Options
    {
        public string Repo, Output;
        public int Turns = 80, Width = 128, Height = 64;
        public int DevelopmentTurns = 10;
        public int Jobs = Math.Min(4, System.Environment.ProcessorCount);
        public double DevelopmentBudget = .60;
        public int[] Seeds = DefaultSeeds;
        public int[] UnlockYears = { 400, 800 };
        public bool Quick, Verify;
    }

    private sealed class Site
    {
        public int Tile;
        public double LandRenewal, AquaticRenewal, JointRank;
    }

    private sealed class Bank
    {
        public int Seed;
        public byte[] Snapshot;
        public Site Site;
    }

    private sealed class State
    {
        public string Id, Fingerprint, ConfigurationHash;
        public int Seed, CampId, Tile;
        public double OpeningQ, InitialFieldCapital, InitialFishingCapital;
        public int[] FieldTiles = Array.Empty<int>();
        public int[] FishingTiles = Array.Empty<int>();
        public byte[] Snapshot;
    }

    private sealed class Scenario
    {
        public string Id, Mode, Scope;
        public double TargetKnee;
        public int UnlockYear;
        public bool Scheduled => UnlockYear >= 0;
    }

    private sealed class Arm
    {
        public string Id, Knowledge;
        public int StartTurn;
        public bool Invest;
    }

    private sealed class Row
    {
        public string StateId, ScenarioId, Mode, Scope, Arm, Knowledge,
            ObservationHash, InitialFingerprint, FinalFingerprint;
        public int Seed, Turn, PriorPopulation, EndingPopulation,
            Farmers, Fishers, Gatherers, Fissions, DaughterPopulation,
            CampCount;
        public bool TechnologyTransition;
        public double OpeningQ, TargetKnee, UnlockYear, StartYear, EndYear,
            ElapsedYears, FieldKnee, FishingKnee, RequestedDevelopmentBudget,
            AcceptedDevelopmentBudget, RealizedDevelopmentBudget,
            AcceptedDevelopmentPeople, RealizedFieldDevelopmentPeople,
            QoL, WelfareRate, RawWelfare, FoodProduced, FieldFood, FishFood,
            GatheredFood, FoodReserves, FieldQMean, FieldQMax, FieldCapital,
            FishingQMean, FishingCapital, FieldBuildWorkerYears,
            FieldMaintenanceWorkerYears, FishingBuildWorkerYears,
            FishingMaintenanceWorkerYears, LandFood, LandBiomass,
            LandCondition, AquaticFood, AquaticBiomass, AquaticCondition;
    }

    private sealed class RunResult
    {
        public State State;
        public Scenario Scenario;
        public Arm Arm;
        public string FinalFingerprint;
        public int ObservationLeakChecks, TransitionCapitalChecks;
        public List<Row> Rows = new List<Row>();
    }

    private sealed class Payoff
    {
        public string StateId, ScenarioId, Mode, Scope, Arm, Knowledge,
            Horizon, SignClassification;
        public int Seed, UnlockYear;
        public double OpeningQ, TargetKnee, Value, UndiscountedValue,
            InvestmentStartYear, DurableBreakEvenYears,
            DurablePositiveYears, MinimumCumulativeValue,
            MinimumCumulativeYear, IncrementalFieldBuildWorkerYears,
            IncrementalFieldMaintenanceWorkerYears,
            IncrementalFishingBuildWorkerYears,
            IncrementalFishingMaintenanceWorkerYears,
            FinalDeltaFieldQ, FinalDeltaFishingQ,
            FinalDeltaFieldCapital, FinalDeltaFishingCapital,
            MinimumDeltaLandCondition, MinimumDeltaAquaticCondition,
            FinalDeltaLandBiomass, FinalDeltaAquaticBiomass,
            IncrementalFissions, IncrementalDaughterPopulation;
        public bool LongRunImproves, CorrectSign;
    }

    private sealed class Timing
    {
        public string StateId, ScenarioId, Scope, LongRunChoice,
            H400Choice, H800Choice;
        public int Seed, UnlockYear;
        public double OpeningQ, TargetKnee, NowUndiscounted,
            AfterUndiscounted, NowH400, AfterH400, NowH800, AfterH800,
            H400LongRunRegret, H800LongRunRegret;
        public bool H400Inversion, H800Inversion, H800StrictlyLowerRegret;
    }

    public static int Run(string[] args)
    {
        try
        {
            CultureInfo.CurrentCulture = Inv;
            CultureInfo.CurrentUICulture = Inv;
            var options = Parse(args);
            Directory.CreateDirectory(options.Output);
            ResolutionMetrics.CaptureCraftWorkDiagnostics = true;
            var timer = Stopwatch.StartNew();
            var config = LoadConfig(options);
            VerifyCurveInvariants(config);
            var resources = LoadResources(options);

            Console.WriteLine("Technology stress: generating " +
                options.Seeds.Length + " disclosed worlds");
            var banks = options.Seeds.Select(seed => BuildBank(seed, config,
                resources)).ToArray();
            var states = BuildStates(banks).ToArray();
            var scenarios = BuildScenarios(options).ToArray();
            WriteStates(Path.Combine(options.Output, "technology_states.csv"),
                states);
            WriteScenarios(Path.Combine(options.Output,
                "technology_scenarios.csv"), scenarios, options);

            var runs = new List<RunResult>();
            foreach (var state in states)
            {
                var work = scenarios.SelectMany(scenario =>
                    Arms(scenario, options).Select(arm =>
                        (Scenario: scenario, Arm: arm))).ToArray();
                var completed = new RunResult[work.Length];
                Parallel.For(0, work.Length, new ParallelOptions
                {
                    MaxDegreeOfParallelism = options.Jobs
                }, index =>
                {
                    completed[index] = RunOne(state, work[index].Scenario,
                        work[index].Arm, options, new PhaseExecution());
                });
                runs.AddRange(completed);
                Console.WriteLine("Completed " + state.Id + " (" +
                    runs.Count + " trajectories)");
            }

            var rows = runs.SelectMany(run => run.Rows).ToArray();
            WriteRows(Path.Combine(options.Output,
                "technology_trajectories.csv"), rows);
            var payoffs = AnalyzePayoffs(states, scenarios, runs).ToArray();
            WritePayoffs(Path.Combine(options.Output,
                "technology_payoffs.csv"), payoffs);
            var timing = AnalyzeTiming(states, scenarios, payoffs).ToArray();
            WriteTiming(Path.Combine(options.Output,
                "technology_timing.csv"), timing);

            string verification = VerifyRuns(options, states, scenarios, runs);
            File.WriteAllText(Path.Combine(options.Output, "verification.txt"),
                verification + Environment.NewLine);
            WriteManifest(options, config, states, scenarios, runs, rows,
                payoffs, timing, timer.Elapsed, verification);
            Console.WriteLine("DONE trajectories=" + runs.Count +
                " turns=" + rows.Length + " seconds=" +
                timer.Elapsed.TotalSeconds.ToString("F1", Inv));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static Options Parse(string[] args)
    {
        string Value(string name, string fallback)
        {
            int index = Array.IndexOf(args, name);
            if (index < 0) return fallback;
            if (index + 1 >= args.Length)
                throw new ArgumentException("Missing value for " + name);
            return args[index + 1];
        }
        int[] Integers(string text) => text.Split(',').Select(value =>
            int.Parse(value, Inv)).ToArray();
        var options = new Options
        {
            Repo = Path.GetFullPath(Value("--repo", ".")),
            Output = Path.GetFullPath(Value("--out",
                "TestResults/InvestmentPaybackTechnologyStress")),
            Turns = int.Parse(Value("--turns", "80"), Inv),
            Width = int.Parse(Value("--width", "128"), Inv),
            Height = int.Parse(Value("--height", "64"), Inv),
            DevelopmentTurns = int.Parse(Value("--development-turns", "10"), Inv),
            Jobs = int.Parse(Value("--jobs", Math.Min(4,
                System.Environment.ProcessorCount).ToString(Inv)), Inv),
            DevelopmentBudget = double.Parse(Value("--development-budget", ".60"), Inv),
            Seeds = Integers(Value("--seeds", string.Join(",", DefaultSeeds))),
            UnlockYears = Integers(Value("--unlock-years", "400,800")),
            Quick = args.Contains("--quick"),
            Verify = args.Contains("--verify")
        };
        if (options.Turns < 1 || options.Width < 32 || options.Height < 16 ||
            options.DevelopmentTurns < 1 || options.DevelopmentBudget <= 0 ||
            options.DevelopmentBudget > 1 || options.Jobs < 1 ||
            options.Jobs > 16 || options.Seeds.Length < 1 ||
            options.UnlockYears.Any(year => year <= 0 || year % 20 != 0))
            throw new ArgumentException("Invalid bounded technology sweep options.");
        if (options.Quick)
        {
            options.Seeds = options.Seeds.Take(1).ToArray();
            options.Turns = Math.Min(options.Turns, 6);
            options.DevelopmentTurns = Math.Min(options.DevelopmentTurns, 2);
            options.UnlockYears = new[] { 40 };
        }
        double years = options.Turns * 20;
        if (options.UnlockYears.Any(year => year >= years))
            throw new ArgumentException("Every unlock must occur inside the run horizon.");
        return options;
    }

    private static IEnumerable<Scenario> BuildScenarios(Options options)
    {
        yield return new Scenario
        {
            Id = "static-qk040-shared", Mode = "static",
            Scope = "shared", TargetKnee = .4, UnlockYear = -1
        };
        foreach (double knee in new[] { .5, .8 })
            foreach (string scope in Scopes)
                yield return new Scenario
                {
                    Id = "static-qk" + QTag(knee) + "-" + scope,
                    Mode = "static", Scope = scope,
                    TargetKnee = knee, UnlockYear = -1
                };
        foreach (int year in options.UnlockYears)
            foreach (double knee in new[] { .5, .8 })
                foreach (string scope in Scopes)
                    yield return new Scenario
                    {
                        Id = "unlock-y" + year + "-qk" + QTag(knee) +
                            "-" + scope,
                        Mode = "scheduled", Scope = scope,
                        TargetKnee = knee, UnlockYear = year
                    };
    }

    private static IEnumerable<Arm> Arms(Scenario scenario, Options options)
    {
        yield return new Arm
        {
            Id = "no-development", Knowledge = "none", Invest = false,
            StartTurn = -1
        };
        yield return new Arm
        {
            Id = "invest-now", Knowledge = "unannounced-no-foresight",
            Invest = true, StartTurn = 0
        };
        if (!scenario.Scheduled) yield break;
        int start = scenario.UnlockYear / 20;
        yield return new Arm
        {
            Id = "invest-after-known", Knowledge = "known-from-opening",
            Invest = true, StartTurn = start
        };
        yield return new Arm
        {
            Id = "invest-after-unannounced",
            Knowledge = "revealed-at-unlock", Invest = true,
            StartTurn = start
        };
    }

    private static string QTag(double value) =>
        ((int)Math.Round(value * 100)).ToString("000", Inv);

    private static SimulationConfig LoadConfig(Options options)
    {
        string path = Path.Combine(options.Repo,
            "Assets/_Anthropocene/Resources/OpeningConfig.json");
        var config = JsonSerializer.Deserialize<SimulationConfig>(
            File.ReadAllText(path), Json) ?? throw new InvalidOperationException(
                "OpeningConfig could not be read.");
        config.Width = options.Width;
        config.Height = options.Height;
        config.Players = 1;
        config.Neolithic.CultivationMarginalEffortKneeQ = DefaultKnee;
        config.Neolithic.FishingMarginalEffortKneeQ = DefaultKnee;
        config.Neolithic.ExperimentalCultivationEffortKneeOverride = true;
        config.Neolithic.ExperimentalFishingEffortKneeOverride = true;
        config.Validate();
        return config;
    }

    private static ResourceGenerationOptions LoadResources(Options options)
    {
        string path = Path.Combine(options.Repo,
            "Assets/_Anthropocene/Resources/ResourceGenerationConfig.json");
        return JsonSerializer.Deserialize<ResourceGenerationOptions>(
            File.ReadAllText(path), Json) ?? throw new InvalidOperationException(
                "ResourceGenerationConfig could not be read.");
    }

    private static SimulationConfig CloneConfig(SimulationConfig source)
        => JsonSerializer.Deserialize<SimulationConfig>(
            JsonSerializer.Serialize(source, Json), Json);

    private static void VerifyCurveInvariants(SimulationConfig config)
    {
        double reference = config.ReferenceIntensity;
        double Capital(double q) => Math.Pow(q / reference, 2);
        config.Neolithic.CultivationMarginalEffortMultiplierPerTenth = 16;
        foreach (double knee in new[] { .4, .5, .8 })
        {
            config.Neolithic.CultivationMarginalEffortKneeQ = knee;
            double join = Capital(knee);
            Near(NeolithicCapital.EffortForCapital(config, join, knee), join,
                1e-12, "effort continuity at q_k=" + knee);
            double h = 1e-7;
            double left = (join - NeolithicCapital.EffortForCapital(config,
                join - h, knee)) / h;
            double right = (NeolithicCapital.EffortForCapital(config,
                join + h, knee) - join) / h;
            Near(left, 1, 2e-5, "left derivative at q_k=" + knee);
            Near(right, 1, 2e-5, "right derivative at q_k=" + knee);
            foreach (double q in new[] { .1, .4, .5, .65, .8, 1.0 })
            {
                double capital = Capital(q);
                double roundTrip = NeolithicCapital.CapitalForEffort(config,
                    NeolithicCapital.EffortForCapital(config, capital, knee),
                    knee);
                Near(roundTrip, capital, 1e-7,
                    "effort inverse at q_k=" + knee + " q=" + q);
            }
            Near(NeolithicCapital.EffortForCapital(config, Capital(.4), knee),
                1, 1e-12, "q=.4 legacy result at q_k=" + knee);
        }
        config.Neolithic.CultivationMarginalEffortKneeQ = DefaultKnee;
        config.Neolithic.FishingMarginalEffortKneeQ = DefaultKnee;
    }

    private static void Near(double actual, double expected, double tolerance,
        string message)
    {
        if (Math.Abs(actual - expected) > tolerance)
            throw new InvalidOperationException(message + " actual=" +
                D(actual) + " expected=" + D(expected));
    }

    private static Bank BuildBank(int seed, SimulationConfig baseConfig,
        ResourceGenerationOptions resources)
    {
        var world = new WorldGenerator().Generate(seed,
            CloneConfig(baseConfig), resources);
        var site = SelectSite(world);
        world.Camps.Clear();
        world.Societies.Clear();
        world.Hunters.Clear();
        world.Herds.Clear();
        world.Improvements.Clear();
        world.FixedHousing.Clear();
        world.FisherySites.Clear();
        world.PlayerCount = 1;
        world.Config.Players = 1;
        return new Bank
        {
            Seed = seed,
            Site = site,
            Snapshot = SaveCodec.Save(world)
        };
    }

    private static Site SelectSite(WorldState world)
    {
        bool Water(int id) => world.Tiles[id].Environment ==
            EnvironmentKind.Shallows || world.Tiles[id].Environment ==
            EnvironmentKind.Lake;
        var candidates = new List<Site>();
        foreach (var tile in world.Tiles.Where(tile => tile.TraversableLand &&
                     tile.Environment != EnvironmentKind.Ice))
        {
            int[] land = world.Map.Radius(tile.Id, 1).Where(id =>
                CultivationEconomy.Cultivable(world.Tiles[id])).ToArray();
            int[] water = world.Map.Radius(tile.Id, world.Config.VisionRadius)
                .Where(Water).ToArray();
            if (land.Length < 4 || water.Length < 2 ||
                !world.Map.Neighbors(tile.Id).Any(Water) ||
                FreshwaterAccess.Available(world, tile.Id) <= 0) continue;
            double landRenewal = land.Sum(id => world.Tiles[id].Seasons
                .Average(season => Math.Max(0, season.MaxFood *
                    season.FoodRecoveryPerDay)) * world.Config.DaysPerYear);
            double aquaticRenewal = water.Sum(id => world.Tiles[id].Seasons
                .Average(season => Math.Max(0, season.MaxFood *
                    season.FoodRecoveryPerDay)) * world.Config.DaysPerYear);
            if (landRenewal <= 0 || aquaticRenewal <= 0) continue;
            candidates.Add(new Site
            {
                Tile = tile.Id,
                LandRenewal = landRenewal,
                AquaticRenewal = aquaticRenewal
            });
        }
        if (candidates.Count < 12)
            throw new InvalidOperationException("Too few mixed stress sites.");
        var landOrder = candidates.OrderBy(item => item.LandRenewal)
            .ThenBy(item => item.Tile).ToArray();
        var waterOrder = candidates.OrderBy(item => item.AquaticRenewal)
            .ThenBy(item => item.Tile).ToArray();
        var landRanks = landOrder.Select((item, index) => (item.Tile,
            Rank: (index + .5) / landOrder.Length)).ToDictionary(item =>
                item.Tile, item => item.Rank);
        var waterRanks = waterOrder.Select((item, index) => (item.Tile,
            Rank: (index + .5) / waterOrder.Length)).ToDictionary(item =>
                item.Tile, item => item.Rank);
        foreach (var item in candidates)
            item.JointRank = Math.Sqrt(landRanks[item.Tile] *
                waterRanks[item.Tile]);
        var ranked = candidates.OrderBy(item => item.JointRank)
            .ThenBy(item => item.Tile).ToArray();
        return ranked[(int)Math.Round(.5 * (ranked.Length - 1),
            MidpointRounding.AwayFromZero)];
    }

    private static IEnumerable<State> BuildStates(Bank[] banks)
    {
        for (int bankIndex = 0; bankIndex < banks.Length; bankIndex++)
            for (int qIndex = 0; qIndex < OpeningQ.Length; qIndex++)
                yield return BuildState(banks[bankIndex],
                    "T" + (bankIndex + 1).ToString("00", Inv) + "-Q" +
                    QTag(OpeningQ[qIndex]), OpeningQ[qIndex]);
    }

    private static State BuildState(Bank bank, string id, double openingQ)
    {
        var world = SaveCodec.Load(bank.Snapshot);
        var camp = new Camp
        {
            Id = world.NextId++,
            Player = 0,
            Tile = bank.Site.Tile,
            Population = Population,
            QoL = 1,
            FoodNeedsMetRatio = 1,
            WaterNeedsMetRatio = 1,
            ClothPerCapita = 1,
            HousingPerCapita = 1,
            StationaryYears = 80,
            StationaryTurns = 4,
            IsVillage = true,
            EconomyAutomationTurns = -1,
            ManagedBreedingIntensity = .55,
            ManagedBreedingPolicyInitialized = true
        };
        camp.PlantFood = Population * world.Config.Consumption *
            world.Config.GenerationIntervals * ReserveTurns;
        world.Camps.Add(camp);
        Ownership.Bind(world);
        camp.Technology = Technology.Cultivation;
        camp.People.Progression.Era = Era.Neolithic;
        camp.People.OpeningTransitionProcessed = true;
        for (int i = 0; i < camp.People.Technologies.Length; i++)
            camp.People.Technologies[i] = true;
        foreach (string technology in NeolithicTech.FirstTier)
            if (!camp.People.NeolithicTechnologies.Contains(technology))
                camp.People.NeolithicTechnologies.Add(technology);
        for (int i = 0; i < camp.People.Experience.Length; i++)
        {
            camp.People.Experience[i] = world.Config.ExperienceThreshold;
            camp.People.NeolithicExperience[i] =
                world.Config.ExperienceThreshold;
        }
        camp.People.Memory = new ContactMemory(world.Config.Width);
        foreach (int tile in world.Map.Radius(camp.Tile,
                     Math.Max(3, world.Config.VisionRadius)))
            camp.People.Memory.Explored.Add(tile, world.Turn);

        double stock = 2 * Population;
        camp.Inventory.HandTools = stock;
        camp.Inventory.FarmTools = stock;
        camp.Inventory.HerdingTools = stock;
        camp.Inventory.FishingTools = stock;
        camp.Inventory.Tools = stock;
        camp.Inventory.Cloth = stock;
        camp.Inventory.Stone = 20 * Population;
        camp.Inventory.Wood = 20 * Population;
        camp.Inventory.Clay = 20 * Population;
        NeolithicProduction.AddFixedHousing(world, camp,
            Population / world.Config.Neolithic.HousingPeoplePerUnit,
            world.Days);
        double naturalStorage = NeolithicProduction.NaturalStorageCapacity(
            world, camp, 0);
        camp.Inventory.Pots = Math.Max(NeolithicProduction.TargetPots(world,
            camp, world.Config.TurnDays / world.Config.DaysPerYear),
            Math.Max(0, camp.Food - naturalStorage) /
            world.Config.Neolithic.PotStorageFoodPerUnit);

        double capital = Math.Pow(openingQ /
            world.Config.ReferenceIntensity, 2);
        int[] fields = CultivationEconomy.Footprint(world, camp);
        foreach (int tileId in fields)
        {
            var tile = world.Tiles[tileId];
            tile.CultivationOperator = camp.Id;
            tile.CultivationCapital = capital;
            tile.CultivationOperatingFraction = 1;
            tile.Cultivation = (float)openingQ;
            tile.Cultivated = true;
        }
        var access = FishingEconomy.BuildAccess(world, camp, camp.Tile,
            camp.Population, world.Days, world.Config.TurnDays);
        if (access.Grounds.Length == 0)
            throw new InvalidOperationException(id + " has no Fishing ground.");
        int[] fishing = access.Grounds.Select(ground => ground.TileId)
            .OrderBy(tile => tile).ToArray();
        foreach (int tileId in fishing)
            world.FisherySites.Add(new FisherySite
            {
                TileId = tileId,
                OperatorCampId = camp.Id,
                Capital = capital,
                LastUpdatedDays = world.Days
            });
        camp.LastSelectedFishingGrounds = fishing.ToList();

        string species = world.Config.Species.Where(item => item.Domesticable)
            .OrderBy(item => item.Id, StringComparer.Ordinal).First().Id;
        world.Herds.Add(new Herd
        {
            Id = world.NextId++, Tile = camp.Tile,
            DomesticCamp = camp.Id, OwnerSociety = camp.SocietyId,
            Species = species, Animals = 30,
            Capacity = world.Config.GetSpecies(species).MaximumHerdSize
        });
        camp.HerdTargetSet = true;
        camp.HerdTarget = 30;

        FisheryState.Normalize(world);
        NeolithicProduction.NormalizeFixedHousing(world);
        byte[] snapshot = SaveCodec.Save(world);
        world = SaveCodec.Load(snapshot);
        snapshot = SaveCodec.Save(world);
        return new State
        {
            Id = id, Seed = bank.Seed, CampId = camp.Id,
            Tile = bank.Site.Tile, OpeningQ = openingQ,
            FieldTiles = fields, FishingTiles = fishing,
            Snapshot = snapshot,
            InitialFieldCapital = fields.Sum(tile =>
                world.Tiles[tile].CultivationCapital),
            InitialFishingCapital = world.FisherySites.Where(site =>
                fishing.Contains(site.TileId)).Sum(site => site.Capital),
            Fingerprint = SaveCodec.Fingerprint(world),
            ConfigurationHash = SaveCodec.ConfigurationHash(world.Config)
        };
    }

    private static RunResult RunOne(State state, Scenario scenario, Arm arm,
        Options options, PhaseExecution execution)
    {
        var world = SaveCodec.Load(state.Snapshot);
        if (SaveCodec.Fingerprint(world) != state.Fingerprint)
            throw new InvalidOperationException("Initial fingerprint drift: " +
                state.Id);
        string physicalCapital = PhysicalCapitalSignature(world);
        ApplyKnees(world.Config, scenario, scenario.Scheduled ?
            DefaultKnee : scenario.TargetKnee);
        AssertPhysicalCapitalUnchanged(world, physicalCapital,
            "static technology configuration");

        var result = new RunResult
        {
            State = state,
            Scenario = scenario,
            Arm = arm
        };
        var builder = new ElderObservationBuilder();
        var compiler = new NativeElderDirectiveCompiler();
        ElderCompiledDecision previous = null;
        var observation = builder.Build(world, state.CampId, false);
        if (scenario.Scheduled)
        {
            // The future schedule exists only in this harness object, never in
            // WorldState or the policy tensor. All scheduled scenarios begin
            // from the same q_k=.4 observation boundary.
            var controlWorld = SaveCodec.Load(state.Snapshot);
            var controlObservation = builder.Build(controlWorld, state.CampId,
                false);
            if (ObservationHash(observation) !=
                    ObservationHash(controlObservation))
                throw new InvalidOperationException(
                    "A future q_k schedule leaked into the opening Elder observation.");
            result.ObservationLeakChecks++;
        }

        for (int step = 0; step < options.Turns; step++)
        {
            var camp = world.Camps.FirstOrDefault(item =>
                item.Id == state.CampId);
            if (camp == null || camp.Population <= 0)
            {
                AddZeroRows(result, world, options, step);
                break;
            }
            double startYear = world.Days / world.Config.DaysPerYear;
            bool transition = scenario.Scheduled &&
                Math.Abs(startYear - scenario.UnlockYear) <= 1e-8;
            if (transition)
            {
                string beforeCapital = PhysicalCapitalSignature(world);
                ApplyKnees(world.Config, scenario, scenario.TargetKnee);
                AssertPhysicalCapitalUnchanged(world, beforeCapital,
                    "scheduled technology transition");
                result.TransitionCapitalChecks++;
                // Rebuild at the physical boundary. Current lower effort costs
                // may legitimately alter current forecasts; only the future
                // schedule was excluded from all earlier observations.
                observation = builder.Build(world, state.CampId, false,
                    previous);
            }

            int priorPopulation = camp.Population;
            string usedObservationHash = ObservationHash(observation);
            bool active = arm.Invest && step >= arm.StartTurn &&
                step < arm.StartTurn + options.DevelopmentTurns;
            var directive = new ElderDirective
            {
                RetainedFieldOperation = 1,
                RetainedHerdFraction = 1,
                DevelopmentBudget = active ? options.DevelopmentBudget : 0,
                FieldDevelopmentShare = 1,
                MovementCandidate = -1
            };

            var planned = AiPlanning.Decide(world, execution, camp.Player);
            var background = CopyOrders(planned.Orders);
            if (!background.TryGetValue(camp.Player, out var orders))
                background[camp.Player] = orders = new List<Order>();
            orders.Add(new Order(OrderKind.NoOp, state.CampId));
            var prepared = TurnPlanner.PrepareWithElderDirectives(world,
                background,
                new Dictionary<int, ElderDirective>
                {
                    [state.CampId] = directive
                },
                new Dictionary<int, ElderObservation>
                {
                    [state.CampId] = observation
                }, compiler, null, execution);
            if (!prepared.ElderDecisions.TryGetValue(state.CampId,
                    out var decision))
                throw new InvalidOperationException(
                    "The technology-stress Elder action was not compiled.");
            var intent = decision.Intent;
            int farmers = Role(intent, WorkforceRoles.Farmer);
            int fishers = Role(intent, WorkforceRoles.Fisher);
            int explicitGatherers = Role(intent, WorkforceRoles.Gatherer);
            int gatherers = Math.Max(0, camp.Population -
                intent.Assignments.Sum(item => item.Population)) +
                explicitGatherers;
            prepared.FishingPlans.TryGetValue(state.CampId,
                out var fishingPlan);
            var grounds = fishingPlan?.Grounds ??
                Array.Empty<FishingGroundPlan>();

            var resolver = new TurnResolver(execution);
            var record = resolver.Resolve(world, prepared);
            camp = world.Camps.FirstOrDefault(item => item.Id == state.CampId);
            double years = record.Duration / world.Config.DaysPerYear;
            var demographic = record.Demographics.FirstOrDefault(item =>
                item.Camp == state.CampId);
            var fieldWork = resolver.LastMetrics.CraftFieldWork != null &&
                    resolver.LastMetrics.CraftFieldWork.TryGetValue(
                        state.CampId, out var work)
                ? work : null;
            var ecology = MeasureEcology(world, state);
            var fissions = (record.Fissions ?? new List<FissionReport>())
                .Where(item => item.ParentCamp == state.CampId).ToArray();
            double quality = camp == null ? 0 :
                ElderRLContract.Unit(camp.QoL);
            result.Rows.Add(new Row
            {
                StateId = state.Id,
                ScenarioId = scenario.Id,
                Mode = scenario.Mode,
                Scope = scenario.Scope,
                Arm = arm.Id,
                Knowledge = arm.Knowledge,
                ObservationHash = usedObservationHash,
                InitialFingerprint = state.Fingerprint,
                Seed = state.Seed,
                Turn = step + 1,
                PriorPopulation = priorPopulation,
                EndingPopulation = camp?.Population ?? 0,
                Farmers = farmers,
                Fishers = fishers,
                Gatherers = gatherers,
                Fissions = fissions.Length,
                DaughterPopulation = fissions.Sum(item => item.Daughter),
                CampCount = world.Camps.Count(item => item.Population > 0),
                TechnologyTransition = transition,
                OpeningQ = state.OpeningQ,
                TargetKnee = scenario.TargetKnee,
                UnlockYear = scenario.UnlockYear,
                StartYear = startYear,
                EndYear = world.Days / world.Config.DaysPerYear,
                ElapsedYears = years,
                FieldKnee = world.Config.Neolithic.
                    CultivationMarginalEffortKneeQ,
                FishingKnee = world.Config.Neolithic.
                    FishingMarginalEffortKneeQ,
                RequestedDevelopmentBudget = directive.DevelopmentBudget,
                AcceptedDevelopmentBudget = decision.Accepted.
                    DevelopmentBudget,
                RealizedDevelopmentBudget = decision.Realized.
                    DevelopmentBudget,
                AcceptedDevelopmentPeople = decision.
                    AcceptedDevelopmentPeople,
                RealizedFieldDevelopmentPeople = decision.
                    ResolvedFieldDevelopmentPeople,
                QoL = quality,
                WelfareRate = priorPopulation * quality,
                RawWelfare = priorPopulation * quality * years,
                FoodProduced = demographic?.FoodProduced ?? 0,
                FieldFood = record.Transfers.Where(transfer =>
                    transfer.Entity == state.CampId && transfer.Kind ==
                    "Cultivation").Sum(transfer => transfer.Food),
                FishFood = record.Transfers.Where(transfer =>
                    transfer.Entity == state.CampId && transfer.Kind ==
                    "FisheryHarvest").Sum(transfer => transfer.Food),
                GatheredFood = demographic?.ForageFood ?? 0,
                FoodReserves = camp?.Food ?? 0,
                FieldQMean = ecology.FieldQMean,
                FieldQMax = ecology.FieldQMax,
                FieldCapital = ecology.FieldCapital,
                FishingQMean = ecology.FishingQMean,
                FishingCapital = ecology.FishingCapital,
                FieldBuildWorkerYears = fieldWork?.
                    ExpansionWorkUsedWorkerYears ?? 0,
                FieldMaintenanceWorkerYears = fieldWork?.
                    MaintenanceWorkerYears ?? 0,
                FishingBuildWorkerYears = grounds.Sum(ground =>
                    ground.BuildWorkerYears),
                FishingMaintenanceWorkerYears = grounds.Sum(ground =>
                    ground.MaintenanceWorkerYears),
                LandFood = ecology.LandFood,
                LandBiomass = ecology.LandBiomass,
                LandCondition = ecology.LandCondition,
                AquaticFood = ecology.AquaticFood,
                AquaticBiomass = ecology.AquaticBiomass,
                AquaticCondition = ecology.AquaticCondition
            });
            previous = decision;
            observation = camp == null || camp.Population <= 0 ? null :
                builder.Build(world, state.CampId, false, previous);
        }

        result.FinalFingerprint = SaveCodec.Fingerprint(world);
        foreach (var row in result.Rows)
            row.FinalFingerprint = result.FinalFingerprint;
        return result;
    }

    private static void AddZeroRows(RunResult result, WorldState world,
        Options options, int first)
    {
        double years = world.Config.TurnDays / world.Config.DaysPerYear;
        for (int step = first; step < options.Turns; step++)
            result.Rows.Add(new Row
            {
                StateId = result.State.Id,
                ScenarioId = result.Scenario.Id,
                Mode = result.Scenario.Mode,
                Scope = result.Scenario.Scope,
                Arm = result.Arm.Id,
                Knowledge = result.Arm.Knowledge,
                InitialFingerprint = result.State.Fingerprint,
                Seed = result.State.Seed,
                Turn = step + 1,
                OpeningQ = result.State.OpeningQ,
                TargetKnee = result.Scenario.TargetKnee,
                UnlockYear = result.Scenario.UnlockYear,
                StartYear = step * years,
                EndYear = (step + 1) * years,
                ElapsedYears = years,
                FieldKnee = world.Config.Neolithic.
                    CultivationMarginalEffortKneeQ,
                FishingKnee = world.Config.Neolithic.
                    FishingMarginalEffortKneeQ
            });
    }

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

    private static void ApplyKnees(SimulationConfig config,
        Scenario scenario, double knee)
    {
        config.Neolithic.CultivationMarginalEffortKneeQ =
            scenario.Scope == "cultivation" || scenario.Scope == "shared"
                ? knee : DefaultKnee;
        config.Neolithic.FishingMarginalEffortKneeQ =
            scenario.Scope == "fishing" || scenario.Scope == "shared"
                ? knee : DefaultKnee;
        // Every arm is forced, including the q_k=.4 control, so later local
        // capability acquisition cannot contaminate a matched counterfactual.
        config.Neolithic.ExperimentalCultivationEffortKneeOverride = true;
        config.Neolithic.ExperimentalFishingEffortKneeOverride = true;
        config.Validate();
    }

    private static void AssertPhysicalCapitalUnchanged(WorldState world,
        string before, string boundary)
    {
        if (before != PhysicalCapitalSignature(world))
            throw new InvalidOperationException(boundary +
                " rewrote stored physical capital.");
    }

    private static string PhysicalCapitalSignature(WorldState world)
    {
        var ledger = new StringBuilder();
        foreach (var tile in world.Tiles.OrderBy(item => item.Id))
            ledger.Append(tile.Id).Append(':')
                .Append(tile.CultivationOperator).Append(':')
                .Append(BitConverter.DoubleToInt64Bits(
                    tile.CultivationCapital)).Append('|');
        ledger.Append('/');
        foreach (var site in world.FisherySites.OrderBy(item => item.TileId)
                     .ThenBy(item => item.OperatorCampId))
            ledger.Append(site.TileId).Append(':')
                .Append(site.OperatorCampId).Append(':')
                .Append(BitConverter.DoubleToInt64Bits(site.Capital))
                .Append('|');
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(
            ledger.ToString())).Select(value => value.ToString("x2", Inv)));
    }

    private static double StoredFieldCapital(WorldState world, State state)
        => state.FieldTiles.Sum(tile =>
            world.Tiles[tile].CultivationCapital);

    private static double StoredFishingCapital(WorldState world, State state)
        => world.FisherySites.Where(site =>
            state.FishingTiles.Contains(site.TileId)).Sum(site => site.Capital);

    private static int Role(EconomicIntent intent, string role)
        => intent?.Assignments?.Where(item => item != null &&
            item.Role == role).Sum(item => item.Population) ?? 0;

    private static string ObservationHash(ElderObservation observation)
    {
        var encoded = ElderObservationSchema.Encode(observation);
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(encoded,
            Json));
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(bytes).Select(value =>
            value.ToString("x2", Inv)));
    }

    private sealed class Ecology
    {
        public double FieldQMean, FieldQMax, FieldCapital, FishingQMean,
            FishingCapital, LandFood, LandBiomass, LandCondition,
            AquaticFood, AquaticBiomass, AquaticCondition;
    }

    private static Ecology MeasureEcology(WorldState world, State state)
    {
        var result = new Ecology();
        foreach (int tile in state.FieldTiles)
        {
            var view = LazyEcology.ProjectEcology(world, tile, world.Days);
            double q = CultivationEconomy.Intensity(world.Config,
                view.CultivationCapital);
            result.FieldQMean += q;
            result.FieldQMax = Math.Max(result.FieldQMax, q);
            result.FieldCapital += view.CultivationCapital;
            result.LandFood += view.Food;
            result.LandBiomass += view.PotentialFood;
            result.LandCondition += view.LandCondition;
        }
        if (state.FieldTiles.Length > 0)
        {
            result.FieldQMean /= state.FieldTiles.Length;
            result.LandCondition /= state.FieldTiles.Length;
        }
        foreach (int tile in state.FishingTiles)
        {
            var view = LazyEcology.ProjectEcology(world, tile, world.Days);
            var site = world.FisherySites.FirstOrDefault(item =>
                item.TileId == tile);
            double capital = site?.Capital ?? 0;
            result.FishingQMean += FishingEconomy.Intensity(world.Config,
                capital);
            result.FishingCapital += capital;
            result.AquaticFood += view.Food;
            result.AquaticBiomass += view.PotentialFood;
            result.AquaticCondition += view.AquaticCondition;
        }
        if (state.FishingTiles.Length > 0)
        {
            result.FishingQMean /= state.FishingTiles.Length;
            result.AquaticCondition /= state.FishingTiles.Length;
        }
        return result;
    }

    private static IEnumerable<Payoff> AnalyzePayoffs(State[] states,
        Scenario[] scenarios, List<RunResult> runs)
    {
        foreach (var state in states)
        foreach (var scenario in scenarios)
        {
            var baseline = runs.Single(run => run.State == state &&
                run.Scenario == scenario &&
                run.Arm.Id == "no-development");
            foreach (var treatment in runs.Where(run => run.State == state &&
                         run.Scenario == scenario &&
                         run.Arm.Id != "no-development"))
            {
                int count = treatment.Rows.Count;
                var deltaRates = new double[count];
                double undiscounted = 0, fieldBuild = 0,
                    fieldMaintenance = 0, fishingBuild = 0,
                    fishingMaintenance = 0, fissions = 0, daughters = 0;
                double minLand = double.PositiveInfinity,
                    minAquatic = double.PositiveInfinity;
                for (int i = 0; i < count; i++)
                {
                    var treated = treatment.Rows[i];
                    var control = baseline.Rows[i];
                    deltaRates[i] = treated.WelfareRate -
                        control.WelfareRate;
                    undiscounted += deltaRates[i] * treated.ElapsedYears;
                    fieldBuild += treated.FieldBuildWorkerYears -
                        control.FieldBuildWorkerYears;
                    fieldMaintenance += treated.FieldMaintenanceWorkerYears -
                        control.FieldMaintenanceWorkerYears;
                    fishingBuild += treated.FishingBuildWorkerYears -
                        control.FishingBuildWorkerYears;
                    fishingMaintenance +=
                        treated.FishingMaintenanceWorkerYears -
                        control.FishingMaintenanceWorkerYears;
                    fissions += treated.Fissions - control.Fissions;
                    daughters += treated.DaughterPopulation -
                        control.DaughterPopulation;
                    minLand = Math.Min(minLand, treated.LandCondition -
                        control.LandCondition);
                    minAquatic = Math.Min(minAquatic,
                        treated.AquaticCondition -
                        control.AquaticCondition);
                }
                foreach (double horizon in new[]
                         {
                             double.PositiveInfinity, 400d, 800d
                         })
                {
                    double value = 0;
                    var cumulative = new double[count];
                    for (int i = 0; i < count; i++)
                    {
                        var row = treatment.Rows[i];
                        double weight = double.IsPositiveInfinity(horizon)
                            ? row.ElapsedYears : DiscountWeight(row.StartYear,
                                row.EndYear, horizon);
                        value += deltaRates[i] * weight;
                        cumulative[i] = value;
                    }
                    int startIndex = Math.Max(0, treatment.Arm.StartTurn);
                    var payback = new double[Math.Max(0,
                        count - startIndex)];
                    var paybackYears = new double[payback.Length];
                    double paybackValue = 0;
                    double investmentStartYear = startIndex < count
                        ? treatment.Rows[startIndex].StartYear
                        : treatment.Rows[count - 1].EndYear;
                    for (int i = startIndex; i < count; i++)
                    {
                        var row = treatment.Rows[i];
                        double weight = double.IsPositiveInfinity(horizon)
                            ? row.ElapsedYears : DiscountWeight(row.StartYear,
                                row.EndYear, horizon);
                        paybackValue += deltaRates[i] * weight;
                        payback[i - startIndex] = paybackValue;
                        paybackYears[i - startIndex] = row.EndYear -
                            investmentStartYear;
                    }
                    int minimum = Array.IndexOf(cumulative, cumulative.Min());
                    bool improves = undiscounted > 1e-8;
                    bool positive = value > 1e-8;
                    var final = treatment.Rows[count - 1];
                    var finalControl = baseline.Rows[count - 1];
                    yield return new Payoff
                    {
                        StateId = state.Id,
                        ScenarioId = scenario.Id,
                        Mode = scenario.Mode,
                        Scope = scenario.Scope,
                        Arm = treatment.Arm.Id,
                        Knowledge = treatment.Arm.Knowledge,
                        Horizon = double.IsPositiveInfinity(horizon)
                            ? "undiscounted" : horizon.ToString("0", Inv),
                        SignClassification = improves
                            ? positive ? "true-positive" : "false-negative"
                            : positive ? "false-positive" :
                                "true-nonpositive",
                        Seed = state.Seed,
                        UnlockYear = scenario.UnlockYear,
                        OpeningQ = state.OpeningQ,
                        TargetKnee = scenario.TargetKnee,
                        Value = value,
                        UndiscountedValue = undiscounted,
                        InvestmentStartYear = investmentStartYear,
                        DurableBreakEvenYears = DurableBreakEven(payback,
                            paybackYears),
                        DurablePositiveYears = DurablePositive(payback,
                            paybackYears),
                        MinimumCumulativeValue = cumulative[minimum],
                        MinimumCumulativeYear = treatment.Rows[minimum].EndYear,
                        IncrementalFieldBuildWorkerYears = fieldBuild,
                        IncrementalFieldMaintenanceWorkerYears =
                            fieldMaintenance,
                        IncrementalFishingBuildWorkerYears = fishingBuild,
                        IncrementalFishingMaintenanceWorkerYears =
                            fishingMaintenance,
                        FinalDeltaFieldQ = final.FieldQMean -
                            finalControl.FieldQMean,
                        FinalDeltaFishingQ = final.FishingQMean -
                            finalControl.FishingQMean,
                        FinalDeltaFieldCapital = final.FieldCapital -
                            finalControl.FieldCapital,
                        FinalDeltaFishingCapital = final.FishingCapital -
                            finalControl.FishingCapital,
                        MinimumDeltaLandCondition = minLand,
                        MinimumDeltaAquaticCondition = minAquatic,
                        FinalDeltaLandBiomass = final.LandBiomass -
                            finalControl.LandBiomass,
                        FinalDeltaAquaticBiomass = final.AquaticBiomass -
                            finalControl.AquaticBiomass,
                        IncrementalFissions = fissions,
                        IncrementalDaughterPopulation = daughters,
                        LongRunImproves = improves,
                        CorrectSign = improves == positive
                    };
                }
            }
        }
    }

    private static IEnumerable<Timing> AnalyzeTiming(State[] states,
        Scenario[] scenarios, Payoff[] payoffs)
    {
        foreach (var state in states)
        foreach (var scenario in scenarios.Where(item => item.Scheduled))
        {
            double Value(string arm, string horizon) => payoffs.Single(item =>
                item.StateId == state.Id && item.ScenarioId == scenario.Id &&
                item.Arm == arm && item.Horizon == horizon).Value;
            double nowU = Value("invest-now", "undiscounted");
            double afterU = Value("invest-after-known", "undiscounted");
            double now400 = Value("invest-now", "400");
            double after400 = Value("invest-after-known", "400");
            double now800 = Value("invest-now", "800");
            double after800 = Value("invest-after-known", "800");
            string longRun = Choice(nowU, afterU);
            string h400 = Choice(now400, after400);
            string h800 = Choice(now800, after800);
            double bestLong = Math.Max(0, Math.Max(nowU, afterU));
            double LongValue(string choice) => choice == "invest-now" ? nowU :
                choice == "invest-after" ? afterU : 0;
            double regret400 = bestLong - LongValue(h400);
            double regret800 = bestLong - LongValue(h800);
            yield return new Timing
            {
                StateId = state.Id,
                ScenarioId = scenario.Id,
                Scope = scenario.Scope,
                Seed = state.Seed,
                UnlockYear = scenario.UnlockYear,
                OpeningQ = state.OpeningQ,
                TargetKnee = scenario.TargetKnee,
                NowUndiscounted = nowU,
                AfterUndiscounted = afterU,
                NowH400 = now400,
                AfterH400 = after400,
                NowH800 = now800,
                AfterH800 = after800,
                LongRunChoice = longRun,
                H400Choice = h400,
                H800Choice = h800,
                H400LongRunRegret = regret400,
                H800LongRunRegret = regret800,
                H400Inversion = h400 != longRun,
                H800Inversion = h800 != longRun,
                H800StrictlyLowerRegret = regret800 + 1e-8 < regret400
            };
        }
    }

    private static string Choice(double now, double after)
    {
        double best = Math.Max(0, Math.Max(now, after));
        if (best <= 1e-8) return "no-development";
        if (after > now + 1e-8) return "invest-after";
        return "invest-now";
    }

    private static double DiscountWeight(double start, double end,
        double horizon) => horizon * (Math.Exp(-start / horizon) -
            Math.Exp(-end / horizon));

    private static double DurableBreakEven(double[] cumulative,
        double[] years)
    {
        const double tolerance = 1e-8;
        if (cumulative.Length == 0 || cumulative[cumulative.Length - 1] <
                -tolerance) return -1;
        if (cumulative.All(value => value >= -tolerance)) return 0;
        for (int i = 0; i < cumulative.Length; i++)
            if (cumulative[i] >= -tolerance && cumulative.Skip(i)
                    .All(value => value >= -tolerance))
                return years[i];
        return -1;
    }

    private static double DurablePositive(double[] cumulative,
        double[] years)
    {
        const double tolerance = 1e-8;
        if (cumulative.Length == 0 || cumulative[cumulative.Length - 1] <=
                tolerance) return -1;
        for (int i = 0; i < cumulative.Length; i++)
            if (cumulative[i] > tolerance && cumulative.Skip(i)
                    .All(value => value > tolerance))
                return years[i];
        return -1;
    }

    private static string VerifyRuns(Options options, State[] states,
        Scenario[] scenarios, List<RunResult> runs)
    {
        int identicalKnowledgePairs = 0;
        foreach (var scenario in scenarios.Where(item => item.Scheduled))
        foreach (var state in states)
        {
            var known = runs.Single(run => run.State == state &&
                run.Scenario == scenario &&
                run.Arm.Id == "invest-after-known");
            var unannounced = runs.Single(run => run.State == state &&
                run.Scenario == scenario &&
                run.Arm.Id == "invest-after-unannounced");
            bool same = known.FinalFingerprint ==
                    unannounced.FinalFingerprint &&
                known.Rows.Select(RowSignature).SequenceEqual(
                    unannounced.Rows.Select(RowSignature));
            if (!same)
                throw new InvalidOperationException(
                    "Known/event-time identical directives diverged: " +
                    state.Id + " " + scenario.Id);
            identicalKnowledgePairs++;
        }
        int observationChecks = runs.Sum(run => run.ObservationLeakChecks);
        int capitalChecks = runs.Sum(run => run.TransitionCapitalChecks);
        string replay = "not-requested";
        if (options.Verify)
        {
            var state = states[0];
            var scenario = scenarios.Last(item => item.Scheduled &&
                item.Scope == "shared");
            var expected = runs.Single(run => run.State == state &&
                run.Scenario == scenario &&
                run.Arm.Id == "invest-after-known");
            var actual = RunOne(state, scenario, expected.Arm, options,
                new PhaseExecution(4, 2, true, 1));
            if (expected.FinalFingerprint != actual.FinalFingerprint ||
                !expected.Rows.Select(RowSignature).SequenceEqual(
                    actual.Rows.Select(RowSignature)))
                throw new InvalidOperationException(
                    "Scalar/reversed-parallel technology replay drift.");
            replay = "pass";
        }
        return "PASS: curve continuity/differentiability/inverses; " +
            observationChecks + " opening future-schedule non-leak checks; " +
            capitalChecks + " stored-capital invariants; " +
            identicalKnowledgePairs + " known/event-time matched pairs; " +
            "scalar/reversed-parallel replay=" + replay + ".";
    }

    private static string RowSignature(Row row) => string.Join("|", new[]
    {
        row.ObservationHash ?? "", I(row.Turn), D(row.WelfareRate),
        D(row.FieldCapital), D(row.FishingCapital),
        D(row.FieldBuildWorkerYears), D(row.FieldMaintenanceWorkerYears),
        D(row.FishingBuildWorkerYears),
        D(row.FishingMaintenanceWorkerYears), I(row.EndingPopulation),
        I(row.Fissions), I(row.DaughterPopulation)
    });

    private static void WriteStates(string path, IEnumerable<State> states)
    {
        using var file = Writer(path);
        file.WriteLine("state_id,seed,opening_q,population,reserve_turns,site_tile,initial_field_capital,initial_fishing_capital,field_tiles,fishing_tiles,initial_fingerprint,configuration_hash");
        foreach (var state in states)
            file.WriteLine(string.Join(",", new[]
            {
                S(state.Id), I(state.Seed), D(state.OpeningQ), I(Population),
                D(ReserveTurns), I(state.Tile), D(state.InitialFieldCapital),
                D(state.InitialFishingCapital),
                S(string.Join(";", state.FieldTiles)),
                S(string.Join(";", state.FishingTiles)),
                S(state.Fingerprint), S(state.ConfigurationHash)
            }));
    }

    private static void WriteScenarios(string path,
        IEnumerable<Scenario> scenarios, Options options)
    {
        using var file = Writer(path);
        file.WriteLine("scenario_id,mode,scope,target_q_k,unlock_year,field_q_k_before,fishing_q_k_before,development_budget,development_turns,development_years");
        foreach (var scenario in scenarios)
        {
            double beforeField = scenario.Scheduled ? DefaultKnee :
                scenario.Scope == "cultivation" || scenario.Scope == "shared"
                    ? scenario.TargetKnee : DefaultKnee;
            double beforeFishing = scenario.Scheduled ? DefaultKnee :
                scenario.Scope == "fishing" || scenario.Scope == "shared"
                    ? scenario.TargetKnee : DefaultKnee;
            file.WriteLine(string.Join(",", new[]
            {
                S(scenario.Id), S(scenario.Mode), S(scenario.Scope),
                D(scenario.TargetKnee), I(scenario.UnlockYear),
                D(beforeField), D(beforeFishing),
                D(options.DevelopmentBudget), I(options.DevelopmentTurns),
                D(options.DevelopmentTurns * 20)
            }));
        }
    }

    private static void WriteRows(string path, IEnumerable<Row> rows)
    {
        using var file = Writer(path);
        file.WriteLine("state_id,scenario_id,mode,scope,arm,knowledge,seed,opening_q,target_q_k,unlock_year,turn,start_year,end_year,elapsed_years,technology_transition,field_q_k,fishing_q_k,requested_development_budget,accepted_development_budget,realized_development_budget,accepted_development_people,realized_field_development_people,prior_population,ending_population,qol,welfare_rate,raw_welfare,farmers,fishers,gatherers,food_produced,field_food,fish_food,gathered_food,food_reserves,field_q_mean,field_q_max,field_capital,fishing_q_mean,fishing_capital,field_build_worker_years,field_maintenance_worker_years,fishing_build_worker_years,fishing_maintenance_worker_years,land_food,land_biomass,land_condition,aquatic_food,aquatic_biomass,aquatic_condition,fissions,daughter_population,camp_count,observation_hash,initial_fingerprint,final_fingerprint");
        foreach (var row in rows)
            file.WriteLine(string.Join(",", new[]
            {
                S(row.StateId), S(row.ScenarioId), S(row.Mode), S(row.Scope),
                S(row.Arm), S(row.Knowledge), I(row.Seed), D(row.OpeningQ),
                D(row.TargetKnee), D(row.UnlockYear), I(row.Turn),
                D(row.StartYear), D(row.EndYear), D(row.ElapsedYears),
                B(row.TechnologyTransition), D(row.FieldKnee),
                D(row.FishingKnee), D(row.RequestedDevelopmentBudget),
                D(row.AcceptedDevelopmentBudget),
                D(row.RealizedDevelopmentBudget),
                D(row.AcceptedDevelopmentPeople),
                D(row.RealizedFieldDevelopmentPeople),
                I(row.PriorPopulation), I(row.EndingPopulation), D(row.QoL),
                D(row.WelfareRate), D(row.RawWelfare), I(row.Farmers),
                I(row.Fishers), I(row.Gatherers), D(row.FoodProduced),
                D(row.FieldFood), D(row.FishFood), D(row.GatheredFood),
                D(row.FoodReserves), D(row.FieldQMean), D(row.FieldQMax),
                D(row.FieldCapital), D(row.FishingQMean),
                D(row.FishingCapital), D(row.FieldBuildWorkerYears),
                D(row.FieldMaintenanceWorkerYears),
                D(row.FishingBuildWorkerYears),
                D(row.FishingMaintenanceWorkerYears), D(row.LandFood),
                D(row.LandBiomass), D(row.LandCondition),
                D(row.AquaticFood), D(row.AquaticBiomass),
                D(row.AquaticCondition), I(row.Fissions),
                I(row.DaughterPopulation), I(row.CampCount),
                S(row.ObservationHash), S(row.InitialFingerprint),
                S(row.FinalFingerprint)
            }));
    }

    private static void WritePayoffs(string path, IEnumerable<Payoff> rows)
    {
        using var file = Writer(path);
        file.WriteLine("state_id,scenario_id,mode,scope,arm,knowledge,horizon,seed,opening_q,target_q_k,unlock_year,value,undiscounted_value,investment_start_year,durable_break_even_years,durable_positive_years,minimum_cumulative_value,minimum_cumulative_year,incremental_field_build_worker_years,incremental_field_maintenance_worker_years,incremental_fishing_build_worker_years,incremental_fishing_maintenance_worker_years,final_delta_field_q,final_delta_fishing_q,final_delta_field_capital,final_delta_fishing_capital,minimum_delta_land_condition,minimum_delta_aquatic_condition,final_delta_land_biomass,final_delta_aquatic_biomass,incremental_fissions,incremental_daughter_population,long_run_improves,correct_sign,sign_classification");
        foreach (var row in rows)
            file.WriteLine(string.Join(",", new[]
            {
                S(row.StateId), S(row.ScenarioId), S(row.Mode), S(row.Scope),
                S(row.Arm), S(row.Knowledge), S(row.Horizon), I(row.Seed),
                D(row.OpeningQ), D(row.TargetKnee), I(row.UnlockYear),
                D(row.Value), D(row.UndiscountedValue),
                D(row.InvestmentStartYear), D(row.DurableBreakEvenYears),
                D(row.DurablePositiveYears), D(row.MinimumCumulativeValue),
                D(row.MinimumCumulativeYear),
                D(row.IncrementalFieldBuildWorkerYears),
                D(row.IncrementalFieldMaintenanceWorkerYears),
                D(row.IncrementalFishingBuildWorkerYears),
                D(row.IncrementalFishingMaintenanceWorkerYears),
                D(row.FinalDeltaFieldQ), D(row.FinalDeltaFishingQ),
                D(row.FinalDeltaFieldCapital),
                D(row.FinalDeltaFishingCapital),
                D(row.MinimumDeltaLandCondition),
                D(row.MinimumDeltaAquaticCondition),
                D(row.FinalDeltaLandBiomass),
                D(row.FinalDeltaAquaticBiomass),
                D(row.IncrementalFissions),
                D(row.IncrementalDaughterPopulation),
                B(row.LongRunImproves), B(row.CorrectSign),
                S(row.SignClassification)
            }));
    }

    private static void WriteTiming(string path, IEnumerable<Timing> rows)
    {
        using var file = Writer(path);
        file.WriteLine("state_id,scenario_id,scope,seed,opening_q,target_q_k,unlock_year,now_undiscounted,after_undiscounted,now_h400,after_h400,now_h800,after_h800,long_run_choice,h400_choice,h800_choice,h400_long_run_regret,h800_long_run_regret,h400_inversion,h800_inversion,h800_strictly_lower_regret");
        foreach (var row in rows)
            file.WriteLine(string.Join(",", new[]
            {
                S(row.StateId), S(row.ScenarioId), S(row.Scope), I(row.Seed),
                D(row.OpeningQ), D(row.TargetKnee), I(row.UnlockYear),
                D(row.NowUndiscounted), D(row.AfterUndiscounted),
                D(row.NowH400), D(row.AfterH400), D(row.NowH800),
                D(row.AfterH800), S(row.LongRunChoice), S(row.H400Choice),
                S(row.H800Choice), D(row.H400LongRunRegret),
                D(row.H800LongRunRegret), B(row.H400Inversion),
                B(row.H800Inversion), B(row.H800StrictlyLowerRegret)
            }));
    }

    private static void WriteManifest(Options options, SimulationConfig config,
        State[] states, Scenario[] scenarios, List<RunResult> runs,
        Row[] rows, Payoff[] payoffs, Timing[] timing, TimeSpan elapsed,
        string verification)
    {
        string Git(params string[] arguments)
        {
            try
            {
                var start = new ProcessStartInfo("git")
                {
                    WorkingDirectory = options.Repo,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                foreach (string argument in arguments)
                    start.ArgumentList.Add(argument);
                using var process = Process.Start(start);
                string output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();
                return process.ExitCode == 0 ? output : "unknown";
            }
            catch { return "unknown"; }
        }
        string HashFile(string relative)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(Path.Combine(options.Repo,
                relative));
            return string.Concat(sha.ComputeHash(stream).Select(value =>
                value.ToString("x2", Inv)));
        }
        string SimulationHash()
        {
            string root = Path.Combine(options.Repo,
                "Assets/_Anthropocene/Simulation");
            string ledger = string.Join("\n", Directory.GetFiles(root, "*.cs")
                .OrderBy(path => Path.GetFileName(path),
                    StringComparer.Ordinal).Select(path =>
                        Path.GetFileName(path) + ":" + HashFile(
                            Path.GetRelativePath(options.Repo, path))));
            using var sha = SHA256.Create();
            return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(ledger))
                .Select(value => value.ToString("x2", Inv)));
        }
        var manifest = new
        {
            harness = "physical-investment-payback-technology-stress-v1",
            generatedUtc = DateTime.UtcNow.ToString("O", Inv),
            simulatorCommit = Git("rev-parse", "HEAD"),
            workingTreeDirty = Git("status", "--porcelain") != string.Empty,
            simulatorVersion = NativeElderEnvironment.SimulatorVersion,
            simulationSourceSha256 = SimulationHash(),
            harnessSourceSha256 = HashFile(
                "Tools/InvestmentPayback/TechnologyStress.cs"),
            openingConfigSha256 = HashFile(
                "Assets/_Anthropocene/Resources/OpeningConfig.json"),
            configurationHash = SaveCodec.ConfigurationHash(config),
            seeds = options.Seeds,
            map = new { options.Width, options.Height },
            decisions = options.Turns,
            years = options.Turns * config.TurnDays / config.DaysPerYear,
            turnYears = config.TurnDays / config.DaysPerYear,
            openingQ = OpeningQ,
            population = Population,
            reserveTurns = ReserveTurns,
            defaultKneeQ = DefaultKnee,
            counterfactualKneeQ = new[] { .5, .8 },
            unlockYears = options.UnlockYears,
            scopes = Scopes,
            development = new
            {
                options.DevelopmentBudget,
                decisions = options.DevelopmentTurns,
                years = options.DevelopmentTurns * config.TurnDays /
                    config.DaysPerYear,
                fieldDevelopmentShare = 1,
                retainedFieldOperation = 1,
                retainedHerdFraction = 1
            },
            discountEFoldingYears = new[] { 400, 800 },
            stateCount = states.Length,
            scenarioCount = scenarios.Length,
            trajectoryCount = runs.Count,
            trajectoryJobs = options.Jobs,
            trajectoryRows = rows.Length,
            payoffRows = payoffs.Length,
            timingRows = timing.Length,
            openingFutureScheduleNonLeakChecks = runs.Sum(run =>
                run.ObservationLeakChecks),
            transitionCapitalChecks = runs.Sum(run =>
                run.TransitionCapitalChecks),
            verification,
            elapsedSeconds = elapsed.TotalSeconds,
            assumptions = new[]
            {
                "q(E), decay, ecology, demographics, fission and Elder reward are unchanged production equations.",
                "q_k changes only the future marginal build and maintenance labor transform; stored E and q(E) are not rewritten and historical work is not refunded.",
                "Every Development decision is compiled by ElderObservationBuilder plus NativeElderDirectiveCompiler through TurnPlanner.PrepareWithElderDirectives.",
                "Development is sustained for the declared consecutive decisions and is field-directed through the existing Elder head.",
                "Fishing has no invented Elder action: fishery capital and harvest are deterministic responses of the ordinary compiler and fishing equations.",
                "Scheduled technology metadata is harness-side and absent from the 512-feature Elder observation; known-from-opening behavior therefore assumes external foresight.",
                "The revealed-at-unlock arm receives the event only at its physical boundary; its action path is intentionally identical to known waiting and is verified bit-exact.",
                "Undiscounted prior-parent N*endpoint-Q over 1,600 years is the primary finite comparator; daughter welfare remains outside the Elder boundary."
            }
        };
        File.WriteAllText(Path.Combine(options.Output, "manifest.json"),
            JsonSerializer.Serialize(manifest, Json));
    }

    private static StreamWriter Writer(string path) =>
        new StreamWriter(path, false, new UTF8Encoding(false));
    private static string D(double value) => value.ToString("R", Inv);
    private static string I(int value) => value.ToString(Inv);
    private static string B(bool value) => value ? "true" : "false";
    private static string S(string value)
    {
        value ??= string.Empty;
        return value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0
            ? value : "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
