# Smooth calendar — equal historical time comparison (9 November 2026)

**Status:** Compact-world tests complete; production-sized campaign under execution at publication of this preliminary evidence. The final production section will be filled only from its completed artifact.

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

## Release blockers

1. **Social and decision-event temporal semantics:** Calendar-dependent opportunities for fission, migration, contact and stock harvesting change long-horizon outcomes. Need fixed-policy/normalized physical-time attribution before promoting a schedule.
2. **Daily temporal accounting:** Small-world 365-day test is numerically valid but differs in cumulative births from a 37-turn schedule over the same year.
3. **Production save metadata:** Earlier production-size test hit `SaveCodec.Load`'s 512 MiB uncompressed metadata header cap near 2,100 settlements. The full-size equal-years campaign intentionally avoids late large-save replay; it cannot be a save-system acceptance.
4. **Campaign scale:** Modern 1-day resolutions imply thousands of authoritative transitions per decade. No modern-era simulation/control/throughput validation is provided here.

Cloud run: https://github.com/GeeseGaggle-211/Anthropocene/actions/runs/42616621219
