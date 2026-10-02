using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Anthropocene.Simulation;

// Read-only profiles, matched-state diagnostics, and bounded scale measurements.
internal static class HerdEcologyProbe
{
    private static void Main(string[] args)
    {
        if (args[0] == "scale") { Scale(); return; }
        if (args[0] == "sites") { Sites(); return; }
        if (args[0] == "moisture-sites") { Sites(true); return; }
        if (args[0] == "grassland-portfolio") { GrasslandPortfolio(args[1]); return; }
        if (args[0] == "grassland-trajectory") { GrasslandTrajectory(args[1]); return; }
        var w = SaveCodec.Load(File.ReadAllBytes(args[1]));
        if (args[0] == "advance")
        {
            while (w.Turn < int.Parse(args[2], CultureInfo.InvariantCulture))
            {
                var ai = AiPlanning.Decide(w);
                new TurnResolver().Resolve(w, ai.Orders, ai.Traces);
            }
            File.WriteAllBytes(args[3], SaveCodec.Save(w)); return;
        }
        if (args[0] == "ecology")
        {
            bool Hydro(Tile t) => t.River || t.OasisInfluence > 0 || t.FreshwaterSource == FreshwaterSource.River || t.FreshwaterSource == FreshwaterSource.Lake || t.FreshwaterSource == FreshwaterSource.Oasis;
            using (var writer = new StreamWriter(args[2]))
            {
                writer.WriteLine("tile,environment,relief,biomass,food,moisture,warm,water,anchor");
                foreach (var t in w.Tiles.Where(t => t.TraversableLand))
                {
                    bool water = Hydro(t) || w.Map.Neighbors(t.Id).Any(id => Hydro(w.Tiles[id]));
                    bool anchor = !water && (t.Relief == Relief.Lowland || t.Relief == Relief.Hills) && (t.Environment == EnvironmentKind.Temperate || t.Environment == EnvironmentKind.Tropical) && t.NaturalVegetationPotential >= VegetationTier.Medium;
                    writer.WriteLine(string.Join(",", t.Id, t.Environment, t.Relief, t.Seasons.Average(s => s.MaxPotentialFood).ToString("R", CultureInfo.InvariantCulture),
                        t.Seasons.Average(s => s.MaxFood).ToString("R", CultureInfo.InvariantCulture), t.Seasons.Average(s => s.Moisture).ToString("R", CultureInfo.InvariantCulture),
                        t.Seasons.All(s => s.Temperature >= 22) ? 1 : 0, water ? 1 : 0, anchor ? 1 : 0));
                }
            }
            return;
        }
        if (args[0] != "portfolio") throw new ArgumentException("ecology, portfolio, advance, or scale");
        var camps = w.Camps.Where(c => c.Id == int.Parse(args[2], CultureInfo.InvariantCulture)).ToArray();
        var rows = camps.Select(c =>
        {
            var e = EconomicIntent.From(c); e.ManagedBreedingIntensity = ManagedBreedingPolicy.Recommend(w, c, c.Tile, new PlanningIndex(w));
            var d = CampProductionDemand.Build(w, c, e);
            var p = EconomicPlanner.Plan(w, c, e, EconomicPlanner.ConfiguredRule(w.Config), out double price);
            var h = d.HerdLadder;
            int selected = p.Assignments.Where(a => a.Role == WorkforceRoles.Herder).Sum(a => a.Population);
            object choices = null;
#if !BASELINE
            if (h != null) choices = Enumerable.Range(0, h.Steps + 1).Select(k => new { herders = k, target = h.Target(k),
                currentMilk = h.CurrentAnimalFood[k], slaughter = h.SlaughterFood[k], currentValue = h.CurrentValue[k], futureValue = h.FutureValue[k], residualValue = h.ResidualValue[k],
                managementYears = h.ManagementYears[k], harvestSavings = h.SavedHarvestYears[k], toolYears = h.ReplacementYears[k], craftYears = h.CraftYears[k],
                totalValue = h.TotalValue[k], netWorkerYears = h.WorkerYears[k], objective = h.TotalValue[k] - (double.IsNaN(price) ? 0 : price) * h.WorkerYears[k] }).ToArray();
#endif
            return new { c.Id, c.Population, animals = HerdingTooling.DomesticAnimals(w, c), years = w.Config.TurnDays / w.Config.DaysPerYear, price, selected,
                p.HerdTarget, d.ForageSupply, d.Biological, d.ManagedFood.AnimalFood, d.ManagedFood.HumanFoodDisplaced,
                forecastAtSelected = TurnResolver.ForecastManagedAnimalFood(w, c, selected, w.Config.GenerationIntervals, w.Config.TurnDays / w.Config.DaysPerYear).AnimalFood,
                pasture = h?.PastureCapacity, choices };
        }).ToArray();
        File.WriteAllText(args[3], JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static void Sites(bool moistureAudit = false)
    {
        var cases = new[] {
            ("dry desert, controlled drinking water", .03f, .03f, false, false),
            ("dry desert, no drinking water", .03f, .03f, false, true),
            ("less arid desert", .10f, .10f, false, false),
            ("grassland", .22f, .22f, false, false),
            ("fertile tropical", .75f, .75f, false, false),
            ("desert river", .85f, .03f, true, false) };
        foreach (var site in cases)
        foreach (double q in moistureAudit ? new[] { 0.0, .000001, .2, .4, .5 } : new[] { 0.0, .2, .4 })
        foreach (int startingAnimals in moistureAudit ? new[] { 22, 200 } : new[] { 22 })
        {
            var cfg = SimulationConfig.Small(1); cfg.Width = 24; cfg.Height = 16; cfg.HerdLabourEnabled = true; cfg.QoLMortalityEnabled = true; cfg.QoLMortalityElasticity = 1; cfg.FissionEnabled = true;
            var w = new WorldState { Config = cfg, Seed = 1, Tiles = new Tile[384], Generation = new GenerationReport { GeneratorVersion = WorldState.GeneratorVersion } };
            var c = new Camp { Id = w.NextId++, Tile = 100, Population = 404, PlantFood = 38487.1265, QoL = .8, ClothPerCapita = .5, HousingPerCapita = .6, RudimentaryExpectation = 1, IsVillage = q >= CultivationEconomy.VillageIntensity };
            w.Camps.Add(c); Ownership.Bind(w); c.Technology = Technology.AnimalHusbandry; c.Technologies[(int)Technology.AnimalHusbandry] = true; c.Technologies[(int)Technology.Cultivation] = true;
            c.People.Progression.Era = Era.Neolithic; foreach (string tech in NeolithicTech.FirstTier) c.People.NeolithicTechnologies.Add(tech);
            c.Inventory.HandTools = 1000; c.Inventory.HerdingTools = 10; c.Inventory.Stone = 5000; c.Inventory.Wood = 5000;
            var footprint = w.Map.Radius(c.Tile, 1).ToArray();
            foreach (int id in Enumerable.Range(0, w.Tiles.Length))
            {
                var t = new Tile { Id = id, ElevationLevel = 1, Relief = Relief.Lowland, RiverEdges = site.Item4 ? new[] { 1 } : null, FreshwaterSource = site.Item5 ? FreshwaterSource.None : FreshwaterSource.RainPool };
                t.Environment = Vegetation.Classify(25, site.Item3, cfg);
                for (int season = 0; season < 4; season++) t.Seasons[season] = new SeasonData { Temperature = 25, RegionalMoisture = site.Item3, Moisture = site.Item2,
                    MaxFreshwater = site.Item5 ? 0 : 25, FoodRecoveryPerDay = (float)EcologyMath.DailyRate(cfg.LandFoodRegeneration * (.2 + site.Item2), cfg.BaseDays), WaterRecoveryPerDay = (float)EcologyMath.DailyRate(cfg.WaterRecovery, cfg.BaseDays) };
                t.NaturalVegetationPotential = t.CurrentVegetation = Vegetation.Potential(t, cfg);
                FoodEcology.ConfigurePotential(t, cfg, true); t.Freshwater = t.Seasons[0].MaxFreshwater * .9;
                if (footprint.Contains(id) && q > 0)
                {
                    t.CultivationOperator = c.Id; t.CultivationCapital = Math.Pow(q / cfg.ReferenceIntensity, 2); t.CultivationOperatingFraction = 1;
                }
                t.Food = .9 * new EcologyCapacity(w, t, new EcologyView(t), 0).Food;
                w.Tiles[id] = t;
            }
            w.Herds.Add(new Herd { Id = w.NextId++, Tile = c.Tile, DomesticCamp = c.Id, OwnerSociety = c.SocietyId, Animals = startingAnimals, Species = "goat", Capacity = 1 });
            var e = EconomicIntent.From(c); e.ManagedBreedingIntensity = .55;
            var d = CampProductionDemand.Build(w, c, e);
            var plan = EconomicPlanner.Plan(w, c, e, EconomicPlanner.ConfiguredRule(cfg), out double price);
            var h = d.HerdLadder;
            int herders = plan.Assignments.Where(a => a.Role == WorkforceRoles.Herder).Sum(a => a.Population);
            int farmers = plan.Assignments.Where(a => a.Role == WorkforceRoles.Farmer).Sum(a => a.Population);
            var actualCandidate = EconomicPlanner.Preview(c, plan, c.Population);
            var actualLabor = CultivationEconomy.CalculateLabor(w, actualCandidate, footprint, new FarmToolWorkBudget(w, actualCandidate), 20);
            var ecology = new EcologyCapacity(w, w.Tiles[c.Tile], new EcologyView(w.Tiles[c.Tile]), 0);
            double rawUpkeep = CultivationEconomy.MaintenanceWorkerRequirement(w, c);
            var laborWitness = new AgriculturalLabor { OperatingFarmers = d.Established ? 100 : 0, FoodGatherers = d.Established ? 0 : 100, Footprint = footprint };
            double fixedHarvest = FoodHorizonForecast.Settlement(w, c, c.Tile, w.Days, cfg.TurnDays,
                laborWitness, 1e9, 0, null, null, null, true).TerrestrialFood / 20;
            double toolsManaged = HerdingTooling.Plan(w, c, 20, 100, startingAnimals).Capacity;
            double? selectedSavings = null;
#if !BASELINE
            selectedSavings = h.SavedHarvestYears[herders];
#endif
            Console.WriteLine(JsonSerializer.Serialize(new { site = site.Item1, q, startingAnimals, soilMoisture = site.Item2, biomassPerTile = w.Tiles[c.Tile].Seasons[0].MaxPotentialFood,
                foodCapacityPerTile = ecology.Food, managedFoodSourcePerYear = ecology.ManagedFlow * cfg.DaysPerYear,
                rawMaintenanceWorkers = rawUpkeep, plannedMaintenanceWorkers = d.Upkeep, established = d.Established,
                resolverOperatingFarmers = actualLabor.OperatingFarmers, resolverExpansionFarmers = actualLabor.ExpansionFarmers,
                resolverMaintenanceFraction = actualLabor.RequiredMaintenance > 0 ? Math.Min(1, actualLabor.EffectiveMaintenanceFarmers / actualLabor.RequiredMaintenance) : 0,
                fixed100FoodWorkerReceiptsPerYear = fixedHarvest, fixed100HerderAnimalsManaged = toolsManaged,
                foodInventory = c.Food, planningIncomingFood = d.Biological,
                currentForagePerYear = d.ForageAfterHerds / 20, biologicalPerYear = d.Biological / 20, pasture = h.PastureCapacity,
                currentMilkAtFullManagement = d.ManagedFood.AnimalFood / 20, price, herders, target = plan.HerdTarget, farmers,
                expansionFarmers = plan.Labor.ExpansionFarmers, milkAtSelected = TurnResolver.ForecastManagedAnimalFood(w, c, herders, cfg.GenerationIntervals, 20).AnimalFood / 20, constrainedFloor = h.MinimumHerders,
                selectedSavedHarvestYears = selectedSavings }));
        }
    }

    // The controlled moisture-audit site with the shared allocation on: the
    // physical crop/herd anchors, the selected plan, and every candidate's
    // objective components at the selection prices (Docs/SharedAllocationValidation.md).
    private static WorldState Site(float soil, float regional, bool river, double q, int startingAnimals, bool shared)
    {
        var cfg = SimulationConfig.Small(1); cfg.Width = 24; cfg.Height = 16; cfg.HerdLabourEnabled = true; cfg.QoLMortalityEnabled = true; cfg.QoLMortalityElasticity = 1; cfg.FissionEnabled = true;
        cfg.SharedCropHerdAllocationEnabled = shared;
        var w = new WorldState { Config = cfg, Seed = 1, Tiles = new Tile[384], Generation = new GenerationReport { GeneratorVersion = WorldState.GeneratorVersion } };
        var c = new Camp { Id = w.NextId++, Tile = 100, Population = 404, PlantFood = 38487.1265, QoL = .8, ClothPerCapita = .5, HousingPerCapita = .6, RudimentaryExpectation = 1, IsVillage = q >= CultivationEconomy.VillageIntensity };
        w.Camps.Add(c); Ownership.Bind(w); c.Technology = Technology.AnimalHusbandry; c.Technologies[(int)Technology.AnimalHusbandry] = true; c.Technologies[(int)Technology.Cultivation] = true;
        c.People.Progression.Era = Era.Neolithic; foreach (string tech in NeolithicTech.FirstTier) c.People.NeolithicTechnologies.Add(tech);
        c.Inventory.HandTools = 1000; c.Inventory.HerdingTools = 10; c.Inventory.Stone = 5000; c.Inventory.Wood = 5000;
        var footprint = w.Map.Radius(c.Tile, 1).ToArray();
        foreach (int id in Enumerable.Range(0, w.Tiles.Length))
        {
            var t = new Tile { Id = id, ElevationLevel = 1, Relief = Relief.Lowland, RiverEdges = river ? new[] { 1 } : null, FreshwaterSource = FreshwaterSource.RainPool };
            t.Environment = Vegetation.Classify(25, regional, cfg);
            for (int season = 0; season < 4; season++) t.Seasons[season] = new SeasonData { Temperature = 25, RegionalMoisture = regional, Moisture = soil,
                MaxFreshwater = 25, FoodRecoveryPerDay = (float)EcologyMath.DailyRate(cfg.LandFoodRegeneration * (.2 + soil), cfg.BaseDays), WaterRecoveryPerDay = (float)EcologyMath.DailyRate(cfg.WaterRecovery, cfg.BaseDays) };
            t.NaturalVegetationPotential = t.CurrentVegetation = Vegetation.Potential(t, cfg);
            FoodEcology.ConfigurePotential(t, cfg, true); t.Freshwater = t.Seasons[0].MaxFreshwater * .9;
            if (footprint.Contains(id) && q > 0) { t.CultivationOperator = c.Id; t.CultivationCapital = Math.Pow(q / cfg.ReferenceIntensity, 2); t.CultivationOperatingFraction = 1; }
            t.Food = .9 * new EcologyCapacity(w, t, new EcologyView(t), 0).Food;
            w.Tiles[id] = t;
        }
        w.Herds.Add(new Herd { Id = w.NextId++, Tile = c.Tile, DomesticCamp = c.Id, OwnerSociety = c.SocietyId, Animals = startingAnimals, Species = "goat", Capacity = 1 });
        return w;
    }
    // Repeatable food per year of the radius-one footprint at field intensity q:
    // an emptied food pool refilled for one turn at constant capital (the
    // planner's own future-land quantity).
    private static double RepeatableFieldFood(WorldState w, Camp c, double q)
    {
        var cfg = w.Config; double years = cfg.TurnDays / cfg.DaysPerYear, total = 0;
        foreach (int id in CultivationEconomy.Footprint(w, c))
        {
            var view = LazyEcology.ProjectEcology(w, id, w.Days); double e = Math.Pow(q / cfg.ReferenceIntensity, 2);
            view.Food = 0; view.CultivationCapital = e;
            total += FoodHorizonForecast.Tile(w, id, w.Days, cfg.TurnDays, null, e, e, 1, false, view).ClosingFood;
        }
        return total / years;
    }
    private static void GrasslandPortfolio(string output)
    {
        var rows = new System.Collections.Generic.List<object>();
        var cases = new[] { ("grassland", .22f, .22f, false), ("fertile tropical", .75f, .75f, false) };
        foreach (var site in cases)
        foreach (double q in site.Item1 == "grassland" ? new[] { 0.0, .4 } : new[] { .4 })
        foreach (int animals in site.Item1 == "grassland" ? new[] { 22, 200 } : new[] { 22 })
        foreach (bool shared in new[] { false, true })
        {
            var w = Site(site.Item2, site.Item3, site.Item4, q, animals, shared); var c = w.Camps[0]; var cfg = w.Config;
            double years = cfg.TurnDays / cfg.DaysPerYear;
            var e = EconomicIntent.From(c); e.ManagedBreedingIntensity = .55;
            var d = CampProductionDemand.Build(w, c, e);
            double fullFood = TurnResolver.ForecastManagedAnimalFood(w, c, 1000, cfg.GenerationIntervals, years).AnimalFood / years / animals;
            SharedAllocation.CaptureLast = true; SharedAllocation.Last = null;
            var plan = EconomicPlanner.Plan(w, c, e, EconomicPlanner.ConfiguredRule(cfg), out double price);
            SharedAllocation.CaptureLast = false;
            var last = SharedAllocation.Last;
            int Role(string role) => plan.Assignments.Where(a => a.Role == role).Sum(a => a.Population);
            rows.Add(new {
                site = site.Item1, soilMoisture = site.Item2, q, animals, shared,
                physical = new {
                    foodPerManagedAnimalYear = fullFood,
                    primitiveFoodPerHerderYear = cfg.AnimalsPerHerder * fullFood,
                    stoneToolFoodPerHerderYear = cfg.Neolithic.StoneHerdingToolAnimalsPerHerder * fullFood,
                    gatherFoodPerWorkerYear = cfg.GatherPerPerson * cfg.DaysPerYear / cfg.BaseDays,
                    harvestProductivity = d.HarvestProductivity,
                    footprintFields = CultivationEconomy.Footprint(w, c).Length,
                    repeatableFieldFoodPerYear = RepeatableFieldFood(w, c, q),
                    repeatableUncultivatedFoodPerYear = RepeatableFieldFood(w, c, 0),
                },
                selected = new { herders = Role(WorkforceRoles.Herder), farmers = Role(WorkforceRoles.Farmer), gatherers = Role(WorkforceRoles.Gatherer),
                    expansionFarmers = plan.Labor?.ExpansionFarmers ?? 0, herdTarget = plan.HerdTargetSet ? plan.HerdTarget : -1, breeding = plan.ManagedBreedingIntensity, laborPrice = price },
                selectionPrices = last == null ? null : new { current = SharedAllocation.LastCurrentPrice, future = SharedAllocation.LastFuturePrice, budget = SharedAllocation.LastBudget },
                candidates = last?.Candidates(SharedAllocation.LastCurrentPrice, SharedAllocation.LastFuturePrice, SharedAllocation.LastBudget)
            });
        }
        File.WriteAllText(output, JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
    }

    // The same site resolved for ten turns per planner: herd, fields, village and people.
    private static void GrasslandTrajectory(string output)
    {
        var rows = new System.Collections.Generic.List<object>();
        foreach (int animals in new[] { 22, 200 })
        foreach (bool shared in new[] { false, true })
        {
            var w = Site(.22f, .22f, false, 0, animals, shared); var c = w.Camps[0]; c.EconomyAutomationTurns = -1;
            for (int turn = 0; turn < 10; turn++)
            {
                var ai = AiPlanning.Decide(w); new TurnResolver().Resolve(w, ai.Orders, ai.Traces);
                c = w.Camps.OrderByDescending(x => x.Population).First();
                var footprint = CultivationEconomy.Footprint(w, c);
                rows.Add(new { animals, shared, turn = turn + 1, camps = w.Camps.Count, population = w.Camps.Sum(x => x.Population),
                    managedAnimals = w.Herds.Where(h => h.DomesticCamp >= 0).Sum(h => h.Animals), village = c.IsVillage,
                    meanFieldQ = footprint.Average(id => CultivationEconomy.Intensity(w.Config, w.Tiles[id].CultivationCapital)),
                    herders = c.Workforce.Assignments.Where(a => a.Role == WorkforceRoles.Herder).Sum(a => a.Population),
                    farmers = c.Workforce.Assignments.Where(a => a.Role == WorkforceRoles.Farmer).Sum(a => a.Population),
                    expansionFarmers = c.EconomicLabor?.ExpansionFarmers ?? 0, herdTarget = c.HerdTargetSet ? c.HerdTarget : -1 });
            }
        }
        File.WriteAllText(output, JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void Scale()
    {
        foreach (int residents in new[] { 100, 1000, 10000 })
        foreach (int herdCount in new[] { 1, 8, 64 })
        {
            var cfg = SimulationConfig.Small(1); cfg.Width = 24; cfg.Height = 16; cfg.HerdLabourEnabled = true; cfg.QoLMortalityEnabled = true; cfg.FissionEnabled = true;
            var w = new WorldState { Config = cfg, Seed = 1, Tiles = new Tile[384], Generation = new GenerationReport { GeneratorVersion = WorldState.GeneratorVersion } };
            foreach (int id in Enumerable.Range(0, w.Tiles.Length))
            {
                var t = new Tile { Id = id, Terrain = Terrain.Grassland, ElevationLevel = 1, Food = 1000000, PotentialFood = 10000000, Freshwater = 10000000, FreshwaterSource = FreshwaterSource.RainPool };
                for (int s = 0; s < 4; s++) t.Seasons[s] = new SeasonData { MaxFood = 1000000, MaxPotentialFood = 10000000, MaxFreshwater = 10000000, Moisture = 1, Temperature = 22 };
                w.Tiles[id] = t;
            }
            var c = new Camp { Id = w.NextId++, Tile = 100, Population = residents, PlantFood = residents * 100, QoL = .8 };
            w.Camps.Add(c); Ownership.Bind(w); c.Technology = Technology.AnimalHusbandry; c.Technologies[(int)Technology.AnimalHusbandry] = true; c.Technologies[(int)Technology.Cultivation] = true;
            c.People.Progression.Era = Era.Neolithic; foreach (string tech in NeolithicTech.FirstTier) c.People.NeolithicTechnologies.Add(tech);
            c.Inventory.HandTools = residents * 100; c.Inventory.HerdingTools = residents;
            for (int i = 0; i < herdCount; i++) w.Herds.Add(new Herd { Id = w.NextId++, Tile = c.Tile, DomesticCamp = c.Id, OwnerSociety = c.SocietyId, Animals = residents * 2 / herdCount + (i < residents * 2 % herdCount ? 1 : 0), Species = i % 2 == 0 ? "goat" : "antelope", Capacity = 1 });
            var e = EconomicIntent.From(c); e.ManagedBreedingIntensity = .55;
            var d = CampProductionDemand.Build(w, c, e); EconomicPlanner.Plan(w, c, e);
            const int repeats = 5;
            long bytes = GC.GetAllocatedBytesForCurrentThread(); var watch = Stopwatch.StartNew();
            for (int i = 0; i < repeats; i++) CampProductionDemand.Build(w, c, e);
            double buildMs = watch.Elapsed.TotalMilliseconds / repeats; long buildBytes = (GC.GetAllocatedBytesForCurrentThread() - bytes) / repeats;
            bytes = GC.GetAllocatedBytesForCurrentThread(); watch.Restart();
            for (int i = 0; i < repeats; i++) EconomicPlanner.Plan(w, c, e);
            Console.WriteLine(JsonSerializer.Serialize(new { residents, herdCount, steps = d.HerdLadder?.Steps, buildMs, buildBytes, planMs = watch.Elapsed.TotalMilliseconds / repeats, planBytes = (GC.GetAllocatedBytesForCurrentThread() - bytes) / repeats }));
        }
    }
}
