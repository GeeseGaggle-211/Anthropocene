# Fission pressure experiment

**Status:** experimental only. Branch `experiment/fission-exit-pressure`, based on `feature/elder-rl-scaffold`.

## Question

Does the old low-QoL fission pressure become useful once it is paired with the current destination machinery, and is a generalized exit-opportunity channel a better anti-oppression mechanism for the Village Elder?

The experiment isolates the **pressure law**. All three variants use the same current:

- minimum camp spacing;
- radius-4 bounded destination search;
- current projected ecology and daughter cohort support;
- richer-site then distance destination ordering;
- daughter first-turn shelter estimate;
- carried-food logic;
- daughter/stayer viability veto;
- deterministic reconcile/commit.

## Variants

### A. `Current`

Production rule:

- crowding/stayers' expected gain;
- food/water distress pull.

### B. `LegacyQoL`

Historical WP10a pressure only, with exact old constants:

- `Q_fiss = 0.75`;
- width `0.10`;
- proposal threshold `0.20`;
- search push `0.20`;
- policy shifts: Discourage `-0.05`, Neutral `0`, Encourage `+0.05`.

Unlike historical WP10a, destination selection and daughter/stayer validation are current. This is deliberate: it tests whether the modern site constraints rehabilitate the old social-pressure idea.

### C. `CurrentPlusExitOpportunity`

Keeps A and adds an independent escape channel:

```
P_exit = (1 - Q_home) * max(0, Q_daughter_expected - Q_home)
```

The daughter comparison is **expectation-neutral**: `Q_daughter_expected` uses the parent's expectation on both sides, so moving cannot create pressure merely by resetting material expectations. The actual post-move daughter QoL remains a separate viability veto.

Provisional experiment defaults:

- `FissionExitThreshold = 0.05`;
- `FissionExitSearchFraction = 0.8`.

These are sweep parameters, not proposed gameplay calibration.

## Focused checks

`Tools/Headless/FissionChecks.cs` now asserts that:

1. production defaults remain `Current`;
2. experimental constants serialize/validate without changing OpeningConfig;
3. the legacy trigger has zero pressure at Q=0.75 and can split below it;
4. `LegacyQoL` uses the current radius-4 search when the distance-3 ring is unviable;
5. `Current` remains independently selectable;
6. exit pressure increases with both dissatisfaction and daughter improvement;
7. a fed low-welfare parent can use the exit channel even when ordinary crowding is suppressed;
8. a well-served parent does not fragment merely because another habitable site exists;
9. no exit occurs when no habitable known destination exists.

## Matched world experiment

Use identical generated worlds and only change `FissionPressureRule`.

Suggested first bank: Continents, 80 founding tribes, seeds 11, 23, 47, 101, 1729; production config; opening plus 40 post-opening turns. Keep trade, immigration, QoL mortality and the economic planner identical.

Example:

```powershell
$rules = @('Current','LegacyQoL','CurrentPlusExitOpportunity')
$seeds = @(11,23,47,101,1729)
foreach ($rule in $rules) {
  foreach ($seed in $seeds) {
    & $sdk $audit --players=80 --seed=$seed --shape=Continents --production-config=1 `
      --turns=12 --post-opening-turns=40 --workers=4 --partition-size=64 --warmup=0 `
      --details=1 --metrics=1 --fingerprints=0 `
      --set="FissionPressureRule=$rule" `
      --output="TestResults/FissionPressure/$rule-$seed.json"
  }
}
```

For timing decisions, repeat with `--metrics=0 --details=0`; detailed counters are for behavior attribution, not clean wall-time comparison.

## Required report

Per variant and seed, report:

- total fissions and fissions by trigger;
- parent population at fission: P25 / median / P75;
- parent Q at fission and daughter expected/actual Q;
- fraction of parents with Q < 0.5, 0.6, 0.7 that fission within the next 1/3 turns;
- Paleolithic/nomadic false-positive fission before normal economic maturity;
- daughter one-, three- and five-turn survival/continuation;
- repeated fission by the same lineage;
- final camp count and population distribution;
- prior-cohort population-weighted QoL;
- time-integrated `N * Q` at settlement and world level;
- deaths, emigration/immigration, distress fission and exit-opportunity fission;
- search counts/candidates and turn cost.

## Decision criterion

The purpose is **not** to maximize fissions.

A useful anti-oppression channel should:

- materially increase escape from persistently low-welfare settlements when credible better sites exist;
- not recreate the WP10a minimum-size fragmentation pathology;
- not fragment high-welfare settlements for marginal opportunity;
- preserve current spacing/viability/conservation/determinism;
- add bounded computation.

If `LegacyQoL` still fragments early camps under the modern search, reject it. If `CurrentPlusExitOpportunity` produces targeted escape without broad fragmentation, it is the preferred candidate for the RL environment.
