using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    internal sealed class ElderActorLaborPlan
    {
        internal ElderActorLaborCatalog Catalog;
        internal ElderActorLaborAttempt Attempt;
        internal EconomicIntent Intent;
        internal ProductionOrder Production;
        internal bool Executable => Attempt?.Valid == true && Intent != null && Production != null;
    }

    // Only syntax, masks and hard action constraints reject. A material-starved
    // specialist is a legal staffing choice whose unused capacity is an outcome.
    internal static class ElderActorLaborLedger
    {
        private const double Epsilon = 1e-8;

        internal static ElderActorLaborAttempt ValidateSyntax(ElderActorLaborCatalog catalog,
            ElderDirective directive)
        {
            var attempt = new ElderActorLaborAttempt { CampId = catalog?.CampId ?? -1,
                RemainingProposals = 1, CatalogDigest = catalog?.Digest };
            var violations = new List<ElderActorLaborViolation>();
            if (catalog == null)
            {
                var server = Violation("catalog-unavailable", 1, 0, "catalog",
                    Array.Empty<int>(), Array.Empty<int>());
                server.Knowledge = "server"; server.Avoidable = false;
                violations.Add(server);
                return Finish(attempt, violations);
            }
            if (directive == null || directive.SchemaVersion != ElderActorLaborContract.ActionSchemaVersion)
            {
                violations.Add(Violation("unsupported-action-schema", directive?.SchemaVersion ?? -1,
                    ElderActorLaborContract.ActionSchemaVersion, "schema", Array.Empty<int>(), Array.Empty<int>()));
                return Finish(attempt, violations);
            }
            var specialty = directive.LaborSpecialtyAllocation;
            if (specialty == null || specialty.Length != ElderActorLaborContract.SpecialtyRows)
            {
                violations.Add(Violation("specialty-row-count", specialty?.Length ?? -1,
                    ElderActorLaborContract.SpecialtyRows, "rows", Array.Empty<int>(), Array.Empty<int>()));
                return Finish(attempt, violations);
            }
            attempt.RequestedSpecialtyAllocation = (double[])specialty.Clone();
            ValidateSimplex(specialty, catalog.SpecialtyMask, "specialty",
                Enumerable.Range(0, specialty.Length).ToArray(), Array.Empty<int>(), violations);
            var activities = directive.LaborActivityAllocation;
            if (activities == null || activities.Length != ElderActorLaborContract.RoleRows)
            {
                violations.Add(Violation("activity-specialty-row-count", activities?.Length ?? -1,
                    ElderActorLaborContract.RoleRows, "rows", Array.Empty<int>(), Array.Empty<int>()));
                return Finish(attempt, violations);
            }
            for (int role = 0; role < ElderActorLaborContract.RoleRows; role++)
            {
                var row = activities[role];
                if (row == null || row.Length != ElderActorLaborContract.ActivityRows)
                {
                    violations.Add(Violation("activity-row-count", row?.Length ?? -1,
                        ElderActorLaborContract.ActivityRows, "rows", new[] { role + 1 }, Array.Empty<int>()));
                    continue;
                }
                attempt.RequestedActivityAllocation[role] = (double[])row.Clone();
                ValidateSimplex(row, catalog.ActivityMask[role], "activity", new[] { role + 1 },
                    Enumerable.Range(0, ElderActorLaborContract.ActivityRows)
                        .Select(activity => ElderActorLaborContract.FlatActivity(role, activity)).ToArray(), violations);
            }
            attempt = Finish(attempt, violations);
            if (attempt.Valid) PopulateAccepted(catalog, attempt);
            return attempt;
        }

        internal static ElderActorLaborAttempt ValidateDecision(
            ElderActorLaborCatalog catalog, ElderDirective directive,
            ElderObservation observation, bool movementEnabled)
        {
            var attempt = ValidateSyntax(catalog, directive);
            var violations = attempt.Violations.Select(item => item.Copy()).ToList();
            if (directive == null ||
                directive.SchemaVersion != ElderActorLaborContract.ActionSchemaVersion)
                return Finish(attempt, violations);

            bool retiredNeutral = Math.Abs(directive.RetainedFieldOperation - 1) <= Epsilon &&
                Math.Abs(directive.RetainedHerdFraction - 1) <= Epsilon &&
                Math.Abs(directive.DevelopmentBudget) <= Epsilon &&
                Math.Abs(directive.FieldDevelopmentShare - .5) <= Epsilon &&
                Math.Abs(directive.FishingDevelopmentShare) <= Epsilon &&
                directive.ServiceLevels == null && directive.ClothingRoute == -1 &&
                directive.ShelterRoute == -1 && directive.CommitTurns == 1;
            if (!retiredNeutral)
                violations.Add(Violation("unsupported-action-schema", 1, 0,
                    "retired-controls-must-be-omitted", Array.Empty<int>(),
                    Array.Empty<int>()));

            int movement = directive.MovementCandidate;
            bool movementValid = movement != ElderModel9Contract.NativeMovement &&
                (movementEnabled || movement == -1) && observation?.Mask != null &&
                observation.Mask.AllowsMovement(movement);
            if (!movementValid)
                violations.Add(Violation("unsupported-action-schema", movement,
                    -1, "movement-code", Array.Empty<int>(), Array.Empty<int>()));
            if (directive.FissionPolicy < 0 || directive.FissionPolicy > 2)
                violations.Add(Violation("unsupported-action-schema",
                    directive.FissionPolicy, 2, "fission-policy",
                    Array.Empty<int>(), Array.Empty<int>()));

            bool tradeValid = directive.TradeValue?.Length == ElderTrade.Groups.Length &&
                directive.TradeGate?.Length == ElderTrade.Groups.Length &&
                ElderRLContract.Finite(directive.TradeExportShare) &&
                directive.TradeExportShare >= 0 && directive.TradeExportShare <= 1;
            if (tradeValid)
                for (int group = 0; group < ElderTrade.Groups.Length; group++)
                    tradeValid &= ElderRLContract.Finite(directive.TradeValue[group]) &&
                        directive.TradeValue[group] >= ElderTrade.MinValue &&
                        directive.TradeValue[group] <= ElderTrade.MaxValue &&
                        directive.TradeGate[group] >= 0 && directive.TradeGate[group] <= 2;
            if (!tradeValid)
                violations.Add(Violation("unsupported-action-schema", 1, 0,
                    "trade-posture", Array.Empty<int>(), Array.Empty<int>()));
            return Finish(attempt, violations);
        }

        internal static ElderActorLaborPlan Evaluate(ElderCompilationContext context,
            ElderDirective directive)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            // Syntax and masks are bound to the committed decision observation. A
            // movement destination may subsequently lose fishing access or other
            // useful work, but that is a legal (possibly unproductive) execution
            // outcome and must not become a hidden action-conditioned rejection.
            var originAccess = FishingEconomy.BuildAccess(context.World,
                context.CommittedCamp, context.CommittedCamp.Tile,
                context.CommittedCamp.Population, context.World.Days,
                context.World.Config.TurnDays);
            var catalog = ElderActorLaborCompiler.BuildCatalog(context.World,
                context.CommittedCamp, originAccess);
            var attempt = ValidateSyntax(catalog, directive);
            var result = new ElderActorLaborPlan { Catalog = catalog, Attempt = attempt };
            if (!attempt.Valid) return result;
            int population = Math.Max(0, context.PlanningCamp.Population);
            double years = catalog.TurnYears;

            var autonomous = new AutonomousElderAction();
            var roleCounts = new int[ElderActorLaborContract.RoleRows];
            var intent = new EconomicIntent { AutomationTurns = 0,
                Labor = new EconomicLaborPlan { PolicyAuthority = true, ActorActivityAuthority = true,
                    ActorActivityWorkers = Enumerable.Range(0, ElderActorLaborContract.RoleRows)
                        .Select(_ => new double[AutonomousElderContract.Activities]).ToArray() } };
            double Workers(int role, int activity) =>
                attempt.AcceptedSpecialtyResidents[role + 1] *
                attempt.RequestedActivityAllocation[role][activity];
            for (int role = 0; role < ElderActorLaborContract.RoleRows; role++)
            {
                int count = attempt.AcceptedSpecialtyResidents[role + 1]; roleCounts[role] = count;
                if (count > 0) intent.Assignments.Add(new PopulationAssignment
                    { Role = WorkforceRoles.All[role], Population = count });
                autonomous.Workforce[role] = population > 0 ? count / (double)population : 0;
                var target = autonomous.Activities[role];
                target[0] = attempt.RequestedActivityAllocation[role][1];
                target[1] = attempt.RequestedActivityAllocation[role][2];
                target[2] = attempt.RequestedActivityAllocation[role][3];
                target[3] = attempt.RequestedActivityAllocation[role][4];
                for (int good = 0; good < ElderActorLaborContract.GoodsRows; good++)
                    target[4 + good] = attempt.RequestedActivityAllocation[role][11 + good];
                intent.Labor.ActorActivityWorkers[role] = target.Select(value => Math.Max(0, value) * count).ToArray();
            }
            int gatherer = Array.IndexOf(WorkforceRoles.All, WorkforceRoles.Gatherer),
                farmer = Array.IndexOf(WorkforceRoles.All, WorkforceRoles.Farmer),
                herder = Array.IndexOf(WorkforceRoles.All, WorkforceRoles.Herder),
                fisher = Array.IndexOf(WorkforceRoles.All, WorkforceRoles.Fisher);
            intent.Labor.ActorIdleWorkers =
                attempt.AcceptedSpecialtyResidents[0];
            intent.Labor.ActorFoodGathererWorkers = Workers(gatherer, 1);
            intent.Labor.MaterialGatherers = Workers(gatherer, 2) + Workers(gatherer, 3) + Workers(gatherer, 4);
            intent.Labor.ActorFieldOperationWorkers = Workers(farmer, 5);
            intent.Labor.ActorFieldExpansionWorkers = Workers(farmer, 6);
            intent.Labor.ExpansionFarmers = intent.Labor.ActorFieldExpansionWorkers;
            intent.Labor.ActorHerdManagementWorkers = Workers(herder, 7);
            intent.Labor.ActorHerdExpansionWorkers = Workers(herder, 8);
            int acceptedHerders = attempt.AcceptedSpecialtyResidents[herder + 1];
            intent.ManagedBreedingIntensity = acceptedHerders > 0
                ? Numbers.Clamp(intent.Labor.ActorHerdExpansionWorkers /
                    acceptedHerders, 0, 1) : 0;
            intent.Labor.ActorFishingHarvestWorkers = Workers(fisher, 9);
            intent.Labor.ActorFishingDevelopmentWorkers = Workers(fisher, 10);
            result.Intent = intent;
            result.Production = ProductionOrder.Autonomous(autonomous, roleCounts,
                preserveExplicitIdle: true);
            return result;
        }

        private static void PopulateAccepted(ElderActorLaborCatalog catalog,
            ElderActorLaborAttempt attempt)
        {
            int population = Math.Max(0, catalog.Population);
            double years = catalog.TurnYears;
            for (int specialty = 0;
                specialty < ElderActorLaborContract.SpecialtyRows; specialty++)
                attempt.RequestedSpecialtyResidents[specialty] =
                    attempt.RequestedSpecialtyAllocation[specialty] * population;
            attempt.AcceptedSpecialtyResidents = AutonomousElderCompiler.Apportion(
                attempt.RequestedSpecialtyAllocation, population);
            for (int role = 0; role < ElderActorLaborContract.RoleRows; role++)
                for (int activity = 0;
                    activity < ElderActorLaborContract.ActivityRows; activity++)
                {
                    double share = attempt.RequestedActivityAllocation[role][activity];
                    attempt.RequestedActivityWorkerYears[role][activity] =
                        attempt.RequestedSpecialtyResidents[role + 1] * years * share;
                    attempt.PlannedActivityWorkerYears[role][activity] =
                        attempt.AcceptedSpecialtyResidents[role + 1] * years * share;
                }
        }

        private static void ValidateSimplex(double[] values, bool[] mask, string prefix,
            int[] specialties, int[] activities, List<ElderActorLaborViolation> violations)
        {
            for (int index = 0; index < values.Length; index++)
            {
                double value = values[index];
                string reason = !ElderRLContract.Finite(value) ? prefix + "-nonfinite" :
                    value < 0 ? prefix + "-negative" :
                    !mask[index] && Math.Abs(value) > Epsilon ? prefix + "-masked-nonzero" : null;
                if (reason != null) violations.Add(Violation(reason, value, 0, "share", specialties,
                    activities.Length == values.Length ? new[] { activities[index] } : activities));
            }
            double sum = values.Where(ElderRLContract.Finite).Sum();
            double expected = mask.Any(value => value) ? 1 : 0;
            if (Math.Abs(sum - expected) > Epsilon)
                violations.Add(Violation(prefix + "-sum", sum, expected, "share", specialties, activities));
        }
        private static ElderActorLaborAttempt Finish(ElderActorLaborAttempt attempt,
            List<ElderActorLaborViolation> violations)
        {
            attempt.Violations = violations.ToArray(); attempt.Valid = violations.Count == 0;
            var avoidable = violations.Where(item => item.Avoidable == true &&
                item.Knowledge == "actor-visible").ToArray();
            attempt.FeasibilityCost = avoidable.Length == 0 ? 0 : 1 + avoidable
                .Sum(item => Math.Max(0, Math.Min(1, item.Severity)));
            if (attempt.FeasibilityCost > ElderActorLaborContract.FeasibilityCostMaximum + 1e-9)
                throw new InvalidOperationException(
                    "Actor-labor feasibility cost exceeded its producer bound.");
            return attempt;
        }
        private static ElderActorLaborViolation Violation(string reason, double requested,
            double available, string unit, int[] specialties, int[] activities)
        {
            bool requestedKnown = ElderRLContract.Finite(requested),
                availableKnown = ElderRLContract.Finite(available);
            return new ElderActorLaborViolation { ReasonCode = ElderActorLaborContract.ReasonCode(reason),
                Reason = reason, Knowledge = "actor-visible", Unit = unit, Phase = "decision",
                SpecialtyRows = specialties, ActivityRows = activities,
                Requested = requestedKnown ? requested : 0,
                Available = availableKnown ? available : 0,
                RequestedKnown = requestedKnown, AvailableKnown = availableKnown,
                Avoidable = true, Severity = requestedKnown && availableKnown
                    ? Math.Min(1, Math.Abs(requested - available) /
                        Math.Max(1e-12, Math.Abs(available))) : 1 };
        }
    }
}
