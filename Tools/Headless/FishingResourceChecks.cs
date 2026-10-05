using System;
using System.Collections.Generic;
using System.Linq;
using Anthropocene.Simulation;

internal static class FishingResourceChecks
{
    private static int checks;

    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception("Fishing resources: " + message);
    }

    private static void Near(double actual, double expected, string message, double tolerance = 1e-8)
    {
        checks++;
        if (double.IsNaN(actual) || Math.Abs(actual - expected) > tolerance * Math.Max(1, Math.Abs(expected)))
            throw new Exception("Fishing resources: " + message + " (actual=" + actual + ", expected=" + expected + ")");
    }

    private static Tile Water(WorldState world, int id, double stock = 1000, double biomass = 1000)
    {
        var tile = world.Tiles[id];
        tile.Terrain = Terrain.Shallows;
        tile.ElevationLevel = 0;
        tile.WaterBody = WaterKind.Saltwater;
        tile.FreshwaterSource = FreshwaterSource.None;
        tile.Freshwater = 0;
        tile.Food = stock;
        tile.PotentialFood = biomass;
        tile.AquaticCondition = 1;
        tile.LandCondition = 1;
        tile.LastEcologyUpdateDays = world.Days;
        tile.NaturalVegetationPotential = tile.CurrentVegetation = VegetationTier.High;
        for (int season = 0; season < 4; season++)
            tile.Seasons[season] = new SeasonData
            {
                MaxFood = (float)biomass,
                MaxPotentialFood = (float)biomass,
                MaxFreshwater = 0,
                FoodRecoveryPerDay = 0,
                BiomassRecoveryPerDay = .02f,
                WaterRecoveryPerDay = 0,
                Moisture = .5f,
                Habitat = .5f
            };
        world.Societies.Single(s => s.Id == tileSociety(world)).Memory.Explored.Add(id, world.Turn);
        return tile;
    }

    private static int tileSociety(WorldState world) => world.Camps.OrderBy(c => c.Id).First().SocietyId;

    private static void EnableFishing(WorldState world, Camp camp, int fishers)
    {
        var society = world.Societies.Single(s => s.Id == camp.SocietyId);
        society.Technology = Technology.Watercraft;
        society.Technologies[(int)Technology.Watercraft] = true;
        camp.Workforce.Assign(WorkforceRoles.Fisher, fishers, camp.Population);
        camp.Inventory.FishingTools = 3;
    }

    private static int SharedShore(WorldState world, Camp first, Camp second = null)
    {
        if (second == null)
            return world.Map.Neighbors(first.Tile).OrderBy(id => id).First();
        return Enumerable.Range(0, world.Tiles.Length)
            .Where(id => world.Map.Distance(first.Tile, id) == 1 && world.Map.Distance(second.Tile, id) == 1)
            .OrderBy(id => id).First();
    }

    private sealed class Resolved
    {
        public TurnRecord Record;
        public TurnResolver Resolver;
        public PreparedTurn Prepared;
    }

    private static Resolved Resolve(WorldState world, IReadOnlyDictionary<int, List<Order>> orders,
        PhaseExecution execution = null)
    {
        execution ??= new PhaseExecution();
        var prepared = TurnPlanner.Prepare(world, orders, null, execution);
        var resolver = new TurnResolver(execution);
        return new Resolved { Prepared = prepared, Resolver = resolver, Record = resolver.Resolve(world, prepared) };
    }

    private static void SharedPoolReceiptsAndTools()
    {
        var world = FishingEconomyChecks.Fixture(24);
        world.Config.GatherRadius = 1;
        world.Config.Consumption = .15;
        var camp = world.Camps.Single();
        int shore = SharedShore(world, camp);
        Water(world, shore);
        foreach (int id in world.Map.Neighbors(camp.Tile).Where(id => id != shore))
            world.Tiles[id].Food = 0;
        EnableFishing(world, camp, fishers: 4);
        int hunterId = world.NextId++;
        world.Hunters.Add(new Hunter
        {
            Id = hunterId, SourceCamp = camp.Id, SocietyId = camp.SocietyId,
            Player = camp.Player, Tile = camp.Tile, Population = 1
        });
        double toolsAtOpen = camp.Inventory.FishingTools;
        double practiceAtOpen = camp.People.Experience[(int)Domain.Fishing];
        var submitted = new Dictionary<int, List<Order>>
        {
            [camp.Player] = new List<Order> { new Order(OrderKind.Fish, hunterId, shore) }
        };
        var prepared = TurnPlanner.Prepare(world, submitted);
        Check(prepared.FishingPlans.TryGetValue(camp.Id, out var plan), "resident Fishing plan is prepared");
        Check(plan.Grounds.Any(g => g.TileId == shore && g.RequestedCatch > 0), "resident catch is claimed on shared shore");
        double coveredWork = plan.Grounds.Sum(g => Math.Max(0, g.ToolCoveredHarvestWorkerYears));

        var resolver = new TurnResolver();
        var record = resolver.Resolve(world, prepared);
        var atShore = record.Transfers.Where(t => t.Tile == shore).ToArray();
        double resident = atShore.Where(t => t.Entity == camp.Id && t.Kind == "FisheryHarvest").Sum(t => t.Food);
        double hunter = atShore.Where(t => t.Entity == hunterId && t.Kind == "Fishing").Sum(t => t.Food);
        double passive = atShore.Where(t => t.Entity == camp.Id &&
            (t.Kind == "Marine gathering" || t.Kind == "Aquatic gathering")).Sum(t => t.Food);
        Check(resident > 0 && hunter > 0 && passive > 0,
            "resident, Hunter, and passive aquatic receipts all draw from one shared tile");
        Check(record.EcologyBalances.Count(b => b.Tile == shore) == 1,
            "the shared aquatic tile is integrated exactly once");
        Check(resolver.LastMetrics.TileIntegrations == record.EcologyBalances.Count,
            "one committed balance exists for every active tile integration");
        var balance = record.EcologyBalances.Single(b => b.Tile == shore);
        Near(balance.FoodHarvest, resident + hunter + passive,
            "all same-tile human receipts reconcile to its finite food harvest");
        Near(balance.StartingFood + balance.FoodRecharge - balance.FoodHarvest - balance.FoodOverflow,
            balance.EndingFood, "shared-pool stock-flow ledger conserves aquatic food", 1e-7);
        Check(camp.AquaticFood > 0, "resident and Hunter fish arrive as typed AquaticFood");
        Near(toolsAtOpen - camp.Inventory.FishingTools,
            coveredWork / world.Config.Neolithic.FishingToolFisherYearsPerUnit,
            "opening Fishing Tools are debited once for prepared covered Fisher work", 1e-7);

        var harvest = record.Transfers;
        var hunterIds = new HashSet<int> { hunterId };
        double passiveAquatic = harvest.Where(t => t.Entity == camp.Id &&
            (t.Kind == "Marine gathering" || t.Kind == "Aquatic gathering")).Sum(t => t.Food);
        double gathering = harvest.Where(t => t.Entity == camp.Id && t.Kind == "Gathering").Sum(t => t.Food)
            + passiveAquatic * (1 - world.Config.PassiveAquaticFishingPracticeShare);
        double fishing = (harvest.Where(t => hunterIds.Contains(t.Entity) && t.Kind == "Fishing").Sum(t => t.Food)
            + harvest.Where(t => t.Entity == camp.Id && t.Kind == "FisheryHarvest").Sum(t => t.Food))
            * world.Config.ActiveFishingPracticeMultiplier
            + passiveAquatic * world.Config.PassiveAquaticFishingPracticeShare;
        double hunting = harvest.Where(t => hunterIds.Contains(t.Entity) && t.Kind == "Hunting").Sum(t => t.Food);
        double budget = record.Demographics.Single(d => d.Camp == camp.Id).StartingPopulation
            * record.Duration / world.Config.PracticeDays;
        double expectedPractice = budget * fishing / Math.Max(1e-12, hunting + gathering + fishing);
        Near(camp.People.Experience[(int)Domain.Fishing] - practiceAtOpen, expectedPractice,
            "Fishing practice credits retained resident/Hunter/passive receipts exactly once", 1e-7);

        var site = world.FisherySites.Single(s => s.TileId == shore);
        Check(site.OperatorCampId == camp.Id && site.Capital > 0,
            "committed fishery endpoint has a canonical operator and physical capital");
        var memory = world.Societies.Single(s => s.Id == camp.SocietyId).FishingGroundKnowledge
            .Single(s => s.TileId == shore);
        Check(memory.ProductiveProfile.Length == 4 && memory.ObservedDays == world.Days,
            "close refreshes the owner's four-season fishing snapshot at the endpoint");
        Check(memory.OperationFraction >= 0 && memory.OperationFraction <= 1,
            "owner snapshot stores a clamped operation fraction");
    }

    private static void OperatorTieRetentionAndDeparture()
    {
        var world = FishingEconomyChecks.Fixture(24);
        world.Config.Consumption = .1;
        var first = world.Camps.Single();
        var second = new Camp
        {
            Id = 2, Player = first.Player, SocietyId = first.SocietyId,
            Tile = world.Map.At(12, 8), Population = 24
        };
        world.Camps.Add(second);
        Ownership.Bind(world);
        EnableFishing(world, first, 12);
        EnableFishing(world, second, 12);
        int shore = SharedShore(world, first, second);
        Water(world, shore, stock: 10000, biomass: 10000);
        first.People.Memory.Explored.Add(shore, world.Turn);
        var firstTurn = Resolve(world, new Dictionary<int, List<Order>>());
        var site = world.FisherySites.Single(s => s.TileId == shore);
        Check(site.OperatorCampId == first.Id,
            "new tied fishery operators are resolved by lowest camp ID");
        Check(firstTurn.Record.Transfers.Any(t => t.Tile == shore && t.Entity == first.Id && t.Kind == "FisheryHarvest" && t.Food > 0)
            && firstTurn.Record.Transfers.Any(t => t.Tile == shore && t.Entity == second.Id && t.Kind == "FisheryHarvest" && t.Food > 0),
            "a losing candidate retains shared-stock harvest rights");

        // Seed a prior operator to exercise incumbent retention even when a
        // lower camp ID is also eligible.
        site.OperatorCampId = second.Id;
        var retained = Resolve(world, new Dictionary<int, List<Order>>());
        site = world.FisherySites.Single(s => s.TileId == shore);
        Check(site.OperatorCampId == second.Id,
            "an eligible incumbent retains the unique management slot over a lower-ID challenger");
        Check(retained.Record.Transfers.Any(t => t.Tile == shore && t.Entity == first.Id && t.Kind == "FisheryHarvest" && t.Food > 0),
            "the management loser continues to harvest the common pool");

        second.IsVillage = true;
        int destination = world.Map.At(second.Tile % world.Config.Width, second.Tile / world.Config.Width + 1);
        var move = new Dictionary<int, List<Order>>
        {
            [second.Player] = new List<Order> { new Order(OrderKind.Move, second.Id, destination: destination) }
        };
        var departure = Resolve(world, move);
        site = world.FisherySites.Single(s => s.TileId == shore);
        Check(departure.Prepared.AbandoningVillages.Contains(second.Id) && second.Tile == destination,
            "accepted village movement is resolved as a departure");
        Check(site.OperatorCampId == first.Id,
            "departure releases the incumbent slot to the remaining eligible operator");
        Check(site.Capital > 0 && second.LastSelectedFishingGrounds.Count == 0,
            "departure preserves physical site capital and clears the former operator selection");
        Check(!second.IsVillage, "a departing village cannot requalify at its old endpoint");
    }

    private static void OverlayAndParallelDeterminism()
    {
        var world = FishingEconomyChecks.Fixture(24);
        var camp = world.Camps.Single();
        int shore = SharedShore(world, camp);
        var tile = Water(world, shore, stock: 100, biomass: 100);
        var view = new EcologyView(tile);
        double q = .5;
        double capital = Math.Pow(q / world.Config.ReferenceIntensity, 2);
        var overlay = new AquaticEcologyOverlay(capital, capital, capital, .75);
        var baseline = new EcologyCapacity(world, tile, view, 0);
        var managed = new EcologyCapacity(world, tile, view, 0, aquaticOverlay: overlay);
        Near(managed.Food, baseline.Food + (baseline.Biomass - baseline.Food) * q * q,
            "aquatic q-squared overlay expands accessible food only");
        Near(managed.Biomass, baseline.Biomass,
            "fishery accessibility leaves underlying biomass unchanged");
        Near(managed.ManagedFlow, world.Config.CultivationRegenFlux * q * baseline.Biomass
            * tile.Seasons[0].BiomassRecoveryPerDay * overlay.OperatingFraction,
            "managed flow uses the same explicit aquatic q and operating fraction");
        var endpoint = LazyEcology.EndpointAquaticOverlay(new AquaticEcologyOverlay(.1, .2, .3, .75)).Value;
        Near(endpoint.OpeningCapital, .3, "endpoint overlay uses ending capital as opening capacity");
        Near(endpoint.AverageCapital, .3, "endpoint overlay clamps with ending capital, not turn-average capital");
        Near(endpoint.EndingCapital, .3, "endpoint overlay retains committed ending capital");

        // The exact same legal actions and state must produce byte-identical
        // fingerprints under serial and reversed-partition resolution.
        var deterministic = FishingEconomyChecks.Fixture(24);
        deterministic.Config.GatherRadius = 1;
        deterministic.Config.Consumption = .1;
        var deterministicCamp = deterministic.Camps.Single();
        int deterministicShore = SharedShore(deterministic, deterministicCamp);
        Water(deterministic, deterministicShore, stock: 1000, biomass: 1000);
        EnableFishing(deterministic, deterministicCamp, 8);
        byte[] bytes = SaveCodec.Save(deterministic);
        var serial = SaveCodec.Load(bytes);
        var parallel = SaveCodec.Load(bytes);
        Resolve(serial, new Dictionary<int, List<Order>>(), new PhaseExecution(1));
        Resolve(parallel, new Dictionary<int, List<Order>>(), new PhaseExecution(4, 1, true, 1));
        Check(SaveCodec.Fingerprint(serial) == SaveCodec.Fingerprint(parallel),
            "serial and reversed-parallel resource/operator commits have identical fingerprints");
    }

    private static void AuthoritativeCapitalReconciliation()
    {
        var world = FishingEconomyChecks.Fixture(24);
        world.Config.Consumption = .1;
        var camp = world.Camps.Single();
        EnableFishing(world, camp, 4);
        int x = camp.Tile % world.Config.Width, y = camp.Tile / world.Config.Width;
        int first = world.Map.At(x + 1, y);
        int second = world.Map.At(x + 2, y);
        int target = world.Map.At(x + 3, y);
        Water(world, first, stock: 0, biomass: 0);
        Water(world, second, stock: 0, biomass: 0);
        Water(world, target, stock: 1000, biomass: 1000);
        camp.People.FishingGroundKnowledge.Add(new FishingGroundSnapshot
        {
            TileId = target, AccessibleFood = 1000, Biomass = 1000,
            Condition = 1, Capital = .1, OperatorCampId = -1,
            ObservedDays = world.Days, OperationFraction = 0,
            ProductiveProfile = new[] { 1.0, 1.0, 1.0, 1.0 }
        });
        world.FisherySites.Add(new FisherySite
        {
            TileId = target, OperatorCampId = -1, Capital = 2,
            LastUpdatedDays = world.Days
        });

        var prepared = TurnPlanner.Prepare(world, new Dictionary<int, List<Order>>());
        var ground = prepared.FishingPlans[camp.Id].Grounds.Single(g => g.TileId == target);
        Check(ground.KnowledgeSource == FishingKnowledgeSource.Snapshot,
            "the reconciliation fixture plans from a hidden stale snapshot");
        Near(ground.OpeningCapital, .1,
            "the immutable hidden plan retains its knowledge-safe capital estimate");
        Check(ground.CapitalWorkerYears > 0,
            "the stale-snapshot ground wins positive management work");
        var expected = FishingEconomy.AllocateCapital(world, 2,
            ground.CapitalWorkerYears, prepared.Horizon.Years);

        new TurnResolver().Resolve(world, prepared);
        var site = world.FisherySites.Single(s => s.TileId == target);
        Near(site.Capital, expected.EndingCapital,
            "the winning manager's work is rebased on authoritative physical capital");
        var refreshed = camp.People.FishingGroundKnowledge.Single(s => s.TileId == target);
        Near(refreshed.OperationFraction, expected.OperationFraction,
            "the refreshed snapshot records the reconciled operation fraction");
    }

    private static void VillageMealSupportUsesFullRequirement()
    {
        var world = FishingEconomyChecks.Fixture(24);
        world.Config.Consumption = 1;
        world.Config.GatherRadius = 1;
        var camp = world.Camps.Single();
        double fullRequirement = camp.Population * world.Config.Consumption * world.Config.GenerationIntervals;
        TurnResolver.FoodBucketBasis(world, camp, world.Config.GenerationIntervals,
            world.Config.TurnDays / world.Config.DaysPerYear, fullRequirement,
            out double reserveTarget, out _);
        camp.PlantFood = reserveTarget + 20;
        int shore = SharedShore(world, camp);
        Water(world, shore, stock: 1000, biomass: 1000);
        EnableFishing(world, camp, 1);

        var resolved = Resolve(world, new Dictionary<int, List<Order>>());
        var needs = resolved.Prepared.Needs[camp.Id];
        double managedCatch = resolved.Record.Transfers.Where(t => t.Entity == camp.Id &&
            t.Kind == "FisheryHarvest").Sum(t => t.Food);
        var report = resolved.Record.Demographics.Single(d => d.Camp == camp.Id);
        Check(needs.ConsumptionTarget < needs.ConsumptionRequired,
            "opening food makes the income target smaller than the full meal requirement");
        Near(report.FishingVillageMealSupport,
            Numbers.Clamp(managedCatch / needs.ConsumptionRequired, 0, 1),
            "Fishing village meal support uses the full locked consumption requirement");
    }

    private static FishingGroundSnapshot StaleSnapshot(int tile) => new FishingGroundSnapshot
    {
        TileId = tile, AccessibleFood = 1, Biomass = 2, Condition = .5,
        Capital = 0, OperatorCampId = -1, ObservedDays = 0,
        ProductiveProfile = new[] { .1, .1, .1, .1 }
    };

    private static void SnapshotPruningAndWorkedDepartureRefresh()
    {
        var world = FishingEconomyChecks.Fixture(24);
        var camp = world.Camps.Single();
        int shore = SharedShore(world, camp);
        var tile = Water(world, shore, stock: 90, biomass: 100);
        EnableFishing(world, camp, 4);
        world.Days = 12;
        tile.LastEcologyUpdateDays = world.Days;
        world.Societies.Single(s => s.Id == camp.SocietyId).FishingGroundKnowledge.Add(StaleSnapshot(shore));

        camp.IsVillage = true;
        int destination = world.Map.Neighbors(camp.Tile).Where(id => id != shore && world.Tiles[id].Land)
            .OrderBy(id => id).First();
        var departureOrders = new Dictionary<int, List<Order>>
        {
            [camp.Player] = new List<Order> { new Order(OrderKind.Move, camp.Id, destination: destination) }
        };
        var prepared = TurnPlanner.Prepare(world, departureOrders);
        Check(prepared.FishingPlans.TryGetValue(camp.Id, out var departingPlan) && departingPlan.Departing,
            "snapshot fixture carries a real accepted departing Fishing plan");
        Check(departingPlan.Grounds.Any(g => g.TileId == shore && g.RequestedCatch > 0),
            "the departing plan retains a harvest request for the previously worked ground");

        var absent = new Society
        {
            Id = 200, Player = 1, Memory = new ContactMemory(world.Config.Width),
            FishingGroundKnowledge = new List<FishingGroundSnapshot> { StaleSnapshot(shore) }
        };
        var missingExplored = new Society
        {
            Id = 300, Player = 2, Memory = new ContactMemory(world.Config.Width) { Explored = null },
            FishingGroundKnowledge = new List<FishingGroundSnapshot> { StaleSnapshot(shore) }
        };
        world.Societies.Add(absent);
        world.Societies.Add(missingExplored);
        var record = new TurnRecord
        {
            Turn = world.Turn, StartDays = world.Days, Duration = 1,
            Transfers = new List<Transfer>
            {
                new Transfer { Tile = shore, Entity = camp.Id, Kind = "FisheryHarvest", Food = 4 }
            }
        };
        TurnResolver.UpdateFishingGroundKnowledge(world, prepared.FishingPlans, record);
        var refreshed = world.Societies.Single(s => s.Id == camp.SocietyId).FishingGroundKnowledge
            .Single(s => s.TileId == shore);
        Near(refreshed.ObservedDays, world.Days,
            "a worked ground refreshes owner knowledge from a committed receipt even for a departing plan");
        Near(refreshed.AccessibleFood, tile.Food,
            "worked-ground refresh uses the committed aquatic endpoint");
        Check(absent.FishingGroundKnowledge.Count == 0,
            "societies absent from observations still prune snapshots outside explored memory");
        Check(missingExplored.FishingGroundKnowledge.Count == 0,
            "a society with missing explored memory retains no fishing snapshots");
    }

    private static void DormantSitesRemainLazyAtScale()
    {
        var world = FishingEconomyChecks.Fixture(24);
        world.Config.Consumption = .1;
        var camp = world.Camps.Single();
        int active = SharedShore(world, camp);
        Water(world, active, stock: 1000, biomass: 1000);
        EnableFishing(world, camp, 4);

        var dormantTiles = Enumerable.Range(0, world.Tiles.Length)
            .Where(tile => tile != active && world.Map.Distance(camp.Tile, tile) > 4)
            .OrderBy(tile => tile).Take(128).ToArray();
        foreach (int tile in dormantTiles)
        {
            Water(world, tile, stock: 100, biomass: 100);
            world.FisherySites.Add(new FisherySite
            {
                TileId = tile, OperatorCampId = -1, Capital = 1,
                LastUpdatedDays = world.Days
            });
        }
        world.FisherySites = world.FisherySites.OrderBy(site => site.TileId).ToList();

        var resolved = Resolve(world, new Dictionary<int, List<Order>>());
        Check(dormantTiles.Length == 128 && resolved.Resolver.LastMetrics.FisherySiteLookups < 64,
            "resource resolution uses bounded point lookups rather than scanning dormant fishery sites");
        Check(dormantTiles.All(tile =>
        {
            var site = world.FisherySites.Single(candidate => candidate.TileId == tile);
            return site.Capital == 1 && site.LastUpdatedDays == 0 && site.OperatorCampId == -1;
        }), "dormant fishery capital remains lazy and untouched");
        Console.WriteLine("FISHING_SPARSE_SCALE dormantSites=" + dormantTiles.Length +
            " pointLookups=" + resolved.Resolver.LastMetrics.FisherySiteLookups +
            " activeIntegrations=" + resolved.Resolver.LastMetrics.TileIntegrations);
    }

    internal static void Run()
    {
        checks = 0;
        SharedPoolReceiptsAndTools();
        OperatorTieRetentionAndDeparture();
        OverlayAndParallelDeterminism();
        AuthoritativeCapitalReconciliation();
        VillageMealSupportUsesFullRequirement();
        SnapshotPruningAndWorkedDepartureRefresh();
        DormantSitesRemainLazyAtScale();
        Console.WriteLine("PASS: " + checks + " Fishing resource assertions");
    }
}
