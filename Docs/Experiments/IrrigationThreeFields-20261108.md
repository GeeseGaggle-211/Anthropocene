# Three-field irrigation: completed cloud prototype sweeps

Date: 8 November 2026. Status: diagnostic evidence, not a production canal implementation or live-town acceptance.

## Main conclusion

Irrigation is useful, but its connection to biology matters. For a controlled seven-tile footprint at q=0.40 with three fertile neighboring fields, three equally capable but dry fields (moisture 0.20), and an unchanged fertile center, bringing all three dry fields to effective moisture 0.50 increases repeatable total food by about **24%** at ten-year turns. The surrounding six fields alone gain **29.1%**.

Across 40 generated three-river/three-dry neighborhoods, changing only crop suitability yields a median **5.10%** gain. Generated dry tiles already have low biomass potential and renewal because natural moisture was baked into their profiles. A separately labelled sensitivity that lets the existing native biomass-productivity and renewal equations respond to supplied moisture increases the median gain to **20.95%**. No global yield multiplier was introduced. This stronger alternative remains a proposal requiring a production design decision.

## Execution and provenance

All simulations ran on a GitHub-hosted Ubuntu cloud runner with .NET 8. No simulation ran on the user's computer; no new GCP VM or change to active RL infrastructure was made.

- Production source: `07f3501c2d5e047e44d61e85cc0d4613b54572d6`.
- Experiment branch: `experiments/irrigation-three-fields-20261108`.
- Completed comprehensive run: https://github.com/GeeseGaggle-211/Anthropocene/actions/runs/42473845599 .
- Executed experiment commit: `2c69c5131edb86b07519b4d5257ac5ed8692923f`.
- Initial crop-only run: 42473379407, commit `ae438260cb47073e733d3325cd7f8698767a17e9`.
- Comprehensive artifact: ID 13614428713, `irrigation-three-fields-20261108`, 1,869,534 bytes; ZIP SHA256 `30f4fc0f1373ea9caec7ab700e6838acb299750bd554ee87af97497a24ac6387`.
- Native simulation/resource source diff was empty; the archive includes source/configuration hashes. Experiment harnesses, reports and workflows exist only on the isolated branch; no production merge occurred.

## 1. What the village fixture means

The footprint is the settlement center plus six adjacent cultivable tiles. The center is unchanged. Three neighboring tiles are well watered; the other three are the irrigation targets. All seven fields have mature, maintained physical cultivation q fixed at 0.40 or 0.50. This deliberately separates irrigation from paid development of previously uncultivated fields. Irrigation alone at q=0 does not establish cultivated land.

Two fixture families were used:

1. **Controlled profiles:** one generated rich river profile per seed is reused in an explicitly synthetic neighborhood. Three target tiles have starting moisture 0.20, 0.30 or 0.40 and retain 25%, 50% or 100% of the wet fields' biological potential. Each before/after pair holds that potential fixed. These ratios are sensitivity assumptions, not measured soil-fertility classes. There are 45 combinations across five seeds.
2. **Generated neighborhoods:** 40 real footprints, eight per seed, from 4,976 eligible candidates across seeds 11, 23, 47, 101 and 1729. Each has a river-bearing center, exactly three river-bearing cultivable neighbors with mean crop suitability >=0.80 and mean temperature >=8 C, and three non-river cultivable neighbors with mean suitability <0.80. The richer half by river-field biomass is sampled at deterministic dry-moisture quantiles. River adjacency is a floodplain proxy, not a measured alluvial-soil class. Some fields are seasonally dry rather than dry in every season. Footprints may overlap; these are alternative sites, not simultaneous settlements.

Crop-only irrigation uses the proposed service overlay:

`m_effective = m_natural + supplied_fraction * max(0, target_moisture - m_natural)`.

The main target is 0.50. Fractions 0/0.25/0.50/0.75/1.00 are tested, together with irrigating one, two or three targets. Native 20-, 15- and 10-year land-harvest semantics are retained. No Plough modifier, q_k change, direct food bonus, rainfall change or farming-radius expansion is included.

## 2. The requested favorable example

Reference: seed 11 controlled profile, q=0.40, ten-year intervals, target-field starting moisture 0.20, equal retained biological potential. Values are steady native receipts divided by physical years, with enough farm work to obtain the food ceiling.

| Neighboring dry fields fully irrigated | Total food/year, including unchanged center | Food-supported person-equivalents | Change |
|---|---:|---:|---:|
| None | 7,958.26 | 1,989.56 | baseline |
| One | 8,594.45 | 2,148.61 | +7.99% |
| Two | 9,230.63 | 2,307.66 | +15.99% |
| Three | 9,866.82 | 2,466.71 | +23.98% |

The center contributes 1,409.55 food/year in every row. Excluding it, the six neighbors rise from 6,548.71 to 8,457.27 food/year: +29.15%. This is not a doubling, because the dry tiles already supply food and the wet fields do not improve.

At the same favorable site, full three-field irrigation gives **+23.49%** at twenty-year turns and **+23.49%** at fifteen-year turns. Absolute food-supported capacities differ with the existing pulse convention: approximately 999 -> 1,233 at twenty years; 1,332 -> 1,644 at fifteen years; 1,990 -> 2,467 at ten years. These are physical food equivalents, not grown populations.

### Partial delivery to all three targets

| Share of the moisture deficit supplied | Total food/year | Whole-footprint gain |
|---|---:|---:|
| 0% | 7,958.26 | 0% |
| 25% | 8,308.61 | 4.40% |
| 50% | 8,727.48 | 9.67% |
| 75% | 9,246.89 | 16.19% |
| 100% | 9,866.82 | 23.98% |

This response is nonlinear because the source crop-suitability curve is quadratic below moisture 0.50. The same crop-only model saturates above that target; tests include targets 0.35, 0.40 and 0.60. Normalized moisture is not a measured volumetric water fraction.

### Existing biological potential changes the payoff

For starting target moisture 0.20, q=0.40 and ten-year turns:

| Target tiles' retained biological potential relative to wet fields | Gain after full irrigation |
|---|---:|
| 100% | 23.98% |
| 50% | 14.04% |
| 25% | 7.67% |

At equal biological potential, increasing starting moisture to 0.30 reduces the full-irrigation gain to 16.95%; at 0.40 it is 8.87%. Thus irrigation should favor a productive but water-limited site rather than give a universal bonus.

## 3. The native map exposes a frozen-biology limitation

In the 40 generated neighborhoods, median target biomass potential is only about **20.6%** of adjacent river fields. That is not evidence of intrinsically infertile soil: `FoodEcology.LandProductivity` directly includes moisture, warmth and relief, and `ConfigurePotential` saves the resulting biomass capacity. The land renewal rate also includes moisture during world generation.

Simply raising the `CropSuitability` input therefore leaves two earlier dry-climate penalties untouched. Four very arid generated cases have essentially zero gain in the crop-only treatment despite improved suitability; it cannot rescue their precomputed seasonal production envelope.

The follow-up holds natural profiles, rainfall, topography, original wild-food-capacity profiles and current stocks fixed, then tests two labelled local overlays:

- **Potential only:** recompute local `MaxPotentialFood` using the existing `LandBiomassBase * LandBiomassMultiplier * LandProductivity(effective_moisture, temperature, relief)`; do not inject stock or rerun world generation.
- **Potential plus renewal:** additionally recompute the native moisture-dependent land renewal rate, preserving any larger existing rate. No new arbitrary multiplier is introduced.

| Model, q=0.40 and ten-year intervals | Median matched gain | Middle half of gains | Median additional food-supported person-equivalents |
|---|---:|---:|---:|
| Crop suitability only | 5.10% | 3.16–6.43% | 69.27 |
| Suitability + responsive biological potential | 17.89% | 14.85–19.70% | 247.12 |
| Suitability + responsive potential and renewal | 20.95% | 19.12–22.25% | 267.28 |

These are paired-site descriptive medians, not confidence intervals; the median paired difference need not equal the difference between endpoint medians. At q=0.50, the full responsive model has a median +25.16% gain at ten-year intervals. Higher q is a separate fixed-field sensitivity, not free development caused by irrigation.

The secondary overlays are not automatically the approved production implementation. They show why a crop-only hook is insufficient if the design intent is to let water-limited ground become more biologically productive. A final implementation must define the irrigated area, managed versus wild ecological capacity, crop/grazing interactions and water demand rather than silently re-greening an entire tile or changing generated climate.

## 4. Extra output still requires harvesting work

In the favorable controlled case, obtaining all additional output requires approximately **178 more population-equivalent farm workers**: calculated overlapping farm work rises from 951.09 to 1,129.06. Maintenance remains 120 because q and equipment did not change; harvesting is the binding task. This is continuous labor accounting, not integer demographic workforce simulation.

With the original per-field Farmer budgets held fixed, the same full irrigation produces only **+4.44%**, supporting about 88 additional meal-equivalents, not the unconstrained +23.98% / 477.14. It can still gain a little because the native crop-share harvesting productivity differs from wild gathering.

For generated sites, the fully responsive model's median gain falls from **20.95% to 6.32%** under fixed per-field staffing. This supports testing the intended Plough harvesting-efficiency improvement alongside irrigation; no Plough effect was actually run here.

Fixed-staff rows use each field's baseline `max(harvest, maintenance)` allocation. Aggregate `FarmWorkers` columns are derived minimum requirements, not the held per-field allocated budget; their change is not evidence of new staffing in this arm. No field receives more than its fixed budget. The adaptive-ceiling rows instead price enough work to collect available production.

## 5. Finite water and the changing calendar

Separate 500-year fixed-capital trajectories use 20-year turns to year 200, fifteen-year turns to 350 and ten-year turns thereafter. Canals switch on at 200; a shutdown arm stops supply at 400. A native finite source supplies a background withdrawal plus the canal claim. Actual source debits, water recharge, closing stocks, delivered quantities and conveyance losses reconcile. Water is allocated once to the three recipients as deficit satisfaction, not copied into three new freshwater stocks.

The calibration between water units and soil-moisture service is not selected. Therefore the full irrigation requirement is swept relative to a stated source-renewal reference, with intake-capacity ratios 0.5/1 and transmission efficiencies 0.8/1. These are declared assumptions, not validated physical irrigation requirements or production defaults. The chosen intake envelope generally keeps total claims below gross source renewal; this is not a comprehensive source-depletion, drought or downstream-flow stress test.

For the favorable controlled case with 80% transmission, representative fulfilled-demand fractions of **100%, 80%, 40% and 20%** produce endpoint food-capacity gains of approximately **23.98%, 17.65%, 7.41% and 3.50%** respectively. The shutdown arm returns to the nonirrigated production regime after the existing opening harvest has cleared; it does not retain a permanent fertility grant.

These trajectories model horizon-average delivery and crop response, not field-scale infiltration, water-table dynamics, canal elevation feasibility, a full river-basin hydrology or a live demographic village. All delivered-water/maintenance values need integration with protected drinking needs and real local networks before production use.

## 6. Canal economics not yet calibrated

No canal construction recipe or recurring canal workforce was implemented. The raw tables include deductions for 15 and 30 total canal workers as opportunity-cost sensitivities only. Their `ExtraNonfarmHeadcountBeforeOtherNeeds` metric is the extra food-supported headcount minus additional minimum farm work at the ceiling; it is not actual specialist growth, a self-sufficiency test, or proof of net profitability.

Construction, tools, materials, upkeep, water entitlement, travel, housing, clothing, mortality, cohesion and source reserves must be paid in the subsequent full-economy experiment. A village can use the food gain for reserves, additional production, exports or demographic support; this diagnostic does not select its policy or cause it to reach 5,000 residents.

## 7. Recommended next implementation decision

Retain the user-facing rule: **connected maintained canals deliver water; supplied fields gain effective soil moisture**. Use explicit local irrigation state rather than modifying baseline rainfall or permanently overwriting generated moisture. The measured results favor letting managed biological potential respond as well, with the full potential/renewal model a candidate for review—not a granted yield bonus.

The first live-economy comparison should hold q_k, farming range and demographic/social parameters fixed and cross irrigation on/off with the separately specified Plough labor effect. Include actual canal construction/upkeep and finite water-to-moisture demand, single-claim water allocation, failures, seasonal drought, field service area and temporary supply loss. Test soil/productivity coupling explicitly rather than tuning one food multiplier to achieve a population target.

## 8. Validation and reproduction

The comprehensive artifact contains **8,055 static parameter rows**, **7,875 dynamic water/food boundary rows**, and records **3,398,040 native ecological integrations** across the baseline and labelled alternatives. All static lanes ran from empty and full stocks for at least 800 physical years, using the final eight horizons. The tested starting-state difference and tail range were zero at output precision. Maximum relative stock mass residual was **2.2202920651672378e-16**. The crop-only q=0 no-wild-food-bonus control passed. Endpoint center behavior, fixed q, nonnegative stocks and food <= biomass were checked.

The original crop-only cloud results were reproduced in the comprehensive run. Archive SHA, native source hashes and derived arithmetic/water ledgers were checked after download. These checks do not constitute the registered headless suite, a Unity playtest, full canal routing, fixed-year model invariance or a complete live settlement.

Reproduce the primary lane with:

```sh
dotnet run -c Release --project Tools/IrrigationThreeFields/Anthropocene.IrrigationThreeFields.csproj -- 11 TestResults/IrrigationThreeFields/11
```

Repeat for seeds 23, 47, 101 and 1729. The separately labelled biological-response comparison is:

```sh
python3 Tools/IrrigationThreeFields/RunBiologyComparison.py
```

The Python script writes an archived derived C# harness with explicit replacements and hashes; it does not modify production simulation files. Native source, both harnesses, derivation metadata, raw CSVs, fixture profiles, summaries and runtime/provenance are in the cloud artifact. Its GitHub retention is thirty days; the conversation evidence ZIP preserves the same bytes independently.

## Source map

- `Assets/_Anthropocene/Simulation/FoodEcology.cs`, lines 11–47: moisture-dependent saved biological potential.
- `Assets/_Anthropocene/Simulation/WorldGenerator.cs`, lines 180–207: native moisture-dependent renewal.
- `EcologyMath.CropSuitability`, `EffectiveWildFoodCapacity`, `ManagedRegenerationFlow`: crop response.
- `EcologicalPressure.Integrate` and `LazyEcology`: unchanged bounded stock and harvest kernel.
- `Tools/IrrigationThreeFields/Program.cs`: exact fixture definitions, treatments, budgets and conservation checks.
