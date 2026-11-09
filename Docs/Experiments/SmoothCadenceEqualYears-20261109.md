# Smooth calendar — equal historical time comparison (9 November 2026)

**Status:** COMPLETE. Three compact worlds and one production-size world ran all three schedules to the same 1,333-year endpoint. All checks passed in this test, but the calendar is not balance-neutral and production-scale reload remains a separate known blocker.

## Correct experimental contract

The user correctly required **the same total elapsed historical time** rather than matching the number of turns. Every arm starts from a saved, byte-identical post-opening world. The exact same source and rates apply. An arm's variable duration is computed using only the current elapsed physical year and its own schedule, and the final turn is shortened, if necessary, to stop at exactly the comparison endpoint.

The early campaign measures exactly **1,333 years from the Neolithic opening boundary**:

- Smooth: `dt_years(y)=20 exp[-ln(2)*y/1200]`, giving **100 actual turns**.
- Constant 20 years: **67 actual turns** (final turn is 13 years).
- Historical stepped comparison: 20 years while year <200, 15 years while year <350, and 10 years thereafter: **119 actual turns** (final turn is 3 years).

These counts emerge from the schedules. No experiment controller forces 100 turns across all treatments; the same physical end timestamp is the invariant.

For the compact-world seed 11, a separate mature-Neolithic fixture also runs over **exactly 365 days**, with 37 exponentially decreasing ten-day-scale turns, 37 fixed ten-day turns, or 365 daily turns. This is a Neolithic numerical smoke test at short horizons, **not a historical simulation of the year 1900 or 2000**.

Code: `Tools/SmoothCadence100/Program.cs` on `experiments/smooth-cadence-fixed-years-20261109`. All native `Assets/_Anthropocene/Simulation` and `Resources` files are identical to pinned main `07f3501c2d5e047e44d61e85cc0d4613b54572d6`. Experiments run in GitHub Actions, not on the user workstation, existing GCP VM, or active RL training environment.

## Completed compact results

Compact world = 256×128 tiles, 16 founders. Endpoints after precisely 1,333 historical years:

| Seed | Smooth 100 turns | Fixed20 67 turns | Stepped 119 turns |
|---|---:|---:|---:|
| 11 | 111,095 | 163,303 | 62,192 |
| 23 | 126,554 | 123,389 | 54,307 |
| 47 | 75,186 | 91,479 | 44,662 |

Worlds complete all prescribed decisions with full heuristic → plan → resolve and physical ecology, labor, demography, fission, immigration, trade and endogenous cultivation. All three compact jobs passed world invariants, population conservation, stock conservation and zero order rejections. Maximum observed relative ecological balance error is <4×10⁻¹⁶ in these three artifacts. Source output is explicitly by realized elapsed years.

The stepped schedule produces fewer people and settlements in every compact world. This is not a mathematical mass-balance failure; it indicates sensitivity to a different chronology of harvest and social/economic decisions, without isolating which paths contribute most. Food coverage remains near complete, so simple hunger is not the main explanatory variable.

Example seed 11 at the physical-year endpoint:

| Metric | Smooth | Fixed20 | Stepped |
|---|---:|---:|---:|
| Settlements | 136 | 205 | 80 |
| Agriculture-first median residents | ~1,000 | ~999 | ~943 |
| Fission events | 118 | 187 | 62 |

### One physical year, short horizons (seed 11)

All from the same 18,493-person checkpoint:

| Schedule | Turns | Elapsed physical years | End population | Births | Deaths |
|---|---:|---:|---:|---:|---:|
| Exponential ten-day scale | 37 | 1 | 18,529 | 602 | 566 |
| Fixed ten days | 37 | 1 | 18,528 | 601 | 566 |
| Fixed one day | 365 | 1 | 18,480 | 553 | 566 |

All passed numerical checks, including exact small-world save/replay. Difference in births at equal elapsed time suggests timestep-dependent timing or gating of demographic opportunities, even though the ledger conserves population. It is not proven that the fertility rate function alone is responsible.

## Production-size equal-years comparison — completed

[GitHub Actions run 42616621219](https://github.com/GeeseGaggle-211/Anthropocene/actions/runs/42616621219), completed all four jobs. Exact executed experiment commit `051b875cf35153b43b06bca5caaa3f7fc68dd54d`. Production artifact ID 13673422532, digest SHA-256 `649a0a667d52677729958ea3eb90cc2e6b97e166c9892d61fc6cf54b86353d67`.

The 1024×512, 80-founding-tribe world (seed 11) began with 11,937 people after native opening. **All three arms completed exactly 1,333 physical years from this same checkpoint.** Their number of decisions is *not* artificially matched.

| Metric | Smooth exponential (100 turns) | Fixed20 (67 turns) | Stepped 20→15→10 (119 turns) |
|---|---:|---:|---:|
| Final world population | **1,347,190** | **1,792,450** | **621,589** |
| Live settlements | 2,116 | 2,237 | 1,110 |
| Median agriculture-first settlement | 775 | 976 | 642 |
| Maximum agriculture-first settlement | 1,108 | 1,063 | 1,098 |
| Fission events | 2,011 | 2,132 | 1,005 |
| Migrant-person movements | 83,791 | 69,315 | 44,663 |
| Food-needs coverage | 99.9058% | 99.9892% | 100.0% |
| Population-weighted QoL | 0.78455 | 0.75321 | 0.79541 |
| Mean owned-footprint q | 0.36499 | 0.37986 | 0.35011 |
| Resident food reserve-years (aggregate) | 13.74 | 18.63 | 15.74 |

Relative to fixed20 at the same end date, smooth has **24.84% fewer residents**, and stepped has **65.32% fewer residents**. The difference is not explained by aggregate meal coverage: all three endpoints are effectively fully fed. The dominant first-order pattern is settlement proliferation and demographic/social decisions, not starvation. Fission and migration differ materially across arms, but this observational comparison does not identify one causal mechanism. The fixed20 control has lower measured QoL despite higher population; population alone is not a welfare criterion.

Native ecological and population bookkeeping: zero submitted-order rejections, all 100+67+119 authoritative resolutions completed, and per-arm whole-world invariant checks passed. Maximum relative ecological balance residual `4.4004637898424434e-16`; each arm's opening population + recorded births − recorded deaths exactly reconciles with its ending population. The three production arms together ran for `732.379` wall seconds on the GitHub-hosted runner. This is a single production seed, not a CI performance distribution.

The production experiment intentionally **did not rerun the earlier known large-save replay failure** (uncompressed save XML metadata header maximum 512 MiB near 2,100 settlements). Numerical/economic completion is not a save/load acceptance.

## Interpretation

The user's methodological correction is substantive. Compare **same world, same initial state, same physical end date, variable turn count**. The prior control that forced 100 decisions at each cadence was not an appropriate test of a fixed twenty-year calendar. The new fixed20 control naturally uses 67 decisions, and its population endpoint differs markedly from the old 100-turn fixed-duration benchmark.

**Assessment:** No numerical conservation catastrophe over the tested schedule, including 365 consecutive daily decisions in the compact smoke test. However, enormous population and settlement divergence across equally long campaigns is a **balance and timing-consistency NO-GO**. A smoothing function can be implemented, but promoting it as the main campaign schedule before normalizing physical-time rules and validating policy responsiveness is not justified. The short-duration daily birth divergence is an additional concern.

Next attribution gates: hold a single staffing/trade/migration policy fixed over matched years; separately normalize physical-time migration caps, return cooldowns, fission/contact counters, production stock/harvest cadence, and birth eligibility; rerun these same seed/fork fixtures; then measure effect per year, not per turn. Preserve late-game performance and persistence checks independently.

## Release blockers and next experiments

1. **Social and decision-event temporal semantics:** Calendar-dependent opportunities for fission, migration, contact and stock harvesting change long-horizon outcomes. Need fixed-policy/normalized physical-time attribution before promoting a schedule.
2. **Daily temporal accounting:** Small-world 365-day test is numerically valid but differs in cumulative births from a 37-turn schedule over the same year.
3. **Production save metadata:** Earlier production-size test hit `SaveCodec.Load`'s 512 MiB uncompressed metadata header cap near 2,100 settlements. The full-size equal-years campaign intentionally avoids late large-save replay; it cannot be a save-system acceptance.
4. **Campaign scale:** Modern 1-day resolutions imply thousands of authoritative transitions per decade. No modern-era simulation/control/throughput validation is provided here.

Cloud run: https://github.com/GeeseGaggle-211/Anthropocene/actions/runs/42616621219
