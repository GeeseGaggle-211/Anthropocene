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
        checks = 0; FullManagedOutput(); UnderManagementAndNoCap(); CurrentNutritionAndNewbornBoundary(); LocalFootprintAndSharedSource(); MovedHunterFoodCohort(); PreparedFoodRemainderLedger(); ProportionalSpeciesManagement(); SharedControlAndSave(); ObservationAndOrderContracts(); PartitionedExposure(); MixedFoodInputBoundary(); MixedAnimalOnlyDoesNotChargePlant(); TypedFabricPriorityBudget(); StorageReceivingCapacity(); SharedStarvationAndSourceLedgers(); SpeciesReplayAndContinuation(); StaleNutritionAndNewbornBoundary(); BreedingIntensityPolicy(); SupportLimitedBreeding(); HerdingToolCoverage(); AcceptedMovementControlsAccessibleSources();
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
        foreach (int id in w.Map.Radius(w.Camps[0].Tile, 1).Where(id => id != w.Camps[0].Tile))
        {
            var neighbor = w.Tiles[id]; neighbor.Food = neighbor.PotentialFood = neighbor.Freshwater = 0;
            for (int s = 0; s < 4; s++) { neighbor.Seasons[s].MaxFood = neighbor.Seasons[s].MaxPotentialFood = neighbor.Seasons[s].MaxFreshwater = 0; }
        }
        tile.PotentialFood = 2560; tile.Freshwater = 2880; for (int s = 0; s < 4; s++) { tile.Seasons[s].MaxPotentialFood = 2560; tile.Seasons[s].MaxFood = 0; tile.Seasons[s].MaxFreshwater = 2880; tile.Seasons[s].FoodRecoveryPerDay = tile.Seasons[s].BiomassRecoveryPerDay = tile.Seasons[s].WaterRecoveryPerDay = 0; }
        w.Config.StarvationGraceTurns = 0; w.Config.Mortality = 0; w.Config.AnnualHumanGrowthRate = 0;
        var r = Resolve(w); var report = r.ManagedHerds.Single();
        Check(report.Nutrition < .81 && report.Nutrition > .79, "managed nutrition uses current forage and water exposure actual=" + report.Nutrition + " forage=" + r.Transfers.Where(t => t.Entity == herd.Id && t.Kind == "Herd").Sum(t => t.PotentialFood) + " unmet=" + herd.UnmetFood + " water=" + herd.UnmetFreshwater);
        Near(report.PotentialFood, report.SurvivingAnimals * 80 * report.Nutrition, "renewable output uses surviving current exposure");
        herd.GrowthRemainder = .9; herd.Animals = 0; var zero = Resolve(w); Check(herd.Animals == 0 && herd.NewbornAnimals == 0 && herd.GrowthRemainder == 0, "zero animals do not reproduce from a stale fraction actual=" + herd.Animals + "/" + herd.NewbornAnimals + "/" + herd.GrowthRemainder);
    }
    private static void LocalFootprintAndSharedSource()
    {
        WorldState Empty()
        {
            var w = Fixture(100, 50); w.Config.AnnualHumanGrowthRate = 0;
            foreach (var tile in w.Tiles)
            {
                tile.Food = tile.PotentialFood = tile.Freshwater = 0;
                for (int s = 0; s < 4; s++)
                {
                    tile.Seasons[s].MaxFood = tile.Seasons[s].MaxPotentialFood = tile.Seasons[s].MaxFreshwater = 0;
                    tile.Seasons[s].FoodRecoveryPerDay = tile.Seasons[s].BiomassRecoveryPerDay = tile.Seasons[s].WaterRecoveryPerDay = 0;
                }
            }
            return w;
        }
        void SetForage(WorldState w, int id, double amount)
        {
            w.Tiles[id].PotentialFood = amount;
            for (int s = 0; s < 4; s++) w.Tiles[id].Seasons[s].MaxPotentialFood = (float)amount;
        }
        void SetWater(WorldState w, int id, double amount)
        {
            w.Tiles[id].FreshwaterSource = FreshwaterSource.RainPool;
            w.Tiles[id].Freshwater = amount;
            for (int s = 0; s < 4; s++) w.Tiles[id].Seasons[s].MaxFreshwater = (float)amount;
        }

        var local = Empty(); var camp = local.Camps.Single(); var herd = local.Herds.Single();
        var neighbors = local.Map.Neighbors(camp.Tile).Where(id => local.Tiles[id].TraversableLand).OrderBy(id => id).ToArray();
        Check(neighbors.Length >= 2, "fixture has separate adjacent pasture and freshwater sources");
        int pasture = neighbors[0], spring = neighbors[1];
        int distant = local.Map.Radius(camp.Tile, 2).Where(id => local.Map.Distance(camp.Tile, id) == 2 && local.Tiles[id].TraversableLand).OrderBy(id => id).First();
        SetForage(local, pasture, 100000); SetWater(local, spring, 100000);
        SetForage(local, distant, 100000); SetWater(local, distant, 100000);
        var localRecord = Resolve(local); var localReport = localRecord.ManagedHerds.Single();
        Near(localReport.Nutrition, 1, "managed herd reaches adjacent pasture and water source");
        Check(localRecord.Transfers.Any(t => t.Entity == herd.Id && t.Tile == pasture && t.PotentialFood > 0), "adjacent land forage is debited at its own tile");
        Check(localRecord.Transfers.Any(t => t.Entity == herd.Id && t.Tile == spring && t.Freshwater > 0), "adjacent freshwater is debited at its own source");
        Check(localRecord.Transfers.All(t => t.Entity != herd.Id || t.Tile != distant), "managed herd never claims land or water at radius two");

        var forecastWorld = Empty(); var forecastCamp = forecastWorld.Camps.Single();
        int destination = forecastWorld.Map.Neighbors(forecastCamp.Tile).Where(id => forecastWorld.Tiles[id].TraversableLand).OrderBy(id => id).First();
        int destinationOnly = forecastWorld.Map.Radius(destination, 1).Where(id =>
            forecastWorld.Map.Distance(forecastCamp.Tile, id) == 2 && forecastWorld.Tiles[id].TraversableLand).OrderBy(id => id).First();
        SetForage(forecastWorld, destinationOnly, 100000); SetWater(forecastWorld, destinationOnly, 100000);
        double forecastYears = forecastWorld.Config.TurnDays / forecastWorld.Config.DaysPerYear;
        double atOrigin = TurnResolver.ForecastManagedAnimalFood(forecastWorld, forecastCamp, 50,
            forecastWorld.Config.GenerationIntervals, forecastYears).AnimalFood;
        double atDestination = TurnResolver.ForecastManagedAnimalFood(forecastWorld, forecastCamp, 50,
            forecastWorld.Config.GenerationIntervals, forecastYears, destination).AnimalFood;
        Check(atOrigin == 0 && atDestination > 0, "destination-aware managed forecast uses only its effective radius-one resources");

        var fractional = Empty(); var fractionalCamp = fractional.Camps.Single();
        fractional.Config.TurnDays = fractional.Config.BaseDays / 2;
        var seasonalSources = fractional.Map.Neighbors(fractionalCamp.Tile)
            .Where(id => fractional.Tiles[id].TraversableLand).OrderBy(id => id).Take(2).ToArray();
        fractional.Tiles[seasonalSources[0]].FreshwaterSource = FreshwaterSource.RainPool;
        fractional.Tiles[seasonalSources[1]].FreshwaterSource = FreshwaterSource.RainPool;
        fractional.Tiles[seasonalSources[0]].Seasons[0].MaxFreshwater = 1000;
        fractional.Tiles[seasonalSources[0]].Seasons[0].WaterRecoveryPerDay = .01f;
        fractional.Tiles[seasonalSources[1]].Seasons[1].MaxFreshwater = 1000;
        fractional.Tiles[seasonalSources[1]].Seasons[1].WaterRecoveryPerDay = .01f;
        var partialClaims = new List<Demand>();
        ManagedHerdLocalClaims.Append(fractional, fractionalCamp.Tile, fractional.Herds.Single().Id,
            0, 100, fractional.Config.TurnDays, partialClaims);
        Near(partialClaims.Where(d => d.Tile == seasonalSources[0]).Sum(d => d.Freshwater), 100,
            "fractional first-season drinking request follows the exposed recharge profile");
        Near(partialClaims.Where(d => d.Tile == seasonalSources[1]).Sum(d => d.Freshwater), 0,
            "unexposed later-season recharge does not attract a fractional-turn water request");
        double firstRecovery = LazyEcology.ProjectEcology(fractional, seasonalSources[0],
            fractional.Config.TurnDays / 2).Freshwater;
        double secondRecovery = LazyEcology.ProjectEcology(fractional, seasonalSources[0],
            fractional.Config.TurnDays).Freshwater;
        Check(firstRecovery > 0 && secondRecovery > firstRecovery && secondRecovery < 1000,
            "unclaimed fractional-turn freshwater recovers gradually within capacity");
        var partialResult = EcologicalPressure.Integrate(fractional, seasonalSources[0],
            new[] { new Demand { Tile = seasonalSources[0], Entity = fractional.Herds.Single().Id,
                Kind = "Herd", Freshwater = 10000 } }, 0, fractional.Config.TurnDays);
        Check(partialResult.Transfers.Single().Freshwater >= 0 && partialResult.Transfers.Single().Freshwater <= 10000 &&
            partialResult.View.Freshwater >= 0 && partialResult.View.Freshwater <= 1000,
            "fractional-turn drinking cannot overdraw the finite source or exceed capacity");
        Near(partialResult.Balance.StartingWater + partialResult.Balance.WaterRecharge,
            partialResult.Balance.EndingWater + partialResult.Balance.WaterHarvest + partialResult.Balance.WaterOverflow,
            "fractional-turn freshwater extraction and regeneration conserve the source");

        var shared = Empty(); var sharedCamp = shared.Camps.Single(); var firstHerd = shared.Herds.Single();
        var secondHerd = new Herd { Id = shared.NextId++, Tile = sharedCamp.Tile, Species = firstHerd.Species,
            Animals = firstHerd.Animals, Capacity = firstHerd.Capacity, DomesticCamp = sharedCamp.Id, OwnerSociety = sharedCamp.SocietyId };
        shared.Herds.Add(secondHerd);
        int sharedTile = shared.Map.Neighbors(sharedCamp.Tile).Where(id => shared.Tiles[id].TraversableLand).OrderBy(id => id).First();
        var species = shared.Config.GetSpecies(firstHerd.Species);
        double foodNeed = firstHerd.Animals * species.FoodConsumption * shared.Config.GenerationIntervals;
        double waterNeed = firstHerd.Animals * species.FreshwaterConsumption * shared.Config.GenerationIntervals;
        SetForage(shared, sharedTile, foodNeed * 1.25); SetWater(shared, sharedTile, waterNeed * 1.25);
        var sharedRecord = Resolve(shared);
        double FirstFood() => sharedRecord.Transfers.Where(t => t.Entity == firstHerd.Id && t.Kind == "Herd").Sum(t => t.PotentialFood);
        double SecondFood() => sharedRecord.Transfers.Where(t => t.Entity == secondHerd.Id && t.Kind == "Herd").Sum(t => t.PotentialFood);
        double FirstWater() => sharedRecord.Transfers.Where(t => t.Entity == firstHerd.Id && t.Kind == "Herd").Sum(t => t.Freshwater);
        double SecondWater() => sharedRecord.Transfers.Where(t => t.Entity == secondHerd.Id && t.Kind == "Herd").Sum(t => t.Freshwater);
        Near(FirstFood(), foodNeed, "first claimant receives bounded shared forage");
        Near(SecondFood(), foodNeed * .25, "later claimant receives only residual shared forage");
        Near(FirstWater(), waterNeed, "first claimant receives bounded shared freshwater");
        Near(SecondWater(), waterNeed * .25, "later claimant receives only residual shared freshwater");
        var balance = sharedRecord.EcologyBalances.Single(b => b.Tile == sharedTile);
        Check(balance.EndingBiomass >= -1e-8 && balance.EndingWater >= -1e-8, "contended tile stocks never go negative");
        Near(balance.BiomassHarvest, FirstFood() + SecondFood(), "shared forage ledger conserves accepted herd receipts");
        Near(balance.WaterHarvest, FirstWater() + SecondWater(), "shared water ledger conserves accepted herd receipts");
    }

    private static void MovedHunterFoodCohort()
    {
        foreach (bool returnToSupply in new[] { false, true })
        {
            var world = Fixture(0, 0); var camp = world.Camps.Single(); world.Herds.Clear();
            int inside = world.Map.Radius(camp.Tile, 2).Where(id => world.Map.Distance(camp.Tile, id) == 2 && world.Tiles[id].TraversableLand).OrderBy(id => id).First();
            int outside = world.Map.Radius(inside, world.Config.HunterMovement).Where(id =>
                world.Map.Distance(camp.Tile, id) > world.Config.SupplyRange && world.Tiles[id].TraversableLand).OrderBy(id => id).First();
            var hunter = new Hunter { Id = world.NextId++, SourceCamp = camp.Id, SocietyId = camp.SocietyId,
                Player = camp.Player, Population = 20, Tile = returnToSupply ? outside : inside };
            world.Hunters.Add(hunter);
            int destination = returnToSupply ? inside : outside;
            var orders = new Dictionary<int, List<Order>> { { camp.Player,
                new List<Order> { new Order(OrderKind.Move, hunter.Id, destination: destination) } } };
            var prepared = TurnPlanner.Prepare(world, orders);
            Check(prepared.Orders.Any(o => o.Entity == hunter.Id && o.Kind == OrderKind.Move),
                "moved-hunter cohort fixture accepts the hunter destination");
            int expectedConsumers = camp.Population + (returnToSupply ? hunter.Population : 0);
            Check(prepared.Needs[camp.Id].FoodConsumers == expectedConsumers,
                "prepared food cohort uses the accepted post-movement hunter connection");
            Near(prepared.Needs[camp.Id].ConsumptionRequired,
                expectedConsumers * world.Config.Consumption * world.Config.GenerationIntervals,
                "prepared food consumption belongs to the connected starting population");
            new TurnResolver().Resolve(world, prepared);
            Check(Supply.Connected(world, hunter) == returnToSupply,
                "resolved hunter supply membership matches the prepared food cohort");
        }
    }
    private static void PreparedFoodRemainderLedger()
    {
        var world = Fixture(0, 0); var camp = world.Camps.Single(); world.Herds.Clear();
        double cohortNeed = camp.Population * world.Config.Consumption * world.Config.GenerationIntervals;
        camp.PlantFood = cohortNeed * .7;
        var hunter = new Hunter { Id = world.NextId++, SourceCamp = camp.Id, SocietyId = camp.SocietyId,
            Player = camp.Player, Population = 20, Tile = camp.Tile };
        world.Hunters.Add(hunter);
        int destination = world.Map.Neighbors(camp.Tile).Where(id => world.Tiles[id].TraversableLand)
            .OrderBy(id => id).First();
        var prepared = TurnPlanner.Prepare(world, new Dictionary<int, List<Order>> { { camp.Player,
            new List<Order> { new Order(OrderKind.Move, hunter.Id, destination: destination) } } });
        var needs = prepared.Needs[camp.Id];
        double initialRemainder = TurnResolver.RemainingFoodTarget(camp, needs);
        var record = new TurnRecord();
        Supply.ProvisionDepartures(world, prepared.Orders, record, prepared.Placement);
        double loaded = hunter.CarriedFood;
        Check(loaded > 0, "moving hunter receives finite provisions from the prepared camp stock");
        Near(TurnResolver.RemainingFoodTarget(camp, needs), initialRemainder + loaded,
            "provision outflow raises the remaining fixed food target");
        hunter.Tile = destination;
        Supply.DepositReturns(world, record);
        Near(TurnResolver.RemainingFoodTarget(camp, needs), initialRemainder,
            "returned provisions lower only the remaining food target");
        Near(needs.FoodConsumers * world.Config.Consumption * world.Config.GenerationIntervals,
            needs.ConsumptionRequired, "provision and return preserve frozen cohort consumption");
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
            w.Config.AnnualHumanGrowthRate = .01145; w.Config.GrowthReserveYears = .1 * w.Config.Neolithic.ReferenceTurnYears;
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
        w.Config.MinimumStorageYears = w.Config.MaximumStorageYears = w.Config.Neolithic.ReferenceTurnYears;
        w.Config.GrowthReserveYears = 2 * w.Config.Neolithic.ReferenceTurnYears; c.Inventory.Pots = 100; c.AnimalFood = 8000;
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
        var underfed = Case(1); foreach (int id in underfed.Map.Radius(underfed.Camps[0].Tile, 1)) { var underfedTile = underfed.Tiles[id]; underfedTile.PotentialFood = underfedTile.Freshwater = 0; for (int s = 0; s < underfedTile.Seasons.Length; s++) { underfedTile.Seasons[s].MaxPotentialFood = underfedTile.Seasons[s].MaxFreshwater = 0; underfedTile.Seasons[s].FoodRecoveryPerDay = underfedTile.Seasons[s].BiomassRecoveryPerDay = underfedTile.Seasons[s].WaterRecoveryPerDay = 0; } }
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
        foreach (int id in policy.Map.Radius(c.Tile, 1)) { policy.Tiles[id].PotentialFood = 0; policy.Tiles[id].Freshwater = 0; }
        herd.StartingAnimals = 100; herd.SurvivingAnimals = 50; Near(Recommend(), .45, "depleted radius-one forage and water limit managed breeding");
        foreach (int id in policy.Map.Radius(c.Tile, 1)) { policy.Tiles[id].PotentialFood = 100000; policy.Tiles[id].Freshwater = 100000; }
        foreach (int n in policy.Map.Neighbors(c.Tile)) policy.Tiles[n].ElevationLevel = 10;
        Near(Recommend(), .45, "impassable peak neighbors do not provide AI destination room");
        foreach (int n in policy.Map.Neighbors(c.Tile)) policy.Tiles[n].ElevationLevel = 1;
        c.Workforce.Assignments = new List<PopulationAssignment> { new PopulationAssignment { Role = WorkforceRoles.Herder, Population = 70 } }; Near(Recommend(), .45, "high observed Herder burden lowers AI breeding"); c.Workforce.Assignments = new List<PopulationAssignment> { new PopulationAssignment { Role = WorkforceRoles.Herder, Population = 10 } };
        c.EconomyAutomationTurns = -1;
        var observation = new AgentObservationBuilder().Build(policy, c.Player); var decision = new HeuristicTribePolicy().Decide(observation, c.Mandate);
        Check(decision.Orders.Any(o => o.Entity == c.Id && (o.Kind == OrderKind.Move || o.Kind == OrderKind.NoOp)) &&
            decision.Orders.All(o => o.Kind != OrderKind.Economy), "Heuristic keeps camp movement in its action slot rather than replacing it with Economy");
        var prepared = TurnPlanner.Prepare(policy, new Dictionary<int, List<Order>> { { c.Player, decision.Orders } });
        var economic = prepared.Orders.Single(o => o.Kind == OrderKind.Economy && o.Entity == c.Id);
        Check(prepared.AutomatedCamps.Contains(c.Id) && economic.Economy.ManagedBreedingIntensity == .65,
            "interturn automation uses the visible breeding recommendation alongside movement");
        var foreignCamp = new Camp { Id = policy.NextId++, Player = 1, Tile = policy.Map.Neighbors(c.Tile).First(), Population = 100 }; policy.Camps.Add(foreignCamp); Ownership.Bind(policy);
        var foreignHerd = new Herd { Id = policy.NextId++, Tile = foreignCamp.Tile, Species = herd.Species, Animals = 100, DomesticCamp = foreignCamp.Id, OwnerSociety = foreignCamp.SocietyId, StartingAnimals = 100, SurvivingAnimals = 1, LastNutrition = 1, ManagementFulfillment = 1 }; policy.Herds.Add(foreignHerd);
        var privateObservation = new AgentObservationBuilder().Build(policy, c.Player); Near(ManagedBreedingPolicy.Recommend(privateObservation, privateObservation.Settlements.Single()), .65, "foreign herd loss stays filtered from the owner AI breeding policy");
    }

    private static void SupportLimitedBreeding()
    {
        WorldState Case(double years, bool waterShort, double mortality = 0)
        {
            var w = Fixture(100, 50); var camp = w.Camps.Single(); var herd = w.Herds.Single();
            w.Config.TurnDays = years * w.Config.DaysPerYear;
            w.Config.StarvationGraceTurns = 0; w.Config.Mortality = mortality;
            herd.GrowthRemainder = .37;
            double intervals = w.Config.TurnDays / w.Config.BaseDays;
            var species = w.Config.GetSpecies(herd.Species);
            double food = herd.Animals * species.FoodConsumption * intervals;
            double water = herd.Animals * species.FreshwaterConsumption * intervals;
            foreach (int id in w.Map.Radius(camp.Tile, 1))
            {
                var tile = w.Tiles[id];
                tile.Freshwater = 0; tile.FreshwaterSource = FreshwaterSource.None;
                if (!waterShort) tile.Food = tile.PotentialFood = 0;
                for (int season = 0; season < 4; season++)
                {
                    tile.Seasons[season].MaxFreshwater = 0;
                    tile.Seasons[season].WaterRecoveryPerDay = 0;
                    if (!waterShort)
                    {
                        tile.Seasons[season].MaxFood = tile.Seasons[season].MaxPotentialFood = 0;
                        tile.Seasons[season].FoodRecoveryPerDay = tile.Seasons[season].BiomassRecoveryPerDay = 0;
                    }
                }
            }
            var center = w.Tiles[camp.Tile];
            center.FreshwaterSource = FreshwaterSource.RainPool;
            center.Freshwater = water * (waterShort ? .51 : 2);
            if (!waterShort) center.PotentialFood = food * .51;
            for (int season = 0; season < 4; season++)
            {
                center.Seasons[season].MaxFreshwater = (float)center.Freshwater;
                if (!waterShort) center.Seasons[season].MaxPotentialFood = (float)center.PotentialFood;
            }
            return w;
        }

        foreach (double years in new[] { .5d, 20d })
            foreach (bool waterShort in new[] { false, true })
            {
                var w = Case(years, waterShort); var herd = w.Herds.Single();
                var report = Resolve(w).ManagedHerds.Single();
                Near(waterShort ? report.WaterNutrition : report.FoodNutrition, .51,
                    "partial " + (waterShort ? "water" : "forage") + " support is measured over " + years + " years", 1e-5);
                Check(report.NewbornAnimals == 0 && herd.Animals == 100,
                    "prior-cohort resource shortage blocks births without a mortality penalty over " + years + " years");
                Near(herd.GrowthRemainder, .37, "resource shortage pauses fractional breeding progress over " + years + " years");
            }

        var mortalityCase = Case(20, true, .18); var hungryHerd = mortalityCase.Herds.Single();
        var hungry = Resolve(mortalityCase).ManagedHerds.Single();
        Check(hungry.StarvationDeaths > 0 && hungry.NewbornAnimals == 0 &&
            hungryHerd.Animals == hungry.SurvivingAnimals,
            "water-short herd declines without replacing starvation deaths in the same turn");
        var continuation = SaveCodec.Load(SaveCodec.Save(mortalityCase));
        Near(continuation.Herds.Single().GrowthRemainder, .37,
            "save continuation preserves paused fractional breeding progress");
        Resolve(mortalityCase); Resolve(continuation);
        Check(SaveCodec.Fingerprint(mortalityCase) == SaveCodec.Fingerprint(continuation),
            "water-short breeding remains exact after save continuation");

        var healthy = Fixture(100, 50); var fullHerd = healthy.Herds.Single();
        healthy.Config.TurnDays = healthy.Config.DaysPerYear / 2;
        fullHerd.GrowthRemainder = .37;
        double fullTarget = (100 + .37) * Math.Exp(healthy.Config.GetSpecies(fullHerd.Species).GrowthRate *
            healthy.Camps.Single().ManagedBreedingIntensity * .5);
        var fullReport = Resolve(healthy).ManagedHerds.Single();
        Near(fullReport.Nutrition, 1, "fully nourished fractional-year herd remains fully supported");
        Near(fullHerd.Animals + fullHerd.GrowthRemainder, fullTarget,
            "fully nourished fractional-year exponential reproduction is unchanged");
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

    private static void AcceptedMovementControlsAccessibleSources()
    {
        void SetupResources(WorldState w, int sourceTile)
        {
            int count = w.Tiles.Length;
            w.Resources = new WorldResourceState
            {
                MaximumQuantity = 100000,
                CatalogHash = ResourceCatalog.Hash, ProfileHash = ResourceCatalog.Hash, ConfigurationHash = ResourceCatalog.Hash,
                Provinces = Array.Empty<GeologicalProvince>(), Province = new ushort[count], History = new ushort[count],
                Host = new byte[count], Marine = new byte[count], Stone = new ushort[count], Clay = new ushort[count], Wood = new ushort[count],
                WoodRecoveryPerYear = .05
            };
            w.Resources.AccessibleParts[(int)MaterialId.Stone] = 10000;
            w.Resources.Stone[sourceTile] = 200;
        }

        // Case 1: Accepted Movement
        {
            var w = Fixture(100, 20); var c = w.Camps[0];
            int origin = c.Tile;
            int dest = w.Map.Neighbors(origin).First(n => w.Tiles[n].TraversableLand);
            int farTile = w.Map.Neighbors(dest).First(n => w.Tiles[n].TraversableLand && w.Map.Distance(origin, n) == 2);
            int originFarTile = w.Map.Neighbors(origin).First(n => w.Tiles[n].TraversableLand && w.Map.Distance(dest, n) == 2);
            SetupResources(w, farTile);
            w.Resources.Stone[originFarTile] = 200;

            c.Inventory.Stone = 0;
            c.Inventory.HandTools = 0;
            c.AnimalFood = 50;
            c.CraftingPriorities = new CraftingPriorities { HandTools = 1, AnimalFabric = 1 };
            c.StonePriority = 1;
            c.EconomyAutomationTurns = 0;
            c.Workforce = new PopulationLedger();
            c.Workforce.Assign(WorkforceRoles.Gatherer, 50, 100);
            c.Workforce.Assign(WorkforceRoles.Craftsperson, 30, 100);
            c.Workforce.Assign(WorkforceRoles.Herder, 20, 100);

            var moveOrder = new Order(OrderKind.Move, c.Id, destination: dest);

            var record = new TurnResolver().Resolve(w, new Dictionary<int, List<Order>>
            {
                [c.Player] = new List<Order> { moveOrder }
            });

            Check(c.Tile == dest, "camp accepted movement moved to destination tile");
            Check(record.MaterialTransfers.Any(t => t.Tile == farTile && t.Material == MaterialId.Stone && t.Amount > 0),
                "camp accesses materials reachable from the accepted destination");
            Check(!record.MaterialTransfers.Any(t => t.Tile == originFarTile),
                "old-location materials do not teleport to accepted destination");
        }

        // Case 2: Rejected Movement (fallback to origin)
        {
            var w = Fixture(100, 20); var c = w.Camps[0];
            int origin = c.Tile;
            int dest = w.Map.Neighbors(origin).First(n => w.Tiles[n].TraversableLand);
            int farTile = w.Map.Neighbors(dest).First(n => w.Tiles[n].TraversableLand && w.Map.Distance(origin, n) == 2);
            int originFarTile = w.Map.Neighbors(origin).First(n => w.Tiles[n].TraversableLand && w.Map.Distance(dest, n) == 2);
            SetupResources(w, farTile);
            w.Resources.Stone[originFarTile] = 200;

            c.Inventory.Stone = 0;
            c.Inventory.HandTools = 0;
            c.AnimalFood = 50;
            c.CraftingPriorities = new CraftingPriorities { HandTools = 1, AnimalFabric = 1 };
            c.StonePriority = 1;
            c.EconomyAutomationTurns = 0;
            c.Workforce = new PopulationLedger();
            c.Workforce.Assign(WorkforceRoles.Gatherer, 50, 100);
            c.Workforce.Assign(WorkforceRoles.Craftsperson, 30, 100);
            c.Workforce.Assign(WorkforceRoles.Herder, 20, 100);

            var moveOrder = new Order(OrderKind.Move, c.Id, destination: -5);

            var record = new TurnResolver().Resolve(w, new Dictionary<int, List<Order>>
            {
                [c.Player] = new List<Order> { moveOrder }
            });

            Check(c.Tile == origin, "camp rejected movement uses fallback origin tile");
            Check(record.MaterialTransfers.Any(t => t.Tile == originFarTile && t.Material == MaterialId.Stone && t.Amount > 0),
                "camp accesses materials reachable from validated fallback origin");
            Check(!record.MaterialTransfers.Any(t => t.Tile == farTile),
                "unreachable destination materials do not teleport to rejected fallback location");
            Check(c.Inventory.Stone > 0, "stone extracted from fallback origin neighbor source");
        }
    }
}
