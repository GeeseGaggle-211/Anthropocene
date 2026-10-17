using System;
using System.Linq;
using Anthropocene.Simulation;

internal static class FishingObservationChecks
{
    private static int checks;
    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception("Fishing observation: " + message);
    }

    private static int AtDistance(WorldState world, int origin, int distance)
        => world.Map.Radius(origin, distance).Where(tile => world.Map.Distance(origin, tile) == distance).OrderBy(tile => tile).First();

    private static void MakeFisheryWater(Tile tile, EnvironmentKind environment)
    {
        tile.Environment = environment;
        tile.Terrain = environment == EnvironmentKind.Lake ? Terrain.Lake : Terrain.Shallows;
        tile.AquaticCondition = .8;
        tile.Food = 210;
        tile.PotentialFood = 1200;
        tile.Freshwater = 0;
        tile.FreshwaterSource = FreshwaterSource.None;
        for (int season = 0; season < tile.Seasons.Length; season++)
        {
            var profile = tile.Seasons[season];
            profile.MaxFood = 210;
            profile.MaxPotentialFood = 1200;
            profile.FoodRecoveryPerDay = .004f;
            profile.BiomassRecoveryPerDay = .004f;
            tile.Seasons[season] = profile;
        }
    }

    private static WorldState Fixture(bool fishingFeature, bool watercraft)
    {
        var world = NeolithicChecks.Fixture(false);
        world.Config.EconomicAllocation = EconomicAllocationStrategy.DemandControl;
        world.Config.NeolithicFishingEnabled = fishingFeature;
        world.Config.CorrectedAquaticSeasonalPressureEnabled = fishingFeature;
        var camp = world.Camps[0];
        camp.Technology = watercraft ? Technology.Watercraft : Technology.None;
        camp.Technologies[(int)Technology.Watercraft] = watercraft;
        camp.People.Progression.Era = watercraft ? Era.Neolithic : Era.Paleolithic;
        Ownership.Bind(world);
        return world;
    }

    public static int Run()
    {
        checks = 0;

        var disabled = Fixture(false, true);
        var disabledCamp = disabled.Camps[0];
        var disabledObservation = new AgentObservationBuilder().Build(disabled, disabledCamp.Player);
        var disabledSettlement = disabledObservation.Settlements.Single(s => s.Id == disabledCamp.Id);
        Check(!disabledSettlement.FishingEnabled && disabledObservation.FishingGrounds.Count == 0,
            "feature-off Watercraft retains its old closed Economy and exposes no resident Fishing grounds");
        Check(!LegalActions.Build(disabledObservation).Single(m => m.Entity == disabledCamp.Id).Actions.Any(a => a.Kind == OrderKind.Economy),
            "feature-off Watercraft has no Economy action mask");

        var enabled = Fixture(true, true);
        var camp = enabled.Camps[0];
        var near = AtDistance(enabled, camp.Tile, 1);
        MakeFisheryWater(enabled.Tiles[near], EnvironmentKind.Shallows);
        var observation = new AgentObservationBuilder().Build(enabled, camp.Player);
        var settlement = observation.Settlements.Single(s => s.Id == camp.Id);
        var mask = LegalActions.Build(observation).Single(m => m.Entity == camp.Id);
        Check(settlement.FishingEnabled && mask.Actions.Any(a => a.Kind == OrderKind.Economy),
            "feature-enabled Watercraft opens the shared Economy capability");
        var fisherPlan = EconomicIntent.From(camp);
        fisherPlan.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Fisher, Population = 2 });
        Check(EconomicIntents.Validate(observation, settlement, fisherPlan) == null,
            "Fisher validation follows feature-enabled Watercraft without requiring a source in the action schema");
        Check(FishingEconomy.CanFish(enabled, camp) && observation.FishingGrounds.Any(g => g.Tile == near && g.Visible),
            "a visible adjacent Shallows source is listed and is legal for resident Fishing preparation");
        double q = .5;
        double capital = Math.Pow(q / enabled.Config.ReferenceIntensity, 2);
        enabled.FisherySites.Add(new FisherySite
        {
            TileId = near, OperatorCampId = -1, Capital = capital,
            LastUpdatedDays = enabled.Days
        });
        var managedObservation = new AgentObservationBuilder().Build(enabled, camp.Player);
        var managedTile = managedObservation.Tiles[near];
        var projected = LazyEcology.ProjectEcology(enabled, near, enabled.Days);
        var overlay = LazyEcology.ProjectAquaticOverlay(enabled, near, enabled.Days, 0);
        var capacity = new EcologyCapacity(enabled, enabled.Tiles[near], projected,
            enabled.Season, aquaticOverlay: overlay);
        Check(managedTile.GatherablePercent == (int)Math.Round(100 * capacity.Food / capacity.Biomass) &&
            managedTile.FoodMaximum == Knowledge.Band(capacity.Food, 70, enabled.Config) &&
            managedTile.Productivity == Knowledge.Band((capacity.Food * capacity.FoodRate + capacity.ManagedFlow)
                * enabled.Config.BaseDays, 30, enabled.Config),
            "visible tile observations include authoritative fishery accessibility and productivity");
        var serialized = ObservationContract.Serialize(observation, camp.Mandate);
        var dto = ObservationContract.Deserialize(serialized);
        Check(dto.Schema == 23 && dto.FishingEnabled && dto.FishingGrounds.Any(g => g.Tile == near && g.Visible),
            "the current schema retains the feature capability and filtered visible-ground DTO");
        Check(dto.Settlements.Single(s => s.Id == camp.Id).FishingEnabled &&
            dto.Settlements.Single(s => s.Id == camp.Id).FishingTools == camp.Inventory.FishingTools,
            "settlement-level Fishing and portable tool fields survive DTO serialization");

        var blockedConfig = Fixture(true, true);
        blockedConfig.Config.CorrectedAquaticSeasonalPressureEnabled = false;
        bool rejected = false;
        try { blockedConfig.Config.Validate(); }
        catch (ArgumentException) { rejected = true; }
        Check(rejected, "resident Fishing requires the corrected aquatic seasonal-pressure gate");
        blockedConfig.Config.CorrectedAquaticSeasonalPressureEnabled = true;
        blockedConfig.Config.EconomicAllocation = EconomicAllocationStrategy.Legacy;
        rejected = false;
        try { blockedConfig.Config.Validate(); }
        catch (ArgumentException) { rejected = true; }
        Check(rejected, "resident Fishing rejects the Legacy automatic allocator");

        // Use owner memory, not current hidden aquatic state, for an out-of-sight
        // remembered ground. A second remembered water tile has no survey and
        // exercises the declared zero-yield/zero-capital prior.
        var hiddenWorld = Fixture(true, true);
        camp = hiddenWorld.Camps[0];
        hiddenWorld.Config.Players = 2;
        var rival = new Camp
        {
            Id = hiddenWorld.NextId++, Player = 1,
            Tile = AtDistance(hiddenWorld, camp.Tile, 12), Population = 24
        };
        hiddenWorld.Camps.Add(rival);
        Ownership.Bind(hiddenWorld);
        int visited = AtDistance(hiddenWorld, camp.Tile, 3);
        int unsurveyed = AtDistance(hiddenWorld, camp.Tile, 4);
        MakeFisheryWater(hiddenWorld.Tiles[visited], EnvironmentKind.Lake);
        MakeFisheryWater(hiddenWorld.Tiles[unsurveyed], EnvironmentKind.Shallows);
        var society = camp.People;
        society.Memory = new ContactMemory(hiddenWorld.Config.Width);
        society.Memory.Explored.Add(visited, hiddenWorld.Turn);
        society.Memory.Explored.Add(unsurveyed, hiddenWorld.Turn);
        camp.LastSelectedFishingGrounds.Add(visited);
        society.FishingGroundKnowledge.Add(new FishingGroundSnapshot
        {
            TileId = visited,
            OperatorCampId = rival.Id,
            AccessibleFood = 245,
            Biomass = 1600,
            Condition = .62,
            Capital = .4,
            OperationFraction = .75,
            ObservedDays = hiddenWorld.Days,
            ProductiveProfile = new[] { .2, .3, .4, .5 }
        });
        hiddenWorld.FisherySites.Add(new FisherySite { TileId = visited, OperatorCampId = rival.Id, Capital = .9, LastUpdatedDays = hiddenWorld.Days });
        hiddenWorld.FisherySites.Add(new FisherySite { TileId = unsurveyed, OperatorCampId = -1, Capital = .7, LastUpdatedDays = hiddenWorld.Days });
        var hiddenObservation = new AgentObservationBuilder().Build(hiddenWorld, camp.Player);
        var rememberedGround = hiddenObservation.FishingGrounds.Single(g => g.Tile == visited);
        var conservativeGround = hiddenObservation.FishingGrounds.Single(g => g.Tile == unsurveyed);
        Check(!rememberedGround.Visible && rememberedGround.Visited && rememberedGround.Selected && rememberedGround.OperatorKnown &&
            !rememberedGround.OwnOperator && rememberedGround.OperatorCamp == -1 && rememberedGround.HasOperation,
            "visited hidden grounds expose only owner snapshots and suppress foreign operator identity");
        Check(!conservativeGround.Visible && !conservativeGround.Visited && conservativeGround.AccessibleFood == 0 &&
            conservativeGround.Biomass == 0 && conservativeGround.CapitalIntensity == 0 && conservativeGround.Productivity == 0,
            "remembered but unsurveyed geometry uses zero accessible yield, zero productivity, and zero capital prior");
        string rememberedJson = ObservationContract.Serialize(hiddenObservation, camp.Mandate);
        hiddenWorld.Tiles[visited].Food = 900000;
        hiddenWorld.Tiles[visited].PotentialFood = 1700000;
        hiddenWorld.Tiles[visited].AquaticCondition = .01;
        hiddenWorld.FisherySites.Single(s => s.TileId == visited).Capital = 1200;
        hiddenWorld.Tiles[unsurveyed].Food = 650000;
        hiddenWorld.Tiles[unsurveyed].PotentialFood = 980000;
        hiddenWorld.FisherySites.Single(s => s.TileId == unsurveyed).Capital = 2400;
        string mutatedJson = ObservationContract.Serialize(new AgentObservationBuilder().Build(hiddenWorld, camp.Player), camp.Mandate);
        Check(rememberedJson == mutatedJson,
            "hidden live stocks, condition and sparse site capital cannot influence a policy observation outside sight");

        Console.WriteLine("PASS: " + checks + " Fishing observation assertions");
        return checks;
    }
}
