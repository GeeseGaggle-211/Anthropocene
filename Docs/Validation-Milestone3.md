# Validation — milestone three, September 10, 2026

Windows x64, Unity 6000.5.10f1, Unity-bundled .NET 8.0.318. Timings are local development measurements. [Milestone-two results](Validation-Milestone2.md) remain archived for comparison.

## Automated checks

- 162 fixed headless assertions passed: 33 focused rules, 59 second-milestone rules and 70 third-milestone checks. Seeded benchmarks add one no-wild-herd-proliferation assertion per world.
- New checks cover proficiency ramp thresholds and full hunting yield, reduced land impact for the same gathered food, passive-aquatic 75/25 Gathering/Fishing practice attribution, workforce-restricted dispatch, workforce/environment save round trips, annualized exponential growth, increasing food demand, food-constrained growth and decline, food-share-based lifestyle progress, automatic hunting, sustainable herd pressure, twenty-year and shortened clocks, two-layer food, disabled-by-default herd fission, passive marine food without freshwater credit, peak movement/AI/placement, stack occupants, hover behavior and hidden-information isolation.
- Herd-count boundary checks cover 1, 9, 10, 14, 15, 25, 99, 100, 101, 199, 200 and 249. Own livestock remains exact; foreign livestock and wild herds remain approximate. Counts in the same bucket produce identical player details and AI size categories. Save/load and authoritative replay preserve the result.
- All 14 Unity EditMode tests passed in 6.31 seconds. Visual tests verify exact contour counts, suppression of same-elevation shared edges, higher-tile ownership of unequal boundaries, no hidden-elevation leaks, nonoverlapping stack hit slots, hover layout, planning paths and connected/uncertain/disconnected supply previews.
- A fresh batch rerun for the proficiency/workforce revision was blocked before normal compilation because the local Unity Licensing Client repeatedly disconnected and Package Manager consequently registered zero built-in modules, including IMGUI and the Test Framework. The resulting missing-`GUIStyle` diagnostics are an environment/licensing failure rather than a source-specific diagnostic. Headless compilation of the authoritative simulation succeeds; current presentation text still needs a Unity-licensed rerun or manual editor open.
- Unity compiled the revised demographic, herd and AI observation rules, and the Windows development player rebuilt successfully (`Build Finished, Result: Success`).
- 30 seeded four-tribe games completed with zero illegal AI orders, zero replay mismatches and no increase in wild-herd entity count while fission was disabled, including reversed entity and submitted-order collections. All population, food-report and workforce conservation checks passed.
- The final thirty-seed Small-world regression run recorded 9,344 net additions and 51 starvation deaths; all 120 tribes survived. First technologies were 25 Animal Husbandry, 58 Cultivation and 37 Watercraft/Fishing: 20.8%, 48.3% and 30.8%. Four-player worlds overrepresent the stable fishing cohort because one matching player is already 25% of the roster before fishing-mandate candidates are added, so this suite remains a mechanics/population regression rather than the representative path-distribution target. The AI made 1,732 visible herd-pursuit commitments and converted 642 into food-producing contact (37.1%). End population had a 25th percentile of 54, median 86, 75th percentile 144 and maximum 274; 56/120 tribes ended at 100 or more. Of 727 turns following observed population increases, 694 answered with additional hunter dispatch or increased gathering.
- The primary path acceptance audit uses three Standard / Continents worlds with 32 tribes. Seeds 1–3 produced Husbandry/Cultivation/Fishing splits of 8/17/7, 9/14/9 and 10/18/4. Aggregate outcome was 27/49/20, or 28.1%/51.0%/20.8%. Every sampled world exceeded 20% Husbandry, produced fishing-first tribes and retained Cultivation as the plurality; all 96 tribes survived. Median end populations were 102, 101 and 125. Wild-herd counts were 1,128, 1,607 and 1,150 with fission disabled. This demonstrates the requested robust sampled target; it is not a mathematical guarantee for every possible seed.

The updated habitat report grouped camps by their initial radius-one landscape. Desert starts averaged 94.7 people and produced twelve Husbandry-first outcomes; grassland averaged 119.9 with one; river/woodland averaged 126.1 with ten; cold-steppe/tundra averaged 42.1 with two. All groups survived. These results show that the nomadic and fishing paths coexist with an agricultural plurality, while cold starts remain the weakest demographic case and the stable policy cohorts are deliberately testable behavioral approximations. The earlier geography batch found river tiles directly bordering desert, confirming that arid river corridors occur, although this is not yet a distribution target or guaranteed-per-map feature.

The broad generation/geography suite also passed. Standard seed 1729 generated 524,288 tiles and 80 viable starts in 7.306 seconds, resolved its opening in 3.168 seconds, saved 40,772,174 bytes in 1.388 seconds and loaded in 1.068 seconds. The eight-seed Standard elevation, viable-start and save/load audit completed without an invariant failure.

The September 10 generational checks additionally cover one-generation starting reserves, one-to-four-generation climate storage, disconnected subsistence foraging with barren-site starvation retained, species herd ceilings and opt-in fission, sustainable hunting pressure and Standard-only support for 80 tribes.

The `ui/civ5-inspired` presentation experiment adds two EditMode checks for the planning path and queue feedback. The path check verifies that previews obey the displayed movement budget, weighted terrain cost and the filtered visible-tile boundary; the queue check distinguishes a newly queued command from replacement of the selected unit's earlier command. These presentation helpers do not mutate simulation state.

## Civ-inspired UI experiment — September 10, 2026

- The focused headless suite passed all 129 assertions (29 original, 57 milestone-two and 43 milestone-three). No simulation source or gameplay tuning changed.
- Unity 6000.5.10f1 compiled the presentation and all 13 EditMode tests passed in 9.11 seconds with no failures or skips. This includes the two new planning-presentation checks above.
- The Windows x64 development player built successfully through `Anthropocene.Editor.PrototypeBuild.Windows`. Unity reported `Build Finished, Result: Success` and exited with code 0.
- Automated player interaction was blocked at launch by a Windows firewall/security permission dialog. The dialog was not acted on, and only this experiment's player process was closed. Selection, target-mode and order-resolution interaction therefore remain a manual smoke-test risk for this branch; no screenshot was retained.
- Standard seed 1729: 524,288 tiles generated in about 6.8 seconds; its four-turn AI opening took about 19.4 seconds headlessly. Sampled managed memory was 210–240 MB. Save was 33.0 MB in about 1.0 second; load about 0.85 seconds. Runtime turn resolution uses a worker/progress screen.

## Generational resolver and scale validation — September 10, 2026

- Standard seed 1729 generated all 524,288 tiles and 80 separated viable starts in 6.88 seconds.
- The complete twelve-turn 80-tribe opening resolved in 2.71 seconds headlessly, using about 229 MB managed memory with bounded wild-herd entities.
- Its 40.35 MB save completed in 1.36 seconds and loaded in 1.07 seconds.
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

Counts include freshwater lakes at their above-sea level. Peaks target the top one percent of land elevation; other quantiles deliberately spread the traversable levels. The 80-start audit retained 1,049–1,830 wild herds, 54–98 oases and 404–2,726 shared river edges. Continuous elevation and biome remain independent of the gameplay categories.

Twelve Small seeds per preset still pass viable-start, drainage-cycle and stellar-temperature checks. Mean land fractions are 0.480 Continents, 0.245 Archipelagos and 0.480 Pangaea; mean islands are 10.9, 21.4 and 7.8 respectively.

## Interactive Windows checks

The Windows player reproduced the Small seed-1 opening: 24 → 39 → 48 people, twenty years per turn, and 1,696 passive marine food rounded in the second-generation report. The camp and two eight-person hunter groups occupied three distinct slots. Clicking each hunter glyph selected its corresponding ID (26 and 31); delayed hover listed the camp and both groups together. Moving to the header removed the hover panel.

Save/load was checked against a changed state: after saving turn three, advancing produced turn four with 80 people and 60 elapsed years. Loading restored turn three, 48 people, 40 elapsed years, both hunter groups and the persisted demographic report. Classic and high-contrast tilesets retained the same occupants, approximate herd badges and elevation information. Zooming out simplified contour density; zooming in restored the detailed outlines.

Review captures are in `TestResults/milestone3-hover-stack.png`, `TestResults/milestone3-contours-demographics.png` and `TestResults/milestone3-hover-coast.png`. Coastal hover shows level 0, undrinkable saltwater, passive marine eligibility and the camp's gathering reach. Panning clears the panel immediately; edge flipping/clamping is covered by the automated layout test. The final rebuild also corrects the journal's hunger sentence to use all seasonal deficit intervals in the generation rather than just the final interval.

The final Windows build completed successfully (`milestone3-build-final.log`) and reloaded the saved demonstration. No managed exceptions appeared in the player log. Source diff whitespace checks pass; Unity's reserialized settings retain generated whitespace and schema changes. An attempted baseline cleanup was rejected by automatic approval review because tracked files could contain existing edits, so those settings remain intact.

The biomass/contour revision again passed all 14 Unity EditMode tests and produced a successful Windows build. Automated presentation checks prove that equal elevation buckets suppress shared edges, the higher tile owns unequal boundaries and fogged neighbors remain sealed. Native screenshot inspection was unavailable in this task because its Windows control surface exposed browser tabs but no native application windows; normal-zoom contour readability therefore remains an explicit manual playtest item.

## Reproduction commands

Run `Tools/Test-Headless.ps1 -Seeds 30 -Standard -Geography`, `Tools/Test-Headless.ps1 -Seeds 30 -BiomePaths`, `Tools/Test-Headless.ps1 -Seeds 3 -NomadStandard` and `Tools/Test-Headless.ps1 -Audit`. Run Unity EditMode tests through the Test Runner or batch mode. Raw logs are in the ignored `TestResults` directory.

For the current interactive check, select Standard / Continents / seed 1729 / 32 tribes. The first view exposes the one-generation reserve and four-generation desert storage ceiling. Dispatch eight people, commit, then move that group two tiles east and commit. A second two-tile eastward plan is visibly beyond the range-three supply tether: the line and sidebar warning both turn red while the order remains legal.
