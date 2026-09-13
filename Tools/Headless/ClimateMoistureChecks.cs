using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Anthropocene.Simulation;

internal static class ClimateMoistureChecks
{
    private static int checks;
    private static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception("Climate moisture: " + message); }
    // Exercise the production stages with identical forcing; only the boundary river differs.
    private static Tile Profile(float temperature, float rain, float riverFlow = 0, bool lake = false)
    {
        var cfg = SimulationConfig.Small(1);
        var tile = new Tile { Environment = lake ? EnvironmentKind.Lake : EnvironmentKind.Grassland, WaterBody = lake ? WaterKind.Freshwater : WaterKind.None, ElevationLevel = 1 };
        for (int s = 0; s < 4; s++) tile.Seasons[s] = new SeasonData { Temperature = temperature, Rain = rain };
        var w = new WorldState { Config = cfg, Tiles = new[] { tile } };
        if (riverFlow > 0) { tile.RiverEdges = new[] { 0, -1, -1, -1, -1, -1 }; w.Rivers.Add(new RiverEdge { Id = 0, Flow = riverFlow }); }
        var context = new GenerationContext { World = w, Map = w.Map };
        new EcologyStage().Generate(context); new VegetationResourceStage().Generate(context);
        return tile;
    }
    public static void Run()
    {
        checks = 0;
        var dry = Profile(30, .005f); var nile = Profile(30, .005f, 280);
        Check(dry.Environment == EnvironmentKind.Desert && dry.NaturalVegetationPotential == VegetationTier.Low, "hot arid dry tile is Desert + Low");
        Check(nile.Environment == EnvironmentKind.Desert && nile.NaturalVegetationPotential == VegetationTier.Medium && nile.CurrentVegetation == VegetationTier.Medium, "major arid river is Desert + Medium");
        Check(nile.Seasons.Select(s => s.RegionalMoisture).SequenceEqual(dry.Seasons.Select(s => s.RegionalMoisture)), "local river cannot alter identical regional climate");
        Check(nile.Seasons.All(s => s.Moisture > .8) && dry.Seasons.All(s => s.Moisture < .01), "Nile soil stays wet beside dry desert");
        Check(nile.Seasons[0].MaxFood > 5 * dry.Seasons[0].MaxFood && nile.Seasons[0].FoodRecoveryPerDay > 4 * dry.Seasons[0].FoodRecoveryPerDay && nile.Seasons[0].MaxPotentialFood > dry.Seasons[0].MaxPotentialFood && nile.Seasons[0].Habitat > 5 * dry.Seasons[0].Habitat, "river retains much higher food capacity, renewal, biomass and habitat");
        Check(nile.DrinkableWater >= 60 && dry.DrinkableWater == 0, "strong river supplies freshwater absent from dry desert");
        Check(nile.Terrain == Terrain.Desert && Vegetation.Label(nile.Environment, nile.CurrentVegetation, nile.River) == "Riparian vegetation", "Nile labels cannot become jungle/rainforest");
        var tropical = Profile(30, .1f, 280); var temperate = Profile(15, .1f, 280);
        Check(tropical.Environment == EnvironmentKind.Tropical && tropical.CurrentVegetation == VegetationTier.High && Vegetation.Label(tropical.Environment, tropical.CurrentVegetation, true) == "Rainforest", "humid tropical river supports rainforest");
        Check(temperate.Environment == EnvironmentKind.Temperate && temperate.CurrentVegetation == VegetationTier.High && Vegetation.Label(temperate.Environment, temperate.CurrentVegetation, true) == "Forest", "wet temperate river supports forest");
        var cold = Profile(0, .005f, 280); var arctic = Profile(-9, .005f, 280); var taiga = Profile(0, .1f, 280);
        Check(cold.Environment == EnvironmentKind.Cold && cold.CurrentVegetation != VegetationTier.High && arctic.Environment == EnvironmentKind.Cold && arctic.CurrentVegetation == VegetationTier.Low, "cold river does not bypass climatic warmth/support");
        Check(taiga.Environment == EnvironmentKind.Cold && taiga.CurrentVegetation == VegetationTier.High && Vegetation.Label(taiga.Environment, taiga.CurrentVegetation) == "Taiga", "regionally supported cold forest remains possible");
        var savanna = Profile(30, .03f, 280); var grass = Profile(15, .025f, 280);
        Check(savanna.Environment == EnvironmentKind.Grassland && savanna.CurrentVegetation == VegetationTier.Medium && grass.Environment == EnvironmentKind.Grassland && grass.CurrentVegetation == VegetationTier.Medium, "dry savanna and grassland rivers stay Medium");
        var weak = Profile(30, .005f, .28f);
        Check(weak.River && weak.CurrentVegetation == VegetationTier.Low, "a weak river flag does not guarantee vegetation uplift");
        var cfg = SimulationConfig.Small(1); float savedWater = nile.Seasons[0].MaxFreshwater;
        for (int s = 0; s < 4; s++) nile.Seasons[s].MaxFreshwater = 0;
        Check(Vegetation.Potential(nile, cfg) == VegetationTier.Low, "wet soil without available freshwater cannot supply hydrologic uplift");
        for (int s = 0; s < 4; s++) nile.Seasons[s].MaxFreshwater = savedWater;
        var lake = Profile(30, .005f, lake: true);
        Check(lake.Environment == EnvironmentKind.Lake && lake.Seasons.All(s => s.RegionalMoisture < .01 && s.Moisture > .8) && lake.DrinkableWater > 0, "lake preserves aquatic identity and distinct local water");

        var oasisConfig = SimulationConfig.Small(1); oasisConfig.Width = 24; oasisConfig.Height = 16;
        var oasisWorld = new WorldState { Seed = 3, Config = oasisConfig, Tiles = Enumerable.Range(0, 384).Select(i => new Tile { Id = i, Environment = EnvironmentKind.Desert, ElevationLevel = 1, Flow = 3, Seasons = Enumerable.Range(0, 4).Select(_ => new SeasonData { Temperature = 30, Rain = .005f }).ToArray() }).ToArray() };
        var oasisContext = new GenerationContext { World = oasisWorld, Map = oasisWorld.Map };
        new EcologyStage().Generate(oasisContext); new OasisStage().Generate(oasisContext); new VegetationResourceStage().Generate(oasisContext);
        var oasis = oasisWorld.Tiles.First(t => t.Oasis);
        Check(oasis.CurrentVegetation == VegetationTier.Medium && oasis.FreshwaterSource == FreshwaterSource.Oasis && oasis.DrinkableWater > 0, "finite oasis water supports Medium vegetation");
        Check(oasisWorld.Tiles.All(t => t.Environment == EnvironmentKind.Desert && t.Seasons.All(s => s.RegionalMoisture == 0)), "oasis stage never changes regional moisture or arid classification");
        Check(oasis.Seasons.All(s => s.Moisture >= .449 && s.MaxFood > dry.Seasons[0].MaxFood && s.Habitat > dry.Seasons[0].Habitat), "oasis subsidy feeds actual soil, productivity and habitat");
        Check(oasisWorld.Tiles.Where(t => t.OasisInfluence > 0 && !t.Oasis).All(t => t.CurrentVegetation == VegetationTier.Low), "weak oasis margins do not guarantee a tree tier");

        var w = new WorldGenerator().Generate(1729, SimulationConfig.Small(4));
        var twin = new WorldGenerator().Generate(1729, SimulationConfig.Small(4));
        Check(SaveCodec.Fingerprint(w) == SaveCodec.Fingerprint(twin), "both moisture profiles participate in deterministic generation");
        Check(w.Tiles.Where(t => t.Land).All(t => t.Environment == Vegetation.Classify(t.Seasons.Average(s => s.Temperature), t.Seasons.Average(s => s.RegionalMoisture), w.Config)), "generated land uses regional classification after oasis placement");
        Check(w.Tiles.Where(t => t.Environment == EnvironmentKind.Desert).All(t => t.NaturalVegetationPotential != VegetationTier.High), "generated dry climates never have natural High vegetation");
        var bytes = SaveCodec.Save(w); var restored = SaveCodec.Load(bytes);
        Check(SaveCodec.Fingerprint(w) == SaveCodec.Fingerprint(restored) && w.Tiles.SelectMany(t => t.Seasons).Select(s => s.RegionalMoisture).SequenceEqual(restored.Tiles.SelectMany(t => t.Seasons).Select(s => s.RegionalMoisture)), "save round-trip preserves exact regional profiles");
        string fingerprint = SaveCodec.Fingerprint(restored); restored.Tiles[0].Seasons[0].RegionalMoisture += .01f;
        Check(fingerprint != SaveCodec.Fingerprint(restored), "fingerprint detects a regional-only state change");
        using (var old = new MemoryStream())
        {
            using (var zip = new GZipStream(old, CompressionLevel.Fastest, true))
            using (var writer = new BinaryWriter(zip)) writer.Write("ANTHROPOCENE-7");
            bool rejected = false;
            try { SaveCodec.Load(old.ToArray()); } catch (InvalidDataException e) { rejected = e.Message.Contains("regional and local moisture"); }
            Check(rejected, "prior save signature is rejected explicitly before any old payload is interpreted");
        }
        Console.WriteLine("PASS: " + checks + " regional/local moisture assertions");
        Console.WriteLine("NILE dry/river regional=" + dry.Seasons[0].RegionalMoisture + "/" + nile.Seasons[0].RegionalMoisture + " soil=" + dry.Seasons[0].Moisture + "/" + nile.Seasons[0].Moisture + " foodCapacity=" + dry.Seasons[0].MaxFood + "/" + nile.Seasons[0].MaxFood + " freshwaterCapacity=" + dry.Seasons[0].MaxFreshwater + "/" + nile.Seasons[0].MaxFreshwater);
    }
}
