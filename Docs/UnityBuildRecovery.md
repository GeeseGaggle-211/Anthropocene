# Rebuilding on this Windows workstation

Read this before retrying a failed Unity build. The September 15, 2026 failure was licensing initialization/IPC, before the build method ran. Repeating batch launches did not fix it. The successful launch used Unity 6000.5.10f1 in graphical mode, hidden, with the running Hub licensing client's named pipe supplied explicitly. Unity then connected to its versioned client, resolved entitlements and built successfully. Unity MCP was not needed; it is not a license repair mechanism.

## 1. Establish the source and project

Work in `C:\Users\suyas\Documents\ChatGPT\Anthropocene`. Check `git status --short --branch`, `git log -3 --oneline` and `ProjectSettings/ProjectVersion.txt`. Preserve local edits and commits. When synchronization is requested, use `git -c maintenance.auto=false -c gc.auto=0 pull --ff-only origin main`; do not reset to upstream or discard a newer local commit to get a clean build.

This repository has previously contained a corrupt local `refs/codex/turn-diffs/checkpoints/...` ref that makes fetch/pull fail with `fatal: bad object`. This is separate from Unity. Do not delete checkpoint refs or run broad prune/repack as part of a rebuild. Use `git ls-remote --exit-code origin refs/heads/main` to read the actual remote hash, then `git cat-file -e <hash>^{commit}` and `git merge-base --is-ancestor <hash> HEAD` to establish whether that exact upstream revision is already present and included. If both succeed, building HEAD includes upstream plus any local commits; report that accurately rather than claiming pull succeeded. If the object is missing or ancestry fails, resolve synchronization before claiming the requested source was built. Never force-push or reset to bypass this check.

Use the installed editor matching `ProjectVersion.txt`, currently `C:\Program Files\Unity\Hub\Editor\6000.5.10f1\Editor\Unity.exe`. Do not build a second checkout such as `C:\Users\suyas\Anthropocene` merely because Unity Hub has it open.

## 2. Inspect processes without exposing credentials

Run process inspection with the host permissions needed by `Get-CimInstance`. Unity Hub editor command lines may contain access tokens. **Never print complete command lines or raw licensing logs.** Extract only project paths, process IDs and named pipes:

```powershell
Get-CimInstance Win32_Process |
  Where-Object { $_.Name -in @('Unity.exe', 'Anthropocene.exe', 'Unity.Licensing.Client.exe') } |
  ForEach-Object {
    $projectMatch = [regex]::Match($_.CommandLine, '(?i)-projectPath\s+(?:"([^"]+)"|(\S+))')
    $pipeMatch = [regex]::Match($_.CommandLine, '(?i)--namedPipe\s+(?:"([^"]+)"|(\S+))')
    [pscustomobject]@{
      Id = $_.ProcessId
      Name = $_.Name
      Executable = $_.ExecutablePath
      Project = if ($projectMatch.Success) { $projectMatch.Groups[1].Value + $projectMatch.Groups[2].Value } else { '' }
      Pipe = if ($pipeMatch.Success) { $pipeMatch.Groups[1].Value + $pipeMatch.Groups[2].Value } else { '' }
    }
  }
```

Never run concurrent editor/batch instances against this checkout. Close its running player before replacing the build. If its editor is already open, use its build menu or close that exact editor cleanly before launching the build. Leave other checkouts and their import workers alone. Record the process ID returned by every build launch. If a prior launch is stuck, verify its project path again before stopping only that process; do not kill all Unity, Hub or licensing processes.

## 3. Launch the proven licensing route

Keep Unity Hub signed in. Inspect the running Hub licensing client using the safe inventory above. On September 15 its `--namedPipe` was `Unity-LicenseClient-suyas`; the failed launches repeatedly tried `LicenseClient-suyas`. Treat these as observed values, not interchangeable names. Use the current observed Hub pipe rather than assuming it is unchanged. If no authenticated client is running, start/sign in through Hub before retrying. Do not copy access tokens into commands, scripts or documentation.

The following is the successful command shape. Substitute the pipe only after inspecting it. Use a fresh log directory for each attempt. Launch the editor hidden with host permissions; omit `-batchmode -nographics` for this recovery route. This does not bypass licensing: require successful entitlement resolution in the log.

```powershell
$projectRoot = 'C:\Users\suyas\Documents\ChatGPT\Anthropocene'
$unityEditor = 'C:\Program Files\Unity\Hub\Editor\6000.5.10f1\Editor\Unity.exe'
$licensePipe = 'Unity-LicenseClient-suyas' # Replace with the observed Hub pipe.
$buildOutput = Join-Path $projectRoot ('TestResults\Rebuild-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $buildOutput | Out-Null
$buildLog = Join-Path $buildOutput 'Build.log'
$buildArgs = '-quit -projectPath "{0}" -licensingIpc "{1}" -executeMethod Anthropocene.Editor.PrototypeBuild.Windows -logFile "{2}"' -f $projectRoot, $licensePipe, $buildLog
$buildProcess = Start-Process -FilePath $unityEditor -ArgumentList $buildArgs -WindowStyle Hidden -PassThru
$buildProcess.Id
```

Wait in bounded intervals (at most 30–60 seconds) and inspect new log output. A normal run advances through entitlement resolution, package import, script compilation and build output. Repeated `Connection to channel ... refused`, `Licensing initialization failed`, or missing `com.unity.editor.headless` indicate licensing trouble; do not keep launching duplicates or delete `Library` for this symptom. A script compilation error is a different failure: inspect the actual error and fix it rather than changing license settings.

The successful September 15 process connected to `LicenseClient-suyas-6000.5.10` after launch and resolved entitlement details. That versioned channel is evidence of success, not a value to blindly hardcode for future editors.

## 4. Prove the build and player work

Require `Build Finished, Result: Success.` and successful editor exit. Check the new managed assemblies under `Builds/Windows/Anthropocene_Data/Managed`; the executable stub's timestamp alone does not establish that gameplay code was rebuilt. Output is `Builds/Windows/Anthropocene.exe`.

For a separate branch build, add `--anthropocene-build-output "<absolute-output-directory>"` to the same Unity build command. The build method writes the executable and its companion files directly there; omitting this option retains `Builds/Windows`. The geology branch uses `Builds/GeologicalResources/Anthropocene.exe`. Inspect the managed assemblies in the chosen directory and run that exact executable for verification.

For a rebuild verification, run the existing `--anthropocene-smoke-output <fresh-absolute-directory>` player route with a separate `-logFile`. The player must be visible for screenshots on this workstation; this is an interactive verification tool, not a hidden background helper. Wait for its `result.txt` and normal exit, inspect the player log for exceptions, and view the actual opening/restored screenshots. The route checks dispatch, stack selection, both symbol sets, supply preview and isolated save/load. It never uses the normal user save slot. Use the preset/atlas routes when the changed subsystem calls for them. Do not rerun unrelated expensive simulation suites for a build-only task.

Inspect `git status` after the editor exits. Unity can rewrite `.meta` whitespace or project settings. Preserve prior work; never restore whole files to HEAD merely to remove noise. For MapArt changes, first prove `git diff --ignore-space-at-eol --exit-code -- Assets/_Anthropocene/Resources/MapArt` succeeds, then remove only newly introduced trailing whitespace. Review any substantive change separately. Keep build products, logs and captures ignored in `TestResults`/`Builds`.

Report the built commit, whether upstream was included and local commits preserved, the build outcome, the actual player checks and the executable location. Explain any remaining synchronization problem separately. Do not describe automated captures as a human usability playtest.

## Recorded successful recovery

September 15: upstream main was `8257614`; local main `9dceda7` included it and the newer balance commit. The explicit Hub-pipe graphical launch built that local main successfully. The visible Standard/Continents/1729/32 smoke run passed dispatch, exact stack selection, both tilesets, supply preview and save/load, finishing at turn 3 with 37 people. All nine captures were present; opening/restored frames were visually inspected, and six transparency probes plus 2,431 differing icon pixels passed. Evidence is ignored under `TestResults/Rebuild-20260915/Build.log` and `Player/`; source and user saves were preserved. This is a verified recovery procedure, not a promise that all future licensing failures have the same cause.
