# Fission pressure experiment — initial results

**Experiment lineage:** `experiment/fission-exit-pressure`, child of `feature/elder-rl-scaffold`.

**Matched-world behavior commit:** `a77b2f8866199a57ce1aa0f09abf167c1715b15a`.

**Focused checks:** GitHub Actions run 37644078897: 655 fission assertions passed in 7.6 s. Subsequent behavior-neutral diagnostic changes only improve invariant error messages.

## Question

Compare three social-pressure laws while holding the modern destination machinery fixed:

1. `Current`: crowding/stayer gain + food/water distress.
2. `LegacyQoL`: exact WP10a low-QoL pressure constants, but modern radius-4 search, spacing, site support, daughter shelter and relative vetoes.
3. `CurrentPlusExitOpportunity`: Current plus
   `(1 - Q_home) * max(0, Q_daughter_expected - Q_home)`, with the daughter comparison made at the parent's expectation.

The production default remains `Current`. None of these results changes the plan of record.

## Focused mechanism result

The modern site constraints do **not** rehabilitate WP10a.

A focused fixture proves that `LegacyQoL` can still split a 40-person parent into a 28-person parent and a 12-person daughter—the minimum legal Neutral split—through the modern radius-4 destination search and daughter/stayer veto.

The generalized exit channel does not automatically split the equivalent minimum-size fixture when the daughter offers only a marginal welfare advantage.

## Matched worlds

Configuration: Continents, 80 founding tribes; seeds 11, 23, 47, 101, 1729; production config; up to 12 opening turns plus 40 post-opening turns; four workers; detailed metrics enabled. The three rules are the only intended behavior difference.

All five Current and all five CurrentPlusExitOpportunity runs completed. LegacyQoL completed four seeds. Seed 11 failed at post-opening turn 22; see "Legacy failure" below.

### Current versus CurrentPlusExitOpportunity — same five seeds

| Seed | Current final pop | Exit final pop | Pop change | Current N×Q | Exit N×Q | N×Q change | Current / Exit fissions (post-opening) |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 11 | 61,962 | 65,349 | +5.5% | 904,823 | 939,153 | +3.8% | 82 / 110 |
| 23 | 103,657 | 119,163 | +15.0% | 1,284,940 | 1,407,657 | +9.6% | 177 / 226 |
| 47 | 108,287 | 125,735 | +16.1% | 1,185,361 | 1,314,051 | +10.9% | 221 / 294 |
| 101 | 95,570 | 121,200 | +26.8% | 1,168,120 | 1,357,924 | +16.2% | 162 / 265 |
| 1729 | 88,937 | 94,867 | +6.7% | 1,151,193 | 1,216,459 | +5.7% | 151 / 186 |

Here N×Q is the sum of each post-opening turn's population times prior-cohort weighted QoL. Turn duration is constant in this experiment, so it is proportional to time-integrated welfare; it is not normalized to person-years.

Across the same five worlds:

| Metric | Current | Current + exit opportunity |
|---|---:|---:|
| Mean final population | 91,683 | 105,263 |
| Mean final camps | 239.8 | 298.8 |
| Mean post-opening prior-cohort QoL | 0.8581 | 0.8607 |
| Mean post-opening N×Q / world | 1,138,887 | 1,247,049 |
| Fissions, entire run | 799 | 1,094 |
| Opening fissions | 6 | 13 |

The exit variant improved N×Q in **all five seeds**, by 3.8–16.2%, while average QoL was slightly higher rather than lower. This is encouraging but is not by itself acceptance: fission creates another independent local footprint, so a rule that fissions more often has a structural route to higher world population and total welfare.

Trigger counts over the full runs:

- Current: 784 Crowding, 15 Distress.
- CurrentPlusExitOpportunity: 693 Crowding, 388 ExitOpportunity, 13 Distress.

The exit channel changes later trajectories; its 388 exits do not simply add on top of the same 799 current fissions.

### What the provisional exit channel is actually doing

Post-opening ExitOpportunity events: 381.

- Parent population P25 / median / P75: **163 / 196 / 209**.
- No Paleolithic or Nomadic ExitOpportunity events occurred; all were classified Agricultural.
- The post-opening ExitOpportunity parents were overwhelmingly non-villages and portable-shelter dominated.
- Median reported committed QoL after the turn was about 0.851. This is **not** the decision's `Q_home`: fission evaluates its pure QoL formula at the post-demography headcount, while the stored camp QoL is the already committed prior-cohort outcome. Do not use that stored value to calibrate the exit threshold.
- Food, water and clothing were fully met at the median ExitOpportunity event. The new channel is therefore mostly acting as an early decentralization/escape pressure through the welfare advantage of a smaller daughter, not as a second starvation trigger.

This means the first `0.05` exit threshold is a **research setting**, not a recommended production calibration. It has not yet demonstrated the desired future case of an established fixed village under taxes, requisitions or deliberate service suppression.

## LegacyQoL result

Four completed LegacyQoL worlds produced **530 total fissions**, of which **319 occurred during the opening**.

Across those four runs:

- parent population P25 / median / P75: **42 / 46 / 53**;
- committed parent QoL P25 / median / P75 at event readback: **0.000 / 0.262 / 0.486**;
- regimes: 119 Paleolithic, 165 Nomadic, 246 Agricultural.

The historical minimum-size fragmentation pathology therefore survives the modern search/spacing/daughter machinery. Modern destination quality is not the missing fix.

### Legacy seed 11 failure

Seed 11 consistently fails at post-opening turn 22 after camps grow from 80 to 188. A behavior-neutral invariant diagnostic identifies:

```
camp=2660 pop=12 assigned=13 food=1271.0949777929081
tile=263908 canEnter=True
```

So the failure is a small-camp workforce over-assignment, not an invalid daughter tile. This is a separate correctness bug exposed by the aggressive small-settlement regime. It should be fixed independently if the engine must support arbitrary high-fragmentation policies, but it does not rescue the LegacyQoL pressure law.

## Interpretation

1. **Reject direct restoration of WP10a.** Low absolute QoL still creates minimum-size fragmentation even with the modern site search and viability constraints.
2. **N×Q is a reasonable Elder objective, but not the sole social constraint.** An independent exit process remains valuable because local total welfare can tolerate significant individual deprivation when population is large.
3. **The generalized exit-opportunity form is materially more promising than an absolute QoL threshold.** It requires both dissatisfaction and a credible relative improvement, and it passed the minimum-size focused counterexample.
4. **Do not ship the current 0.05 threshold.** It increased full-run fission by roughly 37% in the five matched worlds and mostly acted on ~200-person portable settlements. Calibrate only after targeted mature-village/oppression fixtures and a threshold sweep.
5. **Do not select the rule by world N×Q alone.** More daughter footprints mechanically expand aggregate carrying opportunity. Acceptance needs local welfare/escape targeting, daughter continuation, false-positive rates, and later political cases in addition to total welfare.

## Next recommended test

Before the learned Elder is trained, add targeted fixed-settlement fixtures in which food/water remain adequate but an external obligation or controlled service suppression lowers local welfare. Sweep the exit threshold/search fraction and require:

- no minimum-size fragmentation;
- a substantial daughter advantage before exit;
- credible daughter provisioning;
- limited exit from high-welfare settlements;
- deterministic behavior and bounded search;
- preservation of the current crowding and food/water distress mechanisms.

The experiment branch should remain isolated until that calibration is complete.
