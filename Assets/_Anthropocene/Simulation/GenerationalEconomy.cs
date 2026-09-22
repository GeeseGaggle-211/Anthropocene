using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    internal sealed class GenerationResource
    {
        public int Tile;
        public double AvailableFood;
    }

    internal sealed class GrowthPlan
    {
        public double Required, ConsumptionRequired, GrowthFood, ReproductivePopulation, FinalPopulation, ManufacturingInput;
    }

    public sealed partial class TurnResolver
    {
        private void ResolveEcology(WorldState w, List<Order> orders, HashSet<int> establishedHunters, TurnRecord record, Action<int> touch)
        {
            double intervals = record.Duration / w.Config.BaseDays;
            double years = record.Duration / w.Config.DaysPerYear;
            double generationScale = record.Duration / w.Config.PracticeDays;
            var resources = new Dictionary<int, GenerationResource>();
            var foraged = new Dictionary<int, double>();
            var recovering = new HashSet<int>(w.Camps.Where(c => c.DeficitTurns > 0).Select(c => c.Id));
            var agriculturalLabor = w.Camps.OrderBy(c => c.Id).ToDictionary(c => c.Id, c => CultivationEconomy.Prepare(w, c, record.StartDays));

            GenerationResource Resource(int tile)
            {
                if (resources.TryGetValue(tile, out var value)) return value;
                value = new GenerationResource { Tile = tile, AvailableFood = LazyEcology.AccessibleAvailability(w, tile, record.StartDays, record.Duration) };
                resources.Add(tile, value); return value;
            }

            foreach (var camp in w.Camps.OrderBy(c => c.Id))
            {
                record.Demographics.Add(new DemographicReport { Camp = camp.Id, StartingPopulation = w.LinkedPopulation(camp), Years = years, InitialReserves = camp.Food + w.Hunters.Where(h => h.SourceCamp == camp.Id).OrderBy(h => h.Id).Sum(h => h.CarriedFood) });
                camp.StepFoodIncome = 0;
            }

            // Movement happens once. Herd reproduction is realized before the generation's hunting harvest.
            var startingHerdPopulation = w.Herds.OrderBy(h => h.Id).ToDictionary(h => h.Id, h => h.Animals);
            ReproduceHerds(w, intervals, years);
            Hunt(w, orders, establishedHunters, startingHerdPopulation, record, intervals, years);
            foreach (var herd in w.Herds.Where(h => h.DomesticCamp >= 0 || !w.Config.WildHerdFissionEnabled))
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
                if (CultivationEconomy.Enabled(camp))
                {
                    // Agricultural claims are replanned at seasonal barriers, after upkeep
                    // is recomputed from persistent E. Keep the full footprint active.
                    foreach (int id in labor.Footprint.Concat(w.Map.Radius(camp.Tile, w.Config.GatherRadius).Where(id => PassiveGathering.Eligible(w, camp, id))).Distinct())
                        demands.Add(new Demand { Tile = id, Entity = camp.Id, Kind = "Field operation" });
                    continue;
                }
                if (gatherers <= 0) continue;
                var requests = new List<Demand>(); double total = 0;
                foreach (int id in w.Map.Radius(camp.Tile, w.Config.GatherRadius).OrderBy(n => n))
                {
                    if (!PassiveGathering.Eligible(w, camp, id)) continue;
                    var available = Resource(id); var tile = w.Tiles[id];
                    double food = available.AvailableFood * Math.Min(1, Numbers.Fraction(w.Config.GatherFraction, intervals) * gatherers / w.Config.PopulationScale);
                    requests.Add(new Demand { Entity = camp.Id, Tile = id, Kind = PassiveGathering.Kind(tile), ImpactMultiplier = tile.Aquatic ? PracticeEffects.FishingImpact(w.Config, camp) : PracticeEffects.GatheringImpact(w.Config, camp), Food = food }); total += food;
                }
                double scale = Math.Min(1, gatherers * w.Config.GatherPerPerson * intervals / Math.Max(1e-12, total));
                foreach (var request in requests) { request.Food *= scale; campClaims[camp.Id].Add(request); }
            }
            var feedstockForecasts = w.Camps.OrderBy(c => c.Id).ToDictionary(c => c.Id, c =>
            {
                if (!CultivationEconomy.Enabled(c)) return c.Population;
                var labor = agriculturalLabor[c.Id];
                double available = w.Map.Radius(c.Tile, w.Config.GatherRadius)
                    .Where(id => PassiveGathering.Eligible(w, c, id))
                    .Sum(id => LazyEcology.AccessibleAvailability(w, id, record.StartDays, record.Duration));
                double laborCapacity = (labor.OperatingFarmers * w.Config.FarmerProductivity + labor.FoodGatherers) * w.Config.GatherPerPerson * intervals;
                return PlanningResidentPopulation(w, c, intervals, years, c.StepFoodIncome + Math.Min(available, laborCapacity), Math.Max(0, c.Food - c.StepFoodIncome));
            });
            foreach (var camp in w.Camps.OrderBy(c => c.Id))
            {
                var claims = campClaims[camp.Id];
                double scale = Math.Min(1, BiologicalFoodDemand(w, camp, intervals, years) / Math.Max(1e-12, claims.Where(d => !d.Manufacturing).Sum(d => d.Food)));
                double inputScale = Math.Min(1, FabricFeedstockShortfall(w, camp, intervals, years, feedstockForecasts[camp.Id]) / Math.Max(1e-12, claims.Where(d => d.Manufacturing).Sum(d => d.Food)));
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
            foreach (var herd in w.Herds.OrderBy(h => h.Id))
            {
                var species = w.Config.GetSpecies(herd.Species);
                herd.UnmetFood = herd.Animals * species.FoodConsumption * intervals; herd.UnmetFreshwater = herd.Animals * species.FreshwaterConsumption * intervals;
                int source = FreshwaterAccess.Source(w, herd.Tile);
                demands.Add(new Demand { Tile = herd.Tile, Entity = herd.Id, Kind = "Herd", PotentialFood = herd.UnmetFood, Freshwater = source == herd.Tile ? herd.UnmetFreshwater : 0 });
                if (source != herd.Tile) demands.Add(new Demand { Tile = source, Entity = herd.Id, Kind = "Herd", Freshwater = herd.UnmetFreshwater });
            }

            CommitGenerationResources(w, demands, record, touch, foraged, intervals, agriculturalLabor, feedstockForecasts);

            var plans = w.Camps.OrderBy(c => c.Id).ToDictionary(c => c.Id, c => PlanGrowth(w, c, intervals, years));
            foreach (var camp in w.Camps.OrderBy(c => c.Id)) record.Demographics.First(d => d.Camp == camp.Id).FoodRequired += plans[camp.Id].Required;
            foreach (var hunter in w.Hunters.Where(h => !Supply.Connected(w, h))) record.Demographics.First(d => d.Camp == hunter.SourceCamp).FoodRequired += hunter.Population * w.Config.Consumption * intervals;
            SustainGeneration(w, record, intervals, generationScale, plans, foraged, recovering);
            Supply.Finish(w, record);
            // Manufacturing and needs use the same assigned population. Unused gathering
            // worker-time can source materials; realized food gathering earns bridge effort.
            foreach (var camp in w.Camps.OrderBy(c => c.Id).ToArray())
            {
                var labor = agriculturalLabor[camp.Id];
                double gathered = record.Transfers.Where(t => t.Entity == camp.Id && (t.Kind == "Gathering" || t.Kind == "Marine gathering" || t.Kind == "Aquatic gathering")).Sum(t => t.Food);
                labor.GatheringWorkerIntervals = Math.Min(labor.Gatherers * intervals, gathered / w.Config.GatherPerPerson);
                NeolithicProduction.ResolveCampNeolithic(w, camp, intervals, years, record, Math.Max(0, labor.Gatherers * intervals - labor.GatheringWorkerIntervals));
                if (CultivationEconomy.Enabled(camp))
                {
                    foreach (int id in labor.Footprint) { LazyEcology.MaterializeEcology(w, id, record.StartDays + record.Duration); w.Tiles[id].Cultivation = (float)CultivationEconomy.Intensity(w.Config, w.Tiles[id].CultivationCapital); }
                    foreach (int id in labor.Footprint) w.Tiles[id].CultivationOperatingFraction = 0;
                }
            }
            ApplyStorage(w, record, intervals);

            if (w.Config.WildHerdFissionEnabled) SplitHerds(w, record);

            w.Days += record.Duration;
            FinishDemographicReports(w, record, orders);
        }

        private static void AddAgriculturalClaims(WorldState w, Camp camp, AgriculturalLabor labor, double intervals, double biological, double feedstock, Func<int, double> availability, List<Demand> claims)
        {
            var eligible = w.Map.Radius(camp.Tile, w.Config.GatherRadius).Where(id => PassiveGathering.Eligible(w, camp, id)).OrderBy(id => id).ToArray();
            // Operators harvest the same accessible terrestrial supply as gatherers, not a crop-only pool.
            var fields = eligible.Where(id => w.Tiles[id].Land).ToArray();
            var remaining = eligible.ToDictionary(id => id, availability);
            double Allocate(IEnumerable<int> candidates, double requested, bool farmer, bool manufacturing)
            {
                var ids = candidates.Where(remaining.ContainsKey).OrderBy(id => id).ToArray();
                double available = ids.Sum(id => remaining[id]), accepted = Math.Min(Math.Max(0, requested), available);
                if (accepted <= 0) return 0;
                foreach (int id in ids)
                {
                    double amount = accepted * remaining[id] / available; remaining[id] = Math.Max(0, remaining[id] - amount);
                    claims.Add(new Demand { Entity = camp.Id, Tile = id, Kind = farmer ? "Cultivation" : PassiveGathering.Kind(w.Tiles[id]), Food = amount, Manufacturing = manufacturing, ImpactMultiplier = w.Tiles[id].Aquatic ? PracticeEffects.FishingImpact(w.Config, camp) : PracticeEffects.GatheringImpact(w.Config, camp) });
                }
                return accepted;
            }
            double farmCapacity = labor.OperatingFarmers * w.Config.FarmerProductivity * w.Config.GatherPerPerson * intervals;
            double gatherCapacity = labor.FoodGatherers * w.Config.GatherPerPerson * intervals;
            double farmInput = Allocate(fields.Where(id => w.Tiles[id].Land), Math.Min(farmCapacity, feedstock), true, true);
            double gatheredInput = Allocate(eligible.Where(id => w.Tiles[id].Land), Math.Min(gatherCapacity, feedstock - farmInput), false, true);
            double farm = Allocate(fields, Math.Min(farmCapacity - farmInput, biological), true, false);
            Allocate(eligible, Math.Min(gatherCapacity - gatheredInput, biological - farm), false, false);
        }

        private void CommitGenerationResources(WorldState w, List<Demand> demands, TurnRecord record, Action<int> touch, Dictionary<int, double> foraged, double intervals, Dictionary<int, AgriculturalLabor> agriculturalLabor, Dictionary<int, double> feedstockForecasts)
        {
            var herds = w.Herds.ToDictionary(h => h.Id); var camps = w.Camps.ToDictionary(c => c.Id); var hunters = w.Hunters.ToDictionary(h => h.Id);
            var transfers = new List<Transfer>();
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var groups = demands.GroupBy(d => d.Tile).OrderBy(g => g.Key).Select(g => new { Tile = g.Key, Claims = g.ToArray() }).ToArray();
            // Materialization is a sequential write barrier before any worker reads the active state.
            foreach (var group in groups) { LazyEcology.MaterializeEcology(w, group.Tile, record.StartDays); touch(group.Tile); }
            var agriculturalCamps = w.Camps.Where(CultivationEconomy.Enabled).OrderBy(c => c.Id).ToArray();
            double years = record.Duration / w.Config.DaysPerYear;
            var biologicalBudgets = agriculturalCamps.ToDictionary(c => c.Id, c => BiologicalFoodDemand(w, c, intervals, years));
            var inputBudgets = agriculturalCamps.ToDictionary(c => c.Id, c => FabricFeedstockShortfall(w, c, intervals, years, feedstockForecasts[c.Id]));
            var acceptedFood = agriculturalCamps.ToDictionary(c => c.Id, c => 0d);
            var acceptedPlant = agriculturalCamps.ToDictionary(c => c.Id, c => 0d);
            int FoodCamp(string kind, int entity) => kind == "Fishing" ? (Supply.Connected(w, hunters[entity]) ? hunters[entity].SourceCamp : -1) : kind == "Herd" || kind == "Foraging" ? -1 : entity;
            foreach (int tile in agriculturalCamps.SelectMany(c => agriculturalLabor[c.Id].Footprint).Distinct().OrderBy(id => id)) touch(tile);
            // Canonical seasonal barriers couple construction/abandonment to ecology.
            // Work cannot be deposited at the end of a generation without having aged.
            var segments = agriculturalCamps.Length == 0 ? new[] { new EcologySegment(w.Season, record.Duration) } : LazyEcology.Segments(record.StartDays, record.Duration, w.Config.BaseDays).ToArray();
            double start = record.StartDays;
            foreach (var segment in segments)
            {
                double fraction = segment.Days / record.Duration;
                var stepClaims = demands.Select(d => new Demand { Entity = d.Entity, Tile = d.Tile, Kind = d.Kind, Food = d.Food * fraction, PotentialFood = d.PotentialFood * fraction, Freshwater = d.Freshwater * fraction, ImpactMultiplier = d.ImpactMultiplier, Manufacturing = d.Manufacturing }).ToList();
                var availability = new Dictionary<int, double>();
                double Available(int id)
                {
                    if (!availability.TryGetValue(id, out double amount)) availability[id] = amount = LazyEcology.AccessibleAvailability(w, id, start, segment.Days);
                    return amount;
                }
                foreach (var camp in agriculturalCamps) agriculturalLabor[camp.Id] = CultivationEconomy.Prepare(w, camp, start);
                foreach (var camp in agriculturalCamps)
                {
                    double acceptedInput = Math.Min(inputBudgets[camp.Id], acceptedPlant[camp.Id]);
                    double input = Math.Max(0, inputBudgets[camp.Id] - acceptedInput);
                    double biological = Math.Max(0, biologicalBudgets[camp.Id] - (acceptedFood[camp.Id] - acceptedInput));
                    AddAgriculturalClaims(w, camp, agriculturalLabor[camp.Id], segment.Days / w.Config.BaseDays, biological, input, Available, stepClaims);
                    var claims = stepClaims.Where(d => FoodCamp(d.Kind, d.Entity) == camp.Id).ToArray();
                    double scale = Math.Min(1, biological / Math.Max(1e-12, claims.Where(d => !d.Manufacturing).Sum(d => d.Food)));
                    double inputScale = Math.Min(1, input / Math.Max(1e-12, claims.Where(d => d.Manufacturing).Sum(d => d.Food)));
                    foreach (var claim in claims) claim.Food *= claim.Manufacturing ? inputScale : scale;
                }
                var stepGroups = stepClaims.GroupBy(d => d.Tile).OrderBy(g => g.Key).Select(g => new { Tile = g.Key, Claims = g.ToArray() }).ToArray();
                var results = execution.Map(stepGroups, group => EcologicalPressure.Integrate(w, group.Tile, group.Claims, start, segment.Days));
                // No worker mutates entities, allocates IDs, or appends history.
                for (int i = 0; i < stepGroups.Length; i++)
                {
                    var result = results[i]; result.View.Commit(w.Tiles[stepGroups[i].Tile]); record.Pressure.AddRange(result.Pressure); record.EcologyBalances.Add(result.Balance); transfers.AddRange(result.Transfers);
                    foreach (var transfer in result.Transfers)
                    {
                        int camp = FoodCamp(transfer.Kind, transfer.Entity);
                        if (!acceptedFood.ContainsKey(camp)) continue;
                        acceptedFood[camp] += transfer.Food;
                        var tile = w.Tiles[transfer.Tile];
                        if (transfer.Kind != "Fishing" && !tile.Aquatic && tile.WaterBody != WaterKind.Saltwater) acceptedPlant[camp] += transfer.Food;
                    }
                }
                start += segment.Days;
                foreach (var camp in agriculturalCamps)
                {
                    var labor = agriculturalLabor[camp.Id];
                    foreach (int id in labor.Footprint) LazyEcology.MaterializeEcology(w, id, start);
                    double terrestrialGathered = results.Sum(result => result.Transfers.Where(t => t.Entity == camp.Id && t.Kind == "Gathering").Sum(t => t.Food));
                    CultivationEconomy.Expand(w, camp, terrestrialGathered / w.Config.GatherPerPerson * w.Config.BaseDays / w.Config.DaysPerYear, true, record);
                    CultivationEconomy.Expand(w, camp, labor.ExpansionFarmers * segment.Days / w.Config.DaysPerYear, false, record);
                }
            }
            LastMetrics.ActiveTiles = groups.Length; LastMetrics.EcologyMilliseconds = timer.Elapsed.TotalMilliseconds;
            foreach (var transfer in transfers)
            {
                if (transfer.Kind == "Herd")
                {
                    var herd = herds[transfer.Entity]; herd.UnmetFood = Math.Max(0, herd.UnmetFood - transfer.PotentialFood); herd.UnmetFreshwater = Math.Max(0, herd.UnmetFreshwater - transfer.Freshwater);
                }
                else if (transfer.Kind == "Foraging")
                {
                    foraged[transfer.Entity] = (foraged.TryGetValue(transfer.Entity, out var existing) ? existing : 0) + transfer.Food;
                }
                else
                {
                    var camp = transfer.Kind == "Fishing" ? camps[hunters[transfer.Entity].SourceCamp] : camps[transfer.Entity];
                    if (transfer.Kind == "Fishing")
                    {
                        Supply.Award(w, hunters[transfer.Entity], transfer.Food, record, "Fishing");
                    }
                    else
                    {
                        if (transfer.Kind == "Hunting") camp.AnimalFood += transfer.Food;
                        else if (w.Tiles[transfer.Tile].Aquatic || w.Tiles[transfer.Tile].WaterBody == WaterKind.Saltwater) camp.AquaticFood += transfer.Food;
                        else camp.PlantFood += transfer.Food;
                        camp.StepFoodIncome += transfer.Food;
                    }
                }
                record.Transfers.Add(transfer);
            }
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

        private static GrowthPlan PlanGrowth(WorldState w, Camp camp, double intervals, double years)
        {
            int reproductivePopulation = camp.Population + w.Hunters.Where(h => h.SourceCamp == camp.Id && Supply.Connected(w, h)).Sum(h => h.Population);
            double populationBase = reproductivePopulation * w.Config.Consumption * intervals;
            double initialFood = Math.Max(0, camp.Food - camp.StepFoodIncome);
            FoodBucketTargets(w, camp, intervals, years, populationBase, initialFood, out double consumptionIncome, out double reserveTarget, out double reserveFill, out double maximumGrowthFood);
            double priorityIncome = consumptionIncome + reserveFill;
            double available = camp.StepFoodIncome + 1e-8 >= priorityIncome && camp.Food + 1e-8 >= populationBase + reserveTarget
                ? Math.Min(Math.Max(0, camp.StepFoodIncome - priorityIncome), Math.Max(0, camp.Food - populationBase - reserveTarget))
                : 0;
            double maximumRate = w.Config.AnnualHumanGrowthRate * camp.DemographicGrowthMultiplier;
            GrowthPlan ForRate(double rate)
            {
                double growthFood = GrowthFood(populationBase, rate, years);
                double finalPopulation = reproductivePopulation * Math.Exp(rate * years);
                int residents = camp.Population + (int)Math.Floor(camp.GrowthRemainder + Math.Max(0, finalPopulation - reproductivePopulation));
                double input = NeolithicProduction.FabricFeedstockNeed(w, camp, intervals, years, residents);
                return new GrowthPlan { Required = populationBase + growthFood, ConsumptionRequired = populationBase, GrowthFood = growthFood, ReproductivePopulation = reproductivePopulation, FinalPopulation = finalPopulation, ManufacturingInput = input };
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
                    if (plan.GrowthFood + plan.ManufacturingInput <= available + 1e-8) low = rate; else high = rate;
                }
            }
            var result = ForRate(low);
            result.ManufacturingInput = Math.Min(result.ManufacturingInput, Math.Max(0, available - result.GrowthFood));
            return result;
        }

        private static void FoodBucketTargets(WorldState w, Camp camp, double intervals, double years, double populationBase, double initialFood, out double consumptionIncome, out double reserveTarget, out double reserveFill, out double maximumGrowthFood)
        {
            double maximumRate = camp.DeficitTurns <= 0 ? w.Config.AnnualHumanGrowthRate * camp.DemographicGrowthMultiplier : 0;
            // The reserve bucket belongs to the people who exist when this turn resolves.
            // Refill their complete configured operating reserve, bounded by what local
            // storage can retain, before funding births. Newborns happen afterward and
            // neither consume nor acquire reserves retroactively.
            reserveTarget = populationBase * Math.Min(w.Config.GrowthReserveTurns, StorageGenerations(w, camp.Tile));
            double expiringInventory = Math.Max(0, initialFood - reserveTarget);
            double inventoryConsumedFirst = Math.Min(populationBase, expiringInventory);
            consumptionIncome = populationBase - inventoryConsumedFirst;
            double inventoryAfterConsumption = initialFood - inventoryConsumedFirst;
            reserveFill = Math.Max(0, reserveTarget - inventoryAfterConsumption);
            maximumGrowthFood = GrowthFood(populationBase, maximumRate, years);
        }

        private static double GrowthFood(double populationBase, double rate, double years)
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
        private static int PlanningPopulation(WorldState w, Camp camp) => camp.Population + w.Hunters.Where(h => h.SourceCamp == camp.Id && Supply.Connected(w, h)).Sum(h => h.Population);
        public static double PlanningResidentPopulation(WorldState w, Camp camp, double biologicalIncome)
            => PlanningResidentPopulation(w, camp, w.Config.GenerationIntervals, w.Config.TurnDays / w.Config.DaysPerYear, biologicalIncome, camp.Food);
        private static double PlanningResidentPopulation(WorldState w, Camp camp, double intervals, double years, double biologicalIncome, double initialFood)
        {
            int reproductive = PlanningPopulation(w, camp);
            double populationBase = reproductive * w.Config.Consumption * intervals;
            FoodBucketTargets(w, camp, intervals, years, populationBase, initialFood, out double consumption, out _, out double reserve, out double growth);
            double maximumRate = camp.DeficitTurns > 0 ? 0 : w.Config.AnnualHumanGrowthRate * camp.DemographicGrowthMultiplier;
            GrowthPlan ForRate(double rate)
            {
                double finalPopulation = reproductive * Math.Exp(rate * years);
                int residents = camp.Population + (int)Math.Floor(camp.GrowthRemainder + Math.Max(0, finalPopulation - reproductive));
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
            double input = NeolithicProduction.FabricFeedstockNeed(w, camp, intervals, years, residentPopulation);
            if (input <= 0) return 0;
            int population = camp.Population + w.Hunters.Where(h => h.SourceCamp == camp.Id && Supply.Connected(w, h)).Sum(h => h.Population);
            double consumption = population * w.Config.Consumption * intervals;
            double reserve = consumption * Math.Min(w.Config.GrowthReserveTurns, StorageGenerations(w, camp.Tile));
            double sparePlant = Math.Min(camp.PlantFood, Math.Max(0, camp.Food - consumption - reserve));
            return Math.Max(0, input - sparePlant);
        }
        private static void ConsumeCampFood(Camp camp, double amount, double manufacturingInput)
        {
            // Basic needs always get paid. Preserve plant feedstock only to the extent
            // other food can cover this debit; there is still one conserved inventory.
            double protectedPlant = Math.Min(camp.PlantFood, Math.Min(Math.Max(0, camp.Food - amount), manufacturingInput));
            if (protectedPlant <= 0) { camp.Food -= amount; return; }
            double available = camp.Food - protectedPlant;
            double retained = available > 0 ? Math.Max(0, available - amount) / available : 0;
            camp.PlantFood = protectedPlant + (camp.PlantFood - protectedPlant) * retained;
            camp.AnimalFood *= retained; camp.AquaticFood *= retained;
        }

        private static void SustainGeneration(WorldState w, TurnRecord record, double intervals, double generationScale, Dictionary<int, GrowthPlan> plans, Dictionary<int, double> foraged, HashSet<int> recovering)
        {
            foreach (var camp in w.Camps.OrderBy(c => c.Id))
            {
                var linked = w.Hunters.Where(h => h.SourceCamp == camp.Id && Supply.Connected(w, h)).OrderBy(h => h.Id).ToArray();
                int total = camp.Population + linked.Sum(h => h.Population); double required = plans[camp.Id].ConsumptionRequired;
                camp.LastConsumed = Math.Min(required, camp.Food); ConsumeCampFood(camp, camp.LastConsumed, plans[camp.Id].ManufacturingInput); camp.FoodDeficit = required - camp.LastConsumed;
                camp.FoodNeedsMetRatio = required > 0 ? camp.LastConsumed / required : 1;
                if (camp.FoodDeficit > 1e-8) { camp.DeficitTurns++; camp.DeficitSeverity += camp.FoodDeficit / Math.Max(1, required) * generationScale; }
                else { camp.DeficitTurns = 0; camp.DeficitSeverity = 0; }
                if (camp.DeficitTurns > w.Config.StarvationGraceTurns)
                {
                    double lossProgress = camp.DeathRemainder + total * Numbers.Fraction(w.Config.Mortality, generationScale) * camp.FoodDeficit / Math.Max(1e-9, required);
                    int losses = Math.Min(total, (int)Math.Floor(lossProgress)); camp.DeathRemainder = lossProgress - Math.Floor(lossProgress);
                    ApplySharedDeaths(camp, linked, losses, record); record.Log("Tribe " + camp.Player + " lost " + losses + " people to sustained hunger.");
                }
                foreach (var h in linked) { h.DeficitTurns = camp.DeficitTurns; h.FoodDeficit = camp.FoodDeficit * h.Population / Math.Max(1, total); h.LastConsumed = Math.Max(0, h.Population * w.Config.Consumption * intervals - h.FoodDeficit); }
                var summary = record.Demographics.First(d => d.Camp == camp.Id);
                if (camp.FoodDeficit > 1e-8) summary.DeficitSteps++;
                else if (recovering.Contains(camp.Id)) summary.RecoverySteps++;
                else
                {
                    double growthFood = Math.Min(plans[camp.Id].GrowthFood, camp.Food);
                    ConsumeCampFood(camp, growthFood, plans[camp.Id].ManufacturingInput); camp.LastConsumed += growthFood;
                    AddGrowth(w, camp, plans[camp.Id], record, summary);
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
                    double lossProgress = hunter.DeathRemainder + hunter.Population * Numbers.Fraction(w.Config.Mortality, generationScale) * hunter.FoodDeficit / Math.Max(1e-9, required);
                    int deaths = Math.Min(hunter.Population, (int)Math.Floor(lossProgress)); hunter.DeathRemainder = lossProgress - Math.Floor(lossProgress);
                    hunter.Population -= deaths; record.Deaths += deaths; record.PopulationChanges.Add(new PopulationChange { Camp = hunter.SourceCamp, Entity = hunter.Id, Kind = "Starvation", Count = -deaths });
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
            return w.Config.MinimumStorageGenerations + (w.Config.MaximumStorageGenerations - w.Config.MinimumStorageGenerations) * Math.Max(cold, dry);
        }

        private static void ApplyStorage(WorldState w, TurnRecord record, double intervals)
        {
            foreach (var camp in w.Camps.OrderBy(c => c.Id))
            {
                double potMultiplier = NeolithicProduction.StorageCapacityMultiplier(camp);
                double cap = w.LinkedPopulation(camp) * w.Config.Consumption * intervals * StorageGenerations(w, camp.Tile) * potMultiplier;
                double loss = Math.Max(0, camp.Food - cap); camp.Food -= loss;
                var summary = record.Demographics.First(d => d.Camp == camp.Id); summary.DiscardedInventory += loss;
            }
        }

        private static void ReproduceHerds(WorldState w, double intervals, double years)
        {
            foreach (var herd in w.Herds.OrderBy(h => h.Id).ToArray())
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
                var destinations = w.Map.Radius(herd.Tile, 1).Where(n => w.Tiles[n].TraversableLand).OrderByDescending(n => species.Suitability(w.Tiles[n])).ThenBy(n => Numbers.Hash(w.Seed, 151, herd.Id ^ n, w.Turn)).ThenBy(n => n).ToArray();
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
                summary.FoodProduced = summary.TerrestrialFood + summary.MarineFood + summary.LakeFood + summary.HuntFood + summary.FishFood + forage;
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
                camp.Experience[(int)Domain.Hunting] += budget * hunting / total * w.Config.HuntingPracticeMultiplier;
                camp.Experience[(int)Domain.Gathering] += budget * gathering / total;
                camp.Experience[(int)Domain.Fishing] += budget * fishing / total;
            }
        }
    }
}
