# Validation — milestone three, September 10, 2026

Windows x64, Unity 6000.5.10f1, Unity-bundled .NET 8.0.318. Timings are local development measurements. [Milestone-two results](Validation-Milestone2.md) remain archived for comparison.

## Automated checks

- 146 headless assertions passed: 32 focused rules, 59 second-milestone rules and 55 third-milestone checks.
- New checks cover annualized exponential growth of the entire connected tribe, increasing food demand, a food-supported plateau, deficit decline, reserve-only non-growth, demographic rounding/explanations, food-share-based lifestyle progress, automatic hunting only by established co-located groups, twenty-year and shortened clocks, separate potential and human-gatherable food, finite seasonal replenishment, reproduction-before-harvest with retained breeding stock, passive marine food without freshwater credit, shared coastal/land labor, peak movement/AI/placement, stack occupants, hover timing/reset/fade and hidden-information isolation.
- Herd-count boundary checks cover 1, 9, 10, 14, 15, 25, 99, 100, 101, 199, 200 and 249. Own livestock remains exact; foreign livestock and wild herds remain approximate. Counts in the same bucket produce identical player details and AI size categories. Save/load and authoritative replay preserve the result.
- All 14 Unity EditMode tests passed. Added visual tests verify exact contour counts, suppression of same-elevation shared edges, higher-tile ownership of unequal boundaries, no hidden-elevation leaks, nonoverlapping stack hit slots, hover layout, planning paths and connected/uncertain/disconnected supply previews.
- Unity compiled the automatic-hunting presentation, all 14 EditMode tests passed again in 5.01 seconds, and the Windows development player rebuilt successfully.
- 30 seeded four-tribe games completed with zero illegal AI orders and zero replay mismatches, including reversed entity and submitted-order collections. All population and food-report conservation checks passed.
- With food-share progress, connected-group growth and automatic co-located hunting, the thirty-seed run recorded 26,817 net additions and 345 starvation deaths; all 120 tribes survived. First technologies were 18 Animal Husbandry, 55 Cultivation and 47 Watercraft/Fisheries. The AI made 930 visible herd-pursuit commitments and converted 292 into food-producing contact (31.4%), compared with an 8.1% successful interception rate under explicit target orders.

The habitat report grouped camps by their initial radius-one landscape. River/woodland starts averaged 299.3 people after twelve turns, grassland 352.0, desert 214.1 and cold-steppe/tundra 131.1, from 24 initially. Mean hunting food per tribe was 42,236, 59,831, 23,862 and 30,057 respectively; terrestrial gathering was 36,086, 34,805, 27,410 and 15,090. All groups survived. End population had a 25th percentile of 115, median 194 and 75th percentile 296; 93 of 120 tribes ended at 100 or more. Of 493 turns following observed population increases, 443 (89.9%) answered with additional hunter dispatch or increased gathering. Across the same thirty Small worlds, 15 river tiles directly bordered desert, confirming that arid river corridors occur, although this is not yet a distribution target or guaranteed-per-map feature.

The September 10 generational checks additionally cover one-generation starting reserves, one-to-four-generation climate storage, disconnected subsistence foraging with barren-site starvation retained, species herd thresholds/splitting and Standard-only support for 80 tribes.

The `ui/civ5-inspired` presentation experiment adds two EditMode checks for the planning path and queue feedback. The path check verifies that previews obey the displayed movement budget, weighted terrain cost and the filtered visible-tile boundary; the queue check distinguishes a newly queued command from replacement of the selected unit's earlier command. These presentation helpers do not mutate simulation state.

## Civ-inspired UI experiment — September 10, 2026

- The focused headless suite passed all 129 assertions (29 original, 57 milestone-two and 43 milestone-three). No simulation source or gameplay tuning changed.
- Unity 6000.5.10f1 compiled the presentation and all 13 EditMode tests passed in 9.11 seconds with no failures or skips. This includes the two new planning-presentation checks above.
- The Windows x64 development player built successfully through `Anthropocene.Editor.PrototypeBuild.Windows`. Unity reported `Build Finished, Result: Success` and exited with code 0.
- Automated player interaction was blocked at launch by a Windows firewall/security permission dialog. The dialog was not acted on, and only this experiment's player process was closed. Selection, target-mode and order-resolution interaction therefore remain a manual smoke-test risk for this branch; no screenshot was retained.
- Standard seed 1729: 524,288 tiles generated in about 6.8 seconds; its four-turn AI opening took about 19.4 seconds headlessly. Sampled managed memory was 210–240 MB. Save was 33.0 MB in about 1.0 second; load about 0.85 seconds. Runtime turn resolution uses a worker/progress screen.

## Generational resolver and scale validation — September 10, 2026

- Standard seed 1729 generated all 524,288 tiles and 80 separated viable starts in 6.96 seconds.
- The complete twelve-turn 80-tribe opening resolved in 11.31 seconds headlessly, using about 390 MB managed memory with the larger hunter-group allocation.
- Its 44.2 MB save completed in 1.80 seconds and loaded in 1.45 seconds.
- The eight-seed Standard audit retained all elevation, coast/freshwater, oasis, river, start and save/load invariants with 80 camps in every world.
- The Windows player visibly showed one full starting generation, climate storage, 24 → 34 first-generation growth, a green connected tether, and a red four-tile disconnected preview with a matching sidebar warning. A disconnected group remained present and reported its local-forage/provision state after resolution.

## Elevation coverage

Eight Standard Continents worlds, seeds 1–8, 80 camps each. Every world contained every level 0–10. All 640 camps had an opening herd reachable within two land steps around peaks. No camps or herds spawned on peaks. Every world passed freshwater/coast, oasis, river-graph and elevation validation, and all levels survived Standard save/load.

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

Counts include freshwater lakes at their above-sea level. Peaks target the top one percent of land elevation; other quantiles deliberately spread the traversable levels. The 80-start audit retained 977–1,707 wild herds, 54–98 oases and 404–2,726 shared river edges. Continuous elevation and biome remain independent of the gameplay categories.

Twelve Small seeds per preset still pass viable-start, drainage-cycle and stellar-temperature checks. Mean land fractions are 0.480 Continents, 0.245 Archipelagos and 0.480 Pangaea; mean islands are 10.9, 21.4 and 7.8 respectively.

## Interactive Windows checks

The Windows player reproduced the Small seed-1 opening: 24 → 39 → 48 people, twenty years per turn, and 1,696 passive marine food rounded in the second-generation report. The camp and two eight-person hunter groups occupied three distinct slots. Clicking each hunter glyph selected its corresponding ID (26 and 31); delayed hover listed the camp and both groups together. Moving to the header removed the hover panel.

Save/load was checked against a changed state: after saving turn three, advancing produced turn four with 80 people and 60 elapsed years. Loading restored turn three, 48 people, 40 elapsed years, both hunter groups and the persisted demographic report. Classic and high-contrast tilesets retained the same occupants, approximate herd badges and elevation information. Zooming out simplified contour density; zooming in restored the detailed outlines.

Review captures are in `TestResults/milestone3-hover-stack.png`, `TestResults/milestone3-contours-demographics.png` and `TestResults/milestone3-hover-coast.png`. Coastal hover shows level 0, undrinkable saltwater, passive marine eligibility and the camp's gathering reach. Panning clears the panel immediately; edge flipping/clamping is covered by the automated layout test. The final rebuild also corrects the journal's hunger sentence to use all seasonal deficit intervals in the generation rather than just the final interval.

The final Windows build completed successfully (`milestone3-build-final.log`) and reloaded the saved demonstration. No managed exceptions appeared in the player log. Source diff whitespace checks pass; Unity's reserialized settings retain generated whitespace and schema changes. An attempted baseline cleanup was rejected by automatic approval review because tracked files could contain existing edits, so those settings remain intact.

The biomass/contour revision again passed all 14 Unity EditMode tests and produced a successful Windows build. Automated presentation checks prove that equal elevation buckets suppress shared edges, the higher tile owns unequal boundaries and fogged neighbors remain sealed. Native screenshot inspection was unavailable in this task because its Windows control surface exposed browser tabs but no native application windows; normal-zoom contour readability therefore remains an explicit manual playtest item.

## Reproduction commands

Run `Tools/Test-Headless.ps1 -Seeds 30 -Standard -Geography`, `Tools/Test-Headless.ps1 -Seeds 30 -BiomePaths` and `Tools/Test-Headless.ps1 -Audit`. Run Unity EditMode tests through the Test Runner or batch mode. Raw logs are in the ignored `TestResults` directory.

For the current interactive check, select Standard / Continents / seed 1729 / 32 tribes. The first view exposes the one-generation reserve and four-generation desert storage ceiling. Dispatch eight people, commit, then move that group two tiles east and commit. A second two-tile eastward plan is visibly beyond the range-three supply tether: the line and sidebar warning both turn red while the order remains legal.
