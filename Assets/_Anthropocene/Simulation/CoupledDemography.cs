using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    // A biological cohort retains its own physical hunger history. Cohorts in one service pool share the same
    // food, clothing and housing envelope, while every pool in a coupled call contributes to the same evolving
    // crowding/disease density. This prevents dispatch from becoming a reversible density-avoidance mechanism.
    public sealed class DemographicCohortInput
    {
        public double Population, DeficitYears;
        public bool IncludeLegacyStarvation = true;

        public DemographicCohortInput(double population, double deficitYears = 0,
            bool includeLegacyStarvation = true)
        {
            Population = population;
            DeficitYears = deficitYears;
            IncludeLegacyStarvation = includeLegacyStarvation;
        }
    }

    public sealed class DemographicServicePoolInput
    {
        public DemographicCohortInput[] Cohorts;
        public double FoodBudget, ClothingBudget, HousingCapacity;

        public DemographicServicePoolInput(IEnumerable<DemographicCohortInput> cohorts,
            double foodBudget, double clothingBudget, double housingCapacity)
        {
            Cohorts = cohorts?.ToArray() ?? throw new ArgumentNullException(nameof(cohorts));
            FoodBudget = foodBudget;
            ClothingBudget = clothingBudget;
            HousingCapacity = housingCapacity;
        }
    }

    public sealed class CoupledDemographicResult
    {
        public DemographicResult[] Pools = Array.Empty<DemographicResult>();
        public DemographicResult[][] Cohorts = Array.Empty<DemographicResult[]>();
        public DemographicResult Total = new DemographicResult();
        public int CohortCount, PoolCount, RateEvaluations, AcceptedSteps, RejectedSteps, ServiceEvaluations;
        public bool UsedExactScalarReduction;
    }

    public static partial class PhysicalDemography
    {
        // The coupled extension is an explicitly bounded numerical candidate for the v1.1 rule that detached
        // expeditions remain in their source settlement's density. It does not replace the selected analytical
        // scalar propagator: exactly homogeneous service/history schedules reduce to AdvanceBudget unchanged.
        private const double CoupledRelativeTolerance = 2e-10;
        private const int CoupledMaxAcceptedAndRejectedSteps = 200000;
        private const int CoupledStateStride = 9;
        private const int CoupledX = 0, CoupledY = 1, CoupledBirths = 2, CoupledExposure = 3;
        private const int CoupledCrowding = 4, CoupledDisease = 5, CoupledStarvation = 6;
        private const int CoupledAdditional = 7, CoupledHousingY = 8;

        private sealed class CoupledLayout
        {
            public int Pools, Members;
            public int[] PoolOf, WithinPool;
            public double[] Population, DeficitYears, PoolPopulation;
            public bool[] IncludeStarvation;
        }

        private sealed class CoupledWorkspace
        {
            public readonly double[] K1, K2, K3, K4, Temporary, Full, Half, Fine, PoolPopulation;
            public readonly bool[] StarvationActive;

            public CoupledWorkspace(int stateSize, int pools, int members)
            {
                K1 = new double[stateSize]; K2 = new double[stateSize]; K3 = new double[stateSize];
                K4 = new double[stateSize]; Temporary = new double[stateSize]; Full = new double[stateSize];
                Half = new double[stateSize]; Fine = new double[stateSize]; PoolPopulation = new double[pools];
                StarvationActive = new bool[members];
            }
        }

        public static CoupledDemographicResult AdvanceCoupledBudget(SimulationConfig config,
            IReadOnlyList<DemographicServicePoolInput> pools, double years)
        {
            CoupledLayout layout = ValidateCoupledCall(config, pools, years);
            if (TryExactScalarReduction(config, pools, layout, years, out CoupledDemographicResult reduced))
                return reduced;

            double foodRate = FoodPerPersonYear(config), clothingRate = ClothingPerPersonYear(config);
            int count = pools.Count;
            double[] poolFertility = null;
            if (SurplusFunded(config))
            {
                // Prototype: each pool's birth fraction from the scalar surplus rule at its aggregate cohort, with the
                // other pools as density offset; the coupled closure below then runs with those fixed fractions.
                poolFertility = new double[count];
                double totalPopulation = layout.PoolPopulation.Sum();
                for (int p = 0; p < count; p++)
                {
                    double population = layout.PoolPopulation[p], deficit = 0; bool include = false;
                    for (int i = 0; i < layout.Members; i++)
                        if (layout.PoolOf[i] == p) { deficit += layout.DeficitYears[i] * layout.Population[i]; include |= layout.IncludeStarvation[i]; }
                    deficit = population > 0 ? deficit / population : 0;
                    if (population > 0) SurplusBudget(config, population, years, pools[p].FoodBudget, pools[p].ClothingBudget,
                        pools[p].HousingCapacity, deficit, include, Math.Max(0, totalPopulation - population), out poolFertility[p]);
                    else poolFertility[p] = 1;
                }
            }
            var maximumY = new double[count];
            var guess = new double[count];
            var fullFood = new bool[count];
            var fullClothing = new bool[count];
            bool requiresClosure = false;
            for (int p = 0; p < count; p++)
            {
                maximumY[p] = BirthOnlyPersonYears(layout.PoolPopulation[p], config.BaseBirthRate, years);
                guess[p] = maximumY[p];
                fullFood[p] = pools[p].FoodBudget >= foodRate * maximumY[p];
                fullClothing[p] = pools[p].ClothingBudget >= clothingRate * maximumY[p];
                requiresClosure |= !fullFood[p] || !fullClothing[p];
            }

            int allRates = 0, allAccepted = 0, allRejected = 0, evaluations = 0;
            CoupledDemographicResult Evaluate(double[] personYears)
            {
                var nutrition = new double[count];
                var clothing = new double[count];
                for (int p = 0; p < count; p++)
                {
                    double denominator = Math.Max(double.Epsilon, personYears[p]);
                    nutrition[p] = fullFood[p] ? 1 : Numbers.Clamp(pools[p].FoodBudget / (foodRate * denominator), 0, 1);
                    clothing[p] = fullClothing[p] ? 1 : Numbers.Clamp(pools[p].ClothingBudget / (clothingRate * denominator), 0, 1);
                }
                CoupledDemographicResult value = AdvanceCoupledFixed(config, pools, layout, years, nutrition, clothing, poolFertility);
                allRates += value.RateEvaluations; allAccepted += value.AcceptedSteps;
                allRejected += value.RejectedSteps; evaluations++;
                return value;
            }

            CoupledDemographicResult result = Evaluate(guess);
            if (requiresClosure)
            {
                bool converged = false;
                for (int iteration = 0; iteration < config.DemographicMaxServiceIterations; iteration++)
                {
                    double largest = 0;
                    for (int p = 0; p < count; p++)
                    {
                        double model = result.Pools[p].PersonYears;
                        largest = Math.Max(largest, Math.Abs(model - guess[p]) / Math.Max(1, guess[p]));
                    }
                    if (largest <= config.DemographicServiceTolerance)
                    {
                        converged = true;
                        break;
                    }
                    if (iteration + 1 == config.DemographicMaxServiceIterations) break;
                    for (int p = 0; p < count; p++)
                        guess[p] = .5 * (guess[p] + result.Pools[p].PersonYears);
                    result = Evaluate(guess);
                }
                if (!converged)
                    throw new DemographicNumericalException("Coupled finite-service person-year closure did not converge.");
            }

            for (int p = 0; p < count; p++)
            {
                DemographicResult pool = result.Pools[p];
                pool.FoodConsumed = Math.Min(pools[p].FoodBudget, foodRate * pool.PersonYears);
                pool.ClothingConsumed = Math.Min(pools[p].ClothingBudget, clothingRate * pool.PersonYears);
                pool.Nutrition = pool.PersonYears > 0
                    ? Numbers.Clamp(pool.FoodConsumed / (foodRate * pool.PersonYears), 0, 1) : 1;
                pool.ClothingCoverage = pool.PersonYears > 0
                    ? Numbers.Clamp(pool.ClothingConsumed / (clothingRate * pool.PersonYears), 0, 1) : 1;
                for (int c = 0; c < result.Cohorts[p].Length; c++)
                {
                    DemographicResult cohort = result.Cohorts[p][c];
                    double share = pool.PersonYears > 0 ? cohort.PersonYears / pool.PersonYears : 0;
                    cohort.FoodConsumed = pool.FoodConsumed * share;
                    cohort.ClothingConsumed = pool.ClothingConsumed * share;
                    cohort.Nutrition = pool.Nutrition;
                    cohort.ClothingCoverage = pool.ClothingCoverage;
                }
            }
            result.Total = AggregateCoupled(result.Pools);
            result.RateEvaluations = allRates; result.AcceptedSteps = allAccepted;
            result.RejectedSteps = allRejected; result.ServiceEvaluations = evaluations;
            return result;
        }

        // Fixed-coverage entry point is internal so native checks can compare the joint ODE against an independent
        // high-accuracy reference without allowing a gameplay caller to bypass paid service budgets.
        internal static CoupledDemographicResult AdvanceCoupledFixed(SimulationConfig config,
            IReadOnlyList<DemographicServicePoolInput> pools, double years,
            IReadOnlyList<double> nutrition, IReadOnlyList<double> clothing)
        {
            CoupledLayout layout = ValidateCoupledCall(config, pools, years);
            return AdvanceCoupledFixed(config, pools, layout, years, nutrition, clothing);
        }

        private static CoupledDemographicResult AdvanceCoupledFixed(SimulationConfig config,
            IReadOnlyList<DemographicServicePoolInput> pools, CoupledLayout layout, double years,
            IReadOnlyList<double> nutrition, IReadOnlyList<double> clothing, IReadOnlyList<double> fertility = null)
        {
            if (nutrition == null || clothing == null || nutrition.Count != pools.Count || clothing.Count != pools.Count)
                throw new ArgumentException("Coupled service coverage must have exactly one value per pool.");
            for (int p = 0; p < pools.Count; p++)
                if (!Finite(nutrition[p]) || !Finite(clothing[p]) || nutrition[p] < 0 || nutrition[p] > 1 || clothing[p] < 0 || clothing[p] > 1)
                    throw new ArgumentException("Coupled service coverage must be finite and in [0,1].");

            var output = EmptyCoupledResult(pools, layout);
            if (years == 0 || layout.Population.Sum() <= 0) return output;

            int stateSize = layout.Members * CoupledStateStride;
            var state = new double[stateSize];
            for (int i = 0; i < layout.Members; i++)
                state[i * CoupledStateStride + CoupledX] = layout.Population[i] > 0
                    ? Math.Log(layout.Population[i]) : double.NaN;
            var work = new CoupledWorkspace(stateSize, pools.Count, layout.Members);
            double time = 0, step = Math.Min(1, years);
            int rateEvaluations = 0, accepted = 0, rejected = 0;
            double grace = StarvationGraceYears(config);
            while (time < years - 1e-14)
            {
                if (accepted + rejected >= CoupledMaxAcceptedAndRejectedSteps)
                    throw new DemographicNumericalException("Coupled demographic adaptive-step budget exhausted.");
                double boundary = years;
                for (int i = 0; i < layout.Members; i++)
                {
                    int p = layout.PoolOf[i];
                    bool deficient = nutrition[p] < 1 - 1e-12;
                    work.StarvationActive[i] = layout.IncludeStarvation[i] && deficient
                        && layout.DeficitYears[i] + time >= grace - 1e-12;
                    if (layout.IncludeStarvation[i] && deficient)
                    {
                        double eventTime = grace - layout.DeficitYears[i];
                        if (eventTime > time + 1e-12 && eventTime < boundary) boundary = eventTime;
                    }
                }
                step = Math.Min(step, Math.Min(years - time, boundary - time));
                if (!(step > 0) || !Finite(step))
                    throw new DemographicNumericalException("Coupled demographic solver selected a nonpositive step.");

                Rk4Coupled(config, pools, layout, nutrition, clothing, state, step, work.Full, work,
                    work.StarvationActive, ref rateEvaluations, fertility);
                Rk4Coupled(config, pools, layout, nutrition, clothing, state, step * .5, work.Half, work,
                    work.StarvationActive, ref rateEvaluations, fertility);
                Rk4Coupled(config, pools, layout, nutrition, clothing, work.Half, step * .5, work.Fine, work,
                    work.StarvationActive, ref rateEvaluations, fertility);

                double errorRatio = 0;
                for (int j = 0; j < stateSize; j++)
                {
                    if (double.IsNaN(state[j])) continue;
                    double error = Math.Abs(work.Fine[j] - work.Full[j]) / 15;
                    double scale = CoupledRelativeTolerance * (1 + Math.Max(Math.Abs(state[j]), Math.Abs(work.Fine[j])));
                    errorRatio = Math.Max(errorRatio, error / scale);
                }
                double factor = errorRatio <= 1e-30 ? 2.5
                    : Numbers.Clamp(.9 * Math.Pow(1 / errorRatio, .2), .2, 2.5);
                if (errorRatio > 1)
                {
                    rejected++;
                    step *= factor;
                    if (step < 1e-12)
                        throw new DemographicNumericalException("Coupled demographic minimum step exhausted.");
                    continue;
                }
                for (int j = 0; j < stateSize; j++)
                {
                    if (double.IsNaN(state[j])) continue;
                    state[j] = work.Fine[j] + (work.Fine[j] - work.Full[j]) / 15;
                    if (!Finite(state[j]))
                        throw new DemographicNumericalException("Coupled demographic state left the finite double domain.");
                }
                time += step; accepted++;
                step *= factor;
            }

            for (int i = 0; i < layout.Members; i++)
            {
                int p = layout.PoolOf[i], c = layout.WithinPool[i], offset = i * CoupledStateStride;
                DemographicResult member = output.Cohorts[p][c];
                if (layout.Population[i] <= 0) continue;
                double x = state[offset + CoupledX];
                if (x < LogSmallestPositivePopulation)
                    throw new DemographicNumericalException("Coupled continuous population underflowed the finite positive double domain.");
                member.FinalPopulation = Math.Exp(x);
                member.PersonYears = Math.Max(0, state[offset + CoupledY]);
                member.Births = Math.Max(0, state[offset + CoupledBirths]);
                member.ExposureDeaths = Math.Max(0, state[offset + CoupledExposure]);
                member.CrowdingDeaths = Math.Max(0, state[offset + CoupledCrowding]);
                member.DiseaseDeaths = Math.Max(0, state[offset + CoupledDisease]);
                member.LegacyStarvationDeaths = Math.Max(0, state[offset + CoupledStarvation]);
                member.AdditionalDeaths = Math.Max(0, state[offset + CoupledAdditional]);
                member.HousingPersonYears = Math.Max(0, state[offset + CoupledHousingY]);
                member.Nutrition = nutrition[p]; member.ClothingCoverage = clothing[p];
                member.AverageHousingCoverage = member.PersonYears > 0
                    ? Numbers.Clamp(member.HousingPersonYears / member.PersonYears, 0, 1) : 1;
                ReconcileCauses(config, member);
            }
            for (int p = 0; p < pools.Count; p++) output.Pools[p] = AggregateCoupled(output.Cohorts[p]);
            output.Total = AggregateCoupled(output.Pools);
            output.RateEvaluations = rateEvaluations; output.AcceptedSteps = accepted; output.RejectedSteps = rejected;
            return output;
        }

        private static void Rk4Coupled(SimulationConfig config, IReadOnlyList<DemographicServicePoolInput> pools,
            CoupledLayout layout, IReadOnlyList<double> nutrition, IReadOnlyList<double> clothing,
            double[] state, double step, double[] destination, CoupledWorkspace work,
            bool[] starvationActive, ref int rateEvaluations, IReadOnlyList<double> fertility = null)
        {
            CoupledDerivative(config, pools, layout, nutrition, clothing, state, work.K1,
                work.PoolPopulation, starvationActive, ref rateEvaluations, fertility);
            for (int j = 0; j < state.Length; j++) work.Temporary[j] = double.IsNaN(state[j]) ? double.NaN : state[j] + step * .5 * work.K1[j];
            CoupledDerivative(config, pools, layout, nutrition, clothing, work.Temporary, work.K2,
                work.PoolPopulation, starvationActive, ref rateEvaluations, fertility);
            for (int j = 0; j < state.Length; j++) work.Temporary[j] = double.IsNaN(state[j]) ? double.NaN : state[j] + step * .5 * work.K2[j];
            CoupledDerivative(config, pools, layout, nutrition, clothing, work.Temporary, work.K3,
                work.PoolPopulation, starvationActive, ref rateEvaluations, fertility);
            for (int j = 0; j < state.Length; j++) work.Temporary[j] = double.IsNaN(state[j]) ? double.NaN : state[j] + step * work.K3[j];
            CoupledDerivative(config, pools, layout, nutrition, clothing, work.Temporary, work.K4,
                work.PoolPopulation, starvationActive, ref rateEvaluations, fertility);
            for (int j = 0; j < state.Length; j++)
                destination[j] = double.IsNaN(state[j]) ? double.NaN
                    : state[j] + step * (work.K1[j] + 2 * work.K2[j] + 2 * work.K3[j] + work.K4[j]) / 6;
        }

        private static void CoupledDerivative(SimulationConfig config,
            IReadOnlyList<DemographicServicePoolInput> pools, CoupledLayout layout,
            IReadOnlyList<double> nutrition, IReadOnlyList<double> clothing, double[] state,
            double[] derivative, double[] poolPopulation, bool[] starvationActive, ref int rateEvaluations,
            IReadOnlyList<double> fertility = null)
        {
            Array.Clear(derivative, 0, derivative.Length);
            Array.Clear(poolPopulation, 0, poolPopulation.Length);
            double density = 0;
            for (int i = 0; i < layout.Members; i++)
            {
                if (layout.Population[i] <= 0) continue;
                double x = state[i * CoupledStateStride + CoupledX];
                if (x < LogSmallestPositivePopulation)
                    throw new DemographicNumericalException("Coupled continuous population underflowed the finite positive double domain.");
                if (x > Math.Log(double.MaxValue))
                    throw new DemographicNumericalException("Coupled log-population exceeds the finite double domain.");
                double n = Math.Exp(x);
                density += n; poolPopulation[layout.PoolOf[i]] += n;
            }
            if (!Finite(density))
                throw new DemographicNumericalException("Coupled demographic density overflowed.");
            for (int i = 0; i < layout.Members; i++)
            {
                if (layout.Population[i] <= 0) continue;
                int p = layout.PoolOf[i], offset = i * CoupledStateStride;
                double n = Math.Exp(state[offset + CoupledX]);
                double h = poolPopulation[p] > 0
                    ? Math.Min(1, pools[p].HousingCapacity / poolPopulation[p]) : 1;
                DemographicRates rates = RatesAtDensityCoverage(config, density, nutrition[p], clothing[p], h,
                    starvationActive[i], fertilityFactor: fertility != null ? fertility[p] : 1);
                derivative[offset + CoupledX] = rates.Net;
                derivative[offset + CoupledY] = n;
                derivative[offset + CoupledBirths] = rates.Birth * n;
                derivative[offset + CoupledExposure] = rates.Exposure * n;
                derivative[offset + CoupledCrowding] = rates.Crowding * n;
                derivative[offset + CoupledDisease] = rates.Disease * n;
                derivative[offset + CoupledStarvation] = rates.LegacyStarvation * n;
                derivative[offset + CoupledAdditional] = rates.Additional * n;
                derivative[offset + CoupledHousingY] = h * n;
                rateEvaluations++;
            }
        }

        private static CoupledLayout ValidateCoupledCall(SimulationConfig config,
            IReadOnlyList<DemographicServicePoolInput> pools, double years)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (pools == null || pools.Count == 0) throw new ArgumentException("At least one demographic service pool is required.");
            if (!Finite(years) || years < 0) throw new ArgumentException("Demographic duration must be finite and nonnegative.");
            if (config.DemographicMaxServiceIterations <= 0 || config.DemographicServiceTolerance <= 0)
                throw new ArgumentException("Invalid coupled demographic numerical configuration.");
            int members = 0;
            for (int p = 0; p < pools.Count; p++)
            {
                DemographicServicePoolInput pool = pools[p] ?? throw new ArgumentException("A demographic service pool is null.");
                if (pool.Cohorts == null || pool.Cohorts.Length == 0)
                    throw new ArgumentException("Every demographic service pool requires at least one cohort.");
                if (!Finite(pool.FoodBudget) || !Finite(pool.ClothingBudget) || !Finite(pool.HousingCapacity)
                    || pool.FoodBudget < 0 || pool.ClothingBudget < 0 || pool.HousingCapacity < 0)
                    throw new ArgumentException("Coupled physical service budgets must be finite and nonnegative.");
                members += pool.Cohorts.Length;
            }
            var layout = new CoupledLayout
            {
                Pools = pools.Count, Members = members, PoolOf = new int[members], WithinPool = new int[members],
                Population = new double[members], DeficitYears = new double[members],
                IncludeStarvation = new bool[members], PoolPopulation = new double[pools.Count]
            };
            int index = 0;
            for (int p = 0; p < pools.Count; p++)
                for (int c = 0; c < pools[p].Cohorts.Length; c++, index++)
                {
                    DemographicCohortInput cohort = pools[p].Cohorts[c]
                        ?? throw new ArgumentException("A demographic cohort is null.");
                    if (!Finite(cohort.Population) || !Finite(cohort.DeficitYears)
                        || cohort.Population < 0 || cohort.DeficitYears < 0)
                        throw new ArgumentException("Coupled cohort population and hunger history must be finite and nonnegative.");
                    layout.PoolOf[index] = p; layout.WithinPool[index] = c;
                    layout.Population[index] = cohort.Population; layout.DeficitYears[index] = cohort.DeficitYears;
                    layout.IncludeStarvation[index] = cohort.IncludeLegacyStarvation;
                    layout.PoolPopulation[p] += cohort.Population;
                }
            return layout;
        }

        private static bool TryExactScalarReduction(SimulationConfig config,
            IReadOnlyList<DemographicServicePoolInput> pools, CoupledLayout layout, double years,
            out CoupledDemographicResult result)
        {
            result = null;
            int firstMember = Array.FindIndex(layout.Population, n => n > 0);
            if (firstMember < 0)
            {
                result = EmptyCoupledResult(pools, layout);
                result.UsedExactScalarReduction = true;
                return true;
            }
            double deficit = layout.DeficitYears[firstMember];
            bool include = layout.IncludeStarvation[firstMember];
            for (int i = 0; i < layout.Members; i++)
                if (layout.Population[i] > 0 && (layout.DeficitYears[i] != deficit || layout.IncludeStarvation[i] != include))
                    return false;
            int firstPool = Array.FindIndex(layout.PoolPopulation, n => n > 0);
            double firstPopulation = layout.PoolPopulation[firstPool];
            DemographicServicePoolInput reference = pools[firstPool];
            for (int p = 0; p < pools.Count; p++)
            {
                double population = layout.PoolPopulation[p];
                if (population <= 0)
                {
                    if (pools[p].FoodBudget != 0 || pools[p].ClothingBudget != 0 || pools[p].HousingCapacity != 0)
                        return false;
                    continue;
                }
                // Deliberately exact: a near-proportional schedule takes the coupled path rather than invoking an
                // unproved fuzzy homogeneity shortcut.
                if (pools[p].FoodBudget * firstPopulation != reference.FoodBudget * population
                    || pools[p].ClothingBudget * firstPopulation != reference.ClothingBudget * population
                    || pools[p].HousingCapacity * firstPopulation != reference.HousingCapacity * population)
                    return false;
            }
            double totalPopulation = layout.Population.Sum();
            double totalFood = pools.Sum(p => p.FoodBudget);
            double totalClothing = pools.Sum(p => p.ClothingBudget);
            double totalHousing = pools.Sum(p => p.HousingCapacity);
            DemographicResult scalar = AdvanceBudget(config, totalPopulation, years, totalFood, totalClothing,
                totalHousing, deficit, include);
            result = EmptyCoupledResult(pools, layout);
            for (int p = 0; p < pools.Count; p++)
            {
                double poolShare = layout.PoolPopulation[p] / totalPopulation;
                // The scalar solve is shared work. Attribute its diagnostic counters to exactly one non-empty
                // service pool so production's per-pool aggregation retains them without multiplying them by the
                // number of temporary dispatch partitions.
                result.Pools[p] = ScaleCoupledResult(scalar, poolShare, p == firstPool);
                for (int c = 0; c < pools[p].Cohorts.Length; c++)
                {
                    double memberShare = pools[p].Cohorts[c].Population / totalPopulation;
                    result.Cohorts[p][c] = ScaleCoupledResult(scalar, memberShare);
                }
            }
            result.Total = scalar;
            result.UsedExactScalarReduction = true;
            result.RateEvaluations = scalar.HazardEvaluations;
            result.AcceptedSteps = scalar.BandsCrossed;
            result.ServiceEvaluations = scalar.ServiceIterations;
            return true;
        }

        private static CoupledDemographicResult EmptyCoupledResult(IReadOnlyList<DemographicServicePoolInput> pools,
            CoupledLayout layout)
        {
            var result = new CoupledDemographicResult
            {
                PoolCount = pools.Count, CohortCount = layout.Members,
                Pools = new DemographicResult[pools.Count], Cohorts = new DemographicResult[pools.Count][]
            };
            for (int p = 0; p < pools.Count; p++)
            {
                result.Cohorts[p] = new DemographicResult[pools[p].Cohorts.Length];
                for (int c = 0; c < pools[p].Cohorts.Length; c++)
                {
                    double n = pools[p].Cohorts[c].Population;
                    result.Cohorts[p][c] = new DemographicResult
                    {
                        InitialPopulation = n, FinalPopulation = n, Nutrition = 1,
                        ClothingCoverage = 1, AverageHousingCoverage = 1
                    };
                }
                result.Pools[p] = AggregateCoupled(result.Cohorts[p]);
            }
            result.Total = AggregateCoupled(result.Pools);
            return result;
        }

        private static DemographicResult AggregateCoupled(IEnumerable<DemographicResult> values)
        {
            DemographicResult[] items = values.ToArray();
            var result = new DemographicResult();
            foreach (DemographicResult value in items)
            {
                result.InitialPopulation += value.InitialPopulation; result.FinalPopulation += value.FinalPopulation;
                result.PersonYears += value.PersonYears; result.Births += value.Births; result.Deaths += value.Deaths;
                result.ExposureDeaths += value.ExposureDeaths; result.CrowdingDeaths += value.CrowdingDeaths;
                result.DiseaseDeaths += value.DiseaseDeaths; result.LegacyStarvationDeaths += value.LegacyStarvationDeaths;
                result.AdditionalDeaths += value.AdditionalDeaths; result.HousingPersonYears += value.HousingPersonYears;
                result.FoodConsumed += value.FoodConsumed; result.ClothingConsumed += value.ClothingConsumed;
                result.EquilibriumPopulation += value.EquilibriumPopulation;
                result.HasPositiveEquilibrium |= value.HasPositiveEquilibrium;
                result.DeclineToExtinction |= value.DeclineToExtinction;
                result.CauseCorrection = Math.Max(result.CauseCorrection, value.CauseCorrection);
                result.ConservationError += value.ConservationError;
                result.RootEvaluations += value.RootEvaluations;
                result.BandsCrossed += value.BandsCrossed;
                result.HazardEvaluations += value.HazardEvaluations;
                result.QuadratureEvaluations += value.QuadratureEvaluations;
                result.ServiceIterations += value.ServiceIterations;
            }
            if (result.PersonYears > 0)
            {
                result.Nutrition = items.Sum(v => v.Nutrition * v.PersonYears) / result.PersonYears;
                result.ClothingCoverage = items.Sum(v => v.ClothingCoverage * v.PersonYears) / result.PersonYears;
                result.AverageHousingCoverage = items.Sum(v => v.AverageHousingCoverage * v.PersonYears) / result.PersonYears;
            }
            else result.Nutrition = result.ClothingCoverage = result.AverageHousingCoverage = 1;
            return result;
        }

        private static DemographicResult ScaleCoupledResult(DemographicResult source, double share,
            bool includeSharedWork = false)
        {
            return new DemographicResult
            {
                InitialPopulation = source.InitialPopulation * share,
                FinalPopulation = source.FinalPopulation * share,
                PersonYears = source.PersonYears * share,
                Births = source.Births * share, Deaths = source.Deaths * share,
                ExposureDeaths = source.ExposureDeaths * share, CrowdingDeaths = source.CrowdingDeaths * share,
                DiseaseDeaths = source.DiseaseDeaths * share,
                LegacyStarvationDeaths = source.LegacyStarvationDeaths * share,
                AdditionalDeaths = source.AdditionalDeaths * share,
                Nutrition = source.Nutrition, ClothingCoverage = source.ClothingCoverage,
                AverageHousingCoverage = source.AverageHousingCoverage,
                FoodConsumed = source.FoodConsumed * share,
                ClothingConsumed = source.ClothingConsumed * share,
                EquilibriumPopulation = source.EquilibriumPopulation * share,
                HasPositiveEquilibrium = source.HasPositiveEquilibrium,
                DeclineToExtinction = source.DeclineToExtinction,
                CauseCorrection = source.CauseCorrection,
                ConservationError = source.ConservationError * share,
                HousingPersonYears = source.HousingPersonYears * share,
                RootEvaluations = includeSharedWork ? source.RootEvaluations : 0,
                BandsCrossed = includeSharedWork ? source.BandsCrossed : 0,
                HazardEvaluations = includeSharedWork ? source.HazardEvaluations : 0,
                QuadratureEvaluations = includeSharedWork ? source.QuadratureEvaluations : 0,
                ServiceIterations = includeSharedWork ? source.ServiceIterations : 0
            };
        }
    }
}
