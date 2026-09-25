param(
    [ValidateSet('Exact', 'Timing', 'Option5')][string]$Mode = 'Exact',
    [int[]]$Sizes = @(80, 320),
    [string]$OutputRoot = 'TestResults/Options15',
    [string]$Sdk = 'C:/Program Files/Unity/Hub/Editor/6000.5.10f1/Editor/Data/DotNetSdk/dotnet.exe',
    [string]$BaselineRevision = '9dc18c05d3a1ae0a7d3462049dd34a9b7c41f07d',
    [string]$CandidateRevision = 'b5d98d72288d78a45a83967c5370a1cec5451d82'
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $OutputRoot).Path
$dllName = 'Anthropocene.PerformanceAudit.dll'

function Invoke-Audit([string]$variant, [int]$size, [string]$suffix, [bool]$fingerprints) {
    $directory = switch ($variant) { 'baseline' { 'BaselineBin' } 'candidate' { 'CandidateBin' } 'option5' { 'Option5Bin' } }
    $revision = switch ($variant) { 'baseline' { $BaselineRevision } 'candidate' { $CandidateRevision } 'option5' { "$BaselineRevision+CultivationEconomy@$CandidateRevision" } }
    $dll = Join-Path $root "$directory/$dllName"
    $save = Join-Path $root "start-$size.anthro"
    $output = Join-Path $root "$variant-$size-$suffix.json"
    $log = Join-Path $root "$variant-$size-$suffix.log"
    if (!(Test-Path -LiteralPath $dll) -or !(Test-Path -LiteralPath $save)) {
        throw "Build $directory and generate start-$size.anthro before running the matrix."
    }
    $manifest = [ordered]@{
        variant = $variant; revision = $revision; size = $size; mode = $Mode
        executableSha256 = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash
        saveSha256 = (Get-FileHash -LiteralPath $save -Algorithm SHA256).Hash
    }
    $manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root "$variant-$size-$suffix.manifest.json")
    $auditArgs = @("--load=$save", '--turns=12', '--post-opening-turns=25', '--warmup=1',
        "--fingerprints=$([int]$fingerprints)", '--metrics=0', "--revision=$revision", "--output=$output")
    Write-Output "START $variant size=$size mode=$Mode suffix=$suffix"
    & $Sdk $dll @auditArgs *> $log
    if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath $log -Tail 20; throw "Audit failed: $variant/$size/$suffix" }
    Get-Content -LiteralPath $log -Tail 1
}

foreach ($size in $Sizes) {
    if ($Mode -eq 'Exact') {
        Invoke-Audit 'baseline' $size 'exact' $true
        Invoke-Audit 'candidate' $size 'exact' $true
        & (Join-Path $PSScriptRoot 'Compare.ps1') -Baseline (Join-Path $root "baseline-$size-exact.json") -Optimized (Join-Path $root "candidate-$size-exact.json")
        if ($LASTEXITCODE -ne 0) { throw "Exact continuation differs for $size tribes." }
    } elseif ($Mode -eq 'Timing') {
        for ($pair = 1; $pair -le 3; $pair++) {
            # Alternate process order; never benchmark simultaneously.
            $order = if ($pair % 2) { @('baseline', 'candidate') } else { @('candidate', 'baseline') }
            foreach ($variant in $order) { Invoke-Audit $variant $size "pair$pair" $false }
        }
        $baselineFiles = @(1..3 | ForEach-Object { Join-Path $root "baseline-$size-pair$_.json" })
        $candidateFiles = @(1..3 | ForEach-Object { Join-Path $root "candidate-$size-pair$_.json" })
        & (Join-Path $PSScriptRoot 'Compare-Pairs.ps1') -BaselineFiles $baselineFiles -OptimizedFiles $candidateFiles -OutputJson (Join-Path $root "summary-$size.json")
        if ($LASTEXITCODE -ne 0) { throw "Timing comparison failed for $size tribes." }
    } else {
        Invoke-Audit 'option5' $size 'ablation' $false
    }
}
