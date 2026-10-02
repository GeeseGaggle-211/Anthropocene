using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Anthropocene.Simulation;

internal static class Program
{
    private const int StartingPopulation = 240;
    private const int Turns = 50;

    public sealed class RunSpec
    {
        public string Name = "";
        public string Archetype = "";
        public int Herd = 0;
        public double TentDecay = .10;
        public double FabricPerTent = 1;
        public double TentProductivity = 1;
        public double SpecialistMaxBonus = 0;
        public double SpecialistOnset = 100;
        public double SpecialistScale = 400;
    }

    public sealed class AggregateRow
    {
        public string Run, Archetype;
        public int Turn, Population, Tribes, CumulativeFissions, StarvationDeaths, CumulativeStarvationDeaths, QoLBlockedCampTurns, CumulativeCampTurns;
        public double P25Population, MedianPopulation, P75Population, MeanQoL, MedianQoL, HusbandryQoL, AgricultureQoL;
        public double FissionsPerThousand, MedianInterFissionTurns, ManagedAnimals, AnimalsPerPastoralResident;
        public double TerrestrialFood, AnimalFood, SlaughterFood, MarineFood, LakeFood, HuntFood, FishFood, ForageFood, TotalFoodProduced;
        public double MaintenanceFoodConsumed, GrowthFoodAccepted, ManufacturingFood;
        public double ClothingCoverage, HousingCoverage, SocialCohesion, TentDominantPopulationShare, FixedHousingPopulationShare;
        public double QoLMortalityBlockedFraction, TentChainFoodShare, TentChainSurplusFoodShare, TentChainLaborShare, HerdFoodShare;
    }

    public sealed class TribeRow
    {
        public string Run, Archetype, HousingForm;
        public int Turn, Camp, Society, ParentSociety, Population, AgeTurns, ManagedAnimals, Craftspersons, Specialists;
        public double QoL, ClothingCoverage, HousingCoverage, SocialCohesion, FixedShelterPeople, LightShelterPeople, SpecialistMultiplier;
    }

    public sealed class RunResult
    {
        public RunSpec Spec;
        public string SourceFingerprint;
        public List<AggregateRow> Trajectory = new List<AggregateRow>();
        public List<TribeRow> Tribes = new List<TribeRow>();
    }

    private static string Arg(string[] args, string key, string fallback = null)
    {
        var value = args.FirstOrDefault(a => a.StartsWith(key + "=", StringComparison.Ordinal));
        return value == null ? fallback : value.Substring(key.Length + 1);
    }

    private static void Main(string[] args)
    {
        string planPath = Arg(args, "--plan");
        string output = Arg(args, "--output");
        if (string.IsNullOrWhiteSpace(planPath) || string.IsNullOrWhiteSpace(output))
            throw new ArgumentException("Usage: --plan=<SweepPlan.json> --output=<directory>");
        var options = new JsonSerializerOptions { IncludeFields = true, PropertyNameCaseInsensitive = true, WriteIndented = true };
        var specs = JsonSerializer.Deserialize<List<RunSpec>>(File.ReadAllText(planPath), options);
        if (specs == null || specs.Count == 0 || specs.Any(s => string.IsNullOrWhiteSpace(s.Name) || s.Archetype != "pastoral" && s.Archetype != "agricultural"))
            throw new ArgumentException("The sweep plan must contain named pastoral/agricultural runs.");
        if (specs.GroupBy(s => s.Name, StringComparer.Ordinal).Any(g => g.Count() != 1)) throw new ArgumentException("Run names must be unique.");
        Directory.CreateDirectory(output);
        var results = new List<RunResult>();
        foreach (var spec in specs)
        {
            Console.WriteLine("RUN " + spec.Name);
            results.Add(Run(spec));
        }
        File.WriteAllText(Path.Combine(output, "runs.json"), JsonSerializer.Serialize(results, options));
        WriteAggregate(Path.Combine(output, "trajectories.csv"), results.SelectMany(r => r.Trajectory));
        WriteTribes(Path.Combine(output, "per_tribe.csv"), results.SelectMany(r => r.Tribes));
    }

    private static RunResult Run(RunSpec spec)
    {
        var world = Fixture(spec);
        var result = new RunResult { Spec = spec, SourceFingerprint = SaveCodec.Fingerprint(world) };
        var created = world.Camps.ToDictionary(c => c.Id, c => 0);
        var formations = new Dictionary<int, List<int>>();
        int cumulativeFissions = 0, cumulativeDeaths = 0, blocked = 0, campTurns = 0;
        Capture(result, world, null, 0, created, formations, cumulativeFissions, cumulativeDeaths, blocked, campTurns);
        var resolver = new TurnResolver(new PhaseExecution(1, 64, false));
        for (int turn = 1; turn <= Turns; turn++)
        {
            var record = resolver.Resolve(world, new Dictionary<int, List<Order>>());
            foreach (var fission in record.Fissions)
            {
                created[fission.DaughterCamp] = turn;
                if (!formations.TryGetValue(fission.ParentCamp, out var list)) formations[fission.ParentCamp] = list = new List<int>();
                list.Add(turn);
            }
            cumulativeFissions += record.Fissions.Count;
            cumulativeDeaths += record.Deaths;
            foreach (var report in record.Demographics)
            {
                var camp = world.Camps.FirstOrDefault(c => c.Id == report.Camp);
                if (camp != null && CultivationEconomy.EconomyEnabled(camp))
                {
                    campTurns++;
                    if (QoLMortality.CeilingRate(world.Config, report.QoL) <= 1e-12) blocked++;
                }
            }
            if (turn % 5 == 0) Capture(result, world, record, turn, created, formations, cumulativeFissions, cumulativeDeaths, blocked, campTurns);
        }
        return result;
    }

    private static WorldState Fixture(RunSpec spec)
    {
        var config = SimulationConfig.Small(1);
        config.Width = 128; config.Height = 64; config.Players = 1;
        config.QoLMortalityEnabled = true; config.QoLMortalityElasticity = 1;
        config.FissionEnabled = true; config.ImmigrationEnabled = false;
        config.HerdLabourEnabled = true; config.SharedCropHerdAllocationEnabled = true;
        config.ExploredMemoryTurns = 1000;
        var n = config.Neolithic;
        n.LightHousingDecayPerReferenceTurn = spec.TentDecay;
        n.AnimalFabricPerLightHousing = spec.FabricPerTent;
        n.LightHousingBuilderOutputPerBaseDays = n.BuilderHousingOutputPerBaseDays * spec.TentProductivity;
        n.SpecialistScaleMaxBonus = spec.SpecialistMaxBonus;
        n.SpecialistScaleOnsetPopulation = spec.SpecialistOnset;
        n.SpecialistScalePopulation = spec.SpecialistScale;
        config.Validate();

        int tileCount = config.Width * config.Height;
        var world = new WorldState { Seed = 1701, Turn = 0, Days = 0, Config = config, PlayerCount = 1,
            Tiles = new Tile[tileCount], Generation = new GenerationReport { GeneratorVersion = WorldState.GeneratorVersion } };
        world.Rivers.Add(new RiverEdge { Id = 0, Flow = 500, Width = 1 });
        bool agriculture = spec.Archetype == "agricultural";
        for (int id = 0; id < tileCount; id++)
        {
            var tile = new Tile
            {
                Id = id, ElevationLevel = 1, Relief = Relief.Lowland,
                Environment = agriculture ? EnvironmentKind.Desert : EnvironmentKind.Grassland,
                NaturalVegetationPotential = agriculture ? VegetationTier.Medium : VegetationTier.Low,
                CurrentVegetation = agriculture ? VegetationTier.Medium : VegetationTier.Low,
                FreshwaterSource = agriculture ? FreshwaterSource.River : FreshwaterSource.RainPool,
                RiverEdges = agriculture ? new[] { 0, -1, -1, -1, -1, -1 } : null,
                Food = 1800, PotentialFood = 18000, Freshwater = 50000
            };
            for (int season = 0; season < 4; season++)
                tile.Seasons[season] = new SeasonData
                {
                    Temperature = agriculture ? 28 : 18,
                    RegionalMoisture = agriculture ? .06f : .30f,
                    Moisture = agriculture ? .85f : .55f,
                    MaxFood = 2000, MaxPotentialFood = 20000, MaxFreshwater = 50000,
                    FoodRecoveryPerDay = (float)EcologyMath.DailyRate(config.LandFoodRegeneration, config.BaseDays),
                    BiomassRecoveryPerDay = (float)EcologyMath.DailyRate(config.LandFoodRegeneration, config.BaseDays),
                    WaterRecoveryPerDay = (float)EcologyMath.DailyRate(config.WaterRecovery, config.BaseDays), Habitat = 1
                };
            world.Tiles[id] = tile;
        }
        world.Resources = RichResources(tileCount);
        int origin = world.Map.At(config.Width / 2, config.Height / 2);
        var camp = new Camp
        {
            Id = world.NextId++, Player = 0, Tile = origin, Population = StartingPopulation,
            PlantFood = StartingPopulation * config.Consumption * config.GenerationIntervals,
            QoL = 1, FoodNeedsMetRatio = 1, WaterNeedsMetRatio = 1,
            EconomyAutomationTurns = -1, ManagedBreedingIntensity = .55, ManagedBreedingPolicyInitialized = true,
            StationaryYears = agriculture ? 100 : 0, StationaryTurns = agriculture ? 5 : 0, IsVillage = agriculture
        };
        camp.Inventory.HandTools = StartingPopulation * 2;
        camp.Inventory.Pots = StartingPopulation * config.Consumption * config.GenerationIntervals / n.PotStorageFoodPerUnit;
        if (agriculture) camp.Inventory.Cloth = StartingPopulation * 2;
        else camp.Inventory.AnimalFabric = StartingPopulation * 2;
        world.Camps.Add(camp); Ownership.Bind(world);
        camp.People.Progression.Era = Era.Neolithic;
        camp.People.Progression.EraStartDays = 0;
        camp.Technology = agriculture ? Technology.Cultivation : Technology.AnimalHusbandry;
        camp.Technologies[(int)camp.Technology] = true;
        camp.People.OpeningTransitionProcessed = true;
        foreach (string technology in NeolithicTech.FirstTier) camp.People.NeolithicTechnologies.Add(technology);
        camp.People.UnlockedSpecialists.AddRange(WorkforceRoles.Specialists.Concat(WorkforceRoles.Extractors));
        camp.People.Memory = new ContactMemory(config.Width);
        for (int id = 0; id < tileCount; id++) camp.People.Memory.Explored.Add(id, 0);

        if (agriculture)
        {
            // The controlled village starts with six maintained Stone-Farm-Tool
            // fields, the first ordinary intensification step above primitive
            // cultivation. The tool stock is real and depreciates normally.
            camp.Inventory.FarmTools = StartingPopulation;
            NeolithicProduction.AddFixedHousing(world, camp, StartingPopulation / n.HousingPeoplePerUnit, world.Days);
            foreach (int id in world.Map.Radius(origin, 1).OrderBy(x => x).Take(6))
            {
                var tile = world.Tiles[id];
                tile.CultivationCapital = Math.Pow(n.StoneFarmToolsSupportedIntensity / config.ReferenceIntensity, 2);
                tile.Cultivation = (float)n.StoneFarmToolsSupportedIntensity;
                tile.CultivationOperator = camp.Id; tile.Cultivated = true;
            }
        }
        else
        {
            camp.Inventory.LightHousingUnits = StartingPopulation / n.LightHousingPeoplePerUnit;
            world.Herds.Add(new Herd { Id = world.NextId++, Tile = origin, DomesticCamp = camp.Id, OwnerSociety = camp.SocietyId,
                Animals = spec.Herd, Capacity = Math.Max(spec.Herd, 40), Species = "goat", NutritionInitialized = true,
                LastNutrition = 1, LastFoodNutrition = 1, LastWaterNutrition = 1, ManagementFulfillment = 1 });
        }
        NeolithicProduction.ResolveNeeds(world, camp, 0);
        return world;
    }

    private static WorldResourceState RichResources(int count)
    {
        var resources = new WorldResourceState
        {
            MaximumQuantity = 100000, CatalogHash = ResourceCatalog.Hash, ProfileHash = ResourceCatalog.Hash,
            ConfigurationHash = ResourceCatalog.Hash, Provinces = Array.Empty<GeologicalProvince>(),
            Province = new ushort[count], History = new ushort[count], Host = new byte[count], Marine = new byte[count],
            Stone = new ushort[count], Clay = new ushort[count], Wood = new ushort[count], WoodRecoveryPerYear = .05
        };
        for (int i = 0; i < count; i++) resources.Stone[i] = resources.Clay[i] = resources.Wood[i] = 60000;
        return resources;
    }

    private static void Capture(RunResult result, WorldState world, TurnRecord record, int turn,
        Dictionary<int, int> created, Dictionary<int, List<int>> formations, int cumulativeFissions, int cumulativeDeaths, int blocked, int campTurns)
    {
        var camps = world.Camps.Where(c => c.Population > 0).OrderBy(c => c.Id).ToArray();
        var populations = camps.Select(c => (double)c.Population).OrderBy(x => x).ToArray();
        var qualities = camps.Select(c => c.QoL).OrderBy(x => x).ToArray();
        double totalPopulation = camps.Sum(c => c.Population);
        double fixedPeople = 0, tentPeople = 0, weightedClothing = 0, weightedHousing = 0, weightedCohesion = 0;
        foreach (var camp in camps)
        {
            var shelter = NeolithicProduction.HousingCapacity(world, camp, world.Days);
            double clothing = Math.Min(1, camp.ClothPerCapita), housing = NeolithicProduction.HousingCoverage(shelter, camp.Population);
            double cohesion = NeolithicProduction.SocialCohesion(world, camp, camp.Population);
            weightedClothing += camp.Population * clothing; weightedHousing += camp.Population * housing; weightedCohesion += camp.Population * cohesion;
            if (shelter.Light > shelter.Fixed) tentPeople += camp.Population; else if (shelter.Fixed > 0) fixedPeople += camp.Population;
            var society = camp.People;
            int animals = world.Herds.Where(h => h.DomesticCamp == camp.Id).Sum(h => h.Animals);
            string form = shelter.Light > shelter.Fixed ? "tent-dominant" : shelter.Fixed > 0 ? "fixed-housing" : "unhoused";
            int specialists = camp.Workforce.Assignments.Where(a => WorkforceRoles.Specialists.Contains(a.Role) || WorkforceRoles.Extractors.Contains(a.Role)).Sum(a => a.Population);
            result.Tribes.Add(new TribeRow
            {
                Run = result.Spec.Name, Archetype = result.Spec.Archetype, Turn = turn, Camp = camp.Id, Society = camp.SocietyId,
                ParentSociety = society.ParentSociety, Population = camp.Population, AgeTurns = turn - (created.TryGetValue(camp.Id, out int born) ? born : 0),
                ManagedAnimals = animals, Craftspersons = camp.Workforce.Assignments.Where(a => a.Role == WorkforceRoles.Craftsperson).Sum(a => a.Population), Specialists = specialists,
                QoL = camp.QoL, ClothingCoverage = clothing, HousingCoverage = housing, SocialCohesion = cohesion,
                FixedShelterPeople = shelter.Fixed, LightShelterPeople = shelter.Light, HousingForm = form,
                SpecialistMultiplier = SpecialistProductivity.Multiplier(world.Config.Neolithic, camp.Population)
            });
        }
        var reports = record?.Demographics ?? new List<DemographicReport>();
        var receipts = record?.Production ?? new List<ProductionReceipt>();
        double produced = reports.Sum(d => d.FoodProduced);
        double tentFood = receipts.Where(r => r.Good == CraftGood.AnimalFabric || r.Good == CraftGood.LightHousing).Sum(r => r.PlantFood + r.AnimalFood);
        double tentLabor = receipts.Where(r => r.Good == CraftGood.AnimalFabric || r.Good == CraftGood.LightHousing).Sum(r => r.WorkerIntervals);
        double availableLabor = reports.Sum(d => d.StartingPopulation) * world.Config.GenerationIntervals;
        var intervals = formations.Values.SelectMany(list => list.Zip(list.Skip(1), (a, b) => (double)(b - a))).OrderBy(x => x).ToArray();
        double animalsTotal = world.Herds.Where(h => h.DomesticCamp >= 0).Sum(h => h.Animals);
        double pastoralPopulation = camps.Where(CultivationEconomy.HusbandryEnabled).Sum(c => c.Population);
        result.Trajectory.Add(new AggregateRow
        {
            Run = result.Spec.Name, Archetype = result.Spec.Archetype, Turn = turn, Population = (int)totalPopulation, Tribes = camps.Length,
            P25Population = Percentile(populations, .25), MedianPopulation = Percentile(populations, .5), P75Population = Percentile(populations, .75),
            MeanQoL = qualities.Length == 0 ? 0 : qualities.Average(), MedianQoL = Percentile(qualities, .5),
            HusbandryQoL = Mean(camps.Where(CultivationEconomy.HusbandryEnabled).Select(c => c.QoL)),
            AgricultureQoL = Mean(camps.Where(CultivationEconomy.Enabled).Select(c => c.QoL)),
            CumulativeFissions = cumulativeFissions, FissionsPerThousand = totalPopulation > 0 ? 1000 * cumulativeFissions / totalPopulation : 0,
            MedianInterFissionTurns = Percentile(intervals, .5), ManagedAnimals = animalsTotal,
            AnimalsPerPastoralResident = pastoralPopulation > 0 ? animalsTotal / pastoralPopulation : 0,
            TerrestrialFood = reports.Sum(d => d.TerrestrialFood), AnimalFood = reports.Sum(d => d.AnimalFood), SlaughterFood = reports.Sum(d => d.SlaughterFood),
            MarineFood = reports.Sum(d => d.MarineFood), LakeFood = reports.Sum(d => d.LakeFood), HuntFood = reports.Sum(d => d.HuntFood),
            FishFood = reports.Sum(d => d.FishFood), ForageFood = reports.Sum(d => d.ForageFood), TotalFoodProduced = produced,
            GrowthFoodAccepted = reports.Sum(d => d.GrowthFoodAccepted), ManufacturingFood = reports.Sum(d => d.ManufacturingFood),
            MaintenanceFoodConsumed = Math.Max(0, reports.Sum(d => d.FoodConsumed - d.GrowthFoodAccepted)),
            ClothingCoverage = totalPopulation > 0 ? weightedClothing / totalPopulation : 0,
            HousingCoverage = totalPopulation > 0 ? weightedHousing / totalPopulation : 0,
            SocialCohesion = totalPopulation > 0 ? weightedCohesion / totalPopulation : 0,
            TentDominantPopulationShare = totalPopulation > 0 ? tentPeople / totalPopulation : 0,
            FixedHousingPopulationShare = totalPopulation > 0 ? fixedPeople / totalPopulation : 0,
            QoLBlockedCampTurns = blocked, CumulativeCampTurns = campTurns,
            QoLMortalityBlockedFraction = campTurns > 0 ? blocked / (double)campTurns : 0,
            StarvationDeaths = record?.Deaths ?? 0, CumulativeStarvationDeaths = cumulativeDeaths,
            TentChainFoodShare = produced > 0 ? tentFood / produced : 0,
            TentChainSurplusFoodShare = produced > reports.Sum(d => d.FoodConsumed - d.GrowthFoodAccepted)
                ? tentFood / (produced - reports.Sum(d => d.FoodConsumed - d.GrowthFoodAccepted)) : 0,
            TentChainLaborShare = availableLabor > 0 ? tentLabor / availableLabor : 0,
            HerdFoodShare = produced > 0 ? reports.Sum(d => d.AnimalFood + d.SlaughterFood) / produced : 0
        });
    }

    private static double Mean(IEnumerable<double> values)
    {
        var a = values.ToArray(); return a.Length == 0 ? 0 : a.Average();
    }

    private static double Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 0) return 0;
        double at = (sorted.Length - 1) * p; int lo = (int)Math.Floor(at), hi = (int)Math.Ceiling(at);
        return sorted[lo] + (sorted[hi] - sorted[lo]) * (at - lo);
    }

    private static string R(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static void WriteAggregate(string path, IEnumerable<AggregateRow> rows)
    {
        using var w = new StreamWriter(path, false, new UTF8Encoding(false));
        w.WriteLine("run,archetype,turn,population,tribes,p25_population,median_population,p75_population,mean_qol,median_qol,husbandry_qol,agriculture_qol,cumulative_fissions,fissions_per_1000,median_inter_fission_turns,managed_animals,animals_per_pastoral_resident,terrestrial_food,animal_food,slaughter_food,marine_food,lake_food,hunt_food,fish_food,forage_food,total_food_produced,maintenance_food_consumed,growth_food_accepted,manufacturing_food,clothing_coverage,housing_coverage,social_cohesion,tent_population_share,fixed_population_share,qol_blocked_camp_turns,cumulative_camp_turns,qol_blocked_fraction,starvation_deaths,cumulative_starvation_deaths,tent_chain_food_share,tent_chain_surplus_food_share,tent_chain_labor_share,herd_food_share");
        foreach (var x in rows) w.WriteLine(string.Join(",", x.Run, x.Archetype, x.Turn, x.Population, x.Tribes, R(x.P25Population), R(x.MedianPopulation), R(x.P75Population), R(x.MeanQoL), R(x.MedianQoL), R(x.HusbandryQoL), R(x.AgricultureQoL), x.CumulativeFissions, R(x.FissionsPerThousand), R(x.MedianInterFissionTurns), R(x.ManagedAnimals), R(x.AnimalsPerPastoralResident), R(x.TerrestrialFood), R(x.AnimalFood), R(x.SlaughterFood), R(x.MarineFood), R(x.LakeFood), R(x.HuntFood), R(x.FishFood), R(x.ForageFood), R(x.TotalFoodProduced), R(x.MaintenanceFoodConsumed), R(x.GrowthFoodAccepted), R(x.ManufacturingFood), R(x.ClothingCoverage), R(x.HousingCoverage), R(x.SocialCohesion), R(x.TentDominantPopulationShare), R(x.FixedHousingPopulationShare), x.QoLBlockedCampTurns, x.CumulativeCampTurns, R(x.QoLMortalityBlockedFraction), x.StarvationDeaths, x.CumulativeStarvationDeaths, R(x.TentChainFoodShare), R(x.TentChainSurplusFoodShare), R(x.TentChainLaborShare), R(x.HerdFoodShare)));
    }

    private static void WriteTribes(string path, IEnumerable<TribeRow> rows)
    {
        using var w = new StreamWriter(path, false, new UTF8Encoding(false));
        w.WriteLine("run,archetype,turn,camp,society,parent_society,population,age_turns,qol,clothing_coverage,housing_coverage,social_cohesion,fixed_shelter_people,light_shelter_people,housing_form,managed_animals,craftspersons,specialists,specialist_multiplier");
        foreach (var x in rows) w.WriteLine(string.Join(",", x.Run, x.Archetype, x.Turn, x.Camp, x.Society, x.ParentSociety, x.Population, x.AgeTurns, R(x.QoL), R(x.ClothingCoverage), R(x.HousingCoverage), R(x.SocialCohesion), R(x.FixedShelterPeople), R(x.LightShelterPeople), x.HousingForm, x.ManagedAnimals, x.Craftspersons, x.Specialists, R(x.SpecialistMultiplier)));
    }
}
