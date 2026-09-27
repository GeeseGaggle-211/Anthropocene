# Mobile husbandry and agricultural continuity validation

This report compares `codex/mobile-husbandry` with the pinned
`features/neolithic-agricultural` checkpoint `0676824`. That checkpoint keeps
the prior-cohort economy and analytical turn horizon; it is the behavioral
comparison for this pass. No biomass, water, consumption, or recovery constant
is changed here. The new agricultural AI may move a camp while preserving its
automated economy intent, so its long-term field payoff must be examined with
the husbandry results.

The later feature-branch commit `8622750` ships the previously opt-in
`--land-m=63 --land-access-scale=0.5 --cultivation-effort=16` tuning as
new-world production defaults. Its natural three-world camp, herd, and
specialty outcomes are in
[AgricultureHusbandryPlaytests.md](AgricultureHusbandryPlaytests.md).

## State and scaling contract

At each turn boundary, camp positions, populations, saved field capital,
biomass `B`, human-gatherable stock `G`, freshwater `W`, and stored
`LandCondition` are authoritative. `TurnPlanner.Prepare` takes a pure snapshot
of accepted destinations and freezes the prior cohort's targets. Resource
claims draw from finite local stocks; land recovery runs after land extraction
and prepares the next boundary. Domestic herds may claim radius-one land and
freshwater at actual source tiles. Current gathering, grazing, cultivation,
timber, and quiet projection do not write land condition. Four seasonal
profiles are analytical forcing, not four authoritative subturn states.

Preparation work is proportional to accepted orders, candidate camp sites,
nearby indexed source edges, and eligible local claims. Resolution remains
proportional to active tiles and their claimants, independent of the number of
base periods represented by `dt`. The small radius bounds per-camp local work,
but total work still grows with settlements, connected hunters and herds,
contested claim edges, and active tiles. Mature-empire and RL scaling depend on
retaining those sparse indexes and avoiding global scans inside camp forecasts.
One placement edge case remains: `CampPlacement.Resolve` regroups all camps
after each conflict-cancellation round. An adversarial chain of blocked moves
could take `O(C²)` work for `C` camps. The local ecology coverage itself stays
bounded to radius one, and the measured slowdown has not been attributed to
placement conflicts.

## Reproduction

Use the Windows checkout and run builds and audits **sequentially** with the
same SDK, runtime, worker settings, world shape, and production calibration.
The baseline source is archived from the exact feature checkpoint so the
working tree does not need to be switched. The current audit harness is used
for both binaries. The corrected candidate pauses domestic-herd births unless
the preceding herd cohort receives all its forage and water. These files are
ignored under `TestResults`.

```powershell
$sdk = (Resolve-Path ./.tools/dotnet/dotnet.exe).Path
$root = 'TestResults/MobileHusbandryBaseline'
New-Item -ItemType Directory -Path $root -Force | Out-Null
git archive --format=zip --output="$root/FeatureSource.zip" 0676824 Assets/_Anthropocene/Simulation
Expand-Archive -LiteralPath "$root/FeatureSource.zip" -DestinationPath "$root/FeatureSource" -Force
$referenceSource = (Resolve-Path "$root/FeatureSource/Assets/_Anthropocene/Simulation").Path
& $sdk build Tools/PerformanceAudit/Anthropocene.PerformanceAudit.csproj -c Release -t:Rebuild "-p:SimulationSource=$referenceSource" --output "$root/BaselineBin" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Reference build failed' }
& $sdk build Tools/PerformanceAudit/Anthropocene.PerformanceAudit.csproj -c Release -t:Rebuild --output "$root/MobileBin" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Mobile build failed' }
foreach ($variant in @('Baseline', 'Gate-Stationary', 'Gate-Mobile')) {
    $bin = if ($variant -eq 'Baseline') { "$root/BaselineBin/Anthropocene.PerformanceAudit.dll" } else { "$root/MobileBin/Anthropocene.PerformanceAudit.dll" }
    foreach ($seed in @(11, 23, 1729)) {
        $stationary = if ($variant -eq 'Gate-Stationary') { 1 } else { 0 }
        & $sdk $bin --players=80 --turns=12 --post-opening-turns=80 "--seed=$seed" --shape=Continents --warmup=1 --details=1 --fingerprints=2 --land-m=63 --land-access-scale=0.5 --cultivation-effort=16 "--stationary-husbandry=$stationary" "--output=$root/$variant-80-$seed.json"
        if ($LASTEXITCODE -ne 0) { throw "Audit failed: $variant/$seed" }
    }
}
python Tools/PerformanceAudit/Summarize-MobileHusbandry.py $root Gate-
```

`Stationary` changes only AI husbandry Move orders into NoOp before
preparation. It is a diagnostic control and is not a production rule. The
summary uses the final population and animal counts, last 40 turns of
food/forage/water fulfillment, births/deaths, and final radius-one local
stocks. Local stock footprints can overlap, so stock medians are descriptive,
not a world total. Final fingerprints verify repeatability of each run, but
cross-model fingerprints are not expected to match. The raw corrected reports
are `TestResults/MobileHusbandryBaseline/Gate-{Mobile,Stationary}-80-{11,23,1729}.json`.
They were produced on 2026-09-27 with .NET 8.0.31 on Windows x64, four
workers, partition size 64, 7,300-day turns, and config hash
`5CE4E1EE9669FDF168052235B2E8B7824A4DBED7E12698FD52AD5D0D68088B76`.
The candidate audit DLL SHA-256 is
`FB261A0364D574067AB2B43FC6F34FA15272496A2509980D9D84B7DEE2C53E89`;
the reference DLL SHA-256 is
`9EEC3F17FFCE08B7584D082C542E20B11CB055A3DA593482EA1E7C367A087069`.
The reports embed `revision: unspecified`: the DLL hash identifies the measured
binary, but the uncommitted candidate source snapshot is not independently
versioned in the report. A rerun after a source change is a new measurement.

The reference worlds ended as follows; the candidate and stationary results
are reported below after the same sweep. Residents are camp residents, while
the population column includes connected hunter groups.

| Seed | Humans | Population-weighted QoL | Cultivation camps | Cultivation residents | Cultivation moves | Domestic animals | Husbandry moves |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 11 | 46,727 | 0.391 | 57 | 38,819 | 4 | 2,606 | 2 |
| 23 | 47,702 | 0.389 | 55 | 38,161 | 6 | 3,756 | 5 |
| 1729 | 47,363 | 0.346 | 58 | 39,842 | 6 | 3,034 | 8 |

## Three-world outcome sweep

Each run completes its natural opening (ten turns in these seeds) and then
resolves 80 post-opening turns. Fulfillment and births/deaths cover the last
40 turns. `Stationary` and `Mobile` use the same corrected model; the former
suppresses only husbandry AI moves, preserving its economic intent. The
feature checkpoint is a historical comparison, not an exact outcome target
because the new rules intentionally change herd access and land condition.

| Variant | Seed | Humans final | Domestic animals | Husbandry moves | Forage met late | Water met late | Human food met late | Animal births/deaths late |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Reference | 11 | 46,727 | 2,606 | 2 | 0.877 | 0.528 | 0.981 | 9,167 / 9,162 |
| Reference | 23 | 47,702 | 3,756 | 5 | 0.851 | 0.573 | 0.971 | 13,348 / 13,447 |
| Reference | 1729 | 47,363 | 3,034 | 8 | 0.811 | 0.630 | 0.985 | 10,721 / 10,776 |
| Gated stationary | 11 | 58,678 | 8,066 | 3 | 1.000 | 0.964 | 0.994 | 1,562 / 1,588 |
| Gated stationary | 23 | 62,535 | 11,660 | 5 | 1.000 | 0.970 | 0.992 | 2,252 / 1,872 |
| Gated stationary | 1729 | 64,229 | 10,917 | 7 | 1.000 | 0.958 | 0.990 | 2,464 / 2,287 |
| Gated mobile | 11 | 64,466 | 12,755 | 298 | 1.000 | 0.965 | 0.995 | 2,515 / 2,288 |
| Gated mobile | 23 | 70,807 | 19,101 | 313 | 1.000 | 0.958 | 0.994 | 4,275 / 3,350 |
| Gated mobile | 1729 | 72,821 | 18,048 | 311 | 1.000 | 0.960 | 0.995 | 4,181 / 3,406 |

The gated moving control supports roughly 1.6 times as many domestic animals
as its stationary pair. It has real movement rather than a fixed stationary penalty; a healthy stationary herd
can stay if local renewable supply covers its needs. Stationary camps make
three to seven moves in these controls because non-AI moves and technology
changes can still occur; the intervention suppresses husbandry AI Move
orders, not all possible movement history.

Final-turn median radius-one stocks around husbandry camps are shown below.
Opening and ending footprints are each measured at the camp's respective
site, so a moving camp's two columns may cover different tiles. Overlap
between camps is counted separately; these are local signals, not an additive
world inventory.

| Variant | Seed | Biomass B opening → ending | Human food G opening → ending | Freshwater opening → ending |
| --- | ---: | ---: | ---: | ---: |
| Reference | 11 | 118,290 → 118,290 | 14,725 → 14,725 | 179 → 179 |
| Reference | 23 | 175,767 → 175,784 | 15,273 → 15,273 | 275 → 261 |
| Reference | 1729 | 152,540 → 152,336 | 16,859 → 16,859 | 357 → 353 |
| Gated stationary | 11 | 133,722 → 133,722 | 16,225 → 16,225 | 0 → 0 |
| Gated stationary | 23 | 198,950 → 198,950 | 17,400 → 17,400 | 0 → 0 |
| Gated stationary | 1729 | 148,080 → 148,078 | 15,977 → 15,977 | 0 → 0 |
| Gated mobile | 11 | 176,339 → 176,340 | 21,043 → 21,043 | 0 → 0 |
| Gated mobile | 23 | 232,709 → 232,646 | 27,131 → 27,131 | 0 → 0 |
| Gated mobile | 1729 | 227,483 → 227,483 | 27,485 → 27,485 | 0 → 0 |

| Variant | Seed | Cultivation residents final | Cultivation moves | Food needs met late | QoL late | Mean field q late |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Reference | 11 | 38,819 | 4 | 0.996 | 0.489 | 0.508 |
| Reference | 23 | 38,161 | 6 | 0.997 | 0.448 | 0.507 |
| Reference | 1729 | 39,842 | 6 | 0.994 | 0.425 | 0.505 |
| Gated stationary | 11 | 44,181 | 88 | 1.000 | 0.509 | 0.509 |
| Gated stationary | 23 | 43,485 | 101 | 0.998 | 0.509 | 0.511 |
| Gated stationary | 1729 | 47,376 | 111 | 0.997 | 0.513 | 0.512 |
| Gated mobile | 11 | 44,285 | 84 | 0.998 | 0.508 | 0.510 |
| Gated mobile | 23 | 43,485 | 101 | 0.998 | 0.509 | 0.511 |
| Gated mobile | 1729 | 47,376 | 111 | 0.997 | 0.513 | 0.512 |

The earlier feature AI emitted an Economy order in place of Move, explaining
the very small historical cultivation-move counts. This branch lets the same
AI policy choose a camp site and attach an independent economic intent. Its
agricultural score compares bounded three-turn harvest/recovery from existing
owned fields with a discounted rebuild effort from the current soft
cultivation-cost curve. This preserves a payoff for staying at developed
fields, while allowing relocation when local supply is exhausted. These
worlds maintain high agricultural food fulfillment and field intensity.

### Breeding feedback and remaining water shortage

The first mobile run exposed a reproduction feedback, not an inadequate land
recovery rate. Herds received nearly all requested forage but only about half
their requested water. The old resolver still bred survivors under partial
nutrition. At roughly 0.51 nutrition over a twenty-year turn, the default
0.45 breeding intensity produces about 9.7% births from survivors; the
0.18 mortality coefficient removes about 8.8% of starting animals after
starvation grace. Those changes nearly cancel in the final count, concealing
large birth/death turnover. The earlier raw diagnostic reports are
`TestResults/MobileHusbandryBaseline/{Mobile,Stationary}-80-{11,23,1729}.json`;
their late-40-turn result was:

| Earlier ungated variant | Seed | Domestic animals final | Water met | Animal births / deaths |
| --- | ---: | ---: | ---: | ---: |
| Stationary | 11 | 15,634 | 0.510 | 55,160 / 55,086 |
| Stationary | 23 | 21,872 | 0.510 | 77,152 / 77,003 |
| Stationary | 1729 | 20,549 | 0.510 | 72,474 / 72,362 |
| Mobile | 11 | 26,408 | 0.514 | 90,754 / 89,011 |
| Mobile | 23 | 35,927 | 0.512 | 127,088 / 126,146 |
| Mobile | 1729 | 33,133 | 0.511 | 116,542 / 115,987 |

The corrected resolver permits managed births and breeding-progress accrual
only when the prior herd cohort's **realized** forage and water needs are both
fully met. It does not use a forecast to override the resource ledger, change
the AI's breeding-intensity bands, or add newborn demand to the current turn.
The gate reduces requests until available freshwater nearly supports each
herd. In gated mobile seed 11, final-40-turn births/deaths fall to
2,515 / 2,288 and water fulfillment rises from 0.514 to 0.965. The same
direction holds in seeds 23 and 1729 and in the stationary controls, as shown
above. Animal counts remain higher in moving than stationary camps.

There is still a measured water shortage of **3.0–4.2%** across the six
gated runs: late water fulfillment is 0.958–0.970, whereas forage fulfillment
is 1.000. Gated mobile seed 11 requests 12.59 million water units and
receives 12.16 million over the last 40 turns. The final median husbandry
footprint has zero opening and ending freshwater stock, yet within-turn
recharge supplies almost all drinking. Zero boundary stock is not zero
throughput. The residual deficit reflects finite freshwater recovery and
shared claims, not overgrazing; biomass and gatherable stock remain finite,
and no current Neolithic mechanic changes `LandCondition`. This pass leaves
all water and biomass capacities and recovery constants unchanged. A larger
stable herd target would require separate freshwater-capacity calibration or
a distinct watering-route mechanic, with wild-herd and human-water effects
remeasured.

## Matched Release turn timing

Three sequential, warmed timing pairs resumed the same saved Standard/Continents/1729 worlds at 80 and benchmark-only 320 tribes for 25 agricultural post-opening turns. Each pair used four workers and alternated run order. The table reports the **median of the 25 turns** for each phase; the gain is the median of 25 corresponding-turn total-time comparisons, so it need not equal the ratio of the two displayed total medians. Total time includes AI, preparation, and resolution.

| Tribes | Pair | AI ms reference → mobile | Prepare ms reference → mobile | Resolve ms reference → mobile | Total ms reference → mobile | Median paired gain | Allocated MiB reference → mobile |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 80 | 1 | 55.91 → 33.57 | 19.22 → 47.15 | 72.83 → 67.13 | 171.58 → 178.58 | +4.8% | 133.6 → 130.3 |
| 80 | 2 | 51.63 → 37.69 | 21.09 → 52.77 | 85.53 → 85.95 | 168.12 → 195.10 | +5.7% | 133.6 → 129.6 |
| 80 | 3 | 54.02 → 27.26 | 24.21 → 44.30 | 92.84 → 66.91 | 179.57 → 140.74 | +18.4% | 133.8 → 129.1 |
| 320 | 1 | 270.22 → 120.95 | 124.65 → 286.99 | 403.09 → 438.85 | 1,114.05 → 1,144.69 | **−5.5%** | 516.0 → 520.6 |
| 320 | 2 | 224.93 → 99.47 | 98.91 → 277.83 | 343.27 → 344.54 | 796.71 → 1,007.87 | +2.3% | 516.1 → 520.8 |
| 320 | 3 | 280.62 → 104.88 | 115.02 → 249.13 | 432.39 → 308.10 | 1,040.99 → 825.34 | +13.3% | 516.4 → 520.8 |

The median pair gain is **5.7% at 80** and **2.3% at 320**, but the first 320 pair regresses. The 320 candidate allocates about 0.9% more per turn. Preparation is markedly slower while AI is faster, and individual turns show runtime spikes in both variants: candidate turn 32 in 320 pair 2 took 4.19 times its matching reference turn, and other pairs show opposite spikes. The corresponding-turn timing gate is therefore uneven; these results do not establish a reliable large-world speedup. Production outcomes also diverge by design: from the common starting save, final human population is 42,840 → 59,516 at 80 and 185,795 → 246,254 at 320. The timing comparison measures the work of the two evolving worlds, not identical claim graphs.

Timing mode omits detailed source counters and fingerprints. Basic counters remain in the paired CSV: median per-turn plan calls are 79 in both 80-tribe variants and 312 in both 320-tribe variants; median tile integrations are 2,468 → 2,459 and 9,964 → 9,978, respectively. Thus the measured work is still one integration per active tile, and the slower preparation is not explained by more planner calls. The [full per-turn table](../TestResults/MobileHusbandryBaseline/TimingFinal/paired-turns-Timing.csv), [summary](../TestResults/MobileHusbandryBaseline/TimingFinal/summary-Timing.md), and [manifest](../TestResults/MobileHusbandryBaseline/TimingFinal/manifest-Timing.json) retain the pair data, hashes, worker settings, and runtime provenance. `TestResults` is ignored by Git; preserve those local files with the handoff when sharing the raw measurements.

Recreate the paired timing reports after building the two Release DLLs above. Generate both starting saves with the reference binary, then run the pairs:

```powershell
$sdk = (Resolve-Path ./.tools/dotnet/dotnet.exe).Path
foreach ($size in @(80, 320)) {
    & $sdk 'TestResults/MobileHusbandryBaseline/BaselineBin/Anthropocene.PerformanceAudit.dll' "--players=$size" --turns=12 --seed=1729 --shape=Continents --warmup=1 "--save-finished=TestResults/MobileHusbandryBaseline/start-$size.anthro" "--output=TestResults/MobileHusbandryBaseline/start-$size.json"
    if ($LASTEXITCODE -ne 0) { throw "Opening save failed for $size" }
}
& ./Tools/PerformanceAudit/Run-TurnHorizonPairs.ps1 -BaselineDll 'TestResults/MobileHusbandryBaseline/BaselineBin/Anthropocene.PerformanceAudit.dll' -CandidateDll 'TestResults/MobileHusbandryBaseline/MobileBin/Anthropocene.PerformanceAudit.dll' -Save80 'TestResults/MobileHusbandryBaseline/start-80.anthro' -Save320 'TestResults/MobileHusbandryBaseline/start-320.anthro' -OutputRoot 'TestResults/MobileHusbandryBaseline/TimingFinal' -Mode Timing -Pairs 3 -PostOpeningTurns 25 -BaselineRevision '0676824d3baa42c04b8ec5f050ab888d2fd60044' -CandidateRevision 'codex-mobile-husbandry-final'
python Tools/PerformanceAudit/Summarize-TurnHorizonPairs.py TestResults/MobileHusbandryBaseline/TimingFinal --mode Timing
```

## Validation gates

The final fixed suite passed on 2026-09-27 with
`./Tools/Test-Headless.ps1 -Seeds 0`: **62,285 numbered assertions** across
the printed suites. Relevant counts include 258 husbandry, 49 agent contract,
114 economic automation, 276 Neolithic, 49 ecology, 168 horizon ecology,
539 pastoral craft, and 43,824 Issue #25 ecology assertions. The other printed
counts are 33 focused, 59 milestone two, 82 milestone three, 25 foundation,
25 moisture, 15 climate, 35 morphology, 51 archipelago, 8,951 geology,
37 aquatic, 14 start viability, 22 presets, 37 housing material, 83 Wood,
104 tool consumption, 45 Stone Farm Tool contract, 1,154 Stone Farm Tool
planner, 12 mixed tool/Cloth chronology, 6,153 cultivation cache, and
71 horizon cultivation. The execution suite additionally matched scalar,
two- and four-worker, reversed-partition, and reordered-entity fingerprints
over **35 resolutions**, including 25 post-opening turns; its mature
agricultural save restored identical two/four-worker fingerprints.

The final Standard/Continents/1729, 80-tribe exact replay loaded the same
end-of-opening save (`SHA-256
9932E92F268A63EEC5AF5DEF4F8BDEC1B11A1C0D65264C5F5203CA00748F4FE7`)
in every mode. The Release mobile DLL SHA-256 was
`FB261A0364D574067AB2B43FC6F34FA15272496A2509980D9D84B7DEE2C53E89`.
All **25 per-turn fingerprints** matched the scalar result exactly:

| Mode | Workers | Partition | Reversed partitions | Post-opening turns | Per-turn match |
| --- | ---: | ---: | --- | ---: | --- |
| Scalar | 1 | 64 | No | 25 | Exact |
| Two workers | 2 | 64 | No | 25 | Exact |
| Four workers | 4 | 64 | No | 25 | Exact |
| Reversed | 4 | 8 | Yes | 25 | Exact |

The shared final fingerprint is
`CFBFF6E819CF498D4773B116B3250EA745055CD463AB23CA24CDE92D8A4D16A8`.
Raw reports are under `TestResults/MobileHusbandryBaseline/FinalExact/` and
are ignored by Git. Recreate them sequentially from the saved Release DLL:

```powershell
$sdk = (Resolve-Path ./.tools/dotnet/dotnet.exe).Path
$root = 'TestResults/MobileHusbandryBaseline'
New-Item -ItemType Directory -Force -Path "$root/FinalExact" | Out-Null
foreach ($mode in @(
    @{ Name='scalar'; Workers=1; Partition=64; Reverse=0 },
    @{ Name='two'; Workers=2; Partition=64; Reverse=0 },
    @{ Name='four'; Workers=4; Partition=64; Reverse=0 },
    @{ Name='reversed'; Workers=4; Partition=8; Reverse=1 }
)) {
    $auditArgs = @(
        "--load=$root/start-80.anthro", '--post-opening-turns=25',
        "--workers=$($mode.Workers)", "--partition-size=$($mode.Partition)",
        "--reverse-partitions=$($mode.Reverse)", '--fingerprints=1',
        '--metrics=0', '--warmup=0', '--revision=mobile-breeding-gate',
        "--output=$root/FinalExact/$($mode.Name)-80.json"
    )
    & $sdk "$root/MobileBin/Anthropocene.PerformanceAudit.dll" @auditArgs
    if ($LASTEXITCODE -ne 0) { throw "Exact replay failed: $($mode.Name)" }
}
```

Each Standard replay is a save continuation from the common opening
boundary. The fixed execution checks also verify save/load within the
post-opening continuation and reordered entity collections; the performance
audit CLI does not offer a collection-order switch. Focused fixtures cover
accepted and blocked movement, destination-based automation, player
automation precedence, local herd claim boundaries, shared finite ledgers,
depletion and partial-year recovery, full- and partial-nutrition breeding,
fractional-year breeding progress, and unchanged land condition. These exact
results establish deterministic behavior, while the uneven matched timing
results above leave the large-world speed gate open. If mobile herds still
starve often, use the recorded forage and water ledgers to identify the
limiting resource before proposing a separate retune.
