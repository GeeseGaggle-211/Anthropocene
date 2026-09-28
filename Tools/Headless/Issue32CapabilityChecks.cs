using System;
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

    public static int Run()
    {
        checks = 0;
        EconomyGateDiagnostics();
        Console.WriteLine("PASS: " + checks + " Issue 32 capability assertions");
        return checks;
    }
}
