# Validation — milestone three, September 9, 2026

Windows x64, Unity 6000.5.10f1, Unity-bundled .NET 8.0.318. Timings are local development measurements. [Milestone-two results](Validation-Milestone2.md) remain archived for comparison.

## Automated checks

- 129 focused headless assertions passed: 29 original rules, 57 second-milestone rules and 43 third-milestone checks.
- New checks cover annualized exponential growth, increasing food demand, a food-supported plateau, deficit decline, reserve-only non-growth, demographic rounding/explanations, twenty-year and shortened clocks, finite seasonal replenishment, passive marine food without freshwater credit, shared coastal/land labor, peak movement/AI/placement, stack occupants, hover timing/reset/fade and hidden-information isolation.
- Herd-count boundary checks cover 1, 9, 10, 14, 15, 25, 99, 100, 101, 199, 200 and 249. Own livestock remains exact; foreign livestock and wild herds remain approximate. Counts in the same bucket produce identical player details and AI size categories. Save/load and authoritative replay preserve the result.
- All 11 Unity EditMode tests passed. Added visual tests verify exact contour counts at normal zoom, monotonic density at low zoom, nonoverlapping stack hit slots and hover-panel flipping/clamping. A touching-slot failure was fixed with explicit gutters.
- 30 seeded four-tribe games completed with zero illegal AI orders and zero replay mismatches, including reversed entity and submitted-order collections. All population and food-report conservation checks passed.
- The new model recorded 10,752 additions and 2,001 starvation deaths; 84/120 tribes survived. First technologies: 0 Animal Husbandry, 37 Cultivation, 47 Watercraft/Fisheries; 36 tribes died before a breakthrough. This suite now spans human generations and is not directly comparable to the earlier ninety-day-turn survival figures. Marginal sites can decline severely, and the policy/balance still favors fishing over hunting.
- Standard seed 1729: 524,288 tiles generated in about 6.8 seconds; its four-turn AI opening took about 19.4 seconds headlessly. Sampled managed memory was 210–240 MB. Save was 33.0 MB in about 1.0 second; load about 0.85 seconds. Runtime turn resolution uses a worker/progress screen.

## Elevation coverage

Eight Standard Continents worlds, seeds 1–8, six camps each. Every world contained every level 0–10. All 48 camps had an opening herd reachable within two land steps around peaks. No camps or herds spawned on peaks. Every world passed freshwater/coast, oasis, river-graph and elevation validation, and all levels survived Standard save/load.

| Level | Interpretation | Minimum tiles per world | Maximum tiles per world |
|---:|---|---:|---:|
| 0 | Marine | 158,958 | 353,095 |
| 1 | Lowland | 20,298 | 46,718 |
| 2 | Lowland | 23,896 | 54,426 |
| 3 | Rolling hills | 24,703 | 77,222 |
| 4 | Rolling hills | 28,150 | 87,696 |
| 5 | Ridges/highland | 23,059 | 45,093 |
| 6 | Ridges/highland | 19,797 | 38,650 |
| 7 | Mountains | 14,822 | 28,988 |
| 8 | Mountains | 9,881 | 19,356 |
| 9 | Mountains | 4,941 | 9,663 |
| 10 | Impassable peaks | 1,646 | 3,220 |

Counts include freshwater lakes at their above-sea level. Peaks target the top one percent of land elevation; other quantiles deliberately spread the traversable levels. Worlds retained 919–1,645 wild herds, 54–98 oases and 404–2,726 shared river edges. Continuous elevation and biome remain independent of the gameplay categories.

Twelve Small seeds per preset still pass viable-start, drainage-cycle and stellar-temperature checks. Mean land fractions are 0.480 Continents, 0.245 Archipelagos and 0.480 Pangaea; mean islands are 10.9, 21.4 and 7.8 respectively.

## Interactive Windows checks

The Windows player reproduced the Small seed-1 opening: 24 → 39 → 48 people, twenty years per turn, and 1,696 passive marine food rounded in the second-generation report. The camp and two eight-person hunter groups occupied three distinct slots. Clicking each hunter glyph selected its corresponding ID (26 and 31); delayed hover listed the camp and both groups together. Moving to the header removed the hover panel.

Save/load was checked against a changed state: after saving turn three, advancing produced turn four with 80 people and 60 elapsed years. Loading restored turn three, 48 people, 40 elapsed years, both hunter groups and the persisted demographic report. Classic and high-contrast tilesets retained the same occupants, approximate herd badges and elevation information. Zooming out simplified contour density; zooming in restored the detailed outlines.

Review captures are in `TestResults/milestone3-hover-stack.png`, `TestResults/milestone3-contours-demographics.png` and `TestResults/milestone3-hover-coast.png`. Coastal hover shows level 0, undrinkable saltwater, passive marine eligibility and the camp's gathering reach. Panning clears the panel immediately; edge flipping/clamping is covered by the automated layout test. The final rebuild also corrects the journal's hunger sentence to use all seasonal deficit intervals in the generation rather than just the final interval.

The final Windows build completed successfully (`milestone3-build-final.log`) and reloaded the saved demonstration. No managed exceptions appeared in the player log. Source diff whitespace checks pass; Unity's reserialized settings retain generated whitespace and schema changes. An attempted baseline cleanup was rejected by automatic approval review because tracked files could contain existing edits, so those settings remain intact.

## Reproduction commands

Run `Tools/Test-Headless.ps1 -Seeds 30 -Standard -Geography` and `Tools/Test-Headless.ps1 -Audit`. Run Unity EditMode tests through the Test Runner or batch mode. Raw logs are in the ignored `TestResults/milestone3-headless.txt`, `milestone3-elevation.txt`, `milestone3-unity.xml` and `milestone3-build.log`.

The headless `0 --find-ui3` diagnostic finds a combined demonstration. Select Small / Continents / seed 1 / six tribes. Dispatch eight people in each of the first two turns. The turn-three planning view has a camp and two hunter groups stacked, levels 0, 1, 2, 3, 6 and 7 in sight, and a second-generation report of 39 + 9 additions − 0 deaths = 48 people, including approximately 1,695.6 passive marine food over that generation. This is a deterministic play sequence, not a modified save or a special production seed rule.
