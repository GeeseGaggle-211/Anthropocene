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
        checks = 0; Config(); Purity(); ReserveFeedstockClosure(); IncomingFeedstockToolClosure(); HousingReference();
        TypedFeedstockPriority(); HousingMaterialCeiling(); Saturation(); FishingIntegration(); FishingMealBreakpoint(); FishingMaterialBootstrap(); Turns();
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

    private static void FishingMaterialBootstrap()
    {
        var w = World(EconomicAllocationStrategy.DemandControl, (6, 6, 100));
        var c = CampOf(w, 1); var cfg = w.Config;
        cfg.NeolithicFishingEnabled = cfg.CorrectedAquaticSeasonalPressureEnabled = true;
        cfg.FissionEnabled = cfg.ImmigrationEnabled = cfg.TradeEnabled = false;
        cfg.DemographicModel = DemographicModel.PhysicalMortalityV1;
        cfg.SurplusFundedBirths = cfg.ElderKernelSplit = true;
        c.Technologies[(int)Technology.Cultivation] = false;
        c.Technologies[(int)Technology.Watercraft] = true;
        c.Technology = Technology.Watercraft; c.IsVillage = false;
        c.People.NeolithicTechnologies.Clear();
        c.Inventory = new CampInventory(); c.PlantFood = 2000; c.AnimalFood = 0; c.AquaticFood = 9000;
        int shore = w.Map.Neighbors(c.Tile).OrderBy(id => id).First();
        w.Tiles[shore].Terrain = Terrain.Shallows; w.Tiles[shore].ElevationLevel = 0;
        w.Tiles[shore].WaterBody = WaterKind.Saltwater;
        w.Tiles[shore].Freshwater = 0; w.Tiles[shore].FreshwaterSource = FreshwaterSource.None;
        for (int s = 0; s < 4; s++) { w.Tiles[shore].Seasons[s].MaxFreshwater = 0; w.Tiles[shore].Seasons[s].WaterRecoveryPerDay = 0; }
        foreach (var t in w.Tiles)
        {
            t.Food = 100000; t.PotentialFood = 800000;
            for (int s = 0; s < 4; s++) { t.Seasons[s].MaxFood = 100000; t.Seasons[s].MaxPotentialFood = 800000; }
        }
        int count = w.Tiles.Length;
        w.Resources = new WorldResourceState {
            MaximumQuantity = 100000, CatalogHash = ResourceCatalog.Hash,
            ProfileHash = ResourceCatalog.Hash, ConfigurationHash = ResourceCatalog.Hash,
            Provinces = Array.Empty<GeologicalProvince>(), Province = new ushort[count],
            History = new ushort[count], Host = new byte[count], Marine = new byte[count],
            Stone = new ushort[count], Clay = new ushort[count], Wood = new ushort[count], WoodRecoveryPerYear = .05
        };
        foreach (var m in new[] { MaterialId.Stone, MaterialId.Clay, MaterialId.Wood }) w.Resources.AccessibleParts[(int)m] = 10000;
        w.Resources.Stone[c.Tile] = w.Resources.Clay[c.Tile] = w.Resources.Wood[c.Tile] = 1000;
        var orders = new Dictionary<int, List<Order>>();
        var bytes = SaveCodec.Save(w); var replay = SaveCodec.Load(bytes);
        var prepared = TurnPlanner.Prepare(w, orders);
        var intent = prepared.Orders.Single(o => o.Entity == c.Id && o.Kind == OrderKind.Economy).Economy;
        Check(intent.Labor.MaterialGatherers > 0, "Watercraft goods plan reserves raw-material gathering time");
        double pool = c.Population - intent.Assignments.Where(a => a.Role != WorkforceRoles.Gatherer).Sum(a => a.Population);
        double reserved = intent.Labor.MaterialGatherers;
        var preview = EconomicPlanner.Preview(c, intent, c.Population);
        var labor = new AgriculturalLabor { Gatherers = pool };
        int destination = w.Map.Neighbors(c.Tile).First(id => w.Tiles[id].TraversableLand);
        string beforeForecast = SaveCodec.Fingerprint(w);
        foreach (int tile in new[] { c.Tile, destination })
        {
            var forecast = FoodHorizonForecast.Settlement(w, preview, tile, w.Days, cfg.TurnDays, labor, double.MaxValue, 0);
            Check(forecast.MealFood <= (pool - reserved) * cfg.GatherPerPerson * cfg.GenerationIntervals + 1e-6,
                "passive food forecast honors material work at current and accepted locations");
        }
        preview.IsVillage = true;
        var departed = FoodHorizonForecast.Settlement(w, preview, destination, w.Days, cfg.TurnDays, labor, double.MaxValue, 0);
        Check(departed.MealFood > (pool - reserved) * cfg.GatherPerPerson * cfg.GenerationIntervals + 1e-6,
            "village departure releases the old economy reservation");
        Check(beforeForecast == SaveCodec.Fingerprint(w), "material-aware food forecasts are pure");
        var record = new TurnResolver(new PhaseExecution(1)).Resolve(w, prepared);
        double food = record.Transfers.Where(t => t.Entity == c.Id && (t.Kind == "Gathering" || t.Kind == "Marine gathering" || t.Kind == "Aquatic gathering")).Sum(t => t.Food);
        double materialWork = record.MaterialTransfers.Where(t => t.Camp == c.Id && t.Role == WorkforceRoles.Gatherer).Sum(t => t.WorkerIntervals);
        Check(food <= (pool - reserved) * cfg.GatherPerPerson * cfg.GenerationIntervals + 1e-6,
            "productive coast cannot spend reserved material work on passive food");
        Check(materialWork > 0 && food / cfg.GatherPerPerson + materialWork <= pool * cfg.GenerationIntervals + 1e-6,
            "real material extraction shares the finite gathering pool");
        Check(record.Production.Any(p => p.Camp == c.Id && p.Good == CraftGood.HandTools && p.Output > 0)
            && record.Production.Any(p => p.Camp == c.Id && p.Good == CraftGood.Cloth && p.Output > 0),
            "Fishing-only camp bootstraps tools and clothing from zero manufactured stock");
        new TurnResolver(new PhaseExecution(4)).Resolve(replay, TurnPlanner.Prepare(replay, orders));
        Check(SaveCodec.Fingerprint(w) == SaveCodec.Fingerprint(replay), "Fishing material bootstrap survives save/load and worker replay");
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
