using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    public sealed class MineralDepositStage : IWorldGenerationStage
    {
        public string Name => "Finite geological deposits";
        public string Requires => "Geological provinces";
        public string Produces => "Shared deposit bodies and local access fractions";
        private readonly IEnumerable<GeologicalProfile> profiles;
        public MineralDepositStage() : this(GeologicalProfiles.All) { }
        public MineralDepositStage(IEnumerable<GeologicalProfile> profiles) { this.profiles = profiles; }
        public void Generate(GenerationContext c)
        {
            var r = c.World.Resources;
            if (r == null) return;
            var bodies = new List<DepositBody>();
            int seed = c.World.Seed;
            var tuning = c.ResourceOptions.Deposit;
            foreach (var profile in profiles.Where(p => !p.Secondary && !c.ResourceOptions.DisabledProfiles.Contains(p.Id)))
            {
                // Fixed normalized candidate lattice: increasing resolution does not multiply reservoirs.
                for (int domain = 0; domain < 2; domain++) for (int key = 0; key < 2048; key++)
                {
                    int semantic = profile.Id * 8192 + domain * 2048 + key;
                    double x = (key % 64 + GeologicalProvinceStage.Roll(seed, 201, semantic)) / 64;
                    double y = (key / 64 + GeologicalProvinceStage.Roll(seed, 202, semantic)) / 32;
                    int tile = Math.Min(c.Config.Height - 1, (int)(y * c.Config.Height)) * c.Config.Width + Math.Min(c.Config.Width - 1, (int)(x * c.Config.Width));
                    bool marine = c.World.Tiles[tile].WaterBody == WaterKind.Saltwater;
                    if (marine != (domain == 1) || !profile.Eligible(c.World, tile)) continue;
                    bool varied = HasScatteredOccurrences(profile.Id);
                    if (varied && !marine && GeologicalProvinceStage.Roll(seed, 230, semantic) < tuning.ScatteredChance * c.ResourceOptions.Density * c.ResourceOptions.ScatteredDensity)
                    {
                        var scattered = GeologicalFootprints.Sites(c, profile, tile, semantic, true);
                        // A broad mineralized district holds sparse, small occurrences, not one giant ore body.
                        double stock = Math.Min(r.MaximumQuantity * (tuning.ScatteredStockMinimum + tuning.ScatteredStockRange * GeologicalProvinceStage.Roll(seed, 231, semantic)), scattered.Length * (double)r.MaximumQuantity / tuning.ScatteredSiteStockDivisor);
                        if (stock > 0)
                        {
                            bodies.Add(new DepositBody
                            {
                                Id = 1000000000L + (long)profile.Id * 1000000 + key + 1,
                                Profile = profile.Id,
                                OriginTile = tile,
                                InitialQuantity = stock,
                                Quantity = stock,
                                Shape = DepositShape.Field,
                                Concentration = MineralConcentration.Scattered,
                                Hardness = 1,
                                Quality = 15,
                                Components = profile.Composition.ToArray(),
                                Sites = scattered
                            });
                        }
                    }
                    double chance = profile.Id == 36 ? tuning.RareBasinChance : profile.Shape == DepositShape.Bed ? tuning.BedChance : tuning.OrdinaryChance;
                    if (profile.Id == 1 || profile.Id == 2) chance *= tuning.CopperChanceMultiplier;
                    else if (profile.Id == 3) chance *= tuning.TinChanceMultiplier;
                    if (GeologicalProvinceStage.Roll(seed, 203, semantic) >= chance * c.ResourceOptions.Density) continue;
                    var sites = GeologicalFootprints.Sites(c, profile, tile, semantic);
                    if (sites.Length == 0) continue;
                    double grade = .65 + .35 * GeologicalProvinceStage.Roll(seed, 204, semantic);
                    var composition = profile.Composition.Select(v => new DepositComponent(v.Material, Math.Max(1, (int)(v.PartsPerTenThousand * grade)))).ToArray();
                    // Optional co-products are sampled within their parent's private key space.
                    composition = composition.Where((v, i) => i == 0 || v.PartsPerTenThousand >= 500 || GeologicalProvinceStage.Roll(seed, 205, semantic, (int)v.Material) < .6).ToArray();
                    if (profile.Id == 14)
                    {
                        double oilShare = .1 + .8 * GeologicalProvinceStage.Roll(seed, 206, semantic);
                        composition = new[]
                        {
                            new DepositComponent(MaterialId.Petroleum, (int)(9000 * oilShare)),
                            new DepositComponent(MaterialId.NaturalGas, (int)(9000 * (1 - oilShare))),
                            new DepositComponent(MaterialId.Sulfur, 200)
                        };
                    }
                    double quantity = Math.Min(r.MaximumQuantity, Math.Round(r.MaximumQuantity * (tuning.RichStockMinimum + tuning.RichStockRange * GeologicalProvinceStage.Roll(seed, 207, semantic)) / 10) * 10);
                    bodies.Add(new DepositBody
                    {
                        Id = (long)profile.Id * 1000000 + domain * 100000 + key + 1,
                        Profile = profile.Id,
                        OriginTile = tile,
                        InitialQuantity = quantity,
                        Quantity = quantity,
                        Shape = profile.Shape,
                        Hardness = (byte)(1 + (int)(GeologicalProvinceStage.Roll(seed, 208, semantic) * 5)),
                        Quality = (byte)(25 + GeologicalProvinceStage.Roll(seed, 209, semantic) * 76),
                        Components = composition,
                        Sites = sites
                    });
                }
            }
            r.Bodies = bodies.OrderBy(b => b.Id).ToArray();
        }
        public static bool HasScatteredOccurrences(int profile) => profile >= 1 && profile <= 12 || profile >= 20 && profile <= 31 && profile != 24;
    }
}
