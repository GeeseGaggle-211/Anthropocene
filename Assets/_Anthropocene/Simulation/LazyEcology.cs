using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    public struct EcologyView
    {
        public double Food, PotentialFood, Freshwater, Saltwater, LandCondition, AquaticCondition, Days;
        public VegetationTier CurrentVegetation;
        public EcologyView(Tile t) { Food = t.Food; PotentialFood = t.PotentialFood; Freshwater = t.Freshwater; Saltwater = t.Saltwater; LandCondition = t.LandCondition; AquaticCondition = t.AquaticCondition; Days = t.LastEcologyUpdateDays; CurrentVegetation = t.CurrentVegetation; }
        public void Commit(Tile t) { t.Food = Food; t.PotentialFood = PotentialFood; t.Freshwater = Freshwater; t.Saltwater = Saltwater; t.LandCondition = LandCondition; t.AquaticCondition = AquaticCondition; t.CurrentVegetation = CurrentVegetation; t.LastEcologyUpdateDays = Days; }
    }
    public readonly struct EcologySegment
    {
        public readonly int Season; public readonly double Days;
        public EcologySegment(int season, double days) { Season = season; Days = days; }
    }
    public readonly struct EcologyCapacity
    {
        public readonly double Food, Biomass, Water, FoodRate, BiomassRate, WaterRate;
        public EcologyCapacity(WorldState w, Tile t, EcologyView view, int season)
        {
            var s = t.Seasons[season]; double multiplier = OpeningTransitions.FoodMultiplier(w, t);
            double condition = t.Aquatic || t.WaterBody == WaterKind.Saltwater ? view.AquaticCondition : view.LandCondition;
            Biomass = s.MaxPotentialFood * multiplier * condition;
            Food = Math.Min(Biomass, s.MaxFood * multiplier * condition * FoodEcology.GatherableFraction(t, w.Config, view.CurrentVegetation) / FoodEcology.GatherableFraction(t, w.Config, t.NaturalVegetationPotential));
            Water = t.WaterBody == WaterKind.Saltwater || t.FreshwaterSource == FreshwaterSource.None ? 0 : s.MaxFreshwater;
            FoodRate = s.FoodRecoveryPerDay; BiomassRate = s.BiomassRecoveryPerDay; WaterRate = s.WaterRecoveryPerDay;
        }
    }
    public static class LazyEcology
    {
        public static IEnumerable<EcologySegment> Segments(double start, double days, double baseDays)
        {
            double end = start + days;
            while (start < end - 1e-9)
            {
                long interval = (long)Math.Floor(start / baseDays + 1e-10);
                double part = Math.Min(end - start, (interval + 1) * baseDays - start);
                if (part <= 1e-9) break;
                yield return new EcologySegment((int)(interval % 4), part); start += part;
            }
        }
        public static double[] SeasonWeights(double start, double days, double baseDays)
        {
            var weights = new double[4];
            foreach (var segment in Segments(start, days, baseDays)) weights[segment.Season] += segment.Days / baseDays;
            return weights;
        }
        public static EcologyView ProjectEcology(WorldState w, int tile, double queryDays, bool composeCycles = true)
        {
            var t = w.Tiles[tile]; var v = new EcologyView(t);
            if (queryDays < v.Days - 1e-8) throw new ArgumentOutOfRangeException(nameof(queryDays), "Cannot project before stored ecology.");
            double cycleDays = w.Config.BaseDays * 4;
            // Healthy, undisturbed pools have time-independent targets within each season.
            bool quiet = composeCycles && v.LandCondition == 1 && v.AquaticCondition == 1 && v.CurrentVegetation == t.NaturalVegetationPotential;
            double prefix = Math.Min(queryDays - v.Days, (Math.Ceiling(v.Days / cycleDays - 1e-10) * cycleDays - v.Days + cycleDays) % cycleDays);
            foreach (var s in Segments(v.Days, prefix, w.Config.BaseDays)) Recover(w, t, ref v, s);
            long cycles = quiet ? (long)Math.Floor((queryDays - v.Days) / cycleDays + 1e-10) : 0;
            if (cycles > 0)
            {
                var food = SeasonalAffine.Identity; var nonfood = SeasonalAffine.Identity; var water = SeasonalAffine.Identity;
                for (int s = 0; s < 4; s++)
                {
                    var k = new EcologyCapacity(w, t, v, s);
                    food = food.Then(SeasonalAffine.Recovery(k.Food, k.FoodRate, w.Config.BaseDays));
                    nonfood = nonfood.Then(SeasonalAffine.Recovery(k.Biomass - k.Food, k.BiomassRate, w.Config.BaseDays));
                    water = water.Then(SeasonalAffine.Recovery(k.Water, k.WaterRate, w.Config.BaseDays));
                }
                double n = nonfood.Repeat(Math.Max(0, v.PotentialFood - v.Food), cycles);
                v.Food = food.Repeat(v.Food, cycles); v.PotentialFood = v.Food + n; v.Freshwater = water.Repeat(v.Freshwater, cycles); v.Days += cycles * cycleDays;
            }
            foreach (var s in Segments(v.Days, queryDays - v.Days, w.Config.BaseDays)) Recover(w, t, ref v, s);
            v.Days = queryDays; v.Saltwater = t.Seasons[(int)Math.Floor(queryDays / w.Config.BaseDays) % 4].MaxSaltwater;
            ClampCapacity(w, t, ref v); return v;
        }
        internal static void ClampCapacity(WorldState w, Tile t, ref EcologyView v)
        {
            var k = new EcologyCapacity(w, t, v, (int)Math.Floor(v.Days / w.Config.BaseDays) % 4);
            v.PotentialFood = Math.Min(v.PotentialFood, k.Biomass); v.Food = Math.Min(v.Food, Math.Min(v.PotentialFood, k.Food)); v.Freshwater = Math.Min(v.Freshwater, k.Water);
        }
        private static void Recover(WorldState w, Tile t, ref EcologyView v, EcologySegment segment)
        {
            var k = new EcologyCapacity(w, t, v, segment.Season);
            double n = EcologyMath.Stock(Math.Max(0, v.PotentialFood - v.Food), k.Biomass - k.Food, k.BiomassRate, 0, segment.Days).Stock;
            v.Food = EcologyMath.Stock(v.Food, k.Food, k.FoodRate, 0, segment.Days).Stock; v.PotentialFood = v.Food + n;
            v.Freshwater = EcologyMath.Stock(v.Freshwater, k.Water, k.WaterRate, 0, segment.Days).Stock;
            RecoverCondition(w, t, ref v, segment.Days, 0); v.Days += segment.Days;
        }
        internal static void RecoverCondition(WorldState w, Tile t, ref EcologyView v, double days, double damagePerDay)
        {
            var c = w.Config;
            bool aquatic = t.Aquatic || t.WaterBody == WaterKind.Saltwater;
            double recovery = c.ConditionRecoveryPerYear / c.DaysPerYear * (t.NaturalVegetationPotential == VegetationTier.Low ? c.LowPotentialRecoveryMultiplier : t.NaturalVegetationPotential == VegetationTier.High ? c.HighPotentialRecoveryMultiplier : 1);
            bool plankton = aquatic && (w.Generation?.GeneratorVersion ?? WorldState.GeneratorVersion) >= 11;
            v.LandCondition = EcologyMath.Condition(v.LandCondition, 1, recovery, aquatic ? 0 : damagePerDay, days);
            v.AquaticCondition = EcologyMath.Condition(v.AquaticCondition, 1, plankton ? AquaticEcology.ConditionRecoveryPerDay(t) : recovery, aquatic ? damagePerDay : 0, days);
            v.CurrentVegetation = Vegetation.Update(t.NaturalVegetationPotential, v.CurrentVegetation, plankton ? v.AquaticCondition : v.LandCondition, c);
        }
        public static EcologyView MaterializeEcology(WorldState w, int tile, double queryDays)
        { var view = ProjectEcology(w, tile, queryDays); view.Commit(w.Tiles[tile]); return view; }
        // Cumulative accessible stock + renewable flow envelope, never the final standing stock.
        public static double AccessibleAvailability(WorldState w, int tile, double start, double duration)
        {
            var t = w.Tiles[tile]; var view = ProjectEcology(w, tile, start); double available = view.Food;
            foreach (var segment in Segments(start, duration, w.Config.BaseDays))
            {
                var k = new EcologyCapacity(w, t, view, segment.Season);
                available += k.Food * k.FoodRate * segment.Days;
                RecoverCondition(w, t, ref view, segment.Days, 0);
            }
            return Math.Max(0, available);
        }
    }
}
