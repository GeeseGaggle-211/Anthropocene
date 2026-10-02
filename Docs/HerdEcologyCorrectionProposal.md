# Biomass-first land ecology and herd retention valuation

Status: implemented working candidate on `features/herd-ecology-retention`,
retained while balance remains under investigation. The user selected preserving
fertile-land food benchmarks while allowing dry-land wild food to change.
On 1 October 2026 the user redirected further work to crop moisture and planner
bugs; see [AgricultureMoisturePlannerAudit.md](AgricultureMoisturePlannerAudit.md).
Those corrections are now implemented and measured in
[CropHerdBalanceValidation.md](CropHerdBalanceValidation.md), including the
requested 25/50-turn population, QoL, herd and performance comparisons.
The design illustrations below remain arithmetic previews, not external
biological calibration or acceptance of the resulting pastoral balance.

## 1. Produce biomass before human food

For each land tile and each of its four stored seasonal profiles:

```
B_s = B0 * M_s * W_s * R_tile
F_s = B_s * f_natural(tile)
```

- `B_s` is `SeasonData.MaxPotentialFood`, the total biomass capacity.
- `F_s` is `SeasonData.MaxFood`, natural human-edible capacity.
- `M_s` is local soil moisture, including existing river/lake/oasis subsidies.
- `W_s` uses the current temperature response; Ice remains zero production.
- `R_tile` uses the existing relief multipliers.
- `f_natural` is the current gatherable fraction for natural vegetation.
- `B0` is one global biomass normalization, chosen once against fertile-land
  food benchmarks. It is independent of a tile's gatherable fraction.

Remove `.12 + Moisture` from land capacity production: zero local moisture
means zero moisture-supported biomass. Gatherability changes the human-edible
portion, rather than creating additional biomass. At otherwise equal climate
and relief, changing gatherability must leave biomass unchanged.

Retain the existing climate-dependent renewal-rate response for the initial
correction. A zero capacity cannot renew positive food or biomass. This avoids
bundling a second recovery-rate calibration into the capacity correction.

Rivers and oases remain productive through soil-water subsidies; they do not
need a desert-specific exemption. In the new generation path, finish seasonal
moisture, water capacities, and natural vegetation before initializing biomass
and edible food. Remove the food-led oasis capacity override/inverse biomass
initialization from that path. Check oasis and riparian benchmarks separately.
Initialize current stocks as consistent fractions of their respective capacities.

Source changes: `WorldGenerator.VegetationResourceStage`, `FoodEcology`, and the
new-generation path through `OasisStage`. Runtime ecology and cultivation must
consume these same authoritative seasonal capacities.

### Calibration and an arithmetic preview

Record pre-change fertile-land food distributions by season, temperature,
relief, and water access. Fit one normalization to fertile-land edible food and
freeze it before measuring the other outcomes. Preserve the benchmark mean and
check lower/upper quantiles on additional seeds; do not fit per tile or biome.
Do not preserve dry-desert food by increasing its gatherable fraction.

An illustrative fit to Standard/Continents/seed 1 gives `B0 = 53,190.407` in
current game units. Its anchor is traversable Lowland/Hills, Temperate/Tropical
land with Medium/High natural vegetation, excluding rivers, lakes, oases, their
positive influence, and immediate neighboring tiles. This is not the proposed
final multi-world calibration.

| Mean quantity | Current dry desert | Proposed dry desert | Current fertile anchor | Proposed fertile anchor |
| --- | ---: | ---: | ---: | ---: |
| Biomass potential | 27,899.978 | 1,725.413 | 27,475.018 | 27,475.018 |
| Wild edible food | 697.499 | 43.135 | 3,434.377 | 3,434.377 |

The desert sample has 12,489 tiles; the fertile anchor has 16,279. The preview
uses stored climate and relief without regenerating geography, placing camps,
or resolving turns. Dry-desert capacities fall about 94%; this will affect
grazing, cropping, and viable starts. It is not a guarantee that final herd
counts rise. When temperature and relief are controlled further (all seasons
at least 22 C, Lowland only), desert biomass changes from 41,391 to 3,324 while
fertile biomass changes from 39,735 to 40,211.

The ignored reproduction artifact is
`TestResults/HerdValuation/Probe/ecology-proposal-preview.json`; the scratch
probe's `biomass` mode computes this counterfactual without changing mechanics.

## 2. Compare complete herd retention alternatives

At a labour price `w`, compare candidate retained herd sizes and their feasible
management assignments. Each candidate receives:

```
candidate value = current-turn welfare from its actual food/input receipts
                + discounted future useful production
                + discounted residual value of surviving animals
                - discounted management and replacement costs
```

The current-turn receipts include the candidate's sustained animal output and
its actual slaughter quantity. Sustained output uses that candidate's staffing,
nutrition, tool coverage, and displaced human forage. Slaughter follows the
resolver's existing timing after breeding; it is not moved ahead of grazing or
production. Consumption, reserve refill, growth, and manufacturing follow the
existing typed input and storage rules.

### Correct the liquidation counterfactual

For a simplified no-birth decision with `N` owned animals and `H` retained,
slaughter food is `C(N-H)`, where `C` respects species carcass yields. Compare
the total values of adjacent candidates, not the carcass value of the first
animals considered for retention.

For example, keeping the first 2.69 units of a 22-unit herd forgoes selling the
final 2.69 of the herd: compare sale of 22 versus sale of 19.31 in the analytical
ladder. Under the observed camp 1415 buckets, this forgone value is zero; the
current implementation instead charges 4.790637. Actual resolved counts remain
integers and follow the existing deterministic per-herd split.

There is no unconditional `keep < slaughter` rejection before labour costs and
saved alternative work are counted. Compare net alternatives. Remove the
running-minimum behavior that propagates a price based on the wrong liquidation
interval. A monotone marginal ladder may be used only where the corrected total
values justify it; verify the total-worker price search, not just herder counts.

### Price sustained production explicitly

Use an explicit finite planning horizon, initially the existing
`PlanningHorizonYears = 100`, and `DiscountRatePerYear = .01`. Document the time
origin of current-turn receipts and continuation values. Do not use field
capital life as the animal's service-life cap.

For a constant fully managed yield `y`, before labour and varying food values:

```
discounted food = y * (1 - exp(-r*T)) / r
```

Use the continuous limit `y*T` when `r=0`. At current `y=4`, `T=100`, and
`r=.01`, this is 252.848 food units versus a goat carcass's 60 or an antelope
carcass's 80. This illustration excludes costs, scarcity, nutrition, growth,
and residual value; it does not imply an unconditional keep decision.

Include supported offspring in a bounded aggregate continuation forecast.
Evaluate growth analytically until the local forage/water or management limit
binds, then a capacity-limited continuation. Break integration at capacity and
food-value bucket crossings; do not schedule annual simulation steps. Require
the extra herding and tool replacement work as offspring increase the herd.
Use existing species growth and actual breeding/nutrition rules, without adding
natural death/ageing or a mobile growth bonus absent from the resolver.

Distinguish a this-turn breeding cap used during culling from the future
breeding policy; a temporary cut is not a permanent zero-growth assumption.
Value offspring once through their later productive stock. Do not also credit
the same offspring as free food that closes a field gap. Credit future meat
offtake only if the forecast policy actually produces and slaughters it.

At the finite horizon's end, include the discounted usable residual animal
value, so retained stock does not become worthless at an arbitrary cutoff.
The continuation remains conditional on locally observed resources and a fixed
policy; it does not predict future fission, climate, trade, or technology.

### Use real food and labour alternatives

Meal food can substitute across eligible sources. Animal Fabric and Light
Housing require their actual animal-input chain: generic plant-food availability
cannot fill those buckets. Count each unit of Animal Food once. Separate
one-time stock/reserve deficits from ongoing consumption and replacement needs
in the continuation forecast.

Recompute animal food, plant displacement, feedstock envelopes, and the food
gap for the staffing candidate. Cutting nine herders to one must remove the
lost animal output from its budget, rather than preserving the nine-herder
forecast. A cheaper alternative must fund the missing meals and inputs.

Price management work and the alternative harvest/build/upkeep work with the
same labour price and a stated discount convention. Do not treat a field's
capitalized investment return as its direct harvesting productivity. Cache the
physical field ladder and apply candidate-specific gaps to that cached result;
do not run a new ecological forecast for each herd candidate.

Keep the existing seed-herder constraint explicit and distinguish a constrained
decision from the unconstrained economic preference. Do not impose a new
animals-per-resident quota or technology-dependent herding share.

## 3. Architecture and scope

- Authoritative state stays in the current seasonal capacities, finite resource
  stocks, owned herds, inventory, workforce, and committed targets. Forecast
  values and candidate previews are transient.
- Turn boundaries and the existing one-horizon resolver remain authoritative.
  Analytical continuation is a valuation forecast, not a new simulation clock.
- Generation stays O(tiles * four profiles). Target valuation setup is local,
  O(owned herds + herder steps + active claims + bounded local tiles), with
  cached field data and bounded bucket integrations. Avoid a cross-product of
  full field forecasts and herd candidates. Profile the actual implementation
  before accepting that scaling claim.
- At mature populations and in RL training, no per-animal agents, per-year
  resolution loops, remote private information, or extra global scans are
  introduced. Diagnostics do not enter observations, fingerprints, or saves.
- New ecology uses a new generator version. Existing saves restore their stored
  capacities and geography; loading does not regenerate or silently clamp them
  to the new baseline. Verify new and legacy save continuation.
- No herd acquisition/trade system or fission herd-distribution change is part
  of these two corrections. Measure daughters born herdless separately from
  camps that actually liquidate or lose a herd.

## 4. Implementation and decision report

Implement the ecology correction, then the complete candidate forecasts and
liquidation comparison, then finite-horizon continuation pricing. Validate
their individual effects before interpreting the combined outcome.

Primary audit: the existing five Standard/80 worlds (Continents 1/2/3, Pangaea 1,
Archipelagos 1), opening plus 50 follow-on turns, mortality on at elasticity 1,
fission on. Compare four variants: current baseline, ecology only, herd pricing
only, and both corrections. Seeds/topography are matched; changed viable starts
and route adoption can change the cohorts, and must be reported rather than
presented as the same original camps. Use same-state herd fixtures separately
to isolate planner behavior.

Required evidence:

- Fertile food distributions, dry-desert biomass/food distributions, river/oasis
  exceptions, and inspected ecological atlas layers across shapes and seeds.
- Start viability and natural route adoption; no repair by tribe-ID boosts.
- Herd units per resident, actual herd share of meals and animal inputs, herder
  work, growth, slaughter, pasture/water utilization, and positive-to-zero herd
  transitions. Separate original founders and fission daughters.
- Population, deaths, QoL distributions, settlements, and clothing/housing.
  Do not require preservation of a population sustained by the diagnosed
  ecological or pricing errors.
- Explainable candidate value decompositions at camp 1415 and representative
  marginal-land, surplus-food, water-limited, and animal-input-deficit sites.
- Scalar/parallel/reordered replay, finite-stock conservation, save/load,
  alternative ordering/ties, equal economic returns, and turn-duration checks.
- Matched timing, allocations, and work counters; baseline versus candidates,
  including a mature-state/RL-size workload. Keep the existing +5% performance
  target as a proposed acceptance check, rather than a claim of measured cost.

Source/test routing follows `AGENTS.md`: food/viability, ecology, herd labour,
economic automation, mortality/fission, replay, save compatibility, and atlas
checks. The proposal itself does not change the simulation or its defaults.
