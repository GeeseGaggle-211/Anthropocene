# Land food calibration through Cultivation

September 26, 2026. Experimental branch `codex/land-food-population-sweep`,
following the harvest-first checkpoint `42cdc56` and bounded-stock audit
`f3238eb`. This is a candidate profile, not approval to replace the accepted
prior-cohort seasonal checkpoint. The matched outcome and timing gates noted
below remain open.

## Model and chosen candidate

At each resource boundary a land tile has total biomass `B` and a nested
human-gatherable stock `G`, with `0 <= G <= B`. Human claims draw only from
opening `G`; grazers prefer `B-G` and may then draw residual `G`. The tile
recovers after receipts are fixed. The four seasonal climate profiles compose
an analytical recovery operator over the full turn. Field capital gives
intensity `q`; rising `q` exposes more of `B` as `G`, and an *operated* field
adds managed recovery proportional to `CultivationRegenFlux * q * B *
BiomassRecoveryPerDay`. At `q=1`, humans can access all `B`. Actual stocks are
clamped to their current condition and field capacities at the endpoint.

The candidate uses one land multiplier **m=30**, a land-only legacy-quarter
recovery fraction **0.03**, `LandSafeUseRatio=0.5`, and the existing
`CultivationRegenFlux=0.65`. Lake and marine renewal keep their existing
settings; the shared legacy `FoodRegeneration=0.4` still supplies lake renewal.
Oasis overrides remain separate. Generated profile version 13 records the new
land rates and placement rule. Loaded worlds retain stored capacities, rates,
stocks, and their older generator version; no save envelope changed.

Placement now screens land with a conservative repeatable stock: deplete
opening `G`, recover once using the same four-profile operator, and credit at
most the original opening `G`. A cached result per land tile keeps generation
work proportional to candidate-local tiles, not `dt / BaseDays`. This prevents
a large one-time opening stock from qualifying a camp whose next turn would
be barren. The previous dry-marginal selected site failed this test and was
replaced by the reported seed-16 site. Selection changes are disclosed in
`TestResults/LandFoodPopulationSweep/M30-LandR003-QuickFinal/selection.csv`.

## What the population sweep shows

Three pinned 128x64/8-tribe generated sites are reduced to one stationary
camp and seven radius-one land tiles. The controlled lane fixes population,
growth, herds and `q=0` for eight 20-year turns. The live lane retains the
normal transition, growth, wild herds, and automatic Cultivation orders for
32 turns. Opening populations are 8, 16, 24, 32, 48, 64, 96, 144 and 192.
The full 54-case [summary](LandFoodNeolithicPopulationSweep.csv), selected
[per-turn trajectory](LandFoodNeolithicTrajectory.csv), and
[zero-claim control](LandFoodCalibratedNoHarvest.csv) are tracked.

Controlled turn 8, with no cultivation:

| Site | Starting people | End `G` | End `B` | `G` / capacity | Food need met |
| --- | ---: | ---: | ---: | ---: | ---: |
| Open fertile, no claims | 0 | 4,873 | 44,251 | 88% | — |
| Open fertile | 24 | 4,531 | 43,908 | 82% | 100% |
| Open fertile | 96 | 3,784 | 43,162 | 68% | 49% |
| Woodland, no claims | 0 | 9,040 | 56,112 | 82% | — |
| Woodland | 24 | 9,037 | 56,112 | 82% | 100% |
| Woodland | 96 | 8,339 | 55,481 | 75% | 100% |
| Dry marginal, no claims | 0 | 3,018 | 37,906 | 67% | — |
| Dry marginal | 24 | 2,526 | 37,412 | 56% | 100% |
| Dry marginal | 96 | 2,393 | 37,258 | 53% | 31% |

This corrects the original **flat-stock** result: at m=15 and shared
regeneration 0.4, open `G` ended at 93% of capacity for both 24 and 96
people, while 96 people met only 33% of need. At m=30 and land regeneration
0.03, a modest open camp meets need and leaves more stock than a larger one.
Woodland nearly refills after modest use, as intended for a fertile habitat.
The dry site's first-turn loss also occurs with zero claims: its generated
opening stock adjusts to seasonal capacity before human damage is involved.

The plateau remains when *both* populations exhaust opening `G`: open 96 and
192 end turn 8 at the same 3,784. A harvest-first tile cannot register more
extraction after it has already given all accessible opening food. The extra
population suffers unmet need and later demographic losses. This is finite
stock saturation, not same-turn recovered food entering receipts.

The coarse-to-fine candidates establish the tradeoff. At m=20/r=0.01, an
open 24-person camp met 68% of need; m=40/r=0.005 left dry 24-person food
satisfaction at 43%. At m=25/r=0.04, the open 24-versus-96 stock separation
was only 86% versus 77% of capacity. The chosen m=30/r=0.03 produced 82%
versus 68% open, 82% versus 75% woodland, and 56% versus 53% dry while all
three 24-person controls met food need. The other candidate raw ledgers are
under ignored `TestResults/LandFoodPopulationSweep/M*-R*/` directories.

## Follow through Neolithic field growth

Live turn 32, on the same three sites. `G(q=0) cap` is the *same ending tile
condition* evaluated without field capital. It isolates the change in
accessibility ceiling; it is not another simulated world. Managed input is
the pre-cap source term during that turn, not food retained after overflow.

| Site | Initial → final people | Mean `q` | End `G` | End `B` | `G(q=0)` cap → actual `G` cap | Food need met |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Open fertile | 24 → 84 | 0.23 | 7,529 | 44,436 | 5,521 → 8,427 | 100% |
| Open fertile | 96 → 85 | 0.23 | 7,373 | 44,436 | 5,522 → 8,258 | 100% |
| Open fertile | 192 → 88 | 0.27 | 8,030 | 44,432 | 5,510 → 8,971 | 100% |
| Woodland | 24 → 185 | 0.32 | 14,275 | 55,927 | 10,854 → 16,672 | 100% |
| Woodland | 96 → 173 | 0.34 | 15,132 | 55,264 | 10,627 → 17,610 | 100% |
| Woodland | 192 → 185 | 0.35 | 15,109 | 55,150 | 10,590 → 17,596 | 100% |
| Dry marginal | 24 → 49 | 0.15 | 4,155 | 37,813 | 4,537 → 6,456 | 100% |
| Dry marginal | 96 → 47 | 0.16 | 4,319 | 37,818 | 4,536 → 6,686 | 100% |
| Dry marginal | 192 → 66 | 0.26 | 5,897 | 37,901 | 4,533 → 8,599 | 100% |

High initial populations suffered earlier shortages and fell toward local
support before turn 32. The higher ending `G` in some high-population lanes
is therefore **not** evidence that crowding regenerates food: those lanes
invested more field capital and have different ending populations. Fixed
`q=0` controls above isolate population pressure. The live lanes show that
field access and operated renewal gradually raise `G` while `B` stays
bounded. They do not prove every site can support arbitrary population.
Longer population trajectories, including reserve debt and the observed
growth taper near capacity, are in [LandFoodPopulationGrowth.md](LandFoodPopulationGrowth.md).

The naturally selected fertile river pilot starts with 24 residents and
reaches Cultivation at turn 6. Over its next 25 turns, `q` rises from 0.04 to
0.25, radius-one `G` from 5,437 to 7,273, and population from 69 to 91.
Every turn met the prior cohort's food need; late `B` moved only from 35,784
to 35,519. This is a gradual, permanent q≈0.25 posture in a fertile valley,
not a one-turn jump. The [river table](LandFoodRiverCultivation.csv) includes
identical-policy runs with managed recovery set to zero or doubled:

| `CultivationRegenFlux` | Post-Cultivation turn 25 `q` | End `G` | End `B` | Food need met | Mean condition |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 0.00 | 0.19 | 7,528 | 35,783 | 100% | 1.000 |
| 0.65 | 0.25 | 7,273 | 35,519 | 100% | 0.994 |
| 1.30 | 0.22 | 6,822 | 34,730 | 100% | 0.974 |

Under *fixed* opening stock, `q` and claims, the operated `q=.25` recovery
test raises next-turn `G` above zero managed recovery and uncultivated `q=0`.
The live runs change field choices, workforce, claims and condition in
response to the constant. Their reverse end-stock ordering therefore does
not mean a positive managed source physically subtracts food. It **does**
show that the current field/expansion forecast values potential managed flow
even when much of it overflows the finite stock ceiling. At turn 25 the
pre-cap source term was 76,611 with flux .65 and 151,950 with flux 1.30;
retained stocks remained below 8,000. Raising the constant alone is not a
sound response to stock degradation. The forecast should be audited against
next-turn *realizable* stock before this candidate is adopted.

The six fixed-site pilots completed 25 turns after their live Cultivation
transition. Open, woodland and newly selected dry marginal had **100% food
satisfaction in each final ten turns**. Their ending populations were 76,
185 and 28; late `B` was stable (open 45,973 → 45,973; woodland 56,384 →
56,031; dry 29,344 → 29,352). The dry site had two deaths and a minimum
food satisfaction of 77.4% earlier, so this is eventual support, not an
uninterrupted no-starvation claim. A fixed-claim `q=0` versus operated
`q=0.25` test holds opening state and demand equal and confirms the latter
has higher next-turn `G`; the whole-camp pilot does not hold workforce and
population equal to a `q=0` counterfactual.

## Representative 80-tribe worlds and scale

Production configuration, Standard 1,024x512, seed 1, 80 tribes, three map
shapes. Each world reached natural opening completion and ran ten more turns.
Population is linked camps plus hunters; QoL weights the prior resident
cohort; food is realized non-herd tile yield. The
[per-turn table](LandFoodStandard80.csv) also includes Cultivation subgroup
food satisfaction and realized Cultivation yield.

| World | Final turn | Linked people | Prior-cohort QoL | Realized food | Cultivation tribes / people | Cultivation food need met |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Continents | 20 | 13,254 | 0.33 | 1,071,082 | 49 / 7,852 | 99% |
| Pangaea | 21 | 14,097 | 0.36 | 1,155,309 | 57 / 9,310 | 100% |
| Archipelagos | 21 | 16,775 | 0.28 | 1,292,401 | 46 / 8,659 | 96% |

Five 96x48/eight-tribe openings (seeds 1, 7, 11, 17, 1729) completed in
9–10 turns. A separate Standard/Continents/1729 benchmark continued 80 and
benchmark-only 320 tribes for five turns after opening. Its largest active
tile counts were 2,600 and 10,532, **equal to tile integrations** in each
case; there was no work proportional to the eighty historical quarters.
Single-run post-opening combined AI+prepare+execute medians were 285 ms and
1,683 ms, with observed peak managed heaps of 319 MiB and 1,254 MiB. These
unwarmed, unpaired figures describe scale only. A matched Release timing gate
against the reference branch remains required before adoption.

## Validation status and open gates

The full population sweep projected every land tile at case end and found
zero excess over current `G`/`B` capacity in all 54 lanes and three
zero-claim controls. `--land-food-only` passed 37 aquatic and 165 horizon
ecology assertions, including land/aquatic rate isolation, saved generator-12
profile continuation, `q=0.25` managed recovery, conservation, zero stock,
and nonnegative bounds. `--horizon-only` also passed 61 cultivation checks
and the 28-tile-kernel duration-independence check. All 20 transition quick
cases and six fixed 25-turn pilots completed without balance failures. The
fertile-river normal case matched scalar, two-worker and save-continuation
fingerprints exactly.

Open before desktop adoption:

1. Compare every turn's aggregate population, prior-cohort QoL and realized
   food against the accepted prior-cohort reference using paired worlds.
   Re-selection and changed starting stocks must be disclosed; the current
   three Standard reports are candidate-only. The earlier experimental full
   fixed headless run also stopped on an old geology fingerprint tied to
   generated food changes. Do not call this a complete legacy-suite pass.
2. Run sequential, warmed, matched Release performance pairs at 80 and
   benchmark-only 320 tribes. The single runs above do not satisfy the
   no-more-than-10%-slower median gate.
3. Extend the fixed-camp `q=0` counterfactual if whole-pilot attribution of
   cultivation versus policy and population feedback is required. The pure
   equal-claim test proves the local recovery mechanism; the live sweeps
   prove the combined behavior.
4. Correct field and expansion forecasts if they continue to price pre-cap
   managed flow as harvestable food. The zero/.65/1.30 river comparison shows
   this feedback can reverse the expected aggregate stock benefit even while
   the local recovery operator is monotone under identical claims.

Authoritative scaling is one bounded four-profile kernel per active tile,
plus local camp claims and indexed sources. Generation's repeatable-start
cache is transient; no ecological stock, diagnostics, or selection cache is
read from timing data. More mature settlements and RL rollouts will increase
active tiles and planner work; the measured 320-tribe heap is already a reason
to keep that size benchmark-only.

## Recreate

Run sequentially from this branch in a Windows checkout. Raw files are
ignored under `TestResults/LandFoodPopulationSweep/`; the five linked CSVs
above are tracked. Substitute a local .NET 8 executable if needed.

```powershell
$sdk = 'C:/Users/suyas/Documents/ChatGPT/Anthropocene/.tools/dotnet/dotnet.exe'
& $sdk build Tools/LandFoodPopulationSweep/Anthropocene.LandFoodPopulationSweep.csproj -c Release
& $sdk Tools/LandFoodPopulationSweep/bin/Release/net8.0/Anthropocene.LandFoodPopulationSweep.dll --repo . --out TestResults/LandFoodPopulationSweep/M30-LandR003-FullNeolithic --live-turns 32
python Tools/LandFoodPopulationSweep/Summarize.py --input TestResults/LandFoodPopulationSweep/M30-LandR003-FullNeolithic/turns.csv --out Docs/LandFoodNeolithicPopulationSweep.csv --live-turns 32
& $sdk build Tools/NeolithicTransitionValidation/Anthropocene.NeolithicTransitionValidation.csproj -c Release
& $sdk Tools/NeolithicTransitionValidation/bin/Release/net8.0/Anthropocene.NeolithicTransitionValidation.dll --repo . --out TestResults/LandFoodPopulationSweep/M30-LandR003-QuickFinal --quick --seed-limit 18
& $sdk Tools/NeolithicTransitionValidation/bin/Release/net8.0/Anthropocene.NeolithicTransitionValidation.dll --repo . --out TestResults/LandFoodPopulationSweep/M30-LandR003-FixedPilotFinal --seed-limit 18 --site-selection TestResults/LandFoodPopulationSweep/M30-LandR003-QuickFinal/selection.csv --fixed-pilot
foreach ($flux in @(0.00, 0.65, 1.30)) {
    $label = if ($flux -eq 0.00) { 'M30-LandR003-RiverFlux0' } elseif ($flux -eq 0.65) { 'M30-LandR003-RiverManaged' } else { 'M30-LandR003-RiverFlux130' }
    & $sdk Tools/NeolithicTransitionValidation/bin/Release/net8.0/Anthropocene.NeolithicTransitionValidation.dll --repo . --out "TestResults/LandFoodPopulationSweep/$label" --seed-limit 18 --site-selection TestResults/LandFoodPopulationSweep/M30-LandR003-QuickFinal/selection.csv --case productive-river-normal --cultivation-regeneration $flux
}
& $sdk build Tools/PerformanceAudit/Anthropocene.PerformanceAudit.csproj -c Release
foreach ($shape in @('Continents','Pangaea','Archipelagos')) {
    & $sdk Tools/PerformanceAudit/bin/Release/net8.0/Anthropocene.PerformanceAudit.dll --players=80 --turns=12 --post-opening-turns=10 --seed=1 "--shape=$shape" --warmup=0 --metrics=0 --fingerprints=1 --details=1 --production-config=1 "--output=TestResults/LandFoodPopulationSweep/Standard80-$shape.json"
}
python Tools/LandFoodPopulationSweep/SummarizeNeolithic.py
& $sdk run --project Tools/Headless/Anthropocene.Headless.csproj -c Release -- --land-food-only
& $sdk run --project Tools/Headless/Anthropocene.Headless.csproj -c Release -- --horizon-only
```

For the five small openings, use the same performance binary with
`--players=8 --turns=12 --shape=Continents --production-config=1` and seeds
1, 7, 11, 17, 1729. For scale, use seed 1729/Continents with
`--players=80` and `--players=320`, `--post-opening-turns=5`, and
`--fingerprints=0 --details=0`; the 320 case is benchmark-only.
