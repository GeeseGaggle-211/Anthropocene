# Playtest starts and developer atlas

The opening menu offers three environmental presets alongside Custom world. Each uses Standard / Continents / seed **1729** / **80 tribes** and normal tuning. They select an existing generated camp as the human tribe by exchanging player labels; geography, society policies, IDs, spacing, food, reserves and wild herds are unchanged. No technology is granted. Historical names are environmental analogues, not reconstructed civilizations.

| Preset | Camp tile | Local setting | Land food | Aquatic food | Start viability |
| --- | ---: | --- | ---: | ---: | ---: |
| Desert river valley | 217996 | Warm desert, five riparian tiles and two dry tiles in radius 1 | 114.34 | 0 | 180.00% |
| Steppe hunters | 148177 | Five open grassland tiles, two desert tiles, a freshwater river; nearby bison | 34.41 | 0 | 123.87% |
| Cold coastal fishers | 408950 | Cold shore, land freshwater, five adjacent shallows | 1.68 | 255.17 | 180.00% |

Food columns are **annual-mean renewal potential per 91.25-day base season**, summed within radius 1. They are neither current stocks nor realized harvests. They use the same seasonal capacities, rates and sustainable production fraction as the viable-start check. Actual gathering shares one labor ceiling across land and passive coastal food; depletion, competing animals, workforce choices, freshwater and seasonal reserves also matter. The 180% ceiling here is the starting gathering labor ceiling, not a promise of population growth. All three meet the normal 85% viability threshold. The unequal densities are deliberately preserved for comparison.

For playtesting, compare generations with similar dispatched populations and note food produced/eaten, reserves, deaths, growth and first breakthrough in the existing outcome panels. Try river gathering, steppe expeditions onto the nearby bison, and coastal hunter fishing orders. Choosing the preset does not force its expected technological path.

**Build world preview (dev)** generates the selected setup and opens a full-world atlas before play. **Menu → Preview current world (dev)** inspects the existing committed world. Environment, renewable food and freshwater renewal layers share the same coordinates. Cycle annual mean and four seasonal profiles; the heatmaps use a fixed logarithmic scale from zero to 100+ units per tile per base season. These show stored profile potential; the tile inspector separately projects current food and freshwater stock without materializing ecology.

Wheel zooms, dragging pans, clicking selects a tile, **Fit world** resets framing, and **Find my camp** zooms to the human start. White markers show camps, gold marks the human camp, and numbered markers identify the three preset sites when present. This is a rectangular atlas of axial rows, not the normal hex renderer. The tile inspector shows environment, vegetation, elevation, river, source, temperature and seasonal renewal. **Play current start** returns to the normal filtered game. Previewing does not add knowledge to the tribe, alter queued orders, advance time or change saves.

The developer atlas intentionally reveals hidden geography and camp locations and is labelled accordingly. It is available in the prototype; do not mistake it for a player observation. Generation/resolution guards prevent inspection while a worker mutates the world. Textures are built on demand and reused until the layer, season or committed world changes. Preview settings and preset names are UI state, not new save fields; existing save compatibility is unchanged, and the selected camp's ownership already round-trips through the normal save format.

The fixed preset habitat and viability checks fail explicitly if future generation/tuning invalidates a site. Update and remeasure the presets in that case; do not inject food to preserve the table. `ScenarioChecks` exercises the actual generated world, ownership-only changes, filtered observations, exact save restoration, profile averaging, freshwater separation and pure preview reads.
