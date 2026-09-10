using System;
using System.Collections.Generic;
using System.Linq;
using Anthropocene.Simulation;

internal static class Milestone3Checks
{
    private static int checks;
    private static void Check(bool condition, string message) { checks++; if (!condition) throw new Exception("Milestone 3: " + message); }
    private static Dictionary<int, List<Order>> Idle() => new Dictionary<int, List<Order>>();
    internal static WorldState Flat(float capacity = 10000, float recovery = 1)
    {
        var cfg = SimulationConfig.Small(1); cfg.Width = 24; cfg.Height = 16; cfg.ExperienceThreshold = 1000000;
        var w = new WorldState { Config = cfg, Seed = 13, Tiles = new Tile[384], Generation = new GenerationReport() };
        for (int i = 0; i < w.Tiles.Length; i++)
        {
            var t = new Tile { Id = i, Terrain = Terrain.Grassland, Elevation = .55f, ElevationLevel = 2, Food = capacity, Freshwater = 60, FreshwaterSource = FreshwaterSource.RainPool };
            for (int s = 0; s < 4; s++) t.Seasons[s] = new SeasonData { MaxFood = capacity, FoodRate = recovery, Moisture = 1, MaxFreshwater = 80, FreshwaterRate = .3f };
            w.Tiles[i] = t;
        }
        w.Camps.Add(new Camp { Id = w.NextId++, Tile = 150, Player = 0, Population = 24, Food = 4000 }); return w;
    }
    private static void Sea(Tile t, bool open = false)
    {
        t.Terrain = open ? Terrain.Ocean : Terrain.Shallows; t.ElevationLevel = 0; t.WaterBody = WaterKind.Saltwater; t.FreshwaterSource = FreshwaterSource.None; t.Freshwater = 0; t.Saltwater = 100; t.Food = 200;
        for (int s = 0; s < 4; s++) t.Seasons[s] = new SeasonData { MaxFood = 200, FoodRate = .1f, MaxSaltwater = 100 };
    }
    public static void Run()
    {
        var resolver = new TurnResolver(); var w = Flat(); var shortRun = SaveCodec.Load(SaveCodec.Save(w)); shortRun.Config.TurnDays /= 2;
        var result = resolver.Resolve(w, Idle()); var shorter = resolver.Resolve(shortRun, Idle()); var report = result.Demographics.Single();
        double expected = 24 * Math.Exp(w.Config.AnnualHumanGrowthRate * 20);
        Check(Math.Abs(w.Camps[0].Population + w.Camps[0].GrowthRemainder - expected) < 1e-8, "abundant food yields annualized exponential growth over 20 years");
        Check(w.Camps[0].Population > 24 && w.Days == 7300 && report.Years == 20, "a generation advances historical time and adds whole people");
        Check(report.FoodRequired > 24 * 80 && report.FoodRequired > shorter.Demographics[0].FoodRequired * 2, "food demand grows with population within the generation");
        Check(result.Growth > shorter.Growth && shortRun.Days == 3650, "shorter technological-era duration scales demographics and clock");
        Check(report.StartingPopulation + report.Additions - report.Deaths == report.EndingPopulation && report.Explanation.Contains("Population") && report.LimitingFactor.Contains("surplus"), "demographic conservation and visible explanation data are explicit");
        Check(w.Tiles.All(t => t.Food >= 0 && t.Food <= t.Seasons[0].MaxFood), "long turns replenish bounded stocks instead of draining one seasonal snapshot eighty times");
        var standardCapacity = new SimulationConfig { Players = 80 }; standardCapacity.Validate();
        bool rejectedSmall = false; try { SimulationConfig.Small(17).Validate(); } catch (ArgumentException) { rejectedSmall = true; }
        Check(rejectedSmall && SimulationConfig.MaximumPlayersFor(1024, 512) == 80, "tribe limits allow eighty only on worlds large enough to place them");
        w = Flat(); var provisionedHunter = new Hunter { Id = w.NextId++, Player = 0, SourceCamp = w.Camps[0].Id, Tile = w.Camps[0].Tile, Population = 8 }; w.Hunters.Add(provisionedHunter);
        var provisionRecord = new TurnRecord(); Supply.ProvisionDepartures(w, new List<Order> { new Order(OrderKind.Move, provisionedHunter.Id, destination: provisionedHunter.Tile + 2) }, provisionRecord);
        Check(Math.Abs(provisionedHunter.CarriedFood - 8 * w.Config.Consumption * w.Config.GenerationIntervals) < 1e-8, "a departing hunter group can carry one complete generation of provisions");
        w = Flat(); foreach (var tile in w.Tiles) for (int season = 0; season < 4; season++) { var ecology = tile.Seasons[season]; ecology.Temperature = 30; ecology.Moisture = 1; tile.Seasons[season] = ecology; }
        double warmStorage = TurnResolver.StorageGenerations(w, w.Camps[0].Tile);
        foreach (var tile in w.Tiles) for (int season = 0; season < 4; season++) { var ecology = tile.Seasons[season]; ecology.Temperature = -18; ecology.Moisture = .05f; tile.Seasons[season] = ecology; }
        double coldStorage = TurnResolver.StorageGenerations(w, w.Camps[0].Tile);
        Check(Math.Abs(warmStorage - 1) < 1e-8 && coldStorage > 3.5 && coldStorage <= 4, "warm moist storage lasts one generation while cold dry storage approaches four");
        w = Flat(); var growingHerd = new Herd { Id = w.NextId++, Tile = 160, Animals = 149, Capacity = 150, Species = "antelope" }; w.Herds.Add(growingHerd);
        int herdPopulation = w.Herds.Sum(h => h.Animals); var herdTurn = resolver.Resolve(w, Idle());
        Check(w.Herds.Count > 1 && w.Herds.All(h => h.Animals <= h.Capacity) && w.Herds.Sum(h => h.Animals) > herdPopulation && herdTurn.Events.Any(e => e.Contains("divided")), "well-fed herds grow toward 150 and split deterministically above their limit");
        w = Flat(30, .3f); w.Camps[0].Food = 48;
        for (int i = 0; i < 4; i++) resolver.Resolve(w, Idle()); int settled = w.Camps[0].Population;
        var plateau = resolver.Resolve(w, Idle()).Demographics[0];
        Check(settled > 24 && settled < 120 && w.Camps[0].Population <= settled && plateau.Additions == 0 && plateau.FoodReceived < plateau.FoodRequired, "limited recurring food creates carrying pressure without an arbitrary population cap");
        w = Flat(0, 0); w.Camps[0].Food = 0; resolver.Resolve(w, Idle()); resolver.Resolve(w, Idle()); var declining = resolver.Resolve(w, Idle()).Demographics[0];
        Check(declining.Deaths > 0 && declining.EndingPopulation < 24 && declining.Additions == 0 && declining.LimitingFactor.Contains("deficit"), "food deficits cause recorded decline with a visible limiting factor");
        w = Flat(0, 0); w.Camps[0].Food = 10000; var noSurplus = resolver.Resolve(w, Idle()).Demographics[0];
        Check(noSurplus.Additions == 0 && noSurplus.EndingPopulation == 24 && noSurplus.LimitingFactor.Contains("surplus"), "stored reserves alone do not claim sustainable growth");
        w = Flat(); w.Config.TurnDays = 1; var rounding = resolver.Resolve(w, Idle()).Demographics[0];
        Check(rounding.Additions == 0 && rounding.FractionalProgress > 0 && rounding.LimitingFactor.Contains("rounding"), "sub-person growth is retained and explained");
        w = Flat(0, 0); w.Config.TurnDays = w.Config.BaseDays; w.Config.AnnualHumanGrowthRate = 0; var camp = w.Camps[0]; camp.Food = 0;
        int coast = w.Map.Neighbors(camp.Tile).First(); Sea(w.Tiles[coast]); double stock = w.Tiles[coast].Food;
        var coastal = resolver.Resolve(w, Idle()); var marine = coastal.Transfers.Where(t => t.Kind == "Marine gathering").ToArray();
        Check(marine.Sum(t => t.Food) > 0 && camp.Food > 0 && w.Tiles[coast].Food < stock, "a poor-land coastal camp automatically gathers positive marine food and source stock changes");
        Check(marine.All(t => t.Freshwater == 0) && w.Tiles[coast].DrinkableWater == 0 && coastal.Demographics[0].MarineFood > 0, "marine gathering is recorded separately and grants no freshwater");
        w = Flat(200, .1f); w.Config.TurnDays = w.Config.BaseDays; w.Config.AnnualHumanGrowthRate = 0; Sea(w.Tiles[coast]);
        var mixed = resolver.Resolve(w, Idle());
        Check(mixed.Transfers.Where(t => t.Kind.Contains("gathering") || t.Kind == "Gathering").Sum(t => t.Food) <= 24 * w.Config.GatherPerPerson + 1e-8, "marine and terrestrial gathering share one labor budget");
        w = Flat(0, 0); w.Config.TurnDays = w.Config.BaseDays; Sea(w.Tiles[coast], true); Sea(w.Tiles[152]);
        Check(!resolver.Resolve(w, Idle()).Transfers.Any(t => t.Kind == "Marine gathering" && t.Food > 0), "open ocean and nonadjacent shallows are excluded from passive gathering");
        w = Flat(); var h = new Hunter { Id = w.NextId++, Player = 0, SourceCamp = w.Camps[0].Id, Tile = 150, Population = 8 }; w.Hunters.Add(h);
        w.Tiles[151].ElevationLevel = 10;
        Check(!w.CanEnter(151, false) && !w.CanEnter(151, true, w.Camps[0]) && resolver.Validate(w, 0, new Order(OrderKind.Move, h.Id, destination: 151)) != null, "peaks reject camp and hunter movement");
        var animal = new Herd { Id = w.NextId++, Tile = 150, Species = "goat", Animals = 6 }; w.Herds.Add(animal);
        Check(TurnResolver.HerdDestination(w, animal) != 151 && w.Config.GetSpecies("goat").Suitability(w.Tiles[151]) == 0, "peaks are unavailable to herds including goats");
        var view = new AgentObservationBuilder().Build(w, 0);
        Check(view.Tiles[151].ElevationLevel == 10 && new HeuristicTribePolicy().Decide(view, w.Camps[0].Mandate).Orders.All(o => o.Destination != 151), "AI observations and movement preserve peak blocking");
        w.Hunters.Add(new Hunter { Id = w.NextId++, Player = 0, SourceCamp = w.Camps[0].Id, Tile = 150, Population = 3, CarriedFood = 5 });
        w.Herds.Add(new Herd { Id = w.NextId++, Tile = 150, Species = "horse", Animals = 4, DomesticCamp = w.Camps[0].Id });
        view = new AgentObservationBuilder().Build(w, 0); var occupants = TileDetails.Occupants(view, 150);
        Check(occupants.Length == 5 && occupants.Count(p => p.SelectableId >= 0) == 3 && occupants.Select(p => p.Id).Distinct().Count() == 5, "stack data contains camp, both friendly groups, wild herd and domestic livestock");
        string detail = TileDetails.Describe(view, 150, new[] { new Order(OrderKind.Move, h.Id, destination: 152) });
        Check(detail.Contains("OCCUPANTS") && detail.Contains("QUEUED") && detail.Contains("soil moisture") && detail.Contains("maximum") && detail.Contains("Domestic"), "hover details expose all requested visible groups and queued orders");
        int hidden = Enumerable.Range(0, w.Tiles.Length).First(n => !view.Tiles.ContainsKey(n));
        w.Tiles[hidden].Food = 987654; w.Herds.Add(new Herd { Id = w.NextId++, Tile = hidden, Species = "camel", Animals = 500 });
        var changed = new AgentObservationBuilder().Build(w, 0);
        Check(TileDetails.Describe(changed, hidden, new Order[0]) == "" && TileDetails.Describe(changed, 150, new[] { new Order(OrderKind.Move, h.Id, destination: 152) }) == detail, "hover data cannot reveal hidden tile or herd changes");
        var hover = new HoverTiming(); hover.Observe(1, 100, 100, 0, "view", 2, .2, .15, 5); hover.Observe(1, 100, 100, 2, "view", 2, .2, .15, 5);
        Check(hover.Alpha == 0, "hover does not appear until more than two seconds");
        hover.Observe(1, 100, 100, 2.1, "view", 2, .2, .15, 5); Check(hover.Alpha > 0 && hover.Alpha < 1 && hover.Displayed == 1, "hover fades in after its delay");
        hover.Observe(2, 100, 100, 2.15, "view", 2, .2, .15, 5); Check(hover.Alpha > 0 && hover.Alpha < .5, "tile change begins prompt smooth fade-out");
        hover.Observe(2, 100, 100, 4.2, "pan", 2, .2, .15, 5); Check(hover.Alpha == 0, "pan or zoom context resets the delay");
        hover.Observe(2, 106, 100, 6.3, "pan", 2, .2, .15, 5); Check(hover.Alpha == 0, "meaningful movement resets hover even within the tile");
        hover.Observe(-1, 106, 100, 9, "pan", 2, .2, .15, 5); Check(hover.Displayed == -1, "leaving the map clears the overlay");
        var countConfig = new SimulationConfig();
        int[] counts = { 1, 9, 10, 14, 15, 25, 99, 100, 101, 199, 200, 249 };
        string[] labels = { "fewer than 10", "fewer than 10", "~10", "~10", "~20", "~30", "~100", "~100", "100+", "100+", "200+", "200+" };
        for (int i = 0; i < counts.Length; i++) Check(HerdCountKnowledge.Observe(counts[i], countConfig).Label == labels[i], "centralized herd count boundary " + counts[i]);
        w = Flat(); var wild = new Herd { Id = w.NextId++, Tile = 150, Animals = 41, Capacity = 200, Species = "camel" }; w.Herds.Add(wild);
        var livestock = new Herd { Id = w.NextId++, Tile = 150, Animals = 17, Capacity = 200, Species = "goat", DomesticCamp = w.Camps[0].Id }; w.Herds.Add(livestock);
        var observer = new AgentObservationBuilder(); var firstView = observer.Build(w, 0);
        Check(firstView.Herds.First(h => h.Id == livestock.Id).Count.Exact && firstView.Herds.First(h => h.Id == livestock.Id).Count.Label == "17", "own domestic livestock exposes a managed exact count");
        var outsider = new HerdObservation(livestock, countConfig, 0, 1);
        Check(!outsider.Count.Exact && outsider.Count.Label == "~20", "foreign domestic livestock remains approximate");
        string firstDescription = TileDetails.Describe(firstView, 150, new Order[0]);
        wild.Animals = 44; var equivalent = observer.Build(w, 0);
        Check(firstDescription == TileDetails.Describe(equivalent, 150, new Order[0]) && firstView.Herds[0].Size == equivalent.Herds[0].Size && equivalent.Herds[0].Count.Representative == 40, "hover, inspector, badge and AI receive the same estimate within a count bucket");
        var savedCounts = SaveCodec.Load(SaveCodec.Save(w));
        Check(TileDetails.Describe(observer.Build(savedCounts, 0), 150, new Order[0]) == TileDetails.Describe(equivalent, 150, new Order[0]), "count estimates are deterministic across save/load without exposing raw wild counts");
        w.Config.TurnDays = w.Config.BaseDays; savedCounts.Config.TurnDays = w.Config.BaseDays; resolver.Resolve(w, Idle()); resolver.Resolve(savedCounts, Idle());
        Check(SaveCodec.Fingerprint(w) == SaveCodec.Fingerprint(savedCounts), "approximate knowledge does not alter authoritative herd replay");
        Console.WriteLine("PASS: " + checks + " milestone-three assertions");
    }
}
