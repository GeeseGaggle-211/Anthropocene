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
        Check(!ResourceCatalog.Discoverable(MaterialId.MethaneHydrate, new HashSet<string> { "HydrateExtraction" }), "reserved profiles do not grant capabilities");
        foreach (var shape in Enum.GetValues(typeof(WorldShape)).Cast<WorldShape>())
        {
            var config = SimulationConfig.Small(4); config.Shape = shape;
            var generator = new WorldGenerator();
            var before = generator.Generate(42, config, new ResourceGenerationOptions { Enabled = false });
            var world = generator.Generate(42, config); var r = world.Resources;
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
        small.Resources.Version++;
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
    private static void OccurrenceFixtures()
    {
        var config = new SimulationConfig { Width = 256, Height = 128, Players = 4, StartSeparation = 9 };
        var generator = new WorldGenerator(); var w = generator.Generate(42, config); var r = w.Resources;
        var without = generator.Generate(42, config, new ResourceGenerationOptions { ScatteredDensity = 0 });
        var fields = r.Bodies.Where(b => b.Concentration == MineralConcentration.Scattered).ToArray();
        Check(fields.Length > 0 && fields.Any(b => b.Sites.Length > 10), "low-quantity fields cover broad districts");
        Check(fields.All(b => b.InitialQuantity <= 8000 && b.Sites.All(s => b.InitialQuantity * s.Share / 10000 <= 101)), "scattered local host stocks remain small before component fractions");
        Check(fields.All(b => MineralDepositStage.HasScatteredOccurrences(b.Profile) && b.Sites.All(s => w.Tiles[s.Tile].Land && s.Exposure == 10000)), "scattered solids respect land/exposure and exclude fuel systems");
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
