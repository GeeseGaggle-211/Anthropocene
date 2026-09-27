param(
    [Parameter(Mandatory=$true)][string]$BaselineDll,
    [Parameter(Mandatory=$true)][string]$CandidateDll,
    [Parameter(Mandatory=$true)][string]$CheckpointRoot,
    [Parameter(Mandatory=$true)][string]$OutputRoot,
    [Parameter(Mandatory=$true)][string]$BaselineRevision,
    [Parameter(Mandatory=$true)][string]$CandidateRevision,
    [int]$Pairs = 3,
    [ValidateSet('Timing','Details','Counters')][string]$Mode = 'Timing',
    [string]$Sdk = '.tools/dotnet/dotnet.exe'
)
$ErrorActionPreference = 'Stop'
if ($Pairs -lt 1) { throw 'Pairs must be positive.' }
$Sdk = (Resolve-Path -LiteralPath $Sdk).Path
$BaselineDll = (Resolve-Path -LiteralPath $BaselineDll).Path
$CandidateDll = (Resolve-Path -LiteralPath $CandidateDll).Path
$CheckpointRoot = (Resolve-Path -LiteralPath $CheckpointRoot).Path
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
$OutputRoot = (Resolve-Path -LiteralPath $OutputRoot).Path
$cases = @(@(80,11), @(80,23), @(80,1729), @(320,1729))
$manifest = [ordered]@{
    mode = $Mode; pairs = $Pairs; postOpeningTurns = 25
    baselineRevision = $BaselineRevision; candidateRevision = $CandidateRevision
    baselineBinarySha256 = (Get-FileHash -LiteralPath $BaselineDll -Algorithm SHA256).Hash
    candidateBinarySha256 = (Get-FileHash -LiteralPath $CandidateDll -Algorithm SHA256).Hash
    createdUtc = [DateTime]::UtcNow.ToString('O'); runs = @()
}
for ($pair = 1; $pair -le $Pairs; $pair++) {
    foreach ($case in $cases) {
        $players = $case[0]; $seed = $case[1]
        $save = Join-Path $CheckpointRoot "start-$players-$seed.anthro"
        if (-not (Test-Path -LiteralPath $save)) { throw "Missing checkpoint: $save" }
        $variants = if ($pair % 2 -eq 1) { @('Baseline','Candidate') } else { @('Candidate','Baseline') }
        foreach ($variant in $variants) {
            $dll = if ($variant -eq 'Baseline') { $BaselineDll } else { $CandidateDll }
            $revision = if ($variant -eq 'Baseline') { $BaselineRevision } else { $CandidateRevision }
            $stem = "$variant-$players-$seed-pair$pair"
            $report = Join-Path $OutputRoot "$stem.json"
            $log = Join-Path $OutputRoot "$stem.log"
            if (Test-Path -LiteralPath $report) { throw "Refusing to replace an existing report: $report" }
            $runArgs = @($dll, "--load=$save", '--turns=0', '--post-opening-turns=25', '--warmup=1',
                '--workers=4', '--partition-size=64', "--revision=$revision", "--output=$report")
            if ($Mode -eq 'Timing') { $runArgs += @('--details=0','--metrics=0','--fingerprints=0') }
            elseif ($Mode -eq 'Details') { $runArgs += @('--details=1','--metrics=0','--fingerprints=2') }
            else { $runArgs += @('--details=0','--metrics=1','--fingerprints=0') }
            & $Sdk @runArgs *> $log
            if ($LASTEXITCODE -ne 0) { throw "Audit failed: $stem; see $log" }
            $manifest.runs += [ordered]@{ variant=$variant; players=$players; seed=$seed; pair=$pair
                report=$report; saveSha256=(Get-FileHash -LiteralPath $save -Algorithm SHA256).Hash
                arguments=$runArgs }
            $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $OutputRoot 'manifest.json') -Encoding UTF8
            Write-Output "Completed $Mode $stem"
        }
    }
}
