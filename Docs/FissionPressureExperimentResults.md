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

At the time of this first matched run the production default remained `Current`; the subsequent threshold sweeps below promoted `CurrentPlusExitOpportunity @ 0.05` to the working baseline.

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

### What the exit channel is actually doing

Post-opening ExitOpportunity events: 381.

- Parent population P25 / median / P75: **163 / 196 / 209**.
- No Paleolithic or Nomadic ExitOpportunity events occurred; all were classified Agricultural.
- The post-opening ExitOpportunity parents were overwhelmingly non-villages and portable-shelter dominated.
- Median reported committed QoL after the turn was about 0.851. This is **not** the decision's `Q_home`: fission evaluates its pure QoL formula at the post-demography headcount, while the stored camp QoL is the already committed prior-cohort outcome. Do not use that stored value to calibrate the exit threshold.
- Food, water and clothing were fully met at the median ExitOpportunity event. The new channel is therefore mostly acting as an early decentralization/escape pressure through the welfare advantage of a smaller daughter, not as a second starvation trigger.

This first result did not yet justify production calibration by itself; the subsequent coarse and fine sweeps below established `0.05` as the working baseline. It still has not demonstrated the future case of an established fixed village under taxes, requisitions or deliberate service suppression.

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


## Percentile comparison: Current versus CurrentPlusExitOpportunity

The aggregate means above can hide seed-specific regressions, so the five matched worlds were also compared as distributions. P25 / median / P75 below are taken across the five final worlds unless explicitly labeled as settlement-level.

### World-level and Husbandry-track outcomes

| Metric | Current P25 / median / P75 | Exit P25 / median / P75 | Relative change at P25 / median / P75 |
|---|---:|---:|---:|
| Total population | 88,937 / 95,570 / 103,657 | 94,867 / 119,163 / 121,200 | +6.7% / +24.7% / +16.9% |
| Prior-cohort weighted QoL | 0.8547 / 0.8551 / 0.8589 | 0.8596 / 0.8608 / 0.8620 | +0.6% / +0.7% / +0.4% |
| Husbandry resident population | 54,605 / 62,120 / 70,357 | 59,581 / 85,593 / 87,558 | +9.1% / +37.8% / +24.4% |
| Husbandry resident-weighted QoL | 0.8528 / 0.8588 / 0.8639 | 0.8669 / 0.8704 / 0.8728 | +1.6% / +1.4% / +1.0% |
| Managed Husbandry animals | 35,516 / 54,464 / 61,181 | 41,599 / 59,032 / 71,840 | +17.1% / +8.4% / +17.4% |
| Husbandry settlements | 165 / 168 / 184 | 198 / 231 / 271 | +20.0% / +37.5% / +47.3% |

There is therefore **no world-level percentile regression** in the requested success metrics. Population improves at P25, median and P75; QoL improves at all three; Husbandry population improves strongly; and total managed Husbandry animals improve at all three percentiles. The QoL gain is modest in absolute terms but consistently positive.

### Per-Husbandry-settlement structure

Pooling final settlements across all five worlds shows the cost of those gains:

| Metric | Current P25 / median / P75 | Exit P25 / median / P75 |
|---|---:|---:|
| Husbandry settlement population | 168 / 265 / 494 | 134 / 230 / 457 |
| Husbandry settlement QoL | 0.8475 / 0.9039 / 0.9427 | 0.8656 / 0.9246 / 0.9594 |
| Managed animals per Husbandry settlement | 66 / 175 / 352 | 55 / 139 / 285 |

So the proposed rule does **not** increase the size of the typical individual pastoral/mixed settlement. It increases the number of functioning Husbandry settlements and raises their QoL, producing a higher total Husbandry population and a larger total managed herd across each matched world.

A related intensity diagnostic is total managed animals per Husbandry resident, evaluated per world:

- Current P25 / median / P75: **0.650 / 0.774 / 0.856 animals per resident**.
- Exit P25 / median / P75: **0.690 / 0.698 / 0.772 animals per resident**.

The P25 improves, but the median and P75 fall by about 9.8%. This is a real structural shift toward more numerous, smaller, somewhat less herd-intensive Husbandry settlements. It is not hidden by the aggregate improvement and should be watched in later threshold calibration.

### Interpretation

For the stated non-regression gate, the exit rule passes at the **world/Husbandry-track level**: no P25, median or P75 regression in total population, weighted QoL, Husbandry population, Husbandry QoL or total Husbandry animals.

It does **not** pass a hypothetical requirement that each individual Husbandry settlement or pastoral household maintain at least the same herd size/intensity. The experiment currently trades some local herd concentration for more independent settlements, higher local QoL and greater total Husbandry carrying capacity.

This is acceptable only if the design goal is heterogeneous decentralization rather than preserving large pastoral concentrations. Future threshold sweeps should therefore track both total Husbandry animals and animals per Husbandry resident, so improved aggregate welfare cannot conceal excessive fragmentation of pastoral economies.


## Threshold sweep and baseline decision

User decision: **CurrentPlusExitOpportunity with FissionExitThreshold = 0.05 is the new working baseline.**

A coarse sweep (0.05 / 0.075 / 0.10 / 0.125) showed that 0.075 already suppresses nearly all exit events and gives back a large share of the new baseline's population/Husbandry gains. A fine sweep therefore tested 0.055 / 0.060 / 0.065 / 0.070 on the same five seeds.

All 20 fine-sweep simulations completed without invariant failures.

### Fine sweep: world-level P25 / median / P75

| Threshold | Total population | Population-weighted QoL | Husbandry population | Husbandry weighted QoL | Managed Husbandry animals |
|---:|---:|---:|---:|---:|---:|
| **0.050** | **94,867 / 119,163 / 121,200** | **0.8596 / 0.8608 / 0.8620** | **59,581 / 85,593 / 87,558** | **0.8669 / 0.8704 / 0.8728** | **41,599 / 59,032 / 71,840** |
| 0.055 | 92,057 / 109,022 / 113,680 | 0.8584 / 0.8589 / 0.8597 | 58,124 / 76,053 / 80,002 | 0.8644 / 0.8671 / 0.8683 | 39,193 / 57,925 / 65,417 |
| 0.060 | 88,656 / 106,927 / 107,905 | 0.8571 / 0.8572 / 0.8608 | 54,726 / 73,668 / 74,936 | 0.8569 / 0.8649 / 0.8680 | 34,751 / 54,752 / 65,782 |
| 0.065 | 88,123 / 105,378 / 107,889 | 0.8548 / 0.8556 / 0.8593 | 54,193 / 72,399 / 74,920 | 0.8536 / 0.8615 / 0.8655 | 34,276 / 54,294 / 65,329 |
| 0.070 | 87,791 / 104,964 / 106,318 | 0.8548 / 0.8573 / 0.8598 | 53,861 / 72,535 / 73,349 | 0.8538 / 0.8640 / 0.8660 | 33,978 / 55,108 / 65,161 |
| 0.075 | 87,782 / 93,751 / 104,478 | 0.8552 / 0.8568 / 0.8569 | 53,852 / 60,782 / 71,498 | 0.8541 / 0.8613 / 0.8633 | 33,979 / 55,064 / 59,595 |

Even the smallest upward adjustment, 0.055, fails the strict non-regression criterion against 0.05. Relative to 0.05 its P25 / median / P75 changes are:

- total population: **-3.0% / -8.5% / -6.2%**;
- Husbandry population: **-2.5% / -11.2% / -8.6%**;
- managed Husbandry animals: **-5.8% / -1.9% / -8.9%**;
- weighted QoL: -0.14% / -0.22% / -0.26%;
- Husbandry weighted QoL: -0.28% / -0.39% / -0.52%.

### Fragmentation trade-off

| Threshold | Mean final camps | Total fissions | Exit fissions | Husbandry settlements (P25 / median / P75) |
|---:|---:|---:|---:|---:|
| **0.050** | **298.8** | **1,094** | **388** | **198 / 231 / 271** |
| 0.055 | 270.6 | 953 | 237 | 181 / 212 / 230 |
| 0.060 | 259.8 | 899 | 107 | 168 / 202 / 220 |
| 0.065 | 254.0 | 870 | 62 | 165 / 194 / 218 |
| 0.070 | 252.4 | 862 | 35 | 166 / 195 / 211 |
| 0.075 | 242.4 | 812 | 20 | 166 / 170 / 194 |

At 0.055, the engine buys roughly 9% fewer final camps and 13% fewer total fissions, but pays materially more than that in median population and Husbandry population. Above 0.060, the exit channel is rapidly approaching the current-rule behavior.

Per-Husbandry-settlement herd concentration increases as the threshold rises, confirming the expected trade: less fission produces fewer, larger and more herd-intensive settlements. However, the resulting loss in total Husbandry residents and total managed animals is too large to justify the concentration gain under the agreed baseline criteria.

### Decision

Keep **0.05**. It is not merely the best point in the original coarse sweep; the fine sweep shows that the useful behavior sits below a sharp threshold cliff. Raising the threshold sacrifices population, Husbandry participation, herd totals and QoL before it produces an attractive reduction in fragmentation.

Therefore the working fission baseline for the Elder RL environment is:

```
FissionPressureRule = CurrentPlusExitOpportunity
FissionExitThreshold = 0.05
FissionExitSearchFraction = 0.8
```

This baseline retains the existing crowding and food/water-distress channels and adds the independent relative exit-opportunity channel. Future changes must compare against this baseline, not against the pre-exit Current rule.

The next calibration should not continue increasing this scalar threshold. Future anti-oppression work should instead use targeted mature-village/tax/requisition fixtures and, if necessary, alter the structure of the exit opportunity term or its social/political inputs.
