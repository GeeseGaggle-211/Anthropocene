using System;
using System.Collections.Generic;
using System.Linq;
using Anthropocene.Simulation;

// Focused fixtures for the economic-settlement contract. Probe() is also kept as
// a compact before/after witness for the architectural correction.
internal static class EconomicSettlementChecks
{
    private static int checks;
    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception("Economic settlement: " + message);
    }
    private static void Near(double actual, double expected, string message,
        double tolerance = 1e-8)
        => Check(Math.Abs(actual - expected) <= tolerance *
            Math.Max(1, Math.Abs(expected)), message + " (" + actual +
            " vs " + expected + ")");

    private static WorldState Village(int animals, bool husbandry = true,
        Technology primary = Technology.Cultivation, bool fields = true,
        double housing = 30, bool village = true)
    {
        var cfg = SimulationConfig.Small(1);
        cfg.Width = 20; cfg.Height = 14;
        cfg.EconomicAllocation = EconomicAllocationStrategy.DemandControl;
        cfg.AnnualHumanGrowthRate = 0;
        cfg.FissionEnabled = cfg.ImmigrationEnabled = cfg.TradeEnabled = false;
        var w = new WorldState
        {
            Seed = 9301, Config = cfg, Generation = new GenerationReport(),
            Tiles = new Tile[cfg.Width * cfg.Height], PlayerCount = 1, Turn = 4, Days = 1000
        };
        for (int i = 0; i < w.Tiles.Length; i++)
        {
            var tile = new Tile
            {
                Id = i, ElevationLevel = 1, Terrain = Terrain.Grassland,
                FreshwaterSource = FreshwaterSource.RainPool,
                Freshwater = 20000, Food = 1200, PotentialFood = 12000,
                LastEcologyUpdateDays = w.Days
            };
            for (int s = 0; s < tile.Seasons.Length; s++)
                tile.Seasons[s] = new SeasonData
                {
                    Temperature = 18, Moisture = .55f, RegionalMoisture = .5f,
                    MaxFood = 1200, MaxPotentialFood = 12000, MaxFreshwater = 20000,
                    FoodRecoveryPerDay = .004f, BiomassRecoveryPerDay = .004f,
                    WaterRecoveryPerDay = .004f, Habitat = .6f
                };
            w.Tiles[i] = tile;
        }
        var camp = new Camp
        {
            Id = w.NextId++, Player = 0, Tile = w.Map.At(7, 7), Population = 120,
            PlantFood = 6000, AnimalFood = 1000, RudimentaryExpectation = .5,
            StationaryYears = village ? 80 : 0, StationaryTurns = village ? 4 : 0,
            IsVillage = village,
            EconomyAutomationTurns = -1
        };
        camp.Inventory.HandTools = 240; camp.Inventory.FarmTools = 100;
        camp.Inventory.HerdingTools = 20; camp.Inventory.Wood = 1000;
        camp.Inventory.Stone = 1000; camp.Inventory.Clay = 1000;
        w.Camps.Add(camp);
        Ownership.Bind(w);
        camp.Technology = primary;
        camp.Technologies[(int)Technology.Cultivation] = true;
        camp.Technologies[(int)Technology.AnimalHusbandry] = husbandry;
        camp.People.Progression.Era = Era.Neolithic;
        camp.People.NeolithicTechnologies.Add(NeolithicTech.Construction);
        if (fields)
        {
            foreach (int id in w.Map.Radius(camp.Tile, 1))
            {
                w.Tiles[id].CultivationOperator = camp.Id;
                w.Tiles[id].CultivationCapital = .16;
                w.Tiles[id].CultivationOperatingFraction = 1;
            }
        }
        if (housing > 0)
            NeolithicProduction.AddFixedHousing(w, camp, housing, w.Days);
        if (animals > 0)
            w.Herds.Add(new Herd
            {
                Id = w.NextId++, Tile = camp.Tile, DomesticCamp = camp.Id,
                OwnerSociety = camp.SocietyId, Animals = animals, Capacity = Math.Max(animals, 1000)
            });
        return w;
    }

    private sealed class Planned
    {
        public EconomicIntent Intent;
        public ProductionOrder Order;
        public DemandController.Allocation Allocation;
        public int Farmers, Herders;
    }

    private static Planned Plan(WorldState world)
    {
        var camp = world.Camps.OrderBy(c => c.Id).First();
        var intent = DemandController.Plan(world, camp,
            EconomicIntent.From(camp), out var order, out var allocation);
        return new Planned
        {
            Intent = intent, Order = order, Allocation = allocation,
            Farmers = intent.Assignments.Where(a => a.Role == WorkforceRoles.Farmer)
                .Sum(a => a.Population),
            Herders = intent.Assignments.Where(a => a.Role == WorkforceRoles.Herder)
                .Sum(a => a.Population)
        };
    }

    private static Order Movement(WorldState world)
    {
        var camp = world.Camps.OrderBy(c => c.Id).First();
        return new HeuristicTribePolicy().Decide(
            new AgentObservationBuilder().Build(world, camp.Player), camp.Mandate)
            .Orders.Single(o => o.Entity == camp.Id);
    }

    private static void ResolveEconomicTurn(WorldState world,
        PhaseExecution execution = null)
    {
        execution = execution ?? new PhaseExecution();
        var prepared = TurnPlanner.Prepare(world,
            new Dictionary<int, List<Order>>(), null, execution);
        new TurnResolver(execution).Resolve(world, prepared);
    }

    private static void TechnologyOnlyInvariance()
    {
        // Keep meaningful fixed capital but an ordinary housing deficit: the
        // secondary capability must not redirect that deficit into tents.
        var control = Village(0, husbandry: false, housing: 15);
        var treatment = SaveCodec.Load(SaveCodec.Save(control));
        treatment.Camps[0].Technologies[(int)Technology.AnimalHusbandry] = true;
        var controlMove = Movement(control); var treatmentMove = Movement(treatment);
        var a = Plan(control); var b = Plan(treatment);
        Check(controlMove.Kind == OrderKind.NoOp &&
            treatmentMove.Kind == controlMove.Kind &&
            treatmentMove.Destination == controlMove.Destination,
            "A technology-only Husbandry grant does not cause relocation");
        Check(a.Farmers == b.Farmers &&
            a.Allocation.FoodGatherers == b.Allocation.FoodGatherers &&
            a.Allocation.FoodUnmet == b.Allocation.FoodUnmet,
            "A technology-only Husbandry grant leaves field and human-food staffing unchanged");
        Near(b.Order.Ceiling(CraftGood.Housing),
            a.Order.Ceiling(CraftGood.Housing),
            "A technology-only Husbandry grant leaves fixed-housing replacement unchanged");
        Check(a.Order.Ceiling(CraftGood.LightHousing) == 0 &&
            b.Order.Ceiling(CraftGood.LightHousing) == 0,
            "new tent feasibility is unused by an established fixed-housing village");
        Check(b.Herders == 0 && !b.Intent.HerdTargetSet &&
            !b.Allocation.Requests.Any(r => r.Service == "herd development"),
            "Husbandry knowledge without animals creates neither herders nor herd development");
    }

    private static void SmallComplementaryHerd()
    {
        var world = Village(8); var camp = world.Camps[0];
        var site = SettlementSiteEvaluation.Evaluate(world, camp, camp.Tile,
            camp.Population, HerdConsumptionProfile.Owned(world, camp));
        var p = Plan(world);
        Check(site.Herd.ZeroDisplacementCapacity >= 8 &&
            p.Allocation.HSustainable >= 8,
            "B fixture herd is below its zero-displacement and sustainable ceilings");
        Check(Movement(world).Kind == OrderKind.NoOp,
            "a mature village with a complementary herd stays");
        Check(p.Farmers > 0 && p.Herders > 0,
            "retained fields and the complementary herd are both staffed");
        Check(p.Order.Ceiling(CraftGood.Housing) > 0 &&
            p.Order.Ceiling(CraftGood.LightHousing) == 0,
            "fixed housing remains the normal retained shelter capital");
        Check(p.Intent.HerdTargetSet && p.Intent.HerdTarget >= 8 &&
            p.Intent.HerdTarget <= Math.Floor(Math.Min(
                p.Allocation.HSustainable, p.Allocation.HGrown) + 1e-9),
            "the small herd target is bounded by sustainable support and biological growth");
        Check(p.Allocation.Requests.Any(r =>
                r.Service == "retained herd management" &&
                r.Class == DemandController.ServiceClass.Continuity &&
                r.Granted == r.Units) &&
            p.Allocation.Requests.Where(r => r.Service == "herd development")
                .All(r => r.Class == DemandController.ServiceClass.Development),
            "retention is Continuity while every herd increment is Development");
        var forecast = TurnResolver.ForecastManagedAnimalFood(world, camp,
            p.Herders, world.Config.GenerationIntervals,
            world.Config.TurnDays / world.Config.DaysPerYear, camp.Tile);
        Near(forecast.HumanFoodDisplaced, 0,
            "a complementary herd consumes no human-accessible harvest", 1e-7);
    }

    private static void OversizedAcquiredHerd()
    {
        var world = Village(400); var camp = world.Camps[0];
        var p = Plan(world);
        Check(p.Allocation.HCurrent > p.Allocation.HSustainable &&
            p.Allocation.HCurrent <= p.Allocation.HAbsolute,
            "C fixture is above the retained sustainable herd but below the absolute site ceiling");
        Check(p.Farmers > 0 && p.Intent.HerdTargetSet &&
            p.Intent.HerdTarget < 400 &&
            p.Intent.HerdTarget <= Math.Floor(p.Allocation.HSustainable + 1e-9),
            "the oversized herd is reduced without preempting established field operation");
        Check(Movement(world).Kind == OrderKind.NoOp,
            "the village does not relocate merely to avoid controlled liquidation");
        int target = p.Intent.HerdTarget;
        var owned = world.Herds.Single(h => h.DomesticCamp == camp.Id);
        var species = world.Config.GetSpecies(owned.Species);
        double expectedForage = owned.Animals * species.FoodConsumption *
            world.Config.GenerationIntervals;
        double expectedWater = owned.Animals * species.FreshwaterConsumption *
            world.Config.GenerationIntervals;
        ResolveEconomicTurn(world);
        var turn = world.History.Last();
        var report = turn.ManagedHerds.Single(r => r.Camp == camp.Id);
        Near(turn.Pressure.Where(q => q.Kind == "Herd" &&
                q.Entity == owned.Id && q.Pool == "Biomass").Sum(q => q.Requested),
            expectedForage,
            "lowering the herd target does not erase current-turn forage demand");
        Near(turn.Pressure.Where(q => q.Kind == "Herd" &&
                q.Entity == owned.Id && q.Pool == "Freshwater").Sum(q => q.Requested),
            expectedWater,
            "lowering the herd target does not erase current-turn water demand");
        Check(report.Slaughtered > 0 &&
            world.Herds.Where(h => h.DomesticCamp == camp.Id).Sum(h => h.Animals) == target,
            "excess acquired livestock is slaughtered through the authoritative herd target path");
        Check(world.Map.Radius(camp.Tile, 1).All(id =>
                world.Tiles[id].CultivationOperator == camp.Id &&
                world.Tiles[id].CultivationCapital >= .15),
            "field capital remains retained during oversized-herd liquidation");
    }

    private static void GrosslyOversizedHerd()
    {
        var world = Village(4000); var camp = world.Camps[0];
        camp.PlantFood = 200000; camp.AnimalFood = camp.AquaticFood = 0;
        var p = Plan(world);
        Check(p.Allocation.HCurrent > p.Allocation.HAbsolute,
            "D fixture substantially exceeds the absolute ecological ceiling");
        Check(p.Intent.Assignments.Sum(a => a.Population) +
            p.Intent.Labor.MaterialGatherers <= camp.Population + 1e-9,
            "a grossly oversized herd cannot over-allocate residents");
        int starting = world.Herds.Where(h => h.DomesticCamp == camp.Id)
            .Sum(h => h.Animals);
        ResolveEconomicTurn(world);
        var reports = world.History.Last().ManagedHerds.Where(r => r.Camp == camp.Id).ToArray();
        int ending = world.Herds.Where(h => h.DomesticCamp == camp.Id)
            .Sum(h => h.Animals);
        Check(reports.Sum(r => r.StartingAnimals) == starting &&
            reports.Sum(r => r.SurvivingAnimals + r.StarvationDeaths) == starting &&
            ending == reports.Sum(r => r.SurvivingAnimals + r.NewbornAnimals - r.Slaughtered),
            "gross-herd resolution conserves living animals, deaths, births and slaughter");
        Near(reports.Sum(r => r.SlaughterFood), reports.Sum(r =>
            r.Slaughtered * world.Config.GetSpecies(r.Species).FoodYield),
            "gross-herd slaughter conserves carcass food");
        Check(reports.Sum(r => r.Slaughtered) > 0 && camp.DeficitTurns == 0 &&
            camp.FoodNeedsMetRatio >= 1 - 1e-9,
            "liquidation is deterministic and does not impose human starvation");
    }

    private static WorldState PastoralOpportunity(Technology primary)
    {
        var world = Village(180, husbandry: true, primary: primary,
            fields: false, housing: 0, village: false);
        var camp = world.Camps[0]; camp.PlantFood = camp.AnimalFood = 0;
        camp.Mandate.Mobility = 1;
        foreach (var tile in world.Tiles)
        {
            tile.Food = 0; tile.PotentialFood = 100; tile.Freshwater = 0;
            tile.FreshwaterSource = FreshwaterSource.None;
            for (int s = 0; s < tile.Seasons.Length; s++)
            {
                var season = tile.Seasons[s];
                season.MaxFood = 0; season.MaxPotentialFood = 100;
                season.MaxFreshwater = 0; season.WaterRecoveryPerDay = 0;
                tile.Seasons[s] = season;
            }
        }
        int step = world.Map.Neighbors(camp.Tile)
            .Where(id => world.Tiles[id].TraversableLand).OrderBy(id => id).First();
        int rested = world.Map.Neighbors(step)
            .Where(id => world.Map.Distance(camp.Tile, id) == 2 &&
                world.Tiles[id].TraversableLand).OrderBy(id => id).First();
        var rich = world.Tiles[rested]; rich.Food = 10000;
        rich.PotentialFood = 200000; rich.Freshwater = 200000;
        rich.FreshwaterSource = FreshwaterSource.RainPool;
        for (int s = 0; s < rich.Seasons.Length; s++)
        {
            var season = rich.Seasons[s];
            season.MaxFood = 10000; season.MaxPotentialFood = 200000;
            season.MaxFreshwater = 200000;
            rich.Seasons[s] = season;
        }
        return world;
    }

    private static void LegitimatePastoralMovement()
    {
        var world = PastoralOpportunity(Technology.AnimalHusbandry);
        var camp = world.Camps[0]; var choice = Movement(world);
        Check(choice.Kind == OrderKind.Move && choice.Destination != camp.Tile,
            "E poor current forage/water and a dominant alternative permit a pastoral move");
    }

    private static void PrimaryTechnologyInvariance()
    {
        var agricultureFirst = Village(80, husbandry: true,
            primary: Technology.Cultivation);
        var husbandryFirst = SaveCodec.Load(SaveCodec.Save(agricultureFirst));
        husbandryFirst.Camps[0].Technology = Technology.AnimalHusbandry;
        var aMove = Movement(agricultureFirst); var hMove = Movement(husbandryFirst);
        var aPlan = Plan(agricultureFirst); var hPlan = Plan(husbandryFirst);
        Check(aMove.Kind == OrderKind.NoOp && hMove.Kind == aMove.Kind &&
            hMove.Destination == aMove.Destination,
            "F a husbandry-first mixed settlement retains meaningful fields and housing exactly like its physical twin");
        Check(aPlan.Intent.Stamp == hPlan.Intent.Stamp &&
            aPlan.Order.ToString() == hPlan.Order.ToString(),
            "equivalent mixed settlements receive identical allocations regardless of first breakthrough");
        Check(Fission.RegimeOf(agricultureFirst, agricultureFirst.Camps[0]) ==
                Fission.RegimeOf(husbandryFirst, husbandryFirst.Camps[0]) &&
            Fission.RegimeOf(agricultureFirst, agricultureFirst.Camps[0]) ==
                FissionRegime.Agricultural,
            "daughter-site policy also follows retained fields rather than first technology");

        var pastoral = PastoralOpportunity(Technology.Cultivation);
        Check(Movement(pastoral).Kind == OrderKind.Move &&
            Fission.RegimeOf(pastoral, pastoral.Camps[0]) ==
                FissionRegime.Nomadic,
            "G an agriculture-first camp with negligible fixed capital remains free to move pastorally");
    }

    private static void ShelterCapabilityInvariance()
    {
        var control = Village(0, husbandry: false, housing: 15);
        var treatment = SaveCodec.Load(SaveCodec.Save(control));
        treatment.Camps[0].Technologies[(int)Technology.AnimalHusbandry] = true;
        var a = Plan(control); var b = Plan(treatment);
        // With the kernel-scored Elder split, protected shelter serves retained replacement first.
        bool Continuity(DemandController.Request r) => r.Service == "shelter replacement" || r.Service == "shelter protected";
        var ar = a.Allocation.Requests.Where(Continuity).ToArray();
        var br = b.Allocation.Requests.Where(Continuity).ToArray();
        Check(ar.Length > 0 && ar.All(r => r.Good == CraftGood.Housing) && br.All(r => r.Good == CraftGood.Housing) &&
            Math.Abs(ar.Sum(r => r.Units) - br.Sum(r => r.Units)) < 1e-9 &&
            b.Order.Ceiling(CraftGood.LightHousing) == 0,
            "H secondary Husbandry does not redirect feasible fixed-housing continuity to tents");

        var fallback = Village(0, husbandry: true);
        var camp = fallback.Camps[0];
        camp.Inventory.Wood = camp.Inventory.Clay =
            camp.Inventory.HousingMaterial = 0;
        camp.Inventory.AnimalFabric = 100;
        var fallbackPlan = Plan(fallback);
        Check(fallbackPlan.Allocation.Requests.Any(r =>
                (r.Service == "shelter replacement" || r.Service == "shelter protected") &&
                r.Good == CraftGood.LightHousing && r.Granted > 0),
            "an infeasible preferred shelter route admits a feasible bounded fallback");
    }

    private static void ReviewerRegressions()
    {
        // A full pantry removes every immediate food-gap authorization. Natural
        // growth inside the sustainable envelope must still be an explicit
        // Development request, never hidden Continuity or spare-labor growth.
        var stocked = Village(8); var stockedCamp = stocked.Camps[0];
        stockedCamp.PlantFood = 200000; stockedCamp.AnimalFood = 0;
        stockedCamp.Inventory.Cloth = 1000;
        var growth = Plan(stocked);
        var natural = growth.Allocation.Requests.Single(r =>
            r.Service == "herd development");
        Check(natural.Class == DemandController.ServiceClass.Development &&
            natural.PolicyDevelopmentUnits > 0 && natural.Granted > 0 &&
            growth.Intent.HerdTarget > 8,
            "sustainable natural growth remains an admitted Development request even with no immediate food gap");

        // Fixed shelter is not evidence of a plant textile chain. A physically
        // herd-bearing camp with no operated fields may keep the feasible animal
        // route while retaining its fixed Housing shelter route.
        var animalChain = Village(8, fields: false, housing: 30);
        var animalPlan = Plan(animalChain);
        Check(animalPlan.Order.Ceiling(CraftGood.AnimalFabric) > 0 &&
            animalPlan.Order.Ceiling(CraftGood.Cloth) == 0 &&
            animalPlan.Order.Ceiling(CraftGood.LightHousing) == 0,
            "fixed housing alone does not invent a retained plant-textile strategy");

        // Construction cost is not useful output. With physically worthless
        // fields, making reconstruction arbitrarily expensive cannot make their
        // operation attractive through the retained-capital term.
        WorldState Worthless(double intensificationTurns)
        {
            var world = Village(0); var camp = world.Camps[0];
            world.Config.Neolithic.IntensificationTurns = intensificationTurns;
            camp.PlantFood = 200000; camp.AnimalFood = 0;
            foreach (int id in world.Map.Radius(camp.Tile, 1))
            {
                var tile = world.Tiles[id];
                tile.Food = tile.PotentialFood = 0;
                for (int season = 0; season < tile.Seasons.Length; season++)
                {
                    var profile = tile.Seasons[season];
                    profile.MaxFood = profile.MaxPotentialFood = 0;
                    profile.FoodRecoveryPerDay =
                        profile.BiomassRecoveryPerDay = 0;
                    tile.Seasons[season] = profile;
                }
            }
            return world;
        }
        var ordinaryCost = Plan(Worthless(10));
        var extremeCost = Plan(Worthless(10000));
        Check(ordinaryCost.Allocation.SelectedFieldOperation == 0 &&
            extremeCost.Allocation.SelectedFieldOperation == 0,
            "avoided reconstruction cost cannot reward a field with no useful continuation");

        // Retained operating farmers harvest while maintaining fields. Their
        // unused physical capacity must pass through the shared food ledger
        // before another Gatherer is requested for the same growth service.
        var farmerCapacity = Village(0); var farmerCamp = farmerCapacity.Camps[0];
        // The ledger fixture bounds growth with the legacy net rate; the physical model's maximum-birth envelope is larger.
        farmerCapacity.Config.DemographicModel = DemographicModel.LegacyQoLNetGrowth;
        farmerCapacity.Config.AnnualHumanGrowthRate = .01145;
        farmerCamp.PlantFood = 200000;
        farmerCamp.Inventory.Cloth = 1000;
        var farmerPlan = Plan(farmerCapacity);
        Check(farmerPlan.Allocation.Farmers > 0 &&
            farmerPlan.Allocation.RetainedFarmerFoodService > 0 &&
            !farmerPlan.Allocation.Requests.Any(r =>
                r.Service == "food growth gathering") &&
            farmerPlan.Allocation.DevelopmentFoodGap <= 1e-8,
            "retained farmer harvest capacity serves growth once without duplicate gathering labor");

        // Field capital is built after this turn's harvest. Its future closing
        // stock may justify Development work, but cannot erase an unmet current
        // receipt merely by winning the return ordering.
        var temporal = Plan(Village(8));
        var futureField = temporal.Allocation.Requests.Single(r =>
            r.Service == "field expansion");
        Check(futureField.Granted > 0 && futureField.DevelopmentFood > 0 &&
            temporal.Allocation.DevelopmentFoodGap > 0,
            "future field stock does not satisfy the current-turn food ledger");
    }

    private static Camp AddMatureCamp(WorldState world, int x, int y,
        int player, int animals, Technology primary)
    {
        var society = new Society
        {
            Id = world.NextId++, Player = player,
            PolicySeed = 1000 + player, Technology = primary
        };
        society.Technologies[(int)Technology.Cultivation] = true;
        society.Technologies[(int)Technology.AnimalHusbandry] = true;
        society.Progression.Era = Era.Neolithic;
        society.NeolithicTechnologies.Add(NeolithicTech.Construction);
        world.Societies.Add(society);
        var camp = new Camp
        {
            Id = world.NextId++, Player = player, Tile = world.Map.At(x, y),
            Population = 120, PlantFood = 6000, AnimalFood = 1000,
            RudimentaryExpectation = .5, StationaryYears = 80,
            StationaryTurns = 4, IsVillage = true,
            EconomyAutomationTurns = -1
        };
        camp.Inventory.HandTools = 240; camp.Inventory.FarmTools = 100;
        camp.Inventory.HerdingTools = 20; camp.Inventory.Wood = 1000;
        camp.Inventory.Stone = 1000; camp.Inventory.Clay = 1000;
        camp.Bind(society); world.Camps.Add(camp);
        foreach (int id in world.Map.Radius(camp.Tile, 1))
        {
            world.Tiles[id].CultivationOperator = camp.Id;
            world.Tiles[id].CultivationCapital = .16;
            world.Tiles[id].CultivationOperatingFraction = 1;
        }
        NeolithicProduction.AddFixedHousing(world, camp, 30, world.Days);
        if (animals > 0)
            world.Herds.Add(new Herd
            {
                Id = world.NextId++, Tile = camp.Tile, DomesticCamp = camp.Id,
                OwnerSociety = society.Id, Animals = animals,
                Capacity = Math.Max(animals, 5000)
            });
        return camp;
    }

    private static WorldState MixedCohort()
    {
        var world = Village(8);
        AddMatureCamp(world, 2, 2, 1, 80, Technology.AnimalHusbandry);
        AddMatureCamp(world, 16, 2, 2, 400, Technology.Cultivation);
        AddMatureCamp(world, 16, 11, 3, 1200, Technology.AnimalHusbandry);
        world.Config.Players = world.PlayerCount = 4;
        Ownership.Bind(world);
        return world;
    }

    private static void AutonomousTurn(WorldState world, PhaseExecution execution)
    {
        var decisions = AiPlanning.Decide(world, execution);
        var prepared = TurnPlanner.Prepare(world, decisions.Orders,
            decisions.Traces, execution);
        new TurnResolver(execution).Resolve(world, prepared);
    }

    private static void DeterminismAndPersistence()
    {
        var scalar = MixedCohort();
        var parallel = SaveCodec.Load(SaveCodec.Save(scalar));
        parallel.Camps.Reverse(); parallel.Societies.Reverse();
        parallel.Herds.Reverse();
        var scalarExecution = new PhaseExecution();
        var parallelExecution = new PhaseExecution(4, 1, true, 1);
        for (int turn = 0; turn < 2; turn++)
        {
            AutonomousTurn(scalar, scalarExecution);
            AutonomousTurn(parallel, parallelExecution);
            Check(SaveCodec.Fingerprint(scalar) == SaveCodec.Fingerprint(parallel),
                "I scalar and reversed multi-worker settlement plans agree after turn " + turn);
        }

        var uninterrupted = MixedCohort();
        AutonomousTurn(uninterrupted, scalarExecution);
        var loaded = SaveCodec.Load(SaveCodec.Save(uninterrupted));
        for (int turn = 0; turn < 2; turn++)
        {
            AutonomousTurn(uninterrupted, scalarExecution);
            AutonomousTurn(loaded, parallelExecution);
            Check(SaveCodec.Fingerprint(uninterrupted) == SaveCodec.Fingerprint(loaded),
                "I save/load continuation matches across execution modes after turn " + turn);
        }

        var ordinary = Village(8); var large = Village(8);
        large.Camps[0].Population = 10000;
        int ordinaryTiles = SettlementSiteEvaluation.Evaluate(ordinary,
            ordinary.Camps[0], ordinary.Camps[0].Tile, ordinary.Camps[0].Population,
            HerdConsumptionProfile.Owned(ordinary, ordinary.Camps[0])).SurveyedTiles;
        int largeTiles = SettlementSiteEvaluation.Evaluate(large,
            large.Camps[0], large.Camps[0].Tile, large.Camps[0].Population,
            HerdConsumptionProfile.Owned(large, large.Camps[0])).SurveyedTiles;
        Check(ordinaryTiles <= 7 && largeTiles == ordinaryTiles,
            "the shared settlement envelope stays footprint-bounded as population grows");
        int ordinaryAlternatives = Plan(ordinary).Allocation.PortfolioAlternatives;
        int largeAlternatives = Plan(large).Allocation.PortfolioAlternatives;
        Check(ordinaryAlternatives <= 64 && largeAlternatives <= 64,
            "joint settlement alternatives remain fixed-bounded rather than scaling with population");
    }

    public static void Probe()
    {
        var technologyControl = Village(0, husbandry: false, housing: 15);
        var technologyTreatment = SaveCodec.Load(SaveCodec.Save(technologyControl));
        technologyTreatment.Camps[0].Technologies[(int)Technology.AnimalHusbandry] = true;
        var technologyA = Plan(technologyControl);
        var technologyB = Plan(technologyTreatment);
        Console.WriteLine("SETTLEMENT_TECH_ONLY control=farmers=" + technologyA.Farmers +
            " retainedFarmers=" + technologyA.Allocation.Farmers +
            " expansionFarmers=" + technologyA.Intent.Labor.ExpansionFarmers +
            " herders=" + technologyA.Herders +
            " housing=" + technologyA.Order.Ceiling(CraftGood.Housing).ToString("F3") +
            " tents=" + technologyA.Order.Ceiling(CraftGood.LightHousing).ToString("F3") +
            " treatment=farmers=" + technologyB.Farmers +
            " retainedFarmers=" + technologyB.Allocation.Farmers +
            " expansionFarmers=" + technologyB.Intent.Labor.ExpansionFarmers +
            " herders=" + technologyB.Herders +
            " housing=" + technologyB.Order.Ceiling(CraftGood.Housing).ToString("F3") +
            " tents=" + technologyB.Order.Ceiling(CraftGood.LightHousing).ToString("F3"));
        foreach (int animals in new[] { 8, 400 })
        {
            var w = Village(animals); var c = w.Camps[0];
            var ladder = CultivationEconomy.ExpansionLadder(w, c,
                w.Config.TurnDays / w.Config.DaysPerYear,
                CultivationEconomy.Footprint(w, c));
            var intent = DemandController.Plan(w, c, EconomicIntent.From(c), out var order, out var allocation);
            int farmers = intent.Assignments.Where(a => a.Role == WorkforceRoles.Farmer).Sum(a => a.Population);
            int herders = intent.Assignments.Where(a => a.Role == WorkforceRoles.Herder).Sum(a => a.Population);
            var herdRequests = allocation.Requests.Where(q => q.Service.IndexOf("herd", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(q => q.Service + ":" + q.Class + ":" + q.Units.ToString("F3") + ":" + q.Granted.ToString("F3"));
            var developmentRequests = allocation.Requests
                .Where(q => q.Class == DemandController.ServiceClass.Development)
                .Select(q => q.Service + ":" + q.Units.ToString("F3") + ":" +
                    q.Granted.ToString("F3") + ":" + q.PriorityScore.ToString("F3") +
                    ":" + q.Unmet);
            Console.WriteLine("SETTLEMENT_PROBE animals=" + animals +
                " farmers=" + farmers + " herders=" + herders +
                " retainedFarmers=" + allocation.Farmers +
                " foodGatherers=" + allocation.FoodGatherers +
                " retainedFood=" + allocation.RetainedFoodSupply.ToString("F3") +
                " developmentGap=" + allocation.DevelopmentFoodGap.ToString("F3") +
                " fieldOperation=" + allocation.SelectedFieldOperation.ToString("F3") +
                " portfolioValue=" + allocation.SelectedPortfolioValue.ToString("F3") +
                " portfolioUnmet=" + allocation.SelectedPortfolioUnmet.ToString("F3") +
                " herdTarget=" + (intent.HerdTargetSet ? intent.HerdTarget : -1) +
                " sustainable=" + allocation.HSustainable.ToString("F3") +
                " absolute=" + allocation.HAbsolute.ToString("F3") +
                " grown=" + allocation.HGrown.ToString("F3") +
                " housing=" + order.Ceiling(CraftGood.Housing).ToString("F3") +
                " tents=" + order.Ceiling(CraftGood.LightHousing).ToString("F3") +
                " cloth=" + order.Ceiling(CraftGood.Cloth).ToString("F3") +
                " animalFabric=" + order.Ceiling(CraftGood.AnimalFabric).ToString("F3") +
                " fieldScore=" + ladder.Select(s => s.Score).DefaultIfEmpty(0).Max().ToString("F3") +
                " herdRequests=" + string.Join("|", herdRequests) +
                " development=" + string.Join("|", developmentRequests));
        }
        var matureCultivation = Village(80, husbandry: true,
            primary: Technology.Cultivation);
        var matureHusbandry = SaveCodec.Load(SaveCodec.Save(matureCultivation));
        matureHusbandry.Camps[0].Technology = Technology.AnimalHusbandry;
        var pastoralCultivation = PastoralOpportunity(Technology.Cultivation);
        var pastoralHusbandry = PastoralOpportunity(Technology.AnimalHusbandry);
        Console.WriteLine("SETTLEMENT_MOVE matureMixed.cultivationPrimary=" +
            Movement(matureCultivation) + " husbandryPrimary=" +
            Movement(matureHusbandry));
        Console.WriteLine("SETTLEMENT_MOVE pastoral.cultivationPrimary=" +
            Movement(pastoralCultivation) + " husbandryPrimary=" +
            Movement(pastoralHusbandry));
        var farmerCapacity = Village(0);
        // The ledger fixture bounds growth with the legacy net rate; the physical model's maximum-birth envelope is larger.
        farmerCapacity.Config.DemographicModel = DemographicModel.LegacyQoLNetGrowth;
        farmerCapacity.Config.AnnualHumanGrowthRate = .01145;
        farmerCapacity.Camps[0].PlantFood = 200000;
        farmerCapacity.Camps[0].Inventory.Cloth = 1000;
        var farmerCapacityPlan = Plan(farmerCapacity);
        Console.WriteLine("SETTLEMENT_FARMER_CAPACITY retainedFarmers=" +
            farmerCapacityPlan.Allocation.Farmers + " foodGatherers=" +
            farmerCapacityPlan.Allocation.FoodGatherers + " retainedFood=" +
            farmerCapacityPlan.Allocation.RetainedFoodSupply.ToString("F3") +
            " farmerFood=" + farmerCapacityPlan.Allocation
                .RetainedFarmerFoodService.ToString("F3") +
            " farmerPlant=" + farmerCapacityPlan.Allocation
                .RetainedFarmerPlantService.ToString("F3") +
            " developmentGap=" +
            farmerCapacityPlan.Allocation.DevelopmentFoodGap.ToString("F3") +
            " requests=" + string.Join("|", farmerCapacityPlan.Allocation.Requests
                .Where(q => q.Service.IndexOf("food", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(q => q.Service + ":" + q.Units.ToString("F3") + ":" +
                    q.Granted.ToString("F3"))));
    }

    public static int Run()
    {
        checks = 0;
        TechnologyOnlyInvariance();
        SmallComplementaryHerd();
        OversizedAcquiredHerd();
        GrosslyOversizedHerd();
        LegitimatePastoralMovement();
        PrimaryTechnologyInvariance();
        ShelterCapabilityInvariance();
        ReviewerRegressions();
        DeterminismAndPersistence();
        Console.WriteLine("PASS: " + checks +
            " economic-settlement assertions (A-I)");
        return checks;
    }
}
