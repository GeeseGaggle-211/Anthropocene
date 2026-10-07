# Mixed subsistence integration: validation and outstanding balance issues

## Status

Functional integration is validated for the Elder-RL development lineage. This is **not** a blanket non-regression or final Neolithic balance sign-off: population and managed livestock improve, but some QoL and fishing-first lineage outcomes regress. Do not describe the result as every food method improving. No further heuristic weight or ecological tuning was made to conceal those results.

The implementation enables resident Fishing, bilateral trade knowledge, local Husbandry exposure and physical resident taming for new worlds. The accepted relative exit-opportunity fission methodology remains CurrentPlusExitOpportunity, threshold 0.05, search fraction 0.8. Main and the older neolithic-agricultural branch are not modified by this integration.

## Reproducible evidence

Tested simulation/tree commit: `a217fd20d8afb3c9310732340bb4d66e8c45da0d` (combined source; later documentation and reporting edits do not change simulation behavior).

- Final fixed suite plus ten paired worlds: [Actions run 37665902884](https://github.com/GeeseGaggle-211/Anthropocene/actions/runs/37665902884).
- Complete raw JSON, logs and source snapshot: artifact `final-mixed-evidence`, ID `11503306279`, SHA-256 `6c12c24dccba35d89e9aa39a836ecdb75b72de2054e206b61f424d88e9e76151`. Artifact retention is 14 days; pinned source and commands below permit reproduction after expiry.
- Two-seed feature attribution: [Actions run 37666928375](https://github.com/GeeseGaggle-211/Anthropocene/actions/runs/37666928375), simulation checkout fixed at the same `a217fd20` commit. Artifact `mixed-attribution-evidence`, ID `11503017681`, SHA-256 `062fb3433303ba2993017e7baa8bf6dc5635ea5f021829c57bb94e62bcab3019`.
- Dispatch-cohort repair: [PR #40](https://github.com/GeeseGaggle-211/Anthropocene/pull/40), tested repair source `3c6d3c1ccc68937b353ce28161781b6d970368cb`, merged into integration at `874b4b8d085e766e8988b6516dd6af83a1d02af8`.

These tests ran on GitHub-hosted Linux/.NET 8 runners. Results were downloaded and independently checked for all expected seeds and 40 completed post-opening turns. They are not measurements from the user's local computer.

## Correctness and regression suites

All **63 registered fixed headless suites** passed at the combined commit. Recorded fixed-suite wall time was 63.8 seconds with three suite workers; that is test-harness timing, not an in-game performance claim. Relevant counts include:

| Suite | Passing assertions |
|---|---:|
| DispatchCohortChecks | 34 |
| HusbandryDiffusionChecks | 35 |
| MixedSettlementAcquisitionChecks | 28 |
| EconomicSettlementChecks (A-I) | 70 |
| FissionChecks | 657 |
| TradeChecks | 480 |
| FishingObservationChecks | 13 |
| FishingEconomyChecks | 47 |
| FishingResourceChecks | 43 |
| FishingStateChecks | 24 |
| FishingWarmCalibrationChecks | 36,892 |
| SettlingChecks | 65 |

The new resident tests use actual Prepare/Resolve with **no hunter detachments**, for both Cultivation-first and Watercraft-first settlements. They verify a real five-animal transfer, no current-horizon managed output, unchanged first-technology identity/location, save continuation, breeding reserves, no refill after deliberate liquidation, a camp-wide rather than per-species bootstrap cap, fractional population-progress conservation, and collection-order invariance. Bilateral settled-trade tests exercise both counterparties rather than only the direct knowledge helper.

### Failure found and repaired

The first world bank was incomplete: candidate seed 11 failed with camp 2336 at 28 residents but 29 assigned workers. Phase tracing located the first violation immediately after Movement, before production or fission. An accepted hunter Dispatch removed residents, but automatic planning and Fishing access had used the original population.

The repair uses accepted post-dispatch residents in automatic planning, Fishing preparation and draft-cache reuse. A nonserialized planning-only PendingDispatchConsumers field preserves meals for people who have left the workforce but whose hunter entity does not yet exist. It does not erase consumption, clamp workers after resolution, change animal yields or alter the fission baseline. The repaired seed 11 and all ten final matched worlds completed.

## Matched-world protocol

Continents, 80 founding tribes, seeds **11, 23, 47, 101, 1729**; 1024x512 production geography; each completed ten opening turns and forty post-opening turns. Four resolver workers, partition size 64, detailed metrics enabled. Both sides include the dispatch fix and the accepted 0.05 exit-fission rule.

Control: the same corrected code with resident Fishing, corrected aquatic pressure, trade/proximity learning and taming disabled. Candidate: new integration defaults enabled. This corrected control supersedes comparison with the earlier buggy dispatch trajectory; it does not silently redefine the accepted fission equation.

Control configuration hash: `AEF6DEF4547DCDC0A5ED8CD1F1A1CB4797150CC7D60EACE3E0170CBDA0F32307`.

Candidate configuration hash: `D15F008296832A9D7C2E829C35A5EBC530C1B9AB6EAA4839474D977F363A3B8D`.

Quartiles below are across five endpoint worlds: sorted values 2/3/4 (P25/median/P75). They are descriptive order statistics, not confidence bounds. World QoL is the audit's prior-cohort weighted QoL; capability-subgroup QoL uses endpoint resident weights. Capability groups overlap and must not be summed or treated as evidence that the corresponding occupation is active.

### Requested population, QoL and livestock distributions

| Metric | Control P25 / median / P75 | Integrated P25 / median / P75 |
|---|---:|---:|
| Total population | 95,270 / 119,042 / 120,657 | 103,258 / 127,437 / 130,387 |
| Prior-cohort world QoL | 0.8594 / 0.8602 / 0.8612 | 0.8583 / 0.8611 / 0.8616 |
| Husbandry-capable resident population | 60,390 / 86,128 / 87,078 | 71,156 / 94,055 / 100,362 |
| Husbandry-capable resident-weighted QoL | 0.8665 / 0.8710 / 0.8718 | 0.8675 / 0.8693 / 0.8712 |
| Managed Husbandry animals | 40,986 / 59,032 / 71,855 | 45,307 / 70,671 / 76,709 |
| Husbandry-capable settlements | 199 / 232 / 272 | 234 / 257 / 307 |

Mean final world population rises from 105,295.4 to 115,297.2 (**+9.50%**); mean Husbandry-capable residents from 70,558.6 to 83,966.4 (**+19.00%**); mean managed animals from 51,037 to 57,145.6 (**+11.97%**). Capability adoption accounts for part of the Husbandry membership increase; the separate animal totals are therefore essential.

QoL is not uniformly improved. Mean endpoint world prior-cohort QoL changes from about 0.862006 to 0.861362. Its P25 declines; Husbandry subgroup median and P75 decline slightly. These are measured changes, not assumed numerical noise.

### Paired seed outcomes

| Seed | Control population | Integrated population | Population change | Control animals | Integrated animals | Animal change | World QoL change, absolute |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 11 | 63,818 | 66,994 | +4.98% | 10,424 | 11,861 | +13.79% | -0.001085 |
| 23 | 119,042 | 127,437 | +7.05% | 59,032 | 70,671 | +19.72% | -0.003106 |
| 47 | 127,690 | 148,410 | +16.23% | 71,855 | 81,180 | +12.98% | +0.000901 |
| 101 | 120,657 | 130,387 | +8.06% | 72,888 | 76,709 | +5.24% | +0.000456 |
| 1729 | 95,270 | 103,258 | +8.38% | 40,986 | 45,307 | +10.54% | -0.000383 |

All five worlds gain population and managed animals, not merely the pooled quartiles. This still does not establish non-regression for every subgroup or service.

### Fishing-specific outcomes and actual activity

| Metric | Control P25 / median / P75 | Integrated P25 / median / P75 |
|---|---:|---:|
| Watercraft-capable residents | 4,036 / 5,906 / 7,936 | 4,040 / 6,592 / 7,445 |
| Watercraft-capable resident-weighted QoL | 0.8671 / 0.8682 / 0.8772 | 0.8409 / 0.8430 / 0.8508 |
| Assigned resident Fishers | 0 / 0 / 0 | 86 / 91 / 163 |
| Settlements with 2+ capabilities | 201 / 245 / 279 | 236 / 266 / 313 |
| Settlements with all three capabilities | 2 / 3 / 7 | 7 / 7 / 7 |

Percentage growth from zero resident Fishers is undefined, not 0%. The original automatic summary displayed that row incorrectly; the reporting utility now marks zero-baseline percentages unavailable and rejects missing/short matched banks.

Across endpoints, settlements staffing at least two of Farmer/Herder/Fisher rise from **63 to 115**. The integrated worlds contain **28 settlements staffing Fishers** and **three staffing all three food occupations**. These are workforce observations, not an assertion that each staffed occupation necessarily delivered a positive final receipt.

Watercraft-first lineage resident totals (first technology is diagnostic only) are control/integrated: seed 11 **3,791/3,681**, seed 23 **6,435/5,218**, seed 47 **2,202/1,490**, seed 101 **1,033/390**, seed 1729 **1,334/1,666**. Four of five decline, so the aggregate population gain must not be used to claim fishing-first non-regression.

## Two-seed attribution, without retuning

The same source and geography were rerun with (a) only resident Fishing/corrected aquatic pressure enabled and (b) only knowledge diffusion/taming enabled. Each of the four additional worlds completed forty post-opening turns.

| Seed | Variant | World population | Watercraft-first residents | Watercraft-capable residents | Watercraft-capable QoL |
|---:|---|---:|---:|---:|---:|
| 23 | Control | 119,042 | 6,435 | 9,606 | 0.886916 |
| 23 | Fishing only | 117,488 | 5,184 | 8,401 | 0.840962 |
| 23 | Diffusion/taming only | 129,765 | 7,558 | 12,099 | 0.866303 |
| 23 | Both | 127,437 | 5,218 | 9,788 | 0.842964 |
| 101 | Control | 120,657 | 1,033 | 2,717 | 0.868228 |
| 101 | Fishing only | 119,881 | 390 | 2,074 | 0.906848 |
| 101 | Diffusion/taming only | 130,960 | 1,033 | 2,717 | 0.868228 |
| 101 | Both | 130,387 | 390 | 2,074 | 0.906848 |

In these two seeds, enabling the resident-Fishing path reproduces the lineage population loss without diffusion or taming. This localizes the feature group, not the exact cause within movement, economic allocation, capital development, changed activity practice, demography or fission. In seed 101 the surviving fishing-first parent has higher QoL but no daughter, unlike the control; population and individual well-being cannot be conflated. The result must be a retained regression/learning scenario, not repaired by assigning a technology-specific strategic weight.

## Acceptance boundary and remaining work

The correctness substrate is suitable for an **experimental RL integration**, with explicit known balance regressions. It is not a claim that the current heuristic Elder optimizes all three food systems or that RL will automatically fix the fishing outcomes. A release/Neolithic-balance gate must separately address the fishing cohort cases above. Preserve the feature-off comparator and all four feature combinations for attribution.

Other limitations: five seeds are a regression screen, not broad statistical certainty; no long-horizon learned-agent training was run; current taming is a bounded bootstrap rather than a fully costed acquisition investment; natural disasters, direct live-animal trading, Copper Working and later obligations remain separate features. No new rule attracts/lures wildlife to villages; locality follows existing herd movement.

## Reproduction

At the pinned source, run the fixed suites through `Tools/Test-Headless.sh`, including `DispatchCohortChecks`, `MixedSettlementAcquisitionChecks` and the registered Fishing suites. World command:

```sh
dotnet run -c Release --project Tools/PerformanceAudit/Anthropocene.PerformanceAudit.csproj -- \
  --players=80 --seed=11 --shape=Continents --production-config=1 \
  --turns=12 --post-opening-turns=40 --workers=4 --partition-size=64 --warmup=0 \
  --details=1 --metrics=1 --fingerprints=0 --output=TestResults/Mixed/candidate-11.json
```

Repeat the five seeds. Control adds `--set=NeolithicFishingEnabled=0,CorrectedAquaticSeasonalPressureEnabled=0,TradePracticePerValue=0,HusbandryProximityPracticePerYear=0,HusbandryTamingEnabled=0`. Fishing-only disables the last three; diffusion-only disables the first two. Use the checked `Tools/PerformanceAudit/Summarize-MixedSubsistence.py` against a complete control/candidate bank. Detailed-run timing is not a clean performance comparison because populations, settlement counts and instrumentation differ.
