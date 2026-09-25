using System;
using System.Collections.Generic;
using System.Linq;
using Anthropocene.Simulation;

internal static class HusbandryChecks
{
    private static int checks;
    private static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception("Husbandry: " + message); }
    private static void Near(double actual, double expected, string message, double tolerance = 1e-6) => Check(Math.Abs(actual - expected) <= tolerance, message + " actual=" + actual + " expected=" + expected);

    private static WorldState Fixture(int animals = 100, int herders = 50)
    {
        var w = NeolithicChecks.Fixture(false); var c = w.Camps[0];
        c.Technology = Technology.AnimalHusbandry; c.Technologies[(int)Technology.AnimalHusbandry] = true; c.People.Progression.Era = Era.Neolithic;
        c.PlantFood = c.AnimalFood = c.AquaticFood = 0; c.Population = 100; c.Workforce = new PopulationLedger();
        if (herders > 0) c.Workforce.Assign(WorkforceRoles.Herder, herders, c.Population);
        foreach (var tile in w.Tiles)
        {
            tile.Food = 0; tile.PotentialFood = 100000; tile.Freshwater = 100000;
            for (int season = 0; season < 4; season++) { tile.Seasons[season].MaxFood = 100000; tile.Seasons[season].MaxPotentialFood = 100000; tile.Seasons[season].MaxFreshwater = 100000; }
        }
        w.Herds.Add(new Herd { Id = w.NextId++, Tile = c.Tile, Species = "antelope", Animals = animals, Capacity = 1, DomesticCamp = c.Id, OwnerSociety = c.SocietyId });
        return w;
    }
    private static TurnRecord Resolve(WorldState w) => new TurnResolver().Resolve(w, new Dictionary<int, List<Order>>());

    public static int Run()
    {
        checks = 0; FullManagedOutput(); UnderManagementAndNoCap(); CurrentNutritionAndNewbornBoundary(); ProportionalSpeciesManagement(); SharedControlAndSave(); ObservationAndOrderContracts(); PartitionedExposure(); MixedFoodInputBoundary(); MixedAnimalOnlyDoesNotChargePlant(); TypedFabricPriorityBudget(); StorageReceivingCapacity(); SharedStarvationAndSourceLedgers(); SpeciesReplayAndContinuation(); StaleNutritionAndNewbornBoundary(); BreedingIntensityPolicy(); HerdingToolCoverage();
        Console.WriteLine("PASS: " + checks + " managed husbandry assertions"); return checks;
    }
    private static void FullManagedOutput()
    {
        var w = Fixture(); var c = w.Camps[0]; var r = Resolve(w); var herd = w.Herds[0]; var report = r.ManagedHerds.Single();
        Near(report.Nutrition, 1, "fully fed managed herd nutrition"); Near(report.ManagementFulfillment, 1, "fully staffed managed herd coverage");
        Near(report.PotentialFood, 8000, "one human-equivalent animal output uses live reference interval"); Near(report.AcceptedFood, 8000, "animal output enters common useful-food budget");
        Check(r.Transfers.Any(t => t.Kind == "Animal Product" && t.Entity == c.Id && t.Food > 0), "animal product receipt is camp-owned");
        Check(report.StartingAnimals == 100 && report.SurvivingAnimals == 100 && report.StarvationDeaths == 0 && report.NewbornAnimals == herd.Animals - 100, "managed chronology records survivors then newborns");
        Check(r.PopulationChanges.All(p => p.Kind != "Managed herd starvation" && p.Kind != "Managed herd reproduction") && r.Deaths == 0, "animal changes do not corrupt human demographic accounting");
    }
    private static void UnderManagementAndNoCap()
    {
        var w = Fixture(100, 0); var herd = w.Herds[0]; var before = herd.Animals; var r = Resolve(w);
        Check(herd.Animals > before && r.ManagedHerds.Single().AcceptedFood == 0, "under-management reduces output without deleting animals while biological reproduction continues");
        Check(herd.Animals > herd.Capacity, "domestic herd has no legacy Capacity clamp");
    }
    private static void CurrentNutritionAndNewbornBoundary()
    {
        var w = Fixture(100, 50); var tile = w.Tiles[w.Camps[0].Tile]; var herd = w.Herds[0];
        tile.PotentialFood = 2560; tile.Freshwater = 2880; for (int s = 0; s < 4; s++) { tile.Seasons[s].MaxPotentialFood = 2560; tile.Seasons[s].MaxFood = 0; tile.Seasons[s].MaxFreshwater = 2880; tile.Seasons[s].FoodRecoveryPerDay = tile.Seasons[s].BiomassRecoveryPerDay = tile.Seasons[s].WaterRecoveryPerDay = 0; }
        w.Config.StarvationGraceTurns = 0; w.Config.Mortality = 0; w.Config.AnnualHumanGrowthRate = 0;
        var r = Resolve(w); var report = r.ManagedHerds.Single();
        Check(report.Nutrition < .81 && report.Nutrition > .79, "managed nutrition uses current forage and water exposure actual=" + report.Nutrition + " forage=" + r.Transfers.Where(t => t.Entity == herd.Id && t.Kind == "Herd").Sum(t => t.PotentialFood) + " unmet=" + herd.UnmetFood + " water=" + herd.UnmetFreshwater);
        Near(report.PotentialFood, report.SurvivingAnimals * 80 * report.Nutrition, "renewable output uses surviving current exposure");
        herd.GrowthRemainder = .9; herd.Animals = 0; var zero = Resolve(w); Check(herd.Animals == 0 && herd.NewbornAnimals == 0 && herd.GrowthRemainder == 0, "zero animals do not reproduce from a stale fraction actual=" + herd.Animals + "/" + herd.NewbornAnimals + "/" + herd.GrowthRemainder);
    }
    private static void ProportionalSpeciesManagement()
    {
        var w = Fixture(0, 0); var c = w.Camps[0]; c.Workforce.Assign(WorkforceRoles.Herder, 2, c.Population); w.Herds.Clear();
        w.Herds.Add(new Herd { Id = w.NextId++, Tile = c.Tile, Species = "goat", Animals = 10, DomesticCamp = c.Id, OwnerSociety = c.SocietyId });
        w.Herds.Add(new Herd { Id = w.NextId++, Tile = c.Tile, Species = "goat", Animals = 10, DomesticCamp = c.Id, OwnerSociety = c.SocietyId });
        w.Herds.Add(new Herd { Id = w.NextId++, Tile = c.Tile, Species = "antelope", Animals = 20, DomesticCamp = c.Id, OwnerSociety = c.SocietyId });
        var r = Resolve(w); var reports = r.ManagedHerds.OrderBy(x => x.Herd).ToArray();
        Check(reports.All(x => x.ManagementFulfillment > .09 && x.ManagementFulfillment < .11), "species and same-species herd management is proportional and split-invariant");
        Check(reports.Sum(x => x.ManagementFulfillment * x.StartingAnimals / 2) <= 2 + 1e-8, "management allocation never exceeds resident Herder work");
    }
    private static void SharedControlAndSave()
    {
        var w = Fixture(20, 10); var c = w.Camps[0]; var observation = new AgentObservationBuilder().Build(w, c.Player); var settlement = observation.Settlements.Single();
        Check(settlement.Husbandry && LegalActions.Build(observation).Single(x => x.Entity == c.Id).Economy != null, "Husbandry-only camps expose the shared Economy control");
        var plan = EconomicPlanner.Plan(w, c, EconomicIntent.From(c)); Check(plan.Assignments.Any(a => a.Role == WorkforceRoles.Herder) && plan.Assignments.Sum(a => a.Population) <= c.Population, "planner assigns one bounded Herder role");
        w.Config.AnimalFoodPerManagedAnimal = 0; w.Config.ManagedHerdDefaultsInitialized = true;
        var loaded = SaveCodec.Load(SaveCodec.Save(w)); var herd = loaded.Herds.Single(); Near(herd.ManagementFulfillment, 1, "saved managed state preserves legitimate management"); Near(loaded.Config.AnimalFoodPerManagedAnimal, 0, "saved zero AnimalFood yield remains authoritative"); Check(loaded.Camps[0].Workforce.Assignments.Any(a => a.Role == WorkforceRoles.Herder), "saved Herder assignment uses existing ledger");
    }
    private static void ObservationAndOrderContracts()
    {
        var w = Fixture(20, 10); var c = w.Camps[0];
        var intent = EconomicIntent.From(c); var observation = new AgentObservationBuilder().Build(w, c.Player);
        var order = new Order(OrderKind.Economy, c.Id) { Economy = intent };
        Check(OrderRules.Validate(observation, order) == null, "Husbandry-only Economy order validates");
        EconomicIntents.Apply(c, intent); Check(c.Workforce.Assignments.Any(a => a.Role == WorkforceRoles.Herder), "validated Husbandry Economy order applies through the shared ledger");
        var noAgr = NeolithicChecks.Fixture(false); var noAgrObservation = new AgentObservationBuilder().Build(noAgr, noAgr.Camps[0].Player); var farmer = EconomicIntent.From(noAgr.Camps[0]); farmer.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Farmer, Population = 1 });
        Check(OrderRules.Validate(noAgrObservation, new Order(OrderKind.Economy, noAgr.Camps[0].Id) { Economy = farmer }) != null, "Farmer remains illegal without Agriculture");
        var noHerd = NeolithicChecks.Fixture(true); var noHerdObservation = new AgentObservationBuilder().Build(noHerd, noHerd.Camps[0].Player); var herder = EconomicIntent.From(noHerd.Camps[0]); herder.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Herder, Population = 1 });
        Check(OrderRules.Validate(noHerdObservation, new Order(OrderKind.Economy, noHerd.Camps[0].Id) { Economy = herder }) != null, "Herder remains illegal without Husbandry");
        c.Moves = 1; c.StationaryTurns = 0; var movedDto = ObservationContract.Deserialize(ObservationContract.Serialize(new AgentObservationBuilder().Build(w, c.Player), new Mandate()));
        Check(movedDto.Schema == AgentObservation.SchemaVersion && movedDto.Settlements.Single(s => s.Id == c.Id).RecentlyMoved && Math.Abs(movedDto.Settlements.Single(s => s.Id == c.Id).Economy.ManagedBreedingIntensity - c.ManagedBreedingIntensity) < 1e-8, "current-schema DTO carries own movement and shared breeding policy"); c.Moves = 0;

        int foreignTile = w.Map.Neighbors(c.Tile).First(); var foreignCamp = new Camp { Id = w.NextId++, Player = 1, Tile = foreignTile, Population = 20 }; w.Camps.Add(foreignCamp); Ownership.Bind(w);
        var foreign = new Herd { Id = w.NextId++, Tile = foreignTile, Species = "goat", Animals = 17, DomesticCamp = foreignCamp.Id, OwnerSociety = foreignCamp.SocietyId, LastNutrition = .2, ManagementFulfillment = .1, AnimalFoodProduced = 999 };
        w.Herds.Add(foreign); var before = new AgentObservationBuilder().Build(w, c.Player); var beforeJson = ObservationContract.Serialize(before, new Mandate()); var dto = ObservationContract.Deserialize(beforeJson);
        var owned = dto.Herds.Single(h => h.OwnedDomestic); var foreignDto = dto.Herds.Single(h => h.Id == foreign.Id);
        Check(owned.DomesticCamp == c.Id && owned.ExactCount && owned.StartingAnimals >= 0 && owned.PotentialFood >= 0, "owned domestic DTO exposes camp link and managed diagnostics");
        Check(foreignDto.Domestic && !foreignDto.OwnedDomestic && foreignDto.DomesticCamp == -1 && foreignDto.PotentialFood == 0 && foreignDto.ManagementFulfillment == 0, "foreign domestic DTO omits private camp and managed diagnostics");
        foreign.Animals = 19; foreign.LastNutrition = .99; foreign.ManagementFulfillment = 1; foreign.AnimalFoodProduced = 1;
        var after = new AgentObservationBuilder().Build(w, c.Player); Check(beforeJson == ObservationContract.Serialize(after, new Mandate()), "foreign herd private diagnostics and same-band count changes do not alter viewer DTO");
        var ownedHerd = w.Herds.First(h => h.DomesticCamp == c.Id); ownedHerd.LastNutrition = ownedHerd.ManagementFulfillment = ownedHerd.AnimalFoodProduced = 0;
        var loaded = SaveCodec.Load(SaveCodec.Save(w)); var loadedObservation = new AgentObservationBuilder().Build(loaded, c.Player); var loadedOwned = loadedObservation.Herds.Single(h => h.OwnedDomestic);
        Check(loadedOwned.Nutrition == 0 && loadedOwned.ManagementFulfillment == 0 && loadedOwned.AcceptedFood == 0, "owned diagnostic legitimate zeros survive save/load");
    }
    private static void PartitionedExposure()
    {
        var one = Fixture(); one.Config.Species.Single(s => s.Id == "antelope").GrowthRate = 0; var whole = Resolve(one).ManagedHerds.Sum(x => x.PotentialFood);
        var split = Fixture(); split.Config.Species.Single(s => s.Id == "antelope").GrowthRate = 0; split.Config.TurnDays /= 2; var first = Resolve(split).ManagedHerds.Sum(x => x.PotentialFood); var second = Resolve(split).ManagedHerds.Sum(x => x.PotentialFood);
        Near(first + second, whole, "fixed-exposure renewable potential output is duration-partition invariant");
    }
    private static void MixedFoodInputBoundary()
    {
        var w = Fixture(100, 50); var c = w.Camps[0]; c.Technology = Technology.Cultivation; c.Technologies[(int)Technology.Cultivation] = true; c.People.Progression.Era = Era.Neolithic;
        c.CraftingPriorities = new CraftingPriorities { Cloth = 1 }; c.PlantFood = 20; c.People.NeolithicTechnologies.Add(NeolithicTech.Weaving); c.Workforce.Assign(WorkforceRoles.Weaver, 1, c.Population);
        var r = Resolve(w); var d = r.Demographics.Single();
        Check(d.AnimalFood > 0 && d.ManufacturingFood >= 0 && d.FoodReceived >= d.AnimalFood, "mixed Agriculture and Husbandry keeps AnimalFood and plant input provenance");
        Near(d.InitialReserves + d.FoodReceived - d.FoodConsumed - d.ManufacturingFood - d.DiscardedInventory - d.FinalReserves, 0, "mixed food and fabric boundary conserves one common budget");
        var observation = new AgentObservationBuilder().Build(w, c.Player); var plan = EconomicPlanner.Plan(w, c, EconomicIntent.From(c));
        Check(plan.Assignments.Sum(a => a.Population) <= c.Population && EconomicIntents.Validate(observation, observation.Settlements.Single(), plan) == null, "mixed planner reserves Herder work inside the resident and labor budget");
    }
    private static void MixedAnimalOnlyDoesNotChargePlant()
    {
        (TurnRecord Record, double PlantBefore) ResolveCase(bool cloth, double plant, int animals = 198, int herders = 99, int weavers = 1, int deficitTurns = 0)
        {
            var w = Fixture(animals, herders); var c = w.Camps[0];
            c.Technology = Technology.Cultivation; c.Technologies[(int)Technology.Cultivation] = true; c.People.Progression.Era = Era.Neolithic;
            w.Config.AnnualHumanGrowthRate = .01145;
            c.CraftingPriorities = new CraftingPriorities { Cloth = cloth ? 1 : 0, AnimalFabric = 0, LightHousing = 0, HerdingTools = 0 };
            double reserve = Math.Min(NeolithicProduction.DesiredReserveFood(w, c.Population, w.Config.GenerationIntervals), NeolithicProduction.StorageCapacity(w, c, w.Config.GenerationIntervals));
            c.PlantFood = plant; c.AnimalFood = Math.Max(0, reserve - plant);
            c.DeficitTurns = deficitTurns; c.Inventory.HandTools = 10000; c.People.NeolithicTechnologies.Add(NeolithicTech.Weaving); c.Workforce.Assign(WorkforceRoles.Weaver, weavers, c.Population);
            return (Resolve(w), plant);
        }
        foreach (double plant in new[] { 0d, 1d, 100d })
        {
            var withCloth = ResolveCase(true, plant).Record; var withoutCloth = ResolveCase(false, plant).Record;
            var clothReport = withCloth.Demographics.Single(); var plainReport = withoutCloth.Demographics.Single();
            double expectedPlantDebit = Math.Min(plant, 80);
            Near(clothReport.ManufacturingFood, expectedPlantDebit, "AnimalFood-only Cloth input uses only available plant provenance plant=" + plant);
            Near(plainReport.ManufacturingFood, 0, "AnimalFood-only camp has no manufacturing input without Cloth plant=" + plant);
            Check(withCloth.Growth == 25 && withoutCloth.Growth == 25, "positive AnimalFood reserve supports the same 25 births plant=" + plant + " cloth=" + withCloth.Growth + " plain=" + withoutCloth.Growth);
            Near(clothReport.AnimalFood - plainReport.AnimalFood, expectedPlantDebit, "accepted AnimalFood increases by the realized plant debit plant=" + plant + " cloth=" + clothReport.AnimalFood + " plain=" + plainReport.AnimalFood + " m=" + clothReport.ManufacturingFood);
        }
        var recoveringCloth = ResolveCase(true, 500, 192, 96, 4, 1).Record;
        var recoveringPlain = ResolveCase(false, 500, 192, 96, 4, 1).Record;
        Near(recoveringCloth.Demographics.Single().ManufacturingFood, 200, "recovery turn projects current-resident Fabric only");
        Check(recoveringCloth.Growth == 0 && recoveringPlain.Growth == 0, "recovery turn keeps growth disabled in the shared projection");
        Near(recoveringCloth.Demographics.Single().AnimalFood - recoveringPlain.Demographics.Single().AnimalFood, 200, "recovery AnimalFood acceptance matches current-resident Fabric debit");

        var fourCloth = ResolveCase(true, 500, 192, 96, 4).Record;
        var fourPlain = ResolveCase(false, 500, 192, 96, 4).Record;
        Check(fourCloth.Growth == 25 && fourPlain.Growth == 25, "prior-cohort Fabric planning preserves births with four Weavers");
        Near(fourCloth.Demographics.Single().ManufacturingFood, 200, "Fabric charges only the 100 prior-turn residents");
        Near(fourCloth.Demographics.Single().AnimalFood - fourPlain.Demographics.Single().AnimalFood, 200, "four-Weaver AnimalFood acceptance replaces prior-cohort Fabric input");
    }

    private static void TypedFabricPriorityBudget()
    {
        TurnRecord ResolveCase(bool includeCloth, bool animalFirst, double plant)
        {
            var w = Fixture(100, 75); var c = w.Camps[0];
            c.Population = 200; c.Technology = Technology.Cultivation; c.Technologies[(int)Technology.Cultivation] = true; c.People.Progression.Era = Era.Neolithic;
            w.Config.AnnualHumanGrowthRate = .01145; w.Config.GrowthReserveTurns = .1;
            c.CraftingPriorities = new CraftingPriorities { Cloth = includeCloth ? (animalFirst ? 1 : 2) : 0, AnimalFabric = animalFirst ? 2 : 1, LightHousing = 0, HerdingTools = 0 };
            c.PlantFood = plant; c.AnimalFood = 0; c.Inventory.HandTools = 10000;
            c.People.NeolithicTechnologies.Add(NeolithicTech.Weaving); c.Workforce.Assign(WorkforceRoles.Weaver, animalFirst ? 8 : 4, c.Population);
            return Resolve(w);
        }

        var clothFirstControl = ResolveCase(false, false, 0); var clothFirstMixed = ResolveCase(true, false, 0);
        Near(clothFirstMixed.Demographics.Single().FinalReserves, clothFirstControl.Demographics.Single().FinalReserves, "unavailable PlantFood cannot make priority Cloth hide useful AnimalFabric input");
        Near(clothFirstMixed.Demographics.Single().AnimalFood, clothFirstControl.Demographics.Single().AnimalFood, "Cloth-first shared clothing forecast accepts only AnimalFood needed by actual typed production");

        var animalFirstControl = ResolveCase(false, true, 1000); var animalFirstMixed = ResolveCase(true, true, 1000);
        Near(animalFirstMixed.Demographics.Single().FinalReserves, animalFirstControl.Demographics.Single().FinalReserves, "priority AnimalFabric and lower-priority Cloth share one virtual clothing target");
        Near(animalFirstMixed.Demographics.Single().AnimalFood, animalFirstControl.Demographics.Single().AnimalFood, "AnimalFabric-first forecast cannot accept product for duplicate Cloth demand");
    }

    private static void StorageReceivingCapacity()
    {
        var w = Fixture(200, 100); var c = w.Camps[0];
        w.Config.MinimumStorageGenerations = w.Config.MaximumStorageGenerations = 1;
        w.Config.GrowthReserveTurns = 2; c.Inventory.Pots = 100; c.AnimalFood = 8000;
        var r = Resolve(w); var d = r.Demographics.Single();
        Near(c.Inventory.Pots, 90, "existing Pots depreciate once after managed receipt");
        Near(d.AnimalFood, 15200, "AnimalFood acceptance uses post-depreciation shared storage capacity");
        Near(d.DiscardedInventory, 0, "post-depreciation receiving projection prevents food discard");
        Near(d.FinalReserves, 15200, "accepted common food remains within final storage capacity");
    }

    private static void SharedStarvationAndSourceLedgers()
    {
        var w = Fixture(100, 50); var c = w.Camps[0]; c.Food = 0; w.Config.AnnualHumanGrowthRate = 0;
        foreach (var tile in w.Tiles)
        {
            tile.PotentialFood = tile.Freshwater = 0;
            for (int season = 0; season < 4; season++) { tile.Seasons[season].MaxPotentialFood = 0; tile.Seasons[season].MaxFreshwater = 0; }
        }
        bool sawDeathAfterGrace = false;
        for (int turn = 0; turn < 5; turn++)
        {
            var r = Resolve(w); var herd = w.Herds.Single(); var report = r.Demographics.Single();
            Check(herd.Animals == c.Population, "managed animal mortality matches the human population loss boundary");
            if (turn < w.Config.StarvationGraceTurns) Check(herd.Animals == 100 && c.Population == 100, "shared starvation grace preserves people and animals");
            if (turn >= w.Config.StarvationGraceTurns && herd.Animals < 100) sawDeathAfterGrace = true;
            Check(herd.DeficitTurns == c.DeficitTurns, "human and managed-herd starvation grace advances together");
            Near(herd.DeficitSeverity, c.DeficitSeverity, "human and managed-herd starvation severity matches");
            Near(herd.DeathRemainder, c.DeathRemainder, "human and managed-herd fractional death remainder matches");
            Check(r.Deaths == report.Deaths, "managed animal deaths stay outside the human death ledger");
            foreach (var balance in r.EcologyBalances)
            {
                Near(balance.StartingBiomass + balance.BiomassRecharge, balance.EndingBiomass + balance.BiomassHarvest + balance.BiomassOverflow, "biomass source ledger conserves under starvation");
                Near(balance.StartingWater + balance.WaterRecharge, balance.EndingWater + balance.WaterHarvest + balance.WaterOverflow, "freshwater source ledger conserves under starvation");
            }
        }
        Check(sawDeathAfterGrace, "shared starvation eventually realizes a fractional death after grace");
        var sourceCase = Fixture(100, 50); var sourceCamp = sourceCase.Camps[0];
        foreach (var tile in sourceCase.Tiles) { tile.Freshwater = 0; for (int season = 0; season < 4; season++) tile.Seasons[season].MaxFreshwater = 0; }
        int sourceTile = sourceCase.Map.Neighbors(sourceCamp.Tile).First(); sourceCase.Tiles[sourceTile].Freshwater = 100000;
        for (int season = 0; season < 4; season++) sourceCase.Tiles[sourceTile].Seasons[season].MaxFreshwater = 100000;
        var sourceRecord = Resolve(sourceCase); var sourceHerd = sourceCase.Herds.Single(); var sourceTransfer = sourceRecord.Transfers.Where(t => t.Entity == sourceHerd.Id && t.Kind == "Herd").Sum(t => t.Freshwater);
        var sourceBalance = sourceRecord.EcologyBalances.Single(b => b.Tile == sourceTile);
        Check(sourceTransfer > 0 && sourceBalance.WaterHarvest > 0, "managed herd drinks from the authoritative adjacent freshwater source");
        Near(sourceTransfer, sourceBalance.WaterHarvest, "managed herd freshwater receipt equals the source ledger harvest");
        var reset = Fixture(100, 50); var resetCamp = reset.Camps[0]; resetCamp.PlantFood = 8000; Resolve(reset);
        foreach (var tile in reset.Tiles) { tile.PotentialFood = tile.Freshwater = 0; for (int season = 0; season < 4; season++) { tile.Seasons[season].MaxPotentialFood = 0; tile.Seasons[season].MaxFreshwater = 0; } }
        Resolve(reset); resetCamp.PlantFood = 8000;
        foreach (var tile in reset.Tiles) { tile.PotentialFood = tile.Freshwater = 100000; for (int season = 0; season < 4; season++) { tile.Seasons[season].MaxPotentialFood = 100000; tile.Seasons[season].MaxFreshwater = 100000; } }
        Resolve(reset); Check(resetCamp.DeficitTurns == 0 && reset.Herds.Single().DeficitTurns == 0, "full nutrition resets human and managed-herd starvation state");
    }

    private static void SpeciesReplayAndContinuation()
    {
        var original = Fixture(0, 10); var c = original.Camps[0]; original.Herds.Clear();
        original.Herds.Add(new Herd { Id = original.NextId++, Tile = c.Tile, Species = "goat", Animals = 10, DomesticCamp = c.Id, OwnerSociety = c.SocietyId });
        original.Herds.Add(new Herd { Id = original.NextId++, Tile = c.Tile, Species = "goat", Animals = 10, DomesticCamp = c.Id, OwnerSociety = c.SocietyId });
        original.Herds.Add(new Herd { Id = original.NextId++, Tile = c.Tile, Species = "antelope", Animals = 20, DomesticCamp = c.Id, OwnerSociety = c.SocietyId });
        var reversed = SaveCodec.Load(SaveCodec.Save(original)); reversed.Herds.Reverse();
        Resolve(original); Resolve(reversed); Check(SaveCodec.Fingerprint(original) == SaveCodec.Fingerprint(reversed), "same and multiple species remain deterministic under reversed herd order");
        var continuation = SaveCodec.Load(SaveCodec.Save(original)); Resolve(original); Resolve(continuation); Check(SaveCodec.Fingerprint(original) == SaveCodec.Fingerprint(continuation), "managed species replay remains deterministic after save continuation");
    }

    private static void StaleNutritionAndNewbornBoundary()
    {
        var w = Fixture(100, 50); var herd = w.Herds.Single(); var species = w.Config.GetSpecies(herd.Species); species.GrowthRate = .02; herd.LastNutrition = 0; herd.UnmetFood = herd.UnmetFreshwater = 1e9;
        var r = Resolve(w); var report = r.ManagedHerds.Single(); int expectedAnimals = (int)Math.Floor(100 * Math.Exp(species.GrowthRate * w.Camps[0].ManagedBreedingIntensity * 20));
        Check(report.Nutrition > .999999 && herd.Animals == expectedAnimals, "current nutrition overrides stale prior-generation nutrition for reproduction");
        Near(report.PotentialFood, 8000, "newborns receive no retroactive renewable output");
        Near(r.Transfers.Where(t => t.Entity == herd.Id && t.Kind == "Herd").Sum(t => t.PotentialFood), 100 * species.FoodConsumption * 80, "newborns receive no retroactive forage demand");
    }
    private static void BreedingIntensityPolicy()
    {
        WorldState Case(double b, double years = 20)
        {
            var w = Fixture(100, 50); w.Config.TurnDays = years * w.Config.DaysPerYear; w.Config.Species.Single(s => s.Id == "antelope").GrowthRate = .02;
            w.Camps[0].ManagedBreedingIntensity = b; return w;
        }
        var zero = Case(0); zero.Herds.Single().GrowthRemainder = .37; Resolve(zero); Check(zero.Herds.Single().Animals == 100 && zero.Herds.Single().NewbornAnimals == 0 && Math.Abs(zero.Herds.Single().GrowthRemainder - .37) < 1e-8, "b zero creates no intentional managed births or fractional progress");
        var half = Case(.5); Resolve(half); double halfTotal = half.Herds.Single().Animals + half.Herds.Single().GrowthRemainder;
        var full = Case(1); Resolve(full); double fullTotal = full.Herds.Single().Animals + full.Herds.Single().GrowthRemainder;
        Check(fullTotal > halfTotal && halfTotal > 100, "higher b monotonically raises equal-nutrition reproductive intent");
        foreach (double years in new[] { 5d, 10d, 20d, 40d })
        {
            var healthy = Case(.6, years); Resolve(healthy); double total = healthy.Herds.Single().Animals + healthy.Herds.Single().GrowthRemainder;
            Near(total, 100 * Math.Exp(.02 * .6 * years), "managed b reproduction uses " + years + " year duration");
        }
        var whole = Case(.55, 20); Resolve(whole); double wholeTotal = whole.Herds.Single().Animals + whole.Herds.Single().GrowthRemainder;
        var split = Case(.55, 10); Resolve(split); Resolve(split); Near(split.Herds.Single().Animals + split.Herds.Single().GrowthRemainder, wholeTotal, "managed b reproduction is duration partition invariant in animals plus remainder");
        var underfed = Case(1); var underfedTile = underfed.Tiles[underfed.Camps[0].Tile]; underfedTile.PotentialFood = underfedTile.Freshwater = 0; for (int s = 0; s < underfedTile.Seasons.Length; s++) { underfedTile.Seasons[s].MaxPotentialFood = underfedTile.Seasons[s].MaxFreshwater = 0; underfedTile.Seasons[s].FoodRecoveryPerDay = underfedTile.Seasons[s].BiomassRecoveryPerDay = underfedTile.Seasons[s].WaterRecoveryPerDay = 0; }
        underfed.Config.StarvationGraceTurns = 99; Resolve(underfed); Check(underfed.Herds.Single().Animals == 100 && underfed.Herds.Single().NewbornAnimals == 0, "current zero nutrition blocks managed reproduction even at b one");
        var legacy = Case(0); legacy.Camps[0].ManagedBreedingPolicyInitialized = false; var legacyLoaded = SaveCodec.Load(SaveCodec.Save(legacy)); Near(legacyLoaded.Camps[0].ManagedBreedingIntensity, .55, "prepolicy absence migrates to healthy breeding default");
        string prepolicyFixture = System.IO.Path.Combine("TestResults", "Issue20", "issue17-prepolicy.anthro");
        if (System.IO.File.Exists(prepolicyFixture))
        {
            var historic = SaveCodec.Load(System.IO.File.ReadAllBytes(prepolicyFixture));
            Check(historic.Camps.All(camp => camp.ManagedBreedingPolicyInitialized && Math.Abs(camp.ManagedBreedingIntensity - .55) < 1e-8), "genuine prepolicy save migrates to the healthy breeding default");
        }
        var genuineZero = Case(0); var zeroLoaded = SaveCodec.Load(SaveCodec.Save(genuineZero)); Near(zeroLoaded.Camps[0].ManagedBreedingIntensity, 0, "genuine saved b zero remains authoritative");
        var bad = Case(0); bad.Camps[0].ManagedBreedingIntensity = 2; try { SaveCodec.Load(SaveCodec.Save(bad)); Check(false, "invalid present b rejects"); } catch (System.IO.InvalidDataException) { Check(true, "invalid present b rejects"); }
        var policy = Fixture(20, 10); var c = policy.Camps[0]; var herd = policy.Herds.Single();
        double Recommend() { var o = new AgentObservationBuilder().Build(policy, c.Player); return ManagedBreedingPolicy.Recommend(o, o.Settlements.Single()); }
        Near(Recommend(), .55, "healthy AI breeding band uses .55"); herd.LastNutrition = .5; Near(Recommend(), .45, "observed nutrition pressure lowers AI breeding");
        herd.LastNutrition = 0; herd.StartingAnimals = 100; herd.SurvivingAnimals = 50; herd.ManagementFulfillment = 1; Near(Recommend(), .65, "major loss with visible ample current pasture enables recovery");
        herd.StartingAnimals = herd.SurvivingAnimals = 0; herd.LastNutrition = 1; c.Moves = 1; c.StationaryTurns = 0; Near(Recommend(), .65, "recent move into visible ample pasture enables expansion");
        c.Moves = 0; Near(Recommend(), .55, "unmoved new camp is not treated as expansion");
        herd.StartingAnimals = 50; herd.SurvivingAnimals = 25; var clone = new Herd { Id = policy.NextId++, Tile = herd.Tile, Species = herd.Species, Animals = 10, DomesticCamp = c.Id, OwnerSociety = c.SocietyId, StartingAnimals = 50, SurvivingAnimals = 25, LastNutrition = 1, ManagementFulfillment = 1 }; policy.Herds.Add(clone); Near(Recommend(), .65, "split domestic herds retain summed-loss policy result"); policy.Herds.Remove(clone);
        policy.Tiles[c.Tile].PotentialFood = 0; policy.Tiles[c.Tile].Freshwater = 0; herd.StartingAnimals = 100; herd.SurvivingAnimals = 50; Near(Recommend(), .45, "rich adjacent land cannot recover a herd on depleted occupied pasture"); policy.Tiles[c.Tile].PotentialFood = policy.Tiles[c.Tile].Freshwater = 100000;
        foreach (int n in policy.Map.Neighbors(c.Tile)) policy.Tiles[n].ElevationLevel = 10;
        Near(Recommend(), .45, "impassable peak neighbors do not provide AI destination room");
        foreach (int n in policy.Map.Neighbors(c.Tile)) policy.Tiles[n].ElevationLevel = 1;
        c.Workforce.Assignments = new List<PopulationAssignment> { new PopulationAssignment { Role = WorkforceRoles.Herder, Population = 70 } }; Near(Recommend(), .45, "high observed Herder burden lowers AI breeding"); c.Workforce.Assignments = new List<PopulationAssignment> { new PopulationAssignment { Role = WorkforceRoles.Herder, Population = 10 } };
        var observation = new AgentObservationBuilder().Build(policy, c.Player); var decision = new HeuristicTribePolicy().Decide(observation, c.Mandate); var economic = decision.Orders.Single(o => o.Kind == OrderKind.Economy);
        Check(OrderRules.Validate(observation, economic) == null && economic.Economy.ManagedBreedingIntensity == .65, "Heuristic submits its visible breeding recommendation through the ordinary validated Economy intent");
        var foreignCamp = new Camp { Id = policy.NextId++, Player = 1, Tile = policy.Map.Neighbors(c.Tile).First(), Population = 100 }; policy.Camps.Add(foreignCamp); Ownership.Bind(policy);
        var foreignHerd = new Herd { Id = policy.NextId++, Tile = foreignCamp.Tile, Species = herd.Species, Animals = 100, DomesticCamp = foreignCamp.Id, OwnerSociety = foreignCamp.SocietyId, StartingAnimals = 100, SurvivingAnimals = 1, LastNutrition = 1, ManagementFulfillment = 1 }; policy.Herds.Add(foreignHerd);
        var privateObservation = new AgentObservationBuilder().Build(policy, c.Player); Near(ManagedBreedingPolicy.Recommend(privateObservation, privateObservation.Settlements.Single()), .65, "foreign herd loss stays filtered from the owner AI breeding policy");
    }

    private static void HerdingToolCoverage()
    {
        foreach (double years in new[] { 5d, 10d, 20d, 40d })
        {
            var w = Fixture(100, 50); var c = w.Camps[0]; w.Config.TurnDays = years * w.Config.DaysPerYear;
            var plan = HerdingTooling.Plan(w, c, years);
            Near(plan.Fulfillment, 1, "primitive Herder capacity remains two animals per person across turn durations " + years);
            Near(plan.Capacity, 100, "primitive labor is duration invariant " + years);
            Near(HerdingTooling.RequiredHerders(w, c, years), 50, "primitive staffing requirement is duration invariant " + years);
        }
        var partial = Fixture(100, 20); var pc = partial.Camps[0]; pc.Inventory.HerdingTools = 5;
        var partialPlan = HerdingTooling.Plan(partial, pc, 20);
        Near(partialPlan.CoveredHerderYears, 100, "only five start-stock tools cover five Herder-years per year");
        Near(partialPlan.Fulfillment, .4345, "partial stone-tool coverage changes only management capacity");
        Near(partialPlan.ToolsConsumed, 5, "wear is covered Herder-years per twenty-year reference interval");
        var excess = Fixture(100, 100); var ec = excess.Camps[0]; ec.Inventory.HerdingTools = 100;
        var excessPlan = HerdingTooling.Plan(excess, ec, 20);
        Near(excessPlan.Fulfillment, 1, "tool capacity is bounded by herd requirement");
        Near(excessPlan.ToolsConsumed, 100 / 2.69, "excess assigned Herder labor does not waste tool wear");

        var replacement = Fixture(100, 50); var replacementCamp = replacement.Camps[0]; replacementCamp.Inventory.HerdingTools = 20;
        replacementCamp.ManagedBreedingIntensity = 0; replacementCamp.ManagedBreedingPolicyInitialized = true;
        double plannedReplacement = NeolithicProduction.Demand(replacement, replacementCamp, CraftGood.HerdingTools, 20, replacementCamp.Population, false, false);
        Near(plannedReplacement, 100 / 2.69, "pre-management replacement demand includes useful next-period covered work");
        Resolve(replacement);
        Check(replacementCamp.Inventory.HerdingTools < 1e-9, "realized management work debits start-stock tools once");
        Near(NeolithicProduction.Demand(replacement, replacementCamp, CraftGood.HerdingTools, 20, replacementCamp.Population, true, true), plannedReplacement,
            "post-management replacement demand matches the useful next-period target without double charging");

        var delayed = Fixture(100, 20); var dc = delayed.Camps[0];
        dc.ManagedBreedingIntensity = 0; dc.ManagedBreedingPolicyInitialized = true;
        dc.People.NeolithicTechnologies.Add(NeolithicTech.Carpentry); dc.Workforce.Assign(WorkforceRoles.Carpenter, 1, dc.Population);
        dc.CraftingPriorities = new CraftingPriorities { HandTools = 0, Pots = 0, Cloth = 0, FarmTools = 0, Housing = 0, Tools = 0, AnimalFabric = 0, LightHousing = 0, HerdingTools = 1 };
        dc.Inventory.HandTools = 100; dc.Inventory.Wood = dc.Inventory.Stone = 100;
        var first = Resolve(delayed);
        Near(first.ManagedHerds.Single().ManagementFulfillment, .4, "newly produced Herding Tools do not backdate management capacity");
        Near(dc.Inventory.HerdingTools, 20, "useful Carpenter work creates the next generation's Herding Tools");
        var second = Resolve(delayed);
        Near(second.ManagedHerds.Single().ManagementFulfillment, .538, "start-stock Herding Tools improve the next generation's management");
    }
}
