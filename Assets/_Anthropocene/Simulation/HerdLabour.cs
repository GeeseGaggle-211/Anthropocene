using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    // Herd-labour coupling (Docs/HerdLabour.md). Managed herds used to grow on forage and breeding intensity alone and no animal ever
    // left them, while the planner reserved every herder the herd required before any other claim. This file holds the two missing pieces:
    //   * the valued herd ladder: one step per herder, each worth the food its animals yield (in the ExpansionValuation food buckets)
    //     per herder-year, so the planner staffs herders exactly while that value is at least the labour price w;
    //   * controlled slaughter: after breeding, animals above the camp's HerdTarget leave the herd and become carcass food.
    // Everything here is pure except Slaughter, which is the resolver's single mutation. Cost is O(herders staffed + herds) per camp.

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
    // herders first, so the first steps carry more animals). Value[k-1] is the value per herder-year of that step, nonincreasing.
    internal sealed class HerdLadder
    {
        public double[] Value = Array.Empty<double>(), Capacity = new double[1];
        // Animals the herd would reach this turn if it bred at the planned intensity and the pasture carried it (never staffed above this).
        public double GrownAnimals;
        public int Steps => Value.Length;
        // The seed herd the planner keeps managed whatever its value (SimulationConfig.MinimumHerdHerders, at most the steps there are).
        public int MinimumHerders;
        // Herders whose step is worth at least `price` (price <= 0: every step with positive value), plus the seed floor. Nonincreasing in price.
        public int Staffed(double price)
        {
            int count = 0;
            while (count < Value.Length && Value[count] > 0 && (!(price > 0) || Value[count] >= price)) count++;
            return Math.Min(Steps, Math.Max(count, MinimumHerders));
        }
        // Herd size the staffed herders can manage at full fulfilment, never above the herd the pasture grows to.
        public int Target(int herders)
        {
            double managed = Capacity[Math.Max(0, Math.Min(herders, Steps))];
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
        // The planner's herd valuation applies to automated Value-rule husbandry camps when the lever is on.
        public static bool Applies(WorldState w, Camp c)
            => w.Config.HerdLabourEnabled && w.Config.Neolithic.ValueFulfilmentSelected && CultivationEconomy.HusbandryEnabled(c) && c.Population > 0;

        // Builds the ladder after the demand pass has priced every claim. Null when the camp has no managed animals.
        public static HerdLadder Build(WorldState w, Camp c, CampProductionDemand d, double breeding)
        {
            var cfg = w.Config; var n = cfg.Neolithic;
            double years = cfg.TurnDays / cfg.DaysPerYear, intervals = cfg.GenerationIntervals;
            double animals = HerdingTooling.DomesticAnimals(w, c);
            if (!(animals > 0) || !(years > 0)) return null;
            var grown = d.GrownHerd ?? TurnResolver.ForecastGrownManagedAnimalFood(w, c, breeding, intervals, years, c.Tile);
            double grownAnimals = grown.Animals > 0 ? grown.Animals : animals;
            // Net food one managed animal adds per year: its yield less the human forage it displaces (full management; the grown herd's
            // newborns count as animals kept, which is the herd-growth credit of the Value rule).
            double yield = grown.Animals > 0 ? grown.AnimalFood / grown.Animals : d.ManagedFood.AnimalFood / animals;
            double displaced = grown.Animals > 0 ? grown.HumanFoodDisplaced / grown.Animals : d.ManagedFood.HumanFoodDisplaced / animals;
            double netPerYear = Math.Max(0, yield - displaced) / years;
            // Herder steps up to the herd the pasture grows to, never more than the residents.
            var capacity = new List<double> { 0 };
            for (int k = 1; k <= c.Population && capacity[k - 1] < grownAnimals - 1e-9; k++)
                capacity.Add(HerdingTooling.AnimalsManagedBy(w, c, years, k));
            // Buckets: consumption, the animal-fabric and light-housing feedstock the herd's food can fund, reserve refill, growth.
            TurnResolver.FoodBucketTargets(w, c, intervals, years, d.Consumers * cfg.Consumption * intervals, c.Food,
                out double consumption, out _, out double reserveFill, out double maximumGrowthFood);
            double vFood = ExpansionValuation.FoodValue(cfg, c);
            double maxRate = QoLMortality.MaximumRate(w, c);
            double foodPerBirth = ExpansionValuation.FoodPerBirth(cfg.Consumption * intervals, maxRate, years);
            double vGrowth = foodPerBirth > 0 ? Math.Max(0, ExpansionValuation.MarginalWelfare(w, c)) * ExpansionValuation.PersonLife(n) / foodPerBirth : 0;
            var sizes = new List<double> { consumption / years };
            var values = new List<double> { vFood };
            foreach (var claim in d.Claims)
            {
                if (claim.Good != CraftGood.AnimalFabric && claim.Good != CraftGood.LightHousing) continue;
                if (!claim.Valued || !(claim.Quantity > 0) || !(claim.Need > 0)) continue;
                // One animal-food unit makes one Animal Fabric (and one Light Housing from it): its value is the claim's marginal value at the present coverage.
                sizes.Add(claim.Quantity / years); values.Add(claim.FirstUnitValue * Math.Exp(-n.ValueCurveSteepness * claim.Stock / claim.Need));
            }
            sizes.Add(reserveFill / years); values.Add(n.FoodRiskWeight * vFood);
            sizes.Add(maximumGrowthFood / years); values.Add(vGrowth);
            var ladder = new HerdLadder
            {
                Capacity = capacity.ToArray(), GrownAnimals = grownAnimals,
                MinimumHerders = Math.Min(c.Population, Math.Max(0, cfg.MinimumHerdHerders))
            };
            // Human supply without any herd: the no-herd forage the camp could gather per year. Herd food also saves the labour that would harvest
            // that much food: a gatherer, or a farmer once fields are established, brings in altRate food per year.
            double perYear = cfg.DaysPerYear / cfg.BaseDays, altRate = cfg.GatherPerPerson * perYear * (d.Established ? Math.Max(1, cfg.FarmerProductivity) : 1);
            ladder.Value = HerdLadder.StepValues(ladder.Capacity, netPerYear, d.ForageSupply / years, sizes.ToArray(), values.ToArray(), sizes.Sum(), altRate);
            ladder.MinimumHerders = Math.Min(ladder.MinimumHerders, ladder.Steps);
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
            if (!w.Config.HerdLabourEnabled || !camp.HerdTargetSet || herds.Count == 0) return;
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
