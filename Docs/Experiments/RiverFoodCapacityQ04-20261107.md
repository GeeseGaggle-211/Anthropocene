# River-valley food capacity at q = 0.40

## Result and authority

Executed 7 November 2026. A sampled fertile seven-tile river-valley footprint at fixed mature q=0.40 supports a median **1,189.30 people-equivalent of meals**, with P25-P75 **1,144.27-1,233.35**. The production Desert river valley preset (seed 11, center 223410) supports **883.05**. These are repeatable food-only capacities under the current twenty-year harvest convention, not simulated village population trajectories or historical density estimates.

Production source baseline: `07f3501c2d5e047e44d61e85cc0d4613b54572d6`.
Executed experiment commit: `7f94c10f7855b20a5a27abb50bef5d178a6830a3`.
Experiment branch: `experiments/river-food-capacity-q04-20261107`.
[Successful GitHub Actions run 42353950216](https://github.com/GeeseGaggle-211/Anthropocene/actions/runs/42353950216), job 128347822959.
Artifact: `river-food-capacity-q04`, ID 13566822504; ZIP SHA-256 `83f57fbb76507d81593f963fe970e6fea8d393445cc8e6786292a713e86e08ac`.

A workflow diff check confirms all production simulation source and resource configuration files are unchanged from the baseline. Main, RL branches, active training and production configuration were not modified. Only the isolated experiment harness/workflow and this report were added. This report is diagnostic evidence, not authorization to change calibration.

## 1. What was measured

The harness compiles the repository's actual C# `Simulation/*.cs` and calls `EcologicalPressure.Integrate`. It does not replace the ecology with a Python model or annualized yield estimate. This is a direct native ecological-kernel experiment, not a full `TurnPlanner`/`TurnResolver`/DemandControl census.

Five fresh production-size Continents worlds were generated from the shipped opening and resource-generation configurations: 1024x512 tiles, 80 founding tribes, seeds 11, 23, 47, 101 and 1729. Locations are hypothetical mature settlement sites, not exclusively the generator's selected viable player starts.

Each tested footprint contains the center and its six neighbors, all cultivable land, with a river-bearing center. Every tile is held at the same q. At q=0.40, each tile has E=1. Operation is fully maintained; no further field expansion or settlement growth is simulated. Native climate, seasonal moisture, crop suitability, biomass capacity and regeneration remain active. LandCondition is 1; the current land-use kernel does not introduce progressive soil damage.

Excluded: clothing, housing, disease, crowding, fertility, demographic growth, fission, immigration, trade, livestock, fishing, competing resource claims, external food, nonfood crafting/extraction labor and establishment costs. Farm Tools are not granted. Drinking-water supply is not separately imposed as a limit; this is explicitly a food-only result. Soil moisture's crop constraint remains in force.

## 2. Cohorts and sampling

Across five worlds there were 148,721 river-center seven-cultivable-tile footprints. Of these, 110,052 had footprint-average seasonal crop suitability at least 0.80. Within each world's qualifying moist group, the upper quartile by summed natural mean biomass defines the fertile cohort: 27,516 potential locations total.

Sixteen deterministically spaced biomass-quantile sites, including endpoints, were selected per cohort per world. There are 80 fertile selections, 80 all-river selections, 80 warm-desert-river selections, and one preset selection: 241 site/cohort selections at 236 distinct centers. Footprints can overlap. Reported quantiles describe the tested selections; they are not random-sample confidence intervals or an exhaustive census of sustainable capacities.

The warm-desert group requires a Desert center and footprint mean temperature at least 18 C. It is separate from the high-biomass, well-watered fertile group. The preset has five river-bearing tiles in its seven-tile footprint, mean seasonal crop suitability 0.7243, and mean temperature 24.25 C. Calling a location a river valley does not make all seven tiles maximally productive.

## 3. Sustainable-yield protocol

Each tile/setting runs for 40 native twenty-year horizons (800 years), independently from both an empty food/biomass state and the generated stocked state. The last eight horizons supply the steady receipt. Both initial conditions must converge to the same yield, and tail variation must be negligible. Thus the result is not funded by one-time opening biomass.

The native land kernel harvests the opening accessible stock, then analytically regenerates it. The ceiling lane harvests all opening accessible food; this does not harvest all total ecological biomass. Additional q=0.40 lanes leave 20% or 50% of opening accessible food unharvested. These are explicit sensitivity scenarios, not calibrated safety policies, reserve inventories or bad-year probabilities.

Default consumption is 1 food per 91.25-day reference interval. A 7,300-day turn therefore requires 80 food per person, equivalent to 4 food per person-year:

`P_food = steady Food per turn / (Consumption * TurnDays / BaseDays) = H / 80`.

The twenty-year harvest abstraction is deliberately preserved. Multiplying gross annual regeneration by twenty would answer a different question and overstate what this implementation permits residents to collect.

## 4. Main results at q=0.40

Full accessible-stock harvest, production seasonal boundary:

| Location cohort | n | Minimum | P25 | Median | P75 | Maximum |
|---|---:|---:|---:|---:|---:|---:|
| Fertile upper-quartile river footprints | 80 | 948.39 | 1,144.27 | **1,189.30** | 1,233.35 | 1,233.35 |
| Warm desert river footprints | 80 | 351.96 | 758.09 | **872.62** | 1,082.41 | 1,233.35 |
| Desert river valley preset | 1 | 883.05 | 883.05 | **883.05** | 883.05 | 883.05 |
| All sampled river footprints | 80 | 3.96 | 269.10 | 628.58 | 981.35 | 1,233.35 |

All fertile and warm-desert q=0.40 samples are feasible under the calculated native harvesting/maintenance labor budget. Twelve of the 80 all-river samples cannot fund maintenance of seven q=0.40 fields from their own food-supported population. Their physical food figures must not be advertised as self-sustaining seven-field settlements.

Fertile per-world medians are 1,220.14 (seed 11), 1,233.35 (23), 1,195.67 (47), 1,155.94 (101), and 1,144.29 (1729).

### Retained accessible-stock sensitivity

| Opening accessible food left unharvested | Fertile median | Fertile P25-P75 | Warm-desert median | Preset |
|---|---:|---:|---:|---:|
| 0%: ecological food ceiling | **1,189.30** | 1,144.27-1,233.35 | 872.62 | 883.05 |
| 20% | **951.44** | 915.42-986.68 | 698.10 | 706.44 |
| 50% | **594.65** | 572.14-616.68 | 436.31 | 441.52 |

The near-proportional response follows this tested recovery regime; it is not a universal sustainability rule. A 20% unharvested-stock scenario does not establish resilience to drought or other unmodeled shocks.

## 5. Labor and the meaning of agricultural food

The native source's crop-share-weighted harvest rate and maintenance effort law are evaluated separately from the ecology run. At q=0.40, the fertile median requires **544.37 population-equivalent harvest workers**, or **45.77%** of the food-supported population. Maintenance requires 100-120 workers across these footprints (median 120). Maintenance workers also harvest under the existing contract, so the relevant requirement is `max(harvest workers, maintenance workers)`, not their sum. All 80 fertile cases pass this labor feasibility check without Farm Tools. This is continuous population-equivalent accounting, not a measured integer DemandControl allocation or a real-world adult participation rate.

The headline capacity is **total local terrestrial PlantFood**, including crops and remaining edible wild plants. The native pool does not store separately conserved crop and wild harvest inventories. Applying the source's opening-capacity crop fraction attributes a median **628.31 people-equivalent** to the crop component at q=0.40. That is an attribution, not a separate crop-only run.

On the same fertile sites, q=0 yields a median **533.76** people-equivalent. The median matched-site increase from q=0 to q=0.40 is **658.17**, and the median matched ratio is **2.239x**. The increase and the crop-share attribution are different quantities because management also changes recovery of the common stock.

## 6. Intensity sensitivity on the same fertile sites

Full harvest, production phase, no Farm Tools:

| q | Median food capacity | P25-P75 | Median maintenance workers |
|---|---:|---:|---:|
| 0.00 | 533.76 | 509.86-550.80 | 0.00 |
| 0.25 | 806.43 | 775.89-836.29 | 46.88 |
| 0.35 | 1,042.04 | 1,002.59-1,080.64 | 91.88 |
| **0.40** | **1,189.30** | **1,144.27-1,233.35** | **120.00** |
| 0.45 | 1,356.20 | 1,304.85-1,406.43 | 184.92 |
| 0.50 | 1,542.73 | 1,484.31-1,599.87 | 444.61 |

These are fixed mature-capital comparisons, not evidence that the current planner will build these fields or that higher-tier technologies already exist. Above q=0.40 the existing effort curve raises maintenance sharply. At these fertile food ceilings, harvest labor still exceeds maintenance through q=0.50; maintenance and harvest must not be charged twice.

The q=0.40 fertile median stays between 1,189.30 and 1,189.50 over all four starting seasonal phases. Individual footprints have up to 8.74% peak-to-trough variation relative to phase zero. The headline uses the production boundary, not whichever season maximizes yield.

## 7. Validation and independent result checks

The run completed 1,444,960 native tile integrations and emitted 2,651 site/scenario rows across 44 summary groups.

- Maximum relative food/biomass conservation residual: `2.802469790985242e-16`.
- Maximum relative steady-yield difference between empty and stocked starts: `4.741863276228782e-15`.
- Maximum relative variation across the final eight horizons: `1.3313693044796225e-14`.
- Every integration checked nonnegative stock, accessible food <= biomass and unchanged fixed capital.
- Post-download checks independently recomputed all 44 groups from CSV, verified food/person units and overlapping labor arithmetic, and verified all 80 native source hashes plus the artifact ZIP hash.

These checks validate this diagnostic's arithmetic and native kernel usage. They do not constitute a fresh full headless suite, Unity playtest, reserve-economy validation, population-growth run, or mature-game performance benchmark.

## 8. Design implications, not new authorizations

The current q=0.40 seven-tile fertile food envelope is roughly 1,100-1,200 residents, not several thousand. The named desert-river preset is nearer 880. Removing mortality and nonfood obligations cannot by itself make this unchanged local footprint feed a much larger population.

The result supports separating future technology effects: Plough should be assessed against field work and attainable capital; Irrigation against physically available water and crop suitability; and logistics/trade against access to additional non-overlapping food-producing land. The existing crop-suitability response saturates, so a wet fertile location should not automatically receive an unrestricted Irrigation yield multiplier. This report selects no new technology effect or calibration.

Remaining progression is tracked by issue #9 (Kiln Firing, Plough, Irrigation, Sailing) and #10 (Copper Working and Copper Tools). `NeolithicTech` also declares WheelAndAxle as convergence knowledge. Sailing is already within the separate Fishing handoff. Useful-work progression, concrete effects and save/observation/AI integration should be accepted separately from the presence of a technology identifier.

## 9. Reproduction and source map

At the executed experiment commit:

```sh
dotnet run -c Release --project Tools/RiverCapacitySweep/Anthropocene.RiverCapacitySweep.csproj -- TestResults/RiverCapacitySweep
```

The evidence bundle includes `rows.csv`, `rows.json`, `summary.json`, five native `profiles-<seed>.json` fixture inventories, runtime information, exact experiment SHA, the harness/project, 80 native simulation files and both shipped configuration files. `summary.json` contains source/config hashes. GitHub artifact retention is fourteen days; the downloaded ZIP is also supplied with the conversation.

Relevant unchanged source at the production baseline: `CultivationEconomy.cs` for intensity, effort, footprint and overlapping harvest/maintenance; `EcologyMath.cs` for crop suitability and accessible capacity; `LazyEcology.cs` for seasonal profiles and land recovery; `EcologicalPressure.cs` for authoritative harvest/stock conservation; `PlaytestScenarios.cs` for the desert-river preset. No code in these files was changed by this experiment.
