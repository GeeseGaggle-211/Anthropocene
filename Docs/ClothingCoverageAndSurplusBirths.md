# Clothing coverage, fission and surplus-funded births (16 October 2026)

Status: **promoted to the plan of record on 17 October 2026 (§9).** New worlds use
surplus-funded births (DemographyQoL v1.2) and the kernel-scored Elder split on the
merged settlement-portfolio DemandControl planner; saves without the new fields load
them off and continue exactly. Sections 1-8 record the diagnosis and the prototype
measurements on `feature/unified-social-relocation`.

Branch base: `feature/unified-social-relocation` at `f841284` (§1-§6) and
`b4450e9` (§7-§8).

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

As prototyped, each scored candidate was one authoritative `AdvanceBudget`: about
0.17 ms under v1.1 and about 0.36 ms under surplus births, about 13.6 candidates per
camp-turn, so the 12-seed sweep spent 22.1 s of its 27.7 s planning in the kernel
(3.0x the 9.3 s kernel-off run). Section 7 replaces this with a planner-only solver:
the same sweep now spends about 1.7 s in the kernel and plans in 8.0-8.3 s, with every chosen
split identical. The cost remains O(camps x candidates), independent of simulated
duration.

## 4. Known limits

- Coupled pools (camps with linked or detached hunters) take each pool's birth
  fraction from the scalar surplus rule with the other pools as density offset;
  this is not yet checked against an exact coupled bound solve.
- The kernel shelter split prices only the durable route; mixed farming/herding
  camps keep the existing fallback for the remainder.
- The distress-cohort sizing and daughter-forecast defects in §1 are diagnosed,
  not repaired.
- Scalar/parallel replay with the flags on is checked (§7, §8): identical 12-seed
  fingerprints with one worker and with four workers on small reversed partitions.
  The flags remain process-wide statics, so fixed-suite checks cannot toggle them.
- The authoritative surplus solve (`SurplusBudget`) can return its birth fraction
  up to about 2.5e-5 short in person-years: when its bisection stops on the
  tolerance test after moving the upper bound, it returns the stale lower bound
  (worst observed: target 1,402.987 person-years, returned 1,402.952). This affects
  only the flag-on resolution path. It is left unchanged so the 12-seed outcomes
  stay reproducible; the planning solver does not have the defect.
- Village size is now land-capped: typical sites feed about 300-480 people, so the
  600-1,000 band appears only on rich sites. That is a land-calibration question.

## 5. Promotion requirements

1. Contract amendment: v1.1 §5 forbids steepening the nutrition response;
   surplus-funded births changes the birth law and needs an explicit decision,
   new calibration anchors and re-run recovery/shock gates.
2. Configuration and save fields replacing the static diagnostic flags, with
   old-save defaults.
3. Performance work and scalar/parallel replay: done (§7), pending an agreed
   budget.
4. Mixed-use (farming + herding) settlements under the same heuristic: routes done
   (§8); the herd-versus-field posture needs a longer-horizon value than the
   one-turn kernel provides.
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
  `distressladder`, `intens=<turns>`, `soft=<multiplier>`, `dumpturn=<t>`;
  `kernelexact` (score with the authoritative solver), `kernelverify` (score with
  both and count differing decisions), `kernelmixed`, `posture=<0|1|2>`,
  `workers=<n>` (parallel planning and resolution; the last turn prints each
  seed's fingerprint). The summary line reports kernel time, calls, interval solves,
  chosen levels/routes/postures and the surplus case counts.
- `--kernel-scaling=<players>,<turns>[,<seed>][,mixed]`: matched-state planning
  cost after a social-free opening (8: Small; 80: 1024x512; 320: 2048x1024
  benchmark scale). Each continuation turn plans the same world with the kernel
  off, with the planning solver and with the authoritative solver (minimum of three
  repetitions after a full collection, rotated order), then resolves the planning
  solver's plan.
- `--kernel-mixed-checks`: the standalone mixed-use fixtures (§8).
- `DemographyChecks` (fixed suite) compares the planning and authoritative solvers
  over 1,440 budget cases without touching the process-wide flags.

Diagnostic sinks are null or false by default: `Fission.Diagnostic`,
`Immigration.Diagnostic`, `Fission.DistressSizeLadderDiagnostic`, the
`DemandController.Kernel*` fields, and `PhysicalDemography.SurplusFundedBirthsDiagnostic`.

## 7. Planning cost of the kernel split (16 October 2026)

**Abstraction.** Authoritative state is unchanged: resolution still advances each
cohort with `AdvanceBudget`/`SurplusBudget`, and the Elder's scores are transient.
Candidate scoring now calls `PhysicalDemography.PlanningBudget`: the same law,
inputs and tolerances (`DemographicServiceTolerance` 1e-10, `DemographicRootTolerance`
1e-12), solved for planning only. A score differs from the authoritative one only
inside those tolerances, so a decision can change only at a near-exact tie.

**Measured cause.** The 12-seed sweep scored 61,126 candidates (13.6 per camp-turn).
Each surplus-births candidate cost about 62 fixed-service interval solves: a
bisected full-birth closure, a bisected birthless closure and a 48-step bisection
on the birth fraction `g`. Each interval solve also bisected the equilibrium root
from log(double.Epsilon), about 55 rate evaluations.

**Changes** (all in `Demography.cs`, planning path only):

1. Person-year closure (`FastClosure`): `f(m) = PY(m) - m` falls with the assumed
   person-years, so one fixed-point step from the birth-only envelope brackets the
   root after two solves; Brent's method finishes.
2. Surplus classification (`FastSurplus`): with `T = food / foodRate`, the birthless
   closure is fed exactly when `f(0) <= 0` and the full-birth closure exactly when
   `f(1) <= 0`, where `f(g) = PY(F = 1, C = c(T), g) - T`. The two endpoint solves
   classify the case that previously took two complete closures, and in the
   food-bound case they already bracket `g`. A fed camp whose clothing also covers
   the fully clothed path needs one solve.
3. Equilibrium root: halving steps from the current population, then Brent.
4. Exact reuse inside a solve: band edges shared by consecutive bands are evaluated
   once, the net rate uses hoisted per-interval constants with the same arithmetic,
   and the crowding/disease normaliser `expm1(1000 k)` is cached per thread (this
   last one is bitwise-identical and also applies to resolution).
5. A mixed camp's alternative-route candidate with the same kernel inputs as an
   earlier one reuses its result (ties keep the earlier candidate).

**Result, 12-seed sweep** (`kernellex,lextol=0.05,surplusbirths`, 300 turns):

| | Kernel | Planning total | Solves per candidate | Chosen splits |
| --- | ---: | ---: | ---: | --- |
| Authoritative scoring (b4450e9) | 22.1 s | 27.7 s | 62 | reference |
| Planning solver | 1.65-1.76 s | 8.0-8.3 s | 6.4 | identical; CSV byte-identical |
| Kernel off (surplus births only) | 0 | 9.0-9.3 s | - | - |

The kernel-off run has more camps (365 against 221 at turn 25), so its total is not
a matched comparison; the matched figures are in the scaling table. With
`kernelverify` every decision was also scored with the authoritative solver: 0 of
4,499 decisions differ, the largest score difference is 9.4e-7 in QoL, and the
largest person-year difference (2.5e-5) is the authoritative solver's stale-bound
return noted in §4, not the planning solver. Across the 1,440-case fixed matrix the
two solvers agree to 5.5e-9 in person-years and 1.2e-9 in coverage.

**Scaling** (`--kernel-scaling`, seed 11, matched state, minimum of three
repetitions; planning = AI decisions plus Prepare):

| Tribes | Camp-turns | Kernel off, ms per camp-turn | Planning solver: kernel ms per camp-turn | Planning ratio | Authoritative scoring: kernel ms per camp-turn | Planning ratio |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 8 (12-seed sweep, same run) | 4,499 | 1.42 | 0.39 | 1.27-1.28x | 4.9 | 4.9x |
| 80 | 468 | 1.17 | 0.35 | 1.30-1.35x | 4.4-4.5 | 4.8-4.9x |
| 320 | 1,027 | 1.32-1.33 | 0.38 | 1.31-1.32x | 4.4-4.6 | 4.4-4.6x |

Ranges span the single-route and mixed (mode 0) runs. The harness's own 8-tribe
sample (109 camp-turns) is too small: the kernel-off time of the same ten turns
varied from 136 to 190 ms between runs, giving ratios from 1.28x to 1.45x, so the
8-tribe row uses the 12-seed sweep's kernel share of its own planning. The kernel
cost per camp-turn is flat in world size (0.35-0.49 ms everywhere measured), as
O(camps x candidates) predicts; the remaining planner cost also scales with camps.

**Proposed budget:** the kernel split may add at most 35% to matched-state planning
(planning ratio at most 1.35x) at 8, 80 and 320 tribes, measured as above. It is
met everywhere. The example budget of 1.3x is met at 8 tribes and reached or
exceeded by at most 0.05 at 80 and 320 tribes, where planning without the kernel is
cheaper per camp. What remains is the per-solve cost (about 4-5 microseconds,
mostly exponentials, per 20-year interval) at about 6.4 solves per candidate, not
the search.

**Not done, by design.** Warm starts from the previous turn's choice, pruning the
grid to the levels usually chosen (clothing 0.75/1.0 take 84% of choices,
shelter 1.0 takes 56%), and coarser log bands would each cut cost further but change
some chosen splits. None was needed to reach the budget, and each would move the
heuristic away from the exhaustive teacher an RL agent should be compared with.

**Replay.** Scalar against four workers with reversed three-camp partitions gives
identical fingerprints for all 12 seeds and a byte-identical CSV, with and without
`kernelmixed`. Flag-off output is byte-identical to `b4450e9` and the fixed suite
passes.

## 8. Mixed-use settlements (16 October 2026)

**Question.** Farming and herding camps (61% of camp-turns in the 12-seed sweep)
had one clothing route (Cloth whenever agriculture allows it), one priced shelter
route (fixed Housing) and a fixed investment order (herd growth is Continuity,
field expansion Development).

**Action.** `KernelMixedDiagnostic` (`kernelmixed`) lets the Elder of a camp with
both capabilities choose among them with the same kernel and lexicographic
objective. The committed choice is `Allocation.ElderAction`: clothing route and
grid level (units), shelter route and grid level (people), investment posture,
and the kernel's person-years and QoL for it. With `CaptureDemandAllocations`,
`Allocation.KernelCandidates` keeps every scored split as teacher data. This is the
action shape proposed for the Village Elder agent; the levels are the existing
grids (5 clothing x 3 shelter) plus two routes and three postures.

**Search.** Stage 1 is the unchanged grid on the default routes. A mixed camp then
scores the other clothing route at the chosen shelter (4 levels) and the other
shelter route at the chosen clothing (2 levels). Every candidate of a mixed camp
must fit the plant and animal food that can exist this turn and the Stone and
Wood/Clay reachable; other camps keep the unconstrained single-route grid exactly.
The committed clothing and shelter requests follow the chosen routes; the
resolver already honours Protected clothing from either textile.

**Labour posture.** Herd growth and field expansion both ask for labour in 24% of
mixed camp-turns, and in every such case one is rationed (field expansion is
rationed in 99% of the turns it is requested). Their returns arrive in later turns,
so a one-turn score cannot compare them. `KernelPostureMode` 1 scores herd-first
against field-first, and mode 2 also scores no investment, each over this turn plus
one continuation turn: the population follows the first turn, the investment's
measured return (grown-herd food; the ladder's accepted field gain) is added to the
second turn's income, and person-years are summed with person-year-weighted QoL.

**Results** (12 seeds, same settings as §7; single-route reference first):

| Variant | Camps | Population | Median | QoL (final, pop-weighted) | Person-years x QoL | Route / posture choices |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Single route (reference) | 221 | 102,124 | 384 | 0.785 | 20.17M | - |
| Mixed, routes only (mode 0, default) | 221 | 102,193 | 384 | 0.785 | 20.17M | Light Housing 13 of 2,584 mixed camp-turns; Animal Fabric 0 |
| Mixed, herd/field (mode 1) | 216 | 97,782 | 368.5 | 0.780 | 19.64M | field-first 380, herd-first 296 |
| Mixed, herd/field/none (mode 2) | 226 | 95,970 | 327 | 0.792 | 19.69M | none 572, field-first 93, herd-first 91 |

Nutrition stays 0.995-0.996 and clothing coverage 0.887-0.892 (person-year weighted)
in every variant.

**Findings.**

- Clothing routes tie. Cloth and Animal Fabric have the same weaving labour and one
  food unit per unit, both count fully as clothing, and food is fungible in the
  demographic budget, so the two routes give identical scores and the earlier
  (default) route is kept. Animal Fabric wins only when plant feedstock cannot fund
  Cloth; that never happened in the sweep but does in the fixture.
- Fixed Housing dominates tents when its materials exist: about a quarter less
  builder labour per person and full social cohesion (tents count 0.21). The kernel
  chose tents in 13 mixed camp-turns, where Wood and Clay could not fund Housing.
- The two-turn posture score is worse than the existing herd-first rule: mode 1
  picks field-first in 56% of competing turns and loses 4.3% of population and
  0.005 QoL; mode 2 mostly stops investing and loses 6% of population for 0.007 QoL.
  The horizon truncates a herd's compounding growth and a field's persistence, and
  it omits the slaughter food a held herd yields. Investment choice needs a
  longer-horizon value than the turn kernel gives; this is a natural target for the
  learned Elder (a value function), not for a longer hand-built lookahead.
- Cost: mixed mode (0) adds 3.6% kernel calls and about 1% kernel time on the
  12-seed sweep after the same-input reuse; modes 1 and 2 added 6-15% before it.

**Fixtures** (`--kernel-mixed-checks`, 13 assertions): with no plant feedstock and
abundant animal food, the mixed Elder commits Animal Fabric clothing, the Protected
request follows it, is funded, and clothes more than the single-route Elder, whose
Cloth lacks feedstock;
with no Wood or Clay it commits and funds Light Housing; the scored pool records
both routes; planning is pure and deterministic; a farming-only camp's requests,
grants, action and ceilings are identical with and without the flag.

## 9. Promotion and merge (17 October 2026)

`features/neolithic-agricultural` (settlement envelope, resident Fishing, hunter recall; `543084d`) and
`feature/unified-social-relocation` (physical demography v1.1, social relocation, this work; `d37a77e`)
were merged. The authoritative heuristic is neolithic-agricultural's settlement-portfolio DemandControl
planner with this document's kernel split ported into it:

- Configuration (all `OptionalField`, validated; absent in older saves: off / objective 2 / 0.05):
  `SurplusFundedBirths`, `ElderKernelSplit`, `ElderSplitObjective`, `ElderSplitLexTolerance`,
  `ElderMixedRoutes`. `OpeningConfig.json` and the initializers ship true / 2 / 0.05 / true. The static
  diagnostic flags remain as probe overrides that can only switch the split on.
- The kernel runs after the settlement supervisor has fixed retained fields, herds and food staffing,
  so its candidate income is the supervisor's harvest, spare farmer capacity, catch, managed animal
  food and gathering by the remaining workers. Protected shelter serves fixed replacement, then
  portable replacement, then the deficit; the remainders keep their ordinary classes and routes. The
  herd-versus-field posture search is retired (it lost population, §8); the supervisor owns that choice.
- Binding keeps the current-clothing reserve exception to the current-clothing bundle only
  (`min(clothing envelope, normal envelope + current clothing input)`), the fix the relocation branch
  had made; neolithic-agricultural's `max` let reserve clothing and tent feedstock borrow it.
- Observation schema 22 is the union of schema 21 (Fishing, recall, settlement envelope) and schema 19
  (owner-only QoL/physical-demography diagnostics). The save envelope stays `ANTHROPOCENE-11`.
- Retired checks: the relocation branch's shelter-fallback/reservation-fraction checks and
  `FoodServiceClasses` tested that branch's planner internals, which the settlement planner replaced
  (one route per shelter request; reserve and growth gathering as Recovery/Development requests).
  The v1.1 kernel reference matrix pins `SurplusFundedBirths = false`; one settlement ledger fixture
  pins the legacy demographic model it was written for.

Same 12-seed, 25-turn probe as §3 (Small/8, fission and immigration on), merged code:

| Variant | Camps | Population | Median | P90 | 600+ | F | Clothing | Housing | QoL (pop-weighted) | Person-years x QoL | Fissions | Migrations |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| v1.1 law, no split (control) | 462 | 253,392 | 494 | 973 | 169 | 0.949 | 0.566 | 0.772 | 0.461 | 29.98M | 373 | 819 |
| **Plan of record** | **183** | **108,351** | **556** | **999** | **86** | **0.999** | **0.996** | **0.957** | **0.801** | **24.44M** | **91** | **200** |

Against the pre-merge prototype (221 camps, median 384), the settlement envelope raises the median
village to 556 and puts 86 of 183 settlements at 600 or more. The control's larger total population
and summed person-years x QoL come from overshooting land at F 0.95 and splitting into 462 camps at
QoL 0.46; this is the trade-off accepted in §3. Planning took 7.5 s for the 300 continuation turns, of
which the kernel was 1.26 s (3,859 kernel camp-turns, 54,839 planning solves). Of the 1,558 mixed camp-turns,
19 chose the other shelter route and none the other clothing route.

Gate: fixed headless suite (`./Tools/Test-Headless.ps1 -Seeds 0`) with the new defaults, including
scalar-versus-2/4-worker replay; `python Tools/Docs/Check-CurrentContract.py`; Unity EditMode 29/29.
Reproduce the two rows with `--clothing-probe=25,<outDir>,seeds=17;53;991;1;2;3;5;8;13;21;34;89` and
the same with `,v11` appended.

## 10. Actor-labor clothing protection: the converted-teacher 2-turn cycle (28 October 2026)

**Symptom.** In the r5 labor-teacher data (16 players, 128x64, separation 9) most mature camps alternate every
turn between clothing coverage ~0.93 and 0, with QoL reward ~16-17 vs ~8.5 QoL-years per 20-year turn and
residents swinging together (world 90103 camp 29 from turn 22: residents 546/332/508/542/491/569/516...,
Farmers ~300 <-> ~60).

**Mechanism.** The native heuristic teacher does not cycle: its kernel-scored Elder split commits clothing units
(`ProductionOrder.ProtectedClothingUnits`), and `TurnPlanning` sets `TurnNeeds.ProtectedClothing`, so the
resolver pays the clothing-need feedstock ahead of meals (`GenerationalEconomy` `clothingRoom`). The teacher
label is executed through contract 13 (action 12), which has no clothing/food split head:
`ProductionOrder.Autonomous` leaves `ProtectedClothingUnits = -1`, so the executed turn falls back to the legacy
after-meals rule. Whenever meals take the food (the turn after a birth pulse) the whole clothing feedstock is lost,
clothing is 0, exposure deaths cut the population, the next turn has a food surplus, clothing is covered, births
refill the camp, and the cycle repeats. The native shadow outcome from the same states stays at clothing 1.

Isolation (native heuristic, `--model9-cycle=90103,50,16,128,64,9,29,0`, protection disabled by hand only for
this measurement): mature mean |delta cloth| 0.001 -> 0.403, alternating cloth triples 0.0003 -> 0.354,
RMS turn-to-turn QoL-years change 1.13 -> 4.96, the same 0.95 <-> 0 / 17 <-> 9 signature.

**Fix.** `TurnPlanning`: an actor-activity (contract-13) order is protected like the native Elder's when
`ElderKernelSplit` is on: the clothing-need feedstock its own staffing forecasts (net of stock) is paid before
meals. Without the kernel split the legacy after-meals order is kept. Native heuristic trajectories are
unchanged (50 turns of identical world fingerprints, world 90103/16 players). Check:
`ElderActorLaborChecks` (protected exactly when the kernel split is on).

**Measurement** (converted-teacher loop identical to `labor_bc._teacher_transition`, verified row-identical to it on
turns 10-15; `Tools/PerformanceAudit/Model9Cycle/teacher_fast.py`, summarised by `compare.py`; world 90103,
16 players, 128x64, separation 9, turns 20-37 (the before run reached turn 44, the after run turn 37 when stopped);
mature rows are residents >= 50):

| Metric | Before | After |
|---|---|---|
| mean abs turn-to-turn change in clothing coverage | 0.552 | 0.085 |
| share of lag-1 alternating clothing triples (both steps > 0.3) | 0.542 | 0.001 |
| share of mature rows with clothing < 0.1 | 0.300 | 0.000 |
| RMS turn-to-turn change in QoL-years per row | 5.86 | 1.92 |
| mean within-camp variance of QoL-years | 11.22 | 2.05 |
| mean clothing coverage | 0.629 | 0.927 |
| mean QoL-years per row | 13.76 | 16.49 |
| person-QoL-years (sum p0 x QoL x years) | 11.58 M | 13.90 M |
| person-years | 16.95 M | 17.33 M |
| residents at turn 37 | 61,112 | 62,107 |

Camp 29 (case B) now runs clothing 1.00 / ~0.88 and QoL ~18 / ~15.7 instead of ~0.93 / 0 and ~16 / ~8.7.
**Residual, not fixed here:** residents and Farmers still alternate (camp 29: 497/622/514/657..., Farmers
~300 <-> ~60). That smaller cycle is the native planner's own field-development alternation (a high-population
turn authorises a large field build that crowds out gathering and funds few births; the next turn has no gap); it is
present in the native heuristic too (`--model9-cycle`, same world: residents 565/375/576/499...) with clothing at 1.
