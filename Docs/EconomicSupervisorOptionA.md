# Economic supervisor Option A: experimental checkpoint

## Status

**Option A is not an accepted production-planner correction.** It is preserved
as an experimental checkpoint because it improves the frozen refactor, keeps
the architectural contracts and produces useful causal evidence, but it fails
the predeclared population outcome gates against the prior planner by a wide
margin.

The tested implementation is
`e09965df16dd6cf4fc0dc1616a243c1a21abacb7` on
`fix/economic-pastoral-emergence`. Its frozen parent is `fdbf696`, the
stabilized refactor comparison is `2b67080`, and the matched prior-planner
comparison is `543084d`. No animal yield, crop productivity, movement inertia,
breeding rate, housing value, arbitrary herd cap, or technology-specific
weight changed. No RL policy is implemented here.

This report is a follow-up to
[EconomicSettlementEnvelopeValidation.md](EconomicSettlementEnvelopeValidation.md).
That document records the original technology-identity violations and the
architectural correction. This report records the subsequent deterministic
supervisor experiment and supersedes any interpretation that the corrected
architecture had also recovered the prior planner's aggregate outcomes.

## What Option A changed

The experiment addresses the remaining asymmetric treatment of field and herd
Development without introducing Agriculture, Husbandry, Fishing, nomadic, or
sedentary planners:

- Farmer, Herder, and parallel 50/50 Development rays use coarse increments of
  `max(10 people, ceil(10% of residents))`.
- A field band is forecast in its authoritative physical sequence. A weak
  band pays only the finite gathering food available over the same priced-work
  horizon; a field band that beats gathering keeps its full return.
- One bounded crossing point lets the coarse ray step over a zero-valued first
  increment when a larger sequential field prefix genuinely exhausts and beats
  that finite alternative. This is one linear scan and a fixed 24-iteration
  bisection at the single crossing, not a population or field/herd grid search.
- Feasibility clipping regenerates a canonical sequential prefix. It cannot
  fractionally spread a grant across later field bands after skipping earlier
  capital.
- Movement bounds abandoned field-rebuild value by the retained food service
  the capital protects. Movement still compares one shared physical settlement
  envelope and never invokes DemandControl recursively for candidate tiles.
- Opt-in diagnostics record physical bands, admission hurdles, selected grants,
  final compiled grants, checkpoints, and five causal arms. Production does not
  allocate or serialize these traces.

These are semantic/accounting changes, not calibration changes. The resulting
policy remains pure and deterministic, but the outcome evidence below rejects
it as a recovered production strategy.

## Focused contract results

The broad focused run passed the 70 A-I economic-settlement assertions plus all
relevant integration checks.

| Contract case | Result at `e09965d` |
| --- | --- |
| A. Technology-only invariance | Control and secondary-Husbandry clone both stay with 47 Farmers, zero Herders, 16.5 fixed-Housing replacement and zero tents. |
| B. Small complementary herd | Eight animals remain complementary: 48 Farmers, four Herders, target nine, fixed Housing, no human-food displacement, and the ninth animal requires granted Development. |
| C. Oversized acquired herd | Four hundred animals do not preempt the 13 retained Farmers. The plan assigns 96 Herders, targets 205, stays, and authoritatively slaughters 195 animals after current-turn forage and water claims. |
| D. Grossly oversized herd | Staffing stays within residents; liquidation is deterministic; animals and carcass food conserve; the herd obligation does not starve residents. |
| E. Legitimate pastoral move | A weak-capital, herd-dependent settlement moves to the materially superior tile 127. |
| F. Husbandry-first mixed settlement | A mature mixed settlement stays under either primary technology. |
| G. Agriculture-first pastoral outcome | The physically pastoral fixture moves to tile 127 under either primary technology. |
| H. Shelter capability invariance | Secondary Husbandry does not redirect feasible fixed-Housing replacement to tents; infeasible preferred routes still fall back. |
| I. Determinism and persistence | Scalar, four-worker, reversed-partition/collection, and save/load paths match exactly. |

The final broad run also passed:

```text
PASS: 36,408 agent-contract assertions
PASS: 423 planning-draft assertions
PASS: exact scalar / 2-worker / 4-worker / reversed replay across 34 turns
PASS: 638 fission assertions
PASS: 337 immigration assertions
PASS: 480 trade assertions
PASS: 142 demand-control assertions
PASS: 70 economic-settlement assertions (A-I)
PASS: 22,786 Neolithic assertions
PASS: 264 husbandry assertions
PASS: 659 pastoral-craft assertions
PASS: 1,688 herd-labour assertions
PASS: 720 shared-allocation assertions
PASS: 61 settling assertions
```

A production trace replay of seed 11 after making diagnostic capture opt-in
matched all 50 saved per-turn fingerprints and final fingerprint
`6A653D63C2E60E8B2102238BF7ABE0461B32D8DAA9E20BF9A87CE00A8D29AEEF`.

## Focused before/after behavior

Option A is downstream of the architectural herd correction, so it is
important not to attribute that earlier fix to this experiment.

| Fixture | Frozen refactor `2b67080` | Option A `e09965d` |
| --- | --- | --- |
| Small agricultural village + 8 animals | 48 Farmers, 4 Herders, target 9; growth is Development | unchanged |
| Agricultural village + 400 acquired animals | 13 retained Farmers, 96 Herders, target 205; stay and liquidate excess | unchanged |
| Mature mixed settlement, Cultivation primary | Stay | Stay |
| Same physical state, Husbandry primary | Stay | Stay |
| Weak-capital herd-dominated settlement, Cultivation primary | Move to 127 | Move to 127 |
| Same physical state, Husbandry primary | Move to 127 | Move to 127 |

The older prohibited implementation, documented in the preceding validation,
used the technology-selected policy and reserved the oversized herd first: its
400-animal fixture assigned zero Farmers, 120 Herders, and targeted 253. The
architectural refactor—not Option A—removed that behavior.

## Five matched full-system runs

Each cohort uses Continents seeds 11, 23, 47, 101, and 1729; 80 founding
tribes; ten opening turns and 40 post-opening turns; production configuration;
trade, immigration, and fission enabled; four workers; partition size 64; and
per-turn fingerprints. All reports have configuration hash
`8DB46EC2705FCFAE58864597BC4C8A949ED28ACEBED274BED23742C9C085D768`.
Quartiles are across the five final worlds.

| Version | Population P25 / median / P75 | Endpoint QoL P25 / median / P75 | Herd P25 / median / P75 |
| --- | --- | --- | --- |
| Prior planner `543084d` | 146,679 / 163,659 / 170,860 | .85294 / .85955 / .86260 | 44,764 / 62,354 / 67,767 |
| Frozen refactor `2b67080` | 68,828 / 74,159 / 80,782 | .86213 / .86712 / .87261 | 35,772 / 48,316 / 57,275 |
| Option A `e09965d` | **88,937 / 95,570 / 103,657** | **.85843 / .85890 / .86315** | **35,516 / 54,464 / 61,181** |

| Measure | Prior planner | Frozen refactor | Option A |
| --- | ---: | ---: | ---: |
| Pooled final population | 782,815 | 353,905 | **458,413** |
| Pooled managed animals | 271,418 | 210,171 | **226,501** |
| Pooled final settlements | 1,672 | 1,014 | **1,199** |
| Pooled operating field tiles | 8,264 | 2,396 | **2,916** |
| Fissions | 1,272 | 614 | **799** |
| Trades | 374 | 229 | **296** |
| Immigrants | 1,562 | 560 | **955** |

Option A raises pooled population 29.5% over the frozen refactor and recovers
24.4% of the population lost by that refactor. It nevertheless remains 41.4%
below the prior planner. Matched seed losses are 46.6%, 39.3%, 41.7%, 41.6%,
and 39.4%. It therefore fails both predeclared population gates: no more than
5% pooled loss and no more than 10% loss in any seed.

The time-weighted post-opening prior-cohort QoL comparison is .857986 for the
prior planner, .861158 for the frozen refactor, and .858109 for Option A.
Option A passes the separate QoL gate; that does not offset the population
failure and must not be conflated with endpoint QoL.

### Movement and physical settlement state

There is no persistent nomadic/sedentary identity. The physical-state evidence
does contain both long-lived fixed settlements and repeatedly mobile camps.

| Seed | Movement events | Final villages / other camps | Never-moved villages | Moved non-villages / moved more than once |
| ---: | ---: | ---: | ---: | ---: |
| 11 | 146 | 64 / 100 | 50 | 24 / 16 |
| 23 | 128 | 77 / 183 | 59 | 18 / 10 |
| 47 | 126 | 63 / 238 | 46 | 26 / 17 |
| 101 | 117 | 72 / 170 | 48 | 17 / 9 |
| 1729 | 116 | 78 / 154 | 56 | 26 / 9 |

Movement-event P25 / median / P75 is 117 / 126 / 128. Across the five final
worlds there are 259 never-moved villages and 111 non-villages that moved at
least once, including 61 that moved more than once. No natural
village-to-camp transition occurred in this cohort; the focused weak-capital
fixture proves that abandonment remains legal and can win.

As a primary-technology diagnostic only, 765 husbandry-first final camps also
held Cultivation: 68 were villages and 697 were non-villages. These are final
physical states, not a causal count of acquisition-time transitions, because
fission descendants are included. Their distributions were:

| Husbandry-first mixed final state | N | Population P25 / median / P75 | QoL P25 / median / P75 |
| --- | ---: | --- | --- |
| Village | 68 | 413.5 / 643 / 875.25 | .78425 / .87408 / .93859 |
| Non-village | 697 | 161 / 230 / 446 | .84898 / .90502 / .94014 |

## Causal and reference evidence

The canonical reference bank contains 25 states, including 16 real turn-40
settlements from seeds 23 and 101 and populations up to 1,011. With dominance
measured on final compiled grants, no dense alternative dominates the bounded
selection and all planning fingerprints remain pure. The two large synthetic
proposal-work differences disappear after final feasibility clipping. This is
evidence about the implemented objective and request set, not proof that the
objective is correct or globally optimal.

Five-turn causal replay from turn 40 isolates a modest real effect:

| Checkpoint | Frozen hurdle | Finite opportunity | Finite opportunity + bounded movement |
| --- | ---: | ---: | ---: |
| Seed 23 final population | 58,626 | 61,261 (+4.5%) | 61,492 (+4.9%) |
| Seed 101 final population | 49,695 | 52,214 (+5.1%) | 52,320 (+5.3%) |

This explains part of the recovery, not the remaining approximately 41%
population deficit. Invalid earlier prototypes that skipped prefix bands or
spread fractional grants across later bands are excluded from these results.

The regenerated reference report is
`/tmp/economic-option-a-bank/oracle-final-checkpoint.json`, SHA-256
`6d2d1b3e201986c410e83e73f067d06add736600e26666c7cd8e37f23600a3af`.
Its bounded-plan median / P75 query time is 1.115 / 1.359 ms. The path is a
session artifact; the hash and aggregate findings above are the durable record.

## Performance and computational bound

The shared allocator scale check after diagnostic traces became opt-in reports:

| Residents | Operation choices | Herd choices | Tile forecasts | Investment bands | Median plan time |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 100 | 3 | 96 | 42 | 0 | 1.82 ms |
| 1,000 | 6 | 210 | 84 | 42 | 12.56 ms |
| 10,000 | 6 | 210 | 84 | 112 | 7.65 ms |

The production algorithm remains `O(field bands + herd bands + constant)` per
settlement, with bounded local tile forecasts. It adds no year/season loop, no
global optimizer, no population-by-field-by-herd Cartesian search, and no
per-candidate DemandControl call.

The five outcome reports, captured before diagnostic trace allocation was made
opt-in, had run-time P25 / median / P75 of 14.505 / 14.614 / 14.654 seconds,
versus 8.284 / 8.440 / 8.953 seconds for the smaller-population frozen worlds
and 17.537 / 17.790 / 18.050 seconds for the much larger prior-planner worlds.
Those wall times are population/camp-count confounded and are not a normalized
microbenchmark. The exact seed-11 trace-opt-out replay fell from 14.614 to
13.350 seconds with an identical trajectory.

## Remaining strategy-selector audit

No current DemandControl or movement path selects an economic strategy from
primary technology, first breakthrough, Cultivation, Husbandry, or Fishing.
The remaining reads are bounded as follows:

- `Agents.cs` uses the union of economic capabilities only to expose the one
  economic-settlement evaluator. Its score uses animals, fields, shelter,
  water, food and recurring labor; primary technology is absent.
- DemandControl, recipe filters, and intent validation use capabilities to
  expose legal field, herd, fishing, fixed-Housing, or Light-Housing actions.
  They do not choose a strategic identity.
- `OpeningTransitions` reads the first breakthrough for the once-only opening
  compatibility grant. Secondary acquisition cannot replay it.
- `IsVillage` remains a physical/sunk-capital input for abandonment, fixed
  shelter, settlement tenure, cohesion, fission and immigration friction.
  `ProductionDemand.SettlingApplies` also uses non-village physical state for
  the existing settling burden. None dispatches an Agriculture AI.
- Technology discovery, persistence, observation and contact-memory paths read
  technology state but do not allocate labor or choose movement.
- The legacy allocator remains a save-compatibility strategy selected once by
  configuration; a turn never composes it with DemandControl.

No remaining technology-identity selector was found in the scoped economic or
movement paths. Husbandry diffusion, proximity taming and live-animal trade
remain outside this branch.

## Astra-high strategy audit recommendation

After reviewing the implementation, causal bank, dense reference and matched
five-run results, the requested Astra-high auditor recommends **B: a narrow
learned-supervisor experiment**. This is the auditor's recommendation for the
user's decision, not a decision made or implemented by this checkpoint.

The recommended experiment keeps ecology, production, conservation, integer
staffing, equipment, protected services and legal-action enforcement
deterministic. A learned settlement supervisor would choose only bounded legal
retained-field operation, herd target, Development allocation and stay/move
actions. Physically feasible alternatives must be exposed before the current
heuristic value vetoes. Evaluation should use three independent training runs,
a fixed predeclared budget, held-out worlds, both Option A and prior-planner
comparators, exact replay/conservation, the original outcome gates, and
separate reward components for survival, reliable services, population
continuity and viable capital.

The auditor explicitly recommends neither accepting Option A, running another
open-ended deterministic tuning cycle, nor moving the entire allocator to RL.
This branch stops at the experimental checkpoint so that choice remains with
the user.
