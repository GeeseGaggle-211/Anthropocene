param(
    [Parameter(Mandatory = $true)][string]$Baseline,
    [Parameter(Mandatory = $true)][string]$Optimized,
    [string]$Main
)

$old = Get-Content -LiteralPath $Baseline -Raw | ConvertFrom-Json
$new = Get-Content -LiteralPath $Optimized -Raw | ConvertFrom-Json
$oldShape = if ($old.shape) { $old.shape } else { 'Continents' }
$newShape = if ($new.shape) { $new.shape } else { 'Continents' }
if ($old.seed -ne $new.seed -or $old.players -ne $new.players -or $oldShape -ne $newShape) {
    throw 'Seed, player count, and shape must match.'
}

$failures = [System.Collections.Generic.List[string]]::new()
if ($old.samples.Count -ne $new.samples.Count) {
    $failures.Add("Turn counts differ: baseline=$($old.samples.Count), optimized=$($new.samples.Count)")
}
foreach ($prior in $old.samples) {
    $current = $new.samples | Where-Object turn -eq $prior.turn | Select-Object -First 1
    if ($null -eq $current) { $failures.Add("Missing optimized turn $($prior.turn)"); continue }
    foreach ($metric in @('population', 'weightedQol', 'foodYield')) {
        $reference = [double]$prior.$metric
        $observed = [double]$current.$metric
        $variance = if ([Math]::Abs($reference) -le 1e-10) {
            if ([Math]::Abs($observed) -le 1e-10) { 0 } else { [double]::PositiveInfinity }
        } else { [Math]::Abs($observed - $reference) / [Math]::Abs($reference) }
        if ($variance -gt 0.02 + 1e-12) {
            $failures.Add(('Turn {0} {1}: baseline={2:G9}, optimized={3:G9}, variance={4:P3}' -f
                $prior.turn, $metric, $reference, $observed, $variance))
        }
    }
}

if ($Main) {
    $paleo = Get-Content -LiteralPath $Main -Raw | ConvertFrom-Json
    if ($paleo.seed -ne $new.seed -or $paleo.players -ne $new.players -or $paleo.shape -ne $new.shape) {
        throw 'Main benchmark seed, player count, and shape must match.'
    }
    $mainDurations = @($paleo.samples | ForEach-Object { [double]$_.totalMs } | Sort-Object)
    $newDurations = @($new.samples | ForEach-Object { [double]$_.totalMs } | Sort-Object)
    if ($mainDurations.Count -ne $newDurations.Count) {
        $failures.Add('Main and optimized benchmarks completed different numbers of turns.')
    } else {
        $middle = [int][Math]::Floor($mainDurations.Count / 2)
        $mainMedian = if ($mainDurations.Count % 2) { $mainDurations[$middle] } else { ($mainDurations[$middle - 1] + $mainDurations[$middle]) / 2 }
        $newMedian = if ($newDurations.Count % 2) { $newDurations[$middle] } else { ($newDurations[$middle - 1] + $newDurations[$middle]) / 2 }
        if ($newMedian -gt 1.10 * $mainMedian) {
            $failures.Add(('Median planning plus resolution: main={0:F1} ms, optimized={1:F1} ms, ratio={2:F2}x' -f
                    $mainMedian, $newMedian, ($newMedian / $mainMedian)))
        }
        foreach ($prior in $paleo.samples) {
            $current = $new.samples | Where-Object turn -eq $prior.turn | Select-Object -First 1
            if ($null -ne $current -and $current.agriculturalCamps -gt 0 -and $current.totalMs -gt 2 * $prior.totalMs) {
                $failures.Add(('Turn {0} planning plus resolution: main={1:F1} ms, optimized={2:F1} ms, ratio={3:F2}x' -f
                    $prior.turn, $prior.totalMs, $current.totalMs, ($current.totalMs / $prior.totalMs)))
            }
        }
    }
}

if ($failures.Count -eq 0) {
    Write-Output $(if ($Main) { 'PASS: paired outcomes and matched-main performance gates.' } else { 'PASS: paired outcomes <=2% per turn.' })
    exit 0
}
$failures | ForEach-Object { Write-Output "FAIL: $_" }
exit 1
