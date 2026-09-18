using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    public sealed class ResourceAccessAuditStage : IWorldGenerationStage
    {
        public string Name => "Resource opportunity audit";
        public string Requires => "Placement and resources";
        public string Produces => "Diagnostic warnings only";
        public void Generate(GenerationContext c)
        {
            if (c.World.Resources == null) return;
            foreach (var material in new[] { MaterialId.Stone, MaterialId.Clay, MaterialId.Wood })
            {
                int absent = c.World.Camps.Count(camp => !c.Map.Radius(camp.Tile, 2).Any(tile => c.World.Resources.SurfaceTotal(tile, material) > 0 && c.Map.Reachable(camp.Tile, tile, 2, n => c.World.Tiles[n].TraversableLand)));
                if (absent > 0) c.World.Generation.Warnings.Add(absent + " starts lack " + material + " within two traversable hexes (developer opportunity audit; no collection economy is implemented).");
            }
            ResourceValidation.Validate(c.World);
        }
    }
}
