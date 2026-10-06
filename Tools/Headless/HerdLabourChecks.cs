using System;
using System.Collections.Generic;
using System.Linq;
using Anthropocene.Simulation;

// Herd-labour coupling (Docs/HerdLabour.md): the valued herder ladder and its closed forms, the labour-price staffing of herders in the
// Value planner, controlled slaughter (count, proportional split, carcass food, ledgers), the breeding cap, the escape of a herd-trapped camp,
// old saves, save round trips and scalar/worker replay. Owns its worlds and counters (lanes run fixed checks concurrently).
internal static class HerdLabourChecks
{
    private static int checks;
    private static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception("Herd labour: " + message); }
    private static void Near(double a, double b, string message, double tolerance = 1e-9) => Check(Math.Abs(a - b) <= tolerance * Math.Max(1, Math.Abs(b)), message + " (" + a + " vs " + b + ")");
    private static void Trace(string line) { if (Environment.GetEnvironmentVariable("HERD_LABOUR_TRACE") == "1") Console.WriteLine("HERD_LABOUR " + line); }
    private static string Roles(Camp c) => string.Join(",", c.Workforce.Assignments.OrderBy(a => a.Role, StringComparer.Ordinal).Select(a => a.Role + "=" + a.Population));
    private static double Years(WorldState w) => w.Config.TurnDays / w.Config.DaysPerYear;

    // A husbandry + cultivation camp under the Value rule with local Stone, Clay and Wood and a rich pasture.
    private static WorldState Camp(int population, int animals, string species = "goat", bool mortality = false, double elasticity = 1)
    {
        var w = NeolithicChecks.Fixture();
        int count = w.Tiles.Length;
        w.Resources = new WorldResourceState { MaximumQuantity = 100000, CatalogHash = ResourceCatalog.Hash, ProfileHash = ResourceCatalog.Hash, ConfigurationHash = ResourceCatalog.Hash, Provinces = Array.Empty<GeologicalProvince>(), Province = new ushort[count], History = new ushort[count], Host = new byte[count], Marine = new byte[count], Stone = new ushort[count], Clay = new ushort[count], Wood = new ushort[count], WoodRecoveryPerYear = .05 };
        var c = w.Camps[0]; int tile = c.Tile;
        w.Resources.Stone[tile] = 1000; w.Resources.Clay[tile] = 1000; w.Resources.Wood[tile] = 1000;
        c.Inventory.HandTools = 1000; c.Population = population; c.DemographicGrowthMultiplier = 1; c.PlantFood = 3000;
        w.Config.AnnualHumanGrowthRate = new SimulationConfig().AnnualHumanGrowthRate;
        w.Config.HerdLabourEnabled = true; // the lever is opt-in (default false); these fixtures exercise it
        foreach (string tech in NeolithicTech.FirstTier) c.People.NeolithicTechnologies.Add(tech);
        c.Technology = Technology.AnimalHusbandry; c.Technologies[(int)Technology.Cultivation] = true; c.Technologies[(int)Technology.AnimalHusbandry] = true;
        c.People.Progression.Era = Era.Neolithic;
        w.Config.Neolithic.PlannerFulfilment = "Value"; w.Config.QoLMortalityEnabled = mortality; w.Config.QoLMortalityElasticity = elasticity;
        foreach (var t in w.Tiles)
        {
            t.PotentialFood = 100000; t.Freshwater = 100000;
            for (int s = 0; s < 4; s++) { t.Seasons[s].MaxPotentialFood = 100000; t.Seasons[s].MaxFreshwater = 100000; }
        }
        if (animals > 0) w.Herds.Add(new Herd { Id = w.NextId++, Tile = tile, Species = species, Animals = animals, Capacity = 1, DomesticCamp = c.Id, OwnerSociety = c.SocietyId });
        return w;
    }
    private static int Herders(EconomicIntent e) => e.Assignments.Where(a => a.Role == WorkforceRoles.Herder).Sum(a => a.Population);
    private static int Animals(WorldState w, Camp c) => w.Herds.Where(h => h.DomesticCamp == c.Id).Sum(h => h.Animals);
    private static TurnRecord Resolve(WorldState w) => new TurnResolver().Resolve(w, new Dictionary<int, List<Order>>());

    public static int Run()
    {
        checks = 0; Buckets(); Ladder(); Capital(); LandAlternatives(); GrowthCredit(); Capacity(); Split(); Breeding(); Slaughter(); Planner(); Trap(); Healthy(); Saves(); Contracts(); Replay(); CapitalReplay();
        Console.WriteLine("PASS: " + checks + " herd-labour assertions"); return checks;
    }

    private static void Buckets()
    {
        double[] size = { 3, 2 }, value = { 10, 4 };
        Near(HerdBuckets.ValueOf(0, 2, size, value), 20, "inside the first bucket");
        Near(HerdBuckets.ValueOf(2, 4, size, value), 1 * 10 + 1 * 4, "straddling two buckets");
        Near(HerdBuckets.ValueOf(4, 6, size, value), 4, "food beyond the last bucket is worth nothing");
        Near(HerdBuckets.ValueOf(7, 9, size, value), 0, "entirely beyond");
        Near(HerdBuckets.ValueOf(1, 4, new[] { 3.0, 0, 2 }, new[] { 10.0, 99, 4 }), 2 * 10 + 1 * 4, "an empty bucket takes no food");
        Near(HerdBuckets.ValueOf(0, 2, new[] { 3.0 }, new[] { -5.0 }), 0, "a negative value never counts");
    }

    private static void Ladder()
    {
        double[] cap = { 0, 2, 4, 6 };
        var v = HerdLadder.StepValues(cap, 1, 0, new[] { 3.0, 2 }, new[] { 10.0, 4 });
        Check(v.Length == 3, "one step per herder");
        Near(v[0], 20, "step 1 value"); Near(v[1], 14, "step 2 value straddles two buckets"); Near(v[2], 4, "step 3 value");
        // Supply already provided by gathering pre-fills the early buckets and lowers every step.
        var shifted = HerdLadder.StepValues(cap, 1, 3, new[] { 3.0, 2 }, new[] { 10.0, 4 });
        Near(shifted[0], 2 * 4, "supply fills the first bucket: step 1 sits in the second"); Near(shifted[1], 0, "and the rest is worth nothing");
        Check(HerdLadder.StepValues(cap, 0, 0, new[] { 3.0 }, new[] { 10.0 }).All(x => x == 0), "an animal that adds no net food has no value");
        // A later bucket worth more than an earlier one cannot raise a later step: the ladder stays nonincreasing.
        var rising = HerdLadder.StepValues(cap, 1, 0, new[] { 2.0, 2, 2 }, new[] { 1.0, 9, 9 });
        Check(rising[0] >= rising[1] && rising[1] >= rising[2], "running minimum keeps the ladder nonincreasing");
        // Labour saving: herd food the camp's demand would otherwise have to harvest is worth the gatherer-years it saves at the labour price.
        var saving = HerdLadder.StepValues(cap, 1, 0, new[] { 100.0 }, new[] { 0.0 }, 3.5, 2);
        Check(double.IsPositiveInfinity(saving[0]) && saving[1] == 0 && saving[2] == 0, "a step that saves a whole worker-year pays for itself at any price; food beyond demand saves nothing (" + string.Join(",", saving) + ")");
        var mixed = HerdLadder.StepValues(cap, 1, 0, new[] { 100.0 }, new[] { 1.0 }, 3.5, 2);
        Near(mixed[1], 2 / (1 - .75), "a partly saving step is worth staffing while w <= bucket / (1 - L)"); Near(mixed[2], 2, "beyond demand only the scarcity value remains");
        Check(double.IsPositiveInfinity(mixed[0]), "the first step stays self-financing");
        var off = HerdLadder.StepValues(cap, 1, 0, new[] { 100.0 }, new[] { 1.0 }, 0, 0);
        Check(off.SequenceEqual(HerdLadder.StepValues(cap, 1, 0, new[] { 100.0 }, new[] { 1.0 })), "no demand or worker rate switches the labour-saving term off");
        var infinite = new HerdLadder { Value = new[] { double.PositiveInfinity, 8, 2 }, Capacity = cap, GrownAnimals = 6, MinimumHerders = 0 };
        Check(infinite.Staffed(1000) == 1 && infinite.Staffed(8) == 2 && infinite.Staffed(8.01) == 1 && infinite.Staffed(2) == 3 && infinite.Staffed(0) == 3 && infinite.Staffed(double.PositiveInfinity) == 1, "self-financing steps are staffed at every price");
        var ladder = new HerdLadder { Value = v, Capacity = cap, GrownAnimals = 5, MinimumHerders = 0 };
        Check(ladder.Staffed(25) == 0 && ladder.Staffed(20) == 1 && ladder.Staffed(15) == 1 && ladder.Staffed(14) == 2 && ladder.Staffed(4.0001) == 2 && ladder.Staffed(4) == 3, "herders staffed are the steps worth at least the labour price");
        Check(ladder.Staffed(0) == 3 && ladder.Staffed(double.PositiveInfinity) == 0, "free labour staffs every step; an infinite price none");
        ladder.MinimumHerders = 1; Check(ladder.Staffed(double.PositiveInfinity) == 1 && ladder.Staffed(25) == 1 && ladder.Staffed(15) == 1, "the seed floor is always staffed");
        for (double p = 0; p < 30; p += .5) Check(ladder.Staffed(p) >= ladder.Staffed(p + .5), "staffing falls as the price rises");
        Check(ladder.Target(0) == 0 && ladder.Target(1) == 2 && ladder.Target(2) == 4 && ladder.Target(3) == 5 && ladder.Target(99) == 5, "target is the managed herd, never above the grown herd (" + ladder.Target(3) + ")");
        // Zero-valued trailing steps never staff.
        var flat = new HerdLadder { Value = new[] { 5.0, 0, 0 }, Capacity = cap, GrownAnimals = 6, MinimumHerders = 0 };
        Check(flat.Staffed(0) == 1 && flat.Staffed(1) == 1, "steps worth nothing are never staffed");
    }

    private static void Capital()
    {
        // Numerical reference only in tests: production integrates at analytic crossings.
        foreach (double growth in new[] { 0.0, .01, .03 })
        foreach (double discount in new[] { 0.0, .01, .03 })
        foreach (double cap in new[] { 10.0, 25, 100 })
        {
            double reference = 0, foodReference = 0; const int samples = 10000;
            double[] sizes = { 20.0, 35, 100 }, values = { 2.0, 8, .2 };
            for (int i = 0; i < samples; i++)
            {
                double t = (i + .5) * 100 / samples, animals = Math.Min(cap, 10 * Math.Exp(growth * t));
                reference += Math.Exp(-discount * t) * animals * 100 / samples;
                foodReference += Math.Exp(-discount * t) * HerdBuckets.ValueOf(5, 5 + animals * 4, sizes, values) * 100 / samples;
            }
            Near(HerdLabour.AnimalYears(10, growth, cap, discount, 100), reference, "bounded growth integral agrees with independent quadrature", 1e-7);
            Near(HerdLabour.Continuation(10, growth, cap, 4, 5, sizes, values, discount, 100), foodReference, "bucket crossing integral agrees with independent quadrature", 1e-6);
        }
        Near(HerdLabour.DiscountedYears(0, 100), 100, "zero-discount finite horizon");
        Near(HerdLabour.DiscountedYears(.01, 100), (1 - Math.Exp(-1)) / .01, "100-year discount factor");
        Check(HerdLabour.Continuation(2, .02, 100, 4, 0, new[] { 1000.0 }, new[] { 1.0 }, .01, 100)
            > HerdLabour.Continuation(2, 0, 100, 4, 0, new[] { 1000.0 }, new[] { 1.0 }, .01, 100), "offspring change continuation value before pasture binds");
        var portfolio = new HerdLadder { Capacity = new[] { 0.0, 2, 4 }, GrownAnimals = 4, Value = new double[2],
            TotalValue = new[] { 10.0, 9, 30 }, WorkerYears = new[] { 0.0, 1, 2 } };
        Check(portfolio.Staffed(2) == 2, "a poor first marginal step cannot poison a valuable larger choice");
        Check(portfolio.Staffed(11) == 0, "total current liquidation can win at high labour cost");
        portfolio = new HerdLadder { Capacity = portfolio.Capacity, GrownAnimals = 4, Value = new double[2], TotalValue = new[] { 0.0, 0, 0 }, WorkerYears = new[] { 0.0, -1, -.5 } };
        Check(portfolio.Staffed(100) == 1 && portfolio.Staffed(double.PositiveInfinity) == 1, "net harvest savings remain valuable at high prices");
        var random = new Random(54);
        for (int sample = 0; sample < 100; sample++)
        {
            var p = new HerdLadder { Capacity = Enumerable.Range(0, 21).Select(k => (double)k).ToArray(), GrownAnimals = 20,
                Value = new double[20], TotalValue = Enumerable.Range(0, 21).Select(k => random.NextDouble() * 100).ToArray(),
                WorkerYears = Enumerable.Range(0, 21).Select(k => random.NextDouble() * 100 - 20).ToArray(), MinimumHerders = sample % 3 };
            foreach (double price in new[] { 0.0, .05, .5, 5, 50 })
            {
                int expected = Enumerable.Range(p.MinimumHerders, 21 - p.MinimumHerders).OrderByDescending(k => p.TotalValue[k] - price * p.WorkerYears[k]).ThenBy(k => p.WorkerYears[k]).First();
                Check(p.Staffed(price) == expected, "portfolio envelope agrees with exhaustive choice for arbitrary non-concave totals");
            }
        }
        var w = Camp(100, 40); var c = w.Camps[0];
        var d = CampProductionDemand.Build(w, c, EconomicIntent.From(c)); var h = d.HerdLadder;
        Check(h.CurrentAnimalFood[1] < h.CurrentAnimalFood[h.Steps], "candidate output follows its selected management");
        var partial = TurnResolver.ForecastManagedAnimalFood(w, c, 1, w.Config.GenerationIntervals, Years(w), c.Tile);
        Near(h.CurrentAnimalFood[1], partial.AnimalFood, "one-herder candidate agrees with authoritative pure forecast");
        Near(h.SlaughterFood[1], (40 - h.Target(1)) * w.Config.GetSpecies("goat").FoodYield, "liquidation is the whole remaining tail");
        var withMilk = d.WithAnimalFood(w, c, h.CurrentAnimalFood[1]);
        var withoutMilk = d.WithAnimalFood(w, c, 0);
        Check(withMilk.AnimalEnvelopeClothing >= withoutMilk.AnimalEnvelopeClothing, "candidate typed budget follows actual animal receipts");
        Check(withMilk.PhysicalExpansion == d.PhysicalExpansion, "candidate prices reuse physical field forecasts");
        Check(h.TotalValue.All(x => !double.IsNaN(x) && !double.IsInfinity(x)) && h.WorkerYears.All(x => !double.IsNaN(x) && !double.IsInfinity(x)), "production totals remain finite");
        foreach (var tile in w.Tiles) { tile.Freshwater = 0; tile.FreshwaterSource = FreshwaterSource.None; }
        var dry = CampProductionDemand.Build(w, c, EconomicIntent.From(c));
        Check(dry.HerdLadder.FutureValue.All(x => x == 0) && dry.HerdLadder.ResidualValue.All(x => x == 0), "no drinking water earns neither sustained production nor unsupported terminal salvage");
        foreach (double duration in new[] { .001, 1.0, 20, 100 })
        {
            double first = HerdLabour.AnimalYears(5, .02, 20, .01, duration / 2);
            double next = HerdLabour.EndAnimals(5, .02, 20, duration / 2);
            double second = Math.Exp(-.01 * duration / 2) * HerdLabour.AnimalYears(next, .02, 20, .01, duration / 2);
            Near(HerdLabour.AnimalYears(5, .02, 20, .01, duration), first + second, "finite continuation composes across shorter durations without a new clock", 1e-10);
        }
    }

    private static void LandAlternatives()
    {
        var w = Camp(100, 40); var c = w.Camps[0];
        var d = CampProductionDemand.Build(w, c, EconomicIntent.From(c));
        Check(d.HerdLadder != null && d.HerdLadder.PastureCapacity > 0, "the production builder supplies bounded pasture capital");
        var forecast = d.GrownHerd;
        HerdLabour.Pasture(w, c, forecast, w.Config.GenerationIntervals, out double mobile, out double weakMobile, d);
        c.IsVillage = true;
        HerdLabour.Pasture(w, c, forecast, w.Config.GenerationIntervals, out double settled, out double weakSettled, d);
        Near(mobile, settled,
            "current herd capacity uses the same radius-one ecological envelope regardless of village label");
        Near(weakMobile, weakSettled,
            "zero-displacement capacity is not enlarged by a mobile identity");
        c.IsVillage = false;
        var tile = w.Tiles[c.Tile]; tile.Environment = EnvironmentKind.Ice;
        Check(!CultivationEconomy.Cultivable(tile), "the shared fission/field predicate excludes ice");
        var blocked = w.Map.Radius(c.Tile, 2).First(id => w.Map.Distance(c.Tile, id) == 2);
        w.Tiles[blocked].ElevationLevel = 10;
        var bands = HerdLabour.Pasture(w, c, forecast, w.Config.GenerationIntervals, out _, out _, d);
        Check(bands.All(b => b.Tile != blocked), "peaks do not become pasture");
        Check(bands.Length <= 38,
            "pasture work is bounded by local vision with at most one nonhuman and one human-food band per tile");
        var costs = HerdLabour.SavedLabour(new[] { 0.0, 2, 4 }, new[] {
            new HerdLabour.PastureBand { Tile = 1, Animals = 3, Rate = 7.2 },
            new HerdLabour.PastureBand { Tile = 2, Animals = 10, Rate = 18 } }, 7.2);
        Near(costs[0], 1 / 7.2, "weakest pasture is used first");
        Near(costs[1], .5 / 7.2 + .5 / 18, "a herder step can straddle two tile alternatives");
        Check(d.Expansion == null || d.Expansion.Tiles.Length == d.Expansion.FoodPerWorkerYear.Length
            && d.Expansion.Tiles.Length == d.Expansion.Value.Length, "field alternative metadata follows the reused priced ladder");
        var before = SaveCodec.Fingerprint(w); CampProductionDemand.Build(w, c, EconomicIntent.From(c));
        Check(SaveCodec.Fingerprint(w) == before, "pasture and capital valuation are pure");
    }

    private static void GrowthCredit()
    {
        var w = Camp(100, 40); var c = w.Camps[0];
        var valued = CampProductionDemand.Build(w, c, EconomicIntent.From(c));
        Near(valued.HerdGrowthFood, 0, "offspring valued as herd capital are not also credited to the field food gap");
        var uniform = CampProductionDemand.Build(w, c, EconomicIntent.From(c), true, false);
        Check(uniform.HerdGrowthFood > 0 && uniform.HerdLadder == null, "an explicit Uniform rule retains its growth credit even with Value in config");
        var uniformPlan = EconomicPlanner.Plan(w, c, EconomicIntent.From(c), UniformFulfilment.Instance, out _, out _);
        w.Config.HerdLabourEnabled = false;
        var legacy = CampProductionDemand.Build(w, c, EconomicIntent.From(c));
        Check(legacy.HerdGrowthFood > 0 && legacy.HerdLadder == null, "lever off retains the prior herd-growth credit");
        Check(legacy.GrownHerdFood == valued.GrownHerdFood, "removing a valuation credit leaves physical forecast receipts unchanged");
        Check(EconomicPlanner.Plan(w, c, EconomicIntent.From(c), UniformFulfilment.Instance, out _, out _).Stamp == uniformPlan.Stamp,
            "explicit Uniform planner output is unchanged by the capital lever");
        var large = Camp(23, 400); large.Camps[0].QoL = .8;
        var marginal = CampProductionDemand.Build(large, large.Camps[0], EconomicIntent.From(large.Camps[0]));
        Check(marginal.GrownHerd.ManagementFulfillment < 1, "the marginal-yield witness actually has an under-managed whole herd");
        Check(marginal.HerdLadder.CurrentAnimalFood[1] > 0 && marginal.HerdLadder.TotalValue.All(x => !double.IsNaN(x)), "under-managed herds have finite candidate-specific output and totals");
    }

    private static void Capacity()
    {
        var w = Camp(100, 0); var c = w.Camps[0]; var n = w.Config.Neolithic; double years = Years(w), a = w.Config.AnimalsPerHerder;
        Near(HerdingTooling.AnimalsManagedBy(w, c, years, 0), 0, "no herders manage nothing");
        for (int h = 1; h <= 30; h += 7) Near(HerdingTooling.AnimalsManagedBy(w, c, years, h), h * a, "without tools each herder manages AnimalsPerHerder (" + h + ")");
        c.Inventory.HerdingTools = 2.5; double coveredHerders = 2.5 * n.HerdingToolCoverageYearsPerUnit / years;
        foreach (int h in new[] { 1, 2, 3, 5, 12, 40 })
        {
            double covered = Math.Min(h, coveredHerders);
            Near(HerdingTooling.AnimalsManagedBy(w, c, years, h), covered * n.StoneHerdingToolAnimalsPerHerder + (h - covered) * a, "tool-covered herders manage more animals (" + h + ")");
            // Cross-checks: the inverse requirement and the resolver's own management capacity for a herd at least that large.
            double managed = HerdingTooling.AnimalsManagedBy(w, c, years, h);
            Check(HerdingTooling.RequiredHerdersFor(w, c, years, managed) == h, "required herders invert the managed capacity (" + h + ")");
            Near(HerdingTooling.Plan(w, c, years, h, 1000000).Capacity, managed, "management plan capacity agrees for a large herd (" + h + ")");
        }
        Check(HerdingTooling.AnimalsManagedBy(w, c, years, 41) > HerdingTooling.AnimalsManagedBy(w, c, years, 40), "capacity increases with herders");
    }

    private static void Split()
    {
        Check(HerdLabour.Split(0, new[] { 5, 5 }).Sum() == 0 && HerdLabour.Split(5, new int[0]).Length == 0, "nothing to split");
        var k = HerdLabour.Split(10, new[] { 30, 60, 10 });
        Check(k[0] == 3 && k[1] == 6 && k[2] == 1, "exact proportions");
        k = HerdLabour.Split(7, new[] { 10, 10, 10 });
        Check(k.Sum() == 7 && k[0] == 3 && k[1] == 2 && k[2] == 2, "equal remainders go to the lowest index (" + string.Join(",", k) + ")");
        k = HerdLabour.Split(2, new[] { 1, 1, 98 });
        Check(k.Sum() == 2 && k[2] == 2, "the largest remainder wins (" + string.Join(",", k) + ")");
        var rng = new Random(5);
        for (int i = 0; i < 200; i++)
        {
            var counts = Enumerable.Range(0, rng.Next(1, 6)).Select(_ => rng.Next(0, 50)).ToArray(); int total = counts.Sum(), excess = rng.Next(0, total + 5);
            var kills = HerdLabour.Split(excess, counts);
            Check(kills.Sum() == Math.Min(excess, total) && kills.Zip(counts, (x, y) => x >= 0 && x <= y).All(b => b), "split conserves and never over-kills");
            for (int j = 0; j < counts.Length && total > 0; j++) Check(Math.Abs(kills[j] - Math.Min(excess, total) * (double)counts[j] / total) < 1, "each herd gives its proportional share within one animal");
        }
        foreach (double[] carcasses in new[] { new[] { 60.0, 60, 60 }, new[] { 60.0, 80, 70 }, new[] { .7, .3, 1.1 } })
        {
            int[] counts = { 5, 7, 13 }; var liquidation = new HerdLabour.Liquidation(counts, carcasses);
            foreach (int excess in Enumerable.Range(0, 26).Reverse())
            {
                var split = HerdLabour.Split(excess, counts);
                Near(liquidation.Food(excess), split.Select((k, i) => k * carcasses[i]).Sum(), "candidate liquidation agrees with resolver proportional carcasses at every target");
            }
        }
        Check(HerdLabour.Split(1000000000, new[] { 1500000000, 1500000000 }).Sum() == 1000000000, "large counts do not overflow");
    }

    private static void Breeding()
    {
        var w = Camp(100, 200); var c = w.Camps[0]; double years = Years(w); var herd = w.Herds[0];
        double rate = w.Config.GetSpecies("goat").GrowthRate;
        Near(HerdLabour.CapBreeding(w, c, .55, 100000, years), .55, "a target above the expected herd leaves the intensity");
        Near(HerdLabour.CapBreeding(w, c, 0, 10, years), 0, "zero intensity stays zero");
        Near(HerdLabour.CapBreeding(w, c, .55, 150, years), 0, "a target below the present herd stops breeding");
        double full = 200 * Math.Exp(rate * .55 * years), target = (200 + full) / 2;
        double capped = HerdLabour.CapBreeding(w, c, .55, (int)target, years);
        Check(capped > 0 && capped < .55, "an intermediate target lowers the intensity");
        Check(Math.Floor(200 * Math.Exp(rate * capped * years) + 1e-9) <= (int)target && Math.Floor(200 * Math.Exp(rate * (capped + .005) * years) + 1e-9) >= (int)target, "the capped herd's floored size meets the target (" + capped + ")");
        herd.GrowthRemainder = .5;
        Check(HerdLabour.CapBreeding(w, c, .55, (int)target, years) <= capped + 1e-12, "a fractional remainder never raises the cap");
        double again = HerdLabour.CapBreeding(w, c, .55, (int)target, years); Near(again, HerdLabour.CapBreeding(w, c, .55, (int)target, years), "the cap is deterministic", 0);
    }

    // Slaughter fixture: three herds of different species on one camp, fully staffed, no breeding, no human growth.
    private static WorldState SlaughterWorld(int target = -1, bool set = true)
    {
        var w = Camp(100, 0); var c = w.Camps[0]; w.Config.AnnualHumanGrowthRate = 0;
        c.Workforce = new PopulationLedger(); c.Workforce.Assign(WorkforceRoles.Herder, 60, c.Population);
        c.ManagedBreedingIntensity = 0; c.ManagedBreedingPolicyInitialized = true; c.EconomyAutomationTurns = 0;
        foreach (var (species, animals) in new[] { ("goat", 60), ("antelope", 30), ("bison", 10) })
            w.Herds.Add(new Herd { Id = w.NextId++, Tile = c.Tile, Species = species, Animals = animals, Capacity = 1, DomesticCamp = c.Id, OwnerSociety = c.SocietyId });
        if (target >= 0) { c.HerdTargetSet = set; c.HerdTarget = target; }
        return w;
    }

    private static void Slaughter()
    {
        var w = SlaughterWorld(80); var c = w.Camps[0]; var before = w.Herds.ToDictionary(h => h.Id, h => h.Animals);
        double foodBefore = c.AnimalFood; var r = Resolve(w);
        var reports = r.ManagedHerds.Where(m => m.Camp == c.Id).OrderBy(m => m.Herd).ToArray();
        int slaughtered = reports.Sum(m => m.Slaughtered);
        Check(Animals(w, c) == 80 && slaughtered == 20, "the herd is cut to the target (" + Animals(w, c) + ", " + slaughtered + ")");
        var kills = reports.Select(m => m.Slaughtered).ToArray();
        Check(kills.SequenceEqual(new[] { 12, 6, 2 }), "the cut is proportional across herds in id order (" + string.Join(",", kills) + ")");
        foreach (var h in w.Herds) Check(h.Animals == before[h.Id] - reports.Single(m => m.Herd == h.Id).Slaughtered + reports.Single(m => m.Herd == h.Id).NewbornAnimals - reports.Single(m => m.Herd == h.Id).StarvationDeaths && reports.Single(m => m.Herd == h.Id).NewbornAnimals == 0, "animal ledger per herd: before - slaughtered = after (no births at intensity 0, no deaths)");
        double expectedFood = 12 * w.Config.GetSpecies("goat").FoodYield + 6 * w.Config.GetSpecies("antelope").FoodYield + 2 * w.Config.GetSpecies("bison").FoodYield;
        Near(reports.Sum(m => m.SlaughterFood), expectedFood, "carcass food is the species' carcass value per animal");
        Near(r.Transfers.Where(t => t.Kind == "Slaughter" && t.Entity == c.Id).Sum(t => t.Food), expectedFood, "one Slaughter receipt per herd credits the camp");
        var demographic = r.Demographics.Single(d => d.Camp == c.Id);
        Near(demographic.SlaughterFood, expectedFood, "the demographic report carries the carcass food"); Check(demographic.Slaughtered == 20, "and the animals");
        Check(demographic.FoodProduced >= expectedFood, "carcass food is part of food produced (the report's own balance check passed)");
        Check(c.AnimalFood + demographic.ManufacturingFood >= 0 && foodBefore == 0, "the carcass food entered the Animal Food store");
        // No ecology pressure: slaughter is a realized flow, not a claim on any tile.
        Check(r.Transfers.Where(t => t.Kind == "Slaughter").All(t => t.PotentialFood == 0 && t.Freshwater == 0), "slaughter receipts carry no ecology claim");
        // Targets at or above the herd, an unset target, and the lever off do nothing.
        foreach (var (name, world) in new[] { ("target at the herd", SlaughterWorld(100)), ("target above the herd", SlaughterWorld(500)), ("target unset", SlaughterWorld(10, false)), ("no target", SlaughterWorld()) })
        {
            var rr = Resolve(world); Check(Animals(world, world.Camps[0]) == 100 && rr.ManagedHerds.All(m => m.Slaughtered == 0) && rr.Transfers.All(t => t.Kind != "Slaughter"), name + ": no slaughter");
        }
        var off = SlaughterWorld(10); off.Config.HerdLabourEnabled = false; var ro = Resolve(off);
        Check(Animals(off, off.Camps[0]) == 100 && ro.ManagedHerds.All(m => m.Slaughtered == 0), "the lever off: no slaughter even with a target");
        // A zero target slaughters everything and leaves legitimate empty herds.
        var zero = SlaughterWorld(0); Resolve(zero); Check(Animals(zero, zero.Camps[0]) == 0 && zero.Herds.Count == 3, "a zero target empties the herds but keeps them");
        // Slaughter happens after breeding: with breeding on, newborns above the target are slaughtered too.
        var breed = SlaughterWorld(100); breed.Camps[0].ManagedBreedingIntensity = 1; var rb = Resolve(breed);
        Check(rb.ManagedHerds.Sum(m => m.NewbornAnimals) > 0 && Animals(breed, breed.Camps[0]) == 100 && rb.ManagedHerds.Sum(m => m.Slaughtered) == rb.ManagedHerds.Sum(m => m.NewbornAnimals), "newborns above the target are slaughtered after breeding");
    }

    private static void Planner()
    {
        // Slack labour: a modest herd on a big camp is fully staffed and never slaughtered.
        var w = Camp(200, 40); var c = w.Camps[0]; var plan = EconomicPlanner.Plan(w, c, EconomicIntent.From(c));
        var demand = CampProductionDemand.Build(w, c, EconomicIntent.From(c)); var ladder = demand.HerdLadder;
        Check(ladder != null && ladder.Steps >= 1, "the demand pass builds a herd ladder for an automated husbandry camp");
        Check(plan.HerdTargetSet && plan.HerdTarget >= 40, "slack labour: the herd target is at least the herd (" + plan.HerdTarget + ")");
        Check(Herders(plan) == HerdingTooling.RequiredHerders(w, c, Years(w)) || Herders(plan) == ladder.Staffed(0), "slack labour: herders are the full requirement (" + Herders(plan) + ")");
        Check(plan.HerdTarget == ladder.Target(Herders(plan)), "the target is the staffed herders' managed herd, capped by the grown herd (closed form)");
        Check(plan.ManagedBreedingIntensity <= EconomicIntent.From(c).ManagedBreedingIntensity + 1e-12, "the planner never raises breeding intensity");
        Check(EconomicIntents.Validate(EconomicIntents.Constraints(new AgentObservationBuilder().Build(w, c.Player), new AgentObservationBuilder().Build(w, c.Player).Settlements.Single(x => x.Id == c.Id)), plan) == null, "the planned intent validates");
        // Lever off, Uniform rule and no-husbandry camps keep the old unconditional reservation and set no target.
        var off = Camp(200, 40); off.Config.HerdLabourEnabled = false; var po = EconomicPlanner.Plan(off, off.Camps[0], EconomicIntent.From(off.Camps[0]));
        Check(!po.HerdTargetSet && Herders(po) == HerdingTooling.RequiredHerders(off, off.Camps[0], Years(off)), "lever off: no target, the herd's full reservation");
        var uni = Camp(200, 40); var pu = EconomicPlanner.Plan(uni, uni.Camps[0], EconomicIntent.From(uni.Camps[0]), UniformFulfilment.Instance);
        Check(!pu.HerdTargetSet && Herders(pu) == HerdingTooling.RequiredHerders(uni, uni.Camps[0], Years(uni)), "Uniform rule: no target, the herd's full reservation");
        var none = Camp(100, 0); var pn = EconomicPlanner.Plan(none, none.Camps[0], EconomicIntent.From(none.Camps[0]));
        Check(!pn.HerdTargetSet && Herders(pn) == 0, "a camp with no managed animals sets no target");
        // Scarce labour: the same herd on a small camp is staffed below its requirement and capped.
        var tight = Camp(12, 400); var tc = tight.Camps[0]; tc.QoL = .5; tc.FoodNeedsMetRatio = .8;
        var tp = EconomicPlanner.Plan(tight, tc, EconomicIntent.From(tc)); int required = HerdingTooling.RequiredHerders(tight, tc, Years(tight));
        Check(required == 12, "the 400-animal herd would need every resident (" + required + ")");
        Check(Herders(tp) < 12 && tp.HerdTargetSet && tp.HerdTarget < 400, "scarce labour: herders are staffed below the requirement and the herd is capped (" + Herders(tp) + ", " + tp.HerdTarget + ")");
        Check(tp.Assignments.Sum(a => a.Population) <= 12, "the plan stays inside the residents");
        // Determinism: the same world plans the same intent.
        var again = EconomicPlanner.Plan(tight, tc, EconomicIntent.From(tc)); Check(again.Stamp == tp.Stamp, "planning is deterministic");
    }

    private static void Trap()
    {
        // The measured trap: 23 people, 1,165 animals, QoL mortality with elasticity 1, QoL at the bottom.
        WorldState Make(bool lever)
        {
            var w = Camp(23, 1165, "goat", true, 1); var c = w.Camps[0]; c.QoL = 0; c.FoodNeedsMetRatio = 1; c.EconomyAutomationTurns = -1;
            c.Workforce = new PopulationLedger(); c.Workforce.Assign(WorkforceRoles.Herder, 23, 23);
            w.Config.HerdLabourEnabled = lever; return w;
        }
        var control = Make(false); var cc = control.Camps[0]; var treated = Make(true); var tc = treated.Camps[0];
        int controlHerders = 0, treatedHerders = 99, treatedBuilders = 0, treatedWeavers = 0; int start = Animals(treated, tc);
        for (int turn = 0; turn < 4; turn++)
        {
            Resolve(control); Resolve(treated);
            Trace("trap turn " + turn + " control pop=" + cc.Population + " animals=" + Animals(control, cc) + " QoL=" + cc.QoL.ToString("F3") + " [" + Roles(cc) + "] | treated pop=" + tc.Population + " animals=" + Animals(treated, tc) + " target=" + (tc.HerdTargetSet ? tc.HerdTarget.ToString() : "-") + " QoL=" + tc.QoL.ToString("F3") + " [" + Roles(tc) + "]");
            controlHerders = cc.Workforce.Assignments.Where(a => a.Role == WorkforceRoles.Herder).Sum(a => a.Population);
            treatedHerders = tc.Workforce.Assignments.Where(a => a.Role == WorkforceRoles.Herder).Sum(a => a.Population);
            treatedBuilders = Math.Max(treatedBuilders, tc.Workforce.Assignments.Where(a => a.Role == WorkforceRoles.Builder).Sum(a => a.Population));
            treatedWeavers = Math.Max(treatedWeavers, tc.Workforce.Assignments.Where(a => a.Role == WorkforceRoles.Weaver).Sum(a => a.Population));
        }
        Check(controlHerders == cc.Population && controlHerders >= 20, "control: the unconditional reservation keeps every resident herding (" + controlHerders + ")");
        Check(treatedHerders < controlHerders / 2, "treated: herders fall well below every resident (" + treatedHerders + " of " + tc.Population + ")");
        Check(Animals(treated, tc) < start / 2 && Animals(treated, tc) < Animals(control, cc), "treated: the herd was cut (" + Animals(treated, tc) + " vs control " + Animals(control, cc) + ")");
        Check(treatedWeavers > 0 || treatedBuilders > 0, "treated: freed residents were staffed to crafts (weavers " + treatedWeavers + ", builders " + treatedBuilders + ")");
        Check(tc.QoL > cc.QoL, "treated: QoL rises above the control's (" + tc.QoL + " vs " + cc.QoL + ")");
    }

    private static void Healthy()
    {
        // A healthy pastoral camp (about 0.4 animals per person, plenty of residents) is left alone: no slaughter and its herd still breeds.
        var w = Camp(100, 40); var c = w.Camps[0]; c.QoL = .8; c.EconomyAutomationTurns = -1; w.Config.QoLMortalityEnabled = true; w.Config.QoLMortalityElasticity = 1;
        int start = Animals(w, c); var r = Resolve(w);
        Check(r.ManagedHerds.Sum(m => m.Slaughtered) == 0, "a healthy pastoral camp slaughters nothing");
        Check(Animals(w, c) >= start, "and its herd is not shrunk (" + Animals(w, c) + " from " + start + ")");
        int herders = c.Workforce.Assignments.Where(a => a.Role == WorkforceRoles.Herder).Sum(a => a.Population);
        Trace("healthy animals=" + Animals(w, c) + " target=" + (c.HerdTargetSet ? c.HerdTarget.ToString() : "-") + " [" + Roles(c) + "] required=" + HerdingTooling.RequiredHerders(w, c, Years(w)));
        Check(herders >= 1 && herders <= .3 * c.Population, "herder share stays modest (" + herders + " of " + c.Population + ")");
        Check(c.HerdTargetSet && c.HerdTarget >= start, "the healthy camp's planned target is at least its herd (" + c.HerdTarget + ")");
    }

    private static void Saves()
    {
        // An old save (no HerdTargetSet) loads with no target, the lever off, and continues without slaughter.
        var w = SlaughterWorld(80); var c = w.Camps[0];
        var loaded = SaveCodec.Load(SaveCodec.Save(w)); var lc = loaded.Camps[0];
        Check(lc.HerdTargetSet && lc.HerdTarget == 80 && loaded.Config.HerdLabourEnabled && loaded.Config.MinimumHerdHerders == 1, "target and lever round-trip");
        Check(SaveCodec.Fingerprint(loaded) == SaveCodec.Fingerprint(w), "the round trip keeps the fingerprint");
        var a = Resolve(w); var b = Resolve(loaded);
        Check(SaveCodec.Fingerprint(w) == SaveCodec.Fingerprint(loaded) && a.ManagedHerds.Sum(m => m.Slaughtered) == b.ManagedHerds.Sum(m => m.Slaughtered) && a.ManagedHerds.Sum(m => m.Slaughtered) == 20, "a loaded world continues identically, slaughter included");
        var old = SlaughterWorld(); old.Config.HerdLabourEnabled = false; // what an old save deserializes to
        var oldLoaded = SaveCodec.Load(SaveCodec.Save(old)); var ro = Resolve(oldLoaded);
        Check(!oldLoaded.Camps[0].HerdTargetSet && !oldLoaded.Config.HerdLabourEnabled && ro.ManagedHerds.All(m => m.Slaughtered == 0) && Animals(oldLoaded, oldLoaded.Camps[0]) == 100, "an old save has no target, the lever off and no slaughter");
        var bad = SlaughterWorld(80); bad.Camps[0].HerdTarget = -1;
        try { SaveCodec.Load(SaveCodec.Save(bad)); Check(false, "a negative present herd target is rejected on load"); } catch (System.IO.InvalidDataException) { Check(true, "a negative present herd target is rejected on load"); }
        var stale = SlaughterWorld(80, false); stale.Camps[0].HerdTarget = 55; Check(!SaveCodec.Load(SaveCodec.Save(stale)).Camps[0].HerdTargetSet && SaveCodec.Load(SaveCodec.Save(stale)).Camps[0].HerdTarget == 0, "an unset target loads as zero whatever was stored");
        // The planner's target persists through apply and a save.
        var p = Camp(12, 400); var pc = p.Camps[0]; pc.QoL = .5; pc.FoodNeedsMetRatio = .8; var plan = EconomicPlanner.Plan(p, pc, EconomicIntent.From(pc));
        EconomicIntents.Apply(pc, plan); Check(pc.HerdTargetSet && pc.HerdTarget == plan.HerdTarget, "Apply stores the target on the camp");
        var pl = SaveCodec.Load(SaveCodec.Save(p)); Check(pl.Camps[0].HerdTargetSet && pl.Camps[0].HerdTarget == plan.HerdTarget && EconomicIntent.From(pl.Camps[0]).Stamp == EconomicIntent.From(pc).Stamp, "the planned target survives a save");
    }

    private static void Contracts()
    {
        var w = Camp(100, 40); var c = w.Camps[0]; var e = EconomicIntent.From(c);
        Check(!e.HerdTargetSet && e.Copy().HerdTarget == 0 && !e.Copy().HerdTargetSet, "an intent starts with no target");
        string stamp = e.Stamp; e.HerdTargetSet = true; e.HerdTarget = 7;
        Check(e.Stamp != stamp && e.Copy().HerdTargetSet && e.Copy().HerdTarget == 7 && e.Copy().Stamp == e.Stamp, "the target is part of the intent's stamp and copy");
        var constraints = EconomicIntents.Constraints(new AgentObservationBuilder().Build(w, c.Player), new AgentObservationBuilder().Build(w, c.Player).Settlements.Single(x => x.Id == c.Id));
        Check(EconomicIntents.Validate(constraints, e) == null, "a valid player target passes");
        e.HerdTarget = -1; Check(EconomicIntents.Validate(constraints, e) != null, "a negative target is refused");
        e.HerdTarget = 5; constraints.Husbandry = false; Check(EconomicIntents.Validate(constraints, e) != null, "a target without husbandry is refused");
        // The target travels through the observation as an additive field on the owner's economic intent.
        c.HerdTargetSet = true; c.HerdTarget = 33;
        var observation = new AgentObservationBuilder().Build(w, c.Player);
        var settlement = observation.Settlements.Single(s => s.Id == c.Id);
        Check(settlement.Economy.HerdTargetSet && settlement.Economy.HerdTarget == 33, "the owner's observation carries the herd target");
        var dto = ObservationDto.From(observation, c.Mandate); Check(dto.Settlements.Single(s => s.Id == c.Id).Economy.HerdTarget == 33, "and so does the serializable observation");
        Check(LegalActions.Build(observation).Single(m => m.Entity == c.Id).Economy != null && EconomicIntents.Validate(observation, settlement, settlement.Economy.Copy()) == null, "the economy action is legal and the owner's own intent validates");
        var order = new Order(OrderKind.Economy, c.Id) { Economy = EconomicIntent.From(c) }; order.Economy.HerdTarget = 5; order.Economy.HerdTargetSet = true;
        EconomicIntents.Apply(c, order.Economy); Check(c.HerdTargetSet && c.HerdTarget == 5, "a player order sets the target");
    }

    private static void Replay()
    {
        // Scalar versus reversed 4-worker replay of the trap camp over several turns: identical fingerprints and records.
        string Run(PhaseExecution execution)
        {
            var w = Camp(23, 1165, "goat", true, 1); var c = w.Camps[0]; c.QoL = 0; c.EconomyAutomationTurns = -1;
            c.Workforce = new PopulationLedger(); c.Workforce.Assign(WorkforceRoles.Herder, 23, 23);
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < 4; i++)
            {
                var r = new TurnResolver(execution).Resolve(w, new Dictionary<int, List<Order>>());
                sb.Append(SaveCodec.Fingerprint(w)).Append(r.ManagedHerds.Sum(m => m.Slaughtered)).Append(';');
            }
            return sb.ToString();
        }
        string scalar = Run(new PhaseExecution()), workers = Run(new PhaseExecution(4, 3, true, 1));
        Check(scalar == workers, "scalar and reversed 4-worker replay agree");
    }
    private static void CapitalReplay()
    {
        var cfg = SimulationConfig.Small(4);
        cfg.HerdLabourEnabled = true; cfg.QoLMortalityEnabled = true; cfg.QoLMortalityElasticity = 1; cfg.FissionEnabled = true;
        var scalar = new WorldGenerator().Generate(53, cfg);
        // Generated geography plus a controlled pastoral start: this seed's natural
        // four-tribe opening acquires no managed animals, so it cannot witness HV2.
        var pastoral = scalar.Camps[0]; pastoral.Technology = Technology.AnimalHusbandry;
        pastoral.Technologies[(int)Technology.AnimalHusbandry] = true;
        pastoral.People.Progression.Era = Era.Neolithic; pastoral.EconomyAutomationTurns = -1;
        scalar.Herds.Add(new Herd { Id = scalar.NextId++, Tile = pastoral.Tile, Species = "goat", Animals = 5,
            DomesticCamp = pastoral.Id, OwnerSociety = pastoral.SocietyId, Capacity = 1 });
        var parallel = SaveCodec.Load(SaveCodec.Save(scalar));
        var workers = new PhaseExecution(4, 3, true, 1);
        int plannedHerdTurns = 0;
        for (int turn = 0; turn < 34; turn++)
        {
            var plan = AiPlanning.Decide(scalar); new TurnResolver().Resolve(scalar, plan.Orders, plan.Traces);
            parallel.Camps.Reverse(); parallel.Herds.Reverse(); parallel.Hunters.Reverse();
            var other = AiPlanning.Decide(parallel, workers); new TurnResolver(workers).Resolve(parallel, other.Orders, other.Traces);
            Check(SaveCodec.Fingerprint(scalar) == SaveCodec.Fingerprint(parallel), "generated capital/secondary-tech/fission replay turn " + turn);
            if (scalar.Camps.Any(c => c.HerdTargetSet)) plannedHerdTurns++;
            if (turn == 15)
            {
                string before = SaveCodec.Fingerprint(parallel);
                parallel = SaveCodec.Load(SaveCodec.Save(parallel));
                Check(SaveCodec.Fingerprint(parallel) == before, "capital-plan save continuation preserves authoritative state");
            }
        }
        Check(plannedHerdTurns > 0, "generated replay actually exercised the capital planner");
        Trace("generated replay resolutions=34 capitalTurns=" + plannedHerdTurns + " camps=" + scalar.Camps.Count);
    }

}
