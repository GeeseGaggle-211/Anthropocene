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
        // Gross r*K forcing over the requested horizon, before finite-capacity
        // relaxation/clipping. Balance.*Recharge is the net retained stock
        // change after the land recovery kernel; it is not a gross source term.
        public double GrossNaturalFoodInput, GrossNaturalNonfoodInput;
        public List<Transfer> Transfers = new List<Transfer>();
        public List<PressureEntry> Pressure = new List<PressureEntry>();
        public EcologyBalance Balance;
    }
    public static class EcologicalPressure
    {
        // A numerical guard on a daily rate, never a minimum harvest quantity
        // per integration segment. Aquatic condition still responds to overuse.
        private const double SustainableRateEpsilon = 1e-12;
        // Wood extraction is a finite source debit. It does not disturb land
        // condition or reduce the source's biological carrying capacity.
        public static void RecordMaterialExtraction(int tile, int entity, string kind, string pool,
            double requested, double realized, TurnRecord record)
        {
            record.Pressure.Add(new PressureEntry { Tile = tile, Entity = entity, Kind = kind, Pool = pool,
                Requested = requested, Realized = realized, Unmet = Math.Max(0, requested - realized),
                ConditionDamage = 0, WoodCapacityLoss = 0 });
        }
        // Inputs are read-only. Results can be merged by tile/entity ID after a worker barrier.
        public static TileEcologyResult Integrate(WorldState w, int tileId, IEnumerable<Demand> claims, double start, double duration,
            double? averageCultivationCapital = null, double? endingCultivationCapital = null,
            double? operatingFraction = null, bool projectWood = true,
            Action profileEvaluated = null, EcologyView? openingEcology = null,
            bool captureGrossForcing = false,
            AquaticEcologyOverlay? aquaticOverlay = null, IrrigationOverlay? irrigationOverlay = null)
        {
            if (duration < 0) throw new ArgumentOutOfRangeException(nameof(duration));
            var tile = w.Tiles[tileId];
            bool annualLand = tile.Land && w.Config.AnnualLandFoodEnabled;
            aquaticOverlay ??= LazyEcology.ProjectAquaticOverlay(w, tileId, start,
                duration, operatingFraction);
            // Food-only previews use the same stock kernel without initializing
            // the map-wide material-source index for an unrelated Wood sidecar.
            var wood = projectWood ? MaterialEconomy.ProjectWoodSource(w, tileId) : null;
            // A read-only caller may provide the already projected opening
            // boundary when the live tile has since committed its endpoint.
            // This is essential for endpoint reconciliation against the locked
            // prior snapshot, without cloning or rewinding the world.
            var v = openingEcology ?? LazyEcology.ProjectEcology(w, tileId, start,
                true, wood == null ? null : wood.Recover);
            if (irrigationOverlay.HasValue) irrigationOverlay.Value.Apply(ref v);
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
                bool isHuman = r.Kind == "Cultivation" || r.Kind == "Gathering" ||
                    r.Kind == "Marine gathering" || r.Kind == "Aquatic gathering" ||
                    r.Kind == "FisheryHarvest";
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
            // Legacy land claims debit only opening stock. Annual land consumes
            // recurring crop flow before drawing on the slower reserve.
            // Aquatic food and freshwater retain their coupled horizon flows.
            // Aquatic pressure is evaluated after realized receipts. Land
            // condition is fixed; land-use effects remain finite stock debits.
            var landOpening = v;
            var representative = LazyEcology.MeanView(w, tile, v, duration, 0,
                averageCultivationCapital, operatingFraction);
            double openingCapital = v.CultivationCapital, openingAccessChange = 0;
            if (annualLand)
            {
                double beforeAccess = v.Food;
                LazyEcology.ReclassifyAnnualLandFood(w, tile, ref v, representative.CultivationCapital, profileEvaluated);
                openingAccessChange = v.Food - beforeAccess;
            }
            if (aquaticOverlay.HasValue && !tile.Land)
            {
                LazyEcology.ReclassifyAquaticFood(w, tile, ref v,
                    aquaticOverlay.Value.OpeningCapital,
                    aquaticOverlay.Value.AverageCapital,
                    (int)Math.Floor(start / w.Config.BaseDays) % 4);
                result.Balance.StartingFood = v.Food;
            }
            var capacity = new HorizonEcologyCapacity(w, tile, representative, start,
                duration, operatingFraction, aquaticOverlay, annualLand ? profileEvaluated : null);
            // Aquatic ecology uses this representative horizon capacity.
            // Legacy land recovery composes ordered seasonal profiles below;
            // the optional diagnostic callback records their exact forcing.
            if (!tile.Land || annualLand || !captureGrossForcing)
            {
                result.GrossNaturalFoodInput = capacity.Food * capacity.FoodRate * duration;
                result.GrossNaturalNonfoodInput = capacity.Nonfood * capacity.BiomassRate * duration;
            }
            if (tile.Land)
            {
                result.ManagedRenewalInput = capacity.ManagedFlow * duration;
                result.OperatingFraction = operatingFraction ?? tile.CultivationOperatingFraction;
            }
            else if (aquaticOverlay.HasValue)
            {
                result.ManagedRenewalInput = capacity.ManagedFlow * duration;
                result.OperatingFraction = aquaticOverlay.Value.OperatingFraction;
            }
            var flow = tile.Land && !annualLand
                ? ResolveLandOpeningFlow(v, capacity, duration, foodRequest, herdRequest, waterRate)
                : ResolveHorizonFlow(v, capacity, duration, humanRate, herdRate, waterRate, annualLand);
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
                humans, herds, humanImpact, operatingFraction, aquaticOverlay);
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
            var beforeHorizon = annualLand ? landOpening : v;
            v.Food = flow.SecondFood.Stock;
            v.PotentialFood = v.Food + flow.Nonfood.Stock;
            v.Freshwater = flow.Hydration.Stock;
            LazyEcology.RecoverCondition(w, tile, ref v, duration, damagePerDay);
            if (annualLand)
            {
                double beforeAccess = v.Food;
                LazyEcology.ReclassifyAnnualLandFood(w, tile, ref v,
                    endingCultivationCapital ?? CultivationEconomy.Decay(w.Config,
                        openingCapital, operatingFraction ?? tile.CultivationOperatingFraction,
                        duration / w.Config.DaysPerYear), profileEvaluated);
                double endingAccessChange = v.Food - beforeAccess;
                result.Balance.FoodRecharge += Math.Max(0, openingAccessChange) + Math.Max(0, endingAccessChange);
                result.Balance.FoodOverflow += Math.Max(0, -openingAccessChange) + Math.Max(0, -endingAccessChange);
            }
            else if (tile.Land)
            {
                double depletedFood = v.Food, depletedBiomass = v.PotentialFood;
                // With no drinking claim, use the same ordered zero-withdrawal
                // water transition as a latent land tile.
                bool recoverWater = waterRequest <= 0;
                if (recoverWater) v.Freshwater = beforeHorizon.Freshwater;
                Action<EcologyCapacity, double> forcingProfile = null;
                if (captureGrossForcing)
                {
                    result.ManagedRenewalInput = 0;
                    forcingProfile = (profile, spanDays) =>
                    {
                        result.GrossNaturalFoodInput += profile.Food * profile.FoodRate * spanDays;
                        result.GrossNaturalNonfoodInput += Math.Max(0,
                            profile.Biomass - profile.Food) * profile.BiomassRate * spanDays;
                        result.ManagedRenewalInput += profile.ManagedFlow * spanDays;
                    };
                }
                LazyEcology.RecoverLandAfterHarvest(w, tile, ref v, start, duration,
                    averageCultivationCapital, endingCultivationCapital, operatingFraction,
                    recoverWater, profileEvaluated, forcingProfile);
                // These two ledgers include accessibility reclassification when
                // field capital changes. No recovered unit enters a receipt.
                double foodChange = v.Food - depletedFood;
                double biomassChange = v.PotentialFood - depletedBiomass;
                result.Balance.FoodRecharge = Math.Max(0, foodChange);
                result.Balance.FoodOverflow = Math.Max(0, -foodChange);
                result.Balance.BiomassRecharge = Math.Max(0, biomassChange);
                result.Balance.BiomassOverflow = Math.Max(0, -biomassChange);
                if (recoverWater)
                {
                    double waterChange = v.Freshwater - beforeHorizon.Freshwater;
                    result.Balance.WaterRecharge = Math.Max(0, waterChange);
                    result.Balance.WaterOverflow = Math.Max(0, -waterChange);
                }
            }
            else
                v.CultivationCapital = endingCultivationCapital ?? CultivationEconomy.Decay(w.Config,
                    beforeHorizon.CultivationCapital, operatingFraction ?? tile.CultivationOperatingFraction,
                    duration / w.Config.DaysPerYear);
            v.Days = start + duration;
            if (!tile.Land && aquaticOverlay.HasValue)
            {
                double foodBeforeAccessChange = v.Food;
                LazyEcology.ReclassifyAquaticFood(w, tile, ref v,
                    aquaticOverlay.Value.AverageCapital,
                    aquaticOverlay.Value.EndingCapital,
                    (int)Math.Floor(v.Days / w.Config.BaseDays) % 4);
                double accessChange = v.Food - foodBeforeAccessChange;
                if (accessChange > 0) result.Balance.FoodRecharge += accessChange;
                else result.Balance.FoodOverflow += -accessChange;
            }
            wood?.Recover(beforeHorizon, v, duration);
            for (int i = 0; i < requests.Length; i++)
            {
                var d = requests[i];
                double f = contestedFood ? contestedFoodReceipts[i] : Share(humans, d.Food, foodRequest);
                double p = contestedBiomass ? contestedBiomassReceipts[i] : Share(herds, d.PotentialFood, herdRequest);
                double q = contestedWater ? contestedWaterReceipts[i] : Share(water, d.Freshwater, waterRequest);
                // Allocate this settlement's capped share to operating farmers first;
                // gatherers fill the remaining actual gap, never the pre-cap forecast.
                if (!contestedFood && (d.Kind == "Cultivation" || d.Kind == "Gathering" ||
                    d.Kind == "Marine gathering" || d.Kind == "Aquatic gathering" ||
                    d.Kind == "FisheryHarvest"))
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
            var beforeClamp = v;
            LazyEcology.ClampCapacity(w, tile, ref v,
                LazyEcology.EndpointAquaticOverlay(aquaticOverlay));
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
            double days, double humanRate, double herdRate, double waterRate, bool annualLand = false)
        {
            var nonfood = EcologyMath.Stock(Math.Max(0, start.PotentialFood - start.Food),
                capacity.Nonfood, capacity.BiomassRate, herdRate, days);
            double firstDays = Math.Min(days, nonfood.DepletionDays);
            var firstFood = annualLand
                ? EcologyMath.RenewableHarvest(start.Food, capacity.Food, capacity.FoodRate,
                    capacity.ManagedFlow, humanRate, firstDays)
                : EcologyMath.ManagedStock(start.Food, capacity.Food, capacity.FoodRate,
                    capacity.ManagedFlow, humanRate, firstDays);
            double spillRate = Math.Max(0, herdRate - capacity.Nonfood * capacity.BiomassRate);
            double remaining = days - firstDays;
            var secondFood = annualLand
                ? EcologyMath.RenewableHarvest(firstFood.Stock, capacity.Food, capacity.FoodRate,
                    capacity.ManagedFlow, humanRate + spillRate, remaining)
                : EcologyMath.ManagedStock(firstFood.Stock, capacity.Food, capacity.FoodRate,
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
        // A four-profile forcing reducer preserves aquatic overuse pressure
        // without scheduling seasonal state writes. Land use changes only
        // biomass stocks, never biological land condition.
        // The finite per-profile stock window bounds only this damage
        // estimator; actual horizon harvest is resolved by the stock solver.
        private static double SeasonalDamageRate(WorldState world, Tile tile, EcologyView representative,
            double start, double days, double humans, double herds, double humanImpact,
            double? operatingFraction, AquaticEcologyOverlay? aquaticOverlay)
        {
            double realized = humans + herds;
            if (tile.Land || days <= 0 || realized <= 0) return 0;
            double realizedRate = realized / days;
            double impact = (humans * humanImpact + herds) / realized;
            double coefficient = world.Config.AquaticUseDamagePerYear;
            var exposure = new SeasonExposure(start, days, world.Config.BaseDays);
            double weightedDamage = 0;

            // Keep the old one-horizon rate comparison for saves and worlds
            // which have not opted into the aquatic-pressure correction.
            if (!world.Config.CorrectedAquaticSeasonalPressureEnabled)
            {
                for (int season = 0; season < 4; season++)
                {
                    double weight = exposure.At(season) / days;
                    if (weight <= 0) continue;
                    var profile = new EcologyCapacity(world, tile, representative, season, operatingFraction, aquaticOverlay);
                    double safe = EcologyMath.SustainableHarvestRate(profile.Biomass,
                        profile.BiomassRate, world.Config.SustainableProductionFraction, profile.ManagedFlow);
                    double finiteRate = profile.Biomass / Math.Min(world.Config.BaseDays, days)
                        + profile.Biomass * profile.BiomassRate + profile.ManagedFlow;
                    double seasonalRealized = Math.Min(realizedRate, finiteRate);
                    if (seasonalRealized <= 0) continue;
                    double utilization = seasonalRealized / Math.Max(SustainableRateEpsilon, safe);
                    weightedDamage += weight * Math.Pow(Math.Max(0, utilization - world.Config.SafeUseRatio),
                        world.Config.PressureExponent);
                }
                return coefficient * impact * weightedDamage / world.Config.DaysPerYear;
            }

            var exposureDays = new double[4];
            var safeRates = new double[4];
            var finiteRateCeilings = new double[4];
            for (int season = 0; season < 4; season++)
            {
                double seasonDays = exposure.At(season);
                if (seasonDays <= 0) continue;
                var profile = new EcologyCapacity(world, tile, representative, season, operatingFraction, aquaticOverlay);
                exposureDays[season] = seasonDays;
                safeRates[season] = EcologyMath.SustainableHarvestRate(profile.Biomass,
                    profile.BiomassRate, world.Config.SustainableProductionFraction, profile.ManagedFlow);
                finiteRateCeilings[season] = profile.Biomass / Math.Min(world.Config.BaseDays, days)
                    + profile.Biomass * profile.BiomassRate + profile.ManagedFlow;
            }

            var seasonalRates = EcologyMath.AllocateCappedSeasonalRates(realized,
                exposureDays, finiteRateCeilings);
            double conservedHarvest = 0;
            for (int season = 0; season < 4; season++)
            {
                double seasonDays = exposureDays[season];
                double seasonalRealized = seasonalRates[season];
                conservedHarvest += seasonDays * seasonalRealized;
                if (seasonDays <= 0 || seasonalRealized <= 0) continue;
                double utilization = seasonalRealized / Math.Max(SustainableRateEpsilon, safeRates[season]);
                weightedDamage += (seasonDays / days) * Math.Pow(Math.Max(0, utilization - world.Config.SafeUseRatio),
                    world.Config.PressureExponent);
            }
            if (Math.Abs(conservedHarvest - realized) > 1e-10 * Math.Max(1, realized))
                throw new InvalidOperationException("Seasonal aquatic pressure allocation did not conserve the realized horizon harvest.");
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
