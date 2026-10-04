# Trade handoff (current state)

Branch `features/herd-ecology-retention`, commit after `85f584e`. This file is the up-to-date description of trade.
`Docs/Trade.md` is the longer reference, but it still describes the earlier four-good, whole-leg design. Where they
differ, this file wins until `Trade.md` is rewritten. The planner-side follow-up is in
`Docs/TradePlannerRefactorHandoff.md`.

## Scope and switches

- Trade is AI to AI only: player 0 never trades. There are no order kinds or observation fields yet.
- Turn timing:
  - each handshake is agreed in planning from the committed boundary;
  - nothing pending is saved;
  - immigration stays off in trade runs.
- Configuration lives in `SimulationConfig`; every field is an OptionalField, with defaults in `OpeningConfig.json`.

  | Setting | Default |
  | --- | --- |
  | `TradeEnabled` (opt-in) | false |
  | `TradeRange` | 6 |
  | `TradeRequisitionsPerCamp` (K) | 3 |
  | `TradeListLength` (W floor) | 3 |
  | `TradeMargin` | .10 |
  | `TradeDistanceCostPerHex` | .01 |
  | `TradeFoodEnabled` | true |
  | `TradeFoodSafeOnly` | false |
  | `TradeExportLabourShare` | .25 |

- Eligible camps:
  - have population above zero;
  - are not player 0;
  - have a goods economy (Cultivation or Husbandry);
  - have Society memory.

## Goods (`Trade.Registry`, `Trade.cs`)

`TradeGood` is append-only (saved `TradeRecord`s store its number). Adding a good means adding one descriptor, plus a
curve builder if it needs one. A good with a recipe can be made to order and has exactly one part.

| Good | Curve key | Made to order | Import serves needs this turn | Value (provisional except Clothing, Housing and Food) |
| --- | --- | --- | --- | --- |
| Cloth, Animal Fabric | Clothing (shared position) | yes | yes | need curve `V e^{-kx/N}` from `ClaimValuation.NeedGoodTerms` |
| Housing Material | Housing | yes | no | fixed-Housing value; zero unless the camp can build |
| Food (plant, animal, aquatic parts) | Food | no | no | bands on stock + last turn's production (see below) |
| Light Housing | Tents | yes | yes | exponential: shelter of `LightHousingPeoplePerUnit` × tent life; N = held + own demand |
| Pots | Pots | yes | yes | band at `PotValue`, up to held + own demand |
| Hand Tools | HandTools | yes | no | band at replacement labour × lagged labour price |
| Farm Tools | FarmTools | yes | no | band at `FarmToolSavedYears` × price |
| Stone, Clay, Wood | own key each | no | no | band at extraction labour × price, up to last turn's use × `MaterialReserveMultiplier` |

- **Unpriced goods.** Goods priced by the labour price (`EconomicLaborPlan.LabourPrice`) are inactive for a camp until
  a plan records that price. The price is recorded only while trade is on.
- **Food bands.**
  - Consumption is valued at `v_food`; feedstock and reserve at `v_reserve`; growth at `v_growth`; surplus at 0.
  - Food below the protected end is never given. Under `TradeFoodSafeOnly`, food below the growth end is never given.
- **Make or buy.** Each camp values a makeable good on `W'(x)`: its use curve, flattened at its making cost over its
  capacity.
  - Cost = labour per unit × w + food inputs × `v_food`.
  - Capacity = `TradeExportLabourShare` × residents × years / labour. It is bounded by spare typed food and, for
    equipment or housing work, by a shared tool pool.

## Handshake (`Trade.Plan`, pure; computed by `TurnPlanner.Prepare`/`Draft`)

1. **Valuation.** Each eligible camp is valued once (parallel map).
2. **Offers.** Each camp publishes an offer set: the goods it can give now. This is goods only, never quantities.
3. **Requisitions.** Each camp sends at most K requisitions, to remembered foreign settlements within range, nearest
   first.
   - Each requisition is built against that partner's offers:
     - wants are the top-W offered goods by recoverable gain;
     - gives are the top-W of its own giveable goods by lowest loss.
   - W = `Trade.ListLength` = max(`TradeListLength`, ⌈√active goods⌉), which is 4 today.
   - A request is sent only if the requester itself would accept some pair.
4. **Responses.** Responders are processed in ID order; their requests are ordered by the responder's best gain, then
   requester ID.
   - The responder evaluates W×W pairs:
     - quantity starts at the requester's best 1:1 quantity, capped by stock;
     - both sides use closed-form ratio limits;
     - it honours at 1:1, counters at its floor, or halves the quantity up to 3 times;
     - otherwise it ignores the request.
   - Margin and transport:
     - the kin margin is `m(1 − affinity)`;
     - transport loss is `TradeDistanceCostPerHex × d`.
   - A pair never moves one curve both ways in the same turn.
5. **Splitting legs.** Each accepted agreement updates both valuations. `Apply` splits each leg into:
   - a stock part (`WantParts`/`GiveParts`, in the giver's committed proportions, never below its floor);
   - a made-to-order part (`WantMade`/`GiveMade`).

   `AllStock` means neither leg has a made part.
6. **Planning views** (`ApplyToPlanningCamp` via `TurnPlanner.PlanningCampAt`):
   - **Giver:** the stock part leaves its view. The made part becomes `Camp.TradeExports` (by CraftGood), which demand
     adds and the planner reserves first.
   - **Receiver, all-stock agreement:** the goods join its stock.
   - **Receiver, mixed agreement:** the goods join its stock plus `Camp.TradeImports` (counted for needs, never as
     inputs), but only if the import serves needs. Otherwise they arrive for next turn.

## Settlement (`TurnResolver`, `GenerationalEconomy`)

- **Opening, before dispatch (`Trade.Settle`).** Every stock part moves into escrow at the share its giver still holds.
  An all-stock agreement settles immediately: both legs at the smaller share, with the rest refunded.
- **After all camps' production, before needs (`Trade.SettleAfterProduction`).** This step runs only when the plan has
  mixed agreements; the production loop is split for it. Pay on delivery applies:
  1. Each leg's deliverable share = (escrow + the maker's holding, up to its made part) / leg quantity.
  2. Both legs settle at the smaller share, from escrow first and then from inventory.
  3. Any escrow left is refunded.
  4. Food moved here is recorded in `DemographicReport.TradeFood`, so food ledgers balance.
- **During production.** `Camp.TradeOwed` tracks the made exports still outstanding:
  - output fills them first;
  - goods made for export are held back from recipe inputs and from Hand Tool coverage;
  - `TradeExports`/`TradeOwed` are cleared before needs.
- **Conservation.** Every part (including escrow) is conserved and never negative; a violation throws.
- **Recording (`Trade.Finish`).**
  - It writes `TradeRecord`: delivered quantities, `Delivered`/`GiveDelivered` and `WantMadeToOrder`/`GiveMadeToOrder`.
  - It writes `TurnRecord.TradeActivity` (a `TradeSummary`).
  - It calls `ContactKnowledge.RecordTrade` and the inert `RecordTradePractice`.

## Files

| Area | Location |
| --- | --- |
| Trade logic | `Simulation/Trade.cs` |
| Camp fields and config | `Model.cs` (`TradeExports`, `TradeOwed`, `TradeImports`, `WithPrivateInventory`, trade config) |
| Prepared turn and needs | `TurnPlanning.cs` (`PreparedTurn.Trade`, `TurnNeeds.Exports`) |
| Production hooks | `NeolithicProduction.cs`: `Demand`/`OwnDemand`, `ToolReplacementDemands`, `ExportedHousingMaterialLimit`, holdbacks |
| Forecast | `MaterialEconomy.Forecast` |
| Planner reservation | `EconomicPlanner.cs` (reservation, `reserved`, `LabourPrice`) |
| Settlement split | `GenerationalEconomy.cs` |
| Metrics | `PhaseExecution.cs` (`ResolutionMetrics` trade fields) |
| Checks | `Tools/Headless/TradeChecks.cs` |
| Audit | `Tools/PerformanceAudit/Run-TradeAudit.py`, `Summarize-Trade.py` |

## Planner-published values

Any good's curve can now come from the planner instead of its provisional builder: see `Docs/Trade.md`,
"Planner-published values".

- A fulfilment rule implementing `ITradeValuePublisher` returns `TradeValueSchedule`s, at most one per curve key. Each
  is bands or `V e^{-Kx/N}`, with an offset, a floor and an optional make cost.
- `EconomicPlanner.Plan` saves them on `EconomicLaborPlan.TradeValues` (OptionalField) while trade is on.
- The next handshake reads them lagged, like `LabourPrice`.
- No shipped rule publishes yet, so outcomes are unchanged.
- **Decision (4 October 2026):** the provisional Pots band stays the Pots value for now, about 90 food per Pot in the
  order fixture (`TradeChecks.Pots`), until the planner publishes Pots.

## Verification status

**Current.** `--only=TradeChecks` passes all sections (454 assertions, including `Numerics`):

- Registry: unpriced goods have no curve, and want scores now use make-or-buy gain, as `Lists` does.
- Food: rewritten for opening settlement of all-stock agreements. Pay on delivery moved to MadeToOrder.
- MadeToOrder: `OrderWorld(fabricOnly: true)` zeroes every other craft priority, so only fabric is made to order.
  With a labour price, Pots, tents and tools all trade to order.
- New sections: offer sets, `Pots` and `Published`.

`python Tools/Docs/Check-CurrentContract.py` passes.

**Still to do:**

- Fix made-to-order delivery of the new goods and the husbandry gate (see the census below); profile the handshake.
- Add a resolver-level check that goods made for export are held back from recipe inputs and Hand Tool coverage.
- Rewrite `Docs/Trade.md` for the 11 goods and split legs; it carries a superseded note.
- Update `Docs/CurrentSimulationContract.md`, `Docs/TurnResolutionPasses.md` and the AGENTS.md trade row.

**Census of the 11-good semantics** (4 October 2026). Same five worlds, Standard/80, 50 post-opening turns,
`Run-TradeAudit.py --jobs 4`, .NET SDK 8.0.318. Source is `5e8e987` plus the NaN fix below. Output is in
`TestResults/Trade-nanfix` (ignored).

- **Exactness.** Trade off equals the reference (`28eafcf`) exactly on all five worlds.
- **Gate: FAIL**, worse than before the 11-good commit:

  | Husbandry-first measure | t = 25 | t = 50 |
  | --- | --- | --- |
  | Animals, total | −9.33% | −11.70% |
  | Animals, median | −16.7% | −18.5% |
  | Linked population, total | +0.96% | −1.78% |
  | Linked population, median | — | −0.45% |

- **Delivery.** After-production delivery fell to 0.52–0.77 (was 0.96–0.97). The shortfall is concentrated in the new
  made goods:

  | Good | Mean delivered | Short legs |
  | --- | --- | --- |
  | Farm Tools | 0.297 | 2,855 of 3,904 |
  | Light Housing | 0.449 | — |
  | Pots | 0.624 | — |
  | Hand Tools | 0.742 | — |
  | Cloth | 0.838 | — |
  | Animal Fabric | 0.889 | — |
  | Housing Material | 0.898 | — |

  The new goods' demand hooks (`AddedExport`, `ToolReplacementDemands`) apparently do not get them made. Check that
  planner staffing and resolver production actually produce reserved Farm Tools, tents and Pots.
- **Food paid for goods** is large: about 8 million food per world over 50 turns. Typical prices per unit:

  | Good | Food per unit |
  | --- | --- |
  | Light Housing | ~250 |
  | Housing Material | ~120 |
  | Pots | ~82 |
  | Cloth, Animal Fabric | ~45–55 |

  These come from the provisional curves; the Pots value is kept by decision. This is the likeliest driver of the
  animal losses. Measure which camps pay with what food before changing valuations.
- **Handshake time.** Mean 44–68 ms per turn, max 318 ms. The last 10 turns average 113–164 ms. Before the commit it
  was 4–6 ms mean and about 30 ms max. This needs profiling (`--trade-profile`) before the scale runs.
- **NaN fix.** C1's first run crashed writing a `TradeRecord` with `ResponderGain = NaN`. A tent maker holding
  1.8e-10 tents has `N = 1.8e-10`. `Valuation.W` folded the capacity shift into `V`, which underflowed to zero, and the
  integral became `0 × e^{+huge}`. The NaN loss sent `MinimumRatio` into its zero-loss branch, so the maker agreed to
  52 tents for 52 Stone. Segments now carry the shift (`V e^{-K(x+S)/N}`), covered by `TradeChecks.Numerics`. The
  audit tool now logs `TRADE_NONFINITE` and writes named float literals; the rerun logged none.

**Earlier census** (before `85f584e`; five worlds C1, C2, C3, P1, A1; Standard/80; 50 turns):

- Trade off equals the reference exactly.
- After-production delivery was 0.96–0.97.
- The husbandry-first gate failed narrowly: animals total −5.06% at t = 50, median −5.56% at t = 25.
- Population was −3.18%; QoL went from 0.8026 to 0.7965.
- The handshake took 4–6 ms per turn on average (about 30 ms max).

**Known limitations:**

- Valuations for the new goods are provisional.
- About 27 per-good hooks still need a generic claim.
- Cross-good fabric netting may double-produce.

These are covered in the planner refactor handoff.
