# Paired turn audit

`Anthropocene.PerformanceAudit.csproj` compiles the actual simulation sources with the Unity-bundled .NET 8 SDK. The default audit uses four workers and 64-item partitions. `--turns` is the opening-turn limit. `--post-opening-turns=N` adds exactly N generations after `WorldState.Finished` becomes true; the resolver continues from that opening boundary with the original camps, herds, and policies. Each row records the phase before the turn, the number of agricultural camps before resolution, and the count after resolution. This makes the turn that crosses the opening boundary visible without counting its new agriculture as prior work.

The report is schema 2. Pass `--revision=<source SHA>` for the exact source being benchmarked. It records runtime and OS, configuration hash, worker settings, warmup count, counter mode, save-file SHA256, generation time, per-turn phase timings, allocations, GC collections, and fingerprints. `weightedQol` retains its earlier prior-cohort meaning; `endingPopulationWeightedQol` is also recorded. The older `resolveMs`, `turnPrepareMs`, and `turnExecuteMs` fields remain available.

`--warmup=1` runs one 96×48/four-player warmup (eight turns) before generation. `--metrics=1` enables diagnostic counters; leave counters and `--details` off for formal timing. Per-turn fingerprints default on and are computed outside measured intervals, but their serialization can trigger GC that affects a later turn. Use `--fingerprints=0` for formal timing. Those reports explicitly say `replayMode=outcomes-only`; run a separate fingerprint-enabled replay for exact state verification before sign-off.

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
