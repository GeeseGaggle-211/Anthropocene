# Turn-horizon representative 80-tribe gameplay sweep

September 26, 2026. Seasonal reference `c33a5b7`, horizon simulation
`37b1bdd` (documentation checkpoint `991c52c`). This is an outcome audit,
not a performance rerun. The three 80-tribe worlds have **the same seed but
different shapes**; within each shape, seasonal and horizon reports have
identical starting fingerprints and configuration hashes. All use the
production `OpeningConfig.json` and `ResourceGenerationConfig.json`.

Production OpeningConfig and ResourceGenerationConfig; seed 1; Standard 1,024×512; 80 tribes; 20-year turns. Each world runs through natural opening completion and ten further turns. Baseline is the seasonal prior-cohort checkpoint; Horizon is the analytical resolver. Reports start from identical generated fingerprints and configuration hashes within each world. The Archipelagos opening ends at turn 12 in the baseline and turn 11 in the horizon model, so its paired table stops at their last common turn 21; the baseline's turn 22 remains in the CSV.

Population is the sum of linked camp and hunter populations. QoL is weighted by the prior-turn camp residents, matching the prepared-turn cohort. Food is realized non-herd tile yield in the reported turn; it includes hunter fishing. Specialization counts use each camp's ending opening technology. These are full-world AI runs, not isolated policy controls.

## World totals at selected turns

| World | Turn | Model | Linked population | Prior-cohort QoL | Food yield | Surviving tribes | Cultivation / Husbandry / Watercraft |
| --- | ---: | --- | ---: | ---: | ---: | ---: | ---: |
| Continents | 1 | Baseline | 2,277 | 0.600 | 176,777 | 80 | 0 / 0 / 0 |
| Continents | 1 | Horizon | 2,280 | 0.600 | 176,959 | 80 | 0 / 0 / 0 |
| Continents | 10 | Baseline | 8,055 | 0.339 | 678,989 | 80 | 55 / 24 / 1 |
| Continents | 10 | Horizon | 7,968 | 0.298 | 692,045 | 80 | 51 / 28 / 1 |
| Continents | 20 | Baseline | 25,569 | 0.362 | 2,101,757 | 80 | 55 / 24 / 1 |
| Continents | 20 | Horizon | 25,755 | 0.350 | 2,080,107 | 80 | 51 / 28 / 1 |
| Pangaea | 1 | Baseline | 2,352 | 0.600 | 174,938 | 80 | 0 / 0 / 0 |
| Pangaea | 1 | Horizon | 2,352 | 0.600 | 174,938 | 80 | 0 / 0 / 0 |
| Pangaea | 10 | Baseline | 8,050 | 0.403 | 705,506 | 80 | 59 / 20 / 1 |
| Pangaea | 10 | Horizon | 8,081 | 0.339 | 709,999 | 80 | 52 / 28 / 0 |
| Pangaea | 20 | Baseline | 27,785 | 0.387 | 2,244,900 | 80 | 59 / 20 / 1 |
| Pangaea | 20 | Horizon | 26,348 | 0.365 | 2,146,342 | 80 | 52 / 28 / 0 |
| Archipelagos | 1 | Baseline | 2,324 | 0.600 | 178,126 | 80 | 0 / 0 / 0 |
| Archipelagos | 1 | Horizon | 2,325 | 0.600 | 179,662 | 80 | 0 / 0 / 0 |
| Archipelagos | 10 | Baseline | 8,859 | 0.279 | 756,023 | 80 | 45 / 23 / 10 |
| Archipelagos | 10 | Horizon | 9,071 | 0.235 | 766,138 | 80 | 39 / 28 / 12 |
| Archipelagos | 21 | Baseline | 29,377 | 0.333 | 2,368,501 | 80 | 46 / 24 / 10 |
| Archipelagos | 21 | Horizon | 28,951 | 0.317 | 2,276,534 | 80 | 39 / 28 / 13 |

## Final specialization outcomes

Each cell is tribes / linked population / prior-resident-weighted QoL. Food satisfaction is available for every group and turn in the CSV.

| World | Specialization | Baseline | Horizon |
| --- | --- | ---: | ---: |
| Continents | Cultivation | 55 / 18,755 / 0.494 | 51 / 18,057 / 0.499 |
| Continents | AnimalHusbandry | 24 / 6,459 / 0.000 | 28 / 7,241 / 0.000 |
| Continents | Watercraft | 1 / 355 / 0.000 | 1 / 457 / 0.000 |
| Pangaea | Cultivation | 59 / 20,889 / 0.500 | 52 / 19,087 / 0.500 |
| Pangaea | AnimalHusbandry | 20 / 6,503 / 0.000 | 28 / 7,261 / 0.000 |
| Pangaea | Watercraft | 1 / 393 / 0.600 | — |
| Archipelagos | Cultivation | 46 / 18,895 / 0.485 | 39 / 16,914 / 0.496 |
| Archipelagos | AnimalHusbandry | 24 / 7,499 / 0.000 | 28 / 8,177 / 0.000 |
| Archipelagos | Watercraft | 10 / 2,983 / 0.167 | 13 / 3,860 / 0.187 |

## Archipelagos food receipts

Fishing is the active hunter claim; marine gathering is passive coastal food. Values are realized food units, not stocks.

| Period | Model | Active Fishing | Passive marine gathering | Cultivation | Total non-herd food |
| --- | --- | ---: | ---: | ---: | ---: |
| Turn 10 | Baseline | 50,616 | 96,173 | 243,336 | 756,023 |
| Turn 10 | Horizon | 45,738 | 105,725 | 261,982 | 766,138 |
| Turn 21 | Baseline | 121,295 | 150,035 | 1,401,684 | 2,368,501 |
| Turn 21 | Horizon | 104,290 | 232,971 | 1,240,053 | 2,276,534 |
| Cumulative turns 1–21 | Baseline | 1,240,672 | 1,968,102 | 9,485,069 | 22,419,819 |
| Cumulative turns 1–21 | Horizon | 1,193,641 | 2,589,412 | 9,062,424 | 22,219,844 |

## Provenance and limits

The detailed audit is intended for gameplay outcomes. It is not a timing measurement: it enables per-turn fingerprints and per-camp serialization.
The Archipelagos receipt rerun separates active hunter Fishing from passive marine gathering, and its turn fingerprints match the original Archipelagos outcomes. Receipts are whole-world totals, not assigned to each Watercraft tribe; a hunter may belong to another camp.

| World | Baseline JSON SHA-256 | Horizon JSON SHA-256 |
| --- | --- | --- |
| Continents | `E8A65AC475069B6C816A94E1CC29A81B802130C4DB620BACD56B32CD92A26BAA` | `6E532D6A2C392517346C0F18529B317218BC9F7F54659F8E0447115303A9F72A` |
| Pangaea | `C453689E875F083E253220ADC640FB024FF062429A00CA256FD85A2C2F7175A5` | `1C1E1B27578650130D2BA33474205780C5187D5E0E178F5847BB101DD49187FA` |
| Archipelagos | `4BA0A7CC5B0CBAF6D3F4FA6C1FBA0553D42AAB3B22DA7AC53955945B23FFBEA1` | `6E85FDA8A2F4799AF37AE12FA3A56D5A47E6F2FD8B353AC497B8AB6615285F68` |

Fishing-detail JSON SHA-256: Baseline `4440451F56784EBFE7E0E2FA929FCE080D507E63923F8E4FDD23D95F9E07BD66`, Horizon `16E33E65F0E55696B56AB9563E9301995E59B69F8380D73ED7092403ACC3F277`.

`every-turn.csv` contains every observed model-turn and specialization metric.

## Isolated agriculture pilot

The existing `NeolithicSweeps` pilot uses seed 11, three selected productive
sites, 250 initial residents, and 25 turns (500 years). `stock` keeps its
assignments; `replan` submits a new demand-based intent each turn. These
controlled sites are not the 80-tribe worlds above. Values below are the
final completed turn; food share is the fraction of that turn's tile food
credited to cultivation.

| Site | Policy | Seasonal residents / QoL / farm food share | Horizon residents / QoL / farm food share | Status |
| --- | --- | ---: | ---: | --- |
| River | Stock | 547 / 0 / 0% | 541 / 0 / 0% | Both complete 500 years. |
| River | Replan | 1,956 / 0.500 / 100% | 1,788 / 0.500 / 100% | Both complete. |
| Wooded | Stock | 548 / 0 / 0% | 546 / 0 / 0% | Both complete. |
| Wooded | Replan | 1,826 / 0.500 / 99.9% | 1,828 / 0.500 / 100% | Both complete. |
| Open | Stock | 206 / 0.00220 / 100% | 206 / 0.00016 / 100% | Both stop at year 80: the pilot's stock intent exceeds the real workforce. Do not treat this as a 500-year comparison. |
| Open | Replan | 9 / 0.282 / 100% | 3 / 0.015 / 100% | Both complete; severe food-limited decline. |

The invalid open-stock order is present in **both** implementations. The
open-replan decline is materially worse under the horizon model. In the two
productive replan sites, all final-turn food is nearly cultivated, but QoL
remains 0.5 because Housing is zero in both runs. This pilot was already a
site-selected experiment; it is not evidence that a typical tribe reaches
those populations.

## Live husbandry pilot

The existing `NeolithicHusbandryValidation` pilot uses seed 18, an actual
selected pasture and a separate human-food stress site. Each live case
starts with 24 residents and 10 managed antelope, then runs 25 turns.
The `b` setting is the configured breeding intensity. Animal product is
cumulative accepted food over the run.

| Site / breeding | Seasonal residents / animals / animal food | Horizon residents / animals / animal food |
| --- | ---: | ---: |
| Pasture, b=0.40 | 506 / 136 / 69,949 | 473 / 91 / 51,155 |
| Human-food stress, b=0.40 | 53 / 34 / 29,351 | 35 / 13 / 11,293 |
| Pasture, b=0.80 | 510 / 241 / 108,998 | 132 / 135 / 75,226 |
| Human-food stress, b=0.80 | 53 / 49 / 29,889 | 35 / 20 / 11,781 |

Both pilot binaries complete their fixture and conservation/duration checks.
The high-breeding pasture gap is large enough to require a focused causal
review before declaring gameplay balance acceptable. In that case the first
turn already differs in accepted animal product (0 seasonal versus about
157 horizon). At turn 25, the occupied tile's land condition/biomass is
about 0.315/0 seasonal versus 1.0/285 horizon. These endpoints describe
different ecological trajectories; they do not, by themselves, identify
which equation or allocation is wrong.

## What this changes about acceptance

- The 80-tribe totals can conceal strong local effects. At their matched late
  turns, all three horizon worlds have lower prior-cohort-weighted QoL. The
  Pangaea horizon ends with no Watercraft tribe where the seasonal reference
  has one; Archipelagos has 13 versus 10.
- Husbandry QoL is zero in every specialization row in **both** models. That
  is a pre-existing goods/housing balance limitation, not a new horizon-only
  result. Archipelagos Watercraft QoL is low in both (0.167 versus 0.187 at
  turn 21).
- Active Fishing yields 121,295 → 104,290 at Archipelagos turn 21
  (−14.0%), while passive marine gathering rises 150,035 → 232,971
  (+55.3%). Across turns 1–21, active catch changes 1,240,672 → 1,193,641
  (−3.8%). The rerun's per-turn fingerprints match the original
  Archipelagos reports exactly; the receipt field is diagnostic only.
- The agriculture and husbandry fixtures show large changes beyond the
  Standard aggregate. Preserve the structural turn-horizon speed result,
  but do not mark ecological/economic balance parity complete. Investigate
  seasonal stock/access clipping, fixed camp food budgets and the first
  husbandry product difference with paired per-turn ledgers before retuning.

## Reproduce

Use the authoritative Windows checkout and the Release .NET 8 SDK. The
seasonal source archive is made from `c33a5b7` as described in the
[performance audit README](../Tools/PerformanceAudit/README.md); its
SHA-256 is
`1FD3C5F68C97BC36EE605DB8AA76371E0DEEE93D0F7CF98B69AFB75746FBA7F9`.
`PerformanceAudit` has an optional `--production-config=1` switch; existing
timing runs retain their original defaults when it is absent. Build the
**same current harness** against archived and current simulation sources,
then run sequentially:

```powershell
$sdk = (Resolve-Path ./.tools/dotnet/dotnet.exe).Path
$root = 'TestResults/TurnHorizonGameplay'
$seasonalSource = (Resolve-Path TestResults/TurnHorizonBaseline/Source/Assets/_Anthropocene/Simulation).Path
& $sdk build Tools/PerformanceAudit/Anthropocene.PerformanceAudit.csproj -c Release -t:Rebuild "-p:SimulationSource=$seasonalSource" --output "$root/BaselineBin" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Seasonal audit build failed' }
& $sdk build Tools/PerformanceAudit/Anthropocene.PerformanceAudit.csproj -c Release -t:Rebuild --output "$root/HorizonBin" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Horizon audit build failed' }
foreach ($shape in @('Continents','Pangaea','Archipelagos')) {
    foreach ($model in @('Baseline','Horizon')) {
        & $sdk "$root/${model}Bin/Anthropocene.PerformanceAudit.dll" --players=80 --turns=12 --post-opening-turns=10 --seed=1 "--shape=$shape" --warmup=0 --metrics=0 --fingerprints=1 --details=1 --production-config=1 "--revision=$model" "--output=$root/$shape-seed1-$model.json"
        if ($LASTEXITCODE -ne 0) { throw "Gameplay audit failed: $shape/$model" }
    }
}
```

Run the Archipelagos pair again with output names ending `-Fishing.json` to
record the `foodByKind` receipt field and verify that its fingerprints equal
the first pair. Then run:

```powershell
python Tools/PerformanceAudit/Summarize-TurnHorizonGameplay.py --root TestResults/TurnHorizonGameplay --out TestResults/TurnHorizonGameplay/Summary --seed 1
```

The summarizer writes every-turn CSV and the first three tables above. The
agriculture/husbandry pilots use `--pilot --seed 11` and `--pilot --seed 18`,
respectively, with `--out` directories. Their project files now accept the
same `-p:SimulationSource=$seasonalSource` override for a baseline build;
build and run each variant sequentially. The husbandry pilot's embedded
source-manifest field hashes the local checkout even when its binary uses
archived simulation sources, so use the pinned source archive and binary
provenance for the cross-model comparison. Raw JSON remains ignored under
`TestResults/`; the tables and commands here are the durable handoff.
