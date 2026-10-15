using System;
using System.Linq;

namespace Anthropocene.Simulation
{
    // Save-stable model identity. Zero is the pre-v1.1 behavior so an absent field can never silently migrate a save.
    public enum DemographicModel { LegacyQoLNetGrowth = 0, PhysicalMortalityV1 = 1 }

    public sealed class DemographicNumericalException : InvalidOperationException
    {
        public DemographicNumericalException(string message) : base(message) { }
    }

    public readonly struct DemographicRates
    {
        public readonly double Birth, Exposure, Crowding, Disease, LegacyStarvation, Additional;
        public double Death => Exposure + Crowding + Disease + LegacyStarvation + Additional;
        public double Net => Birth - Death;
        public DemographicRates(double birth, double exposure, double crowding, double disease, double starvation, double additional)
        { Birth = birth; Exposure = exposure; Crowding = crowding; Disease = disease; LegacyStarvation = starvation; Additional = additional; }
    }

    // Continuous result. Integer reconciliation is deliberately a turn-boundary concern in GenerationalEconomy.cs.
    public sealed class DemographicResult
    {
        public double InitialPopulation, FinalPopulation, PersonYears;
        public double Births, Deaths, ExposureDeaths, CrowdingDeaths, DiseaseDeaths, LegacyStarvationDeaths, AdditionalDeaths;
        public double Nutrition, ClothingCoverage, AverageHousingCoverage, FoodConsumed, ClothingConsumed;
        public double EquilibriumPopulation, CauseCorrection, ConservationError;
        public bool HasPositiveEquilibrium, DeclineToExtinction;
        public int RootEvaluations, BandsCrossed, HazardEvaluations, QuadratureEvaluations, ServiceIterations;
        internal double HousingPersonYears;

        internal void Add(DemographicResult other)
        {
            FinalPopulation = other.FinalPopulation;
            PersonYears += other.PersonYears; Births += other.Births; Deaths += other.Deaths;
            ExposureDeaths += other.ExposureDeaths; CrowdingDeaths += other.CrowdingDeaths; DiseaseDeaths += other.DiseaseDeaths;
            LegacyStarvationDeaths += other.LegacyStarvationDeaths; AdditionalDeaths += other.AdditionalDeaths;
            HousingPersonYears += other.HousingPersonYears;
            RootEvaluations += other.RootEvaluations; BandsCrossed += other.BandsCrossed; HazardEvaluations += other.HazardEvaluations;
            QuadratureEvaluations += other.QuadratureEvaluations; CauseCorrection = Math.Max(CauseCorrection, other.CauseCorrection);
            HasPositiveEquilibrium = other.HasPositiveEquilibrium; EquilibriumPopulation = other.EquilibriumPopulation;
            DeclineToExtinction = other.DeclineToExtinction;
        }
    }

    // Explicit physical demography (Docs/DemographyQoLContract-v1.1.md).
    //
    // Authoritative state: integer residents plus the saved fractional gross-birth/death ledgers on Camp/Hunter. This kernel is pure.
    // Temporal abstraction: one arbitrary physical interval. Mathematical log-population bands are not simulation timesteps.
    // Scaling: O(root evaluations + crossed/refined log-population bands + fixed-order quadrature), plus a bounded local
    // person-year closure when finite food/textile budgets are supplied. It never advances ecology, production, policy or world state.
    public static class PhysicalDemography
    {
        private const double MinimumPopulation = 1e-13;
        private const double SlopeEpsilon = 1e-14;
        private static readonly double[] GaussNodes =
        {
            -.8611363115940525752, -.3399810435848562648,
             .3399810435848562648,  .8611363115940525752
        };
        private static readonly double[] GaussWeights =
        {
            .3478548451374538574, .6521451548625461426,
            .6521451548625461426, .3478548451374538574
        };

        public static bool Enabled(SimulationConfig config) => config.DemographicModel == DemographicModel.PhysicalMortalityV1;

        // Future hooks. They are explicit and inert in v1.1; no technology-name exception enters the calibrated law.
        public static double FertilityFactor(WorldState world, Camp camp) => 1;
        public static double CrowdingMitigation(WorldState world, Camp camp) => 1;
        public static double DiseaseMitigation(WorldState world, Camp camp) => 1;
        public static double AdditionalMortality(WorldState world, Camp camp) => 0;

        // Read-only settlement projection used only by migration/fission diagnostics. It consumes explicit physical
        // coverage and headcount; composite QoL is intentionally not accepted by this API.
        public static DemographicRates ProjectedSettlementRates(WorldState world, Camp camp, double population,
            double nutrition, double clothingCoverage)
            => Rates(world.Config, population, nutrition, clothingCoverage,
                NeolithicProduction.HousingCapacity(world, camp, world.Days).Total, false, 0,
                CrowdingMitigation(world, camp), DiseaseMitigation(world, camp), FertilityFactor(world, camp), AdditionalMortality(world, camp));

        public static double FoodPerPersonYear(SimulationConfig config)
            => config.Consumption * config.DaysPerYear / config.BaseDays;
        public static double ClothingPerPersonYear(SimulationConfig config)
            => config.Neolithic.FabricConsumptionPerPersonTurn / config.Neolithic.ReferenceTurnYears;
        public static double StarvationGraceYears(SimulationConfig config)
            => config.StarvationGraceTurns * config.Neolithic.ReferenceTurnYears;

        public static DemographicRates Rates(SimulationConfig config, double population, double nutrition,
            double clothingCoverage, double housingCapacity, bool legacyStarvationActive = false,
            double densityPopulationOffset = 0, double crowdingMitigation = 1, double diseaseMitigation = 1,
            double fertilityFactor = 1, double additionalMortality = 0)
        {
            if (!(population >= 0) || !Finite(population) || !(densityPopulationOffset >= 0) || !Finite(densityPopulationOffset))
                throw new DemographicNumericalException("Population and density offset must be finite and nonnegative.");
            double f = Numbers.Clamp(nutrition, 0, 1), c = Numbers.Clamp(clothingCoverage, 0, 1);
            double h = population > 0 ? Math.Min(1, Math.Max(0, housingCapacity) / population) : housingCapacity > 0 ? 1 : 0;
            double density = population + densityPopulationOffset;
            double exposure = config.ExposureMortalityRate * Math.Max(0, 1 - config.ExposureClothingReduction * c - config.ExposureHousingReduction * h);
            double crowding = config.CrowdingMortalityRateAt1000 * ExponentialExcess(density, config.CrowdingExponent) * crowdingMitigation;
            double disease = config.DiseaseMortalityRateAt1000 * ExponentialExcess(density, config.DiseaseExponent) * diseaseMitigation;
            double starvation = legacyStarvationActive && f < 1
                ? -Math.Log(Math.Max(1e-15, 1 - config.Mortality * (1 - f))) / config.Neolithic.ReferenceTurnYears : 0;
            double birth = config.BaseBirthRate * f * fertilityFactor;
            if (!Finite(birth) || !Finite(exposure) || !Finite(crowding) || !Finite(disease) || !Finite(starvation) || !Finite(additionalMortality))
                throw new DemographicNumericalException("A physical demographic hazard overflowed; no mortality ceiling was applied.");
            return new DemographicRates(birth, exposure, crowding, disease, starvation, Math.Max(0, additionalMortality));
        }

        // Fixed physical service over an arbitrary interval. Housing coverage remains physical capacity / N(t), and the
        // legacy-starvation compatibility hazard begins only at its physical grace boundary.
        public static DemographicResult AdvanceFixed(SimulationConfig config, double population, double years,
            double nutrition, double clothingCoverage, double housingCapacity, double deficitYears = 0,
            bool includeLegacyStarvation = true, double densityPopulationOffset = 0)
        {
            ValidateCall(config, population, years);
            var total = new DemographicResult
            {
                InitialPopulation = population, FinalPopulation = population,
                Nutrition = Numbers.Clamp(nutrition, 0, 1), ClothingCoverage = Numbers.Clamp(clothingCoverage, 0, 1)
            };
            if (years == 0 || population <= 0) return total;
            double remaining = years, current = population;
            bool deficient = total.Nutrition < 1 - 1e-12;
            double grace = StarvationGraceYears(config);
            double unexposed = !includeLegacyStarvation || !deficient ? remaining : Math.Min(remaining, Math.Max(0, grace - Math.Max(0, deficitYears)));
            if (unexposed > 0)
            {
                var part = AdvanceCore(config, current, unexposed, total.Nutrition, total.ClothingCoverage,
                    housingCapacity, false, densityPopulationOffset);
                total.Add(part); current = part.FinalPopulation; remaining -= unexposed;
            }
            if (remaining > 1e-14)
            {
                var part = AdvanceCore(config, current, remaining, total.Nutrition, total.ClothingCoverage,
                    housingCapacity, includeLegacyStarvation && deficient, densityPopulationOffset);
                total.Add(part);
            }
            total.FinalPopulation = Math.Max(0, total.FinalPopulation);
            total.Deaths = total.ExposureDeaths + total.CrowdingDeaths + total.DiseaseDeaths + total.LegacyStarvationDeaths + total.AdditionalDeaths;
            total.ConservationError = total.FinalPopulation - total.InitialPopulation - (total.Births - total.Deaths);
            total.AverageHousingCoverage = total.PersonYears > 0 ? Numbers.Clamp(total.HousingPersonYears / total.PersonYears, 0, 1) : 1;
            return total;
        }

        // A fixed, already-paid food/textile envelope. The local monotone root makes F and C consistent with the same
        // person-years that births and deaths create; it never invokes production, trade, ecology or a planner.
        public static DemographicResult AdvanceBudget(SimulationConfig config, double population, double years,
            double foodBudget, double clothingBudget, double housingCapacity, double deficitYears = 0,
            bool includeLegacyStarvation = true, double densityPopulationOffset = 0)
        {
            ValidateCall(config, population, years);
            foodBudget = Math.Max(0, foodBudget); clothingBudget = Math.Max(0, clothingBudget);
            if (years == 0 || population <= 0)
                return new DemographicResult { InitialPopulation = population, FinalPopulation = population, Nutrition = 1, ClothingCoverage = 1 };
            double foodRate = FoodPerPersonYear(config), clothingRate = ClothingPerPersonYear(config);
            double maximumPersonYears = BirthOnlyPersonYears(population, config.BaseBirthRate, years);
            bool fullFood = foodBudget + 1e-10 >= foodRate * maximumPersonYears;
            bool fullClothing = clothingBudget + 1e-10 >= clothingRate * maximumPersonYears;
            int allRoot = 0, allBands = 0, allHazards = 0, allQuadrature = 0, evaluations = 0;
            DemographicResult Evaluate(double personYears)
            {
                double denominator = Math.Max(MinimumPopulation, personYears);
                double food = fullFood ? 1 : Numbers.Clamp(foodBudget / (foodRate * denominator), 0, 1);
                double clothing = fullClothing ? 1 : Numbers.Clamp(clothingBudget / (clothingRate * denominator), 0, 1);
                var value = AdvanceFixed(config, population, years, food, clothing, housingCapacity,
                    deficitYears, includeLegacyStarvation, densityPopulationOffset);
                evaluations++;
                allRoot += value.RootEvaluations; allBands += value.BandsCrossed; allHazards += value.HazardEvaluations; allQuadrature += value.QuadratureEvaluations;
                return value;
            }
            DemographicResult result;
            if (fullFood && fullClothing) result = Evaluate(maximumPersonYears);
            else
            {
                double low = 0, high = Math.Max(MinimumPopulation, maximumPersonYears);
                result = Evaluate(high);
                if (result.PersonYears > high * (1 + 1e-10))
                    throw new DemographicNumericalException("Finite-service closure exceeded the birth-only person-year bracket.");
                for (int iteration = 0; iteration < config.DemographicMaxServiceIterations; iteration++)
                {
                    double middle = (low + high) * .5;
                    result = Evaluate(middle);
                    double residual = result.PersonYears - middle;
                    if (Math.Abs(residual) <= config.DemographicServiceTolerance * Math.Max(1, middle)) break;
                    if (residual > 0) low = middle; else high = middle;
                    if (iteration + 1 == config.DemographicMaxServiceIterations)
                        throw new DemographicNumericalException("Finite food/textile person-year closure did not converge.");
                }
            }
            result.FoodConsumed = Math.Min(foodBudget, foodRate * result.PersonYears);
            result.ClothingConsumed = Math.Min(clothingBudget, clothingRate * result.PersonYears);
            result.Nutrition = result.PersonYears > 0 ? Numbers.Clamp(result.FoodConsumed / (foodRate * result.PersonYears), 0, 1) : 1;
            result.ClothingCoverage = result.PersonYears > 0 ? Numbers.Clamp(result.ClothingConsumed / (clothingRate * result.PersonYears), 0, 1) : 1;
            result.RootEvaluations = allRoot; result.BandsCrossed = allBands; result.HazardEvaluations = allHazards; result.QuadratureEvaluations = allQuadrature;
            result.ServiceIterations = evaluations;
            return result;
        }

        public static double BirthOnlyPersonYears(double population, double annualBirthRate, double years)
        {
            if (population <= 0 || years <= 0) return 0;
            if (Math.Abs(annualBirthRate) < 1e-14) return population * years;
            double value = population * Expm1(annualBirthRate * years) / annualBirthRate;
            if (!Finite(value)) throw new DemographicNumericalException("Birth-only person-year bracket overflowed.");
            return value;
        }

        private static DemographicResult AdvanceCore(SimulationConfig config, double population, double years,
            double nutrition, double clothing, double housingCapacity, bool starvationActive, double densityOffset)
        {
            var result = new DemographicResult { InitialPopulation = population, FinalPopulation = population, Nutrition = nutrition, ClothingCoverage = clothing };
            if (years <= 0 || population <= 0) return result;
            Func<double, DemographicRates> ratesAtPopulation = n => Rates(config, n, nutrition, clothing, housingCapacity, starvationActive, densityOffset);
            int rootEvaluations = 0;
            double RateAtLog(double x)
            {
                rootEvaluations++;
                if (x > Math.Log(double.MaxValue)) throw new DemographicNumericalException("Log-population exceeds the finite double domain.");
                double n = x < -745 ? 0 : Math.Exp(x);
                return ratesAtPopulation(n).Net;
            }

            double equilibrium = 0, rootX = double.NaN;
            double lowLimit = ratesAtPopulation(0).Net; rootEvaluations++;
            if (lowLimit > 0)
            {
                double lo = Math.Log(MinimumPopulation), hi = Math.Log(Math.Max(1, population));
                double rhi = RateAtLog(hi);
                int bracket = 0;
                while (rhi > 0 && bracket++ < config.DemographicMaxRootIterations)
                {
                    hi += Math.Log(2);
                    rhi = RateAtLog(hi);
                }
                if (rhi <= 0)
                {
                    bool converged = false;
                    for (int i = 0; i < config.DemographicMaxRootIterations; i++)
                    {
                        double mid = (lo + hi) * .5, rm = RateAtLog(mid);
                        if (Math.Abs(rm) <= config.DemographicRootTolerance || hi - lo <= 1e-13)
                        {
                            lo = hi = mid; converged = true; break;
                        }
                        if (rm > 0) lo = mid; else hi = mid;
                    }
                    if (!converged)
                        throw new DemographicNumericalException("Positive demographic equilibrium solve did not converge.");
                    rootX = (lo + hi) * .5; equilibrium = Math.Exp(rootX);
                    result.HasPositiveEquilibrium = true; result.EquilibriumPopulation = equilibrium;
                }
                else throw new DemographicNumericalException("Unable to bracket the positive demographic equilibrium.");
            }
            else result.DeclineToExtinction = true;

            double x = Math.Log(Math.Max(MinimumPopulation, population)), remaining = years;
            int segments = 0;
            while (remaining > 1e-13)
            {
                if (++segments > config.DemographicMaxBands)
                    throw new DemographicNumericalException("Demographic log-band budget exhausted before the interval completed.");
                double actualRate = RateAtLog(x);
                if (Math.Abs(actualRate) <= config.DemographicRootTolerance)
                {
                    IntegrateSegment(result, ratesAtPopulation, housingCapacity, x, 0, 0, remaining);
                    remaining = 0; break;
                }
                int direction = actualRate > 0 ? 1 : -1;
                Band(config.DemographicLogBandWidth, x, direction, rootX,
                    housingCapacity > 0 ? Math.Log(housingCapacity) : double.NaN, out double left, out double right);
                double rLeft = RateAtLog(left), rRight = RateAtLog(right);
                double m = (rRight - rLeft) / (right - left);
                if (m > 1e-12) throw new DemographicNumericalException("Physical net growth was not monotone over a demographic band.");
                if (m > 0) m = 0;
                double r0 = rLeft + m * (x - left);
                if (Math.Abs(r0) <= config.DemographicRootTolerance)
                {
                    IntegrateSegment(result, ratesAtPopulation, housingCapacity, x, 0, 0, remaining);
                    remaining = 0; break;
                }
                double edge = direction > 0 ? right : left;
                double edgeRate = rLeft + m * (edge - left);
                bool retainsSign = direction > 0 ? edgeRate >= -config.DemographicRootTolerance : edgeRate <= config.DemographicRootTolerance;
                double crossing = retainsSign ? CrossingTime(x, edge, r0, m) : double.PositiveInfinity;
                bool cross = crossing > 1e-13 && Finite(crossing) && crossing < remaining - 1e-13;
                double h = cross ? crossing : remaining;
                IntegrateSegment(result, ratesAtPopulation, housingCapacity, x, r0, m, h);
                if (cross)
                {
                    x = edge; remaining -= h; result.BandsCrossed++;
                }
                else
                {
                    x = LocalLogPopulation(x, r0, m, h); remaining = 0;
                }
            }
            if (x < Math.Log(double.Epsilon))
                throw new DemographicNumericalException("Continuous population underflowed the finite positive double domain.");
            result.FinalPopulation = Math.Exp(x);
            result.RootEvaluations = rootEvaluations;
            ReconcileCauses(config, result);
            return result;
        }

        private static void IntegrateSegment(DemographicResult result, Func<double, DemographicRates> rates,
            double housingCapacity, double startX, double startRate, double slope, double duration)
        {
            if (duration <= 0) return;
            for (int i = 0; i < 4; i++)
            {
                double t = duration * (GaussNodes[i] + 1) * .5;
                double x = startRate == 0 ? startX : LocalLogPopulation(startX, startRate, slope, t);
                double n = Math.Exp(x), weight = duration * GaussWeights[i] * .5;
                var r = rates(n);
                result.PersonYears += n * weight;
                result.Births += r.Birth * n * weight;
                result.ExposureDeaths += r.Exposure * n * weight;
                result.CrowdingDeaths += r.Crowding * n * weight;
                result.DiseaseDeaths += r.Disease * n * weight;
                result.LegacyStarvationDeaths += r.LegacyStarvation * n * weight;
                result.AdditionalDeaths += r.Additional * n * weight;
                result.HousingPersonYears += (n > 0 ? Math.Min(1, Math.Max(0, housingCapacity) / n) : 1) * n * weight;
                result.HazardEvaluations++; result.QuadratureEvaluations++;
            }
        }

        private static void ReconcileCauses(SimulationConfig config, DemographicResult result)
        {
            double provisional = result.ExposureDeaths + result.CrowdingDeaths + result.DiseaseDeaths + result.LegacyStarvationDeaths + result.AdditionalDeaths;
            double target = result.Births - (result.FinalPopulation - result.InitialPopulation);
            if (target < -1e-9 * Math.Max(1, result.InitialPopulation))
                throw new DemographicNumericalException("Piecewise demographic accounting produced negative total deaths.");
            target = Math.Max(0, target);
            double correction = Math.Abs(target - provisional) / Math.Max(1, result.InitialPopulation);
            result.CauseCorrection = correction;
            if (correction > config.DemographicCauseCorrectionTolerance)
                throw new DemographicNumericalException("Cause-specific death reconciliation exceeded its declared tolerance: " + correction.ToString("G17", System.Globalization.CultureInfo.InvariantCulture));
            if (provisional > 0)
            {
                double scale = target / provisional;
                result.ExposureDeaths *= scale; result.CrowdingDeaths *= scale; result.DiseaseDeaths *= scale;
                result.LegacyStarvationDeaths *= scale; result.AdditionalDeaths *= scale;
            }
            else if (target > 1e-10) throw new DemographicNumericalException("Demographic deaths have no physical cause to reconcile.");
            result.Deaths = target;
            result.ConservationError = result.FinalPopulation - result.InitialPopulation - (result.Births - result.Deaths);
        }

        private static void Band(double width, double x, int direction, double root, double housing,
            out double left, out double right)
        {
            double q = x / width, nearest = Math.Round(q);
            bool onGrid = Math.Abs(q - nearest) <= 2e-12;
            double index = onGrid ? (direction > 0 ? nearest : nearest - 1) : Math.Floor(q);
            left = index * width; right = (index + 1) * width;
            InsertKnot(root, x, direction, ref left, ref right);
            InsertKnot(housing, x, direction, ref left, ref right);
            if (!(right > left) || x < left - 1e-11 || x > right + 1e-11)
                throw new DemographicNumericalException("Unable to select a canonical demographic log band.");
        }

        private static void InsertKnot(double knot, double x, int direction, ref double left, ref double right)
        {
            if (!Finite(knot) || knot <= left || knot >= right) return;
            double epsilon = 2e-12;
            if (knot < x - epsilon) left = Math.Max(left, knot);
            else if (knot > x + epsilon) right = Math.Min(right, knot);
            else if (direction > 0) left = knot;
            else right = knot;
        }

        private static double CrossingTime(double x, double edge, double rate, double slope)
        {
            if (Math.Abs(slope) < SlopeEpsilon) return (edge - x) / rate;
            double z = slope * (edge - x) / rate;
            if (!(z > -1)) return double.PositiveInfinity;
            return Log1p(z) / slope;
        }

        private static double LocalLogPopulation(double x, double rate, double slope, double time)
        {
            if (time == 0 || rate == 0) return x;
            if (Math.Abs(slope) < SlopeEpsilon) return x + rate * time;
            return x + rate * Expm1(slope * time) / slope;
        }

        private static double ExponentialExcess(double population, double k)
        {
            double exponent = k * population;
            if (exponent > 709) throw new DemographicNumericalException("Population-linked mortality exceeded the finite exponential domain.");
            return Expm1(exponent) / Expm1(1000 * k);
        }

        internal static double Expm1(double x)
        {
            if (Math.Abs(x) > 1e-5) return Math.Exp(x) - 1;
            double term = x, sum = x;
            for (int n = 2; n <= 8; n++) { term *= x / n; sum += term; }
            return sum;
        }

        internal static double Log1p(double x)
        {
            if (Math.Abs(x) > 1e-4) return Math.Log(1 + x);
            double term = x, sum = 0;
            for (int n = 1; n <= 12; n++) { if (n > 1) term *= -x; sum += term / n; }
            return sum;
        }

        private static void ValidateCall(SimulationConfig config, double population, double years)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (!Finite(population) || !Finite(years) || population < 0 || years < 0)
                throw new ArgumentException("Demographic population and duration must be finite and nonnegative.");
            if (config.DemographicLogBandWidth <= 0 || config.DemographicMaxBands <= 0)
                throw new ArgumentException("Invalid demographic numerical configuration.");
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    // The only supported legacy-save transition. Callers must opt in and provide provenance; deserialization itself
    // always leaves an absent DemographicModel on LegacyQoLNetGrowth.
    public static class DemographicMigration
    {
        public static DemographicMigrationRecord ToPhysicalV1(WorldState world, string reason, string sourceCommit)
        {
            if (world == null || world.Config == null) throw new ArgumentNullException(nameof(world));
            if (world.Config.DemographicModel != DemographicModel.LegacyQoLNetGrowth)
                throw new InvalidOperationException("Only a legacy demographic world can migrate to PhysicalMortalityV1.");
            if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A demographic migration reason is required.", nameof(reason));
            if (string.IsNullOrWhiteSpace(sourceCommit)) throw new ArgumentException("The source simulator commit is required.", nameof(sourceCommit));
            double referenceYears = world.Config.Neolithic.ReferenceTurnYears;
            foreach (var camp in world.Camps)
                camp.FoodDeficitYears = Math.Max(camp.FoodDeficitYears, Math.Max(0, camp.DeficitTurns) * referenceYears);
            foreach (var hunter in world.Hunters)
                hunter.FoodDeficitYears = Math.Max(hunter.FoodDeficitYears, Math.Max(0, hunter.DeficitTurns) * referenceYears);
            var record = new DemographicMigrationRecord
            {
                Turn = world.Turn, Days = world.Days, From = DemographicModel.LegacyQoLNetGrowth,
                To = DemographicModel.PhysicalMortalityV1, Reason = reason.Trim(), SourceCommit = sourceCommit.Trim(), Camps = world.Camps.Count,
                Hunters = world.Hunters.Count, Population = world.Camps.Sum(world.LinkedPopulation)
            };
            world.DemographicMigrations ??= new System.Collections.Generic.List<DemographicMigrationRecord>();
            world.DemographicMigrations.Add(record);
            world.Config.DemographicModel = DemographicModel.PhysicalMortalityV1;
            world.Config.QoLMortalityEnabled = false;
            return record;
        }
    }
}
