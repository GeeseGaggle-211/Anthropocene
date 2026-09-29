# Planner direct fulfilment plan (B3)

Status: approved plan, 29 September 2026. Phase 1 is in progress. Phases 2–4
are not implemented. This is a design and sequencing record, not a normative
contract; `EconomicAutomation.md` and `TurnResolutionPasses.md` remain the rule
sources until Phase 4 updates them.

## Problem

`EconomicPlanner.Plan` is about 40% of mature-turn CPU (80-tribe `start-80-11`
thread-time profile, five turns). It is a bisection over a fulfilment scalar:
`Evaluate(0)`, `Evaluate(1)` and up to `SearchIterations = 6` more calls, so up
to eight full evaluations per automated camp per turn. Each evaluation rebuilds
the candidate economy and re-runs the same priority-ordered production
schedule several times:

- `NeolithicProduction.ToolReplacementDemand` runs a probe pass and an
  allocation pass, each calling `DemandWithoutToolReplacement` per recipe, and
  is called separately for Hand Tools and Regular Tools;
- `MaterialEconomy.Forecast` runs the recipe schedule again;
- `FabricFeedstockInputs`, `ManagedHerderReservation`, `HousingMultiplier`,
  the initial material room and three `ToolCoverageBudget` instances are
  recomputed although they do not depend on fulfilment.

Roughly 40–50 schedule simulations per camp per turn result. Food and ecology
already follow the opposite philosophy: fixed claims are computed once and each
active tile is integrated once over the horizon. The planner should match.

## Target: demand once, fulfil directly

1. **Demand pass.** A pure `CampProductionDemand` snapshot built once per camp
   from the prepared prior-cohort state: useful demand per good, fixed labor
   claims (herders, the operating-farmer food floor), capacities (residents,
   known-source material room, plant/animal food envelopes, food room, tool
   coverage) and per-unit recipe coefficients (labor by role, materials, food,
   tool service). Tool replacement demand becomes a closed form in equipment
   output; the Craftsperson service balance is already closed-form.
2. **Fulfilment pass.** With demand and capacities fixed, required workers as a
   function of fulfilment is monotone and piecewise linear with integer
   rounding steps. Solve the largest feasible fulfilment exactly by walking its
   breakpoints, then allocate integer jobs in one canonical sweep. Spare
   residents go to field expansion as today.
3. **One production kernel.** The priority-ordered schedule becomes one pure
   function shared by the planner, `TurnNeeds.Production` and resolver
   production, preserving the rule that planner forecasts and production
   agree, in the way `FoodHorizonForecast` wraps the authoritative tile kernel.

## Doctrine check

- **Authoritative state:** unchanged. `EconomicIntent` assignments and labor
  plan, inventories and automation policy remain the only saved outputs. No
  new save fields; no observation schema change (`RecommendedEconomy` is
  derived).
- **Temporal abstraction:** once per `TurnPlanner.Prepare` for each automated
  camp; independent of turn duration.
- **Scaling:** from ~8 evaluations × several schedule simulations to about
  2–3 schedule passes with recipe-sized buffers. Cost stays linear in
  automated camps with a small constant, which matters at 320 tribes and for
  RL, where planning runs every step and cheap counterfactual staffing is
  useful.
- **Determinism:** canonical priority → good → role ordering and no
  hash-order dependence, so per-camp planning stays parallel-safe.
- **Validation:** see each phase.

## Decisions

### 1. The fulfilment scalar ("one f") — open

**Current definition.** `f ∈ [0, 1]` multiplies the remaining useful demand of
every manufactured good at once (`goals[good] × f`). The search picks the
largest `f` whose full labor envelope — manufacturing roles, tool replacement,
material gathering, food gathering and operating farmers — fits the resident
workforce. Consequences:

- every good receives the same *fraction* of its demand; crafting priorities
  only order access to capped materials, food and tool coverage inside one
  evaluation, they do not decide which goods are dropped first;
- the answer is a single number, easy to explain and to keep deterministic;
- rounding (`Ceiling` per role) makes the labor curve a step function, which
  is why the current code bisects instead of solving.

**Alternatives for Phase 3 and later:**

| Fulfilment rule | Meaning | Fit with trade and Elder/RL |
| --- | --- | --- |
| Uniform `f` (current) | All goods scaled together. | Neutral. Imports/exports can enter as supply/demand rows, but the rule has no notion of relative value, so trade cannot express "buy cloth, make pots". |
| Priority-ordered fill | Satisfy goods fully in crafting-priority order until labor or inputs run out. | Matches how food and ecology claims resolve. Priorities become the Elder/RL action. Gameplay change: low-priority goods can drop to zero. |
| Value-weighted fill | Each good carries a value (priority weight now, price later); allocate labor by value per worker-year. | Natural home for trade prices and a learned Elder policy. Needs a defined value scale and calibration; largest gameplay change. |

**Recommendation.** Build the Phase 2 demand snapshot as a list of *claims*
(good, quantity, per-unit coefficients, priority/value) and make the
fulfilment rule a pluggable pure function over it. Ship Phase 3 with the
uniform rule so outcomes stay within tolerance, and add priority-ordered or
value-weighted rules when trade and the Elder agent define prices and actions.
Trade then contributes supply and demand rows to the same snapshot rather than
a second planner. This choice is deferred to the user.

### 2. Farm Tools for expansion — accepted

Farm Tools for expansion farmers become the next turn's demand. The demand
pass sizes Farm Tools for the fixed operating-farmer floor only, removing the
circular dependency between tool demand and the residual field workforce.
Expansion farmers work without newly planned tools in the turn they are
proposed.

### 3. Tolerance — accepted

Matched per-turn population, prior-cohort QoL and realized food within ±5% of
the current planner are acceptable. Larger deviation requires clear evidence
that the new method is better, because it may require recalibration.

## Phases

- **Phase 0 — reference corpus.** Record automated-camp planner inputs and
  outputs on `start-80-11`, `start-80-23`, `start-80-1729` and
  `start-320-1729` across 25 turns as the differential fixture.
- **Phase 1 — exact refactor.** Extract the shared schedule kernel, compute
  both tool goods from one schedule run, hoist fulfilment-invariant values out
  of `Evaluate`, and evaluate full fulfilment before zero. Gate: exact
  fingerprint parity with the prior commit (scalar, 2/4 workers, reversed
  collections, save continuation) and both headless suites unchanged.
- **Phase 2 — demand snapshot.** Introduce `CampProductionDemand`; the
  existing search consumes it. Gate: exact parity.
- **Phase 3 — direct fulfilment solver.** Replace bisection with the
  breakpoint solve and apply decision 2. Gate: invariants (labor ≤ residents,
  no material/food over-commitment, tool coverage respected, planner forecast
  equals resolver production on uncontended single-camp fixtures); Phase 0
  differential report; matched 25-turn distributions on the four checkpoints
  within ±5%; paired timing; rebaselined `FarmPlannerChecks`.
- **Phase 4 — contracts.** Update `EconomicAutomation.md` ("bounded
  fulfillment search"), `TurnResolutionPasses.md` and
  `CurrentSimulationContract.md`.

## Rejected approaches

- Memoizing `Evaluate` results: keeps the repeated-forecast structure.
- Fewer bisection iterations or skipping unchanged camps: speed by
  approximation without fixing the abstraction.
- A general linear-programming optimizer: expensive and replaces a readable
  priority contract with an objective function.
