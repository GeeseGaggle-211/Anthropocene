using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    public sealed class SurfaceMaterialStage : IWorldGenerationStage
    {
        public string Name => "Surface materials and woody capacity";
        public string Requires => "Geological bodies and vegetation";
        public string Produces => "Finite Stone/Clay, renewable Wood potential and sourced placers";
        public void Generate(GenerationContext c)
        {
            var r = c.World.Resources; if (r == null) return;
            var tuning = c.ResourceOptions.Surface;
            foreach (var t in c.World.Tiles)
            {
                if (!t.Land) continue;
                int id = t.Id; var host = (HostRock)r.Host[id]; var history = (GeologicalHistory)r.History[id];
                double amount = tuning.StoneMinimum + tuning.StoneRange * GeologicalProvinceStage.Roll(c.World.Seed, 301, id);
                // Local absence is allowed. Neither placement nor food/water viability is changed.
                double stoneChance = host == HostRock.Clastic ? tuning.ClasticStoneChance : tuning.OtherStoneChance;
                if (GeologicalProvinceStage.Roll(c.World.Seed, 302, id) < stoneChance) r.Stone[id] = Pack(r, amount);
                bool weathering = (history & GeologicalHistory.Weathered) != 0 && host != HostRock.Ultramafic;
                bool fineSediment = (history & GeologicalHistory.Basin) != 0 && (host == HostRock.Clastic || host == HostRock.Carbonate);
                double clayChance = (weathering ? tuning.WeatheredClayChance : tuning.BackgroundClayChance) + (fineSediment ? tuning.FineSedimentClayBonus : 0) + (t.River ? tuning.RiverClayBonus : 0);
                if (GeologicalProvinceStage.Roll(c.World.Seed, 303, id) < clayChance) r.Clay[id] = Pack(r, tuning.ClayMinimum + tuning.ClayRange * GeologicalProvinceStage.Roll(c.World.Seed, 304, id));
                // Low = woody brush, not forest. No trees/wood on ice or water.
                double wood = t.Environment == EnvironmentKind.Ice ? 0 : t.NaturalVegetationPotential == VegetationTier.High ? tuning.HighWood : t.NaturalVegetationPotential == VegetationTier.Medium ? tuning.MediumWood : tuning.LowWood;
                r.Wood[id] = Pack(r, wood * (tuning.WoodMinimum + tuning.WoodRange * GeologicalProvinceStage.Roll(c.World.Seed, 305, id)));
            }
            if (!c.ResourceOptions.DisabledProfiles.Contains(100)) GeologicalPlacers.AddPlacers(c);
        }
        private static ushort Pack(WorldResourceState r, double fraction) => (ushort)Math.Min(r.MaximumQuantity / r.SurfaceUnit, Math.Round(r.MaximumQuantity * fraction / r.SurfaceUnit));
    }
}
