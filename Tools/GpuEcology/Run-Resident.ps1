param(
    [ValidateSet('profile','validate','trajectory')][string]$Mode='trajectory',
    [ValidateSet('off','cpu-cache','gpu-split','gpu-fused','gpu-coeff')][string]$Backend='off',
    [string]$RunId='1'
)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$sdk='C:/Program Files/Unity/Hub/Editor/6000.5.10f1/Editor/Data/DotNetSdk/dotnet.exe'
$env:DOTNET_GCHeapHardLimit='0x180000000'
$env:DOTNET_TieredCompilation='1'
$out=Join-Path $PSScriptRoot 'resident-results'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$label=if($Mode -eq 'trajectory'){"$Mode-$Backend-$RunId"}else{$Mode}
$dll=Join-Path $PSScriptRoot 'TurnProbe/bin/Release/net8.0/TurnProbe.dll'
@{mode=$Mode;backend=$Backend;runId=$RunId;assemblySha256=(Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash;os=[Environment]::OSVersion.VersionString;startedUtc=[DateTime]::UtcNow.ToString('o');gcHeapHardLimit=$env:DOTNET_GCHeapHardLimit;tieredCompilation=$env:DOTNET_TieredCompilation} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $out "$label-metadata.json")
$monitor=$null
if(Get-Command nvidia-smi.exe -ErrorAction SilentlyContinue){$monitor=Start-Process -FilePath 'nvidia-smi.exe' -ArgumentList @('--query-gpu=timestamp,utilization.gpu,memory.used,memory.total','--format=csv,nounits','-l','1') -WindowStyle Hidden -RedirectStandardOutput (Join-Path $out "$label-gpu.csv") -RedirectStandardError (Join-Path $out "$label-gpu-error.txt") -PassThru}
Push-Location $repo
try {
    & $sdk $dll --resident-study $Mode $out $Backend $RunId
    if($LASTEXITCODE -ne 0){throw "Resident study failed: $label"}
} finally {Pop-Location;if($monitor -and -not $monitor.HasExited){Stop-Process -Id $monitor.Id}}
