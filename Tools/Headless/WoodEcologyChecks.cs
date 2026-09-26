using System;
using System.Collections.Generic;
using System.Linq;
using Anthropocene.Simulation;

internal static class WoodEcologyChecks
{
    private static int checks;
    private static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception("Wood ecology: " + message); }
    private static void Near(double actual, double expected, string message, double tolerance = 1e-8) => Check(Math.Abs(actual - expected) <= tolerance, message + " actual=" + actual + " expected=" + expected);
    private static WorldState Fixture(double condition = 1, VegetationTier vegetation = VegetationTier.High)
    {
        var w = NeolithicChecks.Fixture(); var t = w.Tiles[w.Camps[0].Tile];
        t.Environment = EnvironmentKind.Temperate; t.NaturalVegetationPotential = VegetationTier.High; t.CurrentVegetation = vegetation; t.LandCondition = condition;
        int count = w.Tiles.Length;
        w.Resources = new WorldResourceState { MaximumQuantity = 100000, CatalogHash = ResourceCatalog.Hash, ProfileHash = ResourceCatalog.Hash, ConfigurationHash = ResourceCatalog.Hash, Provinces = Array.Empty<GeologicalProvince>(), Province = new ushort[count], History = new ushort[count], Host = new byte[count], Marine = new byte[count], Stone = new ushort[count], Clay = new ushort[count], Wood = new ushort[count], WoodRecoveryPerYear = .05 };
        w.Resources.Wood[t.Id] = 100; w.Resources.Stone[t.Id] = 100; w.Resources.Clay[t.Id] = 100;
        return w;
    }
    private static MaterialSourceState WoodState(WorldState w, double remaining, double days = 0)
    {
        int tile = w.Camps[0].Tile;
        var state = new MaterialSourceState { Id = "surface:" + tile + ":" + (int)MaterialId.Wood, Material = MaterialId.Wood, Initial = 250, Remaining = remaining, LastDays = days };
        w.MaterialSources.Add(state); return state;
    }
    private static double Wood(WorldState w, double days) => MaterialEconomy.KnownSources(w, w.Camps[0], days).Single(s => s.Material == MaterialId.Wood).Available;
    private static void Commit(WorldState w, TileEcologyResult result)
    {
        int tile = w.Camps[0].Tile; result.View.Commit(w.Tiles[tile]);
        if (result.HasWoodSource) MaterialEconomy.CommitWoodProjection(w, tile, result.WoodRemaining, result.WoodDays);
    }
    public static int Run()
    {
        checks = 0; CapacityAndRecovery(); MaterializationAndSave(); ActiveBarrier(); ReceiptAndFiniteMaterials(); VirginWoodCultivation();
        Console.WriteLine("PASS: " + checks + " Wood ecology assertions"); return checks;
    }
    private static void CapacityAndRecovery()
    {
        var healthy = Fixture(); var healthyState = WoodState(healthy, 0);
        var degraded = Fixture(.1, VegetationTier.Low); var degradedState = WoodState(degraded, 0);
        double healthyYear = Wood(healthy, 365), degradedYear = Wood(degraded, 365);
        Check(degradedYear > 0 && degradedYear < healthyYear, "degraded Low vegetation recovers toward reduced support before its capacity binds");
        Near(degradedState.Remaining, 0, "KnownSources Wood projection is pure"); Near(degradedState.LastDays, 0, "pure Wood projection preserves source timestamp");
        Near(Wood(Fixture(.1, VegetationTier.Low), 0), 250.0 / 30.0, "unextracted Wood cannot exceed current vegetation and condition support");
        var overcapacity = Fixture(.1, VegetationTier.Low); var overcapacityState = WoodState(overcapacity, 250);
        Near(Wood(overcapacity, 0), 250.0 / 30.0, "equal-clock existing Wood state cannot exceed current support"); Near(overcapacityState.Remaining, 250, "equal-clock capacity projection remains pure");
        var recovering = Fixture(.1, VegetationTier.Low); recovering.Config.ConditionRecoveryPerYear = Math.Log(2); recovering.Config.Validate(); WoodState(recovering, 0);
        double early = Wood(recovering, 365), late = Wood(recovering, 3650);
        Check(late > early && late <= MaterialEconomy.WoodCapacity(250, recovering.Tiles[recovering.Camps[0].Tile], LazyEcology.ProjectEcology(recovering, recovering.Camps[0].Tile, 3650), recovering.Config), "authoritative ecological recovery permits Wood recovery without exceeding support");
        Near(healthyYear, 250 * (1 - Math.Exp(-.05)), "healthy aligned source preserves former recovery trajectory");
    }
    private static void MaterializationAndSave()
    {
        var agreement = Fixture(.1, VegetationTier.Low); WoodState(agreement, 0);
        var projected = LazyEcology.ProjectEcology(agreement, agreement.Camps[0].Tile, 20 * 365); double projectedWood = Wood(agreement, 20 * 365);
        var materialized = LazyEcology.MaterializeEcology(agreement, agreement.Camps[0].Tile, 20 * 365);
        Near(materialized.LandCondition, projected.LandCondition, "Wood companion projection preserves ProjectEcology land condition"); Near(materialized.Food, projected.Food, "Wood companion projection preserves ProjectEcology food"); Near(agreement.MaterialSources.Single().Remaining, projectedWood, "Wood source projection agrees with materialization", 1e-7);
        var once = Fixture(.1, VegetationTier.Low); WoodState(once, 0); LazyEcology.MaterializeEcology(once, once.Camps[0].Tile, 20 * 365);
        var split = Fixture(.1, VegetationTier.Low); WoodState(split, 0);
        for (double day = split.Config.BaseDays; day <= 20 * 365; day += split.Config.BaseDays) LazyEcology.MaterializeEcology(split, split.Camps[0].Tile, day);
        Near(split.MaterialSources.Single().Remaining, once.MaterialSources.Single().Remaining, "Wood recovery is invariant across authoritative seasonal materialization partitions", 1e-7);
        var saved = Fixture(.1, VegetationTier.Low); WoodState(saved, 0); LazyEcology.MaterializeEcology(saved, saved.Camps[0].Tile, 10 * 365); saved.Days = 10 * 365;
        var loaded = SaveCodec.Load(SaveCodec.Save(saved)); saved.Days = loaded.Days = 20 * 365; LazyEcology.MaterializeEcology(saved, saved.Camps[0].Tile, 20 * 365); LazyEcology.MaterializeEcology(loaded, loaded.Camps[0].Tile, 20 * 365);
        Near(loaded.MaterialSources.Single().Remaining, saved.MaterialSources.Single().Remaining, "Wood ecology/source continuation survives save/load", 1e-7);
        var stale = Fixture(.1, VegetationTier.Low); var staleState = WoodState(stale, 100, 0); stale.Tiles[stale.Camps[0].Tile].LastEcologyUpdateDays = stale.Days = 365;
        string fingerprint = SaveCodec.Fingerprint(stale); double available = Wood(stale, 365);
        Check(available <= 250.0 / 30.0 + 1e-8 && staleState.Remaining == 100 && staleState.LastDays == 0 && SaveCodec.Fingerprint(stale) == fingerprint, "stale saved source clamps only in pure projection without inventing history or rewriting save state");
    }
    private static void ActiveBarrier()
    {
        var w = Fixture(.5, VegetationTier.Medium); var state = WoodState(w, 0); int tile = w.Camps[0].Tile;
        w.Config.LandUseDamagePerYear = 1; w.Config.SafeUseRatio = 0; w.Config.Validate();
        var result = EcologicalPressure.Integrate(w, tile, new[] { new Demand { Tile = tile, Entity = w.Camps[0].Id, Kind = "Gathering", Food = 100000 } }, 0, w.Config.BaseDays);
        Check(result.HasWoodSource && state.Remaining == 0, "parallel ecology result carries Wood projection without mutating source state");
        Commit(w, result); Check(state.LastDays == w.Config.BaseDays && state.Remaining <= MaterialEconomy.WoodCapacity(state.Initial, w.Tiles[tile], result.View, w.Config) + 1e-8, "sequential ecology barrier commits supported Wood state");
    }
    private static void ReceiptAndFiniteMaterials()
    {
        var w = Fixture(); var c = w.Camps[0]; int tile = c.Tile; var record = new TurnRecord();
        w.Config.Neolithic.TimberConversionPressure = 100; w.Config.Validate();
        var wood = MaterialEconomy.KnownSources(w, c, 0).Single(s => s.Material == MaterialId.Wood);
        double spent = MaterialEconomy.Extract(w, c, wood, WorkforceRoles.Gatherer, 100, 100, 0, record);
        Check(spent > 0 && record.MaterialTransfers.Count == 1, "Wood extraction remains receipted");
        var receipt = record.MaterialTransfers.Single(); Near(receipt.Before - receipt.After, receipt.Amount, "Wood receipt records only the actual source debit");
        double loss = record.Pressure.Single().WoodCapacityLoss; double remaining = Wood(w, 0);
        Check(loss > 0 && Math.Abs(receipt.After - loss - remaining) < 1e-8 && remaining <= MaterialEconomy.WoodCapacity(250, w.Tiles[tile], LazyEcology.ProjectEcology(w, tile, 0), w.Config) + 1e-8, "post-conversion capacity loss is distinct from receipts and caps the source");
        var stone = MaterialEconomy.KnownSources(w, c, 0).Single(s => s.Material == MaterialId.Stone); var clay = MaterialEconomy.KnownSources(w, c, 0).Single(s => s.Material == MaterialId.Clay);
        Near(stone.Available, 1000, "Stone availability remains finite and unchanged"); Near(clay.Available, 1000, "Clay availability remains finite and unchanged");
    }
    private static void VirginWoodCultivation()
    {
        foreach (var vegetation in new[] { VegetationTier.Medium, VegetationTier.High })
        {
            var w = Fixture(1, vegetation); var camp = w.Camps[0]; int id = camp.Tile; var tile = w.Tiles[id];
            tile.NaturalVegetationPotential = vegetation;
            camp.CultivationFocusTile = id;
            // Keep this a single-site expansion so the worker-year budget
            // reaches each transition without competing neighboring fields.
            foreach (int neighbor in CultivationEconomy.Footprint(w, camp).Where(site => site != id))
                for (int season = 0; season < w.Tiles[neighbor].Seasons.Length; season++)
                    w.Tiles[neighbor].Seasons[season].MaxPotentialFood = 0;
            tile.CultivationCapital = Math.Pow(.5 / w.Config.ReferenceIntensity, 2);
            tile.Cultivation = .5f; tile.Cultivated = true; tile.CultivationOperator = camp.Id;
            double initial = w.Resources.SurfaceAccessible(id, MaterialId.Wood);
            Check(initial > 0 && !w.MaterialSources.Any(), vegetation + " starts with generated Wood and no explicit source or extraction");
            Near(Wood(w, 0), initial, vegetation + " q=.5 retains virgin Wood");
            Check(!w.MaterialSources.Any(), vegetation + " Wood query is pure before first cultivation displacement");
            Near(EcologyMath.CultivationVegetationRetention(w.Config, .5), 1, vegetation + " q=.5 retention");
            Near(MaterialEconomy.WoodCapacity(initial, tile, new EcologyView(tile), w.Config), initial, vegetation + " q=.5 capacity");

            foreach (double targetQ in new[] { .75, 1.0 })
            {
                double targetCapital = Math.Pow(targetQ / w.Config.ReferenceIntensity, 2);
                double delta = targetCapital - tile.CultivationCapital;
                double factor = vegetation == VegetationTier.High ? w.Config.Neolithic.ForestBuild : w.Config.Neolithic.WoodlandBuild;
                double workerYears = delta * w.Config.Neolithic.BuildWorkerYearsPerTileE * factor;
                double before = targetQ == .75 ? initial : initial * .5;
                var record = new TurnRecord();
                Check(CultivationEconomy.Expand(w, camp, workerYears, false, record) > 0, vegetation + " expansion reaches q=" + targetQ);
                Near(CultivationEconomy.Intensity(w.Config, tile.CultivationCapital), targetQ, vegetation + " exact expansion intensity", 1e-7);
                var source = w.MaterialSources.Single(s => s.Material == MaterialId.Wood && s.Id == "surface:" + id + ":" + (int)MaterialId.Wood);
                double retention = targetQ == .75 ? .5 : 0;
                Near(EcologyMath.CultivationVegetationRetention(w.Config, targetQ), retention, vegetation + " retained natural cover");
                Near(source.Initial, initial, vegetation + " source retains raster baseline");
                Near(source.Remaining, initial * retention, vegetation + " source stock is permanently displaced at q=" + targetQ, 1e-6);
                Near(Wood(w, 0), source.Remaining, vegetation + " source query agrees with stored reduced stock");
                Near(record.Pressure.Where(p => p.Kind == "Cultivation vegetation displacement" && p.Tile == id).Sum(p => p.WoodCapacityLoss), before - source.Remaining,
                    vegetation + " displacement is recorded from real virgin stock", 1e-6);
                Near(tile.LandCondition, 1, vegetation + " expansion preserves biological land condition");
                Check(tile.CurrentVegetation == vegetation, vegetation + " expansion preserves raw ecological vegetation");
            }

            CultivationEconomy.Factors(w.Config, tile, out double build, out double maintenance);
            Near(build, vegetation == VegetationTier.High ? w.Config.Neolithic.ForestBuild : w.Config.Neolithic.WoodlandBuild,
                vegetation + " raw wooded build multiplier remains in force at q=1");
            Near(maintenance, vegetation == VegetationTier.High ? w.Config.Neolithic.ForestMaintenance : w.Config.Neolithic.WoodlandMaintenance,
                vegetation + " raw wooded maintenance multiplier remains in force at q=1");

            tile.CultivationCapital = 0; tile.Cultivation = 0; tile.Cultivated = false; tile.CultivationOperator = -1;
            LazyEcology.MaterializeEcology(w, id, 0);
            var abandoned = w.MaterialSources.Single(s => s.Material == MaterialId.Wood);
            Near(abandoned.Remaining, 0, vegetation + " abandonment cannot restore Wood instantly");
            LazyEcology.MaterializeEcology(w, id, w.Config.DaysPerYear);
            Check(abandoned.Remaining > 0 && abandoned.Remaining < initial, vegetation + " Wood returns only through elapsed recovery");
            Near(abandoned.Remaining, initial * (1 - Math.Exp(-w.Resources.WoodRecoveryPerYear)),
                vegetation + " one-year regrowth follows the configured recovery rate", 1e-6);
            Near(abandoned.LastDays, w.Config.DaysPerYear, vegetation + " regrowth advances the source clock");
            Near(tile.LandCondition, 1, vegetation + " regrowth leaves land condition unchanged");
        }
    }
}
