# Physical investment-payback sweep

This tool runs matched cultivation, husbandry, and fishing investment pulses
through the production C# simulator. It does not contain a Python economic
model. `Program.cs` compiles the files in `Assets/_Anthropocene/Simulation`
directly; `Plot.py` only groups and plots the trajectories and discount
integrals emitted by C#.

## Full reproducible run

From the repository root:

```bash
bash Tools/InvestmentPayback/Run-Sweep.sh \
  TestResults/InvestmentPayback --verify
```

The default experiment uses three disclosed exploratory seeds (`61169`,
`74843`, `91781`), an `L9(3^4)` matched-state design, 27 initial states, ten
arms per state, and 40 twenty-year decisions (800 years). It normally takes
about 75 seconds in a Release build on the audit host.

A bounded smoke run is:

```bash
bash Tools/InvestmentPayback/Run-Sweep.sh \
  /tmp/investment-payback-smoke --quick --turns 6 --verify
```

`DOTNET_BIN` may select another .NET 8 executable. The runner otherwise uses
the repository-local `.tools/dotnet/dotnet`.

## Design

For each generated geography, the harness selects low/medium/high quantiles of
joint local terrestrial and aquatic renewal among legal coastal settlement
sites. It constructs matched mature mixed-subsistence camps using only
production state types and saves each initial state once. An `L9(3^4)` array
crosses three levels of opening field/fishery capital and herd stock,
population, ecological support, and food reserves without requiring all 81
factor combinations. Three independent generation seeds replicate the nine
rows.

Every state receives an unchanged automatic `DemandControl` baseline and nine
first-turn treatments: 5%, 10%, or 20% of opening residents are reassigned
from available gatherers to one domain. Field workers are marked for expansion;
fishers pass through ordinary harvest/maintenance/build allocation; herders
raise management plus the permitted breeding/retention setting toward full
intensity. All later decisions use the unmodified automatic policy. Physical
constraints can clip a requested pulse, and both requested and accepted labor
are recorded.

The treatments do not edit configuration values, ecological equations,
demographic equations, fission, or reward code. The only configuration
differences from `OpeningConfig.json` are a bounded `128 x 64` generated map and
one opening player; their derived configuration hash is written to the
manifest. The synthetic state-bank assumptions (technology, equipment,
housing, and reserve initialization) are also recorded.

## Outputs

- `initial_states.csv`: factors, physical assets, support measures, exact
  fingerprints, and configuration hash.
- `trajectories.csv`: absolute per-turn population, endpoint QoL, `N_prior Q`,
  production, labor, build/maintenance work, capital, herds, ecology, reserves,
  and fission.
- `paired_trajectories.csv`: treatment-minus-baseline physical deltas and C#
  cumulative welfare paths for undiscounted, 100-, 200-, and 400-year e-folds.
- `discount_summary.csv`: final values, worst drawdown, durable welfare
  break-even and positive-onset time, sign classification, and physical totals.
- `manifest.json`: source/configuration identity, dimensions, assumptions, and
  runtime.
- `verification.txt`: scalar versus reversed-parallel replay result.
- `plots/`: aggregate tables plus SVG plots. Ranking comparisons are made only
  among treatments with positive 800-year undiscounted welfare.

The finite-horizon discount integral uses each simulator turn's piecewise
constant realized welfare rate `Delta(N_prior * Q_end)`:

```text
weight(t0,t1;H) = H * (exp(-t0/H) - exp(-t1/H))
```

`H = infinity` reduces to `t1 - t0`. This is exact for the simulator's current
per-turn reward representation; it is not a claim that within-turn population
or QoL histories are available.
