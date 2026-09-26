# Land biomass and natural food access calibration

September 26, 2026. This is the generator-14 calibration on
`codex/food-population-calibration`, based on `features/neolithic-agricultural`
at `8ddd4e26e191d9abbbd02145e73fd787e21b3a72`. The target is roughly
100 people in a long-lived uncultivated camp and 600 in an agricultural
village, with primitive cultivation at or below `q=.4`. The calibration
varies only the generated land biomass multiplier and one uniform factor
applied to every configured natural land gatherable share. It does not change
food consumption, growth, maintenance, natural recovery, managed recovery,
or the food priority ledger.

## Semantics and scale

At the authoritative turn boundary, each land tile stores human-gatherable
food `G` within total biomass `B`. Humans draw only `G`; grazers can reach
all `B`, selecting `B-G` first. Receipts debit opening stock, then the four
climate profiles recover the endpoint for the next turn. The prior cohort's
consumption, reserve, and growth requests are fixed before resolution.

`WorldGenerator` first scales seasonal land `MaxFood` by `m`, then
`FoodEcology.ConfigurePotential` derives `MaxPotentialFood = MaxFood / f`,
where `f` is the natural accessible fraction. Thus `m` raises opening `G`,
while lowering `f` at fixed `m` enlarges initially inaccessible `B` and the
stock that cultivation can make accessible. At `q=0`, the rich, grassland,
arid, and tundra fractions all use **one factor** `s=.39`:

| Habitat share | Previous | Generator 14 | Ratio |
| --- | ---: | ---: | ---: |
| Rich vegetation / river | .25 | .0975 | .39 |
| Grassland / open | .10 | .0390 | .39 |
| Arid | .05 | .0195 | .39 |
| Tundra | .08 | .0312 | .39 |

The biomass multiplier is `m=63`, up from 30. Against generator 13, new
land `MaxFood` is 2.1 times larger and `MaxPotentialFood` is about 5.38
times larger at the same habitat and climate. The ecological equation still
permits `G=B` at `q=1` for a later agricultural tier; generated primitive
settlements cannot invest above the configured supported intensity `q=.4`.
Previously that setting affected tool efficiency but did not stop investment,
so candidate sweeps reached `q` near one and could not test this era's target.
Existing saved higher-intensity fields retain their state and may decay.

The added investment bound is local to each camp's radius-one land footprint.
It changes neither the one-tile-per-active-tile horizon kernel nor its
duration independence. Work remains proportional to active tiles, local
claim edges, camps, and bounded planner forecasts; it does not acquire a
`dt / season` loop or a map-wide scan. More supported people can still raise
planner and demographic cost, so mature-world timing remains a separate
measurement gate.

## Parameter search

The search used the production planner and resolver for three pinned
128×64, eight-player, seven-land-tile generated sites: fertile open
(seed 15, tile 3819), medium woodland (seed 11, tile 4530), and dry marginal
(seed 17, tile 4373). Each camp began with 24 people; other camps were
removed from its isolated world, while wild herds and ecology remained live.
All rows below use the primitive `q=.4` investment bound and normal
`LandFoodRegeneration=.03` and `CultivationRegenFlux=.65`. `s` multiplies all
four natural shares above, never one biome alone. The turn-120 result is
the median across the three sites:

| `m` | Uniform `s` | Agricultural median, turn 120 | Maximum field `q` |
| ---: | ---: | ---: | ---: |
| 60 | 1.00 | 279 | .40 |
| 60 | .60 | 409 | .40 |
| 60 | .40 | 564 | .40 |
| 60 | .36 | 615 | .40 |
| 60 | .30 | 721 | .40 |
| 63 | .37 | 632 | .40 |
| 63 | .38 | 611 | .40 |
| **63** | **.39** | **604** | **.40** |
| 63 | .40 | 592 | .40 |
| 64 | .39 | 614 | .40 |

For the uncultivated control, technology and breakthrough were disabled
only in the validation configuration. Its median at turn 120 was 80 for
`m=50`, 94 for `m=60`, **99 for `m=63,s=.39`**, and 110 for `m=70`.
Since `m` determines opening natural `G` here, varying `s` alone does not
produce the desired Paleolithic lift. The chosen pair meets both numerical
targets in these controlled fixtures without spending food twice.

## Population trajectory

The selected `m=63,s=.39` runs show growth tapering near food support. Mean
`q` is averaged across the seven footprint tiles; `max q` never exceeds .4.
The uncultivated rows are the separate no-breakthrough control, not another
phase of the agricultural run.

| Site | Cultivated people T25 | T40 | T80 | T120 | Mean `q` T40 | Uncultivated people T80 / T120 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Fertile open | 366 | 611 | 611 | 604 | .40 | 101 / 99 |
| Medium woodland | 743 | 836 | 836 | 836 | .40 | 218 / 218 |
| Dry marginal | 204 | 428 | 474 | 472 | .40 | 63 / 63 |
| **Median** | **366** | **611** | **611** | **604** | **.40** | **101 / 99** |

The endpoint stock shows why the larger hidden biomass pool matters. Values
sum the same seven local land tiles at turn 120. The controls have lower
population and no cultivation, so this is a trajectory comparison rather
than an equal-claim ecological experiment.

| Site | Uncultivated `G / B` | Cultivated `G / B` | Cultivated food need met, final 10 turns |
| --- | ---: | ---: | ---: |
| Fertile open | 7,947 / 235,988 | 48,384 / 239,720 | 100% |
| Medium woodland | 17,381 / 301,029 | 67,428 / 305,077 | 100% |
| Dry marginal | 5,042 / 202,885 | 37,822 / 203,819 | 100% |

Total `B` stays similar between each pair while cultivation makes much more
of it human-gatherable at `q=.4`. Cultivated endpoint `G` varied by at most
0.05% of its own final-ten-turn mean at these sites. The reported food
satisfaction and stable local stock support the late population plateau;
they do not establish a hard carrying-capacity rule for other regions.

Woodland remains more productive than fertile open in this coupled fixture
because it supports a larger workforce and contains a mixed seven-tile
footprint. Its wooded tiles retain their higher build and maintenance
coefficients; these are not equal-labor experiments. The 120-turn extension
is synthetic and includes neither human fission nor trade.

Three full Standard/Continents worlds used the production configuration,
80 tribes, four workers, and 80 turns of continuation after opening. The
observed final turn is 90 because opening completed after ten turns. The
table counts only camps with agricultural technology at turn 90; the final
food figure is population-weighted within those camps.

| Seed | Agricultural camps | Village median | Village mean | Agricultural food need met | World population |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 11 | 57 | 682 | 669 | 99.8% | 46,231 |
| 1729 | 57 | 573 | 693 | 99.9% | 47,136 |
| 23 | 55 | 586 | 670 | 99.8% | 46,746 |
| **Pooled camps** | **169** | **608** | — | — | — |

The three isolated habitats do not define the distribution of every
generated start. In these full worlds all camps had passed the Paleolithic
by turn 10; the roughly 100-person Paleolithic target is therefore checked
with the persistent no-breakthrough control, not inferred from a late-world
Paleolithic subgroup. Some villages exceed 600 and others fall short. This
is a median target, not a settlement cap.

## Verification and limitations

`./Tools/Test-Headless.ps1 -Seeds 0` passed on the calibrated code, including
food and biomass bounds, conservation, production, scalar/two/four-worker
replay, reordered collections, and mature agricultural save continuation.
The focused Neolithic and 5,059-case cultivation allocator differential
checks also passed. Tests explicitly retain high-`q` ecology coverage by
using a fixture tier that supports later investment; primitive generated
fields are checked at `q<=.4`.

The final production configuration reproduced every population and food-yield
row in the three 90-turn Standard candidate reports, and every population,
endpoint `G`, and maximum-`q` row in the two 360-row isolated and
no-breakthrough sweeps. The candidate configuration serialized some fractions
with extra floating-point digits, so its configuration hash differs; the
paired outcomes above were unchanged.

Generator 14 changes new land stocks only. Loaded worlds retain their saved
configuration, capacities, and ecology; no save format changes. Enlarging
`B` also changes grazer supply and will need a separate husbandry audit.
The three-world reports had outcome details enabled, so their milliseconds
are not formal benchmarks. One separate, sequential warmed Release timing
pair compared parent `8ddd4e2` with generator 14 at Standard/Continents/1729,
four workers, partition 64, ten opening plus 25 continuation turns. The
parent used its `m=30,s=1` defaults and unbounded primitive investment; the
candidate used its new defaults. This measures the complete tuning change,
including the larger resulting population, rather than isolating one line
of code. Details, fingerprints, and metrics counters were off for timing.

| Tribes | Parent median AI + preparation + resolution | Generator-14 median | Median ratio | Parent / new people at turn 35 | Parent / new tile integrations at turn 35 |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 80 | 213.7 ms | 210.1 ms | .983 | 14,835 / 46,752 | 2,480 / 2,455 |
| 320 | 1,263.2 ms | 1,070.2 ms | .847 | 60,585 / 198,792 | 9,869 / 9,684 |

Median allocated bytes per agricultural turn were 133.6/132.2 MiB at 80
and 518.3/516.6 MiB at 320 (parent/new). The median of individual paired
turn ratios was 1.01 at 80 and .783 at 320; variation within this one pair
is substantial. Three ordered pairs, an exact-replay run, and broader
climate/era balance remain open. This calibration does not declare the
older within-10%-of-`main` performance target complete.

## Reproduce

Run sequentially from this Windows repository checkout. Outputs below are
ignored under `TestResults`; use the tables above for review or keep the
raw files from the reproduced run. The isolated and full-world commands use
the same production defaults unless a flag explicitly overrides them.

```powershell
$sdk = (Resolve-Path ./.tools/dotnet/dotnet.exe).Path
& $sdk build Tools/LandFoodPopulationSweep/Anthropocene.LandFoodPopulationSweep.csproj -c Release
& $sdk Tools/LandFoodPopulationSweep/bin/Release/net8.0/Anthropocene.LandFoodPopulationSweep.dll --repo . --out TestResults/FoodTargetSweep/generator14-isolated --populations 24 --live-turns 120 --controlled-turns 8

$paleoRoot = 'TestResults/FoodTargetSweep/generator14-paleo-config'
$paleoResources = Join-Path $paleoRoot 'Assets/_Anthropocene/Resources'
New-Item -ItemType Directory -Path $paleoResources -Force | Out-Null
Copy-Item Assets/_Anthropocene/Resources/ResourceGenerationConfig.json $paleoResources
$paleoConfig = Get-Content Assets/_Anthropocene/Resources/OpeningConfig.json -Raw | ConvertFrom-Json
$paleoConfig.ExperienceThreshold = 1000000000
$paleoConfig.MajorityEligibilityYears = 1000000
$paleoConfig.ForcedTransitionYears = 1000001
$paleoJson = $paleoConfig | ConvertTo-Json -Depth 100
[System.IO.File]::WriteAllText((Join-Path (Resolve-Path $paleoResources).Path 'OpeningConfig.json'), $paleoJson, [System.Text.UTF8Encoding]::new($false))
& $sdk Tools/LandFoodPopulationSweep/bin/Release/net8.0/Anthropocene.LandFoodPopulationSweep.dll --repo $paleoRoot --out TestResults/FoodTargetSweep/generator14-paleo --populations 24 --live-turns 120 --controlled-turns 8

& $sdk build Tools/PerformanceAudit/Anthropocene.PerformanceAudit.csproj -c Release
foreach ($seed in @(11, 1729, 23)) {
    & $sdk Tools/PerformanceAudit/bin/Release/net8.0/Anthropocene.PerformanceAudit.dll --production-config=1 --players=80 "--seed=$seed" --shape=Continents --turns=12 --post-opening-turns=80 --details=1 --fingerprints=0 --metrics=0 "--output=TestResults/FoodTargetSweep/generator14-standard80-$seed.json"
}
./Tools/Test-Headless.ps1 -Seeds 0
```

For the timing pair, archive the parent source and production configuration
from `8ddd4e2`, build this same audit project with
`-p:SimulationSource=<archive>/Assets/_Anthropocene/Simulation`, then run
that binary from the archive root so it reads the parent JSON. Run the
generator-14 binary from the current root. For both use
`--production-config=1 --seed=1729 --shape=Continents --turns=12
--post-opening-turns=25 --warmup=1 --details=0 --fingerprints=0 --metrics=0`,
with `--players=80` or `320`. Run the four processes sequentially and keep
separate JSON outputs. Compare the `postOpening` samples' `totalMs`, `aiMs`,
`prepareMs`, `executeMs`, `allocatedBytes`, `population`, and
`tileIntegrations`; do not use the outcome reports as performance evidence.
