using System;
using System.Collections.Generic;
using System.Linq;
using Anthropocene.Simulation;

internal static class DemographyRegressionChecks
{
    private static int assertions;
    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new Exception("Demography regression: " + message);
    }

    public static int Run()
    {
        assertions = 0;
        bool protectiveRecovery = KernelTrajectory("protective-goods", (turn, _) => turn < 2 ? (1.0, 0.0, 0.0) : (1.0, 1.0, 1.0), 6, 2);
        bool foodRecovery = KernelTrajectory("temporary-food", (turn, _) => turn == 0 ? (0.0, 1.0, 1.0) : (1.0, 1.0, 1.0), 6, 1);
        bool infeasible = KernelTrajectory("repeated-infeasible", (_, __) => (0.0, 0.0, 0.0), 7, -1);
        FlowAttribution();
        bool economyRecovery = NativeEconomyTrajectory();
        Check(protectiveRecovery, "temporary clothing/housing loss resumes positive physical growth after restoration");
        Check(foodRecovery, "temporary food shortage resumes positive physical growth after restoration");
        Check(infeasible, "repeated infeasible shocks decline for explicit exposure/starvation causes");
        Check(economyRecovery, "the native Elder endogenously rebuilds and sustains food, clothing and housing service");
        Console.WriteLine("RL_MATCH policy=native status=MEASURED policy=learned status=UNAVAILABLE reason=no_learned_policy_runtime_or_model_artifact_in_repository");
        Console.WriteLine("DEMOGRAPHY_GATE NO_GO"
            + " nativeConservation=PASS recoverableKernel=PASS endogenousEconomy=" + (economyRecovery ? "RECOVERED" : "NOT_RECOVERED")
            + " learnedComparison=UNAVAILABLE trainingDefects=UNRESOLVED");
        Console.WriteLine("PASS: " + assertions + " death-spiral regression assertions");
        return assertions;
    }

    private static bool KernelTrajectory(string name, Func<int, double, (double Food, double Clothing, double Housing)> shock, int turns, int restoredTurn)
    {
        var cfg = new SimulationConfig { DemographicModel = DemographicModel.PhysicalMortalityV1 };
        double population = 1000, deficitYears = 0, minimum = population, lastNet = 0, starvation = 0;
        int recovery = -1;
        for (int turn = 0; turn < turns; turn++)
        {
            var service = shock(turn, population);
            double maximumY = PhysicalDemography.BirthOnlyPersonYears(population, cfg.BaseBirthRate, cfg.Neolithic.ReferenceTurnYears);
            double food = service.Food * PhysicalDemography.FoodPerPersonYear(cfg) * maximumY;
            double clothing = service.Clothing * PhysicalDemography.ClothingPerPersonYear(cfg) * maximumY;
            var result = PhysicalDemography.AdvanceBudget(cfg, population, cfg.Neolithic.ReferenceTurnYears, food, clothing,
                service.Housing * population, deficitYears, true);
            lastNet = result.FinalPopulation - population; population = result.FinalPopulation;
            minimum = Math.Min(minimum, population); starvation += result.LegacyStarvationDeaths;
            deficitYears = result.Nutrition >= 1 - 1e-12 ? 0 : deficitYears + cfg.Neolithic.ReferenceTurnYears;
            if (recovery < 0 && restoredTurn >= 0 && turn >= restoredTurn && service.Food >= 1 && service.Clothing >= 1 && service.Housing >= 1 && lastNet > 0) recovery = turn;
            // The controlled kernel has no economic world, so hold water at one and expectation at a declared mature
            // 0.7 while evaluating the unchanged composite welfare function from the realized F/C/H path.
            const double expectation = .7;
            double experiencedPopulation = result.PersonYears / cfg.Neolithic.ReferenceTurnYears;
            double clothingWelfare = 1 - expectation * (1 - result.ClothingCoverage);
            double housingWelfare = 1 - expectation * (1 - result.AverageHousingCoverage);
            double qol = result.Nutrition
                * (cfg.Neolithic.RudimentaryWeightClothing * clothingWelfare + cfg.Neolithic.RudimentaryWeightHousing * housingWelfare)
                * NeolithicProduction.SocialCohesion(cfg.Neolithic, result.AverageHousingCoverage, experiencedPopulation);
            Console.WriteLine("TRAJECTORY case=" + name + " policy=controlled turn=" + turn
                + " grossBirths=" + result.Births.ToString("F4") + " deaths=" + result.Deaths.ToString("F4")
                + " exposure=" + result.ExposureDeaths.ToString("F4") + " crowding=" + result.CrowdingDeaths.ToString("F4")
                + " disease=" + result.DiseaseDeaths.ToString("F4") + " starvation=" + result.LegacyStarvationDeaths.ToString("F4")
                + " nutrition=" + result.Nutrition.ToString("F4") + " population=" + population.ToString("F4")
                + " workforce=" + Math.Floor(population) + " clothing=" + result.ClothingCoverage.ToString("F4")
                + " housing=" + result.AverageHousingCoverage.ToString("F4") + " migration=0 fission=0 qol=" + qol.ToString("F4") + " recovery=" + recovery);
            Check(Math.Abs(result.FinalPopulation - result.InitialPopulation - result.Births + result.Deaths) < 1e-7,
                name + " continuous population conservation at turn " + turn);
            Check(Math.Abs(result.Deaths - result.ExposureDeaths - result.CrowdingDeaths - result.DiseaseDeaths - result.LegacyStarvationDeaths - result.AdditionalDeaths) < 1e-7,
                name + " cause reconciliation at turn " + turn);
        }
        if (restoredTurn >= 0) return recovery >= restoredTurn && lastNet > 0 && population > minimum;
        return lastNet < 0 && starvation > 0 && population < 1000;
    }

    private static void FlowAttribution()
    {
        var migration = UniformWorld(2); migration.Config.ImmigrationEnabled = true; migration.Config.FissionEnabled = false;
        var source = migration.Camps[0]; var destination = migration.Camps[1];
        source.Tile = migration.Map.At(10, 8); destination.Tile = migration.Map.Radius(source.Tile, 2).First(t => migration.Map.Distance(source.Tile, t) == 2);
        foreach (var c in migration.Camps)
        {
            c.Technology = Technology.AnimalHusbandry; c.Technologies[(int)Technology.AnimalHusbandry] = true; c.People.Progression.Era = Era.Neolithic;
            c.Inventory.LightHousingUnits = 1e9; c.Inventory.Cloth = 77; c.Inventory.AnimalFabric = 11;
        }
        source.FoodNeedsMetRatio = .4; source.ClothPerCapita = 1; source.RudimentaryExpectation = .5;
        destination.FoodNeedsMetRatio = 1; destination.ClothPerCapita = 0; destination.RudimentaryExpectation = 1e-6; destination.Inventory.LightHousingUnits = 0;
        ContactKnowledge.MemoryOf(migration, source.People).Upsert(destination.Id, destination.Player, destination.Tile, migration.Turn, ContactSource.Sighting);
        var migrationRecord = BlankRecord(migration); int migrationTotal = migration.Camps.Sum(c => c.Population), sourceBefore = source.Population;
        Immigration.Resolve(migration, migrationRecord, new PhaseExecution(), new ResolutionMetrics());
        var flow = migrationRecord.Migrations.Single(); WelfareDiagnostics.Capture(migration, migrationRecord);
        Console.WriteLine("TRAJECTORY case=emigration-attribution policy=controlled turn=0 grossBirths=0 deaths=0 exposure=0 crowding=0 disease=0 starvation=0"
            + " nutrition=" + source.FoodNeedsMetRatio.ToString("F4") + " population=" + source.Population + " workforce=" + source.Workforce.Assigned
            + " clothing=" + source.ClothPerCapita.ToString("F4") + " housing=" + source.HousingPerCapita.ToString("F4")
            + " migration=" + flow.Migrants + " fission=0 qol=" + flow.SourceQoL.ToString("F4") + " recovery=NA");
        Check(sourceBefore - source.Population == flow.Migrants && migration.Camps.Sum(c => c.Population) == migrationTotal,
            "a focal decline by emigration is recorded as a conserved transfer, not mortality");
        Check(flow.HigherQoLWorseMortality && migrationRecord.SettlementWelfare.Any(x => x.Emigrated == flow.Migrants)
            && migrationRecord.LineageWelfare.Any(x => x.Emigrated == flow.Migrants),
            "migration welfare diagnostics expose both the survival inversion and changed reward jurisdiction");

        var fission = UniformWorld(1); fission.Config.ImmigrationEnabled = false; fission.Config.FissionEnabled = true;
        fission.Config.FissionRadius = 3; fission.Config.FissionHousingRelief = 1; fission.Config.FissionDaughterMargin = 1;
        fission.Config.FissionGainThreshold = fission.Config.FissionGainDiscourage = fission.Config.FissionGainEncourage = 0;
        var parent = fission.Camps.Single(); parent.Technology = Technology.Cultivation; parent.Technologies[(int)Technology.Cultivation] = true;
        parent.People.Progression.Era = Era.Neolithic; parent.FoodNeedsMetRatio = parent.WaterNeedsMetRatio = parent.ClothPerCapita = 1;
        parent.QoL = .5; parent.StationaryYears = 60; parent.RudimentaryExpectation = .7; parent.FoodDeficitYears = 37;
        foreach (int tile in fission.Map.Radius(parent.Tile, 4)) ContactKnowledge.MemoryOf(fission, parent.People).Explored.Add(tile);
        var fissionRecord = BlankRecord(fission); int fissionTotal = fission.Camps.Sum(c => c.Population), parentBefore = parent.Population;
        Fission.Resolve(fission, fissionRecord, new PhaseExecution(), new ResolutionMetrics());
        var split = fissionRecord.Fissions.Single(); var daughter = fission.Camps.Single(c => c.Id == split.DaughterCamp);
        WelfareDiagnostics.Capture(fission, fissionRecord);
        Console.WriteLine("TRAJECTORY case=fission-attribution policy=controlled turn=0 grossBirths=0 deaths=0 exposure=0 crowding=0 disease=0 starvation=0"
            + " nutrition=" + parent.FoodNeedsMetRatio.ToString("F4") + " population=" + parent.Population + " workforce=" + parent.Workforce.Assigned
            + " clothing=" + parent.ClothPerCapita.ToString("F4") + " housing=" + parent.HousingPerCapita.ToString("F4")
            + " migration=0 fission=" + split.Daughter + " qol=" + parent.QoL.ToString("F4") + " recovery=NA");
        Check(parentBefore - parent.Population == split.Daughter && fission.Camps.Sum(c => c.Population) == fissionTotal,
            "a focal decline by fission is recorded as a conserved daughter transfer, not mortality");
        Check(daughter.FoodDeficitYears == parent.FoodDeficitYears && fissionRecord.LineageWelfare.Single().FissionTransfers == split.Daughter,
            "fission preserves physical deficit history and lineage welfare identifies the transfer");
    }

    private static TurnRecord BlankRecord(WorldState world)
    {
        var record = new TurnRecord { Turn = world.Turn };
        foreach (var c in world.Camps) record.Demographics.Add(new DemographicReport { Camp = c.Id, StartingPopulation = c.Population, EndingPopulation = c.Population });
        return record;
    }

    private static WorldState UniformWorld(int camps)
    {
        var cfg = SimulationConfig.Small(camps); cfg.DemographicModel = DemographicModel.PhysicalMortalityV1;
        cfg.Width = 24; cfg.Height = 16; cfg.ContactExpiryTurns = 50; cfg.Neolithic.CohesionBandScale = 220; cfg.Neolithic.CohesionHousingGain = 0;
        var world = new WorldState { Seed = 101, Config = cfg, Generation = new GenerationReport(), Tiles = new Tile[cfg.Width * cfg.Height], Turn = 3, Days = 1000, PlayerCount = camps };
        for (int i = 0; i < world.Tiles.Length; i++)
        {
            var tile = new Tile { Id = i, ElevationLevel = 1, Terrain = Terrain.Grassland, FreshwaterSource = FreshwaterSource.RainPool, Freshwater = 100, PotentialFood = 8000, Food = 1000, LastEcologyUpdateDays = world.Days };
            for (int s = 0; s < 4; s++) tile.Seasons[s] = new SeasonData { Temperature = 18, Moisture = .55f, RegionalMoisture = .5f, MaxFood = 1000, MaxPotentialFood = 8000, MaxFreshwater = 100, FoodRecoveryPerDay = .004f, BiomassRecoveryPerDay = .004f, WaterRecoveryPerDay = .004f, Habitat = .6f };
            world.Tiles[i] = tile;
        }
        for (int i = 0; i < camps; i++) world.Camps.Add(new Camp { Id = world.NextId++, Player = i, Tile = world.Map.At(8 + i * 4, 8), Population = 100, PlantFood = 6000, AnimalFood = 3000, AquaticFood = 1500, QoL = .5, RudimentaryExpectation = .5 });
        Ownership.Bind(world); return world;
    }

    private static bool NativeEconomyTrajectory()
    {
        var cfg = SimulationConfig.Small(1);
        cfg.DemographicModel = DemographicModel.PhysicalMortalityV1;
        cfg.FissionEnabled = false; cfg.ImmigrationEnabled = false; cfg.TradeEnabled = false;
        cfg.EconomicAllocation = EconomicAllocationStrategy.DemandControl;
        var world = new WorldGenerator().Generate(991, cfg); var camp = world.Camps.Single();
        camp.Population = 120; camp.PlantFood = 40000; camp.AnimalFood = camp.AquaticFood = 0;
        camp.Technology = Technology.Cultivation; camp.Technologies[(int)Technology.Cultivation] = true;
        camp.People.Progression.Era = Era.Neolithic;
        foreach (string technology in NeolithicTech.FirstTier) if (!camp.People.NeolithicTechnologies.Contains(technology)) camp.People.NeolithicTechnologies.Add(technology);
        foreach (string role in WorkforceRoles.Specialists) if (!camp.People.UnlockedSpecialists.Contains(role)) camp.People.UnlockedSpecialists.Add(role);
        camp.Inventory.HandTools = camp.Inventory.Tools = 300; camp.Inventory.Wood = camp.Inventory.Clay = camp.Inventory.Stone = 500;
        camp.Inventory.Cloth = 120; camp.Inventory.HousingMaterial = 30; camp.EconomyAutomationTurns = -1;
        // This is an established q_k=0.4 cultivation economy before the protective-goods shock. Its food-producing
        // capital remains real saved state; only clothing, shelter and the relevant specialist assignments are removed.
        double fieldCapital = Math.Pow(.4 / world.Config.ReferenceIntensity, 2);
        foreach (int id in CultivationEconomy.Footprint(world, camp))
        {
            var tile = world.Tiles[id];
            tile.CultivationCapital = fieldCapital; tile.Cultivation = (float)CultivationEconomy.Intensity(world.Config, fieldCapital);
            tile.CultivationOperator = camp.Id; tile.CultivationOperatingFraction = 1; tile.Cultivated = true;
        }
        // Select fertile but finite geography for the recovery case. The native ecology still owns, depletes and
        // regenerates these stocks; the harness does not replenish food or goods after the shock.
        foreach (int id in world.Map.Radius(camp.Tile, world.Config.GatherRadius).Where(id => world.Tiles[id].TraversableLand))
        {
            var tile = world.Tiles[id]; tile.Food *= 4; tile.PotentialFood *= 4;
            for (int season = 0; season < tile.Seasons.Length; season++)
            {
                var profile = tile.Seasons[season]; profile.MaxFood *= 4; profile.MaxPotentialFood *= 4;
                tile.Seasons[season] = profile;
            }
        }
        world.FixedHousing.Add(new FixedHousingStock { CampId = camp.Id, Tile = camp.Tile, Units = 30, LastUpdatedDays = world.Days });
        NeolithicProduction.FixedHousingChanged(world);

        // Controlled shock: remove protective finished goods and the specialist assignments, but leave real people,
        // food, tools and raw resources. Recovery therefore has to pass through the ordinary planner and recipes.
        camp.Workforce.Assign(WorkforceRoles.Weaver, 3, camp.Population);
        camp.Workforce.Assign(WorkforceRoles.Builder, 3, camp.Population);
        camp.Workforce.Assign(WorkforceRoles.Craftsperson, 2, camp.Population);
        camp.Inventory.Cloth = camp.Inventory.AnimalFabric = camp.Inventory.LightHousingUnits = 0;
        world.FixedHousing.RemoveAll(h => h.CampId == camp.Id); NeolithicProduction.FixedHousingChanged(world);
        int removedSpecialists = camp.Workforce.Assignments.Where(a => a.Role == WorkforceRoles.Weaver
            || a.Role == WorkforceRoles.Builder || a.Role == WorkforceRoles.Craftsperson).Sum(a => a.Population);
        camp.Workforce.Assignments.RemoveAll(a => a.Role == WorkforceRoles.Weaver || a.Role == WorkforceRoles.Builder || a.Role == WorkforceRoles.Craftsperson);
        Check(removedSpecialists == 8 && camp.Workforce.Assigned == 0,
            "the endogenous fixture applies a real specialist-workforce shock before native replanning");
        int initial = camp.Population, minimum = initial, recovery = -1, stableTurns = 0; bool specialistsRebuilt = false;
        var resolver = new TurnResolver();
        for (int turn = 0; turn < 12 && world.LinkedPopulation(camp) > 0; turn++)
        {
            var plan = AiPlanning.Decide(world); var record = resolver.Resolve(world, plan.Orders, plan.Traces);
            var d = record.Demographics.Single(r => r.Camp == camp.Id);
            int population = world.LinkedPopulation(camp); minimum = Math.Min(minimum, population);
            int workforce = camp.Workforce.Assigned;
            int specialists = camp.Workforce.Assignments.Where(a => a.Role == WorkforceRoles.Weaver || a.Role == WorkforceRoles.Builder || a.Role == WorkforceRoles.Craftsperson).Sum(a => a.Population);
            double clothMade = record.Production.Where(p => p.Camp == camp.Id && p.Good == CraftGood.Cloth).Sum(p => p.Output);
            double housingMade = record.Production.Where(p => p.Camp == camp.Id && (p.Good == CraftGood.Housing || p.Good == CraftGood.LightHousing)).Sum(p => p.Output);
            string roles = string.Join(",", camp.Workforce.Assignments.Where(a => a.Population > 0)
                .OrderBy(a => a.Role).Select(a => a.Role + ":" + a.Population));
            specialistsRebuilt |= specialists > 0;
            if (camp.ClothPerCapita >= .8 && camp.HousingPerCapita >= .8 && camp.FoodNeedsMetRatio >= .8) stableTurns++; else stableTurns = 0;
            if (recovery < 0 && turn > 0 && stableTurns >= 3) recovery = turn;
            Console.WriteLine("TRAJECTORY case=endogenous-real-economy policy=native turn=" + turn
                + " grossBirths=" + d.GrossBirths + " deaths=" + d.GrossDeaths
                + " exposure=" + d.ExposureDeaths.ToString("F4") + " crowding=" + d.CrowdingDeaths.ToString("F4")
                + " disease=" + d.DiseaseDeaths.ToString("F4") + " starvation=" + d.StarvationDeaths.ToString("F4")
                + " nutrition=" + d.Nutrition.ToString("F4") + " population=" + population + " workforce=" + workforce + " specialists=" + specialists
                + " clothing=" + d.ClothingCoverage.ToString("F4") + " housing=" + d.HousingCoverage.ToString("F4")
                + " clothMade=" + clothMade.ToString("F4") + " housingMade=" + housingMade.ToString("F4")
                + " clothStock=" + camp.Inventory.Cloth.ToString("F4") + " fabricUsed=" + camp.FabricConsumed.ToString("F4")
                + " plantFood=" + camp.PlantFood.ToString("F4") + " roles=" + roles
                + " migration=" + record.Migrations.Sum(m => m.Migrants) + " fission=" + record.Fissions.Sum(f => f.Daughter)
                + " qol=" + camp.QoL.ToString("F4") + " recovery=" + recovery);
            Check(d.StartingPopulation + d.GrossBirths - d.GrossDeaths == d.EndingPopulation,
                "native real-economy integer conservation at turn " + turn);
            Check(workforce <= camp.Population, "native real-economy workforce reconciliation at turn " + turn);
            Check(d.ContinuousDeaths > 0 && d.ExposureDeaths + d.CrowdingDeaths + d.DiseaseDeaths + d.StarvationDeaths > 0,
                "native real-economy decline/growth has physical death causes at turn " + turn);
        }
        bool sustained = stableTurns >= 3 && specialistsRebuilt;
        Console.WriteLine("RECOVERY case=endogenous-real-economy initial=" + initial + " minimum=" + minimum + " final=" + world.LinkedPopulation(camp)
            + " firstSustainedRecovery=" + recovery + " sustainedAtEnd=" + sustained + " specialistsRebuilt=" + specialistsRebuilt);
        return sustained;
    }
}
