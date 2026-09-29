# Value model for planner fulfilment (draft)

Status: design draft for discussion, 29 September 2026. Nothing here is
implemented or approved. It continues
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

Why exponential rather than linear: both have closed forms, but the exponential
never reaches zero. Surplus keeps a small positive value, so it is not
worthless. That is the trade objection's point, and it leaves room for a
neighbour's request to lift that value. Its inverse is cheap:

```
coverage the camp buys at labour price w:  c*(w) = (1/λ) · ln(V / (w · ℓ))
```

## Anchoring V in QoL

Use one unit throughout: the **person-QoL-turn**, raising one person's QoL by
1.0 for one turn. The current formula is
`QoL = F · W · (½·clothing + ½·housing)` with `clothing = 1 − E·(1 − coverage)`,
so the constant marginal QoL of a unit of each need is:

| Good | First-unit value `V` (person-QoL-turns) | Why |
| --- | --- | --- |
| Cloth, Animal Fabric | `½ · E · F · W` | One unit clothes one person for one turn. |
| Housing, Light Housing | `4 · ½ · E · F · W · L` | One unit shelters four people for its service life `L`. |

Service life with 10% decay per reference turn and a discount factor `β` per
turn is `L = 1 / (1 − 0.9·β)`: 10 turns at `β = 1`, 3.6 turns at `β = 0.8`. `β`
must be stated per year and converted by turn length so the same equations
hold for shorter turns. It is a real design lever (the camp's time
preference), and a natural Elder personality or RL parameter.

`F · W` (food and water met) multiplies every craft value. That is the
Amdahl effect already in QoL: while food is short, every craft is worth less
and labour belongs on food. Food and water stay a reserved floor, as today.

### Worked values at current calibration

A turn is 80 intervals of 91.25 days. With `E = F = W = 1` and specialist
rates:

| Work | Output per worker-turn | Value per worker-turn |
| --- | --- | --- |
| Weaver on Cloth | 80 units, clothing 80 people | 40 (plus 80 Plant Food input) |
| Builder on Housing (`β = 1`) | 2.5 units, 10 people | 50 |
| Builder on Housing (`β = 0.8`) | 2.5 units | 18 |
| Generalist Craftsperson | 0.25 × the above | 10 / 12 / 4.5 |

Cloth and Housing are of the same order, so under this model Housing is
**not** inherently poor value. Its failure in the #33 windows (Construction
productive in 4 of 21) is therefore more likely due to its schedule position,
since it is built last from leftover Wood and Clay, or to capability and
useful-demand gates. A value rule should also order material access by value,
not only change shares.

Also note that one Weaver clothes 80 people and one Builder replaces the
housing decay of about 100 people. Crafts rarely bind the resident workforce;
food, fields and herding do. In practice `w` will mostly be set by food-side
and field labour. That raises open question 4.

## Tools price themselves through `w`

A labour-saving tool is worth the labour it saves, valued at `w`. Its cost is
also labour, valued at `w`. So `w` cancels:

> A tool is worth making whenever it saves more labour over its life than it
> takes to make, provided labour is scarce (`w > 0`).

| Tool | Saves per unit | Costs per unit (specialist / generalist) | Ratio |
| --- | --- | --- | --- |
| Farm Tools (1.265625× field labour, 20 farmer-years) | 21% of 80 = 16.8 worker-intervals | 2 / 8 intervals | 8.4 / 2.1 |
| Herding Tools (2 → 2.69 animals per Herder, 20 herder-years) | 25.7% of 80 = 20.6 intervals | 3 / 12 intervals | 6.9 / 1.7 |

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
estimate, which does not exist. Placeholder: treat the reserve gap as a need
with coverage `storage / desired reserve` and `V_Pots = ρ · F-sensitivity`,
where `ρ` is a risk weight (another Elder lever). Flagged as unresolved.

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
2. **Surplus production becomes worthwhile.** Past local coverage the curve is
   near zero, but the bid step holds value up at the bid price for R units. The
   camp produces for export while `bid value ≥ w · (ℓ_X + transport)`.
3. **Every other good is tilted too.** Export work uses labour, so `w` rises and
   the camp makes a little less of every other good. The bid shifts the camp's
   whole allocation, not only X.

An export claim therefore enters the demand pass as an ordinary claim with a
flat value (the bid) and quantity R. The rule above handles it unchanged: its
share is 1 whenever `w` is below the bid value divided by its labour.

### What a bid is worth without money

There is no currency. A bid is barter: "R units of X for P units of Y". Each
camp judges it on its own curves, so no common value scale across camps is
needed:

- **Buyer B** offers while its marginal value of X exceeds its marginal value
  of the Y it gives up.
- **Seller A** accepts while A's marginal value of the Y received is at least
  the value of the X given up: its local marginal value if taken from stock, or
  `w_A · (ℓ_X + transport)` if newly made.
- Trade happens when their ratios of marginal values (marginal rates of
  substitution) differ. The exchange ratio lies between them; who captures the
  surplus is a design choice (seller's ask, buyer's bid or midpoint).

### Housing Material: a portable intermediate

Fixed Housing belongs to its construction site and cannot move. The tradeable
good is therefore the material, not the house. Agreed with the user on
29 September 2026; not yet implemented:

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

- **Labour is split, not added.** The current Builder rate for Housing is
  divided between making Housing Material and assembling it, so labour per
  Housing unit and the shipped balance stay the same. The split fraction is a
  calibration value, still to be chosen.
- **Builders and generalists only.** Builders (Construction) make and assemble
  Housing Material; Craftspersons do both at the usual `GeneralistTaskEfficiency`
  (0.25). No new role, to keep the supply chain simple.
- **No stockpiling beyond useful demand.** Housing Material is made only
  against the Housing gap, as other goods are. Housing demand therefore
  remains a recurring pressure as population grows, which is intended. The
  only exception, when trade exists, is an accepted export claim.
- **The Wood:Clay mix is fixed when material is made.** Housing Material
  becomes an ordinary recipe with direct inputs, and Housing consumes it as
  Light Housing consumes Animal Fabric. This removes construction's special
  position as the final flexible consumer of leftover Wood and Clay, which may
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

### Timing: two-stage preparation, not a one-turn lag

A requisition posted at turn t and filled at t+1 would take 20–40 simulated
years at the current turn length. Recommended instead: resolve requests inside
one `TurnPlanner.Prepare`, all from the committed boundary:

1. **Quote** (per camp, pure, parallel): each camp computes its curves and a
   preliminary `w` from the closed-form shares, without the exact staffing
   pass, and posts bids and asks for neighbours in range.
2. **Match** (canonical, serial, O(bids)): pair bids and asks in canonical
   order (by price, then camp ID and good) into commitments for this turn.
3. **Plan** (per camp, pure, parallel): each camp plans with its accepted
   export and import claims included, as above.

Delivery and payment are exchanged in one canonical step during resolution,
after production, conserving both goods. Partial fills scale both sides.

This keeps demand-once and determinism. It adds work linear in the bids,
which is bounded by neighbours in range.

### Within versus between societies

- **Same Society** (daughter villages, later #15): a request can be a transfer
  valued on the shared welfare, without barter.
- **Different Societies**: barter only, through explicit messages under
  filtered observations. A camp learns a neighbour's need only by receiving its
  bid, never by reading its stores. This matches #23's rule: no overmind;
  autonomous camps with explicit commitments.

## Doctrine check

- **Authoritative state.** The local rule adds none: values and `w` are
  derived per turn. Requisitions add deliberately designed saved state
  (commitments and their delivery ledger) only when trade is implemented.
- **Temporal abstraction.** Once per `TurnPlanner.Prepare`, from the committed
  boundary. Decay and discount are stated per year and converted by turn
  length, so the same equations hold for shorter turns.
- **Scaling.** The local rule is O(claims) per search probe with no extra
  evaluations, so per-camp cost is unchanged. Trade adds O(bids) per turn, and
  bids per camp are bounded by neighbours in range. There is no global market
  clearing.
- **Mature-game scale.** Goods added later, such as amenities or metal tools,
  add claims, not new mechanisms.
- **RL and Elder.** The values, `w` and open bids are observation features.
  `λ`, `β`, `ρ`, priority multipliers and trade margins are the Elder's actions
  or personality. QoL, in the same unit, is the reward.
- **Validation.** See below.

## Phasing

1. **Local value rule.** Values, the search on `w`, value-ordered material
   access, and the resulting `w` reported (not saved). Uniform fill remains the
   default until this phase is calibrated and accepted.
2. **Tool and Pot claims.** Farm and Herding Tools priced by labour saved;
   Pots by the risk placeholder.
3. **Housing Material.** The split recipe, inventory field and observation
   schema above. Useful locally before any trade because it removes
   construction's leftover-material scheduling.
4. **Same-Society requests.** Transfers without barter, after #15.
5. **Barter between societies.** Needs #13 (transport and range), #34 (who gets
   a shared source) and the #23 authority rules.

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

## Open decisions for the user

1. **Curve and QoL.** Keep QoL linear and use the exponential only in the
   planner, or make QoL itself concave to match (which requires recalibrating
   population, growth and fission)? And what `λ`?
2. **Discount `β` per year** for durables: a fixed constant, or an Elder
   personality trait?
3. **Priorities as value multipliers** (recommended), or keep them as a hard
   order?
4. **What is expansion labour worth?** Spare workers go to field expansion,
   which buys future food and growth, not current QoL. Pricing it in the same
   unit is the growth-versus-QoL trade-off that #23 has to settle.
5. **Requisitions.** Binding commitments or revocable quotes? Who captures the
   surplus? Which goods are tradeable? (Housing is traded as Housing
   Material; see above.)
6. **Pots.** Accept the risk placeholder `ρ` for now, or wait for a proper
   expected-shortage estimate?

## Rejected approaches

- **Global market clearing across all camps.** A civilization-wide
  optimizer, rejected by #23, and at least O(camps²).
- **Money now.** Barter on each camp's own curves is enough, and a currency
  needs institutions that don't yet exist.
- **Pure linear marginal QoL.** Fills the cheaper need completely before
  touching the other, and makes surplus worth exactly zero.
- **A one-turn requisition lag.** 20–40 simulated years per trade at current
  turn length.
