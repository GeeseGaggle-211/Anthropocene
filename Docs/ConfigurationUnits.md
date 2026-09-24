# Configuration units

The September 20 Neolithic recipe, absolute worker-time, Housing, Fabric and
expectation constants/sweeps are listed in
[NeolithicRealignment.md](NeolithicRealignment.md#centralized-calibration-and-sweeps).
Both recipe coefficients are 1.0; Housing uses one total Wood/Clay unit.

Current contract: `SimulationConfig` and `OpeningConfig.json` version 7. Serialized defaults must match the C# defaults. [EcologyFoundation.md](EcologyFoundation.md) describes the equations and [Validation.md](Validation.md) records the tests. The prototype uses abstract food/water quantities, not kilograms or litres.

| Fields | Meaning and units |
|---|---|
| `BaseDays` (91.25) | Days in one climate/profile interval; four ordered seasons per cycle. Also the reference duration for legacy consumption/production tuning. |
| `TurnDays` (7,300), `MinDays`, `MaxDays` | Player-decision duration and allowed limits, in days. Resolution captures its duration once. |
| `DaysPerYear` (365) | Converts days to annual rates. |
| `PracticeDays` (7,300) | Reference days for one unit of lifestyle/practice exposure. Changing player-turn duration does not change this reference. |
| `Neolithic.MaterialReserveHorizonYears` (20), `UsefulFinishedGoodsReserveYears` (20) | Physical reserve durations in years. Current-turn material demand adds `MaterialReserveHorizonYears` at the current turn rate; Cloth adds the finished-goods duration to current consumption. The defaults equal one 20-year reference turn, while legacy `*Turns` fields migrate at load. |
| `MajorityEligibilityYears` (200), `ForcedTransitionYears` (240) | Society-era durations in years, converted to persisted day thresholds when ownership is created. |
| `StarvationGraceTurns` (2) | **Player turns**, intentionally independent of elapsed years. This is the user's gameplay compromise. |
| `Food`, `PotentialFood`, `Freshwater`, `CarriedFood`, camp `Food` | Stocks. Human-gatherable Food is part of PotentialFood, not an extra stock. |
| Season `MaxFood`, `MaxPotentialFood`, `MaxFreshwater` | Generated seasonal capacity targets before current condition/accessibility and the optional opening adapter. |
| Season `FoodRecoveryPerDay`, `BiomassRecoveryPerDay`, `WaterRecoveryPerDay` | Daily exponential relaxation rates. These are authoritative generated/saved rates; the resolver never treats them as per-turn fractions. |
| `FoodRegeneration`, `WaterRecovery`, `OasisFoodRegeneration`, `OasisWaterRecovery` | Legacy generation inputs: dimensionless recovered fractions over `BaseDays`, converted once using `-ln(1-f)/BaseDays`. Land climate modifies the input before conversion; aquatic productivity scales the converted daily rate. They do not directly multiply stocks in resolution. |


| Aquatic productivity and upwelling | See [AquaticFoodWeb.md](AquaticFoodWeb.md): Shallows G/B=0.25; lakes G/B=0.5; Ocean=0.1. Ordinary Shallows G=45P. Land-adjacent Shallows cannot upwell; on outer-shelf Shallows, upwelling multiplies capacity by 1+4u and renewal by 1+u. Renewal inputs are 0.65/Shallows, 0.08/Ocean and FoodRegeneration/lakes, converted to daily rates before multiplying by P. |
| Aquatic condition recovery | Generator 11 uses 0.1 times the mean saved daily biomass-renewal rate, independent of terrestrial vegetation. Generator 7–10 retains its original recovery. |
| `LandCondition`, `AquaticCondition` | Normalized condition stocks, 0 to 1. Natural target is currently 1. |
| `ConditionRecoveryPerYear` (0.003093770185904377) | Annual relaxation rate: exactly `-ln(0.94)/20`, before potential modifiers. |
| `LowPotentialRecoveryMultiplier` (0.5), `HighPotentialRecoveryMultiplier` (1.5) | Dimensionless recovery multipliers; Medium uses 1. |
| `SafeUseRatio` (1), `SustainableProductionFraction` (0.5), `PressureExponent` (2) | Dimensionless safe-use threshold, conservative fraction of natural renewal, and overshoot exponent. Managed cultivation flow is added once after applying the natural-production fraction. |
| `CultivationVegetationDisplacementStart` (0.5) | Physical cultivation intensity q at which standing natural vegetation services begin to decline; retention falls linearly to zero at q=1. It does not reduce total biomass capacity or LandCondition. |
| `LandUseDamagePerYear`, `AquaticUseDamagePerYear` (both 0.03) | Annual condition-damage coefficients, applied to realized overshoot. Drinking and unmet demand contribute zero damage. |
| `GatherFraction` | Dimensionless fraction over `BaseDays`; duration conversion is `1-(1-f)^(duration/BaseDays)`. |
| `GatherPerPerson`, `HunterForagePerPerson`, `FishPerPerson` | Abstract food per participating person per `BaseDays`. Land and passive aquatic gathering share one labor ceiling. |
| `PopulationScale` | Person-count normalization in the preserved gathering claim formula. |
| `RichGatherableFraction`, `GrasslandGatherableFraction`, `AridGatherableFraction`, `TundraGatherableFraction` | Dimensionless human-accessible shares of total biomass, selected by environment and current vegetation. Rivers/oasis influence can raise the share. |
| `Consumption` | Abstract food per person per `BaseDays`; named roles do not change consumption. |
| `AnnualHumanGrowthRate` | Maximum annual exponential growth rate. After current-population consumption and reserve refill, available growth food is inverted through the integrated exponential food equation to obtain the realized rate. |
| `DemographicGrowthMultiplier` | Dimensionless local modifier on the maximum annual growth rate. |
| `GrowthSurplusRatio` | Legacy serialized surplus-support setting retained for save/config compatibility; ordered food allocation no longer uses it. |
| `GrowthReserveTurns` | Full operating-reserve target as a multiple of the current supplied population's interval consumption, bounded by local physical storage. It is refilled before growth food. |
| `Mortality` | Dimensionless attrition fraction per reference practice interval, converted with elapsed duration after the two-turn grace. Fractional deaths are carried forward. |
| `StartingReserveGenerations`, `MinimumStorageGenerations`, `MaximumStorageGenerations` | Gameplay multiples of one decision interval's consumption. Current storage interpolates from one to four intervals with climate; this is not a physical spoilage model. |
| `StartingFood` | Legacy nominal stock; generated camps receive the calculated complete starting reserve. |
| `CarryingCapacityPerHunter` | Minimum abstract food capacity per hunter. |
| `HunterCarryGenerations`, `ExpeditionProvisionTurns`, `CampProvisionReserveTurns` | Planning-horizon multiples of current interval consumption in departure/carry/reserve formulas. They deliberately remain gameplay abstractions. |
| `SupplyRange`, `CampMovement`, `HunterMovement`, `HerdMovement`, `WaterMovement` | Hex/path allowances per strategic decision. Camp/hunter/herd movement is 1/2/1, resolved once regardless of elapsed years. |
| `HuntEfficiency` | Expected animals harvested per participating hunter per `BaseDays`, before variance, contact, breeding protection and fieldcraft. |
| `HuntVariance`, `HuntBreedingReserveFraction`, `SustainableHerdGrowthRetention` | Dimensionless harvest variance and animal protection fractions. |
| `SustainableHuntPopulation` | Person threshold for preserving part of the herd's growth. Opening balance scaffolding. |
| Species `FoodConsumption`, `FreshwaterConsumption` | Abstract food/water per animal per `BaseDays`; managed animals use the same source ledgers as human ecological claims. |
| Species `GrowthRate` | Annual exponential reproduction rate. Managed reproduction uses current-generation realized nutrition after starvation; wild reproduction retains the existing capacity/fission rules. Managed herds have no domestic capacity clamp. |
| Species `FoodYield`, `MaximumHerdSize`, `MinimumAnimals`, `MaximumAnimals` | Food per killed animal; herd ceiling and generation/placement count bounds. |
| `AnimalsPerHerder` (2), `AnimalFoodPerManagedAnimal` (1) | Managed baseline capacity in animals per Herder and dimensionless renewable AnimalFood support per fully fed/managed animal. Actual food multiplies live `Consumption` by elapsed `BaseDays` intervals; AnimalFood enters the common useful-food budget. |
| `AnimalFoodNutritionExponent`, `AnimalManagementExponent` (both 1) | Dimensionless exponents applied to renewable output only. They do not alter biological reproduction nutrition. |
| `ManagedHerdDefaultsInitialized` | Optional save migration marker. Missing legacy managed-herd tuning fields receive current defaults; a present marker preserves legitimate zero values and rejects non-finite values. |
| Species environment/relief/oasis affinities, `MovementHabitatWeight` | Dimensionless habitat preferences. They do not alter elapsed movement allowance. |
| `ExperienceThreshold`, `CarryoverFloor` | Practice-point thresholds. Carryover multiplier is dimensionless. |
| Proficiency thresholds, impact reductions, yield/practice multipliers, passive aquatic practice share | Dimensionless task effects, separate from the society-owned practice histories that drive them. No independent specialist proficiency is active yet. |
| `PassiveAquaticFishingPracticeShare` (0.5) | Fraction of passive aquatic food's practice weight credited to Fishing; the remainder goes to Gathering. Does not scale harvested food. |
| `HuntingPracticeMultiplier` (3.25) | Hunting experience intensity after food-share normalization. Successful retained harvest and unsuccessful practice both use this base weight; no additional successful-hunt multiplier applies. It does not change carcass food. |
| Temperature thresholds, climate temperatures/amplitude/gradient/lapse inputs | Degrees Celsius or temperature contrasts over normalized latitude/elevation. Climate is an approximate generator, not a weather forecast. |
| Rain, moisture, runoff, evaporation, moisture decay, wind, soil and oasis influence inputs | Dimensionless generator fields/transport coefficients. They create fixed seasonal profiles and are not per-turn ecology changes. |
| Season `RegionalMoisture` / `Moisture` | Normalized 0–1 climate water balance excluding local subsidy / local soil water including river, lake and oasis subsidy. Environment and climatic vegetation use the former; land productivity, habitat and observed moisture use the latter. Aquatic food uses separate profiles. Both are saved. |
| `OasisVegetationMoisture` (0.45) | Local soil-moisture floor multiplied by oasis influence; never changes regional moisture. |
| Hydrologic vegetation reference (70), river/lake soil subsidy (0.65) | Provisional generation constants: abstract seasonal freshwater capacity for full vegetation uplift, and normalized local soil subsidy. Uplift can reach Medium; High requires climatic support. |
| `VegetationWarmthRange`, `VegetationMoistureRange`, potential thresholds and hysteresis | Temperature range, normalized moisture range and dimensionless classification/condition thresholds. Natural potential is generated once. |
| `HillsProductivity`, `HighlandProductivity`, `MountainProductivity` | Dimensionless generated productivity modifiers. Vegetation does not apply a second total-biomass productivity bonus. |
| `OasisFood`, `OasisFreshwater` | Abstract resource capacities. |
| `StartViability` (85) | Generator-8 eligibility score: percent of starting food needs supported by mean recurring gathering across four seasons, after conservative renewal, starting workforce share and each season's shared labor ceiling. Initial stocks and herd-only biomass are excluded. |
| `StartFreshwaterMinimum` (6) | Minimum accessible freshwater capacity in every season and minimum mean renewable freshwater per BaseDays; nonsaline sources only. Start placement now tests both, rather than initial stock alone. |
| Herd/oasis density, regional variation, spacing, initial-herd guarantees | Generator density/region/hex/count controls. Guaranteed nearby prey and disabled wild fission are explicit opening scaffolds. |
| `OpeningFoodMultiplier`, `InitialDomesticAnimals`, `CultivationMoisture` | Optional opening-adapter reward multiplier, animal count and placement threshold. They are not the future cultivation/domestication model. |
| `GenerationBudgetSeconds`, `GenerationBudgetMegabytes`, hover times | Diagnostics/UI budgets; they cannot influence authoritative simulation outcomes. |

The time-invariance tests hold forcing and integrated claims constant. They do not require two separate player decisions to reproduce one combined decision: relocation, starvation grace, dispatch and policy reconsideration are deliberately decision-level events.

## Generator 10 spatial units

TerrainGeneration uses continuous longitude-periodic fields; lengths are normalized planetary distances, with local rasterization for hotspot cones. PlateCount controls tectonic provinces, not the number of continents. Archipelagos uses 30 narrow crustal ribbons with varied widths, branching and transverse rift gaps. Continents/Pangaea retain their generator-9 base crust fields.

Hotspots is the active-track budget; generation also attempts two extinct tracks per active track, or five for Archipelagos. Zero disables both populations. HotspotChainLength controls trajectory duration: four movement samples per configured unit, or ten for Archipelagos; extinct trajectories add zero to three units. It is no longer an exact island count. A moving Gaussian uplift window along a correlated two-dimensional drift path creates intermittent island groups. Radius/stride scale with map width; deviations use deterministic bounded normal samples. Relative age increases by 0.18 per movement sample, with extinct tracks starting at age 2–6. Signed subsidence allows older peaks to lie below sea level; only the youngest active window adds activity. These are dimensionless procedural chronology proxies, not years, measured plate velocities or literal volcanic lifetimes. SeaLevel, Uplift, ElevationNoise and Erosion remain dimensionless elevation/smoothing inputs.

ClimateGeneration limits its atmospheric grid to 256 x 128 and advects over 48 normalized travel steps per longitude circuit, with 240 spin-up iterations per season. MoistureDecay enters a per-transport-step retention proxy as pow(MoistureDecay,0.2), not once per gameplay tile. Evaporation, OrographicRain and Monsoon modulate ocean recharge, uplift extraction and seasonal winds. Rain/Snow/Runoff remain dimensionless forcing; they are not millimetres of precipitation.

Drainage retains supplied connected depressions from 0.008 normalized depth and screens basin area against max(6,worldTileCount*0.0007). This represents limited basin retention plus geologic spillway erosion, not an arbitrary replacement of large blue lakes with dry raised plateaus. The inflow/evaporation screen uses calibrated dimensionless annual precipitation/temperature proxies. Residual filled land is audited independently. See [WorldGeneration.md](WorldGeneration.md) for exact scope and limits.
