# Construction corrections and three 80-tribe runs — 13 November 2026

The construction corrections are implemented and tested. These are three fresh
normal-AI/Elder playthroughs using the smoothed calendar, with 80 founding tribes
on Standard/Continents 1024 × 512 worlds and seeds 11, 23 and 47. Each runs to the
first authoritative boundary at or beyond 1,600 physical years. No knowledge,
resources, population or workforce were granted after generation. No balance
coefficients, demographic laws or active RL training settings were changed.

## Population

Living tribes are living `Society` IDs. A tribe's population sums all its camps
and hunters; extinct societies are excluded. Fission creates additional tribes.
Quantiles use linear interpolation at `(n − 1) × p` (Type 7).

| Seed | Physical years | Tribes counted | Total population | Median | Maximum | P75 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 11 | 1,605.73 | 3,094 | 2,340,374 | 978 | 1,166 | 1,000 |
| 23 | 1,600.98 | 2,868 | 2,276,649 | 989 | 1,175 | 1,000 |
| 47 | 1,605.73 | 1,328 | 980,038 | 967.5 | 1,176 | 1,000 |

For comparison across the original 80 tribes, the following groups every
descendant society through `ParentSociety`. All 80 founding lineages are included,
with zero for an extinct lineage. This tracks society lineage, not the ancestry
of individual immigrants. Both tables reconcile to the same total population.

| Seed | Physical years | Tribes counted | Total population | Median | Maximum | P75 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 11 | 1,605.73 | 80 | 2,340,374 | 4,000 | 515,123 | 17,048.25 |
| 23 | 1,600.98 | 80 | 2,276,649 | 5,223.5 | 483,736 | 25,885.75 |
| 47 | 1,605.73 | 80 | 980,038 | 2,000 | 312,642 | 8,926.5 |

The epoch is triggered by native opening completion, so independent worlds can
reach different final physical dates. Exact epochs, committed durations and
seasonal exposure are retained; turns were not clipped or replaced with fixed
20-year intervals. This is a cross-seed gameplay report, not a matched-treatment
estimate of irrigation's effect on population.

## What changed

- The generic Tool allowance now contains exactly the paid output, eliminating
  the second allowance that consumed Wood reserved for other production.
- Housing Material throughput uses the same construction-tool multiplier as its
  Builder-time charge. The four-Builder reproduction now completes its segment.
- Canal construction and upkeep materials receive a temporary in-use storage
  allowance, which expires at Close.
- Final canal commitments and production ceilings are capped by the executable
  forecast. Initial grants remain visible separately. Cached drafts are copied
  before this reconciliation, so repeated preparation preserves the draft.

Housing, new canals and canal upkeep are separate jobs with their own priorities
and quantities. They consume one shared Builder pool, generic tool service and
material inventory. Already compiled reservations are not reassigned when the
final forecast reduces a grant; that remaining conservatism is explicit.

## Construction delivery and scale

| Seed | Initial construction grant | Final grant / actual segments | Initial grants reduced | Final-grant shortfalls | Completed canals | Housing/canal overlap turns |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 11 | 119.49 | 109.09 | 12 | 0 | 109 | 84 |
| 23 | 137.46 | 126.09 | 13 | 0 | 126 | 98 |
| 47 | 77.52 | 74 | 4 | 0 | 74 | 61 |

Final construction totals agree with actual receipts within numerical tolerance.
The reduction count includes construction and upkeep; full per-case initial,
final, forecast and actual quantities are retained in the summary JSON. These
reductions are not reported as completed construction. The overlap column counts
camp-turns with both a positive canal construction grant and actual housing
output, demonstrating that the shared Builder pool handles both jobs.

| Seed | Turns | Camps / villages | Canal-work grants (build / upkeep) | Water delivered | Maximum water residual | Runtime seconds | Peak RSS MiB |
| --- | ---: | --- | --- | ---: | ---: | ---: | ---: |
| 11 | 118 | 3,095 / 2,576 | 100 / 1,911 | 3,150,712.29 | 0 | 1,487.9 | 4,016.3 |
| 23 | 120 | 2,868 / 2,588 | 114 / 2,458 | 3,678,131.32 | 0 | 1,839.8 | 3,656.5 |
| 47 | 118 | 1,329 / 1,173 | 68 / 1,809 | 4,177,348.67 | 0 | 737.6 | 2,174.0 |

Timings are concurrent cloud runs, not isolated performance benchmarks. Cost
follows active settlements, construction requests and infrastructure, rather than
population-sized entities or substeps through elapsed years. The report records
all committed population boundaries, final tribe populations, canal material
spending, all Builder-production years (including housing and material making),
upkeep in segment-years, water delivery and mean field soil development.

## Validation and remaining limits

All 69 headless suites pass on the final code, including 283 agricultural,
163 DemandControl, 154 Housing Material recipe, 64 goods-storage and 184 production
order assertions. All 28,787 calendar checks pass. The contract checker passes
92 configuration/API values, 21 test claims and document links. Tests cover
minimum staffing, mixed housing/canal work, material conservation, tight storage,
initial versus final commitments, and repeated cached preparation.

The fixed-calendar compatibility fixture was corrected to retain the committed
duration when disabling smoothing; its old hard-coded shorter duration could
invalidate provisions already carried by hunters. Production calendar rules
were not changed.

The three long runs started before the defensive cached-draft copy was added.
Their harness never supplies a draft. The exact runtime source hashes and the
subsequent three-file cache-only patch are pinned in the manifest. An additional
mature native replay over 80+ physical years yields identical saved bytes and
fingerprints for the runtime direct path, final direct path and final cached
path. The final full headless suite was rerun after that patch.

The previous large-world save metadata limit remains unresolved. This compact
harness retains opening saves and committed-boundary evidence, not final native
saves. It does not establish long-campaign save/load acceptance. Unity interaction
checks remain unperformed because Unity is unavailable. The full five-seed
matched balance matrix has not been rerun; these population results do not choose
new coefficients. The technology profile remains opt-in.

## Evidence and reproduction

- [Summary, calendar durations and grant cases](Experiments/ConstructionFix80-20261113-summary.json)
- [Runtime sources, settings, opening hashes and source amendment](Experiments/ConstructionFix80-20261113-manifest.json)
- [All committed turns](Experiments/ConstructionFix80-20261113-turns.csv)
- [Individual tribe and founding-lineage populations](Experiments/ConstructionFix80-20261113-populations.csv)
- [Validation](Experiments/ConstructionFix80-20261113-validation.json), [controlled reproductions](Experiments/ConstructionFix80-20261113-fixtures.json), and [cache parity](Experiments/ConstructionFix80-20261113-cache-parity.json)
- [Original eleven-case diagnosis](ConstructionGrantAudit.md)

```bash
dotnet build Tools/AgriculturalTechnologySweep/Anthropocene.AgriculturalTechnologySweep.csproj -c Release
# Run once for each seed: 11, 23, 47. For exact seed-11 matching, optionally add
# --opening=TestResults/AgriculturalGameplay80/opening.anthro
dotnet Tools/AgriculturalTechnologySweep/bin/Release/net8.0/Anthropocene.AgriculturalTechnologySweep.dll --population-run --seed=11 --years=1600 --output=TestResults/ConstructionFix80/seed-11
python3 Tools/AgriculturalTechnologySweep/SummarizePopulationRuns.py
dotnet run --project Tools/Headless/Anthropocene.Headless.csproj -c Release -- 0
dotnet run --project Tools/SmoothCalendar/Anthropocene.SmoothCalendar.csproj -c Release
python3 Tools/Docs/Check-CurrentContract.py
```

The source amendment's reverse patch reproduces the exact original runtime
source snapshot. Preserve the original result directory when comparing a new
run. Starting saves and full raw journals remain under
`TestResults/ConstructionFix80/seed-<seed>`.
