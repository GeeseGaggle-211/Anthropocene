# Neolithic 500-year changing-cadence experiment

Date: 7 November 2026. Status: completed native campaigns; diagnostic, not production rollout or acceptance of a 5,000-person town.

## Provenance

Production main: `07f3501c2d5e047e44d61e85cc0d4613b54572d6`.
Executed experiment: `f0b87cadbf60af930d79ad553ce59f4178d09098`.
Branch: `experiments/neolithic-cadence-500y-20261107`.
Successful [Actions run 42382407611](https://github.com/GeeseGaggle-211/Anthropocene/actions/runs/42382407611).
All five paired jobs passed. Production simulation/configuration files and active RL were unchanged. The workflow verifies an empty production diff; independent analysis verified all 80 source hashes against the preceding source archive.

## Requested test and schedule

The user proposed lowering agricultural maintenance and harvesting work through Plough rather than increasing the cultivation knee, and requested a 500-year run whose turn duration declines from 20 to 15 to 10 years.

The specific transition ages were not supplied. This experiment uses 200 years at 20-year turns (10 turns), 150 years at 15-year turns (10 turns), then 150 years at 10-year turns (15 turns). The control uses 25 twenty-year turns. These are test dates, not an approved era calendar.

Five production-size Continents worlds: 1024x512, 80 founding tribes, seeds 11, 23, 47, 101 and 1729. The native opening completes first, in 7-10 turns. Year zero is that common post-opening boundary, not the individual discovery date of every society. Both arms share the exact first 200 measured years, then fork from the same byte-identical year-200 save.

This is the unchanged native AI -> Prepare -> Resolve economy, with endogenous q and population, actual materials/goods/tools, physical demography, trade, movement, fission, immigration and domestication. Resident Fishing retains main's disabled default. No learned policy was used. No new Plough bonus, q/knee shift, biomass increase, farming-radius change, mortality/cohesion adjustment or timer normalization was applied. The intent is to identify cadence effects before choosing a Plough-efficiency coefficient.

Duration is selected before AI observation/planning and fresh Prepare; no PlanningDraft is reused. Reference time-unit constants are unchanged. Only Config.TurnDays changes at the two treatment boundaries. This avoids the known stale-draft path but does not repair or test UI changes made after drafting.

## Paired endpoint populations

Total population includes residents plus detached Hunters. These are five separate alternative worlds.

| Seed | Fixed 20-year turns | Dynamic 20->15->10 | Change |
|---|---:|---:|---:|
| 11 | 189,993 | 274,628 | +44.55% |
| 23 | 154,814 | 386,646 | +149.75% |
| 47 | 178,828 | 257,007 | +43.72% |
| 101 | 164,138 | 316,878 | +93.06% |
| 1729 | 180,784 | 325,612 | +80.11% |
| Pooled total | **868,557** | **1,560,771** | **+79.70%** |

Median final world population is 178,828 versus 316,878. Five paired seeds constitute a regression screen, not a broad confidence interval.

## Settlement outcomes

Agriculture-first means first Neolithic discovery was Cultivation, inherited by daughters. It is diagnostic identity, never a policy selector, and does not establish pure farming or food independence.

| Metric pooled across five worlds at year 500 | Fixed 20 | Dynamic |
|---|---:|---:|
| Live settlements, all routes | 1,704 | 2,592 |
| Agriculture-first live settlements | 1,125 | 1,669 |
| Agriculture-first residents | 567,352 | 1,004,638 |
| Agriculture-first population P25 | 203 | 303 |
| Agriculture-first median population | **430** | **572** |
| Agriculture-first population P75 | 854 | 997 |
| Largest agriculture-first settlement | 1,089 | 1,154 |
| Largest settlement of any route | 1,089 | 1,156 |
| Settlements >=5,000 | **0** | **0** |

No observed committed settlement in either campaign reached 5,000; the maximum across recorded boundaries was 1,156. The main effect is more settlements and moderately larger villages, not large towns. The plotted live-settlement median includes daughter settlements and changing cohort composition; it is not a fixed-cohort growth curve.

Over the 500 years, pooled fission events rise from 1,222 to 2,110. Immigration event-person counts rise from 17,246 to 63,902; these are movements, not unique persons. They combine different populations, choices and per-year decision opportunities, not a controlled migration-only causal estimate.

## Food, labor and demographic pressure

The following endpoint shares are weighted by agriculture-first resident population except where specified:

| Metric | Fixed 20 | Dynamic |
|---|---:|---:|
| Food-needs coverage | 99.9235% | 99.8362% |
| Clothing coverage | 99.7453% | 99.9025% |
| Housing coverage | 94.1631% | 98.0964% |
| Water coverage | 100% | 100% |
| Experienced QoL | 0.78054 | 0.79492 |
| Assigned Farmer share | **39.9644%** | **29.9648%** |
| Gatherer/unassigned-resident share | **49.0627%** | **60.9189%** |
| Own-field footprint mean q, population-weighted | 0.34813 | 0.32740 |
| Median own-field footprint mean q, unweighted | 0.28880 | 0.29699 |

Footprint mean q includes cultivable radius-one fields not operated by the camp as zero. It is not the mean only over improved fields. The gathered/residual role can include raw-material tasks or unassigned people, so it is not a measured wild-food harvest labor total.

The lower Farmer share is NOT a Plough effect; no Plough change was made. Much of the endpoint allocation shifts into Gatherers rather than specialists. Cohort composition and source choice need separate attribution before calling this inefficient allocation.

Near-full meal coverage is not evidence of unused food capacity or self-sufficiency. Trade and alternative food routes remain active. Reserves are uneven: median agriculture-first stored food is essentially zero in both arms, while resident-weighted stock coverage is about 10.33 vs 13.57 years. A few large stocked settlements dominate that aggregate; do not claim every village has a decade of reserves.

The largest dynamic agriculture-first settlement (1,154 residents) is fully fed, clothed and housed, but its stored effective mortality rate is about 0.05007/year against fertility 0.045/year. This directly demonstrates continuing demographic pressure at the observed upper end. It does not isolate demography, fission or cohesion as the sole explanation, and is not a new 5,000-person-state experiment.

Cumulative actual food production across five worlds rises from 931.0 million to 1,447.5 million units. Because population and settlement paths differ, this is world output, not a fixed-area or fixed-workforce yield comparison.

## Plough design implication

Preserve physical q(E), q_k, farming access and ecological capacity when testing the user's labor-efficiency proposal. Separate maintenance/field-operation efficiency from crop-harvesting efficiency and from wild gathering. An explanatory candidate is:

`L_maint' = L_maint / eta_maint`

`L_harvest' = Y_wild/p_wild + Y_crop/(eta_crop*p_crop)`.

These are proposed work-efficiency semantics, not measured historical coefficients or implemented production formulas. Charge finite equipment service and replacement, and any explicitly selected traction costs. Do not grant a second direct yield multiplier or change global GatherPerPerson. Preserve the existing overlap between maintenance and harvesting, rather than charging both as full additive workforces. Expansion and equipment manufacture are separate tasks.

The next controlled Plough comparison should keep the same changing calendar and vary maintenance-only, crop-harvest-only, then both, with q_k and ecology unchanged. Numerical bonus values have not been selected. Better work efficiency may enable more real investment and crafting, but this experiment does not show that it will solve the separate 5,000-person demographic/social constraints.

## Attribution and validation limits

The existing implementation's per-turn migration fractions, cooldowns, contact turns, movement/fission opportunities, availability of new tools/residents and turn-keyed random streams remain unchanged. The treatment is therefore the FULL native calendar change, not pure biological output. The opening-stock pulse model was retained. Timer-normalized controls, equal-fixed-staffing trials and subannual tests remain separate work.

All arms completed exactly 500 measured years. There are 250 unique measured native resolutions because the 50 common-prefix resolutions are shared across arms, plus 45 opening resolutions and four additional transition replay resolutions. Output contains 255 world-boundary rows and 74,415 live-camp observations.

- Zero submitted-order rejections.
- Native resolution invariants and full-world final invariants passed for all ten endpoints.
- Maximum relative food/biomass balance residual: `3.3392348002433604e-16`.
- Independent analysis checked each full arm's population identity: initial + cumulative births - cumulative deaths = final.
- Seed 11 passed exact save/load and next-turn replay at both duration transitions. This is two single-turn transition tests, not full 500-year replay of all seeds or scalar/parallel equivalence.
- All five downloaded artifact ZIP hashes and all 80 native source files were verified.

No fresh full registered headless suite, Unity playtest, learned-policy evaluation, exact timestep-invariance proof or self-sufficient 5,000-person town sign-off is claimed. Source/controller version and raw per-turn semantics are part of the result.

## Reproduction

At the executed experiment commit:

```sh
dotnet run -c Release --project Tools/Cadence500/Anthropocene.Cadence500.csproj -- 11 TestResults/Cadence500/11
```

Repeat for the other seeds. The conversation evidence bundle preserves full source, configuration, original ZIP artifacts, raw world/camp trajectories, source hashes, exact runtime/commit information and independent analysis. Original GitHub artifact retention is fourteen days.

Artifacts: seed 11 ID 13578565614; 23 ID 13578480786; 47 ID 13577129777; 101 ID 13578187236; 1729 ID 13577854737. Main, existing feature branches, running training and VM infrastructure were untouched.
