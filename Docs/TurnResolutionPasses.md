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
| Interturn decision and preparation | Fully committed world at `(Turn, Days)`; filtered observations per policy | `AiPlanning.Decide` returns orders and traces. `TurnPlanner.Prepare` validates and copies orders, applies explicit-order precedence over automation, and freezes `TurnNeeds` for the prior cohort. It changes no authoritative world state. | `PreparedTurn` is bound to that world, turn, start day and duration. |
| Orders and movement | Locked orders and the prepared world | Apply economy intents in entity-ID order; compute herd intentions from the pre-movement view; dispatch established people, provision, move and deposit returns once. | Positions, connected supply, residents and assignments are fixed for resource resolution. |
| Turn setup | Post-movement entities, fixed needs and starting ecology | Establish field ownership and a fixed operating/workforce plan, reproduce herds, hunt, and construct all camp, hunter and herd claims for the whole horizon. These are computational pass writes, not subturn timestamps. | Every claim and average field efficacy is fixed before tile integration. |
| Resource resolution | Start ecological stocks, fixed claims, four-profile forcing, aggregate field efficacy and duration | Integrate each active tile once for the horizon. Commit endpoint food, biomass, water, condition and Wood, then integrated receipts and balances, in tile order. Reconcile field endpoint capital once from realized terrestrial gathering, without changing the claims already evaluated. | Only realized receipts reduce the fixed food/feedstock ledger. No claim or workforce is revised. |
| Needs and demographics | Realized resource and herd flows, fixed `TurnNeeds`, assigned prior cohort | Resolve managed herds, consumption, reserves, growth eligibility, shortages, production, goods, QoL and storage. Record pending deaths and births, then commit them after cohort needs. | Newborn needs and labor begin at the next preparation boundary. |
| Close | Completed endpoint | Reintegrate eligible hunters, bind ownership, progress societies, check conservation and invariants, append canonical history, increment turn. | The committed endpoint becomes the next decision boundary. |

## Rules protecting the pass order

- `EconomicPlanner.Plan` is an interturn read-only policy. AI may use it for a
  filtered observation, and `TurnPlanner.Prepare` may use it for a fallback
  automatic intent. `TurnResolver.Resolve(PreparedTurn)` never calls it or
  recomputes goods, materials or food targets.
- The prior cohort includes people present before births. Dispatch changes who
  is a camp resident without creating new people. Connected hunters share
  biological food demand; disconnected hunters use carried provisions and
  local forage. Consumption, full attainable operating reserve, growth,
  starvation grace, Fabric, Housing and QoL all apply to that cohort. Births
  and pending deaths commit only after production, needs and storage.
- No receipt retroactively changes orders, workforce, field efficacy, claims,
  target sizes or an earlier tile result. An endpoint field-capital correction
  may adjust its endpoint capacity and overflow ledger; it does not recompute
  food already awarded during the turn.
- Camp and village **land** gathering, cultivation footprints, material
  discovery and private land claim buffers are confined to radius one. Sea
  claims use the range rule of their producer; connected hunters may fish
  farther away. Distant hunter and herd claims remain independent and
  compete on shared tiles.
- Every shared tile has one ecological supply and one nonnegative harvest
  ledger. If future parallel writers debit it, guard each read/debit/write
  transaction with a tile mutex. Earlier claimants may exhaust available
  supply; no global tribe priority is required. The present tile integrator
  computes all claims together and merges private worker results in tile
  order. Shared deposit extraction and inventory changes remain canonical.
- Claims, transient source indexes, coefficient caches, horizon calculations,
  planner forecasts, diagnostics and worker buffers are not save fields.
  Worker count, partition order and timing never enter authoritative state.
  Scalar, two- and four-worker, reordered-collection and save-continuation
  runs must reproduce exact fingerprints within this model.

## Duration and work bound

`BaseDays` still defines a legacy unit and the four generated climate
profiles. `ReferenceIntervals = Duration / BaseDays` scales quantities such as
consumption or worker effort. It never sets a loop count. One-year, five-year,
ten-year and twenty-year turns use the same transition functions. The quiet
ecology path composes the four fixed seasonal affine maps algebraically;
active ecology reduces the four profiles to fixed coefficients and evaluates
one bounded stock-flow solve. The cultivation allocator has a fixed number of
capital-resolution bands per local candidate field, independent of duration.
No production path may reintroduce `LazyEcology.Segments` or a loop whose
iterations grow with `Duration / BaseDays`.

The accepted prior-cohort economy remains the behavioral baseline for
demographic semantics. The new horizon equations intentionally change some
ecological and field outcomes relative to seasonal stepping; differences are
calibrated and reported rather than hidden by old partition-equality tests.
