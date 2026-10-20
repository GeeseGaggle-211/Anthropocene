# Goods arbitrage: emergent resale demand (design)

Status: design, 20 October 2026. The user chose emergent arbitrage over an explicit merchant role: "the model will
organically learn that if there was demand from a village further away from the source's radius, then it can sell it
to that village." It is needed before copper and bronze, or a few deposit villages would hold all the metal.

## Why today's mechanics cannot do it

1. **Use value only.** Trade values a good only on the village's own use curve (plus a make cost). A village never
   wants a good it cannot use, so nobody buys to carry. Relay chains (A to B to C) fail for the same reason.
2. **The Elder's lever cannot create demand.** The contract-11 relative value scales the use curve, and 4 x 0 = 0.
3. **No price information.** Contact memory holds where a settlement is, not what it wants or pays.
4. **Reward.** Realised utility is measured on use value, so buying to resell always scores as a loss.

Reach is local (trade range 6, 3 partners per turn, 1% transport per hex) and camps move one tile per 20-year turn.
That allows carrying over several turns, but nothing motivates it.

## Design

### 1. Market memory (authoritative, saved; Society-level, beside ContactMemory)

Each society remembers, per contact settlement and trade group:
- the last observed **want** (the group appeared in that settlement's want list, in a request it sent or answered);
- the **terms** (units of the payment good per unit; realised ratio and quantity);
- the **turn observed**.

**Information rules.** Only what the society actually saw in its own requests and responses is recorded: the ranked
lists exchanged and the agreed or countered terms. A partner's stock, valuation and other trades stay hidden.

**Staleness.** Entries age like other contact snapshots, and a contact known to have moved drops its entries.

**Cost and order.** Size is bounded by K contacts x G groups per society. Updates happen once per turn at
`Trade.Finish`, in canonical (agreement, then camp) order, so they are deterministic and replayable.

### 2. Resale value (per camp, per good, from its own society's market memory)

`r(g)` = the best known net resale price of good g:
- the observed terms, converted to the camp's own value through its marginal value of the payment good;
- times `(1 - transport(distance))`, at the trade cost per hex;
- times a freshness decay with age.

It is capped in quantity by the observed want and by the camp's carrying capacity (section 4).

A market counts when the camp could reach it: within trade range of where the camp is now, or will be after its
current destination intent (contract 10). Demand beyond a source's radius thus becomes reachable by moving toward it,
which is the behaviour the user described.

### 3. The valuation gains a resale band, scaled by the Elder

```
W(x) = max(use(x), rho_k * r(g))   on [own target, own target + Q_resale)
```

Beyond its own need, a unit is worth its resale value, up to the resale quantity.

- `rho_k` in [0, 1] is a **new per-group Elder lever** (action schema 13): "how much to trade for resale".
- **The heuristic's `rho` is 0.** Native trade is unchanged and exact, and the teacher never demonstrates arbitrage.
  The learner must discover it, so the behaviour is emergent rather than cloned.
- Relative value (contract 11) still scales use value, and the gate still applies. A "withhold" group is not
  resold.

### 4. Carrying (who can hold goods in transit)

- **Mobile (non-village) camps.** Carrying capacity = people x a per-person load + pack animals x a per-animal load,
  from the herd's managed animals. Herders therefore carry more, which makes nomads the natural carriers through the
  mechanics, not a rule.
- **Pack animals** also lower the transport cost per hex and extend trade range (a configuration lever, measured
  before calibration).
- **Settled villages** relay within their storage (goods storage when enabled; unbounded today).
- Goods in transit are ordinary camp inventory: conserved, saved, and lost with the camp.

### 5. Observation and learner

**Trade rows** (contract 11) gain, per group:
- the best known resale price;
- its quantity;
- the distance to that market and its age;
- the share of the group's inventory held beyond own need (stock bought to resell).

**Region rows** (contract 10) gain a known-market flag. A destination can be chosen because a buyer lies beyond it.

**The token model** reads `rho` per trade token, as value and gate today, so new metals are new rows.

### 6. Reward

Realised utility is measured on the true valuation, which now includes the market resale band at `rho = 1`. The
market price is set by others, so a posture cannot inflate it.

- A merchant's purchase registers at its real opportunity value, not as a loss.
- A sale registers the gain over that value.
- Credit stays own-lineage (`trade_utility` component).

### 7. Validation

**Fixtures** (before metals exist, with today's goods):
- a producer A of a good (Cloth or Pots);
- a consumer C that needs it, at distance 8-12 from A (beyond A's range);
- a mobile herding society B able to reach both.

**Metrics** (learned against native):
- whether the good reaches C through B;
- the spread of holdings (Gini over settlements);
- B's and C's welfare;
- no starvation from labour diverted to carrying.

**Failure case:** without demand at C, B never hoards.

**Mechanics checks:**
- rho = 0 is exact;
- memory obeys the information rules (no hidden partner state);
- conservation in transit;
- replay.

Copper arrives later as new trade-good rows, with a curve key and source deposits. No new mechanism is needed.

### 8. Scaling

- Memory: O(contacts x groups) per society.
- Resale value: O(groups x known markets) per trading camp per turn, with no global scans.
- The handshake itself is unchanged in order.

## Schedule

After the current curriculum stages (token model, combined stage), and before copper and bronze. The rows and levers
ship as contract 13 on the token model, where they are new rows rather than new heads.
