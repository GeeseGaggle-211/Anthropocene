# Population response to cultivated land food

September 26, 2026. This is an isolated-camp audit of the experimental
`codex/land-food-population-sweep` profile: land biomass multiplier 30,
land regeneration 0.03, and managed cultivation coefficient 0.65. It extends
[LandFoodRecoveryCalibration.md](LandFoodRecoveryCalibration.md); the paired
seasonal outcome and matched performance gates remain open.

## Food response and turn contract

The authoritative state is integer resident population, fractional birth
progress, edible inventory by provenance, and local land `0 <= G <= B` with
field capital and condition. Each twenty-year turn locks the prior cohort's
needs. Land claims take opening `G`, then four climate profiles recover
next-turn stock. Births commit after that cohort's consumption, production,
goods and storage. The 80-season reference at `c33a5b7` and this horizon
resolver use the same `PlanGrowth`, `FoodBucketTargets` and exponential
`GrowthFood` rule. The available land food changed, not the demographic
equation.

For prior-cohort consumption `C`, opening inventory `I`, received food `Y`,
and attainable reserve goal `R`, the resolver first uses inventory above
`R` for consumption, then requires new income to finish consumption and
refill the reserve. Only the remaining income can fund growth. At rate `r`
over `T` years its food cost is

`GrowthFood(C,r,T) = C * ((exp(r*T)-1)/(r*T) - 1)`.

The rate search takes the greatest `r` within the configured maximum whose
growth cost plus locked prior-cohort fabric feedstock fits the surplus. As
surplus approaches zero, the underlying growth rate approaches zero. Births
are integer with fractional progress, so 20-year turns can show flat years
followed by a batch. There is no hard population cap or logistic factor.

For the ordinary case where opening inventory `I` is at most the attainable
reserve `R`, let `A = I + Y` be food available after receipts. The biological
response has these breakpoints (before later goods production):

| Available food `A` | Prior cohort fed | Reserve after needs | Growth rate |
| --- | --- | --- | --- |
| `A < C` | `A/C` | Zero | Zero; repeated shortfalls can cause deaths after grace |
| `C <= A < C+R` | Fully | `A-C` | Zero |
| `A >= C+R` | Fully | Target filled | Greatest `r` whose growth food **and** forecast fabric input fit `A-C-R`, up to the configured maximum |

Thus an increase in population first spends the growth surplus, then prevents
reserve refill, then reduces current-cohort food satisfaction. At any fixed
food supply, the middle regime is a flat population segment while reserves
decline; starvation appears only after the first two buffers are exhausted.
When old inventory exceeds the reserve target, the resolver also requires
new turn income to cover its priority buckets before it funds births, so
`A` alone is not a complete predictor in that edge case.

This priority is exact **for births**. Edible PlantFood used by goods is
debited after demographic planning. `NeolithicProduction.Produce` can draw
it from inventory even when the reserve is below target. That fulfills
prior-cohort goods but can turn an apparently well-fed plateau into reserve
debt. Requiring full reserve funding before *any* edible manufacturing would
change existing behavior; the current code does not make that guarantee.

## Long-run population curve

Three pinned generated sites each retain one camp, seven radius-one land
tiles and live wild herds. Each starts with 24 residents and uses normal
growth, technology transition and automatic cultivation for 600 turns.
This 12,000-year synthetic extension exposes reserve debt; it does not model
human fission, trade, later technology or migration. Complete data are in
[40-turn windows](LandFoodGrowthWindows.csv),
[food-bucket rows](LandFoodGrowthBuckets.csv), and
[managed-recovery controls](LandFoodGrowthFlux.csv).

| Site | Turn 40 people / q | Turn 80 | Turn 160 | Peak people | Turn 600 people / q | Net people, turns 561–600 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Fertile open | 101 / .28 | 130 / .38 | 153 / .46 | 154 | 154 / .48 | 0 |
| Woodland | 185 / .37 | 225 / .50 | 225 / .56 | 225 | 223 / .57 | 0 |
| Dry marginal | 55 / .19 | 84 / .30 | 98 / .36 | 111 | 109 / .40 | −1 |

The first four 40-turn net-growth windows were **77, 29, 18, 5** in fertile
open; **161, 40, 0, 0** in woodland; and **31, 29, 9, 5** in dry marginal.
As q rises, more food becomes accessible and population rises until prior
cohort consumption and goods absorb the receipts. Q can keep rising while
population stands still because surplus must refill the reserve first.

The population peak is gentle but not exact: woodland fell 225→223 (0.9%)
after reserve depletion and dry marginal fell 111→109 (1.8%) with a few
single-person losses. Open remained 154. The dry site had intermittent
late shortfalls. This supports a gradual *aggregate* taper; integer births
and reserve cycles are not monotone every turn. A fixed permanent carrying
capacity has not been proven for every habitat or contested tile.

At turn 160, changing the managed-recovery coefficient produced these
coupled population/field outcomes. The policy also changes field choice,
workforce and condition, so this is not a fixed-q causal experiment:

| Site | Flux 0: people / q | Flux .65 | Flux 1.30 |
| --- | ---: | ---: | ---: |
| Fertile open | 99 / .35 | 153 / .46 | 157 / .47 |
| Woodland | 156 / .25 | 225 / .56 | 225 / .56 |
| Dry marginal | 64 / .23 | 98 / .36 | 95 / .34 |

More field access is associated with more supported people in these sites.
Doubling the coefficient is not uniformly beneficial: the dry policy reaches
a lower q and population. The equal-claim q=.25 recovery test proves the
local food mechanism; the live sweeps measure the combined feedback. The
previous 25-turn fertile-river reversal is still valid.

## A plateau with reserve debt

Woodland shows the actual food buckets. `Y` is food received that turn;
`C` is prior-cohort consumption. Fabric food is edible feedstock consumed
after the demographic plan. Values are rounded from the tracked CSV.

| Turn | People | q | Y | C | Fabric food | Reserve opening → end (goal) | Growth food / births | Result |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| 65 | 217→221 | .47 | 17,864 | 17,360 | 240 | 25,942→26,040 (26,040) | 166 / 4 | Consumption and reserve covered; partial growth. |
| 75 | 225→225 | .49 | 18,113 | 18,000 | 240 | 26,264→26,137 (27,000) | 0 / 0 | Consumption met; reserve short. |
| 160 | 225→225 | .56 | 17,977 | 18,000 | 240 | 8,218→7,954 (27,000) | 0 / 0 | Inventory hides unsustainable demand. |
| 287 | 224→223 | .56 | 17,911 | 17,920 | 0 | 0→0 (26,880) | 0 / 0; one death | Tiny repeated shortfall after reserve exhaustion. |

Food satisfaction alone would call turns 75 and 160 fully fed, but misses
the reserve debt. The woodland camp has 223 residents with rebuilding
reserves at turn 600. The same effect is visible in open and dry sites, with
small late corrections rather than a sudden collapse.

## Validation and scale

The summarizer checks each turn's conservation identity
`opening inventory + receipts = consumption + manufacture + discarded +
ending inventory` within `1e-5`, and checks `G`/`B` capacity bounds. It
requires a positive early growth window, near-zero final growth, less than
2% peak-to-final population loss, and at least 98% food satisfaction in the
last 40 turns for these three fixtures. `--land-food-only` passes 168 horizon
ecology assertions; its growth-bucket test checks zero growth before
consumption and reserve are covered, intermediate partial growth, and the
configured maximum at ample surplus.

Production turn work still follows active tiles, local claims, settlements
and planner work, rather than historical duration. The 600-turn loop is
external validation. Mature multi-camp contention, fission and RL rollout
cost remain separate scale gates. A strict requirement that goods never draw
food below the reserve goal needs an explicit food/production budget change.

## Recreate

Run from this branch in a Windows checkout. Raw files are ignored under
`TestResults/LandFoodPopulationSweep/`; the three CSVs above are tracked.

```powershell
$sdk = 'C:/Users/suyas/Documents/ChatGPT/Anthropocene/.tools/dotnet/dotnet.exe'
& $sdk build Tools/LandFoodPopulationSweep/Anthropocene.LandFoodPopulationSweep.csproj -c Release
& $sdk Tools/LandFoodPopulationSweep/bin/Release/net8.0/Anthropocene.LandFoodPopulationSweep.dll --repo . --out TestResults/LandFoodPopulationSweep/M30-LandR003-Growth600-Buckets --populations 24 --live-turns 600
foreach ($entry in @(@('0','M30-LandR003-Growth160-Flux0'),@('1.3','M30-LandR003-Growth160-Flux130'))) {
    & $sdk Tools/LandFoodPopulationSweep/bin/Release/net8.0/Anthropocene.LandFoodPopulationSweep.dll --repo . --out "TestResults/LandFoodPopulationSweep/$($entry[1])" --populations 24 --live-turns 160 --cultivation-regeneration $entry[0]
}
python Tools/LandFoodPopulationSweep/SummarizeGrowth.py
& $sdk run --project Tools/Headless/Anthropocene.Headless.csproj -c Release -- --land-food-only
```
