# Neolithic–Chalcolithic Civic & Governance System

**Status:** Normative Specification; implemented through M4 (Phase 2 sanitation) on `features/civic-governance`. Opt-in: `SimulationConfig.Civic == null` by default, so existing saves and profiles are unchanged. See §6 for implementation status and measured gates.  
**Target Base Branch:** `features/plough-elder-irrigation` (PR #49, commit `70aae775bfe0ecc1b77ece372bccb3a6031b4eb1`, stacked on PR #48 `features/smooth-calendar`)  
**Feature Branch:** `features/civic-governance`  
**Contract Compliance:** `AGENTS.md` (Anthropocene Engineering Doctrine), `Docs/DemographyQoLContract-v1.1.md`, `Docs/TurnResolutionPasses.md`, `Docs/CurrentSimulationContract.md`

---

## 1. Problem Statement & Design Overview

In PR #49 (`features/plough-elder-irrigation`), irrigated and ploughed Neolithic villages generate substantial food surplus (operating Farmers fall to ~26–33% of workforce), yet populations stall near **~1,000 residents** (80-tribe long runs: P75 = 1,000, max ~1,166–1,176) because two coupled crowding locks remain unmitigated:

1. **Physical Demography** (`Demography.cs:71–75, 111–129`; `Docs/DemographyQoLContract-v1.1.md`):
   $$d(N) = d_{\text{exposure}} + M_c\,c_{1000}\,\Phi(N; k_c) + M_d\,d_{1000}\,\Phi(N; k_d) + d_{\text{starvation}} + d_{\text{additional}}, \qquad \Phi(N; k) = \frac{\operatorname{expm1}(k N)}{\operatorname{expm1}(1000\,k)}$$
   At $N = 1{,}000$ with full food/clothing/housing ($M_c = M_d = 1$), $b_0 = 0.045\text{ yr}^{-1}$, $d_{\text{exposure}} = 0.1\,e_0 = 0.0032555\text{ yr}^{-1}$, crowding causes **69.6%** ($0.031308\text{ yr}^{-1}$) and disease **23.2%** ($0.010436\text{ yr}^{-1}$) of all deaths. Mitigating $M_c$ alone hits a natural **disease ceiling near ~3,000–3,400 residents** (Çatalhöyük-scale dense town); breaking through to **5,000–10,000+ residents** (Trypillia mega-sites / Late Uruk proto-cities) also requires mitigating $M_d$ through urban sanitation and drainage.
2. **Social Cohesion & Relocation** (`NeolithicProduction.cs:1156–1168`, `Fission.cs:619–680`, `Docs/SocialCohesion.md`):
   $$S(N) = \exp\!\left(-(N / N_s)^2\right), \qquad N_{s,0} = N_{\text{band}}(1 + k H) \le 1{,}782$$
   Above ~1,000 residents, $S(N)$ drops sharply ($0.730 \to 0.284$ at $2{,}000$), crushing composite QoL and driving crowding fission and emigration.

### Architecture Summary
- **Extensible Continuous 2D Institutional Grid** on every `Camp`:
  - **Complexity $C \in [0, C_{\max}] \subseteq [0, 1]$**: institutional scale, capped by practice-driven `Society` precedents (Tier 1 $\to$ Tier 2 $\to$ Tier 3A).
  - **Power Distribution $P \in [-1, +1]$**: continuous spectrum from **Corporate / Egalitarian** ($P \to -1$, e.g. Trypillia mega-sites / neighborhood assemblies) through **Hybrid / Council-Oikos** ($P \approx 0$) to **Stratified / Centralized** ($P \to +1$, e.g. Ubaid/Uruk temple-granary cities).
- **Environmental Attractor + Continuous Policy Steering + Alignment Friction**: Four empirically grounded $O(\text{local})$ drivers compute $(C_{\text{env}}, P_{\text{env}})$ from committed simulation state (zero hidden terrain tags). Three levers (`FissionPolicy`, new `ImmigrationPolicy`, new `CivicFocus`) steer $P^*$. Coordinates relax analytically over arbitrary elapsed physical years $\Delta t$. Fighting one's environment is possible, never free: mismatch $|P - P_{\text{env}}|$ increases Steward/upkeep demand and creates open-frontier exit friction.
- **Physical Workforce & Capital Economy**: One unified `WorkforceRoles.Steward` role (adaptive UI label: *Council Arbiter* / *Civic Steward* / *Temple Scribe*) plus sparse fixed `CivicCapital` (Phase 1) and `SanitationCapital` (Phase 2) built and maintained via the generalized PR #49 `Builder` + `HousingMaterial` + generic `Tools` pipeline.
- **Survival-First `DemandControl`**: `Protected` (survival food + kernel-scored clothing/shelter) is untouched; core Stewards and existing capital upkeep sit in `Continuity`; expansion competes in `Development` on a physically comparable `food / worker-year` `PriorityScore`.
- **Two-Phase Delivery, Single Unified Spec**:
  - **Phase 0 (M1)**: Plumb $(M_c, M_d, f, a)$ through `PhysicalDemography` and `CoupledDemography`, and fix `SocialRelocation` counterfactual `state.Origin -> state.View`.
  - **Phase 1 (M2–M3)**: Civic Institutions ($M_c + N_s$, target ~1,000 $\to$ ~3,000–3,400), continuous $P$ trade-offs, precedents (Tier 1, 2, 3A), orders/observations/UI.
  - **Phase 2 (M4)**: Civic Public Works & Sanitation (Tier 3B, `SanitationCapital`, conserved multi-tile freshwater priority, $M_d$, target ~3,400 $\to$ ~5,000–10,000+).

---

## 2. Anthropocene Engineering Doctrine Audit (8-Point Compliance)

| # | Doctrine Principle | Civic & Governance Architecture Guarantee |
| --- | --- | --- |
| **1** | **Design the abstraction first** | Turn boundaries are strictly authoritative. Institutional drift $(C, P)$, `RecentTradeVolume` EMA, and `CivicCapital`/`SanitationCapital` decay & maintenance use closed-form analytical exponentials over elapsed physical years $\Delta t$. Zero sub-turn ticks or intermediate authoritative clocks. |
| **2** | **Treat performance as correctness** | **Scaling law:** Strictly $O(\text{active camps})$ per turn (constant work per camp: $\le 7$ radius-1 tiles, $\le 42$ exit-ring static tiles cached over $\le 91$ local tiles, $O(\lvert\text{Contacts}\rvert)$ bounded local contacts, constant 3 Continuity + 3 Development requests in `DemandControl`, $\le 7$ radius-1 freshwater tiles for Sanitation). **Mature-empire target:** $< 1.5\text{ ms/turn}$ added overhead at 320 tribes / 16,000+ aggregate residents; zero per-person loops; zero $O(\text{map size})$ scans. |
| **3** | **Make cost follow active state** | Zero $O(\Delta t / \text{timestep})$ work. Inactive tiles are never touched or materialized. Pre-civic camps ($C = 0, K = 0$) skip upkeep, Steward staffing, and sanitation water allocation in $O(1)$. |
| **4** | **Prefer sparse, local computation** | **Authoritative memory footprint:** 11 scalar `[OptionalField]` fields on `Camp` (88 bytes/camp, zero per-tile bytes — tile record stays `365 bytes/tile`), 2 sparse collections on `Society`. Single-pass transient `CivicLocalIndex` built once per turn in `CivicGovernance.Close`. Zero `LazyEcology.ProjectEcology` calls inside environmental drivers. |
| **5** | **Measure before optimizing** | Opt-in `PhaseProfile` scopes (`Civic.Close`, `Sanitation.Water`) instrumented from day one; profiled at 1 / 16 / 80 / 320 tribes in M5 before any micro-optimization. |
| **6** | **Keep time and outcomes consistent** | Semigroup-exact `dt`-invariance under `SmoothCalendar` (`1×20yr == 4×5yr == 20×1yr == 7300×1d` to $< 10^{-12}$) for $(C, P)$ relaxation, trade EMA, capital maintenance/retention, and physical-year practice accrual (including Tier-1's exact analytical permanence integral over $[s_0, s_1]$). `PriorityScore` and bootstrap `C_plan` amortize over $T_{\text{amort}} = \max(\Delta t, T_{\text{ref}})$ so `20 -> 1 yr` compression never collapses civic investment. Canonical camp-ID / society-ID / tile-ID commit order preserves determinism and exact food/water/tool/population conservation. |
| **7** | **Recheck assumptions when scope grows** | **Rejected architectures** (explicitly excluded — do NOT reintroduce): (a) *Discrete government-type switches* (cliffs break continuous geographic divergence & RL smoothness); (b) *Per-tile dense civic/sanitation arrays* (violates 320-tribe ~852 MiB heap budget); (c) *Storing `CivicCapital` / `SanitationCapital` inside `CampInventory`* (`Fission.Found` splits all `CampInventory` numeric fields via reflection at `Fission.cs:1004–1008`); (d) *Composite QoL feeding physical birth/death rates* (violates `DemographyQoLContract-v1.1.md`); (e) *Single-tile `FreshwaterAccess.Source` for Sanitation* (`Drinking` at $0.10\,N/\text{yr}$ saturates a max-flow river tile `258.5/yr` at $N = 2{,}585$, starving 3k–10k cities); (f) *Discontinuous `CivicFocus.Auto` step shift into $P^*$* (creates cliff at $\lvert P_{\text{signal}}\rvert = 0.12$ and inflicts self-alignment penalty on AI Elders); (g) *Hard-requiring `NeolithicTech.Pottery` for Tier 3A* (arid river valleys have 45–80 yr natural storage $> 30\text{ yr}$ `GrowthReserveYears`, so `TargetPots == 0` and arid Mesopotamian cities would permanently deadlock); (h) *Unbounded lifetime-annuity `PriorityScore`* (starves food expansion in young $< 600$ villages). |
| **8** | **Include scale in done** | Complete requires: Phase 0 `(1,1,1,0)` bit-parity + equilibrium tests, `dt`-subdivision semigroup tests, save/load & legacy-XML compatibility, `--bound-audit` zero violations, 100% opaque-box E2E Tiers 1–4 + Tier-5 adversarial hardening (0 gaps), matched 3-arm 5-world sweeps + 80-tribe long run + 1/16/80/320 benchmark, and `Check-CurrentContract.py` exit 0. |

---

## 3. Authoritative State & Persistence Contract

### 3.1 `SimulationConfig.Civic` (`Model.cs:27–30`, `41`, `311`)
- Add `[System.Runtime.Serialization.OptionalField] public CivicGovernanceSettings Civic;` on `SimulationConfig` (default `null`).
- Reset `Civic = null;` inside `[OnDeserializing] InitializeNeolithic` (`Model.cs:41`, alongside `SmoothCalendar = null;`) so legacy saves (`ANTHROPOCENE-8..12`) load with civic governance disabled and remain bit-identical.
- Call `Civic?.Validate();` inside `SimulationConfig.Validate()` (`Model.cs:311`, immediately after `Agriculture?.Validate();`).
- Feature predicates on `CivicGovernance`:
  ```csharp
  public static bool Enabled(SimulationConfig cfg) => cfg?.Civic != null && cfg.Civic.Enabled;
  public static bool SanitationEnabled(SimulationConfig cfg) => Enabled(cfg) && cfg.Civic.SanitationEnabled;
  ```
- **Unity `JsonUtility` Guard (`OpeningGame.cs:118`):** Immediately after `JsonUtility.FromJsonOverwrite(tuning.text, config);`, add:
  ```csharp
  if (!System.Text.RegularExpressions.Regex.IsMatch(tuning.text, "\"Civic\"\\s*:\\s*\\{")) config.Civic = null;
  ```
  (Prevents Unity `JsonUtility` from auto-instantiating a non-null default `CivicGovernanceSettings` when loading `OpeningConfig.json` or `AgriculturalTechnologyOpeningConfig.json`.)

### 3.2 `Camp` State (`Model.cs:559–586`) — 11 Persisted `[OptionalField]` + 3 Transient `[NonSerialized]`
All civic fields live **directly on `Camp` (NEVER on `CampInventory`)** so `Fission.Found`'s reflection split (`Fission.cs:1004–1008`) never splits fixed urban capital or institutional coordinates:

| Field on `Camp` | Type | Persistence | Domain / Default | Semantics |
| --- | --- | --- | --- | --- |
| `CivicComplexity` | `double` | `[OptionalField]` | $[0, 1]$, `0` | Institutional complexity coordinate $C$ |
| `CivicStratification` | `double` | `[OptionalField]` | $[-1, +1]$, `0` | Power distribution coordinate $P$ (`-1` Egalitarian $\leftrightarrow$ `+1` Stratified) |
| `CivicCapital` | `double` | `[OptionalField]` | $\ge 0$, `0` | Phase-1 fixed civic infrastructure (stays with parent on fission; zeroed on village move) |
| `SanitationCapital` | `double` | `[OptionalField]` | $\ge 0$, `0` | Phase-2 fixed urban drainage/waterworks (stays with parent on fission; zeroed on village move) |
| `ImmigrationPolicy` | `ImmigrationPolicy` | `[OptionalField]` | `Neutral = 0, Open = 1, Restricted = 2` | Settlement immigration posture |
| `CivicFocus` | `CivicFocus` | `[OptionalField]` | `Auto = 0, Communal = 1, Centralized = 2` | Civic steering order (`Auto` = zero steering push) |
| `RecentTradeVolume` | `double` | `[OptionalField]` | $\ge 0$, `0` | `dt`-invariant EMA of delivered trade volume (person-years / yr) |
| `SanitationWaterRatio` | `double` | `[OptionalField]` | $[0, 1]$, `0` | Last turn's delivered Phase-2 sanitation water fraction $W_{\text{sanit}}$ |
| `LastEnvironmentalComplexity` | `double` | `[OptionalField]` | $[0, 1]$, `0` | Committed $C_{\text{env}}$ from last `CivicGovernance.Close` ($O(1)$, fog-safe) |
| `LastEnvironmentalStratification` | `double` | `[OptionalField]` | $[-1, +1]$, `0` | Committed $P_{\text{env}}$ from last `CivicGovernance.Close` ($O(1)$, fog-safe) |
| `LastCircumscription` | `double` | `[OptionalField]` | $[0, 1]$, `0` | Committed $D_2$ from last `CivicGovernance.Close` ($O(1)$, fog-safe) |
| `SanitationWaterDelivered` | `double` | `[NonSerialized]` | $\ge 0$, `0` | Turn-transient delivered water units from `AllocateSanitationWater` |
| `ActiveSanitationCapital` | `double` | `[NonSerialized]` | $\ge 0$, `0` | Turn-transient retained opening `SanitationCapital` (non-retroactive build gate) |
| `UseActiveSanitationCapital` | `bool` | `[NonSerialized]` | `false` | Guards `ActiveSanitationCapital` during current-turn demography (prevents default-`0` trap after deserialization or standalone tests) |

- **View Cloning Contract:**
  - `Camp.WithPrivateInventory()` (`Model.cs:569`, `MemberwiseClone()`): automatically copies all 11 persisted value fields; explicitly clears `view.SanitationWaterDelivered = 0; view.ActiveSanitationCapital = 0; view.UseActiveSanitationCapital = false;`.
  - `EconomicPlanner.Preview(Camp c, EconomicIntent intent, double population)` (`EconomicPlanner.cs:796–801`): explicitly copies all 11 persisted civic fields from `c` onto `result`, while deliberately leaving `result.IsVillage` unchanged (`false`, matching PR #49) because `TurnPlanning.ComputeNeeds` (`TurnPlanning.cs:518, 549`) passes the preview snapshot to `NeolithicProduction.AnimalTextileNeedApplies(world, snapshot)` (`NeolithicProduction.cs:881`, which checks `c.IsVillage`). Mirroring PR #49's `IrrigationEconomy` pattern, `NeolithicProduction.RecipeAvailable` (`:371`) checks feature-enabled + precedent ONLY (never `c.IsVillage`), while `DemandControl` (which receives `planningCamp` where `TurnPlanning.cs:585–586` sets `IsVillage`) gates all civic/sanitation Continuity and Development requests on `c.IsVillage`.
  - `TurnPlanner.PlanningCampAt(...)` (`TurnPlanning.cs:564–588`): overlays accepted same-turn `FissionPolicy`, `ImmigrationPolicy`, and `CivicFocus` orders onto the planning copy without mutating the authoritative `Camp`.
  - `TurnResolver.Move(...)` (`TurnResolver.cs:398–435`): on **ANY** camp move `target != camp.Tile` (not only `if (abandoning)` at `:414`, matching Fishery operator release at `TurnResolver.cs:421` and fixed Housing staying behind), call `CivicGovernance.ReleaseAbandoned(camp)` setting `CivicCapital = 0; SanitationCapital = 0; SanitationWaterRatio = 0; ActiveSanitationCapital = 0; UseActiveSanitationCapital = false;` so fixed `CivicCapital` and `SanitationCapital` can never walk to a new hex under any code path or synthetic fixture (Society precedents stay intact).

### 3.3 `Society` State (`Societies.cs:46–61`) & `TurnRecord` (`Model.cs`)
- Add on `Society`:
  ```csharp
  [System.Runtime.Serialization.OptionalField] public Dictionary<string, double> CivicExperience = new Dictionary<string, double>();
  [System.Runtime.Serialization.OptionalField] public List<string> CivicPrecedents = new List<string>();
  public bool HasCivicPrecedent(string precedent) => CivicPrecedents != null && CivicPrecedents.Contains(precedent);
  ```
- Canonical precedent IDs (`CivicPrecedents` static class):
  - `CeremonialIntegration = "CeremonialIntegration"` (Tier 1)
  - `CivicOrganization = "CivicOrganization"` (Tier 2)
  - `TokensAndAccounting = "TokensAndAccounting"` (Tier 3A)
  - `PlannedQuartersAndDrainage = "PlannedQuartersAndDrainage"` (Tier 3B — Phase 2)
- Deep-copied in `Fission.Found` (`Fission.cs:969`): `CivicExperience = new Dictionary<string, double>(parent.People.CivicExperience)` and `CivicPrecedents = new List<string>(parent.People.CivicPrecedents)`.
- Null-coalesced in `SaveCodec.MigrateGenerationalDefaults` (`SaveCodec.cs:301`), plus strict finite/range checks on all loaded `Camp` civic fields (`0 <= C <= 1`, `-1 <= P <= 1`, capitals/trade $\ge 0$, `0 <= SanitationWaterRatio <= 1`, `0 <= LastEnvironmentalComplexity <= 1`, `-1 <= LastEnvironmentalStratification <= 1`, `0 <= LastCircumscription <= 1`, valid enum ordinals).
- `TurnRecord`: `[OptionalField] public List<SanitationWaterReceipt> SanitationWater;` and `[OptionalField] public List<CivicTurnReport> CivicReports;` (both `null` when disabled; zero impact on legacy saves or `ANTHROPOCENE-11`/`12` envelope or `365 bytes/tile` binary layout).

---

## 4. Phase 0 Prerequisite: Demography `(M_c, M_d)` Plumbing & Relocation Counterfactual Fix

### 4.1 `PhysicalDemography` & `CoupledDemography` Plumbing (`Demography.cs`, `CoupledDemography.cs`)
Currently `PhysicalDemography.CrowdingMitigation` and `DiseaseMitigation` (`Demography.cs:73–74`) return literal `1`, and `AdvanceCore`/`FastNet` (`Demography.cs:447, 469–470`) and `CoupledDerivative` (`CoupledDemography.cs:358–359`) hardcode `* 1.0`.

1. **Preserve backward-compatible positional parameter order and defaults** across all public/internal/private solver APIs:
   - **`Demography.cs` (existing `fertilityFactor` preserved in-place):**
     - `AdvanceFixed` (public `:133` and private `:141`) and `AdvanceCore` (`:441`) ALREADY declare `double fertilityFactor = 1` in their existing positional slot (`:133` slot 10; `:141` before `bool fast`; `:441` before `bool fast = false`). **Keep `fertilityFactor` in its exact existing position** (never breaking existing positional callers at `:218, 288, 295, 322` or external tests!), and append `double crowdingMitigation = 1, double diseaseMitigation = 1, double additionalMortality = 0` AFTER the existing parameters.
     - `AdvanceBudgetCore` (`:197`) already declares `double fertilityFactor, bool fast = false`: keep those in place, append `double crowdingMitigation = 1, double diseaseMitigation = 1, double additionalMortality = 0`, and scale `BirthOnlyPersonYears` at `:206` to `BirthOnlyPersonYears(population, config.BaseBirthRate * fertilityFactor, years)` (bit-exact `* 1` at `fertilityFactor == 1`; prevents bracket overflow when `fertilityFactor > 1`).
     - `AdvanceBudget` (`:177`), `PlanningBudget` (`:190`), `SurplusBudget` (`:259–302`), and `FastSurplus` (`:309–368`) append trailing defaulted `(..., double crowdingMitigation = 1, double diseaseMitigation = 1, double fertilityFactor = 1, double additionalMortality = 0)`. In `SurplusBudget` and `FastSurplus`, keep `out double fertility` as the surplus birth fraction $g \in [0, 1]$, forward `(crowdingMitigation, diseaseMitigation, additionalMortality)` everywhere, and compose `fertilityFactor * g` (full branch `fertilityFactor`, birthless branch `0`, bisection/Brent `fertilityFactor * g`) into `AdvanceBudgetCore` and `AdvanceFixed`.
     - In `AdvanceCore` (`:447`) forward `(crowdingMitigation, diseaseMitigation, fertilityFactor, additionalMortality)` to `Rates(...)`; in `FastNet` (`:465–474`) replace `* 1.0` with `* crowdingMitigation` (`:469`) and `* diseaseMitigation` (`:470`), check `Finite(additionalMortality)` (`:471`), and replace `+ 0.0` with `+ Math.Max(0, additionalMortality)` (`:473`) to match `RatesAtDensityCoverage` (`:126–128`).
   - **`CoupledDemography.cs`:**
     - `AdvanceCoupledBudget` (`:82`), `TryExactScalarReduction` (`:415`, called at `:86`, calling `AdvanceBudget` at `:455`), `AdvanceCoupledFixed` (both `internal :193` and `private :201`), `Rk4Coupled` (`:308`), and `CoupledDerivative` (`:329–370`) append trailing defaulted `(..., double crowdingMitigation = 1, double diseaseMitigation = 1, double fertilityFactor = 1, double additionalMortality = 0)` (preserving `IReadOnlyList<double> fertility = null` where already present on private `:201, :308, :329`).
     - Forward `(crowdingMitigation, diseaseMitigation, fertilityFactor, additionalMortality)` through `TryExactScalarReduction` (`:86, :455`) and `SurplusBudget` (`:104–105`); scale `BirthOnlyPersonYears` at `:116` to `BirthOnlyPersonYears(layout.PoolPopulation[p], config.BaseBirthRate * fertilityFactor, years)`; and in `CoupledDerivative` (`:358–359`) pass `(crowdingMitigation, diseaseMitigation, fertilityFactor * (fertility != null ? fertility[p] : 1), additionalMortality)` into `RatesAtDensityCoverage`.
2. **Validate mitigation arguments** inside `ValidateCall` / `ValidateCoupledCall`: require finite `crowdingMitigation >= 0`, `diseaseMitigation >= 0`, `fertilityFactor >= 0`, `additionalMortality >= 0`. (Civic governance itself enforces strictly positive floors $M_c \ge M_{c,\min} > 0$, $M_d \ge M_{d,\min} > 0$.)
3. **Wire production hooks & ALL FOUR Phase-0 call sites:**
   - `PhysicalDemography.CrowdingMitigation(w, c)` returns `CivicGovernance.Enabled(w.Config) && c != null && c.Population > 0 ? CivicGovernance.CrowdingMitigation(w, c) : 1`.
   - `PhysicalDemography.DiseaseMitigation(w, c)` returns `CivicGovernance.SanitationEnabled(w.Config) && c != null && c.Population > 0 ? CivicGovernance.DiseaseMitigation(w, c) : 1`.
   - Pass `(CrowdingMitigation(w, camp), DiseaseMitigation(w, camp), FertilityFactor(w, camp), AdditionalMortality(w, camp))` at all four real solver call sites:
     1. `GenerationalEconomy.cs:1224` (`ReconcilePhysicalDemography` $\to$ `PhysicalDemography.AdvanceCoupledBudget`)
     2. `GenerationalEconomy.cs:999` (`PlanningResidentPopulation` $\to$ `PhysicalDemography.AdvanceBudget`)
     3. `DemandControl.cs:1281` (`KernelSplit` `exact == true` $\to$ `PhysicalDemography.AdvanceBudget`)
     4. `DemandControl.cs:1282` (`KernelSplit` `exact == false` $\to$ `PhysicalDemography.PlanningBudget`)
4. **Bit-exact `(1, 1, 1, 0)` parity**: When `(crowdingMitigation, diseaseMitigation, fertilityFactor, additionalMortality) == (1, 1, 1, 0)`, IEEE-754 multiplication by `1.0` and addition of `0.0` produces bit-identical results across all existing 70+ PR #49 fixed checks.

### 4.2 `SocialRelocation.cs` Counterfactual Fix
- In `SocialRelocationForecastCache.Food` (`:800`), `UtilityUpperBound` (`:854–856`), and `Forecast` (`:947–949, 953–955`), switch `FertilityFactor`, `CrowdingMitigation`, `DiseaseMitigation`, and `AdditionalMortality` from **`state.Origin`** to **`c` (`f.Camp`)**.
- In `SocialRelocationForecastCache.Daughter()` (`:589–608`), invoke the shared `CivicGovernance.InitializeDaughterView(world, parent, view)` (identical to `Fission.Found`): zeroes `CivicCapital`, `SanitationCapital`, `RecentTradeVolume`, `SanitationWaterRatio`, resets `ImmigrationPolicy = Neutral`, `CivicFocus = Auto`, sets dampened `(C, P)`, and sets `LastEnvironmentalStratification = view.CivicStratification`, `LastEnvironmentalComplexity = view.CivicComplexity`, `LastCircumscription = 0`.

---

## 5. Quantitative Specification (11 Locked Specification Choices)

### Spec Choice #1 — Four $O(\text{local})$ Environmental Drivers $\to (C_{\text{env}}, P_{\text{env}})$

At `CivicGovernance.Close(w, record, horizonYears)`, build ONE transient `CivicLocalIndex` in a single linear pass over active entities: `canalsByOp` ($O(|\text{Canals}|)$), `irrigatedByOp` ($O(|\text{IrrigatedFields}|)$), `animalsByCamp` ($O(|\text{Herds}|)$), `campsByTile` ($O(|\text{Camps}|)$), plus a per-turn tile-productivity cache over queried ring tiles. All four drivers read ONLY committed local state — zero `LazyEcology.ProjectEcology` calls, zero hidden terrain tags, strictly null-safe when `w.Resources == null`, and strictly player-ID invariant (no `c.Player == 0` branch).

Let $F_1 = \text{Map.Radius}(c.\text{Tile}, 1)$, $L_1 = \{t \in F_1 : \text{CultivationEconomy.Cultivable}(w.\text{Tiles}[t])\}$, and $n_{\text{own}} = |\{t \in L_1 : t.\text{CultivationOperator} == c.\text{Id} \land t.\text{CultivationCapital} > 10^{-9}\}|$.

#### Driver 1 — Land-Limited vs. Labor-Limited Capital Intensity $D_1 \in [0, 1]$
1. **Irrigated share** (over camp's `IrrigatedField`s $f \in \text{irrigatedByOp}[c.\text{Id}]$ with $f.\text{Tile} \in F_1$):
   $$s_{\text{irr}} = \operatorname{clamp}\!\left(\frac{\sum_f \big(0.5 + 0.5\,\operatorname{clamp}(f.\text{WaterService}, 0, 1)\big)}{\max(1,\; \max(n_{\text{own}}, 3))},\; 0,\; 1\right)$$
2. **High-$q$ land-capital intensity** (intensification above extensive hoe farming):
   $$\bar{q} = \frac{1}{\max(1, |L_1|)} \sum_{\substack{t \in L_1 \\ t.\text{Op} = c.\text{Id}}} \text{CultivationEconomy.Intensity}(\text{cfg}, t.\text{CultivationCapital}), \qquad s_q = \operatorname{clamp}\!\left(\frac{\bar{q} - 0.25}{0.40},\; 0,\; 1\right)$$
3. **Plough capability**: $s_{\text{plough}} = 1.0$ if `AgriculturalTechnology.Plough(w, c) && n_own > 0 && c.Inventory.FarmTools > 1e-6`; $0.5$ if Plough tech with $n_{\text{own}} > 0$ and `FarmTools <= 1e-6`; $0.0$ otherwise.
4. **Land-crop subsistence share**: If $n_{\text{own}} == 0$, $f_{\text{crop}} = 0$; else with $W_{\text{food}} = \text{Farmers} + \text{Herders} + \text{Fishers} + c.\text{GathererPopulation}$ and $F_{\text{tot}} = \max(0, c.\text{PlantFood}) + \max(0, c.\text{AnimalFood}) + \max(0, c.\text{AquaticFood})$:
   $$f_{\text{worker}} = \frac{\text{Farmers}}{\max(1, W_{\text{food}})}, \qquad f_{\text{stock}} = \begin{cases}\frac{\max(0, c.\text{PlantFood})}{F_{\text{tot}}} & F_{\text{tot}} > 10^{-9} \\ f_{\text{worker}} & \text{otherwise}\end{cases}, \qquad f_{\text{crop}} = 0.60\,f_{\text{worker}} + 0.40\,f_{\text{stock}}$$
5. **Combined Driver 1:**
   $$D_1 = \operatorname{clamp}\!\Big(f_{\text{crop}} \cdot \big(0.55\,s_{\text{irr}} + 0.27\,s_q + 0.18\,s_{\text{plough}}\big),\; 0,\; 1\Big)$$

#### Driver 2 — Circumscription & Exit Cost $D_2 \in [0, 1]$
1. **Landscape & neighbor circumscription ($c_{\text{ring}} \in [0, 1]$):**
   - Candidate exit ring $R_{\text{exit}} = \{t \in \text{Map.Radius}(c.\text{Tile}, R) : \text{dist}(c.\text{Tile}, t) \ge d_{\min}\}$, where $R = \max(1, \text{cfg.FissionRadius})$ and $d_{\min} = (R \ge \text{cfg.MinimumCampSpacing}) ? \text{cfg.MinimumCampSpacing} : 1$.
   - Collect all *other* active camp tiles within distance $R + \max(0, \text{cfg.MinimumCampSpacing} - 1)$ via `campsByTile`. Tile $t \in R_{\text{exit}}$ is `unblocked(t)` iff `w.Tiles[t].TraversableLand` and $\text{dist}(t, t_{\text{other}}) \ge \text{cfg.MinimumCampSpacing}$ for all other nearby camps.
   - **Static 4-season freshwater viability** `viable(t)` (identical static gate to `PlacementStage.Footprint`, zero `LazyEcology`): over $n \in \text{Map.Radius}(t, 1)$ with `ElevationLevel < 10`, `WaterBody != Saltwater`, `FreshwaterSource != None`, compute `seasonalWater[s] = sum(MaxFreshwater)` and `annualWater = sum_{n,s} MaxFreshwater * WaterRecoveryPerDay * cfg.BaseDays`; viable iff `annualWater / 4 >= cfg.StartFreshwaterMinimum && min_s seasonalWater[s] >= cfg.StartFreshwaterMinimum`.
   - **Static tile productivity** $p(n)$ (from static `Tile.Seasons[0..3]`):
     $$p(n) = \frac{1}{4}\sum_{s=0}^{3}\begin{cases} n.\text{Seasons}[s].\text{MaxFood} + 0.10\,n.\text{Seasons}[s].\text{MaxPotentialFood} & n.\text{TraversableLand} \\ 2.0\,n.\text{Seasons}[s].\text{MaxFood} & n.\text{Aquatic} \land n.\text{ElevationLevel} < 10 \\ 0 & \text{otherwise} \end{cases}$$
     For any ring tile $t$, $\Pi(t) = \sum_{n \in \text{Radius}(t, 1)} p(n)$. For home tile $c.\text{Tile}$, each $n \in F_1$ with active `IrrigatedField` $f$ operated by $c$ (when `cfg.Agriculture != null`) scales $p(n)$ by:
     $$p_{\text{home}}(n) = p(n)\left(1 + \operatorname{clamp}(f.\text{WaterService}, 0, 1) \left[\max\!\left(1,\, \frac{\text{cfg.Agriculture.TargetMoisture}}{\max(0.05,\, \bar{m}_n)}\right)\big(1 + \text{cfg.Agriculture.SoilCapacityGain}\,f.\text{Soil}\big) - 1\right]\right)$$
     where $\bar{m}_n$ is the 4-season mean `Moisture` of tile $n$.
   - Effective viable alternatives & smooth circumscription:
     $$v_{\text{eff}} = \frac{1}{\max(1, |R_{\text{exit}}|)} \sum_{\substack{t \in R_{\text{exit}} \\ \text{viable}(t) \land \text{unblocked}(t)}} \operatorname{clamp}\!\left(\frac{\Pi(t)}{\max(10^{-9}, \Pi_{\text{home}})},\; 0,\; 1\right), \qquad c_{\text{ring}} = \exp\!\left(-\frac{v_{\text{eff}}}{0.18}\right)$$
2. **Sunk immobile capital ratio ($c_{\text{sunk}} \in [0, 1]$):**
   - `fixedPeople = NeolithicProduction.FixedHousingAt(w, c, w.Days) * n.HousingPeoplePerUnit`
   - `lightPeople = Math.Max(0, c.Inventory.LightHousingUnits) * n.LightHousingPeoplePerUnit`
   - `animals = animalsByCamp[c.Id]`, `canalSegments = sum_{s in canalsByOp[c.Id]} Clamp(s.Construction, 0, 1)`, `sumE = sum_{t in L_1, t.Op == c.Id} Math.Max(0, t.CultivationCapital)`.
   $$h_{\text{fixed}} = \frac{\text{fixedPeople}}{\max(10^{-9},\; \text{fixedPeople} + \text{lightPeople} + 2.0\,\text{animals} + 0.50\,\max(1, N))}$$
   $$c_{\text{infra}} = \operatorname{clamp}\!\left(\frac{\text{canalSegments} + 0.20\,\text{sumE}}{4.0},\; 0,\; 1\right), \qquad c_{\text{sunk}} = 0.45\,h_{\text{fixed}} + 0.55\,c_{\text{infra}}$$
3. **Combined Driver 2:**
   $$D_2 = \operatorname{clamp}\!\big(0.72\,c_{\text{ring}} + 0.28\,c_{\text{sunk}},\; 0,\; 1\big)$$

#### Driver 3 — Surplus Appropriability & Chokepoints $D_3 \in [0, 1]$
1. **Appropriable grain/pottery surplus ($a_{\text{grain}}$):** With $F_{\text{yr}} = \max(1, N)\cdot \text{FoodPerPersonYear}(\text{cfg})$, $s_{\text{plant}} = F_{\text{tot}} > 10^{-9} ? \max(0, c.\text{PlantFood}) / F_{\text{tot}} : 0$, $\text{grainYears} = \max(0, c.\text{PlantFood}) / F_{\text{yr}}$, and $\text{potYears} = \max(0, c.\text{Inventory.Pots})\cdot n.\text{PotStorageFoodPerUnit} / F_{\text{yr}}$:
   $$a_{\text{grain}} = \operatorname{clamp}\!\left(0.75\,\operatorname{clamp}\!\left(\frac{\text{grainYears} - 10}{35}, 0, 1\right)(0.35 + 0.65\,s_{\text{plant}}) + 0.25\,\operatorname{clamp}\!\left(\frac{\text{potYears}}{15} + 0.25\,\mathbb{1}[\text{Pottery}], 0, 1\right)s_{\text{plant}},\; 0,\; 1\right)$$
2. **`dt`-smoothed trade flow & hub centrality ($a_{\text{trade}}$):** With `partners` = count of non-moved foreign contacts in `c.People?.Memory?.Contacts` within `cfg.TradeRange`:
   $$a_{\text{trade}} = \operatorname{clamp}\!\left(0.70\,\frac{c.\text{RecentTradeVolume}}{c.\text{RecentTradeVolume} + 0.15\,\max(50, N)} + 0.30\,\min\!\left(1, \frac{\text{partners}}{4.0}\right),\; 0,\; 1\right)$$
3. **Localized resource chokepoints ($a_{\text{res}}$, strictly null-safe & scale-invariant):**
   - If `w.Resources == null`: $a_{\text{res}} = 0$.
   - Else let `matIndex = MaterialAccessIndex.For(w)` and `scale = Math.Max(1.0, w.Resources.MaximumQuantity)`. Over $t \in F_1$:
     - For each `SiteRef` in `matIndex.Sites(t)` with `Body.Concentration == MineralConcentration.Deposit`: add `0.50 * (Body.Quality / 100.0)` if any component is in `{Copper, Tin, Gold, Lead, Silver, Salt, Gemstones}`, else `0.25 * (Body.Quality / 100.0)` if in `{Limestone, Silica, Gypsum, Stone, Clay}`.
     - Over land tiles $t \in F_1$, compute mean scale-invariant fractions $\bar{f}_{\text{clay}} = \text{landCount} > 0 ? \text{sum}(\text{SurfaceAccessible}(t, \text{Clay}) / \text{scale}) / \text{landCount} : 0.0$ and $\bar{f}_{\text{stone}} = \text{landCount} > 0 ? \text{sum}(\text{SurfaceAccessible}(t, \text{Stone}) / \text{scale}) / \text{landCount} : 0.0$ (explicit empty-land-set guard prevents `.Average()` exceptions on degenerate fixtures).
     $$a_{\text{res}} = \operatorname{clamp}\!\left(0.60\,\min(1, \text{depositScore}) + 0.25\,\operatorname{clamp}\!\left(\frac{\bar{f}_{\text{clay}}}{0.25}, 0, 1\right) + 0.15\,\operatorname{clamp}\!\left(\frac{\bar{f}_{\text{stone}}}{0.25}, 0, 1\right),\; 0,\; 1\right)$$
4. **Combined Driver 3:**
   $$D_3 = \operatorname{clamp}\!\big(0.55\,a_{\text{grain}} + 0.25\,a_{\text{trade}} + 0.20\,a_{\text{res}},\; 0,\; 1\big)$$

#### Driver 4 — Collective Coordination & Volatility $D_4 \in [0, 1]$
1. **Static seasonal volatility ($v_{\text{season}}$):** Over traversable land tiles $t \in F_1$ (returning `0.0` when `traversableCount == 0`), mean moisture range $\overline{m_{\text{amp}}} = \text{traversableCount} > 0 ? \text{mean}(\max_s m_s - \min_s m_s) : 0.0$ and mean normalized biomass range $\overline{f_{\text{amp}}} = \text{traversableCount} > 0 ? \text{mean}\!\left((\max_s \text{MaxPotentialFood}_s - \min_s \text{MaxPotentialFood}_s) / \max(10^{-6}, \text{mean}_s \text{MaxPotentialFood}_s)\right) : 0.0$:
   $$v_{\text{season}} = \operatorname{clamp}\!\left(0.55\,\frac{\overline{m_{\text{amp}}}}{0.40} + 0.45\,\frac{\overline{f_{\text{amp}}}}{1.20},\; 0,\; 1\right)$$
2. **Canal upkeep burden:** $v_{\text{canal}} = \operatorname{clamp}(\text{canalSegments} / 4.0, 0, 1)$.
3. **Neighbor density:** $v_{\text{nbr}} = \operatorname{clamp}(n_{\text{nbr}} / 5.0, 0, 1)$ over non-moved foreign contacts within $\max(\text{cfg.TradeRange}, \text{cfg.FissionRadius} + 2)$.
4. **Combined Driver 4:**
   $$D_4 = \operatorname{clamp}\!\big(0.35\,v_{\text{season}} + 0.40\,v_{\text{canal}} + 0.25\,v_{\text{nbr}},\; 0,\; 1\big)$$

#### Combined Attractors $(C_{\text{env}}, P_{\text{env}})$
- **Power Distribution Attractor** ($w_1 = 0.36, w_2 = 0.36, w_3 = 0.18, w_4 = 0.10$, neutral center $S_0 = 0.42$):
  $$S_P = 0.36\,D_1 + 0.36\,D_2 + 0.18\,D_3 + 0.10\,D_4, \qquad P_{\text{env}} = \operatorname{clamp}\!\left(\frac{S_P - 0.42}{0.42},\; -1,\; +1\right)$$
- **Complexity Attractor** (anti-oscillation invariant: uses **unboosted** base housing cohesion scale $N_{s,0} = \text{CohesionScale}(n, H_{\text{camp}}, 1.0) \le 1{,}782$, NEVER boosted $N_s(C)$!):
  $$\rho_{\text{crowd}} = \frac{N}{\max(1, N_{s,0})}, \qquad g_N = \operatorname{clamp}\!\left(\frac{N - 150}{350},\; 0,\; 1\right)$$
  $$C_{\text{raw}} = \operatorname{clamp}\!\Big(g_N \cdot \big((1 - e^{-\rho_{\text{crowd}} / 0.64}) + 0.20\,D_4\big),\; 0,\; 1\Big), \qquad C_{\text{env}} = \min\!\big(C_{\max}(\text{society}),\; C_{\text{raw}}\big)$$
  With crowding scale denominator `0.64`, even a zero-volatility ($D_4 = 0$) fully housed village reaches $C_{\text{raw}}(1{,}000) = 0.584 \ge C_1 = 0.30$ and $C_{\text{raw}}(1{,}600) = 0.754 \ge C_2 = 0.75$, ensuring Tier 2 always saturates cleanly without requiring external conflict or canals.

#### Verified 11-Archetype Divergence Table (Python)
| Archetype | $D_1$ | $D_2$ | $D_3$ | $D_4$ | $S_P$ | $P_{\text{env}}$ | $C_{\text{raw}}$ | Natural Regime |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| (a1) Open rain-fed plains (pre-Plough, 12yr) | 0.044 | 0.120 | 0.168 | 0.138 | 0.103 | **-0.755** | 0.645 | Egalitarian |
| (a2) Open rain-fed plains (Plough, 15yr) | 0.222 | 0.154 | 0.238 | 0.188 | 0.197 | **-0.531** | 0.769 | Egalitarian |
| (a3) Rich rain-fed plains (Plough + 30yr max) | 0.269 | 0.169 | 0.456 | 0.188 | 0.259 | **-0.384** | 0.769 | Egalitarian ($< -0.35$) |
| (b) Circumscribed irrigated valley (AI hub) | 0.755 | 0.775 | 0.689 | 0.853 | 0.760 | **+0.810** | 1.000 | Stratified ($> +0.35$) |
| (c) Coastal fishing village (open coast) | 0.000 | 0.176 | 0.130 | 0.171 | 0.104 | **-0.753** | 0.613 | Egalitarian |
| (d1) Player-0 0-trade irrigated valley (3 canals) | 0.643 | 0.754 | 0.349 | 0.646 | 0.630 | **+0.501** | 0.984 | Stratified ($> +0.35$) |
| (d2) Player-0 2-canal valley (`Resources == null`) | 0.584 | 0.780 | 0.203 | 0.496 | 0.577 | **+0.374** | 0.893 | Stratified ($> +0.35$) |
| (e) Minimal test fixture (`Resources == null`) | 0.024 | 0.074 | 0.000 | 0.000 | 0.035 | **-0.916** | 0.546 | Clean / Null-safe |
| (f) Crowded rain-fed plains (dense neighbors) | 0.285 | 0.451 | 0.509 | 0.287 | 0.385 | **-0.083** | 0.851 | Hybrid (social circumscription) |
| (g) Pastoral nomad camp (open steppe, mobile) | 0.000 | 0.020 | 0.098 | 0.202 | 0.045 | **-0.893** | 0.800* | Strongly Egalitarian (*$C_{\max}=0$) |
| (h) Boxed-in island fishing chiefdom | 0.036 | 0.712 | 0.230 | 0.154 | 0.326 | **-0.224** | 0.648 | Hybrid/Egalitarian (steerable) |

---

### Spec Choice #2 — 2D Coordinate Domain, `dt`-Invariant Relaxation & Fission Dampening

1. **Precedent Ceilings on $C \in [0, C_{\max}]$:**
   - No civic precedent: $C_0 = 0.00$
   - Tier 1 (`CeremonialIntegration`): $C_1 = 0.30$
   - Tier 2 (`CivicOrganization`): $C_2 = 0.75$
   - Tier 3A (`TokensAndAccounting`): $C_3 = 1.00$
   *(Tier 3B `PlannedQuartersAndDrainage` requires Tier 2 + Construction and enables Phase-2 `SanitationCapital` independently of Tier 3A.)*
2. **Strictly Continuous Target $(C^*, P^*)$ (Zero Cliffs):**
   $$C^* = \operatorname{clamp}(C_{\text{env}},\; 0,\; C_{\max}), \qquad P^* = \operatorname{clamp}\!\left(P_{\text{env}} + \Delta P_{\text{fission}} + \Delta P_{\text{imm}} + \Delta P_{\text{focus}},\; -1,\; +1\right)$$
   - $\Delta P_{\text{fission}}$: `Encourage` $= -0.15$, `Neutral` $= 0.00$, `Discourage` $= +0.15$.
   - $\Delta P_{\text{imm}}$: `Open` $= -0.15$, `Neutral` $= 0.00$, `Restricted` $= +0.15$.
   - $\Delta P_{\text{focus}}$: `Communal` $= -0.45$, **`Auto` $= 0.00$**, `Centralized` $= +0.45$.
   - **Zero-cliff guarantee:** `CivicFocus.Auto` contributes **exact `0.0`** to $P^*$, so AI Elders follow $(C_{\text{env}}, P_{\text{env}})$ smoothly with zero discontinuous jump and zero self-inflicted alignment penalty. Maximum explicit steering is $\pm 0.75$ (capable of pulling an extreme $P_{\text{env}} = \pm 0.80$ past neutral at the cost of alignment friction).
3. **Analytical `dt`-Invariant Relaxation (`PhysicalDemography.Expm1`):**
   - Rates: $\lambda_C = 0.025\text{ yr}^{-1}$ ($t_{1/2} = 27.73\text{ yr}$), $\lambda_P = 0.020\text{ yr}^{-1}$ ($t_{1/2} = 34.66\text{ yr}$).
   $$C(t + \Delta t) = \operatorname{clamp}\!\Big(C(t) + (C^* - C(t))\big(-\operatorname{expm1}(-\lambda_C \Delta t)\big),\; 0,\; C_{\max}\Big)$$
   $$P(t + \Delta t) = \operatorname{clamp}\!\Big(P(t) + (P^* - P(t))\big(-\operatorname{expm1}(-\lambda_P \Delta t)\big),\; -1,\; +1\Big)$$
   - **Verified semigroup invariance (Python):** $|4\times 5\text{y} - 20\text{y}| = 0.00$, $|20\times 1\text{y} - 20\text{y}| = 5.55 \times 10^{-17}$, $|7300\times 1\text{d} - 20\text{y}| \le 1.08 \times 10^{-15}$.
4. **Fission Daughter Dampening (`CivicGovernance.DaughterCoordinates`):**
   Shared identically by `Fission.Found` and `SocialRelocationForecastCache.Daughter`:
   $$C_{\text{daughter}} = \operatorname{clamp}(0.50\,C_{\text{parent}},\; 0,\; C_{\max}), \qquad P_{\text{daughter}} = \operatorname{clamp}(0.50\,P_{\text{parent}} - 0.15,\; -1,\; +1)$$
   Daughter inherits deep-copied `Society.CivicPrecedents` and `Society.CivicExperience`, starts with `CivicCapital = 0`, `SanitationCapital = 0`, `RecentTradeVolume = 0`, `ImmigrationPolicy = Neutral`, `CivicFocus = Auto` (hence $I_{\text{civic}} = I_{\text{sanit}} = 0$).

---

### Spec Choice #3 — Capital, Stewards, Alignment Friction, `dt`-Invariant Upkeep & Affordability

Let $u = \frac{P + 1}{2} \in [0, 1]$ and alignment friction (using committed `c.LastEnvironmentalStratification` in $O(1)$):
$$F_{\text{align}}(P, P_{\text{env}}) = 1 + \kappa\,|P - P_{\text{env}}|, \qquad \kappa = 0.35$$

1. **Required Stewards** (zero when $C \le 10^{-12}$ or $N \le 0$):
   $$\beta_{\text{raw}}(P) = 2.60 - 1.45\,u, \qquad \beta(P, \text{has3A}) = \begin{cases} \beta_{\text{raw}}(P) & !\text{has3A} \\ 1 + 0.20\,(\beta_{\text{raw}}(P) - 1) & \text{has3A}\; (\beta \in [1.03, 1.32]) \end{cases}$$
   $$\text{ReqStewards}(N, C, P, \text{has3A}, P_{\text{env}}) = C\,N\,(0.045 + 0.060\,u)\left(1 + \left(\frac{N}{3500}\right)^2\right)^{\frac{\beta(P, \text{has3A}) - 1}{2}} F_{\text{align}}(P, P_{\text{env}})$$
   $C^\infty$-smooth knee at $N_{\text{knee}} = 3{,}500$: near-linear for $N \le 3{,}500$ and $\propto N^\beta$ for $N \gg 3{,}500$.
2. **Required Capitals & Delivered Coverage:**
   $$\text{ReqCap}(N, C, P) = C\,N\,(0.12 + 0.12\,u), \qquad \text{ReqSanitCap}(N) = 0.16\,N$$
   $$I_{\text{civic}} = \begin{cases} 0 & C \le 10^{-12} \lor N \le 0 \\ \min\!\left(1,\; \dfrac{\text{CivicCapital}}{\text{ReqCap}(N, C, P)},\; \dfrac{\text{Stewards}}{\text{ReqStewards}(N, C, P, \text{has3A}, P_{\text{env}})}\right) & \text{otherwise} \end{cases}$$
   $$I_{\text{sanit}} = \begin{cases} 0 & !\text{SanitationEnabled} \lor !\text{Has3B} \lor N \le 0 \\ \min\!\left(1,\; \dfrac{\text{SanitationCapital}_{\text{active}}}{\text{ReqSanitCap}(N)}\right) \cdot \operatorname{clamp}(c.\text{SanitationWaterRatio},\; 0,\; 1) & \text{otherwise} \end{cases}$$
   where $\text{SanitationCapital}_{\text{active}} = c.\text{UseActiveSanitationCapital} ? c.\text{ActiveSanitationCapital} : c.\text{SanitationCapital}$.
3. **Exact `dt`-Invariant Capital Maintenance & Retention Law:**
   - Base exponential decay rates: $\delta_{\text{civic}} = 0.018\text{ yr}^{-1}$ ($\delta_{\text{civic,eff}} = \delta_{\text{civic}} \cdot F_{\text{align}}$), $\delta_{\text{sanit}} = 0.025\text{ yr}^{-1}$.
   - Continuity maintenance target (capped to active requirement so abandoned ruins are never maintained):
     $$u_{\text{target}} = \delta_{\text{eff}} \cdot \min(K_{\text{open}}, \text{Req}) \cdot \Delta t$$
   - **Atomic post-Layer-1 commit in `ResolveCampNeolithic`** (**outside** `if (CultivationEconomy.EconomyEnabled(w, c))`, immediately after line `1150` and before `if (resolveNeeds)` at `:1152`, so existing capitals still decay even if economy is disabled):
     1. Do NOT mutate `c.CivicCapital` or `c.SanitationCapital` before Layer 1 (keeping `M_build` independent of Layer-1 recipe execution order).
     2. Accumulate realized `civicMaintPaid`, `civicBuilt`, `sanitMaintPaid`, `sanitBuilt` from `Produce` (zeros when `EconomyEnabled` is false).
     3. Commit both capitals analytically:
        $$K_{\text{maint}} = \delta_{\text{eff}}\Delta t > 0 \;?\; \min\!\left(K_{\text{open}},\; \frac{u_{\text{paid}}}{\delta_{\text{eff}} \Delta t}\right) : K_{\text{open}}$$
        $$K_{\text{retained}} = K_{\text{maint}} + (K_{\text{open}} - K_{\text{maint}})\,e^{-\delta_{\text{eff}}\Delta t}, \qquad K_{\text{end}} = K_{\text{retained}} + \Delta K_{\text{built}}$$
     4. Set `c.CivicCapital = K_civic_end` (immediately active for current-turn Cohesion, QoL, $M_c$, and `ApplyStorage`). Set `c.ActiveSanitationCapital = K_sanit_retained; c.UseActiveSanitationCapital = true; c.SanitationCapital = K_sanit_end;` so neglected upkeep immediately reduces current-turn $I_{\text{sanit}}$ while newly built `SanitationConstruction` activates cleanly at the next turn boundary once Tier-2 freshwater is allocated in `TurnHorizonResources`. Clear `c.UseActiveSanitationCapital = false` **unconditionally** for every camp immediately after the `if (PhysicalDemography.Enabled(w.Config)) { ... }` block (`GenerationalEconomy.cs:515–521`, right before `ApplyStorage` at `:522`), preventing stale flags in `LegacyQoLNetGrowth` worlds.
   - **Verified `dt`-invariance & conservation (Python):** Semigroup exact under full upkeep, ruin decay, and zero upkeep (`err(20x1y) <= 8.53e-14` for stock, `0.00` for cumulative upkeep work). Moreover, since $K_{\text{maint}} \ge 0$ and $\Delta K_{\text{built}} \ge 0$, $K_{\text{civic,end}} \ge K_{\text{open}} e^{-\delta_{\text{civic,eff}}\Delta t}$ unconditionally (verified `10,000/10,000` random trials), guaranteeing `StorageCapacityAfterPotDepreciation <= ApplyStorage`.
4. **Recipes (`CraftGood` 13–16; `Specialist = Builder`, `Track = Construction`, `Equipment = true`, NOT in `ToolCoverageBudget.IsHousingWork`):**
   - `CivicConstruction = 13`: `4.0` Builder-yr (`Rate = (BaseDays/DaysPerYear)/4.0 = 0.0625`), `2.0` `HousingMaterial`, `1` tool output, `SpecialistOnly = false`, `AgricultureOrFishing = true` (allows Tier-1 bootstrap via Craftsperson before `Construction` mastery).
   - `CivicMaintenance = 14`: `2.0` Builder-yr (`Rate = 0.125`), `1.0` `HousingMaterial`, `1` tool output, `SpecialistOnly = false`, `AgricultureOrFishing = true`.
   - `SanitationConstruction = 15`: `5.0` Builder-yr (`Rate = 0.05`), `2.5` `HousingMaterial`, `1` tool output, `SpecialistOnly = true`, `AgricultureOrFishing = true`.
   - `SanitationMaintenance = 16`: `2.5` Builder-yr (`Rate = 0.10`), `1.0` `HousingMaterial`, `1` tool output, `SpecialistOnly = true`, `AgricultureOrFishing = true`.

#### Verified Steady-State Workforce Affordability (Exact PR #49 Tool Coverage, Python)
On Plough+Irrigation (~30–33% food labor, ~4% essential non-civic crafts), **~60–65% of workforce is free surplus**:

| Scenario | $P$ | Stewards % | Civic Upkeep % | Sanit Upkeep % | Total (Reg Tools) | Total (Hand Tools) | Mismatch ($\lvert\Delta P\rvert = 2 / 1 / 2$) |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| $N=1{,}000$, T1 ($C=0.30$) | $-1 / 0 / +1$ | 1.44 / 2.33 / 3.17 | 0.33 / 0.49 / 0.64 | — | **1.77 / 2.82 / 3.81%** | 1.87 / 2.98 / 4.02% | 3.01 / 3.81 / 6.47% |
| $N=1{,}600$, T1 ($C=0.30$) | $-1 / 0 / +1$ | 1.57 / 2.44 / 3.20 | 0.33 / 0.49 / 0.64 | — | **1.90 / 2.94 / 3.83%** | 2.01 / 3.10 / 4.05% | 3.23 / 3.96 / 6.51% |
| $N=3{,}000$, T2 ($C=0.75$) | $-1 / 0 / +1$ | 5.24 / 7.16 / 8.21 | 0.83 / 1.21 / 1.52 | — | **6.07 / 8.37 / 9.73%** | 6.34 / 8.77 / 10.26% | 10.32 / 11.30 / 16.53% |
| $N=4{,}000$, T2 ($C=0.75$) | $-1 / 0 / +1$ | 6.59 / 8.11 / 8.38 | 0.83 / 1.21 / 1.52 | — | **7.41 / 9.32 / 9.90%** | 7.68 / 9.72 / 10.44% | 12.60 / 12.58 / 16.84% |
| $N=6{,}000$, T2 (**no 3A**+San) | $-1 / 0 / +1$ | 10.11 / 10.25 / 8.73 | 0.83 / 1.21 / 1.52 | 2.24 / 2.18 / 2.03 | **13.17 / 13.64 / 12.28%** | 14.10 / 14.70 / 13.47% | 20.83 / 17.65 / 19.45% |
| $N=6{,}000$, T3 (**+3A**+San) | $-1 / 0 / +1$ | 5.60 / 8.46 / 10.72 | 1.10 / 1.60 / 1.98 | 2.24 / 2.16 / 1.98 | **8.95 / 12.22 / 14.68%** | 9.96 / 13.41 / 16.05% | 13.64 / 15.74 / 23.57% |
| $N=10{,}000$, T2 (**no 3A**+San) | $-1 / 0 / +1$ | 19.86 / 14.83 / 9.30 | 0.83 / 1.21 / 1.52 | 2.24 / 2.18 / 2.03 | **22.93 / 18.22 / 12.85%** | 23.85 / 19.27 / 14.04% | 37.40 / 23.83 / 20.42% |
| $N=10{,}000$, T3 (**+3A**+San) | $-1 / 0 / +1$ | 6.41 / 9.10 / 10.85 | 1.10 / 1.60 / 1.98 | 2.24 / 2.16 / 1.98 | **9.76 / 12.87 / 14.82%** | 10.77 / 14.06 / 16.19% | 15.02 / 16.61 / 23.80% |

- Egalitarian is strictly cheaper throughout $1{,}000\text{–}4{,}000$ (`1.77–7.41%` vs `3.81–9.90%`), crosses above Stratified without Tier 3A at **$N \approx 5{,}650$** (`10.24% >= 10.19%`), and reaches `22.93%` at 10k unless Tier 3A flattens it (`9.76%`). Initial 20-year capital buildout from zero: `1.80–3.61%/yr` (T1 Civic), `4.51–9.02%/yr` (T2 Civic), `8.95–9.98%/yr` (Ph2 Sanitation) — genuine multi-turn works leaving ~40–50% workforce headroom.

---

### Spec Choice #4 — Numerically Solved Mitigation & Cohesion Equilibria

Hard positive mitigation floors enforced by `Validate()`: $M_{c,\min} = 0.05$ ($\alpha_c \le 0.95$) and $M_{d,\min} = 0.15$ ($\alpha_d \le 0.85$).

1. **Crowding Mitigation ($M_c$):**
   $$f_c(C) = \frac{1 - e^{-5.8\,C^{1.65}}}{1 - e^{-5.8}}, \qquad \alpha_c(C, P) = \operatorname{clamp}\!\left((0.925 + 0.025\,P\,C)\,f_c(C),\; 0,\; 1 - M_{c,\min}\right), \qquad M_c = 1 - \alpha_c(C, P)\,I_{\text{civic}}$$
2. **Phase-2 Disease Mitigation ($M_d$):**
   $$f_d(C) = \frac{1.6\,C}{C + 0.6}, \qquad \alpha_d(C, P) = \operatorname{clamp}\!\left((0.735 + 0.075\,P)\,f_d(C),\; 0,\; 1 - M_{d,\min}\right), \qquad M_d = 1 - \alpha_d(C, P)\,I_{\text{sanit}}$$
3. **Social Cohesion Scale Gain ($g_{\text{civic}}$):**
   $$g_{\text{civic}}(C, P) = \left(1.95\,C + 4.45\,C^{4.2}\right)\left(1 + 0.32\,P\,C^{1.5}\right)$$
   $$N_s = N_{\text{band}}(1 + k H)\left(1 + g_{\text{civic}}(C, P)\,I_{\text{civic}}\right), \qquad S(N) = \exp\!\left(-(N / N_s)^2\right)$$
   Hooked into `NeolithicProduction.CohesionScale(n, H, civicMultiplier = 1)` and `SocialCohesion(n, H, N, civicMultiplier = 1)` (`NeolithicProduction.cs:1156–1160`) across **all 8 call sites / overloads** (zero omitted sites):
   1. `NeolithicProduction.cs:1162–1168`: `CohesionScale(WorldState w, Camp c, double population)` and `SocialCohesion(WorldState w, Camp c, double population)` forward `CivicGovernance.CohesionMultiplier(w, c)` (remaining `1.0` when disabled), covering BOTH `ProductionDemand.cs:571` (`ClaimValuation.CampTerms` $\to$ `Trade.Value`) AND `ProductionDemand.cs:834` (`ExpansionValuation.Welfare`). *(Note: `CivicGovernance.Close`'s anti-oscillation $N_{s,0}$ explicitly calls the `NeolithicConfig` overload `CohesionScale(n, H_camp, 1.0)` directly, never the `(WorldState, Camp, double)` overload.)*
   2. `NeolithicProduction.cs:1186` (`ResolveNeeds`)
   3. `NeolithicProduction.cs:1203–1205` (`CommitExperiencedWelfare`)
   4. **`DemandControl.cs:1286–1294` (`KernelSplit` local `QoL(...)`, `:1293`):** MUST pass `CivicGovernance.CohesionMultiplier(w, c)` AND apply `Q_civic` (without this, at $N = 10{,}000$, unboosted $S(10{,}000) = 2.1 \times 10^{-14} < 10^{-12}$ triggers total tie-collapse at `DemandControl.cs:1323` under `ElderSplitObjective == 2`, causing the Elder to pick `(ClothingLevel = 0, ShelterLevel = 0)` and shut off `Protected` clothing/shelter!)
   5. `Fission.cs:162, 174` (`EstimateSplit`; founding daughter uses `civicMult = 1.0`)
   6. **`Fission.cs:253–271` (`QoLModel.Shape` `:260` / `At` `:253` / `ForwardAt` `:270`):** store precomputed `civicMult = CivicGovernance.CohesionMultiplier(w, camp)` and `eqMult = CivicGovernance.QoLEqualityMultiplier(w, camp)` on `QoLModel`, preserving the documented invariant `QoLModel.At(Residents) == EstimateSplit(...).QNow` (`Fission.cs:240`)
   7. `SocialRelocation.cs:845–846` (`UtilityUpperBound`, evaluated on `c = f.Camp`)
   8. `SocialRelocation.cs:939` (`Forecast`, evaluated on `c = f.Camp`)

#### Verified Physical Demography & Cohesion Equilibria (Python, $F = c = h = 1$, $I_{\text{civic}} = 1$)
| Regime | $P$ | $\alpha_c$ | $M_c$ | $\alpha_d$ | $M_d$ | $N^*$ | Brackets ($N_0=1$) | $N_s$ | $S(N^*)$ | $Q_{\text{civic}}$ | $\text{QoL}^*$ |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| **Baseline** ($C_0=0$) | all | 0.0000 | 1.0000 | 0.0000 | 1.0000 | **1,000.0** | 10 | 1,782 | 0.730 | 1.000 | 0.730 |
| **Tier 1** ($C_1=0.30$) | $-1 / 0 / +1$ | .5049/.5091/.5132 | .4951/.4909/.4868 | 0.0000 | 1.0000 | **1,601 / 1,609 / 1,617** | 11 | 2,817–2,932 | .724–.738 | 1.030/1.000/.976 | .746/.731/.720 |
| **Tier 2, Ph1** ($C_2=0.75$) | $-1 / 0 / +1$ | .8844/.9027/.9210 | .1156/.0973/.0790 | 0.0000 | 1.0000 | **2,906 / 3,023 / 3,151** | 12 | 5,723–7,791 | .773–.849 | 1.075/1.000/.940 | .831/.819/.798 |
| **Tier 3A, Ph1** ($C_3=1.00$) | $-1 / 0 / +1$ | .9000/.9250/.9500 | .1000/.0750/.0500 | 0.0000 | 1.0000 | **3,006 / 3,180 / 3,376** | 12 | 9,537–16,836 | .905–.961 | 1.100/1.000/.920 | .996/.944/.884 |
| **Tier 2+3B, Ph2** ($C_2=0.75$) | $-1 / 0 / +1$ | .8844/.9027/.9210 | .1156/.0973/.0790 | .5867/.6533/.7200 | .4133/.3467/.2800 | **5,056 / 5,965 / 7,271** | 13 | 5,723–7,791 | .458–.419 | 1.075/1.000/.940 | .493/.459/.393 |
| **Tier 3AB, Ph2** ($C_3=1.00$) | $-1.0$ | 0.9000 | 0.1000 | 0.6600 | 0.3400 | **5,955.7** | 13 | 9,537 | 0.677 | 1.100 | 0.745 |
| **Tier 3AB, Ph2** ($C_3=1.00$) | $-0.5$ | 0.9125 | 0.0875 | 0.6975 | 0.3025 | **6,695.7** | 13 | 11,362 | 0.707 | 1.048 | 0.740 |
| **Tier 3AB, Ph2** ($C_3=1.00$) | $0.0$ | 0.9250 | 0.0750 | 0.7350 | 0.2650 | **7,645.3** | 13 | 13,187 | 0.715 | 1.000 | 0.715 |
| **Tier 3AB, Ph2** ($C_3=1.00$) | $+0.5$ | 0.9375 | 0.0625 | 0.7725 | 0.2275 | **8,908.1** | 14 | 15,012 | 0.703 | 0.958 | 0.673 |
| **Tier 3AB, Ph2** ($C_3=1.00$) | $+1.0$ | 0.9500 | 0.0500 | 0.8100 | 0.1900 | **10,669.0** | 14 | 16,836 | 0.669 | 0.920 | 0.616 |

- **Partial coverage monotonicity** ($P = 0$, $I \in \{0, 0.25, 0.50, 0.75, 1.0\}$): Tier 2 Ph1 $N^* \in \{1000.0, 1201.9, 1505.4, 2012.2, 3023.2\}$; Tier 3AB Ph2 ($I_{\text{sanit}} = I_{\text{civic}}$) $N^* \in \{1000.0, 1278.4, 1771.5, 2882.0, 7645.3\}$.
- **Solver numerical safety at absolute floor ($M_{c,\min}=0.05, M_{d,\min}=0.15$):** $N^*_{\max} = 11{,}994.29$; doubling brackets from $N_0=1$: **14** (vs `DemographicMaxRootIterations = 128`, 9.1× margin); log-bands from $N_0=24$: **124.3** (vs `DemographicMaxBands = 8192`, 65.9× margin); peak $k_d \cdot 2^{14} = 0.4450 \ll 709$ (1,593× margin).

---

### Spec Choice #5 — Continuous $P \in [-1, +1]$ Asymmetric Trade-Offs (Zero Cliffs)

All modifiers reduce identically to `1.0` (or `0.0`) when `!CivicGovernance.Enabled(cfg)`, $C = 0$, or $I_{\text{civic}} = 0$. Let $u = \frac{P + 1}{2} \in [0, 1]$.

1. **Welfare-Only QoL Equality Modifier** (**NEVER enters `PhysicalDemography`!**):
   $$Q_{\text{civic}}(C, P, I_{\text{civic}}) = 1 + \left(-0.09\,P + 0.01\,P^2\right) C\,I_{\text{civic}}$$
   Strictly decreasing in $P$ ($1.100$ at $P=-1$, $1.000$ at $P=0$, $0.920$ at $P=+1$ for $C=I_{\text{civic}}=1$). Applied via `Numbers.Clamp(baseQoL * Q_civic, 0, 1)` at **all 8 welfare sites**: `NeolithicProduction.ResolveNeeds` (`:1186`), `NeolithicProduction.CommitExperiencedWelfare` (`:1203–1205`), `DemandControl.KernelSplit` local `QoL(...)` (`:1286–1294`), `Fission.EstimateSplit` (`:163–164, 175–176`), `Fission.QoLModel.At`/`ForwardAt` (`:253, 270`, preserving `QoLModel.At(Residents) == EstimateSplit(...).QNow`), `SocialRelocationForecastCache.UtilityUpperBound` (`:847`), `SocialRelocationForecastCache.Forecast` (`:938–939`), and `ProductionDemand.ExpansionValuation.Welfare` (`:834–835`).
2. **Stratified Public-Works Builder Mobilization Bonus** (`SpecialistProductivity.CraftRate`, `SpecialistProductivity.cs:17–25`):
   $$M_{\text{build}}(C, P, I_{\text{civic}}) = 1 + 0.35\,u^2\,C\,I_{\text{civic}} \qquad (1.000\times \text{ at } P=-1 \leftrightarrow 1.350\times \text{ at } P=+1)$$
   Applies ONLY to specialist Builder on public-works infrastructure recipes (`CanalConstruction`, `CanalMaintenance`, `CivicConstruction`, `CivicMaintenance`, `SanitationConstruction`, `SanitationMaintenance`), NEVER private `Housing`, `LightHousing`, or `HousingMaterial`. Multiplying `rate` before `ToolCoverageBudget.Capacity`/`Spend`/`Time` cuts Builder-time by $1 / M_{\text{build}}$ while `consumed[grade] += units / OutputsPerTool` preserves **exact physical tool wear per unit**.
3. **Stratified Centralized Granary Storage Bonus:**
   $$M_{\text{storage}}(C, P, I_{\text{civic}}) = 1 + 0.50\,u^2\,C\,I_{\text{civic}} \qquad (1.000\times \text{ at } P=-1 \leftrightarrow 1.500\times \text{ at } P=+1)$$
   - `NeolithicProduction.NaturalStorageCapacity` (`:789–793`) multiplies base natural storage by `CivicGovernance.GranaryStorageMultiplier(w, c)`.
   - `NeolithicProduction.StorageCapacityAfterPotDepreciation` (`:832–837`) evaluates `GranaryStorageMultiplier` at projected lower-bound capital $K_{\text{lower}} = c.\text{CivicCapital} \cdot e^{-\delta_{\text{civic,eff}}\Delta t} \le K_{\text{end}}$, preserving exact food conservation in `ApplyStorage`.
   - `GoodsStorage.Begin` (`GoodsStorage.cs:54`) reads `NaturalStorageCapacity(w, c, 0)` automatically.
4. **Unified Open-Frontier Exit Mismatch & Relocation Thresholds** (shared by `Fission.cs` and `Immigration.cs`):
   $$M_{\text{exit}}(c) = c.\text{CivicComplexity} \cdot \max\!\big(0,\; c.\text{CivicStratification} - c.\text{LastEnvironmentalStratification}\big) \cdot \big(1 - \operatorname{clamp}(c.\text{LastCircumscription}, 0, 1)\big)$$
   (Weighting by $C$ guarantees pre-civic villages at $C = 0$ have exact $M_{\text{exit}} = 0$.)
   - **Fission threshold** (`Fission.EffectiveMoverThreshold`, used identically in `Fission.Evaluate` and `Fission.RelocationAt`): let $G^* = \text{Kinship.GainThreshold}(\text{cfg}, c.\text{FissionPolicy})$. Return exact $G^*$ whenever `!CivicGovernance.Enabled(cfg) || c.CivicComplexity <= 1e-12` (so custom fixtures with `FissionGainThreshold < 0.02` or `> 0.50` stay bit-exact at $C = 0$); otherwise:
     $$\theta_{\text{eff}}(c) = \operatorname{clamp}\!\Big(G^* + 0.045\,P\,C\,I_{\text{civic}} - 0.060\,M_{\text{exit}}(c),\; \min(0.02, G^*),\; \max(0.50, G^*)\Big)$$
     Aligned Neutral ($G^* = 0.113$, $C = I_{\text{civic}} = 1$): Egalitarian $\to \mathbf{0.068}$ (Trypillia dispersal), Stratified $\to \mathbf{0.158}$ (holds on reserves). Coerced Stratified on an open frontier ($P=+1, P_{\text{env}}=-0.5, D_2=0.15 \Rightarrow M_{\text{exit}}=1.275$): $\mathbf{0.0815} < 0.113$ (exit friction overcomes Stratified resistance!).
   - **Immigration pull & exit push** (`Immigration.PolicyFor`, Choice #8): $\Delta K_P = 0.025\,P_{\text{dest}}\,C_{\text{dest}}\,I_{\text{civic,dest}}$ and $\Delta K_{\text{exit}} = -0.030\,M_{\text{exit}}(\text{source})$.

---

### Spec Choice #6 — Practice-Driven Precedents & Trade Seam

In `CivicGovernance.Close(w, record, horizonYears)` (exact internal order):
1. **Update `Camp.RecentTradeVolume` EMA** from `record.Trades` once per active camp (never inside `Trade.Finish`'s per-agreement loop!):
   Convert each delivered trade leg to physical person-years: `Food`: `qty / PhysicalDemography.FoodPerPersonYear(cfg)` (`4.0` at default config); `Cloth`/`AnimalFabric`: `qty * 20.0`; `HousingMaterial`/`LightHousing`: `qty * 4.0`; `Pots`: `qty * 5.0`; tools & raw materials: `qty * 1.0`. Agreement volume $V_k = 0.5(\text{PY}_{\text{want}} + \text{PY}_{\text{give}})$. With $\text{annualFlow}_c = V_{\text{turn},c} / \max(10^{-6}, \Delta t)$ and $\lambda_{\text{trade}} = 0.025\text{ yr}^{-1}$:
   $$\text{RecentTradeVolume}_c(t + \Delta t) = \text{annualFlow}_c + (\text{RecentTradeVolume}_c(t) - \text{annualFlow}_c)\,e^{-\lambda_{\text{trade}}\Delta t}$$
2. **Accrue Civic Practice & Unlock Precedents** ($\Delta t = \text{record.Duration} / \text{cfg.DaysPerYear}$, canonical `c.Id` accrual, then canonical `s.Id` $\times$ tier order `1, 2, 3A, 3B` unlock commit — lifting $C_{\max}$ BEFORE step 3 relaxes $(C, P)$!):
   Pre-index `builderWorkYears[c.Id]` once in $O(\lvert\text{record.Production}\rvert)$ as $\sum p.\text{WorkerIntervals} \cdot (\text{BaseDays} / \text{DaysPerYear})$ where `p.Role == Builder`. Let $N_{s,0} = \text{CohesionScale}(n, H_c, 1.0)$ and $\rho_{\text{crowd}} = N / \max(1, N_{s,0})$.
   For **Tier 1** (`CeremonialIntegration`), because `TurnResolver.Move` resets `StationaryTurns = 0; StationaryYears = 0;` (`TurnResolver.cs:430, 433`) while `ResolveNeeds` adds `c.StationaryYears += years;` (`NeolithicProduction.cs:1183`) *before* `CivicGovernance.Close`, accrual MUST gate on `c.StationaryTurns > 0` (exact `0` for any mobile camp that moved this turn) and integrate $\operatorname{clamp}(s / 40, 0, 1)$ analytically over $[s_0, s_1]$ where $s_1 = \max(0, c.\text{StationaryYears})$ and $s_0 = \max(0, s_1 - \Delta t)$:
   $$\Delta t_{\text{eff}} = \frac{\min(s_1, 40)^2 - \min(s_0, 40)^2}{80} + \max\!\big(0,\; s_1 - \max(s_0, 40)\big)$$

| Precedent | Prerequisites | Per-Turn Accrual ($\Delta X$, physical-yr) | Threshold $\Theta$ | Verified Unlock Time |
| --- | --- | --- | ---: | --- |
| **Tier 1:** `CeremonialIntegration` | `Era == Neolithic` **AND** `c.StationaryTurns > 0` | `c.StationaryTurns <= 0 ? 0 :` $\Delta t_{\text{eff}} \cdot \operatorname{clamp}\!\left(\frac{\rho_{\text{crowd}} - 0.25}{0.50}, 0, 1\right)$ | **40.0** | **64.3 yr** steady-state ($s_0 \ge 40$) / **84.3 yr** from $s_0=0$ at $N=1{,}000$; exact **0** if mobile or $N \le 445$; semigroup-exact across `2×20y / 8×5y / 40×1y` (`X_1 = 12.446689`, err `< 1e-14`) and `14,600×1d` (err `< 1e-12`) |
| **Tier 2:** `CivicOrganization` | Tier 1 | $\Delta t \cdot \min\!\left(1, \frac{\text{stewards}}{15}\right) \left(0.55\,\operatorname{clamp}\!\left(\frac{\rho_{\text{crowd}} - 0.50}{0.60}, 0, 1\right) + 0.45\,\min\!\left(1, \frac{\text{builderWorkYears}}{\max(10^{-6}, \Delta t)\cdot 10}\right)\right)$ | **80.0** | **104.8 yr** at $N=1{,}500$; 0 if `stewards == 0` |
| **Tier 3A:** `TokensAndAccounting` | Tier 2 **AND** (`HasTech(Pottery)` $\lor$ `Pots > 1e-6` $\lor$ `HasLocalClay(w, c)` $\lor$ `w.Resources == null`) | $\Delta t \cdot \min\!\left(1, \frac{\text{stewards}}{35}\right) \cdot m_{\text{pot}} \cdot \big(0.65\,u_{\text{store}} + 0.55\,u_{\text{trade}}\big)$, with $m_{\text{pot}} = \text{HasTech(Pottery)} ? 1.25 : 1.00$, $u_{\text{store}} = \operatorname{clamp}\!\left(\frac{\max(0, c.\text{Food})}{F_{\text{yr}}\cdot 20}\cdot \operatorname{clamp}\!\left(\frac{N}{1200}, 0, 1\right), 0, 1\right)$, $u_{\text{trade}} = \frac{\text{RecentTradeVolume}}{\text{RecentTradeVolume} + 0.15\,\max(100, N)}$ | **120.0** | **Player 0 (0 trade):** **147.7 yr** (Pottery) / **184.6 yr** (dry Clay). **AI hub ($u_{\text{trade}}=0.6$):** **98.0 yr / 122.4 yr** (~34% faster) |
| **Tier 3B:** `PlannedQuartersAndDrainage` | `SanitationEnabled` **AND** Tier 2 **AND** `HasTech(Construction)` | $\Delta t \cdot \operatorname{clamp}\!\left(\frac{N - 1200}{1800}, 0, 1\right) \left(0.55\,\min\!\left(1, \frac{\text{stewards}}{35}\right) + 0.45\,\min\!\left(1, \frac{\text{builderWorkYears}}{\max(10^{-6}, \Delta t)\cdot 20}\right)\right)$ | **100.0** | **100.0 yr** at $N \ge 3{,}000$; 0 if $N \le 1{,}200$ |

3. **Compute $(C_{\text{env}}, P_{\text{env}}, D_2)$ via transient `CivicLocalIndex`**, commit `LastEnvironmentalComplexity`, `LastEnvironmentalStratification`, `LastCircumscription`, and apply analytical `Expm1` relaxation to `(C, P)` over `horizonYears`.
4. **Tier 3A Trade Bonuses & Inert Diffusion Seam (`Trade.cs`):**
   - `Trade.Plan` (`Trade.cs:920–924`): per camp `c`, `range = cfg.TradeRange + ExtraTradeRange(w, c)` (`+2` with Tier 3A) and `maxReqs = cfg.TradeRequisitionsPerCamp + ExtraTradeRequisitions(w, c)` (`+2` with Tier 3A).
   - `Trade.Valuation` (`Trade.cs:551`): add `public bool HasAccounting;`; in `Trade.Respond` (`Trade.cs:867`), multiply `margin` by `(r.HasAccounting ? 1 - TokensMarginReduction : 1) * (p.HasAccounting ? 1 - TokensMarginReduction : 1)` (`TokensMarginReduction = 0.25`, bit-identical `1.0` when disabled).
   - Inert diffusion seam in `Trade.Finish` (`Trade.cs:1170`): call `CivicGovernance.RecordTradeDiffusion(w, r.People, p.People, V_k)` and reverse; strictly no-op when `PrecedentDiffusionPerTrade == 0` (default).

---

### Spec Choice #7 — `DemandControl` Priority, Re-Calibrated `PriorityScore` & `HasInfrastructure`

#### A. Survival-First Service Classes & Exact Request Sizing (`DemandControl.cs`)
- **`Protected` (Class 0):** Untouched (survival food + kernel-scored clothing/shelter always strictly first).
- **`Continuity` (Class 2, appended AFTER farm/herding/fishing tools and `canal maintenance` so civic continuity trims first under tight integer-staffing ties):**
  Let $T_{\text{amort}} = \max(\text{years},\; \text{cfg.Neolithic.ReferenceTurnYears})$ (`max(years, 20)`), and $C_{\text{plan}} = \min\!\big(C_{\max},\; \max(c.\text{CivicComplexity},\; C_{\text{next}}(w, c, T_{\text{amort}}))\big)$ (ensures `dt`-invariant bootstrap of durable capital on both 20-yr and 1-yr SmoothCalendar turns!). `DemandControl` evaluates `ReqCap`/`ReqStewards` at the camp's committed `c.CivicStratification` so Turn-$T$ staffing matches Turn-$T$ $I_{\text{civic}}$ in `ResolveNeeds`/`ReconcilePhysicalDemography`, while same-turn `CivicFocus`/`FissionPolicy`/`ImmigrationPolicy` steer $P \to P^*$ at Close (and `PlanningDraft` still invalidates on policy change for preview/draft consistency). All civic/sanitation Continuity and Development requests gate on `c.IsVillage` (matching PR #49's `Irrigation` at `DemandControl.cs:975`), whereas `CivicRecipesAvailable(w, c)` (`Enabled && HasCivicPrecedent(CeremonialIntegration)`) and `SanitationRecipesAvailable(w, c)` (`SanitationEnabled && HasCivicPrecedent(PlannedQuartersAndDrainage)`) check feature + precedent ONLY (never `c.IsVillage`, mirroring `NeolithicProduction.cs:371` so `ComputeNeeds` on `EconomicPlanner.Preview` never forecasts `0`).
  1. `civic maintenance` (`CraftGood.CivicMaintenance`): when `c.IsVillage && c.CivicCapital > Epsilon && CivicRecipesAvailable(w, c)`, target $u_{\text{civic,maint}} = \delta_{\text{civic,eff}} \cdot \min(c.\text{CivicCapital},\; \text{ReqCap}(N, C_{\text{plan}}, P)) \cdot \text{years}$.
  2. `core stewards` (`LabourRequest`, `WorkforceRoles.Steward`, `WholeUnits = false`): when `c.IsVillage && StewardAvailable(w, c) && c.CivicCapital > Epsilon && c.CivicComplexity > 1e-12`, target $\text{coreStewards} = \min(1, c.\text{CivicCapital} / \max(\epsilon, \text{ReqCap}(N, C, P))) \cdot \text{ReqStewards}(N, C, P, \text{has3A}, P_{\text{env}})$. Zero material inputs $\implies$ unaffected by timber shortages during Continuity bisection!
  3. `sanitation maintenance` (`CraftGood.SanitationMaintenance`, Phase 2): when `SanitationEnabled && c.IsVillage && c.SanitationCapital > Epsilon && SanitationRecipesAvailable(w, c)`, target $u_{\text{sanit,maint}} = \delta_{\text{sanit}} \cdot \min(c.\text{SanitationCapital},\; \text{ReqSanitCap}(N)) \cdot \text{years}$.
- **`Development` (Class 4, competing on `food / worker-year` `PriorityScore` against Wild Gather `7.20`, Fertile Field `7.0–7.7`, Marginal Field `3.6–5.5`, Canal `7.2–8.6`, Plough `9.0–12.0`):**
  1. `civic development` (`CraftGood.CivicConstruction`, gated by `c.IsVillage && CivicGovernance.Enabled(w.Config) && CivicRecipesAvailable(w, c) && residents >= DevelopmentMinPopulation = 300`):
     - $\Delta K_c = \max(0, \text{ReqCap}(N, C_{\text{plan}}, P) - c.\text{CivicCapital})$, $\Delta I_c = \Delta K_c / \max(\epsilon, \text{ReqCap}(N, C_{\text{plan}}, P))$, and marginal Steward cost matched to $\Delta I_c$:
       $$\Delta S_{\text{score}} = \Delta I_c \cdot \text{ReqStewards}(N, C_{\text{plan}}, P, \text{has3A}, P_{\text{env}})$$
     - Annualized joint worker cost (using exact `Bundle.RoleYears.Values.Sum()` for `CivicConstruction` and `CivicMaintenance`, matching `CanalConstruction` at `DemandControl.cs:990`):
       $$W_{\text{civic,yr}} = \frac{\text{BundleRoleYears}(\text{CivicConstruction}) \cdot \Delta K_c}{T_{\text{amort}}} + \Delta S_{\text{score}} + \text{BundleRoleYears}(\text{CivicMaintenance}) \cdot \Delta K_c \cdot \delta_{\text{civic,eff}}$$
       $$\text{PriorityScore}_{\text{civic}} = \frac{N \cdot d_{\text{crowd}}(N) \cdot \alpha_c(C_{\text{plan}}, P) \cdot \Delta I_c \cdot (\text{CivicDeathFoodYears} \cdot f_0)}{\max(\epsilon,\; W_{\text{civic,yr}})}$$
     - Using marginal $\Delta S_{\text{score}}$ (which coincides identically with $\text{ReqStewards}(C_{\text{plan}}) - \text{coreStewards}$ whenever $K_{\text{open}} \le \text{ReqCap}(N, C, P)$) ensures $\Delta I_c$ cancels **algebraically in ALL states** — including Turn 2+ under `SmoothCalendar` ($\Delta t < 20\text{ yr}$) when $K_{\text{open}} > \text{ReqCap}(N, C, P)$ (`6.48 / 5.69 / 5.35` across `dt = 20 / 5 / 1 yr` instead of collapsing `6.48 -> 0.45`) and after population dips (over-capitalized $N=1{,}600$ holds exact `8.04`).
  2. `civic stewards` (`LabourRequest`, `WorkforceRoles.Steward`): while `C_plan(T_amort)` governs durable `CivicCapital` (`civic development`) and `PriorityScore_civic` amortization, perishable current-turn `civic stewards` are sized strictly to **opening `C`** (matching Turn-$T$ $I_{\text{civic}}$ evaluation!).
     - Gate: `CivicGovernance.Enabled(w.Config) && c.IsVillage && c.CivicComplexity > 1e-12 && CivicGovernance.StewardAvailable(w, c) && civicBuild != null && c.CivicCapital < ReqCap(N, C, P) - Epsilon`, with `devStewards = Math.Max(0, ReqStewards(N, C, P, has3A, P_env) - coreStewards) > Epsilon`, identical `PriorityScore` to `civic development`, placed immediately after `civic development` in `alloc.Requests` (`OrderByDescending(PriorityScore).ThenBy(IndexOf)` guarantees `civicBuild.Granted` is finalized first).
     - Inside the Development loop (`DemandControl.cs:1596–1615`), cap `wanted` with null-safe `civicBuild?.Granted ?? 0`:
       $$\text{activeTarget} = C > 10^{-12} \;?\; \min\!\left(1,\; \frac{c.\text{CivicCapital} + (\text{civicBuild?.Granted} ?? 0)}{\max(\epsilon,\; \text{ReqCap}(N, C, P))}\right)\text{ReqStewards}(N, C, P, \text{has3A}, P_{\text{env}}) \;:\; 0$$
       $$\text{wanted} = \max(0,\; \text{activeTarget} - \text{coreStewards})$$
     - Result: Turn 1 after Tier-1 unlock ($C = 0$) builds durable `CivicCapital` with `0` idle Stewards; `Close` relaxes $C > 0$ unconditionally; Turn 2 immediately staffs `coreStewards` matching $C$ with **0.00 idle Stewards** across `dt in {20, 5, 1, 1/365}`, and `K_open >= ReqCap(N, C, P)` automatically emits zero development Stewards (eliminating any `civicBuild == null` NRE or zero-`PriorityScore` gap).
  3. `sanitation development` (`CraftGood.SanitationConstruction`, Phase 2, gated by `CivicGovernance.SanitationEnabled(w.Config) && c.IsVillage && SanitationRecipesAvailable(w, c) && residents >= SanitationDevelopmentMinPopulation = 1200` and `w_avail = ForecastSanitationWaterFraction(w, c, years) > 0.05`):
     - Target $K_{s,\text{target}} = \text{ReqSanitCap}(N) \cdot w_{\text{avail}}$, $\Delta K_s = \max(0, K_{s,\text{target}} - c.\text{SanitationCapital})$, $\Delta I_s = \Delta K_s / \max(\epsilon, \text{ReqSanitCap}(N))$.
     - Annualized worker cost & `PriorityScore`:
       $$W_{\text{sanit,yr}} = \frac{\text{BundleRoleYears}(\text{SanitationConstruction}) \cdot \Delta K_s}{T_{\text{amort}}} + \text{BundleRoleYears}(\text{SanitationMaintenance}) \cdot \Delta K_s \cdot \delta_{\text{sanit}}$$
       $$\text{PriorityScore}_{\text{sanit}} = \frac{N \cdot d_{\text{disease}}(N) \cdot \alpha_d(C_{\text{plan}}, P) \cdot \Delta I_s \cdot (\text{SanitationDeathFoodYears} \cdot f_0)}{\max(\epsilon,\; W_{\text{sanit,yr}})}$$
- **Why Separate `CivicDeathFoodYears = 6.0` (`24.0 food/death`) and `SanitationDeathFoodYears = 15.0` (`60.0 food/death`):**
  With exact PR #49 Builder+HM bundle costs (`9.33` Builder-yr/unit Civic, `11.67` Builder-yr/unit Sanitation), $d_{\text{crowd}} = 3 \times d_{\text{disease}}$ at $N = 1{,}000$ while Sanitation's capital intensity per resident (`0.093`) is ~1.8× Tier-1 Civic's (`0.053`). A single scalar provably cannot simultaneously stay $< 7.20$ at $N = 600$ for Civic (requires $\le 6.3$) and exceed $7.20$ at $N = 2{,}500\text{–}3{,}400$ for Sanitation (requires $\ge 12\text{–}15$). Pinning `CivicDeathFoodYears = 6.0` and `SanitationDeathFoodYears = 15.0` (half of `PersonLifeYears = 30`, still well below the `88.9` lifetime annuity) hits every crossover cleanly and remains 100% `dt`-invariant under `SmoothCalendar`:

#### Verified Reconciled `PriorityScore` Table (Regular Tools, Aligned $P = P_{\text{env}}$, `has3A = false`; Civic Initial Build $M_{\text{build}} = 1$, Sanitation Active City $I_{\text{civic}} = 1, M_{\text{build}}(C, P, 1)$, Python)
| Population $N$ | Civic T1 ($P = -1 / 0 / +1$) | Civic T2 ($P = -1 / 0 / +1$) | Sanit T2 ($P = -1 / 0 / +1$) | Sanit T3 ($P = -1 / 0 / +1$) | Competition vs Food (`Wild=7.2`, `Fertile=7.0–7.7`, `Canal=7.2–8.6`, `Plough=9–12`) |
| ---: | ---: | ---: | ---: | ---: | --- |
| 300 | 3.40 / 2.19 / 1.62 | 2.38 / 1.55 / 1.16 | *(gated `<1200`)* | *(gated `<1200`)* | **Food wins 100%** (young villages protected) |
| 600 | **6.75 / 4.36 / 3.24** | **4.73 / 3.09 / 2.33** | *(gated)* | *(gated)* | **All Civic `< 7.20`** (protects small villages across all $P$) |
| 900 | 10.03 / 6.51 / 4.87 | 7.03 / 4.62 / 3.49 | *(gated)* | *(gated)* | Egalitarian T1 activates; Hybrid/Strat wait for 1k |
| **1,000** | **11.09 / 7.21 / 5.41** | 7.77 / 5.12 / 3.88 | *(gated)* | *(gated)* | **Hybrid T1 crosses `7.20` right at the 1,000 crowding wall!** |
| **1,600** | 17.13 / 11.33 / 8.64 | **12.00 / 8.04 / 6.20** | 5.20 / 5.95 / 7.02 | 5.85 / 6.75 / 8.10 | **Hybrid T2 crosses fertile fields at 1,600;** Sanit below food |
| 2,000 | 20.75 / 13.93 / 10.79 | 14.54 / 9.88 / 7.75 | 6.53 / **7.48** / 8.82 | **7.35 / 8.48 / 10.18** | Sanitation crosses `7.20` at 2,000 |
| 2,500 | 24.76 / 17.00 / 13.46 | 17.35 / 12.06 / 9.67 | **8.22 / 9.41 / 11.10** | **9.25 / 10.68 / 12.81** | Sanitation beats wild gathering, fertile fields & canals |
| **3,400** | 30.51 / 22.00 / 18.24 | 21.37 / 15.60 / 13.09 | **11.32 / 12.96 / 15.29** | **12.74 / 14.70 / 17.64** | **Sanitation beats Plough harvest — breaks the ~3,400 disease wall!** |
| 6k–10k | **31.8–52.5** | **22.8–37.7** | 20.7–49.3 | 23.3–56.9 | Fully funds urban institutions & sanitation whenever Survival+Protected allow |

- **`EquipmentTarget` Steward Exclusion (`DemandControl.cs:1840`):** Add `&& p.Key != WorkforceRoles.Steward` so Stewards never inflate craft tool targets.
- **`EffectiveFocus` Display Classification (UI/Hover only — zero effect on $P^*$):** `CivicFocus.Auto` shows *Communal* when $P_{\text{env}} \le -0.15$, *Centralized* when $P_{\text{env}} \ge +0.15$, else *Hybrid*; explicit `Communal`/`Centralized` shows itself.

#### B. Generalized `ProductionOrder.HasInfrastructure` Contract
Replace every PR #49 `order?.Irrigation != null` / `== null` guard with `order?.HasInfrastructure == true` / `!= true` (where `ProductionOrder.HasInfrastructure => Irrigation != null || Civic != null || Sanitation != null`, with `CivicOrder` and `SanitationOrder` deep-copied in `ProductionOrder.CopyForPreparation()` at `ProductionDemand.cs:1004–1013`):
- `NeolithicProduction.cs`: `CraftGood` (`:56`), `Recipes` (`:316–325`), `Stock` (`:345`), `Priority` (`:364`), `RecipeAvailable` (`:371`, feature + precedent ONLY — never `c.IsVillage`), `OwnDemand` (`:522–525`, dispatching all 6 infrastructure goods via unified `IsInfrastructureGood`/`InfrastructureDemand` and adding `CivicGovernance.InfrastructureHousingMaterials`), `DemandWithoutToolReplacement` (`:607, 780–785`, forwarding `order` alongside skipping `FlatHousingMaterialLimit` at `:668, 708` when `order?.HasInfrastructure == true`), `Produce` (`:975, 985, 1003` unified `IsInfrastructureGood`/`InfrastructureDemand`, `:1044`, and explicit no-op `case` arms for `CraftGood` 13–16 in `switch (recipe.Good)` at `:1053` so `Produce` never throws `ArgumentOutOfRangeException`), and `ResolveCampNeolithic` (atomic `CivicGovernance.CommitCapitals` **outside** `if (CultivationEconomy.EconomyEnabled(w, c))`, immediately after line `:1150` and before `if (resolveNeeds)` at `:1152`).
- `MaterialEconomy.cs`: `Forecast` (`:776, 779, 798`).
- `GoodsStorage.cs`: `PlannedUse` (`:155`, using `NeolithicProduction.IsInfrastructureGood(r.Good)`).
- `TurnPlanning.cs`: `Prepare` (`:277`) and `ReconcileInfrastructure` (`:460–475`, looping over all 6 infrastructure `CraftGood`s).
- `DemandControl.cs`: `EquipmentTarget` (`:1840`), `production.Civic` & `production.Sanitation` creation (`:1888–1890`).
- `SpecialistProductivity.cs`: `CraftRate` (`:17–25`) applying `M_build` for specialist Builder on `IsInfrastructureGood(recipe.Good)`.

---

### Spec Choice #8 — `ImmigrationPolicy` & Airtight Upper-Bound Soundness Proofs (`--bound-audit`)

1. **Shared `Immigration.PolicyFor(w, source, destination, distance, penalty)` (`Immigration.cs:246, 486`):**
   Used identically by `Immigration.Decide` and `Immigration.JointIndex.Decide`. Returns exact legacy `SocialRelocationPolicy` when `!CivicGovernance.Enabled(w.Config)`. Otherwise:
   - `Neutral (0)`: $\Delta K_{\text{pol}} = 0$, $\text{incTol} = \text{cfg.FissionDaughterMargin}$ (`0.05`).
   - `Open (1)`: $\Delta K_{\text{pol}} = -0.03$, $\text{incTol} = 0.05 + 0.02 = 0.07$.
   - `Restricted (2)`: $\Delta K_{\text{pol}} = +0.15$, $\text{incTol} = 0.05 \times 0.30 = 0.015$.
   - Voluntary $P$-pull on `destination`: $\Delta K_P = 0.025 \cdot P_{\text{dest}} \cdot C_{\text{dest}} \cdot I_{\text{civic,dest}}$.
   - Exit mismatch push on `source`: $\Delta K_{\text{exit}} = -0.030 \cdot M_{\text{exit}}(\text{source})$.
   - `privateCost = Math.Max(0, PrivateCost(cfg, distance, source, destination, penalty) + deltaK_pol + deltaK_P + deltaK_exit)`.
2. **Daughter `Steward` Handling (`WorkforceTransfer.Split` `SocialRelocation.cs:416`, `Fission.SplitWorkforce` `Fission.cs:1126`):**
   When `daughterFarmersBecomeGatherers == true`, convert departing `Steward`s to `Gatherer` alongside `Farmer`s (founding daughters have `CivicCapital = 0`, so retained `Steward`s would yield $I_{\text{civic}} = 0$ while starving founding food gathering). On `Immigration` (`daughterFarmersBecomeGatherers == false`), `Steward`s retain their role cleanly.
3. **Theorem 1 — Soundness of `SocialRelocationForecastCache.UtilityUpperBound` (`SocialRelocation.cs:830–872`):**
   *Preconditions:* Both `UtilityUpperBound` and `Forecast` call the same `f = Food(state, opportunity)`, evaluate `civicMult = CohesionMultiplier(w, c)`, `eqMult = QoLEqualityMultiplier(w, c)`, `crowding = CrowdingMitigation(w, c)`, `disease = DiseaseMitigation(w, c)`, `additional = AdditionalMortality(w, c)` strictly on **`c`** (invariant to `MaterialEconomy.Forecast`, which runs with `order = null` and produces zero civic/sanitation capital), and return `+Infinity` if any weight or scale is non-positive/non-finite.  
   *Proof:* Since $W \ge 0, M \ge 0$, and $J p_{\text{job}}$ is identical in bound and forecast:
   - $Q_{\text{exact}} = \operatorname{clamp}(f.\text{Nutrition} \cdot W_{\text{cov}} \cdot \text{prot}(c_{\text{cloth}}, h_{\text{cov}}) \cdot S(H_{\text{share}}, N, m_{\text{civic}}) \cdot m_{\text{eq}}, 0, 1)$. Because $\text{prot}$ is affine on $[0, 1]^2$, $S(H, N, m_{\text{civic}})$ is monotone on $H \in [0, 1]$ (with $m_{\text{civic}} > 0$), $m_{\text{eq}} \ge 0$, and $\operatorname{clamp}(\cdot, 0, 1)$ is monotone: $Q_{\text{exact}} \le \operatorname{clamp}(f.\text{Nutrition} \cdot W_{\text{cov}} \cdot \max(0, \text{prot}_{\max}) \cdot \max(S(0), S(1)) \cdot m_{\text{eq}}, 0, 1) = Q_{\text{upper}}$.
   - In `Rates`, $d(x, y) = d_{\text{exp},0}\max(0, 1 - r_C x - r_H y) + K_{\text{fixed}}$ where $K_{\text{fixed}}$ is identical in bound and forecast and $(x, y) \mapsto \max(0, 1 - r_C x - r_H y)$ attains its global minimum on $\{0, 1\}^2$. Hence $d_{1,\min} \le d_1$, $d_{2,\min} \le d_2$, and $R_{\text{lower}} = 1 - e^{-\max(0, d_{1,\min} t_1 + d_{2,\min}(T - t_1))} \le R_{\text{exact}}$.
   - Therefore $U_{\text{bound}} \ge U_{\text{exact}}$ and `MoverBound >= decision.MoverUtilityChange` unconditionally. $\blacksquare$
4. **Theorem 2 — Soundness of `Fission.Evaluate` Preselection (`Fission.cs:649–673`):**
   Use shared `threshold = EffectiveMoverThreshold(w, camp)` (matching `Fission.RelocationAt`) and compute `daughterQoLUpper = Numbers.Clamp(Math.Max(SocialCohesion(n, 0, candidateSize), SocialCohesion(n, 1, candidateSize)) * CivicGovernance.QoLEqualityMultiplierFor(cfg, daughterC, daughterP, 0), 0, 1)` (where `QoLEqualityMultiplierFor(..., 0)` equals `1.0` for founding daughters with `CivicCapital = 0 => I_civic = 0`, so `daughterQoLUpper` reduces to `SocialCohesion(n, 1, candidateSize) <= 1.0`, matching `Fission.cs:660`, while defensively generalizing if future phases ever grant founding capital). Since founding daughters have `CivicCapital = SanitationCapital = 0`, $I_{\text{civic}} = I_{\text{sanit}} = 0 \implies m_{\text{civic}} = 1, M_c = M_d = 1, m_{\text{eq}} = 1$, $Q_{d,\text{exact}} \le Q_{d,\text{upper}}$, $R_{d,\text{lower}} \le R_{d,\text{exact}}$, and `FissionSearchFraction * threshold <= threshold`, no valid fission candidate can ever be pruned. $\blacksquare$
   *(Both theorems verified in Python across 50,000 randomized corner/interior trials: **max violation `+0.000e+00`**.)*

---

### Spec Choice #9 — Orders, Observations (`SchemaVersion = 23`), AI Policy, `TurnPlanning` & UI

1. **Append-only `OrderKind` (`Model.cs:11`):** `ImmigrationPolicy = 8`, `CivicFocus = 9`.
   - Gated in BOTH `OrderRules.Validate` (`ActionContract.cs:22`) and `LegalActions.Build` (`ActionContract.cs:82`) on `o.CivicGovernanceEnabled` (populated identically in `Build` and `BuildForValidation`), keeping legacy action masks bit-identical when `Civic == null`.
2. **Workforce (`Workforce.cs:20–24`):**
   - `public const string Steward = "Steward";` added to `WorkforceRoles.All` ONLY (NOT `Specialists`, NOT `Extractors`).
   - Adaptive UI label `WorkforceRoles.Label(string role, Camp camp)`: `"Council Arbiter"` ($P \le -0.30$), `"Temple Scribe"` ($P \ge +0.30$), `"Civic Steward"` ($-0.30 < P < +0.30$), `"Steward"` (`camp == null`).
   - `EconomicConstraints.StewardEnabled` (`EconomicIntents.cs:99`) populated from `SettlementObservation.StewardEnabled` (`CivicGovernance.StewardAvailable(w, c)` — true when `Enabled && HasCivicPrecedent(CeremonialIntegration)`), **AND** explicitly set `StewardEnabled = CivicGovernance.StewardAvailable(world, camp)` in `TurnPlanning.cs:363–371` (`AutomaticIntent`'s direct `new EconomicConstraints` construction — otherwise `EconomicIntents.Validate` throws `InvalidOperationException` on every automated Steward plan!). `EconomicIntents.Validate` rejects `Steward` assignments when `!c.StewardEnabled`.
3. **Observations (`Agents.cs:282`, `PlanningIndex.cs:93`, `ObservationContract.cs:33`):**
   - Bump `AgentObservation.SchemaVersion = 23`.
   - Add `CivicGovernanceEnabled`, `SanitationEnabled` on `AgentObservation`/`ObservationDto`.
   - Add strictly **owner-only** fields on `SettlementObservation` / `ObservedSettlementDto` (zero rival leakage): `StewardEnabled` (populated in both full & validation views), `StewardPopulation`, `CivicComplexity`, `CivicStratification`, `LastEnvironmentalComplexity`, `LastEnvironmentalStratification` ($O(1)$ fog-safe!), `CivicCapital`, `CivicCoverage`, `SanitationCapital`, `SanitationCoverage`, `SanitationWaterRatio`, `ImmigrationPolicy`, `CivicFocus`, `CivicPrecedents`.
   - Update ALL **6** headless test files directly asserting `22` (verified via grep on `features/civic-governance`):
     `HousingMaterialRecipeChecks.cs:257,262`, `EconomicTransitionChecks.cs:89`, `PastoralCraftChecks.cs:357`, `FishingObservationChecks.cs:99`, `ContactKnowledgeChecks.cs:257–258, 263`, `DemographyChecks.cs:227`.
4. **Multi-Policy `TurnPlanning.Prepare`, `AutomaticIntent` & `TurnPlanner.Draft` (`TurnPlanning.cs`):**
   - `IsPolicyOrder(kind)` matches `FissionPolicy`, `ImmigrationPolicy`, `CivicFocus`.
   - Conflict check (`:225–234`): allow at most 1 ordinary order + at most 1 order per distinct policy `OrderKind` (preserving `FissionChecks.cs:1249–1252`).
   - **`acceptedActions` filter (`TurnPlanning.cs:243`):** MUST change `o.Kind != OrderKind.FissionPolicy` to `!IsPolicyOrder(o.Kind)` so submitting `ImmigrationPolicy`/`CivicFocus` alongside an ordinary order (or both policy orders together) never throws duplicate-key `ArgumentException` in `.ToDictionary(o => o.Entity)`!
   - **`AutomaticIntent` constraints (`TurnPlanning.cs:363–371`):** set `StewardEnabled = CivicGovernance.StewardAvailable(world, camp)` on the direct `new EconomicConstraints` instance.
   - **`TurnPlanner.Draft` (`TurnPlanning.cs:422–455`):** extract AI-submitted policy orders from `orders`, pass them into `PlanningCampAt` / `AutomaticIntent`, and store effective `(FissionPolicy, ImmigrationPolicy, CivicFocus)` on `DraftIntent` (`:155–163, :448`). In `Prepare` (`:271–281`), reuse `cached` ONLY when all three match effective policies, otherwise recompute `AutomaticIntent` with same-turn policy overlay in `PlanningCampAt` (also passed to `ComputeNeeds`).
   - `TurnResolver.Resolve` (`:86–90`) commits all three policy `OrderKind`s in canonical `Entity` order before `Economy`, `Trade.Settle`, and `ResolveEcology`.
5. **`HeuristicTribePolicy` (`Agents.cs`):**
   - When `!o.CivicGovernanceEnabled`: emit **ZERO** `FissionPolicy`/`ImmigrationPolicy`/`CivicFocus` orders (preserving `FissionChecks.cs:1255` bit-identically).
   - When `o.CivicGovernanceEnabled`: keep `FissionPolicy = Neutral` and `CivicFocus = Auto` (pure endogenous geographic divergence); adjust `ImmigrationPolicy` strictly from owner-only observation (`Open` when $P \le -0.35$, low crowding mortality $< 0.025$, nutrition $\ge 0.90$; `Restricted` when $P \ge +0.45$, crowding mortality $\ge 0.035$; `Neutral` hysteresis band).
6. **Presentation (`OpeningGame.cs`, `OpeningDetails.cs`, `OpeningPlanning.cs`, `OpeningVillageInspector.cs`, `HoverDetails.cs`, `OpeningTopographySmoke.cs`):**
   - `OpeningGame.cs`: add `plannedPolicies` keyed by `(Entity, OrderKind)` so policy orders never overwrite `planned[camp.Id]`; merge `planned.Values` + `plannedPolicies.Values` into `playerOrders` inside `EndTurn` (`OpeningGame.cs:485`); clear on load/gen/`FinishTurn`/`OpeningTopographySmoke`; extend `Describe(Order)`; pass merged queued orders in `OpeningDetails.cs:93` (`DrawHover`) and `OpeningPlanning.cs:224–226` (`CommitPreview`); add `--anthropocene-civic-governance` selecting `"CivicGovernanceOpeningConfig"` + mandatory `JsonUtility` regex null-guard.
   - `OpeningVillageInspector.cs`: hide `Steward` when disabled, render `WorkforceRoles.Label(role, camp)`, add **CIVIC & GOVERNANCE** panel showing $(C, P)$, $(C_{\text{env}}, P_{\text{env}})$, alignment friction, $I_{\text{civic}}$, Phase-2 $I_{\text{sanit}}$ / $W_{\text{sanit}}$, precedents + progress, and 3-way toggles for `FissionPolicy`, `ImmigrationPolicy`, `CivicFocus`.
   - `HoverDetails.cs`: append one owner-only `CIVIC · ...` summary line for owned settlements when `o.CivicGovernanceEnabled`.

---

### Spec Choice #10 — Phase-2 `SanitationCapital` Conserved Multi-Tile Freshwater Priority

Because `IrrigationEconomy.DrinkingClaims` (`0.10 * N / yr`) places all human drinking on a single tile (`FreshwaterAccess.Source`), it saturates a max-flow river tile (`258.5 / yr`) at $N = 2{,}585$. Therefore, Phase-2 sanitation draws proportionally across **all eligible local freshwater tiles in `w.Map.Radius(c.Tile, 1)`** (`WaterBody != Saltwater && FreshwaterSource != None`), mirroring `ManagedHerdLocalClaims`, inside an airtight **3-tier conserved water order** in `TurnHorizonResources.cs:304–316`:

1. **Tier 1 (Essential Biological — highest priority):** `Herd` + `Drinking` (`0.10 * N * years` on `FreshwaterAccess.Source`; when `SanitationEnabled && w.Config.Agriculture == null`, protect the same `0.10 * N * years` drinking reservation in `AllocateSanitationWater` so human drinking ALWAYS comes strictly first).
2. **Tier 2 (Municipal Sanitation — `CivicGovernance.AllocateSanitationWater`, inserted between lines `309` and `310`):**
   - At the start of `AllocateSanitationWater`, explicitly reset `c.SanitationWaterDelivered = 0; c.SanitationWaterRatio = 0;` for **ALL** camps so camps that lose sanitation or village status can never retain stale water ratios.
   - For each active village $c$ in canonical `c.Id` order with `c.SanitationCapital > 1e-12`:
     $$\text{SanitWaterNeed}(c) = c.\text{Population} \cdot \min\!\left(1,\; \frac{c.\text{SanitationCapital}}{\max(\epsilon,\; \text{ReqSanitCap}(c.\text{Population}))}\right) \cdot \text{SanitationWaterPerPersonYear} \cdot \text{years}$$
     with **`SanitationWaterPerPersonYear = 0.02`** (`60/yr` at 3k, `100/yr` at 5k, `200/yr` at 10k).
   - For each eligible tile $s \in \text{Radius}(c.\text{Tile}, 1)$, compute positive residual $R_s = \max(0, \text{IrrigationEconomy.WaterEnvelope}(w, s, \text{start}, \text{days}) - \text{tier1}[s])$.
   - Camp $c$ proposes $p_{c,s} = \min\!\big(R_s,\; \text{SanitWaterNeed}(c) \cdot R_s / \sum_u R_u\big)$. Across competing camps on the same tile $s$, scale all proposals proportionally by $\min(1, R_s / \sum_c p_{c,s})$ in canonical `(tile, campId)` order.
   - Emit `new Demand { Tile = s, Entity = c.Id, Kind = "SanitationWater", Freshwater = withdrawal }` into `claims`, materialize/activate/touch tile $s$, append `SanitationWaterReceipt` to `record.SanitationWater`, set `c.SanitationWaterDelivered` and `c.SanitationWaterRatio = Clamp(delivered / SanitWaterNeed, 0, 1)`.
3. **Tier 3 (Agricultural Irrigation — `IrrigationEconomy.Allocate` at line `310`):**
   - `IrrigationEconomy.cs:645` ALREADY deducts all `d.Freshwater > 0 && d.Kind != "Irrigation"` from `available` — so `Kind == "SanitationWater"` is automatically prioritized ahead of irrigation canals!
   - Extend `TurnHorizonResources.cs:538` to recognize `transfer.Kind == "SanitationWater"` as a water-only receipt debited once at its source (and optionally cross-check realized `transfer.Freshwater` against `SanitationWaterDelivered` for end-to-end conservation verification).
   - **Forecast parity:** Append `CivicGovernance.ForecastSanitationWaterClaims(w, years, ...)` (fog-filtered by `tileKnown`) in `IrrigationEconomy.EssentialForecastClaimsForSources` (`:333–419`) and `EssentialForecastClaims` (`:420–435`). Similarly, `ForecastSanitationWaterFraction(w, c, years)` (called by `DemandControl` for `w_avail > 0.05`) MUST enforce the same `0.10 * N * years` human drinking reservation when `SanitationEnabled && w.Config.Agriculture == null` so dry oases without `Agriculture` never build unusable dry sewers.
4. **Verified Habitat Coverage ($N = 3\text{k} / 5\text{k} / 10\text{k}$, Python):**
   - **River Valley (2 big rivers, `258.5/yr` each):** `W_sanit = 1.000 / 1.000 / 1.000` (58.5/yr left for canals even at 10k).
   - **Small River (2 small rivers, `129.2/yr` each):** `W_sanit = 1.000 / 1.000 / 0.646`.
   - **Lake Shore (2 lakes, `206.8/yr` each):** `W_sanit = 1.000 / 1.000 / 1.000`.
   - **Temperate Rain-Fed Plain (6 RainPools, `43.1/yr` each):** `W_sanit = 1.000 / 1.000 / 1.000`.
   - **Arid Desert Oasis (1 Oasis, `42.2/yr` + 6 dry):** `W_sanit = 0.000 / 0.000 / 0.000` (physically water-limited; `DemandControl`'s `w_avail > 0.05` gate prevents building unusable dry sewers!).

---

### Spec Choice #11 — Unified `CivicGovernanceSettings`, Profile JSON, Turn Order & M1–M5 Plan

#### A. Complete `CivicGovernanceSettings` Parameter Table & `Validate()` Contract
Every `double` field is rejected by `Validate()` if `NaN`, `Infinity`, or outside its stated domain.

| C# Field Name | Default | Valid Range | Role |
| --- | ---: | --- | --- |
| `Enabled` | `false` | `bool` | Master Phase-1 opt-in switch (`true` in profile JSON) |
| `SanitationEnabled` | `false` | `bool` | Independent Phase-2 switch (`true` in profile JSON) |
| `Tier1ComplexityCeiling` | `0.30` | $(0, C_2)$ | Tier 1 (`CeremonialIntegration`) $C_{\max}$ |
| `Tier2ComplexityCeiling` | `0.75` | $(C_1, C_3)$ | Tier 2 (`CivicOrganization`) $C_{\max}$ |
| `Tier3ComplexityCeiling` | `1.00` | $(C_2, 1]$ | Tier 3A (`TokensAndAccounting`) $C_{\max}$ |
| `ComplexityRelaxationRate` | `0.025` | $> 0$ | $\lambda_C$ ($\text{yr}^{-1}$) |
| `StratificationRelaxationRate` | `0.020` | $> 0$ | $\lambda_P$ ($\text{yr}^{-1}$) |
| `TradeVolumeRelaxationRate` | `0.025` | $> 0$ | $\lambda_{\text{trade}}$ ($\text{yr}^{-1}$) |
| `ComplexityCrowdingScale` | `0.64` | $> 0$ | Denominator in $1 - e^{-\rho_{\text{crowd}} / 0.64}$ |
| `FissionPolicySteeringShift` | `0.15` | $[0, 1]$ | $\Delta P_{\text{fission}}$ (`Encourage` $-$ / `Discourage` $+$) |
| `ImmigrationPolicySteeringShift` | `0.15` | $[0, 1]$ | $\Delta P_{\text{imm}}$ (`Open` $-$ / `Restricted` $+$) |
| `CivicFocusSteeringShift` | `0.45` | $[0, 1]$ | $\Delta P_{\text{focus}}$ (`Communal` $-$ / `Centralized` $+$; `Auto` $= 0$) |
| `DaughterComplexityRetention` | `0.50` | $[0, 1]$ | $\gamma_C$ on fission |
| `DaughterStratificationRetention` | `0.50` | $[0, 1]$ | $\gamma_P$ on fission |
| `DaughterFrontierEgalitarianShift` | `0.15` | $[0, 1]$ | $\delta_{P,\text{frontier}}$ subtracted on fission |
| `MinCrowdingMitigation` | `0.05` | $(0, 1]$ | Hard positive floor $M_{c,\min} > 0$ |
| `MinDiseaseMitigation` | `0.15` | $(0, 1]$ | Hard positive floor $M_{d,\min} > 0$ |
| `CrowdingMitigationBase` | `0.925` | $[0, 1]$ | Base $\alpha_c$ coefficient |
| `CrowdingMitigationStratBonus` | `0.025` | $[0, 0.5]$ | Stratified $P C$ term in $\alpha_c$ |
| `CrowdingShapeRate` | `5.8` | $> 0$ | Rate in $f_c(C)$ |
| `CrowdingShapeExponent` | `1.65` | $> 0$ | Exponent in $f_c(C)$ |
| `DiseaseMitigationBase` | `0.735` | $[0, 1]$ | Base $\alpha_d$ coefficient |
| `DiseaseMitigationStratBonus` | `0.075` | $[0, 0.5]$ | Stratified $P$ term in $\alpha_d$ |
| `DiseaseHalfScale` | `0.60` | $> 0$ | Half-scale in $f_d(C) = (1 + h) C / (C + h)$ |
| `CohesionLinearGain` | `1.95` | $\ge 0$ | Linear $C$ term in $g_{\text{civic}}$ |
| `CohesionUrbanGain` | `4.45` | $\ge 0$ | Urban $C^{4.2}$ term in $g_{\text{civic}}$ |
| `CohesionUrbanExponent` | `4.2` | $\ge 1$ | Exponent in $g_{\text{civic}}$ |
| `CohesionStratFactor` | `0.32` | $[0, 0.9]$ | Stratified factor $(1 + 0.32 P C^{1.5})$ |
| `EgalitarianStewardRate` | `0.045` | $> 0$ | Base Steward share at $P = -1, C = 1$ |
| `StratifiedStewardRate` | `0.105` | $\ge$ Egal | Base Steward share at $P = +1, C = 1$ |
| `StewardScalingKnee` | `3500` | $> 0$ | Smooth superlinear knee population |
| `EgalitarianStewardExponent` | `2.60` | $\ge 1$ | Pre-3A asymptotic exponent at $P = -1$ |
| `StratifiedStewardExponent` | `1.15` | $\ge 1$ | Pre-3A asymptotic exponent at $P = +1$ |
| `AccountingExponentRetention` | `0.20` | $(0, 1]$ | Tier-3A retention of excess $(\beta - 1)$ |
| `AlignmentFrictionRate` | `0.35` | $\ge 0$ | $\kappa$ in $1 + \kappa \lvert P - P_{\text{env}} \rvert$ |
| `EgalitarianCapitalPerPerson` | `0.12` | $> 0$ | `ReqCap` units/person at $P = -1, C = 1$ |
| `StratifiedCapitalPerPerson` | `0.24` | $\ge$ Egal | `ReqCap` units/person at $P = +1, C = 1$ |
| `CivicCapitalDecayPerYear` | `0.018` | $> 0$ | $\delta_{\text{civic}}$ ($\text{yr}^{-1}$) |
| `CivicBuildBuilderYears` | `4.0` | $> 0$ | `CivicConstruction` Builder-yr/unit |
| `CivicBuildHousingMaterials` | `2.0` | $\ge 0$ | `CivicConstruction` HM/unit |
| `CivicMaintainBuilderYears` | `2.0` | $> 0$ | `CivicMaintenance` Builder-yr/unit |
| `CivicMaintainHousingMaterials` | `1.0` | $\ge 0$ | `CivicMaintenance` HM/unit |
| `SanitationCapitalPerPerson` | `0.16` | $> 0$ | `ReqSanitCap` units/person |
| `SanitationCapitalDecayPerYear` | `0.025` | $> 0$ | $\delta_{\text{sanit}}$ ($\text{yr}^{-1}$) |
| `SanitationBuildBuilderYears` | `5.0` | $> 0$ | `SanitationConstruction` Builder-yr/unit |
| `SanitationBuildHousingMaterials` | `2.5` | $\ge 0$ | `SanitationConstruction` HM/unit |
| `SanitationMaintainBuilderYears` | `2.5` | $> 0$ | `SanitationMaintenance` Builder-yr/unit |
| `SanitationMaintainHousingMaterials` | `1.0` | $\ge 0$ | `SanitationMaintenance` HM/unit |
| `SanitationWaterPerPersonYear` | `0.02` | $> 0$ | Phase-2 freshwater need / covered person-yr |
| `QoLEqualityLinear` | `0.09` | $[0, 0.5]$ | $q_1$ in $1 + (-q_1 P + q_2 P^2) C I_{\text{civic}}$ |
| `QoLEqualityQuadratic` | `0.01` | $[0, 0.25]$ | $q_2$ (`Validate()` requires $q_1 = q_2 = 0$ or $q_1 > 2 q_2$ so strictly decreasing or cleanly zero-ablated) |
| `StratifiedPublicWorksBonus` | `0.35` | $\ge 0$ | Max $M_{\text{build}} - 1$ at $P = +1, C = I = 1$ |
| `StratifiedGranaryStorageBonus` | `0.50` | $\ge 0$ | Max $M_{\text{storage}} - 1$ at $P = +1, C = I = 1$ |
| `FissionStratificationResistance` | `0.045` | $[0, 0.25]$ | $\Delta\theta_P$ on fission threshold |
| `FissionExitMismatchReduction` | `0.060` | $[0, 0.25]$ | $\Delta\theta_{\text{exit}}$ on open-frontier mismatch |
| `OpenImmigrationCostReduction` | `0.030` | $[0, 0.5]$ | `Open` PrivateCost reduction |
| `OpenIncumbentToleranceBonus` | `0.020` | $[0, 0.5]$ | `Open` incumbent tolerance bonus |
| `RestrictedImmigrationCostPenalty` | `0.150` | $[0, 1.0]$ | `Restricted` PrivateCost penalty |
| `RestrictedIncumbentToleranceScale` | `0.30` | $[0, 1]$ | `Restricted` incumbent tolerance multiplier |
| `StratificationImmigrationFriction` | `0.025` | $[0, 0.25]$ | $\Delta K_P$ per $P C I_{\text{civic}}$ |
| `ExitMismatchImmigrationReduction` | `0.030` | $[0, 0.25]$ | $\Delta K_{\text{exit}}$ per $M_{\text{exit}}$ |
| `Tier1PracticeThreshold` | `40.0` | $> 0$ | `CeremonialIntegration` threshold (yr) |
| `Tier2PracticeThreshold` | `80.0` | $> 0$ | `CivicOrganization` threshold (yr) |
| `Tier3APracticeThreshold` | `120.0` | $> 0$ | `TokensAndAccounting` threshold (yr) |
| `Tier3BPracticeThreshold` | `100.0` | $> 0$ | `PlannedQuartersAndDrainage` threshold (yr) |
| `TokensTradeRangeBonus` | `2` | $[0, 10]$ | Extra hexes in `Trade.Plan` with Tier 3A |
| `TokensTradeRequisitionBonus` | `2` | $[0, 10]$ | Extra requisitions in `Trade.Plan` with Tier 3A |
| `TokensMarginReduction` | `0.25` | $[0, 0.9]$ | Per-side margin reduction in `Trade.Respond` |
| `PrecedentDiffusionPerTrade` | `0.0` | $\ge 0$ | Inert trade diffusion hook (default `0`) |
| `CivicDeathFoodYears` | `6.0` | $> 0$ | `DemandControl` Civic `PriorityScore` life-food-yr |
| `SanitationDeathFoodYears` | `15.0` | $> 0$ | `DemandControl` Sanitation `PriorityScore` life-food-yr |
| `DevelopmentMinPopulation` | `300` | $\ge 1$ | Minimum residents for Civic Development request |
| `SanitationDevelopmentMinPopulation` | `1200` | $\ge 1$ | Minimum residents for Sanitation Development request |

#### B. `Assets/_Anthropocene/Resources/CivicGovernanceOpeningConfig.json` (Created in M2)
Exact copy of `Assets/_Anthropocene/Resources/AgriculturalTechnologyOpeningConfig.json` (`OpeningConfig` + `SmoothCalendar` + `Agriculture`) plus ONE top-level `"Civic"` object matching the unified table above **100% identically** (`"Enabled": true`, `"SanitationEnabled": true`, and every numeric parameter equal to its exact C# default). `OpeningConfig.json` and `AgriculturalTechnologyOpeningConfig.json` remain untouched:

```json
  "Civic": {
    "Enabled": true,
    "SanitationEnabled": true,
    "Tier1ComplexityCeiling": 0.30,
    "Tier2ComplexityCeiling": 0.75,
    "Tier3ComplexityCeiling": 1.00,
    "ComplexityRelaxationRate": 0.025,
    "StratificationRelaxationRate": 0.020,
    "TradeVolumeRelaxationRate": 0.025,
    "ComplexityCrowdingScale": 0.64,
    "FissionPolicySteeringShift": 0.15,
    "ImmigrationPolicySteeringShift": 0.15,
    "CivicFocusSteeringShift": 0.45,
    "DaughterComplexityRetention": 0.50,
    "DaughterStratificationRetention": 0.50,
    "DaughterFrontierEgalitarianShift": 0.15,
    "MinCrowdingMitigation": 0.05,
    "MinDiseaseMitigation": 0.15,
    "CrowdingMitigationBase": 0.925,
    "CrowdingMitigationStratBonus": 0.025,
    "CrowdingShapeRate": 5.8,
    "CrowdingShapeExponent": 1.65,
    "DiseaseMitigationBase": 0.735,
    "DiseaseMitigationStratBonus": 0.075,
    "DiseaseHalfScale": 0.60,
    "CohesionLinearGain": 1.95,
    "CohesionUrbanGain": 4.45,
    "CohesionUrbanExponent": 4.2,
    "CohesionStratFactor": 0.32,
    "EgalitarianStewardRate": 0.045,
    "StratifiedStewardRate": 0.105,
    "StewardScalingKnee": 3500,
    "EgalitarianStewardExponent": 2.60,
    "StratifiedStewardExponent": 1.15,
    "AccountingExponentRetention": 0.20,
    "AlignmentFrictionRate": 0.35,
    "EgalitarianCapitalPerPerson": 0.12,
    "StratifiedCapitalPerPerson": 0.24,
    "CivicCapitalDecayPerYear": 0.018,
    "CivicBuildBuilderYears": 4.0,
    "CivicBuildHousingMaterials": 2.0,
    "CivicMaintainBuilderYears": 2.0,
    "CivicMaintainHousingMaterials": 1.0,
    "SanitationCapitalPerPerson": 0.16,
    "SanitationCapitalDecayPerYear": 0.025,
    "SanitationBuildBuilderYears": 5.0,
    "SanitationBuildHousingMaterials": 2.5,
    "SanitationMaintainBuilderYears": 2.5,
    "SanitationMaintainHousingMaterials": 1.0,
    "SanitationWaterPerPersonYear": 0.02,
    "QoLEqualityLinear": 0.09,
    "QoLEqualityQuadratic": 0.01,
    "StratifiedPublicWorksBonus": 0.35,
    "StratifiedGranaryStorageBonus": 0.50,
    "FissionStratificationResistance": 0.045,
    "FissionExitMismatchReduction": 0.060,
    "OpenImmigrationCostReduction": 0.030,
    "OpenIncumbentToleranceBonus": 0.020,
    "RestrictedImmigrationCostPenalty": 0.150,
    "RestrictedIncumbentToleranceScale": 0.30,
    "StratificationImmigrationFriction": 0.025,
    "ExitMismatchImmigrationReduction": 0.030,
    "Tier1PracticeThreshold": 40.0,
    "Tier2PracticeThreshold": 80.0,
    "Tier3APracticeThreshold": 120.0,
    "Tier3BPracticeThreshold": 100.0,
    "TokensTradeRangeBonus": 2,
    "TokensTradeRequisitionBonus": 2,
    "TokensMarginReduction": 0.25,
    "PrecedentDiffusionPerTrade": 0.0,
    "CivicDeathFoodYears": 6.0,
    "SanitationDeathFoodYears": 15.0,
    "DevelopmentMinPopulation": 300,
    "SanitationDevelopmentMinPopulation": 1200
  }
```

#### C. Normative Close-Phase Pass Order (`TurnResolver.cs:192–259`)
`Ownership.Bind` $\to$ `BreakthroughBound` $\to$ `TechnologyDiscovery.Resolve` $\to$ `AgriculturalTechnology.Close` $\to$ **`CivicGovernance.Close`** *(1. Trade EMA, 2. Practice & Precedents, 3. Local Attractors & `dt`-Relaxation)* $\to$ `fieldContact.Resolve` $\to$ `Immigration.Resolve` $\to$ `Fission.Resolve` $\to$ `IrrigationEconomy.ReconcileOwnership` $\to$ `WelfareDiagnostics.Capture` $\to$ `Invariants` $\to$ `ContactKnowledge.EndTurn` $\to$ `History.Add` / `Turn++` $\to$ `SmoothCalendar.CommitNextBoundary`.

#### D. Companion Unity `.meta` & `.gitignore` Checklist (M1–M5)
- **M2:** `Assets/_Anthropocene/Simulation/CivicGovernance.cs` + `CivicGovernance.cs.meta` (`MonoImporter`, fresh 32-hex GUID); `Assets/_Anthropocene/Resources/CivicGovernanceOpeningConfig.json` + `CivicGovernanceOpeningConfig.json.meta` (`TextScriptImporter`, fresh 32-hex GUID).
- **M5:** `Tools/CivicGovernanceSweep/Anthropocene.CivicGovernanceSweep.csproj` + `.gitignore` negation `!Tools/CivicGovernanceSweep/Anthropocene.CivicGovernanceSweep.csproj` (and `!Tools/CivicGovernanceE2E/Anthropocene.CivicGovernanceE2E.csproj` if retained in PR).

#### E. Phased Implementation Plan & Quantitative Verification Gates (M1–M5)

| Milestone | Scope & Exclusive Files | Mandatory Exit Gates |
| --- | --- | --- |
| **M1 (Phase 0)** | `Demography.cs`, `CoupledDemography.cs`, `GenerationalEconomy.cs`, `DemandControl.cs` (plumbing call sites), `SocialRelocation.cs`, `Tools/Headless/DemographyChecks.cs` | Bit-exact `(1,1,1,0)` parity on ALL PR #49 fixed checks; solver equilibrium tests match §5 Choice #4 table to `< 0.1%` (including `(0.05, 0.15)` floor); `SocialRelocation` `state.View` & `Daughter()` tests pass. |
| **M2 (Phase 1A)** | `CivicGovernance.cs` (+ `.meta`), `Model.cs`, `Societies.cs`, `Workforce.cs`, `SaveCodec.cs`, `ActionContract.cs`, `ObservationContract.cs`, `PlanningIndex.cs`, `Agents.cs`, `EconomicIntents.cs`, `EconomicPlanner.cs`, `TurnPlanning.cs`, `TurnResolver.cs`, `CivicGovernanceOpeningConfig.json` (+ `.meta`), `Tools/Headless/CivicGovernanceChecks.cs`, `Program.cs`, `Anthropocene.Headless.csproj`, and **all 6 `SchemaVersion == 23` test files** | All fixed checks + `--civic-only` pass; semigroup `dt`-invariance (`1×20y == 4×5y == 20×1y` to `< 1e-9`) for $(C, P)$, Trade EMA, and practice (including Tier-1 permanence ramp `0 -> 40 yr`); save/load roundtrip + legacy XML omission; `AgentContractChecks` (`BuildForValidation` parity, zero rival leakage, fog & player-ID invariance); `FissionChecks.cs:1255` preserved when `Civic == null`. |
| **M3 (Phase 1B)** | `CivicGovernance.cs`, `NeolithicProduction.cs`, `MaterialEconomy.cs`, `ProductionDemand.cs`, `DemandControl.cs`, `GoodsStorage.cs`, `SpecialistProductivity.cs`, `Trade.cs`, `Immigration.cs`, `Fission.cs`, `SocialRelocation.cs`, `TurnPlanning.cs`, `TurnResolver.cs`, `OpeningGame.cs`, `OpeningDetails.cs`, `OpeningVillageInspector.cs`, `OpeningPlanning.cs`, `OpeningTopographySmoke.cs`, `HoverDetails.cs`, `Tools/Headless/CivicGovernanceChecks.cs` | **Phase-1 ~3,000 gate:** settlements climb organically to ~2,800–3,400 (hitting disease ceiling naturally with `SanitationEnabled = false`); natural geographic divergence (rain-fed plains $P < -0.25$ vs circumscribed irrigated valleys $P > +0.25$); welfare-only QoL isolation test; tool & storage conservation tests; `--bound-audit` **0 violations**; famine shock yields Egalitarian dispersal vs Stratified reserve holdout without starving `Protected`. |
| **M4 (Phase 2)** | `CivicGovernance.cs`, `TurnHorizonResources.cs`, `IrrigationEconomy.cs`, `NeolithicProduction.cs`, `DemandControl.cs`, `TurnPlanning.cs`, `GenerationalEconomy.cs`, `SocialRelocation.cs`, `OpeningVillageInspector.cs`, `Tools/Headless/CivicGovernanceChecks.cs` | **Phase-2 5,000–10,000 gate:** conserved 3-tier freshwater priority (`Drinking/Herd > SanitationWater > Irrigation`) to `< 1e-7`; dry oasis $W_{\text{sanit}} = 0$ builds zero dry sewers; river/lake/temperate cities break ~3,400 wall to **5,000–10,000+** (Stratified peaking higher than Egalitarian; Egalitarian cheaper at 1k–4k); `--bound-audit` **0 violations**. |
| **M5 (Final)** | `Tools/CivicGovernanceSweep/*`, `.gitignore`, `Docs/CivicGovernance.md`, `Docs/CurrentSimulationContract.md`, `Docs/TurnResolutionPasses.md`, `AGENTS.md` | 100% E2E Tiers 1–4 pass (`Tools/CivicGovernanceE2E/`); Tier-5 adversarial hardening reaches 0 gaps; 3-arm matched 5-world sweep (seeds `11, 23, 47, 101, 1729`) + 80-tribe long run + 1/16/80/320 benchmark recorded; `Check-CurrentContract.py` exit 0; clean git branch (zero `.agents/`, all `.meta` present), pushed & PR opened `--base features/plough-elder-irrigation`. |

## 6. Implementation Status & Measured Gates (27 November 2026)

| Milestone | Commit(s) | Status |
| --- | --- | --- |
| M0 spec | `e5d3b3f` | Locked (this document). |
| M1 demography `(M_c, M_d)` plumbing | `da6646c` | Bit-exact `(1, 1)` parity with PR #49 fixed checks. |
| M2 Phase 1A (state, Steward, attractors, precedents, orders, observations, persistence) | `acd9e32`…`31ae46a` | Done. |
| M3 Phase 1B (civic capital, DemandControl Continuity/Development, cohesion, trade/immigration/fission seams, UI hooks) | `d3941a4`, `efbb3ea` | Done. |
| M4 Phase 2 sanitation | this PR | Done: `CraftGood.SanitationConstruction` (15) / `SanitationMaintenance` (16) Builder recipes gated on Tier 3B; `ProductionOrder.Sanitation`; dt-invariant retention shared with civic capital; Tier-2 `SanitationWater` pass in `TurnHorizonResources` after biological/drinking claims and before `IrrigationEconomy.Allocate`, with forecast parity in irrigation planning; DemandControl sanitation maintenance (Continuity) and development (gated by residents ≥ `SanitationDevelopmentMinPopulation` and forecast water share > 0.05). |
| M5 sweep tooling | — | Not done. The E2E suite (`Tools/CivicGovernanceE2E/`) is included; the 3-arm, 5-world sweep and the 1/16/80/320 benchmark remain open. |

**Measured gates (headless Release, Linux VM):**
- `--civic-only`: 1,002 assertions pass, including the M4 contracts: recipe gating, the 0.15 `M_d` floor, dt-invariant retention, conserved per-tile sanitation water with and without the agriculture block, drinking-first ranking, dry-oasis no-build, and zero sanitation state for fission daughters.
- Analytical equilibrium (`M_c` at its floor): ~3,400 with no sanitation, and 5,000–10,000 with full coverage.
- Organic Phase-2 gate (Phase-1 endpoint, radius-2 cultivated land, 5-year turns, 120 turns): the sanitation arm unlocks Tier 3B organically, reaches 98–100% watered coverage (`M_d` ≈ 0.30), peaks at 6,592 and settles at ~5,700 once food stores are spent. The matched `SanitationEnabled = false` arm holds at 3,082.
- Phase-1 gate: organic climb from 600 to the 2,800–3,400 disease ceiling; the `Civic == null` control stays at ~1,000.
- Full fixed headless suite: exit 0, and the benchmark line is unchanged from PR #49 (`seeds=20 survivors=80/80 starvationDeaths=24217 netGrowth=30353 illegalOrders=0`).
- E2E (`dotnet run -c Release --project Tools/CivicGovernanceE2E -- --tier=all --jobs=3`): 156/156 pass.

**Known limits:**
- Unity `OnGUI` presentation (inspector, hover, legal-action UI) compiles in the shared simulation sources but has not been run interactively in the VM.
- E2E is opaque-box: it cannot inspect the internal `ElderAction`, so its KernelSplit guard is weak. That regression is covered by the headless checks instead.
- The organic Phase-2 gate seeds the Phase-1 civic endpoint rather than replaying the full 600→6,000 climb.
- Egalitarian vs Stratified Phase-2 peak ordering is covered by the analytic `DiseaseMitigationFor` strat bonus, not by a matched organic sweep (M5).
