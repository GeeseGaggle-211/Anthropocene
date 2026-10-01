# HV1: animal units and proposed calibration ranges

Status: **unit derivation complete; numerical proposals provisional, not approved calibration**.
Prepared on `features/herd-valuation`, based on `87f5a71`. No production tuning
values have changed. See [HerdValuationValidation.md](HerdValuationValidation.md)
for the HV2 implementation and measured outcomes.

## What an animal unit means in this engine

The engine does not simulate individual livestock heads. At the shipped
`Consumption = 1`, `BaseDays = 91.25`, `DaysPerYear = 365` and
`AnimalFoodPerManagedAnimal = 1`, a fully nourished and managed animal unit
produces **4 food units per year**, enough for one person's annual meals.
Nutrition and management reduce that output; `FoodConsumption` is a claim on
abstract ecological biomass, not a measured ration in kilograms.

For an illustrative 2,000 kcal/person/day conversion, one food unit is
182,500 kcal and one animal unit produces 730,000 kcal/year. This conversion is
an interpretive anchor, not a new engine constant. It does not establish the
number of real animals represented by a unit. Human-edible food and grazing
biomass cannot be assigned the same energy conversion without a separate
forage/edibility model.

The existing carcass values reveal an important inconsistency in a literal
head-count interpretation:

| Engine species | Carcass food per unit | Person-years of meals | Renewable annual output / carcass |
| --- | ---: | ---: | ---: |
| Goat | 60 | 15 | 6.67% |
| Antelope | 80 | 20 | 5.00% |
| Bison | 150 | 37.5 | 2.67% |
| Camel | 120 | 30 | 3.33% |
| Horse | 110 | 27.5 | 3.64% |

For example, a goat unit's carcass is 10.95 million illustrative kcal. Assuming
15 kg edible carcass at 2,500 kcal/kg would make that unit 292 real goats, not
the design's approximate 10–30. Both carcass assumptions need verification;
the calculation demonstrates that **the unit cannot be grounded from renewable
yield while ignoring the hunting/slaughter coefficient**. Bison is also a
wild-bovid proxy, not evidence for domestic cattle, and antelope is not a
grounded domestic-livestock category.

## Capacity proposal, conditional on the unit definition

The design states an indicative 100–200 small-stock heads per herder. Treat
that statement as a hypothesis requiring a primary citation. With a proposed
unit of 25 small-stock heads, it implies **4–8 units per primitive herder**.
The shipped capacity of 2 would mean 50 heads/herder under that interpretation.
For cattle, a separate tentative 5-head unit and 10–30 heads/herder would imply
2–6 units/herder; this illustrates why a universal species-independent number
is a modeling choice, not an ethnographic constant.

The following tier table preserves today's relative multipliers. It is an
arithmetic sensitivity proposal, **not a sourced assertion about tools in
these eras**. Only the primitive/Stone tiers affect the current implementation.
Later-era multipliers need independent grounding before activation.

| Capacity setting | Shipped | Ratio to primitive | Proposed small-stock range, conditional on 25 heads/unit |
| --- | ---: | ---: | ---: |
| `AnimalsPerHerder` | 2 | 1 | 4–8 |
| `StoneHerdingToolAnimalsPerHerder` | 2.69 | 1.345 | 5.38–10.76 |
| `EarlyAdvancedHerdingAnimalsPerHerder` | 3.87 | 1.935 | 7.74–15.48 |
| `CopperHerdingAnimalsPerHerder` | 6.02 | 3.01 | 12.04–24.08 |
| `IronHerdingAnimalsPerHerder` | 10.71 | 5.355 | 21.42–42.84 |
| `PoweredHerdingAnimalsPerHerder` | 20 | 10 | 40–80 |

## Growth and offtake: distinguish the quantities

`SpeciesDefinition.GrowthRate` is an annual exponential coefficient. At
nutrition 1 and the default breeding intensity 0.55, all shipped species have
`exp(0.0201 * 0.55) - 1 = 1.1116%` annual increase, a 62.7-year doubling time.
It is not a lambing/calving percentage. The current managed model has no
natural ageing/death pass; a field rate must therefore represent retained net
increase after biological losses, not gross births. Deliberate slaughter and
starvation are separate realized flows.

These are **proposed sensitivity brackets awaiting source verification**, not
claimed FAO ranges. They show exactly what an approved real annual increase
would imply for the engine at breeding intensity 0.55:

| Proposed species proxy | Annual retained increase bracket | Implied `GrowthRate = ln(1 + increase) / 0.55` | Provisional sustainable offtake bracket to verify separately |
| --- | ---: | ---: | ---: |
| Sheep/goat (goat unit) | 8–20% | 0.140–0.331 | 15–30%/year |
| Domestic cattle (bison proxy, explicit limitation) | 2–8% | 0.036–0.140 | 5–15%/year |
| Camel | 1–5% | 0.018–0.089 | 3–8%/year |
| Horse | 3–10% | 0.054–0.173 | No bracket proposed without a source |
| Antelope | Not proposed | Not proposed | Not proposed |

Natural increase and sustainable offtake are not interchangeable. Herd
composition, breeding females, milk extraction, juvenile deaths, drought and
husbandry practice all matter. Do not add the offtake bracket to the growth
coefficient, use it as a hidden multiplier, or apply a cattle range to bison
without an explicit proxy decision. The production resolver's long horizon,
newborn exclusion and prepared target/breeding cap must also be tested when
any larger growth coefficient is considered in HV3.

## Citation status and required verification

The proxy returned `403 CONNECT` for both `www.fao.org` and
`openknowledge.fao.org` during this task. No source text was retrieved, so the
proposed numerical brackets above **must not be described as sourced ranges**.
The bibliography to verify includes:

- R. T. Wilson, *Small ruminant production and the small ruminant genetic resource
  in tropical Africa*, FAO Animal Production and Health Paper 88 (1991):
  reproduction, flock structure, losses, offtake and productivity.
- C. Seré and H. Steinfeld, *World livestock production systems: current status,
  issues and trends*, FAO Animal Production and Health Paper 127 (1996):
  system-specific productivity; distinguish pastoral from mixed farming.
- R. Blench, *Pastoralism in the new millennium*, FAO (2001): pastoral
  organization and labor. Find actual head-count/labor observations; a generic
  pastoral overview is not evidence for a 100–200-head staffing bracket.

Publisher entry points: [FAO](https://www.fao.org/) and
[FAO Open Knowledge](https://openknowledge.fao.org/). Once access is available,
record the exact page/table, study population, year, animal basis and measured
quantity, then replace or narrow the provisional brackets. This is the
remaining HV1 blocker. User approval is still required before HV3 changes
production calibration, as specified in [HerdValuationHandoff.md](HerdValuationHandoff.md).

## Decisions supported by this work

1. Approve an animal-unit interpretation jointly for renewable yield, carcass
   food and forage demand, rather than approving a staffing number alone.
2. Verify the external evidence before approving any numerical bracket.
3. Resolve the capital-life degeneracy described in the validation report
   before expecting a growth-rate sweep to change capital valuation.
4. Preserve today's rates and the opt-in flag until HV3 acceptance is evaluated.
