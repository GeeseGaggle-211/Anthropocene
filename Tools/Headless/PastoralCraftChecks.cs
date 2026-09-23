using System;
using System.Collections.Generic;
using System.Linq;
using Anthropocene.Simulation;

internal static class PastoralCraftChecks
{
    private static int checks;
    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception("Pastoral crafts: " + message);
    }
    private static void Near(double actual, double expected, string message, double tolerance = 1e-8)
        => Check(Math.Abs(actual - expected) <= tolerance, message + " actual=" + actual + " expected=" + expected);

    private static WorldState HusbandryWorld()
    {
        var w = NeolithicChecks.Fixture(false);
        var c = w.Camps[0];
        c.Technology = Technology.AnimalHusbandry;
        c.Technologies[(int)Technology.AnimalHusbandry] = true;
        c.People.Progression.Era = Era.Neolithic;
        w.FixedHousing = new List<FixedHousingStock>();
        return w;
    }

    private static void EnableBothPaths(WorldState w, Camp c)
    {
        c.Technology = Technology.Cultivation;
        c.Technologies[(int)Technology.AnimalHusbandry] = true;
        c.Technologies[(int)Technology.Cultivation] = true;
        c.People.Progression.Era = Era.Neolithic;
    }

    private static CraftRecipe Recipe(WorldState w, CraftGood good)
        => NeolithicProduction.Recipes(w.Config).Single(recipe => recipe.Good == good);

    private static ProductionReceipt Produce(WorldState w, Camp c, CraftGood good, string role, double workerIntervals, double demand)
    {
        var record = new TurnRecord();
        double equipment = 100000;
        var coverage = new ToolCoverageBudget(w, c, w.Config.GenerationIntervals);
        NeolithicProduction.Produce(w, c, Recipe(w, good), role, workerIntervals,
            w.Config.GenerationIntervals, demand, ref equipment, record, coverage);
        return record.Production.SingleOrDefault(receipt => receipt.Good == good);
    }

    private static void AddInputs(Camp c, CraftRecipe recipe, double quantity)
    {
        c.AnimalFood += recipe.AnimalFood * quantity;
        c.Inventory.AnimalFabric += recipe.AnimalFabric * quantity;
        c.Inventory.Stone += recipe.Stone * quantity;
        c.Inventory.Wood += recipe.Wood * quantity;
    }

    private static TurnRecord ResolveEconomy(WorldState w, Camp c, params PopulationAssignment[] assignments)
    {
        var intent = EconomicIntent.From(c);
        intent.Assignments = assignments.ToList();
        intent.Priorities = c.CraftingPriorities;
        intent.AutomationTurns = 0;
        var order = new Order(OrderKind.Economy, c.Id) { Economy = intent };
        return new TurnResolver().Resolve(w, new Dictionary<int, List<Order>> { [c.Player] = new List<Order> { order } });
    }

    public static int Run()
    {
        checks = 0;
        RecipeRatiosAndGeneralistRates();
        BlockedInputsAndToolsAreAtomic();
        SharedClothingAndDistinctHousingFeedstock();
        MixedModeClothingDemandDoesNotManufactureDuplicateFabric();
        PortableHousingMovesAndPersists();
        MixedHousingDecayFundsOnlyTheActualShortfall();
        HusbandryEconomyUnlocksSharedWeaving();
        PastoralObservationContractIsFilteredAndPure();
        PastoralIntentCopiesStampsAndRejectsInvalidValues();
        PastoralLegitimateZerosSurviveSaveLoad();
        Console.WriteLine("PASS: " + checks + " pastoral craft assertions");
        return checks;
    }

    private static void RecipeRatiosAndGeneralistRates()
    {
        var w = HusbandryWorld(); var c = w.Camps[0];
        c.Inventory.HandTools = 1000;
        c.CraftingPriorities = new CraftingPriorities { AnimalFabric = 1, LightHousing = 1, HerdingTools = 1 };

        c.AnimalFood = 2; c.Inventory.AnimalFabric = 0;
        var fabric = Produce(w, c, CraftGood.AnimalFabric, WorkforceRoles.Craftsperson, 8, 2);
        Check(fabric != null, "a Husbandry Craftsperson can make Animal Fabric before Weaving mastery");
        Near(fabric.Output, 2, "generalist Weaver work runs at one quarter of the specialist rate");
        Near(fabric.AnimalFood, 2, "Animal Fabric consumes one Animal Product per unit");
        Near(c.AnimalFood, 0, "the product feedstock is actually debited");
        Near(c.Inventory.AnimalFabric, 2, "Animal Fabric enters its distinct inventory");
        Near(c.Inventory.HandTools, 998, "realized Animal Fabric output consumes shared manufacturing tools");

        c.Inventory.AnimalFabric = 3;
        var shelter = Produce(w, c, CraftGood.LightHousing, WorkforceRoles.Craftsperson, 384, 3);
        Check(shelter != null, "a Husbandry Craftsperson can make Light Housing before Construction mastery");
        Near(shelter.Output, 3, "generalist Builder work uses the shared quarter-rate calibration");
        Near(shelter.AnimalFabric, 3, "Light Housing consumes one Animal Fabric per unit");
        Near(c.Inventory.AnimalFabric, 0, "portable shelter consumes Animal Fabric stock");
        Near(c.Inventory.LightHousingUnits, 3, "Light Housing enters portable inventory");
        Near(c.Inventory.HousingUnits, 0, "pastoral shelter does not become legacy fixed Housing");

        c.Inventory.Wood = 2; c.Inventory.Stone = 2;
        var tools = Produce(w, c, CraftGood.HerdingTools, WorkforceRoles.Craftsperson, 8, 2);
        Check(tools != null, "a Husbandry Craftsperson can make Herding Tools before Carpentry mastery");
        Near(tools.Output, 2, "generalist Carpenter work uses the shared quarter-rate calibration");
        Near(tools.Wood, 2, "each Herding Tool consumes one Wood");
        Near(tools.Stone, 2, "each Herding Tool consumes one Stone");
        Near(c.Inventory.Wood, 0, "Herding Tool Wood input is debited");
        Near(c.Inventory.Stone, 0, "Herding Tool Stone input is debited");
        Near(c.Inventory.HerdingTools, 2, "Herding Tools enter their distinct inventory");

        var agriculture = NeolithicChecks.Fixture(true); var agriculturalCamp = agriculture.Camps[0];
        agriculturalCamp.People.NeolithicTechnologies.Add(NeolithicTech.Weaving);
        agriculturalCamp.Inventory.HandTools = 10;
        agriculturalCamp.AnimalFood = 10;
        Check(Produce(agriculture, agriculturalCamp, CraftGood.AnimalFabric, WorkforceRoles.Weaver, 10, 10) == null,
            "the Husbandry-only recipe is unavailable to an Agriculture-only camp");
        Near(agriculturalCamp.AnimalFood, 10, "the unavailable recipe leaves food provenance untouched");
    }

    private static void BlockedInputsAndToolsAreAtomic()
    {
        foreach (CraftGood good in new[] { CraftGood.AnimalFabric, CraftGood.LightHousing, CraftGood.HerdingTools })
        {
            var w = HusbandryWorld(); var c = w.Camps[0]; var recipe = Recipe(w, good);
            c.Inventory.HandTools = 0; c.Inventory.Tools = 0;
            AddInputs(c, recipe, 2);
            double food = c.AnimalFood, fabric = c.Inventory.AnimalFabric, stone = c.Inventory.Stone, wood = c.Inventory.Wood;
            Check(Produce(w, c, good, WorkforceRoles.Craftsperson, 1000, 2) == null,
                good + " production is blocked when manufacturing-tool capacity is absent");
            Near(c.AnimalFood, food, good + " keeps Animal Product when tools block all output");
            Near(c.Inventory.AnimalFabric, fabric, good + " keeps Animal Fabric when tools block all output");
            Near(c.Inventory.Stone, stone, good + " keeps Stone when tools block all output");
            Near(c.Inventory.Wood, wood, good + " keeps Wood when tools block all output");

            c.Inventory.HandTools = 100;
            if (recipe.AnimalFood > 0) c.AnimalFood = 0;
            else if (recipe.AnimalFabric > 0) c.Inventory.AnimalFabric = 0;
            else if (recipe.Stone > 0) c.Inventory.Stone = 0;
            double foodBefore = c.AnimalFood, fabricBefore = c.Inventory.AnimalFabric;
            double stoneBefore = c.Inventory.Stone, woodBefore = c.Inventory.Wood;
            Check(Produce(w, c, good, WorkforceRoles.Craftsperson, 1000, 2) == null,
                good + " production is blocked when any required feedstock is absent");
            Near(c.AnimalFood, foodBefore, good + " does not debit Animal Product on a blocked recipe");
            Near(c.Inventory.AnimalFabric, fabricBefore, good + " does not debit Animal Fabric on a blocked recipe");
            Near(c.Inventory.Stone, stoneBefore, good + " does not debit Stone on a blocked recipe");
            Near(c.Inventory.Wood, woodBefore, good + " does not debit Wood on a blocked recipe");
        }
    }

    private static void SharedClothingAndDistinctHousingFeedstock()
    {
        var w = HusbandryWorld(); var c = w.Camps[0];
        c.Population = 8;
        c.Inventory.Cloth = 3;
        c.Inventory.AnimalFabric = 10;
        NeolithicProduction.ResolveNeeds(w, c, w.Config.Neolithic.ReferenceTurnYears);
        Near(c.FabricConsumed, 8, "both fabric types satisfy one shared clothing need");
        Near(c.Inventory.Cloth, 0, "plant Cloth is consumed before Animal Fabric");
        Near(c.Inventory.AnimalFabric, 5, "Animal Fabric supplies only the residual clothing need");
        Near(c.ClothPerCapita, 1, "combined clothing inventory provides full first-pass coverage");

        c.People.NeolithicTechnologies.Add(NeolithicTech.Construction);
        c.Inventory.HandTools = 100;
        c.Inventory.Cloth = 100;
        c.Inventory.AnimalFabric = 2;
        c.Inventory.LightHousingUnits = 0;
        c.Inventory.HousingUnits = 0;
        var housing = Produce(w, c, CraftGood.LightHousing, WorkforceRoles.Builder, 64, 2);
        Check(housing != null, "a Builder can turn Animal Fabric into portable shelter");
        Near(housing.Output, 2, "one Animal Fabric produces one Light Housing unit before tool consumption");
        Near(housing.AnimalFabric, 2, "Light Housing records its Animal Fabric provenance");
        Near(c.Inventory.AnimalFabric, 0, "Light Housing spends the distinct Animal Fabric feedstock");
        Near(c.Inventory.Cloth, 100, "plant Cloth cannot substitute for Light Housing feedstock");
        Near(c.Inventory.LightHousingUnits, 2, "Light Housing remains a separate portable finished good");
        Near(c.Inventory.HousingUnits, 0, "making Light Housing does not create fixed Housing inventory");
    }

    private static void MixedModeClothingDemandDoesNotManufactureDuplicateFabric()
    {
        var mixed = HusbandryWorld(); var c = mixed.Camps[0]; EnableBothPaths(mixed, c);
        c.Population = 4; c.PlantFood = 100; c.AnimalFood = 100; c.Inventory.HandTools = 100;
        c.Inventory.AnimalFabric = 0; c.Inventory.Cloth = 0;
        Near(NeolithicProduction.Demand(mixed, c, CraftGood.AnimalFabric, 20), 8,
            "a mixed Agriculture/Husbandry camp forecasts the shared one-turn clothing need once");
        c.Inventory.AnimalFabric = 8;
        c.People.NeolithicTechnologies.Add(NeolithicTech.Weaving);
        c.Workforce.Assign(WorkforceRoles.Weaver, 1, c.Population);
        c.CraftingPriorities = new CraftingPriorities { Cloth = 1 };
        Near(NeolithicProduction.Demand(mixed, c, CraftGood.Cloth, 20), 0,
            "Animal Fabric stock satisfies the common clothing target for plant Cloth demand");
        var mixedRecord = ResolveEconomy(mixed, c, new PopulationAssignment { Role = WorkforceRoles.Weaver, Population = 1 });
        Check(!mixedRecord.Production.Any(receipt => receipt.Good == CraftGood.Cloth && receipt.Output > 0),
            "a mixed camp's Cloth priority cannot manufacture goods after Animal Fabric fills apparel demand");

        var animalEnough = HusbandryWorld(); c = animalEnough.Camps[0];
        c.Population = 4; c.PlantFood = 100000; c.AnimalFood = 100;
        c.People.NeolithicTechnologies.Add(NeolithicTech.Weaving);
        c.Workforce.Assign(WorkforceRoles.Weaver, 1, c.Population);
        c.Inventory.Cloth = 8; c.Inventory.AnimalFabric = 0; c.Inventory.HandTools = 100;
        c.CraftingPriorities = new CraftingPriorities { AnimalFabric = 1, LightHousing = 0 };
        var animalRecord = ResolveEconomy(animalEnough, c, new PopulationAssignment { Role = WorkforceRoles.Weaver, Population = 1 });
        Check(!animalRecord.Production.Any(receipt => receipt.Good == CraftGood.AnimalFabric && receipt.Output > 0),
            "Animal Fabric is not manufactured when Cloth already fills apparel need and Light Housing is disabled");
    }

    private static void PortableHousingMovesAndPersists()
    {
        var w = HusbandryWorld(); var c = w.Camps[0];
        w.Config.TurnDays = 20 * w.Config.DaysPerYear;
        w.Config.Neolithic.RudimentaryExpectationRampYears = 1;
        c.Population = 45;
        c.PlantFood = 100000;
        c.Inventory.Cloth = 1000;
        c.Inventory.LightHousingUnits = 10;
        c.Inventory.HousingUnits = 0;
        int origin = c.Tile;
        w.FixedHousing.Add(new FixedHousingStock { CampId = c.Id, Tile = origin, Units = 8, LastUpdatedDays = w.Days });
        int destination = w.Map.Neighbors(origin).Where(id => w.CanEnter(id, false, c)).OrderBy(id => id).First();
        var resolver = new TurnResolver();
        var move = new Order(OrderKind.Move, c.Id, destination: destination);
        Check(resolver.Validate(w, 0, move) == null, "an ordinary Husbandry camp can relocate to adjacent land");

        string beforeQuery = SaveCodec.Fingerprint(w);
        double fixedNow = NeolithicProduction.FixedHousingAt(w, c.Id, origin, w.Days);
        double fixedAfterTwentyYears = NeolithicProduction.FixedHousingAt(w, c.Id, origin, w.Days + 20 * w.Config.DaysPerYear);
        Near(fixedNow, 8, "the original fixed settlement stock is queryable by tile");
        Near(fixedAfterTwentyYears, 7.2, "fixed Housing depreciates by ten percent over twenty years");
        Check(SaveCodec.Fingerprint(w) == beforeQuery, "future fixed-housing queries are pure");

        var orders = new Dictionary<int, List<Order>> { [0] = new List<Order> { move } };
        resolver.Resolve(w, orders);
        Check(c.Tile == destination, "the submitted Move actually relocates the camp");
        Near(NeolithicProduction.FixedHousingAt(w, c.Id, origin, w.Days), 7.2, "fixed capital stays at its origin after relocation");
        Near(NeolithicProduction.FixedHousingAt(w, c.Id, destination, w.Days), 0, "fixed capital does not follow the camp");
        Near(c.Inventory.LightHousingUnits, 9, "portable Housing follows the camp and depreciates for elapsed time");
        Near(NeolithicProduction.HousingSupply(w, c, w.Days), 9, "the moved camp's shelter supply uses portable stock at its new tile");
        Check(w.Days > w.FixedHousing.Single().LastUpdatedDays, "the away-site save fixture has positive elapsed time on latent fixed stock");
        double savedFixedUnits = w.FixedHousing.Single().Units;
        double savedFixedTimestamp = w.FixedHousing.Single().LastUpdatedDays;
        NeolithicProduction.ResolveNeeds(w, c, 0);
        Near(c.HousingPerCapita, .8, "portable shelter supplies the same four-people-per-unit coverage after moving");
        Near(c.QoL, .9, "portable shelter contributes the same half-weight first-pass housing QoL");
        double awaySupply = NeolithicProduction.HousingSupply(w, c, w.Days);
        var restored = SaveCodec.Load(SaveCodec.Save(w));
        var restoredCamp = restored.Camps.Single();
        Check(SaveCodec.Fingerprint(restored) == SaveCodec.Fingerprint(w), "portable and fixed Housing state round trips exactly");
        Near(restored.FixedHousing.Single().Units, savedFixedUnits, "away-site save preserves unmaterialized fixed stock");
        Near(restored.FixedHousing.Single().LastUpdatedDays, savedFixedTimestamp, "away-site save preserves the fixed-stock latent timestamp");
        Near(NeolithicProduction.HousingSupply(restored, restoredCamp, restored.Days), awaySupply, "shelter query agrees after save/load while away");
        Near(NeolithicProduction.FixedHousingAt(restored, restoredCamp.Id, origin, restored.Days), 7.2, "saved fixed stock retains its original tile");

        int returnTile = origin;
        var returnOrder = new Order(OrderKind.Move, restoredCamp.Id, destination: returnTile);
        Check(resolver.Validate(restored, 0, returnOrder) == null, "a saved camp can return to its former settlement");
        resolver.Resolve(restored, new Dictionary<int, List<Order>> { [0] = new List<Order> { returnOrder } });
        Check(restoredCamp.Tile == origin, "the return Move relocates the saved camp to its former tile");
        Near(NeolithicProduction.FixedHousingAt(restored, restoredCamp.Id, origin, restored.Days), 6.48, "fixed settlement capital continues to decay while the camp is away");
        Near(NeolithicProduction.HousingSupply(restored, restoredCamp, restored.Days), 14.58, "returning combines decayed fixed and portable shelter at the same tile");
        NeolithicProduction.ResolveNeeds(restored, restoredCamp, 0);
        Near(restoredCamp.HousingPerCapita, 1, "fixed and portable shelter use the same first-pass coverage scale after return");
        Near(restoredCamp.QoL, 1, "fixed-plus-portable shelter restores full first-pass housing QoL");
    }

    private static void MixedHousingDecayFundsOnlyTheActualShortfall()
    {
        var w = HusbandryWorld(); var c = w.Camps[0]; EnableBothPaths(w, c);
        c.Population = 8; c.PlantFood = 100000; c.Inventory.HandTools = 100;
        c.Inventory.Wood = 100; c.Inventory.Clay = 100;
        c.Inventory.LightHousingUnits = 1; c.Inventory.HousingUnits = 0;
        c.People.NeolithicTechnologies.Add(NeolithicTech.Construction);
        c.Workforce.Assign(WorkforceRoles.Builder, 1, c.Population);
        c.CraftingPriorities = new CraftingPriorities { Housing = 1, LightHousing = 0 };
        w.FixedHousing.Add(new FixedHousingStock { CampId = c.Id, Tile = c.Tile, Units = 1, LastUpdatedDays = w.Days });
        var record = new TurnRecord();
        NeolithicProduction.ResolveCampNeolithic(w, c, w.Config.GenerationIntervals, 20, record);
        double housingOutput = record.Production.Where(receipt => receipt.Good == CraftGood.Housing).Sum(receipt => receipt.Output);
        Near(housingOutput, .2, "fixed and portable stocks jointly fund housing demand after one depreciation each");
        Near(w.FixedHousing.Single().Units - housingOutput, .9, "fixed shelter receives one 20-year depreciation before new Housing is added");
        Near(c.Inventory.LightHousingUnits, .9, "portable shelter receives one 20-year depreciation");
    }

    private static void HusbandryEconomyUnlocksSharedWeaving()
    {
        var w = HusbandryWorld(); var c = w.Camps[0];
        c.Population = 4;
        c.PlantFood = 100000;
        c.AnimalFood = 20;
        c.Inventory.HandTools = 100000;
        w.Config.Neolithic.CraftMasteryThreshold = .5;
        var intent = EconomicIntent.From(c);
        intent.Assignments = new List<PopulationAssignment> { new PopulationAssignment { Role = WorkforceRoles.Craftsperson, Population = 1 } };
        intent.Priorities = new CraftingPriorities { AnimalFabric = 1 };
        intent.AutomationTurns = 0;
        var order = new Order(OrderKind.Economy, c.Id) { Economy = intent };
        var record = new TurnResolver().Resolve(w, new Dictionary<int, List<Order>> { [0] = new List<Order> { order } });
        var receipt = record.Production.SingleOrDefault(production => production.Good == CraftGood.AnimalFabric);
        Check(receipt != null && receipt.Output > 0, "ordinary Economy intents unlock Husbandry-first useful crafting");
        Check(c.People.NeolithicTechnologies.Contains(NeolithicTech.Weaving), "useful Animal Fabric work advances the shared Weaving track");
        Check(c.People.UnlockedSpecialists.Contains(WorkforceRoles.Weaver), "shared Weaving mastery unlocks the ordinary Weaver role");
        Check(!c.People.NeolithicTechnologies.Contains("Dairying"), "pastoral production does not create a Dairying technology");
    }

    private static void PastoralObservationContractIsFilteredAndPure()
    {
        var w = HusbandryWorld(); var own = w.Camps[0];
        own.Inventory.AnimalFabric = 2.25; own.Inventory.LightHousingUnits = 3.5; own.Inventory.HerdingTools = 4.75;
        own.ManagedBreedingIntensity = .42; own.ManagedBreedingPolicyInitialized = true;
        own.CraftingPriorities = new CraftingPriorities { AnimalFabric = .2, LightHousing = .4, HerdingTools = .6 };
        var ownedHerd = new Herd { Id = w.NextId++, Tile = own.Tile, Animals = 6, DomesticCamp = own.Id, Species = w.Config.Species.First(s => s.Domesticable).Id, LastNutrition = .8, LastFoodNutrition = .7, LastWaterNutrition = .9, ManagementFulfillment = .5, AnimalFoodPotential = 9, AnimalFoodProduced = 4 };
        w.Herds.Add(ownedHerd);

        int rivalTile = w.Map.Neighbors(own.Tile).OrderBy(id => id).First();
        var rival = new Camp { Id = w.NextId++, Player = 1, Tile = rivalTile, Population = 12, PlantFood = 12345 };
        rival.Inventory.AnimalFabric = 9876; rival.Inventory.LightHousingUnits = 8765; rival.Inventory.HerdingTools = 7654;
        rival.Technology = Technology.AnimalHusbandry; rival.Technologies[(int)Technology.AnimalHusbandry] = true;
        w.Camps.Add(rival);
        w.Herds.Add(new Herd { Id = w.NextId++, Tile = rivalTile, Animals = 23, DomesticCamp = rival.Id, Species = ownedHerd.Species, LastNutrition = .25, LastFoodNutrition = .2, LastWaterNutrition = .3, ManagementFulfillment = .1, AnimalFoodPotential = 77, AnimalFoodProduced = 66 });
        Ownership.Bind(w);

        string before = SaveCodec.Fingerprint(w);
        var observed = new AgentObservationBuilder().Build(w, own.Player);
        string json = ObservationContract.Serialize(observed, own.People.Mandate);
        var dto = ObservationContract.Deserialize(json);
        Check(dto.Schema == 11 && AgentObservation.SchemaVersion == 11, "pastoral observation uses DTO schema 11");
        var ownDto = dto.Settlements.Single(settlement => settlement.Id == own.Id);
        Near(ownDto.AnimalFabric, 2.25, "schema 11 exposes owned Animal Fabric stock");
        Near(ownDto.LightHousing, 3.5, "schema 11 exposes owned portable Housing stock");
        Near(ownDto.HerdingTools, 4.75, "schema 11 exposes owned Herding Tool stock");
        Check(ownDto.Husbandry, "schema 11 identifies the owned Husbandry control surface");
        Near(ownDto.Economy.Priorities.AnimalFabric, .2, "schema 11 carries the pastoral Animal Fabric priority");
        Near(ownDto.Economy.Priorities.LightHousing, .4, "schema 11 carries the pastoral Light Housing priority");
        Near(ownDto.Economy.Priorities.HerdingTools, .6, "schema 11 carries the Herding Tool priority");
        var ownedDtoHerd = dto.Herds.Single(herd => herd.Id == ownedHerd.Id);
        Check(ownedDtoHerd.OwnedDomestic && ownedDtoHerd.ExactCount && ownedDtoHerd.DomesticCamp == own.Id,
            "owned herd observation retains exact management identity");
        Near(ownedDtoHerd.FoodNutrition, .7, "owned herd observation exposes food nutrition");
        Near(ownedDtoHerd.AcceptedFood, 4, "owned herd observation exposes accepted Animal Product");
        var rivalDtoHerd = dto.Herds.Single(herd => herd.Domestic && herd.Id != ownedHerd.Id);
        Check(!rivalDtoHerd.OwnedDomestic && !rivalDtoHerd.ExactCount && rivalDtoHerd.DomesticCamp == -1,
            "foreign domestic herd count and management identity remain filtered");
        Near(rivalDtoHerd.FoodNutrition + rivalDtoHerd.WaterNutrition + rivalDtoHerd.Nutrition + rivalDtoHerd.ManagementFulfillment + rivalDtoHerd.PotentialFood + rivalDtoHerd.AcceptedFood, 0,
            "foreign domestic herd nutrition and production diagnostics remain hidden");
        Check(dto.Settlements.All(settlement => settlement.Id != rival.Id), "foreign camp inventory DTO is omitted");
        Check(!json.Contains("9876") && !json.Contains("8765") && !json.Contains("7654") && !json.Contains("12345"),
            "serialized observation contains no foreign pastoral inventories or food stock");
        Check(SaveCodec.Fingerprint(w) == before, "building and serializing schema 11 observation leaves authoritative state unchanged");
    }

    private static void PastoralIntentCopiesStampsAndRejectsInvalidValues()
    {
        var w = HusbandryWorld(); var c = w.Camps[0];
        c.CraftingPriorities = new CraftingPriorities { AnimalFabric = .15, LightHousing = .35, HerdingTools = .55 };
        c.ManagedBreedingIntensity = .45; c.ManagedBreedingPolicyInitialized = true;
        var source = EconomicIntent.From(c);
        var copy = source.Copy();
        Near(copy.Priorities.AnimalFabric, .15, "EconomicIntent copy preserves Animal Fabric priority");
        Near(copy.Priorities.LightHousing, .35, "EconomicIntent copy preserves Light Housing priority");
        Near(copy.Priorities.HerdingTools, .55, "EconomicIntent copy preserves Herding Tool priority");
        Near(copy.ManagedBreedingIntensity, .45, "EconomicIntent copy preserves pastoral breeding policy");
        Check(!ReferenceEquals(copy.Priorities, source.Priorities), "EconomicIntent copy owns an independent priorities object");
        string stamp = copy.Stamp;
        copy.Priorities.AnimalFabric = .16;
        Check(copy.Stamp != stamp, "EconomicIntent stamp includes Animal Fabric priority");
        stamp = copy.Stamp; copy.Priorities.LightHousing = .36;
        Check(copy.Stamp != stamp, "EconomicIntent stamp includes Light Housing priority");
        stamp = copy.Stamp; copy.Priorities.HerdingTools = .56;
        Check(copy.Stamp != stamp, "EconomicIntent stamp includes Herding Tool priority");

        var observation = new AgentObservationBuilder().Build(w, c.Player);
        var proposed = EconomicIntent.From(c);
        proposed.Priorities.AnimalFabric = double.NaN;
        Check(EconomicIntents.Validate(observation, observation.Settlements.Single(s => s.Id == c.Id), proposed) != null,
            "validated Economy intent rejects NaN Animal Fabric priority");
        proposed = EconomicIntent.From(c); proposed.Priorities.LightHousing = -0.01;
        Check(EconomicIntents.Validate(observation, observation.Settlements.Single(s => s.Id == c.Id), proposed) != null,
            "validated Economy intent rejects negative Light Housing priority");
        proposed = EconomicIntent.From(c); proposed.Priorities.HerdingTools = double.NaN;
        Check(EconomicIntents.Validate(observation, observation.Settlements.Single(s => s.Id == c.Id), proposed) != null,
            "validated Economy intent rejects NaN Herding Tool priority");
        proposed = EconomicIntent.From(c); proposed.ManagedBreedingIntensity = double.NaN;
        Check(EconomicIntents.Validate(observation, observation.Settlements.Single(s => s.Id == c.Id), proposed) != null,
            "validated Economy intent rejects NaN breeding intensity");
        proposed = EconomicIntent.From(c); proposed.ManagedBreedingIntensity = -0.01;
        Check(EconomicIntents.Validate(observation, observation.Settlements.Single(s => s.Id == c.Id), proposed) != null,
            "validated Economy intent rejects negative breeding intensity");
    }

    private static void PastoralLegitimateZerosSurviveSaveLoad()
    {
        var w = HusbandryWorld(); var c = w.Camps[0];
        c.Inventory.AnimalFabric = 0; c.Inventory.LightHousingUnits = 0; c.Inventory.HerdingTools = 0;
        c.CraftingPriorities = new CraftingPriorities { AnimalFabric = 0, LightHousing = 0, HerdingTools = 0 };
        c.ManagedBreedingIntensity = 0; c.ManagedBreedingPolicyInitialized = true;
        var loaded = SaveCodec.Load(SaveCodec.Save(w)); var restored = loaded.Camps.Single();
        Near(restored.Inventory.AnimalFabric, 0, "zero Animal Fabric stock is preserved by save/load");
        Near(restored.Inventory.LightHousingUnits, 0, "zero portable Housing stock is preserved by save/load");
        Near(restored.Inventory.HerdingTools, 0, "zero Herding Tool stock is preserved by save/load");
        Near(restored.CraftingPriorities.AnimalFabric, 0, "zero Animal Fabric priority is preserved by save/load");
        Near(restored.CraftingPriorities.LightHousing, 0, "zero Light Housing priority is preserved by save/load");
        Near(restored.CraftingPriorities.HerdingTools, 0, "zero Herding Tool priority is preserved by save/load");
        Near(restored.ManagedBreedingIntensity, 0, "zero managed breeding policy is preserved by save/load");
        Check(SaveCodec.Fingerprint(w) == SaveCodec.Fingerprint(loaded), "pastoral zero state round trips with the same authoritative fingerprint");
    }
}
