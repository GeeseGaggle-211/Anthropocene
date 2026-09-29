# Value model implementation plan

Status: approved plan, 29 September 2026; converged the same day with
[QoLFissionTradeDesign.md](QoLFissionTradeDesign.md). Not implemented. The
economic design is in [ValueModelDesign.md](ValueModelDesign.md); QoL,
fission, food trade and proto-towns are in QoLFissionTradeDesign.md. This
document orders the work, names the files and sets the gates. It continues
[PlannerDirectFulfilmentPlan.md](PlannerDirectFulfilmentPlan.md), whose Phase 3
rule interface (`IFulfilmentRule`, `SearchPath`) this plan uses unchanged.
`EconomicAutomation.md` and `TurnResolutionPasses.md` remain the rule sources
until WP11 updates them.

## Settled parameters

| Parameter | Value | Status |
| --- | --- | --- |
| QoL | `Q = B · R · S(N)`; linear in need coverage; no clamp | agreed |
| Social cohesion | `S(N) = exp(−(N / N_s)^β)`, `β = 2` | agreed, calibration target |
| Cohesion scale `N_s` | Paleolithic 220, Nomadic 550, Agricultural 1780 | agreed, fitted to fission targets |
| Fission size targets | Paleolithic high above 100; Nomadic median about 250; Agricultural median about 600, upper about 1,000 | agreed |
| Fission levers | `Q_fiss` 0.75, `ΔQ_fiss` 0.10, `θ_fiss` 0.20, `f_fiss` 0.30, `r_fiss` 3 | candidates, swept |
| Immigration | `θ_imm` 0.075, `r_imm` 4, lifestyle barriers; opportunity term zero | candidates; opportunity term out of scope |
| Fission daughters | independent kin Societies; affinity inert at 0.9 | agreed; affinity change out of scope |
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
| **TF (WP8)** | T0 + food | Village differentiation and proto-towns: food-rich villages export food for goods. Required for the section 11.2 town sweep. |
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
WP2 QoL cohesion ──┬── WP3 value rule ── WP4 tools/Pots/expansion ├─ WP6 trade foundation ── WP7 handshake ── WP8 food trade
                   │   WP5 Housing Material ─────────────────────┘
                   └── WP9 lineage, fission, immigration (parallel with WP3–WP8)
WP10 calibration sweeps (after WP8 and WP9) ── WP11 contracts
```

Kin trade terms are built in WP6 but stay inert until WP9 creates kin
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
  camp ID, effective tile and its economic settings. WP7 adds handshake state.
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

### WP6 — Trade foundation (no AI)

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
- Observations: this turn's requisitions addressed to the camp, its own trade
  history, and each partner's past requisitions to this camp. No foreign
  stores, values or other camps' messages.
- Kin terms: an affinity function (0 for unrelated Societies; 0.9 for kin once
  WP9 records lineage) sets margin `m · (1 − a)` and surplus pricing.
- Config: tradeable set (T0), `K = 3`, `m = 10%`, kin affinity 0.9, contact
  range, provisional distance and bulk costs.

Headless: ledger conservation, save round-trip, order validation and masks,
observation filtering, scaling of both sides on shortfall, integrated
valuation monotone with diminishing gain, and no handshake surviving its turn.

### WP7 — Handshake in the planning pass

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

### WP8 — Food trade

Following QoLFissionTradeDesign.md section 7 (`GenerationalEconomy.cs` food
buckets, `TurnPlanning.cs` `TurnNeeds`, the WP6 exchange step):

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

### WP9 — Lineage, fission and immigration

Following QoLFissionTradeDesign.md sections 3, 4, 6.4 and 9.

- **Lineage.** A saved optional parent-Society link on `Society`. Kin affinity
  is 0.9 for parent–daughter and sibling pairs, inert.
- **Daughter Societies.** Fission creates a new Society and camp with IDs from
  `NextId`. The daughter inherits the parent's culture (technologies,
  traditions, policy preferences). Population, portable stores and herds are
  split in proportion to the daughter fraction, conserving every ledger; fixed
  Housing stays with its site.
- **Cached destination.** A saved optional per-camp target tile, searched
  lazily within `r_fiss` only when the push exceeds the search threshold and no
  valid target exists; invalidated when occupied, unviable or the regime
  changes. Replaces `Camp.FissionWilling`.
- **Mobility pass** in the Close phase: freeze the post-demography snapshot;
  one immigration intent per source (opportunity term zero); otherwise one
  fission intent against the cached target; reconcile immigration once, then
  fission on the remaining population, in canonical order with no rerouting;
  commit simultaneously. QoL is recomputed next turn.
- **Observations and UI:** own lineage and kin relations; journal entries for
  fission and immigration.

Decisions to settle at the start of WP9, before code:

- how a daughter Society is controlled: an AI player slot, and what happens
  when the human player's camp fissions;
- whether the player's own camps fission automatically or on the player's
  confirmation (the #23 mandate question);
- exactly which knowledge and practice experience a daughter inherits.

Headless: conservation across fission and immigration; no fission without a
viable site; no same-turn multi-hop; bounded search counts; determinism under
reordering and parallel runs; save continuation.

Measurement: fission multiplies settlement and Society counts, so every
per-camp and per-Society cost grows with it. Measure camp and Society counts,
memory (especially any dense per-Society visibility or knowledge state) and
turn time over long runs at 80 and 320 starting tribes.

### WP10 — Calibration sweeps

- Section 11.1 fission calibration: hazard by regime and size against the
  targets (Paleolithic above 100, Nomadic about 250, Agricultural about 600
  and 1,000).
- Section 11.2 food-export and town sweep, including the food export policy.
- Tradeable-set permutations T0, TF, T1–T5 on the four checkpoints: trade
  volume, QoL, population, #33 windows and the proto-town diagnostic.
- Sweep `λ`, `r`, `m`, `β`, `N_s` and the fission levers within their ranges.

### WP11 — Contracts

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
  itself. WP5 adds one inventory field; WP6 adds the trade ledger; WP9 adds the
  parent-Society link and cached fission target, and creates Societies and
  camps. Drafts, values, `w` and handshake working state are never saved.
- **Temporal abstraction:** everything runs once per turn from the committed
  boundary; values use physical years; mobility is one pass per turn.
- **Scaling:** WP1 hides latency, not CPU. WP3 adds O(claims) per search
  probe. WP7 adds at most `3 × camps` requisitions per turn. WP9 is
  `O(N · r_imm² + F · r_fiss² + N)`, but raises `N` itself over time.
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
  any dense per-Society memory. Mitigated by the WP9 long-run measurements
  before fission is enabled by default.
- **Food trade breaks the food ledger.** Mitigated by the WP8 band rules and
  conservation checks.
- **Provisional transport costs** before #13 may overstate exchange. Report
  sweeps as provisional.
