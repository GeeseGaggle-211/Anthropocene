# Turn resolution pass contract

This is the current, normative pass order for the prepared-turn economy on
`features/neolithic-agricultural`. When changing a pass, preserve its input
boundary and write ownership. A later pass may consume a receipt or advance
state from an earlier pass; it must not revise that pass's claims, workforce,
allocation, or outcome. Update this document and its replay checks together
when the order deliberately changes.

The default decision spans 7,300 days (twenty years). `BaseDays` is 91.25, so
an agricultural dependency tile crosses 80 seasonal outer barriers. These
are ecological and economic intervals inside one WEGO decision, not 80
movement, birth, or order turns. Other turn durations use the corresponding
ordered `LazyEcology.Segments`.

## Boundaries and write ownership

| Pass | Stable input | Permitted writes and output | Barrier before the next pass |
| --- | --- | --- | --- |
| Interturn decision and preparation | Fully committed world at `(Turn, Days)`; each policy receives its filtered observation | `AiPlanning.Decide` returns orders and traces. `TurnPlanner.Prepare` validates and copies orders, applies explicit camp-order precedence over fallback automation, and creates transient `PreparedTurn` orders and `TurnNeeds`. It does not change authoritative state. | A prepared turn is bound to that exact world object, turn, and day. The compatibility `Resolve(world, orders, traces)` overload prepares before entering resolution. |
| Orders and movement | Locked orders and the prepared world | Apply economic intents in entity-ID order; calculate herd intentions from the pre-dispatch view; dispatch established people, provision departures, move camps/hunters/herds with canonical collision rules, and deposit returns. New groups receive IDs here but cannot act as established hunters this generation. | Positions, connected supply, resident assignments, and stores are final for resource resolution. |
| Ecology setup | Post-movement entities and fixed needs | Prepare field state, record the starting demographic cohort and reserves, reproduce herds, resolve hunting, and build stationary camp, fishing, disconnected-hunter, and herd claims. | Initial active tiles are materialized to the interval start before integration workers read them. |
| Resource resolution | Post-setup claims, ecological stocks, fixed food and plant-input targets | Integrate independent active tiles once over the whole interval. For the agricultural dependency set, execute the ordered seasonal barriers below. Commit tile state, wood state, ecological balances, and realized transfers in stable tile order; then deliver food, freshwater, and forage receipts. | Only realized receipts reduce remaining targets. No food or plant-input target is recomputed from births or new inventory. |
| Needs and demographics | Realized food and herd flows, fixed `TurnNeeds`, and the assigned prior cohort | Resolve managed herds; plan growth; pay camp/connected-hunter food and separate disconnected-hunter provisions; record shortage and pending births/deaths; produce goods and gather materials; pay Fabric, Housing, and QoL needs; apply storage. Commit pending deaths and births after production and needs, advance `Days`, and finish demographic reports. | The endpoint state, including newborns, is visible only to the next prepared turn. |
| Close | Completed generation state | Reintegrate eligible hunters, bind owners, apply society progression, validate conservation and affected tiles, coalesce indistinguishable seasonal transfers, append canonical history, and increment `Turn`. | The committed world becomes the next decision boundary. |

The demographic cohort consists of people present before this generation's
births. A valid dispatch moves existing people from camp-resident goods and
QoL needs to hunter provisioning; connected hunters remain in shared
biological food demand, while disconnected hunters use carried provisions
and local forage. `TurnNeeds.Residents` and its production, goods, housing,
and feedstock goals are frozen before resolution. The fixed food target
guides claims; actual consumption and starvation use the same existing
people after dispatch and supply connectivity are resolved. Consumption
precedes a full attainable operating-reserve refill, which precedes growth.
Shortage and the existing starvation grace apply to those people. Birth
progress can be calculated earlier, but newborns are committed only after
the cohort's production, goods, QoL, and storage are complete. They first
consume or request goods in the following turn.

`EconomicPlanner.Plan` is an interturn, read-only policy. AI may request its
recommendation while building a filtered decision observation, and
`TurnPlanner.Prepare` may produce one fallback automated intent when no
explicit camp order was submitted. Ordinary observations and order
validation do not run it. `TurnResolver.Resolve(PreparedTurn)` must never
call it or change the frozen need ledger. Forecasts and the local source
index are transient; they are neither save fields nor permission to create
food, material, equipment, or people.

## Agricultural seasonal barrier

`CommitGenerationResources` marks the agricultural dependency set before
integration: each agricultural field footprint and eligible camp-gathering
tile, plus any tile carrying a claim linked to an agricultural camp. All
competing claims on one of those tiles cross the same seasonal barriers.
An independent active tile has no field-state dependency and is integrated
once over the whole interval by `EcologicalPressure.Integrate`; that call
still advances the seasonal stock equations internally. Its result can be
committed after the agricultural loop because the tile sets are disjoint.

For each seasonal segment, in this order:

1. Copy that segment's initial claims. Materialize fields to the segment
   start and settle field ownership with `CommitSeasonBoundary` in camp-ID
   order. This is a write barrier.
2. Calculate labor from the committed field state, then commit operating
   fractions. No claim calculation may observe half-updated operating state.
3. Construct agricultural gathering and field claims from the post-write
   state. The previous segment's realized receipts reduce the fixed
   biological and plant-input ledgers. Scale connected fishing and local
   claims against the same remaining camp target. Claims are requests, not
   stock debits.
4. Group all claims by tile, integrate each tile from the same segment-start
   snapshot, and return value results. Integration workers do not mutate
   entities, source ledgers, IDs, or history.
5. Commit tile and wood results and realized receipts in canonical tile
   order. Update accepted-food and plant-input ledgers only from those
   receipts. Then consume covered farm-tool work and expand or abandon fields
   in camp-ID order. Expansion changes the next segment's state; it does not
   revise this segment's harvest.

The outer barriers are required by the current field model: construction,
ownership, maintenance, and tool-supported operating fractions can change
between seasonal receipts. A single turn-wide fixed claim cannot reproduce
that dependency with the present equations. Prior 20- and 40-barrier
experiments failed isolated dry-site transition comparisons; changing the
barrier count requires a validated analytic field model, not only a Standard
aggregate comparison.

## Locality, shared resources, and deterministic commits

- Camp and village **land** gathering, cultivation footprints, land
  material discovery, and future private land-claim buffers cover at most
  the settlement's immediate radius-one tiles. Configuration currently
  validates `GatherRadius == 1`, and present passive coastal gathering also
  uses that setting; this is not a permanent range invariant for sea claims.
  `MaterialAccessIndex` maps radius-one land tiles to deposit sites and
  current source state without scanning every deposit for each query.
  Movement changes the radius-one land neighborhood for the next query.
- Sea claims follow the range rule of their producer. A connected hunter
  can already fish farther from its source camp; a later fishing pass may
  introduce other longer-ranged sea claims. Do not reject a sea claim or
  constrain a future private claim buffer merely because its tile is more
  than one step from the camp. Disconnected hunters and herds claim at their
  own tiles, and herd drinking can use an adjacent freshwater tile. All
  claims still compete when grouped on the same tile; connected fishing
  still counts against its camp's fixed food target.
- In this checkpoint, `EcologicalPressure.Integrate` integrates a shared tile
  once and divides its realized flows proportionally among claimants; the
  result commits in tile order. A later sequential-debit allocator may instead
  let one tribe consume from a tile-local working state, then let the next
  tribe see and consume only the remainder. If parallel writers are added,
  guard each read/debit/write transaction with that tile's mutex. Apply
  ecological renewal only once per segment, never let a stock become negative,
  and preserve `Food <= Biomass`. Scarcity may leave a later tribe with zero;
  no global tribe priority is required. Shared deposit extraction, wood
  conversion, inventory changes, entity IDs, and `TurnRecord` receipts still
  commit in canonical order. Endpoint timber extraction can apply new wood
  pressure; it cannot rewrite earlier seasonal food receipts. Parallel
  evaluation may fill private indexed results only. Worker count, partition
  order, timings, and diagnostics are not authoritative state.
- Maintain conservation of population, food, biomass, freshwater, and finite
  materials. Scalar, two- and four-worker, reversed-partition,
  reordered-collection, and save-continuation runs must retain exact
  fingerprints. A parallel-only change has no balance-variance allowance.

## Performance status of this checkpoint

The prior-cohort economy is the behavioral baseline, including its
intentional difference from the former ending-population QoL denominator.
It is not yet a matched-`main` performance success. The September 25
warmed audit measured combined planning-plus-resolution medians of 249.0 ms
versus 190.1 ms at Standard/80 and 933.0 ms versus 504.0 ms at
benchmark-only Standard/320. These are a 1.31x and 1.85x gap respectively;
see [Performance.md](Performance.md) for seed, worker settings, turn counts,
and remaining limitations. Moving work between planning and resolution
does not close that gap. Report both phases together, actual tile
integrations, seasonal preparation, allocations, and production when
changing this pipeline.
