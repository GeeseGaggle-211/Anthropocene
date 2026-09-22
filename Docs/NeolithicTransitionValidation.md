# Paleo to Agriculture transition validation

Issue #14 measures the integrated transition at harness revision
`58566cffa53bf9dc33511765ac1773eeaef72169`. It is a measurement report, not
a calibration recommendation. No production constant, ecology rate, technology
reward, or economy policy was changed for this work.

## Method

The runner generates seeds 1–18 with the production configuration on a compact
128×64, eight-player world. It isolates the focal camp and its society to bound
the experiment, while retaining the generated wild herds and ecology. It uses
the ordinary filtered-observation `HeuristicTribePolicy` and its normal Economy
order after ordinary activity-driven Cultivation discovery. The site contrast
cohort deliberately relocates an otherwise unmodified generated camp to an
eligible generated site; it is therefore a controlled generated-site contrast,
not a claim about the normal placement distribution.

Every eligible original placed camp with a seven-land-tile radius-one footprint
is preflighted and recorded; one ordinary Cultivation outcome is retained as a
placed-camp control. The selection table reports all shortlisted candidates and
their non-Cultivation outcomes. The final selected open site is seed 15/tile
3819, a Grassland center, rather than the earlier Desert-overlap pilot site.

| requested contrast | seed/tile | center environment | radius-one Low/Medium/High | operational Paleo support |
|---|---:|---|---:|---:|
| productive river | 2 / 3682 | Tropical | 0 / 0 / 7 | 461 |
| open fertile | 15 / 3819 | Grassland | 4 / 2 / 1 | 52 |
| medium woodland | 11 / 4530 | Grassland | 0 / 4 / 3 | 137 |
| high forest | 3 / 3713 | Tropical | 0 / 0 / 7 | 439 |
| dry marginal | 17 / 4373 | Desert | 4 / 1 / 2 | 37 |

The local Paleo support number is an operational eight-generation q=0,
Paleolithic hold-order estimate: growth and progression are disabled only in the
probe; the largest population with no deaths, final-three-turn full satisfaction
and at least its original generation reserve is bracketed against P+1. It is not
an infinite-horizon carrying-capacity proof. Normal mobile rows report their
actual transition location and population/support ratio; they are not described
as retained habitat or exact stress ratios when movement changes the location.

Fixed-site controls retain ordinary practice/discovery but hold the camp during
Paleo, then submit the standard automatic Economy intent. The three settlement
stress controls set population and one generation of PlantFood once immediately
after recording the live breakthrough; they retain live experience, technology,
q/E, ecology, material and finished-good state. The explicit JSONL setup event
is excluded from demographic flow accounting; every subsequent generation uses
the normal ledger.

Each live Cultivation row resolves 25 further generations. The trace preserves
raw demographic, food, material receipt/production, biomass/Food/water ecology,
workforce, inventory, q/E footprint, accepted orders and decision candidates.
Raw output is ignored in `TestResults/Issue20/Issue14-Final-20260923-0105`;
`Tools/NeolithicTransitionValidation/Summarize.ps1` produces compact phase and
emergence tables from it.

## Final matrix

All 20 rows reached Cultivation and 25 post-discovery resolutions. `P` is final
resident population; food is the minimum realized satisfaction; `q/E` are final
mean footprint intensity/capital. Every row recorded zero discarded inventory
and zero runner invariant failures.

The complete committed phase table is
[NeolithicTransitionValidationPhaseMetrics.csv](NeolithicTransitionValidationPhaseMetrics.csv)
and the per-good output, assignment and observed-technology timing table is
[NeolithicTransitionValidationEmergence.csv](NeolithicTransitionValidationEmergence.csv).
They record actual/first-resolved/discovery/final population and q/E, reserves,
resident-denominator Farmer/Food-Gatherer/material-labor shares, specialists,
QoL and footprint for every row.

| case | transition habitat | P | food | q/E | QoL | moves | deaths |
|---|---|---:|---:|---:|---:|---:|---:|
| placed camp | high forest | 511 | .863 | .482/1.504 | .486 | 0 | 0 |
| productive river | productive river | 969 | .939 | .297/.550 | .469 | 0 | 0 |
| open fertile normal | nonlocal after 4 moves | 376 | 1.000 | .311/.606 | .500 | 4 | 0 |
| medium woodland normal | high forest after 1 move | 645 | 1.000 | .423/1.203 | .500 | 1 | 0 |
| high forest normal | high forest | 999 | 1.000 | .309/.599 | .500 | 0 | 0 |
| dry marginal normal | high forest after 1 move | 141 | 1.000 | .301/.573 | .556 | 1 | 0 |
| scarce Clay | productive river | 956 | .915 | .296/.549 | .471 | 0 | 37 |
| scarce Stone | productive river | 983 | .926 | .307/.590 | .000 | 0 | 13 |
| scarce Wood | productive river | 895 | 1.000 | .292/.533 | .500 | 0 | 0 |
| mobile 1.2 | nonlocal after 4 moves | 385 | 1.000 | .317/.628 | .518 | 4 | 0 |
| mobile 1.4 | nonlocal after 3 moves | 315 | 1.000 | .418/1.123 | .500 | 3 | 0 |
| mobile 1.5 | nonlocal after 3 moves | 305 | 1.000 | .415/1.109 | .503 | 3 | 0 |
| reserve .5 | productive river | 969 | .939 | .297/.550 | .469 | 0 | 0 |
| reserve 2.0 | productive river | 969 | .939 | .297/.550 | .469 | 0 | 0 |
| fixed open | open fertile | 130 | 1.000 | .227/.395 | .547 | 0 | 0 |
| fixed woodland | medium woodland/high forest | 318 | 1.000 | .410/1.150 | .500 | 0 | 0 |
| fixed dry | dry marginal | 80 | 1.000 | .204/.305 | .500 | 0 | 0 |
| fixed settlement 1.2 | open fertile; 62/52 = 1.192 | 120 | 1.000 | .233/.414 | .548 | 0 | 0 |
| fixed settlement 1.4 | open fertile; 73/52 = 1.404 | 136 | 1.000 | .238/.430 | .540 | 0 | 0 |
| fixed settlement 1.5 | open fertile; 78/52 = 1.500 | 136 | 1.000 | .240/.438 | .539 | 0 | 0 |

The normal mobile rows demonstrate the important limit of an initial-population
stress label: the live policy relocated before or during discovery, so their
actual transition support ratios were .766, .862 and .922, not their nominal
initial multipliers. The explicitly labelled fixed-settlement controls provide
the requested 1.2/1.4/1.5 measurement at the settled Agriculture boundary.
No movement/fission conclusion is available: issue #15 remains deferred and the
runner records only relocation.

The finite Clay and Stone fixtures are binding in their own accounting: their
next-turn selected-material availability is zero. Wood is a distinct controlled
local-source-absence fixture: it zeros Wood surface raster/components within the
transition footprint because simply depleting a renewable ledger would recover
during the next generation. Its zero next-turn availability verifies absence, not
a finite-source depletion. A zero selected-material debit means the material path
was blocked, not that a fixture supplied free material.

## Diagnosed constraints

The river, placed-camp, scarce-Clay and scarce-Stone rows exhaust food reserves
late in the run. This is not a missing-food ledger: each final active footprint
ends effectively empty. For
the river, the seven footprint tiles' 560 seasonal entries total
72,785.339 Food recharge/harvest, exactly the recorded Food receipt; 620 food
gatherers had nominal capacity above that receipt. Fabric manufacturing falls to
zero before the final river deficit, so it is not the acute direct cause. The
placed-camp row consumes its 6.731 starting Food plus 35,252.454 recharge and
ends effectively empty; combined farming and gathering nominal capacity exceeds
its 35,259 receipt. The Clay case likewise has nominal gathering capacity above
its fully harvested current ecological flow. Clay and Stone still visibly block their
material/QoL paths, but this trace does not contain the operating/maintenance/
expansion split needed to claim that a particular material constraint caused a
q plateau or starvation counterfactually. Those causes remain undetermined.

The .5/1/2 reserve rows converge to the same recorded final river state, but the
matrix does not claim equal transient paths. The validation records the result;
it does not retune it.

## Workforce and emergence

The live economy keeps remaining residents as food gatherers after Farmer and
material assignments. At the final river row this is 338 Farmers (34.9%), 620
food Gatherers (64.0%) and no material labor; the late deficit therefore occurs
despite high food-gatherer coverage. The final high-forest row instead has 347
Farmers (35.2%), 605.7 food Gatherers (61.4%) and 16.3 material labor (1.7%).
Fixed open and fixed dry finish Farmer-heavy (95.4% and 95.0%), with about 2.6
and 2.0 material workers respectively. The CSV preserves the full rows and
separates linked hunters from residents.

Where Craftsperson activity emerges, first useful output is Hand Tools from the
Craftsperson: turns 4–9 for the mobile/fixed/open variants, turn 7 for the river
and placed controls, and turn 13 for the mobile woodland normal. First positive
specialist assignment appears later in several fast-growth cases (river turn 9,
placed turn 10, high forest turn 13); no specialist or useful output appears in
the Stone-absence row. The observed tracks are Toolmaking and Weaving everywhere
except that the Stone row remains empty; Construction appears in several mobile,
open, dry, placed and river rows, and Pottery only in the river/reserve rows.
These are measurements of the present allocation policy, not technology quotas.

## Verification

The harness build completed with zero warnings. The full 20-case scalar,
reversed-two-worker and save-continuation replay fingerprints are identical:
`512461690A2C82585650D10899CEEC4FEB7CBB856C0E01C7F488198409812BB0`.
The matrix contains 621 resolved turns, including 500 post-Cultivation turns.
Astra's full raw-ledger audit covered 20 cases, 621 turn rows, three explicit
settlement-setup events and 5,907,695 checks: population/continuity,
manufacturing, setup provenance and exact ratios all reconciled at zero. Its
largest residuals were Food `3.129e-10`, material receipt `9.304e-10`, biomass
`1.427e-11`, water `5.957e-12`, and raw Clay `1.137e-13`. All four independently
replayed raw per-case SHA-256 hashes and all CSV fields matched exactly. Evidence
is retained in ignored `TestResults/Issue20/issue14-full-final-audit.json`.
The focused independent worker
run passed 37 Housing, 21 Wood ecology, 87 automation, 271 Neolithic, 58 tool,
45 field-tool, 1,149 planner and 12 mixed chronology assertions. Its broader
economy run additionally passed 59 milestone-two, 82 milestone-three, 25 ecology,
10 agent-contract and scalar/two/four-worker reversed replay through 12
resolutions. Astra independently rebuilt a four-case subset and passed its
scalar/parallel/save continuation; the final matrix raw ledger and matching
subset hashes are retained under ignored `TestResults/Issue20/`.
