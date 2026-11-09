# Smoothly decaying turn duration — 100-turn native economy stress test

Date: 9 November 2026. Status: 3/3 compact-world campaigns complete; production-size campaign still executing when this preliminary record was written. This experiment changes no production simulation source and does not alter active RL environments.

## Source and method

Source baseline `07f3501c2d5e047e44d61e85cc0d4613b54572d6`. Experiment head `e0c3ac6cf7c5588b818ad8ad30e39218d8173bd6`. [Actions workflow 42595221792](https://github.com/GeeseGaggle-211/Anthropocene/actions/runs/42595221792).

Native C# whole-economy AI → Prepare → Resolve, with production, ecological stocks, demographic births/deaths, fission, migration, trade, craft, fields, tools, and housing. Fixed field states are **not** injected. Openings resolve at the shipped twenty-year interval; every arm starts from an identical saved post-opening world.

The experimental early-game decay is `dt_years(t)=20 exp[-ln(2)*t/1200]`, where t is years since the Neolithic opening. It begins at 20 years, declines to 9.31046 years by the hundredth decision, and spans 1333.000998 simulated years. The primary control is exactly 100 turns at a constant 13.33000998 years, matching the same elapsed physical time. A fixed 20-year 100-turn arm spans 2000 years and is **not** an elapsed-time-matched population comparison.

The compact tests have 256×128 worlds, 16 founders, seeds 11, 23 and 47. One production-size 1024×512, 80-founder run is a separate performance/stability gate (pending). All reported outcomes are native heuristic policies, not learned RL behavior.

For seed 11 only, a saved mature-Neolithic fixture was forked after 300 simulated years and resolved for 100 turns with (a) the virtual-1900 exponential `dt_days(t)=10exp[-ln(10)*t/100]`, t in years, (b) its equal-100-turn/equal-elapsed-time constant-duration match, and (c) 100 consecutive one-day turns. These are Neolithic physics under short time intervals, **not** a simulated industrial or information society.

## Compact-world outcomes (complete)

| Seed | Initial resident+detached pop | Smooth after 100 turns | Fixed time-matched after 100 turns | Difference smooth vs matched | Fixed 20-year after 100 turns (2000 years) |
|---|---:|---:|---:|---:|---:|
| 11 | 1,668 | 111,095 | 76,799 | +44.66% | 168,048 |
| 23 | 1,799 | 126,553 | 84,530 | +49.71% | 126,753 |
| 47 | 1,877 | 75,183 | 93,054 | −19.21% | 91,917 |

All 9 compact early arms completed 100 turns. Each passed native whole-world invariants, exact initial+gross births−gross deaths=final population, no submitted-order rejections, and food/biomass stock conservation. Largest measured relative ecological-balance residual across these three seed artifacts: 4.36e−16. All measured outcomes depend on the native timing of future decisions, resource extraction, migration, fission, domestication and keyed randomness, so **the calendar is not shown to be balance-neutral**. Population divergence is not a mass-balance defect and must not be explained as a pure food-yield increase.

### Subannual smoke (seed 11, 100 turns from identical mature-Neolithic state)

| Arm | Elapsed physical years | Starting population | Final population | Migrant-person movements |
|---|---:|---:|---:|---:|
| Ten-day-scale exponential near virtual 1900 | 2.657568 | 18,493 | 18,596 | 146 |
| Constant duration matched in elapsed years | 2.657568 | 18,493 | 18,608 | 146 |
| Daily floor | 0.273973 | 18,493 | 18,479 | 129 |

All three passed conservation, whole-world invariants and zero-rejection checks. Seed 11 exact save/load replay checked the early arms at steps 1/51/100, plus the ten-day-scale and daily arms at steps 1/51/100.

The annualized migrant-person movement count is ~55/year in the ten-day-scale arm versus ~471/year in the daily arm (an ~8.6× ratio). These unequal-duration observations cannot identify a pure migration coefficient, but they reveal why per-turn migration limits, return cooldowns, fission opportunities and domestication-contact counters need explicit physical-time semantics before enabling the 2050 daily clock. Migrants are movements, not unique people.

## Production-size persistence failure (confirmed)

The first 1024×512, 80-founding-tribe run reached 99 completed smooth turns (1323.69 years), 1,299,036 people and 2,098 settlements. Its production simulation had passed the recorded per-turn conservation and invariants through those 99 transitions. Before turn 100, the experiment's deliberate save/load replay witness failed at `SaveCodec.Load` with `InvalidDataException: Invalid save metadata size.` Evidence: [Actions run 42595221792](https://github.com/GeeseGaggle-211/Anthropocene/actions/runs/42595221792) and production artifact 13663452102.

The production source's `SaveCodec.Load` reads the uncompressed XML metadata header length and rejects values greater than `MaximumMetadataBytes = 512*1024*1024` (or less than one). `SaveCodec.Save` had returned bytes; the throw occurred on Load, before the next world transition. `SaveCodec.CopyWithoutTiles` includes the entire `WorldState.History`, and each `TurnRecord` can contain large settlement and trade lists. Thus growing historical metadata is a plausible root cause, but this experiment has not yet isolated which member dominates or measured the exact header length. Do not simply double the cap without profiling the constituents.

A subsequent production-only continuation omits the repeated large-save replay witness so that the full 100-turn economy and time-matched control can complete independently. This does **not** waive the save defect. Production results must distinguish numeric progression from reload/persistence acceptance.

## Acceptance and blockers

**Numerical gate (compact): PASS for the tested schedules and population/ecology accounting.**

**Physical/social-time semantics: NOT CLEARED.** A calendar change currently changes social opportunity frequency, discrete movement and contact maturation, planning cadence and land opening-stock harvest frequency. Matching elapsed physical time alone does not imply comparable trajectories. The native land pulse-harvest contract (#29) remains open. The observed early smooth-versus-flat population variation reaches approximately +50% and −19% across only three seeds; the economic model needs attribution and more seeds before accepting balance.

**Daily model: SMOKE ONLY.** 100 daily turns passed numerical checks with negligible duration, but no historical-era technology, global daily-performance budget or life-cycle saturation beyond those 100 turns was tested.

**Next gates:** correct horizon-sensitive draft reuse, classify and normalize time-dependent contact and migration rules, compare matched physical-year forage/harvest under fixed policy, validate observation/AI/service budgets near one day, and evaluate 100-turn modern windows with their actual era-specific economies once implemented. Keep smooth-calendar logic in the isolated experiment and retain compatibility for old saved durations. No user-approved production calendar curve or historical anchor dates were committed.

Exact per-seed trajectories, checks, configuration hashes and the experiment harness are in the per-job artifacts attached to the Actions workflow.
