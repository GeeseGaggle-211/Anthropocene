using System;
using System.Linq;
using Anthropocene.Simulation;

internal static class FishingInspectionChecks
{
    public static int Run()
    {
        int checks = 0;
        void Check(bool ok, string message) { checks++; if (!ok) throw new Exception("Fishing inspection: " + message); }
        foreach (bool lake in new[] { false, true })
        foreach (bool mixed in new[] { false, true })
        {
            var w = FishingEconomyChecks.Fixture(); var c = w.Camps[0];
            c.Technologies[(int)Technology.Cultivation] = mixed;
            w.Config.VisionRadius = 1;
            int[] route = { w.Map.At(11, 8), w.Map.At(12, 8), w.Map.At(13, 8) };
            foreach (int tile in route)
            {
                w.Tiles[tile].Environment = lake ? EnvironmentKind.Lake : EnvironmentKind.Shallows;
                w.Tiles[tile].Terrain = lake ? Terrain.Lake : Terrain.Shallows;
                c.People.Memory.Explored.Add(tile, w.Turn);
            }
            c.People.FishingGroundKnowledge.Add(new FishingGroundSnapshot { TileId = route[1],
                AccessibleFood = 42, Biomass = 100, Condition = .7, ObservedDays = w.Days, Capital = .2 });
            var intent = EconomicIntent.From(c);
            intent.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Fisher, Population = 6 });
            string before = SaveCodec.Fingerprint(w);
            var view = FishingInspection.Build(w, c.Player, c, intent);
            Check(view.Grounds.Length == 3 && view.Fishers == 6, "three legal grounds and explicit Fisher staffing");
            var reserving = EconomicIntent.From(c);
            reserving.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Fisher, Population = 6 });
            reserving.Labor = new EconomicLaborPlan { MaterialGatherers = 2.5 };
            var split = FishingInspection.Build(w, c.Player, c, reserving);
            Check(Math.Abs(split.MaterialGatherers - 2.5) < 1e-12 && Math.Abs(split.FoodGatherers + split.MaterialGatherers - split.Gatherers) < 1e-12,
                "Gatherer pool separates food work from the prepared material reservation");
            Check(view.Grounds.All(g => g.Path.Length == g.Plan.Distance + 1 && g.Path[0] == c.Tile && g.Path.Last() == g.Plan.TileId), "routes start at settlement and end at ground");
            Check(view.Grounds.All(g => g.Path.Zip(g.Path.Skip(1), (a,b) => w.Map.Neighbors(a).Contains(b)).All(x => x)), "every route edge is legal adjacency");
            var historical = view.Grounds.Single(g => g.Plan.TileId == route[1]);
            var unknown = view.Grounds.Single(g => g.Plan.TileId == route[2]);
            Check(historical.Plan.KnowledgeSource == FishingKnowledgeSource.Snapshot && historical.Plan.OpeningAccessibleFood == 42, "dated owner snapshot");
            Check(unknown.Plan.KnowledgeSource == FishingKnowledgeSource.ConservativePrior && unknown.Plan.RequestedCatch == 0, "unknown prior has no catch");
            Check(SaveCodec.Fingerprint(w) == before, "inspection is pure");
            w.Tiles[route[1]].Food = 99999; w.Tiles[route[1]].AquaticCondition = .01;
            var hidden = FishingInspection.Build(w, c.Player, c, intent);
            Check(hidden.Grounds.Single(g => g.Plan.TileId == route[1]).Plan.OpeningAccessibleFood == 42 && hidden.EquippedCatch == view.EquippedCatch, "hidden live state cannot affect inspection");
            var preview = EconomicAllocationStrategy.Preview(w, c, EconomicIntent.From(c));
            Check(preview.Stamp == EconomicIntents.Automatic(w, c, true).Stamp, "human preview equals AI strategy");
            var observation = new AgentObservationBuilder().Build(w, c.Player);
            Check(EconomicIntents.Validate(observation, observation.Settlements.Single(s => s.Id == c.Id), preview) == null, "preview is a valid shared Economy intent");
            bool rejected = false;
            try { FishingInspection.Build(w, c.Player + 1, c, intent); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "foreign camp rejected");
        }
        var replacementWorld = FishingEconomyChecks.Fixture();
        var replacementCamp = replacementWorld.Camps[0];
        var near = replacementWorld.Map.Neighbors(replacementCamp.Tile).OrderBy(t => t).Take(4).ToArray();
        foreach (int tile in near)
        {
            replacementWorld.Tiles[tile].Terrain = Terrain.Shallows;
            replacementWorld.Tiles[tile].WaterBody = WaterKind.Saltwater;
        }
        replacementCamp.LastSelectedFishingGrounds = near.Take(3).ToList();
        foreach (int tile in near.Take(3))
        {
            replacementWorld.Tiles[tile].Food = replacementWorld.Tiles[tile].PotentialFood = 0;
            for (int season = 0; season < 4; season++) replacementWorld.Tiles[tile].Seasons[season] = new SeasonData();
        }
        var replacement = FishingEconomy.BuildAccess(replacementWorld, replacementCamp, replacementCamp.Tile,
            replacementCamp.Population, replacementWorld.Days, replacementWorld.Config.TurnDays);
        Check(replacement.Grounds.Any(g => g.TileId == near[3] && g.YieldPerFisherYear > 0),
            "three exhausted incumbents cannot evict a productive newcomer at a zero cutoff");
        Console.WriteLine("PASS: " + checks + " Fishing inspection assertions"); return checks;
    }
}
