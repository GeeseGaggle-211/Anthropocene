using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Anthropocene.Simulation;

// Native adaptive-log-midpoint comparator required by the v1.1 performance gate.
// It is diagnostic-only: a comparator failure cannot silently change authoritative simulation state.
internal static class DemographyBenchmark
{
    private sealed class Case { public double N, Years, Food, Clothing, Housing, DeficitYears; }
    private sealed class CoupledCase { public DemographicServicePoolInput[] Pools; public double Years; }
    private sealed class Sample { public double Milliseconds; public long Bytes; }
    private sealed class AdaptiveResult
    {
        public double Initial, Final, PersonYears, Births, Deaths, Exposure, Crowding, Disease, Starvation, Additional;
        public double FoodConsumed, ClothingConsumed, Nutrition, ClothingCoverage, HousingPersonYears, ConservationError;
        public int RateEvaluations, AcceptedSteps, RejectedSteps, ServiceIterations;
        public void Add(AdaptiveResult part)
        {
            Final = part.Final; PersonYears += part.PersonYears; Births += part.Births;
            Exposure += part.Exposure; Crowding += part.Crowding; Disease += part.Disease;
            Starvation += part.Starvation; Additional += part.Additional; HousingPersonYears += part.HousingPersonYears;
            RateEvaluations += part.RateEvaluations; AcceptedSteps += part.AcceptedSteps; RejectedSteps += part.RejectedSteps;
        }
    }

    public static void Run(int samples)
    {
        if (samples < 5) throw new ArgumentOutOfRangeException(nameof(samples), "At least five samples are needed for p50/p95 reporting.");
        var cfg = new SimulationConfig { DemographicModel = DemographicModel.PhysicalMortalityV1, QoLMortalityEnabled = false };
        cfg.Validate();
        foreach (int settlements in new[] { 80, 320, 1094 })
        {
            BenchmarkScale(cfg, settlements, samples);
            BenchmarkCoupledScale(cfg, settlements, samples);
        }
    }

    private static void BenchmarkScale(SimulationConfig cfg, int settlements, int samples)
    {
        Case[] cases = Cases(cfg, settlements);
        RunNative(cfg, cases); RunAdaptive(cfg, cases); // JIT and closure warm-up.
        var native = new Sample[samples]; var adaptive = new Sample[samples];
        for (int i = 0; i < samples; i++)
        {
            if ((i & 1) == 0)
            {
                native[i] = Measure(() => RunNative(cfg, cases));
                adaptive[i] = Measure(() => RunAdaptive(cfg, cases));
            }
            else
            {
                adaptive[i] = Measure(() => RunAdaptive(cfg, cases));
                native[i] = Measure(() => RunNative(cfg, cases));
            }
        }

        DemographicResult[] nr = RunNative(cfg, cases); AdaptiveResult[] ar = RunAdaptive(cfg, cases);
        var endpoint = new double[cases.Length]; var personYears = new double[cases.Length];
        double maxConservation = 0; long roots = 0, bands = 0, hazards = 0, quadrature = 0, service = 0;
        long midpointRates = 0, accepted = 0, rejected = 0, midpointService = 0;
        for (int i = 0; i < cases.Length; i++)
        {
            endpoint[i] = Relative(nr[i].FinalPopulation, ar[i].Final);
            personYears[i] = Relative(nr[i].PersonYears, ar[i].PersonYears);
            maxConservation = Math.Max(maxConservation, Math.Max(Math.Abs(nr[i].ConservationError), Math.Abs(ar[i].ConservationError)));
            roots += nr[i].RootEvaluations; bands += nr[i].BandsCrossed; hazards += nr[i].HazardEvaluations;
            quadrature += nr[i].QuadratureEvaluations; service += nr[i].ServiceIterations;
            midpointRates += ar[i].RateEvaluations; accepted += ar[i].AcceptedSteps; rejected += ar[i].RejectedSteps;
            midpointService += ar[i].ServiceIterations;
        }
        Array.Sort(endpoint); Array.Sort(personYears);
        if (endpoint[^1] > .0005 || personYears[^1] > .0005 || maxConservation > 1e-7)
            throw new Exception("Native demographic solver exceeded the diagnostic midpoint accuracy/conservation gate.");
        Console.WriteLine("DEMOGRAPHY_BENCHMARK settlements=" + settlements + " profile=accounting-diagnostics samples=" + samples
            + " analytic_p50_ms=" + P(native.Select(x => x.Milliseconds).ToArray(), .5).ToString("F4")
            + " analytic_p95_ms=" + P(native.Select(x => x.Milliseconds).ToArray(), .95).ToString("F4")
            + " analytic_p99_ms=" + P(native.Select(x => x.Milliseconds).ToArray(), .99).ToString("F4")
            + " midpoint_p50_ms=" + P(adaptive.Select(x => x.Milliseconds).ToArray(), .5).ToString("F4")
            + " midpoint_p95_ms=" + P(adaptive.Select(x => x.Milliseconds).ToArray(), .95).ToString("F4")
            + " midpoint_p99_ms=" + P(adaptive.Select(x => x.Milliseconds).ToArray(), .99).ToString("F4")
            + " analytic_alloc_p50=" + P(native.Select(x => (double)x.Bytes).ToArray(), .5).ToString("F0")
            + " analytic_alloc_p95=" + P(native.Select(x => (double)x.Bytes).ToArray(), .95).ToString("F0")
            + " midpoint_alloc_p50=" + P(adaptive.Select(x => (double)x.Bytes).ToArray(), .5).ToString("F0")
            + " midpoint_alloc_p95=" + P(adaptive.Select(x => (double)x.Bytes).ToArray(), .95).ToString("F0"));
        Console.WriteLine("DEMOGRAPHY_BENCHMARK_COUNTERS settlements=" + settlements
            + " rootEvaluations=" + roots + " bands=" + bands + " hazardEvaluations=" + hazards
            + " quadratureEvaluations=" + quadrature + " serviceEvaluations=" + service
            + " midpointRateEvaluations=" + midpointRates + " midpointAccepted=" + accepted
            + " midpointRejected=" + rejected + " midpointServiceEvaluations=" + midpointService
            + " cacheHits=0 cacheLookups=0 fallbacks=0 analyticFailures=0 midpointFailures=0");
        Console.WriteLine("DEMOGRAPHY_BENCHMARK_ERROR settlements=" + settlements
            + " endpointMedian=" + P(endpoint, .5).ToString("G6") + " endpointMax=" + endpoint[^1].ToString("G6")
            + " personYearsMedian=" + P(personYears, .5).ToString("G6") + " personYearsMax=" + personYears[^1].ToString("G6")
            + " maxConservation=" + maxConservation.ToString("G6"));
    }

    private static void BenchmarkCoupledScale(SimulationConfig cfg, int settlements, int samples)
    {
        CoupledCase[] cases = CoupledCases(cfg, settlements);
        RunCoupled(cfg, cases);
        var measured = new Sample[samples];
        for (int i = 0; i < samples; i++) measured[i] = Measure(() => RunCoupled(cfg, cases));
        CoupledDemographicResult[] results = RunCoupled(cfg, cases);
        long pools = 0, cohorts = 0, rates = 0, accepted = 0, rejected = 0, services = 0, scalar = 0;
        int worstCohorts = 0, worstPools = 0, worstRates = 0, worstAccepted = 0, worstRejected = 0;
        double maxConservation = 0;
        foreach (CoupledDemographicResult result in results)
        {
            pools += result.PoolCount; cohorts += result.CohortCount; rates += result.RateEvaluations;
            accepted += result.AcceptedSteps; rejected += result.RejectedSteps; services += result.ServiceEvaluations;
            if (result.UsedExactScalarReduction) scalar++;
            worstCohorts = Math.Max(worstCohorts, result.CohortCount); worstPools = Math.Max(worstPools, result.PoolCount);
            worstRates = Math.Max(worstRates, result.RateEvaluations); worstAccepted = Math.Max(worstAccepted, result.AcceptedSteps);
            worstRejected = Math.Max(worstRejected, result.RejectedSteps);
            maxConservation = Math.Max(maxConservation, Math.Abs(result.Total.ConservationError));
        }
        if (maxConservation > 1e-7)
            throw new Exception("Coupled demographic benchmark exceeded its conservation gate.");
        Console.WriteLine("DEMOGRAPHY_COUPLED_BENCHMARK settlements=" + settlements + " samples=" + samples
            + " p50_ms=" + P(measured.Select(x => x.Milliseconds).ToArray(), .5).ToString("F4")
            + " p95_ms=" + P(measured.Select(x => x.Milliseconds).ToArray(), .95).ToString("F4")
            + " p99_ms=" + P(measured.Select(x => x.Milliseconds).ToArray(), .99).ToString("F4")
            + " alloc_p50=" + P(measured.Select(x => (double)x.Bytes).ToArray(), .5).ToString("F0")
            + " alloc_p95=" + P(measured.Select(x => (double)x.Bytes).ToArray(), .95).ToString("F0"));
        Console.WriteLine("DEMOGRAPHY_COUPLED_COUNTERS settlements=" + settlements
            + " pools=" + pools + " cohorts=" + cohorts + " scalarReductions=" + scalar
            + " rateEvaluations=" + rates + " accepted=" + accepted + " rejected=" + rejected
            + " serviceEvaluations=" + services + " worstPools=" + worstPools + " worstCohorts=" + worstCohorts
            + " worstRateEvaluations=" + worstRates + " worstAccepted=" + worstAccepted
            + " worstRejected=" + worstRejected + " maxConservation=" + maxConservation.ToString("G6"));
    }

    private static Sample Measure(Action action)
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread(); var timer = Stopwatch.StartNew(); action(); timer.Stop();
        return new Sample { Milliseconds = timer.Elapsed.TotalMilliseconds, Bytes = GC.GetAllocatedBytesForCurrentThread() - before };
    }

    private static Case[] Cases(SimulationConfig cfg, int count)
    {
        double[] populations = { 6, 12, 24, 80, 250, 600, 1000, 1600, 2500, 10000, 50000, 100000 };
        double[] food = { .25, .4, .72, 1, .55, .9 }, clothing = { 0, .35, .7, 1, .5, .9 };
        double[] housing = { 0, .4, .75, 1, .6, .9 }, years = { 1, 5, 20, 50 };
        var result = new Case[count];
        for (int i = 0; i < count; i++)
        {
            double n = populations[i % populations.Length], duration = years[(i / populations.Length) % years.Length];
            double maximumY = PhysicalDemography.BirthOnlyPersonYears(n, cfg.BaseBirthRate, duration);
            double f = food[(i * 5 + 1) % food.Length], c = clothing[(i * 7 + 2) % clothing.Length];
            result[i] = new Case
            {
                N = n, Years = duration,
                Food = f * PhysicalDemography.FoodPerPersonYear(cfg) * maximumY,
                Clothing = c * PhysicalDemography.ClothingPerPersonYear(cfg) * maximumY,
                Housing = housing[(i * 11 + 3) % housing.Length] * n,
                DeficitYears = f < 1 && i % 5 == 0 ? PhysicalDemography.StarvationGraceYears(cfg) : 0
            };
        }
        return result;
    }

    private static CoupledCase[] CoupledCases(SimulationConfig cfg, int count)
    {
        Case[] scalar = Cases(cfg, count);
        var result = new CoupledCase[count];
        for (int i = 0; i < count; i++)
        {
            Case c = scalar[i];
            if (i % 4 == 0)
            {
                result[i] = new CoupledCase
                {
                    Years = c.Years,
                    Pools = new[] { new DemographicServicePoolInput(
                        new[] { new DemographicCohortInput(c.N, c.DeficitYears) }, c.Food, c.Clothing, c.Housing) }
                };
                continue;
            }
            double resident = c.N * .7, detached = c.N - resident;
            var residentMembers = i % 3 == 0
                ? new[]
                {
                    new DemographicCohortInput(resident * .75, c.DeficitYears),
                    new DemographicCohortInput(resident * .25, c.DeficitYears + cfg.Neolithic.ReferenceTurnYears)
                }
                : new[] { new DemographicCohortInput(resident, c.DeficitYears) };
            var pools = new List<DemographicServicePoolInput>
            {
                new DemographicServicePoolInput(residentMembers, c.Food * .72, c.Clothing, c.Housing),
                new DemographicServicePoolInput(new[] { new DemographicCohortInput(detached, c.DeficitYears) },
                    c.Food * .28, 0, 0)
            };
            if (i % 5 == 0)
            {
                double moved = detached * .35;
                pools[1] = new DemographicServicePoolInput(
                    new[] { new DemographicCohortInput(detached - moved, c.DeficitYears) }, c.Food * .18, 0, 0);
                pools.Add(new DemographicServicePoolInput(
                    new[] { new DemographicCohortInput(moved, c.DeficitYears + 2) }, c.Food * .10, 0, 0));
            }
            result[i] = new CoupledCase { Years = c.Years, Pools = pools.ToArray() };
        }
        return result;
    }

    private static DemographicResult[] RunNative(SimulationConfig cfg, Case[] cases)
    {
        var results = new DemographicResult[cases.Length];
        for (int i = 0; i < cases.Length; i++)
        {
            Case c = cases[i];
            results[i] = PhysicalDemography.AdvanceBudget(cfg, c.N, c.Years, c.Food, c.Clothing, c.Housing, c.DeficitYears, true);
        }
        return results;
    }

    private static CoupledDemographicResult[] RunCoupled(SimulationConfig cfg, CoupledCase[] cases)
    {
        var results = new CoupledDemographicResult[cases.Length];
        for (int i = 0; i < cases.Length; i++)
            results[i] = PhysicalDemography.AdvanceCoupledBudget(cfg, cases[i].Pools, cases[i].Years);
        return results;
    }

    private static AdaptiveResult[] RunAdaptive(SimulationConfig cfg, Case[] cases)
    {
        var results = new AdaptiveResult[cases.Length];
        for (int i = 0; i < cases.Length; i++) results[i] = AdaptiveBudget(cfg, cases[i]);
        return results;
    }

    private static AdaptiveResult AdaptiveBudget(SimulationConfig cfg, Case c)
    {
        double foodRate = PhysicalDemography.FoodPerPersonYear(cfg), clothingRate = PhysicalDemography.ClothingPerPersonYear(cfg);
        double maximumY = PhysicalDemography.BirthOnlyPersonYears(c.N, cfg.BaseBirthRate, c.Years);
        bool fullFood = c.Food + 1e-10 >= foodRate * maximumY, fullClothing = c.Clothing + 1e-10 >= clothingRate * maximumY;
        int rates = 0, accepted = 0, rejected = 0, evaluations = 0;
        AdaptiveResult Evaluate(double personYears)
        {
            double denominator = Math.Max(1e-13, personYears);
            double f = fullFood ? 1 : Numbers.Clamp(c.Food / (foodRate * denominator), 0, 1);
            double clothing = fullClothing ? 1 : Numbers.Clamp(c.Clothing / (clothingRate * denominator), 0, 1);
            AdaptiveResult value = AdaptiveFixed(cfg, c.N, c.Years, f, clothing, c.Housing, c.DeficitYears);
            rates += value.RateEvaluations; accepted += value.AcceptedSteps; rejected += value.RejectedSteps; evaluations++;
            return value;
        }

        AdaptiveResult result;
        if (fullFood && fullClothing) result = Evaluate(maximumY);
        else
        {
            double low = 0, high = Math.Max(1e-13, maximumY); result = Evaluate(high);
            for (int iteration = 0; iteration < cfg.DemographicMaxServiceIterations; iteration++)
            {
                double middle = (low + high) * .5; result = Evaluate(middle);
                double residual = result.PersonYears - middle;
                if (Math.Abs(residual) <= cfg.DemographicServiceTolerance * Math.Max(1, middle)) break;
                if (residual > 0) low = middle; else high = middle;
                if (iteration + 1 == cfg.DemographicMaxServiceIterations)
                    throw new DemographicNumericalException("Adaptive midpoint service closure did not converge.");
            }
        }
        result.FoodConsumed = Math.Min(c.Food, foodRate * result.PersonYears);
        result.ClothingConsumed = Math.Min(c.Clothing, clothingRate * result.PersonYears);
        result.Nutrition = result.PersonYears > 0 ? Numbers.Clamp(result.FoodConsumed / (foodRate * result.PersonYears), 0, 1) : 1;
        result.ClothingCoverage = result.PersonYears > 0 ? Numbers.Clamp(result.ClothingConsumed / (clothingRate * result.PersonYears), 0, 1) : 1;
        result.RateEvaluations = rates; result.AcceptedSteps = accepted; result.RejectedSteps = rejected; result.ServiceIterations = evaluations;
        return result;
    }

    private static AdaptiveResult AdaptiveFixed(SimulationConfig cfg, double population, double years,
        double food, double clothing, double housing, double deficitYears)
    {
        var total = new AdaptiveResult { Initial = population, Final = population, Nutrition = food, ClothingCoverage = clothing };
        double remaining = years, current = population;
        bool deficient = food < 1 - 1e-12;
        double unexposed = !deficient ? remaining : Math.Min(remaining, Math.Max(0, PhysicalDemography.StarvationGraceYears(cfg) - Math.Max(0, deficitYears)));
        if (unexposed > 0)
        {
            AdaptiveResult part = AdaptivePart(cfg, current, unexposed, food, clothing, housing, false);
            total.Add(part); current = part.Final; remaining -= unexposed;
        }
        if (remaining > 1e-14) total.Add(AdaptivePart(cfg, current, remaining, food, clothing, housing, deficient));
        Reconcile(total); return total;
    }

    private static AdaptiveResult AdaptivePart(SimulationConfig cfg, double population, double years,
        double food, double clothing, double housing, bool starvation)
    {
        var result = new AdaptiveResult { Initial = population, Final = population };
        if (years <= 0 || population <= 0) return result;
        double x = Math.Log(population), time = 0, step = Math.Min(1, years);
        // Tight enough to be a higher-accuracy comparator for the selected 0.05
        // fixed grid while remaining usable at mature-world settlement counts.
        const double tolerance = 2e-8;
        for (int guard = 0; time < years - 1e-14; guard++)
        {
            if (guard >= 200000) throw new DemographicNumericalException("Adaptive midpoint step budget exhausted.");
            step = Math.Min(step, years - time);
            DemographicRates r0 = PhysicalDemography.Rates(cfg, Math.Exp(x), food, clothing, housing, starvation); result.RateEvaluations++;
            double midpoint = x + .5 * step * r0.Net;
            DemographicRates rMid = PhysicalDemography.Rates(cfg, Math.Exp(midpoint), food, clothing, housing, starvation); result.RateEvaluations++;
            double one = x + step * rMid.Net;
            double q1 = x + .25 * step * r0.Net;
            DemographicRates rq1 = PhysicalDemography.Rates(cfg, Math.Exp(q1), food, clothing, housing, starvation); result.RateEvaluations++;
            double half = x + .5 * step * rq1.Net;
            DemographicRates rh = PhysicalDemography.Rates(cfg, Math.Exp(half), food, clothing, housing, starvation); result.RateEvaluations++;
            double q2 = half + .25 * step * rh.Net;
            DemographicRates rq2 = PhysicalDemography.Rates(cfg, Math.Exp(q2), food, clothing, housing, starvation); result.RateEvaluations++;
            double two = half + .5 * step * rq2.Net;
            double error = Math.Abs(two - one) / 3;
            double scale = error == 0 ? 2 : Numbers.Clamp(.9 * Math.Pow(tolerance / error, 1.0 / 3), .2, 2);
            if (error > tolerance)
            {
                result.RejectedSteps++; step *= scale;
                if (step < 1e-12) throw new DemographicNumericalException("Adaptive midpoint minimum step exhausted.");
                continue;
            }
            Accumulate(result, rq1, Math.Exp(q1), housing, step * .5);
            Accumulate(result, rq2, Math.Exp(q2), housing, step * .5);
            x = two; time += step; result.AcceptedSteps++; step *= scale;
        }
        result.Final = Math.Exp(x); return result;
    }

    private static void Accumulate(AdaptiveResult result, DemographicRates rates, double population, double housing, double duration)
    {
        double y = population * duration;
        result.PersonYears += y; result.Births += rates.Birth * y; result.Exposure += rates.Exposure * y;
        result.Crowding += rates.Crowding * y; result.Disease += rates.Disease * y;
        result.Starvation += rates.LegacyStarvation * y; result.Additional += rates.Additional * y;
        result.HousingPersonYears += Math.Min(1, Math.Max(0, housing) / population) * y;
    }

    private static void Reconcile(AdaptiveResult result)
    {
        double provisional = result.Exposure + result.Crowding + result.Disease + result.Starvation + result.Additional;
        double target = Math.Max(0, result.Births - (result.Final - result.Initial));
        if (provisional > 0)
        {
            double scale = target / provisional;
            result.Exposure *= scale; result.Crowding *= scale; result.Disease *= scale;
            result.Starvation *= scale; result.Additional *= scale;
        }
        else if (target > 1e-9) throw new DemographicNumericalException("Adaptive midpoint deaths have no physical cause.");
        result.Deaths = target; result.ConservationError = result.Final - result.Initial - result.Births + result.Deaths;
    }

    private static double Relative(double value, double reference) => Math.Abs(value - reference) / Math.Max(1e-12, Math.Abs(reference));
    private static double P(double[] values, double p)
    {
        Array.Sort(values); double index = (values.Length - 1) * p;
        int lower = (int)Math.Floor(index), upper = (int)Math.Ceiling(index);
        return lower == upper ? values[lower] : values[lower] + (values[upper] - values[lower]) * (index - lower);
    }
}
