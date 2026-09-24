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
    // A local, pure companion to an existing Wood source. It has no ecological
    // fields of its own: every capacity target comes from the authoritative
    // EcologyView supplied by LazyEcology or EcologicalPressure.
    internal sealed class WoodSourceProjection
    {
        private readonly WorldState world;
        private readonly int tile;
        private MaterialSourceState state;
        private readonly double initial;
        private double remaining;
        internal WoodSourceProjection(WorldState world, int tile, MaterialSourceState state)
            : this(world, tile, state, state?.Initial ?? 0)
        {
        }
        internal WoodSourceProjection(WorldState world, int tile, MaterialSourceState state, double initial)
        {
            this.world = world; this.tile = tile; this.state = state; this.initial = Math.Max(0, initial);
            remaining = state == null ? this.initial : Math.Max(0, state.Remaining);
            var stored = new EcologyView(world.Tiles[tile]);
            if (state != null && state.LastDays > stored.Days + 1e-8) throw new InvalidOperationException("Wood source cannot be ahead of authoritative ecology.");
            // Older saves can have a source clock behind a later ecology commit.
            // Do not reconstruct unrecorded ecological history: establish the
            // current authoritative ceiling and continue from this boundary.
            remaining = Math.Min(remaining, MaterialEconomy.WoodCapacity(this.initial, world.Tiles[tile], stored, world.Config));
        }
        internal double Remaining => remaining;
        internal void Recover(EcologyView before, EcologyView after, double days) => Recover(before, after, days, 0);
        internal void Recover(EcologyView before, EcologyView after, double days, double conditionDamagePerDay)
        {
            if (days <= 0) { remaining = Math.Min(remaining, MaterialEconomy.WoodCapacity(initial, world.Tiles[tile], after, world.Config)); return; }
            remaining = MaterialEconomy.RecoverWood(world, world.Tiles[tile], initial, remaining, before, after, days, conditionDamagePerDay);
        }
        internal void Commit()
        {
            if (state == null)
            {
                if (initial <= 0 || world.MaterialSources == null) return;
                string id = "surface:" + tile + ":" + (int)MaterialId.Wood;
                state = world.MaterialSources.FirstOrDefault(s => s.Id == id && s.Material == MaterialId.Wood);
                if (state == null)
                {
                    state = new MaterialSourceState { Id = id, Material = MaterialId.Wood, Initial = initial };
                    world.MaterialSources.Add(state);
                }
            }
            state.Remaining = remaining;
            state.LastDays = world.Tiles[tile].LastEcologyUpdateDays;
        }
    }
    // A turn-local production aid.  It never owns material: inventory and source
    // state remain authoritative.  Its fixed ratio prevents Housing from changing
    // Wood/Clay policy as gathering and earlier specialist work exchange stock.
    public sealed class HousingMaterialPlan
    {
        public readonly double WoodShare, ClayShare;
        private double reservedWood, reservedClay;
        internal HousingMaterialPlan(double woodShare, double clayShare, double directWood, double directClay)
        {
            WoodShare = woodShare; ClayShare = clayShare;
            reservedWood = Math.Max(0, directWood); reservedClay = Math.Max(0, directClay);
        }
        public void CompleteDirect(CraftRecipe recipe, double output)
        {
            if (recipe == null || recipe.ConstructionMaterial > 0 || output <= 0) return;
            reservedWood = Math.Max(0, reservedWood - output * recipe.Wood);
            reservedClay = Math.Max(0, reservedClay - output * recipe.Clay);
        }
        public double LimitHousing(Camp c, double desired, double materialPerHousing, out double wood, out double clay)
        {
            wood = clay = 0;
            if (desired <= 0 || materialPerHousing <= 0 || WoodShare + ClayShare <= 0) return 0;
            double freeWood = Math.Max(0, c.Inventory.Wood - reservedWood);
            double freeClay = Math.Max(0, c.Inventory.Clay - reservedClay);
            double amount = Math.Min(desired, (freeWood + freeClay) / materialPerHousing);
            if (amount <= 1e-12) return 0;
            double total = amount * materialPerHousing;
            wood = Math.Min(freeWood, total * WoodShare);
            clay = Math.Min(freeClay, total * ClayShare);
            double remaining = total - wood - clay;
            double addWood = Math.Min(remaining, freeWood - wood);
            wood += addWood; remaining -= addWood;
            clay += Math.Min(remaining, freeClay - clay);
            return amount;
        }
    }
    public static class MaterialEconomy
    {
        public static MaterialSourceObservation[] Observe(WorldState w, Camp c, double days) => KnownSources(w, c, days).Select(s => new MaterialSourceObservation { Id = s.Id, Tile = s.Tile, Distance = s.Distance, Material = s.Material, Available = s.Available }).ToArray();
        public static readonly MaterialId[] Materials = { MaterialId.Stone, MaterialId.Clay, MaterialId.Wood };
        // Construction is the flexible consumer.  Use all visible, reachable
        // source stock to choose its mix, after retaining the source- and
        // labor-feasible direct recipe plan. The ratio sees current residual
        // material after that committed work, while the mutable reservation
        // below protects only this turn's remaining direct work.
        public static HousingMaterialPlan PlanHousingMaterials(WorldState w, Camp c, CraftRecipe[] recipes, IDictionary<CraftGood, double> planned, double years, double days)
        {
            var sources = KnownSources(w, c, days);
            var room = Materials.ToDictionary(material => material, material => Inventory(c, material) + sources.Where(s => s.Material == material).Sum(s => s.Available));
            double directWood = 0, directClay = 0;
            // Forecast is already role-, tool-, plant- and demand-capped.  Give
            // its direct recipes one deterministic source/material feasibility
            // pass before they can reserve anything from Housing.  In particular,
            // a Tool job lacking Stone cannot reserve its Wood merely because a
            // visible Wood source exists.
            foreach (var recipe in recipes.Where(r => r.ConstructionMaterial <= 0).OrderByDescending(r => NeolithicProduction.Priority(c, r.Good)).ThenBy(r => r.Good))
            {
                if (!planned.TryGetValue(recipe.Good, out double output) || output <= 0) continue;
                if (recipe.Stone > 0) output = Math.Min(output, room[MaterialId.Stone] / recipe.Stone);
                if (recipe.Clay > 0) output = Math.Min(output, room[MaterialId.Clay] / recipe.Clay);
                if (recipe.Wood > 0) output = Math.Min(output, room[MaterialId.Wood] / recipe.Wood);
                if (output <= 1e-12) continue;
                room[MaterialId.Stone] -= output * recipe.Stone;
                room[MaterialId.Clay] -= output * recipe.Clay;
                room[MaterialId.Wood] -= output * recipe.Wood;
                directWood += output * recipe.Wood;
                directClay += output * recipe.Clay;
            }
            double availableWood = Inventory(c, MaterialId.Wood) + sources.Where(s => s.Material == MaterialId.Wood).Sum(s => s.Available);
            double availableClay = Inventory(c, MaterialId.Clay) + sources.Where(s => s.Material == MaterialId.Clay).Sum(s => s.Available);
            HousingShares(availableWood, availableClay, directWood, directClay, out double woodShare, out double clayShare);
            return new HousingMaterialPlan(woodShare, clayShare, directWood, directClay);
        }
        // Keep every forecast and resolver path on the same residual policy.
        // Direct recipe inputs are the committed current-turn demand; the
        // physical reserve horizon remains a gathering target, not a second
        // Housing reservation.
        internal static void HousingShares(double availableWood, double availableClay, double directWood, double directClay, out double woodShare, out double clayShare)
        {
            double wood = Math.Max(0, availableWood - Math.Max(0, directWood));
            double clay = Math.Max(0, availableClay - Math.Max(0, directClay));
            double total = wood + clay;
            woodShare = total > 0 ? wood / total : 0;
            clayShare = total > 0 ? clay / total : 0;
        }
        private static string SurfaceId(int tile, MaterialId material) => "surface:" + tile + ":" + (int)material;
        internal static WoodSourceProjection ProjectWoodSource(WorldState w, int tile)
        {
            var state = w.MaterialSources.FirstOrDefault(s => s.Id == SurfaceId(tile, MaterialId.Wood) && s.Material == MaterialId.Wood);
            if (state != null) return new WoodSourceProjection(w, tile, state);
            // Cultivation can displace a surface stock before its first harvest.
            // Keep that stock virtual during pure forecasts, then persist the
            // same capped projection at the next ecology/material write barrier.
            if (w.Resources == null || CultivationEconomy.Intensity(w.Config, w.Tiles[tile].CultivationCapital) <= 0) return null;
            double initial = w.Resources.SurfaceAccessible(tile, MaterialId.Wood);
            return initial > 0 ? new WoodSourceProjection(w, tile, null, initial) : null;
        }
        // The ordinal fraction is a biological support ratio, not a replacement
        // for the generated Wood raster. A naturally Low tile therefore keeps
        // its generated brush capacity, while High -> Medium -> Low follows the
        // same current-vegetation hysteresis as the rest of ecology.
        internal static double WoodCapacity(double initial, Tile tile, EcologyView ecology, SimulationConfig config)
        {
            if (!tile.Land || initial <= 0) return 0;
            double natural = (int)tile.NaturalVegetationPotential + 1;
            double current = Math.Min((int)tile.NaturalVegetationPotential, (int)ecology.CurrentVegetation) + 1;
            double ecological = Math.Max(0, initial) * Numbers.Clamp(ecology.LandCondition, 0, 1) * current / natural;
            double q = CultivationEconomy.Intensity(config, ecology.CultivationCapital);
            return EcologyMath.EffectiveWoodCapacity(ecological, q, config);
        }
        // Integrate Wood toward ecological support and the q-derived natural-cover
        // cap through this condition segment. Both condition and q change smoothly
        // here; splitting at the retention threshold keeps the stock integration
        // analytic and duration-partition independent.
        internal static double RecoverWood(WorldState w, Tile tile, double initial, double remaining, EcologyView before, EcologyView after, double days, double conditionDamagePerDay)
        {
            double capacityBefore = WoodCapacity(initial, tile, before, w.Config);
            remaining = Math.Min(Math.Max(0, remaining), capacityBefore);
            double woodRate = w.Resources == null ? 0 : Math.Max(0, w.Resources.WoodRecoveryPerYear) / w.Config.DaysPerYear;
            if (woodRate <= 1e-18 || days <= 0) return Math.Min(remaining, WoodCapacity(initial, tile, after, w.Config));
            double vegetation = Math.Min((int)tile.NaturalVegetationPotential, (int)before.CurrentVegetation) + 1;
            vegetation /= (double)((int)tile.NaturalVegetationPotential + 1);
            bool aquatic = tile.Aquatic || tile.WaterBody == WaterKind.Saltwater;
            double recovery = aquatic ? 0 : LazyEcology.LandConditionRecoveryPerDay(w, tile);
            double damage = Math.Max(0, conditionDamagePerDay);
            double q = CultivationEconomy.Intensity(w.Config, before.CultivationCapital);
            double lambda = CultivationDecayRatePerDay(w, tile);
            double retentionStart = w.Config.CultivationVegetationDisplacementStart;
            double firstDays = days;
            if (lambda > 1e-18 && q > retentionStart)
            {
                double crossing = Math.Log(q / retentionStart) / lambda;
                firstDays = Math.Min(days, Math.Max(0, crossing));
            }
            double condition = Numbers.Clamp(before.LandCondition, 0, 1);
            if (firstDays > 0)
            {
                remaining = RecoverWoodInterval(remaining, initial * vegetation, condition, recovery, damage, woodRate, firstDays,
                    q, lambda, lambda > 1e-18 && q > retentionStart, w.Config);
                condition = EcologyMath.Condition(condition, 1, recovery, damage, firstDays);
            }
            double secondDays = days - firstDays;
            if (secondDays > 1e-12)
            {
                double qAtBoundary = lambda > 1e-18 ? q * Math.Exp(-lambda * firstDays) : q;
                remaining = RecoverWoodInterval(remaining, initial * vegetation, condition, recovery, damage, woodRate, secondDays,
                    qAtBoundary, 0, false, w.Config);
            }
            return Math.Min(Math.Max(0, remaining), WoodCapacity(initial, tile, after, w.Config));
        }
        private static double CultivationDecayRatePerDay(WorldState w, Tile tile)
        {
            double halfLife = Math.Max(1e-12, w.Config.Neolithic.DecayHalfLifeYears);
            return Math.Log(2) / halfLife * (1 - Numbers.Clamp(tile.CultivationOperatingFraction, 0, 1)) / w.Config.DaysPerYear / 2;
        }
        private static double RecoverWoodInterval(double remaining, double ecologicalScale, double condition, double recovery, double damage,
            double woodRate, double days, double q, double qDecayPerDay, bool qDecliningAboveThreshold, SimulationConfig config)
        {
            if (days <= 0 || woodRate <= 1e-18) return Math.Max(0, remaining);
            double conditionRate = recovery + damage;
            double equilibrium = conditionRate <= 1e-18 ? condition : recovery / conditionRate;
            double conditionTransient = condition - equilibrium;
            double retentionStart = config.CultivationVegetationDisplacementStart;
            double factor = Math.Max(0, ecologicalScale);
            var coefficients = new List<double>(4);
            var rates = new List<double>(4);
            if (qDecliningAboveThreshold && qDecayPerDay > 1e-18)
            {
                double intercept = 1 / (1 - retentionStart);
                double slope = q / (1 - retentionStart);
                coefficients.Add(factor * intercept * equilibrium); rates.Add(0);
                coefficients.Add(factor * intercept * conditionTransient); rates.Add(conditionRate);
                coefficients.Add(-factor * slope * equilibrium); rates.Add(qDecayPerDay);
                coefficients.Add(-factor * slope * conditionTransient); rates.Add(conditionRate + qDecayPerDay);
            }
            else
            {
                double retention = EcologyMath.CultivationVegetationRetention(config, q);
                coefficients.Add(factor * retention * equilibrium); rates.Add(0);
                coefficients.Add(factor * retention * conditionTransient); rates.Add(conditionRate);
            }
            double woodDecay = Math.Exp(-woodRate * days);
            double endpoint = Math.Max(0, remaining) * woodDecay;
            for (int i = 0; i < coefficients.Count; i++)
            {
                double forcingDecay = Math.Exp(-rates[i] * days);
                double response = Math.Abs(woodRate - rates[i]) <= 1e-12 * Math.Max(1, woodRate)
                    ? woodRate * days * woodDecay
                    : woodRate * (forcingDecay - woodDecay) / (woodRate - rates[i]);
                endpoint += coefficients[i] * response;
            }
            return Math.Max(0, endpoint);
        }
        internal static double ClampWoodSource(WorldState w, int tile, EcologyView ecology)
        {
            var state = w.MaterialSources.FirstOrDefault(s => s.Id == SurfaceId(tile, MaterialId.Wood) && s.Material == MaterialId.Wood);
            if (state == null && CultivationEconomy.Intensity(w.Config, ecology.CultivationCapital) > 0)
                state = EnsureWoodSource(w, tile, ecology);
            if (state == null) return 0;
            double before = state.Remaining;
            state.Remaining = Math.Min(Math.Max(0, state.Remaining), WoodCapacity(state.Initial, w.Tiles[tile], ecology, w.Config));
            state.LastDays = ecology.Days;
            return Math.Max(0, before - state.Remaining);
        }
        // Called around an instantaneous cultivation-capital change. Establishing
        // the source at the old q first makes capacity lost to displacement an
        // actual stock loss instead of allowing it to return when q later decays.
        internal static double ClampWoodSource(WorldState w, int tile, EcologyView before, EcologyView after)
        {
            var state = w.MaterialSources.FirstOrDefault(s => s.Id == SurfaceId(tile, MaterialId.Wood) && s.Material == MaterialId.Wood)
                ?? EnsureWoodSource(w, tile, before);
            if (state == null) return 0;
            double stockBeforeClamp = state.Remaining;
            state.Remaining = Math.Min(Math.Max(0, state.Remaining), WoodCapacity(state.Initial, w.Tiles[tile], after, w.Config));
            state.LastDays = after.Days;
            return Math.Max(0, stockBeforeClamp - state.Remaining);
        }
        private static MaterialSourceState EnsureWoodSource(WorldState w, int tile, EcologyView ecology)
        {
            if (w.Resources == null || w.MaterialSources == null) return null;
            string id = SurfaceId(tile, MaterialId.Wood);
            var existing = w.MaterialSources.FirstOrDefault(s => s.Id == id && s.Material == MaterialId.Wood);
            if (existing != null) return existing;
            double initial = w.Resources.SurfaceAccessible(tile, MaterialId.Wood);
            if (initial <= 0) return null;
            var state = new MaterialSourceState { Id = id, Material = MaterialId.Wood, Initial = initial,
                Remaining = WoodCapacity(initial, w.Tiles[tile], ecology, w.Config), LastDays = ecology.Days };
            w.MaterialSources.Add(state);
            return state;
        }
        internal static void CommitWoodProjection(WorldState w, int tile, double remaining, double days)
        {
            var state = w.MaterialSources.FirstOrDefault(s => s.Id == SurfaceId(tile, MaterialId.Wood) && s.Material == MaterialId.Wood);
            if (state == null && CultivationEconomy.Intensity(w.Config, w.Tiles[tile].CultivationCapital) > 0)
                state = EnsureWoodSource(w, tile, new EcologyView(w.Tiles[tile]));
            if (state == null) return;
            state.Remaining = Math.Min(Math.Max(0, remaining), WoodCapacity(state.Initial, w.Tiles[tile], new EcologyView(w.Tiles[tile]), w.Config)); state.LastDays = days;
        }
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
                double remaining;
                if (material == MaterialId.Wood)
                {
                    var projection = ProjectWoodSource(w, tile);
                    if (projection == null)
                    {
                        var projected = LazyEcology.ProjectEcology(w, tile, Math.Max(days, w.Tiles[tile].LastEcologyUpdateDays));
                        remaining = WoodCapacity(initial, w.Tiles[tile], projected, w.Config);
                    }
                    else
                    {
                        LazyEcology.ProjectEcology(w, tile, Math.Max(days, w.Tiles[tile].LastEcologyUpdateDays), true, projection.Recover);
                        remaining = projection.Remaining;
                    }
                }
                else remaining = state == null ? initial : recovery > 0 ? initial + (state.Remaining - initial) * Math.Exp(-recovery * Math.Max(0, days - state.LastDays) / w.Config.DaysPerYear) : state.Remaining;
                result.Add(new KnownMaterialSource { Id = id, Tile = tile, Material = material, Initial = initial, Share = share, Available = remaining * share, RecoveryPerYear = recovery, Distance = w.Map.Distance(c.Tile, tile) });
            }
        }
        public static double Extract(WorldState w, Camp c, KnownMaterialSource requested, string role, double workerIntervals, double useful, double days, TurnRecord record)
        {
            if (!CultivationEconomy.Enabled(c) || workerIntervals <= 0 || useful <= 0) return 0;
            var source = KnownSources(w, c, days).FirstOrDefault(s => s.Id == requested.Id && s.Tile == requested.Tile && s.Material == requested.Material);
            if (source == null) return 0;
            if (source.Material == MaterialId.Wood)
            {
                // Establish the source at the same ecology boundary before its
                // first persisted debit; queries above remain pure.
                LazyEcology.MaterializeEcology(w, source.Tile, days);
                source = KnownSources(w, c, days).FirstOrDefault(s => s.Id == requested.Id && s.Tile == requested.Tile && s.Material == requested.Material);
                if (source == null) return 0;
            }
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
        // Compatibility wrapper for the resolver's post-field/post-pot phase.
        public static HousingMaterialPlan Gather(WorldState w, Camp c, CraftRecipe[] recipes, double intervals, double years, double generalistIntervals, ref double equipment, TurnRecord record, bool postAgriculturalResolution = false)
            => Gather(w, c, recipes, intervals, years, generalistIntervals, ref equipment, record, postAgriculturalResolution, postAgriculturalResolution);

        public static HousingMaterialPlan Gather(WorldState w, Camp c, CraftRecipe[] recipes, double intervals, double years, double generalistIntervals, ref double equipment, TurnRecord record, bool fieldWorkAlreadyResolved, bool potsAlreadyDecayed)
        {
            double days = record.StartDays + record.Duration;
            double housingForecast = 0;
            var forecast = new Dictionary<MaterialId, double> { [MaterialId.Stone] = 0, [MaterialId.Clay] = 0, [MaterialId.Wood] = 0 };
            var planned = Forecast(w, c, recipes, intervals, years, equipment, false, -1, fieldWorkAlreadyResolved, potsAlreadyDecayed);
            var housing = PlanHousingMaterials(w, c, recipes, planned, years, days);
            foreach (var recipe in recipes)
            {
                double amount = planned[recipe.Good];
                forecast[MaterialId.Stone] += amount * recipe.Stone;
                forecast[MaterialId.Clay] += amount * recipe.Clay;
                forecast[MaterialId.Wood] += amount * recipe.Wood;
                if (recipe.ConstructionMaterial > 0) housingForecast += amount * recipe.ConstructionMaterial;
            }
            double Shortfall(MaterialId material)
            {
                double horizon = w.Config.Neolithic.MaterialReserveMultiplier(years);
                double direct = Math.Max(0, forecast[material] * horizon - Inventory(c, material));
                if (material == MaterialId.Stone) return direct;
                double share = material == MaterialId.Wood ? housing.WoodShare : housing.ClayShare;
                double total = forecast[material] * horizon + housingForecast * horizon * share;
                return Math.Max(0, total - Inventory(c, material));
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
            return housing;
        }
        public static double Inventory(Camp c, MaterialId material) => material == MaterialId.Stone ? c.Inventory.Stone : material == MaterialId.Clay ? c.Inventory.Clay : c.Inventory.Wood;
        public static string Specialist(MaterialId material) => material == MaterialId.Stone ? WorkforceRoles.Stonecutter : material == MaterialId.Clay ? WorkforceRoles.ClayMiner : material == MaterialId.Wood ? WorkforceRoles.Woodcutter : null;
        // Mirror the ordered recipe/role plan without changing stock or gaining progress.
        // Each worker and tool interval is forecast only once, not once per recipe.
        // Compatibility wrapper for callers whose production phase has completed
        // both field work and pot depreciation.
        public static Dictionary<CraftGood, double> Forecast(WorldState w, Camp c, CraftRecipe[] recipes, double intervals, double years, double equipment, bool includePlantDemand = false, double residentPopulation = -1, bool postAgriculturalResolution = false)
            => Forecast(w, c, recipes, intervals, years, equipment, includePlantDemand, residentPopulation, postAgriculturalResolution, postAgriculturalResolution);

        public static Dictionary<CraftGood, double> Forecast(WorldState w, Camp c, CraftRecipe[] recipes, double intervals, double years, double equipment, bool includePlantDemand, double residentPopulation, bool fieldWorkAlreadyResolved, bool potsAlreadyDecayed, double plantFoodBudget = -1, double animalFoodBudget = -1)
        {
            var planned = recipes.ToDictionary(r => r.Good, r => 0.0);
            var tools = new ToolCoverageBudget(w, c, intervals, equipment);
            double plant = plantFoodBudget >= 0 ? plantFoodBudget : includePlantDemand ? double.PositiveInfinity : c.PlantFood;
            double animalFood = animalFoodBudget >= 0 ? animalFoodBudget : includePlantDemand ? double.PositiveInfinity : c.AnimalFood;
            double animalFabric = Math.Max(0, c.Inventory.AnimalFabric);
            foreach (string role in WorkforceRoles.Specialists.Concat(new[] { WorkforceRoles.Craftsperson }))
            {
                double labor = CultivationEconomy.Workers(c, role) * intervals;
                bool lightHousingUseful = NeolithicProduction.Priority(c, CraftGood.LightHousing) > 0 &&
                    (CultivationEconomy.Workers(c, WorkforceRoles.Builder) > 0 || CultivationEconomy.Workers(c, WorkforceRoles.Craftsperson) > 0);
                foreach (var recipe in recipes.OrderByDescending(r => NeolithicProduction.Priority(c, r.Good)).ThenBy(r => r.Good))
                {
                    if (NeolithicProduction.Priority(c, recipe.Good) <= 0) continue;
                    if (recipe.AgricultureOnly && !CultivationEconomy.Enabled(c) || recipe.HusbandryOnly && !CultivationEconomy.HusbandryEnabled(c)) continue;
                    bool specialist = role == recipe.Specialist && c.People.HasTech(recipe.Track);
                    if (!specialist && (role != WorkforceRoles.Craftsperson || recipe.SpecialistOnly)) continue;
                    double rate = recipe.Rate * (specialist ? 1 : w.Config.Neolithic.GeneralistTaskEfficiency);
                    if (rate <= 0) continue;
                    double useful = NeolithicProduction.Demand(w, c, recipe.Good, years, residentPopulation, fieldWorkAlreadyResolved, potsAlreadyDecayed);
                    if (recipe.Good == CraftGood.Cloth || recipe.Good == CraftGood.AnimalFabric)
                    {
                        double residents = residentPopulation < 0 ? c.Population : residentPopulation;
                        var n = w.Config.Neolithic;
                        double clothing = residents * n.FabricConsumptionPerPersonTurn * ((years + n.UsefulFinishedGoodsReserveYears) / n.ReferenceTurnYears);
                        double shelter = lightHousingUseful ? Math.Max(0, residents / n.HousingPeoplePerUnit - NeolithicProduction.HousingSupply(w, c, w.Days + years * w.Config.DaysPerYear)) : 0;
                        double virtualCloth = Math.Max(0, c.Inventory.Cloth) + planned[CraftGood.Cloth];
                        double virtualAnimalFabric = animalFabric;
                        double remainingClothing = Math.Max(0, clothing - virtualCloth - virtualAnimalFabric);
                        double clothingBeforeAnimalFabric = Math.Max(0, clothing - virtualCloth);
                        double animalFabricForShelter = Math.Max(0, virtualAnimalFabric - clothingBeforeAnimalFabric);
                        double remainingShelter = Math.Max(0, shelter - animalFabricForShelter - planned[CraftGood.LightHousing]);
                        // Cloth and Animal Fabric satisfy one shared clothing
                        // target. Their priority order determines which recipe
                        // serves the remaining demand; the forecast must debit
                        // both outputs from the same virtual ledger.
                        useful = recipe.Good == CraftGood.Cloth
                            ? remainingClothing
                            : remainingClothing + remainingShelter;
                    }
                    bool sharedFabricNeed = recipe.Good == CraftGood.Cloth || recipe.Good == CraftGood.AnimalFabric;
                    double amount = Math.Min(sharedFabricNeed ? useful : Math.Max(0, useful - planned[recipe.Good]), recipe.Equipment ? tools.Capacity(recipe.Good, rate, labor) : labor * rate);
                    if (recipe.PlantFood > 0) amount = Math.Min(amount, plant / recipe.PlantFood);
                    if (recipe.AnimalFood > 0) amount = Math.Min(amount, animalFood / recipe.AnimalFood);
                    if (recipe.AnimalFabric > 0) amount = Math.Min(amount, animalFabric / recipe.AnimalFabric);
                    if (amount <= 0) continue;
                    labor -= recipe.Equipment ? tools.Spend(recipe.Good, rate, amount) : amount / rate;
                    planned[recipe.Good] += amount; plant -= amount * recipe.PlantFood; animalFood -= amount * recipe.AnimalFood;
                    if (recipe.Good == CraftGood.AnimalFabric) animalFabric += amount;
                    animalFabric -= amount * recipe.AnimalFabric;
                }
            }
            return planned;
        }
    }
}
