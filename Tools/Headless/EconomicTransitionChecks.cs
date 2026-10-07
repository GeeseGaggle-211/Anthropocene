using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using Anthropocene.Simulation;

internal static class EconomicTransitionChecks
{
    private static int checks;
    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception("Economic transition: " + message);
    }

    private static Dictionary<int, List<Order>> Submitted(WorldState world, IEnumerable<Order> orders)
        => new Dictionary<int, List<Order>> { [world.Camps.Single().Player] = orders.ToList() };

    private static WorldState Transition(Technology technology, bool fishingFeature,
        out Camp camp, out Hunter hunter)
    {
        var world = NeolithicChecks.Fixture(false);
        world.Config.NeolithicFishingEnabled = fishingFeature;
        world.Config.CorrectedAquaticSeasonalPressureEnabled = fishingFeature;
        world.Config.EconomicAllocation = EconomicAllocationStrategy.DemandControl;
        camp = world.Camps.Single();
        int campTile = camp.Tile;
        int tile = world.Map.Radius(campTile, 2)
            .Where(id => world.Map.Distance(campTile, id) == 2 && world.Tiles[id].TraversableLand)
            .OrderBy(id => id).First();
        hunter = new Hunter
        {
            Id = world.NextId++, Player = camp.Player, SocietyId = camp.SocietyId,
            SourceCamp = camp.Id, Tile = tile, Population = 8
        };
        camp.Population -= hunter.Population;
        world.Hunters.Add(hunter);
        var domain = (Domain)((int)technology - 1);
        camp.Experience[(int)domain] = world.Config.ExperienceThreshold;
        TurnResolver.Breakthrough(world, camp, new TurnRecord());
        return world;
    }

    private static void UnlockEvents()
    {
        foreach (var item in new[]
        {
            (Technology.Cultivation, false),
            (Technology.AnimalHusbandry, false),
            (Technology.Watercraft, true)
        })
        {
            var world = Transition(item.Item1, item.Item2, out var camp, out var hunter);
            Check(camp.Technology == item.Item1 && hunter.EconomicTransitionRecall,
                item.Item1 + " marks an existing detachment exactly at the first resident-economy unlock");
            var later = new Hunter
            {
                Id = world.NextId++, Player = camp.Player, SocietyId = camp.SocietyId,
                SourceCamp = camp.Id, Tile = camp.Tile, Population = 4
            };
            world.Hunters.Add(later);
            Check(!later.EconomicTransitionRecall,
                item.Item1 + " does not reinterpret a later deliberate dispatch as transition debt");
        }

        var disabled = Transition(Technology.Watercraft, false, out var watercraftCamp,
            out var watercraftHunter);
        Check(!watercraftHunter.EconomicTransitionRecall &&
            !CultivationEconomy.EconomyEnabled(disabled, watercraftCamp),
            "feature-off Watercraft retains the historical nonresident path without a recall");
        watercraftCamp.People.NeolithicExperience[(int)Domain.Gathering] =
            disabled.Config.SecondaryTechnologyThreshold;
        TechnologyDiscovery.QueueEligible(disabled, watercraftCamp.People, Domain.Gathering);
        TechnologyDiscovery.Resolve(disabled, new TurnRecord());
        Check(watercraftCamp.Technologies[(int)Technology.Cultivation] &&
            watercraftHunter.EconomicTransitionRecall,
            "the later secondary technology starts the recall when it first opens the resident economy");
    }

    private static void PolicyAndResolution()
    {
        var world = Transition(Technology.Cultivation, false, out var camp, out var hunter);
        int startingResidents = camp.Population;
        var observation = new AgentObservationBuilder().Build(world, camp.Player);
        var dto = ObservationContract.Deserialize(ObservationContract.Serialize(observation, camp.Mandate));
        Check(AgentObservation.SchemaVersion == 22 && dto.Schema == 22 &&
            dto.Hunters.Single(h => h.Id == hunter.Id).EconomicTransitionRecall &&
            dto.Hunters.Single(h => h.Id == hunter.Id).EconomicTransitionWaypoint == camp.Tile,
            "the current schema exposes the owned detachment's marker and only its currently visible route endpoint");

        var decision = new HeuristicTribePolicy().Decide(observation, camp.Mandate);
        var campOrder = decision.Orders.Single(order => order.Entity == camp.Id);
        var hunterOrder = decision.Orders.Single(order => order.Entity == hunter.Id);
        Check(campOrder.Kind == OrderKind.NoOp && campOrder.RequestAutomaticEconomy,
            "the resident economy starts while its source camp holds position for the return");
        Check(hunterOrder.Kind == OrderKind.Move && hunterOrder.Destination == camp.Tile,
            "the marked detachment takes the deterministic legal move closest to its source camp");
        Check(decision.Trace.Candidates.Any(candidate => candidate.Selected &&
            candidate.Components.Contains("EconomicTransitionRecall")),
            "the policy trace explains the transition override");

        var loaded = SaveCodec.Load(SaveCodec.Save(world));
        Check(loaded.Hunters.Single(h => h.Id == hunter.Id).EconomicTransitionRecall &&
            loaded.Hunters.Single(h => h.Id == hunter.Id).EconomicTransitionRoute
                .SequenceEqual(hunter.EconomicTransitionRoute) &&
            loaded.Hunters.Single(h => h.Id == hunter.Id).EconomicTransitionVisited
                .SetEquals(hunter.EconomicTransitionVisited),
            "an active recall and its visible search state round-trip through the compatible world envelope");

        new TurnResolver().Resolve(world, Submitted(world, decision.Orders));
        hunter = world.Hunters.Single(h => h.Id == hunter.Id);
        Check(hunter.Tile == camp.Tile && hunter.EconomicTransitionRecall,
            "the first transition turn returns the marked detachment without silently merging it");
        observation = new AgentObservationBuilder().Build(world, camp.Player);
        decision = new HeuristicTribePolicy().Decide(observation, camp.Mandate);
        Check(decision.Orders.Single(order => order.Entity == hunter.Id).Kind == OrderKind.Reintegrate,
            "a returned transition detachment explicitly reintegrates on the next boundary");
        new TurnResolver().Resolve(world, Submitted(world, decision.Orders));
        Check(world.Hunters.All(group => group.Id != hunter.Id) &&
            camp.Population == startingResidents + hunter.Population,
            "reintegration restores the resident workforce and retires the bounded recall state");
    }

    private static void BarrierRoute()
    {
        var world = NeolithicChecks.Fixture(false);
        world.Config.Consumption = .001;
        var camp = world.Camps.Single();
        camp.Tile = world.Map.At(12, 8);
        camp.Technology = Technology.Cultivation;
        camp.Technologies[(int)Technology.Cultivation] = true;
        camp.People.Progression.Era = Era.Neolithic;
        var hunter = new Hunter
        {
            Id = world.NextId++, Player = camp.Player, SocietyId = camp.SocietyId,
            SourceCamp = camp.Id, Tile = world.Map.At(8, 8), Population = 1
        };
        camp.Population -= hunter.Population;
        world.Hunters.Add(hunter);
        for (int row = 3; row <= 13; row++)
            world.Tiles[world.Map.At(10, row)].ElevationLevel = 10;

        Check(EconomicTransition.Begin(world, camp.People) == 1 &&
            hunter.EconomicTransitionRoute.SequenceEqual(new[] { hunter.Tile }) &&
            hunter.EconomicTransitionVisited.SetEquals(new[] { hunter.Tile }),
            "the unlock records only the detachment's starting tile and reads no hidden route");
        var resolver = new TurnResolver();
        bool legal = true;
        int turns = 0;
        for (; turns < 50 && world.Hunters.Any(h => h.Id == hunter.Id); turns++)
        {
            var observation = new AgentObservationBuilder().Build(world, camp.Player);
            var decision = new HeuristicTribePolicy().Decide(observation, camp.Mandate);
            var hunterOrder = decision.Orders.Single(order => order.Entity == hunter.Id);
            legal &= OrderRules.Validate(observation, hunterOrder) == null;
            resolver.Resolve(world, Submitted(world, decision.Orders));
        }
        Check(legal,
            "every visible exploration or backtracking step obeys the shared movement contract");
        Check(world.Hunters.All(h => h.Id != hunter.Id),
            "visible depth-first exploration reaches and reintegrates around the ridge in " + turns +
            " turns without cycling; tile=" +
            hunter.Tile + " route=" + (hunter.EconomicTransitionRoute?.Count ?? 0) +
            " visited=" + (hunter.EconomicTransitionVisited?.Count ?? 0));
    }

    private static WorldState PrivacyWitness(int gap, out Camp camp, out Hunter hunter)
    {
        var world = NeolithicChecks.Fixture(false);
        world.Config.Consumption = .001;
        camp = world.Camps.Single();
        camp.Tile = world.Map.At(12, 8);
        camp.Technology = Technology.Cultivation;
        camp.Technologies[(int)Technology.Cultivation] = true;
        camp.People.Progression.Era = Era.Neolithic;
        hunter = new Hunter
        {
            Id = world.NextId++, Player = camp.Player, SocietyId = camp.SocietyId,
            SourceCamp = camp.Id, Tile = world.Map.At(8, 8), Population = 1
        };
        camp.Population -= hunter.Population;
        world.Hunters.Add(hunter);
        for (int row = 0; row < world.Map.Height; row++)
            world.Tiles[world.Map.At(10, row)].ElevationLevel = 10;
        world.Tiles[world.Map.At(10, gap)].ElevationLevel = 0;
        EconomicTransition.Begin(world, camp.People);
        return world;
    }

    private static void PrivacyAndMovementCost()
    {
        var north = PrivacyWitness(2, out var northCamp, out var northHunter);
        var south = PrivacyWitness(north.Map.Height - 3, out var southCamp, out var southHunter);
        var northObservation = new AgentObservationBuilder().Build(north, northCamp.Player);
        var southObservation = new AgentObservationBuilder().Build(south, southCamp.Player);
        string northJson = ObservationContract.Serialize(northObservation, northCamp.Mandate);
        string southJson = ObservationContract.Serialize(southObservation, southCamp.Mandate);
        var northOrder = new HeuristicTribePolicy().Decide(northObservation, northCamp.Mandate)
            .Orders.Single(order => order.Entity == northHunter.Id);
        var southOrder = new HeuristicTribePolicy().Decide(southObservation, southCamp.Mandate)
            .Orders.Single(order => order.Entity == southHunter.Id);
        Check(northJson == southJson && northOrder.ToString() == southOrder.ToString(),
            "moving a ridge gap only in unseen terrain cannot change an observation or recall order");

        var world = NeolithicChecks.Fixture(false);
        world.Config.Consumption = .001;
        world.Config.WaterMovement = 2;
        var camp = world.Camps.Single();
        int start = world.Map.At(10, 8), shallow = world.Map.At(11, 8);
        camp.Tile = world.Map.At(12, 8);
        camp.Technology = Technology.Cultivation;
        camp.Technologies[(int)Technology.Cultivation] = true;
        camp.Technologies[(int)Technology.Watercraft] = true;
        camp.People.Progression.Era = Era.Neolithic;
        foreach (int tile in world.Map.Neighbors(start).Concat(world.Map.Neighbors(camp.Tile))
            .Where(tile => tile != start && tile != shallow && tile != camp.Tile))
            world.Tiles[tile].ElevationLevel = 10;
        world.Tiles[start].Terrain = Terrain.Grassland;
        world.Tiles[start].ElevationLevel = 0;
        world.Tiles[shallow].Terrain = Terrain.Shallows;
        world.Tiles[shallow].ElevationLevel = 0;
        world.Tiles[camp.Tile].Terrain = Terrain.Grassland;
        world.Tiles[camp.Tile].ElevationLevel = 0;
        var hunter = new Hunter
        {
            Id = world.NextId++, Player = camp.Player, SocietyId = camp.SocietyId,
            SourceCamp = camp.Id, Tile = start, Population = 1
        };
        camp.Population -= hunter.Population;
        world.Hunters.Add(hunter);
        EconomicTransition.Begin(world, camp.People);
        var observation = new AgentObservationBuilder().Build(world, camp.Player);
        var observed = observation.Hunters.Single(h => h.Id == hunter.Id);
        var order = new HeuristicTribePolicy().Decide(observation, camp.Mandate)
            .Orders.Single(candidate => candidate.Entity == hunter.Id);
        Check(observation.WaterMovementCost == 1 &&
            observed.EconomicTransitionWaypoint == camp.Tile &&
            order.Kind == OrderKind.Move && order.Destination == camp.Tile &&
            OrderRules.Validate(observation, order) == null,
            "recall planning and legality share the configured shallow-water movement cost");
    }

    private static void Cancellation()
    {
        var waiting = Transition(Technology.Cultivation, false, out var waitCamp,
            out var waitHunter);
        new TurnResolver().Resolve(waiting, Submitted(waiting, new[]
        {
            new Order(OrderKind.NoOp, waitCamp.Id),
            new Order(OrderKind.NoOp, waitHunter.Id)
        }));
        Check(!waitHunter.EconomicTransitionRecall &&
            waitHunter.EconomicTransitionRoute == null &&
            waitHunter.EconomicTransitionVisited == null,
            "an explicit owner wait cancels the policy-visible automatic recall state");

        var moving = Transition(Technology.Cultivation, false, out var moveCamp,
            out var moveHunter);
        var observation = new AgentObservationBuilder().Build(moving, moveCamp.Player);
        int destination = moving.Map.Neighbors(moveCamp.Tile)
            .Where(tile => moving.Tiles[tile].TraversableLand)
            .First(tile => OrderRules.Validate(observation,
                new Order(OrderKind.Move, moveCamp.Id, destination: tile)) == null);
        new TurnResolver().Resolve(moving, Submitted(moving, new[]
        {
            new Order(OrderKind.Move, moveCamp.Id, destination: destination),
            new Order(OrderKind.Move, moveHunter.Id, destination: moveCamp.Tile)
        }));
        Check(!moveHunter.EconomicTransitionRecall &&
            moveHunter.EconomicTransitionRoute == null &&
            moveHunter.EconomicTransitionVisited == null,
            "moving the held source camp cancels its hunters' stale return state through the camp index");
    }

    private static void FishingPolicyAndCompatibility()
    {
        var config = SimulationConfig.Small(1);
        config.NeolithicFishingEnabled = true;
        config.CorrectedAquaticSeasonalPressureEnabled = true;
        config.EconomicAllocation = EconomicAllocationStrategy.DemandControl;
        var world = new WorldGenerator().Generate(3701, config);
        var camp = world.Camps.Single();
        camp.Technology = Technology.Watercraft;
        camp.Technologies[(int)Technology.Watercraft] = true;
        camp.People.Progression.Era = Era.Neolithic;
        var decision = new HeuristicTribePolicy().Decide(
            new AgentObservationBuilder().Build(world, camp.Player), camp.Mandate);
        Check(decision.Orders.Single(order => order.Entity == camp.Id).Kind == OrderKind.Dispatch &&
            decision.Orders.Single(order => order.Entity == camp.Id).RequestAutomaticEconomy,
            "unused Fishing capability requests resident economic preparation without suppressing an otherwise valid dispatch");

        var serializer = new DataContractSerializer(typeof(Hunter));
        string xml;
        using (var stream = new MemoryStream())
        {
            serializer.WriteObject(stream, new Hunter
                {
                    Id = 1, EconomicTransitionRecall = true,
                    EconomicTransitionRoute = new List<int> { 2, 3 },
                    EconomicTransitionVisited = new HashSet<int> { 2, 3 }
                });
            xml = Encoding.UTF8.GetString(stream.ToArray());
        }
        string legacy = Regex.Replace(xml,
            "<EconomicTransitionRecall>[^<]*</EconomicTransitionRecall>", "");
        legacy = Regex.Replace(legacy,
            "<EconomicTransitionRoute[^>]*>.*?</EconomicTransitionRoute>", "", RegexOptions.Singleline);
        legacy = Regex.Replace(legacy,
            "<EconomicTransitionVisited[^>]*>.*?</EconomicTransitionVisited>", "", RegexOptions.Singleline);
        Check(legacy != xml && !legacy.Contains("EconomicTransition"),
            "the compatibility fixture removes the new optional hunter transition state");
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(legacy)))
        {
            var old = (Hunter)serializer.ReadObject(stream);
            Check(!old.EconomicTransitionRecall && old.EconomicTransitionRoute == null &&
                old.EconomicTransitionVisited == null,
                "an older save without transition state continues without inventing a recall");
        }

        var privacy = new WorldGenerator().Generate(3702, SimulationConfig.Small(2));
        var observer = privacy.Camps[0];
        var foreign = privacy.Camps[1];
        var foreignHunter = new Hunter
        {
            Id = privacy.NextId++, Player = foreign.Player, SocietyId = foreign.SocietyId,
            SourceCamp = foreign.Id, Tile = observer.Tile, Population = 4,
            EconomicTransitionRecall = true,
            EconomicTransitionRoute = new List<int> { observer.Tile },
            EconomicTransitionVisited = new HashSet<int> { observer.Tile }
        };
        foreign.Population -= foreignHunter.Population;
        privacy.Hunters.Add(foreignHunter);
        var foreignView = new AgentObservationBuilder().Build(privacy, observer.Player)
            .ForeignHunters.Single(h => h.Id == foreignHunter.Id);
        Check(!foreignView.EconomicTransitionRecall && foreignView.EconomicTransitionWaypoint == -1,
            "a foreign hunter never reveals its private transition marker or route endpoint");
    }

    public static int Run()
    {
        checks = 0;
        UnlockEvents();
        PolicyAndResolution();
        BarrierRoute();
        PrivacyAndMovementCost();
        Cancellation();
        FishingPolicyAndCompatibility();
        Console.WriteLine("PASS: " + checks + " economic-transition assertions");
        return checks;
    }
}
