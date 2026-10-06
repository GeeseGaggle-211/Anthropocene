# Herd-labour coupling: controlled slaughter and a valued herd size

Authoritative record of the rule that lets a camp choose how many managed animals it keeps, prices the herders they need on the same labour price as every other claim, and slaughters the rest. Read with `Docs/QoLMortality.md` (the loop it breaks), `Docs/ValueModelDesign.md` (labour price `w`, claim values), `Docs/HusbandrySettling.md` (the herd-growth credit and food buckets it reuses) and `Docs/EconomicAutomation.md`.

## Status (read first)

**Opt-in.** `HerdLabourEnabled` remains **false** (OpeningConfig, API default
and old saves). The current HV2 candidate uses retained capital value minus
slaughter value and local marginal field/pasture alternatives. Read
[HerdValuationValidation.md](HerdValuationValidation.md) for current mechanics,
tests, measurements and unresolved design decisions, and
[HerdValuationGrounding.md](HerdValuationGrounding.md) for pending grounding.

**Historical baseline below.** The following derivation and measurements
record the original coupling at `9494571`/`87f5a71`, including its established
farmer shortcut and herd-growth credit. Those valuation details are superseded
by the HV2 candidate. The target, breeding cap, seed floor, slaughter ledger,
action and save contracts continue to apply.

## Engineering doctrine header

| Question | Answer |
| --- | --- |
| Authoritative state | Per camp `Camp.HerdTargetSet` / `Camp.HerdTarget` (the chosen herd size; unset means no slaughter), carried on `EconomicIntent` and saved as optional fields; per herd the animal count; per camp the Animal Food store. Config: `HerdLabourEnabled` (lever; saves without it load false) and `MinimumHerdHerders` (seed herd). The herder ladder, its prices and the grown-herd forecast are transient planner values: never saved, observed or hashed. |
| Temporal abstraction | Turn boundaries stay authoritative. The planner fixes the target at preparation from the committed world; the resolver slaughters once per turn, after breeding and before the growth plan, in the same managed-herd step. Rates are per year; a turn integrates them once. Nothing depends on `dt` except through the existing turn horizon (`years`). |
| Computational scaling | Planner: one ladder per automated husbandry camp per plan, `O(staffed herders)` (at most the residents) plus one grown-herd forecast that cultivating camps already paid for (husbandry-only camps pay one extra). Resolver: `O(herds of the camp)`. No map scan, no allocation per tile, no work per elapsed year. |
| Mature-game scale | Cost follows husbandry camps, herds and residents. The herder ladder is bounded by the residents, so a 2,000-person camp builds at most 2,000 steps once per plan; slaughter touches only herds above their target. The five-world Standard/80 sweep below measured median turn time within noise of the baseline (286 vs 275 ms fission off, 380 vs 456 ms fission on, 304 vs 306 ms mortality off; the smaller fission-on empire is faster). |
| RL implications | The target is a plain integer on the owner's economic intent, returned in the observation's economy block and settable through the existing Economy order and its mask. The AI policy (the planner) is deterministic and sets it from observable state. The current overall observation schema is 20; this feature did not change it. |
| Validation | `Tools/Headless/HerdLabourChecks.cs` (closed forms, slaughter ledgers, planner, trap fixture, healthy fixture, old saves, replay), the full focused suite against `TestResults/Distress/verify-new.txt`, and the five-world sweep below. |

## Root cause

Managed herds grew on forage and `ManagedBreedingIntensity` alone (`reproductionTarget` uses nutrition, never labour), animals never left a herd except by starvation, and the planner reserved `HerdingTooling.RequiredHerders` before any other claim (`EconomicPlanner.ManagedHerderReservation`). With QoL mortality on (a = 1), a camp below `Q*` stops growing, its herd keeps growing, and the herders the herd needs take every resident: 44 of 106 husbandry camps (156 of 1,153 with fission on) ended with every resident herding, no Animal Fabric, no housing and QoL 0, at up to 50 animals per person (the mortality-off run: 0.2-0.7 animals per person, herder share at most 0.26).

## What was found (how the three pieces worked before)

- **Herd growth.** `ResolveManagedHerds`: a fully nourished herd (`LastNutrition >= 1`) grows to `floor((animals + remainder) * exp(GrowthRate * breeding * years * nutrition))`; below full nutrition breeding progress pauses and, after the grace turns, starvation removes animals. Breeding is the only lever (`ManagedBreedingPolicy`, 0.45 / 0.55 / 0.65 from nutrition, loss, management, herder burden and pasture room). Nothing removed animals for any other reason.
- **ManagementFulfillment.** `HerdingTooling.Plan` turns the camp's assigned Herders into `Capacity = covered * StoneHerdingToolAnimalsPerHerder + uncovered * AnimalsPerHerder` and `Fulfillment = min(1, Capacity / animals)`. It scales a herd's food yield (`ManagedAnimalFoodYield`, `AnimalManagementExponent` 1) and nothing else: an under-managed herd still breeds and starves only on forage.
- **Herder reservation.** `CampProductionDemand.Build` set `ReservedHerders = RequiredHerders(all animals)` and every `Evaluate(s)` in the fulfilment search put exactly that many Herders into the proposal before crafts, food or fields, so a large herd consumed the residents and starved everything the search could price.

## The rule

```
animals per herder, with tools covering c herders:  managed(h) = min(h, c) * S + (h - min(h, c)) * A     (HerdingTooling.AnimalsManagedBy)
                                                    S = StoneHerdingToolAnimalsPerHerder, A = AnimalsPerHerder, c = tools * coverage years / years
step k of the herd ladder (the k-th herder):        animals Delta_k = managed(k) - managed(k-1), food f_k = Delta_k * n    n = net food per animal-year
                                                    n = (grown herd's yield - the human forage it displaces) / years
break-even price of step k:                         p_k = bucket_k / (1 - L_k)           (+infinity when L_k >= 1), then a running minimum
                                                    bucket_k = value of f_k filled into the food buckets after the free forage supply S0
                                                    L_k = min(f_k, demand - food before k) / altRate     gatherer- or farmer-years the herd food saves per herder-year
staffed herders at labour price w:                  max(MinimumHerdHerders, #{k : p_k >= w}),  w = the Value path's price at s (infinite at s <= 0, 0 at s >= 1)
H_target                                            floor(min(grown herd, managed(staffed herders)))
```

- **Value basis.** Buckets are those of `ExpansionValuation`: consumption at `v_food = N Q / (F D_year)`, the Animal Fabric and Light Housing feedstock the herd's food can fund at each claim's marginal value, operating-reserve refill at `FoodRiskWeight * v_food`, and growth at `v_growth`. The herd-growth credit is the newborns of the grown herd, who count as animals kept. `altRate` is a gatherer's food per year (`GatherPerPerson * DaysPerYear / BaseDays`), or a farmer's once fields are established (times `FarmerProductivity`). Units: person-QoL-years per herder-year, the same as `w`.
- **Competing like any other claim.** `EconomicPlanner.Plan` no longer reserves herders first when the Value rule and `HerdLabourEnabled` apply: `Evaluate(s)` staffs `StaffedHerders(s)`, nondecreasing in `s`, so the existing monotone search finds the largest position the residents can staff, and the herders are part of that staffing. `Evaluate(0)` keeps only the seed herd, so the search's feasible-at-zero invariant holds. Uniform rule, lever off, and camps with no animals keep the old unconditional reservation and set no target.
- **Seed herd.** `MinimumHerdHerders` (1) keeps one herder's worth of animals managed whatever their value, so a herd is never slaughtered to nothing and can regrow.
- **Never above the pasture.** The ladder stops at the grown herd, which the planner's forecast runs through the same forage and water solver as the resolver, so a pasture that cannot carry the herd already lowers it. Breeding stays paused below full nutrition.
- **Breeding cap.** After the target is chosen the planner lowers the breeding intensity (never raises it, bisection on the floored expected herd) so the herd expected at the end of the turn does not exceed the target. Intensity is then the only lever and slaughter takes what remains (for instance a camp that lost herders this turn). `ManagedBreedingPolicy` itself is unchanged: its recommendation is the cap's input.
- **Slaughter.** `HerdLabour.Slaughter`, called from `ResolveManagedHerds` after each camp's breeding: if `HerdTargetSet`, animals above `HerdTarget` are removed from the camp's herds, floors of the proportional shares first, the remainder by largest fractional remainder, ties to the lower herd id. Each animal yields `SpeciesDefinition.FoodYield` food units (the hunting carcass value, 60-150, without the hunters' practice multiplier), credited to `Camp.AnimalFood` and `StepFoodIncome`, recorded as a `Slaughter` transfer (no tile claim, no ecology pressure), in `ManagedHerdReport.Slaughtered` / `SlaughterFood` and in `DemographicReport.Slaughtered` / `SlaughterFood`, and counted in `FoodProduced` so the report's own food balance holds. A carcass the storage cannot hold is discarded through the existing inventory-loss path. Animals before = animals after + slaughtered exactly.
- **Player control.** The target is a field of the `EconomicIntent` an Economy order carries (`HerdTargetSet`, `HerdTarget >= 0`, Husbandry required); `EconomicIntents.Apply` stores it on the camp; the owner's observation returns it in `ObservedSettlementDto.Economy`. A camp on manual economy keeps its last target. Fission daughters start with no target (`Fission.cs` copies breeding intensity only); the planner sets theirs on their first automated plan.
- **Old saves.** `HerdLabourEnabled` loads false and `HerdTargetSet` false, so a loaded game keeps the unconditional reservation and never slaughters. A negative stored target is rejected on load; an unset target loads as zero.

## Approximations

- The planner's food envelopes still use the full-herd managed-food forecast; a herd cut below it yields less food that turn (its management fulfilment falls), and the next plan sees the smaller herd.
- A camp whose committed QoL is 0 values food at 0 (`FoodValue` is proportional to Q), so the ladder keeps only herders that pay for themselves in saved labour or feedstock. This is what lets a trapped camp escape and also why a herd can be cut to the seed in a single turn.
- Herding is compared with gathering or farming at the planner's own rates (`GatherPerPerson`, `FarmerProductivity`); a herder at `AnimalsPerHerder = 2` brings in less food than a farmer, so once fields are established the ladder keeps only tool-covered or scarcity-valued herders. See the measured consequence below.
- The seed herd, the carcass value and the labour-saving term are documented levers or constants, not calibrated against any historical herd.

## Validation

`HerdLabourChecks.cs` (fixed suite, 984 assertions plus the save checks): bucket and ladder closed forms; staffing at a price and the seed floor; managed animals per herder with and without tools, cross-checked against `RequiredHerdersFor` and `HerdingTooling.Plan`; the proportional split (200 random cases and 32-bit extremes); breeding-cap bisection; slaughter count, split, carcass food, transfers, demographic ledger, newborn-after-breeding order, lever off / unset / above-herd cases; planner closed form (slack labour fully staffed and never slaughtered, scarce labour capped, lever off / Uniform / no animals unchanged); the trap fixture (23 people, 1,165 animals, mortality on, a = 1, QoL 0: control keeps every resident herding and the herd grows to 2,402 in four turns, treated falls to the seed herd with Builders, Weavers and Farmers staffed and QoL rising); a healthy camp (100 people, 40 animals) untouched; save round trips, old-save, negative-target rejection; scalar versus reversed 4-worker replay. The focused suite (seeds 20, as the reference) matches `TestResults/Distress/verify-new.txt` line for line apart from the new check and one benchmark line: the seeded opening benchmark's `netGrowth` moves 4984 to 5032 (+1.0%; survivors, starvation deaths, illegal orders and technology counts unchanged) because opening-turn husbandry camps now staff herders by price (two legacy fixtures that pinned the unconditional reservation now set `HerdLabourEnabled = false`: `FarmPlannerChecks` "managed-herd planner reserves the resident-bounded Herder envelope" and `PastoralCraftChecks` "organic bootstrap produces Herding Tools", whose 150-animal herd is now rightly cut).

## Measurement

Five Standard/80 worlds (Continents 1, 2, 3, Pangaea 1, Archipelagos 1), 12 opening + 50 post-opening turns, `--details=1`, harness `TestResults/HerdLabour/bin` built from the commit this work started on plus only these changes, at most two runs at once. Baselines are `TestResults/QoLM/runs` `a1_f0`, `a1_f1` and `head_f0`. With `--herd-labour=0` the harness reproduces `a1_f0` exactly (Continents 1: population 46,925, food 3,799,771.920). Reduction: `Tools/PerformanceAudit/Summarize-HerdLabour.py` and `Summarize-Cohesion.py`. Pooled over the five worlds; camp-turn columns are post-opening turns 10-50. A husbandry camp holds managed animals at the final turn; trapped means every resident herds and QoL is at or below `Q*`.

| run | population (turn 60) | food yield | husbandry camps | all residents herding | trapped (Q <= Q*) | animals slaughtered | slaughter food / animal food | median turn ms |
|---|---|---|---|---|---|---|---|---|
| a1_f0 (mortality a=1, fission off, before) | 232,619 | 1.88e7 | 106 | 44 | 44 | 0 | 0 | 275 |
| hl1_f0 (after) | 248,832 | 2.02e7 | 105 | 0 | 0 | 3,110 | 8.1% | 286 |
| a1_f1 (fission on, before) | 978,004 | 8.09e7 | 1,144 | 156 | 156 | 0 | 0 | 456 |
| hl1_f1 (after) | 662,153 | 5.50e7 | 118 | 0 | 0 | 3,045 | 8.8% | 380 |
| head_f0 (mortality off, before) | 424,825 | 3.40e7 | 106 | 0 | 0 | 0 | 0 | 306 |
| hl0_f0 (after) | 360,715 | 2.89e7 | 105 | 0 | 0 | 8,099 | 7.3% | 304 |

Animals per person and herder share of residents (P10/P25/median/P75/P90, husbandry camps, camp-turns 10-50):

| run | animals per person | herder share |
|---|---|---|
| a1_f0 | 0.31/0.65/1.22/4.52/26.44 | 0.12/0.24/0.46/1.00/1.00 |
| hl1_f0 | 0.00/0.00/0.01/0.02/0.06 | 0.00/0.00/0.00/0.01/0.04 |
| a1_f1 | 0.30/0.50/0.82/1.44/3.23 | 0.11/0.18/0.29/0.51/1.00 |
| hl1_f1 | 0.00/0.00/0.01/0.02/0.07 | 0.00/0.00/0.00/0.01/0.04 |
| head_f0 | 0.19/0.30/0.44/0.63/0.85 | 0.03/0.10/0.16/0.23/0.30 |
| hl0_f0 | 0.00/0.00/0.01/0.02/0.11 | 0.00/0.00/0.00/0.01/0.05 |

Animal Fabric and Light Housing made (camp-turns 10-50), final Animal Fabric stock and people sheltered in tents (final turn):

| run | Animal Fabric made | Light Housing made | Animal Fabric stock | tent shelter people |
|---|---|---|---|---|
| a1_f0 | 1,522,079 | 40,676 | 34,369 | 24,884 |
| hl1_f0 | 1,047,264 | 43,473 | 14,653 | 33,792 |
| a1_f1 | 8,769,849 | 272,096 | 454,631 | 294,811 |
| hl1_f1 | 1,051,034 | 51,381 | 15,935 | 40,857 |
| head_f0 | 1,438,113 | 80,753 | 17,745 | 37,342 |
| hl0_f0 | 1,126,457 | 52,147 | 19,719 | 32,246 |

QoL P25/median/P75 by first technology (camp-turns 10-50), and final-turn residents by first technology:

| run | Husbandry QoL | Cultivation QoL | Watercraft QoL | residents Cultivation / Husbandry / Watercraft |
|---|---|---|---|---|
| a1_f0 | 0.09/0.67/0.76 | 0.76/0.88/0.97 | 0.78/0.88/0.97 | 171,500 / 47,979 / 6,818 |
| hl1_f0 | 0.71/0.84/0.94 | 0.76/0.88/0.97 | 0.78/0.88/0.97 | 171,494 / 64,227 / 6,818 |
| a1_f1 | 0.70/0.82/0.92 | 0.77/0.88/0.95 | 0.81/0.94/0.99 | 358,858 / 595,319 / 16,978 |
| hl1_f1 | 0.77/0.89/0.95 | 0.77/0.88/0.95 | 0.81/0.94/0.99 | 358,918 / 279,414 / 16,978 |
| head_f0 | 0.17/0.44/0.78 | 0.66/0.84/0.95 | 0.78/0.89/0.97 | 235,278 / 175,843 / 7,359 |
| hl0_f0 | 0.52/0.74/0.89 | 0.66/0.84/0.95 | 0.75/0.86/0.97 | 235,035 / 112,028 / 7,322 |

Camp-size P90/P95 by housing class (targets 100 / 250 / 600-1,000; score is the relative distance outside the band, lower is better):

| run | unhoused | tent-dominant | fixed-dominant | median size u/t/f | score |
|---|---|---|---|---|---|
| a1_f0 | 53/68 | 692/712 | 865/941 | 25/605/393 | 3.62 |
| hl1_f0 | 52/60 | 589/621 | 834/932 | 25/291/421 | 2.84 |
| a1_f1 | 94/222 | 666/674 | 737/787 | 27/385/445 | 4.58 |
| hl1_f1 | 104/227 | 363/413 | 712/810 | 28/197/407 | 2.41 |
| head_f0 | 65/84 | 1699/1942 | 1552/1922 | 27/720/685 | 14.04 |
| hl0_f0 | 64/81 | 670/843 | 1351/1466 | 27/299/636 | 4.87 |

On top of the hinterland/relative-destination fission work (`TestResults/Hinterland`, fission on, mortality a = 1; its lever-off control reproduces that run exactly, Continents 1 population 382,255): `h_a1` against `hb_a1` (this change, same flags). Population 2,085,151 to 902,292, husbandry camps 3,342 to 119, all-residents-herding camps 315 to 0, animals per person median 0.70 to 0.00 (camp-turns 0.79 to 0.01), herder share median 0.26 to 0.00, husbandry QoL median 0.88 to 0.89, camp-size score 5.49 to 3.50 (tent-dominant P90 630 to 347), median turn time 571 to 395 ms, animals slaughtered 2,983 (8.6% of animal food).

Reading the results:

- **The trap is gone.** No camp ends with every resident herding (44 of 106 and 156 of 1,144 before; 315 of 3,342 on top of the hinterland work), husbandry-camp QoL rises (median 0.67 to 0.84 fission off, 0.82 to 0.89 on) and the husbandry-first population rises 34% fission off. With fission on, the spiral that multiplied husbandry camps from 106 to 1,144 no longer happens (118 camps), so total population falls 32% to a smaller, better-housed empire (camp-size score 4.58 to 2.41).
- **Herds are now small once a camp holds fields.** The median husbandry camp keeps 0.01 animals per person: a camp with established fields staffs only the seed herd (see Approximations), so husbandry stops being a food source after cultivation begins. With mortality off, where no trap existed, population falls 15% (425k to 361k) and the husbandry-first population 36%, the cost of removing the herder subsidy. Camp sizes move toward the design targets (tent-dominant P90 1,699 to 670, score 14.0 to 4.9) because tent-dominant camps were the ones living on herds. This is the Value rule's own price (a herder at `AnimalsPerHerder = 2` brings in less food than a farmer), so the levers that reverse it are `AnimalsPerHerder`, the herding-tool coverage or `HerdLabourEnabled = false`, not a change to this mechanism.
- Animal Fabric made falls 31% (fission off) because the herds that fed it are smaller; Light Housing made and tent shelter rise.

## Rejected architectures

- Keeping the herder-first reservation and only trimming it when it exceeds the residents: the herd would still outgrow the people one turn at a time, and the price of herder labour would stay invisible to every other claim.
- A QoL- or starvation-triggered slaughter rule: the same loop would re-form whenever QoL recovered; the target must follow the value of the herd, not a symptom.
- Probabilistic or per-animal slaughter: breaks determinism and collection-order independence for no gain; the split is integer, proportional and ordered by herd id.
- A global herd cap: arbitrary, and unrelated to labour (`AGENTS.md` forbids arbitrary population caps; the same reasoning applies).
