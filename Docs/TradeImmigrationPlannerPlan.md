# Trade, immigration and planner refactor: implementation plan

Status: **proposed** (3 October 2026). This is the work order after merging
`features/demographic-structure` (portable-housing parameters, specialist
scale, QoL elasticity 1) into `features/herd-ecology-retention`. It sequences
existing designs. It does not replace them:

- trade: [ValueModelImplementationPlan.md](ValueModelImplementationPlan.md)
  WP7–WP9 and [QoLFissionTradeDesign.md](QoLFissionTradeDesign.md) §6–7;
- immigration: [Immigration.md](Immigration.md) (WP10b/WP10b-2, already
  implemented, opt-in);
- planner evidence: [SharedAllocationValidation.md](SharedAllocationValidation.md).

The phases run in order. Each ends with its own acceptance gate before the
next starts. Each is tested with the other features held fixed, so that
outcome changes can be attributed.

## Phase 1: trade alone

**Progress.** 1a, 1b and 1c are implemented for AI-to-AI trade of Cloth,
Animal Fabric, Housing Material and food, with the same-turn handshake:
[Trade.md](Trade.md). Order kinds, observations and the player panel are
deferred to the Unity workstation.

**Configuration under test.** Immigration off. Fission, QoL mortality, herd
labour and shared allocation are on, as in the census runs. A `TradeEnabled`
switch is added, false by default and in saves without it.

**1a. Foundation (WP7, no AI).**

- *Authoritative state:*
  - `Bid { Give, Receive, AcceptanceLimit }`;
  - requisition, acknowledgement (honour, counter, ignore) and close records;
  - a saved trade ledger of confirmed terms, deliveries and lapsed or ignored
    requisitions.
- *No pending state is saved:* every handshake finishes or lapses within its
  turn.
- *Resolution:* one canonical exchange step after production and before
  needs. It conserves both sides; a shortfall scales both sides
  proportionally. The resolver invariants are extended accordingly.
- *Orders and observations:* order kinds to send and answer requisitions,
  validated by `OrderRules` and `LegalActions`. Observations show only this
  turn's requisitions addressed to the camp, its own ledger and each partner's
  past requisitions to it. Never foreign stores, values or messages.
- *Partners:* known settlements (`ContactKnowledge`) within trade range.
  `ContactKnowledge.RecordTrade` and `TechnologyDiscovery.RecordTradePractice`
  get their first callers.
- *Goods:* the tradeable set is configuration, starting with T0 (Housing
  Material, Cloth).

**1b. Handshake in the planning pass (WP8).** The pass splits into three
stages:

1. **Demand pass, parallel per camp.** It already computes every value curve
   from the committed boundary.
2. **Bounded handshake, canonical and sequential.** Requests are ranked by
   `IntegratedGain − ExpectedGiveCost`. Each camp sends at most `K = 3`; there
   is one handshake per pair per good. Responders process in value order, ties
   by camp ID and good.
3. **Fulfilment plan, parallel per camp.** Confirmed trades enter it as fixed
   export and import claims.

So planning stays parallel. Trade adds one synchronization barrier and an
`O(camps × K)` canonical step, not a sequential plan. A lagged market (plans
read last turn's committed offers) is the fallback if the barrier costs too
much.

`TurnPlanner.Draft` keeps its key-checked reuse. Trade adds the WP8 recompute
triggers: the player's camps, their counterparties, and camps whose
destination changed.

**1c. Food trade (WP9).** This adds food to the tradeable set (TF) with
seller bands:

- protected survival food (consumption, clothing feedstock, operating
  reserve) is never tradeable;
- growth food is valued at marginal welfare;
- safe surplus is the cheapest export.

Confirmed imports enter the prepared food envelope before demographic
planning. Short delivery scales growth and reserve. No food unit counts for
two camps' growth.

**Planner interface (frozen for phase 3).** The planner sees confirmed trades
only as fixed claims on `CampProductionDemand`: imports add supply, exports
add a demand carrying the agreed quantity and value. The handshake never calls
into the fulfilment search. Phase 3 can therefore rewrite the planner behind
this interface.

**Scaling law.** Per turn:

```
O(camps · demand pass)                          (existing)
+ O(camps · K · partners-in-range)              (request scoring)
+ O(confirmed trades)                           (exchange)
```

Partners are bounded by contact range, so trade cost follows camps, not map
size. Handshake work, End Turn latency and the barrier wait are measured at
80 and 320 tribes and on a five-world 50-turn run.

**Validation.**

- *Headless (new `TradeChecks`):*
  - ledger and food conservation, and save round trip;
  - order validation and masks, and observation filtering;
  - proportional scaling on shortfall;
  - integrated valuation that is monotone with diminishing gain;
  - no handshake surviving its turn;
  - scalar versus 2/4-worker and reversed-partition replay with trade active;
  - draft versus sequential fingerprints;
  - the protected food band is never debited;
  - unconfirmed food never supports births.
- *Two- and three-camp fixtures:*
  - complementary surpluses trade;
  - identical camps do not;
  - kin terms change the margin only;
  - a trade that would breach useful local demand is refused.
- *Five-world census, trade on versus off:* trades per camp-turn,
  goods/QoL coverage, population and herd distributions (the husbandry-first
  −5% / <10× gate applies), village differentiation, and the
  proto-town diagnostic (§8.3).
- *Unity:* the planning-panel UI (trade ledger, answers as orders) needs the
  Windows workstation. It cannot be built or verified in this cloud
  environment. Phase 1 can ship AI-to-AI trade first and the player panel
  second.

## Phase 2: immigration

Immigration exists (opt-in) and has its own fixed checks. The phase is to
re-validate it on the merged branch and decide its default; no new mechanic is
planned unless the measurements demand one.

1. **Re-measure with trade off, then on.** On the five worlds, record:
   - flows and two-turn reversal rate (WP10b-2's acceptance metric);
   - QoL change at source and destination;
   - census distributions and the husbandry gate;
   - fission interaction (sources sit out fission);
   - immigration's O(N) cost counters.

   QoL elasticity is now 1 (the merged default), and `Fission.QoLModel` sees
   traded goods, so the WP10b-2 numbers must be re-taken.
2. **Fix what the measurements show.** Candidates already named in the design:
   - the deferred opportunity term `O_j − O_i` (specialist pull towards trade
     centres), now possible because trade exists;
   - lifestyle barriers (nomadic↔sedentary costs).

   Changing these is outcome-changing and needs a census.
3. **Decide `ImmigrationEnabled`'s default** (Handoff §5 item 4) from those
   measurements.

## Phase 3: economic planner refactor

Measured problems to solve (all in [SharedAllocationValidation.md](SharedAllocationValidation.md)):

- **Cost.** About 1.3–1.4 ms per camp-turn, linear in camps; a saturated
  Standard map projects to 10–25 s of planning per turn on 4 cores. Most of it
  is a full craft-scheduler rerun inside each fulfilment evaluation and the
  per-choice allocation set-up. Caching, warm starts and grouping have been
  measured and recovered only about 5–20%.
- **Valuation gaps found by the husbandry audit:**
  - spare labour priced at zero buys field expansion even on dry grassland;
  - the village threshold's loss of pasture range is never priced against the
    herd;
  - the herd continuation growth cliff.
- **Two planners.** The legacy and shared paths coexist behind
  `SharedCropHerdAllocationEnabled`.

Proposed direction, to be designed in a short note for approval before code
(drafted: [PlannerTradeStorageDesign.md](PlannerTradeStorageDesign.md), with the
planner-first trade order and pottery storage; the architecture is proposed in
[EconomicControlContract.md](EconomicControlContract.md) with
[Addendum A](EconomicControlContractAddendumA.md), which these bullets become requirements of):

- **Separate valuation, allocation and realization.** Prices come from the
  demand pass. Allocation is a closed-form or bounded portfolio over
  subsistence, herds, fields and trade claims. The craft scheduler then
  realizes the result **once** per plan, instead of once per search step.
  That targets the per-plan constant, which is the remaining cost lever.
- **One planner path.** Retire the legacy path, or freeze it as a reference
  only, after a census against the shared path.
- **Price what the audit found unpriced:** the opportunity cost of spare
  labour, the village and pasture transition, and continuous herd growth.
  Each change gets a census and the husbandry gate.
- **Keep the phase 1 trade interface unchanged.**
- **Acceptance:**
  - census gates as above;
  - exact or gated outcomes;
  - per camp-turn preparation cost against the control at 25/50 turns;
  - the 80/320-tribe scale runs;
  - a stated per-plan cost target agreed before work starts.

## Cross-cutting rules

- **Determinism:** every new pass is pure per camp, followed by a canonical
  commit. Scalar, worker, reversed-partition and save-continuation replay are
  required for every phase.
- **Saves:** all new fields are `OptionalField`, and absent fields load with
  the feature off. `ANTHROPOCENE-11` stays unless a format change is
  unavoidable; observation-schema bumps are documented.
- **Information:** trade and immigration add no hidden information to
  observations. The immigration estimator's true-state read stays confined to
  the resolver population process.
- **Evidence:** each phase updates `CurrentSimulationContract.md`, the AGENTS
  map and its validation document, and records seeds, configuration and
  binaries.
