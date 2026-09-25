# Paired turn audit

`Anthropocene.PerformanceAudit.csproj` compiles the actual simulation sources with the Unity-bundled .NET 8 SDK. It uses four workers, a 64-item partition, and the same generated world parameters for all runs. Timings include AI planning and the compatibility resolver's preparation, so moving work across the phase boundary cannot hide cost. JSON files belong in ignored `TestResults/`.

From the authoritative Windows checkout, build and run one source revision at a time:

```powershell
$sdk = 'C:/Program Files/Unity/Hub/Editor/6000.5.10f1/Editor/Data/DotNetSdk/dotnet.exe'
& $sdk build Tools/PerformanceAudit/Anthropocene.PerformanceAudit.csproj -c Release -t:Rebuild --nologo
& $sdk Tools/PerformanceAudit/bin/Release/net8.0/Anthropocene.PerformanceAudit.dll --players=80 --turns=12 --seed=1729 --shape=Continents --output=TestResults/PerformanceOptimized-80.json
```

For a saved branch revision, extract only `Assets/_Anthropocene/Simulation` with `git archive --format=zip`, expand it under ignored `TestResults/`, and pass its absolute directory to `-p:SimulationSource=...` at build time. Rebuild with `-p:DefineConstants=MAIN` when compiling `main`, which predates QoL and Neolithic types. Rebuild without that property before compiling this branch again. Use `--players=320` only as a benchmark; the world takes substantial memory. `--details=1` adds per-camp diagnostics for small fixtures. `--metrics=1` enables atomic hot-path counters and should be used for diagnosis, not timing comparisons.

Run `Compare.ps1 -Baseline <current-branch JSON> -Optimized <optimized JSON> -Main <main JSON>` to inspect the historical paired per-turn 2% population, prior-cohort-weighted QoL, and food-yield comparisons, plus the median and per-turn performance gates. The old ending-population behavior is no longer an outcome acceptance baseline: future parallel-only changes require exact fingerprints against the accepted prepared-turn checkpoint. Capture the prior resident population before each turn for the QoL weight; births first enter the following turn's weight. Zero QoL must remain zero. Natural opening completion can end a requested 12-turn run earlier; unequal completion turn counts fail the comparison. `--warmup=1` performs the same small run before the measured world when comparing warmed runtimes.

For isolated Neolithic cases, build `Tools/NeolithicTransitionValidation` against the selected revision with `-p:SimulationSource=<absolute simulation directory>`, run it with `--quick --case <comma-separated IDs> --out <ignored TestResults directory>`, and pass both `trajectory.jsonl` files to `CompareTransition.ps1 -Baseline ... -Optimized ...`. The transition tool uses `AiPlanning.Decide` for ordinary AI decisions so it measures the same recommendation path as the game. It compares each case and turn's population, QoL and demographic food production without smoothing away a failed isolated settlement.
