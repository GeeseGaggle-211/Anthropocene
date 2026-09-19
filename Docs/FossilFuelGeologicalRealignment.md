# Fossil Fuel (Coal & Petroleum) Geological Realignment — September 18, 2026

## Executive Summary

Anthropocene strategy simulations previously generated **Coal** and **Petroleum** in quantities that were severely disjoint from Earth history and geological reality:
- **Coal (Profile 13)** was shockingly sparse: generating only **1 to 4 deposit bodies per 1024×512 world** (mean: **2.5 bodies** globally).
- **Petroleum (Profile 14)** generated only **10 to 26 bodies per world** (mean: **18.8 bodies**, ~50% offshore in abyssal/shelf waters), but these appeared as isolated singletons rather than authentic, prolific oil provinces.

This update resolves both issues within the authoritative geological generation pipeline on `feature/geologic-ancient`:
1. **Sedimentary Mountain Foreland Synergy**: Uplifted ancient mountain ranges (`AncientOrogenStrength`) and convergent tectonic boundaries shed massive wedges of eroded clastic sediment (molasse/flysch) into flanking flexural foreland basins, lowlands, and river floodplains (`HostRock.Clastic`, `GeologicalHistory.Basin`), accumulating extensive deltaic peat mires (`Peat`) that mature under burial overburden (`Mature`).
2. **Coal Pathways & Common Occurrence**:
   - **Bituminous coal seam (Profile 13)**: Deep, thermally matured high-grade coal seams (`Basin | Peat | Mature`, `DepositShape.Seam`).
   - **Paralic coal basin (Profile 41)**: Broad stratiform brown coal and lignite beds (`Basin | Peat`, `DepositShape.Bed`) in deltaic/coastal wetlands without requiring deep geothermal maturation.
   - **Scattered outcroppings**: Enabled for Profiles 13 and 41 (`HasScatteredOccurrences`), creating surface-exposed outcroppings (`DepositShape.Field`) across coal basins.
   - **Empirical result**: Global coal bodies increased from **2.5** to **47.6 bodies per world** (~19× increase), making coal relatively common and balanced between major seams/beds (25.5) and surface outcroppings (22.1).
3. **Petroleum Systems & Concentrated Fairways (Ideas A & B)**:
   - **Idea A (Foreland Fold-and-Thrust Fairways & Depositional Coupling)**: In sedimentary basins, source-rock thermal maturation (`Source & Mature`) and compressional/extensional deformation (`Reservoir & SealTrap`) are geologically coupled into active **Petroleum Fairways**. Candidate traps inside an active fairway roll with a calibrated multiplier (`PetroleumChanceMultiplier = 2.5`), generating **dense clusters of 4–10 fields** across each province (e.g. Persian Gulf, Alberta, Maracaibo, Timan-Pechora style).
   - **Idea B (Two Complementary Profiles)**:
     - **Deep structural petroleum system (Profile 14)**: Conventional high-pressure anticlinal and salt-dome reservoirs with light oil and rich natural gas (`DepositShape.Reservoir`, `Domain = 1`). Globals increased from 18.8 to **62.4 bodies per world**, strongly clustered in fairways.
     - **Stratigraphic petroleum & surface seeps (Profile 42)**: Shallow up-dip migration belts, heavy oil/tar sands, bitumen beds, and natural surface asphalt seeps (`Basin | Source | Preserved`, `DepositShape.Bed`, `Domain = 1`). Yields **82.8 bodies per world** (~50% onshore, ~50% continental shelves/slopes), including 26.2 surface-exposed seeps (`BurialDepth.Surface`, `Exposure = 10000`) representing historic pitch lakes and oil springs (e.g. Hit, Baku, La Brea).

---

## 1. Geological Pathways Matrix

| Pathway | Profile ID | Host Rocks | Required History | Domain | Shape | Grade (Parts / 10,000) | Geological / Historical Role |
| --- | ---: | --- | --- | ---: | --- | --- | --- |
| **Bituminous coal seam** | 13 | Clastic | `Basin \| Peat \| Mature` | 0 (Land) | Seam | 9000 Coal, 300 S | High-rank bituminous/anthracite coal seams; deep thermal maturation under foreland burden. |
| **Paralic coal basin** | 41 | Clastic | `Basin \| Peat` | 0 (Land) | Bed | 8500 Coal, 1000 Clay, 200 S | Broad sub-bituminous & lignite beds in deltaic floodplains and coastal wetlands; associated underclay. |
| **Deep structural petroleum** | 14 | Clastic, Carbonate | `Basin \| Source \| Mature \| Reservoir \| SealTrap \| Preserved` | 1 (Land & Shelf) | Reservoir | Variable (10–90% Oil, 10–90% Gas), 200 S | Giant conventional reservoirs; anticlinal folds & fault closures in active petroleum fairways. |
| **Stratigraphic petroleum & seeps** | 42 | Clastic, Carbonate | `Basin \| Source \| Preserved` | 1 (Land & Shelf) | Bed | 8000 Petroleum, 1000 Gas, 300 S | Heavy oil, bitumen, oil sands, and surface asphalt springs; scattered occurrences are 100% surface-exposed. |

---

## 2. Multi-Seed Empirical Verification

Deposit body distributions measured across 8 standard continental worlds (**1024×512**, 80 tribes):

| Seed | Coal Seams (13) | Paralic Beds (41) | **Total Coal** | Petro Giants (14) | Petro Seeps/Beds (42) | **Total Petro** | Total Deposits |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| **1729** | 13 | 13 | **26** | 71 | 85 | **156** | 1,335 |
| **42** | 31 | 32 | **63** | 64 | 96 | **160** | 1,872 |
| **11** | 24 | 37 | **61** | 59 | 82 | **141** | 1,864 |
| **1** | 13 | 21 | **34** | 46 | 58 | **104** | 1,533 |
| **2** | 19 | 21 | **40** | 67 | 78 | **145** | 1,442 |
| **3** | 6 | 20 | **26** | 48 | 61 | **109** | 1,503 |
| **4** | 19 | 29 | **48** | 77 | 98 | **175** | 1,507 |
| **5** | 32 | 51 | **83** | 67 | 104 | **171** | 1,744 |
| **Mean** | **19.6** | **28.0** | **47.6** | **62.4** | **82.8** | **145.1** | **1,600** |

### Breakdown: Conventional Deposits vs Scattered Occurrences

Across Seeds 1729, 42, 11, 1:
- **Coal**: 50.5% Rich Conventional Deposits (mean 25.5 bodies), 49.5% Scattered Surface Outcroppings (mean 20.5 bodies).
- **Petroleum 14**: 100% Deep High-Pressure Reservoirs (mean 60.0 bodies), 0 scattered.
- **Petroleum 42**: 69.8% Stratigraphic Heavy Oil Beds (mean 54.0 bodies), 30.2% Surface Asphalt/Pitch Seeps (mean 26.2 bodies).
- **Petroleum 42 Marine Distribution**: 49.4% Onshore Land, 50.6% Offshore Continental Shelves/Slopes.

---

## 3. Test Suite & Build Verification

- **Headless Assertions**: `PASS: 9524 geology assertions` (including exact profile specifications, host rejection invariants, surface exposure contracts, and save continuation).
- **Multi-Shape Small World Audit**: 18 worlds (Continents, Archipelagos, Pangaea across seeds 0, 1, 5, 42, 1729, 31415) all verified for 100% connected deposit bodies and compact memory footprint (~0.12–0.22 MiB).
- **Unity EditMode Tests**: 26/26 passed with 0 failures (`Test results: passed=26 failed=0 skipped=0 total=26`).
- **Unity Windows Player Build**: Compiled cleanly (`Anthropocene.exe`, `Anthropocene.Simulation.dll`, `Anthropocene.Runtime.dll`), copied to `Builds/Windows/`.
- **Standalone Player Smoke Test**: Verified across 37 materials, total and accessible overlays, selector, and normal fog (78 screenshot captures, 0 errors).
