using System;
using System.Linq;
using Anthropocene.Simulation;

internal static class ClimateGenerationChecks
{
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception("Climate generation: " + message); }

    // Continuous synthetic landforms isolate the atmospheric model from procedural relief.
    // Matching normalized geography at different resolutions catches per-tile transport decay.
    private static WorldState Continent(int width, int height, bool ridge = false, bool ocean = true)
    {
        var cfg = new SimulationConfig { Width = width, Height = height, Players = 1 };
        var world = new WorldState { Seed = 4109, Config = cfg, Tiles = new Tile[width * height] };
        for (int row = 0; row < height; row++)
            for (int col = 0; col < width; col++)
            {
                double x = col / (double)width, y = row / (double)(height - 1);
                bool land = !ocean || x > .22 && x < .76 && y > .10 && y < .90;
                double mountain = ridge && land ? .31 * Math.Exp(-Math.Pow((x - .40) / .027, 2)) : 0;
                world.Tiles[row * width + col] = new Tile { Id = row * width + col, Terrain = land ? Terrain.Grassland : Terrain.Ocean, Elevation = (float)(land ? .48 + mountain : .30) };
            }
        new ClimateStage().Generate(new GenerationContext { World = world, Map = world.Map });
        return world;
    }

    private static double Rain(WorldState world, double left, double right, double top, double bottom) => world.Tiles
        .Where(t => t.Id % world.Config.Width / (double)world.Config.Width >= left && t.Id % world.Config.Width / (double)world.Config.Width < right
            && t.Id / world.Config.Width / (double)(world.Config.Height - 1) >= top && t.Id / world.Config.Width / (double)(world.Config.Height - 1) < bottom)
        .Average(t => t.Seasons.Average(s => s.Runoff));

    public static void Run()
    {
        checks = 0;
        var flat = Continent(192, 96);
        var repeat = Continent(192, 96);
        Check(flat.Tiles.SelectMany(t => t.Seasons).SequenceEqual(repeat.Tiles.SelectMany(t => t.Seasons)), "seasonal atmospheric fields repeat exactly for identical input");
        Check(flat.Tiles.SelectMany(t => t.Seasons).All(s => !float.IsNaN(s.Temperature) && !float.IsInfinity(s.Temperature) && s.Rain >= 0 && s.Snow >= 0 && s.Runoff >= 0 && Math.Abs(s.WindX) <= 1.000001), "all climate fields are finite and precipitation is nonnegative");
        Check(flat.Tiles.SelectMany(t => t.Seasons).All(s => (s.Temperature > 0 ? s.Snow == 0 : s.Rain == 0) && Math.Abs(s.Rain + s.Snow - s.Runoff) < 1e-7), "precipitation partitions into liquid or snow without duplication");
        Check(flat.Tiles.SelectMany(t => t.Seasons).All(s => s.RegionalMoisture == 0 && s.Moisture == 0), "climate forcing does not bypass regional/local soil accounting");
        double equator = flat.Tiles.Where(t => t.Id / 192 >= 44 && t.Id / 192 <= 52).Average(t => t.Seasons.Average(s => s.Temperature));
        double poles = flat.Tiles.Where(t => t.Id / 192 < 5 || t.Id / 192 > 90).Average(t => t.Seasons.Average(s => s.Temperature));
        Check(equator > poles + 25, "stellar latitude forcing retains a cold polar regime");
        Check(flat.Tiles.Where(t => t.Id / 192 < 40).All(t => t.Seasons[1].Temperature < t.Seasons[3].Temperature)
            && flat.Tiles.Where(t => t.Id / 192 > 55).All(t => t.Seasons[1].Temperature > t.Seasons[3].Temperature), "seasons are opposite across hemispheres");
        var summerRain = flat.Tiles.Where(t => t.Land && t.Id / 192 > 52 && t.Id / 192 < 65).Select(t => Math.Abs(t.Seasons[1].Runoff - t.Seasons[3].Runoff)).Average();
        Check(summerRain > .001, "seasonal circulation moves rainfall instead of merely recoloring temperature");
        var rowRain = flat.Tiles.Where(t => t.Id / 192 == 23 && t.Land).Select(t => t.Seasons.Average(s => s.Runoff)).ToArray();
        Check(rowRain.Max() > rowRain.Min() * 1.2, "ocean distance and curved circulation create longitude variation within one latitude");

        var mountain = Continent(192, 96, ridge: true);
        double windwardFlat = Rain(flat, .34, .40, .20, .29), windwardMountain = Rain(mountain, .34, .40, .20, .29);
        double leewardFlat = Rain(flat, .43, .53, .20, .29), leewardMountain = Rain(mountain, .43, .53, .20, .29);
        Check(windwardMountain > windwardFlat * 1.1, "mountain uplift increases windward precipitation");
        Check(leewardMountain < leewardFlat * .9, "upwind mountain extraction produces a leeward rain shadow");
        int peak = 24 * 192 + 77;
        Check(mountain.Tiles[peak].Seasons.Average(s => s.Temperature) < flat.Tiles[peak].Seasons.Average(s => s.Temperature) - 5,
            "mountain height cools the air while the lowland latitude is unchanged");

        var large = Continent(768, 384);
        double smallInterior = Rain(flat, .46, .64, .23, .32), largeInterior = Rain(large, .46, .64, .23, .32);
        Check(largeInterior > smallInterior * .65 && largeInterior < smallInterior * 1.35, "more gameplay tiles do not exhaust the same continental air mass");
        Check(Rain(large, .25, .32, .20, .29) > largeInterior * 2, "ocean-fed lowlands are wetter than the deep interior of a synthetic supercontinent");
        var dryPlanet = Continent(96, 48, ocean: false);
        Check(dryPlanet.Tiles.Average(t => t.Seasons.Average(s => s.Runoff)) < .003, "land recycling does not invent a moisture source on a planet without evaporation");
        double dryEquator = Rain(dryPlanet, 0, 1, .45, .51);
        foreach (var tile in dryPlanet.Tiles)
        {
            double y = tile.Id / 96 / 47.0;
            if (y > .52 && y < .75) { tile.Terrain = Terrain.Ocean; tile.Elevation = .30f; }
        }
        new ClimateStage().Generate(new GenerationContext { World = dryPlanet, Map = dryPlanet.Map });
        Check(Rain(dryPlanet, 0, 1, .45, .51) > dryEquator + .003,
            "an ocean at another latitude can supply a land-only equatorial row through meridional transport");
        Console.WriteLine("PASS: " + checks + " atmospheric generation assertions");
        Console.WriteLine("CLIMATE windward flat/ridge=" + windwardFlat.ToString("F4") + "/" + windwardMountain.ToString("F4")
            + " leeward flat/ridge=" + leewardFlat.ToString("F4") + "/" + leewardMountain.ToString("F4")
            + " same-interior 192/768=" + smallInterior.ToString("F4") + "/" + largeInterior.ToString("F4"));
    }
}
