using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    internal sealed class OasisStage : IWorldGenerationStage
    {
        public string Name => "Groundwater-proxy oases";
        public string Requires => "Arid biome, continuous relief, drainage, rainfall, boundary rivers";
        public string Produces => "Finite freshwater and productivity features without changing biome";
        public static double Viability(WorldState w, Tile tile)
        {
            if (tile.Terrain != Terrain.Desert || !tile.Land || tile.Relief == Relief.Mountain) return 0;
            double recharge = Numbers.Clamp(Math.Log(1 + tile.Flow) / Math.Log(1 + w.Config.RiverThreshold), 0, 1);
            double depression = Numbers.Clamp(tile.DepressionDepth / .018, 0, 1);
            double rain = Numbers.Clamp(tile.Seasons.Average(s => s.Rain) / .04, 0, 1);
            return recharge * .7 + depression * .2 + rain * .1;
        }
        public void Generate(GenerationContext context)
        {
            var w = context.World; var cfg = w.Config; var map = w.Map; var excluded = new HashSet<int>(); var spaced = new HashSet<int>();
            foreach (var tile in w.Tiles)
                if (!tile.Land || tile.River) excluded.UnionWith(map.Radius(tile.Id, cfg.OasisSurfaceWaterSeparation));
            int columns = (cfg.Width + cfg.OasisRegionSize - 1) / cfg.OasisRegionSize;
            foreach (var region in w.Tiles.Where(t => t.Terrain == Terrain.Desert).GroupBy(t => (t.Id / cfg.Width / cfg.OasisRegionSize) * columns + t.Id % cfg.Width / cfg.OasisRegionSize).OrderBy(g => g.Key))
            {
                var candidates = region.Where(t => !excluded.Contains(t.Id)).Select(t => new { Tile = t, Score = Viability(w, t) }).Where(x => x.Score >= cfg.OasisViabilityThreshold)
                    .OrderBy(x => -Math.Log(Math.Max(1e-12, Numbers.Random(w.Seed, 120, x.Tile.Id))) / (x.Score * x.Score)).ThenBy(x => x.Tile.Id);
                int desired = (int)Math.Floor(region.Count() * cfg.OasisDensity);
                if (region.Count() >= cfg.OasisMinimumRegionArea && cfg.OasisDensity > 0) desired = Math.Max(1, desired);
                foreach (var candidate in candidates)
                {
                    if (desired <= 0) break; if (spaced.Contains(candidate.Tile.Id)) continue;
                    candidate.Tile.Oasis = true; desired--; spaced.UnionWith(map.Radius(candidate.Tile.Id, cfg.OasisSpacing - 1));
                }
            }
            foreach (var origin in w.Tiles.Where(t => t.Oasis).OrderBy(t => t.Id))
                foreach (int n in map.Radius(origin.Id, 1))
                {
                    var tile = w.Tiles[n]; if (!tile.Land) continue;
                    double factor = n == origin.Id ? 1 : cfg.OasisNeighborFalloff;
                    tile.OasisInfluence = (float)Math.Max(tile.OasisInfluence, factor);
                    if (tile.FreshwaterSource == FreshwaterSource.None || tile.FreshwaterSource == FreshwaterSource.RainPool) tile.FreshwaterSource = FreshwaterSource.Oasis;
                    for (int season = 0; season < 4; season++)
                    {
                        var data = tile.Seasons[season];
                        data.MaxFreshwater = Math.Max(data.MaxFreshwater, (float)(cfg.OasisFreshwater * factor)); data.AccessibleFreshwater = data.MaxFreshwater;
                        data.MaxFood = Math.Max(data.MaxFood, (float)(cfg.OasisFood * factor)); data.FoodRate = Math.Max(data.FoodRate, (float)(cfg.OasisFoodRegeneration * factor));
                        data.FreshwaterRate = (float)cfg.OasisWaterRecovery; data.Habitat = Math.Max(data.Habitat, .25f * (float)factor); tile.Seasons[season] = data;
                    }
                    tile.Freshwater = tile.Seasons[0].MaxFreshwater * .9; tile.Food = tile.Seasons[0].MaxFood * .9;
                }
            Validate(w);
        }
        public static void Validate(WorldState w)
        {
            var oases = w.Tiles.Where(t => t.Oasis).ToArray(); var marked = new HashSet<int>();
            foreach (var oasis in oases)
            {
                if (oasis.Terrain != Terrain.Desert || !oasis.Land || oasis.WaterBody != WaterKind.None || oasis.FreshwaterSource != FreshwaterSource.Oasis || Viability(w, oasis) < w.Config.OasisViabilityThreshold)
                    throw new InvalidOperationException("Ineligible oasis.");
                if (marked.Contains(oasis.Id)) throw new InvalidOperationException("Oasis spacing violated.");
                marked.UnionWith(w.Map.Radius(oasis.Id, w.Config.OasisSpacing - 1));
                if (w.Map.Radius(oasis.Id, w.Config.OasisSurfaceWaterSeparation).Any(n => !w.Tiles[n].Land || w.Tiles[n].River)) throw new InvalidOperationException("Oasis overlaps ordinary surface-water access.");
                if (oasis.Freshwater > oasis.Seasons[0].MaxFreshwater + 1e-6) throw new InvalidOperationException("Oasis capacity exceeded.");
            }
        }
    }
    internal static class WildHerdPlacement
    {
        public static double Weight(WorldState w, Tile tile, SpeciesDefinition species)
        {
            double forage = tile.Seasons.Average(s => s.MaxFood) / Math.Max(1, species.FoodConsumption * species.MinimumAnimals * 3);
            double fresh = tile.Seasons.Average(s => s.MaxFreshwater) / Math.Max(1, species.FreshwaterConsumption * species.MinimumAnimals * 5);
            return species.Suitability(tile) * (.15 + Math.Min(1, Math.Sqrt(Math.Max(0, forage)))) * (.2 + Math.Min(1, Math.Sqrt(Math.Max(0, fresh))));
        }
        private static void Add(WorldState w, Tile tile)
        {
            var species = w.Config.Species.OrderBy(s => s.Id, StringComparer.Ordinal).ToArray(); double total = species.Sum(s => Weight(w, tile, s));
            double roll = Numbers.Random(w.Seed, 133, tile.Id) * total; var chosen = species[species.Length - 1];
            foreach (var s in species) { roll -= Weight(w, tile, s); if (roll <= 0) { chosen = s; break; } }
            int animals = chosen.MinimumAnimals + (int)(Numbers.Random(w.Seed, 134, tile.Id) * (chosen.MaximumAnimals - chosen.MinimumAnimals + 1));
            w.Herds.Add(new Herd { Id = w.NextId++, Tile = tile.Id, Species = chosen.Id, Animals = animals, Capacity = chosen.MaximumHerdSize });
        }
        public static void Populate(WorldState w)
        {
            int size = w.Config.HerdRegionSize, columns = (w.Config.Width + size - 1) / size;
            foreach (var region in w.Tiles.Where(t => t.TraversableLand && t.Terrain != Terrain.Ice).GroupBy(t => (t.Id / w.Config.Width / size) * columns + t.Id % w.Config.Width / size).OrderBy(g => g.Key))
            {
                double variation = 1 + (Numbers.Random(w.Seed, 130, region.Key) * 2 - 1) * w.Config.HerdRegionVariation;
                int count = (int)Math.Round(region.Count() * w.Config.HerdsPerLandTile * variation);
                var candidates = region.Select(t => new { Tile = t, Suitability = w.Config.Species.Sum(s => Weight(w, t, s)) }).Where(x => x.Suitability > 0)
                    .OrderBy(x => -Math.Log(Math.Max(1e-12, Numbers.Random(w.Seed, 131, x.Tile.Id))) / x.Suitability).ThenBy(x => x.Tile.Id).Take(count);
                foreach (var candidate in candidates) Add(w, candidate.Tile);
            }
        }
        public static void EnsureOpeningHerds(WorldState w)
        {
            var occupied = new HashSet<int>(w.Herds.Select(h => h.Tile)); var campTiles = new HashSet<int>(w.Camps.Select(c => c.Tile));
            foreach (var camp in w.Camps.OrderBy(c => c.Id))
            {
                var reachable = w.Map.Radius(camp.Tile, w.Config.VisionRadius).Where(n => w.Map.Reachable(camp.Tile, n, w.Config.VisionRadius, p => w.Tiles[p].TraversableLand)).ToArray();
                if (reachable.Any(occupied.Contains)) continue;
                int selected = reachable.Where(n => !occupied.Contains(n) && !campTiles.Contains(n) && w.Config.Species.Any(s => Weight(w, w.Tiles[n], s) > 0)).OrderByDescending(n => w.Config.Species.Max(s => Weight(w, w.Tiles[n], s))).ThenBy(n => Numbers.Hash(w.Seed, 132, n, camp.Player)).DefaultIfEmpty(-1).First();
                if (selected < 0) throw new InvalidOperationException("Start has no appropriate opening herd habitat.");
                Add(w, w.Tiles[selected]); occupied.Add(selected);
            }
        }
    }
}
