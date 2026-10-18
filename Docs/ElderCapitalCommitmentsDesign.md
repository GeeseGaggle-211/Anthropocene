# Elder capital commitments: multi-turn capital decisions (design for review)

Status: proposal, 18 October 2026. Approved in principle: saving a commitment and extending saves is acceptable, and
save compatibility is a convenience, not a requirement. Nothing is implemented yet.

## Problem

The learned Elder re-chooses every control every 20-year turn, including the slow capital decisions:
- field retention and field development;
- herd retention and herd development;
- clothing and shelter routes.

Their payoff arrives several turns later: a herd breeds, field capital builds, shelter changes the housing stock.
Under PPO each turn's choice is credited only through noisy per-turn advantages.

Evidence:
- The mixed-use oracle (`--model9-oracle`) shows fixed allocations beating the heuristic by 1-11% in 5 of 6 worlds.
  The learned policy captures about 3.6%.
- Whole-world welfare spreads about 25% across allocation choices, and per-seed outcomes diverge by about 9%, so
  the per-turn signal is weak against the noise.

## Idea

Let the Elder commit a capital decision for a span of turns, then hold it. Holding is an option in the
reinforcement-learning sense:
- One decision covers several turns of consequences.
- The policy learns from the value of the whole span.
- The world sees steady capital policy instead of turn-to-turn churn.

Short-cycle controls are not committed and stay per-turn: service levels, the food and growth buckets, fishing
staffing.

## Authoritative state (saved)

`ElderInstitutionMemory` moves to schema 2 and gains one commitment record per village:
- the committed values: retained field operation, retained herd fraction, development budget, field development
  share, and the clothing and shelter routes;
- the turn it was set and its span in turns (1-6);
- the remaining turns.

Schema-1 memories load with no commitment, which means per-turn behaviour.

## Temporal abstraction

At each turn boundary a village either holds its commitment, with no new strategic or route draw, or makes a new
one. A commitment ends when its span runs out, when the Elder breaks it (an explicit action, at a cost), or on a
forced break. Forced breaks happen at:
- fission, for both parent and daughter;
- relocation;
- a capability change (a new technology);
- a survival shortfall, where the existing distress rules already override.

No sub-turn state exists and no new clock is added. The span is counted in turns, so a shorter turn length means a
proportionally shorter commitment in years. If the turn length becomes configurable, revisit the span in years.

## Action and observation

- **Action.** A categorical span head (hold, or commit for 1, 2, 3, 4 or 6 turns) is added to the directive.
  Holding re-emits the stored values, so the compiler and planner see an ordinary directive and stay unchanged.
- **Observation.** The commitment's values, its age and remaining turns, whether it was forced to break, and the
  realized-versus-committed gap (the existing `ElderPreviousDecisionObservation` covers part of this).
- **Teacher labels.** The heuristic is represented as "commit for 1 turn", which reproduces it exactly, so teacher
  fidelity holds and stage 1 can clone it.

## Learner

- **Semi-Markov PPO.**
  - A held turn draws no new action, so it adds no log-probability and no entropy.
  - The decision's advantage is the discounted reward over its span plus the bootstrapped value at the next
    decision. GAE runs over decision points, not turns, and the discount already follows elapsed years.
- **Breaking cost.** A small cost on voluntary breaks makes commitment the default without forbidding change. It is
  the same "slight penalty" principle as the mix-change cost, but on a decision the policy can actually see.
- **Critic.** It predicts value at decision points. The privileged inputs are unchanged.

## Scaling

- The cost is O(villages) per turn for storage and observation. Planning is unchanged, because a hold re-emits a
  directive and the planner compiles it as before.
- Decisions per world fall, which saves learner compute. Simulator cost is unchanged.
- With the median commitment near 3 turns, policy samples drop about threefold while the same simulated years cover
  more consequences per sample. That should help sample efficiency at RL-training scale.

## Validation

1. **Teacher fidelity:** commit-for-1 everywhere reproduces the heuristic exactly (fingerprints).
2. **Save/load:**
   - schema-2 memories round-trip;
   - schema-1 loads with no commitment;
   - continuation fingerprints match.
3. **Forced breaks:** fission, relocation, new technology and survival shortfall each end a commitment, with a logged
   reason.
4. **Learning test:** stage 2 with commitments against stage 2 without, on 3 seeds each, gated per regime. A
   commitment policy that beats the shared-head baseline beyond seed noise is the result we want. Then rerun stage 3
   with free routes, now committed: if committed routes no longer cost default worlds, this was the cause.

## Rejected

- **Fixed decision cadence for everyone** (for example, every third turn): it ignores state and forces stale choices
  in a crisis.
- **Longer turns:** that changes game semantics.
- **Value-target tricks without commitment** (n-step returns, a larger lambda): cheaper, but they don't reduce the
  number of noisy decisions. Worth trying as a control, not as the fix.
