using System;
using System.Collections.Generic;

namespace Anthropocene.Simulation
{
    // What a decision did to its settlement's capital commitment (ElderCompiledDecision.CommitmentAction).
    public enum ElderCommitmentAction
    {
        // Capital controls decided for this turn only (CommitTurns 1, or an earlier schema). Ends any commitment.
        Single = 0,
        // A new commitment for CommitTurns (2-6) turns.
        Commit = 1,
        // The active commitment held: its committed capital controls were re-emitted.
        Hold = 2,
        // A hold was requested but the commitment was force-broken (relocation, fission, capability change,
        // survival shortfall); the directive's own capital controls were used for this turn.
        Broken = 3,
        // A hold was requested with no active commitment; the directive's own capital controls were used.
        NoCommitment = 4
    }

    // Contract 9 capital commitments (Docs/ElderCapitalCommitmentsDesign.md, Part 1). Authoritative state is the
    // settlement's ElderInstitutionMemory (saved); Resolve runs in the directive compiler on the committed world at
    // Prepare, and Record updates the memory at the decision's resolution, so a replay reproduces both exactly.
    // A hold changes only which capital controls the ordinary directive carries: the compiler, DemandControl and the
    // resolver are unchanged, and CommitTurns 1 reproduces contract 8 exactly.
    public static class ElderCommitments
    {
        public const int MaxTurns = 6;
        // The span head's categories: 0 holds; 1 decides this turn only; 2, 3, 4 and 6 commit.
        public static readonly int[] Spans = { 0, 1, 2, 3, 4, 6 };

        // Economy capability bits a commitment is made under; a change (a new technology) forces a break.
        public static int Capabilities(WorldState world, Camp camp)
            => (CultivationEconomy.Enabled(camp) ? 1 : 0) | (CultivationEconomy.HusbandryEnabled(camp) ? 2 : 0) |
               (NeolithicCapabilities.ResidentFishingEnabled(world, camp) ? 4 : 0);

        // Why the settlement's commitment cannot be held now, or null when it can. Events dated after the decision
        // that set the commitment break it: relocation and fission (including in the committing turn), plus a
        // capability change or an active food deficit.
        public static string BreakReason(WorldState world, Camp camp, ElderInstitutionMemory memory)
        {
            if (camp == null || memory == null || !memory.HasCommitment || memory.Commitment == null || memory.CommitmentRemaining <= 0)
                return "no active commitment";
            if (memory.HasLastMovement && memory.LastMovementDays > memory.CommitmentSetDays + 1e-6) return "relocation";
            if (memory.HasLastFission && memory.LastFissionDays > memory.CommitmentSetDays + 1e-6) return "fission";
            if (Capabilities(world, camp) != memory.CommitmentCapabilities) return "capability change";
            if (camp.DeficitTurns > 0) return "survival shortfall";
            return null;
        }

        public static bool CanHold(WorldState world, Camp camp) => BreakReason(world, camp, camp?.ElderMemory) == null;

        // The directive the compiler validates: a hold re-emits the committed capital controls; anything else is
        // the submitted directive with a legal span.
        internal static ElderDirective Resolve(WorldState world, Camp committed, ElderDirective directive,
            List<ElderIntervention> interventions, out ElderCommitmentAction action, out int span, out string reason)
        {
            action = ElderCommitmentAction.Single; span = 1; reason = null;
            if (directive == null) return null;
            var resolved = directive.Copy();
            if (resolved.SchemaVersion < ElderModel9Contract.CommitmentSchemaVersion) { resolved.CommitTurns = 1; return resolved; }
            if (resolved.CommitTurns < 0 || resolved.CommitTurns > MaxTurns)
            {
                interventions.Add(new ElderIntervention { Kind = ElderInterventionKind.LegalConstraint, Control = "commitment",
                    Reason = "commitment span must be 0 (hold) to " + MaxTurns + " turns; this turn only", Requested = resolved.CommitTurns, Accepted = 1 });
                resolved.CommitTurns = 1;
            }
            if (resolved.CommitTurns >= 1)
            {
                span = resolved.CommitTurns;
                action = span > 1 ? ElderCommitmentAction.Commit : ElderCommitmentAction.Single;
                return resolved;
            }
            var memory = committed?.ElderMemory;
            reason = BreakReason(world, committed, memory);
            if (reason != null)
            {
                bool had = memory != null && memory.HasCommitment;
                action = had ? ElderCommitmentAction.Broken : ElderCommitmentAction.NoCommitment;
                interventions.Add(new ElderIntervention { Kind = ElderInterventionKind.LegalConstraint, Control = "commitment",
                    Reason = (had ? "commitment broken: " : "hold unavailable: ") + reason + "; this turn's capital controls", Requested = 0, Accepted = 1 });
                resolved.CommitTurns = 1;
                return resolved;
            }
            action = ElderCommitmentAction.Hold; span = 0;
            var held = memory.Commitment;
            resolved.RetainedFieldOperation = held.RetainedFieldOperation;
            resolved.RetainedHerdFraction = held.RetainedHerdFraction;
            resolved.DevelopmentBudget = held.DevelopmentBudget;
            resolved.FieldDevelopmentShare = held.FieldDevelopmentShare;
            resolved.FishingDevelopmentShare = held.FishingDevelopmentShare;
            resolved.ClothingRoute = held.ClothingRoute;
            resolved.ShelterRoute = held.ShelterRoute;
            return resolved;
        }

        // Memory bookkeeping when the decision resolves (ElderInstitutionMemorySystem.RecordDecision).
        internal static void Record(WorldState world, Camp camp, ElderInstitutionMemory memory, ElderCompiledDecision decision, double decisionDays)
        {
            if (memory == null || decision == null) return;
            switch ((ElderCommitmentAction)decision.CommitmentAction)
            {
                case ElderCommitmentAction.Commit:
                    var accepted = decision.CommitmentValues ?? decision.Accepted ?? new ElderDirective();
                    memory.HasCommitment = true;
                    memory.Commitment = new ElderDirective
                    {
                        SchemaVersion = ElderModel9Contract.ActionSchemaVersion,
                        RetainedFieldOperation = accepted.RetainedFieldOperation, RetainedHerdFraction = accepted.RetainedHerdFraction,
                        DevelopmentBudget = accepted.DevelopmentBudget, FieldDevelopmentShare = accepted.FieldDevelopmentShare,
                        FishingDevelopmentShare = accepted.FishingDevelopmentShare,
                        ClothingRoute = accepted.ClothingRoute, ShelterRoute = accepted.ShelterRoute, CommitTurns = decision.CommitTurns
                    };
                    memory.CommitmentSetDays = decisionDays;
                    memory.CommitmentTurns = decision.CommitTurns;
                    memory.CommitmentRemaining = Math.Max(0, decision.CommitTurns - 1);
                    memory.CommitmentCapabilities = Capabilities(world, camp);
                    break;
                case ElderCommitmentAction.Hold:
                    memory.CommitmentRemaining = Math.Max(0, memory.CommitmentRemaining - 1);
                    if (memory.CommitmentRemaining == 0) { memory.HasCommitment = false; memory.Commitment = null; }
                    break;
                case ElderCommitmentAction.Broken:
                    memory.HasCommitment = false; memory.Commitment = null;
                    memory.LastCommitmentBreak = decision.CommitmentBreak;
                    memory.LastCommitmentBreakDays = decisionDays;
                    break;
                default:
                    // Deciding for this turn only replaces any commitment (a voluntary break when one was active).
                    memory.HasCommitment = false; memory.Commitment = null;
                    break;
            }
        }
    }
}
