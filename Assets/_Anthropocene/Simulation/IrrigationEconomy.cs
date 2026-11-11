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
        public static bool RouteValid(WorldState w, int source, IReadOnlyList<int> route)
        {
            if (!Enabled(w) || source < 0 || source >= w.Tiles.Length || !FreshSource(w.Tiles[source]) ||
                route == null || route.Count == 0 || route.Count > 7) return false;
            int prior = source; var seen = new HashSet<int> { source };
            foreach (int id in route)
            {
                if (id < 0 || id >= w.Tiles.Length || !seen.Add(id) || !w.Tiles[id].TraversableLand ||
                    w.Map.Distance(prior, id) != 1 || w.Tiles[id].Elevation >
                    w.Tiles[prior].Elevation + w.Config.Agriculture.HydraulicTolerance ||
                    Blocked(w, prior, id)) return false;
                prior = id;
            }
            return true;
        }
        private static bool CompatibleRoute(WorldState w, Camp c, int source, int[] route)
        {
            int from = source, parent = -1;
            foreach (int tile in route)
            {
                var existing = Owned(w, c).FirstOrDefault(s => s.Source == source && s.Tile == tile);
                if (existing != null && (existing.From != from || existing.Parent != parent)) return false;
                parent = existing?.Id ?? -2; from = tile;
            }
            return true;
        }
        // Conservative local routing: visible continuous terrain only. The
        // farming footprint bounds targets and paths; intakes may border it.
        public static IrrigationProject Opportunity(WorldState w, Camp c, bool discoveryOnly = false)
        {
            if (!Enabled(w) || !CultivationEconomy.Enabled(c)) return null;
            var visible = Knowledge.Visible(w, c.Player);
            var footprint = CultivationEconomy.Footprint(w, c).Where(id => visible.Contains(id) &&
                (w.Tiles[id].CultivationOperator < 0 || w.Tiles[id].CultivationOperator == c.Id)).ToArray();
            var allowed = new HashSet<int>(footprint);
            var sources = footprint.SelectMany(id => w.Map.Radius(id, 1)).Distinct().Where(id => visible.Contains(id) && FreshSource(w.Tiles[id]) && w.Tiles[id].Seasons.Any(s => s.MaxFreshwater > 0 && s.WaterRecoveryPerDay > 0)).OrderBy(id => id);
            IrrigationProject best = null;
            foreach (int source in sources)
            {
                var queue = new Queue<int[]>(); queue.Enqueue(Array.Empty<int>());
                var visited = new HashSet<int> { source };
                while (queue.Count > 0)
                {
                    var path = queue.Dequeue(); int last = path.Length == 0 ? source : path[path.Length - 1];
                    foreach (int id in w.Map.Radius(last, 1).Where(id => allowed.Contains(id)).OrderBy(id => id))
                    {
                        if (visited.Contains(id)) continue;
                        var route = path.Concat(new[] { id }).ToArray();
                        if (!RouteValid(w, source, route) || !CompatibleRoute(w, c, source, route)) continue;
                        visited.Add(id); queue.Enqueue(route);
                        if (!w.Tiles[id].Seasons.Any(s => s.Moisture < w.Config.Agriculture.TargetMoisture - 1e-9)) continue;
                        if ((w.IrrigatedFields ?? new List<IrrigatedField>()).Any(f => f.Tile == id)) continue;
                        double remaining = route.Sum(tile => 1 - (Owned(w, c).FirstOrDefault(s => s.Source == source && s.Tile == tile)?.Construction ?? 0));
                        if (remaining <= 1e-9) continue;
                        double gain = discoveryOnly ? 1 : CapacityGain(w, c, source, route, id);
                        if (gain <= 0) continue;
                        if (best == null || gain / remaining > best.AnnualFoodGain / best.Remaining + 1e-9)
                            best = new IrrigationProject { Source = source, Target = id, Route = route, Remaining = remaining, AnnualFoodGain = gain };
                    }
                }
            }
            return best;
        }
        private static double CapacityGain(WorldState w, Camp camp, int source, int[] route, int id)
        {
            var view = LazyEcology.ProjectEcology(w, id, w.Days);
            // Match field development's retained next-boundary food metric.
            // An undeveloped target must first acquire cultivation through the
            // ordinary paid field-development path.
            if (view.CultivationCapital <= 0) return 0;
            double years = w.Config.TurnDays / w.Config.DaysPerYear;
            var canals = (w.Canals ?? new List<CanalSegment>()).Where(s => s.Source == source).ToList();
            var segmentIds = new HashSet<int>(canals.Select(s => s.Id));
            var fields = (w.IrrigatedFields ?? new List<IrrigatedField>()).Where(f => segmentIds.Contains(f.Endpoint)).ToList();
            var beforeWorld = w.WithIrrigationNetwork(canals.ToList(), fields.ToList());
            var essential = EssentialForecastClaims(w, years);
            var beforeWater = Allocate(beforeWorld, essential, w.Days, w.Config.TurnDays);
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
            var afterWater = Allocate(prospective, essential, w.Days, w.Config.TurnDays);
            double gain = 0;
            foreach (var field in fields.Where(f => f.Operator == camp.Id))
            {
                var old = beforeWater.Fields.TryGetValue(field.Tile, out var opening) ? opening : new IrrigationOverlay(0, field.Soil, field.Area);
                var supplied = afterWater.Fields[field.Tile];
                var before = FoodHorizonForecast.Tile(w, field.Tile, w.Days, w.Config.TurnDays, irrigationOverlay: old);
                var after = FoodHorizonForecast.Tile(w, field.Tile, w.Days, w.Config.TurnDays, irrigationOverlay: supplied);
                gain += after.ClosingFood - before.ClosingFood;
            }
            return Math.Max(0, gain) / years;
        }
        internal static List<Demand> EssentialForecastClaims(WorldState w, double years)
        {
            // Conditional on the current herd locations. The authoritative pass
            // supplies accepted post-movement claims to the same allocator.
            var claims = new List<Demand>(); double intervals = years * w.Config.DaysPerYear / w.Config.BaseDays;
            foreach (var herd in w.Herds.Where(h => h.DomesticCamp < 0).OrderBy(h => h.Id))
                claims.Add(new Demand { Tile = FreshwaterAccess.Source(w, herd.Tile), Entity = herd.Id, Kind = "Herd",
                    Freshwater = herd.Animals * w.Config.GetSpecies(herd.Species).FreshwaterConsumption * intervals });
            var camps = w.Camps.ToDictionary(c => c.Id);
            foreach (var herd in w.Herds.Where(h => h.DomesticCamp >= 0).OrderBy(h => h.Id))
                ManagedHerdLocalClaims.Append(w, camps.TryGetValue(herd.DomesticCamp, out var camp) ? camp.Tile : herd.Tile,
                    herd.Id, 0, herd.Animals * w.Config.GetSpecies(herd.Species).FreshwaterConsumption * intervals,
                    years * w.Config.DaysPerYear, claims, null, claims);
            claims.AddRange(DrinkingClaims(w, years));
            return claims;
        }
        internal static IEnumerable<Demand> DrinkingClaims(WorldState w, double years)
        {
            if (w.Config.Agriculture == null) yield break;
            foreach (var c in w.Camps.OrderBy(c => c.Id))
                yield return new Demand { Tile = FreshwaterAccess.Source(w, c.Tile), Entity = c.Id, Kind = "Drinking",
                    Freshwater = c.Population * w.Config.Agriculture.AnnualDrinkingPerPerson * years };
            foreach (var h in w.Hunters.OrderBy(h => h.Id))
                yield return new Demand { Tile = FreshwaterAccess.Source(w, h.Tile), Entity = h.Id, Kind = "Drinking",
                    Freshwater = h.Population * w.Config.Agriculture.AnnualDrinkingPerPerson * years };
        }
        public static double UpkeepDemand(WorldState w, Camp c, double years)
            => !Enabled(w) ? 0 : Owned(w, c).Where(s => s.Construction >= 1 - 1e-9)
                .Sum(s => Math.Max(0, years - Math.Max(0, s.FundedYears - years)));
        internal static double Demand(ProductionOrder order, CraftGood good)
            => order?.Irrigation == null ? 0 : good == CraftGood.CanalConstruction ? order.Irrigation.Construction : order.Irrigation.Upkeep;
        internal static void Produce(WorldState w, Camp c, CraftGood good, double amount, ProductionOrder order, double endpoint)
        {
            if (!Enabled(w) || order?.Irrigation == null || amount <= 0) throw new InvalidOperationException("Irrigation requires an Elder allocation.");
            w.Canals ??= new List<CanalSegment>(); w.IrrigatedFields ??= new List<IrrigatedField>();
            if (good == CraftGood.CanalMaintenance)
            {
                foreach (var segment in Owned(w, c).Where(s => s.Construction >= 1 - 1e-9))
                {
                    double add = Math.Min(amount, Math.Max(0, w.Config.TurnDays / w.Config.DaysPerYear - segment.FundedYears));
                    segment.FundedYears += add; amount -= add;
                    if (add > 0) segment.Condition = 1;
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
            double start, double days)
        {
            var result = new IrrigationAllocation();
            if (!Enabled(w) || w.IrrigatedFields == null || days <= 0) return result;
            var cfg = w.Config.Agriculture; double years = days / w.Config.DaysPerYear;
            var segments = (w.Canals ?? new List<CanalSegment>()).ToDictionary(s => s.Id);
            var camps = w.Camps.ToDictionary(c => c.Id);
            var services = new List<Service>();
            var usersBySegment = new Dictionary<int, List<Service>>();
            foreach (var field in w.IrrigatedFields.OrderBy(f => f.Tile))
            {
                result.Fields[field.Tile] = new IrrigationOverlay(0, field.Soil, field.Area);
                result.EndingSoil[field.Tile] = field.Soil;
                if (!camps.TryGetValue(field.Operator, out var camp) || !camp.IsVillage ||
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
                    Operation = tile.CultivationOperatingFraction, Q = q };
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
            foreach (var segment in w.Canals ?? new List<CanalSegment>())
            { segment.FundedYears = Math.Max(0, segment.FundedYears - years); segment.LastUpdatedDays = endpoint; }
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
                if (owners.Length != 1 || !camps.TryGetValue(owners[0], out var camp) || !camp.IsVillage ||
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
