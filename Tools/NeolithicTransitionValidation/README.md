# Paleo to Agriculture transition validation

`Anthropocene.NeolithicTransitionValidation.csproj` runs a deterministic,
measurement-only transition matrix against the current simulation. It does not
modify production constants, technology rewards, ecology rates, or the economy
planner/resolver.

The default cohort contains five explicitly classified viable generated sites:
productive river, open fertile land, medium woodland, high forest, and dry
marginal land. Because generated camp placement strongly favours some habitats,
the five contrast cohorts place an otherwise unmodified generated Paleo camp on
the selected viable generated tile. The original placed camps are also all
preflighted and one live Cultivation outcome is retained as a separate control.
`selection.csv` records every shortlisted generated-site result and every
original placed-camp result, including non-Cultivation outcomes.

The mobile normal, reserve, scarcity, and placed-camp cases use the existing
`HeuristicTribePolicy`, its filtered observation, and its regular Economy order
after Cultivation. Wild herds remain active. The fixed-site complements use an
explicit player-style NoOp Paleo hold (so movement cannot replace the requested
habitat), then the same `EconomicIntents.Automatic` Economy control after the
ordinary live Cultivation discovery. They still earn experience and discover
Agriculture through the normal rules.
Agriculture is never granted: its normal Mastery/Maturity/Forced rule and the
experience state are recorded. Cultivation outcomes run for exactly 25 further
resolutions; other outcomes are retained through the 37-resolution discovery
horizon as censored cases.

The five normal habitat cases start at their generated 24-person population.
Three mobile cases start at 1.2, 1.4, and 1.5 times a measured local Paleo
support estimate. That estimate is a q=0, isolated, hold-order clone with only
demography/progression disabled for the calculation: eight generations, no
deaths, full food satisfaction in the last three generations, and final food at
least the one-generation starting reserve. The emitted support table proves the
P/P+1 bracket. The harness also measures support at the actual live transition
tile and reports the realised population/support ratio.

The two reserve cases use 0.5 and 2.0 of the generated one-generation reserve.
Clay and Stone scarcity set every known reachable source ledger to zero at the
live Cultivation boundary. Wood uses a separately labelled local-source-absence
fixture: all local surface Wood and Wood components of bodies visible in the
transition footprint are removed, because merely zeroing a recoverable Wood
ledger would not remain scarce for a full generation. Each trace exposes the
affected IDs, next-turn availability, and selected-material production debit.

The fixed-site open, woodland, and dry controls complement the mobile rows when
the normal heuristic relocates. Three further fixed-site settlement-stress rows
make the requested 1.2/1.4/1.5 local-support ratios observable at the actual
Agriculture boundary. After recording the breakthrough generation, they set
only resident population and a one-generation PlantFood reserve once; technology,
experience, q/E, ecology, materials, goods, and constants remain live. The
JSONL `controlledSettlementInitialization` event records before/after people
and food provenance. It is a fixture event, excluded from demographic flows;
all later generations use the normal conservation ledger.

Generated CSV/JSONL evidence belongs under `TestResults` and is intentionally
ignored. The committed validation report transcribes its matrix and explains
the particular generated selection and any observed failures.

Run from the repository root:

```powershell
& ./.tools/dotnet/dotnet.exe build Tools/NeolithicTransitionValidation/Anthropocene.NeolithicTransitionValidation.csproj
& ./.tools/dotnet/dotnet.exe ./Tools/NeolithicTransitionValidation/bin/Debug/net8.0/Anthropocene.NeolithicTransitionValidation.dll --repo . --out TestResults/NeolithicTransitionValidation
```

`--fixed-pilot` runs only the six fixed controls; its purpose is a focused
25-post-Cultivation measurement and it deliberately does not claim a replay
comparison. `--case productive-river-normal,scarce-wood` runs a reproducible
subset; full subsets automatically retain the river baseline for the required
scalar/parallel/save continuation replay. `Summarize.ps1` converts a completed
trace into compact phase and specialist-emergence CSVs for the report.
