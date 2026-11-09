# Irrigation-driven soil development: completed 600-year cloud sweep

Date: 9 November 2026. Status: isolated diagnostic, not production implementation or a live demographic town.

## Result

The proposed rule produces an immediate irrigation response, a gradual capacity-driven increase, and a plateau. At full qualifying irrigation and maintained q >= 0.40, soil development reaches its specified ceiling at year 500. Under the native opening-stock harvest rule, food receipts settle at the mature level around year 520. This timing is partly prescribed by the rule; the diagnostic tests its stock-conserved consequences, not the historical validity of a 500-year soil timescale.

For the middle sensitivity (50% additional capacity on the THREE target fields at maturity), the 40 generated locations have a median whole-footprint gain of 9.01% relative to water-only irrigation, and 31.98% relative to no irrigation at year 600. Three wet neighbors and the center are unchanged. Fixed initial field staffing captures only 0.123% additional food relative to the correspondingly staffed water-only arm: harvesting labor remains important.

## Provenance

- Unchanged production baseline: `07f3501c2d5e047e44d61e85cc0d4613b54572d6`.
- Experiment branch: `experiments/irrigation-soil-600y-20261109`.
- Executed commit: `d8836388f4cf7869b64394f18c06deedd8a3918b`.
- Successful GitHub-hosted cloud run: https://github.com/GeeseGaggle-211/Anthropocene/actions/runs/42586127428 .
- Artifact ID 13658888139; 7,235,953 bytes; SHA256 `e79190652631e931bff65751499d0033c99f54eea7f5ac9df09224b64d169721`.
- Five seeds: 11, 23, 47, 101, 1729. Forty generated three-wet/three-dry neighborhoods reused from the preceding sweep, plus five explicitly synthetic dry-desert reference fixtures.
- All simulation execution occurred in GitHub Actions; no user-device or active RL-machine execution, new GCP VM or production merge.

## Model and assumptions

Only the three irrigation-target tiles accumulate one persistent normalized development value a:

`a_next = min(1, a + dt_years * I * O * min(1, q/0.40) / 500)`.

q is actual physical cultivation, not the technology knee q_k. O=1 in the maintained fixed-field runs; q and q_k are not advanced or changed by irrigation. I is realized water-service fulfillment from a finite shared source, treated as qualifying sediment-bearing service. A separate sediment-poor control has water but no soil accumulation. This is a service proxy, NOT a conserved sediment-mass model or calibrated soil science.

The selected capacity/renewal irrigation mechanism is retained. Effective moisture changes the native land-productivity and renewal equations; rainfall, temperature, terrain and original natural wild-food capacity remain unchanged. The soil contribution modifies capacity, not inventory:

`B_water = native water-responsive capacity`

`B = B_water * (1 + g * a)`.

The terminal capacity gap was not specified by the preceding design. Therefore g=0.25, 0.50 and 1.00 are explicitly uncalibrated sensitivities: 25%, 50% or 100% additional target-field biomass capacity above the water-only profile after full development. g=0 is the water-only control. These are not percentage bonuses on final harvest or proposed production defaults. No biological stock is created by changing a. Current water still determines B_water, so retained soil development does not grant permanently wet land after canal failure.

The primary moisture target remains 0.50 for comparison with the earlier experiment. A separately reported target-1.00 lane prevents conflating that reference with universally adequate irrigation. Required water increases with the target's moisture deficit. Neither target nor water-to-moisture conversion is calibrated physical hydrology.

## Time schedules and initialization

Both schedules start with the same respective mature-q, unirrigated steady-state fields, not newly gifted biomass. Initial states were converged from empty and full stocks over at least 800 years. Irrigation then starts at year 0; soil starts at a=0.

- Fixed10: 60 ten-year turns, isolating the slow soil effect.
- Dynamic: 10 twenty-year turns to year 200, 10 fifteen-year turns to year 350, and 25 ten-year turns to year 600: 45 turns total.

Native land food is harvested once at each player boundary, followed by analytical recovery. Consequently the immediate water benefit appears in the following harvest, and shortening decisions produces separate steps in annualized food. A year-200 row describes the preceding 180-200 interval; the first 15-year receipt is reported at 215. The first 10-year receipt after year 350 is reported at 360.

Each production transition uses the exact interval mean of the capped-linear soil-development state to approximate changing capacity, NOT end-of-turn development retroactively over the entire interval. A slower recovery-only reference checks this approximation while preserving the same single harvest event.

## Main endpoint results: q=0.40, moisture target 0.50

Food-supported population is annual harvested food divided by 4 food/person/year, NOT simulated residents. Values are medians over 40 generated sites. Gains are medians of paired site ratios, so ratios of the displayed endpoint medians need not equal them.

| Treatment | Median food/person equivalents at year 600 | Gain vs matched water-only | Gain vs matched no irrigation |
|---|---:|---:|---:|
| No irrigation | 1,360.18 | -- | 0% |
| Water only | 1,644.92 | 0% | 20.95% |
| Soil: +25% mature target capacity | 1,718.33 | 4.51% | 26.44% |
| Soil: +50% mature target capacity | 1,791.74 | 9.01% | 31.98% |
| Soil: +100% mature target capacity | 1,938.57 | 18.00% | 42.86% |

Fixed and dynamic schedules have identical final medians here: by year 600 both use ten-year turns and have saturated development. Their paths and cumulative food differ. This is not a claim of full coupled-economy timestep invariance.

### Horizon of the middle (+50%) sensitivity

| Year | Development completed | Fixed10 food/person equivalents | Dynamic food/person equivalents |
|---|---:|---:|---:|
| 20 | 4% | 1,646.39 | 680.44 |
| 100 | 20% | 1,669.88 | 832.74 |
| 200 | 40% | 1,699.24 | 847.42 |
| 350 | 70% | 1,743.29 | 1,160.73 |
| 500 | 100% | 1,787.34 | 1,787.34 |
| 520 | 100% | 1,791.74 | 1,791.74 |
| 600 | 100% | 1,791.74 | 1,791.74 |

The fixed10 curve isolates a gradual almost-linear gain followed by a plateau. The dynamic curve adds large steps due to the existing harvest cadence; those steps are not soil-deposition windfalls.

## Local fields versus whole footprint

At generated sites, the median target-field output ratio versus the dry baseline is about 12.78x for water only and 18.81x for the middle soil sensitivity. The whole-footprint changes are smaller because four productive tiles do not improve.

A separately constructed seed-11 desert reference sets natural target moisture to 0.10 and computes its baseline capacity/renewal from the native dry-land equations; it does not retain the earlier artificial rich dry-field potential. The three targets supply 20.16 food/year without irrigation, 1,310.08 with water only, and 1,948.36 after full development in the middle sensitivity. The latter is 96.63x the near-barren baseline but only about 48.7% above water-only target output. Total seven-tile food goes 5,658.35 -> 6,948.26 -> 7,586.55 food/year, or +34.08% after both improvements. Do not attribute the entire 96.63x local change to soil alone.

## Water, q, labor, and interruption tests

- Half water supply: at q=.40 the soil value reaches 0.60 at year 600. In the middle capacity sensitivity, the median extra whole-footprint gain beyond matched half-supplied water-only is 1.59%. Water reduces both current productivity and soil-development speed.
- Lower actual q=.20: full water likewise reaches only 60% development in 600 years; full maturity would require 1,000 qualifying years under this specified rule. Higher q=.50 does not accelerate beyond the capped reference rate; it changes the underlying crop production.
- q=.50: water-only median food/person equivalents are 2,206.83, middle soil 2,436.24, and high soil 2,665.66. The corresponding middle/high total gains over no irrigation are 38.14% and 51.26%. Field q remains fixed; this experiment does not pay the cost of reaching it.
- No qualifying sediment: identical output to water-only and a=0, despite full water delivery. No irrigation and q=0 controls also accrue no soil development; the q=0 g=0/.50 trajectories match exactly.
- Stop water from years 350-450: soil progress stays at 0.70 during the interruption. Food falls after the already-available opening stock is collected. At year 450 median food capacity is about 1,363 person-equivalents; after restored production has reached the next receipt it is about 1,749 at year 470. Development reaches 1 at year 600 rather than 500, with receipts still catching up (1,787 versus continuous 1,792). The run stops at 600; later stabilization is not separately measured.
- Fixed staffing: the per-field workforce is fixed to the opening max(harvest,maintenance) budget. At q=.40, fixed10, water-only median output is 1,445.65 person-equivalents and middle soil 1,447.16. The median paired soil-only gain is 0.123%. This is not a simulated Plough effect; no Plough modifier is included.
- Target moisture 1.00: with full water, water-only median is 1,939.67 and middle soil 2,237.66 person-equivalents. Middle soil adds 15.26% beyond this matched water-only arm, or 64.48% relative to the same no-irrigation fields. This exposes the importance of the still-open water target; it does not select 1.00 or the +50% mature multiplier as production defaults.

## Water accounting and limits

The finite source is actually debited by a native background claim plus canal intake. Transmission is 0.80; target delivery is allocated once, not copied into new freshwater sources. Full field demand is an assumed source-relative function: 0.4 times the reference source rate, scaled by the summed target moisture deficit relative to the deficit to 1.00. Background demand is 0.2 times that reference. This intentionally supplies the full and half coverage treatments without claiming a physical irrigation-volume calibration. It is not a drought/depletion stress test or a downstream river model.

No canal construction/maintenance recipe, explicit sediment transport, hydraulic routing, erosion, salinity, grazing competition, new crop selection, housing, clothing, trade, demography, sanitation, civic capacity or fission is included. Farm labor is priced or fixed in diagnostic worker equivalents; it is not a full household labor/material closure. No tested output is a sustainable live settlement population.

## Validation

- 2,340 principal trajectories plus 16 reference trajectories; 123,690 total boundary rows.
- 1,454,040 native ecological integrations, including initial convergence and recovery references.
- Maximum relative stock mass residual: 2.220355283544969e-16.
- Source withdrawal equals delivery plus loss; independent analysis also reconciles source stock/recharge/background/overflow.
- Maximum soil-clock partition discrepancy: 1.1102230246251565e-16.
- On two seed-11 fixtures, two cadences and two soil gains, the mean-capacity transition differs from the quarter-year recovery-only reference by at most 0.36023% of whole-footprint annual food. Annual versus quarterly reference disagreement is at most 0.01373%. These are restricted approximation checks, not a proof for arbitrary nonlinear environments or all sites.
- The quarter-year diagnostic does not add intra-turn harvests or decisions. Slow loops are for verification, not proposed authoritative architecture.
- All 40 generated water-only year-600 results reproduce the prior capacity-and-renewal sweep within 3.4e-16 relative error.
- Independent CSV checks verify 600 elapsed years in every trajectory, bounded soil state, food units, target/unchanged partitions, no duplicate rows, and q=0 controls.
- All 80 native source hashes match the prior pinned evidence byte-for-byte; production-source diff is empty.

## Reproduction and next decision

At executed commit d8836388f4cf7869b64394f18c06deedd8a3918b:

```sh
python3 Tools/IrrigationSoil600/Run.py
```

The runner derives a new experimental main from the prior fixture-generation harness and compiles unchanged native Simulation sources. The artifact includes the derivation/hashes, C# additions, profile fixtures, per-seed rows/summary, configuration and runtime identity. Original GitHub artifact retention is thirty days.

The method produces the intended persistent, bounded development curve without a separate soil simulator. The unresolved balance decision is the mature capacity gap relative to equally supplied reference farmland. Select that gap and the water-sufficiency target from controlled comparisons before locking a production coefficient. Then test real harvesting/equipment/canal economics; the current fixed-staff result makes that next gate particularly important.
