using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Anthropocene.Simulation
{
    public static class ElderActorLaborContract
    {
        public const int NativeContractVersion = 13, ActionSchemaVersion = 12,
            ObservationSchemaVersion = 13, EnvironmentSchemaVersion = 13,
            ReplaySchemaVersion = 13;
        public const int RegistryVersion = 1, CatalogSchemaVersion = 1,
            AttemptSchemaVersion = 1, FeedbackSchemaVersion = 1,
            ReasonRegistryVersion = 1;
        public const int SpecialtyRows = 14, RoleRows = 13, ActivityRows = 22,
            SpecialtyFeatureWidth = 12, ActivityFeatureWidth = 50,
            ResourceRows = 20, ResourceFeatureWidth = 8, GoodsRows = 11,
            MaxProposals = 2, FeasibilityCostMaximum = 319,
            DecisionFeasibilityCostMaximum = 638;

        public static readonly string[] SpecialtyIds = new[] { "idle" }
            .Concat(WorkforceRoles.All).ToArray();
        public static readonly string[] ActivityIds =
        {
            "idle", "food.gather", "stock.stone", "stock.clay", "stock.wood",
            "field.operation", "field.expansion", "herd.management", "herd.expansion",
            "fishing.harvest", "fishing.development", "goods.hand-tools", "goods.pots",
            "goods.cloth", "goods.farm-tools", "goods.housing", "goods.tools",
            "goods.animal-fabric", "goods.light-housing", "goods.herding-tools",
            "goods.housing-material", "goods.fishing-tools"
        };
        public static readonly string[] ResourceIds =
        {
            "resident-work", "stone", "clay", "wood", "plant-food", "animal-food",
            "animal-fabric", "housing-material", "cloth", "hand-tools", "tools",
            "farm-tools", "herding-tools", "fishing-tools", "terrestrial-food",
            "aquatic-food", "pasture-food", "freshwater", "cultivable-capital", "food-room"
        };
        public static readonly string[] ReasonIds =
        {
            "none", "catalog-unavailable", "unsupported-action-schema",
            "specialty-row-count", "specialty-nonfinite", "specialty-negative",
            "specialty-masked-nonzero", "specialty-sum", "activity-specialty-row-count",
            "activity-row-count", "activity-nonfinite", "activity-negative",
            "activity-masked-nonzero", "activity-sum", "fallback-after-retry",
            "actual-work-unavailable", "planned-output-unavailable",
            "realized-output-unavailable", "resource-receipt-unavailable", "camp-vanished"
        };
        public static readonly string RegistryDigest = HashRegistry(SpecialtyIds,
            ActivityIds, RegistryVersion, SpecialtyFeatureWidth, ActivityFeatureWidth);
        public static readonly string ReasonRegistryDigest = HashRegistry(ReasonIds,
            Array.Empty<string>(), ReasonRegistryVersion, 0, 0);

        static ElderActorLaborContract()
        {
            // The current simulator registry is fixed-width, while the learner scores
            // the emitted tokens with shared projectors.  A new physical specialty or
            // activity therefore requires an explicit registry/version migration; it
            // must never silently resize the native matrices under an old digest.
            if (SpecialtyIds.Length != SpecialtyRows ||
                WorkforceRoles.All.Length != RoleRows ||
                ActivityIds.Length != ActivityRows ||
                ResourceIds.Length != ResourceRows ||
                Enum.GetValues(typeof(CraftGood)).Length != GoodsRows)
                throw new InvalidOperationException(
                    "Actor-labor registry dimensions changed without a contract migration.");
        }

        internal static int FlatActivity(int role, int activity)
            => checked(role * ActivityRows + activity);
        internal static int ReasonCode(string id)
        {
            int code = Array.IndexOf(ReasonIds, id);
            return code < 0 ? 0 : code;
        }
        private static string HashRegistry(string[] first, string[] second,
            int version, int firstWidth, int secondWidth)
        {
            string payload = version.ToString(CultureInfo.InvariantCulture) + "|" +
                firstWidth + "|" + secondWidth + "|" + string.Join("|", first) +
                (second.Length == 0 ? "" : "|" + string.Join("|", second));
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(payload)))
                    .Replace("-", "").ToLowerInvariant();
        }
    }

    [Serializable]
    public sealed class ElderActorLaborViolation
    {
        public int ReasonCode;
        public string Reason, Knowledge, Unit, Phase;
        public int[] SpecialtyRows = Array.Empty<int>(), ActivityRows = Array.Empty<int>();
        public double Requested, Available, Severity;
        public bool RequestedKnown = true, AvailableKnown = true;
        public bool? Avoidable;
        public ElderActorLaborViolation Copy() => new ElderActorLaborViolation
        {
            ReasonCode = ReasonCode, Reason = Reason, Knowledge = Knowledge, Unit = Unit,
            Phase = Phase, SpecialtyRows = (int[])SpecialtyRows.Clone(),
            ActivityRows = (int[])ActivityRows.Clone(), Requested = Requested,
            Available = Available, Severity = Severity, RequestedKnown = RequestedKnown,
            AvailableKnown = AvailableKnown, Avoidable = Avoidable
        };
    }

    [Serializable]
    public sealed class ElderActorLaborSpecialtyRow
    {
        public int Row;
        public string Id, RequiredTechnology, UnavailableReason;
        public bool Present, Known, ActorControllable, WholeResidents;
        public double[] Features = new double[ElderActorLaborContract.SpecialtyFeatureWidth];
        public ElderActorLaborSpecialtyRow Copy()
        {
            var copy = (ElderActorLaborSpecialtyRow)MemberwiseClone();
            copy.Features = (double[])Features.Clone(); return copy;
        }
    }

    [Serializable]
    public sealed class ElderActorLaborActivityRow
    {
        public int Row, SpecialtyRow, ActivityRow;
        public double RecipeScheduleOrdinal;
        public string SpecialtyId, ActivityId, Phase, Unit, OutputGood, OutputUnit,
            UnavailableReason;
        public bool Present, Known, ActorControllable, FractionalWorkerTime,
            Nonlinear, VisibleCompetitionPossible, MaxWorkerYearsKnown,
            OutputCapacityKnown, SpecialistOnly, IsOperationOrHarvest,
            IsDevelopmentOrExpansion, OutputsStone, OutputsClay, OutputsWood;
        public double MaxWorkerYears, CapacityPerWorkerYearLower,
            CapacityPerWorkerYearUpper, StonePerOutput, ClayPerOutput, WoodPerOutput,
            PlantFoodPerOutput, AnimalFoodPerOutput, AnimalFabricPerOutput,
            HousingMaterialPerOutput, ClothPerOutput, ConstructionMaterialPerOutput,
            ToolServicePerOutput;
        public double[] Features = new double[ElderActorLaborContract.ActivityFeatureWidth];
        public ElderActorLaborActivityRow Copy()
        {
            var copy = (ElderActorLaborActivityRow)MemberwiseClone();
            copy.Features = (double[])Features.Clone(); return copy;
        }
    }

    [Serializable]
    public sealed class ElderActorLaborResourceRow
    {
        public int Row;
        public string ResourceId, Unit, Phase, Reason;
        public bool Present, Known, CompetitionPossible;
        public double Opening, LocallyAccessible, FixedReserved;
        public double[] Features = new double[ElderActorLaborContract.ResourceFeatureWidth];
        public ElderActorLaborResourceRow Copy()
        {
            var copy = (ElderActorLaborResourceRow)MemberwiseClone();
            copy.Features = (double[])Features.Clone(); return copy;
        }
    }

    [Serializable]
    public sealed class ElderActorLaborCatalog
    {
        public int SchemaVersion = ElderActorLaborContract.CatalogSchemaVersion;
        public int RegistryVersion = ElderActorLaborContract.RegistryVersion;
        public int ReasonRegistryVersion = ElderActorLaborContract.ReasonRegistryVersion;
        public int CampId, Turn, Population;
        public double TurnYears;
        public string[] SpecialtyIds = Array.Empty<string>(), ActivityIds = Array.Empty<string>(),
            ReasonIds = Array.Empty<string>();
        public ElderActorLaborSpecialtyRow[] SpecialtyRows = Array.Empty<ElderActorLaborSpecialtyRow>();
        public bool[] SpecialtyMask = Array.Empty<bool>();
        public bool[][] ActivityMask = Array.Empty<bool[]>();
        public ElderActorLaborActivityRow[] ActivityRows = Array.Empty<ElderActorLaborActivityRow>();
        public ElderActorLaborResourceRow[] Resources = Array.Empty<ElderActorLaborResourceRow>();
        public string RegistryDigest, ReasonRegistryDigest, Digest;
        public ElderActorLaborCatalog Copy() => new ElderActorLaborCatalog
        {
            SchemaVersion = SchemaVersion, RegistryVersion = RegistryVersion,
            ReasonRegistryVersion = ReasonRegistryVersion, CampId = CampId, Turn = Turn,
            Population = Population, TurnYears = TurnYears,
            SpecialtyIds = (string[])SpecialtyIds.Clone(), ActivityIds = (string[])ActivityIds.Clone(),
            ReasonIds = (string[])ReasonIds.Clone(),
            SpecialtyRows = SpecialtyRows.Select(row => row.Copy()).ToArray(),
            SpecialtyMask = (bool[])SpecialtyMask.Clone(),
            ActivityMask = ActivityMask.Select(row => (bool[])row.Clone()).ToArray(),
            ActivityRows = ActivityRows.Select(row => row.Copy()).ToArray(),
            Resources = Resources.Select(row => row.Copy()).ToArray(),
            RegistryDigest = RegistryDigest, ReasonRegistryDigest = ReasonRegistryDigest,
            Digest = Digest
        };
    }

    [Serializable]
    public sealed class ElderActorLaborAttempt
    {
        public int SchemaVersion = ElderActorLaborContract.AttemptSchemaVersion;
        public int CampId, Attempt, RemainingProposals;
        public bool Valid, Fallback;
        public double[] RequestedSpecialtyAllocation = new double[ElderActorLaborContract.SpecialtyRows];
        public double[] RequestedSpecialtyResidents = new double[ElderActorLaborContract.SpecialtyRows];
        public int[] AcceptedSpecialtyResidents = new int[ElderActorLaborContract.SpecialtyRows];
        public double[][] RequestedActivityAllocation = Matrix(),
            RequestedActivityWorkerYears = Matrix(), PlannedActivityWorkerYears = Matrix();
        public ElderActorLaborViolation[] Violations = Array.Empty<ElderActorLaborViolation>();
        public string CatalogDigest;
        public double FeasibilityCost;
        internal static double[][] Matrix() => Enumerable.Range(0,
            ElderActorLaborContract.RoleRows).Select(_ =>
                new double[ElderActorLaborContract.ActivityRows]).ToArray();
        internal static double[][] Clone(double[][] source) => source?.Select(row =>
            (double[])row?.Clone()).ToArray();
        public ElderActorLaborAttempt Copy() => new ElderActorLaborAttempt
        {
            SchemaVersion = SchemaVersion, CampId = CampId, Attempt = Attempt,
            RemainingProposals = RemainingProposals, Valid = Valid, Fallback = Fallback,
            RequestedSpecialtyAllocation = (double[])RequestedSpecialtyAllocation.Clone(),
            RequestedSpecialtyResidents = (double[])RequestedSpecialtyResidents.Clone(),
            AcceptedSpecialtyResidents = (int[])AcceptedSpecialtyResidents.Clone(),
            RequestedActivityAllocation = Clone(RequestedActivityAllocation),
            RequestedActivityWorkerYears = Clone(RequestedActivityWorkerYears),
            PlannedActivityWorkerYears = Clone(PlannedActivityWorkerYears),
            Violations = Violations.Select(item => item.Copy()).ToArray(),
            CatalogDigest = CatalogDigest, FeasibilityCost = FeasibilityCost
        };
    }

    [Serializable]
    public sealed class ElderActorLaborFeedback
    {
        public int SchemaVersion = ElderActorLaborContract.FeedbackSchemaVersion;
        public int CampId, DecisionTurn, ExecutionComparatorAttempt, FallbackReasonCode;
        public string ParentDecisionId, FoundingParentDecisionId, FallbackReason;
        public bool Fallback;
        public ElderActorLaborCatalog DecisionCatalog;
        public ElderActorLaborAttempt InitialRequest, FinalSampledRequest;
        public double[][] CommittedActivityWorkerYears = ElderActorLaborAttempt.Matrix();
        public double?[][] ActualSchedulerConsumedWorkerYears = NullableMatrix();
        public bool[][] ActualWorkerYearsKnown = BoolMatrix();
        public string[][] ActualWorkerYearsReasons = StringMatrix();
        public int[][] ActualWorkerYearsReasonCodes = IntMatrix();
        public double?[] PlannedGoodsOutput = new double?[ElderActorLaborContract.GoodsRows],
            RealizedGoodsOutput = new double?[ElderActorLaborContract.GoodsRows],
            RealizedResourceReceipts = new double?[ElderActorLaborContract.ResourceRows];
        public bool[] PlannedGoodsOutputKnown = new bool[ElderActorLaborContract.GoodsRows],
            GoodsOutputKnown = new bool[ElderActorLaborContract.GoodsRows],
            ResourceReceiptKnown = new bool[ElderActorLaborContract.ResourceRows];
        public string[] PlannedGoodsOutputReasons = new string[ElderActorLaborContract.GoodsRows],
            GoodsOutputReasons = new string[ElderActorLaborContract.GoodsRows],
            ResourceReceiptReasons = new string[ElderActorLaborContract.ResourceRows];
        public int[] PlannedGoodsOutputReasonCodes = new int[ElderActorLaborContract.GoodsRows],
            GoodsOutputReasonCodes = new int[ElderActorLaborContract.GoodsRows],
            ResourceReceiptReasonCodes = new int[ElderActorLaborContract.ResourceRows];
        private static double?[][] NullableMatrix() => Enumerable.Range(0,
            ElderActorLaborContract.RoleRows).Select(_ => new double?[ElderActorLaborContract.ActivityRows]).ToArray();
        private static bool[][] BoolMatrix() => Enumerable.Range(0,
            ElderActorLaborContract.RoleRows).Select(_ => new bool[ElderActorLaborContract.ActivityRows]).ToArray();
        private static string[][] StringMatrix() => Enumerable.Range(0,
            ElderActorLaborContract.RoleRows).Select(_ => new string[ElderActorLaborContract.ActivityRows]).ToArray();
        private static int[][] IntMatrix() => Enumerable.Range(0,
            ElderActorLaborContract.RoleRows).Select(_ => new int[ElderActorLaborContract.ActivityRows]).ToArray();
        public ElderActorLaborFeedback Copy()
        {
            var copy = (ElderActorLaborFeedback)MemberwiseClone();
            copy.DecisionCatalog = DecisionCatalog?.Copy(); copy.InitialRequest = InitialRequest?.Copy();
            copy.FinalSampledRequest = FinalSampledRequest?.Copy();
            copy.CommittedActivityWorkerYears = ElderActorLaborAttempt.Clone(CommittedActivityWorkerYears);
            copy.ActualSchedulerConsumedWorkerYears = ActualSchedulerConsumedWorkerYears.Select(row => (double?[])row.Clone()).ToArray();
            copy.ActualWorkerYearsKnown = ActualWorkerYearsKnown.Select(row => (bool[])row.Clone()).ToArray();
            copy.ActualWorkerYearsReasons = ActualWorkerYearsReasons.Select(row => (string[])row.Clone()).ToArray();
            copy.ActualWorkerYearsReasonCodes = ActualWorkerYearsReasonCodes.Select(row => (int[])row.Clone()).ToArray();
            copy.PlannedGoodsOutput = (double?[])PlannedGoodsOutput.Clone(); copy.RealizedGoodsOutput = (double?[])RealizedGoodsOutput.Clone();
            copy.RealizedResourceReceipts = (double?[])RealizedResourceReceipts.Clone();
            copy.PlannedGoodsOutputKnown = (bool[])PlannedGoodsOutputKnown.Clone(); copy.GoodsOutputKnown = (bool[])GoodsOutputKnown.Clone();
            copy.ResourceReceiptKnown = (bool[])ResourceReceiptKnown.Clone();
            copy.PlannedGoodsOutputReasons = (string[])PlannedGoodsOutputReasons.Clone(); copy.GoodsOutputReasons = (string[])GoodsOutputReasons.Clone();
            copy.ResourceReceiptReasons = (string[])ResourceReceiptReasons.Clone();
            copy.PlannedGoodsOutputReasonCodes = (int[])PlannedGoodsOutputReasonCodes.Clone(); copy.GoodsOutputReasonCodes = (int[])GoodsOutputReasonCodes.Clone();
            copy.ResourceReceiptReasonCodes = (int[])ResourceReceiptReasonCodes.Clone(); return copy;
        }
    }

    internal sealed class ElderActorLaborRejectedException : InvalidOperationException
    {
        internal readonly ElderActorLaborAttempt Attempt;
        internal ElderActorLaborRejectedException(ElderActorLaborAttempt attempt)
            : base("Actor-labor proposal requires rejection/retry before turn execution.")
            => Attempt = attempt?.Copy();
    }

    internal static class ElderActorLaborCompiler
    {
        private const double Epsilon = 1e-9;
        internal static ElderActorLaborCatalog BuildCatalog(WorldState world, Camp camp,
            FishingAccessPlan fishingAccess = null)
        {
            if (world == null || camp == null) throw new ArgumentNullException();
            double years = world.Config.TurnDays / world.Config.DaysPerYear;
            if (!(years > 0) || !ElderRLContract.Finite(years)) throw new InvalidOperationException("Invalid actor-labor horizon.");
            var demand = CampProductionDemand.BuildPhysical(world, camp, EconomicIntent.From(camp), false, true, 0, fishingAccess);
            var specialtyMask = new bool[ElderActorLaborContract.SpecialtyRows]; specialtyMask[0] = true;
            var specialties = new ElderActorLaborSpecialtyRow[ElderActorLaborContract.SpecialtyRows];
            specialties[0] = Specialty(0, "idle", true, null, true, false, false, false, false, false, false);
            for (int role = 0; role < ElderActorLaborContract.RoleRows; role++)
            {
                string id = WorkforceRoles.All[role], tech = EconomicIntents.RequiredTechnology(id);
                bool techOk = tech == null || camp.People.HasTech(tech);
                bool capable = techOk && (id != WorkforceRoles.Farmer || demand.CultivationEnabled) &&
                    (id != WorkforceRoles.Herder || demand.HusbandryEnabled) &&
                    (id != WorkforceRoles.Fisher || FishingEconomy.FeatureEnabled(world, camp));
                specialtyMask[role + 1] = capable;
                specialties[role + 1] = Specialty(role + 1, id, capable, tech, techOk,
                    id == WorkforceRoles.Gatherer,
                    id == WorkforceRoles.Gatherer || id == WorkforceRoles.Stonecutter || id == WorkforceRoles.ClayMiner || id == WorkforceRoles.Woodcutter,
                    id == WorkforceRoles.Farmer, id == WorkforceRoles.Herder, id == WorkforceRoles.Fisher,
                    id == WorkforceRoles.Craftsperson || WorkforceRoles.Specialists.Contains(id));
            }
            var masks = Enumerable.Range(0, ElderActorLaborContract.RoleRows).Select(_ => new bool[ElderActorLaborContract.ActivityRows]).ToArray();
            var rows = new List<ElderActorLaborActivityRow>(ElderActorLaborContract.RoleRows * ElderActorLaborContract.ActivityRows);
            var recipes = demand.Recipes.ToDictionary(recipe => recipe.Good);
            for (int role = 0; role < ElderActorLaborContract.RoleRows; role++)
                for (int activity = 0; activity < ElderActorLaborContract.ActivityRows; activity++)
                {
                    var row = NewActivity(role, activity, WorkforceRoles.All[role], years, camp.Population);
                    // Every row keeps explicit idle even when the specialty token
                    // is masked. This gives the policy one uniform legal simplex
                    // and prevents unavailable rows from requiring an empty-softmax
                    // special case. Non-idle activity still requires the specialty.
                    bool enabled = activity == 0 || specialtyMask[role + 1] &&
                        ActivityAvailable(world, camp, fishingAccess, recipes,
                            WorkforceRoles.All[role], activity, row);
                    row.Present = row.Known = row.ActorControllable = enabled; masks[role][activity] = enabled;
                    if (!enabled && row.UnavailableReason == null) row.UnavailableReason = specialtyMask[role + 1] ? "activity-not-supported" : "specialty-unavailable";
                    row.Features = ActivityFeatures(row); rows.Add(row);
                }
            var catalog = new ElderActorLaborCatalog
            {
                CampId = camp.Id, Turn = world.Turn, Population = Math.Max(0, camp.Population), TurnYears = years,
                SpecialtyIds = (string[])ElderActorLaborContract.SpecialtyIds.Clone(), ActivityIds = (string[])ElderActorLaborContract.ActivityIds.Clone(),
                ReasonIds = (string[])ElderActorLaborContract.ReasonIds.Clone(), SpecialtyRows = specialties,
                SpecialtyMask = specialtyMask, ActivityMask = masks, ActivityRows = rows.ToArray(),
                Resources = BuildResources(world, camp, demand, years), RegistryDigest = ElderActorLaborContract.RegistryDigest,
                ReasonRegistryDigest = ElderActorLaborContract.ReasonRegistryDigest
            };
            catalog.Digest = Digest(catalog); return catalog;
        }

        private static ElderActorLaborSpecialtyRow Specialty(int row, string id, bool present, string tech, bool techOk,
            bool food, bool raw, bool field, bool herd, bool fishing, bool craft)
        {
            var result = new ElderActorLaborSpecialtyRow { Row = row, Id = id, RequiredTechnology = tech, Present = present,
                Known = true, ActorControllable = present, WholeResidents = true,
                UnavailableReason = present ? null : tech != null && !techOk ? "required-technology-unavailable" : "physical-capability-unavailable" };
            result.Features = new[] { present ? 1d : 0, 1d, present ? 1d : 0, 1d, row == 0 ? 1d : 0,
                food ? 1d : 0, raw ? 1d : 0, field ? 1d : 0, herd ? 1d : 0, fishing ? 1d : 0,
                craft ? 1d : 0, techOk ? 1d : 0 }; return result;
        }
        private static ElderActorLaborActivityRow NewActivity(int role, int activity, string roleId, double years, int population)
            => new ElderActorLaborActivityRow { Row = ElderActorLaborContract.FlatActivity(role, activity), SpecialtyRow = role,
                ActivityRow = activity, SpecialtyId = roleId, ActivityId = ElderActorLaborContract.ActivityIds[activity],
                Phase = activity >= 11 ? "production" : "work-before-production", Unit = "worker-years",
                FractionalWorkerTime = true, MaxWorkerYearsKnown = true,
                MaxWorkerYears = Math.Max(0, population) * years,
                Nonlinear = activity >= 5 && activity <= 10, VisibleCompetitionPossible = activity >= 1 && activity <= 4,
                IsOperationOrHarvest = activity == 5 || activity == 7 || activity == 9,
                IsDevelopmentOrExpansion = activity == 6 || activity == 8 || activity == 10,
                OutputsStone = activity == 2, OutputsClay = activity == 3,
                OutputsWood = activity == 4,
                OutputGood = activity >= 11 ? ((CraftGood)(activity - 11)).ToString() : null,
                OutputUnit = activity == 1 || activity == 9 ? "food-units" : activity >= 2 && activity <= 4 ? "material-units" :
                    activity == 8 ? "animal-heads" : activity >= 11 ? "good-units" : null };
        private static bool ActivityAvailable(WorldState world, Camp camp, FishingAccessPlan fishingAccess,
            IReadOnlyDictionary<CraftGood, CraftRecipe> recipes, string role, int activity, ElderActorLaborActivityRow row)
        {
            if (activity == 0) return true;
            if (role == WorkforceRoles.Gatherer && activity == 1)
            { row.OutputCapacityKnown = true; row.CapacityPerWorkerYearLower =
                row.CapacityPerWorkerYearUpper = Math.Max(0,
                    world.Config.GatherPerPerson * world.Config.DaysPerYear /
                    world.Config.BaseDays); return true; }
            if (activity >= 2 && activity <= 4)
            {
                MaterialId material = activity == 2 ? MaterialId.Stone : activity == 3 ? MaterialId.Clay : MaterialId.Wood;
                if (role != WorkforceRoles.Gatherer && role != MaterialEconomy.Specialist(material)) return false;
                double rate = SpecialistProductivity.ExtractionRate(world, camp, material, role) * world.Config.DaysPerYear / world.Config.BaseDays;
                row.OutputCapacityKnown = true;
                row.CapacityPerWorkerYearLower =
                    row.CapacityPerWorkerYearUpper = Math.Max(0, rate);
                return rate > Epsilon;
            }
            if (activity == 5 || activity == 6) return role == WorkforceRoles.Farmer && CultivationEconomy.Enabled(camp);
            if (activity == 7 || activity == 8) return role == WorkforceRoles.Herder && CultivationEconomy.HusbandryEnabled(camp);
            if (activity == 9 || activity == 10)
            {
                if (role != WorkforceRoles.Fisher || !FishingEconomy.FeatureEnabled(world, camp)) return false;
                if (fishingAccess?.Grounds == null || fishingAccess.Grounds.Length == 0)
                { row.UnavailableReason = "no-known-fishing-ground-at-accepted-site"; return false; }
                row.OutputCapacityKnown = true;
                row.CapacityPerWorkerYearLower = 0;
                row.CapacityPerWorkerYearUpper = Math.Max(0,
                    fishingAccess.Grounds.Max(item => item.YieldPerFisherYear)); return true;
            }
            if (activity < 11) return false;
            CraftGood good = (CraftGood)(activity - 11);
            if (!recipes.TryGetValue(good, out var recipe) || !NeolithicProduction.RecipeAvailable(world, camp, recipe)) return false;
            bool specialist = role == recipe.Specialist && camp.People.HasTech(recipe.Track);
            if (!specialist && (role != WorkforceRoles.Craftsperson || recipe.SpecialistOnly)) return false;
            double rateOut = SpecialistProductivity.CraftRate(world, camp, recipe, role) * world.Config.DaysPerYear / world.Config.BaseDays;
            if (!(rateOut > Epsilon)) return false;
            row.OutputCapacityKnown = true;
            row.CapacityPerWorkerYearLower = row.CapacityPerWorkerYearUpper = rateOut;
            row.RecipeScheduleOrdinal = NeolithicProduction.ScheduleKey(good);
            row.SpecialistOnly = recipe.SpecialistOnly; row.StonePerOutput = recipe.Stone; row.ClayPerOutput = recipe.Clay;
            row.WoodPerOutput = recipe.Wood; row.PlantFoodPerOutput = recipe.PlantFood; row.AnimalFoodPerOutput = recipe.AnimalFood;
            row.AnimalFabricPerOutput = recipe.AnimalFabric; row.HousingMaterialPerOutput = recipe.HousingMaterial;
            row.ClothPerOutput = recipe.Cloth; row.ConstructionMaterialPerOutput = recipe.ConstructionMaterial;
            row.ToolServicePerOutput = recipe.Equipment ? 1 : 0; return true;
        }
        private static double[] ActivityFeatures(ElderActorLaborActivityRow row)
        {
            int a = row.ActivityRow; double phase = row.Phase == "work-before-production" ? 1 : row.Phase == "production" ? 2 : 0;
            double outputUnit = row.OutputUnit == "good-units" ? 1 : row.OutputUnit == "material-units" ? 2 : row.OutputUnit == "food-units" ? 3 : row.OutputUnit == "animal-heads" ? 4 : 0;
            var features = new List<double> { row.Present ? 1d : 0, row.Known ? 1d : 0, row.ActorControllable ? 1d : 0,
                row.FractionalWorkerTime ? 1d : 0, row.Nonlinear ? 1d : 0, row.VisibleCompetitionPossible ? 1d : 0,
                phase, 1d, row.OutputGood == null ? 0 : a - 10d,
                row.RecipeScheduleOrdinal + 1d, row.MaxWorkerYearsKnown ? 1d : 0,
                row.MaxWorkerYears, row.OutputCapacityKnown ? 1d : 0,
                row.CapacityPerWorkerYearLower, row.CapacityPerWorkerYearUpper,
                row.StonePerOutput,
                row.ClayPerOutput, row.WoodPerOutput, row.PlantFoodPerOutput, row.AnimalFoodPerOutput,
                row.AnimalFabricPerOutput, row.HousingMaterialPerOutput, row.ClothPerOutput, row.ConstructionMaterialPerOutput,
                row.ToolServicePerOutput, row.SpecialistOnly ? 1d : 0, a == 0 ? 1d : 0, a == 1 ? 1d : 0,
                a >= 2 && a <= 4 ? 1d : 0, a == 5 || a == 6 ? 1d : 0, a == 7 || a == 8 ? 1d : 0,
                a == 9 || a == 10 ? 1d : 0, a >= 11 ? 1d : 0, outputUnit,
                row.IsOperationOrHarvest ? 1d : 0,
                row.IsDevelopmentOrExpansion ? 1d : 0,
                row.OutputsStone ? 1d : 0, row.OutputsClay ? 1d : 0,
                row.OutputsWood ? 1d : 0 };
            for (int good = 0; good < ElderActorLaborContract.GoodsRows; good++)
                features.Add(a == 11 + good ? 1d : 0);
            if (features.Count != ElderActorLaborContract.ActivityFeatureWidth)
                throw new InvalidOperationException("Actor-labor activity feature width drift.");
            return features.ToArray();
        }
        private static ElderActorLaborResourceRow[] BuildResources(WorldState world, Camp camp, CampProductionDemand demand, double years)
        {
            var result = Enumerable.Range(0, ElderActorLaborContract.ResourceRows).Select(index => new ElderActorLaborResourceRow
            { Row = index, ResourceId = ElderActorLaborContract.ResourceIds[index], Unit = index == 0 || index == 18 ? "worker-years" :
                index >= 1 && index <= 3 ? "material-units" : index == 16 || index == 17 ? "animal-heads" : "good-units",
                Phase = "opening", Reason = "no-authoritative-local-measurement" }).ToArray();
            void Known(int row, double opening = 0, double accessible = 0, double reserved = 0, string phase = "opening", bool competition = false)
            { var item = result[row]; item.Present = item.Known = true; item.Reason = null; item.Phase = phase; item.Opening = Math.Max(0, opening);
                item.LocallyAccessible = Math.Max(0, accessible); item.FixedReserved = Math.Max(0, reserved); item.CompetitionPossible = competition; }
            Known(0, camp.Population * years);
            Known(1, MaterialEconomy.Inventory(camp, MaterialId.Stone), demand.Accessible.TryGetValue(MaterialId.Stone, out double stone) ? stone : 0, competition: true);
            Known(2, MaterialEconomy.Inventory(camp, MaterialId.Clay), demand.Accessible.TryGetValue(MaterialId.Clay, out double clay) ? clay : 0, competition: true);
            Known(3, MaterialEconomy.Inventory(camp, MaterialId.Wood), demand.Accessible.TryGetValue(MaterialId.Wood, out double wood) ? wood : 0, competition: true);
            Known(4, camp.PlantFood); Known(5, camp.AnimalFood); Known(6, camp.Inventory.AnimalFabric); Known(7, camp.Inventory.HousingMaterial);
            double reserve = NeolithicProduction.TentClothReserve(world, camp, years, camp.Population) + camp.TradeHeldForExport(CraftGood.Cloth);
            Known(8, camp.Inventory.Cloth, 0, Math.Min(Math.Max(0, camp.Inventory.Cloth), Math.Max(0, reserve)));
            Known(9, camp.Inventory.HandTools); Known(10, camp.Inventory.Tools); Known(11, camp.Inventory.FarmTools);
            Known(12, camp.Inventory.HerdingTools); Known(13, camp.Inventory.FishingTools);
            Known(14, 0, demand.TerrestrialAfterHerds, 0, "work-before-production", true);
            Known(15, 0, demand.AquaticAfterHerds, 0, "work-before-production", true);
            var profile = HerdConsumptionProfile.Owned(world, camp);
            if (profile.Exists) { var support = SettlementSiteEvaluation.Evaluate(world, camp, camp.Tile, camp.Population, profile).Herd;
                Known(16, 0, support.AbsoluteForageCapacity, 0, "work-before-production", true);
                Known(17, 0, support.WaterCapacity, 0, "work-before-production", true); }
            result[18].Reason = "represented-by-field-capital-state-not-scalar-stock"; Known(19, 0, demand.FoodRoomEnvelope, 0, "production");
            foreach (var row in result) row.Features = new[] { row.Present ? 1d : 0, row.Known ? 1d : 0,
                row.Phase == "opening" ? 1d : row.Phase == "work-before-production" ? 2d : row.Phase == "production" ? 3d : 0d,
                row.Unit == "worker-years" ? 1d : row.Unit == "good-units" ? 2d : row.Unit == "material-units" ? 3d : row.Unit == "animal-heads" ? 4d : 0d,
                row.Opening, row.LocallyAccessible, row.FixedReserved, row.CompetitionPossible ? 1d : 0 }; return result;
        }
        internal static ElderCompiledDecision Compile(ElderCompilationContext context, ElderDirective requested)
        {
            var plan = ElderActorLaborLedger.Evaluate(context, requested);
            if (!plan.Executable) throw new ElderActorLaborRejectedException(plan.Attempt);
            var accepted = requested.Copy();
            accepted.LaborSpecialtyAllocation = (double[])plan.Attempt.RequestedSpecialtyAllocation.Clone();
            accepted.LaborActivityAllocation = ElderActorLaborAttempt.Clone(plan.Attempt.RequestedActivityAllocation);
            return new ElderCompiledDecision { Requested = requested.Copy(), Accepted = accepted, Realized = accepted.Copy(), Intent = plan.Intent,
                Production = plan.Production, ActorLabor = plan.Attempt, ControlledCampId = context.CommittedCamp.Id,
                PlanningPopulation = context.PlanningCamp.Population, AcceptedTile = context.AcceptedTile };
        }
        internal static ElderActorLaborAttempt ValidateSyntax(ElderActorLaborCatalog catalog, ElderDirective directive)
            => ElderActorLaborLedger.ValidateSyntax(catalog, directive);
        private static string Digest(ElderActorLaborCatalog catalog)
        {
            var text = new StringBuilder().Append(catalog.SchemaVersion).Append('|').Append(catalog.RegistryVersion).Append('|')
                .Append(catalog.RegistryDigest).Append('|').Append(catalog.ReasonRegistryDigest).Append('|').Append(catalog.CampId)
                .Append('|').Append(catalog.Turn).Append('|').Append(catalog.Population).Append('|')
                .Append(catalog.TurnYears.ToString("R", CultureInfo.InvariantCulture));
            foreach (var row in catalog.SpecialtyRows) { text.Append('|').Append(row.Id); foreach (double value in row.Features) text.Append(',').Append(value.ToString("R", CultureInfo.InvariantCulture)); }
            foreach (var row in catalog.ActivityRows) { text.Append('|').Append(row.SpecialtyId).Append(':').Append(row.ActivityId); foreach (double value in row.Features) text.Append(',').Append(value.ToString("R", CultureInfo.InvariantCulture)); }
            foreach (var row in catalog.Resources) { text.Append('|').Append(row.ResourceId); foreach (double value in row.Features) text.Append(',').Append(value.ToString("R", CultureInfo.InvariantCulture)); }
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-", "").ToLowerInvariant();
        }
    }
}
