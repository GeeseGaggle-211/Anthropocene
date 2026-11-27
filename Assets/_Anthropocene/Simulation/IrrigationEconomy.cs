using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    [Serializable]
    public sealed class CanalSegment
    {
        public int Id, Operator, Source, From, Tile, Parent = -1;
        public double Construction, ActiveDays, LastUpdatedDays, Throughput, Condition = 1;
        // Paid service reserved at the previous Close. No newly produced service
        // can retroactively irrigate the production interval that paid for it.
        public double FundedYears, CommissioningYears;
    }
    [Serializable]
    public sealed class IrrigatedField
    {
        public int Tile, Operator, Endpoint;
        public double Area = 1, Soil, LastUpdatedDays, WaterService;
    }
    public sealed class IrrigationProject
    {
        public int Source, Target;
        public int[] Route = Array.Empty<int>();
        public double Remaining, AnnualFoodGain;
    }
    public sealed class IrrigationOrder
    {
        public IrrigationProject Project;
        public double Construction, Upkeep;
    }
    public readonly struct IrrigationOverlay
    {
        public readonly double Water, Soil, Area;
        public IrrigationOverlay(double water, double soil, double area)
        { Water = Numbers.Clamp(water, 0, 1); Soil = Numbers.Clamp(soil, 0, 1); Area = Numbers.Clamp(area, 0, 1); }
        public void Apply(ref EcologyView view)
        { view.IrrigationWater = Water; view.IrrigationSoil = Soil; view.IrrigationArea = Area; }
    }
    [Serializable]
    public sealed class IrrigationWaterReceipt
    {
        public int Source, Field, Operator;
        public double Withdrawal, Delivery, Loss, SupplyFraction, SoilBefore, SoilAfter;
    }
    public sealed class IrrigationAllocation
    {
        public readonly Dictionary<int, IrrigationOverlay> Fields = new Dictionary<int, IrrigationOverlay>();
        public readonly Dictionary<int, double> EndingSoil = new Dictionary<int, double>();
        public readonly List<Demand> Withdrawals = new List<Demand>();
        public double Withdrawal, Delivery, Loss;
        public readonly List<IrrigationWaterReceipt> Receipts = new List<IrrigationWaterReceipt>();
    }

    public static class IrrigationEconomy
    {
        public static bool Enabled(WorldState w) => w.Config.Agriculture?.IrrigationEnabled == true;
        public static bool InfrastructureGood(CraftGood good) => good == CraftGood.CanalConstruction || good == CraftGood.CanalMaintenance;
        internal static IEnumerable<CanalSegment> Owned(WorldState w, Camp c)
            => (w.Canals ?? new List<CanalSegment>()).Where(s => s.Operator == c.Id).OrderBy(s => s.Id);
        public static bool FertileGate(WorldState w, Camp c)
        {
            if (!Enabled(w)) return false;
            foreach (int id in CultivationEconomy.Footprint(w, c))
            {
                var t = w.Tiles[id];
                if (t.CultivationOperator >= 0 && t.CultivationOperator != c.Id) continue;
                if (t.Seasons.Average(s => s.Moisture) < w.Config.Agriculture.FertileMoisture) continue;
                if (CultivationEconomy.Intensity(w.Config, LazyEcology.ProjectEcology(w, id, w.Days).CultivationCapital)
                    + 1e-9 < w.Config.Agriculture.DevelopmentQ) return false;
            }
            return true;
        }
        private static bool FreshSource(Tile t) => t.WaterBody != WaterKind.Saltwater &&
            (t.FreshwaterSource == FreshwaterSource.River || t.FreshwaterSource == FreshwaterSource.Lake);
        private static bool Blocked(WorldState w, int from, int to)
        {
            for (int direction = 0; direction < 6; direction++)
                if (w.Map.Neighbor(from, direction) == to)
                    return (w.Tiles[from].RidgeMask & (1 << direction)) != 0 ||
                        (w.Tiles[to].RidgeMask & (1 << ((direction + 3) % 6))) != 0;
            return true;
        }
        internal static double ValleyRiseTolerance(WorldState w)
        {
            var cfg = w.Config.Agriculture;
            if (cfg == null) return 0;
            double tol = cfg.HydraulicTolerance;
            if (cfg.ValleyMaxSlopeDegrees > 0 && cfg.ValleyBankWidthMeters > 0)
            {
                double metersPerUnit = w.Config.ElevationMetersPerUnit > 0 ? w.Config.ElevationMetersPerUnit : 12000.0;
                double valleyRise = cfg.ValleyBankWidthMeters * Math.Tan(cfg.ValleyMaxSlopeDegrees * Math.PI / 180.0) / metersPerUnit;
                if (valleyRise > tol) tol = valleyRise;
            }
            return tol;
        }
        public static bool RouteValid(WorldState w, int source, IReadOnlyList<int> route)
        {
            if (!Enabled(w) || source < 0 || source >= w.Tiles.Length || !FreshSource(w.Tiles[source]) ||
                route == null || route.Count == 0 || route.Count > 7) return false;
            double strictTol = w.Config.Agriculture.HydraulicTolerance;
            double valleyTol = ValleyRiseTolerance(w);
            bool sourceMountain = w.Tiles[source].Relief == Relief.Mountain;
            float sourceElevation = w.Tiles[source].Elevation;
            int prior = source; var seen = new HashSet<int> { source };
            foreach (int id in route)
            {
                if (id < 0 || id >= w.Tiles.Length || !seen.Add(id) || !w.Tiles[id].TraversableLand ||
                    w.Map.Distance(prior, id) != 1 || Blocked(w, prior, id)) return false;
                double stepLimit = (sourceMountain || w.Tiles[id].Relief == Relief.Mountain) ? strictTol : valleyTol;
                if (w.Tiles[id].Elevation > w.Tiles[prior].Elevation + stepLimit ||
                    w.Tiles[id].Elevation > sourceElevation + stepLimit) return false;
                prior = id;
            }
            return true;
        }
        private static bool CompatibleRoute(IReadOnlyDictionary<(int Source, int Tile), CanalSegment> ownedBySourceTile, int source, int[] route)
        {
            int from = source, parent = -1;
            foreach (int tile in route)
            {
                ownedBySourceTile.TryGetValue((source, tile), out var existing);
                if (existing != null && (existing.From != from || existing.Parent != parent)) return false;
                parent = existing?.Id ?? -2; from = tile;
            }
            return true;
        }
        private static bool CompatibleRoute(WorldState w, Camp c, int source, int[] route)
        {
            var owned = new Dictionary<(int Source, int Tile), CanalSegment>();
            foreach (var s in Owned(w, c))
                if (!owned.ContainsKey((s.Source, s.Tile))) owned[(s.Source, s.Tile)] = s;
            return CompatibleRoute(owned, source, route);
        }
        // Conservative local routing: visible continuous terrain only. The
        // farming footprint bounds targets and paths; intakes may border it.
        public static IrrigationProject Opportunity(WorldState w, Camp c, bool discoveryOnly = false)
        {
            if (!Enabled(w) || !CultivationEconomy.Enabled(c) || c.Population <= 0) return null;
            bool localAllVisible = w.Config.VisionRadius >= 2;
            HashSet<int> visible = localAllVisible ? null : Knowledge.Visible(w, c.Player);
            bool IsVisible(int id) => localAllVisible ? w.Map.Distance(c.Tile, id) <= w.Config.VisionRadius : visible.Contains(id);
            var footprint = CultivationEconomy.Footprint(w, c).Where(id => IsVisible(id) &&
                (w.Tiles[id].CultivationOperator < 0 || w.Tiles[id].CultivationOperator == c.Id)).ToArray();
            if (footprint.Length == 0) return null;
            var allowed = new HashSet<int>(footprint);
            var sources = footprint.SelectMany(id => w.Map.Radius(id, 1)).Distinct()
                .Where(id => IsVisible(id) && FreshSource(w.Tiles[id]) && w.Tiles[id].Seasons.Any(s => s.MaxFreshwater > 0 && s.WaterRecoveryPerDay > 0))
                .OrderBy(id => id).ToArray();
            if (sources.Length == 0) return null;
            var ownedBySourceTile = new Dictionary<(int Source, int Tile), CanalSegment>();
            Dictionary<int, List<CanalSegment>> canalsBySource = null;
            if (w.Canals != null)
            {
                foreach (var s in w.Canals.OrderBy(x => x.Id))
                {
                    if (s.Operator == c.Id && !ownedBySourceTile.ContainsKey((s.Source, s.Tile)))
                        ownedBySourceTile[(s.Source, s.Tile)] = s;
                    if (!discoveryOnly)
                    {
                        canalsBySource ??= new Dictionary<int, List<CanalSegment>>();
                        if (!canalsBySource.TryGetValue(s.Source, out var list)) canalsBySource[s.Source] = list = new List<CanalSegment>();
                        list.Add(s);
                    }
                }
            }
            double years = w.Config.TurnDays / w.Config.DaysPerYear;
            Dictionary<int, List<Demand>> essentialBySource = null;
            IrrigationProject best = null;
            foreach (int source in sources)
            {
                // Minimum-remaining-cost search over the <=7 local footprint tiles
                // so branching off an existing canal segment is preferred over a
                // redundant unbuilt parallel segment.
                var bestCost = new Dictionary<int, (double Remaining, int Hops)>();
                var frontier = new List<(double Remaining, int Hops, int[] Route)> { (0, 0, Array.Empty<int>()) };
                var bestRoutes = new Dictionary<int, (double Remaining, int[] Route)>();
                while (frontier.Count > 0)
                {
                    int pick = 0;
                    for (int i = 1; i < frontier.Count; i++)
                    {
                        int cmp = frontier[i].Remaining.CompareTo(frontier[pick].Remaining);
                        if (cmp == 0) cmp = frontier[i].Hops.CompareTo(frontier[pick].Hops);
                        if (cmp == 0 && frontier[i].Route.Length > 0 && frontier[pick].Route.Length > 0)
                            cmp = frontier[i].Route[frontier[i].Route.Length - 1].CompareTo(frontier[pick].Route[frontier[pick].Route.Length - 1]);
                        if (cmp < 0) pick = i;
                    }
                    var current = frontier[pick];
                    frontier.RemoveAt(pick);
                    int last = current.Route.Length == 0 ? source : current.Route[current.Route.Length - 1];
                    if (current.Route.Length >= 7) continue;
                    foreach (int id in w.Map.Radius(last, 1).Where(id => allowed.Contains(id)).OrderBy(id => id))
                    {
                        if (id == source || Array.IndexOf(current.Route, id) >= 0) continue;
                        var route = new int[current.Route.Length + 1];
                        Array.Copy(current.Route, route, current.Route.Length);
                        route[current.Route.Length] = id;
                        if (!RouteValid(w, source, route) || !CompatibleRoute(ownedBySourceTile, source, route)) continue;
                        double stepRem = 1 - (ownedBySourceTile.TryGetValue((source, id), out var existing) ? existing.Construction : 0);
                        double rem = current.Remaining + Math.Max(0, stepRem);
                        int hops = route.Length;
                        if (bestCost.TryGetValue(id, out var prior) &&
                            (rem > prior.Remaining + 1e-9 || (Math.Abs(rem - prior.Remaining) <= 1e-9 && hops >= prior.Hops)))
                            continue;
                        bestCost[id] = (rem, hops);
                        bestRoutes[id] = (rem, route);
                        frontier.Add((rem, hops, route));
                    }
                }
                List<Demand> essential = null;
                List<CanalSegment> sourceCanals = null;
                List<IrrigatedField> sourceFields = null;
                IrrigationAllocation beforeWater = null;
                Dictionary<int, Camp> campLookup = null;
                foreach (var entry in bestRoutes.OrderBy(p => p.Key))
                {
                    int id = entry.Key; double remaining = entry.Value.Remaining; int[] route = entry.Value.Route;
                    if (remaining <= 1e-9) continue;
                    if (!w.Tiles[id].Seasons.Any(s => s.Moisture < w.Config.Agriculture.TargetMoisture - 1e-9)) continue;
                    if (Field(w, id) != null) continue;
                    if (discoveryOnly)
                    {
                        if (best == null || 1 / remaining > best.AnnualFoodGain / best.Remaining + 1e-9)
                            best = new IrrigationProject { Source = source, Target = id, Route = route, Remaining = remaining, AnnualFoodGain = 1 };
                        continue;
                    }
                    if (LazyEcology.ProjectEcology(w, id, w.Days).CultivationCapital <= 0) continue;
                    if (essential == null)
                    {
                        if (essentialBySource == null)
                        {
                            // Collect fog-safe local claims once for all candidate sources of this camp.
                            var allLocal = EssentialForecastClaimsForSources(w, years, sources, IsVisible);
                            essentialBySource = new Dictionary<int, List<Demand>>();
                            foreach (var d in allLocal)
                            {
                                if (!essentialBySource.TryGetValue(d.Tile, out var bucket)) essentialBySource[d.Tile] = bucket = new List<Demand>();
                                bucket.Add(d);
                            }
                        }
                        essential = essentialBySource.TryGetValue(source, out var srcClaims) ? srcClaims : new List<Demand>();
                        sourceCanals = canalsBySource != null && canalsBySource.TryGetValue(source, out var list) ? list : new List<CanalSegment>();
                        var segmentIds = new HashSet<int>(sourceCanals.Select(s => s.Id));
                        sourceFields = segmentIds.Count == 0 ? new List<IrrigatedField>()
                            : (w.IrrigatedFields ?? new List<IrrigatedField>()).Where(f => segmentIds.Contains(f.Endpoint)).ToList();
                        campLookup = BuildCampLookup(w, c, sourceFields);
                        var beforeWorld = w.WithIrrigationNetwork(sourceCanals.ToList(), sourceFields.ToList());
                        beforeWater = Allocate(beforeWorld, essential, w.Days, w.Config.TurnDays, campLookup, operatingFraction: 1);
                    }
                    double gain = CapacityGain(w, c, source, route, id, years, sourceCanals, sourceFields, essential, beforeWater, campLookup);
                    if (gain <= 0) continue;
                    if (best == null || gain / remaining > best.AnnualFoodGain / best.Remaining + 1e-9)
                        best = new IrrigationProject { Source = source, Target = id, Route = route, Remaining = remaining, AnnualFoodGain = gain };
                }
            }
            return best;
        }
        private static Dictionary<int, Camp> BuildCampLookup(WorldState w, Camp primary, List<IrrigatedField> fields)
        {
            var lookup = new Dictionary<int, Camp>();
            if (primary != null) lookup[primary.Id] = primary;
            if (fields.Count == 0) return lookup;
            var needed = new HashSet<int>();
            foreach (var f in fields) if (!lookup.ContainsKey(f.Operator)) needed.Add(f.Operator);
            if (needed.Count == 0) return lookup;
            foreach (var camp in w.Camps)
                if (needed.Contains(camp.Id)) lookup[camp.Id] = camp;
            return lookup;
        }
        private static double CapacityGain(WorldState w, Camp camp, int source, int[] route, int id, double years,
            List<CanalSegment> sourceCanals, List<IrrigatedField> sourceFields, List<Demand> essential,
            IrrigationAllocation beforeWater, Dictionary<int, Camp> campLookup)
        {
            var canals = new List<CanalSegment>(sourceCanals);
            var fields = new List<IrrigatedField>(sourceFields);
            int parent = -1, from = source, next = w.NextId;
            foreach (int tile in route)
            {
                var prior = canals.FirstOrDefault(s => s.Operator == camp.Id && s.Tile == tile);
                if (prior != null) canals.Remove(prior);
                // Completion includes its paid first service period. Existing
                // branch users compete in this very same finite-water allocator.
                var segment = new CanalSegment { Id = prior?.Id ?? next++, Operator = camp.Id,
                    Source = source, From = from, Tile = tile, Parent = parent, Construction = 1,
                    ActiveDays = w.Days, FundedYears = Math.Max(years, prior?.FundedYears ?? 0),
                    Throughput = prior?.Throughput ?? w.Config.Agriculture.AnnualThroughput };
                canals.Add(segment); parent = segment.Id; from = tile;
            }
            fields.Add(new IrrigatedField { Tile = id, Operator = camp.Id, Endpoint = parent });
            fields.Sort((a, b) => a.Tile.CompareTo(b.Tile));
            var prospective = w.WithIrrigationNetwork(canals, fields);
            var afterWater = Allocate(prospective, essential, w.Days, w.Config.TurnDays, campLookup, operatingFraction: 1);
            double gain = 0;
            foreach (var field in fields.Where(f => f.Operator == camp.Id))
            {
                var old = beforeWater.Fields.TryGetValue(field.Tile, out var opening) ? opening : new IrrigationOverlay(0, field.Soil, field.Area);
                var supplied = afterWater.Fields[field.Tile];
                var before = FoodHorizonForecast.Tile(w, field.Tile, w.Days, w.Config.TurnDays, operatingFraction: 1, irrigationOverlay: old);
                var after = FoodHorizonForecast.Tile(w, field.Tile, w.Days, w.Config.TurnDays, operatingFraction: 1, irrigationOverlay: supplied);
                gain += after.ClosingFood - before.ClosingFood;
            }
            return Math.Max(0, gain) / years;
        }
        internal static HashSet<int> LocalSources(WorldState w, HashSet<int> localTiles)
        {
            var result = new HashSet<int>();
            if (!Enabled(w) || w.IrrigatedFields == null || w.Canals == null || w.IrrigatedFields.Count == 0) return result;
            Dictionary<int, CanalSegment> byId = null;
            foreach (int tile in localTiles)
            {
                var field = Field(w, tile);
                if (field == null) continue;
                byId ??= w.Canals.ToDictionary(s => s.Id);
                if (byId.TryGetValue(field.Endpoint, out var seg)) result.Add(seg.Source);
            }
            return result;
        }
        internal static IrrigationAllocation AllocateLocal(WorldState w, IEnumerable<Demand> essentialClaims,
            double start, double days, HashSet<int> localSources, Camp primary = null)
        {
            if (!Enabled(w) || localSources == null || localSources.Count == 0 || w.IrrigatedFields == null || w.IrrigatedFields.Count == 0 || days <= 0)
                return new IrrigationAllocation();
            var localCanals = (w.Canals ?? new List<CanalSegment>()).Where(s => localSources.Contains(s.Source)).ToList();
            if (localCanals.Count == 0) return new IrrigationAllocation();
            var segmentIds = new HashSet<int>(localCanals.Select(s => s.Id));
            var localFields = w.IrrigatedFields.Where(f => segmentIds.Contains(f.Endpoint)).ToList();
            if (localFields.Count == 0) return new IrrigationAllocation();
            var lookup = BuildCampLookup(w, primary, localFields);
            return Allocate(w.WithIrrigationNetwork(localCanals, localFields), essentialClaims, start, days, lookup);
        }
        internal static List<Demand> EssentialForecastClaimsForSources(WorldState w, double years,
            IReadOnlyCollection<int> sources, Func<int, bool> tileKnown = null)
        {
            var claims = new List<Demand>();
            if (sources == null || sources.Count == 0 || years <= 0) return claims;
            var sourceSet = sources as HashSet<int> ?? new HashSet<int>(sources);
            var neighborTiles = new HashSet<int>();
            foreach (int s in sourceSet) foreach (int n in w.Map.Radius(s, 1)) neighborTiles.Add(n);
            HashSet<int> visibleNeighbors = null;
            if (tileKnown != null)
            {
                visibleNeighbors = new HashSet<int>();
                foreach (int n in neighborTiles) if (tileKnown(n)) visibleNeighbors.Add(n);
            }
            double intervals = years * w.Config.DaysPerYear / w.Config.BaseDays;
            // Include dispatched-empty (Population==0) camps for domestic-herd lookup;
            // filter Population > 0 only when emitting Drinking claims.
            Dictionary<int, Camp> localCamps = null;
            foreach (var c in w.Camps)
            {
                if (!neighborTiles.Contains(c.Tile)) continue;
                if (tileKnown != null && !tileKnown(c.Tile)) continue;
                localCamps ??= new Dictionary<int, Camp>();
                localCamps[c.Id] = c;
            }
            // Wild herds first in canonical ID order, then managed herds in canonical ID order,
            // passing accumulated claims as `existing` so managed-herd residual routing matches resolution.
            var wildLocal = new List<Herd>();
            var domesticLocal = new List<(Herd Herd, int Tile)>();
            foreach (var herd in w.Herds)
            {
                if (herd.Animals <= 0) continue;
                if (herd.DomesticCamp < 0)
                {
                    if (!neighborTiles.Contains(herd.Tile)) continue;
                    if (tileKnown != null && !tileKnown(herd.Tile)) continue;
                    wildLocal.Add(herd);
                }
                else if (localCamps != null && localCamps.TryGetValue(herd.DomesticCamp, out var camp))
                    domesticLocal.Add((herd, camp.Tile));
            }
            wildLocal.Sort((a, b) => a.Id.CompareTo(b.Id));
            foreach (var herd in wildLocal)
            {
                int src = FreshwaterAccess.Source(w, herd.Tile);
                if (sourceSet.Contains(src))
                    claims.Add(new Demand { Tile = src, Entity = herd.Id, Kind = "Herd",
                        Freshwater = herd.Animals * w.Config.GetSpecies(herd.Species).FreshwaterConsumption * intervals });
            }
            domesticLocal.Sort((a, b) => a.Herd.Id.CompareTo(b.Herd.Id));
            foreach (var pair in domesticLocal)
            {
                var herdClaims = new List<Demand>();
                ManagedHerdLocalClaims.Append(w, pair.Tile, pair.Herd.Id, 0,
                    pair.Herd.Animals * w.Config.GetSpecies(pair.Herd.Species).FreshwaterConsumption * intervals,
                    years * w.Config.DaysPerYear, herdClaims, visibleNeighbors, claims);
                foreach (var d in herdClaims) if (sourceSet.Contains(d.Tile)) claims.Add(d);
            }
            if (w.Config.Agriculture != null)
            {
                if (localCamps != null)
                    foreach (var c in localCamps.Values.Where(c => c.Population > 0).OrderBy(c => c.Id))
                    {
                        int src = FreshwaterAccess.Source(w, c.Tile);
                        if (sourceSet.Contains(src))
                            claims.Add(new Demand { Tile = src, Entity = c.Id, Kind = "Drinking",
                                Freshwater = c.Population * w.Config.Agriculture.AnnualDrinkingPerPerson * years });
                    }
                foreach (var h in w.Hunters)
                {
                    if (h.Population <= 0 || !neighborTiles.Contains(h.Tile)) continue;
                    if (tileKnown != null && !tileKnown(h.Tile)) continue;
                    int src = FreshwaterAccess.Source(w, h.Tile);
                    if (sourceSet.Contains(src))
                        claims.Add(new Demand { Tile = src, Entity = h.Id, Kind = "Drinking",
                            Freshwater = h.Population * w.Config.Agriculture.AnnualDrinkingPerPerson * years });
                }
            }
            foreach (var d in CivicGovernance.ForecastSanitationWaterClaims(w, years, tileKnown))
                if (sourceSet.Contains(d.Tile)) claims.Add(d);
            claims.Sort((a, b) =>
            {
                int cmp = a.Tile.CompareTo(b.Tile);
                if (cmp != 0) return cmp;
                cmp = a.Entity.CompareTo(b.Entity);
                return cmp != 0 ? cmp : StringComparer.Ordinal.Compare(a.Kind, b.Kind);
            });
            return claims;
        }
        internal static List<Demand> EssentialForecastClaims(WorldState w, double years)
        {
            // Conditional on the current herd locations. The authoritative pass
            // supplies accepted post-movement claims to the same allocator.
            var claims = new List<Demand>(); double intervals = years * w.Config.DaysPerYear / w.Config.BaseDays;
            foreach (var herd in w.Herds.Where(h => h.DomesticCamp < 0 && h.Animals > 0).OrderBy(h => h.Id))
                claims.Add(new Demand { Tile = FreshwaterAccess.Source(w, herd.Tile), Entity = herd.Id, Kind = "Herd",
                    Freshwater = herd.Animals * w.Config.GetSpecies(herd.Species).FreshwaterConsumption * intervals });
            var camps = w.Camps.ToDictionary(c => c.Id);
            foreach (var herd in w.Herds.Where(h => h.DomesticCamp >= 0 && h.Animals > 0).OrderBy(h => h.Id))
                ManagedHerdLocalClaims.Append(w, camps.TryGetValue(herd.DomesticCamp, out var camp) ? camp.Tile : herd.Tile,
                    herd.Id, 0, herd.Animals * w.Config.GetSpecies(herd.Species).FreshwaterConsumption * intervals,
                    years * w.Config.DaysPerYear, claims, null, claims);
            claims.AddRange(DrinkingClaims(w, years));
            claims.AddRange(CivicGovernance.ForecastSanitationWaterClaims(w, years));
            return claims;
        }
        internal static IEnumerable<Demand> DrinkingClaims(WorldState w, double years)
        {
            if (w.Config.Agriculture == null) yield break;
            foreach (var c in w.Camps.Where(c => c.Population > 0).OrderBy(c => c.Id))
                yield return new Demand { Tile = FreshwaterAccess.Source(w, c.Tile), Entity = c.Id, Kind = "Drinking",
                    Freshwater = c.Population * w.Config.Agriculture.AnnualDrinkingPerPerson * years };
            foreach (var h in w.Hunters.Where(h => h.Population > 0).OrderBy(h => h.Id))
                yield return new Demand { Tile = FreshwaterAccess.Source(w, h.Tile), Entity = h.Id, Kind = "Drinking",
                    Freshwater = h.Population * w.Config.Agriculture.AnnualDrinkingPerPerson * years };
        }
        private static List<CanalSegment> ActiveMaintenanceSegments(WorldState w, Camp c)
        {
            var result = new List<CanalSegment>();
            if (!Enabled(w) || w.Canals == null || w.IrrigatedFields == null || !c.IsVillage || c.Population <= 0) return result;
            var localFields = new List<IrrigatedField>();
            foreach (int tile in w.Map.Radius(c.Tile, 1))
            {
                var field = Field(w, tile);
                if (field != null && field.Operator == c.Id && w.Tiles[tile].CultivationOperator == c.Id)
                    localFields.Add(field);
            }
            if (localFields.Count == 0) return result;
            var owned = new Dictionary<int, CanalSegment>();
            foreach (var s in w.Canals)
                if (s.Operator == c.Id && s.Construction >= 1 - 1e-9) owned[s.Id] = s;
            if (owned.Count == 0) return result;
            var activeIds = new HashSet<int>();
            foreach (var field in localFields)
            {
                var path = new List<CanalSegment>();
                var seen = new HashSet<int>();
                if (!owned.TryGetValue(field.Endpoint, out var seg)) continue;
                bool valid = true;
                while (seg != null)
                {
                    if (!seen.Add(seg.Id)) { valid = false; break; }
                    path.Add(seg);
                    if (seg.Parent < 0) break;
                    if (!owned.TryGetValue(seg.Parent, out seg)) { valid = false; break; }
                }
                if (valid) foreach (var s in path) activeIds.Add(s.Id);
            }
            foreach (int id in activeIds.OrderBy(x => x)) result.Add(owned[id]);
            return result;
        }
        public static double UpkeepDemand(WorldState w, Camp c, double years)
            => !Enabled(w) ? 0 : ActiveMaintenanceSegments(w, c)
                .Sum(s => Math.Max(0, years - Math.Max(0, s.FundedYears - years)));
        internal static double Demand(ProductionOrder order, CraftGood good)
            => order?.Irrigation == null ? 0 : good == CraftGood.CanalConstruction ? order.Irrigation.Construction : order.Irrigation.Upkeep;
        internal static void Produce(WorldState w, Camp c, CraftGood good, double amount, ProductionOrder order, double endpoint)
        {
            if (!Enabled(w) || order?.Irrigation == null || amount <= 0) throw new InvalidOperationException("Irrigation requires an Elder allocation.");
            w.Canals ??= new List<CanalSegment>(); w.IrrigatedFields ??= new List<IrrigatedField>();
            if (good == CraftGood.CanalMaintenance)
            {
                double years = w.Config.TurnDays / w.Config.DaysPerYear;
                var segments = ActiveMaintenanceSegments(w, c);
                if (segments.Count == 0) return;
                // Water-filling across active segments equalizes FundedYears upward
                // so partial upkeep maximizes the bottleneck min(FundedYears) across serial paths.
                var ordered = segments.Where(s => s.FundedYears < years - 1e-12)
                    .OrderBy(s => s.FundedYears).ThenBy(s => s.Id).ToArray();
                int count = ordered.Length;
                double remaining = amount;
                for (int i = 0; i < count && remaining > 1e-12; i++)
                {
                    double nextLevel = i + 1 < count ? Math.Min(years, ordered[i + 1].FundedYears) : years;
                    double gap = Math.Max(0, nextLevel - ordered[i].FundedYears);
                    if (gap <= 1e-12) continue;
                    double step = Math.Min(gap, remaining / (i + 1));
                    for (int j = 0; j <= i; j++)
                    {
                        ordered[j].FundedYears += step;
                        ordered[j].Condition = 1;
                    }
                    remaining -= step * (i + 1);
                }
                return;
            }
            var project = order.Irrigation.Project;
            if (project == null || !RouteValid(w, project.Source, project.Route) || !CompatibleRoute(w, c, project.Source, project.Route)) throw new InvalidOperationException("Invalid committed canal route.");
            int parent = -1, from = project.Source;
            foreach (int tile in project.Route)
            {
                var segment = Owned(w, c).FirstOrDefault(s => s.Source == project.Source && s.Tile == tile);
                if (segment == null)
                {
                    if (amount <= 1e-12) break;
                    segment = new CanalSegment { Id = w.NextId++, Operator = c.Id, Source = project.Source, From = from,
                        Tile = tile, Parent = parent, Throughput = w.Config.Agriculture.AnnualThroughput, LastUpdatedDays = endpoint };
                    w.Canals.Add(segment);
                }
                double add = Math.Min(amount, Math.Max(0, 1 - segment.Construction));
                segment.Construction += add; amount -= add;
                segment.CommissioningYears += add * w.Config.TurnDays / w.Config.DaysPerYear;
                if (segment.Construction >= 1 - 1e-9 && segment.CommissioningYears > 0)
                {
                    segment.ActiveDays = endpoint;
                    segment.FundedYears += segment.CommissioningYears;
                    segment.CommissioningYears = 0;
                }
                parent = segment.Id; from = tile;
            }
            var end = Owned(w, c).FirstOrDefault(s => s.Source == project.Source && s.Tile == project.Target && s.Construction >= 1 - 1e-9);
            if (end != null && !w.IrrigatedFields.Any(f => f.Tile == project.Target))
            {
                foreach (int tile in CultivationEconomy.Footprint(w, c).Where(id => id == end.Tile &&
                    (w.Tiles[id].CultivationOperator < 0 || w.Tiles[id].CultivationOperator == c.Id)).OrderBy(id => id))
                    if (!w.IrrigatedFields.Any(f => f.Tile == tile)) w.IrrigatedFields.Add(new IrrigatedField
                        { Tile = tile, Operator = c.Id, Endpoint = end.Id, LastUpdatedDays = endpoint });
                w.IrrigatedFields.Sort((a, b) => a.Tile.CompareTo(b.Tile));
            }
        }
        public static double SoilAdvance(double initial, double years, double water, double operation, double q, AgriculturalTechnologySettings cfg)
            => Math.Min(1, initial + Math.Max(0, years) * Numbers.Clamp(water, 0, 1) * Numbers.Clamp(operation, 0, 1) * Math.Min(1, Math.Max(0, q) / cfg.SoilReferenceQ) / cfg.SoilYears);
        internal static double MeanSoil(double initial, double years, double water, double operation, double q, AgriculturalTechnologySettings cfg)
        {
            double rate = water * operation * Math.Min(1, Math.Max(0, q) / cfg.SoilReferenceQ) / cfg.SoilYears;
            if (rate <= 0 || years <= 0) return initial;
            double grow = Math.Min(years, (1 - initial) / rate);
            return (grow * (initial + .5 * rate * grow) + years - grow) / years;
        }
        internal static IrrigatedField Field(WorldState w, int tile)
        {
            var fields = w.IrrigatedFields;
            if (!Enabled(w) || fields == null) return null;
            int lo = 0, hi = fields.Count - 1;
            while (lo <= hi) { int m = (lo + hi) / 2; if (fields[m].Tile == tile) return fields[m]; if (fields[m].Tile < tile) lo = m + 1; else hi = m - 1; }
            return null;
        }
        internal static void RestoreOverlay(WorldState w, int tile, ref EcologyView view, double queryDays)
        {
            var field = Field(w, tile);
            if (field != null) new IrrigationOverlay(queryDays <= field.LastUpdatedDays + 1e-8 ? field.WaterService : 0, field.Soil, field.Area).Apply(ref view);
        }
        private sealed class Service
        {
            public IrrigatedField Field;
            public CanalSegment[] Path;
            public double Demand, Withdrawal, Retention, Operation, Q;
        }
        // The largest constant request fully fundable by the same analytical
        // water kernel used by EcologicalPressure. This is a pure capacity
        // envelope, not a second integration or debit of the source.
        internal static double WaterEnvelope(WorldState w, int source, double start, double days)
        {
            if (days <= 0) return 0;
            var tile = w.Tiles[source]; var view = LazyEcology.ProjectEcology(w, source, start);
            var cap = new HorizonEcologyCapacity(w, tile, view, start, days);
            double stock = Numbers.Clamp(view.Freshwater, 0, cap.Water);
            double x = cap.WaterRate * days;
            if (x < 1e-8) return stock + cap.WaterRate * cap.Water * days;
            double transient = x > 700 ? 0 : stock / (Math.Exp(x) - 1);
            return Math.Max(0, cap.WaterRate * (cap.Water + transient) * days * (1 - 1e-12));
        }
        public static IrrigationAllocation Allocate(WorldState w, IEnumerable<Demand> essentialClaims,
            double start, double days, IReadOnlyDictionary<int, Camp> campLookup = null, double? operatingFraction = null)
        {
            var result = new IrrigationAllocation();
            if (!Enabled(w) || w.IrrigatedFields == null || w.IrrigatedFields.Count == 0 || days <= 0) return result;
            var cfg = w.Config.Agriculture; double years = days / w.Config.DaysPerYear;
            var segments = (w.Canals ?? new List<CanalSegment>()).ToDictionary(s => s.Id);
            var camps = campLookup ?? w.Camps.ToDictionary(c => c.Id);
            var services = new List<Service>();
            var usersBySegment = new Dictionary<int, List<Service>>();
            foreach (var field in w.IrrigatedFields.OrderBy(f => f.Tile))
            {
                result.Fields[field.Tile] = new IrrigationOverlay(0, field.Soil, field.Area);
                result.EndingSoil[field.Tile] = field.Soil;
                if (!camps.TryGetValue(field.Operator, out var camp) || camp.Population <= 0 || !camp.IsVillage ||
                    w.Map.Distance(camp.Tile, field.Tile) > 1 || w.Tiles[field.Tile].CultivationOperator != camp.Id ||
                    !segments.TryGetValue(field.Endpoint, out var endpoint)) continue;
                var path = new List<CanalSegment>(); var seen = new HashSet<int>(); var segment = endpoint;
                bool valid = true;
                while (segment != null)
                {
                    if (!seen.Add(segment.Id) || segment.Operator != camp.Id || segment.Construction < 1 - 1e-9 ||
                        segment.ActiveDays > start + 1e-8 || segment.FundedYears <= 0) { valid = false; break; }
                    path.Add(segment);
                    if (segment.Parent < 0) break;
                    if (!segments.TryGetValue(segment.Parent, out segment)) { valid = false; break; }
                }
                path.Reverse();
                if (!valid || !RouteValid(w, endpoint.Source, path.Select(s => s.Tile).ToArray())) continue;
                var tile = w.Tiles[field.Tile];
                double q = CultivationEconomy.Intensity(w.Config, tile.CultivationCapital);
                if (q <= 0) continue;
                double demand = 0; var exposure = new SeasonExposure(start, days, w.Config.BaseDays);
                for (int season = 0; season < 4; season++) demand += exposure.At(season) / w.Config.DaysPerYear *
                    cfg.AnnualApplication * field.Area * Math.Max(0, cfg.TargetMoisture - tile.Seasons[season].Moisture) / cfg.TargetMoisture;
                if (demand <= 1e-12) continue;
                double retention = Math.Pow(cfg.Retention, path.Count);
                double funded = Math.Min(1, path.Min(s => s.FundedYears) / years);
                var service = new Service { Field = field, Path = path.ToArray(), Demand = demand,
                    Retention = retention, Withdrawal = demand / retention * funded,
                    Operation = operatingFraction ?? tile.CultivationOperatingFraction, Q = q };
                services.Add(service);
                foreach (var p in path) { if (!usersBySegment.TryGetValue(p.Id, out var users)) usersBySegment[p.Id] = users = new List<Service>(); users.Add(service); }
            }
            // Shared branch bottlenecks reduce competing requests together.
            foreach (var segment in segments.Values.OrderBy(s => s.Id))
            {
                if (!usersBySegment.TryGetValue(segment.Id, out var users)) continue;
                double wanted = users.Sum(s => s.Withdrawal);
                double capacity = segment.Throughput * Math.Min(years, segment.FundedYears) * segment.Condition;
                double scale = wanted <= 0 ? 0 : Math.Min(1, capacity / wanted);
                foreach (var user in users) user.Withdrawal *= scale;
            }
            var essential = essentialClaims.Where(d => d.Freshwater > 0 && d.Kind != "Irrigation")
                .GroupBy(d => d.Tile).ToDictionary(g => g.Key, g => g.Sum(d => d.Freshwater));
            foreach (var group in services.GroupBy(s => s.Path[0].Source).OrderBy(g => g.Key))
            {
                double protectedWater = essential.TryGetValue(group.Key, out double need) ? need : 0;
                double available = Math.Max(0, WaterEnvelope(w, group.Key, start, days) - protectedWater);
                double requested = group.Sum(s => s.Withdrawal);
                double scale = requested <= 0 ? 0 : Math.Min(1, available / requested);
                foreach (var service in group.OrderBy(s => s.Field.Tile))
                {
                    double withdrawal = service.Withdrawal * scale;
                    double delivery = withdrawal * service.Retention;
                    double sigma = Math.Min(1, delivery / service.Demand);
                    bool qualifies = w.Tiles[group.Key].FreshwaterSource == FreshwaterSource.River &&
                        !w.Tiles[service.Field.Tile].River;
                    double irrigation = qualifies ? sigma : 0;
                    double soil = SoilAdvance(service.Field.Soil, years, irrigation, service.Operation, service.Q, cfg);
                    double mean = MeanSoil(service.Field.Soil, years, irrigation, service.Operation, service.Q, cfg);
                    result.Fields[service.Field.Tile] = new IrrigationOverlay(sigma, mean, service.Field.Area);
                    result.EndingSoil[service.Field.Tile] = soil;
                    if (withdrawal > 0) result.Withdrawals.Add(new Demand { Tile = group.Key, Entity = service.Field.Operator,
                        Kind = "Irrigation", Freshwater = withdrawal });
                    result.Receipts.Add(new IrrigationWaterReceipt { Source = group.Key, Field = service.Field.Tile,
                        Operator = service.Field.Operator, Withdrawal = withdrawal, Delivery = delivery, Loss = withdrawal - delivery,
                        SupplyFraction = sigma, SoilBefore = service.Field.Soil, SoilAfter = soil });
                    result.Withdrawal += withdrawal; result.Delivery += delivery; result.Loss += withdrawal - delivery;
                }
            }
            return result;
        }
        internal static void Commit(WorldState w, IrrigationAllocation allocation, double endpoint, double years)
        {
            if (!Enabled(w)) return;
            foreach (var field in w.IrrigatedFields ?? new List<IrrigatedField>())
                if (allocation.Fields.TryGetValue(field.Tile, out var overlay))
                {
                    field.Soil = allocation.EndingSoil[field.Tile]; field.WaterService = overlay.Water;
                    field.LastUpdatedDays = endpoint; w.Tiles[field.Tile].Irrigated = overlay.Water > 0;
                }
            var canals = w.Canals;
            if (canals == null || canals.Count == 0) return;
            var byId = canals.ToDictionary(s => s.Id);
            var served = new HashSet<int>();
            foreach (var field in w.IrrigatedFields ?? new List<IrrigatedField>())
            {
                if (!byId.TryGetValue(field.Endpoint, out var seg)) continue;
                var seen = new HashSet<int>();
                while (seg != null && seen.Add(seg.Id))
                {
                    served.Add(seg.Id);
                    if (seg.Parent < 0) break;
                    byId.TryGetValue(seg.Parent, out seg);
                }
            }
            foreach (var segment in canals)
            {
                if (served.Contains(segment.Id))
                    segment.FundedYears = Math.Max(0, segment.FundedYears - years);
                segment.LastUpdatedDays = endpoint;
            }
        }
        internal static void ReleaseAbandoned(WorldState w, int campId)
        {
            if (!Enabled(w)) return;
            if (w.Canals != null) foreach (var s in w.Canals) if (s.Operator == campId) s.Operator = -1;
            if (w.IrrigatedFields != null) foreach (var f in w.IrrigatedFields) if (f.Operator == campId) f.Operator = -1;
        }
        // Cultivation rights are authoritative. Transfer abandoned networks at
        // Close only when their serviced fields have one legal successor. Soil
        // stays attached to the field even while a contested network is dry.
        internal static void ReconcileOwnership(WorldState w)
        {
            if (!Enabled(w) || w.Canals == null || w.IrrigatedFields == null) return;
            var segments = w.Canals.ToDictionary(s => s.Id);
            int Root(int endpoint)
            {
                var seen = new HashSet<int>();
                while (segments.TryGetValue(endpoint, out var s) && s.Parent >= 0 && seen.Add(endpoint)) endpoint = s.Parent;
                return endpoint;
            }
            var camps = w.Camps.ToDictionary(c => c.Id);
            var networks = w.Canals.GroupBy(s => Root(s.Id)).ToDictionary(g => g.Key, g => g.ToArray());
            foreach (var group in w.IrrigatedFields.GroupBy(f => Root(f.Endpoint)).OrderBy(g => g.Key))
            {
                var owners = group.Select(f => w.Tiles[f.Tile].CultivationOperator).Distinct().ToArray();
                if (owners.Length != 1 || !camps.TryGetValue(owners[0], out var camp) || camp.Population <= 0 || !camp.IsVillage ||
                    group.Any(f => w.Map.Distance(camp.Tile, f.Tile) > 1)) continue;
                foreach (var f in group) f.Operator = camp.Id;
                foreach (var segment in networks[group.Key]) segment.Operator = camp.Id;
            }
        }
        // Owner-facing explanation; no external actor observation schema change.
        public static string Status(WorldState w, Camp c)
        {
            if (w.Config.Agriculture == null) return "Agricultural technology profile is disabled.";
            var n = w.Config.Agriculture;
            string tool = AgriculturalTechnology.Plough(w, c)
                ? $"Farm Tools: Plough knowledge, maintenance {n.PloughMaintenance:F2}x / crop harvest {n.PloughHarvest:F2}x; {c.Inventory.FarmTools * w.Config.Neolithic.FarmToolFarmerYearsPerUnit:F1} Farmer-years available."
                : "Farm Tools retain their current benefits until Plough is learned.";
            var owned = Owned(w, c).ToArray();
            var fields = (w.IrrigatedFields ?? new List<IrrigatedField>()).Where(f => f.Operator == c.Id).ToArray();
            string gate = !n.IrrigationEnabled ? "disabled" : !c.People.HasTech(NeolithicTech.Irrigation) ? "knowledge required" :
                !FertileGate(w, c) ? $"develop every naturally fertile tile to q={n.DevelopmentQ:F2} first" :
                "Elder considers paid projects when additional food capacity is needed";
            return tool + "\nIrrigation: " + gate + $". Canals {owned.Length}, completed {owned.Count(s => s.Construction >= 1 - 1e-9)}; " +
                $"funded segment-years {owned.Sum(s => s.FundedYears):F1}.\n" +
                $"Construction: {n.CanalHousingMaterials:F1} Housing Materials + {n.CanalBuilderYears:F1} Builder-years per segment, plus tools and prepaid first-period upkeep. " +
                $"Upkeep: {n.UpkeepHousingMaterials:F2} materials + {n.UpkeepBuilderYears:F2} Builder-years per segment-year.\n" +
                $"Served fields {fields.Length}; last supply {(fields.Length == 0 ? 0 : fields.Average(f => f.WaterService)):P0}; " +
                $"soil development {(fields.Length == 0 ? 0 : fields.Average(f => f.Soil)):P0}.";
        }
        public static void Validate(WorldState w)
        {
            if (w.Canals == null && w.IrrigatedFields == null) return;
            bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x) && x >= 0;
            var segments = w.Canals ?? new List<CanalSegment>();
            if (segments.Any(s => s == null || s.Id <= 0 || s.Source < 0 || s.Source >= w.Tiles.Length || s.Tile < 0 || s.Tile >= w.Tiles.Length ||
                !Finite(s.Construction) || s.Construction > 1 + 1e-8 || !Finite(s.CommissioningYears) || !Finite(s.FundedYears) || !Finite(s.Throughput) ||
                !Finite(s.LastUpdatedDays) || !Finite(s.ActiveDays) || !Finite(s.Condition) || s.Condition > 1 || s.From < 0 || s.From >= w.Tiles.Length) || segments.Select(s => s.Id).Distinct().Count() != segments.Count)
                throw new InvalidOperationException("Invalid canal state.");
            var ids = segments.ToDictionary(s => s.Id);
            foreach (var s in segments)
            {
                var seen = new HashSet<int>(); var p = s;
                while (p != null)
                {
                    if (!seen.Add(p.Id)) throw new InvalidOperationException("Cyclic canal state.");
                    if (p.Parent < 0) { if (p.From != p.Source) throw new InvalidOperationException("Disconnected intake."); break; }
                    int from = p.From;
                    if (!ids.TryGetValue(p.Parent, out p) || p.Source != s.Source || p.Tile != from) throw new InvalidOperationException("Disconnected canal state.");
                }
            }
            var fields = w.IrrigatedFields ?? new List<IrrigatedField>();
            if (fields.Any(f => f == null || f.Tile < 0 || f.Tile >= w.Tiles.Length || !ids.ContainsKey(f.Endpoint) || (ids.TryGetValue(f.Endpoint, out var end) && end.Tile != f.Tile) ||
                !Finite(f.Soil) || f.Soil > 1 || !Finite(f.Area) || f.Area > 1 || !Finite(f.WaterService) || f.WaterService > 1 || !Finite(f.LastUpdatedDays)) ||
                fields.Select(f => f.Tile).Distinct().Count() != fields.Count) throw new InvalidOperationException("Invalid irrigated field state.");
            fields.Sort((a, b) => a.Tile.CompareTo(b.Tile));
        }
    }
}
