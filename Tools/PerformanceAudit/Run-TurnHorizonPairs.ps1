param(
    [Parameter(Mandatory = $true)][string]$BaselineDll,
    [Parameter(Mandatory = $true)][string]$CandidateDll,
    [Parameter(Mandatory = $true)][string]$Save80,
    [Parameter(Mandatory = $true)][string]$Save320,
    [Parameter(Mandatory = $true)][string]$OutputRoot,
    [string]$BaselineRevision = 'c33a5b7',
    [string]$CandidateRevision = 'working',
    [ValidateSet('Timing', 'Exact', 'Counters')][string]$Mode = 'Timing',
    [ValidateRange(1, 10)][int]$Pairs = 3,
    [ValidateRange(1, 100)][int]$PostOpeningTurns = 25
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$sdkCandidates = @(
    (Join-Path $repo '.tools/dotnet/dotnet.exe'),
    'C:/Program Files/Unity/Hub/Editor/6000.5.10f1/Editor/Data/DotNetSdk/dotnet.exe'
)
$sdk = $sdkCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $sdk) { $sdk = (Get-Command dotnet -ErrorAction Stop).Source }
$baseline = (Resolve-Path -LiteralPath $BaselineDll).Path
$candidate = (Resolve-Path -LiteralPath $CandidateDll).Path
$saves = @{
    80 = (Resolve-Path -LiteralPath $Save80).Path
    320 = (Resolve-Path -LiteralPath $Save320).Path
}
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
$output = (Resolve-Path -LiteralPath $OutputRoot).Path
$fingerprints = if ($Mode -eq 'Exact') { 1 } else { 0 }
$metrics = if ($Mode -eq 'Counters') { 1 } else { 0 }
$manifest = [ordered]@{
    createdUtc = (Get-Date).ToUniversalTime().ToString('o')
    mode = $Mode
    pairs = $Pairs
    baselineRevision = $BaselineRevision
    candidateRevision = $CandidateRevision
    baselineDll = $baseline
    baselineDllSha256 = (Get-FileHash -LiteralPath $baseline -Algorithm SHA256).Hash
    candidateDll = $candidate
    candidateDllSha256 = (Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash
    save80 = $saves[80]
    save80Sha256 = (Get-FileHash -LiteralPath $saves[80] -Algorithm SHA256).Hash
    save320 = $saves[320]
    save320Sha256 = (Get-FileHash -LiteralPath $saves[320] -Algorithm SHA256).Hash
    sdk = $sdk
    warmupRuns = 1
    postOpeningTurns = $PostOpeningTurns
    workerCount = 4
    partitionSize = 64
}
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $output "manifest-$Mode.json")

foreach ($size in @(80, 320)) {
    for ($pair = 1; $pair -le $Pairs; $pair++) {
        $order = if ($pair % 2 -eq 1) { @('baseline', 'candidate') } else { @('candidate', 'baseline') }
        foreach ($variant in $order) {
            $dll = if ($variant -eq 'baseline') { $baseline } else { $candidate }
            $revision = if ($variant -eq 'baseline') { $BaselineRevision } else { $CandidateRevision }
            $report = Join-Path $output "$Mode-$size-pair$pair-$variant.json"
            Write-Host "TURN_HORIZON_AUDIT mode=$Mode size=$size pair=$pair variant=$variant"
            & $sdk $dll "--load=$($saves[$size])" --turns=0 "--post-opening-turns=$PostOpeningTurns" `
                --warmup=1 "--fingerprints=$fingerprints" "--metrics=$metrics" `
                "--revision=$revision" "--output=$report"
            if ($LASTEXITCODE -ne 0) { throw "Audit failed: $Mode/$size/pair$pair/$variant" }
        }
    }
}
