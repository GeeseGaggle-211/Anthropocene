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
        // Transient diagnostics: the managed source term may overflow the
        // bounded food pool, so it is not itself realized food recovery.
        public double WoodRemaining, WoodDays, ManagedRenewalInput, OperatingFraction;
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
            if (tile.Land && CultivationEconomy.Intensity(w.Config, v.CultivationCapital) >= 1)
                v.Food = v.PotentialFood;
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
            int foodEntity = -1, biomassEntity = -1, waterEntity = -1;
            bool contestedFood = false, contestedBiomass = false, contestedWater = false;
            for (int i = 0; i < requests.Length; i++)
            {
                var r = requests[i];
                foodRequest += r.Food;
                herdRequest += r.PotentialFood;
                waterRequest += r.Freshwater;
                humanImpactWeighted += r.Food * r.ImpactMultiplier;
                if (r.Food > 0)
                {
                    if (foodEntity == -1) foodEntity = r.Entity;
                    else if (foodEntity != r.Entity) contestedFood = true;
                }
                if (r.PotentialFood > 0)
                {
                    if (biomassEntity == -1) biomassEntity = r.Entity;
                    else if (biomassEntity != r.Entity) contestedBiomass = true;
                }
                if (r.Freshwater > 0)
                {
                    if (waterEntity == -1) waterEntity = r.Entity;
                    else if (waterEntity != r.Entity) contestedWater = true;
                }
                bool isHuman = r.Kind == "Cultivation" || r.Kind == "Gathering" || r.Kind == "Marine gathering" || r.Kind == "Aquatic gathering";
                if (isHuman)
                {
                    if (r.Kind == "Cultivation") farmClaim += r.Food;
                    else gatherClaim += r.Food;
                }
            }
            double humanImpact = foodRequest <= 0 ? 1 : humanImpactWeighted / foodRequest;
            double humanRate = duration > 0 ? foodRequest / duration : 0;
            double herdRate = duration > 0 ? herdRequest / duration : 0;
            double waterRate = duration > 0 ? waterRequest / duration : 0;
            // Land claims debit only the stock present at the resource boundary.
            // Aquatic food and freshwater retain their coupled horizon flows.
            // Damage is evaluated after realized receipts, so habitat loss
            // cannot retroactively erase the stock that caused it.
            var representative = LazyEcology.MeanView(w, tile, v, duration, 0,
                averageCultivationCapital, operatingFraction);
            var capacity = new HorizonEcologyCapacity(w, tile, representative, start, duration, operatingFraction);
            if (tile.Land)
            {
                result.ManagedRenewalInput = capacity.ManagedFlow * duration;
                result.OperatingFraction = operatingFraction ?? tile.CultivationOperatingFraction;
            }
            var flow = tile.Land
                ? ResolveLandOpeningFlow(v, capacity, duration, foodRequest, herdRequest, waterRate)
                : ResolveHorizonFlow(v, capacity, duration, humanRate, herdRate, waterRate);
            double humans = flow.Humans, herds = flow.Herds, water = flow.Water, spilled = flow.Spilled;
            // Each finite tile pool has one integrated harvest ledger. On a
            // contested tile, claimant IDs debit that ledger in stable order.
            double[] contestedFoodReceipts = contestedFood ? AllocateContestedPool(requests, humans, ClaimPool.Food) : null;
            double[] contestedBiomassReceipts = contestedBiomass ? AllocateContestedPool(requests, herds, ClaimPool.Biomass) : null;
            double[] contestedWaterReceipts = contestedWater ? AllocateContestedPool(requests, water, ClaimPool.Water) : null;
            if (contestedFood && humans > 0)
            {
                double awardedImpact = 0;
                for (int i = 0; i < requests.Length; i++)
                    awardedImpact += contestedFoodReceipts[i] * requests[i].ImpactMultiplier;
                humanImpact = awardedImpact / humans;
            }
            double foodCapacityDays = capacity.FoodCapacityDays;
            double biomassCapacityDays = capacity.BiomassCapacityDays;
            double waterCapacityDays = capacity.WaterCapacityDays;
            double damagePerDay = SeasonalDamageRate(w, tile, representative, start, duration,
                humans, herds, humanImpact, operatingFraction);
            double damage = damagePerDay * duration;
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
            LazyEcology.RecoverCondition(w, tile, ref v, duration, damagePerDay);
            if (tile.Land)
            {
                double depletedFood = v.Food, depletedBiomass = v.PotentialFood;
                LazyEcology.RecoverLandAfterHarvest(w, tile, ref v, start, duration,
                    averageCultivationCapital, endingCultivationCapital, operatingFraction);
                // These two ledgers include accessibility reclassification when
                // field capital changes. No recovered unit enters a receipt.
                double foodChange = v.Food - depletedFood;
                double biomassChange = v.PotentialFood - depletedBiomass;
                result.Balance.FoodRecharge = Math.Max(0, foodChange);
                result.Balance.FoodOverflow = Math.Max(0, -foodChange);
                result.Balance.BiomassRecharge = Math.Max(0, biomassChange);
                result.Balance.BiomassOverflow = Math.Max(0, -biomassChange);
            }
            else
                v.CultivationCapital = endingCultivationCapital ?? CultivationEconomy.Decay(w.Config,
                    beforeHorizon.CultivationCapital, operatingFraction ?? tile.CultivationOperatingFraction,
                    duration / w.Config.DaysPerYear);
            v.Days = start + duration;
            wood?.Recover(beforeHorizon, v, duration, damagePerDay);
            for (int i = 0; i < requests.Length; i++)
            {
                var d = requests[i];
                double f = contestedFood ? contestedFoodReceipts[i] : Share(humans, d.Food, foodRequest);
                double p = contestedBiomass ? contestedBiomassReceipts[i] : Share(herds, d.PotentialFood, herdRequest);
                double q = contestedWater ? contestedWaterReceipts[i] : Share(water, d.Freshwater, waterRequest);
                // Allocate this settlement's capped share to operating farmers first;
                // gatherers fill the remaining actual gap, never the pre-cap forecast.
                if (!contestedFood && (d.Kind == "Cultivation" || d.Kind == "Gathering" || d.Kind == "Marine gathering" || d.Kind == "Aquatic gathering"))
                {
                    double accepted = Share(humans, farmClaim + gatherClaim, foodRequest);
                    double farmAccepted = Math.Min(farmClaim, accepted);
                    f = d.Kind == "Cultivation" ? Share(farmAccepted, d.Food, farmClaim) : Share(Math.Max(0, accepted - farmAccepted), d.Food, gatherClaim);
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
            public readonly double Humans, Herds, Water, Spilled;
            public HorizonFlow(StockFlow nonfood, StockFlow firstFood, StockFlow secondFood, StockFlow hydration,
                double humans, double herds, double spilled)
            {
                Nonfood = nonfood; FirstFood = firstFood; SecondFood = secondFood; Hydration = hydration;
                Humans = humans; Herds = herds; Water = hydration.Realized; Spilled = spilled;
            }
        }
        private static HorizonFlow ResolveHorizonFlow(EcologyView start, HorizonEcologyCapacity capacity,
            double days, double humanRate, double herdRate, double waterRate)
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
            return new HorizonFlow(nonfood, firstFood, secondFood, hydration, humans, herds, spilled);
        }
        private static HorizonFlow ResolveLandOpeningFlow(EcologyView start, HorizonEcologyCapacity capacity,
            double days, double humanRequest, double herdRequest, double waterRate)
        {
            double biomass = Math.Max(0, start.PotentialFood);
            double food = Numbers.Clamp(start.Food, 0, biomass);
            double nonfood = biomass - food;
            double humans = Math.Min(Math.Max(0, humanRequest), food);
            double herdNonfood = Math.Min(Math.Max(0, herdRequest), nonfood);
            double spilled = Math.Min(Math.Max(0, herdRequest - herdNonfood), food - humans);
            var n = new StockFlow(nonfood - herdNonfood, herdNonfood,
                Math.Max(0, herdRequest - herdNonfood - spilled), 0, 0, double.PositiveInfinity);
            var first = new StockFlow(food - humans, humans,
                Math.Max(0, humanRequest - humans), 0, 0, double.PositiveInfinity);
            var second = new StockFlow(food - humans - spilled, spilled, 0, 0, 0,
                double.PositiveInfinity);
            var hydration = EcologyMath.Stock(start.Freshwater, capacity.Water,
                capacity.WaterRate, waterRate, days);
            return new HorizonFlow(n, first, second, hydration, humans,
                herdNonfood + spilled, spilled);
        }
        // A four-profile forcing reducer preserves the nonlinear overuse
        // penalty of a poor season without scheduling seasonal state writes.
        // The finite per-profile stock window bounds only this damage
        // estimator; actual horizon harvest is resolved by the stock solver.
        private static double SeasonalDamageRate(WorldState world, Tile tile, EcologyView representative,
            double start, double days, double humans, double herds, double humanImpact,
            double? operatingFraction)
        {
            double realized = humans + herds;
            if (days <= 0 || realized <= 0) return 0;
            double realizedRate = realized / days;
            double impact = (humans * humanImpact + herds) / realized;
            double coefficient = tile.Aquatic || tile.WaterBody == WaterKind.Saltwater
                ? world.Config.AquaticUseDamagePerYear : world.Config.LandUseDamagePerYear;
            var exposure = new SeasonExposure(start, days, world.Config.BaseDays);
            double weightedDamage = 0;
            for (int season = 0; season < 4; season++)
            {
                double weight = exposure.At(season) / days;
                if (weight <= 0) continue;
                var profile = new EcologyCapacity(world, tile, representative, season, operatingFraction);
                double safe = EcologyMath.SustainableHarvestRate(profile.Biomass,
                    profile.BiomassRate, world.Config.SustainableProductionFraction, profile.ManagedFlow);
                // With a single opening debit, land cannot use twenty years of
                // renewal to justify stripping the entire opening biomass.
                // The finite-stock bound matters on long turns; the renewal
                // bound still governs short turns and aquatic food is unchanged.
                if (tile.Land)
                    safe = Math.Min(safe, profile.Biomass
                        * world.Config.SustainableProductionFraction / days);
                double finiteRate = profile.Biomass / Math.Min(world.Config.BaseDays, days)
                    + profile.Biomass * profile.BiomassRate + profile.ManagedFlow;
                double seasonalRealized = Math.Min(realizedRate, finiteRate);
                if (seasonalRealized <= 0) continue;
                double utilization = seasonalRealized / Math.Max(SustainableRateEpsilon, safe);
                double safeUse = tile.Land ? world.Config.LandSafeUseRatio : world.Config.SafeUseRatio;
                weightedDamage += weight * Math.Pow(Math.Max(0, utilization - safeUse),
                    world.Config.PressureExponent);
            }
            return coefficient * impact * weightedDamage / world.Config.DaysPerYear;
        }
        private enum ClaimPool { Food, Biomass, Water }
        private static double Requested(Demand demand, ClaimPool pool) => pool == ClaimPool.Food
            ? demand.Food : pool == ClaimPool.Biomass ? demand.PotentialFood : demand.Freshwater;
        private static double[] AllocateContestedPool(Demand[] requests, double realized, ClaimPool pool)
        {
            var receipts = new double[requests.Length];
            double remaining = Math.Max(0, realized);
            for (int first = 0; first < requests.Length;)
            {
                int last = first + 1;
                while (last < requests.Length && requests[last].Entity == requests[first].Entity) last++;
                double farm = 0, other = 0;
                for (int i = first; i < last; i++)
                {
                    double requested = Requested(requests[i], pool);
                    if (pool == ClaimPool.Food && requests[i].Kind == "Cultivation") farm += requested;
                    else other += requested;
                }
                double accepted = Math.Min(remaining, farm + other);
                remaining = Math.Max(0, remaining - accepted);
                double farmAccepted = Math.Min(farm, accepted);
                double otherAccepted = Math.Max(0, accepted - farmAccepted);
                for (int i = first; i < last; i++)
                {
                    double requested = Requested(requests[i], pool);
                    receipts[i] = pool == ClaimPool.Food && requests[i].Kind == "Cultivation"
                        ? Share(farmAccepted, requested, farm)
                        : Share(otherAccepted, requested, other);
                }
                first = last;
            }
            return receipts;
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
