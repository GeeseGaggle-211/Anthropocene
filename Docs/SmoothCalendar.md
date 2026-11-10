# Smooth calendar integration v1

Status: isolated implementation PR; not a demographic rebalance.

## Authority and activation
`SimulationConfig.SmoothCalendar == null` preserves fixed `TurnDays`, including old saves and the shipped `OpeningConfig.json`. The new `SmoothCalendarOpeningConfig.json` is an explicit new-game profile. Set the config before generation; do not mutate the clock during observation/planning. Normal construction retains the existing fixed behavior unless the profile is chosen. The global epoch is committed once all founding societies finish the opening; this is not a separate clock for each society.

## Curve
The first portion is the measured early exponential, `20 exp(-ln(2)*elapsedYears/1200)` years. Historical mode uses a provisional, serialized Neolithic epoch of -5000 CE on a continuous mathematical year axis. This date is a configurable pacing assumption, not a historical claim or technology gate. A monotone C1 cubic in log duration joins the early curve at 1750 to the modern exponential at 1800. Modern duration is `10 exp(-ln(10)*(year-1900)/100)` days: 100 days at 1800, 31.6228 at 1850, 10 at 1900, 3.1623 at 1950, and 1.5849 at 1980. Another C1 log-duration join over 1990–2000 reaches a one-day floor. The floor remains positive through 2050 and beyond; this module does not impose an end-game victory date. Version 1 requires the engine's existing 365-day-year convention.

Alternative curve `EarlyExponential` retains the equal-years experiment's early equation, with a one-day lower bound. It is for controlled attribution, not a finalized alternate historical campaign.

## Boundary contract
All systems within one turn see the same immutable duration. `TurnResolver` computes the next duration only after the previous economy, demography, social and knowledge passes have committed. `TurnRecord` retains the actual old start/duration. `PreparedTurn` and `PlanningDraft` carry a value snapshot of the calendar and duration; changed settings invalidate reuse and reject prepared resolution. AI observation/planning and save loading reject stale smooth horizons rather than silently replacing a prepared plan. No mutation occurs in pure forecasts.

## Compatibility and scope
Legacy missing/null settings load fixed; the new profile is opt-in and creates no automatic migration. BaseDays, DaysPerYear, PracticeDays, material-life units, births/deaths, migration/fission rules, harvest kernel and RL schemas are unchanged. The earlier causal-demography investigation and 512-MiB historical save-header issue are deferred, not fixed or claimed as this PR's acceptance gates. Large-save reload failure remains independently documented. Existing running RL environments, checkpoints and branches are untouched.

## Validation
Run `dotnet run -c Release --project Tools/SmoothCalendar/Anthropocene.SmoothCalendar.csproj` from repository root. Focused checks cover numeric anchors, monotonicity and smooth joins, invalid config, native opening, three-seed automatic scheduling, records/stock/population invariants, exact save continuation, duration/config cache invalidation, legacy behavior and a daily numerical smoke. Run the registered headless suites independently. These tests do not establish a complete industrial economy, global daily performance, or timestep-independent gameplay.

## Design deliverables
Irrigation and Plough are separate implementation designs, not code effects bundled into this scheduler PR. Their accepted mechanisms do not alter q_k or the demographic model.
