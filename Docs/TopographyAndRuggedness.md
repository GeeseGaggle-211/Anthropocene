# Topography and ruggedness

Status: implemented on `feature/topography-ruggedness`; **draft and not approved for merge**. Windows build, EditMode tests and the nine-frame Unity capture route passed on 6 November 2026 (see the follow-up below). Full prototype parity, frame profiling and integration with newer `main` remain unaccepted. The implementation baseline is `d4f949a7eb83a59150741222e410fbbfbc196f2d` (`origin/main`, 18 October 2026).

## Contract

`Tile.Elevation` remains the only authoritative continuous height field. The feature does not create or modify elevation, elevation levels, relief, drainage, ecology, placement, movement, combat, or turn resolution. The existing ten gameplay elevation levels and level-10 impassability rule are unchanged.

The read-only `WorldState.TerrainMetrics` cache exposes continuous height, fitted local gradient/slope, relative height, ruggedness, and neighborhood sample count. It is derived state: it is excluded from serialization and fingerprints and is rebuilt deterministically from saved elevations after load.

## Ruggedness

For each tile, collect the radius-four hex neighborhood. An interior neighborhood has 61 cells. Cylindrical longitude wraps; cells beyond the two polar rows are omitted. The fit uses pointy-top axial centers scaled so adjacent centers are one distance unit apart:

```text
x = dq + dr / 2
y = sqrt(3) dr / 2
h(x,y) = a + bx + cy
R = sqrt(mean((h_actual - h_plane)^2))
score = 100 R / (R + 0.005)
```

Height and `R` use the generator's normalized elevation units. `b`, `c`, and the reported slope use normalized elevation per center-to-center hex spacing. The displayed score is dimensionless on a common 0–100 scale. The `0.005` term is provisional display calibration, not a physical landform threshold.

The centered least-squares calculation removes a constant height offset and any locally uniform incline. Flat ground and a perfect plane therefore score approximately zero; local departures from that plane increase the score. The polar fit uses every valid cell in its partial neighborhood and records its actual sample count.

The canonical cache stores three `float` arrays (two gradient components and ruggedness) and one `byte` sample-count array: 13 bytes per tile plus normal managed-array/object overhead. A Standard 1024×512 world therefore has 6,815,744 bytes (6.50 MiB) of payload. Player generation and load prewarm it on their existing background worker. The headless simulator can construct it lazily through the same thread-safe read-only interface.

The player view deliberately does not read the canonical full-world values. `KnownTerrainAnalysis` fits only heights present in the filtered observation: current visible geography plus remembered static geography. It produces metrics only for currently visible tiles. A score based on incomplete knowledge is labelled `Observed ruggedness` with its known/possible sample count. Changing an inaccessible height cannot alter a visible score. The developer world preview is explicitly global and uses the canonical cache.

## Continuous contours and rendering

`TopographicContours` triangulates the lattice of neighboring hex centers, then linearly interpolates every contour crossing on a shared center-to-center edge. Canonical edge-point and triangle identities join adjacent segments, suppress duplicates, and join longitude-seam crossings once. Triangles stop at polar boundaries.

Player contours are built only from currently visible land tiles. A contour requires all three triangle vertices to be visible, so linework ends at fog or coast instead of consulting a hidden height. Geometry is cached per observation and effective zoom/detail step. An 8×8 wrapped chunk index limits each repaint to segments near screen-visible chunks; biome tiles, hillshade, and contours are drawn before shores, rivers, vegetation, settlements, units, planning marks, and the final selection frame.

Contour intervals are presentation-only:

| Hex radius | Normal | Fine |
| --- | ---: | ---: |
| below 30 px | 0.100 | 0.100 |
| 30–43 px | 0.050 | 0.050 |
| 44–51 px | 0.025 | 0.025 |
| 52 px and above | 0.025 | 0.0125 |

Major contours occur every 0.100 normalized elevation and use a heavier line. Zoom changes select a nested subset, so retained contours do not move. Hillshade comes from the same observation-safe fitted gradient and lightly modulates the existing biome color. Ruggedness uses one teal–green–gold–rust–plum 0–100 ramp; optional contours provide height context.

Player controls are `1` Terrain, `2` Topography, `3` Ruggedness, `G` Normal/Fine, and `K` ruggedness contour context. The layer can change without regeneration. Selected-tile details report continuous height, slope, numeric ruggedness, and whether the local fit is partial.

Ground colour and artwork follow the tile's biome at every elevation. The former
highland/mountain background override is no longer selected; hills and mountains
use their desert, grassland, temperate, tropical or cold ground. Topography and
ruggedness communicate the shape of that ground. Gameplay elevation levels,
relief classification and peak impassability are unchanged.

## Automated evidence

Focused headless checks cover exact plane recovery, flat and inclined zero cases, offset invariance, added undulations, score normalization, deterministic longitude rotation, the seam, polar sample counts, finite values, contour interpolation/continuity/deduplication, nested zoom density, hidden-height isolation, cache identity, byte-identical saves, deterministic reconstruction, unchanged fingerprints/turn results, and a Pangaea seed-29 smoke world.

The Standard / Continents / seed-11 benchmark on this Linux runner (one process, Release; timings are single-run diagnostics, not hardware-independent budgets) recorded:

| Work | Result |
| --- | ---: |
| World generation | 10,368.0 ms |
| Canonical ruggedness preprocessing / cold cache access | 337.8 / 339.1 ms |
| Cache payload / measured managed delta | 6,815,744 / 6,815,920 bytes |
| Filtered 19-tile player analysis | 4.434 ms |
| Normal / Fine visible contour build | 2.953 ms (0 segments) / 1.202 ms (7 segments) |
| Save before / after cache | 3,362.2 / 3,026.4 ms; byte-identical, 72,356,172 bytes |
| Load / deterministic cache rebuild | 2,137.5 / 303.1 ms |

Save size is unchanged. Cache construction does not change the fingerprint. The save timing difference above is a non-interleaved single-sample variance; the cache is not visited by `SaveCodec`. Frame time, camera-pan smoothness, and GPU/UI allocation behavior require the Unity visual run below and remain unaccepted in this environment.

In the player, the 339 ms cold-cache work runs on the existing generation/load worker, not when a layer button is pressed. The filtered analysis is prepared at observation refresh; first Topography activation in this opening needed only the 2.953 ms contour build plus rendering. Ruggedness reuses the filtered scores and, when contour context is enabled, the same contour cache. Actual input-to-frame latency remains part of the Unity gate.

## Unity visual acceptance

The development-player route `--anthropocene-topography-output` creates nine actual Unity screenshots and a `captures.txt` manifest. It covers rolling terrain versus topography, Normal versus Fine at the same zoom, broken uplands in topography/ruggedness, smooth elevated terrain in topography/ruggedness, a river/unit/fog gameplay frame, and Pangaea seed 29. Every row records seed, size, shape, player count, camera tile and axial coordinates, pan, zoom, visible-tile count, tileset, layer, density, interval, continuous/gameplay elevation, canonical and displayed ruggedness/sample count, activation time, and source SHA. It asserts that view changes preserve the authoritative fingerprint.

Follow [UnityBuildRecovery.md](UnityBuildRecovery.md) and make a visible Development build of the committed branch. Then run the built player from PowerShell (replace the paths with the chosen build/evidence directories):

```powershell
$sourceSha = git rev-parse HEAD
& 'Builds\Topography\Anthropocene.exe' `
  --anthropocene-topography-output 'TestResults\Topography\Player' `
  --anthropocene-source-sha $sourceSha `
  -logFile 'TestResults\Topography\Player.log'
```

Acceptance requires `result.txt`, all nine PNG files, a clean player log, and human review of contour joins at ordinary edges and the longitude seam; fog boundaries; biome/rivers/settlements/units/selection legibility; high-but-smooth versus genuinely rugged land; aliasing while zooming; and pan/zoom frame behavior. These captures must not be replaced with synthetic illustrations.

## Windows review follow-up — 6 November 2026

Code revision `5fa5a1b4324449efee3f75f123023d3020509256` fixes the capture
routine's CS0170 definite-assignment failure, widens the layer controls so
`3 Ruggedness` fits, and removes the elevation-based highland background override.
Unity 6000.5.10f1 built the Development player successfully; all 31 EditMode tests
passed, including biome-ground coverage across Lowland, Hills, Highland and
Mountain relief. No simulation source changed.

The nine-frame capture route passed at 1280 x 800 using Standard/Continents/11
and Tiny/Pangaea/29. The manifest records the code revision and unchanged
authoritative fingerprints during view use. Screenshots confirm the complete
Ruggedness label and biome ground in the rolling and smooth elevated comparisons.
Local, ignored evidence is in `TestResults/PR45Fixes/` (Build.log, EditMode.xml,
Player.log, and Captures with captures.txt/result.txt).

The opening player fits still use 19/61 known cells. For example, the rolling
capture displays 22.4 versus the full-world score 42.4; smooth elevated ground
displays 2.0 versus 12.4. These partial fits are labelled in the inspector and
are not numerical parity with the fully known chat prototype. The earlier
review verified mouse layer selection, G/1/3/K shortcuts, pan, zoom and stable
displayed scores across layers. Frame-time/allocation profiling, a dedicated
longitude-seam visual pass, and current-main integration remain outstanding.

## Full-world atlas review — 6 November 2026

The developer atlas now offers Topography alongside Ruggedness. Topography keeps
biome colours and uses the same continuous-height contour construction and
hillshade function as the player view. Ruggedness offers a contour toggle; both
layers offer Normal/Fine density and preserve the camera when switching. Labels
are shortened to fit all nine layers at the default window width. The atlas
remains rectangular axial rows, rather than the player's symbolic hex artwork.

Contours are cached in a screen-sized transparent raster. Construction reads
only viewport tiles plus a one-cell margin (all tiles at fit-world), using the
shared O(N log N) contour builder; memory/upload scales with screen pixels.
Pan, zoom, density, layer and world changes invalidate the appropriate cache.
No simulation state, player observation, save format or generation rule changes.

`--anthropocene-topography-atlas-output <fresh-directory>` captures eight actual
Unity frames: paired full-world, rolling-region, rough-coast and smooth-uplands
views for Standard/Continents/11/80, and leaves the atlas open for inspection.
It checks unchanged authoritative fingerprints and player-visible tile IDs.
At 1920 x 1080, regional zoom 32 covers about 32 x 19 tiles. Selected regional
ruggedness scores are 42.37, 76.62 and 12.35, each using all 61 samples. These are
the earlier review's locations, not a claim to reproduce the prototype's exact
chosen sites. Full-world contour interval is 0.100; regional interval is 0.025.

Initial validation passed eight captures and all 31 EditMode tests. Measured
contour-raster builds were 233–255 ms for the whole Standard world and 6–16 ms
for the regional views; these are one-time UI cache builds, not frame-time or
simulation throughput benchmarks. Local evidence: `TestResults/PR45Atlas/`.

## Contour, river and climate inspection follow-up

The atlas now has its own finer contour LOD (Normal/Fine at full-world intervals
0.025/0.0125, reaching 0.0125/0.0025 regionally), actual boundary-river overlays,
unblended land ruggedness colours, and annual/seasonal Temperature and Snowfall
layers. Player contour intervals and generated terrain/climate are unchanged.
The capture route now writes twelve images, including the natural desert-river
region and both climate views, checks state/fog purity and leaves the atlas open.
See [TerrainClimateAudit.md](TerrainClimateAudit.md) for measured seed-11
limitations, reproduction and the proposed generation follow-up. Snowfall is a
precipitation fraction; it does not claim accumulated snow cover.

## Physical-altitude follow-up (generator 15)

New worlds now convert normalized elevation to metres above sea level at
12,000 m/unit and use 6.5 C/km surface cooling. Older saves retain abstract
calibration and their stored profiles. See [TerrainClimateAudit.md](TerrainClimateAudit.md)
for the generation boundary, compatibility checks and changed climate evidence.
The atlas and player inspector show derived metres; atlas Fine contours are
150 m at world scale and as fine as 30 m close up, with 1,200 m major contours.
These units do not redefine the ten gameplay elevation levels.

The atlas capture route now writes fourteen images: six paired topography and
ruggedness locations (including the highest peak), then world Temperature and
Snowfall. It records the source revision and physical calibration in its
manifest, checks unchanged world/fog state and leaves full-world Topography
open. This supersedes the earlier eight/twelve-frame route descriptions above.
