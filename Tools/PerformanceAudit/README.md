# Paired turn audit

## Reproduce the analytical turn-horizon comparison

The analytical resolver uses the seasonal prepared-turn checkpoint `c33a5b7`
as its behavioral and performance reference. Build the **same current audit
harness** against each source tree; this keeps measurement fields and the
managed-heap probe identical. Run all builds and audits sequentially in this
Windows checkout. The commands create only ignored files under
`TestResults/TurnHorizonRepro`.

```powershell
$sdk = (Resolve-Path ./.tools/dotnet/dotnet.exe).Path
$root = 'TestResults/TurnHorizonRepro'
New-Item -ItemType Directory -Path $root -Force | Out-Null
git archive --format=zip --output="$root/SeasonalSource.zip" c33a5b7 Assets/_Anthropocene/Simulation
Expand-Archive -LiteralPath "$root/SeasonalSource.zip" -DestinationPath "$root/SeasonalSource" -Force
$seasonalSource = (Resolve-Path "$root/SeasonalSource/Assets/_Anthropocene/Simulation").Path
& $sdk build Tools/PerformanceAudit/Anthropocene.PerformanceAudit.csproj -c Release -t:Rebuild "-p:SimulationSource=$seasonalSource" --output "$root/SeasonalBin" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Seasonal build failed' }
& $sdk build Tools/PerformanceAudit/Anthropocene.PerformanceAudit.csproj -c Release -t:Rebuild --output "$root/HorizonBin" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Horizon build failed' }
foreach ($size in @(80, 320)) {
    & $sdk "$root/SeasonalBin/Anthropocene.PerformanceAudit.dll" "--players=$size" --turns=12 --seed=1729 --shape=Continents --warmup=1 "--save-finished=$root/start-$size.anthro" "--output=$root/opening-$size.json"
    if ($LASTEXITCODE -ne 0) { throw "Opening save failed for $size" }
}
$candidateRevision = (git rev-parse HEAD).Trim()
& ./Tools/PerformanceAudit/Run-TurnHorizonPairs.ps1 -BaselineDll "$root/SeasonalBin/Anthropocene.PerformanceAudit.dll" -CandidateDll "$root/HorizonBin/Anthropocene.PerformanceAudit.dll" -Save80 "$root/start-80.anthro" -Save320 "$root/start-320.anthro" -OutputRoot "$root/Timing" -Mode Timing -Pairs 3 -CandidateRevision $candidateRevision
python Tools/PerformanceAudit/Summarize-TurnHorizonPairs.py "$root/Timing" --mode Timing
```

Run `-Mode Exact -Pairs 1` and `-Mode Counters -Pairs 1
-PostOpeningTurns 1` into separate directories with the same binary/save
arguments. Exact mode records fingerprints over the default 25-turn
continuation; counter mode can use one representative post-opening turn to
capture hot-path work without incurring a full instrumented continuation.
Counter-mode milliseconds are not a timing reference. The runner alternates
source order between timing pairs.
Its manifest records binary and save SHA-256 hashes. The summarizer aligns
per-turn samples, reports combined AI + preparation + resolution medians,
phase costs, allocations, managed heap, and per-turn ratios. See
[TurnHorizonRefactor.md](../../Docs/TurnHorizonRefactor.md) for outcome
calibration, acceptance signals, and measured results. For the 20 quick
Neolithic cases, build the current transition-validator harness against each
source tree and run the binaries sequentially:

```powershell
& $sdk build Tools/NeolithicTransitionValidation/Anthropocene.NeolithicTransitionValidation.csproj -c Release -t:Rebuild "-p:SimulationSource=$seasonalSource" --output "$root/SeasonalTransitionBin" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Seasonal transition build failed' }
& $sdk build Tools/NeolithicTransitionValidation/Anthropocene.NeolithicTransitionValidation.csproj -c Release -t:Rebuild --output "$root/HorizonTransitionBin" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Horizon transition build failed' }
& $sdk "$root/SeasonalTransitionBin/Anthropocene.NeolithicTransitionValidation.dll" --quick --seed-limit 18 --out "$root/SeasonalTransition"
if ($LASTEXITCODE -ne 0) { throw 'Seasonal transition run failed' }
& $sdk "$root/HorizonTransitionBin/Anthropocene.NeolithicTransitionValidation.dll" --quick --seed-limit 18 --out "$root/HorizonTransition"
if ($LASTEXITCODE -ne 0) { throw 'Horizon transition run failed' }
python Tools/NeolithicTransitionValidation/Compare-Horizons.py --baseline "$root/SeasonalTransition" --candidate "$root/HorizonTransition" --out "$root/TransitionComparison"
```

The comparator marks fixtures with different injected starting states as
unpaired. Its per-turn CSV includes field and nearby aquatic condition,
ending tool inventories, goods, material and food ledgers. The measured
comparison is in [TurnHorizonPerformanceAudit.md](../../Docs/TurnHorizonPerformanceAudit.md).

For the directional full-opening `main` context, archive pinned
`e472cf760232eaa614b387a0212146221f505cd9`, build the same audit
harness against that source, then run each variant from generation through
natural opening completion with the same seed, size, warmup and flags. Run
the processes sequentially; this is a single-run context, not a paired
acceptance measurement:

```powershell
git archive --format=zip --output="$root/MainSource.zip" e472cf760232eaa614b387a0212146221f505cd9 Assets/_Anthropocene/Simulation
Expand-Archive -LiteralPath "$root/MainSource.zip" -DestinationPath "$root/MainSource" -Force
$mainSource = (Resolve-Path "$root/MainSource/Assets/_Anthropocene/Simulation").Path
& $sdk build Tools/PerformanceAudit/Anthropocene.PerformanceAudit.csproj -c Release -t:Rebuild "-p:SimulationSource=$mainSource" --output "$root/MainBin" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Main audit build failed' }
foreach ($size in @(80, 320)) {
    foreach ($variant in @('Main', 'Seasonal', 'Horizon')) {
        $bin = "$root/${variant}Bin/Anthropocene.PerformanceAudit.dll"
        & $sdk $bin "--players=$size" --turns=12 --seed=1729 --shape=Continents --warmup=1 --fingerprints=0 --metrics=0 "--output=$root/opening-$variant-$size.json"
        if ($LASTEXITCODE -ne 0) { throw "Opening audit failed: $variant/$size" }
    }
}
```

`Anthropocene.PerformanceAudit.csproj` compiles the actual simulation sources with the Unity-bundled .NET 8 SDK. The default audit uses four workers and 64-item partitions; `--workers=N`, `--partition-size=N` and `--reverse-partitions=1` select exact-replay execution variants. `--turns` is the opening-turn limit. `--post-opening-turns=N` adds exactly N generations after `WorldState.Finished` becomes true; the resolver continues from that opening boundary with the original camps, herds, and policies. Each row records the phase before the turn, the number of agricultural camps before resolution, and the count after resolution. This makes the turn that crosses the opening boundary visible without counting its new agriculture as prior work.

The report is schema 2. Pass `--revision=<source SHA>` for the exact source being benchmarked. It records runtime and OS, configuration hash, worker settings, warmup count, counter mode, save-file SHA256, generation time, per-turn phase timings, allocations, GC collections, and fingerprints. `weightedQol` retains its earlier prior-cohort meaning; `endingPopulationWeightedQol` is also recorded. The older `resolveMs`, `turnPrepareMs`, and `turnExecuteMs` fields remain available.

`--warmup=1` runs one 96×48/four-player warmup (eight turns) before generation. `--metrics=1` enables diagnostic counters; leave counters and `--details` off for formal timing. Per-turn fingerprints default on and are computed outside measured intervals, but their serialization can trigger GC that affects a later turn. Use `--fingerprints=0` for formal timing, `--fingerprints=1` for every-turn exact checks, or `--fingerprints=2` for an initial and final exact hash without per-turn serialization. Timing reports explicitly say `replayMode=outcomes-only`; run a separate fingerprint-enabled replay for exact state verification before sign-off. Optional `--land-m`, `--land-access-scale` (uniform multiplier on all four natural land fractions), and `--cultivation-effort` overrides support opt-in calibration runs without changing production defaults.

Detailed field-selection callbacks conservatively invalidate the Option 5 score cache. Their timings therefore cannot measure the normal no-callback cache speedup; use that mode for event counts only. The small-world warmup does not establish that every mature agricultural path has been exercised.

## Reproduce the Options 1 and 5 audit

The checked revisions are baseline `9dc18c05d3a1ae0a7d3462049dd34a9b7c41f07d` and candidate `b5d98d72288d78a45a83967c5370a1cec5451d82`. The Option 5 ablation is a hybrid: baseline simulation sources with only `CultivationEconomy.cs` taken from the candidate. The commands below archive both revisions, build three distinct binaries, and create the natural opening-completion saves from the baseline binary. All artifacts go under a fresh ignored `TestResults/Options15-<timestamp>` directory.

Run every archive, build, save-generation, and audit command sequentially. Do not run builds or benchmarks at the same time; all builds use the same project output configuration and the timing mode intentionally alternates source order.

```powershell
$sdk = 'C:/Program Files/Unity/Hub/Editor/6000.5.10f1/Editor/Data/DotNetSdk/dotnet.exe'
$root = "TestResults/Options15-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
$baselineRevision = '9dc18c05d3a1ae0a7d3462049dd34a9b7c41f07d'
$candidateRevision = 'b5d98d72288d78a45a83967c5370a1cec5451d82'
New-Item -ItemType Directory -Path $root -Force | Out-Null

git archive --format=zip --output="$root/BaselineSource.zip" $baselineRevision Assets/_Anthropocene/Simulation
Expand-Archive -LiteralPath "$root/BaselineSource.zip" -DestinationPath "$root/BaselineSource" -Force
git archive --format=zip --output="$root/CandidateSource.zip" $candidateRevision Assets/_Anthropocene/Simulation
Expand-Archive -LiteralPath "$root/CandidateSource.zip" -DestinationPath "$root/CandidateSource" -Force
git archive --format=zip --output="$root/Option5Source.zip" $baselineRevision Assets/_Anthropocene/Simulation
Expand-Archive -LiteralPath "$root/Option5Source.zip" -DestinationPath "$root/Option5Source" -Force
Copy-Item -LiteralPath "$root/CandidateSource/Assets/_Anthropocene/Simulation/CultivationEconomy.cs" -Destination "$root/Option5Source/Assets/_Anthropocene/Simulation/CultivationEconomy.cs" -Force
Compress-Archive -Path "$root/Option5Source/Assets" -DestinationPath "$root/Option5HybridSource.zip" -Force

$sourceManifest = @(
    [ordered]@{ variant = 'baseline'; revision = $baselineRevision; archiveSha256 = (Get-FileHash -LiteralPath "$root/BaselineSource.zip" -Algorithm SHA256).Hash },
    [ordered]@{ variant = 'candidate'; revision = $candidateRevision; archiveSha256 = (Get-FileHash -LiteralPath "$root/CandidateSource.zip" -Algorithm SHA256).Hash },
    [ordered]@{ variant = 'option5'; revision = "$baselineRevision+CultivationEconomy@$candidateRevision"; archiveSha256 = (Get-FileHash -LiteralPath "$root/Option5HybridSource.zip" -Algorithm SHA256).Hash }
)
$sourceManifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$root/source-manifest.json"

$baselineSimulation = (Resolve-Path -LiteralPath "$root/BaselineSource/Assets/_Anthropocene/Simulation").Path
$candidateSimulation = (Resolve-Path -LiteralPath "$root/CandidateSource/Assets/_Anthropocene/Simulation").Path
$option5Simulation = (Resolve-Path -LiteralPath "$root/Option5Source/Assets/_Anthropocene/Simulation").Path
foreach ($bin in @('BaselineBin', 'CandidateBin', 'Option5Bin')) { New-Item -ItemType Directory -Path "$root/$bin" -Force | Out-Null }

& $sdk build Tools/PerformanceAudit/Anthropocene.PerformanceAudit.csproj -c Release -t:Rebuild --output "$root/BaselineBin" "-p:SimulationSource=$baselineSimulation" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Baseline build failed.' }
& $sdk build Tools/PerformanceAudit/Anthropocene.PerformanceAudit.csproj -c Release -t:Rebuild --output "$root/CandidateBin" "-p:SimulationSource=$candidateSimulation" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Candidate build failed.' }
& $sdk build Tools/PerformanceAudit/Anthropocene.PerformanceAudit.csproj -c Release -t:Rebuild --output "$root/Option5Bin" "-p:SimulationSource=$option5Simulation" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Option 5 ablation build failed.' }

foreach ($size in @(80, 320)) {
    & $sdk "$root/BaselineBin/Anthropocene.PerformanceAudit.dll" "--players=$size" --turns=12 --seed=1729 --shape=Continents --warmup=1 "--revision=$baselineRevision" "--save-finished=$root/start-$size.anthro" --fingerprints=1 "--output=$root/start-$size.json"
    if ($LASTEXITCODE -ne 0) { throw "Baseline opening/checkpoint generation failed for $size players." }
}
```

The runner writes one `*.manifest.json` beside each report. Each manifest records the variant/source label, binary SHA256, and checkpoint-save SHA256. `source-manifest.json` records SHA256 hashes for the archived source inputs; the Option 5 archive is the baseline tree with the candidate `CultivationEconomy.cs` substituted. Keep the source archives, extracted trees, binaries, saves, manifests, and reports together when retaining an audit.

The three runner modes are:

- `Exact` (default) loads the same `start-<size>.anthro` baseline save into baseline and candidate binaries, records fingerprints, and compares exact continuation. This is the behavior gate.
- `Timing` runs three matched baseline/candidate pairs per size, alternates which binary runs first, disables fingerprints and diagnostic counters, and writes paired summaries. Use this for repeated timing measurements.
- `Option5` runs the hybrid binary once per size as an exploratory ablation. It does not create paired timing evidence or apply the acceptance gates.

After the build and checkpoint steps above, choose exactly one mode for a run:

```powershell
& ./Tools/PerformanceAudit/Run-Options15.ps1 -Mode Exact -Sizes @(80, 320) -OutputRoot $root -Sdk $sdk
& ./Tools/PerformanceAudit/Run-Options15.ps1 -Mode Timing -Sizes @(80, 320) -OutputRoot $root -Sdk $sdk
& ./Tools/PerformanceAudit/Run-Options15.ps1 -Mode Option5 -Sizes @(80, 320) -OutputRoot $root -Sdk $sdk
```

For timing, `Compare-Pairs.ps1` validates the three paired reports and emits median paired ratios for total time, per-turn median, P95, maximum, and allocated bytes. Its targets are aggregate time ≤0.80, per-turn median ≤0.90, and P95 and maximum ≤1.05. Timing reports use outcomes-only replay and do not establish exact state equivalence; run `Exact` separately for that gate.

The comparator requires exactly 25 requested/completed post-opening turns by default (`-ExpectedPostOpeningTurns` can explicitly select another horizon), identical workload/source/runtime across repeats, and a shared save within each pair. It reports target flags without treating a missed speed target as malformed input. Review matched per-turn medians as well as the aggregate tails; [the measured Options 1/5 report](../../Docs/NeolithicOptions15Validation.md) records an unresolved final-turn regression that the aggregate tail summary alone would miss.

Use `Compare.ps1 -Baseline <JSON> -Optimized <JSON>` for a direct report comparison. Schema-2 reports require an explicit `replayMode`; exact mode verifies every turn plus initial, opening, and final fingerprints, while outcomes-only mode compares the measured outcomes exactly. Older reports retain the historical 2% population, prior-cohort QoL, and food-yield comparison. Optional `-Main` applies the historical opening-only main timing checks; `main` lacks the Neolithic economy and is not the post-opening acceptance baseline.

For isolated behavior checks, `Tools/NeolithicTransitionValidation` uses the real `AiPlanning.Decide` path. Keep its dry-marginal and placed-camp fixtures for changes that can alter outcomes. The standard audit measures wall time; detailed counters and per-camp diagnostics are separate diagnostic runs, not timing inputs.
