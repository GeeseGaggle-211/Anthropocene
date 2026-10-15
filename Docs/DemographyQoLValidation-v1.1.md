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

The live social-flow gate does not pass yet. Physical-mode runs show 17 rapid
pair reversals against zero in the matched legacy control, with higher
normalized fission and migration activity and only 9 of 296 terminal camps
simultaneously in the 600-1,000 range with at least 0.8 food, clothing and
housing service. The heterogeneous coupled solver is also materially slower
and more allocation-heavy than the scalar analytical path at mature stress
scale. These are diagnosis and performance obligations, not reasons to alter
the biological coefficients or silently retune social thresholds.

No population floor, free resource, automatic manufacturing, correction
bonus, arbitrary survival weight, reward shaping or QoL-to-mortality path was
added. The Elder reward and calibrated QoL utility remain unchanged.

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
counts. The higher activity and reversal count are an unresolved signal, not
proof that any particular transfer is pathological and not a requirement that
physical worlds reproduce legacy counts or reach zero reversals. The required
finding is a same-physics attribution showing no expectation/ledger reset,
accounting exploit or unjustified feedback, with daughter/receiver follow-up
service and economic trajectories. Social-cohesion relief plus density
mortality remains a candidate interaction. Calibration is preserved pending
that evidence; no threshold has been retuned to make this table pass.

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
| No reset/accounting exploit or unjustified migration/fission feedback | **NO-GO: activity/reversal signal requires same-physics attribution** |
| Mature heterogeneous performance | REVIEW: bounded but expensive |
| Matched learned-Elder comparison | UNAVAILABLE |
| External RL observation/history and action plumbing | OPEN |
| Fresh generation/normalizer/native-SHA manifest | OPEN |

Concrete next steps toward the native GO gate are:

1. Run a same-physical-law `fission off/on x immigration off/on` ablation on
   the same seeds and initial saves. Export each of the 17 reversal pairs,
   lineage/local welfare, physical rates, deficit histories and QoL decisions,
   then follow daughter/receiver food, protection, workforce and recovery. The
   legacy control alone cannot isolate causality, and zero reversals is not a
   mechanical target.
2. Attribute the excess activity to the forward QoL estimate, seasonal
   service changes, social-cohesion relief, density mortality, cached fission
   destinations or their interaction. Do not change weights, deadbands,
   hysteresis or fission thresholds unless that controlled evidence identifies
   a specific defect.
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
dotnet run -c Release --no-build --project Tools/Headless/Anthropocene.Headless.csproj -- --demography-benchmark=5
dotnet run -c Release --no-build --project Tools/Headless/Anthropocene.Headless.csproj -- 0 --jobs=3 --check-timings
```
