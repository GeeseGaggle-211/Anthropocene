using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using Anthropocene.Simulation;

// DemandControl slice 1 (Docs/EconomicControlContract.md with Addendum A): the strategy selector, purity, legal intents,
// the §9 Housing reference case, saturated budgets, ceilings honoured by forecast and resolver, determinism and saves.
// Owns its worlds and counters (lanes run checks concurrently).
internal static class DemandControlChecks
{
    private static int checks;
    private static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception("DemandControl: " + message); }
    private static void Near(double a, double b, string message, double tolerance = 1e-9) => Check(Math.Abs(a - b) <= tolerance * Math.Max(1, Math.Abs(b)), message + " (" + a + " vs " + b + ")");

    private static WorldState World(string strategy, params (int x, int y, int population)[] camps)
    {
        var cfg = SimulationConfig.Small(1); cfg.DemographicModel = DemographicModel.LegacyQoLNetGrowth; cfg.EconomicAllocation = strategy; cfg.Width = 24; cfg.Height = 16; cfg.AnnualHumanGrowthRate = 0; cfg.LandUseDamagePerYear = 0; cfg.ConditionRecoveryPerYear = 0;
        var w = new WorldState { Seed = 404, Config = cfg, Generation = new GenerationReport(), Tiles = new Tile[cfg.Width * cfg.Height] };
        for (int i = 0; i < w.Tiles.Length; i++)
        {
            var t = new Tile { Id = i, ElevationLevel = 1, Terrain = Terrain.Grassland, FreshwaterSource = FreshwaterSource.RainPool, Freshwater = 100, PotentialFood = 8000, Food = 1000 };
            for (int s = 0; s < 4; s++) t.Seasons[s] = new SeasonData { Temperature = 18, Moisture = .55f, RegionalMoisture = .5f, MaxFood = 1000, MaxPotentialFood = 8000, MaxFreshwater = 100, FoodRecoveryPerDay = .004f, BiomassRecoveryPerDay = .004f, WaterRecoveryPerDay = .004f, Habitat = .6f };
            w.Tiles[i] = t;
        }
        w.Camps.Add(new Camp { Id = w.NextId++, Player = 0, Tile = w.Map.At(20, 13), Population = 40, PlantFood = 6000, AnimalFood = 1000, RudimentaryExpectation = .5 });
        for (int i = 0; i < camps.Length; i++)
        {
            var (x, y, population) = camps[i];
            var c = new Camp { Id = w.NextId++, Player = i + 1, Tile = w.Map.At(x, y), Population = population, PlantFood = 400 * population, AnimalFood = 50 * population, RudimentaryExpectation = .5, StationaryYears = 40, StationaryTurns = 2, IsVillage = true, EconomyAutomationTurns = -1 };
            c.Inventory.HandTools = 2 * population; c.Inventory.Wood = 5 * population; c.Inventory.Stone = 5 * population; c.Inventory.Clay = 5 * population;
            w.Camps.Add(c);
        }
        w.Config.Players = camps.Length + 1; w.PlayerCount = camps.Length + 1; w.Turn = 3; w.Days = 1000;
        Ownership.Bind(w);
        foreach (var c in w.Camps)
        {
            c.Technology = Technology.Cultivation; c.Technologies[(int)Technology.Cultivation] = true; c.People.Progression.Era = Era.Neolithic;
            c.People.NeolithicTechnologies.Add(NeolithicTech.Construction);
        }
        foreach (var t in w.Tiles) t.LastEcologyUpdateDays = w.Days;
        return w;
    }
    private static Camp CampOf(WorldState w, int player) => w.Camps.Single(c => c.Player == player);
    private static double Years(WorldState w) => w.Config.TurnDays / w.Config.DaysPerYear;
    private static EconomicIntent Plan(WorldState w, Camp c, out ProductionOrder order, out DemandController.Allocation allocation)
        => DemandController.Plan(w, c, EconomicIntent.From(c), out order, out allocation);
    private static EconomicConstraints Constraints(WorldState w, Camp c) => new EconomicConstraints
    {
        Agriculture = CultivationEconomy.Enabled(c), Husbandry = CultivationEconomy.HusbandryEnabled(c), FishingEnabled = NeolithicCapabilities.FeatureFishingEnabled(w, c), Population = c.Population,
        Technologies = c.People.NeolithicTechnologies.ToArray(), FocusTiles = CultivationEconomy.Footprint(w, c).ToArray()
    };

    public static int Run()
    {
        checks = 0; Config(); Purity(); ReserveFeedstockClosure(); IncomingFeedstockToolClosure(); HousingReference(); RawMaterialContinuation();
        TypedFeedstockPriority(); HousingMaterialCeiling(); Saturation(); FishingIntegration(); FishingMealBreakpoint(); Turns();
        Console.WriteLine("PASS: " + checks + " demand-control assertions"); return checks;
    }

    // The operating food reserve and the finished-clothing buffer cannot spend
    // the same Plant Food.  Current clothing may displace reserve refill, but
    // reserve clothing is normal manufacturing and must use only food beyond
    // meals plus the attainable reserve target.  Forecast and resolution must
    // therefore close on the same Cloth quantity.
    private static void ReserveFeedstockClosure()
    {
        var w = World(EconomicAllocationStrategy.DemandControl, (6, 6, 100)); var c = CampOf(w, 1);
        w.Config.DemographicModel = DemographicModel.PhysicalMortalityV1;
        c.People.NeolithicTechnologies.Add(NeolithicTech.Weaving);
        c.Inventory.Cloth = c.Inventory.AnimalFabric = 0;
        c.Inventory.Pots = 1000000;
        foreach (var tile in w.Tiles)
        {
            tile.Food = tile.PotentialFood = 0;
            for (int season = 0; season < tile.Seasons.Length; season++)
            {
                SeasonData profile = tile.Seasons[season];
                profile.MaxFood = profile.MaxPotentialFood = 0;
                tile.Seasons[season] = profile;
            }
        }
        double intervals = w.Config.GenerationIntervals;
        double meals = c.Population * w.Config.Consumption * intervals;
        double reserve = NeolithicProduction.DesiredReserveFood(w, c.Population, intervals);
        c.PlantFood = meals + reserve; c.AnimalFood = c.AquaticFood = 0;
        NeolithicProduction.AddFixedHousing(w, c, 1000, w.Days);

        Plan(w, c, out _, out var allocation);
        var current = allocation.Requests.Single(q => q.Service == "clothing");
        var buffer = allocation.Requests.Single(q => q.Service == "clothing reserve");
        Check(allocation.PlantEnvelope <= 1e-8 && allocation.PlantEnvelopeClothing > 0,
            "normal Plant feedstock preserves the attainable reserve while current clothing may precede it");
        Check(current.Granted > 0 && Math.Abs(current.Granted - current.Units) <= 1e-8,
            "current clothing remains fully funded ahead of reserve refill");
        Check(buffer.Units > 0 && buffer.Granted <= 1e-8 && buffer.Unmet == "plant feedstock",
            "reserve clothing cannot spend the operating food reserve twice");

        var prepared = TurnPlanner.Prepare(w, new Dictionary<int, List<Order>>());
        double planned = prepared.Needs[c.Id].Production[CraftGood.Cloth];
        TurnRecord record = new TurnResolver().Resolve(w, prepared);
        double actual = record.Production.Where(p => p.Camp == c.Id && p.Good == CraftGood.Cloth).Sum(p => p.Output);
        Near(actual, planned, "reserve-safe Cloth forecast equals authoritative production", 1e-7);
    }

    // DemandControl admits current-clothing feedstock that will be gathered in
    // this turn.  Its tool input must be sized from that admitted work, not from
    // the opening Plant Food stock; otherwise the forecast closes a large Cloth
    // grant through only the few tools needed by the opening stock.
    private static void IncomingFeedstockToolClosure()
    {
        var w = World(EconomicAllocationStrategy.DemandControl, (6, 6, 100)); var c = CampOf(w, 1);
        c.People.NeolithicTechnologies.Add(NeolithicTech.Toolmaking);
        c.People.NeolithicTechnologies.Add(NeolithicTech.Weaving);
        c.Inventory.HandTools = c.Inventory.Tools = 0;
        c.Inventory.Cloth = c.Inventory.AnimalFabric = 0;
        c.PlantFood = c.AnimalFood = 0;
        // Non-plant stock pays meals/reserve while local land supplies the
        // same-turn textile input. Plant Food therefore opens at exactly zero
        // without making the complete service physically infeasible.
        c.AquaticFood = 20000;
        NeolithicProduction.AddFixedHousing(w, c, 1000, w.Days);
        var intent = Plan(w, c, out var order, out var allocation);
        var clothing = allocation.Requests.Where(q => q.Service == "clothing" && q.Good == CraftGood.Cloth).ToArray();
        double allocatedCloth = clothing.Sum(q => q.Granted);
        double allocatedToolOutputs = order.Ceiling(CraftGood.HandTools)
            + order.Ceiling(CraftGood.Tools) * ToolCoverageBudget.OutputsPerTool;
        Check(allocatedCloth > 0 && allocatedToolOutputs + 1e-8 >= allocatedCloth,
            "fixture allocates incoming-feedstock Cloth with its complete tool service");

        var candidate = EconomicPlanner.Preview(c, intent, c.Population);
        var recipes = NeolithicProduction.Recipes(w.Config);
        var forecast = MaterialEconomy.Forecast(w, candidate, recipes, w.Config.GenerationIntervals,
            Years(w), new ToolCoverageBudget(w, candidate, w.Config.GenerationIntervals).Remaining,
            true, c.Population, false, false, knownSources: MaterialEconomy.KnownSources(w, candidate, w.Days), order: order);
        double forecastToolOutputs = forecast[CraftGood.HandTools]
            + forecast[CraftGood.Tools] * ToolCoverageBudget.OutputsPerTool;
        Check(forecastToolOutputs > 0 && forecast[CraftGood.Cloth] > 0,
            "the ceiling-bearing forecast makes tools and Cloth despite zero opening feedstock");
        Check(forecast[CraftGood.Cloth] <= forecastToolOutputs + 1e-8,
            "forecast Cloth remains bounded by realized same-turn tool service");
        Check(forecast[CraftGood.HandTools] <= order.Ceiling(CraftGood.HandTools) + 1e-8
            && forecast[CraftGood.Tools] <= order.Ceiling(CraftGood.Tools) + 1e-8,
            "forecast tool output remains bounded by the Elder allocation");

        var prepared = TurnPlanner.Prepare(w, new Dictionary<int, List<Order>>());
        double plannedCloth = prepared.Needs[c.Id].Production[CraftGood.Cloth];
        TurnRecord record = new TurnResolver().Resolve(w, prepared);
        double actualCloth = record.Production.Where(p => p.Camp == c.Id && p.Good == CraftGood.Cloth).Sum(p => p.Output);
        double actualToolOutputs = record.Production.Where(p => p.Camp == c.Id && p.Good == CraftGood.HandTools).Sum(p => p.Output)
            + record.Production.Where(p => p.Camp == c.Id && p.Good == CraftGood.Tools).Sum(p => p.Output)
                * ToolCoverageBudget.OutputsPerTool;
        Check(plannedCloth > 0 && actualCloth > 0,
            "the real gather-production path bootstraps tools and realizes incoming-feedstock Cloth"
                + " (planned=" + plannedCloth + ", actual=" + actualCloth
                + ", tools=" + actualToolOutputs + ", food=" + c.Food + ")");
        Check(actualCloth <= actualToolOutputs + 1e-8,
            "realized Cloth cannot exceed the same-turn tools physically made in the zero-opening-tool fixture");
    }

    // Current clothing may use typed feedstock before the operating reserve is
    // full. That exception belongs to the clothing service, not to every bundle:
    // a dual-capability camp whose clothing is already stocked cannot promise
    // tent fabric from the same reserve food.
    private static void TypedFeedstockPriority()
    {
        var w = World(EconomicAllocationStrategy.DemandControl, (6, 6, 100)); var c = CampOf(w, 1);
        c.Technologies[(int)Technology.AnimalHusbandry] = true;
        c.People.NeolithicTechnologies.Add(NeolithicTech.Weaving);
        c.Inventory.Cloth = 10000;
        foreach (var tile in w.Tiles)
        {
            tile.Food = tile.PotentialFood = 0;
            for (int season = 0; season < tile.Seasons.Length; season++)
            {
                SeasonData profile = tile.Seasons[season];
                profile.MaxFood = profile.MaxPotentialFood = 0;
                tile.Seasons[season] = profile;
            }
        }
        double meals = c.Population * w.Config.Consumption * w.Config.GenerationIntervals;
        c.PlantFood = 0; c.AquaticFood = 0; c.AnimalFood = meals + 1000;
        Plan(w, c, out var order, out var allocation);
        var tents = allocation.Requests.Where(q => q.Good == CraftGood.LightHousing).ToArray();
        Check(allocation.AnimalEnvelope <= 1e-9 && allocation.AnimalEnvelopeClothing > 1e-9,
            "fixture has Animal Food only in the before-reserve clothing envelope");
        Check(tents.All(q => q.Granted <= 1e-9 && q.Unmet == "animal feedstock")
            && order.Ceiling(CraftGood.LightHousing) <= 1e-9,
            "tent feedstock cannot borrow the current-clothing reserve exception: "
                + string.Join(";", tents.Select(q => q.Service + "=" + q.Units + "/" + q.Granted + "/" + q.Unmet))
                + " ceiling=" + order.Ceiling(CraftGood.LightHousing));
        var fixedFallback = allocation.Requests.Where(q => q.Service == "shelter deficit"
            && q.Good == CraftGood.Housing).ToArray();
        Check(fixedFallback.Length == 1 && fixedFallback[0].Granted > 0
            && order.Ceiling(CraftGood.Housing) > 0,
            "an unfundable tent route yields to fixed Housing");
    }

    private static void HousingMaterialCeiling()
    {
        var w = World(EconomicAllocationStrategy.DemandControl, (6, 6, 100)); var c = CampOf(w, 1);
        c.Workforce.Assign(WorkforceRoles.Builder, 10, c.Population);
        c.Inventory.HandTools = 10000; c.Inventory.Wood = 10000; c.Inventory.Clay = 10000;
        var recipes = NeolithicProduction.Recipes(w.Config);
        var ceilings = recipes.ToDictionary(r => r.Good, r => 0d);
        ceilings[CraftGood.HousingMaterial] = .25;
        ceilings[CraftGood.Housing] = .25;
        var order = new ProductionOrder(new[] { CraftGood.HousingMaterial, CraftGood.Housing }, ceilings);
        var forecast = MaterialEconomy.Forecast(w, c, recipes, w.Config.GenerationIntervals,
            Years(w), 10000, true, c.Population, false, false, knownSources: null, order: order);
        Check(forecast[CraftGood.HousingMaterial] <= .25 + 1e-9,
            "Housing Material forecast retains the Elder's output ceiling");
    }

    private static string FindConfig()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            string path = Path.Combine(d.FullName, "Assets", "_Anthropocene", "Resources", "OpeningConfig.json");
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException("OpeningConfig.json");
    }

    private static void Config()
    {
        Check(new SimulationConfig().EconomicAllocation == EconomicAllocationStrategy.DemandControl, "DemandControl is the default strategy (plan of record)");
        var bad = new SimulationConfig { EconomicAllocation = "Optimal" }; bool rejected = false; try { bad.Validate(); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "an unknown strategy is rejected");
        var serializer = new DataContractSerializer(typeof(SimulationConfig)); string xml;
        using (var stream = new MemoryStream()) { serializer.WriteObject(stream, new SimulationConfig { EconomicAllocation = EconomicAllocationStrategy.DemandControl }); xml = Encoding.UTF8.GetString(stream.ToArray()); }
        Check(xml.Contains("<EconomicAllocation>DemandControl</EconomicAllocation>"), "the strategy is serialized");
        string legacy = Regex.Replace(xml, "<EconomicAllocation>[^<]*</EconomicAllocation>", "");
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(legacy))) Check(((SimulationConfig)serializer.ReadObject(stream)).EconomicAllocation == EconomicAllocationStrategy.Legacy, "an older config loads Legacy");
        var m = Regex.Match(File.ReadAllText(FindConfig()), "\"EconomicAllocation\":\\s*\"([A-Za-z]+)\"");
        Check(m.Success && m.Groups[1].Value == "DemandControl", "OpeningConfig.json carries DemandControl");
    }

    // Pure: planning reads the committed camp and writes nothing; the intent validates.
    private static void Purity()
    {
        var w = World(EconomicAllocationStrategy.DemandControl, (6, 6, 120), (12, 6, 60)); var c = CampOf(w, 1);
        string before = SaveCodec.Fingerprint(w);
        var intent = Plan(w, c, out var order, out var allocation);
        Check(SaveCodec.Fingerprint(w) == before, "planning changes no committed state");
        Check(EconomicIntents.Validate(Constraints(w, c), intent) == null, "the intent is legal: " + EconomicIntents.Validate(Constraints(w, c), intent));
        Check(intent.Assignments.Sum(a => a.Population) + intent.Labor.MaterialGatherers <= c.Population + 1e-9, "staffing fits the residents");
        Check(order != null && order.HasCeilings && NeolithicProduction.Recipes(w.Config).All(r => order.Ceiling(r.Good) >= 0 && !double.IsInfinity(order.Ceiling(r.Good))), "every good has a finite ceiling");
        Check(allocation.Requests.Any(q => q.Service == "clothing") && allocation.Requests.All(q => q.Granted <= q.Units + 1e-9), "requests are made and never over-granted");
        var again = Plan(w, c, out var order2, out _);
        Check(again.Stamp == intent.Stamp && order2.ToString() == order.ToString(), "planning is deterministic");
        // Legacy dispatch is the Value/Uniform planner exactly.
        var lw = World(EconomicAllocationStrategy.Legacy, (6, 6, 120)); var lc = CampOf(lw, 1);
        var viaStrategy = EconomicAllocationStrategy.Plan(lw, lc, EconomicIntent.From(lc), out var legacyOrder);
        var direct = EconomicPlanner.Plan(lw, lc, EconomicIntent.From(lc), EconomicPlanner.ConfiguredRule(lw.Config), out _, out var directOrder);
        Check(viaStrategy.Stamp == direct.Stamp && (legacyOrder?.ToString() ?? "") == (directOrder?.ToString() ?? "") && !(legacyOrder?.HasCeilings ?? false), "Legacy dispatch equals the Value/Uniform planner and sets no ceilings");
    }

    // v0.1 §9: 480 residents, 120 fixed Housing units, 10% decay per reference turn: 12 units of recurring replacement, about
    // 4.8 Builder-equivalents compiled to 5 Builders at the unscaled specialist/hand-tool baseline, delivered next turn.
    private static void HousingReference()
    {
        var w = World(EconomicAllocationStrategy.DemandControl, (6, 6, 480)); var cfg = w.Config; var n = cfg.Neolithic; var c = CampOf(w, 1);
        NeolithicProduction.AddFixedHousing(w, c, 120, w.Days);
        c.Inventory.Cloth = 2000; // clothing met: shelter is the work
        Check(Math.Abs(Years(w) - n.ReferenceTurnYears) < 1e-9 && Math.Abs(n.HousingDecayPerTurn - .10) < 1e-12 && n.HousingPeoplePerUnit == 4, "fixture: the reference turn, decay and housing size");
        var intent = Plan(w, c, out var order, out var allocation);
        var replacement = allocation.Requests.Single(q => q.Service == "shelter replacement");
        Near(replacement.Units, 12, "12 units of replacement are requested", 1e-6);
        Near(replacement.Granted, 12, "with feasible inputs the replacement is granted in full", 1e-6);
        Check(!allocation.Requests.Any(q => q.Service == "shelter deficit" && q.Units > 1e-6), "with a constant cohort there is no deficit beyond replacement");
        Check(allocation.Staff.TryGetValue(WorkforceRoles.Builder, out int builders) && builders == 5, "the replacement compiles to 5 Builders (" + builders + ")");
        Near(order.Ceiling(CraftGood.Housing), 12, "Housing output is capped at the 12-unit target", 1e-6);
        // Delivered: one resolved turn restores the 120 units, and the next request is replacement again.
        var prepared = TurnPlanner.Prepare(w, new Dictionary<int, List<Order>>());
        new TurnResolver().Resolve(w, prepared);
        double made = w.History.Last().Production.Where(p => p.Camp == c.Id && p.Good == CraftGood.Housing).Sum(p => p.Output);
        Near(made, 12, "the resolver makes the 12 units", 1e-6);
        Near(NeolithicProduction.FixedHousingAt(w, c, w.Days), 120, "the turn ends with 120 units", 1e-6);
        Plan(w, c, out _, out var next);
        Near(next.Requests.Single(q => q.Service == "shelter replacement").Units, 12, "the next request is replacement again", 1e-6);
    }

    // Saturated budgets: few residents ration labour, record the cause and never over-staff.
    private static void Saturation()
    {
        var w = World(EconomicAllocationStrategy.DemandControl, (6, 6, 12)); var c = CampOf(w, 1); c.Inventory.Cloth = 0;
        NeolithicProduction.AddFixedHousing(w, c, 0.1, w.Days);
        var intent = Plan(w, c, out _, out var allocation);
        Check(EconomicIntents.Validate(Constraints(w, c), intent) == null, "a saturated plan is legal");
        // Every resident needed for survival (census C1 turn 51: 64 herders for 64 residents): no zero-person role remains.
        var herdsOnly = World(EconomicAllocationStrategy.DemandControl, (6, 6, 10)); var hc = CampOf(herdsOnly, 1);
        hc.Technologies[(int)Technology.AnimalHusbandry] = true; hc.Inventory.Wood = 0; hc.Inventory.Stone = 0; hc.Inventory.Clay = 0;
        herdsOnly.Herds.Add(new Herd { Id = herdsOnly.NextId++, Tile = hc.Tile, Animals = 400, DomesticCamp = hc.Id });
        var herdIntent = Plan(herdsOnly, hc, out _, out _);
        Check(herdIntent.Assignments.All(a => a.Population > 0) && EconomicIntents.Validate(Constraints(herdsOnly, hc), herdIntent) == null, "a camp whose residents all herd gets no zero-person role");
        Check(intent.Assignments.Sum(a => a.Population) + intent.Labor.MaterialGatherers <= c.Population + 1e-9, "a saturated plan fits the residents");
        Check(allocation.Requests.Where(q => q.Units > q.Granted + 1e-6).All(q => q.Unmet.Length > 0), "every shortfall names its cause");
        // Materials: no Wood or Clay in reach leaves Housing Material unmade, attributed to material shortage.
        var dry = World(EconomicAllocationStrategy.DemandControl, (6, 6, 200)); var dc = CampOf(dry, 1); dc.Inventory.Wood = 0; dc.Inventory.Clay = 0; dc.Inventory.HousingMaterial = 0;
        Plan(dry, dc, out var dryOrder, out var dryAllocation);
        var shelter = dryAllocation.Requests.Where(q => q.Good == CraftGood.Housing && q.Units > 1e-6).ToList();
        Check(shelter.Count > 0 && shelter.All(q => q.Granted < 1e-6 && q.Unmet == "material shortage") && dryOrder.Ceiling(CraftGood.Housing) < 1e-6, "with no construction material in reach no Housing is promised");
        Check(dryAllocation.Requests.Any(q => q.Good != CraftGood.Housing && q.Granted > 1e-6), "unrelated requests still use the remaining capacity");
    }

    // Fishing-only economies use the same physical pass and resident budget. A prepared access plan is injected exactly as
    // TurnPlanning does, so the planner must use its aquatic envelope without counting it as plant feedstock.
    private static void FishingIntegration()
    {
        var w = World(EconomicAllocationStrategy.DemandControl, (6, 6, 60));
        var c = CampOf(w, 1); var cfg = w.Config;
        c.Food = 0;
        cfg.NeolithicFishingEnabled = true; cfg.CorrectedAquaticSeasonalPressureEnabled = true;
        c.Technologies[(int)Technology.Cultivation] = false;
        c.Technologies[(int)Technology.Watercraft] = true;
        c.Technology = Technology.Watercraft;
        int shore = w.Map.Neighbors(c.Tile).OrderBy(id => id).First();
        w.Tiles[shore].Terrain = Terrain.Shallows;

        var ground = new FishingGroundPlan(shore, FishingRoute.Shallows, 1, FishingKnowledgeSource.Visible,
            false, 0, 500, 1000, 1, 0, 500, 500, new double[4], 100);
        var access = new FishingAccessPlan(c.Id, c.Tile, c.Population, w.Days, cfg.TurnDays,
            cfg.TurnDays / cfg.DaysPerYear, cfg.Neolithic.FishingToolFisherYearsPerUnit,
            cfg.Neolithic.FishingToolHarvestMultiplier, new[] { ground }, new[] { ground });

        Check(CultivationEconomy.EconomyEnabled(w, c), "a legal Watercraft shore activates the resident goods economy without Cultivation");
        var physical = CampProductionDemand.BuildPhysical(w, c, EconomicIntent.From(c), false, fishingAccess: access);
        Check(ReferenceEquals(physical.FishingAccess, access), "the prebuilt accepted-tile access envelope is reused");
        Near(physical.ForageSupply, physical.PlantSupply + physical.AquaticSupply, "plant and aquatic food remain separated");
        Check(physical.PlantEnvelope <= physical.TerrestrialAfterHerds + Math.Max(0, c.PlantFood) + 1e-9,
            "aquatic supply never enters the plant feedstock envelope");

        var intent = DemandController.Plan(w, c, EconomicIntent.From(c), access, out _, out var allocation);
        Check(allocation.Fishers > 0 && intent.Assignments.Any(a => a.Role == WorkforceRoles.Fisher && a.Population == allocation.Fishers),
            "resident Fishing competes on the survival frontier and commits its Fisher staffing (fishers=" + allocation.Fishers +
                ", catch=" + access.CatchEnvelope(1) + ", need=" + physical.Biological + ", forage=" + physical.ForageAfterHerds +
                ", grounds=" + physical.FishingAccess.Grounds.Length + ")");
        Check(EconomicIntents.Validate(Constraints(w, c), intent) == null, "a fishing-only DemandControl plan validates");
        Check(intent.Assignments.Sum(a => a.Population) + intent.Labor.MaterialGatherers <= c.Population + 1e-9,
            "Fishers share the common resident budget with goods work");
        var fishingTools = allocation.Requests.Single(q => q.Service == "fishing tools");
        Near(fishingTools.Units, allocation.Fishers * Years(w) / cfg.Neolithic.FishingToolFisherYearsPerUnit,
            "FishingTools continuity is sized after Fisher staffing");

        var recipes = NeolithicProduction.Recipes(cfg).ToDictionary(r => r.Good);
        Check(NeolithicProduction.RecipeAvailable(w, c, recipes[CraftGood.Cloth]) &&
            NeolithicProduction.RecipeAvailable(w, c, recipes[CraftGood.Housing]) &&
            NeolithicProduction.RecipeAvailable(w, c, recipes[CraftGood.HousingMaterial]),
            "Fishing-only Watercraft economies can use Cloth and fixed Housing recipes");
        Check(!NeolithicProduction.RecipeAvailable(w, c, recipes[CraftGood.FarmTools]),
            "FarmTools remain Cultivation-only for Fishing economies");

        var disabled = World(EconomicAllocationStrategy.DemandControl, (6, 6, 60)); var dc = CampOf(disabled, 1);
        dc.Technologies = new bool[Enum.GetValues(typeof(Technology)).Length]; dc.Technology = Technology.None;
        Check(!CultivationEconomy.EconomyEnabled(disabled, dc), "feature-off eligibility remains unchanged");
    }

    private static void RawMaterialContinuation()
    {
        var releasedWorld = NeolithicChecks.Fixture();
        releasedWorld.Config.EconomicAllocation = EconomicAllocationStrategy.DemandControl;
        releasedWorld.Config.ElderRawMaterialsEnabled = true;
        var releasedCamp = releasedWorld.Camps[0];
        releasedCamp.Workforce.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Stonecutter, Population = 99 });
        int priorSpecialists = releasedCamp.Workforce.Assignments.Where(a => a.Role != WorkforceRoles.Gatherer).Sum(a => a.Population);
        var releasedIntent = DemandController.Plan(releasedWorld, releasedCamp, EconomicIntent.From(releasedCamp),
            out _, out var releasedAllocation);
        int replannedSpecialists = releasedIntent.Assignments.Where(a => a.Role != WorkforceRoles.Gatherer).Sum(a => a.Population);
        double postIntentResidual = Math.Max(0, releasedCamp.Population - replannedSpecialists - releasedIntent.Labor.MaterialGatherers -
            releasedAllocation.FoodGatherers - releasedAllocation.FeedstockGatherers);
        Check(priorSpecialists > replannedSpecialists && releasedAllocation.PhaseAUnassignedResidents == 0 &&
              releasedAllocation.PhaseAFallbackGathererResidents == postIntentResidual,
            "replanning down from prior specialist staffing commits every newly released residual interval to native fallback, never Phase B (prior=" + priorSpecialists + ", planned=" + replannedSpecialists + ", fallback=" + releasedAllocation.PhaseAFallbackGathererResidents + ", idle=" + releasedAllocation.PhaseAUnassignedResidents + ", residual=" + postIntentResidual + ")");

        var w = NeolithicChecks.Fixture(); var c = w.Camps[0];
        w.Config.EconomicAllocation = EconomicAllocationStrategy.DemandControl;
        w.Config.ElderRawMaterialsEnabled = true;
        w.Config.Neolithic.MaterialReserveHorizonYears = 40;
        int count = w.Tiles.Length;
        w.Resources = new WorldResourceState { MaximumQuantity = 100000, CatalogHash = ResourceCatalog.Hash,
            ProfileHash = ResourceCatalog.Hash, ConfigurationHash = ResourceCatalog.Hash,
            Provinces = Array.Empty<GeologicalProvince>(), Province = new ushort[count], History = new ushort[count],
            Host = new byte[count], Marine = new byte[count], Stone = new ushort[count], Clay = new ushort[count], Wood = new ushort[count] };
        foreach (int tile in w.Map.Radius(c.Tile, 1))
        { w.Resources.Stone[tile] = 1000; w.Resources.Clay[tile] = 1000; w.Resources.Wood[tile] = 1000; }
        var intent = DemandController.Plan(w, c, EconomicIntent.From(c), out var order, out var allocation);
        var originalRequests = allocation.Requests.Select(q => (Request: q, q.Units, q.Granted)).ToArray();
        var originalStaff = allocation.Staff.OrderBy(p => p.Key, StringComparer.Ordinal).ToArray();
        var originalExtractionIntervals = allocation.PhaseAExtractionIntervals.OrderBy(p => p.Key, StringComparer.Ordinal).ToArray();
        int originalNonGatherers = allocation.PhaseANonGathererStaff;
        int originalUnassigned = allocation.PhaseAUnassignedResidents;
        double originalGatherers = allocation.PhaseAMaterialGathererIntervals;
        var originalCeilings = Enum.GetValues(typeof(CraftGood)).Cast<CraftGood>().ToDictionary(g => g, order.Ceiling);
        var horizon = TurnHorizon.Next(w);
        var planningCamp = EconomicPlanner.Preview(c, intent, c.Population);
        var recipes = NeolithicProduction.Recipes(w, planningCamp);
        double equipment = new ToolCoverageBudget(w, planningCamp, horizon).Remaining;
        c.People.NeolithicTechnologies.Add(NeolithicTech.Toolmaking);
        c.People.NeolithicTechnologies.Add(NeolithicTech.Carpentry);
        planningCamp.People.NeolithicTechnologies = new List<string>(c.People.NeolithicTechnologies);
        planningCamp.Workforce.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Stonecutter, Population = 2 });
        planningCamp.Workforce.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Woodcutter, Population = 2 });
        var sources = MaterialEconomy.KnownSources(w, planningCamp, w.Days);
        var comparisonPhaseAIntervals = new Dictionary<string, double>(allocation.PhaseAExtractionIntervals, StringComparer.Ordinal)
        {
            [WorkforceRoles.Stonecutter] = 2 * horizon.ReferenceIntervals,
            [WorkforceRoles.Woodcutter] = 2 * horizon.ReferenceIntervals
        };
        var comparisonRecipes = new[]
        {
            new CraftRecipe { Good = CraftGood.HandTools, Stone = 10 },
            new CraftRecipe { Good = CraftGood.Pots, Wood = 10 }
        };
        var forecast = comparisonRecipes.ToDictionary(r => r.Good, r => 1d);
        var needs = new TurnNeeds { Camp = c.Id, Residents = c.Population, Order = order, Production = forecast,
            Housing = new HousingMaterialPlan(0, 0, new List<HousingMaterialPlan.Reservation>(), new HashSet<CraftGood>(), 0) };
        var baseline = MaterialEconomy.BuildBaselineGatherPlan(w, planningCamp, comparisonRecipes,
            horizon.ReferenceIntervals, horizon.Years, allocation.PhaseAMaterialGathererIntervals, needs, sources,
            comparisonPhaseAIntervals);
        var rawTargets = MaterialEconomy.BuildBaselineMaterialTargets(w, comparisonRecipes, horizon.Years, needs);
        // Compare the prepared ordered intent stream against the untouched
        // legacy Gather loop on an independently reconstructed identical fixture.
        var legacyWorld = NeolithicChecks.Fixture(); var legacyCamp = legacyWorld.Camps[0];
        legacyWorld.Config.EconomicAllocation = EconomicAllocationStrategy.DemandControl;
        legacyWorld.Config.ElderRawMaterialsEnabled = false;
        legacyWorld.Config.Neolithic.MaterialReserveHorizonYears = w.Config.Neolithic.MaterialReserveHorizonYears;
        int legacyCount = legacyWorld.Tiles.Length;
        legacyWorld.Resources = new WorldResourceState { MaximumQuantity = 100000, CatalogHash = ResourceCatalog.Hash,
            ProfileHash = ResourceCatalog.Hash, ConfigurationHash = ResourceCatalog.Hash,
            Provinces = Array.Empty<GeologicalProvince>(), Province = new ushort[legacyCount], History = new ushort[legacyCount],
            Host = new byte[legacyCount], Marine = new byte[legacyCount], Stone = new ushort[legacyCount], Clay = new ushort[legacyCount], Wood = new ushort[legacyCount] };
        foreach (int tile in legacyWorld.Map.Radius(legacyCamp.Tile, 1))
        { legacyWorld.Resources.Stone[tile] = 1000; legacyWorld.Resources.Clay[tile] = 1000; legacyWorld.Resources.Wood[tile] = 1000; }
        var legacyIntent = DemandController.Plan(legacyWorld, legacyCamp, EconomicIntent.From(legacyCamp), out var legacyOrder, out var legacyAllocation);
        var legacyHorizon = TurnHorizon.Next(legacyWorld);
        var legacyPlanningCamp = EconomicPlanner.Preview(legacyCamp, legacyIntent, legacyCamp.Population);
        legacyCamp.People.NeolithicTechnologies = new List<string>(c.People.NeolithicTechnologies);
        legacyPlanningCamp.People.NeolithicTechnologies = new List<string>(c.People.NeolithicTechnologies);
        legacyCamp.Workforce.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Stonecutter, Population = 2 });
        legacyCamp.Workforce.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Woodcutter, Population = 2 });
        legacyPlanningCamp.Workforce.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Stonecutter, Population = 2 });
        legacyPlanningCamp.Workforce.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Woodcutter, Population = 2 });
        var legacyRecipes = comparisonRecipes;
        var legacySources = MaterialEconomy.KnownSources(legacyWorld, legacyPlanningCamp, legacyWorld.Days);
        double legacyEquipment = new ToolCoverageBudget(legacyWorld, legacyPlanningCamp, legacyHorizon).Remaining;
        var legacyForecast = comparisonRecipes.ToDictionary(r => r.Good, r => 1d);
        var legacyNeeds = new TurnNeeds { Camp = legacyCamp.Id, Residents = legacyCamp.Population, Order = legacyOrder,
            Production = legacyForecast, Housing = new HousingMaterialPlan(0, 0, new List<HousingMaterialPlan.Reservation>(), new HashSet<CraftGood>(), 0) };
        var legacyPhaseALabor = new Dictionary<string, double>(legacyAllocation.PhaseAExtractionIntervals, StringComparer.Ordinal)
        {
            [WorkforceRoles.Stonecutter] = 2 * legacyHorizon.ReferenceIntervals,
            [WorkforceRoles.Woodcutter] = 2 * legacyHorizon.ReferenceIntervals
        };
        var legacyBaseline = MaterialEconomy.BuildBaselineGatherPlan(legacyWorld, legacyPlanningCamp, legacyRecipes,
            legacyHorizon.ReferenceIntervals, legacyHorizon.Years, 0, legacyNeeds, legacySources, legacyPhaseALabor);
        Check(legacyBaseline.Debits.Count > 0, "deterministic comparison fixture has baseline material work");
        var legacyRecord = new TurnRecord { StartDays = legacyWorld.Days, Duration = legacyWorld.Config.TurnDays };
        double legacyBudget = new ToolCoverageBudget(legacyWorld, legacyPlanningCamp, legacyHorizon).Remaining;
        MaterialEconomy.Gather(legacyWorld, legacyPlanningCamp, legacyRecipes, legacyHorizon.ReferenceIntervals,
            legacyHorizon.Years, legacyAllocation.PhaseAMaterialGathererIntervals, ref legacyBudget, legacyRecord, true, true, legacyNeeds);
        var expectedDebits = baseline.Debits.Select(d => (d.Source.Id, d.Source.Material, d.Role, d.Units)).ToArray();
        var actualDebits = legacyRecord.MaterialTransfers.Select(r => (r.Source, r.Material, r.Role, r.Amount)).ToArray();
        Check(expectedDebits.Length > 0 && expectedDebits.Select(x => x.Role).Distinct(StringComparer.Ordinal).Count() >= 2 &&
            baseline.Debits.Any(d => d.Horizon > 1 + 1e-12) && expectedDebits.Length == actualDebits.Length && expectedDebits.Zip(actualDebits).All(pair =>
                pair.First.Id == pair.Second.Source && pair.First.Material == pair.Second.Material && pair.First.Role == pair.Second.Role &&
                Math.Abs(pair.First.Units - pair.Second.Amount) <= 1e-9),
            "nonempty shared baseline plan preserves legacy current-input/reserve debit order and amounts across competing roles (plan=" + expectedDebits.Length + ", roles=" + expectedDebits.Select(x => x.Role).Distinct(StringComparer.Ordinal).Count() + ", reserve=" + baseline.Debits.Count(d => d.Horizon > 1 + 1e-12) + ", legacy=" + actualDebits.Length + ")");

        // One authoritative geological body is visible through two local sites
        // with fractional access. Sequential role debits must conserve the
        // body's stock, not subtract the body debit from each local alias.
        var sharedBodySources = new[]
        {
            new KnownMaterialSource { Id = "body:shared-stone", Tile = planningCamp.Tile, Material = MaterialId.Stone, Initial = 100, Available = 25, Share = .25, Distance = 0 },
            new KnownMaterialSource { Id = "body:shared-stone", Tile = planningCamp.Tile + 1, Material = MaterialId.Stone, Initial = 100, Available = 75, Share = .75, Distance = 1 }
        };
        var sharedBodyNeeds = new TurnNeeds { Camp = planningCamp.Id, Residents = planningCamp.Population, Order = order,
            Production = Enum.GetValues(typeof(CraftGood)).Cast<CraftGood>().ToDictionary(g => g, g => g == CraftGood.HandTools ? 20d : 0d),
            Housing = new HousingMaterialPlan(0, 0, new List<HousingMaterialPlan.Reservation>(), new HashSet<CraftGood>(), 0) };
        var sharedBodyPlan = MaterialEconomy.BuildBaselineGatherPlan(w, planningCamp, comparisonRecipes,
            horizon.ReferenceIntervals, horizon.Years, 100, sharedBodyNeeds, sharedBodySources,
            new Dictionary<string, double>(StringComparer.Ordinal)
            { [WorkforceRoles.Stonecutter] = 100, [WorkforceRoles.Gatherer] = 100 });
        double bodyDebited = sharedBodyPlan.Debits.Where(d => d.Source.Id == "body:shared-stone").Sum(d => d.Units);
        double bodyResidual = sharedBodyPlan.ResidualSources.Where(s => s.Id == "body:shared-stone").Sum(s => s.Available);
        Check(sharedBodyPlan.Debits.Count(d => d.Source.Id == "body:shared-stone") >= 2 && bodyDebited > 0 && bodyDebited < 100 &&
            Math.Abs(bodyResidual - (100 - bodyDebited)) < 1e-8,
            "fractional shared-body aliases conserve authoritative stock across sequential extraction-role debits (debits=" + bodyDebited + ", residual=" + bodyResidual + ")");
        order.BaselineGatherPlan = baseline;
        DemandController.FinalizeRawMaterialPhase(w, planningCamp, intent, order, allocation,
            new ElderDirective { ServiceLevels = new[] { 1d, 1, 1, 1, 1, 1 } }, rawTargets, horizon);
        Check(ReferenceEquals(order.BaselineGatherPlan, baseline), "resolution order owns the exact prepared baseline-plan object");
        Check(originalRequests.All(x => allocation.Requests.Contains(x.Request) && x.Request.Units == x.Units && x.Request.Granted == x.Granted),
            "Phase-B continuation leaves every Phase-A request and grant unchanged");
        Check(originalNonGatherers == allocation.PhaseANonGathererStaff && originalUnassigned == allocation.PhaseAUnassignedResidents &&
            originalGatherers == allocation.PhaseAMaterialGathererIntervals && originalStaff.SequenceEqual(allocation.Staff.OrderBy(p => p.Key, StringComparer.Ordinal)) &&
            originalExtractionIntervals.SequenceEqual(allocation.PhaseAExtractionIntervals.OrderBy(p => p.Key, StringComparer.Ordinal)),
            "Phase-A staffing and exported idle pool remain frozen");
        var overlapAllocation = new DemandController.Allocation();
        overlapAllocation.PhaseAUnassignedResidents = 10;
        overlapAllocation.Staff[WorkforceRoles.Stonecutter] = 2;
        overlapAllocation.PhaseAExtractionIntervals[WorkforceRoles.Stonecutter] = 2 * horizon.ReferenceIntervals;
        var overlapPhaseAIntervals = overlapAllocation.PhaseAExtractionIntervals[WorkforceRoles.Stonecutter];
        var overlapPhaseAStaff = overlapAllocation.Staff[WorkforceRoles.Stonecutter];
        c.StonePriority = 1;
        DemandController.FinalizeRawMaterialPhase(w, planningCamp, intent, new ProductionOrder(order.Goods, new Dictionary<CraftGood, double>()), overlapAllocation,
            new ElderDirective { ServiceLevels = new[] { 1d, 1, 1, 1, 1, 1 } }, rawTargets, horizon);
        Check(overlapAllocation.PhaseBStaff.Count > 0 && overlapAllocation.PhaseBStaff.ContainsKey(WorkforceRoles.Stonecutter) &&
            overlapAllocation.PhaseAExtractionIntervals[WorkforceRoles.Stonecutter] == overlapPhaseAIntervals &&
            overlapAllocation.Staff[WorkforceRoles.Stonecutter] == overlapPhaseAStaff,
            "overlapping Phase-B specialist staff is separate while its Phase-A staff and intervals remain frozen");
        var fractionalAllocation = new DemandController.Allocation { PhaseAUnassignedResidents = 10 };
        var fractionalOrder = new ProductionOrder(Array.Empty<CraftGood>(), new Dictionary<CraftGood, double>());
        DemandController.FinalizeRawMaterialPhase(w, planningCamp, intent, fractionalOrder, fractionalAllocation,
            new ElderDirective { ServiceLevels = new[] { 0d, 0, 0, 0, 0, .35 } }, rawTargets, horizon);
        var fractionalRequests = fractionalAllocation.Requests.Where(r => r.RawMaterial.HasValue).ToArray();
        Check(fractionalRequests.Length > 0 && fractionalRequests.Any(r => r.Granted > 1e-9) &&
              fractionalRequests.All(r => r.Granted <= .35 * r.Units + 1e-8) &&
              fractionalOrder.RawMaterialAllowances.All(a => a.Units <= .35 * a.TargetStock + 1e-8),
            "non-vacuous fractional service proportionally caps each typed raw authorization when a frozen unused pool exists");
        Check(Enum.GetValues(typeof(CraftGood)).Cast<CraftGood>().All(g => originalCeilings[g] == order.Ceiling(g)),
            "Phase-B does not reopen any existing goods ceiling");
        Check(allocation.Requests.Where(q => q.RawMaterial.HasValue).All(q => q.Class == DemandController.ServiceClass.Development && q.Bundle != null &&
            q.Bundle.Plant == 0 && q.Bundle.Animal == 0 && q.Bundle.FoodService == 0 && q.Bundle.ToolOutputs == 0),
            "raw requests are typed Development bundles with no direct food or tool cost");
        Check(order.RawMaterialAllowances.All(a => a.WorkerIntervals <= originalUnassigned * horizon.ReferenceIntervals + 1e-9),
            "dedicated raw worker intervals fit the Phase-A genuinely unassigned pool");
        Check(allocation.PhaseAFallbackGathererIntervals == allocation.PhaseAFallbackGathererResidents * horizon.ReferenceIntervals &&
              allocation.PhaseAFallbackGathererResidents + allocation.PhaseAUnassignedResidents <= c.Population,
            "Phase-A native fallback gatherers are frozen separately from the residual dedicated worker pool");
        var fallbackOnlyAllocation = new DemandController.Allocation
        { PhaseAUnassignedResidents = 0, PhaseAFallbackGathererResidents = 5,
          PhaseAFallbackGathererIntervals = 5 * horizon.ReferenceIntervals };
        var fallbackOnlyOrder = new ProductionOrder(Array.Empty<CraftGood>(), new Dictionary<CraftGood, double>());
        DemandController.FinalizeRawMaterialPhase(w, planningCamp, intent, fallbackOnlyOrder, fallbackOnlyAllocation,
            new ElderDirective { ServiceLevels = new[] { 1d, 1, 1, 1, 1, 1 } }, rawTargets, horizon);
        Check(fallbackOnlyOrder.RawMaterialAllowances.Count == 0 && fallbackOnlyAllocation.PhaseBStaff.Count == 0 &&
              fallbackOnlyAllocation.Requests.Where(r => r.RawMaterial.HasValue).All(r => r.Granted == 0),
            "Phase-B authorization is zero when the entire residual cohort is already committed to native fallback gathering");

        var exhaustedWorld = NeolithicChecks.Fixture(); var exhaustedCamp = exhaustedWorld.Camps[0];
        exhaustedWorld.Config.ElderRawMaterialsEnabled = true;
        var exhaustedPlanning = EconomicPlanner.Preview(exhaustedCamp, EconomicIntent.From(exhaustedCamp), exhaustedCamp.Population);
        var knownBeforeExhaustion = MaterialEconomy.KnownSources(exhaustedWorld, exhaustedPlanning, exhaustedWorld.Days);
        foreach (var source in knownBeforeExhaustion.GroupBy(s => s.Id, StringComparer.Ordinal).Select(g => g.First()))
        {
            var state = MaterialEconomy.SourceState(exhaustedWorld, source.Id);
            if (state == null)
            {
                state = new MaterialSourceState { Id = source.Id, Material = source.Material,
                    Initial = source.Initial, Remaining = 0, LastDays = exhaustedWorld.Days };
                exhaustedWorld.MaterialSources.Add(state); MaterialEconomy.RegisterSourceState(exhaustedWorld, state);
            }
            else state.Remaining = 0;
        }
        var exhaustedOrder = new ProductionOrder(Array.Empty<CraftGood>(), new Dictionary<CraftGood, double>());
        var exhaustedAllocation = new DemandController.Allocation { PhaseAUnassignedResidents = 20 };
        foreach (var material in MaterialEconomy.Materials) exhaustedAllocation.ServiceRequested[(int)material] = 0;
        var exhaustedBaseline = new BaselineMaterialTargets();
        foreach (var material in MaterialEconomy.Materials) exhaustedBaseline.ByMaterial[material] = 100;
        DemandController.FinalizeRawMaterialPhase(exhaustedWorld, exhaustedCamp, EconomicIntent.From(exhaustedCamp),
            exhaustedOrder, exhaustedAllocation,
            new ElderDirective { ServiceLevels = new[] { 1d, 1, 1, 1, 1, 1 } }, exhaustedBaseline, TurnHorizon.Next(exhaustedWorld));
        Check(exhaustedOrder.RawMaterialAllowances.Count == 0 && !exhaustedAllocation.Requests.Any(r => r.RawMaterial.HasValue),
            "exhausted finite sources cannot create raw-material requests or consume dedicated staffing");

        var shared = World(EconomicAllocationStrategy.DemandControl, (6, 6, 60), (7, 6, 60));
        int sharedTile = CampOf(shared, 1).Tile;
        int sharedCount = shared.Tiles.Length;
        shared.Resources = new WorldResourceState { MaximumQuantity = 100000, CatalogHash = ResourceCatalog.Hash,
            ProfileHash = ResourceCatalog.Hash, ConfigurationHash = ResourceCatalog.Hash,
            Provinces = Array.Empty<GeologicalProvince>(), Province = new ushort[sharedCount], History = new ushort[sharedCount],
            Host = new byte[sharedCount], Marine = new byte[sharedCount], Stone = new ushort[sharedCount],
            Clay = new ushort[sharedCount], Wood = new ushort[sharedCount] };
        int sharedSite2 = shared.Map.Radius(sharedTile, 1).Where(id => id != sharedTile && shared.Tiles[id].TraversableLand).OrderBy(id => id).First();
        shared.Resources.Version = 4; shared.Resources.GenerationVersion = 3;
        shared.Resources.DeepReserveMultiplier = 1; shared.Resources.DeepMiningTechnology = "AdvancedMining";
        shared.Resources.DeepStone = new ushort[sharedCount]; shared.Resources.DeepClay = new ushort[sharedCount];
        shared.Resources.Bodies = new[] { new DepositBody { Id = 42, OriginTile = sharedTile, Quantity = 50,
            SiteExposureWeightTotal = 10000, Components = new[] { new DepositComponent(MaterialId.Stone, 10000)
                { InitialAccessibleQuantity = 50, AccessibleQuantity = 50 } },
            Sites = new[] { new DepositSite { Tile = sharedTile, Share = 2500, Exposure = 10000 },
                new DepositSite { Tile = sharedSite2, Share = 7500, Exposure = 10000 } } } };
        string sharedStoneId = "body:42:" + (int)MaterialId.Stone;
        shared.MaterialSources.Add(new MaterialSourceState { Id = sharedStoneId, Material = MaterialId.Stone,
            Initial = 50, Remaining = 50, LastDays = shared.Days });
        shared.Config.ElderRawMaterialsEnabled = true;
        var sharedRecord = new TurnRecord { StartDays = shared.Days, Duration = shared.Config.TurnDays };
        var sharedNeedsByCamp = new Dictionary<int, TurnNeeds>();
        int laterSharedCamp = shared.Camps.Where(c => c.Player > 0).Max(c => c.Id);
        foreach (var camp in shared.Camps.Where(x => x.Player > 0).OrderBy(x => x.Id))
        {
            camp.Inventory.Stone = 0;
            var sharedRecipes = new[] { new CraftRecipe { Good = CraftGood.HandTools, Stone = 1 } };
            var zero = Enum.GetValues(typeof(CraftGood)).Cast<CraftGood>().ToDictionary(g => g, _ => 0d);
            if (camp.Id == laterSharedCamp) zero[CraftGood.HandTools] = 30;
            var sharedOrder = new ProductionOrder(sharedRecipes.Select(r => r.Good).ToArray(), new Dictionary<CraftGood, double>());
            var sharedSources = MaterialEconomy.KnownSources(shared, camp, shared.Days);
            var sharedNeeds = new TurnNeeds { Camp = camp.Id, Residents = camp.Population, Order = sharedOrder,
                Production = zero, Housing = MaterialEconomy.PlanHousingMaterials(shared, camp, sharedRecipes, zero,
                    shared.Config.TurnDays / shared.Config.DaysPerYear, shared.Days, sharedSources, sharedOrder) };
            sharedNeeds.PreBaselineInventory = new Dictionary<MaterialId, double>
                { [MaterialId.Stone] = camp.Inventory.Stone, [MaterialId.Clay] = camp.Inventory.Clay, [MaterialId.Wood] = camp.Inventory.Wood };
            sharedNeeds.BaselineReceipts = new Dictionary<MaterialId, double>
                { [MaterialId.Stone] = 0, [MaterialId.Clay] = 0, [MaterialId.Wood] = 0 };
            sharedOrder.RawMaterialAllowances.Add(new ProductionOrder.RawMaterialAllowance { Material = MaterialId.Stone,
                Role = WorkforceRoles.Gatherer, Units = 40, WorkerIntervals = 100, RequestId = "shared:" + camp.Id,
                EligibleSourceIds = new[] { sharedStoneId },
                TargetStock = 1000, ServiceFraction = 1 });
            sharedNeedsByCamp[camp.Id] = sharedNeeds;
            double equipmentBudget = 0;
            MaterialEconomy.Gather(shared, camp, sharedRecipes, shared.Config.GenerationIntervals,
                shared.Config.TurnDays / shared.Config.DaysPerYear, 100, ref equipmentBudget, sharedRecord, true, true, sharedNeeds);
        }
        shared.Diagnostics = new TurnDiagnostics(shared.Turn);
        bool previousDetailedCounters = ResolutionMetrics.CaptureDetailedCounters;
        ResolutionMetrics.CaptureDetailedCounters = true;
        try { MaterialEconomy.SettleElderRawMaterials(shared, sharedNeedsByCamp, sharedRecord); }
        finally { ResolutionMetrics.CaptureDetailedCounters = previousDetailedCounters; }
        var sharedRawReceipts = sharedRecord.MaterialTransfers.Where(r => r.Phase == "elder-raw" &&
            r.Source == sharedStoneId).ToArray();
        double laterBaseline = sharedRecord.MaterialTransfers.Where(r => r.Camp == laterSharedCamp &&
            r.Material == MaterialId.Stone && r.Phase != "elder-raw").Sum(r => r.Amount);
        double allSharedReceipts = sharedRecord.MaterialTransfers.Where(r => r.Source == sharedStoneId).Sum(r => r.Amount);
        Check(sharedRawReceipts.Length > 0 && sharedRawReceipts[0].Camp == shared.Camps.Where(c => c.Player > 0).Min(c => c.Id) &&
            laterBaseline > 0 && allSharedReceipts > 0 && allSharedReceipts <= 50 + 1e-8,
            "late camp-ID ordered allowances debit shared residual stock once after all baseline passes (count=" + sharedRawReceipts.Length + ", units=" + sharedRawReceipts.Sum(r => r.Amount) + ", baseline=" + laterBaseline + ", all=" + string.Join(";", sharedRecord.MaterialTransfers.Select(r => r.Camp + ":" + r.Phase + ":" + r.Amount + ":" + r.Before + ":" + r.After)) + ", camps=" + string.Join(",", sharedRawReceipts.Select(r => r.Camp)) + ", enabled=" + shared.Config.ElderRawMaterialsEnabled + ", auth=" + shared.Config.EconomicAllocation + ", pre=" + string.Join(";", sharedNeedsByCamp.Select(p => p.Key + ":" + (p.Value.PreBaselineInventory == null ? "null" : p.Value.PreBaselineInventory[MaterialId.Stone]) + ":" + p.Value.Order.RawMaterialAllowances[0].EligibleSourceIds.Length)) + ")");
        int activeAllowanceCount = sharedNeedsByCamp.Values.Sum(n => n.Order.RawMaterialAllowances.Count);
        Check(shared.Diagnostics.RawSettlementCampIndexEntries == shared.Camps.Count &&
              shared.Diagnostics.RawSettlementNeedRows == sharedNeedsByCamp.Count &&
              shared.Diagnostics.RawSettlementAllowanceRows == activeAllowanceCount &&
              shared.Diagnostics.RawSettlementSourceQueries <= activeAllowanceCount,
            "late settlement work is one O(C) camp index plus need rows and allowance-local source queries (camps=" +
            shared.Diagnostics.RawSettlementCampIndexEntries + ", needs=" + shared.Diagnostics.RawSettlementNeedRows +
            ", allowances=" + shared.Diagnostics.RawSettlementAllowanceRows + ", sourceQueries=" + shared.Diagnostics.RawSettlementSourceQueries + ")");
        var baselineOnlyTransfers = sharedRecord.MaterialTransfers.Where(r => r.Phase != "elder-raw").ToArray();
        double baselineBodyDebited = baselineOnlyTransfers.Where(r => r.Source == sharedStoneId).Sum(r => r.Amount);
        bool fractionalSiteShare = shared.Camps.Where(c => c.Player > 0).SelectMany(c => MaterialEconomy.KnownSources(shared, c, shared.Days))
            .Any(s => s.Id == sharedStoneId && s.Share < 1 - 1e-9);
        bool settlementFractions = true;
        foreach (double serviceFraction in new[] { 0d, .35, 1d })
        {
            var sourceState = MaterialEconomy.SourceState(shared, sharedStoneId);
            sourceState.Remaining = Math.Max(0, 50 - baselineBodyDebited); sourceState.LastDays = shared.Days;
            foreach (var pair in sharedNeedsByCamp)
            {
                var camp = shared.Camps.First(c => c.Id == pair.Key);
                camp.Inventory.Stone = pair.Value.PreBaselineInventory[MaterialId.Stone] + pair.Value.BaselineReceipts[MaterialId.Stone];
                pair.Value.Order.RawMaterialAllowances[0].ServiceFraction = serviceFraction;
            }
            var fractionalRecord = new TurnRecord { StartDays = shared.Days, Duration = shared.Config.TurnDays };
            fractionalRecord.MaterialTransfers.AddRange(baselineOnlyTransfers);
            MaterialEconomy.SettleElderRawMaterials(shared, sharedNeedsByCamp, fractionalRecord);
            double rawReceived = fractionalRecord.MaterialTransfers.Where(r => r.Phase == "elder-raw" && r.Source == sharedStoneId).Sum(r => r.Amount);
            double rawLimit = sharedNeedsByCamp.Values.Sum(n => n.Order.RawMaterialAllowances.Sum(a =>
                Math.Min(a.Units, Math.Max(0, a.ServiceFraction * (a.TargetStock -
                    n.PreBaselineInventory[a.Material] - n.BaselineReceipts[a.Material])))));
            double totalDebited = baselineOnlyTransfers.Where(r => r.Source == sharedStoneId).Sum(r => r.Amount) + rawReceived;
            bool amountOk = serviceFraction == 0 ? rawReceived == 0 : rawReceived > 0 && rawReceived <= rawLimit + 1e-8;
            bool conserved = Math.Abs(totalDebited + sourceState.Remaining - 50) < 1e-8 && rawReceived <= 50 + 1e-8;
            settlementFractions &= amountOk && conserved;
        }
        Check(fractionalSiteShare && settlementFractions,
            "live late settlement honors 0/fractional/full service caps and conserves one shared geological body through Share<1 aliases");
        var scarcityCamp = shared.Camps.First(x => x.Player > 0);
        scarcityCamp.EconomicLabor = new EconomicLaborPlan { PolicyAuthority = false };
        var scarcityOrder = new ProductionOrder(Array.Empty<CraftGood>(), new Dictionary<CraftGood, double>())
        { PhaseAExtractionIntervals = new Dictionary<string, double> { [WorkforceRoles.Gatherer] = 2 * horizon.ReferenceIntervals } };
        Check(TurnResolver.PassiveFoodGathererIntervals(scarcityCamp,
            new TurnNeeds { Order = new ProductionOrder(Array.Empty<CraftGood>(), new Dictionary<CraftGood, double>())
                { PhaseAExtractionIntervals = new Dictionary<string, double> { [WorkforceRoles.Gatherer] = 8 * horizon.ReferenceIntervals } } },
            8 * horizon.ReferenceIntervals, horizon.ReferenceIntervals) == 8 * horizon.ReferenceIntervals,
            "flag-on zero-service/no-allowance plan leaves legacy passive food gathering unchanged");
        scarcityOrder.RawMaterialAllowances.Add(new ProductionOrder.RawMaterialAllowance
        { Material = MaterialId.Stone, Role = WorkforceRoles.Gatherer, Units = 10, WorkerIntervals = 2 * horizon.ReferenceIntervals });
        var scarcityNeeds = new TurnNeeds { Order = scarcityOrder };
        double remainingPassive = TurnResolver.PassiveFoodGathererIntervals(scarcityCamp, scarcityNeeds, 5, horizon.ReferenceIntervals);
        Check(Math.Abs(remainingPassive - 1) < 1e-9 &&
              scarcityOrder.PhaseAExtractionIntervals[WorkforceRoles.Gatherer] + scarcityOrder.RawMaterialAllowances.Sum(a => a.WorkerIntervals) + remainingPassive * horizon.ReferenceIntervals <=
                  5 * horizon.ReferenceIntervals + 1e-9 && scarcityOrder.PhaseAExtractionIntervals[WorkforceRoles.Gatherer] == 2 * horizon.ReferenceIntervals,
            "scarce passive food labor reserves frozen Phase-A and dedicated Phase-B Gatherer intervals exactly once");

        WorldState RegeneratingWoodWorld()
        {
            var woodWorld = NeolithicChecks.Fixture(); var woodCamp = woodWorld.Camps[0];
            woodWorld.Config.EconomicAllocation = EconomicAllocationStrategy.DemandControl;
            woodWorld.Config.TurnDays = woodWorld.Config.DaysPerYear;
            int tiles = woodWorld.Tiles.Length;
            woodWorld.Resources = new WorldResourceState { MaximumQuantity = 100000, CatalogHash = ResourceCatalog.Hash,
                ProfileHash = ResourceCatalog.Hash, ConfigurationHash = ResourceCatalog.Hash, WoodRecoveryPerYear = 1,
                Provinces = Array.Empty<GeologicalProvince>(), Province = new ushort[tiles], History = new ushort[tiles],
                Host = new byte[tiles], Marine = new byte[tiles], Stone = new ushort[tiles], Clay = new ushort[tiles], Wood = new ushort[tiles] };
            int woodTile = woodWorld.Map.Neighbors(woodCamp.Tile).OrderBy(id => id).First();
            woodWorld.Resources.Wood[woodTile] = 500;
            woodWorld.Tiles[woodTile].CultivationCapital = 1;
            woodWorld.Tiles[woodTile].CultivationOperator = woodCamp.Id;
            woodWorld.MaterialSources.Add(new MaterialSourceState { Id = "surface:" + woodTile + ":" + (int)MaterialId.Wood,
                Material = MaterialId.Wood, Initial = 500, Remaining = 0, LastDays = woodWorld.Days });
            return woodWorld;
        }
        var regrowing = RegeneratingWoodWorld(); var regrowingCamp = regrowing.Camps[0];
        var regrowingRecipes = new[] { new CraftRecipe { Good = CraftGood.Pots, Wood = 100 } };
        var woodForecast = Enum.GetValues(typeof(CraftGood)).Cast<CraftGood>().ToDictionary(g => g, g => g == CraftGood.Pots ? 100d : 0d);
        var woodHousing = new HousingMaterialPlan(0, 0, new List<HousingMaterialPlan.Reservation>(), new HashSet<CraftGood>(), 0);
        var woodNeeds = new TurnNeeds { Camp = regrowingCamp.Id, Residents = regrowingCamp.Population,
            Production = woodForecast, Housing = woodHousing };
        var endSources = MaterialEconomy.KnownSources(regrowing, regrowingCamp, regrowing.Days + regrowing.Config.TurnDays);
        var startWood = MaterialEconomy.KnownSources(regrowing, regrowingCamp, regrowing.Days)
            .Where(s => s.Material == MaterialId.Wood).Sum(s => s.Available);
        var woodOrder = new ProductionOrder(new[] { CraftGood.Pots }, new Dictionary<CraftGood, double>());
        woodNeeds.Order = woodOrder;
        var woodHorizon = TurnHorizon.Next(regrowing);
        var woodPlan = MaterialEconomy.BuildBaselineGatherPlan(regrowing, regrowingCamp, regrowingRecipes,
            woodHorizon.ReferenceIntervals, woodHorizon.Years, 1000, woodNeeds, endSources,
            new Dictionary<string, double> { [WorkforceRoles.Gatherer] = 1000 });
        var woodTargets = MaterialEconomy.BuildBaselineMaterialTargets(regrowing, regrowingRecipes, woodHorizon.Years, woodNeeds);
        var disabledRawAllocation = new DemandController.Allocation();
        DemandController.FinalizeRawMaterialPhase(regrowing, regrowingCamp, EconomicIntent.From(regrowingCamp), woodOrder,
            disabledRawAllocation, new ElderDirective { ServiceLevels = new[] { 1d, 1, 1, 1, 1, 0 } }, woodTargets, woodHorizon);
        Check(startWood <= 1e-9 && endSources.Sum(s => s.Material == MaterialId.Wood ? s.Available : 0) > 0 &&
              woodPlan.Debits.Any(d => d.Source.Material == MaterialId.Wood) && woodOrder.RawMaterialAllowances.Count == 0,
            "depleted Wood regenerates at the frozen turn endpoint while a zero Elder service adds no Phase-B allowance");
        var legacyWood = RegeneratingWoodWorld(); var legacyWoodCamp = legacyWood.Camps[0];
        var legacyWoodNeeds = new TurnNeeds { Camp = legacyWoodCamp.Id, Residents = legacyWoodCamp.Population,
            Production = woodForecast, Housing = woodHousing };
        double woodEquipment = 0;
        var woodRecord = new TurnRecord { StartDays = legacyWood.Days, Duration = legacyWood.Config.TurnDays };
        MaterialEconomy.Gather(legacyWood, legacyWoodCamp, regrowingRecipes, woodHorizon.ReferenceIntervals,
            woodHorizon.Years, 1000, ref woodEquipment, woodRecord, true, true, legacyWoodNeeds);
        var plannedWoodDebits = woodPlan.Debits.Where(d => d.Source.Material == MaterialId.Wood)
            .Select(d => (d.Source.Id, d.Role, d.Units)).ToArray();
        var executedWood = woodRecord.MaterialTransfers.Where(r => r.Material == MaterialId.Wood)
            .Select(r => (r.Source, r.Role, r.Amount)).ToArray();
        Check(plannedWoodDebits.Length > 0 && plannedWoodDebits.Length == executedWood.Length &&
              plannedWoodDebits.Zip(executedWood).All(pair => pair.First.Id == pair.Second.Source &&
                  pair.First.Role == pair.Second.Role && Math.Abs(pair.First.Units - pair.Second.Amount) <= 1e-8),
            "endpoint-projected depleted/regenerating Wood plan matches legacy Gather debit order and receipts");
    }

    private static void FishingMealBreakpoint()
    {
        var w = FishingEconomyChecks.Fixture(24); var c = w.Camps.Single();
        w.Config.AnnualHumanGrowthRate = 0;
        double meals = c.Population * w.Config.Consumption * w.Config.GenerationIntervals;
        TurnResolver.FoodBucketBasis(w, c, w.Config.GenerationIntervals,
            w.Config.TurnDays / w.Config.DaysPerYear, meals, out double reserve, out _);
        c.PlantFood = reserve;
        int shore = w.Map.Neighbors(c.Tile).OrderBy(id => id).First();
        var tile = w.Tiles[shore];
        tile.Terrain = Terrain.Shallows; tile.ElevationLevel = 0;
        tile.WaterBody = WaterKind.Saltwater; tile.FreshwaterSource = FreshwaterSource.None;
        tile.Food = 250; tile.PotentialFood = 1000; tile.AquaticCondition = 1;
        tile.LastEcologyUpdateDays = w.Days;
        for (int i = 0; i < tile.Seasons.Length; i++)
        {
            var season = tile.Seasons[i];
            season.MaxFood = 250; season.MaxPotentialFood = 1000;
            season.FoodRecoveryPerDay = 0; season.BiomassRecoveryPerDay = 0;
            tile.Seasons[i] = season;
        }
        var access = FishingEconomy.BuildAccess(w, c, c.Tile, c.Population,
            w.Days, w.Config.TurnDays);
        var intent = DemandController.Plan(w, c, EconomicIntent.From(c), access,
            out _, out var allocation);
        Check(access.CatchEnvelope(10) >= meals - 1e-9,
            "fixture: ten Fishers can meet the 24-person meal target");
        Check(allocation.Fishers > 0 && allocation.Fishers + allocation.FoodGatherers == 10,
            "the meal-target catch breakpoint selects ten mixed food workers over fourteen Gatherers"
                + " (fishers=" + allocation.Fishers + ", gatherers=" + allocation.FoodGatherers + ")");
        Check(EconomicIntents.Validate(Constraints(w, c), intent) == null,
            "the meal-breakpoint Fishing plan validates");
    }

    // Whole turns: legal plans, ceilings honoured, invariants, replay and save continuation.
    private static void Turns()
    {
        WorldState Fresh() { var w = World(EconomicAllocationStrategy.DemandControl, (6, 6, 150), (10, 6, 80), (6, 10, 40), (14, 10, 300)); w.Turn = 1; w.Days = 0; foreach (var t in w.Tiles) t.LastEcologyUpdateDays = 0; return w; }
        var world = Fresh();
        for (int turn = 0; turn < 4; turn++)
        {
            var prepared = TurnPlanner.Prepare(world, new Dictionary<int, List<Order>>());
            Check(prepared.AutomatedCamps.Count >= 4, "fixture: the villages are automated");
            var ceilings = prepared.Needs.Where(p => p.Value.Order != null).ToDictionary(p => p.Key, p => p.Value.Order);
            Check(ceilings.Count >= 4 && ceilings.Values.All(o => o.HasCeilings), "prepared needs carry the DemandControl ceilings");
            new TurnResolver().Resolve(world, prepared);
            TurnResolver.Invariants(world); checks++;
            foreach (var g in world.History.Last().Production.GroupBy(p => (p.Camp, p.Good)))
                if (ceilings.TryGetValue(g.Key.Camp, out var o)) Check(g.Sum(p => p.Output) <= o.Ceiling(g.Key.Good) + 1e-6, $"camp {g.Key.Camp} makes no more {g.Key.Good} than allocated ({g.Sum(p => p.Output)} > {o.Ceiling(g.Key.Good)})");
        }
        Check(world.Camps.Where(c => c.Player != 0).All(c => c.Population > 0) && world.History.Sum(h => h.Production.Count) > 0, "the villages live and produce");
        var worlds = new[] { Fresh(), Fresh() }; worlds[1].Camps.Reverse(); worlds[1].Societies.Reverse();
        var workers = new[] { new PhaseExecution(), new PhaseExecution(4, 2, true, 1) };
        for (int t = 0; t < 3; t++)
        {
            for (int i = 0; i < 2; i++) workersResolve(worlds[i], workers[i]);
            Check(SaveCodec.Fingerprint(worlds[0]) == SaveCodec.Fingerprint(worlds[1]), "DemandControl replays identically across workers and order after turn " + t);
        }
        var saved = Fresh(); workersResolve(saved, new PhaseExecution());
        var loaded = SaveCodec.Load(SaveCodec.Save(saved));
        Check(loaded.Config.EconomicAllocation == EconomicAllocationStrategy.DemandControl, "the strategy round trips");
        for (int t = 0; t < 2; t++) { workersResolve(saved, new PhaseExecution()); workersResolve(loaded, new PhaseExecution()); Check(SaveCodec.Fingerprint(saved) == SaveCodec.Fingerprint(loaded), "continued run equals the uninterrupted run after turn " + t); }
    }
    private static void workersResolve(WorldState w, PhaseExecution execution)
        => new TurnResolver(execution).Resolve(w, TurnPlanner.Prepare(w, new Dictionary<int, List<Order>>(), null, execution));
}
