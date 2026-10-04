# Planner-led trade and pottery storage: design note

Status: **approved**, 4 October 2026 (open questions resolved in §7). Phase S is in progress. This is the short note that phase 3 of
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

- **Value is cut off at the room.** Each good's curve is truncated at free room, so overflow has no local value. That
  is what turns stock into Export marks and stops imports into a full store.
- **Pots get a value from storage.** A Pot's value comes from the room it adds for both food and goods. This replaces
  the provisional Pots band (about 90 food per Pot, kept until now by decision) once P1 publishes Pots.
- **Room as a planner input.** The planner treats room as a constrained input, like tool time. It does not make more
  goods than fit.

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
| **R** Planner refactor | Production scheduled once per plan; one planner path; spare labour, village–pasture transition and herd growth priced (plan document, phase 3). | Phase 3 gates in the plan document, including 80/320-tribe scale. |

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
