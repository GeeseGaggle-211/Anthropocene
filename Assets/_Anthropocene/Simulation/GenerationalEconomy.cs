using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    public sealed class ManagedFoodForecast
    {
        public double AnimalFood, HumanFoodDisplaced;
        // Canonical own-herd references from this existing forecast pass; transient.
        internal Herd[] OwnedHerds = Array.Empty<Herd>();
        // Total animals behind a grown-herd forecast (TurnResolver.ForecastGrownManagedAnimalFood); 0 otherwise.
        public double Animals;
        // Transient weighted species inputs, collected in the existing own-herd forecast pass.
        // They describe animal units, not livestock heads, and are never persisted.
        public double GrowthRate, CarcassFood, FoodConsumption, WaterConsumption, ManagementFulfillment;
    }

    internal sealed class GenerationResource
    {
        public int Tile;
        public double AvailableFood;
    }

    internal sealed class GrowthPlan
    {
        public double Required, ConsumptionRequired, GrowthFood, ReproductivePopulation, FinalPopulation, ManufacturingInput, PlantManufacturingInput, AnimalManufacturingInput;
        // Diagnostic prototype only: clothing feedstock the Elder committed ahead of meals (zero otherwise).
        public double ProtectedClothingFood;
        // Transient explanation of the food chronology used by the opt-in
        // recovery diagnostic. These values never enter authoritative state.
        public double ProjectedFood, NetIncome, PriorityIncome, AvailableAfterReserve;
        public double PlantFoodAvailable, AnimalFoodAvailable;
        public double ClothingPlantInput, ClothingAnimalInput;
        public double RequestedPlantInput, RequestedAnimalInput;
    }

    // A domestic herd uses the camp's immediate land footprint. These are
    // requests against opening biomass and finite freshwater, not receipts;
    // tile integration remains the sole authority when claimants compete.
    internal static class ManagedHerdLocalClaims
    {
        // existing: claims already placed this turn (wild and other herds, earlier managed herds). Water is then split
        // by each source's water left after those claims, so a source another herd already saturates gets no share
        // while neighbouring sources have spare water. Without existing claims, or when every source is already
        // claimed in full, the split is by source water as before.
        public static void Append(WorldState world, int campTile, int herdId, double foodNeed,
            double waterNeed, double duration, List<Demand> claims, HashSet<int> visible = null,
            IReadOnlyList<Demand> existing = null)
        {
            if (foodNeed <= 0 && waterNeed <= 0) return;
            var sites = LocalHerdEcology.Survey(world, campTile, 1, duration,
                null, visible).Sites;
            if (sites.Length == 0) return;

            var foodClaims = new double[sites.Length];
            var waterClaims = new double[sites.Length];
            double nonhuman = sites.Sum(s => s.NonhumanFood);
            double human = sites.Sum(s => s.HumanFood);
            double requestedFood = Math.Max(0, foodNeed);
            double first = Math.Min(requestedFood, nonhuman);
            double second = Math.Min(Math.Max(0, requestedFood - first), human);
            double additional = Math.Max(0, requestedFood - first - second);
            double totalBiomass = nonhuman + human;
            int landCount = sites.Count(s => s.Land);
            int lastLand = Array.FindLastIndex(sites, s => s.Land);
            double remainingFood = Math.Max(0, foodNeed);
            for (int i = 0; i < sites.Length; i++)
            {
                if (!sites[i].Land) continue;
                double proposed = Portion(first, sites[i].NonhumanFood, nonhuman)
                    + Portion(second, sites[i].HumanFood, human)
                    + (totalBiomass > 0 ? Portion(additional,
                        sites[i].NonhumanFood + sites[i].HumanFood, totalBiomass)
                        : additional / landCount);
                foodClaims[i] = i == lastLand ? remainingFood
                    : Math.Min(remainingFood, Math.Max(0, proposed));
                remainingFood = Math.Max(0, remainingFood - foodClaims[i]);
            }
            double water = sites.Sum(s => s.Water);
            double requestedWater = Math.Max(0, waterNeed);
            int sourceCount = sites.Count(s => s.WaterSource);
            int lastSource = Array.FindLastIndex(sites, s => s.WaterSource);
            double remainingWater = Math.Max(0, waterNeed);
            double[] residual = null;
            if (existing != null && requestedWater > 0)
            {
                var claimed = new Dictionary<int, double>();
                foreach (var d in existing)
                    if (d.Freshwater > 0)
                        claimed[d.Tile] = (claimed.TryGetValue(d.Tile, out double prior) ? prior : 0) + d.Freshwater;
                residual = sites.Select(site => site.WaterSource
                    ? Math.Max(0, site.Water - (claimed.TryGetValue(site.Tile, out double c) ? c : 0)) : 0).ToArray();
                if (!(residual.Sum() > 0)) residual = null;
            }
            if (residual != null)
            {
                double free = residual.Sum();
                int lastFree = Array.FindLastIndex(residual, r => r > 0);
                for (int i = 0; i < sites.Length; i++)
                {
                    if (!(residual[i] > 0)) continue;
                    waterClaims[i] = i == lastFree ? remainingWater
                        : Math.Min(remainingWater, Math.Max(0, Portion(requestedWater, residual[i], free)));
                    remainingWater = Math.Max(0, remainingWater - waterClaims[i]);
                }
            }
            else
            for (int i = 0; i < sites.Length; i++)
            {
                if (!sites[i].WaterSource) continue;
                double proposed = water > 0 ? Portion(requestedWater, sites[i].Water, water)
                    : requestedWater / sourceCount;
                waterClaims[i] = i == lastSource ? remainingWater
                    : Math.Min(remainingWater, Math.Max(0, proposed));
                remainingWater = Math.Max(0, remainingWater - waterClaims[i]);
            }

            for (int i = 0; i < sites.Length; i++)
                if (foodClaims[i] > 0 || waterClaims[i] > 0)
                    claims.Add(new Demand { Tile = sites[i].Tile, Entity = herdId, Kind = "Herd",
                        PotentialFood = foodClaims[i], Freshwater = waterClaims[i] });
        }

        private static double Portion(double total, double weight, double weights)
            => total <= 0 || weight <= 0 || weights <= 0 ? 0 : total * weight / weights;
    }

    public sealed partial class TurnResolver
    {
        // Planning envelope only: use visible ecology and the camp's own managed
        // animals, with no newborns and no mutation. The resolver remains the
        // authority for actual source competition, survival, and accepted output.
        public static ManagedFoodForecast ForecastManagedAnimalFood(WorldState w, Camp c, int herders, double intervals, double years, int? campTile = null)
            => ForecastManagedHerds(w, c, herders, intervals, years, campTile, null, null, -1);

        // Pure planning forecast of what the camp's herds add after one more turn of breeding (Value rule, Docs/HusbandrySettling.md).
        // Each herd that is fully nourished in the ordinary forecast (the resolver breeds only then) grows to
        // floor((animals + remainder) * exp(GrowthRate * breeding * years)); the grown herd is then run through the same
        // forage/water forecast with the herders it would need (capped at the residents), so pasture or water that cannot carry it
        // lowers nutrition and yield exactly as in the resolver. Animals reports the grown total. No newborn or ecology is committed.
        public static ManagedFoodForecast ForecastGrownManagedAnimalFood(WorldState w, Camp c, double breeding, double intervals, double years, int? campTile = null)
        {
            var nutrition = new Dictionary<int, double>();
            var current = ForecastManagedHerds(w, c, 0, intervals, years, campTile, null, nutrition, -1);
            if (nutrition.Count == 0) return new ManagedFoodForecast();
            var grown = new Dictionary<int, double>();
            double total = 0;
            foreach (var herd in w.Herds.Where(h => h.DomesticCamp == c.Id && nutrition.ContainsKey(h.Id)).OrderBy(h => h.Id))
            {
                double animals = herd.Animals;
                if (nutrition[herd.Id] >= 1 - 1e-12)
                {
                    var species = w.Config.GetSpecies(herd.Species);
                    double target = (herd.Animals + herd.GrowthRemainder) * Math.Exp(species.GrowthRate * breeding * years * nutrition[herd.Id]);
                    animals = Math.Max(animals, Math.Floor(target));
                }
                grown[herd.Id] = animals; total += animals;
            }
            int herders = HerdingTooling.RequiredHerdersFor(w, c, years, total);
            var result = ForecastManagedHerds(w, c, herders, intervals, years, campTile, grown, null, total);
            result.Animals = total;
            return result;
        }

        private static ManagedFoodForecast ForecastManagedHerds(WorldState w, Camp c, int herders, double intervals, double years, int? campTile,
            Dictionary<int, double> animalsOf, Dictionary<int, double> nutritionOut, double managedAnimals)
        {
            var forecast = new ManagedFoodForecast();
            if (w == null || c == null || intervals <= 0 || years <= 0 || !CultivationEconomy.HusbandryEnabled(c)) return forecast;
            var visible = Knowledge.Visible(w, c.Player);
            var owned = w.Herds.Where(h => h.DomesticCamp == c.Id && h.Animals > 0).OrderBy(h => h.Id).ToArray();
            forecast.OwnedHerds = owned;
            var herds = owned.Where(h => visible.Contains(h.Tile)).ToArray();
            if (herds.Length == 0) return forecast;
            var ownedIds = owned.Select(h => h.Id).ToHashSet();
            double Animals(Herd h) => animalsOf != null && animalsOf.TryGetValue(h.Id, out double a) ? a : h.Animals;
            var claims = new List<Demand>();
            var requestedFood = new Dictionary<int, double>();
            var requestedWater = new Dictionary<int, double>();
            // Rival claims are collected first so the camp's own herds route their water around sources the
            // rivals already saturate, exactly as the resolver does.
            var rivalAll = new List<Demand>();
            double duration = intervals * w.Config.BaseDays;
            foreach (var rival in w.Herds.Where(h => !ownedIds.Contains(h.Id)
                && visible.Contains(h.Tile)).OrderBy(h => h.Id))
            {
                var rivalClaims = new List<Demand>();
                var species = w.Config.GetSpecies(rival.Species);
                double rivalAnimals = HerdCountKnowledge.Observe(rival.Animals, w.Config).Representative;
                if (rivalAnimals <= 0) continue;
                bool domestic = rival.DomesticCamp >= 0; // Public HerdObservation.Domestic.
                if (!domestic)
                {
                    rivalAnimals = Math.Max(rivalAnimals, Math.Ceiling(rivalAnimals
                        * Math.Exp(Math.Max(0, species.GrowthRate) * years)));
                    int source = FreshwaterAccess.Source(w, rival.Tile);
                    if (visible.Contains(rival.Tile))
                        rivalClaims.Add(new Demand
                        {
                            Tile = rival.Tile, Entity = rival.Id, Kind = "Herd",
                            PotentialFood = rivalAnimals * species.FoodConsumption * intervals,
                            Freshwater = source == rival.Tile
                                ? rivalAnimals * species.FreshwaterConsumption * intervals : 0
                        });
                    if (source != rival.Tile && visible.Contains(source))
                        rivalClaims.Add(new Demand
                        {
                            Tile = source, Entity = rival.Id, Kind = "Herd",
                            Freshwater = rivalAnimals * species.FreshwaterConsumption * intervals
                        });
                }
                else
                {
                    // The owner's camp identity is private for a foreign herd.
                    // At a committed boundary a domestic herd is located with
                    // its camp, so its public current tile is the only permitted
                    // footprint centre. Do not redistribute a hidden share onto
                    // the visible subset of a partial footprint.
                    if (w.Map.Radius(rival.Tile, 1).Any(id => !visible.Contains(id)))
                        continue;
                    ManagedHerdLocalClaims.Append(w, rival.Tile, rival.Id,
                        rivalAnimals * species.FoodConsumption * intervals,
                        rivalAnimals * species.FreshwaterConsumption * intervals,
                        duration, rivalClaims, visible);
                }
                rivalAll.AddRange(rivalClaims);
            }
            foreach (var herd in herds)
            {
                var species = w.Config.GetSpecies(herd.Species);
                double food = Animals(herd) * species.FoodConsumption * intervals;
                double water = Animals(herd) * species.FreshwaterConsumption * intervals;
                requestedFood[herd.Id] = food; requestedWater[herd.Id] = water;
                ManagedHerdLocalClaims.Append(w, campTile ?? c.Tile, herd.Id, food, water,
                    intervals * w.Config.BaseDays, claims, visible, rivalAll.Concat(claims).ToList());
            }
            // Visible rival herds draw from the same finite forage and water
            // ledgers during resolution. Include only claims on a source this
            // camp's own herds also use; unrelated rivals cannot affect this
            // forecast. A rival's private count, deficit, growth remainder,
            // capacity and owner location are not planner inputs: wild/foreign
            // counts first pass through the same public observation bands as
            // policy input. Wild growth uses a public full-intake projection
            // because its prior nutrition is private; count bands mean this is
            // an estimate, not a guaranteed upper or lower bound.
            var ownClaimTiles = claims.Select(d => d.Tile).ToHashSet();
            claims.AddRange(rivalAll.Where(d => ownClaimTiles.Contains(d.Tile)));
            var transfers = new Dictionary<int, (double food, double water)>();
            foreach (var group in claims.GroupBy(d => d.Tile).OrderBy(g => g.Key))
            {
                var result = EcologicalPressure.Integrate(w, group.Key, group, w.Days, intervals * w.Config.BaseDays);
                foreach (var transfer in result.Transfers)
                {
                    if (!requestedFood.ContainsKey(transfer.Entity)) continue;
                    transfers.TryGetValue(transfer.Entity, out var prior);
                    transfers[transfer.Entity] = (prior.food + transfer.PotentialFood, prior.water + transfer.Freshwater);
                }
                forecast.HumanFoodDisplaced += result.Pressure.Where(p => p.Pool == "Biomass"
                    && p.Kind == "Herd" && ownedIds.Contains(p.Entity)).Sum(p => p.GatherableSpill);
            }
            double management = HerdingTooling.Plan(w, c, years, herders, managedAnimals).Fulfillment;
            forecast.ManagementFulfillment = management;
            foreach (var herd in herds)
            {
                var species = w.Config.GetSpecies(herd.Species);
                double count = Animals(herd);
                forecast.GrowthRate += count * species.GrowthRate;
                forecast.CarcassFood += count * species.FoodYield;
                forecast.FoodConsumption += count * species.FoodConsumption;
                forecast.WaterConsumption += count * species.FreshwaterConsumption;
                transfers.TryGetValue(herd.Id, out var realized);
                double foodNutrition = requestedFood[herd.Id] <= 1e-12 ? 1 : Numbers.Clamp(realized.food / requestedFood[herd.Id], 0, 1);
                double waterNutrition = requestedWater[herd.Id] <= 1e-12 ? 1 : Numbers.Clamp(realized.water / requestedWater[herd.Id], 0, 1);
                double nutrition = Math.Min(foodNutrition, waterNutrition);
                if (nutritionOut != null) nutritionOut[herd.Id] = nutrition;
                double producingAnimals = Animals(herd);
                if (nutrition < 1 - 1e-12 && herd.DeficitTurns + 1 > w.Config.StarvationGraceTurns)
                {
                    double deathRemainder = herd.DeathRemainder;
                    int deaths = StarvationLosses((int)Math.Floor(producingAnimals),
                        1 - nutrition, w.Config.Mortality,
                        intervals * w.Config.BaseDays / w.Config.PracticeDays,
                        ref deathRemainder);
                    producingAnimals = Math.Max(0, producingAnimals - deaths);
                }
                forecast.AnimalFood += ManagedAnimalFoodYield(w, producingAnimals, nutrition, management, intervals);
            }
            double totalAnimals = herds.Sum(Animals);
            if (totalAnimals > 0)
            {
                forecast.GrowthRate /= totalAnimals; forecast.CarcassFood /= totalAnimals;
                forecast.FoodConsumption /= totalAnimals; forecast.WaterConsumption /= totalAnimals;
            }
            return forecast;
        }
        private static double ManagedAnimalFoodYield(WorldState w, double animals, double nutrition, double management, double intervals)
            => Math.Max(0, animals * w.Config.AnimalFoodPerManagedAnimal * w.Config.Consumption * intervals
                * Math.Pow(Numbers.Clamp(nutrition, 0, 1), w.Config.AnimalFoodNutritionExponent)
                * Math.Pow(Numbers.Clamp(management, 0, 1), w.Config.AnimalManagementExponent));

        private void ResolveEcology(WorldState w, List<Order> orders, HashSet<int> establishedHunters, TurnRecord record, Action<int> touch,
            Dictionary<int, TurnNeeds> needs, IReadOnlyCollection<int> abandoningVillages,
            IReadOnlyDictionary<int, FishingPlan> fishingPlans)
        {
            var phaseClock = System.Diagnostics.Stopwatch.StartNew();
            var horizon = TurnHorizon.For(w, record);
            double intervals = horizon.ReferenceIntervals;
            double years = horizon.Years;
            double generationScale = horizon.PracticeScale;
            var resources = new Dictionary<int, GenerationResource>();
            var foraged = new Dictionary<int, double>();
            var recovering = new HashSet<int>(w.Camps.Where(c => c.DeficitTurns > 0).Select(c => c.Id));
            var agriculturalLabor = w.Camps.OrderBy(c => c.Id).ToDictionary(c => c.Id, c =>
                new AgriculturalLabor
                {
                    Gatherers = c.GathererPopulation + CultivationEconomy.Workers(c, WorkforceRoles.Gatherer),
                    Footprint = CultivationEconomy.Enabled(c) && !abandoningVillages.Contains(c.Id)
                        ? CultivationEconomy.Footprint(w, c) : Array.Empty<int>()
                });

            GenerationResource Resource(int tile)
            {
                if (resources.TryGetValue(tile, out var value)) return value;
                value = new GenerationResource { Tile = tile, AvailableFood = LazyEcology.AccessibleAvailability(w, tile, record.StartDays, record.Duration) };
                resources.Add(tile, value); return value;
            }

            foreach (var camp in w.Camps.OrderBy(c => c.Id))
            {
                record.Demographics.Add(new DemographicReport { Camp = camp.Id, StartingPopulation = w.LinkedPopulation(camp), Years = years, Model = w.Config.DemographicModel, QoLMortalityReduction = QoLMortality.Reduction(w, camp), QoL = camp.QoL, InitialReserves = camp.Food + w.Hunters.Where(h => h.SourceCamp == camp.Id).OrderBy(h => h.Id).Sum(h => h.CarriedFood) });
                camp.StepFoodIncome = 0;
            }

            // Movement happens once. Herd reproduction is realized before the generation's hunting harvest.
            var startingHerdPopulation = w.Herds.OrderBy(h => h.Id).ToDictionary(h => h.Id, h => h.Animals);
            var taming = new List<HusbandryAcquisition.Opportunity>();
            ReproduceHerds(w, intervals, years);
            Hunt(w, orders, establishedHunters, startingHerdPopulation, record, intervals, years, needs, taming);
            foreach (var herd in w.Herds.Where(h => h.DomesticCamp < 0 && !w.Config.WildHerdFissionEnabled))
                herd.Animals = Math.Min(herd.Animals, herd.Capacity);
            var demands = new List<Demand>();
            var campClaims = w.Camps.ToDictionary(c => c.Id, c => new List<Demand>());
            foreach (var order in orders.Where(o => o.Kind == OrderKind.Fish).OrderBy(o => o.Entity))
            {
                var hunter = w.Hunters.FirstOrDefault(h => h.Id == order.Entity); if (hunter == null || hunter.Population <= 0) continue;
                if (w.Map.Distance(hunter.Tile, order.Target) <= 1)
                {
                    var claim = new Demand { Tile = order.Target, Entity = hunter.Id, Kind = "Fishing", ImpactMultiplier = PracticeEffects.FishingImpact(w.Config, w.Camps.First(c => c.Id == hunter.SourceCamp)), Food = hunter.Population * w.Config.FishPerPerson * intervals };
                    if (Supply.Connected(w, hunter)) campClaims[hunter.SourceCamp].Add(claim);
                    else
                    {
                        claim.Food = Math.Min(claim.Food, Math.Max(0, Supply.Capacity(w, hunter) - hunter.CarriedFood));
                        demands.Add(claim);
                    }
                }
            }
            // Resident fishing claims join ordinary gathering, Hunter fishing,
            // and crop harvest before the one shared tile integration.
            if (fishingPlans != null)
                foreach (var plan in fishingPlans.Values.OrderBy(p => p.CampId))
                    if (campClaims.TryGetValue(plan.CampId, out var residentClaims))
                    {
                        var camp = w.Camps.FirstOrDefault(c => c.Id == plan.CampId);
                        foreach (var ground in plan.Grounds.OrderBy(g => g.TileId))
                            if (ground.RequestedCatch > 0)
                                residentClaims.Add(new Demand
                                {
                                    Tile = ground.TileId, Entity = plan.CampId, Kind = "FisheryHarvest",
                                    ImpactMultiplier = camp == null ? 1 : PracticeEffects.FishingImpact(w.Config, camp),
                                    Food = ground.RequestedCatch
                                });
                    }
            foreach (var camp in w.Camps.OrderBy(c => c.Id))
            {
                var labor = agriculturalLabor[camp.Id];
                double gatherers = PassiveFoodGathererIntervals(camp, needs.TryGetValue(camp.Id, out var preparedNeeds) ? preparedNeeds : null,
                    labor.Gatherers, intervals);
                // Phase-B Elder raw-material Gatherers are genuinely dedicated
                // workers, even though DemandControl does not make its entire
                // economic labor plan policy-authoritative. Remove only these
                // newly committed intervals from food gathering.
                if (CultivationEconomy.Enabled(camp) && !abandoningVillages.Contains(camp.Id))
                {
                    // Field and nearby gathering tiles receive one turn-horizon
                    // integration even if the camp ultimately makes no claim.
                    foreach (int id in labor.Footprint.Concat(w.Map.Radius(camp.Tile, w.Config.GatherRadius).Where(id => PassiveGathering.Eligible(w, camp, id))).Distinct())
                        demands.Add(new Demand { Tile = id, Entity = camp.Id, Kind = "Field operation" });
                    continue;
                }
                if (gatherers <= 0) continue;
                var residentFishingReservations = campClaims[camp.Id]
                    .Where(d => d.Kind == "FisheryHarvest" && d.Food > 0)
                    .GroupBy(d => d.Tile).ToDictionary(g => g.Key, g => g.Sum(d => d.Food));
                double PassiveAvailable(int tile)
                {
                    double reserved = residentFishingReservations.TryGetValue(tile, out double fish) ? fish : 0;
                    return Math.Max(0, Resource(tile).AvailableFood - reserved);
                }
                AddPassiveCampClaims(w, camp, gatherers, record.StartDays,
                    record.Duration, intervals, abandoningVillages.Contains(camp.Id),
                    PassiveAvailable, campClaims[camp.Id]);
            }
            foreach (var camp in w.Camps.OrderBy(c => c.Id))
            {
                var claims = campClaims[camp.Id];
                double scale = Math.Min(1, RemainingFoodTarget(camp, needs[camp.Id]) / Math.Max(1e-12, claims.Where(d => !d.Manufacturing).Sum(d => d.Food)));
                double inputScale = Math.Min(1, needs[camp.Id].PlantFeedstockGap / Math.Max(1e-12, claims.Where(d => d.Manufacturing).Sum(d => d.Food)));
                foreach (var claim in claims)
                {
                    claim.Food *= claim.Manufacturing ? inputScale : scale;
                    demands.Add(claim);
                }
            }
            foreach (var hunter in w.Hunters.Where(h => h.Population > 0 && !Supply.Connected(w, h)).OrderBy(h => h.Id))
            {
                double need = PhysicalDemography.Enabled(w.Config)
                    ? PhysicalDemography.FoodPerPersonYear(w.Config) * PhysicalDemography.BirthOnlyPersonYears(EffectivePopulation(hunter), w.Config.BaseBirthRate, years)
                    : hunter.Population * w.Config.Consumption * intervals;
                demands.Add(new Demand { Tile = hunter.Tile, Entity = hunter.Id, Kind = "Foraging", ImpactMultiplier = PracticeEffects.GatheringImpact(w.Config, w.Camps.First(c => c.Id == hunter.SourceCamp)), Food = Math.Min(need, hunter.Population * w.Config.HunterForagePerPerson * intervals) });
            }
            var campTiles = w.Camps.ToDictionary(c => c.Id, c => c.Tile);
            // Wild herds claim first; managed herds then route their water, in herd-ID order, around sources the
            // claims already placed saturate (ManagedHerdLocalClaims.Append, residual split).
            foreach (var herd in w.Herds.OrderBy(h => h.Id))
            {
                var species = w.Config.GetSpecies(herd.Species);
                herd.UnmetFood = herd.Animals * species.FoodConsumption * intervals; herd.UnmetFreshwater = herd.Animals * species.FreshwaterConsumption * intervals;
                if (herd.DomesticCamp >= 0) continue;
                int source = FreshwaterAccess.Source(w, herd.Tile);
                demands.Add(new Demand { Tile = herd.Tile, Entity = herd.Id, Kind = "Herd", PotentialFood = herd.UnmetFood, Freshwater = source == herd.Tile ? herd.UnmetFreshwater : 0 });
                if (source != herd.Tile) demands.Add(new Demand { Tile = source, Entity = herd.Id, Kind = "Herd", Freshwater = herd.UnmetFreshwater });
            }
            foreach (var herd in w.Herds.Where(h => h.DomesticCamp >= 0).OrderBy(h => h.Id))
                ManagedHerdLocalClaims.Append(w,
                    campTiles.TryGetValue(herd.DomesticCamp, out int campTile) ? campTile : herd.Tile,
                    herd.Id, herd.UnmetFood, herd.UnmetFreshwater, horizon.Days, demands, null, demands);

            LastMetrics.SetupEconomyMilliseconds = phaseClock.Elapsed.TotalMilliseconds;
            phaseClock.Restart();
            var gatheredFoodForMaterials = CommitHorizonResources(w, demands, record, touch, foraged,
                horizon, agriculturalLabor, needs, abandoningVillages, fishingPlans);
            phaseClock.Restart();
            ResolveManagedHerds(w, record, horizon, needs);
            LastMetrics.ManagedHerdMilliseconds = phaseClock.Elapsed.TotalMilliseconds;
            phaseClock.Restart();

            var plans = w.Camps.OrderBy(c => c.Id).ToDictionary(c => c.Id, c => PlanGrowth(w, c, intervals, years, needs[c.Id]));
            LastMetrics.GrowthPlanMilliseconds = phaseClock.Elapsed.TotalMilliseconds;
            phaseClock.Restart();
            foreach (var camp in w.Camps.OrderBy(c => c.Id))
            {
                record.Demographics.First(d => d.Camp == camp.Id).FoodRequired += plans[camp.Id].Required;
                if (LastMetrics.CaptureWork)
                {
                    var flow = LastMetrics.CraftFoodFlowFor(camp.Id);
                    var need = needs[camp.Id];
                    flow.ConsumptionTarget = need.ConsumptionTarget;
                    flow.ReserveTarget = need.ReserveTarget;
                    flow.ReserveFillTarget = need.ReserveFillTarget;
                    flow.GrowthFoodTarget = plans[camp.Id].GrowthFood;
                    flow.ProjectedFood = plans[camp.Id].ProjectedFood;
                    flow.NetIncome = plans[camp.Id].NetIncome;
                    flow.PriorityIncome = plans[camp.Id].PriorityIncome;
                    flow.AvailableAfterReserve = plans[camp.Id].AvailableAfterReserve;
                    flow.PlantFoodAvailable = plans[camp.Id].PlantFoodAvailable;
                    flow.AnimalFoodAvailable = plans[camp.Id].AnimalFoodAvailable;
                    flow.ClothingPlantInput = plans[camp.Id].ClothingPlantInput;
                    flow.ClothingAnimalInput = plans[camp.Id].ClothingAnimalInput;
                    flow.RequestedPlantInput = plans[camp.Id].RequestedPlantInput;
                    flow.RequestedAnimalInput = plans[camp.Id].RequestedAnimalInput;
                    flow.BudgetedPlantInput = plans[camp.Id].PlantManufacturingInput;
                    flow.BudgetedAnimalInput = plans[camp.Id].AnimalManufacturingInput;
                }
            }
            foreach (var hunter in w.Hunters.Where(h => !Supply.Connected(w, h)).OrderBy(h => h.Id))
            {
                double required = PhysicalDemography.Enabled(w.Config)
                    ? PhysicalDemography.FoodPerPersonYear(w.Config) * PhysicalDemography.BirthOnlyPersonYears(EffectivePopulation(hunter), w.Config.BaseBirthRate, years)
                    : hunter.Population * w.Config.Consumption * intervals;
                record.Demographics.First(d => d.Camp == hunter.SourceCamp).FoodRequired += required;
            }
            var pendingBirths = new Dictionary<int, GrowthPlan>();
            var pendingCampDeaths = new List<(Camp Camp, Hunter[] Linked, int Losses)>();
            var pendingHunterDeaths = new List<(Hunter Hunter, int Losses)>();
            var physicalCampFood = new Dictionary<int, PhysicalFoodEnvelope>();
            var physicalHunterFood = new Dictionary<int, PhysicalFoodEnvelope>();
            var pendingPhysicalCamps = new List<PhysicalCampEvents>();
            var pendingPhysicalHunters = new List<PhysicalHunterEvents>();
            SustainGeneration(w, record, intervals, years, generationScale, needs, plans, foraged, recovering,
                pendingBirths, pendingCampDeaths, pendingHunterDeaths, physicalCampFood, physicalHunterFood, LastMetrics);
            Supply.Finish(w, record);
            LastMetrics.SustainMilliseconds = phaseClock.Elapsed.TotalMilliseconds;
            phaseClock.Restart();
            // Manufacturing and needs use the same assigned population. Unused gathering
            // worker-time can source materials; realized food gathering earns bridge effort.
            // Goods agreed for trade after production settle between every camp's production and every camp's
            // needs (Docs/Trade.md); without such goods the camps produce and meet needs in one pass as before.
            bool tradeAfterProduction = trade != null && trade.Plan.AfterProduction;
            var neolithicCamps = w.Camps.OrderBy(c => c.Id).ToArray();
            foreach (var camp in neolithicCamps)
            {
                var labor = agriculturalLabor[camp.Id];
                double gathered = gatheredFoodForMaterials.TryGetValue(camp.Id, out double amount) ? amount : 0;
                labor.GatheringWorkerIntervals = Math.Min(labor.Gatherers * intervals, gathered / w.Config.GatherPerPerson);
                double phaseBRawGathererIntervals = needs[camp.Id]?.Order?.RawMaterialAllowances?
                    .Where(a => a != null && a.Role == WorkforceRoles.Gatherer)
                    .Sum(a => Math.Max(0, a.WorkerIntervals)) ?? 0;
                double baselineMaterialGathererIntervals = Math.Max(0,
                    labor.Gatherers * intervals - labor.GatheringWorkerIntervals - phaseBRawGathererIntervals);
                NeolithicProduction.ResolveCampNeolithic(w, camp, horizon, record,
                    baselineMaterialGathererIntervals, needs[camp.Id], !tradeAfterProduction);
                if (CultivationEconomy.Enabled(camp))
                {
                    foreach (int id in labor.Footprint) { LazyEcology.MaterializeEcology(w, id, record.StartDays + record.Duration); w.Tiles[id].Cultivation = (float)CultivationEconomy.Intensity(w.Config, w.Tiles[id].CultivationCapital); }
                    foreach (int id in labor.Footprint) w.Tiles[id].CultivationOperatingFraction = 0;
                }
            }
            if (tradeAfterProduction)
            {
                Trade.SettleAfterProduction(w, trade, record);
                foreach (var camp in neolithicCamps)
                    if (camp.Population > 0) NeolithicProduction.ResolveNeeds(w, camp, horizon.Years, needs[camp.Id]?.Residents ?? camp.Population);
            }
            LastMetrics.ProductionMilliseconds = phaseClock.Elapsed.TotalMilliseconds;
            LastMetrics.ProductionReceipts = record.Production.Count;
            if (PhysicalDemography.Enabled(w.Config))
            {
                ReconcilePhysicalDemography(w, record, years, physicalCampFood, physicalHunterFood,
                    pendingPhysicalCamps, pendingPhysicalHunters);
                // Remote provisions may have been refunded after person-year closure.
                Supply.Finish(w, record);
            }
            ApplyStorage(w, record, intervals);

            // Raw material authorization is settled globally only after every
            // camp's baseline gather, production, optional goods trade, needs,
            // and storage have completed. Its stock is next-turn inventory.
            if (w.Config.ElderRawMaterialsEnabled &&
                w.Config.EconomicAllocation == EconomicAllocationStrategy.DemandControl)
                MaterialEconomy.SettleElderRawMaterials(w, needs, record);

            // Physical service and goods settle against the locked cohort. Gross natural events become authoritative
            // only after storage, so newborns cannot create same-turn capacity and deaths cannot erase it.
            foreach (var pending in pendingPhysicalCamps)
                ReconcilePhysicalEvents(pending.Camp, pending.Linked, pending.Cohorts, record);
            foreach (var pending in pendingPhysicalHunters)
                ReconcilePhysicalEvents(pending.Hunter, pending.Result, record);
            // Physical deaths can reduce a detached cohort after its last food settlement. Re-cap provisions at the
            // authoritative endpoint and attribute any population-dependent carrying overflow to the food ledger.
            if (PhysicalDemography.Enabled(w.Config)) Supply.Finish(w, record);

            // Taming commits at the end boundary: captured animals were part of
            // the wild herd's ecology this turn and become managed only for the
            // next decision/production horizon.
            HusbandryAcquisition.ResolveTaming(w, taming, record);
            if (w.Config.WildHerdFissionEnabled) SplitHerds(w, record);
            // Demographic losses are applied after this cohort has completed
            // production, needs and storage. They cannot reduce the workforce
            // retroactively, and no newborn is included in the loss base.
            foreach (var loss in pendingCampDeaths)
                ApplySharedDeaths(loss.Camp, loss.Linked, loss.Losses, record);
            foreach (var loss in pendingHunterDeaths)
            {
                loss.Hunter.Population -= loss.Losses; record.Deaths += loss.Losses;
                record.PopulationChanges.Add(new PopulationChange { Camp = loss.Hunter.SourceCamp, Entity = loss.Hunter.Id, Kind = "Starvation", Count = -loss.Losses });
            }
            foreach (var camp in w.Camps.OrderBy(c => c.Id))
                if (pendingBirths.TryGetValue(camp.Id, out var growth))
                    AddGrowth(w, camp, growth, record, record.Demographics.First(d => d.Camp == camp.Id));

            w.Days += record.Duration;
            FinishDemographicReports(w, record, orders);
        }

        internal static double PassiveFoodGathererIntervals(Camp camp, TurnNeeds needs, double availableFoodGatherers, double intervals)
        {
            if (camp?.EconomicLabor?.PolicyAuthority == true)
                return Math.Max(0, availableFoodGatherers - camp.EconomicLabor.MaterialGatherers);
            if (needs?.Order?.RawMaterialAllowances == null || needs.Order.RawMaterialAllowances.Count == 0)
                return Math.Max(0, availableFoodGatherers);
            double phaseA = needs?.Order?.PhaseAExtractionIntervals != null &&
                needs.Order.PhaseAExtractionIntervals.TryGetValue(WorkforceRoles.Gatherer, out double frozen)
                    ? frozen : 0;
            double phaseB = needs?.Order?.RawMaterialAllowances?.Where(a => a.Role == WorkforceRoles.Gatherer).Sum(a => a.WorkerIntervals) ?? 0;
            return Math.Max(0, availableFoodGatherers - (phaseA + phaseB) / Math.Max(1e-12, intervals));
        }

        // Membership is fixed after movement; all quantities below are refreshed at
        // each seasonal barrier. Scratch claims never escape into saved receipts.
        internal sealed class AgriculturalClaimScratch
        {
            public readonly int[] Footprint, Eligible, LandIndices;
            public readonly double[] Remaining;
            public readonly List<Demand> Claims = new List<Demand>();
            private readonly List<Demand> pool = new List<Demand>();
            private int used;
            public AgriculturalClaimScratch(WorldState w, Camp camp, int[] footprint)
            {
                Footprint = footprint;
                Eligible = w.Map.Radius(camp.Tile, w.Config.GatherRadius).Where(id => PassiveGathering.Eligible(w, camp, id)).OrderBy(id => id).ToArray();
                LandIndices = Enumerable.Range(0, Eligible.Length).Where(i => w.Tiles[Eligible[i]].Land).ToArray();
                Remaining = new double[Eligible.Length];
            }
            public void Clear() { Claims.Clear(); used = 0; }
            public Demand NextClaim()
            {
                if (used == pool.Count) pool.Add(new Demand());
                return pool[used++];
            }
        }

        // The ordinary mobile-camp claim path is shared with read-only food
        // forecasts. In particular an accepted village departure uses this
        // path, with bridge-level access on its former fields and no farm work.
        internal static void AddPassiveCampClaims(WorldState w, Camp camp, double gatherers,
            double startDays, double durationDays, double intervals, bool abandoning,
            Func<int, double> availability, List<Demand> claims)
        {
            if (gatherers <= 0) return;
            var requests = new List<Demand>(); double total = 0;
            foreach (int id in w.Map.Radius(camp.Tile, w.Config.GatherRadius).OrderBy(n => n))
            {
                if (!PassiveGathering.Eligible(w, camp, id)) continue;
                var tile = w.Tiles[id];
                double openingFood = availability(id);
                if (abandoning && tile.TraversableLand)
                {
                    // Abandonment leaves the physical field intact, but the
                    // mobile camp can access at most bridge-level q this turn,
                    // including on overlapping old tiles.
                    var view = LazyEcology.ProjectEcology(w, id, startDays);
                    double bridgeCapital = Math.Pow(w.Config.GathererBridgeCeiling /
                        w.Config.ReferenceIntensity, 2);
                    if (view.CultivationCapital > bridgeCapital)
                    {
                        view.CultivationCapital = bridgeCapital;
                        openingFood = Math.Min(openingFood,
                            new HorizonEcologyCapacity(w, tile, view, startDays,
                                durationDays, 0).Food);
                    }
                }
                double food = openingFood * Math.Min(1,
                    Numbers.Fraction(w.Config.GatherFraction, intervals) *
                    gatherers / w.Config.PopulationScale);
                requests.Add(new Demand { Entity = camp.Id, Tile = id,
                    Kind = PassiveGathering.Kind(tile),
                    ImpactMultiplier = tile.Aquatic
                        ? PracticeEffects.FishingImpact(w.Config, camp)
                        : PracticeEffects.GatheringImpact(w.Config, camp),
                    Food = food });
                total += food;
            }
            double scale = Math.Min(1, gatherers * w.Config.GatherPerPerson * intervals /
                Math.Max(1e-12, total));
            foreach (var request in requests) { request.Food *= scale; claims.Add(request); }
        }

        internal static void AddAgriculturalClaims(WorldState w, Camp camp, AgriculturalLabor labor, double intervals, double biological, double feedstock, Func<int, double> availability, List<Demand> claims, AgriculturalClaimScratch scratch)
        {
            var eligible = scratch.Eligible;
            // Operators harvest the same accessible terrestrial supply as gatherers, not a crop-only pool.
            var remaining = scratch.Remaining;
            for (int i = 0; i < eligible.Length; i++) remaining[i] = availability(eligible[i]);
            double Allocate(int[] indices, double requested, bool farmer, bool manufacturing)
            {
                int count = indices == null ? eligible.Length : indices.Length;
                double available = 0;
                for (int i = 0; i < count; i++) available += remaining[indices == null ? i : indices[i]];
                double accepted = Math.Min(Math.Max(0, requested), available);
                if (accepted <= 0) return 0;
                for (int i = 0; i < count; i++)
                {
                    int index = indices == null ? i : indices[i], id = eligible[index];
                    double amount = accepted * remaining[index] / available; remaining[index] = Math.Max(0, remaining[index] - amount);
                    var claim = scratch.NextClaim();
                    claim.Entity = camp.Id; claim.Tile = id; claim.Kind = farmer ? "Cultivation" : PassiveGathering.Kind(w.Tiles[id]);
                    claim.Food = amount; claim.Manufacturing = manufacturing; claim.PotentialFood = 0; claim.Freshwater = 0;
                    claim.ImpactMultiplier = w.Tiles[id].Aquatic ? PracticeEffects.FishingImpact(w.Config, camp) : PracticeEffects.GatheringImpact(w.Config, camp);
                    claims.Add(claim);
                }
                return accepted;
            }
            double productivity = labor.HarvestProductivity > 0 ? labor.HarvestProductivity
                : CultivationEconomy.HarvestProductivity(w, camp, labor.Footprint);
            double farmCapacity = labor.OperatingFarmers * productivity * w.Config.GatherPerPerson * intervals;
            double gatherCapacity = labor.FoodGatherers * w.Config.GatherPerPerson * intervals;
            double farmInput = Allocate(scratch.LandIndices, Math.Min(farmCapacity, feedstock), true, true);
            double gatheredInput = Allocate(scratch.LandIndices, Math.Min(gatherCapacity, feedstock - farmInput), false, true);
            double farm = Allocate(scratch.LandIndices, Math.Min(farmCapacity - farmInput, biological), true, false);
            Allocate(null, Math.Min(gatherCapacity - gatheredInput, biological - farm), false, false);
        }

        private void ResolveManagedHerds(WorldState w, TurnRecord record, TurnHorizon horizon, Dictionary<int, TurnNeeds> needs)
        {
            double intervals = horizon.ReferenceIntervals;
            double years = horizon.Years;
            double generationScale = horizon.PracticeScale;
            var managementByHerd = new Dictionary<int, double>();
            var managementPlans = new Dictionary<int, HerdingManagementPlan>();
            foreach (var camp in w.Camps.OrderBy(c => c.Id))
            {
                var plan = HerdingTooling.Plan(w, camp, horizon);
                managementPlans[camp.Id] = plan;
                if (activeElderDecisions.TryGetValue(camp.Id,
                        out var elderDecision))
                {
                    int totalHerders = camp.Workforce.Assignments
                        .Where(assignment => assignment.Role ==
                            WorkforceRoles.Herder)
                        .Sum(assignment => assignment.Population);
                    elderDecision.ResolvedHerdDevelopmentPeople = Math.Max(0,
                        totalHerders -
                            elderDecision.AcceptedRetainedHerdPeople);
                }
                foreach (var herd in w.Herds.Where(h => h.DomesticCamp == camp.Id && h.Animals > 0).OrderBy(h => h.Id))
                    managementByHerd[herd.Id] = plan.Fulfillment;
            }
            var potentialByCamp = new Dictionary<int, double>();
            var reportByHerd = new Dictionary<int, ManagedHerdReport>();
            foreach (var herd in w.Herds.Where(h => h.DomesticCamp >= 0).OrderBy(h => h.Id).ToArray())
            {
                if (herd.Animals <= 0)
                {
                    herd.AnimalFoodPotential = herd.AnimalFoodProduced = 0;
                    herd.LastNutrition = herd.LastFoodNutrition = herd.LastWaterNutrition = 1;
                    herd.ManagementFulfillment = 1;
                    herd.StartingAnimals = herd.SurvivingAnimals = herd.NewbornAnimals = herd.StarvationDeaths = 0;
                    herd.GrowthRemainder = 0;
                    herd.NutritionInitialized = true;
                    record.ManagedHerds.Add(new ManagedHerdReport { Herd = herd.Id, Camp = herd.DomesticCamp, Species = herd.Species, FoodNutrition = 1, WaterNutrition = 1, Nutrition = 1, ManagementFulfillment = 1 });
                    continue;
                }
                var species = w.Config.GetSpecies(herd.Species);
                herd.StartingAnimals = herd.Animals;
                double foodRequired = herd.Animals * species.FoodConsumption * intervals;
                double waterRequired = herd.Animals * species.FreshwaterConsumption * intervals;
                double foodNutrition = foodRequired <= 1e-12 ? 1 : Numbers.Clamp(1 - herd.UnmetFood / foodRequired, 0, 1);
                double waterNutrition = waterRequired <= 1e-12 ? 1 : Numbers.Clamp(1 - herd.UnmetFreshwater / waterRequired, 0, 1);
                double nutrition = Math.Min(foodNutrition, waterNutrition);
                double management = managementByHerd.TryGetValue(herd.Id, out double assignedCoverage) ? assignedCoverage : 0;
                herd.LastFoodNutrition = foodNutrition; herd.LastWaterNutrition = waterNutrition; herd.LastNutrition = nutrition; herd.ManagementFulfillment = management; herd.NutritionInitialized = true;
                if (nutrition < 1 - 1e-12) { herd.DeficitTurns++; herd.DeficitSeverity += (1 - nutrition) * generationScale; }
                else { herd.DeficitTurns = 0; herd.DeficitSeverity = 0; }
                int startingAnimals = herd.Animals;
                int deaths = 0;
                if (herd.DeficitTurns > w.Config.StarvationGraceTurns)
                {
                    deaths = StarvationLosses(startingAnimals, 1 - nutrition, w.Config.Mortality, generationScale, ref herd.DeathRemainder);
                    herd.Animals -= deaths;
                }
                herd.SurvivingAnimals = herd.Animals; herd.StarvationDeaths = deaths;
                double potential = ManagedAnimalFoodYield(w, herd.Animals, nutrition, management, intervals);
                herd.AnimalFoodPotential = potential;
                potentialByCamp[herd.DomesticCamp] = (potentialByCamp.TryGetValue(herd.DomesticCamp, out double total) ? total : 0) + herd.AnimalFoodPotential;
                var managementPlan = managementPlans.TryGetValue(herd.DomesticCamp, out var realizedManagement) ? realizedManagement : null;
                double coveredShare = managementPlan == null || managementPlan.Animals <= 0 ? 0 : managementPlan.CoveredHerderYears * herd.StartingAnimals / managementPlan.Animals;
                double toolsShare = managementPlan == null || managementPlan.Animals <= 0 ? 0 : managementPlan.ToolsConsumed * herd.StartingAnimals / managementPlan.Animals;
                reportByHerd[herd.Id] = new ManagedHerdReport { Herd = herd.Id, Camp = herd.DomesticCamp, Species = herd.Species, StartingAnimals = herd.StartingAnimals, SurvivingAnimals = herd.SurvivingAnimals, StarvationDeaths = deaths, FoodNutrition = foodNutrition, WaterNutrition = waterNutrition, Nutrition = nutrition, ManagementFulfillment = management, PotentialFood = herd.AnimalFoodPotential, CoveredHerderYears = coveredShare, HerdingToolsConsumed = toolsShare };
            }
            // Herding capacity is snapshotted from stock present at the start of
            // management. Debit its realized covered work before food/growth
            // projections so replacement planning sees post-wear inventory.
            foreach (var camp in w.Camps.OrderBy(c => c.Id))
            {
                if (!managementPlans.TryGetValue(camp.Id, out var plan)) continue;
                if (plan.ToolsConsumed > 0) camp.Inventory.HerdingTools = Math.Max(0, camp.Inventory.HerdingTools - plan.ToolsConsumed);
            }
            // Project the entire potential receipt through the same growth/input
            // solver used by SustainGeneration.  Then cap the receipt and make
            // one final projection at that accepted amount; this keeps a birth
            // threshold and its post-growth Fabric customer on one deterministic
            // budget without fixed-point iteration over integer population.
            foreach (var camp in w.Camps.OrderBy(c => c.Id))
            {
                var campHerds = w.Herds.Where(h => h.DomesticCamp == camp.Id && reportByHerd.ContainsKey(h.Id)).OrderBy(h => h.Id).ToArray();
                if (campHerds.Length == 0) continue;
                double potential = potentialByCamp.TryGetValue(camp.Id, out double projectedPotential) ? projectedPotential : 0;
                double acceptedTotal = 0;
                if (potential > 0)
                {
                    double target = RemainingFoodTarget(camp, needs[camp.Id]);
                    double tentativeInput = PlanGrowth(w, camp, intervals, years, needs[camp.Id], potential).ManufacturingInput;
                    double headroom = Math.Max(0, target + tentativeInput);
                    double acceptedBudget = Math.Min(potential, headroom);
                    double finalInput = PlanGrowth(w, camp, intervals, years, needs[camp.Id], acceptedBudget).ManufacturingInput;
                    double finalHeadroom = Math.Max(0, target + finalInput);
                    acceptedTotal = Math.Min(acceptedBudget, finalHeadroom);
                }
                double remaining = acceptedTotal;
                foreach (var herd in campHerds)
                {
                    double accepted = Math.Min(herd.AnimalFoodPotential, Math.Max(0, remaining));
                    if (accepted > 0)
                    {
                        camp.AnimalFood += accepted; camp.StepFoodIncome += accepted; remaining -= accepted;
                        record.Transfers.Add(new Transfer { Tile = herd.Tile, Entity = camp.Id, Kind = "Animal Product", Food = accepted });
                    }
                    herd.AnimalFoodProduced = accepted;
                    var species = w.Config.GetSpecies(herd.Species);
                    int newborns = 0;
                    if (herd.Animals > 0)
                    {
                        // Forage and water were claimed for the prior cohort.
                        // An unmet adult need leaves no demonstrated support for
                        // newborns. Pause breeding progress until both needs are
                        // met; the fully nourished exponential remains unchanged.
                        if (herd.LastNutrition >= 1 - 1e-12)
                        {
                            double breeding = camp.ManagedBreedingIntensity;
                            double reproductionTarget = (herd.Animals + herd.GrowthRemainder) * Math.Exp(species.GrowthRate * breeding * years * herd.LastNutrition);
                            newborns = Math.Max(0, (int)Math.Floor(reproductionTarget) - herd.Animals);
                            herd.GrowthRemainder = reproductionTarget - Math.Floor(reproductionTarget); herd.Animals += newborns;
                        }
                    }
                    else herd.GrowthRemainder = 0;
                    herd.NewbornAnimals = newborns;
                    var report = reportByHerd[herd.Id]; report.NewbornAnimals = newborns; report.AcceptedFood = accepted; record.ManagedHerds.Add(report);
                }
                // Controlled slaughter after breeding (Docs/HerdLabour.md): animals above the camp's chosen herd target become carcass food.
                HerdLabour.Slaughter(w, camp, campHerds, reportByHerd, record);
            }
        }

        private static int StarvationLosses(int population, double deficitFraction, double mortality, double generationScale, ref double remainder)
        {
            if (population <= 0 || deficitFraction <= 0 || generationScale <= 0) return 0;
            double lossProgress = remainder + population * Numbers.Fraction(mortality, generationScale) * Numbers.Clamp(deficitFraction, 0, 1);
            int losses = Math.Min(population, (int)Math.Floor(lossProgress)); remainder = lossProgress - Math.Floor(lossProgress); return losses;
        }

        public static double[] ReferenceSeasonWeights(double startDays, double duration, double baseDays)
        {
            var weights = new double[4]; double position = startDays / baseDays, remaining = duration / baseDays;
            double first = Math.Min(remaining, Math.Ceiling(position + 1e-10) - position);
            if (first > 1e-10) { weights[((int)Math.Floor(position) % 4 + 4) % 4] += first; position += first; remaining -= first; }
            double cycles = Math.Floor(remaining / 4); for (int i = 0; i < 4; i++) weights[i] += cycles; position += cycles * 4; remaining -= cycles * 4;
            while (remaining > 1e-10)
            {
                double part = Math.Min(1, remaining); weights[((int)Math.Floor(position + 1e-9) % 4 + 4) % 4] += part; position += part; remaining -= part;
            }
            return weights;
        }

        private static GrowthPlan PlanGrowth(WorldState w, Camp camp, double intervals, double years, TurnNeeds needs, double incomingAnimalFood = 0)
        {
            int reproductivePopulation = needs.FoodConsumers;
            double populationBase = needs.ConsumptionRequired;
            double priorityIncome = needs.ConsumptionTarget + needs.ReserveFillTarget;
            double projectedFood = camp.Food + Math.Max(0, incomingAnimalFood);
            // Provision withdrawals and return deposits change the remaining
            // ledger, never this cohort's prepared consumption or reserve size.
            double netIncome = projectedFood - needs.OpeningCampFood;
            // Order: consumption, then the clothing-need feedstock, then the
            // operating-reserve refill, then growth together with every other
            // manufacturing input. The clothing part is paid even when the
            // reserve is short; it shrinks the refill and growth budget instead.
            double clothingPlant = Math.Min(Math.Max(0, needs.PlantClothingFeedstock), Math.Max(0, camp.PlantFood));
            double clothingAnimal = Math.Min(Math.Max(0, needs.AnimalClothingFeedstock), Math.Max(0, camp.AnimalFood + incomingAnimalFood));
            double afterMeals = Math.Max(0, projectedFood - populationBase);
            // Diagnostic prototype: an Elder-committed clothing split is paid
            // ahead of meals (bounded by the food actually present).
            double clothingRoom = needs.ProtectedClothing ? Math.Max(0, projectedFood) : afterMeals;
            clothingPlant = Math.Min(clothingPlant, clothingRoom);
            clothingAnimal = Math.Min(clothingAnimal, Math.Max(0, clothingRoom - clothingPlant));
            double clothing = clothingPlant + clothingAnimal;
            double available = netIncome + 1e-8 >= priorityIncome + clothing && projectedFood + 1e-8 >= populationBase + clothing + needs.ReserveTarget
                ? Math.Min(Math.Max(0, netIncome - priorityIncome - clothing), Math.Max(0, projectedFood - populationBase - clothing - needs.ReserveTarget))
                : 0;
            double maximumRate = QoLMortality.MaximumRate(w, camp);
            bool physical = PhysicalDemography.Enabled(w.Config);
            double maximumGrowthFood = GrowthFood(populationBase, maximumRate, years);
            // Physical births are funded from every real edible unit left after current meals and the clothing feedstock.
            // A partially filled policy reserve remains a finite stock, but cannot act as the retired all-or-nothing
            // fertility switch. Legacy planning keeps its exact reserve-first growth allocation through `available`.
            double demographicAvailable = physical
                ? Math.Max(0, PhysicalDemographicFoodBudget(projectedFood, populationBase, maximumGrowthFood, clothing) - populationBase)
                : available;
            GrowthPlan ForRate(double rate)
            {
                double growthFood = GrowthFood(populationBase, rate, years);
                double finalPopulation = reproductivePopulation * Math.Exp(rate * years);
                // Field work has already consumed the turn-start FarmTools when
                // growth is planned; Pots still depreciate in ResolveCampNeolithic.
                // Fabric demand is a joint growth/input cost.  Forecast may report
                // the recipe's useful demand even when the camp has no plant-food
                // provenance left; charge only the plant input that can actually be
                // paid by this candidate.  Keeping this clamp inside the rate search
                // prevents unavailable plant feedstock from suppressing births.
                double plantInput = Math.Min(needs.PlantFeedstock, Math.Max(0, camp.PlantFood));
                double animalInput = Math.Min(needs.AnimalFeedstock, Math.Max(0, camp.AnimalFood + incomingAnimalFood));
                plantInput = Math.Max(plantInput, clothingPlant); animalInput = Math.Max(animalInput, clothingAnimal);
                return new GrowthPlan { Required = populationBase + growthFood, ConsumptionRequired = populationBase, GrowthFood = growthFood, ReproductivePopulation = reproductivePopulation, FinalPopulation = finalPopulation, PlantManufacturingInput = plantInput, AnimalManufacturingInput = animalInput, ManufacturingInput = plantInput + animalInput };
            }
            // The integer birth outcome is the Fabric customer. Search the monotone
            // combined cost instead of iterating residents, which can ping-pong at a
            // birth threshold when one extra set of clothes removes one birth.
            double low = 0, high = maximumRate;
            if (maximumRate > 1e-12 && demographicAvailable > 1e-12)
            {
                for (int iteration = 0; iteration < 64; iteration++)
                {
                    double rate = (low + high) * .5;
                    var plan = ForRate(rate);
                    double cost = physical ? plan.GrowthFood : plan.GrowthFood + plan.ManufacturingInput - clothing;
                    if (cost <= demographicAvailable + 1e-8) low = rate; else high = rate;
                }
            }
            var result = ForRate(low);
            if (needs.ProtectedClothing) result.ProtectedClothingFood = clothing;
            if (ResolutionMetrics.CaptureCraftWorkDiagnostics || w.EconomicDiagnosticCapture)
            {
                result.ProjectedFood = projectedFood; result.NetIncome = netIncome;
                result.PriorityIncome = priorityIncome; result.AvailableAfterReserve = available;
                result.PlantFoodAvailable = Math.Max(0, camp.PlantFood);
                result.AnimalFoodAvailable = Math.Max(0, camp.AnimalFood + incomingAnimalFood);
                result.ClothingPlantInput = clothingPlant; result.ClothingAnimalInput = clothingAnimal;
                result.RequestedPlantInput = result.PlantManufacturingInput;
                result.RequestedAnimalInput = result.AnimalManufacturingInput;
            }
            double manufacturingBudget = Math.Max(0, available - result.GrowthFood);
            double otherPlant = Math.Min(result.PlantManufacturingInput - clothingPlant, manufacturingBudget);
            manufacturingBudget -= otherPlant;
            double otherAnimal = Math.Min(result.AnimalManufacturingInput - clothingAnimal, manufacturingBudget);
            result.PlantManufacturingInput = clothingPlant + Math.Max(0, otherPlant);
            result.AnimalManufacturingInput = clothingAnimal + Math.Max(0, otherAnimal);
            result.ManufacturingInput = result.PlantManufacturingInput + result.AnimalManufacturingInput;
            if (physical)
            {
                // Parallel preparation can differ by a few ulps in economic forecasts. The mortality kernel must receive
                // one canonical physical service envelope, independent of worker partitioning or collection order.
                result.GrowthFood = Math.Round(result.GrowthFood, 9, MidpointRounding.AwayFromZero);
                result.Required = populationBase + result.GrowthFood;
            }
            return result;
        }

        internal static void FoodBucketTargets(WorldState w, Camp camp, double intervals, double years, double populationBase, double initialFood, out double consumptionIncome, out double reserveTarget, out double reserveFill, out double maximumGrowthFood)
        {
            FoodBucketBasis(w, camp, intervals, years, populationBase, out reserveTarget, out maximumGrowthFood);
            SplitFoodBuckets(populationBase, initialFood, reserveTarget, out consumptionIncome, out reserveFill);
        }
        // The camp/population part of the bucket targets, independent of the
        // opening food; planners that vary only the food reuse it.
        internal static void FoodBucketBasis(WorldState w, Camp camp, double intervals, double years, double populationBase, out double reserveTarget, out double maximumGrowthFood)
        {
            double maximumRate = QoLMortality.MaximumRate(w, camp);
            // The reserve bucket belongs to the people who exist when this turn resolves and remains bounded by local
            // storage. Legacy growth allocation refills it before births; PhysicalMortalityV1 uses the same target for
            // economic demand but PhysicalDemographicFoodBudget prevents it from becoming a fertility gate.
            double desiredReserve = NeolithicProduction.DesiredReserveFood(w, populationBase / Math.Max(1e-12, w.Config.Consumption * intervals), intervals);
            // Pots depreciate later in ResolveCampNeolithic.  Reserve and growth
            // projection must use their post-depreciation capacity now so every
            // food source shares the same receiving envelope and ApplyStorage
            // does not discard an accepted receipt.
            double achievableStorage = NeolithicProduction.StorageCapacityAfterPotDepreciation(w, camp, intervals, years);
            reserveTarget = Math.Min(desiredReserve, achievableStorage);
            maximumGrowthFood = GrowthFood(populationBase, maximumRate, years);
        }
        internal static void SplitFoodBuckets(double populationBase, double initialFood, double reserveTarget, out double consumptionIncome, out double reserveFill)
        {
            double expiringInventory = Math.Max(0, initialFood - reserveTarget);
            double inventoryConsumedFirst = Math.Min(populationBase, expiringInventory);
            consumptionIncome = populationBase - inventoryConsumedFirst;
            double inventoryAfterConsumption = initialFood - inventoryConsumedFirst;
            reserveFill = Math.Max(0, reserveTarget - inventoryAfterConsumption);
        }

        internal static double GrowthFood(double populationBase, double rate, double years)
        {
            if (rate <= 1e-12 || years <= 0) return 0;
            double x = rate * years;
            return populationBase * ((Math.Exp(x) - 1) / x - 1);
        }

        // One physical allocation rule shared by prediction and resolution. `protectedFood` is a validated, typed
        // non-meal debit (currently clothing feedstock in resolution); operating-reserve targets never gate this budget.
        internal static double PhysicalDemographicFoodBudget(double totalFood, double currentNeed,
            double maximumGrowthFood, double protectedFood)
            => Math.Min(Math.Max(0, currentNeed) + Math.Max(0, maximumGrowthFood),
                Math.Max(0, totalFood - Math.Max(0, protectedFood)));

        private static double GrowthRateForFood(double populationBase, double maximumRate, double years, double availableGrowthFood)
        {
            if (maximumRate <= 1e-12 || years <= 0 || availableGrowthFood <= 1e-12) return 0;
            if (GrowthFood(populationBase, maximumRate, years) <= availableGrowthFood + 1e-8) return maximumRate;
            double low = 0, high = maximumRate;
            for (int iteration = 0; iteration < 64; iteration++)
            {
                double middle = (low + high) * .5;
                if (GrowthFood(populationBase, middle, years) <= availableGrowthFood) low = middle; else high = middle;
            }
            return low;
        }

        // Economic demand still asks for current consumption, the configured reserve and maximum newborn service.
        // Physical allocation uses PhysicalDemographicFoodBudget so reserve completion is not a fertility gate.
        // Already-realized connected hunting consumes this demand.
        public static double UsefulCampFoodDemand(WorldState w, Camp camp, double intervals, double years)
            => BiologicalFoodDemand(w, camp, intervals, years) + FabricFeedstockShortfall(w, camp, intervals, years, camp.Population);
        // Turn-start planning uses the very same consumption/reserve/growth buckets,
        // but never mistakes last turn's StepFoodIncome for an incoming shipment.
        public static double PlanningFoodDemand(WorldState w, Camp camp)
        {
            double populationBase = PlanningPopulation(w, camp) * w.Config.Consumption * w.Config.GenerationIntervals;
            FoodBucketTargets(w, camp, w.Config.GenerationIntervals, w.Config.TurnDays / w.Config.DaysPerYear, populationBase, camp.Food, out double consumption, out _, out double reserve, out double growth);
            return consumption + reserve + growth;
        }
        private static int PlanningPopulation(WorldState w, Camp camp) => camp.Population + w.Hunters.Where(h => h.SourceCamp == camp.Id && Supply.InRange(w, camp, h.Tile)).Sum(h => h.Population);

        // The target was frozen before movement. The only live term is the
        // change in inventory: harvest and returned provisions reduce the
        // remainder; provisions withdrawn for departing hunters increase it.
        internal static double RemainingFoodTarget(Camp camp, TurnNeeds needs, bool includeAnimalFeedstock = false)
        {
            double netIncome = camp.Food - needs.OpeningCampFood;
            if (!includeAnimalFeedstock) return Math.Max(0, needs.FoodTarget - netIncome);
            // Carcasses can fill both the common food budget and typed Animal
            // Fabric input. Other food covers the common budget first, while
            // an AnimalFood receipt can satisfy either target only once.
            double netAnimal = camp.AnimalFood - needs.OpeningAnimalFood;
            double netOther = netIncome - netAnimal;
            return Math.Max(0, Math.Max(0, needs.FoodTarget - netOther)
                + needs.AnimalFeedstockGap - netAnimal);
        }

        public static double PlanningResidentPopulation(WorldState w, Camp camp, double biologicalIncome)
            => PlanningResidentPopulation(w, camp, w.Config.GenerationIntervals, w.Config.TurnDays / w.Config.DaysPerYear, biologicalIncome, camp.Food);
        private static double PlanningResidentPopulation(WorldState w, Camp camp, double intervals, double years, double biologicalIncome, double initialFood)
        {
            int reproductive = PlanningPopulation(w, camp);
            double populationBase = reproductive * w.Config.Consumption * intervals;
            FoodBucketTargets(w, camp, intervals, years, populationBase, initialFood, out double consumption, out _, out double reserve, out double growth);
            if (PhysicalDemography.Enabled(w.Config))
            {
                // This narrow pure projection shares the resolver's reserve-independent newborn-service rule. It receives
                // untyped biological food and sees only existing textiles; callers that model production/imports must
                // account for typed clothing feedstock and future textile receipts in their own economic forecast.
                double projectedFood = Math.Max(0, initialFood) + Math.Max(0, biologicalIncome);
                double foodBudget = PhysicalDemographicFoodBudget(projectedFood, populationBase, growth, 0);
                double clothingBudget = Math.Max(0, camp.Inventory.Cloth) + Math.Max(0, camp.Inventory.AnimalFabric);
                double housing = NeolithicProduction.HousingCapacity(w, camp, w.Days).Total;
                double hunterRemainders = w.Hunters.Where(h => h.SourceCamp == camp.Id && Supply.InRange(w, camp, h.Tile))
                    .OrderBy(h => h.Id).Sum(h => h.GrowthRemainder - h.DeathRemainder);
                double effective = Math.Max(0, reproductive + camp.GrowthRemainder - camp.DeathRemainder + hunterRemainders);
                var physical = PhysicalDemography.AdvanceBudget(w.Config, effective, years, foodBudget, clothingBudget,
                    housing, camp.FoodDeficitYears, true);
                double change = physical.FinalPopulation - effective;
                return change >= 0 ? camp.Population + change
                    : Math.Max(0, camp.Population + change * camp.Population / Math.Max(1, reproductive));
            }
            double maximumRate = QoLMortality.MaximumRate(w, camp);
            GrowthPlan ForRate(double rate)
            {
                double finalPopulation = reproductive * Math.Exp(rate * years);
                int residents = camp.Population + (int)Math.Floor(camp.GrowthRemainder + Math.Max(0, finalPopulation - reproductive));
                // Turn-start planning projects future field/feedstock claims;
                // it must not clamp that projection to the camp's current
                // PlantFood stock before those claims are resolved.
                return new GrowthPlan { GrowthFood = GrowthFood(populationBase, rate, years), ReproductivePopulation = reproductive, FinalPopulation = finalPopulation, ManufacturingInput = NeolithicProduction.FabricFeedstockNeed(w, camp, intervals, years, residents) };
            }
            // The rate search already enforces the configured maximum rate. Keep
            // the full post-priority food surplus here so plant feedstock is not
            // accidentally capped by the biological growth bucket.
            double available = Math.Max(0, biologicalIncome - consumption - reserve);
            double low = 0, high = maximumRate;
            if (maximumRate > 1e-12 && available > 1e-12)
            {
                for (int iteration = 0; iteration < 64; iteration++)
                {
                    double rate = (low + high) * .5;
                    var plan = ForRate(rate);
                    if (plan.GrowthFood + plan.ManufacturingInput <= available + 1e-8) low = rate; else high = rate;
                }
            }
            var result = ForRate(low);
            return camp.Population + (int)Math.Floor(camp.GrowthRemainder + Math.Max(0, result.FinalPopulation - reproductive));
        }
        private static double BiologicalFoodDemand(WorldState w, Camp camp, double intervals, double years)
        {
            int reproductivePopulation = camp.Population + w.Hunters.Where(h => h.SourceCamp == camp.Id && Supply.Connected(w, h)).Sum(h => h.Population);
            double populationBase = reproductivePopulation * w.Config.Consumption * intervals;
            double initialFood = Math.Max(0, camp.Food - camp.StepFoodIncome);
            FoodBucketTargets(w, camp, intervals, years, populationBase, initialFood, out double consumptionIncome, out _, out double reserveFill, out double maximumGrowthFood);
            double totalIncomeTarget = consumptionIncome + reserveFill + maximumGrowthFood;
            return Math.Max(0, totalIncomeTarget - camp.StepFoodIncome);
        }

        private static double FabricFeedstockShortfall(WorldState w, Camp camp, double intervals, double years, double residentPopulation)
        {
            var input = NeolithicProduction.FabricFeedstockInputs(w, camp, intervals, years, residentPopulation);
            if (input.Total <= 0) return 0;
            int population = camp.Population + w.Hunters.Where(h => h.SourceCamp == camp.Id && Supply.Connected(w, h)).Sum(h => h.Population);
            double consumption = population * w.Config.Consumption * intervals;
            double reserve = Math.Min(NeolithicProduction.DesiredReserveFood(w, population, intervals), NeolithicProduction.StorageCapacityAfterPotDepreciation(w, camp, intervals, years));
            double spareFood = Math.Max(0, camp.Food - consumption - reserve);
            double sparePlant = Math.Min(camp.PlantFood, spareFood);
            double spareAnimal = Math.Min(camp.AnimalFood, Math.Max(0, spareFood - sparePlant));
            return Math.Max(0, input.Total - Math.Min(input.PlantFood, sparePlant) - Math.Min(input.AnimalFood, spareAnimal));
        }
        private static double PlantFabricFeedstockShortfall(WorldState w, Camp camp, double intervals, double years, double residentPopulation)
        {
            double input = NeolithicProduction.FabricFeedstockInputs(w, camp, intervals, years, residentPopulation).PlantFood;
            if (input <= 0) return 0;
            int population = camp.Population + w.Hunters.Where(h => h.SourceCamp == camp.Id && Supply.Connected(w, h)).Sum(h => h.Population);
            double consumption = population * w.Config.Consumption * intervals;
            double reserve = Math.Min(NeolithicProduction.DesiredReserveFood(w, population, intervals), NeolithicProduction.StorageCapacityAfterPotDepreciation(w, camp, intervals, years));
            double sparePlant = Math.Min(camp.PlantFood, Math.Max(0, camp.Food - consumption - reserve));
            return Math.Max(0, input - sparePlant);
        }

        // MaterialEconomy.FabricFeedstockNeed is deliberately a useful-demand
        // forecast: it must not invent a second plant-food inventory.  Growth
        // planning therefore uses the same forecast while charging only the
        // plant provenance currently available to the camp.  This helper is
        // pure and is used from every growth-rate search and the pre-growth
        // headroom projection, so the integer birth boundary cannot see a
        // phantom fabric debit.
        private static ManufacturingFoodPlan ActualFabricFeedstockNeed(WorldState w, Camp camp, double intervals, double years, double residentPopulation, double incomingAnimalFood = 0, bool fieldWorkAlreadyResolved = true, bool potsAlreadyDecayed = false)
        {
            var forecast = NeolithicProduction.FabricFeedstockInputs(w, camp, intervals, years, residentPopulation, fieldWorkAlreadyResolved, potsAlreadyDecayed, Math.Max(0, camp.PlantFood), Math.Max(0, camp.AnimalFood + incomingAnimalFood));
            forecast.PlantFood = Math.Min(Math.Max(0, camp.PlantFood), Math.Max(0, forecast.PlantFood));
            forecast.AnimalFood = Math.Min(Math.Max(0, camp.AnimalFood + incomingAnimalFood), Math.Max(0, forecast.AnimalFood));
            return forecast;
        }

        private static void ConsumeCampFood(Camp camp, double amount, double plantManufacturingInput, double animalManufacturingInput)
        {
            // Basic needs always get paid. Retain only typed food inputs that fit
            // beyond the current consumption debit; no Animal Product can become
            // a PlantFood craft input or vice versa.
            double afterNeeds = Math.Max(0, camp.Food - amount);
            double protectedPlant = Math.Min(camp.PlantFood, Math.Min(afterNeeds, plantManufacturingInput));
            double protectedAnimal = Math.Min(camp.AnimalFood, Math.Min(Math.Max(0, afterNeeds - protectedPlant), animalManufacturingInput));
            if (protectedPlant + protectedAnimal <= 0) { camp.Food -= amount; return; }
            double available = camp.Food - protectedPlant - protectedAnimal;
            double retained = available > 0 ? Math.Max(0, available - amount) / available : 0;
            camp.PlantFood = protectedPlant + (camp.PlantFood - protectedPlant) * retained;
            camp.AnimalFood = protectedAnimal + (camp.AnimalFood - protectedAnimal) * retained; camp.AquaticFood *= retained;
        }

        private sealed class PhysicalFoodEnvelope
        {
            public double Plant, Animal, Aquatic, Local, Carried;
            public double Stored => Plant + Animal + Aquatic + Carried;
            public double Total => Stored + Local;
        }

        private sealed class PhysicalCampEvents { public Camp Camp; public Hunter[] Linked; public DemographicResult[] Cohorts; }
        private sealed class PhysicalHunterEvents { public Hunter Hunter; public DemographicResult Result; }

        private static void SustainGeneration(WorldState w, TurnRecord record, double intervals, double years, double generationScale, Dictionary<int, TurnNeeds> needs, Dictionary<int, GrowthPlan> plans, Dictionary<int, double> foraged, HashSet<int> recovering, Dictionary<int, GrowthPlan> pendingBirths,
            List<(Camp Camp, Hunter[] Linked, int Losses)> pendingCampDeaths, List<(Hunter Hunter, int Losses)> pendingHunterDeaths,
            Dictionary<int, PhysicalFoodEnvelope> physicalCampFood, Dictionary<int, PhysicalFoodEnvelope> physicalHunterFood, ResolutionMetrics metrics)
        {
            bool physical = PhysicalDemography.Enabled(w.Config);
            foreach (var camp in w.Camps.OrderBy(c => c.Id))
            {
                var linked = w.Hunters.Where(h => h.SourceCamp == camp.Id && Supply.Connected(w, h)).OrderBy(h => h.Id).ToArray();
                int actualConsumers = camp.Population + linked.Sum(h => h.Population);
                int total = needs[camp.Id].FoodConsumers;
                if (actualConsumers != total) throw new InvalidOperationException("Prepared food cohort changed during resolution for camp " + camp.Id + ".");
                double required = needs[camp.Id].ConsumptionRequired;
                if (physical)
                {
                    double plant = camp.PlantFood, animal = camp.AnimalFood, aquatic = camp.AquaticFood;
                    ConsumeCampFood(camp, Math.Min(plans[camp.Id].Required, plans[camp.Id].ProtectedClothingFood > 0 ? Math.Max(0, camp.Food - plans[camp.Id].ProtectedClothingFood) : camp.Food), plans[camp.Id].PlantManufacturingInput, plans[camp.Id].AnimalManufacturingInput);
                    var envelope = new PhysicalFoodEnvelope
                    {
                        Plant = Math.Max(0, plant - camp.PlantFood), Animal = Math.Max(0, animal - camp.AnimalFood),
                        Aquatic = Math.Max(0, aquatic - camp.AquaticFood)
                    };
                    physicalCampFood[camp.Id] = envelope;
                    camp.LastConsumed = envelope.Total;
                    camp.FoodDeficit = Math.Max(0, required - envelope.Total);
                    camp.FoodNeedsMetRatio = required > 0 ? Math.Min(1, envelope.Total / required) : 1;
                    foreach (var h in linked)
                    {
                        h.FoodDeficit = camp.FoodDeficit * h.Population / Math.Max(1, total);
                        h.LastConsumed = envelope.Total * h.Population / Math.Max(1, total);
                    }
                    continue;
                }
                camp.LastConsumed = Math.Min(required, camp.Food); ConsumeCampFood(camp, camp.LastConsumed, plans[camp.Id].PlantManufacturingInput, plans[camp.Id].AnimalManufacturingInput); camp.FoodDeficit = required - camp.LastConsumed;
                camp.FoodNeedsMetRatio = required > 0 ? camp.LastConsumed / required : 1;
                if (camp.FoodDeficit > 1e-8) { camp.DeficitTurns++; camp.DeficitSeverity += camp.FoodDeficit / Math.Max(1, required) * generationScale; }
                else { camp.DeficitTurns = 0; camp.DeficitSeverity = 0; }
                if (camp.DeficitTurns > w.Config.StarvationGraceTurns)
                {
                    int losses = StarvationLosses(total, camp.FoodDeficit / Math.Max(1e-9, required), w.Config.Mortality, generationScale, ref camp.DeathRemainder);
                    pendingCampDeaths.Add((camp, linked, losses)); record.Log("Tribe " + camp.Player + " lost " + losses + " people to sustained hunger.");
                }
                foreach (var h in linked) { h.DeficitTurns = camp.DeficitTurns; h.FoodDeficit = camp.FoodDeficit * h.Population / Math.Max(1, total); h.LastConsumed = Math.Max(0, h.Population * w.Config.Consumption * intervals - h.FoodDeficit); }
                var summary = record.Demographics.First(d => d.Camp == camp.Id);
                if (camp.FoodDeficit > 1e-8) summary.DeficitSteps++;
                else if (recovering.Contains(camp.Id)) summary.RecoverySteps++;
                else
                {
                    double growthFood = Math.Min(plans[camp.Id].GrowthFood, camp.Food);
                    ConsumeCampFood(camp, growthFood, plans[camp.Id].PlantManufacturingInput, plans[camp.Id].AnimalManufacturingInput); camp.LastConsumed += growthFood;
                    summary.GrowthFoodAccepted += growthFood;
                    if (metrics.CaptureWork)
                        metrics.CraftFoodFlowFor(camp.Id).GrowthFoodUsed = growthFood;
                    pendingBirths[camp.Id] = plans[camp.Id];
                }
                summary.FoodConsumed += camp.LastConsumed;
            }
            foreach (var hunter in w.Hunters.Where(h => !Supply.Connected(w, h)).OrderBy(h => h.Id))
            {
                double required = physical
                    ? PhysicalDemography.FoodPerPersonYear(w.Config) * PhysicalDemography.BirthOnlyPersonYears(EffectivePopulation(hunter), w.Config.BaseBirthRate, years)
                    : hunter.Population * w.Config.Consumption * intervals;
                double local = Math.Min(required, foraged.TryGetValue(hunter.Id, out var gathered) ? gathered : 0);
                double carried = Math.Min(required - local, hunter.CarriedFood); hunter.CarriedFood -= carried;
                hunter.LastConsumed = local + carried; hunter.FoodDeficit = required - hunter.LastConsumed;
                if (physical)
                {
                    physicalHunterFood[hunter.Id] = new PhysicalFoodEnvelope { Local = local, Carried = carried };
                    continue;
                }
                if (hunter.FoodDeficit > 1e-8) { hunter.DeficitTurns++; hunter.DeficitSeverity += hunter.FoodDeficit / Math.Max(1, required) * generationScale; }
                else { hunter.DeficitTurns = 0; hunter.DeficitSeverity = 0; }
                if (hunter.DeficitTurns > w.Config.StarvationGraceTurns)
                {
                    int deaths = StarvationLosses(hunter.Population, hunter.FoodDeficit / Math.Max(1e-9, required), w.Config.Mortality, generationScale, ref hunter.DeathRemainder);
                    pendingHunterDeaths.Add((hunter, deaths));
                    record.Log("Expedition " + hunter.Id + " lost " + deaths + " people to hunger beyond supply range.");
                }
                var summary = record.Demographics.First(d => d.Camp == hunter.SourceCamp); summary.FoodConsumed += hunter.LastConsumed; if (hunter.FoodDeficit > 1e-8) summary.DeficitSteps++;
            }
        }

        private static double EffectivePopulation(Hunter hunter)
            => Math.Max(0, hunter.Population + hunter.GrowthRemainder - hunter.DeathRemainder);

        private static double EffectivePopulation(Camp camp, IEnumerable<Hunter> linked)
            => Math.Max(0, camp.Population + camp.GrowthRemainder - camp.DeathRemainder
                + linked.Sum(h => h.Population + h.GrowthRemainder - h.DeathRemainder));

        // Physical demography closes finite food/textile service against the person-years it creates, then reconciles
        // gross events at the turn boundary. Only B-D changes the workforce: equilibrium turnover cannot erase roles.
        private static void ReconcilePhysicalDemography(WorldState w, TurnRecord record, double years,
            Dictionary<int, PhysicalFoodEnvelope> campFood, Dictionary<int, PhysicalFoodEnvelope> hunterFood,
            List<PhysicalCampEvents> pendingCamps, List<PhysicalHunterEvents> pendingHunters)
        {
            foreach (var camp in w.Camps.OrderBy(c => c.Id))
            {
                var linked = w.Hunters.Where(h => h.SourceCamp == camp.Id && Supply.Connected(w, h)).OrderBy(h => h.Id).ToArray();
                var detached = w.Hunters.Where(h => h.SourceCamp == camp.Id && !Supply.Connected(w, h)).OrderBy(h => h.Id).ToArray();
                if (!campFood.TryGetValue(camp.Id, out var food)) food = new PhysicalFoodEnvelope();
                double initialCloth = Math.Max(0, camp.Inventory.Cloth + camp.ResolvedClothConsumed);
                double initialAnimalFabric = Math.Max(0, camp.Inventory.AnimalFabric + camp.ResolvedAnimalFabricConsumed);
                double clothingBudget = initialCloth + initialAnimalFabric;
                double housing = NeolithicProduction.HousingCapacity(w, camp, w.Days).Total;
                var residentCohorts = new[]
                {
                    new DemographicCohortInput(Math.Max(0, camp.Population + camp.GrowthRemainder - camp.DeathRemainder), camp.FoodDeficitYears)
                }.Concat(linked.Select(h => new DemographicCohortInput(EffectivePopulation(h), h.FoodDeficitYears))).ToArray();
                var pools = new List<DemographicServicePoolInput>
                {
                    new DemographicServicePoolInput(residentCohorts, food.Total, clothingBudget, housing)
                };
                var detachedFood = new PhysicalFoodEnvelope[detached.Length];
                for (int i = 0; i < detached.Length; i++)
                {
                    if (!hunterFood.TryGetValue(detached[i].Id, out detachedFood[i])) detachedFood[i] = new PhysicalFoodEnvelope();
                    pools.Add(new DemographicServicePoolInput(
                        new[] { new DemographicCohortInput(EffectivePopulation(detached[i]), detached[i].FoodDeficitYears) },
                        detachedFood[i].Total, 0, 0));
                }
                CoupledDemographicResult coupled = PhysicalDemography.AdvanceCoupledBudget(w.Config, pools, years);
                DemographicResult result = coupled.Pools[0];

                // Reconstruct textile stocks from their pre-needs provenance, then debit exactly the closed budget.
                double clothUsed = Math.Min(initialCloth, result.ClothingConsumed);
                double animalUsed = Math.Min(initialAnimalFabric, Math.Max(0, result.ClothingConsumed - clothUsed));
                camp.Inventory.Cloth = Math.Max(0, initialCloth - clothUsed);
                camp.Inventory.AnimalFabric = Math.Max(0, initialAnimalFabric - animalUsed);
                camp.ResolvedClothConsumed = clothUsed; camp.ResolvedAnimalFabricConsumed = animalUsed;
                camp.FabricConsumed = clothUsed + animalUsed;

                RefundCampFood(camp, food, Math.Max(0, food.Total - result.FoodConsumed));
                camp.LastConsumed = result.FoodConsumed;
                camp.FoodDeficit = Math.Max(0, PhysicalDemography.FoodPerPersonYear(w.Config) * result.PersonYears - result.FoodConsumed);
                camp.FoodNeedsMetRatio = result.Nutrition;
                UpdateDeficitState(camp, result.Nutrition, years, w.Config.Neolithic.ReferenceTurnYears);
                camp.DemographicDeficitMergeWeight = coupled.Cohorts[0][0].FinalPopulation;
                for (int i = 0; i < linked.Length; i++)
                {
                    Hunter h = linked[i];
                    DemographicResult member = coupled.Cohorts[0][i + 1];
                    h.LastConsumed = member.FoodConsumed;
                    h.FoodDeficit = Math.Max(0, PhysicalDemography.FoodPerPersonYear(w.Config) * member.PersonYears - member.FoodConsumed);
                    UpdateDeficitState(h, result.Nutrition, years, w.Config.Neolithic.ReferenceTurnYears);
                    h.DemographicDeficitMergeWeight = member.FinalPopulation;
                }

                pendingCamps.Add(new PhysicalCampEvents { Camp = camp, Linked = linked, Cohorts = coupled.Cohorts[0] });
                double experiencedPopulation = years > 0 ? result.PersonYears / years : result.FinalPopulation;
                NeolithicProduction.CommitExperiencedWelfare(w, camp, experiencedPopulation, result.Nutrition,
                    result.ClothingCoverage, result.AverageHousingCoverage);
                CommitLocalRates(camp, result);
                var summary = record.Demographics.First(d => d.Camp == camp.Id);
                AddPhysicalDiagnostics(summary, result);
                for (int i = 0; i < detached.Length; i++)
                {
                    Hunter hunter = detached[i];
                    PhysicalFoodEnvelope hunterEnvelope = detachedFood[i];
                    DemographicResult hunterResult = coupled.Pools[i + 1];
                    double actualLocal = Math.Min(hunterEnvelope.Local, hunterResult.FoodConsumed);
                    double actualCarried = Math.Min(hunterEnvelope.Carried, Math.Max(0, hunterResult.FoodConsumed - actualLocal));
                    hunter.CarriedFood += hunterEnvelope.Carried - actualCarried;
                    hunter.LastConsumed = actualLocal + actualCarried;
                    hunter.FoodDeficit = Math.Max(0, PhysicalDemography.FoodPerPersonYear(w.Config) * hunterResult.PersonYears - hunterResult.FoodConsumed);
                    UpdateDeficitState(hunter, hunterResult.Nutrition, years, w.Config.Neolithic.ReferenceTurnYears);
                    hunter.DemographicDeficitMergeWeight = coupled.Cohorts[i + 1][0].FinalPopulation;
                    pendingHunters.Add(new PhysicalHunterEvents { Hunter = hunter, Result = hunterResult });
                    summary.DiscardedInventory += Math.Max(0, hunterEnvelope.Local - actualLocal);
                    AddPhysicalDiagnostics(summary, hunterResult);
                }
                AddCoupledDiagnostics(summary, coupled);
            }

            foreach (var summary in record.Demographics)
            {
                if (summary.PersonYears > 0)
                {
                    summary.EffectiveFertilityRate = summary.ContinuousBirths / summary.PersonYears;
                    summary.EffectiveMortalityRate = summary.ContinuousDeaths / summary.PersonYears;
                    summary.ExposureMortalityRate = summary.ExposureDeaths / summary.PersonYears;
                    summary.CrowdingMortalityRate = summary.CrowdingDeaths / summary.PersonYears;
                    summary.DiseaseMortalityRate = summary.DiseaseDeaths / summary.PersonYears;
                    summary.StarvationMortalityRate = summary.StarvationDeaths / summary.PersonYears;
                }
                var camp = w.Camps.First(c => c.Id == summary.Camp);
                summary.QoL = camp.QoL;
            }
        }

        private static void RefundCampFood(Camp camp, PhysicalFoodEnvelope food, double refund)
        {
            double stored = food.Plant + food.Animal + food.Aquatic;
            if (!(refund > 0) || !(stored > 0)) return;
            refund = Math.Min(refund, stored);
            camp.PlantFood += refund * food.Plant / stored;
            camp.AnimalFood += refund * food.Animal / stored;
            camp.AquaticFood += refund * food.Aquatic / stored;
        }

        private static void UpdateDeficitState(Camp camp, double nutrition, double years, double referenceYears)
        {
            if (nutrition >= 1 - 1e-12)
            {
                camp.FoodDeficitYears = 0; camp.DeficitTurns = 0; camp.DeficitSeverity = 0;
            }
            else
            {
                camp.FoodDeficitYears += years; camp.DeficitTurns++;
                camp.DeficitSeverity += (1 - nutrition) * years / Math.Max(1e-12, referenceYears);
            }
        }

        private static void UpdateDeficitState(Hunter hunter, double nutrition, double years, double referenceYears)
        {
            if (nutrition >= 1 - 1e-12)
            {
                hunter.FoodDeficitYears = 0; hunter.DeficitTurns = 0; hunter.DeficitSeverity = 0;
            }
            else
            {
                hunter.FoodDeficitYears += years; hunter.DeficitTurns++;
                hunter.DeficitSeverity += (1 - nutrition) * years / Math.Max(1e-12, referenceYears);
            }
        }

        private static void ReconcilePhysicalEvents(Camp camp, Hunter[] linked,
            DemographicResult[] cohorts, TurnRecord record)
        {
            if (cohorts == null || cohorts.Length != linked.Length + 1)
                throw new DemographicNumericalException("Connected demographic cohort reconciliation is incomplete.");
            int count = cohorts.Length;
            int[] residents = new[] { camp.Population }.Concat(linked.Select(h => h.Population)).ToArray();
            double[] birthProgress = new[] { camp.GrowthRemainder + cohorts[0].Births }
                .Concat(linked.Select((h, i) => h.GrowthRemainder + cohorts[i + 1].Births)).ToArray();
            double[] deathProgress = new[] { camp.DeathRemainder + cohorts[0].Deaths }
                .Concat(linked.Select((h, i) => h.DeathRemainder + cohorts[i + 1].Deaths)).ToArray();
            ReconcileWholeEvents(residents.Sum(), cohorts.Sum(r => r.FinalPopulation),
                birthProgress.Sum(), deathProgress.Sum(), out int totalBirths, out int totalDeaths,
                out double birthRemainder, out double deathRemainder, out double extinctionResidual);

            // Connected cohorts share one rounding ledger so a reversible dispatch cannot manufacture or erase a
            // whole event. Within that conserved total, completed births and deaths follow each cohort's own
            // continuous event burden. The half-person convention applies once to the settlement total, not once per
            // temporary partition: otherwise three viable fractional singleton cohorts could create a population floor.
            int[] births = AllocateWholeEvents(birthProgress, totalBirths, null);
            int[] deathCapacity = Enumerable.Range(0, count)
                .Select(i => residents[i] + births[i]).ToArray();
            int[] deaths = AllocateWholeEvents(deathProgress, totalDeaths, deathCapacity);
            camp.GrowthRemainder = camp.DeathRemainder = 0;
            foreach (Hunter hunter in linked) { hunter.GrowthRemainder = 0; hunter.DeathRemainder = 0; }

            // Keep the shared fractional ledger on a surviving authoritative entity. Usually that is the camp; when
            // burden-based attribution rounds the last camp resident away, a surviving expedition carries both gross
            // remainders so their signed combination is not clipped or lost on the next continuation.
            int ledgerOwner = -1;
            for (int i = 0; i < count; i++)
                if (residents[i] + births[i] - deaths[i] > 0) { ledgerOwner = i; break; }
            if (ledgerOwner >= 0)
            {
                if (ledgerOwner == 0)
                {
                    camp.GrowthRemainder = birthRemainder;
                    camp.DeathRemainder = deathRemainder;
                }
                else
                {
                    linked[ledgerOwner - 1].GrowthRemainder = birthRemainder;
                    linked[ledgerOwner - 1].DeathRemainder = deathRemainder;
                }
            }
            else if (birthRemainder > 1e-10 || deathRemainder > 1e-10)
                throw new DemographicNumericalException("An extinct connected settlement retained fractional demographic progress.");

            ApplyCampPhysicalEvents(camp, births[0], deaths[0], cohorts[0], record);
            for (int i = 0; i < linked.Length; i++)
                ApplyHunterPhysicalEvents(linked[i], births[i + 1], deaths[i + 1], cohorts[i + 1], record, 0);

            // Integer extinction is a settlement-level rounding decision for connected cohorts. A cohort that
            // rounds to zero while another connected entity carries the shared fractional ledger has not gone
            // extinct independently. Attribute only the residual actually discarded by ReconcileWholeEvents, once.
            if (extinctionResidual > 0)
            {
                var aggregate = new DemographicResult
                {
                    ExposureDeaths = cohorts.Sum(r => r.ExposureDeaths),
                    CrowdingDeaths = cohorts.Sum(r => r.CrowdingDeaths),
                    DiseaseDeaths = cohorts.Sum(r => r.DiseaseDeaths),
                    LegacyStarvationDeaths = cohorts.Sum(r => r.LegacyStarvationDeaths)
                };
                RecordIntegerExtinction(record, record.Demographics.First(d => d.Camp == camp.Id),
                    camp.Id, aggregate, extinctionResidual);
            }
        }

        private static void ReconcilePhysicalEvents(Hunter hunter, DemographicResult result, TurnRecord record)
        {
            double birthProgress = hunter.GrowthRemainder + result.Births, deathProgress = hunter.DeathRemainder + result.Deaths;
            ReconcileWholeEvents(hunter.Population, result.FinalPopulation, birthProgress, deathProgress,
                out int births, out int deaths, out hunter.GrowthRemainder, out hunter.DeathRemainder, out double extinctionResidual);
            ApplyHunterPhysicalEvents(hunter, births, deaths, result, record, extinctionResidual);
        }

        private static int[] AllocateWholeEvents(double[] progress, int total, int[] capacity)
        {
            var result = new int[progress.Length];
            for (int i = 0; i < progress.Length; i++)
            {
                int cap = capacity == null ? int.MaxValue : Math.Max(0, capacity[i]);
                result[i] = Math.Min(cap, CheckedWholeEvents(progress[i], "cohort"));
            }
            int assigned = result.Sum();
            if (assigned < total)
            {
                foreach (int i in Enumerable.Range(0, progress.Length)
                    .OrderByDescending(i => progress[i] - Math.Floor(progress[i]))
                    .ThenByDescending(i => progress[i]).ThenBy(i => i))
                {
                    int cap = capacity == null ? int.MaxValue : Math.Max(0, capacity[i]);
                    if (assigned >= total) break;
                    if (result[i] < cap) { result[i]++; assigned++; }
                }
                if (assigned < total)
                {
                    foreach (int i in Enumerable.Range(0, progress.Length)
                        .OrderByDescending(i => progress[i]).ThenBy(i => i))
                    {
                        int cap = capacity == null ? int.MaxValue : Math.Max(0, capacity[i]);
                        int add = Math.Min(total - assigned, cap - result[i]);
                        if (add > 0) { result[i] += add; assigned += add; }
                        if (assigned >= total) break;
                    }
                }
            }
            else if (assigned > total)
            {
                foreach (int i in Enumerable.Range(0, progress.Length)
                    .OrderBy(i => progress[i] - Math.Floor(progress[i]))
                    .ThenBy(i => progress[i]).ThenByDescending(i => i))
                {
                    int remove = Math.Min(assigned - total, result[i]);
                    if (remove > 0) { result[i] -= remove; assigned -= remove; }
                    if (assigned <= total) break;
                }
            }
            if (assigned != total)
                throw new DemographicNumericalException("Per-cohort event allocation could not preserve the settlement total.");
            return result;
        }

        private static void ApplyCampPhysicalEvents(Camp camp, int births, int deaths,
            DemographicResult result, TurnRecord record)
        {
            int net = births - deaths;
            if (camp.Population + net < 0)
                throw new DemographicNumericalException("Gross event rounding exceeded a camp cohort's population.");
            if (net < 0) camp.Workforce.ApplyCasualties(camp.Population, -net);
            camp.Population += net; camp.Workforce.Normalize(camp.Population);
            if (net != 0)
                record.PopulationChanges.Add(new PopulationChange
                { Camp = camp.Id, Entity = camp.Id, Kind = "Physical demography (net)", Count = net });
            DemographicReport summary = record.Demographics.First(d => d.Camp == camp.Id);
            RecordGrossEvents(record, summary, births, deaths);
        }

        private static void ApplyHunterPhysicalEvents(Hunter hunter, int births, int deaths,
            DemographicResult result, TurnRecord record, double extinctionResidual = -1)
        {
            int net = births - deaths;
            if (hunter.Population + net < 0)
                throw new DemographicNumericalException("Gross event rounding exceeded an expedition's population.");
            hunter.Population += net;
            if (net != 0)
                record.PopulationChanges.Add(new PopulationChange
                { Camp = hunter.SourceCamp, Entity = hunter.Id, Kind = "Physical demography (net)", Count = net });
            DemographicReport summary = record.Demographics.First(d => d.Camp == hunter.SourceCamp);
            RecordGrossEvents(record, summary, births, deaths);
            double residual = extinctionResidual >= 0 ? extinctionResidual
                : hunter.Population == 0 && result.FinalPopulation < .5 ? result.FinalPopulation : 0;
            RecordIntegerExtinction(record, summary, hunter.Id, result, residual);
        }

        // Gross floors normally define the unique exact integer/remainder representation. At the last resident they can
        // otherwise either extinguish a still-material (>=0.5) continuous cohort too early or preserve an asymptotically
        // declining singleton forever. Defer the former whole death; explicitly round and report the latter.
        private static void ReconcileWholeEvents(int residents, double continuousEndpoint, double birthProgress, double deathProgress,
            out int births, out int deaths, out double birthRemainder, out double deathRemainder, out double extinctionResidual)
        {
            births = CheckedWholeEvents(birthProgress, "birth"); deaths = CheckedWholeEvents(deathProgress, "death");
            int endpoint = residents + births - deaths;
            extinctionResidual = 0;
            if (continuousEndpoint < .5 && endpoint > 0)
            {
                deaths += endpoint;
                endpoint = 0;
            }
            else if (continuousEndpoint >= .5 && endpoint <= 0)
            {
                // Keep one authoritative resident and carry the completed death progress. A later birth may balance it;
                // a later physical decline may cross the explicit extinction threshold.
                deaths = residents + births - 1;
                endpoint = 1;
            }
            if (endpoint <= 0)
            {
                if (continuousEndpoint >= .5)
                    throw new DemographicNumericalException("Integer extinction reconciliation lost a viable continuous cohort.");
                extinctionResidual = Math.Max(0, continuousEndpoint);
                birthRemainder = deathRemainder = 0;
                return;
            }
            birthRemainder = birthProgress - births; deathRemainder = deathProgress - deaths;
            if (birthRemainder < -1e-10 || deathRemainder < -1e-10)
                throw new DemographicNumericalException("Integer demographic reconciliation produced a negative gross remainder.");
            birthRemainder = Math.Max(0, birthRemainder); deathRemainder = Math.Max(0, deathRemainder);
        }

        private static void RecordIntegerExtinction(TurnRecord record, DemographicReport summary, int entity,
            DemographicResult result, double residual)
        {
            if (!(residual > 0)) return;
            summary.IntegerExtinction = true;
            summary.IntegerExtinctionResidual += residual;
            record.Log("Entity " + entity + " reached integer extinction after physical decline; continuous residual="
                + residual.ToString("G6", System.Globalization.CultureInfo.InvariantCulture)
                + ", exposure=" + result.ExposureDeaths.ToString("G6", System.Globalization.CultureInfo.InvariantCulture)
                + ", crowding=" + result.CrowdingDeaths.ToString("G6", System.Globalization.CultureInfo.InvariantCulture)
                + ", disease=" + result.DiseaseDeaths.ToString("G6", System.Globalization.CultureInfo.InvariantCulture)
                + ", starvation=" + result.LegacyStarvationDeaths.ToString("G6", System.Globalization.CultureInfo.InvariantCulture) + ".");
        }

        private static int CheckedWholeEvents(double progress, string kind)
        {
            if (double.IsNaN(progress) || double.IsInfinity(progress) || progress < -1e-10 || progress > int.MaxValue)
                throw new DemographicNumericalException("Invalid gross " + kind + " progress at the turn boundary.");
            return (int)Math.Floor(Math.Max(0, progress));
        }

        private static void RecordGrossEvents(TurnRecord record, DemographicReport summary, int births, int deaths)
        {
            record.Growth += births; record.Deaths += deaths;
            summary.GrossBirths += births; summary.GrossDeaths += deaths;
            summary.Additions += births; summary.Deaths += deaths;
            if (births > 0) summary.SurplusSteps++;
        }

        private static void CommitLocalRates(Camp camp, DemographicResult result)
        {
            double y = result.PersonYears;
            camp.EffectiveFertilityRate = y > 0 ? result.Births / y : 0;
            camp.EffectiveMortalityRate = y > 0 ? result.Deaths / y : 0;
            camp.ExposureMortalityRate = y > 0 ? result.ExposureDeaths / y : 0;
            camp.CrowdingMortalityRate = y > 0 ? result.CrowdingDeaths / y : 0;
            camp.DiseaseMortalityRate = y > 0 ? result.DiseaseDeaths / y : 0;
            camp.StarvationMortalityRate = y > 0 ? result.LegacyStarvationDeaths / y : 0;
        }

        private static void AddPhysicalDiagnostics(DemographicReport summary, DemographicResult result)
        {
            double previousYears = summary.PersonYears, combinedYears = previousYears + result.PersonYears;
            if (combinedYears > 0)
            {
                summary.Nutrition = (summary.Nutrition * previousYears + result.Nutrition * result.PersonYears) / combinedYears;
                summary.ClothingCoverage = (summary.ClothingCoverage * previousYears + result.ClothingCoverage * result.PersonYears) / combinedYears;
                summary.HousingCoverage = (summary.HousingCoverage * previousYears + result.AverageHousingCoverage * result.PersonYears) / combinedYears;
            }
            summary.PersonYears = combinedYears;
            summary.ContinuousBirths += result.Births; summary.ContinuousDeaths += result.Deaths;
            summary.ExposureDeaths += result.ExposureDeaths; summary.CrowdingDeaths += result.CrowdingDeaths;
            summary.DiseaseDeaths += result.DiseaseDeaths; summary.StarvationDeaths += result.LegacyStarvationDeaths;
            summary.AdditionalDeaths += result.AdditionalDeaths;
            summary.FoodConsumed += result.FoodConsumed;
            summary.EquilibriumPopulation += result.HasPositiveEquilibrium ? result.EquilibriumPopulation : 0;
            summary.HasPositiveEquilibrium |= result.HasPositiveEquilibrium; summary.DeclineToExtinction |= result.DeclineToExtinction;
            summary.CauseCorrection = Math.Max(summary.CauseCorrection, result.CauseCorrection);
            summary.ConservationError += result.ConservationError;
            summary.RootEvaluations += result.RootEvaluations; summary.BandsCrossed += result.BandsCrossed;
            summary.HazardEvaluations += result.HazardEvaluations; summary.QuadratureEvaluations += result.QuadratureEvaluations;
            summary.ServiceIterations += result.ServiceIterations;
            if (result.Nutrition < 1 - 1e-12) summary.DeficitSteps++;
        }

        private static void AddCoupledDiagnostics(DemographicReport summary, CoupledDemographicResult result)
        {
            summary.CoupledPools += result.PoolCount;
            summary.CoupledCohorts += result.CohortCount;
            summary.CoupledRateEvaluations += result.RateEvaluations;
            summary.CoupledAcceptedSteps += result.AcceptedSteps;
            summary.CoupledRejectedSteps += result.RejectedSteps;
            summary.CoupledServiceEvaluations += result.ServiceEvaluations;
            if (result.UsedExactScalarReduction) summary.CoupledScalarReductions++;
        }

        private static void ApplySharedDeaths(Camp camp, Hunter[] linked, int losses, TurnRecord record)
        {
            int total = camp.Population + linked.Sum(h => h.Population); int[] counts = new[] { camp.Population }.Concat(linked.Select(h => h.Population)).ToArray();
            int[] deaths = counts.Select(n => total == 0 ? 0 : (int)((long)n * losses / total)).ToArray(); int remainder = losses - deaths.Sum();
            foreach (int i in Enumerable.Range(0, counts.Length).OrderByDescending(i => total == 0 ? 0 : (long)counts[i] * losses % total).ThenBy(i => i)) if (remainder > 0 && deaths[i] < counts[i]) { deaths[i]++; remainder--; }
            camp.Workforce.ApplyCasualties(camp.Population, deaths[0]);
            camp.Population -= deaths[0]; record.PopulationChanges.Add(new PopulationChange { Camp = camp.Id, Entity = camp.Id, Kind = "Starvation", Count = -deaths[0] });
            camp.Workforce.Normalize(camp.Population);
            for (int i = 0; i < linked.Length; i++) { linked[i].Population -= deaths[i + 1]; record.PopulationChanges.Add(new PopulationChange { Camp = camp.Id, Entity = linked[i].Id, Kind = "Starvation", Count = -deaths[i + 1] }); }
            record.Deaths += losses;
        }

        private static void AddGrowth(WorldState w, Camp camp, GrowthPlan plan, TurnRecord record, DemographicReport summary)
        {
            double progress = camp.GrowthRemainder + Math.Max(0, plan.FinalPopulation - plan.ReproductivePopulation);
            int added = (int)Math.Floor(progress); camp.GrowthRemainder = progress - added; camp.Population += added; record.Growth += added;
            if (plan.FinalPopulation > plan.ReproductivePopulation || added > 0) summary.SurplusSteps++;
            if (added > 0) record.PopulationChanges.Add(new PopulationChange { Camp = camp.Id, Entity = camp.Id, Kind = "Net growth", Count = added });
        }

        public static double StorageGenerations(WorldState w, int tile)
        {
            double temperature = w.Tiles[tile].Seasons.Average(s => s.Temperature), moisture = w.Tiles[tile].Seasons.Average(s => s.Moisture);
            double cold = Numbers.Clamp((12 - temperature) / 30, 0, 1), dry = Numbers.Clamp((.45 - moisture) / .45, 0, 1);
            double fraction = Math.Max(cold, dry), referenceYears = w.Config.Neolithic.ReferenceTurnYears;
            double minimumTurns = w.Config.MinimumStorageYears / referenceYears;
            double maximumTurns = w.Config.MaximumStorageYears / referenceYears;
            return minimumTurns + (maximumTurns - minimumTurns) * fraction;
        }

        private static void ApplyStorage(WorldState w, TurnRecord record, double intervals)
        {
            foreach (var camp in w.Camps.OrderBy(c => c.Id))
            {
                double cap = NeolithicProduction.StorageCapacity(w, camp, intervals);
                // Goods storage: goods in Pots beyond their base allowance take food space, never the reserve's share
                // (Docs/PlannerTradeStorageDesign.md §3). Goods consumed this turn no longer count at its end.
                if (camp.GoodsStorage != null)
                {
                    var end = GoodsStorage.Begin(w, camp, w.Config.TurnDays / w.Config.DaysPerYear, camp.Population, 0, 0, true);
                    cap = end.FoodCapacity(camp.Inventory.Pots, end.Occupied(GoodsStorage.StockOf(camp)));
                    camp.GoodsStorage = null;
                }
                double loss = Math.Max(0, camp.Food - cap); camp.Food -= loss;
                var summary = record.Demographics.First(d => d.Camp == camp.Id); summary.DiscardedInventory += loss;
            }
        }

        private static void ReproduceHerds(WorldState w, double intervals, double years)
        {
            foreach (var herd in w.Herds.Where(h => h.DomesticCamp < 0).OrderBy(h => h.Id).ToArray())
            {
                var species = w.Config.GetSpecies(herd.Species); double demand = herd.Animals * intervals;
                double intake = Math.Min(1 - herd.UnmetFood / Math.Max(1e-9, demand * species.FoodConsumption), 1 - herd.UnmetFreshwater / Math.Max(1e-9, demand * species.FreshwaterConsumption));
                double unrestricted = (herd.Animals + herd.GrowthRemainder) * Math.Exp(species.GrowthRate * years * Math.Max(0, intake));
                double target = unrestricted;
                int grown = Math.Max(0, (int)Math.Floor(target) - herd.Animals); herd.GrowthRemainder = target - Math.Floor(target); herd.Animals += grown;
            }
        }

        private static void SplitHerds(WorldState w, TurnRecord record)
        {
            foreach (var herd in w.Herds.Where(h => h.DomesticCamp < 0 && h.Animals > h.Capacity).OrderBy(h => h.Id).ToArray())
            {
                int groups = (int)Math.Ceiling(herd.Animals / (double)herd.Capacity), total = herd.Animals, baseSize = total / groups, remainder = total % groups;
                var species = w.Config.GetSpecies(herd.Species);
                var destinations = w.Map.Radius(herd.Tile, 1).Where(n => w.Tiles[n].TraversableLand).OrderByDescending(n =>
                {
                    // The Paleo path has no cultivation overlay. Preserve its
                    // existing raw-vegetation ranking without projecting every
                    // candidate tile for every herd fission.
                    if (w.Tiles[n].CultivationCapital <= 0) return species.Suitability(w.Tiles[n]);
                    var ecology = LazyEcology.ProjectEcology(w, n, record.StartDays + record.Duration);
                    double retention = EcologyMath.CultivationVegetationRetention(w.Config, CultivationEconomy.Intensity(w.Config, ecology.CultivationCapital));
                    return species.Suitability(w.Tiles[n], ecology.CurrentVegetation, retention);
                }).ThenBy(n => Numbers.Hash(w.Seed, 151, herd.Id ^ n, w.Turn)).ThenBy(n => n).ToArray();
                herd.Animals = baseSize + (remainder > 0 ? 1 : 0);
                for (int i = 1; i < groups; i++)
                {
                    int tile = destinations.Length == 0 ? herd.Tile : destinations[i % destinations.Length]; int animals = baseSize + (i < remainder ? 1 : 0);
                    w.Herds.Add(new Herd { Id = w.NextId++, Tile = tile, Species = herd.Species, Animals = animals, Capacity = herd.Capacity });
                }
                record.Log("Herd " + herd.Id + " grew beyond " + herd.Capacity + " animals and divided into " + groups + " herds.");
            }
        }

        private sealed class ReportIndex
        {
            public readonly Dictionary<int, Camp> Camps;
            public readonly Dictionary<int, Hunter> Hunters;
            public readonly ILookup<int, Hunter> CampHunters;
            public readonly ILookup<int, Transfer> Harvest;
            public readonly ILookup<int, LogisticsRecord> Logistics;
            public readonly ILookup<int, PopulationChange> Population;
            public readonly ILookup<int, Order> Orders;
            public ReportIndex(WorldState w, TurnRecord record, IReadOnlyList<Order> orders)
            {
                Camps = w.Camps.ToDictionary(c => c.Id); Hunters = w.Hunters.ToDictionary(h => h.Id); CampHunters = w.Hunters.ToLookup(h => h.SourceCamp);
                var owners = Camps.Keys.ToDictionary(id => id, id => id); foreach (var h in w.Hunters) owners[h.Id] = h.SourceCamp;
                Harvest = record.Transfers.Where(t => owners.ContainsKey(t.Entity)).ToLookup(t => owners[t.Entity]);
                Logistics = record.Logistics.ToLookup(l => l.Camp); Population = record.PopulationChanges.ToLookup(p => p.Camp);
                Orders = orders.Where(o => owners.ContainsKey(o.Entity)).ToLookup(o => owners[o.Entity]);
            }
        }

        private static void FinishDemographicReports(WorldState w, TurnRecord record, IReadOnlyList<Order> orders)
        {
            record.Transfers = record.Transfers.GroupBy(t => (t.Tile, t.Entity, t.Kind)).OrderBy(g => g.Key.Tile).ThenBy(g => g.Key.Entity).ThenBy(g => g.Key.Kind, StringComparer.Ordinal)
                .Select(g => new Transfer { Tile = g.Key.Tile, Entity = g.Key.Entity, Kind = g.Key.Kind, Food = g.Sum(t => t.Food), PotentialFood = g.Sum(t => t.PotentialFood), Freshwater = g.Sum(t => t.Freshwater) }).ToList();
            var index = new ReportIndex(w, record, orders);
            foreach (var summary in record.Demographics)
            {
                var camp = index.Camps[summary.Camp];
                var harvest = index.Harvest[camp.Id].ToArray();
                summary.TerrestrialFood = harvest.Where(t => t.Kind == "Gathering" || t.Kind == "Cultivation").Sum(t => t.Food); summary.MarineFood = harvest.Where(t => t.Kind == "Marine gathering").Sum(t => t.Food); summary.LakeFood = harvest.Where(t => t.Kind == "Aquatic gathering").Sum(t => t.Food);
                summary.HuntFood = harvest.Where(t => t.Kind == "Hunting").Sum(t => t.Food); summary.FishFood = harvest.Where(t => t.Kind == "Fishing" || t.Kind == "FisheryHarvest").Sum(t => t.Food); double forage = harvest.Where(t => t.Kind == "Foraging").Sum(t => t.Food); summary.ForageFood = forage;
                summary.AnimalFood = harvest.Where(t => t.Kind == "Animal Product").Sum(t => t.Food);
                summary.SlaughterFood = harvest.Where(t => t.Kind == "Slaughter").Sum(t => t.Food);
                summary.Slaughtered = record.ManagedHerds.Where(m => m.Camp == camp.Id).Sum(m => m.Slaughtered);
                summary.FoodProduced = summary.TerrestrialFood + summary.MarineFood + summary.LakeFood + summary.HuntFood + summary.FishFood + summary.AnimalFood + summary.SlaughterFood + forage;
                summary.FoodReceived = summary.FoodProduced - index.Logistics[camp.Id].Sum(l => l.HarvestOverflow);
                summary.DiscardedInventory += index.Logistics[camp.Id].Sum(l => l.InventoryLoss);
                if (summary.Model != DemographicModel.PhysicalMortalityV1)
                {
                    summary.Additions = index.Population[camp.Id].Where(p => p.Count > 0).Sum(p => p.Count);
                    summary.Deaths = -index.Population[camp.Id].Where(p => p.Count < 0).Sum(p => p.Count);
                }
                summary.EndingPopulation = w.LinkedPopulation(camp); summary.NetPercent = 100.0 * (summary.EndingPopulation - summary.StartingPopulation) / Math.Max(1, summary.StartingPopulation);
                summary.FinalReserves = camp.Food + index.CampHunters[camp.Id].OrderBy(h => h.Id).Sum(h => h.CarriedFood);
                summary.FractionalProgress = summary.Model == DemographicModel.PhysicalMortalityV1 ? camp.GrowthRemainder - camp.DeathRemainder : camp.GrowthRemainder;
                double balanceDiff = summary.InitialReserves + summary.FoodReceived + summary.TradeFood - summary.FoodConsumed - summary.ManufacturingFood - summary.DiscardedInventory - summary.FinalReserves;
                if (Math.Abs(balanceDiff) > 1e-5) throw new InvalidOperationException($"Unexplained food balance in demographic report for Camp {camp.Id}: Initial={summary.InitialReserves:F4}, Recv={summary.FoodReceived:F4}, Consumed={summary.FoodConsumed:F4}, Discarded={summary.DiscardedInventory:F4}, Final={summary.FinalReserves:F4}, Diff={balanceDiff:F6}");
                summary.LimitingFactor = summary.Model == DemographicModel.PhysicalMortalityV1
                    ? summary.DeficitSteps > 0 ? "Physical food service was incomplete"
                    : summary.StarvationDeaths > 1e-9 ? "Sustained food deficiency caused starvation mortality"
                    : summary.ExposureDeaths >= Math.Max(summary.CrowdingDeaths, summary.DiseaseDeaths) ? "Exposure mortality"
                    : summary.CrowdingDeaths >= summary.DiseaseDeaths ? "Crowding mortality" : "Disease mortality"
                    : summary.DeficitSteps > 0 ? "Food deficits limited growth" : summary.Additions > 0 ? "Growth supported by food surplus; demand rose with population" : summary.SurplusSteps > 0 ? "Demographic rounding; fractional progress " + summary.FractionalProgress.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) : summary.RecoverySteps > 0 ? "Starvation recovery" : "No sustainable food surplus or insufficient reserves";
                record.Log(summary.Explanation);
            }
            AwardLifestyleProgress(w, record, orders, index);
        }

        private static void AwardLifestyleProgress(WorldState w, TurnRecord record, IReadOnlyList<Order> orders, ReportIndex index)
        {
            double generationScale = record.Duration / w.Config.PracticeDays;
            foreach (var summary in record.Demographics.OrderBy(report => report.Camp))
            {
                var camp = index.Camps[summary.Camp];
                var hunterIds = new HashSet<int>(index.CampHunters[camp.Id].Select(h => h.Id));
                double hunting = index.Harvest[camp.Id].Where(t => hunterIds.Contains(t.Entity) && t.Kind == "Hunting").Sum(t => t.Food);
                double passiveAquatic = index.Harvest[camp.Id].Where(t => t.Entity == camp.Id && (t.Kind == "Marine gathering" || t.Kind == "Aquatic gathering")).Sum(t => t.Food);
                double gathering = index.Harvest[camp.Id].Where(t => (t.Entity == camp.Id && t.Kind == "Gathering") || hunterIds.Contains(t.Entity) && t.Kind == "Foraging").Sum(t => t.Food) + passiveAquatic * (1 - w.Config.PassiveAquaticFishingPracticeShare);
                double fishing = (index.Harvest[camp.Id].Where(t => hunterIds.Contains(t.Entity) && t.Kind == "Fishing").Sum(t => t.Food)
                    + index.Harvest[camp.Id].Where(t => t.Entity == camp.Id && t.Kind == "FisheryHarvest").Sum(t => t.Food))
                    * w.Config.ActiveFishingPracticeMultiplier + passiveAquatic * w.Config.PassiveAquaticFishingPracticeShare;
                foreach (var logistics in index.Logistics[camp.Id].Where(l => l.HarvestOverflow > 0))
                {
                    double overflow = logistics.HarvestOverflow;
                    double fromHunting = Math.Min(overflow, index.Harvest[camp.Id].Where(t => t.Entity == logistics.Hunter && t.Kind == "Hunting").Sum(t => t.Food)); hunting -= fromHunting; overflow -= fromHunting;
                    fishing -= Math.Min(overflow, index.Harvest[camp.Id].Where(t => t.Entity == logistics.Hunter && t.Kind == "Fishing").Sum(t => t.Food));
                }
                foreach (var hunt in index.Harvest[camp.Id].Where(t => hunterIds.Contains(t.Entity) && t.Kind == "Hunting" && t.Food <= 0))
                {
                    var hunter = index.Hunters.TryGetValue(hunt.Entity, out var found) ? found : null; if (hunter == null) continue;
                    double practice = hunter.Population * w.Config.Consumption * w.Config.GenerationIntervals * w.Config.UnproductivePracticeFraction;
                    hunting += practice;
                }
                foreach (var order in index.Orders[camp.Id].Where(o => hunterIds.Contains(o.Entity) && o.Kind == OrderKind.Fish))
                {
                    bool successful = index.Harvest[camp.Id].Any(t => t.Entity == order.Entity && t.Food > 0 && t.Kind == "Fishing");
                    if (successful) continue;
                    var hunter = index.Hunters.TryGetValue(order.Entity, out var found) ? found : null; if (hunter == null) continue;
                    fishing += hunter.Population * w.Config.Consumption * w.Config.GenerationIntervals * w.Config.UnproductivePracticeFraction;
                }
                double total = hunting + gathering + fishing; if (total <= 1e-9) continue;
                double budget = summary.StartingPopulation * generationScale;
                TechnologyDiscovery.AddPractice(w, camp.People, Domain.Hunting, budget * hunting / total * w.Config.HuntingPracticeMultiplier, PracticeSource.Hunting);
                TechnologyDiscovery.AddPractice(w, camp.People, Domain.Gathering, budget * gathering / total, PracticeSource.Gathering);
                TechnologyDiscovery.AddPractice(w, camp.People, Domain.Fishing, budget * fishing / total, PracticeSource.Fishing);
            }
        }
    }
}
