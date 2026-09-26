# Soft cultivation barrier: rejected food calibration

September 26, 2026. This audit uses the turn-horizon resolver on
`features/neolithic-agricultural` at `8ddd4e26e191d9abbbd02145e73fd787e21b3a72`.
It explains why the subsequent `m=63`, uniform-access `s=.39` experiment
must **not** become the production profile. The experiment accidentally added
a hard field-intensity limit at `q=.4`; that limit has been removed. The
production defaults on this branch are restored to generator 13,
`LandBiomassMultiplier=30`, and natural land shares `.25/.10/.05/.08`.
The intended targets remain approximately 100 people in a long-lived
uncultivated camp and 600 in an agricultural village, with Neolithic
intensity encountering a *soft* effort barrier near `q=.4`.

## What the current soft rule actually does

The authoritative field state is cultivation capital `E` on each land tile.
At turn boundaries, intensity is `q = min(1, 0.4 sqrt(E))`. Worker investment
adds `E` at a fixed cost per unit; maintenance grows with `E`. This is a
convex, **quadratic** effort curve in `q`, not an exponential threshold:

| Field `q` | Capital `E` needed | Capital relative to `q=.4` |
| ---: | ---: | ---: |
| .4 | 1 | 1x |
| .6 | 2.25 | 2.25x |
| .8 | 4 | 4x |
| 1 | 6.25 | 6.25x |

`PrimitiveAgricultureSupportedIntensity=.4` currently enters the Farm Tool
efficiency ratio; it is not an investment threshold in the field allocator
or the economic planner. The only physical bound in those paths is `q=1`.
The parent `m=30,s=1` resolver already reaches mean `q=.44` on the open
fixture and `.55` on woodland by turn 120. Thus the expected strong soft
barrier near `.4` is **not implemented by the current equation**. The
withdrawn hard cap hid that discrepancy instead of resolving it. This
finding calls for a separate cultivation-effort design decision; this audit
does not change that mechanic.

## Why the food-only candidate failed

Land claims debit opening human food `G`, nested inside total biomass `B`.
Only after claims does the tile recover for the next turn. Humans take only
`G`; grazers may take all `B`, preferring `B-G`. These authoritative stocks,
the prior-cohort food priority, four climate profiles, and the one-horizon
tile integration are unchanged here. The cost of the sweep follows three
active radius-one camp footprints and 120 turn boundaries; it introduces no
authoritative work proportional to seasons within a turn. The same
stock/access equations would apply to mature worlds and RL rollouts, so a
calibration that causes runaway villages cannot be accepted from an isolated
population target alone.

Generation scales natural `G` with `m`. It derives total `B` using the
natural accessible fraction, so lowering every land fraction by one uniform
factor `s` enlarges the biomass that cultivation can make accessible without
lowering generated `q=0` food. This preserves the nested pools and `G=B` at
`q=1`, but it does not stop the planner investing beyond `.4`.

The paired isolated runs use 128×64, eight-player generated maps, then retain
one 24-person camp at each pinned seven-land-tile site. Wild herds and
ecology remain live. The no-breakthrough control changes only technology
thresholds. Values below are measured from `turns.csv`; `q` is the mean
across the seven footprint tiles, while maximum `q` is the highest one tile.

| Profile | No-breakthrough median population T120 | Agricultural median T40 | Agricultural median T120 | Median mean `q` T120 | Highest tile `q` T120 |
| --- | ---: | ---: | ---: | ---: | ---: |
| Parent `m=30,s=1` | — | 101 | 148 | .44 | .72 |
| Candidate `m=63,s=.39` | 99 | 1,002 | 556 | .88 | 1.00 |
| Larger `m=80,s=.3071` | 126 | 1,965 | 608 | .97 | 1.00 |
| Larger `m=100,s=.2457` | 157 | 3,696 | 942 | 1.00 | 1.00 |

The `m=80` row reaches roughly 600 at turn 120 only *after* a much larger
peak. It misses the 100-person uncultivated target and the intended gentle
approach to village capacity. Holding `m*s` roughly constant does not hold
Paleolithic population constant: `m` raises opening `G`, while `s` mainly
changes the hidden part of `B`.

For the originally selected `m=63,s=.39`, the site trajectories expose the
overshoot directly:

| Site | People T40 | Mean `q` T40 | People T120 | Mean `q` T120 | Maximum `q` T120 |
| --- | ---: | ---: | ---: | ---: | ---: |
| Fertile open | 1,002 | .63 | 509 | .88 | 1.00 |
| Medium woodland | 1,484 | .73 | 704 | 1.00 | 1.00 |
| Dry marginal | 543 | .41 | 556 | .84 | .98 |

All three camps met 100% of food need at turn 40, yet the fertile camp still
assigned 11,160 expansion worker-years. The planner can pursue its future
food target when projected demand exceeds supply; neither it nor the field
allocator contains a soft `.4` threshold strong enough to halt this
feedback. By turn 80, the dry camp met only 71.5% of food need. These are
real population and field responses, not a QoL calibration issue.

At `m=63`, varying only the **uniform** share factor did not solve both
targets in the tested range:

| Uniform `s` | Agricultural median people T120 | Median mean `q` T120 | Highest tile `q` T120 |
| ---: | ---: | ---: | ---: |
| .30 | 623 | .93 | 1.00 |
| .40 | 549 | .86 | 1.00 |
| .60 | 431 | .76 | 1.00 |
| .80 | 355 | .71 | .98 |
| 1.00 | 292 | .65 | .98 |

This is a sampled range, not a mathematical proof that no food pair can
ever work. It establishes that the selected pair and the tested nearby
directions fail the user's `q` and population-trajectory requirements.
The former hard-cap result of 604 median people and the three full Standard
worlds were measured with the unintended cap and are invalid for choosing
an uncapped food profile. The same applies to that candidate's timing pair.
No replacement profile is accepted, and there is no save-version change.

The follow-up opt-in exponential effort curve and its full-world sweeps are
recorded in [CultivationSoftEffortSweep.md](CultivationSoftEffortSweep.md).
The production defaults remain unchanged pending review.

After restoring the soft rule and generator-13 defaults, the focused
`./Tools/Test-Headless.ps1 -Seeds 0` suite passed, including scalar/worker
replay, cultivation allocation, ecology conservation, and save continuation.
A fresh Release rerun of `m=63,s=.39` reproduced the recorded populations,
mean `q`, and maximum `q` at turns 40, 80, and 120 exactly.

## Reproduce the decisive measurements

Run from the Windows repository root with the current soft resolver. The
sweep writes ignored CSVs under `TestResults`. Use a sequential Release run;
do not compare its timing with a different binary or configuration.

```powershell
$ErrorActionPreference = 'Stop'
$sdk = @(
    './.tools/dotnet/dotnet.exe',
    'C:/Program Files/Unity/Hub/Editor/6000.5.10f1/Editor/Data/DotNetSdk/dotnet.exe'
) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $sdk) { $sdk = (Get-Command dotnet -ErrorAction Stop).Source }
& $sdk build Tools/LandFoodPopulationSweep/Anthropocene.LandFoodPopulationSweep.csproj -c Release
$dll = 'Tools/LandFoodPopulationSweep/bin/Release/net8.0/Anthropocene.LandFoodPopulationSweep.dll'
$base = Get-Content Assets/_Anthropocene/Resources/OpeningConfig.json -Raw | ConvertFrom-Json
$cases = @(
    @{ Name = 'parent'; M = 30; S = 1; Paleo = $false },
    @{ Name = 'm63-s039'; M = 63; S = .39; Paleo = $false },
    @{ Name = 'm63-s039-paleo'; M = 63; S = .39; Paleo = $true },
    @{ Name = 'm80-s03071'; M = 80; S = .307125; Paleo = $false },
    @{ Name = 'm80-s03071-paleo'; M = 80; S = .307125; Paleo = $true }
)
foreach ($case in $cases) {
    $root = Join-Path 'TestResults/SoftBarrierAudit/config' $case.Name
    $resources = Join-Path $root 'Assets/_Anthropocene/Resources'
    New-Item -ItemType Directory -Path $resources -Force | Out-Null
    Copy-Item Assets/_Anthropocene/Resources/ResourceGenerationConfig.json $resources
    $cfg = $base | ConvertTo-Json -Depth 100 | ConvertFrom-Json
    $cfg.LandBiomassMultiplier = $case.M
    $cfg.RichGatherableFraction = .25 * $case.S
    $cfg.GrasslandGatherableFraction = .10 * $case.S
    $cfg.AridGatherableFraction = .05 * $case.S
    $cfg.TundraGatherableFraction = .08 * $case.S
    if ($case.Paleo) {
        $cfg.ExperienceThreshold = 1000000000
        $cfg.MajorityEligibilityYears = 1000000
        $cfg.ForcedTransitionYears = 1000001
    }
    $path = Join-Path (Resolve-Path $resources).Path 'OpeningConfig.json'
    [System.IO.File]::WriteAllText($path, ($cfg | ConvertTo-Json -Depth 100), [System.Text.UTF8Encoding]::new($false))
    & $sdk $dll --repo $root --out (Join-Path 'TestResults/SoftBarrierAudit/runs' $case.Name) --populations 24 --live-turns 120 --controlled-turns 1
    if ($LASTEXITCODE -ne 0) { throw "Sweep failed: $($case.Name)" }
}
./Tools/Test-Headless.ps1 -Seeds 0
```

The `live` rows in each `turns.csv` give population, mean and maximum `q`,
food satisfaction, worker allocation, and opening/ending `G` and `B` at
every turn. A subsequent effort-curve change must be benchmarked at 80 and
320 tribes, test deterministic scalar/worker replay and save continuation,
and rerun the isolated and three Standard-world balance sweeps before it
can be adopted. The accepted prior-cohort and harvest-first contracts remain
the behavioral baseline.
