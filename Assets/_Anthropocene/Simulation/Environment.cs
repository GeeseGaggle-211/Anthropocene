using System;
using System.Linq;

namespace Anthropocene.Simulation
{
    public enum EnvironmentKind : byte { Ocean, Shallows, Lake, Desert, Grassland, Temperate, Tropical, Cold, Ice }
    public enum VegetationTier : byte { Low, Medium, High }

    public static class Vegetation
    {
        public static EnvironmentKind Classify(double temperature, double regionalMoisture, SimulationConfig c) =>
            temperature < c.IceTemperature ? EnvironmentKind.Ice : temperature < c.ColdTemperature ? EnvironmentKind.Cold :
            regionalMoisture < c.DesertMoisture ? EnvironmentKind.Desert : regionalMoisture < c.SteppeMoisture ? EnvironmentKind.Grassland :
            temperature > c.TropicalTemperature ? EnvironmentKind.Tropical : EnvironmentKind.Temperate;
        public static VegetationTier Potential(Tile t, SimulationConfig c)
        {
            if (!t.Land) return AquaticEcology.PlanktonPotential(t);
            if (t.Environment == EnvironmentKind.Ice) return VegetationTier.Low;
            double temperature = t.Seasons.Average(s => s.Temperature), regional = t.Seasons.Average(s => s.RegionalMoisture);
            double warmth = Numbers.Clamp((temperature - c.IceTemperature) / c.VegetationWarmthRange, 0, 1);
            double Score(double moisture) => warmth * Numbers.Clamp((moisture - c.VegetationDryFloor) / c.VegetationMoistureRange, 0, 1);
            VegetationTier Tier(double score) => score < c.VegetationMediumPotential ? VegetationTier.Low : score < c.VegetationHighPotential ? VegetationTier.Medium : VegetationTier.High;
            // Rate each season's local gain against its actual freshwater capacity. A river flag alone gives no uplift.
            double uplift = t.WaterBody == WaterKind.Saltwater || t.FreshwaterSource == FreshwaterSource.None ? 0 :
                t.Seasons.Average(s => Math.Max(0, s.Moisture - s.RegionalMoisture) * Numbers.Clamp(s.MaxFreshwater / 70.0, 0, 1));
            var climatic = Tier(Score(regional));
            var local = (VegetationTier)Math.Min((int)VegetationTier.Medium, (int)Tier(Score(regional + uplift)));
            var potential = (VegetationTier)Math.Max((int)climatic, (int)local);
            // Natural opening vegetation only: High requires regional support. Future climate transformation can reclassify the environment.
            return t.Environment == EnvironmentKind.Desert ? (VegetationTier)Math.Min((int)potential, (int)VegetationTier.Medium) : potential;
        }
        public static VegetationTier Update(VegetationTier natural, VegetationTier current, double condition, SimulationConfig c)
        {
            current = (VegetationTier)Math.Min((int)natural, (int)current);
            if (natural == VegetationTier.Low) return VegetationTier.Low;
            if (natural == VegetationTier.Medium) return current == VegetationTier.Medium ? condition < c.MediumVegetationDown ? VegetationTier.Low : current : condition > c.MediumVegetationUp ? VegetationTier.Medium : current;
            if (current == VegetationTier.High && condition < c.HighVegetationDown) current = VegetationTier.Medium;
            if (current == VegetationTier.Medium && condition < c.HighPotentialLowDown) current = VegetationTier.Low;
            if (current == VegetationTier.Low && condition > c.HighPotentialLowUp) current = VegetationTier.Medium;
            if (current == VegetationTier.Medium && condition > c.HighVegetationUp) current = VegetationTier.High;
            return current;
        }
        public static Terrain LegacyTerrain(EnvironmentKind environment, VegetationTier vegetation) =>
            environment == EnvironmentKind.Ocean ? Terrain.Ocean : environment == EnvironmentKind.Shallows ? Terrain.Shallows : environment == EnvironmentKind.Lake ? Terrain.Lake :
            environment == EnvironmentKind.Desert ? Terrain.Desert : environment == EnvironmentKind.Cold ? Terrain.Tundra : environment == EnvironmentKind.Ice ? Terrain.Ice :
            environment == EnvironmentKind.Tropical && vegetation == VegetationTier.High ? Terrain.Jungle :
            (environment == EnvironmentKind.Temperate || environment == EnvironmentKind.Tropical) && vegetation >= VegetationTier.Medium ? Terrain.Woodland : Terrain.Grassland;
        public static string Label(EnvironmentKind environment, VegetationTier tier, bool river = false)
        {
            if (environment == EnvironmentKind.Desert && tier == VegetationTier.Medium && river) return "Riparian vegetation";
            if (tier == VegetationTier.High) return environment == EnvironmentKind.Tropical ? "Rainforest" : environment == EnvironmentKind.Cold ? "Taiga" : environment == EnvironmentKind.Temperate ? "Forest" : environment == EnvironmentKind.Grassland ? "Lush grassland" : "Lush arid growth";
            if (tier == VegetationTier.Medium) return environment == EnvironmentKind.Temperate || environment == EnvironmentKind.Tropical ? "Woodland" : environment == EnvironmentKind.Cold ? "Tree line" : "Grass and shrubs";
            return "Sparse cover";
        }
    }
}
