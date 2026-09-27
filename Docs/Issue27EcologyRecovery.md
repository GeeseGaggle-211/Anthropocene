# Issue #27: active/passive land recovery

Base: `features/neolithic-agricultural` at `8fa6e7c6ec2a6d2df76aef0322e05346c57dd09a`. The issue branch is `codex/issue-27-ecology-recovery`. Measurements below use Windows x64, Unity's bundled .NET 8 SDK and the same source revision for each candidate binary. The ignored `TestResults/Issue27/` directory holds the local binaries, saves, reports and logs.

## Authority and numerical rule

The saved tile stocks, land condition, cultivation capital, vegetation and Wood source are authoritative at turn boundaries. Claims debit opening land stock once. Active recovery and pure projection then use the same ordered affine maps of the four saved `SeasonData` profiles; materialization commits that projection. A complete four-profile cycle is composed once and repeated algebraically. The two possible partial-cycle edges use bounded work and cache each distinct capacity for the fixed representative land state. No seasonal state is committed and no loop count depends on `dt / BaseDays`.

Stored `LandCondition` remains fixed. For unplanned decaying cultivation, recovery uses `Emean = E0 * MeanExponential(lambda, dt)` and final reclassification uses `Eend = E0 * exp(-lambda * dt)`; explicit field-allocation mean and endpoint values override these defaults. `q = Intensity(E)` is evaluated from those representative values, not stepped each season. This aggregate approximation is not an exact semigroup under arbitrary partitions. No-withdrawal land freshwater shares the ordered seasonal transition; drinking claims retain the existing finite horizon solver. Wood remains a pure companion from the same start and capped endpoint views. No save field or format changes.

## C# witness and numerical error

`Issue27EcologyChecks` sets `q=0`, frozen `LandCondition=.75`, Food and biomass to 5, effective seasonal Food ceilings to `[75, 7.5, 75, 75]`, both recovery rates to `.01/day`, `BaseDays=91.25` and duration to 7,300 days, with no claims. Biomass ceilings are 150, so they do not hide the Food discrepancy.

| C# endpoint | Before: active | Before: passive / materialized | After: all three |
| --- | ---: | ---: | ---: |
| Food | 64.1178085773345 | 58.125 | 64.1178085773345 |
| PotentialFood | 139.1178085773345 | 150 | 139.1178085773345 |
| Freshwater | 0 | 0 | 0 |

The regression matrix covers 4 cultivation intensities × 3 conditions × 3 operating fractions × constant/strongly seasonal profiles × empty/partial/full stock × zero/nonzero recovery × aligned/partial-season starts. It checks Food, biomass, water, capital, vegetation, fixed condition, stock-flow balance, pure query, materialization, saved/dormant/touched continuation and Wood. A follow-up fixture also starts a `q=1` tile with Food 5 and biomass 10, below capacity and with zero recovery: active integration, pure projection and materialization agree at zero and twenty years, and a Food claim cannot receive more than the opening Food stock. A save-loaded full-field tile retains the same finite Food and active/passive endpoint. Full-field capacity does not mint Food on read. Numeric equality uses relative tolerance `1e-8`; no bitwise equality is promised between different aggregate partitions.

| 20-year projection | Whole horizon Food | Annual partitions | Seasonal partitions | Largest absolute difference |
| --- | ---: | ---: | ---: | ---: |
| `q=0`, fixed condition | 64.1178085773345 | 64.1178085773345 | 64.1178085773345 | 0 |
| `q=.6`, operation 0 | 79.84339869436177 | 79.77455085413064 | 79.77545927834738 | 0.068847840231129 (0.0862% of whole) |

The seasonal partition is a slow repeated-transition reference, not an additional authoritative simulation clock. The reported error is a fixture measurement, not a general error bound. The core evaluates four distinct profiles for `q=0` and five when decaying capital requires reclassification, at both aligned and tested off-phase starts for 20, 200 and 2,000 years. A horizon whose start and end occupy different profiles can require one further reclassification capacity. Work remains `O(active tiles + claims + locally projected tiles)` per turn, with a fixed profile cost per tile and no added retained memory.

## Performance audit

The baseline and candidate PerformanceAudit binaries were built in Release against the same audit harness. Opening-complete 80- and benchmark-only 320-tribe saves were generated once by the baseline binary, then reused by both variants. Four workers and 64-item partitions were used. Formal timing disables diagnostic counters and fingerprints; separate one-turn counter and exact runs check work and replay. The first post-opening counter run has identical active tile integrations and claims in both variants: 2,618/3,395 at 80 and 10,678/13,596 at 320. Its food receipt totals also match. Cross-version fingerprints differ, as expected when latent nonquiet tiles use the corrected physical recovery.

| Three paired 25-turn continuations | 80 tribes: baseline → candidate | 320 tribes: baseline → candidate |
| --- | ---: | ---: |
| Pair 1 total time | 4,981 → 4,774 ms (0.959×) | 29,184 → 27,777 ms (0.952×) |
| Pair 2 total time | 5,119 → 4,948 ms (0.967×) | 22,343 → 29,395 ms (1.316×) |
| Pair 3 total time | 5,051 → 4,756 ms (0.941×) | 22,408 → 22,018 ms (0.983×) |
| Median paired total ratio | 0.959 | 0.983 |
| Allocated bytes, median of each variant's three runs | 3.379 → 3.356 GiB | 13.196 → 13.131 GiB |
| Peak managed heap, median of each variant's three runs | 419.9 → 418.6 MiB | 1,673.9 → 1,682.3 MiB |

These totals include AI planning, turn preparation and resolution across all 25 turns. Per-column medians of the three runs, in milliseconds, give more phase detail (the independently chosen medians need not sum to the median total):

| 25-turn phase | 80 baseline → candidate | 320 baseline → candidate |
| --- | ---: | ---: |
| AI planning | 1,535 → 1,273 | 4,267 → 5,166 |
| Turn preparation | 1,718 → 1,605 | 7,792 → 9,069 |
| Resolution | 1,827 → 1,978 | 10,828 → 11,426 |
| Ecology phase within resolution | 409 → 404 | 2,293 → 4,172 |
| Tile integration within ecology | 114 → 100 | 890 → 1,023 |

Ten repeated first post-opening turn pairs from the same saves hold the opening workload fixed. Median paired candidate/baseline ratios are:

| Same starting turn, ten pairs | 80 tribes | 320 tribes |
| --- | ---: | ---: |
| Total time | 1.027 (pair range 0.483–1.094) | 0.959 (0.583–1.348) |
| AI planning | 0.971 | 1.052 |
| Turn preparation | 0.967 | 0.869 |
| Resolution | 1.048 | 0.888 |
| Ecology phase | 0.951 | 1.004 |
| Tile integration | 0.902 | 1.305 |
| Allocated bytes | 0.998 | 0.995 |

The 320-tribe 25-turn pair-2 tail (1.316×), and the wider same-turn tails, exceed the proposed 5% full-turn screen; the screen is **not cleared**. Phase times and GC/scheduling vary substantially between pairs, and corrected projections can change orders even from the same save. The 25-turn paths also diverge in active tile and claim counts, so their total ratios are directional rather than a same-workload kernel speed result. The exact first-turn states differ despite equal counts and receipts.

An isolated fixed-input Release benchmark clarifies the local cost. Median-of-three process runs of the current source for a 20-year query give 484 → 582 ns/call for aligned `q=.6` passive land and 461 → 793 ns/call at a partial-season start; both allocate zero bytes. A zero-claim aligned `q=.6` active integration gives 943 → 1,099 ns/call, with unchanged 408 bytes/call. These are measured per-call increases from ordered composition and cultivation reclassification. Profile caching bounds them; it does not erase the cost of the required seasonal law. They must be considered in later RL query budgets. No claim of an overall runtime win is made.

## Validation and reproduction

The initial reconciliation passed `--issue27-ecology-only` (4,346 assertions) and the full headless suites. The full-field follow-up passes `--issue27-ecology-only` (4,366 assertions) and the final `./Tools/Test-Headless.ps1 -Seeds 0`. The same production code passed `./Tools/Test-Headless.ps1 -Seeds 30 -Standard -Geography` (30/30 seeded worlds, Standard generation/opening/save/load and three geography shapes); two save-loaded assertions were added after that broad run and passed focused and full `-Seeds 0` checks. The fixed suite includes scalar/two/four-worker, reordered-collection and save-continuation replay.

From the repository root, the focused witness is:

```powershell
& 'C:/Program Files/Unity/Hub/Editor/6000.5.10f1/Editor/Data/DotNetSdk/dotnet.exe' run --project Tools/Headless/Anthropocene.Headless.csproj -- --issue27-ecology-only
```

Build `Tools/PerformanceAudit/Anthropocene.PerformanceAudit.csproj` in Release once with `-p:SimulationSource=<archive of 8fa6e7c simulation>` and once with the candidate source. Generate opening-complete saves using the baseline binary's `--save-finished` flag. Reproduce paired timing with `Tools/PerformanceAudit/Run-TurnHorizonPairs.ps1` using those DLLs, saves, `-Mode Timing -Pairs 3 -PostOpeningTurns 25`; use `-Mode Counters -Pairs 1 -PostOpeningTurns 1` and `-Mode Exact -Pairs 1 -PostOpeningTurns 1` separately. The harness, flags and source-archive procedure are specified in `Tools/PerformanceAudit/README.md`.
