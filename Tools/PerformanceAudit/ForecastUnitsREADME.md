# Four-world forecast / physical-units comparison

`Run-ForecastUnitsPairs.ps1` compares the same saved opening state under two
simulation builds. It measures 25 subsequent twenty-year turns, including AI,
preparation and resolution. Generation, loading, fingerprinting and report
serialization are outside the measured turn total. This is a matched
post-opening comparison, not a new-world-to-endpoint timing.

The fixed cases are Standard 1024×512 / 80 tribes with seeds 11, 23 and 1729,
and benchmark 2048×1024 / 320 tribes with seed 1729. Production configuration
and resource configuration generate the checkpoints naturally. Each run uses
four workers, partition size 64 and one small-world warmup. Timing mode disables
detailed diagnostics and fingerprints. Three pairs alternate execution order;
all processes run sequentially. Do not run Unity, other tests or benchmark jobs
concurrently.

## Reproduce

Run from the authoritative Windows checkout. Preserve the source revision in
each report. The baseline is `c519cd4f1d35520bb0788171f8fb3e9c851a6dcf` and the
simulation candidate is `5e34e7f30de1f80b5ca8a5f4a1a9967227cfa396`. Later commits
containing only documentation or measurement tools do not change that candidate.

1. Archive the baseline's `Assets/_Anthropocene/Simulation` and the two resource
   JSON files into an ignored directory using `git archive`, then expand it.
   This is a source fixture, not a second authoritative checkout.
2. Build `Anthropocene.PerformanceAudit.csproj` twice with the same current
   harness and .NET SDK, `-c Release -t:Rebuild -p:UseSharedCompilation=false`.
   For the baseline set `-p:SimulationSource=<absolute archived simulation path>`;
   for the candidate use the candidate simulation path. Give each a separate
   `-o` directory.
3. From the archived baseline root, run the baseline DLL once for each case:
   `--players=<count> --seed=<seed> --shape=Continents --production-config=1
   --turns=12 --post-opening-turns=0 --warmup=1 --details=0 --metrics=0
   --fingerprints=0 --revision=<baseline SHA>
   --save-finished=<checkpoint directory>/start-<count>-<seed>.anthro`.
   The run must actually finish the opening. Never substitute an injected
   technology/field fixture for these checkpoints.
4. Run the paired script with `-Pairs 3 -Mode Timing`, then with `-Pairs 1
   -Mode Details`, using separate new output directories:

```powershell
./Tools/PerformanceAudit/Run-ForecastUnitsPairs.ps1 `
  -BaselineDll <baseline DLL> -CandidateDll <candidate DLL> `
  -CheckpointRoot <checkpoint directory> -OutputRoot <new output directory> `
  -BaselineRevision c519cd4f1d35520bb0788171f8fb3e9c851a6dcf `
  -CandidateRevision 5e34e7f30de1f80b5ca8a5f4a1a9967227cfa396 `
  -Pairs 3 -Mode Timing
python Tools/PerformanceAudit/Summarize-ForecastUnits.py <timing directory> <details directory> <summary directory>
```

Each manifest retains exact arguments, input-save hashes and executable hashes.
The summarizer checks settings, 25-turn counts, revisions, cohort membership,
and per-turn population parity between timing and detailed runs. Details mode
also records a final exact fingerprint. The baseline and candidate fingerprints
are expected to differ when intended behavior or configuration schema changes.

Population is linked people per tribe, including remote hunters and retained
extinct tribes. Quartiles use linear interpolation at `(N−1)×p`, separately for
the first-breakthrough Cultivation and AnimalHusbandry routes. Fishing tribes
are excluded from those two distributions, not silently reclassified.

For the performance threshold, compute each pair's candidate/baseline time
ratio minus one, average pairs within each world, then average the four worlds
equally. A result greater than +10% requires a phase/work audit. Counters mode
can collect that evidence separately; its overhead must not enter timing mode.
Issue #33's per-society discovery windows are a separate cohort and are not
pooled with this global-opening performance comparison.
