# Value model for planner fulfilment (draft)

Status: design agreed for implementation, 29 September 2026; not implemented.
The ordered work is in [ValueModelImplementationPlan.md](ValueModelImplementationPlan.md).
Its social companion, which sets the QoL formula, fission, food trade and
proto-towns, is [QoLFissionTradeDesign.md](QoLFissionTradeDesign.md); where the
two differ, that document governs QoL and food. This document continues
[ValueFulfilmentHandover.md](ValueFulfilmentHandover.md) and the "Decisions"
section of [PlannerDirectFulfilmentPlan.md](PlannerDirectFulfilmentPlan.md).
`EconomicAutomation.md` and `TurnResolutionPasses.md` remain the rule sources.

## The two questions

A value rule has to answer, for every good X a camp can make:

1. **What is X worth, given that it costs labour Y?** Value per unit divided by
   the full labour cost per unit gives value per worker.
2. **Could labour Y do more good elsewhere?** That is opportunity cost. The
   answer is one number per camp and turn, the **shadow price of labour `w`**:
   the value the last assigned worker adds in their best use.

At the optimum every good that is produced returns the same value per worker at
the margin, equal to `w` (the equimarginal rule). A good whose *first* unit
returns less than `w` is not made. So the planner does not need to rank goods
pairwise. It needs value curves, labour costs, and a search for `w`.

## Demand, supply and price

Demand and supply curves are not inherently linear; linear is the textbook
first-order approximation. What defines the price is where the **marginal
value** of one more unit (demand) meets its **marginal cost** (supply):

- **Demand** comes from the value curve below.
- **Supply cost** of one more unit of X is `w · ℓ_X`, where `ℓ_X` is the full
  labour per unit. It rises when labour is scarce because `w` rises, even though
  each recipe has a constant rate.
- The **local price** of X is where they meet.

### Value curve

The value of one more unit of X falls exponentially with the coverage of the
need it serves:

```
v_X(c) = V_X · exp(−λ_X · c),   c = (stock after decay + planned output) / D_X
```

- `D_X` is the need level: cloth for the cohort's turn, shelter for the
  cohort, the reserve gap for Pots. Normalizing by `D_X` makes the curve the
  same shape for a 30-person camp and a 300-person village.
- `V_X` is the value of the first unit (see "Anchoring V in QoL").
- `λ_X` sets how quickly value falls. At `λ = 3`, the last unit that completes
  the need is worth `e⁻³ ≈ 5%` of the first.
- The curve applies for `0 ≤ c ≤ 1`. Beyond full coverage, local-use value is
  **zero**: surplus is worth something only through an external use such as a
  confirmed trade claim.

Why exponential rather than linear: it gives value that rises smoothly with
scarcity instead of filling the cheaper need completely before touching the
other, and its inverse is cheap:

```
coverage the camp buys at labour price w:  c*(w) = (1/λ) · ln(V / (w · ℓ))
```

## Anchoring V in QoL

Use one unit throughout: the **person-QoL-year**, raising one person's QoL by
1.0 for one year. Values are stated in physical years so the same functions
apply at any turn length; the default 20-year turn simply evaluates them over
20 years. The QoL formula (from
[QoLFissionTradeDesign.md](QoLFissionTradeDesign.md), sections 2.1–2.4) is
`Q = B · R · S(N)`: survival `B = F · W`, rudimentary needs
`R = ½·clothing + ½·housing` with `clothing = 1 − E·(1 − coverage)`, and the
social cohesion multiplier `S(N) = exp(−(N / N_s)^β)`. QoL stays linear in each
need's coverage, so the constant marginal QoL of a unit of each need is:

| Good | First-unit value `V` (person-QoL-years) | Why |
| --- | --- | --- |
| Cloth, Animal Fabric | `½ · E · B · S · 20` | `FabricConsumptionPerPersonTurn = 1` per 20-year reference turn, so one unit clothes one person for 20 years. |
| Housing, Light Housing | `4 · ½ · E · B · S · L` | One unit shelters four people for its discounted service life `L` in years. |

`S` is fixed within one plan because the prior cohort's population is fixed at
preparation. A crowded settlement therefore values goods somewhat less, by the
factor `S`.

Housing decays 10% per 20-year reference turn, a continuous rate
`k = −ln(0.9) / 20 ≈ 0.0053` per year (mean life about 190 years). With a
discount rate `r` per year, the discounted service life is `L = 1 / (k + r)`:
190 years at `r = 0`, 61 years at `r = 0.011` (equivalent to a factor of 0.8
per 20 years). The discount rate is a real design lever (the camp's time
preference), and a natural Elder personality or RL parameter. Baseline 0.01
per year, swept over 0–0.02.

`B = F · W` (food and water met) multiplies every craft value. That is the
Amdahl effect already in QoL: while food is short, every craft is worth less
and labour belongs on food. Food and water stay a reserved floor, as today.

### Worked values at current calibration

Recipe rates are configured per `BaseDays` (91.25 days), a legacy rate unit
that the turn horizon converts once; below they are restated per worker-year.
With `E = F = W = 1` and specialist rates:

| Work | Output per worker-year | Value per worker-year (person-QoL-years) |
| --- | --- | --- |
| Weaver on Cloth | 4 units, clothing 4 people for 20 years | 40 (plus 4 Plant Food input) |
| Builder on Housing (`r = 0`) | 0.125 units, sheltering 0.5 people | 48 |
| Builder on Housing (`r = 0.011`) | 0.125 units | 15 |
| Generalist Craftsperson | 0.25 × the above | 10 / 12 / 3.8 |

Cloth and Housing are of the same order, so under this model Housing is
**not** inherently poor value. Its failure in the #33 windows (Construction
productive in 4 of 21) is therefore more likely due to its schedule position,
since it is built last from leftover Wood and Clay, or to capability and
useful-demand gates. A value rule should also order material access by value,
not only change shares.

Also note that one Weaver keeps about 80 people clothed and one Builder
replaces the housing decay of about 100 people. Crafts rarely bind the
resident workforce; food, fields and herding do. In practice `w` will mostly
be set by food-side and field labour. That raises open question 4.

## Tools price themselves through `w`

A labour-saving tool is worth the labour it saves, valued at `w`. Its cost is
also labour, valued at `w`. So `w` cancels:

> A tool is worth making whenever it saves more labour over its life than it
> takes to make, provided labour is scarce (`w > 0`).

| Tool | Saves per unit | Costs per unit (specialist / generalist) | Ratio |
| --- | --- | --- | --- |
| Farm Tools (1.265625× field labour, 20 farmer-years) | 21% of 20 = 4.2 worker-years | 0.5 / 2 worker-years | 8.4 / 2.1 |
| Herding Tools (2 → 2.69 animals per Herder, 20 herder-years) | 25.7% of 20 = 5.1 worker-years | 0.75 / 3 worker-years | 6.9 / 1.7 |

Costs include making the tool and extracting its Wood (and Stone) inputs.

In value per worker these claims return 2–8× `w`, so a value fill takes them
first, up to their useful demand. Beyond useful demand they save nothing (idle
tools neither wear nor help), so they stop there. This is the precise meaning
of "tools matter when labour is scarce". When labour is slack (`w = 0`), they
are made only to demand, as now. It also suggests Carpentry's 0/21 in #33 is
not a valuation failure; its cause is more likely upstream (capability,
material access or zero forecast demand) and should be diagnosed separately.

Hand and Regular Tools are *coverage* inputs to equipment recipes, not labour
savers. Their value is derived: the value of the output they enable, less that
output's other costs. They can remain sized by `ToolReplacementDemands` from
the chosen outputs, as today.

**Pots** protect future food. A proper value needs an expected-shortage
estimate, which does not exist yet. Agreed placeholder: treat the reserve gap
as a need with coverage `storage / desired reserve`, valued through food
security (below) and scaled by a configured risk weight `ρ`. Natural disasters
and immigration are planned; both create real shortfalls (lost harvests,
sudden extra mouths) that Pots buffer. When they exist, `ρ` is replaced by
their expected shortfall. Only the probability term changes, not the method.

**Food security** is the common basis for Pots and field expansion. Food met
`F` multiplies every other QoL term, so one unit of food shortfall avoided is
worth the QoL it protects:

```
value of avoided shortfall per food unit ≈ (QoL / F) · residents / food demand
```

in person-QoL-years. An expansion farmer prevents a *forecast* shortfall as the
cohort grows; a Pot prevents a *risk-weighted* one. Both use the same
formula, so neither needs its own tuning.

### Revised WP4 design (30 September 2026, after the WP4a regression)

**What went wrong.** WP4a priced saved labour at `w0`, the start-of-turn
*craft* labour price (best need good's `V·e^(−λc)/ℓ`, about 6.4 person-QoL-years
per worker-year). Farm Tools save *farmer* labour, which is slack (tens of
thousands of farmers; leftovers go to expansion). At that price a Farm Tool was
worth 26.9 per unit against Cloth's 2.5, went first in the production order,
took Wood and tool coverage, and cut mean QoL by 28–32%. WP4a was reverted
(`10dbc75`). **Rule: price saved labour at the marginal value of the labour it
actually frees, never at another labour pool's price.**

**Corrected order: WP4b (expansion) first, then tools and Pots.**

*Food value per unit (person-QoL-years per food unit per year of supply).*
One extra food unit per year covers `1/D_year` of the cohort's annual need
(`D_year = N · Consumption · DaysPerYear / BaseDays`). Because `F` multiplies
all of QoL, that protects `N · Q / (F · D_year)`:

```
v_food = N · Q / (F · D_year)        (F guarded ≥ 1e-6; Q, F from the committed camp)
```

*WP4b — expansion labour (second design: step ladder, bucketed food value).*
The first design (a single `g` and need `shortfall / g`) failed its gate: `g`
is 0.01-0.1 food/yr per worker-year, so the need was 2-6x the whole camp; and
in slack camps leftover residents already exhaust the allocator's positive
steps, so a further expansion worker's marginal product is zero, not `g`. Food
beyond consumption and reserve only funds births, which the average food value
overstates. The redesign values what the allocator would actually do:
- **Step ladder.** `CultivationEconomy.ExpansionLadder` runs the resolver's own
  greedy farmer allocation (same candidates, decay, operator guards, step
  selection; `NextStep` and `AllocateHorizonEffort` are shared) from the
  start-of-turn committed state with unbounded effort and records each step:
  build effort `B_i` in worker-years (what the allocator charges),
  score `g_i` and annual retained food `dF_i` from the same
  `FoodHorizonForecast.Investment`. It stops when no step has a positive score,
  or (for the planner) once the cumulative `dF` reaches the total food gap.
  Farm Tool efficiency and the bridge pass are ignored: effort is worker-years.
- **Food buckets** (per year, from `TurnResolver.FoodBucketTargets` at the
  planning population): `gapCons = max(0, consumption - supply)`,
  `gapSec = max(0, consumption + reserveFill - supply)`;
  `gapTotal = max(0, consumption + reserveFill + maxGrowthFood + clothFeedstock - supply)`
  (the existing forecast gap; the existing gate is kept).
- **Values per food unit** (person-QoL-years): consumption `v_food = N Q / (F D_year)`;
  reserve refill `ρ · v_food` (`FoodRiskWeight` = 0.1, the risk placeholder
  shared with Pots: reserve food raises no QoL until shocks exist);
  growth `v_growth = max(0, dW/dN) · L_person / food_per_birth`, with
  `W(N) = N Q(N)`, `Q(N) = F · Wtr · S(N) · sum_k w_k (1 - E (1 - min(1, c_k N0 / N)))`
  for clothing and housing at their committed coverages `c_k` (goods dilution,
  friction and cohesion included), `dW/dN = W(N0+1) - W(N0)`;
  `food_per_birth = C_turn (x e^x - e^x + 1) / (x^2 e^x)` with
  `x = maxRate · years`, the derivative of `GrowthFood` with respect to final
  population (limit `0.5 C_turn`; no growth bucket when `maxRate = 0`);
  `L_person = 1 / (1 / PersonLifeYears + r)`, `PersonLifeYears = 30`. The
  design's labour term (+w per new worker) is omitted: it is circular with the
  price being set.
- **Ladder claim.** Walk the ladder with cumulative annual food `Fc`; step `i`'s
  food `[Fc, Fc + dF_i]` is valued piecewise (consumption at `v_food`, reserve at `ρ v_food`, then
  growth at `v_growth`, nothing beyond `gapTotal`), giving the average `v_i`;
  value per build worker-year `u_i = g_i · L_cap · v_i`, with `L_cap =
  1 / (ln 2 / DecayHalfLifeYears + r)`; truncated once `Fc >= gapTotal`, and
  made nonincreasing by a running minimum. The ladder is the marginal-value
  curve of expansion, replacing the exponential curve for this claim.
- **Search.** At labour price `w(s)` expansion buys the build worker-years of the
  ladder prefix with `u_i >= w` (0 at `s <= 0`); staffed workers for the turn are
  `ceil(worker-years / years)`, at most the field workers left. The ladder can
  only raise the path's top, never lower its end (only with no craft claim does
  it own both ends). Workers left after the search still go to expansion exactly
  as today, so an economy with slack labour behaves as before; only labour-scarce
  camps trade affordable ladder steps against crafts. The search is two-stage
  (craft requirement first, then the full requirement above the price of the
  first ladder step): interpolating on a total that the ladder inflates to many
  times the residents stalled near `s = 0` and cut crafts expansion never
  staffed (a −3.8% to −4.6% QoL artefact in the gate).

*Farm-labour price.* After the search, `w_farm` is the `u` of the first ladder
step that the plan's total expansion worker-years (`ExpansionFarmers · years`,
staffed plus leftover) do not cover: the value of one freed farmer-year. 0 when
the ladder is exhausted or no claim exists. (The demand pass cannot know the
coming plan; WP4a order keys need an estimate from last turn's saved
`EconomicLabor.ExpansionFarmers`, which the camp carries.)

*WP4a (redo) — tools and Pots.*
- Farm Tools: `V = savedFT · w_farm`; Herding Tools: herder labour is also
  field labour whose freed years go to the same margin, so
  `V = savedHT · w_farm`. With no food shortfall, `w_farm = 0` and labour-saving
  tools are made only to demand at the tail of the order, as before WP4a.
- Order keys use these per-unit values; tools that consume coverage rank
  below need goods unless their value per unit is genuinely higher.
- Pots: food security with the risk placeholder `ρ`:
  `V_Pot = ρ · PotStorageFoodPerUnit · v_food · L_pot`, `L_pot` from Pot decay and `r`.

*Gate for each step.* Checkpoints under `--fulfilment=Value` against the
previous Value runs: mean QoL must not fall; population and food no bias beyond
the control; per-camp clothing, housing, tool output and managed animals
reported; #33 windows rerun.

## Fulfilment rule: search on `w`

This fits the existing interface with no change to `SearchPath`.

For each value claim g (useful quantity `Q_g`, stock `S_g`, labour per unit
`ℓ_g`, curve `V_g, λ_g, D_g`):

```
output_g(w) = clamp(c*_g(w) · D_g − S_g, 0, Q_g)
share_g(w)  = output_g(w) / Q_g
```

Map the path position to the price on a log scale:

```
w(s) = w_hi · (w_lo / w_hi)^s
w_hi = max_g V_g / ℓ_g          (nothing is worth making: all shares 0 at s = 0)
w_lo = min_g v_g(full) / ℓ_g    (everything reaches useful demand at s = 1)
```

Shares never decrease as `s` rises, which `IFulfilmentRule` requires. The
existing search finds the largest feasible `s`, meaning the **lowest labour price
the residents can staff**. That price is the shadow price of labour, so the
solve reports it for free (transient, never saved). The exact staffing pass
(`Evaluate`) remains the authority on materials, food envelopes, tool coverage
and integer rounding. `ℓ_g` in the curve is only the closed-form estimate used
to shape the shares: craft labour plus input extraction plus input food
gathering.

Output is capped at useful demand `Q_g` for local claims, preserving the rule
that nothing is stockpiled for its own sake or for research. Only export
claims (below) justify production beyond local need.

**Player and Elder priorities** become multipliers on `V`:
`priority / default priority`. Defaults are then neutral, zero still disables a
good, and no save field changes. Priorities stay the player's and the Elder's
lever, now with a defined meaning ("value this 2× as much").

Determinism: claims in canonical good order; `w` is a pure function of the
demand pass; ties are broken by good ID.

## Requisitions: how neighbours tilt the curve

A requisition is a neighbour's request for R units of X, offering something in
return. It adds **external demand** to the camp's own demand curve.

### The tilt

At any price p, the camp's total demand for X is its own demand plus every bid
worth at least p:

```
Q_total(p) = (D/λ) · ln(V / p)  +  Σ { R_b : bid b worth ≥ p after transport }
```

Graphically, the local curve decays; each bid adds a flat step at its price,
and the combined schedule is the local curve with those steps inserted. The
consequences are:

1. **The value of X rises to the best open bid.** A camp will not consume a
   unit locally that is worth less than what a neighbour pays. X's reservation
   price becomes `max(local marginal value, best open bid)`. This is the
   "value by best use" rule from the handover, now with a concrete mechanism.
2. **Surplus production becomes worthwhile.** Past local coverage the local
   value is zero, but the bid step holds value up at the bid price for R units. The
   camp produces for export while `bid value ≥ w · (ℓ_X + transport)`.
3. **Every other good is tilted too.** Export work uses labour, so `w` rises and
   the camp makes a little less of every other good. The bid shifts the camp's
   whole allocation, not only X.

An export claim therefore enters the demand pass as an ordinary claim with a
flat value (the bid) and quantity R. The rule above handles it unchanged: its
share is 1 whenever `w` is below the bid value divided by its labour.

### Bids: barter now, currency later

There is no currency yet, but Anthropocene is meant to reach the modern era, so
bids must not hard-code barter. Represent every bid as two bundles:

```
Bid { Give: [(good, quantity)], Receive: [(good, quantity)] }
```

Barter is a bid whose bundles contain ordinary goods. Currency, when it
arrives, is one more good in the same bundles: it has no consumption use, and
its value to a camp is its **exchange value**, the best use of what it buys.
That is the "best use" rule already in this model. A posted currency price is
then just a bid with currency on one side, and a camp's local marginal value
divided by its marginal value of currency is its reservation price in
currency. Nothing in the valuation, the claims or the fulfilment rule changes
when money is added; only the set of goods and a later market mechanism do.

To keep that path open:

- keep values in each camp's own unit (person-QoL-years); never compare two
  camps' values directly, only goods exchanged;
- keep bids as bundles, never as fixed bilateral pairs;
- record accepted exchange ratios in the trade ledger, so later markets have a
  price history to start from.

Each camp judges a bid on its own curves, so no common value scale across
camps is needed. A bundle that materially changes coverage is valued by the
**area under the curve**, not at one marginal point:

```
ΔU_X(c0 → c1) = (V_X · D_X / λ_X) · [exp(−λ_X · c0) − exp(−λ_X · c1)]
```

Received goods integrate upward from current coverage; goods surrendered from
local useful stock integrate the area lost.

- **Buyer B** offers while the integrated value of the X it receives exceeds
  the integrated value of the Y it gives up.
- **Seller A** accepts while the integrated value of the Y received is at
  least the cost of the X given up: its integrated local value if taken from
  stock inside useful coverage, or `w_A · ℓ_X` plus material and transport
  costs if newly made.
- Trade happens when their ratios of marginal values (marginal rates of
  substitution) differ. The exchange ratio lies between them; who captures the
  surplus is a design choice (seller's ask, buyer's bid or midpoint).

### Housing Material: a portable intermediate

Fixed Housing belongs to its construction site and cannot move. The tradeable
good is therefore the material, not the house. Agreed with the user on
29 September 2026. Recipe split implemented as WP5a (labour, tool wear and material per Housing unchanged); the access-order change is WP5b (implemented; see EconomicAutomation.md):

```
Wood / Clay  ->  Housing Material  ->  Housing (fixed, built on site)
(extraction)     (portable, stored)     (Builder assembly)
```

Today the Housing recipe already consumes one construction-material unit per
Housing unit (`HousingMaterialUnitsPerHousing = 1`), filled from Wood and Clay
by `MaterialEconomy.HousingShares` and debited at the moment of building. The
change makes that middle step a stored good, mirroring the pastoral chain
Animal Product -> Animal Fabric -> Light Housing.

Agreed defaults:

- **Labour is split 50/50, not added.** The current Builder labour per Housing
  unit is divided equally between making Housing Material and assembling it,
  so each step runs at twice the current Housing rate (0.25 units per
  Builder-year instead of 0.125). Labour per Housing unit and the shipped
  balance stay the same. The split is a calibration target for later.
- **Builders and generalists only.** Builders (Construction) make and assemble
  Housing Material; Craftspersons do both at the usual `GeneralistTaskEfficiency`
  (0.25). No new role, to keep the supply chain simple.
- **No decay.** Stored Housing Material does not decay for now; holdings are
  already capped by useful demand. Also a later calibration target.
- **No stockpiling beyond useful demand.** Housing Material is made only
  against the Housing gap, as other goods are. Housing demand therefore
  remains a recurring pressure as population grows, which is intended. The
  only exception, when trade exists, is an accepted export claim.
- **The Wood:Clay mix is fixed when material is made.** Housing Material
  becomes an ordinary recipe with direct inputs, and Housing consumes it as
  Light Housing consumes Animal Fabric. This removes construction's special
  position as the final flexible consumer of leftover Wood and Clay (removed by WP5b), which may
  be behind Construction's 4-of-21 result in #33.
- **Persistence.** A new optional `CampInventory.HousingMaterial` field in the
  current `ANTHROPOCENE-11` envelope (absent means zero), and an owned-inventory
  field in observations (schema 15 -> 16). Foreign stores stay private.
- **Transport weight is out of scope here.** Its cost per unit belongs to
  logistics (#13) and will be designed separately.

Value: Housing Material takes a derived value, the value of the Housing it
completes minus the assembly labour at `w`. It needs no separate curve or
tuning. A neighbour's request for housing is a request for Housing Material;
the buyer spends its own builder labour on site. Sending builders to a
neighbour's site remains a separate labour-service option for later.

### Handshake: requisition and acknowledgement

Agreed: trade terms follow a req/ack handshake with at most one counter.

```
REQ    A -> B : "I want X; I offer Y" (plus A's acceptance limit)
ACK    B -> A : honour | counter | ignore
CLOSE  A -> B : accept counter | reject
```

- **What to request.** A requests its highest-value good: the one it is
  scarcest in, where its value curve is steepest.
- **What to offer.** A offers its lowest-value good *among goods B has
  requested before*, because B's past requisitions reveal what B is short of.
  With no history, A offers its lowest-value good. This avoids offering what
  both sides already have in surplus.
- **Honour.** B honours when what it receives is worth at least what it gives
  up: its local marginal value if taken from stock, or `w_B · ℓ_X` if it must
  make it.
- **Counter.** B may change the quantity or swap the good: "I'll give X, but I
  want Z", where Z is B's own highest-value good. Counters are how preferences
  come out without anyone reading another camp's stores. B counters at its own
  break-even terms plus a margin `m`.
- **Ignore.** B has nothing to spare beyond its own needs, or nothing it wants
  in exchange. Ignoring reveals nothing.
- **Close.** A accepts the counter if it still gains (within its acceptance
  limit) and rejects otherwise. A confirmed trade is binding.

Neither side ever sees the other's values, only the terms. `m` is a configured
bargaining margin with a hook for the Elder, like the discount rate. For kin
Societies the margin falls to `m · (1 − a)`, and surplus may go for nothing
(see below).

**Acceptance limit.** A REQ carries the worst terms A would accept, so A's
close step is automatic. This lets a human requester complete a trade in one
turn without a second decision.

**Bounds and determinism:**

- at most `K` requisitions per camp per turn (for example 1–3 goods);
- a responder handles the REQs it received in order of value to itself, ties
  broken by camp ID and good;
- one handshake per pair of camps per good per turn; a rejected REQ may be
  reissued next turn;
- every step uses values computed from the committed boundary, not values
  updated by trades confirmed earlier in the same turn. A responder only
  reduces its own spare capacity as it confirms. So a change to one handshake
  never cascades beyond that responder;
- confirmed terms are recorded in the trade ledger, giving later markets and
  currency a price history.

**The trade ledger: every handshake finishes in its turn.** Agreed: nothing
is carried across the turn transition. At 20-year turns a request answered a
turn later would be 20 years stale: the sender's needs have changed, offered
goods may be spent, and the sender may have moved or starved.

1. **While the player plans, they see the complete ledger for this turn.**
   The background draft computes every neighbour's requisitions from the
   committed boundary, so the ledger shows the requisitions addressed to the
   player, each with what it offers and honour, counter or ignore, alongside
   the player's own trade history and each partner's past requisitions.
2. **The player decides everything in their orders:** answers to incoming
   requisitions, plus any requisitions of their own, each with its acceptance
   limit.
3. **At End Turn the AI side finishes each handshake during preparation.** An
   AI whose requisition the player countered accepts or rejects at once. An AI
   receiving the player's requisition honours, counters or ignores it; a
   counter within the player's acceptance limit is accepted automatically, and
   one outside it **lapses**.
4. **Goods move in this turn's resolution.** The next turn's ledger reports
   what happened: confirmed trades with their terms, and requisitions that
   lapsed, were ignored or went unanswered. The player can re-request on fresh
   values.

AI camps follow the same rule: any handshake that does not finish within the
turn lapses. Rules:

- **No saved pending state.** Only the trade ledger's history (confirmed,
  lapsed and ignored items) is saved. Every trade is judged on the current
  boundary's values.
- **Unanswered means lapsed.** A requisition the player did not answer by End
  Turn lapses, as if ignored.
- **The resolver never waits for input.** Decisions arrive only as submitted
  orders or AI drafts.
- **Filtered observations.** The ledger shows only requisitions addressed to
  the camp and its own history, never other camps' trades, stores or values.

Trade-off: a counter just outside the player's acceptance limit lapses instead
of coming back to them. The limit is the player's own decision, and they can
re-request next turn. If this proves too strict in play, an optional setting
could hold the player's own requisitions for one turn to review counters.

Sending a REQ reveals scarcity. That is a deliberate strategic cost, and an
Elder may hold requests back for it.

Goods are exchanged in one canonical step during resolution, after
production, conserving both sides. If production falls short, both sides of
the trade scale down in proportion.

### Scheduling: plan during the player's turn

A player's thinking time can hide the planner's and the handshake's latency.
Today `AiPlanning.Decide` and `TurnPlanner.Prepare`, which calls
`EconomicPlanner.Plan` for every automated camp, run only after the player
presses End Turn (`OpeningGame.EndTurn`), so the player waits for all of it.

Both are pure functions of the committed boundary, and `Decide` already
excludes the human player (`excludedPlayer = 0`). So as soon as a turn is
committed, a background task can compute:

1. every AI camp's decision (movement, dispatch, fishing);
2. every AI camp's REQs, AI-to-AI acknowledgements and closes;
3. every AI camp's automated economic plan, with its confirmed trades;
4. the REQs addressed to the player, shown during planning so the player can
   honour, counter or ignore them in the same turn's orders.

At End Turn, only the parts that depend on the player's orders are
recomputed:

- the player's own camps;
- AI camps whose effective destination changes, because
  `CampPlacement.Resolve` settles the player's moves against theirs;
- AI responders that received a REQ from the player, and the AI requesters
  whose confirmed trades that changes (at most one level, per the rule above);
- AI camps trading with the player, whose plans take the player's answers.

Everything else is reused. Because each camp's plan is pure, the reuse is exact:
the result must equal computing the whole turn after End Turn. That equality is
the acceptance test: per-turn fingerprints, speculative versus sequential, under
scalar and parallel runs.

Rules this has to keep:

- **Nothing writes the world during the planning phase.** The UI continues to
  read the committed world while the background task reads it too. Lazily
  created transient caches (`MaterialAccessIndex.For`, `TurnDiagnostics.For`,
  `PlanningIndex`) must be created before the task starts, or be proven safe
  to create concurrently.
- **Discard on world change.** New game, load, or any change to the committed
  world cancels the task and drops its results. Results are keyed to the
  world's turn and day.
- **Busy guards stay.** Save may run alongside the pure precompute; resolution
  still waits for it.
- **Visibility timing never enters results.** Outcomes depend only on
  submitted orders, never on when something was shown. A requisition the
  player did not answer lapses.

This hides latency for a human player; it does not reduce CPU. Headless runs,
AI-only turns and RL training get no benefit, so the handshake must stay
bounded in its own right, as above.

### Kin Societies and unrelated Societies

- **Kin Societies.** A fission daughter is an independent polity sharing its
  parent's culture and language; parent and daughters are kin with affinity
  `a` ([QoLFissionTradeDesign.md](QoLFissionTradeDesign.md), 6.4). Kin terms
  are friendlier, never self-sacrificing: the margin becomes `m · (1 − a)`,
  and only **surplus** beyond useful demand may go at `(1 − a)` of its value,
  down to a gift. Affinity is set at fission by the parent's fission policy
  (0.5 discourage, 0.9 neutral, 1.0 encourage) and is then inert; its change
  over time is out of scope.
- **Unrelated Societies** (`a = 0`): barter at the full margin, through explicit
  messages under filtered observations. A camp learns a neighbour's need only
  by receiving its bid, never by reading its stores. This matches #23's rule:
  no overmind; autonomous camps with explicit commitments.

### Food

Food trade is required: non-food barter alone cannot produce proto-towns. It
follows [QoLFissionTradeDesign.md](QoLFissionTradeDesign.md), section 7:
seller-side bands (protected survival food, growth food, safe surplus), buyer
values ordered by starvation avoided, reserve restored and growth funded, and
growth food valued by the marginal welfare of an added resident, which uses
this model's labour price `w`. Confirmed food imports enter the prepared food
envelope before demographic planning; every handshake completes during
preparation, so the timing contract holds.

## Doctrine check

- **Authoritative state.** The local rule adds none: values and `w` are
  derived per turn. Requisitions add deliberately designed saved state
  (commitments and their delivery ledger) only when trade is implemented.
- **Temporal abstraction.** Once per `TurnPlanner.Prepare`, from the committed
  boundary. Values, decay and discount are functions of physical years; a
  turn evaluates the same functions over its duration (20 years by default),
  with no sub-turn steps.
- **Scaling.** The local rule is O(claims) per search probe with no extra
  evaluations, so per-camp cost is unchanged. Trade adds O(bids) per turn, and
  bids per camp are bounded by neighbours in range. There is no global market
  clearing.
- **Mature-game scale.** Goods added later, such as amenities or metal tools,
  add claims, not new mechanisms.
- **RL and Elder.** The values, `w` and open bids are observation features.
  `λ`, `r`, `ρ`, priority multipliers and trade margins are the Elder's actions
  or personality. QoL, in the same unit, is the reward.
- **Validation.** See below.

## Phasing

The ordered work packages are in
[ValueModelImplementationPlan.md](ValueModelImplementationPlan.md).

## Validation

- **Fixtures.** The rule satisfies the equimarginal condition; shares never
  decrease along the path; tool claims are pursued first whenever `w > 0`; a
  camp with no labour scarcity reproduces full fulfilment; results are
  identical under scalar and parallel runs and reordered collections.
- **Drift.** Compare against the uniform rule with
  `Tools/PerformanceAudit/Compare-Drift.py` and a same-method control on the
  four checkpoints. Here the drift is expected: the question is whether mean
  QoL rises without population or food bias.
- **Progression.** Rerun the closed #33 harness. Improvement in Construction,
  Pottery and Carpentry windows, or a stated reason why not, is the acceptance
  signal, as #38 directs.

## Decisions

Agreed with the user on 29 September 2026:

1. **QoL stays linear in need coverage.** The exponential curve is only the
   planner's price allocation. The QoL formula gains the social cohesion
   multiplier `S(N)` from
   [QoLFissionTradeDesign.md](QoLFissionTradeDesign.md). `λ` is a planner
   calibration value.
2. **Priorities are value multipliers** (`priority / default priority`; zero
   disables).
3. **Expansion labour sits on the same spectrum as crafts.** A worker in the
   fields is not making cloth or housing, so expansion competes for labour at
   the same price `w`. Its value is the food shortfall it prevents, priced by
   food security (see Pots). Workers left over after all claims still go to
   expansion, as today.
4. **Requisitions are req/ack handshakes**: REQ, then honour, counter or
   ignore, then accept or reject; at most one counter; confirmed trades are
   binding.
5. **Pots use the risk placeholder `ρ`** until disasters and immigration supply
   an expected-shortfall estimate.
6. **Bids are bundles** so currency can be added later as one more good.
7. **Kin terms are friendlier, never self-sacrificing**: margin `m · (1 − a)`,
   only surplus given cheaply; affinity set by the parent's fission policy
   (0.5 / 0.9 / 1.0), then inert.
8. **Discount rate `r` is one configured constant**, baseline 0.01 per year and
   swept over 0–0.02, with a hook for the Elder to adjust it later.
9. **Handshake limits:** `K = 3` requisitions per camp per turn; default
   bargaining margin `m = 10%`.
10. **Initial tradeable goods:** Housing Material and Cloth, with candidate
    permutations for sweeps listed in the implementation plan. Food trade
    follows as its own work package.
11. **Plan during the player's turn.** AI decisions, AI-to-AI handshakes and
    AI economic plans are computed in the background while the player plans;
    only order-dependent parts are recomputed at End Turn.
12. **Bundles are valued by the area under the curve**; local value beyond
    full coverage is zero.
13. **Transport:** bounded range plus provisional distance and bulk costs until
    logistics (#13).

## Open decisions

1. **`λ`**, the value curve's steepness (calibration).
2. **Provisional distance and bulk costs** for transport until #13.

## Rejected approaches

- **Global market clearing across all camps.** A civilization-wide
  optimizer, rejected by #23, and at least O(camps²).
- **Money now.** Barter on each camp's own curves is enough for now, and a
  currency needs institutions that don't yet exist. Bids are bundles so it can
  be added later without redesign.
- **Linear marginal value as the planner's allocation curve.** Fills the
  cheaper need completely before touching the other. (QoL itself stays linear
  in coverage.)
- **Shared welfare between kin villages.** Would let a village sacrifice its
  own needs for a sibling; replaced by surplus-only kin terms.
- **Subtractive social friction inside a clamp.** Could zero every marginal
  value in a large, poorly supplied settlement; replaced by the cohesion
  multiplier.
- **Carrying pending items across the turn transition.** At 20-year turns
  they go stale; replaced by one-turn handshakes and the trade ledger.
- **A turn per handshake step.** 20–60 simulated years per trade at current
  turn length; replaced by in-preparation handshakes and acceptance limits.
- **Offering simply the lowest-value good.** Often worthless to the partner
  when both hold the same surplus; replaced by offers informed by the
  partner's past requisitions.
