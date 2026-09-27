using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Anthropocene.Simulation;

// Bounded, disclosed generated-site controls. These do not represent normal
// placement frequency: the camp is relocated before play and held stationary
// through an unmodified activity-driven breakthrough.
internal static class GeneratedSiteControls
{
    internal sealed class Boundary
    {
        public string Scenario, InitialHabitat, Habitat, Route, Rule, SourceCounts, Mobility;
        public int Seed, CampId, UnlockTurn, Tile;
        public double StoneAvailable, ClayAvailable, WoodAvailable, Moisture, Population, Food;
        public bool River;
        public int Low, Medium, High, Moves;
        public byte[] Save;
        public string Status;
    }

    private sealed class Site { public string Label; public int Seed, Tile; }
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true };
    private static readonly Site[] Sites =
    {
        new Site { Label = "pinned-site-02-3682", Seed = 2, Tile = 3682 },
        new Site { Label = "pinned-site-15-3819", Seed = 15, Tile = 3819 },
        new Site { Label = "pinned-site-11-4530", Seed = 11, Tile = 4530 },
        new Site { Label = "pinned-site-17-4373", Seed = 17, Tile = 4373 }
    };

    public static List<Boundary> Capture(SimulationConfig config, ResourceGenerationOptions resources, int maxDiscoveryTurns)
    {
        var result = new List<Boundary>();
        foreach (var site in Sites)
        {
            var world = new WorldGenerator().Generate(site.Seed,
                JsonSerializer.Deserialize<SimulationConfig>(JsonSerializer.Serialize(config, Json), Json), resources);
            if (site.Tile < 0 || site.Tile >= world.Tiles.Length) throw new InvalidOperationException("Generated-site tile is outside its map: " + site.Seed + "/" + site.Tile);
            Camp camp = world.Camps.OrderBy(c => c.Id).First();
            world.Camps = new List<Camp> { camp };
            world.Hunters.Clear();
            world.Societies = world.Societies.Where(s => s.Id == camp.SocietyId).ToList();
            world.Herds = world.Herds.Where(h => h.DomesticCamp < 0 || h.DomesticCamp == camp.Id).ToList();
            world.Improvements = world.Improvements.Where(i => i.Settlement == camp.Id).ToList();
            camp.Tile = site.Tile; camp.Moves = 0; camp.StationaryTurns = 0; camp.StationaryYears = 0;
            Ownership.Bind(world);
            string initialHabitat = HabitatAt(world, camp.Tile);

            string route = null, rule = null; int unlockTurn = -1;
            var resolver = new TurnResolver(new PhaseExecution());
            for (int turn = 0; turn < maxDiscoveryTurns && world.LinkedPopulation(camp) > 0; turn++)
            {
                var execution = new PhaseExecution();
                var proposal = AiPlanning.Decide(world, execution);
                var siteOrders = proposal.Orders[camp.Player].Where(o => o.Entity != camp.Id || o.Kind != OrderKind.Move).ToList();
                if (!siteOrders.Any(o => o.Entity == camp.Id) ) siteOrders.Add(new Order(OrderKind.NoOp, camp.Id));
                var orders = new Dictionary<int, List<Order>> { [camp.Player] = siteOrders };
                var prepared = TurnPlanner.Prepare(world, orders, proposal.Traces, execution);
                var record = resolver.Resolve(world, prepared);
                if (camp.Technology == Technology.Cultivation || camp.Technology == Technology.AnimalHusbandry)
                {
                    route = camp.Technology.ToString(); rule = camp.People.Progression.CompletionRule ?? "none";
                    unlockTurn = record.Turn; break;
                }
            }
            string actualHabitat = route == null ? initialHabitat : HabitatAt(world, camp.Tile);
            var unlockEcology = route == null ? null : Describe(world, camp.Tile);
            var sources = route == null ? Array.Empty<KnownMaterialSource>() : MaterialEconomy.KnownSources(world, camp, world.Days).ToArray();
            string sourceCounts = "Stone=" + sources.Count(s => s.Material == MaterialId.Stone) + ";Clay=" + sources.Count(s => s.Material == MaterialId.Clay) + ";Wood=" + sources.Count(s => s.Material == MaterialId.Wood);
            result.Add(new Boundary
            {
                Scenario = "generated-site-stationary-discovery:" + site.Label, InitialHabitat = initialHabitat, Habitat = actualHabitat,
                Route = route ?? "unresolved", Rule = rule ?? "none", SourceCounts = sourceCounts,
                Mobility = "stationary-during-discovery-control", Seed = site.Seed, CampId = camp.Id,
                StoneAvailable = sources.Where(s => s.Material == MaterialId.Stone).Sum(s => s.Available),
                ClayAvailable = sources.Where(s => s.Material == MaterialId.Clay).Sum(s => s.Available),
                WoodAvailable = sources.Where(s => s.Material == MaterialId.Wood).Sum(s => s.Available),
                River = unlockEcology?.River ?? false, Moisture = unlockEcology?.Moisture ?? 0,
                Low = unlockEcology?.Low ?? 0, Medium = unlockEcology?.Medium ?? 0, High = unlockEcology?.High ?? 0,
                Moves = camp.Moves, Population = camp.Population, Food = camp.Food,
                UnlockTurn = unlockTurn, Tile = camp.Tile, Save = route == null ? null : SaveCodec.Save(world),
                Status = route == null ? "unresolved-within-" + maxDiscoveryTurns + "-turn-cap" : "actual-" + route + "-breakthrough"
            });
        }
        return result;
    }

    private static string HabitatAt(WorldState world, int tile)
        => Describe(world, tile).Name;

    private sealed class SiteHabitat { public string Name; public bool River; public double Moisture; public int Low, Medium, High; }
    private static SiteHabitat Describe(WorldState world, int tile)
    {
        int[] ids = world.Map.Radius(tile, 1).Where(id => world.Tiles[id].TraversableLand).OrderBy(id => id).ToArray();
        var tiles = ids.Select(id => world.Tiles[id]).ToArray();
        bool river = tiles.Any(t => t.River || t.Oasis);
        double moisture = tiles.Length == 0 ? 0 : tiles.Average(t => t.Seasons.Average(s => s.Moisture));
        int medium = ids.Count(id => LazyEcology.ProjectEcology(world, id, world.Days).CurrentVegetation == VegetationTier.Medium);
        int high = ids.Count(id => LazyEcology.ProjectEcology(world, id, world.Days).CurrentVegetation == VegetationTier.High);
        int low = ids.Count(id => LazyEcology.ProjectEcology(world, id, world.Days).CurrentVegetation == VegetationTier.Low);
        var center = world.Tiles[tile];
        string name = river ? "river" : center.Environment == EnvironmentKind.Desert || moisture < .16 ? "dry-marginal"
            : medium >= 4 || high >= 4 ? "woodland-forest" : "open-fertile";
        return new SiteHabitat { Name = name, River = river, Moisture = moisture, Low = low, Medium = medium, High = high };
    }
}
