# Trade → planner refactor handoff

Branch `features/herd-ecology-retention`. Phase 1 of `Docs/TradeImmigrationPlannerPlan.md` (trade) is mechanically
complete; this note hands phase 3 (planner refactor) to the next agent. `Docs/Trade.md` is the rule reference; it
predates the last commit (see "State of the last commit").

## State of the last commit (work in progress)

The last commit generalizes trade mechanics and makes **Light Housing, Pots, Hand Tools, Farm Tools, Stone, Clay and
Wood** tradeable alongside Cloth, Animal Fabric, Housing Material and Food. It **compiles** in the headless project.
`--only=TradeChecks` passed Config, Valuation, Handshake, Eligibility, Kin, Settlement and Planning. The run stopped at
an obsolete part-index assertion in `Registry()`, which is now fixed but has not been re-run. **Food, MadeToOrder,
Replay and Persistence have not been run against the new semantics.** Neither have the fixed suite, the census
(`Tools/PerformanceAudit/Run-TradeAudit.py` + `Summarize-Trade.py`) or the docs/contract checker
(`python Tools/Docs/Check-CurrentContract.py`). Do these first; expect expectation updates in `Food()` and `MadeToOrder()`,
because escrow and delivery timing changed. `Docs/Trade.md`, `Docs/CurrentSimulationContract.md`,
`Docs/TurnResolutionPasses.md` and the AGENTS.md trade row still describe the four-good, pre-split behaviour; update them.

### What changed (Trade.cs and hooks)

- **Goods.** `TradeGood` is now append-only and extended (saved records store the number). Each new good is one
  `Registry` descriptor. Made goods (with a `Recipe`) have exactly one part, which is validated at static init.
  `ImportServesNeeds` is true for Cloth, Animal Fabric, Light Housing and Pots, and false for everything else.
- **Provisional valuations** (no value matching; the refactor owns this). The new builders are `Tents`, `Pots`,
  `HandTools`, `FarmTools`, `Stone`, `Clay` and `Wood`:

  | Good | Curve and value | Extent |
  | --- | --- | --- |
  | Tents | exponential; V = people per tent × housing weight × e·b·s × tent life | N = held + own demand |
  | Pots | band at `ClaimValuation.PotValue` | held + own demand |
  | Hand Tools | band at replacement labour × lagged labour price | held + own demand |
  | Farm Tools | band at `FarmToolSavedYears` × price | held + own demand |
  | Stone, Clay, Wood | band at extraction labour × price | last turn's use × `MaterialReserveMultiplier` (from production receipts) |

  Goods priced at the labour price are `Unpriced` (inactive for that camp) until a plan has recorded
  `EconomicLabor.LabourPrice`.
- **Offers and W.** Each camp publishes an offer set: the goods it can give (`Stock > 0`), as goods only with no
  quantities. A requester's wants are drawn only from the partner's offers, one requisition per partner.
  `Trade.ListLength = max(TradeListLength, ceil(sqrt(active goods)))` (currently 4), so the W×W responder work grows
  linearly in the number of goods.
- **Split legs.** `TradeAgreement` has `WantParts`/`GiveParts` (the stock part) and `WantMade`/`GiveMade` (the
  made-to-order part, taken from `Valuation.Apply`). `AllStock` means no made part. Apply never draws stock below the
  curve's floor.
- **Settlement.** Every stock part goes into escrow at the opening. An all-stock agreement settles at the opening at
  `min` of the two legs' available shares, and receivers plan with what they receive. A mixed agreement settles after
  production: both legs settle at `min(deliverable)`, where deliverable = escrow + the maker's holding up to its made
  part. Delivery comes from escrow first, then from inventory; the rest of escrow is refunded. Conservation and
  non-negativity are checked per part.
- **Planning views.** A giver's stock part leaves the view (`Deltas`). Its made part is `TradeExports` (the made part
  only). `TradeMake`/`plan.Make` were removed. Receivers: all-stock agreements go into `Deltas`; mixed agreements go
  into `Deltas` + `Imports` only when `ImportServesNeeds`.
- **Demand hooks.**
  - `NeolithicProduction.Demand` = `OwnDemand` + made export for Light Housing, Pots and Farm Tools.
    `ToolReplacementDemands` adds Hand Tools. Cloth, Animal Fabric and Housing Material keep their `inventory − TradeExport`
    netting, which is algebraically identical under the new semantics.
  - `ExportedHousingMaterialLimit(…, stock = own, owed = outstanding)` no longer nets owed against held.
  - The resolver tracks `Camp.TradeOwed` (outstanding made exports) during production. Output fills it first, and goods
    made for export are held back from inputs (`TradeHeldForExport`) and from Hand Tool coverage credit.
    `MaterialEconomy.Forecast` mirrors this with `planned`.
  - `EconomicPlanner.Evaluate` reserves the made part (`reserved` dict, `fromStock` gone). `requested = reserved + own`
    everywhere, goals minus reserved, exported tents never shelter the camp, and the final role pass holds reserved
    Animal Fabric and Housing Material out of inputs.

## Open problems for the planner refactor

1. **Replace the per-good hooks with one generic export/import claim.** About 27 bespoke sites remain: `Demand`
   netting, `AddedExport`, `ToolReplacementDemands`, the Forecast fabric branch, `ExportedHousingMaterialLimit`, input
   holdbacks, `TradeImports`, and the planner reservation and `reserved` bookkeeping. A claim of the form
   (good, quantity, timing, serves-needs) should flow through demand, forecast, staffing and the resolver uniformly.
   - **Known inconsistency:** an owed Animal Fabric made part also lowers Cloth's net stock in the clothing target, so the
     pooled clothing need may double-produce.
2. **Shadow prices.** Tools, Pots and raw materials have only the provisional curves above. The planner should expose
   marginal values (per good, per camp) that trade reads directly instead of re-deriving them. This is "value matching".
   - **The seam exists** (`Docs/Trade.md`, "Planner-published values"). The new planner's fulfilment rule implements
     `ITradeValuePublisher.TradeValues(w, camp, demand, position)` and returns one `TradeValueSchedule` per curve key it
     prices: bands or `V e^{-Kx/N}` over the curve position (held units of the key's goods plus `Offset`), a `Floor`,
     and optionally a `MakeCost`.
   - `EconomicPlanner.Plan` records them on `EconomicLaborPlan.TradeValues`. The next handshake uses them in place of
     the provisional builders; keys left unpublished stay provisional, so goods can migrate one at a time.
   - Publishing needs no trade code changes. `TradeChecks.Published` covers every registered good.
   - Pots stay at the provisional value (about 90 food per Pot in the order fixture) until then: decision of
     4 October 2026.
3. **Valuation gaps from the last census** (before this commit; 5 worlds, 50 turns):
   - after-production delivery was 0.96–0.97;
   - the husbandry-first gate failed narrowly (animals total −5.06% at t = 50; median −5.56% at t = 25);
   - population −3.18%, QoL 0.8026 → 0.7965;
   - cultivation cloth per capita 0.745 vs 0.787.

   Causes:
   - husbandry camps consume fabric feedstock now but are paid late; escrowed food reaches growth only next turn;
   - cultivation clothing deficit;
   - Cloth delivery is the weakest.

   Re-measure after this commit; the timing changes (opening settlement of all-stock agreements) should help.
4. **Phase 3 items** from `Docs/TradeImmigrationPlannerPlan.md`: planner cost, the two planner paths (legacy vs shared
   allocation), and re-validating immigration (phase 2) once trade is stable.

## Tools

- Census:

  ```
  python3 Tools/PerformanceAudit/Run-TradeAudit.py --jobs 4
  python3 Tools/PerformanceAudit/Summarize-Trade.py
  ```

  Variants are reference (28eafcf), off and on. Off must equal reference exactly. The gate: husbandry-first population
  and animals, total and median, no worse than −5% and below 10×.
- Headless probes:
  - `--trade-probe=<save>` (handshake);
  - `--trade-resolve=<save>,<turns>` (resolved turns, with env `TRADE_SHORT` and `TRADE_SELLER`);
  - `--trade-profile=<save>` (work counters, `Trade.Counters`);
  - `--trade-roles=<save>` (buyer and seller deltas, on vs off).
- Checks: `Tools/Headless/TradeChecks.cs`, a fixed-suite entry run with `--only=TradeChecks`.
