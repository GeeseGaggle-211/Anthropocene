# Generator 9: what failed, what changed, and the actual worlds

September 13, 2026. This is a replacement of the geography and atmospheric-generation model, not a recoloring of the atlas. All example maps were rendered from Anthropocene's authoritative simulation state. The old generator was preserved and run with the same Standard / Continents / seed 1729 / 80-tribe settings for comparison.

## Why the initial result failed

The implementation and its acceptance checks were too weak for the intended world. We tested local playability, deterministic replay and valid resource state, but did not inspect and test planetary morphology. A named tectonic or hydrology stage was mistaken for evidence that the process produced credible geography.

- **The coastline was effectively locked to plate polygons.** Continental plates started at height 0.57, ocean plates at 0.23, with sea level 0.45. The single sine/cosine elevation pattern varied by at most 0.11: continental interiors stayed above water and ocean interiors stayed below it. Local smoothing could not fix that categorical step.
- **Larger maps became drier simply because they had more tiles.** Humidity lost precipitation and another 3% at each tile, and every latitude row was isolated. At typical default rain fractions, only about 6–19% of the original humidity remained after 25 land tiles. A 1,024-column planet therefore had narrow wet margins and enormous dry interiors.
- **A drainage correction was treated as permanent water.** The priority flood raised depressions to their spillways and labelled every sufficiently deep correction as Lake. Broad sine-wave basins behind narrow ridges became huge flat reservoirs, whether or not they should retain water.
- **Hotspots were only overlapping bumps.** Four radius-two bumps all advanced east/northeast. Origins could be on continents and the track ignored plate motion. Most tracks did not become recognizable island chains.

The new morphology suite was also run against that preserved implementation. It fails **10 of 20 checks**, despite the old world's 80 viable starts. This is why playability checks alone did not catch the problem.

## What replaced it

The references informed the design directly: [Polygonal Map Generation](http://www-cs-students.stanford.edu/~amitp/game-programming/polygon-map-generation/) separates terrain, drainage and moisture concerns; [Mapgen4's actual source](https://github.com/redblobgames/mapgen4/blob/main/map.ts) combines terrain at several scales, extracts upwind moisture, applies orographic rain, and accumulates drainage flow in reverse traversal order. [Red Blob's noise tutorial](https://www.redblobgames.com/maps/terrain-from-noise/) also explains combining scales and wrapping worlds. These concepts were implemented in original C# around our existing hex simulation; no JavaScript renderer, replacement simulation or external dependency was imported. Detailed source and licensing notes are in [WorldGenerationReferences.md](WorldGenerationReferences.md).

**Continents and relief.** Branching crustal fragments provide varied continental axes and arms. Continuous, longitude-periodic noise bends those fields at multiple scales, producing gulfs, peninsulas, coastal islands and shelves. Continental shape no longer follows categorical plate ownership. Warped tectonic provinces and their relative movement instead influence broad mountain belts, trenches and volcanic margins. Pangaea uses a bounded supercontinent field; Archipelagos uses smaller fragments. Their shapes were inspected separately, not inferred from the Continents result.

**Hotspots.** Marine origins generate tracks aligned with plate motion. Individual islands vary in spacing, radius and peak height; older features shrink and subside. Euclidean cone profiles replace the identical hexagonal bumps. The tests compare hotspot-enabled terrain with the exact same terrain without hotspots and verify that formerly marine tiles become multiple distinct, varied islands.

**Climate.** A bounded atmospheric grid transports humidity in two dimensions over normalized planetary distances. It includes curved seasonal wind belts, meridional exchange, ocean evaporation, maritime moderation, windward rain, leeward drying and partial land recycling. Regional climate moisture still excludes river/lake/oasis subsidies, preserving productive desert river valleys. A matched 192-column versus 768-column interior differs by less than 1% in the rainfall test. A controlled ridge increases windward rainfall from 0.0136 to 0.0423 and reduces leeward rainfall from 0.0062 to 0.0032.

**Drainage.** Routing surfaces are separated from retained lakes. Large or insufficiently supplied basins breach their spill paths, including nested barriers; sufficiently deep, smaller, supplied basins retain freshwater. Basin area scales with world resolution, and residual filled land is measured so replacing a giant blue lake with a giant dry plateau cannot conceal the defect. The lake screen compares dimensionless precipitation and evaporation proxies; it is not a measured physical water budget.

**Performance and integration.** Wetter terrain exposed pathological hash collisions in packed river-edge keys. Mixing the key bits preserves deterministic lookup results and reduced the river stage from roughly 25–30 seconds during development to roughly 0.6–1.2 seconds in final exports. The simulation stays Unity-independent. The three playtest presets were reselected from natural seed-5 camps; no resources, populations or technologies were boosted. The developer preview now includes elevation and volcanic-activity layers.

## Measured results and world copies

Every final world below is Standard size, 1,024 × 512, with 80 tribes and unchanged gameplay tuning. Percentages have different denominators: land and lakes are shares of the whole world; desert and forest climates are shares of land. “Forest climates” means Temperate + Tropical environment, not an exact tree-cover estimate; Cold can also support taiga.

| World | Land | Lakes | Desert land | Forest climates | Generation time |
| --- | ---: | ---: | ---: | ---: | ---: |
| **Old Continents 1729** | 49.87% | 14.58% | 34.88% | 0.54% | Historical baseline |
| Continents 1729 | 27.22% | 0.0845% | 11.15% | 46.63% | 6.01 s |
| Continents 42 | 35.01% | 0.0498% | 17.08% | 48.22% | 4.81 s |
| Continents 31415 | 33.27% | 0.0216% | 10.88% | 40.78% | 4.51 s |
| Continents 5 | 32.36% | 0.0446% | 14.48% | 37.27% | 4.70 s |
| Pangaea 42 | 25.13% | 0.0681% | 36.10% | 29.72% | 8.19 s |
| Archipelagos 42 | 15.10% | 0.0130% | 8.68% | 67.61% | 7.49 s |

Times are measured native .NET Release export runs on this workstation, not Unity load-time promises. Pangaea deliberately retains a broad arid interior and wetter coasts. These are measured examples, not forced global biome quotas.

The generated deliverables are under `TestResults/WorldGeneration`:

- `After/index.html`: gallery of all six final worlds, with environment, elevation, food, freshwater and volcanic diagnostics.
- `Anthropocene-worlds-generator-9.zip`: six separate playable saves, 30 PNG maps, configuration, measured metrics, provenance and gallery files. About 305 MB; no user save slot was touched.
- `Before/continents-1729`: the preserved generator-8 reference, including its actual map and save.
- Each final world's manifest records the simulation-source fingerprint, world fingerprint and exact configuration. Exports can be reproduced with `Tools/WorldAtlas/Export-Worlds.ps1`; images are not generated art or screenshots borrowed from the references.

## Validation and limits

The final suite passes **357 fixed checks** and **19 Unity tests**. Eight Standard worlds have **640/640 viable starts**. The six exported worlds have **480/480 viable starts** and no connected residual filled-land patch deeper than 0.008. Final environment, elevation and volcanic maps were inspected at world scale. The 20 morphology checks protect the representative continent/lake/vegetation/coast/hotspot defects; 20 drainage checks cover wet/dry bowls, small retained lakes, oversized basins, nested barriers, freshwater, flow conservation, acyclic downhill routing and longitude seams. Fifteen climate checks cover scale, windward/leeward response, cross-latitude moisture and repeatability.

The broader suite passes 30 Small-world opening simulations, an 80-tribe Standard opening/save round trip and 36 shape/seed geography checks. Small worlds retained 120/120 surviving tribes, zero illegal orders and all three first breakthroughs (16 Husbandry / 84 Cultivation / 20 Watercraft). Median final population was 116, with quartiles 92/136 and maximum 233. These are new generated cohorts, not a controlled food-balance comparison with the old geography. [Validation.md](Validation.md) records final test and player evidence.

This is plausible procedural game geography, not a geophysical history or fluid simulation. Raw tectonic diagnostic fields can still show province boundaries. Climate is four steady seasonal profiles, not live weather. Lake evaporation is not fed back into the regional atmosphere; the visible river graph treats lakes as outlets rather than drawing flow-through lake channels. Those approximations are explicit and do not reintroduce the old categorical coastlines or world-scale flood basins.

New worlds use **generator 9**. Existing generator-7 and generator-8 saves remain supported and restore their original geography without regeneration. Start a new world to use the corrected generator; loading an old world intentionally preserves it.
