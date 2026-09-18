using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    internal static class GeologicalPlacers
    {
        internal static void AddPlacers(GenerationContext c)
        {
            var r = c.World.Resources;
            var result = r.Bodies.ToList();
            var map = c.Map;
            foreach (var source in r.Bodies.Where(b => b.Concentration == MineralConcentration.Deposit && (b.Profile == 3 || b.Profile == 7 || b.Profile == 31 || b.Profile == 26)).OrderBy(b => b.Id))
            {
                var path = new List<int> { source.OriginTile };
                int current = source.OriginTile;
                // The drainage graph is acyclic; no river-edge substitutions or material replication.
                int limit = Math.Max(8, c.Config.Width / 12);
                for (int step = 0; step < limit; step++)
                {
                    int next = c.World.Tiles[current].Downstream;
                    if (next < 0) break;
                    path.Add(next);
                    current = next;
                    if (c.World.Tiles[current].WaterBody == WaterKind.Saltwater)
                    {
                        // Bounded shelf sorting proxy continues the same finite source seaward.
                        for (int shelf = 0; shelf < c.ResourceOptions.Placer.ShelfSteps; shelf++)
                        {
                            int n = map.Neighbors(current).Where(id => c.World.Tiles[id].WaterBody == WaterKind.Saltwater && !path.Contains(id) && (MarineSetting)r.Marine[id] == MarineSetting.Shelf)
                                .OrderBy(id => GeologicalProvinceStage.Roll(c.World.Seed, 306, (int)source.Id, id)).ThenBy(id => id).DefaultIfEmpty(-1).First();
                            if (n < 0) break;
                            path.Add(n);
                            current = n;
                        }
                        break;
                    }
                    if (step > 1 && ((GeologicalHistory)r.History[current] & GeologicalHistory.Sorted) != 0 && c.World.Tiles[current].Relief == Relief.Lowland) break;
                }
                if (path.Count < 2 || c.World.Tiles[current].Environment == EnvironmentKind.Lake) continue;
                var tuning = c.ResourceOptions.Placer;
                double mass = source.InitialQuantity * (tuning.TransferMinimum + tuning.TransferRange * GeologicalProvinceStage.Roll(c.World.Seed, 307, (int)source.Id));
                if (mass <= 0) continue;
                source.Quantity -= mass;
                var placerSites = path.Skip(Math.Max(1, path.Count - tuning.MaximumSites))
                    .Select(id => new DepositSite { Tile = id, Exposure = 10000, Burial = BurialDepth.Surface, Setting = (MarineSetting)r.Marine[id] })
                    .ToList();
                GeologicalFootprints.AssignShares(placerSites, Enumerable.Range(1, placerSites.Count).Select(i => (double)i).ToArray());
                result.Add(new DepositBody
                {
                    Id = 10000000000L + source.Id,
                    SourceBody = source.Id,
                    Profile = 100,
                    OriginTile = current,
                    InitialQuantity = mass,
                    Quantity = mass,
                    Shape = DepositShape.Bed,
                    Hardness = 1,
                    Quality = source.Quality,
                    Components = (DepositComponent[])source.Components.Clone(),
                    TransportPath = path.ToArray(),
                    Sites = placerSites.ToArray()
                });
            }
            r.Bodies = result.OrderBy(b => b.Id).ToArray();
        }
    }
}
