# Turn-horizon implementation and performance audit

September 25, 2026. This is the handoff for `codex/turn-horizon-refactor`.
The performance source checkpoint is `37b1bdd`; the seasonal prepared-turn
reference is `c33a5b7cb1b3999591caeaf1835802dec7cd5612`, a descendant of
`features/neolithic-agricultural`. Both preserve the prior-cohort needs rule.
The pinned `main` opening reference is
`e472cf760232eaa614b387a0212146221f505cd9`. These are different
simulation models, so old and new fingerprints are expected to differ.

## Reproduce the data

Run from the authoritative Windows checkout with the Release .NET 8 runtime.
The [audit README](../Tools/PerformanceAudit/README.md) gives the complete
archive, build, opening-save, and paired-run commands. The baseline source
archive SHA-256 was
`1FD3C5F68C97BC36EE605DB8AA76371E0DEEE93D0F7CF98B69AFB75746FBA7F9`.
The three-pair timing manifest records binary hashes, and the common
opening saves hash to `ECFAE40B06AB5B036894A8A8FE3DA00DA67F3EBE828A1F1EA358F61C40487B7A`
at 80 tribes and `E101EA96F7B53E2C1CAA2A8A90CC5A73400DAAF1253188C33ABB11B756D690FC`
at 320. Keep the ignored `TestResults/TurnHorizonBaseline`,
`TurnHorizonCandidate`, `TurnHorizonComparison`, `TurnHorizonPairs`, and
`TurnHorizonMain` directories together if transferring raw evidence.

The measured workload uses Standard / Continents / seed 1729, a 7,300-day
turn, 1,024×512 with 80 tribes or benchmark-only 2,048×1,024 with 320.
Both binaries load the **same baseline opening save** at natural completion
(turn 10 or 11) and resolve 25 more turns. Each process performs one
96×48/four-tribe warmup. Four managed workers use 64-item partitions.
The paired Release processes run sequentially, alternating baseline-first
and candidate-first order. Formal timing disables detailed metrics, per-turn
fingerprints, and camp details; it measures `AI + TurnPlanner.Prepare +
TurnResolver.Resolve`. Both binaries use the same current audit harness,
compiled against their own simulation source. The JSON records .NET 8.0.31,
Windows x64, configuration/save hashes, timings and outcomes. The
[pair summarizer](../Tools/PerformanceAudit/Summarize-TurnHorizonPairs.py)
checks common inputs and repeated numeric outcomes and writes every aligned
turn to CSV. Detailed-counter and exact-fingerprint runs are separate.

## Matched 25-turn timing

Each row is a pair's median across all 25 agricultural continuation turns.
The gain column is the median of **paired per-turn** total-time gains, not a
ratio of the two displayed medians. Milliseconds are wall time.

| Tribes | Pair | AI baseline → horizon | Prepare baseline → horizon | Resolve baseline → horizon | Combined baseline → horizon | Paired gain | Allocated MiB baseline → horizon |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 80 | 1 | 60.9 → 58.0 | 36.8 → 26.2 | 647.4 → 98.9 | 775.5 → 196.4 | 73.2% | 369.0 → 131.5 |
| 80 | 2 | 57.4 → 54.9 | 36.1 → 21.3 | 634.3 → 78.7 | 741.8 → 181.3 | 74.8% | 369.5 → 132.4 |
| 80 | 3 | 59.7 → 70.7 | 35.3 → 22.2 | 554.8 → 80.9 | 735.3 → 210.4 | 72.1% | 369.5 → 132.0 |
| 320 | 1 | 344.5 → 290.1 | 193.9 → 129.8 | 3,354.1 → 390.1 | 3,992.0 → 946.9 | 76.9% | 1,559.8 → 518.9 |
| 320 | 2 | 330.7 → 260.1 | 203.2 → 120.3 | 3,704.3 → 343.1 | 4,269.7 → 1,038.3 | 74.1% | 1,559.8 → 518.4 |
| 320 | 3 | 359.2 → 261.0 | 217.3 → 118.9 | 3,293.3 → 352.8 | 4,189.2 → 804.4 | 76.6% | 1,559.8 → 519.0 |

The median pair-level improvement is **73.2% at 80** and **76.6% at 320**;
all six pairs improve. No corresponding turn is more than 10% slower in two
or more pairs. Sampled per-turn managed heap falls from about 461–470 to
394–396 MiB at 80 and from about 1,906–1,916 to 1,581–1,588 MiB at 320.
These are managed-heap snapshots after each turn, not a continuous peak or
whole-process working set. AI and preparation exhibit occasional GC and
scheduling spikes; the repeated paired medians are the speed evidence.

The seasonal reference spends about 43–47% of the combined 320-tribe median
turn in the instrumented ecology pass (and 28–46% at 80 across pairs). That
timer already includes claim construction, field rescoring and source
lookups; it stops before transfer receipts are applied. The greater combined
gain reflects less receipt bookkeeping and allocation/GC pressure beyond
that timer, as well as changes in AI and preparation costs. This is an
Amdahl warning against treating tile integration alone as the complete hot
path.

### Work counters from one matched starting turn

Counters are enabled here and their milliseconds are **not** formal timing.
The 80-tribe rows are turn 11; 320 rows are turn 12.

| Measure | 80 seasonal → horizon | 320 seasonal → horizon |
| --- | ---: | ---: |
| Unique active tiles | 2,611 → 2,615 | 10,571 → 10,620 |
| Tile integration calls | 34,764 → 2,615 | 148,031 → 10,620 |
| Agricultural dependency tiles revisited seasonally | 407 → 0 | 1,740 → 0 |
| Claims allocated | 84,454 → 3,337 | 362,724 → 13,606 |
| Local source lookups | 166,357 → 19,105 | 791,504 → 73,606 |
| Local source queries / indexed deposit edges | 564 / 14,889 → 560 / 14,841 | 2,052 / 50,691 → 2,054 / 50,688 |
| Planner calls / forecasts | 77 / 572 → 77 / 556 | 313 / 1,800 → 313 / 1,796 |
| Production receipts | 104 → 110 | 480 → 495 |
| Instrumented ecology ms | 336.1 → 21.7 | 1,553.7 → 100.1 |
| Instrumented allocated MiB, full turn | 312.2 → 151.4 | 1,283.3 → 564.2 |

`TileIntegrations == ActiveTiles` in the new resolver; the 80 outer
agricultural barriers and the internal 80-segment active ecology path are
gone. A fixed-world duration check uses **28 tile kernels** for half-, one-,
five-, ten- and twenty-year turns. The resource pass now scales with active
tiles, claims, camps and their radius-one land candidates, and local source
edges. The field allocator uses a fixed 32-band solve over at most seven
land candidates per camp. Claim grouping is one global grouping plus one
tile grouping, rather than each camp rescanning the growing claim list.
The wider turn still includes AI, herds and demographic lookups; this audit
does not assert strict O(camps) for every subsystem.

## Outcome and validation limits

The 25-turn Standard continuations repeat their numeric outcomes exactly
across timing pairs. Relative to the seasonal reference, the largest
per-turn population / prior-cohort weighted QoL / realized-food-yield
differences are **2.36% / 4.11% / 2.12%** at 80 and **1.28% / 2.27% /
1.59%** at 320. Final linked population is 49,747 → 50,593 at 80 and
197,431 → 197,577 at 320. These are calibration differences, not a
fingerprint match or an outcome-equivalence gate.

A subsequent production-config gameplay audit across three 80-tribe worlds
and isolated agriculture/husbandry pilots found larger specialization and
local-food effects. Its population, QoL, food and fishing tables are in
[TurnHorizonGameplaySweep.md](TurnHorizonGameplaySweep.md). Those outcomes
are additional calibration evidence and do not alter the timed Release
pairs above.

The 20 quick Neolithic transition cases all completed without invariant or
conservation failure. The [calibration comparator](../Tools/NeolithicTransitionValidation/Compare-Horizons.py)
aligns 14 cases with identical initial/injected states across 182 case-turns
and 7,837 paired metric rows. Six mobile or fixed-stress fixtures inject
different starting population or food and are flagged as unpaired. The
expanded condition/tool trace was produced by isolated Release builds of
the same archived baseline and the `37b1bdd` simulation. Removing its added
fields gives exact matches to all 263 rows in each earlier quick trace.
Local differences can be much larger than the Standard aggregate:

| Aligned case, final turn | Seasonal | Horizon | Main difference |
| --- | ---: | ---: | --- |
| Medium woodland residents | 43 | 12 | −72.1%; a T7 wild-herd choice changes after earlier ecology divergence. |
| High forest residents | 168 | 264 | +57.1%; the horizon grants much more opening-stock/renewal harvest. |
| Open fertile residents / field capital | 176 / 0.06428 | 115 / 0.00689 | −34.7% residents; lower realized gathering then changes bridge investment and later automation. |
| Fixed open fertile T8 food produced | 327 | 5,184 | A different prior ecological state changes staffing from 24 to 46 farmers; this is not an isolated field-allocator comparison. |
| Dry marginal T10 Stone receipt | 325.9 | 0 | Earlier Toolmaking unlock switches the candidate to regular Tools, requiring less Stone; both reserve/material ledgers reconcile. |

The first high-forest divergence occurs before agriculture: on tile 1363,
the seasonal model awards about 199 food while the horizon model awards
about 364, and ending biomass differs sharply. The old solver clips stock
and access at each season. The new solver uses four climate profiles as
weighted coefficients and one bounded stock/harvest equation; it cannot
reproduce every within-turn dry-season bottleneck. A bounded two-pass
condition correction matched that tile's **ending biomass** but still
awarded 364 food and broke a zero-renewal opening-stock case, so it was
rejected. The discrepancy is a documented model limitation, not evidence
of an agricultural staffing defect or a lost material receipt. Do not claim
outcome equivalence to `c33a5b7` from the Standard aggregate. The trace
records per-field condition, mean condition across eligible radius-one land
fields, adjacent Shallows/Lakes mean condition where present, ending Hand,
regular, Farm and Herding Tools, field intensity/capital, goods outputs and
inputs, and material ledgers. The largest aligned land-condition change is
dry marginal T13, **0.982203 → 0.798141** (−18.74%). Aquatic comparison
has only 11 aligned case-turns; its largest shift is −0.249% at open fertile
T8. These means do not stand in for every affected tile in the world.

The fixed open fertile QoL gap has a separate visible chain. Its population
first diverges at turn 3, before Neolithic production. At turn 8, the
seasonal planner assigns 30 craftspeople and 24 farmers, while the horizon
planner assigns 7 craftspeople and 46 farmers. The seasonal camp builds
Housing and unlocks Construction; the horizon camp spends its 560 available
craft worker-intervals on Hand Tools and Cloth, gathers no Wood or Clay,
and builds no Housing. Its Cloth component reaches 1 but Housing stays 0,
so QoL is 0.5 through turn 13. Ending Hand Tools are 46.482 → 0, while
regular Tools are 0 → 12.287. Mean land condition changes only
0.99998873 → 0.99996787, so local condition collapse is not the cause of
this QoL gap. The production labor and material ledgers reconcile. This
exposes a planner/balance sensitivity to changed ecology and prior
population, rather than a missing production debit.

Within the new model, the full headless suite passes conservation and exact
scalar/two-/four-worker, reversed-partition, reordered-collection and
save-continuation replay over **35 resolutions**, including 25 mature
post-opening turns. Focused checks cover 84 horizon ecology assertions,
61 cultivation assertions, 44,012 Issue #25 ecology assertions, 0/partial/full
Farm Tool coverage, contested Food/biomass/freshwater, and duration work
counts. Separate Standard 80/320 one-turn exact reruns from the same saves
matched initial, per-turn, and final fingerprints within the horizon model.
No transient horizon fields or counters were serialized; compatible saves
loaded in both binaries.

## `main` context and next measurements

A separate **single-run, warmed** full-opening check used the same current
harness and pinned `main` source. Natural completion took 10 turns at 80
and 11 at 320. Combined AI + preparation + resolution totals were:

| Tribes | Pinned `main` | Seasonal reference | Horizon |
| ---: | ---: | ---: | ---: |
| 80 | 2.208 s | 2.169 s | 2.164 s |
| 320 | 9.250 s | 16.170 s | 9.769 s |

The candidate is near `main` in this single opening sample. `main` lacks
the Neolithic economy, the ecological models diverge, and this is not a
three-pair opening acceptance run. The older within-10%-of-`main` target
therefore remains **unproven**. No corresponding agricultural opening turn
exceeded twice `main` in this sample (the highest ratio was 1.88× at 80 and
1.42× at 320). Before declaring balance parity, derive and
test a turn-level representation of seasonal stock/access clipping with
constant work in duration, then rerun the dry and high-variance local cases.
Do not reintroduce authoritative seasonal states or an event queue to
hide those differences.
