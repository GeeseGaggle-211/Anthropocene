# Demography and QoL v1.1 validation

Date: 15 October 2026
Baseline: `86cba712dfd76673bd9637052543bde85f810e0b`
Candidate branch: `feature/demographic-mortality-v1`
Implementation commit: `c0ff9f1a6d7402185db2c157acb2a9702171093f`

## Decision

Implementation status is **complete enough for continued native diagnosis but
NO-GO for serious MLP training**.

The physical kernel, resource closure, save/version boundary, gross population
accounting, deterministic replay and controlled externally restored shocks pass.
The native heuristic does not sustain clothing recovery in the endogenous
specialist-rebuild fixture, and the repository contains neither a learned-policy
runtime/model artifact nor the unresolved observation/action/training fixes
needed for a matched learned-Elder comparison. Numerical success does not waive
those failures.

No population floor, free resource, automatic manufacturing, correction bonus,
migration threshold change or survival-shaped reward was added.

## Numerical and conservation evidence

`--demography-only` currently passes 70 assertions. Coverage includes:

- exact birth, exposure, crowding and disease anchors;
- the 1,000-person stationary root with nonzero gross turnover;
- 75:25 crowding/disease at 1,000 and parity at 100,000;
- zero-time identity and finite positive propagation at 0.25, 6, 12 and 24;
- conserved, cause-attributed integer extinction below half a resident, with no
  immortal singleton or survival floor;
- arbitrary duration and 20 versus 2x10 versus 4x5 composition;
- finite food/textile person-year closure and typed-food balance;
- 40-year starvation grace and immediate reset at full nutrition;
- agreement with an independent fine-step RK4 reference;
- integer gross-ledger/cause reconciliation in a real turn;
- invariance of physical births/deaths to composite QoL;
- save continuation, explicit migration provenance and schema 19;
- dispatch/return parity against an undispatched control, including birth/death
  remainders and connected-hunter food demand.

Legacy compatibility remains executable. Focused results after the refactor are
679 QoL-mortality assertions, 340 immigration assertions and 634 fission
assertions. Old serialized configurations with no `DemographicModel` selector
load `LegacyQoLNetGrowth`; only the explicit migration API changes that state,
and its fingerprinted provenance includes the source simulator commit.

## Controlled death-spiral trajectories

`--demography-regression` passes 69 conservation/attribution assertions and logs
every turn's gross births, cause-specific deaths, nutrition, population,
workforce, material coverage, migration, fission, QoL and recovery marker.
For the kernel-only controls, QoL uses the unchanged composite welfare formula
with water fixed at one and mature expectation fixed at 0.7; it is reported for
diagnosis and never fed back into the physical trajectory.

All controlled kernel shocks begin at 1,000 residents and use 20-year turns.

| Case | Shock endpoint | Recovery observation | Result |
| --- | ---: | --- | --- |
| Clothing and housing absent for 40 years | 520.72 | Positive growth resumes immediately after restoration; 949.11 after 80 restored years | PASS |
| Food absent for 20 years | 518.58 | Positive growth resumes immediately; 973.84 after 100 restored years | PASS |
| Repeated zero food/clothing/housing | 1.77 after 140 years | Every decline is exposure/crowding/disease, plus named post-grace starvation from turn 3 | PASS, genuinely infeasible |
| Emigration only | source 100 -> 90, destination receives 10 | Global people and gross ledgers conserved; deaths remain zero | PASS |
| Fission only | parent 100 -> 70, daughter receives 30 | Global people, deficit history and lineage transfer accounting conserved; deaths remain zero | PASS |

The first protective-goods shock endpoints (658.84 after 20 years and 520.72
after 40) reproduce the handoff's controlled reference values to the reported
precision. There is no integration overshoot or obsolete post-deficit birth ban.

### Endogenous real-economy recovery

The native heuristic fixture removes finished clothing and fixed/portable
shelter plus Weaver, Builder and Craftsperson assignments, while retaining real
people, food, tools and raw construction resources. It then uses ordinary
`AiPlanning.Decide` and `TurnResolver.Resolve`; the harness injects no goods.

Specialists are rebuilt in the first turn and food/housing recover. Clothing is
fully covered for turns 0-2, falls to 0.568 at turn 3 and reaches zero for turns
4-7. Population moves 120 -> 223 -> 326 -> 449 -> 473 -> 439 -> 464 -> 490 ->
513. The turn-4 decline is fully attributed (319 gross births, 353 gross deaths;
162.69 exposure, 128.93 crowding, 42.77 disease and 18.27 starvation in the
continuous cause counters). It is not a migration/fission misclassification or
workforce arithmetic loss.

Result: **NOT RECOVERED at the terminal boundary**. This identifies a native
economic/policy durability defect: transient specialist reconstruction does not
maintain the textile service the expanding population requires. It is an open
GO-gate item, not a reason to weaken physical mortality.

## Migration, fission and welfare audit

The calibrated utility weights, destination ranking, deadband, hysteresis and
fission thresholds are unchanged. One controlled compatibility defect was fixed:
the daughter state already inherited the parent's expectation, but its veto and
founding QoL estimate modeled a fresh expectation. The estimate now uses the
same inherited expectation, and a focused regression proves that lowering only
the parent's expectation changes the daughter estimate; no threshold or survival
weight was added. Physical diagnostics establish
that a destination with higher projected QoL and worse physical mortality can
still attract migrants; the controlled case moves 10 people and sets
`HigherQoLWorseMortality` and the matching metrics counter. This is reported,
not silently prevented with a survival weight.

Social transfers proportionally move gross-birth/death progress. Fission also
moves typed provisions, inventory and herds under the existing rules and now
inherits expectation and physical deficit history. Local and lineage welfare
records expose immigration and fission transfers separately, preventing a
policy evaluation from interpreting a jurisdiction change as welfare creation.

The focused suites retain the established social-cohesion, fission and
immigration calibration. They do not by themselves prove absence of long-horizon
fragmentation feedback in mature multi-settlement worlds. That remains part of
the native-world promotion gate.

## Native solver comparison

Command (Release build):

```text
dotnet run --project Tools/Headless/Anthropocene.Headless.csproj -c Release --no-build -- --demography-benchmark=5
```

The comparator applies the same finite food/textile budgets, housing capacities,
durations and starvation boundaries through an adaptive log-midpoint solver.
The 1,094-settlement scale is the recorded mature fission count used by prior
performance work. Values are local-machine measurements and cover the
demographic batch, not the whole turn.

| Settlements | Analytic p50 / p95 ms | Midpoint p50 / p95 ms | Analytic / midpoint p50 allocated bytes |
| ---: | ---: | ---: | ---: |
| 80 | 53.68 / 59.50 | 125.21 / 166.00 | 1,399,888 / 864,576 |
| 320 | 156.01 / 194.87 | 617.34 / 626.81 | 5,676,328 / 3,493,056 |
| 1,094 | 545.84 / 741.98 | 2,238.61 / 2,273.92 | 19,480,024 / 11,978,608 |

Across the repeated scenario matrix, maximum analytic-versus-midpoint endpoint
error is 0.03076% and maximum person-year error is 0.03086%; median errors are
about 0.00304% and 0.00172%. Maximum conservation error is
`1.46e-11`. There were zero numerical failures and zero fallbacks. The analytic
batch records 3,708,777 root evaluations, 679,300 crossed bands, 2,869,012
hazard/quadrature evaluations and 36,576 service evaluations at 1,094
settlements; midpoint records 31,851,650 rate evaluations, 6,296,924 accepted
steps and 73,406 rejected steps.

The analytic method is faster in this isolated native comparison, but allocates
more. No cache exists (`0/0` hits/lookups), and there is intentionally no silent
production fallback. Whole-turn 80/320 timings and a diagnostics-off allocation
profile have not yet been rerun, so the broader performance promotion gate is
still open.

## RL diagnostics and training gate

The authorized objective was not edited. Schema 19 exposes owner-local QoL,
nutrition, clothing/housing coverage, deficit duration, fertility/mortality and
cause rates. Turn history carries settlement-local and lineage-wide
population-weighted welfare plus transfer counts. These allow failures to be
classified as food, protective goods, density, disease, starvation, migration,
fission or action/economy defects.

The native heuristic was measured on the endogenous shock. A learned Elder was
not: this repository has `IAgentPolicy`, `HeuristicTribePolicy` and test policies,
but no loadable MLP policy or checkpoint. Substituting the heuristic or a scripted
policy would not be a matched learned-policy comparison.

### GO matrix

| Gate | Status |
| --- | --- |
| Exact physical law and numerical accuracy | PASS |
| Gross/cause/resource conservation | PASS |
| Save compatibility and explicit migration | PASS |
| Controlled external restoration | PASS |
| Dispatch, emigration and fission attribution | PASS |
| Deterministic save/worker replay | PASS; complete fixed suite passed in 67.1 seconds on three lanes (1,916 MiB peak working set) |
| Sustained endogenous native recovery | **FAIL** (textile relapse) |
| Mature live-world migration/fission pathology sweep | OPEN |
| Pinned `q_k=0.4` live-economy matrix | OPEN |
| Three-seed agriculture/husbandry/fishing/mixed continuations | OPEN |
| Whole-turn 80/320 performance profile | OPEN |
| Matched learned-Elder comparison | **UNAVAILABLE** |
| RL observation/history defect closure | OPEN (schema 19 diagnostics added; policy-memory validation remains) |
| RL action/trade-interface defect closure | OPEN |
| Training generation, normalizer and native-SHA manifest | OPEN |

Overall: **NO-GO for restarting serious MLP training**. The branch is suitable
for diagnosing and fixing the remaining economic and RL gates without changing
the physical law or QoL reward.
