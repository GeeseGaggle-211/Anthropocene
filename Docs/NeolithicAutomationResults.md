# Demand automation measurements — September 21, 2026

Simulation revision: `abe3e685ea17dda4ee3cf19b8e6c8fa837fe53b8`, following `ab54ecf090ad89e56897cba2b4dbfc94b017f8e3`, on `features/neolithic-agricultural`. See [EconomicAutomation.md](EconomicAutomation.md) for the design reconciliation, controls and architecture. Historical pre-automation measurements remain in [NeolithicSweepResults.md](NeolithicSweepResults.md).

## Cohort and reproducibility

Completed 168 independent runs of 25 twenty-year turns (4,200 generation records), starting from 250 people, Agriculture, q=0, one generation of food reserves, and no equipment/materials/Fabric/Housing. The seed-11 river cohort includes all 80 Fabric × Housing-capacity × Housing-decay combinations plus 24 baseline/sensitivity cases; eight-case Fabric/farmer slices cover the other eight seed/site combinations. Seeds: 11, 42, 1729; sites: river, wooded, open. Automation is enabled once, then empty orders test saved multi-turn operation. Independent replay of all three seed-11 baselines reproduces 75 generation records exactly.

Baseline configuration hash remains `A3DE685DAC3474A1DD52504AF200E841C3D5C326E7DDDCF438599C97B9BA1F30`, identical to the earlier sweep. No recipe, extraction/manufacturing, ecological, demographic, mastery or QoL constants were retuned.

Run the sweep project with `--automatic --seed 11 --habitat river`. Optional `--parts 4 --part 0` through `--part 3` divide independent variants without changing them. Use `--automatic --seed <seed> --replicate` for the eight-case baseline/Fabric/farmer slice, optionally restricted with `--habitat`. Replays use `--automatic --seed 11 --pilot`. `SummarizeAutomation.py --source <validated-root> --previous <old-run-summary.json> --replay <replay-root> --out <report-directory> --revision <simulation-sha>` validates completeness, conservation, labor, configuration identity and exact replay before exporting Markdown, JSON and a raw-data ZIP. Export directories must not mix duplicate runs.

## Results

| Seed 11 river policy/version | Population at 500 years | Assigned craft % | Productive craft % | Farmer harvest share | Weaving / Construction year |
| --- | ---: | ---: | ---: | ---: | --- |
| Previous automatic | 527 | 21.06 | 9.00 | 0% | 280 / 300 |
| Previous per-turn reset | 264 | 44.11 | 0.00 | 0% | neither |
| Previous fixed-share control | 1016 | 9.94 | 2.25 | 29.6% | 40 / 80 |
| Saved demand automation | 942 | 2.55 | 2.25 | approximately 100% | 40 / 40 |

These historical comparisons are not a same-code causal A/B: the new implementation also corrects farmer-access/working-time and Fabric/growth accounting. The fixed-share control is not an optimized policy.

The new reference has zero deaths, full Food/Clothing/Housing satisfaction and QoL, mean q=.3236, 359 assigned Farmers, 13 Weavers and 11 Builders. Farmer-majority harvesting begins at year 40. Material extraction uses .125% of population worker-time. Unassigned residents remain available as gatherers but are not necessarily working; neither an available-worker count nor farmer harvest attribution identifies wild-versus-cultivated biomass.

All six river/wooded baseline exemplars reach 942. They share saturated ecological profiles, so they are not six independent productivity treatments. The three open baselines reach 18 (seed 11, 232 deaths), 304 (42, 54 deaths), and 17 (1729, 233 deaths). Their initial 250-person fixtures can exceed local food capacity. Automation cannot create food or rescue infeasible starts; Housing/Clothing shortfalls are not death triggers.

| Fabric/person/turn, seed 11 river | Population | Productive craft % | Steady specialist prediction % | Weaving / Construction year |
| ---: | ---: | ---: | ---: | --- |
| .25 | 929 | 1.3125 | 1.3125 | 80 / 40 |
| .5 | 911 | 1.6250 | 1.6250 | 60 / 40 |
| 1 | 942 | 2.2500 | 2.2500 | 40 / 40 |
| 2 | 933 | 3.5000 | 3.5000 | 40 / 40 |
| 4 | 942 | 6.0000 | 6.0000 | 40 / 60 |

Prediction: `100 × [FabricNeed / 80 + HousingDecay / (PeoplePerHousing × 2.5)]`, conditional on stationary population, saturated needs, no stock bridging, matching specialists and Hand Tools. Baseline gives 1.25% Weavers plus 1% Builders. Extraction/tool work is additional; assigned headcount also reflects rounding and forecast headroom. Food-limited joint cases have lower realized manufacturing and unmet needs, not a violation of the recipe rate.

Increasing farmer efficiency 1.25 → 1.5 → 1.75 → 2 reduces final assigned Farmer shares 44.75% → 38.11% → 32.57% → 28.02%, while farmer harvest share remains approximately 100%. Corresponding populations are 923, 942, 918, 953. Population is not monotonic: integer staffing, useful-demand/reserve thresholds and ecological feedback change trajectories.

Weaving emerges in 152/168 runs and Construction in 168/168. Toolmaking, Pottery, Carpentry and CopperWorking do not emerge in these default-target runs. Pots/Farm Tools retain zero targets and useful durable-tool demand can end below mastery thresholds. Matching extractor/manufacturer adoption is tested with controlled unlocked-role fixtures; default emergence does not exercise every specialist path. Conditional Copper-tool probes do not imply emergent Copper progression.

## Verification and limits

- Final economic suite: 209 existing Neolithic + 76 automation assertions, economic/AI contracts and scalar/two/four-worker replay pass.
- Broad fixed headless regression passed, including 8,948 geology assertions and Paleo/worldgen checks; the final economic suite was rerun after the last food-projection fix.
- Final Unity EditMode: 28/28 passing. Windows build succeeds. Actual player passes queued-intent purity, recurring Fabric debit, exact isolated save/load and two-turn automation continuing after load without resubmitted orders. Five captures; automated invocation and visual inspection, not a human usability study.
- All 4,200 records have zero recipe/source conservation or workforce violations, and all runs have zero resolver errors. Four rows exceed the diagnostic 1e-6 food-discard threshold; maximum residual is 1.6023550415411592e-6 Food, maximum fraction of harvest 2.1400654500575367e-11. These floating-point residuals are disclosed rather than reported as zero-discard success.
- Local forecasts are not globally optimal. Shared-source contention and ecological change can reduce realized output. Source receipts remain authoritative. No claim of indefinite equilibrium or historical validation.

Evidence is ignored under `TestResults/NeolithicAutomation-Validated`, `NeolithicAutomation-Validated-Replay`, and `Automation-Unity` (`tests-final.xml`, `build-final.log`, `Player-final`). User deliverables include the full 80-row joint matrix, all 168 summaries, verification metadata, raw JSON/JSONL archive, final Unity XML and player result/capture. Generated evidence/builds are not committed.

Still deferred: automatic QoL fission, trade/logistics, expeditions, irrigation yield/withdrawals, SoilCondition, generic tool bonuses, Plough/Farm Tool effects, artificial tool wear, forced research, and later Copper progression. No blocking design conflict remains for the requested automation.
