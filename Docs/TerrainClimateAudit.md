# Terrain, river and altitude-climate audit — 6 November 2026

Status: display corrections are implemented on PR 45; generation corrections
are **not** implemented or accepted by this audit. Results below are one generated
Standard / Continents / seed 11 / 80-tribe world with the shipped configuration.
This is a deterministic geography diagnostic, not a training experiment.

Run from the repository root:

```powershell
dotnet run --project Tools/TerrainAudit/Anthropocene.TerrainAudit.csproj -c Release
```

## 1. Contour detail and rivers

The atlas originally borrowed player zoom thresholds. At fit-world, even Fine
therefore used 0.100 normalized-height contours. At the earlier regional zoom
it used 0.025. The updated atlas uses 0.025/0.0125 at world scale for Normal/Fine,
then reaches 0.0125/0.0025 on close inspection. Player contour spacing is unchanged.
The base contour lattice is finer, while retained contours still use the same
absolute levels, interpolation and fog-filtered vertices.

The atlas now draws actual directed boundary river edges in blue, with a toggle.
These are mapped from the shared hex-corner lattice into rectangular axial rows,
not approximated by connecting river-flagged tile centres. The contour/rivers
raster remains cached by viewport, scale, density and toggles. Work scales with
visible tiles/edges and screen pixels; full-world construction is O(N log N).

However, drawing rivers does not establish carved valleys. Of 45,392 river edges,
1,104 (2.43%) increase in raw corner height (the mean of their adjacent tile
centres), while none increases in the flood-filled routing surface. At 2,748
river start corners that routing surface exceeds raw corner height by more than
0.00001. This is a real separation in `BoundaryRiverStage`: its priority flood
raises its private routing surface without cutting the corresponding terrain.
78.6% of river-bank tiles are below the mean of nearby non-river land within two
hexes; the median difference is only -0.00101 normalized height. River-bank
height is a local diagnostic, not the unrepresented channel-bed elevation.

## 2. Ruggedness and ancient ranges

| Population | Median ruggedness | 90th percentile | Share scoring at least 50 |
| --- | ---: | ---: | ---: |
| All land | 21.11 | 51.15 | 10.5% |
| Ancient-orogen core (strength > 0.5) | 31.49 | 54.37 | 13.3% |
| Ancient uplift > 0.05 | 32.64 | 54.55 | 13.7% |
| Gameplay elevation level 10 | 74.14 | 91.65 | 94.1% |

The metric fits out uniform slope and measures residual variation over radius
four. It deliberately does not equate elevation with ruggedness. Ancient orogens
currently add broad decayed uplift profiles, low-frequency ridge variation and
one smoothing pass. They do not model a drainage-connected dissected valley
network. Increasing the display score or painting every old range red would
conceal this geometry limitation. The updated atlas uses the exact legend colour
on land, without the earlier 22% biome blend, and retains blue marine geography.
The score/normalization itself is unchanged.

Old age does not imply uniformly smooth terrain: differential erosion and
resistant rocks can preserve or create strong local relief. Primary context:
[USGS, Appalachian physiographic regions](https://md.water.usgs.gov/groundwater/physiomaps/),
[NPS, Great Smoky Mountains geology](https://www.nps.gov/grsm/learn/nature/geology.htm).
These support investigating dissection, not a universal ruggedness target for
all ancient ranges.

## 3. Altitude temperature and snowfall

Temperature already subtracts `max(0, height - SeaLevel) * LapseRate`, with
`SeaLevel=0.45` and `LapseRate=28` degrees Celsius per normalized-height unit.
There is no documented metre conversion. Gameplay elevation levels are land
quantiles: level 8 spans roughly 0.598–0.630 in this world and receives mean
altitude cooling of only 4.54 C. It is not an absolute alpine-altitude band.

Level-10 land has median height 0.784 and mean altitude cooling 9.44 C; only 8.9%
receives snowfall in any profile, and 5.4% is Cold/Ice biome. The highest tile
(156607; q959/r152; height 0.920624) has temperatures 1.9/-2.5/1.9/6.3 C and snow
in one of four profiles. This is evidence for revisiting height-to-temperature
calibration if these heights are intended to represent alpine mountains.

The standard-atmosphere reference is 6.5 C per kilometre, not 6.5 C per arbitrary
height unit: [FAA Aviation Weather, chapter 1](https://www.faa.gov/documentLibrary/media/Advisory_Circular/AC_00-6B.pdf).
Actual surface climate varies with latitude, moisture, season and exposure. A
metre conversion must be chosen before interpreting current cooling physically.
That calibration preference was requested from the user and is still pending.

The viewer now includes annual/seasonal Temperature and Snowfall layers and
reports the selected tile's seasonal temperature range, cooling estimate and
number of snowy profiles. Snowfall is the precipitation-weighted fraction of
precipitation falling as snow. It is **not accumulated snow cover**, snow depth,
a snowline, or glaciation; those states are absent from the current model.

There is a further ordering concern: climate runs before hydrology, which can
modify continuous heights. Rerunning the full climate stage on the final world
changes some land temperatures by up to 0.8523 C (10,365 tiles exceed 0.1 C).
This combines final elevation and water-class changes; it is not a pure isolated
lapse-rate comparison. It is much smaller than the potential altitude-scale
change and should be resolved explicitly with the generator follow-up.

## Abstraction audit for the proposed generator follow-up

- **Authority:** saved final `Tile.Elevation`, boundary river geometry/routing
  surface and four `SeasonData` profiles. Derived UI scores/colours must not
  substitute for actual valley relief or cold climate.
- **Time:** generation-only geological abstraction, followed by four fixed
  seasonal profiles. No erosion-by-year loop and no new simulation timestep.
- **Scaling:** bounded local terrain work and indexed drainage passes; target
  O(N log N) generation including routing, O(N) storage. Geological age must
  affect parameters, not the number of simulated steps. No work per population,
  claim or settlement is needed for these fixed geographic fields.
- **Mature game / RL:** unchanged per-turn geography cost; generation and reset
  cost, memory, habitat/start changes and altered observation distributions must
  be measured. UI inspection is not evidence of training readiness.
- **Compatibility:** make a documented new-world generation revision; preserve
  existing saved heights, climate and their configuration. Do not silently
  regenerate user saves or redefine movement levels as physical metres.
- **Validation:** controlled ridges/valleys and matched-latitude altitude tests;
  raw versus routing channel slopes; precipitation partition/conservation;
  deterministic seams and saves; morphology/climate/drainage checks across world
  shapes; inspect large maps and remeasure viable starts and generation cost.

A candidate should couple river dissection and resistant ridge structure to the
existing terrain before final climate/ecology, then calibrate altitude cooling
in explicit units. Rejected shortcuts are colour-only mountains, globally
inflated ruggedness scores, snow solely by elevation percentile, or a dense
geological-time erosion simulation. Numerical coefficients and any persistent
snow model remain design decisions; this document does not silently ship them.
