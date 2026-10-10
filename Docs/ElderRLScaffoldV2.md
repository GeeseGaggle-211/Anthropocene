# Elder RL scaffold v2 native contract

Scaffold v2 is the frozen pre-PPO native simulation/interface contract. The
normative design remains external; this file records what the repository
implements and how it is tested. It does not define PPO, a recurrent learner,
a new objective, a fishing action head, or a technology-specific policy.

## Contract versions

| Contract | Version | Compatibility rule |
| --- | ---: | --- |
| Actor observation | 2 | v1 tensors are rejected |
| Native environment/replay | 2 | v1 reset/replay metadata is rejected |
| Five-head action | 1 | unchanged |
| Training-only critic sidecar | 1 | optional and never tensor-encoded |
| Persisted institutional memory | 1 | normalized at SaveCodec load |

The source contracts are in
[`ElderRLContracts.cs`](../Assets/_Anthropocene/Simulation/ElderRLContracts.cs),
the registry and encoder in
[`ElderObservationSchema.cs`](../Assets/_Anthropocene/Simulation/ElderObservationSchema.cs),
and the native boundary in
[`ElderEnvironment.cs`](../Assets/_Anthropocene/Simulation/ElderEnvironment.cs).

## Tensor allocation

| Offset | Width/shape | v2 semantics |
| ---: | ---: | --- |
| 0 | 16 | population, connected consumers, previous/next interval years, deficit age, QoL/security/coverage/reserves, movement/fission ages, signed migration, village flag, realized trade utility |
| 16 | 8 | retained field count/operation/maintenance/support/rebuild/service and mean current positive-`E` retained-field `q` |
| 24 | 13 | owned-herd aggregate |
| 37 | 3 | reserved |
| 40 | 16 | prior requested/accepted/realized action, split, intervention count, elapsed years, movement acceptance |
| 56 | 7 | candidate/recent fission, daughter, bounded current exit-opportunity pressure, stayer/daughter evidence |
| 63 | 1 | reserved |
| 64 | 5 x 3 | explicit semantic capability rows |
| 79 | 1 | reserved |
| 80 | 4 | structural availability: field retention, herd retention, field Development, herd Development |
| 84 | 11 x 3 | persisted EMA traces at 20/60/180 years |
| 117 | 3 | trace maturity |
| 120 | 8 | reserved |
| 128 | 7 x 12 | local site candidates |
| 212, 224, 236 | 3 x 11 | fishing rows: distance/incumbency/knowledge/yield/food/biomass/condition/current `E`/current `q`/operation/current maintenance |
| 223, 235, 247 | 3 x 1 | reserved fishing slots; no prospective build oracle |
| 248 | 8 x 8 | owned-herd groups |
| 312 | 11 x 8 | explicit commodity keys: stock, need, protected floor, physical capacity, realized import/export, legal capabilities |
| 400 | 8 x 8 | contacts: distance, physical age, affinity, realized utility/flow/migration, kin/visibility |
| 464 | 5 x 3 | presence/omission/overflow |
| 479 | 1 | reserved |
| 480 | 2 | movement enabled/count |
| 482 | 8 | stay plus seven exact candidate masks |
| 490 | 22 | reserved |

Every value has a parallel presence bit. `q_k`, absolute world age, raw turn
counts, planner Priority, forward migration attractiveness, and raw identities
are absent.

The four continuous mask bits describe structural causal availability, not
temporary affordability. The learner derives Development availability as
field-or-herd and split activity as field-and-herd. A field-only settlement
canonically uses field share 1; herd-only uses 0; neither forces Development
budget 0. Inactive heads should contribute no learner log-probability or
entropy. Temporary labor, material, reserve, or ecological constraints remain
simulator consequences.

## Settlement-local production frontier

[`SettlementProductionFrontier.cs`](../Assets/_Anthropocene/Simulation/SettlementProductionFrontier.cs)
is the single resolver for cultivation and fishing labor technology. It reads
the settlement's existing legal capability/technology state and returns a
neutral knee and identity. Existing Plough/Copper capability can improve the cultivation frontier.
Fishing progression uses separate
`PrimitiveFishingSupportedIntensity`/`SailingFishingSupportedIntensity`/
`CopperFishingSupportedIntensity` calibration fields. They default to 0.4,
so Sailing/Copper do not inherit agricultural numbers until fishing efficacy
is measured. This is a seam for later progression, not a new technology tree.

`CultivationEconomy` and `FishingEconomy` retain generic
`EffortForCapital`, `AdditionalEffort`, and `CapitalForEffort` primitives.
Production planner, maintenance calculation, build allocation, and resolver use
the same local resolution. A change never writes field/fishery `E`, recomputes
`q(E)`, or refunds historical labor.

Two explicit independent experimental override gates retain the investment
sweep's global `0.4/0.5/0.8` counterfactual path. The harness forces even the
`0.4` control, preventing later capability acquisition from contaminating a
matched experiment.

## Persisted institutional memory

[`ElderInstitutionMemory.cs`](../Assets/_Anthropocene/Simulation/ElderInstitutionMemory.cs)
stores physical initialization/integration timestamps, current signals, 33
EMA states, maturity, previous requested/accepted/realized evidence, event
timestamps, and critic-only frontier-change evidence on `Camp`.

The trace time constants are 20, 60, and 180 years. The semantic families are:

1. QoL;
2. food security;
3. water security;
4. food reserves;
5. population pressure;
6. signed realized migration;
7. field maintenance pressure;
8. herd management pressure;
9. fishing maintenance pressure;
10. realized trade utility; and
11. realized Development.

For constant `x`, lazy advancement is exact:
`m' = exp(-dt/tau)m + (1-exp(-dt/tau))x`. Event-separated updates first
integrate the old signal to the event timestamp and then install the new signal.
Only Elder-enabled camps are updated, using their local footprint and latest
turn record; observation cost does not grow with total `World.History`.

## Corrected realized evidence

- Field slot 23 is the arithmetic mean `q(E)` over the controlled camp's
  positive-capital retained fields, in deterministic tile order.
- Fishing reports current physical `E`, `q`, retained operation, and actual
  current maintenance from the same local effort equation. The former build
  placeholder is reserved rather than turned into a forecast oracle.
- Commodity rows use an explicit semantic list and omit planner Priority.
- Migration is signed realized people per population-year.
- Trade records exact nonlinear utility of physically delivered quantities in
  local person-QoL-year units, evaluated on the committed opening value curves
  captured with the agreement. The simulator has no within-turn utility path.
  Observation flows do not multiply quantities by delivery fractions a second
  time.
- Fission exposes bounded current exit-opportunity pressure, not the historical
  `DaughterGain` sentinel.
- Contacts use persisted physical observation timestamps and bilateral
  realized trade/migration evidence.

## Reward and replay

[`ElderRewardContract.cs`](../Assets/_Anthropocene/Simulation/ElderRewardContract.cs)
returns prior population, realized QoL, elapsed years, `N_prior*Q`, and
`N_prior*Q*dt`. The environment scalar is the undiscounted interval quantity.
There is no reset-population dependence and no intrinsic technology,
population-growth, diversity, exploration, agriculture, husbandry, or fishing
bonus. Campaign normalization and discounting remain learner-side.

Replay v2 includes requested, accepted, and realized directives, intervention
count/evidence, elapsed years, deterministic post-step fingerprint, and the
optional critic sidecar. Verification recomputes and checks every item.

## Privileged critic sidecar

[`ElderTrainingCriticSidecarBuilder.cs`](../Assets/_Anthropocene/Simulation/ElderTrainingCriticSidecarBuilder.cs)
builds an explicitly requested training-only record with effective
cultivation/fishing knees, neutral identities, and regime-change
presence/indicator/timestamp. `ElderObservation` has no field of this type and
`ElderObservationSchema.Encode` cannot accept or traverse it. The sidecar is
replayable but unavailable to runtime policy and ordinary game knowledge.

## Compatibility approximations and exclusions

- Old contact records have turn stamps but no day stamps. Save migration uses
  `turn * current TurnDays`, clamped to current world time. New records are exact.
- An already-active deficit first seen when institutional memory is initialized
  is dated to that boundary; old state does not retain its physical onset.
- No future unlock schedule exists in actor state. Controlled schedules remain
  harness-side and are applied only when physical time reaches the declared
  boundary.
- This pass does not change ecology, demography, fission decisions, the reward
  objective, production defaults, or the action interface. It adds observation
  evidence/persistence and routes capital labor through an already-approved
  generic frontier seam.

Focused regression coverage is in
[`ElderRLChecks.cs`](../Tools/Headless/ElderRLChecks.cs); the complete command
set is in [`Tools/ElderRL/README.md`](../Tools/ElderRL/README.md).
