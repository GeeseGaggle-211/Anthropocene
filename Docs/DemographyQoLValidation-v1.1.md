# Demography and QoL v1.1 validation

Date: 15 October 2026

Baseline: `86cba712dfd76673bd9637052543bde85f810e0b`

Candidate branch: `feature/demographic-mortality-v1`

Contract SHA-256: `1b5df9d99f4437c28fb5b14106892aede0931b2bfcce06fe4251aacc912c7baf`

The contract stored at
[DemographyQoLContract-v1.1.md](DemographyQoLContract-v1.1.md) is byte-for-byte
identical to the supplied authority. Implementation details are in
[DemographyQoLImplementation-v1.1.md](DemographyQoLImplementation-v1.1.md).

## Decision

The implementation is **NO-GO for the native promotion gate and NO-GO for a
fresh serious MLP training generation**.

The physical law, arbitrary-duration scalar solver, paid-service closure,
gross/cause accounting, small-population rule, save migration, deterministic
replay and controlled economic recovery pass. The coupled settlement-density
extension passes its targeted reference but still needs a broader matrix.
The real native economy can rebuild specialists and protective goods without
injected resources. Three generated seeds conserve population, contain
Agriculture, Husbandry, Watercraft and mixed technology memberships, and
contain viable 600-1,000-person settlements; membership is not flow dependence.

The live social-flow gate does not pass yet, but its locus is now bounded.
The same-physical-law 2x2 ablation shows that fission creates the settlement
topology and nearly all of the camp-count increase; immigration is inert from
the shared eight-camp boundary until fission creates nearby known lineage
settlements. Immigration does not amplify the normalized fission rate, but its
receiver forecast is optimistic relative to the next resolved economy. An
exploratory row raises only the return penalty from 0.10 to 0.20, cutting
pair-level rapid reversals from 16/393 to 8/384 and eliminating one-turn
returns while preserving 97.7 percent of flows. It is not promoted: the
event-weighted receiver observations do not improve, and matched no-flow replay
plus held-out seeds are still required. The remaining projection gap is a
concrete social-mechanics attribution/forecast obligation, not a renewed
demographic death loop or a reason to alter the biological coefficients. The
heterogeneous coupled solver is also materially slower and more
allocation-heavy than the scalar analytical path at mature stress scale.

No population floor, free resource, automatic manufacturing, correction
bonus, arbitrary survival weight, reward shaping or QoL-to-mortality path was
added. The Elder reward, composite QoL function and migration's QoL,
distance, attachment and lifestyle terms remain unchanged; only the existing
return-hysteresis cost was varied experimentally; shipped settings remain
unchanged.

## Native numerical and conservation evidence

`--demography-only` passes **512 assertions**. The production path covers:

- exact fertility, exposure, crowding and disease anchors, including 75:25 at
  1,000 and crowding/disease parity at 100,000;
- a stationary fully served 1,000-person root with nonzero gross turnover;
- arbitrary physical duration, zero-time identity and 20 versus 2x10 versus
  4x5 composition;
- finite food/textile closure against the same path's person-years;
- distinct starvation ages and exact grace-boundary segmentation;
- scalar equilibrium/band crossings, no-positive-root decline and overflow/
  underflow fail-closed behavior;
- a shared evolving density for camp, connected expedition and detached
  expedition cohorts, with resource-local service pools;
- one conserved integer event ledger for connected cohorts and per-cohort
  allocation from each cohort's own continuous birth/death burden;
- explicit sub-half-person extinction without an immortal singleton or a
  demographic floor, including partition round-away without false extinction
  and exactly one aggregate residual when a connected settlement expires;
- zero-budget microscopic scalar and heterogeneous coupled paths with no free
  food, clothing or births;
- save continuation, migration provenance, dispatch/return parity and physical
  invariance to composite QoL.

The independent fixed-step RK4 matrices report:

| Matrix | Cases | Endpoint max | Person-years max | Births max | Death max | Cause max |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Fixed service | 288 | 0.000308286 | 0.000309861 | 0.000309861 | 0.000360311 | 0.000320838 |
| Finite budgets | 12 | 0.000220191 | 0.000254384 | 0.000254384 | 0.000157584 | 0.000108702 |

Those values are relative errors (for example, `0.000308286` is about
0.0308%). Maximum scaled conservation error is `6.82e-16`; maximum cause
correction is `0.000655473`, within the declared contract tolerance. The
independent four-cohort/three-pool, six-year fixed-service vector reference has
worst scaled error `1.75542e-7` and passes its `2e-7` threshold. This is one
targeted case, not a broad vector certification: the finite-budget vector tests
currently verify returned budget identities, and the 100,000-person stress
case verifies positivity/conservation, without an independent closed-budget
endpoint/cause matrix.

Focused compatibility suites currently pass 679 QoL-mortality assertions, 352
immigration assertions, 641 fission assertions, 22 demographic-structure
assertions and 74,928 agent-contract assertions. The complete three-lane fixed
suite passes in 71.7 seconds with a 1,773 MiB peak working set. An absent demographic model
selector still loads `LegacyQoLNetGrowth`; only the explicit migration API
changes it and records source-commit provenance.

## Death-spiral and recovery trajectories

`--demography-regression` passes **83 assertions** and logs gross births,
cause-specific deaths, nutrition, population, workforce, material coverage,
migration, fission, QoL and recovery state every turn.

All controlled kernel shocks start at 1,000 residents and use 20-year turns.

| Case | Distress | Restored behavior | Result |
| --- | --- | --- | --- |
| Clothing and housing loss | `1000 -> 658.844 -> 520.723` over 40 years without protective goods | Positive growth resumes immediately; `949.114` after 80 restored years | PASS |
| Temporary food loss | `1000 -> 518.578` after 20 years without food | Positive growth resumes immediately; `973.837` after 100 restored years | PASS |
| Repeated infeasible shock | `1000 -> 1.774` over 140 years with no food, clothing or housing | Decline is fully attributed; starvation begins only after the 40-year grace | PASS, physically infeasible |
| Emigration only | source `100 -> 90`, receiver gets 10 | Global residents and physical ledgers conserved; deaths remain zero | PASS |
| Fission only | parent `100 -> 70`, daughter gets 30 | Residents, provisions, event progress, deficit history and lineage transfer conserved; deaths remain zero | PASS |

The protective-goods endpoints after 20 and 40 years reproduce the handoff's
controlled values. QoL is reported for welfare diagnosis but never feeds the
trajectory. Recovery has no obsolete birth prohibition or solver overshoot.

### Endogenous recovery under the real economy

The native fixture starts with 120 residents, removes finished clothing and
fixed/portable shelter, and removes Weaver, Builder and Craftsperson
assignments while retaining finite people, tools, raw resources and food. It
then runs ordinary `AiPlanning.Decide` and `TurnResolver.Resolve`. The harness
injects no goods and calls no automatic manufacturing shortcut.

The Elder rebuilds specialists, cloth and housing. Population never falls
below 120, reaches 939 at turn 8 and 992 at turn 11. Food, clothing and housing
remain at or above 0.8 from turn 8 through the terminal boundary; the recorded
first sustained recovery is turn 8 and `specialistsRebuilt=True`. Intermediate
relapses are physically attributed rather than hidden: for example, turn 3
ends at 433 with 385 gross births and 374 gross deaths, including 221.292
exposure, 114.399 crowding and 37.946 disease deaths.

Result: **PASS for this controlled endogenous native-recovery gate**. It proves
recoverability exists; it does not prove every generated settlement or learned
policy can recover. In particular, this is one deliberately resource-feasible
120-person fixture. It does not yet cover 6/12/24-person remnants, multiple
matched sites, or economic continuation after population loss caused separately
by deaths, emigration and fission. Its `PlanningResidentPopulation` witness is
a homogeneous/local scalar test projection, not coupled forecast/resolution
parity and not a currently live Elder or RL planning interface.

## Live native economy, migration and fission

`--demography-live=30` runs seeds 17, 53 and 991 from generated Small/8 worlds
through the eight-turn opening and 30 post-opening decisions. It uses the real
heuristic, economy, ecology, trade, immigration and fission. Each continuation
is replayed from a save and compared by exact fingerprint.

| Seed | Camps | Population min / median / max | Mean F / C / H | Fully viable target camps | q around 0.4 | Migrations / migrants | Fissions | Rapid reversals |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 17 | 8 -> 94 | 2 / 216 / 936 | .887 / .192 / .345 | 5 | 23 | 198 / 2,382 | 86 | 4 |
| 53 | 8 -> 86 | 5 / 199 / 923 | .920 / .208 / .137 | 1 | 8 | 120 / 1,329 | 78 | 4 |
| 991 | 8 -> 116 | 9 / 208 / 939 | .921 / .231 / .148 | 3 | 13 | 102 / 1,012 | 108 | 9 |

Aggregate evidence:

- 114 turns, including 90 decision continuations; 24 starting camps become
  296 living camps and 76,549 residents;
- 700,137 integer gross births and 624,164 integer gross deaths;
- continuous cause totals: 428,263.306 exposure, 141,866.989 crowding,
  47,045.610 disease and 7,142.936 starvation deaths;
- maximum continuous conservation residual `2.27374e-13`;
- 296 cultivation, 184 husbandry and 111 watercraft terminal memberships, with
  236 mixed-economy camps;
- 44 terminal camps have footprint-average cultivation intensity in
  `[0.32, 0.48]`; 9 are both fully served and in the 600-1,000 target range;
- three recovered-distress witnesses, one in every seed;
- 420 migrations carrying 4,723 people and 272 fissions carrying 9,975 people.

The route counts are technology memberships only. They do not certify actual
subsistence dependence on cultivation, herding or fishing, nor material/import
dependence during recovery; those flow-level route checks remain open.

Physical rates remain diagnostic after the unchanged social decision. The
audit records 16 committed negative-net daughters, 165 committed negative-net
receivers, 17 negative-net fission proposals and 165 negative-net immigration
groups. It also records **29 committed higher-QoL/worse-mortality moves**.
Focused fixtures demonstrate legitimate above-equilibrium decline and a move
from a worse declining source to a safer still-declining receiver. The
projections use food, clothing, shelter, density and inherited deficit age,
never composite QoL; destination utility, ranking, group-size ramp, deadband,
hysteresis, fission thresholds and reward remain unchanged.

The matched legacy-QoL control uses the same seeds, social switches, heuristic
and decision horizon. It ends with 73 camps, 20,671 people, 52 migrations/400
migrants, 49 fissions and zero rapid pair reversals. Normalized activity is:

| Model | Fissions per 1,000 camp-turns | Migrants per 1,000 person-turns | Rapid pair reversals |
| --- | ---: | ---: | ---: |
| Physical v1.1 | 50.822 | 5.414 | 17 |
| Legacy QoL | 29.483 | 1.577 | 0 |

This control is matched operational evidence, not a one-variable causal
ablation: the demographic laws create different populations and settlement
counts. It motivated the same-physics attribution below, rather than a demand
that physical worlds reproduce legacy counts or reach zero reversals. No
threshold was retuned to make either table pass.

### Same-physics fission/immigration ablation

`--demography-social-ablation=30` completes one social-flow-disabled physical
opening for each seed, saves that boundary, and loads the exact bytes into four
30-decision branches. The three boundary saves all finish at turn 8; together
they contain 24 camps and 2,653 people. Every branch therefore has the same
physical law, calendar, people, economy, geography and saved history before
the two switches diverge. The corrected run passes **37,656 assertions**, including
boundary fingerprints, population/cause conservation, expectation inheritance,
workforce bounds and event follow-up.

| Fission | Immigration | Terminal camps | Terminal population | Fissions | Migrations / people | Rapid reversals | Fully served 600-1,000 camps |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| off | off | 24 | 11,489 | 0 | 0 / 0 | 0 | 2 |
| on | off | 310 | 84,187 | 286 | 0 / 0 | 0 | 11 |
| off | on | 24 | 11,489 | 0 | 0 / 0 | 0 | 2 |
| on | on | 297 | 76,581 | 273 | 393 / 4,296 | 16 | 8 |

The off/off and immigration-only branches are exactly equal in all reported
outcomes. In these three worlds the founding settlements have no qualifying
local flow, so immigration becomes active only after fission establishes
nearby known camps. Fission-only and both-on normalized fission rates are
respectively 53.790 and 53.508 per 1,000 camp-turns; immigration slightly
reduces rather than amplifies fragmentation (13 fewer terminal camps). Of the
286 fission-only commits, 255 are crowding and 31 distress. The settlement
proliferation is therefore a fission cadence/balance question, not migration
creating more splits.

All 393 combined-branch immigration intents are accepted in full: there are no
partial or rejected intents. All flows remain inside the founding lineage and
383 are between first-degree kin; 118 involve a daughter at most three turns
old. Six of the 16 reversals involve such a young daughter. The current test
therefore locates the activity in fission-created lineage clusters rather than
unrelated-settlement contact; it does not exclude an interaction with the
physical population trajectories.

The event follow-up distinguishes viable colonization from the remaining
calibration problem:

- no daughter or receiver followed for three turns becomes extinct;
- fission-only daughters average 1.926 times their founding population after three
  turns. Their next-turn QoL is only 0.028 below the fission-only estimate
  (0.019 with both systems, whose three-turn population ratio is 1.924), so no expectation reset or immediate daughter
  viability defect appears;
- immigration receivers average 1.158 times their post-flow population after
  three turns, but their next-turn QoL is 0.056 below the accepted estimate.
  At that boundary food/clothing/housing coverage changes by
  `-0.046 / -0.187 / +0.033` and deficit age rises by 10.319 years on average;
  by three turns clothing is down 0.361, QoL is down 0.136 and deficit age is
  up 35.691 years;
- 163 accepted receivers have a negative projected net rate at commit, rising
  to 213 at the next boundary and 227 among the 329 three-turn observations.
  Population still grows on average and none expires, so negative net is not a
  valid veto; it is useful attribution for the observed material-service gap.

These receiver rows are event-weighted: one camp can receive and be followed
more than once. They compare the accepted static post-inflow projection with
the later real-economy observation; without matched accepted-flow/no-flow
replay, the gap is not yet a causal estimate of harm from immigration.

Result: the previous broad social-flow concern is now a narrow native NO-GO.
Keep fission/QoL calibration unchanged while measuring its intended settlement
cadence. For immigration, replay these exact boundaries through a controlled
deadband/group-size/hysteresis sweep, separating selective reversal suppression
from next-turn receiver forecast error and preserving durable beneficial moves.
No production threshold was changed by this test.

### Matched immigration calibration sweep

`--demography-immigration-sweep=30` reloads those same three saved physical
boundaries for every candidate, leaves fission and the QoL welfare projection fixed, and
changes only the named immigration levers. It passes **162,124 assertions**.
In addition to conservation and follow-up, every turn asserts the phase
boundary directly: `DemographicReport.StartingPopulation` equals the preceding
Close boundary, `EndingPopulation` excludes the current turn's transfers, and
the population after immigration first becomes the economy/demography starting
population on the following turn. Same-Close welfare/contact diagnostics and
fission destination occupancy still see the committed state.

| Candidate | Cooldown / penalty | Migrations / people | Rapid reversals (age 1 / 2 / 3) | Fissions | Terminal population | Fully served target camps |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Hysteresis off | 0 / 0 | 382 / 4,149 | 21 (5 / 6 / 10) | 271 | 75,677 | 7 |
| **Current default** | **3 / 0.10** | **393 / 4,296** | **16 (5 / 2 / 9)** | **273** | **76,581** | **8** |
| Penalty 0.15 | 3 / 0.15 | 388 / 4,280 | 14 (2 / 1 / 11) | 274 | 76,423 | 8 |
| Exploratory candidate | 3 / 0.20 | 384 / 4,262 | 8 (0 / 1 / 7) | 273 | 76,289 | 8 |
| Penalty 0.25 | 3 / 0.25 | 381 / 4,176 | 8 (0 / 1 / 7) | 272 | 75,844 | 7 |
| Penalty 0.30 | 3 / 0.30 | 389 / 4,299 | 6 (0 / 0 / 6) | 272 | 76,507 | 7 |
| Longer cooldown | 5 / 0.15 | 388 / 4,235 | 10 (2 / 1 / 7) | 274 | 76,304 | 7 |
| Longer + stronger | 5 / 0.20 | 373 / 4,078 | 6 (0 / 1 / 5) | 272 | 76,194 | 8 |
| Deadband control | 3 / 0.10, theta 0.10 | 363 / 4,012 | 14 (3 / 2 / 9) | 284 | 77,287 | 6 |
| Group-size control | 3 / 0.10, max 0.075 | 393 / 3,452 | 17 (4 / 6 / 7) | 264 | 76,499 | 9 |

The shipped hysteresis already mitigates oscillation: removing it raises rapid
returns from 16 to 21, even though the divergent no-hysteresis trajectory has
fewer total flows. In these three fitting seeds, a penalty of 0.20 is the
smallest tested stronger penalty that halves pair-level rapid returns,
eliminates every one-turn return, retains 97.7 percent of flows and
99.2 percent of migrants, and leaves the aggregate fission count unchanged.
The response is not monotone: for example, 0.30 has more flows and migrants
than 0.20 while recording fewer fitted-sample returns. These are pair-level
camp flows, not tracked individual migrants, so they do not by themselves
establish harmful oscillation.

The 0.20 row is therefore **not promoted**. At 0.20 no followed receiver
expires, but its event-weighted mean next-turn QoL projection gap is -0.062,
clothing coverage changes by -0.204, and deficit age rises by 11.021 years;
three turns out, QoL is down 0.136 and deficit age is up 37.017 years. Those
observations do not improve on the 0.10 row, and all three seeds fitted the
sweep. The shipped `ImmigrationReturnPenalty` remains 0.10; the cooldown,
deadband, maximum group, destination ranking, `ForwardUtility`, fission
calibration and physical mortality are unchanged. The remaining native
social-flow blocker is to causally separate accepted-flow effects from the
underlying next-turn real economy, especially clothing and food-deficit
history—not a death loop and not evidence for a mortality veto.

## Native solver and turn-performance evidence

Command:

```text
dotnet run -c Release --no-build --project Tools/Headless/Anthropocene.Headless.csproj -- --demography-benchmark=5
```

The scalar comparison applies identical finite food/textile budgets, housing,
deficit ages and durations to the analytical solver and the adaptive
second-order log-midpoint comparator. The coupled column is a deliberately
heterogeneous multi-cohort/multi-pool stress matrix; it is not an alternative
solver for the same scalar cases.

| Cases | Analytical p50 / p95 / p99 ms | Midpoint p50 / p95 / p99 ms | Coupled stress p50 / p95 / p99 ms | Allocated bytes p50: analytical / midpoint / coupled |
| ---: | ---: | ---: | ---: | ---: |
| 80 | 43.72 / 44.65 / 44.74 | 118.65 / 122.66 / 123.03 | 205.22 / 213.70 / 214.66 | 1,399,888 / 864,576 / 9,492,512 |
| 320 | 139.57 / 146.66 / 147.99 | 567.64 / 575.87 / 576.41 | 951.18 / 976.72 / 981.79 | 5,676,328 / 3,493,056 / 38,723,088 |
| 1,094 | 498.76 / 523.78 / 524.97 | 2,088.10 / 2,150.94 / 2,158.11 | 3,460.83 / 3,528.97 / 3,541.84 | 19,480,024 / 11,978,608 / 132,930,448 |

At 1,094 scalar cases, maximum analytical-versus-midpoint endpoint error is
`0.000307595`, maximum person-year error is `0.000308637`, and maximum
conservation error is `1.45519e-11`. The analytical run records 3,863,379 root
evaluations, 679,300 crossed bands, 2,869,012 hazard evaluations, 2,869,012
quadrature evaluations and 36,576 service evaluations. Midpoint records
31,851,650 rate evaluations, 6,296,924 accepted steps and 73,406 rejected
steps. There are no scalar fallbacks or numerical failures.

The 1,094-case coupled stress records 2,078 pools, 2,351 cohorts, 274 exact
scalar reductions, 38,831,544 rate evaluations, 1,291,508 accepted steps and
107,090 rejected steps, with maximum conservation error `3.64e-12`. It is
bounded and correct, but slower than both scalar methods and allocates about
133 MB per batch. Optimization or production-frequency evidence remains a
performance-promotion obligation.

The independent fixed-step RK4 is used as a correctness reference in the
numerical matrices, not as a production candidate. Its native p50/p95 runtime
and a same-state legacy whole-demography kernel timing are not reported by this
harness. They remain explicit evidence gaps rather than being inferred from
unequal operation counts.

### Whole-turn profile

A late candidate snapshot was rebuilt and measured on Continents/seed 17 with
four workers, metrics on, fission and immigration isolated off, no warmup, the
opening plus five post-opening turns, and no fingerprints in the timing run.
It predates the final shared-counter/extinction and microscopic-funding fixes;
those paths do not alter this ordinary-population scenario, but the table is
not claimed as an exact final-commit timing. Percentiles include every measured
turn.

| Initial camps | Turns | Total p50 / p95 / p99 ms | Resolver execute p50 / p95 / p99 ms | Allocations p50 / p95 MiB | Peak managed heap MiB |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 80 | 12 | 422.94 / 673.55 / 714.07 | 273.60 / 496.27 / 540.20 | 79.90 / 107.47 | 443.43 |
| 320 | 13 | 1,151.91 / 1,324.96 / 1,336.14 | 774.69 / 938.04 / 957.00 | 361.80 / 449.80 | 1,880.89 |

These establish present full-turn scale; without a matched same-state prior
binary they are not a claimed speedup. The timing JSON is intentionally kept
outside the repository under `/tmp`.

## RL diagnostics and training boundary

The authorized population-weighted QoL objective is unchanged. Schema 19
exposes owner-local QoL, nutrition, clothing/housing service, deficit duration,
fertility, mortality and cause rates. Turn diagnostics separately record
settlement-local population-weighted welfare and lineage-wide weighted/mean
welfare, including immigration and fission transfer counts. Population leaving
a jurisdiction can therefore be distinguished from death or welfare creation.

The native heuristic is measured in both the endogenous shock and the live
three-seed matrix. A learned Elder is not: this repository contains
`IAgentPolicy`, the native heuristic and test policies, but no loadable MLP
runtime/checkpoint. No RL repository, frozen bank, checkpoint or normalizer was
modified.

Before training, the external schema-19 observation/history consumer and its
TRAIN-only normalizer must be migrated and validated; the external action
adapter must be exercised against the current legal-action/receipt contract;
and a fresh-generation manifest must pin the final native SHA, schema, reward
identity, cohort bank and normalizer. None can be certified from this native
repository alone.

## Gate matrix and ordered next work

| Gate | Status |
| --- | --- |
| Exact physical law and scalar numerical tolerance | PASS |
| Coupled settlement density, local service pools and deficit ages | Targeted reference PASS; broad vector closure/cause matrix and performance review remain |
| Gross/cause/resource conservation and small populations | PASS |
| Legacy save compatibility and explicit migration | PASS |
| Controlled external restoration | PASS |
| Controlled endogenous real-economy recovery | PASS for one 120-person resource-feasible fixture; breadth open |
| Native three-seed population conservation and exact replay | PASS |
| Agriculture, husbandry, fishing and mixed route presence | PRESENT; actual subsistence/material dependence not certified |
| q around 0.4 and viable 600-1,000 settlements | OBSERVED, not yet a broad balance pass |
| No reset/accounting exploit | PASS: same-boundary 2x2 conservation, expectation inheritance and three-turn survival |
| Immigration hysteresis | Shipped 0.10 mitigates fitted pair returns (21 to 16); 0.20 is exploratory (8) and unpromoted; economy/demography turn-boundary timing PASS |
| Receiver/fission calibration | **NO-GO: event-weighted receiver projection/service gap has unresolved causal attribution; fission cadence remains unreviewed** |
| Mature heterogeneous performance | REVIEW: bounded but expensive |
| Matched learned-Elder comparison | UNAVAILABLE |
| External RL observation/history and action plumbing | OPEN |
| Fresh generation/normalizer/native-SHA manifest | OPEN |

Concrete next steps toward the native GO gate are:

1. Replay accepted migrations against matched no-flow and forced-flow branches,
   then repeat on held-out seeds. Track individual transferred cohorts where
   possible, durable one-way moves, and event-weighted receiver food,
   protection, deficit history and QoL. This is required before promoting the
   0.20 candidate or changing group size. Do not add mortality to utility or
   treat a negative instantaneous net rate as a veto.
2. Measure fission parent populations, crowding gain, daughter site support and
   refission interval against the intended settlement cadence. The current
   ablation shows fission drives camp count but its daughters survive and its
   near-term estimate is close; change its gain/size calibration only if that
   cadence evidence identifies a defect.
3. Expand real-economy recovery to matched 6/12/24-person remnants, multiple
   sites and population declines caused separately by deaths, emigration and
   fission. Record whether specialist capacity, food and protective goods can
   be rebuilt without injected resources.
4. Add a seeded independent coupled-vector matrix across population, duration,
   service-pool mix and deficit-age boundaries, including finite-budget closure,
   arbitrary-interval composition and each death cause—not only total B/D/Y.
5. Expand the live matrix across more matched sites and 25-50 decisions,
   stratifying q around 0.4, service coverage, 600-1,000 viability, recovery
   time and all transfer causes. Verify actual cultivation/herding/fishing and
   material/import dependence rather than technology membership alone.
6. Profile how often live worlds use exact scalar reduction versus the vector
   path, then reduce vector rate evaluations/allocations or establish a
   reviewed mature-scale budget. Add the missing same-state legacy and
   high-accuracy-reference native timing evidence.
7. Rerun conservation/replay, controlled shocks, live social controls and
   80/320/mature performance after any narrowly justified correction. Native
   GO requires the social-flow signal to be explained without weakening
   physical mortality, suppressing legitimate negative-net moves or disguising
   transfers as deaths.
8. Only after native GO, freeze the simulator SHA and complete the external RL
   schema/history, action, normalizer and matched learned-policy gates. Then,
   and only then, create a fresh training generation.

Overall: **NO-GO for serious MLP training**. Numerical solver success and one
recoverable native fixture are necessary but not sufficient promotion evidence.

## Reproduction

From the repository root with the pinned .NET 8 SDK:

```text
dotnet run -c Release --no-restore --project Tools/Headless/Anthropocene.Headless.csproj -- --demography-only
dotnet run -c Release --no-build --project Tools/Headless/Anthropocene.Headless.csproj -- --demography-regression
dotnet run -c Release --no-build --project Tools/Headless/Anthropocene.Headless.csproj -- --demography-live=30
dotnet run -c Release --no-build --project Tools/Headless/Anthropocene.Headless.csproj -- --demography-social-ablation=30
dotnet run -c Release --no-build --project Tools/Headless/Anthropocene.Headless.csproj -- --demography-immigration-sweep=30
dotnet run -c Release --no-build --project Tools/Headless/Anthropocene.Headless.csproj -- --demography-benchmark=5
dotnet run -c Release --no-build --project Tools/Headless/Anthropocene.Headless.csproj -- 0 --jobs=3 --check-timings
```
