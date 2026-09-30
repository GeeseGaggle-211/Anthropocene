using System;
using System.Collections.Generic;
using System.Linq;
using Anthropocene.Simulation;

internal static class Issue32CapabilityChecks
{
    private const string MissingTechnology = "Economy requires Agriculture or Animal Husbandry.";
    private const string IncompletePlan = "Economic plan is incomplete; assignments and priorities are required.";
    private static int checks;

    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception("Issue 32: " + message);
    }

    private static void EconomyGateDiagnostics()
    {
        var watercraftWorld = NeolithicChecks.Fixture(false);
        var watercraftCamp = watercraftWorld.Camps.Single();
        watercraftCamp.Technologies[(int)Technology.Watercraft] = true;
        var observation = new AgentObservationBuilder().Build(watercraftWorld, watercraftCamp.Player);
        var settlement = observation.Settlements.Single();
        Check(observation.Watercraft && !settlement.Agriculture && !settlement.Husbandry,
            "Watercraft capability remains distinct from the two shared-Economy prerequisites");

        string reason = OrderRules.Validate(observation, new Order(OrderKind.Economy, watercraftCamp.Id)
            { Economy = new EconomicIntent() });
        Check(reason == MissingTechnology,
            "a complete Economy order reports its missing technology through ordinary action validation");

        int shallow = watercraftWorld.Map.Neighbors(watercraftCamp.Tile).First();
        watercraftWorld.Tiles[shallow].Terrain = Terrain.Shallows;
        watercraftWorld.Tiles[shallow].WaterBody = WaterKind.Saltwater;
        watercraftWorld.Tiles[shallow].ElevationLevel = 0;
        var hunter = new Hunter
        {
            Id = watercraftWorld.NextId++, SourceCamp = watercraftCamp.Id,
            SocietyId = watercraftCamp.SocietyId, Player = watercraftCamp.Player,
            Tile = watercraftCamp.Tile, Population = 4
        };
        watercraftWorld.Hunters.Add(hunter);
        observation = new AgentObservationBuilder().Build(watercraftWorld, watercraftCamp.Player);
        Check(OrderRules.Validate(observation, new Order(OrderKind.Fish, hunter.Id, shallow)) == null,
            "the existing Fish action remains legal at a visible adjacent Shallows source");
        int ocean = watercraftWorld.Map.Neighbors(watercraftCamp.Tile).Skip(1).First();
        watercraftWorld.Tiles[ocean].Terrain = Terrain.Ocean;
        watercraftWorld.Tiles[ocean].WaterBody = WaterKind.Saltwater;
        watercraftWorld.Tiles[ocean].ElevationLevel = 0;
        observation = new AgentObservationBuilder().Build(watercraftWorld, watercraftCamp.Player);
        Check(OrderRules.Validate(observation, new Order(OrderKind.Fish, hunter.Id, ocean)) != null,
            "current Fishing cannot expand into an Ocean source");

        var agriculture = new EconomicConstraints { Agriculture = true, Population = watercraftCamp.Population };
        Check(EconomicIntents.Validate(agriculture, null) == IncompletePlan,
            "an Agriculture-capable camp reports a missing plan separately from missing technology");
        Check(EconomicIntents.Validate(agriculture,
            new EconomicIntent { Assignments = null }) == IncompletePlan,
            "a missing assignment list is an incomplete plan");
        Check(EconomicIntents.Validate(agriculture,
            new EconomicIntent { Priorities = null }) == IncompletePlan,
            "missing priorities are an incomplete plan");

        var noSharedEconomy = new EconomicConstraints { Population = watercraftCamp.Population };
        Check(EconomicIntents.Validate(noSharedEconomy, null) == MissingTechnology,
            "missing technology takes precedence when both capability and plan are absent");
        Check(EconomicIntents.Validate(null, new EconomicIntent()) ==
            "Economic capability information is unavailable.",
            "unavailable filtered capability data has its own validation reason");

        Check(EconomicIntents.Validate(agriculture, new EconomicIntent()) == null,
            "an Agriculture plan remains accepted after separating diagnostics");
        Check(EconomicIntents.Validate(new EconomicConstraints
        {
            Husbandry = true, Population = watercraftCamp.Population
        }, new EconomicIntent()) == null,
            "a Husbandry plan remains accepted after separating diagnostics");
    }

    private sealed class CapabilityCase
    {
        public string Name;
        public bool Agriculture, Husbandry, Watercraft;
        public Technology Primary;
    }

    private static void CapabilityMatrix()
    {
        var cases = new[]
        {
            new CapabilityCase { Name = "Paleo-only" },
            new CapabilityCase { Name = "Agriculture-only", Agriculture = true, Primary = Technology.Cultivation },
            new CapabilityCase { Name = "Husbandry-only", Husbandry = true, Primary = Technology.AnimalHusbandry },
            new CapabilityCase { Name = "Watercraft-only", Watercraft = true, Primary = Technology.Watercraft },
            new CapabilityCase { Name = "mixed Watercraft, Agriculture, and Husbandry", Agriculture = true, Husbandry = true, Watercraft = true, Primary = Technology.Watercraft }
        };

        foreach (var capability in cases)
        {
            var world = NeolithicChecks.Fixture(false);
            var camp = world.Camps.Single();
            camp.Technology = capability.Primary;
            camp.People.Progression.Era = capability.Agriculture || capability.Husbandry || capability.Watercraft
                ? Era.Neolithic : Era.Paleolithic;
            camp.Technologies[(int)Technology.Cultivation] = capability.Agriculture;
            camp.Technologies[(int)Technology.AnimalHusbandry] = capability.Husbandry;
            camp.Technologies[(int)Technology.Watercraft] = capability.Watercraft;
            var observation = new AgentObservationBuilder().Build(world, camp.Player);
            var settlement = observation.Settlements.Single();
            Check(settlement.Agriculture == capability.Agriculture &&
                settlement.Husbandry == capability.Husbandry &&
                observation.Watercraft == capability.Watercraft,
                capability.Name + " observation exposes each capability independently");

            string economyResult = OrderRules.Validate(observation,
                new Order(OrderKind.Economy, camp.Id) { Economy = new EconomicIntent() });
            bool sharedEconomy = capability.Agriculture || capability.Husbandry;
            Check(sharedEconomy ? economyResult == null : economyResult == MissingTechnology,
                capability.Name + " Economy eligibility follows Agriculture or Husbandry alone");

            string farmerResult = OrderRules.Validate(observation,
                new Order(OrderKind.Economy, camp.Id)
                {
                    Economy = new EconomicIntent
                    {
                        Assignments = new List<PopulationAssignment>
                        { new PopulationAssignment { Role = WorkforceRoles.Farmer, Population = 1 } }
                    }
                });
            string herderResult = OrderRules.Validate(observation,
                new Order(OrderKind.Economy, camp.Id)
                {
                    Economy = new EconomicIntent
                    {
                        Assignments = new List<PopulationAssignment>
                        { new PopulationAssignment { Role = WorkforceRoles.Herder, Population = 1 } }
                    }
                });
            if (!sharedEconomy)
            {
                Check(farmerResult == MissingTechnology && herderResult == MissingTechnology,
                    capability.Name + " cannot submit Farmer or Herder assignments before the shared Economy gate");
            }
            else
            {
                Check((capability.Agriculture ? farmerResult == null : farmerResult == "Farmer assignments require Cultivation.") &&
                    (capability.Husbandry ? herderResult == null : herderResult == "Herder assignments require Animal Husbandry."),
                    capability.Name + " validates Farmer and Herder assignments against their own capability");
            }
        }
    }

    private static void AquaticFoodIsNotFabricFeedstock()
    {
        var clothWorld = NeolithicChecks.Fixture(true);
        var clothCamp = clothWorld.Camps.Single();
        clothCamp.PlantFood = 0;
        clothCamp.AnimalFood = 100;
        clothCamp.AquaticFood = 100;
        clothCamp.Inventory.HandTools = 100;
        clothCamp.People.NeolithicTechnologies.Add(NeolithicTech.Weaving);
        var clothRecord = new TurnRecord();
        double clothEquipment = 0;
        double cloth = NeolithicProduction.Produce(clothWorld, clothCamp,
            NeolithicProduction.Recipes(clothWorld.Config).Single(recipe => recipe.Good == CraftGood.Cloth),
            WorkforceRoles.Weaver, 80, 80, 10, ref clothEquipment, clothRecord);
        Check(cloth == 0 && clothRecord.Production.All(receipt => receipt.Good != CraftGood.Cloth),
            "AquaticFood cannot replace PlantFood in the Cloth recipe");

        var fabricWorld = NeolithicChecks.Fixture(false);
        var fabricCamp = fabricWorld.Camps.Single();
        fabricCamp.Technologies[(int)Technology.AnimalHusbandry] = true;
        fabricCamp.Technology = Technology.AnimalHusbandry;
        fabricCamp.People.Progression.Era = Era.Neolithic;
        fabricCamp.AquaticFood = 100;
        fabricCamp.AnimalFood = 0;
        fabricCamp.Inventory.HandTools = 100;
        fabricCamp.People.NeolithicTechnologies.Add(NeolithicTech.Weaving);
        var fabricRecord = new TurnRecord();
        double fabricEquipment = 0;
        double animalFabric = NeolithicProduction.Produce(fabricWorld, fabricCamp,
            NeolithicProduction.Recipes(fabricWorld.Config).Single(recipe => recipe.Good == CraftGood.AnimalFabric),
            WorkforceRoles.Weaver, 80, 80, 10, ref fabricEquipment, fabricRecord);
        Check(animalFabric == 0 && fabricRecord.Production.All(receipt => receipt.Good != CraftGood.AnimalFabric),
            "AquaticFood cannot replace AnimalFood in the AnimalFabric recipe");
    }

    private static void SaltwaterShallows(Tile tile)
    {
        tile.Terrain = Terrain.Shallows;
        tile.ElevationLevel = 0;
        tile.WaterBody = WaterKind.Saltwater;
        tile.FreshwaterSource = FreshwaterSource.None;
        tile.Freshwater = 0;
        tile.Saltwater = 100;
        tile.Food = 200;
        tile.PotentialFood = 200;
        for (int season = 0; season < 4; season++)
            tile.Seasons[season] = new SeasonData
            {
                MaxFood = 200, MaxPotentialFood = 200,
                FoodRecoveryPerDay = (float)EcologyMath.DailyRate(.1, 91.25),
                BiomassRecoveryPerDay = (float)EcologyMath.DailyRate(.1, 91.25),
                MaxSaltwater = 100
            };
    }

    private static Dictionary<int, List<Order>> Submitted(WorldState world, params Order[] orders)
        => new Dictionary<int, List<Order>> { [world.Camps.Single().Player] = orders.ToList() };

    private static TechnologyStatus Status(WorldState world, Technology technology)
        => TechnologyDiscovery.Statuses(world, world.Camps.Single().People)
            .Single(status => status.Technology == technology);

    private static WorldState WatercraftFirstWorld(out int shallows)
    {
        var world = NeolithicChecks.Fixture(false);
        world.Config.TurnDays = world.Config.PracticeDays / 2;
        world.Config.OpeningCompatibilityEffects = true;
        world.Config.FishPerPerson = 100;
        // These witnesses follow a single camp's technology progress; keep the lone camp from founding a daughter (fission has its own checks).
        world.Config.FissionEnabled = false;
        var camp = world.Camps.Single();
        camp.Food = 0;
        int campShallows = world.Map.Neighbors(camp.Tile).First();
        SaltwaterShallows(world.Tiles[campShallows]);
        // Keep the visible opening seed eligible, but leave its current stock
        // empty. The active Fishing witness sits one tile beyond camp gathering.
        world.Tiles[campShallows].Food = 0;
        world.Tiles[campShallows].PotentialFood = 0;
        int hunterTile = world.Map.Neighbors(camp.Tile).First(tile => world.Tiles[tile].Land);
        shallows = world.Map.Neighbors(hunterTile)
            .First(tile => world.Map.Distance(camp.Tile, tile) == 2);
        SaltwaterShallows(world.Tiles[shallows]);
        var hunter = new Hunter
        {
            Id = world.NextId++, Player = camp.Player, SourceCamp = camp.Id,
            SocietyId = camp.SocietyId, Tile = hunterTile, Population = 20
        };
        camp.Population -= hunter.Population;
        world.Hunters.Add(hunter);
        camp.Experience[(int)Domain.Fishing] = world.Config.ExperienceThreshold - 1e-6;

        var fishing = new Order(OrderKind.Fish, hunter.Id, shallows)
            { RequestAutomaticEconomy = true };
        var prepared = TurnPlanner.Prepare(world, Submitted(world, fishing));
        Check(prepared.Rejections.Count == 0, "a valid Fish order is accepted before Watercraft discovery");
        Check(!prepared.Orders.Any(order => order.Kind == OrderKind.Economy),
            "a Fish action's existing automatic-economy request does not grant Watercraft-only Economy");
        var record = new TurnResolver().Resolve(world, prepared);
        Check(record.Transfers.Any(transfer => transfer.Entity == hunter.Id &&
            transfer.Kind == "Fishing" && transfer.Food > 0),
            "the ordinary Fish order yields a finite active Fishing receipt; actual=" +
            string.Join(";", record.Transfers.Select(transfer => transfer.Kind + ":" + transfer.Food)) +
            " events=" + string.Join(";", record.Events));
        Check(camp.Technology == Technology.Watercraft && camp.Technologies[(int)Technology.Watercraft],
            "real Fishing practice produces a Watercraft-first opening breakthrough");
        Check(camp.People.Progression.Era == Era.Neolithic &&
            !CultivationEconomy.Enabled(camp) && !CultivationEconomy.HusbandryEnabled(camp),
            "the first Watercraft breakthrough does not grant Agriculture or Husbandry");
        Check(world.Improvements.Count(improvement => improvement.Kind == ImprovementKind.FisherySeed) == 1,
            "the existing Watercraft opening compatibility seed remains a one-time first-transition effect");
        return world;
    }

    private static Order AutomaticNoOp(Camp camp)
        => new Order(OrderKind.NoOp, camp.Id) { RequestAutomaticEconomy = true };

    private static void WatercraftToAgricultureThroughGathering()
    {
        var world = WatercraftFirstWorld(out _);
        var resolver = new TurnResolver();
        var camp = world.Camps.Single();
        var openingSeeds = world.Improvements.Count;
        var transitionSeeds = camp.People.TransitionSeeds.Count;
        var gatherer = Status(world, Technology.Cultivation);
        Check(gatherer.Threshold == 225 && !gatherer.Acquired && !gatherer.Eligible,
            "the Gathering-to-Agriculture track starts gated at the shared 225-point threshold");

        bool savedPartial = false, crossedAtClose = false;
        for (int turn = 0; turn < 12 && !Status(world, Technology.Cultivation).Acquired; turn++)
        {
            camp = world.Camps.Single();
            var before = Status(world, Technology.Cultivation);
            var prepared = TurnPlanner.Prepare(world, Submitted(world, AutomaticNoOp(camp)));
            Check(prepared.Rejections.Count == 0 &&
                prepared.Orders.Any(order => order.Kind == OrderKind.NoOp && order.Entity == camp.Id),
                "ordinary camp activity remains accepted while Gathering progress accumulates");
            Check(!prepared.Orders.Any(order => order.Kind == OrderKind.Economy),
                "Watercraft-only status cannot prepare the shared Economy before Gathering unlocks Agriculture");
            var record = resolver.Resolve(world, prepared);
            Check(record.Transfers.Any(transfer => transfer.Entity == camp.Id &&
                transfer.Kind == "Gathering" && transfer.Food > 0),
                "the accepted ordinary camp turn realizes Gathering food");

            var after = Status(world, Technology.Cultivation);
            Check(after.Experience > before.Experience && after.Progress > before.Progress,
                "realized Gathering activity advances the society-owned Agriculture progress");
            Check(!prepared.Orders.Any(order => order.Kind == OrderKind.Economy),
                "secondary discovery at Close does not rewrite the already prepared turn");
            if (after.Acquired)
            {
                crossedAtClose = true;
                Check(camp.Technologies[(int)Technology.Cultivation],
                    "the acquired Agriculture capability is reflected in the shared society flags");
                Check(record.Events.Any(message => message.Contains("discovered Cultivation through Gathering")),
                    "the close-boundary event identifies the Gathering route");
                break;
            }

            Check(after.Progress < after.Threshold && after.BlockedReason == "More Gathering practice required",
                "sub-threshold Gathering remains explicitly blocked");
            byte[] bytes = SaveCodec.Save(world);
            var loaded = SaveCodec.Load(bytes);
            Check(SaveCodec.Fingerprint(world) == SaveCodec.Fingerprint(loaded),
                "partial Watercraft-first Gathering progress survives a save round trip");
            Check(Status(loaded, Technology.Cultivation).Progress == after.Progress &&
                !Status(loaded, Technology.Cultivation).Acquired,
                "the saved partial Gathering progress resumes at the same effective value");
            savedPartial = true;
            world = loaded;
        }

        camp = world.Camps.Single();
        Check(savedPartial, "the Watercraft-first Agriculture workflow persisted at least one partial progress state");
        Check(crossedAtClose && Status(world, Technology.Cultivation).Acquired,
            "ordinary Gathering eventually unlocks Agriculture at turn Close");
        Check(camp.Technology == Technology.Watercraft && camp.People.Progression.Era == Era.Neolithic,
            "secondary Agriculture preserves the Watercraft-first primary technology and Neolithic era");
        Check(world.Improvements.Count == openingSeeds && camp.People.TransitionSeeds.Count == transitionSeeds,
            "secondary Agriculture creates no repeated opening seeds or rewards");

        int destination = world.Map.Neighbors(camp.Tile).First(tile => world.Tiles[tile].Land);
        string beforePrepare = SaveCodec.Fingerprint(world);
        var move = new Order(OrderKind.Move, camp.Id, destination: destination)
            { RequestAutomaticEconomy = true };
        var next = TurnPlanner.Prepare(world, Submitted(world, move));
        var economy = next.Orders.SingleOrDefault(order => order.Kind == OrderKind.Economy && order.Entity == camp.Id);
        Check(economy != null && next.Orders.Any(order => order.Kind == OrderKind.Move && order.Entity == camp.Id),
            "the next Prepare activates existing shared Economy alongside an ordinary Move through the current request protocol");
        Check(EconomicIntents.Validate(EconomicIntents.Constraints(
            new AgentObservationBuilder().Build(world, camp.Player),
            new AgentObservationBuilder().Build(world, camp.Player).Settlements.Single()), economy.Economy) == null,
            "the next prepared shared plan uses the existing valid Economy contract");
        Check(SaveCodec.Fingerprint(world) == beforePrepare,
            "preparation remains a pure boundary while enabling newly acquired Economy");
    }

    private static void WatercraftToHusbandryThroughHunting()
    {
        var world = WatercraftFirstWorld(out _);
        var resolver = new TurnResolver();
        var camp = world.Camps.Single();
        var hunter = world.Hunters.Single();
        int preyTile = world.Map.Neighbors(camp.Tile).First(tile => world.Tiles[tile].Land);
        var prey = new Herd
        {
            Id = world.NextId++, Tile = preyTile, Animals = 300, Capacity = 300,
            Species = "antelope", DomesticCamp = -1
        };
        world.Herds.Add(prey);
        int openingSeeds = world.Improvements.Count;
        int transitionSeeds = camp.People.TransitionSeeds.Count;
        var hunterStatus = Status(world, Technology.AnimalHusbandry);
        Check(hunterStatus.Threshold == 225 && !hunterStatus.Acquired && !hunterStatus.Eligible,
            "the Hunting-to-Husbandry track starts gated at the shared 225-point threshold");

        bool savedPartial = false, crossedAtClose = false, sawSuccessfulHunt = false;
        for (int turn = 0; turn < 12 && !Status(world, Technology.AnimalHusbandry).Acquired; turn++)
        {
            camp = world.Camps.Single();
            hunter = world.Hunters.Single();
            prey = world.Herds.Single(herd => herd.Id == prey.Id);
            var before = Status(world, Technology.AnimalHusbandry);
            int predictedPreyTile = TurnResolver.HerdDestination(world, prey);
            var hunt = new Order(OrderKind.Hunt, hunter.Id, prey.Id, predictedPreyTile)
                { RequestAutomaticEconomy = true };
            var prepared = TurnPlanner.Prepare(world, Submitted(world, hunt));
            Check(prepared.Rejections.Count == 0 &&
                prepared.Orders.Any(order => order.Kind == OrderKind.Hunt && order.Entity == hunter.Id),
                "a visible Hunt order can coexist with a Watercraft-only society before Husbandry");
            Check(!prepared.Orders.Any(order => order.Kind == OrderKind.Economy),
                "Watercraft-only status cannot prepare the shared Economy before Hunting unlocks Husbandry");
            var record = resolver.Resolve(world, prepared);
            bool successfulHunt = record.Transfers.Any(transfer => transfer.Entity == hunter.Id &&
                transfer.Kind == "Hunting" && transfer.Food > 0);
            sawSuccessfulHunt |= successfulHunt;

            var after = Status(world, Technology.AnimalHusbandry);
            Check(after.Experience > before.Experience && after.Progress > before.Progress,
                "realized Hunting activity advances the society-owned Husbandry progress");
            Check(!prepared.Orders.Any(order => order.Kind == OrderKind.Economy),
                "secondary discovery at Close does not rewrite the already prepared Hunt turn");
            if (after.Acquired)
            {
                crossedAtClose = true;
                Check(camp.Technologies[(int)Technology.AnimalHusbandry],
                    "the acquired Husbandry capability is reflected in the shared society flags");
                Check(record.Events.Any(message => message.Contains("discovered AnimalHusbandry through Hunting")),
                    "the close-boundary event identifies the Hunting route");
                break;
            }

            Check(after.Progress < after.Threshold && after.BlockedReason == "More Hunting practice required",
                "sub-threshold Hunting remains explicitly blocked");
            byte[] bytes = SaveCodec.Save(world);
            var loaded = SaveCodec.Load(bytes);
            Check(SaveCodec.Fingerprint(world) == SaveCodec.Fingerprint(loaded),
                "partial Watercraft-first Hunting progress survives a save round trip");
            Check(Status(loaded, Technology.AnimalHusbandry).Progress == after.Progress &&
                !Status(loaded, Technology.AnimalHusbandry).Acquired,
                "the saved partial Hunting progress resumes at the same effective value");
            savedPartial = true;
            world = loaded;
        }

        camp = world.Camps.Single();
        Check(savedPartial, "the Watercraft-first Husbandry workflow persisted at least one partial progress state");
        Check(sawSuccessfulHunt, "the Watercraft-first Husbandry workflow realizes a successful hunt");
        Check(crossedAtClose && Status(world, Technology.AnimalHusbandry).Acquired,
            "ordinary Hunting eventually unlocks Husbandry at turn Close");
        Check(camp.Technology == Technology.Watercraft && camp.People.Progression.Era == Era.Neolithic,
            "secondary Husbandry preserves the Watercraft-first primary technology and Neolithic era");
        Check(world.Improvements.Count == openingSeeds && camp.People.TransitionSeeds.Count == transitionSeeds &&
            !world.Herds.Any(herd => herd.DomesticCamp == camp.Id),
            "secondary Husbandry creates no repeated opening seeds, animals or transition rewards");

        int destination = world.Map.Neighbors(camp.Tile).First(tile => world.Tiles[tile].Land);
        var move = new Order(OrderKind.Move, camp.Id, destination: destination)
            { RequestAutomaticEconomy = true };
        var next = TurnPlanner.Prepare(world, Submitted(world, move));
        Check(next.Orders.Any(order => order.Kind == OrderKind.Move && order.Entity == camp.Id) &&
            next.Orders.Any(order => order.Kind == OrderKind.Economy && order.Entity == camp.Id),
            "the next Prepare activates existing shared Economy alongside a Move after Husbandry unlocks");
    }

    public static int Run()
    {
        checks = 0;
        EconomyGateDiagnostics();
        CapabilityMatrix();
        AquaticFoodIsNotFabricFeedstock();
        WatercraftToAgricultureThroughGathering();
        WatercraftToHusbandryThroughHunting();
        Console.WriteLine("PASS: " + checks + " Issue 32 capability assertions");
        return checks;
    }
}
