# Current-engine Neolithic sweeps — September 20, 2026

**Historical pre-automation measurements.** The saved demand planner and specialist adoption now replace the limitations diagnosed in this pass. See [EconomicAutomation.md](EconomicAutomation.md) and [NeolithicAutomationResults.md](NeolithicAutomationResults.md) for the later implementation and rerun. The original results below are preserved for comparison, not presented as current behavior.

Simulation source revision: `1c95bf6a7ec3ece9513e781211e1f24b29cef029`.
This measurement pass changes no gameplay source or production constants.

Completed: **328 runs and 8,200 resolved generations**, with zero resolver errors
or receipt-arithmetic/workforce-budget failures. All 80 joint cases are present.
Six independently repeated pilot baselines match all 150 generation records
exactly. The existing **209 Neolithic assertions** also passed again. No Unity
rebuild was needed because only measurement tooling/documentation changed.
The tiny discard residual appears in 156 generation rows; its maximum fraction
of harvested food is about 7.97e-9. It remains an unresolved measured discrepancy.

## Scenario and reproducibility

The runner loads current `OpeningConfig.json` and production geological resource
options. It generates 256×128 worlds with seeds 11, 42 and 1729. In each world it
selects the highest initial wild-food renewal seven-land-tile river, wooded and
open footprints with seasonal freshwater access. These are optimistic examples,
not random samples. River examples can be fully wooded, and the best productive
profiles saturate at the same capacities across seeds.

Each paired case starts with 250 people, Agriculture, q=0, one generation of
plant-food reserves, and zero manufactured inventory. Other camps and animals
are removed; local source quantities and ecology are not repaired. This isolates
local agriculture/manufacturing, not natural Paleo transition or regional trade.

- `stock`: the shipped automatic proposal, which preserves existing assignments.
- `replan`: the existing demand-based reset proposal submitted each turn.
- `managed-control`: explicitly experimental player orders assigning 20% Farmers
  and 10% craft slots, with unlocked Weavers/Builders filling needs inside those
  slots. Remaining people gather. It does not grant technology or goods and does
  not modify the shipped policy.

The primary experiment uses the full Fabric {0.25,0.5,1,2,4} × Housing capacity
{3,4,5,6} × Housing decay {.05,.10,.15,.20} matrix on seed-11 river. Other
seed-11 sites receive baseline slices and one-at-a-time sensitivities. Fabric and
farmer-productivity slices repeat across all nine seed/site pairs with stock and
replan policies. The managed control exercises 16 selected variants. Runs last
25 twenty-year generations. Housing tool ranges are also tested conditionally
with explicitly supplied inventory/capability, never as emergent Copper Working.

Build `Tools/NeolithicSweeps/Anthropocene.NeolithicSweeps.csproj` with .NET 8.
Run its DLL from the repository root with the following argument sets, using
different output directories:

```text
--seed 11 --habitat river --out TestResults/NeolithicSweeps/11-river
--seed 11 --habitat wooded --out TestResults/NeolithicSweeps/11-wooded
--seed 11 --habitat open --out TestResults/NeolithicSweeps/11-open
--seed 42 --replicate --out TestResults/NeolithicSweeps/42-all
--seed 1729 --replicate --out TestResults/NeolithicSweeps/1729-all
--seed 11 --habitat river --managed --out TestResults/NeolithicSweeps/11-managed
```

`Summarize.py <input-root> <output-directory>` validates completeness and creates
the Markdown report, all run summaries, paired deltas and SVG chart. Optional
`--replay <pilot-directory>` compares exact per-generation records against a
previous run. `--partial` is only for in-progress inspection, not final reporting.

## Measured baseline at 500 years

On the productive seed-11 river footprint:

| Policy | Population | Assigned generalists | Assigned specialists | Realized craft-worker time | Weaving | Construction |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Stock | 527 | 21.06% | 0% | 9.00% | 280 years | 300 years |
| Replan | 264 | 44.11% | 0% | 0% | not reached | not reached |
| Managed control | 1,016 | 7.58% | 2.36% | 2.25% | 40 years | 80 years |

Labor percentages use the population at the start of the last generation;
population is the ending count. Productive craft time excludes source extraction.
Unlock years are elapsed since the standardized Agriculture start, not a claim
about when Agriculture emerges.

The stock policy keeps the initial 111 craftspeople as an absolute count and
never assigns Farmers. Its q reaches about .226 through gatherer tending.
The replan policy repeatedly reserves about 44% for crafts while nearly all
gatherer time goes into food, starving raw-material/tool bootstrap. The managed
control reaches mean q .386, but operating Farmers still collect only about 29.6%
of food at the baseline productivity. It does not reproduce the historical
majority-farming transition timeline.

At baseline saturation, productive generalist craft labor is exactly 5% Fabric
plus 4% Housing maintenance. With unlocked specialists it is 1.25% plus 1%.
At Fabric demand 2 these become 14% generalist versus 3.5% specialist work.
Thus recipe/labor calibration agrees with the revised design, while default
allocation and specialist adoption do not produce its intended trajectory.

The old analytical housing table explicitly used the superseded generic output
rate. Its population totals are not an identical-environment benchmark for this
current-engine, revised-Housing experiment. Population responses are not uniformly
monotonic because of reserve/growth thresholds and discrete labor allocations.

## Known limitations exposed

- Default policy does not adopt unlocked specialists. Their throughput advantage
  requires actual valid specialist assignments.
- No useful default Pots/FarmTools targets means no Pottery/Carpentry progress.
  Toolmaking is constrained by useful demand for durable equipment. Copper Working
  remains deferred. Do not invent output just to force research completion.
- The selected open sites can be drastically food-limited: baseline stock
  populations end at 8, 57, 8 for seeds 11, 42, 1729. This is not QoL mortality.
- A small storage/discard residual of about 0.000160652 Food was observed. The
  previous zero-waste fixture did not expose it. Do not label the broader run
  perfectly waste-free or silently retune the resolver in a measurement pass.
- Water remains the existing satisfaction input, and imports/logistics, fission,
  irrigation, broad tool effects and other deferred systems remain unchanged.

Raw machine-readable results are ignored under
`TestResults/NeolithicSweeps-20260920`. The task's delivered report/archive contain
the complete trajectories, effective configuration, overrides, source metadata,
breakthrough timings, failures and paired comparisons.
