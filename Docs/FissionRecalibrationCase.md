# Fission recalibration case

Measurement only, no calibration change. Question: why does the heuristic reference lose 16% of its residents under
the shipped Save15 rules (settling grace and plant tents, `Docs/SettlingGrace.md`), and should fission be retuned?

## Setup

- **Policy and worlds:** the frozen reference checkpoint with heuristic movement, on the default 54 worlds (seeds
  2000-2019 and 2026-2059), 25 turns.
- **Tooling:** RL movement-audit rollouts with the bridge rule override (`ANTHROPOCENE_SIM_RULES`).
- **Diagnostics:** the fission diagnostics this branch adds, enabled with `ANTHROPOCENE_FISSION_DIAG=1`.
  - `Fission.Diagnostic` records every evaluation with its stage, mover, source and aggregate utility changes and the
    policy threshold.
  - `Fission.CounterfactualDiagnostic` re-evaluates the same frozen snapshot with every fission-decision read of the
    settled expectation forced to 1 (`Fission.ExpectationOneDiagnostic`). It is pure and commits nothing.
  - Both are null or off in play.
  - With the diagnostics on, the old-rule rollouts reproduce the earlier per-step fingerprints exactly (54/54).
- **Comparison:** old rules (both rules off) against the shipped rules, plus a threshold sweep under the shipped rules.
  The sweep scales all three policy thresholds (`FissionGainThreshold` 0.113, `FissionGainDiscourage` 0.080,
  `FissionGainEncourage` 0.0955) by k.

## 1. Where the residents went

Per world, at turn 25:

| | old rules | shipped | change |
|---|---|---|---|
| residents | 17,318 | 14,357 | -2,961 |
| founding camps | 4,383 | 4,292 | -91 (3%) |
| daughters (all generations) | 12,935 | 10,065 | -2,870 (97%) |
| fissions | 30.5 | 22.8 | -25% |
| daughter size at founding | 98.6 | 109.4 | +11% |

- 97% of the loss is fewer daughters: lineages spread to fewer sites.
- The founding camps that stay grow almost exactly as before.

## 2. Fission margins

Decided evaluations per world, binned by the mover's utility gain minus the policy threshold:

| margin | < -0.10 | -0.10 to -0.05 | -0.05 to -0.02 | -0.02 to 0 | >= 0 |
|---|---|---|---|---|---|
| old rules | 385.2 | 13.3 | 6.0 | 2.2 | 32.3 |
| shipped | 341.5 | 13.4 | 4.6 | 1.3 | 24.1 |

Counterfactual for the 30.5 fissions per world committed under the old rules, re-evaluated in the same state with
E = 1:

| outcome at E = 1 | per world |
|---|---|
| still proposed | 24.7 |
| just below the threshold (< 0.02) | 0.4 |
| 0.02-0.05 below | 0.7 |
| far below (> 0.05) | 4.6 |
| fails the source screen | 0.1 |

- About 5.8 of the old fissions per world existed only because the reset expectation discounted the arriving
  daughter's missing clothing and housing.
- Most of those 5.8 fail by more than 0.05, so a small threshold change cannot bring them back.
- The rest of the drop, about 1.9 per world, compounds through the trajectory: fewer daughters means fewer later
  parents.

## 3. After the split

| run | fissions | daughter survival to turn 25 | residents | lineage welfare | welfare per person-turn |
|---|---|---|---|---|---|
| old rules | 30.5 | 100% | 17,318 | 3.67 M | 15.35 |
| shipped | 22.8 | 100% | 14,357 | 3.11 M | 15.36 |

- Every daughter survives in every run.
- Welfare per person-turn is flat (15.26-15.38 across all runs, sweep included).
- The old extra fissions added headcount, and therefore total welfare, at unchanged per-capita welfare. They paid off
  in total welfare and population, not in welfare per resident.

## 4. Threshold sweep under the shipped rules

| k (x thresholds) | fissions/world | residents | vs old | lineage welfare | vs old | welfare per person-turn |
|---|---|---|---|---|---|---|
| 1.00 (shipped) | 22.8 | 14,357 | -17.1% | 3.11 M | -15.4% | 15.36 |
| 0.85 | 25.2 | 15,287 | -11.7% | 3.26 M | -11.2% | 15.35 |
| 0.70 | 29.3 | 16,735 | -3.4% | 3.54 M | -3.7% | 15.38 |
| 0.55 | 33.7 | 17,852 | +3.1% | 3.69 M | +0.6% | 15.36 |
| 0.40 | 40.3 | 19,706 | +13.8% | 3.99 M | +8.7% | 15.26 |

- The old rate of 30.5 fissions per world returns near k = 0.65: `FissionGainThreshold` about 0.073, Discourage
  about 0.052, Encourage about 0.062.
- Lower thresholds make daughters smaller (88.6 at k = 0.40).
- Per-capita welfare stays flat until k = 0.40, where it begins to fall.

## Reading

- The fission threshold is a pure density knob here. It moves settlement count, population and summed welfare
  together and leaves welfare per resident unchanged; no daughter dies at any setting.
- The 2.9k residents the grace removed came from an accounting artefact, not from better lives.
- Restoring them is a choice about how densely lineages should colonise, not a correction.
- Because the interim reward is summed welfare, any such retune also rewards the learner for headcount.
