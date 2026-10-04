# Planner-led trade and pottery storage: design note

Status: **approved**, 4 October 2026 (open questions resolved in §7). Phases S, C and V implemented (§3.6–3.10); R (planner refactor) next. This is the short note that phase 3 of
[TradeImmigrationPlannerPlan.md](TradeImmigrationPlannerPlan.md) asks for before planner code.
It sets the order in which the new economic planner and trade interact. It also adds a storage limit for finished
goods.

It follows [QoLFissionTradeDesign.md](QoLFissionTradeDesign.md):

- §5: value curve `v_X(c) = V_X e^{-λc}`, the shadow labour price `w`, and value by best use;
- §6: bounded handshake;
- §7: food bands.

It keeps the trade mechanics in [Trade.md](Trade.md) and [TradeHandoff.md](TradeHandoff.md), except where its
valuations come from.

## 1. Decisions

| Decision | Choice | Source |
| --- | --- | --- |
| Planning order | Valuation forecast first; under-served goods are marked for import and over-served goods for export. Then the handshake, then the fulfilment plan. | User, 4 Oct |
| Trade input | Requests and offers are built from the forecast's marks and curves. | User, 4 Oct |
| Storage pool | One pool shared by food and goods, scaling with Pots. | User, 4 Oct |
| Storage scope | All finished goods; raw materials (Stone, Clay, Wood) are excluded. | User, 4 Oct |
| Space per good | Every counted good takes the same space per unit. | User, 4 Oct |
| Overflow | Goods additions are **blocked**, never destroyed. | User, 4 Oct |
| Labour price in the forecast | Last turn's committed `w` (`EconomicLaborPlan.LabourPrice`). | User, 4 Oct (O5) |

## 2. Turn pipeline

All of this happens in `TurnPlanner.Prepare` (and `Draft`) from the committed boundary. Nothing below writes the
world until the resolver.

### P1. Valuation forecast

Pure and parallel per camp.

**Per good X, it computes:**
- useful demand `D_X`;
- coverage `c = stock / D_X` (pre-decay terms as in §5.1);
- the curve `V_X e^{-λc}`;
- the make cost `κ_X = w_{t−1} · labour_X + inputs`, with inputs at the camp's food value;
- the make-to-order capacity;
- the camp's free **storage room** (§3).

**It also marks each good:**

| Mark | Condition | Meaning |
| --- | --- | --- |
| **Import** | `c < 1` and `v_X(c) > 0` | Ranked by the value recoverable up to `min(c = 1, room)` (§6.3 integrated gain). |
| **Export** | Stock above useful demand (`c ≥ 1`, so no local-use value), or spare make capacity | It can be given without losing local use, or made at `κ_X`. |
| None | Otherwise | Neither listed nor offered. |

**Limits:**
- No fulfilment search and no craft scheduler run, so cost is `O(goods)` per camp.
- The work reuses the demand inputs that the planner's demand pass already builds (`CampProductionDemand`).
- Food keeps its §7 bands; it is marked Export only from safe surplus (or growth food, under the opportunity-cost
  policy).

**Output.** One curve schedule per curve key, in the form of `TradeValueSchedule` (bands or need curve, offset,
floor, make cost), plus the marks.
- These are **same-turn and transient**. They are handed straight to the handshake and never saved.
- The saved, lagged `EconomicLaborPlan.TradeValues` path built for the interim stays as a fallback until P1
  covers every good (O6).

### P2. Handshake

This is the existing `Trade.Plan`, with these changes:
- valuations come from P1 instead of the provisional builders;
- a camp's **offer list is its Export marks**;
- a requester's **want list is drawn from its Import marks**, intersected with the partner's offers;
- give lists come from its Export marks;
- the limits stay: at most K requests, known contacts within range, W×W pairs per responder, canonical order;
- "broadcast" means only this bounded offer visibility, with no market clearing.

There is one new limit: a receiver never accepts goods beyond its free storage room. Its curve is cut off at the
room, so value beyond it is zero, and a quantity above the room is never agreed.

### P3. Fulfilment plan

The planner runs once per camp. Confirmed trades enter as **fixed claims** with first priority:
- **Exports:** made-to-order parts get first call on role labour, tool time, inputs and storage room. Stock parts have
  already left the camp's view.
- **Imports:** added as supply, or as needs-only supply when they arrive after production.

This is the "planner interface frozen for phase 3" in the plan document. Making the claims first priority is new and
deliberate.

### P4. Forecast and resolver

`MaterialEconomy.Forecast` and the resolver's production loop serve outstanding export claims **before** the ordered
recipes, from the same labour, tool time and inputs.

The current code reserves exports only in the planner's first reservation pass. Its final staffing pass, the forecast
and the resolver then spend labour and tool time in crafting-priority order. In the census this starved Farm Tools:
0.30 delivered, with Weaver Cloth taking all tool time first ([TradeHandoff.md](TradeHandoff.md)). Making claims
first priority in production itself means every planner inherits it.

## 3. Pottery storage

### 3.1 Space and capacity

Space is measured in food units, the unit Pots already use (`PotStorageFoodPerUnit = 80`).

| Term | Definition |
| --- | --- |
| Natural food storage `N_nat` | Existing, site and climate dependent (`NaturalStorageCapacity`). **Food only.** |
| Base goods storage `B_g` | New config `GoodsBaseStorage` = x goods units (O2: per camp or per resident). **Goods only.** Applies with or without Pots. |
| Pot storage `P` | `Pots × PotStorageFoodPerUnit`. **Shared** by food and goods. |
| Space per goods unit `s` | New config `GoodsStorageSpacePerUnit`, the same for every counted good. |

Occupancy:
- Goods space is `G = s · Σ counted goods`. It fills `s·B_g` first, then Pots.
- Food is `F`. It fills `N_nat` first, then Pots.
- The pot constraint is `max(0, G − s·B_g) + max(0, F − N_nat) ≤ P`.
- **Free goods room** is `B_g + (P − pot space used by food and goods) / s`, never below zero.

### 3.2 Counted goods

- **Counted:** Cloth, Animal Fabric, Housing Material, Light Housing units, Hand Tools, Tools, Copper Tools, Farm
  Tools, Herding Tools, Improved Ceramics.
- **Excluded:**
  - Stone, Clay and Wood (decision);
  - Pots, which are the containers;
  - fixed Housing, which is a structure (`WorldState.FixedHousing`), not inventory;
  - legacy `HousingUnits`/`Houses`.
- Whether goods **in use** count (pitched tents sheltering people, tools covering workers) is O4. The decision "every
  good the same space" says nothing about it.

### 3.3 Blocking, not destroying

- **Goods additions are capped at the free room at that moment.** This covers:
  - production (`NeolithicProduction.Produce`, mirrored in `MaterialEconomy.Forecast` and the planner);
  - trade receipts at settlement;
  - any other inflow.
- **Trade settlement:** the receiver's free room is a third bound on the deliverable share. Both legs still settle at
  the smaller share, so conservation holds and the giver keeps the rest.
- **Nothing is destroyed:**
  - Pots decay 10% per reference turn, so capacity can fall below what is already stored. The excess is
    grandfathered, with no additions until consumption, decay, gifts or exports bring it back under the cap.
  - Loaded saves already over the cap are treated the same way.
- **Food keeps its current rule.** Harvest and herd flows are not blocked. Food above its capacity is discarded at
  end of turn (`ApplyStorage`, `DemographicReport.DiscardedInventory`).
  - Food's capacity becomes `N_nat + P − goods' pot space`, so goods in Pots reduce food storage.
  - This difference between food and goods is deliberate: food arrives from ecology, goods from decisions.

### 3.4 Food and goods compete for Pots (O1)

In a shared pool, goods can crowd out the food operating reserve. That reserve is part of the protected survival band
(§7.1).

**Recommendation:** goods may not occupy the pot space needed for the camp's planned operating-reserve target. Goods
room becomes `B_g + max(0, P − max(F, reserve target) pot share) / s`. Food above the reserve competes with goods on
value, through the planner.

Without this guard, storage turns into a survival risk set by craft decisions.

### 3.5 Valuation effects

- **Value is cut off at the room (phase V, not S).** Each good's curve is truncated at free room, so overflow has no
  local value. That is what turns stock into Export marks and stops imports into a full store.
  - Phase S tried a per-good cut-off on the current trade curves and dropped it. A swap frees the room its given good
    occupied, but a per-good cut-off cannot see that.
  - In the fixture, an over-full Cloth camp valued Housing Material at zero, although trading Cloth away would have
    made room for it.
  - Phase V's curves must price the net room change of a pair. Until then, settlement alone enforces room (§3.3).
- **Pots get a value from storage.** A Pot's value comes from the room it adds for both food and goods. This replaces
  the provisional Pots band (about 90 food per Pot, kept until now by decision) once P1 publishes Pots.
- **Room as a planner input.** The planner treats room as a constrained input, like tool time. It does not make more
  goods than fit.

### 3.6 Phase S implementation

`GoodsStorage.cs` holds the rule:
- `Begin` sets the turn's storage terms and in-use allowances;
- `Capacity` and `FoodCapacity` apply the shared pool with the reserve guard;
- `Occupied` measures the space goods take;
- `Limit` cuts an addition to what fits, by bisection on a convex or monotone occupancy;
- `Add` records an addition on a virtual stock.

| Step | What happens |
| --- | --- |
| Turn start | `TurnResolver.Resolve` sets `Camp.GoodsStorage` (transient, never saved) from each camp's prepared needs, before the opening trade settlement. |
| Production | `NeolithicProduction.Produce` cuts each step to `Limit`; inputs are consumed only for what is stored. |
| Forecast | `MaterialEconomy.Forecast` applies the same rule to a virtual stock, so the prepared needs and the planner's reforecast see it. |
| Trade settlement | The receiver's room is a third bound on each agreement's share, at the opening and after production. Both legs scale; refunds are never blocked. |
| End of turn | `ApplyStorage` uses `FoodCapacity` and clears the terms. |

These allowances come from the prepared plan (in the forecast, from useful demand): equipment outputs, which set Hand
and Regular Tools in use, and Housing assembled, which sets Housing Material in use.

The planner's own staffing pass is unchanged. That is planner-refactor work, so the planner may still staff work that
storage then blocks.

Checks are in `GoodsStorageChecks` (`--only=GoodsStorageChecks`). The census tools are
`Tools/PerformanceAudit/Run-StorageAudit.py` and `Summarize-Storage.py`.

### 3.7 Phase S census (4 October 2026)

**Setup.** Five Standard/80 worlds (C1–C3, P1, A1), 50 post-opening turns, trade and immigration off, census
configuration. Source is the phase S working tree on `a3466a1`; output is in `TestResults/Storage-S1` (ignored).

| Variant | Husbandry gate | Population t50 | QoL t50 | Cloth / capita t50 | Discarded food t50 | Stored goods made |
| --- | --- | --- | --- | --- | --- | --- |
| off vs reference | **exact** on all five worlds | | | | | |
| x = 4, s = 20 | pass (identical to off) | ±0 | ±0 | ±0 | ±0 | ±0 |
| x = 2, s = 20 (default) | pass | +0.05% | +0.02% | +0.02% | ±0 | +0.02% |
| x = 2, s = 5 | identical to x = 2, s = 20 | | | | | |
| x = 1, s = 20 | pass (animals +6.6%, linked pop +8.6%) | +6.1% | −0.5% | +0.3% | −39% | +4.4% |

**Reading.**
- **Pots give goods no space today.** `s` does not matter (x2 and x2s5 are identical). Pots are made only up to the
  food reserve target, and the reserve guard keeps that space for food. Goods are bounded by the base allowance
  alone. Pots gain value from goods only once phase V prices storage.
- **x = 2 (the default) barely binds.** Median holdings are about 1.1 goods units per resident, so x ≥ 4 never binds.
- **x = 1 binds and outcomes rise.** Population is +6%, husbandry animals +7%, discarded food −39%, and QoL −0.5%.
  - A plausible cause is fabric production cut by storage, which leaves its plant and animal food feedstock for
    growth.
  - This is one run per variant and has not been attributed. Run a ledger comparison before choosing x = 1 as a
    calibration.
- **Cost.** Planning time per turn rose by about 1–7% at x = 2, from the per-forecast storage terms (`Begin`, including
  the Farm Tool need). This is one timing run on a shared machine.

### 3.9 Phase C: claim-first production (4 October 2026)

**Implementation.** Agreed exports take labour, tool time and inputs before the camp's own recipes. Within each
tool-dependency layer, every owed good is made first, in recipe order, by the roles that can make it
(`NeolithicProduction.ExportRoles`: the specialist, then a Craftsperson). Where it applies:

- **Resolver** (`ResolveCampNeolithic`). No Housing Material plan holds inputs back from a claim; its reservations
  still see the output.
- **Forecast** (`MaterialEconomy.Forecast`). The loop is now `Step(role, recipe, exportOnly)`.
- **Planner's final staffing pass** (`exportStaffed`).

The census and probes found four further fixes:

1. **Inputs in `Trade.MakeToOrder`.** Make-to-order capacity is now bounded by made inputs on hand (Animal Fabric for
   tents, Housing Material) and by raw materials in reach: stock plus known sources, as the planner counts them.
   Before, camps promised tents with no fabric and Pots with no Clay.
2. **Zero-person roles.** The planner no longer adds a material role staffed by zero people, which is an invalid
   assignment. Tiny export claims reached it.
3. **The Pots price.** About 90 food per Pot is the provisional **use value** of a Pot from stock with no making
   capacity. A Pot made with spare capacity costs its making cost, about 4.5 food in the fixture. The census-era price
   followed an agreement that had used up the herder's capacity. `TradeChecks.Pots` now pins both prices.
4. **Coverage.** `TradeChecks.ClaimFirst` covers resolver, forecast and input bounds.

**Census.** Five worlds, 50 turns, trade on against off. Output is in `TestResults/Trade-C3` (ignored).

| Measure | Before phase C (`TestResults/Trade-nanfix`) | Phase C |
| --- | --- | --- |
| Trade off vs reference | exact | exact |
| Made-to-order delivery, per world | 0.51–0.77 | **0.98–0.99** |
| Farm Tools / Pots / Light Housing | 0.30 / 0.62 / 0.45 | 0.99 / 0.99 / 1.00 |
| Housing Material / Cloth / Hand Tools / Animal Fabric | 0.90 / 0.84 / 0.74 / 0.89 | 0.99 / 0.97 / 1.00 / **0.92** (104 legs) |
| Husbandry gate, animals at t50 (total / median) | −11.7% / −18.5% | **−12.0% / −13.4% (fail)** |
| Handshake mean ms per turn | 44–68 | 44–67 |

**Gate.**
- Delivery passes overall and for every good except Animal Fabric (0.92 over 104 legs).
- The husbandry gate still fails. Undelivered goods were not its cause; it is phase V's valuation work, as the earlier
  census pointed to food paid for goods.

**Known limitation.** A village that relocates this turn has its plan cleared, so it cannot make what it agreed to
make. The handshake runs from the committed world before relocations are known. Bounding it would make the plan depend
on movement orders and break draft reuse. Those legs settle at 0, with the escrow refunded.

### 3.10 Phase V: valuation on planner terms (4 October 2026)

**Implementation.** `Trade.Value` now uses the planner's claim terms (`ClaimValuation`) from the committed camp:

| Good | Curve |
| --- | --- |
| Cloth, Animal Fabric, Housing | need goods, as before |
| Tents | the Light Housing claim on all shelter in tent units |
| Pots | the food-security claim (`PotValue`) over the reserve's target Pots, at the Pots this turn's decay leaves; this replaces the provisional ~90-food band, as §3.5 approved |
| Farm Tools | labour freed at the lagged farm-labour price `EconomicLaborPlan.FarmLabourPrice`, now recorded by the planner while trade is on |
| Hand Tools | needed up to last turn's use, from receipts; the 44 ms tool schedule no longer runs in valuation |

- **Export mark:** every non-food floor is the useful need, so stock below it is never offered.
- **Import mark:** wants are drawn only below the target, as before.
- **Storage room:** checked on each pair's net change in stored goods (`Valuation.Fits`), so a full store still swaps
  stored goods.
- **Checks:** `TradeChecks.PhaseV` and the rewritten `Pots` (479 assertions).

**Census.** Five worlds, 50 turns, trade on against off. Output is in `TestResults/Trade-V1` (ignored).

| Measure | Phase C (`Trade-C3`) | Phase V |
| --- | --- | --- |
| Trade off vs reference | exact | exact |
| Husbandry gate, animals at t50 (total / median) | −12.0% / −13.4% **fail** | **+2.1% / +0.8% pass** |
| Herders in husbandry-first camps at t50 | −12.7% | +2.3% |
| All-camp population at t50 | −2.6% | −1.4% |
| QoL at t50 (population-weighted, against off 0.8025) | 0.7945 | 0.7946 |
| Trades per world | about 9,000–13,500 | about 3,300–5,000 |
| Food traded, all worlds | 39.7 M | 6.3 M |
| Made-to-order delivery | 0.98–0.99 | 0.99 (every traded good ≥ 0.986) |
| Handshake mean ms per turn | 44–67 | **20–36** (one 500 ms spike, C2) |

**Reading.**
- The husbandry losses came from herders pulled into exports. Herd size is labour-bound: animals per herder were
  identical with trade on and off.
- With the Export mark, camps stop selling stock they need, and food and raw-material flows collapse. Labour
  returns to herding.

**Gate.**
- The husbandry gate passes, and delivery passes.
- Handshake time halved but is not back to the 4–6 ms of the four-good design. The budget to agree is about
  0.07 ms per eligible camp per turn, the current 20–36 ms at about 350 camps.

**Open.**
- Trade on is still slightly negative overall: population −1.4% and QoL −1% against off. It is not yet the
  positive-sum design. Measure who loses before tuning (phase R prices herd growth and spare labour).
- Farm Tools no longer trade. They are priced only when the plan's farm-labour price is set, which happens
  rarely. That price comes from the expansion ladder, a planner matter.

### 3.11 Who loses from trade, and the 50-turn full-system profile (4 October 2026)

**Setup.** Five Standard/80 worlds, 50 post-opening turns, census configuration, phase V source (`4350815`). Camps
are traced to their 80 founding camps per world through fission events, so lineages are comparable across runs.
Output is in `TestResults/Trade-V1` and `TestResults/Profile-V` (ignored).

**Who loses.**
- Losses are concentrated in lineages that **started as Cultivation** camps: at t50, population −11.1% and QoL −3.4
  points; Watercraft lineages lose −16% population.
  - Their Cloth per capita falls (0.787 → 0.725) and they make 20% fewer fissions (333 vs 416).
  - Food and housing are unchanged.
- Husbandry-start lineages gain (+1.9% population). Lineages that never trade are identical with trade on and off.
- Founding regimes do not trade with each other. The loss is **within** the farming group:
  - farmers make Pots to order for one another (Pots +48%, Watercraft +221%);
  - meanwhile their own Cloth (−6%), Housing (−7%) and Tools (−5%) fall.
- **Cause:** make-to-order. Exports take tool time first (phase C), and the make cost (`labour × w + inputs`) does not
  charge for the tool time they displace. With `TradeExportLabourShare = 0` (stock-only trade):

  | Measure at t50 | Trade off | Trade on | Stock only |
  | --- | --- | --- | --- |
  | Population | 1,377,024 | −1.4% | **+1.1%** |
  | Farming lineages | | −11% | **±0** |
  | QoL | 0.8025 | 0.7946 | 0.7999 |
  | Husbandry animals | 408,927 | +2.1% | +2.4% |

**Decision (4 October 2026).** Stock-only trade is the default (`TradeExportLabourShare = 0` in code and
`OpeningConfig.json`) until the planner prices tool time. Make-to-order stays implemented and tested. The census
`on` variant now measures stock-only trade. The full-system profile below used make-to-order (0.25).

**Full system: fission, immigration and trade on, 50 turns.**
- Per turn, scalar (`--workers=1`), mean over worlds: 735 ms; last 10 turns 1.66 s; max 2.4 s (single world: 3.3 s).
- About 37 s per world for 50 turns.
- Heap at most 568 MB.
- Against fission and immigration without trade: about +6% total time and +14% over the last 10 turns. Migrants at
  t50 rise to 1,501 from 868.

**Settlement outcomes**, all five worlds:

| Turn | Settlements | Population | Population min / p25 / median / p75 / max | QoL p25 / median / p75 |
| --- | --- | --- | --- | --- |
| 1 | 421 | 20,185 | 9 / 31 / 46 / 61 / 133 | 0.344 / 0.594 / 0.950 |
| 10 | 446 | 53,645 | 6 / 55 / 100 / 174 / 469 | 0.685 / 0.886 / 0.991 |
| 25 | 659 | 200,720 | 3 / 160 / 297 / 437 / 741 | 0.792 / 0.906 / 0.966 |
| 40 | 1,759 | 690,330 | 1 / 227 / 384 / 538 / 879 | 0.766 / 0.886 / 0.948 |
| 50 | 2,997 | 1,342,553 | 1 / 274 / 444 / 652 / 1,000 | 0.724 / 0.858 / 0.931 |

**QoL minimum is 0.000 at every turn.** About 28–37 **stuck camps** (about 1%), one per affected AI player, keep the
same small population for all 50 turns. They have no crafting staff, clothing or housing, so QoL is 0 with food met.
They are present without trade too (a planner or AI issue, not trade). Excluding them, the t50 QoL minimum is
0.004–0.285 by world.

## 4. Doctrine check

| Question | Answer |
| --- | --- |
| Authoritative state | Unchanged inventories, plus three config fields (`GoodsStorageEnabled`, default false; `GoodsBaseStorage`; `GoodsStorageSpacePerUnit`), all OptionalField. Capacity and room are derived from Pots and stocks; nothing new is saved. P1 valuations and marks are transient. `w` stays the saved `LabourPrice`. |
| Temporal abstraction | Turn boundaries stay authoritative. The room check happens at each addition, in the resolver's existing canonical production and settlement order, which is already authoritative. Food discard stays at end of turn. |
| Scaling | P1 is `O(camps · goods)`, with no scheduler. The handshake is unchanged: `O(camps · (G log W + contacts) + camps · K · W²)`. A storage check is `O(1)` per addition. Cost follows active camps and goods, not elapsed time. |
| Mature game and RL scale | Linear in camps; 80/320-tribe runs required. No global pass. |
| Determinism | P1 is pure per camp (parallel map); P2–P4 keep canonical order. Scalar, worker, reversed-partition and save-continuation replay are required. |
| Information and RL | A camp's own room and occupancy can be exact in its observation; foreign stores are never exposed. Player fields are a later schema bump. |
| Default off | With `GoodsStorageEnabled = false` and P1 not yet replacing the builders, outcomes must equal the current reference exactly. |

## 5. Rejected architectures

- **A full planner pass before trade and another after it.** That doubles the roughly 1.3 ms per camp-turn plan, which
  is the main cost already.
- **Market clearing or global price search:** excluded by §6.
- **Destroying goods overflow:** decision is to block. Food discard is unchanged.
- **Separate food and goods pots:** decision is a shared pool.
- **Per-good storage volume:** decision is uniform space per unit.
- **Reserving export work only in the planner.** This is the current state; it fails when the forecast and resolver
  spend labour and tool time in priority order.

## 6. Phases and gates

Each phase is behind a switch or keeps outcomes exact while off. Each ends with the five-world census (Standard/80,
50 turns) and its own gate.

| Phase | Work | Gate |
| --- | --- | --- |
| **S** Storage | §3 behind `GoodsStorageEnabled`: room in production, forecast, planner and settlement; reserve guard if O1 is accepted; checks for blocking, grandfathering, conservation and saves. | Off equals the reference exactly. On, with trade off: husbandry gate (−5% / under 10×), cloth and housing per capita, food discard, QoL. Sweep `s`, `x`. |
| **C** Claim-first production | §2 P4 in forecast and resolver, then in the planner's final pass. | Trade-on census: made-to-order delivery ≥ 0.95 overall and per good. Off is exact. |
| **V** Valuation forecast | §2 P1–P2: same-turn curves and marks for every good, replacing the provisional builders key by key. Marks drive offers and lists. | Trade-on census: the husbandry gate; handshake time back to the earlier ~4–6 ms mean, or a stated budget. Off is exact. |
| **R** Planner refactor ([EconomicControlContract.md](EconomicControlContract.md) + [Addendum A](EconomicControlContractAddendumA.md), proposed as the phase R design) | Production scheduled once per plan; one planner path; spare labour, village–pasture transition and herd growth priced (plan document, phase 3). | Phase 3 gates in the plan document, including 80/320-tribe scale. |

The suggested order is **S → C → V → R**:
- S and C are small, independently measurable, and fix today's measured failures.
- V needs both: the storage cutoff gives the Export marks their meaning, and first-priority claims make Export marks
  deliverable.

## 7. Resolved questions

Resolved 4 October 2026: the user accepted every recommendation.

1. **O1. Reserve guard: yes.** Goods never take the pot space the food operating reserve needs.
   - Goods space is `s·B_g + max(0, P − foodPot)`, where `foodPot = min(P, max(0, max(F, R) − N_nat))`. `R` is
     `DesiredReserveFood` for the reserve population.
   - Food's end-of-turn capacity always keeps its reserve share:
     `N_nat + max(P − goodsPot, min(P, max(0, R − N_nat)))`.
2. **O2. Base storage `x` is per resident** (`GoodsBaseStoragePerResident`).
3. **O3. Space per goods unit `s`** gets a starting value from the phase S measurement, then a sweep.
4. **O4. Goods in use do not occupy storage.** Only stock above current use counts:
   - tents count above the people that fixed Housing leaves unsheltered;
   - each tool kind counts above the camp's current-turn need for it.
5. **O5. Last turn's labour price `w`.**
6. **O6. Keep the lagged `TradeValues` path** as a fallback and external-planner interface until P1 prices every good;
   revisit then. (No explicit recommendation had been given; this follows §2.)
7. **O7. Mobile camps use the same rule** (Pots are carried inventory). A lower carry limit is a later lever. (No
   explicit recommendation had been given; this is the simplest consistent default.)

## 8. Multi-turn contracts (allowed for, not enabled)

The user asked on 4 October 2026 that the design **allow** trade agreements lasting several turns without the AI
necessarily using them. If testing shows they improve population, QoL or performance, the findings go to the user for
conditional approval before any default changes.

Today every agreement is agreed and settled within one turn, and nothing pending is saved. A contract changes that.

| Question | Answer |
| --- | --- |
| Authoritative state | A saved, optional `TradeContract` list: ID from `NextId`, parties, goods, per-turn quantities, ratio, first and last turn, delivered history, and a breach count. Older saves load with none. |
| Each turn | Before P1, each live contract becomes **fixed claims**: an export claim for the seller, an import for the buyer. The goods are reserved, so the turn's handshake no longer offers them. The claims flow through P3/P4 like an agreed trade (claim-first). Settlement is pay on delivery with both legs scaling, as now. A food import committed by contract is binding at preparation, so it may support the turn's growth plan (§7.3 of the QoL design). |
| Ending | At the last turn; when a party disappears or moves beyond `TradeRange`; or after `TradeContractBreachLimit` short deliveries in a row. Ending is canonical and recorded in the trade journal. |
| Agreeing | Through the same bounded handshake, with a term of 1 to `TradeContractMaxTurns` turns. A contract's value is the discounted sum of per-turn gains on P1's curves. With the planner forecast still single-turn, this repeats this turn's curve; a multi-turn forecast would replace it. |
| Switches | `TradeContractMaxTurns = 1` (default) is today's behaviour exactly. AI use needs a value above 1; no AI policy is enabled by default. |
| Scaling | O(live contracts) per turn to expand claims and settle. Contracts can lower handshake work, because fewer goods are renegotiated each turn. |
| Determinism and information | Contracts are handled in ID order. Each party sees only its own contracts; foreign contracts are never observed. |

**Why it might help:**
- Stable demand lets the planner staff export specialists without re-earning each order.
- Binding food imports are what a food-importing proto-town needs (§8 of the QoL design).
- Fewer per-turn negotiations could lower handshake cost.

**Risks:**
- Lock-in at stale terms when the curves move.
- More saved state.
- Breach handling adds work for the resolver.

**Where it fits.** After phase V. Contract valuation needs the planner's same-turn curves, and claims need claim-first
production (phase C). Phases S, C and V add no single-turn assumption that would block it:
- claims are generic (good, quantity, timing);
- storage room applies to any receipt.

**Experiment before any default.** Compare `TradeContractMaxTurns = 1` with 3 and 5 on the five-world census:
- population and QoL;
- the husbandry gate;
- delivery;
- handshake and planning time.
