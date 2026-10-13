param(
    [ValidateSet('baseline','device','validate','bench','matched','compact-matched','trajectory','boundary')][string]$Mode = 'device',
    [string]$Backend = 'fused',
    [string]$RunId = '1'
)
$ErrorActionPreference = 'Stop'
$env:DOTNET_GCHeapHardLimit = '0x180000000' # 6 GiB, identical across backends on this host.
$env:DOTNET_TieredCompilation = if ($Mode -eq 'bench') { '0' } else { '1' }
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$sdkCandidates = @(
    (Join-Path $repo '.tools/dotnet/dotnet.exe'),
    'C:/Program Files/Unity/Hub/Editor/6000.5.10f1/Editor/Data/DotNetSdk/dotnet.exe'
)
$sdk = $sdkCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $sdk) { $sdk = (Get-Command dotnet -ErrorAction Stop).Source }
$project = if ($Mode -eq 'baseline') { 'CpuProbe' } else { 'TurnProbe' }
Push-Location $repo
try {
    & $sdk build -c Release "Tools/GpuEcology/$project/$project.csproj"
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    $dll = "Tools/GpuEcology/$project/bin/Release/net8.0/$project.dll"
    $out = Join-Path $repo 'Tools/GpuEcology/fusion-results'
    New-Item -ItemType Directory -Force -Path $out | Out-Null
    $label = if ($Mode -eq 'trajectory') { "$Mode-$Backend-$RunId" } else { $Mode }
    @{
        mode=$Mode; backend=$Backend; runId=$RunId;
        assemblySha256=(Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash;
        gcHeapHardLimit=$env:DOTNET_GCHeapHardLimit; tieredCompilation=$env:DOTNET_TieredCompilation;
        os=[System.Environment]::OSVersion.VersionString; startedUtc=[DateTime]::UtcNow.ToString('o')
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $out "$label-run-metadata.json")
    $monitor = $null
    if (Get-Command nvidia-smi.exe -ErrorAction SilentlyContinue) {
        $monitor = Start-Process -FilePath 'nvidia-smi.exe' -ArgumentList @('--query-gpu=timestamp,utilization.gpu,memory.used,memory.total','--format=csv,nounits','-l','1') -WindowStyle Hidden -RedirectStandardOutput (Join-Path $out "$label-gpu-samples.csv") -RedirectStandardError (Join-Path $out "$label-gpu-sampling-error.txt") -PassThru
    }
    try {
        if ($Mode -eq 'baseline') { & $sdk $dll baseline Tools/GpuEcology/fusion-results }
        elseif ($Mode -eq 'trajectory') { & $sdk $dll --fusion-study trajectory Tools/GpuEcology/fusion-results $Backend $RunId }
        else { & $sdk $dll --fusion-study $Mode Tools/GpuEcology/fusion-results }
        if ($LASTEXITCODE -ne 0) { throw "Experiment failed: $Mode" }
    } finally {
        if ($monitor -and -not $monitor.HasExited) { Stop-Process -Id $monitor.Id }
    }
} finally { Pop-Location }
