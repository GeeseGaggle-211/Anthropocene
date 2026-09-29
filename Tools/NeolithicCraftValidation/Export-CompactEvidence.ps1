param(
    [string]$Candidate = 'TestResults/NeolithicCraftValidation/FinalCandidate',
    [string]$Baseline = 'TestResults/NeolithicCraftValidation/FinalBaseline',
    [string]$Comparison = 'TestResults/NeolithicCraftValidation/FinalComparison'
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
function Resolve-RepoPath([string]$Path) { Join-Path $repoRoot $Path }
$candidateDir = Resolve-RepoPath $Candidate
$baselineDir = Resolve-RepoPath $Baseline
$comparisonDir = Resolve-RepoPath $Comparison
$resultsDir = Join-Path $PSScriptRoot 'Results'
New-Item -ItemType Directory -Force -Path $resultsDir | Out-Null

$summaryFiles = @{
    'scenario-summary.csv' = 'Issue33ScenarioSummary.csv'
    'coverage-summary.csv' = 'Issue33CoverageSummary.csv'
    'replay-summary.csv' = 'Issue33ReplaySummary.csv'
}
foreach ($name in $summaryFiles.Keys) {
    Copy-Item -LiteralPath (Join-Path $comparisonDir $name) -Destination (Join-Path $resultsDir $summaryFiles[$name]) -Force
}
Copy-Item -LiteralPath (Join-Path $candidateDir 'manifest.json') -Destination (Join-Path $resultsDir 'Issue33CandidateManifest.json') -Force
Copy-Item -LiteralPath (Join-Path $baselineDir 'manifest.json') -Destination (Join-Path $resultsDir 'Issue33BaselineManifest.json') -Force

$witnessColumns = @('good','control','role','source_ids','source_amounts','food_provenance','raw_stock_open','material_receipts','planned_output','actual_output','worker_years','stone_used','clay_used','wood_used','plantfood_used','animalfood_used','animalfabric_used','housingmaterial_used','handtools_consumed','tools_consumed','negative_missing_input','negative_output','replacement_period1','replacement_period2','recurrence_scope','status')
Import-Csv (Join-Path $candidateDir 'recipe-witnesses.csv') |
    Select-Object $witnessColumns |
    Export-Csv (Join-Path $resultsDir 'Issue33RecipeWitnesses.csv') -NoTypeInformation -Encoding utf8

$pairs = Import-Csv (Join-Path $comparisonDir 'paired-comparison.csv')
$pairs | Group-Object cohort_class, branch, pair_status | ForEach-Object {
    $first = $_.Group[0]
    [pscustomobject]@{
        cohort_class = $first.cohort_class
        branch = $first.branch
        pair_status = $first.pair_status
        windows = $_.Count
        distinct_unlock_saves = @($_.Group.candidate_unlock_save_sha256 | Sort-Object -Unique).Count
        config_file_hashes_match = (@($_.Group | Where-Object { $_.candidate_config_file_hash -ne $_.baseline_config_file_hash }).Count -eq 0)
        resource_config_hashes_match = (@($_.Group | Where-Object { $_.candidate_resource_config_hash -ne $_.baseline_resource_config_hash }).Count -eq 0)
    }
} | Sort-Object cohort_class, branch | Export-Csv (Join-Path $resultsDir 'Issue33PairedStatusCounts.csv') -NoTypeInformation -Encoding utf8

$specialists = Import-Csv (Join-Path $comparisonDir 'specialist-summary.csv') |
    Where-Object { $_.source -eq 'candidate' -and $_.cohort_class -eq 'natural-activity-discovery' -and $_.branch -eq 'policy' }
$specialists | Group-Object tradition, specialist_role, workstream | ForEach-Object {
    $group = $_.Group
    $productive = @($group | Where-Object { [int]$_.productive_turns -gt 0 })
    $turns = @($group | ForEach-Object { [int]$_.productive_turns } | Sort-Object)
    $median = if ($turns.Count) { if ($turns.Count % 2) { $turns[[int][math]::Floor($turns.Count / 2)] } else { ($turns[$turns.Count / 2 - 1] + $turns[$turns.Count / 2]) / 2.0 } } else { 0 }
    [pscustomobject]@{
        tradition = $group[0].tradition
        specialist_role = $group[0].specialist_role
        workstream = $group[0].workstream
        measured_windows = $group.Count
        windows_with_productive_output = $productive.Count
        total_productive_turns = ($turns | Measure-Object -Sum).Sum
        median_productive_turns = $median
        maximum_continuous_productive_streak = ($group | ForEach-Object { [int]$_.longest_productive_streak } | Measure-Object -Maximum).Maximum
    }
} | Sort-Object tradition | Export-Csv (Join-Path $resultsDir 'Issue33SpecialistEmergence.csv') -NoTypeInformation -Encoding utf8

$constraints = Import-Csv (Join-Path $comparisonDir 'unresolved-constraints.csv') |
    Where-Object { $_.source -eq 'candidate' -and $_.cohort_class -eq 'natural-activity-discovery' -and $_.branch -eq 'policy' }
$constraints | Group-Object route, good, constraint, unresolved | ForEach-Object {
    $first = $_.Group[0]
    [pscustomobject]@{
        route = $first.route
        good = $first.good
        constraint = $first.constraint
        unresolved = $first.unresolved
        grouped_rows = $_.Count
        turn_occurrences = ($_.Group | ForEach-Object { [int]$_.turn_occurrences } | Measure-Object -Sum).Sum
    }
} | Sort-Object route, good, constraint | Export-Csv (Join-Path $resultsDir 'Issue33NaturalPolicyConstraints.csv') -NoTypeInformation -Encoding utf8

$candidateManifest = Get-Content (Join-Path $candidateDir 'manifest.json') -Raw | ConvertFrom-Json
$baselineManifest = Get-Content (Join-Path $baselineDir 'manifest.json') -Raw | ConvertFrom-Json
[pscustomobject]@{
    candidate_revision = $candidateManifest.simulationRevision
    candidate_source_sha256 = $candidateManifest.sourceHash
    candidate_runner_sha256 = $candidateManifest.runnerHash
    candidate_config_hash = $candidateManifest.configHash
    candidate_config_file_sha256 = $candidateManifest.configFileHash
    candidate_resource_config_sha256 = $candidateManifest.resourceConfigHash
    candidate_diagnostics = $candidateManifest.craftWorkDiagnostics
    baseline_revision = $baselineManifest.simulationRevision
    baseline_source_sha256 = $baselineManifest.sourceHash
    baseline_runner_sha256 = $baselineManifest.runnerHash
    baseline_config_hash = $baselineManifest.configHash
    baseline_config_file_sha256 = $baselineManifest.configFileHash
    baseline_resource_config_sha256 = $baselineManifest.resourceConfigHash
    baseline_diagnostics = $baselineManifest.craftWorkDiagnostics
    unlock_checkpoints = $candidateManifest.unlocks
    candidate_windows = [int]($candidateManifest.trajectoryRows / 25)
    baseline_windows = [int]($baselineManifest.trajectoryRows / 25)
    candidate_trajectory_rows = $candidateManifest.trajectoryRows
    baseline_trajectory_rows = $baselineManifest.trajectoryRows
    post_unlock_turns = $candidateManifest.postUnlockTurns
    same_config_file_hash = ($candidateManifest.configFileHash -eq $baselineManifest.configFileHash)
    same_resource_config_hash = ($candidateManifest.resourceConfigHash -eq $baselineManifest.resourceConfigHash)
    savecodec_config_hashes_match = ($candidateManifest.configHash -eq $baselineManifest.configHash)
} | Export-Csv (Join-Path $resultsDir 'Issue33PairingProvenance.csv') -NoTypeInformation -Encoding utf8

Write-Host "Wrote compact Issue #33 evidence to $resultsDir"
