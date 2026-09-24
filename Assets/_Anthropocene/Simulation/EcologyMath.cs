using System;

namespace Anthropocene.Simulation
{
    public readonly struct StockFlow
    {
        public readonly double Stock, Realized, Unmet, Recharge, Overflow, DepletionDays;
        public StockFlow(double stock, double realized, double unmet, double recharge, double overflow, double depletion)
        { Stock = stock; Realized = realized; Unmet = unmet; Recharge = recharge; Overflow = overflow; DepletionDays = depletion; }
    }
    public static class EcologyMath
    {
        // Cultivation overlays the natural vegetation services while leaving
        // ecological vegetation state and total biomass capacity independent.
        public static double CultivationVegetationRetention(SimulationConfig config, double cultivationIntensity)
        {
            double q = Numbers.Clamp(cultivationIntensity, 0, 1);
            double displacementStart = config.CultivationVegetationDisplacementStart;
            return q <= displacementStart ? 1 : Numbers.Clamp((1 - q) / (1 - displacementStart), 0, 1);
        }
        public static double RetainedWildFoodCapacity(double ecologicalWildFood, double cultivationIntensity, SimulationConfig config)
            => Math.Max(0, ecologicalWildFood) * CultivationVegetationRetention(config, cultivationIntensity);

        // Ecological wild food is retained first, then the remaining biomass is
        // made accessible by cultivation according to q squared.
        public static double EffectiveWildFoodCapacity(double ecologicalWildFood, double biomass, double cultivationIntensity, SimulationConfig config)
        {
            double q = Numbers.Clamp(cultivationIntensity, 0, 1), capacity = Math.Max(0, biomass);
            double wildNatural = Numbers.Clamp(RetainedWildFoodCapacity(ecologicalWildFood, q, config), 0, capacity);
            return wildNatural + (capacity - wildNatural) * q * q;
        }
        public static double ManagedRegenerationFlow(SimulationConfig config, double cultivationIntensity, double biomass, double biomassRecoveryPerDay, double operatingFraction)
            => Math.Max(0, config.CultivationRegenFlux) * Numbers.Clamp(cultivationIntensity, 0, 1) * Math.Max(0, biomass) * Math.Max(0, biomassRecoveryPerDay) * Numbers.Clamp(operatingFraction, 0, 1);
        public static double SustainableHarvestRate(double biomass, double biomassRecoveryPerDay, double sustainableProductionFraction, double managedFlow)
            => Math.Max(0, biomass) * Math.Max(0, biomassRecoveryPerDay) * Math.Max(0, sustainableProductionFraction) + Math.Max(0, managedFlow);
        public static double EffectiveWoodCapacity(double ecologicalCapacity, double cultivationIntensity, SimulationConfig config)
            => Math.Max(0, ecologicalCapacity) * CultivationVegetationRetention(config, cultivationIntensity);

        // Bounded stock with additive managed renewal, integrated before any transfer.
        public static StockFlow ManagedStock(double initial, double capacity, double rate, double inflow, double request, double days)
        {
            if (inflow <= 0) return Stock(initial, capacity, rate, request, days);
            double overflow = Math.Max(0, initial - capacity), start = Numbers.Clamp(initial, 0, capacity);
            double supply = rate * capacity + inflow, depletion = double.PositiveInfinity;
            double end;
            if (rate <= 1e-18) { if (request > inflow) depletion = start / (request - inflow); end = start + (inflow - request) * days; }
            else { double equilibrium = (supply - request) / rate; end = equilibrium + (start - equilibrium) * Math.Exp(-rate * days); if (equilibrium < 0) depletion = Math.Log((start - equilibrium) / -equilibrium) / rate; }
            double realized = request * Math.Min(days, depletion) + Math.Min(request, supply) * Math.Max(0, days - depletion);
            end = Numbers.Clamp(end, 0, capacity);
            return new StockFlow(end, realized, Math.Max(0, request * days - realized), Math.Max(0, end - start + realized), overflow, depletion);
        }
        // Configuration migration: legacy fractions described one BaseDays interval.
        public static double DailyRate(double fraction, double baseDays) => -Math.Log(1 - Numbers.Clamp(fraction, 0, 1 - 1e-12)) / baseDays;
        public static double Condition(double initial, double natural, double recoveryPerDay, double damagePerDay, double days)
        {
            double rate = recoveryPerDay + damagePerDay;
            if (rate <= 0) return initial;
            double equilibrium = recoveryPerDay * natural / rate;
            return Numbers.Clamp(equilibrium + (initial - equilibrium) * Math.Exp(-rate * days), 0, 1);
        }
        public static StockFlow Stock(double initial, double capacity, double rate, double requestPerDay, double days)
        {
            capacity = Math.Max(0, capacity); rate = Math.Max(0, rate); requestPerDay = Math.Max(0, requestPerDay);
            double overflow = Math.Max(0, initial - capacity), start = Numbers.Clamp(initial, 0, capacity);
            if (days <= 0) return new StockFlow(start, 0, 0, 0, overflow, double.PositiveInfinity);
            double depletion = double.PositiveInfinity, end;
            if (rate <= 1e-18)
            {
                if (requestPerDay > 0) depletion = start / requestPerDay;
                end = Math.Max(0, start - requestPerDay * days);
            }
            else
            {
                double equilibrium = capacity - requestPerDay / rate;
                end = equilibrium + (start - equilibrium) * Math.Exp(-rate * days);
                if (equilibrium < 0) depletion = Math.Log((start - equilibrium) / -equilibrium) / rate;
            }
            double realized = requestPerDay * Math.Min(days, depletion) + Math.Min(requestPerDay, rate * capacity) * Math.Max(0, days - depletion);
            end = Numbers.Clamp(end, 0, capacity);
            return new StockFlow(end, realized, Math.Max(0, requestPerDay * days - realized), Math.Max(0, end - start + realized), overflow, depletion);
        }
    }

    // A capped affine map is closed under composition: min(a*x+b, ceiling).
    public readonly struct SeasonalAffine
    {
        public readonly double A, B, Ceiling;
        public SeasonalAffine(double a, double b, double ceiling) { A = a; B = b; Ceiling = ceiling; }
        public static SeasonalAffine Identity => new SeasonalAffine(1, 0, double.PositiveInfinity);
        public static SeasonalAffine Recovery(double capacity, double rate, double days)
        { double a = Math.Exp(-rate * days); return new SeasonalAffine(a, capacity * (1 - a), capacity); }
        public SeasonalAffine Then(SeasonalAffine next) => new SeasonalAffine(next.A * A, next.A * B + next.B, Math.Min(next.Ceiling, next.A * Ceiling + next.B));
        public double Apply(double value) => Math.Min(Ceiling, A * value + B);
        public double Repeat(double value, long count)
        {
            if (count == 0) return value;
            double power = Math.Pow(A, count), sum = Math.Abs(1 - A) < 1e-14 ? count : (1 - power) / (1 - A);
            double previous = Math.Pow(A, count - 1), previousSum = Math.Abs(1 - A) < 1e-14 ? count - 1 : (1 - previous) / (1 - A);
            double ceiling = double.IsPositiveInfinity(Ceiling) ? Ceiling : Math.Min(Ceiling, previous * Ceiling + B * previousSum);
            return Math.Min(ceiling, power * value + B * sum);
        }
    }
}
