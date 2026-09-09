# Milestone two — readable geography and expedition ecology

Historical implementation notes. See [milestone three](Milestone3.md) for the current generation-length turns, elevation contours, stack interaction and hover information.

The simulation remains independent of Unity. The implementation adds supply-path logistics, fractional population growth, regional herds, biome/relief separation, rivers on hex boundaries, coastal shallows, oases and explicit freshwater. The original Drive text is preserved in `DesignSpec.txt`; this document records the later requirements and concrete tuning decisions.

## Tilesets and information

`ITileRenderer.Draw(TileRenderContext)` receives filtered tile observations and semantic entity keys. It does not receive the authoritative world. `LayeredTileRenderer` composes biome ground, relief, water features, improvements, food markers and entities. Rivers are line segments on the actual shared hex edges, with width and flow arrows. Occupied tiles move the biome glyph aside so it cannot obscure a unit. Stacks have a marker and the inspector lists each visible entity.

The two JSON files in `Assets/_Anthropocene/Resources/Tilesets` provide classic and high-contrast mappings. V or the menu switches sets without changing simulation state. Each file has version 1, a unique `Id`, a `Name` and `Definitions`. Definitions have unique `Key` values, `Glyph`, HTML `Tint`/`Background`, `Scale`, and optional `SpritePath`. A `fallback` key is mandatory. Examples: `biome.desert`, `relief.hills`, `feature.river`, `feature.oasis`, `species.camel`, `entity.camp`.

To use sprites, import a texture as Sprite (2D and UI) beneath a Unity Resources folder and set `SpritePath` to its Resources-relative path without extension. The renderer loads the sprite and uses its texture rectangle; missing sprites fall back to the configured glyph with a warning. No sprite assets or external font/library installation are required by the current glyph sets. New renderers can implement the same interface without changing saves or simulation rules.

Relief glyphs are `^` Hills, `^^` Highland and `M` Mountain; the tile inspector also names Lowland. `o` identifies an oasis while its biome remains Desert. `w` identifies accessible freshwater. Marine tiles explicitly say saltwater is undrinkable. Food markers and qualitative inspection remain consistent across tilesets. Help is scrollable.

## Logistics and demographic tuning

`SupplyRange = 3` counts a traversable hex path from source camp to hunters. It is independent of visibility and does not restrict movement. Watercraft can permit a path through shallows; deep ocean remains impassable. Connected hunters contribute harvest to camp and share its food. Before movement, connected groups can withdraw up to three base-turn rations, leaving a half-turn reserve for people at camp. Carry capacity is five food per hunter. Withdrawals, deposits, retained harvest, overflow and ending inventory are recorded separately.

Disconnected groups consume carried food and accumulate their own deficit and starvation grace. Distant harvest never teleports into camp. Re-entering supply deposits inventory before harvesting and eating; reintegration at camp then returns population. AI sees its own supply/provision status and favors returning when provisions are depleted, hunger develops or carry capacity fills.

After consumption, a fed camp gains abstract net demographic progress at `HumanGrowthRate = 0.0125` per 90-day base turn. Progress scales as `(1 + rate)^(duration/baseDays) - 1`, multiplied by a reserve factor up to one at 1.5 turns of camp food. Hunger blocks growth. Whole people are added at camp and fractional progress persists. This is a tunable opening-game abstraction, not an age/cohort or realistic birth model. Deaths and positive growth have separate population events and conservation checks.

## Geography and wildlife

Biome derives from climate/moisture. Relief derives independently from continuous elevation above sea level: Hills at 0.08, Highland at 0.20, Mountain at 0.34. Their configurable productivity multipliers are 0.95, 0.8 and 0.5. A wooded hill remains Woodland. The legacy Mountain terrain enum is retained for source compatibility but generation uses the relief field.

Center-based drainage establishes flow and depressions. A separate pointy-hex corner graph generates rivers: vertices are shared corners, edges are shared tile sides, and the horizontal seam wraps. Priority flooding produces a directed acyclic drainage network with finite accumulated runoff; edges above the threshold connect downstream to water outlets. Both neighboring tiles bind the same edge ID. Validation checks direction, geometry, bank correspondence, duplicate edges, continuity, nondecreasing flow and cycles. Source headwaters and explicit water mouths are intentional endpoints. Surface heights include spill elevation in depressions; these are not claimed to be a full erosion simulation.

After elevation and hydrology changes, any Ocean tile adjacent to Land becomes Shallows, including across the wrap seam. Lakes remain freshwater and are never reclassified as marine coasts.

Wild herds use configurable 32 × 32 regions, 0.008 herds per eligible land tile and ±35% seeded density variation. Weighted sampling uses species habitat, forage and freshwater capacity; each tile initially holds at most one wild herd. Start placement requires accessible habitat, and a final pass ensures every camp has an appropriate herd reachable on land within opening vision radius 2.

Species definitions live in `OpeningConfig.json`: stable string IDs, biome weights, relief/oasis preference, food/freshwater needs, growth, yields, sizes and migration preference. Initial species are antelope, goat, bison, camel and horse. Camels favor desert/oasis habitat and consume less freshwater; horses favor grasslands, with weaker tundra and woodland suitability. Hunting practice and domestication selection use stable IDs. Habitat weights bias placement; they do not require every individual to occupy the highest-weight biome.

## Oases and water meaning

Oases are a separate semantic feature on non-mountain Desert land. A viability score combines accumulated drainage/recharge proxy (70%), depression depth (20%) and rainfall (10%). Seeded weighted candidates must clear 0.18 viability, remain more than three hexes from ordinary surface water/rivers, and stay at least seven hexes apart. Per 32 × 32 region, target density is 0.003 per desert tile; regions with at least 100 desert tiles target at least one when a viable, sufficiently separated candidate exists. Thus broad desert supports occasional oases without inventing groundwater everywhere.

An oasis has finite freshwater capacity 70, food floor 38, food recovery 0.3 and freshwater recovery 0.14 per base turn. Its adjacent land receives 30% influence/capacity floors; overlapping influence uses a maximum, not additive infinite stock. Extraction uses the same proportional finite-resource allocator as other sources. Oasis influence raises camel suitability and influences migration. The model is a seeded physical proxy, not a groundwater aquifer simulator.

`SeasonData.Moisture` is soil moisture for vegetation. `Tile.Freshwater` is drinkable stock only when a freshwater source is present. `Tile.Saltwater` is separate marine stock and never satisfies drinking claims. Rivers, lakes, rain pools and oases are explicit freshwater sources. Rain pools require rainfall and drainage thresholds; ordinary wet soil does not automatically provide drinking water. Food on marine tiles regenerates using the marine envelope independently of freshwater availability.

Land herds can drink on their tile or from an adjacent bank/shore. `FreshwaterAccess.Source` deterministically selects the richest accessible freshwater source; allocation removes water from that actual tile. It never copies a lake's stock onto land. Start viability also requires freshwater in the gathering neighborhood, so sea alone cannot validate a camp. Human drinking consumption is not a separate resource budget in this opening; the explicit access query and fresh-only start gate are available for that later extension.

## Persistence and validation

Versioned saves retain river edges and flow, relief, water types/sources/stocks, oasis fields, species definitions and practice, hunter inventory/deficits, growth remainders and events. Loads rebuild shared river bindings and validate state; they never rerun generation. Version-two saves use a new filename and incompatible older files are rejected without deletion.

Run `Tools/Test-Headless.ps1 -Seeds 30 -Standard -Geography` for focused rules, reversed-order replay, preset generation and the full-sized benchmark. Add `-Audit` for the eight Standard-world geography/ecology comparison. Unity EditMode checks cover resource-loaded tilesets/configuration in addition to the simulation integration suite. Results and current balance limitations are in `Validation.md`.
