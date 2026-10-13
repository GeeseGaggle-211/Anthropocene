# Native Elder RL boundary — scaffold v2

This directory exposes the frozen pre-PPO native simulator contract. It contains
no trainer, Python bridge, ML runtime, learned weights, PPO implementation,
GRU, or Transformer.

The version tuple is:

- observation schema **v2**;
- environment/replay schema **v2**;
- action schema **v1** (unchanged); and
- optional training-only critic sidecar schema **v1**.

A v1 observation or environment payload is rejected. It is never interpreted
using v2 meanings.

## Authority and action boundary

The policy retains exactly five strategic controls:

1. retained-field operation;
2. retained-herd fraction;
3. a common Development labour budget;
4. the field share of Development; and
5. one masked local movement candidate, including explicit `stay`.

These are targets, not worker counts or recipes. `NativeElderDirectiveCompiler`
maps them through the production planner. Continuous masks represent structural
causal availability: field retention, herd retention, field Development, and
herd Development. A learner must not sample or score an inactive continuous
head. Field-only Development canonically uses share 1, herd-only share 0, and
no available Development channel forces budget 0. Temporary scarcity remains
a simulator consequence rather than an action mask.
The simulator remains authoritative
for legality, placement, integer staffing, tools, inputs, production, trade,
fishing, ecology, demography, immigration, and fission. Fishing remains a
deterministic economic service response and has no Elder action head.

For each decision, the compiler/resolver exposes requested, accepted, and
physically realized directives, field/herd Development split, interventions,
and elapsed physical years. Raw compiler internals and entity IDs stay out of
the actor tensor.

## Observation v2

The checked-in tensor remains 512 floats plus 512 presence bits. Its blocks are:

| Slots | Meaning |
| --- | --- |
| 0–15 | own settlement, physical interval/event ages, security, reserves, realized migration/trade |
| 16–23 | retained fields; slot 23 is mean current retained-field `q` |
| 24–39 | herd aggregate plus three reserved slots |
| 40–55 | prior requested/accepted/realized directive and elapsed years |
| 56–63 | recent fission evidence plus one reserved slot |
| 64–83 | semantic capabilities, one reserved slot, and four structural continuous-action masks |
| 84–116 | 11 persisted institutional traces at 20/60/180-year time constants |
| 117–127 | three trace-maturity values plus eight reserved slots |
| 128–211 | seven local site rows |
| 212–247 | three fishing rows with current `E`, `q`, operation, and maintenance; one reserved slot per row |
| 248–311 | eight owned-herd rows |
| 312–399 | eleven explicitly keyed commodity rows |
| 400–463 | eight permission-safe contact rows with physical ages and realized flows |
| 464–511 | presence/overflow, action masks, and reserved migration space |

The exact registry, row keys, sources, units, permissions, and missing-value
rules are emitted by the `schema` command. Commodity rows use an explicit
checked-in semantic list rather than enum order. Planner priority and future
attractiveness/build forecasts are not actor features.

The actor does not observe absolute world age, raw turn counts, `q_k`, a
marginal cost derivative, future technology schedules, or a prospective
response curve. It observes consequences: current physical capital/intensity,
operation, current maintenance, security, realized flows, and persisted traces.

## Local production frontiers

Stored `E`, `q(E)`, decay, and historical construction labour are unchanged.
`SettlementProductionFrontier` resolves cultivation and fishing effort knees
from each settlement's current legal capability state. Fishing uses independent
primitive/Sailing/Copper calibration fields, all defaulting to 0.4 until its
technology-efficiency curve is measured; it does not borrow agricultural
Plough/Copper intensities. Planner forecasts,
maintenance, build debits, and resolver inverses all use the same resolved
frontier. One settlement's capability cannot change another settlement's
frontier.

The investment-payback harness has explicit independent cultivation/fishing
override gates for controlled `q_k=0.4/0.5/0.8` counterfactuals. Those gates are
experiment configuration, not gameplay technology and not actor information.
A capability change alters future physical/economic consequences; it does not
prescribe strategy or grant/refund capital.

## Institutional memory and physical time

`Camp.ElderMemory` is optional, serializable settlement/institution state. It is
initialized for a controlled Elder settlement and updated only at physical
event boundaries. It never scans full `World.History`.

For a piecewise-constant signal, every trace uses the exact lazy update

```text
a = exp(-dt / tau)
m(t + dt) = a m(t) + (1 - a) x
```

at `tau = 20, 60, 180` years. The 11 traces are QoL, food security, water
security, food reserves, population pressure, signed realized migration, field
pressure, herd pressure, fishing pressure, realized trade utility, and realized
Development. Per-horizon maturity is `1-exp(-age/tau)` and initialization/event
timestamps are persisted.

Actor-visible time consists of known physical durations: previous and next
interval years, deficit/movement/fission age with explicit presence, contact
age, and prior-action elapsed years. For legacy saves, missing contact days are
migrated from the old turn stamp and the then-current configured turn duration;
that reconstruction is an explicit approximation. A deficit already active at
first Elder initialization is dated to first institutional observation because
older state has no physical onset timestamp.

## Reward, replay, and critic-only data

The native boundary reports:

```text
welfare_rate = prior_population * realized_QoL
undiscounted_interval_welfare = welfare_rate * elapsed_years
scalar_reward = undiscounted_interval_welfare
```

There is no episode-initial-population normalizer. Food, water, continuity,
ecology, settlement, capital, and catastrophic-loss fields are diagnostic
sidecars and do not alter the scalar objective. The external learner owns
campaign `N_ref`, physical-time discount `rho`, and the eventual GAE convention.

Replay v2 records requested, accepted, and realized directives; intervention
evidence; elapsed years; frozen background orders; optional critic sidecars;
and deterministic post-step fingerprints. Verification rejects build,
configuration, schema, action-evidence, physical-time, sidecar, or fingerprint
divergence.

`ElderTrainingCriticSidecar` is returned only when explicitly requested. It is
a separate type and is unreachable from `ElderObservation` and its encoder. It
contains effective cultivation/fishing knees, neutral frontier identities, and
regime-change indicators/timestamps for training-only asymmetric-critic
ablations. Runtime actors and ordinary game knowledge never receive it.

## CLI checks

With .NET 8 installed:

```bash
dotnet run --project Tools/ElderRL/Anthropocene.ElderRL.csproj -c Release -- schema
dotnet run --project Tools/ElderRL/Anthropocene.ElderRL.csproj -c Release -- reset-smoke
dotnet run --project Tools/ElderRL/Anthropocene.ElderRL.csproj -c Release -- step-smoke
dotnet run --project Tools/ElderRL/Anthropocene.ElderRL.csproj -c Release -- replay-smoke
dotnet run --project Tools/ElderRL/Anthropocene.ElderRL.csproj -c Release -- benchmark --steps=2
dotnet run --project Tools/Headless/Anthropocene.Headless.csproj -c Release -- --elder-rl-only
```

The focused suite covers schema accounting and rejection, local-frontier
isolation, causal field/fishing observations, physical-time EMA semigroup and
save persistence, O(1)-history construction, reward additivity, all five action
heads, Option A parity, scalar/parallel determinism, continuation, and replay.

## Learner handoff

The separately reviewed adapter may encode v2 for Gymnasium, batch native
environments, and later test MLP/PPO. It must retain native replay artifacts and
must not move economics into Python. Synthetic shocks and a sparse global event
scheduler remain deferred. GRU and Transformer are deferred learner-side
experiments, not missing native simulator work.

The autonomous model4 integration has a separately confirmed history gap:
scenario resets clear ElderMemory, autonomous initialization does not restore
it, and compilation supplies no current autonomous decision/outcome receipt.
Its nominal EMA channels therefore repeat current-state signals, unlike the
persistent scaffold path described above. See
[`AutonomousHorizonResultsV5.md`](../../Docs/AutonomousHorizonResultsV5.md) and
[`autonomous-model-v4-manifest.json`](autonomous-model-v4-manifest.json).
The frozen mechanical experiment passes; further training promotion/scale-out
is NO-GO until versioned history repair. GRU comparison eligibility is not reached.

Python training work remains in the separate private
[`GeeseGaggle-211/Anthropocene-RL`](https://github.com/GeeseGaggle-211/Anthropocene-RL)
repository. `training-repository.json` records the schema-v2 compatibility
boundary; the downstream repository must pin the exact candidate commit before
training.
