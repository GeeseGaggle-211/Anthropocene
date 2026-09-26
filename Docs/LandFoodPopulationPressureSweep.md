# Harvest-first land food: population-pressure sweep

September 26, 2026. This measures experimental commit `42cdc56` with one
bounded-stock hardening change and no retuning. It is on
`codex/land-food-population-sweep`; it does not change the accepted resolver
on `codex/land-food-harvest-recover`.

## Question and model boundary

Authoritative state is each land tile's turn-boundary total biomass `B`,
human-gatherable subset `G`, condition, and field capital. A land claim debits
only opening stock, then the four climate profiles compose recovery to the
next turn. The controlled sweep asks whether **more people leave less `G` and
`B` at the next boundary**, separate from food satisfaction. It uses the
production `TurnResolver` and the prototype's `LandBiomassMultiplier=15`,
`LandSafeUseRatio=0.5`, and `LandUseDamagePerYear=0.03`.

Three pinned generated sites preserve the earlier comparison: open fertile
seed 15/tile 3819, medium woodland seed 11/tile 4530, and dry marginal seed
17/tile 4373. Each has one isolated camp, seven radius-one land tiles, no
hunters, and one 80-unit food reserve per starting person. Initial populations
are 8, 16, 24, 32, 48, 64, 96, 144, and 192. Each turn lasts 20 years.

The **controlled `q=0`** lane clears wild herds, sets growth to zero, extends
starvation grace, and prevents mastery within its eight turns. This isolates
human pressure while reporting unmet needs. The **live** lane retains wild
herds, normal growth, starvation, technology progression, and automatic
economic orders after Cultivation, for twelve turns. Both hold the camp at
the pinned tile. A third lane integrates the same seven tiles for eight turns
with **zero claims** and no population. These are fixture interventions, not
new gameplay rules. The [complete case summary](LandFoodPopulationPressureSweep.csv)
and [zero-claim control](LandFoodNoHarvestControl.csv) are tracked; raw
per-turn ledgers are in ignored `TestResults/LandFoodPopulationSweep/M15-Final/`.

## End of controlled turn 8

The table shows radius-one totals, rounded to food units. `max G/B` is the
sum of each tile's endpoint capacity after condition and climate forcing;
`G` is a subset of `B`. Food satisfaction is for the fixed starting cohort in
turn 8. The complete CSV contains all nine population levels.

| Site | Starting people | End `G` | End `B` | Max `G` | Max `B` | Food satisfaction |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Open fertile | 0, no claims | 2,563 | 23,415 | 2,764 | 25,859 | — |
| Open fertile | 24 | 2,563 | 23,415 | 2,764 | 25,859 | 100% |
| Open fertile | 48 | 2,563 | 23,414 | 2,764 | 25,859 | 67% |
| Open fertile | 96 | 2,563 | 23,414 | 2,764 | 25,859 | 33% |
| Open fertile | 192 | 2,563 | 23,414 | 2,764 | 25,859 | 17% |
| Medium woodland | 0, no claims | 5,200 | 31,283 | 5,540 | 32,804 | — |
| Medium woodland | 24 | 5,200 | 31,283 | 5,540 | 32,804 | 100% |
| Medium woodland | 48 | 5,196 | 31,267 | 5,535 | 32,787 | 100% |
| Medium woodland | 96 | 5,155 | 31,104 | 5,491 | 32,611 | 67% |
| Medium woodland | 192 | 5,155 | 31,104 | 5,491 | 32,611 | 34% |
| Dry marginal | 0, no claims | 1,510 | 18,985 | 2,269 | 26,485 | — |
| Dry marginal | 24 | 1,506 | 18,969 | 2,262 | 26,456 | 78% |
| Dry marginal | 48 | 1,506 | 18,969 | 2,262 | 26,456 | 39% |
| Dry marginal | 96 | 1,506 | 18,969 | 2,262 | 26,456 | 20% |
| Dry marginal | 192 | 1,506 | 18,969 | 2,262 | 26,456 | 10% |

At fertile open, even 192 people leave virtually the same endpoint as the
zero-claim control. Woodland `B` at 192 is only 0.6% below its zero-claim
control; dry marginal is 0.1% below. Extra people instead receive less food.
The dry site's first-turn fall from opening `B=23,837` to roughly `18,985`
also occurs **without harvest**. It is chiefly a move toward that site's
climate/condition capacity, not evidence of human overharvest. At 8 and 16
people the dry camp's gathering labor yields only about 79% of basic need
by turn 8 even though opening `G` remains available. Around 24 people,
finite opening `G` begins to bind as well.

The live lane does not rescue this pressure signal. It changes population and
field accessibility at the same time. At the open site, 24 starters end turn
12 with 32 people, `q=0.055`, and `G=2,799`; 192 starters end with 68 people,
`q=0.191`, and `G=3,708`. Higher field intensity exposes more `B` as `G`,
while the population falls. The full CSV distinguishes both lanes.

## Diagnosis

The current result is a property of the recovery curve over 20 years, not a
missing biomass clamp. A low-population harvest and an exhausting harvest
both approach the same endpoint during the subsequent long recovery. Once a
camp's request is at least opening `G`, all larger requests leave the same
post-claim `G=0`; the population-independent recovery operator must then
produce the same endpoint. At the open site, 24 people's consumption,
reserve-refill and growth target already exceeds opening `G=2,488` in the
live setup. At woodland, 24 do not exhaust opening `G`, yet the long recovery
still erases the stock difference by the next turn. Current damage is small:
controlled turn-8 mean condition is 1.000 open, at least 0.994 woodland,
and at least 0.998 dry across these populations.

Therefore this prototype **does not yet express the desired persistent
population-pressure response**. It can make people hungry, but it keeps
the next turn's land stock nearly fixed. Raising the damage coefficient until
stock declines would conflate food scarcity with permanent habitat damage.
A revised model needs both enough opening access for modest populations and
a recovery operator whose endpoint retains a measurable effect of realized
extraction at higher populations. No recovery constants were changed here.

## Bounds, scaling, and validation

`LazyEcology.ClampCapacity` now clamps `B` into `[0, K_B]`, `G` into
`[0, min(B, K_G)]`, and freshwater into its capacity. The existing final
tile integration clamp remains in place. The resolver's invariant now checks
`B <= K_B` using current condition and field state, beyond its prior raw
seasonal-max check. Generation rejects nonfinite food capacities and stocks.
Focused tests cover nested pools, fully cultivated `G=B`, 20-year managed
recovery with very high flux, fractional-year recovery, nonnegative stocks,
and conservation. The sweep checks every radius-one tile each turn and
projects **all land tiles** once at the end of each case. Across 54 complete
population cases and three zero-claim controls, maximum capacity excess was
zero. Rebuilding and rerunning after the clamp changes produced an identical
SHA-256 for the raw population CSV, so normal outputs did not change.

The production land kernel still does bounded work per active tile and its
claims rather than `O(dt / BaseDays)` state writes. This sweep has one camp
per world, so it does not certify aggregate 80/320-tribe performance or tile
contention. The added clamp and invariant are constant-time per checked
tile; transient sweep diagnostics and timings are not persisted or fed back
into state. No save-format change was made.

`--horizon-only` passed 152 ecology and 61 cultivation assertions, plus the
duration-independent tile-kernel check. The broader fixed headless run passed
its first 324 assertions but stopped at the geology fingerprint: this
experimental branch changes generated food stock and therefore does not
match the older original-world fingerprint. This is not a claimed full-suite
pass.

## Recreate

From a Windows checkout of `codex/land-food-population-sweep`, use .NET 8:

```powershell
dotnet build Tools/LandFoodPopulationSweep/Anthropocene.LandFoodPopulationSweep.csproj -c Release
dotnet Tools/LandFoodPopulationSweep/bin/Release/net8.0/Anthropocene.LandFoodPopulationSweep.dll --repo . --out TestResults/LandFoodPopulationSweep/M15-Final
python Tools/LandFoodPopulationSweep/Summarize.py --input TestResults/LandFoodPopulationSweep/M15-Final/turns.csv --out Docs/LandFoodPopulationPressureSweep.csv
dotnet run --project Tools/Headless/Anthropocene.Headless.csproj -c Release -- --horizon-only
```

The command records the exact generated configuration, all 54 per-turn
trajectories, and the three zero-claim controls. The nine population levels,
eight controlled turns, twelve live turns and multiplier are overridable via
`--populations`, `--controlled-turns`, `--live-turns`, and `--land-m`.
