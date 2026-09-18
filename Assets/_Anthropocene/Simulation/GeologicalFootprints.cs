using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    internal static class GeologicalFootprints
    {
        internal static DepositSite[] Sites(GenerationContext c, GeologicalProfile profile, int origin, int key, bool scattered = false)
        {
            int width = c.Config.Width;
            int height = c.Config.Height;
            var tuning = c.ResourceOptions.Deposit;
            bool broad = profile.Shape == DepositShape.Reservoir || profile.Shape == DepositShape.Field || profile.Shape == DepositShape.Bed;
            double radius = broad ? tuning.BroadRadius : tuning.NarrowRadius;
            if (scattered) radius = tuning.ScatteredRadius;
            double rx = radius * (1 + GeologicalProvinceStage.Roll(c.World.Seed, 210, key));
            double ry = profile.Shape == DepositShape.Vein ? radius * tuning.VeinWidth : radius * tuning.OtherWidth;
            if (scattered) ry = radius * tuning.ScatteredWidth;
            int province = c.World.Resources.Province[origin];
            double angle = (province > 0 ? c.World.Resources.Provinces[province - 1].Angle : Math.PI * GeologicalProvinceStage.Roll(c.World.Seed, 213, key)) + .5 * (GeologicalProvinceStage.Roll(c.World.Seed, 214, key) - .5);
            double cos = Math.Cos(angle);
            double sin = Math.Sin(angle);
            double bound = Math.Max(rx, ry) * 1.15;
            int ox = origin % width;
            int oy = origin / width;
            int bx = Math.Max(1, (int)Math.Ceiling(bound * width));
            int by = Math.Max(1, (int)Math.Ceiling(bound * height));
            var sites = new List<DepositSite>();
            for (int y = Math.Max(0, oy - by); y <= Math.Min(height - 1, oy + by); y++)
            for (int x = ox - bx; x <= ox + bx; x++)
            {
                int tile = y * width + (x % width + width) % width;
                double dx = (x - ox) / (double)width;
                double dy = (y - oy) / (double)height;
                double a = (dx * cos + dy * sin) / rx;
                double b = (-dx * sin + dy * cos) / ry;
                double edge = 1 + .18 * TerrainNoise.Fractal(c.World.Seed, 12250, (x % width + width) % width / (double)width, y / (double)height, 128, 2);
                if ((tile != origin && a * a + b * b > edge) || !profile.Eligible(c.World, tile)) continue;
                if (scattered)
                {
                    if (!c.World.Tiles[tile].Land) continue;
                    if (tile != origin && GeologicalProvinceStage.Roll(c.World.Seed, 232, key, tile) >= tuning.ScatteredSiteBaseChance + tuning.ScatteredSiteCenterBonus * Math.Max(0, 1 - a * a - b * b)) continue;
                }
                bool buried = profile.Shape == DepositShape.Reservoir || profile.Shape == DepositShape.Seam;
                double cover = GeologicalProvinceStage.Roll(c.World.Seed, 211, key) + (c.World.Tiles[origin].Elevation - c.World.Tiles[tile].Elevation) * 3;
                var depth = buried ? (cover < .65 ? BurialDepth.Deep : BurialDepth.Shallow) : cover < .4 ? BurialDepth.Surface : BurialDepth.Shallow;
                if (scattered) depth = BurialDepth.Surface;
                if (profile.Id == 33 || profile.Id == 34) depth = BurialDepth.Surface;
                // Access is a fraction of local material, not discovery permission or equipment authorization.
                sites.Add(new DepositSite
                {
                    Tile = tile,
                    Burial = depth,
                    Setting = (MarineSetting)c.World.Resources.Marine[tile],
                    Exposure = (ushort)(depth == BurialDepth.Surface ? 10000 : depth == BurialDepth.Shallow ? 2000 : 200)
                });
            }
            // Keep only the connected piece containing the origin after geological clipping.
            var available = new HashSet<int>(sites.Select(s => s.Tile));
            var connected = new HashSet<int> { origin };
            var queue = new Queue<int>();
            queue.Enqueue(origin);
            while (queue.Count > 0)
                foreach (int n in c.Map.Neighbors(queue.Dequeue()))
                    if (available.Contains(n) && connected.Add(n)) queue.Enqueue(n);
            sites = sites.Where(s => scattered || connected.Contains(s.Tile)).OrderBy(s => s.Tile).ToList();
            var weights = sites.Select(s => {
                if (scattered || !MineralDepositStage.HasScatteredOccurrences(profile.Id)) return 1.0;
                double dx = TerrainNoise.LongitudeDelta(s.Tile % width / (double)width, ox / (double)width);
                double dy = (s.Tile / width - oy) / (double)height;
                double a = (dx * cos + dy * sin) / rx;
                double b = (-dx * sin + dy * cos) / ry;
                return tuning.CoreFloor + Math.Exp(-tuning.CoreDecay * (a * a + b * b));
            }).ToArray();
            AssignShares(sites, weights);
            return sites.ToArray();
        }
        internal static void AssignShares(List<DepositSite> sites, double[] weights)
        {
            if (sites.Count == 0 || sites.Count > 10000)
                throw new InvalidOperationException("Mineral footprint exceeds the 10,000-part site allocation; reduce deposit footprint tuning for this world size.");
            double sum = weights.Sum();
            int remaining = 10000 - sites.Count;
            int assigned = 0;
            var fractions = new double[sites.Count];
            for (int i = 0; i < sites.Count; i++)
            {
                double exact = weights[i] / sum * remaining;
                int share = 1 + (int)Math.Floor(exact);
                var site = sites[i];
                site.Share = (ushort)share;
                sites[i] = site;
                assigned += share;
                fractions[i] = exact - Math.Floor(exact);
            }
            foreach (int i in Enumerable.Range(0, sites.Count).OrderByDescending(i => fractions[i]).ThenBy(i => sites[i].Tile).Take(10000 - assigned))
            {
                var site = sites[i];
                site.Share++;
                sites[i] = site;
            }
        }
    }
}
