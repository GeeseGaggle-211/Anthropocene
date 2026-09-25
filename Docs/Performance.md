# Scale measurements - September 13, 2026

## Neolithic Options 1/5 follow-up — September 25, 2026

The production resolver on `codex/turn-horizon-refactor` now evaluates one
analytical transition per turn. Its measured calibration and paired speed
evidence are in [TurnHorizonRefactor.md](TurnHorizonRefactor.md). The Options
1/5 and seasonal results below describe earlier checkpoints.

The exact-cache offshoot `codex/neolithic-options-1-5-validation` compares simulation candidate `b5d98d7` against `9dc18c0`. Three sequential matched pairs at each size cover 25 post-opening generations (500 years), with all tribes/herds and the audit's original API defaults. Median paired AI + preparation + resolution time falls **3.7% at 80 tribes** and **13.6% at 320**, missing the proposed **20%** aggregate target at both sizes. Allocated bytes fall approximately **15.5%/15.3%**. The 80-tribe runtime results are mixed; the 320-tribe aggregate results improve consistently, but a repeatable >5% final-turn slowdown remains unresolved. Separate exact runs match all 25 per-turn fingerprints at both sizes.

The measured seasonal loop still accounts for roughly 34% of candidate 80-tribe end-to-end time; numerical seasonal integration alone is about 11%. Reducing the repeated preparation and expansion work is relevant to the retained analytical design, but its speed and trajectory accuracy remain untested. See [NeolithicOptions15Validation.md](NeolithicOptions15Validation.md) for every pair, tail/GC evidence, provenance, limits and the experimental-branch disposition, and [NeolithicAnalyticalTurnPlan.md](NeolithicAnalyticalTurnPlan.md) for the follow-up proposal. The following measurements remain historical; no new `main`-relative gate pass is claimed.

## Historical scale baseline

Windows x64; Unity-bundled .NET 8.0.318; seed **1729**, Continents, default twenty-year turns. Each audit runs all twelve turns, a four-worker resolver, a scalar replay world, fingerprints after every turn, and an initial/final save/load. These are local development measurements, not isolated microbenchmarks. All reference and restored fingerprints matched.

| Measurement | 1,024 x 512 / 80 tribes | 2,048 x 1,024 / 320 tribes | Ratio |
|---|---:|---:|---:|
| World generation | 7.915 s | 46.791 s | 5.91x |
| AI, twelve turns | 0.755 s | 1.795 s | 2.38x |
| Resolution, twelve turns | 1.931 s | 9.113 s | 4.72x |
| Active ecology, twelve turns | 0.483 s | 2.047 s | 4.24x |
| Herd intentions, twelve turns | 0.423 s | 2.070 s | 4.90x |
| Scalar reference resolution, twelve turns | 3.582 s | 16.021 s | 4.47x |
| Initial save | 1.244 s | 4.684 s | 3.76x |
| Initial load | 1.224 s | 5.210 s | 4.26x |
| Final save | 1.466 s | 5.035 s | 3.43x |
| Final load | 2.066 s | 10.256 s | 4.96x |
| Compressed final save | 39.337 MiB | 146.806 MiB | 3.73x |
| Managed heap after generation, one world | 215.072 MiB | 852.213 MiB | 3.96x |
| Peak whole-process working set | 1516.430 MiB | 4516.988 MiB | 2.98x |
| Highest sampled managed heap, including final load | 930.612 MiB | 3683.375 MiB | 3.96x |

The heap peak is sampled, not continuously profiled. The table includes the final-load sample; earlier JSON field `peakSampledManagedBytes` covered generation and turn samples only, so the table takes the greater of that field and `managedBytes`. The runner now includes final load in the named peak field too. Whole-process peak includes the replay/restored world, serialization buffers, runtime and diagnostics; it is not the memory required by one live game.

| Workset | 80 tribes | 320 tribes |
|---|---:|---:|
| Settlements | 80 | 320 |
| Hunter groups | 272 | 1060 |
| Herd entities, wild and owned | 1639 | 6956 |
| Active tiles, first to last resolution | 2282 to 2326 | 9291 to 9539 |
| First / twelfth resolution | 86.3 / 226.1 ms | 476.3 / 1055.4 ms |

Ordinary resolution does not scan all tiles for renewal. A shared planning index supplies local occupant queries and projected ecology. Independent AI/herd intentions and active-tile integrations use bounded workers; IDs, collisions, reductions and history commit in canonical order. Per-settlement reporting indexes avoid repeated scans of the entire transfer log. Sparse per-claim pressure and balance diagnostics are retained for the latest resolution and excluded from save metadata; authoritative continuation state and outcome receipts remain saved.

Four times the area/tribes requires 4.24x active-ecology work and 4.72x complete resolution in this measurement. Generation (5.91x) and herd intentions (4.90x) remain above ideal linear scaling. Generation still uses dense per-tile objects, seasonal arrays and global sorting. This is an explicit remaining cost, not evidence of an exact linear bound.

**320 tribes remains benchmark-only.** Its single-world managed heap after generation is about 852 MiB, approaching the existing 1,000 MB generation budget. Before exposing a large preset, profile and compact dense tile/season storage; do not raise the budget to conceal the cost. The renderer is camera-culled and simplifies layers with zoom, but this audit is a headless simulation measurement, not a rendering frame-time measurement.

Reproduce from the Windows checkout:

```powershell
./Tools/Test-Headless.ps1 -Seeds 0 -ScalePlayers 80
./Tools/Test-Headless.ps1 -Seeds 0 -ScalePlayers 320
```

Raw JSON is written to ignored `TestResults/EcologyScale-80.json` and `TestResults/EcologyScale-320.json`. The checked-in table preserves the measured results without committing generated logs or saves.

## Prepared-turn pipeline audit — September 25, 2026

For a focused handoff on the accepted `features/neolithic-agricultural`
branch, including the current cost model, fresh seasonal activity counters,
and commands to reproduce matched `main` runs, see
[NeolithicTurnPerformanceHandoff.md](NeolithicTurnPerformanceHandoff.md).

The branch's unmodified `65af0df` revision changed any agricultural world from one full-turn integration per active tile to 80 global seasonal outer passes. In a matched .NET 8 Release, four-worker, Standard/Continents/1729/80 audit, its turn 6 took 7.02 s AI planning and 18.86 s resolution; the prepared-turn implementation took 0.225 s planning and 0.225 s resolution on the same turn and matched population (4,319) and realized human food yield (388,575.506). The older 80-tribe QoL JSON used ending-population weights, so its QoL number must not be compared with the corrected prior-cohort metric. The unmodified branch reached turn 7 at 46.06 s planning and 212.39 s resolution; the full unmodified 80 run was stopped during turn 8 because it was no longer a practical validation loop. These measurements are the same executable and source revision within each run; they are directional across separate process executions.

The new pipeline prepares intents and prior-cohort targets once, indexes local material sources, batches stable extraction choices, and integrates only agricultural dependency tiles seasonally. Independent active tiles retain one analytic full-turn integration. The `TileIntegrations` counter reports actual work: Standard/80 turn 10 integrated 33,713 tiles (394 seasonal dependency tiles, 2,193 independent tiles) while `ActiveTiles` reported 2,587 unique tiles. Standard/320 turn 11 integrated 147,616 tiles (1,733 seasonal dependency tiles). Counts, plan/forecast calls, local source queries, allocation claims, production receipts, and phase timings are transient; hot-path source lookup counting is opt-in with `--metrics=1` in `Tools/PerformanceAudit`.

The strict performance completion gates are **not yet met**. In the latest matched warmed harness, `main` versus this branch completed 10 turns at 80 tribes and 11 at 320, with planning-plus-resolution medians of 190.1/249.0 ms and 504.0/933.0 ms respectively (1.31x and 1.85x). Totals were 1.857/4.049 s and 5.748/17.208 s. Agricultural turns 8–10 at 80 tribes took 3.65–6.04x their corresponding `main` turns; turns 7–11 at 320 took 2.52–4.72x. The benchmark used the same Windows checkout, Unity-bundled .NET 8 Release runtime, seed 1729, Continents shape, four workers, 64-item partitions and a common small-world warmup. `main` lacks the branch's Neolithic economy, so this comparison includes the full feature cost. The historical 2.686 s/10.908 s totals covered twelve turns and are reference signals only. After the final feedstock-target correction, the 320-tribe scalar/parallel/reordered/save continuation check completed through its natural turn-11 ending with exact fingerprints. The latest warm run integrated 147,616 tiles in turn 11. Timing varies with GC and process state; the specified ceiling fails by a margin too large to attribute to that variance.

Paired unmodified-branch versus prepared-turn outcomes passed the former per-turn relative 2% population, prior-cohort-weighted QoL, and realized-food comparison for all five 96×48/4-player Continents seeds (1, 2, 3, 11, 42). Seed 11's largest QoL differences were 1.80% and 1.88% at turns 11–12. Weighting by ending population would incorrectly charge births against the completed cohort. The Standard/80 matched outcomes were identical through turn 6 and within 0.47% on food at turn 7; later unmodified-branch turns were not completed. `Tools/PerformanceAudit/Compare.ps1` reproduces this historical comparison; a future parallel-only change requires exact replay against the prepared-turn checkpoint.

The **historical paired isolated Neolithic transition comparison is retired as an acceptance gate for this checkpoint**: the user chose the prior-cohort pass structure as the new behavioral baseline. The transition runner was updated to use `AiPlanning.Decide`, the real interturn AI path, because a direct ordinary observation deliberately no longer computes economic recommendations. Three matched quick cases from unmodified `65af0df` and the prepared-turn branch exceeded 2% after correcting the prepared plant-feedstock shortfall: in the placed-camp control, turn-8 production of 100 cloth covers 109 starting residents here (QoL 0.459) versus 139 ending residents in the old code (QoL 0.360). The denominator difference is required by the prior-cohort rule. In the fixed dry-marginal case, turn-13 population is 55 versus 46, a 16.4% gap. These differences remain disclosed causal evidence; subsequent parallel-only changes must match this checkpoint exactly. The matched-`main` performance gate remains open.

At the **seasonal checkpoint**, the 80 outer barriers were retained to satisfy
its then-current equivalence gate. Paleo built one fixed turn-wide claim per
active tile; the Neolithic path changed field operation and food claims after
each seasonal receipt. Collapsing those barriers to 20 annual barriers kept
the Standard aggregate within 2%, but exceeded that gate in the fixed
dry-marginal case: population differed by up to 19.6% and one turn's food
production by 80.9%. Forty half-year barriers still differed by up to 10.9%
population and 5.9% food. The later turn-horizon refactor deliberately
replaced these intermediate decisions with a fixed turn allocation and an
analytical resource transition; the older partition-equivalence test is now
calibration evidence, not the production contract. Reducing the planner's
fulfillment search from twelve to six probes preserved exact trajectories in
all 20 quick transition cases under the former seasonal model.

## Transition causality, AI scheduling and Burst assessment — September 25, 2026

The paired transition traces are in ignored `TestResults/TransitionBaselinePairedFinal/trajectory.jsonl` and `TestResults/TransitionOptimizedGapFix/trajectory.jsonl`. `Tools/PerformanceAudit/AnalyzeTransition.py` prints the first and subsequent differences in orders, staffing, production, food, cultivation capital and QoL. The first material outcome differences followed **different economic plans**, not an isolated change in the stock integration equations:

`python Tools/PerformanceAudit/AnalyzeTransition.py TestResults/TransitionBaselinePairedFinal/trajectory.jsonl TestResults/TransitionOptimizedGapFix/trajectory.jsonl placed-camp-control dry-marginal-normal fixed-dry-marginal` reproduces the compact comparison.

| Case and first difference | Unmodified branch | Prepared-turn branch | One-line diagnostic restoring the old planner's predicted-newborn population |
|---|---:|---:|---:|
| Dry marginal, turn 7 | 8 craft / 2 farm; food 7,502.19; population 99 | 9 craft / 0 farm; food 7,244.94; population 92 | Old staffing, food and population reproduced exactly; Hand Tools still differed 160 vs 158.06 |
| Fixed dry marginal, turn 10 | 6 craft / 31 farm; food 3,552.37; field intensity 0.03605; population 44 | 19 craft / 17 farm; food 3,474.04; intensity 0.02341; population 42 | Old staffing, food, intensity and population reproduced exactly |
| Fixed dry marginal, turn 13 | Population 55 | Population 46 | Population 56 (1.8% over old rather than 16.4% under) |
| Placed camp, turn 8 | 100 cloth; 139 ending residents; QoL 0.359712 | 100 cloth; 109 starting residents; QoL 0.458716 | Old staffing and food reproduced; QoL remains 0.458716 |

That diagnostic changed only `EconomicPlanner.Plan`'s resident forecast from the intended prior cohort back to `PlanningResidentPopulation`; the source was immediately restored. It is **not** a proposed fix: forecasting newborn goods in the same turn violates the locked-cohort rule. With matched orders, the first dry-site food and cultivation values matched the old branch, so the sparse seasonal frontier has no demonstrated independent effect at those first divergences. There are still genuine downstream economy changes: the fixed dry turn-10 matched-plan diagnostic made 74 cloth rather than 82.94, and later tool output and inventory changed later plans. The QoL denominator difference is required by prior-cohort semantics. A numerical ±2% comparison to the old ending-cohort QoL cannot pass that turn while making the same 100 cloth for 109 rather than 139 residents.

The desktop currently calls `AiPlanning.Decide` inside `EndTurn`'s worker after the human commits. AI plans are independent filtered-observation reads of the committed world and do not observe pending human orders. They are already parallel across players through `PhaseExecution.Map`, although the default 64-camp partition gives only two partitions for 79 AI tribes and five for 319. A safe latency change is to start one tagged AI planning task after world adoption/turn completion, while the player queues orders; at commit, the worker awaits that task, checks the captured world identity/turn/day, merges the human orders, and prepares/resolves once. Loaded/replaced worlds invalidate old tasks. This moves AI latency out of the commit wait when the player takes longer than planning, but does not reduce total CPU. The final `TurnPlanner.Prepare` still depends on merged player orders and remains after commit unless split into cached nonplayer needs plus a small player finalization.

The next parallelism boundary is per-camp **pure evaluation**, followed by a canonical merge: compute AI intents and nonplayer need forecasts into indexed result slots, validate/merge orders in player order, construct per-camp seasonal claims into private buffers, merge them by tile/entity, integrate tiles, commit receipts, then allocate shared deposits in source ID order. Whole camp production can be parallel only after shared source allocation and with private receipts committed in camp ID order. The existing `PhaseExecution.Map` is sufficient to test these boundaries before a Unity-only job backend. Smaller, balanced planning partitions need their own wall-time and replay check; the current 64-camp chunks can strand workers when agricultural camps have uneven forecast costs.

A warmed Standard/Continents/1729 four-worker audit measured turn-10 AI / final preparation / execution at **236 / 34 / 551 ms** for 80 tribes and **438 / 190 / 1,896 ms** for 320. At turn 11 the 320 values were **446 / 198 / 2,596 ms**. These timings are from separate process runs, and phase duration varies with allocation and GC. The per-turn population, weighted QoL and food fields in these instrumented runs matched the earlier optimized run exactly. The aggregate planning-plus-resolution gate remains failed; moving AI work to player time changes perceived wait, not this gate.

The installed Unity package lock contains Burst 1.8.30 and Collections 6.5.0, but the headless-compatible `Anthropocene.Simulation` assembly has no Unity references. Burst cannot compile the current managed `WorldState`, `Camp`, LINQ, dictionaries or per-tile receipt lists directly. In a Standard/320 turn-11 audit, the 80-barrier ecology phase spent **369 ms preparing claims, 255 ms integrating seasonal tiles, 81 ms integrating independent tiles, and 211 ms expanding fields**, out of 1,021 ms ecology and 2,596 ms execution. A second warmed run measured 1,269 / 254 / 94 / 236 ms for the same four components, showing large preparation-time variability while numeric integration stayed near 250 ms. On Standard/80 turn 10, those components were 135 / 130 / 21 / 90 ms out of 401 ms ecology. Burst of the numeric integration alone has an impossible zero-cost ceiling of about 13% of 320 turn-11 execution; packing and scheduling lower its real potential. Profile allocation and simplify seasonal claim preparation first. A later Burst proof of concept should extract one allocation-free numeric tile kernel over blittable snapshots and native arrays, keep shared-source allocation and canonical receipts in managed code, and require exact Unity/headless fingerprint replay before activation. Do not parallelize whole tribe turns across shared tiles or deposits.

## Resource overlay measurement — September 18, 2026

The generation-only geology overlay adds compact rasters (12 bytes/tile) and sparse deposit bodies. Six Standard/80 worlds (seeds 42/1729, all shapes) measured 6.24–6.88 MiB of estimated packed resource payload and 153–284 ms of overlay generation/validation. These are .NET 8 Release exporter observations, not Unity timing guarantees.

The 2048×1024 Archipelagos/42/**80-tribe** audit measured 25.14 MiB packed resource payload, 917.73 MiB total managed heap after collection, and 434 ms overlay time. The existing 1,000 MiB budget was retained. Its full resource-bearing save was 230,574,431 bytes and passed resource fingerprint restoration. This is distinct from the historical 320-tribe benchmark above. Peak transient allocation was not instrumented; no claim of a 64 MiB managed-overhead cap or new public large preset follows from the packed-payload measurement.

The resource-generator-2 occurrence follow-up on the same large seed/configuration measured **25.63 MiB** packed payload, **918.40 MiB** collected managed heap, **722.6 ms** overlay time and **426** resource bodies/fields. Its **230,773,224-byte** save passed exact resource restoration. The run overlapped other validation/build work, so elapsed-time differences are not an isolated performance comparison. Evidence: `TestResults/ResourceDistributionV2/audit.log`.
