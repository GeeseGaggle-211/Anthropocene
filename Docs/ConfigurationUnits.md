# Configuration units

Current contract: `SimulationConfig` and `OpeningConfig.json` version 6. Serialized defaults must match the C# defaults. [EcologyFoundation.md](EcologyFoundation.md) describes the equations and [Validation.md](Validation.md) records the tests. The prototype uses abstract food/water quantities, not kilograms or litres.

| Fields | Meaning and units |
|---|---|
| `BaseDays` (91.25) | Days in one climate/profile interval; four ordered seasons per cycle. Also the reference duration for legacy consumption/production tuning. |
| `TurnDays` (7,300), `MinDays`, `MaxDays` | Player-decision duration and allowed limits, in days. Resolution captures its duration once. |
| `DaysPerYear` (365) | Converts days to annual rates. |
| `PracticeDays` (7,300) | Reference days for one unit of lifestyle/practice exposure. Changing player-turn duration does not change this reference. |
| `MajorityEligibilityYears` (200), `ForcedTransitionYears` (240) | Society-era durations in years, converted to persisted day thresholds when ownership is created. |
| `StarvationGraceTurns` (2) | **Player turns**, intentionally independent of elapsed years. This is the user's gameplay compromise. |
| `Food`, `PotentialFood`, `Freshwater`, `CarriedFood`, camp `Food` | Stocks. Human-gatherable Food is part of PotentialFood, not an extra stock. |
| Season `MaxFood`, `MaxPotentialFood`, `MaxFreshwater` | Generated seasonal capacity targets before current condition/accessibility and the optional opening adapter. |
| Season `FoodRecoveryPerDay`, `BiomassRecoveryPerDay`, `WaterRecoveryPerDay` | Daily exponential relaxation rates. These are authoritative generated/saved rates; the resolver never treats them as per-turn fractions. |
| `FoodRegeneration`, `WaterRecovery`, `OasisFoodRegeneration`, `OasisWaterRecovery` | Legacy generation inputs: dimensionless recovered fractions over `BaseDays`, converted once using `-ln(1-f)/BaseDays`. Climate modifies the input before conversion. They do not directly multiply stocks in resolution. |
| `LandCondition`, `AquaticCondition` | Normalized condition stocks, 0 to 1. Natural target is currently 1. |
| `ConditionRecoveryPerYear` (0.003093770185904377) | Annual relaxation rate: exactly `-ln(0.94)/20`, before potential modifiers. |
| `LowPotentialRecoveryMultiplier` (0.5), `HighPotentialRecoveryMultiplier` (1.5) | Dimensionless recovery multipliers; Medium uses 1. |
| `SafeUseRatio` (1), `SustainableProductionFraction` (0.5), `PressureExponent` (2) | Dimensionless safe-use threshold, conservative fraction of renewable production, and overshoot exponent. |
| `LandUseDamagePerYear`, `AquaticUseDamagePerYear` (both 0.03) | Annual condition-damage coefficients, applied to realized overshoot. Drinking and unmet demand contribute zero damage. |
| `GatherFraction` | Dimensionless fraction over `BaseDays`; duration conversion is `1-(1-f)^(duration/BaseDays)`. |
| `GatherPerPerson`, `HunterForagePerPerson`, `FishPerPerson` | Abstract food per participating person per `BaseDays`. Land and passive aquatic gathering share one labor ceiling. |
| `PopulationScale` | Person-count normalization in the preserved gathering claim formula. |
| `RichGatherableFraction`, `GrasslandGatherableFraction`, `AridGatherableFraction`, `TundraGatherableFraction` | Dimensionless human-accessible shares of total biomass, selected by environment and current vegetation. Rivers/oasis influence can raise the share. |
| `Consumption` | Abstract food per person per `BaseDays`; named roles do not change consumption. |
| `AnnualHumanGrowthRate` | Annual exponential growth rate, reduced by recurring surplus, reserves and the integrated food budget. |
| `DemographicGrowthMultiplier`, `GrowthSurplusRatio` | Dimensionless local growth modifier and surplus support threshold. |
| `GrowthReserveTurns` | Reserve-support multiple of the resolved interval's baseline food need; a gameplay planning horizon. |
| `Mortality` | Dimensionless attrition fraction per reference practice interval, converted with elapsed duration after the two-turn grace. Fractional deaths are carried forward. |
| `StartingReserveGenerations`, `MinimumStorageGenerations`, `MaximumStorageGenerations` | Gameplay multiples of one decision interval's consumption. Current storage interpolates from one to four intervals with climate; this is not a physical spoilage model. |
| `StartingFood` | Legacy nominal stock; generated camps receive the calculated complete starting reserve. |
| `CarryingCapacityPerHunter` | Minimum abstract food capacity per hunter. |
| `HunterCarryGenerations`, `ExpeditionProvisionTurns`, `CampProvisionReserveTurns` | Planning-horizon multiples of current interval consumption in departure/carry/reserve formulas. They deliberately remain gameplay abstractions. |
| `SupplyRange`, `CampMovement`, `HunterMovement`, `HerdMovement`, `WaterMovement` | Hex/path allowances per strategic decision. Camp/hunter/herd movement is 1/2/1, resolved once regardless of elapsed years. |
| `HuntEfficiency` | Expected animals harvested per participating hunter per `BaseDays`, before variance, contact, breeding protection and fieldcraft. |
| `HuntVariance`, `HuntBreedingReserveFraction`, `SustainableHerdGrowthRetention` | Dimensionless harvest variance and animal protection fractions. |
| `SustainableHuntPopulation` | Person threshold for preserving part of the herd's growth. Opening balance scaffolding. |
| Species `FoodConsumption`, `FreshwaterConsumption` | Abstract food/water per animal per `BaseDays`. |
| Species `GrowthRate` | Annual exponential reproduction rate. Previous food/hydration shortfalls reduce reproduction; hunting follows growth, then the configured herd ceiling applies when fission is disabled (and always to owned livestock). |
| Species `FoodYield`, `MaximumHerdSize`, `MinimumAnimals`, `MaximumAnimals` | Food per killed animal; herd ceiling and generation/placement count bounds. |
| Species environment/relief/oasis affinities, `MovementHabitatWeight` | Dimensionless habitat preferences. They do not alter elapsed movement allowance. |
| `ExperienceThreshold`, `CarryoverFloor` | Practice-point thresholds. Carryover multiplier is dimensionless. |
| Proficiency thresholds, impact reductions, yield/practice multipliers, passive aquatic practice share | Dimensionless task effects, separate from the society-owned practice histories that drive them. No independent specialist proficiency is active yet. |
| Temperature thresholds, climate temperatures/amplitude/gradient/lapse inputs | Degrees Celsius or temperature contrasts over normalized latitude/elevation. Climate is an approximate generator, not a weather forecast. |
| Rain, moisture, runoff, evaporation, moisture decay, wind, soil and oasis influence inputs | Dimensionless generator fields/transport coefficients. They create fixed seasonal profiles and are not per-turn ecology changes. |
| `VegetationWarmthRange`, `VegetationMoistureRange`, potential thresholds and hysteresis | Temperature range, normalized moisture range and dimensionless classification/condition thresholds. Natural potential is generated once. |
| `HillsProductivity`, `HighlandProductivity`, `MountainProductivity` | Dimensionless generated productivity modifiers. Vegetation does not apply a second total-biomass productivity bonus. |
| `OasisFood`, `OasisFreshwater`, `StartFreshwaterMinimum`, `StartViability` | Abstract resource capacities or start-placement thresholds. |
| Herd/oasis density, regional variation, spacing, initial-herd guarantees | Generator density/region/hex/count controls. Guaranteed nearby prey and disabled wild fission are explicit opening scaffolds. |
| `OpeningFoodMultiplier`, `InitialDomesticAnimals`, `CultivationMoisture` | Optional opening-adapter reward multiplier, animal count and placement threshold. They are not the future cultivation/domestication model. |
| `GenerationBudgetSeconds`, `GenerationBudgetMegabytes`, hover times | Diagnostics/UI budgets; they cannot influence authoritative simulation outcomes. |

The time-invariance tests hold forcing and integrated claims constant. They do not require two separate player decisions to reproduce one combined decision: relocation, starvation grace, dispatch and policy reconsideration are deliberately decision-level events.
