# Geological Iron Generation Correction and Ancient History Realignment — September 18, 2026

## Executive Summary

Anthropocene strategy simulations previously restricted Iron occurrence to a single pathway: Profile 8 ("Ancient iron formation" / BIF), while an initialization bug simultaneously marked every tile in every generated world with `GeologicalHistory.Ancient`.

This update resolves both issues within the existing geological resource generation pipeline:
1. **`Ancient` history initialization is corrected**: The universal default is removed. `Ancient` is now restricted to genuine cratonic continental terranes (stable continental interior crust away from active plate margins, volcanic arcs, and oceanic basins).
2. **Iron is split into five distinct geological pathways**:
   - **Banded iron formations (BIFs)** (Profile 8): Ancient basement/metamorphic cratons; industrial-scale motherlodes (70% Fe); high-grade, strongly clustered.
   - **Sedimentary ironstone / siderite** (Profile 37): Clastic and carbonate sedimentary basins; medium-grade (38% Fe) with broad regional distribution.
   - **Weathered goethite / limonite** (Profile 38): Weathered horizons over iron-rich parent lithologies (mafic, ultramafic, and basement rocks; excluding granite/clastics); shallow occurrences (45% Fe).
   - **Titaniferous magnetite** (Profile 39): Mafic and ultramafic intrusive complexes; regional igneous deposits (52% Fe) with associated Titanium co-product (12% Ti).
   - **Bog iron** (Profile 40): Freshwater wetlands, river floodplains, and poorly drained lowlands (Domain 7); 100% surface exposure; modest supplemental reserve size (28% Fe); documented hook for future deposit regeneration.
3. **Gameplay calibration target achieved**: 
   - Usable iron is broadly accessible: **88.8% of all spawned tribes** (and **92.5% of continental starts**) find reachable iron within **10 traversable land steps** (up from 30.0% before).
   - Median distance to nearest reachable iron is **1.0 hex step** (P90 is 4.0 steps).
   - Genuinely rich BIF deposits remain geographically concentrated and clustered.
   - Iron is substantially easier to obtain regionally than copper (35.0% within 10 steps) and radically easier than tin (8.75% within 10 steps).
   - No landscape, elevation, climate, drainage, or ecology passes were altered; the change is strictly within the resource generation stage.

---

## 1. Ancient History Realignment

### Root Cause
In `GeologicalGeneration.cs`, initial tile setup previously assigned:
```csharp
r.History[t.Id] = (ushort)(GeologicalHistory.Ancient | GeologicalHistory.Preserved | (marine ? GeologicalHistory.MarineFacies : GeologicalHistory.None));
```
Because `Ancient` was applied unconditionally to all tiles, subsequent province history rolls (`Roll < tuning.AncientChance`) were redundant, and oceanic floor, volcanic arcs, and young basins were all treated as ancient Archean/Proterozoic cratonic basement.

### Correction
1. Universal default removed: `GeologicalHistory.Ancient` is no longer applied in the initial tile loop.
2. Cratonic continental terranes: Tiles are assigned `Ancient` during initial generation only when they represent stable continental interior lithosphere:
   - Must be continental crust (`t.Continental && !marine`).
   - Must be outside active plate collision/deformation zones (`c.BoundaryDistance >= 0.045`).
   - Must be volcanically inactive (`t.Volcanism < 0.05`).
   - Keyed spatial terrane noise (`TerrainNoise.Fractal(seed, 12050, x, y, 4, 3) > -0.05`) creates coherent continental craton shields.
3. Province propagation: When geological provinces roll `Ancient` (`tuning.AncientChance = 0.7`), `Ancient` history is only inherited by continental land tiles (`c.World.Tiles[id].Continental && !marine`). Saltwater marine tiles never receive `Ancient` history.
4. Non-regression: All dependent profiles requiring `Ancient` (Primary gold 7, Ancient iron formation 8, Manganese sediment 21, Metamorphic graphite 28, Basement uranium 30) were verified to remain viable and eligible across cratonic terranes without suppression.

---

## 2. The Five Geological Iron Pathways

| Pathway | Profile ID | Host Rocks | Required History | Domain | Shape | Grade (Parts / 10,000) | Geological / Historical Role |
| --- | ---: | --- | --- | ---: | --- | --- | --- |
| **Banded iron formation (BIF)** | 8 | Basement, Metamorphic | `Ancient` | 0 (Land) | Bed | 7000 Fe (70%) | Industrial motherlodes; giant clustered deposits in ancient cratonic shields (Superior/Algoma type). |
| **Sedimentary ironstone** | 37 | Clastic, Carbonate | `Basin` | 0 (Land) | Bed | 3800 Fe (38%) | Oolitic ironstone / minette / siderite beds in sedimentary basins; broad geographic distribution. |
| **Weathered iron ore** | 38 | Mafic, Ultramafic, Basement | `Weathered \| Preserved` | 0 (Land) | Bed | 4500 Fe (45%) | Gossans and lateritic iron caps over iron-rich mafic/basement rocks; excludes felsic/carbonate hosts. |
| **Titaniferous magnetite** | 39 | Mafic, Ultramafic | `Intrusive` | 0 (Land) | Lens | 5200 Fe (52%), 1200 Ti (12%) | Magmatic layered intrusions; regional deposits with titanium co-product. |
| **Bog iron** | 40 | Any | `None` | 7 (Wetlands) | Bed | 2800 Fe (28%) | Lowland alluvial/wetland precipitation; surface-exposed; modest supplemental supply for early metallurgy. |

### Bog Iron and Domain 7
Domain 7 is defined in `GeologicalProfiles.cs` as freshwater wetlands and poorly drained riverine lowlands:
- Excludes marine and arid extremes: `!t.Land || t.Environment == Desert || t.Environment == Ice`.
- Requires wetland hydrological indicators:
  - River edges: `t.River`.
  - Freshwater sources: `t.FreshwaterSource == River || t.FreshwaterSource == Lake`.
  - Drainage channels: `t.Flow > 0.05`.
  - Depression basins / swamps: `t.DepressionDepth > 0`.
  - Moist lowlands: `t.Relief == Relief.Lowland && t.Seasons.Any(s => s.Moisture > 0.45)`.
- **Burial Depth**: Forced to `BurialDepth.Surface` (`Exposure = 10000`) in `GeologicalFootprints.cs`.
- **Reserve Cap**: Rich stock is capped at 20% of standard rich deposits in `GeologicalDeposits.cs`, ensuring it functions as a modest supplemental resource rather than an industrial basin.
- **Renewability Hook**: A documented hook is preserved in `GeologicalDeposits.cs` for future century-scale wetland bog iron regeneration when dynamic mineral replenishment is introduced.

---

## 3. Spawn Resource Access Audit Results

The standard 5-world benchmark was executed using `Measure-Starts.ps1` and `Summarize.py`:
- **400 tribes total**: 80 tribes each across Continents (seeds 11, 42, 1729), Pangaea (seed 42), and Archipelagos (seed 42).
- World dimensions: Standard **1024×512**, start separation 12.
- Production configuration: `MaximumQuantity: 10000000`, `DeepReserveMultiplier: 50`.

### Nearest Occurrence Across All 400 Tribes

| Material | Within 2 land steps | Within 5 land steps | Within 10 land steps | Median steps (reachable) | P90 steps (reachable) | Unreachable (no land route) |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| **Iron** | **77.0% (308/400)** | **84.8% (339/400)** | **88.8% (355/400)** | **1.0** | **4.0** | **32 / 400** |
| **Copper** | 16.5% (66/400) | 24.8% (99/400) | 35.0% (140/400) | 13.0 | 76.0 | 86 / 400 |
| **Zinc** | 12.0% (48/400) | 15.5% (62/400) | 21.2% (85/400) | 28.0 | 81.0 | 89 / 400 |
| **Tin** | 4.0% (16/400) | 6.5% (26/400) | 8.8% (35/400) | 50.0 | 137.8 | 93 / 400 |

### Continental Tribes Breakdown (320 Tribes across Continents & Pangaea)

| World | Within 2 land steps | Within 5 land steps | Within 10 land steps | Unreachable tribes |
| --- | ---: | ---: | ---: | ---: |
| Continents-11 | 66 / 80 (82.5%) | 73 / 80 (91.3%) | 76 / 80 (95.0%) | 4 / 80 |
| Continents-42 | 68 / 80 (85.0%) | 75 / 80 (93.8%) | 75 / 80 (93.8%) | 3 / 80 |
| Continents-1729 | 69 / 80 (86.3%) | 73 / 80 (91.3%) | 74 / 80 (92.5%) | 2 / 80 |
| Pangaea-42 | 62 / 80 (77.5%) | 69 / 80 (86.3%) | 71 / 80 (88.8%) | 6 / 80 |
| **Total Continental** | **265 / 320 (82.8%)** | **290 / 320 (90.6%)** | **296 / 320 (92.5%)** | **15 / 320 (4.7%)** |

In Archipelagos-42 (where landmasses are fragmented into island chains), **59 / 80 (73.8%)** of tribes reach iron within 10 land steps, with the remaining 17 tribes located on small isolated islands.

### Traces vs Substantial Reserves (Units Accessible at Nearest Tile)

| Material | Threshold ≥0 units | Threshold ≥10 units | Threshold ≥100 units | Nearest Rich Deposit Median |
| --- | ---: | ---: | ---: | ---: |
| **Iron** | 355 / 400 (88.8%) | 354 / 400 (88.5%) | 296 / 400 (74.0%) | **7.0 steps** |
| **Copper** | 140 / 400 (35.0%) | 134 / 400 (33.5%) | 62 / 400 (15.5%) | 34.0 steps |
| **Zinc** | 85 / 400 (21.2%) | 85 / 400 (21.2%) | 51 / 400 (12.8%) | 58.0 steps |
| **Tin** | 35 / 400 (8.8%) | 35 / 400 (8.8%) | 26 / 400 (6.5%) | 60.0 steps |

---

## 4. Verification and Determinism

- **Headless Assertions**: 7,863 geology assertions pass with 0 errors.
- **Replay and Determinism**: Tested across scalar, 2-worker, and 4-worker resolutions; exact save round trip verified across all shapes and seeds.
- **Fingerprint Preservation**: `SaveCodec.LegacyFingerprint` is completely identical before and after resource injection, confirming zero leakage or changes to core terrain, ecology, entities, or gameplay state.
