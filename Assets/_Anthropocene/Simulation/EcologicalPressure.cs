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
        public double Requested, Realized, Unmet, CapacityDays, Recharge, ConditionDamage, GatherableSpill, WoodCapacityLoss;
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
        public bool HasWoodSource;
        public double WoodRemaining, WoodDays;
        public List<Transfer> Transfers = new List<Transfer>();
        public List<PressureEntry> Pressure = new List<PressureEntry>();
        public EcologyBalance Balance;
    }
    public static class EcologicalPressure
    {
        // A numerical guard on a daily rate, never a minimum harvest quantity
        // per integration segment. Even a very low-flow tile can be overused.
        private const double SustainableRateEpsilon = 1e-12;
        // Instantaneous realized conversion is an integrated hazard in the same bounded
        // condition equation as extraction. Recovery is materialized before disturbance.
        public static void ApplyConversion(WorldState w, int tile, int entity, string kind, string pool, double requested, double realized, double pressure, double days, TurnRecord record, Dictionary<int, EcologyBalance> balanceIndex = null)
        {
            var t = w.Tiles[tile]; var view = LazyEcology.MaterializeEcology(w, tile, days);
            LazyEcology.RecoverCondition(w, t, ref view, 0, 0);
            view.LandCondition = EcologyMath.Condition(view.LandCondition, 1, 0, Math.Max(0, pressure), 1);
            view.CurrentVegetation = Vegetation.Update(t.NaturalVegetationPotential, view.CurrentVegetation, view.LandCondition, w.Config);
            var before = view; LazyEcology.ClampCapacity(w, t, ref view); view.Commit(t);
            double woodCapacityLoss = MaterialEconomy.ClampWoodSource(w, tile, view);
            if (balanceIndex == null && record != null) balanceIndex = MaterialEconomy.GetOrCreateBalanceIndex(record);
            EcologyBalance balance = null;
            if (balanceIndex != null) balanceIndex.TryGetValue(tile, out balance);
            else balance = record?.EcologyBalances?.LastOrDefault(b => b.Tile == tile);
            if (balance != null) { balance.BiomassOverflow += before.PotentialFood - view.PotentialFood; balance.FoodOverflow += before.Food - view.Food; balance.EndingBiomass = view.PotentialFood; balance.EndingFood = view.Food; }
            record.Pressure.Add(new PressureEntry { Tile = tile, Entity = entity, Kind = kind, Pool = pool, Requested = requested, Realized = realized, Unmet = Math.Max(0, requested - realized), ConditionDamage = pressure, WoodCapacityLoss = woodCapacityLoss });
        }
        // Inputs are read-only. Results can be merged by tile/entity ID after a worker barrier.
        public static TileEcologyResult Integrate(WorldState w, int tileId, IEnumerable<Demand> claims, double start, double duration,
            double? averageCultivationCapital = null, double? endingCultivationCapital = null, double? operatingFraction = null)
        {
            if (duration < 0) throw new ArgumentOutOfRangeException(nameof(duration));
            var tile = w.Tiles[tileId]; var wood = MaterialEconomy.ProjectWoodSource(w, tileId);
            var v = LazyEcology.ProjectEcology(w, tileId, start, true, wood == null ? null : wood.Recover);
            var result = new TileEcologyResult { Balance = new EcologyBalance { Tile = tileId, StartingBiomass = v.PotentialFood, StartingFood = v.Food, StartingWater = v.Freshwater } };
            var requests = CanonicalRequests(claims);
            if (duration == 0)
            {
                foreach (var claim in requests)
                {
                    result.Transfers.Add(new Transfer { Tile = tileId, Entity = claim.Entity, Kind = claim.Kind });
                    if (claim.Food > 0) AddPressure(result, claim, "Accessible food", claim.Food, 0, 0, 0, 0, 0);
                    if (claim.PotentialFood > 0) AddPressure(result, claim, "Biomass", claim.PotentialFood, 0, 0, 0, 0, 0);
                    if (claim.Freshwater > 0) AddPressure(result, claim, "Freshwater", claim.Freshwater, 0, 0, 0, 0, 0);
                }
                result.Balance.EndingBiomass = v.PotentialFood;
                result.Balance.EndingFood = v.Food;
                result.Balance.EndingWater = v.Freshwater;
                result.View = v;
                if (wood != null) { result.HasWoodSource = true; result.WoodRemaining = wood.Remaining; result.WoodDays = v.Days; }
                return result;
            }
            double foodRequest = 0, herdRequest = 0, waterRequest = 0, humanImpactWeighted = 0;
            double farmClaim = 0, gatherClaim = 0;
            bool singleEntity = true;
            int humanEntity = -1;
            for (int i = 0; i < requests.Length; i++)
            {
                var r = requests[i];
                foodRequest += r.Food;
                herdRequest += r.PotentialFood;
                waterRequest += r.Freshwater;
                humanImpactWeighted += r.Food * r.ImpactMultiplier;
                bool isHuman = r.Kind == "Cultivation" || r.Kind == "Gathering" || r.Kind == "Marine gathering" || r.Kind == "Aquatic gathering";
                if (isHuman)
                {
                    if (humanEntity == -1) humanEntity = r.Entity;
                    else if (humanEntity != r.Entity) singleEntity = false;
                    if (r.Kind == "Cultivation") farmClaim += r.Food;
                    else gatherClaim += r.Food;
                }
            }
            double humanImpact = foodRequest <= 0 ? 1 : humanImpactWeighted / foodRequest;
            double humanRate = duration > 0 ? foodRequest / duration : 0;
            double herdRate = duration > 0 ? herdRequest / duration : 0;
            double waterRate = duration > 0 ? waterRequest / duration : 0;
            // A bounded predictor/corrector accounts for pressure's effect on
            // average habitat support without creating subturn decisions.
            var representative = LazyEcology.MeanView(w, tile, v, duration, 0,
                averageCultivationCapital, operatingFraction);
            var capacity = new HorizonEcologyCapacity(w, tile, representative, start, duration, operatingFraction);
            var flow = ResolveHorizonFlow(w, tile, v, capacity, duration, humanRate, herdRate, waterRate, humanImpact);
            // With no renewable safe flow, all realized withdrawal is a debit
            // against opening stock. Let that debit cause pressure at the end;
            // feeding the resulting near-zero condition back into this same
            // horizon would erase the stock before it could be withdrawn.
            if (flow.DamagePerDay > 0 && capacity.SafeRate > SustainableRateEpsilon)
            {
                representative = LazyEcology.MeanView(w, tile, v, duration, flow.DamagePerDay,
                    averageCultivationCapital, operatingFraction);
                capacity = new HorizonEcologyCapacity(w, tile, representative, start, duration, operatingFraction);
                flow = ResolveHorizonFlow(w, tile, v, capacity, duration, humanRate, herdRate, waterRate, humanImpact);
            }
            double humans = flow.Humans, herds = flow.Herds, water = flow.Water, spilled = flow.Spilled;
            double foodCapacityDays = capacity.FoodCapacityDays;
            double biomassCapacityDays = capacity.BiomassCapacityDays;
            double waterCapacityDays = capacity.WaterCapacityDays;
            double damage = flow.DamagePerDay * duration;
            result.Balance.BiomassRecharge = flow.Nonfood.Recharge + flow.FirstFood.Recharge + flow.SecondFood.Recharge;
            result.Balance.BiomassHarvest = humans + herds;
            result.Balance.BiomassOverflow = flow.Nonfood.Overflow + flow.FirstFood.Overflow + flow.SecondFood.Overflow;
            result.Balance.FoodRecharge = flow.FirstFood.Recharge + flow.SecondFood.Recharge;
            result.Balance.FoodHarvest = humans + spilled;
            result.Balance.FoodOverflow = flow.FirstFood.Overflow + flow.SecondFood.Overflow;
            result.Balance.WaterRecharge = flow.Hydration.Recharge;
            result.Balance.WaterHarvest = water;
            result.Balance.WaterOverflow = flow.Hydration.Overflow;
            var beforeHorizon = v;
            v.Food = flow.SecondFood.Stock;
            v.PotentialFood = v.Food + flow.Nonfood.Stock;
            v.Freshwater = flow.Hydration.Stock;
            LazyEcology.RecoverCondition(w, tile, ref v, duration, flow.DamagePerDay);
            v.CultivationCapital = endingCultivationCapital ?? CultivationEconomy.Decay(w.Config,
                beforeHorizon.CultivationCapital, operatingFraction ?? tile.CultivationOperatingFraction,
                duration / w.Config.DaysPerYear);
            v.Days = start + duration;
            wood?.Recover(beforeHorizon, v, duration, flow.DamagePerDay);
            for (int i = 0; i < requests.Length; i++)
            {
                var d = requests[i];
                double f = Share(humans, d.Food, foodRequest), p = Share(herds, d.PotentialFood, herdRequest), q = Share(water, d.Freshwater, waterRequest);
                // Allocate this settlement's capped share to operating farmers first;
                // gatherers fill the remaining actual gap, never the pre-cap forecast.
                if (d.Kind == "Cultivation" || d.Kind == "Gathering" || d.Kind == "Marine gathering" || d.Kind == "Aquatic gathering")
                {
                    double curFarmClaim = farmClaim, curGatherClaim = gatherClaim;
                    if (!singleEntity)
                    {
                        curFarmClaim = 0; curGatherClaim = 0;
                        for (int j = 0; j < requests.Length; j++)
                        {
                            var x = requests[j];
                            if (x.Entity == d.Entity)
                            {
                                if (x.Kind == "Cultivation") curFarmClaim += x.Food;
                                else if (x.Kind == "Gathering" || x.Kind == "Marine gathering" || x.Kind == "Aquatic gathering") curGatherClaim += x.Food;
                            }
                        }
                    }
                    double accepted = Share(humans, curFarmClaim + curGatherClaim, foodRequest);
                    double farmAccepted = Math.Min(curFarmClaim, accepted);
                    f = d.Kind == "Cultivation" ? Share(farmAccepted, d.Food, curFarmClaim) : Share(Math.Max(0, accepted - farmAccepted), d.Food, curGatherClaim);
                }
                result.Transfers.Add(new Transfer { Tile = tileId, Entity = d.Entity, Kind = d.Kind, Food = f, PotentialFood = p, Freshwater = q });
                if (d.Food > 0) AddPressure(result, d, "Accessible food", d.Food, f, foodCapacityDays, result.Balance.FoodRecharge, Share(damage, f, humans + herds), 0);
                if (d.PotentialFood > 0) AddPressure(result, d, "Biomass", d.PotentialFood, p, biomassCapacityDays, result.Balance.BiomassRecharge, Share(damage, p, humans + herds), Share(spilled, p, herds));
                if (d.Freshwater > 0) AddPressure(result, d, "Freshwater", d.Freshwater, q, waterCapacityDays, result.Balance.WaterRecharge, 0, 0);
            }
            v.Days = start + duration; v.Saltwater = tile.Seasons[(int)Math.Floor(v.Days / w.Config.BaseDays) % 4].MaxSaltwater;
            var beforeClamp = v; LazyEcology.ClampCapacity(w, tile, ref v);
            result.Balance.BiomassOverflow += beforeClamp.PotentialFood - v.PotentialFood; result.Balance.FoodOverflow += beforeClamp.Food - v.Food; result.Balance.WaterOverflow += beforeClamp.Freshwater - v.Freshwater;
            result.Balance.EndingBiomass = v.PotentialFood; result.Balance.EndingFood = v.Food; result.Balance.EndingWater = v.Freshwater;
            result.View = v;
            double retainedBefore = EcologyMath.CultivationVegetationRetention(w.Config,
                CultivationEconomy.Intensity(w.Config, beforeHorizon.CultivationCapital));
            double retainedAfter = EcologyMath.CultivationVegetationRetention(w.Config,
                CultivationEconomy.Intensity(w.Config, v.CultivationCapital));
            double displaced = Math.Max(0, retainedBefore - retainedAfter);
            if (displaced > 1e-12)
            {
                double woodCapacityLoss = 0;
                if (wood != null)
                {
                    string sourceId = "surface:" + tileId + ":" + (int)MaterialId.Wood;
                    double initialWood = MaterialEconomy.SourceState(w, sourceId)?.Initial
                        ?? w.Resources?.SurfaceAccessible(tileId, MaterialId.Wood) ?? 0;
                    var oldField = v; oldField.CultivationCapital = beforeHorizon.CultivationCapital;
                    woodCapacityLoss = Math.Max(0,
                        MaterialEconomy.WoodCapacity(initialWood, tile, oldField, w.Config)
                        - MaterialEconomy.WoodCapacity(initialWood, tile, v, w.Config));
                }
                result.Pressure.Add(new PressureEntry
                {
                    Tile = tileId, Entity = tile.CultivationOperator,
                    Kind = "Cultivation vegetation displacement", Pool = "Vegetation cover",
                    Requested = displaced, Realized = displaced,
                    WoodCapacityLoss = woodCapacityLoss, ConditionDamage = 0
                });
            }
            if (wood != null) { result.HasWoodSource = true; result.WoodRemaining = wood.Remaining; result.WoodDays = v.Days; }
            return result;
        }
        private readonly struct HorizonFlow
        {
            public readonly StockFlow Nonfood, FirstFood, SecondFood, Hydration;
            public readonly double Humans, Herds, Water, Spilled, DamagePerDay;
            public HorizonFlow(StockFlow nonfood, StockFlow firstFood, StockFlow secondFood, StockFlow hydration,
                double humans, double herds, double spilled, double damagePerDay)
            {
                Nonfood = nonfood; FirstFood = firstFood; SecondFood = secondFood; Hydration = hydration;
                Humans = humans; Herds = herds; Water = hydration.Realized; Spilled = spilled; DamagePerDay = damagePerDay;
            }
        }
        private static HorizonFlow ResolveHorizonFlow(WorldState world, Tile tile, EcologyView start,
            HorizonEcologyCapacity capacity, double days, double humanRate, double herdRate,
            double waterRate, double humanImpact)
        {
            var nonfood = EcologyMath.Stock(Math.Max(0, start.PotentialFood - start.Food),
                capacity.Nonfood, capacity.BiomassRate, herdRate, days);
            double firstDays = Math.Min(days, nonfood.DepletionDays);
            var firstFood = EcologyMath.ManagedStock(start.Food, capacity.Food, capacity.FoodRate,
                capacity.ManagedFlow, humanRate, firstDays);
            double spillRate = Math.Max(0, herdRate - capacity.Nonfood * capacity.BiomassRate);
            double remaining = days - firstDays;
            var secondFood = EcologyMath.ManagedStock(firstFood.Stock, capacity.Food, capacity.FoodRate,
                capacity.ManagedFlow, humanRate + spillRate, remaining);
            double flowingDays = Math.Min(remaining, secondFood.DepletionDays);
            double humanInSecond = humanRate * flowingDays
                + Math.Min(humanRate, capacity.Food * capacity.FoodRate + capacity.ManagedFlow)
                * Math.Max(0, remaining - flowingDays);
            humanInSecond = Math.Min(secondFood.Realized, humanInSecond);
            double humans = firstFood.Realized + humanInSecond;
            double spilled = Math.Max(0, secondFood.Realized - humanInSecond);
            double herds = nonfood.Realized + spilled;
            var hydration = EcologyMath.Stock(start.Freshwater, capacity.Water, capacity.WaterRate, waterRate, days);
            double damage = 0;
            if (days > 0 && humans + herds > 0)
            {
                double utilization = (humans + herds) / days / Math.Max(SustainableRateEpsilon, capacity.SafeRate);
                double coefficient = tile.Aquatic || tile.WaterBody == WaterKind.Saltwater
                    ? world.Config.AquaticUseDamagePerYear : world.Config.LandUseDamagePerYear;
                double impact = (humans * humanImpact + herds) / (humans + herds);
                damage = coefficient * impact
                    * Math.Pow(Math.Max(0, utilization - world.Config.SafeUseRatio), world.Config.PressureExponent)
                    / world.Config.DaysPerYear;
            }
            return new HorizonFlow(nonfood, firstFood, secondFood, hydration, humans, herds, spilled, damage);
        }
        private static Demand[] CanonicalRequests(IEnumerable<Demand> claims)
        {
            if (claims is Demand[] array)
            {
                if (array.Length <= 1) return array;
                if (array.Length == 2)
                {
                    int cmp = array[0].Entity.CompareTo(array[1].Entity);
                    if (cmp == 0) cmp = string.CompareOrdinal(array[0].Kind, array[1].Kind);
                    if (cmp <= 0) return array;
                    return new Demand[] { array[1], array[0] };
                }
                bool sorted = true;
                for (int i = 1; i < array.Length; i++)
                {
                    int c = array[i - 1].Entity.CompareTo(array[i].Entity);
                    if (c == 0) c = string.CompareOrdinal(array[i - 1].Kind, array[i].Kind);
                    if (c > 0) { sorted = false; break; }
                }
                if (sorted) return array;
                return array.OrderBy(d => d.Entity).ThenBy(d => d.Kind, StringComparer.Ordinal).ToArray();
            }
            if (claims is ICollection<Demand> coll && coll.Count == 0) return Array.Empty<Demand>();
            return claims.OrderBy(d => d.Entity).ThenBy(d => d.Kind, StringComparer.Ordinal).ToArray();
        }
        private static double Share(double total, double claim, double claims) => claims <= 0 ? 0 : total * claim / claims;
        private static void AddPressure(TileEcologyResult result, Demand d, string pool, double request, double realized, double capacity, double recharge, double damage, double spill) =>
            result.Pressure.Add(new PressureEntry { Tile = d.Tile, Entity = d.Entity, Kind = d.Kind, Pool = pool, Requested = request, Realized = realized, Unmet = Math.Max(0, request - realized), CapacityDays = capacity, Recharge = recharge, ConditionDamage = damage, GatherableSpill = spill });
    }
}
