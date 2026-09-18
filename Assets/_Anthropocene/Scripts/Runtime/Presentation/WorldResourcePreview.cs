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
                double amount = resources.ComponentQuantity(body, material);
                if (amount == 0) continue;
                foreach (var site in body.Sites)
                    values[site.Tile] += accessible ? resources.AccessibleAt(body, site, material) : amount * site.Share / 10000;
            }
            return values;
        }

        public static Color ColorFor(double value, Color terrain)
        {
            if (value <= 0) return terrain * .35f;
            float strength = Mathf.Clamp01((float)(Math.Log(1 + value) / Math.Log(100001)));
            // A visible minimum distinguishes even a trace component from absent material.
            return Color.Lerp(new Color(.13f, .43f, .46f), new Color(1, .83f, .22f), strength);
        }

        public static string Describe(WorldState world, int tile, MaterialId material)
        {
            var r = world.Resources;
            if (r == null) return "This saved world has no geological resource layer. Generate a new world to inspect resources.";
            double total = r.SurfaceTotal(tile, material), accessible = r.SurfaceAccessible(tile, material);
            var deposits = new StringBuilder(); int count = 0;
            foreach (var body in r.Bodies)
            {
                double amount = r.ComponentQuantity(body, material); if (amount <= 0) continue;
                foreach (var site in body.Sites.Where(s => s.Tile == tile))
                {
                    total += amount * site.Share / 10000; accessible += r.AccessibleAt(body, site, material); count++;
                    deposits.Append("\nBody ").Append(body.Id).Append(" · ").Append(body.Shape)
                        .Append("\n").Append(site.Burial).Append(" · ").Append(site.Setting)
                        .Append(" · exposure ").Append((site.Exposure / 100.0).ToString("0.#")).Append("%")
                        .Append("\nShared reserve: ").Append(body.Quantity.ToString("N0"))
                        .Append(" units across ").Append(body.Sites.Length).Append(" tiles\n");
                }
            }
            string discovery = r.DiscoveryTechnologies[(int)material];
            var text = new StringBuilder("INSPECTED TILE ").Append(tile).Append(" · ").Append(world.Tiles[tile].Environment)
                .Append("\n\n").Append(Name(material)).Append(" on this tile\nTotal: ").Append(total.ToString("N1"))
                .Append(" units\nHuman accessible: ").Append(accessible.ToString("N1")).Append(" units")
                .Append("\nBase accessibility: ").Append((100 * r.AccessibleFraction(material)).ToString("0.##")).Append("%")
                .Append("\nDiscovery: ").Append(discovery.Length == 0 ? "available from the start" : Regex.Replace(discovery, "([a-z])([A-Z])", "$1 $2"))
                .Append("\n\nHost: ").Append((HostRock)r.Host[tile]).Append("\nSetting: ").Append((MarineSetting)r.Marine[tile]);
            if (material == MaterialId.Wood)
                text.Append("\n\nVegetation potential: ").Append(world.Tiles[tile].NaturalVegetationPotential)
                    .Append("\nRenewable capacity: ").Append(total.ToString("N0")).Append(" units")
                    .Append("\nRecovery coefficient: ").Append((r.WoodRecoveryPerYear * 100).ToString("0.#"))
                    .Append("% / year\nGeneration metadata; harvesting and regrowth are deferred to the economy pass.");
            else if (material != MaterialId.Stone && material != MaterialId.Clay)
                text.Append("\n\n").Append(count).Append(" intersecting deposit(s)").Append(deposits)
                    .Append("\nTile totals allocate each shared body's material by its tile share. Accessibility also includes local exposure. A shared reserve is counted once across the whole deposit.");
            return text.ToString();
        }
    }
}
