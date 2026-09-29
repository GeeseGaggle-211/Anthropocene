# Planner direct fulfilment plan (B3)

Status: approved plan, 29 September 2026. Phases 1 and 2 are implemented;
Phase 3's rule interface and path solve are implemented with the uniform rule.
Decision 2 was dropped after measurement; schedule unification and Phase 4
are not implemented.
The value-fill design continues in
[ValueFulfilmentHandover.md](ValueFulfilmentHandover.md). This is a design and sequencing record, not a normative
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

**User constraint (29 September 2026).** If value-weighted fill is chosen,
values must be semi-dynamic: a scarcer good is more valuable. Values therefore
become a function of the camp's stock relative to its useful demand (and, once
trade exists, of external supply), recomputed at the demand pass from the
prepared prior boundary — never mid-fulfilment — so one turn still computes
demand once and fulfils it directly. The scarcity curve, its bounds and any
smoothing across turns are calibration choices to settle before that rule is
implemented.

**QoL-grounded value fill — analysis (29 September 2026).** Proposal: value
each good by how much it moves quality of life, in the spirit of Amdahl's law,
with tools valued as labor becomes scarce and amenities added later. The
current formula is
`QoL = FoodMet × WaterMet × (½·clothing + ½·housing)` with
`clothing = 1 − E·(1 − clothCoverage)` (housing likewise) and expectation `E`
ramping with settled years.

- *What holds.* One currency tied to an existing, calibrated mechanic gives
  explainable values and doubles as the Elder/RL reward. The multiplicative
  food and water terms are exactly the Amdahl case: they scale every other
  gain, so while food or water is short, marginal workers belong there. The
  planner already reserves food first, which this formalizes.
- *Limit 1 — no scarcity curve.* Clothing and housing are linear up to full
  coverage, then flat. Marginal QoL per unit is constant until a need is met,
  then zero. Pure marginal-QoL fill is bang-bang: it fills the cheaper need
  completely before touching the other, and values do not rise smoothly
  with scarcity as required. Scarcity sensitivity needs diminishing returns
  somewhere.
- *Limit 2 — Pots and tools are not in QoL.* Pots protect future food
  (storage), tools save labor, materials are inputs. They need derived values:
  a tool is worth the labor it saves times the shadow price of labor — the
  marginal QoL of one more worker, which the fulfilment solve can report. That
  is the precise form of "tools matter when labor is scarce". Pots need an
  expected-shortage value, which does not exist yet.
- *Limit 3 — durability.* Housing and tools last many turns; cloth is consumed
  each turn. Single-turn QoL undervalues durables unless value covers the
  good's service life (existing decay rates give it).
- *Limit 4 — merging Pots into QoL.* Adding a storage term to QoL changes the
  outcome metric and forces recalibration of population, growth and fission
  pressure. Valuing Pots through food security keeps QoL unchanged.

**Alternatives:**

1. *Recommended — QoL-grounded values with a planner-only scarcity curve.*
   Keep the QoL outcome formula. For planning, value a need by
   `∂QoL/∂coverage × (1 − coverage)^γ` (γ > 0), so the value rises smoothly
   as coverage falls, and price intermediates (tools, Pots, materials) by
   shadow labor and food-security value. Behaviour changes, QoL calibration
   does not.
2. *Concave QoL itself* (for example CES across needs). Most principled
   design: diminishing returns become gameplay. Requires recalibrating
   population, growth and fission.
3. *Least-satisfied-need first (Liebig).* Raise whichever need has the lowest
   coverage. Simple, strongly scarcity-driven, no derivatives; weaker basis
   for trade prices and RL.

Uniform fill remains the default until one is chosen and calibrated.

**Trade objection (user, 29 September 2026).** Alternative 1 values a good
only through the village's own need coverage. Once local housing is covered
its value falls to zero, so a village would never build surplus housing for a
neighbor that needs it, even though trade makes that worthwhile. The
objection holds for any valuation that sees only local needs.

**Amended direction.** Value *goods*, not needs, and let a good draw value
from every use it has:

- *consumption* — local marginal QoL of the need it serves (the scarcity
  curve applies here, to needs);
- *exchange* — for trade, the local value of what the good can be exchanged
  for (net of transport), or a regional price once markets exist;
- *production input* — derived value for tools, materials and Pots through
  the labor or food security they provide.

A claim's value is the best of these. Local need-based values then act as
reservation prices: the least a village accepts to give a good up, and the
most it pays to acquire one. Trade demand enters the demand pass as export
claims carrying their exchange value, so surplus housing for a neighbor
competes for labor on equal terms with local needs. The fulfilment interface
already supports this: claims are a list, and a rule orders them by value.

Open design questions for the trade mechanic, not the planner: who sets
exchange terms or prices and when (a turn-boundary quote keeps demand-once);
whether production may be speculative or only against committed orders;
transport cost and supply range (#13); and what a village may know of a
neighbor's needs under filtered observations.

### 2. Farm Tools for expansion — dropped after measurement

Measured in Phase 3, it lowered QoL systematically (see decision 3) and the
exact staffing pass does not need the circularity it removed. The user
dropped it on 29 September 2026; expansion farmers keep their Farm Tool
demand. The original proposal follows for the record.

Farm Tools for expansion farmers become the next turn's demand. The demand
pass sizes Farm Tools for the fixed operating-farmer floor only, removing the
circular dependency between tool demand and the residual field workforce.
Expansion farmers work without newly planned tools in the turn they are
proposed.

### 3. Tolerance — accepted

Matched per-turn population, prior-cohort QoL and realized food within ±5% of
the current planner are acceptable. Larger deviation requires clear evidence
that the new method is better, because it may require recalibration.

**Measured noise floor.** A same-method control — the current bisection with
one more iteration, i.e. strictly finer precision — already moves per-turn
prior-cohort QoL by up to 3.5–9.5% on the 80-tribe checkpoints in late turns,
because small staffing differences diverge trajectories. Per-turn maxima on a
volatile world aggregate therefore cannot distinguish methods. Evaluations
should report the mean signed deviation over the 25 turns (bias; the control
stays within ±0.5%) alongside per-turn maxima, and compare both against a
same-method control run.

| 25 turns vs baseline, mean signed | Population | QoL | Food |
| --- | ---: | ---: | ---: |
| Control (bisection, 7 steps) | −0.20 to +0.14% | −0.45 to −0.20% | −0.27 to +0.26% |
| Phase 3 solver (shipped) | −0.47 to +0.43% | −0.57 to +0.03% | −0.38 to +0.45% |
| Phase 3 + decision 2 (dropped) | +0.10 to +0.66% | −4.66 to −2.39% | +0.01 to +0.67% |

Worlds: `start-80-11`, `start-80-23`, `start-80-1729`, `start-320-1729`;
Release, four workers, 25 post-opening turns each.

## Phases

- **Phase 0 — reference corpus.** Record automated-camp planner inputs and
  outputs on `start-80-11`, `start-80-23`, `start-80-1729` and
  `start-320-1729` across 25 turns as the differential fixture.
- **Phase 1 — exact refactor (implemented).** Compute both tool goods from one
  schedule run (`NeolithicProduction.ToolReplacementDemands`), evaluate each
  recipe's base demand once per tool schedule, reuse per-good demand within one
  `MaterialEconomy.Forecast`, reuse tool demand within the planner's final role
  pass, hoist the housing multiplier and herder reservation out of `Evaluate`,
  and evaluate full fulfilment before zero. Results: per-turn fingerprints
  match the prior commit on `start-80-11` (eight turns; scalar, four workers,
  reversed collections/partitions), `start-80-1729` (six turns) and
  `start-320-1729` (three turns); fixed and broad headless suites produce
  identical output. On `start-80-11`, planner CPU fell about 45% (≈1,460 →
  805 ms over eight turns), total turn CPU 20%, allocation 22% (1,157 → 905
  MiB) and wall time about 5% (2,315 → 2,197 ms, three alternating pairs; the
  work already ran on four workers).

  *Scope correction:* the planner's capped pass, `ToolReplacementDemands` and
  `MaterialEconomy.Forecast` are three different schedules (labor-only probe
  with a housing reserve, a two-layer tool-coverage balance, and the planner's
  capped material/food pass), not one algorithm called three times. Unifying
  them changes results, so the shared kernel moves to Phase 3.
- **Phase 2 — demand snapshot (implemented).** `ProductionDemand.cs` adds
  `CampProductionDemand.Build`, the planner's demand pass: footprint and field
  state, food supply and envelopes, herder reservation, known sources and
  material room, the prepared preview, per-good goals and the housing
  multiplier, built once per `Plan`. It also exposes `ProductionClaim`s (good,
  recipe, quantity, priority, value, role, rate) as the fulfilment-rule input;
  `Value` defaults to `Priority` and is unused by the current uniform rule.
  The existing search reads the snapshot unchanged. Results: per-turn
  fingerprints match Phase 1 on `start-80-11` (scalar, four workers,
  reversed), `start-80-1729` and `start-320-1729`; both headless suites are
  unchanged apart from six new demand-snapshot assertions (purity, one claim
  per good, quantities, deferred Farm Tools, specialist rates, determinism).
- **Phase 3 — fulfilment rule and path solve (partly implemented).**
  Implemented: `IFulfilmentRule` maps a path position `s ∈ [0, 1]` to a share
  per claim (`UniformFulfilment` is the default; `EconomicPlanner.Plan`
  accepts another rule), the staffing pass applies per-claim shares, and
  `SearchPath` replaces six-step bisection with interpolation toward the
  resident boundary plus a closing probe (same 1/64 tolerance and six-probe
  cap; halving after two rounds). With the uniform rule the first two steps
  were fingerprint-exact; the solver then evaluates 19–25% fewer staffing
  passes (80/11: 1,655 → 1,344 over eight turns; 320/1729: 3,320 → 2,501
  over three). Paired wall time at 80 tribes is within noise.

  *Why not a closed-form breakpoint solve:* the staffing pass calls the same
  `Demand`, `ToolReplacementDemands` and `Forecast` contracts as production,
  which is how planner forecasts stay consistent with the resolver. A
  closed-form labor curve would duplicate those contracts. The breakpoints
  are therefore found by evaluating the exact pass, not derived.

  *Decision 2 measured and dropped:* deferring expansion Farm Tools lowered
  prior-cohort QoL systematically (mean −2.4% to −4.7% over 25 turns; −5% to
  −7% over turns 11–25 on the 80-tribe worlds; single turns to −15%) with
  population and food unchanged. Its purpose was to break the Farm
  Tools/field-workforce circularity for a closed-form solve, which the exact
  pass does not need.

  *Not done:* unifying the three production schedules with the resolver
  (changes authoritative production; separate step). Gate: invariants (labor ≤ residents,
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
