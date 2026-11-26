# Paid physical Sanitation (Irrigation-unlocked)

This implements the approved Sanitation specification on stacked branch
`features/sanitation` (based on `features/plough-elder-irrigation`, commit
`70aae77`). The feature is opt-in via `AgriculturalTechnologySettings.SanitationEnabled`
(enabled in `Assets/_Anthropocene/Resources/AgriculturalTechnologyOpeningConfig.json`,
defaulting `false` on legacy saves and untouched in `OpeningConfig.json` and
`SmoothCalendarOpeningConfig.json`). Active RL training, action schemas,
observation schemas, crowding mortality, `QoL`, `SocialCohesion`, and `Fission`
thresholds remain unchanged.

## Approved specification & Engineering Doctrine

| Doctrine Dimension | Design & Implementation |
| --- | --- |
| **Authoritative State** | Sparse site-bound `List<FixedSanitationStock> WorldState.FixedSanitation` (`CampId`, `Tile`, `Units`, `LastUpdatedDays`) mirroring `FixedHousingStock`, backed by an immutable lock-free transient `FixedSanitationIndex` (`O(1)` site lookup; invalidated on direct edits via `FixedSanitationChanged`). Zero dense per-tile state. `[OptionalField]` `Camp.HasSanitationWaterRatio` / `Camp.SanitationWaterRatio` record turn delivery ratios. |
| **Temporal Abstraction (`dt` Consistency)** | Continuous exponential depreciation `U(t1) = U(t0) * (1 - d)^(dt / ReferenceTurnYears)` over physical years (`10%` per 20-year reference turn, invariant across `1 d`, `1 yr`, `20 yr` subdivisions). Inside `PhysicalDemography`, sanitation coverage is evaluated continuously as `s(N) = clamp(S_cap / N, 0, 1)` at instantaneous density `N(t)` with an explicit kink knot `ln(S_cap)` in `Band`. |
| **Monotonicity & Conservation** | Since `s(N)` is non-increasing in `N`, `m_d(N) = max(0, 1 - alpha_s * s(N))` is non-decreasing in `N`, preserving strict monotonicity of `R(ln N)` and one unique positive equilibrium `N*`. Exact cause-specific death conservation (`N1 - N0 = B - sum(D_j)`) and freshwater source conservation (`Start + Recharge - Harvest - Overflow = End`) hold across arbitrary `dt`. |
| **Computational Scaling** | `O(active villages with Sanitation)` per turn: `O(1)` indexed lookup, `O(1)` arithmetic in `RatesAtDensityCoverage` / `FastNet`, `CoupledStateStride = 10` preserving `TryExactScalarReduction`, and at most `+2` `PlanningBudget` evaluations per Sanitation-capable village in `DemandControl` (no 3D grid blowup). `O(1)` in map size, population, and `dt`. |
| **Save & RL Compatibility** | `ANTHROPOCENE-11` / `12` envelopes unchanged; `CraftGood.Sanitation = 13` appended at the end of `CraftGood`. `[OnDeserializing] InitializeSanitationDefaults` populates valid numeric defaults with `SanitationEnabled = false` on legacy/PR #49 saves without altering `ValleyMaxSlopeDegrees` or `ValleyBankWidthMeters`. No new `OrderKind` or observation layout change. |
| **Validation Strategy** | Dedicated `SanitationChecks` (417 assertions), full 71-suite headless run, `SmoothCalendar` (28,787 checks), `Check-CurrentContract.py`, sparse `--benchmark` (`1–320` villages), and 5-world matched ablation (`Seeds 11, 23, 47, 101, 1729`, 40 runs + `--labor-audit` exact-replay verification). |

## Quantitative demographic grounding

In `PhysicalDemography.RatesAtDensityCoverage` (`Assets/_Anthropocene/Simulation/Demography.cs`),
full nutrition (`F = 1`), clothing (`C = 1`), and fixed housing (`H = 1`) yield
an exact baseline equilibrium at `N* = 1,000.0` with a 75:25 Crowding:Disease
density-mortality split (`CrowdingMortalityRateAt1000 = 0.031308 / yr`,
`DiseaseMortalityRateAt1000 = 0.010436 / yr`).

Sanitation acts strictly on the `PhysicalMortalityV1` disease hazard via
`effectiveDiseaseMit = max(0, diseaseMitigation * max(0, 1 - alpha_s * s(N)))`
with `CrowdingMitigation = 1.0`, `QoL`, `SocialCohesion`, `QoLMortality`, and
`Fission` thresholds untouched:

| Sanitation Disease Reduction (`alpha_s`) | Effective `m_d` (Full Coverage) | Exact Equilibrium `N*` | Lift vs. `1,000` |
| ---: | ---: | ---: | ---: |
| `0%` (baseline) | `1.00` | `1,000.0` | `+0.0%` |
| `25%` | `0.75` | `1,066.2` | `+6.6%` |
| **`50%` (Approved default)** | **`0.50`** | **`1,141.8`** (`1,141.845454`) | **`+14.2%`** |
| `75%` | `0.25` | `1,229.2` | `+22.9%` |
| `100%` (theoretical limit) | `0.00` | `1,331.1` | `+33.1%` |

Coupled settlements (`CoupledDemography.cs`) supply `EffectiveSanitationCapacity`
to the home-camp service pool and `0` to detached expeditions, reducing exactly
to scalar `AdvanceBudget` whenever pools are proportional. `AverageSanitationCoverage`
is recorded on `DemographicResult` / `DemographicReport`, and `SanitationPeople`
is recorded on `SettlementWelfareReport`. `PhysicalDemography.DiseaseMitigation(world, camp[, pop])`
feeds `ProjectedSettlementRates` and `SocialRelocation` (`UtilityUpperBound` and
`Forecast` on `state.View`, keeping fission daughters at `1.0` and `--bound-audit` sound).

## Physical infrastructure, freshwater coupling, Elder planner & UI

- **Unlock & site binding:** `AgriculturalTechnology.Sanitation(w, c)` is active
  when `w.Config.Agriculture.SanitationEnabled` is true, `c.IsVillage` is true,
  and `c.People.HasTech(NeolithicTech.Irrigation)`. Installed `FixedSanitationStock`
  is bound to `(CampId, Tile)`: relocating a camp leaves stock behind on the old
  tile (where it depreciates naturally), and fission daughters start at `0`.
- **Production, tools & storage (`CraftGood.Sanitation = 13`):** Built by
  `WorkforceRoles.Builder` (`NeolithicTech.Construction`, `SpecialistOnly = true`)
  using `HousingMaterial` and shared `ToolCoverageBudget` (`IsHousingWork = true`,
  so Hand / Improved / Copper Tools apply identically to fixed Housing). Default
  parameters on `AgriculturalTechnologySettings` (~20% of fixed Housing's
  per-person cost):
  - `SanitationDiseaseReduction = 0.50`
  - `SanitationPeoplePerUnit = 20`
  - `SanitationHousingMaterialsPerUnit = 1.0`
  - `SanitationBuilderOutputPerBaseDays = 0.03125`
  - `SanitationDecayPerReferenceTurn = 0.10`
  - `SanitationWaterPerPersonYear = 0.15`
  Installed sanitation writes directly onto `FixedSanitationStock` (exempt from
  Pot `GoodsStorage`; queued `HousingMaterial` is exempted via `GoodsStorage.PlannedUse`).
  `OwnDemand(CraftGood.Sanitation)` and `SharedHousingMaterialConsumer` both
  require a finite ceiling-bearing `ProductionOrder` (matching
  `CanalConstruction` / `CanalMaintenance`), preserving exact
  `SocialRelocationForecastCache` and manual-turn parity.
- **Conserved freshwater coupling:** In `TurnHorizonResources.CommitHorizonResources`,
  installed sanitation serving `min(camp.Population, installedSanitationCapacity)`
  emits `"Sanitation"` claims (`servedPeople * 0.15 * years`) on
  `FreshwaterAccess.Source(w, c.Tile)` alongside `"Drinking"` claims before
  `IrrigationEconomy.Allocate` (protected ahead of irrigation residual). Realized
  delivery sets `HasSanitationWaterRatio` / `SanitationWaterRatio` (falling back
  to `camp.WaterNeedsMetRatio` when no claim was submitted), and
  `EffectiveSanitationCapacity = InstalledSanitationCapacity * waterRatio`.
- **Elder planner (`DemandControl.cs`):**
  - `ServiceClass.Continuity` replaces continuous depreciation up to current
    residents whenever `sanWaterScale > Epsilon` (dry villages with zero water
    do not spend Builder labor or `HousingMaterial` maintaining unusable stock).
  - `ServiceClass.Development` runs at most `+2` `PlanningBudget` evaluations
    (`addFull` and `0.5 * addFull`) after the winning `clothing x shelter` split
    targeting `max(residents, projectedEndPopulation)` bounded by remaining
    Builder labor and materials, committing expansion only when net person-years
    strictly increase.
- **UI readout (`OpeningVillageInspector.cs`, `IrrigationEconomy.Status`):**
  When Sanitation is unlocked, the village inspector displays installed units,
  capacity, effective coverage, and active disease reduction (with scroll heights
  hoisted above `BeginScrollView`).

## Sparse scaling benchmark (`--benchmark`)

Measured with `DOTNET_TieredCompilation=0` (20 repetitions per sample,
`Tools/AgriculturalTechnologySweep --benchmark`). Water/ecology runs
`SanitationClaims` + `IrrigationEconomy.Allocate` + `EcologicalPressure.Integrate`
across `population in {100, 1,000,000}` and `years in {1/365, 20, 600}`;
the Sanitation kernel runs `EffectiveSanitationCapacity` + `PlanningBudget`
across `sanitationPopulation in {100, 1,200}` and `sanitationYears in {1/365, 1, 20}`:

| Active villages | Water & Ecology time range | Water & Ecology allocated bytes | Sanitation kernel time range | Sanitation kernel allocated bytes |
| ---: | ---: | ---: | ---: | ---: |
| `1` | `0.022–0.045 ms` | `7,586 B` | `0.0029–0.0144 ms` | `914 B` |
| `16` | `0.249–0.337 ms` | `69,138 B` | `0.0535–0.2439 ms` | `14,594 B` |
| `80` | `1.060–1.366 ms` | `338,602 B` | `0.2034–1.1689 ms` | `72,962 B` |
| `320` | `3.827–5.542 ms` | `1,365,514 B` | `0.8248–4.7853 ms` | `291,842 B` |

Allocations are strictly linear in active villages (`912 B/village + 2 B` for
the Sanitation kernel; `~4.27 KB/village` for combined water/ecology) and
completely invariant across settlement population and turn duration `dt`.

## Five-world matched ablation (`Seeds 11, 23, 47, 101, 1729`)

- **Design:** Native `96x48`, 4-player worlds run for 600 organic years with
  `SanitationEnabled = false` so all treatments fork from identical mature
  checkpoints (5 seeds × 4 treatments × 2 staffing regimes = **40 matched runs**,
  each `603.341813` smoothed physical years; calendar SHA-256
  `a273aba70ca51ae38676015d4cd1e036064552f6d21634f9d20e458070bdc77f`).
- **Verification:** `Summarize.py` confirmed exact paired start/end/duration/season
  vectors, checkpoint/source hashes, and conserved crop/wild food attribution;
  `--labor-audit` replayed all **40/40** runs with `exact-replay=pass`.
- **Non-regression parity:** Both `none` and `combined-soil` reproduce PR #49
  (`b106f8f`) exactly (`combined-soil` Elder food `-2.864%`, work/food `-1.872%`,
  total pop `91,646`, `p25/med/p75 = 648.0 / 996.0 / 1000.0`; fixed-field food
  `-0.510%`, work/food `-2.760%`).

### Pooled 5-world results — Elder endogenous staffing (`fixedFieldStaffing = false`)

| Treatment | Total pop (camps) | Settlement pop (`p25` / `med` / `p75` / `mean` / `max`) | Camps `>1,000` | Disease deaths | Disease deaths averted (vs `none` / vs `combined-soil`) | Active San. units / mean cov. | San. WY / HM | Plant food Δ / Work-per-food Δ | Canals / Water |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `none` | `96,186` (`119`) | `643.0` / `997.0` / `1000.0` / `808.3` / `1,055` | `4` | `485,830.0` | `+0.0` / `−13,907.1` | `0.0` / `0.000` | `0.0` / `0.0` | `+0.000%` / `+0.000%` | `37` / `0` |
| `combined-soil` | `91,646` (`113`) | `648.0` / `996.0` / `1000.0` / `811.0` / `1,010` | `3` | `471,923.0` | `+13,907.1` / `+0.0` | `0.0` / `0.000` | `0.0` / `0.0` | `−2.864%` / `−1.872%` | `41` / `1,193,316` |
| `sanitation` | `95,084` (`114`) | `649.3` / `1000.5` / `1023.0` / `834.1` / `1,119` | **`57`** | `427,493.7` | **`+58,336.4`** / `+44,429.3` | `4,705.1` / `0.284` | `89,102.8` / `16,706.8` | `−1.129%` / `+1.707%` | `37` / `0` |
| `combined-soil-sanitation` | `95,887` (`113`) | **`691.0`** / **`1000.0`** / **`1022.0`** / **`848.6`** / `1,095` | **`55`** | `435,893.5` | **`+49,936.5`** / **`+36,029.4`** | `4,824.6` / `0.273` | `90,342.0` / `16,939.1` | **`+0.248%`** / **`−0.022%`** | `41` / `1,388,774` |

### Pooled 5-world results — Fixed-field staffing (`fixedFieldStaffing = true`)

| Treatment | Total pop (camps) | Settlement pop (`p25` / `med` / `p75` / `mean` / `max`) | Camps `>1,000` | Disease deaths | Disease deaths averted (vs `none` / vs `combined-soil`) | Active San. units / mean cov. | San. WY / HM | Plant food Δ / Work-per-food Δ | Canals / Water |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `none` | `94,909` (`119`) | `651.5` / `991.0` / `1000.0` / `797.6` / `1,150` | `6` | `475,859.6` | `+0.0` / `−1,457.8` | `0.0` / `0.000` | `0.0` / `0.0` | `+0.000%` / `+0.000%` | `37` / `0` |
| `combined-soil` | `93,469` (`119`) | `596.5` / `990.0` / `1000.0` / `785.5` / `1,142` | `6` | `474,401.7` | `+1,457.8` / `+0.0` | `0.0` / `0.000` | `0.0` / `0.0` | `−0.510%` / `−2.760%` | `43` / `1,154,952` |
| `sanitation` | `99,595` (`125`) | `565.0` / `1000.0` / `1023.0` / `796.8` / `1,188` | **`55`** | `429,017.4` | **`+46,842.2`** / `+45,384.3` | `4,787.9` / `0.269` | `87,652.8` / `16,434.9` | `+1.272%` / `+1.567%` | `37` / `0` |
| `combined-soil-sanitation` | `95,514` (`116`) | **`625.5`** / **`1000.0`** / **`1022.3`** / **`823.4`** / `1,174` | **`55`** | `427,291.1` | **`+48,568.5`** / **`+47,110.7`** | `4,644.9` / `0.265` | `87,187.6` / `16,347.7` | `+0.250%` / `−1.556%` | `40` / `1,305,161` |

### Per-seed breakdown (Elder endogenous staffing)

| Seed | Treatment | Camps | Total pop | `p25` / `med` / `p75` / `mean` / `max` | `>1,000` | Disease deaths (averted vs `none`) | Active San. units (`mean cov.`) |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| `11` | `none` / `combined-soil` | `26` / `22` | `25,222` / `21,346` | `995/1000/1000/970.1/1001` vs `996/1000/1000/970.3/1000` | `1` / `0` | `124,611.4` / `114,375.8` (`+10,235.6`) | `0.0` / `0.0` |
| `11` | `sanitation` / `combined-soil-sanitation` | `23` / `22` | `22,287` / `22,091` | `990/1019/1023/969.0/1092` vs `987/1020/1023/1004.1/1092` | `15` / `15` | `99,157.2` (`+25,454.1`) / `101,447.7` (`+23,163.7`) | `1,060.3` (`0.283`) / `1,115.2` (`0.276`) |
| `23` | `none` / `combined-soil` | `15` / `15` | `12,144` / `12,352` | `696/966/1000/809.6/1009` vs `763/958/1000/823.5/1010` | `1` / `2` | `60,719.5` / `61,167.1` (`−447.6`) | `0.0` / `0.0` |
| `23` | `sanitation` / `combined-soil-sanitation` | `14` / `16` | `11,918` / `14,099` | `745/1008/1021/851.3/1070` vs `807/1017/1022/881.2/1070` | `8` / `9` | `54,584.7` (`+6,134.8`) / `58,521.3` (`+2,198.1`) | `593.5` (`0.261`) / `704.4` (`0.250`) |
| `47` | `none` / `combined-soil` | `21` / `21` | `16,093` / `16,071` | `592/854/1000/766.3/1055` vs `583/863/1000/765.3/1005` | `1` / `1` | `82,491.1` / `82,858.1` (`−367.0`) | `0.0` / `0.0` |
| `47` | `sanitation` / `combined-soil-sanitation` | `22` / `21` | `17,267` / `16,367` | `593/877/1023/784.9/1119` vs `576/913/1022/779.4/1069` | `8` / `6` | `77,365.8` (`+5,125.3`) / `77,358.3` (`+5,132.8`) | `892.5` (`0.262`) / `853.2` (`0.241`) |
| `101` | `none` / `combined-soil` | `46` / `44` | `33,731` / `32,786` | `443/968/1000/733.3/1003` vs `478/978/1000/745.1/1000` | `1` / `0` | `168,088.3` / `163,320.6` (`+4,767.8`) | `0.0` / `0.0` |
| `101` | `sanitation` / `combined-soil-sanitation` | `44` / `43` | `34,328` / `34,065` | `496/1000/1022/780.2/1095` vs `513/1000/1022/792.2/1095` | `20` / `19` | `151,045.2` (`+17,043.1`) / `152,984.5` (`+15,103.9`) | `1,694.3` (`0.310`) / `1,684.8` (`0.300`) |
| `1729` | `none` / `combined-soil` | `11` / `11` | `8,996` / `9,091` | `668/1000/1000/817.8/1000` vs `719/1000/1000/826.5/1000` | `0` / `0` | `49,919.7` / `50,201.4` (`−281.6`) | `0.0` / `0.0` |
| `1729` | `sanitation` / `combined-soil-sanitation` | `11` / `11` | `9,284` / `9,265` | `698/1022/1023/844.0/1090` vs `676/1022/1023/842.3/1078` | `6` / `6` | `45,340.7` (`+4,579.1`) / `45,581.7` (`+4,338.0`) | `464.5` (`0.305`) / `467.1` (`0.298`) |

### Key findings from the ablation

1. **Consistent break above the `1,000` ceiling across all 5 worlds:** Settlements
   above `1,000` residents rise from `4` (`none`) / `3` (`combined-soil`) to `57`
   (`sanitation`) / `55` (`combined-soil-sanitation`) in the Elder regime (`6/6`
   to `55/55` in fixed-field), with pooled `p75` rising from `1,000` to `1,023`
   and water-rich sites reaching `1,095–1,119` (and `1,174–1,188` in fixed-field).
2. **Physical freshwater throttling works as designed:** Although villages install
   `4,705–4,825` active units (`94–96k` installed person-capacity across `~95–96k`
   residents), the conserved `0.15`/person-year freshwater claim naturally throttles
   effective coverage (`meanSanitationCoverage = 0.273–0.284` pooled over 600 yr)
   on modest freshwater sources while letting river-rich sites approach `N* = 1,141.8`.
3. **Substantial disease-mortality reduction:** `sanitation` averts **58,336.4**
   disease deaths vs. `none` (`−12.0%`), and `combined-soil-sanitation` averts
   **49,936.5** vs. `none` (`36,029.4` vs. `combined-soil`) in the Elder regime
   (`46,842.2` and `48,568.5` in fixed-field).
4. **Genuine synergy with Plough and Irrigation:** Combining Sanitation with Plough
   and Irrigation lifts Elder mean settlement population to **`848.6`** (`+40.3` vs.
   `none`, `+37.5` vs. `combined-soil`), lifts `p25` by **`+43`** (`648 → 691`),
   reverses `combined-soil`'s endogenous plant-food drop (`−2.864% → +0.248%`), and
   increases delivered irrigation water by **`+16.4%`** (`1,193,316 → 1,388,774`)
   as larger villages absorb freed Plough labor and river-valley food capacity.

## Reproduction

Run from the repository root with .NET 8:

```sh
dotnet build -c Release Tools/Headless/Anthropocene.Headless.csproj
dotnet build -c Release Tools/SmoothCalendar/Anthropocene.SmoothCalendar.csproj
dotnet build -c Release Tools/AgriculturalTechnologySweep/Anthropocene.AgriculturalTechnologySweep.csproj

dotnet run --no-build -c Release --project Tools/Headless/Anthropocene.Headless.csproj -- --only=SanitationChecks,AgriculturalTechnologyChecks,HousingMaterialRecipeChecks,DemandControlChecks,GoodsStorageChecks,DemographyChecks,FissionChecks,ImmigrationChecks,SocialRelocationChecks,SaveCodecChecks --bound-audit --jobs=1
dotnet run --no-build -c Release --project Tools/Headless/Anthropocene.Headless.csproj -- 0
dotnet run --no-build -c Release --project Tools/SmoothCalendar/Anthropocene.SmoothCalendar.csproj

DOTNET_TieredCompilation=0 dotnet run --no-build -c Release --project Tools/AgriculturalTechnologySweep/Anthropocene.AgriculturalTechnologySweep.csproj -- --benchmark
dotnet run --no-build -c Release --project Tools/AgriculturalTechnologySweep/Anthropocene.AgriculturalTechnologySweep.csproj -- --treatments=none,combined-soil,sanitation,combined-soil-sanitation
dotnet run --no-build -c Release --project Tools/AgriculturalTechnologySweep/Anthropocene.AgriculturalTechnologySweep.csproj -- --labor-audit
python3 Tools/AgriculturalTechnologySweep/Summarize.py
python3 Tools/Docs/Check-CurrentContract.py
```

Unity Editor interactive UI acceptance (`OpeningVillageInspector.cs`) remains
pending because no Unity 6000.5.10f1 installation is available in this headless
Linux environment.
