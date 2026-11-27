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
        private readonly Dictionary<int, Society> societiesById;
        private readonly Dictionary<int, Society[]> daughtersByParent;
        public PlanningIndex(WorldState w)
        {
            world = w; Camps = w.Camps.ToDictionary(c => c.Id);
            societiesById = w.Societies.ToDictionary(s => s.Id);
            daughtersByParent = w.Societies.Where(s => s.ParentSociety > 0).GroupBy(s => s.ParentSociety).ToDictionary(g => g.Key, g => g.OrderBy(s => s.Id).ToArray());
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
        internal Society SocietyById(int id) => societiesById.TryGetValue(id, out var s) ? s : null;
        internal Society[] Daughters(int parentSociety) => At(daughtersByParent, parentSociety);
        internal Hunter[] OwnHunters(int player) => At(huntersByPlayer, player);
        internal Hunter[] CampHunters(int camp) => At(huntersByCamp, camp);
        internal IEnumerable<Camp> VisibleCamps(IEnumerable<int> tiles) => tiles.SelectMany(n => At(campsByTile, n)).OrderBy(c => c.Id);
        internal IEnumerable<Hunter> VisibleHunters(IEnumerable<int> tiles) => tiles.SelectMany(n => At(huntersByTile, n)).OrderBy(h => h.Id);
        internal IEnumerable<Herd> VisibleHerds(IEnumerable<int> tiles) => tiles.SelectMany(n => At(herdsByTile, n)).OrderBy(h => h.Id);
        internal Herd[] CampHerds(int camp) => At(herdsByCamp, camp);
        internal bool TryFisherySite(int tile, out FisherySite site)
        {
            var sites = world.FisherySites;
            int low = 0, high = (sites?.Count ?? 0) - 1;
            while (low <= high)
            {
                int middle = low + ((high - low) >> 1);
                int comparison = sites[middle].TileId.CompareTo(tile);
                if (comparison == 0) { site = sites[middle]; return true; }
                if (comparison < 0) low = middle + 1;
                else high = middle - 1;
            }
            site = null;
            return false;
        }
        internal IEnumerable<Transfer> Extraction(IEnumerable<int> entities) => entities.OrderBy(n => n).SelectMany(n => At(extractionByEntity, n));
        internal int Population(Camp c) => c.Population + CampHunters(c.Id).Sum(h => h.Population);
        private readonly Func<int, EcologyView> project;
        // Projection is pure, so a racing duplicate computes the same view.
        public EcologyView Ecology(int tile)
            => ecology.TryGetValue(tile, out var view) ? view : ecology.GetOrAdd(tile, project);
        private readonly ConcurrentDictionary<int, RememberedTileObservation[]> remembered = new ConcurrentDictionary<int, RememberedTileObservation[]>();
        // Shared by every camp of the Society in this planning phase. Pure: static geography only.
        internal RememberedTileObservation[] Remembered(Society society, int player)
            => society.Memory == null ? Array.Empty<RememberedTileObservation>() : remembered.GetOrAdd(society.Id, _ => ContactKnowledge.Remembered(world, society, VisibleCached(player)));
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
        public readonly int Id, Tile, Population, Gatherers, LinkedPopulation, FisherPopulation, FishingVillageMaturity, FishingVillageMealSupport, StewardPopulation;
        public readonly double FishingTools;
        public readonly double AnimalFabric, LightHousing, HerdingTools, FixedHousing, HousingMaterial;
        // Owner-only physical demography and welfare diagnostics from the last committed turn.
        public readonly double QoL, Nutrition, ClothingCoverage, HousingCoverage, FoodDeficitYears;
        public readonly double EffectiveFertilityRate, EffectiveMortalityRate, ExposureMortalityRate, CrowdingMortalityRate, DiseaseMortalityRate, StarvationMortalityRate;
        public readonly bool RecentlyMoved;
        public readonly bool IsVillage;
        public readonly bool Agriculture, Husbandry, FishingEnabled, StewardEnabled;
        public readonly string[] Technologies;
        public readonly EconomicIntent Economy;
        public readonly EconomicIntent RecommendedEconomy;
        // Owner-only fission state (WP10a): the camp's policy and its cached destination tile (-1 when none).
        public readonly FissionPolicy FissionPolicy;
        public readonly int FissionDestination;
        // Owner-only civic governance state (Docs/CivicGovernance.md §5 Choice #9.3).
        public readonly double CivicComplexity, CivicStratification, LastEnvironmentalComplexity, LastEnvironmentalStratification;
        public readonly double CivicCapital, CivicCoverage, SanitationCapital, SanitationCoverage, SanitationWaterRatio;
        public readonly ImmigrationPolicy ImmigrationPolicy;
        public readonly CivicFocus CivicFocus;
        public readonly string[] CivicPrecedents;
        public readonly MaterialSourceObservation[] MaterialSources;
        // forValidation (order validation only): MaterialSources is empty and FixedHousing 0; OrderRules.Validate reads neither.
        public SettlementObservation(Camp c, PlanningIndex index, bool recommendEconomy = false, bool forValidation = false)
        {
            Id = c.Id; Tile = c.Tile; Population = c.Population; Gatherers = c.GathererPopulation; LinkedPopulation = index.Population(c);
            FisherPopulation = c.Workforce.Assignments.Where(a => a.Role == WorkforceRoles.Fisher).Sum(a => a.Population);
            StewardPopulation = c.Workforce.Assignments.Where(a => a.Role == WorkforceRoles.Steward).Sum(a => a.Population);
            FishingTools = c.Inventory.FishingTools;
            AnimalFabric = c.Inventory.AnimalFabric; LightHousing = c.Inventory.LightHousingUnits; HerdingTools = c.Inventory.HerdingTools; HousingMaterial = c.Inventory.HousingMaterial;
            FixedHousing = forValidation ? 0 : NeolithicProduction.FixedHousingAt(index.World, c, index.World.Days);
            QoL = c.QoL; Nutrition = c.FoodNeedsMetRatio; ClothingCoverage = c.ClothPerCapita; HousingCoverage = c.HousingPerCapita; FoodDeficitYears = c.FoodDeficitYears;
            EffectiveFertilityRate = c.EffectiveFertilityRate; EffectiveMortalityRate = c.EffectiveMortalityRate; ExposureMortalityRate = c.ExposureMortalityRate;
            CrowdingMortalityRate = c.CrowdingMortalityRate; DiseaseMortalityRate = c.DiseaseMortalityRate; StarvationMortalityRate = c.StarvationMortalityRate;
            RecentlyMoved = c.Moves > 0 && c.StationaryTurns == 0; IsVillage = c.IsVillage; FissionPolicy = c.FissionPolicy; FissionDestination = c.FissionDestination == null ? -1 : c.FissionDestination.Tile;
            Agriculture = CultivationEconomy.Enabled(c); Husbandry = CultivationEconomy.HusbandryEnabled(c);
            // Economy capability is a society flag, not a prediction that a
            // currently eligible ground will produce. Ground access belongs
            // to the prepared Fishing plan, so action masks cannot reveal
            // hidden water quality or route/source availability.
            FishingEnabled = index.World.Config.NeolithicFishingEnabled && c.Technologies[(int)Technology.Watercraft];
            StewardEnabled = CivicGovernance.StewardAvailable(index.World, c);
            CivicComplexity = c.CivicComplexity;
            CivicStratification = c.CivicStratification;
            LastEnvironmentalComplexity = c.LastEnvironmentalComplexity;
            LastEnvironmentalStratification = c.LastEnvironmentalStratification;
            CivicCapital = c.CivicCapital;
            CivicCoverage = CivicGovernance.Enabled(index.World.Config) ? CivicGovernance.CivicCoverage(index.World, c) : 0;
            SanitationCapital = c.SanitationCapital;
            SanitationCoverage = CivicGovernance.SanitationEnabled(index.World.Config) ? CivicGovernance.SanitationCoverage(index.World, c) : 0;
            SanitationWaterRatio = c.SanitationWaterRatio;
            ImmigrationPolicy = c.ImmigrationPolicy;
            CivicFocus = c.CivicFocus;
            CivicPrecedents = (c.People.CivicPrecedents ?? new List<string>()).ToArray();
            var lastReport = forValidation ? null : index.World.History.LastOrDefault()?.Demographics?.FirstOrDefault(d => d.Camp == c.Id);
            FishingVillageMaturity = Knowledge.Band(lastReport?.FishingVillageMaturity ?? 0, 1, index.World.Config);
            FishingVillageMealSupport = Knowledge.Band(lastReport?.FishingVillageMealSupport ?? 0, 1, index.World.Config);
            Technologies = c.People.NeolithicTechnologies.ToArray(); Economy = EconomicIntent.From(c);
            RecommendedEconomy = recommendEconomy && (Agriculture || Husbandry || FishingEnabled) ? EconomicIntents.Automatic(index.World, c, true) : Economy.Copy();
            MaterialSources = !forValidation && (Agriculture || Husbandry || FishingEnabled) ? MaterialEconomy.Observe(index.World, c, index.World.Days) : Array.Empty<MaterialSourceObservation>();
        }
    }
}
