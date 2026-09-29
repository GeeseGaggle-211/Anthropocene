using System;
using System.Collections.Generic;
using System.Linq;
using Anthropocene.Simulation;

internal static class HorizonCultivationChecks
{
    private static int checks;
    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception("Horizon cultivation: " + message);
    }

    private static void Near(double actual, double expected, string message, double tolerance = 1e-8)
        => Check(Math.Abs(actual - expected) <= tolerance,
            message + " actual=" + actual + " expected=" + expected);

    private static WorldState Fixture(double startingCapital = 0, double farmTools = 0)
    {
        var world = NeolithicChecks.Fixture();
        var camp = world.Camps[0];
        camp.CultivationFocusTile = camp.Tile;
        camp.Workforce.Assignments.Clear();
        camp.Workforce.Assign(WorkforceRoles.Farmer, 10, camp.Population);
        camp.EconomicLabor = new EconomicLaborPlan { ExpansionFarmers = 8 };
        camp.Inventory.FarmTools = farmTools;
        if (startingCapital > 0)
        {
            var tile = world.Tiles[camp.Tile];
            tile.CultivationCapital = startingCapital;
            tile.Cultivation = (float)CultivationEconomy.Intensity(world.Config, startingCapital);
            tile.Cultivated = true; tile.CultivationOperator = camp.Id;
        }
        return world;
    }

    private static void PurePlanAndLocalBounds()
    {
        var world = Fixture(0, 2);
        var camp = world.Camps[0];
        var budget = new FarmToolWorkBudget(world, camp);
        var labor = CultivationEconomy.PrepareHorizon(world, camp, 0, 20, budget);
        string before = SaveCodec.Fingerprint(world);
        var plan = CultivationEconomy.PlanHorizonExpansion(world, camp, 20,
            labor.ExpansionFarmers * 20, 20, budget, labor.Footprint);
        Check(SaveCodec.Fingerprint(world) == before, "planning leaves authoritative world unchanged");
        Check(plan.Tiles.Length > 0 && plan.Tiles.Length <= 7, "at most the radius-one field footprint is planned");
        Check(plan.Tiles.SequenceEqual(plan.Tiles.OrderBy(id => id)), "field result slots use tile order");
        Check(plan.Tiles.All(id => world.Tiles[id].TraversableLand && world.Map.Distance(camp.Tile, id) <= 1),
            "no field escapes the radius-one land footprint");
        Check(plan.NewFieldTiles().All(id => plan.Tiles.Contains(id)), "new-field reservations are a subset of the plan");
        double ceiling = 1 / (world.Config.ReferenceIntensity * world.Config.ReferenceIntensity);
        for (int i = 0; i < plan.Tiles.Length; i++)
        {
            Check(plan.StartCapital[i] >= 0 && plan.AverageCapital[i] >= 0 &&
                plan.EndCapital[i] >= 0 && plan.EndCapital[i] <= ceiling + 1e-8,
                "planned field capital stays physical");
            Check(plan.TryGetTile(plan.Tiles[i], out double mean, out double end) &&
                Math.Abs(mean - plan.AverageCapital[i]) < 1e-10 &&
                Math.Abs(end - plan.EndCapital[i]) < 1e-10, "tile lookup matches indexed plan");
        }
        Check(plan.BridgeWorkerYearsUsed <= 20 + 1e-8 &&
            plan.FarmerWorkerYearsUsed <= labor.ExpansionFarmers * 20 + 1e-8,
            "physical work never exceeds the turn-level labor budget");
    }

    private static void SameWorkIsIndependentOfSeasonCount()
    {
        var shortWorld = Fixture();
        var longWorld = Fixture();
        var shortCamp = shortWorld.Camps[0]; var longCamp = longWorld.Camps[0];
        var shortBudget = new FarmToolWorkBudget(shortWorld, shortCamp);
        var longBudget = new FarmToolWorkBudget(longWorld, longCamp);
        CultivationEconomy.PrepareHorizon(shortWorld, shortCamp, 0, 1, shortBudget);
        CultivationEconomy.PrepareHorizon(longWorld, longCamp, 0, 20, longBudget);
        // Eight worker-years fit the one-year workforce as well as the longer
        // turn; the same physical effort should not depend on season count.
        var shortPlan = CultivationEconomy.PlanHorizonExpansion(shortWorld, shortCamp, 1, 8, 0, shortBudget);
        var longPlan = CultivationEconomy.PlanHorizonExpansion(longWorld, longCamp, 20, 8, 0, longBudget);
        Check(shortPlan.Tiles.SequenceEqual(longPlan.Tiles), "same fields are considered at one and twenty years");
        for (int i = 0; i < shortPlan.Tiles.Length; i++)
            Near(shortPlan.EndCapital[i], longPlan.EndCapital[i],
                "equal worker-years build equal new fields regardless of seasonal count", 1e-8);
    }

    private static void FarmToolCoverageAndWorkConservation()
    {
        double[] startingTools = { 0, 2, 8 };
        double[] builtCapital = new double[startingTools.Length];
        for (int scenario = 0; scenario < startingTools.Length; scenario++)
        {
            var world = Fixture(0, startingTools[scenario]);
            var camp = world.Camps[0];
            var budget = new FarmToolWorkBudget(world, camp);
            var labor = CultivationEconomy.PrepareHorizon(world, camp, 0, 20, budget);
            double requested = labor.ExpansionFarmers * 20;
            var plan = CultivationEconomy.PlanHorizonExpansion(world, camp, 20,
                requested, 0, budget, labor.Footprint);
            double covered = Math.Min(requested,
                startingTools[scenario] * world.Config.Neolithic.FarmToolFarmerYearsPerUnit);
            Near(plan.CoveredFarmerWorkerYears, covered,
                "zero, partial, and full starting Farm Tool stocks cover only available farmer-years");
            Near(plan.FarmerWorkerYearsUsed, requested,
                "all requested farmer-years reach initially empty fields");
            Near(camp.Inventory.FarmTools, startingTools[scenario],
                "the field plan does not debit Farm Tools before endpoint commit");
            double effectiveWork = plan.FarmerWorkerYearsUsed +
                (CultivationEconomy.FarmToolFieldWorkEfficiency(world.Config.Neolithic) - 1) * covered;
            builtCapital[scenario] = plan.EndCapital.Sum();
            Near(builtCapital[scenario] * world.Config.Neolithic.BuildWorkerYearsPerTileE,
                effectiveWork, "field investment conserves uncovered and covered work", 1e-7);

            CultivationEconomy.CommitHorizonExpansion(world, camp, plan, new TurnRecord(), budget);
            double spent = covered / world.Config.Neolithic.FarmToolFarmerYearsPerUnit;
            Near(startingTools[scenario] - camp.Inventory.FarmTools, spent,
                "endpoint tool debit equals realized covered farmer-years");
            Check(camp.Inventory.FarmTools >= -1e-10 && budget.RemainingTools >= -1e-10,
                "field service cannot make held Farm Tools negative");
            Near(plan.Tiles.Sum(id => world.Tiles[id].CultivationCapital), builtCapital[scenario],
                "committed field capital equals the planned endpoint", 1e-7);
        }
        Check(builtCapital[0] < builtCapital[1] && builtCapital[1] < builtCapital[2],
            "partial Farm Tool service produces capital between zero and full coverage");
    }

    private static void ZeroDurationDoesNoFieldWork()
    {
        var world = Fixture(.1, 1);
        var camp = world.Camps[0];
        double before = world.Tiles[camp.Tile].CultivationCapital;
        var budget = new FarmToolWorkBudget(world, camp);
        var labor = CultivationEconomy.PrepareHorizon(world, camp, 0, 0, budget);
        var plan = CultivationEconomy.PlanHorizonExpansion(world, camp, 0, 100, 100, budget, labor.Footprint);
        Near(plan.FarmerWorkerYearsUsed, 0, "zero-duration horizon cannot spend farmer work");
        Near(plan.BridgeWorkerYearsUsed, 0, "zero-duration horizon cannot spend bridge work");
        Near(plan.EndCapital[Array.IndexOf(plan.Tiles, camp.Tile)], before,
            "zero-duration horizon does not change capital");
        Near(budget.ReservedMaintenanceWorkerYears, 0, "zero-duration maintenance does not use farm tools");
    }

    private static void BridgeReconciliationAndOneToolDebit()
    {
        var world = Fixture(.1, 2);
        var camp = world.Camps[0];
        var budget = new FarmToolWorkBudget(world, camp);
        var labor = CultivationEconomy.PrepareHorizon(world, camp, 0, 20, budget);
        Near(camp.Inventory.FarmTools, 2, "preparation only reserves farm-tool service");
        var estimated = CultivationEconomy.PlanHorizonExpansion(world, camp, 20,
            labor.ExpansionFarmers * 20, 80, budget, labor.Footprint);
        var realized = CultivationEconomy.ReconcileHorizonBridge(world, camp, estimated, 0, budget);
        Check(realized.BridgeWorkerYearsUsed == 0, "zero realized gathering removes bridge investment");
        Check(realized.NewFieldTiles().All(id => estimated.NewFieldTiles().Contains(id)),
            "reconciliation cannot reserve a new competing field");
        Near(camp.Inventory.FarmTools, 2, "reconciliation remains pure");
        var record = new TurnRecord();
        foreach (int id in estimated.Tiles)
        {
            estimated.TryGetTile(id, out double average, out double endpoint);
            var integrated = EcologicalPressure.Integrate(world, id, Array.Empty<Demand>(), 0,
                20 * world.Config.DaysPerYear, average, endpoint, world.Tiles[id].CultivationOperatingFraction);
            integrated.View.Commit(world.Tiles[id]);
            record.EcologyBalances.Add(integrated.Balance);
        }
        CultivationEconomy.CommitHorizonExpansion(world, camp, realized, record, budget, estimated);
        Near(2 - camp.Inventory.FarmTools,
            (budget.ReservedMaintenanceWorkerYears + realized.CoveredFarmerWorkerYears) /
            world.Config.Neolithic.FarmToolFarmerYearsPerUnit, "maintenance and expansion debit tools once", 1e-8);
        Check(world.Tiles.All(t => t.CultivationCapital >= -1e-8 && t.Food >= -1e-8 &&
            t.PotentialFood >= t.Food - 1e-8), "reconciled field remains nonnegative and food is bounded");
        bool rejected = false;
        try { CultivationEconomy.CommitHorizonExpansion(world, camp, realized, record, budget, estimated); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "a second endpoint commit cannot consume farm tools twice");
    }

    private static void SoftEffortChargesBuildAndMaintenance()
    {
        var world = Fixture(Math.Pow(.6 / .4, 2));
        var config = world.Config;
        config.Neolithic.CultivationMarginalEffortMultiplierPerTenth = 16;
        double capital = world.Tiles[world.Camps[0].Tile].CultivationCapital;
        Near(CultivationEconomy.EffortForCapital(config, 1), 1,
            "the soft effort curve joins the historical work at qt");
        Check(CultivationEconomy.EffortForCapital(config, capital) / capital >= 20,
            "q=.6 requires more than twenty times the quadratic effort");
        foreach (double q in new[] { .1, .4, .5, .6, .8, 1.0 })
        {
            double e = Math.Pow(q / config.ReferenceIntensity, 2);
            Near(CultivationEconomy.CapitalForEffort(config,
                CultivationEconomy.EffortForCapital(config, e)), e,
                "effort and physical capital convert in both directions", 1e-7);
        }
        var camp = world.Camps[0];
        CultivationEconomy.Factors(config, world.Tiles[camp.Tile], out _, out double vegetationMaintenance);
        double baseMaintenance = config.Neolithic.MaintenanceWorkersPerTileE(config.MaintenanceCoefficient) * vegetationMaintenance;
        Near(CultivationEconomy.MaintenanceWorkerRequirement(world, camp),
            baseMaintenance * CultivationEconomy.EffortForCapital(config, capital),
            "field upkeep follows the same soft effort curve as construction", 1e-6);

        var building = Fixture();
        building.Config.Neolithic.CultivationMarginalEffortMultiplierPerTenth = 16;
        building.Config.Neolithic.OpenBuild = 1;
        building.Config.Neolithic.WoodlandBuild = 1;
        building.Config.Neolithic.ForestBuild = 1;
        var builder = building.Camps[0];
        var plan = CultivationEconomy.PlanHorizonExpansion(building, builder, 20,
            100000, 0, null);
        double chargedEffort = plan.EndCapital.Sum(e =>
            CultivationEconomy.EffortForCapital(building.Config, e));
        Near(plan.FarmerWorkerYearsUsed,
            chargedEffort * building.Config.Neolithic.BuildWorkerYearsPerTileE,
            "bounded horizon allocation debits nonlinear build effort exactly", 1e-5);
    }

    private static void VillageCheckpointUsesOwnedFootprint()
    {
        var world = Fixture();
        var camp = world.Camps[0];
        var land = world.Map.Radius(camp.Tile, 1).Where(id => world.Tiles[id].TraversableLand).ToArray();
        Check(land.Length == 7, "village checkpoint includes the entire radius-one land footprint");
        double Capital(double q) => Math.Pow(q / world.Config.ReferenceIntensity, 2);
        foreach (int id in land)
        {
            world.Tiles[id].CultivationCapital = Capital(.24);
            world.Tiles[id].CultivationOperator = camp.Id;
        }
        var record = new TurnRecord();
        CultivationEconomy.UpdateVillageAtBoundary(world, camp, record);
        Check(!camp.IsVillage && camp.VillageTurns == 0, "average q below .25 remains a camp");
        foreach (int id in land) world.Tiles[id].CultivationCapital = Capital(.25);
        world.Tiles[land[0]].CultivationOperator = camp.Id + 100;
        CultivationEconomy.UpdateVillageAtBoundary(world, camp, record);
        Check(!camp.IsVillage, "a neighboring camp's field cannot promote this camp");
        world.Tiles[land[0]].CultivationOperator = camp.Id;
        CultivationEconomy.UpdateVillageAtBoundary(world, camp, record);
        Check(camp.IsVillage && camp.VillageTurns == 1, "average q at .25 promotes exactly once");
        Check(record.Events.Any(e => e.Contains("transitioned into a Village")), "village transition is logged");
        var loaded = SaveCodec.Load(SaveCodec.Save(world));
        Check(loaded.Camps[0].IsVillage && loaded.Camps[0].VillageTurns == 1,
            "village status and turn count survive save continuation");
        foreach (int id in land) world.Tiles[id].CultivationCapital = 0;
        CultivationEconomy.UpdateVillageAtBoundary(world, camp, record);
        Check(camp.IsVillage && camp.VillageTurns == 2,
            "field decay alone does not decide to abandon village status");

        var singleField = Fixture();
        var singleCamp = singleField.Camps[0];
        singleField.Tiles[singleCamp.Tile].CultivationCapital = Capital(1);
        singleField.Tiles[singleCamp.Tile].CultivationOperator = singleCamp.Id;
        CultivationEconomy.UpdateVillageAtBoundary(singleField, singleCamp, new TurnRecord());
        Check(!singleCamp.IsVillage, "one intense field does not replace the footprint-average gate");

        var resolved = Fixture();
        var resident = resolved.Camps[0];
        resolved.Config.Neolithic.DecayHalfLifeYears = 1000000000;
        foreach (int id in resolved.Map.Radius(resident.Tile, 1))
        {
            resolved.Tiles[id].CultivationCapital = Capital(.35);
            resolved.Tiles[id].CultivationOperator = resident.Id;
        }
        var turn = new TurnResolver().Resolve(resolved, new Dictionary<int, List<Order>>());
        Check(resident.IsVillage && resident.VillageTurns == 1,
            "resource endpoint promotes an established agricultural camp");
        Check(turn.Events.Count(e => e.Contains("transitioned into a Village")) == 1,
            "resource endpoint records one promotion event");
        var move = new Order(OrderKind.Move, resident.Id,
            destination: resolved.Map.Neighbors(resident.Tile).First());
        Check(OrderRules.Validate(new AgentObservationBuilder().Build(resolved, resident.Player), move) == null,
            "the player may order a village to abandon its site");
    }

    private static void VillageDepartureLosesFieldsAndHousing()
    {
        var staying = Fixture();
        var home = staying.Camps[0];
        int destination = staying.Map.Neighbors(home.Tile).First();
        var oldLand = staying.Map.Radius(home.Tile, 1).ToArray();
        var newLand = new HashSet<int>(staying.Map.Radius(destination, 1));
        double capital = Math.Pow(.35 / staying.Config.ReferenceIntensity, 2);
        foreach (int id in oldLand)
        {
            var tile = staying.Tiles[id];
            tile.CultivationCapital = capital;
            tile.CultivationOperator = home.Id;
            tile.Cultivation = .35f;
            if (newLand.Contains(id)) continue;
            tile.Food = tile.PotentialFood = 10000;
            for (int season = 0; season < tile.Seasons.Length; season++)
            {
                var profile = tile.Seasons[season];
                profile.MaxFood = 10000;
                profile.MaxPotentialFood = 10000;
                tile.Seasons[season] = profile;
            }
        }
        home.IsVillage = true;
        home.VillageTurns = 3;
        home.StationaryYears = 100;
        home.RudimentaryExpectation = 1;
        home.PlantFood = 0;
        home.Inventory.Cloth = 100;
        NeolithicProduction.AddFixedHousing(staying, home, 25, staying.Days);
        byte[] initial = SaveCodec.Save(staying);
        var leaving = SaveCodec.Load(initial);
        var parallelLeaving = SaveCodec.Load(initial);
        var migrant = leaving.Camps[0];
        var stayTurn = new TurnResolver().Resolve(staying, new Dictionary<int, List<Order>>());
        var leaveTurn = new TurnResolver().Resolve(leaving,
            new Dictionary<int, List<Order>> { [migrant.Player] = new List<Order>
                { new Order(OrderKind.Move, migrant.Id, destination: destination) } });
        Check(migrant.Tile == destination && !migrant.IsVillage && migrant.VillageTurns == 0,
            "accepted village movement immediately regresses to a camp");
        Check(leaveTurn.Events.Any(e => e.Contains("abandoned its fields")),
            "the abandonment decision is recorded");
        Check(oldLand.All(id => leaving.Tiles[id].CultivationOperator != migrant.Id),
            "abandoned fields are unavailable to the moving camp this turn");
        Check(!leaveTurn.Transfers.Any(t => t.Entity == migrant.Id && t.Kind == "Cultivation"),
            "no cultivated harvest is awarded after abandonment");
        Check(migrant.StationaryYears >= 100,
            "settled domestic expectations persist despite relocation");
        Check(NeolithicProduction.FixedHousingAt(leaving, migrant.Id, destination, leaving.Days) == 0,
            "fixed village housing stays at the former site");
        Check(migrant.FoodNeedsMetRatio < home.FoodNeedsMetRatio,
            "abandonment loses local food security under the same cohort demand");
        Check(migrant.QoL < home.QoL,
            "the unchanged QoL formula registers the lost housing and food");
        Check(migrant.QoL < home.QoL * .25,
            "losing the village's food and fixed housing causes a sharp immediate QoL collapse");
        double expectation = migrant.RudimentaryExpectation;
        double clothing = 1 - expectation * (1 - migrant.ClothPerCapita);
        double housing = 1 - expectation * (1 - migrant.HousingPerCapita);
        double expectedQoL = migrant.FoodNeedsMetRatio * migrant.WaterNeedsMetRatio *
            (leaving.Config.Neolithic.RudimentaryWeightClothing * clothing +
             leaving.Config.Neolithic.RudimentaryWeightHousing * housing);
        Near(migrant.QoL, expectedQoL, "village departure uses the ordinary QoL formula");
        Console.WriteLine("VILLAGE_ABANDONMENT stayFood=" + home.FoodNeedsMetRatio +
            " moveFood=" + migrant.FoodNeedsMetRatio + " stayQoL=" + home.QoL +
            " moveQoL=" + migrant.QoL + " moveHousing=" + migrant.HousingPerCapita);
        var parallelCamp = parallelLeaving.Camps[0];
        new TurnResolver(new PhaseExecution(4, 3, true, 1)).Resolve(parallelLeaving,
            new Dictionary<int, List<Order>> { [parallelCamp.Player] = new List<Order>
                { new Order(OrderKind.Move, parallelCamp.Id, destination: destination) } });
        Check(SaveCodec.Fingerprint(leaving) == SaveCodec.Fingerprint(parallelLeaving),
            "village abandonment is exact across scalar and four-worker resolution");
        var continuation = SaveCodec.Load(SaveCodec.Save(leaving));
        Check(SaveCodec.Fingerprint(leaving) == SaveCodec.Fingerprint(continuation),
            "abandoned village state survives save continuation exactly");
        new TurnResolver().Resolve(leaving, new Dictionary<int, List<Order>>());
        new TurnResolver(new PhaseExecution(4, 3, true, 1)).Resolve(continuation,
            new Dictionary<int, List<Order>>());
        Check(SaveCodec.Fingerprint(leaving) == SaveCodec.Fingerprint(continuation),
            "abandoned village continuation is exact across worker counts");
        Check(stayTurn.Events.All(e => !e.Contains("abandoned its fields")),
            "staying villagers retain their settlement status");
    }

    // Regression: adjacent agricultural camps share radius-one footprint tiles.
    // The higher-id camp expands onto a shared tile; the lower-id camp planned no
    // change there and must not compare that tile with its own preclaim endpoint.
    private static WorldState SharedFootprintFixture(out Camp idle, out Camp expander, out int shared)
    {
        var world = Fixture();
        idle = world.Camps[0];
        int idleTile = idle.Tile; // lambdas cannot capture the out parameter
        int otherTile = world.Map.Neighbors(idle.Tile).First(id => world.Tiles[id].TraversableLand);
        expander = new Camp { Id = world.NextId++, Player = idle.Player, Tile = otherTile,
            Population = idle.Population, PlantFood = idle.PlantFood };
        world.Camps.Add(expander);
        Ownership.Bind(world);
        expander.Technology = Technology.Cultivation;
        expander.Technologies[(int)Technology.Cultivation] = true;
        shared = world.Map.Neighbors(idle.Tile).Intersect(world.Map.Neighbors(otherTile))
            .Where(id => id != idleTile && id != otherTile && world.Tiles[id].TraversableLand).OrderBy(id => id).First();
        idle.Workforce.Assignments.Clear();
        idle.Workforce.Assign(WorkforceRoles.Farmer, 10, idle.Population);
        idle.EconomicLabor = new EconomicLaborPlan { ExpansionFarmers = 0 };
        idle.CultivationFocusTile = idle.Tile;
        expander.Workforce.Assignments.Clear();
        expander.Workforce.Assign(WorkforceRoles.Farmer, 10, expander.Population);
        expander.EconomicLabor = new EconomicLaborPlan { ExpansionFarmers = 8 };
        expander.CultivationFocusTile = shared;
        expander.Inventory.FarmTools = 0;
        return world;
    }

    private static void AdjacentCampsShareFieldTile()
    {
        var world = SharedFootprintFixture(out var idle, out var expander, out int shared);
        Check(world.Map.Distance(idle.Tile, expander.Tile) == 1, "fixture camps are adjacent");
        Check(idle.Id < expander.Id, "the non-expanding camp commits first");
        Check(world.Map.Radius(idle.Tile, 1).Contains(shared) && world.Map.Radius(expander.Tile, 1).Contains(shared),
            "the tile belongs to both radius-one footprints");
        Check(world.Tiles[shared].CultivationOperator < 0 && world.Tiles[shared].CultivationCapital == 0,
            "shared tile starts unclaimed");
        byte[] initial = SaveCodec.Save(world);
        var parallel = SaveCodec.Load(initial);
        var orders = new Dictionary<int, List<Order>>();
        new TurnResolver().Resolve(world, orders);
        var tile = world.Tiles[shared];
        Check(tile.CultivationOperator == expander.Id, "the expanding camp operates the shared tile");
        Check(tile.CultivationCapital > 0 && tile.Cultivated, "the expanding camp built capital on the shared tile");
        Check(world.Tiles.All(t => t.CultivationOperator != idle.Id || t.Id == idle.Tile),
            "the idle camp operates no tile beyond its own site, in particular not the shared tile");
        Check(world.Camps.Count(c => c.Id != expander.Id && world.Map.Radius(c.Tile, 1).Contains(shared)) == 1,
            "the idle camp's footprint includes the shared tile without writing it");
        Check(world.Tiles.All(t => t.CultivationCapital >= -1e-8 && t.Food >= -1e-8 && t.PotentialFood >= t.Food - 1e-8),
            "shared-footprint turn keeps tile stocks nonnegative and bounded");
        var pa = parallel;
        pa.Camps.Reverse(); pa.Hunters.Reverse(); pa.Herds.Reverse();
        new TurnResolver(new PhaseExecution(4, 3, true, 1)).Resolve(pa, new Dictionary<int, List<Order>>());
        Check(SaveCodec.Fingerprint(world) == SaveCodec.Fingerprint(pa),
            "shared-footprint expansion is exact across scalar and reversed four-worker resolution");
        Check(pa.Tiles[shared].CultivationOperator == expander.Id, "parallel result keeps the same operator");
        new TurnResolver().Resolve(world, new Dictionary<int, List<Order>>());
        Check(world.Tiles[shared].CultivationOperator == expander.Id, "the field continues on a second turn");
    }

    public static int Run()
    {
        checks = 0;
        PurePlanAndLocalBounds();
        SameWorkIsIndependentOfSeasonCount();
        FarmToolCoverageAndWorkConservation();
        ZeroDurationDoesNoFieldWork();
        BridgeReconciliationAndOneToolDebit();
        SoftEffortChargesBuildAndMaintenance();
        VillageCheckpointUsesOwnedFootprint();
        VillageDepartureLosesFieldsAndHousing();
        AdjacentCampsShareFieldTile();
        Console.WriteLine("PASS: " + checks + " horizon cultivation assertions");
        return checks;
    }
}
