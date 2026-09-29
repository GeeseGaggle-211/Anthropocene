param([int]$Seeds = 30, [switch]$Standard, [switch]$Geography, [switch]$Audit, [switch]$BiomePaths, [switch]$NomadAudit, [switch]$NomadStandard, [int]$ScalePlayers = 0)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$sdkCandidates = @(
    (Join-Path $projectRoot '.tools/dotnet/dotnet.exe'),
    'C:/Program Files/Unity/Hub/Editor/6000.5.10f1/Editor/Data/DotNetSdk/dotnet.exe'
)
$dotnetPath = $sdkCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $dotnetPath) { $dotnetPath = (Get-Command dotnet -ErrorAction Stop).Source }
$runArgs = @('run', '-c', 'Release', '--project', (Join-Path $projectRoot 'Tools/Headless/Anthropocene.Headless.csproj'), '--', "$Seeds")
if ($Standard) { $runArgs += '--standard' }
if ($Geography) { $runArgs += '--geography' }
if ($Audit) { $runArgs += '--audit3' }
if ($BiomePaths) { $runArgs += '--biome-paths' }
if ($NomadAudit) { $runArgs += '--nomad-audit' }
if ($NomadStandard) { $runArgs += '--nomad-standard' }
if ($ScalePlayers -gt 0) { $runArgs += "--scale=$ScalePlayers" }
& $dotnetPath @runArgs
if ($LASTEXITCODE -ne 0) { throw "Headless tests failed with exit code $LASTEXITCODE" }
