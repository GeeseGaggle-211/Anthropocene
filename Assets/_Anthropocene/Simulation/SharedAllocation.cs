using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Anthropocene.Simulation
{
    // Transient memo of pure local kernel results for one camp's plan (opening
    // projections and pasture scans; SharedAllocation also keeps its
    // operation-independent tile forecasts). Every key holds the exact bit
    // pattern of each input that varies inside the plan, so a hit returns what
    // the uncached call computes. It is owned by
    // one plan on one worker and is never iterated, so neither worker
    // partitioning nor collection order can change a value or a work count.
    internal sealed class PlanningKernelCache
    {
        private readonly WorldState world;
        private readonly Dictionary<(int, long), EcologyView> views = new Dictionary<(int, long), EcologyView>();
        // Pasture capacity, non-field capacity and settled local capacity.
        internal double[] Pasture;
        // Kernel work done for this plan (each investment is two tile
        // integrations). Deterministic per plan. With one ladder per plan no
        // investment repeats, so investments are counted, not memoized.
        internal int Projections, Investments;
        internal PlanningKernelCache(WorldState world) { this.world = world; }
        private static long Bits(double value) => BitConverter.DoubleToInt64Bits(value);
        private void Hit() { if (ResolutionMetrics.CaptureDetailedCounters) Interlocked.Increment(ref TurnDiagnostics.For(world).KernelCacheHits); }
        internal EcologyView View(int tile, double days)
        {
            var key = (tile, Bits(days));
            if (views.TryGetValue(key, out var view)) { Hit(); return view; }
            if (ResolutionMetrics.CaptureDetailedCounters) Interlocked.Increment(ref TurnDiagnostics.For(world).PlannerTileProjections);
            Projections++;
            return views[key] = LazyEcology.ProjectEcology(world, tile, days);
        }
    }

    // A local portfolio service inside EconomicPlanner, shared by all capable
    // camps. No lifestyle planner, trial world mutation or per-year transition.
    // Operation choices are induced by the existing 32 q bands and tool/staffing
    // breakpoints. Forecasts are cached once; price queries visit only totals.
    internal sealed class SharedAllocation
    {
        internal sealed class Choice
        {
            public CampProductionDemand Demand;
            public HerdLadder Herd;
            public int Herders, Farmers, Workers, Target;
            public double AnimalFood, Value, CurrentWork, FutureWork, Operation, ExpansionYears;
        }
        private sealed class Portfolio
        {
            public CampProductionDemand Demand;
            public HerdLadder Herd;
            public SharedExpansionSchedule[] Expansion;
            public FoodIncomeLedger Ledger;
            public double Value, CurrentWork, FutureWork, Operation;
        }
        private readonly WorldState world;
        private readonly Camp camp;
        private readonly Portfolio[] portfolios;
        private readonly double years, currentLife;
        internal int OperationChoices => portfolios.Length;
        // Direct tile forecasts and (cached) investment comparisons made while
        // building the portfolios: the plan's food-kernel work outside Select.
        internal int TileForecasts { get; private set; }
        internal int InvestmentComparisons => cache?.Investments ?? -1;
        private readonly PlanningKernelCache cache;
        internal int HerdChoices => portfolios.Sum(p => (p.Herd?.Steps ?? 0) + 1);
        private double[] priceCrossings;
        internal double[] PriceCrossings
        {
            get
            {
                if (priceCrossings != null) return priceCrossings;
                var values = new List<double>(); var work = new List<double>();
                foreach (var p in portfolios)
                    for (int k = p.Herd?.IndexOf(p.Herd.MinimumHerders) ?? 0; k <= (p.Herd?.Steps ?? 0); k++)
                    {
                        values.Add(p.Value + (p.Herd?.TotalValue[k] ?? 0));
                        work.Add(p.CurrentWork + p.FutureWork + (p.Herd?.WorkerYears[k] ?? 0));
                    }
                var envelope = new HerdLadder { Value = new double[values.Count - 1], Capacity = new double[values.Count],
                    TotalValue = values.ToArray(), WorkerYears = work.ToArray() };
                return priceCrossings = envelope.PriceCrossings;
            }
        }
        // cacheKernel = false is the uncached reference used by the exactness checks.
        internal SharedAllocation(WorldState world, Camp camp, CampProductionDemand demand, double breeding, bool cacheKernel = true)
        {
            this.world = world; this.camp = camp;
            cache = cacheKernel ? new PlanningKernelCache(world) : null;
            // Land forecasts that cannot depend on this camp's operating choice:
            // tiles outside its footprint keep their own saved fraction, and a
            // field with no capital has q = 0, so operation never reaches the
            // kernel. One forecast per tile serves every operating choice.
            var fixedLand = new Dictionary<(int, long), (double Receipts, double Food, double Biomass, double HarvestWork)>();
            // Future harvest productivity follows each portfolio's own future field
            // state, with CultivationEconomy.HarvestProductivity's stock-weighted
            // crop/wild rule over the footprint (the current turn keeps the committed
            // productivity, which is what the resolver harvests this turn).
            bool cropRate = world.Config.CropHarvestingOnly && world.Config.FarmerProductivity > 1;
            int season = (int)Math.Floor(world.Days / world.Config.BaseDays) % 4;
            var footprintTiles = new HashSet<int>(demand.Footprint);
            var cfg = world.Config; var n = cfg.Neolithic;
            years = cfg.TurnDays / cfg.DaysPerYear;
            double future = Math.Max(0, n.PlanningHorizonYears - years), r = n.DiscountRatePerYear;
            currentLife = HerdLabour.DiscountedYears(r, years);
            double futureLife = Math.Exp(-r * years) * HerdLabour.DiscountedYears(r, future);
            double raw = demand.CultivationEnabled ? CultivationEconomy.MaintenanceWorkerRequirement(world, camp) : 0;
            double toolWorkers = Math.Max(0, camp.Inventory.FarmTools) * n.FarmToolFarmerYearsPerUnit / years;
            double efficiency = CultivationEconomy.FarmToolFieldWorkEfficiency(n);
            double Workers(double fraction)
            {
                double required = raw * fraction;
                double covered = Math.Min(toolWorkers, required / efficiency);
                return covered + Math.Max(0, required - covered * efficiency);
            }
            var staffing = new SortedSet<int> { 0, (int)Math.Min(camp.Population, Math.Ceiling(Workers(1) - 1e-9)) };
            if (raw > 0)
            {
                double q = demand.Operated.Length > 0 ? demand.Operated.Average(id => CultivationEconomy.Intensity(cfg, world.Tiles[id].CultivationCapital)) : 0;
                double decay = Math.Log(2) / n.DecayHalfLifeYears * Math.Max(years, future);
                for (int band = 1; band < 32 && q > 0 && decay > 0; band++)
                {
                    double fraction = 1 + 2 * Math.Log((band / 32.0) / q) / decay;
                    if (fraction > 0 && fraction < 1) staffing.Add((int)Math.Min(camp.Population, Math.Ceiling(Workers(fraction) - 1e-9)));
                }
                staffing.Add((int)Math.Min(camp.Population, Math.Ceiling(Math.Min(toolWorkers, raw / efficiency))));
            }
            var choices = new List<Portfolio>();
            var pending = new List<(int Farmers, double Operation, CampProductionDemand Demand, double MaximumNeed, double Aquatic, double FutureMaintenance, double Residual)>();
            foreach (int farmers in staffing)
            {
                double covered = Math.Min(farmers, Math.Min(toolWorkers, raw / efficiency));
                double operation = raw > 0 ? Math.Min(1, (covered * efficiency + Math.Max(0, farmers - covered)) / raw) : 0;
                var d = demand.AllocationCopy(); d.OperationFarmers = farmers; d.Upkeep = farmers;
                d.PhysicalExpansion = null; d.Expansion = null; d.HerdLadder = null;
                double land = 0, futureLand = 0, futureMaintenance = 0, residual = 0, futureBiomass = 0, futureFieldFood = 0, futureFieldWork = 0;
                foreach (int id in d.Eligible.Where(id => world.Tiles[id].Land))
                {
                    var tile = world.Tiles[id];
                    EcologyView opening;
                    if (cache != null) opening = cache.View(id, world.Days);
                    else
                    {
                        opening = LazyEcology.ProjectEcology(world, id, world.Days);
                        if (ResolutionMetrics.CaptureDetailedCounters) Interlocked.Add(ref TurnDiagnostics.For(world).PlannerTileProjections, 2);
                    }
                    bool owned = d.Operated.Contains(id);
                    double start = Math.Max(0, opening.CultivationCapital);
                    double op = owned ? operation : tile.CultivationOperatingFraction;
                    var fixedKey = (id, owned && start > 0 ? BitConverter.DoubleToInt64Bits(op) : long.MinValue);
                    double Mean(double duration)
                    {
                        double rate = Math.Log(2) / n.DecayHalfLifeYears * (1 - op);
                        return rate * duration > 1e-10 ? start * (1 - Math.Exp(-rate * duration)) / (rate * duration) : start;
                    }
                    double end = CultivationEconomy.Decay(cfg, start, op, years);
                    double terminalCapital = CultivationEconomy.Decay(cfg, start, op, Math.Max(years, n.PlanningHorizonYears));
                    if (cache == null || !fixedLand.TryGetValue(fixedKey, out var forecast))
                    {
                        // A land tile's accessible availability is its projected opening stock.
                        double request = cache != null ? Math.Max(0, opening.Food)
                            : LazyEcology.AccessibleAvailability(world, id, world.Days, cfg.TurnDays, op, Mean(years));
                        var current = FoodHorizonForecast.Tile(world, id, world.Days, cfg.TurnDays,
                            new[] { new Demand { Tile = id, Entity = camp.Id, Kind = "Gathering", Food = request } }, Mean(years), end, op, false, opening);
                        // Conservative repeatable refill: an emptied food pool at the
                        // horizon's mean retained capital. No gross renewal is sold.
                        var empty = opening; empty.Food = 0; empty.CultivationCapital = Mean(Math.Max(years, n.PlanningHorizonYears));
                        empty.PotentialFood = Math.Max(0, current.ClosingBiomass - current.ClosingFood);
                        var refill = FoodHorizonForecast.Tile(world, id, world.Days, cfg.TurnDays, null,
                            empty.CultivationCapital, terminalCapital, op, false, empty);
                        double harvestWork = 0;
                        if (cropRate && footprintTiles.Contains(id))
                        {
                            var capacity = new EcologyCapacity(world, tile, empty, season);
                            double crop = tile.CultivationOperator >= 0 && tile.CultivationOperator != camp.Id ? 0 : Math.Max(0, capacity.Food - capacity.WildFood);
                            double cropShare = capacity.Food > 0 ? crop / capacity.Food : 0;
                            harvestWork = refill.ClosingFood * (1 - cropShare + cropShare / cfg.FarmerProductivity);
                        }
                        forecast = (current.HumanFoodReceipts, refill.ClosingFood, refill.ClosingBiomass, harvestWork);
                        TileForecasts += 2;
                        if (cache != null) fixedLand[fixedKey] = forecast;
                    }
                    else if (ResolutionMetrics.CaptureDetailedCounters) Interlocked.Add(ref TurnDiagnostics.For(world).KernelCacheHits, 2);
                    land += forecast.Receipts;
                    if (cropRate && footprintTiles.Contains(id)) { futureFieldFood += forecast.Food; futureFieldWork += forecast.HarvestWork; }
                    futureLand += forecast.Food;
                    futureBiomass += forecast.Biomass;
                    residual += forecast.Food;
                    if (owned && raw > 0)
                    {
                        CultivationEconomy.Factors(cfg, tile, out _, out double maintenance);
                        futureMaintenance += n.MaintenanceWorkersPerTileE(cfg.MaintenanceCoefficient) * maintenance * op
                            * .5 * (CultivationEconomy.EffortForCapital(cfg, start) + CultivationEconomy.EffortForCapital(cfg, terminalCapital));
                    }
                }
                double aquatic = Math.Max(0, demand.ForageSupply - demand.PlantSupply);
                d.PlantSupply = land; d.ForageSupply = land + aquatic;
                d.TerrestrialAfterHerds = Math.Max(0, land - demand.ManagedFood.HumanFoodDisplaced);
                d.ForageAfterHerds = d.TerrestrialAfterHerds + aquatic;
                d.FutureForageAfterHerds = Math.Max(0, futureLand - demand.ManagedFood.HumanFoodDisplaced) + aquatic;
                d.FutureUpkeep = futureMaintenance;
                d.FutureHarvestProductivity = !d.CultivationEnabled ? 1 : !cropRate ? cfg.FarmerProductivity : futureFieldWork > 0 ? futureFieldFood / futureFieldWork : 1;
                double forageNeed = d.ManagedFood.FoodConsumption * cfg.GenerationIntervals;
                d.FuturePastureCapacity = forageNeed > 0
                    ? Math.Max(0, futureBiomass - Math.Min(futureLand, d.Meals)) / forageNeed : double.PositiveInfinity;
                double maximumNeed = Math.Max(0, (d.Meals + d.Biological) / years - d.FutureForageAfterHerds / years);
                pending.Add((farmers, operation, d, maximumNeed, aquatic, futureMaintenance, residual));
            }
            // One expansion ladder and one herd ladder per plan
            // (Docs/SharedAllocationValidation.md, shared ladders). Both are priced
            // from the most maintained operating choice. The expansion ladder is
            // recorded once up to the largest food need and each choice keeps its
            // own need-capped prefix; a reduced choice therefore cannot also buy
            // back the decay it just accepted. Herd values, harvest savings and
            // future pasture likewise assume the maintained field state.
            double ladderOperation = pending.Max(x => x.Operation);
            var reference = pending.First(x => x.Operation == ladderOperation).Demand;
            var herd = HerdLabour.Applies(world, camp) ? HerdLabour.Build(world, camp, reference, breeding, cache) : null;
            double ladderNeed = pending.Where(x => x.Demand.CultivationEnabled && x.MaximumNeed > 0).Select(x => x.MaximumNeed).DefaultIfEmpty(0).Max();
            var ladder = ladderNeed > 0
                ? CultivationEconomy.ExpansionLadder(world, camp, years, demand.Footprint, ladderNeed, ladderOperation, cache)
                : Array.Empty<CultivationEconomy.ExpansionStep>();
            foreach (var (farmers, operation, d, maximumNeed, aquatic, futureMaintenance, residual) in pending)
            {
                d.PhysicalExpansion = d.CultivationEnabled && maximumNeed > 0
                    ? CultivationEconomy.CutLadder(ladder, maximumNeed)
                    : Array.Empty<CultivationEconomy.ExpansionStep>();
                d.SetFoodEnvelopes(world, camp, demand.ManagedFood.AnimalFood);
                var ledger = new FoodIncomeLedger(world, camp, d);
                var incoming = ledger.Receive(d.TerrestrialAfterHerds, aquatic, 0);
                double vFood = ExpansionValuation.FoodValue(cfg, camp);
                double annualFood = d.FutureForageAfterHerds / years;
                double futureMeals = Math.Min(d.Meals / years, annualFood);
                double usefulCurrent = incoming.Consumption + incoming.Reserve + incoming.Growth + incoming.IncomingPlantInput;
                double gatherRate = cfg.GatherPerPerson * cfg.DaysPerYear / cfg.BaseDays;
                double harvestRate = gatherRate * Math.Max(1, d.HarvestProductivity);
                double currentHarvest = Math.Min(d.TerrestrialAfterHerds, farmers * harvestRate * years);
                double currentWorkers = farmers + Math.Max(0, usefulCurrent - currentHarvest) / years / gatherRate;
                double futureHarvest = Math.Min(futureMeals, futureMaintenance * gatherRate * Math.Max(1, d.FutureHarvestProductivity));
                double futureWorkers = futureMaintenance + Math.Max(0, futureMeals - futureHarvest) / gatherRate;
                double value = Math.Exp(-r * years) * (incoming.Value - ledger.Receive(0, 0, 0).Value)
                    + futureLife * futureMeals * vFood
                    + Math.Exp(-r * n.PlanningHorizonYears) * n.FoodRiskWeight * vFood * Math.Min(residual, d.Meals);
                choices.Add(new Portfolio { Demand = d, Ledger = ledger, Herd = herd, Expansion = new SharedExpansionSchedule[(herd?.Steps ?? 0) + 1], Value = value,
                    CurrentWork = currentWorkers * currentLife, FutureWork = futureWorkers * futureLife, Operation = operation });
            }
            portfolios = choices.ToArray();
            if (ResolutionMetrics.CaptureDetailedCounters)
            {
                var diagnostics = TurnDiagnostics.For(world);
                Interlocked.Increment(ref diagnostics.SharedAllocations);
                Interlocked.Add(ref diagnostics.SharedOperationChoices, portfolios.Length);
                Interlocked.Add(ref diagnostics.SharedHerdChoices, HerdChoices);
            }
        }
        // Transient diagnostics (never authoritative or saved): every candidate's
        // objective components at the given prices, in Select's own terms.
        internal sealed class CandidateRow
        {
            public double Operation, CropValue, HerdCurrentValue, HerdFutureValue, HerdResidualValue, CurrentWork, FutureWork,
                HerdManagementYears, HerdSavedHarvestYears, HerdReplacementYears, HerdCraftYears, ExpansionYears, ExpansionValue, Net,
                HarvestProductivity, FutureHarvestProductivity;
            public int Farmers, Herders, Target, Workers; public bool Feasible;
        }
        [ThreadStatic] internal static bool CaptureLast;
        [ThreadStatic] internal static SharedAllocation Last;
        [ThreadStatic] internal static double LastCurrentPrice, LastFuturePrice;
        [ThreadStatic] internal static int LastBudget;
        internal List<CandidateRow> Candidates(double currentPrice, double futurePrice, int residentBudget)
        {
            var rows = new List<CandidateRow>();
            foreach (var p in portfolios)
            {
                var h = p.Herd; int first = h == null ? 0 : h.IndexOf(h.MinimumHerders);
                for (int k = first; k <= (h?.Steps ?? 0); k++)
                {
                    double current = p.CurrentWork + (h?.CurrentWorkerYears[k] ?? 0);
                    double future = p.FutureWork + (h == null ? 0 : h.WorkerYears[k] - h.CurrentWorkerYears[k]);
                    int fixedWorkers = p.Demand.OperationFarmers + (h?.CountAt(k) ?? p.Demand.ReservedHerders);
                    int workers = fixedWorkers + (int)Math.Max(0, Math.Ceiling(current / Math.Max(1e-12, currentLife) - fixedWorkers - 1e-9));
                    double animal = h == null ? p.Demand.ManagedFood.AnimalFood : h.CurrentAnimalFood[k] + h.SlaughterFood[k];
                    var expansion = p.Expansion[k] ?? (p.Expansion[k] = ExpansionValuation.SharedSchedule(world, camp, p.Demand, animal, h?.FutureAnnualFood[k], p.Ledger));
                    expansion.Purchase(currentPrice, futurePrice, Math.Max(0, residentBudget - workers) * years, out double expansionYears, out double expansionValue);
                    double value = p.Value + (h?.TotalValue[k] ?? 0);
                    rows.Add(new CandidateRow { Operation = p.Operation, Farmers = p.Demand.OperationFarmers, Herders = h?.CountAt(k) ?? p.Demand.ReservedHerders,
                        Target = h?.Target(h.CountAt(k)) ?? 0, Workers = workers, Feasible = workers <= residentBudget,
                        CropValue = p.Value, HerdCurrentValue = h?.CurrentValue[k] ?? 0, HerdFutureValue = h?.FutureValue[k] ?? 0, HerdResidualValue = h?.ResidualValue[k] ?? 0,
                        CurrentWork = current, FutureWork = future, HerdManagementYears = h?.ManagementYears[k] ?? 0, HerdSavedHarvestYears = h?.SavedHarvestYears[k] ?? 0,
                        HerdReplacementYears = h?.ReplacementYears[k] ?? 0, HerdCraftYears = h?.CraftYears[k] ?? 0, ExpansionYears = expansionYears, ExpansionValue = expansionValue,
                        Net = value - currentPrice * current - futurePrice * future + expansionValue,
                        HarvestProductivity = p.Demand.HarvestProductivity, FutureHarvestProductivity = p.Demand.FutureHarvestProductivity });
                }
            }
            return rows;
        }
        // Current spare work can have zero price without making its committed
        // future work free. The full craft planner runs only for the selected
        // portfolio, not for each herder or a population Cartesian product.
        internal Choice Select(double currentPrice, double futurePrice, int residentBudget)
        {
            if (ResolutionMetrics.CaptureDetailedCounters) Interlocked.Increment(ref TurnDiagnostics.For(world).SharedSelections);
            if (CaptureLast) { Last = this; LastCurrentPrice = currentPrice; LastFuturePrice = futurePrice; LastBudget = residentBudget; }
            currentPrice = double.IsNaN(currentPrice) ? 0 : Math.Max(0, currentPrice);
            futurePrice = double.IsNaN(futurePrice) ? 0 : Math.Max(0, futurePrice);
            Choice best = null; Portfolio selected = null; SharedExpansionSchedule selectedExpansion = null; double bestNet = double.NegativeInfinity;
            foreach (var p in portfolios)
            {
                var h = p.Herd;
                int first = h == null ? 0 : h.IndexOf(h.MinimumHerders);
                for (int k = first; k <= (h?.Steps ?? 0); k++)
                {
                    double current = p.CurrentWork + (h?.CurrentWorkerYears[k] ?? 0);
                    double future = p.FutureWork + (h == null ? 0 : h.WorkerYears[k] - h.CurrentWorkerYears[k]);
                    int fixedWorkers = p.Demand.OperationFarmers + (h?.CountAt(k) ?? p.Demand.ReservedHerders);
                    int workers = fixedWorkers + (int)Math.Max(0, Math.Ceiling(current / Math.Max(1e-12, currentLife) - fixedWorkers - 1e-9));
                    if (workers > residentBudget) continue;
                    double animal = h == null ? p.Demand.ManagedFood.AnimalFood : h.CurrentAnimalFood[k] + h.SlaughterFood[k];
                    var expansion = p.Expansion[k] ?? (p.Expansion[k] = ExpansionValuation.SharedSchedule(world, camp, p.Demand, animal, h?.FutureAnnualFood[k], p.Ledger));
                    expansion.Purchase(currentPrice, futurePrice, (residentBudget - workers) * years, out double expansionYears, out double expansionValue);
                    double value = p.Value + (h?.TotalValue[k] ?? 0);
                    double net = value - currentPrice * current - futurePrice * future + expansionValue;
                    if (net <= bestNet + 1e-9) continue;
                    bestNet = net; selected = p; selectedExpansion = expansion; best = new Choice { Herd = h, Herders = h?.CountAt(k) ?? p.Demand.ReservedHerders,
                        Farmers = p.Demand.OperationFarmers, Workers = workers, Target = h?.Target(h.CountAt(k)) ?? 0,
                        AnimalFood = animal, Value = value, CurrentWork = current, FutureWork = future, Operation = p.Operation, ExpansionYears = expansionYears };
                }
            }
            if (best != null) best.Demand = selected.Demand.WithSharedExpansion(world, camp, best.AnimalFood, selectedExpansion.Price(futurePrice));
            // Minimum seed staffing is still bounded by residents. A distressed
            // zero-fulfilment plan may be short of food; it never creates workers.
            if (best == null)
            {
                var p = portfolios[0]; var h = p.Herd; int k = h?.IndexOf(h.MinimumHerders) ?? 0;
                double animal = h == null ? p.Demand.ManagedFood.AnimalFood : h.CurrentAnimalFood[k] + h.SlaughterFood[k];
                best = new Choice { Demand = p.Demand.WithAnimalFood(world, camp, animal), Herd = h,
                    Herders = h?.CountAt(k) ?? p.Demand.ReservedHerders, Target = h?.Target(h.CountAt(k)) ?? 0, AnimalFood = animal };
            }
            return best;
        }
    }
}
