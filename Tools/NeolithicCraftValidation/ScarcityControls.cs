// Disclosed one-time source-stock interventions for Issue #33 paired cohorts.
// This is a validation-only mutation of saved accessible source stock, not a
// simulated extraction, labor receipt, or production grant.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;
using Anthropocene.Simulation;

[Serializable]
public sealed class ScarcitySourceReceipt
{
    public string Source;
    public string Scope;
    public MaterialId Material;
    public string Tiles;
    public double Share, SourceStockBefore, Depleted, SourceStockAfter;
    public double LocalAvailableBefore, LocalAvailableAfter;
}

[Serializable]
public sealed class ScarcityControlResult
{
    [JsonIgnore]
    public byte[] Save;
    public MaterialId Material;
    public int Camp, Turn;
    public double RetainedFraction, AvailableBefore, AvailableAfter, Depleted;
    public string InterventionId, Scenario;
    public List<ScarcitySourceReceipt> Receipts = new List<ScarcitySourceReceipt>();
}

public static class ScarcityControls
{
    public const double DefaultRetainedFraction = 0.0;

    /// <summary>
    /// Clones one activity-driven unlock checkpoint and applies a single
    /// reduction to each unique, locally known source pool of the requested
    /// material. Deep geological stocks, source initial budgets, renewal rates,
    /// terrain, IDs, and the captured camp inventory remain unchanged.
    /// </summary>
    public static ScarcityControlResult Apply(byte[] unlockSave, int campId, MaterialId material,
        double retainedFraction = DefaultRetainedFraction)
    {
        if (unlockSave == null || unlockSave.Length == 0) throw new ArgumentException("An unlock save is required.", nameof(unlockSave));
        if (!MaterialEconomy.Materials.Contains(material)) throw new ArgumentException("Only Stone, Clay, or Wood can be depleted.", nameof(material));
        if (double.IsNaN(retainedFraction) || double.IsInfinity(retainedFraction) || retainedFraction < 0 || retainedFraction >= 1)
            throw new ArgumentOutOfRangeException(nameof(retainedFraction), "Retained fraction must be in [0,1).");

        var world = SaveCodec.Load(unlockSave);
        var camp = world.Camps.FirstOrDefault(c => c.Id == campId)
            ?? throw new InvalidOperationException("The unlock checkpoint does not contain camp " + campId + ".");

        // Wood availability is an ecological projection. Commit only its
        // already-authoritative boundary projection so the intervention's
        // sparse source clock starts at the same time and future renewal is
        // neither lost nor counted twice.
        if (material == MaterialId.Wood)
            foreach (int tile in world.Map.Radius(camp.Tile, 1).Where(id => world.Tiles[id].TraversableLand).OrderBy(id => id))
                LazyEcology.MaterializeEcology(world, tile, world.Days);

        var before = MaterialEconomy.KnownSources(world, camp, world.Days)
            .Where(s => s.Material == material && s.Available > 1e-12 && s.Share > 1e-12)
            .OrderBy(s => s.Id, StringComparer.Ordinal).ThenBy(s => s.Tile).ToArray();
        if (before.Length == 0) throw new InvalidOperationException("No locally known accessible " + material + " source exists at the captured unlock boundary.");

        world.MaterialSources ??= new List<MaterialSourceState>();
        var result = new ScarcityControlResult
        {
            Material = material, Camp = camp.Id, Turn = world.Turn, RetainedFraction = retainedFraction,
            InterventionId = "unlock-stock-shock-" + material + "-camp" + camp.Id + "-turn" + world.Turn,
            Scenario = "one-time exogenous depletion of every unique local KnownSources pool to " +
                (retainedFraction * 100).ToString("R", CultureInfo.InvariantCulture) + "% remaining; not extraction/labor; deep stock and renewal preserved"
        };

        foreach (var group in before.GroupBy(s => s.Id, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var source = group.First();
            double sourceBefore = source.Available / source.Share;
            if (group.Any(s => !Near(s.Available / s.Share, sourceBefore)))
                throw new InvalidOperationException("Inconsistent local shares for source " + group.Key + ".");
            double localBefore = group.Sum(s => s.Available);
            var state = world.MaterialSources.FirstOrDefault(s => s.Id == source.Id);
            if (state == null)
            {
                state = new MaterialSourceState { Id = source.Id, Material = material, Initial = source.Initial };
                world.MaterialSources.Add(state);
            }
            if (state.Material != material || state.Initial <= 0)
                throw new InvalidOperationException("Invalid persisted source state for " + source.Id + ".");

            state.Remaining = sourceBefore * retainedFraction;
            state.LastDays = material == MaterialId.Wood ? world.Tiles[source.Tile].LastEcologyUpdateDays : world.Days;
            MaterialEconomy.RegisterSourceState(world, state);

            result.AvailableBefore += localBefore;
            result.Depleted += sourceBefore * (1 - retainedFraction) * group.Sum(s => s.Share);
            result.Receipts.Add(new ScarcitySourceReceipt
            {
                Source = source.Id, Material = material,
                Scope = source.Id.StartsWith("body:", StringComparison.Ordinal)
                    ? "body-pooled accessible stock shared by this deposit's sites; local `Share` values disclose exposed portion"
                    : "tile-local surface accessible stock",
                Tiles = string.Join("|", group.Select(s => s.Tile).Distinct().OrderBy(t => t)),
                Share = group.Sum(s => s.Share), SourceStockBefore = sourceBefore,
                Depleted = sourceBefore * (1 - retainedFraction), SourceStockAfter = state.Remaining,
                LocalAvailableBefore = localBefore, LocalAvailableAfter = localBefore * retainedFraction
            });
        }

        var after = MaterialEconomy.KnownSources(world, camp, world.Days)
            .Where(s => s.Material == material).ToArray();
        result.AvailableAfter = after.Sum(s => s.Available);
        if (!Near(result.AvailableAfter, result.AvailableBefore * retainedFraction))
            throw new InvalidOperationException("The local " + material + " availability shock did not preserve its declared retained fraction.");

        result.Save = SaveCodec.Save(world);
        var reloaded = SaveCodec.Load(result.Save);
        if (!string.Equals(SaveCodec.Fingerprint(world), SaveCodec.Fingerprint(reloaded), StringComparison.Ordinal))
            throw new InvalidOperationException("Scarcity intervention failed exact save/load continuation.");
        var reloadedCamp = reloaded.Camps.First(c => c.Id == camp.Id);
        double reloadedAvailable = MaterialEconomy.KnownSources(reloaded, reloadedCamp, reloaded.Days)
            .Where(s => s.Material == material).Sum(s => s.Available);
        if (!Near(reloadedAvailable, result.AvailableAfter))
            throw new InvalidOperationException("Scarcity intervention availability changed across save/load.");
        return result;
    }

    private static bool Near(double a, double b)
        => Math.Abs(a - b) <= 1e-8 * Math.Max(1, Math.Max(Math.Abs(a), Math.Abs(b)));
}
