# Shipped agriculture and husbandry route playtests

The `features/neolithic-agricultural` route sweep used the shipped new-world
configuration from `8622750` after merging the destination-aware mobile
husbandry work at `2b17cb8` and restoring the village checkpoint. It is a
normal AI playtest: no land or field
tuning CLI overrides, forced movements, or injected settlements. The four
natural human-accessible fractions are uniformly half their former values;
generated land biomass is multiplied by 63. The Neolithic cultivation effort
multiplier beyond the soft `q=0.4` threshold is 16. These are new-world
defaults; loaded saves keep their stored values.

## Method

Each Standard/Continents world began with 80 tribes and completed its natural
opening in ten turns, then continued for 80 more twenty-year turns. The final
population percentiles treat each extant camp as one observation; they are
linear interpolations at ranks `(N-1) × {0.25, 0.5, 0.75}`. Resident counts
exclude connected hunter groups. Domestic-herd percentiles treat each live
herd as one observation; extinct records are counted separately. The pooled
rows combine entities from the three worlds before computing percentiles.

The audit ran on Windows x64 with .NET 8.0.31, four workers, partition size
64, one warmup, production configuration, detailed rows, and initial/final
fingerprints. All three reports have config hash
`5CE4E1EE9669FDF168052235B2E8B7824A4DBED7E12698FD52AD5D0D68088B76`.
The measured Release audit DLL SHA-256 is
`4534C3DCA7D4FA9617F6A1F8B77C74E8D6E315124C89131CC8DA186D426B5DB9`.
The raw reports are ignored local artifacts in `TestResults/VillageCheckpoint`;
the command and committed summarizer below regenerate these tables.

## Final residents

| Seed | Agricultural villages | Village p25 / median / p75 | Agricultural camps | Husbandry camps | Husbandry p25 / median / p75 | Total humans |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 11 | 59 | 313 / 765 / 1,054 | 1 (28 residents) | 19 | 422 / 932 / 1,293 | 63,541 |
| 23 | 56 | 418 / 679 / 1,028 | 1 (27 residents) | 19 | 892 / 1,210 / 1,545 | 70,807 |
| 1729 | 57 | 497 / 646 / 1,163 | 0 | 22 | 780 / 1,072 / 1,316 | 72,821 |
| Pooled | 172 | 437 / 698 / 1,069 | 2 (p25/median/p75: 27.2/27.5/27.8) | 60 | 722 / 1,074 / 1,404 | 207,169 |

The exact decimal quartiles are produced by the summarizer. The world total
includes other people, including connected hunters; it is not the sum of the
resident-route columns. A cultivation camp becomes a village when its own
fields average at least `q=0.25` over every radius-one traversable land tile.
This is a settlement checkpoint, not a population cap. No husbandry camp
became a village. The two final agricultural camps were below the field
checkpoint and were too few for a meaningful distribution by world.

## Final domestic herds

Every domestic herd in these runs belonged to a husbandry camp. One seed-11
herd record was extinct and is excluded from the live-herd percentiles.

| Seed | Live herds | Extinct records | Animals p25 | Median | p75 | Total animals |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 11 | 18 | 1 | 255 | 707 | 977 | 12,889 |
| 23 | 19 | 0 | 538 | 888 | 1,181 | 19,101 |
| 1729 | 22 | 0 | 578 | 727 | 913 | 18,048 |
| Pooled | 59 | 1 | 409 | 755 | 1,129 | 50,038 |

| Species, pooled | Live herds | p25 | Median | p75 |
| --- | ---: | ---: | ---: | ---: |
| Antelope | 12 | 440 | 652 | 688 |
| Bison | 9 | 113 | 455 | 555 |
| Camel | 5 | 2,173 | 2,436 | 2,616 |
| Goat | 29 | 789 | 983 | 1,206 |
| Horse | 4 | 598 | 745 | 788 |

## Economic specialties

These are final resident assignments pooled across the three worlds.
Gatherers are residents without an explicit specialist assignment. Thus the
shares describe people, not percentages of camps.

| Route | Final residents | Farmer | Herder | Gatherer | Weaver | Toolmaker | Stonecutter | Craftsperson | Builder |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Cultivation (172 villages, 2 camps) | 135,009 | 129,963 (96.3%) | 0 | 2,178 (1.6%) | 1,613 (1.2%) | 524 (0.4%) | 502 (0.4%) | 204 (0.2%) | 25 (<0.1%) |
| Husbandry camps | 67,445 | 0 | 24,791 (36.8%) | 40,383 (59.9%) | 0 | 0 | 0 | 2,271 (3.4%) | 0 |

The split remains consistent: farmers account for about 96% of agricultural
residents and herders for about 37% of husbandry residents. Agricultural
villages made cloth (1,313,921 units), tools (329,297), and some housing
(1,639) over the final ten turns; the two agricultural camps made another
540 cloth and 135 tools.
Husbandry camps assigned craftspeople but recorded **zero manufactured
output across all 90 turns**. All 5,047 husbandry camp-turn detail rows had
zero Stone and zero HandTools; 4,251 held AnimalFood. The recipe path requires
equipment for AnimalFabric, while the HandTools bootstrap requires Stone.
The persistent absence of these inputs strongly implicates the equipment
bootstrap. The report does not claim husbandry goods production is healthy;
the missing fabric and housing also contribute to husbandry's low QoL.

## Late-route health

Food and QoL below are weighted by the prior resident cohort over the final
40 turns. Herd fulfillment uses total realized divided by total requested
forage or freshwater in the same window.

| Seed | Cultivation food met | Cultivation QoL | Mean field q | Husbandry food met | Husbandry QoL | Herd forage met | Herd water met | Herd births / starvation deaths |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 11 | 99.9% | 0.509 | 0.510 | 99.5% | 0.091 | 100.0% | 96.7% | 2,515 / 2,156 |
| 23 | 99.8% | 0.509 | 0.511 | 99.4% | 0.125 | 100.0% | 95.8% | 4,275 / 3,350 |
| 1729 | 99.7% | 0.513 | 0.512 | 99.5% | 0.115 | 100.0% | 96.0% | 4,181 / 3,406 |

No village-to-camp departure occurred in the three normal AI runs. The AI
skips village relocation forecasts while food is secure and considers an
abandonment only in a food emergency with a better visible site. A player can
order a move at any time. An accepted move reverts village status immediately,
leaves fixed housing and physical field capital at the old site, and makes
those fields unavailable for cultivation that turn. Established domestic
expectations persist, so the existing QoL formula records the lost food and
housing. In a paired stay/move fixture, food fulfillment fell from 1.00 to
0.07 and QoL from 0.950 to 0.035 in the departure turn. This result uses the
ordinary QoL formula; the size of the drop depends on the destination's food
and portable shelter. Husbandry AI made 298, 313, and 311 moves. There were
zero changed land-condition tiles at every measured turn. Mean agricultural
field intensity is about 0.51, above the `q=0.4` soft threshold but well
below full access; it is a measured outcome, not a hard cap. Herd water is the
remaining resource shortfall, at roughly 4% unmet, despite full forage.

The full fixed headless suite passed after restoring the village checkpoint,
including the 36-resolution scalar/two-/four-worker replay, reversed
partitions and collections, 25 post-opening turns, mature agricultural save
restoration, 4,724 cultivation score-cache differential assertions, and a
deliberate village abandonment case. The abandonment fixture additionally
matched exact fingerprints across scalar and four-worker resolution and save
continuation. The three route
playtests had zero land-condition writes and completed without exceptions.

## Reproduce

Run sequentially in the authoritative Windows checkout. `TestResults` is
ignored; no additional source tree is needed. Rebuilding from the same source
and runtime should reproduce outcomes and fingerprints, though measured
milliseconds can vary with the machine. Do not use timing values from this
detailed audit as a formal performance comparison: `--details=1` and final
fingerprint serialization add diagnostic work outside the measured phases.

```powershell
$sdk = (Resolve-Path ./.tools/dotnet/dotnet.exe).Path
$root = 'TestResults/VillageCheckpoint'
New-Item -ItemType Directory -Path "$root/bin" -Force | Out-Null
& $sdk build Tools/PerformanceAudit/Anthropocene.PerformanceAudit.csproj -c Release -t:Rebuild --output "$root/bin" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Audit build failed' }
$dll = (Resolve-Path "$root/bin/Anthropocene.PerformanceAudit.dll").Path
foreach ($seed in @(11, 23, 1729)) {
    & $sdk $dll --players=80 --turns=12 --post-opening-turns=80 "--seed=$seed" --shape=Continents --warmup=1 --details=1 --fingerprints=2 --production-config=1 --revision=village-checkpoint "--output=$root/village-80-$seed.json"
    if ($LASTEXITCODE -ne 0) { throw "Playtest failed: $seed" }
}
python Tools/PerformanceAudit/Summarize-RoutePlaytests.py $root village
```

The summarizer prints the full per-world and pooled species and specialty
tables. `Docs/MobileHusbandryValidation.md` contains the matched stationary
control and earlier feature-checkpoint comparison. That comparison used the
same tuning numbers as CLI overrides; this report confirms their shipped
production configuration behaves the same.
