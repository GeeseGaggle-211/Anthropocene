# Shared crop and herd allocation correction

Implemented on `features/herd-ecology-retention`, 2 October 2026, following
[PastoralPlannerCorrectionDesign.md](PastoralPlannerCorrectionDesign.md).
The corrected ecology/crop source at `03d1ed1` is the control.
`SharedCropHerdAllocationEnabled` defaults to **false**, including saves
without the optional field. The enabled Value planner uses the correction;
animal units, yields, rates, R=16, crop moisture response, harvest-first
resolution, fission policy and QoL-mortality equations keep their existing values.

## Mechanics and authoritative state

`FoodIncomeLedger` credits opening inventory through `FoodBucketTargets` once.
Plant, aquatic and animal food share meals, protected reserve refill and growth
buckets. Typed clothing/tent inputs are earmarked once with their recipe
provenance, clothing before reserve; opening feedstock is distinguished from
incoming feedstock when charging harvest work. Pots provide storage capacity.
Slaughter is a one-time current receipt; future field gaps use the retained
herd's discounted recurring yield, and liquidated animals earn no later yield.

`SharedAllocation` is a transient local portfolio service inside the existing
`EconomicPlanner`. It compares field operation at q/tool/staffing breakpoints
and a sparse retain/grow/slaughter herd ladder. Current spare labour gets
price zero, while future work keeps the labour price selected by the craft
search. Field expansion has to buy a positive valued ladder prefix; the
unpriced final spare-field fallback is disabled. Whole expansion workers are
rounded down to the purchased work budget, so rounding cannot add unpriced work. Incremental field upkeep and
harvesting are charged by their maximum because those tasks overlap. Future
covered-tool replacement includes manufacturing and material extraction work.
Milk savings are capped by the recurring human harvest that this baseline
actually staffs; unstaffed future growth work cannot become a saving.
The returned intent still passes the existing craft, food, material, tool and
resident feasibility calculations.

Explicit expansion work is reserved before upkeep in `CalculateLabor` under
the new flag. This makes a plan that reduces field operation and builds a
local alternative reach the resolver without first spending build work on
abandoned upkeep. The existing kernel applies the selected operating fraction
and analytical capital decay; no forecast mutates the world. Field capital,
food/inventories, herd counts and workforce intents remain authoritative.
Resolution still commits canonical one-turn results for the prior cohort.

The future field baseline uses the local ecology kernel's finite receipts and
endpoint stocks. A conservative repeatable refill starts with an empty food
pool, retained non-food biomass and horizon-mean field capital. Terminal
capital bounds the field upkeep estimate. Future herd support is bounded by
this local biomass after human harvest and the existing local forage/water
ceiling. Unsupported retained animals do not earn repeating production or
terminal salvage. The continuation assumes unrestricted breeding only for
the full feasible grown-herd option; held/cut options price zero further
reproduction. Automatic preparation recomputes the existing breeding
recommendation each turn before applying the selected target cap.

Primary technology does not choose a planner or add a quota. The old
husbandry-primary settling penalty is bypassed in this mode; continuation
uses the same radius-one physical support ceiling for all capable camps.
Acquiring new animals remains #12, and excess-herd fission remains HV4.

## Computation and limits

Per camp, work follows the local footprint, at most 34 operation breakpoints,
at most 37 herd staffing choices per operation, existing field bands and
owned herd groups. Population changes the numeric breakpoints, not a loop
bound. Physical field forecasts are cached once per operation; expansion
benefit/work coefficients once per feasible herd choice. Repeated labour-price
queries visit those totals; they do not rerun the ecology kernel or the craft
planner for each herd choice. There is no year-by-year production forecast,
new dense map state or full population × field × herd × craft search.
A per-plan `PlanningKernelCache` (below, *Planner cost*) shares opening
projections, operation-independent tile forecasts, marginal field investments
and pasture scans across the operation choices without changing any value.

This is a bounded local valuation approximation, not a proof of a global
optimum. The future refill uses horizon-mean capital; future upkeep uses the
mean of opening and terminal effort; aquatic continuation holds the current
local envelope, and future harvesting uses the opening crop/wild mix. Exact
rolling future crop-mix and labour-price trajectories are not validated. Herd and field continuation retain the current cohort's needs
rather than forecasting a century of exponential population growth. These
approximations are part of valuation only. The authoritative turn continues
to use the existing ecology, labour and demographic equations.

## Validation and reproduction

The focused `SharedAllocationChecks` cover common food accounting at inventory
levels below/above protected reserves, typed provenance, profitable herd versus
crop choices, rejection of useless/free-current-cost expansion, q-dependent
uncapped upkeep, resolver operation/build separation and endpoint decay,
water constraints, seed floors, resident budgets, liquidation, label independence,
purity and save migration. A controlled generated pastoral start runs 34 turns
with fission and QoL mortality active and compares scalar execution with reversed
four-worker execution and a mid-run save/load. The fixed headless suite also
checks the existing control paths.

Unity is unavailable in this managed environment. These are .NET 8 / C# 9
simulation checks; the Unity runtime UI/build has not been run.

```bash
source /workspace/anthropocene-cloud/activate.sh
dotnet build Tools/Headless/Anthropocene.Headless.csproj -c Release
dotnet Tools/Headless/bin/Release/net8.0/Anthropocene.Headless.dll 0 --jobs=2 --check-timings
python Tools/Docs/Check-CurrentContract.py
python Tools/PerformanceAudit/Run-SharedAllocationAudit.py --phase=all --jobs=2 --pairs=3
python Tools/PerformanceAudit/Summarize-SharedAllocation.py
```

The runner reconstructs the control from `git show 03d1ed1`, verifies every
source against the tracked corrected-source hash recipe, snapshots candidate
sources and records both digests. It generates five Standard/80 control
openings: Continents seeds 1/2/3, Pangaea seed 1 and Archipelagos seed 1.
Candidate runs load those exact saved bytes; fission, herd labour and
QoL mortality are active with elasticity 1. The only candidate configuration
change is the shared-allocation flag. Each run continues 50 post-opening turns;
turn 25 is taken from that same trajectory. Results under `TestResults/` are
ignored; the scripts and tables below retain the reproduction method/evidence.

Performance uses the identical C1 opening save, one worker and three sequential
AB/BA pairs at each horizon, after gameplay/build/check processes finish.
Warmup is one existing small-world run per process. Timings exclude audit
serialization, details, metrics and fingerprints. Phase time, total allocation,
peak managed heap and cost per camp-turn are reported separately. The five-world
census and the single-world performance cohort must not be conflated.

<!-- SHARED-ALLOCATION-RESULTS -->

## Results and acceptance status

The corrected portfolio raises herd retention and population substantially, but **runtime acceptance remains open**. Total matched-save time rises **33.5% at 25 turns** and **138.6% at 50 turns**; cost per camp-turn rises **26.9% / 71.5%**. This exceeds the earlier HV3 +5% timing criterion. The flag stays off by default. Animal-unit grounding and HV3 range approval remain user decisions before calibration; this work does not change those rates or declare the earlier HV3 balance/QoL gates satisfied.

All triples below are unweighted **P25 / median / P75**, using sorted linear interpolation. A tribe is one living camp and its linked hunters; daughters count as their own tribes. The primary technology groups are reporting labels. Resident counts exclude linked hunters. Animals are abstract managed animal units, including zero-herd camps, not zoological head counts. QoL is unweighted across camps; the CSV also preserves resident-weighted QoL.

### Five-world population, herd and QoL census

| Post-opening turns | Variant | Primary technology | Tribes | Residents | Linked people | Managed animals | QoL |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 25 | control | AnimalHusbandry | 176 | 167.5/303/453.8 | 184.8/312/468.5 | 2/4/6 | 0.847/0.916/0.960 |
| 25 | candidate | AnimalHusbandry | 299 | 195.5/319/436 | 198/324/448 | 7/18/38.5 | 0.872/0.911/0.954 |
| 25 | control | Cultivation | 296 | 120.5/276.5/447.2 | 125/285.5/451 | 0/0/0 | 0.792/0.909/0.978 |
| 25 | candidate | Cultivation | 300 | 128.8/296/478.2 | 138.5/310.5/480.2 | 0/0/0 | 0.731/0.881/0.968 |
| 25 | control | Watercraft | 69 | 112/202/293 | 142/210/331 | 0/0/0 | 0.750/0.898/0.972 |
| 25 | candidate | Watercraft | 69 | 102/206/352 | 136/220/358 | 0/0/0 | 0.771/0.878/0.959 |
| 50 | control | AnimalHusbandry | 1136 | 247.8/404/553.2 | 247.8/404.5/559 | 2/5/7 | 0.810/0.897/0.943 |
| 50 | candidate | AnimalHusbandry | 2307 | 271/444/668 | 271/444/668 | 37/118/250.5 | 0.732/0.876/0.938 |
| 50 | control | Cultivation | 730 | 269.2/415.5/568.8 | 271.2/421/573.8 | 0/0/0 | 0.746/0.854/0.927 |
| 50 | candidate | Cultivation | 665 | 277/445/609 | 280/452/617 | 0/0/0 | 0.727/0.839/0.915 |
| 50 | control | Watercraft | 87 | 240.5/416/570.5 | 264.5/421/589 | 0/0/0 | 0.684/0.839/0.943 |
| 50 | candidate | Watercraft | 89 | 228/389/562 | 236/389/599 | 0/0/0 | 0.716/0.820/0.917 |

| Turns | Variant | Tribes | Linked population | Resident population | Managed animals |
| --- | --- | --- | --- | --- | --- |
| 25 | control | 541 | 162,504 | 155,093 | 733 |
| 25 | candidate | 668 | 211,876 | 204,480 | 8,914 |
| 50 | control | 1,953 | 815,784 | 808,582 | 5,919 |
| 50 | candidate | 3,061 | 1,381,126 | 1,373,931 | 405,175 |

At turn 50 the pooled linked population grows from 815,784 to 1,381,126 (+69.3%). Husbandry-primary median animals grow from 5 to 118. Sustained animal production supplies **1.1% → 3.6%** of husbandry food receipts at turn 25 and **1.1% → 11.7%** at turn 50. Husbandry median QoL changes **0.916 → 0.911** and **0.897 → 0.876** respectively. Larger herds do not establish calibrated pastoral balance; crops and wild/aquatic food still supply most receipts.

### Villages separated from all camps

The census above covers all camps. This table isolates villages, with resident population, animals and QoL on the same quantile convention. `settlement-populations.csv` additionally includes mobile camps and linked population.

| Turns | Variant | Primary technology | Villages | Residents | Managed animals | QoL |
| --- | --- | --- | --- | --- | --- | --- |
| 25 | control | AnimalHusbandry | 20 | 433.2/532/596.5 | 6/7/8 | 0.857/0.894/0.912 |
| 25 | candidate | AnimalHusbandry | 77 | 427/488/537 | 18/37/77 | 0.849/0.886/0.905 |
| 25 | control | Cultivation | 66 | 327.2/396/539.8 | 0/0/0 | 0.851/0.917/0.961 |
| 25 | candidate | Cultivation | 162 | 321.5/433/555 | 0/0/0 | 0.743/0.853/0.926 |
| 25 | control | Watercraft | 10 | 225.5/335.5/676.5 | 0/0/0 | 0.773/0.931/0.968 |
| 25 | candidate | Watercraft | 18 | 311.5/388.5/513 | 0/0/0 | 0.801/0.876/0.948 |
| 50 | control | AnimalHusbandry | 263 | 517/652/720 | 7/9/10 | 0.727/0.801/0.874 |
| 50 | candidate | AnimalHusbandry | 1034 | 563/676/718 | 85/212.5/416.8 | 0.694/0.732/0.836 |
| 50 | control | Cultivation | 270 | 435.2/550.5/669 | 0/0/0 | 0.768/0.875/0.926 |
| 50 | candidate | Cultivation | 406 | 464/569/670 | 0/0/0 | 0.729/0.842/0.908 |
| 50 | control | Watercraft | 53 | 356/541/701 | 0/0/0 | 0.718/0.871/0.961 |
| 50 | candidate | Watercraft | 60 | 343.8/512/667 | 0/0/0 | 0.730/0.834/0.908 |

At turn 50 corrected husbandry villages have median 676 residents and QoL 0.732, while their mobile camps have median 299 residents and QoL 0.928. That separation is material to later calibration; the pooled QoL should not hide it.

### Field intensity, work, food and breeding

| Turns | Variant | Primary technology | Resident-weighted local mean q | Farmers | Herders | Sustained animal food share | Zero breeding / targeted camps |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 25 | control | AnimalHusbandry | 0.131 | 35,628 | 377 | 1.07% | 57/161 |
| 25 | candidate | AnimalHusbandry | 0.175 | 6,538 | 4,135 | 3.60% | 26/262 |
| 25 | control | Cultivation | 0.182 | 66,745 | 0 | 0.00% | 0/0 |
| 25 | candidate | Cultivation | 0.251 | 24,852 | 0 | 0.00% | 0/0 |
| 25 | control | Watercraft | 0.162 | 11,743 | 0 | 0.00% | 0/0 |
| 25 | candidate | Watercraft | 0.229 | 4,240 | 0 | 0.00% | 0/0 |
| 50 | control | AnimalHusbandry | 0.182 | 287,260 | 2,823 | 1.07% | 326/1013 |
| 50 | candidate | AnimalHusbandry | 0.213 | 65,904 | 166,343 | 11.69% | 442/2168 |
| 50 | control | Cultivation | 0.223 | 201,519 | 0 | 0.00% | 0/0 |
| 50 | candidate | Cultivation | 0.263 | 53,624 | 0 | 0.00% | 0/0 |
| 50 | control | Watercraft | 0.338 | 26,761 | 0 | 0.00% | 0/0 |
| 50 | candidate | Watercraft | 0.349 | 14,104 | 0 | 0.00% | 0/0 |

Zero breeding reports an applied intensity of exactly zero, rather than treating every `.45` recommendation as a cap. Saved targets and applied intensities remain in every detailed camp row. Population-linked food/inventory, births, deaths, slaughter and food by source remain in the raw turn reports and CSV trajectories.

At turn 50 husbandry herder share P90 is 0.323; no camp in either variant staffs every resident as a herder at either reported endpoint. Husbandry QoL P25 is 0.732, below the earlier HV3 fission-on target of 0.77. That calibration gate remains open.

### Matched C1 performance regression

All six runs per horizon use opening-save SHA-256 `8a8bfc83ad0d3f0ec7e479e19cd13e1b90f331b00fce3d7a56c18b58be6fcad8`. Medians below are across three sequential pairs. Change is the median of paired ratios, which can differ from the ratio of the two displayed medians. Camp-turns sum camps **before** each turn; new fission daughters do not retroactively increase that turn's preparation workload.

| Turns | Metric | Control median | Corrected median | Paired change | Pair range |
| --- | --- | --- | --- | --- | --- |
| 25 | Total | 6.419 s | 8.569 s | +33.5% | +30.8% to +34.1% |
| 25 | AI | 1.855 s | 1.856 s | +0.1% | -2.8% to +1.4% |
| 25 | Preparation | 2.524 s | 4.472 s | +79.4% | +77.2% to +80.0% |
| 25 | Execution | 2.040 s | 2.179 s | +6.8% | +1.8% to +9.9% |
| 25 | Allocated | 2.965 GiB | 4.008 GiB | +35.2% | +35.2% to +35.2% |
| 25 | Peak sampled managed heap | 559.763 MiB | 568.784 MiB | +1.6% | +1.5% to +1.6% |
| 25 | Total per camp-turn | 3.116 ms | 3.953 ms | +26.9% | +24.2% to +27.4% |
| 25 | Allocated per camp-turn | 1.474 MiB | 1.893 MiB | +28.4% | +28.4% to +28.4% |
| 25 | Preparation per camp-turn | 1.225 ms | 2.063 ms | +70.5% | +68.3% to +71.1% |
| 50 | Total | 15.286 s | 36.216 s | +138.6% | +132.7% to +143.2% |
| 50 | AI | 3.688 s | 4.198 s | +16.6% | +13.8% to +20.3% |
| 50 | Preparation | 7.043 s | 26.453 s | +279.0% | +261.4% to +283.9% |
| 50 | Execution | 4.532 s | 5.614 s | +24.8% | +22.5% to +26.4% |
| 50 | Allocated | 8.101 GiB | 17.876 GiB | +120.7% | +120.7% to +120.7% |
| 50 | Peak sampled managed heap | 724.935 MiB | 756.010 MiB | +4.3% | +4.3% to +5.8% |
| 50 | Total per camp-turn | 2.246 ms | 3.826 ms | +71.5% | +67.3% to +74.9% |
| 50 | Allocated per camp-turn | 1.219 MiB | 1.934 MiB | +58.6% | +58.6% to +58.6% |
| 50 | Preparation per camp-turn | 1.035 ms | 2.795 ms | +172.5% | +159.8% to +176.0% |

| Turns | Variant | Camp-turns | Ending tribes | Ending population |
| --- | --- | --- | --- | --- |
| 25 | control | 2060 | 96 | 30,186 |
| 25 | candidate | 2168 | 120 | 39,273 |
| 50 | control | 6805 | 371 | 152,929 |
| 50 | candidate | 9466 | 579 | 264,470 |

All repeats preserve each variant's population, camps and camp-turn workload. The gameplay and separate instrumented C1 profiles also reproduce those endpoints. The larger cohort explains part of total runtime, but preparation per camp-turn still rises **70.5% / 172.5%**. This is a real planner regression, not a claim that more tribes are cheaper to plan.

### Separate work profiles and scale checks

Instrumented runs are separate from timing pairs. These work counts explain the remaining preparation cost; forecasting multiple operation alternatives increases the local kernel and investment-comparison count.

| Turns | Variant | Plans | Food kernel queries | Investment comparisons | Positive-operation field tile-turns | Mean positive operation |
| --- | --- | --- | --- | --- | --- | --- |
| 25 | control | 2060 | 173185 | 72358 | 8105 | 1.0000 |
| 50 | control | 6804 | 719991 | 313108 | 30853 | 1.0000 |
| 25 | candidate | 2168 | 788790 | 334925 | 9936 | 0.9964 |
| 50 | candidate | 9459 | 5234464 | 2315045 | 48479 | 0.9933 |

Operation statistics exclude fields with zero operation; they cannot be used to infer the fraction of abandoned fields. q is the camp-footprint mean at the endpoint, whereas operation is measured inside resource resolution. More maintainable fields and lower unpriced expansion work can coexist with a higher ending q.

| Residents | Operation choices | Total herd choices | Median plan ms | Thread allocation per plan |
| --- | --- | --- | --- | --- |
| 100 | 3 | 96 | 2.25 | 0.542 MiB |
| 1,000 | 6 | 210 | 10.95 | 3.081 MiB |
| 10,000 | 6 | 210 | 13.00 | 4.935 MiB |

The isolated fixture has seven rich q=.5 fields, goats numbering half the residents, no Farm/Herding Tools, all first-tier capabilities and no geological resource state. One full plan warms the call, followed by five timed plans. These numbers bound numeric population scaling; they are not a modern-era or RL rollout benchmark. The full fixed suite passes; **410 focused shared-allocation assertions** pass, including 34-turn exact scalar/reversed-worker/save continuation.

Source digests are SHA-256 of compact sorted filename→file-SHA-256 maps:

- `control`: `2232bfce6b4fa6194be0d748a93fdf4979525efe4dc9e8227c998db2cdfbe25a`
- `candidate`: `68de32e4ddf6d3f0cdef90ad1f707976a718a8165de96248ec6c81b73677bc5a`

The control has 61 simulation source files; the candidate has 63. The final live candidate source hashes match the audited snapshot. Results include `summary.json`, `census-pooled.csv`, `census-by-world.csv`, `settlement-populations.csv`, `husbandry-subsets.csv`, `trajectories.csv`, `demography.csv`, `timing-runs.csv`, `timing-comparison.csv`, raw world/profile/timing JSON, source snapshots and build/check logs under `TestResults/SharedAllocation/`.

## Planner cost: exact per-plan kernel cache

Follow-up on 2 October 2026 to the regression above. The fixed suite and
the matched C1 replay show it **changes no outcome**: with the flag on, all 50
post-opening fingerprints match the pre-cache source, and with the flag off
(default) all 25 match. It reduces the regression but does **not** close it,
and runtime acceptance stays open.

### Cause

The demand pass used to build the expansion ladder and herd ladder once per
camp. `SharedAllocation` rebuilt both for every field-operation choice, and it
also ran two kernel forecasts per gatherable land tile and two pasture scans.
At 50 turns that was 553 food-kernel queries and 245 investment comparisons
per plan (control: 106 and 46). Most of that work is identical across
operation choices:

- tiles outside the footprint keep their saved operating fraction;
- a field with zero capital has `q = 0`, so its managed flow (∝ `q · op`), its
  decay and its effort are zero whatever the operation;
- after the first partial band, every ladder step lands on the same 32-band
  grid;
- pasture capacity and the settled local capacity read only the committed
  boundary.

### Change

`PlanningKernelCache` (in `SharedAllocation.cs`) belongs to one plan on one
worker and is never iterated. Each key holds the exact bit pattern of every
varying input, so a hit returns exactly what the uncached call would. It
memoizes:

- opening projections;
- operation-independent tile forecasts;
- `HorizonMarginalInvestment` results;
- the two pasture scans.

`FoodIncomeLedger` computes the opening-independent bucket basis once
(`TurnResolver.FoodBucketBasis`, with `SplitFoodBuckets` for the
food-dependent part, which is also what `FoodBucketTargets` now calls). Each
herd step makes one ledger receipt instead of three identical-argument ones.
`Select` reuses one ledger per portfolio.

**Fixed-grid ladder (measured, then withdrawn).** Step 1 first ran the
cached ladder's greedy loop to its end and cut it at the need cap. That keeps
the steps identical and makes investment counts independent of the camp's
food need: in the fixture, 154 at both 1,000 and 10,000 residents. It cost
+32% preparation at 50 turns (see *Timing*), so on 2 October 2026 the user
chose the need-capped ladder. Kernel work again follows the food need, up to
the same ceiling of `footprint tiles × (32 bands + operation choices)`
investments. The timing table below measured the fixed-grid variant.

### Checks

`SharedAllocationChecks` (611 assertions) compares a cached and an uncached
`SharedAllocation` bit for bit: every price crossing, every selection over six
prices and two budgets, and the cut ladder against the capped ladder for four
operations and six caps. Its fixtures include empty, half-operated and
foreign-operated fields. The full fixed suite passes, including scalar versus
2/4-worker and reversed-partition replay.

### Work counts

The new opt-in counters are `SharedAllocations`, `SharedOperationChoices`,
`SharedHerdChoices`, `ExpansionLadderBuilds`, `HerdLadderBuilds`,
`PastureScans`, `PlannerTileProjections`, `PlannerEvaluations`,
`SharedSelections`, `KernelCacheHits`, `SharedAllocationMilliseconds` and
`PlanMilliseconds`. They follow `ResolutionMetrics.CaptureDetailedCounters`
and appear in the audit JSON. They describe one instrumented C1 50-turn run
per source, separate from the timing pairs.

| Per plan (9,459 plans) | Before | Cached |
| --- | --- | --- |
| Food-kernel queries | 553.4 | 403.6 |
| Investment comparisons | 244.7 | 172.1 |
| Planner tile projections | 116.6 | 19.9 |
| Pasture scans | 4.2 | 1.2 |
| Ladder / herd-ladder builds | 3.21 / 2.10 | 3.21 / 2.10 |
| Planner evaluations (`Evaluate`) | 2.71 | 2.71 |
| Allocated bytes | 1.94 MiB | 1.71 MiB |

"Before" counts for the new counters come from the same source with the
cache disabled (`cacheKernel: false`). Its fingerprints also match.
`Evaluate` runs only 2.7 times per plan, so the fulfilment search was not
the cost driver.

### Timing

This uses the method above: the C1 opening, one worker, sequential runs with
pair order alternated, three pairs, no other jobs, and medians of paired
ratios with the pair range in brackets. The opening was regenerated in this
environment (.NET 8.0.131, SHA-256 `d7bc62e4…`). The control and candidate
endpoints are identical to the earlier save's.

| Turns | Metric | Control | Before | Cached | Cached vs before |
| --- | --- | --- | --- | --- | --- |
| 25 | Preparation | 3.856 s | 5.132 s | 4.722 s | −7.3% [−19.4, −7.2] |
| 25 | Total | 7.250 s | 8.912 s | 8.543 s | −2.2% [−18.1, −1.3] |
| 25 | Preparation per camp-turn | 1.872 ms | 2.367 ms | 2.178 ms | vs control +13.2% (before +22.9%) |
| 50 | Preparation | 7.966 s | 25.971 s | 21.548 s | −17.0% [−24.2, −13.7] |
| 50 | Total | 15.465 s | 35.499 s | 31.152 s | −9.7% [−19.2, −8.5] |
| 50 | Allocated | 8.10 GiB | 17.88 GiB | 15.75 GiB | −11.9% |
| 50 | Preparation per camp-turn | 1.171 ms | 2.744 ms | 2.276 ms | vs control +95.6% (before +135.5%) |

The 25-turn ranges are wide in this environment.

**Cost of need-independent work.** The same source with the ladder stopped
at the need cap was compared at 50 turns, three pairs, before the final
list-capacity change. The capped ladder is also exact (same endpoints), but
the fixed grid costs **+32.2% preparation** [+28.7, +38.3] and **+22.8%
total** [+17.9, +25.7]. Switching back is a one-line change in
`CultivationEconomy.ExpansionLadder`.

### Step 2: one expansion ladder per plan

The user approved this outcome-changing step on 2 October 2026, with
acceptance on per camp-turn cost and the five-world census staying within
the current branch's P25–P75 bands. `SharedAllocation` now builds one
expansion ladder per plan, priced from the field state of the most
maintained operating choice. It is recorded up to the largest food need among
the choices, and each choice takes its own need-capped prefix
(`CultivationEconomy.CutLadder`, the capped loop's own running sum).

The most maintained choice therefore gets exactly its former ladder. A
reduced choice prices its new fields as if the existing ones were kept, and
cannot also buy back the decay it accepts. On C1, 89.6% of plans choose full
operation, 8.3% abandon their fields, 1.5% choose a reduced band level and
0.6% the tool-coverage level. Restricting choices to {0, full, tool} instead
would have kept 93.4% of plans unchanged but saved little, because plans see
3.6 operation choices on average and at most 7. That lever was not taken.

**Checks.** `SharedAllocationChecks` adds that a cut ladder equals the
directly capped ladder for every tested cap and operation (731 assertions).
The full fixed suite passes.

**Census.** Five worlds (C1, C2, C3, P1, A1), 50 turns from the same openings,
shared ladder versus the exact branch, pooled tribes (`linked > 0`). For each
metric the candidate median must lie within the exact branch's P25–P75. All
56 checks pass: 7 metrics × {all, Cultivation, Husbandry, Watercraft} × {25,
50} turns.

| Turns | Group | Tribes | Residents median [exact P25, P75] → shared | Animals | QoL |
| --- | --- | --- | --- | --- | --- |
| 25 | All | 668 → 668 | 296 [164, 439] → 296 | 0 [0, 15] → 0 | 0.901 [0.798, 0.959] → 0.902 |
| 50 | All | 3,061 → 3,079 | 444 [271, 656] → 441 | 62 [0, 184] → 62 | 0.863 [0.730, 0.933] → 0.863 |
| 50 | Cultivation | 665 → 671 | 445 [277, 609] → 451 | 0 → 0 | 0.839 → 0.838 |
| 50 | Husbandry | 2,307 → 2,318 | 444 [271, 668] → 440 | 118 [37, 250] → 118 | 0.876 → 0.877 |
| 50 | Watercraft | 89 → 90 | 389 [228, 562] → 389 | 0 → 0 | 0.820 [0.716, 0.917] → 0.806 |

The other checked metrics are linked population, mean field q, farmer share
and herder share. At 50 turns, pooled population is +0.14% (1,381,126 →
1,383,033) and managed animals +0.8%. The technology mix is unchanged to
three decimals (Cultivation 0.217 / Husbandry 0.754 / Watercraft 0.029). C1
diverges from the exact branch from post-opening turn 8.

**Work counts** (C1, 50 turns, one instrumented run each):

| Per plan | Exact (need-capped) | Shared ladder |
| --- | --- | --- |
| Expansion ladder builds | 3.21 | 0.83 |
| Food-kernel queries | 232.0 | 203.6 |
| Investment comparisons | 86.3 | 72.1 |
| Kernel cache hits | 204.8 | 29.7 |
| `SharedAllocation` constructor | 0.605 ms | 0.510 ms |
| Plan | 1.457 ms | 1.381 ms |

The need-capped exact branch reproduces every pre-cache fingerprint. Much of
the repeated ladder work was already a cache hit, so sharing the ladder
mainly saves the greedy loop and the per-choice off-grid steps: about 5% of
plan time and 16% of the constructor.

**Timing.** These are C1 sequential pairs after the container resumed. The
noise was larger than in Step 1: control preparation alone ranged 2.5–4.4 s
over the same 25 turns, and the unchanged AI phase varied by up to 25%
between paired runs. Three triples (control, exact, shared) at 25 and 50
turns, plus four further alternating exact/shared pairs at 50 turns:

| Measure (50 turns) | Control | Exact need-capped | Shared ladder |
| --- | --- | --- | --- |
| Preparation per camp-turn, triple medians | 1.266 ms | 2.048 ms | 1.690 ms |
| Shared vs exact preparation | | | −17.0% over the 3 triples (range −17.2% to −4.9%); −8.1% over 4 alternating pairs (range −15.2% to +0.8%) |

The paired 25-turn change is within noise (+2.8%, range +1.1% to +25.0%).
Against the 2.744 ms per camp-turn measured before Step 1, the chain is:

1. The exact cache.
2. Relaxing to the need-capped ladder (exact, about −32%).
3. The shared ladder (about −5% to −8%).

Together these bring 50-turn preparation per camp-turn to roughly 1.7–1.8
ms, against a control of about 1.2–1.3 ms. Preparation stays roughly
+35–60% over the control per camp-turn, and the candidate's camps are also
larger. Runtime acceptance against the earlier HV3 +5% total criterion
remains open.

### What remains

With the need-capped shared ladder, the constructor is about 0.5 ms of a
1.4 ms plan. Most of it is the per-choice land forecasts for fields with
capital, plus the per-choice herd ladders (2.1 per plan). The rest of the
plan is the same craft fulfilment search and demand pass the control runs.
Sharing the herd ladder across operation choices would be the next
outcome-changing lever. It has not been measured.

```bash
python3 Tools/PerformanceAudit/Run-SharedAllocationAudit.py --phase=build --output=TestResults/PlannerPerf
# Generate the C1 opening with the control build, then run each build with
# --load=<opening> --turns=0 --post-opening-turns=25|50, --metrics=1 --fingerprints=1
# for counts/exactness and --metrics=0 --fingerprints=0 for timing pairs.
```

