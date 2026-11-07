# Fishing Unity integration and Sailing

**Current balance decision:** the Fishing investigation is accepted as resolved
after the mixed-work correction and the broader Archipelagos comparison. The
remaining measured on/off population differences are retained as evidence, not
as an outstanding balance blocker for review. Earlier milestone decisions below
are historical. Native Unity compilation and interactive verification remain
separate, unverified validation items.

**7 November follow-up:** the major seed-23 regression source is now isolated and
repaired: non-agricultural passive gathering consumed labor reserved for raw
materials, starving otherwise-funded manufacturing. See the 7 November cause
section for the identical-state counterfactual, corrected cohorts and limits.
The original measurements below remain historical evidence, not reruns of the fix.

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
General new-game defaults remain off pending native Unity verification.

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
At this milestone, issue #41 remained a balance release blocker pending matched
Fishing cohort evidence. The later correction and acceptance are recorded below.

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
feedbacks. This initial audit did not justify additional tuning or accepting the
regression as a tradeoff, so #41 remained a release blocker at that milestone.
The later causal correction and broader Archipelagos audit supersede that decision.

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

## Initial delivery and release decision

The initial draft of [PR #47](https://github.com/GeeseGaggle-211/Anthropocene/pull/47) contained
separate Unity integration, selection repair, Sailing and audit commits, followed
by the initial evidence report. At that point, correctness and registered
long-run gates passed, but the seed-23 cohort decline was unresolved and kept
the PR in draft. The subsequent cause, broader audit and accepted balance
decision are documented below. General defaults remain off, and the opt-in
profile still requires native Unity verification; no interactive playtest is
claimed here.

## 7 November: cause of the seed-23 regression

The main demonstrated defect is the boundary between mixed food and material
work, **not a failure to deliver the planned fish catch**. `DemandController`
reserved resident Gatherer time in `EconomicLabor.MaterialGatherers` to extract
Stone/Wood for its admitted Hand Tools, clothing and housing. Agricultural
claims use `AgriculturalLabor.FoodGatherers`, which subtracts this reservation.
The non-agricultural `AddPassiveCampClaims` path instead used the complete
Gatherer pool. Where available food saturated gathering capacity, it consumed
all the work before `MaterialEconomy.Gather`; that later pass correctly found no
remaining extraction time. No Stone meant no Hand Tools, which prevented the
tool-dependent goods from being made despite positive grants and assigned
Craftspersons. This is a resolver failure to honor the heuristic's mixed-work
plan. It does not establish that the heuristic's food portfolio is globally
optimal.

The two seed-23 founders are camps **1435 and 1447**. In the original enabled
case they made no clothing until turns **19 and 21**, the first resolutions after
Cultivation on turns **18 and 20**. With Fishing disabled, Cultivation arrived
at turn **13** for both. Actual receipts distribute practice: resident fishery
food credits Fishing, while terrestrial/passive gathering also credits Gathering
(`AwardLifestyleProgress`). Greater dependence on resident Fishing therefore
slowed Gathering practice, extending the period in the broken non-agricultural
production path. This practice allocation is an existing rule, not changed here.

### Identical-state causal experiment

Load the original enabled save after turn 11 and resolve turn 12. Both founders
start with 172 residents. Opening state, the complete prepared demand allocation
and workforce assignments compare **exactly equal** between runs. For camp 1435,
the plan assigns 79 Craftspersons and reserves 17 Gatherers for materials.
The only changed rule is honoring that reservation in passive food claims.

| Camp 1435, turn 12 | Original | Corrected |
| --- | ---: | ---: |
| Planned Cloth | 278.929618 | 278.929618 |
| Actual Hand Tools | 0 | 340 |
| Actual Cloth | 0 | 278.929618 |
| Passive food receipts | 7,056 | 4,608 |
| Resident fishing receipts | 9,152 | 9,152 |
| Experienced clothing coverage | 0 | 1 |
| Continuous exposure deaths | 117.842119 | 74.864816 |
| Ending residents | 190 | 248 |

Camp 1447 independently makes 345.724597 Hand Tools and 278.929618 Cloth with
the correction, versus zero goods before; its ending residents also rise from
190 to 248. Food gathering falls because raw extraction now pays its actual
labor cost. There is no free food, material, tool, technology or demographic
subsidy. This single-turn result establishes the causal mechanism independently
of later technology, trade, movement and fission feedback.

### Corrected seed-23 cohort and residual

The final implementation repeats the same production-config Continents/80 bank:
eight opening turns, then exactly 40 post-opening turns; Sailing off. Both
feature-off and feature-on controls use the correction because the same passive
path also serves Husbandry-only camps. All non-Fishing rules stay fixed.

| Source | Fishing | First-Watercraft settlements | Residents | Resident-weighted QoL |
| --- | --- | ---: | ---: | ---: |
| Before correction | off | 9 | 5,309 | 0.809426 |
| Before correction | on | 3 | 1,954 | 0.823606 |
| Corrected | off | 9 | 5,289 | 0.808167 |
| Corrected | on | 7 | 4,556 | 0.808107 |

The absolute deficit falls from **3,355 to 733 residents**, closing **78.2%** of
the original gap; the relative deficit falls from **63.2% to 13.9%**. Comparing
the corrected enabled case only with the old control would mix code versions,
so the corrected control is the relevant final comparator.

In the corrected enabled run both founders produce Cloth at turn **8**, Housing
at turn **11**, and Cultivation arrives at turns **15/14**. Their final populations
are **565/1,000**, versus **479/1,000** in the corrected control. The residual is
entirely in the descendant settlements: **2,991 versus 3,810** residents, offset
by 86 more residents in the original founders. This is a decomposition of the
outcome, not proof that every later social/economic decision is optimal.

The trace records five fissions in the corrected enabled lineage versus seven
in the control (original enabled: one). Both enabled founders first split at
turn 9; the control first splits at turns 13/16. Their later destination and
utility gates differ: camp 1435 has 37 `no-viable-site` evaluations in the enabled
run, while camp 1447 has 27 `reject-mover` evaluations. We retain those actual
gate results rather than changing fission thresholds to force equal settlement
counts. A separate original-code **pressure-only** run (Fishing off, corrected
aquatic pressure on) exactly matches every original control Watercraft-descended
camp population throughout all 48 turns. The pressure switch therefore does
not explain this seed's measured cohort deficit.

Seed **101** also completes a matched 40-post-opening-turn check (opening ends
at turn 10). The corrected control has **22 settlements / 3,490 residents /
0.835510 weighted QoL**; corrected Fishing has **25 / 4,687 / 0.850408**. Fishing
remains ahead of its matched control by 34.3%, but its total is below the original
enabled 6,965. Restoring manufacturing is not guaranteed to increase aggregate
lineage population: it also changes development, mortality and social decisions.
These two seeds do not constitute a general balance sign-off.

### Implementation, scope and validation

`AddPassiveCampClaims` now deducts the prepared material reservation before
building food claims. The pure `FoodHorizonForecast.Settlement` passes the same
reservation, including accepted locations; accepted village departure releases
the old economy reservation. Correction and regression tests are committed at
`b6a690ad2785409ff41bcfd393c74630463ad392`. No authoritative state, save schema, observation,
RL interface, recipe, yield, fishery capital law, demographic coefficient or
social threshold changes. The correction adds constant arithmetic to the existing
bounded local claim builder; it adds no timestep loop or world scan. Old saves
load unchanged, but future non-agricultural turns now honor their saved/planned
material reservations.

`DemandControlChecks.FishingMaterialBootstrap` uses a productive coast and zero
manufactured stock. It fails against the original simulation with “productive
coast cannot spend reserved material work on passive food.” The corrected path
produces real raw inputs, Hand Tools and Cloth, bounds food plus material work
by the resident cohort, and matches save/load scalar/four-worker replay.
Additional checks cover current/accepted-location forecasts, departure and
purity. All **70 fixed suites** pass (74 PASS lines, 95.1 seconds under concurrent
audit load; this is not a performance comparison). The final focused follow-up
passes **158 DemandControl, 659 pastoral-craft and 408 forecast assertions**.
Short replays from the exact same initial fingerprints reproduce every traced
row through turn 12 and confirm opening completion at 8/10 for seeds 23/101
in both final configurations. Those marker checks are retained in the evidence.

At this milestone the two original registered 50-turn replays predated the
correction and had not been rerun. The residual cohort deficit was retained as
a blocker pending the broader Archipelagos comparison below. The subsequent
balance decision is recorded at the top of this report; current review checks
are recorded in the final section. Native Unity compilation and play remain
unverified. No RL checkout, training job or checkpoint changed.

### Reproduce the diagnostic

From the repository root, build `Tools/FishingRegressionAudit/Anthropocene.FishingRegressionAudit.csproj`
in Release. The probe records prepared grants, material extraction, actual goods,
practice, cause-specific demography and fission gates for every Watercraft-first
descendant. It refuses to overwrite output, writes artifact/configuration hashes
and final fingerprints, and never changes simulation policy through diagnostics.

```sh
dotnet build -c Release Tools/FishingRegressionAudit/Anthropocene.FishingRegressionAudit.csproj
dotnet Tools/FishingRegressionAudit/bin/Release/net8.0/Anthropocene.FishingRegressionAudit.dll \
  --fishing=1 --pressure=1 --seed=23 --end-turn=48 --revision=YOUR_COMMIT \
  --output=TestResults/FishingRegressionFresh/final-fishing-23.jsonl
# Repeat with --fishing=0 --pressure=0 for its matched control.
# Seed 101 completes opening at turn 10: use --end-turn=50 for 40 more turns.
```

For old-source controls, extract only `Assets/_Anthropocene/Simulation` from
commit `64ce95343bb5d9f6d04d72dc017ec8352a49323b` to a temporary directory and
build the same probe with `-p:SimulationSource=ABSOLUTE_EXTRACTED_SIMULATION_PATH`
and a separate `-o` directory. To reproduce the frozen counterfactual, run that
old binary with `--fishing=1 --pressure=1 --end-turn=11 --save-turns=11`, then
run both binaries with `--load=OUTPUT.jsonl.turn-11.anthro --end-turn=12` and
distinct outputs. Loading preserves all saved configuration; it does not apply
the fresh-world feature switches. Raw evidence is retained under ignored
`TestResults/FishingRegression`; [FishingMixedWorkEvidence.json](FishingMixedWorkEvidence.json)
contains the compact evidence. `Tools/FishingRegressionAudit/Summarize.py`
rebuilds it from the named `final-control-23`, `final-fishing-23`,
`final-control-101`, `final-fishing-101`, `baseline-pressure-only-23`,
`before-frozen` and `final-frozen` JSONL files and their metadata, plus validation
logs. It asserts identical saved inputs and prepared allocations in the frozen
experiment and verifies every raw artifact hash.
For these first long-run artifacts, whose metadata preceded the explicit opening
marker, the reducer also reads the four `opening-SEED-FLAG` short replays. Future
probe runs record that marker directly.

## Archipelagos follow-up

The same corrected simulation (`b6a690a`; no additional simulation changes) was
run on **Archipelagos, 1024×512, 80 founders**, seeds **23 and 101**, with the
production configuration and four workers. Each on/off pair changes only resident
Fishing and corrected aquatic pressure; Sailing stays off. Each run completes
its opening and then exactly **40 additional turns**. Seed 23 finishes opening
at turn 10 and ends at 50; seed 101 finishes opening at 8 and ends at 48.

The opening has **12 Watercraft-first founding societies on seed 23** and
**11 on seed 101**, equal between the on/off variants. Seed 23 therefore samples
six times as many Fishing founders as the earlier Continents case's two.

| Seed | Fishing | Watercraft-descended settlements | Residents | Median settlement population | Resident-weighted QoL |
| --- | --- | ---: | ---: | ---: | ---: |
| 23 | off | 65 | 43,705 | 728 | 0.748258 |
| 23 | on | 57 | 43,931 | 944 | 0.766557 |
| 101 | off | 69 | 40,178 | 564 | 0.762657 |
| 101 | on | 87 | 69,124 | 913 | 0.772431 |

Fishing changes this cohort's population by **+0.52%** on seed 23 and **+72.04%**
on seed 101. QoL rises in both. Seed 23 still has fewer descendant settlements,
but larger settlements offset the difference; its original founders finish at
7,690 residents versus 7,529 in the control. Measured post-opening fissions are
39 versus 50. Seed 101 has 76 versus 58 fissions and more descendants, although
its original founders total 8,603 versus 9,208 residents. These are distinct
expansion outcomes, not evidence that every population increase is a direct
increase in the founding camps' food yield.

World populations also rise: **524,605 → 528,769** on seed 23 and
**485,744 → 540,059** on seed 101. The evidence retains the full per-turn census
of all first-technology cohorts, not only the Watercraft lineage.

The mixed-work correction remains active in the broader sample. During the
post-opening period, **69/69** seed-23 and **70/70** seed-101 camp-turns with a
positive Cloth grant while holding neither Cultivation nor Husbandry produce
positive Cloth. This checks the prior zero-production failure; it does **not**
claim every grant is fully realized. Eighteen distinct Fishing-only camps make
Cloth on seed 23, and 24 on seed 101; they also produce real Hand Tools and
Housing. Their Cloth totals are 13,249.71 and 12,100.81 units, respectively.
The original material-starvation symptom is absent from these granted camp-turns.

First technology is an ancestry label. **All** surviving Watercraft-descended
settlements have Cultivation by the endpoint. On the last turn, **30/57**
settlements on seed 23 and **40/87** on seed 101 both staff Farmers and Fishers
and realize resident fishing receipts. Thus this comparison exercises mixed
economies rather than assuming that a Watercraft lineage lives only on fish.
Those lineages harvest **8.77 million** and **8.81 million** resident fishing
food units over the measured 40 turns.

All four audits exit successfully. The reducer verifies artifact hashes,
1024×512/80/Archipelagos configuration, feature switches, exactly 40 measured
turns, endpoint uniqueness and matched founding-cohort counts. The audit builds
without errors; it changes no simulation rule or default. The balance review
accepted the Fishing matter as resolved on this evidence. The two sampled worlds
do not establish that every heuristic decision in every geography is optimal.
Native Unity validation remains unverified; the final section records the fresh
five-world replay checks. General new-game defaults remain off.

Reproduce from the repository root after building the diagnostic project:

```sh
dotnet Tools/FishingRegressionAudit/bin/Release/net8.0/Anthropocene.FishingRegressionAudit.dll \
  --shape=Archipelagos --seed=23 --fishing=1 --pressure=1 \
  --post-opening-turns=40 --revision=YOUR_COMMIT \
  --output=TestResults/FishingArchipelagosFresh/fishing-23.jsonl
# Repeat for seed 101 and for --fishing=0 --pressure=0 with control-SEED filenames.
python3 Tools/FishingRegressionAudit/SummarizeArchipelagos.py \
  TestResults/FishingArchipelagosFresh TestResults/FishingArchipelagosFresh/summary.json
```

The probe's `--shape` and relative `--post-opening-turns` options remove the need
to guess when each world's opening finishes; `--end-turn` remains available for
saved-state counterfactuals. [FishingArchipelagosEvidence.json](FishingArchipelagosEvidence.json)
contains the committed summary, per-turn cross-cohort census, fingerprints,
configuration/artifact hashes, manufacturing checks and caveats. Raw JSONL and
logs remain under ignored `TestResults/FishingArchipelagos`.

## Review preparation and current validation

Balance review accepts the Fishing matter as resolved after the shared material
work correction and the Archipelagos comparison. PR #47 is ready for review
with production defaults unchanged. The tested source is
`4522f05e2c2259f8ac5966178aa805e30a4bb891`; simulation behavior last changed at
`b6a690ad2785409ff41bcfd393c74630463ad392`. The initial review-preparation commit
affects validation documentation and evidence only; the subsequent PR-comment
follow-up below relocates the debit without changing its amount.

Fresh checks on that source:

- All **70 registered fixed suites** pass (74 PASS lines), including the final
  158-assertion DemandControl suite and the warm Fishing calibration. The run
  takes 75.9 seconds on three lanes under concurrent long-run load; this is not
  a performance comparison.
- Headless, PerformanceAudit and FishingRegressionAudit build without errors.
  Existing CS0649 unassigned diagnostic-field warnings remain; every reported
  field also exists on the PR base. No new warning category was introduced.
- Changed-text hygiene passes for 45 files: UTF-8, LF, final newline and no
  trailing whitespace. All five changed Python files parse, all five changed
  JSON files parse, and `git diff --check origin/main...HEAD` passes.
- C# 9 syntax parsing passes all 106 project C# files. All 18 changed Unity
  assets have `.meta` files; all 166 asset GUIDs are valid and unique. These
  checks do not substitute for Unity API compilation.
- The current-contract checker passes 93 JSON/API values, 21 test claims and
  document links. Both cohort reducers reproduce the committed mixed-work and
  Archipelagos evidence byte-for-byte. The default-off and opt-in profile
  feature settings are verified.
- The fresh **no-population-movement** five-world / 50-turn scenario passes its
  matched control, exact artifact/fingerprint replay, conservation and activity
  gates. Agriculture/Husbandry medians are **1,000/1,000**, against **800/800**
  floors. It records **8,445,289.923** resident fishing food receipts.
- The fresh **all-social-features** five-world / 50-turn scenario also passes
  exact artifact/fingerprint replay, conservation and activity gates.
  Agriculture/Husbandry medians are **831/880.5**, against **384/306.4** floors;
  the Fishing median is **982** across 177 endpoint settlements. It records
  **5,471 fissions**, **8,473 migrations** and **24,808,906.160** resident fishing
  food receipts. Both scenarios use identical candidate opening checkpoints.

[Current review evidence](FishingReviewEvidence.json) records commands, log and
raw-artifact hashes, all per-turn census distributions, checkpoint identities and
final fingerprints. Both long-run processes exit successfully. The registered
five-world census uses its API-default configuration; the separate Archipelagos
audit above uses the production configuration. Their cohorts are not interchangeable.
Final changed-text checks also pass after adding this evidence: 46 files,
including six JSON files, with no hygiene or parsing issues.

There is no dedicated lint command or GitHub Actions workflow in this repository;
GitHub reports no commit statuses or PR-triggered workflows for the tested head.
At the initial review-preparation check, the PR had no review submissions or
inline review threads; the conversation comment is addressed below. Native
Unity 6000.5.10f1 is not installed here: engine compilation, EditMode execution
and interactive verification remain explicit review validation items.

## PR comment: RL lineage merge boundary

The [review comment](https://github.com/GeeseGaggle-211/Anthropocene/pull/47#issuecomment-6701552797)
identifies a real cross-lineage merge hazard. Read-only inspection confirms that
`28473f2806e2ca5d4304b6ee9b5ddca5ae5c9ce7` introduced
`PassiveFoodGathererIntervals` on the RL lineage. At the cited pretraining source,
`8bd480b2797c5ee925d6ea7b9b844849b0d0966e`, that helper already handles actor
activity authority, policy-authoritative material reservations and phase-A/B
Gatherer extraction allowances before calling `AddPassiveCampClaims`.

The original PR fix was correct against main's gross `labor.Gatherers` call,
but its in-function debit could combine with the RL helper and charge material
work twice. The follow-up makes the boundary explicit: the authoritative caller
and the forecast caller each compute food-only Gatherers, and
`AddPassiveCampClaims` only consumes that supplied pool. Village departure still
releases the old reservation. Current-main arithmetic is unchanged.

A read-only three-way file merge against the PR base and `8bd480b` now produces
an explicit conflict at the authoritative caller. When integrating those
lineages, retain the RL `PassiveFoodGathererIntervals` call and its authority
branches as the single authoritative debit; retain this PR's single forecast
debit, because that forecast still starts from gross labor. Do not subtract
material work again inside `AddPassiveCampClaims`. This preview is a merge
handoff check, not a claim that the combined RL branch builds or passes tests.

The cited pretraining source already reserves material labor, so this particular
missing-debit defect does not explain deficits in that dataset. Its
`FishingEconomy` still contains the incumbent-eviction defect repaired by this
PR; the shared simulation can therefore affect both teacher and actor outcomes.
The size of that effect on training metrics has not been measured here. No RL
branch, training job or checkpoint was modified.

Validation of this boundary relocation: all 70 fixed suites pass again (74 PASS
lines), including 160 DemandControl assertions and explicit zero/positive
food-only pool checks. The previous and updated audit binaries load the identical
saved Fishing world and resolve turns 12–15; all 23 traced camp-turn rows are
byte-identical, as are the final fingerprint and world population of 44,563.
The audit build has zero errors, with the same existing CS0649 warnings.
[Boundary evidence](FishingMergeBoundaryEvidence.json) records hashes and both
comparison outcomes. The two 50-turn scenarios above were run before this
behavior-preserving relocation and were not repeated for it. Native Unity
verification remains outstanding.
