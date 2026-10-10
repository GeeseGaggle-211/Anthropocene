using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    internal static class ElderDirectiveValidation
    {
        internal static ElderDirective Validate(ElderDirective requested,
            ElderObservation observation, List<ElderIntervention> interventions)
        {
            ElderDirective fallback = requested?.Copy() ?? new ElderDirective();
            if (requested == null)
                interventions.Add(new ElderIntervention
                {
                    Kind = ElderInterventionKind.InvalidActionFallback,
                    Control = "directive", Reason = "null directive; safe default",
                    Requested = double.NaN, Accepted = 0
                });
            if (fallback.SchemaVersion != ElderRLContract.ActionSchemaVersion)
            {
                interventions.Add(new ElderIntervention
                {
                    Kind = ElderInterventionKind.SchemaMismatch,
                    Control = "schema-version",
                    Reason = "unsupported action schema; safe default",
                    Requested = fallback.SchemaVersion,
                    Accepted = ElderRLContract.ActionSchemaVersion
                });
                fallback = new ElderDirective();
            }

            void Unit(string control, double requestedValue,
                Action<double> assign)
            {
                double accepted = ElderRLContract.Unit(requestedValue);
                assign(accepted);
                if (!ElderRLContract.Finite(requestedValue) ||
                    Math.Abs(requestedValue - accepted) > 1e-12)
                    interventions.Add(new ElderIntervention
                    {
                        Kind = ElderInterventionKind.LegalConstraint,
                        Control = control,
                        Reason = "continuous action must be finite and within [0,1]",
                        Requested = requestedValue, Accepted = accepted
                    });
            }
            Unit("retained-field-operation", fallback.RetainedFieldOperation,
                value => fallback.RetainedFieldOperation = value);
            Unit("retained-herd-fraction", fallback.RetainedHerdFraction,
                value => fallback.RetainedHerdFraction = value);
            Unit("development-budget", fallback.DevelopmentBudget,
                value => fallback.DevelopmentBudget = value);
            Unit("field-development-share", fallback.FieldDevelopmentShare,
                value => fallback.FieldDevelopmentShare = value);

            void ForceInactive(string control, double forced,
                Func<double> read, Action<double> assign, string reason)
            {
                double before = read();
                if (Math.Abs(before - forced) <= 1e-12)
                {
                    assign(forced);
                    return;
                }
                assign(forced);
                interventions.Add(new ElderIntervention
                {
                    Kind = ElderInterventionKind.LegalConstraint,
                    Control = control, Reason = reason,
                    Requested = before, Accepted = forced
                });
            }

            var mask = observation?.Mask;
            if (mask != null)
            {
                if (!mask.FieldRetention)
                    ForceInactive("retained-field-operation", 0,
                        () => fallback.RetainedFieldOperation,
                        value => fallback.RetainedFieldOperation = value,
                        "field-retention channel is structurally unavailable");
                if (!mask.HerdRetention)
                    ForceInactive("retained-herd-fraction", 0,
                        () => fallback.RetainedHerdFraction,
                        value => fallback.RetainedHerdFraction = value,
                        "herd-retention channel is structurally unavailable");

                bool field = mask.FieldDevelopment;
                bool herd = mask.HerdDevelopment;
                if (!field && !herd)
                {
                    ForceInactive("development-budget", 0,
                        () => fallback.DevelopmentBudget,
                        value => fallback.DevelopmentBudget = value,
                        "no Development channel is structurally available");
                    ForceInactive("field-development-share", .5,
                        () => fallback.FieldDevelopmentShare,
                        value => fallback.FieldDevelopmentShare = value,
                        "Development split is inactive with no available channel");
                }
                else if (field && !herd)
                    ForceInactive("field-development-share", 1,
                        () => fallback.FieldDevelopmentShare,
                        value => fallback.FieldDevelopmentShare = value,
                        "only field Development is structurally available");
                else if (!field && herd)
                    ForceInactive("field-development-share", 0,
                        () => fallback.FieldDevelopmentShare,
                        value => fallback.FieldDevelopmentShare = value,
                        "only herd Development is structurally available");
            }

            if (observation?.Mask == null ||
                !observation.Mask.AllowsMovement(fallback.MovementCandidate))
            {
                interventions.Add(new ElderIntervention
                {
                    Kind = ElderInterventionKind.InvalidActionFallback,
                    Control = "movement-candidate",
                    Reason = "categorical candidate is absent from the action mask; stay",
                    Requested = fallback.MovementCandidate,
                    Accepted = -1
                });
                fallback.MovementCandidate = -1;
            }
            return fallback;
        }
    }

    // The only learned/heuristic directive compiler. It delegates all actual
    // work, goods, resource and integer decisions to DemandControl.
    public sealed class NativeElderDirectiveCompiler : IElderDirectiveCompiler
    {
        public ElderCompiledDecision Compile(ElderCompilationContext context,
            ElderDirective directive)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            var interventions = new List<ElderIntervention>();
            var requested = directive?.Copy();
            var legal = ElderDirectiveValidation.Validate(directive,
                context.Observation, interventions);
            var world = context.World;
            var camp = context.PlanningCamp;
            var settings = context.Settings ?? EconomicIntent.From(camp);
            if (context.Settings == null &&
                CultivationEconomy.HusbandryEnabled(camp) &&
                HerdingTooling.DomesticAnimals(world,
                    context.CommittedCamp) > 0)
                settings.ManagedBreedingIntensity =
                    ManagedBreedingPolicy.Recommend(world,
                        context.CommittedCamp, context.AcceptedTile,
                        new PlanningIndex(world));

            var intent = DemandController.Plan(world, camp, settings,
                context.FishingAccess, out var production, out var allocation,
                null, null, legal, interventions);
            int residents = Math.Max(0, camp.Population);
            double herdRatio = allocation.HCurrent > 1e-9
                ? allocation.HRetained / allocation.HCurrent : 0;
            double finalField = Math.Max(0,
                intent.Labor?.ExpansionFarmers ??
                    allocation.FinalFieldBudget);
            int totalHerders = intent.Assignments.Where(assignment =>
                    assignment.Role == WorkforceRoles.Herder)
                .Sum(assignment => assignment.Population);
            double finalHerd = Math.Max(0,
                totalHerders - allocation.Herders);
            double finalDevelopment = finalField + finalHerd;
            double requestedPeople = requested != null &&
                    ElderRLContract.Finite(requested.DevelopmentBudget)
                ? requested.DevelopmentBudget * residents
                : legal.DevelopmentBudget * residents;
            if (Math.Abs(finalDevelopment -
                    allocation.SelectedFieldBudget -
                    allocation.SelectedHerdBudget) > 1e-8)
                interventions.Add(new ElderIntervention
                {
                    Kind = ElderInterventionKind.ResourceConstraint,
                    Control = "development-budget",
                    Reason = "downstream resource or integer staffing repair",
                    Requested = allocation.SelectedFieldBudget +
                        allocation.SelectedHerdBudget,
                    Accepted = finalDevelopment
                });

            var compiled = legal.Copy();
            compiled.RetainedFieldOperation =
                allocation.SelectedFieldOperation;
            compiled.RetainedHerdFraction = herdRatio;
            compiled.DevelopmentBudget = residents > 0
                ? Numbers.Clamp(finalDevelopment / residents, 0, 1) : 0;
            compiled.FieldDevelopmentShare = finalDevelopment > 1e-9
                ? Numbers.Clamp(finalField / finalDevelopment, 0, 1)
                : legal.FieldDevelopmentShare;

            void EnsureIntervention(string control, double requestedValue,
                double acceptedValue, string reason)
            {
                if (Math.Abs(requestedValue - acceptedValue) <= 1e-8 ||
                    interventions.Any(item => item.Control == control)) return;
                interventions.Add(new ElderIntervention
                {
                    Kind = ElderInterventionKind.PhysicalConstraint,
                    Control = control, Reason = reason,
                    Requested = requestedValue, Accepted = acceptedValue
                });
            }
            EnsureIntervention("retained-field-operation",
                legal.RetainedFieldOperation,
                compiled.RetainedFieldOperation,
                "integer field staffing and shared resident limit");
            EnsureIntervention("retained-herd-fraction",
                legal.RetainedHerdFraction,
                compiled.RetainedHerdFraction,
                "whole-animal, ecology and shared resident limit");
            EnsureIntervention("development-budget", legal.DevelopmentBudget,
                compiled.DevelopmentBudget,
                "physical options, resources and integer staffing");
            EnsureIntervention("field-development-share",
                legal.FieldDevelopmentShare,
                compiled.FieldDevelopmentShare,
                "physical channel capacity and integer staffing");

            int[] retainedFieldTiles = world.Map.Radius(camp.Tile, 1)
                .Where(tile => world.Tiles[tile].CultivationOperator ==
                        context.CommittedCamp.Id &&
                    LazyEcology.ProjectEcology(world, tile, world.Days)
                        .CultivationCapital > 1e-9)
                .OrderBy(tile => tile).ToArray();

            return new ElderCompiledDecision
            {
                Requested = requested ?? new ElderDirective(),
                Accepted = compiled,
                // Reconciled from authoritative state by TurnResolver after
                // economy resolution and before immigration/fission.
                Realized = compiled.Copy(),
                Interventions = interventions.ToArray(),
                Intent = intent,
                Production = production,
                RequestedDevelopmentPeople = requestedPeople,
                AcceptedDevelopmentPeople = finalDevelopment,
                AcceptedFieldDevelopmentPeople = finalField,
                AcceptedHerdDevelopmentPeople = finalHerd,
                ControlledCampId = context.CommittedCamp.Id,
                PlanningPopulation = residents,
                AcceptedTile = context.AcceptedTile,
                AcceptedRetainedFieldPeople = allocation.Farmers,
                AcceptedRetainedHerdPeople = allocation.Herders,
                StartingHerdAnimals = (int)Math.Min(int.MaxValue,
                    Math.Max(0, Math.Round(allocation.HCurrent))),
                RetainedFieldTiles = retainedFieldTiles
            };
        }
    }

    internal static class ElderDecisionRealization
    {
        internal static void Reconcile(WorldState world,
            ElderCompiledDecision decision)
        {
            if (world == null || decision?.Accepted == null) return;
            var camp = world.Camps.FirstOrDefault(item => item.Id ==
                decision.ControlledCampId);
            if (camp == null)
            {
                decision.Realized = new ElderDirective
                {
                    MovementCandidate = -1
                };
                return;
            }

            var realized = decision.Accepted.Copy();
            realized.RetainedFieldOperation =
                Numbers.Clamp(decision.ResolvedRetainedFieldOperation, 0, 1);
            int animals = world.Herds.Where(herd =>
                    herd.DomesticCamp == camp.Id)
                .Sum(herd => Math.Max(0, herd.Animals));
            realized.RetainedHerdFraction = decision.StartingHerdAnimals > 0
                ? Numbers.Clamp(animals /
                    (double)decision.StartingHerdAnimals, 0, 1) : 0;

            double fieldDevelopment = Math.Max(0,
                decision.ResolvedFieldDevelopmentPeople);
            double herdDevelopment = Math.Max(0,
                decision.ResolvedHerdDevelopmentPeople);
            double development = fieldDevelopment + herdDevelopment;
            realized.DevelopmentBudget = decision.PlanningPopulation > 0
                ? Numbers.Clamp(development /
                    decision.PlanningPopulation, 0, 1) : 0;
            realized.FieldDevelopmentShare = development > 1e-9
                ? Numbers.Clamp(fieldDevelopment / development, 0, 1)
                : decision.Accepted.FieldDevelopmentShare;
            realized.MovementCandidate = camp.Tile == decision.AcceptedTile
                ? decision.Accepted.MovementCandidate : -1;
            decision.Realized = realized;
        }
    }

    // Exact strategic projection of the production Option A supervisor. The
    // factory evaluates Option A once on the committed local settlement, then
    // exposes only its four bounded strategic targets through IElderPolicy.
    // Evaluation compiles that directive through the same native compiler as a
    // learned policy; it never substitutes Option A after compilation.
    public sealed class OptionAElderPolicy : IElderPolicy
    {
        private readonly ElderDirective directive;

        private OptionAElderPolicy(ElderDirective directive)
        {
            this.directive = directive?.Copy() ?? throw new
                ArgumentNullException(nameof(directive));
        }

        public static OptionAElderPolicy FromCommittedState(WorldState world,
            int campId, FishingAccessPlan fishingAccess = null)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            var camp = world.Camps.FirstOrDefault(item => item.Id == campId)
                ?? throw new ArgumentException("Option A camp is not present.",
                    nameof(campId));
            var intent = DemandController.Plan(world, camp,
                EconomicIntent.From(camp), fishingAccess, out _,
                out var allocation);
            double fieldDevelopment = Math.Max(0,
                intent.Labor?.ExpansionFarmers ??
                    allocation.FinalFieldBudget);
            int totalHerders = intent.Assignments.Where(assignment =>
                    assignment.Role == WorkforceRoles.Herder)
                .Sum(assignment => assignment.Population);
            double herdDevelopment = Math.Max(0,
                totalHerders - allocation.Herders);
            double development = fieldDevelopment + herdDevelopment;
            return new OptionAElderPolicy(new ElderDirective
            {
                RetainedFieldOperation = allocation.SelectedFieldOperation,
                RetainedHerdFraction = allocation.HCurrent > 1e-9
                    ? allocation.HRetained / allocation.HCurrent : 0,
                DevelopmentBudget = camp.Population > 0
                    ? development / camp.Population : 0,
                FieldDevelopmentShare = development > 1e-9
                    ? fieldDevelopment / development : .5,
                MovementCandidate = -1
            });
        }

        public ElderDirective Decide(ElderObservation observation)
        {
            if (observation == null)
                throw new ArgumentNullException(nameof(observation));
            if (observation.SchemaVersion !=
                    ElderRLContract.ObservationSchemaVersion)
                throw new ArgumentException("Unsupported Elder observation schema.",
                    nameof(observation));
            return directive.Copy();
        }
    }
}
