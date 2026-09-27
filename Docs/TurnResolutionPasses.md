# Turn-resolution pass contract

This is the normative one-way contract for the production resolver. The
September 25 seasonal implementation is preserved at commit `c33a5b7` for
calibration; its 80 agricultural barriers are historical, not a required
implementation pattern. See [TurnHorizonRefactor.md](TurnHorizonRefactor.md)
for the equations, duration inventory, validation and performance evidence.

One WEGO decision creates one authoritative transition from the committed
start to the committed end of `TurnRecord.Duration`. `TurnHorizon` captures
that duration in days, years, legacy reference intervals and practice scale.
The four climate profiles are forcing data inside analytical ecological
coefficients. They do not create seasonal or annual world states.

| Pass | Stable input | Permitted writes and output | Next barrier |
| --- | --- | --- | --- |
| Interturn decision and preparation | Fully committed world at `(Turn, Days)`; filtered observations per policy | `AiPlanning.Decide` returns orders and traces. `TurnPlanner.Prepare` validates and copies orders, resolves camp placement conflicts in a pure snapshot, applies explicit economy intent precedence, prepares automated economy intents for the effective destination, and freezes `TurnNeeds` for the prior cohort. It changes no authoritative world state. | `PreparedTurn` binds placements, intents, needs, world, turn, start day and duration. |
| Orders and movement | Locked orders, placements and the prepared world | Apply locked economy intents in entity-ID order; compute wild-herd intentions from the pre-movement view; dispatch established people, provision, move and deposit returns once. Domestic herds follow their camp. An accepted village move reverts it to a camp, releases its old radius-one field ownership, and leaves fixed housing at the old site. | Positions, connected supply, residents and assignments are fixed for resource resolution. |
| Turn setup | Post-movement entities, fixed needs and starting ecology | Establish field ownership and a fixed operating/workforce plan, reproduce herds, hunt, and construct all camp, hunter and herd claims for the whole horizon. These are computational pass writes, not subturn timestamps. | Every claim and average field efficacy is fixed before tile integration. |
| Resource resolution | Start ecological stocks, fixed claims, four-profile forcing, aggregate field efficacy and duration | Integrate each active tile once for the horizon. Land claims debit opening `G` and `B`; only after receipts are fixed does the tile recover for the next turn. Domestic herds claim eligible radius-one land forage and radius-one freshwater through the same finite tile ledgers as other claimants. Operated field intensity raises the human-accessible ceiling and managed recovery without adding food to current receipts. A departing village does no field work this turn and its gatherers can access at most bridge-level `q=0.25` food on former fields. Commit endpoint food, biomass, freshwater and Wood, then receipts and balances, in tile order. Reconcile field endpoint capital once from realized terrestrial gathering, without changing claims already evaluated. After all fields are committed, a cultivation camp whose own radius-one land footprint averages at least `q=0.25` becomes a village. | Only realized receipts reduce the fixed food/feedstock ledger. No claim or workforce is revised. |
| Needs and demographics | Realized resource and herd flows, fixed `TurnNeeds`, assigned prior cohort | Resolve managed herds, consumption, reserves, growth eligibility, shortages, production, goods, QoL and storage. Record pending deaths and births, then commit them after cohort needs. | Newborn needs and labor begin at the next preparation boundary. |
| Close | Completed endpoint | Reintegrate eligible hunters, bind ownership, progress societies, check conservation and invariants, append canonical history, increment turn. | The committed endpoint becomes the next decision boundary. |

## Rules protecting the pass order

- `EconomicPlanner.Plan` is an interturn read-only policy. AI chooses movement
  from filtered observations; `TurnPlanner.Prepare` uses the planner once for
  each eligible automated camp. `TurnResolver.Resolve(PreparedTurn)` never calls it or
  recomputes goods, materials or food targets. A camp Move does not suppress
  enabled automation; the destination-aware intent is prepared before movement
  and committed exactly once. An AI camp action may carry a transient request
  for that one plan even when its saved automation mode is Off, preserving the
  AI's previous per-turn staffing behavior. A player Move with automation Off
  retains its assignments.
- The prior cohort includes people present before births. Dispatch changes who
  is a camp resident without creating new people. Preparation evaluates
  accepted hunter destinations against the effective camp destination to
  freeze connected food consumers. Connected hunters share biological food
  demand; disconnected hunters use carried provisions and local forage.
  Provision withdrawals and deposited returns change only the remaining
  food target, not its prepared size. Consumption, full attainable operating
  reserve, growth,
  starvation grace, Fabric, Housing and QoL all apply to that cohort. Births
  and pending deaths commit only after production, needs and storage.
- Village status is saved camp state, decided at a turn boundary from the
  average intensity of its own fields over every traversable land tile within
  radius one. Village movement is an explicit abandonment decision by a
  player or an AI in a food emergency. It does not destroy physical field
  capital, but the departing camp cannot operate those fields this turn;
  formerly fixed housing remains at its original tile. Accumulated domestic
  expectations persist, so the ordinary food, clothing and housing QoL
  formula exposes the immediate loss. A blocked move does not abandon the
  village. This adds bounded local work per moving village, not a map scan.
- Domestic herd forage and water needs are likewise measured for the animals
  present before reproduction. Their realized minimum nutrition governs
  starvation and product yield; an adult shortage pauses births and fractional
  breeding progress. Fully supplied herds retain the configured growth curve,
  and newborn herd needs begin at the next preparation boundary.
- No receipt retroactively changes orders, workforce, field efficacy, claims,
  target sizes or an earlier tile result. An endpoint field-capital correction
  may adjust its endpoint capacity and overflow ledger; it does not recompute
  food already awarded during the turn.
- Camp and village **land** gathering, cultivation footprints, material
  discovery, domestic herd grazing and private land claim buffers are confined
  to radius one. Domestic herd drinking uses radius-one freshwater sources. Sea
  claims use the range rule of their producer; connected hunters may fish
  farther away. Distant hunter and wild-herd claims remain independent and
  compete on shared tiles.
- Every shared tile has one ecological supply and one nonnegative harvest
  ledger. If future parallel writers debit it, guard each read/debit/write
  transaction with a tile mutex. Earlier claimants may exhaust available
  supply; no global tribe priority is required. The present tile integrator
  computes all claims together and merges private worker results in tile
  order. Shared deposit extraction and inventory changes remain canonical.
- Land biomass `B`, human-gatherable food `G`, and freshwater are finite stocks.
  Realized harvest and drinking debit them; bounded recovery over elapsed `dt`
  prepares future stock and never revises current receipts. Every committed
  boundary satisfies `0 <= G <= B <= biomass capacity` and
  `0 <= freshwater <= water capacity`. Current Neolithic gathering, grazing,
  cultivation, timber and quiet projection do not change stored
  `LandCondition`; its saved value remains available to later disturbance
  mechanics. Aquatic condition retains its own rules.
- Claims, transient source indexes, coefficient caches, horizon calculations,
  planner forecasts, diagnostics and worker buffers are not save fields.
  Worker count, partition order and timing never enter authoritative state.
  Scalar, two- and four-worker, reordered-collection and save-continuation
  runs must reproduce exact fingerprints within this model.

## Duration and work bound

`BaseDays` still defines a legacy unit and the four generated climate
profiles. `ReferenceIntervals = Duration / BaseDays` scales quantities such as
consumption or worker effort. It never sets a loop count. One-year, five-year,
ten-year and twenty-year turns use the same transition functions. Land recovery
after opening claims and pure passive land projection share the same ordered
four-profile affine map, algebraically repeated across whole cycles with bounded
partial-cycle edges. A per-transition cache evaluates each distinct seasonal
capacity once for the fixed representative land state. Aquatic ecology and
claimed freshwater retain their bounded horizon stock-flow solve. The
cultivation allocator has a fixed number of capital-resolution bands per local
candidate field, independent of duration.
No production path may reintroduce `LazyEcology.Segments` or a loop whose
iterations grow with `Duration / BaseDays`.

The accepted prior-cohort economy remains the behavioral baseline for
demographic semantics. The new horizon equations intentionally change some
ecological and field outcomes relative to seasonal stepping; differences are
calibrated and reported rather than hidden by old partition-equality tests.
