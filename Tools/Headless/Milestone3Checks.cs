using System;
using System.Collections.Generic;
using System.Linq;
using Anthropocene.Simulation;

internal static class Milestone3Checks
{
    private static int checks;
    private static void Check(bool condition, string message) { checks++; if (!condition) throw new Exception("Milestone 3: " + message); }
    private static Dictionary<int, List<Order>> Idle() => new Dictionary<int, List<Order>>();
    internal static WorldState Flat(float capacity = 10000, float recovery = 1)
    {
        var cfg = SimulationConfig.Small(1); cfg.Width = 24; cfg.Height = 16; cfg.ExperienceThreshold = 1000000;
        var w = new WorldState { Config = cfg, Seed = 13, Tiles = new Tile[384], Generation = new GenerationReport() };
        for (int i = 0; i < w.Tiles.Length; i++)
        {
            var t = new Tile { Id = i, Terrain = Terrain.Grassland, Elevation = .55f, ElevationLevel = 2, Food = capacity, PotentialFood = capacity * 10, Freshwater = 60, FreshwaterSource = FreshwaterSource.RainPool };
            for (int s = 0; s < 4; s++) t.Seasons[s] = new SeasonData { MaxFood = capacity, MaxPotentialFood = capacity * 10, FoodRecoveryPerDay = (float)EcologyMath.DailyRate(recovery, 91.25), BiomassRecoveryPerDay = (float)EcologyMath.DailyRate(recovery, 91.25), Moisture = 1, MaxFreshwater = 80, WaterRecoveryPerDay = (float)EcologyMath.DailyRate(.3, 91.25) };
            w.Tiles[i] = t;
        }
        w.Camps.Add(new Camp { Id = w.NextId++, Tile = 150, Player = 0, Population = 24, Food = 4000 }); Ownership.Bind(w); return w;
    }
    private static void Sea(Tile t, bool open = false)
    {
        t.Terrain = open ? Terrain.Ocean : Terrain.Shallows; t.ElevationLevel = 0; t.WaterBody = WaterKind.Saltwater; t.FreshwaterSource = FreshwaterSource.None; t.Freshwater = 0; t.Saltwater = 100; t.Food = 200; t.PotentialFood = 200;
        for (int s = 0; s < 4; s++) t.Seasons[s] = new SeasonData { MaxFood = 200, MaxPotentialFood = 200, FoodRecoveryPerDay = (float)EcologyMath.DailyRate(.1, 91.25), BiomassRecoveryPerDay = (float)EcologyMath.DailyRate(.1, 91.25), MaxSaltwater = 100 };
    }
    public static void Run()
    {
        var resolver = new TurnResolver(); var w = Flat(); var shortRun = SaveCodec.Load(SaveCodec.Save(w)); shortRun.Config.TurnDays /= 2;
        var result = resolver.Resolve(w, Idle()); var shorter = resolver.Resolve(shortRun, Idle()); var report = result.Demographics.Single();
        double expected = 24 * Math.Exp(w.Config.AnnualHumanGrowthRate * 20);
        Check(Math.Abs(w.Camps[0].Population + w.Camps[0].GrowthRemainder - expected) < 1e-8, "abundant food yields annualized exponential growth over 20 years");
        Check(w.Camps[0].Population > 24 && w.Days == 7300 && report.Years == 20, "a generation advances historical time and adds whole people");
        Check(report.FoodRequired > 24 * 80 && report.FoodRequired > shorter.Demographics[0].FoodRequired * 2, "food demand grows with population within the generation");
        Check(result.Growth > shorter.Growth && shortRun.Days == 3650, "shorter technological-era duration scales demographics and clock");
        Check(report.StartingPopulation + report.Additions - report.Deaths == report.EndingPopulation && report.Explanation.Contains("Population") && report.LimitingFactor.Contains("surplus"), "demographic conservation and visible explanation data are explicit");
        var accelerated = Flat(); accelerated.Camps[0].DemographicGrowthMultiplier = 2; resolver.Resolve(accelerated, Idle());
        Check(Math.Abs(accelerated.Camps[0].Population + accelerated.Camps[0].GrowthRemainder - 24 * Math.Exp(accelerated.Config.AnnualHumanGrowthRate * 2 * 20)) < 1e-8, "a persisted demographic multiplier can extend the same food-limited model in later eras");
        var secondGeneration = resolver.Resolve(w, Idle()).Demographics.Single();
        Check(secondGeneration.StartingPopulation > report.StartingPopulation && secondGeneration.FoodRequired > report.FoodRequired && secondGeneration.TerrestrialFood > report.TerrestrialFood, "population growth raises demand and automatically expands local gathering in the next generation");
        w = Flat(); w.Config.GatherPerPerson = 100; w.Camps[0].Population = 16;
        var suppliedHunters = new Hunter { Id = w.NextId++, Player = 0, SourceCamp = w.Camps[0].Id, SocietyId = w.Camps[0].SocietyId, Tile = w.Camps[0].Tile, Population = 8 }; w.Hunters.Add(suppliedHunters);
        resolver.Resolve(w, Idle());
        Check(Math.Abs(w.LinkedPopulation(w.Camps[0]) + w.Camps[0].GrowthRemainder - 24 * Math.Exp(w.Config.AnnualHumanGrowthRate * 20)) < 1e-8 && suppliedHunters.Population == 8, "the entire supplied tribe forms the reproductive base while additions appear at camp");
        Check(w.Tiles.All(t => t.Food >= 0 && t.Food <= t.Seasons[0].MaxFood), "long turns replenish bounded stocks instead of draining one seasonal snapshot eighty times");
        var standardCapacity = new SimulationConfig { Players = 80 }; standardCapacity.Validate();
        bool rejectedSmall = false; try { SimulationConfig.Small(17).Validate(); } catch (ArgumentException) { rejectedSmall = true; }
        Check(rejectedSmall && SimulationConfig.MaximumPlayersFor(1024, 512) == 80, "tribe limits allow eighty only on worlds large enough to place them");
        w = Flat(); var provisionedHunter = new Hunter { Id = w.NextId++, Player = 0, SourceCamp = w.Camps[0].Id, SocietyId = w.Camps[0].SocietyId, Tile = w.Camps[0].Tile, Population = 8 }; w.Hunters.Add(provisionedHunter);
        var provisionRecord = new TurnRecord(); Supply.ProvisionDepartures(w, new List<Order> { new Order(OrderKind.Move, provisionedHunter.Id, destination: provisionedHunter.Tile + 2) }, provisionRecord);
        Check(Math.Abs(provisionedHunter.CarriedFood - 8 * w.Config.Consumption * w.Config.GenerationIntervals * w.Config.HunterCarryGenerations) < 1e-8, "a departing hunter group can use its configured generational carrying capacity");
        w = Flat(); foreach (var tile in w.Tiles) for (int season = 0; season < 4; season++) { var ecology = tile.Seasons[season]; ecology.Temperature = 30; ecology.Moisture = 1; tile.Seasons[season] = ecology; }
        double warmStorage = TurnResolver.StorageGenerations(w, w.Camps[0].Tile);
        foreach (var tile in w.Tiles) for (int season = 0; season < 4; season++) { var ecology = tile.Seasons[season]; ecology.Temperature = -18; ecology.Moisture = .05f; tile.Seasons[season] = ecology; }
        double coldStorage = TurnResolver.StorageGenerations(w, w.Camps[0].Tile);
        Check(Math.Abs(warmStorage - 1) < 1e-8 && coldStorage > 3.5 && coldStorage <= 4, "warm moist storage lasts one generation while cold dry storage approaches four");
        var ecologyConfig = new SimulationConfig(); var forestFood = new Tile { Terrain = Terrain.Woodland, Food = 20 }; var grassFood = new Tile { Terrain = Terrain.Grassland, Food = 20 }; var desertFood = new Tile { Terrain = Terrain.Desert, Food = 20 };
        foreach (var tile in new[] { forestFood, grassFood, desertFood }) for (int season = 0; season < 4; season++) tile.Seasons[season] = new SeasonData { MaxFood = 100, FoodRecoveryPerDay = (float)EcologyMath.DailyRate(.2, 91.25) };
        FoodEcology.ConfigurePotential(forestFood, ecologyConfig, true); FoodEcology.ConfigurePotential(grassFood, ecologyConfig, true); FoodEcology.ConfigurePotential(desertFood, ecologyConfig, true);
        Check(forestFood.Seasons[0].MaxPotentialFood == 400 && grassFood.Seasons[0].MaxPotentialFood == 1000 && desertFood.Seasons[0].MaxPotentialFood == 2000 && forestFood.Food == 20 && grassFood.Food == 20 && desertFood.Food == 20, "potential biomass preserves depleted gatherable food at rich 25%, grassland 10% and desert 5% accessibility");
        w = Flat(); var growingHerd = new Herd { Id = w.NextId++, Tile = 160, Animals = 149, Capacity = 150, Species = "antelope" }; w.Herds.Add(growingHerd);
        int herdPopulation = w.Herds.Sum(h => h.Animals); var herdTurn = resolver.Resolve(w, Idle());
        Check(w.Herds.Count == 1 && growingHerd.Animals == 150 && !herdTurn.Events.Any(e => e.Contains("divided")), "well-fed wild herds stop at their carrying ceiling without filling the map with daughter entities");
        w = Flat(); growingHerd = new Herd { Id = w.NextId++, Tile = 160, Animals = 9, Capacity = 100, Species = "camel" }; w.Herds.Add(growingHerd);
        for (int generation = 0; generation < 6; generation++) resolver.Resolve(w, Idle());
        Check(growingHerd.Animals == 100 && w.Herds.Count == 1, "a nine-animal well-fed herd approaches its one-hundred-head ceiling in six generations");
        w = Flat(); w.Config.WildHerdFissionEnabled = true; growingHerd = new Herd { Id = w.NextId++, Tile = 160, Animals = 149, Capacity = 150, Species = "antelope" }; w.Herds.Add(growingHerd);
        var managedHerd = new Herd { Id = w.NextId++, Tile = w.Camps[0].Tile, Animals = 100, Capacity = 100, Species = "camel", DomesticCamp = w.Camps[0].Id, OwnerSociety = w.Camps[0].SocietyId }; w.Herds.Add(managedHerd);
        herdPopulation = w.Herds.Sum(h => h.Animals); herdTurn = resolver.Resolve(w, Idle());
        Check(w.Herds.Count > 2 && w.Herds.All(h => h.Animals <= h.Capacity) && managedHerd.Animals == managedHerd.Capacity && w.Herds.Sum(h => h.Animals) > herdPopulation && herdTurn.Events.Any(e => e.Contains("divided")), "optional wild-herd fission remains available as an explicit later ruleset while managed herds stay bounded");
        w = Flat(); w.Config.GatherPerPerson = .1; var huntingCamp = w.Camps[0]; huntingCamp.Population -= 8; var hunters = new Hunter { Id = w.NextId++, Player = 0, SourceCamp = huntingCamp.Id, SocietyId = huntingCamp.SocietyId, Tile = huntingCamp.Tile, Population = 8 }; w.Hunters.Add(hunters);
        var camels = new Herd { Id = w.NextId++, Tile = w.Map.Neighbors(huntingCamp.Tile).First(), Animals = 14, Capacity = 100, Species = "camel" }; w.Herds.Add(camels);
        int interception = TurnResolver.HerdDestination(w, camels); var sustainableHunt = resolver.Resolve(w, new Dictionary<int, List<Order>> { [0] = new List<Order> { new Order(OrderKind.Move, hunters.Id, destination: interception) } });
        Check(sustainableHunt.Transfers.Where(t => t.Kind == "Hunting").Sum(t => t.Food) == 480 * w.Config.HuntingFoodYieldScale && w.Config.GetSpecies("camel").FoodYield * 16 == 24 * w.Config.Consumption * w.Config.GenerationIntervals && camels.Animals == 16 && sustainableHunt.Events.Any(e => e.Contains("keep growing")), "one eight-person group takes only sustainable growth; usable recovery scales without changing the baseline carcass balance or animal losses");
        Check(huntingCamp.Experience.Sum() > 24 && huntingCamp.Experience[(int)Domain.Hunting] > huntingCamp.Experience[(int)Domain.Gathering], "food-weighted hunting practice receives its configured learning intensity without reducing other domain progress");
        double huntFood = sustainableHunt.Transfers.Where(t => t.Kind == "Hunting").Sum(t => t.Food);
        double gatherFood = sustainableHunt.Transfers.Where(t => t.Kind == "Gathering" || t.Kind == "Foraging").Sum(t => t.Food);
        double lifestyleBudget = sustainableHunt.Demographics.Single().StartingPopulation * sustainableHunt.Duration / w.Config.PracticeDays;
        Check(huntFood > 0 && Math.Abs(huntingCamp.Experience[(int)Domain.Hunting] - 2 * lifestyleBudget * huntFood / (huntFood + gatherFood) * w.Config.HuntingPracticeMultiplier) < 1e-8 && Math.Abs(huntingCamp.Experience[(int)Domain.Gathering] - lifestyleBudget * gatherFood / (huntFood + gatherFood)) < 1e-8, "successful hunt experience doubles without taking practice from gathering or changing the verified carcass yield");
        var failedHuntWorld = Flat(); failedHuntWorld.Config.HuntEfficiency = 0; failedHuntWorld.Camps[0].Population -= 8;
        var unsuccessfulHunters = new Hunter { Id = failedHuntWorld.NextId++, Player = 0, SourceCamp = failedHuntWorld.Camps[0].Id, SocietyId = failedHuntWorld.Camps[0].SocietyId, Tile = failedHuntWorld.Camps[0].Tile, Population = 8 }; failedHuntWorld.Hunters.Add(unsuccessfulHunters);
        failedHuntWorld.Herds.Add(new Herd { Id = failedHuntWorld.NextId++, Tile = unsuccessfulHunters.Tile, Animals = 20, Capacity = 100, Species = "camel" });
        foreach (int neighbor in failedHuntWorld.Map.Neighbors(unsuccessfulHunters.Tile)) failedHuntWorld.Tiles[neighbor].ElevationLevel = 10;
        var failedHunt = resolver.Resolve(failedHuntWorld, Idle());
        double failedPractice = unsuccessfulHunters.Population * failedHuntWorld.Config.Consumption * failedHuntWorld.Config.GenerationIntervals * failedHuntWorld.Config.UnproductivePracticeFraction;
        double failedGathering = failedHunt.Transfers.Where(t => t.Kind == "Gathering" || t.Kind == "Foraging").Sum(t => t.Food);
        Check(failedHunt.Transfers.Any(t => t.Kind == "Hunting" && t.Food == 0) && Math.Abs(failedHuntWorld.Camps[0].Experience[(int)Domain.Hunting] - 24 * failedPractice / (failedPractice + failedGathering) * failedHuntWorld.Config.HuntingPracticeMultiplier) < 1e-8, "failed hunts retain their previous reduced practice reward");
        var practiceCamp = new Camp(); var practiceConfig = new SimulationConfig();
        practiceCamp.Experience[(int)Domain.Hunting] = practiceConfig.ExperienceThreshold * practiceConfig.PracticeBonusStart;
        Check(PracticeEffects.Ramp(practiceConfig, practiceCamp, Domain.Hunting) == 0, "practice bonuses do not begin before the configured quarter-proficiency threshold");
        practiceCamp.Experience[(int)Domain.Hunting] = practiceConfig.ExperienceThreshold * practiceConfig.PracticeBonusFull;
        Check(Math.Abs(PracticeEffects.HuntingYield(practiceConfig, practiceCamp) - practiceConfig.HuntingFoodYieldScale * 1.5) < 1e-8, "half proficiency grants the full fifty-percent hunting fieldcraft yield bonus");
        practiceCamp.Experience[(int)Domain.Gathering] = practiceConfig.ExperienceThreshold * practiceConfig.PracticeBonusFull;
        practiceCamp.Experience[(int)Domain.Fishing] = practiceConfig.ExperienceThreshold * practiceConfig.PracticeBonusFull;
        Check(PracticeEffects.GatheringImpact(practiceConfig, practiceCamp) == .5 && PracticeEffects.FishingImpact(practiceConfig, practiceCamp) == .5, "half proficiency halves gathering and fishing environmental impact");
        var densityWorld = Flat(); Check(WildHerdPlacement.Density(densityWorld, new Tile { Terrain = Terrain.Grassland }) > WildHerdPlacement.Density(densityWorld, new Tile { Terrain = Terrain.Desert }) && WildHerdPlacement.Density(densityWorld, new Tile { Terrain = Terrain.Tundra }) > 1, "grassland and cold-steppe expanses increase regional wild-herd density");
        var noviceEcology = Flat(); noviceEcology.Config.SafeUseRatio = 0; var skilledEcology = SaveCodec.Load(SaveCodec.Save(noviceEcology));
        skilledEcology.Camps[0].Experience[(int)Domain.Gathering] = skilledEcology.Config.ExperienceThreshold * skilledEcology.Config.PracticeBonusFull;
        var noviceHarvest = resolver.Resolve(noviceEcology, Idle()); var skilledHarvest = resolver.Resolve(skilledEcology, Idle());
        int worked = noviceEcology.Camps[0].Tile;
        Check(Math.Abs(noviceHarvest.Transfers.Where(t => t.Kind == "Gathering").Sum(t => t.Food) - skilledHarvest.Transfers.Where(t => t.Kind == "Gathering").Sum(t => t.Food)) < 1e-8 && skilledEcology.Tiles[worked].LandCondition > noviceEcology.Tiles[worked].LandCondition, "gathering proficiency preserves more land condition for the same food");
        var passiveFishing = Flat(); passiveFishing.Config.SafeUseRatio = 0; foreach (int tile in passiveFishing.Map.Radius(passiveFishing.Camps[0].Tile, 1)) { passiveFishing.Tiles[tile].Food = 0; passiveFishing.Tiles[tile].PotentialFood = 0; for (int season = 0; season < 4; season++) { var ecology = passiveFishing.Tiles[tile].Seasons[season]; ecology.FoodRecoveryPerDay = 0; ecology.BiomassRecoveryPerDay = 0; passiveFishing.Tiles[tile].Seasons[season] = ecology; } }
        int passiveCoast = passiveFishing.Map.Neighbors(passiveFishing.Camps[0].Tile).First(); Sea(passiveFishing.Tiles[passiveCoast]);
        var skilledFishingImpact = SaveCodec.Load(SaveCodec.Save(passiveFishing)); skilledFishingImpact.Camps[0].Experience[(int)Domain.Fishing] = skilledFishingImpact.Config.ExperienceThreshold * skilledFishingImpact.Config.PracticeBonusFull;
        var oldFishingShare = SaveCodec.Load(SaveCodec.Save(passiveFishing)); oldFishingShare.Config.PassiveAquaticFishingPracticeShare = .25;
        var noviceFishingHarvest = resolver.Resolve(passiveFishing, Idle()); var skilledFishingHarvest = resolver.Resolve(skilledFishingImpact, Idle());
        var oldFishingHarvest = resolver.Resolve(oldFishingShare, Idle());
        Check(passiveFishing.Camps[0].Experience[(int)Domain.Gathering] > 0 && Math.Abs(passiveFishing.Camps[0].Experience[(int)Domain.Gathering] / passiveFishing.Camps[0].Experience[(int)Domain.Fishing] - 1) < 1e-8, "passive aquatic harvest credits equal Gathering and Fishing practice");
        Check(Math.Abs(passiveFishing.Camps[0].Experience[(int)Domain.Fishing] - 2 * oldFishingShare.Camps[0].Experience[(int)Domain.Fishing]) < 1e-8 && passiveFishing.Camps[0].Experience.Sum() == oldFishingShare.Camps[0].Experience.Sum() && noviceFishingHarvest.Demographics.Single().FoodProduced == oldFishingHarvest.Demographics.Single().FoodProduced && passiveFishing.Camps[0].Food == oldFishingShare.Camps[0].Food && passiveFishing.Tiles[passiveCoast].AquaticCondition == oldFishingShare.Tiles[passiveCoast].AquaticCondition, "50/50 passive practice doubles fishing credit while preserving total practice, food and current-turn aquatic condition");
        Check(skilledFishingHarvest.Transfers.Where(t => t.Kind == "Marine gathering").Sum(t => t.Food) >= noviceFishingHarvest.Transfers.Where(t => t.Kind == "Marine gathering").Sum(t => t.Food) && skilledFishingImpact.Tiles[passiveCoast].AquaticCondition > passiveFishing.Tiles[passiveCoast].AquaticCondition, "fishing proficiency preserves more aquatic condition and at least as much food under damaging pressure");
        var ledgerWorld = Flat(); ledgerWorld.Camps[0].Workforce.Assignments.Add(new PopulationAssignment { Role = "future.craft", Population = 5 }); ledgerWorld.Tiles[0].LandCondition = .42; ledgerWorld.Tiles[1].AquaticCondition = .37;
        Check(ledgerWorld.Camps[0].GathererPopulation == 19 && resolver.Validate(ledgerWorld, 0, new Order(OrderKind.Dispatch, ledgerWorld.Camps[0].Id, population: 20)) != null, "the workforce ledger leaves unassigned Paleolithic residents as gatherers and prevents dispatching assigned people");
        var ledgerCopy = SaveCodec.Load(SaveCodec.Save(ledgerWorld));
        Check(ledgerCopy.Camps[0].GathererPopulation == 19 && ledgerCopy.Camps[0].Workforce.Assignments.Single().Role == "future.craft" && ledgerCopy.Tiles[0].LandCondition == .42 && ledgerCopy.Tiles[1].AquaticCondition == .37, "workforce assignments and persistent environmental condition survive save/load");
        foreach (int neighbor in w.Map.Neighbors(camels.Tile)) w.Tiles[neighbor].ElevationLevel = 10;
        var secondHunters = new Hunter { Id = w.NextId++, Player = 0, SourceCamp = huntingCamp.Id, SocietyId = huntingCamp.SocietyId, Tile = camels.Tile, Population = 8 };
        w.Hunters.Add(secondHunters); int beforeSecondHunt = camels.Animals; resolver.Resolve(w, Idle());
        Check(camels.Animals > beforeSecondHunt, "two eight-person groups over one herd still leave net herd growth");
        var thirdHunters = new Hunter { Id = w.NextId++, Player = 0, SourceCamp = huntingCamp.Id, SocietyId = huntingCamp.SocietyId, Tile = camels.Tile, Population = 8 };
        w.Hunters.Add(thirdHunters); huntingCamp.Food = 0; int beforeHeavyHunt = camels.Animals; resolver.Resolve(w, Idle());
        Check(camels.Animals < beforeHeavyHunt, "more than two eight-person groups can exert net overhunting pressure on one herd");
        w = Flat(); w.Config.GatherPerPerson = .1; huntingCamp = w.Camps[0]; huntingCamp.Population -= 8; hunters = new Hunter { Id = w.NextId++, Player = 0, SourceCamp = huntingCamp.Id, SocietyId = huntingCamp.SocietyId, Tile = huntingCamp.Tile, Population = 8 }; w.Hunters.Add(hunters);
        camels = new Herd { Id = w.NextId++, Tile = huntingCamp.Tile, Animals = 100, Capacity = 100, Species = "camel" }; w.Herds.Add(camels);
        foreach (int neighbor in w.Map.Neighbors(camels.Tile)) w.Tiles[neighbor].ElevationLevel = 10;
        for (int generation = 0; generation < 10; generation++) resolver.Resolve(w, Idle());
        Check(huntingCamp.Technology == Technology.AnimalHusbandry && huntingCamp.Experience[(int)Domain.Hunting] > huntingCamp.Experience[(int)Domain.Gathering], "a sustained food-dominant hunting strategy remains a viable Paleolithic path to animal husbandry");
        w = Flat(); huntingCamp = w.Camps[0]; huntingCamp.Population -= 8; hunters = new Hunter { Id = w.NextId++, Player = 0, SourceCamp = huntingCamp.Id, SocietyId = huntingCamp.SocietyId, Tile = huntingCamp.Tile, Population = 8 }; w.Hunters.Add(hunters);
        foreach (int neighbor in w.Map.Neighbors(hunters.Tile)) w.Tiles[neighbor].ElevationLevel = 10;
        camels = new Herd { Id = w.NextId++, Tile = hunters.Tile, Animals = 10, Capacity = 80, Species = "camel" }; w.Herds.Add(camels);
        var automaticHunt = resolver.Resolve(w, Idle());
        Check(automaticHunt.Transfers.Any(t => t.Entity == hunters.Id && t.Kind == "Hunting" && t.Food > 0), "an established hunter group automatically hunts wild prey sharing its resolved tile");
        w = Flat(); var localHerd = new Herd { Id = w.NextId++, Tile = w.Camps[0].Tile, Animals = 10, Capacity = 80, Species = "camel" }; w.Herds.Add(localHerd);
        foreach (int neighbor in w.Map.Neighbors(localHerd.Tile)) w.Tiles[neighbor].ElevationLevel = 10;
        int animalsBeforeDispatch = localHerd.Animals; var dispatchTurn = resolver.Resolve(w, new Dictionary<int, List<Order>> { [0] = new List<Order> { new Order(OrderKind.Dispatch, w.Camps[0].Id, population: 8) } });
        Check(localHerd.Animals >= animalsBeforeDispatch && !dispatchTurn.Transfers.Any(t => t.Kind == "Hunting"), "a newly dispatched group cannot auto-hunt before its first planning phase");
        w = Flat(); w.Camps[0].Population = 40;
        var existingHunters = new Hunter { Id = w.NextId++, Player = 0, SourceCamp = w.Camps[0].Id, SocietyId = w.Camps[0].SocietyId, Tile = w.Camps[0].Tile, Population = 8 }; w.Hunters.Add(existingHunters);
        w.Herds.Add(new Herd { Id = w.NextId++, Tile = w.Map.Neighbors(w.Camps[0].Tile).First(), Animals = 40, Capacity = 150, Species = "antelope" });
        var growthResponse = new HeuristicTribePolicy().Decide(new AgentObservationBuilder().Build(w, 0), w.Camps[0].Mandate);
        Check(growthResponse.Orders.Any(o => o.Kind == OrderKind.Dispatch && o.Population >= 4) && growthResponse.Orders.All(o => o.Kind != OrderKind.Hunt), "a larger tribe with visible prey can dispatch another group while pursuit uses movement rather than an explicit hunt order");
        w = Flat(30, .3f); w.Camps[0].Food = 48;
        for (int i = 0; i < 10; i++) resolver.Resolve(w, Idle());
        double fullySuppliedPopulation = 24 * Math.Exp(w.Config.AnnualHumanGrowthRate * 200);
        Check(w.Camps[0].Population > 24 && w.Camps[0].Population < fullySuppliedPopulation * .8, "limited recurring food constrains otherwise exponential growth without an arbitrary population cap");
        w = Flat(0, 0); w.Camps[0].Food = 0; resolver.Resolve(w, Idle()); resolver.Resolve(w, Idle()); var declining = resolver.Resolve(w, Idle()).Demographics[0];
        Check(declining.Deaths > 0 && declining.EndingPopulation < 24 && declining.Additions == 0 && declining.LimitingFactor.Contains("deficit"), "food deficits cause recorded decline with a visible limiting factor");
        w = Flat(0, 0); w.Camps[0].Food = 10000; var noSurplus = resolver.Resolve(w, Idle()).Demographics[0];
        Check(noSurplus.Additions == 0 && noSurplus.EndingPopulation == 24 && noSurplus.LimitingFactor.Contains("surplus"), "stored reserves alone do not claim sustainable growth");
        w = Flat(); w.Config.TurnDays = 1; var rounding = resolver.Resolve(w, Idle()).Demographics[0];
        Check(rounding.Additions == 0 && rounding.FractionalProgress > 0 && rounding.LimitingFactor.Contains("rounding"), "sub-person growth is retained and explained");
        w = Flat(0, 0); w.Config.TurnDays = w.Config.BaseDays; w.Config.AnnualHumanGrowthRate = 0; var camp = w.Camps[0]; camp.Food = 0;
        int coast = w.Map.Neighbors(camp.Tile).First(); Sea(w.Tiles[coast]); double stock = w.Tiles[coast].Food;
        var coastal = resolver.Resolve(w, Idle()); var marine = coastal.Transfers.Where(t => t.Kind == "Marine gathering").ToArray();
        Check(marine.Sum(t => t.Food) > 0 && camp.Food > 0 && w.Tiles[coast].Food < stock, "a poor-land coastal camp automatically gathers positive marine food and source stock changes");
        Check(marine.All(t => t.Freshwater == 0) && w.Tiles[coast].DrinkableWater == 0 && coastal.Demographics[0].MarineFood > 0, "marine gathering is recorded separately and grants no freshwater");
        w = Flat(200, .1f); w.Config.TurnDays = w.Config.BaseDays; w.Config.AnnualHumanGrowthRate = 0; Sea(w.Tiles[coast]);
        var mixed = resolver.Resolve(w, Idle());
        Check(mixed.Transfers.Where(t => t.Kind.Contains("gathering") || t.Kind == "Gathering").Sum(t => t.Food) <= 24 * w.Config.GatherPerPerson + 1e-8, "marine and terrestrial gathering share one labor budget");
        w = Flat(0, 0); w.Config.TurnDays = w.Config.BaseDays; Sea(w.Tiles[coast], true); Sea(w.Tiles[152]);
        Check(!resolver.Resolve(w, Idle()).Transfers.Any(t => t.Kind == "Marine gathering" && t.Food > 0), "open ocean and nonadjacent shallows are excluded from passive gathering");
        w = Flat(); var h = new Hunter { Id = w.NextId++, Player = 0, SourceCamp = w.Camps[0].Id, SocietyId = w.Camps[0].SocietyId, Tile = 150, Population = 8 }; w.Hunters.Add(h);
        w.Tiles[151].ElevationLevel = 10;
        Check(!w.CanEnter(151, false) && !w.CanEnter(151, true, w.Camps[0]) && resolver.Validate(w, 0, new Order(OrderKind.Move, h.Id, destination: 151)) != null, "peaks reject camp and hunter movement");
        var animal = new Herd { Id = w.NextId++, Tile = 150, Species = "goat", Animals = 6 }; w.Herds.Add(animal);
        Check(TurnResolver.HerdDestination(w, animal) != 151 && w.Config.GetSpecies("goat").Suitability(w.Tiles[151]) == 0, "peaks are unavailable to herds including goats");
        var view = new AgentObservationBuilder().Build(w, 0);
        Check(view.Tiles[151].ElevationLevel == 10 && new HeuristicTribePolicy().Decide(view, w.Camps[0].Mandate).Orders.All(o => o.Destination != 151), "AI observations and movement preserve peak blocking");
        w.Hunters.Add(new Hunter { Id = w.NextId++, Player = 0, SourceCamp = w.Camps[0].Id, SocietyId = w.Camps[0].SocietyId, Tile = 150, Population = 3, CarriedFood = 5 });
        w.Herds.Add(new Herd { Id = w.NextId++, Tile = 150, Species = "horse", Animals = 4, DomesticCamp = w.Camps[0].Id, OwnerSociety = w.Camps[0].SocietyId });
        view = new AgentObservationBuilder().Build(w, 0); var occupants = TileDetails.Occupants(view, 150);
        Check(occupants.Length == 5 && occupants.Count(p => p.SelectableId >= 0) == 3 && occupants.Select(p => p.Id).Distinct().Count() == 5, "stack data contains camp, both friendly groups, wild herd and domestic livestock");
        string detail = TileDetails.Describe(view, 150, new[] { new Order(OrderKind.Move, h.Id, destination: 152) });
        Check(detail.Contains("OCCUPANTS") && detail.Contains("QUEUED") && detail.Contains("soil moisture") && detail.Contains("maximum") && detail.Contains("Domestic"), "hover details expose all requested visible groups and queued orders");
        int hidden = Enumerable.Range(0, w.Tiles.Length).First(n => !view.Tiles.ContainsKey(n));
        w.Tiles[hidden].Food = 987654; w.Herds.Add(new Herd { Id = w.NextId++, Tile = hidden, Species = "camel", Animals = 500 });
        var changed = new AgentObservationBuilder().Build(w, 0);
        Check(TileDetails.Describe(changed, hidden, new Order[0]) == "" && TileDetails.Describe(changed, 150, new[] { new Order(OrderKind.Move, h.Id, destination: 152) }) == detail, "hover data cannot reveal hidden tile or herd changes");
        var hover = new HoverTiming(); hover.Observe(1, 100, 100, 0, "view", 2, .2, .15, 5); hover.Observe(1, 100, 100, 2, "view", 2, .2, .15, 5);
        Check(hover.Alpha == 0, "hover does not appear until more than two seconds");
        hover.Observe(1, 100, 100, 2.1, "view", 2, .2, .15, 5); Check(hover.Alpha > 0 && hover.Alpha < 1 && hover.Displayed == 1, "hover fades in after its delay");
        hover.Observe(2, 100, 100, 2.15, "view", 2, .2, .15, 5); Check(hover.Alpha > 0 && hover.Alpha < .5, "tile change begins prompt smooth fade-out");
        hover.Observe(2, 100, 100, 4.2, "pan", 2, .2, .15, 5); Check(hover.Alpha == 0, "pan or zoom context resets the delay");
        hover.Observe(2, 106, 100, 6.3, "pan", 2, .2, .15, 5); Check(hover.Alpha == 0, "meaningful movement resets hover even within the tile");
        hover.Observe(-1, 106, 100, 9, "pan", 2, .2, .15, 5); Check(hover.Displayed == -1, "leaving the map clears the overlay");
        var countConfig = new SimulationConfig();
        int[] counts = { 1, 9, 10, 14, 15, 25, 99, 100, 101, 199, 200, 249 };
        string[] labels = { "fewer than 10", "fewer than 10", "~10", "~10", "~20", "~30", "~100", "~100", "100+", "100+", "200+", "200+" };
        for (int i = 0; i < counts.Length; i++) Check(HerdCountKnowledge.Observe(counts[i], countConfig).Label == labels[i], "centralized herd count boundary " + counts[i]);
        w = Flat(); var wild = new Herd { Id = w.NextId++, Tile = 150, Animals = 41, Capacity = 200, Species = "camel" }; w.Herds.Add(wild);
        var livestock = new Herd { Id = w.NextId++, Tile = 150, Animals = 17, Capacity = 200, Species = "goat", DomesticCamp = w.Camps[0].Id, OwnerSociety = w.Camps[0].SocietyId }; w.Herds.Add(livestock);
        var observer = new AgentObservationBuilder(); var firstView = observer.Build(w, 0);
        Check(firstView.Herds.First(h => h.Id == livestock.Id).Count.Exact && firstView.Herds.First(h => h.Id == livestock.Id).Count.Label == "17", "own domestic livestock exposes a managed exact count");
        var outsider = new HerdObservation(livestock, countConfig, 0, 1);
        Check(!outsider.Count.Exact && outsider.Count.Label == "~20", "foreign domestic livestock remains approximate");
        string firstDescription = TileDetails.Describe(firstView, 150, new Order[0]);
        wild.Animals = 44; var equivalent = observer.Build(w, 0);
        Check(firstDescription == TileDetails.Describe(equivalent, 150, new Order[0]) && firstView.Herds[0].Size == equivalent.Herds[0].Size && equivalent.Herds[0].Count.Representative == 40, "hover, inspector, badge and AI receive the same estimate within a count bucket");
        var savedCounts = SaveCodec.Load(SaveCodec.Save(w));
        Check(TileDetails.Describe(observer.Build(savedCounts, 0), 150, new Order[0]) == TileDetails.Describe(equivalent, 150, new Order[0]), "count estimates are deterministic across save/load without exposing raw wild counts");
        w.Config.TurnDays = w.Config.BaseDays; savedCounts.Config.TurnDays = w.Config.BaseDays; resolver.Resolve(w, Idle()); resolver.Resolve(savedCounts, Idle());
        Check(SaveCodec.Fingerprint(w) == SaveCodec.Fingerprint(savedCounts), "approximate knowledge does not alter authoritative herd replay");
        Console.WriteLine("PASS: " + checks + " milestone-three assertions");
    }
}
