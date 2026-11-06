# Playtest starts and developer atlas

The opening menu offers three environmental presets alongside Custom world. Each uses Standard / Continents / seed **11** / **80 tribes** and normal tuning. They select an existing generated camp as the human tribe by exchanging player labels; geography, society policies, IDs, spacing, food, reserves and wild herds are unchanged. No technology is granted. Historical names are environmental analogues, not reconstructed civilizations.

These sites were rechecked among naturally generated camps under generator 15's physical altitude climate and the land harvest-before-recovery profile. The warm river site moved from 225046 to 223410 because the former site cooled below the preset's temperature criterion. The steppe and Temperate coastal sites remain. Legacy-altitude worlds retain the old river marker and preset. Seed 11 supplies all three openings without changing landscape, stocks, herds or camp placement to fit the presets.

| Preset | Camp tile | Local setting | Land food | Aquatic food | Start viability |
| --- | ---: | --- | ---: | ---: | ---: |
| Desert river valley | 223410 | Desert river tiles | 610.51 | 0 | 180.00% |
| Steppe hunters | 413877 | Open grassland, freshwater nearby, reachable horse or bison | 64.96 | 0 | 145.80% |
| Coastal fishers | 117495 | Temperate shore, three adjacent Shallows, land freshwater | 40.51 | 112.98 | 180.00% |

Food columns are **annual-mean renewal potential per 91.25-day base season**, summed within radius 1. They are neither current stocks nor realized harvests. Start viability now credits conservatively repeatable **opening land stock** after a depleted tile recovers through the preceding turn, plus aquatic renewal, under one shared labor ceiling. It does not simply divide the food columns by current demand. Depletion, competing animals, workforce choices, freshwater and reserves also matter. The 180% ceiling is the starting gathering labor ceiling, not a promise of population growth. All three meet the normal 85% viability threshold.

The four stored seasonal profiles show the local variation behind those annual means:

| Preset / food source | Season 1 | Season 2 | Season 3 | Season 4 | Minimum local freshwater capacity across seasons |
| --- | ---: | ---: | ---: | ---: | ---: |
| Desert river / land | 610.75 | 610.01 | 610.43 | 610.87 | 514.30 |
| Steppe / land | 55.20 | 143.43 | 54.89 | 6.32 | 227.53 |
| Temperate shore / land | 37.73 | 24.27 | 41.61 | 58.41 | 67.89 |
| Temperate shore / shallows | 103.93 | 35.29 | 103.93 | 208.78 | 0: saltwater cannot be drunk |

Food is renewal potential in the same units as the first table; freshwater is **capacity**, not a food yield or a promise of realized recharge. The coastal site's fourth season has the strongest shallow potential while the steppe site's fourth season is lean. The viable-start rule checks every season's freshwater and the repeatable land stock plus aquatic renewal under the shared labor limit. Turn-horizon harvest can consume only opening land food; recovered land food enters the next turn.

For playtesting, compare generations with similar dispatched populations and note food produced/eaten, reserves, deaths, growth and first breakthrough in the existing outcome panels. Try river gathering, steppe expeditions toward reachable horse or bison herds, and coastal hunter fishing orders. The fisher's three shallows provide most local renewal potential, while land freshwater supplies drinking water. Choosing the preset does not force its expected technological path.

**Build world preview (dev)** generates the selected setup and opens a full-world atlas before play. **Menu → Preview current world (dev)** inspects the existing committed world. Environment, elevation, volcanic activity, renewable food and freshwater renewal layers share the same coordinates. Cycle annual mean and four seasonal profiles; the heatmaps use a fixed logarithmic scale from zero to 100+ units per tile per base season. These show stored profile potential; the tile inspector separately projects current food and freshwater stock without materializing ecology.

Wheel zooms, dragging pans, clicking selects a tile, **Fit world** resets framing, and **Find my camp** zooms to the human start. White markers show camps, gold marks the human camp, and numbered markers identify the three preset sites when present. This is a rectangular atlas of axial rows, not the normal hex renderer. The tile inspector shows environment, vegetation (plankton/algae on water), elevation, river, source, temperature and seasonal renewal. **Play current start** returns to the normal filtered game. Previewing does not add knowledge to the tribe, alter queued orders, advance time or change saves.

The developer atlas intentionally reveals hidden geography and camp locations and is labelled accordingly. It is available in the prototype; do not mistake it for a player observation. Generation/resolution guards prevent inspection while a worker mutates the world. Textures are built on demand and reused until the layer, season or committed world changes. Preview settings and preset names are UI state, not new save fields; existing save compatibility is unchanged, and the selected camp's ownership already round-trips through the normal save format.

The fixed preset habitat and viability checks fail explicitly if future generation/tuning invalidates a site. Update and remeasure the presets in that case; do not inject food to preserve the table. `ScenarioChecks` exercises the actual generated world, ownership-only changes, filtered observations, exact save restoration, profile averaging, freshwater separation and pure preview reads.

The preset suite contains **22 automated assertions**, including all three normal save round-trips and rejection of mismatched shape/tribe-count settings. The tables above were measured from the generated seed-11 world; final test and rebuilt-player evidence is recorded in [Validation.md](Validation.md). This establishes reproducibility and environmental contrast; it does not establish multi-generation balance or player usability.
