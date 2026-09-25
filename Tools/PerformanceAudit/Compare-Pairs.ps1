param(
    [Parameter(Mandatory = $true)][string[]]$BaselineFiles,
    [Parameter(Mandatory = $true)][string[]]$OptimizedFiles,
    [string]$OutputJson,
    [ValidateRange(1, 10000)][int]$ExpectedPostOpeningTurns = 25
)

if ($BaselineFiles.Count -lt 3 -or $BaselineFiles.Count -ne $OptimizedFiles.Count) {
    throw 'Supply at least three matched baseline/candidate report paths.'
}

function Median([double[]]$values) {
    $sorted = @($values | Sort-Object)
    if ($sorted.Count -eq 0) { return [double]::NaN }
    $middle = [int][Math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2) { return $sorted[$middle] }
    return ($sorted[$middle - 1] + $sorted[$middle]) / 2
}

function Percentile([double[]]$values, [double]$fraction) {
    $sorted = @($values | Sort-Object)
    if ($sorted.Count -eq 0) { return [double]::NaN }
    $rank = [Math]::Max(1, [Math]::Ceiling($fraction * $sorted.Count))
    return $sorted[$rank - 1]
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

function Require-Report($report, [string]$label) {
    $required = @('schemaVersion', 'revision', 'seed', 'players', 'shape', 'configHash', 'runtime', 'runtimeVersion',
        'operatingSystem', 'processArchitecture', 'worldWidth', 'worldHeight', 'turnDays', 'workerCount', 'partitionSize',
        'warmupRuns', 'metricsEnabled', 'detailsEnabled', 'fingerprintsEnabled', 'replayMode', 'loadedFromSave',
        'inputSaveSha256', 'openingTurnsMeasured', 'postOpeningTurnsRequested', 'postOpeningTurnsMeasured', 'samples')
    foreach ($field in $required) {
        if ($null -eq $report.$field) { throw "$label is missing required schema-2 field '$field'." }
    }
    if ($report.schemaVersion -ne 2) { throw "$label must use schemaVersion 2." }
    if ([string]::IsNullOrWhiteSpace([string]$report.revision) -or $report.revision -eq 'unspecified') { throw "$label must declare --revision with a source SHA." }
    if ($report.replayMode -notin @('exact', 'outcomes-only')) { throw "$label replayMode must be exact or outcomes-only." }
    if (($report.replayMode -eq 'exact') -ne [bool]$report.fingerprintsEnabled) { throw "$label replayMode and fingerprintsEnabled disagree." }
    if (-not $report.loadedFromSave -or [string]::IsNullOrWhiteSpace([string]$report.inputSaveSha256)) {
        throw "$label must load the shared post-opening checkpoint and report its SHA256."
    }
    if ($report.postOpeningTurnsRequested -ne $ExpectedPostOpeningTurns -or $report.postOpeningTurnsMeasured -ne $ExpectedPostOpeningTurns) {
        throw "$label must request and complete exactly $ExpectedPostOpeningTurns post-opening turns."
    }
    if ($report.openingTurnsMeasured -ne 0 -or $report.samples.Count -ne $ExpectedPostOpeningTurns) {
        throw "$label must contain only the fixed post-opening checkpoint workload."
    }
    if ($report.replayMode -eq 'exact' -and [string]::IsNullOrWhiteSpace([string]$report.initialFingerprint)) {
        throw "$label exact mode requires initialFingerprint."
    }
    if ($report.replayMode -eq 'exact' -and [string]::IsNullOrWhiteSpace([string]$report.finishedFingerprint)) {
        throw "$label exact mode requires finishedFingerprint for the shared opening checkpoint."
    }
    foreach ($sample in $report.samples) {
        foreach ($field in @('turn', 'phase', 'postOpening', 'preAgriculturalCamps', 'agriculturalCampsAfterTurn',
            'population', 'priorWeightedQol', 'foodYield', 'totalMs', 'allocatedBytes')) {
            if ($null -eq $sample.$field) { throw "$label turn row is missing '$field'." }
        }
        $turnNumber = 0
        if (-not [int]::TryParse([string]$sample.turn, [ref]$turnNumber)) { throw "$label has a non-integer turn identifier." }
        if ([string]::IsNullOrWhiteSpace([string]$sample.phase)) { throw "$label has an empty phase identifier." }
        if (($sample.postOpening -and $sample.phase -ne 'post-opening') -or (-not $sample.postOpening -and $sample.phase -ne 'opening')) {
            throw "$label turn $turnNumber has inconsistent phase/cohort identifiers."
        }
        if ($sample.preAgriculturalCamps -lt 0 -or $sample.agriculturalCampsAfterTurn -lt 0) {
            throw "$label turn $turnNumber has a negative agricultural cohort count."
        }
        $duration = 0.0
        if (-not [double]::TryParse([string]$sample.totalMs, [ref]$duration) -or [double]::IsNaN($duration) -or [double]::IsInfinity($duration) -or $duration -le 0) {
            throw "$label turn $turnNumber has nonpositive or nonfinite totalMs."
        }
        if ($report.replayMode -eq 'exact' -and [string]::IsNullOrWhiteSpace([string]$sample.fingerprint)) {
            throw "$label exact mode requires a fingerprint for every sample."
        }
    }
    $postSamples = @($report.samples | Where-Object { $_.postOpening })
    if ($postSamples.Count -ne $ExpectedPostOpeningTurns) { throw "$label has the wrong post-opening sample cohort." }
    if (@($postSamples | Where-Object { $_.preAgriculturalCamps -gt 0 }).Count -eq 0) {
        throw "$label post-opening horizon contains no turn with agricultural camps before resolution."
    }
    $postTurns = @($postSamples | ForEach-Object { [int]$_.turn } | Sort-Object)
    for ($turnIndex = 1; $turnIndex -lt $postTurns.Count; $turnIndex++) {
        if ($postTurns[$turnIndex] -ne $postTurns[$turnIndex - 1] + 1) { throw "$label post-opening turn identifiers are not consecutive." }
    }
    if ($report.replayMode -eq 'exact' -and [string]::IsNullOrWhiteSpace([string]$report.finalFingerprint)) {
        throw "$label exact mode requires finalFingerprint."
    }
}

$baselineRunProfile = $null
$optimizedRunProfile = $null
$profileFields = @('revision', 'seed', 'players', 'shape', 'configHash', 'runtime', 'runtimeVersion', 'operatingSystem',
    'processArchitecture', 'worldWidth', 'worldHeight', 'turnDays', 'workerCount', 'partitionSize', 'warmupRuns',
    'metricsEnabled', 'detailsEnabled', 'replayMode', 'loadedFromSave', 'inputSaveSha256',
    'postOpeningTurnsRequested', 'postOpeningTurnsMeasured')
$rows = [System.Collections.Generic.List[object]]::new()
$pairNumber = 0
for ($i = 0; $i -lt $BaselineFiles.Count; $i++) {
    $pairNumber++
    $baseline = Get-Content -LiteralPath $BaselineFiles[$i] -Raw | ConvertFrom-Json
    $optimized = Get-Content -LiteralPath $OptimizedFiles[$i] -Raw | ConvertFrom-Json
    Require-Report $baseline "Pair $pairNumber baseline"
    Require-Report $optimized "Pair $pairNumber optimized"

    $baselineProfile = @($profileFields | ForEach-Object { "$($_)=$($baseline.$_)" }) -join '|'
    $optimizedProfile = @($profileFields | ForEach-Object { "$($_)=$($optimized.$_)" }) -join '|'
    if ($null -eq $baselineRunProfile) { $baselineRunProfile = $baselineProfile }
    elseif ($baselineProfile -ne $baselineRunProfile) { throw "Pair $pairNumber baseline workload, revision, or runtime differs from the other pairs." }
    if ($null -eq $optimizedRunProfile) { $optimizedRunProfile = $optimizedProfile }
    elseif ($optimizedProfile -ne $optimizedRunProfile) { throw "Pair $pairNumber optimized workload, revision, or runtime differs from the other pairs." }

    foreach ($field in @('schemaVersion', 'seed', 'players', 'shape', 'configHash', 'runtime', 'runtimeVersion', 'operatingSystem',
        'processArchitecture', 'worldWidth', 'worldHeight', 'turnDays', 'replayMode', 'finishedFingerprint',
        'workerCount', 'partitionSize', 'warmupRuns', 'metricsEnabled', 'detailsEnabled', 'fingerprintsEnabled', 'loadedFromSave',
        'inputSaveSha256', 'initialFingerprint')) {
        if ($baseline.$field -ne $optimized.$field) { throw "Pair $pairNumber metadata mismatch: $field." }
    }
    if ($baseline.metricsEnabled -or $optimized.metricsEnabled -or $baseline.detailsEnabled -or $optimized.detailsEnabled) {
        throw "Pair $pairNumber has diagnostic counters/details enabled; timing comparisons require both off."
    }
    if ($baseline.initialFingerprint -ne $optimized.initialFingerprint) { throw "Pair $pairNumber starting state fingerprint mismatch." }
    if ($baseline.finishedFingerprint -ne $optimized.finishedFingerprint) { throw "Pair $pairNumber opening checkpoint fingerprint mismatch." }
    if ($baseline.replayMode -eq 'exact' -and $baseline.finalFingerprint -ne $optimized.finalFingerprint) {
        throw "Pair $pairNumber final fingerprint mismatch."
    }
    if ($baseline.postOpeningTurnsMeasured -lt 1 -or $baseline.postOpeningTurnsMeasured -ne $optimized.postOpeningTurnsMeasured) {
        throw "Pair $pairNumber does not contain the same non-empty post-opening horizon."
    }
    if ($baseline.samples.Count -ne $optimized.samples.Count) { throw "Pair $pairNumber sample counts differ." }

    $beforeByTurn = Index-Samples $baseline "Pair $pairNumber baseline"
    $afterByTurn = Index-Samples $optimized "Pair $pairNumber optimized"
    foreach ($turn in $beforeByTurn.Keys) {
        if (-not $afterByTurn.ContainsKey($turn)) { throw "Pair $pairNumber optimized report lacks turn $turn." }
        if ($baseline.fingerprintsEnabled) {
            if ($beforeByTurn[$turn].fingerprint -ne $afterByTurn[$turn].fingerprint) {
                throw "Pair $pairNumber turn $turn fingerprint mismatch."
            }
        }
        foreach ($metric in @('population', 'priorWeightedQol', 'foodYield')) {
            if ($null -eq $beforeByTurn[$turn].$metric -or $null -eq $afterByTurn[$turn].$metric) {
                throw "Pair $pairNumber turn $turn is missing cohort outcome $metric."
            }
            if ($beforeByTurn[$turn].$metric -ne $afterByTurn[$turn].$metric) {
                throw "Pair $pairNumber turn $turn outcome differs: $metric."
            }
        }
        if ($beforeByTurn[$turn].phase -ne $afterByTurn[$turn].phase) { throw "Pair $pairNumber turn $turn phase mismatch." }
        foreach ($field in @('postOpening', 'preAgriculturalCamps', 'agriculturalCampsAfterTurn')) {
            if ($beforeByTurn[$turn].$field -ne $afterByTurn[$turn].$field) {
                throw "Pair $pairNumber turn $turn cohort field '$field' differs."
            }
        }
    }

    $before = @($baseline.samples | Where-Object { $_.postOpening })
    $after = @($optimized.samples | Where-Object { $_.postOpening })
    if ($before.Count -ne $baseline.postOpeningTurnsMeasured -or $after.Count -ne $before.Count) {
        throw "Pair $pairNumber post-opening sample count does not match its metadata."
    }
    $beforeTimes = [double[]]@($before | ForEach-Object { [double]$_.totalMs })
    $afterTimes = [double[]]@($after | ForEach-Object { [double]$_.totalMs })
    $beforeAlloc = [double[]]@($before | ForEach-Object { [double]$_.allocatedBytes })
    $afterAlloc = [double[]]@($after | ForEach-Object { [double]$_.allocatedBytes })
    $beforeTotal = ($beforeTimes | Measure-Object -Sum).Sum
    $afterTotal = ($afterTimes | Measure-Object -Sum).Sum
    $beforeMedian = Median $beforeTimes
    $afterMedian = Median $afterTimes
    $beforeP95 = Percentile $beforeTimes 0.95
    $afterP95 = Percentile $afterTimes 0.95
    $beforeMax = ($beforeTimes | Measure-Object -Maximum).Maximum
    $afterMax = ($afterTimes | Measure-Object -Maximum).Maximum
    $beforeAllocated = ($beforeAlloc | Measure-Object -Sum).Sum
    $afterAllocated = ($afterAlloc | Measure-Object -Sum).Sum
    $beforeAllocMedian = Median $beforeAlloc
    $afterAllocMedian = Median $afterAlloc

    $rows.Add([pscustomobject]@{
        pair = $pairNumber
        baselineRevision = $baseline.revision
        optimizedRevision = $optimized.revision
        replayMode = $baseline.replayMode
        players = $baseline.players
        seed = $baseline.seed
        horizon = $before.Count
        baselineTotalMs = [Math]::Round($beforeTotal, 1)
        optimizedTotalMs = [Math]::Round($afterTotal, 1)
        totalRatio = $afterTotal / $beforeTotal
        aggregateTargetPassed = ($afterTotal / $beforeTotal) -le 0.80
        baselineMedianMs = [Math]::Round($beforeMedian, 1)
        optimizedMedianMs = [Math]::Round($afterMedian, 1)
        medianRatio = $afterMedian / $beforeMedian
        medianTargetPassed = ($afterMedian / $beforeMedian) -le 0.90
        baselineP95Ms = [Math]::Round($beforeP95, 1)
        optimizedP95Ms = [Math]::Round($afterP95, 1)
        p95Ratio = $afterP95 / $beforeP95
        p95TailPassed = ($afterP95 / $beforeP95) -le 1.05
        baselineMaxMs = [Math]::Round($beforeMax, 1)
        optimizedMaxMs = [Math]::Round($afterMax, 1)
        maxRatio = $afterMax / $beforeMax
        maxTailPassed = ($afterMax / $beforeMax) -le 1.05
        baselineAllocatedBytes = [long]$beforeAllocated
        optimizedAllocatedBytes = [long]$afterAllocated
        allocatedRatio = if ($beforeAllocated -eq 0) { [double]::NaN } else { $afterAllocated / $beforeAllocated }
        baselineAllocatedMedianBytes = [long]$beforeAllocMedian
        optimizedAllocatedMedianBytes = [long]$afterAllocMedian
        allocatedMedianRatio = if ($beforeAllocMedian -eq 0) { [double]::NaN } else { $afterAllocMedian / $beforeAllocMedian }
    })
}

$rows | Format-Table pair,players,seed,horizon,replayMode,totalRatio,medianRatio,p95Ratio,maxRatio,allocatedRatio,allocatedMedianRatio -AutoSize
$summary = [pscustomobject]@{
    pairs = $rows.Count
    players = ($rows[0].players)
    replayMode = $rows[0].replayMode
    exactReplayVerified = ($rows[0].replayMode -eq 'exact')
    exactReplayStatus = if ($rows[0].replayMode -eq 'exact') { 'verified in these reports' } else { 'requires a separate exact replay run' }
    medianTotalRatio = Median ([double[]]@($rows | ForEach-Object totalRatio))
    aggregateTargetPassed = (Median ([double[]]@($rows | ForEach-Object totalRatio))) -le 0.80
    medianPerTurnRatio = Median ([double[]]@($rows | ForEach-Object medianRatio))
    medianTargetPassed = (Median ([double[]]@($rows | ForEach-Object medianRatio))) -le 0.90
    medianP95Ratio = Median ([double[]]@($rows | ForEach-Object p95Ratio))
    p95TailPassed = (Median ([double[]]@($rows | ForEach-Object p95Ratio))) -le 1.05
    medianMaxRatio = Median ([double[]]@($rows | ForEach-Object maxRatio))
    maxTailPassed = (Median ([double[]]@($rows | ForEach-Object maxRatio))) -le 1.05
    tailTargetsPassed = ((Median ([double[]]@($rows | ForEach-Object p95Ratio))) -le 1.05) -and
        ((Median ([double[]]@($rows | ForEach-Object maxRatio))) -le 1.05)
    medianAllocatedRatio = Median ([double[]]@($rows | ForEach-Object allocatedRatio))
    medianAllocatedMedianRatio = Median ([double[]]@($rows | ForEach-Object allocatedMedianRatio))
}
Write-Output "Median paired ratios (optimized / baseline):"
$summary | Format-List
if ($summary.replayMode -eq 'outcomes-only') {
    Write-Warning 'Timing rows use outcome-only verification. Run a separate exact replay comparison for final sign-off.'
}
if (-not [string]::IsNullOrWhiteSpace($OutputJson)) {
    $document = [pscustomobject]@{
        schemaVersion = 1
        createdUtc = [DateTime]::UtcNow.ToString('O')
        targetDefinitions = [pscustomobject]@{
            aggregateTotalRatioMaximum = 0.80
            perTurnMedianRatioMaximum = 0.90
            p95AndMaximumRatioMaximum = 1.05
            ratioDirection = 'optimized / baseline'
        }
        summary = $summary
        pairs = @($rows)
    }
    $fullPath = [System.IO.Path]::GetFullPath($OutputJson)
    [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($fullPath)) | Out-Null
    $encoding = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText($fullPath, ($document | ConvertTo-Json -Depth 8), $encoding)
    Write-Output "Wrote paired summary JSON."
}
