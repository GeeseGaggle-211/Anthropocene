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
        private readonly Dictionary<int, Camp[]> campsByPlayer, campsByTile;
        private readonly Dictionary<int, Hunter[]> huntersByPlayer, huntersByTile, huntersByCamp;
        private readonly Dictionary<int, Herd[]> herdsByTile;
        private readonly Dictionary<int, Transfer[]> extractionByEntity;
        private readonly ConcurrentDictionary<int, EcologyView> ecology = new ConcurrentDictionary<int, EcologyView>();
        internal readonly Dictionary<int, Camp> Camps;
        public PlanningIndex(WorldState w)
        {
            world = w; Camps = w.Camps.ToDictionary(c => c.Id);
            campsByPlayer = w.Camps.GroupBy(c => c.Player).ToDictionary(g => g.Key, g => g.OrderBy(c => c.Id).ToArray());
            campsByTile = w.Camps.GroupBy(c => c.Tile).ToDictionary(g => g.Key, g => g.OrderBy(c => c.Id).ToArray());
            huntersByPlayer = w.Hunters.GroupBy(h => h.Player).ToDictionary(g => g.Key, g => g.OrderBy(h => h.Id).ToArray());
            huntersByTile = w.Hunters.GroupBy(h => h.Tile).ToDictionary(g => g.Key, g => g.OrderBy(h => h.Id).ToArray());
            huntersByCamp = w.Hunters.GroupBy(h => h.SourceCamp).ToDictionary(g => g.Key, g => g.OrderBy(h => h.Id).ToArray());
            herdsByTile = w.Herds.GroupBy(h => h.Tile).ToDictionary(g => g.Key, g => g.OrderBy(h => h.Id).ToArray());
            extractionByEntity = (w.History.LastOrDefault()?.Transfers ?? new List<Transfer>()).GroupBy(t => t.Entity).ToDictionary(g => g.Key, g => g.ToArray());
        }
        private static T[] At<T>(Dictionary<int, T[]> map, int id) => map.TryGetValue(id, out var result) ? result : Array.Empty<T>();
        internal Camp[] OwnCamps(int player) => At(campsByPlayer, player);
        internal Hunter[] OwnHunters(int player) => At(huntersByPlayer, player);
        internal Hunter[] CampHunters(int camp) => At(huntersByCamp, camp);
        internal IEnumerable<Camp> VisibleCamps(IEnumerable<int> tiles) => tiles.SelectMany(n => At(campsByTile, n)).OrderBy(c => c.Id);
        internal IEnumerable<Hunter> VisibleHunters(IEnumerable<int> tiles) => tiles.SelectMany(n => At(huntersByTile, n)).OrderBy(h => h.Id);
        internal IEnumerable<Herd> VisibleHerds(IEnumerable<int> tiles) => tiles.SelectMany(n => At(herdsByTile, n)).OrderBy(h => h.Id);
        internal IEnumerable<Transfer> Extraction(IEnumerable<int> entities) => entities.OrderBy(n => n).SelectMany(n => At(extractionByEntity, n));
        internal int Population(Camp c) => c.Population + CampHunters(c.Id).Sum(h => h.Population);
        public EcologyView Ecology(int tile) => ecology.GetOrAdd(tile, n => LazyEcology.ProjectEcology(world, n, world.Days));
        public HashSet<int> Visible(int player)
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
        public readonly bool IsVillage;
        public SettlementObservation(Camp c, PlanningIndex index) { Id = c.Id; Tile = c.Tile; Population = c.Population; Gatherers = c.GathererPopulation; LinkedPopulation = index.Population(c); IsVillage = c.IsVillage; }
    }
}
