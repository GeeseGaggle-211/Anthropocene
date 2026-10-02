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
    }

    // A domestic herd uses the camp's immediate land footprint. These are
    // requests against opening biomass and finite freshwater, not receipts;
    // tile integration remains the sole authority when claimants compete.
    internal static class ManagedHerdLocalClaims
    {
        private struct Site
        {
            public int Tile;
            public bool Land, WaterSource;
            public double NonhumanFood, HumanFood, Water;
        }

        public static void Append(WorldState world, int campTile, int herdId, double foodNeed,
            double waterNeed, double duration, List<Demand> claims, HashSet<int> visible = null)
        {
            if (foodNeed <= 0 && waterNeed <= 0) return;
            var sites = new List<Site>(7);
            var exposure = new SeasonExposure(world.Days, Math.Max(0, duration), world.Config.BaseDays);
            foreach (int tileId in world.Map.Radius(campTile, 1).OrderBy(id => id))
            {
                if (visible != null && !visible.Contains(tileId)) continue;
                var tile = world.Tiles[tileId];
                if (!tile.TraversableLand && tile.WaterBody != WaterKind.Freshwater) continue;
                var stock = LazyEcology.ProjectEcology(world, tileId, world.Days);
                var site = new Site { Tile = tileId, Land = tile.TraversableLand };
                if (tile.TraversableLand)
                {
                    double biomass = Math.Max(0, stock.PotentialFood);
                    site.HumanFood = Numbers.Clamp(stock.Food, 0, biomass);
                    site.NonhumanFood = biomass - site.HumanFood;
                }
                if (tile.WaterBody != WaterKind.Saltwater && tile.FreshwaterSource != FreshwaterSource.None)
                {
                    site.WaterSource = true;
                    // Recharge is a water-only horizon flow. Include a bounded
                    // recharge proxy when splitting requests, so an empty but
                    // renewing spring is still visited. Integration calculates
                    // the actual receipt and clamps its finite source ledger.
                    double recharge = 0;
                    for (int season = 0; season < 4; season++)
                    {
                        var profile = tile.Seasons[season];
                        recharge += Math.Max(0, profile.MaxFreshwater * profile.WaterRecoveryPerDay)
                            * exposure.At(season);
                    }
                    site.Water = Math.Max(0, stock.Freshwater) + recharge;
                }
                sites.Add(site);
            }
            if (sites.Count == 0) return;

            var foodClaims = new double[sites.Count];
            var waterClaims = new double[sites.Count];
            double nonhuman = sites.Sum(s => s.NonhumanFood);
            double human = sites.Sum(s => s.HumanFood);
            double requestedFood = Math.Max(0, foodNeed);
            double first = Math.Min(requestedFood, nonhuman);
            double second = Math.Min(Math.Max(0, requestedFood - first), human);
            double additional = Math.Max(0, requestedFood - first - second);
            double totalBiomass = nonhuman + human;
            int landCount = sites.Count(s => s.Land);
            int lastLand = sites.FindLastIndex(s => s.Land);
            double remainingFood = Math.Max(0, foodNeed);
            for (int i = 0; i < sites.Count; i++)
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
            int lastSource = sites.FindLastIndex(s => s.WaterSource);
            double remainingWater = Math.Max(0, waterNeed);
            for (int i = 0; i < sites.Count; i++)
            {
                if (!sites[i].WaterSource) continue;
                double proposed = water > 0 ? Portion(requestedWater, sites[i].Water, water)
                    : requestedWater / sourceCount;
                waterClaims[i] = i == lastSource ? remainingWater
                    : Math.Min(remainingWater, Math.Max(0, proposed));
                remainingWater = Math.Max(0, remainingWater - waterClaims[i]);
            }

            for (int i = 0; i < sites.Count; i++)
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
            double Animals(Herd h) => animalsOf != null && animalsOf.TryGetValue(h.Id, out double a) ? a : h.Animals;
            var claims = new List<Demand>();
            var requestedFood = new Dictionary<int, double>();
            var requestedWater = new Dictionary<int, double>();
            foreach (var herd in herds)
            {
                var species = w.Config.GetSpecies(herd.Species);
                double food = Animals(herd) * species.FoodConsumption * intervals;
                double water = Animals(herd) * species.FreshwaterConsumption * intervals;
                requestedFood[herd.Id] = food; requestedWater[herd.Id] = water;
                ManagedHerdLocalClaims.Append(w, campTile ?? c.Tile, herd.Id, food, water,
                    intervals * w.Config.BaseDays, claims, visible);
            }
            var transfers = new Dictionary<int, (double food, double water)>();
            foreach (var group in claims.GroupBy(d => d.Tile).OrderBy(g => g.Key))
            {
                var result = EcologicalPressure.Integrate(w, group.Key, group, w.Days, intervals * w.Config.BaseDays);
                foreach (var transfer in result.Transfers)
                {
                    transfers.TryGetValue(transfer.Entity, out var prior);
                    transfers[transfer.Entity] = (prior.food + transfer.PotentialFood, prior.water + transfer.Freshwater);
                }
                forecast.HumanFoodDisplaced += result.Pressure.Where(p => p.Pool == "Biomass" && p.Kind == "Herd").Sum(p => p.GatherableSpill);
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
                forecast.AnimalFood += ManagedAnimalFoodYield(w, Animals(herd), nutrition, management, intervals);
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
            Dictionary<int, TurnNeeds> needs, IReadOnlyCollection<int> abandoningVillages)
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
                record.Demographics.Add(new DemographicReport { Camp = camp.Id, StartingPopulation = w.LinkedPopulation(camp), Years = years, QoLMortalityReduction = QoLMortality.Reduction(w, camp), QoL = camp.QoL, InitialReserves = camp.Food + w.Hunters.Where(h => h.SourceCamp == camp.Id).OrderBy(h => h.Id).Sum(h => h.CarriedFood) });
                camp.StepFoodIncome = 0;
            }

            // Movement happens once. Herd reproduction is realized before the generation's hunting harvest.
            var startingHerdPopulation = w.Herds.OrderBy(h => h.Id).ToDictionary(h => h.Id, h => h.Animals);
            ReproduceHerds(w, intervals, years);
            Hunt(w, orders, establishedHunters, startingHerdPopulation, record, intervals, years, needs);
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
            foreach (var camp in w.Camps.OrderBy(c => c.Id))
            {
                var labor = agriculturalLabor[camp.Id];
                double gatherers = labor.Gatherers;
                if (CultivationEconomy.Enabled(camp) && !abandoningVillages.Contains(camp.Id))
                {
                    // Field and nearby gathering tiles receive one turn-horizon
                    // integration even if the camp ultimately makes no claim.
                    foreach (int id in labor.Footprint.Concat(w.Map.Radius(camp.Tile, w.Config.GatherRadius).Where(id => PassiveGathering.Eligible(w, camp, id))).Distinct())
                        demands.Add(new Demand { Tile = id, Entity = camp.Id, Kind = "Field operation" });
                    continue;
                }
                if (gatherers <= 0) continue;
                AddPassiveCampClaims(w, camp, gatherers, record.StartDays,
                    record.Duration, intervals, abandoningVillages.Contains(camp.Id),
                    id => Resource(id).AvailableFood, campClaims[camp.Id]);
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
                double need = hunter.Population * w.Config.Consumption * intervals;
                demands.Add(new Demand { Tile = hunter.Tile, Entity = hunter.Id, Kind = "Foraging", ImpactMultiplier = PracticeEffects.GatheringImpact(w.Config, w.Camps.First(c => c.Id == hunter.SourceCamp)), Food = Math.Min(need, hunter.Population * w.Config.HunterForagePerPerson * intervals) });
            }
            var campTiles = w.Camps.ToDictionary(c => c.Id, c => c.Tile);
            foreach (var herd in w.Herds.OrderBy(h => h.Id))
            {
                var species = w.Config.GetSpecies(herd.Species);
                herd.UnmetFood = herd.Animals * species.FoodConsumption * intervals; herd.UnmetFreshwater = herd.Animals * species.FreshwaterConsumption * intervals;
                if (herd.DomesticCamp >= 0)
                {
                    ManagedHerdLocalClaims.Append(w,
                        campTiles.TryGetValue(herd.DomesticCamp, out int campTile) ? campTile : herd.Tile,
                        herd.Id, herd.UnmetFood, herd.UnmetFreshwater, horizon.Days, demands);
                }
                else
                {
                    int source = FreshwaterAccess.Source(w, herd.Tile);
                    demands.Add(new Demand { Tile = herd.Tile, Entity = herd.Id, Kind = "Herd", PotentialFood = herd.UnmetFood, Freshwater = source == herd.Tile ? herd.UnmetFreshwater : 0 });
                    if (source != herd.Tile) demands.Add(new Demand { Tile = source, Entity = herd.Id, Kind = "Herd", Freshwater = herd.UnmetFreshwater });
                }
            }

            LastMetrics.SetupEconomyMilliseconds = phaseClock.Elapsed.TotalMilliseconds;
            phaseClock.Restart();
            var gatheredFoodForMaterials = CommitHorizonResources(w, demands, record, touch, foraged, horizon, agriculturalLabor, needs, abandoningVillages);
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
                if (ResolutionMetrics.CaptureCraftWorkDiagnostics)
                {
                    var flow = LastMetrics.CraftFoodFlowFor(camp.Id);
                    var need = needs[camp.Id];
                    flow.ConsumptionTarget = need.ConsumptionTarget;
                    flow.ReserveTarget = need.ReserveTarget;
                    flow.ReserveFillTarget = need.ReserveFillTarget;
                    flow.GrowthFoodTarget = plans[camp.Id].GrowthFood;
                }
            }
            foreach (var hunter in w.Hunters.Where(h => !Supply.Connected(w, h))) record.Demographics.First(d => d.Camp == hunter.SourceCamp).FoodRequired += hunter.Population * w.Config.Consumption * intervals;
            var pendingBirths = new Dictionary<int, GrowthPlan>();
            var pendingCampDeaths = new List<(Camp Camp, Hunter[] Linked, int Losses)>();
            var pendingHunterDeaths = new List<(Hunter Hunter, int Losses)>();
            SustainGeneration(w, record, intervals, generationScale, needs, plans, foraged, recovering, pendingBirths, pendingCampDeaths, pendingHunterDeaths, LastMetrics);
            Supply.Finish(w, record);
            LastMetrics.SustainMilliseconds = phaseClock.Elapsed.TotalMilliseconds;
            phaseClock.Restart();
            // Manufacturing and needs use the same assigned population. Unused gathering
            // worker-time can source materials; realized food gathering earns bridge effort.
            foreach (var camp in w.Camps.OrderBy(c => c.Id).ToArray())
            {
                var labor = agriculturalLabor[camp.Id];
                double gathered = gatheredFoodForMaterials.TryGetValue(camp.Id, out double amount) ? amount : 0;
                labor.GatheringWorkerIntervals = Math.Min(labor.Gatherers * intervals, gathered / w.Config.GatherPerPerson);
                NeolithicProduction.ResolveCampNeolithic(w, camp, horizon, record, Math.Max(0, labor.Gatherers * intervals - labor.GatheringWorkerIntervals), needs[camp.Id]);
                if (CultivationEconomy.Enabled(camp))
                {
                    foreach (int id in labor.Footprint) { LazyEcology.MaterializeEcology(w, id, record.StartDays + record.Duration); w.Tiles[id].Cultivation = (float)CultivationEconomy.Intensity(w.Config, w.Tiles[id].CultivationCapital); }
                    foreach (int id in labor.Footprint) w.Tiles[id].CultivationOperatingFraction = 0;
                }
            }
            LastMetrics.ProductionMilliseconds = phaseClock.Elapsed.TotalMilliseconds;
            LastMetrics.ProductionReceipts = record.Production.Count;
            ApplyStorage(w, record, intervals);

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

        private static void ResolveManagedHerds(WorldState w, TurnRecord record, TurnHorizon horizon, Dictionary<int, TurnNeeds> needs)
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
            clothingPlant = Math.Min(clothingPlant, afterMeals);
            clothingAnimal = Math.Min(clothingAnimal, Math.Max(0, afterMeals - clothingPlant));
            double clothing = clothingPlant + clothingAnimal;
            double available = netIncome + 1e-8 >= priorityIncome + clothing && projectedFood + 1e-8 >= populationBase + clothing + needs.ReserveTarget
                ? Math.Min(Math.Max(0, netIncome - priorityIncome - clothing), Math.Max(0, projectedFood - populationBase - clothing - needs.ReserveTarget))
                : 0;
            double maximumRate = QoLMortality.MaximumRate(w, camp);
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
            if (maximumRate > 1e-12 && available > 1e-12)
            {
                for (int iteration = 0; iteration < 64; iteration++)
                {
                    double rate = (low + high) * .5;
                    var plan = ForRate(rate);
                    if (plan.GrowthFood + plan.ManufacturingInput - clothing <= available + 1e-8) low = rate; else high = rate;
                }
            }
            var result = ForRate(low);
            double manufacturingBudget = Math.Max(0, available - result.GrowthFood);
            double otherPlant = Math.Min(result.PlantManufacturingInput - clothingPlant, manufacturingBudget);
            manufacturingBudget -= otherPlant;
            double otherAnimal = Math.Min(result.AnimalManufacturingInput - clothingAnimal, manufacturingBudget);
            result.PlantManufacturingInput = clothingPlant + Math.Max(0, otherPlant);
            result.AnimalManufacturingInput = clothingAnimal + Math.Max(0, otherAnimal);
            result.ManufacturingInput = result.PlantManufacturingInput + result.AnimalManufacturingInput;
            return result;
        }

        internal static void FoodBucketTargets(WorldState w, Camp camp, double intervals, double years, double populationBase, double initialFood, out double consumptionIncome, out double reserveTarget, out double reserveFill, out double maximumGrowthFood)
        {
            double maximumRate = QoLMortality.MaximumRate(w, camp);
            // The reserve bucket belongs to the people who exist when this turn resolves.
            // Refill their complete configured operating reserve, bounded by what local
            // storage can retain, before funding births. Newborns happen afterward and
            // neither consume nor acquire reserves retroactively.
            double desiredReserve = NeolithicProduction.DesiredReserveFood(w, populationBase / Math.Max(1e-12, w.Config.Consumption * intervals), intervals);
            // Pots depreciate later in ResolveCampNeolithic.  Reserve and growth
            // projection must use their post-depreciation capacity now so every
            // food source shares the same receiving envelope and ApplyStorage
            // does not discard an accepted receipt.
            double achievableStorage = NeolithicProduction.StorageCapacityAfterPotDepreciation(w, camp, intervals, years);
            reserveTarget = Math.Min(desiredReserve, achievableStorage);
            double expiringInventory = Math.Max(0, initialFood - reserveTarget);
            double inventoryConsumedFirst = Math.Min(populationBase, expiringInventory);
            consumptionIncome = populationBase - inventoryConsumedFirst;
            double inventoryAfterConsumption = initialFood - inventoryConsumedFirst;
            reserveFill = Math.Max(0, reserveTarget - inventoryAfterConsumption);
            maximumGrowthFood = GrowthFood(populationBase, maximumRate, years);
        }

        internal static double GrowthFood(double populationBase, double rate, double years)
        {
            if (rate <= 1e-12 || years <= 0) return 0;
            double x = rate * years;
            return populationBase * ((Math.Exp(x) - 1) / x - 1);
        }

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

        // Human acceptance is ordered: current-population consumption, complete refill of
        // the configured operating reserve, then the food budget for maximum growth.
        // Already-realized connected hunting consumes this budget.
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

        private static void SustainGeneration(WorldState w, TurnRecord record, double intervals, double generationScale, Dictionary<int, TurnNeeds> needs, Dictionary<int, GrowthPlan> plans, Dictionary<int, double> foraged, HashSet<int> recovering, Dictionary<int, GrowthPlan> pendingBirths,
            List<(Camp Camp, Hunter[] Linked, int Losses)> pendingCampDeaths, List<(Hunter Hunter, int Losses)> pendingHunterDeaths, ResolutionMetrics metrics)
        {
            foreach (var camp in w.Camps.OrderBy(c => c.Id))
            {
                var linked = w.Hunters.Where(h => h.SourceCamp == camp.Id && Supply.Connected(w, h)).OrderBy(h => h.Id).ToArray();
                int actualConsumers = camp.Population + linked.Sum(h => h.Population);
                int total = needs[camp.Id].FoodConsumers;
                if (actualConsumers != total) throw new InvalidOperationException("Prepared food cohort changed during resolution for camp " + camp.Id + ".");
                double required = needs[camp.Id].ConsumptionRequired;
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
                    if (ResolutionMetrics.CaptureCraftWorkDiagnostics)
                        metrics.CraftFoodFlowFor(camp.Id).GrowthFoodUsed = growthFood;
                    pendingBirths[camp.Id] = plans[camp.Id];
                }
                summary.FoodConsumed += camp.LastConsumed;
            }
            foreach (var hunter in w.Hunters.Where(h => !Supply.Connected(w, h)).OrderBy(h => h.Id))
            {
                double required = hunter.Population * w.Config.Consumption * intervals;
                double local = Math.Min(required, foraged.TryGetValue(hunter.Id, out var gathered) ? gathered : 0);
                double carried = Math.Min(required - local, hunter.CarriedFood); hunter.CarriedFood -= carried;
                hunter.LastConsumed = local + carried; hunter.FoodDeficit = required - hunter.LastConsumed;
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
                summary.HuntFood = harvest.Where(t => t.Kind == "Hunting").Sum(t => t.Food); summary.FishFood = harvest.Where(t => t.Kind == "Fishing").Sum(t => t.Food); double forage = harvest.Where(t => t.Kind == "Foraging").Sum(t => t.Food); summary.ForageFood = forage;
                summary.AnimalFood = harvest.Where(t => t.Kind == "Animal Product").Sum(t => t.Food);
                summary.SlaughterFood = harvest.Where(t => t.Kind == "Slaughter").Sum(t => t.Food);
                summary.Slaughtered = record.ManagedHerds.Where(m => m.Camp == camp.Id).Sum(m => m.Slaughtered);
                summary.FoodProduced = summary.TerrestrialFood + summary.MarineFood + summary.LakeFood + summary.HuntFood + summary.FishFood + summary.AnimalFood + summary.SlaughterFood + forage;
                summary.FoodReceived = summary.FoodProduced - index.Logistics[camp.Id].Sum(l => l.HarvestOverflow);
                summary.DiscardedInventory += index.Logistics[camp.Id].Sum(l => l.InventoryLoss);
                summary.Additions = index.Population[camp.Id].Where(p => p.Count > 0).Sum(p => p.Count); summary.Deaths = -index.Population[camp.Id].Where(p => p.Count < 0).Sum(p => p.Count);
                summary.EndingPopulation = w.LinkedPopulation(camp); summary.NetPercent = 100.0 * (summary.EndingPopulation - summary.StartingPopulation) / Math.Max(1, summary.StartingPopulation);
                summary.FinalReserves = camp.Food + index.CampHunters[camp.Id].OrderBy(h => h.Id).Sum(h => h.CarriedFood); summary.FractionalProgress = camp.GrowthRemainder;
                double balanceDiff = summary.InitialReserves + summary.FoodReceived - summary.FoodConsumed - summary.ManufacturingFood - summary.DiscardedInventory - summary.FinalReserves;
                if (Math.Abs(balanceDiff) > 1e-5) throw new InvalidOperationException($"Unexplained food balance in demographic report for Camp {camp.Id}: Initial={summary.InitialReserves:F4}, Recv={summary.FoodReceived:F4}, Consumed={summary.FoodConsumed:F4}, Discarded={summary.DiscardedInventory:F4}, Final={summary.FinalReserves:F4}, Diff={balanceDiff:F6}");
                summary.LimitingFactor = summary.DeficitSteps > 0 ? "Food deficits limited growth" : summary.Additions > 0 ? "Growth supported by food surplus; demand rose with population" : summary.SurplusSteps > 0 ? "Demographic rounding; fractional progress " + summary.FractionalProgress.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) : summary.RecoverySteps > 0 ? "Starvation recovery" : "No sustainable food surplus or insufficient reserves";
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
                double fishing = index.Harvest[camp.Id].Where(t => hunterIds.Contains(t.Entity) && t.Kind == "Fishing").Sum(t => t.Food) * w.Config.ActiveFishingPracticeMultiplier + passiveAquatic * w.Config.PassiveAquaticFishingPracticeShare;
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
                TechnologyDiscovery.AddPractice(camp.People, Domain.Hunting, budget * hunting / total * w.Config.HuntingPracticeMultiplier, PracticeSource.Hunting);
                TechnologyDiscovery.AddPractice(camp.People, Domain.Gathering, budget * gathering / total, PracticeSource.Gathering);
                TechnologyDiscovery.AddPractice(camp.People, Domain.Fishing, budget * fishing / total, PracticeSource.Fishing);
            }
        }
    }
}
