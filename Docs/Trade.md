# Trade between known settlements (phase 1: AI-to-AI barter of goods and food)

Status: **implemented, opt-in** (`TradeEnabled`, false by default, in
`OpeningConfig.json` and in saves without the field). This is phase 1 of
[TradeImmigrationPlannerPlan.md](TradeImmigrationPlannerPlan.md): WP7 (foundation),
WP8 (same-turn handshake in the planning pass) and WP9 (food) of
[ValueModelImplementationPlan.md](ValueModelImplementationPlan.md), following
[QoLFissionTradeDesign.md](QoLFissionTradeDesign.md) §6–7. The tradeable set is
Cloth, Animal Fabric, Housing Material (T0 + T1) and food (TF).

Code: `Assets/_Anthropocene/Simulation/Trade.cs`. Hooks:

- `TurnPlanner.Prepare`/`Draft` compute the plan;
- `TurnPlanner.PlanningCampAt` shows the planner and the prepared needs the
  post-trade stock and food;
- `TurnResolver.Resolve` settles before dispatch;
- `ClaimValuation.NeedGoodTerms` and the `ExpansionValuation` food values supply
  the curves.

Checks: `Tools/Headless/TradeChecks.cs` (fixed suite). Probe:
`--trade-probe=<save>`. Audit: `Tools/PerformanceAudit/Run-TradeAudit.py` and
`Summarize-Trade.py`.

## Doctrine summary

| Question | Answer |
| --- | --- |
| Authoritative state | Camp inventories and typed food only. Each turn's settled trades and handshake counts are journalled in `TurnRecord.Trades`/`TradeActivity`. No requisition, offer or agreement is ever saved: every handshake completes or lapses within its turn. |
| Temporal abstraction | One handshake per turn, agreed at the committed boundary in the planning pass. It settles at the opening of resolution, before dispatch, so provisions, production and needs all see the stock the planner saw. Quantities are stocks, not rates. |
| Scaling law | `O(camps · (G log W + contacts))` pure, parallel valuation and request lists (`G` goods, `W = TradeListLength`), then `O(camps · K · W²)` canonical responses with a constant number of bisections each, then `O(trades · parts)` settlement. Cost follows eligible camps and grows linearly with the number of goods. Nothing scores all ordered pairs of goods. |
| Determinism | Valuations and requests are pure per camp (`PhaseExecution.Map`). Responses run once, sequentially: by responder camp ID, then the responder's own best gain (descending), then requester ID. Settlement is in agreement order. |
| RL / information | Automation-internal, like the economic planner: no new order kinds or observation fields. Partners come only from remembered contacts (`ContactKnowledge`). A request carries only the requester's ranked want and give lists; a responder's stock and values stay private to the pass. |
| Validation | `TradeChecks` (fixtures, food bands, conservation, purity, replay, persistence) plus the five-world census below. |

## Rules

**Eligibility.** All of the following must hold:

- `TradeEnabled`;
- residents > 0;
- not player 0 (the human's camps never trade until a player panel exists);
- a goods economy (Cultivation or Husbandry);
- a Society with remembered knowledge (past its first breakthrough).

**Goods are data.** `Trade.Registry` holds one descriptor per `TradeGood`:

- its stock parts (camp fields), so food is one good with three typed parts;
- the key of the value curve it is priced on (goods sharing a key share one
  curve and one position);
- whether the configuration trades it.

Adding a good is one descriptor, plus one curve builder only if no existing
curve fits. No handshake code names a good.

**Curves** (person-QoL-years per unit, the planner's units):

- **Clothing** (Cloth and Animal Fabric): the clothing claim's `V e^{-k x/N}`.
  Position is Cloth + Animal Fabric; `N` is this turn's requirement.
- **Housing** (Housing Material): one unit completes one fixed Housing unit.
  - `V` = people per unit × housing value per person;
  - position = shelter units + material held;
  - `N` = shelter units + this turn's Housing demand.

  It is worth nothing to a camp that cannot build (no Construction or no
  agriculture).
- **Food** (§7): bands on the envelope `P = stock + last turn's committed
  production` (`DemographicReport.FoodProduced`, a lagged forecast). The bands,
  in fill order, are:
  1. consumption of every linked consumer, at `v_food` (`FoodValue`);
  2. clothing-need feedstock and the operating reserve, at
     `v_reserve = FoodRiskWeight · v_food`. Bands 1 and 2 are **protected**;
  3. feasible growth food, at
     `v_growth = max(0, dW/dN) · L / food_per_birth` (`MarginalWelfare`,
     `PersonLife`, `FoodPerBirth`), capped at `v_reserve`;
  4. surplus, at 0.

  A camp never gives food from the protected band. The safe-only control
  (`TradeFoodSafeOnly`) also keeps growth food. Food moves in the seller's
  committed plant/animal/aquatic proportions.

**Requests.** A requester cannot see what partners hold, so it sends ranked
lists (§6.2's desired and offered bundles):

- **wants:** the `W` goods with the most recoverable utility, i.e. the
  integrated gain from its position up to the curve's target coverage;
- **gives:** the `W` giveable goods with the lowest marginal loss.

A request goes out only if some listed pair is one the requester itself would
accept. It goes to at most `K` remembered, unmoved foreign partners within
`TradeRange` hexes (nearest first, ties by camp ID).

**Responses (the handshake).** The responder evaluates only the `W × W` listed
pairs against its own stock and keeps the agreement that gains it most. For
each pair:

1. Quantity is the requester's 1:1 best quantity (marginal values equal),
   capped by what the responder may give.
2. The requester's limit `r_max` is the largest give-per-want ratio with
   `Gain(want) (1 − t) ≥ (1 + m) Loss(give)`. The transport loss is
   `t = TradeDistanceCostPerHex × distance`.
3. The responder's floor `r_min` is the smallest ratio with
   `Gain(give) (1 − t) ≥ (1 + m) Loss(want)`.
4. If `r_min ≤ 1`, the responder **honours** at 1:1 (or at `r_max` when that
   is below 1). If `1 < r_min ≤ r_max`, it **counters** at `r_min` and the
   requester closes automatically. Otherwise the quantity is halved, at most
   three times.

If no pair works, the request is **ignored**. A confirmed agreement updates
both sides' positions at once, so no stock is promised twice. Kin use margin
`m (1 − Kinship.Affinity)`.

**Planning injection.** The plan is a pure function of the committed world,
independent of orders. `Prepare` therefore reuses a matching draft's plan, and
draft intents remain valid. The planner and the prepared needs see each
trading camp with its post-trade goods and typed food (a private copy,
`Camp.WithPrivateInventory`). Confirmed food imports enter the prepared food
envelope (`TurnNeeds.OpeningCampFood`, bucket targets, feedstock gaps) before
demographic planning. Unconfirmed food never does, and a unit sold is
counted only by the buyer.

**Settlement.** Agreements transfer each stock part from committed stock in
agreement order, before dispatch. If a side holds less than agreed, both sides
of that agreement scale in proportion (`Delivered < 1`). The ordinary food
ledger then scales the buyer's reserve and growth to the food actually held.
Settlement checks conservation of every part and that no stock goes negative.
Both Societies record a `ContactSource.Trade` contact. Imported food credits
`RecordTradePractice` for the practice that produced it (plant to Gathering,
animal to Hunting, aquatic to Fishing). This is inert while
`TradePracticePerValue = 0`.

## Persistence and compatibility

- **Config.** `TradeEnabled`, `TradeRange`, `TradeRequisitionsPerCamp`,
  `TradeListLength`, `TradeMargin`, `TradeDistanceCostPerHex`,
  `TradeFoodEnabled` and `TradeFoodSafeOnly` are `OptionalField`. Saves without
  them load disabled with the default levers.
- **Turn records.** `TurnRecord.Trades` and `TurnRecord.TradeActivity` are
  optional. Older records load with an empty journal and no activity.
- **Format.** No save-format or observation-schema change (`ANTHROPOCENE-11`,
  schema 18).
- **Default-off behaviour.** The disabled path computes nothing and moves
  nothing. The config fields enter the fingerprint, so default-off exactness is
  measured on outcomes against the pre-trade commit (below).

## Limitations (phase 1)

- AI-to-AI only. The human player neither trades nor sees trade. The player
  panel and order kinds (WP7's `OrderRules`/`LegalActions` part) need the Unity
  workstation.
- Barter of one pair per agreement and one agreement per request. Multi-good
  bundles are not formed.
- Trade is from stock only. Goods that are made to need and consumed within
  the turn (Housing Material, most Cloth) are rarely in stock at the boundary,
  so they rarely trade. Producing to order is the planner's job: an export
  claim in the fulfilment plan, which is the phase 3 interface.
- The food envelope's income term is last turn's production, not this turn's
  plan.
- Animal Fabric is valued only as clothing, not as Light Housing input.
- Range is measured to the remembered tile.

## Measurements

**Setup.** `Run-TradeAudit.py` runs on five Standard/80-tribe worlds (Continents
1–3, Pangaea 1, Archipelagos 1) for 50 post-opening turns, from opening
checkpoints saved by the current source with trade off. The configuration is
the census one:

- fission, herd labour, QoL mortality (elasticity 1) and shared allocation on;
- immigration off.

There are three variants:

- `reference`: the pre-trade commit `28eafcf`;
- `off`: the current source with trade off;
- `on`: the current source with `TradeEnabled=1`.

Use `Summarize-Trade.py` to reduce the output.

**Default-off exactness.** Every per-turn outcome and per-camp row of `off`
equals `reference` on all five worlds over 50 turns.

**Trade on: structurally inert for T0 goods.** On C1 and C2:

- trade-eligible camp-turns: 9,566 and 9,040;
- requisitions: 647 and 762;
- honoured or countered: none; **all were ignored**;
- trades settled: zero;
- outcomes identical to `off`;
- handshake cost: mean 0.9 ms per turn, maximum 9 ms.

**All five worlds.** Totals over 50 turns:

| World | Eligible camp-turns | Requisitions | Trades | Handshake ms/turn (mean / max) |
| --- | ---: | ---: | ---: | --- |
| C1 | 9,566 | 647 | 0 | 0.89 / 7.9 |
| C2 | 9,040 | 762 | 0 | 0.95 / 9.0 |
| C3 | 11,441 | 916 | 0 | 1.66 / 16.5 |
| P1 | 9,137 | 618 | 0 | 1.07 / 11.9 |
| A1 | 12,058 | 1,069 | 0 | 1.24 / 8.4 |

Every requisition was ignored, and `on` equals `off` exactly. The husbandry-first
gate therefore passes trivially (+0.00% at 25 and 50 turns).

`--trade-probe=<save>` on the C1 state at turn 36 (119 eligible camps) shows why:

- **Housing Material:** total stock at the turn boundary is 0. It is a pure
  intermediate, made and consumed within the turn of the Housing it completes.
- **Clothing:** husbandry camps hold about one turn's requirement as Animal
  Fabric. Cultivation camps hold none. 30 camps hold residual Cloth (3,145 units
  in all), and it is those camps that request Housing Material that nobody holds.
- **Contacts:** none in range at the opening. Camps start beyond each other's
  vision; contacts appear with fission daughters from about turn 23.

So barter of T0 goods from committed stock has nothing to exchange. The genuine
complementarity is clothing (Animal Fabric from herders) against **food** (from
farmers), which needs WP9. A goods-for-goods market needs either goods produced
to order (forward claims in the fulfilment plan, a phase 3 interface question)
or a counter-good held as stock. Food is that counter-good.
