using System;
using System.Linq;

namespace Anthropocene.Simulation
{
    // Finalize independently depletable human-accessible and deeper pools after placer transport.
    internal static class GeologicalResourcePools
    {
        internal static void Initialize(WorldResourceState resources)
        {
            resources.DeepStone = (ushort[])resources.Stone.Clone();
            resources.DeepClay = (ushort[])resources.Clay.Clone();
            foreach (var body in resources.Bodies)
            {
                body.SiteExposureWeightTotal = body.Sites.Sum(site => site.Share * (double)site.Exposure);
                double weightedExposure = body.SiteExposureWeightTotal / 100000000.0;
                for (int i = 0; i < body.Components.Length; i++)
                {
                    var part = body.Components[i];
                    part.AccessibleQuantity = body.Quantity * part.PartsPerTenThousand / 10000.0 * weightedExposure * resources.AccessibleFraction(part.Material);
                    part.DeepQuantity = part.AccessibleQuantity * resources.DeepReserveMultiplier;
                    part.InitialAccessibleQuantity = part.AccessibleQuantity;
                    part.InitialDeepQuantity = part.DeepQuantity;
                    body.Components[i] = part;
                }
            }
            var byId = resources.Bodies.ToDictionary(body => body.Id);
            foreach (var child in resources.Bodies.Where(body => body.SourceBody != 0))
            {
                var parent = byId[child.SourceBody];
                for (int i = 0; i < parent.Components.Length; i++)
                {
                    var part = parent.Components[i];
                    var childPart = child.Components.Single(item => item.Material == part.Material);
                    part.InitialAccessibleQuantity += childPart.InitialAccessibleQuantity;
                    part.InitialDeepQuantity += childPart.InitialDeepQuantity;
                    parent.Components[i] = part;
                }
            }
        }
    }
}
