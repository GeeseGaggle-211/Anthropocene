# Terrain, river and altitude-climate audit — 6 November 2026

Status: display corrections and the user-authorized physical altitude climate
are implemented on PR 45. River incision and ancient-range dissection remain
open. Sections 1–3 below record the **pre-altitude baseline**: one generated
Standard / Continents / seed 11 / 80-tribe world with the former climate calibration.
This is a deterministic geography diagnostic, not a training experiment.

Run from the repository root:

```powershell
dotnet run --project Tools/TerrainAudit/Anthropocene.TerrainAudit.csproj -c Release -- --legacy
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
The user subsequently approved an Earth-like altitude scale for new worlds;
the implemented conversion and measured effects are recorded below.

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

## Validation of the display correction

Unity 6000.5.10f1 built code revision `7d6c9ac` successfully; all 31 EditMode tests
passed. The twelve-frame 1920 x 1080 capture route passed with unchanged world
fingerprint and player-visible tile IDs. The focused headless suite passed 984
assertions. The standalone audit reproduced the numbers above, and the current
contract document check passed. Ignored local evidence is in
`TestResults/PR45TerrainAudit/` (baseline.txt, reproduce.txt, topography-checks.txt,
Build.log, EditMode.xml, Player.log, Captures). The capture manifest records the
exact code revision. Finer full-world contour/river raster builds measured
677–713 ms; the representative rolling close-up measured 116–120 ms. These are
cache reconstruction costs, not steady-state frame times. Full frame profiling
remains outstanding. The subsequent altitude correction is documented below.

## Physical altitude for new worlds — generator 15

The user authorized an Earth-like altitude scale on 6 November 2026. New worlds
use `ElevationMetersPerUnit=12000` and `LapseRateCPerKm=6.5`:

```
altitude metres = (Tile.Elevation - SeaLevel) * ElevationMetersPerUnit
surface cooling C = max(0, altitude metres) * LapseRateCPerKm / 1000
```

The 12,000 m/unit conversion is a chosen game calibration, not a measured Earth
constant. With sea level 0.45, height 0.55 is 1,200 m, 0.70 is 3,000 m and the
generator's 1.20 ceiling is 9,000 m. The 6.5 C/km rate follows the standard
atmosphere reference above; local weather and inversions are not simulated.
Negative underwater elevations are depths relative to sea level and do not
warm surface air. Gameplay elevation levels remain relative movement bands.

**Authority and ordering.** Final saved `Tile.Elevation`, saved calibration and
four saved seasonal profiles are authoritative. Altitude is derived, with no
second per-tile height field. The climate stage captures a temporary height
snapshot; after hydrology, `SurfaceClimateStage` applies the exact difference
in altitude cooling to each changed tile's four temperatures, then repartitions
its existing precipitation at 0 C. Total precipitation, runoff and atmospheric
transport remain fixed. Ecology, vegetation, food, geological surface resources
and placement consume the corrected profiles. This is an explicit one-way
generation boundary, not an iterative atmosphere/hydrology equilibrium. A full
climate rerun on final land/water classifications still differs by up to 0.707 C
in seed 11 because maritime forcing also changes; this is a retained abstraction.

**Time, cost and mature scale.** Four environmental profiles remain fixed forcing;
no new turn clock, snowpack integration or geological-time loop is introduced.
The additional correction is O(N), with four constant profile operations per
changed tile and a temporary 4N-byte height snapshot, released after the pass.
It adds two configuration scalars to saves and no per-turn geography work. Cost
does not grow with population, settlements, claims or historical duration.
On 2048×1024 / 160 tribes / seed 11, total generation plus initial terrain-metric
construction took 24.75 s; the correction took 19.88 ms with an 8 MiB snapshot.
All 160 starts exceeded the normal 85% viability threshold (minimum 86.1%).
The observed managed heap was 1.32 GB including uncollected objects; this is not
a peak-memory or incremental-allocation measurement. No RL readiness claim is
made: new-world climate changes habitat, resources and observation distributions.

**Compatibility.** Missing altitude fields deserialize to physical scale zero,
retaining `LapseRate=28` and the legacy generation path. Existing saved heights,
profiles, resources and generator provenance are not regenerated or relabelled.
Explicit custom physical scales round-trip. New generation records version 15.
The selected-tile inspector and contour legend show physical metres only for
physical-mode worlds; older worlds retain abstract units.

### Seed-11 comparison

Both audits use Standard / Continents / seed 11 / 80 tribes. The generated land
height and ruggedness distributions are unchanged in this comparison.

| Measurement | Legacy altitude | Physical altitude |
| --- | ---: | ---: |
| Land mean annual temperature | 12.29 C | 8.31 C |
| Land classified Cold/Ice | 18.7% | 29.2% |
| Land receiving snow in any profile | 24.6% | 34.5% |
| Level-10 land mean temperature | 8.82 C | -8.03 C |
| Level-10 land receiving snow in any profile | 8.9% | 99.0% |
| Highest tile seasonal temperatures | 1.9 / -2.5 / 1.9 / 6.3 C | -21.6 / -26.0 / -21.6 / -17.3 C |

The highest tile (156607) is 5,647 m and now Ice biome with snowfall in every
profile. New generation took 10.38 s including initial terrain-metric
construction; all 80 starts remained viable (minimum 107.4%). The warm desert
river preset moved to an existing generated camp at tile 223410 because the
old site no longer met its warm-habitat contract. The steppe and coastal sites
remain. See [Playtesting.md](Playtesting.md) for remeasured food and viability.

Snowfall still means precipitation falling as snow, not persistent snow cover
or glaciers. River counts can change with climate; raw-channel uphill segments
and insufficient dissection in old ranges are **not fixed by this change**.
Rejected alternatives remain percentile-driven snow, cosmetic ruggedness
inflation, rewriting saved worlds and a second coupled climate/routing solve.

### Reproduction and validation

Run the audit without `--legacy` for physical defaults, or with `--large` for
the 2048×1024 scale probe. `AltitudeClimateChecks` contains 28 assertions for
matched-latitude cooling, snowy equatorial peaks, final-height correction,
precipitation conservation, legacy field omission and custom exact save
round-trips. Existing climate, morphology, drainage and topography checks run
on physical defaults. Historical geology payload hashes and the locked S24
fishing cohort explicitly retain legacy altitude, preserving their original
goldens rather than silently replacing that evidence.

Local evidence: `TestResults/PR45Altitude/` (`focused.txt`, `standard11.txt`,
`large-scale.txt`, `full-suite-final.txt`, Unity build/test logs and `Captures`).
The expanded capture route records fourteen views including the highest peak,
temperature and snowfall, and asserts that viewing preserves the saved world
fingerprint and player-visible tile IDs. Final execution results are recorded
in [Validation.md](Validation.md).
