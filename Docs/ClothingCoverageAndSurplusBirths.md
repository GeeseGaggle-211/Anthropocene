# Clothing coverage, fission and surplus-funded births (16 October 2026)

Status: **diagnosed; recommended direction prototyped behind diagnostic flags.**
Nothing here changes default behaviour. With every flag off, the 12-seed probe
trace below and the fixed headless suite (`./Tools/Test-Headless.ps1 -Seeds 0`)
reproduce exactly. Promotion to defaults needs the performance work and the
contract amendment listed at the end.

Branch base: `feature/unified-social-relocation` at `f841284`.

## 1. Question and answer in brief

The redesigned demography/QoL/DemandControl economy appeared to under-allocate
clothing and to cause excessive QoL-driven fission. The diagnosis found one
implementation defect, two planner-architecture defects and one defect in the
v1.1 demographic law:

| # | Finding | Level |
| --- | --- | --- |
| 1 | Current clothing is sized for the opening residents (`N0 x T`), while coverage is measured against realized person-years including births. Fully funded camps reach `C = N0T/Y` (0.82 on average, 0.61 in growing original camps). | Implementation |
| 2 | Strict class order (Survival before Continuity) plus a hard "food beyond meals" feedstock gate gives clothing exactly zero at land-limited camps although Cloth costs about 1.2% of meals. Population feedback makes it a period-2 limit cycle (`C` alternates 0 and 0.9; 171 of 190 large moves reverse). A labour-saturated variant gives zero clothing with only 5% of workers needed. Earlier 1.33x-4x food-to-Cloth sweeps could not help because any positive input against a zero envelope is zero. | Architecture (planner) |
| 3 | Fission is housing-driven, not clothing-driven: a free-housing oracle cuts fissions 76 -> 17 (3 seeds), a free-clothing oracle 76 -> 66. Splitting raises capacity/N for stayers without building, because fixed housing stays with the parent. | Mechanism (by design) |
| 4 | v1.1 makes food weakly self-limiting: births are linear in `F` and the starvation hazard is small (Mortality 0.18) and delayed by a 40-year grace, so a 10% food shortfall costs about a third of the mortality of having no clothing. Protected villages overshoot their land: a site feeding 600 settles at 746.6 people with `F = 0.80` (contract §5 predicted this). | Demographic law |

Field intensification cannot close the food gap: at hungry, land-limited camps
DemandControl already grants about 88% of idle labour to fields, but one
worker-year of building returns about 0.015 food/year against 7.2 for a year of
gathering. 4x faster building or a softer effort curve (16 -> 4) barely changes
the outcome. Fed capacity rises about 4x from bare land to q 0.4 (about 40 -> 150
people per tile) and little beyond.

Relocation cannot drain chronic hunger either. Distress cohorts are sized from a
source nutrition forecast of about 0.53 against about 0.75 realized, so the group is
about half the village and the mover gate rejects 71-73% of hungry camp-turns;
daughter forecasts also omit the daughter's own clothing/shelter production
(forecast clothing 0.33-0.58 against 0.85-0.93 realized at age one). Offering
smaller distress cohorts triples fission and nearly doubles population, but the
share of people in well-fed camps falls, because protected populations refill a
turn's departures within the same turn.

## 2. Recommended direction (prototype)

1. **Surplus-funded births** (`PhysicalDemography.SurplusFundedBirthsDiagnostic`).
   Food first pays the resident cohort's meals; births occur at `b0 * g`, where
   `g` in [0,1] is the largest birth fraction whose newborn person-years the
   remaining food still feeds in full. If even a birthless cohort cannot be fed,
   `g = 0` and the ordinary nutrition closure and hunger hazards apply. This
   keeps the contract's "births must not create unpaid person-years" and makes
   food a first-class demographic requirement without the retired QoL growth
   ceiling (whose clothing/housing gate caused the old death spiral).
2. **Option-3 Elder split** (`DemandController.KernelSplitDiagnostic`). The Elder
   scores a 5 x 3 grid of (current clothing units, shelter-backlog people) with
   the authoritative `PhysicalDemography.AdvanceBudget`, commits the winner as a
   `Protected` class ahead of meals, and the resolver honours that clothing
   feedstock before consumption (`TurnNeeds.ProtectedClothing`,
   `GrowthPlan.ProtectedClothingFood`). Clothing is sized against the
   birth-only person-year envelope, which also removes finding 1.
3. **Lexicographic objective, 5% tolerance** (`KernelSplitObjective = 2`,
   `KernelLexTolerance = .05`): keep person-years within 5% of the best candidate,
   then maximize QoL. Under surplus births person-years are fixed by food at the
   land limit, so the person-years objective goes flat and its tie rule chose no
   protection; QoL-weighted and terminal-value objectives sacrifice about 12% of
   population.

### Real-kernel equilibria (`--surplus-equilibria`, 80 turns, from 100 and 1,500)

| Land feeds | Coverage | v1.1 N / F | Surplus births N / F |
| ---: | ---: | --- | --- |
| 300 | 100% | 493 / 0.61 | 300 / 1.00 |
| 600 | 50% | 654 / 0.92 | 600 / 1.00 |
| 600 | 100% | 746.6 / 0.80 | 600 / 1.00 |
| 900 | 100% | 942 / 0.96 | 900 / 1.00 |
| any | 0% | 300 / 1.00 | 300 / 1.00 |

### 12-seed sweep (Small(8), seeds 17;53;991;1;2;3;5;8;13;21;34;89, 25 turns after the social-free opening)

Shipped social settings:

| Variant | Camps | Population | Median village | F | Clothing coverage | Pop-weighted QoL | Fissions | Clothing turn-to-turn change |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Baseline | 411 | 215,684 | 490 | 0.94 | 0.50 | 0.505 | 313 | 0.151 |
| Option 3, v1.1 law | 261 | 159,721 | 608 | 0.86 | 0.96 | 0.648 | 159 | 0.060 |
| Surplus births, current planner | 365 | 175,397 | 432 | 1.00 | 0.82 | 0.667 | 278 | 0.281 |
| Surplus births + option 3, lexicographic 5% | 221 | 102,124 | 384 | 1.00 | 0.95 | 0.785 | 129 | 0.117 |

Population per camp is about the same in every surplus variant (440-480). The
total differs because the protected planner removes housing-driven fission, so
fewer daughters colonize land. Under the v1.1 law, camp-turns with `F < 0.8` are
18% (baseline) and 39% (option 3); under surplus births they are 1-4%.

Matched isolation (fission and immigration off, the same 96 camps):

| Variant | Population | QoL | Clothing coverage | Pop x QoL | Summed person-years x QoL | Clothing change |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Surplus births, current planner | 47.2k | 0.576 | 0.74 | 27.2k | 8.36M | 0.175 |
| + option 3, person-years | 50.8k | 0.599 | 0.67 | 30.4k | 9.31M | 0.210 |
| **+ option 3, lexicographic 5%** | **45.4k** | **0.713** | **0.92** | **32.3k** | **10.45M** | **0.134** |
| + option 3, terminal value | 41.8k | 0.743 | 0.96 | 31.0k | 9.83M | 0.198 |
| + option 3, QoL-weighted | 41.6k | 0.744 | 0.95 | 30.9k | 9.18M | 0.222 |

Population x QoL alone still favours the v1.1 hungry equilibrium (option 3:
38.7k in the matched run) because QoL falls only linearly with `F`. A fed
requirement therefore cannot be expressed by that metric alone.

## 3. Cost

Each scored candidate is one `AdvanceBudget`: about 0.17 ms under v1.1 and about
0.34 ms under surplus births (the bound case solves twice and bisects `g`). The
grid uses about 15 candidates per camp-turn. Planning in the 12-seed sweep takes
about 27 s for 300 turns, against about 10 s without the kernel: 2.7x. The cost is
O(camps x candidates) and independent of simulated duration, but it is not
acceptable at 80/320-tribe scale without pruning (the chosen clothing grid points
concentrate at 0.75/1.0), warm starts from the previous turn, or a closed-form
bound case.

## 4. Known limits

- Coupled pools (camps with linked or detached hunters) take each pool's birth
  fraction from the scalar surplus rule with the other pools as density offset;
  this is not yet checked against an exact coupled bound solve.
- The kernel shelter split prices only the durable route; mixed farming/herding
  camps keep the existing fallback for the remainder.
- The distress-cohort sizing and daughter-forecast defects in §1 are diagnosed,
  not repaired.
- Scalar/parallel replay with the flags on is unchecked (the flags are process-wide
  statics; the decision path is pure).
- Village size is now land-capped: typical sites feed about 300-480 people, so the
  600-1,000 band appears only on rich sites. That is a land-calibration question.

## 5. Promotion requirements

1. Contract amendment: v1.1 §5 forbids steepening the nutrition response;
   surplus-funded births changes the birth law and needs an explicit decision,
   new calibration anchors and re-run recovery/shock gates.
2. Configuration and save fields replacing the static diagnostic flags, with
   old-save defaults.
3. Performance work (above) and scalar/parallel replay.
4. Mixed-use (farming + herding) settlements under the same heuristic.
5. The heuristic then serves as the teacher/baseline for the renewed Village
   Elder RL agent, whose action is the committed labour/food split.

## 6. Reproduction

All commands run from the repository root with `.tools/dotnet/dotnet.exe run -c Release --project Tools/Headless/Anthropocene.Headless.csproj -- 0 <switch>`:

- `--surplus-equilibria`: the kernel equilibrium table.
- `--clothing-probe=25,<outDir>,seeds=17;53;991;1;2;3;5;8;13;21;34;89[,options]`:
  per camp-turn CSV of clothing request -> grant -> output -> coverage, field
  ladders, and fission/immigration gate outcomes. Options: `kernel`, `kernelwy`,
  `kernellex`, `kernelterm`, `kerneldeaths`, `lextol=<x>`, `surplusbirths`,
  `shelteronly`, `freecloth`, `freehouse`, `nofission`, `noimm`,
  `distressladder`, `intens=<turns>`, `soft=<multiplier>`, `dumpturn=<t>`.

Diagnostic sinks are null or false by default: `Fission.Diagnostic`,
`Immigration.Diagnostic`, `Fission.DistressSizeLadderDiagnostic`, the
`DemandController.Kernel*` fields, and `PhysicalDemography.SurplusFundedBirthsDiagnostic`.
