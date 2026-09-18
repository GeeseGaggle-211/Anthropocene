# Resource access from spawned tribes — September 18, 2026

The five-world audit finds plentiful nearby Stone, Clay and Wood, but distant and uneven metal opportunities. Broader scattered fields do not yet give most starts a nearby substantial Copper or Tin supply. No generation, placement or economy settings were changed for this audit.

## Sample and meaning

Generated **400 tribes**: Standard **1024×512**, 80 tribes each, Continents seeds **11, 42, 1729**, Pangaea **42**, Archipelagos **42**. Simulation source is commit **7416187** on `feature/geological-resource-generation`, core generator 11/resource generator 2, with the resource configuration's default **100,000 maximum** per body/surface source. Simulation defaults and start separation 12 are used. This is a designed comparison of five worlds, not an estimate of the frequency of each shape in play.

The catalog has no formal Neolithic-era tags. The core basket is Stone, Clay, Wood, Copper and Tin from the earlier design discussion. Eight supporting materials use the catalog's same early access tier: Gold, Lead, Zinc, Silver, Salt, Limestone, Silica and Gypsum. This is a provisional early-material basket, not a historical assertion: Tin explicitly requires `BronzeWorking`, and Silica requires `Glassmaking`.

“Accessible” means **latent physical potential after the required discovery technology**, using saved access and exposure fractions. Stone/Clay have 100% base access, Wood 25%, and the other ten materials 10% before site exposure. The opening currently has no material harvesting/economy or resource fields in player observations. Copper remains undiscovered until `CopperWorking`; every other material retains its catalog technology requirement. These numbers are neither owned inventory nor an assertion that tribes can collect them now.

Distances are shortest **hex steps** on traversable land, respecting longitude wrapping and excluding water, lakes and elevation-level-10 peaks. Zero means the camp tile. “No land route” means no qualifying source anywhere in that connected traversable land component. It does not mean no source exists elsewhere in the world. A separate geometric distance ignores intermediate barriers but still targets traversable-land sources. No distance is converted to kilometers, travel turns or transport capacity.

## Nearest occurrence, including traces

Percentages use all 400 tribes. Median and P90 use only tribes with a land route; unreachable tribes are not assigned an artificial distance. Quantiles use linear interpolation, so some distances are fractional summaries of integer measurements.

| Material | Within 2 land steps | Within 10 | Median steps, reachable | P90 steps, reachable | No land route |
| --- | ---: | ---: | ---: | ---: | ---: |
| Stone | 100% | 100% | 0 | 1 | 0/400 |
| Clay | 91.75% | 99.75% | 1 | 2 | 1/400 |
| Wood | 100% | 100% | 0 | 0 | 0/400 |
| Copper | 16.5% | 35% | 13 | 76 | 86/400 |
| Tin | 4% | 8.75% | 50 | 137.8 | 93/400 |
| Gold | 20.75% | 34.5% | 15 | 62 | 73/400 |
| Lead | 12% | 21.25% | 28 | 81 | 89/400 |
| Zinc | 12% | 21.25% | 28 | 81 | 89/400 |
| Silver | 17.75% | 33% | 15 | 66 | 81/400 |
| Salt | 4.25% | 11.5% | 45 | 121.6 | 108/400 |
| Limestone | 5.25% | 7.25% | 67.5 | 185 | 104/400 |
| Silica | 2.25% | 5% | 58 | 131.8 | 97/400 |
| Gypsum | 2.5% | 6.5% | 55.5 | 143.1 | 110/400 |

All 400 starts have Wood on the camp tile; 327 have Stone and 152 have Clay. All starts have Stone within two steps. The sole unreachable Clay start is **Pangaea/42, player 56** (one-based tribe 57), tile 346337, in a 26-tile traversable component.

The median accessible stock summed within two land steps is **1,245,775 Stone**, **389,405 Clay** and **418,546.25 Wood**. These exceed the cap because the cap is per source, not per catchment. Each metal's radius-two median is zero. Catchments can overlap between tribes and must not be summed as independent exclusive reserves. Wood figures are capacities; this audit does not accumulate future renewal.

## Traces versus larger quantities

Thresholds below require the stated accessible quantity **at one target tile**, including all contributions there. They are diagnostic cutoffs, not proposed recipe requirements, extraction rates or a definition of viable supply.

| Material | Minimum accessible units at tile | Within 10 steps | Median steps, reachable | No land route |
| --- | ---: | ---: | ---: | ---: |
| Copper | Any positive | 140/400 (35%) | 13 | 86/400 |
| Copper | 1 | 62/400 (15.5%) | 29 | 91/400 |
| Copper | 10 | 24/400 (6%) | 41 | 95/400 |
| Copper | 100 | 4/400 (1%) | 107 | 123/400 |
| Tin | Any positive | 35/400 (8.75%) | 50 | 93/400 |
| Tin | 1 | 26/400 (6.5%) | 56 | 96/400 |
| Tin | 10 | 15/400 (3.75%) | 61 | 96/400 |
| Tin | 100 | 6/400 (1.5%) | 78 | 107/400 |

The median quantity at the nearest reachable trace-inclusive tile is **0.420 Copper units** or **4.383 Tin units**. The nearest source is chosen by distance, not maximum quantity among equal-distance candidates. A more generous aggregate criterion—at least ten accessible units summed anywhere within ten steps—passes **75/400 Copper** and **29/400 Tin** starts. This differs from requiring ten units at a single tile.

Only **2/400** starts have both Copper and Tin within two steps, **7/400** within five, and **17/400** within ten, including traces. These checks do not imply a completed bronze supply chain.

## Variation by world

These distances include traces; medians again exclude unreachable starts. The small Archipelago medians describe only the few starts on resource-bearing land components.

| World, 80 tribes each | Copper within 10 | Copper median / no route | Tin within 10 | Tin median / no route |
| --- | ---: | ---: | ---: | ---: |
| Continents 11 | 37 | 12 / 3 | 9 | 54 / 4 |
| Continents 42 | 31 | 13 / 5 | 9 | 42 / 5 |
| Continents 1729 | 44 | 7 / 3 | 6 | 98 / 3 |
| Pangaea 42 | 19 | 41.5 / 6 | 8 | 45 / 6 |
| Archipelagos 42 | 9 | 2 / 69 | 3 | 6 / 75 |

Across the four non-Archipelago worlds, 17/320 starts lack a Copper land route and 18/320 lack Tin. In Archipelagos, **86.25% lack Copper** and **93.75% lack Tin** on their reachable land component. Geometric median distances in that world are 65 steps to Copper and 112 to Tin; crossing water alone does not imply a nearby target. Actual maritime paths are outside this audit.

The evidence suggests two separate tuning questions: quantities in scattered fields, and their geographic coverage. Raising the reserve cap can improve quantities at existing occurrences but does not place sources on resource-free islands. If broadly local early material access is desired, the next generation pass should evaluate occurrence/host coverage as well as quantity. Placement should remain independent of minerals; this report does not propose adding deposits around camps. Salt, Limestone, Silica and Gypsum also merit attention if they become broadly required early inputs. No tuning decision was applied here.

## Reproduction and verification

From the repository root:

```powershell
./Tools/ResourceAccess/Measure-Starts.ps1
python ./Tools/ResourceAccess/Summarize.py

# Optional comparison with a different cap, in a separate output directory:
./Tools/ResourceAccess/Measure-Starts.ps1 -Maximum 1000000 -Output TestResults/TribeResourceAccess1M
python ./Tools/ResourceAccess/Summarize.py TestResults/TribeResourceAccess1M
```

The one-million command is an example, not a measured result in this report. `-Worlds` accepts comma-separated shape/seed pairs, e.g. `Continents:11,Archipelagos:42`. The audit reads `ResourceGenerationConfig.json` and records all resolved options and hashes. It compiles actual simulation sources in a separate .NET tool, touches no user save slot, and emits per-world JSON plus `data.json`; the dependency-free Python summarizer emits `tribes.csv`, `summary.csv`, `summary.json` and compact `view-data.json`. Player IDs in files are zero-based; the interactive report additionally labels tribes one-based.

The algorithm builds the map adjacency graph once, then uses multi-source breadth-first searches per material and threshold. Camp catchments are cached to radius ten. Bodies contribute their remaining component reserve allocated by exact site shares; exposure and saved accessibility apply afterward. Source-funded placer quantities are not counted again as their parent's initial reserve. Surface quantities and body quantities are checked for conservation separately from access. All five worlds retained identical authoritative fingerprints before/after measurement and spawned exactly 80 camps.

An independent delegated reviewer regenerated Continents/11 and Archipelagos/42 and passed **3,246 comparisons** across 11 ordinary, distant and unreachable starts, all 13 materials and all four thresholds. Reference checks used camp-origin BFS, direct `HexMap.Distance` scans, explicit component/share/exposure arithmetic, rich-source distance, component size and radius 0/1/2/5/10 totals. The regenerated world fingerprints matched exactly. Evidence is ignored under `TestResults/TribeResourceAccess/Review/` (`Program.cs`, `Review.csproj`, `result.txt`); primary JSON and CSV are alongside it. The inline report was checked in a browser for threshold/world/material updates and actual generated geography. This is read-only simulation analysis, not an additional Unity player or economy playtest.
