using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Anthropocene.Simulation;

// Controlled measurement harness only. It compiles the production simulation
// sources into this executable and changes no production equation or objective.
internal static class Program
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
    {
        IncludeFields = true,
        WriteIndented = true
    };

    private static readonly int[] DefaultSeeds = { 61169, 74843, 91781 };
    private static readonly double[] CapitalQ = { .10, .25, .40 };
    private static readonly int[] Populations = { 80, 160, 320 };
    private static readonly double[] ReserveTurns = { .25, 1, 2 };
    private static readonly double[] HerdAssetFractions = { .25, .50, .80 };
    private static readonly double[] PulseShares = { .05, .10, .20 };
    private static readonly string[] LevelNames = { "low", "medium", "high" };
    private static readonly string[] Domains = { "field", "herd", "fishing" };

    // L9(3^4): asset, population, ecological support, food reserves.
    private static readonly int[,] L9 =
    {
        { 0, 0, 0, 0 },
        { 0, 1, 1, 1 },
        { 0, 2, 2, 2 },
        { 1, 0, 1, 2 },
        { 1, 1, 2, 0 },
        { 1, 2, 0, 1 },
        { 2, 0, 2, 1 },
        { 2, 1, 0, 2 },
        { 2, 2, 1, 0 }
    };

    private sealed class Options
    {
        public string Repo, Output;
        public int Turns = 40, Width = 128, Height = 64;
        public int[] Seeds = DefaultSeeds;
        public bool Quick, Verify;
    }

    private sealed class SiteCandidate
    {
        public int Tile, LandTiles, FishingTiles;
        public double LandFood, LandBiomass, LandRenewalPerYear;
        public double AquaticFood, AquaticBiomass, AquaticRenewalPerYear;
        public double Freshwater, LandRank, AquaticRank, JointRank;
    }

    private sealed class GeographyBank
    {
        public int Seed;
        public byte[] Snapshot;
        public SiteCandidate[] Sites;
    }

    private sealed class StateSpec
    {
        public string StateId, InitialFingerprint, ConfigurationHash;
        public int Seed, MatrixRow, CapitalLevel, PopulationLevel,
            SupportLevel, ReserveLevel, Population, CampId, SiteTile;
        public double CapitalQ, ReserveTurns, InitialFood, InitialFieldQ,
            InitialFishingQ, InitialFieldCapital, InitialFishingCapital,
            InitialHerdAnimals, HerdZeroDisplacementCapacity,
            HerdAbsoluteCapacity, LandFood, LandBiomass,
            LandRenewalPerYear, AquaticFood, AquaticBiomass,
            AquaticRenewalPerYear, Freshwater, LandRank, AquaticRank,
            JointSupportRank;
        public int[] FieldTiles = Array.Empty<int>();
        public int[] FishingTiles = Array.Empty<int>();
        public byte[] Snapshot;
    }

    private sealed class PulseResult
    {
        public int RequestedPeople, AcceptedPeople;
        public double Share;
        public int BaselineRolePeople, TreatmentRolePeople;
        public int BaselineHerdTarget, TreatmentHerdTarget;
    }

    private sealed class TrajectoryRow
    {
        public string StateId, RunId, Arm, Domain, Intensity,
            InitialFingerprint;
        public int Seed, MatrixRow, CapitalLevel, PopulationLevel,
            SupportLevel, ReserveLevel, Turn, PriorPopulation,
            EndingPopulation, Farmers, Herders, Fishers, Gatherers,
            OtherAssigned, HerdAnimals, Fissions, DaughterPopulation,
            CampCount, Slaughtered, PulseRequestedPeople,
            PulseAcceptedPeople, HerdTarget, NewbornAnimals,
            HerdStarvationDeaths;
        public double PulseShare, StartYear, EndYear, ElapsedYears, QoL,
            WelfareRate, RawWelfare, FoodProduced, FieldFood, FishFood,
            AnimalFood, GatheredFood, FoodConsumed, FoodReserves,
            ReserveYears, FieldQMean, FieldQMax, FieldCapital,
            FishingQMean, FishingCapital, LandFood, LandBiomass,
            LandCondition, AquaticFood, AquaticBiomass,
            AquaticCondition, FieldBuildWorkerYears,
            FieldMaintenanceWorkerYears, FishingBuildWorkerYears,
            FishingMaintenanceWorkerYears, HerderWorkerYears,
            ManagementFulfilment, ManagedBreedingIntensity,
            HumanFoodDisplaced, SlaughterFood;
    }

    private sealed class RunResult
    {
        public string RunId, FinalFingerprint;
        public List<TrajectoryRow> Rows = new List<TrajectoryRow>();
    }

    private sealed class DiscountRow
    {
        public string StateId, RunId, Domain, Intensity, Horizon,
            SignClassification;
        public int Seed, MatrixRow, CapitalLevel, PopulationLevel,
            SupportLevel, ReserveLevel;
        public double PulseShare, PulseWorkerYears, Value,
            UndiscountedValue, Undiscounted400Value,
            DurableBreakEvenYears, DurablePositiveYears,
            MinimumCumulativeValue, MinimumCumulativeYear, IncrementalFood,
            IncrementalFieldBuildWorkerYears,
            IncrementalFishingBuildWorkerYears,
            IncrementalHerderWorkerYears, IncrementalHerdHeadsAt20Years,
            ImmediateWelfareDelta;
        public bool LongRunImproves, CorrectSign;
    }

    private sealed class PairedRow
    {
        public string StateId, RunId, Domain, Intensity;
        public int Seed, MatrixRow, Turn;
        public double PulseShare, EndYear, DeltaWelfareRate,
            DeltaRawWelfare, CumulativeUndiscounted, Cumulative100,
            Cumulative200, Cumulative400, DeltaFoodProduced,
            DeltaFieldFood, DeltaFishFood, DeltaAnimalFood,
            DeltaGatheredFood, DeltaFoodReserves, DeltaFarmers,
            DeltaHerders, DeltaFishers, DeltaGatherers,
            DeltaFieldBuildWorkerYears, DeltaFishingBuildWorkerYears,
            DeltaFieldQMean, DeltaFishingQMean, DeltaHerdAnimals,
            DeltaLandFood, DeltaAquaticFood, DeltaFissions,
            DeltaDaughterPopulation;
    }

    private static int Main(string[] args)
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
            var resources = LoadResources(options);
            Console.WriteLine("Investment-payback sweep: generating " +
                options.Seeds.Length + " disclosed exploration worlds");
            var banks = options.Seeds.Select(seed => BuildGeographyBank(
                seed, config, resources)).ToArray();

            var states = BuildStates(banks, options).ToArray();
            WriteStates(Path.Combine(options.Output, "initial_states.csv"), states);
            Console.WriteLine("State bank: " + states.Length +
                " matched initial snapshots");

            var runs = new List<RunResult>();
            foreach (var state in states)
            {
                var baseline = Run(state, "baseline", "none", "none", 0,
                    options.Turns, new PhaseExecution());
                runs.Add(baseline);
                foreach (string domain in Domains)
                    for (int intensity = 0; intensity < PulseShares.Length;
                         intensity++)
                        runs.Add(Run(state, "treatment", domain,
                            LevelNames[intensity], PulseShares[intensity],
                            options.Turns, new PhaseExecution()));
                Console.WriteLine("Completed " + state.StateId + " (" +
                    runs.Count + " trajectories total)");
            }

            var trajectoryRows = runs.SelectMany(run => run.Rows).ToArray();
            WriteTrajectories(Path.Combine(options.Output,
                "trajectories.csv"), trajectoryRows);
            var discounts = AnalyzeDiscounts(states, runs, options.Turns)
                .ToArray();
            WriteDiscounts(Path.Combine(options.Output,
                "discount_summary.csv"), discounts);
            var paired = PairTrajectories(states, runs).ToArray();
            WritePaired(Path.Combine(options.Output,
                "paired_trajectories.csv"), paired);

            string verification = "not requested";
            if (options.Verify)
                verification = Verify(states[0], runs, options.Turns);
            File.WriteAllText(Path.Combine(options.Output,
                "verification.txt"), verification + Environment.NewLine);
            WriteManifest(options, config, states, runs, discounts, paired,
                timer.Elapsed, verification);
            Console.WriteLine("DONE trajectories=" + runs.Count +
                " turns=" + trajectoryRows.Length + " seconds=" +
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
        var result = new Options
        {
            Repo = Path.GetFullPath(Value("--repo", ".")),
            Output = Path.GetFullPath(Value("--out",
                "TestResults/InvestmentPayback")),
            Turns = int.Parse(Value("--turns", "40"), Inv),
            Width = int.Parse(Value("--width", "128"), Inv),
            Height = int.Parse(Value("--height", "64"), Inv),
            Quick = args.Contains("--quick"),
            Verify = args.Contains("--verify")
        };
        string seeds = Value("--seeds", string.Join(",", DefaultSeeds));
        result.Seeds = seeds.Split(',').Select(value =>
            int.Parse(value, Inv)).ToArray();
        if (result.Turns < 1 || result.Width < 32 || result.Height < 16 ||
            result.Seeds.Length < 1)
            throw new ArgumentException("Invalid bounded sweep dimensions.");
        if (result.Quick)
        {
            result.Seeds = result.Seeds.Take(1).ToArray();
            result.Turns = Math.Min(result.Turns, 6);
        }
        return result;
    }

    private static SimulationConfig LoadConfig(Options options)
    {
        string path = Path.Combine(options.Repo,
            "Assets/_Anthropocene/Resources/OpeningConfig.json");
        var config = JsonSerializer.Deserialize<SimulationConfig>(
            File.ReadAllText(path), Json) ?? throw new InvalidOperationException(
                "OpeningConfig could not be read.");
        // Experimental scale only: every physical/demographic/economic setting
        // remains the production value loaded above.
        config.Width = options.Width;
        config.Height = options.Height;
        config.Players = 1;
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

    private static GeographyBank BuildGeographyBank(int seed,
        SimulationConfig baseConfig, ResourceGenerationOptions resources)
    {
        var config = CloneConfig(baseConfig);
        var world = new WorldGenerator().Generate(seed, config, resources);
        var sites = SelectSites(world);
        if (sites.Length != 3)
            throw new InvalidOperationException("Seed " + seed +
                " did not yield the three controlled support strata.");
        world.Camps.Clear();
        world.Societies.Clear();
        world.Hunters.Clear();
        world.Herds.Clear();
        world.Improvements.Clear();
        world.FixedHousing.Clear();
        world.FisherySites.Clear();
        world.PlayerCount = 1;
        world.Config.Players = 1;
        return new GeographyBank
        {
            Seed = seed,
            Snapshot = SaveCodec.Save(world),
            Sites = sites
        };
    }

    private static SiteCandidate[] SelectSites(WorldState world)
    {
        bool FishingWater(int id) =>
            world.Tiles[id].Environment == EnvironmentKind.Shallows ||
            world.Tiles[id].Environment == EnvironmentKind.Lake;
        var candidates = new List<SiteCandidate>();
        foreach (var tile in world.Tiles.Where(tile =>
                     tile.TraversableLand &&
                     tile.Environment != EnvironmentKind.Ice))
        {
            int[] footprint = world.Map.Radius(tile.Id, 1)
                .Where(id => CultivationEconomy.Cultivable(world.Tiles[id]))
                .ToArray();
            if (footprint.Length < 4 ||
                !world.Map.Neighbors(tile.Id).Any(FishingWater)) continue;
            int[] water = world.Map.Radius(tile.Id, world.Config.VisionRadius)
                .Where(FishingWater).ToArray();
            if (water.Length < 2) continue;
            double fresh = FreshwaterAccess.Available(world, tile.Id);
            if (!(fresh > 0)) continue;
            double landFood = 0, landBiomass = 0, landRenewal = 0;
            foreach (int id in footprint)
            {
                var ecology = LazyEcology.ProjectEcology(world, id,
                    world.Days);
                landFood += Math.Max(0, ecology.Food);
                landBiomass += Math.Max(0, ecology.PotentialFood);
                landRenewal += world.Tiles[id].Seasons.Average(season =>
                    Math.Max(0, season.MaxFood * season.FoodRecoveryPerDay)) *
                    world.Config.DaysPerYear;
            }
            double aquaticFood = 0, aquaticBiomass = 0,
                aquaticRenewal = 0;
            foreach (int id in water)
            {
                var ecology = LazyEcology.ProjectEcology(world, id,
                    world.Days);
                aquaticFood += Math.Max(0, ecology.Food);
                aquaticBiomass += Math.Max(0, ecology.PotentialFood);
                aquaticRenewal += world.Tiles[id].Seasons.Average(season =>
                    Math.Max(0, season.MaxFood * season.FoodRecoveryPerDay)) *
                    world.Config.DaysPerYear;
            }
            if (!(landBiomass > 0) || !(aquaticBiomass > 0)) continue;
            candidates.Add(new SiteCandidate
            {
                Tile = tile.Id,
                LandTiles = footprint.Length,
                FishingTiles = water.Length,
                LandFood = landFood,
                LandBiomass = landBiomass,
                LandRenewalPerYear = landRenewal,
                AquaticFood = aquaticFood,
                AquaticBiomass = aquaticBiomass,
                AquaticRenewalPerYear = aquaticRenewal,
                Freshwater = fresh
            });
        }
        if (candidates.Count < 12)
            throw new InvalidOperationException("Too few mixed land/fishing sites: " +
                candidates.Count);
        AssignRanks(candidates, candidate => candidate.LandRenewalPerYear,
            (candidate, rank) => candidate.LandRank = rank);
        AssignRanks(candidates, candidate => candidate.AquaticRenewalPerYear,
            (candidate, rank) => candidate.AquaticRank = rank);
        foreach (var candidate in candidates)
            candidate.JointRank = Math.Sqrt(candidate.LandRank *
                candidate.AquaticRank);
        var ranked = candidates.OrderBy(candidate => candidate.JointRank)
            .ThenBy(candidate => candidate.Tile).ToArray();
        double[] targets = { .20, .50, .80 };
        return targets.Select(target => ranked[(int)Math.Round(target *
            (ranked.Length - 1), MidpointRounding.AwayFromZero)]).ToArray();
    }

    private static void AssignRanks(List<SiteCandidate> candidates,
        Func<SiteCandidate, double> value,
        Action<SiteCandidate, double> assign)
    {
        var ordered = candidates.OrderBy(value).ThenBy(candidate =>
            candidate.Tile).ToArray();
        for (int i = 0; i < ordered.Length; i++)
            assign(ordered[i], ordered.Length == 1 ? 1 :
                (i + .5) / ordered.Length);
    }

    private static IEnumerable<StateSpec> BuildStates(
        GeographyBank[] banks, Options options)
    {
        int bankIndex = 0;
        foreach (var bank in banks)
        {
            for (int row = 0; row < (options.Quick ? 3 : 9); row++)
            {
                int capital = L9[row, 0], population = L9[row, 1],
                    support = L9[row, 2], reserve = L9[row, 3];
                string stateId = "S" + (bankIndex + 1).ToString("00", Inv) +
                    "-R" + (row + 1).ToString("00", Inv);
                yield return BuildState(bank, stateId, row + 1, capital,
                    population, support, reserve);
            }
            bankIndex++;
        }
    }

    private static StateSpec BuildState(GeographyBank bank, string stateId,
        int matrixRow, int capitalLevel, int populationLevel,
        int supportLevel, int reserveLevel)
    {
        var world = SaveCodec.Load(bank.Snapshot);
        var site = bank.Sites[supportLevel];
        int population = Populations[populationLevel];
        var camp = new Camp
        {
            Id = world.NextId++,
            Player = 0,
            Tile = site.Tile,
            Population = population,
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
        double foodPerTurn = population * world.Config.Consumption *
            world.Config.GenerationIntervals;
        camp.PlantFood = foodPerTurn * ReserveTurns[reserveLevel];
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
        foreach (int id in world.Map.Radius(camp.Tile,
                     Math.Max(3, world.Config.VisionRadius)))
            camp.People.Memory.Explored.Add(id, world.Turn);

        double stock = 2 * population;
        camp.Inventory.HandTools = stock;
        camp.Inventory.FarmTools = stock;
        camp.Inventory.HerdingTools = stock;
        camp.Inventory.FishingTools = stock;
        camp.Inventory.Tools = stock;
        camp.Inventory.Cloth = stock;
        camp.Inventory.Stone = 20 * population;
        camp.Inventory.Wood = 20 * population;
        camp.Inventory.Clay = 20 * population;
        NeolithicProduction.AddFixedHousing(world, camp,
            population / world.Config.Neolithic.HousingPeoplePerUnit,
            world.Days);
        double naturalStorage = NeolithicProduction.NaturalStorageCapacity(
            world, camp, 0);
        camp.Inventory.Pots = Math.Max(
            NeolithicProduction.TargetPots(world, camp,
                world.Config.TurnDays / world.Config.DaysPerYear),
            Math.Max(0, camp.Food - naturalStorage) /
            world.Config.Neolithic.PotStorageFoodPerUnit);

        double q = CapitalQ[capitalLevel];
        double capital = Math.Pow(q / world.Config.ReferenceIntensity, 2);
        int[] fieldTiles = CultivationEconomy.Footprint(world, camp);
        foreach (int id in fieldTiles)
        {
            var tile = world.Tiles[id];
            tile.CultivationOperator = camp.Id;
            tile.CultivationCapital = capital;
            tile.CultivationOperatingFraction = 1;
            tile.Cultivation = (float)q;
            tile.Cultivated = true;
        }

        var access = FishingEconomy.BuildAccess(world, camp, camp.Tile,
            camp.Population, world.Days, world.Config.TurnDays);
        if (access.Grounds.Length == 0)
            throw new InvalidOperationException(stateId +
                " has no resident Fishing ground.");
        int[] fishingTiles = access.Grounds.Select(ground => ground.TileId)
            .OrderBy(id => id).ToArray();
        foreach (int id in fishingTiles)
            world.FisherySites.Add(new FisherySite
            {
                TileId = id,
                OperatorCampId = camp.Id,
                Capital = capital,
                LastUpdatedDays = world.Days
            });
        camp.LastSelectedFishingGrounds = fishingTiles.ToList();

        string species = world.Config.Species.Where(item => item.Domesticable)
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .First().Id;
        var herd = new Herd
        {
            Id = world.NextId++,
            Tile = camp.Tile,
            DomesticCamp = camp.Id,
            OwnerSociety = camp.SocietyId,
            Species = species,
            Animals = 1,
            Capacity = world.Config.GetSpecies(species).MaximumHerdSize
        };
        world.Herds.Add(herd);
        var survey = LocalHerdEcology.Survey(world, camp.Tile, 1,
            world.Config.TurnDays);
        var support = LocalHerdEcology.Support(survey,
            HerdConsumptionProfile.Owned(world, camp),
            world.Config.GenerationIntervals);
        double managementCapacity = HerdingTooling.AnimalsManagedBy(world,
            camp, world.Config.TurnDays / world.Config.DaysPerYear,
            Math.Max(1, population / 4));
        double herdCeiling = Math.Min(support.ZeroDisplacementCapacity,
            managementCapacity);
        int animals = Math.Max(2, (int)Math.Floor(herdCeiling *
            HerdAssetFractions[capitalLevel]));
        animals = Math.Min(animals, Math.Max(2,
            world.Config.GetSpecies(species).MaximumHerdSize));
        herd.Animals = animals;
        herd.Capacity = Math.Max(animals,
            world.Config.GetSpecies(species).MaximumHerdSize);
        camp.HerdTargetSet = true;
        camp.HerdTarget = animals;

        FisheryState.Normalize(world);
        NeolithicProduction.NormalizeFixedHousing(world);
        byte[] snapshot = SaveCodec.Save(world);
        world = SaveCodec.Load(snapshot);
        camp = world.Camps.Single(item => item.Id == camp.Id);
        snapshot = SaveCodec.Save(world);
        return new StateSpec
        {
            StateId = stateId,
            Seed = bank.Seed,
            MatrixRow = matrixRow,
            CapitalLevel = capitalLevel,
            PopulationLevel = populationLevel,
            SupportLevel = supportLevel,
            ReserveLevel = reserveLevel,
            Population = population,
            CampId = camp.Id,
            SiteTile = site.Tile,
            CapitalQ = q,
            ReserveTurns = ReserveTurns[reserveLevel],
            InitialFood = camp.Food,
            InitialFieldQ = MeanFieldQ(world, fieldTiles),
            InitialFishingQ = MeanFishingQ(world, fishingTiles),
            InitialFieldCapital = fieldTiles.Sum(id =>
                world.Tiles[id].CultivationCapital),
            InitialFishingCapital = world.FisherySites.Where(item =>
                fishingTiles.Contains(item.TileId)).Sum(item => item.Capital),
            InitialHerdAnimals = animals,
            HerdZeroDisplacementCapacity = support.ZeroDisplacementCapacity,
            HerdAbsoluteCapacity = support.AbsoluteCapacity,
            LandFood = site.LandFood,
            LandBiomass = site.LandBiomass,
            LandRenewalPerYear = site.LandRenewalPerYear,
            AquaticFood = site.AquaticFood,
            AquaticBiomass = site.AquaticBiomass,
            AquaticRenewalPerYear = site.AquaticRenewalPerYear,
            Freshwater = site.Freshwater,
            LandRank = site.LandRank,
            AquaticRank = site.AquaticRank,
            JointSupportRank = site.JointRank,
            FieldTiles = fieldTiles,
            FishingTiles = fishingTiles,
            Snapshot = snapshot,
            InitialFingerprint = SaveCodec.Fingerprint(world),
            ConfigurationHash = SaveCodec.ConfigurationHash(world.Config)
        };
    }

    private static RunResult Run(StateSpec state, string arm, string domain,
        string intensity, double pulseShare, int turns,
        PhaseExecution execution)
    {
        var world = SaveCodec.Load(state.Snapshot);
        if (SaveCodec.Fingerprint(world) != state.InitialFingerprint)
            throw new InvalidOperationException("Initial state fingerprint drift for " +
                state.StateId);
        string runId = state.StateId + ":" + arm + ":" + domain + ":" +
            intensity;
        var result = new RunResult { RunId = runId };
        for (int step = 0; step < turns; step++)
        {
            var camp = world.Camps.FirstOrDefault(item =>
                item.Id == state.CampId);
            if (camp == null || camp.Population <= 0 ||
                world.Camps.All(item => world.LinkedPopulation(item) <= 0))
            {
                AddZeroRows(result, state, runId, arm, domain, intensity,
                    pulseShare, world, step, turns);
                break;
            }
            int priorPopulation = camp.Population;
            double startYear = world.Days / world.Config.DaysPerYear;
            var draft = TurnPlanner.Draft(world, camp.Player, execution);
            if (!draft.Intents.TryGetValue(camp.Id, out var controlledDraft))
                throw new InvalidOperationException("No automatic controlled intent for " +
                    runId + " at turn " + step);
            PulseResult pulse = new PulseResult { Share = pulseShare };
            if (arm == "treatment" && step == 0)
                pulse = ApplyPulse(world, camp, controlledDraft, domain,
                    pulseShare);
            var intent = controlledDraft.Order.Economy;
            int farmers = Role(intent, WorkforceRoles.Farmer);
            int herders = Role(intent, WorkforceRoles.Herder);
            int fishers = Role(intent, WorkforceRoles.Fisher);
            int explicitGatherers = Role(intent, WorkforceRoles.Gatherer);
            int gatherers = Math.Max(0, camp.Population -
                intent.Assignments.Sum(item => item.Population)) +
                explicitGatherers;
            int other = intent.Assignments.Where(item => item.Role !=
                    WorkforceRoles.Farmer && item.Role != WorkforceRoles.Herder &&
                    item.Role != WorkforceRoles.Fisher && item.Role !=
                    WorkforceRoles.Gatherer)
                .Sum(item => item.Population);
            var prepared = TurnPlanner.Prepare(world, draft.Ai.Orders,
                draft.Ai.Traces, execution, draft);
            prepared.FishingPlans.TryGetValue(camp.Id, out var fishingPlan);
            var fishingGrounds = fishingPlan?.Grounds ??
                Array.Empty<FishingGroundPlan>();
            var management = HerdingTooling.Plan(world, camp,
                prepared.Horizon.Years, herders);
            var resolver = new TurnResolver(execution);
            var record = resolver.Resolve(world, prepared);
            camp = world.Camps.FirstOrDefault(item => item.Id == state.CampId);
            double years = record.Duration / world.Config.DaysPerYear;
            double quality = camp == null ? 0 :
                ElderRLContract.Unit(camp.QoL);
            var demographic = record.Demographics.FirstOrDefault(item =>
                item.Camp == state.CampId);
            var herdReports = record.ManagedHerds.Where(item =>
                item.Camp == state.CampId).ToArray();
            var controlledHerdIds = new HashSet<int>(herdReports.Select(item =>
                item.Herd));
            var fieldWork = resolver.LastMetrics.CraftFieldWork != null &&
                    resolver.LastMetrics.CraftFieldWork.TryGetValue(state.CampId,
                        out var work)
                ? work : null;
            int endingPopulation = camp?.Population ?? 0;
            int herdAnimals = world.Herds.Where(herd =>
                    herd.DomesticCamp == state.CampId)
                .Sum(herd => Math.Max(0, herd.Animals));
            var fissions = (record.Fissions ?? new List<FissionReport>())
                .Where(item => item.ParentCamp == state.CampId).ToArray();
            var ecology = Ecology(world, state);
            result.Rows.Add(new TrajectoryRow
            {
                StateId = state.StateId,
                RunId = runId,
                Arm = arm,
                Domain = domain,
                Intensity = intensity,
                InitialFingerprint = state.InitialFingerprint,
                Seed = state.Seed,
                MatrixRow = state.MatrixRow,
                CapitalLevel = state.CapitalLevel,
                PopulationLevel = state.PopulationLevel,
                SupportLevel = state.SupportLevel,
                ReserveLevel = state.ReserveLevel,
                Turn = step + 1,
                PriorPopulation = priorPopulation,
                EndingPopulation = endingPopulation,
                Farmers = farmers,
                Herders = herders,
                Fishers = fishers,
                Gatherers = gatherers,
                OtherAssigned = other,
                HerdAnimals = herdAnimals,
                Fissions = fissions.Length,
                DaughterPopulation = fissions.Sum(item => item.Daughter),
                CampCount = world.Camps.Count(item => item.Population > 0),
                Slaughtered = demographic?.Slaughtered ?? 0,
                NewbornAnimals = herdReports.Sum(item => item.NewbornAnimals),
                HerdStarvationDeaths = herdReports.Sum(item =>
                    item.StarvationDeaths),
                PulseRequestedPeople = pulse.RequestedPeople,
                PulseAcceptedPeople = pulse.AcceptedPeople,
                HerdTarget = intent.HerdTargetSet ? intent.HerdTarget : -1,
                PulseShare = pulseShare,
                StartYear = startYear,
                EndYear = world.Days / world.Config.DaysPerYear,
                ElapsedYears = years,
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
                AnimalFood = record.Transfers.Where(transfer =>
                    transfer.Entity == state.CampId && transfer.Kind ==
                    "Animal Product").Sum(transfer => transfer.Food),
                GatheredFood = demographic?.ForageFood ?? 0,
                FoodConsumed = demographic?.FoodConsumed ?? 0,
                FoodReserves = camp?.Food ?? 0,
                ReserveYears = camp == null || camp.Population <= 0 ? 0 :
                    camp.Food / Math.Max(1e-12,
                        PhysicalFoodTimebase.StockForYears(world,
                            camp.Population, 1)),
                FieldQMean = ecology.FieldQMean,
                FieldQMax = ecology.FieldQMax,
                FieldCapital = ecology.FieldCapital,
                FishingQMean = ecology.FishingQMean,
                FishingCapital = ecology.FishingCapital,
                LandFood = ecology.LandFood,
                LandBiomass = ecology.LandBiomass,
                LandCondition = ecology.LandCondition,
                AquaticFood = ecology.AquaticFood,
                AquaticBiomass = ecology.AquaticBiomass,
                AquaticCondition = ecology.AquaticCondition,
                FieldBuildWorkerYears = fieldWork?.ExpansionWorkUsedWorkerYears ?? 0,
                FieldMaintenanceWorkerYears = fieldWork?.MaintenanceWorkerYears ?? 0,
                FishingBuildWorkerYears = fishingGrounds.Sum(ground =>
                    ground.BuildWorkerYears),
                FishingMaintenanceWorkerYears = fishingGrounds.Sum(ground =>
                    ground.MaintenanceWorkerYears),
                HerderWorkerYears = herders * years,
                ManagementFulfilment = management.Fulfillment,
                ManagedBreedingIntensity = intent.ManagedBreedingIntensity,
                HumanFoodDisplaced = record.Pressure.Where(entry =>
                    controlledHerdIds.Contains(entry.Entity) &&
                    entry.Kind == "Herd")
                    .Sum(entry => entry.GatherableSpill),
                SlaughterFood = demographic?.SlaughterFood ?? 0
            });
        }
        result.FinalFingerprint = SaveCodec.Fingerprint(world);
        return result;
    }

    private static void AddZeroRows(RunResult result, StateSpec state,
        string runId, string arm, string domain, string intensity,
        double pulseShare, WorldState world, int first, int turns)
    {
        double years = world.Config.TurnDays / world.Config.DaysPerYear;
        for (int step = first; step < turns; step++)
            result.Rows.Add(new TrajectoryRow
            {
                StateId = state.StateId,
                RunId = runId,
                Arm = arm,
                Domain = domain,
                Intensity = intensity,
                InitialFingerprint = state.InitialFingerprint,
                Seed = state.Seed,
                MatrixRow = state.MatrixRow,
                CapitalLevel = state.CapitalLevel,
                PopulationLevel = state.PopulationLevel,
                SupportLevel = state.SupportLevel,
                ReserveLevel = state.ReserveLevel,
                Turn = step + 1,
                PulseShare = pulseShare,
                StartYear = step * years,
                EndYear = (step + 1) * years,
                ElapsedYears = years
            });
    }

    private static PulseResult ApplyPulse(WorldState world, Camp camp,
        DraftIntent draft, string domain, double share)
    {
        var intent = draft.Order.Economy.Copy();
        string role = domain == "field" ? WorkforceRoles.Farmer :
            domain == "herd" ? WorkforceRoles.Herder :
            domain == "fishing" ? WorkforceRoles.Fisher :
            throw new ArgumentException("Unknown investment domain: " + domain);
        int requested = Math.Max(1, (int)Math.Round(camp.Population * share,
            MidpointRounding.AwayFromZero));
        int baselineRole = Role(intent, role);
        int assigned = intent.Assignments.Sum(item => item.Population);
        int implicitGatherers = Math.Max(0, camp.Population - assigned);
        var explicitGatherer = intent.Assignments.FirstOrDefault(item =>
            item.Role == WorkforceRoles.Gatherer);
        int explicitCount = explicitGatherer?.Population ?? 0;
        int accepted = Math.Min(requested,
            implicitGatherers + explicitCount);
        int fromExplicit = Math.Max(0, accepted - implicitGatherers);
        if (fromExplicit > 0)
        {
            explicitGatherer.Population -= fromExplicit;
            if (explicitGatherer.Population == 0)
                intent.Assignments.Remove(explicitGatherer);
        }
        var target = intent.Assignments.FirstOrDefault(item =>
            item.Role == role);
        if (accepted > 0)
        {
            if (target == null)
                intent.Assignments.Add(new PopulationAssignment
                {
                    Role = role,
                    Population = accepted
                });
            else target.Population += accepted;
        }
        int treatmentRole = baselineRole + accepted;
        int baselineHerdTarget = intent.HerdTargetSet ? intent.HerdTarget : -1;
        if (intent.Labor != null)
        {
            int nonGatherers = intent.Assignments.Where(item => item.Role !=
                    WorkforceRoles.Gatherer)
                .Sum(item => item.Population);
            intent.Labor.MaterialGatherers = Math.Min(
                intent.Labor.MaterialGatherers,
                Math.Max(0, camp.Population - nonGatherers));
        }
        if (domain == "field")
        {
            intent.Labor ??= new EconomicLaborPlan();
            intent.Labor.ExpansionFarmers = Math.Min(treatmentRole,
                Math.Max(0, intent.Labor.ExpansionFarmers) + accepted);
        }
        if (domain == "herd")
        {
            double years = world.Config.TurnDays /
                world.Config.DaysPerYear;
            double scale = Numbers.Clamp(share / PulseShares.Last(), 0, 1);
            intent.ManagedBreedingIntensity = Numbers.Clamp(
                intent.ManagedBreedingIntensity + scale *
                    (1 - intent.ManagedBreedingIntensity), 0, 1);
            var grown = TurnResolver.ForecastGrownManagedAnimalFood(world,
                camp, intent.ManagedBreedingIntensity,
                world.Config.GenerationIntervals, years, camp.Tile);
            double biological = Math.Max(HerdingTooling.DomesticAnimals(world,
                camp), grown.Animals);
            double managed = HerdingTooling.AnimalsManagedBy(world, camp,
                years, treatmentRole);
            int targetAnimals = (int)Math.Floor(Math.Min(biological, managed) +
                1e-9);
            intent.HerdTargetSet = true;
            intent.HerdTarget = Math.Max(intent.HerdTarget,
                Math.Max(0, targetAnimals));
            intent.ManagedBreedingIntensity = HerdLabour.CapBreeding(world,
                camp, intent.ManagedBreedingIntensity, intent.HerdTarget,
                years);
        }
        var constraints = new EconomicConstraints
        {
            Agriculture = CultivationEconomy.Enabled(camp),
            Husbandry = CultivationEconomy.HusbandryEnabled(camp),
            FishingEnabled = FishingEconomy.FeatureEnabled(world, camp),
            Population = camp.Population,
            Technologies = camp.People.NeolithicTechnologies.ToArray(),
            FocusTiles = CultivationEconomy.Footprint(world, camp)
        };
        string error = EconomicIntents.Validate(constraints, intent);
        if (error != null)
            throw new InvalidOperationException("Experimental pulse is invalid: " +
                error);
        draft.Order = new Order(OrderKind.Economy, camp.Id)
        {
            Economy = intent
        };
        return new PulseResult
        {
            RequestedPeople = requested,
            AcceptedPeople = accepted,
            Share = share,
            BaselineRolePeople = baselineRole,
            TreatmentRolePeople = treatmentRole,
            BaselineHerdTarget = baselineHerdTarget,
            TreatmentHerdTarget = intent.HerdTargetSet ? intent.HerdTarget : -1
        };
    }

    private static int Role(EconomicIntent intent, string role)
        => intent?.Assignments?.Where(item => item != null && item.Role == role)
            .Sum(item => item.Population) ?? 0;

    private sealed class EcologyMetrics
    {
        public double FieldQMean, FieldQMax, FieldCapital,
            FishingQMean, FishingCapital, LandFood, LandBiomass,
            LandCondition, AquaticFood, AquaticBiomass,
            AquaticCondition;
    }

    private static EcologyMetrics Ecology(WorldState world, StateSpec state)
    {
        var result = new EcologyMetrics();
        int landCount = 0;
        foreach (int id in state.FieldTiles)
        {
            var ecology = LazyEcology.ProjectEcology(world, id, world.Days);
            double q = CultivationEconomy.Intensity(world.Config,
                ecology.CultivationCapital);
            result.FieldQMean += q;
            result.FieldQMax = Math.Max(result.FieldQMax, q);
            result.FieldCapital += ecology.CultivationCapital;
            result.LandFood += ecology.Food;
            result.LandBiomass += ecology.PotentialFood;
            result.LandCondition += ecology.LandCondition;
            landCount++;
        }
        if (landCount > 0)
        {
            result.FieldQMean /= landCount;
            result.LandCondition /= landCount;
        }
        int fishingCount = 0;
        foreach (int id in state.FishingTiles)
        {
            var ecology = LazyEcology.ProjectEcology(world, id, world.Days);
            var site = world.FisherySites.FirstOrDefault(item =>
                item.TileId == id);
            double capital = site?.Capital ?? 0;
            result.FishingQMean += FishingEconomy.Intensity(world.Config,
                capital);
            result.FishingCapital += capital;
            result.AquaticFood += ecology.Food;
            result.AquaticBiomass += ecology.PotentialFood;
            result.AquaticCondition += ecology.AquaticCondition;
            fishingCount++;
        }
        if (fishingCount > 0)
        {
            result.FishingQMean /= fishingCount;
            result.AquaticCondition /= fishingCount;
        }
        return result;
    }

    private static double MeanFieldQ(WorldState world, int[] tiles)
        => tiles.Length == 0 ? 0 : tiles.Average(id =>
            CultivationEconomy.Intensity(world.Config,
                world.Tiles[id].CultivationCapital));

    private static double MeanFishingQ(WorldState world, int[] tiles)
        => tiles.Length == 0 ? 0 : tiles.Average(id =>
            FishingEconomy.Intensity(world.Config,
                world.FisherySites.FirstOrDefault(site =>
                    site.TileId == id)?.Capital ?? 0));

    private static IEnumerable<DiscountRow> AnalyzeDiscounts(
        StateSpec[] states, List<RunResult> runs, int turns)
    {
        foreach (var state in states)
        {
            var baseline = runs.Single(run => run.RunId == state.StateId +
                ":baseline:none:none");
            foreach (var treatment in runs.Where(run => run.RunId.StartsWith(
                         state.StateId + ":treatment:",
                         StringComparison.Ordinal)))
            {
                var first = treatment.Rows[0];
                double undiscounted = 0, undiscounted400 = 0,
                    incrementalFood = 0, fieldBuild = 0, fishingBuild = 0,
                    herderWork = 0;
                var deltaRates = new double[turns];
                for (int i = 0; i < turns; i++)
                {
                    var treated = treatment.Rows[i];
                    var control = baseline.Rows[i];
                    double deltaRate = treated.WelfareRate -
                        control.WelfareRate;
                    deltaRates[i] = deltaRate;
                    undiscounted += deltaRate * treated.ElapsedYears;
                    if (treated.EndYear <= 400 + 1e-9)
                        undiscounted400 += deltaRate * treated.ElapsedYears;
                    incrementalFood += treated.FoodProduced -
                        control.FoodProduced;
                    fieldBuild += treated.FieldBuildWorkerYears -
                        control.FieldBuildWorkerYears;
                    fishingBuild += treated.FishingBuildWorkerYears -
                        control.FishingBuildWorkerYears;
                    herderWork += treated.HerderWorkerYears -
                        control.HerderWorkerYears;
                }
                foreach (double horizon in new[]
                         {
                             double.PositiveInfinity, 100d, 200d, 400d
                         })
                {
                    double value = 0;
                    var cumulative = new double[turns];
                    for (int i = 0; i < turns; i++)
                    {
                        double start = treatment.Rows[i].StartYear;
                        double end = treatment.Rows[i].EndYear;
                        double weight = double.IsPositiveInfinity(horizon)
                            ? end - start
                            : horizon * (Math.Exp(-start / horizon) -
                                Math.Exp(-end / horizon));
                        value += deltaRates[i] * weight;
                        cumulative[i] = value;
                    }
                    bool improves = undiscounted > 1e-8;
                    bool discountedPositive = value > 1e-8;
                    int minimumIndex = Array.IndexOf(cumulative,
                        cumulative.Min());
                    string classification = improves
                        ? discountedPositive ? "true-positive" :
                            "false-negative"
                        : discountedPositive ? "false-positive" :
                            "true-nonpositive";
                    yield return new DiscountRow
                    {
                        StateId = state.StateId,
                        RunId = treatment.RunId,
                        Domain = first.Domain,
                        Intensity = first.Intensity,
                        Horizon = double.IsPositiveInfinity(horizon)
                            ? "undiscounted" : horizon.ToString("0", Inv),
                        SignClassification = classification,
                        Seed = state.Seed,
                        MatrixRow = state.MatrixRow,
                        CapitalLevel = state.CapitalLevel,
                        PopulationLevel = state.PopulationLevel,
                        SupportLevel = state.SupportLevel,
                        ReserveLevel = state.ReserveLevel,
                        PulseShare = first.PulseShare,
                        PulseWorkerYears = first.PulseAcceptedPeople *
                            first.ElapsedYears,
                        Value = value,
                        UndiscountedValue = undiscounted,
                        Undiscounted400Value = undiscounted400,
                        DurableBreakEvenYears = DurableBreakEven(cumulative,
                            treatment.Rows.Select(row => row.EndYear).ToArray()),
                        DurablePositiveYears = DurablePositive(cumulative,
                            treatment.Rows.Select(row => row.EndYear).ToArray()),
                        MinimumCumulativeValue = cumulative[minimumIndex],
                        MinimumCumulativeYear = treatment.Rows[minimumIndex]
                            .EndYear,
                        IncrementalFood = incrementalFood,
                        IncrementalFieldBuildWorkerYears = fieldBuild,
                        IncrementalFishingBuildWorkerYears = fishingBuild,
                        IncrementalHerderWorkerYears = herderWork,
                        IncrementalHerdHeadsAt20Years =
                            treatment.Rows[0].HerdAnimals -
                            baseline.Rows[0].HerdAnimals,
                        ImmediateWelfareDelta = treatment.Rows[0].RawWelfare -
                            baseline.Rows[0].RawWelfare,
                        LongRunImproves = improves,
                        CorrectSign = improves == discountedPositive ||
                            !improves && !discountedPositive
                    };
                }
            }
        }
    }

    private static double DurableBreakEven(double[] cumulative,
        double[] endYears)
    {
        const double tolerance = 1e-8;
        if (cumulative.Length == 0 || cumulative[cumulative.Length - 1] <
            -tolerance) return -1;
        bool everNegative = cumulative.Any(value => value < -tolerance);
        if (!everNegative && cumulative[cumulative.Length - 1] >= -tolerance)
            return 0;
        for (int i = 0; i < cumulative.Length; i++)
            if (cumulative[i] >= -tolerance &&
                cumulative.Skip(i).All(value => value >= -tolerance))
                return endYears[i];
        return -1;
    }

    private static double DurablePositive(double[] cumulative,
        double[] endYears)
    {
        const double tolerance = 1e-8;
        if (cumulative.Length == 0 ||
            cumulative[cumulative.Length - 1] <= tolerance) return -1;
        for (int i = 0; i < cumulative.Length; i++)
            if (cumulative[i] > tolerance &&
                cumulative.Skip(i).All(value => value > tolerance))
                return endYears[i];
        return -1;
    }

    private static IEnumerable<PairedRow> PairTrajectories(
        StateSpec[] states, List<RunResult> runs)
    {
        foreach (var state in states)
        {
            var baseline = runs.Single(run => run.RunId == state.StateId +
                ":baseline:none:none");
            foreach (var treatment in runs.Where(run => run.RunId.StartsWith(
                         state.StateId + ":treatment:",
                         StringComparison.Ordinal)))
            {
                double cumulative = 0, cumulative100 = 0,
                    cumulative200 = 0, cumulative400 = 0;
                for (int i = 0; i < treatment.Rows.Count; i++)
                {
                    var treated = treatment.Rows[i];
                    var control = baseline.Rows[i];
                    double deltaRate = treated.WelfareRate -
                        control.WelfareRate;
                    double deltaRaw = deltaRate * treated.ElapsedYears;
                    cumulative += deltaRaw;
                    cumulative100 += deltaRate * DiscountWeight(
                        treated.StartYear, treated.EndYear, 100);
                    cumulative200 += deltaRate * DiscountWeight(
                        treated.StartYear, treated.EndYear, 200);
                    cumulative400 += deltaRate * DiscountWeight(
                        treated.StartYear, treated.EndYear, 400);
                    yield return new PairedRow
                    {
                        StateId = state.StateId,
                        RunId = treatment.RunId,
                        Domain = treated.Domain,
                        Intensity = treated.Intensity,
                        Seed = state.Seed,
                        MatrixRow = state.MatrixRow,
                        Turn = treated.Turn,
                        PulseShare = treated.PulseShare,
                        EndYear = treated.EndYear,
                        DeltaWelfareRate = deltaRate,
                        DeltaRawWelfare = deltaRaw,
                        CumulativeUndiscounted = cumulative,
                        Cumulative100 = cumulative100,
                        Cumulative200 = cumulative200,
                        Cumulative400 = cumulative400,
                        DeltaFoodProduced = treated.FoodProduced -
                            control.FoodProduced,
                        DeltaFieldFood = treated.FieldFood - control.FieldFood,
                        DeltaFishFood = treated.FishFood - control.FishFood,
                        DeltaAnimalFood = treated.AnimalFood -
                            control.AnimalFood,
                        DeltaGatheredFood = treated.GatheredFood -
                            control.GatheredFood,
                        DeltaFoodReserves = treated.FoodReserves -
                            control.FoodReserves,
                        DeltaFarmers = treated.Farmers - control.Farmers,
                        DeltaHerders = treated.Herders - control.Herders,
                        DeltaFishers = treated.Fishers - control.Fishers,
                        DeltaGatherers = treated.Gatherers - control.Gatherers,
                        DeltaFieldBuildWorkerYears =
                            treated.FieldBuildWorkerYears -
                            control.FieldBuildWorkerYears,
                        DeltaFishingBuildWorkerYears =
                            treated.FishingBuildWorkerYears -
                            control.FishingBuildWorkerYears,
                        DeltaFieldQMean = treated.FieldQMean -
                            control.FieldQMean,
                        DeltaFishingQMean = treated.FishingQMean -
                            control.FishingQMean,
                        DeltaHerdAnimals = treated.HerdAnimals -
                            control.HerdAnimals,
                        DeltaLandFood = treated.LandFood - control.LandFood,
                        DeltaAquaticFood = treated.AquaticFood -
                            control.AquaticFood,
                        DeltaFissions = treated.Fissions - control.Fissions,
                        DeltaDaughterPopulation = treated.DaughterPopulation -
                            control.DaughterPopulation
                    };
                }
            }
        }
    }

    private static double DiscountWeight(double start, double end,
        double horizon) => horizon * (Math.Exp(-start / horizon) -
            Math.Exp(-end / horizon));

    private static string Verify(StateSpec state, List<RunResult> runs,
        int turns)
    {
        var sentinels = new[]
        {
            ("baseline", "none", "none", 0d),
            ("treatment", "field", "high", .20),
            ("treatment", "herd", "high", .20),
            ("treatment", "fishing", "high", .20)
        };
        foreach (var sentinel in sentinels)
        {
            string id = state.StateId + ":" + sentinel.Item1 + ":" +
                sentinel.Item2 + ":" + sentinel.Item3;
            var expected = runs.Single(run => run.RunId == id);
            var replay = Run(state, sentinel.Item1, sentinel.Item2,
                sentinel.Item3, sentinel.Item4, turns,
                new PhaseExecution(4, 2, true, 1));
            if (expected.FinalFingerprint != replay.FinalFingerprint ||
                !expected.Rows.Select(row => row.RawWelfare).SequenceEqual(
                    replay.Rows.Select(row => row.RawWelfare)))
                throw new InvalidOperationException("Scalar/parallel replay drift: " +
                    id);
        }
        return "PASS: four scalar/reversed-parallel sentinel trajectories " +
            "matched final fingerprints and per-turn raw welfare.";
    }

    private static void WriteStates(string path, IEnumerable<StateSpec> states)
    {
        using var file = new StreamWriter(path, false, new UTF8Encoding(false));
        file.WriteLine("state_id,seed,matrix_row,capital_level,population_level,support_level,reserve_level,population,site_tile,capital_q,reserve_turns,initial_food,initial_field_q,initial_fishing_q,initial_field_capital,initial_fishing_capital,initial_herd_animals,herd_zero_displacement_capacity,herd_absolute_capacity,land_food,land_biomass,land_renewal_per_year,aquatic_food,aquatic_biomass,aquatic_renewal_per_year,freshwater,land_percentile_rank,aquatic_percentile_rank,joint_support_rank,field_tiles,fishing_tiles,initial_fingerprint,configuration_hash");
        foreach (var state in states)
            file.WriteLine(string.Join(",", new[]
            {
                S(state.StateId), I(state.Seed), I(state.MatrixRow),
                S(LevelNames[state.CapitalLevel]),
                S(LevelNames[state.PopulationLevel]),
                S(LevelNames[state.SupportLevel]),
                S(LevelNames[state.ReserveLevel]), I(state.Population),
                I(state.SiteTile), D(state.CapitalQ), D(state.ReserveTurns),
                D(state.InitialFood), D(state.InitialFieldQ),
                D(state.InitialFishingQ), D(state.InitialFieldCapital),
                D(state.InitialFishingCapital), D(state.InitialHerdAnimals),
                D(state.HerdZeroDisplacementCapacity),
                D(state.HerdAbsoluteCapacity), D(state.LandFood),
                D(state.LandBiomass), D(state.LandRenewalPerYear),
                D(state.AquaticFood), D(state.AquaticBiomass),
                D(state.AquaticRenewalPerYear), D(state.Freshwater),
                D(state.LandRank), D(state.AquaticRank),
                D(state.JointSupportRank),
                S(string.Join(";", state.FieldTiles)),
                S(string.Join(";", state.FishingTiles)),
                S(state.InitialFingerprint), S(state.ConfigurationHash)
            }));
    }

    private static void WriteTrajectories(string path,
        IEnumerable<TrajectoryRow> rows)
    {
        using var file = new StreamWriter(path, false, new UTF8Encoding(false));
        file.WriteLine("state_id,run_id,arm,domain,intensity,seed,matrix_row,capital_level,population_level,support_level,reserve_level,turn,start_year,end_year,elapsed_years,prior_population,ending_population,qol,welfare_rate,raw_welfare,food_produced,field_food,fish_food,animal_food,gathered_food,food_consumed,food_reserves,reserve_years,farmers,herders,fishers,gatherers,other_assigned,field_build_worker_years,field_maintenance_worker_years,fishing_build_worker_years,fishing_maintenance_worker_years,herder_worker_years,management_fulfilment,managed_breeding_intensity,human_food_displaced,field_q_mean,field_q_max,field_capital,fishing_q_mean,fishing_capital,herd_animals,newborn_animals,herd_starvation_deaths,land_food,land_biomass,land_condition,aquatic_food,aquatic_biomass,aquatic_condition,fissions,daughter_population,camp_count,slaughtered,slaughter_food,herd_target,pulse_share,pulse_requested_people,pulse_accepted_people,initial_fingerprint");
        foreach (var row in rows)
            file.WriteLine(string.Join(",", new[]
            {
                S(row.StateId), S(row.RunId), S(row.Arm), S(row.Domain),
                S(row.Intensity), I(row.Seed), I(row.MatrixRow),
                S(LevelNames[row.CapitalLevel]),
                S(LevelNames[row.PopulationLevel]),
                S(LevelNames[row.SupportLevel]),
                S(LevelNames[row.ReserveLevel]), I(row.Turn),
                D(row.StartYear), D(row.EndYear), D(row.ElapsedYears),
                I(row.PriorPopulation), I(row.EndingPopulation), D(row.QoL),
                D(row.WelfareRate), D(row.RawWelfare), D(row.FoodProduced),
                D(row.FieldFood), D(row.FishFood), D(row.AnimalFood),
                D(row.GatheredFood), D(row.FoodConsumed),
                D(row.FoodReserves), D(row.ReserveYears), I(row.Farmers),
                I(row.Herders), I(row.Fishers), I(row.Gatherers),
                I(row.OtherAssigned), D(row.FieldBuildWorkerYears),
                D(row.FieldMaintenanceWorkerYears),
                D(row.FishingBuildWorkerYears),
                D(row.FishingMaintenanceWorkerYears),
                D(row.HerderWorkerYears), D(row.ManagementFulfilment),
                D(row.ManagedBreedingIntensity), D(row.HumanFoodDisplaced),
                D(row.FieldQMean),
                D(row.FieldQMax), D(row.FieldCapital),
                D(row.FishingQMean), D(row.FishingCapital),
                I(row.HerdAnimals), I(row.NewbornAnimals),
                I(row.HerdStarvationDeaths), D(row.LandFood),
                D(row.LandBiomass),
                D(row.LandCondition), D(row.AquaticFood),
                D(row.AquaticBiomass), D(row.AquaticCondition),
                I(row.Fissions), I(row.DaughterPopulation), I(row.CampCount),
                I(row.Slaughtered), D(row.SlaughterFood), I(row.HerdTarget),
                D(row.PulseShare), I(row.PulseRequestedPeople),
                I(row.PulseAcceptedPeople), S(row.InitialFingerprint)
            }));
    }

    private static void WriteDiscounts(string path,
        IEnumerable<DiscountRow> rows)
    {
        using var file = new StreamWriter(path, false, new UTF8Encoding(false));
        file.WriteLine("state_id,run_id,domain,intensity,horizon,seed,matrix_row,capital_level,population_level,support_level,reserve_level,pulse_share,pulse_worker_years,value,undiscounted_value,undiscounted_400_value,durable_break_even_years,durable_positive_years,minimum_cumulative_value,minimum_cumulative_year,incremental_food,incremental_field_build_worker_years,incremental_fishing_build_worker_years,incremental_herder_worker_years,incremental_herd_heads_at_20_years,immediate_welfare_delta,long_run_improves,correct_sign,sign_classification");
        foreach (var row in rows)
            file.WriteLine(string.Join(",", new[]
            {
                S(row.StateId), S(row.RunId), S(row.Domain), S(row.Intensity),
                S(row.Horizon), I(row.Seed), I(row.MatrixRow),
                S(LevelNames[row.CapitalLevel]),
                S(LevelNames[row.PopulationLevel]),
                S(LevelNames[row.SupportLevel]),
                S(LevelNames[row.ReserveLevel]), D(row.PulseShare),
                D(row.PulseWorkerYears), D(row.Value),
                D(row.UndiscountedValue), D(row.Undiscounted400Value),
                D(row.DurableBreakEvenYears), D(row.DurablePositiveYears),
                D(row.MinimumCumulativeValue), D(row.MinimumCumulativeYear),
                D(row.IncrementalFood),
                D(row.IncrementalFieldBuildWorkerYears),
                D(row.IncrementalFishingBuildWorkerYears),
                D(row.IncrementalHerderWorkerYears),
                D(row.IncrementalHerdHeadsAt20Years),
                D(row.ImmediateWelfareDelta), B(row.LongRunImproves),
                B(row.CorrectSign), S(row.SignClassification)
            }));
    }

    private static void WritePaired(string path, IEnumerable<PairedRow> rows)
    {
        using var file = new StreamWriter(path, false, new UTF8Encoding(false));
        file.WriteLine("state_id,run_id,domain,intensity,seed,matrix_row,turn,end_year,pulse_share,delta_welfare_rate,delta_raw_welfare,cumulative_undiscounted,cumulative_100,cumulative_200,cumulative_400,delta_food_produced,delta_field_food,delta_fish_food,delta_animal_food,delta_gathered_food,delta_food_reserves,delta_farmers,delta_herders,delta_fishers,delta_gatherers,delta_field_build_worker_years,delta_fishing_build_worker_years,delta_field_q_mean,delta_fishing_q_mean,delta_herd_animals,delta_land_food,delta_aquatic_food,delta_fissions,delta_daughter_population");
        foreach (var row in rows)
            file.WriteLine(string.Join(",", new[]
            {
                S(row.StateId), S(row.RunId), S(row.Domain), S(row.Intensity),
                I(row.Seed), I(row.MatrixRow), I(row.Turn), D(row.EndYear),
                D(row.PulseShare), D(row.DeltaWelfareRate),
                D(row.DeltaRawWelfare), D(row.CumulativeUndiscounted),
                D(row.Cumulative100), D(row.Cumulative200),
                D(row.Cumulative400), D(row.DeltaFoodProduced),
                D(row.DeltaFieldFood), D(row.DeltaFishFood),
                D(row.DeltaAnimalFood), D(row.DeltaGatheredFood),
                D(row.DeltaFoodReserves), D(row.DeltaFarmers),
                D(row.DeltaHerders), D(row.DeltaFishers),
                D(row.DeltaGatherers), D(row.DeltaFieldBuildWorkerYears),
                D(row.DeltaFishingBuildWorkerYears),
                D(row.DeltaFieldQMean), D(row.DeltaFishingQMean),
                D(row.DeltaHerdAnimals), D(row.DeltaLandFood),
                D(row.DeltaAquaticFood), D(row.DeltaFissions),
                D(row.DeltaDaughterPopulation)
            }));
    }

    private static void WriteManifest(Options options, SimulationConfig config,
        StateSpec[] states, List<RunResult> runs, DiscountRow[] discounts,
        PairedRow[] paired, TimeSpan elapsed, string verification)
    {
        string Commit()
        {
            try
            {
                var start = new ProcessStartInfo("git",
                    "rev-parse HEAD")
                {
                    WorkingDirectory = options.Repo,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(start);
                string value = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();
                return process.ExitCode == 0 ? value : "unknown";
            }
            catch { return "unknown"; }
        }
        string Hash(string relative)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(Path.Combine(options.Repo,
                relative));
            return string.Concat(sha.ComputeHash(stream).Select(value =>
                value.ToString("x2", Inv)));
        }
        string SimulationSourceHash()
        {
            string root = Path.Combine(options.Repo,
                "Assets/_Anthropocene/Simulation");
            string ledger = string.Join("\n", Directory.GetFiles(root, "*.cs")
                .OrderBy(path => Path.GetFileName(path),
                    StringComparer.Ordinal)
                .Select(path => Path.GetFileName(path) + ":" +
                    Hash(Path.GetRelativePath(options.Repo, path))));
            using var sha = SHA256.Create();
            return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(ledger))
                .Select(value => value.ToString("x2", Inv)));
        }
        var manifest = new
        {
            harness = "physical-investment-payback-v1",
            normativeDocument = new
            {
                fileId = "1rZjIo7NMrxn3OQzGFHJI2QAZN7rauHgSCm-nbMNUCT0",
                revision = "54",
                modifiedUtc = "2026-10-10T06:01:53.849Z"
            },
            simulatorCommit = Commit(),
            simulatorVersion = NativeElderEnvironment.SimulatorVersion,
            simulationSourceSha256 = SimulationSourceHash(),
            harnessSourceSha256 = Hash(
                "Tools/InvestmentPayback/Program.cs"),
            configurationHash = SaveCodec.ConfigurationHash(config),
            openingConfigSha256 = Hash(
                "Assets/_Anthropocene/Resources/OpeningConfig.json"),
            resourceConfigSha256 = Hash(
                "Assets/_Anthropocene/Resources/ResourceGenerationConfig.json"),
            seeds = options.Seeds,
            map = new { options.Width, options.Height },
            decisions = options.Turns,
            years = options.Turns * config.TurnDays / config.DaysPerYear,
            turnDays = config.TurnDays,
            turnYears = config.TurnDays / config.DaysPerYear,
            stateDesign = "three-seed replication of L9(3^4): asset, population, ecological support, reserves",
            capitalQ = CapitalQ,
            herdAssetFractions = HerdAssetFractions,
            populations = Populations,
            reserveTurns = ReserveTurns,
            pulseShares = PulseShares,
            domains = Domains,
            discountEFoldingYears = new[] { 100, 200, 400 },
            stateCount = states.Length,
            trajectoryCount = runs.Count,
            trajectoryRows = runs.Sum(run => run.Rows.Count),
            discountRows = discounts.Length,
            pairedRows = paired.Length,
            initialFingerprints = states.ToDictionary(state => state.StateId,
                state => state.InitialFingerprint),
            verification,
            elapsedSeconds = elapsed.TotalSeconds,
            assumptions = new[]
            {
                "One 20-year extra-labour pulse is taken from implicit/explicit gatherers; all later decisions use the unchanged DemandControl policy.",
                "The husbandry pulse also raises first-turn breeding/retention toward full intensity in proportion to the nominal 5/10/20 percent treatment; births, forage, water, management, starvation and slaughter remain production equations.",
                "Initial sites are generated coastal mixed-subsistence states at joint land/aquatic support quantiles; site ranking is selection only, not an economic equation.",
                "All first-tier Neolithic craft capabilities and two population-scaled tool/service stocks are supplied equally to matched arms.",
                "The normative parent-local welfare boundary is prior resident population times endpoint realized QoL, held constant within each analytical turn.",
                "The 800-year continuation is the finite long-run reference; it is not an infinite-horizon proof or unseen validation set."
            }
        };
        File.WriteAllText(Path.Combine(options.Output, "manifest.json"),
            JsonSerializer.Serialize(manifest, Json));
    }

    private static string D(double value) =>
        value.ToString("R", Inv);
    private static string I(int value) => value.ToString(Inv);
    private static string B(bool value) => value ? "true" : "false";
    private static string S(string value)
    {
        value ??= string.Empty;
        return value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0
            ? value : "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
