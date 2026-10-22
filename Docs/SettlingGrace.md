# One-time settling grace

Status: plan of record for new worlds (`SimulationConfig.SettlingGraceOnce = true`, `OpeningConfig.json`
`Save15Rules.SettlingGraceOnce`). Saves without the Save15 envelope load with the rule off and keep the earlier
behaviour exactly.

## Problem

The settled expectation `E = floor + (1 - floor) min(1, StationaryYears / RudimentaryExpectationRampYears)` scales
the clothing and housing shortfall terms of QoL (`1 - E (1 - coverage)`). Before this rule every move of a non-village
camp set `StationaryYears = 0` (`TurnResolver.Move`, and the planning copy `TurnPlanner.PlanningCampAt`), so a camp
with long tenure dropped from `E = 1.0` to `0.40` after a one-tile move and its clothing and housing shortfalls counted
60% less. The model9 movement audit (RL `docs/MODEL9_MOVEMENT_AUDIT.md`) found a learned policy exploiting it: it
shuttled between its own housed tiles (49% of moves returned to one of the last two tiles), split more, and hid real
clothing and housing shortfalls.

The low expectation was meant only as a grace for tribes first settling after the Paleolithic transition.

## Rule

- **Authoritative state** (per camp, Save15 block): `SettlingYears` (Neolithic years lived by the lineage, never reset
  by a move), `SettledExpectationEstablished` (the grace is over) and `SiteYears` (years at the current tile, reset by
  every move). `StationaryYears` and `StationaryTurns` keep their meaning.
- **Completion condition.** The grace ends the first time the lineage has lived `RudimentaryExpectationRampYears`
  (100 years, five 20-year turns) of settled life, counted in `NeolithicProduction.ResolveNeeds` where
  `StationaryYears` accrues, or `StationaryYears` itself reaches the ramp. For a camp that never moves the two
  coincide with the first time `E` reaches 1.0. Counting lineage years rather than waiting for `E = 1` stops a camp
  that keeps moving from staying in the grace indefinitely; the horizon is the ramp's own.
- **After completion** `E = 1` (`NeolithicProduction.SettledExpectation`), whatever `StationaryYears` does; moving
  never lowers it.
- **Moves** still reset `StationaryTurns`, `SiteYears` and (outside village abandonment) `StationaryYears`, in the
  resolver and in the planning copy, so the planner and the resolver agree.
- **Site tenure.** The fixed- versus portable-shelter test in `DemandControl` (`demonstratedFixedTenure`) reads
  `NeolithicProduction.SiteTenureYears`: `SiteYears` under the rule, the historical `StationaryYears` without it.
- **Fission.** A daughter inherits her parent's `SettlingYears` and `SettledExpectationEstablished`; her `SiteYears`
  starts at zero at her new tile.
- All readers of an unresolved `E` (`ProductionDemand`, `Fission`, `SocialRelocation`) use
  `NeolithicProduction.SettledExpectation`, numerically identical to the old formula when the rule is off.

## Scaling, determinism and persistence

O(1) per camp per turn; no new scans. Deterministic, no new random streams. The rule fields are `[NonSerialized]`
and travel in the Save15 envelope (`SaveCodec`, `ANTHROPOCENE-15`): it is the Save14 layout with the subsistence
profile as extension flag 4, plus a trailing block with the rule flags, `ClothPerLightHousing` and each camp's
settling state. It is written only when either Save15 rule is on, so every world with both rules off keeps its
earlier envelope, payload and `SaveCodec.Fingerprint` byte for byte. `ConfigurationHash` adds a discriminator when
a Save15 rule is on.

## Validation

`Tools/Headless/SettlingGraceChecks.cs` (fixed suite): golden fingerprints of two generated Small(6) worlds after
13 turns with both rules off, recorded from a build of 557c6f9; the grace and established cases; moves reset
`StationaryTurns` and `SiteYears`; daughters inherit; the planning copy agrees with the resolver; site-tenure test;
Save15 round trip and three-turn continuation; earlier envelopes load with the rules off; `OpeningConfig.json`
consistency. A cross-build probe (base 557c6f9 versus this branch with both rules off, seeds 11 and 23, 30 turns,
plus loading the base build's saves and continuing 9 turns) matched every fingerprint.

## Measured effect (model9 S-s55, 54 default worlds, 25 turns)

RL movement-audit tools (RL `feature/r3-tenure`, bridge rule override `ANTHROPOCENE_SIM_RULES`), S-s55 with the
grouped region choice against the frozen reference with heuristic movement, both under the same rules. With both
rules off all 108 rollouts reproduce the audit's per-step fingerprints exactly.

| | current rule | grace | grace + plant tents |
|---|---|---|---|
| welfare / ref (reported) | 1.166 | 1.065 | 1.071 |
| welfare / ref at E = 1 | 1.135 | 1.065 | 1.071 |
| physical-move share; returns to one of last two tiles | 0.323; 48.9% | 0.408; 52.3% | 0.410; 52.2% |
| fissions per world (ref) | 70.6 (30.5) | 55.9 (23.0) | 55.9 (22.8) |
| settlements below 30 | 3.5% | 3.1% | 2.8% |
| clothing / housing coverage (population-weighted) | 0.932 / 0.863 | 0.926 / 0.868 | 0.926 / 0.868 |
| final QoL (at E = 1) | 0.889 (0.879) | 0.865 (0.865) | 0.866 (0.866) |
| 4-turn MOVE / STAY fork welfare (move wins) | 0.997, E = 1: 0.967 | 0.957 (12%) | 0.956 (12%) |
| full-episode MOVE / STAY fork welfare | 1.40, E = 1: 1.35 | 1.089 | 1.103 |

The reported and E = 1 gaps now coincide: the accounting artefact is gone. The reference also changes (residents
-16%, fissions 30.5 to 23.0), because planners and fission now value clothing and housing at the full expectation.
S-s55 was trained under the old rule and still shuttles; it must be retrained before its movement can be judged.

## Reference shift decomposition

Heuristic reference only (frozen reference checkpoint, heuristic movement), 54 worlds, 25 turns, one rule at a time.
The two switches used here are measurement-only statics (`NeolithicProduction.SiteTenureRepointDiagnosticOff`,
`GraceTenureCompletionDiagnostic`), set through the RL bridge's `ANTHROPOCENE_SIM_STATICS`.

| reference run | residents | fissions/world | settlements | welfare |
|---|---|---|---|---|
| current rule | 17,318 | 30.5 | 39.4 | 0 |
| plant tents alone | -0.9% | 30.0 | 38.9 | -0.7% |
| grace, tenure test not repointed | -16.4% | 23.0 | 31.6 | -15.1% |
| grace (shipped) | -16.4% | 23.0 | 31.6 | -15.1% |
| grace + tents (shipped) | -17.1% | 22.8 | 31.3 | -15.4% |
| grace ending only when the old ramp reaches 1.0 | -13.5% | 24.1 | 32.8 | -12.9% |
| that variant + tents | -13.9% | 23.9 | 32.6 | -13.1% |

- The site-tenure repoint has no measurable effect, and the tents almost none. The shift comes from the grace.
- It is not caused by never-moving camps reaching E = 1 sooner. Under the current rule, non-moving camps follow the
  old ramp exactly: mean E 0.88 at episode start, 1.00 by turn 16. Under the grace every lineage has already lived
  100 Neolithic years during the opening, so E = 1 from the start of the episode. Only 4.0% of non-moving camp-turns
  differ from the old ramp, all of them camps that moved earlier.
- It comes from moving camps. The heuristic moves on 4% of camp-turns. Under the old rule each move reset E to 0.40,
  which raised the arriving camp's and its daughter's estimated QoL and drove distress fission. That discount is the
  artefact being removed, so the ordinary game loses it too.
- The old-ramp completion variant recovers only 3 of the 16 points. It also lets camps that never stay 100 years
  remain in the grace: with it, S-s55's E averages 0.990 and its reported welfare / ref (1.058) exceeds its E = 1
  value (1.054). It is not adopted.
- Holding the reference's fission and population at their old level would need a separate recalibration of fission,
  not a different completion rule.
