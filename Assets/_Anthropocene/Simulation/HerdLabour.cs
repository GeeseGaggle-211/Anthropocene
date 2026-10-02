using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Anthropocene.Simulation
{
    // Opt-in herd-labour coupling (Docs/HerdValuationValidation.md):
    //   * total retention alternatives: current milk/slaughter, finite continuation and residual assets,
    //     less management, manufacturing and replacement work at the shared labour price;
    //   * controlled slaughter: after breeding, animals above the camp's HerdTarget leave the herd and become carcass food.
    // Everything here is pure except Slaughter, the resolver's single mutation. The added local valuation walks at most 19 pasture
    // tiles plus local choices; mixed-species exact slaughter splits cost O(choices * owned herds * log owned herds).
    // Forecasts retain their existing own-herd pass. No new persistent state or time-step loop.

    // Piecewise food buckets in the order supply fills them, with the value per food unit of each (person-QoL-years per food unit).
    internal static class HerdBuckets
    {
        // Value per year of the food interval [from, to) (food per year, measured from the start of supply) over consecutive buckets of
        // `size` (per year) worth `value` per unit; food beyond the last bucket is worth nothing.
        public static double ValueOf(double from, double to, double[] size, double[] value)
        {
            double sum = 0, edge = 0;
            for (int i = 0; i < size.Length; i++)
            {
                double start = edge, end = edge + Math.Max(0, size[i]); edge = end;
                double overlap = Math.Min(to, end) - Math.Max(from, start);
                if (overlap > 0) sum += overlap * Math.Max(0, value[i]);
            }
            return sum;
        }
    }

    // One camp's herder steps for a turn. Step k adds the k-th herder, who looks after Capacity[k] - Capacity[k-1] animals (tool-covered
    // herders first, so the first steps carry more animals). Production compares complete candidate totals; Value is a legacy diagnostic.
    internal sealed class HerdLadder
    {
        public double[] Value = Array.Empty<double>(), Capacity = new double[1];
        // Animals the herd would reach this turn if it bred at the planned intensity and the pasture carried it (never staffed above this).
        public double GrownAnimals;
        public int Steps => Value.Length;
        // The seed herd the planner keeps managed whatever its value (SimulationConfig.MinimumHerdHerders, at most the steps there are).
        public int MinimumHerders;
        // Portfolio totals are transient; legacy Value is diagnostic only in portfolio mode.
        public double[] TotalValue, WorkerYears, CurrentAnimalFood, SlaughterFood;
        public double[] CurrentValue, FutureValue, ResidualValue, ManagementYears, SavedHarvestYears, ReplacementYears, CraftYears;
        // Joint-allocation candidates use bounded staffing breakpoints. Legacy
        // ladders retain one index per herder for exact control continuation.
        internal int[] HerderCounts;
        internal double[] CurrentWorkerYears;
        internal double[] FutureAnnualFood;
        internal int IndexOf(int herders)
        {
            if (HerderCounts == null) return Math.Max(0, Math.Min(herders, Steps));
            int index = Array.BinarySearch(HerderCounts, herders);
            return index >= 0 ? index : Math.Max(0, ~index - 1);
        }
        internal int CountAt(int index) => HerderCounts == null ? index : HerderCounts[index];
        internal double AnimalFoodAt(int herders) => CurrentAnimalFood[IndexOf(herders)];
        internal double SlaughterFoodAt(int herders) => SlaughterFood[IndexOf(herders)];
        // HV2 diagnostics are transient, like the ladder itself.
        public double PastureCapacity, NonFieldCapacity, FieldAlternativeRate, CapitalLifeYears;
        // Production maximizes total value minus price times net discounted work, subject to the seed floor.
        // Net work is monotone in price; herder counts need not be. Legacy marginal fixtures use Value.
        public int Staffed(double price)
        {
            if (TotalValue != null)
            {
                double w = double.IsNaN(price) ? 0 : Math.Max(0, price);
                int low = 0, high = PriceCrossings.Length;
                while (low < high)
                {
                    int mid = (low + high) / 2;
                    if (w < PriceCrossings[mid]) low = mid + 1; else high = mid;
                }
                return CountAt(Choices[low]);
            }
            int count = 0;
            while (count < Value.Length && Value[count] > 0 && (!(price > 0) || Value[count] >= price)) count++;
            return Math.Min(Steps, Math.Max(count, MinimumHerders));
        }
        private int[] choices;
        private double[] priceCrossings;
        internal double[] PriceCrossings { get { EnsureEnvelope(); return priceCrossings; } }
        private int[] Choices { get { EnsureEnvelope(); return choices; } }
        // Upper concave envelope of total value versus net work. Dominated choices
        // never win at nonnegative w. Its adjacent slopes give every possible
        // staffing switch, including non-concave marginal herd choices.
        private void EnsureEnvelope()
        {
            if (choices != null) return;
            var hull = new List<int>();
            int firstIndex = IndexOf(MinimumHerders);
            foreach (int k in Enumerable.Range(firstIndex, Steps - firstIndex + 1)
                .OrderBy(k => WorkerYears[k]).ThenByDescending(k => TotalValue[k]).ThenBy(k => k))
            {
                if (hull.Count > 0 && (WorkerYears[k] <= WorkerYears[hull[hull.Count - 1]] + 1e-10
                    || TotalValue[k] <= TotalValue[hull[hull.Count - 1]] + 1e-10)) continue;
                while (hull.Count >= 2)
                {
                    int a = hull[hull.Count - 2], b = hull[hull.Count - 1];
                    double first = (TotalValue[b] - TotalValue[a]) / (WorkerYears[b] - WorkerYears[a]);
                    double next = (TotalValue[k] - TotalValue[b]) / (WorkerYears[k] - WorkerYears[b]);
                    if (first > next) break;
                    hull.RemoveAt(hull.Count - 1);
                }
                hull.Add(k);
            }
            choices = hull.ToArray(); priceCrossings = new double[Math.Max(0, choices.Length - 1)];
            for (int i = 0; i < priceCrossings.Length; i++)
                priceCrossings[i] = (TotalValue[choices[i + 1]] - TotalValue[choices[i]]) / (WorkerYears[choices[i + 1]] - WorkerYears[choices[i]]);
        }
        // Herd size the staffed herders can manage at full fulfilment, never above the herd the pasture grows to.
        public int Target(int herders)
        {
            double managed = Capacity[IndexOf(herders)];
            return (int)Math.Floor(Math.Min(GrownAnimals, managed) + 1e-9);
        }

        // Closed form for tests and the builder: each step's break-even labour price from the cumulative capacities, the net food one managed
        // animal yields per year, the food per year supply already provides before the herd, and the buckets. A step's herd food is worth
        //   bucket value (the food is scarce: it fills buckets past the free supply)  +  w * L, L = gatherer-years it saves per herder-year
        // where L = (herd food of the step that the camp's demand would otherwise have to harvest) / altFoodPerWorkerYear, and the step is worth
        // staffing while that is at least w, i.e. while w <= bucket / (1 - L); a step with L >= 1 pays for itself at any price (+infinity).
        // demandPerYear and altFoodPerWorkerYear of 0 switch the labour-saving term off. A running minimum keeps the ladder nonincreasing.
        public static double[] StepValues(double[] capacity, double netFoodPerAnimalYear, double supplyPerYear, double[] bucketSize, double[] bucketValue)
            => StepValues(capacity, netFoodPerAnimalYear, supplyPerYear, bucketSize, bucketValue, 0, 0);
        public static double[] StepValues(double[] capacity, double netFoodPerAnimalYear, double supplyPerYear, double[] bucketSize, double[] bucketValue, double demandPerYear, double altFoodPerWorkerYear)
        {
            var values = new double[Math.Max(0, capacity.Length - 1)];
            double floor = double.PositiveInfinity;
            for (int k = 1; k < capacity.Length; k++)
            {
                double from = supplyPerYear + capacity[k - 1] * netFoodPerAnimalYear, to = supplyPerYear + capacity[k] * netFoodPerAnimalYear;
                double bucket = netFoodPerAnimalYear > 0 ? HerdBuckets.ValueOf(from, to, bucketSize, bucketValue) : 0;
                double saved = 0;
                if (netFoodPerAnimalYear > 0 && altFoodPerWorkerYear > 0 && demandPerYear > 0)
                {
                    double before = capacity[k - 1] * netFoodPerAnimalYear, food = (capacity[k] - capacity[k - 1]) * netFoodPerAnimalYear;
                    saved = Math.Max(0, Math.Min(food, demandPerYear - before)) / altFoodPerWorkerYear;
                }
                double v = saved >= 1 ? double.PositiveInfinity : bucket / (1 - saved);
                floor = Math.Min(floor, v); values[k - 1] = floor;
            }
            return values;
        }
    }

    internal static class HerdLabour
    {
        // Reuse scratch across choices. Exact same floors/largest remainders as
        // resolver Split, without allocating/sorting LINQ records for each herder.
        internal sealed class Liquidation : IComparer<int>
        {
            private readonly int[] counts, order;
            private readonly double[] carcasses;
            private readonly long total;
            private readonly bool uniformIntegerYield;
            private int excess;
            public Liquidation(int[] counts, double[] carcasses)
            {
                this.counts = counts; this.carcasses = carcasses; total = counts.Sum(x => (long)x);
                order = Enumerable.Range(0, counts.Length).ToArray();
                uniformIntegerYield = carcasses.Length > 0 && carcasses[0] == Math.Floor(carcasses[0]) && carcasses.All(x => x == carcasses[0]);
            }
            public int Compare(int a, int b)
            {
                int comparison = (excess * (long)counts[b] % total).CompareTo(excess * (long)counts[a] % total);
                return comparison != 0 ? comparison : a.CompareTo(b);
            }
            public double Food(int slaughtered)
            {
                if (slaughtered <= 0 || total <= 0) return 0;
                excess = (int)Math.Min(slaughtered, total);
                if (uniformIntegerYield) return excess * carcasses[0];
                double food = 0; int allocated = 0;
                for (int i = 0; i < counts.Length; i++)
                {
                    int killed = (int)(excess * (long)counts[i] / total);
                    allocated += killed; food += killed * carcasses[i];
                }
                int remainder = excess - allocated;
                if (remainder > 0)
                {
                    Array.Sort(order, this);
                    for (int i = 0; i < remainder; i++) food += carcasses[order[i]];
                }
                return food;
            }
        }

        // Finite discounted integrals. No annual forecast loop or fictitious loss rate.
        private static double ExpIntegral(double rate, double years)
        {
            double x = rate * years;
            return Math.Abs(x) < 1e-5 ? years * (1 + x / 2 + x * x / 6 + x * x * x / 24)
                : (Math.Exp(x) - 1) / rate;
        }
        internal static double DiscountedYears(double discount, double years) => ExpIntegral(-discount, Math.Max(0, years));
        internal static double EndAnimals(double animals, double growth, double capacity, double years)
            => animals <= 0 || capacity <= 0 ? 0 : Math.Min(capacity, animals * Math.Exp(Math.Min(700, Math.Max(0, growth) * years)));
        // Integral of exp(-r t) min(K, H exp(g t)), including its exact capacity crossing.
        internal static double AnimalYears(double animals, double growth, double capacity, double discount, double years)
        {
            if (!(animals > 0) || !(capacity > 0) || !(years > 0)) return 0;
            animals = Math.Min(animals, capacity); growth = Math.Max(0, growth);
            double hit = growth > 0 ? Math.Min(years, Math.Log(capacity / animals) / growth) : years;
            return animals * ExpIntegral(growth - discount, hit)
                + capacity * Math.Exp(-discount * hit) * DiscountedYears(discount, years - hit);
        }
        // Piecewise useful food value, integrated at analytic stock/bucket crossings.
        internal static double Continuation(double animals, double growth, double capacity, double yield,
            double supply, double[] sizes, double[] values, double discount, double years)
        {
            if (!(yield > 0)) return 0;
            double result = 0, edge = 0;
            for (int i = 0; i < sizes.Length; i++)
            {
                double before = Math.Max(0, (edge - supply) / yield);
                edge += Math.Max(0, sizes[i]);
                double after = Math.Max(0, (edge - supply) / yield);
                result += yield * Math.Max(0, values[i]) * (AnimalYears(animals, growth, Math.Min(capacity, after), discount, years)
                    - AnimalYears(animals, growth, Math.Min(capacity, before), discount, years));
            }
            return result;
        }

        // At most 19 local visible tiles (camp vision 2, herd movement 1). Mobile
        // outer pasture is an option for a future move, never extra current receipts.
        // Existing grown-herd forage/water forecasts still bound the current target.
        internal struct PastureBand
        {
            public int Tile;
            public double Animals, Rate;
        }
        internal static PastureBand[] Pasture(WorldState w, Camp c, ManagedFoodForecast forecast, double intervals,
            out double capacity, out double nonFieldCapacity, CampProductionDemand demand = null)
        {
            double forage = 0, weakForage = 0, water = 0;
            double gatherRate = w.Config.GatherPerPerson * w.Config.DaysPerYear / w.Config.BaseDays;
            var bands = new List<PastureBand>();
            int range = c.IsVillage ? 1 : Math.Min(w.Config.VisionRadius, 1 + w.Config.HerdMovement);
            var exposure = new SeasonExposure(w.Days, w.Config.TurnDays, w.Config.BaseDays);
            if (ResolutionMetrics.CaptureDetailedCounters) Interlocked.Increment(ref TurnDiagnostics.For(w).PastureScans);
            foreach (int id in w.Map.Radius(c.Tile, range))
            {
                var tile = w.Tiles[id];
                if (tile.ElevationLevel >= 10) continue;
                if (ResolutionMetrics.CaptureDetailedCounters) Interlocked.Increment(ref TurnDiagnostics.For(w).PlannerTileProjections);
                var stock = LazyEcology.ProjectEcology(w, id, w.Days);
                if (tile.TraversableLand)
                {
                    double biomass = Math.Max(0, stock.PotentialFood);
                    forage += biomass;
                    bool field = w.Map.Distance(c.Tile, id) <= 1 && CultivationEconomy.Cultivable(tile)
                        && (tile.CultivationOperator < 0 || tile.CultivationOperator == c.Id);
                    if (!field) weakForage += biomass;
                    double rate = field ? gatherRate * Math.Max(1, w.Config.FarmerProductivity) : gatherRate;
                    if (biomass > 0) bands.Add(new PastureBand { Tile = id, Animals = biomass, Rate = rate });
                }
                if (tile.WaterBody == WaterKind.Saltwater || tile.FreshwaterSource == FreshwaterSource.None) continue;
                water += Math.Max(0, stock.Freshwater);
                for (int season = 0; season < 4; season++)
                    water += Math.Max(0, tile.Seasons[season].MaxFreshwater * tile.Seasons[season].WaterRecoveryPerDay) * exposure.At(season);
            }
            double foodNeed = forecast.FoodConsumption * intervals, waterNeed = forecast.WaterConsumption * intervals;
            double foodCapacity = foodNeed > 0 ? forage / foodNeed : double.PositiveInfinity;
            double waterCapacity = waterNeed > 0 ? water / waterNeed : double.PositiveInfinity;
            capacity = Math.Min(foodCapacity, waterCapacity);
            if (double.IsPositiveInfinity(capacity)) capacity = forecast.Animals;
            nonFieldCapacity = forage > 0 ? capacity * weakForage / forage : 0;
            double animalCapacity = capacity;
            return bands.Select(b => new PastureBand { Tile = b.Tile, Animals = forage > 0 ? animalCapacity * b.Animals / forage : 0, Rate = b.Rate })
                .OrderBy(b => b.Rate).ThenBy(b => b.Tile).ToArray();
        }

        // Consume the cheapest pasture first. A band and a herder step can each
        // straddle the other; walk them together once, O(steps + local tiles).
        internal static double[] SavedLabour(double[] capacity, PastureBand[] bands, double fallbackRate)
        {
            var result = new double[Math.Max(0, capacity.Length - 1)];
            int band = 0; double used = 0;
            for (int k = 1; k < capacity.Length; k++)
            {
                double added = capacity[k] - capacity[k - 1], left = added, saving = 0;
                while (left > 1e-12 && band < bands.Length)
                {
                    double take = Math.Min(left, Math.Max(0, bands[band].Animals - used));
                    if (bands[band].Rate > 0) saving += take / bands[band].Rate;
                    left -= take; used += take;
                    if (used >= bands[band].Animals - 1e-12) { band++; used = 0; }
                }
                if (left > 0 && fallbackRate > 0) saving += left / fallbackRate;
                result[k - 1] = added > 0 ? saving / added : 0;
            }
            return result;
        }

        // The planner's herd valuation applies to automated Value-rule husbandry camps when the lever is on.
        public static bool Applies(WorldState w, Camp c)
            => (w.Config.HerdLabourEnabled || w.Config.SharedCropHerdAllocationEnabled) && w.Config.Neolithic.ValueFulfilmentSelected && CultivationEconomy.HusbandryEnabled(c) && c.Population > 0;

        // Builds the ladder after the demand pass has priced every claim. Null when the camp has no managed animals.
        // cache (optional, one plan only) reuses the two pasture scans, which read
        // only the committed boundary, across the shared allocation's portfolios.
        public static HerdLadder Build(WorldState w, Camp c, CampProductionDemand d, double breeding)
            => Build(w, c, d, breeding, null);
        internal static HerdLadder Build(WorldState w, Camp c, CampProductionDemand d, double breeding, PlanningKernelCache cache)
        {
            var cfg = w.Config; var n = cfg.Neolithic;
            double years = cfg.TurnDays / cfg.DaysPerYear, intervals = cfg.GenerationIntervals;
            var herds = d.ManagedFood.OwnedHerds;
            double animals = herds.Sum(h => (double)h.Animals);
            if (!(animals > 0) || !(years > 0)) return null;
            var grown = d.GrownHerd ?? TurnResolver.ForecastGrownManagedAnimalFood(w, c, breeding, intervals, years, c.Tile);
            double grownAnimals = grown.Animals > 0 ? grown.Animals : animals;
            var counts = herds.Select(h => h.Animals).ToArray();
            var carcasses = herds.Select(h => CarcassFood(w, h)).ToArray();
            var liquidation = new Liquidation(counts, carcasses);
            bool shared = cfg.SharedCropHerdAllocationEnabled;
            var capacity = new List<double> { 0 };
            int[] countsOfHerders = null;
            if (shared)
            {
                int maximum = Math.Min(c.Population, HerdingTooling.RequiredHerdersFor(w, c, years, grownAnimals));
                var breaks = new SortedSet<int> { 0, maximum, Math.Min(maximum, cfg.MinimumHerdHerders), HerdingTooling.RequiredHerdersFor(w, c, years, animals) };
                int toolBreak = (int)Math.Min(maximum, Math.Max(0, c.Inventory.HerdingTools) * n.HerdingToolCoverageYearsPerUnit / years);
                breaks.Add(toolBreak); breaks.Add(Math.Min(maximum, toolBreak + 1));
                // Fixed allocation resolution, not a population or time loop.
                for (int band = 1; band < 32; band++) breaks.Add((int)Math.Ceiling(maximum * band / 32.0));
                countsOfHerders = breaks.Where(k => k >= 0 && k <= maximum).ToArray();
                capacity = countsOfHerders.Select(k => HerdingTooling.AnimalsManagedBy(w, c, years, k)).ToList();
            }
            else for (int k = 1; k <= c.Population && capacity[k - 1] < grownAnimals - 1e-9; k++)
                capacity.Add(HerdingTooling.AnimalsManagedBy(w, c, years, k));
            var ladder = new HerdLadder { Capacity = capacity.ToArray(), GrownAnimals = grownAnimals,
                MinimumHerders = Math.Min(shared ? countsOfHerders[countsOfHerders.Length - 1] : capacity.Count - 1, Math.Max(0, cfg.MinimumHerdHerders)),
                Value = new double[capacity.Count - 1], TotalValue = new double[capacity.Count], WorkerYears = new double[capacity.Count],
                CurrentAnimalFood = new double[capacity.Count], SlaughterFood = new double[capacity.Count],
                CurrentValue = new double[capacity.Count], FutureValue = new double[capacity.Count], ResidualValue = new double[capacity.Count],
                ManagementYears = new double[capacity.Count], SavedHarvestYears = new double[capacity.Count], ReplacementYears = new double[capacity.Count], CraftYears = new double[capacity.Count],
                HerderCounts = countsOfHerders, CurrentWorkerYears = new double[capacity.Count], FutureAnnualFood = new double[capacity.Count] };
            if (ResolutionMetrics.CaptureDetailedCounters) Interlocked.Increment(ref TurnDiagnostics.For(w).HerdLadderBuilds);
            double localCapacity;
            if (cache?.Pasture != null)
            {
                ladder.PastureCapacity = cache.Pasture[0]; ladder.NonFieldCapacity = cache.Pasture[1]; localCapacity = cache.Pasture[2];
                if (ResolutionMetrics.CaptureDetailedCounters) Interlocked.Increment(ref TurnDiagnostics.For(w).KernelCacheHits);
            }
            else
            {
                Pasture(w, c, grown, intervals, out ladder.PastureCapacity, out ladder.NonFieldCapacity, d);
                // Continuation promises only the existing one-tile forage/water footprint.
                // Mobile outer pasture remains diagnostic, never free future receipts.
                var settled = EconomicPlanner.Preview(c, EconomicIntent.From(c), c.Population); settled.IsVillage = true;
                Pasture(w, settled, grown, intervals, out localCapacity, out _, d);
                if (cache != null) cache.Pasture = new[] { ladder.PastureCapacity, ladder.NonFieldCapacity, localCapacity };
            }
            if (shared) localCapacity = Math.Min(localCapacity, d.FuturePastureCapacity);
            double horizon = Math.Max(years, n.PlanningHorizonYears), future = horizon - years, r = n.DiscountRatePerYear;
            ladder.CapitalLifeYears = DiscountedYears(r, horizon);
            double nowDiscount = Math.Exp(-r * years), terminalDiscount = Math.Exp(-r * horizon);
            double annualHuman = (shared ? d.FutureForageAfterHerds : d.ForageAfterHerds) / years, annualMeals = d.Meals / years;
            double harvestRate = cfg.GatherPerPerson * cfg.DaysPerYear / cfg.BaseDays;
            if (d.Established) harvestRate *= Math.Max(1, d.HarvestProductivity);
            ladder.FieldAlternativeRate = harvestRate;
            // Future harvesting uses the planned field state's productivity (shared
            // planner); the current turn keeps the committed productivity.
            double futureHarvestRate = shared && d.Established
                ? cfg.GatherPerPerson * cfg.DaysPerYear / cfg.BaseDays * Math.Max(1, d.FutureHarvestProductivity) : harvestRate;
            TurnResolver.FoodBucketTargets(w, c, intervals, years, d.Meals, c.Food,
                out _, out _, out double reserveFill, out double growthFood);
            double vFood = ExpansionValuation.FoodValue(cfg, c);
            double foodPerBirth = ExpansionValuation.FoodPerBirth(cfg.Consumption * intervals, QoLMortality.MaximumRate(w, c), years);
            double vGrowth = foodPerBirth > 0 ? Math.Max(0, ExpansionValuation.MarginalWelfare(w, c)) * ExpansionValuation.PersonLife(n) / foodPerBirth : 0;
            var fabric = d.Claims.FirstOrDefault(x => x.Good == CraftGood.AnimalFabric);
            var housing = d.Claims.FirstOrDefault(x => x.Good == CraftGood.LightHousing);
            double ClaimValue(ProductionClaim claim) => claim != null && claim.Valued && claim.Need > 0
                ? claim.FirstUnitValue * Math.Exp(-n.ValueCurveSteepness * claim.Stock / claim.Need) : 0;
            double housingInput = Math.Min(fabric?.Quantity ?? 0, housing?.Quantity ?? 0);
            double fabricInput = Math.Max(0, (fabric?.Quantity ?? 0) - housingInput);
            double humanNow = Math.Max(0, c.PlantFood) + Math.Max(0, c.AquaticFood) + d.ForageAfterHerds;
            double mealGap = Math.Max(0, d.Meals - humanNow);
            double humanLeft = Math.Max(0, humanNow - d.Meals);
            double reserveGap = Math.Max(0, reserveFill - humanLeft);
            double growthGap = Math.Max(0, growthFood - Math.Max(0, humanLeft - reserveFill));
            // Plant availability may fill meals/reserves, never the typed animal-input axis.
            // Fabric used for tents is removed from the clothing bucket, so every input is credited once.
            var currentSizes = new[] { mealGap, fabricInput, housingInput, reserveGap, growthGap };
            var values = new[] { vFood, ClaimValue(fabric), ClaimValue(housing), n.FoodRiskWeight * vFood, vGrowth };
            double futureFabric = fabricInput > 0 ? Math.Min(fabricInput / years, c.Population * n.FabricConsumptionPerPersonTurn / n.ReferenceTurnYears) : 0;
            double futureHousing = housingInput > 0 ? Math.Min(housingInput / years,
                c.Population / n.LightHousingPeoplePerUnit * -Math.Log(1 - n.HousingDecayPerTurn) / n.ReferenceTurnYears) : 0;
            double futureMealGap = Math.Max(0, annualMeals - annualHuman);
            double futureGrowthGap = Math.Max(0, growthFood / years - Math.Max(0, annualHuman - annualMeals));
            var ledger = shared ? new FoodIncomeLedger(w, c, d) : null;
            FoodIncomeLedger.Receipt afterHuman = default;
            if (shared)
            {
                afterHuman = ledger.Receive(d.TerrestrialAfterHerds, Math.Max(0, d.ForageAfterHerds - d.TerrestrialAfterHerds), 0);
                currentSizes = new[] { afterHuman.RemainingConsumption, fabricInput, housingInput, afterHuman.RemainingReserve, afterHuman.RemainingGrowth };
            }
            // Reserve and finished-goods shortfalls are one-time, not annual streams.
            var futureSizes = new[] { futureMealGap, futureFabric, futureHousing, 0.0, futureGrowthGap };
            double management = Math.Pow(d.ManagedFood.ManagementFulfillment, cfg.AnimalManagementExponent);
            double fullCurrentFood = management > 0 ? d.ManagedFood.AnimalFood / management : 0;
            double yield = fullCurrentFood / animals / years;
            double baseGrowth = grown.GrowthRate * Math.Max(0, breeding);
            var tool = d.Claims.FirstOrDefault(x => x.Good == CraftGood.HerdingTools);
            double toolLabour = tool?.LabourPerUnit ?? 0;
            if (!(toolLabour > 0) && tool != null)
            {
                double rate = tool.Rate * cfg.DaysPerYear / cfg.BaseDays;
                toolLabour = rate > 0 ? 1 / rate : double.PositiveInfinity;
                foreach (var material in new[] { MaterialId.Stone, MaterialId.Wood })
                {
                    bool specialist = c.People.HasTech(EconomicIntents.RequiredTechnology(MaterialEconomy.Specialist(material)));
                    double extraction = n.SpecialistMaterialExtractionRate * (specialist ? 1 : n.GeneralistTaskEfficiency) * cfg.DaysPerYear / cfg.BaseDays;
                    toolLabour += extraction > 0 ? 1 / extraction : double.PositiveInfinity;
                }
            }
            for (int k = 0; k <= ladder.Steps; k++)
            {
                int herdersNow = ladder.CountAt(k);
                int target = ladder.Target(herdersNow);
                var plan = HerdingTooling.Plan(w, c, years, herdersNow, animals);
                double milk = fullCurrentFood * Math.Pow(plan.Fulfillment, cfg.AnimalManagementExponent);
                double slaughter = liquidation.Food(Math.Max(0, (int)animals - target));
                ladder.CurrentAnimalFood[k] = milk; ladder.SlaughterFood[k] = slaughter;
                double incoming = milk + slaughter;
                // Shared mode: one receipt per step serves both the marginal value
                // (against the human-only receipt above) and the harvest saving.
                var receipts = shared ? ledger.Receive(d.TerrestrialAfterHerds, Math.Max(0, d.ForageAfterHerds - d.TerrestrialAfterHerds), incoming) : default;
                double currentValue = shared
                    ? receipts.Value - afterHuman.Value
                    : HerdBuckets.ValueOf(c.AnimalFood, c.AnimalFood + incoming, currentSizes, values);
                // Savings are harvesting that could actually be done, after typed inputs and inventory.
                double currentFoodNeed = Math.Max(0, d.Biological);
                double beforeHarvest = Math.Min(d.ForageAfterHerds, currentFoodNeed);
                double afterHarvest = Math.Min(d.ForageAfterHerds, Math.Max(0, currentFoodNeed - incoming + fabricInput + housingInput));
                if (shared)
                {
                    beforeHarvest = afterHuman.Consumption + afterHuman.Reserve + afterHuman.Growth + afterHuman.IncomingPlantInput;
                    double used = receipts.Consumption + receipts.Reserve + receipts.Growth + receipts.IncomingPlantInput + receipts.IncomingAnimalInput;
                    afterHarvest = Math.Max(receipts.IncomingPlantInput, used - incoming);
                }
                double gatherPerYear = cfg.GatherPerPerson * cfg.DaysPerYear / cfg.BaseDays;
                double HarvestWorkers(double food)
                {
                    double land = Math.Min(d.TerrestrialAfterHerds, food);
                    if (shared) return d.OperationFarmers + (gatherPerYear > 0
                        ? Math.Max(0, food - Math.Min(land, d.OperationFarmers * harvestRate * years)) / years / gatherPerYear : 0);
                    double field = d.Established && harvestRate > 0 ? Math.Max(d.Upkeep, land / years / harvestRate) : 0;
                    double remaining = Math.Max(0, food - Math.Min(land, field * harvestRate * years));
                    return field + (gatherPerYear > 0 ? remaining / years / gatherPerYear : 0);
                }
                double savedNow = Math.Max(0, HarvestWorkers(beforeHarvest) - HarvestWorkers(afterHarvest));
                double primitive = cfg.AnimalsPerHerder;
                double activeHerderYears = plan.CoveredHerderYears + years * Math.Max(0,
                    Math.Min(animals, plan.Capacity) - plan.CoveredHerderYears / years * n.StoneHerdingToolAnimalsPerHerder) / primitive;
                double coveredFraction = activeHerderYears > 0 ? Math.Min(1, plan.CoveredHerderYears / activeHerderYears) : 0;
                double effective = primitive + coveredFraction * (n.StoneHerdingToolAnimalsPerHerder - primitive);
                // A held/cut target cannot promise unrestricted offspring while
                // the rolling plan caps them. Only the full feasible breeding
                // option prices that continuation; the seed floor is explicit.
                double growth = target <= localCapacity + 1e-9 && (!shared || target >= Math.Floor(grownAnimals + 1e-9)) ? baseGrowth : 0;
                double futureTarget = shared ? Math.Min(target, localCapacity) : target;
                double futureCapacity = Math.Max(futureTarget, Math.Min(localCapacity, c.Population * effective));
                double neededTools = future > 0 ? coveredFraction * AnimalYears(futureTarget, growth, futureCapacity, 0, future) / effective / n.HerdingToolCoverageYearsPerUnit : 0;
                double remainingTools = Math.Max(0, c.Inventory.HerdingTools - plan.ToolsConsumed);
                double replacement = Math.Max(0, neededTools - remainingTools);
                double feasibleTools = Math.Min(d.Accessible[MaterialId.Stone] + c.Inventory.Stone, d.Accessible[MaterialId.Wood] + c.Inventory.Wood);
                if (replacement > feasibleTools || !(toolLabour < double.PositiveInfinity))
                {
                    // Unsupported replacement is not free: use primitive continuation.
                    effective = primitive; coveredFraction = 0; replacement = 0;
                    futureCapacity = Math.Max(futureTarget, Math.Min(localCapacity, c.Population * effective));
                }
                double futureManagement = target > 0 ? Math.Min(1, c.Population * effective / target) : 1;
                double futureYield = yield * Math.Pow(futureManagement, cfg.AnimalManagementExponent);
                double futureValue = Continuation(futureTarget, growth, futureCapacity, futureYield, 0, futureSizes, values, r, future);
                ladder.FutureAnnualFood[k] = future > 0 ? futureYield * AnimalYears(futureTarget, growth, futureCapacity, r, future)
                    / Math.Max(1e-12, DiscountedYears(r, future)) : 0;
                // Credit a usable residual only for stock the local forage/water can support.
                // Unsupported animals do not earn salvage by assuming they survive starvation.
                double terminal = Math.Min(localCapacity, EndAnimals(futureTarget, growth, futureCapacity, future));
                double terminalValue = HerdBuckets.ValueOf(0, terminal * grown.CarcassFood, currentSizes, values);
                // The shared future human baseline staffs recurring meals.
                // Milk cannot release extra growth-harvest work that baseline
                // never employed.
                double baselineHarvest = Math.Min(annualHuman, annualMeals + (shared ? 0 : growthFood / years));
                double unpaidLimit = futureMealGap + futureFabric + futureHousing;
                double gatherRate = cfg.GatherPerPerson * cfg.DaysPerYear / cfg.BaseDays;
                double FoodIntegral(double limit) => futureYield > 0 ? futureYield * AnimalYears(futureTarget, growth, Math.Min(futureCapacity, limit / futureYield), r, future) : 0;
                double cheapHarvest = d.Established ? Math.Min(baselineHarvest, shared ? d.FutureUpkeep * futureHarvestRate : d.TerrestrialAfterHerds / years) : 0;
                double expensiveHarvest = baselineHarvest - cheapHarvest;
                // Existing maintenance remains a minimum in the actual planner.
                // Milk frees crop-harvest work only above that floor.
                double cheapSaving = Math.Max(0, cheapHarvest - (shared ? d.FutureUpkeep : d.Upkeep) * futureHarvestRate);
                double usedLimit = unpaidLimit + expensiveHarvest + cheapSaving;
                double displacedHarvest = FoodIntegral(usedLimit) - FoodIntegral(unpaidLimit);
                double expensiveSaved = FoodIntegral(unpaidLimit + expensiveHarvest) - FoodIntegral(unpaidLimit);
                double futureSaved = gatherRate > 0 && futureHarvestRate > 0 ? displacedHarvest / futureHarvestRate
                    + expensiveSaved * (1 / gatherRate - 1 / futureHarvestRate) : 0;
                double craftYears = Continuation(futureTarget, growth, futureCapacity, futureYield, 0, futureSizes,
                    new[] { 0.0, Math.Max(0, fabric?.LabourPerUnit ?? 0), Math.Max(0, housing?.LabourPerUnit ?? 0), 0.0, 0.0 }, r, future);
                double replacementWorkerYears = replacement > 0 ? replacement * toolLabour * DiscountedYears(r, future) / Math.Max(1e-12, future) : 0;
                ladder.CurrentValue[k] = nowDiscount * currentValue;
                ladder.FutureValue[k] = nowDiscount * futureValue;
                ladder.ResidualValue[k] = terminalDiscount * terminalValue;
                ladder.ManagementYears[k] = herdersNow * DiscountedYears(r, years) + nowDiscount * AnimalYears(futureTarget, growth, Math.Min(futureCapacity, c.Population * effective), r, future) / effective;
                ladder.SavedHarvestYears[k] = savedNow * DiscountedYears(r, years) + nowDiscount * futureSaved;
                ladder.ReplacementYears[k] = nowDiscount * replacementWorkerYears;
                ladder.CraftYears[k] = nowDiscount * craftYears;
                ladder.TotalValue[k] = ladder.CurrentValue[k] + ladder.FutureValue[k] + ladder.ResidualValue[k];
                ladder.WorkerYears[k] = ladder.ManagementYears[k] + ladder.ReplacementYears[k] + ladder.CraftYears[k] - ladder.SavedHarvestYears[k];
                ladder.CurrentWorkerYears[k] = (herdersNow - savedNow) * DiscountedYears(r, years);
                if (k > 0)
                {
                    double cost = ladder.WorkerYears[k] - ladder.WorkerYears[k - 1];
                    ladder.Value[k - 1] = cost > 0 ? (ladder.TotalValue[k] - ladder.TotalValue[k - 1]) / cost : double.PositiveInfinity;
                }
            }
            return ladder;
        }

        // Caps the breeding intensity so the herd expected at the end of the turn does not exceed the target (intensity is then the only
        // lever; slaughter takes the rest). Never raises it. Deterministic bisection over the monotone expected herd.
        public static double CapBreeding(WorldState w, Camp c, double intensity, int target, double years)
        {
            var herds = w.Herds.Where(h => h.DomesticCamp == c.Id && h.Animals > 0).OrderBy(h => h.Id).ToArray();
            if (herds.Length == 0 || !(intensity > 0)) return intensity;
            // The resolver floors each herd's breeding target and carries the fraction, so the expected herd is the sum of floors.
            double Expected(double b) => herds.Sum(h => Math.Floor((h.Animals + h.GrowthRemainder) * Math.Exp(w.Config.GetSpecies(h.Species).GrowthRate * b * years) + 1e-9));
            if (Expected(intensity) <= target) return intensity;
            if (Expected(0) > target) return 0;
            double low = 0, high = intensity;
            for (int i = 0; i < 40; i++) { double mid = (low + high) / 2; if (Expected(mid) <= target) low = mid; else high = mid; }
            return low;
        }

        // Proportional split of `excess` animals over herds holding `counts` (in herd-id order): floors first, the rest to the largest
        // fractional remainders, ties to the lower index. Sums to `excess`, never exceeds a herd's count.
        public static int[] Split(int excess, int[] counts)
        {
            long total = counts.Sum(x => (long)x);
            var kills = new int[counts.Length];
            if (excess <= 0 || total <= 0) return kills;
            excess = (int)Math.Min(excess, total);
            for (int i = 0; i < counts.Length; i++) kills[i] = (int)(excess * (long)counts[i] / total);
            int remainder = excess - kills.Sum();
            foreach (int i in Enumerable.Range(0, counts.Length).OrderByDescending(i => excess * (long)counts[i] % total).ThenBy(i => i))
                if (remainder > 0 && kills[i] < counts[i]) { kills[i]++; remainder--; }
            return kills;
        }

        // Carcass food of one slaughtered animal: the species' hunting carcass value (SpeciesDefinition.FoodYield, food units per animal),
        // without the hunters' practice multiplier.
        public static double CarcassFood(WorldState w, Herd herd) => w.Config.GetSpecies(herd.Species).FoodYield;

        // The resolver's slaughter for one camp, after breeding: animals above the target leave the herds in id order, proportionally.
        // Credits the carcass food to the camp's Animal Food store as a realized flow (no ecology pressure) and records it.
        public static void Slaughter(WorldState w, Camp camp, IReadOnlyList<Herd> herds, IDictionary<int, ManagedHerdReport> reports, TurnRecord record)
        {
            if (!(w.Config.HerdLabourEnabled || w.Config.SharedCropHerdAllocationEnabled) || !camp.HerdTargetSet || herds.Count == 0) return;
            var counts = herds.Select(h => h.Animals).ToArray();
            long total = counts.Sum(x => (long)x);
            int excess = (int)Math.Max(0, Math.Min(int.MaxValue, total - Math.Max(0, camp.HerdTarget)));
            if (excess <= 0) return;
            var kills = Split(excess, counts);
            for (int i = 0; i < herds.Count; i++)
            {
                if (kills[i] <= 0) continue;
                var herd = herds[i]; double food = kills[i] * CarcassFood(w, herd);
                herd.Animals -= kills[i];
                if (herd.Animals <= 0) herd.GrowthRemainder = 0;
                camp.AnimalFood += food; camp.StepFoodIncome += food;
                record.Transfers.Add(new Transfer { Tile = herd.Tile, Entity = camp.Id, Kind = "Slaughter", Food = food });
                if (reports.TryGetValue(herd.Id, out var report)) { report.Slaughtered = kills[i]; report.SlaughterFood = food; }
            }
        }
    }
}
