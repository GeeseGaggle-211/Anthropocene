# Agriculture, herd valuation, and crop moisture

Status: historical investigation and proposal, followed by user-authorized
implementation on 1 October 2026. The calculations and source positions below
describe the preserved pre-moisture candidate. The implemented `.50` crop
curve, proportional harvest bonus, tool/stock-credit fixes, and attainable
fission support are documented in [CropHerdBalanceValidation.md](CropHerdBalanceValidation.md).
That report contains the five-world 25/50-turn population, QoL, herd and
matched performance comparisons. The biomass-first and finite-horizon herd
changes remain on `features/herd-ecology-retention`; pastoral balance is still
unresolved. The isolated previews below are distinct from the final combined
implementation and must not be substituted for its generated-world results.

## Finding

A crop-specific moisture curve is warranted for investigation. The current
biomass-first correction already makes dry land produce less biomass, but
cultivation converts the same proportion of that biomass into human food at
the same field intensity everywhere. Further reducing total biomass would
also remove the grazing resource. Penalizing the cultivated food increment
and managed food renewal addresses crop suitability while retaining the
existing moisture-limited pasture.

Moisture alone will not resolve all the observed agricultural advantage.
There is a discontinuous harvest bonus for establishing any field, optimistic
agricultural fission support, and inconsistent farm-tool assumptions. These
must be distinguished from tuning the ecological curve.

## Source trace and planner findings

1. **Crop accessibility has no additional moisture constraint.**
   `FoodEcology.LandProductivity` (lines 11–18) now sets biomass from local soil
   moisture, warmth, and relief. `WorldGenerator.VegetationResourceStage`
   (line 197) also reduces renewal with moisture. However,
   `EcologyMath.EffectiveWildFoodCapacity` (lines 67–71) returns
   `G_retained + (B - G_retained) * q²`, and `ManagedRegenerationFlow`
   (lines 73–74) adds `.65 * q * B * biomassRecovery * operation`.
   Neither introduces a separate crop-suitability response. At `q=.4`, before
   vegetation displacement begins, edible capacity is 18.1% of biomass for
   natural accessibility .025, 20.2% for .05, and 26.5% for .125. These are
   respectively 7.24×, 4.04×, and 2.12× their uncultivated capacities. Absolute
   dry-land output is still lower, but the relative cultivation gain is large.

2. **Any positive field enables the full harvesting bonus. Design shortcut.**
   `CampProductionDemand.Build` (line 318) sets `Established` when any
   operated tile has positive capital. `EconomicPlanner.Plan` (lines 501–510)
   then staffs at the full farmer rate, currently 10.8 food per worker-year
   versus gathering's 7.2. `TurnResolver.AddAgriculturalClaims`, in
   `GenerationalEconomy.cs` (lines 479–505), explicitly lets farmers harvest
   the same accessible land food as gatherers, including natural wild food.
   The bonus does not scale with cultivated area, crop share, or intensity.
   In the controlled wet tropical case, seven fields at `q=.000001` cost
   about `7.5e-10` maintenance workers but increase the receipts of 100 food
   workers from 720 to 1,080 per year. Accessible food barely changes.
   Selected retention falls from 26 animals to 18 in that comparison.
   This is a planner/resolver-consistent rule, not a forecast parity bug.

3. **Farm-tool coverage is inconsistent. Confirmed planning discrepancy.**
   `CultivationEconomy.PlannedMaintenanceFarmers` (lines 297–298) divides
   upkeep by stone-tool efficiency regardless of actual starting Farm Tools.
   `EconomicPlanner.Plan` calls it when sizing prospective fields (line 294).
   In contrast, `MaintenanceFarmersForPlanning` (lines 300–307) checks stock,
   and `CalculateLabor` (lines 228–245) requires real tool coverage and clamps
   expansion after maintenance. With 404 residents, 200 goats, zero Farm Tools,
   and seven grassland fields at `q=.5`, the planner assigns 371 farmers and
   reserves 78 for expansion. Actual maintenance requires 370.5053 farmers;
   the resolver leaves only 0.4947 for expansion. Tool efficiency is
   `(.45/.40)² = 1.265625`, which explains the optimistic reservation.
   Tools manufactured later cannot cover starting field work
   (`FarmToolWorkBudget` and `PrepareHorizon`). Align this reservation with
   the actual covered/uncovered work calculation.

4. **Stored food is credited twice in part of the new herd calculation.
   Confirmed arithmetic error in the retained working candidate.**
   `PlanningFoodDemand`, in `GenerationalEconomy.cs` (lines 761–765), already
   returns the incoming consumption/reserve/growth demand after inventory
   credit in `FoodBucketTargets` (lines 726–731).
   `HerdLabour.Build` (line 373) subtracts `c.Food` again when computing
   current harvest savings. An isolated correction removes that subtraction.
   It changes labour-saving estimates in both directions depending on whether
   local harvest is already supply-limited; it does not consistently favor
   farming. None of the 60 controlled cases changed selected herder count,
   and the matched camp-1415 checkpoint still selects two herders and five
   retained animals. Fix it, but do not claim it explains the remaining
   pasture/crop balance.

5. **Field maintenance is an unpriced prior commitment. Optimization limit.**
   `EconomicPlanner.Plan` (lines 506–507, 544) reserves the greater of upkeep
   and harvest work. It does not compare a priced choice to reduce upkeep,
   accept capital decay, and retain more animals. The physical resolver allows
   partial operation and decay (`CultivationEconomy.CalculateLabor`,
   `CommitOperatingFraction`, and `Decay`). This makes high-maintenance fields
   difficult to displace economically. Upkeep and harvesting intentionally
   overlap; simply adding the two would change the labor contract.

6. **Agricultural fission assumes prospective productivity. Settlement design
   discrepancy, outside `EconomicPlanner` itself.**
   `Fission.RegimeOf` (lines 260–261) classifies any camp with Cultivation as
   agricultural, including a husbandry-primary camp with secondary Cultivation.
   `Fission.Desirability` (lines 470–483) then adds the gross managed renewal
   of a radius-one footprint at primitive `q=.4` for the entire turn. It does
   not establish a field-investment plan or charge its construction/maintenance
   and tools. The labor ceiling is applied to desirability; the separate support
   ratio used for estimated daughter food uses gross supply. The function
   documents itself as an estimate, not a realized-harvest forecast.
   With current land resolution, humans harvest opening stock once and renewal
   fills the bounded pool for a later turn. Gross renewal is therefore not
   automatically usable current food. As a diagnostic illustration, the wet
   tropical fixture's wild stock plus the assumed managed source is about
   9,968 food/year across seven tiles, whereas the opening accessible food of
   already established `.4` fields is about 3,330/year. The grassland quantities
   are about 1,265 versus 745. These are arithmetic support comparisons, not
   measured fission outcomes. A moisture modifier should also reach this
   estimate, but it will not repair the gross-versus-usable distinction.

## The `.4` soft barrier is already present

The user correctly recalled the breakpoint. `CultivationEconomy.Intensity`
sets `q=.4*sqrt(E)`, while `EffortForCapital` charges the exponential continuation
above `E=1`. `CultivationMarginalEffortMultiplierPerTenth=16` is the current
configuration. Both build and maintenance use that effort, not just `q²`:

| q | Effort weight | Maintenance workers, seven open tiles without Farm Tools |
| ---: | ---: | ---: |
| .2 | .25 | 25 |
| .4 | 1 | 100 |
| .5 | 3.7051 | 370.5 |
| .6 | 46.9859 | 4,698.6 |

Woodland/forest add their existing multipliers. This is a soft barrier, not a
hard cap, and it is already used in field investment scoring. Much of the
observed agricultural advantage happens below it; raising the above-.4 cost
again would leave the tiny-field bonus and weakly moisture-constrained crop
accessibility in place. A nomadic camp avoids these field costs but still pays
herding work, tool replacement, and pasture/water constraints.

At current primitive management, one herder supports two animals producing
eight food/year when fully nourished. Stone coverage raises this to 2.69
animals and 10.76 food/year, close to the farmer's 10.8. This comparison is a
labor capacity comparison; scarce biomass, crop stock, water, and typed inputs
still bound actual receipts.

## Proposed ecological response

Preserve the current biomass and natural-food generation:

```
B_s = B0 * M_s * W_s * relief
G_s = B_s * natural_accessibility
A_s = min(1, (M_s / M_reference)^p)
F_s(q) = G_retained,s(q) + A_s * (B_s - G_retained,s(q)) * q²
managed_flow_s = A_s * .65 * q * B_s * biomass_recovery_s * operation
```

`M_s` is actual seasonal local soil moisture, including river/oasis subsidies.
Drinking-water access by itself does not improve crop suitability. Apply the
same response to the crop increment **and** managed renewal; otherwise one
path bypasses the moisture constraint. Keep actual `q` for field costs and
vegetation displacement. Do not lower nominal `q` to implement the penalty.

Test `p=2` and `M_reference=.35` or `.50` as candidates. These are game-model
calibration choices, not biological thresholds established by external data.
`.35` makes a gentler distinction and affects fewer moderately moist sites;
`.50` provides a stronger grassland penalty. Use `.50` as the stronger test
candidate before accepting either. Wet tropical soil at `.75` and a desert
river footprint with local soil `.85` have `A=1` under both; both retain their
current food. This should create advantages according to actual moisture,
rather than a hard-coded biome preference.

## Controlled results

240 cases: four isolated source variants, each covering six sites, five field
intensities (`0`, `.000001`, `.2`, `.4`, `.5`), and two starting herds (22 and
200). Each uses 404 residents, seven identical local tiles, all four seasons
at 25 C, controlled soil moisture, 38,487 stored plant food, ten Herding Tools,
zero Farm Tools, and otherwise identical starting technology/materials. Drinking
water is available except in the explicitly dry-water control. Stocks begin at
90% of the matching accessible capacity. These are synthetic diagnostics,
not regenerated-world survival or population-balance results.

At `q=.4`, with 22 animals:

| Site / local moisture | Current food capacity per tile | `.35` curve | `.50` curve |
| --- | ---: | ---: | ---: |
| Dry desert / .03 | 288.82 | 41.72 | 40.79 |
| Less arid desert / .10 | 962.75 | 200.71 | 166.17 |
| Grassland / .22 | 2,363.78 | 1,287.86 | 929.45 |
| Fertile tropical / .75 | 10,571.59 | unchanged | unchanged |
| Desert river / .85 | 11,981.14 | unchanged | unchanged |

The `.50` curve lowers the grassland capacity by 60.7%. Its grazing biomass is
unchanged at 11,701.89 per tile. The fully managed 22-animal output remains
88 food/year in the water-supplied cases. Their nominal retention remains
27 on grassland and dry land, allowing current stock to grow; the wet tropical
and river cases retain eight. The no-drinking-water desert produces zero
sustained animal food and retains only the constrained minimum target of two.
There is no promise that fewer crops can support a larger human population
without sufficient starting animals and pasture water.

With 200 starting animals on `.4` grassland, the current planner selects 108
herders, target 222, and 222 farmers with 122 reserved for expansion. Both
moisture candidates select 122 herders, target 249, and 195 farmers with 95
reserved for expansion. Milk remains 800/year in all three because current
200-animal management is already complete. The change reserves more capacity
for herd growth; it does not produce additional current animals.

The previews preserve biomass and full-management animal output in all 120
crop comparisons, preserve physical wild food at `q=0`, preserve tested wet
sites' plans, and maintain `0 <= accessible food <= biomass`: 632 assertions.
At `q=0`, field-investment plans may legitimately change because prospective
fields are now less productive even though current wild food is unchanged.

The earlier five-world biomass audit's fertile anchors include moderately
moist land: 13.3–31.8% of tiles have **mean** moisture below .5, versus 1.8–7.3%
below .35. These mean-profile percentages are context only; a seasonal modifier
must be evaluated with actual four-season profiles. Preserving natural fertile
food does not prove every cultivated-land quantile is preserved. Quantify those
changes before accepting a threshold.

## Next work and model contract

First repair current-stock credit and starting-tool labor parity. Then test the
crop-moisture curve while removing or replacing the positive-capital harvesting
shortcut and making agricultural fission use achievable support. Diagnose each
change separately before a combined balance audit. A small field should not
grant the full productivity of established agriculture on every accessible
wild-food tile. The replacement bonus needs an explicit crop/field service
definition shared by planning and resolution; no arbitrary herd quota is needed.

Authoritative state stays in existing seasonal moisture, field capital,
biomass/food stocks, and operating fractions. Proposed threshold/exponent
settings must be saved with legacy-neutral migration. Turn boundaries and the
single harvest contract stay fixed. One shared seasonal response should feed
`EcologyCapacity`, stock reclassification, field forecasts, observations, and
fission support; do not insert a planner-only discount. The analytical kernels
remain duration-independent. Added work scales with active local field/profile
evaluations (four profiles); no yearly or individual-animal loop is required.
Benchmark mature settlements and RL batches before accepting the mechanic.

Acceptance needs conservation, zero/low/high moisture, seasonal drought,
river/oasis versus drinking-only water, `q=0` compatibility, crop/renewal parity,
tool coverage, field abandonment, scalar/worker/reordered/save continuation,
and mortality-plus-fission generated worlds. Report herd animals per resident,
sustained animal receipts, farmland intensity, resident/food-worker allocation,
starvation, daughter viability, and runtime by actual moisture and water access.
Husbandry technology adoption alone is insufficient evidence of a pastoral
economy. No new generated-world or Unity validation is claimed by this audit.

## Reproduce and preservation

Activate the cloud SDK and run:

```bash
source /workspace/anthropocene-cloud/activate.sh
python3 Tools/PerformanceAudit/Run-MoistureCurveAudit.py
```

The runner snapshots current simulation sources under ignored
`TestResults/MoistureCurveAudit`, compiles Release probes, and patches only
those copies. Variants are `current`, `stock-credit-fixed`, `crop-m035`, and
`crop-m050`. JSONL rows expose receipts, capacity, planned and resolver-shaped
workforce, and herd choices. `provenance.json` records SHA-256 hashes for every
source in each variant and verifies production-source preservation.
`checks.json` records the 632 passed checks. The stock-credit checkpoint at
camp 1415 is a separate ignored diagnostic. The previous broad headless pass
remains applicable to the preserved production candidate; this turn changes
only diagnostics and documents. These previews are deliberately not installed
as new production defaults.
