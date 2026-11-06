using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Anthropocene.Simulation;

internal static class AltitudeClimateChecks
{
    private static int checks;
    private static void Check(bool condition, string message)
    { checks++; if (!condition) throw new Exception("Altitude climate: " + message); }
    private static bool Near(double a, double b, double tolerance = 1e-5) => Math.Abs(a - b) < tolerance;

    public static void Run()
    {
        checks = 0;
        var config = SimulationConfig.Small(1);
        Check(config.ElevationMetersPerUnit == 12000 && config.LapseRateCPerKm == 6.5, "new-world defaults carry explicit physical units");
        Check(Near(AltitudeClimate.Meters(config, config.SeaLevel), 0), "sea level is the altitude datum");
        Check(Near(AltitudeClimate.Meters(config, config.SeaLevel + .1), 1200), "normalized heights have a fixed metre conversion");
        Check(Near(AltitudeClimate.Cooling(config, config.SeaLevel + 1000.0 / 12000), 6.5), "1000 metres cools by 6.5 C");
        Check(AltitudeClimate.Cooling(config, config.SeaLevel - .2) == 0, "ocean depth does not warm surface air");
        foreach (double invalid in new[] { -1.0, double.NaN, double.PositiveInfinity })
        {
            var bad = SimulationConfig.Small(1); bad.ElevationMetersPerUnit = invalid;
            bool threw = false; try { bad.Validate(); } catch (ArgumentException) { threw = true; }
            Check(threw, "invalid physical height scale rejected");
            bad = SimulationConfig.Small(1); bad.LapseRateCPerKm = invalid;
            threw = false; try { bad.Validate(); } catch (ArgumentException) { threw = true; }
            Check(threw, "invalid physical cooling rate rejected");
        }
        var world = new WorldState { Seed = 81, Config = config, Tiles = new Tile[config.Width * config.Height] };
        foreach (int id in Enumerable.Range(0, world.Tiles.Length))
            world.Tiles[id] = new Tile { Id = id, Terrain = id % config.Width < config.Width / 3 ? Terrain.Ocean : Terrain.Grassland,
                Elevation = (float)config.SeaLevel };
        int target = config.Width * (config.Height / 2) + config.Width / 2;
        var context = new GenerationContext { World = world, Map = world.Map };
        new ClimateStage().Generate(context);
        var seaProfiles = world.Tiles[target].Seasons.ToArray();
        foreach (double metres in new[] { 1000.0, 3000.0, 6000.0 })
        {
            world.Tiles[target].Elevation = (float)(config.SeaLevel + metres / config.ElevationMetersPerUnit);
            new ClimateStage().Generate(context);
            Check(world.Tiles[target].Seasons.Select((s, i) => Near(seaProfiles[i].Temperature - s.Temperature, metres * .0065)).All(x => x),
                "matched-latitude surface climate uses physical altitude in every season: " + metres);
        }
        Check(world.Tiles[target].Seasons.All(s => s.Temperature < 0 && s.Rain == 0 && s.Snow > 0), "humid high equatorial mountain receives snowfall");
        var before = world.Tiles[target].Seasons.ToArray();
        world.Tiles[target].Elevation -= 1000f / 12000;
        new SurfaceClimateStage().Generate(context);
        Check(world.Tiles[target].Seasons.Select((s, i) => Near(s.Temperature - before[i].Temperature, 6.5)).All(x => x), "hydrological lowering warms final profiles consistently");
        Check(world.Tiles[target].Seasons.Select((s, i) => s.Rain + s.Snow == before[i].Rain + before[i].Snow && s.Runoff == before[i].Runoff && s.WindX == before[i].WindX).All(x => x), "final-height correction conserves precipitation, runoff and wind");
        Check(context.ClimateSurfaceHeights == null, "temporary generation height snapshot is released");
        // Cross the freezing threshold after a hydrological height adjustment.
        new ClimateStage().Generate(context);
        for (int s = 0; s < 4; s++) world.Tiles[target].Seasons[s] = new SeasonData { Temperature = -2, Snow = .1f, Runoff = .1f };
        world.Tiles[target].Elevation -= 1000f / 12000;
        new SurfaceClimateStage().Generate(context);
        Check(world.Tiles[target].Seasons.All(s => s.Temperature > 0 && s.Rain == .1f && s.Snow == 0 && s.Runoff == .1f), "final-height correction repartitions snow to rain without losing water");
        config.ElevationMetersPerUnit = 0;
        Check(Near(AltitudeClimate.Cooling(config, config.SeaLevel + .1), 2.8), "legacy config retains original abstract lapse rate");
        var unchanged = world.Tiles[target].Seasons.ToArray();
        new SurfaceClimateStage().Generate(new GenerationContext { World = world, Map = world.Map });
        Check(unchanged.SequenceEqual(world.Tiles[target].Seasons), "legacy surface profiles are not corrected");
        SaveCompatibility();
        Console.WriteLine("PASS: " + checks + " altitude/climate assertions");
    }

    private static void SaveCompatibility()
    {
        var config = SimulationConfig.Small(2); config.ElevationMetersPerUnit = 0;
        var legacy = new WorldGenerator().Generate(92, config); legacy.Generation.GeneratorVersion = 14;
        var loaded = SaveCodec.Load(WithoutAltitudeFields(legacy));
        Check(loaded.Generation.GeneratorVersion == 14 && loaded.Config.ElevationMetersPerUnit == 0, "absent fields preserve legacy generation and altitude mode");
        Check(loaded.Config.LapseRateCPerKm == 6.5 && loaded.Config.LapseRate == legacy.Config.LapseRate, "legacy cooling defaults migrate without enabling physical mode");
        Check(loaded.Tiles.Select(t => t.Elevation).SequenceEqual(legacy.Tiles.Select(t => t.Elevation))
            && loaded.Tiles.SelectMany(t => t.Seasons).SequenceEqual(legacy.Tiles.SelectMany(t => t.Seasons)), "legacy save load preserves every height and seasonal profile exactly");
        Check(SaveCodec.Fingerprint(loaded) == SaveCodec.Fingerprint(legacy), "migrated legacy state matches explicit legacy configuration");
        config = SimulationConfig.Small(2); config.ElevationMetersPerUnit = 10000; config.LapseRateCPerKm = 6;
        var current = new WorldGenerator().Generate(92, config);
        var bytes = SaveCodec.Save(current); var copy = SaveCodec.Load(bytes);
        Check(copy.Generation.GeneratorVersion == WorldState.GeneratorVersion && copy.Config.ElevationMetersPerUnit == 10000 && copy.Config.LapseRateCPerKm == 6, "custom physical calibration round-trips with generation revision");
        Check(bytes.SequenceEqual(SaveCodec.Save(copy)), "physical world's repeated save is byte-identical");
        Check(current.Tiles.SelectMany(t => t.Seasons).SequenceEqual(copy.Tiles.SelectMany(t => t.Seasons)), "physical profiles do not regenerate on load");
        current.Generation.GeneratorVersion = 15;
        var beforeTerrain = SaveCodec.Load(WithoutAltitudeFields(current, terrainOnly: true));
        Check(!beforeTerrain.Config.DrainageTerrainEnabled && beforeTerrain.Generation.GeneratorVersion == 15,
            "saves predating erosion keep the legacy terrain selector and provenance");
        Check(current.Tiles.Select(t => t.Elevation).SequenceEqual(beforeTerrain.Tiles.Select(t => t.Elevation))
            && current.Tiles.SelectMany(t => t.Seasons).SequenceEqual(beforeTerrain.Tiles.SelectMany(t => t.Seasons)),
            "missing erosion option never regenerates stored terrain or climate");
    }

    private static byte[] WithoutAltitudeFields(WorldState world, bool terrainOnly = false)
    {
        using var raw = new MemoryStream();
        using (var input = new MemoryStream(SaveCodec.Save(world)))
        using (var gzip = new GZipStream(input, CompressionMode.Decompress)) gzip.CopyTo(raw);
        raw.Position = 0;
        using var reader = new BinaryReader(raw, Encoding.UTF8, true);
        string magic = reader.ReadString(); int length = reader.ReadInt32();
        byte[] header = reader.ReadBytes(length), body = reader.ReadBytes((int)(raw.Length - raw.Position));
        XDocument document;
        using (var xml = XmlDictionaryReader.CreateBinaryReader(header, XmlDictionaryReaderQuotas.Max)) document = XDocument.Load(xml);
        foreach (var field in document.Descendants().Where(e => terrainOnly ? e.Name.LocalName == "DrainageTerrainEnabled"
            : e.Name.LocalName == "ElevationMetersPerUnit" || e.Name.LocalName == "LapseRateCPerKm").ToArray()) field.Remove();
        using var binary = new MemoryStream();
        using (var xml = XmlDictionaryWriter.CreateBinaryWriter(binary, null, null, false)) { document.WriteTo(xml); xml.Flush(); }
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, true))
        using (var writer = new BinaryWriter(gzip, Encoding.UTF8, true))
        { writer.Write(magic); writer.Write((int)binary.Length); writer.Write(binary.ToArray()); writer.Write(body); }
        return output.ToArray();
    }
}
