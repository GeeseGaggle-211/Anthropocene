# Analytical annual land food experiment

This opt-in experiment follows PR #51 and the [civic population/irrigation audit](CivicIrrigationTrajectoryAudit.md). It removes the rule that a land tile can supply only its opening food stock during a multi-year turn. Enable `SimulationConfig.AnnualLandFoodEnabled`, or use `Assets/_Anthropocene/Resources/AnnualLandFoodOpeningConfig.json` (the civic opening with that flag enabled). The shipped opening and old saves remain harvest-first.

## State and equations

The authoritative state remains `Tile.Food`, `Tile.PotentialFood`, cultivation capital, and the ecology timestamp. Food is the accessible portion of the slower biomass reserve; cultivation can change accessibility without creating biomass. No annual cohorts, seasonal inventories, dense tile arrays or sub-turn authoritative writes are added.

Cultivated supply is a separate **flow**, using the existing `CultivationRegenFlux`, field intensity, operating fraction, moisture response, biomass capacity and recovery coefficient. Irrigation affects those same inputs through its finite-water overlay. This experiment does not recalibrate the yield multiplier or assume that the opening stock is harvested once again every year.

For fixed forcing over a horizon of `T` physical days:

```
g = managed crop renewal per day
h = finite requested harvest / T
crop harvest rate = min(g, h)
reserve demand d = max(0, h - g)
dS/dt = r * (K - S) - d, with 0 <= S <= K
```

The reserve has the exponential solution `S(t) = K - d/r + (S(0) - K + d/r) exp(-rt)` until it reaches zero. After depletion, harvest is limited to `g + rK`; unmet demand is recorded in that same turn. The `r = 0` case is a finite linear drawdown. Excess crop yield expires rather than replenishing the reserve. It is reported as input and overflow, preserving `opening + recharge = closing + harvest + overflow` for food and total biomass.

Herds use nonfood biomass first. Its analytical depletion time splits the food calculation into at most two phases; grazing then competes for accessible food, with human food taking priority. There is no annual iteration. An empty reserve recovers naturally, including on an inactive tile, using the same zero-demand transition as an active tile.

The four climate profiles are reduced to equal annual weights. Rates are averaged and reserve capacities are rate-weighted so the source `rK` is preserved. Fractional years use the same annual mean. This deliberately smooths seasonal shortages; it does not model discrete harvest dates or within-year food storage. Endpoint capacities, observations, forecasts and invariants use that same annual definition.

## Population and planning

Physical demography already integrates `personYears = integral N(t) dt`. Actual meal consumption is `foodPerPersonYear * personYears * nutrition`, bounded by available inventory, with unused food refunded. Births and deaths therefore change food consumption within a long turn. The annual mode requires physical demography and reuses that solver; it does not debit the opening headcount for twenty identical years or charge meals twice.

Harvest and consumption remain distinct. The one-way turn contract fixes labor/tool/water-constrained harvest claims before demography; claims can also fill explicit reserves and manufacturing demand. Ecology spreads each fixed harvest claim uniformly over elapsed physical time. This is an annual-mean production model, **not** a joint continuous solution in which instantaneous `N(t)` replans tile extraction each year. Inventories connect the finite harvest budget to the person-year meal debit. Changing policy, capital, competition or demographic services between shorter turns can still change results.

Planner availability includes within-horizon renewal. The cached settlement/current-food envelope, new-site placement and fission support estimates use the same annual model, with repeatable support given by the exact empty-reserve flow `rK + g`. Cultivation and irrigation investment comparisons include recurring crop yield as well as retained accessible stock. These are potential supply estimates; they never bypass finite harvesting labor, tools or delivered water.

## Scaling and compatibility

- Four climate profiles per capacity evaluation and at most two food-depletion phases per active tile, independent of elapsed years, turn length or population headcount. Capital changes add bounded capacity evaluations for accessibility reclassification. Existing canonical claim sorting/allocation costs still follow claim count.
- Capital averaging, local footprints and planner caches retain their existing bounded approximations. There is no new world-wide scan in the production path and no work per resident or per year.
- Existing tile/save and RL observation layouts remain unchanged. A saved optional boolean selects the experiment; absent/false retains the old behavior. Serialized fingerprints include the new field, so cross-version parity compares decoded states reserialized by the same binary.
- Turning the mode on projects/clamps old seasonal stocks to the annual capacity on first access. There is no compensating grant of food. This intervention transient is part of the matched continuation experiment.
- This does not eliminate total cost from taking more player turns: each turn still plans and resolves. It eliminates annual catch-up loops and repeated opening-stock harvest as a source of artificial productivity.

## Validation and reproduction

Build `Tools/Headless/Anthropocene.Headless.csproj` and run `--annual-land-food-only`. The checks compare the closed form with an independent fine-step reference, test finite claims and depletion, conservation, grazing, one 20-year interval versus twenty annual intervals under fixed forcing, passive/active parity, field-capital changes, forecast purity, person-year consumption, legacy flag migration, native persistence and duration-independent work/allocation.

Build `Tools/CivicGovernanceSweep/Anthropocene.CivicGovernanceSweep.csproj` and run:

```
--annual-food-experiment --output=TestResults/AnnualLandFood/run
```

This resumes each of the five audit's native year-1,000 full-treatment saves in three arms: legacy, annual, and annual with the then-scheduled turn duration frozen. It continues to the original year-2,000 endpoint, records every boundary, checks invariants, and compares a live next turn with a save/reload next turn. The legacy arm must reproduce the decoded original endpoint exactly. Generate the prerequisite audit saves using [the audit instructions](CivicIrrigationTrajectoryAudit.md).

## Measured outcomes, 28 November 2026

The five seeds resume the same native full-civic saves at approximately year 1,000 and continue to approximately year 2,000. No population, technology, demographic coefficient or irrigation gate is changed. The fixed arms freeze the already scheduled 7.904-year duration; the normal smoothed calendar reaches approximately 4.435 years at the end. Endpoint times differ by at most one turn. Largest villages:

| Seed | Original, shrinking turns | Original, frozen turns¹ | Annual, shrinking turns | Annual, frozen turns | Annual late growth, residents/century² |
| --- | ---: | ---: | ---: | ---: | ---: |
| 11 | 7,939 | 4,468 | 8,223 | 8,207 | +12.8 |
| 23 | 7,389 | 3,982 | 7,521 | 7,534 | +26.8 |
| 47 | 7,485 | 4,382 | 8,216 | 8,454 | +2.0 |
| 101 | 8,130 | 4,222 | 8,237 | 8,229 | +2.4 |
| 1729 | 6,937 | 4,080 | 7,509 | 7,792 | −2.3 |

¹ Frozen-original controls are from the preceding [audit](CivicIrrigationTrajectoryAudit.md). ² OLS slope over the last 200 physical years, tracking the changing maximum village, not a fixed village ID.

The annual model supports the target population range under either calendar. The largest endpoints differ by less than 3.8% between its two calendar arms, compared with the much larger original dependence. Whole-world population sums are 482,999 with shrinking turns and 480,665 with frozen turns, versus 391,077 in the shrinking-turn legacy controls. These are five compact 96×48 worlds; the inherited checkpoint includes the earlier Plough/Irrigation knowledge grant.

Late growth is mostly a plateau: four annual arms have small positive slopes, and seed 1729 is slightly negative. This is not continuing rapid growth. Irrigated fields total **34 → 35** in both the annual and legacy treatments, with the one new field in different seeds. The food transition addresses the calendar artifact; sparse new irrigation adoption remains a separate planner/opportunity question. The irrigated-versus-dry kernel fixture confirms that delivered water increases annual cultivated supply, but does not establish that every settlement has a worthwhile new canal route.

The [summary and provenance](Experiments/AnnualLandFood-20261128-summary.json) and [trajectory samples](Experiments/AnnualLandFood-20261128-trajectories.csv) retain the exact run identities and boundary measurements. `SummarizeAnnualFood.py` reproduces these from the raw outputs. All 15 final runs pass invariants and live/save/reload continuation checks. All five legacy continuations exactly reproduce the original decoded endpoints. The allocation-free quiet specialization also preserves all 15 pre-optimization native endpoint saves byte-for-byte.

Validation passed:

- **1,927 annual-model assertions**, including an independent fine-step reference, fixed-forcing partition equivalence, population-year meal debit, contested allocation, operation/irrigation response, passive recovery under capital decay, native save compatibility and 20 generated turns with scalar/four-worker replay.
- Existing Neolithic/food/tool/husbandry/forecast checks; 11,589 physical-demography, 1,002 civic, 337 agriculture, 163 DemandControl, 54 save-codec, 680 fission, 720 shared-allocation, 42 settlement, 34,222 agent-contract and 14 start-viability assertions. Current-contract documentation checks and `git diff --check` pass.
- Five native turns from the same 80-founder, 1024×512 opening save in each mode. Each resolves approximately 3,500–3,600 active tiles exactly once, with no seasonal barriers. [Scale evidence](Experiments/AnnualLandFood-20261128-scale.json) records the measured work and allocations.

Quiet annual projection allocates **zero bytes** in the focused test. An initial implementation allocated 83.9 MB per turn in the 80-founder smoke; the zero-demand specialization reduced this to 73.7 MB, compared with 70.9 MB in legacy mode (about +3.9%). Mean whole-turn times were 567 ms annual versus 618 ms legacy. These are five unwarmed, behaviorally divergent turns on a shared host, **not a throughput certification or a claimed speedup**. The fixed tile-kernel probe uses the same allocation per call from one day to 600 years; no annual loop is hidden inside the solver.

The experiment remains opt-in. It averages seasons and fixed harvest claims; policy/capital/demography replanning can still change outcomes under shorter turns. Full 320-founder/mature-empire RL throughput and Unity interaction have not been validated. No irrigation adoption or population target is enforced.

The subsequent [headless civic playthrough](CivicPlaythrough.md) enables annual
food at civic onset instead of year 1,000. It records earlier accounting,
divergent institutional paths, and a village-level irrigation eligibility/use
funnel. Its different intervention time produces a different population history.
