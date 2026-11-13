# Corrected irrigation — 80-tribe smoothed-calendar playthrough

**Subsequent construction fixes:** [three new 80-tribe runs](ConstructionFix80.md)
record the corrected budget and capacity implementation. This page retains the
earlier run and its original limitations as historical evidence.

**13 November diagnosis:** the [construction grant audit](ConstructionGrantAudit.md)
traces the shortfalls to doubled implicit Tools ceilings and an inconsistent
Housing Material labor cap. The original measurements below remain unchanged.

**Readiness limitations:** some later construction grants exceed actual output,
requiring further funding/production-schedule investigation. The full world
reaches the target date, but final
save/load fails because the retained metadata exceeds the loader's 512 MiB
limit. This is a separate long-run persistence issue; full-world save acceptance
and next-turn replay cannot be claimed. The recording harness now journals
every committed snapshot and retains validation failures alongside results.

## Correction and regression

Canal manufacturing demand now uses the same commissioning-material calculation
as assembly: base construction materials plus upkeep for the current physical
interval. Carried materials and exports still share the existing net demand.
This changes neither recipes' prices nor the calendar, demographic laws, active
RL configuration, saved schema or technology coefficients. It adds constant
arithmetic to the existing per-order demand calculation.

The regression exercises absent and partial material stock, daily intervals,
the failing 13.445341-year interval and a fixed-duration numerical fixture.
It checks manufacturing, actual construction, forecast agreement, material
conservation and the shared Builder budget. It adds 36 assertions.

The original seed-23 natural playthrough was rerun from generation. Village 52
now builds its first segment at year 860.917723 using 11.344534 materials and
8.361335 direct Builder-years, rather than building 88.148177% with 10 materials.
The first water receipt is at year 874.259047, the next interval, versus the
pre-fix year 989.902628. The long fractional-completion tail is removed.

The seed-23 matched comparison was also rerun for 603.341813 physical years.
At the same final date, Village 52 retains 120,000 food and uses 264 operating
Farmers with both technologies; the disabled control retains 92,795.163 food
and uses 313. The disabled control retains its exact pre-fix final fingerprint.
World irrigation delivery rises from the pre-fix 23,378.795 to
33,368.009; paid canal construction/upkeep materials rise from 88.004 to 111.740.
Earlier operation therefore incurs additional real costs. This small matched
example is separate from the full-world descriptive run below.

## Full-world protocol

The playthrough starts from a fresh Standard/Continents world: **1,024 × 512,
80 founding tribes, seed 11**. It loads `AgriculturalTechnologyOpeningConfig.json`
and the production `ResourceGenerationConfig.json`, with normal AI for every
tribe and endogenous Elder allocation. No technologies, resources or canal
orders are injected. The target is at least **1,600 physical years** from
generation, retaining each actual committed boundary.

The historical-exponential smoothed calendar is definitive. The opening uses
its configured 20-year intervals; the post-opening calendar continuously
shortens them. Reported milestone years are elapsed physical years from world
generation, not historical dates. The manifest pins initial settings, the
starting-save hash, source hashes and calendar implementation. Timing evidence
pins the epoch, every committed duration and seasonal exposure in days.

This is one natural full-world trajectory, not an 80-tribe treatment/control
experiment or a replacement for the five-seed balance matrix. Population is a
descriptive outcome, not evidence of technology benefit by itself.

## Full-world findings

The world completes **1,605.730735 physical years in 118 turns**.
It starts with 1,920 people in 80 camps and finishes with
**1,363,048 people** (including hunters), 2,150 camps and
1,840 villages. The final turn spans **8.925442 years**.
The calendar epoch is day 73,000 with display origin
-5000; seasonal exposure sums to the committed
elapsed days. All source hashes remain pinned in the manifest.

| Milestone | Physical year |
| --- | ---: |
| First Irrigation attainment | 297.754246 |
| First canal construction | 408.173726 |
| First water receipt | 425.907774 |
| First Plough attainment | 528.640863 |

The first canal belongs to Village 2335. Its four eligible fertile tiles
all pass the threshold; minimum q is 0.375. This is an actual Elder decision,
with useful construction and water beginning in the following interval.

| Whole-world measurement | Result |
| --- | ---: |
| Camps with enabled Plough knowledge at final boundary | 1,894 |
| Farming camps with Plough knowledge and positive Farm Tools | 1,864 |
| Camps with Irrigation knowledge | 204 |
| Completed / started canal segments | 58 / 58 |
| Operators receiving water in final interval | 37 |
| Cumulative construction Housing Materials (includes commissioning) | 645.269 |
| Cumulative continuity Housing Materials | 2,043.162 |
| Direct construction Builder-years | 453.173 |
| Direct upkeep Builder-years | 5,107.906 |
| Cumulative irrigation withdrawal | 2,728,345.157 |
| Cumulative irrigation delivery | 2,167,659.764 |
| Cumulative irrigation losses | 560,685.394 |
| Maximum per-receipt conservation residual | 0 |
| Final maximum soil development | 100.00% |
| Final camp food reserves | 78,943,870.841 |
| Attributed crop-food receipts across the run | 1,672,387,680.972 |
| Attributed wild-food receipts across the run | 1,675,290,695.787 |

There are 66 positive construction grants,
60 actual construction receipts and
11 authorized-versus-actual construction
mismatches above 1e-7. These are additional discrepancies between a recorded
positive grant and actual output, not simply partial authorization. For example,
Village 4605 at turn 84 is granted one segment but produces 0.880003: it makes
10.829056 Housing Materials, spends 1.076068 on continuity and has only
9.752988 left for construction. Village 4058 at turn 77 is granted one segment
but produces no Housing Materials or canal work. The commissioning demand fix
is active in these cases; remaining material-extraction/workforce scheduling
needs a focused investigation. Forecast/resolution acceptance is still blocked.

Water is paid and finite: withdrawal equals
delivery plus losses to numerical precision. Source counts are
58 river-fed and 0 lake-fed segments.
Knowledge counts do not imply full tool coverage or that every village irrigates.

Direct canal labor above excludes manufacturing inputs. Across all construction
uses, the world manufactures 1,297,240.999 Housing Materials,
spending 3,501,337.063 Builder-years on that stage.
Those totals include housing and cannot all be attributed to canals. Crop/wild
figures are the engine's conserved agricultural plant-food attribution, not all
food from every occupation. This trajectory does not estimate causal labor
savings or select new balance coefficients.

### Persistence and performance finding

The full world's final save contains **1450.35 MiB of metadata**, exceeding
the loader's **512 MiB** bound; its compressed file is 306.29 MiB.
`SaveCodec.Load` rejects it with `Invalid save metadata size.`. The final save is
retained as a reproduction artifact, but is not loadable by this build. Exact
full-world next-turn replay therefore remains unverified. Metadata growth and
save-size handling need a separate correction; raising the cap or deleting
history was not used to disguise this failure.

Observed AI + preparation + resolution time has median
5.359 seconds and maximum
13.163 seconds per turn. These include the actual
expanding settlement population and are not an isolated comparative benchmark.

The [per-turn CSV](Experiments/AgriculturalGameplay80-20261112.csv),
[summary](Experiments/AgriculturalGameplay80-20261112-summary.json),
[manifest](Experiments/AgriculturalGameplay80-20261112-manifest.json) and
[timing/save evidence](Experiments/AgriculturalGameplay80-20261112-timing.json)
are retained with this report. Local `TestResults/AgriculturalGameplay80/overview.png`
shows the world trajectory; `player.html` shows Village 2335's actual turns.

## Validation and reproduction

All 69 fixed headless suites passed, including 181 agricultural technology assertions
and 154 Housing Material recipe assertions. Its 20-seed opening benchmark had
zero illegal orders. The separate calendar suite passed 28,787 assertions.
Both corrected seed-23 runs passed final save/load and exact next-turn replay.
The two full-world executions agree on all 118 logged turn dates, durations,
resident population, construction and water totals; this is a metric replay
check, not a replacement for the blocked full-world save continuation check.
Headless Chromium verified the final viewer's seed/village, completion and
following-interval water receipt, playback and final date with no JavaScript
errors. The viewer prominently retains the remaining acceptance blockers.
Unity rendering and interaction remain unverified in this environment.

```bash
dotnet build Tools/AgriculturalTechnologySweep/Anthropocene.AgriculturalTechnologySweep.csproj -c Release
dotnet Tools/AgriculturalTechnologySweep/bin/Release/net8.0/Anthropocene.AgriculturalTechnologySweep.dll --gameplay --natural --standard --seed=11 --years=1600 --verify-continuation --output=TestResults/AgriculturalGameplay80
python3 Tools/AgriculturalTechnologySweep/SummarizeGameplay80.py
python3 Tools/AgriculturalTechnologySweep/RenderGameplay.py --root TestResults/AgriculturalGameplay80 --single --corrected --focus 2335 --validation-note "11 construction grants underfilled; final save exceeds the metadata limit."
```

Opening/final save artifacts and raw snapshots are generated in
`TestResults/AgriculturalGameplay80`. The opening is loadable; the final save
reproduces the metadata-limit failure. The HTML player is a read-only developer
view of authoritative snapshots, not a Unity build. Simulation timing excludes
snapshot rendering/serialization; the process was instrumented and shared its host with analysis tasks, so timings are observations rather than an
isolated performance benchmark.
