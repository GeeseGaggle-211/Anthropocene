using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    // Contract 10 movement (Docs/ElderModel9Contract9MovementDesign.md, implementation notes): regional candidate rows
    // within radius 4 and a persisted destination intent. One decision per turn; an intent is an option that steps one
    // tile per turn along the shortest known path until arrival, cancellation, infeasibility or IntentMaximumTurns.
    [Serializable]
    public sealed class ElderRegionObservation
    {
        public bool Current, Intent, Visible, PathFeasible;
        public int Distance, PathTurns;
        // Land environment (Desert, Grassland, Temperate, Tropical, Cold) as an index, -1 otherwise; static geography.
        public int EnvironmentGroup = -1;
        public bool River, Freshwater, Coast;
        public int Relief;
        // Live forecasts, visible tiles only (zero on remembered rows, which carry static geography alone).
        public double HumanFood, HerdForage, HerdWater, FarmingFood, FieldEffort;
        // Known fishing access (from the Society's own fishing-ground knowledge) and known settlements within two tiles.
        public double FishingAccess;
        public int KnownForeign, KnownKin;
    }

    public static class ElderMovement
    {
        public const int MaxRegions = 8, Radius = 4, IntentMaximumTurns = 6, RowWidth = 24;
        // Movement codes beyond contract 7's -2 native, -1 stay and 0-6 site rows.
        public const int KeepIntent = -3, RegionBase = 100;
        private static readonly EnvironmentKind[] Groups =
            { EnvironmentKind.Desert, EnvironmentKind.Grassland, EnvironmentKind.Temperate, EnvironmentKind.Tropical, EnvironmentKind.Cold };

        public static bool IsRegion(int candidate) => candidate >= RegionBase && candidate < RegionBase + MaxRegions;

        // What the Society knows of a tile: the live observation when visible, otherwise its remembered geography.
        private static bool Known(AgentObservation game, int tile, out bool visible, out bool land, out int level, out EnvironmentKind environment,
            out bool river, out Relief relief)
        {
            if (game.Tiles.TryGetValue(tile, out var t))
            {
                visible = true; land = t.Land; level = t.ElevationLevel; environment = t.Environment; river = t.River; relief = t.Relief;
                return true;
            }
            visible = false;
            if (game.RememberedTiles.TryGetValue(tile, out var r))
            {
                land = r.Land && !r.Aquatic; level = r.ElevationLevel; environment = r.Environment; river = r.River; relief = r.Relief;
                return true;
            }
            land = false; level = 10; environment = EnvironmentKind.Ocean; river = false; relief = Relief.Lowland;
            return false;
        }

        private static bool Enterable(AgentObservation game, int tile)
            => Known(game, tile, out _, out bool land, out int level, out _, out _, out _) && land && level < 10;

        // Breadth-first shortest paths over enterable known tiles within Radius of the origin (at most 61 tiles).
        internal static Dictionary<int, int> Paths(AgentObservation game, int origin, out Dictionary<int, int> parent)
        {
            var depth = new Dictionary<int, int> { [origin] = 0 };
            parent = new Dictionary<int, int>();
            var queue = new Queue<int>(); queue.Enqueue(origin);
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                foreach (int n in game.Map.Neighbors(current).OrderBy(n => n))
                {
                    if (depth.ContainsKey(n) || game.Map.Distance(origin, n) > Radius || !Enterable(game, n)) continue;
                    depth[n] = depth[current] + 1; parent[n] = current; queue.Enqueue(n);
                }
            }
            return depth;
        }

        // The first tile on the shortest known path from the origin to the target (the origin itself when they coincide).
        internal static int FirstStep(int origin, int target, Dictionary<int, int> parent)
        {
            int step = target;
            while (parent.TryGetValue(step, out int previous) && previous != origin) step = previous;
            return step;
        }

        private static bool SpacingBlocked(AgentObservation game, Camp camp, int tile)
        {
            if (tile == camp.Tile || game.MinimumCampSpacing <= 1) return false;
            if (game.Settlements.Any(s => s.Id != camp.Id && game.Map.Distance(tile, s.Tile) < game.MinimumCampSpacing)) return true;
            if (game.Rivals.Any(r => game.Map.Distance(tile, r.Tile) < game.MinimumCampSpacing)) return true;
            return game.KnownSettlements.Any(k => !k.Moved && k.Tile >= 0 && k.Camp != camp.Id &&
                game.Map.Distance(tile, k.Tile) < game.MinimumCampSpacing);
        }

        internal sealed class Regions
        {
            public ElderRegionObservation[] Rows = Array.Empty<ElderRegionObservation>();
            public int[] Tiles = Array.Empty<int>(), FirstSteps = Array.Empty<int>();
            public int IntentRow = -1;
            public int Omitted;
        }

        internal static Regions Build(WorldState world, Camp camp, AgentObservation game, PlanningIndex index, ElderInstitutionMemory memory)
        {
            var map = game.Map;
            var depth = Paths(game, camp.Tile, out var parent);
            var visibleSet = new HashSet<int>(game.Tiles.Keys);
            var kin = new HashSet<int>(game.KinRelations.Select(k => k.Player)) { camp.Player };
            var cfg = world.Config;
            double meals = Math.Max(1, camp.Population * cfg.Consumption * cfg.GenerationIntervals);
            double Quantize(double ratio) => Math.Round(Numbers.Clamp(ratio, 0, 3) * 20, MidpointRounding.AwayFromZero) / 20;
            double Access(int tile) => game.FishingGrounds.Sum(g => g.Productivity * FishingEconomy.DistanceFactor(map.Distance(tile, g.Tile)));
            IEnumerable<int> Footprint(int tile) => map.Radius(tile, cfg.GatherRadius);

            // Live farming forecast on a visible tile: the footprint's visible biomass capacity weighted by crop
            // suitability, over the camp's meals (the cultivated food a fully worked field footprint could reach).
            double Farming(int tile)
            {
                double food = 0;
                foreach (int n in Footprint(tile))
                {
                    if (!visibleSet.Contains(n) || !world.Tiles[n].TraversableLand) continue;
                    var ecology = LazyEcology.ProjectEcology(world, n, world.Days);
                    double moisture = world.Tiles[n].Seasons.Average(s => (double)s.Moisture);
                    food += Math.Max(0, ecology.PotentialFood) * EcologyMath.CropSuitability(cfg, moisture);
                }
                return Quantize(food / meals);
            }
            double Effort(int tile)
            {
                var land = Footprint(tile).Where(n => game.Tiles.TryGetValue(n, out var t) && t.Land).Select(n => game.Tiles[n]).ToArray();
                return land.Length == 0 ? 0 : land.Average(t => t.CurrentVegetation == VegetationTier.High ? 1.0 : t.CurrentVegetation == VegetationTier.Medium ? .5 : 0);
            }
            // Selection scores (cheap): visible tiles from their live observation, remembered tiles from static priors.
            double Pastoral(int tile, bool visible)
                => visible ? Footprint(tile).Where(game.Tiles.ContainsKey).Sum(n => game.Tiles[n].Land ? (double)game.Tiles[n].Food : 0)
                    : Footprint(tile).Count(n => Known(game, n, out _, out bool land, out _, out var env, out _, out _) && land &&
                        (env == EnvironmentKind.Grassland || env == EnvironmentKind.Temperate));
            double FarmingPrior(int tile, bool visible)
                => visible ? Footprint(tile).Where(n => game.Tiles.TryGetValue(n, out var t) && t.Land).Sum(n => game.Tiles[n].PotentialFood *
                        EcologyMath.CropSuitability(cfg, world.Tiles[n].Seasons.Average(s => (double)s.Moisture)))
                    : Footprint(tile).Count(n => Known(game, n, out _, out bool land, out _, out var env, out bool river, out _) && land &&
                        river && (env == EnvironmentKind.Grassland || env == EnvironmentKind.Temperate || env == EnvironmentKind.Tropical));

            var candidates = map.Radius(camp.Tile, Radius).Where(tile => tile != camp.Tile && depth.ContainsKey(tile) && !SpacingBlocked(game, camp, tile))
                .OrderBy(tile => tile).ToArray();
            var chosen = new List<int> { camp.Tile };
            int intentRow = -1;
            bool intent = memory != null && memory.HasIntent && memory.IntentTile != camp.Tile && candidates.Contains(memory.IntentTile);
            if (intent) { intentRow = chosen.Count; chosen.Add(memory.IntentTile); }
            // Best visible and best remembered tile for each economy, round robin, until the rows are full.
            var lists = new List<int[]>();
            foreach (var score in new Func<int, bool, double>[] { Pastoral, FarmingPrior, (tile, _) => Access(tile) })
                foreach (bool visible in new[] { true, false })
                    lists.Add(candidates.Where(t => visibleSet.Contains(t) == visible).OrderByDescending(t => score(t, visible)).ThenBy(t => t).ToArray());
            var cursor = new int[lists.Count];
            bool added = true;
            while (chosen.Count < MaxRegions && added)
            {
                added = false;
                for (int l = 0; l < lists.Count && chosen.Count < MaxRegions; l++)
                {
                    while (cursor[l] < lists[l].Length && chosen.Contains(lists[l][cursor[l]])) cursor[l]++;
                    if (cursor[l] >= lists[l].Length) continue;
                    chosen.Add(lists[l][cursor[l]++]); added = true;
                }
            }
            var herd = HerdConsumptionProfile.Owned(world, camp);
            var result = new Regions { IntentRow = intentRow, Tiles = chosen.ToArray(), Omitted = Math.Max(0, candidates.Length + 1 - chosen.Count) };
            result.FirstSteps = chosen.Select(tile => tile == camp.Tile ? tile : FirstStep(camp.Tile, tile, parent)).ToArray();
            result.Rows = chosen.Select((tile, row) =>
            {
                Known(game, tile, out bool visible, out _, out _, out var environment, out bool river, out var relief);
                var r = new ElderRegionObservation
                {
                    Current = tile == camp.Tile, Intent = row == intentRow, Visible = visible, Distance = map.Distance(camp.Tile, tile),
                    PathTurns = depth.TryGetValue(tile, out int d) ? d : 0, PathFeasible = depth.ContainsKey(tile),
                    EnvironmentGroup = Array.IndexOf(Groups, environment), River = river, Relief = (int)relief,
                    Freshwater = river || map.Neighbors(tile).Any(n => Known(game, n, out _, out _, out _, out var env, out _, out _) && env == EnvironmentKind.Lake),
                    Coast = map.Neighbors(tile).Any(n => Known(game, n, out _, out _, out _, out var env, out _, out _) && env == EnvironmentKind.Shallows),
                    FishingAccess = Access(tile),
                    KnownForeign = game.KnownSettlements.Count(k => !k.Moved && k.Tile >= 0 && k.Camp != camp.Id && !kin.Contains(k.Owner) && map.Distance(tile, k.Tile) <= 2),
                    KnownKin = game.Settlements.Count(s => s.Id != camp.Id && map.Distance(tile, s.Tile) <= 2) +
                        game.KnownSettlements.Count(k => !k.Moved && k.Tile >= 0 && k.Camp != camp.Id && k.Owner != camp.Player && kin.Contains(k.Owner) && map.Distance(tile, k.Tile) <= 2)
                };
                if (visible)
                {
                    var coverage = AgentObservation.LocalCoverage(world, camp, index, visibleSet, tile, camp.Population, herd);
                    r.HumanFood = coverage.HumanFood; r.HerdForage = coverage.HerdForage; r.HerdWater = coverage.HerdWater;
                    r.FarmingFood = Farming(tile); r.FieldEffort = Effort(tile);
                }
                return r;
            }).ToArray();
            return result;
        }

        // Region-row and keep-intent legality, appended to the action mask's movement candidates. A row is legal when
        // its first path step is an ordinary legal move (or it is the current tile, i.e. stay).
        internal static IEnumerable<int> Legal(AgentObservation game, Camp camp, Regions regions)
        {
            for (int row = 0; row < regions.Tiles.Length; row++)
            {
                int step = regions.FirstSteps[row];
                bool ok = regions.Tiles[row] == camp.Tile || step != camp.Tile && OrderRules.Validate(game, new Order(OrderKind.Move, camp.Id, destination: step)) == null;
                if (!ok) continue;
                yield return RegionBase + row;
                if (row == regions.IntentRow) yield return KeepIntent;
            }
        }

        // Memory bookkeeping when the decision resolves (ElderInstitutionMemorySystem.RecordDecision): a region choice
        // sets the intent, keeping it ages it, anything else (native, stay, a contract-7 site row, or a repaired request)
        // cancels it; arrival and the turn limit end it.
        internal static void Record(Camp camp, ElderInstitutionMemory memory, ElderCompiledDecision decision, double days)
        {
            var accepted = decision.Accepted ?? decision.Requested;
            int movement = accepted?.MovementCandidate ?? -1;
            if (IsRegion(movement) && accepted.IntentTile >= 0 && accepted.IntentTile != camp.Tile)
            {
                bool same = memory.HasIntent && memory.IntentTile == accepted.IntentTile;
                memory.HasIntent = true; memory.IntentTile = accepted.IntentTile;
                if (!same) { memory.IntentSetDays = days; memory.IntentTurns = 0; }
                memory.IntentTurns++;
            }
            else if (movement == KeepIntent && memory.HasIntent) memory.IntentTurns++;
            else Clear(memory);
            if (memory.HasIntent && (memory.IntentTurns > IntentMaximumTurns || memory.IntentTile == camp.Tile)) Clear(memory);
        }

        internal static void Clear(ElderInstitutionMemory memory)
        {
            memory.HasIntent = false; memory.IntentTile = -1; memory.IntentTurns = 0; memory.IntentSetDays = 0;
        }
    }
}
