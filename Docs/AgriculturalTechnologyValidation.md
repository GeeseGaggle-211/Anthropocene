# Agricultural technology validation — 11 November 2026

**21 November review and 25° valley-diversion rerun (`b106f8f`):** following the
Plough harvest-staffing, shared Builder tool-budget, ecology-continuity,
localized-routing fixes (`ac882b0`) and the non-gorge river-valley diversion
rule (`ValleyMaxSlopeDegrees = 25`, `ValleyBankWidthMeters = 1200`, `b106f8f`),
the full five-seed × thirteen-treatment × two-regime CI matrix (**130 paired
runs**, seeds `11, 23, 47, 101, 1729`) and all 70 headless suites
(`AgriculturalTechnologyChecks`: 337 assertions; `SmoothCalendar`: 28,787
checks; `--benchmark`: 320 networks @ 1M pop / 600 yr in 2.90 ms) passed. Every
seed now organically discovers Irrigation by year 199–298 (including seed 1729,
previously unattained), expanding active irrigation to **41 canals / 32 fields /
21 settlements / 1.193M delivered water** (vs. 13 canals / 9 settlements /
379K water under strict downhill-only gravity).

| Treatment (`b106f8f`, 5 seeds) | Elder food Δ | Elder work/food Δ | Fixed-field food Δ | Fixed-field work/food Δ | Elder pop (`p25` / `med` / `p75`) | Canals / Water |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| `none` | +0.000% | +0.000% | +0.000% | +0.000% | 96,186 (`643.0` / `997.0` / `1000.0`) | 37 / 0 |
| `maintenance` | -0.609% | -0.338% | +1.900% | -0.506% | 94,423 (`652.0` / `998.0` / `1000.0`) | 37 / 0 |
| `harvest` | -1.599% | -1.368% | -0.102% | -2.542% | 94,068 (`673.0` / `996.0` / `1000.0`) | 37 / 0 |
| `combined` | -3.131% | -1.720% | -0.669% | -2.492% | 91,166 (`647.2` / `996.0` / `1000.0`) | 37 / 0 |
| `water` | +0.287% | -0.104% | +1.033% | -0.157% | 96,390 (`645.0` / `999.0` / `1000.0`) | 41 / 1,192,670 |
| `soil` | +0.317% | -0.131% | +1.147% | -0.214% | 96,573 (`645.0` / `997.0` / `1000.0`) | 41 / 1,192,692 |
| `combined-water` | -2.915% | -1.741% | -0.558% | -2.667% | 91,669 (—) | 41 / 1,193,303 |
| `combined-soil` | -2.864% | -1.872% | -0.510% | -2.760% | 91,646 (`648.0` / `996.0` / `1000.0`) | 41 / 1,193,316 |
| `low-plough` | -1.953% | -0.198% | +1.335% | -0.740% | 92,221 (—) | 37 / 0 |
| `high-plough` | -0.629% | -2.726% | +0.953% | -3.911% | 93,816 (`640.2` / `996.0` / `1000.0`) | 37 / 0 |
| `half-cost` | -2.856% | -1.934% | -0.447% | -2.803% | 92,008 (—) | 41 / 1,193,283 |
| `double-cost` | -2.337% | -1.827% | -0.493% | -2.726% | 92,709 (—) | 41 / 1,193,317 |
| `moisture-one` | -0.434% | -1.756% | +1.103% | -2.711% | 96,779 (—) | 51 / 1,654,494 |

On the **7 sub-cap (`<950`) irrigated settlements**, irrigation lifts mean
population by **+6.1%** (`574.9 → 609.9`, median `651 → 711`, `+9.2%`) in `soil`
vs. `none` and **+5.7%** (`562.6 → 594.7`, `p25` `396 → 421`, median `634 → 677`)
in `combined-soil` vs. `combined`, while `p75` and overall median remain pinned
near the `1,000`-person crowding ceiling across all treatments. Organic first
availability on `b106f8f` (Plough / Irrigation years): seed 11 (`517.8` /
`237.8`), seed 23 (`521.6` / `276.7`), seed 47 (`488.6` / `257.8`), seed 101
(`501.6` / `199.3`), seed 1729 (`577.8` / `297.8`).

**26 November Sanitation follow-up (`features/sanitation`):** [Sanitation.md](Sanitation.md)
documents paid physical Sanitation unlocked upon `Irrigation` (`SanitationEnabled`,
50% disease-only reduction, conserved `0.15`/person-year freshwater coupling,
Elder Continuity + `+1..2` `PlanningBudget` Development) and extends the
`AgriculturalTechnologySweep` harness to 15 treatments (`sanitation` and
`combined-soil-sanitation`), with a 40-run matched five-seed ablation, `--labor-audit`
exact-replay verification, and `SanitationChecks` (417 assertions). Pre-sanitation
`none` and `combined-soil` arms reproduce the `b106f8f` results above identically.

**15 November persistence follow-up:** [SavePersistence.md](SavePersistence.md)
records recovery of the large-world save, preserving its original fingerprint
and history. This resolves the documented metadata-size failure; native Unity
interaction validation remains pending.

**13 November construction correction:** the [three 80-tribe reruns](ConstructionFix80.md)
record the repaired tool allowance, Builder material throughput, construction
storage, final grant reconciliation and cached-draft isolation. The final code
passes all 69 headless suites. The historical tables below are not recalibrated
by these normal-play reruns; large-world final-save and Unity acceptance remain
open.

**12 November correction:** headless gameplay exposed and prompted a fix for
manufacturing demand omitting prepaid canal upkeep. The [80-tribe follow-up](AgriculturalTechnologyGameplay80.md)
records fresh validation of the corrected build. The tables below remain the
pre-fix experiment: they are reproducible historical evidence, not a fresh
calibration of the corrected build. The full five-seed treatment matrix has not
been rerun after this correction. The 80-tribe run exposes additional construction
grant/output discrepancies and a final-save metadata limit; acceptance remains
blocked. Unity interaction acceptance also remains pending.

Plough and paid Elder irrigation are implemented in an opt-in profile using the
approved smoothed calendar. The tested baseline retains maintenance **1.50×**,
crop harvest **1.25×**, moisture target **0.50**, and nonzero canal construction
and upkeep costs. The results support useful paid benefits in these cohorts;
they do not imply every investment, settlement, or stronger coefficient wins.
Unity interaction acceptance remains pending because this environment has no
Unity 6000.5.10f1 installation. The feature is therefore delivered for review,
not silently enabled in existing worlds or active RL training.

## Principal experiment

- Five seeds: 11, 23, 47, 101, 1729; native 96×48 worlds, four initial players.
- Each cohort completes the native opening and at least 600 organic years. Plough and Irrigation availability dates are recorded before identical diagnostic knowledge grants at the matched fork.
- Thirteen treatments × two staffing regimes × five seeds = **130 comparisons**, each **603.341813 physical years** after its common checkpoint. All paired starts, ends, committed durations and four-season exposure vectors match exactly.
- The second regime fixes operating Farmers in checkpoint settlements. Other Elder jobs and later settlements remain endogenous; it is not fixed total workforce/population.
- Every final state passes save/load and exact next-turn replay. All 130 runs were replayed with labor attribution enabled and matched their original authoritative fingerprints.
- Source-file SHA-256, calendar SHA-256, serialized settings, epoch, per-treatment coefficients, committed durations and starting-save hashes are in the [manifest](Experiments/AgriculturalTechnology-20261111-manifest.json).
- [All 130 measurements](Experiments/AgriculturalTechnology-20261111.csv), [aggregate results](Experiments/AgriculturalTechnology-20261111-summary.json), and [network timing samples](Experiments/AgriculturalTechnology-20261111-benchmark.json) are committed. Reproduction commands are in [the implementation contract](AgriculturalTechnology.md). Binary checkpoints and full receipts are generated under `TestResults/AgriculturalTechnology`; CI retains them as artifacts.

## Economic results

Percent changes below pool the five seeds relative to the same-regime no-technology arm.
Food is conserved crop-plus-wild attribution, not population. Measured work includes
reconciled maintenance/harvest overlap, field expansion, equivalent land gathering,
actual material extraction and all manufacturing, including tools and Housing Materials.
It excludes unrelated hunter, Fisher and Herder work. Thus the denominator is measured
agricultural/craft work, not all resident time. All original demographic laws remain active.

| Treatment | Elder food Δ | Elder work/food Δ | Fixed-field food Δ | Fixed-field work/food Δ |
| --- | ---: | ---: | ---: | ---: |
| none | +0.000% | +0.000% | +0.000% | +0.000% |
| maintenance | +0.069% | +0.334% | +0.754% | -0.497% |
| harvest | +0.183% | -1.732% | +0.058% | -1.124% |
| combined | +0.638% | -0.808% | +0.806% | -1.588% |
| water | +0.028% | -0.032% | +0.030% | -0.040% |
| soil | +0.047% | -0.050% | +0.043% | -0.041% |
| combined-water | +0.657% | -0.787% | +0.851% | -1.624% |
| combined-soil | +0.671% | -0.812% | +0.866% | -1.626% |
| low-plough | +0.159% | -0.796% | +0.501% | -0.976% |
| high-plough | -3.249% | -1.925% | +1.105% | -2.168% |
| half-cost | +0.698% | -0.862% | +0.846% | -1.615% |
| double-cost | +0.672% | -0.798% | +0.876% | -1.656% |
| moisture-one | +0.687% | -0.830% | +0.868% | -1.603% |

The combined Plough baseline supplies 0.638% more plant food while using 0.808%
less measured work per unit of food in the endogenous cohort; total measured work
falls by 58,188 worker-years. With irrigation and soil, the corresponding changes
are +0.671% food, −0.812% work/food and 48,503 total worker-years saved against
no technology. The fixed-field regime gives +0.866% food and −1.626% work/food
for the combined Plough/irrigation/soil treatment.

Incrementally, adding irrigation and soil to combined Plough produces 63,691
additional food for 9,685 additional measured worker-years in the endogenous
cohort, and 103,281 food for 6,254 worker-years in the fixed-field cohort. Soil
above water-only adds 27,418 food and saves 3,620 measured worker-years in the
endogenous cohort. These totals include policy responses and replacement costs;
they are not isolated historical productivity estimates.

Effects remain local and conditional. Seed 23 constructs new paid canals; seed
1729 never invests. Seed 101 has a negative endogenous incremental irrigation
food result despite the pooled gain. The high-Plough diagnostic (1.60/1.35)
reduces endogenous food by 3.249%, while improving the fixed-field cohort. This
non-monotonic policy response is a reason to retain the approved 1.50/1.25
baseline rather than select the largest coefficients or retune demography.
Half/double nonzero costs and moisture 1.00 were evaluated, but do not establish
a robust reason to replace the 0.50 target or baseline costs.

The retained engineering costs are 5 base Builder-years + 10 Housing Materials
per segment, plus prepaid first-period upkeep; ongoing upkeep is 0.25 base
Builder-years + 0.10 materials per segment-year. Generic tool productivity/service
applies normally. Missing materials are manufactured through shared reservations.
Water-only treatment sets soil capacity gain to zero while retaining its latent
soil clock, isolating the economic effect without rewriting saved field history.

## Organic first availability

Times are physical years since world generation, measured at Close before
diagnostic grants. Later inherited availability is retained in the raw manifest.

| Seed | Plough first year | Irrigation first year |
| --- | ---: | ---: |
| 11 | 549.81 | 293.85 |
| 23 | 585.59 | 647.32 |
| 47 | 553.88 | 295.35 |
| 101 | 533.88 | 312.15 |
| 1729 | 593.88 | not attained |

## Regression and compatibility

- **145 agricultural assertions** cover tool overlap/partial coverage/exhaustion/daily wear, fractional discovery and Close chronology, individual fertile/undeveloped/empty gates, absent need, affordability, continuity, route failures, activation and commissioned service, shared water, source conservation, river/lake soil, saturation/interruption, ownership/save-load, forecast/kernel agreement, pure prospective networks, and complete Elder replay.
- Soil milestone checks: 100/250/500 qualifying years yield 0.20/0.50/1.00; half supply requires 1,000 years; 365 daily increments equal one annual increment. Fixed-duration fixtures are numerical checks only.
- **All 69 registered headless suites passed** on the final simulation source, including the 154-assertion Housing Material recipe suite. Reported wall time: 64.2 s, peak working set 1,877 MiB; suites ran with three lanes.
- **28,787 smooth-calendar assertions passed**, covering anchors, continuity/monotonicity, schedule/save compatibility, duration stamps, native multi-seed scheduling, exact continuation and daily smoke.
- The carried-provision regression verifies that a shorter next horizon does not invalidate stock legally carried from the completed interval. The next provisioning limit is unchanged.
- Existing numeric IDs, legacy opt-out saves, action/observation layouts and active RL training are preserved. The existing learned-policy availability diagnostic is not an RL validation claim.

## Performance

The pure sparse-network allocator plus native source/field ecology kernels were
run for 1/16/80/320 networks, both 100 and 1,000,000 people per settlement, and
daily/20-year/600-year numerical horizons. Twenty repetitions per sample,
`DOTNET_TieredCompilation=0`; timings are local diagnostic measurements. Allocated
bytes are identical across population and duration for each network count.

| Active networks | Time range per evaluation | Allocated bytes |
| --- | ---: | ---: |
| 1 | 0.035–0.075 ms | 6,540 |
| 16 | 0.364–0.450 ms | 62,060 |
| 80 | 0.693–1.291 ms | 305,716 |
| 320 | 2.059–5.530 ms | 1,232,460 |

The existing whole-world compatibility benchmarks also passed scalar/parallel
replay and save/load: seed 1729, Continents, 80 and 320 initial settlements.
These use the unchanged legacy profile; the enabled infrastructure kernel is
measured separately above. They are not smoothed-calendar balance evidence.

| Initial settlements | Turns | Turn P50 / P95 | Peak working set |
| --- | ---: | ---: | ---: |
| 80 | 7 | 298.1 / 674.0 ms | 1,621 MiB |
| 320 | 10 | 1,115.2 / 1,858.7 ms | 6,216 MiB |

## Remaining acceptance limits

Unity EditMode/player interaction and visual inspection are **not performed**:
the editor is absent from this cloud workspace. Use Unity 6000.5.10f1 and the
new-game flag in the implementation contract to check inspector text/progress,
busy-state behavior, lack of construction override, save/load and turn advancement.
Full-map enabled 80/320-settlement long-campaign balance is not established by
the small-world causal sweeps or the separate kernel/legacy scale benchmarks.
The supported production model remains local gravity routing with horizon-level
operating/intensity assumptions, not detailed hydraulics or arbitrary within-turn
staffing changes. Keep the profile opt-in while completing interactive acceptance.
