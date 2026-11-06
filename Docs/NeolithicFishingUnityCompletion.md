# Fishing Unity integration and Sailing

## Baseline and authority

Work starts from the fetched, verified default `main`,
`07f3501c2d5e047e44d61e85cc0d4613b54572d6` (6 November 2026), on
`features/neolithic-fishing-unity`. The October 5 contract is a physical-law
reference, not the production base. The current source includes resident Fishing
(`db14106`) and its bounded lookup fixes (`876179a`), later physical demography,
social relocation, field domestication and generation-16 terrain.

Read: Fishing Contract v0.9 (Drive `1eHySnLbn3f1RSwDWBjwxfpx3smkwXI3c`),
`NeolithicFishingImplementationPlan.md`, current simulation/pass contracts,
issues #41 and #9 including discussion, and the read-only
`feature/elder-rl-scaffold:Docs/MixedSubsistenceDiffusionValidation.md`.
No RL branch, training job, checkpoint or infrastructure is changed.

Reconciliation: current source implements the v0.9 direct-adjacency water graph;
the pass document's phrase “routes through traversable land” is inaccurate.
The October cold-water blocker has since been repaired behind the corrected
pressure gate. Both opening Fishing flags remain false. #41's results at
`a217fd20d8afb3c9310732340bb4d66e8c45da0d` are historical and are not expected
to numerically match today's terrain/demography. No physical parameter or social
baseline is tuned to hide a cohort regression.

## Abstraction and player behavior

Authoritative state remains camp workforce/inventory, society technology/practice,
sparse place-bound fisheries and the existing finite aquatic tile ledger.
The new inspection is derived, owner-only and pure. It shares the preparation
route search and catch envelope, uses dated owner snapshots outside vision and
zero-yield unsurveyed priors, and reads only the selected camp's past receipts.
UI caching keys on world, turn, camp and draft intent. Work scales with bounded
water-route area and active owned records, never elapsed years or population.
The simulation remains one analytical WEGO transition; no training interface or
reward changes are introduced.

Economy buttons now dispatch through `EconomicAllocationStrategy`, matching AI
and Prepare. Watercraft-only settlements expose Cloth and fixed Housing
priorities as well as Fishers and Fishing Tools. The full-width Fishing section
shows up to three proposed grounds, routes, distance loss, observed/remembered
food and condition, capital-derived q, work/rest status, catch/tool estimates,
material access warnings and last-turn food/village qualification receipts.
“Show ground and water route” centers the map and draws the legal route using
the existing path renderer. Forecasts are labelled estimates, not guarantees.

The menu's Fishing validation profile and CLI
`--anthropocene-fishing-validation` load `FishingValidationConfig.json` on top of
OpeningConfig for **new worlds only**. Existing natural landscape presets remain
available; the profile grants no technology, workforce, resources or capital.
General new-game defaults remain off pending balance and Unity acceptance.

## Validation (updated as milestones complete)

.NET SDK 8.0.427 used locally (8.0.419 is also installed) in this cloud workspace. Full baseline fixed
suite passed (three lanes, 54.9 s, reported peak working set 2111 MiB); the runner
also executed its default 20-seed benchmark with zero illegal orders.
Milestone A focused checks pass: inspection 40, Fishing economy 47, resources 43,
observations 13, state 23, seasonal pressure 53 assertions. Lake/coastal and
fishing-only/mixed inspection fixtures cover route adjacency, snapshot/prior
separation, hidden-state perturbation, purity and player/AI intent parity.
Sparse-site and 1280-camp eligibility scale guards pass unchanged.

Unity 6000.5.10f1 is absent here. No Unity compilation, EditMode execution,
player capture or interactive playtest is claimed. Simulation test results do
not validate visual layout. Native Unity build/play verification is a release gate.

Raw logs and audit output are under ignored `TestResults/FishingUnity/`.
Issue #41 remains an explicit balance release blocker until matched Fishing
cohort evidence justifies closing it.

## Sailing milestone and correctness repair

Implementation commits: Unity `90e4a723498b28eca46977b374a4a0b68ae483d8`,
selection repair `a225ecb0340ebcfb7ab235a3bae19ad4fb5ad3e3`, Sailing
`d014ff0479b56c24c9b0426b801439ab16dc8860`.

The offshore integration test reproduced a source-selection defect: three old
zero-yield grounds met a zero third-place cutoff and could evict a productive
newcomer. Replacement now also requires the incumbent to meet the unchanged
2.5% margin against the actual ground being displaced. A separate pre-Sailing
fixture reproduces the same case. This is a selection correctness correction,
not yield/capital/biomass or population tuning. The final seed-23/101 endpoint
results are unchanged by this repair; it does not resolve #41.

`SecondTierTechnology` is shared useful-work progression plumbing with a
track descriptor for the defined Sailing route. Other second-tier effects stay
inert. It reuses the existing `CraftExperience`/`NeolithicTechnologies` save
fields and shared `Neolithic.CraftMasteryThreshold=400`, with the existing
output/base-recipe-rate work units. Only realized Weaver Cloth or Animal Fabric
production counts, in a Watercraft camp with actual resident fishery receipts
or successful Hunter fishing while using Shallows. Coast ownership, stock,
generalist output and idle work do not count. Positive authoritative receipts
prove legal water activity without a new per-camp world scan. Close commits
the unlock; the prepared current-turn geometry is unchanged.

The optional saved `NeolithicSailingEnabled` defaults false, including when
absent from a resident-Fishing save. The named validation profile enables it.
Sailing gives range five and distance retention 0.90 within three total grounds.
Ocean is a terminal neighbor of Shallows, never a traversal vertex; at most one
Ocean ground can be selected, including during incumbent replacement. Offshore
work has no capital/maintenance/managed renewal or village credit. AquaticFood,
finite tools, natural ecology, movement and trade laws remain shared and intact.

The final fixed suite passed all 70 registered suites (74 PASS output lines),
70.2 seconds on three lanes, peak reported working set 1811 MiB. Focused
follow-up passes: Sailing 23, inspection 41, save/state 24 assertions. Sailing
checks cover the five-step terminal route, no Ocean chain/shortcut, no Lake-to-sea
route, one-of-three selection, compatibility gating, useful-work context,
partial-progress save/load, a real production-driven Close unlock, next-Prepare
activation, an actual offshore receipt, zero offshore capital/village credit,
and exact scalar/four-worker save continuation. The warm census still passes
36,892 assertions; eligibility/sparse-resource guards, cold-water pressure,
forecast, conservation and replay suites pass. Headless builds have four
pre-existing CS0649 warnings and no errors. C# 9 syntax parsing passed all 106
project files; this does not substitute for Unity API compilation.

`FishingIntegrationTests` adds a native Unity resource-loading test for the
OpeningConfig + validation-profile path; it is **not executed here**. Native
layout, map interaction, resource import, EditMode and player verification remain
required. Use the menu profile or CLI flag, choose the natural Fishing landscape
preset, discover Watercraft, then open Settlement Economy. Manual Fishers disable
automation; Allocate once / continuous automation use the common dispatcher.
Show ground draws a route on the normal map. Useful textile production supplies
Sailing; no research purchase or technology-grant button exists.

## Current matched #41 evidence

Continents, 1024×512, 80 founding societies, seeds 23 and 101; production config,
four workers, 40 post-opening turns. Opening completes after 8 and 10 turns,
respectively. The sole comparison is resident Fishing + corrected aquatic
pressure off/on; Sailing is off in both. All other current defaults, including
terrain, demography, field domestication, social laws and Elder configuration,
are held fixed. This is a current-source comparison, **not** numerical
reproduction of the October RL branch's older four-way diffusion/taming bank.
No RL branch or checkpoint was modified. All four runs complete.

| Seed | Fishing | World population | First-Watercraft settlements | First-Watercraft residents | Resident-weighted QoL |
| --- | --- | ---: | ---: | ---: | ---: |
| 23 | off | 310,684 | 9 | 5,309 | 0.809426 |
| 23 | on | 323,967 | 3 | 1,954 | 0.823606 |
| 101 | off | 505,383 | 25 | 3,618 | 0.846032 |
| 101 | on | 482,497 | 33 | 6,965 | 0.857947 |

First technology is only a diagnostic lineage label. Aggregate growth cannot
hide seed 23's **63.2%** first-Watercraft resident decline. Seed 101 improves
that cohort on current source, unlike the historical case. QoL and population
remain separate outcomes.

Across the 40 measured turns, the enabled first-Watercraft cohorts show:

| Measurement | Seed 23 | Seed 101 |
| --- | ---: | ---: |
| Locked Fisher person-turns | 2,919 | 4,180 |
| Requested / realized resident catch | 701,394.846 / 701,394.846 | 978,528.704 / 961,727.009 |
| Realized / forecast | 100% | 98.283% |
| Planned maintenance / required worker-years | 24,294.470 / 24,294.470 | 30,410.584 / 31,153.564 |
| Planned capital-building worker-years | 34,085.530 | 40,391.805 |
| Tool-covered harvest worker-years | 18,159.736 | 32,341.253 |
| Fishing Tools produced | 961.987 | 1,706.063 |
| Moves, off → on | 2 → 1 | 5 → 18 |
| Fissions, off → on | 7 → 1 | 23 → 30 |
| Immigrants / emigrants, off → on | 260/260 → 0/0 | 463/463 → 403/403 |

Each produced Fishing Tool pays one Wood and one Stone plus shared craft/tool
labor; those costs are not free. Fishing also reserves the same resident cohort
that would otherwise gather/craft. The artifact separates actual food receipts
by source and includes endpoint staffing, material/tool inventories, planned
ground work and local stocks. In seed 23, forecast catch is fully met and planned maintenance is fully allocated, so
this loss is not explained by a simple missed catch or planning-stage maintenance
deficit. The audit does not isolate actual operator contests or reoccupation. Fewer descendant settlements and changed food portfolios
accompany it. In seed 101 there is a small receipt shortfall and partial
maintenance alongside more moves and descendants. These are attribution clues,
not a proven causal decomposition of the economic, demographic and relocation
feedbacks. No additional tuning or claim that the regression is an acceptable
tradeoff is justified. **#41 remains a release blocker.**

Reproduce the bank and its strict summary with:

```sh
python3 Tools/PerformanceAudit/Run-FishingUnityAudit.py --output TestResults/FishingUnityFresh
# Or reduce an existing complete bank:
python3 Tools/PerformanceAudit/Summarize-FishingUnity.py TestResults/FishingUnityFresh
```

The runner refuses to overwrite artifacts. Detailed timing is diagnostic, not
a matched throughput claim: populations and settlement counts diverge. Controls
were built before the selection repair; that path is unreachable with Fishing
disabled. Enabled cases were rerun at `d014ff0` plus the audit-only fields. Their
population/QoL/catch endpoints equal the pre-repair runs. Configuration and raw
artifact hashes are retained in the committed evidence summary.

## Registered long-horizon scenarios and reproduction

The registered census remains C1/C2/C3/P1/A1, 80 founders each, completed
openings followed by exactly 50 committed turns. Both scenarios share the same
Fishing-enabled post-opening checkpoint identities; the social feature toggles
are the only difference. The no-movement scenario additionally regenerates its
matched feature-off reference. These registered tests use their existing API
configuration and resource-generation defaults, not the separate production-JSON
#41 bank above. Sailing is disabled in these basic Fishing gates.

The final no-movement scenario passes exact artifact/fingerprint replay,
conservation, positive activity and population gates. Agriculture and Husbandry
both have median 1,000 against matched floors 800; Fishing has ten settlements,
median 1,000, P25 817, P75 1,000, minimum 207, maximum 1,000. Fishing QoL
min/P25/median/P75/max is 0.729823 / 0.729856 / 0.729856 / 0.806391 / 0.979414.
It records 24,742 Fisher person-turns, 5,795,741.211 resident fishing receipts,
269,449.059 tool-covered Fisher-years, 409,790.473 capital worker-years and
2,782 active site-turns. The committed evidence JSON retains all 50 per-turn
branch distributions, configuration, checkpoint identities and final fingerprints.

```sh
bash Tools/Test-Headless.sh 0 --jobs=3 --check-timings
bash Tools/Test-Headless.sh --only=SailingChecks,FishingInspectionChecks,FishingStateChecks
bash Tools/Test-Headless.sh --fishing-50-no-movement
bash Tools/Test-Headless.sh --fishing-50-all-features
python3 Tools/Docs/Check-CurrentContract.py
```

The superseded pre-selection-fix all-features process was stopped to release
memory while the final run continued. It is not counted as a passing test.
Only the completed final artifacts constitute release evidence. The earlier
no-movement baseline is retained separately in ignored artifacts.

The final **all-social-features scenario passes**, including identical replay
artifacts and final fingerprints. Its Agriculture median is **845.5 ≥ 384**;
Husbandry is **883.5 ≥ 306.4**. It records 5,534 fissions, 8,409 migrations,
104,030 Fisher person-turns, 23,325,464.397 resident fishing food receipts,
999,559.957 tool-covered Fisher-years, 1,786,746.210 capital worker-years and
10,298 site-turns. Across 5,933 ending settlements:

| First branch (n) | Population min / P25 / median / P75 / max | QoL min / P25 / median / P75 / max |
| --- | --- | --- |
| Agriculture (4,062) | 15 / 447 / 845.5 / 999 / 1,100 | 0.118005 / 0.729792 / 0.756677 / 0.852565 / 0.999712 |
| Husbandry (1,662) | 21 / 586.25 / 883.5 / 999 / 1,090 | 0.078823 / 0.729722 / 0.744398 / 0.833922 / 0.999489 |
| Fishing (209) | 23 / 435 / 781 / 999 / 1,063 | 0 / 0.729856 / 0.777705 / 0.883100 / 0.998576 |

The full per-turn evidence, exact checkpoint/final fingerprints, matched #41
summary and test/artifact hashes are committed in
[NeolithicFishingUnityEvidence.json](NeolithicFishingUnityEvidence.json).
The two long-run checkpoint-identity arrays also match each other exactly.
Documentation checks pass 93 configuration/API values, 21 test claims and links.
Audit/reporting refinements are at `5354793ff2323677ea14f13a5b73bfeee55c9fdd`;
the evidence commit changes documentation only. The remote default branch was
reverified as `07f3501` before delivery.

## Delivery and release decision

Draft [PR #47](https://github.com/GeeseGaggle-211/Anthropocene/pull/47) contains
separate Unity integration, selection repair, Sailing and audit commits, followed
by this evidence report. **Keep the general new-game defaults off.** Correctness
and registered long-run gates pass, but #41's seed-23 cohort decline remains
unresolved, and this environment cannot verify native Unity compilation or
presentation. The opt-in profile is ready for that native verification; this is
not final balance sign-off or a claim of an interactive playtest.
