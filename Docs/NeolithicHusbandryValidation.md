# Live husbandry validation (Issue #18)

This measurement ran the live resolver for 25 twenty-year decisions (500 years)
after Issue #17. It did not tune any production, ecology, breeding, starvation,
movement, or workforce constant. The run used the production antelope interface:
`r=0.0201`, forage `0.4`, water `0.45`, `y=1`, `eta=2`, and nutrition and
management exponents of one. Its source and run instructions are in
[`Tools/NeolithicHusbandryValidation`](../Tools/NeolithicHusbandryValidation/README.md).

## Method

The compact measurement world was generated with seed 18 at 128 × 64 and eight
players. The fixture removes rival human societies, camps, hunters and
improvements, but retains generated wild herds and ecology. It then installs one
controlled Neolithic camp with 24 residents, ten domestic antelope, one normal
starting reserve, and a fixed player breeding value sent through the ordinary
`EconomicIntent` surface. It does not use the adaptive AI breeding heuristic.

There were 1,666 eligible seven-land human footprints. The harness records their
full cheap proxy table, shortlists the 12 positive occupied-tile pasture proxies
in descending pasture order, and performs finite eight-generation human and herd
support probes. The selected pasture case was tile 4583 (herd support 87; human
support 279); its cheap proxies were 113.97 and 250.88. The separately retained
near-human-anchor stress case was tile 3016 (herd support 17; human support 54),
with proxies 23.93 and 55.05. These are finite-horizon observations with starting
stocks and real recovery/extraction ledgers, not claims of asymptotic carrying
capacity. An earlier pilot's tile 5727 is retained as a rejected pilot result and
is not used below.

Each raw row records phase-start and submitted occupations, current nutrition,
management and hydration, demographic food provenance, animal birth/death
remainders, all pressure and ecology balances, and source receipts. The harness
fails a row if human population or useful-food conservation, biomass, food,
water, animal conservation, or the domestic herd's exact one-biomass/one-water
source claims fail. Astra independently audited 25 cases / 352 rows / 172,534
raw checks: maximum residuals were food `1.2733e-11`, biomass `7.1623e-12`, and
water `6.5228e-12`.

## Live results

`P` below is resident population because no hunters are present; “Gather eq.” is
realized final food gathering divided by live gathering productivity, while
residual Gatherers remain assigned Gatherers and are not called idle labour.

| Site / b | Final P | Final animals | A:P | Turn-25 Herder assignment | Year-500 gather eq. | Run-min n / hydration | Animal births / deaths | Human births / deaths | Accepted Animal Product |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Pasture .40 | 279 | 62 | .22 | 32 | 124.98 | .340 / 1.000 | 143 / 91 | 279 / 24 | 48,683 |
| Pasture .50 | 270 | 65 | .24 | 34 | 124.97 | .298 / .939 | 181 / 126 | 269 / 23 | 43,915 |
| Pasture .60 | 265 | 70 | .26 | 36 | 124.97 | .256 / .796 | 223 / 163 | 266 / 25 | 38,919 |
| Pasture .70 | 263 | 74 | .28 | 38 | 124.97 | .246 / .778 | 262 / 198 | 269 / 30 | 38,312 |
| Pasture .80 | 265 | 81 | .31 | 42 | 124.97 | .205 / .620 | 313 / 242 | 254 / 13 | 46,212 |
| Human-anchor stress .40 | 53 | 35 | .66 | 18 | 19.69 | .532 / .676 | 70 / 45 | 34 / 5 | 29,993 |
| Human-anchor stress .50 | 53 | 39 | .74 | 20 | 19.69 | .477 / .609 | 89 / 60 | 29 / 0 | 30,339 |
| Human-anchor stress .60 | 55 | 42 | .76 | 22 | 19.69 | .433 / .554 | 107 / 75 | 31 / 0 | 30,193 |
| Human-anchor stress .70 | 53 | 47 | .89 | 23 | 19.69 | .396 / .508 | 126 / 89 | 35 / 6 | 30,107 |
| Human-anchor stress .80 | 53 | 51 | .96 | 25 | 19.69 | .365 / .468 | 145 / 104 | 35 / 6 | 30,016 |

All management fulfillment minima were one: `PlanHusbandry` reserved the needed
Herders and all remaining phase-start residents stayed assigned Gatherers. The
first table's `Run-min` values are the minimum realized herd nutrition and
hydration across all 25 decisions; they are not the sampled nutrition in the
separate year-100/300/500 table. Turn-25 Herder assignments and gathering
equivalents use the final decision's phase-start workforce. On the stress
range, Herder phase share rose from 33.33% at .40 to 47.17% at .80; realized
gathering-equivalent share rose only 36.46% to 37.16%, while the residual
gathering-equivalent fell from 30.21% to 15.67%. On the pasture range the
corresponding Herder shares were 11.19% to 15.61%, with residual equivalents
45.11% to 37.93%. A residual equivalent is unused gathering capacity in that
generation, not a validated idle worker, free craft capacity, or sustainable
surplus: every final range has herd nutrition below one and several have human
decline. On the pasture range, herd forage demand over 500 years rose from
45,088 at `b=.40` to 77,536 at `.80`, while realized forage intake was
27,633–30,017.
At the sampled year 300, herd nutrition was .641 / .256 / .212 for `b=.40` /
`.60` / `.80`; at year 500 it was .433 / .390 / .333. Hydration was one at
these sampled pasture endpoints, while the run-wide minimum hydration was
.620–1.000. These receipts show the late pasture food constraint directly.
Management remained fully covered, and turn-25 Herder assignments used 11.19%
to 15.61% of phase-start residents, so the late nutrition loss is not a
management-coverage failure. Higher `b` increased cumulative births in both
ranges, but starvation deaths also rose; the separate Issue #17 checks cover
equal-condition reproductive intent. The selected site had finite probe support
of 87 animals and 279 people, yet the one-camp live runs ended with 62–81
animals and 263–279 people. That probe is not a hard cap or promise: live forage
claims, condition and resident food demand evolve together. These results
identify ecology as the main late constraint, with breeding intensity driving
more births and starvation deaths; there is no herd movement or fission policy
to expand map access, and no abrupt labor saturation. They do not support
retuning.

## Comparison with Part II analytical envelope

The reference is the original [Part II v0.3 husbandry
analysis](https://drive.google.com/file/d/1gthS_EOSky2pd8qMUUfVivCXc9XfWzwg)
(modified 2026-09-19). It holds fresh equivalent daughter ranges after fission;
the live run holds one selected generated range, real local LandCondition and
water, no implemented fission/migration policy, and the live simultaneous
ecology/labour resolver. Its camp counts are therefore an upper-bound comparison,
not a target for this map.

| b | Part II P / A / camps at 500y | Part II Herder / free share | Live pasture P / A at 500y |
| --- | ---: | ---: | ---: |
| .40 | 664 / 552 / 4 | 44.5% / 42.5% | 279 / 62 |
| .50 | 1,663 / 1,477 / 8 | 45.7% / 42.6% | 270 / 65 |
| .60 | 3,182 / 3,669 / 16 | 57.0% / 43.0% | 265 / 70 |
| .70 | 5,179 / 9,490 / 64 | 90.5% / 9.5% | 263 / 74 |
| .80 | 6,765 / 22,154 / 128 | 100% / 0% | 265 / 81 |

The Part II labor shares are from its healthy analytical daughter-range model;
they are not measured live assignments. The live pasture cases retained much
larger non-Herder assignments because their single occupied range constrained
herd growth through food availability before Herder capacity approached the
analytical labor envelope.

The three directly comparable analytical trajectories show the same early
ordering but diverge once the selected live range is depleted:

| b / year | Part II P / A | Live pasture P / A | Live herd n at that year |
| --- | ---: | ---: | ---: |
| .40 / 100 | 75 / 22 | 74 / 22 | 1.000 |
| .40 / 300 | 157 / 112 | 303 / 104 | .641 |
| .40 / 500 | 664 / 552 | 279 / 62 | .433 |
| .60 / 100 | 75 / 33 | 74 / 33 | 1.000 |
| .60 / 300 | 322 / 361 | 290 / 117 | .256 |
| .60 / 500 | 3,182 / 3,669 | 265 / 70 | .390 |
| .80 / 100 | 75 / 50 | 74 / 49 | 1.000 |
| .80 / 300 | 685 / 1,122 | 278 / 127 | .212 |
| .80 / 500 | 6,765 / 22,154 | 265 / 81 | .333 |

For this trajectory table, `n` is `min(FoodNutrition, WaterNutrition)` on the
specific sampled turn, rounded to three decimals. It differs from the run-wide
minimum in the first table; for example, the `.60` pasture herd reached .256
food nutrition at year 300, then .390 at year 500.

The live outcome keeps substantial assigned non-Herder labour in the .40–.60
rows, but it does not reproduce the analytical healthy operating envelope after
the local ecology becomes limiting. The traces expose the actual current
nutrition, condition and source receipts needed for later calibration; this
validation proposes no retuning.

The near-human-anchor stress site's low direct herd support caused substantial
animal starvation at every tested value. On the pasture site, the occupied
forage tile (4583) had `B=0`, `G=0`, and `LandCondition=.14` at year 500 in all
five breeding cases; in the final decision its realized herd biomass intake was
885–887 against a request of 2,048–2,656. Food and water claims are fully
serialized, and the report does not attribute this depletion to wild competition
without an overlapping claim. Initial and recovery overflow are retained in the
balances rather than being labelled waste.

## Fixed controls and chronology

A disclosed two-generation fixed-forage fixture used P=200, no human growth or
gathering, full Herders (`ceil(A/2)`), ample fixed water, and one common forage
capacity/recovery curve installed once. It is a finite ecological response
control, not a carrying-capacity estimate. At 100 / 200 / 400 animals the
realized forage and Animal Product were 6,400 / 12,401.87 / 3,641.81 and 16,000
/ 31,004.67 / 9,104.53. The 400-animal case reached minimum nutrition .05 even
with full herder coverage and ample water. It produced less over this 40-year,
source-bound fixture; this finite result does not by itself establish an
infinite-horizon sustainable yield.

For healthy b=0, twenty years resolved as one 20-year, two 10-year, or four
5-year decisions with exactly equal cumulative potential/accepted Animal Product
(8,000), forage (3,200), and water (3,600). At b=.60 all partitions ended at
127 animals plus identical fractional remainder `.2775565`; output differed
(8,000 / 8,480 / 8,740) because newborns correctly do not consume or produce
retroactively. In the zero-stock scarce fixture, 20 / 10 / 5 year partitions
ended with animal deficit turns 1 / 2 / 4 and animals 100 / 100 / 91. This is
the configured decision-count starvation grace and duration-scaled fractional
death behavior, not a modified starvation law.

## Implemented movement, fission and slaughter signals

All scenarios submit Economy orders only at a fixed occupied tile. They issue no
slaughter or movement orders, and the current implementation has no managed-herd
fission or policy pressure signal to measure; reported slaughter is zero and
moves are zero. This validates neither a future migration/fission policy nor a
claim that husbandry cannot move. Those controls remain deferred under Issue
#15.

## Determinism

The complete outputs from an independent same-source build were byte-identical.
The b=.60 pasture scalar, reversed two-worker, and save-continuation traces
match after scenario-ID normalization (SHA-256
`4B9922CAA9A04802556D16A4644BDFDA0BA6E556B0889AB27B46FF56B1ABF17D`),
with final fingerprint
`A6FBCF44F32D146F5629C9316A92B6522E32FB65D0A8D87ECFACF94BF9C9B184`.

The complete raw JSONL and compact CSV tables remain under ignored
`TestResults/Issue20/Issue18-Final-20260923-0700/`; they are regenerated from
the committed harness and do not form source artifacts.
