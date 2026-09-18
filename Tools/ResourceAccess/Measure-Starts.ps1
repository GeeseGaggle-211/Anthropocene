param([string]$Output = 'TestResults/TribeResourceAccess', [int]$Maximum, [double]$DeepReserveMultiplier,
    [string]$Worlds = 'Continents:11,Continents:42,Continents:1729,Pangaea:42,Archipelagos:42')
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$sdk = 'C:/Program Files/Unity/Hub/Editor/6000.5.10f1/Editor/Data/DotNetSdk/dotnet.exe'
if (-not (Test-Path -LiteralPath $sdk)) { $sdk = (Get-Command dotnet -ErrorAction Stop).Source }
Push-Location $root
try {
    $sourceCommit = git rev-parse HEAD
    $runArgs = @('run','--project',(Join-Path $PSScriptRoot 'Anthropocene.ResourceAccess.csproj'),'-c','Release','--','--output',$Output,'--worlds',$Worlds,'--source-commit',$sourceCommit)
    if ($PSBoundParameters.ContainsKey('Maximum')) { $runArgs += @('--maximum', "$Maximum") }
    if ($PSBoundParameters.ContainsKey('DeepReserveMultiplier')) { $runArgs += @('--deep-reserve-multiplier', $DeepReserveMultiplier.ToString([Globalization.CultureInfo]::InvariantCulture)) }
    & $sdk @runArgs
    if ($LASTEXITCODE -ne 0) { throw 'Spawn resource audit failed.' }
} finally { Pop-Location }
