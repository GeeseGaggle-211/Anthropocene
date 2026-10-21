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

## Implementation status (simulator side, 21 October 2026, branch feature/c13-market-memory)

The status is implemented behind `SimulationConfig.MarketMemoryEnabled`, default false. There is no Elder
observation, action or contract change, and the learner integration is round 4. Evidence is in
[Validation.md](Validation.md), "Contract 13 simulator side".

### Built (`MarketMemory.cs`, hooks in `Trade.cs`)

**Market memory (section 1).** `Society.Market` holds sorted (camp, good) entries:
- the last turn the settlement was seen wanting the good, from its want list in a request this Society answered, or
  from a good it accepted in payment;
- the last settled terms (quantity, price and payment good);
- the last turn it was seen offering the good, from offers a requester sees.

Information rules:
- Updates come only from this Society's own requests and responses, at `Trade.Finish`, in canonical order (offers,
  requests, agreements).
- No stock, valuation or third-party trade is ever read.
- Location always comes from the contact snapshot.

Entries are forgotten:
- after `ExpiryTurns` (6) without refresh;
- when the contact is forgotten, or known to have moved.

Freshness is 0.5^(age / `HalfLifeTurns` 2). A fission daughter copies its parent's memory, as it copies its contacts.

**Resale value and band (sections 2-3).** For every non-food curve key, the price is:

`r = max over reachable buyers of price x own marginal value of the payment good x (1 - transport) / (1 + margin) x terms freshness`

Reachable means within trade range of the camp, or of its contract-10 intent tile. The margin divisor keeps a
producer's floor at or below the price its own buyer paid, so prices cannot ratchet.

`d` is hidden and recomputed every turn:

`d = (1 - e^-S) x min(1, wanted / carry) x unserved share x spread`

- `S` sums buyer freshness divided by (1 + known rival suppliers within range of that buyer).
- `wanted` is the fresh, rival-shared observed quantity.
- The unserved share is (wanted - held beyond own need) / wanted.
- `spread` is (r - own use at the band end) / r.

The curve becomes `ResaleCurve`: max(use, d r) below own target + held + min(unserved, carrying room). It enters every
valuation, heuristic or Elder. Therefore:
- `TrueW`, and with it realised utility (section 6), includes it;
- the contract-11 relative value scales it;
- withheld goods are never resold.

**Carrying (section 4).**
- A mobile camp carries residents x `CarryPerPerson` (0.5) + managed animals x `CarryPerPackAnimal` (2) goods units.
- A settled village is unbounded (goods storage still bounds stored goods).
- Goods in transit are ordinary inventory.

**Knowledge chain (section 0), today's goods only.** A received made good adds craft work toward the track that makes
it (Cloth toward Weaving, Pots toward Pottery, and so on):
- the amount is its realised received value x `CraftExposurePerValue` (1);
- units beyond the receiver's own need count at `CarriedExposureWeight` (0.25);
- the existing `CraftMasteryThreshold` unlocks the technology and its specialist.

**Persistence.**
- The `ANTHROPOCENE-15` envelope (flags byte: history, growth, subsistence) carries a market extension with the
  levers and memories. It is written only with the flag on.
- Older envelopes load with the flag off.
- The configuration hash takes a market discriminator only when the flag is on.

**Flag off.** Byte-identical saves and fingerprints (see Validation).

### Deferred

- Pack animals lowering transport cost and extending trade range. The design calls for measurement before
  calibration, and changing the range changes partner selection.
- Copper and other metal goods, and the recognised/workable split for raw goods. Today's finished goods are already
  usable without their making technology. Housing Material, which a camp without Construction values at zero, is the
  only exception, and it is an intermediate.
- The Elder trade-row and region-row features of section 5 (contract 13 observation, round 4).
- The fixtures of section 7 that need the learner.

### Findings, and parts of the design I think are wrong or underspecified

1. **Offer-filtered want lists hide demand from non-holders.**
   - A requester lists only goods its partner offers, so a carrier sees a settlement's demand for a good only after
     holding some itself.
   - The A-to-B-to-C chain therefore starts only from a B that once held the good.
   - Raw-ore demand (section 0) will not reach a carrier that never held ore, unless requests also carry unmet wants.
2. **Barter terms equalise marginal rates.**
   - A carrier that *requests* from its buyer pays down to its own marginal rate, so the remembered price barely
     exceeds its own use and `d` stays near zero.
   - Arbitrage pays when the carrier is the responder, or when the buyer pays in a good the carrier keeps needing.
     The chain fixture needed exactly that (C pays in Housing Material, which B lacks).
   - The 1:1 honour rule sets most remembered prices, so `r` mostly reflects the payment good's marginal value, not
     scarcity.
3. **"Own need" is not a hard boundary on exponential curves.**
   - Their use tail stays positive, so a camp that lists a good because of its band buys along the use tail beyond
     the band end whenever its payment is nearly free.
   - In the fixture, B bought 100 cloth against a 17.7-unit band.
   - The same tail lets goods relay without market memory: B accepts cloth as payment, then sells it on. Flag-off
     worlds already move goods through intermediaries this way.
4. **`d` as a multiplier below 1 always discounts the resale price.** The band binds only where own use falls below
   d r, which is the intended margin. With one fresh buyer, d is at most about 0.5, so thin markets rarely pull.
5. **Self-referential pricing.** Without the margin divisor, a producer valuing its surplus at its own buyer's price
   would demand at least d(1+m)/(1-t) times the last price, an upward ratchet once d > 0.86. Dividing r by (1 + margin)
   removes it. The design should state the rule.

### 8. Scaling

- Memory: O(contacts x groups) per society.
- Resale value: O(groups x known markets) per trading camp per turn, with no global scans.
- The handshake itself is unchanged in order.

## Schedule

After the current curriculum stages (token model, combined stage), and before copper and bronze. The rows and levers
ship as contract 13 on the token model, where they are new rows rather than new heads.
