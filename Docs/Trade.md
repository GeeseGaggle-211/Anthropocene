# Trade between known settlements (phase 1: AI-to-AI barter of T0 goods)

Status: **implemented, opt-in** (`TradeEnabled`, false by default, in
`OpeningConfig.json` and in saves without the field). This is phase 1 of
[TradeImmigrationPlannerPlan.md](TradeImmigrationPlannerPlan.md): WP7 (foundation)
and WP8 (same-turn handshake in the planning pass) of
[ValueModelImplementationPlan.md](ValueModelImplementationPlan.md), following
[QoLFissionTradeDesign.md](QoLFissionTradeDesign.md) §6. Food (WP9) is the next step
and is not traded yet.

Code: `Assets/_Anthropocene/Simulation/Trade.cs`. Hooks:

- `TurnPlanner.Prepare`/`Draft` compute the plan;
- `TurnPlanner.PlanningCampAt` shows the planner the post-trade stock;
- `TurnResolver.Resolve` settles;
- `ClaimValuation.NeedGoodTerms` is the shared value curve.

Checks: `Tools/Headless/TradeChecks.cs` (fixed suite). Audit:
`Tools/PerformanceAudit/Run-TradeAudit.py`.

## Doctrine summary

| Question | Answer |
| --- | --- |
| Authoritative state | Camp inventories only. Each turn's settled trades and handshake counts are journalled in `TurnRecord.Trades`/`TradeActivity`. No requisition, offer or agreement is ever saved: every handshake completes or lapses within its turn. |
| Temporal abstraction | One handshake per turn, agreed at the committed boundary in the planning pass. It settles at the opening of the economy phase (after hunter returns, before production and needs). Quantities are stocks, not rates, so turn length does not change the rule. |
| Scaling law | `O(camps)` valuations (pure, parallel), then `O(camps · contacts)` partner selection (parallel), then `O(camps · K)` canonical responses with a constant number of bisections each, then `O(trades)` settlement. Cost follows eligible camps; it does not follow map size or elapsed time. |
| Determinism | Valuations and requisitions are pure per camp (`PhaseExecution.Map`). Responses run once, sequentially: by responder camp ID, then the responder's own gain (descending), then requester ID and good. Settlement is in agreement order. |
| RL / information | Phase 1 is automation-internal, like the economic planner: no new order kinds, no observation fields. Partners come only from the Society's remembered contacts (`ContactKnowledge`), never from hidden state. A responder's valuation of its own stock is private to the pass. |
| Validation | `TradeChecks` (fixtures, conservation, purity, replay, persistence) plus the five-world census below. |

## Rules

**Eligibility.** All of the following must hold:

- `TradeEnabled`;
- residents > 0;
- not player 0 (the human's camps never trade until a player panel exists);
- a goods economy (Cultivation or Husbandry);
- a Society with remembered knowledge (past its first breakthrough).

**Goods (T0).** Cloth and Housing Material are traded from committed stock.
Each camp values them on the planner's own curves (`ClaimValuation.NeedGoodTerms`):
marginal value `V e^{-k x / N}` with `k = ValueCurveSteepness`.

- **Cloth:** the position `x` counts Cloth plus Animal Fabric. `V` and `N` are
  the clothing claim's first-unit value and this turn's clothing requirement.
- **Housing Material:** one unit completes one Housing unit, so it is priced on
  the fixed-Housing curve:
  - `V` = people per unit × housing value per person;
  - position = shelter units + Housing Material held;
  - `N` = shelter units + this turn's Housing demand.

  A camp that cannot build (no Construction or no agriculture) values it at zero.

Integrated value over `[a, b]` is `V N / k (e^{-k a/N} − e^{-k b/N})`. A side's
gain from receiving `q` is the integral above its position. Its loss from giving
`q` is the integral below.

**Requisitions.** Each eligible camp picks its single best direction at 1:1
(want Cloth and give Housing Material, or the reverse). It requests the quantity
`q*` at which its marginal want value equals its marginal give value, bounded by
its own give stock, and only if the net gain at margin `m` is positive. It sends
that request to at most `K = TradeRequisitionsPerCamp` remembered partners. A
partner qualifies if it:

- belongs to another Society;
- is an eligible camp;
- is not known to have moved;
- lies within `TradeRange` hexes of the requester's tile (measured to the
  partner's recorded tile).

Nearest partners come first; ties go to the lower camp ID.

**Responses (the handshake).** Requests are processed in canonical order. For
each one:

1. The requested quantity is re-capped by:
   - the requester's best quantity at its *current* positions (earlier
     agreements this turn have already moved them);
   - the responder's stock.
2. The requester's acceptance limit `r_max` is the largest give-per-want ratio
   with `Gain(want) (1 − t) ≥ (1 + m) Loss(give)`. The transport loss is
   `t = TradeDistanceCostPerHex × distance`.
3. The responder's floor `r_min` is the smallest ratio with
   `Gain(give) (1 − t) ≥ (1 + m) Loss(want)`.
4. If `r_min ≤ 1`, the responder **honours** at 1:1 (or at `r_max` when that is
   below 1). If `1 < r_min ≤ r_max`, it **counters** at `r_min`; the counter is
   inside the requester's limit, so the requester closes it automatically.
   Otherwise the quantity is halved, at most three times, before the request is
   **ignored**.
5. A confirmed agreement updates both sides' positions immediately, so no stock
   is promised twice and later requests see diminished marginal values.

Kin margin: between parent and daughter Societies the margin is
`m (1 − Kinship.Affinity)`. Kinship changes terms only, never who is asked.

**Planning injection.** The plan is a pure function of the committed world,
independent of submitted orders. `Prepare` therefore reuses a matching draft's
plan, and draft intents remain valid. The planner and the prepared needs see
each trading camp with its post-trade Cloth and Housing Material (a private
inventory copy, `Camp.WithPrivateInventory`). This is the "confirmed trades
enter the fulfilment plan as fixed claims" interface of the plan document: the
handshake never calls into the fulfilment search.

**Settlement.** Agreements transfer from committed stock in agreement order. If
a side holds less than agreed, both sides of that agreement scale in proportion
and the record carries `Delivered < 1`. Settlement cannot normally fall short,
because the handshake never promises more than committed stock and nothing
consumes stock between planning and settlement. Settlement checks that each
good's world total is conserved and that no stock goes negative. Both Societies
record a `ContactSource.Trade` contact.

`TechnologyDiscovery.RecordTradePractice` is not called for T0 goods: Cloth and
Housing Material belong to no subsistence `Domain`. Its first caller will be
food trade (WP9).

## Persistence and compatibility

- **Config.** `TradeEnabled`, `TradeRange`, `TradeRequisitionsPerCamp`,
  `TradeMargin` and `TradeDistanceCostPerHex` are `OptionalField`. Saves without
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
- One direction per requester per turn, and barter only. There is no food (WP9)
  and no third good.
- The valuation is the camp's current-turn need curve. It does not anticipate
  next turn's production or the stock the planner will make this turn.
- Range is measured to the remembered tile, so stale-but-unmoved contacts can
  be out of date by at most the contact expiry.

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
equals `reference` (C1–C3 so far; the remaining worlds are recorded when the
run completes).

**Trade on: structurally inert for T0 goods.** On C1 and C2:

- trade-eligible camp-turns: 9,566 and 9,040;
- requisitions: 647 and 762;
- honoured or countered: none; **all were ignored**;
- trades settled: zero;
- outcomes identical to `off`;
- handshake cost: mean 0.9 ms per turn, maximum 9 ms.

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
