# Prepared-turn Neolithic performance handoff

This report is the archived seasonal prepared-turn reference at `c33a5b7`.
The production turn-horizon refactor and its newer measurements are recorded
in [TurnHorizonRefactor.md](TurnHorizonRefactor.md); statements below that
retain seasonal barriers describe that historical checkpoint.

September 25 follow-up: the exact Options 1/5 cache experiment is on
`codex/neolithic-options-1-5-validation`, based on `9dc18c0`. See
[its validation report](NeolithicOptions15Validation.md) for the sustained
25-generation comparison and [the analytical turn plan](NeolithicAnalyticalTurnPlan.md)
for the retained follow-up. The measurements below remain historical opening
evidence, not the current Neolithic acceptance denominator.

**Scope:** `features/neolithic-agricultural`, with the accepted prior-cohort
economy and the default seasonal resource resolver. This report is for work on
that branch. No event-driven resolver is involved. The behavioral checkpoint
is `38c1354`; the subsequent seasonal-only profiling commit adds opt-in
counters without changing the turn rules. The historical `main` comparison
uses `e472cf760232eaa614b387a0212146221f505cd9`. The unmodified
Neolithic branch comparison uses `65af0df` and is only a directional record;
its old ending-population QoL rule is not the accepted behavior.

## Decision and performance status

`TurnPlanner.Prepare` locks validated orders and prior-cohort food, material,
goods, and resident targets before `TurnResolver.Resolve`. The resolver does
not call `EconomicPlanner.Plan`; later receipts reduce the fixed targets.
Food, production, housing, clothing, QoL, shortage, and starvation concern
the pre-birth cohort. Births commit after those passes and enter demand at
the following turn. Preserve the [pass contract](TurnResolutionPasses.md).

The initial fix recovered a pathological regression: on Standard / Continents
/ seed 1729 / 80 tribes, unmodified `65af0df` turn 6 took 7.02 s in AI
planning plus 18.86 s resolving. The prepared-turn revision took 0.225 s in
each phase, with matching population and realized food on that turn. The old
branch was stopped during turn 8. This is a comparison of separate processes,
not a paired full-opening speed claim.

The remaining matched-`main` gate is open. The previous warmed Release audit
used one Windows checkout, four workers with 64-item partitions, a small-world
warmup, and the same seed/configuration for each source. It completed 10 turns
at 80 tribes and 11 at benchmark-only 320 tribes:

| Combined AI + preparation + resolution | `main` | accepted branch | Gap |
| --- | ---: | ---: | ---: |
| 80-tribe per-turn median | 190.1 ms | 249.0 ms | 1.31x |
| 320-tribe per-turn median | 504.0 ms | 933.0 ms | 1.85x |
| 80-tribe complete measured opening | 1.857 s | 4.049 s | 2.18x |
| 320-tribe complete measured opening | 5.748 s | 17.208 s | 2.99x |

Agricultural turns 8–10 at 80 tribes took 3.65–6.04 times the corresponding
`main` turns; turns 7–11 at 320 took 2.52–4.72 times. `main` lacks the
Neolithic economy, so this gap includes its feature cost. It is still the
requested performance target: combined median within 10% of matched `main`,
with no agricultural turn over twice its corresponding `main` turn. Neither
condition passed. The historical 2.686 s / 10.908 s twelve-turn totals are
not directly comparable to these naturally completed 10/11-turn runs.

One fresh, sequential Release comparison after adding opt-in counters yielded
200.4 versus 296.3 ms per-turn medians at 80 and 799.2 versus 1,036.6 ms
at 320 (`main` versus branch). The 320 `main` run had a 1.57 s AI spike on
turn 10; the branch had 1.34 s AI on turn 9. These single-pair timings show
substantial process/GC variance and are **not** a replacement for three
sequential warmed pairs. Fresh branch-only runs and the separate activity
runs produced identical per-turn population, prior-cohort weighted QoL, and
food yield; counter capture is not authoritative state.

## Work performed by the default resolver

Let `S = TurnDays / BaseDays` (80 here), `A` be unique active claim tiles,
`F` the agricultural dependency tiles, `U = A - F`, `N_a` agricultural
camps, `R` radius-one local candidates, and `Q` field-work allocation
increments. The resource pass makes approximately `U + S*F` outer
`EcologicalPressure.Integrate` calls, plus `S` repetitions of agricultural
claim preparation, labor/operation calculation, receipt commit, and field
expansion. Each independent `U` call still runs the 80 climate segments
*inside* its stock kernel. `TileIntegrations` counts calls, not those inner
segments. Quiet, unclaimed tiles are lazily projected rather than scanned
each turn.

As a function of active entities and claims, the dominant local work is
roughly `O(S*A + S*(N_a*R + local claims) + Q*R*4)`. With fixed turn length,
radius, and bounded local claims, it behaves near linearly in settlement
count but has a large seasonal constant. The full implementation has
quadratic incidental paths: each camp's `LinkedPopulation` scans hunters,
`Supply.Connected` finds a source camp by a linear search, and demographic
updates repeatedly call `record.Demographics.First`. These should be indexed
when profiling shows they matter; the observed agricultural hotspot is the
seasonal preparation/integration/expansion path. See
[`GenerationalEconomy.cs`](../Assets/_Anthropocene/Simulation/GenerationalEconomy.cs),
[`CultivationEconomy.cs`](../Assets/_Anthropocene/Simulation/CultivationEconomy.cs),
[`TurnPlanning.cs`](../Assets/_Anthropocene/Simulation/TurnPlanning.cs), and
[`Logistics.cs`](../Assets/_Anthropocene/Simulation/Logistics.cs).

Fresh, branch-only workload counters at the last naturally completed turn
are below. The phase milliseconds and allocations come from `--metrics=0`;
activity counts come from a separate `--metrics=1` run with identical outcomes.
`allocatedBytes` covers AI, preparation, and resolution, excluding generation.

| Measure | 80 tribes, turn 10 | 320 tribes, turn 11 |
| --- | ---: | ---: |
| Agricultural camps | 58 | 248 |
| Unique active tiles `A` | 2,587 | 10,709 |
| Agricultural dependency tiles `F` | 394 | 1,733 |
| Independent active tiles `U` | 2,193 | 8,976 |
| Outer tile integration calls `U + 80F` | 33,713 | 147,616 |
| Claims allocated | 81,792 | 358,417 |
| Planner calls / forecast calls | 74 / 594 | 312 / 1,929 |
| Camp-season boundaries | 4,480 | 19,760 |
| Field increments / selected-tile switches | 3,732 / 3,395 | 16,238 / 14,484 |
| Target completions / tool exhaustions | 78 / 0 | 401 / 1 |
| Zero-food tile-segment endings with a food claim | 3,039 | 21,823 |
| Seasonal preparation | 140.3 ms | 708.8 ms |
| Seasonal tile integration | 152.5 ms | 269.1 ms |
| Seasonal receipt commit | 22.2 ms | 85.8 ms |
| Seasonal field expansion | 100.8 ms | 230.4 ms |
| Full-turn independent integration | 35.2 ms | 96.4 ms |
| Combined AI + preparation + resolution | 921.5 ms | 3,223.6 ms |
| Allocated bytes | 403.6 MB | 1,679.3 MB |

The four inner climate profiles are retained. `EconomicPlanner.Plan` runs
between turns, not at each of the 80 barriers. Seasonal *economic operations*
are the repeated cultivation labor/operation calculation, food-claim
construction, and field-choice scoring. The 14,484 selected-tile switches
are mostly the existing `0.01 E` marginal-scoring increments, not 14,484
distinct player decisions or opportunities to skip an interval. The
zero-food count is likewise a tile-segment ending state, not unique deposits
or first-time depletion events.

The 20 quick isolated transition cases produced 11,120 camp-season
boundaries, 11,545 field increments, 10,540 selected-tile switches, 178
target completions, four tool exhaustions, 30,008 zero-food tile-segment
endings, and 89,880 integration calls. In fixed dry-marginal alone: 400
camp-season boundaries, 556 field increments, 474 switches, and 3,437
integration calls. This fixture is particularly sensitive to a small
food/reserve difference changing the *next turn's* automated staffing.

The 320 turn-11 fresh sample spent 1,421 ms in ecology out of 3,224 ms
combined work. Even removing all ecology would have a roughly 44% ceiling
on combined **time reduction** for that sample. Removing seasonal preparation
alone (709 ms) has only a 22% time-reduction ceiling; actual savings would
be lower. Timing is
variable: earlier warmed 320 turn-11 samples measured seasonal preparation
from 369 to 1,269 ms, while numeric seasonal integration was near 254 ms.
Optimize allocation/claim preparation and field scoring before assuming a
faster numeric kernel or Burst backend can close the full gap.

## Recreate the measurements in this Windows checkout

Use Unity's .NET 8 SDK and Release builds. Run one process at a time, with no
Unity Editor or other benchmark load against this checkout. All JSON/CSV
output under `TestResults/` is ignored. Record `git rev-parse HEAD`, SDK
version, seed, shape, world size, worker count, partition size, completion
turn count, and whether detailed counters were enabled. The harness fixes
four workers and 64-item partitions. `--warmup=1` performs the same
96x48/4-player run before the measured world. `--players=320` is a
benchmark only and needs substantial memory.

```powershell
$sdk = 'C:/Program Files/Unity/Hub/Editor/6000.5.10f1/Editor/Data/DotNetSdk/dotnet.exe'
$audit = 'Tools/PerformanceAudit/bin/Release/net8.0/Anthropocene.PerformanceAudit.dll'
git rev-parse HEAD
& $sdk --version
& $sdk build Tools/PerformanceAudit/Anthropocene.PerformanceAudit.csproj -c Release -t:Rebuild --nologo
& $sdk $audit --players=80 --turns=12 --seed=1729 --shape=Continents --warmup=1 --output=TestResults/NeolithicHandoff-80.json
& $sdk $audit --players=320 --turns=12 --seed=1729 --shape=Continents --warmup=1 --output=TestResults/NeolithicHandoff-320.json
```

Capture activity separately because `--metrics=1` enables extra hot-path
counters and can distort wall time. It does not change authoritative results.
`--details=1` emits per-camp stores, staffing, and production when tracing an
isolated discrepancy; leave it off for timing.

```powershell
& $sdk $audit --players=80 --turns=12 --seed=1729 --shape=Continents --warmup=1 --metrics=1 --output=TestResults/NeolithicHandoff-80-activity.json
& $sdk $audit --players=320 --turns=12 --seed=1729 --shape=Continents --warmup=1 --metrics=1 --output=TestResults/NeolithicHandoff-320-activity.json
& $sdk run --project Tools/NeolithicTransitionValidation/Anthropocene.NeolithicTransitionValidation.csproj -c Release -- --quick --activity --out TestResults/NeolithicHandoffTransitionQuick
```

To reproduce a `main` timing reference without changing the checkout, extract
the pinned simulation source into ignored `TestResults/`. Build the *current
audit harness* against that source with `MAIN` defined. Rebuild the accepted
branch without `MAIN` afterward; both builds write the same DLL. Never run
them concurrently. Use a fresh output directory when repeating this recipe.

```powershell
git archive --format=zip --output=TestResults/NeolithicHandoffMainSource.zip e472cf760232eaa614b387a0212146221f505cd9 Assets/_Anthropocene/Simulation
Expand-Archive -LiteralPath TestResults/NeolithicHandoffMainSource.zip -DestinationPath TestResults/NeolithicHandoffMainSource -Force
$mainSimulation = (Resolve-Path 'TestResults/NeolithicHandoffMainSource/Assets/_Anthropocene/Simulation').Path
& $sdk build Tools/PerformanceAudit/Anthropocene.PerformanceAudit.csproj -c Release -t:Rebuild "-p:SimulationSource=$mainSimulation" '-p:DefineConstants=MAIN' --nologo
& $sdk $audit --players=80 --turns=12 --seed=1729 --shape=Continents --warmup=1 --output=TestResults/NeolithicHandoff-main-80.json
& $sdk $audit --players=320 --turns=12 --seed=1729 --shape=Continents --warmup=1 --output=TestResults/NeolithicHandoff-main-320.json
& $sdk build Tools/PerformanceAudit/Anthropocene.PerformanceAudit.csproj -c Release -t:Rebuild --nologo
```

Then run the accepted-branch commands again into separately named JSONs.
Compare the same turn numbers and `totalMs = aiMs + turnPrepareMs +
turnExecuteMs` (on `main`, `aiMs + resolveMs`). If natural completion differs,
disclose it; do not silently compare unequal turn sets. For a formal speed
decision, run **three sequential warmed pairs** at each size and report each
pair, their agricultural-turn medians, complete-opening totals, and every
corresponding post-breakthrough turn. Keep `--metrics=0` for those pairs.
Do not time `main` and branch in parallel. `main` QoL is not comparable to
the branch's prior-cohort QoL and is not a speed-gate input.

The fixed validation path remains:

```powershell
./Tools/Test-Headless.ps1 -Seeds 0
./Tools/Test-Headless.ps1 -Seeds 0 -ScalePlayers 80
./Tools/Test-Headless.ps1 -Seeds 0 -ScalePlayers 320
```

Future refactors must match the accepted prepared-turn checkpoint's
fingerprints exactly across scalar, two/four-worker, reversed-partition,
reordered-collection, and save-continuation runs. Preserve conservation and
the prior-cohort rule. The old ending-population QoL and isolated transition
comparison against `65af0df` are historical causality checks, not acceptance
targets. Re-run the 20 quick transition cases and the fixed dry-marginal
fixture before treating a Standard aggregate as proof of equivalence.

The seasonal instrumentation and this report were checked with
`Test-Headless.ps1 -Seeds 0`: the fixed suites and twelve-resolution
scalar/two/four-worker, reversed-partition, reordered-collection replay
passed. The fresh 80/320 normal and activity audit pairs matched their
population, prior-cohort weighted QoL, and food yield fields exactly at every
completed turn. The 20-case quick transition activity run completed.

Repository note: this workstation has two pre-existing broken local refs
under `refs/codex/turn-diffs/checkpoints`; an ordinary `git fetch` failed
during this handoff. The remote Neolithic branch tip was checked through the
GitHub API, the push succeeded using file-backed GitHub CLI authentication
with automatic maintenance disabled, and the local tracking branch matches
the remote tip. Repair those unrelated checkpoint refs before depending on
ordinary fetch/maintenance in this checkout; do not mistake their warning
for a simulation build or benchmark failure.
