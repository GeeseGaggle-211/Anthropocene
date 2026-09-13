using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    [Serializable]
    public sealed class BiomePreference
    {
        public Terrain Biome;
        public double Weight;
        public BiomePreference() { }
        public BiomePreference(Terrain biome, double weight) { Biome = biome; Weight = weight; }
    }
    [Serializable]
    public sealed class SpeciesDefinition
    {
        public string Id, Name;
        public double FoodConsumption = .4, FreshwaterConsumption = .45, GrowthRate = .0201, FoodYield = 80;
        public int MinimumAnimals = 4, MaximumAnimals = 10, MaximumHerdSize = 120;
        public bool Domesticable = true;
        public double DefaultHabitatWeight = .05, ReliefAffinity, OasisAffinity, MovementHabitatWeight = .3;
        public List<BiomePreference> Biomes = new List<BiomePreference>();
        public double Suitability(Tile tile, VegetationTier? projectedVegetation = null)
        {
            var vegetation = projectedVegetation ?? tile.CurrentVegetation;
            if (!tile.TraversableLand) return 0;
            var woodland = vegetation >= VegetationTier.Medium && (tile.Environment == EnvironmentKind.Temperate || tile.Environment == EnvironmentKind.Tropical);
            var preference = Biomes.FirstOrDefault(b => b.Biome == Terrain.Jungle && tile.Environment == EnvironmentKind.Tropical && vegetation == VegetationTier.High)
                ?? Biomes.FirstOrDefault(b => woodland ? b.Biome == Terrain.Woodland : b.Biome == Vegetation.LegacyTerrain(tile.Environment, vegetation));
            double biome = preference?.Weight ?? DefaultHabitatWeight;
            double relief = tile.Relief == Relief.Hills || tile.Relief == Relief.Highland || tile.Relief == Relief.Mountain ? ReliefAffinity : 0;
            return Math.Max(0, biome * (1 + relief) * (1 + OasisAffinity * tile.OasisInfluence));
        }
        public static List<SpeciesDefinition> Defaults() => new List<SpeciesDefinition>
        {
            new SpeciesDefinition { Id = "antelope", Name = "Antelope", MaximumHerdSize = 150, Biomes = new List<BiomePreference> { new BiomePreference(Terrain.Grassland, 1.4), new BiomePreference(Terrain.Woodland, .8), new BiomePreference(Terrain.Desert, .2) } },
            new SpeciesDefinition { Id = "goat", Name = "Wild goats", FoodConsumption = .25, FreshwaterConsumption = .3, FoodYield = 60, MaximumHerdSize = 100, ReliefAffinity = 1.5, Biomes = new List<BiomePreference> { new BiomePreference(Terrain.Grassland, .7), new BiomePreference(Terrain.Woodland, .9), new BiomePreference(Terrain.Desert, .35), new BiomePreference(Terrain.Tundra, .6) } },
            new SpeciesDefinition { Id = "bison", Name = "Bison", FoodConsumption = .65, FreshwaterConsumption = .7, FoodYield = 150, MinimumAnimals = 3, MaximumAnimals = 7, MaximumHerdSize = 150, Biomes = new List<BiomePreference> { new BiomePreference(Terrain.Grassland, 1.3), new BiomePreference(Terrain.Tundra, .7), new BiomePreference(Terrain.Woodland, .4) } },
            new SpeciesDefinition { Id = "camel", Name = "Wild camels", FoodConsumption = .22, FreshwaterConsumption = .12, FoodYield = 120, MaximumHerdSize = 100, OasisAffinity = 5, MovementHabitatWeight = .7, Biomes = new List<BiomePreference> { new BiomePreference(Terrain.Desert, 2.5), new BiomePreference(Terrain.Grassland, .12) } },
            new SpeciesDefinition { Id = "horse", Name = "Wild horses", FoodConsumption = .42, FreshwaterConsumption = .4, FoodYield = 110, MaximumHerdSize = 120, Biomes = new List<BiomePreference> { new BiomePreference(Terrain.Grassland, 1.8), new BiomePreference(Terrain.Tundra, .5), new BiomePreference(Terrain.Woodland, .2) } }
        };
    }
    [Serializable]
    public sealed class SpeciesPractice { public string Species; public double Experience; }
    [Serializable]
    public sealed class PopulationChange { public int Camp, Entity, Count; public string Kind; }
    [Serializable]
    public sealed class LogisticsRecord
    {
        public int Hunter, Camp;
        public bool InSupply;
        public double ProvisionsLoaded, Deposited, RetainedHarvest, Overflow, HarvestOverflow, InventoryLoss, EndingCarriedFood;
    }
}
