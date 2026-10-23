using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    public enum FishingRoute { Shallows, Lake }
    public enum FishingKnowledgeSource { Visible, Snapshot, ConservativePrior }

    internal readonly struct FishingCapitalAllocation
    {
        public readonly double OpeningCapital, AverageCapital, EndingCapital;
        public readonly double RequiredMaintenanceWorkerYears, MaintenanceWorkerYears, BuildWorkerYears;
        public readonly double ManagementIntensity, OperationFraction;

        public FishingCapitalAllocation(double openingCapital, double averageCapital, double endingCapital,
            double requiredMaintenanceWorkerYears, double maintenanceWorkerYears, double buildWorkerYears,
            double managementIntensity, double operationFraction)
        {
            OpeningCapital = openingCapital; AverageCapital = averageCapital; EndingCapital = endingCapital;
            RequiredMaintenanceWorkerYears = requiredMaintenanceWorkerYears;
            MaintenanceWorkerYears = maintenanceWorkerYears; BuildWorkerYears = buildWorkerYears;
            ManagementIntensity = managementIntensity; OperationFraction = operationFraction;
        }
    }

    // One ground's knowledge-safe opening envelope and (once staffed) its
    // immutable turn work. This is preparation state, never saved or observed.
    public sealed class FishingGroundPlan
    {
        public readonly int TileId;
        public readonly FishingRoute Route;
        public readonly int Distance;
        public readonly double DistanceFactor;
        public readonly FishingKnowledgeSource KnowledgeSource;
        public readonly bool Incumbent;
        public readonly int RivalOperators;
        public readonly double OpeningAccessibleFood, OpeningBiomass, OpeningCondition, OpeningCapital;
        public readonly double ProspectiveClosingCapacity, FoodPotential;
        public readonly double[] ProductiveProfile;
        // Horizon-averaged marginal yield per nominal Fisher-year, including
        // distance and known rival pressure. Unknown priors have exactly zero.
        public readonly double YieldPerFisherYear;
        public readonly double AssignedFisherYears, HarvestWorkerYears, ToolCoveredHarvestWorkerYears, CapitalWorkerYears;
        public readonly double MaintenanceWorkerYears, RequiredMaintenanceWorkerYears, BuildWorkerYears;
        public readonly double AverageCapital, EndingCapital, ManagementIntensity, OperationFraction;
        public readonly double RequestedCatch;

        internal FishingGroundPlan(int tileId, FishingRoute route, int distance, FishingKnowledgeSource source,
            bool incumbent, int rivalOperators, double openingFood, double biomass, double condition,
            double openingCapital, double closingCapacity, double foodPotential, double[] productiveProfile,
            double yieldPerFisherYear, double assignedFisherYears = 0, double harvestWorkerYears = 0,
            double toolCoveredHarvestWorkerYears = 0, double capitalWorkerYears = 0, double maintenanceWorkerYears = 0,
            double requiredMaintenanceWorkerYears = 0, double buildWorkerYears = 0,
            double averageCapital = 0, double endingCapital = 0, double managementIntensity = 0,
            double operationFraction = 0, double requestedCatch = 0)
        {
            TileId = tileId; Route = route; Distance = distance;
            DistanceFactor = FishingEconomy.DistanceFactor(distance);
            KnowledgeSource = source; Incumbent = incumbent; RivalOperators = rivalOperators;
            OpeningAccessibleFood = openingFood; OpeningBiomass = biomass; OpeningCondition = condition;
            OpeningCapital = openingCapital; ProspectiveClosingCapacity = closingCapacity; FoodPotential = foodPotential;
            ProductiveProfile = productiveProfile == null ? new double[4] : (double[])productiveProfile.Clone();
            YieldPerFisherYear = yieldPerFisherYear;
            AssignedFisherYears = assignedFisherYears; HarvestWorkerYears = harvestWorkerYears;
            ToolCoveredHarvestWorkerYears = toolCoveredHarvestWorkerYears;
            CapitalWorkerYears = capitalWorkerYears;
            MaintenanceWorkerYears = maintenanceWorkerYears; RequiredMaintenanceWorkerYears = requiredMaintenanceWorkerYears;
            BuildWorkerYears = buildWorkerYears; AverageCapital = averageCapital; EndingCapital = endingCapital;
            ManagementIntensity = managementIntensity; OperationFraction = operationFraction; RequestedCatch = requestedCatch;
        }
    }

    // Produced once per camp/destination during Prepare (or Draft) and shared
    // with physical-demand planning. It contains no live hidden ecological data.
    public sealed class FishingAccessPlan
    {
        public readonly int CampId, CampTile, Residents;
        public readonly double StartDays, HorizonDays, HorizonYears;
        public readonly double FishingToolFisherYearsPerUnit, FishingToolHarvestMultiplier;
        public readonly FishingGroundPlan[] Candidates, Grounds;
        public readonly int[] GroundIds;

        internal FishingAccessPlan(int camp, int campTile, int residents, double startDays, double horizonDays,
            double horizonYears, double fishingToolFisherYearsPerUnit, double fishingToolHarvestMultiplier,
            FishingGroundPlan[] candidates, FishingGroundPlan[] grounds)
        {
            CampId = camp; CampTile = campTile; Residents = residents; StartDays = startDays;
            HorizonDays = horizonDays; HorizonYears = horizonYears;
            FishingToolFisherYearsPerUnit = Math.Max(1e-12, fishingToolFisherYearsPerUnit);
            FishingToolHarvestMultiplier = Math.Max(1, fishingToolHarvestMultiplier);
            Candidates = candidates ?? Array.Empty<FishingGroundPlan>();
            Grounds = grounds ?? Array.Empty<FishingGroundPlan>();
            GroundIds = Grounds.Select(g => g.TileId).OrderBy(id => id).ToArray();
        }

        // Expected current-horizon receipts for the shared survival frontier.
        // Worker-years are allocated to the highest-yield selected ground first;
        // each ground's accessible envelope caps its one-turn receipt.
        public double CatchEnvelope(int fisherCount, double fishingTools = 0)
        {
            if (fisherCount <= 0 || HorizonDays <= 0 || Grounds.Length == 0) return 0;
            double years = HorizonYears;
            if (!(years > 0)) return 0;
            FishingEconomy.AllocateHarvestWork(Grounds, fisherCount * years, Math.Max(0, fishingTools),
                FishingToolFisherYearsPerUnit, FishingToolHarvestMultiplier, out var work, out var tools);
            double total = 0;
            for (int i = 0; i < Grounds.Length; i++)
            {
                var ground = Grounds[i];
                double baseCatch = work[i] * ground.YieldPerFisherYear;
                double bonus = work[i] > 0
                    ? baseCatch * (FishingToolHarvestMultiplier - 1) * tools[i] / work[i] : 0;
                total += Math.Min(ground.FoodPotential, Math.Max(0, baseCatch + bonus));
            }
            return total;
        }

        public double MarginalCatchPerFisher(int fisherCount, double fishingTools = 0)
        {
            int n = Math.Max(0, fisherCount);
            return CatchEnvelope(n, fishingTools) - CatchEnvelope(Math.Max(0, n - 1), fishingTools);
        }
    }

    // Final, frozen result for one camp. The resolver reads this object; it must
    // not repeat the route search or inspect knowledge/live stock again.
    public sealed class FishingPlan
    {
        public readonly int CampId, CampTile, Residents, FisherCount;
        public readonly double HorizonDays, HorizonYears, FishingToolsAtOpen,
            RequestedHarvestWorkerYears, RequestedDevelopmentWorkerYears;
        public readonly bool Departing, OperatorEligible;
        public readonly FishingGroundPlan[] Candidates, Grounds;

        internal FishingPlan(int campId, int campTile, int residents, int fisherCount,
            double horizonDays, double horizonYears, double fishingToolsAtOpen,
            bool departing, bool operatorEligible, FishingGroundPlan[] candidates,
            FishingGroundPlan[] grounds, double requestedHarvestWorkerYears = 0,
            double requestedDevelopmentWorkerYears = 0)
        {
            CampId = campId; CampTile = campTile; Residents = residents; FisherCount = fisherCount;
            HorizonDays = horizonDays; HorizonYears = horizonYears; FishingToolsAtOpen = fishingToolsAtOpen;
            RequestedHarvestWorkerYears = requestedHarvestWorkerYears;
            RequestedDevelopmentWorkerYears = requestedDevelopmentWorkerYears;
            Departing = departing; OperatorEligible = operatorEligible;
            Candidates = candidates ?? Array.Empty<FishingGroundPlan>(); Grounds = grounds ?? Array.Empty<FishingGroundPlan>();
        }
    }

    // Scoped to one Prepare/Draft. It caches visibility and each of the two
    // bounded route floods by camp and accepted destination; no global map scan.
    public sealed class FishingAccessCache
    {
        private readonly WorldState world;
        private readonly Dictionary<int, HashSet<int>> visibleByPlayer = new Dictionary<int, HashSet<int>>();
        private Dictionary<int, VisionSource[]> visionSourcesByPlayer;
        private readonly Dictionary<RouteKey, Dictionary<int, int>> routes = new Dictionary<RouteKey, Dictionary<int, int>>();
        private readonly Dictionary<int, int[]> visibleCampIds = new Dictionary<int, int[]>();
        private Dictionary<int, int[]> campsByTile;
        private Dictionary<int, Camp> campsById;
        private Dictionary<int, int[]> selectedGroundCamps;

        private readonly struct VisionSource
        {
            public readonly int Tile, Radius;
            public VisionSource(int tile, int radius) { Tile = tile; Radius = radius; }
        }

        private readonly struct RouteKey : IEquatable<RouteKey>
        {
            public readonly int Camp, Tile;
            public readonly FishingRoute Route;
            public RouteKey(int camp, int tile, FishingRoute route) { Camp = camp; Tile = tile; Route = route; }
            public bool Equals(RouteKey other) => Camp == other.Camp && Tile == other.Tile && Route == other.Route;
            public override bool Equals(object obj) => obj is RouteKey other && Equals(other);
            public override int GetHashCode() => ((Camp * 397) ^ Tile) * 397 ^ (int)Route;
        }

        public FishingAccessCache(WorldState world)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
        }

        internal bool TrySite(int tile, out FisherySite site)
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

        internal HashSet<int> Visible(int player)
        {
            if (!visibleByPlayer.TryGetValue(player, out var visible))
            {
                if (visionSourcesByPlayer == null)
                {
                    var hunterPeople = new Dictionary<int, int>();
                    foreach (var hunter in world.Hunters)
                        if (hunter.Population > 0)
                            hunterPeople[hunter.SourceCamp] = (hunterPeople.TryGetValue(hunter.SourceCamp, out int prior)
                                ? prior : 0) + hunter.Population;
                    var sourceIndex = new Dictionary<int, List<VisionSource>>();
                    void Add(int owner, int tile)
                    {
                        if (!sourceIndex.TryGetValue(owner, out var list)) sourceIndex[owner] = list = new List<VisionSource>();
                        list.Add(new VisionSource(tile, world.Config.VisionRadius));
                    }
                    foreach (var camp in world.Camps)
                        if (camp.Population + (hunterPeople.TryGetValue(camp.Id, out int linked) ? linked : 0) > 0)
                            Add(camp.Player, camp.Tile);
                    foreach (var hunter in world.Hunters)
                        if (hunter.Population > 0) Add(hunter.Player, hunter.Tile);
                    visionSourcesByPlayer = sourceIndex.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
                }
                visible = new HashSet<int>();
                if (visionSourcesByPlayer.TryGetValue(player, out var visibleSources))
                    foreach (var source in visibleSources)
                        visible.UnionWith(world.Map.Radius(source.Tile, source.Radius));
                visibleByPlayer[player] = visible;
            }
            return visible;
        }

        internal ExploredTiles Remembered(Society society) => society?.Memory?.Explored;

        internal int[] VisibleCampIds(int player)
        {
            if (!visibleCampIds.TryGetValue(player, out var ids))
            {
                if (campsByTile == null)
                    campsByTile = world.Camps.Where(camp => camp != null).GroupBy(camp => camp.Tile)
                        .ToDictionary(group => group.Key, group => group.Select(camp => camp.Id).OrderBy(id => id).ToArray());
                var visible = Visible(player);
                ids = visible.OrderBy(tile => tile)
                    .Where(tile => campsByTile.ContainsKey(tile))
                    .SelectMany(tile => campsByTile[tile]).OrderBy(id => id).ToArray();
                visibleCampIds[player] = ids;
            }
            return ids;
        }

        internal Camp Camp(int id)
        {
            if (campsById == null) campsById = world.Camps.Where(camp => camp != null)
                .ToDictionary(camp => camp.Id);
            return campsById.TryGetValue(id, out var camp) ? camp : null;
        }

        internal int[] SelectedGroundCamps(int tile)
        {
            if (selectedGroundCamps == null)
            {
                var index = new Dictionary<int, List<int>>();
                foreach (var camp in world.Camps.Where(camp => camp != null).OrderBy(camp => camp.Id))
                    foreach (int ground in camp.LastSelectedFishingGrounds ?? new List<int>())
                    {
                        if (!index.TryGetValue(ground, out var camps)) index[ground] = camps = new List<int>();
                        camps.Add(camp.Id);
                    }
                selectedGroundCamps = index.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
            }
            return selectedGroundCamps.TryGetValue(tile, out var result) ? result : Array.Empty<int>();
        }

        internal Dictionary<int, int> Distances(Camp camp, int acceptedTile, FishingRoute route,
            HashSet<int> visible, ExploredTiles remembered)
        {
            var key = new RouteKey(camp.Id, acceptedTile, route);
            if (routes.TryGetValue(key, out var cached)) return cached;
            var result = new Dictionary<int, int>();
            Tile origin = acceptedTile >= 0 && acceptedTile < world.Tiles.Length ? world.Tiles[acceptedTile] : null;
            if (origin != null && origin.TraversableLand)
            {
                var queue = new Queue<int>();
                foreach (int adjacent in world.Map.Neighbors(acceptedTile).OrderBy(id => id))
                {
                    if (!Known(adjacent, visible, remembered) || !IsRouteTile(world.Tiles[adjacent], route)) continue;
                    result[adjacent] = 1; queue.Enqueue(adjacent);
                }
                while (queue.Count > 0)
                {
                    int current = queue.Dequeue(); int nextDistance = result[current] + 1;
                    if (nextDistance > FishingEconomy.GroundRange) continue;
                    foreach (int next in world.Map.Neighbors(current).OrderBy(id => id))
                    {
                        if (result.ContainsKey(next) || !Known(next, visible, remembered) || !IsRouteTile(world.Tiles[next], route)) continue;
                        result[next] = nextDistance; queue.Enqueue(next);
                    }
                }
            }
            routes.Add(key, result);
            return result;
        }

        private static bool Known(int tile, HashSet<int> visible, ExploredTiles remembered)
            => visible.Contains(tile) || remembered != null && remembered.Contains(tile);

        private static bool IsRouteTile(Tile tile, FishingRoute route)
            => tile != null && (route == FishingRoute.Shallows
                ? tile.Environment == EnvironmentKind.Shallows : tile.Environment == EnvironmentKind.Lake);

    }

    public static class FishingEconomy
    {
        public const int GroundRange = 3;
        public const double DistanceRetention = .75;
        public const double GroundSwitchMargin = .025;
        public const double ProspectiveCapacityWeight = .20;
        private const double Epsilon = 1e-12;

        public static bool FeatureEnabled(WorldState world, Camp camp)
            => world != null && camp != null && world.Config.NeolithicFishingEnabled && WatercraftEnabled(camp);

        public static bool WatercraftEnabled(Camp camp)
            => camp != null && camp.Technologies != null && camp.Technologies.Length > (int)Technology.Watercraft
                && camp.Technologies[(int)Technology.Watercraft];

        public static bool HasLegalSource(WorldState world, Camp camp, int acceptedTile = -1)
        {
            if (world == null || camp == null) return false;
            int tile = acceptedTile >= 0 ? acceptedTile : camp.Tile;
            if (world.Tiles == null || tile < 0 || tile >= world.Tiles.Length || !world.Tiles[tile].TraversableLand) return false;
            // This is a local capability query, not a full access-plan build.
            // Do not construct the Prepare cache or index global camps/sites.
            var visible = Knowledge.Visible(world, camp.Player);
            var remembered = camp.People?.Memory?.Explored;
            foreach (int adjacent in world.Map.Neighbors(tile))
                if ((visible.Contains(adjacent) || remembered != null && remembered.Contains(adjacent)) &&
                    (world.Tiles[adjacent].Environment == EnvironmentKind.Shallows || world.Tiles[adjacent].Environment == EnvironmentKind.Lake))
                    return true;
            return false;
        }

        public static bool CanFish(WorldState world, Camp camp, int acceptedTile = -1)
            => FeatureEnabled(world, camp) && HasLegalSource(world, camp, acceptedTile);

        public static double DistanceFactor(int distance)
            => distance < 1 || distance > GroundRange ? 0 : Math.Pow(DistanceRetention, distance - 1);

        // Public aliases keep fishing forecasts and filtered observations on
        // the same neutral equations CultivationEconomy uses.
        public static double Intensity(SimulationConfig config, double capital) => NeolithicCapital.Intensity(config, capital);
        public static double EffortForCapital(SimulationConfig config, double capital)
            => NeolithicCapital.EffortForCapital(config, capital,
                config.Neolithic.FishingMarginalEffortKneeQ);
        public static double EffortForCapital(WorldState world, Camp camp,
            double capital) => NeolithicCapital.EffortForCapital(world.Config,
                capital, SettlementProductionFrontier
                    .EffectiveFishingEffortKnee(world, camp));
        public static double AdditionalEffort(SimulationConfig config,
            double fromCapital, double toCapital)
            => NeolithicCapital.AdditionalEffort(config, fromCapital,
                toCapital, config.Neolithic.FishingMarginalEffortKneeQ);
        public static double AdditionalEffort(WorldState world, Camp camp,
            double fromCapital, double toCapital)
            => NeolithicCapital.AdditionalEffort(world.Config, fromCapital,
                toCapital, SettlementProductionFrontier
                    .EffectiveFishingEffortKnee(world, camp));
        public static double CapitalForEffort(SimulationConfig config, double effort)
            => NeolithicCapital.CapitalForEffort(config, effort,
                config.Neolithic.FishingMarginalEffortKneeQ);
        public static double CapitalForEffort(WorldState world, Camp camp,
            double effort) => NeolithicCapital.CapitalForEffort(world.Config,
                effort, SettlementProductionFrontier
                    .EffectiveFishingEffortKnee(world, camp));
        public static double Decay(SimulationConfig config, double capital, double operationFraction, double years) => NeolithicCapital.Decay(config, capital, operationFraction, years);
        public static double MeanDecayedCapital(SimulationConfig config, double capital, double operationFraction, double years) => NeolithicCapital.MeanDecayedCapital(config, capital, operationFraction, years);

        public static double ProjectedCapital(WorldState world, FisherySite site, double days, double operationFraction = 0)
        {
            if (world == null || site == null) return 0;
            double elapsedYears = Math.Max(0, days - site.LastUpdatedDays) / world.Config.DaysPerYear;
            return NeolithicCapital.Decay(world.Config, site.Capital, operationFraction, elapsedYears);
        }

        // Capital work is selected from knowledge-safe plans, but the resolver
        // can reapply that immutable work to the authoritative physical site.
        // Keeping the equations here makes planning and reconciliation identical.
        internal static FishingCapitalAllocation AllocateCapital(WorldState world,
            Camp camp, double openingCapital, double effectiveWork, double years)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (camp == null) throw new ArgumentNullException(nameof(camp));
            openingCapital = Math.Max(0, openingCapital);
            effectiveWork = Math.Max(0, effectiveWork);
            years = Math.Max(0, years);
            double requiredMaintenance = world.Config.Neolithic.MaintenanceWorkersPerTileE(world.Config.MaintenanceCoefficient)
                * EffortForCapital(world, camp, openingCapital) * years;
            double maintenanceWork = Math.Min(effectiveWork, requiredMaintenance);
            double operation = requiredMaintenance > Epsilon
                ? Numbers.Clamp(maintenanceWork / requiredMaintenance, 0, 1)
                : openingCapital > Epsilon && effectiveWork > Epsilon ? 1 : 0;
            double decayed = NeolithicCapital.Decay(world.Config, openingCapital, operation, years);
            double buildWork = Math.Max(0, effectiveWork - maintenanceWork);
            double addedEffort = world.Config.Neolithic.BuildWorkerYearsPerTileE > 0
                ? buildWork / world.Config.Neolithic.BuildWorkerYearsPerTileE : 0;
            double endingCapital = CapitalForEffort(world, camp,
                EffortForCapital(world, camp, decayed) + addedEffort);
            double meanCapital = NeolithicCapital.MeanDecayedCapital(world.Config, openingCapital, operation, years)
                + .5 * Math.Max(0, endingCapital - decayed);
            return new FishingCapitalAllocation(openingCapital, meanCapital, endingCapital,
                requiredMaintenance, maintenanceWork, buildWork,
                NeolithicCapital.Intensity(world.Config, meanCapital), operation);
        }

        public static FishingAccessPlan BuildAccess(WorldState world, Camp camp, int acceptedTile,
            int residents, double startDays, double horizonDays, FishingAccessCache cache = null)
        {
            if (world == null || camp == null) throw new ArgumentNullException(world == null ? nameof(world) : nameof(camp));
            cache = cache ?? new FishingAccessCache(world);
            double years = Math.Max(0, horizonDays) / world.Config.DaysPerYear;
            if (!FeatureEnabled(world, camp) || residents <= 0 || world.Tiles == null || acceptedTile < 0 || acceptedTile >= world.Tiles.Length ||
                !world.Tiles[acceptedTile].TraversableLand || horizonDays <= 0)
                return new FishingAccessPlan(camp.Id, acceptedTile, Math.Max(0, residents), startDays, Math.Max(0, horizonDays), years,
                    world.Config.Neolithic.FishingToolFisherYearsPerUnit, world.Config.Neolithic.FishingToolHarvestMultiplier,
                    Array.Empty<FishingGroundPlan>(), Array.Empty<FishingGroundPlan>());

            var visible = cache.Visible(camp.Player);
            var remembered = cache.Remembered(camp.People);
            bool HasLegalNeighbor() => world.Map.Neighbors(acceptedTile).Any(id =>
                (visible.Contains(id) || remembered != null && remembered.Contains(id)) &&
                (world.Tiles[id].Environment == EnvironmentKind.Shallows || world.Tiles[id].Environment == EnvironmentKind.Lake));
            if (!HasLegalNeighbor())
                return new FishingAccessPlan(camp.Id, acceptedTile, residents, startDays, horizonDays, years,
                    world.Config.Neolithic.FishingToolFisherYearsPerUnit, world.Config.Neolithic.FishingToolHarvestMultiplier,
                    Array.Empty<FishingGroundPlan>(), Array.Empty<FishingGroundPlan>());

            var lastSelected = camp.LastSelectedFishingGrounds == null
                ? new HashSet<int>() : new HashSet<int>(camp.LastSelectedFishingGrounds);
            var snapshots = (camp.People?.FishingGroundKnowledge ?? new List<FishingGroundSnapshot>()).Where(s => s != null)
                .GroupBy(s => s.TileId).ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.ObservedDays).First());
            var visibleRivals = new HashSet<int>(cache.VisibleCampIds(camp.Player));
            var candidates = new List<FishingGroundPlan>();
            AddRoute(FishingRoute.Shallows);
            AddRoute(FishingRoute.Lake);

            void AddRoute(FishingRoute route)
            {
                var routeTiles = cache.Distances(camp, acceptedTile, route, visible, remembered);
                foreach (var entry in routeTiles.OrderBy(p => p.Key))
                {
                    int id = entry.Key; var tile = world.Tiles[id]; bool isVisible = visible.Contains(id);
                    snapshots.TryGetValue(id, out var snapshot);
                    FishingKnowledgeSource source = isVisible ? FishingKnowledgeSource.Visible
                        : snapshot != null ? FishingKnowledgeSource.Snapshot : FishingKnowledgeSource.ConservativePrior;
                    double openingFood = 0, biomass = 0, condition = 0, openingCapital = 0, closingCapacity = 0, rate = 0;
                    bool knownSite = false;
                    var profile = new double[4];
                    if (source == FishingKnowledgeSource.Visible)
                    {
                        double queryDays = Math.Max(world.Days, tile.LastEcologyUpdateDays);
                        var opening = LazyEcology.ProjectEcology(world, id, queryDays);
                        var end = LazyEcology.ProjectEcology(world, id, Math.Max(queryDays, startDays + horizonDays));
                        openingFood = Math.Max(0, opening.Food); biomass = Math.Max(0, opening.PotentialFood);
                        condition = Numbers.Clamp(opening.AquaticCondition, 0, 1);
                        FisherySite site = null;
                        if (cache.TrySite(id, out site))
                        {
                            openingCapital = ProjectedCapital(world, site, queryDays);
                            knownSite = true;
                        }
                        double expectedOperation = knownSite && site.OperatorCampId >= 0 ? 1 : 0;
                        var aquaticOverlay = new AquaticEcologyOverlay(openingCapital,
                            openingCapital, openingCapital, expectedOperation);
                        var weights = SeasonalWeights(world, startDays, horizonDays);
                        double closingBiomass = Math.Max(0, end.PotentialFood);
                        double g0 = 0, endG0 = 0;
                        for (int season = 0; season < 4; season++)
                        {
                            var openCapacity = new EcologyCapacity(world, tile, opening, season, 0, aquaticOverlay);
                            var closeCapacity = new EcologyCapacity(world, tile, end, season, 0, aquaticOverlay);
                            double accessibleRatio = openCapacity.Biomass > Epsilon
                                ? Numbers.Clamp(openCapacity.Food / openCapacity.Biomass, 0, 1) : 0;
                            double safe = EcologyMath.SustainableHarvestRate(openCapacity.Biomass, openCapacity.BiomassRate,
                                world.Config.SustainableProductionFraction, openCapacity.ManagedFlow);
                            profile[season] = safe * accessibleRatio;
                            rate += profile[season] * weights[season];
                            g0 += openCapacity.Food * weights[season];
                            endG0 += closeCapacity.Food * weights[season];
                        }
                        // EcologyCapacity has already applied the q-squared
                        // fishery accessibility overlay exactly once.
                        openingFood = Math.Min(Math.Max(0, openingFood), Math.Max(0, g0));
                        closingCapacity = Math.Max(0, endG0);
                    }
                    else if (source == FishingKnowledgeSource.Snapshot)
                    {
                        openingFood = Math.Max(0, snapshot.AccessibleFood); biomass = Math.Max(0, snapshot.Biomass);
                        condition = Numbers.Clamp(snapshot.Condition, 0, 1);
                        openingCapital = NeolithicCapital.Decay(world.Config, Math.Max(0, snapshot.Capital), 0,
                            Math.Max(0, startDays - snapshot.ObservedDays) / world.Config.DaysPerYear);
                        if (snapshot.ProductiveProfile != null)
                            Array.Copy(snapshot.ProductiveProfile, profile, Math.Min(4, snapshot.ProductiveProfile.Length));
                        var weights = SeasonalWeights(world, startDays, horizonDays);
                        for (int season = 0; season < 4; season++) rate += Math.Max(0, profile[season]) * weights[season];
                        double q = Intensity(world.Config, openingCapital);
                        double share = AquaticEcology.HarvestableShare(tile);
                        double g0 = biomass * share;
                        closingCapacity = g0 + Math.Max(0, biomass - g0) * q * q;
                    }
                    // A remembered but unsurveyed ground deliberately uses the
                    // all-zero conservative prior: geometry only, no stock,
                    // capital, yield or claim can be inferred from hidden ecology.
                    int rivalOperators = RivalCount(world, camp, id, source, cache, snapshots, visibleRivals);
                    double rivalFactor = 1.0 / (1 + rivalOperators);
                    double foodPotential = source == FishingKnowledgeSource.ConservativePrior ? 0
                        : Math.Max(0, (1 - ProspectiveCapacityWeight) * openingFood + ProspectiveCapacityWeight * closingCapacity
                            + Math.Max(0, rate) * horizonDays) * rivalFactor;
                    double maxAnnualCatch = world.Config.FishPerPerson * world.Config.DaysPerYear / world.Config.BaseDays;
                    double yield = years > 0 ? Math.Min(maxAnnualCatch, foodPotential / years) * DistanceFactor(entry.Value) : 0;
                    candidates.Add(new FishingGroundPlan(id, route, entry.Value, source, lastSelected.Contains(id),
                        rivalOperators, openingFood, biomass, condition, openingCapital, closingCapacity, foodPotential,
                        profile, Math.Max(0, yield)));
                }
            }

            var ranked = candidates.OrderByDescending(g => g.YieldPerFisherYear).ThenBy(g => g.TileId).ToArray();
            var selectedGrounds = ranked.Take(3).ToList();
            if (selectedGrounds.Count == 3)
            {
                // Incumbency protects the selected set at its cutoff. It does
                // not add a blanket score bonus that could move a low-yield
                // incumbent ahead of substantially better grounds.
                double cutoff = selectedGrounds[2].YieldPerFisherYear;
                foreach (var incumbent in ranked.Where(g => g.Incumbent && selectedGrounds.All(s => s.TileId != g.TileId)))
                {
                    if (incumbent.YieldPerFisherYear + Epsilon < cutoff * (1 - GroundSwitchMargin)) continue;
                    int replace = selectedGrounds.FindLastIndex(g => !g.Incumbent);
                    if (replace >= 0) selectedGrounds[replace] = incumbent;
                }
            }
            var selectedIds = new HashSet<int>(selectedGrounds.Select(g => g.TileId));
            var selected = candidates.Where(g => selectedIds.Contains(g.TileId)).OrderBy(g => g.TileId).ToArray();
            return new FishingAccessPlan(camp.Id, acceptedTile, residents, startDays, horizonDays, years,
                world.Config.Neolithic.FishingToolFisherYearsPerUnit, world.Config.Neolithic.FishingToolHarvestMultiplier,
                candidates.OrderBy(g => g.TileId).ToArray(), selected);
        }

        public static FishingPlan PreparePlan(WorldState world, Camp camp, FishingAccessPlan access,
            int fisherCount, double fishingTools, double horizonDays, bool departing, int residentOverride = -1)
            => PreparePlan(world, camp, access, fisherCount, fisherCount, 0,
                fishingTools, horizonDays, departing, residentOverride, false);

        // Contract-13 actor mode keeps harvest and fishery-capital work in
        // separate fractional worker-equivalent budgets. Unused harvest work
        // never becomes development, and development work never requests catch.
        public static FishingPlan PrepareActorPlan(WorldState world, Camp camp,
            FishingAccessPlan access, int fisherCount, double harvestWorkers,
            double developmentWorkers, double fishingTools, double horizonDays,
            bool departing, int residentOverride = -1)
            => PreparePlan(world, camp, access, fisherCount, harvestWorkers,
                developmentWorkers, fishingTools, horizonDays, departing,
                residentOverride, true);

        private static FishingPlan PreparePlan(WorldState world, Camp camp,
            FishingAccessPlan access, int fisherCount, double harvestWorkers,
            double developmentWorkers, double fishingTools, double horizonDays,
            bool departing, int residentOverride, bool actorSeparated)
        {
            if (world == null || camp == null) throw new ArgumentNullException(world == null ? nameof(world) : nameof(camp));
            int tile = access?.CampTile ?? camp.Tile;
            int residents = Math.Max(0, residentOverride >= 0 ? residentOverride : access?.Residents ?? camp.Population);
            double years = Math.Max(0, horizonDays) / world.Config.DaysPerYear;
            fisherCount = Math.Min(residents, Math.Max(0, fisherCount));
            harvestWorkers = Math.Min(fisherCount, Math.Max(0, harvestWorkers));
            developmentWorkers = Math.Min(Math.Max(0,
                fisherCount - harvestWorkers), Math.Max(0, developmentWorkers));
            fishingTools = Math.Max(0, fishingTools);
            var candidates = access?.Candidates ?? Array.Empty<FishingGroundPlan>();
            var grounds = access?.Grounds ?? Array.Empty<FishingGroundPlan>();
            if (years <= 0 || fisherCount <= 0 || grounds.Length == 0)
                return new FishingPlan(camp.Id, tile, residents, fisherCount, horizonDays, years, fishingTools,
                    departing, false, candidates, Array.Empty<FishingGroundPlan>(),
                    harvestWorkers * years, developmentWorkers * years);

            double toolMultiplier = Math.Max(1, world.Config.Neolithic.FishingToolHarvestMultiplier);
            AllocateHarvestWork(grounds, harvestWorkers * years, fishingTools,
                world.Config.Neolithic.FishingToolFisherYearsPerUnit, toolMultiplier,
                out var harvestEffort, out var covered);
            // Catch allocation stops when a ground's known envelope is full.
            // Fisher staffing does not: the same assigned service-years still
            // maintain/build known grounds even when no additional catch is
            // forecast. Spread that surplus deterministically over the selected
            // known grounds; conservative priors never receive work/capital.
            var assigned = (double[])harvestEffort.Clone();
            var development = new double[grounds.Length];
            double surplus = actorSeparated
                ? developmentWorkers * years
                : Math.Max(0, fisherCount * years - harvestEffort.Sum());
            var investable = Enumerable.Range(0, grounds.Length)
                .Where(i => grounds[i].KnowledgeSource != FishingKnowledgeSource.ConservativePrior)
                .OrderByDescending(i => grounds[i].YieldPerFisherYear).ThenBy(i => grounds[i].TileId).ToArray();
            for (int position = 0; position < investable.Length && surplus > Epsilon; position++)
            {
                int i = investable[position]; int remainingGrounds = investable.Length - position;
                double share = surplus / remainingGrounds;
                assigned[i] += share;
                development[i] += share;
                surplus -= share;
            }
            var result = new List<FishingGroundPlan>(grounds.Length);
            bool operatorEligible = false;
            for (int i = 0; i < grounds.Length; i++)
            {
                var ground = grounds[i]; double fisherYears = assigned[i];
                if (ground.KnowledgeSource == FishingKnowledgeSource.ConservativePrior)
                    fisherYears = 0; // Unknown geometry never becomes a hidden-stock claim.
                double harvestYears = actorSeparated ? harvestEffort[i] : fisherYears;
                double capitalYears = actorSeparated ? development[i] : fisherYears;
                double effectiveWork = departing ? 0 : capitalYears * ground.DistanceFactor;
                double effectiveToolWork = Math.Min(harvestYears, covered[i]);
                var capital = AllocateCapital(world, camp,
                    ground.OpeningCapital, effectiveWork, years);
                double baseCatch = harvestYears * ground.YieldPerFisherYear;
                double toolBonus = harvestEffort[i] > Epsilon
                    ? baseCatch * (toolMultiplier - 1)
                        * effectiveToolWork / harvestEffort[i] : 0;
                double catchRequest = ground.KnowledgeSource == FishingKnowledgeSource.ConservativePrior ? 0
                    : Math.Min(ground.FoodPotential, Math.Max(0, baseCatch + toolBonus));
                if (!departing && effectiveWork > Epsilon && ground.KnowledgeSource != FishingKnowledgeSource.ConservativePrior) operatorEligible = true;
                result.Add(new FishingGroundPlan(ground.TileId, ground.Route, ground.Distance, ground.KnowledgeSource,
                    ground.Incumbent, ground.RivalOperators, ground.OpeningAccessibleFood, ground.OpeningBiomass,
                    ground.OpeningCondition, ground.OpeningCapital, ground.ProspectiveClosingCapacity, ground.FoodPotential,
                    ground.ProductiveProfile, ground.YieldPerFisherYear, fisherYears,
                    harvestYears, effectiveToolWork,
                    effectiveWork,
                    capital.MaintenanceWorkerYears, capital.RequiredMaintenanceWorkerYears, capital.BuildWorkerYears,
                    capital.AverageCapital, capital.EndingCapital, capital.ManagementIntensity,
                    capital.OperationFraction, catchRequest));
            }
            return new FishingPlan(camp.Id, tile, residents, fisherCount, horizonDays, years, fishingTools,
                departing, operatorEligible, candidates,
                result.OrderBy(g => g.TileId).ToArray(), harvestWorkers * years,
                developmentWorkers * years);
        }

        internal static void AllocateHarvestWork(FishingGroundPlan[] grounds, double totalWorkerYears,
            double fishingTools, double fisherYearsPerUnit, double toolMultiplier,
            out double[] work, out double[] covered)
        {
            work = new double[grounds?.Length ?? 0];
            covered = new double[grounds?.Length ?? 0];
            double remaining = Math.Max(0, totalWorkerYears);
            double remainingTools = Math.Max(0, fishingTools) * Math.Max(Epsilon, fisherYearsPerUnit);
            if (grounds == null || grounds.Length == 0 || remaining <= 0) return;
            double multiplier = Math.Max(1, toolMultiplier);
            var order = Enumerable.Range(0, grounds.Length).Where(i => grounds[i].YieldPerFisherYear > 0 && grounds[i].FoodPotential > 0)
                .OrderByDescending(i => grounds[i].YieldPerFisherYear).ThenBy(i => grounds[i].TileId).ToArray();
            foreach (int i in order)
            {
                double yield = grounds[i].YieldPerFisherYear;
                double capacity = grounds[i].FoodPotential;
                double toolWork = Math.Min(Math.Min(remaining, remainingTools), capacity / (yield * multiplier));
                double untooledWork = Math.Min(Math.Max(0, remaining - toolWork),
                    Math.Max(0, capacity - toolWork * yield * multiplier) / yield);
                work[i] = toolWork + untooledWork; covered[i] = toolWork;
                remaining -= work[i]; remainingTools -= toolWork;
                if (remaining <= Epsilon) break;
            }
        }

        private static double[] SeasonalWeights(WorldState world, double startDays, double horizonDays)
        {
            var exposure = LazyEcology.SeasonWeights(startDays, horizonDays, world.Config.BaseDays);
            double total = exposure.Sum();
            if (total <= Epsilon) return new[] { .25, .25, .25, .25 };
            for (int i = 0; i < exposure.Length; i++) exposure[i] /= total;
            return exposure;
        }

        private static int RivalCount(WorldState world, Camp camp, int tile, FishingKnowledgeSource source,
            FishingAccessCache cache, Dictionary<int, FishingGroundSnapshot> snapshots, HashSet<int> visibleRivals)
        {
            int count = 0; int operatorCamp = -1;
            if (source == FishingKnowledgeSource.Visible && cache.TrySite(tile, out var site))
                operatorCamp = site.OperatorCampId;
            else if (source == FishingKnowledgeSource.Snapshot && snapshots.TryGetValue(tile, out var snapshot))
                operatorCamp = snapshot.OperatorCampId;
            if (operatorCamp >= 0 && operatorCamp != camp.Id)
            {
                var other = cache.Camp(operatorCamp);
                if (other != null && other.SocietyId != camp.SocietyId &&
                    (source == FishingKnowledgeSource.Snapshot || visibleRivals.Contains(other.Id))) count++;
            }
            if (source == FishingKnowledgeSource.Visible)
            {
                foreach (int otherId in cache.SelectedGroundCamps(tile))
                {
                    if (otherId == camp.Id || otherId == operatorCamp || !visibleRivals.Contains(otherId)) continue;
                    var other = cache.Camp(otherId);
                    if (other != null && other.SocietyId != camp.SocietyId) count++;
                }
            }
            return count;
        }

    }

}
