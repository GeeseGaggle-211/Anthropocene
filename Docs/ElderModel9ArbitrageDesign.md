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

### 0. Knowledge chain: finished goods before raw materials (review, 20 October 2026)

A society cannot want a good it does not know. Demand for a raw material therefore follows exposure to what is made
from it. The user's chain:
1. village C buys copper tools from B, which trades with A, which has the ore;
2. using them, C accumulates experience toward copper working;
3. once C learns it, C starts demanding raw copper to make its own.

Mechanics:
- **Two levels of knowing a good.** A good can be *recognised* (held, traded, valued for its use) or *workable* (made,
  or its raw input used).
  - Finished goods are recognised by any society that has received or seen them in trade, and are usable without the
    making technology. Today copper tools count only for societies holding CopperWorking; that rule changes to "to
    make", not "to use".
  - Raw goods are recognised by societies that can work them. Carriers recognise them from seeing them wanted or
    offered in their own requests and responses (section 1). A trader can carry ingots it cannot smelt.
- **Exposure toward the making technology.** Receiving and using a finished good adds knowledge exposure toward the
  technology that makes it (copper tools toward CopperWorking), in proportion to its realised use value.
  - This extends the existing trade-exposure hook (`TechnologyDiscovery.AddKnowledgeExposure` /
    `RecordTradePractice`), today limited to the three opening domains, to Neolithic and later technologies.
  - Discovery follows the existing threshold rules. Exposure without use (a good merely carried) counts less.
- **Then the raw demand appears.** Once C holds CopperWorking, raw copper has use value for C (its recipe), so it
  enters C's want lists. Partners see that in requests and record it (section 1). Carriers between C and the ore
  then gain a resale value for it (sections 2-3). Ore demand thus spreads outward from the frontier of copper-tool
  use, which is the diffusion the user described.

Validation adds a chain fixture (A ore, B smith, C tool-user, beyond A's range). It checks that C:
- uses B's tools;
- accumulates CopperWorking exposure;
- discovers it within a measured number of turns;
- then demands ore, which reaches it through carriers.

The no-exposure control never demands ore.

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

### 3. The valuation gains a demand-scaled resale band

```
W(x) = max(use(x), d(g) * r(g))   on [own target, own target + Q_resale)
```

Beyond its own need, a unit is worth its resale value, weighted by how strong and reliable the demand is, up to the
resale quantity.

**`d(g)` in [0, 1]** is an intermediate, hidden quantity recomputed every turn from market memory. It is neither a
configured constant nor a frozen weight (user review, 20 October 2026). It rises with:
- the quantity wanted relative to what the camp can carry;
- the number of distinct known buyers;
- freshness;
- the spread between resale price and own use.

It falls as markets go stale, as the camp's own deliveries fill observed wants, and as other known suppliers serve
the same buyers. Strong, recent, unserved demand pulls goods toward it; satisfied or stale demand stops pulling.

**It is in every village's valuation**, heuristic or Elder, so goods diffuse even in worlds the heuristic runs. This
is a deliberate economic change: native trade is re-measured and the teacher regenerated.

**What the Elder learns on top:**
- *where* to be: destination intents toward markets;
- *which* groups to carry: the gate, and relative value, which now scales the resale band as well as use value;
- *when* demand is worth acting on: it observes `d` and `r` in its trade rows.

Its arbitrage is therefore learned from demand signals, and can exceed the heuristic's, because the heuristic never
moves for a market. A "withhold" group is not resold.

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
- the demand strength `d`;
- the best known resale price;
- its quantity;
- the distance to that market and its age;
- the share of the group's inventory held beyond own need (stock bought to resell).

**Region rows** (contract 10) gain a known-market flag. A destination can be chosen because a buyer lies beyond it.

**The token model** reads them per trade token, so new metals are new rows. No new action head is needed: the
existing relative value, gate and movement carry it.

### 6. Reward

Realised utility is measured on the true valuation, which now includes the demand-scaled resale band. Demand and
prices are set by others, so a posture cannot inflate it.

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
- with no market memory (d = 0 everywhere) trade is exactly today's;
- `d` falls as observed wants are filled and as markets age;
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
