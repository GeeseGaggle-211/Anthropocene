using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    // One immutable-position index per planning phase; discard at the movement barrier.
    // Contains authoritative references internally, and is never exposed to a policy.
    public sealed class PlanningIndex
    {
        private readonly WorldState world;
        internal WorldState World => world;
        private readonly Dictionary<int, Camp[]> campsByPlayer, campsByTile;
        private readonly Dictionary<int, Hunter[]> huntersByPlayer, huntersByTile, huntersByCamp;
        private readonly Dictionary<int, Herd[]> herdsByTile, herdsByCamp;
        private readonly Dictionary<int, Transfer[]> extractionByEntity;
        private readonly ConcurrentDictionary<int, EcologyView> ecology = new ConcurrentDictionary<int, EcologyView>();
        private readonly ConcurrentDictionary<int, HashSet<int>> visibility = new ConcurrentDictionary<int, HashSet<int>>();
        internal readonly Dictionary<int, Camp> Camps;
        public PlanningIndex(WorldState w)
        {
            world = w; Camps = w.Camps.ToDictionary(c => c.Id);
            project = n => LazyEcology.ProjectEcology(world, n, world.Days);
            campsByPlayer = w.Camps.GroupBy(c => c.Player).ToDictionary(g => g.Key, g => g.OrderBy(c => c.Id).ToArray());
            campsByTile = w.Camps.GroupBy(c => c.Tile).ToDictionary(g => g.Key, g => g.OrderBy(c => c.Id).ToArray());
            huntersByPlayer = w.Hunters.GroupBy(h => h.Player).ToDictionary(g => g.Key, g => g.OrderBy(h => h.Id).ToArray());
            huntersByTile = w.Hunters.GroupBy(h => h.Tile).ToDictionary(g => g.Key, g => g.OrderBy(h => h.Id).ToArray());
            huntersByCamp = w.Hunters.GroupBy(h => h.SourceCamp).ToDictionary(g => g.Key, g => g.OrderBy(h => h.Id).ToArray());
            herdsByTile = w.Herds.GroupBy(h => h.Tile).ToDictionary(g => g.Key, g => g.OrderBy(h => h.Id).ToArray());
            herdsByCamp = w.Herds.Where(h => h.DomesticCamp >= 0).GroupBy(h => h.DomesticCamp)
                .ToDictionary(g => g.Key, g => g.OrderBy(h => h.Id).ToArray());
            extractionByEntity = (w.History.LastOrDefault()?.Transfers ?? new List<Transfer>()).GroupBy(t => t.Entity).ToDictionary(g => g.Key, g => g.ToArray());
        }
        private static T[] At<T>(Dictionary<int, T[]> map, int id) => map.TryGetValue(id, out var result) ? result : Array.Empty<T>();
        internal Camp[] OwnCamps(int player) => At(campsByPlayer, player);
        internal Hunter[] OwnHunters(int player) => At(huntersByPlayer, player);
        internal Hunter[] CampHunters(int camp) => At(huntersByCamp, camp);
        internal IEnumerable<Camp> VisibleCamps(IEnumerable<int> tiles) => tiles.SelectMany(n => At(campsByTile, n)).OrderBy(c => c.Id);
        internal IEnumerable<Hunter> VisibleHunters(IEnumerable<int> tiles) => tiles.SelectMany(n => At(huntersByTile, n)).OrderBy(h => h.Id);
        internal IEnumerable<Herd> VisibleHerds(IEnumerable<int> tiles) => tiles.SelectMany(n => At(herdsByTile, n)).OrderBy(h => h.Id);
        internal Herd[] CampHerds(int camp) => At(herdsByCamp, camp);
        internal IEnumerable<Transfer> Extraction(IEnumerable<int> entities) => entities.OrderBy(n => n).SelectMany(n => At(extractionByEntity, n));
        internal int Population(Camp c) => c.Population + CampHunters(c.Id).Sum(h => h.Population);
        private readonly Func<int, EcologyView> project;
        // Projection is pure, so a racing duplicate computes the same view.
        public EcologyView Ecology(int tile)
            => ecology.TryGetValue(tile, out var view) ? view : ecology.GetOrAdd(tile, project);
        public HashSet<int> Visible(int player) => new HashSet<int>(VisibleCached(player));
        internal HashSet<int> VisibleCached(int player) => visibility.GetOrAdd(player, p => BuildVisible(p));
        private HashSet<int> BuildVisible(int player)
        {
            var tiles = new HashSet<int>();
            foreach (var c in OwnCamps(player).Where(c => Population(c) > 0)) tiles.UnionWith(world.Map.Radius(c.Tile, world.Config.VisionRadius));
            foreach (var h in OwnHunters(player).Where(h => h.Population > 0)) tiles.UnionWith(world.Map.Radius(h.Tile, world.Config.VisionRadius));
            return tiles;
        }
    }
    [Serializable]
    public sealed class PolicyState
    {
        public double HuntingInclination = 1, GatheringInclination = 1.15, FishingInclination = 1;
        public static PolicyState Seeded(int seed) => new PolicyState
        {
            HuntingInclination = .4 + Numbers.Random(seed, 191, 0), GatheringInclination = .85 + .6 * Numbers.Random(seed, 192, 0), FishingInclination = .4 + Numbers.Random(seed, 193, 0)
        };
    }
    public sealed class SettlementObservation
    {
        public readonly int Id, Tile, Population, Gatherers, LinkedPopulation;
        public readonly double AnimalFabric, LightHousing, HerdingTools, FixedHousing;
        public readonly bool RecentlyMoved;
        public readonly bool IsVillage;
        public readonly bool Agriculture, Husbandry;
        public readonly string[] Technologies;
        public readonly EconomicIntent Economy;
        public readonly EconomicIntent RecommendedEconomy;
        public readonly MaterialSourceObservation[] MaterialSources;
        public SettlementObservation(Camp c, PlanningIndex index, bool recommendEconomy = false) { Id = c.Id; Tile = c.Tile; Population = c.Population; Gatherers = c.GathererPopulation; LinkedPopulation = index.Population(c); AnimalFabric = c.Inventory.AnimalFabric; LightHousing = c.Inventory.LightHousingUnits; HerdingTools = c.Inventory.HerdingTools; FixedHousing = NeolithicProduction.FixedHousingAt(index.World, c.Id, c.Tile, index.World.Days); RecentlyMoved = c.Moves > 0 && c.StationaryTurns == 0; IsVillage = c.IsVillage; Agriculture = CultivationEconomy.Enabled(c); Husbandry = CultivationEconomy.HusbandryEnabled(c); Technologies = c.People.NeolithicTechnologies.ToArray(); Economy = EconomicIntent.From(c); RecommendedEconomy = recommendEconomy && (Agriculture || Husbandry) ? EconomicIntents.Automatic(index.World, c, true) : Economy.Copy(); MaterialSources = Agriculture || Husbandry ? MaterialEconomy.Observe(index.World, c, index.World.Days) : Array.Empty<MaterialSourceObservation>(); }
    }
}
