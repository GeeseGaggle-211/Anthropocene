# Herd valuation: herds as breeding capital, cultivation payoff and marginal land

Status: **design, not implemented.** This is the next mechanic in the social-economy line. It recalibrates how the Value
planner prices a managed herd against fields, so that the herd-labour coupling (`Docs/HerdLabour.md`,
`HerdLabourEnabled`, shipped opt-in) can be turned on without wiping out pastoralism. The work queue, commands and
baselines are in `Docs/HerdValuationHandoff.md`. Read with `Docs/HerdLabour.md`, `Docs/ValueModelDesign.md`,
`Docs/HusbandrySettling.md`, `Docs/QoLMortality.md`, `Docs/SocialCohesion.md` and `Docs/Fission.md`.

## 1. Problem

### 1.1 What the herd-labour coupling fixed

Before the coupling, managed herds grew on forage and breeding intensity alone. No animal left a herd except by
starvation. The planner reserved every herder the herd required before staffing any other claim.

With QoL mortality on (elasticity a = 1), this locked herders into a trap:
1. A camp's QoL fell below the zero-growth level Q* = 0.746, so its people stopped growing.
2. The herd kept growing and its herders consumed all the labour.
3. With nobody weaving or building, there was no Animal Fabric and no housing, so QoL fell to 0 and growth stayed at 0.

Measured (five Standard/80 worlds, turn 50 after the opening):

| Run | Camps where every resident herds |
|---|---|
| Fission off | 44 of 106 husbandry camps |
| Fission on | 156 of 1,144 husbandry camps |

A typical trapped camp held 23 people and 1,165 animals.

The coupling staffs herders at the labour price w like any other claim. It slaughters animals above the staffed herd
target, crediting their carcass food. It removes the trap in every configuration.

### 1.2 What it broke (why the lever ships off)

Once a camp holds fields, the valuation keeps only the seed herd: about 0.01 animals per person, down from 0.4–1.2.

| Measurement | Lever off | Lever on |
|---|---|---|
| Husbandry-first population, mortality off, fission off | 175,843 | 112,028 (−36%) |
| Camps holding herds, with hinterland, a = 1, fission on | 3,342 | 119 |
| Total population, same run | 2.09 M | 0.90 M |

That removes the husbandry route, which the design and the shipped route distribution rely on: about 30% of tribes
reach Husbandry first.

### 1.3 Root cause: the herd is valued as a one-turn food flow

`HerdLabour.Build` (`Assets/_Anthropocene/Simulation/HerdLabour.cs`) values each herder step by the food its animals
yield during the coming turn. It places that food into the ExpansionValuation buckets: consumption, then Animal
Fabric/Light Housing feedstock, then reserve refill, then growth. It adds a labour-saving term. The step is staffed
while its value is at least w.

At current calibration a herder loses to a farmer:

| Worker | Calculation | Food per year | Feeds |
|---|---|---|---|
| Herder | `AnimalsPerHerder` 2 × `AnimalFoodPerManagedAnimal` 1 food per animal per 91.25-day interval | 8 | 2 people |
| Gatherer | `GatherPerPerson` 1.8 per interval | 7.2 | 1.8 people |
| Established farmer | gatherer × `FarmerProductivity` | 10.8 | 2.7 people |

(A person consumes 4 food per year.)

What the one-turn valuation leaves out:

1. **The herd is capital that reproduces.** Slaughtering an animal destroys its future offspring and yield. The ladder
   counts only one turn of newborns (the grown-herd forecast) and nothing beyond. Fields, by contrast, are valued over
   their capital life: `ExpansionValuation.CapitalLife = 1/(ln2/DecayHalfLifeYears + r)`.
2. **Cultivation pays off late.** A new field step costs build and upkeep labour now, and its yield ramps over
   `IntensificationTurns`. The herd ladder compares herders with an *established* farmer's productivity, as if the
   field already existed. It ignores the payoff delay and the risk that a new field's yield arrives decades later.
3. **Husbandry uses marginal land.** Forage on non-cultivable tiles (steppe, arid, hills, tundra edges) has no field
   alternative. There, the herd competes only with gathering, not with farming. The ladder applies one camp-wide
   alternative rate whatever land the animals graze. Mobile herds also support about 1.6× the animals of stationary
   ones (`Docs/MobileHusbandryValidation.md`). This is already used as `SettledHerdGrowthPenalty` 0.35 for settling,
   but not in the herd's own value.
4. **Herder productivity is ungrounded.** One "animal" yields a person's consumption: 1 food per 91.25 days, about
   180,000 kcal at 2,000 kcal/day. One such unit stands for a herd of real animals (on the order of 10–30 small stock).
   A herder managing 2 units is then roughly 20–60 small stock, below ethnographic herd sizes per herder (often
   100–200 sheep/goats, or tens of cattle). `GrowthRate` 0.0201 per year, times breeding intensity 0.55, gives
   about 1.1% per year, also far below the natural increase of real herds.

   These are calibration values, not laws. Re-ground them with sources before tuning around them.

The planner is behaving correctly: at high intensity, fields feed more people per worker and per tile. The model is
missing the offsets that kept pastoralism viable historically:
- the herd's regenerative capital value;
- the time cultivation needs to pay off;
- the herd's command of land that cannot be farmed.

## 2. Goals and non-goals

### 2.1 Goals

- Value a herd step on the same basis as a field step: person-QoL-years, the same discount rate
  `DiscountRatePerYear`, and the same labour price w.
- A herd is worth its discounted stream of yield and offspring over its breeding life, net of the labour to manage
  it. Slaughter happens only when the carcass is worth more than keeping the animal.
- Field steps carry their real payoff profile: build labour now, yield after a ramp. The herd's alternative is the
  *marginal* field step's value at w, not the established farmer's average productivity.
- On land with no field alternative (non-cultivable footprint tiles, or the pasture a mobile camp ranges over), the
  herd competes only with gathering.
- Herder productivity and herd growth are grounded in cited ethnographic or zooarchaeological ranges and recorded in
  `Docs/ConfigurationUnits.md`.

### 2.2 Non-goals

- No hard quota or share target for husbandry.
- No technology-label switch.
- No per-animal agents.
- Herd trade (WP7–WP9 in `Docs/ValueModelImplementationPlan.md`) is out of scope.

### 2.3 Acceptance

Five Standard/80 worlds, 12 + 50 turns, `HerdLabourEnabled = true`:
- **Route preserved.** Compared with the lever-off control in the same configuration, the share of camps holding
  herds and the husbandry-first population are within ±20%, with mortality off and with a = 1.
- **Herder share bounded.** No camp has every resident herding with Q ≤ Q*. Herder share P90 is at most about 0.35.
- **QoL.** Husbandry QoL P25 is at or above the lever-on result already measured: fission off 0.71, fission on 0.77.
- **Settling and villages.** The settling behaviour in `Docs/HusbandrySettling.md` is not regressed: husbandry camps
  that become villages and Cultivation adoption timing.
- **Scaling.** Cost per camp is O(steps + herds), with no new global scans. Turn time stays within +5% of the lever-on
  result.

## 3. Design

### 3.1 Herd capital value per animal (closed form)

Notation:
- y: net food yield per managed animal per year (yield less displaced human forage, as now).
- g: the herd's net growth rate per year at the planned breeding intensity and the nutrition the pasture supports.
- r: `DiscountRatePerYear`.
- K: pasture capacity for this camp's herd, from the grown-herd forecast's forage and water limits.
- v_f: the marginal food value, taken from the bucket the food lands in. The ladder already computes this.

Ignoring capacity, an animal kept indefinitely is worth its yield plus its offspring's yield:

```
V_keep = v_f · y / (r + m − g)       for r + m > g; m is the herd's capital loss rate (death/ageing)
```

Near capacity, offspring have nowhere to go, so the growth term vanishes. Use a capacity-weighted growth
g_eff = g · max(0, 1 − H/K), where H is the herd after this step. Then:

```
V_keep(step) = v_f · y · L_herd(g_eff),   L_herd(g) = 1 / (r + m − g)   (cap L_herd at the field CapitalLife ceiling
                                                                           so neither asset can be worth more than
                                                                           a perpetuity at r)
V_slaughter  = v_f' · FoodYield_species   (carcass food now, valued at the bucket it fills)
step value per herder-year = (V_keep − V_slaughter) per animal × animals the step manages / L_herd   (annualised)
```

Use the same annualisation as the expansion ladder's lifetime value, so herd and field steps compare like with like.
Slaughter takes animals whose annualised keep value is below w per herder-share. This replaces "animals above the
staffed capacity", with the staffed capacity still the hard upper bound.

Open choices, to be decided by measurement (record the decision in `Docs/HerdLabour.md`):
- **m.** A species mortality or ageing rate; default 0 if the model has no natural herd deaths beyond starvation.
- **Mobile versus sedentary g.** Divide g by (1 − `SettledHerdGrowthPenalty`) for mobile camps, or use the settled
  rate only for villages.

### 3.2 Cultivation payoff profile in the comparison

The herd's labour-saving term currently uses `altRate = GatherPerPerson · (Established ? FarmerProductivity : 1)`.
Replace it with the **marginal field step's annualised net value per worker-year at price w**. This is the same number
the expansion ladder already computes for its next step (`ExpansionValuation` / `CultivationEconomy.ExpansionLadder`),
including:
- build labour now;
- yield ramping over `IntensificationTurns`;
- discounting at r;
- the settling cost for husbandry-primary camps (`SettlingTerms`).

Do not reuse the average established-farmer rate. Where the ladder has no next step (no cultivable land left), the
field alternative is absent and only gathering remains.

One closed-form check ties the two ladders together: a herd step and a field step with equal annualised value at w
must be staffed in the same order, whichever is enumerated first. Add a test.

### 3.3 Marginal land: per-tile alternative

Split the herd's forage claim by footprint tile into cultivable tiles (fields possible) and non-cultivable tiles.
The test is the same predicate the fission destination support uses for "cultivable footprint land".
- On non-cultivable tiles, the herd's alternative is gathering only.
- On cultivable tiles, the alternative is the marginal field step from 3.2.

The herd ladder's steps then carry a land mix. The first animals graze the land with the weakest alternative, so the
ladder stays nonincreasing. For mobile (non-village) camps, add the pasture within herd range (`HerdMovement`)
outside the footprint, with no field alternative at all. This is what makes nomadic pastoralism on steppe out-value
farming, while on rich alluvial land fields still win.

### 3.4 Grounded herder productivity and herd growth

Before tuning:
1. Write the unit derivation in `Docs/ConfigurationUnits.md`. One managed animal unit yields 1 food per interval, which
   is one person's consumption. State what a unit represents in real animals for each species (`FoodYield`,
   `FoodConsumption`) and cite ranges for:
   - small stock per herder, and cattle per herder;
   - annual herd natural increase for sheep/goat, cattle and camel;
   - sustainable offtake share.
2. Propose `AnimalsPerHerder`, the Herding Tool tiers (`StoneHerdingToolAnimalsPerHerder` 2.69 …
   `PoweredHerdingAnimalsPerHerder` 20) and species `GrowthRate` values within those ranges.
3. Sweep within the cited ranges, not outside them. A value outside the range needs a written reason.

Changing these alters the shipped Husbandry calibration (`Docs/AgricultureHusbandryPlaytests.md`,
`Docs/MobileHusbandryValidation.md`). Re-measure the route distribution in the AGENTS.md regression (seeds 1–3, every
shape, Standard/80) and update its numbers.

### 3.5 Interplay with existing mechanics

- **Breeding cap.** `HerdLabour.CapBreeding` stays. Intensity is capped so the expected herd does not overshoot the
  (now larger) target.
- **Herd-growth credit and settling cost** (`Docs/HusbandrySettling.md`). The credit in `ExpansionValuation` and the
  new capital value must not double count. When the herd ladder carries capital value, the expansion ladder's
  herd-growth credit should be removed, or reduced to the part not captured, with a test.
- **QoL mortality.** At Q = 0 the food value v_f is 0, which is mathematically right for multiplicative QoL. A camp
  with no clothing gains no QoL from food. The Animal Fabric/Light Housing feedstock buckets carry the value for such
  camps; verify they do in the trap fixture.
- **Fission.** Daughters copy the parent's `HerdTarget` proportionally. This is a Fission.cs change, coordinated with
  package HV4 (herd-carrying split) in the handoff.

## 4. Doctrine checklist

| Question | Answer |
|---|---|
| Authoritative state | Unchanged from `Docs/HerdLabour.md`: herd counts, `Camp.HerdTarget`, Animal Food store. Capital values, land mixes and alternative rates are transient planner values. They are never saved, observed or hashed. |
| Temporal abstraction | Annual rates, discounted perpetuity in closed form. Evaluated once per camp per plan. No sub-turn stepping. |
| Scaling | O(herder steps + footprint tiles + herds) per camp. It reuses the expansion ladder already built for the camp. No global scans. |
| Mature-game scale | The formulas take later-era herding tools and veterinary hooks as multipliers on herder productivity and on m and g. |
| RL implications | The herd target remains an owner-only Economy setting, visible in the owner's observation. AI and player use the same rule. |
| Validation | Closed forms (3.1–3.3), herd/field ordering equivalence, the trap fixture, a healthy-pastoral fixture, a steppe-versus-alluvial fixture, five-world sweep against section 2.3, determinism, save compatibility. |

## 5. Rejected alternatives

- **A fixed husbandry share or herd floor:** an arbitrary cutoff, contrary to the grounding rule.
- **Turning the herd labour coupling off permanently:** this restores the trap under QoL mortality.
- **Raising `AnimalsPerHerder` alone, without capital value:** it hides the one-turn valuation error and would
  overshoot on rich land.
- **Valuing herds by the one-turn grown-herd forecast plus a larger growth credit:** still a one-turn horizon,
  inconsistent with how fields are valued over their capital life.
