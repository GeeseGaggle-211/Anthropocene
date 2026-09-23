# Live husbandry validation

This is the Issue #18 measurement harness. It compiles the actual simulation
sources with .NET 8 and makes no gameplay changes. A compact `128 x 64`,
eight-player generated world is used so the raw ledgers are reproducible and
reviewable. Human rivals are removed from the measured fixture; generated wild
herds and all ordinary ecology remain present.

Run the pilot or full matrix from the repository root:

```powershell
./.tools/dotnet/dotnet.exe build Tools/NeolithicHusbandryValidation/Anthropocene.NeolithicHusbandryValidation.csproj -p:OutputPath=bin/issue18/ -p:BaseIntermediateOutputPath=obj/issue18/
./.tools/dotnet/dotnet.exe Tools/NeolithicHusbandryValidation/bin/issue18/Anthropocene.NeolithicHusbandryValidation.dll --repo . --seed 18 --out TestResults/Issue20/Issue18-Final
./Tools/NeolithicHusbandryValidation/Summarize.ps1 -InputDirectory TestResults/Issue20/Issue18-Final -OutputDirectory TestResults/Issue20/Issue18-Final/Tables
```

The main scenarios use a controlled 24-resident, 10-antelope managed camp at
the selected generated location. They retain the live `EconomicIntent` control
surface and set each requested breeding intensity directly, so adaptive AI
policy does not overwrite the tested value. The raw trace records phase-start
and submitted occupations; unassigned residents remain ordinary Gatherers, so
the report treats realized gathering worker-equivalents as productive labour
rather than calling residual Gatherers idle labour.

Candidate selection records every eligible seven-land footprint's cheap proxy.
Only positive occupied-tile pasture candidates are shortlisted, in descending
pasture-proxy order and then by distance to the reference human-gather proxy.
Finite eight-generation support probes then choose the closest nonzero
herd/human pair. The distinct nearest-human-proxy site is retained as an
explicit stress scenario even when its herd support is zero. These probes are
finite horizon observations, not carrying-capacity proofs.

Additional disclosed controls are deliberately not normal starts: a fixed
forage/water range with 200 people and enough Herders for 100/200/400 animals;
healthy and scarce duration fixtures; and scalar/reversed-two-worker/save
continuation replay. The fixed-forage capacity and recovery curve is set once,
never replenished per decision. Its measured source receipts, rather than its
nominal setup numbers, are the evidence.

`trajectory.jsonl` is intentionally ignored because it contains complete
per-generation raw food, animal, pressure, ecology and conservation ledgers.
`selection-proxies.json`, `support-probes.json`, `selection.json`, and
`matrix.jsonl` are likewise run outputs. `Summarize.ps1` produces compact CSVs
for review without changing those measurements.
