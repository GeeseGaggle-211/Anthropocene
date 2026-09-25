param(
    [Parameter(Mandatory = $true)][string]$Baseline,
    [Parameter(Mandatory = $true)][string]$Optimized,
    [string]$Case
)

$old = @{}
Get-Content -LiteralPath $Baseline | ForEach-Object {
    $row = $_ | ConvertFrom-Json
    if ($null -ne $row.turn -and (!$Case -or $row.id -eq $Case)) { $old["$($row.id):$($row.turn)"] = $row }
}
$new = @{}
Get-Content -LiteralPath $Optimized | ForEach-Object {
    $row = $_ | ConvertFrom-Json
    if ($null -ne $row.turn -and (!$Case -or $row.id -eq $Case)) { $new["$($row.id):$($row.turn)"] = $row }
}
$failures = [System.Collections.Generic.List[string]]::new()
foreach ($key in $old.Keys) {
    if (-not $new.ContainsKey($key)) { $failures.Add("Missing optimized turn $key"); continue }
    $prior = $old[$key]; $current = $new[$key]
    $pairs = @(
        @('population', [double]$prior.population, [double]$current.population),
        @('qol', [double]$prior.qols.qol, [double]$current.qols.qol),
        @('foodProduced', [double]$prior.demographic.FoodProduced, [double]$current.demographic.FoodProduced)
    )
    foreach ($pair in $pairs) {
        $reference = [double]$pair[1]; $observed = [double]$pair[2]
        $variance = if ([Math]::Abs($reference) -le 1e-10) {
            if ([Math]::Abs($observed) -le 1e-10) { 0 } else { [double]::PositiveInfinity }
        } else { [Math]::Abs($observed - $reference) / [Math]::Abs($reference) }
        if ($variance -gt 0.02 + 1e-12) {
            $failures.Add(('{0} {1}: baseline={2:G9}, optimized={3:G9}, variance={4:P3}' -f
                $key, $pair[0], $reference, $observed, $variance))
        }
    }
}
foreach ($key in $new.Keys) {
    if (-not $old.ContainsKey($key)) { $failures.Add("Additional optimized turn $key") }
}
if ($failures.Count -eq 0) { Write-Output 'PASS: paired transition outcomes <=2% per turn.'; exit 0 }
$failures | Sort-Object | ForEach-Object { Write-Output "FAIL: $_" }
exit 1
