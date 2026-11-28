using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Xml.Linq;
using Anthropocene.Simulation;

internal static class AnnualLandFoodChecks
{
    private static int checks;
    private static void Check(bool ok, string message)
    { checks++; if (!ok) throw new Exception("Annual land food: " + message); }
    private static void Near(double a, double b, string message, double tolerance = 1e-8)
        => Check(Math.Abs(a - b) <= tolerance * Math.Max(1, Math.Max(Math.Abs(a), Math.Abs(b))),
            message + " actual=" + a + " expected=" + b);

    private static WorldState Fixture(double q = .5)
    {
        var w = NeolithicChecks.Fixture();
        w.Config.DemographicModel = DemographicModel.PhysicalMortalityV1;
        w.Config.AnnualLandFoodEnabled = true;
        var t = w.Tiles[w.Camps[0].Tile];
        t.CultivationCapital = Math.Pow(q / w.Config.ReferenceIntensity, 2);
        t.CultivationOperatingFraction = 1;
        t.Food = 20; t.PotentialFood = 180;
        for (int s = 0; s < 4; s++)
        {
            t.Seasons[s].MaxFood = 50 + 10 * s;
            t.Seasons[s].MaxPotentialFood = 500 + 100 * s;
            t.Seasons[s].FoodRecoveryPerDay = (float)((.08 + .02 * s) / 365);
            t.Seasons[s].BiomassRecoveryPerDay = (float)((.04 + .01 * s) / 365);
        }
        return w;
    }
    private static void Balance(TileEcologyResult r)
    {
        var b = r.Balance;
        Near(b.StartingFood + b.FoodRecharge, b.EndingFood + b.FoodHarvest + b.FoodOverflow, "food conservation");
        Near(b.StartingBiomass + b.BiomassRecharge, b.EndingBiomass + b.BiomassHarvest + b.BiomassOverflow, "biomass conservation");
        Near(b.StartingWater + b.WaterRecharge, b.EndingWater + b.WaterHarvest + b.WaterOverflow, "water conservation");
        Check(r.View.Food >= 0 && r.View.PotentialFood >= r.View.Food, "finite nonnegative reserve");
    }
    private static void ScalarReference()
    {
        foreach (double demand in new[] { 0.0, 2.0, 8.0, 50.0 })
        foreach (double years in new[] { .1, 1, 20, 100 })
        {
            var result = EcologyMath.RenewableHarvest(25, 100, .1, 3, demand, years);
            // Independent small-step reference used only in tests: grow the
            // natural reserve, spend crop yield, then debit any residual need.
            double stock = 25, harvest = 0, step = years / 100000;
            for (int i = 0; i < 100000; i++)
            {
                stock += .1 * (100 - stock) * step;
                double crop = Math.Min(demand, 3) * step;
                double debit = Math.Min(stock, demand * step - crop);
                stock -= debit; harvest += crop + debit;
            }
            Near(result.Stock, stock, "independent reference stock", 2e-4);
            Near(result.Realized, harvest, "independent reference harvest", 2e-4);
            Near(25 + result.Recharge, result.Stock + result.Realized + result.Overflow, "scalar conservation");
            double splitStock = 25, splitHarvest = 0;
            for (int i = 0; i < 20; i++)
            {
                var part = EcologyMath.RenewableHarvest(splitStock, 100, .1, 3, demand, years / 20);
                splitStock = part.Stock; splitHarvest += part.Realized;
            }
            Near(result.Stock, splitStock, "partition reserve");
            Near(result.Realized, splitHarvest, "partition harvest");
        }
        var quiet = EcologyMath.RenewableHarvest(25, 100, .1, 1000, 0, 1);
        Near(quiet.Stock, EcologyMath.Stock(25, 100, .1, 0, 1).Stock, "unused crop never restores reserve");
        var supported = EcologyMath.RenewableHarvest(100, 100, .1, 3, 2, 20);
        Near(supported.Stock, 100, "crop-supported population leaves reserve intact");
        Near(supported.Realized, 40, "renewable food meets annual demand");
        var depleted = EcologyMath.RenewableHarvest(25, 100, .1, 3, 50, 20);
        Near(depleted.Stock, 0, "overdraw exhausts reserve during horizon");
        Check(depleted.Unmet > 0, "exhaustion limits same-horizon food");
    }
    private static void Kernel()
    {
        foreach (double q in new[] { 0.0, .5, 1.0 })
        foreach (double humans in new[] { 0.0, 10, 150 })
        foreach (double herds in new[] { 0.0, 100 })
        {
            var w = Fixture(q); int id = w.Camps[0].Tile; var tile = w.Tiles[id];
            string fingerprint = SaveCodec.Fingerprint(w);
            Demand[] Claims(double years) => new[] {
                new Demand { Tile = id, Entity = 1, Kind = "Cultivation", Food = humans * years },
                new Demand { Tile = id, Entity = 2, Kind = "Herd", PotentialFood = herds * years }
            };
            var whole = EcologicalPressure.Integrate(w, id, Claims(20), 0, 7300, projectWood: false);
            Balance(whole);
            var phase = EcologicalPressure.Integrate(w, id, Claims(20), 0, 7300,
                projectWood: false, openingEcology: new EcologyView(tile));
            Near(phase.View.Food, whole.View.Food, "repeatability");
            var forecast = FoodHorizonForecast.Tile(w, id, 0, 7300, Claims(20));
            Near(forecast.HumanFoodReceipts, whole.Transfers.Sum(t => t.Food), "forecast receipts");
            Check(fingerprint == SaveCodec.Fingerprint(w), "pure forecast and integration");
            var view = new EcologyView(tile); double food = 0, biomass = 0;
            for (int i = 0; i < 20; i++)
            {
                var part = EcologicalPressure.Integrate(w, id, Claims(1), i * 365, 365,
                    projectWood: false, openingEcology: view);
                Balance(part); view = part.View;
                food += part.Transfers.Sum(t => t.Food);
                biomass += part.Transfers.Sum(t => t.PotentialFood);
            }
            Near(view.Food, whole.View.Food, "20 annual harvests equal one 20-year harvest: reserve");
            Near(view.PotentialFood, whole.View.PotentialFood, "partition total biomass");
            Near(food, whole.Transfers.Sum(t => t.Food), "partition human food");
            Near(biomass, whole.Transfers.Sum(t => t.PotentialFood), "partition grazing");
            Check(food <= humans * 20 + 1e-7 && biomass <= herds * 20 + 1e-7, "finite claims bound receipts");
            if (humans == 0 && herds == 0)
            {
                var passive = LazyEcology.ProjectEcology(w, id, 7300);
                Near(passive.Food, whole.View.Food, "passive and active zero-demand agreement");
                Near(passive.PotentialFood, whole.View.PotentialFood, "passive biomass agreement");
            }
        }
        var growth = Fixture(.2); int target = growth.Camps[0].Tile;
        var change = EcologicalPressure.Integrate(growth, target,
            new[] { new Demand { Tile = target, Entity = 1, Kind = "Cultivation", Food = 500 } },
            0, 7300, 2, 4, .5, projectWood: false);
        Balance(change);
        Near(change.View.CultivationCapital, 4, "commit ending capital");
        var opportunity = FoodHorizonForecast.Investment(growth, target, 0, 7300, .25, 2, 2, 1);
        Check(opportunity.AnnualizedRetainedFoodGain > 0, "investment includes recurring cultivated supply");
        growth.Config.Agriculture = new AgriculturalTechnologySettings { IrrigationEnabled = true };
        growth.Config.CropMoistureReference = .5;
        for (int s = 0; s < 4; s++) growth.Tiles[target].Seasons[s].Moisture = .15f;
        var dry = FoodHorizonForecast.Tile(growth, target, 0, 7300, operatingFraction: 1,
            irrigationOverlay: new IrrigationOverlay(0, 0, 1));
        var wet = FoodHorizonForecast.Tile(growth, target, 0, 7300, operatingFraction: 1,
            irrigationOverlay: new IrrigationOverlay(1, 0, 1));
        Check(wet.GrossManagedFoodInput > dry.GrossManagedFoodInput, "delivered irrigation raises annual crop flow");
        Check(FoodHorizonForecast.InvestmentSupply(growth, target, wet) >
            FoodHorizonForecast.InvestmentSupply(growth, target, dry), "irrigation planner values crop flow");
        var idle = FoodHorizonForecast.Tile(growth, target, 0, 7300, operatingFraction: 0);
        Near(idle.GrossManagedFoodInput, 0, "unoperated fields supply no managed renewal");
        // The allocation-free quiet specialization must also match declining
        // capital and partial operation, not just maintained fields.
        foreach (double operation in new[] { 0.0, .3, 1.0 })
        {
            growth.Tiles[target].CultivationOperatingFraction = operation;
            var quiet = LazyEcology.ProjectEcology(growth, target, 7300);
            var active = EcologicalPressure.Integrate(growth, target, Array.Empty<Demand>(),
                0, 7300, projectWood: false);
            Near(quiet.Food, active.View.Food, "quiet specialization with field decay");
            Near(quiet.PotentialFood, active.View.PotentialFood, "quiet biomass with field decay");
            Near(quiet.CultivationCapital, active.View.CultivationCapital, "quiet capital with field decay");
        }
        var contested = Enumerable.Range(1, 100).Select(i => new Demand {
            Tile = target, Entity = i, Kind = "Gathering", Food = 1000 }).ToArray();
        var ordered = EcologicalPressure.Integrate(growth, target, contested, 0, 7300, projectWood: false);
        var reversed = EcologicalPressure.Integrate(growth, target, contested.Reverse(), 0, 7300, projectWood: false);
        Balance(ordered); Balance(reversed);
        for (int i = 0; i < ordered.Transfers.Count; i++)
            Near(ordered.Transfers[i].Food, reversed.Transfers[i].Food, "canonical contested allocation");
    }
    private static void PopulationAndPersistence()
    {
        var w = Fixture(); var cfg = new SimulationConfig { AnnualLandFoodEnabled = true };
        cfg.SurplusFundedBirths = false; cfg.Validate();
        double rate = PhysicalDemography.FoodPerPersonYear(cfg);
        var result = PhysicalDemography.AdvanceBudget(cfg, 100, 20, 1e10, 1e10, 1e10,
            crowdingMitigation: 0, diseaseMitigation: 0);
        double netRate = cfg.BaseBirthRate - cfg.ExposureMortalityRate *
            (1 - cfg.ExposureClothingReduction - cfg.ExposureHousingReduction);
        double expectedYears = 100 * (Math.Exp(netRate * 20) - 1) / netRate;
        Near(result.PersonYears, expectedYears, "growing population integrated annually", 1e-7);
        Near(result.FoodConsumed, expectedYears * rate, "food debit follows population-years", 1e-7);
        Check(result.FoodConsumed > 100 * 20 * rate, "new residents eat before next turn");
        var loaded = SaveCodec.Load(SaveCodec.Save(w));
        Check(loaded.Config.AnnualLandFoodEnabled, "mode survives native save");
        // Operation is a transient funded-plan input, not saved tile state.
        Near(EcologicalPressure.Integrate(loaded, w.Camps[0].Tile, Array.Empty<Demand>(),
                0, 7300, operatingFraction: 1, projectWood: false).View.Food,
            EcologicalPressure.Integrate(w, w.Camps[0].Tile, Array.Empty<Demand>(),
                0, 7300, operatingFraction: 1, projectWood: false).View.Food, "save continuation ecology");
        var serializer = new DataContractSerializer(typeof(SimulationConfig));
        using var stream = new MemoryStream(); serializer.WriteObject(stream, w.Config); stream.Position = 0;
        var xml = XDocument.Load(stream);
        xml.Descendants().Where(e => e.Name.LocalName == "AnnualLandFoodEnabled").Remove();
        using var reader = xml.CreateReader();
        Check(!((SimulationConfig)serializer.ReadObject(reader)).AnnualLandFoodEnabled, "absent legacy flag stays disabled");
    }
    private static void Scaling()
    {
        var w = Fixture(); int id = w.Camps[0].Tile;
        foreach (double years in new[] { 1.0 / 365, 1, 20, 600 })
        {
            var claims = new[] { new Demand { Tile = id, Entity = 1, Kind = "Cultivation", Food = 50 * years } };
            var preview = FoodHorizonForecast.Tile(w, id, 0, years * 365, claims, countRecoveryProfiles: true);
            Check(preview.RecoveryProfileEvaluations == 4, "four main profiles independent of duration");
            var timer = Stopwatch.StartNew(); long allocated = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 5000; i++)
                EcologicalPressure.Integrate(w, id, claims, 0, years * 365, projectWood: false);
            Console.WriteLine($"annual kernel years={years:G6} calls=5000 ms={timer.Elapsed.TotalMilliseconds:F1} bytes={GC.GetAllocatedBytesForCurrentThread() - allocated}");
            LazyEcology.ProjectEcology(w, id, years * 365);
            allocated = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) LazyEcology.ProjectEcology(w, id, years * 365);
            Check(GC.GetAllocatedBytesForCurrentThread() == allocated, "quiet annual projection allocates zero bytes");
        }
    }
    private static void GeneratedReplay()
    {
        var cfg = JsonSerializer.Deserialize<SimulationConfig>(File.ReadAllText(
            "Assets/_Anthropocene/Resources/AnnualLandFoodOpeningConfig.json"),
            new JsonSerializerOptions { IncludeFields = true });
        cfg.Width = 96; cfg.Height = 48; cfg.Players = 4; cfg.Validate();
        var w = new WorldGenerator().Generate(11, cfg);
        var replica = SaveCodec.Load(SaveCodec.Save(w));
        for (int turn = 0; turn < 20; turn++)
        {
            var ai = AiPlanning.Decide(w); var other = AiPlanning.Decide(replica);
            var resolver = new TurnResolver(new PhaseExecution(1));
            resolver.Resolve(w, TurnPlanner.Prepare(w, ai.Orders, ai.Traces));
            new TurnResolver(new PhaseExecution(4)).Resolve(replica,
                TurnPlanner.Prepare(replica, other.Orders, other.Traces));
            Check(SaveCodec.Fingerprint(w) == SaveCodec.Fingerprint(replica), "generated opening scalar/worker/save replay");
            Check(resolver.LastMetrics.TileIntegrations == resolver.LastMetrics.ActiveTiles,
                "one integration per active tile");
        }
        TurnResolver.Invariants(w);
    }
    public static void Run()
    {
        ScalarReference(); Kernel(); PopulationAndPersistence(); Scaling(); GeneratedReplay();
        Console.WriteLine("PASS: " + checks + " annual land food assertions");
    }
}
