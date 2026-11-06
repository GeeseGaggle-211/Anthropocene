using System;

namespace Anthropocene.Simulation
{
    /// <summary>Physical vertical scale for newly generated worlds; zero scale preserves legacy climate.</summary>
    public static class AltitudeClimate
    {
        public static bool Physical(SimulationConfig config) => config.ElevationMetersPerUnit > 0;
        public static double Meters(SimulationConfig config, double elevation)
            => (elevation - config.SeaLevel) * config.ElevationMetersPerUnit;
        public static double Cooling(SimulationConfig config, double elevation)
            => Math.Max(0, elevation - config.SeaLevel) * (Physical(config)
                ? config.ElevationMetersPerUnit * config.LapseRateCPerKm / 1000.0 : config.LapseRate);
    }

    // A single generation-boundary correction, not a second atmospheric solve.
    // Hydrology holds precipitation/runoff fixed while changing ground height.
    // Correct the stored surface temperature and repartition that same water.
    internal sealed class SurfaceClimateStage : IWorldGenerationStage
    {
        public string Name => "Final-elevation temperature and snowfall";
        public string Requires => "Generated climate and final hydrological elevations";
        public string Produces => "Height-consistent seasonal temperature and rain/snow partition";
        public void Generate(GenerationContext context)
        {
            if (!AltitudeClimate.Physical(context.Config)) return;
            var reference = context.ClimateSurfaceHeights;
            var tiles = context.World.Tiles;
            if (reference == null || reference.Length != tiles.Length)
                throw new InvalidOperationException("Final surface climate requires the generation climate height snapshot.");
            foreach (var tile in tiles)
            {
                if (tile.Elevation == reference[tile.Id]) continue;
                double adjustment = AltitudeClimate.Cooling(context.Config, reference[tile.Id])
                    - AltitudeClimate.Cooling(context.Config, tile.Elevation);
                for (int season = 0; season < tile.Seasons.Length; season++)
                {
                    var profile = tile.Seasons[season];
                    float precipitation = profile.Rain + profile.Snow;
                    profile.Temperature = (float)(profile.Temperature + adjustment);
                    profile.Rain = profile.Temperature > 0 ? precipitation : 0;
                    profile.Snow = profile.Temperature <= 0 ? precipitation : 0;
                    tile.Seasons[season] = profile;
                }
            }
            context.ClimateSurfaceHeights = null;
        }
    }
}
