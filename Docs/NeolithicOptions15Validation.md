# Neolithic Options 1/5 validation

Date: September 25, 2026. Branch: `codex/neolithic-options-1-5-validation`.

**Throughput decision: the 20% aggregate-time target is not met.** Exact baseline/candidate replay passes. Median paired time reductions are **3.7% at 80 tribes** and **13.6% at 320 tribes**, with about 15% fewer allocated bytes at both sizes. A repeatable final-turn regression at 320 tribes also remains unresolved. Retain this branch as a validated experiment rather than promote it as meeting the target. The analytical turn design is the follow-up candidate; it has not been implemented.

The user approved the exact-cache experiment on an offshoot branch after reviewing the [assessment](NeolithicPerformanceOptimizationPlan.md). The unmet throughput target makes the [analytical turn plan](NeolithicAnalyticalTurnPlan.md) the retained follow-up candidate. It remains design-only and is not part of this implementation.

The simulation baseline is `9dc18c05d3a1ae0a7d3462049dd34a9b7c41f07d`; the implemented candidate is `b5d98d72288d78a45a83967c5370a1cec5451d82`. The common audit harness and final mature save-replay check are committed at `5244ba24e271c3163b7730cd55c638b1ebcda2e5`; baseline and candidate use the same audit source and SDK. The requested upstream pull encountered an existing broken Codex checkpoint reference; the GitHub branch API independently confirmed that the baseline was already the upstream branch tip. No checkpoint references were repaired or deleted.

## Implemented scope

Option 5 caches each cultivation candidate's projected ecology and expensive four-season score within one `Expand` call. Every selection iteration still calculates the exact affordable step for every tile. A score is reusable only while that step and its state inputs match. The selected tile is invalidated after its write; arbitrary selection callbacks invalidate the entire cache. This correction matters when the remaining shared labor budget makes previously untouched candidates budget limited.

Option 1 caches post-movement footprints, static gathering membership and hunter-to-food-camp connectivity for one resource pass. It reuses per-camp demands, per-tile claim buffers and availability scratch storage. Current ownership, operation, labor, ecological availability and quantities are still refreshed at each original seasonal boundary. Small claim groups use stable insertion sorting; larger groups retain the original stable ordering path. Receipts own their values before buffers are reused.

The ecological integrator, configured allocation increment, seasonal barriers, shared resource coupling, prior-cohort needs, birth timing and save format are unchanged. No persistent cache or approximation is introduced. The code changes are in `CultivationEconomy.cs` and `GenerationalEconomy.cs`.

Luna work was divided into cultivation implementation and differential checks, audit/gate implementation, and independent seasonal/replay review. Astra implemented seasonal scratch reuse, integrated the work and ran validation sequentially.

## Correctness evidence

| Check | Result |
| --- | --- |
| Cultivation allocator against an uncached reference | 6,175 assertions passed: budget clipping, steps, bridge/ceiling, focus/ties, ecology, tools, pressure, callback order and callback-driven neighbor changes. The no-callback production path is checked separately. |
| Full fixed headless suite | Passed, including ecology/conservation/save checks and the new allocator coverage. |
| Mature worker and collection-order replay | Scalar versus two/four workers matched every fingerprint across 37 resolutions, including 25 post-opening turns with partial initial/final seasons and reversed entity/partition order. Both worker worlds were saved/reloaded after post-opening turn 12, matched immediately, and continued matching the unsaved scalar world afterward. The final focused economy suite passed after this checkpoint was added. |
| Transition matrix, baseline versus candidate | All 20 quick cases completed. Their full `trajectory.jsonl` files were byte identical; SHA256 `C5010A6E95B61F58F5D12F52A5107610B08B7A622CE8230846EA7B55BD1D8850`. Quick mode itself does not claim the full scalar/parallel/save trio. |
| Shared-save large-world replay | 80 and 320 tribes each matched the baseline fingerprint after every one of 25 post-opening turns, plus initial/final state. A separate four-player 34-turn opening-plus-continuation audit also matched exactly. |
| Final opening scale audits | 80 and 320 tribes completed 10 and 11 opening resolutions respectively, with scalar/four-worker fingerprints identical every turn and after initial/final save/load. These complement the longer mature replay checks above. |
| Unity 6000.5.10f1 EditMode | 29/29 passed. No presentation changes or new player usability claim. |

## Measured throughput

The three 80-tribe pairs miss both speed targets. The median paired total ratio is **0.9627** (3.7% less time), and the median-turn ratio is **0.9779** (2.2% less time). Allocation falls much more consistently, by **15.5%**. The individual total ratios span 0.7633–1.1012, so this is not evidence of a reliable 20% gain. The median paired p95 and maximum ratios are 1.0237 and 1.0407, within the 5% tail screen; individual pairs still have worse tails.

At 320 tribes, all three pairs improve total time, but none reaches 20%. The median paired total ratio is **0.8637** (13.6% less time). The median-turn ratio is **0.8719**, meeting the secondary 10% median-turn target. Median p95/maximum ratios are **0.8440/0.8231**; the tail screen passes. Allocated bytes fall **15.3%**. Acceptance requires both sizes to meet the primary target, so these results do not pass the experiment's aggregate gate.

Every entry below covers the same 25 turns. Ratios use unrounded values; displayed durations are rounded. A ratio below one favors the candidate.

| Tribes / pair | Baseline total (s) | Candidate total (s) | Total ratio | Baseline / candidate median (ms) | Baseline / candidate p95 (ms) | Baseline / candidate max (ms) |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 80 / 1 | 19.622 | 21.608 | 1.1012 | 793.7 / 830.1 | 1,018.1 / 1,113.8 | 1,124.9 / 1,170.7 |
| 80 / 2 | 20.316 | 19.559 | 0.9627 | 747.3 / 730.7 | 1,077.1 / 1,102.6 | 1,096.7 / 1,235.9 |
| 80 / 3 | 23.289 | 17.776 | 0.7633 | 811.8 / 713.6 | 1,428.5 / 914.6 | 1,682.1 / 929.3 |
| 320 / 1 | 106.186 | 91.711 | 0.8637 | 3,921.5 / 3,419.3 | 6,278.4 / 5,165.6 | 7,500.8 / 5,947.2 |
| 320 / 2 | 105.122 | 86.000 | 0.8181 | 4,051.3 / 3,336.8 | 5,794.0 / 4,890.1 | 5,955.5 / 4,902.2 |
| 320 / 3 | 109.764 | 98.870 | 0.9008 | 4,233.3 / 3,708.6 | 6,396.2 / 6,234.2 | 6,622.2 / 6,621.4 |

At 80 tribes, all three timing pairs also match their separate exact run's population, prior-cohort QoL, food yield and agricultural counts. Median total allocation per 25-turn run is approximately 11.493 GB baseline and 9.710 GB candidate (decimal units), saving about 71 MB per turn. Across all three runs, Gen0/Gen1/Gen2 collection counts are 4,137/1,030/98 versus 3,498/851/90. These counts do not measure pause duration or establish the cause of timing variation.

Matching each turn's median across the three repeats flags turn 33 at 1.151× baseline. Its individual pair ratios are 1.070/1.314/1.030; the result is variable and does not support a claim of uniformly faster turns. No samples were removed, and the favorable third pair does not override the primary gate.

At 320 tribes, all six timing reports likewise match their separate exact run's population, prior-cohort QoL, food yield and agricultural counts. Median total allocation per run is 48.133 GB baseline and 40.779 GB candidate, about 294 MB less per turn. Across the three runs, Gen0/Gen1/Gen2 counts are 17,000/4,159/129 versus 14,358/3,670/106.

Matching per-turn medians flags turns 21 and 36 at 1.152× and 1.083× baseline. Turn 21 varies between pairs. **Turn 36 exceeds the 5% regression screen in all three pairs** (1.076/1.054/1.229), so the aggregate p95/maximum pass does not clear every matched-turn screen. Its workload is identical: 248 agricultural camps, 9,371 active tiles, 1,739 seasonal tiles, 146,752 tile integrations and 394,986 claims. Allocation falls about 311 MB, yet total time rises 326/250/1,038 ms. The delay appears in seasonal preparation in pair 1, final preparation in pair 2, and execution outside the improving ecology phases in pair 3. Each candidate turn records two Gen2 collections versus one in its baseline. Pause durations were not captured; this correlation does not isolate a cause. Keep the regression unresolved, with no promotion claim based on averages.

### Cultivation-only ablation

One additional run per size used the baseline source with only candidate `CultivationEconomy.cs` substituted. This hybrid does not include Option 1's seasonal scratch reuse. Population, prior-cohort QoL and food yield match the baseline exactly. The component's differential allocator checks provide separate correctness coverage; this fingerprints-off ablation is not an additional exact full-world replay.

| Tribes | Option 5-only total (s) | Baseline three-run median total (s) | Option 5-only allocation (GB) | Baseline median allocation (GB) |
| --- | ---: | ---: | ---: | ---: |
| 80 | 21.375 | 20.316 | 11.577 | 11.493 |
| 320 | 113.344 | 106.186 | 48.538 | 48.133 |

These single, later runs do not establish a causal timing effect or Option 1's standalone speedup. They provide no independent evidence of an end-to-end win from Option 5 and show its scratch-array allocation overhead. The accepted timing evidence remains the three combined-change pairs at each size, with the unmet target unchanged.

### Remaining seasonal cost

An independent timer-boundary review found that seasonal preparation, integration, commit and expansion watches run sequentially inside each segment. They are subsets of the enclosing ecology/execution time, not additional costs to add to those totals.

| Candidate 80-tribe phase | Median total over 25 turns (s) |
| --- | ---: |
| AI | 2.304 |
| Turn preparation | 0.998 |
| Resolver execution | 16.144 |
| Seasonal preparation | 2.014 |
| Seasonal integration | 1.907 |
| Seasonal commit | 0.602 |
| Seasonal expansion | 2.432 |
| Independent-tile integration | 0.514 |

Summing the four seasonal watches within each run, then taking the median, gives **6.734 seconds**: a median **34.4%** of AI + preparation + execution, or **41.3%** of execution. Individual phase medians need not add up to that median of sums. The seasonal numerical integration share alone is a median 10.7% of total time. The analytical design is therefore relevant only if it also reduces repeated claim preparation and field allocation, rather than substituting only the stock equations already solved analytically within each season.

These are elapsed-time attribution measurements, with GC and process/machine variability; they do not predict an analytical solver's speedup. All measured outcomes match the separate exact replay, but a future approximation would require its own trajectory, flow, conservation and replay checks.

## Measurement contract

The primary comparison is the complete current Neolithic implementation against its exact optimized version. Historical `main` lacks this economy and is retained only as an opening diagnostic; no new main-relative pass is claimed.

Each workload uses Continents, seed 1729, four workers, 64-item partitions and twenty-year turns. The 80-tribe world is 1024×512; the benchmark-only 320-tribe world is 2048×1024. A baseline-generated save at natural opening completion is reused by every revision and run. Each run resolves exactly 25 more generations (500 years) with the real AI path, all original tribes and live herds. No growth, ecology or agriculture controls are changed to lighten the workload. These are the audit's existing API-default worlds: resource maximum 100,000 and no deep reserves, not the desktop's enlarged 10-million/50× resource preset.

| Workload | 80 tribes | 320 tribes |
| --- | ---: | ---: |
| Measured turn labels | 11–35 | 12–36 |
| Agricultural camps, every measured turn | 58 | 248 |
| Population after first → last measured turn | 9,175 → 49,747 | 42,601 → 197,431 |
| Active tiles, first → last measured turn | 2,611 → 2,353 | 10,571 → 9,371 |
| Seasonal dependency tiles, first → last measured turn | 407 → 410 | 1,740 → 1,739 |

The shared input save SHA256 values are `E298B0E004BE8069981BE93573A76E90D12A82253B26CDF6A10FC0BF0DF3C02B` (80) and `04568A1D515FFAFA7E749BB391E08BDFB1C1BADFD837B9A486A212F3B9B56503` (320).

Three sequential matched pairs per size alternate process order. The primary ratio is candidate total AI + preparation + resolution time divided by the baseline total over the same 25 turns; acceptance requires the median paired ratio ≤0.80 at **each** size. The secondary target is a median-turn ratio ≤0.90, with p95/worst-turn regressions above 5% flagged for investigation. Outcomes cannot be traded away to meet these targets.

The runtime is Windows x64 / .NET 8.0.21, compiled Release with the Unity 6000.5.10f1 bundled SDK 8.0.318. Every process receives the same one-run, eight-turn 96×48/four-player warmup. This is a small-world warmup, not a claim that every mature agricultural path was pre-JITed. Generation/loading, hashing, report output and save I/O are outside measured intervals. Formal timing disables fingerprints and detailed counters; separate runs verify exact fingerprints after every turn because full-state hashing can affect later garbage collection. No benchmark processes or Unity test runs overlap. Detailed field-selection callbacks conservatively invalidate the score cache, so their timings cannot estimate the normal no-callback cache speedup.

Allocated bytes and GC counts describe transient work, not retained memory. The new caches are local to an allocation call or resource pass; no new state grows across turns. Retained-memory effects of the 500-year workload are not measured by the timing harness. The source/API/reference labels and executable/save hashes are recorded with the reports; labels are supplied by the runner rather than inferred from a binary.

## Reproduction and evidence

See [the audit README](../Tools/PerformanceAudit/README.md) for source extraction, isolated output builds, shared checkpoint generation and the `Run-Options15.ps1` matrix. Use `-Mode Exact` before `-Mode Timing`. `-Mode Option5` builds evidence for the cultivation-only contribution from a disclosed hybrid source; a single ablation run is exploratory, not a repeated causal estimate.

Machine evidence is intentionally ignored under `TestResults/Options15/`: `HeadlessFixed.log`, `EconomyFinal.log`, `Scale-{80,320}.json` and logs, `TransitionBaseline/`, `TransitionCandidate/`, `Unity/EditMode.xml`, baseline/candidate exact reports, numbered timing pairs, executable/save manifests and `summary-{80,320}.json`. Recreate these outputs with the checked-in harness; do not force-add generated saves or reports.
