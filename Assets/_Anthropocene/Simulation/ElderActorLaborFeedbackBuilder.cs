using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    // Builds only provenance-backed successor evidence.  Missing scheduler
    // witnesses remain nullable with a typed reason; committed worker-years are
    // never relabelled as productive or consumed work.
    internal static class ElderActorLaborFeedbackBuilder
    {
        internal static ElderActorLaborFeedback Begin(ElderActorLaborCatalog catalog,
            ElderActorLaborAttempt initial, ElderActorLaborAttempt finalSample,
            int decisionTurn, string parentDecisionId, bool fallback)
        {
            if (catalog == null || initial == null || finalSample == null)
                throw new ArgumentNullException();
            var result = new ElderActorLaborFeedback
            {
                CampId = catalog.CampId,
                DecisionTurn = decisionTurn,
                ExecutionComparatorAttempt = finalSample.Attempt,
                ParentDecisionId = parentDecisionId,
                DecisionCatalog = catalog.Copy(),
                InitialRequest = initial.Copy(),
                FinalSampledRequest = finalSample.Copy(),
                Fallback = fallback,
                FallbackReason = fallback ? "fallback-after-retry" : "",
                FallbackReasonCode = fallback
                    ? ElderActorLaborContract.ReasonCode("fallback-after-retry") : 0
            };
            result.FinalSampledRequest.Fallback = fallback;
            FillUnavailable(result);
            return result;
        }

        internal static void BindPrepared(ElderActorLaborFeedback feedback,
            PreparedTurn prepared)
        {
            if (feedback == null || prepared == null) return;
            if (prepared.ElderDecisions.TryGetValue(feedback.CampId, out var decision) &&
                decision?.ActorLabor != null)
                feedback.CommittedActivityWorkerYears = ElderActorLaborAttempt.Clone(
                    decision.ActorLabor.PlannedActivityWorkerYears);

            if (prepared.Needs.TryGetValue(feedback.CampId, out var needs) &&
                needs?.Production != null)
                for (int good = 0; good < ElderActorLaborContract.GoodsRows; good++)
                {
                    var id = (CraftGood)good;
                    feedback.PlannedGoodsOutput[good] = Math.Max(0,
                        needs.Production.TryGetValue(id, out double amount) ? amount : 0);
                    feedback.PlannedGoodsOutputKnown[good] = true;
                    feedback.PlannedGoodsOutputReasons[good] = "";
                    feedback.PlannedGoodsOutputReasonCodes[good] = 0;
                }
        }

        internal static void BindResolved(ElderActorLaborFeedback feedback,
            TurnRecord record, SimulationConfig config, bool campVanished)
        {
            if (feedback == null || record == null || config == null) return;
            double intervalYears = config.BaseDays / config.DaysPerYear;
            var production = (record.Production ?? new List<ProductionReceipt>())
                .Where(item => item.Camp == feedback.CampId).ToArray();
            var materials = (record.MaterialTransfers ?? new List<MaterialReceipt>())
                .Where(item => item.Camp == feedback.CampId).ToArray();
            var logistics = record.Logistics ?? new List<LogisticsRecord>();
            var hunterOwners = logistics.ToDictionary(item => item.Hunter,
                item => item.Camp);
            bool Owned(Transfer item) => item.Entity == feedback.CampId ||
                hunterOwners.TryGetValue(item.Entity, out int owner) &&
                owner == feedback.CampId;
            var transfers = (record.Transfers ?? new List<Transfer>())
                .Where(Owned).ToArray();
            var managedHerds = (record.ManagedHerds ?? new List<ManagedHerdReport>())
                .Where(item => item.Camp == feedback.CampId).ToArray();
            var materialIds = new[] { MaterialId.Stone, MaterialId.Clay,
                MaterialId.Wood };

            // Goods output is the native production receipt only.  It never
            // includes trade imports, opening stock or a net inventory change.
            for (int good = 0; good < ElderActorLaborContract.GoodsRows; good++)
            {
                feedback.RealizedGoodsOutput[good] = production
                    .Where(item => (int)item.Good == good).Sum(item => Math.Max(0, item.Output));
                feedback.GoodsOutputKnown[good] = true;
                feedback.GoodsOutputReasons[good] = "";
                feedback.GoodsOutputReasonCodes[good] = 0;
            }

            // Raw extraction and manufacturing own exact consumed-work
            // receipts.  Field/herd/fishing and food gathering do not yet have
            // an authoritative scheduler-consumed witness and remain null.
            for (int role = 0; role < ElderActorLaborContract.RoleRows; role++)
                for (int activity = 0; activity < ElderActorLaborContract.ActivityRows; activity++)
                {
                    int flat = ElderActorLaborContract.FlatActivity(role, activity);
                    var row = feedback.DecisionCatalog.ActivityRows[flat];
                    if (!row.Present || !(activity >= 2 && activity <= 4 || activity >= 11))
                        continue;
                    string roleId = WorkforceRoles.All[role];
                    double work = activity >= 11
                        ? production.Where(item => item.Role == roleId &&
                            (int)item.Good == activity - 11)
                            .Sum(item => Math.Max(0, item.WorkerIntervals)) * intervalYears
                        : materials.Where(item => item.Role == roleId &&
                            item.Material == materialIds[activity - 2])
                            .Sum(item => Math.Max(0, item.WorkerIntervals)) * intervalYears;
                    feedback.ActualSchedulerConsumedWorkerYears[role][activity] = work;
                    feedback.ActualWorkerYearsKnown[role][activity] = true;
                    feedback.ActualWorkerYearsReasons[role][activity] = "";
                    feedback.ActualWorkerYearsReasonCodes[role][activity] = 0;
                }

            for (int material = 0; material < 3; material++)
            {
                int resource = 1 + material;
                feedback.RealizedResourceReceipts[resource] = materials
                    .Where(item => item.Material == materialIds[material])
                    .Sum(item => Math.Max(0, item.Amount));
                feedback.ResourceReceiptKnown[resource] = true;
                feedback.ResourceReceiptReasons[resource] = "";
                feedback.ResourceReceiptReasonCodes[resource] = 0;
            }

            // These are gross native receipts, never closing-stock deltas.  The
            // same harvest can intentionally appear in a typed stock component
            // (plant/animal food) and in its source class (terrestrial/aquatic):
            // those rows are alternative actor-visible descriptions, not values
            // intended to be summed into one conservation ledger.
            bool hunterOwnershipComplete = !(record.Transfers ?? new List<Transfer>())
                .Any(item => (item.Kind == "Hunting" || item.Kind == "Fishing" ||
                    item.Kind == "Foraging") && !hunterOwners.ContainsKey(item.Entity));
            double terrestrialFood = transfers.Where(item => item.Kind == "Gathering" ||
                    item.Kind == "Cultivation" || item.Kind == "Foraging")
                .Sum(item => Math.Max(0, item.Food));
            double huntingFood = transfers.Where(item => item.Kind == "Hunting")
                .Sum(item => Math.Max(0, item.Food));
            double fishingFood = transfers.Where(item => item.Kind == "Fishing")
                .Sum(item => Math.Max(0, item.Food));
            double aquaticFood = transfers.Where(item => item.Kind == "Aquatic gathering" ||
                    item.Kind == "Marine gathering" || item.Kind == "FisheryHarvest")
                .Sum(item => Math.Max(0, item.Food));
            // Detached-hunter harvest overflow was produced but never became a
            // camp/linked-hunter stock receipt.  Reuse the authoritative native
            // allocation order from FinishDemographicReports: hunting first,
            // then fishing. Foraging is consumed locally and is not part of this
            // carried-harvest overflow field.
            foreach (var item in logistics.Where(item => item.Camp == feedback.CampId &&
                item.HarvestOverflow > 0).OrderBy(item => item.Hunter))
            {
                double overflow = item.HarvestOverflow;
                double hunterHunting = transfers.Where(transfer =>
                        transfer.Entity == item.Hunter && transfer.Kind == "Hunting")
                    .Sum(transfer => Math.Max(0, transfer.Food));
                double fromHunting = Math.Min(overflow, hunterHunting);
                huntingFood -= fromHunting;
                overflow -= fromHunting;
                double hunterFishing = transfers.Where(transfer =>
                        transfer.Entity == item.Hunter && transfer.Kind == "Fishing")
                    .Sum(transfer => Math.Max(0, transfer.Food));
                fishingFood -= Math.Min(overflow, hunterFishing);
            }
            double animalFood = managedHerds.Sum(item =>
                Math.Max(0, item.AcceptedFood) + Math.Max(0, item.SlaughterFood)) +
                Math.Max(0, huntingFood);
            aquaticFood += Math.Max(0, fishingFood);
            // A missing hunter->camp logistics identity makes every camp's
            // purported total ambiguous. Keep the inherited unavailable masks
            // instead of silently publishing an actor-controlled-only partial.
            if (hunterOwnershipComplete)
            {
                BindResourceReceipt(feedback, 4, terrestrialFood);
                BindResourceReceipt(feedback, 5, animalFood);
                BindResourceReceipt(feedback, 14, terrestrialFood);
                BindResourceReceipt(feedback, 15, aquaticFood);
            }

            // Craft outputs that are themselves a later physical input retain
            // their production-receipt provenance in the matching resource row.
            // Inputs consumed by a recipe are deliberately not netted against
            // these gross receipts; the current feedback contract has no typed
            // consumption lane and RealizedResourceReceipts must stay truthful.
            BindCraftResource(feedback, production, 6, CraftGood.AnimalFabric);
            BindCraftResource(feedback, production, 7, CraftGood.HousingMaterial);
            BindCraftResource(feedback, production, 8, CraftGood.Cloth);
            BindCraftResource(feedback, production, 9, CraftGood.HandTools);
            BindCraftResource(feedback, production, 10, CraftGood.Tools);
            BindCraftResource(feedback, production, 11, CraftGood.FarmTools);
            BindCraftResource(feedback, production, 12, CraftGood.HerdingTools);
            BindCraftResource(feedback, production, 13, CraftGood.FishingTools);

            if (campVanished)
            {
                int code = ElderActorLaborContract.ReasonCode("camp-vanished");
                for (int role = 0; role < ElderActorLaborContract.RoleRows; role++)
                    for (int activity = 0; activity < ElderActorLaborContract.ActivityRows; activity++)
                        if (!feedback.ActualWorkerYearsKnown[role][activity])
                        {
                            feedback.ActualWorkerYearsReasons[role][activity] = "camp-vanished";
                            feedback.ActualWorkerYearsReasonCodes[role][activity] = code;
                        }
            }
        }

        private static void BindCraftResource(ElderActorLaborFeedback feedback,
            ProductionReceipt[] production, int resource, CraftGood good)
            => BindResourceReceipt(feedback, resource, production
                .Where(item => item.Good == good)
                .Sum(item => Math.Max(0, item.Output)));

        private static void BindResourceReceipt(ElderActorLaborFeedback feedback,
            int resource, double amount)
        {
            feedback.RealizedResourceReceipts[resource] = Math.Max(0, amount);
            feedback.ResourceReceiptKnown[resource] = true;
            feedback.ResourceReceiptReasons[resource] = "";
            feedback.ResourceReceiptReasonCodes[resource] = 0;
        }

        private static void FillUnavailable(ElderActorLaborFeedback feedback)
        {
            int actual = ElderActorLaborContract.ReasonCode("actual-work-unavailable"),
                planned = ElderActorLaborContract.ReasonCode("planned-output-unavailable"),
                output = ElderActorLaborContract.ReasonCode("realized-output-unavailable"),
                resource = ElderActorLaborContract.ReasonCode("resource-receipt-unavailable");
            for (int role = 0; role < ElderActorLaborContract.RoleRows; role++)
                for (int activity = 0; activity < ElderActorLaborContract.ActivityRows; activity++)
                {
                    feedback.ActualWorkerYearsReasons[role][activity] = "actual-work-unavailable";
                    feedback.ActualWorkerYearsReasonCodes[role][activity] = actual;
                }
            for (int good = 0; good < ElderActorLaborContract.GoodsRows; good++)
            {
                feedback.PlannedGoodsOutputReasons[good] = "planned-output-unavailable";
                feedback.PlannedGoodsOutputReasonCodes[good] = planned;
                feedback.GoodsOutputReasons[good] = "realized-output-unavailable";
                feedback.GoodsOutputReasonCodes[good] = output;
            }
            for (int row = 0; row < ElderActorLaborContract.ResourceRows; row++)
            {
                feedback.ResourceReceiptReasons[row] = "resource-receipt-unavailable";
                feedback.ResourceReceiptReasonCodes[row] = resource;
            }
        }
    }
}
