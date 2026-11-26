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
        public double Nutrition, ClothingCoverage, AverageHousingCoverage, AverageSanitationCoverage, FoodConsumed, ClothingConsumed;
        public double EquilibriumPopulation, CauseCorrection, ConservationError;
        public bool HasPositiveEquilibrium, DeclineToExtinction;
        public int RootEvaluations, BandsCrossed, HazardEvaluations, QuadratureEvaluations, ServiceIterations;
        internal double HousingPersonYears, SanitationPersonYears;

        internal void Add(DemographicResult other)
        {
            FinalPopulation = other.FinalPopulation;
            PersonYears += other.PersonYears; Births += other.Births; Deaths += other.Deaths;
            ExposureDeaths += other.ExposureDeaths; CrowdingDeaths += other.CrowdingDeaths; DiseaseDeaths += other.DiseaseDeaths;
            LegacyStarvationDeaths += other.LegacyStarvationDeaths; AdditionalDeaths += other.AdditionalDeaths;
            HousingPersonYears += other.HousingPersonYears; SanitationPersonYears += other.SanitationPersonYears;
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
    public static partial class PhysicalDemography
    {
        private const double SlopeEpsilon = 1e-14;
        private static readonly double LogSmallestPositivePopulation = Math.Log(double.Epsilon);
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

        internal static double SanitationDiseaseReduction(SimulationConfig config)
            => config?.Agriculture != null && config.Agriculture.SanitationEnabled
                ? Numbers.Clamp(config.Agriculture.SanitationDiseaseReduction, 0, 1) : 0;

        // Future hooks. They are explicit and inert in v1.1 except DiseaseMitigation when physical Sanitation is installed.
        public static double FertilityFactor(WorldState world, Camp camp) => 1;
        public static double CrowdingMitigation(WorldState world, Camp camp) => 1;
        public static double DiseaseMitigation(WorldState world, Camp camp)
            => DiseaseMitigation(world, camp, camp?.Population ?? 0);
        public static double DiseaseMitigation(WorldState world, Camp camp, double population)
        {
            if (world == null || camp == null) return 1;
            double alpha = SanitationDiseaseReduction(world.Config);
            if (!(alpha > 0)) return 1;
            double capacity = NeolithicProduction.EffectiveSanitationCapacity(world, camp, world.Days);
            if (!(capacity > 0)) return 1;
            double s = population > 0 ? Math.Min(1, capacity / population) : 1;
            return Math.Max(0, 1 - alpha * s);
        }
        public static double AdditionalMortality(WorldState world, Camp camp) => 0;

        // Read-only settlement projection used only by migration/fission diagnostics. It consumes explicit physical
        // coverage and headcount; composite QoL is intentionally not accepted by this API.
        public static DemographicRates ProjectedSettlementRates(WorldState world, Camp camp, double population,
            double nutrition, double clothingCoverage, double? deficitYears = null)
        {
            double deficit = Math.Max(0, deficitYears ?? camp.FoodDeficitYears);
            bool starvationActive = nutrition < 1 - 1e-12 && deficit >= StarvationGraceYears(world.Config) - 1e-12;
            return Rates(world.Config, population, nutrition, clothingCoverage,
                NeolithicProduction.HousingCapacity(world, camp, world.Days).Total, starvationActive, 0,
                CrowdingMitigation(world, camp), DiseaseMitigation(world, camp, population), FertilityFactor(world, camp), AdditionalMortality(world, camp));
        }

        public static double FoodPerPersonYear(SimulationConfig config)
            => config.Consumption * config.DaysPerYear / config.BaseDays;
        public static double ClothingPerPersonYear(SimulationConfig config)
            => config.Neolithic.FabricConsumptionPerPersonTurn / config.Neolithic.ReferenceTurnYears;
        public static double StarvationGraceYears(SimulationConfig config)
            => config.StarvationGraceTurns * config.Neolithic.ReferenceTurnYears;

        public static DemographicRates Rates(SimulationConfig config, double population, double nutrition,
            double clothingCoverage, double housingCapacity, bool legacyStarvationActive = false,
            double densityPopulationOffset = 0, double crowdingMitigation = 1, double diseaseMitigation = 1,
            double fertilityFactor = 1, double additionalMortality = 0,
            double sanitationCapacity = 0, double? diseaseReduction = null)
        {
            if (!(population >= 0) || !Finite(population) || !(densityPopulationOffset >= 0) || !Finite(densityPopulationOffset))
                throw new DemographicNumericalException("Population and density offset must be finite and nonnegative.");
            if (!Finite(sanitationCapacity) || sanitationCapacity < 0)
                throw new DemographicNumericalException("Sanitation capacity must be finite and nonnegative.");
            double h = population > 0 ? Math.Min(1, Math.Max(0, housingCapacity) / population) : housingCapacity > 0 ? 1 : 0;
            double s = population > 0 ? Math.Min(1, Math.Max(0, sanitationCapacity) / population) : sanitationCapacity > 0 ? 1 : 0;
            double rawAlpha = diseaseReduction ?? SanitationDiseaseReduction(config);
            if (!Finite(rawAlpha))
                throw new DemographicNumericalException("Disease reduction must be finite.");
            double alpha = Numbers.Clamp(rawAlpha, 0, 1);
            return RatesAtDensityCoverage(config, population + densityPopulationOffset, nutrition,
                clothingCoverage, h, legacyStarvationActive, crowdingMitigation, diseaseMitigation,
                fertilityFactor, additionalMortality, s, alpha);
        }

        // The coupled expedition propagator supplies one shared physical density while each service pool retains its
        // own clothing/housing/sanitation coverage. This is the same v1.1 hazard law as Rates; only the state decomposition differs.
        internal static DemographicRates RatesAtDensityCoverage(SimulationConfig config, double density,
            double nutrition, double clothingCoverage, double housingCoverage, bool legacyStarvationActive = false,
            double crowdingMitigation = 1, double diseaseMitigation = 1, double fertilityFactor = 1,
            double additionalMortality = 0, double sanitationCoverage = 0, double diseaseReduction = 0)
        {
            if (!(density >= 0) || !Finite(density))
                throw new DemographicNumericalException("Demographic density must be finite and nonnegative.");
            double f = Numbers.Clamp(nutrition, 0, 1), c = Numbers.Clamp(clothingCoverage, 0, 1);
            double h = Numbers.Clamp(housingCoverage, 0, 1);
            double s = Numbers.Clamp(sanitationCoverage, 0, 1);
            double effectiveDiseaseMit = Math.Max(0, diseaseMitigation * Math.Max(0, 1 - Numbers.Clamp(diseaseReduction, 0, 1) * s));
            double exposure = config.ExposureMortalityRate * Math.Max(0, 1 - config.ExposureClothingReduction * c - config.ExposureHousingReduction * h);
            double crowding = config.CrowdingMortalityRateAt1000 * ExponentialExcess(density, config.CrowdingExponent) * crowdingMitigation;
            double disease = config.DiseaseMortalityRateAt1000 * ExponentialExcess(density, config.DiseaseExponent) * effectiveDiseaseMit;
            double starvation = legacyStarvationActive && f < 1
                ? -Math.Log(Math.Max(1e-15, 1 - config.Mortality * (1 - f))) / config.Neolithic.ReferenceTurnYears : 0;
            double birth = config.BaseBirthRate * f * fertilityFactor;
            if (!Finite(birth) || !Finite(exposure) || !Finite(crowding) || !Finite(disease) || !Finite(starvation) || !Finite(additionalMortality))
                throw new DemographicNumericalException("A physical demographic hazard overflowed; no mortality ceiling was applied.");
            return new DemographicRates(birth, exposure, crowding, disease, starvation, Math.Max(0, additionalMortality));
        }

        // Fixed physical service over an arbitrary interval. Housing and sanitation coverage remain physical capacity / N(t),
        // and the legacy-starvation compatibility hazard begins only at its physical grace boundary.
        public static DemographicResult AdvanceFixed(SimulationConfig config, double population, double years,
            double nutrition, double clothingCoverage, double housingCapacity, double deficitYears = 0,
            bool includeLegacyStarvation = true, double densityPopulationOffset = 0, double fertilityFactor = 1,
            double sanitationCapacity = 0, double? diseaseReduction = null)
            => AdvanceFixed(config, population, years, nutrition, clothingCoverage, housingCapacity, deficitYears,
                includeLegacyStarvation, densityPopulationOffset, fertilityFactor, false,
                sanitationCapacity, ResolveDiseaseReduction(config, diseaseReduction));

        // fast = true is the planning solver (PlanningBudget): bracketed Brent roots instead of bisection and reused band
        // edge rates. Same law and tolerances; results differ from the authoritative solve only within those tolerances.
        private static DemographicResult AdvanceFixed(SimulationConfig config, double population, double years,
            double nutrition, double clothingCoverage, double housingCapacity, double deficitYears,
            bool includeLegacyStarvation, double densityPopulationOffset, double fertilityFactor, bool fast,
            double sanitationCapacity = 0, double diseaseReduction = 0)
        {
            ValidateCall(config, population, years, sanitationCapacity, diseaseReduction);
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
                    housingCapacity, false, densityPopulationOffset, fertilityFactor, fast, sanitationCapacity, diseaseReduction);
                total.Add(part); current = part.FinalPopulation; remaining -= unexposed;
            }
            if (remaining > 1e-14)
            {
                var part = AdvanceCore(config, current, remaining, total.Nutrition, total.ClothingCoverage,
                    housingCapacity, includeLegacyStarvation && deficient, densityPopulationOffset, fertilityFactor, fast, sanitationCapacity, diseaseReduction);
                total.Add(part);
            }
            total.FinalPopulation = Math.Max(0, total.FinalPopulation);
            total.Deaths = total.ExposureDeaths + total.CrowdingDeaths + total.DiseaseDeaths + total.LegacyStarvationDeaths + total.AdditionalDeaths;
            total.ConservationError = total.FinalPopulation - total.InitialPopulation - (total.Births - total.Deaths);
            total.AverageHousingCoverage = total.PersonYears > 0 ? Numbers.Clamp(total.HousingPersonYears / total.PersonYears, 0, 1) : 1;
            total.AverageSanitationCoverage = total.PersonYears > 0 ? Numbers.Clamp(total.SanitationPersonYears / total.PersonYears, 0, 1) : 0;
            return total;
        }

        // A fixed, already-paid food/textile envelope. The local monotone root makes F and C consistent with the same
        // person-years that births and deaths create; it never invokes production, trade, ecology or a planner.
        public static DemographicResult AdvanceBudget(SimulationConfig config, double population, double years,
            double foodBudget, double clothingBudget, double housingCapacity, double deficitYears = 0,
            bool includeLegacyStarvation = true, double densityPopulationOffset = 0,
            double sanitationCapacity = 0, double? diseaseReduction = null)
        {
            double alpha = ResolveDiseaseReduction(config, diseaseReduction);
            return SurplusFunded(config)
                ? SurplusBudget(config, population, years, foodBudget, clothingBudget, housingCapacity, deficitYears, includeLegacyStarvation, densityPopulationOffset, out _, false, sanitationCapacity, alpha)
                : AdvanceBudgetCore(config, population, years, foodBudget, clothingBudget, housingCapacity, deficitYears, includeLegacyStarvation, densityPopulationOffset, 1, false, sanitationCapacity, alpha);
        }

        // Planning evaluation of AdvanceBudget (the Elder's candidate scoring; never resolution). The same law and
        // tolerances, solved with bracketed Brent steps: the person-year closure is bracketed by one fixed-point step from
        // the birth-only envelope, the surplus case is classified and its birth fraction bracketed by the two endpoint
        // solves (FastSurplus), and the equilibrium by halving steps from the population. About 6 interval solves per
        // call instead of about 62, at about 60% of the hazard evaluations each; results agree with AdvanceBudget within
        // DemographicServiceTolerance / DemographicRootTolerance (Docs/ClothingCoverageAndSurplusBirths.md §7).
        internal static DemographicResult PlanningBudget(SimulationConfig config, double population, double years,
            double foodBudget, double clothingBudget, double housingCapacity, double deficitYears = 0,
            bool includeLegacyStarvation = true, double densityPopulationOffset = 0,
            double sanitationCapacity = 0, double? diseaseReduction = null)
        {
            double alpha = ResolveDiseaseReduction(config, diseaseReduction);
            return SurplusFunded(config)
                ? SurplusBudget(config, population, years, foodBudget, clothingBudget, housingCapacity, deficitYears, includeLegacyStarvation, densityPopulationOffset, out _, true, sanitationCapacity, alpha)
                : AdvanceBudgetCore(config, population, years, foodBudget, clothingBudget, housingCapacity, deficitYears, includeLegacyStarvation, densityPopulationOffset, 1, true, sanitationCapacity, alpha);
        }

        private static DemographicResult AdvanceBudgetCore(SimulationConfig config, double population, double years,
            double foodBudget, double clothingBudget, double housingCapacity, double deficitYears,
            bool includeLegacyStarvation, double densityPopulationOffset, double fertilityFactor, bool fast = false,
            double sanitationCapacity = 0, double diseaseReduction = 0)
        {
            ValidateCall(config, population, years, sanitationCapacity, diseaseReduction);
            foodBudget = Math.Max(0, foodBudget); clothingBudget = Math.Max(0, clothingBudget);
            if (years == 0 || population <= 0)
                return new DemographicResult { InitialPopulation = population, FinalPopulation = population, Nutrition = 1, ClothingCoverage = 1 };
            double foodRate = FoodPerPersonYear(config), clothingRate = ClothingPerPersonYear(config);
            double maximumPersonYears = BirthOnlyPersonYears(population, config.BaseBirthRate, years);
            // Funding is physical stock, so an absolute epsilon must not turn a microscopic zero budget into
            // free full service. Exact equality is sufficient for the birth-only upper envelope shortcut.
            bool fullFood = foodBudget >= foodRate * maximumPersonYears;
            bool fullClothing = clothingBudget >= clothingRate * maximumPersonYears;
            int allRoot = 0, allBands = 0, allHazards = 0, allQuadrature = 0, evaluations = 0;
            DemographicResult Evaluate(double personYears)
            {
                double denominator = personYears > 0 ? personYears : double.Epsilon;
                double food = fullFood ? 1 : Numbers.Clamp(foodBudget / (foodRate * denominator), 0, 1);
                double clothing = fullClothing ? 1 : Numbers.Clamp(clothingBudget / (clothingRate * denominator), 0, 1);
                var value = AdvanceFixed(config, population, years, food, clothing, housingCapacity,
                    deficitYears, includeLegacyStarvation, densityPopulationOffset, fertilityFactor, fast,
                    sanitationCapacity, diseaseReduction);
                evaluations++;
                allRoot += value.RootEvaluations; allBands += value.BandsCrossed; allHazards += value.HazardEvaluations; allQuadrature += value.QuadratureEvaluations;
                return value;
            }
            DemographicResult result;
            if (fullFood && fullClothing) result = Evaluate(maximumPersonYears);
            else if (fast) result = FastClosure(config, maximumPersonYears, Evaluate);
            else
            {
                double low = 0, high = Math.Max(double.Epsilon, maximumPersonYears);
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

        // Diagnostic prototype only (default off; process-wide so parallel resolution sees it). Surplus-funded births:
        // food pays the resident cohort's meals first; births occur at BaseBirthRate * g, where g in [0,1] is the
        // largest birth fraction whose newborn person-years the remaining food still feeds in full. When food cannot
        // feed even a birthless cohort, g = 0 and the ordinary nutrition closure and hunger hazards apply.
        internal static volatile bool SurplusFundedBirthsDiagnostic;
        internal static bool SurplusFunded(SimulationConfig config) => SurplusFundedBirthsDiagnostic || config.SurplusFundedBirths;
        internal static long SurplusCalls, SurplusFullCalls, SurplusShortageCalls, SurplusBoundCalls;
        internal static DemographicResult SurplusBudget(SimulationConfig config, double population, double years,
            double foodBudget, double clothingBudget, double housingCapacity, double deficitYears,
            bool includeLegacyStarvation, double densityPopulationOffset, out double fertility, bool fast = false,
            double sanitationCapacity = 0, double? diseaseReduction = null)
        {
            double alpha = ResolveDiseaseReduction(config, diseaseReduction);
            System.Threading.Interlocked.Increment(ref SurplusCalls);
            if (fast && years > 0 && population > 0 && foodBudget > 0)
                return FastSurplus(config, population, years, foodBudget, clothingBudget, housingCapacity, deficitYears,
                    includeLegacyStarvation, densityPopulationOffset, out fertility, sanitationCapacity, alpha);
            fertility = 1;
            var full = AdvanceBudgetCore(config, population, years, foodBudget, clothingBudget, housingCapacity,
                deficitYears, includeLegacyStarvation, densityPopulationOffset, 1, fast, sanitationCapacity, alpha);
            if (years == 0 || population <= 0 || full.Nutrition >= 1 - 1e-12)
            { System.Threading.Interlocked.Increment(ref SurplusFullCalls); return full; }
            fertility = 0;
            var none = AdvanceBudgetCore(config, population, years, foodBudget, clothingBudget, housingCapacity,
                deficitYears, includeLegacyStarvation, densityPopulationOffset, 0, fast, sanitationCapacity, alpha);
            if (none.Nutrition < 1 - 1e-12)
            { System.Threading.Interlocked.Increment(ref SurplusShortageCalls); return none; }
            System.Threading.Interlocked.Increment(ref SurplusBoundCalls);
            // Food binds between: every resident is fed and the remaining food feeds exactly the newborns, so the
            // closed person-years are foodBudget / foodRate and clothing coverage follows from the same person-years.
            double foodRate = FoodPerPersonYear(config), clothingRate = ClothingPerPersonYear(config);
            double target = Math.Max(0, foodBudget) / foodRate;
            double c = clothingRate * target > 0 ? Numbers.Clamp(Math.Max(0, clothingBudget) / (clothingRate * target), 0, 1) : 1;
            double lo = 0, hi = 1; int evaluations = 0;
            for (int i = 0; i < 48; i++)
            {
                double mid = (lo + hi) * .5;
                var trial = AdvanceFixed(config, population, years, 1, c, housingCapacity, deficitYears,
                    includeLegacyStarvation, densityPopulationOffset, mid, false, sanitationCapacity, alpha);
                evaluations++;
                if (trial.PersonYears > target) hi = mid; else lo = mid;
                if (hi - lo <= 1e-12 || Math.Abs(trial.PersonYears - target) <= config.DemographicServiceTolerance * Math.Max(1, target)) break;
            }
            fertility = lo;
            var result = AdvanceFixed(config, population, years, 1, c, housingCapacity, deficitYears,
                includeLegacyStarvation, densityPopulationOffset, lo, false, sanitationCapacity, alpha);
            result.FoodConsumed = Math.Min(Math.Max(0, foodBudget), foodRate * result.PersonYears);
            result.ClothingConsumed = Math.Min(Math.Max(0, clothingBudget), clothingRate * result.PersonYears);
            result.Nutrition = result.PersonYears > 0 ? Numbers.Clamp(result.FoodConsumed / (foodRate * result.PersonYears), 0, 1) : 1;
            result.ClothingCoverage = result.PersonYears > 0 ? Numbers.Clamp(result.ClothingConsumed / (clothingRate * result.PersonYears), 0, 1) : 1;
            result.ServiceIterations = full.ServiceIterations + none.ServiceIterations + evaluations + 1;
            return result;
        }

        // Planning form of SurplusBudget. With T = food / foodRate and c = clothing / (clothingRate T), let
        // f(g) = PY(F = 1, C = c, birth fraction g) - T, rising in g. Because each closure is monotone in its assumed
        // person-years and F = 1 below T, the birthless closure is fed exactly when f(0) <= 0 and the full-birth closure
        // exactly when f(1) <= 0. The two endpoints therefore classify the case that SurplusBudget finds with two complete
        // closures, and in the bound case they already bracket g.
        private static DemographicResult FastSurplus(SimulationConfig config, double population, double years,
            double foodBudget, double clothingBudget, double housingCapacity, double deficitYears,
            bool includeLegacyStarvation, double densityPopulationOffset, out double fertility,
            double sanitationCapacity = 0, double diseaseReduction = 0)
        {
            double foodRate = FoodPerPersonYear(config), clothingRate = ClothingPerPersonYear(config);
            double target = foodBudget / foodRate;
            double c = clothingRate * target > 0 ? Numbers.Clamp(Math.Max(0, clothingBudget) / (clothingRate * target), 0, 1) : 1;
            double tolerance = config.DemographicServiceTolerance * Math.Max(1, target);
            int evaluations = 0;
            DemographicResult At(double g, double clothing = double.NaN)
            {
                evaluations++;
                return AdvanceFixed(config, population, years, 1, double.IsNaN(clothing) ? c : clothing, housingCapacity, deficitYears,
                    includeLegacyStarvation, densityPopulationOffset, g, true, sanitationCapacity, diseaseReduction);
            }
            var one = At(1);
            double f1 = one.PersonYears - target;
            if (f1 <= 0)
            {
                System.Threading.Interlocked.Increment(ref SurplusFullCalls);
                fertility = 1;
                // Fed with full births. If clothing also covers the fully clothed path, the closure's root is that path
                // itself (PY rises with C, so the root lies at or below it and C = 1 there): one solve, no closure.
                var clothed = c >= 1 ? one : At(1, 1);
                if (clothed.PersonYears <= target && Math.Max(0, clothingBudget) >= clothingRate * clothed.PersonYears)
                {
                    clothed.FoodConsumed = foodRate * clothed.PersonYears; clothed.ClothingConsumed = clothingRate * clothed.PersonYears;
                    clothed.Nutrition = 1; clothed.ClothingCoverage = 1; clothed.ServiceIterations = evaluations;
                    return clothed;
                }
                var full = AdvanceBudgetCore(config, population, years, foodBudget, clothingBudget, housingCapacity,
                    deficitYears, includeLegacyStarvation, densityPopulationOffset, 1, true, sanitationCapacity, diseaseReduction);
                full.ServiceIterations += evaluations;
                return full;
            }
            var zero = At(0);
            double f0 = zero.PersonYears - target;
            if (f0 > 0)
            {
                System.Threading.Interlocked.Increment(ref SurplusShortageCalls);
                fertility = 0;
                var none = AdvanceBudgetCore(config, population, years, foodBudget, clothingBudget, housingCapacity,
                    deficitYears, includeLegacyStarvation, densityPopulationOffset, 0, true, sanitationCapacity, diseaseReduction);
                none.ServiceIterations += evaluations;
                return none;
            }
            System.Threading.Interlocked.Increment(ref SurplusBoundCalls);
            DemographicResult result;
            if (f0 >= -tolerance) { fertility = 0; result = zero; }
            else if (f1 <= tolerance) { fertility = 1; result = one; }
            else if (!Brent(0, f0, zero, 1, f1, one, g => { var r = At(g); return (r.PersonYears - target, r); },
                    _ => tolerance, 1e-12, 64, out fertility, out result))
                throw new DemographicNumericalException("Surplus-funded birth fraction did not converge.");
            result.FoodConsumed = Math.Min(foodBudget, foodRate * result.PersonYears);
            result.ClothingConsumed = Math.Min(Math.Max(0, clothingBudget), clothingRate * result.PersonYears);
            result.Nutrition = result.PersonYears > 0 ? Numbers.Clamp(result.FoodConsumed / (foodRate * result.PersonYears), 0, 1) : 1;
            result.ClothingCoverage = result.PersonYears > 0 ? Numbers.Clamp(result.ClothingConsumed / (clothingRate * result.PersonYears), 0, 1) : 1;
            result.ServiceIterations = evaluations;
            return result;
        }

        // Planning closure: f(m) = PY(m) - m falls with the assumed person-years m, so one fixed-point step from the
        // birth-only envelope m0 gives m1 = PY(m0) <= m0 with f(m1) >= 0 and f(m0) <= 0: a tight bracket after two
        // evaluations instead of bisection from zero.
        private static DemographicResult FastClosure(SimulationConfig config, double maximumPersonYears, Func<double, DemographicResult> evaluate)
        {
            double Tolerance(double m) => config.DemographicServiceTolerance * Math.Max(1, m);
            double high = Math.Max(double.Epsilon, maximumPersonYears);
            var rHigh = evaluate(high);
            if (rHigh.PersonYears > high * (1 + 1e-10))
                throw new DemographicNumericalException("Finite-service closure exceeded the birth-only person-year bracket.");
            double fHigh = rHigh.PersonYears - high;
            if (Math.Abs(fHigh) <= Tolerance(high)) return rHigh;
            double low = rHigh.PersonYears;
            var rLow = evaluate(low);
            double fLow = rLow.PersonYears - low;
            if (Math.Abs(fLow) <= Tolerance(low)) return rLow;
            if (fLow < 0)
            {
                // Rounding broke the monotone step: fall back to the full bracket.
                high = low; fHigh = fLow; rHigh = rLow;
                low = 0; rLow = evaluate(0); fLow = rLow.PersonYears;
                if (Math.Abs(fLow) <= Tolerance(0)) return rLow;
            }
            if (!Brent(low, fLow, rLow, high, fHigh, rHigh, m => { var r = evaluate(m); return (r.PersonYears - m, r); },
                Tolerance, 1e-14 * Math.Max(1, high), config.DemographicMaxServiceIterations, out _, out var closed))
                throw new DemographicNumericalException("Finite food/textile person-year closure did not converge.");
            return closed;
        }

        // Brent's method (inverse quadratic / secant steps safeguarded by bisection) on a sign-changing bracket [a, b] with
        // known end values and payloads. Stops at the first evaluated point with |f| <= tolerance(x), or when the bracket
        // is narrower than xTolerance; returns false after maxIterations. Deterministic: a pure function of its inputs.
        private static bool Brent<T>(double a, double fa, T pa, double b, double fb, T pb, Func<double, (double f, T value)> f,
            Func<double, double> tolerance, double xTolerance, int maxIterations, out double root, out T value)
        {
            double c = b, fc = fb, d = b - a, e = d; T pc = pb;
            for (int i = 0; i < maxIterations; i++)
            {
                if ((fb > 0 && fc > 0) || (fb < 0 && fc < 0)) { c = a; fc = fa; pc = pa; e = d = b - a; }
                if (Math.Abs(fc) < Math.Abs(fb)) { a = b; b = c; c = a; fa = fb; fb = fc; fc = fa; pa = pb; pb = pc; pc = pa; }
                double tol1 = 4e-16 * Math.Abs(b) + .5 * xTolerance, xm = .5 * (c - b);
                if (Math.Abs(xm) <= tol1 || fb == 0) { root = b; value = pb; return true; }
                if (Math.Abs(e) >= tol1 && Math.Abs(fa) > Math.Abs(fb))
                {
                    double s = fb / fa, p, q;
                    if (a == c) { p = 2 * xm * s; q = 1 - s; }
                    else { q = fa / fc; double r = fb / fc; p = s * (2 * xm * q * (q - r) - (b - a) * (r - 1)); q = (q - 1) * (r - 1) * (s - 1); }
                    if (p > 0) q = -q;
                    p = Math.Abs(p);
                    if (2 * p < Math.Min(3 * xm * q - Math.Abs(tol1 * q), Math.Abs(e * q))) { e = d; d = p / q; }
                    else { d = xm; e = d; }
                }
                else { d = xm; e = d; }
                a = b; fa = fb; pa = pb;
                b += Math.Abs(d) > tol1 ? d : (xm >= 0 ? tol1 : -tol1);
                (fb, pb) = f(b);
                if (Math.Abs(fb) <= tolerance(b)) { root = b; value = pb; return true; }
            }
            root = double.NaN; value = default;
            return false;
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
            double nutrition, double clothing, double housingCapacity, bool starvationActive, double densityOffset,
            double fertilityFactor = 1, bool fast = false, double sanitationCapacity = 0, double diseaseReduction = 0)
        {
            var result = new DemographicResult { InitialPopulation = population, FinalPopulation = population, Nutrition = nutrition, ClothingCoverage = clothing };
            if (years <= 0 || population <= 0) return result;
            double alpha = Numbers.Clamp(diseaseReduction, 0, 1);
            Func<double, DemographicRates> ratesAtPopulation = n => Rates(config, n, nutrition, clothing, housingCapacity, starvationActive, densityOffset, fertilityFactor: fertilityFactor, sanitationCapacity: sanitationCapacity, diseaseReduction: alpha);
            int rootEvaluations = 0;
            double RateAtLog(double x)
            {
                rootEvaluations++;
                if (x > Math.Log(double.MaxValue)) throw new DemographicNumericalException("Log-population exceeds the finite double domain.");
                if (x < LogSmallestPositivePopulation)
                    throw new DemographicNumericalException("Continuous population underflowed the finite positive double domain.");
                double n = Math.Exp(x);
                return fast ? FastNet(n) : ratesAtPopulation(n).Net;
            }
            // Planning solver: Rates(...).Net for n > 0 with the per-interval constants hoisted. The same operations in
            // the same order (crowding mitigation is 1 and additional mortality 0 on this path), so the value is identical.
            double fastF = Numbers.Clamp(nutrition, 0, 1), fastC = Numbers.Clamp(clothing, 0, 1);
            double fastBirth = config.BaseBirthRate * fastF * fertilityFactor;
            double fastStarvation = starvationActive && fastF < 1
                ? -Math.Log(Math.Max(1e-15, 1 - config.Mortality * (1 - fastF))) / config.Neolithic.ReferenceTurnYears : 0;
            double fastCapacity = Math.Max(0, housingCapacity);
            double fastSanitation = Math.Max(0, sanitationCapacity);
            double FastNet(double n)
            {
                double h = Numbers.Clamp(Math.Min(1, fastCapacity / n), 0, 1), density = n + densityOffset;
                double s = Numbers.Clamp(Math.Min(1, fastSanitation / n), 0, 1);
                double exposure = config.ExposureMortalityRate * Math.Max(0, 1 - config.ExposureClothingReduction * fastC - config.ExposureHousingReduction * h);
                double crowding = config.CrowdingMortalityRateAt1000 * ExponentialExcess(density, config.CrowdingExponent) * 1.0;
                double disease = config.DiseaseMortalityRateAt1000 * ExponentialExcess(density, config.DiseaseExponent) * Math.Max(0, 1 - alpha * s);
                if (!Finite(fastBirth) || !Finite(exposure) || !Finite(crowding) || !Finite(disease) || !Finite(fastStarvation))
                    throw new DemographicNumericalException("A physical demographic hazard overflowed; no mortality ceiling was applied.");
                return fastBirth - (exposure + crowding + disease + fastStarvation + 0.0);
            }
            // Planning solver: consecutive bands share edges (a crossed edge is the next band's start and, without a
            // new knot, its near edge), so the same log-population is asked for repeatedly. Reuse is exact.
            double k0 = double.NaN, k1 = double.NaN, k2 = double.NaN, v0 = 0, v1 = 0, v2 = 0; int slot = 0;
            double EdgeRate(double x)
            {
                if (!fast) return RateAtLog(x);
                if (x == k0) return v0; if (x == k1) return v1; if (x == k2) return v2;
                double v = RateAtLog(x);
                if (slot == 0) { k0 = x; v0 = v; } else if (slot == 1) { k1 = x; v1 = v; } else { k2 = x; v2 = v; }
                slot = (slot + 1) % 3;
                return v;
            }

            double equilibrium = 0, rootX = double.NaN;
            double lowLimit = ratesAtPopulation(0).Net; rootEvaluations++;
            if (lowLimit > 0)
            {
                double lo = LogSmallestPositivePopulation, hi = Math.Log(Math.Max(1, population));
                double rhi = RateAtLog(hi);
                int bracket = 0;
                while (rhi > 0 && bracket++ < config.DemographicMaxRootIterations)
                {
                    hi += Math.Log(2);
                    rhi = RateAtLog(hi);
                }
                if (rhi <= 0 && fast)
                {
                    // Planning solver: walk down by halving the population to a positive rate (the bracket loop above
                    // usually leaves it one step away), then Brent in log-population.
                    double a = hi - Math.Log(2), ra = a > LogSmallestPositivePopulation ? RateAtLog(a) : lowLimit;
                    while (ra <= 0 && a > LogSmallestPositivePopulation)
                    {
                        hi = a; rhi = ra; a -= Math.Log(2);
                        ra = a > LogSmallestPositivePopulation ? RateAtLog(a) : lowLimit;
                    }
                    a = Math.Max(a, LogSmallestPositivePopulation);
                    if (Math.Abs(rhi) <= config.DemographicRootTolerance) rootX = hi;
                    else
                    {
                        if (!Brent<object>(a, ra, null, hi, rhi, null, z => (RateAtLog(z), null),
                            _ => config.DemographicRootTolerance, 1e-13, config.DemographicMaxRootIterations, out double z0, out _))
                            throw new DemographicNumericalException("Positive demographic equilibrium solve did not converge.");
                        rootX = z0;
                    }
                    equilibrium = Math.Exp(rootX);
                    result.HasPositiveEquilibrium = true; result.EquilibriumPopulation = equilibrium;
                }
                else if (rhi <= 0)
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

            double x = Math.Log(population), remaining = years;
            int segments = 0;
            double sanitationKnot = sanitationCapacity > 0 && alpha > 0 ? Math.Log(sanitationCapacity) : double.NaN;
            while (remaining > 1e-13)
            {
                if (++segments > config.DemographicMaxBands)
                    throw new DemographicNumericalException("Demographic log-band budget exhausted before the interval completed.");
                double actualRate = EdgeRate(x);
                if (Math.Abs(actualRate) <= config.DemographicRootTolerance)
                {
                    IntegrateSegment(result, ratesAtPopulation, housingCapacity, sanitationCapacity, x, 0, 0, remaining);
                    remaining = 0; break;
                }
                int direction = actualRate > 0 ? 1 : -1;
                Band(config.DemographicLogBandWidth, x, direction, rootX,
                    housingCapacity > 0 ? Math.Log(housingCapacity) : double.NaN, sanitationKnot, out double left, out double right);
                double rLeft = EdgeRate(left), rRight = EdgeRate(right);
                double m = (rRight - rLeft) / (right - left);
                if (m > 1e-12) throw new DemographicNumericalException("Physical net growth was not monotone over a demographic band.");
                if (m > 0) m = 0;
                double r0 = rLeft + m * (x - left);
                if (Math.Abs(r0) <= config.DemographicRootTolerance)
                {
                    IntegrateSegment(result, ratesAtPopulation, housingCapacity, sanitationCapacity, x, 0, 0, remaining);
                    remaining = 0; break;
                }
                double edge = direction > 0 ? right : left;
                double edgeRate = rLeft + m * (edge - left);
                bool retainsSign = direction > 0 ? edgeRate >= -config.DemographicRootTolerance : edgeRate <= config.DemographicRootTolerance;
                double crossing = retainsSign ? CrossingTime(x, edge, r0, m) : double.PositiveInfinity;
                bool cross = crossing > 1e-13 && Finite(crossing) && crossing < remaining - 1e-13;
                double h = cross ? crossing : remaining;
                IntegrateSegment(result, ratesAtPopulation, housingCapacity, sanitationCapacity, x, r0, m, h);
                if (cross)
                {
                    x = edge; remaining -= h; result.BandsCrossed++;
                }
                else
                {
                    x = LocalLogPopulation(x, r0, m, h); remaining = 0;
                }
            }
            if (x < LogSmallestPositivePopulation)
                throw new DemographicNumericalException("Continuous population underflowed the finite positive double domain.");
            result.FinalPopulation = Math.Exp(x);
            result.RootEvaluations = rootEvaluations;            ReconcileCauses(config, result);
            return result;
        }

        private static void IntegrateSegment(DemographicResult result, Func<double, DemographicRates> rates,
            double housingCapacity, double sanitationCapacity, double startX, double startRate, double slope, double duration)
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
                result.SanitationPersonYears += (n > 0 ? Math.Min(1, Math.Max(0, sanitationCapacity) / n) : sanitationCapacity > 0 ? 1 : 0) * n * weight;
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

        private static void Band(double width, double x, int direction, double root, double housing, double sanitation,
            out double left, out double right)
        {
            double q = x / width, nearest = Math.Round(q);
            bool onGrid = Math.Abs(q - nearest) <= 2e-12;
            double index = onGrid ? (direction > 0 ? nearest : nearest - 1) : Math.Floor(q);
            left = index * width; right = (index + 1) * width;
            InsertKnot(root, x, direction, ref left, ref right);
            InsertKnot(housing, x, direction, ref left, ref right);
            InsertKnot(sanitation, x, direction, ref left, ref right);
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
            // The normalizer depends on k alone; memoizing it per thread returns the identical double.
            if (k != normalizerK || double.IsNaN(normalizerK)) { normalizerK = k; normalizer = Expm1(1000 * k); }
            return Expm1(exponent) / normalizer;
        }
        [ThreadStatic] private static double normalizerK, normalizer;

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

        private static double ResolveDiseaseReduction(SimulationConfig config, double? diseaseReduction)
        {
            double raw = diseaseReduction ?? SanitationDiseaseReduction(config);
            if (!Finite(raw))
                throw new ArgumentException("Demographic disease reduction must be finite.");
            return Numbers.Clamp(raw, 0, 1);
        }

        private static void ValidateCall(SimulationConfig config, double population, double years, double sanitationCapacity = 0, double diseaseReduction = 0)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (!Finite(population) || !Finite(years) || population < 0 || years < 0)
                throw new ArgumentException("Demographic population and duration must be finite and nonnegative.");
            if (!Finite(sanitationCapacity) || sanitationCapacity < 0)
                throw new ArgumentException("Demographic sanitation capacity must be finite and nonnegative.");
            if (!Finite(diseaseReduction))
                throw new ArgumentException("Demographic disease reduction must be finite.");
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
