# Playtest starts and developer atlas

The opening menu offers three environmental presets alongside Custom world. Each uses Standard / Continents / seed **11** / **80 tribes** and normal tuning. They select an existing generated camp as the human tribe by exchanging player labels; geography, society policies, IDs, spacing, food, reserves and wild herds are unchanged. No technology is granted. Historical names are environmental analogues, not reconstructed civilizations.

These sites were reselected for generator 10 after new offshore geography changed the generated camp cohort. The former seed-5 world no longer has qualifying steppe and cold-fishing camps among its 80 starts. Seed 11 supplies all three requested openings under the same habitat checks; it was selected for environmental contrast, without running simulations to pick technological outcomes.

| Preset | Camp tile | Local setting | Land food | Aquatic food | Start viability |
| --- | ---: | --- | ---: | ---: | ---: |
| Desert river valley | 209873 | 20.1°C mean; seven desert river tiles | 139.63 | 0 | 180.00% |
| Steppe hunters | 192746 | 17.8°C mean; seven open grassland tiles, five river tiles; adjacent bison | 96.94 | 0 | 180.00% |
| Cold coastal fishers | 431802 | 2.2°C mean; four wooded Cold tiles, three adjacent shallows; land rain pools | 7.49 | 166.93 | 155.79% |

Food columns are **annual-mean renewal potential per 91.25-day base season**, summed within radius 1. They are neither current stocks nor realized harvests. They use the same seasonal capacities, rates and sustainable production fraction as the viable-start check. Actual gathering shares one labor ceiling across land and passive coastal food; depletion, competing animals, workforce choices, freshwater and seasonal reserves also matter. The 180% ceiling here is the starting gathering labor ceiling, not a promise of population growth. All three meet the normal 85% viability threshold. The unequal densities are deliberately preserved for comparison.

The four stored seasonal profiles show why the fisher's annual mean is insufficient by itself:

| Preset / food source | Season 1 | Season 2 | Season 3 | Season 4 | Minimum local freshwater capacity across seasons |
| --- | ---: | ---: | ---: | ---: | ---: |
| Desert river / land | 140.46 | 116.31 | 139.15 | 162.60 | 661.27 |
| Steppe / land | 95.76 | 69.72 | 95.06 | 127.23 | 483.40 |
| Cold shore / land | 4.12 | 19.52 | 5.99 | 0.33 | 25.00 |
| Cold shore / shallows | 153.81 | 340.45 | 153.81 | 19.63 | 0: saltwater cannot be drunk |

Food is renewal potential in the same units as the first table; freshwater is **capacity**, not a food yield or a promise of realized recharge. This cold fisher is in the Southern Hemisphere: Season 2 is productive summer and Season 4 is lean winter. The viable-start rule checks every season's freshwater and annual renewable food with the normal shared labor limit. The seasonal reserve test is therefore more informative than comparing full initial food stocks.

For playtesting, compare generations with similar dispatched populations and note food produced/eaten, reserves, deaths, growth and first breakthrough in the existing outcome panels. Try river gathering, steppe expeditions onto the adjacent bison (tile 192745), and coastal hunter fishing orders. The steppe camp stands on a river in open country. The fisher's three shallows provide most renewable local food; its wooded shore supports less human food and must still supply drinking water. Choosing the preset does not force its expected technological path.

**Build world preview (dev)** generates the selected setup and opens a full-world atlas before play. **Menu → Preview current world (dev)** inspects the existing committed world. Environment, elevation, volcanic activity, renewable food and freshwater renewal layers share the same coordinates. Cycle annual mean and four seasonal profiles; the heatmaps use a fixed logarithmic scale from zero to 100+ units per tile per base season. These show stored profile potential; the tile inspector separately projects current food and freshwater stock without materializing ecology.

Wheel zooms, dragging pans, clicking selects a tile, **Fit world** resets framing, and **Find my camp** zooms to the human start. White markers show camps, gold marks the human camp, and numbered markers identify the three preset sites when present. This is a rectangular atlas of axial rows, not the normal hex renderer. The tile inspector shows environment, vegetation, elevation, river, source, temperature and seasonal renewal. **Play current start** returns to the normal filtered game. Previewing does not add knowledge to the tribe, alter queued orders, advance time or change saves.

The developer atlas intentionally reveals hidden geography and camp locations and is labelled accordingly. It is available in the prototype; do not mistake it for a player observation. Generation/resolution guards prevent inspection while a worker mutates the world. Textures are built on demand and reused until the layer, season or committed world changes. Preview settings and preset names are UI state, not new save fields; existing save compatibility is unchanged, and the selected camp's ownership already round-trips through the normal save format.

The fixed preset habitat and viability checks fail explicitly if future generation/tuning invalidates a site. Update and remeasure the presets in that case; do not inject food to preserve the table. `ScenarioChecks` exercises the actual generated world, ownership-only changes, filtered observations, exact save restoration, profile averaging, freshwater separation and pure preview reads.

The preset suite contains **22 automated assertions**, including all three normal save round-trips and rejection of mismatched shape/tribe-count settings. The tables above were measured from the generated seed-11 world; final test and rebuilt-player evidence is recorded in [Validation.md](Validation.md). This establishes reproducibility and environmental contrast; it does not establish multi-generation balance or player usability.
