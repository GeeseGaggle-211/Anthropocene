# World generation: references and failure analysis

Reference review: September 13, 2026. This document distinguishes the sources' algorithms, the diagnosed generator-8 defects, and recommendations for Anthropocene. Validation notes and the implemented generator remain authoritative for what has actually shipped.

## Primary sources reviewed

### Polygonal Map Generation for Games

Amit Patel's [original article](http://www-cs-students.stanford.edu/~amitp/game-programming/polygon-map-generation/) builds island maps around gameplay constraints. It separates the adjacency graph from the corner/boundary graph, allowing movement between regions and rivers along their borders. The coastline may come from noise or another shape function. Water connected to the exterior is distinguished from enclosed water by flood fill. Its elevation increases with distance from the coast; river flow joins downhill and river width follows the square root of accumulated flow. Noisy boundaries conceal the coarse polygons without crossing neighboring edges.

These ideas can be used on hex grids; switching Anthropocene to Voronoi cells is not necessary. The article's moisture-by-distance-to-freshwater and monotonic inland elevation are specific design choices, not general physical models. Adopting them literally would erase our dry river valleys and force mountains toward island interiors. Its process retrospective also emphasizes visualizing intermediate data to catch bugs that plausible code can conceal. The original page was retrieved over the user-supplied HTTP URL after the HTTPS endpoint failed.

### Mapgen4

The [Mapgen4 repository](https://github.com/redblobgames/mapgen4) is an interactive wilderness generator. Its stated priorities are rapid regeneration and attractive output, rather than physically accurate terrain. It is a reference for coherent procedural structure, not an Earth simulator or a ready-made tectonic solution.

The actual [map.ts implementation](https://github.com/redblobgames/mapgen4/blob/main/map.ts), reviewed on the date above, contains these relevant operations:

- `precalculateNoise` caches several independently offset spatial frequencies.
- `assignTriangleElevation` starts from an interpolated coarse constraint field, adds fine coast variation, and mixes low hills with a distance field from mountain peaks. Land and ocean elevation treatments differ.
- `assignRainfall` visits regions in wind order, averages humidity from upwind neighbors, replenishes it over water, and extracts orographic rainfall where elevation restricts humidity.
- `assignDownslope` explores outward from the ocean with an elevation-priority queue. `assignFlow` accumulates discharge in reverse traversal order and can lower downstream terrain to remove uphill channels.

These are separable algorithms. Importing its browser renderer, painting interface, mesh libraries or TypeScript toolchain would not itself fix Anthropocene's climate, lakes or hotspot placement.

Patel's [rainfall development notes](https://simblob.blogspot.com/2018/09/mapgen4-rainfall.html) explain the advantage of deriving rivers from rainfall: a river can carry water from wet uplands through dry terrain. They also identify limitations of straight wind transport, including artificial green corridors behind mountain passes. Consequently, repeating the same approach along isolated horizontal rows would preserve one of our main defects. A cylinder also needs different boundary handling from Mapgen4's finite island canvas.

### Multiscale and wraparound noise

Patel's [noise terrain tutorial](https://www.redblobgames.com/maps/terrain-from-noise/) explains combining spatial frequencies, separating temperature/moisture from elevation, reshaping landmass distributions, and constructing seamless cylindrical noise. These support a continuously varying continent field and varied coast detail. Noise alone does not provide drainage, tectonic history or a climate model; those require separate constraints.

## What was wrong with generator 8

The following findings come from Anthropocene's own `WorldGenerator.cs` and `Hydrology.cs` before the replacement. They explain mechanisms, rather than attributing intent to a previous designer.

| Visible symptom | Cause in the old implementation | Required direction |
| --- | --- | --- |
| Oceans and coasts following long geometric lines | A weighted nearest-center plate partition directly chose a base height of `0.57` or `0.23`. The `0.34` discontinuity straddled sea level. The added elevation variation was only one product of broad sine/cosine waves, followed by two very local smoothing passes. | Separate continental shape from categorical plate ownership. Use a continuous field with variation at several scales; preserve longitude wrapping. Let plate interactions influence relief without making every plate edge a shoreline. |
| Thin wet coastal strips and enormous dry interiors | Humidity lost both rainfall and a `0.97` multiplier at every tile. At common lowland rain fractions the remaining humidity fell to roughly 6–19% after 25 land tiles. Standard worlds have 1,024 columns, but transport distances were not normalized to that scale. Every row was an isolated east/west calculation. | Define transport at an explicit spatial scale, exchange moisture across rows, retain latitude/season effects and rain shadows, and test the same geographic pattern at multiple resolutions. |
| Huge lakes and flattened interiors | The priority flood treated every filled depression deeper than `0.018` as permanent lake water and raised its elevation to the spill level. Broad smooth basins and narrow uplift barriers could therefore flood enormous regions. There was no distinction between a drainage correction and a water body supported by its catchment. | Resolve drainage connectivity separately from choosing retained lakes. Use basin/outlet structure and water availability; constrain pathological flooding and preserve connected downstream flow. |
| Hotspots failing to read as island chains | All chains advanced east/northeast by fixed offsets. Origins could lie on existing continents; four radius-two bumps overlapped; older bumps decayed quickly. The calculation did not use the plate movement vectors. | Prefer marine origins for oceanic chains, use plate motion to define the age progression, and scale spacing/radius so several separate islands can actually emerge. |
| Automated checks passed anyway | Existing checks emphasized nonnegative resources, river connectivity, coast adjacency, viable camps and replay. Those are necessary but do not measure continental silhouettes, largest lake area, inland climatic diversity or visible hotspot chains. | Keep simulation invariants and add morphology metrics plus full-world and close-up image inspection across multiple ordinary seeds. |

This was a modeling and validation failure. A functioning tectonic stage name and deterministic output did not establish that the generated geography matched the intended world. A playable patch around each selected camp also did not establish that the rest of the planet was credible.

## Adaptation boundaries for Anthropocene

The recommended approach is a fresh C# implementation using the existing hex topology and keyed random streams. Preserve the simulation's authoritative tile geography, dual-boundary rivers, seasonal regional/local moisture distinction, start viability calculation and saved-world restoration. Geometry and climate improvements must not be disguised as food boosts at preset camps.

Coastline noise, basin drainage and coarse moisture transport should be checked independently before ecology and placement. A successful delivery should show the actual generated worlds, record their seeds/configuration/version, and include morphology and gameplay checks. Diagnostic images must be exported from actual generated state; a hand-drawn replacement or a reference generator's screenshot is not evidence that our generator improved.

Hotspot tracks and broad plate interactions are procedural proxies. Unless geological history, crustal age, plate motion through time and climate circulation are explicitly simulated, the result should be described as geographically plausible game terrain, not scientifically reconstructed continents. Islands may also arise from flooded terrain or boundary volcanism; not every island should be labelled a hotspot.

## Provenance and code reuse

The inspected [Mapgen4 license](https://github.com/redblobgames/mapgen4/blob/main/LICENSE) is Apache 2.0, and `map.ts` carries Red Blob Games' 2018 copyright notice. If any code is copied or translated, retain applicable notices, include the license in source and distributed builds, identify modifications in the adapted files, and preserve any applicable upstream NOTICE attribution. Record the source revision with the imported material. A conceptual reference should be credited accurately as such; do not claim a port when the implementation is independent.

The [original ActionScript mapgen2 license](https://github.com/amitp/mapgen2/blob/master/LICENSE) is MIT. Copies or substantial portions require its copyright and permission notice. This differs from Mapgen4 and the later JavaScript rewrite; do not assume all Red Blob projects share one license.

Mapgen4's dependency licenses are listed separately in its [README](https://github.com/redblobgames/mapgen4#license). Review those only if the corresponding libraries are actually imported. No reference code or third-party package is added by this document itself.
