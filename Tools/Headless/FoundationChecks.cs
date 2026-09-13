using System;
using System.Linq;
using Anthropocene.Simulation;

internal static class FoundationChecks
{
    private static int checks;
    private static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception("Foundation: " + message); }
    private static WorldState Fixture()
    {
        var cfg = SimulationConfig.Small(1); cfg.Width = 24; cfg.Height = 16;
        var w = new WorldState { Config = cfg, Generation = new GenerationReport(), Tiles = new Tile[384] };
        for (int i = 0; i < w.Tiles.Length; i++)
        {
            var t = new Tile { Id = i, Terrain = Terrain.Grassland, ElevationLevel = 1, Food = 40, PotentialFood = 400, Freshwater = 50, FreshwaterSource = FreshwaterSource.RainPool };
            for (int s = 0; s < 4; s++) t.Seasons[s] = new SeasonData { MaxFood = 80, MaxPotentialFood = 800, MaxFreshwater = 80, FoodRate = .3f, PotentialFoodRate = .3f, FreshwaterRate = .3f, Moisture = .5f };
            w.Tiles[i] = t;
        }
        w.Camps.Add(new Camp { Id = w.NextId++, Population = 24, Food = 4000, Tile = 100 }); Ownership.Bind(w); return w;
    }
    public static int Run()
    {
        checks = 0;
        var cfg = SimulationConfig.Small(1);
        Check(Vegetation.Classify(-11, .8, cfg) == EnvironmentKind.Ice && Vegetation.Classify(0, .8, cfg) == EnvironmentKind.Cold && Vegetation.Classify(30, .1, cfg) == EnvironmentKind.Desert, "environment precedence preserves climate breakpoints");
        Check(Vegetation.Classify(25, .5, cfg) == EnvironmentKind.Tropical && Vegetation.Classify(15, .2, cfg) == EnvironmentKind.Grassland && Vegetation.Classify(15, .6, cfg) == EnvironmentKind.Temperate, "environment is independent of relief");
        var cover = new Tile { Terrain = Terrain.Woodland, Relief = Relief.Mountain };
        for (int i = 0; i < 4; i++) cover.Seasons[i] = new SeasonData { Temperature = 15, Moisture = .8f, MaxFood = 100 };
        Check(Vegetation.Potential(cover, cfg) == VegetationTier.High, "wet highland retains high vegetation potential");
        cover.CurrentVegetation = VegetationTier.High; FoodEcology.ConfigurePotential(cover, cfg, true); double biomassCapacity = cover.Seasons[0].MaxPotentialFood;
        cover.CurrentVegetation = VegetationTier.Low;
        Check(cover.Seasons[0].MaxPotentialFood == biomassCapacity && Math.Abs(FoodEcology.GatherableFraction(cover, cfg) - .1) < 1e-10, "cover loss reduces accessibility without creating biomass");
        Check(Vegetation.Update(VegetationTier.High, VegetationTier.High, .2, cfg) == VegetationTier.Low && Vegetation.Update(VegetationTier.High, VegetationTier.Low, .9, cfg) == VegetationTier.High, "long intervals cross multiple vegetation thresholds");
        Check(Vegetation.Update(VegetationTier.Medium, VegetationTier.Low, .5, cfg) == VegetationTier.Low && Vegetation.Update(VegetationTier.Medium, VegetationTier.Medium, .5, cfg) == VegetationTier.Medium, "hysteresis retains the previous tier inside its band");
        var w = Fixture(); var first = w.Camps[0];
        var daughter = new Camp { Id = w.NextId++, SocietyId = first.SocietyId, Player = first.Player, Population = 10, Tile = 180, Food = 200 };
        w.Camps.Add(daughter); Ownership.Bind(w); first.Experience[1] = 70;
        Check(w.Societies.Count == 1 && daughter.Experience[1] == 70, "one society shares tradition across two local populations");
        daughter.Food = 75; Check(first.Food == 4000 && first.Population == 24, "settlement inventory/demography remains local");
        var copy = SaveCodec.Load(SaveCodec.Save(w)); copy.Camps[0].Experience[2] = 55;
        Check(copy.Societies.Count == 1 && copy.Camps[1].Experience[2] == 55, "save/load rebinds shared ownership");
        var ledger = new PopulationLedger(); ledger.Assign("craft", 5, 20); ledger.Assign("tend", 7, 20); ledger.Reassign("craft", "tend", 2, 20); ledger.Release("tend", 1, 20);
        Check(ledger.Gatherers(20) == 9 && ledger.Assignments.Single(a => a.Role == "craft").Population == 3, "assignment/reassignment/release uses resident pool");
        var reversed = new PopulationLedger { Assignments = ledger.Assignments.Select(a => new PopulationAssignment { Role = a.Role, Population = a.Population }).Reverse().ToList() };
        ledger.ApplyCasualties(20, 7); reversed.ApplyCasualties(20, 7);
        Check(ledger.Assignments.Select(a => a.Role + a.Population).SequenceEqual(reversed.Assignments.Select(a => a.Role + a.Population)) && ledger.Gatherers(13) == 6, "casualties proportionally share losses independent of role-list order");
        bool rejected = false; try { ledger.Assign("craft", 100, 13); } catch (InvalidOperationException) { rejected = true; } Check(rejected, "overassignment rejected atomically");
        foreach (int turnYears in new[] { 10, 20 })
        {
            w = Fixture(); first = w.Camps[0]; w.Config.TurnDays = turnYears * 365; first.Experience = new[] { 20.0, 60, 10 }; w.Days = 199 * 365; w.Turn = 50;
            TurnResolver.Breakthrough(w, first, new TurnRecord()); Check(first.Technology == Technology.None, "turn number cannot force progression");
            w.Days = 200 * 365; TurnResolver.Breakthrough(w, first, new TurnRecord()); Check(first.Technology == Technology.Cultivation && w.Societies[0].Progression.Era == Era.Neolithic, "maturity is 200 years at either duration");
            w = Fixture(); first = w.Camps[0]; first.Experience = new[] { 10.0, 10, 10 }; w.Days = 240 * 365;
            TurnResolver.Breakthrough(w, first, new TurnRecord()); Check(first.ForcedTransition && w.Finished && w.Societies[0].TransitionSeeds.Count == 1, "forced completion is explicit with preserved transition seed");
        }
        w = Fixture(); first = w.Camps[0]; w.Config.OpeningCompatibilityEffects = false; first.Experience[0] = w.Config.ExperienceThreshold; TurnResolver.Breakthrough(w, first, new TurnRecord());
        Check(w.Herds.Count == 0 && w.Societies[0].TransitionSeeds.Single().Technology == Technology.AnimalHusbandry, "core husbandry creates a transition seed without inventing livestock");
        w = Fixture(); first = w.Camps[0]; first.Experience[1] = w.Config.ExperienceThreshold; TurnResolver.Breakthrough(w, first, new TurnRecord());
        Check(w.Improvements.Single().Society == first.SocietyId && w.Improvements[0].Settlement == first.Id, "opening compatibility improvement has explicit owner and manager");
        return checks;
    }
}
