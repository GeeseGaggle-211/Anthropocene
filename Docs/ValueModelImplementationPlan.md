# Value model implementation plan

Status: approved plan, 29 September 2026. Not implemented. The design and its
reasoning are in [ValueModelDesign.md](ValueModelDesign.md); this document
orders the work, names the files and sets the gates. It continues
[PlannerDirectFulfilmentPlan.md](PlannerDirectFulfilmentPlan.md), whose Phase 3
rule interface (`IFulfilmentRule`, `SearchPath`) this plan uses unchanged.
`EconomicAutomation.md` and `TurnResolutionPasses.md` remain the rule sources
until work package 7 updates them.

## Settled parameters

| Parameter | Value | Status |
| --- | --- | --- |
| Requisitions per camp per turn `K` | 3 | agreed |
| Default bargaining margin `m` | 10% | agreed |
| Discount rate `r` | about 0.01 per year, one constant, Elder hook | agreed |
| Housing Material labour split | 50% making, 50% assembly | agreed, calibration target |
| Housing Material decay | none | agreed, calibration target |
| Initial tradeable goods | Housing Material, Cloth | agreed |
| QoL | stays linear; exponential curve is planner pricing only | agreed |
| Priorities | value multipliers, `priority / default priority` | agreed |
| Pots | risk placeholder `ρ` until disasters and immigration | agreed |
| Value curve steepness `λ` | calibration | open |
| Kinship reduction of `m` for same-Society partners | calibration | open |
| Trade contact range | placeholder until #13 | open |
| Unanswered requisitions | held pending; settled in resolution by standing policy, else carried forward | agreed |
| Pending requisition expiry | turns | open |

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
| **T0 (initial)** | Housing Material, Cloth | The two QoL needs. Housing is the #33 failure; Cloth is where one Weaver clothes about 80 people, so surplus is common. |
| T1 | T0 + Animal Fabric | Agricultural–pastoral exchange. Animal Fabric substitutes for Cloth 1:1 and feeds Light Housing, so pastoral and farming camps have natural complements. |
| T2 | T0 + Wood, Clay, Stone | Comparative advantage in raw materials for source-poor sites. Also stresses shared-source contention (#34). |
| T3 | T0 + Pots | Storage exchange. Mostly relevant once disasters and immigration exist. |
| T4 | T0 + Farm Tools, Herding Tools | Labour-saving capital, for sites that lack Carpentry. |
| T5 | T1 + T2 | Broad non-food trade: checks that the handshake bounds and the value ordering hold with many goods. |

Food (Plant, Animal, Aquatic) is **deferred**. Trading it touches the
food-first budget, reserve refill and growth allocation, and needs its own
design. Hand and Regular Tools stay out: they are coverage inputs sized by
`ToolReplacementDemands`, not goods with their own value curve.

## Work packages

Each package lands separately, with its own validation. Packages 1–4 need no
trade; 5 and 6 build trade on them.

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
- Record End Turn wall latency (time from End Turn to a resolved turn) at
  80 and 320 tribes, for WP1.

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
  camp ID, effective tile and its economic settings. WP6 adds handshake state.
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

### WP2 — Local value rule

Simulation (`ProductionDemand.cs`, `EconomicPlanner.cs`, `NeolithicConfig.cs`):

- `CampProductionDemand` computes, per claim: first-unit value `V` from the
  QoL anchor (person-QoL-years), need level `D`, stock, closed-form labour per
  unit `ℓ` (craft labour plus input extraction plus input food gathering),
  discounted service life for durables, and the priority multiplier.
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

### WP3 — Tools, Pots and expansion

- Farm and Herding Tools valued by labour saved at `w`; they take priority
  whenever `w > 0`, up to useful demand.
- Food-security value per food unit, shared by Pots (scaled by `ρ`) and field
  expansion.
- Expansion competes for workers at `w` instead of taking only workers left
  over; leftover workers still go to expansion.
- Config: `ρ`.

Gate: as WP2, plus `FarmPlannerChecks` rebaselined and the change in farmer
counts reported.

### WP4 — Housing Material

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

### WP5 — Trade foundation (no AI)

- Data: `Bid { Give, Receive }` bundles; requisition, acknowledgement (honour,
  counter, ignore) and close records; a saved **trade ledger** of confirmed
  terms and deliveries; saved **pending requisitions** with their expiry.
- Standing response policies: per-camp delegated rules (honour within limits,
  ignore) used by the Elder for AI camps and exposed as settings for the
  player. They settle pending requisitions during resolution.
- Resolution: one canonical exchange step after production and before needs,
  conserving both sides; shortfalls scale both sides in proportion. The same
  step settles pending requisitions by standing policy, using
  preparation-time values and only surplus stock or production beyond the
  camp's planned needs. Unsettled requisitions carry to the next turn until
  they expire. Update conservation invariants.
- Orders: new order kinds for sending a requisition and for answering one,
  validated by `OrderRules` with `LegalActions` masks. A requisition carries
  the sender's acceptance limit.
- Observations: requisitions addressed to the player, and each partner's past
  requisitions to this camp. No foreign stores, values or other camps'
  messages.
- Config: tradeable set (T0), `K = 3`, `m = 10%`, contact range placeholder.
  Transport cost is zero until #13; the range limit stands in for it.

Headless: ledger conservation, save round-trip (including pending
requisitions), order validation and masks, observation filtering, scaling of
both sides on shortfall, resolution-time settlement never exceeding surplus,
and expiry.

### WP6 — Handshake in the planning pass

- Inside `TurnPlanner.Draft`: AI requisitions (highest-value good requested;
  offer chosen from goods the partner has requested before, else the
  lowest-value good), AI-to-AI acknowledgements and closes, all from committed
  boundary values. Responders handle requisitions in order of value to
  themselves, ties by camp ID and good; at most `K` per camp; one handshake per
  pair per good per turn.
- Requisitions addressed to the player appear during planning
  (`OpeningPlanning.cs`); the player answers in orders. Unanswered
  requisitions, for the player or any AI camp, stay pending and are settled in
  resolution by standing policy or carried forward (WP5).
- At End Turn, recompute only: the player's camps; AI camps whose destination
  changed; responders that received the player's requisitions and the
  requesters whose confirmations that changes (one level); AI camps trading
  with the player.
- Confirmed trades enter the demand pass as export and import claims; export
  claims carry the agreed value and quantity.
- Same-Society terms (kinship reduction of `m`, surplus only) are implemented
  but inert until #15 creates multiple camps per Society.

Headless: draft-versus-sequential fingerprints with trade active; invalidation
fixtures for each recompute trigger; handshake bounds; no information beyond
received messages.

Measurement: handshake work count per turn at 80 and 320 tribes, and End Turn
latency. Sweep T0–T5 on the four checkpoints and report trade volume, QoL,
population and the #33 windows per set.

### WP7 — Contracts

- `TurnResolutionPasses.md`: the interturn draft during the player's turn, its
  reuse rule, and the exchange step in resolution.
- `EconomicAutomation.md`: value fulfilment replaces "bounded fulfillment
  search", plus Housing Material and trade.
- `CurrentSimulationContract.md`, `ConfigurationUnits.md`, `Validation.md`, and
  the `AGENTS.md` source map entries for new files.
- Phase 4 of the planner plan is completed here.

## Doctrine summary

- **Authoritative state:** unchanged in WP1–WP3. WP4 adds one inventory field;
  WP5 adds the trade ledger and message records. Drafts, values, `w` and
  handshake working state are never saved.
- **Temporal abstraction:** everything runs once per turn from the committed
  boundary; values use physical years.
- **Scaling:** WP1 hides latency, not CPU. WP2 adds O(claims) per search probe.
  WP6 adds O(requisitions), at most `3 × camps` per turn.
- **RL:** values, `w` and received requisitions are observation features;
  `λ`, `r`, `ρ`, `m` and priorities are Elder levers; QoL is the reward.
- **Determinism:** draft reuse must be exact; all handshake ordering is
  canonical.

## Risks

- **Lazy caches read by the UI and the draft at once.** Mitigated by the WP1
  audit and prewarming.
- **Draft invalidation misses a dependency.** Mitigated by fingerprint
  equality against the sequential path in every check.
- **Value rule lowers QoL** by under-filling needs near full coverage.
  Mitigated by the WP2 gate; `λ` is calibrated before the default switches.
- **Trade with zero transport cost** before #13 overstates exchange. The
  contact range limits it; report sweeps as provisional.
