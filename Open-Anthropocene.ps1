param(
    [string]$UnityEditorPath = 'C:\Program Files\Unity\Hub\Editor\6000.5.10f1\Editor\Unity.exe'
)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot

if (-not (Test-Path -LiteralPath $UnityEditorPath -PathType Leaf)) {
    throw "Unity Editor was not found at: $UnityEditorPath"
}

Push-Location -LiteralPath $projectRoot
try {
    git pull --ff-only
    if ($LASTEXITCODE -ne 0) {
        throw 'Git pull failed. Resolve the repository state before opening Unity.'
    }

    Start-Process -FilePath $UnityEditorPath -ArgumentList @('-projectPath', $projectRoot) -WorkingDirectory $projectRoot
}
finally {
    Pop-Location
}
