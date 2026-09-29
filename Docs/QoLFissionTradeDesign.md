# QoL, fission, trade and proto-urbanization

Working architecture for the Neolithic social economy.

Status: design working draft, 29 September 2026. Converted from the user's
working design document of the same date. Sections 2.3–2.4, 3.5, 4, 6.4,
7.2, 10, 11.1 and 12 were amended the same day to converge with
[ValueModelDesign.md](ValueModelDesign.md); the amendments are listed at the
end. Not implemented. The ordered work is in
[ValueModelImplementationPlan.md](ValueModelImplementationPlan.md).

**Core objective:** make self-sufficient villages viable, but create a
positive-sum path in which some food-rich villages remain rural while selected
craft and trade centres become food-importing proto-towns. Social friction,
fission, migration and trade must emerge from one coherent utility
architecture without requiring a global optimizer.

## 0. Purpose and scope

This document turns the current QoL, fission, valuation and trade discussions
into one working design. It sits beside the existing Neolithic agriculture and
value-model documents and constrains future implementation. It does not replace
the physical agriculture, husbandry, ecology or production contracts.

The current branch already models recurring Fabric consumption, fixed and
portable Housing, rudimentary expectation, food-first demographic growth, and
a value-based planner proposal with a local shadow price of labour. The missing
layer is the social-economic bridge between those systems.

### 0.1 Design goals

- Food and water remain the survival gate. A settlement cannot compensate for
  starvation with manufactured goods.
- Clothing and Housing remain recurring rudimentary needs with graded
  satisfaction.
- Social friction is part of QoL and increases with co-resident population.
- Low QoL alone does not automatically split a settlement; fission
  additionally requires a viable destination.
- Immigration and fission are resolved from a frozen population snapshot in
  one bounded pass; no iterative population churn or global matching.
- Ordinary villages may remain self-sufficient. Trade must be beneficial
  rather than mandatory.
- Food-rich villages can exchange surplus food for manufactured goods without
  necessarily sacrificing current survival or reserve security.
- Some centrally positioned or resource-advantaged settlements can specialize,
  export manufactured goods, import food, and become proto-towns.
- Town and city labels are descriptive outcomes, not upgrade buttons or
  production bonuses.
- All balance constants remain centralized and sweepable.

### 0.2 Explicitly deferred

- Permanent-settlement amenity as an immigration attractor.
- Institutional mitigation of social friction (law, councils, policing,
  bureaucracy).
- Luxury and amenity goods beyond the current rudimentary-needs and
  manufactured-goods framework.
- Full transport and logistics cost; the first trade pass uses bounded range
  plus provisional distance and bulk costs.
- Currency and market clearing.
- City and state formation, taxation, tribute and coercive food requisition.
- Opportunity terms in immigration (specialist pull toward trade centres);
  set to zero in this pass.
- Change of affinity between kin Societies over time (decay and regeneration).

## 1. Current-code starting point

The current `features/neolithic-agricultural` branch provides the physical
substrate. The social layer is added above these mechanics rather than
replacing them.

| Current mechanic | Current behaviour / value | Design treatment |
| --- | --- | --- |
| Fabric consumption | 1 Fabric per person per 20-year reference turn | Retain as recurring clothing need; consume stock and production. |
| Housing capacity | 4 people per Housing unit | Retain as shelter saturation denominator. |
| Housing decay | 10% per 20-year reference turn | Retain; replacement demand is real recurring labour. |
| Builder output | 2.5 Housing units per Builder-generation | Retain as current calibration; sweep separately if needed. |
| Rudimentary weights | 0.5 Clothing / 0.5 Housing | Retain for first QoL calibration. |
| Expectation floor | 0.25, ramping to 1 over 100 stationary years; reset when a camp moves | Retain; mobile and early groups are less penalized by missing rudimentary goods. |
| Human fission | `FissionWilling` exists but is reset to 0 | Replace with the architecture in this document. |
| Demographic growth | Food-first; `DemographicGrowthMultiplier = 1.0` | Leave independent of QoL for now. |
| Value planning | Equimarginal planner with shadow labour price `w` | Use as the economic half of trade and specialization. |

## 2. QoL architecture

QoL is an absolute statement about life in a settlement or mobile camp. It is
not itself a migration order. Migration and fission use QoL as an input to
separate decision functions.

### 2.1 Survival gate

```
B = F_sat · W_sat
```

`F_sat` and `W_sat` are the current food- and water-need satisfaction ratios.
The product remains the hard gate: when basic survival fails, all
above-survival welfare is proportionally suppressed.

### 2.2 Rudimentary needs

```
E     = E_floor + (1 − E_floor) · clamp(StationaryYears / E_ramp_years, 0, 1)
C_eff = 1 − E · (1 − C_sat)
H_eff = 1 − E · (1 − H_sat)
R     = w_cloth · C_eff + w_housing · H_eff
```

`C_sat` is recurring clothing saturation after actual Fabric consumption.
`H_sat` is usable shelter divided by required shelter. For this pass, fixed
Housing and Light Housing both satisfy the basic shelter term; their future
amenity distinction is deferred.

### 2.3 Social cohesion (revised)

Social friction represents the ordinary coordination burden of larger
co-resident groups: conflict resolution, informal enforcement, coordination,
free-riding, theft, interpersonal disputes and the general difficulty of
keeping a large community coherent before formal institutions exist.

Friction acts as a **cohesion multiplier** on all above-survival welfare, not
as a subtraction:

```
S(N) = exp(−(N / N_s)^β)
```

- `N` is the co-resident camp or settlement population, not linked hunters.
- `β = 2` reflects coordination burden growing with the number of pairwise
  relationships (proportional to N²), the scalar-stress argument. It is a
  sweepable constant.
- `N_s` is the **cohesion scale** of the group's social organization. It
  depends on the subsistence regime, decided at the turn boundary:

| Regime | Applies to | `N_s` |
| --- | --- | --- |
| Paleolithic | Societies before their first breakthrough | 220 |
| Nomadic | Mobile post-breakthrough camps without Cultivation (pastoral, foraging, fishing) | 550 |
| Agricultural | Camps and villages with Cultivation | 1780 |

A regime change takes effect at the next turn boundary. Sedentary
organization is what lets agricultural settlements hold far larger groups
together; the regime scale encodes that, not a production bonus.

### 2.4 Final QoL (revised)

```
Q = B · R · S(N)
```

Every factor lies in [0, 1], so Q needs no clamp. Its consequences follow from
the analytical model itself:

- **Scarcity compounds with size.** A gap in survival or rudimentary needs
  shrinks the group size a settlement can hold before fission pressure
  begins, sharply so for large groups (section 3.5). A small band tolerates
  shortages that break up a large village.
- **Goods never lose their value.** `∂Q/∂coverage = B · S · w_need · E > 0` for
  every uncovered need, so the planner never sees a zero marginal value from a
  clamp.
- **Crowding dilutes gains.** A crowded settlement gains less QoL per unit of
  goods (by the factor `S`), so the planner values goods somewhat less there.
  This is intended, and is watched in the sweeps.

Social friction is counted once, inside QoL. Fission and immigration must not
apply it again.

## 3. Fission mechanics

Fission asks a different question from immigration: would part of this
population be better off founding a separate community at a viable empty site?
It requires both a push from the home settlement and a credible destination.

### 3.1 Lazy persistent destination

Each camp or settlement owns one cached potential fission destination. It is
searched only when fission pressure is high enough to justify searching and no
valid target is already cached.

- Agricultural destination: fertile, watered, cultivable land capable of
  supporting the proposed daughter cohort.
- Pastoral or nomadic destination: feeding ground scored by human food, herd
  forage and herd water.
- Search radius is bounded and configurable; baseline `r_fiss = 3`, sweep 2–5.
- The cached destination persists until fission occurs, it becomes occupied or
  invalid, the source changes subsistence regime, or ecological support falls
  below viability.
- No per-turn reranking of all candidate sites.

### 3.2 Fission push

```
P_push = clamp((Q_fiss − Q_home) / ΔQ_fiss, 0, 1)
```

`Q_fiss` is the QoL level below which the pressure to separate becomes
meaningful. This is a smooth push rather than a hard population threshold.

### 3.3 Destination desirability

```
D_target = clamp(SiteSupport(daughter_cohort) / TargetSupport, 0, 1)
F_score  = P_push · D_target
```

`SiteSupport` is regime-specific but always evaluates the daughter cohort, not
the parent's full population. A fission proposal exists only if
`F_score > θ_fiss`, no higher-priority immigration proposal exists, and the
daughter group satisfies minimum viable population and provisioning rules.

### 3.4 Fission population

```
N_daughter = floor(N_home · f_fiss)
```

The daughter fraction is deliberately chunky because fission represents a
coherent household or kin group rather than individual migration. The
daughter founds a new Society (section 6.4).

| Lever | Baseline candidate | Sweep | Status |
| --- | --- | --- | --- |
| `Q_fiss` | 0.75 | 0.70–0.80 | Calibration target |
| `ΔQ_fiss` | 0.10 | 0.05–0.15 | Calibration |
| `θ_fiss` | 0.20 | 0.10–0.30 | Calibration |
| `f_fiss` | 0.30 | 0.20–0.40 | Calibration |
| `r_fiss` | 3 hexes | 2–5 | Performance-bounded |

### 3.5 Fission size by regime (new)

With a fully desirable destination (`D_target = 1`), a fission proposal
appears once `Q < Q* = Q_fiss − θ_fiss · ΔQ_fiss = 0.73`. Solving
`B · R · S(N*) = Q*` gives the population at which fission begins:

```
N* = N_s · [ln(B · R / Q*)]^(1/β)      (N* = 0 when B · R ≤ Q*)
```

The `N_s` values in section 2.3 are fitted to the design targets at stated
reference conditions (`B = 1`):

- Mobile regimes: `R_ref = 0.90`. Expectation stays near its 0.25 floor
  because `StationaryYears` resets on each move, so even partial goods
  coverage (about 60%) leaves R high.
- Agricultural regime: `R_ref = 0.82`, a long-settled village (`E = 1`) with
  needs about 82% met; Housing is the known shortfall (#33).

| Regime | `N*` at reference (median target) | `N*` fully supplied (`B · R = 1`) | Design target |
| --- | --- | --- | --- |
| Paleolithic | 101 | 123 | High fission probability above 100 |
| Nomadic | 252 | 309 | Median about 250 |
| Agricultural | 607 | 999 | Median about 600; upper echelon about 1,000 |

Scarcity compounding, agricultural regime:

| `B · R` | 1.00 | 0.95 | 0.90 | 0.85 | 0.82 | 0.78 | 0.75 | ≤ 0.73 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `N*` | 999 | 914 | 814 | 694 | 607 | 458 | 293 | push at any size |

Below `Q*`, the push exists at any size, but fission still requires a viable
destination and a minimum viable daughter group. The reference medians are
design anchors; the acceptance targets are measured distributions (11.1).

## 4. Immigration interface and ordering

Full immigration amenity is deferred, but the fission design reserves the
interface. Immigration compares an existing destination against the current
settlement; fission is tested only when no existing destination is
sufficiently better.

```
ΔU_imm(i → j) = w_Q · (Q_j − Q_i) − K_distance − K_attachment − K_lifestyle
```

The opportunity term `O_j,g − O_i,g` (profession-specific pull, for example
specialist brain drain toward trade centres) is out of scope for this pass and
is zero.

- Immigration is evaluated before fission from the same frozen snapshot.
- Each source emits at most one immigration intent: its best valid known
  destination within `r_imm`.
- A source with a valid immigration intent does not also emit a fission
  intent that turn.
- Sedentary-to-nomadic lifestyle switching carries a larger barrier than
  nomadic-to-sedentary switching.
- No newly arrived population can migrate again during the same turn.

| Barrier | Initial sweep |
| --- | --- |
| Immigration deadband `θ_imm` | 0.05 / 0.075 / 0.10 |
| Nomadic → sedentary extra cost | 0 / 0.05 / 0.10 |
| Sedentary → nomadic extra cost | 0.10 / 0.15 / 0.25 |
| Immigration radius `r_imm` | 3 / 4 / 5 hexes |

## 5. Economic value model

The value model in [ValueModelDesign.md](ValueModelDesign.md) is the economic
core. For each manufactured good X, the planner compares the value of another
unit with the opportunity cost of the labour required to make it.

```
produce X while marginal_value_X ≥ w · labour_per_unit_X
```

`w` is the local shadow price of resident labour: the value contributed by the
last worker in their best available use. This is a local planning
equilibrium, not a global market optimum.

### 5.1 Marginal local-use curve

```
v_X(c) = V_X · exp(−λ_X · c),   0 ≤ c ≤ 1
c = (stock_after_decay + planned_output) / D_X
```

The exponential is a planner-only scarcity curve. QoL remains linear in need
coverage. Local production is capped at useful local demand: once coverage
exceeds 1, local-use value is zero. Surplus production becomes valuable only
through an external use such as an accepted trade claim.

### 5.2 Integrated value for bundles

A trade bundle must not be valued at a single marginal point when the quantity
materially changes coverage. Use the area under the marginal-value curve:

```
ΔU_X(c0 → c1) = (V_X · D_X / λ_X) · [exp(−λ_X · c0) − exp(−λ_X · c1)]
```

For received goods, integrate upward from current coverage. For goods
surrendered from local useful stock, integrate the lost area. This prevents
the twentieth unit of imported Cloth from being valued as highly as the first
unit in an acute shortage.

### 5.3 Value by best use

```
Value_X = max(Value_local_use, Value_exchange, Value_production_input)
```

Tools derive value from labour saved; Housing Material derives value from the
Housing it completes minus remaining assembly labour; trade claims give
otherwise-surplus output an external use.

## 6. Trade bundles and bounded handshake

Trade is bilateral and local. There is no global price solver and no
civilization-wide optimizer.

```
Bid { Give: [(Good, Qty)], Receive: [(Good, Qty)], AcceptanceLimit }
```

Each camp evaluates the same bundle using its own value curves. Values are
never directly compared across settlements; only the proposed physical
exchange is shared.

### 6.1 Acceptance test

```
Gain_i = IntegratedValue_i(receive) − IntegratedLoss_i(give)
         − ProductionOpportunityCost_i − TransportCost_i
```

A trade is acceptable when both parties have non-negative gain after their
bargaining margin. When a requested export must be newly produced, its
opportunity cost is `w · labour` plus material, feedstock and transport costs.
When it comes from stock inside local useful coverage, the seller also loses
the integrated local-use value.

### 6.2 Handshake

- REQ: requester states desired bundle, offered bundle and worst acceptable
  terms.
- ACK: responder may honour, counter once, or ignore.
- CLOSE: requester accepts or rejects the counter; acceptance is automatic if
  already inside the REQ's acceptance limit.
- At most K requisitions per camp per turn; baseline K = 3.
- Every handshake finishes or lapses within its turn; the player sees the
  complete trade ledger while planning.
- No global market clearing. All values come from the committed boundary and
  canonical ordering.
- Confirmed exchange ratios are recorded in a trade ledger so later markets
  and currency can inherit price history.

### 6.3 Quantity selection

Potential requests are ranked by total recoverable utility, not merely the
first marginal unit. For each shortage, compute the integrated utility
available between current coverage and the requested or target coverage;
request the best bounded improvement.

```
RequestScore_X(q) = IntegratedGain_X(q) − ExpectedGiveCost(q)
```

### 6.4 Kin Societies and affinity (new)

A fission daughter is an **independent polity**: a new Society spawned from
its parent, sharing its culture and language. A parent and its daughters, and
daughters of the same parent, are kin and carry an **affinity** `a ∈ [0, 1]`;
unrelated Societies have `a = 0`.

- Kin terms: the bargaining margin becomes `m · (1 − a)`. Surplus beyond
  useful local demand may be given at `(1 − a)` of its value, down to a gift.
- A Society never trades away units inside its own useful demand below its own
  reservation value, whatever the affinity.
- In this pass affinity is **inert and high**: `a = 0.9` for every kin pair,
  so the kin margin is 1%. Decay over tens of turns, and regeneration through
  contact or trade, are out of scope.

## 7. Food trading

Food trading is required to test village differentiation and proto-town
formation. Non-food barter alone cannot validate urbanization.

Food already has a strict biological ordering in the simulation, so it is not
treated like an ordinary craft inventory. The trade design preserves the
current consumption, reserve and growth ledger.

### 7.1 Seller-side food bands

- **Protected survival food:** current-cohort consumption, mandatory
  manufacturing feedstock and the operating reserve. Ordinary voluntary trade
  cannot spend this band.
- **Growth food:** food allocated to feasible demographic growth. It has a
  real opportunity value (7.2).
- **Safe surplus:** food remaining after survival, reserve, manufacturing and
  the planned growth. This is the cheapest voluntary export supply.

```
F_safe_export = max(0, F_projected − F_consumption − F_reserve
                       − F_manufacturing − F_growth)
```

### 7.2 Food value (revised)

Imported food enters the ordinary camp food ledger; no urban food pool is
created. Its value is highest when it prevents current consumption shortfall,
then when it restores operating reserves, then when it funds growth.

| Food use | Relative value | Rule |
| --- | --- | --- |
| Avoid current starvation | Very high | Food-security value; must dominate ordinary craft consumption. |
| Restore operating reserve | High | Food-security value; protects next-turn continuity and shocks. |
| Fund demographic growth | The welfare an added person brings | Below; competes with manufactured goods at the margin. |
| Excess beyond useful storage and growth | Zero locally | Natural export candidate. |

**Growth food is valued by the marginal welfare of one more resident.** Total
welfare is `W = N · Q`. One added person contributes, per year:

```
dW/dN = B · S · [R − E · Σ_uncovered w_k · c_k]     (their own QoL, net of
                                                   diluting shared goods)
        − β · (N / N_s)^β · Q                       (added social friction)
        + w                                         (their labour, at the
                                                   local labour price)
```

The value of growth food is then
`max(0, dW/dN) · L / food_per_birth`, where `L` is a discounted
person-lifetime in years and `food_per_birth` is the food cost of one birth
under the current growth rule. Demographic growth itself is unchanged and
still food-first; this value only decides whether growth food is worth more
exported than kept.

This delivers the design intent without a separate rule:

- A **low-QoL, food-rich village** whose goods are limited by materials or
  skills, not labour, has a low `w`. More people mostly dilute scarce goods and
  add friction, so its growth food is worth little locally and is exported for
  the goods it lacks.
- A **balanced village** with labour-limited production values an added
  worker highly, so it keeps its growth food and may import more.
- **Mobile groups** (E near 0.25) barely feel goods dilution, so their growth
  food keeps its value.

Food export policy is a sweep lever: this valuation (proposed default) versus
a safe-only control that never exports growth food.

### 7.3 Timing contract

Growth planning occurs before ordinary post-production exchange, so food
imports intended to feed the same generation are represented as a prepared
inbound commitment before demographic food planning. The physical exchange
settles in the canonical trade step, but the commitment is part of the
prepared food envelope and must conserve on settlement.

- No speculative inbound food may support births unless the trade is confirmed
  at preparation. Every handshake completes during preparation, so this holds.
- If delivery is short, growth and reserve outcomes scale to actual realized
  food; the resolver does not invent the promised amount.
- The same food unit cannot be counted for both seller growth and buyer
  growth.

## 8. Design goal: villages, proto-towns and later cities

The target is not to make village self-sufficiency fail. The target is to make
regional specialization more productive for some settlement configurations
while allowing many villages to remain villages.

### 8.1 Stage 1 — self-sufficient settlement network

Fission and migration populate desirable land. Early villages use generalists
and a small number of specialists to satisfy most needs locally. This remains
a valid strategy.

### 8.2 Stage 2 — positive-sum differentiation

Settlements acquire comparative advantages from land productivity, resource
access, trade centrality, specialist experience and later bounded
agglomeration effects. A food-rich village may find it cheaper to export food
and import Cloth or Housing Material than to maintain the same local craft
mix.

```
Trade is positive-sum when
Value(imported goods + labour freed) > Value(food exported + trade costs)
```

The manufacturing centre sees the reverse: external bids make additional
specialist labour worthwhile. As specialist share rises, its local food
surplus shrinks and can become a food deficit. It then becomes structurally
dependent on rural imports.

### 8.3 Proto-town diagnostic

Townhood is descriptive, not causal. A settlement becomes town-like when a
combination of the following emerges:

- meaningful non-food specialist share;
- regular manufactured-goods exports to multiple settlements;
- meaningful imported-food share;
- external demand supporting production beyond local useful demand;
- population support above what its local food footprint alone would sustain.

No threshold grants a flat Town production bonus. The label can be used for
UI, history and later institution unlocks once the underlying economic
structure already exists.

### 8.4 Why some villages remain villages

- Their agricultural land has high marginal returns, so labour is worth more
  in food production.
- Their accessible external market is too small to support export
  specialization.
- Transport costs make local craft self-sufficiency competitive.
- A nearby craft centre already serves the regional manufactured-goods market.
- Social cohesion limits and fission pressure shed excess rural population
  before urban-scale concentration occurs.

### 8.5 Cities are a later layer

The current design creates proto-towns, not complete cities. Cities require
the immigration attractors and institutional mitigation deliberately deferred
here: amenities, durable built environment, specialist opportunity,
administration and ultimately state capacity. The architecture is designed so
those later terms enter utility without replacing trade, QoL or food
accounting.

## 9. Mobility resolution and performance contract

Population mobility must never become an iterative graph-optimization problem.
Every decision is evaluated once from a frozen end-of-turn snapshot.

1. Freeze all camp populations, QoL values, regimes, known neighbours and
   cached fission targets.
2. Each settlement scores only known settlements within `r_imm` and emits at
   most one immigration intent.
3. If no immigration intent clears the deadband, evaluate one fission intent
   against the cached target.
4. Reconcile all immigration intents once. Do not reroute rejected or excess
   migrants to second-best targets.
5. Reconcile fission intents once using population remaining after approved
   immigration outflow.
6. Commit all population changes simultaneously. Recompute QoL only next turn.

```
T(N) = O(N · r_imm² + F · r_fiss² + N)
```

With both radii bounded at 5 or less, the pass is O(N) in the number of
settlements. `F` is only the subset needing a fresh fission-site search,
normally much smaller than N.

Forbidden: all-pairs settlement comparisons, iterative rerouting, stable
matching, min-cost flow, global market clearing, same-turn multi-hop
migration, or recomputing QoL after each transfer.

## 10. Central calibration and sweep table

| Lever | Baseline / current | Initial sweep | Notes |
| --- | --- | --- | --- |
| Fabric consumption | 1 / person / 20 y | 0.5–4 | Current physical need. |
| Housing people/unit | 4 | 3–6 | Current. |
| Housing decay | 0.10 / 20 y | 0.05–0.20 | Current. |
| Rudimentary weights | 0.5 / 0.5 | 0.4–0.6 split | Clothing / Housing. |
| Expectation floor | 0.25 | 0.15–0.35 | Mobile and early tolerance. |
| Expectation ramp | 100 y | 60–160 y | Stationary expectations. |
| Cohesion exponent `β` | 2 | 1.5–3 | Pairwise coordination burden. |
| `N_s` Paleolithic | 220 | 180–260 | Fission above about 100. |
| `N_s` Nomadic | 550 | 450–650 | Median about 250. |
| `N_s` Agricultural | 1780 | 1500–2100 | Median about 600, upper about 1,000. |
| `Q_fiss` | 0.75 | 0.70–0.80 | Fission push onset. |
| `ΔQ_fiss` | 0.10 | 0.05–0.15 | Smoothness. |
| `θ_fiss` | 0.20 | 0.10–0.30 | Requires destination quality. |
| `f_fiss` | 0.30 | 0.20–0.40 | Daughter cohort size. |
| `r_fiss` | 3 | 2–5 | Lazy site search. |
| `θ_imm` | 0.075 | 0.05–0.10 | Immigration deadband. |
| `r_imm` | 4 | 3–5 | Bounded neighbour search. |
| Kin affinity `a` | 0.9, inert | 0.7–1.0 | Decay out of scope. |
| `λ` | open | calibrate | Planner scarcity curve. |
| Discount rate `r` | 0.01 / y | 0–0.02 / y | Durable-value sensitivity. |
| Requisitions `K` | 3 | 1–3 | Bounded handshake. |
| Bargaining margin `m` | 10% | 0–20% | Local and Elder policy hook. |
| Food export policy | Growth valued by marginal welfare | vs safe-only | Critical town-formation lever. |

Changing `Q_fiss`, `θ_fiss` or `ΔQ_fiss` moves every regime's fission size;
refit `N_s` with the section 3.5 formula when they change.

## 11. Required analytical sweeps and acceptance tests

### 11.1 QoL and fission calibration (revised)

- Paleolithic groups: fission probability is high above 100 residents under
  realistic clothing and shelter states; quantify hazard at N = 50, 75, 100,
  125, 150.
- Nomadic groups: median fission size about 250; quantify at N = 150, 200,
  250, 300, 350.
- Agricultural villages: median fission size about 600, upper echelon about
  1,000; quantify at N = 250, 500, 750, 1000 across full and partial
  rudimentary-needs satisfaction.
- Verify no hard population cliff; hazard changes smoothly with QoL and target
  desirability.
- Verify no fission when no viable daughter site exists, even under low QoL.

### 11.2 Food-export and town sweep

- Environment and capacity: river, grassland, forest, marginal land; local
  sustainable food support spanning roughly 400–1200 people.
- Compare self-sufficient village, food-exporting farm village, and
  manufactured-goods-exporting craft centre.
- Measure safe food exports after consumption, reserve, manufacturing inputs
  and planned growth, and growth-food exports under the marginal-welfare
  valuation.
- Measure the exchange ratios at which farm villages are better off importing
  manufactured goods than producing them locally.
- Measure how many surrounding food villages are required to support a 500-,
  750- and 1000-person craft centre at different import shares.
- Sweep generalist efficiency, specialist productivity and agglomeration,
  `λ`, transport penalty and food-export policy.

### 11.3 Trade-bundle tests

- Integrated bundle valuation is monotone and shows diminishing gain as
  coverage approaches 100%.
- A reciprocal barter is accepted only if both camps value their received
  bundle at least as highly as their surrendered bundle plus costs and margin.
- Local useful demand never produces surplus merely for research; only
  confirmed external claims justify export production.
- Food exports cannot debit the protected survival band.
- Confirmed inbound food supports same-turn demographic planning only if
  confirmed at preparation and later conserved on delivery.
- All trade results remain deterministic under reordered collections and
  parallel execution.

### 11.4 Performance tests

- Mobility neighbour checks scale with bounded local radius, never all-pairs
  camps.
- Fission-site search occurs only when the cached target is missing or invalid
  and fission push exceeds the search threshold.
- At most one immigration intent or one fission intent per source settlement
  per turn.
- No iterative rerouting after destination scaling or collision.
- Trade remains bounded by K requisitions per camp; no global clearing pass.

## 12. Repository integration map

| Area | Current source / design | Expected future integration |
| --- | --- | --- |
| QoL calculation | `NeolithicProduction.ResolveNeeds` | Multiply by the cohesion `S(N)` of the camp's regime; leave `DemographicGrowthMultiplier` unchanged. |
| QoL constants | `NeolithicConfig` | Add `β`, regime `N_s`, fission, migration and affinity levers. |
| Fission state | `Camp.FissionWilling` currently reset | Replace with derived mobility proposal state plus a saved cached target. |
| Society lineage | `Societies.cs` (`Society`) | Add a saved parent-Society link; daughters inherit culture; kin affinity inert at 0.9. |
| Agricultural site scoring | `Agents` agricultural candidate forecasting | Reuse physical support projection for daughter-site desirability. |
| Pastoral site scoring | `SiteCoverage` and husbandry forecasts | Reuse min(human food, herd forage, herd water). |
| Economic value | `ValueModelDesign.md`, `ProductionDemand` | Integrated bundle valuation, food bands and growth-food value. |
| Planner | `EconomicPlanner`, `IFulfilmentRule` | Keep local shadow price `w`; no global optimizer. |
| Trade | `ValueModelImplementationPlan.md` | Trade foundation, handshake and food-trade work packages. |
| Turn resolution | `TurnResolutionPasses.md` | Frozen post-demography mobility snapshot and one commit pass. |

## 13. Design summary

Survival determines whether people can live somewhere. Rudimentary needs and
social cohesion determine how good life is. Immigration asks whether an
existing community is sufficiently better. Fission asks whether a viable new
community is worth founding. Trade lets settlements specialize without making
self-sufficiency impossible. Food imports are the bridge that turns selected
craft villages into proto-towns.

The economic objective is regional differentiation, not a universal
settlement ladder. A healthy rural village may remain a rural village
indefinitely. A craft centre becomes town-like only when external demand makes
specialization worthwhile enough that local food production is no longer
sufficient and surrounding settlements benefit from supplying it.

## Convergence amendments (29 September 2026)

Agreed with the user when merging this design with the value model:

1. **Social friction became a cohesion multiplier** (2.3–2.4). The original
   subtractive term `k_social · max(0, N − N_free)` inside a clamp could zero
   every marginal value in a large, poorly supplied settlement. The multiplier
   needs no clamp. Its regime scales are fitted to the targets: Paleolithic
   fission above about 100, nomadic median about 250, agricultural median
   about 600 and upper echelon about 1,000 (3.5, 10, 11.1).
2. **Fission daughters are independent kin Societies** with inert high
   affinity; change of affinity over time is out of scope (6.4).
3. **Growth food is valued by the marginal welfare of an added resident**, so
   low-QoL, food-rich villages export food to balanced villages (7.2). The
   safe-only policy remains a sweep control.
4. **Immigration opportunity terms are out of scope** (4).
5. Trade wording aligned with the agreed handshake: acceptance limits and
   one-turn lifetime (6.2), bounded range with provisional distance and bulk
   costs (0.2).
