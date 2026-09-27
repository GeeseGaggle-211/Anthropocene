# Issues 26, 28, 33 and 35: integration evidence

The simulation candidate is `5e34e7f30de1f80b5ca8a5f4a1a9967227cfa396`, based on
`c519cd4f1d35520bb0788171f8fb3e9c851a6dcf`. Changes remain unmerged on
`codex/issues-26-28-33-35`. Sol High implemented #26; Luna implemented #28,
#33 validation and #35 documentation, with parent review and independent
measurement. Documentation and validation-tool commits after the simulation
candidate do not change the measured simulation source.

## Implemented behavior

- [Issue26Forecast.md](Issue26Forecast.md): pure local forecasts use the same
  analytical kernel as resolution. Field investment values retained boundary
  food; Farmer harvest is limited to terrestrial food. Accepted departure and
  blocked-move forecasts preserve the resolver's respective claim paths.
- [ConfigurationUnits.md](ConfigurationUnits.md): operating reserves and natural
  storage use explicit physical years. Defaults remain equivalent to the old
  twenty-year policy. Save migration distinguishes absent fields from explicit
  zero values and uses the saved reference duration, not actual short-turn `dt`.
- [CurrentSimulationContract.md](CurrentSimulationContract.md): an authority and
  evidence index distinguishes current contracts, calibration, compatibility,
  historical measurements and unresolved design questions. The original
  specification and external historical documents were preserved.
- [NeolithicCraftValidation.md](NeolithicCraftValidation.md): separate controlled
  craft-chain witnesses and naturally discovered, per-society 25-turn cohorts.
  The craft validation is still being completed; this report does not yet claim
  its acceptance checks passed.

No production rates, mastery thresholds, land multiplier, accessibility, or
shipped `R=16` calibration were retuned. The prior-cohort and one-horizon pass
boundaries remain in force. Craft diagnostics are transient and disabled by
default.

## Requested population distributions

These four matched worlds use natural baseline-generated opening checkpoints.
All completed their opening after ten twenty-year turns. Each version then
resolves exactly 25 additional turns (500 years). This common global-opening
boundary is the performance cohort; it is separate from #33's clock starting
at each individual society's discovery.

Population means **linked people per tribe**, including remote hunters. All
initial tribes remain in the cohort, including any extinct ones. None of the
Agriculture/Husbandry tribes below were extinct at the measured endpoint.
Route means first-breakthrough Cultivation or AnimalHusbandry; Fishing tribes
are excluded from these two distributions. Quartiles linearly interpolate at
`(N−1)×p`.

| Tribes / seed | Route | N | p25 | Median | p75 |
| --- | --- | ---: | ---: | ---: | ---: |
| 80 / 11 | Agriculture | 63 | 232 | 595 | 886 |
| 80 / 11 | Husbandry | 15 | 325.5 | 775 | 1,253 |
| 80 / 23 | Agriculture | 56 | 306.25 | 537.5 | 864.25 |
| 80 / 23 | Husbandry | 20 | 997.75 | 1,255.5 | 1,443.25 |
| 80 / 1729 | Agriculture | 55 | 367.5 | 496 | 974 |
| 80 / 1729 | Husbandry | 23 | 674.5 | 936 | 1,333 |
| 320 / 1729 | Agriculture | 243 | 355 | 602 | 923 |
| 320 / 1729 | Husbandry | 68 | 884.5 | 1,159.5 | 1,463.75 |

Baseline and candidate quartiles are both committed in
[populations.csv](../Tools/PerformanceAudit/Results/Issues26-28-33-35/populations.csv).
The detailed-run population at every measured turn matches all three timing
repetitions for the same version/world. This guards against reporting-mode
effects on the requested outcomes.

## Performance regression

Three alternating baseline/candidate pairs per world ran sequentially, for 24
timing processes. Each process used .NET 8.0.31 on Windows 10.0.26200, four
workers, partition size 64 and one small-world warmup. The three 80-tribe worlds
are 1024×512; the 320-tribe benchmark world is 2048×1024. Both versions load
byte-identical saves and retain saved production/resource configuration.

Measured total is AI planning + turn preparation + resolution for all 25 turns.
Generation, save loading, reporting and fingerprints are outside that total.
Timing mode disables optional counters, detailed rows and fingerprints.
Population reports and final fingerprints were collected in eight separate
detailed processes. Builds and other simulation checks were paused during the
timing comparisons.

| Tribes / seed | Baseline mean seconds | Candidate mean seconds | Mean paired time change |
| --- | ---: | ---: | ---: |
| 80 / 11 | 6.944 | 6.664 | −3.85% |
| 80 / 23 | 6.885 | 6.811 | −0.81% |
| 80 / 1729 | 6.005 | 6.123 | +2.36% |
| 320 / 1729 | 36.409 | 37.123 | +3.77% |

The equal-world arithmetic mean of paired candidate/baseline time ratios minus
one is **+0.37%**. The requested **greater-than-10% audit threshold was not
crossed**, so no regression-triggered architecture audit was required. Individual
repetitions vary; these are host measurements, not a claim of a statistically
established speedup or universal performance equivalence.

Total allocated bytes over the 25 turns increased from 3.903 to 4.002 GB,
3.801 to 3.889 GB, 3.645 to 3.743 GB, and 13.939 to 14.334 GB respectively
(decimal GB, means across the three timing repetitions). Allocation totals are
not retained memory. Raw phase times and allocation totals are retained in
[performance.csv](../Tools/PerformanceAudit/Results/Issues26-28-33-35/performance.csv).
[provenance.json](../Tools/PerformanceAudit/Results/Issues26-28-33-35/provenance.json)
records source revisions, executable/config/save hashes, dimensions and final
fingerprints for every world/version.

## Validation and physical-unit isolation

`./Tools/Test-Headless.ps1 -Seeds 0` passed on the simulation candidate. This
includes the fixed ecology, economy, generation, observation, conservation and
save checks; scalar versus 2/4 workers with reversed collections/partitions
across 34 resolutions, including 25 post-opening turns; and mature agricultural
save restoration.

The new focused suites passed **408 #26 assertions** and **54 #28 assertions**.
The #26 fixed-plan ledger forecast and actual Resolve both delivered 72 Food
over seven local tiles. A saturated managed-input case increased gross input
from zero to 145,999.9967366457 without increasing retained Food. One-year and
2,000-year warm queries each allocated 712 bytes and evaluated four ordered
land-recovery profiles. The profile counter excludes opening projection and
coefficient construction; it is not presented as a count of all kernel work.

#28 checks exercise actual legacy SaveCodec metadata, missing/custom/zero
physical fields, repeated reload, inventory preservation, and Prepare→Resolve
at 20 years, 5 years, 1 year and 7 days with resident, connected-hunter and remote
cohorts. Existing short-turn growth-only fixtures now set an explicit zero-year
reserve so they continue to isolate growth rather than implicitly changing the
reserve with `dt`.

An independently built #28-only source fixture continued the genuine 80/11
baseline checkpoint for 25 turns. Maximum absolute difference was **0.0** for
population, food yield, births, deaths, managed animals, pastoral linked people,
and each camp's residents/linked people/Food/PlantFood/AnimalFood/QoL/mean field q.
This is an isolation check of the twenty-year units change, separate from the
combined candidate's intentional #26 behavior changes. The fixture and raw
comparison are preserved under the ignored evidence directory.

The contract checker passes 24 JSON/API values, six test-entry claims and local
document links: `python Tools/Docs/Check-CurrentContract.py`.
Unity 6000.5.10f1 EditMode verification passed **29/29 tests** with no failures
or skips. The editor resolved its entitlement through the currently observed
Hub licensing pipe and exited successfully. The separate Windows player build
also succeeded with freshly compiled Simulation/Runtime assemblies. Its first
Neolithic smoke attempt passed queued-order purity, economy resolution and an
exact save/load, then failed the automation-countdown assertion. This failure
is under investigation; player verification and final craft-chain/cohort
evidence remain pending at this report checkpoint.

## Reproduction and limits

[ForecastUnitsREADME.md](../Tools/PerformanceAudit/ForecastUnitsREADME.md)
provides archive/build/checkpoint instructions and the paired runner commands.
The summary command used was:

```powershell
python Tools/PerformanceAudit/Summarize-ForecastUnits.py `
  TestResults/Issues26-28-33-35/FinalTiming `
  TestResults/Issues26-28-33-35/FinalDetails `
  Tools/PerformanceAudit/Results/Issues26-28-33-35
```

Heavy source fixtures, saves, build outputs, logs and full per-turn reports stay
under ignored `TestResults/Issues26-28-33-35`. Timing and detailed manifests
there preserve every exact command and input hash. No user save slot was used.

The forecast reports boundary overflow, closing capacity, gross forcing and
net retained recovery separately. The kernel does not currently expose an exact
cumulative rejected-gross-flux integral; the gross-to-net gap includes natural
turnover and accessibility reclassification and must not be labeled pure
clipping. This limitation is explicit in the API and #26 report. Results here
do not validate deferred Elder strategy, subsequent-era mechanics or RL training.
