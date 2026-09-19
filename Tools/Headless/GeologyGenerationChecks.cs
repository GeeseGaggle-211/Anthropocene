using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Anthropocene.Simulation;

internal static class GeologyGenerationChecks
{
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception("Geology: " + message); }
    private static GenerationContext Context(WorldState w, ResourceGenerationOptions options = null) => new GenerationContext { World = w, Map = w.Map, ResourceOptions = options ?? new ResourceGenerationOptions() };
    private static string BodyHash(DepositBody[] bodies, WorldResourceState source)
    { var saved = source.Bodies; try { source.Bodies = bodies; return ResourceCodec.Fingerprint(source); } finally { source.Bodies = saved; } }
    public static void Run()
    {
        checks = 0;
        Check(ResourceCatalog.Materials.Select(m => m.Id).Distinct().Count() == ResourceCatalog.Materials.Count, "stable unique material IDs");
        Check(ResourceCatalog.Get(MaterialId.Stone).BaseAccessibleFraction == 1 && ResourceCatalog.Get(MaterialId.Clay).BaseAccessibleFraction == 1 && ResourceCatalog.Get(MaterialId.Wood).BaseAccessibleFraction == .25, "surface accessibility contracts");
        Check(!ResourceCatalog.Discoverable(MaterialId.Copper, new HashSet<string>()) && ResourceCatalog.Discoverable(MaterialId.Copper, new HashSet<string> { "CopperWorking" }), "copper requires copper working");
        Check(ResourceCatalog.Materials.All(m => ResourceCatalog.Concentrations.ContainsKey(m.Id)), "every catalog material has an entry in the concentrations lookup table");
        Check(ResourceCatalog.Concentration(MaterialId.Coal) == 10.0, "coal concentration is 10.0");
        Check(ResourceCatalog.Concentration(MaterialId.Petroleum) == 1.0, "petroleum oil concentration is 1.0");
        Check(ResourceCatalog.Concentration(MaterialId.Coal) == 10.0 * ResourceCatalog.Concentration(MaterialId.Petroleum), "coal concentration is 10x that of oil");
        Check(ResourceCatalog.Materials.Where(m => m.Id != MaterialId.Coal).All(m => ResourceCatalog.Concentration(m.Id) == 1.0), "all other resources remain at baseline concentration 1.0");
        var originalResourceFingerprints = new Dictionary<WorldShape, string>
        {
            [WorldShape.Continents] = "01EC7A6F0A665D3D897F41D33A9DE9902E5C95F46406BC4F18E8D785F56AE79B",
            [WorldShape.Archipelagos] = "831385E7BC40465059312F1D229610B0F023A987DB6191E2A4B8DBA519B884E0",
            [WorldShape.Pangaea] = "F689F7E83659FE2BF0624170AD1C034D042D338C35016B8880F35DBBC18340E8"
        };
        foreach (var shape in Enum.GetValues(typeof(WorldShape)).Cast<WorldShape>())
        {
            var config = SimulationConfig.Small(4); config.Shape = shape;
            var generator = new WorldGenerator();
            var before = generator.Generate(42, config, new ResourceGenerationOptions { Enabled = false });
            var world = generator.Generate(42, config); var r = world.Resources;
            var coreTiles = world.Tiles.Where(t => t.Land && t.Continental && world.Generation?.AncientOrogenStrength != null && world.Generation.AncientOrogenStrength[t.Id] > 0.55f).ToArray();
            if (coreTiles.Length > 0)
            {
                Check(coreTiles.All(t => {
                    var host = (HostRock)r.Host[t.Id];
                    return host == HostRock.Metamorphic || host == HostRock.Granite || host == HostRock.Alkaline;
                }), shape + " ancient orogen metamorphic cores survive normal province overwrites");
            }
            Check(ResourceCodec.Fingerprint(r) == originalResourceFingerprints[shape], shape + " original resource generation fingerprint");
            Check(SaveCodec.LegacyFingerprint(before) == SaveCodec.LegacyFingerprint(world), shape + " preserves all legacy state and IDs");
            Check(ObservationContract.Serialize(new AgentObservationBuilder().Build(before, 0), before.Camps[0].Mandate) == ObservationContract.Serialize(new AgentObservationBuilder().Build(world, 0), world.Camps[0].Mandate), shape + " latent minerals do not leak into observations");
            Check(ResourceCodec.Fingerprint(r) == ResourceCodec.Fingerprint(generator.Generate(42, config).Resources), shape + " repeated generation");
            Check(r.Bodies.Length > 0 && r.Bodies.All(b => b.InitialQuantity <= 100000), shape + " finite large quantities");
            Check(r.Wood.Where((v, i) => !world.Tiles[i].Land).All(v => v == 0), shape + " no marine timber");
            Check(world.Tiles.Where(t => t.Land && t.Environment != EnvironmentKind.Ice && t.NaturalVegetationPotential == VegetationTier.High).All(t => r.SurfaceTotal(t.Id, MaterialId.Wood) >= 80000), shape + " high vegetation timber capacity");
            Check(r.Bodies.Where(b => b.SourceBody == 0).All(b => b.Sites.All(s => GeologicalProfiles.All.Single(p => p.Id == b.Profile).Eligible(world, s.Tile))), shape + " host and process eligibility throughout footprints");
            Check(r.Bodies.All(b => Connected(world, b)), shape + " connected multi-hex bodies");
            Check(r.Bodies.Where(b => b.SourceBody != 0).GroupBy(b => b.SourceBody).All(g => Math.Abs(r.Bodies.Single(b => b.Id == g.Key).Quantity + g.Sum(b => b.InitialQuantity) - r.Bodies.Single(b => b.Id == g.Key).InitialQuantity) < 1e-6), shape + " secondary deposits conserve source host and components");
            var restored = SaveCodec.Load(SaveCodec.Save(world));
            Check(SaveCodec.Fingerprint(world) == SaveCodec.Fingerprint(restored), shape + " resource round trip");
            var legacy = SaveCodec.Load(SaveCodec.Save(before));
            Check(legacy.Resources == null && SaveCodec.Fingerprint(legacy) == SaveCodec.Fingerprint(before), shape + " old envelope retains absent resources");
            var original = ResourceCodec.Fingerprint(r); Array.Reverse(r.Bodies);
            Check(ResourceCodec.Fingerprint(r) == original, shape + " canonical resource serialization"); Array.Reverse(r.Bodies);
            var reversedWorld = generator.Generate(42, config, new ResourceGenerationOptions { Enabled = false });
            var reverse = Context(reversedWorld); new GeologicalProvinceStage().Generate(reverse); new MineralDepositStage(GeologicalProfiles.All.Reverse()).Generate(reverse); new SurfaceMaterialStage().Generate(reverse);
            // Generation tectonic scratch affects marine ridge classification: use the same shared pipeline for exact comparison below.
            var forwardWorld = generator.Generate(42, config, new ResourceGenerationOptions { Enabled = false }); var forward = Context(forwardWorld);
            new GeologicalProvinceStage().Generate(forward); new MineralDepositStage().Generate(forward); new SurfaceMaterialStage().Generate(forward);
            Check(ResourceCodec.Fingerprint(reversedWorld.Resources) == ResourceCodec.Fingerprint(forwardWorld.Resources), shape + " profile traversal independence");
            var disabled = generator.Generate(42, config, new ResourceGenerationOptions { DisabledProfiles = new[] { 33, 34, 35 } });
            Check(BodyHash(r.Bodies.Where(b => b.Profile != 33 && b.Profile != 34 && b.Profile != 35).ToArray(), r) == BodyHash(disabled.Resources.Bodies, r), shape + " offshore profile toggles do not reshuffle land deposits");
            var resolver = new TurnResolver(); var held = world.Camps.ToDictionary(c => c.Player, c => new List<Order> { new Order(OrderKind.NoOp, c.Id) });
            resolver.Resolve(world, held); resolver.Resolve(restored, held);
            Check(SaveCodec.Fingerprint(world) == SaveCodec.Fingerprint(restored), shape + " save continuation");
            Check(ResourceCodec.Fingerprint(world.Resources) == original, shape + " opening economy does not mutate resource stocks");
        }
        OffshoreFixtures();
        OccurrenceFixtures();
        IronPathwayFixtures();
        FossilFuelPathwayFixtures();
        TuningFixtures();
        QuantityLimitFixtures();
        DeepReserveFixtures();
        var small = new WorldGenerator().Generate(1, SimulationConfig.Small(4));
        var body = small.Resources.Bodies.First(b => b.SourceBody == 0);
        double originalQuantity = body.Quantity;
        body.Quantity = double.NaN;
        bool rejected = false; try { ResourceValidation.Validate(small); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "NaN reserves rejected");
        body.Quantity = originalQuantity;
        var depleted = small.Resources.Bodies.First(b => b.SourceBody == 0 && !small.Resources.Bodies.Any(s => s.SourceBody == b.Id));
        depleted.Quantity = 0;
        small.Resources.AccessibleParts[(int)MaterialId.Copper] = 777;
        var oldRates = SaveCodec.Load(SaveCodec.Save(small));
        Check(oldRates.Resources.Bodies.Single(b => b.Id == depleted.Id).Quantity == 0 && oldRates.Resources.AccessibleFraction(MaterialId.Copper) == .0777, "depleted stocks and saved access tuning survive without regeneration");
        small.Resources.Version = WorldResourceState.CurrentVersion + 1;
        rejected = false; try { SaveCodec.Load(SaveCodec.Save(small)); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "future resource schemas rejected");
        Console.WriteLine("PASS: " + checks + " geology assertions");
    }
    private static bool Connected(WorldState w, DepositBody b)
    {
        if (b.Concentration == MineralConcentration.Scattered) return true; // Deliberately separate occurrences within one district.
        var sites = new HashSet<int>(b.Sites.Select(s => s.Tile)); var reached = new HashSet<int> { b.OriginTile }; var queue = new Queue<int>(); queue.Enqueue(b.OriginTile);
        while (queue.Count > 0) foreach (int n in w.Map.Neighbors(queue.Dequeue())) if (sites.Contains(n) && reached.Add(n)) queue.Enqueue(n);
        return reached.Count == sites.Count;
    }
    private static void QuantityLimitFixtures()
    {
        var generator = new WorldGenerator();
        var config = SimulationConfig.Small(4);
        var baseline = generator.Generate(42, config);
        foreach (int maximum in new[] { 1006, 250000, 1000000, int.MaxValue })
        {
            var options = new ResourceGenerationOptions { MaximumQuantity = maximum };
            var world = generator.Generate(42, config, options);
            var r = world.Resources;
            Check(r.MaximumQuantity == maximum && r.ConfigurationHash == options.Hash && r.Bodies.All(b => b.InitialQuantity <= maximum), "configured cap bounds bodies at " + maximum);
            Check(SaveCodec.LegacyFingerprint(world) == SaveCodec.LegacyFingerprint(baseline) && r.Bodies.Select(b => b.Id).SequenceEqual(baseline.Resources.Bodies.Select(b => b.Id)), "quantity cap preserves core world and deposit placement at " + maximum);
            Check(new[] { MaterialId.Stone, MaterialId.Clay, MaterialId.Wood }.All(m => world.Tiles.All(t => r.SurfaceTotal(t.Id, m) >= 0 && r.SurfaceTotal(t.Id, m) <= maximum)), "compact surface stocks do not overflow at " + maximum);
            Check(world.Tiles.Where(t => t.Land && t.Environment != EnvironmentKind.Ice && t.NaturalVegetationPotential == VegetationTier.High).All(t => r.SurfaceTotal(t.Id, MaterialId.Wood) >= maximum * .8 - r.SurfaceUnit), "wood capacity scales with configured cap at " + maximum);
            Check(SaveCodec.Fingerprint(SaveCodec.Load(SaveCodec.Save(world))) == SaveCodec.Fingerprint(world), "configured quantity encoding roundtrip at " + maximum);
            if (maximum <= WorldResourceState.LegacyMaximumQuantity) continue;
            Check(r.Version == 3 && r.SurfaceUnit == WorldResourceState.SurfaceUnitFor(maximum), "larger caps use explicit schema-3 encoding");
            int unit = r.SurfaceUnit;
            r.SurfaceUnit = 0;
            bool rejected = false;
            try { SaveCodec.Load(SaveCodec.Save(world)); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "invalid saved surface scale rejected");
            r.SurfaceUnit = unit;
            r.Version = 2;
            rejected = false;
            try { ResourceValidation.Validate(world); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "large quantities cannot masquerade as a legacy resource schema");
        }
    }
    private static void DeepReserveFixtures()
    {
        var config = SimulationConfig.Small(4);
        var generator = new WorldGenerator();
        var old = generator.Generate(42, config);
        var shallowOptions = new ResourceGenerationOptions { MaximumQuantity = 10000000 };
        var shallow = generator.Generate(42, config, shallowOptions);
        var options = new ResourceGenerationOptions { MaximumQuantity = 10000000, DeepReserveMultiplier = 50 };
        var deep = generator.Generate(42, config, options);
        var r = deep.Resources;
        Check(r.Version == 4 && r.GenerationVersion == 3 && r.DeepMiningTechnology == "AdvancedMining" && r.DeepReserveMultiplier == 50, "deep worlds use independent schema and capability metadata");
        Check(r.ConfigurationHash == options.Hash && options.Hash != shallowOptions.Hash && shallow.Resources.ConfigurationHash == shallowOptions.Hash, "deep setting has its own configuration identity");
        Check(SaveCodec.LegacyFingerprint(deep) == SaveCodec.LegacyFingerprint(shallow) &&
            r.Bodies.Select(b => b.Id).SequenceEqual(shallow.Resources.Bodies.Select(b => b.Id)), "deep setting preserves core world and body placement");
        foreach (var b in r.Bodies)
        {
            var counterpart = shallow.Resources.Bodies.Single(v => v.Id == b.Id);
            Check(b.InitialQuantity == counterpart.InitialQuantity && b.Quantity == counterpart.Quantity && b.Sites.SequenceEqual(counterpart.Sites), "deep setting preserves near-surface stocks and sites");
            foreach (var part in b.Components)
            {
                double expected = counterpart.Components.Single(c => c.Material == part.Material).PartsPerTenThousand * b.Quantity / 10000.0 *
                    b.Sites.Sum(s => s.Share * (double)s.Exposure) / 100000000.0 * r.AccessibleFraction(part.Material) * r.Concentration(part.Material);
                Check(Math.Abs(part.AccessibleQuantity - expected) < 1e-6 && Math.Abs(part.DeepQuantity - 50 * expected) < 1e-5,
                    "accessible and deep pools preserve exposure-weighted access at every concentration");
                Check(Math.Abs(b.Sites.Sum(s => r.AccessibleAt(b, s, part.Material)) - part.AccessibleQuantity) < 1e-6 &&
                    Math.Abs(b.Sites.Sum(s => r.DeepAt(b, s, part.Material)) - part.DeepQuantity) < 1e-5,
                    "site access and deep shares conserve each material pool");
            }
        }
        Check(r.Bodies.Any(b => b.Components.Any(c => c.DeepQuantity > 0)) && r.Bodies.Any(b => b.Concentration == MineralConcentration.Scattered), "deep and scattered fixtures present");
        var oldById = old.Resources.Bodies.ToDictionary(b => b.Id);
        foreach (var b in r.Bodies.Where(b => oldById.ContainsKey(b.Id)))
            Check(Math.Abs(b.InitialQuantity - oldById[b.Id].InitialQuantity * 100) <= 510 &&
                Math.Abs(b.Quantity - oldById[b.Id].Quantity * 100) <= 510, "rich and trace stocks scale one hundredfold within ten-unit rounding");
        Check(r.Bodies.Where(b => b.SourceBody != 0).GroupBy(b => b.SourceBody).All(g =>
            Math.Abs(r.Bodies.Single(b => b.Id == g.Key).Quantity + g.Sum(b => b.InitialQuantity) - r.Bodies.Single(b => b.Id == g.Key).InitialQuantity) < 1e-5), "placers conserve near-surface stock and leave deep reserves separate");
        Check(r.Bodies.Where(b => b.SourceBody != 0).GroupBy(b => b.SourceBody).All(g =>
            r.Bodies.Single(b => b.Id == g.Key).Components.All(c =>
            Math.Abs(c.AccessibleQuantity + g.Sum(b => b.Components.Single(v => v.Material == c.Material).InitialAccessibleQuantity) - c.InitialAccessibleQuantity) < 1e-6 &&
            Math.Abs(c.DeepQuantity + g.Sum(b => b.Components.Single(v => v.Material == c.Material).InitialDeepQuantity) - c.InitialDeepQuantity) < 1e-5)),
            "placers conserve explicit accessible and deep component pools");
        foreach (var b in r.Bodies.Where(b => b.Components.Any(c => c.DeepQuantity > 0)).Take(10))
        {
            var material = b.Components[0].Material;
            Check(r.TotalComponentQuantity(b, material) == b.Components.Single(c => c.Material == material).AccessibleQuantity + r.DeepComponentQuantity(b, material) &&
                b.Sites.All(s => r.AccessibleAt(b, s, material) <= r.ComponentQuantity(b, material)), "deep stock is excluded from human accessible amounts");
        }
        Check(SaveCodec.Fingerprint(SaveCodec.Load(SaveCodec.Save(deep))) == SaveCodec.Fingerprint(deep), "schema-4 deep quantities round trip");
        int stoneTile = Array.FindIndex(r.DeepStone, v => v > 0);
        Check(stoneTile >= 0 && r.SurfaceDeepTotal(stoneTile, MaterialId.Stone) == 50 * r.SurfaceAccessible(stoneTile, MaterialId.Stone), "stone's deep pool starts at fifty times accessible stone");
        double deepStone = r.SurfaceDeepTotal(stoneTile, MaterialId.Stone);
        r.Stone[stoneTile] = 0;
        ResourceValidation.Validate(deep);
        Check(r.SurfaceAccessible(stoneTile, MaterialId.Stone) == 0 && r.SurfaceDeepTotal(stoneTile, MaterialId.Stone) == deepStone, "surface depletion does not draw down deep stone");
        r.DeepStone[stoneTile] = 0;
        ResourceValidation.Validate(deep);
        Check(r.SurfaceDeepTotal(stoneTile, MaterialId.Stone) == 0, "deep depletion is independent of surface stone");
        var target = r.Bodies.First(b => b.Components.Any(c => c.DeepQuantity > 0));
        var changed = target.Components[0]; double original = changed.DeepQuantity;
        changed.AccessibleQuantity = 0; target.Components[0] = changed;
        ResourceValidation.Validate(deep);
        Check(r.DeepComponentQuantity(target, changed.Material) == original && target.Components[0].AccessibleQuantity == 0, "component accessible depletion leaves deep stock intact");
        changed.AccessibleQuantity = target.Components[0].InitialAccessibleQuantity; target.Components[0] = changed;
        changed.DeepQuantity = double.NaN; target.Components[0] = changed;
        bool rejected = false; try { ResourceValidation.Validate(deep); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "nonfinite deep stock rejected");
        changed.DeepQuantity = original; target.Components[0] = changed;
        changed.InitialAccessibleQuantity = target.InitialQuantity * changed.PartsPerTenThousand / 10000 * r.AccessibleFraction(changed.Material) * r.Concentration(changed.Material) + 1000;
        changed.InitialDeepQuantity = changed.InitialAccessibleQuantity * r.DeepReserveMultiplier; target.Components[0] = changed;
        rejected = false; try { ResourceValidation.Validate(deep); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "excessive saved component pool rejected");
        r.DeepReserveMultiplier = 0;
        rejected = false; try { ResourceValidation.Validate(deep); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "invalid deep metadata rejected");
    }
    private static void TuningFixtures()
    {
        var config = SimulationConfig.Small(4);
        var generator = new WorldGenerator();
        var baseline = generator.Generate(42, config);
        var province = new ResourceGenerationOptions { Province = new ProvinceTuning { Count = 1, SourceChance = 0 } };
        var changedProvince = generator.Generate(42, config, province);
        Check(changedProvince.Resources.Provinces.Length == 1 && changedProvince.Resources.ConfigurationHash == province.Hash, "province geometry/history tuning is applied and identified");
        Check(SaveCodec.LegacyFingerprint(changedProvince) == SaveCodec.LegacyFingerprint(baseline), "province tuning leaves core world unchanged");
        Check(SaveCodec.Fingerprint(SaveCodec.Load(SaveCodec.Save(changedProvince))) == SaveCodec.Fingerprint(changedProvince), "nondefault resource tuning survives save and load");

        var noDeposits = new ResourceGenerationOptions { Deposit = new DepositTuning { OrdinaryChance = 0, BedChance = 0, RareBasinChance = 0, ScatteredChance = 0 } };
        var barren = generator.Generate(42, config, noDeposits);
        Check(barren.Resources.Bodies.Length == 0 && barren.Resources.Stone.SequenceEqual(baseline.Resources.Stone), "deposit chance tuning affects deposits without changing surface stone");

        var noStone = new ResourceGenerationOptions { Surface = new SurfaceTuning { ClasticStoneChance = 0, OtherStoneChance = 0 } };
        var stoneless = generator.Generate(42, config, noStone);
        Check(stoneless.Resources.Stone.All(v => v == 0) && BodyHash(stoneless.Resources.Bodies, baseline.Resources) == ResourceCodec.Fingerprint(baseline.Resources), "surface tuning affects stone without changing any deposit state");

        var noTransfer = new ResourceGenerationOptions { Placer = new PlacerTuning { TransferMinimum = 0, TransferRange = 0 } };
        var withoutPlacers = generator.Generate(42, config, noTransfer);
        Check(withoutPlacers.Resources.Bodies.All(b => b.SourceBody == 0 && b.Quantity == b.InitialQuantity), "zero placer transfer leaves primary stock intact");

        var noScatteredStock = new ResourceGenerationOptions { Deposit = new DepositTuning { ScatteredStockMinimum = 0, ScatteredStockRange = 0 } };
        var withoutScatteredStock = generator.Generate(42, config, noScatteredStock);
        Check(withoutScatteredStock.Resources.Bodies.All(b => b.Concentration != MineralConcentration.Scattered), "zero scattered stock omits empty fields");

        bool rejected = false;
        try { new ResourceGenerationOptions { Deposit = new DepositTuning { ScatteredStockRange = .081 } }.Validate(); }
        catch (ArgumentException) { rejected = true; }
        Check(rejected, "scattered stock settings cannot exceed persisted eight-percent limit");
        rejected = false;
        try { new ResourceGenerationOptions { Province = new ProvinceTuning { SourceChance = double.NaN } }.Validate(); }
        catch (ArgumentException) { rejected = true; }
        Check(rejected, "nonfinite province tuning rejected");
    }
    private static void OccurrenceFixtures()
    {
        var config = new SimulationConfig { Width = 256, Height = 128, Players = 4, StartSeparation = 9 };
        var generator = new WorldGenerator(); var w = generator.Generate(42, config); var r = w.Resources;
        var without = generator.Generate(42, config, new ResourceGenerationOptions { ScatteredDensity = 0 });
        var fields = r.Bodies.Where(b => b.Concentration == MineralConcentration.Scattered).ToArray();
        Check(fields.Length > 0 && fields.Any(b => b.Sites.Length > 10), "low-quantity fields cover broad districts");
        Check(fields.All(b => b.InitialQuantity <= 8000 && b.Sites.All(s => b.InitialQuantity * s.Share / 10000 <= 101)), "scattered local host stocks remain small before component fractions");
        Check(fields.All(b => MineralDepositStage.HasScatteredOccurrences(b.Profile) && b.Sites.All(s => w.Tiles[s.Tile].Land && s.Exposure == 10000)), "scattered occurrences respect land and surface exposure");
        Check(BodyHash(r.Bodies.Where(b => b.Concentration == MineralConcentration.Deposit).ToArray(), r) == BodyHash(without.Resources.Bodies, r), "scattered density cannot reshuffle or dilute rich deposits");
        Check(r.Bodies.Where(b => b.SourceBody != 0).Any(b => b.Sites.Length > 1), "source-funded placer reaches extend across multiple connected tiles");
        foreach (var m in new[] { MaterialId.Copper, MaterialId.Tin, MaterialId.Iron })
        {
            var deposits = r.Bodies.Where(b => b.Concentration == MineralConcentration.Deposit && r.ComponentQuantity(b, m) > 0).ToArray();
            var scattered = fields.Where(b => r.ComponentQuantity(b, m) > 0).ToArray();
            var richTiles = new HashSet<int>(deposits.SelectMany(b => b.Sites).Select(s => s.Tile));
            Check(scattered.Any(b => b.Sites.Any(s => !richTiles.Contains(s.Tile))), m + " low occurrences extend beyond rich deposit footprints");
            Check(deposits.Max(b => b.Sites.Max(s => r.ComponentQuantity(b, m) * s.Share / 10000)) > 5 * scattered.Max(b => b.Sites.Max(s => r.ComponentQuantity(b, m) * s.Share / 10000)), m + " concentrated deposits remain distinctly richer");
        }
        Check(r.Bodies.Any(b => b.SourceBody == 0 && b.Concentration == MineralConcentration.Deposit && b.Sites.Length > 8 && b.Sites.Max(s => (int)s.Share) > 3 * b.Sites.Min(s => (int)s.Share)), "rich deposits have cores and poorer margins");
        without.Resources.Version = 1; without.Resources.GenerationVersion = 1;
        string legacy = SaveCodec.Fingerprint(without); var restored = SaveCodec.Load(SaveCodec.Save(without));
        Check(restored.Resources.Version == 1 && restored.Resources.GenerationVersion == 1 && SaveCodec.Fingerprint(restored) == legacy, "schema-1 resources retain their exact payload without regeneration");
        r.GenerationVersion = WorldResourceState.CurrentGenerationVersion + 1;
        bool rejected = false; try { SaveCodec.Load(SaveCodec.Save(w)); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "future resource generator versions rejected");
    }
    private static void IronPathwayFixtures()
    {
        var config = new SimulationConfig { Width = 256, Height = 128, Players = 4, StartSeparation = 9 };
        var generator = new WorldGenerator();
        var world = generator.Generate(42, config);
        var r = world.Resources;

        // 1. Regression test: Ancient history is not universally true
        Check(world.Tiles.Any(t => (r.History[t.Id] & (ushort)GeologicalHistory.Ancient) == 0), "Ancient history is no longer universal across all tiles");
        Check(world.Tiles.Where(t => t.WaterBody == WaterKind.Saltwater).All(t => (r.History[t.Id] & (ushort)GeologicalHistory.Ancient) == 0), "marine tiles never have Ancient cratonic history");
        Check(world.Tiles.Any(t => t.Continental && (r.History[t.Id] & (ushort)GeologicalHistory.Ancient) != 0), "cratonic continental terranes retain Ancient history");

        // Verify dependent profiles requiring Ancient are not suppressed
        int[] ancientProfiles = { 7, 8, 21, 28, 30 };
        foreach (int pid in ancientProfiles)
        {
            var profile = GeologicalProfiles.All.Single(p => p.Id == pid);
            Check((profile.Required & GeologicalHistory.Ancient) == GeologicalHistory.Ancient, pid + " requires Ancient history");
            Check(world.Tiles.Any(t => profile.Eligible(world, t.Id)), pid + " has eligible tiles across cratonic terranes");
        }

        // 2. Iron occurrence pathway eligibility and incompatibility tests
        var bif = GeologicalProfiles.All.Single(p => p.Id == 8);
        var ironstone = GeologicalProfiles.All.Single(p => p.Id == 37);
        var weathered = GeologicalProfiles.All.Single(p => p.Id == 38);
        var titanomagnetite = GeologicalProfiles.All.Single(p => p.Id == 39);
        var bog = GeologicalProfiles.All.Single(p => p.Id == 40);

        Check(bif.Name == "Ancient iron formation" && bif.Hosts.SequenceEqual(new[] { HostRock.Basement, HostRock.Metamorphic }) && bif.Required == GeologicalHistory.Ancient && bif.Composition[0].PartsPerTenThousand == 7000, "BIF specification");
        Check(ironstone.Name == "Sedimentary ironstone" && ironstone.Hosts.SequenceEqual(new[] { HostRock.Clastic, HostRock.Carbonate }) && ironstone.Required == GeologicalHistory.Basin && ironstone.Composition[0].PartsPerTenThousand == 3800, "sedimentary ironstone specification");
        Check(weathered.Name == "Weathered iron ore" && weathered.Hosts.SequenceEqual(new[] { HostRock.Mafic, HostRock.Ultramafic, HostRock.Basement }) && weathered.Required == (GeologicalHistory.Weathered | GeologicalHistory.Preserved) && weathered.Composition[0].PartsPerTenThousand == 4500, "weathered iron ore specification");
        Check(titanomagnetite.Name == "Titaniferous magnetite" && titanomagnetite.Hosts.SequenceEqual(new[] { HostRock.Mafic, HostRock.Ultramafic }) && titanomagnetite.Required == GeologicalHistory.Intrusive && titanomagnetite.Composition.Any(c => c.Material == MaterialId.Iron && c.PartsPerTenThousand == 5200) && titanomagnetite.Composition.Any(c => c.Material == MaterialId.Titanium && c.PartsPerTenThousand == 1200), "titaniferous magnetite specification");
        Check(bog.Name == "Bog iron" && bog.Domain == 7 && bog.Composition[0].PartsPerTenThousand == 2800, "bog iron specification");

        // Incompatible host rejection
        Check(!bif.Hosts.Contains(HostRock.Granite) && !bif.Hosts.Contains(HostRock.Clastic) && !bif.Hosts.Contains(HostRock.Carbonate), "BIF rejects granite and sedimentary hosts");
        Check(!ironstone.Hosts.Contains(HostRock.Granite) && !ironstone.Hosts.Contains(HostRock.Basement) && !ironstone.Hosts.Contains(HostRock.Mafic), "ironstone rejects igneous and basement hosts");
        Check(!weathered.Hosts.Contains(HostRock.Granite) && !weathered.Hosts.Contains(HostRock.Clastic) && !weathered.Hosts.Contains(HostRock.Carbonate), "weathered iron rejects granite and sedimentary hosts");
        Check(!titanomagnetite.Hosts.Contains(HostRock.Granite) && !titanomagnetite.Hosts.Contains(HostRock.Clastic), "titanomagnetite rejects felsic and sedimentary hosts");

        // Domain 7 wetland checks
        var desertTile = world.Tiles.FirstOrDefault(t => t.Environment == EnvironmentKind.Desert);
        if (desertTile != null) Check(!bog.Eligible(world, desertTile.Id), "bog iron rejects desert tiles");
        var iceTile = world.Tiles.FirstOrDefault(t => t.Environment == EnvironmentKind.Ice);
        if (iceTile != null) Check(!bog.Eligible(world, iceTile.Id), "bog iron rejects ice tiles");

        // 3. World deposit verification for iron
        var ironBodies = r.Bodies.Where(b => b.Components.Any(c => c.Material == MaterialId.Iron)).ToArray();
        Check(ironBodies.Length > 0, "iron deposits and occurrences exist");
        Check(ironBodies.Any(b => b.Profile == 8), "BIF deposits generated");
        Check(ironBodies.Any(b => b.Profile == 37 || b.Profile == 38 || b.Profile == 39 || b.Profile == 40), "new iron pathways generated");

        // Bog iron is surface-exposed and modest in reserve size
        var bogBodies = r.Bodies.Where(b => b.Profile == 40).ToArray();
        if (bogBodies.Length > 0)
        {
            Check(bogBodies.All(b => b.Sites.All(s => s.Burial == BurialDepth.Surface && s.Exposure == 10000)), "bog iron is strictly surface-exposed");
            var bogRich = bogBodies.Where(b => b.Concentration == MineralConcentration.Deposit).ToArray();
            var bifRich = r.Bodies.Where(b => b.Profile == 8 && b.Concentration == MineralConcentration.Deposit).ToArray();
            if (bogRich.Length > 0 && bifRich.Length > 0)
                Check(bifRich.Max(b => b.InitialQuantity) >= 3 * bogRich.Max(b => b.InitialQuantity), "BIF industrial motherlodes far exceed modest bog iron reserves");
        }
    }
    private static void FossilFuelPathwayFixtures()
    {
        var config = new SimulationConfig { Width = 256, Height = 128, Players = 4, StartSeparation = 9 };
        var generator = new WorldGenerator();
        var world = generator.Generate(42, config);
        var r = world.Resources;

        // 1. Profile specifications
        var coalSeam = GeologicalProfiles.All.Single(p => p.Id == 13);
        var paralicCoal = GeologicalProfiles.All.Single(p => p.Id == 41);
        var petroDeep = GeologicalProfiles.All.Single(p => p.Id == 14);
        var petroStrat = GeologicalProfiles.All.Single(p => p.Id == 42);

        Check(coalSeam.Name == "Coal seam" && coalSeam.Hosts.SequenceEqual(new[] { HostRock.Clastic }) && coalSeam.Required == (GeologicalHistory.Basin | GeologicalHistory.Peat | GeologicalHistory.Mature) && coalSeam.Domain == 0 && coalSeam.Shape == DepositShape.Seam, "coal seam specification");
        Check(paralicCoal.Name == "Paralic coal basin" && paralicCoal.Hosts.SequenceEqual(new[] { HostRock.Clastic }) && paralicCoal.Required == (GeologicalHistory.Basin | GeologicalHistory.Peat) && paralicCoal.Domain == 0 && paralicCoal.Shape == DepositShape.Bed && paralicCoal.Composition[0].PartsPerTenThousand == 8500, "paralic coal basin specification");
        Check(petroDeep.Name == "Petroleum system" && petroDeep.Hosts.SequenceEqual(new[] { HostRock.Clastic, HostRock.Carbonate }) && petroDeep.Required == (GeologicalHistory.Basin | GeologicalHistory.Source | GeologicalHistory.Mature | GeologicalHistory.Reservoir | GeologicalHistory.SealTrap | GeologicalHistory.Preserved) && petroDeep.Domain == 1 && petroDeep.Shape == DepositShape.Reservoir, "deep petroleum system specification");
        Check(petroStrat.Name == "Stratigraphic petroleum & surface seeps" && petroStrat.Hosts.SequenceEqual(new[] { HostRock.Clastic, HostRock.Carbonate }) && petroStrat.Required == (GeologicalHistory.Basin | GeologicalHistory.Source | GeologicalHistory.Preserved) && petroStrat.Domain == 1 && petroStrat.Shape == DepositShape.Bed && petroStrat.Composition[0].PartsPerTenThousand == 8000, "stratigraphic petroleum specification");

        // Host restrictions
        Check(!coalSeam.Hosts.Contains(HostRock.Granite) && !coalSeam.Hosts.Contains(HostRock.Basement) && !coalSeam.Hosts.Contains(HostRock.Carbonate), "coal rejects igneous and carbonate hosts");
        Check(!paralicCoal.Hosts.Contains(HostRock.Granite) && !paralicCoal.Hosts.Contains(HostRock.Basement) && !paralicCoal.Hosts.Contains(HostRock.Carbonate), "paralic coal rejects igneous and carbonate hosts");
        Check(!petroDeep.Hosts.Contains(HostRock.Granite) && !petroDeep.Hosts.Contains(HostRock.Basement) && !petroDeep.Hosts.Contains(HostRock.Mafic), "petroleum system rejects igneous and basement hosts");
        Check(!petroStrat.Hosts.Contains(HostRock.Granite) && !petroStrat.Hosts.Contains(HostRock.Basement) && !petroStrat.Hosts.Contains(HostRock.Mafic), "stratigraphic petroleum rejects igneous and basement hosts");

        // Deposits in world
        var coalBodies = r.Bodies.Where(b => b.Profile == 13 || b.Profile == 41).ToArray();
        Check(coalBodies.Length > 0, "coal deposits generated");
        Check(coalBodies.Any(b => b.Profile == 13), "bituminous coal seams generated");
        Check(coalBodies.Any(b => b.Profile == 41), "paralic coal beds generated");

        var petroBodies = r.Bodies.Where(b => b.Profile == 14 || b.Profile == 42).ToArray();
        Check(petroBodies.Length > 0, "petroleum bodies generated");
        Check(petroBodies.Any(b => b.Profile == 14), "deep structural petroleum reservoirs generated");
        Check(petroBodies.Any(b => b.Profile == 42), "stratigraphic petroleum & seeps generated");

        // Scattered surface petroleum seeps
        var seeps = r.Bodies.Where(b => b.Profile == 42 && b.Concentration == MineralConcentration.Scattered).ToArray();
        if (seeps.Length > 0)
        {
            Check(seeps.All(b => b.Sites.All(s => s.Burial == BurialDepth.Surface && s.Exposure == 10000)), "scattered petroleum seeps are strictly surface-exposed");
        }
    }
    public static void Audit(bool large)
    {
        foreach (var shape in Enum.GetValues(typeof(WorldShape)).Cast<WorldShape>())
            foreach (int seed in new[] { 0, 1, 5, 42, 1729, 31415 })
            {
                var c = SimulationConfig.Small(4); c.Shape = shape;
                var w = new WorldGenerator().Generate(seed, c);
                ResourceValidation.Validate(w);
                Check(w.Resources.Bodies.All(b => Connected(w, b)), "audit connected deposits");
                Console.WriteLine("GEOLOGY_SMALL " + shape + " seed=" + seed + " bodies=" + w.Resources.Bodies.Length + " packedMiB=" + (w.Resources.PackedBytes / 1048576.0).ToString("F2"));
            }
        if (large)
        {
            var c = new SimulationConfig { Width = 2048, Height = 1024, Players = 80, BenchmarkScale = true, Shape = WorldShape.Archipelagos };
            var timer = System.Diagnostics.Stopwatch.StartNew(); var w = new WorldGenerator().Generate(42, c);
            long heap = GC.GetTotalMemory(true); double overlay = w.Generation.Stages.Select((s, i) => new { s, i }).Where(x => x.s == "Latent geological provinces" || x.s == "Finite geological deposits" || x.s == "Surface materials and woody capacity" || x.s == "Resource opportunity audit").Sum(x => w.Generation.Milliseconds[x.i]);
            Check(w.Resources.PackedBytes <= 64L * 1048576 && heap < c.GenerationBudgetMegabytes * 1048576L, "large-world resource and total memory budgets");
            Console.WriteLine("GEOLOGY_LARGE Archipelagos seed=42 players=80 bodies=" + w.Resources.Bodies.Length + " packedMiB=" + (w.Resources.PackedBytes / 1048576.0).ToString("F2") + " heapMiB=" + (heap / 1048576.0).ToString("F2") + " overlayMs=" + overlay.ToString("F1") + " totalSeconds=" + timer.Elapsed.TotalSeconds.ToString("F2"));
            var saved = SaveCodec.Save(w); string original = ResourceCodec.Fingerprint(w.Resources); w = null; GC.Collect();
            var loaded = SaveCodec.Load(saved); Check(ResourceCodec.Fingerprint(loaded.Resources) == original, "large-world resource save round trip");
            Console.WriteLine("GEOLOGY_LARGE saveBytes=" + saved.Length + " resource round trip passed");
        }
    }
    private static void OffshoreFixtures()
    {
        var config = SimulationConfig.Small(1); int count = config.Width * config.Height;
        var w = new WorldState { Seed = 42, Config = config, Generation = new GenerationReport(), Tiles = new Tile[count] };
        var options = new ResourceGenerationOptions { Density = 4 };
        w.Resources = new WorldResourceState { CatalogHash = ResourceCatalog.Hash, ProfileHash = GeologicalProfiles.Hash, ConfigurationHash = options.Hash, MaximumQuantity = 100000, WoodRecoveryPerYear = .05,
            Provinces = Array.Empty<GeologicalProvince>(), Province = new ushort[count], Host = Enumerable.Repeat((byte)HostRock.Clastic, count).ToArray(), History = Enumerable.Repeat(ushort.MaxValue, count).ToArray(), Marine = new byte[count], Stone = new ushort[count], Clay = new ushort[count], Wood = new ushort[count] };
        for (int i = 0; i < count; i++)
        {
            bool land = i % config.Width < config.Width / 2;
            w.Tiles[i] = new Tile { Id = i, Environment = land ? EnvironmentKind.Grassland : EnvironmentKind.Ocean, WaterBody = land ? WaterKind.None : WaterKind.Saltwater };
            w.Resources.Marine[i] = (byte)(land ? MarineSetting.Land : i % config.Width < config.Width * 3 / 4 ? MarineSetting.Shelf : MarineSetting.AbyssalPlain);
        }
        new MineralDepositStage(new[] { GeologicalProfiles.All.Single(p => p.Id == 14) }).Generate(Context(w, options));
        var r = w.Resources;
        Check(r.Bodies.Any(b => b.Sites.Any(s => s.Setting == MarineSetting.Land) && b.Sites.Any(s => s.Setting == MarineSetting.Shelf)), "one reservoir can cross the coastline");
        Check(r.Bodies.Any(b => b.Sites.Any(s => s.Setting == MarineSetting.AbyssalPlain)), "deep-water petroleum requires complete basin context");
        Check(r.Bodies.All(b => b.Sites.Sum(s => (int)s.Share) == 10000), "cross-coast reserves not duplicated");
        Check(r.Bodies.Any(b => b.Sites.Any(s => s.Setting == MarineSetting.Shelf && s.Burial == BurialDepth.Deep)), "water depth independent of burial depth");
        ResourceValidation.Validate(w);
        Array.Clear(r.History, 0, count);
        new MineralDepositStage(new[] { GeologicalProfiles.All.Single(p => p.Id == 14) }).Generate(Context(w, options));
        Check(r.Bodies.Length == 0, "ocean alone cannot create petroleum");
        var nodule = GeologicalProfiles.All.Single(p => p.Id == 33); var crust = GeologicalProfiles.All.Single(p => p.Id == 34); var sulfide = GeologicalProfiles.All.Single(p => p.Id == 35);
        int id = count - 1; r.History[id] = ushort.MaxValue;
        Check(nodule.Eligible(w, id) && !crust.Eligible(w, id) && !sulfide.Eligible(w, id), "nodules require sediment-covered abyssal setting");
        r.Host[id] = (byte)HostRock.Mafic; r.Marine[id] = (byte)MarineSetting.Seamount;
        Check(crust.Eligible(w, id) && !nodule.Eligible(w, id) && !sulfide.Eligible(w, id), "crusts require rocky seamount setting");
        r.Marine[id] = (byte)MarineSetting.HydrothermalRidge;
        Check(sulfide.Eligible(w, id) && !nodule.Eligible(w, id) && !crust.Eligible(w, id), "sulfides require hydrothermal setting");
    }
}
