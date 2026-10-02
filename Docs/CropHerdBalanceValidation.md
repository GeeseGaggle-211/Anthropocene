# Crop moisture, agricultural planning, and herd balance

Implemented and measured on `features/herd-ecology-retention`, 1 October 2026.
This package preserves the prior biomass-first ecology and finite-horizon herd
valuation. It implements the user-authorized crop response and planner fixes
from [AgricultureMoisturePlannerAudit.md](AgricultureMoisturePlannerAudit.md).

The crop correction removes a substantial dry-land agricultural advantage,
but **the generated economies still do not behave as pastoral societies**.
At turn 50, husbandry-primary tribes hold a median of five domestic animal
units and obtain about 1.1% of current food receipts from sustained animal
production. Most cultivate fields. Performance has a small measured regression
at 25 turns; the lower total time at 50 turns chiefly reflects fewer tribes.

## Implemented rules and source trace

1. **Crop suitability uses local soil water.**
   `EcologyMath.CropSuitability` computes
   `s = min(1, (Moisture / CropMoistureReference)^CropMoistureExponent)`.
   New-world defaults and `OpeningConfig.json` use reference `.50`, exponent
   `2`. Effective food capacity is
   `retainedWild + (Biomass - retainedWild) * q² * s`; managed renewal is
   `.65 * q * Biomass * biomassDailyRate * operation * s`.
   `EcologyCapacity`, field-stock reclassification, and compatibility
   observations pass actual seasonal soil moisture to the shared helpers.
   Biomass, natural wild food, grazing accessibility, and aquatic production
   receive no additional crop penalty. Moisture at or above `.50` preserves
   crop capacity and renewal; dry river valleys benefit from local hydrology
   even when their regional climate remains desert.

2. **A field no longer gives every wild-food worker the full farmer bonus.**
   `CultivationEconomy.HarvestProductivity` infers each field's crop share from
   the shared capacity, weights it by current finite accessible stock, and
   computes `effectiveFactor = totalFood / totalWork`, where
   `work = wildFood + cropFood / FarmerProductivity`. This is the harmonic
   blend of the base gathering rate and the crop harvesting rate. Food remains
   one conserved physical pool; no new crop inventory is introduced. Foreign
   operators' fields give no crop-work bonus to the querying camp.
   `CampProductionDemand` caches the factor. `EconomicPlanner`,
   `TurnResolver.AddAgriculturalClaims`, food forecasts, and harvest-work
   diagnostics use the same factor. It is calculated from opening stocks;
   fields not yet built do not grant immediate harvesting productivity.
   Fertile **ecological capacity/renewal** benchmarks are preserved, while
   realized worker-limited receipts deliberately change with this correction.

3. **Maintenance uses real tool coverage.**
   `CultivationEconomy.PlannedMaintenanceFarmers` uses
   `MaintenanceFarmersForPlanning`, matching the starting-stock tool budget.
   Zero Farm Tools no longer earn the full `1.265625` field-work efficiency.
   Tools made later still cannot backdate their benefit.

4. **Animal food credits inventory once and frees only removable harvest work.**
   `HerdLabour.Build` uses `d.Biological` directly for the current incoming
   demand, since `PlanningFoodDemand` already credits inventory. Its current
   and future harvest savings respect the existing maintenance floor. Milk
   cannot claim to release workers whose field upkeep remains mandatory.
   The retained finite-horizon portfolio still compares current slaughter,
   current sustained yield, continuation, terminal salvage, management,
   craft work, and real replacement costs at the shared labour price.

5. **Fission cannot spend unbuilt daughter fields.**
   Under the new policy, `Fission.SiteFood` clears hypothetical destination
   field capital and clamps accessible stock to natural capacity before its
   repeatable-food estimate. Daughter support is bounded by obtainable wild/
   aquatic harvest and labour, without the old primitive-`q=.4` managed-flow
   credit. Camps with herds also pass forage and water gates, including
   agricultural camps that hold secondary Husbandry. Opening forage stock
   pays herd needs after human land harvest; aquatic harvest does not debit
   forage. Opening water plus recharge pays herd water. Destination ordering
   retains uncapped physical richness as its second key.

`CropHarvestingOnly=true` selects the corrected harvesting/fission policy.
The new settings are optional saved fields. Missing fields load reference
zero, exponent two, and policy false, preserving the old crop response and
flat harvest/fission rules. Actual tool coverage and arithmetic corrections
apply to both policies. Save geography/capacities are never regenerated.
Herd labour, fission, and QoL-mortality defaults remain off; the measurements
below explicitly enable them.

Authoritative state remains saved field capital/operation, finite tile stocks,
seasonal profiles, herd counts, and inventory. The harvest factor and portfolio
are transient pure calculations. The new factor walks at most seven local
fields per calculation; suitability is constant-time per profile. Fission
uses the existing bounded local candidate search. No year-by-year loop,
population cap, new dense map state, or global scan is added. The existing
herd portfolio still scales with candidate herder counts and owned herd
records; its mixed-species liquidation cost remains a mature-game/RL concern.

## Controlled ecology and labour evidence

The six-site probe uses warm lowland tiles, fixed local moisture, 404 residents,
disclosed drinking-water controls, and otherwise identical resources and
herds. These are controlled fixtures, not generated habitat averages.
At `q=.4`, total biomass is identical between variants:

| Site / soil moisture | Biomass per tile | Before food capacity | Corrected capacity | Before managed food/year | Corrected managed food/year |
| --- | ---: | ---: | ---: | ---: | ---: |
| Dry desert, .03 | 1,595.7 | 288.8 | 40.8 | 11.491 | .041 |
| Less arid desert, .10 | 5,319.0 | 962.7 | 166.2 | 50.012 | 2.000 |
| Grassland, .22 | 11,701.9 | 2,363.8 | 929.4 | 154.316 | 29.876 |
| Fertile tropical, .75 | 39,892.8 | 10,571.6 | 10,571.6 | 1,199.599 | 1,199.599 |
| Desert river, .85 local / .03 regional | 45,211.8 | 11,981.1 | 11,981.1 | 1,504.970 | 1,504.970 |

With 200 starting animals, the grassland fixture retains 249 grown animal
units rather than 222. Wet tropical and river fixtures still retain only
eight: the combined corrections do not erase agriculture's advantage on
fertile land. Dry desert without drinking water retains only the two-animal
seed; suppressing crops cannot substitute for water.

With `q=.000001`, 22 starting animals, and ample wet-site food, 100 food
workers now obtain approximately 720 food/year rather than 1,080. The tiny
field has essentially no crop share, so its bonus tends continuously to one.
Both wet fixtures retain 26 grown animals instead of 18/tropical and
eight/river in this controlled comparison. This is evidence that the shortcut
affected the herd tradeoff, not evidence that generated pastoral balance is solved.

## Five-world populations, QoL, and domestic animals

Each variant freshly generates Standard `1024×512` worlds with 80 original
tribes: Continents seeds 1, 2, 3; Pangaea seed 1; Archipelagos seed 1.
Both use production config, herd labour on, fission on, QoL mortality on
with elasticity **1**, one scalar worker, and twenty-year turns. Immigration
remains off. Each reaches opening completion normally and then runs 50 more
turns; the censuses are at **25/50 post-opening turns** (500/1,000 years).
Generation is seed matched, but the crop package can change the opening,
so the generated outcome comparison is not a matched post-opening state.

Agriculture/Husbandry below mean **primary first-breakthrough technology**,
not exclusive subsistence. Fission descendants retain that classification.
Population includes residents and all camp-linked hunters; QoL is weighted
by current resident population using committed camp QoL. QoL describes the
last resolved cohort; new daughters have their founding estimate until the
next resolution. Watercraft-primary tribes form the remaining population.

Pooled totals across the five worlds:

| Post-opening turns | Variant | Agricultural tribes | Agricultural population | Ag. QoL | Husbandry tribes | Husbandry population | H. QoL | H. domestic animal units |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 25 | Before this package | 328 | 115,294 | .832 | 252 | 89,861 | .898 | 1,059 |
| 25 | Corrected | 296 | 88,701 | .855 | 176 | 56,297 | .890 | 733 |
| 50 | Before this package | 973 | 444,545 | .810 | 1,820 | 777,948 | .828 | 10,132 |
| 50 | Corrected | 730 | 309,902 | .813 | 1,136 | 467,833 | .843 | 5,919 |

At turn 50, agricultural population falls 30.3% and husbandry population
falls 39.9%. Husbandry QoL rises .0146, but the animal/population ratio falls
slightly from .0130 to .0127. The corrected ratio is **one animal unit per
79 linked people**. These are the model's aggregate domestic-animal units;
each fully managed unit supplies roughly one person's annual meals. They
must not be relabelled as a realistic number of individual livestock heads.

### Camp and settlement population distributions

These are **unweighted distributions across individual live camps**, pooled
across the five worlds; each camp contributes one observation. Counts below
use resident `Population`, excluding linked hunters. The preceding total
population table includes linked hunters. P25/P50/P75 use linear
interpolation at sorted-array index `(n - 1) * percentile`, so fractional
quartiles and medians are expected. These are not distributions of the five
world totals or population-weighted distributions.

All camps/settlements, classified by primary technology:

| Turns | Variant | Primary technology | Camps | P25 residents | Median residents | P75 residents |
| --- | --- | --- | ---: | ---: | ---: | ---: |
| 25 | Before this package | Agriculture | 328 | 203 | 327.5 | 489.5 |
| 25 | Corrected | Agriculture | 296 | 120.5 | 276.5 | 447.25 |
| 25 | Before this package | Husbandry | 252 | 231.5 | 343.5 | 451.75 |
| 25 | Corrected | Husbandry | 176 | 167.5 | 303 | 453.75 |
| 50 | Before this package | Agriculture | 973 | 290 | 452 | 594 |
| 50 | Corrected | Agriculture | 730 | 269.25 | 415.5 | 568.75 |
| 50 | Before this package | Husbandry | 1,820 | 252 | 412 | 602 |
| 50 | Corrected | Husbandry | 1,136 | 247.75 | 404 | 553.25 |

Villages only (`IsVillage=true`), retaining the same primary-technology
classification. The local footprint-intensity threshold determines village
status; as the [planner follow-up](PastoralPlannerAudit.md) shows, a mobile
camp can still operate intensive fields on part of its footprint.

| Turns | Variant | Primary technology | Villages | P25 residents | Median residents | P75 residents |
| --- | --- | --- | ---: | ---: | ---: | ---: |
| 25 | Before this package | Agriculture | 141 | 306 | 437 | 550 |
| 25 | Corrected | Agriculture | 66 | 327.25 | 396 | 539.75 |
| 25 | Before this package | Husbandry | 56 | 406.25 | 492 | 572.5 |
| 25 | Corrected | Husbandry | 20 | 433.25 | 532 | 596.5 |
| 50 | Before this package | Agriculture | 437 | 490 | 576 | 699 |
| 50 | Corrected | Agriculture | 270 | 435.25 | 550.5 | 669 |
| 50 | Before this package | Husbandry | 582 | 460.5 | 641 | 714 |
| 50 | Corrected | Husbandry | 263 | 517 | 652 | 720 |

The generated `TestResults/CropHerdBalance/settlement-populations.csv` and
`summary.json` include resident and linked-population quartiles for all
camps, villages, and mobile camps. Existing `medianTribePopulation` fields
remain explicitly linked-population statistics; the new
`medianResidentPopulation` fields provide resident-only values. At corrected
turn 50, the all-camp median is similar for the two routes (415.5 agriculture,
404 husbandry), while husbandry villages are larger (652 vs 550.5). The
primary-technology label still does not establish pastoral subsistence.

Corrected outcomes by world:

| World | Turns | Ag. population | Ag. QoL | H. population | H. QoL | H. animals |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Continents 1 | 25 | 18,411 | .861 | 11,775 | .902 | 144 |
| Continents 2 | 25 | 20,019 | .855 | 7,800 | .894 | 122 |
| Continents 3 | 25 | 17,930 | .865 | 11,192 | .858 | 128 |
| Pangaea 1 | 25 | 17,341 | .857 | 12,639 | .894 | 159 |
| Archipelagos 1 | 25 | 15,000 | .837 | 12,891 | .903 | 180 |
| Continents 1 | 50 | 68,398 | .814 | 84,531 | .845 | 1,027 |
| Continents 2 | 50 | 64,638 | .812 | 86,480 | .859 | 1,095 |
| Continents 3 | 50 | 64,634 | .819 | 112,500 | .845 | 1,288 |
| Pangaea 1 | 50 | 57,094 | .817 | 88,779 | .842 | 1,231 |
| Archipelagos 1 | 50 | 55,138 | .802 | 95,543 | .823 | 1,278 |

The husbandry label conceals considerable cultivation at corrected turn 50:

| Husbandry-primary subset | Tribes | Linked population | Resident-weighted QoL | Domestic animals |
| --- | ---: | ---: | ---: | ---: |
| All | 1,136 | 467,833 | .843 | 5,919 |
| With fields | 1,069 | 456,890 | .841 | 5,812 |
| Without fields | 67 | 10,943 | .908 | 107 |
| Villages | 263 | 162,250 | .783 | 2,086 |
| Mobile camps | 873 | 305,583 | .874 | 3,833 |
| Herd owners | 1,072 | 448,354 | .843 | 5,919 |
| Herdless camps | 64 | 19,479 | .825 | 0 |

Only two husbandry-primary camps lack Cultivation technology; together they
contain 111 linked people and four animals. Mobile status is not equivalent
to absence of fields. The endpoint assignments contain 287,260 Farmers and
2,823 Herders across husbandry-primary tribes. Sustained animal food is
1.071% of their current food receipts, versus 1.101% before this package.
This is a **receipt share**, not a measured consumption share: typed feedstock,
reserves, and growth also use food.

Both audits include live fission and mortality. Across the 50 post-opening
turns, corrected worlds record 1,534 fissions, 397 human deaths, 19 animal
deaths and 1,948 slaughtered animal units; the control records 2,470, 467,
10 and 2,923. Human deaths here are recorded attrition/starvation. The current
QoL-mortality implementation reduces the net growth ceiling rather than
recording those implied deaths or making a low-QoL camp decline. Enabling it
must not be described as a full vital-statistics mortality simulation.

## Matched 25/50-turn performance

The performance experiment loads **identical pre-package C1 opening-save
bytes** into both builds. The corrected build explicitly sets the three new
crop settings after load, so legacy migration cannot silently disable them.
Each duration has three sequential AB/BA/AB pairs, one untimed warm-up,
one worker, partition size 64, and metrics/details/fingerprints disabled.
No other simulation jobs run during these timings. Generation, save loading,
warm-up, output diagnostics and JSON serialization are excluded.
`totalMs` includes AI + Prepare + Execute; allocations span that same work.

| Horizon | Median before | Median corrected | Median paired runtime change | Allocation change | Time per tribe-turn change |
| --- | ---: | ---: | ---: | ---: | ---: |
| 25 turns | 6.896 s | 7.014 s | **+3.17%** | +.59% | +6.08% |
| 50 turns | 21.119 s | 17.100 s | **−17.25%** | −13.90% | +4.72% |

The percentage is the median of each pair's ratio, not the ratio of the two
absolute medians. Runtime changes range +1.72% to +3.53% at 25 turns and
−19.55% to −15.46% at 50. Three pairs establish the observed regression
range; they do not support a formal significance claim.

| Phase / memory metric | 25-turn paired change | 50-turn paired change |
| --- | ---: | ---: |
| AI | +6.07% | −10.65% |
| Prepare | +3.92% | −19.63% |
| Execute | −.81% | −15.34% |
| Allocated bytes, median before → corrected | 3.200 → 3.219 GB | 10.326 → 8.890 GB |
| Peak sampled managed heap, median before → corrected | 588.39 → 588.35 MB | 764.53 → 762.92 MB |

At 25 turns, workload is 2,154 vs 2,095 tribe-turns and the final census is
113 vs 97 tribes. At 50, workload is 9,275 vs 7,329 and final tribes are
541 vs 384. The 50-turn total speedup therefore includes a large reduction
in active settlements. Time per tribe-turn is a useful warning about overhead,
not an isolated same-state microbenchmark: the worlds differ in fields,
herds, fission candidates, and active tiles. Metrics were disabled, so tile
kernel work counts are not available from these timing runs.

The local scale probe additionally tests 100/1,000/10,000 residents and
1/8/64 mixed-species herd records with identical frozen fixtures. Median
planner time over three batches (five calls per warmed case):

| Residents / herd records | Before planner | Corrected planner | Before allocation | Corrected allocation |
| --- | ---: | ---: | ---: | ---: |
| 100 / 1 | .981 ms | .966 ms | 235,972 B | 236,020 B |
| 1,000 / 8 | 3.032 ms | 2.814 ms | 651,396 B | 651,444 B |
| 10,000 / 64 | 21.491 ms | 20.536 ms | 4,657,620 B | 4,657,668 B |

This fixture has no fields and ample inventory. It checks bounded scaling
and the new herd arithmetic; it does not isolate the seven-field harvesting
query seen in the generated economy. Large mixed-herd planning still costs
about 20 ms/camp and several MB per call, which remains a limitation for
large empires and RL throughput. The new arithmetic adds only 48 B per Plan
in these fixtures; timing noise prevents treating their small speedups as
a demonstrated optimization.

## Verification, provenance, and reproduction

Release build and the complete fixed headless suite pass: 104 crop,
627 fission, 1,687 herd-labour, 4,724 cultivation-cache differential and
4,366 active/passive ecology checks, plus the remaining economy, generation,
ownership, conservation and replay suites. Replay covers scalar versus 2/4
workers, reversed partitions/entity collections, and 25 post-opening turns.
Save checks cover new setting round trips and metadata with all three fields
absent. Controlled fixtures that relied on unstated moisture or the old flat
bonus now specify wet soil or an explicit food budget while retaining their
conservation, allocation and chronology assertions. Unity is unavailable in
this cloud workspace; engine/presentation tests were not run.

Source base: `ddbecbfeb3df9a393135cc79f976285205809826`, with preserved
uncommitted biomass-first/finite-horizon work. The **control is that preserved
candidate, not the Git base and not the earlier inverse-biomass implementation**.
SHA-256 digests of sorted, compact JSON maps of all 61 simulation source hashes:

| Build | Source-map digest |
| --- | --- |
| Pre-moisture control | `933c4f23418a6d5ea36209570969acdbe211b39b984a0d0fcd6dc2332e7223b3` |
| Corrected | `2232bfce6b4fa6194be0d748a93fdf4979525efe4dc9e8227c998db2cdfbe25a` |

Matched checkpoint SHA-256:
`DC9AD43DB66FE619C39920B194FED569F5A514BFDBFA4B995AAE65BB45709142`.
Environment: Debian 13 x64, .NET SDK 8.0.425, runtime 8.0.31.
Per-run JSON records its source label, config hash, dimensions, switches,
checkpoint hash, runtime, and measurement modes. Repeated timings have
identical endpoint population/tribe counts and workload within each variant.

```bash
source /workspace/anthropocene-cloud/activate.sh
dotnet build Tools/Headless/Anthropocene.Headless.csproj -c Release
dotnet Tools/Headless/bin/Release/net8.0/Anthropocene.Headless.dll 0 --jobs=2 --check-timings
python3 Tools/PerformanceAudit/Run-CropHerdBalanceAudit.py --phase build
python3 Tools/PerformanceAudit/Run-CropHerdBalanceAudit.py --phase gameplay --jobs 2
# Run only after gameplay/builds/checks finish; timing pairs are sequential.
python3 Tools/PerformanceAudit/Run-CropHerdBalanceAudit.py --phase performance --pairs 3
python3 Tools/PerformanceAudit/Summarize-CropHerdBalance.py
python3 Tools/Docs/Check-CurrentContract.py
```

Outputs are under ignored `TestResults/CropHerdBalance`: raw gameplay and
performance JSON/logs, opening saves, source hashes, pooled/by-world CSVs,
husbandry subsets, demography, trajectories, paired timings, and scale/site
JSONL. The tracked `Tools/PerformanceAudit/Baselines` reverse patch reconstructs
the control on a fresh checkout with GNU `patch`; the runner verifies all
source hashes before and after applying it and refuses a fabricated baseline.
If later simulation source changes, restore the preserved exact control
snapshot instead of silently relabelling current code as the original control.
`Run-MoistureCurveAudit.py` remains a historical isolated-preview reproducer
and accepts `--source` pointing to that pre-package snapshot.

## Remaining decisions

The user's subsequent request to ascertain the remaining planner bias is
answered in [PastoralPlannerAudit.md](PastoralPlannerAudit.md). The matched
corrected-C1 follow-up confirms spare crop expansion outside the priced
claim, inconsistent protected-inventory credits in crop/herd valuation, and
herd choices conditioned on maintained fields. It introduces no production
changes, so the census and timing measurements above remain applicable.

Retain the corrected crop mechanism and the confirmed arithmetic fixes: the
controlled dry/wet response and shared work-rate contracts pass, and the
prior tiny-field subsidy is removed. Do not interpret the increase in QoL or
the large husbandry-primary population as success on pastoral subsistence.
Generated herds still contribute only about 1% of food receipts.

The remaining planner investigation should compare marginal field investment
against herd expansion on generated dry/grassland sites, including the actual
worker costs and finite starting herd. Existing maintenance remains a prior
commitment; the planner has no priced alternative to reduce upkeep and
abandon fields. Fission support also omits dairy, and animal acquisition
remains the separate #12 work. Those are explicit remaining limits, not
newly implemented features or a basis for silently boosting animal yield.
Five world seeds and these two horizons do not establish a universal climate
balance or justify enabling the social-economy levers by default.
