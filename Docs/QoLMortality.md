# QoL-dependent mortality

Authoritative record of the rule that couples a camp's quality of life (QoL) to its growth ceiling, its calibration sweep and its limits.

## Engineering doctrine header

| Question | Answer |
| --- | --- |
| Authoritative state | `SimulationConfig.BaseBirthRate` (b0), `AnnualHumanGrowthRate` (g), `QoLMortalityElasticity` (a), `QoLMortalityEnabled`, and each camp's committed `Camp.QoL` from its last resolved needs. Nothing else is stored: the hook factors are pure functions and the demographic report fields are explanations. |
| Temporal abstraction | Annual rates. A turn still integrates growth analytically as `exp(r T)`; only the rate `r` changes. Turn boundaries stay authoritative; the QoL that sets `r` is the one the turn's planning sees (the previous turn's resolved QoL), never a sub-turn value. |
| Computational scaling | O(1) per camp per call: one `Math.Pow`, no scans, no allocation, no dependence on `dt`, map size, tile count or history. Four planner/resolver sites call the same helper once per camp per forecast. |
| Mature-game scale | Cost follows the camp count. Because growth is ceilinged by QoL, camps are smaller and fewer: the Standard/80 sweep's median turn time fell from 802 ms to 350 ms (fission on, turn 60: 149 camps against 550 per world). |
| RL implications | The ceiling is a deterministic function of observable camp state (QoL is already in the owner's observation); no hidden randomness or global scan. |
| Validation | `Tools/Headless/QoLMortalityChecks.cs` (closed forms, shared-site consistency, Paleolithic exemption, old saves, replay), the full focused suite, and the five-world Standard/80 sweep below. |

## Rule

```
d(Q) = d0 * MortalityFactor(society, camp) * Q^(-a)          Q floored at 1e-06; Q -> 0 gives r = 0
r    = max(0, b0 * FertilityFactor(society, camp) - d(Q))    annual net growth ceiling; food still limits growth as before
d0   = b0 - AnnualHumanGrowthRate                             so r(1) = AnnualHumanGrowthRate exactly
Q*   = (d0 / b0)^(1/a)                                        growth stops at and below Q* (an outcome, not a parameter)
```

- **Births are fixed; QoL raises deaths.** `b0` is `BaseBirthRate` (0.045 a year, a pre-modern crude birth rate and an external demographic anchor). Net growth is floored at zero: low QoL gives diminishing growth only. There is no reported non-starvation decline and the deaths implied by `d(Q)` are not recorded (the existing net-rate convention); starvation deaths are unchanged.
- **Food still limits growth.** `r` replaces `AnnualHumanGrowthRate` in every `maximumRate` computation: `TurnResolver.PlanGrowth`, `FoodBucketTargets` (the planning/forecast food buckets), `PlanningResidentPopulation` and the Value rule's `ExpansionValuation.FoodPerBirth`. They all call `QoLMortality.MaximumRate(w, camp)`, which also keeps `DemographicGrowthMultiplier` (it multiplies `r`) and the `DeficitTurns > 0` gives zero rule. Planner preview camps (`EconomicPlanner.Preview`) now carry the committed QoL so forecasts equal the resolver.
- **Which Q.** The camp's committed `Camp.QoL`, computed by `NeolithicProduction.ResolveNeeds` at the end of its last resolved turn: the same value the turn's planning sees.
- **Hooks for later eras.** `QoLMortality.MortalityFactor` and `FertilityFactor` return 1 now. Medicine, sanitation and vaccination multiply mortality (below 1); education, urbanisation and contraception multiply fertility (below 1). Per Society or camp, pure, deterministic, O(1), no new saved state. Checks assert that explicit factors multiply `d` and `b0` exactly.
- **Explanation.** `DemographicReport.QoLMortalityReduction` is `r(1) - r(Q)` (an annual rate) and `DemographicReport.QoL` the QoL used; the explanation gains "Quality of life 0.90 raised deaths and lowered the annual growth ceiling by 0.79 points." only when the reduction is positive. Both fields are optional (absent in older saves).

### Paleolithic handling

A Paleolithic camp has no goods economy, so its resolved QoL is capped at `1 - E` of the rudimentary term (about 0.75 at the expectation floor) times food and cohesion by construction (Docs/Fission.md, WP10a findings). That number says nothing about wellbeing, and even at the shipped `a = 1` the zero-growth QoL `Q*` = 0.746 is close enough to freeze rudimentary camps for an irrelevant proxy. The rule therefore applies only to camps with Cultivation or Animal Husbandry (`CultivationEconomy.EconomyEnabled`); every other camp, including a Watercraft-only camp, keeps `AnnualHumanGrowthRate` unchanged. The opening calibration (12-turn opening, 200/240-year fallbacks) is preserved, and a camp that has just unlocked the economy plans its first economy turn from the last Paleolithic QoL; it then gets a real QoL.

### Fission daughters

A newly founded daughter used to keep the unset `Camp.QoL = 1.0` until its first resolution (the artefact noted in Docs/Fission.md). `Fission.Found` now stores the estimate that justified the split (`Evaluation.DaughterQoL`, also in the fission report), falling back to the parent's committed QoL if no usable estimate exists. This also makes `MarginalWelfare` and the first-turn growth ceiling honest. The effect on the throughput tables: the "unhoused" class no longer reads 1.00/1.00/1.00 (stored-QoL artefact) but its real distribution.

### Old saves

`QoLMortalityEnabled` is an optional field that is **false by default** and **false when absent from a save** (the same convention as `FissionEnabled`/`ImmigrationEnabled`), so a loaded game continues with its original growth ceiling and its fingerprint continuation is unchanged. An old save that explicitly stores `QoLMortalityElasticity = 2` retains 2; an absent elasticity receives the current default but remains inert because the lever is off. Validation requires `0 < BaseBirthRate <= 0.2` and a positive finite elasticity, and when the lever is enabled `BaseBirthRate > AnnualHumanGrowthRate` (otherwise `d0 <= 0`). The save envelope `ANTHROPOCENE-11`, the filename and the observation schema are unchanged.

## Calibration sweep

Harness: `Tools/PerformanceAudit` built once (`TestResults/QoLM/bin`), HEAD 21e0ec6 built from its archived simulation sources with the same harness (`TestResults/QoLM/bin-head`; the `head_*` rows). Five Standard/80 worlds (Continents 1, 2, 3, Pangaea 1, Archipelagos 1), 12 opening + 50 post-opening turns, `--details=1 --metrics=1 --workers=1 --immigration=0`, three runs at a time on a 4-core machine (timings are therefore indicative). `a` via `--set=QoLMortalityElasticity=...`, `b0` via `--set=BaseBirthRate=...`, fission via `--fission=0/1`. Reductions: `Summarize-FissionThroughput.py`, `Summarize-Cohesion.py` conventions (camp-turn observations from post-opening turns 10..50, classes by actual housing, targets 100 / 250 / 600-1,000; score is the relative distance of P90/P95 outside the target band, lower is better) and `Summarize-QoLMortality.py` (population, food, blocked camp-turns, QoL by first technology). Raw runs are ignored under `TestResults/QoLM/runs`.

`a` is varied at `b0 = 0.045`; `b0` is varied at the best `a` (2). `a = 3` is an extra point between the grid values. `Q*` is `((b0 - g)/b0)^(1/a)` with `g = 0.01145`: a 0.5 -> 0.556, 1 -> 0.746, 2 -> 0.863, 3 -> 0.907, 4 -> 0.929 (b0 0.035 -> 0.820, b0 0.055 -> 0.890 at a = 2). The tag `aXbY` means elasticity X and birth rate Y; `head` is the previous behaviour.

### Fission on (the design case)

Per-world means for population and camps (`pop`, `camps` at turn 60); fissions are totals over five worlds; median turn time is whole-turn milliseconds.

| run | fissions | pop | camps | mean QoL | turn ms (median) |
|---|---|---|---|---|---|
| head_f1 | 2348 | 576093 | 550 | 0.715 | 802 |
| a0.5_f1 | 1798 | 308397 | 440 | 0.762 | 501 |
| a1_f1 | 1479 | 195601 | 376 | 0.786 | 456 |
| a2_f1 | 347 | 47595 | 149 | 0.698 | 350 |
| a3_f1 | 235 | 34676 | 127 | 0.701 | 327 |
| a4_f1 | 217 | 30250 | 123 | 0.703 | 331 |
| a2b0.035_f1 | 1084 | 123108 | 297 | 0.753 | 416 |
| a2b0.055_f1 | 268 | 38680 | 134 | 0.698 | 337 |

Camp size P90/P95 (n) by housing class; targets 100 / 250 / 600-1,000
| run | unhoused | tent-dominant | fixed-dominant | median size u/t/f | score |
|---|---|---|---|---|---|
| head_f1 | 226/246 (4601) | 1385/1749 (20359) | 1504/1828 (50770) | 33/430/678 | 14.59 |
| a0.5_f1 | 209/229 (5531) | 937/958 (17884) | 1006/1083 (32294) | 30/451/529 | 8.05 |
| a1_f1 | 94/222 (5644) | 666/674 (16274) | 737/787 (25190) | 27/385/445 | 4.58 |
| a2_f1 | 88/103 (6939) | 458/464 (6744) | 643/673 (14377) | 31/264/280 | 1.72 |
| a3_f1 | 81/95 (6861) | 377/393 (4783) | 526/548 (13759) | 31/208/250 | 1.29 |
| a4_f1 | 80/94 (6991) | 337/352 (4359) | 460/476 (13714) | 32/187/229 | 1.20 |
| a2b0.035_f1 | 101/161 (6513) | 542/552 (14047) | 657/736 (20038) | 31/330/378 | 3.00 |
| a2b0.055_f1 | 84/94 (6947) | 411/417 (5483) | 573/598 (13919) | 31/239/255 | 1.36 |

QoL P25/median/P75 per housing class
| run | unhoused | tent-dominant | fixed-dominant |
|---|---|---|---|
| head_f1 | 1.00/1.00/1.00 | 0.64/0.88/0.95 | 0.57/0.77/0.89 |
| a0.5_f1 | 0.00/0.14/0.87 | 0.58/0.85/0.94 | 0.68/0.82/0.92 |
| a1_f1 | 0.00/0.02/0.57 | 0.73/0.88/0.95 | 0.73/0.86/0.93 |
| a2_f1 | 0.00/0.02/0.04 | 0.35/0.83/0.89 | 0.82/0.89/0.97 |
| a3_f1 | 0.00/0.02/0.04 | 0.27/0.84/0.91 | 0.82/0.91/0.98 |
| a4_f1 | 0.00/0.02/0.04 | 0.24/0.84/0.92 | 0.83/0.93/0.98 |
| a2b0.035_f1 | 0.00/0.02/0.05 | 0.78/0.87/0.94 | 0.78/0.87/0.95 |
| a2b0.055_f1 | 0.00/0.02/0.04 | 0.31/0.85/0.90 | 0.82/0.90/0.98 |

| run | worlds | population | camps | food yield (turn 60) | fissions | human births | camp-turns blocked (Q <= Q*) | Q* |
|---|---|---|---|---|---|---|---|---|
| head_f1 | 5 | 2880466 | 2748 | 2.36e+08 | 2348 | 2872145 | n/a | - |
| a0.5_f1 | 5 | 1541986 | 2198 | 1.27e+08 | 1798 | 1532712 | 17.9% (9314/51992) | 0.556 |
| a1_f1 | 5 | 978004 | 1879 | 8.09e+07 | 1479 | 968610 | 29.9% (12960/43390) | 0.746 |
| a2_f1 | 5 | 237973 | 747 | 1.94e+07 | 347 | 228458 | 51.5% (12529/24342) | 0.863 |
| a3_f1 | 5 | 173379 | 635 | 1.41e+07 | 235 | 163847 | 57.9% (12549/21685) | 0.907 |
| a4_f1 | 5 | 151249 | 617 | 1.23e+07 | 217 | 141718 | 61.2% (13060/21346) | 0.929 |
| a2b0.035_f1 | 5 | 615542 | 1484 | 5.06e+07 | 1084 | 606035 | 40.6% (14976/36880) | 0.820 |
| a2b0.055_f1 | 5 | 193399 | 668 | 1.57e+07 | 268 | 183933 | 55.0% (12449/22631) | 0.890 |

QoL P25/median/P75 by first technology (n camp-turns), post-opening turns 10..50
| run | AnimalHusbandry | Cultivation | Watercraft |
|---|---|---|---|
| head_f1 | 0.41/0.77/0.91 (37632) | 0.66/0.80/0.91 (32414) | 0.78/0.90/0.98 (1905) |
| a0.5_f1 | 0.54/0.79/0.91 (26579) | 0.73/0.85/0.93 (23471) | 0.80/0.92/0.98 (1944) |
| a1_f1 | 0.70/0.82/0.92 (24216) | 0.77/0.88/0.95 (17324) | 0.81/0.94/0.99 (1852) |
| a2_f1 | 0.06/0.49/0.86 (10590) | 0.82/0.89/0.97 (11950) | 0.85/0.95/0.99 (1804) |
| a3_f1 | 0.04/0.30/0.87 (8107) | 0.83/0.91/0.97 (11776) | 0.85/0.95/0.99 (1804) |
| a4_f1 | 0.03/0.27/0.88 (7885) | 0.84/0.93/0.98 (11659) | 0.84/0.95/0.99 (1804) |
| a2b0.035_f1 | 0.53/0.82/0.92 (21528) | 0.81/0.89/0.97 (13523) | 0.82/0.94/0.99 (1831) |
| a2b0.055_f1 | 0.04/0.37/0.87 (9073) | 0.83/0.90/0.97 (11756) | 0.86/0.95/0.99 (1804) |

### Fission off

| run | fissions | pop | camps | mean QoL | turn ms (median) |
|---|---|---|---|---|---|
| head_f0 | 0 | 84965 | 80 | 0.707 | 306 |
| a0.5_f0 | 0 | 58350 | 80 | 0.730 | 278 |
| a1_f0 | 0 | 46524 | 80 | 0.751 | 275 |
| a2_f0 | 0 | 35114 | 80 | 0.766 | 266 |
| a3_f0 | 0 | 30147 | 80 | 0.769 | 260 |
| a4_f0 | 0 | 26989 | 80 | 0.776 | 275 |
| a2b0.035_f0 | 0 | 39339 | 80 | 0.757 | 279 |
| a2b0.055_f0 | 0 | 32032 | 80 | 0.767 | 274 |

Camp size P90/P95 (n) by housing class; targets 100 / 250 / 600-1,000
| run | unhoused | tent-dominant | fixed-dominant | median size u/t/f | score |
|---|---|---|---|---|---|
| head_f0 | 65/84 (3726) | 1699/1942 (1553) | 1552/1922 (14812) | 27/720/685 | 14.04 |
| a0.5_f0 | 54/67 (4847) | 971/1006 (2259) | 1079/1209 (12968) | 27/831/483 | 6.20 |
| a1_f0 | 53/68 (5082) | 692/712 (2548) | 865/941 (12445) | 25/605/393 | 3.62 |
| a2_f0 | 59/80 (5319) | 505/520 (2544) | 647/674 (12212) | 25/440/296 | 2.10 |
| a3_f0 | 58/112 (5380) | 414/433 (2538) | 527/549 (12157) | 25/367/271 | 1.71 |
| a4_f0 | 62/253 (5345) | 372/374 (2583) | 462/477 (12147) | 25/321/248 | 2.95 |
| a2b0.035_f0 | 55/87 (5226) | 586/598 (2637) | 744/781 (12212) | 25/505/321 | 2.74 |
| a2b0.055_f0 | 57/123 (5313) | 447/461 (2580) | 576/600 (12182) | 25/393/273 | 1.91 |

QoL P25/median/P75 per housing class
| run | unhoused | tent-dominant | fixed-dominant |
|---|---|---|---|
| head_f0 | 0.00/0.00/0.51 | 0.24/0.67/0.88 | 0.58/0.80/0.93 |
| a0.5_f0 | 0.00/0.00/0.15 | 0.47/0.56/0.81 | 0.70/0.86/0.96 |
| a1_f0 | 0.00/0.00/0.15 | 0.66/0.73/0.85 | 0.75/0.88/0.97 |
| a2_f0 | 0.00/0.00/0.05 | 0.75/0.83/0.87 | 0.82/0.89/0.97 |
| a3_f0 | 0.00/0.00/0.11 | 0.76/0.87/0.91 | 0.82/0.91/0.98 |
| a4_f0 | 0.00/0.00/0.10 | 0.76/0.89/0.92 | 0.84/0.93/0.98 |
| a2b0.035_f0 | 0.00/0.00/0.04 | 0.67/0.79/0.85 | 0.80/0.89/0.97 |
| a2b0.055_f0 | 0.00/0.00/0.08 | 0.74/0.85/0.89 | 0.83/0.90/0.97 |

| run | worlds | population | camps | food yield (turn 60) | fissions | human births | camp-turns blocked (Q <= Q*) | Q* |
|---|---|---|---|---|---|---|---|---|
| head_f0 | 5 | 424825 | 400 | 3.4e+07 | 0 | 417069 | n/a | - |
| a0.5_f0 | 5 | 291750 | 400 | 2.35e+07 | 0 | 282405 | 21.0% (3449/16400) | 0.556 |
| a1_f0 | 5 | 232619 | 400 | 1.88e+07 | 0 | 223129 | 33.4% (5476/16400) | 0.746 |
| a2_f0 | 5 | 175569 | 400 | 1.43e+07 | 0 | 166026 | 46.4% (7614/16400) | 0.863 |
| a3_f0 | 5 | 150733 | 400 | 1.23e+07 | 0 | 141176 | 54.0% (8853/16400) | 0.907 |
| a4_f0 | 5 | 134944 | 400 | 1.09e+07 | 0 | 125382 | 57.2% (9388/16400) | 0.929 |
| a2b0.035_f0 | 5 | 196697 | 400 | 1.59e+07 | 0 | 187162 | 41.2% (6754/16400) | 0.820 |
| a2b0.055_f0 | 5 | 160162 | 400 | 1.3e+07 | 0 | 150627 | 50.5% (8286/16400) | 0.890 |

QoL P25/median/P75 by first technology (n camp-turns), post-opening turns 10..50
| run | AnimalHusbandry | Cultivation | Watercraft |
|---|---|---|---|
| head_f0 | 0.17/0.44/0.78 (4346) | 0.66/0.84/0.95 (11480) | 0.78/0.89/0.97 (574) |
| a0.5_f0 | 0.26/0.52/0.77 (4346) | 0.70/0.86/0.96 (11480) | 0.78/0.87/0.97 (574) |
| a1_f0 | 0.09/0.67/0.76 (4346) | 0.76/0.88/0.97 (11480) | 0.78/0.88/0.97 (574) |
| a2_f0 | 0.02/0.63/0.85 (4346) | 0.82/0.89/0.97 (11480) | 0.81/0.91/0.97 (574) |
| a3_f0 | 0.02/0.59/0.88 (4346) | 0.83/0.91/0.98 (11480) | 0.79/0.93/0.98 (574) |
| a4_f0 | 0.03/0.59/0.91 (4346) | 0.84/0.93/0.98 (11480) | 0.82/0.92/0.97 (574) |
| a2b0.035_f0 | 0.02/0.64/0.82 (4346) | 0.80/0.88/0.97 (11480) | 0.82/0.90/0.97 (574) |
| a2b0.055_f0 | 0.02/0.60/0.87 (4346) | 0.82/0.90/0.97 (11480) | 0.82/0.91/0.98 (574) |

### Reading the tables

- **Size falls monotonically with the elasticity.** A larger `a` raises `Q*`, so the cohesion decline `S(N)` stops growth at a smaller camp. With fission on, the fixed-dominant P90 falls 1,504 -> 1,006 -> 737 -> 643 -> 526 -> 460 for a = 0.5, 1, 2, 3, 4 against the 600-1,000 band; tent-dominant 1,385 -> 937 -> 666 -> 458 -> 377 -> 337 against 250. Without fission the same ordering holds (fixed 1,552 -> 1,079 -> 865 -> 647 -> 527 -> 462).
- **Population falls with it.** At a = 2 and fission on, population is 48k per world against 576k and camps 149 against 550. Camps now plateau near their `Q*` size (fixed-dominant median 280) below their fission onset, so fissions drop from 2,348 to 347 and the camp count no longer compounds. Without fission population falls from 85k to 35k per world over the same 80 camps. Food yield falls in step (it follows people).
- **QoL.** Fixed-dominant median QoL rises 0.77 -> 0.89 and the Cultivation camps' P25 from 0.66 to 0.82 (fission on). Mean QoL does not rise at a >= 2 (0.698 against 0.715) because husbandry camps with no housing and poor food sit at the bottom of the distribution (P25 0.06, against 0.41 before; median 0.49): below `Q*` they cannot grow, so the camps that were already struggling stay small. About half of post-opening goods-economy camp-turns (51.5 percent at a = 2) are at or below `Q*` and cannot grow.
- **Score.** Fission on: 14.59 (head), 8.05, 4.58, 1.72, 1.29, 1.20 for a = 0.5, 1, 2, 3, 4. Fission off: 14.04, 6.20, 3.62, 2.10, 1.71, 2.95 (a = 4 is penalised by one unhoused P95 outlier of 253). Summed over both modes the grid ranks a = 2 (3.82) ahead of a = 4 (4.15) and a = 1 (8.20).
- **Birth rate at a = 2.** b0 = 0.055 raises `Q*` to 0.890 and scores 1.36 / 1.91 (3.27 summed) but leaves the fixed-dominant P90 at 573-576, below the 600 edge, and cuts population a further 19 percent (39k against 48k per world, fission on). b0 = 0.035 lowers `Q*` to 0.820, scores 3.00 / 2.74 (5.74 summed) and keeps 123k people per world.
- **Cost.** Median turn time falls from 802 to 350 ms (fission on) and 306 to 266 ms (off) because there are fewer and smaller camps; the rule itself is O(1) per camp.

## Shipped defaults and why

`BaseBirthRate = 0.045`, `QoLMortalityElasticity = 1`, `QoLMortalityEnabled = false` (opt-in).

The historical sweep above originally motivated `a = 2`, but the controlled demographic-structure follow-up in [DemographicStructureCalibration.md](DemographicStructureCalibration.md) supersedes that choice. At `a = 2`, `Q* = 0.863` can stop growth before a Neutral crowding split, particularly in tent-dominant husbandry camps. At `a = 1`, `Q* = 0.746`: the Neutral cohesion threshold is reached at QoL about 0.81 for both tent-dominant and fixed-housing populations, so fission is reachable while demographic growth remains positive. The earlier sweep also shows that `a = 1` avoids the severe husbandry QoL trap of `a = 2` (husbandry QoL P25 0.70 rather than 0.06).

The enable default has not changed. The controlled follow-up explicitly enables both QoL mortality and fission; ordinary new worlds and old saves still receive no QoL mortality unless requested.

## Limits

- The three headline numbers (camp size, population, QoL) trade against each other through one exponent. No value of `a` reaches all of the targets with the population of the previous behaviour; it was never a target.
- Husbandry camps without housing have very low QoL (P25 0.02-0.06 at a >= 2) and cannot grow below `Q*`; whether mobile herders' QoL should carry a housing term is a design question outside this change.
- Paleolithic growth is deliberately unchanged, so the opening is unaffected; a camp's first economy turn plans from its last Paleolithic QoL.
- The sweep is five worlds on one machine with three concurrent runs. Counts are cohort outcomes, not quotas.
