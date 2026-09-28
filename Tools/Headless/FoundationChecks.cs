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
            for (int s = 0; s < 4; s++) t.Seasons[s] = new SeasonData { MaxFood = 80, MaxPotentialFood = 800, MaxFreshwater = 80, FoodRecoveryPerDay = (float)EcologyMath.DailyRate(.3, 91.25), BiomassRecoveryPerDay = (float)EcologyMath.DailyRate(.3, 91.25), WaterRecoveryPerDay = (float)EcologyMath.DailyRate(.3, 91.25), Moisture = .5f };
            w.Tiles[i] = t;
        }
        w.Camps.Add(new Camp { Id = w.NextId++, Population = 24, Food = 4000, Tile = 100 }); Ownership.Bind(w); return w;
    }
    public static int Run()
    {
        checks = 0;
        foreach (var invalid in new[] { new SimulationConfig { MediumVegetationUp = double.NaN }, new SimulationConfig { ForcedTransitionYears = 100 }, new SimulationConfig { FoodRegeneration = 1 } })
        {
            bool invalidRejected = false; try { invalid.Validate(); } catch (ArgumentException) { invalidRejected = true; }
            Check(invalidRejected, "invalid environmental rates or era thresholds are rejected before generation");
        }
        var cfg = SimulationConfig.Small(1);
        double landAccessScale = cfg.RichGatherableFraction / .25;
        Check(landAccessScale > 0 &&
            Math.Abs(cfg.GrasslandGatherableFraction / .10 - landAccessScale) < 1e-12 &&
            Math.Abs(cfg.AridGatherableFraction / .05 - landAccessScale) < 1e-12 &&
            Math.Abs(cfg.TundraGatherableFraction / .08 - landAccessScale) < 1e-12,
            "all natural land gatherable shares use one uniform calibration factor");
        Check(Vegetation.Classify(-11, .8, cfg) == EnvironmentKind.Ice && Vegetation.Classify(0, .8, cfg) == EnvironmentKind.Cold && Vegetation.Classify(30, .1, cfg) == EnvironmentKind.Desert, "environment precedence preserves climate breakpoints");
        Check(Vegetation.Classify(25, .5, cfg) == EnvironmentKind.Tropical && Vegetation.Classify(15, .2, cfg) == EnvironmentKind.Grassland && Vegetation.Classify(15, .6, cfg) == EnvironmentKind.Temperate, "environment is independent of relief");
        var cover = new Tile { Terrain = Terrain.Woodland, Relief = Relief.Mountain };
        for (int i = 0; i < 4; i++) cover.Seasons[i] = new SeasonData { Temperature = 15, RegionalMoisture = .8f, Moisture = .8f, MaxFood = 100 };
        Check(Vegetation.Potential(cover, cfg) == VegetationTier.High, "wet highland retains high vegetation potential");
        cover.CurrentVegetation = VegetationTier.High; FoodEcology.ConfigurePotential(cover, cfg, true); double biomassCapacity = cover.Seasons[0].MaxPotentialFood;
        cover.CurrentVegetation = VegetationTier.Low;
        Check(cover.Seasons[0].MaxPotentialFood == biomassCapacity && Math.Abs(FoodEcology.GatherableFraction(cover, cfg) - cfg.GrasslandGatherableFraction) < 1e-10, "cover loss reduces accessibility without creating biomass");
        Check(Vegetation.Update(VegetationTier.High, VegetationTier.High, .2, cfg) == VegetationTier.Low && Vegetation.Update(VegetationTier.High, VegetationTier.Low, .9, cfg) == VegetationTier.High, "long intervals cross multiple vegetation thresholds");
        Check(Vegetation.Update(VegetationTier.Medium, VegetationTier.Low, .5, cfg) == VegetationTier.Low && Vegetation.Update(VegetationTier.Medium, VegetationTier.Medium, .5, cfg) == VegetationTier.Medium, "hysteresis retains the previous tier inside its band");
        var w = Fixture(); var first = w.Camps[0];
        var daughter = new Camp { Id = w.NextId++, SocietyId = first.SocietyId, Player = first.Player, Population = 10, Tile = 180, Food = 200 };
        w.Camps.Add(daughter); Ownership.Bind(w); first.Experience[1] = 70;
        Check(w.Societies.Count == 1 && daughter.Experience[1] == 70, "one society shares tradition across two local populations");
        daughter.Food = 75; Check(first.Food == 4000 && first.Population == 24, "settlement inventory/demography remains local");
        var societyOrder = w.Societies.Select(s => s.Id).ToArray(); var nextId = w.NextId;
        SaveCodec.Save(w); SaveCodec.Fingerprint(w); Check(w.NextId == nextId && w.Societies.Select(s => s.Id).SequenceEqual(societyOrder), "save and fingerprint preserve authoritative ownership and ordering");
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
        SecondaryDiscoveries();
        return checks;
    }

    private static void SecondaryDiscoveries()
    {
        var w = Fixture(); var camp = w.Camps[0];
        camp.Experience = new[] { 225.0, 225.0, 0.0 };
        TurnResolver.Breakthrough(w, camp, new TurnRecord());
        Check(camp.Technology == Technology.AnimalHusbandry && camp.People.Progression.Era == Era.Neolithic, "opening tie keeps its primary route");
        var seeds = camp.People.TransitionSeeds.Count; var herds = w.Herds.Count; var improvements = w.Improvements.Count;
        TechnologyDiscovery.Resolve(w, new TurnRecord());
        Check(camp.Technologies[(int)Technology.Cultivation] && camp.Technology == Technology.AnimalHusbandry && camp.People.TransitionSeeds.Count == seeds && w.Herds.Count == herds && w.Improvements.Count == improvements, "eligible secondary domain unlocks without opening grants");
        TechnologyDiscovery.Resolve(w, new TurnRecord());
        Check(camp.People.TransitionSeeds.Count == seeds && w.Herds.Count == herds && w.Improvements.Count == improvements, "secondary discovery is idempotent");
        OpeningTransitions.Apply(w, camp, Technology.Watercraft);
        Check(camp.People.TransitionSeeds.Count == seeds && w.Improvements.Count == improvements && !camp.Technologies[(int)Technology.Watercraft], "opening compatibility cannot be replayed as a later discovery");
        var saved = SaveCodec.Load(SaveCodec.Save(w));
        Check(saved.Camps[0].Technologies[(int)Technology.Cultivation] && saved.Camps[0].People.OpeningTransitionProcessed, "secondary unlock and opening marker survive save/load");
        var status = TechnologyDiscovery.Statuses(saved, saved.Camps[0].People).Single(s => s.Technology == Technology.Cultivation);
        Check(status.Acquired && status.Progress >= status.Threshold, "owned status exposes acquired domain and threshold");
        saved.Camps[0].People.TransitionSeeds.Clear();
        saved.Camps[0].People.OpeningTransitionProcessed = false;
        saved.Camps[0].People.OpeningTransitionProcessedInitialized = false;
        var migrated = SaveCodec.Load(SaveCodec.Save(saved));
        Check(migrated.Camps[0].People.OpeningTransitionProcessed && migrated.Camps[0].People.TransitionSeeds.Count == 0, "legacy completed era with no transition seed cannot replay an opening reward");
        var fresh = Fixture();
        var freshCopy = SaveCodec.Load(SaveCodec.Save(fresh));
        Check(freshCopy.Camps[0].People.OpeningTransitionProcessedInitialized && !freshCopy.Camps[0].People.OpeningTransitionProcessed, "new Paleo save retains legitimate false opening marker");
        Check(freshCopy.Camps[0].Experience.All(x => x == 0), "legitimate zero domain progress survives save/load");

        var forced = Fixture(); var forcedCamp = forced.Camps[0];
        forcedCamp.Experience = new[] { 200.0, 180.0, 0.0 }; forced.Days = 240 * 365;
        TurnResolver.Breakthrough(forced, forcedCamp, new TurnRecord());
        TechnologyDiscovery.Resolve(forced, new TurnRecord());
        Check(forcedCamp.ForcedTransition && forcedCamp.SecondaryDomain == (int)Domain.Gathering &&
            forcedCamp.Technologies[(int)Technology.Cultivation] && forcedCamp.Experience[(int)Domain.Gathering] == 180 &&
            TechnologyDiscovery.Statuses(forced, forcedCamp.People)[(int)Domain.Gathering].Progress == 225,
            "forced first transition carries secondary practice to the existing 225-point threshold without changing raw experience");

        var natural = NeolithicChecks.Fixture(false); var first = natural.Camps[0];
        first.Population = 24;
        first.Experience = new[] { 225.0, 225.0, 0.0 };
        var before = new AgentObservationBuilder().Build(natural, first.Player);
        Check(!before.TechnologyStatuses.Any(s => s.Acquired), "prior observation cannot see future discoveries");
        var turn = new TurnResolver().Resolve(natural, new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<Order>>());
        Check(first.Technology == Technology.AnimalHusbandry && first.Technologies[(int)Technology.Cultivation] && first.People.TransitionSeeds.Count == 1 &&
            turn.Events.Count(e => e.Contains("entered the Neolithic")) == 1 && turn.Events.Count(e => e.Contains("discovered Cultivation through")) == 1,
            "first and independently eligible secondary acquisition commit in the same Close with distinct events");
        var after = new AgentObservationBuilder().Build(natural, first.Player);
        Check(after.TechnologyStatuses.Single(s => s.Technology == Technology.Cultivation).Acquired && after.Settlements.Single().Agriculture,
            "second capability is visible and usable only at the next preparation boundary");

        var earned = NeolithicChecks.Fixture(false); var workerCamp = earned.Camps[0];
        workerCamp.Population = 24; workerCamp.Food = workerCamp.PlantFood = 0;
        workerCamp.Technology = Technology.AnimalHusbandry;
        workerCamp.Technologies[(int)Technology.AnimalHusbandry] = true;
        workerCamp.People.Progression.Era = Era.Neolithic;
        workerCamp.Experience[(int)Domain.Gathering] = 224;
        var farmerIntent = new EconomicIntent { Assignments = new System.Collections.Generic.List<PopulationAssignment> { new PopulationAssignment { Role = WorkforceRoles.Farmer, Population = 1 } } };
        var priorView = new AgentObservationBuilder().Build(earned, workerCamp.Player);
        Check(EconomicIntents.Validate(priorView, priorView.Settlements.Single(), farmerIntent) != null,
            "a Farmer order cannot be prepared before this horizon's earned discovery");
        var earnedTurn = new TurnResolver().Resolve(earned, new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<Order>>());
        Check(earnedTurn.Transfers.Any(t => t.Kind == "Gathering" && t.Food > 0) && workerCamp.Experience[(int)Domain.Gathering] >= 225 &&
            workerCamp.Technologies[(int)Technology.Cultivation] && !earnedTurn.Transfers.Any(t => t.Kind == "Cultivation") &&
            earned.Tiles.All(t => t.CultivationCapital == 0), "real gathering crosses the threshold at Close without retroactive Farmer output or fields");
        var nextView = new AgentObservationBuilder().Build(earned, workerCamp.Player);
        Check(EconomicIntents.Validate(nextView, nextView.Settlements.Single(), farmerIntent) == null,
            "the acquired Farmer capability is legal at the next preparation boundary");

        w = Fixture(); camp = w.Camps[0]; camp.Experience = new[] { 224.0, 224.0, 224.0 };
        camp.People.Progression.Era = Era.Neolithic; camp.Technology = Technology.Cultivation; camp.Technologies[(int)Technology.Cultivation] = true;
        w.Days = 1000 * 365; TechnologyDiscovery.Resolve(w, new TurnRecord());
        Check(!camp.Technologies[(int)Technology.AnimalHusbandry] && !camp.Technologies[(int)Technology.Watercraft], "time and subthreshold practice cannot unlock a secondary technology");
        camp.Experience[(int)Domain.Fishing] = 225;
        TechnologyDiscovery.Resolve(w, new TurnRecord());
        Check(camp.Technologies[(int)Technology.Watercraft] && w.Improvements.Count == 0 && w.Herds.Count == 0, "fishing practice unlocks Watercraft without a fishery or herd");
    }
}
