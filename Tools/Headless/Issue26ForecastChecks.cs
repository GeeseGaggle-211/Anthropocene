using System;
using System.Collections.Generic;
using System.Linq;
using Anthropocene.Simulation;

internal static class Issue26ForecastChecks
{
    private static int checks;
    private static void Check(bool ok, string message)
    { checks++; if (!ok) throw new Exception("Issue 26: " + message); }
    private static void Near(double actual, double expected, string message)
        => Check(Math.Abs(actual - expected) <= 1e-8 * Math.Max(1, Math.Max(Math.Abs(actual), Math.Abs(expected))),
            message + " actual=" + actual + " expected=" + expected);

    private static WorldState Land(double food, double biomass, float maxFood,
        float maxBiomass, float rate, double q = 0, double operation = 1)
    {
        var w = NeolithicChecks.Fixture();
        var tile = w.Tiles[w.Camps[0].Tile];
        tile.Food = food; tile.PotentialFood = biomass;
        tile.CultivationCapital = Math.Pow(q / w.Config.ReferenceIntensity, 2);
        tile.CultivationOperatingFraction = operation;
        tile.CultivationOperator = w.Camps[0].Id;
        for (int s = 0; s < 4; s++)
        {
            tile.Seasons[s].MaxFood = maxFood;
            tile.Seasons[s].MaxPotentialFood = maxBiomass;
            tile.Seasons[s].FoodRecoveryPerDay = rate;
            tile.Seasons[s].BiomassRecoveryPerDay = rate;
        }
        return w;
    }

    private static void KernelParityAndPurity()
    {
        foreach (double q in new[] { 0.0, .25, .8 })
        foreach (double operation in new[] { 0.0, .5, 1.0 })
        foreach (double duration in new[] { 0.0, 365.0, 7300.0 })
        {
            var w = Land(7, 45, 30, 100, .01f, q, operation);
            int id = w.Camps[0].Tile;
            var claims = new[]
            {
                new Demand { Tile = id, Entity = 1, Kind = "Gathering", Food = 20 },
                new Demand { Tile = id, Entity = 2, Kind = "Herd", PotentialFood = 16 }
            };
            string before = SaveCodec.Fingerprint(w);
            Check(w.MaterialIndex == null, "cold fixture has no material index");
            var forecast = FoodHorizonForecast.Tile(w, id, w.Days, duration, claims,
                null, null, operation, true);
            Check(w.MaterialIndex == null && TurnDiagnostics.For(w).BodyScans == 0 &&
                TurnDiagnostics.For(w).PlanCalls == 0,
                "food-only query does not discover sources or invoke planner");
            var actual = EcologicalPressure.Integrate(w, id, claims, w.Days,
                duration, null, null, operation, captureGrossForcing: true);
            Near(forecast.OpeningFood, actual.Balance.StartingFood, "opening Food");
            Near(forecast.OpeningBiomass, actual.Balance.StartingBiomass, "opening biomass");
            Near(forecast.HumanFoodReceipts, actual.Transfers.Sum(t => t.Food), "human receipt");
            Near(forecast.HerdBiomassReceipts, actual.Transfers.Sum(t => t.PotentialFood), "herd receipt");
            Near(forecast.ClosingFood, actual.View.Food, "closing Food");
            Near(forecast.ClosingBiomass, actual.View.PotentialFood, "closing biomass");
            Near(forecast.GrossNaturalFoodInput, actual.GrossNaturalFoodInput, "gross natural input");
            Near(forecast.GrossManagedFoodInput, actual.ManagedRenewalInput, "gross managed input");
            Check(before == SaveCodec.Fingerprint(w), "forecast and integrator are pure");
            var again = FoodHorizonForecast.Tile(w, id, w.Days, duration, claims,
                null, null, operation, true);
            Near(again.ClosingFood, forecast.ClosingFood, "repeat query result");
        }
        var seasonal = Land(7, 45, 30, 100, .01f);
        int seasonalId = seasonal.Camps[0].Tile;
        for (int s = 0; s < 4; s++)
        {
            seasonal.Tiles[seasonalId].Seasons[s].MaxFood = 10 + s * 9;
            seasonal.Tiles[seasonalId].Seasons[s].FoodRecoveryPerDay = .002f * (s + 1);
        }
        var opening = new EcologyView(seasonal.Tiles[seasonalId]);
        double orderedGross = 0;
        for (int s = 0; s < 4; s++)
        {
            var capacity = new EcologyCapacity(seasonal,
                seasonal.Tiles[seasonalId], opening, s, 1);
            orderedGross += capacity.Food * capacity.FoodRate * seasonal.Config.BaseDays;
        }
        var seasonalForecast = FoodHorizonForecast.Tile(seasonal, seasonalId,
            0, 4 * seasonal.Config.BaseDays, allCompetitionKnown: true);
        Near(seasonalForecast.GrossNaturalFoodInput, orderedGross,
            "gross natural forcing follows ordered seasonal profiles");
    }

    private static void ClippedInvestmentAndTiming()
    {
        var full = Land(100, 100, 100, 100, .02f);
        full.Config.CultivationRegenFlux = 100;
        int id = full.Camps[0].Tile;
        double e = Math.Pow(.1 / full.Config.ReferenceIntensity, 2);
        var clipped = FoodHorizonForecast.Investment(full, id, 0, 7300,
            0, e, e, 1, 10, 20);
        Check(clipped.After.GrossManagedFoodInput > clipped.Before.GrossManagedFoodInput,
            "managed gross flow is separately reported");
        Check(clipped.After.FoodAtCapacity &&
            clipped.After.GrossFoodInputLessNetRecovery > 0,
            "saturated stock reports a positive gross-versus-net gap");
        var fullTile = full.Tiles[id];
        var beforeView = new EcologyView(fullTile);
        var afterView = beforeView; afterView.CultivationCapital = e;
        double LegacyFlow(EcologyView view)
        {
            double flow = 0;
            for (int season = 0; season < 4; season++)
            {
                var k = new EcologyCapacity(full, fullTile, view, season, 1);
                flow += Math.Min(k.Food * k.FoodRate + k.ManagedFlow,
                    EcologyMath.SustainableHarvestRate(k.Biomass, k.BiomassRate,
                        full.Config.SustainableProductionFraction, k.ManagedFlow)
                    * full.Config.LandSafeUseRatio);
            }
            return flow;
        }
        Check(LegacyFlow(afterView) > LegacyFlow(beforeView),
            "prior gross-flow score falsely credits saturated managed input");
        Check(clipped.TileQueries == 2, "investment uses two bounded kernel queries");
        Near(clipped.RetainedFoodGain, 0, "capacity-clipped flow has no retained investment gain");
        Near(clipped.AnnualizedFoodPerWorkerYear, 0, "clipped flow has no worker value");
        Console.WriteLine("ISSUE26 clipping gross managed before/after=" +
            clipped.Before.GrossManagedFoodInput.ToString("R") + "/" +
            clipped.After.GrossManagedFoodInput.ToString("R") +
            " retained delta=" + clipped.RetainedFoodGain.ToString("R"));

        var growing = Land(5, 80, 20, 100, .01f);
        id = growing.Camps[0].Tile;
        e = Math.Pow(.5 / growing.Config.ReferenceIntensity, 2);
        var valuable = FoodHorizonForecast.Investment(growing, id, 0, 7300,
            0, e * .5, e, 1, 10, 20);
        Check(valuable.RetainedFoodGain > 0 && valuable.AnnualizedRetainedFoodGain > 0,
            "q raises retained next-boundary Food");
        var claim = new[] { new Demand { Tile = id, Entity = growing.Camps[0].Id,
            Kind = "Cultivation", Food = 50 } };
        var baseReceipt = FoodHorizonForecast.Tile(growing, id, 0, 7300,
            claim, 0, 0, 1, true);
        var candidateReceipt = FoodHorizonForecast.Tile(growing, id, 0, 7300,
            claim, e * .5, e, 1, true);
        Near(baseReceipt.HumanFoodReceipts, 5, "opening receipt is bounded by Food");
        Near(candidateReceipt.HumanFoodReceipts, baseReceipt.HumanFoodReceipts,
            "new q cannot retroactively increase current harvest");
    }

    private static void FixedPlanExecutionParity()
    {
        var scalar = NeolithicChecks.Fixture(); var camp = scalar.Camps[0];
        scalar.Config.TurnDays = 365;
        scalar.Config.GathererBridgeEfficiency = 0;
        camp.Population = 10; camp.PlantFood = 0;
        camp.Workforce.Assign(WorkforceRoles.Gatherer, camp.Population, camp.Population);
        var parallel = SaveCodec.Load(SaveCodec.Save(scalar));
        var prepared = TurnPlanner.Prepare(scalar, new Dictionary<int, List<Order>>());
        var parallelPrepared = TurnPlanner.Prepare(parallel, new Dictionary<int, List<Order>>());
        double target = TurnResolver.RemainingFoodTarget(camp, prepared.Needs[camp.Id]);
        var labor = new AgriculturalLabor
        {
            Gatherers = camp.Population, FoodGatherers = camp.Population,
            Footprint = CultivationEconomy.Footprint(scalar, camp)
        };
        string before = SaveCodec.Fingerprint(scalar);
        var forecast = FoodHorizonForecast.Settlement(scalar, camp, camp.Tile,
            scalar.Days, scalar.Config.TurnDays, labor, target,
            prepared.Needs[camp.Id].PlantFeedstockGap, null, null, null, true);
        Check(before == SaveCodec.Fingerprint(scalar) && scalar.MaterialIndex == null,
            "fixed-plan forecast leaves saved state and source cache untouched");
        var actual = new TurnResolver().Resolve(scalar, prepared);
        var parallelActual = new TurnResolver(new PhaseExecution(4, 1, true, 1))
            .Resolve(parallel, parallelPrepared);
        double received = actual.Transfers.Where(t => t.Entity == camp.Id &&
            t.Kind == "Gathering").Sum(t => t.Food);
        Near(forecast.TerrestrialFood, received,
            "fixed-plan no-contention terrestrial receipt equals execution");
        Console.WriteLine("ISSUE26 fixed plan forecast/actual food=" +
            forecast.TerrestrialFood.ToString("R") + "/" + received.ToString("R") +
            " local tile queries=" + forecast.TileQueries);
        foreach (var predicted in forecast.Tiles)
        {
            var actualTile = actual.EcologyBalances.Single(b => b.Tile == predicted.Tile);
            Near(predicted.ClosingFood, actualTile.EndingFood,
                "fixed-plan tile closing Food equals execution");
            Near(predicted.ClosingBiomass, actualTile.EndingBiomass,
                "fixed-plan tile closing biomass equals execution");
        }
        Check(SaveCodec.Fingerprint(scalar) == SaveCodec.Fingerprint(parallel),
            "scalar and reverse-partition four-worker fixed plans replay exactly");
        Near(actual.Transfers.Where(t => t.Entity == camp.Id).Sum(t => t.Food),
            parallelActual.Transfers.Where(t => t.Entity == camp.Id).Sum(t => t.Food),
            "scalar and parallel receipts agree");

        var reordered = Land(5, 25, 20, 80, .01f);
        int id = reordered.Camps[0].Tile;
        var claims = new[]
        {
            new Demand { Tile = id, Entity = 100, Kind = "Gathering", Food = 7 },
            new Demand { Tile = id, Entity = 200, Kind = "Cultivation", Food = 9 },
            new Demand { Tile = id, Entity = 300, Kind = "Herd", PotentialFood = 8 }
        };
        var forward = FoodHorizonForecast.Tile(reordered, id, 0, 365,
            claims, null, null, null, true);
        var reverse = FoodHorizonForecast.Tile(reordered, id, 0, 365,
            claims.Reverse(), null, null, null, true);
        Near(forward.ClosingFood, reverse.ClosingFood,
            "claim enumeration order cannot change closing Food");
        Near(forward.HumanFoodReceipts, reverse.HumanFoodReceipts,
            "claim enumeration order cannot change aggregate receipts");
        Check(forward.Transfers.Select(t => t.Entity + ":" + t.Food)
            .SequenceEqual(reverse.Transfers.Select(t => t.Entity + ":" + t.Food)),
            "canonical claimant receipts ignore input order");
    }

    private static void CoastalFoodAndFeedstock()
    {
        var w = NeolithicChecks.Fixture(); var c = w.Camps[0];
        c.PlantFood = c.AnimalFood = c.AquaticFood = 0;
        int lake = w.Map.Neighbors(c.Tile).OrderBy(id => id).First();
        w.Tiles[lake].Terrain = Terrain.Lake;
        foreach (int id in w.Map.Radius(c.Tile, 1))
        {
            var tile = w.Tiles[id];
            tile.Food = tile.PotentialFood = id == lake ? 1000 : 1;
            for (int s = 0; s < 4; s++)
            {
                tile.Seasons[s].MaxFood = id == lake ? 1000 : 1;
                tile.Seasons[s].MaxPotentialFood = id == lake ? 1000 : 1;
            }
        }
        var labor = new AgriculturalLabor
        {
            OperatingFarmers = 20, FoodGatherers = 80,
            Footprint = CultivationEconomy.Footprint(w, c)
        };
        var forecast = FoodHorizonForecast.Settlement(w, c, c.Tile, 0, 365,
            labor, 10000, 1000, null, null, null, true);
        Check(forecast.AquaticFood > 0, "supplemental lake gathering remains available");
        Check(forecast.TerrestrialFood <= labor.Footprint.Length + 1e-8,
            "terrestrial receipts do not borrow aquatic stock");
        Check(forecast.TerrestrialPlantFeedstock <= forecast.TerrestrialFood + 1e-8,
            "plant manufacturing consumes only terrestrial receipts once");
        Near(forecast.MealFood + forecast.TerrestrialPlantFeedstock,
            forecast.TerrestrialFood + forecast.AquaticFood,
            "one useful-food ledger splits meals from plant manufacturing");
        Check(forecast.MealFood >= forecast.AquaticFood - 1e-8,
            "aquatic receipts remain meals and never plant feedstock");
        Check(forecast.Tiles.SelectMany(t => t.Transfers)
            .Where(t => t.Kind == "Cultivation").All(t => w.Tiles[t.Tile].Land),
            "farmers never receive lake food");
        var noFarmers = new AgriculturalLabor
        {
            OperatingFarmers = 0, FoodGatherers = 80,
            Footprint = labor.Footprint
        };
        var zero = FoodHorizonForecast.Settlement(w, c, c.Tile, 0, 365,
            noFarmers, 10000, 1000, null, null, null, true);
        Check(zero.Tiles.Length >= labor.Footprint.Length &&
            zero.Tiles.All(t => t.ClosingFood >= 0),
            "zero-farmer query still transitions the field footprint");
        Check(zero.TileQueries == zero.Tiles.Length && zero.TileQueries <= 7,
            "zero-farmer query count follows local active tiles");
    }

    private static void ZeroTechnologyAndAbandonment()
    {
        var paleo = NeolithicChecks.Fixture(false); var camp = paleo.Camps[0];
        var plan = EconomicPlanner.Plan(paleo, camp,
            new EconomicIntent { AutomationTurns = -1 });
        Check(plan.Assignments.Count == 0 && plan.AutomationTurns == 0,
            "no Agriculture creates no automated field labor");
        var zero = FoodHorizonForecast.Tile(paleo, camp.Tile, 0, 7300,
            Array.Empty<Demand>(), 0, 0, 0, true);
        Near(zero.GrossManagedFoodInput, 0, "q zero has no managed food source");
        bool rejectedFarmer = false;
        try
        {
            FoodHorizonForecast.Settlement(paleo, camp, camp.Tile, 0, 365,
                new AgriculturalLabor { OperatingFarmers = 1,
                    Footprint = Array.Empty<int>() }, 100, 0);
        }
        catch (ArgumentException) { rejectedFarmer = true; }
        Check(rejectedFarmer, "forecast rejects farming labor without Agriculture");

        var abandoned = Land(20, 80, 30, 100, .01f, .8, 0);
        int id = abandoned.Camps[0].Tile;
        var partial = FoodHorizonForecast.Tile(abandoned, id, 0, 7300,
            Array.Empty<Demand>(), null, null, .5, true);
        var idle = FoodHorizonForecast.Tile(abandoned, id, 0, 7300,
            Array.Empty<Demand>(), null, null, 0, true);
        Check(partial.GrossManagedFoodInput > idle.GrossManagedFoodInput,
            "partial operation changes managed gross input");
        Check(idle.ClosingFood >= 0 && idle.ClosingBiomass >= idle.ClosingFood,
            "abandoned high-q field remains bounded");
        abandoned.Config.CultivationRegenFlux = 100;
        var saturated = FoodHorizonForecast.Tile(abandoned, id, 0, 7300,
            Array.Empty<Demand>(), null, null, 1, true);
        var endpoint = new EcologyCapacity(abandoned, abandoned.Tiles[id],
            new EcologyView { Food = saturated.ClosingFood,
                PotentialFood = saturated.ClosingBiomass,
                CultivationCapital = CultivationEconomy.Decay(abandoned.Config,
                    abandoned.Tiles[id].CultivationCapital, 1, 20),
                LandCondition = abandoned.Tiles[id].LandCondition,
                CurrentVegetation = abandoned.Tiles[id].CurrentVegetation }, 0, 1);
        Check(saturated.ClosingFood <= saturated.ClosingBiomass + 1e-8 &&
            saturated.ClosingFood <= endpoint.Food + 1e-8,
            "high-q managed input clips to physical capacity");
    }

    private static void AcceptedAndBlockedMovement()
    {
        foreach (bool blocked in new[] { false, true })
        {
            var w = NeolithicChecks.Fixture(); var c = w.Camps[0];
            w.Config.TurnDays = 365;
            w.Config.GathererBridgeEfficiency = 0;
            c.PlantFood = 0; c.Workforce.Assign(WorkforceRoles.Gatherer, c.Population, c.Population);
            c.IsVillage = true;
            int origin = c.Tile;
            int destination = w.Map.Neighbors(origin).OrderBy(id => id).First();
            int exclusive = w.Map.Neighbors(destination)
                .First(id => w.Map.Distance(origin, id) == 2);
            foreach (var tile in w.Tiles) tile.Food = tile.PotentialFood = 0;
            w.Tiles[origin].Food = w.Tiles[origin].PotentialFood = 30;
            w.Tiles[origin].CultivationCapital =
                Math.Pow(.8 / w.Config.ReferenceIntensity, 2);
            w.Tiles[origin].CultivationOperator = c.Id;
            w.Tiles[origin].CultivationOperatingFraction = 1;
            w.Tiles[exclusive].Food = w.Tiles[exclusive].PotentialFood = 100;
            if (blocked)
            {
                var occupant = new Camp { Id = w.NextId++, Tile = destination, Population = 1 };
                occupant.Bind(c.People); w.Camps.Add(occupant);
            }
            var submitted = new Dictionary<int, List<Order>>
            { [c.Player] = new List<Order> { new Order(OrderKind.Move, c.Id,
                destination: destination) } };
            var prepared = TurnPlanner.Prepare(w, submitted);
            int accepted = prepared.Placement.Destinations[c.Id];
            Check(accepted == (blocked ? origin : destination),
                "placement reports accepted move or rejected fallback");
            Check(prepared.AbandoningVillages.Contains(c.Id) == !blocked,
                "only accepted village movement abandons field work");
            var labor = blocked
                ? CultivationEconomy.CalculateLabor(w, c,
                    CultivationEconomy.Footprint(w, c), new FarmToolWorkBudget(w, c), 1)
                : new AgriculturalLabor { Gatherers = c.Population,
                    FoodGatherers = c.Population,
                    Footprint = Array.Empty<int>() };
            var forecast = FoodHorizonForecast.Settlement(w, c, accepted, 0, 365,
                labor, TurnResolver.RemainingFoodTarget(c, prepared.Needs[c.Id]),
                prepared.Needs[c.Id].PlantFeedstockGap, null, null, null, true);
            var actual = new TurnResolver().Resolve(w, prepared);
            double received = actual.Transfers.Where(t => t.Entity == c.Id &&
                (t.Kind == "Gathering" || t.Kind == "Aquatic gathering" ||
                 t.Kind == "Marine gathering")).Sum(t => t.Food);
            Check(c.Tile == accepted, "resolver commits prepared destination");
            Check(c.IsVillage == blocked && w.Tiles[origin].CultivationCapital > 0,
                "accepted departure leaves physical field capital but village status falls back");
            Near(forecast.TerrestrialFood, received,
                "forecast and prepared resolution agree on accepted/fallback receipt");
            foreach (var predicted in forecast.Tiles)
            {
                var settled = actual.EcologyBalances.Single(b => b.Tile == predicted.Tile);
                Near(predicted.ClosingFood, settled.EndingFood,
                    "accepted/fallback tile closing Food equals execution");
                Near(predicted.ClosingBiomass, settled.EndingBiomass,
                    "accepted/fallback tile closing biomass equals execution");
            }
        }
    }

    private static void BoundedWork()
    {
        var w = Land(5, 30, 50, 100, .01f);
        int id = w.Camps[0].Tile;
        FoodHorizonForecast.Tile(w, id, 0, 365);
        long before = GC.GetAllocatedBytesForCurrentThread();
        var shortRun = FoodHorizonForecast.Tile(w, id, 0, 365,
            countRecoveryProfiles: true);
        long shortBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        var longRun = FoodHorizonForecast.Tile(w, id, 0, 2000 * 365.0,
            countRecoveryProfiles: true);
        long longBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(shortRun.Tile == longRun.Tile && longBytes <= shortBytes + 4096 &&
            shortRun.RecoveryProfileEvaluations <= 6 &&
            longRun.RecoveryProfileEvaluations <= 6,
            "query allocation is bounded independently of years/BaseDays: " +
            shortBytes + "/" + longBytes + " profiles=" +
            shortRun.RecoveryProfileEvaluations + "/" +
            longRun.RecoveryProfileEvaluations);
        Console.WriteLine("ISSUE26 bounded query bytes 1y/2000y=" +
            shortBytes + "/" + longBytes + " recovery profiles=" +
            shortRun.RecoveryProfileEvaluations + "/" +
            longRun.RecoveryProfileEvaluations);
    }

    public static int Run()
    {
        checks = 0;
        KernelParityAndPurity(); FixedPlanExecutionParity();
        ClippedInvestmentAndTiming();
        CoastalFoodAndFeedstock(); ZeroTechnologyAndAbandonment();
        AcceptedAndBlockedMovement(); BoundedWork();
        Console.WriteLine("PASS: " + checks + " issue 26 forecast assertions");
        return checks;
    }
}
