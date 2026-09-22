using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    [Serializable]
    public sealed class MaterialSourceState
    {
        public string Id;
        public MaterialId Material;
        public double Initial, Remaining, LastDays;
    }
    [Serializable]
    public sealed class MaterialKnowledge
    {
        public int Society, Tile;
        public string Source;
    }
    [Serializable]
    public sealed class MaterialReceipt
    {
        public int Camp, Society, Tile;
        public string Source, Role;
        public MaterialId Material;
        public double Amount, Before, After, WorkerIntervals, Days;
    }
    [Serializable]
    public sealed class MaterialSourceObservation
    {
        public string Id;
        public int Tile, Distance;
        public MaterialId Material;
        public double Available;
    }
    [Serializable]
    public sealed class KnownMaterialSource
    {
        public string Id;
        public int Tile, Distance;
        public MaterialId Material;
        public double Initial, Available, Share, RecoveryPerYear;
    }
    public static class MaterialEconomy
    {
        public static MaterialSourceObservation[] Observe(WorldState w, Camp c, double days) => KnownSources(w, c, days).Select(s => new MaterialSourceObservation { Id = s.Id, Tile = s.Tile, Distance = s.Distance, Material = s.Material, Available = s.Available }).ToArray();
        public static readonly MaterialId[] Materials = { MaterialId.Stone, MaterialId.Clay, MaterialId.Wood };
        // Packed geological rasters remain provenance. Sparse fractional continuation is
        // authoritative after first extraction; all economic queries pass through here.
        public static KnownMaterialSource[] KnownSources(WorldState w, Camp c, double days)
        {
            if (w.Resources == null) return Array.Empty<KnownMaterialSource>();
            var r = w.Resources; var result = new List<KnownMaterialSource>();
            var visible = Knowledge.Visible(w, c.Player);
            var sites = w.Map.Radius(c.Tile, 1).Where(id => w.Tiles[id].TraversableLand && visible.Contains(id)).OrderBy(id => id).ToArray();
            foreach (int tile in sites)
                foreach (var material in Materials)
                {
                    if (!string.IsNullOrEmpty(r.DiscoveryTechnologies[(int)material]) && !c.People.HasTech(r.DiscoveryTechnologies[(int)material])) continue;
                    double initial = r.SurfaceAccessible(tile, material);
                    if (initial > 0) Add("surface:" + tile + ":" + (int)material, tile, material, initial, 1, material == MaterialId.Wood ? r.WoodRecoveryPerYear : 0);
                    foreach (var body in r.Bodies.Where(b => b.Sites.Any(s => s.Tile == tile && s.Exposure > 0 && s.Burial != BurialDepth.Deep)).OrderBy(b => b.Id))
                    {
                        var site = body.Sites.First(s => s.Tile == tile);
                        double local = r.AccessibleAt(body, site, material);
                        double total = body.Sites.Sum(s => r.AccessibleAt(body, s, material));
                        if (local > 0 && total > 0) Add("body:" + body.Id + ":" + (int)material, tile, material, total, local / total, 0);
                    }
                }
            return result.ToArray();
            void Add(string id, int tile, MaterialId material, double initial, double share, double recovery)
            {
                var state = w.MaterialSources.FirstOrDefault(s => s.Id == id);
                double remaining = state == null ? initial : recovery > 0 ? initial + (state.Remaining - initial) * Math.Exp(-recovery * Math.Max(0, days - state.LastDays) / w.Config.DaysPerYear) : state.Remaining;
                result.Add(new KnownMaterialSource { Id = id, Tile = tile, Material = material, Initial = initial, Share = share, Available = remaining * share, RecoveryPerYear = recovery, Distance = w.Map.Distance(c.Tile, tile) });
            }
        }
        public static double Extract(WorldState w, Camp c, KnownMaterialSource requested, string role, double workerIntervals, double useful, double days, TurnRecord record)
        {
            if (!CultivationEconomy.Enabled(c) || workerIntervals <= 0 || useful <= 0) return 0;
            var source = KnownSources(w, c, days).FirstOrDefault(s => s.Id == requested.Id && s.Tile == requested.Tile && s.Material == requested.Material);
            if (source == null) return 0;
            bool specialist = role == Specialist(source.Material) && c.People.HasTech(EconomicIntents.RequiredTechnology(role));
            if (!specialist && role != WorkforceRoles.Gatherer) return 0;
            double rate = w.Config.Neolithic.SpecialistMaterialExtractionRate * (specialist ? 1 : w.Config.Neolithic.GeneralistTaskEfficiency);
            double amount = Math.Min(source.Available, Math.Min(useful, workerIntervals * rate));
            if (amount <= 1e-12) return 0;
            var state = w.MaterialSources.FirstOrDefault(s => s.Id == source.Id);
            if (state == null) { state = new MaterialSourceState { Id = source.Id, Material = source.Material, Initial = source.Initial, Remaining = source.Initial, LastDays = days }; w.MaterialSources.Add(state); }
            double before = source.Available / source.Share;
            state.Remaining = before - amount; state.LastDays = days;
            if (source.Material == MaterialId.Stone) c.Inventory.Stone += amount; else if (source.Material == MaterialId.Clay) c.Inventory.Clay += amount; else c.Inventory.Wood += amount;
            if (!w.MaterialKnowledge.Any(k => k.Society == c.SocietyId && k.Source == source.Id && k.Tile == source.Tile)) w.MaterialKnowledge.Add(new MaterialKnowledge { Society = c.SocietyId, Source = source.Id, Tile = source.Tile });
            record.MaterialTransfers.Add(new MaterialReceipt { Camp = c.Id, Society = c.SocietyId, Tile = source.Tile, Source = source.Id, Material = source.Material, Role = role, Amount = amount, Before = before, After = state.Remaining, WorkerIntervals = amount / rate, Days = days });
            if (source.Material == MaterialId.Wood)
            {
                double fraction = amount / Math.Max(1e-12, source.Initial);
                EcologicalPressure.ApplyConversion(w, source.Tile, c.Id, "Timber extraction", "Wood", useful, amount, fraction * w.Config.Neolithic.TimberConversionPressure, days, record);
            }
            return amount / rate;
        }
        public static void Gather(WorldState w, Camp c, CraftRecipe[] recipes, double intervals, double years, double generalistIntervals, ref double equipment, TurnRecord record, bool potsAlreadyDecayed = false)
        {
            double days = record.StartDays + record.Duration;
            double housingForecast = 0;
            var forecast = new Dictionary<MaterialId, double> { [MaterialId.Stone] = 0, [MaterialId.Clay] = 0, [MaterialId.Wood] = 0 };
            var planned = Forecast(w, c, recipes, intervals, years, equipment, false, -1, potsAlreadyDecayed);
            foreach (var recipe in recipes)
            {
                double amount = planned[recipe.Good];
                forecast[MaterialId.Stone] += amount * recipe.Stone;
                forecast[MaterialId.Clay] += amount * recipe.Clay;
                forecast[MaterialId.Wood] += amount * recipe.Wood;
                if (recipe.ConstructionMaterial > 0)
                {
                    housingForecast += amount * recipe.ConstructionMaterial;
                }
            }
            double Shortfall(MaterialId material)
            {
                double horizon = 1 + w.Config.Neolithic.MaterialReserveHorizonTurns;
                double direct = Math.Max(0, forecast[material] * horizon - Inventory(c, material));
                double spareWood = Math.Max(0, c.Inventory.Wood - forecast[MaterialId.Wood] * horizon);
                double spareClay = Math.Max(0, c.Inventory.Clay - forecast[MaterialId.Clay] * horizon);
                return direct + (material == MaterialId.Stone ? 0 : Math.Max(0, housingForecast * horizon - spareWood - spareClay));
            }
            foreach (string role in WorkforceRoles.Extractors.Concat(new[] { WorkforceRoles.Gatherer }))
            {
                double labor = role == WorkforceRoles.Gatherer ? generalistIntervals : CultivationEconomy.Workers(c, role) * intervals;
                while (labor > 1e-10)
                {
                    var candidates = KnownSources(w, c, days).Where(s => s.Available > 1e-10 && (role == WorkforceRoles.Gatherer || role == Specialist(s.Material)));
                    var selected = candidates.Select(s => new { Source = s, Demand = Shortfall(s.Material), Priority = s.Material == MaterialId.Stone ? c.StonePriority : s.Material == MaterialId.Clay ? c.ClayPriority : c.WoodPriority }).Where(x => x.Demand * x.Priority > 1e-10).OrderByDescending(x => x.Demand * x.Priority).ThenBy(x => x.Source.Distance).ThenBy(x => x.Source.Id, StringComparer.Ordinal).FirstOrDefault();
                    if (selected == null) break;
                    // One BaseDays worker interval, reassessing after each source receipt.
                    double spent = Extract(w, c, selected.Source, role, Math.Min(labor, 1), selected.Demand, days, record);
                    if (spent <= 1e-12) break;
                    labor -= spent;
                }
            }
        }
        public static double Inventory(Camp c, MaterialId material) => material == MaterialId.Stone ? c.Inventory.Stone : material == MaterialId.Clay ? c.Inventory.Clay : c.Inventory.Wood;
        public static string Specialist(MaterialId material) => material == MaterialId.Stone ? WorkforceRoles.Stonecutter : material == MaterialId.Clay ? WorkforceRoles.ClayMiner : material == MaterialId.Wood ? WorkforceRoles.Woodcutter : null;
        // Mirror the ordered recipe/role plan without changing stock or gaining progress.
        // Each worker and tool interval is forecast only once, not once per recipe.
        public static Dictionary<CraftGood, double> Forecast(WorldState w, Camp c, CraftRecipe[] recipes, double intervals, double years, double equipment, bool includePlantDemand = false, double residentPopulation = -1, bool potsAlreadyDecayed = false)
        {
            var planned = recipes.ToDictionary(r => r.Good, r => 0.0);
            var tools = new ToolCoverageBudget(w, c, intervals, equipment);
            double plant = includePlantDemand ? double.PositiveInfinity : c.PlantFood;
            foreach (string role in WorkforceRoles.Specialists.Concat(new[] { WorkforceRoles.Craftsperson }))
            {
                double labor = CultivationEconomy.Workers(c, role) * intervals;
                foreach (var recipe in recipes.OrderByDescending(r => NeolithicProduction.Priority(c, r.Good)).ThenBy(r => r.Good))
                {
                    if (NeolithicProduction.Priority(c, recipe.Good) <= 0) continue;
                    bool specialist = role == recipe.Specialist && c.People.HasTech(recipe.Track);
                    if (!specialist && (role != WorkforceRoles.Craftsperson || recipe.SpecialistOnly)) continue;
                    double rate = recipe.Rate * (specialist ? 1 : w.Config.Neolithic.GeneralistTaskEfficiency);
                    if (rate <= 0) continue;
                    double amount = Math.Min(Math.Max(0, NeolithicProduction.Demand(w, c, recipe.Good, years, residentPopulation, potsAlreadyDecayed) - planned[recipe.Good]), recipe.Equipment ? tools.Capacity(recipe.Good, rate, labor) : labor * rate);
                    if (recipe.PlantFood > 0) amount = Math.Min(amount, plant / recipe.PlantFood);
                    if (amount <= 0) continue;
                    labor -= recipe.Equipment ? tools.Spend(recipe.Good, rate, amount) : amount / rate;
                    planned[recipe.Good] += amount; plant -= amount * recipe.PlantFood;
                }
            }
            return planned;
        }
    }
}
