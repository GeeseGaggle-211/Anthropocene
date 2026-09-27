# Shipped agriculture and husbandry route playtests

The `features/neolithic-agricultural` route sweep used the shipped new-world
configuration from `8622750` after merging the destination-aware mobile
husbandry work at `2b17cb8`. It is a normal AI playtest: no land or field
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
`5EAF5DFA48EF0A1C6F3B02ED73AF69478C0FA619CE236A6C6D76E3476FAF8F19`.
The raw reports are ignored local artifacts in `TestResults/RoutePlaytests`;
the command and committed summarizer below regenerate these tables.

## Final residents

| Seed | Cultivation camps | Cultivation p25 / median / p75 | Husbandry camps | Husbandry p25 / median / p75 | Total humans |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 11 | 60 | 308 / 759 / 1,054 | 19 | 422 / 932 / 1,293 | 64,466 |
| 23 | 57 | 412 / 677 / 1,005 | 19 | 892 / 1,210 / 1,545 | 70,807 |
| 1729 | 57 | 497 / 646 / 1,163 | 22 | 780 / 1,072 / 1,316 | 72,821 |
| Pooled | 174 | 417 / 683 / 1,087 | 60 | 722 / 1,074 / 1,404 | 208,094 |

The exact decimal quartiles are produced by the summarizer. The world total
includes other people, including connected hunters; it is not the sum of the
two resident-route columns. No villages appeared. `Camp.IsVillage` is read by
the action, observation, and display code, but normal simulation code has no
assignment that promotes a camp to a village. This sweep therefore supplies
no village population distribution. A promotion rule must be designed and
implemented before natural village quartiles can be measured.

## Final domestic herds

Every domestic herd in these runs belonged to a husbandry camp. One seed-11
herd record was extinct and is excluded from the live-herd percentiles.

| Seed | Live herds | Extinct records | Animals p25 | Median | p75 | Total animals |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 11 | 18 | 1 | 255 | 707 | 977 | 12,755 |
| 23 | 19 | 0 | 538 | 888 | 1,181 | 19,101 |
| 1729 | 22 | 0 | 578 | 727 | 913 | 18,048 |
| Pooled | 59 | 1 | 409 | 755 | 1,129 | 49,904 |

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
| Cultivation | 135,146 | 130,094 (96.3%) | 0 | 2,183 (1.6%) | 1,617 (1.2%) | 521 (0.4%) | 502 (0.4%) | 204 (0.2%) | 25 (<0.1%) |
| Husbandry | 68,229 | 0 | 24,709 (36.2%) | 41,242 (60.4%) | 0 | 0 | 0 | 2,278 (3.3%) | 0 |

The split is consistent in each world: farmers account for 95.8–96.6% of
agricultural residents; herders for 33.3–37.9% of husbandry residents.
Agricultural camps made cloth (1,314,396 units), tools (329,394), some
housing (1,620), pots (313), and farm tools (56) over the final ten turns.
Husbandry camps assigned craftspeople but recorded **zero manufactured
output across all 90 turns**. All 5,047 husbandry camp-turn detail rows had
zero Stone and zero HandTools; 4,249 held AnimalFood. The recipe path requires
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
| 11 | 99.8% | 0.508 | 0.510 | 99.5% | 0.093 | 100.0% | 96.5% | 2,515 / 2,288 |
| 23 | 99.8% | 0.509 | 0.511 | 99.4% | 0.125 | 100.0% | 95.8% | 4,275 / 3,350 |
| 1729 | 99.7% | 0.514 | 0.512 | 99.5% | 0.115 | 100.0% | 96.0% | 4,181 / 3,406 |

Husbandry AI made 298, 313, and 311 moves across the three runs. There were
zero changed land-condition tiles at every measured turn. Mean agricultural
field intensity is about 0.51, above the `q=0.4` soft threshold but well
below full access; it is a measured outcome, not a hard cap. Herd water is the
remaining resource shortfall, at roughly 4% unmet, despite full forage.

The full fixed headless suite passed after shipping the defaults, including
36-resolution scalar/two-/four-worker replay with reversed partitions and
collections, 25 post-opening turns, the mature agricultural save restoration,
and 4,724 cultivation score-cache differential assertions. The three route
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
$root = 'TestResults/RoutePlaytests'
New-Item -ItemType Directory -Path "$root/bin" -Force | Out-Null
& $sdk build Tools/PerformanceAudit/Anthropocene.PerformanceAudit.csproj -c Release -t:Rebuild --output "$root/bin" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Audit build failed' }
$dll = (Resolve-Path "$root/bin/Anthropocene.PerformanceAudit.dll").Path
foreach ($seed in @(11, 23, 1729)) {
    & $sdk $dll --players=80 --turns=12 --post-opening-turns=80 "--seed=$seed" --shape=Continents --warmup=1 --details=1 --fingerprints=2 --production-config=1 --revision=8622750 "--output=$root/shipped-80-$seed.json"
    if ($LASTEXITCODE -ne 0) { throw "Playtest failed: $seed" }
}
python Tools/PerformanceAudit/Summarize-RoutePlaytests.py $root shipped
```

The summarizer prints the full per-world and pooled species and specialty
tables. `Docs/MobileHusbandryValidation.md` contains the matched stationary
control and earlier feature-checkpoint comparison. That comparison used the
same tuning numbers as CLI overrides; this report confirms their shipped
production configuration behaves the same.
