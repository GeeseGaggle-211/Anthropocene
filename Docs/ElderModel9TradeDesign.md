# model9 contract 11: Elder trade posture

Status: approved by the user on 20 October 2026, with self-interested credit (own lineage only) plus the
`trade_utility` critic component. Implemented the same day; see "Implementation" at the end. It follows the user's direction that the Elder
learn *when and how to offer goods and at what relative value*, and their warning that mining and service economies
starve unless trade is sufficiently rewarded (`ElderCapitalCommitmentsDesign.md`, Part 2).

## What exists

Trade (`Trade.cs`, `Docs/TradeHandoff.md`) is native and automated: one same-turn handshake per turn, AI to AI. Each
eligible camp:

1. **values** every good on the planner's own curves (`Trade.Value`): use value, a make-to-order cost, and a
   make-to-order capacity of `TradeExportLabourShare` (a configuration constant) of its residents' turn;
2. **lists** its wants (the most recoverable utility among goods a known partner offers) and its gives (the lowest
   marginal loss), W of each;
3. as a **responder**, accepts or counters at closed-form ratio bounds from those values, a margin and transport.

None of this can be steered by the Elder: the heuristic sets every input. The Elder's only influence is indirect,
through its economy mix and service levels.

## The decision to support

A village that can make Cloth cheaply, next to herders short of clothing, should learn to:
- offer Cloth;
- put labour into making more of it for trade;
- set its price high enough to gain from the trade, and low enough to be accepted.

A village whose own needs are tight should learn to withhold. Over time, trade should make specialisation pay. The
same levers later carry mining and service economies, whose output is worth little to the village itself.

## Proposed contract

### Authoritative state

None new. Trade postures are per-turn directive fields, like service levels. A posture that should persist is a
capital commitment (contract 9), not new trade state. Agreements still complete or lapse within the turn.

### Action (per village per turn; trade groups follow the value-curve keys)

The groups are clothing, housing, light housing, pots, tools, materials and food.

1. **Relative value** `a_k` for each group, on a log scale in [1/4, 4]. It multiplies the village's marginal values
   on that curve, which feed want scores, give scores and both ratio bounds.
   - Above 1: the village holds out for more and wants more.
   - Below 1: it parts with the good cheaply.
   - Food's protected band is never scaled: a village cannot sell its consumption or reserve.
2. **Offer gate** for each group: withhold, offer from stock, or offer and make to order.
3. **Export labour share** `e` in [0, e_max]: the share of the residents' turn available for made-to-order exports.
   It replaces the configuration constant for this village; e_max is the configuration's legal ceiling. This is
   "how much to produce for trade", i.e. specialisation.

**The heuristic is exactly representable:** a_k = 1, every gate set to "offer and make", and e = the configuration
value. Teacher labels are therefore exact, and the new heads start at native (zero weights with a native bias, as
contract 10's movement head), so there is no re-cloning.

### Observation

Today's observation already has:
- commodity rows (stock, need, capacity, recent imports and exports);
- contact rows (trade flow and realised trade utility per partner);
- a realised-trade-utility memory trace.

Added, per trade group:
- last turn's realised terms (ratio paid or received);
- the realised gain;
- whether any known partner offered it;
- the village's own make-to-order cost.

Another camp's private values, stock and orders stay hidden, as today: a request carries ranked lists only.

### Learner

- Heads: a Gaussian on log a_k per group, a categorical gate per group, and a logistic-normal export share.
- They are anchored to the teacher like the other capital heads. Under the looser anchor (0.01) the guard below
  applies.
- They are capital controls, so a commitment holds them.

### Reward credit (decision for review)

Trade gains reach welfare only through what the goods do for the village: clothing coverage, shelter, tools. Two
problems follow.

1. **Specialist economies.** A village whose output it cannot use (stone, later ore and services) earns welfare only
   from what it buys. That is correct, but slow to learn: the credit arrives a step removed.
2. **The partner's gain.** With lineage reward, a village is credited for its partner's gain only when the partner
   shares its lineage.

Proposals:
- **(a)** Add a `trade_utility` component to the vector critic: realised trade utility per resident-year, from
  `TradeRecord`'s realised utilities. It enters the advantage at a small weight to speed credit, and is reported
  always.
- **(b)** Decide whether a village's reward includes its partners' realised gain:
  - none: self-interested trade, today's lineage rule;
  - a share: cooperative trade between kin, or between everyone.

  This is a game-design choice. Self-interested trade with acceptance on both sides is still mutually beneficial per
  trade. Shared credit makes villages favour trades that help partners more than themselves.

### Temporal abstraction and scaling

- The same single handshake per turn.
- Postures scale curve values and the capacity bound inside `Trade.Value`. That is O(goods) per village per turn,
  with no new searches and no change to the handshake's `O(camps (G log W + contacts)) + O(camps K W^2)`.
- Directed villages pass their postures to `Trade.Plan` through `TurnPlanner.PrepareWithElderDirectives`. Plans stay
  pure and canonical, and replay carries the directives.

### Validation

- **Exactness:** native postures reproduce today's trades and fingerprints bit for bit.
- **Monotonicity:**
  - raising a_k never makes a village give more of k at a lower ratio;
  - "withhold" never gives;
  - e = 0 makes nothing to order.
- **Conservation and replay:** as `TradeChecks`.
- **Curriculum:** trade fixtures from scanned openings, with complementary neighbours (a cloth maker within reach of
  herders short of clothing), measured on joint welfare and on trade volume against native.
- **Gate:** default worlds within the usual tolerance, plus trade fixtures above native, plus the no-collapse guard.

### The no-collapse guard (applies to every looser-anchor stage)

At arm level, with intervals pooled over seeds:
1. extinctions not significantly above the comparison (E3 rule);
2. no regime more than 3% below the reference;
3. default-world QoL no more than 0.03 below the reference (this catches E2's population-for-QoL drift);
4. the share of settlements below 30 residents not significantly above the reference.

### Rejected

- **Per-partner, per-trade actions** (choosing whom to trade with and at what price each turn): an unbounded action,
  information about partners the village does not have, and a planner rewrite.
- **Price shading without an offer gate:** "never sell X" then needs an extreme a_k, which is a hard target for a
  Gaussian head.
- **Reward shaping on trade volume:** it rewards trading, not the gains from trade.

## Decisions (user, 20 October 2026)

1. The three levers and the seven groups are approved.
2. Credit: (a) the `trade_utility` component is approved; (b) partners' gains are not credited (self-interested, own
   lineage only).
3. The no-collapse guard: the user accepts the looser anchor provided nothing collapses.

## Implementation (20 October 2026)

**Simulator** (27ff141):
- `ElderTrade.cs` holds the posture, groups, clamping and repair, the observation rows, and realised utility.
- `Trade.Valuation` gains `ValueScale` and `Withheld`:
  - `W` scales use value (cost unchanged, so `Crossing` uses c/a);
  - `TrueW` is the unscaled curve. Realised utility (`TradeUtilitySnapshot`) is measured on it, so a posture cannot
    inflate its own reward.
- `Trade.Value`/`Plan` take postures; `MakeToOrder` takes the village's export share.
- `TurnPlanner` passes the postures of directed villages and skips the draft plan when any posture exists.
- Action schema 11 adds `TradeValue`/`TradeGate`/`TradeExportShare`. `From` clamps exactly as the compiler's `Repair`,
  which logs the repair.
- The `trade` observation set has 7 rows x 10 features (`ElderModel9Encoder.TradeFeatureNames`). Rows come from the
  village's own native valuation and last turn's trades.
- Villages report `TradeUtility` per step. Teacher labels carry the native posture.

**Learner** (RL):
- heads: Gaussian log value per group, a categorical gate per group, and a logistic export share. They start native:
  value 1, Make at 0.8, and a share that snaps to 0 at evaluation;
- `--learned-trade` treats the posture as a capital control anchored to the teacher, turns on the vector critic, and
  sets the `trade_utility` weight to `contracts.TRADE_UTILITY_WEIGHT` = 0.1 (`data.reward_components`: the village's
  own utility, not pooled by lineage);
- exact upgrade from contract 10 (zero trunk columns for the new set; a zero `trade_utility` head on saved vector
  critics).

**Evidence:**
- 112 model9 assertions:
  - a native posture reproduces the handshake exactly;
  - withholding gives nothing;
  - a zero share makes nothing to order;
  - relative values scale `W`, never `TrueW`;
  - utility reporting is correct;
  - schema-10 directives and repairs behave as specified.
- The fixed suite (486 trade assertions) and 37 learner tests pass.
- An upgraded contract-9 policy reproduces the decisive experiment's per-world welfare bit for bit, with learned
  trade off and with learned trade at its native start (DEVELOPMENT 2000 and 2001; 80-86 trading village-turns per
  25-turn world).

**Not yet done:**
- trade fixtures (complementary neighbours);
- commitments holding the posture: commitments are off;
- the planner counting imports as supply (roadmap).
