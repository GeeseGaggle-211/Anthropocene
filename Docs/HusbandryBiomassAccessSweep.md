# Husbandry counts under the land-food calibration

This is a measurement on `features/neolithic-agricultural` after the opt-in
cultivation-effort merge. It changes no husbandry, breeding, grazing, or food
rule, and does not change production settings. It tests whether the proposed
agricultural food profile also changes husbandry tribes.

## Definitions and method

Three generated Standard/Continents worlds (seeds 11, 23, 1729) each start
with 80 tribes. AI decisions and the production resolver run through the
opening and 70 more twenty-year turns, ending at turn 80. Four profiles
separate the proposed inputs:

| Profile | Land biomass `m` | Uniform natural human-access factor `s` | Cultivation effort `R` |
| --- | ---: | ---: | ---: |
| Production control | 30 | 1 | 1 |
| Biomass only | 63 | 1 | 16 |
| Proposed candidate | 63 | .5 | 16 |
| Effort control | 63 | .5 | 1 |

At a fixed `m`, `s=.5` halves each land habitat's natural human-accessible
fraction; it is not 50% accessibility on every tile. Generated human
gatherable food `G` stays approximately the same at `q=0`, while total
biomass `B` doubles. Domestic and wild grazers can use all `B`, preferring
the part humans cannot gather. `R` changes the cost of building and
maintaining cultivation above `q=.4`; it does not directly change herd
grazing or breeding.

“Husbandry camp” means a camp whose primary technology is
`AnimalHusbandry`. Resident humans are `Camp.Population`; domestic animals
are the sum of `Herd.Animals` with `DomesticCamp` equal to that camp's ID.
The endpoint table includes only camps with at least one resident. It does
not include their separate hunter groups in the human number, or count wild
animals as owned livestock. At candidate seed 23, an extinct camp retains
six domestic animals; those six are excluded from the active-camp count.
The audit also records linked human population and global wild animals.

The compact counts are derived outside the timed AI, preparation, and
resolution phases. All 720 per-turn rows in the control, biomass-only, and
candidate reports reconcile the per-camp resident and domestic-animal sums
to their reported world totals.

## Turn-80 counts

| Profile | Seed | Active husbandry camps | Resident humans | Owned animals | Median humans per camp | Median animals per camp |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Production control | 11 | 24 | 2,928 | 1,009 | 109 | 34 |
| Production control | 23 | 20 | 2,170 | 874 | 83 | 32 |
| Production control | 1729 | 30 | 4,935 | 1,628 | 139 | 51 |
| Biomass only | 11 | 21 | 5,864 | 1,760 | 228 | 48 |
| Biomass only | 23 | 20 | 6,105 | 2,347 | 231 | 93 |
| Biomass only | 1729 | 20 | 5,541 | 1,660 | 269 | 72 |
| Proposed candidate | 11 | 22 | 6,717 | 2,596 | 219 | 54 |
| Proposed candidate | 23 | 20 | 7,205 | 3,757 | 293 | 154 |
| Proposed candidate | 1729 | 21 | 6,434 | 3,030 | 285 | 74 |

Across the three worlds, the median of world-level husbandry totals changes
from **2,928 humans and 1,009 domestic animals** at production settings to
**6,717 humans and 3,030 domestic animals** with the proposed profile.
Holding `m=63,R=16` and changing only `s=1` to `.5` raises the corresponding
world medians from 5,864 to 6,717 humans and from 1,760 to 3,030 animals.
These are generated-world comparisons, not paired outcomes for identical
camps: altered stocks can change viable starts, technology paths, and the
number of surviving husbandry camps.

Global wild animals are separate from owned herds. At turn 80 the candidate
worlds contain 176,528, 147,022, and 123,594 wild animals for seeds 11,
23, and 1729 respectively. Their production controls contain 174,838,
145,741, and 123,373. The large domestic-herd response should therefore
not be described as a comparable rise in the global wild-herd count.

## Candidate trajectory

Counts below are resident humans and their domestic animals in husbandry
camps at the end of each turn. The single extinct camp's six animals are
excluded from seed 23 at turn 80.

| Seed | Turn 20 humans / animals | Turn 40 | Turn 60 | Turn 80 |
| ---: | ---: | ---: | ---: | ---: |
| 11 | 6,424 / 1,253 | 6,902 / 2,584 | 6,748 / 2,602 | 6,717 / 2,596 |
| 23 | 5,802 / 1,514 | 8,034 / 4,365 | 7,356 / 3,812 | 7,205 / 3,757 |
| 1729 | 5,844 / 1,365 | 7,302 / 3,467 | 6,539 / 3,044 | 6,434 / 3,030 |

Seeds 23 and 1729 peak near turn 40 and lose about 10–12% of their
husbandry residents and 13–14% of their domestic animals by turn 80.
Seed 11 is closer to a plateau. The candidate thus expands carrying counts
but does not produce the uniformly gentle population approach seen in the
agricultural village sweep. At turn 40, median husbandry food-needs-met
ratios are .98, .98, and .96 for the three candidate worlds; they are 1.00
at turn 80. These endpoint ratios alone do not establish the cause of the
intervening contraction. A nutrition, births/deaths, and forage-ledger audit
would be required before changing husbandry tuning.

The effort control is decisive for scope: at `m=63,s=.5`, setting `R=1`
or `R=16` produces **identical resident and domestic-animal counts for every
husbandry camp on all 240 compared turns**. In these worlds, the husbandry
change comes from the land food profile, not the new cultivation labor
curve. The full world population does change with `R` because agricultural
camps respond to it.

## Reproduce

Run sequentially in Release from the Windows repository checkout. Audit
JSONs are ignored under `TestResults/HusbandryAccessSweep`. `--details=1`
adds each camp's `Population`, `linked`, `technology`, and `managedAnimals`;
turn samples also include world and husbandry totals. `--fingerprints=0`
avoids per-turn serialization in these measurement runs.

```powershell
$sdk = 'C:/Program Files/Unity/Hub/Editor/6000.5.10f1/Editor/Data/DotNetSdk/dotnet.exe'
& $sdk build Tools/PerformanceAudit/Anthropocene.PerformanceAudit.csproj -c Release
$audit = 'Tools/PerformanceAudit/bin/Release/net8.0/Anthropocene.PerformanceAudit.dll'
$out = 'TestResults/HusbandryAccessSweep'
New-Item -ItemType Directory -Path $out -Force | Out-Null
$profiles = @(
    @{ Name='control'; M=30; S=1; R=1 },
    @{ Name='biomass-only'; M=63; S=1; R=16 },
    @{ Name='candidate'; M=63; S=.5; R=16 },
    @{ Name='access-only'; M=63; S=.5; R=1 }
)
foreach ($p in $profiles) {
    foreach ($seed in @(11, 23, 1729)) {
        & $sdk $audit "--seed=$seed" --players=80 --shape=Continents --production-config=1 "--land-m=$($p.M)" "--land-access-scale=$($p.S)" "--cultivation-effort=$($p.R)" --turns=12 --post-opening-turns=70 --warmup=1 --details=1 --fingerprints=0 "--output=$out/$($p.Name)-$seed.json"
        if ($LASTEXITCODE -ne 0) { throw "Sweep failed: $($p.Name) / $seed" }
    }
}
```

For each report's `samples`, filter `camps` to
`technology == "AnimalHusbandry"` and `Population > 0`, then sum
`Population` and `managedAnimals` at the desired turn. Compare those sums
across the four profiles. The output also retains per-camp food and field
diagnostics for follow-up; the nine control/ablation/candidate runs were
checked for per-turn aggregate reconciliation, and the two `m=63,s=.5`
effort variants were compared for every husbandry camp on every turn.
