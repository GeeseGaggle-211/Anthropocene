using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    [Serializable]
    public sealed class PressureEntry
    {
        public int Tile, Entity;
        public string Kind, Pool;
        public double Requested, Realized, Unmet, CapacityDays, Recharge, ConditionDamage, GatherableSpill;
    }
    [Serializable]
    public sealed class EcologyBalance
    {
        public int Tile;
        public double StartingBiomass, EndingBiomass, BiomassRecharge, BiomassHarvest, BiomassOverflow;
        public double StartingFood, EndingFood, FoodRecharge, FoodHarvest, FoodOverflow;
        public double StartingWater, EndingWater, WaterRecharge, WaterHarvest, WaterOverflow;
    }
    public sealed class TileEcologyResult
    {
        public EcologyView View;
        public List<Transfer> Transfers = new List<Transfer>();
        public List<PressureEntry> Pressure = new List<PressureEntry>();
        public EcologyBalance Balance;
    }
    public static class EcologicalPressure
    {
        // Inputs are read-only. Results can be merged by tile/entity ID after a worker barrier.
        public static TileEcologyResult Integrate(WorldState w, int tileId, IEnumerable<Demand> claims, double start, double duration)
        {
            var tile = w.Tiles[tileId]; var v = LazyEcology.ProjectEcology(w, tileId, start);
            var result = new TileEcologyResult { Balance = new EcologyBalance { Tile = tileId, StartingBiomass = v.PotentialFood, StartingFood = v.Food, StartingWater = v.Freshwater } };
            var requests = claims.OrderBy(d => d.Entity).ThenBy(d => d.Kind, StringComparer.Ordinal).ToArray();
            double foodRequest = requests.Sum(d => d.Food), herdRequest = requests.Sum(d => d.PotentialFood), waterRequest = requests.Sum(d => d.Freshwater);
            double humanImpact = foodRequest <= 0 ? 1 : requests.Sum(d => d.Food * d.ImpactMultiplier) / foodRequest;
            double humanRate = foodRequest / duration, herdRate = herdRequest / duration, waterRate = waterRequest / duration;
            double humans = 0, herds = 0, water = 0, spilled = 0, foodCapacityDays = 0, biomassCapacityDays = 0, waterCapacityDays = 0, damage = 0;
            foreach (var segment in LazyEcology.Segments(start, duration, w.Config.BaseDays))
            {
                var k = new EcologyCapacity(w, tile, v, segment.Season); double dt = segment.Days;
                double n0 = Math.Max(0, v.PotentialFood - v.Food), kn = Math.Max(0, k.Biomass - k.Food);
                var n = EcologyMath.Stock(n0, kn, k.BiomassRate, herdRate, dt);
                double firstDays = Math.Min(dt, n.DepletionDays);
                var first = EcologyMath.Stock(v.Food, k.Food, k.FoodRate, humanRate, firstDays);
                double spillRate = Math.Max(0, herdRate - kn * k.BiomassRate), remaining = dt - firstDays;
                var second = EcologyMath.Stock(first.Stock, k.Food, k.FoodRate, humanRate + spillRate, remaining);
                double flowingDays = Math.Min(remaining, second.DepletionDays);
                double secondHuman = humanRate * flowingDays + Math.Min(humanRate, k.Food * k.FoodRate) * Math.Max(0, remaining - flowingDays);
                double humanHarvest = first.Realized + secondHuman, herdSpill = Math.Max(0, second.Realized - secondHuman), herdHarvest = n.Realized + herdSpill;
                var hydration = EcologyMath.Stock(v.Freshwater, k.Water, k.WaterRate, waterRate, dt);
                v.Food = second.Stock; v.PotentialFood = v.Food + n.Stock; v.Freshwater = hydration.Stock;
                humans += humanHarvest; herds += herdHarvest; water += hydration.Realized; spilled += herdSpill;
                var b = result.Balance;
                b.BiomassRecharge += n.Recharge + first.Recharge + second.Recharge; b.BiomassHarvest += humanHarvest + herdHarvest; b.BiomassOverflow += n.Overflow + first.Overflow + second.Overflow;
                b.FoodRecharge += first.Recharge + second.Recharge; b.FoodHarvest += humanHarvest + herdSpill; b.FoodOverflow += first.Overflow + second.Overflow;
                b.WaterRecharge += hydration.Recharge; b.WaterHarvest += hydration.Realized; b.WaterOverflow += hydration.Overflow;
                foodCapacityDays += k.Food * dt; biomassCapacityDays += k.Biomass * dt; waterCapacityDays += k.Water * dt;
                // Utilization compares realized extraction with a conservative renewable flow.
                // Requests that failed, and ordinary drinking, contribute no damage.
                double sustainable = k.Biomass * k.BiomassRate * dt * w.Config.SustainableProductionFraction;
                double utilization = (humanHarvest + herdHarvest) / Math.Max(1, sustainable);
                double coefficient = tile.Aquatic || tile.WaterBody == WaterKind.Saltwater ? w.Config.AquaticUseDamagePerYear : w.Config.LandUseDamagePerYear;
                double impact = humanHarvest + herdHarvest <= 0 ? 1 : (humanHarvest * humanImpact + herdHarvest) / (humanHarvest + herdHarvest);
                double damagePerDay = coefficient * impact * Math.Pow(Math.Max(0, utilization - w.Config.SafeUseRatio), w.Config.PressureExponent) / w.Config.DaysPerYear;
                damage += damagePerDay * dt;
                LazyEcology.RecoverCondition(w.Config, tile, ref v, dt, damagePerDay); v.Days += dt;
            }
            foreach (var d in requests)
            {
                double f = Share(humans, d.Food, foodRequest), p = Share(herds, d.PotentialFood, herdRequest), q = Share(water, d.Freshwater, waterRequest);
                result.Transfers.Add(new Transfer { Tile = tileId, Entity = d.Entity, Kind = d.Kind, Food = f, PotentialFood = p, Freshwater = q });
                if (d.Food > 0) AddPressure(result, d, "Accessible food", d.Food, f, foodCapacityDays, result.Balance.FoodRecharge, Share(damage, f, humans + herds), 0);
                if (d.PotentialFood > 0) AddPressure(result, d, "Biomass", d.PotentialFood, p, biomassCapacityDays, result.Balance.BiomassRecharge, Share(damage, p, humans + herds), Share(spilled, p, herds));
                if (d.Freshwater > 0) AddPressure(result, d, "Freshwater", d.Freshwater, q, waterCapacityDays, result.Balance.WaterRecharge, 0, 0);
            }
            v.Days = start + duration; v.Saltwater = tile.Seasons[(int)Math.Floor(v.Days / w.Config.BaseDays) % 4].MaxSaltwater;
            var beforeClamp = v; LazyEcology.ClampCapacity(w, tile, ref v);
            result.Balance.BiomassOverflow += beforeClamp.PotentialFood - v.PotentialFood; result.Balance.FoodOverflow += beforeClamp.Food - v.Food; result.Balance.WaterOverflow += beforeClamp.Freshwater - v.Freshwater;
            result.Balance.EndingBiomass = v.PotentialFood; result.Balance.EndingFood = v.Food; result.Balance.EndingWater = v.Freshwater;
            result.View = v; return result;
        }
        private static double Share(double total, double claim, double claims) => claims <= 0 ? 0 : total * claim / claims;
        private static void AddPressure(TileEcologyResult result, Demand d, string pool, double request, double realized, double capacity, double recharge, double damage, double spill) =>
            result.Pressure.Add(new PressureEntry { Tile = d.Tile, Entity = d.Entity, Kind = d.Kind, Pool = pool, Requested = request, Realized = realized, Unmet = Math.Max(0, request - realized), CapacityDays = capacity, Recharge = recharge, ConditionDamage = damage, GatherableSpill = spill });
    }
}
