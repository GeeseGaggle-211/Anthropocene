using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Anthropocene.Simulation
{
    // Native Elder contract 7 (model9): the learned Elder sets service levels and routes; the DemandControl compiler
    // turns them into roles, recipes and whole workers. Observation 7 is the v2 typed observation split into entity
    // sets with explicit row presence plus a demography block. Every village decides every turn (multi-village
    // environment), and the heuristic's own decision is recorded as an exact contract-7 action for teacher labels.
    public static class ElderModel9Contract
    {
        // Contract 8 adds the three-way economy mix (fishing development) and fishing access on site rows.
        // Contract 9 adds multi-turn capital commitments (Docs/ElderCapitalCommitmentsDesign.md, Part 1).
        // Contract 10 adds regional movement rows and destination intents (Docs/ElderModel9Contract9MovementDesign.md).
        // Contract 11 adds the Elder trade posture and trade-group rows (Docs/ElderModel9TradeDesign.md).
        // Contract 12 adds activity-token rows (observation only; Anthropocene-RL docs/MODEL9_TOKENS.md). Action schema 11.
        public const int NativeContractVersion = 12;
        public const int ActionSchemaVersion = 11, ObservationSchemaVersion = 12, EnvironmentSchemaVersion = 12, ReplaySchemaVersion = 12;
        // Schema-10 directives remain valid (no trade posture), schema 9 has no intents, schema 8 no commitments, schema 7
        // no fishing development.
        public const int MovementActionSchemaVersion = 10, CommitmentActionSchemaVersion = 9, PreviousActionSchemaVersion = 8, LegacyActionSchemaVersion = 7;
        // The first schema with fishing development, commitments, intents and trade posture.
        public const int FishingSchemaVersion = 8, CommitmentSchemaVersion = 9, MovementSchemaVersion = 10, TradeSchemaVersion = 11;
        public static readonly string[] GoodsServices = { "food", "clothing", "shelter", "tools", "storage", "materials" };
        public static readonly string[] StrategicControls = { "retained-field-operation", "retained-herd-fraction", "development-budget", "field-development-share", "fishing-development-share" };
        // The Development budget's economy mix: ((1 - f) s, (1 - f)(1 - s), f) for field share s and fishing share f.
        public static readonly string[] EconomyMix = { "cultivation", "herding", "fishing" };
        public const int MovementSlots = 1 + ElderRLContract.MaxLocalSites; // stay + site rows
        // MovementCandidate -2: the native heuristic keeps the camp's movement/dispatch orders (curriculum stages 1-3).
        public const int NativeMovement = -2;

        // Maps a DemandControl request service name to its contract-7 goods service (-1: not a goods service).
        public static int ServiceOf(string service)
        {
            switch (service)
            {
                case "food reserve recovery": case "food growth gathering": return 0;
                case "clothing": case "clothing reserve": return 1;
                case "shelter protected": case "shelter replacement": case "portable shelter replacement": case "shelter deficit": return 2;
                case "farm tools": case "herding tools": case "fishing tools": return 3;
                case "storage": return 4;
                default: return -1;
            }
        }

        // Entity sets: name, rows, row width. Row presence is carried separately; missing rows are never zeros.
        public static readonly (string Name, int Rows, int Width)[] Sets =
        {
            ("sites", ElderRLContract.MaxLocalSites, 14),
            ("fishing", ElderRLContract.MaxFishingGrounds, 11),
            ("herds", ElderRLContract.MaxHerdGroups, 8),
            ("commodities", ElderRLContract.MaxCommoditySlots, 8),
            ("contacts", ElderRLContract.MaxKnownContacts, 9),
            // Contract 10: regional movement candidates (ElderMovement), appended so earlier set indexes are unchanged.
            ("regions", ElderMovement.MaxRegions, ElderMovement.RowWidth),
            // Contract 11: one row per trade group (ElderTrade.Groups).
            ("trade", 7, ElderTrade.RowWidth),
            // Contract 12: activity tokens (Docs/ElderCapitalCommitmentsDesign.md Part 2, slice 1), one row per staffable
            // activity kind in EconomyMix order; later kinds append rows.
            ("activities", 3, ActivityWidth)
        };
        public const int ActivityWidth = 12;
        // Contract 10 movement head: native, keep intent, then one slot per region row.
        public const int RegionMovementSlots = 2 + ElderMovement.MaxRegions;
        public const int CoreV2Width = 128, PresenceV2Width = 48, DemographyWidth = 12, ContextWidth = 3 + 4 + 6 + 6 + CommitmentWidth;
        // Contract 9 commitment block (appended after the contract-8 context, so earlier feature indexes are unchanged).
        public const int CommitmentWidth = 11;
        public static int CoreWidth => CoreV2Width + PresenceV2Width + DemographyWidth + ContextWidth;
        // Training-only critic inputs (never part of the actor observation): growth headroom and lineage context.
        public static readonly string[] PrivilegedFeatures = { "log-population", "episode-age-turns", "site-food-over-meals",
            "footprint-potential-food-over-meals", "footprint-food-over-meals", "sustainable-herd-over-animals",
            "absolute-herd-over-animals", "last-growth", "daughters", "fixed-housing-per-resident" };
    }

    // One encoded village observation. Values are raw physical units; the learner normalizes (symlog plus a fixed
    // TRAIN-fitted affine). Present flags mark which core features are defined.
    [Serializable]
    public sealed class ElderModel9Tensor
    {
        public float[] Core;
        public bool[] CorePresent;
        public float[][] Sets;      // per set: Rows * Width, row-major
        public bool[][] RowPresent; // per set: Rows
        public bool[] MovementMask = new bool[ElderModel9Contract.MovementSlots];
        public bool[] ClothingRouteMask = new bool[2], ShelterRouteMask = new bool[2];
        // Contract 8: which economy-mix coordinates (cultivation, herding, fishing) are structurally available.
        public bool[] EconomyMixMask = { true, true, true };
        // Contract 9: the active capital commitment can be held this turn.
        public bool CommitmentHoldMask;
        // Contract 10: native, keep intent, region rows 0-7 (ElderModel9Contract.RegionMovementSlots).
        public bool[] RegionMovementMask = new bool[ElderModel9Contract.RegionMovementSlots];
    }

    // The heuristic's decision expressed as a contract-7 action, with the masks that say which label parts carry
    // information (a service with no native request, or a route the camp cannot use, is not a label).
    [Serializable]
    public sealed class ElderModel9Label
    {
        public ElderDirective Action = new ElderDirective { SchemaVersion = ElderModel9Contract.ActionSchemaVersion };
        public bool[] ServiceMask = new bool[ElderModel9Contract.GoodsServices.Length];
        public bool StrategicFieldMask, StrategicHerdMask, DevelopmentMask, DevelopmentShareMask, FishingShareMask;
        public bool ClothingRouteMask, ShelterRouteMask, MovementMask;
        // Contract 11: the trade posture is a label wherever the village can trade.
        public bool TradeMask;
        public double RepairSize;
    }

    public static class ElderModel9Encoder
    {
        public static ElderModel9Tensor Encode(ElderObservation o, ElderModel9Context context)
        {
            var v2 = ElderObservationSchema.Encode(o);
            var t = new ElderModel9Tensor();
            int w = ElderModel9Contract.CoreWidth; t.Core = new float[w]; t.CorePresent = new bool[w];
            int k = 0;
            for (int i = 0; i < ElderModel9Contract.CoreV2Width; i++, k++) { t.Core[k] = v2.Values[i]; t.CorePresent[k] = v2.Present[i]; }
            for (int i = 464; i < 512; i++, k++) { t.Core[k] = v2.Values[i]; t.CorePresent[k] = v2.Present[i]; }
            var d = o.Demography;
            foreach (double value in new[] { d.BirthRate, d.MortalityRate, d.ExposureRate, d.CrowdingRate, d.DiseaseRate, d.StarvationRate,
                d.FoodSurplusPerResident, d.BirthFundingFraction, d.FedCapacityRatio, d.HousingPerResident, d.ClothingPerResident, d.SurplusFundedBirths ? 1 : 0 })
            { t.Core[k] = (float)(ElderRLContract.Finite(value) ? value : 0); t.CorePresent[k] = true; k++; }
            for (int p = 0; p < 3; p++, k++) { t.Core[k] = o.CurrentFissionPolicy == p ? 1 : 0; t.CorePresent[k] = true; }
            foreach (bool flag in context.ClothingRouteFeasible.Concat(context.ShelterRouteFeasible))
            { t.Core[k] = flag ? 1 : 0; t.CorePresent[k] = context.HasPrevious; k++; }
            for (int s = 0; s < ElderModel9Contract.GoodsServices.Length; s++, k++)
            {
                double perResident = o.Core.Population > 0 ? context.PreviousServiceRequested[s] / o.Core.Population : 0;
                t.Core[k] = (float)perResident; t.CorePresent[k] = context.HasPrevious;
            }
            // Contract 8 fishing block: availability now; last allocation's frontier and fishing development.
            double population = Math.Max(1, o.Core.Population);
            foreach (var (value, present) in new[] {
                (o.Mask.FishingDevelopment ? 1.0 : 0.0, true),
                (context.PreviousFrontierFishers / population, context.HasPrevious),
                (context.PreviousMarginalCatchRatio, context.HasPrevious),
                (context.PreviousMeals > 0 ? context.PreviousFishingFood / context.PreviousMeals : 0, context.HasPrevious),
                (context.PreviousFishingShare, context.HasPrevious),
                (context.PreviousFishingDevelopment / population, context.HasPrevious) })
            { t.Core[k] = (float)(ElderRLContract.Finite(value) ? value : 0); t.CorePresent[k] = present; k++; }
            // Contract 9 commitment block: whether one is active, its remaining and elapsed turns, the committed capital
            // controls, and whether the last turn force-broke one.
            var commitment = context.Commitment; bool active = commitment != null;
            foreach (var (value, present) in new[] {
                (active ? 1.0 : 0.0, true),
                (active ? context.CommitmentRemaining : 0.0, true),
                (active ? context.CommitmentAge : 0.0, true),
                (commitment?.RetainedFieldOperation ?? 0, active), (commitment?.RetainedHerdFraction ?? 0, active),
                (commitment?.DevelopmentBudget ?? 0, active), (commitment?.FieldDevelopmentShare ?? 0, active),
                (commitment?.FishingDevelopmentShare ?? 0, active),
                (commitment != null && commitment.ClothingRoute == 1 ? 1.0 : 0.0, active && commitment.ClothingRoute >= 0),
                (commitment != null && commitment.ShelterRoute == 1 ? 1.0 : 0.0, active && commitment.ShelterRoute >= 0),
                (context.CommitmentBrokenLastTurn ? 1.0 : 0.0, true) })
            { t.Core[k] = (float)(ElderRLContract.Finite(value) ? value : 0); t.CorePresent[k] = present; k++; }
            t.CommitmentHoldMask = o.Mask.CommitmentHold;
            // Entity sets from the v2 typed blocks (offsets fixed by ElderObservationSchema.Registry).
            int[] offsets = { 128, 212, 248, 312, 400 };
            int[] strides = { 12, 12, 8, 8, 8 };
            int[] counts = { o.Sites.Length, o.Fishing.Length, o.HerdGroups.Length, o.Commodities.Length, o.Contacts.Length };
            t.Sets = new float[ElderModel9Contract.Sets.Length][]; t.RowPresent = new bool[ElderModel9Contract.Sets.Length][];
            for (int s = 0; s < ElderModel9Contract.Sets.Length; s++)
            {
                if (s == RegionSet) { EncodeRegions(o, t); continue; }
                if (s == TradeSet) { EncodeTrade(o, t); continue; }
                if (s == ActivitySet) { EncodeActivities(o, context, t); continue; }
                var set = ElderModel9Contract.Sets[s];
                t.Sets[s] = new float[set.Rows * set.Width]; t.RowPresent[s] = new bool[set.Rows];
                for (int r = 0; r < set.Rows; r++)
                {
                    t.RowPresent[s][r] = r < counts[s];
                    if (!t.RowPresent[s][r]) continue;
                    int width = Math.Min(set.Width, strides[s]);
                    for (int c = 0; c < width && c < (s == 1 ? 11 : strides[s]); c++)
                        t.Sets[s][r * set.Width + c] = v2.Values[offsets[s] + r * strides[s] + c];
                    if (s == 4) t.Sets[s][r * set.Width + 8] = (float)o.Contacts[r].PopulationBand;
                    if (s == 0)
                    {
                        t.Sets[s][r * set.Width + 12] = o.Sites[r].FishingSource ? 1 : 0;
                        t.Sets[s][r * set.Width + 13] = (float)o.Sites[r].FishingAccess;
                    }
                }
            }
            t.MovementMask[0] = true;
            for (int i = 0; i < ElderRLContract.MaxLocalSites; i++) t.MovementMask[i + 1] = o.Mask.AllowsMovement(i);
            t.ClothingRouteMask = context.HasPrevious ? (bool[])context.ClothingRouteFeasible.Clone() : new[] { true, true };
            t.ShelterRouteMask = context.HasPrevious ? (bool[])context.ShelterRouteFeasible.Clone() : new[] { true, true };
            if (!t.ClothingRouteMask.Any(x => x)) t.ClothingRouteMask = new[] { true, true };
            if (!t.ShelterRouteMask.Any(x => x)) t.ShelterRouteMask = new[] { true, true };
            t.EconomyMixMask = new[] { o.Mask.FieldDevelopment, o.Mask.HerdDevelopment, o.Mask.FishingDevelopment };
            if (!t.EconomyMixMask.Any(x => x)) t.EconomyMixMask = new[] { true, true, true };
            t.RegionMovementMask[0] = true;
            t.RegionMovementMask[1] = o.Mask.AllowsMovement(ElderMovement.KeepIntent);
            for (int r = 0; r < ElderMovement.MaxRegions; r++)
                t.RegionMovementMask[2 + r] = r < o.Regions.Length && o.Mask.AllowsMovement(ElderMovement.RegionBase + r);
            return t;
        }

        public const int RegionSet = 5, TradeSet = 6, ActivitySet = 7;

        // Contract 12 activity tokens from the observation's own field, herd and fishing blocks (no new state): per
        // kind, legality, staffing, maintenance load, capital at stake, current food share, marginal headroom, the
        // capital lost on abandonment, operation, intensity, whether it feeds textiles. A row is present when its kind
        // is practised or developable.
        private static void EncodeActivities(ElderObservation o, ElderModel9Context context, ElderModel9Tensor t)
        {
            var set = ElderModel9Contract.Sets[ActivitySet];
            t.Sets[ActivitySet] = new float[set.Rows * set.Width]; t.RowPresent[ActivitySet] = new bool[set.Rows];
            double people = Math.Max(1, o.Core.Population), years = Math.Max(1e-9, o.Core.NextIntervalYears);
            double meals = context.HasPrevious && context.PreviousMeals > 0 ? context.PreviousMeals : 0;
            var f = o.Fields; var h = o.Herd;
            var fishing = o.Fishing.Where(g => g.Incumbent).ToArray();
            double fishers = context.HasPrevious ? context.PreviousFrontierFishers + context.PreviousFishingDevelopment : 0;
            var rows = new[]
            {
                // Cultivation.
                new double[] { o.Mask.FieldDevelopment ? 1 : 0, f.MaintenanceWorkerYears * f.CurrentOperation / years / people,
                    f.MaintenanceWorkerYears / years / people, f.RetainedCapitalService, f.CurrentFoodSupport,
                    Math.Max(0, f.FutureFoodSupport - f.CurrentFoodSupport), f.RebuildBurden, f.CurrentOperation, f.CurrentIntensity, 1, f.RetainedTiles, 0 },
                // Herding.
                new double[] { o.Mask.HerdDevelopment ? 1 : 0, h.CurrentManagementWorkerYears / years / people, h.CurrentManagementWorkerYears / years / people,
                    h.Animals / people, meals > 0 ? h.CurrentAnimalFood / meals : 0,
                    h.Animals > 0 ? Math.Max(0, h.ZeroDisplacementCapacity / h.Animals - 1) : 0, h.HumanFoodDisplaced / Math.Max(1e-9, meals > 0 ? meals : people),
                    h.ManagementFulfilment, h.Nutrition, 1, h.BirthTrend - h.MortalityTrend, 0 },
                // Fishing.
                new double[] { o.Mask.FishingDevelopment ? 1 : 0, fishers / people, fishing.Sum(g => g.MaintenanceWorkerYears) / years / people,
                    fishing.Sum(g => g.CurrentCapital), meals > 0 && context.HasPrevious ? context.PreviousFishingFood / meals : 0,
                    o.Fishing.Length > 0 ? o.Fishing.Max(g => g.YieldPerFisherYear) : 0, 0,
                    fishing.Length > 0 ? fishing.Average(g => g.OperationFraction) : 0, fishing.Length > 0 ? fishing.Average(g => g.CurrentIntensity) : 0,
                    0, context.HasPrevious ? context.PreviousMarginalCatchRatio : 0, 0 }
            };
            bool[] present = { o.Mask.FieldDevelopment || f.RetainedTiles > 0, o.Mask.HerdDevelopment || h.Animals > 0, o.Mask.FishingDevelopment || fishing.Length > 0 };
            for (int r = 0; r < set.Rows; r++)
            {
                t.RowPresent[ActivitySet][r] = present[r];
                if (!present[r]) continue;
                for (int c = 0; c < set.Width; c++)
                    t.Sets[ActivitySet][r * set.Width + c] = (float)(ElderRLContract.Finite(rows[r][c]) ? rows[r][c] : 0);
            }
        }

        public static string[] ActivityFeatureNames() => new[] { "developable", "staffed-per-resident", "maintenance-per-resident",
            "capital-at-stake", "food-share", "marginal-headroom", "abandonment-loss", "operation", "intensity", "feeds-textiles",
            "kind-specific", "reserved" };

        // Contract 11 trade-group rows; a row is present when its group is tradeable for the village.
        private static void EncodeTrade(ElderObservation o, ElderModel9Tensor t)
        {
            var set = ElderModel9Contract.Sets[TradeSet];
            t.Sets[TradeSet] = new float[set.Rows * set.Width]; t.RowPresent[TradeSet] = new bool[set.Rows];
            for (int r = 0; r < set.Rows && r < o.Trade.Length; r++)
            {
                var x = o.Trade[r];
                if (!x.Active) continue;
                t.RowPresent[TradeSet][r] = true;
                var values = new[] { x.Held, x.Giveable, x.MakeCapacity, x.HasMakeCost ? x.MakeCost : 0, x.HasMakeCost ? 1 : 0, x.UseValue,
                    x.Imported, x.Exported, x.RealisedUtility, 0 };
                for (int c = 0; c < set.Width; c++)
                    t.Sets[TradeSet][r * set.Width + c] = (float)(ElderRLContract.Finite(values[c]) ? values[c] : 0);
            }
        }

        public static string[] TradeFeatureNames() => new[] { "held", "giveable", "make-capacity", "make-cost", "has-make-cost", "use-value",
            "imported", "exported", "realised-utility", "reserved" };

        // Contract 10 region rows, in ElderRegionObservation order (raw units; flags 0/1).
        private static void EncodeRegions(ElderObservation o, ElderModel9Tensor t)
        {
            var set = ElderModel9Contract.Sets[RegionSet];
            t.Sets[RegionSet] = new float[set.Rows * set.Width]; t.RowPresent[RegionSet] = new bool[set.Rows];
            for (int r = 0; r < set.Rows && r < o.Regions.Length; r++)
            {
                var x = o.Regions[r];
                t.RowPresent[RegionSet][r] = true;
                var values = new double[] { x.Current ? 1 : 0, x.Intent ? 1 : 0, x.Visible ? 1 : 0, x.PathFeasible ? 1 : 0,
                    x.Distance, x.PathTurns,
                    x.EnvironmentGroup == 0 ? 1 : 0, x.EnvironmentGroup == 1 ? 1 : 0, x.EnvironmentGroup == 2 ? 1 : 0,
                    x.EnvironmentGroup == 3 ? 1 : 0, x.EnvironmentGroup == 4 ? 1 : 0,
                    x.River ? 1 : 0, x.Freshwater ? 1 : 0, x.Coast ? 1 : 0, x.Relief,
                    x.HumanFood, x.HerdForage, x.HerdWater, x.FarmingFood, x.FieldEffort, x.FishingAccess,
                    x.KnownForeign, x.KnownKin, 0 };
                for (int c = 0; c < set.Width; c++)
                    t.Sets[RegionSet][r * set.Width + c] = (float)(ElderRLContract.Finite(values[c]) ? values[c] : 0);
            }
        }

        public static string[] RegionFeatureNames() => new[] { "current", "intent", "visible", "path-feasible", "distance", "path-turns",
            "desert", "grassland", "temperate", "tropical", "cold", "river", "freshwater", "coast", "relief",
            "human-food", "herd-forage", "herd-water", "farming-food", "field-effort", "fishing-access",
            "known-foreign", "known-kin", "reserved" };

        // Feature names for the learner's schema registry and audits.
        public static string[] CoreFeatureNames()
        {
            var names = new List<string>();
            for (int i = 0; i < ElderModel9Contract.CoreV2Width; i++) names.Add("v2-" + i);
            for (int i = 464; i < 512; i++) names.Add("v2-" + i);
            names.AddRange(new[] { "birth-rate", "mortality-rate", "exposure-rate", "crowding-rate", "disease-rate", "starvation-rate",
                "food-surplus-per-resident", "birth-funding-fraction", "fed-capacity-ratio", "housing-per-resident", "clothing-per-resident", "surplus-funded-births" });
            names.AddRange(new[] { "fission-neutral", "fission-discourage", "fission-encourage" });
            names.AddRange(new[] { "cloth-route-feasible", "fabric-route-feasible", "fixed-shelter-feasible", "light-shelter-feasible" });
            names.AddRange(ElderModel9Contract.GoodsServices.Select(s => "previous-request-" + s + "-per-resident"));
            names.AddRange(new[] { "fishing-development-available", "frontier-fishers-per-resident", "frontier-marginal-catch-over-gathering",
                "fishing-food-over-meals", "previous-fishing-development-share", "previous-fishing-development-per-resident" });
            names.AddRange(new[] { "commitment-active", "commitment-remaining-turns", "commitment-age-turns", "committed-field-operation",
                "committed-herd-fraction", "committed-development-budget", "committed-field-share", "committed-fishing-share",
                "committed-clothing-animal-fabric", "committed-shelter-light", "commitment-broken-last-turn" });
            return names.ToArray();
        }
    }

    // Per-village state carried between decisions: the previous allocation's route feasibility and requests.
    public sealed class ElderModel9Context
    {
        public bool HasPrevious;
        public bool[] ClothingRouteFeasible = { true, true }, ShelterRouteFeasible = { true, true };
        public double[] PreviousServiceRequested = new double[ElderModel9Contract.GoodsServices.Length];
        public double PreviousFrontierFishers, PreviousMarginalCatchRatio, PreviousFishingFood, PreviousMeals;
        public double PreviousFishingShare, PreviousFishingDevelopment;
        public ElderCompiledDecision PreviousDecision;
        // Contract 9: the settlement's active commitment (null when none), read from its saved Elder memory at
        // observation time.
        public ElderDirective Commitment;
        public double CommitmentRemaining, CommitmentAge;
        public bool CommitmentBrokenLastTurn;

        // A branch copy for search forks. PreviousDecision is shared: compiled decisions are replaced, never mutated.
        public ElderModel9Context Copy()
        {
            var c = (ElderModel9Context)MemberwiseClone();
            c.ClothingRouteFeasible = (bool[])ClothingRouteFeasible.Clone(); c.ShelterRouteFeasible = (bool[])ShelterRouteFeasible.Clone();
            c.PreviousServiceRequested = (double[])PreviousServiceRequested.Clone();
            c.Commitment = Commitment?.Copy();
            return c;
        }
    }

    // A search branch point (learner-side expert iteration): the saved world, every village's environment context and
    // the prior populations of the last step. Opaque outside the environment.
    public sealed class ElderModel9Fork
    {
        internal byte[] World;
        internal Dictionary<int, ElderModel9Context> Contexts;
        internal Dictionary<int, int> Prior;
        internal int ReplaySteps;
    }

    public static class ElderModel9Observation
    {
        public static void FillDemography(WorldState w, Camp c, ElderObservation o)
        {
            var cfg = w.Config; var d = o.Demography;
            d.BirthRate = c.EffectiveFertilityRate; d.MortalityRate = c.EffectiveMortalityRate;
            d.ExposureRate = c.ExposureMortalityRate; d.CrowdingRate = c.CrowdingMortalityRate;
            d.DiseaseRate = c.DiseaseMortalityRate; d.StarvationRate = c.StarvationMortalityRate;
            double meals = Math.Max(1, c.Population) * cfg.Consumption * cfg.GenerationIntervals;
            d.FoodSurplusPerResident = c.Population > 0 ? (Math.Max(0, c.Food) - meals) / c.Population : 0;
            d.BirthFundingFraction = cfg.BaseBirthRate > 0 ? ElderRLContract.Unit(c.EffectiveFertilityRate / cfg.BaseBirthRate) : 0;
            d.FedCapacityRatio = o.Sites.FirstOrDefault(s => s.Current)?.HumanFood ?? 0;
            d.HousingPerResident = c.HousingPerCapita; d.ClothingPerResident = c.ClothPerCapita;
            d.SurplusFundedBirths = PhysicalDemography.SurplusFunded(cfg);
            o.CurrentFissionPolicy = (int)c.FissionPolicy;
        }
    }

    public static class ElderModel9Teacher
    {
        // The exact allocation DemandControl used this turn, expressed as a contract-7 action.
        internal static ElderModel9Label Label(WorldState w, Camp c, EconomicIntent intent, DemandController.Allocation a,
            List<Order> campOrders, ElderObservationMetadata metadata)
        {
            var label = new ElderModel9Label();
            var act = label.Action;
            // Development is labelled by the strategic budgets the native ray search selected (the quantities the
            // directive path consumes), not by compiled headcount: whole-animal growth within existing herders has a
            // positive budget share but adds no herder.
            double fieldDevelopment = Math.Max(0, a.SelectedFieldBudget);
            double herdDevelopment = Math.Max(0, a.SelectedHerdBudget);
            if (a.HerdDevelopmentAnimals > 1e-9 && herdDevelopment < .5) herdDevelopment = .5; // free growth: a positive sub-person budget
            double development = fieldDevelopment + herdDevelopment;
            act.RetainedFieldOperation = ElderRLContract.Unit(a.SelectedFieldOperation);
            act.RetainedHerdFraction = a.HCurrent > 1e-9 ? ElderRLContract.Unit(a.HRetained / a.HCurrent) : 0;
            act.DevelopmentBudget = c.Population > 0 ? ElderRLContract.Unit(development / c.Population) : 0;
            act.FieldDevelopmentShare = development > 1e-9 ? ElderRLContract.Unit(fieldDevelopment / development) : .5;
            // The native planner never develops fishing beyond the survival frontier. A compiled contract-8 directive
            // records the Fishers it actually staffed (the realized economy mix).
            double fishingDevelopment = Math.Max(0, a.FishingDevelopmentGranted);
            act.FishingDevelopmentShare = development + fishingDevelopment > 1e-9
                ? ElderRLContract.Unit(fishingDevelopment / (development + fishingDevelopment)) : 0;
            if (fishingDevelopment > 0)
                act.DevelopmentBudget = c.Population > 0 ? ElderRLContract.Unit((development + fishingDevelopment) / c.Population) : 0;
            label.FishingShareMask = a.FishingDevelopmentFeasible && development + fishingDevelopment > 1e-9;
            label.StrategicFieldMask = a.Farmers > 0 || a.SelectedFieldOperation > 1e-9;
            label.StrategicHerdMask = a.HCurrent > 1e-9;
            label.DevelopmentMask = true; label.DevelopmentShareMask = development > 1e-9;
            act.ServiceLevels = new double[ElderModel9Contract.GoodsServices.Length];
            for (int s = 0; s < act.ServiceLevels.Length; s++)
            {
                bool requested = a.ServiceRequested[s] > 1e-9;
                label.ServiceMask[s] = requested;
                act.ServiceLevels[s] = requested ? ElderRLContract.Unit(a.ServiceGranted[s] / a.ServiceRequested[s]) : 1;
                if (requested) label.RepairSize += Math.Max(0, a.ServiceRequested[s] - a.ServiceGranted[s]);
            }
            act.ClothingRoute = a.ChosenClothingRoute == CraftGood.Cloth ? 0 : 1;
            act.ShelterRoute = a.ChosenShelterRoute == CraftGood.Housing ? 0 : 1;
            label.ClothingRouteMask = a.ClothingRouteFeasible[0] && a.ClothingRouteFeasible[1];
            label.ShelterRouteMask = a.ShelterRouteFeasible[0] && a.ShelterRouteFeasible[1];
            act.MovementCandidate = -1; label.MovementMask = true;
            var move = campOrders?.FirstOrDefault(o => o.Entity == c.Id && o.Kind == OrderKind.Move);
            if (move != null)
            {
                int index = Array.IndexOf(metadata?.SiteTiles ?? Array.Empty<int>(), move.Destination);
                if (index >= 0) act.MovementCandidate = index; else label.MovementMask = false;
            }
            var policy = campOrders?.FirstOrDefault(o => o.Entity == c.Id && o.Kind == OrderKind.FissionPolicy);
            act.FissionPolicy = policy != null ? policy.Target : (int)c.FissionPolicy;
            // Contract 11: the heuristic trades on its native posture (values 1, every gate open to making, the
            // configuration's export share), labelled wherever the village can trade.
            act.TradeValue = Enumerable.Repeat(1.0, ElderTrade.Groups.Length).ToArray();
            act.TradeGate = Enumerable.Repeat(ElderTrade.Make, ElderTrade.Groups.Length).ToArray();
            act.TradeExportShare = w.Config.TradeExportLabourShare;
            label.TradeMask = Trade.Eligible(w, c);
            return label;
        }
    }

    [Serializable]
    public sealed class ElderModel9Village
    {
        public int Camp, Society, ParentSociety, Player;
        public int PriorPopulation, Population;
        public double QoL, Nutrition, Clothing, Housing, Years, Welfare;
        // Contract 11: realised trade utility over the step (person-QoL-years, true valuations).
        public double TradeUtility;
        public bool Terminated, Founded;
        public ElderModel9Tensor Observation;
        public ElderModel9Label Teacher;   // the heuristic's (or the compiled directive's) decision this step
        public ElderIntervention[] Interventions = Array.Empty<ElderIntervention>();
        public string Staffing = "";   // compiled economic intent (diagnostic)
        public float[] Privileged;      // ElderModel9Contract.PrivilegedFeatures; critic-only
    }

    [Serializable]
    public sealed class ElderModel9Step
    {
        public int Turn;
        public double Years;
        public string Fingerprint;
        public List<ElderModel9Village> Villages = new List<ElderModel9Village>();
        public double NativeMilliseconds;
        // Diagnostic phase timings (never authoritative): heuristic decide, prepare/compile, teacher labels,
        // resolution, fingerprint, observation build, privileged features and tensor encoding.
        public double DecideMs, PrepareMs, LabelMs, ResolveMs, FingerprintMs, ObserveMs, PrivilegedMs, EncodeMs;
        public List<string> Rejections = new List<string>();
        public Dictionary<int, string> Orders = new Dictionary<int, string>();
    }

    [Serializable]
    public sealed class ElderModel9ReplayStep
    {
        public Dictionary<int, ElderDirective> Directives = new Dictionary<int, ElderDirective>();
        public Dictionary<int, List<Order>> Background = new Dictionary<int, List<Order>>();
        public List<DecisionTrace> Traces = new List<DecisionTrace>();
        public string PostFingerprint;
    }

    [Serializable]
    public sealed class ElderModel9Replay
    {
        public byte[] InitialSnapshot;
        public int StartTurn;
        public int WithheldTechnologies;
        public string InitialFingerprint, ConfigurationHash, BuildHash;
        public bool MovementEnabled;
        public List<ElderModel9ReplayStep> Steps = new List<ElderModel9ReplayStep>();
    }

    // Multi-village environment: every camp with a resident economy is a village; daughters join when founded.
    // A village without a submitted directive runs the native heuristic (teacher), so an empty action map is the
    // ordinary game. Determinism: background orders are canonical and the resolver commits in ID order.
    public sealed class ElderModel9Environment
    {
        private readonly PhaseExecution execution;
        private readonly IElderDirectiveCompiler compiler = new NativeElderDirectiveCompiler();
        private readonly ElderObservationBuilder builder = new ElderObservationBuilder { ValueOrderedSites = true };
        private readonly Dictionary<int, ElderModel9Context> contexts = new Dictionary<int, ElderModel9Context>();
        private Dictionary<int, ElderObservation> observations = new Dictionary<int, ElderObservation>();
        private Dictionary<int, ElderObservationMetadata> metadata = new Dictionary<int, ElderObservationMetadata>();
        public WorldState World { get; private set; }
        public ElderModel9Replay Replay { get; private set; }
        public bool MovementEnabled { get; private set; }
        public bool TeacherLabels = true;

        public ElderModel9Environment(PhaseExecution execution = null) { this.execution = execution ?? new PhaseExecution(); }

        public static string BuildHash => typeof(ElderModel9Environment).Assembly.ManifestModule.ModuleVersionId.ToString("N");

        // A shipped-configuration Small world played by the heuristic through its opening (the first breakthroughs),
        // the start state of every model9 episode. Deterministic in (seed, players).
        public static byte[] GenerateOpening(int seed, int players, out int openingTurns, int maxTurns = 40)
            => GenerateOpening(seed, players, out openingTurns, maxTurns, 0);

        // withheldTechnologies: SimulationConfig.TrainingWithheldTechnologies for capability fixtures.
        // Optional configuration override for calibration sweeps (headless --set); saved with the generated world.
        public static Action<SimulationConfig> ConfigOverride;

        public static byte[] GenerateOpening(int seed, int players, out int openingTurns, int maxTurns, int withheldTechnologies)
        {
            var cfg = SimulationConfig.Small(players);
            ConfigOverride?.Invoke(cfg);
            cfg.Validate();
            var world = new WorldGenerator().Generate(seed, cfg); var resolver = new TurnResolver();
            world.Config.TrainingWithheldTechnologies = withheldTechnologies;
            openingTurns = 0;
            while (!world.Finished && openingTurns < maxTurns)
            {
                var plan = AiPlanning.Decide(world);
                resolver.Resolve(world, plan.Orders, plan.Traces);
                openingTurns++;
            }
            return SaveCodec.Save(world);
        }

        // Training-only critic features (ElderModel9Contract.PrivilegedFeatures): physical headroom the actor's capped
        // observation does not carry, plus lineage and episode context. Pure reads of committed state.
        private float[] Privileged(Camp camp, int priorPopulation, int age)
        {
            var w = World; var cfg = w.Config;
            double meals = Math.Max(1e-9, Math.Max(1, camp.Population) * cfg.Consumption * cfg.GenerationIntervals);
            var profile = HerdConsumptionProfile.Owned(w, camp);
            var site = SettlementSiteEvaluation.Evaluate(w, camp, camp.Tile, camp.Population, profile);
            double potential = 0, food = 0;
            foreach (int tile in w.Map.Radius(camp.Tile, 1))
            {
                var ecology = LazyEcology.ProjectEcology(w, tile, w.Days);
                potential += Math.Max(0, ecology.PotentialFood); food += Math.Max(0, ecology.Food);
            }
            double animals = Math.Max(1, profile.Animals);
            int daughters = w.Societies.Count(s => s.ParentSociety == camp.SocietyId);
            double housing = NeolithicProduction.FixedHousingAt(w, camp, w.Days) * cfg.Neolithic.HousingPeoplePerUnit / Math.Max(1, camp.Population);
            double growth = priorPopulation > 0 ? camp.Population / (double)priorPopulation - 1 : 0;
            return new[] { Math.Log(1 + Math.Max(0, camp.Population)), age, site.HumanFood / meals, potential / meals, food / meals,
                site.SustainableHerdCapacity / animals, site.AbsoluteHerdCapacity / animals, growth, daughters, housing }
                .Select(v => (float)(ElderRLContract.Finite(v) ? v : 0)).ToArray();
        }

        // Training-only capability fixture mask, re-applied after every load (it is never saved).
        public int WithheldTechnologies;

        public ElderModel9Step Reset(byte[] snapshot, bool movementEnabled)
        {
            World = SaveCodec.Load((byte[])snapshot.Clone());
            World = SaveCodec.Load(SaveCodec.Save(World));
            World.Config.TrainingWithheldTechnologies = WithheldTechnologies;
            MovementEnabled = movementEnabled; contexts.Clear(); lastPrior = new Dictionary<int, int>(); Forked = false;
            ResolutionMetrics.CaptureDemandAllocations = true;
            var initial = SaveCodec.Save(World);
            Replay = new ElderModel9Replay { StartTurn = World.Turn, InitialSnapshot = initial, InitialFingerprint = SaveCodec.Fingerprint(World),
                ConfigurationHash = SaveCodec.ConfigurationHash(World.Config), BuildHash = BuildHash, MovementEnabled = movementEnabled,
                WithheldTechnologies = WithheldTechnologies };
            var step = new ElderModel9Step { Turn = World.Turn, Fingerprint = Replay.InitialFingerprint };
            Observe(step, new Dictionary<int, int>());
            return step;
        }

        // Search forks. Fork rebases the live environment on its own saved world, so the live path and every branch
        // restored from the fork start from byte-identical state with the same per-village context: branches are exact
        // paired comparisons. Restore returns the decision step (observations; no rewards). After a fork the replay
        // record is not verifiable (Forked), because verification does not carry the environment context.
        private Dictionary<int, int> lastPrior = new Dictionary<int, int>();
        public bool Forked { get; private set; }

        public ElderModel9Fork Fork(out ElderModel9Step step)
        {
            if (World == null) throw new InvalidOperationException("Reset required.");
            var fork = new ElderModel9Fork
            {
                World = SaveCodec.Save(World), Contexts = contexts.ToDictionary(p => p.Key, p => p.Value.Copy()),
                Prior = new Dictionary<int, int>(lastPrior), ReplaySteps = Replay.Steps.Count
            };
            step = Restore(fork);
            return fork;
        }

        public ElderModel9Step Restore(ElderModel9Fork fork)
        {
            World = SaveCodec.Load((byte[])fork.World.Clone());
            World.Config.TrainingWithheldTechnologies = WithheldTechnologies;
            contexts.Clear();
            foreach (var pair in fork.Contexts) contexts[pair.Key] = pair.Value.Copy();
            lastPrior = new Dictionary<int, int>(fork.Prior);
            if (Replay.Steps.Count > fork.ReplaySteps) Replay.Steps.RemoveRange(fork.ReplaySteps, Replay.Steps.Count - fork.ReplaySteps);
            Forked = true;
            var step = new ElderModel9Step { Turn = World.Turn, Fingerprint = SaveCodec.Fingerprint(World) };
            Observe(step, new Dictionary<int, int>(lastPrior));
            return step;
        }

        public IEnumerable<int> Villages() => World.Camps.Where(c => c.Population > 0 && CultivationEconomy.EconomyEnabled(World, c)).Select(c => c.Id).OrderBy(id => id);

        // Submitted directives for some villages; the rest run the heuristic.
        public ElderModel9Step Step(IReadOnlyDictionary<int, ElderDirective> submitted)
        {
            if (World == null) throw new InvalidOperationException("Reset required.");
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var phase = System.Diagnostics.Stopwatch.StartNew();
            var plan = AiPlanning.Decide(World, execution);
            double decideMs = phase.Elapsed.TotalMilliseconds;
            var background = plan.Orders.ToDictionary(p => p.Key, p => p.Value.Select(o => o.Copy()).ToList());
            var directives = new Dictionary<int, ElderDirective>();
            foreach (var pair in (submitted ?? new Dictionary<int, ElderDirective>()).OrderBy(p => p.Key))
            {
                var camp = World.Camps.FirstOrDefault(c => c.Id == pair.Key);
                if (camp == null || pair.Value == null || !observations.ContainsKey(camp.Id)) continue;
                var directive = pair.Value.Copy();
                if (!MovementEnabled) directive.MovementCandidate = ElderModel9Contract.NativeMovement;
                directives[camp.Id] = directive;
                if (!background.TryGetValue(camp.Player, out var orders)) background[camp.Player] = orders = new List<Order>();
                // One ordinary order per camp: a learned move replaces whatever the heuristic ordered; staying keeps a
                // non-movement order (e.g. a hunter dispatch) and otherwise replaces a heuristic move with NoOp.
                // NativeMovement keeps the heuristic's ordinary orders; FissionPolicy -1 keeps its policy order.
                int movement = directive.MovementCandidate;
                var meta = metadata[camp.Id];
                var mask = observations[camp.Id].Mask;
                bool move = movement >= 0 && movement < meta.SiteTiles.Length && mask.AllowsMovement(movement);
                // Contract 10: a region row or keep-intent names its target for the intent and steps one tile along the
                // known path (the current-tile row is stay).
                int regionStep = -1;
                if (directive.SchemaVersion >= ElderModel9Contract.MovementSchemaVersion && mask.AllowsMovement(movement) &&
                    (ElderMovement.IsRegion(movement) || movement == ElderMovement.KeepIntent))
                {
                    int row = movement == ElderMovement.KeepIntent ? meta.IntentRow : movement - ElderMovement.RegionBase;
                    if (row >= 0 && row < meta.RegionTiles.Length)
                    {
                        directive.IntentTile = meta.RegionTiles[row];
                        if (meta.RegionFirstSteps[row] != camp.Tile) regionStep = meta.RegionFirstSteps[row];
                    }
                }
                if (directive.FissionPolicy >= 0) orders.RemoveAll(o => o.Entity == camp.Id && o.Kind == OrderKind.FissionPolicy);
                if (movement == ElderModel9Contract.NativeMovement) { }
                else if (move)
                {
                    orders.RemoveAll(o => o.Entity == camp.Id && o.Kind != OrderKind.Economy);
                    orders.Add(new Order(OrderKind.Move, camp.Id, destination: meta.SiteTiles[movement]));
                }
                else if (regionStep >= 0)
                {
                    orders.RemoveAll(o => o.Entity == camp.Id && o.Kind != OrderKind.Economy);
                    orders.Add(new Order(OrderKind.Move, camp.Id, destination: regionStep));
                }
                else if (orders.RemoveAll(o => o.Entity == camp.Id && o.Kind == OrderKind.Move) > 0 || !orders.Any(o => o.Entity == camp.Id && o.Kind != OrderKind.Economy))
                    orders.Add(new Order(OrderKind.NoOp, camp.Id));
                if (directive.FissionPolicy >= 0 && directive.FissionPolicy != (int)camp.FissionPolicy)
                    orders.Add(new Order(OrderKind.FissionPolicy, camp.Id, target: directive.FissionPolicy));
            }
            var result = Advance(background, directives, plan.Traces);
            result.DecideMs = decideMs;
            result.NativeMilliseconds = timer.Elapsed.TotalMilliseconds;
            return result;
        }

        private ElderModel9Step Advance(Dictionary<int, List<Order>> background, Dictionary<int, ElderDirective> directives, List<DecisionTrace> traces)
        {
            var prior = World.Camps.ToDictionary(c => c.Id, c => c.Population);
            var priorSociety = World.Camps.ToDictionary(c => c.Id, c => c.SocietyId);
            var phase = System.Diagnostics.Stopwatch.StartNew();
            PreparedTurn prepared = directives.Count > 0
                ? TurnPlanner.PrepareWithElderDirectives(World, background, directives,
                    directives.Keys.ToDictionary(id => id, id => observations[id]), compiler, traces ?? new List<DecisionTrace>(), execution)
                : TurnPlanner.Prepare(World, background, traces ?? new List<DecisionTrace>());
            double prepareMs = phase.Elapsed.TotalMilliseconds; phase.Restart();
            // Teacher labels from the exact allocations Prepare used (captured before resolution mutates the world).
            var labels = new Dictionary<int, ElderModel9Label>();
            var interventions = new Dictionary<int, ElderIntervention[]>(); var staffing = new Dictionary<int, string>();
            foreach (var id in prepared.AutomatedCamps)
            {
                var camp = World.Camps.FirstOrDefault(c => c.Id == id);
                if (camp == null || !observations.ContainsKey(id)) continue;
                var intent = prepared.Orders.FirstOrDefault(o => o.Entity == id && o.Kind == OrderKind.Economy)?.Economy;
                DemandController.Allocation allocation = null;
                if (prepared.ElderDecisions.TryGetValue(id, out var decision)) { interventions[id] = decision.Interventions; allocation = decision.Allocation; }
                if (allocation == null) prepared.DemandAllocations.TryGetValue(id, out allocation);
                if (allocation == null || !TeacherLabels) continue;
                background.TryGetValue(camp.Player, out var orders);
                labels[id] = ElderModel9Teacher.Label(World, camp, intent, allocation, orders, metadata[id]);
                staffing[id] = intent == null ? "" : intent.Stamp;
                var context = Context(id);
                context.HasPrevious = true;
                Array.Copy(allocation.ClothingRouteFeasible, context.ClothingRouteFeasible, 2);
                Array.Copy(allocation.ShelterRouteFeasible, context.ShelterRouteFeasible, 2);
                Array.Copy(allocation.ServiceRequested, context.PreviousServiceRequested, context.PreviousServiceRequested.Length);
                context.PreviousFrontierFishers = allocation.Fishers; context.PreviousMarginalCatchRatio = allocation.FrontierMarginalCatchRatio;
                context.PreviousFishingFood = allocation.FishingFood; context.PreviousMeals = allocation.Meals;
                context.PreviousFishingDevelopment = allocation.FishingDevelopmentGranted;
                context.PreviousFishingShare = labels[id].Action.FishingDevelopmentShare;
                if (decision != null) context.PreviousDecision = decision;
            }
            double labelMs = phase.Elapsed.TotalMilliseconds; phase.Restart();
            var record = new TurnResolver(execution).Resolve(World, prepared);
            double resolveMs = phase.Elapsed.TotalMilliseconds; phase.Restart();
            double years = record.Duration / World.Config.DaysPerYear;
            var step = new ElderModel9Step { Turn = World.Turn, Years = years, Fingerprint = SaveCodec.Fingerprint(World) };
            step.PrepareMs = prepareMs; step.LabelMs = labelMs; step.ResolveMs = resolveMs; step.FingerprintMs = phase.Elapsed.TotalMilliseconds;
            step.Rejections.AddRange(prepared.Rejections);
            foreach (var g in prepared.Orders.GroupBy(o => o.Entity)) step.Orders[g.Key] = string.Join("|", g.Select(o => o.Kind + ":" + o.Target + ":" + o.Destination + ":" + o.Population));
            Replay.Steps.Add(new ElderModel9ReplayStep
            {
                Directives = directives.ToDictionary(p => p.Key, p => p.Value.Copy()),
                Background = background.ToDictionary(p => p.Key, p => p.Value.Select(o => o.Copy()).ToList()),
                Traces = (traces ?? new List<DecisionTrace>()).ToList(),
                PostFingerprint = step.Fingerprint
            });
            // Rewards for the villages that decided this step, then observations for the next decision.
            foreach (var id in observations.Keys.OrderBy(id => id))
            {
                var camp = World.Camps.FirstOrDefault(c => c.Id == id);
                prior.TryGetValue(id, out int p0);
                var v = new ElderModel9Village
                {
                    Camp = id, PriorPopulation = p0, Years = years, Terminated = camp == null || camp.Population <= 0,
                    Society = camp?.SocietyId ?? (priorSociety.TryGetValue(id, out int s) ? s : -1),
                    Teacher = labels.TryGetValue(id, out var label) ? label : null, Staffing = staffing.TryGetValue(id, out var st) ? st : "",
                    Interventions = interventions.TryGetValue(id, out var list) ? list ?? Array.Empty<ElderIntervention>() : Array.Empty<ElderIntervention>()
                };
                if (camp != null)
                {
                    v.Player = camp.Player; v.Population = camp.Population; v.QoL = camp.QoL; v.Nutrition = camp.FoodNeedsMetRatio;
                    v.Clothing = camp.ClothPerCapita; v.Housing = camp.HousingPerCapita;
                    v.ParentSociety = World.Societies.FirstOrDefault(x => x.Id == camp.SocietyId)?.ParentSociety ?? 0;
                }
                v.Welfare = v.Terminated ? 0 : p0 * v.QoL * years;
                v.TradeUtility = ElderTrade.RealisedUtility(record, id);
                step.Villages.Add(v);
            }
            lastPrior = prior;
            Observe(step, prior);
            return step;
        }

        private ElderModel9Context Context(int id)
        {
            if (!contexts.TryGetValue(id, out var context)) contexts[id] = context = new ElderModel9Context();
            return context;
        }

        // Builds the next decision's observations; villages founded this turn appear as new decision rows.
        private void Observe(ElderModel9Step step, Dictionary<int, int> prior)
        {
            observations = new Dictionary<int, ElderObservation>(); metadata = new Dictionary<int, ElderObservationMetadata>();
            var rows = step.Villages.ToDictionary(v => v.Camp);
            foreach (int id in Villages())
            {
                var camp = World.Camps.First(c => c.Id == id);
                ElderInstitutionMemorySystem.EnsureInitialized(World, camp);
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var obs = builder.Build(World, id, MovementEnabled, out var meta, Context(id).PreviousDecision);
                ElderModel9Observation.FillDemography(World, camp, obs);
                observations[id] = obs; metadata[id] = meta;
                if (!rows.TryGetValue(id, out var v))
                {
                    v = new ElderModel9Village { Camp = id, Society = camp.SocietyId, Player = camp.Player, Population = camp.Population,
                        QoL = camp.QoL, Founded = !prior.ContainsKey(id) && prior.Count > 0,
                        ParentSociety = World.Societies.FirstOrDefault(x => x.Id == camp.SocietyId)?.ParentSociety ?? 0 };
                    step.Villages.Add(v);
                }
                step.ObserveMs += clock.Elapsed.TotalMilliseconds; clock.Restart();
                var memory = camp.ElderMemory; var context = Context(id);
                bool active = memory != null && memory.HasCommitment && memory.Commitment != null && memory.CommitmentRemaining > 0;
                context.Commitment = active ? memory.Commitment.Copy() : null;
                context.CommitmentRemaining = active ? memory.CommitmentRemaining : 0;
                context.CommitmentAge = active ? memory.CommitmentTurns - memory.CommitmentRemaining : 0;
                context.CommitmentBrokenLastTurn = memory?.LastCommitmentBreak != null &&
                    memory.LastCommitmentBreakDays + World.Config.TurnDays + 1e-6 >= World.Days;
                v.Observation = ElderModel9Encoder.Encode(obs, context);
                step.EncodeMs += clock.Elapsed.TotalMilliseconds; clock.Restart();
                v.Privileged = Privileged(camp, prior.TryGetValue(id, out int before) ? before : camp.Population, step.Turn - Replay.StartTurn);
                step.PrivilegedMs += clock.Elapsed.TotalMilliseconds;
            }
            step.Villages.Sort((a, b) => a.Camp.CompareTo(b.Camp));
        }

        public static void VerifyReplay(ElderModel9Replay replay, PhaseExecution execution = null)
        {
            var world = SaveCodec.Load((byte[])replay.InitialSnapshot.Clone());
            world.Config.TrainingWithheldTechnologies = replay.WithheldTechnologies;
            bool capture = ResolutionMetrics.CaptureDemandAllocations; ResolutionMetrics.CaptureDemandAllocations = true;
            try
            {
                if (SaveCodec.Fingerprint(world) != replay.InitialFingerprint) throw new InvalidOperationException("Replay initial fingerprint mismatch.");
                var builder = new ElderObservationBuilder { ValueOrderedSites = true }; var compiler = new NativeElderDirectiveCompiler();
                var previous = new Dictionary<int, ElderCompiledDecision>();
                int index = 0;
                foreach (var s in replay.Steps)
                {
                    // Mirror the environment exactly: every village is observed (in camp-ID order) before each decision.
                    var observations = new Dictionary<int, ElderObservation>();
                    foreach (var camp in world.Camps.Where(c => c.Population > 0 && CultivationEconomy.EconomyEnabled(world, c)).OrderBy(c => c.Id).ToList())
                    {
                        ElderInstitutionMemorySystem.EnsureInitialized(world, camp);
                        previous.TryGetValue(camp.Id, out var prev);
                        var obs = builder.Build(world, camp.Id, replay.MovementEnabled, out _, prev);
                        ElderModel9Observation.FillDemography(world, camp, obs);
                        observations[camp.Id] = obs;
                    }
                    observations = observations.Where(p => s.Directives.ContainsKey(p.Key)).ToDictionary(p => p.Key, p => p.Value);
                    var background = s.Background.ToDictionary(p => p.Key, p => p.Value.Select(o => o.Copy()).ToList());
                    var prepared = s.Directives.Count > 0
                        ? TurnPlanner.PrepareWithElderDirectives(world, background, s.Directives.ToDictionary(p => p.Key, p => p.Value.Copy()),
                            observations, compiler, s.Traces, execution ?? new PhaseExecution())
                        : TurnPlanner.Prepare(world, background, s.Traces);
                    foreach (var pair in prepared.ElderDecisions) previous[pair.Key] = pair.Value;
                    new TurnResolver(execution ?? new PhaseExecution()).Resolve(world, prepared);
                    if (SaveCodec.Fingerprint(world) != s.PostFingerprint) throw new InvalidOperationException("Replay diverged at step " + index + ".");
                    index++;
                }
            }
            finally { ResolutionMetrics.CaptureDemandAllocations = capture; }
        }
    }

    // Minimal little-endian NPY (format 1.0) writer for teacher-data shards.
    public static class NpyWriter
    {
        public static void Write(string path, float[] data, params int[] shape) => Write(path, "<f4", shape, w => { foreach (var v in data) w.Write(v); });
        public static void Write(string path, int[] data, params int[] shape) => Write(path, "<i4", shape, w => { foreach (var v in data) w.Write(v); });
        public static void Write(string path, bool[] data, params int[] shape) => Write(path, "|u1", shape, w => { foreach (var v in data) w.Write((byte)(v ? 1 : 0)); });
        public static void Write(string path, double[] data, params int[] shape) => Write(path, "<f8", shape, w => { foreach (var v in data) w.Write(v); });

        private static void Write(string path, string descr, int[] shape, Action<BinaryWriter> body)
        {
            string dims = shape.Length == 1 ? shape[0] + "," : string.Join(", ", shape);
            string header = "{'descr': '" + descr + "', 'fortran_order': False, 'shape': (" + dims + "), }";
            int total = 10 + header.Length + 1;
            int pad = (64 - total % 64) % 64;
            header = header + new string(' ', pad) + "\n";
            using (var stream = File.Create(path))
            using (var w = new BinaryWriter(stream))
            {
                w.Write(new byte[] { 0x93, (byte)'N', (byte)'U', (byte)'M', (byte)'P', (byte)'Y', 1, 0 });
                w.Write((ushort)header.Length);
                w.Write(Encoding.ASCII.GetBytes(header));
                body(w);
            }
        }
    }
}
