using System;
using System.Collections.Generic;

namespace Anthropocene.Simulation
{
    // Aquatic fishery management is a turn-horizon overlay. The sparse physical
    // capital remains in FisherySite; these values are fixed inputs to one
    // integration and never become dense tile state.
    public readonly struct AquaticEcologyOverlay
    {
        public readonly double OpeningCapital, AverageCapital, EndingCapital, OperatingFraction;

        public AquaticEcologyOverlay(double openingCapital, double averageCapital,
            double endingCapital, double operatingFraction)
        {
            OpeningCapital = Math.Max(0, openingCapital);
            AverageCapital = Math.Max(0, averageCapital);
            EndingCapital = Math.Max(0, endingCapital);
            OperatingFraction = Numbers.Clamp(operatingFraction, 0, 1);
        }
    }

    public struct EcologyView
    {
        public double Food, PotentialFood, Freshwater, Saltwater, LandCondition, AquaticCondition, Days, CultivationCapital;
        public double IrrigationWater, IrrigationSoil, IrrigationArea;
        public VegetationTier CurrentVegetation;
        public EcologyView(Tile t) { IrrigationWater = IrrigationSoil = IrrigationArea = 0; Food = t.Food; PotentialFood = t.PotentialFood; Freshwater = t.Freshwater; Saltwater = t.Saltwater; LandCondition = t.LandCondition; AquaticCondition = t.AquaticCondition; Days = t.LastEcologyUpdateDays; CurrentVegetation = t.CurrentVegetation; CultivationCapital = t.CultivationCapital; }
        public void Commit(Tile t) { t.Food = Food; t.PotentialFood = PotentialFood; t.Freshwater = Freshwater; t.Saltwater = Saltwater; t.LandCondition = LandCondition; t.AquaticCondition = AquaticCondition; t.CurrentVegetation = CurrentVegetation; t.LastEcologyUpdateDays = Days; t.CultivationCapital = CultivationCapital; }
    }
    public readonly struct EcologyCapacity
    {
        public readonly double Food, WildFood, ManagedFlow, Biomass, Water, FoodRate, BiomassRate, WaterRate;
        public EcologyCapacity(WorldState w, Tile t, EcologyView view, int season,
            double? operatingFraction = null, AquaticEcologyOverlay? aquaticOverlay = null)
        {
            var s = t.Seasons[season]; double multiplier = OpeningTransitions.FoodMultiplier(w, t);
            double condition = t.Aquatic || t.WaterBody == WaterKind.Saltwater ? view.AquaticCondition : view.LandCondition;
            Biomass = s.MaxPotentialFood * multiplier * condition;
            double ecologicalWildFood = Math.Min(Biomass, s.MaxFood * multiplier * condition * FoodEcology.GatherableFraction(t, w.Config, view.CurrentVegetation) / FoodEcology.GatherableFraction(t, w.Config, t.NaturalVegetationPotential));
            double q = CultivationEconomy.Intensity(w.Config, view.CultivationCapital);
            WildFood = EcologyMath.RetainedWildFoodCapacity(ecologicalWildFood, q, w.Config);
            Food = EcologyMath.EffectiveWildFoodCapacity(ecologicalWildFood, Biomass, q, w.Config, s.Moisture);
            ManagedFlow = EcologyMath.ManagedRegenerationFlow(w.Config, q, Biomass, s.BiomassRecoveryPerDay, operatingFraction ?? t.CultivationOperatingFraction, s.Moisture);
            if (aquaticOverlay.HasValue && t.Aquatic &&
                (t.Environment == EnvironmentKind.Shallows || t.Environment == EnvironmentKind.Lake))
            {
                double fisheryIntensity = CultivationEconomy.Intensity(w.Config,
                    aquaticOverlay.Value.AverageCapital);
                // Fishing changes only the human-accessible fraction of B. It
                // does not increase B or use any terrestrial crop response.
                Food += Math.Max(0, Biomass - Food) * fisheryIntensity * fisheryIntensity;
                ManagedFlow = Math.Max(0, w.Config.CultivationRegenFlux) * fisheryIntensity *
                    Math.Max(0, Biomass) * Math.Max(0, s.BiomassRecoveryPerDay) *
                    aquaticOverlay.Value.OperatingFraction;
            }
            Water = t.WaterBody == WaterKind.Saltwater || t.FreshwaterSource == FreshwaterSource.None ? 0 : s.MaxFreshwater;
            FoodRate = s.FoodRecoveryPerDay; BiomassRate = s.BiomassRecoveryPerDay; WaterRate = s.WaterRecoveryPerDay;
            if (t.Land && w.Config.Agriculture?.IrrigationEnabled == true && view.IrrigationArea > 0 && q > 0)
            {
                var settings = w.Config.Agriculture;
                double area = Numbers.Clamp(view.IrrigationArea, 0, 1);
                var watered = s;
                watered.Moisture = (float)(s.Moisture + view.IrrigationWater * Math.Max(0, settings.TargetMoisture - s.Moisture));
                double waterBiomass = s.Moisture > 1e-9 ? Biomass * watered.Moisture / s.Moisture :
                    FoodEcology.LandBiomassBase * w.Config.LandBiomassMultiplier *
                    FoodEcology.LandProductivity(t, watered, w.Config) * multiplier * condition;
                // A persistent soil advantage survives drought, but B_water
                // remains constrained by actual current moisture.
                double improved = waterBiomass * (1 + settings.SoilCapacityGain * view.IrrigationSoil);
                Biomass = (1 - area) * Biomass + area * improved;
                double warmth = Numbers.Clamp((s.Temperature + 8) / 30.0, .015, 1);
                double fraction = w.Config.LandFoodRegenerationInitialized ? w.Config.LandFoodRegeneration : w.Config.FoodRegeneration;
                double waterRate = EcologyMath.DailyRate(fraction * (.2 + watered.Moisture) * warmth, w.Config.BaseDays);
                double naturalRate = EcologyMath.DailyRate(fraction * (.2 + s.Moisture) * warmth, w.Config.BaseDays);
                double rateGain = Math.Max(0, waterRate - naturalRate);
                FoodRate += area * rateGain;
                BiomassRate += area * rateGain;
                waterRate = s.BiomassRecoveryPerDay + rateGain;
                double managedFood = EcologyMath.EffectiveWildFoodCapacity(ecologicalWildFood, improved, q, w.Config, watered.Moisture);
                Food = Math.Min(Biomass, (1 - area) * Food + area * managedFood);
                ManagedFlow = (1 - area) * ManagedFlow + area * EcologyMath.ManagedRegenerationFlow(w.Config,
                    q, improved, waterRate, operatingFraction ?? t.CultivationOperatingFraction, watered.Moisture);
            }

        }
    }
    // Four fixed climate profiles remain forcing data. Their exposure can be
    // reduced without visiting the 4 * years seasons of a player turn.
    internal readonly struct SeasonExposure
    {
        private readonly double first, second, third, fourth;
        private static double Cumulative(double time, double baseDays, int season)
        {
            double cycle = 4 * baseDays;
            double cycles = Math.Floor(time / cycle);
            double remainder = time - cycles * cycle;
            return cycles * baseDays + Numbers.Clamp(remainder - season * baseDays, 0, baseDays);
        }
        public SeasonExposure(double start, double days, double baseDays)
        {
            if (baseDays <= 0 || days < 0) throw new ArgumentOutOfRangeException(days < 0 ? nameof(days) : nameof(baseDays));
            first = Cumulative(start + days, baseDays, 0) - Cumulative(start, baseDays, 0);
            second = Cumulative(start + days, baseDays, 1) - Cumulative(start, baseDays, 1);
            third = Cumulative(start + days, baseDays, 2) - Cumulative(start, baseDays, 2);
            fourth = Cumulative(start + days, baseDays, 3) - Cumulative(start, baseDays, 3);
        }
        public double At(int season) => season == 0 ? first : season == 1 ? second : season == 2 ? third : fourth;
    }
    // A weighted generator for the bounded stock equations. Capacities use
    // rate-weighted forcing so r*K is the exact mean source term for the four
    // climate profiles. EcologyMath.Stock then solves that generator over dt.
    internal readonly struct HorizonEcologyCapacity
    {
        public readonly double Food, Nonfood, Water, FoodRate, BiomassRate, WaterRate, ManagedFlow, SafeRate;
        public readonly double FoodCapacityDays, BiomassCapacityDays, WaterCapacityDays;
        public HorizonEcologyCapacity(WorldState world, Tile tile, EcologyView representative,
            double start, double days, double? operatingFraction = null,
            AquaticEcologyOverlay? aquaticOverlay = null)
        {
            double foodDays = 0, nonfoodDays = 0, waterDays = 0;
            double foodRate = 0, biomassRate = 0, waterRate = 0;
            double foodSource = 0, nonfoodSource = 0, waterSource = 0, managed = 0, safe = 0;
            double biomassDays = 0;
            var exposure = new SeasonExposure(start, days, world.Config.BaseDays);
            for (int season = 0; season < 4; season++)
            {
                double weight = days > 0 ? exposure.At(season) / days : 0;
                if (weight <= 0) continue;
                var capacity = new EcologyCapacity(world, tile, representative, season,
                    operatingFraction, aquaticOverlay);
                double nonfood = Math.Max(0, capacity.Biomass - capacity.Food);
                foodDays += exposure.At(season) * capacity.Food;
                nonfoodDays += exposure.At(season) * nonfood;
                biomassDays += exposure.At(season) * capacity.Biomass;
                waterDays += exposure.At(season) * capacity.Water;
                foodRate += weight * capacity.FoodRate;
                biomassRate += weight * capacity.BiomassRate;
                waterRate += weight * capacity.WaterRate;
                foodSource += weight * capacity.FoodRate * capacity.Food;
                nonfoodSource += weight * capacity.BiomassRate * nonfood;
                waterSource += weight * capacity.WaterRate * capacity.Water;
                managed += weight * capacity.ManagedFlow;
                safe += weight * EcologyMath.SustainableHarvestRate(capacity.Biomass, capacity.BiomassRate,
                    world.Config.SustainableProductionFraction, capacity.ManagedFlow);
            }
            FoodCapacityDays = foodDays; BiomassCapacityDays = biomassDays; WaterCapacityDays = waterDays;
            FoodRate = foodRate; BiomassRate = biomassRate; WaterRate = waterRate;
            Food = foodRate > 0 ? foodSource / foodRate : days > 0 ? foodDays / days : 0;
            Nonfood = biomassRate > 0 ? nonfoodSource / biomassRate : days > 0 ? nonfoodDays / days : 0;
            Water = waterRate > 0 ? waterSource / waterRate : days > 0 ? waterDays / days : 0;
            ManagedFlow = managed; SafeRate = safe;
        }
    }
    public static class LazyEcology
    {
        internal static AquaticEcologyOverlay? EndpointAquaticOverlay(
            AquaticEcologyOverlay? overlay)
            => overlay.HasValue
                ? new AquaticEcologyOverlay(overlay.Value.EndingCapital,
                    overlay.Value.EndingCapital, overlay.Value.EndingCapital,
                    overlay.Value.OperatingFraction)
                : (AquaticEcologyOverlay?)null;

        internal static AquaticEcologyOverlay? ProjectAquaticOverlay(WorldState world,
            int tileId, double startDays, double durationDays,
            double? operatingFraction = null)
        {
            if (world == null || !world.Config.NeolithicFishingEnabled ||
                tileId < 0 || tileId >= world.Tiles.Length) return null;
            var tile = world.Tiles[tileId];
            if (!tile.Aquatic || (tile.Environment != EnvironmentKind.Shallows &&
                tile.Environment != EnvironmentKind.Lake)) return null;
            var sites = world.FisherySites;
            if (sites == null || sites.Count == 0) return null;
            int low = 0, high = sites.Count - 1;
            FisherySite site = null;
            while (low <= high)
            {
                int middle = low + ((high - low) >> 1);
                int compare = sites[middle].TileId.CompareTo(tileId);
                if (compare == 0) { site = sites[middle]; break; }
                if (compare < 0) low = middle + 1;
                else high = middle - 1;
            }
            if (site == null || site.Capital <= 0 || startDays + 1e-8 < site.LastUpdatedDays ||
                durationDays < 0) return null;
            double opening = NeolithicCapital.Decay(world.Config, site.Capital, 0,
                Math.Max(0, startDays - site.LastUpdatedDays) / world.Config.DaysPerYear);
            double operation = Numbers.Clamp(operatingFraction ?? 0, 0, 1);
            double years = durationDays / world.Config.DaysPerYear;
            double average = NeolithicCapital.MeanDecayedCapital(world.Config, opening,
                operation, years);
            double ending = NeolithicCapital.Decay(world.Config, opening, operation, years);
            return new AquaticEcologyOverlay(opening, average, ending, operation);
        }

        public static double[] SeasonWeights(double start, double days, double baseDays)
        {
            var exposure = new SeasonExposure(start, days, baseDays);
            return new[] { exposure.At(0) / baseDays, exposure.At(1) / baseDays,
                exposure.At(2) / baseDays, exposure.At(3) / baseDays };
        }
        // Representative state is the exact time mean for aquatic condition
        // recovery and capital decay, with a single vegetation classification.
        // It is transient forcing data, never an authoritative mid-turn write.
        internal static EcologyView MeanView(WorldState w, Tile tile, EcologyView start, double days,
            double damagePerDay = 0, double? averageCapital = null, double? operatingFraction = null)
        {
            var mean = start;
            bool aquatic = tile.Aquatic || tile.WaterBody == WaterKind.Saltwater;
            bool plankton = aquatic && (w.Generation?.GeneratorVersion ?? WorldState.GeneratorVersion) >= 11;
            double recovery = LandConditionRecoveryPerDay(w, tile);
            mean.LandCondition = start.LandCondition;
            mean.AquaticCondition = EcologyMath.MeanCondition(start.AquaticCondition, 1,
                plankton ? AquaticEcology.ConditionRecoveryPerDay(tile) : recovery,
                aquatic ? damagePerDay : 0, days);
            double decay = Math.Log(2) / w.Config.Neolithic.DecayHalfLifeYears
                * (1 - Numbers.Clamp(operatingFraction ?? tile.CultivationOperatingFraction, 0, 1)) / w.Config.DaysPerYear;
            mean.CultivationCapital = averageCapital ?? start.CultivationCapital * EcologyMath.MeanExponential(decay, days);
            mean.CurrentVegetation = Vegetation.Update(tile.NaturalVegetationPotential, start.CurrentVegetation,
                plankton ? mean.AquaticCondition : mean.LandCondition, w.Config);
            return mean;
        }
        private static EcologyView PassiveResponseView(WorldState w, Tile tile, EcologyView start, double days)
        {
            var response = MeanView(w, tile, start, days);
            var exposure = new SeasonExposure(start.Days, days, w.Config.BaseDays);
            double stockRate = 0;
            for (int season = 0; season < 4; season++)
            {
                var climate = tile.Seasons[season];
                stockRate += exposure.At(season) / days
                    * Math.Max(climate.FoodRecoveryPerDay, climate.BiomassRecoveryPerDay);
            }
            bool aquatic = tile.Aquatic || tile.WaterBody == WaterKind.Saltwater;
            bool plankton = aquatic && (w.Generation?.GeneratorVersion ?? WorldState.GeneratorVersion) >= 11;
            double landRecovery = LandConditionRecoveryPerDay(w, tile);
            response.LandCondition = start.LandCondition;
            response.AquaticCondition = EcologyMath.ResponseCondition(start.AquaticCondition, 1,
                plankton ? AquaticEcology.ConditionRecoveryPerDay(tile) : landRecovery,
                0, stockRate, days);
            response.CurrentVegetation = Vegetation.Update(tile.NaturalVegetationPotential,
                start.CurrentVegetation, plankton ? response.AquaticCondition : response.LandCondition, w.Config);
            return response;
        }
        // `recovered` keeps a pure companion Wood projection on the same one-way
        // horizon. It observes only start and endpoint; no seasonal callbacks.
        internal static EcologyView ProjectEcology(WorldState w, int tile, double queryDays,
            bool composeCycles, Action<EcologyView, EcologyView, double> recovered,
            AquaticEcologyOverlay? aquaticOverlay = null)
        {
            var t = w.Tiles[tile]; var v = new EcologyView(t);
            IrrigationEconomy.RestoreOverlay(w, tile, ref v, queryDays);
            if (queryDays < v.Days - 1e-8) throw new ArgumentOutOfRangeException(nameof(queryDays), "Cannot project before stored ecology.");
            double days = Math.Max(0, queryDays - v.Days);
            var overlay = aquaticOverlay ?? ProjectAquaticOverlay(w, tile, v.Days, days);
            var before = v;
            if (days > 0)
            {
                if (t.Land)
                {
                    RecoverCondition(w, t, ref v, days, 0);
                    RecoverLandAfterHarvest(w, t, ref v, before.Days, days,
                        null, null, null, true);
                }
                else
                {
                    bool quiet = !overlay.HasValue && before.CultivationCapital == 0 && before.LandCondition == 1
                        && before.AquaticCondition == 1 && before.CurrentVegetation == t.NaturalVegetationPotential;
                    if (quiet) ProjectQuiet(w, t, ref v, days);
                    else
                    {
                        var response = PassiveResponseView(w, t, before, days);
                        var capacity = new HorizonEcologyCapacity(w, t, response, before.Days, days,
                            aquaticOverlay: overlay);
                        if (overlay.HasValue)
                            ReclassifyAquaticFood(w, t, ref v, overlay.Value.OpeningCapital,
                                overlay.Value.AverageCapital, (int)Math.Floor(before.Days / w.Config.BaseDays) % 4);
                        double nonfood = EcologyMath.Stock(Math.Max(0, v.PotentialFood - v.Food),
                            capacity.Nonfood, capacity.BiomassRate, 0, days).Stock;
                        v.Food = overlay.HasValue
                            ? EcologyMath.ManagedStock(v.Food, capacity.Food, capacity.FoodRate,
                                capacity.ManagedFlow, 0, days).Stock
                            : EcologyMath.Stock(v.Food, capacity.Food, capacity.FoodRate, 0, days).Stock;
                        v.PotentialFood = v.Food + nonfood;
                        v.Freshwater = EcologyMath.Stock(before.Freshwater, capacity.Water, capacity.WaterRate, 0, days).Stock;
                        RecoverCondition(w, t, ref v, days, 0);
                        v.CultivationCapital = CultivationEconomy.Decay(w.Config, before.CultivationCapital,
                            t.CultivationOperatingFraction, days / w.Config.DaysPerYear);
                        if (overlay.HasValue)
                            ReclassifyAquaticFood(w, t, ref v, overlay.Value.AverageCapital,
                                overlay.Value.EndingCapital,
                                (int)Math.Floor(queryDays / w.Config.BaseDays) % 4);
                    }
                }
            }
            v.Days = queryDays; v.Saltwater = t.Seasons[(int)Math.Floor(queryDays / w.Config.BaseDays) % 4].MaxSaltwater;
            // Endpoint accessible capacity is set by endpoint capital, not the
            // horizon-average capital used for renewal and harvest integration.
            ClampCapacity(w, t, ref v, EndpointAquaticOverlay(overlay));
            if (days > 0) recovered?.Invoke(before, v, days);
            return v;
        }
        public static EcologyView ProjectEcology(WorldState w, int tile, double queryDays,
            bool composeCycles = true) => ProjectEcology(w, tile, queryDays, composeCycles, null);
        // Undisturbed seasonal recovery is exactly composable: at most one
        // partial cycle on each edge and four maps for any number of full years.
        private static void ProjectQuiet(WorldState w, Tile tile, ref EcologyView view, double days)
        {
            double baseDays = w.Config.BaseDays, cycleDays = 4 * baseDays;
            double phase = view.Days - Math.Floor(view.Days / cycleDays) * cycleDays;
            double first = phase <= 1e-9 ? 0 : Math.Min(days, cycleDays - phase);
            if (first > 0) AdvanceQuietEdge(w, tile, ref view, first);
            double remaining = days - first;
            long cycles = (long)Math.Floor(remaining / cycleDays + 1e-10);
            if (cycles > 0)
            {
                var food = SeasonalAffine.Identity;
                var nonfood = SeasonalAffine.Identity;
                var water = SeasonalAffine.Identity;
                for (int season = 0; season < 4; season++)
                {
                    var capacity = new EcologyCapacity(w, tile, view, season);
                    food = food.Then(SeasonalAffine.Recovery(capacity.Food, capacity.FoodRate, baseDays));
                    nonfood = nonfood.Then(SeasonalAffine.Recovery(capacity.Biomass - capacity.Food,
                        capacity.BiomassRate, baseDays));
                    water = water.Then(SeasonalAffine.Recovery(capacity.Water, capacity.WaterRate, baseDays));
                }
                double n = nonfood.Repeat(Math.Max(0, view.PotentialFood - view.Food), cycles);
                view.Food = food.Repeat(view.Food, cycles);
                view.PotentialFood = view.Food + n;
                view.Freshwater = water.Repeat(view.Freshwater, cycles);
                double covered = cycles * cycleDays;
                view.Days += covered; remaining -= covered;
            }
            if (remaining > 1e-9) AdvanceQuietEdge(w, tile, ref view, remaining);
        }
        private static void AdvanceQuietEdge(WorldState w, Tile tile, ref EcologyView view, double days)
        {
            double remaining = days, baseDays = w.Config.BaseDays;
            for (int i = 0; i < 5 && remaining > 1e-9; i++)
            {
                long interval = (long)Math.Floor(view.Days / baseDays + 1e-10);
                double part = Math.Min(remaining, (interval + 1) * baseDays - view.Days);
                if (part <= 1e-9) break;
                var capacity = new EcologyCapacity(w, tile, view, (int)(interval % 4));
                double n = EcologyMath.Stock(Math.Max(0, view.PotentialFood - view.Food),
                    capacity.Biomass - capacity.Food, capacity.BiomassRate, 0, part).Stock;
                view.Food = EcologyMath.Stock(view.Food, capacity.Food, capacity.FoodRate, 0, part).Stock;
                view.PotentialFood = view.Food + n;
                view.Freshwater = EcologyMath.Stock(view.Freshwater, capacity.Water, capacity.WaterRate, 0, part).Stock;
                view.Days += part; remaining -= part;
            }
        }
        // Land receipts are fixed at the opening boundary. This operator only
        // prepares the stock that the next turn may claim. A complete year is
        // four ordered affine maps, repeated in constant work for any horizon.
        internal static void RecoverLandAfterHarvest(WorldState w, Tile tile, ref EcologyView view,
            double start, double days, double? averageCapital, double? endingCapital,
            double? operatingFraction, bool recoverWater = false, Action profileEvaluated = null,
            Action<EcologyCapacity, double> forcingProfile = null)
        {
            double previousCapital = view.CultivationCapital;
            double operation = operatingFraction ?? tile.CultivationOperatingFraction;
            double workingCapital = previousCapital;
            if (averageCapital.HasValue) workingCapital = averageCapital.Value;
            else if (previousCapital > 0 && operation < 1)
            {
                double decay = Math.Log(2) / w.Config.Neolithic.DecayHalfLifeYears
                    * (1 - Numbers.Clamp(operation, 0, 1)) / w.Config.DaysPerYear;
                workingCapital *= EcologyMath.MeanExponential(decay, days);
            }
            EcologyCapacity naturalCapacity = default;
            int naturalSeason = -1;
            ReclassifyLandFood(w, tile, ref view, previousCapital, workingCapital,
                ref naturalCapacity, ref naturalSeason, profileEvaluated);
            view.CultivationCapital = workingCapital;
            view.Days = start;
            // Capacity depends on the fixed representative land state and the
            // profile ID, never on how often that profile occurs in the horizon.
            Span<EcologyCapacity> profiles = stackalloc EcologyCapacity[4];
            int profileMask = 0;
            double baseDays = w.Config.BaseDays, cycleDays = 4 * baseDays;
            double phase = start - Math.Floor(start / cycleDays) * cycleDays;
            double first = phase <= 1e-9 ? 0 : Math.Min(days, cycleDays - phase);
            if (first > 0) AdvanceLandRecoveryEdge(w, tile, ref view, first,
                operatingFraction, recoverWater, profiles, ref profileMask,
                profileEvaluated, forcingProfile);
            double remaining = days - first;
            long cycles = (long)Math.Floor(remaining / cycleDays + 1e-10);
            if (cycles > 0)
            {
                bool alignedWholeCycles = first == 0 && Math.Abs(remaining - cycles * cycleDays) <= 1e-9;
                var food = SeasonalAffine.Identity;
                var nonfood = SeasonalAffine.Identity;
                var water = SeasonalAffine.Identity;
                for (int season = 0; season < 4; season++)
                {
                    EcologyCapacity capacity;
                    if (alignedWholeCycles)
                    {
                        profileEvaluated?.Invoke();
                        capacity = new EcologyCapacity(w, tile, view, season, operatingFraction);
                    }
                    else capacity = LandCapacityAt(w, tile, view, season, operatingFraction,
                        profiles, ref profileMask, profileEvaluated);
                    forcingProfile?.Invoke(capacity, baseDays * cycles);
                    food = food.Then(SeasonalAffine.ManagedRecovery(capacity.Food,
                        capacity.FoodRate, capacity.ManagedFlow, baseDays));
                    nonfood = nonfood.Then(SeasonalAffine.Recovery(
                        Math.Max(0, capacity.Biomass - capacity.Food),
                        capacity.BiomassRate, baseDays));
                    if (recoverWater)
                        water = water.Then(SeasonalAffine.Recovery(capacity.Water,
                            capacity.WaterRate, baseDays));
                }
                double n = nonfood.Repeat(Math.Max(0, view.PotentialFood - view.Food), cycles);
                view.Food = food.Repeat(view.Food, cycles);
                view.PotentialFood = view.Food + n;
                if (recoverWater) view.Freshwater = water.Repeat(view.Freshwater, cycles);
                double covered = cycles * cycleDays;
                view.Days += covered; remaining -= covered;
            }
            if (remaining > 1e-9) AdvanceLandRecoveryEdge(w, tile, ref view, remaining,
                operatingFraction, recoverWater, profiles, ref profileMask,
                profileEvaluated, forcingProfile);
            double finalCapital = endingCapital ?? (previousCapital <= 0 || operation >= 1
                ? previousCapital : CultivationEconomy.Decay(w.Config,
                    previousCapital, operation, days / w.Config.DaysPerYear));
            ReclassifyLandFood(w, tile, ref view, workingCapital, finalCapital,
                ref naturalCapacity, ref naturalSeason, profileEvaluated);
            view.CultivationCapital = finalCapital;
            view.Days = start + days;
        }
        private static void AdvanceLandRecoveryEdge(WorldState w, Tile tile, ref EcologyView view,
            double days, double? operatingFraction, bool recoverWater,
            Span<EcologyCapacity> profiles, ref int profileMask, Action profileEvaluated,
            Action<EcologyCapacity, double> forcingProfile)
        {
            double remaining = days, baseDays = w.Config.BaseDays;
            for (int i = 0; i < 5 && remaining > 1e-9; i++)
            {
                long interval = (long)Math.Floor(view.Days / baseDays + 1e-10);
                double part = Math.Min(remaining, (interval + 1) * baseDays - view.Days);
                if (part <= 1e-9) break;
                var capacity = LandCapacityAt(w, tile, view, (int)(interval % 4),
                    operatingFraction, profiles, ref profileMask, profileEvaluated);
                forcingProfile?.Invoke(capacity, part);
                double n = SeasonalAffine.Recovery(Math.Max(0, capacity.Biomass - capacity.Food),
                    capacity.BiomassRate, part).Apply(Math.Max(0, view.PotentialFood - view.Food));
                view.Food = SeasonalAffine.ManagedRecovery(capacity.Food, capacity.FoodRate,
                    capacity.ManagedFlow, part).Apply(view.Food);
                view.PotentialFood = view.Food + n;
                if (recoverWater)
                    view.Freshwater = SeasonalAffine.Recovery(capacity.Water,
                        capacity.WaterRate, part).Apply(view.Freshwater);
                view.Days += part; remaining -= part;
            }
        }
        private static EcologyCapacity LandCapacityAt(WorldState w, Tile tile, EcologyView view,
            int season, double? operatingFraction, Span<EcologyCapacity> profiles,
            ref int profileMask, Action profileEvaluated)
        {
            int bit = 1 << season;
            if ((profileMask & bit) == 0)
            {
                profileEvaluated?.Invoke();
                profiles[season] = new EcologyCapacity(w, tile, view, season, operatingFraction);
                profileMask |= bit;
            }
            return profiles[season];
        }
        private static void ReclassifyLandFood(WorldState w, Tile tile, ref EcologyView view,
            double previousCapital, double nextCapital, ref EcologyCapacity naturalCapacity,
            ref int naturalSeason, Action profileEvaluated)
        {
            if (Math.Abs(previousCapital - nextCapital) <= 1e-12 || view.PotentialFood <= 0) return;
            int season = (int)Math.Floor(view.Days / w.Config.BaseDays) % 4;
            // The ecological biomass and wild-food base are independent of q.
            // Evaluate that base once, then derive both access shares from it.
            if (naturalSeason != season)
            {
                var natural = view; natural.CultivationCapital = 0;
                profileEvaluated?.Invoke();
                naturalCapacity = new EcologyCapacity(w, tile, natural, season);
                naturalSeason = season;
            }
            double previousQ = CultivationEconomy.Intensity(w.Config, previousCapital);
            double nextQ = CultivationEconomy.Intensity(w.Config, nextCapital);
            double baseBiomass = naturalCapacity.Biomass;
            double oldFood = EcologyMath.EffectiveWildFoodCapacity(naturalCapacity.WildFood,
                baseBiomass, previousQ, w.Config, tile.Seasons[season].Moisture);
            double newFood = EcologyMath.EffectiveWildFoodCapacity(naturalCapacity.WildFood,
                baseBiomass, nextQ, w.Config, tile.Seasons[season].Moisture);
            if (tile.Land && w.Config.Agriculture?.IrrigationEnabled == true && view.IrrigationArea > 0)
            {
                // Keep one continuous irrigated biomass denominator across q=0 <-> q>0
                // so entering or leaving cultivation cannot destroy or fabricate wild food.
                var settings = w.Config.Agriculture;
                var s = tile.Seasons[season];
                double area = Numbers.Clamp(view.IrrigationArea, 0, 1);
                var watered = s;
                watered.Moisture = (float)(s.Moisture + view.IrrigationWater * Math.Max(0, settings.TargetMoisture - s.Moisture));
                double multiplier = OpeningTransitions.FoodMultiplier(w, tile);
                double condition = Numbers.Clamp(view.LandCondition, 0, 1);
                double waterBiomass = s.Moisture > 1e-9 ? baseBiomass * watered.Moisture / s.Moisture :
                    FoodEcology.LandBiomassBase * w.Config.LandBiomassMultiplier *
                    FoodEcology.LandProductivity(tile, watered, w.Config) * multiplier * condition;
                double improved = waterBiomass * (1 + settings.SoilCapacityGain * view.IrrigationSoil);
                double blendedBiomass = (1 - area) * baseBiomass + area * improved;
                double oldIrr = EcologyMath.EffectiveWildFoodCapacity(naturalCapacity.WildFood,
                    improved, previousQ, w.Config, watered.Moisture);
                double newIrr = EcologyMath.EffectiveWildFoodCapacity(naturalCapacity.WildFood,
                    improved, nextQ, w.Config, watered.Moisture);
                baseBiomass = blendedBiomass;
                oldFood = Math.Min(blendedBiomass, (1 - area) * oldFood + area * oldIrr);
                newFood = Math.Min(blendedBiomass, (1 - area) * newFood + area * newIrr);
            }
            double oldShare = baseBiomass <= 0 ? 0 : Numbers.Clamp(oldFood / baseBiomass, 0, 1);
            double newShare = baseBiomass <= 0 ? 0 : Numbers.Clamp(newFood / baseBiomass, 0, 1);
            double nonfood = Math.Max(0, view.PotentialFood - view.Food);
            if (newShare > oldShare && oldShare < 1)
                view.Food += nonfood * (newShare - oldShare) / (1 - oldShare);
            else if (newShare < oldShare && oldShare > 0)
                view.Food *= newShare / oldShare;
            view.Food = Numbers.Clamp(view.Food, 0, view.PotentialFood);
        }
        internal static void ReclassifyAquaticFood(WorldState w, Tile tile,
            ref EcologyView view, double fromCapital, double toCapital, int season)
        {
            if (!tile.Aquatic || (tile.Environment != EnvironmentKind.Shallows &&
                tile.Environment != EnvironmentKind.Lake) ||
                Math.Abs(fromCapital - toCapital) <= 1e-12 || view.PotentialFood <= 0) return;
            var fromOverlay = new AquaticEcologyOverlay(fromCapital, fromCapital,
                fromCapital, 0);
            var toOverlay = new AquaticEcologyOverlay(toCapital, toCapital,
                toCapital, 0);
            double biomass = Math.Max(0, view.PotentialFood);
            double oldFood = new EcologyCapacity(w, tile, view, season,
                aquaticOverlay: fromOverlay).Food;
            double newFood = new EcologyCapacity(w, tile, view, season,
                aquaticOverlay: toOverlay).Food;
            double oldShare = biomass <= 0 ? 0 : Numbers.Clamp(oldFood / biomass, 0, 1);
            double newShare = biomass <= 0 ? 0 : Numbers.Clamp(newFood / biomass, 0, 1);
            double nonfood = Math.Max(0, view.PotentialFood - view.Food);
            if (newShare > oldShare && oldShare < 1)
                view.Food += nonfood * (newShare - oldShare) / (1 - oldShare);
            else if (newShare < oldShare && oldShare > 0)
                view.Food *= newShare / oldShare;
            view.Food = Numbers.Clamp(view.Food, 0, view.PotentialFood);
        }
        internal static void ClampCapacity(WorldState w, Tile t, ref EcologyView v,
            AquaticEcologyOverlay? aquaticOverlay = null)
        {
            var k = new EcologyCapacity(w, t, v,
                (int)Math.Floor(v.Days / w.Config.BaseDays) % 4,
                aquaticOverlay: aquaticOverlay);
            v.PotentialFood = Numbers.Clamp(v.PotentialFood, 0, k.Biomass);
            v.Food = Numbers.Clamp(v.Food, 0, Math.Min(v.PotentialFood, k.Food));
            v.Freshwater = Numbers.Clamp(v.Freshwater, 0, k.Water);
        }
        internal static void RecoverCondition(WorldState w, Tile t, ref EcologyView v, double days, double damagePerDay)
        {
            var c = w.Config;
            bool aquatic = t.Aquatic || t.WaterBody == WaterKind.Saltwater;
            double recovery = LandConditionRecoveryPerDay(w, t);
            bool plankton = aquatic && (w.Generation?.GeneratorVersion ?? WorldState.GeneratorVersion) >= 11;
            // LandCondition is a stored future-disturbance input. Current
            // gathering, grazing, cultivation, timber and quiet projection do
            // not write it; only finite land stocks recover over elapsed time.
            v.AquaticCondition = EcologyMath.Condition(v.AquaticCondition, 1, plankton ? AquaticEcology.ConditionRecoveryPerDay(t) : recovery, aquatic ? damagePerDay : 0, days);
            v.CurrentVegetation = Vegetation.Update(t.NaturalVegetationPotential, v.CurrentVegetation, plankton ? v.AquaticCondition : v.LandCondition, c);
        }
        internal static double LandConditionRecoveryPerDay(WorldState w, Tile t) => w.Config.ConditionRecoveryPerYear / w.Config.DaysPerYear *
            (t.NaturalVegetationPotential == VegetationTier.Low ? w.Config.LowPotentialRecoveryMultiplier : t.NaturalVegetationPotential == VegetationTier.High ? w.Config.HighPotentialRecoveryMultiplier : 1);
        public static EcologyView MaterializeEcology(WorldState w, int tile, double queryDays)
        {
            var wood = MaterialEconomy.ProjectWoodSource(w, tile);
            var view = ProjectEcology(w, tile, queryDays, true, wood == null ? null : wood.Recover);
            view.Commit(w.Tiles[tile]); w.Tiles[tile].Cultivation = (float)CultivationEconomy.Intensity(w.Config, view.CultivationCapital);
            wood?.Commit(); return view;
        }
        // Land claims can only debit the opening stock; renewal is for the next turn.
        // Aquatic claims retain the coupled stock-flow envelope.
        public static double AccessibleAvailability(WorldState w, int tile, double start, double duration,
            double? operatingFraction = null, double? averageCultivationCapital = null,
            AquaticEcologyOverlay? aquaticOverlay = null)
        {
            var t = w.Tiles[tile]; var view = ProjectEcology(w, tile, start); double available = view.Food;
            if (t.Land) return Math.Max(0, available);
            var overlay = aquaticOverlay ?? ProjectAquaticOverlay(w, tile, start,
                duration, operatingFraction);
            if (overlay.HasValue)
                ReclassifyAquaticFood(w, t, ref view, overlay.Value.OpeningCapital,
                    overlay.Value.AverageCapital,
                    (int)Math.Floor(start / w.Config.BaseDays) % 4);
            var mean = MeanView(w, t, view, duration, 0, averageCultivationCapital, operatingFraction);
            var capacity = new HorizonEcologyCapacity(w, t, mean, start, duration,
                operatingFraction, overlay);
            available += (capacity.Food * capacity.FoodRate + capacity.ManagedFlow) * duration;
            return Math.Max(0, available);
        }
    }
}
