param(
    [Parameter(Mandatory = $true)][string]$Baseline,
    [Parameter(Mandatory = $true)][string]$Optimized,
    [string]$Main,
    [double]$LegacyOutcomeTolerance = 0.02
)

$old = Get-Content -LiteralPath $Baseline -Raw | ConvertFrom-Json
$new = Get-Content -LiteralPath $Optimized -Raw | ConvertFrom-Json
$failures = [System.Collections.Generic.List[string]]::new()

foreach ($field in @('seed', 'players', 'shape')) {
    if ($old.$field -ne $new.$field) { throw "Seed, player count, and shape must match ($field differs)." }
}
foreach ($field in @('configHash', 'worldWidth', 'worldHeight', 'turnDays', 'workerCount', 'partitionSize', 'warmupRuns', 'metricsEnabled', 'detailsEnabled', 'fingerprintsEnabled')) {
    if ($null -ne $old.$field -and $null -ne $new.$field -and $old.$field -ne $new.$field) {
        throw "Benchmark metadata must match ($field differs)."
    }
}

$schema2 = $old.schemaVersion -eq 2 -or $new.schemaVersion -eq 2
if ($schema2) {
    if ($old.schemaVersion -ne 2 -or $new.schemaVersion -ne 2) { throw 'Schema-2 reports can only be compared with schema-2 reports.' }
    foreach ($report in @($old, $new)) {
        foreach ($field in @('revision', 'seed', 'players', 'shape', 'configHash', 'runtime', 'runtimeVersion', 'operatingSystem', 'processArchitecture',
            'workerCount', 'partitionSize', 'turnDays', 'warmupRuns', 'metricsEnabled', 'detailsEnabled', 'fingerprintsEnabled', 'replayMode',
            'loadedFromSave', 'inputSaveSha256', 'openingTurnsMeasured', 'postOpeningTurnsRequested', 'postOpeningTurnsMeasured')) {
            if ($null -eq $report.$field) { throw "Schema-2 report is missing required metadata '$field'." }
        }
        if ($report.samples.Count -lt 1) { throw 'Schema-2 reports must contain at least one measured turn.' }
        if ($report.replayMode -notin @('exact', 'outcomes-only') -or (($report.replayMode -eq 'exact') -ne [bool]$report.fingerprintsEnabled)) {
            throw 'Schema-2 replayMode and fingerprintsEnabled must agree.'
        }
        if ($report.replayMode -eq 'exact' -and $report.postOpeningTurnsMeasured -gt 0 -and [string]::IsNullOrWhiteSpace([string]$report.finishedFingerprint)) {
            throw 'A post-opening workload requires a fingerprinted opening checkpoint in the report.'
        }
        if ($report.replayMode -eq 'outcomes-only' -and $report.loadedFromSave -and
            [string]::IsNullOrWhiteSpace([string]$report.inputSaveSha256)) {
            throw 'A loaded outcomes-only report requires the checkpoint save SHA256.'
        }
        foreach ($sample in $report.samples) {
            if ($null -eq $sample.turn -or [string]::IsNullOrWhiteSpace([string]$sample.phase)) {
                throw 'Schema-2 sample rows require turn and phase values.'
            }
        }
        if ($report.replayMode -eq 'exact') {
            if ([string]::IsNullOrWhiteSpace([string]$report.initialFingerprint) -or [string]::IsNullOrWhiteSpace([string]$report.finalFingerprint)) {
                throw 'Exact schema-2 reports require initial and final fingerprints.'
            }
            foreach ($sample in $report.samples) {
                if ([string]::IsNullOrWhiteSpace([string]$sample.fingerprint)) { throw "Exact schema-2 report is missing turn $($sample.turn) fingerprint." }
            }
        }
    }
    if ($old.replayMode -ne $new.replayMode) { throw 'Schema-2 reports must use the same replayMode.' }
    if ($old.loadedFromSave -ne $new.loadedFromSave) { throw 'Schema-2 reports must have the same loaded/generated start mode.' }
    if ($old.replayMode -eq 'outcomes-only' -and $old.loadedFromSave) {
        if ([string]::IsNullOrWhiteSpace([string]$old.inputSaveSha256) -or [string]::IsNullOrWhiteSpace([string]$new.inputSaveSha256) -or
            $old.inputSaveSha256 -ne $new.inputSaveSha256) {
            throw 'Loaded outcomes-only reports must use the same checkpoint save SHA256.'
        }
    }
}

function Index-Samples($report, [string]$label) {
    $index = @{}
    foreach ($sample in $report.samples) {
        $key = [string]$sample.turn
        if ($index.ContainsKey($key)) { throw "$label contains duplicate turn $key." }
        $index[$key] = $sample
    }
    return $index
}

$oldByTurn = Index-Samples $old 'Baseline'
$newByTurn = Index-Samples $new 'Optimized'
if ($oldByTurn.Count -ne $newByTurn.Count) { $failures.Add("Turn counts differ: baseline=$($oldByTurn.Count), optimized=$($newByTurn.Count)") }

$hasFingerprints = $schema2 -and $old.replayMode -eq 'exact'
if ($hasFingerprints) {
    if ($old.initialFingerprint -ne $new.initialFingerprint -or $old.finishedFingerprint -ne $new.finishedFingerprint -or $old.finalFingerprint -ne $new.finalFingerprint) {
        $failures.Add('Initial, opening-completion, or final fingerprint differs.')
    }
    foreach ($turn in $oldByTurn.Keys) {
        if (-not $newByTurn.ContainsKey($turn)) { $failures.Add("Missing optimized turn $turn"); continue }
        if ($oldByTurn[$turn].fingerprint -ne $newByTurn[$turn].fingerprint) {
            $failures.Add("Turn $turn fingerprint differs: baseline=$($oldByTurn[$turn].fingerprint), optimized=$($newByTurn[$turn].fingerprint)")
        }
        if ($oldByTurn[$turn].phase -ne $newByTurn[$turn].phase) { $failures.Add("Turn $turn phase differs.") }
    }
    if ($old.postOpeningTurnsMeasured -ne $new.postOpeningTurnsMeasured) { $failures.Add('Post-opening horizons differ.') }
    Write-Output "Comparison mode: exact per-turn fingerprints ($($old.revision) -> $($new.revision))."
} else {
    $outcomeTolerance = if ($schema2) { 0 } else { $LegacyOutcomeTolerance }
    Write-Output $(if ($schema2) { 'Comparison mode: schema-2 outcomes-only checks.' } else { "Comparison mode: legacy $([Math]::Round($LegacyOutcomeTolerance * 100, 2))% population, prior-cohort QoL, and food-yield outcomes." })
    foreach ($prior in $old.samples) {
        $key = [string]$prior.turn
        if (-not $newByTurn.ContainsKey($key)) { $failures.Add("Missing optimized turn $key"); continue }
        $current = $newByTurn[$key]
        $qolMetric = if ($null -ne $prior.priorWeightedQol -and $null -ne $current.priorWeightedQol) { 'priorWeightedQol' } else { 'weightedQol' }
        foreach ($metric in @('population', $qolMetric, 'foodYield')) {
            if ($null -eq $prior.$metric -or $null -eq $current.$metric) {
                if ($schema2 -and $old.replayMode -eq 'outcomes-only') { throw "Schema-2 outcomes-only comparison is missing turn $key metric '$metric'." }
                continue
            }
            # Older reports stored only weightedQol, which was already prior-cohort weighted.
            $reference = [double]$prior.$metric
            $observed = [double]$current.$metric
            $variance = if ([Math]::Abs($reference) -le 1e-10) {
                if ([Math]::Abs($observed) -le 1e-10) { 0 } else { [double]::PositiveInfinity }
            } else { [Math]::Abs($observed - $reference) / [Math]::Abs($reference) }
            if ($variance -gt $outcomeTolerance + 1e-12) {
                $failures.Add(('Turn {0} {1}: baseline={2:G9}, optimized={3:G9}, variance={4:P3}' -f
                    $key, $metric, $reference, $observed, $variance))
            }
        }
    }
}

if ($Main) {
    $paleo = Get-Content -LiteralPath $Main -Raw | ConvertFrom-Json
    foreach ($field in @('seed', 'players', 'shape')) {
        if ($paleo.$field -ne $new.$field) { throw "Main benchmark scenario must match ($field differs)." }
    }
    $mainByTurn = Index-Samples $paleo 'Main'
    $mainDurations = @($paleo.samples | ForEach-Object { [double]$_.totalMs } | Sort-Object)
    $newDurations = @($new.samples | ForEach-Object { [double]$_.totalMs } | Sort-Object)
    if ($mainDurations.Count -ne $newDurations.Count) {
        $failures.Add('Main and optimized benchmarks completed different numbers of turns.')
    } elseif ($mainDurations.Count -gt 0) {
        $middle = [int][Math]::Floor($mainDurations.Count / 2)
        $mainMedian = if ($mainDurations.Count % 2) { $mainDurations[$middle] } else { ($mainDurations[$middle - 1] + $mainDurations[$middle]) / 2 }
        $newMedian = if ($newDurations.Count % 2) { $newDurations[$middle] } else { ($newDurations[$middle - 1] + $newDurations[$middle]) / 2 }
        if ($newMedian -gt 1.10 * $mainMedian) {
            $failures.Add(('Historical main median: main={0:F1} ms, optimized={1:F1} ms, ratio={2:F2}x' -f
                $mainMedian, $newMedian, ($newMedian / $mainMedian)))
        }
        foreach ($prior in $paleo.samples) {
            $key = [string]$prior.turn
            if (-not $newByTurn.ContainsKey($key)) { continue }
            $current = $newByTurn[$key]
            $preAgriculture = if ($null -ne $current.preAgriculturalCamps) { $current.preAgriculturalCamps } else { $current.agriculturalCamps }
            if ($preAgriculture -gt 0 -and $current.totalMs -gt 2 * $prior.totalMs) {
                $failures.Add(('Turn {0} planning plus resolution: main={1:F1} ms, optimized={2:F1} ms, ratio={3:F2}x' -f
                    $key, $prior.totalMs, $current.totalMs, ($current.totalMs / $prior.totalMs)))
            }
        }
    }
}

if ($failures.Count -eq 0) {
    $successMessage = if ($Main) { 'PASS: selected comparison checks.' }
        elseif ($hasFingerprints) { 'PASS: exact fingerprints by turn.' }
        elseif ($schema2) { 'PASS: schema-2 outcomes-only checks; exact replay is not established.' }
        else { 'PASS: legacy outcomes within tolerance.' }
    Write-Output $successMessage
    exit 0
}
$failures | ForEach-Object { Write-Output "FAIL: $_" }
exit 1
