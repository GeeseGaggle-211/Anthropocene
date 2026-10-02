using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Anthropocene.Simulation;

// Diagnostic-only stationary plans and explicitly hypothetical endowments.
// No production policy changes; every temporary input is restored before replay.
internal static class PastoralPlannerProbe
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = false };
    private static void Main(string[] args)
    {
        var world = SaveCodec.Load(File.ReadAllBytes(args[1]));
        Directory.CreateDirectory(args[2]);
        if (args[0] == "timeline")
        {
            var founders = new HashSet<int>(world.Camps.Where(c => c.Technology == Technology.AnimalHusbandry).Select(c => c.Id));
            using (var stream = new StreamWriter(Path.Combine(args[2], "founder-timeline.jsonl")))
            {
                for (int t = 0; t <= 50; t++)
                {
                    foreach (var c in world.Camps.Where(c => founders.Contains(c.Id) && c.Population > 0).OrderBy(c => c.Id))
                        stream.WriteLine(JsonSerializer.Serialize(Inspect(world, c, t, "actual"), Json));
                    if (t == 0 || t == 25 || t == 50)
                    {
                        string save = Path.Combine(args[2], "turn-" + t + ".save");
                        File.WriteAllBytes(save, SaveCodec.Save(world));
                        Snapshot(world, args[2], t);
                    }
                    if (t < 50)
                    {
                        var ai = AiPlanning.Decide(world);
                        new TurnResolver().Resolve(world, ai.Orders, ai.Traces);
                    }
                }
            }
            Console.WriteLine("timeline finished population=" + world.Camps.Sum(c => world.LinkedPopulation(c)) + " tribes=" + world.Camps.Count);
        }
        else if (args[0] == "snapshot") Snapshot(world, args[2], args.Length > 3
            ? int.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : world.Turn);
        else throw new ArgumentException("timeline|snapshot save output-directory");
    }

    private static object Inspect(WorldState w, Camp c, int t, string scenario)
    {
        var e = EconomicIntent.From(c);
        e.ManagedBreedingIntensity = ManagedBreedingPolicy.Recommend(w, c, c.Tile, new PlanningIndex(w));
        var d = CampProductionDemand.Build(w, c, e);
        var p = EconomicPlanner.Plan(w, c, e, EconomicPlanner.ConfiguredRule(w.Config), out double price, out _, out double farmPrice);
        var h = d.HerdLadder;
        int selected = p.Assignments.Where(a => a.Role == WorkforceRoles.Herder).Sum(a => a.Population);
        double years = w.Config.TurnDays / w.Config.DaysPerYear;
        TurnResolver.FoodBucketTargets(w, c, w.Config.GenerationIntervals, years, d.Meals, c.Food,
            out double consumptionIncome, out double reserveTarget, out double reserveFill, out double growthFood);
        double humanNow = c.PlantFood + c.AquaticFood + d.ForageAfterHerds;
        double herdMealGap = Math.Max(0, d.Meals - humanNow);
        double herdHumanLeft = Math.Max(0, humanNow - d.Meals);
        double herdReserveGap = Math.Max(0, reserveFill - herdHumanLeft);
        double herdGrowthGap = Math.Max(0, growthFood - Math.Max(0, herdHumanLeft - reserveFill));
        double commonMealGap = Math.Max(0, consumptionIncome - d.ForageAfterHerds);
        double commonReserveGap = Math.Max(0, reserveFill - Math.Max(0, d.ForageAfterHerds - consumptionIncome));
        double commonGrowthGap = Math.Max(0, growthFood - Math.Max(0, d.ForageAfterHerds - consumptionIncome - reserveFill));
        var footprint = w.Map.Radius(c.Tile, 1).Where(id => w.Tiles[id].TraversableLand).ToArray();
        double q = footprint.Length > 0 ? footprint.Average(id => CultivationEconomy.Intensity(w.Config, LazyEcology.ProjectEcology(w, id, w.Days).CultivationCapital)) : 0;
        double milk = h == null ? 0 : h.CurrentAnimalFood[selected];
        double slaughter = h == null ? 0 : h.SlaughterFood[selected];
        var candidate = d.WithAnimalFood(w, c, milk + slaughter);
        double paidExpansionYears = 0;
        if (candidate.Expansion != null)
            for (int i = 0; i < candidate.Expansion.Value.Length && (double.IsNaN(price) || candidate.Expansion.Value[i] >= price); i++)
                paidExpansionYears += candidate.Expansion.WorkerYears[i];
        var baseForecast = d.GrownHerd ?? TurnResolver.ForecastGrownManagedAnimalFood(w, c, e.ManagedBreedingIntensity, w.Config.GenerationIntervals, years, c.Tile);
        var settled = EconomicPlanner.Preview(c, e, c.Population); settled.IsVillage = true;
        HerdLabour.Pasture(w, settled, baseForecast, w.Config.GenerationIntervals, out double localCapacity, out _, d);
        double bestObjective = h == null || double.IsNaN(price) ? 0 : h.TotalValue[selected] - price * h.WorkerYears[selected];
        double spareExpansion = Math.Max(0, (p.Labor?.ExpansionFarmers ?? 0) - Math.Ceiling(paidExpansionYears / years - 1e-8));
        object choices = h == null ? null : Enumerable.Range(h.MinimumHerders, h.Steps - h.MinimumHerders + 1).Select(k => new
        {
            herders = k, target = h.Target(k), milk = h.CurrentAnimalFood[k], slaughter = h.SlaughterFood[k],
            currentValue = h.CurrentValue[k], futureValue = h.FutureValue[k], terminalValue = h.ResidualValue[k],
            managementYears = h.ManagementYears[k], replacementYears = h.ReplacementYears[k], craftYears = h.CraftYears[k],
            harvestSavings = h.SavedHarvestYears[k], netWork = h.WorkerYears[k],
            objective = h.TotalValue[k] - (double.IsNaN(price) ? 0 : price) * h.WorkerYears[k],
            objectiveDeltaFromSelected = h.TotalValue[k] - (double.IsNaN(price) ? 0 : price) * h.WorkerYears[k] - bestObjective,
            // Sensitivity only: retain the existing future work price, while
            // treating idle current workers as zero-cost. Not a new policy.
            freeSpareCurrentObjectiveDelta = h.TotalValue[k] - (double.IsNaN(price) ? 0 : price) *
                (h.WorkerYears[k] - Math.Min(spareExpansion, Math.Max(0, k - selected)) * HerdLabour.DiscountedYears(w.Config.Neolithic.DiscountRatePerYear, years)) - bestObjective
        }).ToArray();
        return new
        {
            postOpeningTurn = t, worldTurn = w.Turn, scenario, c.Id, c.Population, linked = w.LinkedPopulation(c), c.QoL, c.IsVillage,
            cultivation = CultivationEconomy.Enabled(c), environment = w.Tiles[c.Tile].Environment.ToString(),
            regionalMoisture = footprint.Length > 0 ? footprint.Average(id => w.Tiles[id].Seasons.Average(s => s.RegionalMoisture)) : 0,
            soilMoisture = footprint.Length > 0 ? footprint.Average(id => w.Tiles[id].Seasons.Average(s => s.Moisture)) : 0,
            q, c.Food, c.PlantFood, c.AnimalFood, d.Meals, consumptionIncome, reserveTarget, reserveFill, growthFood,
            d.Biological, d.ForageAfterHerds, d.TerrestrialAfterHerds, d.Upkeep, d.HarvestProductivity,
            herdMealGap, herdReserveGap, herdGrowthGap, commonMealGap, commonReserveGap, commonGrowthGap,
            futureAnnualMealGap = Math.Max(0, (d.Meals - d.ForageAfterHerds) / years),
            futureAnnualGrowthGap = Math.Max(0, growthFood / years - Math.Max(0, (d.ForageAfterHerds - d.Meals) / years)),
            animals = HerdingTooling.DomesticAnimals(w, c), grownAnimals = baseForecast.Animals,
            grownManagementFulfillment = baseForecast.ManagementFulfillment, localCapacity, mobileCapacity = h?.PastureCapacity,
            recommendedBreeding = e.ManagedBreedingIntensity, selectedBreeding = p.ManagedBreedingIntensity,
            selectedHerders = selected, selectedTarget = p.HerdTarget, price, farmPrice,
            farmers = p.Assignments.Where(a => a.Role == WorkforceRoles.Farmer).Sum(a => a.Population),
            gatherers = p.Assignments.Where(a => a.Role == WorkforceRoles.Gatherer).Sum(a => a.Population),
            expansionFarmers = p.Labor?.ExpansionFarmers ?? 0,
            candidateExpansionSteps = candidate.Expansion?.Value.Length ?? 0,
            candidateFirstExpansionValue = candidate.Expansion?.Value.FirstOrDefault() ?? 0,
            pricedExpansionWorkers = paidExpansionYears / years,
            unpricedExpansionWorkersEstimate = spareExpansion,
            d.FoodValue, d.GrowthFoodValue, d.GapConsumptionPerYear, d.GapSecurityPerYear, d.GapTotalPerYear,
            claims = d.Claims.Where(x => x.Good == CraftGood.AnimalFabric || x.Good == CraftGood.LightHousing || x.Good == CraftGood.Cloth)
                .Select(x => new { good = x.Good.ToString(), x.Quantity, x.Need, x.Stock, x.FirstUnitValue, x.LabourPerUnit }),
            fields = footprint.Select(id =>
            {
                var tile = w.Tiles[id]; var view = LazyEcology.ProjectEcology(w, id, w.Days);
                var cap = new EcologyCapacity(w, tile, view, w.Season);
                return new { tile = id, own = tile.CultivationOperator == c.Id, q = CultivationEconomy.Intensity(w.Config, view.CultivationCapital),
                    moisture = tile.Seasons.Average(s => s.Moisture), cropSuitability = tile.Seasons.Average(s => EcologyMath.CropSuitability(w.Config, s.Moisture)),
                    biomass = cap.Biomass, wildCapacity = cap.WildFood, cropCapacity = cap.Food - cap.WildFood, view.Food };
            }), choices
        };
    }

    private static void Snapshot(WorldState w, string directory, int t)
    {
        string before = SaveCodec.Fingerprint(w);
        var camps = w.Camps.Where(c => c.Technology == Technology.AnimalHusbandry && c.Population > 0).OrderBy(c => c.Id).ToArray();
        using (var output = new StreamWriter(Path.Combine(directory, "census-" + t + ".jsonl")))
            foreach (var c in camps) output.WriteLine(JsonSerializer.Serialize(Inspect(w, c, t, "actual"), Json));
        // Diverse disclosed cases; counterfactuals alter one camp's endowment,
        // not production defaults, and are never resolved into the live timeline.
        var selected = camps.Take(3).Concat(camps.OrderByDescending(c => c.Population).Take(2))
            .Concat(camps.OrderBy(c => w.Tiles[c.Tile].Seasons.Average(s => s.Moisture)).Take(2)).Distinct().ToArray();
        using (var output = new StreamWriter(Path.Combine(directory, "counterfactuals-" + t + ".jsonl")))
        foreach (var c in selected)
        {
            output.WriteLine(JsonSerializer.Serialize(Inspect(w, c, t, "actual"), Json));
            double plant = c.PlantFood, animal = c.AnimalFood, aquatic = c.AquaticFood;
            try
            {
                c.PlantFood = c.AnimalFood = c.AquaticFood = 0;
                output.WriteLine(JsonSerializer.Serialize(Inspect(w, c, t, "zero food inventory"), Json));
            }
            finally { c.PlantFood = plant; c.AnimalFood = animal; c.AquaticFood = aquatic; }
            bool cultivation = c.Technologies[(int)Technology.Cultivation];
            var ownFields = w.Map.Radius(c.Tile, 1).Select(id => w.Tiles[id]).Where(tile => tile.CultivationOperator == c.Id).ToArray();
            var capitals = ownFields.Select(tile => tile.CultivationCapital).ToArray();
            var intensities = ownFields.Select(tile => tile.Cultivation).ToArray();
            var operators = ownFields.Select(tile => tile.CultivationOperator).ToArray();
            var operations = ownFields.Select(tile => tile.CultivationOperatingFraction).ToArray();
            var stocks = ownFields.Select(tile => tile.Food).ToArray();
            try
            {
                c.Technologies[(int)Technology.Cultivation] = false;
                for (int i = 0; i < ownFields.Length; i++)
                {
                    var tile = ownFields[i]; tile.CultivationCapital = 0; tile.Cultivation = 0;
                    tile.CultivationOperator = -1; tile.CultivationOperatingFraction = 0;
                    tile.Food = Math.Min(tile.Food, new EcologyCapacity(w, tile, new EcologyView(tile), w.Season).Food);
                }
                output.WriteLine(JsonSerializer.Serialize(Inspect(w, c, t, "pastoral-only, own fields removed"), Json));
            }
            finally
            {
                c.Technologies[(int)Technology.Cultivation] = cultivation;
                for (int i = 0; i < ownFields.Length; i++)
                {
                    var tile = ownFields[i]; tile.CultivationCapital = capitals[i]; tile.Cultivation = intensities[i];
                    tile.CultivationOperator = operators[i]; tile.CultivationOperatingFraction = operations[i]; tile.Food = stocks[i];
                }
            }
            var herds = w.Herds.Where(h => h.DomesticCamp == c.Id && h.Animals > 0).OrderBy(h => h.Id).ToArray();
            if (herds.Length == 0) continue;
            var counts = herds.Select(h => h.Animals).ToArray();
            try
            {
                int total = Math.Max(200, c.Population), left = total;
                for (int i = 0; i < herds.Length; i++)
                {
                    herds[i].Animals = i == herds.Length - 1 ? left : total / herds.Length;
                    left -= herds[i].Animals;
                }
                output.WriteLine(JsonSerializer.Serialize(Inspect(w, c, t, "hypothetical herd endowment"), Json));
            }
            finally { for (int i = 0; i < herds.Length; i++) herds[i].Animals = counts[i]; }
        }
        if (SaveCodec.Fingerprint(w) != before) throw new Exception("Diagnostic counterfactuals changed authoritative state.");
        Console.WriteLine("snapshot " + t + " camps=" + camps.Length + " purity=verified");
    }
}
