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
- `TurnResolver.Resolve` settles food before dispatch and goods between production and needs (`GenerationalEconomy.cs`);
- `ClaimValuation.NeedGoodTerms` and the `ExpansionValuation` food values supply
  the curves.

Checks: `Tools/Headless/TradeChecks.cs` (fixed suite). Probe:
`--trade-probe=<save>`. Audit: `Tools/PerformanceAudit/Run-TradeAudit.py` and
`Summarize-Trade.py`.

## Doctrine summary

| Question | Answer |
| --- | --- |
| Authoritative state | Camp inventories and typed food only. Each turn's settled trades and handshake counts are journalled in `TurnRecord.Trades`/`TradeActivity`. No requisition, offer or agreement is ever saved: every handshake completes or lapses within its turn. |
| Temporal abstraction | One handshake per turn, agreed at the committed boundary in the planning pass. Food (a stock) settles at the opening of resolution, before dispatch. Goods a camp can make (Cloth, Animal Fabric, Housing Material) settle after every camp's production and before every camp's needs. A seller can therefore make them to order this turn, and a buyer's needs see them. Quantities are stocks, not rates. |
| Scaling law | `O(camps · (G log W + contacts))` (valuations cache each camp's need terms; the responder's ratio bounds are closed-form inverses of the capped curves, and a ranked answer on unchanged positions is reused) pure, parallel valuation and request lists (`G` goods, `W = TradeListLength`), then `O(camps · K · W²)` canonical responses with a constant number of bisections each, then `O(trades · parts)` settlement. Cost follows eligible camps and grows linearly with the number of goods. Nothing scores all ordered pairs of goods. The planner runs once per camp as before, with export claims as fixed demand. |
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

**Made to order.** A good with a recipe the camp can run (its
agriculture/husbandry requirement met, priority > 0) may be offered beyond stock:

- **cost per unit:** `LabourPerUnit · w + food inputs · v_food(P)`. Here `w` is
  the labour price the camp's last Value plan chose
  (`EconomicLaborPlan.LabourPrice`, recorded only while trade is on), and
  `LabourPerUnit` is the claim valuation's own formula
  (`ClaimValuation.LabourPerUnit`);
- **capacity:** `TradeExportLabourShare` of its residents' turn, bounded by:
  - the spare (unprotected) food its recipe consumes, typed;
  - for equipment and housing work, the same share of its tool outputs (one
    Hand Tool covers one output, one Regular Tool four). This pool is shared by
    all such goods, so a camp cannot promise the same tools twice.
- **make or buy:** giving and receiving are both valued on
  `W'(x) = u(x + cap)` below `x* − cap`, `cost` on `[x* − cap, x*]`, and `u(x)`
  above, where:
  - `u` is the use value;
  - `cap` is the remaining make-to-order capacity;
  - `x*` is where `u = cost`.

  `W` is what the camp's best own making leaves the good worth.
  `min(u, cost) ≤ W' ≤ u`, so a camp with little capacity prices its stock near
  its use value (it cannot simply remake what it sells), and receiving a good it
  can make cheaply is worth little more than the making cost. `W'` is still
  piecewise constant or exponential, so the responder's bounds keep their
  closed-form inverses. A gift draws on capacity by the making it induces
  (refilling towards `x*` within `cap`) and on held stock for the rest; the
  induced part is the agreement's made-to-order quantity.

Without a recorded labour price (for example the first trading turn), nothing is
made to order.

**Export claims and reservation.** Every unit a seller agrees to deliver of such a
good is an export claim on its planning view (`Camp.TradeExports`, transient).

- **Fixed claim.** The Value planner (`EconomicPlanner.Plan`, `Evaluate`) staffs
  the made-to-order part (`Camp.TradeMake`) first, at any fulfilment position. It
  reserves the recipe role's labour and debits the claim's materials and food
  inputs. The rest is delivered from stock. Stock held for delivery is excluded from own use. The claim never draws
  on the own clothing-need feedstock envelope.
- **Demand and forecast.** `NeolithicProduction.Demand` and
  `MaterialEconomy.Forecast` net the claim against stock, so the material
  re-forecast plans the claim's Wood and Clay. A good owed as itself (Cloth or
  Animal Fabric) is forecast as that good, whatever the pooled clothing need. The clothing claim's position
  falls by it. The clothing-need feedstock, the food ledger and the prepared
  `TurnNeeds.Production` include it.
- **Resolver.** Owed goods are held back from recipe inputs while the camp
  produces.

Imports arrive after production:

- The buyer's planning view counts Cloth and Animal Fabric imports as stock for
  this turn's needs, but never as recipe inputs (`Camp.TradeImports`), so it
  does not plan tents from fabric that has not arrived.
- Housing Material is only a production input (the Housing assembly runs during
  production). An import of it is therefore stock from the next turn and does
  not enter the buyer's planning view.
- Exported Housing Material is made before, and outside, the seller's own
  assembly ceiling (`NeolithicProduction.ExportedHousingMaterialLimit`). The
  planner reserves its Wood and Clay through its material plan.

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
both sides' positions at once, so no stock is promised twice. A response may not
move a curve in the opposite direction to a flow already agreed between the same
pair this turn. A camp may still buy from one neighbour and sell to another. Kin use margin
`m (1 − Kinship.Affinity)`.

**Planning injection.** The plan is a pure function of the committed world,
independent of orders. `Prepare` therefore reuses a matching draft's plan, and
draft intents remain valid. The planner and the prepared needs see each
trading camp with its post-trade goods and typed food (a private copy,
`Camp.WithPrivateInventory`). Confirmed food imports enter the prepared food
envelope (`TurnNeeds.OpeningCampFood`, bucket targets, feedstock gaps) before
demographic planning. Unconfirmed food never does, and a unit sold is
counted only by the buyer.

**Settlement, paid on delivery.** Each agreement has two legs, one per good.

- **Food legs** leave their giver at the opening of resolution, before dispatch,
  into escrow. The giver's provisions, food ledger and growth see the reduced
  stock its prepared needs saw.
- **Goods legs** settle after every camp's production and before every camp's
  needs. The production/needs loop is split only in turns that have such legs.
  Two goods legs scale together if a side is short. A goods leg never delivers
  more than the other side's escrow can pay for.
- **Releasing escrow.** After the goods legs settle, each escrowed food leg pays
  its receiver the share of the agreement the goods leg delivered and refunds
  the rest to its giver. Both legs therefore record the same delivered fraction
  (`Delivered`/`GiveDelivered`). Food that moves then is reported per camp as
  `DemographicReport.TradeFood`, so the food ledger still balances.
- **Consequence:** food received through trade arrives after this turn's growth
  plan. It is stock for the next turn and is not in the receiver's planning view.

Settlement never moves more of a part than its giver holds. It checks
conservation of every stock part (camps plus escrow) and that nothing goes
negative. Records are written after the economy phase, marking made-to-order
legs (`WantMadeToOrder`/`GiveMadeToOrder`). Both Societies record a
`ContactSource.Trade` contact. Received food credits the inert
`RecordTradePractice`.

## Persistence and compatibility

- **Config.** `TradeEnabled`, `TradeRange`, `TradeRequisitionsPerCamp`,
  `TradeListLength`, `TradeMargin`, `TradeDistanceCostPerHex`,
  `TradeFoodEnabled`, `TradeFoodSafeOnly` and `TradeExportLabourShare` are
  `OptionalField`.
- **Reports.** `DemographicReport.TradeFood` is optional; older reports load
  zero.
- **Labour price and records.** `EconomicLaborPlan.LabourPriceSet`/`LabourPrice`
  (outside the intent stamp) and `TradeRecord.GiveDelivered`/`WantMadeToOrder`/
  `GiveMadeToOrder` are optional. Older records load with the give leg fully
  delivered. Saves without
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
- The made-to-order cost uses last turn's labour price, and capacity is a
  labour share bounded by spare food inputs. The planner reserves the claim's
  labour and inputs, but a shortage of materials or tools can still leave a
  delivery short. The seller is then paid in proportion, and the buyer, who
  planned on the full import, falls short that turn.
- Food bought with goods arrives after this turn's growth plan (pay on
  delivery), so it supports growth only from the next turn.
- Imported Animal Fabric arrives after production, so it cannot feed this
  turn's Light Housing, although the buyer's planner counted it.
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
- `on`: `TradeEnabled=1` with the defaults (food on, W = 3, K = 3).

`Summarize-Trade.py` reduces the output.

**Default-off exactness.** Every per-turn outcome and per-camp row of `off`
equals `reference` on all five worlds over 50 turns.

**Trade on.** Totals over 50 turns:

| World | Eligible camp-turns | Requisitions | Trades (honoured / countered) | Fabric ← food | Cloth ← food | Food ← clothing | Handshake ms/turn (mean / max) |
| --- | ---: | ---: | --- | ---: | ---: | ---: | --- |
| C1 | 9,577 | 6,723 | 1,393 (34 / 1,359) | 1,273 | 86 | 34 | 3.9 / 30 |
| C2 | 8,983 | 5,891 | 1,243 (10 / 1,233) | 1,128 | 105 | 10 | 3.8 / 35 |
| C3 | 11,619 | 8,821 | 1,910 (62 / 1,848) | 1,705 | 143 | 62 | 6.5 / 101 |
| P1 | 9,068 | 6,191 | 1,331 (38 / 1,293) | 1,190 | 103 | 38 | 4.0 / 50 |
| A1 | 12,123 | 7,502 | 1,487 (117 / 1,370) | 1,229 | 141 | 117 | 4.7 / 23 |

Trade is the farmer–herder exchange. Villages buy clothing (mostly Animal
Fabric) with food, at about 30–40 food per clothing unit (C1: 106,809 fabric
for 3,510,578 food). That rate follows from a seller's high clothing value
against the buyer's low growth-band food value. A few camps short of food buy
it at 1:1 for clothing. Housing Material never trades: there is no boundary
stock (see the limitations). No goods trade happened before contacts formed,
around turns 18–25.

Pooled outcomes on the five worlds, trade on against off:

| Turn | Population | Weighted QoL | Camps | Cultivation-first population | Husbandry-first population (median) | Husbandry-first animals (median) |
| --- | --- | --- | --- | --- | --- | --- |
| 25 | 211,270 → 209,907 (−0.65%) | 0.8597 → 0.8573 | 668 → 666 | +0.07% | −1.44% (+0.60%) | +0.07% (0%) |
| 50 | 1,383,458 → 1,401,517 (+1.31%) | 0.8026 → 0.8008 | 3,091 → 3,097 | +1.01% | +1.42% (+2.04%) | +0.58% (+3.39%) |

The husbandry-first gate (no more than −5% and below 10×) passes at 25 and 50
turns. The effect is small because trade moves stock, not production: a camp's
planner adapts only from the turn after it trades.

**Made-to-order census (commit `ff79821`): gate fails.** Same setup, with
export claims and settlement between production and needs.

- `off` still equals `reference` exactly on all five worlds.
- `on` makes 3,163–4,269 trades per world over 50 turns. Housing Material is
  now traded heavily (for food, Cloth and Animal Fabric), as is Animal Fabric
  for food. The handshake costs 9–15 ms per turn on average (maximum 162 ms).
- Of the made-to-order legs, a mean of 64–66% was delivered, and 2,054–2,756
  per world (about half) were short.

Pooled outcomes, trade on against off:

| Turn | Population | Weighted QoL | Camps | Cultivation-first population | Husbandry-first population (median) | Husbandry-first animals (median) |
| --- | --- | --- | --- | --- | --- | --- |
| 25 | −0.38% | 0.8597 → 0.8517 | 668 → 732 | −0.07% | +0.64% (−19.7%) | −0.20% (−16.7%) |
| 50 | −1.11% | 0.8026 → 0.7862 | 3,091 → 3,451 | −7.59% | +0.92% (−13.8%) | +0.01% (−16.9%) |

The husbandry-first medians fail the −5% gate. Totals do not move much, but
there are more, smaller camps.

**Diagnosis** (`--trade-resolve=<save>,1` with `TRADE_SHORT=1`, on the C1
state after turn 46). Every short seller made exactly its prepared plan, and the
plan carried only part of the export. For example, Housing Material: 27 planned
against 314 owed, 13 against 263, 1 against 294; Cloth: 0 against 242.

The export claim raises demand, but it does not bind the planner:

- Housing Material is not a valued claim.
- Nothing reserves labour or materials for an export.
- Capacity is a labour share, not the planner's actual spare labour.

Meanwhile buyers paid food at the opening and planned on the full import. That
cost farming villages food (cultivation-first population −7.6% at 50 turns) and
lowered QoL.

**Follow-up fixes and census (commit `2a3a02f`).** These fixes followed the
diagnosis above, each found with `--trade-resolve` or `--trade-roles` on the C1
state after turn 46:

- **Pay on delivery.** Food is escrowed at the opening and pays the delivered
  share after production.
- **Planner reservation.** The planner staffs and supplies the made part of
  each export first.
- **Tool pool.** Made-to-order capacity is bounded by a shared tool-output
  pool.
- **Make or buy.** Giving and receiving are valued on the make-or-buy curve
  `W'`, so sellers with little capacity no longer sell stock at making cost.
- **Owed fabric.** An owed Cloth or Animal Fabric deficit is forecast as that
  good.
- **Import timing.** Imports are never same-turn recipe inputs, and Housing
  Material imports are next-turn stock.
- **Housing Material reservation.** The reservation plans its Housing Material
  inputs, which it previously never made.
- **Assembly ceiling.** Owed Housing Material is made outside the seller's
  assembly ceiling.

Five worlds, 50 turns, trade on against off:

- `off` equals `reference` exactly.
- Trades: 3,460–5,013 per world. Mean made-to-order delivery: 0.961–0.974,
  with 249–378 short legs per world (about 5%).
- Handshake: mean 4–6 ms per turn, maximum 28–32 ms, with the 4-worker
  audit's preparation. Per-turn prepare and execute times are within run noise
  of `off` (one run per variant on a shared machine).

| Turn | Population | Weighted QoL | Camps | Cultivation-first population | Husbandry-first population, total (median) | Husbandry-first animals, total (median) |
| --- | --- | --- | --- | --- | --- | --- |
| 25 | −1.85% | 0.8597 → 0.8553 | 668 → 652 | −0.23% | −3.84% (+1.35%) | −3.84% (−5.56%, 18 → 17) |
| 50 | −3.18% | 0.8026 → 0.7965 | 3,091 → 2,998 | +0.04% | −4.17% (0.00%) | −5.06% (+2.54%) |

The gate still fails narrowly: herd animals total −5.06% at 50 turns, and the
median at 25 turns is one animal on 18. Two known losses remain:

- **Cultivation-first clothing coverage** falls from 0.787 to 0.745 at 50 turns.
- **Husbandry growth.** Making fabric to order consumes animal food this turn,
  while its food payment arrives only after this turn's growth plan (pay on
  delivery). Husbandry-first camps grow and fission less (2,201 against 2,312
  camps).

**Earlier T0-only run (commit `89f7a54`).** With Cloth and Housing Material
only, all 5,012 requisitions on the five worlds were ignored and outcomes
equalled `off`. Housing Material has no boundary stock, and clothing was held
only by herders, who had nothing to buy with it. That run is what motivated
adding Animal Fabric and food.
