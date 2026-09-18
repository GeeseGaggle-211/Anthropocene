using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Anthropocene.Simulation;
using UnityEngine;

namespace Anthropocene.Presentation
{
    // Read-only developer atlas adapter. Never used to build a player observation.
    public static class WorldResourcePreview
    {
        public static string Name(MaterialId material) => Regex.Replace(material.ToString(), "([a-z])([A-Z])", "$1 $2");

        public static double[] Values(WorldState world, MaterialId material, bool accessible)
        {
            var values = new double[world.Tiles.Length]; var resources = world.Resources;
            if (resources == null) return values;
            for (int i = 0; i < values.Length; i++)
                values[i] = accessible ? resources.SurfaceAccessible(i, material) : resources.SurfaceTotal(i, material);
            foreach (var body in resources.Bodies)
            {
                double amount = resources.TotalComponentQuantity(body, material);
                if (amount == 0) continue;
                foreach (var site in body.Sites)
                    values[site.Tile] += accessible ? resources.AccessibleAt(body, site, material) : resources.TotalAt(body, site, material);
            }
            return values;
        }

        public static double ScaleMaximum(WorldResourceState resources, MaterialId material, bool accessible)
        {
            if (resources == null) return ResourceCatalog.DefaultMaximumQuantity;
            bool hasDeep = !accessible && resources.DeepReserveMultiplier > 0 && material != MaterialId.Wood;
            return resources.MaximumQuantity * (hasDeep ? 1 + resources.DeepReserveMultiplier : 1);
        }

        public static Color ColorFor(double value, Color terrain, double maximum = ResourceCatalog.DefaultMaximumQuantity)
        {
            if (value <= 0) return terrain * .35f;
            float strength = Mathf.Clamp01((float)(Math.Log(1 + value) / Math.Log(1.0 + maximum)));
            // A visible minimum distinguishes even a trace component from absent material.
            return Color.Lerp(new Color(.13f, .43f, .46f), new Color(1, .83f, .22f), strength);
        }

        public static string Describe(WorldState world, int tile, MaterialId material)
        {
            var r = world.Resources;
            if (r == null) return "This saved world has no geological resource layer. Generate a new world to inspect resources.";
            double total = r.SurfaceTotal(tile, material), accessible = r.SurfaceAccessible(tile, material), deep = r.SurfaceDeepTotal(tile, material);
            var deposits = new StringBuilder(); int count = 0;
            foreach (var body in r.Bodies)
            {
                double amount = r.TotalComponentQuantity(body, material); if (amount <= 0) continue;
                foreach (var site in body.Sites.Where(s => s.Tile == tile))
                {
                    total += r.TotalAt(body, site, material); accessible += r.AccessibleAt(body, site, material); count++;
                    deep += r.DeepAt(body, site, material);
                    deposits.Append("\nBody ").Append(body.Id).Append(" · ").Append(body.Concentration == MineralConcentration.Scattered ? "Scattered occurrences" : body.Shape.ToString())
                        .Append("\n").Append(site.Burial).Append(" · ").Append(site.Setting)
                        .Append(" · exposure ").Append((site.Exposure / 100.0).ToString("0.#")).Append("%")
                        .Append("\nShared ").Append(Name(material)).Append(" reserve: ").Append(amount.ToString("N1"))
                        .Append(" units across ").Append(body.Sites.Length).Append(" tiles");
                    if (body.DeepQuantity > 0) deposits.Append("\nOf which deep: ").Append(r.DeepComponentQuantity(body, material).ToString("N1")).Append(" units");
                    deposits.Append("\n");
                }
            }
            string discovery = r.DiscoveryTechnologies[(int)material];
            var text = new StringBuilder("INSPECTED TILE ").Append(tile).Append(" · ").Append(world.Tiles[tile].Environment)
                .Append("\n\n").Append(Name(material)).Append(" on this tile\nTotal: ").Append(total.ToString("N1"))
                .Append(" units\nHuman accessible: ").Append(accessible.ToString("N1")).Append(" units")
                .Append(deep > 0 ? "\nDeep reserve included in total: " + deep.ToString("N1") + " units\nDeep access requires " + Regex.Replace(r.DeepMiningTechnology, "([a-z])([A-Z])", "$1 $2") + "; excluded from human accessible." : "")
                .Append("\nAccessible share of local total: ").Append(total > 0 ? (100 * accessible / total).ToString("0.##") + "%" : "no local stock")
                .Append("\nDiscovery: ").Append(discovery.Length == 0 ? "available from the start" : Regex.Replace(discovery, "([a-z])([A-Z])", "$1 $2"))
                .Append("\n\nHost: ").Append((HostRock)r.Host[tile]).Append("\nSetting: ").Append((MarineSetting)r.Marine[tile]);
            if (material == MaterialId.Wood)
                text.Append("\n\nVegetation potential: ").Append(world.Tiles[tile].NaturalVegetationPotential)
                    .Append("\nRenewable capacity: ").Append(total.ToString("N0")).Append(" units")
                    .Append("\nRecovery coefficient: ").Append((r.WoodRecoveryPerYear * 100).ToString("0.#"))
                    .Append("% / year\nGeneration metadata; harvesting and regrowth are deferred to the economy pass.");
            else if (material != MaterialId.Stone && material != MaterialId.Clay)
                text.Append("\n\n").Append(count).Append(" intersecting resource source(s)").Append(deposits)
                    .Append("\nTile totals use the saved site allocation. New mineral pools include exposure in that allocation and keep early-accessible and deep quantities separate. A shared reserve is counted once across the whole deposit.");
            return text.ToString();
        }
    }
}
