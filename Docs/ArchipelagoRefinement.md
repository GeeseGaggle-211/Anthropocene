# Archipelago fragmentation and hotspot trails

This follow-up addresses the requested increase in small island chains, narrower continental fragments and irregular historical hotspot tracks. It follows the generator-9 replacement described in [WorldGeneration.md](WorldGeneration.md). The evidence below comes from generated simulation state; no illustrative islands were added to the maps.

## Preserved generator-9 reference

The reference is **Standard / Archipelagos / seed 42 / 80 tribes**, at 1,024 × 512 tiles. Its original maps, measured geography, configuration, source/save fingerprints and playable save are preserved under `TestResults/ArchipelagoRefinement/Before/archipelagos-42`. The previous `TestResults/WorldGeneration/After` copies remain unchanged.

| Measure | Generator 9 |
| --- | ---: |
| Land share of world | 15.10% |
| Land tiles | 79,180 |
| Separate land components | 49 |
| Largest component | 36,731 tiles; 46.39% of all land |
| Five largest components together | 97.80% of all land |
| Components with at least 1,000 tiles | 5 |
| Components with 100–999 tiles | 2 |
| Components with 10–99 tiles | 33 |
| Components with 1–9 tiles | 9 |
| Components with 4–999 tiles | 36 |
| Coastal land tiles / all land | 6.52% |
| Viable placed camps | 80 / 80 |

The maps were inspected in environment, elevation and volcanic views. They show a few broad, forested landmasses and several short, nearly straight strings of similarly sized islands. The small-island count alone concealed the problem: almost all land area remained in five large connected masses, with very little middle-sized fragmentation.

The generator-9 Archipelagos field started from ten broad nuclei, each with two short branching arms. Overlapping fragments joined into larger masses. Its eight hotspot origins each offered four age samples at most, using a sinusoidal bend and age-multiplied spacing offsets. Those choices produced recognizable chains but did not provide the requested density or irregular historical progression.

## Generator 10 model

Archipelagos now starts from **30 curved ribbons of crust**. Each contains several elongated fragments, with variable spacing, width, coastline detail and occasional asymmetric peninsulas. Transverse rifts break the ribbons into separate islands. The Continents and Pangaea base-shape models are preserved; the new hotspot history applies to every shape.

Each hotspot begins at a seeded marine origin and follows one continuous drifting path. A moving uplift window advances along that path in x, y and diagonal directions. Correlated Gaussian changes in heading and lateral position, plus a smooth regional direction field, bend the path while preserving local continuity. Uplift pulses vary in spacing and width; radius and height also vary. At default settings Archipelagos tracks have 40–70 sampled positions, with several separated eruption episodes along the same trajectory. These positions are samples of a continuous procedural path, not independent random island locations.

The default eight active hotspot origins have an additional 40 historical origins in Archipelagos, or 16 historical origins in the other shapes. Placement may reject an origin without suitable initial ocean floor. Older samples shrink and subside; some persist as submerged seamounts. Relative age, step spacing and uplift strength are generation parameters, not measured geological time or plate speeds. Current volcanic activity is recorded separately from inherited topography, so extinct trails need not remain marked as active.

Overlapping windows use the same preserved initial ocean floor for eligibility and relief. Their elevations merge by a maximum envelope, and current hotspot activity has its own maximum envelope added to the tectonic contribution. Uplift blends into the local seabed and fades smoothly to zero at its outer support. This avoids the sharp, constant-height underwater strips exposed during intermediate map inspection.

The geological inspiration is the succession of volcanoes along a plate-motion hotspot track and the later decline, erosion and subsidence of older islands, described in the USGS [Evolution of Hawaiian Volcanoes](https://www.usgs.gov/observatories/hvo/evolution-hawaiian-volcanoes) and [Growth and degradation of Hawaiian volcanoes](https://www.usgs.gov/publications/growth-and-degradation-hawaiian-volcanoes). These sources support separating current activity from inherited volcanic relief. The Gaussian deviations, moving-window parameters and relative-age constants are game-generation choices; those publications do not calibrate them. This remains a procedural model rather than a physical geological simulation.

## Comparison method

Compare the same seed, shape, resolution, tribe count and gameplay tuning before and after the geometry change. Inspect additional seeds for variation rather than tuning solely to seed 42. Use the production generation pipeline for final exports, including placement and validation; geography-only exports are intermediate diagnostics and do not establish playable start counts.

Land-component sizes use six-neighbor hex connectivity with longitude wrapping. Land excludes lakes and sea. Component count, the largest/five-largest shares and the size bands above measure how area is distributed; coastal-land share provides a simple indication of how much land is near a shore. These are diagnostics, not physical island-area measurements or target quotas. A cluster of one-tile specks cannot substitute for inhabited, varied islands, and a high component count does not by itself prove narrow shapes or coherent chains.

Hotspot history must be checked in the implementation and deterministic tests as well as the rendered maps. A volcanic heatmap alone cannot establish that positions follow a cumulative trajectory, that deviations follow the intended distribution, or that old features subside. Environment and elevation maps must confirm that the actual terrain contains the resulting islands and submerged continuation.

The exporter records source/configuration/save fingerprints and uses the same palettes and fixed food/freshwater scales across runs. Successive iterations are in separate folders under `TestResults/ArchipelagoRefinement`; the reference and normal player save slot are preserved.

## Measured geography and visual review

Final generation uses unchanged gameplay tuning, Standard resolution and 80 tribes per world. Land share is a percentage of the whole world; all other percentages in this table use land as their denominator. The size band excludes one-, two- and three-tile specks.

| Archipelagos world | Land share | Land components | Largest component | Five-largest share | Components with 4–999 tiles | Land on 4–999-tile components | Coastal land |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Generator 9, seed 42 | 15.10% | 49 | 36,731 tiles | 97.80% | 36 | 2.19% | 6.52% |
| Generator 10, seed 42 | 11.15% | 462 | 3,492 tiles | 23.89% | 302 | 69.55% | 23.50% |
| Generator 10, seed 1729 | 11.99% | 455 | 3,851 tiles | 19.28% | 300 | 68.02% | 23.42% |
| Generator 10, seed 31415 | 11.50% | 450 | 2,585 tiles | 15.10% | 299 | 72.77% | 23.96% |

Environment, elevation and volcanic maps were inspected for all seven final exports: these three Archipelagos worlds, Continents seeds 42, 5 and 11, and Pangaea seed 42. The Archipelagos maps show many narrow continental fragments and longer, curved trails of smaller islands; the elevation layer shows submerged continuation between and beyond emergent islands. Active volcanic markers are concentrated near active heads while historical relief remains visible. The sharp, flat underwater strips from an intermediate implementation are absent after local-seabed blending and smooth support tapering. Following the final preset-only source change, all 30 map PNGs from the original six worlds were verified byte-identical to the inspected copies; seed 11 was inspected separately.

The controls retain their intended larger shapes. Continents 42 has two large land components and Continents 5 has three; Pangaea 42 retains one large component containing 97.69% of its land. Their land shares are 35.39%, 32.59% and 25.42%, respectively. The new shared preset world, Continents 11, has two large land components, 41.21% land and 91 separate components in the 4–999-tile size band. All **560 of 560** placed camps across the seven exports pass the seasonal food/freshwater viability check, and no residual filled-land component exceeds the depth threshold of 0.008. Local viability remains a separate check from visual morphology and multi-generation outcomes.

The longer hotspot histories also add many smaller islands around the larger world shapes:

| Control world | All land components, generator 9 → 10 | Components with 4–999 tiles, generator 9 → 10 |
| --- | ---: | ---: |
| Continents 42 | 41 → 125 | 37 → 102 |
| Pangaea 42 | 39 → 144 | 36 → 111 |

These counts describe separate islands, not the number of geological tracks. The environment and elevation maps show their arrangement into longer trails, while retaining the existing larger continental outlines.

The named playtests now select three natural camps from **Standard / Continents / seed 11 / 80 tribes**. Their tile IDs are 209873 (desert river), 192746 (steppe hunter) and 431802 (cold fisherman). The final export contains all three, with viability scores of 180%, 180% and 155.79%, respectively. The first two have rivers and nearby camel/bison herds; the cold camp has three adjacent Shallows tiles and a mean temperature of 2.25°C. Selection exchanges player ownership without changing the generated habitat or stocks. Detailed density measurements and seasonal interpretation are in [Playtesting.md](Playtesting.md).

## Reproducible copies

Open `TestResults/ArchipelagoRefinement/Final/index.html` for the seven-world gallery. Each world includes environment, elevation, renewable food, freshwater and volcanic PNGs, its exact configuration, camp data, measured geography, a provenance manifest and a separate playable `world.anthro` save. The files are generated artifacts and intentionally ignored by Git. The normal player save and the generator-9 reference remain untouched.

All seven final manifests record generator 10 and simulation-source SHA256 `BDCD35743FED43BFDCAB065B86ACFC064E03862A8743C5982326337328C91E0F`. Configuration and save checksums were independently verified; all seven worlds used the full production pipeline and placed 80 camps. Generation took 5.10–7.46 seconds per world in this export run, excluding image export and saving. These timings are observations on this workstation, not a performance guarantee.

The complete gallery and saves are also packaged in `TestResults/ArchipelagoRefinement/Anthropocene-archipelago-refinement-generator-10.zip` (341,666,074 bytes; 79 entries including 35 PNGs and seven saves). Its SHA256 is `4DE5DADFCE1C3E89E1459EC70661364E26E61CA6E005F0EAD018A441950D9C3C`. `final-artifact-verification.json`, `final-comparison.json` and `archive-verification.json` beside the archive record the checks and measured values. The archive contains only the final seven worlds and gallery; preserved references and intermediate iterations remain outside it.

## Opening simulation results

The separate `TestResults/ArchipelagoRefinement/OpeningAudit` runs the actual simulation and AI for the three Standard / Archipelagos / 80-tribe worlds until the opening completes. All **240 tribes survived**, every submitted order was legal, and every final save/load round trip restored the exact ending fingerprint. These simulations ended at different dates; the population totals therefore describe each completed opening, not a comparison at one common year.

| Seed | Opening years | Husbandry tribes / people | Cultivation tribes / people | Watercraft tribes / people | Total people |
| --- | ---: | ---: | ---: | ---: | ---: |
| 42 | 240 | 12 / 1,798 | 53 / 10,226 | 15 / 2,315 | 14,339 |
| 1729 | 220 | 19 / 2,117 | 48 / 7,499 | 13 / 2,313 | 11,929 |
| 31415 | 200 | 14 / 2,134 | 53 / 7,758 | 13 / 2,316 | 12,208 |
| **Total** | — | **45 / 6,049** | **154 / 25,483** | **41 / 6,944** | **38,476** |

The aggregate first-breakthrough shares are 18.75% Husbandry, 64.17% Cultivation and 17.08% Watercraft. Cultivation remains the most common outcome in this sample despite the increased coastline. These are measured AI outcomes from the new geography, not technology quotas, proof of universal balance, or a controlled before/after food-yield experiment.
