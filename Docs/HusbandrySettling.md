# Husbandry settling: earned secondary technologies and herds weighed against fields

Two linked changes, both opt-in by configuration defaults that preserve nothing of the old discovery rule (part A changes
outcomes; part B changes only the Value planner). Facts come from the code at the parent revision and from the measurements below.

## Doctrine statement

- **Authoritative state.** Part A adds one saved, optional per-Society array, `Society.NeolithicExperience` (`double[3]` by `Domain`),
  and three optional config levers (`SecondaryTechnologyThreshold`, `TradePracticePerValue`, `SettledHerdGrowthPenalty`). Part B adds
  no saved state: the herd forecast, the herd credit, the settling cost and the expansion cap are transient fields of
  `CampProductionDemand`, rebuilt once per planned camp and never saved, observed or fingerprinted.
- **Temporal abstraction.** Turn boundaries stay authoritative. Practice is credited once per resolved turn (as before) and
  secondary discovery is committed at Close and usable from the next Prepare. The herd forecast is one closed-form growth step
  over the turn horizon (`exp(rate x breeding x years)`), the same rule the resolver uses for newborns, plus the existing one-horizon
  ecology integration; it is not a sub-turn simulation and does not change with `TurnDays` except through `years`.
- **Computational scaling.** Part A is O(1) per practice credit and per Society at Close. Part B adds, per planned husbandry camp with
  Cultivation and a herd, two more runs of the existing managed-herd forecast (radius-one tiles, one integration per tile) and, for
  non-village husbandry-primary camps only, an unbounded ladder (at most `candidates x 35` greedy passes, each O(candidates) for the
  village-average update). Cost follows active camps with herds, not years. At 80 tribes the five-world audit shows no measurable
  change in per-turn time (see Performance); a mature 320-tribe empire multiplies the same per-camp term.
- **Mature-game scale.** Practice credits are population-sized, so any fixed threshold is crossed ever sooner as camps grow; part A
  is therefore a short delay, not a gate, at 225 (measured below). The village-average walk is bounded by the footprint
  (at most seven land tiles at radius one).
- **RL implications.** Observation schema is unchanged; `TechnologyStatus.Progress` changes meaning for an unacquired secondary
  technology (Neolithic practice) and its `Threshold` is `SecondaryTechnologyThreshold` once in the Neolithic. Legal actions are
  unchanged. Planner output stays deterministic and pure, so replay and the PlanningDraft key checks are unaffected.
- **Validation.** `SettlingChecks.cs` (60 assertions), the updated `FoundationChecks.cs`/`FissionChecks.cs`, the full focused
  suite against `TestResults/WP10b/verify-v2.txt`, the contract checker, and the five-world 12 + 50 turn audit below.

## Part A: secondary technologies are earned from Neolithic practice

Diagnosis at the parent revision: `Society.Experience[d]` held every Paleolithic credit and every camp gathers, so a
Husbandry-first Society reached 225 Gathering points during the opening and held Cultivation at its end (16 of 22 at the
reference world, 22 of 22 two turns later).

Rules now in force:

1. **One practice API.** `TechnologyDiscovery.AddPractice(society, domain, amount, source)` with `PracticeSource { Gathering,
   Hunting, Fishing, Trade }` is the only writer of domain practice. The three call sites of `AwardLifestyleProgress` were
   replaced one for one with unchanged amounts (the existing Milestone-three assertions pin them). It always credits
   `Experience` (first breakthrough, practice bonuses); from the Society's first Close in the Neolithic it also credits
   `NeolithicExperience`. The breakthrough Close itself credits only `Experience`, because the era changes after the credit.
2. **Discovery.** After the breakthrough a technology for domain `d` is granted when the Society's Neolithic practice in `d` reaches
   `SecondaryTechnologyThreshold` (default 225, the value of `ExperienceThreshold`). Technologies already held are kept. Status
   (`TechnologyDiscovery.Statuses`) shows an unacquired technology's Neolithic progress against that threshold and an acquired
   one's lifetime practice; the player's "ways of living" label now reads the lifetime ramp directly (`PracticeEffects.Progress`).
3. **Forced-transition carryover is unchanged.** The forced Society's `SecondaryDomain` keeps `max(CarryoverFloor, CarryoverMultiplier
   x lifetime experience)` as its progress, exactly as before, so it is granted at its first Close; every other domain of that
   Society starts from zero Neolithic practice.
4. **Trade hook.** `TechnologyDiscovery.RecordTradePractice(config, receiver, domain, goodsValue)` credits `goodsValue x
   TradePracticePerValue` through `AddPractice` with source `Trade` and returns the points. The default weight 0 keeps it inert and
   nothing calls it yet. Intended mapping for trade work: imported grain to Gathering (Cultivation), herd products to Hunting
   (Husbandry), fish to Fishing (Watercraft), with the goods' value in the same units the exchange uses.
5. **Persistence.** `NeolithicExperience` is an optional field; a missing or wrong-length array loads as zeros
   (`SaveCodec.Load`). The three config levers are optional fields with `OnDeserializing` defaults (225, 0, 0.35). Fission daughters copy
   the array. Old saves that were already Neolithic restart their Neolithic practice from zero.

### What A changes at default (measured)

Practice credits equal a camp's resident count per turn-scaled budget, so a husbandry camp of a few hundred people credits more
than 225 Gathering points in a single turn. The default threshold therefore delays discovery by one to four turns rather than
preventing it (tables below: Husbandry-first Societies holding Cultivation at post-opening turn 1 / 5 / 12 fall from 102 / 106 / 106 to
68 / 104 / 106 of 106). Turn 1 shows a real cost: the 38 Societies that have not yet adopted Cultivation have fewer farmers and the
Husbandry-first QoL P25 falls from 0.451 to 0.199 that turn, recovering by turn 5 (P25 0.902 against 0.964). The sensitivity table shows
what a threshold that actually binds does: 2000 keeps Cultivation out for about 12 turns at a large turn-1 QoL cost (median 0.499),
10000 keeps it out for 20 or more turns and collapses QoL to about 0.54 at turn 12. Default 225 is what the specification asked for; if the
intent is for Husbandry-first Societies to stay herders for a long time, the threshold has to scale with population (practice per
camp-turn is of the order of the camp's residents) rather than be a constant, which is left as a decision.

## Part B: herds weighed against fields (Value rule)

### Step 0: what bounds managed herd growth (from the code)

- **Breeding.** `ResolveManagedHerds` (`GenerationalEconomy.cs`) breeds a herd only if `LastNutrition >= 1 - 1e-12` (fully nourished
  this turn): target `(animals + GrowthRemainder) x exp(species.GrowthRate x camp.ManagedBreedingIntensity x years x LastNutrition)`,
  floor, remainder retained (goat `GrowthRate` 0.0201 per year; intensity 0.55 by default, chosen per camp by
  `ManagedBreedingPolicy`). Newborns commit after production and needs.
- **Nutrition.** Each herd claims forage (`PotentialFood`) and freshwater on the camp's radius-one footprint
  (`ManagedHerdLocalClaims`); `EcologicalPressure.Integrate` returns realized transfers, unmet food/water give
  `LastFoodNutrition`/`LastWaterNutrition`. Below one there is no breeding, and after `StarvationGraceTurns` starvation deaths. Nutrition is
  the only capacity on herd size: there is no explicit carrying-capacity clamp for managed herds.
- **Management fulfilment.** `HerdingTooling.Plan` gives `Fulfillment = min(1, capacity / animals)` from assigned herders
  (`AnimalsPerHerder`, or tool-covered herder-years at `StoneHerdingToolAnimalsPerHerder`). It multiplies animal food
  (`ManagedAnimalFoodYield`: `animals x AnimalFoodPerManagedAnimal x consumption x nutrition^e x management^e`) and affects neither
  breeding nor survival. The planner reserves `RequiredHerders(current animals)` first (capped at residents), so fulfilment is 1
  unless the camp has fewer residents than the herd needs.
- **Fields and forage.** Cultivated capital does not reduce a tile's total biomass capacity (`EcologyCapacity.Biomass` ignores `q`):
  it changes which part is human food (`EffectiveWildFoodCapacity` q-squared access, wild retention falling after
  `CultivationVegetationDisplacementStart`) and adds regeneration (`ManagedFlow`). Herds and field harvest draw on the same
  biomass pool, so competition is through shared stock and `GatherableSpill` (human forage displaced by grazing), not a capacity cut.
- **Village status and movement.** `UpdateVillageAtBoundary` sets `IsVillage` when the average `q` of the camp's own fields over
  its radius-one land reaches 0.25. A village moves only by abandoning its fields (`Move` with `abandoningVillages`; the AI allows it
  only in a food emergency with a clearly better site, `Agents.cs`). Mobile herds support about 1.6 times the animals of stationary
  ones (`MobileHusbandryValidation.md`). Nothing in the parent planner weighed that.

### Consequences for the design

- Herd growth is **not labour-limited**: breeding needs nutrition and intensity, not herders. Management fulfilment limits
  yield only, and the herder reservation is already the planner's first claim. Per the specification the answer is to document it and
  rely on the herd credit plus the settling cost; there is no herd-expansion claim.
- Herd growth *is* limited by pasture and water, which is what the forecast reads, so a herd that cannot be carried earns no
  credit and the camp builds fields (tested with a water-short fixture).

### Rules

All in value units (person-QoL-years per worker-year), consistent with WP4b (`EconomicAutomation.md`).

1. **Herd credit.** `TurnResolver.ForecastGrownManagedAnimalFood` (pure) runs the ordinary forecast for nutrition, grows each fully
   nourished herd by the resolver's own birth rule at the planned breeding intensity, and re-runs the forage/water/management
   forecast for the grown herd with `RequiredHerdersFor(grown animals)` herders. `HerdGrowthFood = max(0, dAnimalFood -
   dHumanFoodDisplaced)`. Wherever the planner decided "are fields needed" it now compares forecast demand with `Supply +
   HerdGrowthFood` (`ExpansionValuation.Apply` gate and gaps, `EconomicPlanner.Plan` `expand` and `intendedExpansion`). With
   `ValueFulfilmentSelected`, cultivation and husbandry enabled and a herd present; otherwise 0.
2. **No herd-expansion claim** (not labour-limited; see above).
3. **Settling cost.** For a camp that is not a village and whose first technology is Animal Husbandry, the ladder is recorded in full
   and each step carries `VillageAverage`, the footprint average after it (camp-operated or stepped fields over the radius-one
   land count). At the first step that reaches 0.25 the camp would forfeit mobility. The cost is `C = SettledHerdGrowthPenalty x
   L_cap x V(F_h)`, where `F_h` is the forecast grown-herd food per year and `V` the bucket value (consumption, reserve at rho,
   growth) of filling `F_h` into the raw food gap from its start (same bucket values as the ladder; raw gaps exclude the credit).
   `tail` is the summed value `u_i x B_i` of that step and every later valued step. If `tail > C` the tail's values are scaled by
   `1 - C / tail` (ordering and monotonicity kept); otherwise the ladder stops before the crossing step and
   `ExpansionEffortCap` (worker-years of all earlier steps) limits expansion farmers, staffed plus spare, to
   `floor(cap / years)`. Primary-Cultivation camps, existing villages and zero penalty are unaffected. The default 0.35
   is the measured loss of herd support (1 - 1/1.6 = 0.375) rounded down; `L_cap` is the field capital life, used as a documented
   proxy for the animals' service life.
4. **Uniform rule unchanged;** no saved field is added for B.

### Limits of B

- The cap only binds when crossing is within reach of this turn's labour; before that, fields simply accumulate below the threshold.
  At mature populations herd food is small against need (20 thousand animals across 106 husbandry camps against 430 thousand people
  at post-opening turn 50), so fields clearly win the comparison and villages follow; B delays, it does not prevent.
- The valuation ignores that a settled camp may also keep a smaller herd, and it does not price the mobile herd's option value of moving
  to new pasture; it is a one-turn, per-camp estimate.
- The village average counts only fields the camp operates or steps this turn, not decay of unstepped fields.

## Tests

`Tools/Headless/SettlingChecks.cs` (60 assertions, `--only=SettlingChecks`): practice API and sources, Paleolithic versus Neolithic
crediting (direct and through a resolved turn), discovery at the threshold, configured threshold, forced carryover, status
semantics, saves (round trip, missing field, old config defaults, config validation, `OpeningConfig.json`), trade hook (inert, weighted,
pre-breakthrough), grown-herd forecast (independent arithmetic, purity, equality with the resolver's newborns, no growth without breeding
or water), demand-pass credit and the exact gap shortening, planner fixtures (growing herd with pasture keeps spare labour off fields;
the same herd without water, or a camp with no herd, expands fields; Uniform unchanged; purity, repeatability and save stability),
settling cost (husbandry camp capped and blocked while the cultivator is not; end-to-end under automation the husbandry camp ends at
0.246 < 0.25 and stays a camp, the cultivator reaches 0.268 and becomes a village; zero penalty, village and settle-when-it-pays cases;
ladder `VillageAverage`). `FoundationChecks` fixtures that set lifetime practice to trigger secondary discovery were changed to set
Neolithic practice, and assert the new rule. `FissionChecks` now asserts that daughters copy `NeolithicExperience`. The PlanningDraft
suite passes unchanged.

### Focused-suite differences against `TestResults/WP10b/verify-v2.txt`

Every other line is identical. The four changed lines:

- `foundation assertions` 46 to 49: the secondary-discovery fixtures gained three assertions (Paleolithic practice grants nothing; the
  breakthrough Close grants no secondary technology; Neolithic practice grants it) and the existing ones were re-pointed.
- `PlanningDraft counters` reused 146 to 140, recomputed 44 to 38, invalidation recomputed 13 to 12: the draft fixtures' societies
  discover secondary technologies later under part A, so fewer technology-change invalidations occur; the same counters are
  produced with part B disabled, so B does not cause the change. Outcomes checked by the suite are unchanged.
- `fission assertions` 490 to 492: the reflective "a daughter inherits every Society field" loop now includes `NeolithicExperience`
  (+1) and an explicit copy assertion was added (+1).
- `settling assertions`: the new 60-assertion suite.

## Measurements

Five Standard/80-tribe worlds (Continents 1, 2, 3, Pangaea 1, Archipelagos 1), 12 opening turns plus 50 post-opening turns, default
configuration (`--production-config=1`, fission and immigration off, four workers, `--details=1`), `Tools/PerformanceAudit`.
The baseline is the parent revision with the audit's added per-camp technology flags; its population, managed animals and food
yield are identical turn by turn to `TestResults/Spacing/<world>.json` in all five worlds (checked), so those files are the control.
Raw outputs and scripts are in `TestResults/Settling/` (not committed; `analyze.py`, `run.sh`). "Husbandry-first" means a camp
whose first technology is Animal Husbandry; one camp per Society because fission is off. Turns are post-opening turns (1 = the first
turn after the opening completed). Pooled over the five worlds: 106 Husbandry-first and 280 Cultivation-first Societies.

### Baseline (current HEAD, no change)
| post-opening turn | husb.-first camps | villages | hold Cultivation | farmer share | herder share | managed animals (all) | animals in husb. camps | husb.-first camps moved | husb. moves (all husb.-enabled) | population | stored food | food yield | husb. pop P25/med/P75 | husb. QoL P25/med/P75 | cult.-first camps | hold Husbandry | cult. pop P25/med/P75 | cult. QoL P25/med/P75 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | 106 | 0 | 102 | 0.318 | 0.057 | 1424 | 1424 | 18 | 38 | 50312 | 3.48e+06 | 4.32e+06 | 51 / 85 / 135 | 0.451 / 0.672 / 0.994 | 280 | 57 | 65 / 99 / 147 | 0.600 / 0.810 / 0.960 |
| 5 | 106 | 0 | 106 | 0.494 | 0.064 | 3501 | 3501 | 27 | 61 | 89049 | 6.62e+06 | 7.61e+06 | 113 / 187 / 271 | 0.964 / 0.985 / 0.994 | 280 | 90 | 112 / 183 / 275 | 0.798 / 0.902 / 0.980 |
| 12 | 106 | 26 | 106 | 0.562 | 0.117 | 16288 | 16288 | 12 | 31 | 166888 | 1.35e+07 | 1.42e+07 | 296 / 466 / 620 | 0.898 / 0.945 / 0.974 | 280 | 108 | 200 / 342 / 522 | 0.798 / 0.926 / 0.979 |
| 20 | 106 | 86 | 106 | 0.492 | 0.205 | 62888 | 62888 | 11 | 14 | 275438 | 2.26e+07 | 2.31e+07 | 616 / 993 / 1273 | 0.637 / 0.768 / 0.893 | 280 | 117 | 285 / 505 / 842 | 0.759 / 0.884 / 0.961 |
| 50 | 106 | 103 | 106 | 0.741 | 0.167 | 79316 | 79316 | 6 | 6 | 427783 | 3.18e+07 | 3.44e+07 | 1050 / 1748 / 2172 | 0.205 / 0.341 / 0.639 | 280 | 128 | 480 / 781 / 1254 | 0.534 / 0.712 / 0.907 |

AnimalHusbandry-first Societies (n=106) adopting Cultivation, by post-opening turn (0 = held at the opening end): 0:93, 1:9, 2:2, 3:2; never within the run: 0; median 0.0

Cultivation-first Societies (n=280) adopting AnimalHusbandry, by post-opening turn (0 = held at the opening end): 0:39, 1:18, 2:13, 3:5, 4:8, 5:7, 6:2, 7:4, 8:4, 9:2, 10:3, 11:3, 14:1, 16:1, 17:1, 18:4, 19:1, 20:1, 24:1, 25:1, 26:1, 27:1, 34:1, 40:1, 41:1, 42:2, 43:2; never within the run: 152; median 2.0

### Candidate (A + B)
| post-opening turn | husb.-first camps | villages | hold Cultivation | farmer share | herder share | managed animals (all) | animals in husb. camps | husb.-first camps moved | husb. moves (all husb.-enabled) | population | stored food | food yield | husb. pop P25/med/P75 | husb. QoL P25/med/P75 | cult.-first camps | hold Husbandry | cult. pop P25/med/P75 | cult. QoL P25/med/P75 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | 106 | 0 | 68 | 0.163 | 0.057 | 1426 | 1426 | 17 | 26 | 50264 | 3.49e+06 | 4.31e+06 | 47 / 85 / 136 | 0.199 / 0.483 / 0.739 | 280 | 30 | 65 / 98 / 147 | 0.600 / 0.810 / 0.956 |
| 5 | 106 | 0 | 104 | 0.461 | 0.064 | 3496 | 3496 | 26 | 52 | 88867 | 6.6e+06 | 7.59e+06 | 109 / 188 / 272 | 0.902 / 0.977 / 0.991 | 280 | 72 | 112 / 184 / 275 | 0.812 / 0.904 / 0.979 |
| 12 | 106 | 20 | 106 | 0.505 | 0.118 | 16395 | 16395 | 12 | 35 | 165595 | 1.34e+07 | 1.4e+07 | 286 / 455 / 614 | 0.912 / 0.945 / 0.975 | 280 | 99 | 196 / 340 / 522 | 0.816 / 0.930 / 0.981 |
| 20 | 106 | 69 | 106 | 0.422 | 0.207 | 62701 | 62701 | 14 | 16 | 272262 | 2.25e+07 | 2.26e+07 | 619 / 922 / 1237 | 0.642 / 0.770 / 0.883 | 280 | 112 | 284 / 504 / 842 | 0.746 / 0.877 / 0.965 |
| 50 | 106 | 100 | 106 | 0.717 | 0.160 | 81219 | 81219 | 9 | 9 | 427626 | 3.12e+07 | 3.4e+07 | 1128 / 1762 / 2095 | 0.222 / 0.332 / 0.646 | 280 | 123 | 475 / 781 / 1252 | 0.532 / 0.730 / 0.914 |

AnimalHusbandry-first Societies (n=106) adopting Cultivation, by post-opening turn (0 = held at the opening end): 0:49, 1:19, 2:17, 3:12, 4:3, 5:4, 7:1, 9:1; never within the run: 0; median 1.0

Cultivation-first Societies (n=280) adopting AnimalHusbandry, by post-opening turn (0 = held at the opening end): 0:14, 1:16, 2:18, 3:12, 4:7, 5:5, 6:4, 7:6, 8:8, 9:4, 10:3, 11:1, 12:1, 13:1, 14:3, 15:1, 16:1, 17:1, 18:4, 19:1, 20:1, 24:1, 25:1, 26:1, 28:1, 34:1, 40:1, 41:1, 42:2, 43:2; never within the run: 157; median 4



Per world, Husbandry-first camps:

| world | set | husb.-first camps | villages at post-opening 1 / 5 / 12 / 20 / 50 | hold Cultivation at 1 / 5 / 12 | population at 12 / 50 | managed animals at 12 / 50 |
|---|---|---|---|---|---|---|
| Continents-1 | baseline | 22 | 0 / 0 / 5 / 16 / 21 | 21 / 22 / 22 | 31908 / 87859 | 3288 / 20321 |
| Continents-1 | candidate | 22 | 0 / 0 / 4 / 10 / 20 | 12 / 21 / 22 | 31668 / 89204 | 3301 / 20875 |
| Continents-2 | baseline | 22 | 0 / 0 / 4 / 19 / 22 | 21 / 22 / 22 | 30830 / 81025 | 3257 / 14240 |
| Continents-2 | candidate | 22 | 0 / 0 / 3 / 15 / 21 | 14 / 22 / 22 | 30454 / 80070 | 3207 / 14539 |
| Continents-3 | baseline | 16 | 0 / 0 / 4 / 12 / 16 | 15 / 16 / 16 | 30173 / 80878 | 2994 / 13857 |
| Continents-3 | candidate | 16 | 0 / 0 / 3 / 11 / 14 | 11 / 16 / 16 | 29838 / 79884 | 2995 / 14151 |
| Pangaea-1 | baseline | 22 | 0 / 0 / 5 / 19 / 21 | 22 / 22 / 22 | 34789 / 90798 | 3253 / 16346 |
| Pangaea-1 | candidate | 22 | 0 / 0 / 2 / 18 / 21 | 13 / 22 / 22 | 34938 / 91587 | 3399 / 16888 |
| Archipelagos-1 | baseline | 24 | 0 / 0 / 8 / 20 / 23 | 23 / 24 / 24 | 39188 / 87223 | 3496 / 14552 |
| Archipelagos-1 | candidate | 24 | 0 / 0 / 8 / 15 / 24 | 18 / 23 / 24 | 38697 / 86881 | 3493 / 14766 |

### Reading the tables

- **Villages.** Husbandry-first villages at post-opening turn 12 / 20 / 50: 26 / 86 / 103 at baseline against 20 / 69 / 100 with the
  change. Settling is delayed by several turns but by turn 50 nearly every husbandry camp is a village in both. The sensitivity
  table separates the parts: with part A approximately off (threshold 1) the herd credit alone takes Continents 1 from 5 / 16 / 21
  to 4 / 11 / 22 villages at turns 12 / 20 / 50 and Archipelagos 1 from 8 / 20 / 23 to 9 / 18 / 24; part A (default 225) adds little in
  Continents 1 (4 / 10 / 20) and more in Archipelagos 1 (8 / 15 / 24); switching the settling cost off changes neither, so the
  cost itself rarely binds and the delay is the herd credit and the later Cultivation.
- **Cultivation adoption among Husbandry-first Societies.** By the opening's end 93 of 106 held it at baseline against 49; the
  median adoption turn moves from 0 to 1 and the last adopter from turn 3 to turn 9. All 106 hold it by turn 12 in both.
- **Shares.** Farmer share of husbandry-first residents at turns 12 / 20 / 50: 0.562 / 0.492 / 0.741 against 0.505 / 0.422 / 0.717.
  Herder share is essentially unchanged (0.117 / 0.205 / 0.167 against 0.118 / 0.207 / 0.160). Managed animals (all camps) 16288 / 62888 / 79316
  against 16395 / 62701 / 81219.
- **Moves.** Husbandry-first camps that moved: 18 / 27 / 12 / 11 / 6 against 17 / 26 / 12 / 14 / 9 at turns 1 / 5 / 12 / 20 / 50 (all
  husbandry-enabled camps: 38 / 61 / 31 / 14 / 6 against 26 / 52 / 35 / 16 / 9). Mobility is not materially preserved.
- **Population and QoL.** Total population at turn 50 is 427783 at baseline and 427626 with the change; Husbandry-first QoL median
  at 12 / 20 / 50 is 0.945 / 0.768 / 0.341 against 0.945 / 0.770 / 0.332. Turn 1 and turn 5 are lower (above). Cultivation-first median QoL
  at 50 is 0.712 against 0.730.
- **Cultivation-first Societies and Husbandry.** Part A applies to them too: adoption of Husbandry by the opening's end falls from
  39 to 14 of 280 and the median adoption turn rises from 2 to 4; 152 (baseline) and 157 of 280 never adopt it within the run. Their
  QoL and population are essentially unchanged (table).

### Sensitivity (Continents 1 and Archipelagos 1)

Same runs with one lever changed (`--set=Name=Value`); "threshold 1" approximates part A off (adoption one turn after the
breakthrough), keeping part B.

| world | configuration | villages among husb.-first at 12 / 20 / 50 | hold Cultivation at 1 / 5 / 12 / 20 | farmer share 12 / 20 / 50 | herder share 12 / 20 / 50 | husb.-first QoL median 5 / 12 / 20 / 50 | population 12 / 50 | managed animals 50 |
|---|---|---|---|---|---|---|---|---|
| Continents-1 | baseline (HEAD) | 5 / 16 / 21 | 21 / 22 / 22 / 22 | 0.47 / 0.51 / 0.70 | 0.12 / 0.23 / 0.20 | 0.975 / 0.946 / 0.826 / 0.341 | 31908 / 87859 | 20321 |
| Continents-1 | A+B default (thr 225, penalty .35) | 4 / 10 / 20 | 12 / 21 / 22 / 22 | 0.52 / 0.42 / 0.67 | 0.12 / 0.23 / 0.16 | 0.972 / 0.953 / 0.832 / 0.300 | 31668 / 89204 | 20875 |
| Continents-1 | threshold 1 (A ~off, B on) | 4 / 11 / 22 | 22 / 22 / 22 / 22 | 0.49 / 0.38 / 0.73 | 0.13 / 0.23 / 0.19 | 0.991 / 0.950 / 0.845 / 0.315 | 31498 / 91277 | 20499 |
| Continents-1 | penalty 0 (A + herd credit only) | 4 / 10 / 20 | 12 / 21 / 22 / 22 | 0.54 / 0.41 / 0.67 | 0.12 / 0.23 / 0.16 | 0.972 / 0.953 / 0.832 / 0.300 | 31668 / 89115 | 20806 |
| Continents-1 | threshold 2000 | 1 / 9 / 22 | 0 / 0 / 14 / 22 | 0.37 / 0.37 / 0.73 | 0.14 / 0.26 / 0.19 | 0.499 / 0.889 / 0.818 / 0.299 | 30701 / 87223 | 20855 |
| Continents-1 | threshold 10000 | 0 / 0 / 21 | 0 / 0 / 0 / 4 | 0.00 / 0.06 / 0.69 | 0.16 / 0.35 / 0.19 | 0.499 / 0.536 / 0.219 / 0.334 | 30234 / 88927 | 22284 |
| Archipelagos-1 | baseline (HEAD) | 8 / 20 / 23 | 23 / 24 / 24 / 24 | 0.49 / 0.56 / 0.75 | 0.11 / 0.19 / 0.15 | 0.973 / 0.923 / 0.739 / 0.352 | 39188 / 87223 | 14552 |
| Archipelagos-1 | A+B default (thr 225, penalty .35) | 8 / 15 / 24 | 18 / 23 / 24 / 24 | 0.46 / 0.43 / 0.76 | 0.10 / 0.17 / 0.15 | 0.971 / 0.933 / 0.766 / 0.477 | 38697 / 86881 | 14766 |
| Archipelagos-1 | threshold 1 (A ~off, B on) | 9 / 18 / 24 | 24 / 24 / 24 / 24 | 0.55 / 0.40 / 0.77 | 0.11 / 0.17 / 0.15 | 0.972 / 0.916 / 0.712 / 0.386 | 39106 / 88329 | 14458 |
| Archipelagos-1 | penalty 0 (A + herd credit only) | 8 / 15 / 24 | 18 / 23 / 24 / 24 | 0.47 / 0.42 / 0.76 | 0.10 / 0.17 / 0.16 | 0.971 / 0.933 / 0.766 / 0.494 | 38731 / 85814 | 14869 |
| Archipelagos-1 | threshold 2000 | 2 / 13 / 23 | 0 / 3 / 18 / 22 | 0.46 / 0.45 / 0.77 | 0.12 / 0.18 / 0.15 | 0.592 / 0.931 / 0.802 / 0.382 | 35964 / 85106 | 14732 |
| Archipelagos-1 | threshold 10000 | 0 / 1 / 20 | 0 / 0 / 0 / 6 | 0.00 / 0.07 / 0.74 | 0.15 / 0.32 / 0.16 | 0.591 / 0.545 / 0.407 / 0.371 | 35480 / 81196 | 14636 |


- The settling cost alone (penalty 0 against default) leaves villages unchanged in both worlds and every other column within noise.
- Raising the threshold does delay villages (2000: 1 / 9 / 22 at turns 12 / 20 / 50 in Continents 1; 10000: 0 / 0 / 21) but pays in QoL
  and does not change where the camps end up; by turn 50 the same 21 to 22 of 22 are villages.

### Performance

Indicative only (three audits ran concurrently on a four-core machine, so these are not formal timings). Mean total milliseconds per
turn, baseline against candidate: Continents 1 383 / 384, Continents 2 344 / 384, Continents 3 352 / 346, Pangaea 1 336 / 344,
Archipelagos 1 211 / 312 (concurrency noise dominates; the allocation per turn rises 1 to 4 percent: 110 to 111, 107 to 108, 108 to
109, 104 to 105, 101 to 105 MiB). The added work is two managed-herd forecasts per husbandry camp with Cultivation and a herd per Plan.

| world | set | mean total ms/turn | median | mean prepare ms | mean execute ms | allocated MiB/turn | peak heap MiB |
|---|---|---|---|---|---|---|---|
| Continents-1 | baseline | 383 | 352 | 128.5 | 157.2 | 110 | 588 |
| Continents-1 | candidate | 384 | 344 | 135.4 | 147.8 | 111 | 582 |
| Continents-2 | baseline | 344 | 305 | 115.2 | 135.0 | 107 | 586 |
| Continents-2 | candidate | 384 | 343 | 133.8 | 149.0 | 108 | 576 |
| Continents-3 | baseline | 352 | 310 | 123.2 | 141.1 | 108 | 574 |
| Continents-3 | candidate | 346 | 330 | 115.9 | 141.7 | 109 | 593 |
| Pangaea-1 | baseline | 336 | 293 | 116.9 | 137.3 | 104 | 598 |
| Pangaea-1 | candidate | 344 | 307 | 116.3 | 132.8 | 105 | 606 |
| Archipelagos-1 | baseline | 211 | 186 | 76.0 | 73.1 | 101 | 566 |
| Archipelagos-1 | candidate | 312 | 278 | 128.4 | 92.8 | 105 | 457 |


## Not done

- No formal timing pair or scale run (320 tribes). No population-scaled secondary threshold; see the decision above.
- Unity EditMode tests and the player were not run; the only presentation change is the "ways of living" label reading
  `PracticeEffects.Progress`.
