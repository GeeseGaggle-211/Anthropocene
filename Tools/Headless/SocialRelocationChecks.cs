using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using Anthropocene.Simulation;

// Unified Social Relocation v0.2: pure acceptance equations, sustainable-service
// inputs, neutral future-opportunity extension, nonmonotone group sizes and
// canonical capacity reconciliation.
internal static class SocialRelocationChecks
{
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception("Social relocation: " + message); }
    private static void Near(double actual, double expected, string message, double tolerance = 1e-10)
        => Check(Math.Abs(actual - expected) <= tolerance * Math.Max(1, Math.Abs(expected)), message + " (" + actual + " vs " + expected + ")");

    private sealed class RecordsProvider : IEmploymentOpportunityProvider
    {
        private readonly Func<EmploymentOpportunityRequest, IReadOnlyList<EmploymentOpportunityRecord>> records;
        public int Calls, CallsWithReservations;
        public RecordsProvider(Func<EmploymentOpportunityRequest, IReadOnlyList<EmploymentOpportunityRecord>> records)
        { this.records = records; }
        public IReadOnlyList<EmploymentOpportunityRecord> Opportunities(EmploymentOpportunityRequest request)
        {
            Calls++; if (request.PendingReservations.Values.Sum() > 0) CallsWithReservations++;
            return records(request);
        }
    }

    private static SocialServiceForecast Envelope(int camp, int population, double q, double risk,
        double years = 20, double birth = .03, double death = .02)
        => new SocialServiceForecast(camp, population, years, population * years, population * years, 1,
            population * years, population * years, 1, population, 1, 1, q, risk,
            population * years, population * Math.Exp((birth - death) * years), birth, death,
            death, 0, 0, 0, population * years * death, 0, 0, 0,
            population * years, 0, 0, 1, 1, 1, 1, 1, 1,
            SocialForecastConfidence.LocalPhysicalCapacity | SocialForecastConfidence.WorkforceBound);

    private static WorldState World(int camps)
    {
        var cfg = SimulationConfig.Small(camps);
        cfg.DemographicModel = DemographicModel.PhysicalMortalityV1;
        cfg.Width = 24; cfg.Height = 16; cfg.ImmigrationEnabled = true; cfg.FissionEnabled = false;
        cfg.GatherPerPerson = 0; cfg.LandUseDamagePerYear = 0; cfg.ConditionRecoveryPerYear = 0;
        cfg.Neolithic.CohesionBandScale = 2000; cfg.Neolithic.CohesionHousingGain = 0;
        var w = new WorldState { Seed = 818, Config = cfg, Generation = new GenerationReport(), Tiles = new Tile[cfg.Width * cfg.Height] };
        for (int i = 0; i < w.Tiles.Length; i++)
        {
            var tile = new Tile { Id = i, ElevationLevel = 1, Terrain = Terrain.Grassland,
                FreshwaterSource = FreshwaterSource.RainPool, Freshwater = 100, Food = 1000, PotentialFood = 8000 };
            for (int s = 0; s < 4; s++) tile.Seasons[s] = new SeasonData { Temperature = 18,
                Moisture = .55f, RegionalMoisture = .5f, MaxFood = 1000, MaxPotentialFood = 8000,
                MaxFreshwater = 100, FoodRecoveryPerDay = .004f, BiomassRecoveryPerDay = .004f,
                WaterRecoveryPerDay = .004f, Habitat = .6f };
            w.Tiles[i] = tile;
        }
        int center = w.Map.At(10, 8);
        var positions = w.Map.Radius(center, 2).Where(t => w.Map.Distance(center, t) == 2).OrderBy(t => t).ToArray();
        for (int i = 0; i < camps; i++)
        {
            int population = 100;
            var camp = new Camp { Id = w.NextId++, Player = i, Tile = i == camps - 1 ? center : positions[i],
                Population = population, RudimentaryExpectation = .5, WaterNeedsMetRatio = 1 };
            camp.PlantFood = population * cfg.Consumption * cfg.GenerationIntervals;
            camp.Inventory.Cloth = 1000; camp.Inventory.LightHousingUnits = 1e9;
            w.Camps.Add(camp);
        }
        w.Config.Players = camps; w.PlayerCount = camps; w.Turn = 7; w.Days = 1000;
        Ownership.Bind(w); foreach (var tile in w.Tiles) tile.LastEcologyUpdateDays = w.Days;
        return w;
    }

    private static TurnRecord ResolveImmigration(WorldState w, IEmploymentOpportunityProvider provider = null)
    {
        var record = new TurnRecord { Turn = w.Turn };
        foreach (var c in w.Camps) record.Demographics.Add(new DemographicReport
            { Camp = c.Id, StartingPopulation = c.Population, EndingPopulation = c.Population });
        Immigration.Resolve(w, record, new PhaseExecution(), new ResolutionMetrics(), provider);
        return record;
    }

    public static int Run()
    {
        checks = 0;
        CommonEquation(); OpportunityContract(); SustainableForecast(); FiniteServiceEnvelope();
        SharedDensity(); FiniteMaterialEnvelope(); DaughterFoodEnvelope(); DaughterCapacityOrder();
        HerdCapacitySplit(); OpportunityLaborClosure(); NeutralParity();
        NonmonotoneSizing(); ReservationRevalidation(); PendingBaselineRevalidation();
        WorkforceAccounting(); PersistenceSurface();
        Console.WriteLine("PASS: " + checks + " social relocation assertions"); return checks;
    }

    private static void MakeEconomic(Camp camp)
    {
        camp.Technology = Technology.Cultivation;
        camp.Technologies[(int)Technology.Cultivation] = true;
        camp.People.Progression.Era = Era.Neolithic;
        foreach (string technology in NeolithicTech.FirstTier)
            if (!camp.People.NeolithicTechnologies.Contains(technology))
                camp.People.NeolithicTechnologies.Add(technology);
    }

    private static void CommonEquation()
    {
        var source0 = Envelope(1, 100, .40, .20);
        var source1 = Envelope(1, 90, .43, .18);
        var destination0 = Envelope(2, 100, .50, .20);
        var destination1 = Envelope(2, 110, .57, .21);
        var policy = new SocialRelocationPolicy(.075, 0, .05, .05, privateCost: .01);
        var result = SocialRelocationEvaluator.Evaluate(SocialRelocationDestination.ExistingSettlement,
            source0, source1, destination0, destination1, policy);
        Near(result.MoverUtilityChange, (.57 - .21) - (.40 - .20) - .01, "mover compares destination X1 with source X0 once");
        Near(result.SourceUtilityChange, (.43 - .18) - (.40 - .20), "source stayers compare their own X1 and X0");
        Near(result.IncumbentUtilityChange, (.57 - .21) - (.50 - .20), "receiver incumbents compare their own X1 and X0");
        Check(result.Accepted && result.MoverPass && result.AggregatePass && result.SourcePass && result.IncumbentPass,
            "one shared four-part acceptance rule admits a funded positive move");

        var harmed = Envelope(2, 110, .20, .30);
        var incumbentVeto = SocialRelocationEvaluator.Evaluate(SocialRelocationDestination.ExistingSettlement,
            source0, source1, destination0, harmed, new SocialRelocationPolicy(-1, -1, 1, .05));
        Check(!incumbentVeto.Accepted && !incumbentVeto.IncumbentPass,
            "an existing receiver cannot externalize excessive harm onto incumbents");
        var daughter = SocialRelocationEvaluator.Evaluate(SocialRelocationDestination.NewDaughter,
            source0, source1, SocialServiceForecast.Empty(3, 20), harmed,
            new SocialRelocationPolicy(-1, -1, 1, .05));
        Check(daughter.IncumbentPass, "a new daughter correctly omits the nonexistent incumbent term");

        var negativeNetDestination = Envelope(4, 10, .85, .08, birth: .01, death: .02);
        var noNetVeto = SocialRelocationEvaluator.Evaluate(SocialRelocationDestination.NewDaughter,
            Envelope(1, 100, .30, .20), Envelope(1, 90, .31, .19), SocialServiceForecast.Empty(4, 20),
            negativeNetDestination, new SocialRelocationPolicy(.05, -.2, .1, .1));
        Check(negativeNetDestination.BirthRate < negativeNetDestination.MortalityRate && noNetVeto.Accepted,
            "negative instantaneous net growth is not a categorical admission veto");
    }

    private static void OpportunityContract()
    {
        var skills = new Dictionary<string, int> { [WorkforceRoles.Weaver] = 3, [WorkforceRoles.Builder] = 2 };
        var request = new EmploymentOpportunityRequest(1, 2, 5, SocialRelocationDestination.ExistingSettlement, skills);
        EmploymentOpportunityRecord Record(int demand = 5, int funding = 5, int tools = 5, int inputs = 5,
            int workplace = 5, int filled = 0, string role = WorkforceRoles.Weaver)
            => new EmploymentOpportunityRecord(role, demand, funding, tools, inputs, workplace, filled,
                preferencePerWorker: .2, foodPerWorker: 4, clothingPerWorker: 2,
                shelterPeoplePerWorker: 1, verifiedSurplusPerWorker: 3);
        var funded = new RecordsProvider(_ => new[] { Record() });
        var match = EmploymentOpportunityMatcher.Match(funded, request);
        Check(match.Matched == 3 && match.Reservations[WorkforceRoles.Weaver] == 3,
            "funded work is bounded by qualified workers and one match per migrant");
        Near(match.Food, 12, "matched work exposes only its verified physical food output");
        Near(match.Clothing, 6, "matched work exposes only its verified clothing output");
        Near(match.VerifiedSurplus, 9, "verified outside surplus is counted once");

        foreach (var provider in new IEmploymentOpportunityProvider[]
        {
            new RecordsProvider(_ => new[] { Record(funding: 0) }),
            new RecordsProvider(_ => new[] { Record(tools: 0) }),
            new RecordsProvider(_ => new[] { Record(inputs: 0) }),
            new RecordsProvider(_ => new[] { Record(workplace: 0) }),
            new RecordsProvider(_ => new[] { Record(filled: 5) }),
            new RecordsProvider(_ => new[] { Record(role: WorkforceRoles.Potter) })
        })
            Check(EmploymentOpportunityMatcher.Match(provider, request).Matched == 0,
                "unfunded, infeasible, filled or wrong-skill paper vacancies have zero opportunity");

        var pending = new EmploymentOpportunityRequest(1, 2, 5, SocialRelocationDestination.ExistingSettlement,
            skills, new Dictionary<string, int> { [WorkforceRoles.Weaver] = 4 });
        Check(EmploymentOpportunityMatcher.Match(funded, pending).Matched == 1,
            "available slots decline after canonical reservations");
        var absent = EmploymentOpportunityMatcher.Match(null, request);
        var neutral = EmploymentOpportunityMatcher.Match(NeutralEmploymentOpportunityProvider.Instance, request);
        Check(absent.Matched == neutral.Matched && absent.Preference == neutral.Preference
            && absent.Food == neutral.Food && absent.VerifiedSurplus == neutral.VerifiedSurplus,
            "missing and explicit neutral opportunity providers are exactly equivalent");
    }

    private static void SustainableForecast()
    {
        var w = World(2); var a = w.Camps[0]; var b = w.Camps[1];
        a.FoodNeedsMetRatio = .02; a.ClothPerCapita = .01; a.QoL = .123;
        b.FoodNeedsMetRatio = .99; b.ClothPerCapita = .98; b.QoL = .987;
        var cache = new SocialRelocationForecastCache(w, w.Camps);
        var fa = cache.Baseline(a); var fb = cache.Baseline(b);
        Near(fa.Nutrition, fb.Nutrition, "historical food satisfaction is not forecast production capacity");
        Near(fa.ClothingCoverage, fb.ClothingCoverage, "historical clothing coverage is not forecast textile capacity");
        Near(fa.QoL, fb.QoL, "forecast welfare is rebuilt from physical services, not committed Camp.QoL");
        Near(a.QoL, .123, "forecasting does not rewrite experienced source QoL");
        Near(b.QoL, .987, "forecasting does not rewrite experienced destination QoL");
        Check(fa.FoodAvailable > 0 && fa.FoodNeed > 0 && fa.PersonYears > 0
            && fa.Confidence.HasFlag(SocialForecastConfidence.WorkforceBound),
            "the immutable envelope exposes physical budgets, person-years and confidence");

        b.Inventory.Cloth = 0; b.Inventory.LightHousingUnits = 0;
        var cache2 = new SocialRelocationForecastCache(w, w.Camps); var exposed = cache2.Baseline(b);
        Check(exposed.ClothingCoverage < fa.ClothingCoverage && exposed.HousingCoverage < fa.HousingCoverage
            && exposed.ExposureRate > fa.ExposureRate,
            "explicit missing protection raises the existing physical exposure hazard without using QoL");
    }

    private static void FiniteServiceEnvelope()
    {
        var w = World(1); var camp = w.Camps[0];
        camp.Population = 137; camp.PlantFood = 321.5; camp.AnimalFood = 17.25;
        camp.AquaticFood = 0; camp.Inventory.Cloth = 11.75;
        camp.Inventory.AnimalFabric = 2.5; camp.Inventory.LightHousingUnits = 0;
        var forecast = new SocialRelocationForecastCache(w, w.Camps).Baseline(camp);
        double years = w.Config.TurnDays / w.Config.DaysPerYear;
        double personYears = PhysicalDemography.BirthOnlyPersonYears(camp.Population,
            w.Config.BaseBirthRate * PhysicalDemography.FertilityFactor(w, camp), years);
        Near(forecast.FoodNeed, personYears * PhysicalDemography.FoodPerPersonYear(w.Config),
            "food service is budgeted against the birth-only person-year envelope");
        Near(forecast.ClothingNeed, personYears * PhysicalDemography.ClothingPerPersonYear(w.Config),
            "clothing service is budgeted against the same finite person-year envelope");
        Check(forecast.Nutrition * forecast.FoodNeed <= forecast.FoodAvailable + 1e-10,
            "forecast nutrition never spends more food than is physically available");
        Check(forecast.ClothingCoverage * forecast.ClothingNeed <= forecast.ClothingAvailable + 1e-10,
            "forecast protection never spends more textiles than are physically available");
    }

    private static void SharedDensity()
    {
        WorldState CampOnly(int residents)
        {
            var w = World(1); var camp = w.Camps[0]; camp.Population = residents;
            camp.PlantFood = 1e6; camp.Inventory.Cloth = 1e6; camp.Inventory.LightHousingUnits = 1e6;
            return w;
        }
        var detached = CampOnly(100); var source = detached.Camps[0];
        detached.Hunters.Add(new Hunter { Id = detached.NextId++, SourceCamp = source.Id,
            SocietyId = source.SocietyId, Player = source.Player, Tile = detached.Map.Neighbors(source.Tile).First(),
            Population = 50, CarriedFood = 1e12 });
        var linked = CampOnly(150);
        var withHunter = new SocialRelocationForecastCache(detached, detached.Camps).Baseline(source);
        var allInCamp = new SocialRelocationForecastCache(linked, linked.Camps).Baseline(linked.Camps[0]);
        Near(withHunter.CrowdingRate, allInCamp.CrowdingRate,
            "a detached cohort contributes to the source settlement's crowding density");
        Near(withHunter.DiseaseRate, allInCamp.DiseaseRate,
            "a detached cohort contributes to the source settlement's disease density");
        Check(withHunter.FoodAvailable < detached.Hunters[0].CarriedFood * .001,
            "detached carried provisions are not pooled into the camp's local service budget");
    }

    private static void FiniteMaterialEnvelope()
    {
        var w = World(1); var camp = w.Camps[0]; MakeEconomic(camp);
        camp.Population = 30; camp.PlantFood = 1e9; camp.AnimalFood = 1e9;
        camp.Inventory = new CampInventory { HandTools = 1e6, Tools = 1e6 };
        camp.Workforce.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Toolmaker, Population = 10 });
        camp.Workforce.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Builder, Population = 10 });
        camp.Workforce.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Carpenter, Population = 10 });
        camp.FarmToolsTarget = 1000;
        var recipes = NeolithicProduction.Recipes(w.Config);
        var outputs = MaterialEconomy.Forecast(w, camp, recipes, w.Config.GenerationIntervals,
            w.Config.TurnDays / w.Config.DaysPerYear, 1e9, false, camp.Population, false, false,
            camp.PlantFood, camp.AnimalFood, Array.Empty<KnownMaterialSource>(), null, 0, 0, 0);
        Check(outputs[CraftGood.HandTools] == 0 && outputs[CraftGood.Tools] == 0
            && outputs[CraftGood.FarmTools] == 0 && outputs[CraftGood.HousingMaterial] == 0
            && outputs[CraftGood.Housing] == 0,
            "zero Stone/Clay/Wood and no sources cannot manufacture tools or housing");

        camp.PlantFood = camp.AnimalFood = 0; camp.Inventory = new CampInventory { HandTools = 1e6 };
        camp.Workforce = new PopulationLedger();
        camp.Workforce.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Weaver, Population = 15 });
        camp.Workforce.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Builder, Population = 15 });
        var cache = new SocialRelocationForecastCache(w, w.Camps);
        var daughterWorkers = WorkforceTransfer.Copy(camp.Workforce);
        var daughter = cache.Daughter(camp, 30, w.Map.Neighbors(camp.Tile).First(), new CampInventory(),
            daughterWorkers, 0, 0, 0, new SocialNewSiteCapacity(1e9, 0));
        var forecast = cache.Forecast(daughter);
        Check(forecast.FoodAvailable == 0 && forecast.ClothingAvailable == 0 && forecast.ShelterCapacity == 0,
            "an all-specialist daughter gets no synthetic gathering, textile feedstock or shelter construction");
    }

    private static void DaughterCapacityOrder()
    {
        SocialServiceForecast[] Run(bool reverse)
        {
            var w = World(1); w.Config.GatherPerPerson = 100; var parent = w.Camps[0];
            parent.PlantFood = parent.AnimalFood = parent.AquaticFood = 0;
            var cache = new SocialRelocationForecastCache(w, w.Camps);
            int tile = w.Map.Neighbors(parent.Tile).First();
            var small = cache.Daughter(parent, 20, tile, new CampInventory(), new PopulationLedger(), 0, 0, 0,
                new SocialNewSiteCapacity(100, 0));
            var large = cache.Daughter(parent, 30, tile, new CampInventory(), new PopulationLedger(), 0, 0, 0,
                new SocialNewSiteCapacity(500, 0));
            SocialServiceForecast a, b;
            if (reverse) { b = cache.Forecast(large); a = cache.Forecast(small); }
            else { a = cache.Forecast(small); b = cache.Forecast(large); }
            return new[] { a, b };
        }
        var forward = Run(false); var reverse = Run(true);
        Near(forward[0].FoodAvailable, reverse[0].FoodAvailable,
            "daughter forecast caching is invariant to evaluating the smaller candidate first");
        Near(forward[1].FoodAvailable, reverse[1].FoodAvailable,
            "daughter forecast caching is invariant to evaluating the larger candidate first");
        Check(forward[1].FoodAvailable > forward[0].FoodAvailable,
            "candidate-specific new-site food capacity is not reused across daughter sizes");
    }

    private static void DaughterFoodEnvelope()
    {
        // A founding site carries raw, typed ecological supply. The forecast is
        // solely responsible for applying the transferred cohort's access and
        // labor limits, and only satisfaction is capped at one.
        var w = World(1); var parent = w.Camps[0]; int tile = w.Map.Neighbors(parent.Tile).First();
        w.Config.GatherPerPerson = 1000; parent.PlantFood = parent.AnimalFood = parent.AquaticFood = 0;
        var cache = new SocialRelocationForecastCache(w, w.Camps);
        var small = cache.Daughter(parent, 5, tile, new CampInventory(), new PopulationLedger(), 0, 0, 0,
            new SocialNewSiteCapacity(1000, 0));
        var smallForecast = cache.Forecast(small);
        double access = Math.Min(1, Numbers.Fraction(w.Config.GatherFraction, w.Config.GenerationIntervals)
            * 5 / w.Config.PopulationScale);
        double expected = Math.Min(1000 * access,
            5 * w.Config.GatherPerPerson * w.Config.GenerationIntervals);
        Near(smallForecast.PlantFoodProduction, expected,
            "a small daughter's site accessibility is applied exactly once to raw land supply");
        Near(smallForecast.FoodAvailable, expected,
            "typed site production enters the finite food envelope exactly once");

        var rich = cache.Daughter(parent, 20, tile, new CampInventory(), new PopulationLedger(), 0, 0, 0,
            new SocialNewSiteCapacity(10000, 0));
        var richForecast = cache.Forecast(rich);
        Check(richForecast.RenewableFoodRatio > 1 && richForecast.RenewableFoodCoverage == 1
            && richForecast.PlantFoodProduction > 20 * w.Config.Consumption * w.Config.GenerationIntervals,
            "a rich site preserves uncapped production headroom while satisfaction remains capped");
        Near(richForecast.RenewableFoodRatio,
            (richForecast.PlantFoodProduction + richForecast.AnimalFoodProduction
                + richForecast.AquaticFoodProduction) / richForecast.FoodNeed,
            "the uncapped renewable-food diagnostic is production divided by the service need");

        // The deterministic fission split converts transferred farmers to site
        // gatherers while retaining other specialists. The resulting 27-person
        // gathering cohort, not the full daughter headcount, bounds production.
        MakeEconomic(parent); parent.Workforce = new PopulationLedger();
        parent.Workforce.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Farmer, Population = 40 });
        parent.Workforce.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Weaver, Population = 10 });
        var transfer = WorkforceTransfer.Split(parent.Workforce, 100, 30, true);
        var workforceState = cache.Daughter(parent, 30, tile, new CampInventory(), transfer.Movers, 0, 0, 0,
            new SocialNewSiteCapacity(1e9, 0));
        var workforceForecast = cache.Forecast(workforceState);
        Check(transfer.MoverComposition[WorkforceRoles.Gatherer] == 27
            && transfer.Movers.Assignments.All(a => a.Role != WorkforceRoles.Farmer),
            "the exact transferred fission workforce contains 27 site gatherers and no farmers");
        Near(workforceForecast.PlantFoodProduction,
            27 * w.Config.GatherPerPerson * w.Config.GenerationIntervals,
            "new-site production uses the exact transferred gathering workforce");

        // Aquatic food can feed people but cannot become plant-fibre input. A
        // land-only control with the same physical quantity can fund Cloth.
        w.Config.GatherPerPerson = 100; parent.Workforce = new PopulationLedger();
        parent.Workforce.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Weaver, Population = 15 });
        var inventory = new CampInventory { HandTools = 1e6 };
        var aquaticState = cache.Daughter(parent, 30, tile, inventory, parent.Workforce, 0, 0, 0,
            new SocialNewSiteCapacity(0, 1e9, herdForageCoverage: .25, herdWaterCoverage: .5));
        var landState = cache.Daughter(parent, 30, tile, inventory, parent.Workforce, 0, 0, 0,
            new SocialNewSiteCapacity(1e9, 0));
        var aquaticForecast = cache.Forecast(aquaticState); var landForecast = cache.Forecast(landState);
        Check(aquaticForecast.PlantFoodProduction == 0
            && aquaticForecast.AquaticFoodProduction > aquaticForecast.FoodNeed
            && aquaticForecast.ClothingAvailable == 0,
            "aquatic-only site production remains aquatic and cannot manufacture plant cloth");
        Check(landForecast.PlantFoodProduction > landForecast.FoodNeed && landForecast.ClothingAvailable > 0,
            "the matched land-food control can fund plant-feedstock clothing");
        Near(aquaticForecast.HerdForageCoverage, .25,
            "pasture feasibility remains an explicit constraint rather than human plant food");
        Near(aquaticForecast.HerdWaterCoverage, .5,
            "herd water feasibility remains separately diagnosed");
    }

    private static void HerdCapacitySplit()
    {
        WorldState Fixture(bool withHerd)
        {
            var w = World(1); w.Config.GatherPerPerson = 1; var camp = w.Camps[0]; MakeEconomic(camp);
            camp.Technologies[(int)Technology.AnimalHusbandry] = true;
            camp.Inventory.HerdingTools = 1e6; camp.PlantFood = camp.AnimalFood = camp.AquaticFood = 0;
            camp.Workforce.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Herder, Population = 20 });
            if (withHerd) w.Herds.Add(new Herd { Id = w.NextId++, Tile = camp.Tile, Animals = 100,
                Capacity = 200, DomesticCamp = camp.Id, OwnerSociety = camp.SocietyId, Species = "goat" });
            return w;
        }
        var herdWorld = Fixture(true); var camp = herdWorld.Camps[0];
        var cache = new SocialRelocationForecastCache(herdWorld, herdWorld.Camps);
        SocialSettlementState State(double fraction) => SocialRelocationForecastCache.Existing(camp,
            camp.Population, 0, 0, 0, camp.Workforce, managedAnimalFraction: fraction);
        var full = cache.Forecast(State(1)); var half = cache.Forecast(State(.5)); var none = cache.Forecast(State(0));
        var controlWorld = Fixture(false);
        var control = new SocialRelocationForecastCache(controlWorld, controlWorld.Camps).Baseline(controlWorld.Camps[0]);
        Near(none.FoodAvailable, control.FoodAvailable,
            "a source retaining none of a split herd neither keeps its animal output nor pays its forage displacement");
        Check(Math.Abs(full.FoodAvailable - half.FoodAvailable) > 1e-9,
            "the source forecast scales managed-herd capacity with the retained herd fraction");
    }

    private static void OpportunityLaborClosure()
    {
        var w = World(1); w.Config.GatherPerPerson = .01; var camp = w.Camps[0];
        camp.PlantFood = camp.AnimalFood = camp.AquaticFood = 0;
        camp.Workforce.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Builder, Population = 2 });
        var cache = new SocialRelocationForecastCache(w, w.Camps);
        var state = SocialRelocationForecastCache.Existing(camp, camp.Population, 0, 0, 0,
            camp.Workforce, densityPopulationOffset: cache.DensityOffset(camp));
        var baseline = cache.Forecast(state);
        var gatherProvider = new RecordsProvider(_ => new[] { new EmploymentOpportunityRecord(
            WorkforceRoles.Gatherer, 10, 10, 10, 10, 10) });
        var gatherMatch = EmploymentOpportunityMatcher.Match(gatherProvider,
            new EmploymentOpportunityRequest(camp.Id, camp.Id, 10, SocialRelocationDestination.ExistingSettlement,
                new Dictionary<string, int> { [WorkforceRoles.Gatherer] = 10 }));
        var reserved = cache.Forecast(state, gatherMatch);
        Check(reserved.FoodAvailable < baseline.FoodAvailable,
            "a matched gatherer is reserved from ordinary production instead of being double-employed");

        var productiveProvider = new RecordsProvider(_ => new[] { new EmploymentOpportunityRecord(
            WorkforceRoles.Gatherer, 10, 10, 10, 10, 10, foodPerWorker: 5) });
        var productive = EmploymentOpportunityMatcher.Match(productiveProvider,
            new EmploymentOpportunityRequest(camp.Id, camp.Id, 10, SocialRelocationDestination.ExistingSettlement,
                new Dictionary<string, int> { [WorkforceRoles.Gatherer] = 10 }));
        var productiveForecast = cache.Forecast(state, productive);
        Near(productiveForecast.FoodAvailable, reserved.FoodAvailable + 50,
            "verified opportunity output is added once after reserving its workers");

        var builderProvider = new RecordsProvider(_ => new[] { new EmploymentOpportunityRecord(
            WorkforceRoles.Builder, 2, 2, 2, 2, 2, foodPerWorker: 7) });
        var builder = EmploymentOpportunityMatcher.Match(builderProvider,
            new EmploymentOpportunityRequest(camp.Id, camp.Id, 2, SocialRelocationDestination.ExistingSettlement,
                new Dictionary<string, int> { [WorkforceRoles.Builder] = 2 }));
        var combined = EmploymentOpportunityMatch.Combine(productive, builder);
        var combinedForecast = cache.Forecast(state, combined);
        Near(combinedForecast.FoodAvailable, productiveForecast.FoodAvailable + 14,
            "an earlier funded cohort's output persists exactly once when a later opportunity is combined");
    }

    private static void NeutralParity()
    {
        WorldState Fixture()
        {
            var w = World(2); var source = w.Camps[0]; var destination = w.Camps[1];
            source.WaterNeedsMetRatio = .01;
            destination.PlantFood = 12000; destination.Inventory.Cloth = 3000;
            destination.Inventory.LightHousingUnits = 1e9;
            return w;
        }
        var missing = Fixture(); var neutral = Fixture();
        var a = ResolveImmigration(missing, null); var b = ResolveImmigration(neutral, NeutralEmploymentOpportunityProvider.Instance);
        Check(a.Migrations.Count == b.Migrations.Count && a.Migrations.Count == 1,
            "missing and neutral providers select the same physical flow");
        var ma = a.Migrations[0]; var mb = b.Migrations[0];
        Check(ma.SourceCamp == mb.SourceCamp && ma.DestinationCamp == mb.DestinationCamp
            && ma.Migrants == mb.Migrants && BitConverter.DoubleToInt64Bits(ma.MoverUtilityChange) == BitConverter.DoubleToInt64Bits(mb.MoverUtilityChange),
            "neutral-provider decisions are bit-identical");
        Check(missing.Camps.Select(c => c.Population).SequenceEqual(neutral.Camps.Select(c => c.Population))
            && missing.Camps.Select(c => c.Food).SequenceEqual(neutral.Camps.Select(c => c.Food)),
            "neutral-provider authoritative commits are identical");
    }

    private static RecordsProvider FivePersonProductiveProvider(int destination)
        => new RecordsProvider(request => request.DestinationCamp == destination && request.Movers == 5
            ? new[] { new EmploymentOpportunityRecord(WorkforceRoles.Gatherer, 5, 5, 5, 5, 5,
                foodPerWorker: 5000, clothingPerWorker: 500, shelterPeoplePerWorker: 500) }
            : Array.Empty<EmploymentOpportunityRecord>());

    private static void PrepareNonmonotone(WorldState w)
    {
        var destination = w.Camps.Last();
        foreach (var source in w.Camps.Take(w.Camps.Count - 1))
        {
            source.WaterNeedsMetRatio = .35;
            source.PlantFood = source.Population * w.Config.Consumption * w.Config.GenerationIntervals;
            source.Inventory.Cloth = 1000; source.Inventory.LightHousingUnits = 1e9;
        }
        destination.PlantFood = destination.AnimalFood = destination.AquaticFood = 0;
        destination.Inventory.Cloth = destination.Inventory.AnimalFabric = destination.Inventory.LightHousingUnits = 0;
        destination.RudimentaryExpectation = .8;
    }

    private static void NonmonotoneSizing()
    {
        var w = World(2); PrepareNonmonotone(w); var provider = FivePersonProductiveProvider(w.Camps.Last().Id);
        var sizes = Immigration.CandidateSizes(w.Config, w.Camps[0].Population);
        Check(sizes.SequenceEqual(new[] { 4, 5, 7, 10 }), "bounded shortlist contains minimum and 2.5/5/7.5/10-percent sizes without exceeding the cap");
        var record = ResolveImmigration(w, provider);
        Check(record.Migrations.Count == 1 && record.Migrations[0].Migrants == 5,
            "a supported smaller group is found even when the ten-percent group is unsupported (flows="
            + record.Migrations.Count + ", migrants=" + string.Join(",", record.Migrations.Select(m => m.Migrants))
            + ", calls=" + provider.Calls + ")");
        Check(record.Migrations[0].MatchedOpportunities == 5,
            "the selected nonmonotone group records only matched credible work");
    }

    private static void ReservationRevalidation()
    {
        var w = World(3); PrepareNonmonotone(w); var provider = FivePersonProductiveProvider(w.Camps.Last().Id);
        var record = ResolveImmigration(w, provider);
        Check(record.Migrations.Count >= 1 && record.Migrations.Sum(m => m.MatchedOpportunities) == 5
            && record.Migrations.Count(m => m.MatchedOpportunities > 0) == 1,
            "one five-slot receiver reserves exactly one canonical cohort even when its real output can support another flow (flows="
            + record.Migrations.Count + ", matched=" + string.Join(",", record.Migrations.Select(m => m.MatchedOpportunities)) + ")");
        Check(provider.CallsWithReservations > 0,
            "later pending inflows are rescored after opportunity reservations");
    }

    private static void PendingBaselineRevalidation()
    {
        var w = World(3); PrepareNonmonotone(w); int destination = w.Camps.Last().Id;
        var provider = new RecordsProvider(request => request.DestinationCamp == destination && request.Movers == 5
            ? new[] { new EmploymentOpportunityRecord(WorkforceRoles.Gatherer, 10, 10, 10, 10, 10,
                foodPerWorker: 5000, clothingPerWorker: 500, shelterPeoplePerWorker: 500) }
            : Array.Empty<EmploymentOpportunityRecord>());
        TurnRecord record = ResolveImmigration(w, provider);
        Check(record.Migrations.Count == 2 && record.Migrations.All(m => m.Migrants == 5),
            "two physically funded five-person cohorts can share a ten-slot receiver");
        MigrationReport first = record.Migrations[0], second = record.Migrations[1];
        Check(first.IncumbentUtilityChange >= -w.Config.FissionDaughterMargin - 1e-12
            && second.IncumbentUtilityChange >= -w.Config.FissionDaughterMargin - 1e-12,
            "joint revalidation retains the incumbent safeguard for both accepted cohorts");
        Check(first.AggregateWelfareChange >= -1e-12 && second.AggregateWelfareChange >= -1e-12,
            "joint revalidation retains the common aggregate-welfare constraint for every cohort");
        Check(provider.CallsWithReservations > 0,
            "the later cohort sees both the pending physical state and reserved opportunity slots");
    }

    private static void WorkforceAccounting()
    {
        var ledger = new PopulationLedger();
        ledger.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Farmer, Population = 40 });
        ledger.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Weaver, Population = 10 });
        var move = WorkforceTransfer.Split(ledger, 100, 30);
        Check(move.Stayers.Assignments.Single(a => a.Role == WorkforceRoles.Farmer).Population == 28
            && move.Movers.Assignments.Single(a => a.Role == WorkforceRoles.Farmer).Population == 12,
            "immigration splits assigned roles by deterministic largest remainder");
        Check(move.Stayers.Assigned + move.Movers.Assigned == ledger.Assigned
            && move.MoverComposition.Values.Sum() == 30,
            "assigned roles and the complete mover cohort are conserved");
        var daughter = WorkforceTransfer.Split(ledger, 100, 30, true);
        Check(daughter.Movers.Assignments.All(a => a.Role != WorkforceRoles.Farmer)
            && daughter.MoverComposition[WorkforceRoles.Gatherer] == 27,
            "fission retains the existing rule that daughter farmers become site gatherers");
    }

    private static void PersistenceSurface()
    {
        var w = World(2);
        var serializer = new DataContractSerializer(typeof(WorldState));
        string xml;
        using (var stream = new MemoryStream())
        {
            serializer.WriteObject(stream, w); xml = Encoding.UTF8.GetString(stream.ToArray());
        }
        Check(!xml.Contains(nameof(IEmploymentOpportunityProvider))
            && !xml.Contains(nameof(NeutralEmploymentOpportunityProvider))
            && !xml.Contains(nameof(SocialRelocationForecastCache)),
            "opportunity providers and relocation caches add no authoritative save state");
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        var back = (WorldState)serializer.ReadObject(input);
        Check(back.Config.DemographicModel == DemographicModel.PhysicalMortalityV1
            && back.Camps.Select(c => c.Population).SequenceEqual(w.Camps.Select(c => c.Population)),
            "ordinary physical worlds round-trip without a social schema migration");
    }
}
