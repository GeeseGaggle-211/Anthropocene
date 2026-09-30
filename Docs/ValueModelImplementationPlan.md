# Value model implementation plan

Status: approved plan, 29 September 2026; converged the same day with
[QoLFissionTradeDesign.md](QoLFissionTradeDesign.md). Partly implemented; see
"Progress and measurements" below. The
economic design is in [ValueModelDesign.md](ValueModelDesign.md); QoL,
fission, food trade and proto-towns are in QoLFissionTradeDesign.md. This
document orders the work, names the files and sets the gates. It continues
[PlannerDirectFulfilmentPlan.md](PlannerDirectFulfilmentPlan.md), whose Phase 3
rule interface (`IFulfilmentRule`, `SearchPath`) this plan uses unchanged.
`EconomicAutomation.md` and `TurnResolutionPasses.md` remain the rule sources
until WP12 updates them.

## Progress and measurements (as of 30 September 2026)

Branch `features/neolithic-agricultural`. Measurements use the four
post-opening checkpoints (`TestResults/Issues26-28-33-35/start-{80-11,80-23,
80-1729,320-1729}.anthro`, ignored local files; regenerate per
`Tools/PerformanceAudit/ForecastUnitsREADME.md`), 25 post-opening turns, four
workers, `Tools/PerformanceAudit` with `--details=1 --fingerprints=1`, compared
with `Compare-Drift.py` (mean signed deviation) against a same-method control
(planner `SearchIterations` 6 → 7: per-turn max 0.1–1.8%, mean ≤ 0.5%). The
checkpoints predate `PlannerFulfilment`, so they load as Uniform: Value runs
need `--fulfilment=Value`.

| Package | Commit | Result |
| --- | --- | --- |
| WP0 baselines | — | Checkpoints generated (opening ends turn 10). #33 harness baseline on current code: Toolmaker 24/24, Weaver 24/24, Builder 11/24, Potter 8/24, Carpenter 5/24. |
| Crash fix | `c536e5e` (+ fixture) | Adjacent camps sharing a footprint tile crashed horizon commit (`start-80-1729` turn 17). Guard + regression; other worlds fingerprint-identical. |
| WP1 planning draft | `0c2f970` | Exact: per-turn fingerprints identical on 80-11, 80-23, 320-1729 with and without draft. Unity side uncompiled here. |
| WP2 cohesion | `0f25a8d` | `Q = F·W·R·S(N)`; only QoL values changed. |
| WP3 value rule | `ad7bf59`, `f044f80` | Opt-in; roughly neutral alone (mean QoL −0.7 to +0.8%): crafts rarely bind labour. |
| WP5a Housing Material | `5c9af86` | Neutral (≤ 0.01% drift). #33 witness updated `898595c`. |
| WP5b material order | `fd5b2be` | Neutral: housing was limited by tool coverage, not Wood order. |
| WP3b value production order | `8dacff7` | Under Value: mean QoL **+25% to +34%**, housing 0.23–0.40 → 0.98, clothing 0.76–0.80 → 0.71–0.74, population/food −0.4% to +0.6%; #33 Builder 11/24 → 24/24. |
| Value default (λ = 3) | `2ff638f` | `PlannerFulfilment = "Value"` for new worlds; saves without the field load Uniform. |
| WP4a (first try) | `4cdbf25`, reverted `10dbc75` | Mean QoL −28% to −32%: saved farmer labour priced at the craft labour price; Farm Tools ×2–4 took Wood and coverage. See ValueModelDesign "Revised WP4 design". |
| WP4b (first try) | `853ac3d`, reverted `f7e87a6` | Mean QoL −3.4% to −4.6% (control ≤ 0.1%), population/food unbiased. The claim moved 150–270 Builders/Craftspersons per world to fields by turns 3–5 with no measurable food (±0.1%, max field intensity +0.001). Cause below. |
| WP4b (redo) | this commit | Allocator step ladder, three food buckets (consumption `v_food`, reserve `ρ·v_food`, growth dW/dN), two-stage search. Mean QoL −0.08% to −0.15% (80-1729 −0.60%, noisy control), W = N·Q −0.02% to −0.62%, population and food within ±0.13%: neutral on slack-labour checkpoints. Without the two-stage search: −3.8% to −4.6% (search artefact); with reserve at full `v_food`: −0.07% to −1.08%. |
| WP4a (redo) | this commit | Farm/Herding Tools at the start-of-turn farm-labour price `w_farm0` (ladder value after last turn's expansion farmers; saved herder-years `S/A − 1` per covered herder-year), Pots `ρ·S·v_food` as a need claim. Mean QoL −0.32%, −0.16%, +0.58%, −0.20%; W = N·Q −0.59% to +0.52%; population/food within ±0.5%; Pots ×1.4–1.9 by turn 25 (risk priced, not yet rewarded). A per-turn Pot credit over its life (×3.3) failed: Pots ×4–8, QoL −0.8% to −1.2%. Tool keys 0.02–0.7 stay below need goods. |
| #33 after WP4 | `002c889` | Natural policy windows (24): Toolmaker, Weaver, Builder 24/24; Potter 7/24 (8 before WP4); Carpenter 5/24. Unchanged. |
| Calibration (one at a time, 80-11/80-23/80-1729, vs defaults λ = 3, ρ = 0.1, r = 0.01) | `b40ff68` | Mean QoL: λ = 2 −3.2% to −5.1% (clothing 0.70–0.74 → 0.53–0.59); λ = 4 −0.8% to −1.2%; r = 0.005 −3.5% to −5.5% (longer Housing life crowds out clothing); r = 0.02 +1.1/−0.3/+0.4% (housing falls, mixed); ρ = 0.05 +0.3/+0.7/0.0%; ρ = 0.2 −0.6/−0.1/−0.7%. Population and food within ±0.7% everywhere. Defaults kept: λ = 3 is the optimum; r = 0.02 is not decisively better; ρ only costs QoL until disasters exist (its QoL optimum is 0 by construction), so it stays a placeholder at 0.1. |
| Performance (3 alternating pairs, 25 post-opening turns, `bcdef6e` vs head `91cfad3`) | `91cfad3` | Value rule: total +5.1% (80-11) and +5.8% (320-1729); prepare (planner) +27% / +31%, AI −4%, execute +1–2%, allocations +15%. Uniform on head: −1%. Below the 10% audit trigger. |
| 50-turn rounds (Standard/80: Continents 1–3, Pangaea 1, Archipelagos 1; Value vs Uniform) | `91cfad3` | Post-opening turn 50, P25/median/P75. Agricultural (n = 280): population 472/770/1220, QoL 0.44/0.63/0.89 (Uniform 0.34/0.44/0.49; low housing in 189 camps under Uniform, 0 under Value). Husbandry (n = 106): population 1027/1794/2189, QoL 0.11/0.24/0.40 (Uniform 0.10/0.21/0.40). No clothing in 80/106 husbandry camps under both rules (89/280 agricultural under Value, 46/280 under Uniform): a clothing-supply collapse as camps grow, not caused by the planner rule. No fission (WP10 not implemented): 400 camps throughout. |

**WP4b diagnosis.** `g` came from the allocator's
`AnnualizedFoodPerWorkerYear`: annualised *retained next-boundary* food over
build plus 100 years of maintenance labour, a conservative ranking score. On
80-11 turns 11–15 it is 0.01–0.1 food/yr per worker-year (build share of that
labour ≈ 0.8), against 4 food/yr per person: one more person fed costs
40–400 build worker-years. The claim's need `shortfall/g` was therefore 2–6×
the camp's whole population (up to 57×), its coverage never rose, and
expansion acted as a flat demand at `w ≈ V_exp` = 0.1–1.1. That outbids cloth
at coverage ≈ 0.7 (marginal V/ℓ·e^{−2.1} ≈ 0.6–1.1), so crafts were cut. By
the model's own numbers the trade is roughly even, but the food never appears:
surplus food beyond F = 1 only buys growth, which average food value
`N·Q/(F·D_year)` overstates (QoLFissionTradeDesign 7.2 values growth food by
dW/dN). A build-share correction of the need (`shortfall·B/ΔF`) was tried and
changes little. Open decision before a redo: value expansion food at the
growth margin (dW/dN), use a harvest-flow productivity, or keep expansion
leftover-only and derive `w_farm` for tools from operating-farmer output.

**Clothing collapse diagnosis (30 September 2026).** Pre-existing: the
pre-WP1 baseline `bcdef6e` has 82/106 husbandry camps without clothing at
post-opening turn 50 (Value head 80, Uniform head 84); husbandry mobility is
unchanged (6.0 / 6.0 / 6.2 moves per camp in 50 turns). Root cause is the food
allocation order, not labour, tools or the planner rule: fabric feedstock (one
food per Cloth or Animal Fabric unit) is funded only from food left after this
turn's meals and a *full* operating-reserve refill (`GrowthReserveYears = 30`,
about 1.5 turns of meals); `SparePlant`, `AnimalEnvelope` and
`FoodRoomEnvelope` in the demand pass are then zero and no Weaver can be
staffed. Camps that have grown to their food limit (supply ≈ meals) therefore
make no clothing at all, although clothing feedstock is only about 2.5% of
meals; clothing flips on for a turn after a good harvest refills the reserve
and off again (the observed alternation). Witness: Continents/1/80, camp 1422
at post-opening turn 42 (910 residents, 766 farmers, supply 70,371 < meals
72,800, reserve target 109,200): the plan staffs 0 Weavers; +300 Tools changes
nothing; +40,000 food staffs 23 Weavers. Husbandry camps reach the food limit
sooner (median 1,800 residents), hence 80/106. Secondary: a relocating camp
has one turn with no production and leaves fixed Housing (209 husbandry
camp-turns); Farm and Herding Tools absorb most tool coverage at scale.
Fix: fabric feedstock ahead of reserve refill (user decision 30 September).

**Husbandry mobility check.** MobileHusbandryValidation's exact command
(Continents 11/23/1729, 80 post-opening turns, `--land-m=63
--land-access-scale=0.5 --cultivation-effort=16`) on the pre-WP1 baseline
`bcdef6e` and on head `4480ea9`: husbandry AI moves 214/331/278 → 196/309/248
(−7% to −11%; last 40 turns 53/105/83 → 45/79/46), with late herd forage met
1.000 in all runs, water met 0.950–0.967 → 0.959–0.964 and domestic animals
+1% to +8%. Herds are no worse supplied, so the lower move count is fewer
supply-driven relocations, not a stuck-in-place regression (the report's
mobile control made 298–313 moves under its older model).

**Next, in order (user decision 30 September: correctness over speed):**
1. ~~WP4b redo~~ (user chose the growth-margin valuation; done, see table).
2. ~~WP4a redo~~ (done, see table) — Farm/Herding Tools at `w_farm`, Pots by food security with `ρ`.
3. ~~Calibration sweeps (λ, r, ρ) on the checkpoints~~ (done, defaults kept).
4. ~~Performance regression~~ (done, +5–6% under Value). Open: husbandry clothing collapse at scale. Performance regression: paired timing of `bcdef6e` (pre-WP1) against the
   final planner head on a quiet machine (planner ms, total turn ms,
   allocations); >10% triggers a phase/work audit.

## Settled parameters

| Parameter | Value | Status |
| --- | --- | --- |
| QoL | `Q = B · R · S(N)`; linear in need coverage; no clamp | agreed |
| Social cohesion | `S(N) = exp(−(N / N_s)^β)`, `β = 2` | agreed, calibration target |
| Cohesion scale `N_s` | Paleolithic 220, Nomadic 550, Agricultural 1780 | agreed, fitted to fission targets |
| Fission size targets | Paleolithic high above 100; Nomadic median about 250; Agricultural median about 600, upper about 1,000 | agreed |
| Fission levers | `Q_fiss` 0.75, `ΔQ_fiss` 0.10, `θ_fiss` 0.20, `r_fiss` 3, minimum daughter 12 | candidates, swept |
| Fission policy (per camp) | Discourage / Neutral / Encourage: `Q_fiss` −0.05 / 0 / +0.05; `f_fiss` 0.15 / 0.30 / 0.40; affinity 0.5 / 0.9 / 1.0 | agreed, calibration target |
| Minimum camp spacing | 3 hexes, all placement (land footprints never overlap) | agreed |
| Immigration | `θ_imm` 0.075, `r_imm` 4, lifestyle barriers; opportunity term zero | candidates; opportunity term out of scope |
| Fission daughters | smaller group splits off as an independent kin Society in a new AI player slot; the player keeps the original camp; knowledge and practice copied at fission | agreed; affinity change out of scope |
| Requisitions per camp per turn `K` | 3 | agreed |
| Default bargaining margin `m` | 10%; kin margin `m · (1 − a)` | agreed |
| Discount rate `r` | 0.01 per year, swept 0–0.02, Elder hook | agreed |
| Housing Material labour split | 50% making, 50% assembly | agreed, calibration target |
| Housing Material decay | none | agreed, calibration target |
| Initial tradeable goods | Housing Material, Cloth; food in its own package | agreed |
| Bundle valuation | area under the curve; zero local value beyond full coverage | agreed |
| Growth food value | marginal welfare of an added resident; safe-only as sweep control | agreed direction, calibration target |
| Priorities | value multipliers, `priority / default priority` | agreed |
| Pots | risk placeholder `ρ` until disasters and immigration | agreed |
| Handshake lifetime | one turn; unfinished handshakes lapse; the player sees the complete trade ledger while planning | agreed |
| Transport | bounded range plus provisional distance and bulk costs until #13 | agreed; cost values open |
| Contact knowledge | after the Neolithic unlock: remembered static geography and settlement snapshots from sighting (incl. hunters), trade and kin; contact expiry candidate 5 turns | agreed |
| Value curve steepness `λ` | calibration | open |

New values go in `NeolithicConfig` with matching entries in
`Assets/_Anthropocene/Resources/OpeningConfig.json`, `ConfigurationUnits.md`
and `CurrentSimulationContract.md` (checked by
`python Tools/Docs/Check-CurrentContract.py`).

## Tradeable goods: initial set and candidate permutations

The tradeable set is a configured list, not code, so each permutation can be
swept with the same build. `T0` ships first; the others are sweep candidates,
each testing one question.

| Set | Goods | What it tests |
| --- | --- | --- |
| **T0 (initial)** | Housing Material, Cloth | The two QoL needs. Housing is the #33 failure; one Weaver clothes about 80 people, so surplus is common. |
| **TF (WP9)** | T0 + food | Village differentiation and proto-towns: food-rich villages export food for goods. Required for the section 11.2 town sweep. |
| T1 | T0 + Animal Fabric | Agricultural–pastoral exchange. Animal Fabric substitutes for Cloth 1:1 and feeds Light Housing. |
| T2 | T0 + Wood, Clay, Stone | Comparative advantage in raw materials for source-poor sites; stresses shared-source contention (#34). |
| T3 | T0 + Pots | Storage exchange; mostly relevant once disasters and immigration exist. |
| T4 | T0 + Farm Tools, Herding Tools | Labour-saving capital, for sites that lack Carpentry. |
| T5 | TF + T1 + T2 | Broad trade: checks that the handshake bounds and value ordering hold with many goods. |

Hand and Regular Tools stay out: they are coverage inputs sized by
`ToolReplacementDemands`, not goods with their own value curve.

## Order and dependencies

```
WP0 baselines
WP1 planning pass ─────────────────────────────────────────────┐
WP2 QoL cohesion ──┬── WP3 value rule ── WP4 tools/Pots/expansion ├─ WP7 trade foundation ── WP8 handshake ── WP9 food trade
                   │   WP5 Housing Material ─────────────────────┤
                   │   WP6 contact knowledge ────────────────────┘
                   └── WP10 lineage, fission, immigration (after WP6; parallel with WP3–WP9)
WP11 calibration sweeps (after WP9 and WP10) ── WP12 contracts
```

Kin trade terms are built in WP7 but stay inert until WP10 creates kin
Societies.

## Work packages

### WP0 — Baselines

- Regenerate the four checkpoints (`start-80-11`, `start-80-23`,
  `start-80-1729`, `start-320-1729`) as described in
  `Tools/PerformanceAudit/ForecastUnitsREADME.md`; they are ignored local
  files.
- Record the uniform-rule baseline and a same-method control with
  `Tools/PerformanceAudit/Compare-Drift.py`.
- Record the closed #33 harness baseline: productive specialist windows
  (Toolmaking 21/21, Weaving 17/21, Construction 4/21, Pottery 0/21,
  Carpentry 0/21) and Housing satisfaction.
- Record the QoL and population distributions by regime and settlement size.
- Record End Turn wall latency at 80 and 320 tribes, for WP1.

### WP1 — Planning pass during the player's turn

**This pass is written first**, and every later package's per-camp planning
and handshake work runs inside it. It changes no outcomes.

Today `OpeningGame.EndTurn` runs `AiPlanning.Decide`, then
`TurnPlanner.Prepare` (which calls `EconomicPlanner.Plan` for every automated
camp), then `Resolve`, all after the player presses End Turn. `Decide` already
excludes the human player, and both steps are pure over the committed world.

Simulation (`TurnPlanning.cs`, `PhaseExecution.cs`):

- Add `TurnPlanner.Draft(world, excludedPlayer, execution)`, returning a
  `PlanningDraft`: the world's turn and day stamp, AI orders and traces, the
  AI camps' tentative placements, and each automated camp's intent keyed by
  camp ID, effective tile and its economic settings. WP8 adds handshake state.
- Add an overload `Prepare(world, submitted, traces, execution, draft)`. It
  validates all orders as today, resolves `CampPlacement` with the player's
  orders, and reuses a draft intent only when its key still matches. Everything
  else is recomputed. `Prepare` without a draft keeps today's behaviour.
- Count reuse and recomputation in `TurnDiagnostics` (transient).

Runtime (`OpeningGame.cs`):

- Start the draft on a worker when a committed world is adopted: after
  `FinishTurn`, after a load and after a new game.
- `EndTurn` waits for the draft if it is still running, then calls `Prepare`
  with it. With no draft it runs as today.
- New game and load cancel the draft; a draft whose stamp does not match the
  world is discarded.
- Save may run alongside the draft; both only read. Resolution still waits
  for both.
- Show a small indicator while the AI is planning. The player can end the
  turn at any time.

Thread safety, audited before the draft runs next to the UI:

- `MaterialAccessIndex.For` (compare-exchange) and `TurnDiagnostics.For`
  (exchange) already create safely; prewarm both on the main thread anyway.
- Find every other lazily created cache attached to `WorldState` or its
  entities, and prewarm it or show it is safe to create concurrently.
- The UI's observation build (`Refresh`) must not write to the world.

Headless (`Tools/Headless/`): new `PlanningDraftChecks.cs`, registered in
`Anthropocene.Headless.csproj` and the `fixedChecks` list in `Program.cs`:

- per-turn fingerprints with a draft equal those without, under scalar, two-
  and four-worker runs and reversed collections;
- forced invalidation: a player move that conflicts with an AI destination, a
  player Economy order, and automation toggled for a camp;
- a stale draft (wrong stamp) is never used.

Measurement: End Turn latency with a simulated think time in
`Tools/PerformanceAudit`. CPU per turn must be unchanged; headless, AI-only and
RL runs get no speed-up, so later packages must stay bounded on their own.

Gate: exact fingerprints; unchanged `./Tools/Test-Headless.ps1 -Seeds 0` and
`-Seeds 30 -Standard -Geography` output apart from the new assertions;
measured latency reduction.

### WP2 — QoL cohesion multiplier

Simulation (`NeolithicProduction.cs`, `NeolithicConfig.cs`):

- `ResolveNeeds` computes `Q = B · R · S(N)` with
  `S(N) = exp(−(N / N_s)^β)`, `N` the co-resident cohort.
- A pure regime function decides `N_s` at the turn boundary: Paleolithic when
  the Society has no first breakthrough (`Technology.None`), Agricultural when
  Cultivation is enabled, Nomadic otherwise. Fishing and Watercraft camps are
  Nomadic unless they farm.
- Config: `β`, three `N_s` values.
- Audit every consumer of `Camp.QoL` (observations, inspector, AI traces,
  saves). Today nothing in the simulation reads it back, so the change affects
  displayed and saved QoL, and the planner anchors from WP3 on. Growth stays
  independent of QoL.

Headless: formula and regime tests; `S` monotone in `N`; the section 3.5
fission-size table reproduced from the formula; regime switch applied only at
the boundary; save continuation.

Gate: headless suites pass with only the expected QoL changes; QoL
distributions by regime and size reported against WP0.

### WP3 — Local value rule

Simulation (`ProductionDemand.cs`, `EconomicPlanner.cs`, `NeolithicConfig.cs`):

- `CampProductionDemand` computes, per claim: first-unit value `V` from the
  QoL anchor (person-QoL-years, including `B · S`), need level `D`, stock,
  closed-form labour per unit `ℓ` (craft labour plus input extraction plus
  input food gathering), discounted service life for durables, and the
  priority multiplier. Local value is zero beyond full coverage.
- Add `ValueFulfilment : IFulfilmentRule`: shares from `w(s)` on the log path
  described in the design. Uniform stays the default until the gate passes.
- In `Evaluate`, order material and food access by value at the current share
  instead of by crafting priority.
- Return the labour price `w` in the plan result and trace (transient, never
  saved).
- Config: `λ`, `r`; runs inside the WP1 pass with no further change.

Headless: extend `EconomicAutomationChecks` or add `ValueFulfilmentChecks.cs`:
equimarginal condition, shares nondecreasing in `s`, full fulfilment when
labour is slack, zero priority disables a good, determinism.

Gate: mean QoL does not fall and population and food show no bias beyond the
control in `Compare-Drift.py`; the #33 rerun is reported. Only then switch the
default rule.

### WP4 — Tools, Pots and expansion

- Farm and Herding Tools valued by labour saved at `w`; they take priority
  whenever `w > 0`, up to useful demand.
- Food-security value per food unit, shared by Pots (scaled by `ρ`) and field
  expansion.
- Expansion competes for workers at `w` instead of taking only workers left
  over; leftover workers still go to expansion.
- Config: `ρ`.

Gate: as WP3, plus `FarmPlannerChecks` rebaselined and the change in farmer
counts reported.

### WP5 — Housing Material

Status: WP5a (recipe split, inventory, schema 16, same scheduling position and access order) is implemented and
reproduces the shipped outcomes; WP5b (material access order) is not started.

- Recipes (`NeolithicProduction.Recipes`): a Housing Material recipe with the
  Wood/Clay inputs and half of today's Builder labour per Housing unit; Housing
  consumes one Housing Material with the other half. Builders and generalist
  Craftspersons only.
- The Wood:Clay mix is fixed when material is made; `MaterialEconomy.HousingShares`
  moves to that recipe and construction loses its final-consumer position in
  `Evaluate`.
- Useful demand for Housing Material is the Housing gap; no stockpiling beyond
  it; no decay.
- State: optional `CampInventory.HousingMaterial` in the `ANTHROPOCENE-11`
  envelope (absent means zero); owned observation field, schema 15 → 16.
  Update `SaveCodec`, `ObservationContract`, `AgentObservation.SchemaVersion`
  and the save format notes together.
- UI: inspector line in `OpeningDetails.cs`.

Headless: recipe conservation (labour per Housing unit unchanged), save
round-trip and old-save load, observation filtering.

Gate: shipped balance unchanged within the drift control apart from the
scheduling change, which is reported against the #33 Construction baseline.

### WP6 — Contact knowledge

Following QoLFissionTradeDesign.md section 3.7. Prerequisite for trade (WP7)
and for fission and immigration (WP10): with `VisionRadius = 2` and 3-hex
spacing, no camp can otherwise see a neighbour or a destination.

- **State** (saved, optional, per Society): explored tiles in a sparse chunked
  set; settlement contacts (camp, owner, tile, turn learned, source). Old saves
  load with no knowledge.
- **Updates** at the end of each turn, from the vision already computed
  (`Agents.Visible`): record explored tiles, and record, refresh or mark moved
  the contacts in view. Only Societies past their first breakthrough remember;
  Paleolithic Societies keep live vision only.
- **Trade and kin hooks:** receiving a requisition or confirming a trade
  refreshes the partner (called from WP7–WP8); fission records parent and
  daughter and shares the parent's knowledge copy-on-write (called from WP10).
- **Expiry:** contacts not refreshed for a configured number of turns
  (candidate 5) are dropped.
- **Observations** (`AgentObservationBuilder`, `ObservationContract`): remembered
  static geography for explored tiles outside current vision, and known
  settlements with the turn learned. No live occupants, ecology or herds on
  remembered tiles. Schema version bump; `HoverDetails` and the map renderer
  show remembered tiles distinctly from visible ones.
- **Contract:** the filtered-observation rule in `AGENTS.md` and
  `CurrentSimulationContract.md` gains remembered knowledge as a deliberate,
  labelled category.

Headless: remembered tiles never expose live stocks, herds or occupants; a
contact never updates without a sighting, trade or kin event; moved and expiry
handling; Paleolithic Societies remember nothing; save continuation and old
saves; scalar-versus-parallel fingerprints.

Measurement: memory per Society against explored area, at 80 and 320 tribes
and on the largest map; update cost per turn.

### WP7 — Trade foundation (no AI)

- Data: `Bid { Give, Receive, AcceptanceLimit }` bundles; requisition,
  acknowledgement (honour, counter, ignore) and close records; a saved **trade
  ledger** of confirmed terms, deliveries and lapsed or ignored requisitions.
  No pending state is saved; every handshake finishes or lapses within its
  turn.
- Valuation: integrated bundle value (area under the curve) for received and
  surrendered goods; production opportunity cost `w · ℓ` for new output.
- Resolution: one canonical exchange step after production and before needs,
  conserving both sides; shortfalls scale both sides in proportion. Update
  conservation invariants.
- Orders: new order kinds for sending a requisition and for answering one,
  validated by `OrderRules` with `LegalActions` masks. A requisition carries
  the sender's acceptance limit.
- Requisitions can be sent only to known settlements (WP6) within trade range.
- Observations: this turn's requisitions addressed to the camp, its own trade
  history, and each partner's past requisitions to this camp. No foreign
  stores, values or other camps' messages.
- Kin terms: an affinity lookup (0 for unrelated Societies; the saved kin
  affinity once WP10 records lineage) sets margin `m · (1 − a)` and surplus
  pricing.
- Config: tradeable set (T0), `K = 3`, `m = 10%`, contact range, provisional
  distance and bulk costs.

Headless: ledger conservation, save round-trip, order validation and masks,
observation filtering, scaling of both sides on shortfall, integrated
valuation monotone with diminishing gain, and no handshake surviving its turn.

### WP8 — Handshake in the planning pass

- Inside `TurnPlanner.Draft`: AI requisitions ranked by
  `RequestScore(q) = IntegratedGain(q) − ExpectedGiveCost(q)`; offers chosen
  from goods the partner has requested before, else the lowest-value good;
  AI-to-AI acknowledgements and closes, all from committed boundary values.
  Responders handle requisitions in order of value to themselves, ties by camp
  ID and good; at most `K` per camp; one handshake per pair per good per turn.
- A trade-ledger panel in the planning UI (`OpeningPlanning.cs`) shows this
  turn's requisitions addressed to the player (with honour, counter or
  ignore), the player's own new requisitions with acceptance limits, and last
  turn's outcomes: confirmed, lapsed, ignored. Answers are orders.
- At End Turn, AI camps close handshakes with the player during preparation:
  accept or reject the player's counters, and honour, counter or ignore the
  player's requisitions, auto-accepting counters within the player's limit.
  Unfinished handshakes lapse.
- At End Turn, recompute only: the player's camps; AI camps whose destination
  changed; responders that received the player's requisitions and the
  requesters whose confirmations that changes (one level); AI camps trading
  with the player.
- Confirmed trades enter the demand pass as export and import claims; export
  claims carry the agreed value and quantity.

Headless: draft-versus-sequential fingerprints with trade active; invalidation
fixtures for each recompute trigger; handshake bounds; no information beyond
received messages.

Measurement: handshake work count per turn at 80 and 320 tribes, and End Turn
latency.

### WP9 — Food trade

Following QoLFissionTradeDesign.md section 7 (`GenerationalEconomy.cs` food
buckets, `TurnPlanning.cs` `TurnNeeds`, the WP7 exchange step):

- Seller bands from the prepared food ledger: protected survival food
  (consumption, manufacturing feedstock, operating reserve; never tradeable),
  growth food, safe surplus.
- Growth-food value: `max(0, dW/dN) · L / food_per_birth`, with
  `dW/dN = B · S · [R − E · Σ_uncovered w_k c_k] − β (N / N_s)^β · Q + w`.
  Buyer values ordered by starvation avoided, reserve restored, growth funded.
- Food export policy config: marginal-welfare valuation (proposed default) or
  safe-only (control).
- Confirmed food imports enter the prepared food envelope before demographic
  planning; physical exchange settles in the canonical step; short delivery
  scales growth and reserve to realized food; one food unit is never counted
  for both seller and buyer growth.
- Add food to the tradeable set as `TF`.

Headless: the protected band is never debited; unconfirmed food never supports
births; short delivery scales outcomes; no double counting; food ledger
conservation (`FoundationChecks`); determinism under reordering and parallel
runs.

### WP10 — Lineage, fission and immigration

Following QoLFissionTradeDesign.md sections 3, 4, 6.4 and 9.

- **Fission policy.** A saved per-camp setting, Discourage / Neutral /
  Encourage, default Neutral (optional field; absent means Neutral). It shifts
  `Q_fiss` by −0.05 / 0 / +0.05, sets the daughter fraction to 0.15 / 0.30 /
  0.40 and the daughter's affinity to 0.5 / 0.9 / 1.0. Exposed in the owner's
  observation, the settlement inspector (`OpeningVillageInspector.cs`) and as a
  legal action for AI policies (`ActionContract.cs`); the heuristic AI keeps
  Neutral until the Elder (#23) chooses.
- **Lineage.** Saved optional parent-Society link and kin affinity on
  `Society`. Parent–daughter affinity comes from the policy; two daughters of
  one parent take the lower of their parent affinities. Inert afterwards.
- **Daughter Societies.** Fission is always a smaller group splitting off. The
  parent keeps its camp, site, fields and fixed Housing, and its controller
  (the player keeps the original camp). The daughter is a new Society in a new
  AI player slot, with Society, camp and player IDs assigned canonically and a
  policy seed from the keyed random streams. Its knowledge and practice
  experience are copied from the parent at the moment of fission
  (technologies, traditions, policy preferences, domain and species practice,
  progression). Herds and portable stores go with the daughter in its
  population fraction, conserving every ledger.
- **Cached destination.** A saved optional per-camp target tile, searched
  lazily over explored tiles and current vision (WP6) within `r_fiss` only when the push exceeds the search threshold and no
  valid target exists; invalidated when occupied, unviable or the regime
  changes. Replaces `Camp.FissionWilling`.
- **Minimum camp spacing.** `CampPlacement.Resolve` rejects any placement
  closer than 3 hexes to another camp: fission destinations, relocations and
  ordinary moves. This changes existing movement legality for players and AI,
  so the order masks and movement previews (`OpeningPlanning.cs`) follow it,
  and the change is validated on its own before fission is enabled.
- **Mobility pass** in the Close phase: freeze the post-demography snapshot;
  one immigration intent per source toward a known settlement (WP6) within
  `r_imm` (opportunity term zero); otherwise one
  fission intent against the cached target; reconcile immigration once, then
  fission on the remaining population, in canonical order with no rerouting;
  commit simultaneously. QoL is recomputed next turn.
- **Parallelism.** Scoring, push, destination checks and lazy site searches
  run per camp on bounded workers (`PhaseExecution.Map`) over the frozen
  snapshot; reconciliation and commits are canonical and proportional to the
  number of intents.
- **Observations and UI:** own lineage, kin relations and fission policy;
  journal entries for fission and immigration.

Headless: conservation across fission and immigration; daughters always
smaller than parents; policy levers applied exactly; copied knowledge and
practice equal to the parent's at fission; no fission without a viable site;
no placement within 3 hexes; no same-turn multi-hop; bounded search counts;
scalar-versus-parallel and reordered-collection fingerprints; save
continuation including the new optional fields.

Measurement: fission multiplies settlement, Society and player counts, so every
per-camp and per-Society cost grows with it. Measure camp, Society and player
counts, memory (especially any dense per-player visibility or knowledge state)
and turn time over long runs at 80 and 320 starting tribes, with worker
scaling at 1, 2 and 4 workers.

### WP11 — Calibration sweeps

- Section 11.1 fission calibration: hazard by regime and size against the
  targets (Paleolithic above 100, Nomadic about 250, Agricultural about 600
  and 1,000).
- Section 11.2 food-export and town sweep, including the food export policy.
- Tradeable-set permutations T0, TF, T1–T5 on the four checkpoints: trade
  volume, QoL, population, #33 windows and the proto-town diagnostic.
- Sweep `λ`, `r`, `m`, `β`, `N_s` and the fission levers within their ranges.

### WP12 — Contracts

- `TurnResolutionPasses.md`: the interturn draft during the player's turn, its
  reuse rule, the exchange step, prepared food imports, and the Close-phase
  mobility pass.
- `EconomicAutomation.md`: value fulfilment replaces "bounded fulfillment
  search", plus Housing Material and trade.
- `CurrentSimulationContract.md`, `ConfigurationUnits.md`, `Validation.md`, and
  the `AGENTS.md` source map entries for new files.
- Phase 4 of the planner plan is completed here.

## Doctrine summary

- **Authoritative state:** unchanged in WP1–WP4 apart from the QoL value
  itself. WP5 adds one inventory field; WP6 adds per-Society
  knowledge (explored tiles, contacts); WP7 adds the trade ledger; WP10 adds the
  parent-Society link and cached fission target, and creates Societies and
  camps. Drafts, values, `w` and handshake working state are never saved.
- **Temporal abstraction:** everything runs once per turn from the committed
  boundary; values use physical years; mobility is one pass per turn.
- **Scaling:** WP1 hides latency, not CPU. WP3 adds O(claims) per search
  probe. WP8 adds at most `3 × camps` requisitions per turn. WP10 is
  `O(N · r_imm² + F · r_fiss² + N)`, but raises `N` itself over time, so
  per-camp and per-Society work runs in parallel with canonical commits.
- **RL:** QoL, values, `w` and received requisitions are observation
  features; `λ`, `r`, `ρ`, `m` and priorities are Elder levers; QoL is the
  reward.
- **Determinism:** draft reuse must be exact; handshake and mobility ordering
  is canonical.

## Risks

- **Lazy caches read by the UI and the draft at once.** Mitigated by the WP1
  audit and prewarming.
- **Draft invalidation misses a dependency.** Mitigated by fingerprint
  equality against the sequential path in every check.
- **Value rule lowers QoL** by under-filling needs near full coverage.
  Mitigated by the WP3 gate; `λ` is calibrated before the default switches.
- **Fission multiplies camps and Societies,** raising every per-camp cost and
  any dense per-Society memory. Mitigated by the WP10 long-run measurements
  before fission is enabled by default.
- **Food trade breaks the food ledger.** Mitigated by the WP9 band rules and
  conservation checks.
- **Provisional transport costs** before #13 may overstate exchange. Report
  sweeps as provisional.
