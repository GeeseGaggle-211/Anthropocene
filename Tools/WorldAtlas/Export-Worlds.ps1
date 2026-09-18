param(
    [string]$Seeds = '1729',
    [ValidateSet('Continents','Archipelagos','Pangaea')][string]$Shape = 'Continents',
    [int]$Players = 80,
    [int]$Width = 1024,
    [int]$Height = 512,
    [string]$Output = 'TestResults/WorldGeneration/Atlas',
    [switch]$GeographyOnly,
    [switch]$Save,
    [string]$SimulationSource,
    [int]$ResourceMaximum
)
$ErrorActionPreference = 'Stop'
$atlasRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$sdk = 'C:/Program Files/Unity/Hub/Editor/6000.5.10f1/Editor/Data/DotNetSdk/dotnet.exe'
if (-not (Test-Path -LiteralPath $sdk)) { $sdk = (Get-Command dotnet -ErrorAction Stop).Source }
$atlasProject = Join-Path $PSScriptRoot 'Anthropocene.WorldAtlas.csproj'
$atlasSource = if ($SimulationSource) { (Resolve-Path -LiteralPath $SimulationSource).Path } else { Join-Path $atlasRoot 'Assets/_Anthropocene/Simulation' }
function Get-SimulationSourceFingerprint([string]$Directory) {
    $atlasSourceNames = [string[]](Get-ChildItem -LiteralPath $Directory -Filter '*.cs' -File | ForEach-Object { $_.Name })
    [Array]::Sort($atlasSourceNames, [StringComparer]::Ordinal)
    $atlasSourceLines = $atlasSourceNames | ForEach-Object { $_ + ':' + (Get-FileHash -LiteralPath (Join-Path $Directory $_) -Algorithm SHA256).Hash }
    $atlasHasher = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($atlasHasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($atlasSourceLines -join "`n"))).Replace('-', '') }
    finally { $atlasHasher.Dispose() }
}
$atlasSourceHash = Get-SimulationSourceFingerprint $atlasSource
$buildArgs = @('build', $atlasProject, '-c', 'Release', '--nologo', '-v', 'quiet')
if ($SimulationSource) { $buildArgs += "-p:SimulationSource=$atlasSource" }
& $sdk @buildArgs
if ($LASTEXITCODE -ne 0) { throw 'World atlas build failed.' }
if ((Get-SimulationSourceFingerprint $atlasSource) -ne $atlasSourceHash) { throw 'Simulation sources changed during compilation. Rerun the export once edits are complete.' }
$atlasArgs = @((Join-Path $PSScriptRoot 'bin/Release/net8.0/Anthropocene.WorldAtlas.dll'), '--seeds', $Seeds, '--shape', $Shape, '--players', "$Players", '--width', "$Width", '--height', "$Height", '--output', $Output)
$atlasArgs += @('--source-sha256', $atlasSourceHash, '--source-description', $(if ($SimulationSource) { 'Preserved simulation source snapshot' } else { 'Current checkout simulation source files' }))
if ($GeographyOnly) { $atlasArgs += '--geography-only' }
if ($Save) { $atlasArgs += '--save' }
if ($PSBoundParameters.ContainsKey('ResourceMaximum')) { $atlasArgs += @('--resource-maximum', "$ResourceMaximum") }
& $sdk @atlasArgs
if ($LASTEXITCODE -ne 0) { throw 'World atlas export failed.' }
