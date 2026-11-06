# Drainage-shaped terrain and ancient uplift margins — generator 16

The user requested that generated topography follow the river flow and that
ancient uplift lose more material at its outer edges. Generator 16 implements
both for new worlds. These are actual saved elevation changes; the ruggedness
formula, colour scale and contour interpolation are unchanged.

## Authority and generation boundary

`Tile.Elevation` remains the single terrain-height authority. `Tile.Downstream`
and `RiverEdge` connectivity/flow remain the two existing drainage graphs. After
incision, `RiverEdge.FromSurface/ToSurface` equal the means of the actual tile
heights incident to their corners, rather than a separate raised routing surface.
Retained lake and marine surfaces are fixed. Generation diagnostics are transient
and excluded from saves; they describe source uplift and subsequent removal,
not another gameplay elevation field.

The ordered pipeline is:

1. Plates and broad uplift, including inherited ancient ranges.
2. Preferential ancient-margin erosion.
3. Atmospheric forcing, tile hydrology and retained lakes.
4. Boundary river routing and accumulated flow.
5. Flow-following incision and compatible final terrain heights.
6. Final-height temperature/rain/snow correction, gameplay elevation levels,
   ecology, vegetation, geological resource placement and starting camps.

Tectonic geology therefore precedes rivers. Resource/deposit generation remains
after final terrain so host eligibility, surface Wood/Clay and drainage-driven
placer transport consume coherent geography. No resource bodies are generated
and then silently left on an obsolete surface. Rainfall/runoff and lake retention
use the earlier climate solve; final surface temperature changes repartition
that fixed precipitation. This remains an explicit one-way abstraction, not a
coupled equilibrium solver.

## Erosion pattern

The existing dominant ancient-orogen footprint strength fades from the core to
the outer flanks and track ends. With `edge = 1 - strength`, the fraction of
inherited uplift removed is:

```
fraction = (0.08 + 0.57 * edge²) * clamp(age / 400, 0.25, 1)
```

This is a bounded procedural age proxy, not measured erosion per million years.
At equal age and uplift, outer terrain loses a larger fraction than the core.
Removal is limited to inherited uplift and cannot cut marine cells or cross sea
level. Absolute removal tends back to zero beyond the uplift footprint. Broad
uplift survives; the diagnostic `AncientOrogenUplift` continues to record the
original contribution, useful for geological history and matched comparisons.

River bank incision uses accumulated flow through a bounded logarithmic proxy:
`min(0.025, 0.0015 + 0.004 * log(1 + flow / RiverThreshold))` in normalized height.
Ancient flanks receive up to twice that cut, weighted by inherited uplift and
edge exposure. Adjacent land receives one quarter of the bank cut, forming a
local valley shoulder. Overlapping cuts take their maximum, not a sum. These
coefficients are game calibration, not a sediment-transport law. Valley
continuity can require a deeper downstream breach than the initial local cut.

## Reconciling both drainage graphs

At ordinary hex corners, two incident tile heights are shared by the endpoints
of a boundary river. Comparing their means cancels those two banks: the remaining
upstream tile must be at least as high as the remaining downstream tile. The
segment also crosses the shared-bank midpoint in the contour interpolation.
Upstream-to-bank and bank-to-downstream constraints ensure that midpoint lies
between the endpoint heights: a river cannot cross a hidden mid-segment hump.
Polar
corners have fewer contributors; conservative pairwise constraints on the common
mean handle them without fabricated polar tiles. Wrapped contributor lookup is
tested against the forward corner construction.

These river constraints and the existing tile-downstream constraints form one
graph. Fixed water-level lower bounds propagate upstream. A multi-source minimum
heap then finds the greatest surface below the proposed incision that satisfies
every downstream constraint. Cycles produce flat reaches; they do not cause
repeated convergence scans. Flow, graph connectivity, river mouths and retained
water levels stay unchanged within this stage.

The two graphs can disagree at a retained lake bank even before erosion. A bank
below its required lake datum is graded up to that datum and reported separately,
not counted as eroded terrain. In Standard seed 11 this affected two tiles, by at
most 0.0000747 height (0.90 m). No water surface changed. This is a geometric bank
correction, not a sediment-conserving deposition model. Eroded sediment mass is
not simulated; geological placer resource transfers retain their own conservation.

## Time, scaling, mature worlds and RL

Both operations happen only during world generation. Four seasonal profiles and
authoritative turn boundaries are unchanged. Geological age controls a bounded
coefficient, not an iteration count; no work scales with historical duration,
population, settlement count, claims or turn length.

Margin erosion is O(N). River shaping is O((N + R) log N), with O(N + R) transient
arrays/adjacency and a bounded number of local neighbour visits per river bank.
The constraint graph has one edge per existing tile-drainage link plus a constant
number per boundary river. There is no dense all-pairs matrix or per-source walk
to the ocean. Temporary original/floor/cut arrays use 12N bytes, visited flags N,
adjacency heads 8N, and four integer adjacency lists 16 bytes per capacity slot;
heap storage and temporary corner/LINQ objects add overhead. Two diagnostic
removal rasters retain 8N bytes in the generation report, excluded from saves.
They add no per-turn work. Ordinary terrain metrics remain a derived cache.

The larger-map probe is 2048×1024 with 160 tribes. Training resets pay generation
cost, but mature turns do not repeat erosion. New geometry affects climate,
biomes, resources, movement bands, viable starts and RL observations. The local
checks are deterministic simulation validation, not arm selection or training
readiness; confirmation seeds are untouched.

Rejected alternatives: cosmetic ruggedness inflation, drawing a private channel
below unchanged contours, dropping uphill river segments, repeatedly rerouting
until convergence, simulating erosion year by year, or lowering retained lakes
to hide a bank mismatch.

## Compatibility and validation

`DrainageTerrainEnabled=true` is the new-world default. Older saves missing it
load false, retaining every saved height, river surface and seasonal profile;
load never invokes generation. Generation provenance is now 16. Historical
geology/S24 fishing fixtures explicitly disable both physical altitude and this
erosion option, retaining their original goldens. Current generation, ecology,
resource conservation, presets and replay checks use the new defaults.

`RiverTerrainChecks` covers exact corner contributors/seams/poles, equal-uplift
edge/core removal, bounded age, fixed sea, real incision on controlled valleys,
unchanged river flow/connectivity, raw-corner/midpoint and tile-graph descent, all three
world shapes, viable starts, repeat generation and exact saves. Altitude tests
also remove the new field from a serialized payload and verify unchanged loaded
heights/profiles. Existing climate, morphology, drainage and topography suites
remain enabled. The complete suite and compact geography matrix passed.

Reproduce with `Tools/TerrainAudit/Anthropocene.TerrainAudit.csproj`: default is
Standard/Continents/11/80; `--no-erosion` holds the physical altitude calibration
while disabling these two stages; `--large` uses 2048×1024/160. `--legacy` disables
physical altitude and erosion for the original audit. Evidence lives under
ignored `TestResults/PR45Erosion/`.

| Standard seed-11 measure | Generator 15 | Generator 16 |
| --- | ---: | ---: |
| River edges | 45,365 | 45,089 |
| Raw corner uphill segments | 1,104 | 0 |
| River-bank tiles below nearby non-river land | 78.6% | 97.0% |
| Median bank-minus-nearby height | -0.00101 | -0.00697 |
| Land median ruggedness | 21.11 | 31.23 |
| Ancient-core median ruggedness | 31.49 | 36.84 |
| Ancient-core share scoring at least 50 | 13.3% | 16.9% |
| Viable camps | 80/80 | 80/80 |

These population summaries do not imply every river bank or old range is rugged.
The larger-map run also retained 160/160 viable camps. Standard seed 11 removed
height from 62,429 tiles during river shaping, mean 0.00554 (66 m), maximum
0.04876 (585 m). Margin erosion took 20.4 ms and river shaping 827.1 ms in that
run; whole generation plus terrain-metric warmup took 12.22 s. Timings are local
single-run measurements. The sixteen-frame Unity capture route adds the ancient
flank with greatest river cut within strength 0.1–0.5 as a disclosed inspection
site, alongside the earlier comparison regions, full-world maps and climate.

At 2048×1024 / 160 tribes / seed 11, the final midpoint-aware implementation
generated in 32.02 s including terrain-metric warmup. Margin erosion took
56.56 ms and valley shaping 2,724.54 ms. All 160 starts passed viability
(minimum 85.8%); no banks needed raising. River shaping lowered 304,508 tiles,
mean 0.00669 (80 m), maximum 0.16980 (2,038 m). That maximum is a downstream
continuity breach, not the bounded initial bank cut. Large gorges are possible;
this model does not limit incision by a sediment budget or explicit bedrock
hardness. Managed heap after generation was 1.33 GB including uncollected
objects, not a peak or an incremental-allocation measurement. The earlier
60.84 s run overlapped other tests and is not an isolated timing comparison.
