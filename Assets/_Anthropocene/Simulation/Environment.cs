using System;
using System.Linq;

namespace Anthropocene.Simulation
{
    public enum EnvironmentKind : byte { Ocean, Shallows, Lake, Desert, Grassland, Temperate, Tropical, Cold, Ice }
    public enum VegetationTier : byte { Low, Medium, High }

    public static class Vegetation
    {
        public static EnvironmentKind Classify(double temperature, double moisture, SimulationConfig c) =>
            temperature < c.IceTemperature ? EnvironmentKind.Ice : temperature < c.ColdTemperature ? EnvironmentKind.Cold :
            moisture < c.DesertMoisture ? EnvironmentKind.Desert : temperature > c.TropicalTemperature ? EnvironmentKind.Tropical :
            moisture < c.SteppeMoisture ? EnvironmentKind.Grassland : EnvironmentKind.Temperate;
        public static VegetationTier Potential(Tile t, SimulationConfig c)
        {
            if (!t.Land || t.Environment == EnvironmentKind.Ice) return VegetationTier.Low;
            double temperature = t.Seasons.Average(s => s.Temperature), moisture = Math.Max(t.Seasons.Average(s => s.Moisture), c.OasisVegetationMoisture * t.OasisInfluence);
            double v = Numbers.Clamp((temperature - c.IceTemperature) / c.VegetationWarmthRange, 0, 1) * Numbers.Clamp((moisture - c.VegetationDryFloor) / c.VegetationMoistureRange, 0, 1);
            return v < c.VegetationMediumPotential ? VegetationTier.Low : v < c.VegetationHighPotential ? VegetationTier.Medium : VegetationTier.High;
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
        public static string Label(EnvironmentKind environment, VegetationTier tier)
        {
            if (tier == VegetationTier.High) return environment == EnvironmentKind.Tropical ? "Rainforest" : environment == EnvironmentKind.Cold ? "Taiga" : environment == EnvironmentKind.Temperate ? "Forest" : environment == EnvironmentKind.Grassland ? "Lush grassland" : "Lush arid growth";
            if (tier == VegetationTier.Medium) return environment == EnvironmentKind.Temperate || environment == EnvironmentKind.Tropical ? "Woodland" : environment == EnvironmentKind.Cold ? "Tree line" : "Grass and shrubs";
            return "Sparse cover";
        }
    }
}
