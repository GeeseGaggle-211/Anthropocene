# Playtest starts and developer atlas

The opening menu offers three environmental presets alongside Custom world. Each uses Standard / Continents / seed **5** / **80 tribes** and normal tuning. They select an existing generated camp as the human tribe by exchanging player labels; geography, society policies, IDs, spacing, food, reserves and wild herds are unchanged. No technology is granted. Historical names are environmental analogues, not reconstructed civilizations.

These sites were reselected for generator 9 after the terrain, drainage and atmospheric-transport correction. The former seed-1729 sites no longer describe the same habitats. Seed 5 supplies all three requested openings in one natural world; it was selected for the environmental contrast, without running simulations to pick technological outcomes.

| Preset | Camp tile | Local setting | Land food | Aquatic food | Start viability |
| --- | ---: | --- | ---: | ---: | ---: |
| Desert river valley | 309013 | 22.3°C mean; seven desert tiles, five river tiles; two dry margins | 153.30 | 0 | 180.00% |
| Steppe hunters | 176325 | 16.1°C mean; seven open grassland tiles, two river tiles; reachable bison | 45.11 | 0 | 174.72% |
| Cold coastal fishers | 98992 | 1.8°C mean; five wooded Cold tiles, two adjacent shallows; land rain pools | 13.85 | 83.56 | 148.67% |

Food columns are **annual-mean renewal potential per 91.25-day base season**, summed within radius 1. They are neither current stocks nor realized harvests. They use the same seasonal capacities, rates and sustainable production fraction as the viable-start check. Actual gathering shares one labor ceiling across land and passive coastal food; depletion, competing animals, workforce choices, freshwater and seasonal reserves also matter. The 180% ceiling here is the starting gathering labor ceiling, not a promise of population growth. All three meet the normal 85% viability threshold. The unequal densities are deliberately preserved for comparison.

The four stored seasonal profiles show why the fisher's annual mean is insufficient by itself:

| Preset / food source | Season 1 | Season 2 | Season 3 | Season 4 | Minimum local freshwater capacity across seasons |
| --- | ---: | ---: | ---: | ---: | ---: |
| Desert river / land | 157.42 | 160.23 | 157.84 | 137.70 | 470.93 |
| Steppe / land | 39.89 | 41.44 | 43.93 | 55.17 | 173.92 |
| Cold shore / land | 12.84 | 0.65 | 12.84 | 29.06 | 31.25 |
| Cold shore / shallows | 77.34 | 12.47 | 77.34 | 167.09 | 0: saltwater cannot be drunk |

Food is renewal potential in the same units as the first table; freshwater is **capacity**, not a food yield or a promise of realized recharge. Season 2 is the cold fisher's lean winter profile, and Season 4 is its productive summer. The viable-start rule checks every season's freshwater and annual renewable food with the normal shared labor limit. The seasonal reserve test is therefore more informative than comparing full initial food stocks.

For playtesting, compare generations with similar dispatched populations and note food produced/eaten, reserves, deaths, growth and first breakthrough in the existing outcome panels. Try river gathering, steppe expeditions onto the nearby bison (tile 175303), and coastal hunter fishing orders. The steppe camp accesses a nearby river rather than standing on its bank. The fisher's two shallows provide most renewable local food; its wooded shore supports less human food and must still supply drinking water. Choosing the preset does not force its expected technological path.

**Build world preview (dev)** generates the selected setup and opens a full-world atlas before play. **Menu → Preview current world (dev)** inspects the existing committed world. Environment, elevation, volcanic activity, renewable food and freshwater renewal layers share the same coordinates. Cycle annual mean and four seasonal profiles; the heatmaps use a fixed logarithmic scale from zero to 100+ units per tile per base season. These show stored profile potential; the tile inspector separately projects current food and freshwater stock without materializing ecology.

Wheel zooms, dragging pans, clicking selects a tile, **Fit world** resets framing, and **Find my camp** zooms to the human start. White markers show camps, gold marks the human camp, and numbered markers identify the three preset sites when present. This is a rectangular atlas of axial rows, not the normal hex renderer. The tile inspector shows environment, vegetation, elevation, river, source, temperature and seasonal renewal. **Play current start** returns to the normal filtered game. Previewing does not add knowledge to the tribe, alter queued orders, advance time or change saves.

The developer atlas intentionally reveals hidden geography and camp locations and is labelled accordingly. It is available in the prototype; do not mistake it for a player observation. Generation/resolution guards prevent inspection while a worker mutates the world. Textures are built on demand and reused until the layer, season or committed world changes. Preview settings and preset names are UI state, not new save fields; existing save compatibility is unchanged, and the selected camp's ownership already round-trips through the normal save format.

The fixed preset habitat and viability checks fail explicitly if future generation/tuning invalidates a site. Update and remeasure the presets in that case; do not inject food to preserve the table. `ScenarioChecks` exercises the actual generated world, ownership-only changes, filtered observations, exact save restoration, profile averaging, freshwater separation and pure preview reads.

The generator-9 preset revalidation passed **22 automated assertions**, including all three normal save round-trips and rejection of mismatched shape/tribe-count settings. The tables above were measured after the final basin-retention adjustment, from the same generated seed-5 world. This establishes reproducibility and environmental contrast; it does not establish multi-generation balance or player usability.
