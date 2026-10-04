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
        internal void Recover(EcologyView before, EcologyView after, double days)
        {
            if (days <= 0) { remaining = Math.Min(remaining, MaterialEconomy.WoodCapacity(initial, world.Tiles[tile], after, world.Config)); return; }
            remaining = MaterialEconomy.RecoverWood(world, world.Tiles[tile], initial, remaining, before, after, days);
        }
        internal void Commit()
        {
            if (state == null)
            {
                if (initial <= 0 || world.MaterialSources == null) return;
                string id = "surface:" + tile + ":" + (int)MaterialId.Wood;
                state = MaterialEconomy.SourceState(world, id);
                if (state == null)
                {
                    state = new MaterialSourceState { Id = id, Material = MaterialId.Wood, Initial = initial };
                    world.MaterialSources.Add(state); MaterialEconomy.RegisterSourceState(world, state);
                }
            }
            state.Remaining = remaining;
            state.LastDays = world.Tiles[tile].LastEcologyUpdateDays;
        }
    }
    // A turn-local production aid.  It never owns material: inventory and source
    // state remain authoritative.  It fixes Housing Material's place in the
    // material-access order for one turn (WP5b).  Direct-input recipes ranked
    // above Housing Material keep a hard reservation until realized: Housing
    // Material cannot take it.  Recipes ranked below keep a soft reservation:
    // Housing Material first draws Wood/Clay they do not need, in the fixed
    // Wood:Clay proportion, and only then dips into the reserved amounts.  While
    // Housing Material is unfinished, its outstanding claim is in turn a hard
    // limit on lower-ranked direct recipes, whatever role runs first.
    public sealed class HousingMaterialPlan
    {
        internal sealed class Reservation
        {
            public CraftGood Good; public double Wood, Clay, Output; public bool Hard;
        }
        public readonly double WoodShare, ClayShare;
        private readonly List<Reservation> reservations;
        private readonly HashSet<CraftGood> lowerRanked;
        private double claim;
        internal HousingMaterialPlan(double woodShare, double clayShare, List<Reservation> reservations, HashSet<CraftGood> lowerRanked, double claim)
        {
            WoodShare = woodShare; ClayShare = clayShare;
            this.reservations = reservations ?? new List<Reservation>();
            this.lowerRanked = lowerRanked ?? new HashSet<CraftGood>();
            this.claim = Math.Max(0, claim);
        }
        private double Reserved(bool hard, bool wood)
        {
            double total = 0;
            for (int i = 0; i < reservations.Count; i++)
            {
                var r = reservations[i];
                if (r.Hard == hard) total += r.Output * (wood ? r.Wood : r.Clay);
            }
            return total;
        }
        // Outstanding Housing Material claim, in construction-material units.
        internal double Claim => claim;
        internal double HardWood => Reserved(true, true);
        internal double HardClay => Reserved(true, false);
        internal double SoftWood => Reserved(false, true);
        internal double SoftClay => Reserved(false, false);
        public void CompleteDirect(CraftRecipe recipe, double output)
        {
            if (recipe == null || output <= 0) return;
            if (recipe.ConstructionMaterial > 0) { claim = Math.Max(0, claim - output * recipe.ConstructionMaterial); return; }
            for (int i = 0; i < reservations.Count; i++)
                if (reservations[i].Good == recipe.Good) reservations[i].Output = Math.Max(0, reservations[i].Output - output);
        }
        // Once every role that can make a recipe has been considered, an unrealized
        // reservation (or Housing Material claim) is released instead of lingering.
        internal void Settle(CraftRecipe recipe, string role)
        {
            if (recipe == null || role != WorkforceRoles.Craftsperson && !(recipe.SpecialistOnly && role == recipe.Specialist)) return;
            Release(recipe);
        }
        internal void Release(CraftRecipe recipe)
        {
            if (recipe == null) return;
            if (recipe.ConstructionMaterial > 0) { claim = 0; return; }
            for (int i = 0; i < reservations.Count; i++)
                if (reservations[i].Good == recipe.Good) reservations[i].Output = 0;
        }
        // A direct recipe ranked below Housing Material may not take Wood or Clay
        // that Housing Material's unfinished claim still needs.  The claim can be
        // met from either material, so only what Clay cannot cover falls on Wood.
        public double LimitDirect(double inventoryWood, double inventoryClay, CraftRecipe recipe, double amount)
        {
            if (recipe == null || recipe.ConstructionMaterial > 0 || claim <= 1e-12 || !lowerRanked.Contains(recipe.Good)) return amount;
            double usableWood = Math.Max(0, inventoryWood - HardWood), usableClay = Math.Max(0, inventoryClay - HardClay);
            if (recipe.Wood > 0) amount = Math.Min(amount, Math.Max(0, usableWood - Math.Max(0, claim - usableClay)) / recipe.Wood);
            if (recipe.Clay > 0) amount = Math.Min(amount, Math.Max(0, usableClay - Math.Max(0, claim - usableWood)) / recipe.Clay);
            return Math.Max(0, amount);
        }
        public double LimitHousing(Camp c, double desired, double materialPerHousing, out double wood, out double clay)
            => LimitHousing(c.Inventory.Wood, c.Inventory.Clay, desired, materialPerHousing, out wood, out clay);
        public double LimitHousing(double inventoryWood, double inventoryClay, double desired, double materialPerHousing, out double wood, out double clay)
        {
            wood = clay = 0;
            if (!(desired > 0) || !(materialPerHousing > 0)) return 0;
            double softWood = SoftWood, softClay = SoftClay;
            double usableWood = Math.Max(0, inventoryWood - HardWood), usableClay = Math.Max(0, inventoryClay - HardClay);
            double amount = Math.Min(desired, (usableWood + usableClay) / materialPerHousing);
            if (amount <= 1e-12) return 0;
            double total = amount * materialPerHousing;
            // First the Wood and Clay no later recipe needs, in the fixed plan proportion.
            double freeWood = Math.Max(0, usableWood - softWood), freeClay = Math.Max(0, usableClay - softClay);
            wood = Math.Min(freeWood, total * WoodShare);
            clay = Math.Min(freeClay, total * ClayShare);
            double remaining = total - wood - clay;
            double addWood = Math.Min(remaining, freeWood - wood);
            wood += addWood; remaining -= addWood;
            double addClay = Math.Min(remaining, freeClay - clay);
            clay += addClay; remaining -= addClay;
            if (remaining > 1e-12)
            {
                // Then, because Housing Material outranks them, the reserved amounts,
                // split in proportion to what remains of each.
                double reservedWood = Math.Max(0, Math.Min(softWood, usableWood - wood));
                double reservedClay = Math.Max(0, Math.Min(softClay, usableClay - clay));
                double reservedTotal = reservedWood + reservedClay;
                if (reservedTotal > 0)
                {
                    double dipWood = Math.Min(reservedWood, remaining * reservedWood / reservedTotal);
                    double dipClay = Math.Min(reservedClay, remaining - dipWood);
                    dipWood += Math.Min(reservedWood - dipWood, remaining - dipWood - dipClay);
                    wood += dipWood; clay += dipClay;
                }
            }
            return amount;
        }
    }
    public static class MaterialEconomy
    {
        internal static MaterialSourceState SourceState(WorldState w, string id) => MaterialAccessIndex.For(w).State(id);
        internal static void RegisterSourceState(WorldState w, MaterialSourceState state) => w.MaterialIndex?.AddState(state);
        public static MaterialSourceObservation[] Observe(WorldState w, Camp c, double days) => KnownSources(w, c, days).Select(s => new MaterialSourceObservation { Id = s.Id, Tile = s.Tile, Distance = s.Distance, Material = s.Material, Available = s.Available }).ToArray();
        public static readonly MaterialId[] Materials = { MaterialId.Stone, MaterialId.Clay, MaterialId.Wood };
        private static readonly string[] ExtractionRoles = { WorkforceRoles.Stonecutter, WorkforceRoles.Woodcutter, WorkforceRoles.ClayMiner, WorkforceRoles.Gatherer };
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TurnRecord, Tuple<int, Dictionary<int, EcologyBalance>>> BalanceIndices
            = new System.Runtime.CompilerServices.ConditionalWeakTable<TurnRecord, Tuple<int, Dictionary<int, EcologyBalance>>>();

        internal static Dictionary<int, EcologyBalance> GetOrCreateBalanceIndex(TurnRecord record)
        {
            if (record?.EcologyBalances == null) return null;
            int count = record.EcologyBalances.Count;
            if (BalanceIndices.TryGetValue(record, out var entry) && entry.Item1 == count)
                return entry.Item2;
            var index = new Dictionary<int, EcologyBalance>(count);
            foreach (var b in record.EcologyBalances) index[b.Tile] = b;
            BalanceIndices.Remove(record);
            BalanceIndices.Add(record, Tuple.Create(count, index));
            return index;
        }
        // Housing Material (the construction step's Wood/Clay recipe) is the flexible
        // consumer; the Housing assembly takes none directly.  Since WP5b it holds
        // its ordinary place in the material-access order (crafting priority, ties
        // by good, exactly like every other recipe): direct recipes ranked above it
        // have already taken their inputs, and the remaining planned demand of
        // recipes ranked below it is only a soft reservation.  Use all visible,
        // reachable source stock to choose the mix, after retaining the source- and
        // labor-feasible direct recipe plan.
        public static HousingMaterialPlan PlanHousingMaterials(WorldState w, Camp c, CraftRecipe[] recipes, IDictionary<CraftGood, double> planned, double years, double days, KnownMaterialSource[] knownSources = null, ProductionOrder order = null)
        {
            var sources = knownSources ?? KnownSources(w, c, days);
            double Room(MaterialId material) => Inventory(c, material) + sources.Where(s => s.Material == material).Sum(s => s.Available);
            return BuildHousingPlan(recipes, good => NeolithicProduction.Priority(c, good), planned, Room(MaterialId.Stone), Room(MaterialId.Clay), Room(MaterialId.Wood), order);
        }
        // The one place a turn's Housing Material position, reservations and fixed
        // Wood:Clay mix are decided; every schedule (planner, forecast, gathering,
        // resolver) builds or consumes this plan.  `planned` is the role-, tool-,
        // plant- and demand-capped forecast.  Its direct recipes get one
        // deterministic source/material feasibility pass in access order: a Tool job
        // lacking Stone cannot reserve its Wood merely because Wood exists.
        internal static HousingMaterialPlan BuildHousingPlan(CraftRecipe[] recipes, Func<CraftGood, double> priority, IDictionary<CraftGood, double> planned, double stone, double clay, double wood, ProductionOrder order = null)
            => BuildHousingPlan(ProductionOrder.Sort(recipes, priority, order), planned, stone, clay, wood);
        // `order` is the caller's material-access order (recipes not listed take no part).
        internal static HousingMaterialPlan BuildHousingPlan(CraftRecipe[] order, IDictionary<CraftGood, double> planned, double stone, double clay, double wood)
        {
            var material = order.FirstOrDefault(r => r.ConstructionMaterial > 0);
            double Feasible(CraftRecipe r, double output, double s, double cl, double wd)
            {
                if (r.Stone > 0) output = Math.Min(output, s / r.Stone);
                if (r.Clay > 0) output = Math.Min(output, cl / r.Clay);
                if (r.Wood > 0) output = Math.Min(output, wd / r.Wood);
                return output;
            }
            var reservations = new List<HousingMaterialPlan.Reservation>();
            var lowerRanked = new HashSet<CraftGood>();
            var later = new List<CraftRecipe>();
            bool afterMaterial = false;
            foreach (var recipe in order)
            {
                if (recipe.ConstructionMaterial > 0) { afterMaterial = true; continue; }
                if (afterMaterial) lowerRanked.Add(recipe.Good);
                if (!planned.TryGetValue(recipe.Good, out double output) || output <= 0) continue;
                if (afterMaterial) { later.Add(recipe); continue; }
                output = Feasible(recipe, output, stone, clay, wood);
                if (output <= 1e-12) continue;
                stone -= output * recipe.Stone; clay -= output * recipe.Clay; wood -= output * recipe.Wood;
                reservations.Add(new HousingMaterialPlan.Reservation { Good = recipe.Good, Wood = recipe.Wood, Clay = recipe.Clay, Output = output, Hard = true });
            }
            // What the lower-ranked recipes would take if Housing Material were absent.
            double softWood = 0, softClay = 0, probeStone = stone, probeClay = clay, probeWood = wood;
            foreach (var recipe in later)
            {
                double output = Feasible(recipe, planned[recipe.Good], probeStone, probeClay, probeWood);
                if (output <= 1e-12) continue;
                probeStone -= output * recipe.Stone; probeClay -= output * recipe.Clay; probeWood -= output * recipe.Wood;
                softWood += output * recipe.Wood; softClay += output * recipe.Clay;
            }
            double units = material != null && planned.TryGetValue(material.Good, out double materialOutput) ? Math.Max(0, materialOutput) * material.ConstructionMaterial : 0;
            double total = Math.Min(units, Math.Max(0, wood) + Math.Max(0, clay));
            HousingShares(wood, clay, softWood, softClay, out double woodShare, out double clayShare);
            if (woodShare + clayShare <= 0) HousingShares(wood, clay, 0, 0, out woodShare, out clayShare);
            if (total > 1e-12)
            {
                double freeWood = Math.Max(0, wood - softWood), freeClay = Math.Max(0, clay - softClay);
                double first = Math.Min(total, freeWood + freeClay);
                double drawWood = first * woodShare, drawClay = first * clayShare;
                double remaining = total - first;
                if (remaining > 1e-12)
                {
                    double reservedWood = Math.Max(0, Math.Min(softWood, wood)), reservedClay = Math.Max(0, Math.Min(softClay, clay));
                    double reservedTotal = reservedWood + reservedClay;
                    if (reservedTotal > 0)
                    {
                        double dipWood = Math.Min(reservedWood, remaining * reservedWood / reservedTotal);
                        double dipClay = Math.Min(reservedClay, remaining - dipWood);
                        drawWood += dipWood; drawClay += dipClay;
                    }
                }
                // The fixed mix is the whole expected draw, so gathering and
                // production plan for the same Wood:Clay.
                if (drawWood + drawClay > 0) { woodShare = drawWood / (drawWood + drawClay); clayShare = drawClay / (drawWood + drawClay); }
                wood -= drawWood; clay -= drawClay;
            }
            else total = 0;
            foreach (var recipe in later)
            {
                double output = Feasible(recipe, planned[recipe.Good], stone, clay, wood);
                if (output <= 1e-12) continue;
                stone -= output * recipe.Stone; clay -= output * recipe.Clay; wood -= output * recipe.Wood;
                reservations.Add(new HousingMaterialPlan.Reservation { Good = recipe.Good, Wood = recipe.Wood, Clay = recipe.Clay, Output = output, Hard = false });
            }
            return new HousingMaterialPlan(woodShare, clayShare, reservations, lowerRanked, total);
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
            var state = SourceState(w, SurfaceId(tile, MaterialId.Wood));
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
        // cap over one horizon. The retention-threshold split is closed-form math
        // inside this pure projection, not an authoritative subturn state.
        internal static double RecoverWood(WorldState w, Tile tile, double initial, double remaining, EcologyView before, EcologyView after, double days)
        {
            double capacityBefore = WoodCapacity(initial, tile, before, w.Config);
            remaining = Math.Min(Math.Max(0, remaining), capacityBefore);
            double woodRate = w.Resources == null ? 0 : Math.Max(0, w.Resources.WoodRecoveryPerYear) / w.Config.DaysPerYear;
            if (woodRate <= 1e-18 || days <= 0) return Math.Min(remaining, WoodCapacity(initial, tile, after, w.Config));
            double vegetation = Math.Min((int)tile.NaturalVegetationPotential, (int)before.CurrentVegetation) + 1;
            vegetation /= (double)((int)tile.NaturalVegetationPotential + 1);
            // Land condition is fixed during this ecology horizon. Wood grows
            // toward the support of that stored condition, current vegetation,
            // and the changing cultivation cover.
            double ecologicalSupport = initial * vegetation * Numbers.Clamp(before.LandCondition, 0, 1);
            double q = CultivationEconomy.Intensity(w.Config, before.CultivationCapital);
            double lambda = CultivationDecayRatePerDay(w, tile);
            double retentionStart = w.Config.CultivationVegetationDisplacementStart;
            double firstDays = days;
            if (lambda > 1e-18 && q > retentionStart)
            {
                double crossing = Math.Log(q / retentionStart) / lambda;
                firstDays = Math.Min(days, Math.Max(0, crossing));
            }
            if (firstDays > 0)
            {
                remaining = RecoverWoodInterval(remaining, ecologicalSupport, woodRate, firstDays,
                    q, lambda, lambda > 1e-18 && q > retentionStart, w.Config);
            }
            double secondDays = days - firstDays;
            if (secondDays > 1e-12)
            {
                double qAtBoundary = lambda > 1e-18 ? q * Math.Exp(-lambda * firstDays) : q;
                remaining = RecoverWoodInterval(remaining, ecologicalSupport, woodRate, secondDays,
                    qAtBoundary, 0, false, w.Config);
            }
            return Math.Min(Math.Max(0, remaining), WoodCapacity(initial, tile, after, w.Config));
        }
        private static double CultivationDecayRatePerDay(WorldState w, Tile tile)
        {
            double halfLife = Math.Max(1e-12, w.Config.Neolithic.DecayHalfLifeYears);
            return Math.Log(2) / halfLife * (1 - Numbers.Clamp(tile.CultivationOperatingFraction, 0, 1)) / w.Config.DaysPerYear / 2;
        }
        private static double RecoverWoodInterval(double remaining, double ecologicalScale,
            double woodRate, double days, double q, double qDecayPerDay, bool qDecliningAboveThreshold, SimulationConfig config)
        {
            if (days <= 0 || woodRate <= 1e-18) return Math.Max(0, remaining);
            double retentionStart = config.CultivationVegetationDisplacementStart;
            double factor = Math.Max(0, ecologicalScale);
            double woodDecay = Math.Exp(-woodRate * days);
            double endpoint = Math.Max(0, remaining) * woodDecay;
            if (qDecliningAboveThreshold && qDecayPerDay > 1e-18)
            {
                double intercept = 1 / (1 - retentionStart);
                double slope = q / (1 - retentionStart);
                endpoint += WoodForcingResponse(factor * intercept, 0, woodRate, days, woodDecay);
                endpoint += WoodForcingResponse(-factor * slope, qDecayPerDay, woodRate, days, woodDecay);
            }
            else
            {
                double retention = EcologyMath.CultivationVegetationRetention(config, q);
                endpoint += WoodForcingResponse(factor * retention, 0, woodRate, days, woodDecay);
            }
            return Math.Max(0, endpoint);
        }
        private static double WoodForcingResponse(double coefficient, double forcingRate, double woodRate, double days, double woodDecay)
        {
            double forcingDecay = Math.Exp(-forcingRate * days);
            double response = Math.Abs(woodRate - forcingRate) <= 1e-12 * Math.Max(1, woodRate)
                ? woodRate * days * woodDecay
                : woodRate * (forcingDecay - woodDecay) / (woodRate - forcingRate);
            return coefficient * response;
        }
        internal static double ClampWoodSource(WorldState w, int tile, EcologyView ecology)
        {
            var state = SourceState(w, SurfaceId(tile, MaterialId.Wood));
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
            var state = SourceState(w, SurfaceId(tile, MaterialId.Wood))
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
            var existing = SourceState(w, id);
            if (existing != null) return existing;
            double initial = w.Resources.SurfaceAccessible(tile, MaterialId.Wood);
            if (initial <= 0) return null;
            var state = new MaterialSourceState { Id = id, Material = MaterialId.Wood, Initial = initial,
                Remaining = WoodCapacity(initial, w.Tiles[tile], ecology, w.Config), LastDays = ecology.Days };
            w.MaterialSources.Add(state); RegisterSourceState(w, state);
            return state;
        }
        internal static void CommitWoodProjection(WorldState w, int tile, double remaining, double days)
        {
            var state = SourceState(w, SurfaceId(tile, MaterialId.Wood));
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
            System.Threading.Interlocked.Increment(ref TurnDiagnostics.For(w).LocalSourceQueries);
            var r = w.Resources; var index = MaterialAccessIndex.For(w); var result = new List<KnownMaterialSource>();
            // VisionRadius is fixed at two, so the owner's radius-one neighborhood
            // is visible without scanning every camp and hunter for each query.
            var sites = w.Map.Radius(c.Tile, 1).Where(id => w.Tiles[id].TraversableLand).OrderBy(id => id).ToArray();
            foreach (int tile in sites)
                foreach (var material in Materials)
                {
                    if (!string.IsNullOrEmpty(r.DiscoveryTechnologies[(int)material]) && !c.People.HasTech(r.DiscoveryTechnologies[(int)material])) continue;
                    double initial = r.SurfaceAccessible(tile, material);
                    if (initial > 0) Add("surface:" + tile + ":" + (int)material, tile, material, initial, 1, material == MaterialId.Wood ? r.WoodRecoveryPerYear : 0);
                    foreach (var entry in index.Sites(tile))
                    {
                        System.Threading.Interlocked.Increment(ref TurnDiagnostics.For(w).DepositEdges);
                        var body = entry.Body; var site = entry.Site;
                        double local = r.AccessibleAt(body, site, material);
                        double total = index.Total(body, material);
                        if (local > 0 && total > 0) Add("body:" + body.Id + ":" + (int)material, tile, material, total, local / total, 0);
                    }
                }
            return result.ToArray();
            void Add(string id, int tile, MaterialId material, double initial, double share, double recovery)
            {
                var state = index.State(id);
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
            => ExtractKnown(w, c, requested, role, workerIntervals, useful, days, record, false);

        private static double ExtractKnown(WorldState w, Camp c, KnownMaterialSource requested, string role, double workerIntervals, double useful, double days, TurnRecord record, bool trustedLocalCatalog)
        {
            if (!CultivationEconomy.EconomyEnabled(c) || workerIntervals <= 0 || useful <= 0) return 0;
            var source = trustedLocalCatalog ? requested : KnownSources(w, c, days).FirstOrDefault(s => s.Id == requested.Id && s.Tile == requested.Tile && s.Material == requested.Material);
            if (source == null) return 0;
            if (source.Material == MaterialId.Wood)
            {
                // Establish the source at the same ecology boundary before its
                // first persisted debit; queries above remain pure.
                LazyEcology.MaterializeEcology(w, source.Tile, days);
                if (trustedLocalCatalog)
                {
                    var wood = ProjectWoodSource(w, source.Tile);
                    if (wood != null) source.Available = wood.Remaining * source.Share;
                }
                else source = KnownSources(w, c, days).FirstOrDefault(s => s.Id == requested.Id && s.Tile == requested.Tile && s.Material == requested.Material);
                if (source == null || source.Available <= 0) return 0;
            }
            bool specialist = role == Specialist(source.Material) && c.People.HasTech(EconomicIntents.RequiredTechnology(role));
            if (!specialist && role != WorkforceRoles.Gatherer) return 0;
            double rate = SpecialistProductivity.ExtractionRate(w, c, source.Material, role);
            double amount = Math.Min(source.Available, Math.Min(useful, workerIntervals * rate));
            if (amount <= 1e-12) return 0;
            var state = SourceState(w, source.Id);
            if (state == null) { state = new MaterialSourceState { Id = source.Id, Material = source.Material, Initial = source.Initial, Remaining = source.Initial, LastDays = days }; w.MaterialSources.Add(state); RegisterSourceState(w, state); }
            double before = source.Available / source.Share;
            state.Remaining = Math.Max(0, before - amount); state.LastDays = days;
            if (source.Material == MaterialId.Stone) c.Inventory.Stone += amount; else if (source.Material == MaterialId.Clay) c.Inventory.Clay += amount; else c.Inventory.Wood += amount;
            if (MaterialAccessIndex.For(w).Remember(c.SocietyId, source.Id, source.Tile))
                w.MaterialKnowledge.Add(new MaterialKnowledge { Society = c.SocietyId, Source = source.Id, Tile = source.Tile });
            record.MaterialTransfers.Add(new MaterialReceipt { Camp = c.Id, Society = c.SocietyId, Tile = source.Tile, Source = source.Id, Material = source.Material, Role = role, Amount = amount, Before = before, After = state.Remaining, WorkerIntervals = amount / rate, Days = days });
            if (source.Material == MaterialId.Wood)
            {
                EcologicalPressure.RecordMaterialExtraction(source.Tile, c.Id,
                    "Timber extraction", "Wood", useful, amount, record);
            }
            return amount / rate;
        }
        // Compatibility wrapper for the resolver's post-field/post-pot phase.
        public static HousingMaterialPlan Gather(WorldState w, Camp c, CraftRecipe[] recipes, double intervals, double years, double generalistIntervals, ref double equipment, TurnRecord record, bool postAgriculturalResolution = false)
            => Gather(w, c, recipes, intervals, years, generalistIntervals, ref equipment, record, postAgriculturalResolution, postAgriculturalResolution);

        public static HousingMaterialPlan Gather(WorldState w, Camp c, CraftRecipe[] recipes, double intervals, double years, double generalistIntervals, ref double equipment, TurnRecord record, bool fieldWorkAlreadyResolved, bool potsAlreadyDecayed, TurnNeeds needs = null)
        {
            double days = record.StartDays + record.Duration;
            double housingForecast = 0;
            var forecast = new Dictionary<MaterialId, double> { [MaterialId.Stone] = 0, [MaterialId.Clay] = 0, [MaterialId.Wood] = 0 };
            var planned = needs?.Production ?? Forecast(w, c, recipes, intervals, years, equipment, false, -1, fieldWorkAlreadyResolved, potsAlreadyDecayed, order: needs?.Order);
            var housing = needs?.Housing ?? PlanHousingMaterials(w, c, recipes, planned, years, days, null, needs?.Order);
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
            var localSources = KnownSources(w, c, days);
            foreach (string role in ExtractionRoles)
            {
                double labor = role == WorkforceRoles.Gatherer ? generalistIntervals : CultivationEconomy.Workers(c, role) * intervals;
                if (labor <= 1e-10) continue;
                var roleSources = localSources.Where(s => role == WorkforceRoles.Gatherer || role == Specialist(s.Material)).ToArray();
                if (roleSources.Length == 0) continue;
                double rate = SpecialistProductivity.ExtractionRate(w, c, roleSources[0].Material, role);
                // A source is considered once per role and footprint site for the
                // whole turn. This bounds work by nearby sources, not by the
                // number of reference intervals in the turn. Scores rank the
                // aggregate unmet need; they do not schedule extraction dates.
                var visited = new HashSet<(string Id, int Tile)>();
                for (int sourcePass = 0; sourcePass < roleSources.Length && labor > 1e-10; sourcePass++)
                {
                    KnownMaterialSource bestSource = null;
                    double bestScore = 1e-10, bestDemand = 0;
                    for (int i = 0; i < roleSources.Length; i++)
                    {
                        var s = roleSources[i];
                        if (s.Available <= 1e-10 || visited.Contains((s.Id, s.Tile))) continue;
                        double demand = Shortfall(s.Material);
                        double priority = s.Material == MaterialId.Stone ? c.StonePriority : s.Material == MaterialId.Clay ? c.ClayPriority : c.WoodPriority;
                        double score = demand * priority;
                        if (score <= 1e-10) continue;
                        if (bestSource == null || score > bestScore)
                        {
                            bestSource = s; bestScore = score; bestDemand = demand;
                        }
                        else if (score == bestScore)
                        {
                            if (s.Distance < bestSource.Distance)
                            {
                                bestSource = s; bestScore = score; bestDemand = demand;
                            }
                            else if (s.Distance == bestSource.Distance && StringComparer.Ordinal.Compare(s.Id, bestSource.Id) < 0)
                            {
                                bestSource = s; bestScore = score; bestDemand = demand;
                            }
                        }
                    }
                    if (bestSource == null) break;
                    visited.Add((bestSource.Id, bestSource.Tile));
                    double batch = Math.Min(labor, Math.Min(bestSource.Available, bestDemand) / rate);
                    double spent = ExtractKnown(w, c, bestSource, role, batch, bestDemand, days, record, true);
                    if (spent <= 1e-12) break;
                    var state = SourceState(w, bestSource.Id);
                    if (state != null)
                        for (int i = 0; i < localSources.Length; i++)
                            if (localSources[i].Id == bestSource.Id)
                                localSources[i].Available = Math.Max(0, state.Remaining * localSources[i].Share);
                    labor -= spent;
                }
            }
            return housing;
        }
        public static HousingMaterialPlan Gather(WorldState w, Camp c, CraftRecipe[] recipes, TurnHorizon horizon, double generalistIntervals, ref double equipment, TurnRecord record, bool fieldWorkAlreadyResolved, bool potsAlreadyDecayed, TurnNeeds needs = null)
            => Gather(w, c, recipes, horizon.ReferenceIntervals, horizon.Years, generalistIntervals, ref equipment, record, fieldWorkAlreadyResolved, potsAlreadyDecayed, needs);
        public static double Inventory(Camp c, MaterialId material) => material == MaterialId.Stone ? c.Inventory.Stone : material == MaterialId.Clay ? c.Inventory.Clay : c.Inventory.Wood;
        public static string Specialist(MaterialId material) => material == MaterialId.Stone ? WorkforceRoles.Stonecutter : material == MaterialId.Clay ? WorkforceRoles.ClayMiner : material == MaterialId.Wood ? WorkforceRoles.Woodcutter : null;
        // Mirror the ordered recipe/role plan without changing stock or gaining progress.
        // Each worker and tool interval is forecast only once, not once per recipe.
        // Compatibility wrapper for callers whose production phase has completed
        // both field work and pot depreciation.
        public static Dictionary<CraftGood, double> Forecast(WorldState w, Camp c, CraftRecipe[] recipes, double intervals, double years, double equipment, bool includePlantDemand = false, double residentPopulation = -1, bool postAgriculturalResolution = false)
            => Forecast(w, c, recipes, intervals, years, equipment, includePlantDemand, residentPopulation, postAgriculturalResolution, postAgriculturalResolution);

        public static Dictionary<CraftGood, double> Forecast(WorldState w, Camp c, CraftRecipe[] recipes, double intervals, double years, double equipment, bool includePlantDemand, double residentPopulation, bool fieldWorkAlreadyResolved, bool potsAlreadyDecayed, double plantFoodBudget = -1, double animalFoodBudget = -1, KnownMaterialSource[] knownSources = null, ProductionOrder order = null)
        {
            System.Threading.Interlocked.Increment(ref TurnDiagnostics.For(w).ForecastCalls);
            var planned = recipes.ToDictionary(r => r.Good, r => 0.0);
            var tools = new ToolCoverageBudget(w, c, intervals, equipment);
            double plant = plantFoodBudget >= 0 ? plantFoodBudget : includePlantDemand ? double.PositiveInfinity : c.PlantFood;
            double animalFood = animalFoodBudget >= 0 ? animalFoodBudget : includePlantDemand ? double.PositiveInfinity : c.AnimalFood;
            // Imports arrive after production and are never inputs; goods made for export (Docs/Trade.md: output
            // first fills the made-to-order part owed) are held back from inputs.
            double animalFabric = Math.Max(0, c.Inventory.AnimalFabric) - c.TradeImport(CraftGood.AnimalFabric);
            double housingMaterial = Math.Max(0, c.Inventory.HousingMaterial) - c.TradeImport(CraftGood.HousingMaterial);
            double HeldForExport(CraftGood good) => Math.Min(c.TradeExport(good), planned[good]);
            double Outstanding(CraftGood good) => Math.Max(0, c.TradeExport(good) - planned[good]);
            var orderedRecipes = ProductionOrder.Sort(recipes, good => NeolithicProduction.Priority(c, good), order);
            var steps = ProductionOrder.Steps(orderedRecipes, order != null).ToArray();
            var roles = WorkforceRoles.Specialists.Concat(new[] { WorkforceRoles.Craftsperson }).ToArray();
            var laborByRole = roles.ToDictionary(role => role, role => CultivationEconomy.Workers(c, role) * intervals);
            bool lightHousingUseful = NeolithicProduction.Priority(c, CraftGood.LightHousing) > 0 &&
                (CultivationEconomy.Workers(c, WorkforceRoles.Builder) > 0 || CultivationEconomy.Workers(c, WorkforceRoles.Craftsperson) > 0);
            // The forecast never mutates the camp, so each good's pure useful
            // demand is evaluated once; both tool goods share one schedule run.
            var usefulDemand = new Dictionary<CraftGood, double>();
            double UsefulDemand(CraftGood good)
            {
                if (usefulDemand.TryGetValue(good, out double value)) return value;
                if (good == CraftGood.HandTools || good == CraftGood.Tools)
                {
                    var tools = NeolithicProduction.ToolReplacementDemands(w, c, years, residentPopulation < 0 ? c.Population : residentPopulation, fieldWorkAlreadyResolved, potsAlreadyDecayed, knownSources, order);
                    usefulDemand[CraftGood.HandTools] = tools.HandTools; usefulDemand[CraftGood.Tools] = tools.Tools;
                    return good == CraftGood.HandTools ? tools.HandTools : tools.Tools;
                }
                return usefulDemand[good] = NeolithicProduction.Demand(w, c, good, years, residentPopulation, fieldWorkAlreadyResolved, potsAlreadyDecayed, knownSources, order);
            }
            // Goods storage (Docs/PlannerTradeStorageDesign.md §3): the resolver's room rule on a virtual stock, with this
            // turn's useful equipment and Housing work in use and the Pots the turn keeps after decay (plus those it makes).
            GoodsStorage storage = null; double[] stored = null; double storagePots = 0;
            if (GoodsStorage.Enabled(w.Config))
            {
                GoodsStorage.PlannedUse(recipes, good => NeolithicProduction.Priority(c, good) > 0 ? UsefulDemand(good) : 0, out double equipmentUse, out double housingUse);
                storage = GoodsStorage.Begin(w, c, years, residentPopulation, equipmentUse, housingUse);
                stored = GoodsStorage.StockOf(c);
                var nc = w.Config.Neolithic;
                storagePots = Math.Max(0, c.Inventory.Pots) * (potsAlreadyDecayed ? 1 : Math.Pow(1 - nc.PotDecayPerReferenceTurn, Math.Max(0, years) / nc.ReferenceTurnYears));
            }
            // Toolmaking has no equipment input. Solve that aggregate supply
            // first, then use start stock plus forecast output to cap all other
            // work. Both sweeps describe one turn allocation, not two times.
            // One production step of `recipe` by `role`. exportOnly makes only what is still owed for export (claim-first,
            // Docs/PlannerTradeStorageDesign.md §2 P4), whatever the recipe's priority.
            void Step(string role, CraftRecipe recipe, bool exportOnly)
            {
                if (!exportOnly && NeolithicProduction.Priority(c, recipe.Good) <= 0) return;
                if (recipe.AgricultureOnly && !CultivationEconomy.Enabled(c) || recipe.HusbandryOnly && !CultivationEconomy.HusbandryEnabled(c)) return;
                bool specialist = role == recipe.Specialist && c.People.HasTech(recipe.Track);
                if (!specialist && (role != WorkforceRoles.Craftsperson || recipe.SpecialistOnly)) return;
                double rate = SpecialistProductivity.CraftRate(w, c, recipe, role);
                if (rate <= 0) return;
                double labor = laborByRole[role];
                double useful;
                if (recipe.Good != CraftGood.Cloth && recipe.Good != CraftGood.AnimalFabric) useful = UsefulDemand(recipe.Good);
                else
                {
                    double residents = residentPopulation < 0 ? c.Population : residentPopulation;
                    var n = w.Config.Neolithic;
                    double clothing = residents * n.FabricConsumptionPerPersonTurn * ((years + n.UsefulFinishedGoodsReserveYears) / n.ReferenceTurnYears);
                    double shelter = lightHousingUseful ? NeolithicProduction.HousingGapPeople(w, c, residents, w.Days + years * w.Config.DaysPerYear) / n.LightHousingPeoplePerUnit : 0;
                    double virtualCloth = Math.Max(0, c.Inventory.Cloth) - c.TradeExport(CraftGood.Cloth) + planned[CraftGood.Cloth];
                    double virtualAnimalFabric = animalFabric - c.TradeExport(CraftGood.AnimalFabric);
                    double remainingClothing = Math.Max(0, clothing - virtualCloth - virtualAnimalFabric);
                    double clothingBeforeAnimalFabric = Math.Max(0, clothing - virtualCloth);
                    double animalFabricForShelter = Math.Max(0, virtualAnimalFabric - clothingBeforeAnimalFabric);
                    double remainingShelter = Math.Max(0, shelter - animalFabricForShelter - planned[CraftGood.LightHousing]);
                    // Both fabric recipes serve the same cohort demand; goods owed for export are owed as that
                    // good, so its own deficit is made whatever the pooled clothing need (Docs/Trade.md).
                    useful = recipe.Good == CraftGood.Cloth
                        ? Math.Max(remainingClothing, Math.Max(0, -virtualCloth))
                        : Math.Max(remainingClothing + remainingShelter, Math.Max(0, -virtualAnimalFabric));
                }
                bool sharedFabricNeed = recipe.Good == CraftGood.Cloth || recipe.Good == CraftGood.AnimalFabric;
                double wanted = exportOnly ? Outstanding(recipe.Good) : sharedFabricNeed ? useful : Math.Max(0, useful - planned[recipe.Good]);
                // DemandControl ceilings: no ordinary step makes more than the allocated output (exports keep their claim).
                if (!exportOnly && order != null && order.HasCeilings) wanted = Math.Min(wanted, Math.Max(0, order.Ceiling(recipe.Good) - planned[recipe.Good]));
                double amount = Math.Min(wanted, recipe.Equipment ? tools.Capacity(recipe.Good, rate, labor) : labor * rate);
                // Housing Material and its assembly draw on one Builder pool.
                if (recipe.Good == CraftGood.HousingMaterial)
                    amount = NeolithicProduction.ExportedHousingMaterialLimit(tools, rate, labor, exportOnly ? Outstanding(recipe.Good) : Math.Max(0, useful - planned[recipe.Good]), housingMaterial - HeldForExport(CraftGood.HousingMaterial), Outstanding(CraftGood.HousingMaterial));
                if (recipe.HousingMaterial > 0) amount = Math.Min(amount, Math.Max(0, housingMaterial - HeldForExport(CraftGood.HousingMaterial)) / recipe.HousingMaterial);
                if (recipe.PlantFood > 0) amount = Math.Min(amount, plant / recipe.PlantFood);
                if (recipe.AnimalFood > 0) amount = Math.Min(amount, animalFood / recipe.AnimalFood);
                if (recipe.AnimalFabric > 0) amount = Math.Min(amount, Math.Max(0, animalFabric - HeldForExport(CraftGood.AnimalFabric)) / recipe.AnimalFabric);
                if (storage != null && amount > 0)
                    amount = storage.Limit(stored, storage.Capacity(c.Food, storagePots + planned[CraftGood.Pots]), recipe.Good, amount, recipe.AnimalFabric, recipe.HousingMaterial);
                if (amount <= 0) return;
                if (stored != null) GoodsStorage.Add(stored, recipe.Good, amount, recipe.AnimalFabric, recipe.HousingMaterial);
                labor -= recipe.Equipment ? tools.Spend(recipe.Good, rate, amount) : recipe.Good == CraftGood.HousingMaterial ? tools.Time(recipe.Good, rate, amount) : amount / rate;
                laborByRole[role] = labor;
                double forExport = Math.Min(amount, Outstanding(recipe.Good));
                planned[recipe.Good] += amount; plant -= amount * recipe.PlantFood; animalFood -= amount * recipe.AnimalFood;
                tools.Credit(recipe.Good, amount - forExport);
                if (recipe.Good == CraftGood.AnimalFabric) animalFabric += amount;
                animalFabric -= amount * recipe.AnimalFabric;
                housingMaterial += recipe.Good == CraftGood.HousingMaterial ? amount : 0;
                housingMaterial -= amount * recipe.HousingMaterial;
            }
            for (int balanceLayer = 0; balanceLayer < 2; balanceLayer++)
            {
                // Agreed exports first, in recipe order, from the roles that can make them: they take labour, tool time
                // and inputs before this turn's own recipes, as in the resolver.
                foreach (var recipe in recipes)
                {
                    bool toolInput = recipe.Good == CraftGood.HandTools || recipe.Good == CraftGood.Tools;
                    if (toolInput != (balanceLayer == 0) || !(Outstanding(recipe.Good) > 1e-12)) continue;
                    foreach (var role in NeolithicProduction.ExportRoles(c, recipe)) if (Outstanding(recipe.Good) > 1e-12) Step(role, recipe, true);
                }
                foreach (var (role, recipe) in steps)
                {
                    bool toolInput = recipe.Good == CraftGood.HandTools || recipe.Good == CraftGood.Tools;
                    if (toolInput != (balanceLayer == 0)) continue;
                    Step(role, recipe, false);
                }
            }
            return planned;
        }
        public static Dictionary<CraftGood, double> Forecast(WorldState w, Camp c, CraftRecipe[] recipes, TurnHorizon horizon, double equipment, bool includePlantDemand, double residentPopulation, bool fieldWorkAlreadyResolved, bool potsAlreadyDecayed, double plantFoodBudget = -1, double animalFoodBudget = -1, KnownMaterialSource[] knownSources = null, ProductionOrder order = null)
            => Forecast(w, c, recipes, horizon.ReferenceIntervals, horizon.Years, equipment, includePlantDemand, residentPopulation, fieldWorkAlreadyResolved, potsAlreadyDecayed, plantFoodBudget, animalFoodBudget, knownSources, order);
    }
}
