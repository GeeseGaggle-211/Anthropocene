# Cultivation effort above the Neolithic threshold

Status: measured opt-in calibration on `codex/food-population-calibration`. The
production `OpeningConfig.json` remains at land biomass multiplier `30`, its
original natural-access fractions, and cultivation effort multiplier `1`.
The measurements below do not authorize changing those defaults.

## Mechanic and cost

The authoritative field state remains `CultivationCapital` (`E`), saved per
tile. Intensity is still `q = min(1, qt sqrt(E))`, with `qt =
ReferenceIntensity = .4` in the measured profile. Neither `E` nor `q` has a
new hard cap at `.4`. The labor weight `C` is derived from that state:

```
k = ln(R) / .1
C(q) = (q / qt)^2                                    when q <= qt
C(q) = 1 + 2 / (qt k) * (exp(k (q - qt)) - 1)        when q > qt, R > 1
C(q) = E                                             when R = 1
```

`R` multiplies **marginal effort** for every extra `.1 q` beyond `qt`.
`C` and its first derivative meet the old quadratic work curve at `qt`.
Construction debits `C(q_after) - C(q_before)` times the existing biome and
tool cost. Maintenance charges `C(q)` times the existing maintenance and
biome cost. Planning forecasts, the bounded horizon allocator, and the legacy
expansion helper use that same weight. The saved physical capital and
ecological effect of a given `q` are unchanged. `R=1` takes the original
arithmetic path exactly.

| Intensity `q` | Old capital `E` | Effort `C` at `R=16` | Relative build and upkeep weight |
| ---: | ---: | ---: | ---: |
| .40 | 1.00 | 1.00 | 1.00× |
| .45 | 1.27 | 1.54 | 1.22× |
| .50 | 1.56 | 3.71 | 2.37× |
| .55 | 1.89 | 12.36 | 6.54× |
| .60 | 2.25 | 46.99 | 20.88× |
| .80 | 4.00 | 11,819.38 | 2,954.84× |

This is deliberately a steep **soft** barrier near `.6`, not a field-state
clamp. It would also impede a future high-intensity era unless that era changes
the labor relation; that question remains outside the implemented opening.

Turn boundaries remain authoritative. The prior cohort's food targets and
one-way resource pass are unchanged. Each candidate field uses a bounded set
of 32 intensity bands and at most seven local land tiles per camp. The
additional transcendental math costs `O(settlements × local fields × candidate
bands)`; it adds no work proportional to the number of years or seasons in a
turn. The resulting cost must still be profiled at mature-world and RL scale
before adoption.

## What “access factor .5” means

The tested food profile uses `m=63` for generated land food and multiplies
**every land habitat's natural human-accessible fraction** by the same
factor `s=.5`. It is not 50% accessibility on every tile:

| Land fraction | Production `s=1` | Test `s=.5` |
| --- | ---: | ---: |
| Rich or river | .25 | .125 |
| Grassland | .10 | .050 |
| Arid | .05 | .025 |
| Tundra | .08 | .040 |

Generated `MaxFood` (natural `G`) scales with `m`. `MaxPotentialFood`
(total biomass `B`) is derived by dividing that food by the habitat fraction.
Consequently, with the same `m`, changing `s` does not lower initial `q=0`
human food: it enlarges the biomass inaccessible until cultivation. Relative
to production `m=30,s=1`, the tested `m=63,s=.5` has approximately 2.1×
natural gatherable stock and 4.2× total land biomass before local rounding.
Humans still take only `G`; grazers can take all `B`, choosing `B-G` first;
`G=B` remains reachable at `q=1`. The extra total biomass may also support
more wild herds; husbandry is not recalibrated here.

## Isolated-site search

Each live case starts a 24-person camp on a pinned seven-land-tile generated
footprint. It retains wild herds, ecology, the production AI planner, and the
normal resolver, while removing other camps. The paired no-breakthrough case
keeps the same generated site but prevents agriculture. Runs last 120
20-year turns; these are long-run balance probes, not a claim that the
opening itself lasts 2,400 years. `q` below is the median of each site's
seven-tile mean; “highest” is the highest individual tile among the three.

First, hold `m=63,s=.39` and vary `R`:

| `R` | Median agricultural people T120 | Median mean `q` T120 | Highest tile `q` | Lowest site food satisfaction T120 |
| ---: | ---: | ---: | ---: | ---: |
| 1 | 556 | .88 | 1.00 | .90 |
| 8 | 940 | .56 | .59 | 1.00 |
| 10 | 918 | .55 | .59 | 1.00 |
| 12 | 917 | .54 | .57 | 1.00 |
| 16 | 887 | .53 | .56 | 1.00 |

The stronger barrier prevents the old drive toward `q=1`, but exposes too
much total biomass at `s=.39`. Holding `m=63,R=16`, a uniform increase in
the natural fraction reduces the extra stock cultivation unlocks:

| Uniform `s` | Median agricultural people T120 | Median mean `q` | Highest tile `q` |
| ---: | ---: | ---: | ---: |
| .50 | 688 | .52 | .55 |
| .550 | 627 | .52 | .55 |
| .575 | 596 | .51 | .54 |
| .600 | 577 | .51 | .54 |

At `m=63,s=.5,R=16`, the individual live sites show a gradual approach to
capacity, without the prior peak-and-collapse pattern:

| Site | People T40 | People T80 | People T120 | Mean `q` T120 | Food need met T120 |
| --- | ---: | ---: | ---: | ---: | ---: |
| Fertile open | 591 | 686 | 688 | .520 | 100% |
| Medium woodland | 830 | 945 | 945 | .531 | 100% |
| Dry marginal | 343 | 535 | 539 | .507 | 100% |

The no-breakthrough controls at `m=63,s=.5` end at 99, 218, and 63 people
on those sites; the median is **99**. They remain at `q=0`. These controls
demonstrate the target median, while the woodland and dry outcomes show that
a single median does not describe every habitat.

The sweep also checks that projected `0 <= G <= B`, both stocks remain under
their changing tile capacities, and food never becomes negative. The focused
headless ecology and conservation assertions pass.

## Three Standard worlds

The same `m=63,s=.5,R=16` was run with Standard/Continents, 80 tribes,
seeds 11, 23, and 1729, using AI orders and 70 post-opening turns after each
ten-turn opening. Medians include only camps whose technology is Cultivation.
`q` is the median of the camps' radius-one land-footprint means; the largest
tile `q` is also reported. This footprint statistic may include a shared
border tile, so it is a diagnostic rather than an ownership ledger.

| Seed | Agricultural camps T80 | Median people T40 | T60 | T80 | Median `q` T80 | Highest tile `q` T80 | Total people T80 |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 11 | 57 | 686 | 726 | 730 | .51 | .56 | 47,458 |
| 23 | 55 | 567 | 596 | 600 | .50 | .56 | 48,320 |
| 1729 | 58 | 526 | 595 | 609 | .50 | .56 | 48,318 |

The cross-world median village is **609** at turn 80; the village medians
change by +4, +4, and +14 people from turn 60 to 80. This is a gentle
late slope, not an artificial population cap. The three worlds vary
materially, with seed 11 at 730, so “600” remains a balance target rather
than an invariant for every map.

A matched seed-1729 control at `m=63,s=.575,R=1` reached median village
population 522 at turn 40, 580 at turn 60, then 522 at turn 80; at least
one tile reached `q=1`, and total population fell from 46,652 to 36,037
between turns 40 and 80. With `R=16` at that same food setting, median
village population was 477, 522, then 541, maximum tile `q=.55`, and total
population stayed near 43,000. This isolates the stabilizing effect of
labor cost before the later `s=.5` adjustment.

## Replay and cost evidence

The full `./Tools/Test-Headless.ps1 -Seeds 0` suite passes. Its cultivation
cache differential check caught an initial `R=1` floating-order change;
the implementation now uses the exact old arithmetic when `R=1`. A fresh
80-turn seed-1729 run at `m=63,s=.5,R=16` matched the final fingerprint of
70-turn save continuations with scalar, two workers, and four workers with
reversed partitions. The opt-in save therefore retains the effort parameter.
The fresh run captured every turn; continuations captured the final
fingerprint. Collection reorder under this opt-in setting has not been
separately checked, although the standard execution suite covers it at
production defaults.

Release timing is directional, from sequential warmed runs, not a repeated
paired performance acceptance test. For seed 1729 at 80 tribes, median
combined AI + preparation + resolution over turns 20–80 was 153.5 ms at
`R=1,s=.575` and 150.8 ms at `R=16,s=.575`. At 320 benchmark-only tribes,
one matched ten-turn opening pair at `m=63,s=.5` had agricultural-turn
medians 1,119.5 ms (`R=1`) and 1,126.7 ms (`R=16`), with about 573 MB
allocated per turn in either case. Only four opening turns had agriculture,
before most fields crossed `qt`; a mature 320-tribe profile and repeated
pairs are still required to claim a scaling result. The function adds no
per-year loop, but its exponential calls could become material with many
more high-intensity fields.

## Reproduce

Run from the Windows checkout in sequential Release processes. Output files
under `TestResults/CultivationEffortSweep` are intentionally ignored. Use
`--fingerprints=2` for a final exact hash without per-turn serialization;
`1` records every turn, and `0` disables hashes.

```powershell
$sdk = 'C:/Program Files/Unity/Hub/Editor/6000.5.10f1/Editor/Data/DotNetSdk/dotnet.exe'
& $sdk build Tools/LandFoodPopulationSweep/Anthropocene.LandFoodPopulationSweep.csproj -c Release
& $sdk build Tools/PerformanceAudit/Anthropocene.PerformanceAudit.csproj -c Release
$sweep = 'Tools/LandFoodPopulationSweep/bin/Release/net8.0/Anthropocene.LandFoodPopulationSweep.dll'
$audit = 'Tools/PerformanceAudit/bin/Release/net8.0/Anthropocene.PerformanceAudit.dll'
& $sdk $sweep --repo . --out TestResults/CultivationEffortSweep/repro-live --land-m 63 --land-access-scale .5 --cultivation-effort 16 --populations 24 --live-turns 120 --controlled-turns 1
& $sdk $sweep --repo . --out TestResults/CultivationEffortSweep/repro-paleo --land-m 63 --land-access-scale .5 --cultivation-effort 16 --no-breakthrough 1 --populations 24 --live-turns 120 --controlled-turns 1
foreach ($seed in @(11, 23, 1729)) {
    & $sdk $audit "--seed=$seed" --players=80 --shape=Continents --production-config=1 --land-m=63 --land-access-scale=.5 --cultivation-effort=16 --turns=12 --post-opening-turns=70 --warmup=1 --details=1 --fingerprints=0 "--output=TestResults/CultivationEffortSweep/repro-standard-$seed.json"
}
& $sdk $audit --seed=1729 --players=320 --shape=Continents --production-config=1 --land-m=63 --land-access-scale=.5 --cultivation-effort=16 --turns=12 --warmup=1 --details=0 --fingerprints=0 --output=TestResults/CultivationEffortSweep/repro-320.json
./Tools/Test-Headless.ps1 -Seeds 0
```

The `turns.csv` live rows expose population, field intensity, receipt and
food-satisfaction histories; audit `samples` expose per-turn camp population,
field intensity, food and combined phase time. For a paired control, use the
same command and change only `--cultivation-effort` to `1`. When comparing
cost, run controls and candidates sequentially with the same warmup, runtime,
seed and machine conditions.
