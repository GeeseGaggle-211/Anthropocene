# Economic Control Contract, Addendum A: reconciliation with implemented trade and storage

**Status:** proposed for approval, 4 October 2026. The reconciliations below are recommendations until the user
approves them; §A6 lists the decisions.

**Reference:** `features/herd-ecology-retention`, commit `a85d5e8`, which contains the contract's reference `6a802dc`.

**Amends:** [EconomicControlContract.md](EconomicControlContract.md) v0.1 §§2, 8, 12, 15, 16. Where this addendum and
v0.1 differ, this addendum governs.

**Related:**
- [PlannerTradeStorageDesign.md](PlannerTradeStorageDesign.md): phases S, C and V, and the measurements in §3.6–3.11;
- [TradeImmigrationPlannerPlan.md](TradeImmigrationPlannerPlan.md): phase 3;
- [Trade.md](Trade.md) and [TradeHandoff.md](TradeHandoff.md).

v0.1 was written against a revision where trade was a design only. Since then, trade, pottery storage, claim-first
production and planner-consistent trade valuation have been implemented and measured. This addendum resolves three
conflicts:
- the trade boundary (§A1–A2);
- commitment priority (§A3);
- the two overlapping phase R plans (§A4).

## A1. Trade as implemented (replaces the status statement in v0.1 §12)

Trade is implemented, opt-in (`TradeEnabled`), AI-to-AI only, and **stock-only by default**
(`TradeExportLabourShare = 0`, decided 4 October 2026). v0.1 §12 has three states; they map onto the code as follows.

| v0.1 state | Implementation |
| --- | --- |
| Opportunity | Each camp's offer set and its ranked want and give lists in the bounded handshake (`Trade.Plan`). The handshake is pure, computed once in preparation from the committed boundary, with at most K requests and known contacts within range. It neither replans counterparties nor iterates to a fixed point. |
| Admitted commitment | A `TradeAgreement` in `PreparedTurn.Trade`. Its stock part is escrowed at the opening of resolution. Its made-to-order part (only when `TradeExportLabourShare > 0`) becomes a fixed export claim (`TurnNeeds.Exports`, `Camp.TradeOwed`) that reserves capacity once. |
| Realized delivery | `Trade.Settle`: an all-stock agreement settles at the opening, before dispatch, provisioning and growth planning. `Trade.SettleAfterProduction` settles agreements with a made part, after all production and before needs, paying on delivery. Both legs scale to the smaller deliverable share; every part is conserved; refunds are never blocked. |

v0.1 §12 requires that an import reduce a local request only if it is eligible before that request's phase. The
implementation meets this:
- **All-stock receipts** are realized at the opening, so preparation plans with them, including same-turn food.
  Settlement happens before any consumption or growth phase, so no unrealized promise is spent.
- **Made-to-order receipts** count only for needs (`Camp.TradeImports`): they are never recipe inputs and never fund
  births.
- **Food bought with goods** that arrives after production reaches growth only the next turn. This matches v0.1 §12's
  "no same-turn births on an unrealized promise".

v0.1 §12's empty external-commitment adapter therefore becomes **the existing trade interface**:
- trade off (`TradeEnabled = false`) is the empty adapter, and the census verifies it equals the pre-trade reference
  exactly;
- the allocator consumes admitted commitments only through `TurnNeeds.Exports` and post-trade planning views
  (`TurnPlanner.PlanningCampAt`), never by calling the handshake.

**Storage.** Pottery storage (`GoodsStorage`, opt-in) supplies the rules for v0.1's Storage controller: finished
goods share Pot space with food above a per-resident base, with the food reserve protected and additions blocked
rather than destroyed. Trade settlement already bounds a receipt by the receiver's room. The allocator must treat free
room as a constrained resource, as it treats equipment service.

## A2. Trade-valuation adapter (resolves v0.1 §12, last paragraph)

v0.1 forbids keeping the Value search alive only to produce a labour price. Trade valuation (phase V) reads two kinds
of input.

| Input | Source today | Depends on the Value search? |
| --- | --- | --- |
| Service value curves, for clothing, fixed and portable shelter, Pots and food bands | `ClaimValuation.CampTerms`, `NeedGoodTerms`, `HousingLife`, `PotValue`, `NeolithicProduction.TargetPots`, `ExpansionValuation.FoodValue`, `TurnResolver.FoodBucketBasis` | **No.** These are closed-form, pure functions of the committed camp. |
| Labour per unit and recipe inputs | `ClaimValuation.LabourPerUnit`, recipes, `SpecialistProductivity` | **No.** These are physical. |
| Labour price `w` | `EconomicLaborPlan.LabourPrice`, lagged, from `ValueFulfilment.LaborPrice` | **Yes** |
| Farm-labour price `w_farm` | `EconomicLaborPlan.FarmLabourPrice`, lagged, from the expansion ladder in the Value claims | **Yes** |

`w` prices the Hand Tools and raw-material curves and, when make-to-order is enabled, the make cost. `w_farm` prices
Farm Tools.

**Recommendation.**
1. **Keep the closed-form service-value functions as a strategy-independent library.** They become the trade adapter's
   value source under both Legacy and DemandControl.
2. **Move them out of `ProductionDemand.cs`'s `ClaimValuation` class into a pure valuation module.** That way they do
   not drag Value-search code along.
3. **DemandControl publishes `w` after its arbitration**, into the same lagged field (`EconomicLaborPlan.LabourPrice`),
   as the **service value per worker-year of the marginal granted bundle**. That is the bundle whose fulfilment the
   labour budget cut, valued with the library above.
   - When no labour constraint binds, it publishes the value of a spare resident's default use: gathered food per
     worker-year times `ExpansionValuation.FoodValue`.
   - This is defined by the allocator's own result. It uses no search and costs `O(bundles)`.
4. **`w_farm` comes from DemandControl's field-service controller** as the value a farmer-year produces in the
   supervisor-selected fields, using the same food value. Until it exists, Farm Tools stay unpriced, as they mostly are
   today.
5. **Use the `ITradeValuePublisher` seam where possible.** A strategy that can produce a full value schedule for a good
   overrides the adapter for that good. Absent a schedule, the adapter applies.

## A3. Commitment priority (reconciles phase C with v0.1 §8)

| Design | Order |
| --- | --- |
| Phase C (`0935c33`) | **Claim-first:** an agreed made-to-order export takes labour, tool time and inputs before all of the camp's own recipes. |
| v0.1 §8 | Admitted commitments are class 3, "Recovery and commitments", **below** survival and operating continuity. |

The census shows why this matters. With claim-first and an unpriced tool-time cost, farming lineages made Pots for
one another at the expense of their own Cloth: population −11% and QoL −3.4 points at turn 50. That is why trade is
now stock-only by default ([PlannerTradeStorageDesign.md](PlannerTradeStorageDesign.md) §3.11).

**Recommendation: adopt v0.1's classes, with an admission test.**
1. **Admission.** A camp may admit a made-to-order commitment only against capacity that remains after its class 1–2
   bundles (survival and operating continuity) are reserved. The handshake's make-to-order quote, today
   `Trade.MakeToOrder` capacity, becomes that residual capacity, including tool time and storage room. It is computed
   from the same feed-forward requests the allocator uses. It is a bounded quote, not a replan.
2. **Service.** Once admitted, a commitment is served after class 1–2 and **before** class 3 recovery and class 4
   development. Phase C's claim-first code becomes "commitments after operating continuity", in the forecast, the
   resolver and the allocator alike.
3. **Shortfall.** If realized capacity still falls short, both legs scale (pay on delivery), as now. A shortfall is
   recorded as delivery or forecast error in `UnmetDemand`, never covered by consuming class 1–2 service.
4. **Re-enabling make-to-order** (`TradeExportLabourShare > 0`) requires:
   - admission against residual capacity, including tool time;
   - the class ordering above;
   - the census gates: farming-lineage population and QoL no worse than trade off, the husbandry gate, delivery
     ≥ 0.95.

   Until then, stock-only trade has no production priority question at all: every leg is escrowed stock.

## A4. One phase R plan

[TradeImmigrationPlannerPlan.md](TradeImmigrationPlannerPlan.md) phase 3 and
[PlannerTradeStorageDesign.md](PlannerTradeStorageDesign.md) §6 phase R describe the refactor as:
- valuation separated from allocation;
- a single scheduler realization per plan;
- one planner path;
- pricing for spare labour, the village and pasture transition, and herd growth.

v0.1 is a more specific architecture for the same work.

**Recommendation:** v0.1 together with this addendum **is** phase R. The other two documents' phase R bullets become
requirements it must meet, not a parallel design.

| Phase R requirement | Where v0.1 + A meets it |
| --- | --- |
| Separate valuation, allocation and realization | Controllers (feed-forward requests), the Elder (arbitration), the resolver (receipts); the valuation library in §A2 |
| Realize the scheduler once per plan | v0.1 §8: one dependency and resource allocation plus at most one repair pass |
| One planner path | v0.1 §15: a `Legacy \| DemandControl` selector; retire Legacy after a census |
| Price spare labour, the village and pasture transition, herd growth | v0.1 §13 strategy layer and herd-service controller. **Measured priority:** trade diverted herders because herd growth was unpriced (§3.10–3.11). The herd-service controller must request herd continuity as class 2, operating continuity of a retained asset, so export or other work cannot silently displace it. |
| Keep the trade interface | §A1: admitted commitments through `TurnNeeds.Exports` and planning views; values through §A2 |

The trade pipeline in [PlannerTradeStorageDesign.md](PlannerTradeStorageDesign.md) §2 maps onto v0.1:

| Trade pipeline step | v0.1 equivalent |
| --- | --- |
| P1, the valuation forecast | The controllers' feed-forward `ServiceDemand` on the frozen snapshot, valued with the §A2 library. An Import mark is a deficit-repair band the settlement cannot fill locally. An Export mark is retained stock above `U + S_target`, or residual capacity after class 1–2. |
| P2, the handshake | Unchanged |
| P3, fulfilment | The Elder allocation, with admitted commitments in class 3 |
| P4 | The resolver honouring the same order |

## A5. Further amendments to v0.1

| v0.1 section | Amendment |
| --- | --- |
| §2, existing-code table | Add rows: `Trade.cs` (handshake, settlement, valuation; see §A1–A2), `GoodsStorage.cs` (storage rules, §A1), and `EconomicLaborPlan.LabourPrice` / `FarmLabourPrice` / `TradeValues` (adapter outputs, §A2). |
| §11, fission and commitments | Every agreement is single-turn today, so nothing crosses a fission. The multi-turn contract option ([PlannerTradeStorageDesign.md](PlannerTradeStorageDesign.md) §8) must keep a contract with its named camp, as v0.1 says. |
| §15, `MaterialEconomy.Forecast` | Phase C's export pass in the forecast serves **admitted** commitments only; it is not a fresh allocation, so it is consistent with v0.1. Under §A3 it moves after class 1–2. |
| §16, acceptance tests | Add the trade gates already automated: trade off equals the reference exactly (`Run-TradeAudit.py`); with trade on, the husbandry-first gate, made-to-order delivery ≥ 0.95, and the founding-lineage comparison (population and QoL by founding regime) from §3.11. Also `TradeChecks` (480 assertions) and `GoodsStorageChecks` (64). |
| §16, cost evidence | Baseline at `a85d5e8`, five Standard/80 worlds, 50 turns, scalar, fission + immigration + make-to-order trade: 735 ms per turn mean, 1.66 s over the last 10 turns, about 4 ms per settlement-turn, heap at most 568 MB. The per-plan cost target is still to be agreed (§A6). |

## A6. Decisions requested

1. **Adapter (§A2):**
   - keep the closed-form service values as a strategy-independent library;
   - DemandControl publishes `w` as the marginal granted bundle's value per worker-year, with the spare-gatherer value
     as its floor;
   - `w_farm` comes from the field-service controller.
2. **Priority (§A3):**
   - commitments are class 3, after survival and operating continuity, with admission against residual capacity;
   - make-to-order stays off until the gates are met.
3. **Plan (§A4):** v0.1 together with this addendum is phase R; the other documents' phase R bullets become its
   requirements.
4. **Cost target:** set after a breakdown of current planner cost per camp-turn, the next measurement task.
