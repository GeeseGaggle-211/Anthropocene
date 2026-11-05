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
| Interturn decision and preparation | Fully committed world at `(Turn, Days)`; filtered observations per policy | `AiPlanning.Decide` returns orders and traces. `TurnPlanner.Prepare` validates and copies orders, resolves camp placement conflicts in a pure snapshot, applies explicit economy intent precedence, prepares automated economy intents for the effective destination, and freezes `TurnNeeds` for the prior cohort. When `TradeEnabled`, it first computes the **trade handshake** (`Trade.Plan`, pure over the committed boundary, independent of orders; [Trade.md](Trade.md)), and the automated intents and `TurnNeeds` see each trading camp's planning view: imports and stock-settled exports as stock, goods it will make for export as export claims (`TurnNeeds.Exports`). Feature-enabled Watercraft opens the shared goods-economy capability whether or not a ground is currently available. DemandControl prepares workforce and Fishing Tool demand; only a camp with a legal Shallows/Lake source receives a non-empty resident `FishingPlan`, which freezes accepted location, cohort, selected grounds, route distances, work, tool service, catch requests and capital projection. Candidate scoring uses visible ecology, the Society's own visited-ground snapshots, or the conservative zero-yield/zero-capital prior for remembered but unsurveyed geometry. The plan is immutable resolution input. With `ElderKernelSplit`, DemandControl scores a bounded grid of clothing and shelter levels (and, with `ElderMixedRoutes`, the other route of a farming and herding camp) with the planning evaluation of the demographic kernel and commits the best as a Protected class ahead of Continuity; the order carries `ProtectedClothingUnits`, which `TurnNeeds` freezes as protected clothing feedstock. Prepare does not write world state. | `PreparedTurn` binds placements, intents, needs, the trade plan, any eligible `FishingPlan`s, world, turn, start day and duration. |
| Orders and movement | Locked orders, placements and the prepared world | Apply locked economy intents in entity-ID order; move trade food legs into escrow (pay on delivery) in agreement order; select at most one field-attracted eligible wild herd per agricultural village in the configured detection radius and compute wild-herd intentions from the pre-movement view (the q-weighted score preserves the existing movement gate and flee branch); dispatch established people, provision, move and deposit returns once. Domestic herds follow their camp. An accepted village move reverts it to a camp, releases its old radius-one field ownership and fishery operators, and leaves fixed housing at the old site. A camp that departs cannot acquire fishery management during the departure turn. | Positions, connected supply, residents and assignments are fixed for resource resolution. |
| Turn setup | Post-movement entities, fixed needs and starting ecology | Establish field ownership and a fixed operating/workforce plan, reconcile at most one maintaining operator per sparse fishery site, reproduce herds, hunt, and construct all camp, resident-fishing, hunter and herd claims for the whole horizon. A valid prior operator is retained while reachable and supplying positive work; otherwise the lowest eligible camp ID wins. A losing camp can still claim shared aquatic stock but receives no management credit and is not replanned. These are computational pass writes, not subturn timestamps. | Every claim, operator, aquatic overlay and average field/fishery efficacy is fixed before tile integration. |
| Resource resolution | Start ecological stocks, fixed claims, four-profile forcing, aggregate field/fishery efficacy and duration | Integrate each active tile once for the horizon. Land claims debit opening `G` and `B`; aquatic passive, Hunter, and resident `FisheryHarvest` claims share one finite tile pool. Only after receipts are fixed does the tile recover for the next turn. Domestic herds claim eligible radius-one land forage and radius-one freshwater through the same finite tile ledgers as other claimants. Operated field intensity raises the human-accessible ceiling and managed recovery without adding food to current receipts. Fishery intensity raises aquatic human accessibility by the explicit quadratic overlay and adds managed aquatic flow proportional to `q × B × recovery × operation`; it never raises biomass capacity or changes land ecology. Passive gathering cannot separately promise stock already allocated to resident Fishing. A departing village does no field work this turn and its gatherers can access at most bridge-level `q=0.25` food on former fields. Commit endpoint food, biomass, freshwater and Wood, then receipts and balances, in tile order. Reconcile field and sparse fishery endpoint capital once from their prepared work and realized claims, without changing claims already evaluated. After all fields are committed, a cultivation camp whose own radius-one land footprint averages at least `q=0.25` becomes a village; a Fishing village must also meet both the prepared/realized worked-ground maturity and managed-meal-support thresholds (each at least `0.5`). The corrected aquatic seasonal-pressure reducer allocates the fixed horizon receipt across the four exposure profiles for damage accounting only; it conserves that receipt and creates no intermediate state. | Only realized receipts reduce the fixed food/feedstock ledger. No claim or workforce is revised. |
| Needs and demographics | Realized resource and herd flows, fixed `TurnNeeds`, assigned prior cohort | Resolve managed herds and escrow the finite food budget, then resolve production, goods and physical needs. Protected clothing feedstock (`TurnNeeds.ProtectedClothing`) is honoured before meals. When trade has goods legs, every camp produces first, the goods settle in agreement order, escrowed food pays the delivered share and every camp resolves needs; otherwise production and needs remain one pass. Under `PhysicalMortalityV1`, close the paid food/textile person-years with dynamic shelter (with `SurplusFundedBirths`, births occur at `BaseBirthRate x g`, `g` the largest fraction whose newborns the food left after the resident cohort's meals feeds; Docs/ClothingCoverageAndSurplusBirths.md), recompute QoL only as experienced welfare, return unused typed provisions, and record pending gross births and cause-specific deaths. Apply storage to the locked cohort, then commit the single net resident/workforce change. The legacy model retains its old growth/starvation path. | Newborn needs and labor begin at the next preparation boundary. |
| Close | Completed endpoint and realized practice receipts | Reintegrate eligible hunters, bind ownership, resolve the first breakthrough and acquire independently qualified domains. Then resolve [peaceful field contact](FieldDomestication.md) once for each selected herd/Society: current village sight and no attempted hunt are required, missing contact resets the streak, and threshold acquisition transfers the existing herd and unlocks Husbandry without issuing current-turn products or claims. Construct one transient, on-demand `SocialRelocationForecastCache` from the post-demography endpoint. Under `PhysicalMortalityV1`, immigration then fission use the same bounded physical-service counterfactual and mover/source/incumbent/aggregate constraints; under `LegacyQoLNetGrowth`, each retains its exact former QoL law. Immigration considers only known local camps and a deterministic bounded size shortlist, canonically includes pending receiver resources/reservations, then revalidates every remaining flow against the exact joint endpoint (including each camp's frozen-resident departure); endpoint-indexed caches invalidate and retry only incident dependencies after a failed batch. Commit all departures before arrivals, and never let arrivals re-emigrate in the same Close. Immigration participants sit out fission. Before its known-site search, Physical-v1 fission may reject only through a permissive common-envelope upper bound (exact source plus optimistic full-service daughter); historical coverage is not an input, and `FissionSearchFraction` only lowers the valid upper-bound request threshold. Only the common finite-capacity evaluator may accept crowding or distress fission. Both passes conserve transferred workforce and physical state. They do not rerun the economy or demography: arrivals and daughters enter real production, needs, births and deaths next turn. Then capture settlement/lineage welfare diagnostics. `ContactKnowledge.EndTurn` records/prunes owner-specific Fishing ground snapshots with endpoint accessible food, biomass, condition, productive profile, fishery capital/operator and observation day; snapshots are sparse and pruned with their explored-memory blocks. Resident `FisheryHarvest` credits Fishing practice once using `ActiveFishingPracticeMultiplier` during domain acquisition. Conservation, history and turn increment follow. | The committed endpoint becomes the next decision boundary. |

## Rules protecting the pass order

- Trade ([Trade.md](Trade.md)) is agreed once, in preparation, from the committed
  boundary; nothing pending is saved. Food legs leave their giver into escrow after the
  economy intents and before dispatch, so provisions and the food ledger see the
  reduced stock that the prepared needs saw. Goods legs settle between all camps'
  production and all camps' needs, so a seller's export claim (staffed first by its
  planner) is made this turn and a buyer's needs see the delivery; escrowed food then
  pays on delivery. No step plans or revises any claim: a short delivery is scaled,
  never re-negotiated.

- The social-relocation passes ([Immigration.md](Immigration.md),
  [Fission.md](Fission.md)) run once per resolution, after
  `BreakthroughBound` and `TechnologyDiscovery.Resolve` and before
  `ContactKnowledge.EndTurn`. Their shared forecast cache and parallel phases never
  write authoritative state; all writes (cache
  updates, new entities, moved ledgers) happen in the canonical serial commit. A fission
  policy order is applied with the economy intents at the start of resolution, so it is
  in force for that turn's pass. No same-turn multi-hop, no rerouting of a dropped
  proposal, no QoL recomputation after a transfer. In physical mode, both passes use
  the same pure realizable-service estimate and explicit mortality hazards; crowding
  and distress identify the fission source condition but do not select another law.
  Legacy mode retains the former committed-QoL estimates.

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
  food target, not its prepared size. Consumption, the clothing-need fabric
  feedstock, full attainable operating reserve, growth together with every
  other manufacturing input,
  starvation grace, Fabric, Housing and QoL all apply to that cohort. Births
  and pending deaths commit only after production, needs and storage.
- In `PhysicalMortalityV1`, `QoLMortality.MaximumRate` supplies only the exact
  birth-only food envelope `0.045/year`; it never reads QoL, deficit state or the
  old growth multiplier. `PhysicalDemography` then closes realized nutrition and
  clothing against the same person-years and integrates exposure, crowding,
  disease and the named post-grace starvation hook with dynamic shelter. Gross
  birth/death ledgers reconcile once after storage, so equilibrium turnover does
  not erase specialists and newborns cannot fund or store their own goods. The
  committed QoL remains the existing food/water/expectation/material/cohesion
  experienced-welfare signal used by economics, the Elder reward and the welfare
  component of social counterfactuals. Social survival risk is forecast from the
  physical inputs and hazards, never from composite QoL. See
  [DemographyQoLContract-v1.1.md](DemographyQoLContract-v1.1.md) and
  [UnifiedSocialRelocationContract-v0.2.md](UnifiedSocialRelocationContract-v0.2.md).
- `LegacyQoLNetGrowth` preserves the former preparation rule: committed QoL may
  reduce the net-growth ceiling, `DeficitTurns > 0` may prohibit growth and
  starvation is the old discrete loss. This path exists for unmigrated saves and
  explicit compatibility fixtures; it is not the new-world demographic law.
- Clothing-need feedstock is paid ahead of the reserve refill. Its amount is
  `NeolithicProduction.ClothingNeedFeedstock`: the plant food (Cloth) and animal
  food (Animal Fabric) of the fabric covering
  `max(0, residents * FabricConsumptionPerPersonTurn * years / ReferenceTurnYears
  - Cloth stock - Animal Fabric stock)`, met in `ResolveNeeds` order (Cloth, then
  Animal Fabric) and capped by the planned fabric output. `TurnNeeds` records it
  (`PlantClothingFeedstock`, `AnimalClothingFeedstock`); its feedstock-harvest gap
  may use inventory beyond this turn's meals (`NeolithicProduction.FeedstockGaps`),
  while every other feedstock still needs inventory beyond meals, clothing and the
  operating reserve. `PlanGrowth` pays the clothing part first (capped by typed
  stock left after meals), so the reserve-refill and growth budget shrink by it even
  when the reserve is short, and the remaining feedstock shares the growth budget as
  before. `ConsumeCampFood` already protects the typed input from meals and growth
  spending. The planner's envelopes use the same definition: the fabric recipes
  funded as clothing need see food after meals, all other work sees food after the
  reserve. No saved field is added.
- Village status is saved camp state, decided at a turn boundary from the
  average intensity of its own fields over every traversable land tile within
  radius one. Village movement is an explicit abandonment decision by a
  player or an AI in a food emergency. It does not destroy physical field
  capital, but the departing camp cannot operate those fields this turn;
  formerly fixed housing remains at its original tile. Accumulated domestic
  expectations persist, so the ordinary food, clothing, housing and cohesion QoL
  formula exposes the immediate loss. A blocked move does not abandon the
  village. This adds bounded local work per moving village, not a map scan.
- Domestic herd forage and water needs are likewise measured for the animals
  present before reproduction. Their realized minimum nutrition governs
  starvation and product yield; an adult shortage pauses births and fractional
  breeding progress. Fully supplied herds retain the configured growth curve,
  and newborn herd needs begin at the next preparation boundary.
- Controlled slaughter (Docs/HerdLabour.md) is the last step of the managed-herd
  pass: after breeding, a camp's animals above its prepared `HerdTarget` leave
  its herds (id order, proportional) and their carcass food joins the camp's
  Animal Food as a realized flow that claims no tile. It reads only the target
  fixed at preparation and mutates only herd counts and that store, so the
  turn's forage, water and management receipts are unchanged.
- No receipt retroactively changes orders, workforce, field efficacy, claims,
  target sizes or an earlier tile result. An endpoint field-capital correction
  may adjust its endpoint capacity and overflow ledger; it does not recompute
  food already awarded during the turn.
- Secondary subsistence discovery uses the society's ongoing domain practice,
  the configured practice-point threshold and the existing forced-transition
  carryover for its selected secondary domain. The 200/240-year majority and
  forced rules decide only the first Paleolithic breakthrough. Later acquisition
  records knowledge without repeating the opening animal or improvement grant.
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
- Resident fishing grounds are sparse (`FisherySite`, keyed by aquatic tile),
  not flags or capital fields on every tile. Before Sailing, each eligible
  Watercraft camp routes through traversable land to Shallows-only and
  Lake-only sources, selects no more than three grounds within water-kind
  range three, and discounts effort by `0.75^(distance-1)` (distance one is
  undiminished). All scoring, selected-ground state, operator reconciliation,
  fishery work, and claims use the accepted destination and one immutable
  prepared plan. Fishing Tools cover only harvest Fisher-years: each unit
  covers 20 such years and gives 1.5x catch on covered work; the same effective
  Fisher-work budget also supplies maintenance and building without subtracting
  the requested harvest work. Resident claims share the tile's finite aquatic
  stock with Hunter and passive claims. A maintained site's accessibility is
  `G = G0 + (B - G0) q²`; managed aquatic flow is
  `0.65 q B r operation`. This raises neither biomass capacity nor terrestrial
  recovery. Unsurveyed remembered grounds use a zero-yield, zero-capital prior.
  Policies see only live ecology for currently visible tiles and their own
  coarse dated snapshots outside vision; no policy or player inspector receives
  hidden live aquatic stock. `NeolithicFishingEnabled` and
  `CorrectedAquaticSeasonalPressureEnabled` default off for legacy loads, and
  resident Fishing requires both gates plus DemandControl. These conditions
  leave feature-off opening FisherySeed rewards and fingerprints unchanged.
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
