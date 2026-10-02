using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Anthropocene.Simulation;

internal static class CropAgricultureChecks
{
    private static int checks;
    private static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception("CropAgriculture: " + message); }
    private static void Near(double actual, double expected, string message, double tolerance = 1e-7)
        => Check(Math.Abs(actual - expected) <= tolerance * Math.Max(1, Math.Abs(expected)), message + " actual=" + actual + " expected=" + expected);

    private static WorldState Fields(double q, float moisture = .75f)
    {
        var w = NeolithicChecks.Fixture(); var c = w.Camps[0]; c.Population = 404;
        foreach (int id in CultivationEconomy.Footprint(w, c))
        {
            var t = w.Tiles[id]; t.CultivationCapital = Math.Pow(q / .4, 2); t.CultivationOperator = c.Id;
            t.CultivationOperatingFraction = 1;
            for (int s = 0; s < 4; s++)
            {
                t.Seasons[s].Moisture = moisture; t.Seasons[s].MaxFood = 10000; t.Seasons[s].MaxPotentialFood = 100000;
            }
            var cap = new EcologyCapacity(w, t, new EcologyView(t), 0);
            t.Food = .9 * cap.Food; t.PotentialFood = 90000;
        }
        return w;
    }

    public static void Run()
    {
        checks = 0;
        var cfg = SimulationConfig.Small(1);
        foreach (double m in new[] { 0.0, .03, .1, .22, .5, .85, 1.0 })
        foreach (double q in new[] { 0.0, .000001, .2, .4, .7, 1.0 })
        {
            double food = EcologyMath.EffectiveWildFoodCapacity(100, 1000, q, cfg, m);
            Check(food >= 0 && food <= 1000, "crop capacity is bounded by biomass");
            if (q == 0) Near(food, 100, "natural food ignores crop suitability");
            if (m == 0) Near(food, EcologyMath.RetainedWildFoodCapacity(100, q, cfg), "zero moisture adds no crops");
            if (m >= .5) Near(food, EcologyMath.EffectiveWildFoodCapacity(100, 1000, q, cfg, 1), "wet capacity benchmark");
            double renewal = EcologyMath.ManagedRegenerationFlow(cfg, q, 1000, .001, 1, m);
            if (m == 0) Near(renewal, 0, "zero moisture adds no managed renewal");
        }
        Near(EcologyMath.CropSuitability(cfg, .22), .1936, "grassland crop response");
        cfg.CropMoistureReference = 0;
        Near(EcologyMath.EffectiveWildFoodCapacity(100, 1000, .4, cfg, 0), 244, "legacy-neutral crop capacity");
        foreach (double invalid in new[] { -.1, 1.1, double.NaN, double.PositiveInfinity })
        {
            var bad = SimulationConfig.Small(1); bad.CropMoistureReference = invalid;
            bool rejected = false; try { bad.Validate(); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "invalid moisture reference is rejected");
        }

        foreach (double q in new[] { 0.0, .000001, .4 })
        {
            var w = Fields(q); var c = w.Camps[0]; var footprint = CultivationEconomy.Footprint(w, c);
            string before = SaveCodec.Fingerprint(w);
            double factor = CultivationEconomy.HarvestProductivity(w, c, footprint);
            if (q < .00001) Near(factor, 1, "vanishing field gives vanishing harvest benefit", 1e-9);
            else Near(factor, 24400.0 / (10000 + 14400 / 1.5), "crop and wild harvest pay distinct work rates");
            var labor = new AgriculturalLabor { OperatingFarmers = 100, Footprint = footprint };
            var receipt = FoodHorizonForecast.Settlement(w, c, c.Tile, 0, w.Config.TurnDays, labor, 1e9, 0);
            Near(receipt.TerrestrialFood, 100 * 7.2 * 20 * factor, "forecast uses mixed harvest labour");
            Check(before == SaveCodec.Fingerprint(w), "crop labour and forecast remain pure");
        }
        var dry = Fields(.4, .22f); var wet = Fields(.4);
        var dc = new EcologyCapacity(dry, dry.Tiles[dry.Camps[0].Tile], new EcologyView(dry.Tiles[dry.Camps[0].Tile]), 0);
        var wc = new EcologyCapacity(wet, wet.Tiles[wet.Camps[0].Tile], new EcologyView(wet.Tiles[wet.Camps[0].Tile]), 0);
        Near(dc.Biomass, wc.Biomass, "crop penalty does not remove grazing biomass");
        Check(dc.Food < wc.Food && dc.ManagedFlow < wc.ManagedFlow, "both crop access and renewal respond to moisture");

        var tools = Fields(.5, .22f); var tc = tools.Camps[0]; tc.Inventory.FarmTools = 0;
        double required = CultivationEconomy.MaintenanceWorkerRequirement(tools, tc);
        Near(CultivationEconomy.PlannedMaintenanceFarmers(tools, tc, 404), required, "zero tools provide no prospective efficiency");
        tc.Inventory.FarmTools = required / CultivationEconomy.FarmToolFieldWorkEfficiency(tools.Config.Neolithic);
        Near(CultivationEconomy.PlannedMaintenanceFarmers(tools, tc, 404), required / 1.265625, "starting tools cover maintenance");

        var committed = Fields(.5); var hc = committed.Camps[0]; hc.PlantFood = 100000;
        committed.Config.HerdLabourEnabled = true; hc.Technologies[(int)Technology.AnimalHusbandry] = true;
        hc.Inventory.HerdingTools = 10;
        committed.Herds.Add(new Herd { Id = committed.NextId++, Tile = hc.Tile, DomesticCamp = hc.Id, OwnerSociety = hc.SocietyId, Animals = 22, Species = "goat", Capacity = 1 });
        var hd = CampProductionDemand.Build(committed, hc, EconomicIntent.From(hc));
        Near(hd.Biological, 0, "stored food already funds the incoming biological budget");
        Check(hd.HerdLadder != null && hd.HerdLadder.SavedHarvestYears.All(y => Math.Abs(y) < 1e-8),
            "animal food does not free workers below committed field maintenance");

        var saved = SaveCodec.Save(dry); var loaded = SaveCodec.Load(saved);
        Near(loaded.Config.CropMoistureReference, .5, "save retains crop reference");
        Check(loaded.Config.CropHarvestingOnly && SaveCodec.Fingerprint(dry) == SaveCodec.Fingerprint(loaded), "save retains harvest policy and state");
        using (var output = new MemoryStream())
        {
            using (var input = new MemoryStream(saved))
            using (var unzip = new GZipStream(input, CompressionMode.Decompress))
            using (var reader = new BinaryReader(unzip))
            using (var zip = new GZipStream(output, CompressionMode.Compress, true))
            using (var writer = new BinaryWriter(zip))
            {
                writer.Write(reader.ReadString()); int length = reader.ReadInt32();
                using (var xml = XmlDictionaryReader.CreateBinaryReader(reader.ReadBytes(length), XmlDictionaryReaderQuotas.Max))
                using (var metadata = new MemoryStream())
                {
                    var document = XDocument.Load(xml);
                    document.Descendants().Where(e => e.Name.LocalName == "CropMoistureReference" || e.Name.LocalName == "CropMoistureExponent" || e.Name.LocalName == "CropHarvestingOnly").Remove();
                    using (var binary = XmlDictionaryWriter.CreateBinaryWriter(metadata, null, null, false)) document.WriteTo(binary);
                    writer.Write((int)metadata.Length); writer.Write(metadata.ToArray());
                }
                unzip.CopyTo(zip);
            }
            var legacy = SaveCodec.Load(output.ToArray());
            Near(legacy.Config.CropMoistureReference, 0, "absent crop response loads neutral");
            Check(!legacy.Config.CropHarvestingOnly, "absent harvest policy preserves the old rule");
        }
        Console.WriteLine("PASS: " + checks + " crop agriculture assertions");
    }
}
