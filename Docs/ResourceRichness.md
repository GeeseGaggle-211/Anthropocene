# Mineral richness and deep reserves — September 18, 2026

Production generation now uses `MaximumQuantity=10000000` and `DeepReserveMultiplier=50`. For an old early-accessible mineral amount of **1 unit**, the new model supplies approximately **100 early-accessible units** and **5,000 deep units**. Early access is therefore `100 / 5100 = 1.9608%` of the combined mineral reserve. Deep stock adds nothing to the starting-accessible overlay. The 50× setting follows the user's final clarification and replaces the provisional 100× deep multiplier discussed during implementation.

This applies per mineral, including associated co-products, scattered occurrences, concentrated deposits, placers, Stone and Clay. Wood retains its separate 25% accessibility and renewable-capacity rules, with no deep reserve. Its capacity scales with the global source setting, independently of edible biomass. Existing material technology requirements remain; the saved deep requirement is `AdvancedMining`. Actual extraction rates, the mining technology curve and exponential consumption remain a future economy pass.

## Same five worlds, 400 starts

The comparison regenerates the historical Standard 1024×512 sample: 80 tribes each on Continents 11/42/1729, Pangaea 42 and Archipelagos 42. Iron was added to the earlier 13-material local audit. All 37 active materials were checked for global pool accounting, including the scattered subset.

The table counts tribes with **one tile holding at least the threshold accessible units within ten traversable-land steps**. These cutoffs distinguish traces from larger stocks; they are not extraction throughput or recipe requirements. Water, lakes and level-10 peaks block the path. A tribe may have a geographically close source across water but no land route.

| Material | ≥10 units: before → after | ≥100 units: before → after | Median steps to ≥100 units: before → after |
| --- | ---: | ---: | ---: |
| Copper | 24 → 134 / 400 | 4 → 62 / 400 | 107 → 29 |
| Tin | 15 → 35 / 400 | 6 → 26 / 400 | 78 → 56 |
| Zinc | 3 → 85 / 400 | 0 → 51 / 400 | 116 → 37 |
| Iron | 28 → 120 / 400 | 1 → 102 / 400 | 202 → 32 |

Distance medians exclude unreachable tribes; denominators for access percentages always include all 400. At the 100-unit threshold, the new no-land-route counts are Copper 91, Tin 93, Zinc 89 and Iron 69. Median summaries alone must not be read as a guarantee of access.

New mean stocks per complete generated world (not per tribe):

| Material | Early-accessible units | Deep units |
| --- | ---: | ---: |
| Copper | 3,336,306 | 166,815,318 |
| Tin | 1,533,553 | 76,677,625 |
| Zinc | 1,888,876 | 94,443,786 |
| Iron | 2,657,615 | 132,880,768 |

The rounded means can hide the exact ratio; unrounded mineral deep totals equal 50× accessible totals. World totals include all geological settings; the per-start land-path audit excludes unreachable sources. Neither measure is owned inventory or currently discovered supply.

## What changed, and what did not

For positive radius-two scattered Copper/Tin/Zinc/Iron quantities, the increase is exactly 100× within floating-point precision. Mixed concentrated/scattered local ratios vary approximately **99.993–100.007×** due to the existing reference-budget rounding; surface rasters are also quantized. The final separate-pool implementation matches the frozen 10-million-cap accessible quantities to a maximum relative difference of **4.22×10⁻¹⁵**.

Occurrence locations and land barriers are unchanged. Counting any positive quantity, the fractions of starts with a source within ten land steps remain **Copper 35%, Tin 8.75%, Zinc 21.25%, Iron 30%**. This tuning supplies more material at existing locations; it does not yet make copper or iron locally present for most tribes. Tin remains much less locally available, and isolated islands remain constrained. If the next goal is widespread nearby iron, occurrence/host coverage will need a separate adjustment. Camp placement and food/freshwater geography were not changed to manufacture mineral access.

The new totals are not the old raw host reserve plus extra deep stock. Schema 4 converts the reference host/composition/exposure/access inputs into explicit early-accessible and deep **material pools**. Counting the old reference host as inventory as well would double-count material. See [GeologicalResources.md](GeologicalResources.md) for the API and persistence rules.

## Validation and reproduction

- **Five worlds, 400 tribes, 14 local materials**: all generated; measurement left state unchanged and all five full saves round-tripped with identical bytes and fingerprints.
- **40,046 geometry/metadata comparisons**: same cohorts, sampled source locations, geometric/land paths, identities and discovery settings before/after.
- **145,606 additional comparisons**: all local stock fields and four nearest-source thresholds match the frozen 100× reference experiment, plus latest-source and explicit historical-preset fingerprint checks.
- **All 37 materials**: total, accessible and deep allocation reconcile; deep=50×accessible for minerals and zero for Wood, including scattered fields.
- **26/26 Unity tests**: total/accessible raster accounting, separate scale, depleted early pool with intact deep stock, saved semantics and unchanged state/observations.
- **Focused headless suite**: 437 core assertions, 6,518 geological assertions and scalar/2/4-worker replay through twelve resolutions passed.
- **Large world**: Archipelagos/42/2048×1024/80, 426 bodies; generation 23.60 seconds, collected heap 922.93 MiB and packed resources 33.66 MiB. All material pools passed; save/load preserved fingerprints and all 231,323,756 save bytes. This is one concurrent-run measurement, not a performance comparison.
- **Atlas export**: production-config Continents/42/96×48 produces 122 maps, including 35 nonempty deep layers. All 37 accessible PNGs, five geography PNGs and Wood total/renewal are byte-identical with deep reserves toggled off: 44 image comparisons. Total/deep allocation checks pass.

The historical baseline and an immutable pre-change audit executable are kept under ignored `TestResults/TribeResourceAccessRichness/{Baseline,Snapshot}`. The actual final model is under `Final`, with JSON, `tribes.csv`, `summary.csv`, `material-totals.csv` and before/after comparisons. `100x` is the intermediate cap-only experiment and must not be represented as the final deep-reserve implementation. Unity and atlas evidence are under `TestResults/ResourceRichness`.

```powershell
# Current production preset:
./Tools/ResourceAccess/Measure-Starts.ps1 -Output TestResults/CurrentResourceAccess
python ./Tools/ResourceAccess/Summarize.py TestResults/CurrentResourceAccess

# Explicit historical control despite the changed production JSON:
./Tools/ResourceAccess/Measure-Starts.ps1 -Maximum 100000 -DeepReserveMultiplier 0 -Output TestResults/HistoricalResourceAccess
python ./Tools/ResourceAccess/Summarize.py TestResults/HistoricalResourceAccess
python ./Tools/ResourceAccess/Compare.py TestResults/HistoricalResourceAccess TestResults/CurrentResourceAccess

# Actual generated maps and resource save:
./Tools/WorldAtlas/Export-Worlds.ps1 -Seeds '42' -Shape Continents -Output TestResults/CurrentResourceAtlas -Save
```

Historical API defaults remain 100,000/0 to preserve reference generators and their hashes; the player, atlas and tribe-audit entry points load the production configuration explicitly. Saved worlds keep their own quantities, schema and technology metadata. New deep pools are never generated implicitly for an old save.
