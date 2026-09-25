# Neolithic implementation reconciliation — 20 September 2026

Historical implementation record: its seasonal cultivation barriers describe
the `c33a5b7` calibration checkpoint. Production now uses the analytical
turn-horizon resolver described in [TurnHorizonRefactor.md](TurnHorizonRefactor.md).

Authority: the five Anthropocene Drive design documents, their reconciliation and
supersession addenda, and the implementation request dated 20 September 2026.
The explicit request supersedes still-deferred recipe wording in the live QoL
document: PlantFoodPerFabricUnit=1 and HousingMaterialUnitsPerHousing=1 total
Wood/Clay, with free substitution, are mandatory configurable recipe inputs.

## Pre-edit discrepancy map

| Area | Existing branch discrepancy | Required replacement |
| --- | --- | --- |
| Cultivation | Ungated q growth, population-scaled maintenance, per-call increments, phantom farmers, one-tile expansion | Persistent E, absolute worker-time, capability/ownership gates, repeated marginal allocation |
| Food | Independent farm generator plus wild extraction and flat Cultivated multiplier | Shared ecological state, claims and demographic/reserve acceptance budget |
| Land | Vegetation-tier-only clearing modifiers, no persistent operator | Canonical wooded predicate, one operator, abandonment decay |
| Materials | Population creates Stone/Clay/Wood | Known reachable source queries, depletion and receipts |
| Manufacturing | Specialists pooled, inputs clamped after output, generic rates and invented bonuses | Recipe-specific labor, atomic inputs, useful demand, realized output-capacity tool consumption |
| QoL | Cloth wear proxy, double housing conversion/upkeep, hard shortfalls and fission | Inventory consumption, durable housing deficit, compensable expectation-adjusted utility |
| Controls | UI mutates assignments, focus and technologies | Validated replayable player/AI economic intents |
| Persistence | Non-positive QoL values replaced by defaults | Versioned migration, exact legitimate zero preservation |
| Tests | Existing Neolithic tests assert superseded behavior | Contract acceptance, deterministic simulation and Paleo regression coverage |

Deferred: automatic fission, expedition formulas, irrigation construction/yield/
withdrawal, SoilCondition, Copper tool durability, broad tool bonuses, Plough and later
agricultural tiers, and full trade/logistics. Pots now provide additive food-storage capacity
with configured duration-aware depreciation. Stone Farm Tools provide the first labor-side
field-work tier only: covered maintenance and expansion use the derived `.45/.40` efficiency
and consume realized covered worker-years. Acquiring or losing tools leaves physical q/E
unchanged. More effective expansion work can raise persisted q, allowing the existing
q-dependent ecology to raise managed regeneration. Tools add no direct Farmer harvest
or ecological yield multiplier.

## Source hierarchy and traceability

Read before editing, in `/Google Drive/Anthropocene`:

1. [Neolithic Design Concept Part I](https://docs.google.com/document/d/1ve2MCp-gQxTt-D1vgzk80bv4o5u250MvLj_0FvLQF68/edit), including the 20 September reconciliation addendum.
2. [Agricultural Technologies Part I](https://docs.google.com/document/d/1ylTWUbUzIa4cgqp9Nf8XCFd1ztoIveJS8iQP_BqNRT4/edit), including QoL/manufacturing integration.
3. [Ecology and Land-Use Foundation](https://docs.google.com/document/d/1Z5JTqIq5Kzk1QF2mc5KB3lG3-pixOVKcCnwiRaOtlfc/edit), subject to its supersession notice.
4. [Geological Resources](https://docs.google.com/document/d/1ZwRBd-Zb2_wCEScVgV8pogeQUClh9oIzqe8LB0GMA8o/edit), including economic integration locks.
5. [QoL, Rudimentary Needs and Early Urbanization](https://docs.google.com/document/d/1L9VPG-qTquVGaR_XhSYBZoeP71MjqGDBxt52lzqhmD0/edit).

Specific reconciliation/integration addenda supersede older alternatives. The
user's explicit latest 1:1 recipe instruction supersedes the still-deferred
coefficient wording retrieved from the live QoL document. Existing code and
historical repository design notes are not authority over these contracts.

## Implemented architecture

- `CultivationEconomy` owns physical E, derived q, capability checks, absolute
  worker-time, radius-one ownership, upkeep and repeated marginal expansion.
  Only actual assigned farmers operate fields. Their upkeep and expansion
  labor are disjoint. Gatherer bridge work comes only from realized terrestrial
  food gathering after Agriculture, with its lower efficiency and q ceiling.
- `LazyEcology` and `EcologicalPressure` retain one Food/PotentialFood system.
  Cultivation changes accessible capacity and bounded managed renewal; it does
  not generate inventory independently. Competing claims share the same tile
  supply. Operating farmers receive their settlement's capped food share first;
  gathering fills the actual gap. The common consumption/reserve/growth and
  manufacturing-input budget caps accepted food. Fabric adds a plant-only
  accepted-input sub-budget; ample animal/aquatic food cannot hide that shortage
  or fill that input claim. Basic consumption is always paid first, preserving
  plant feedstock only when other food covers the debit. There is no second
  food inventory. The old Cultivated flag has no flat food bonus.
- Construction and abandonment are coupled at canonical seasonal barriers,
  rather than depositing twenty years of capital after twenty years of ecology.
  Farmer upkeep/expansion and harvest claims are replanned at every barrier
  against current E. Cumulative realized harvest consumes one turn-wide budget;
  replanning does not refill either biological or plant-input acceptance.
  Abandoned E persists and decays. Canonical wooded predicates, not a High
  vegetation label alone, select woody surcharges. Clearing and timber use the
  shared bounded ecological conversion-pressure handler.
- `MaterialEconomy` queries only known, reachable, accessible Stone/Clay/Wood
  sources. Compact geological stocks remain generation/provenance budgets;
  sparse fractional `WorldState.MaterialSources` is their economic continuation,
  not an additional inventory. Shared deposit IDs prevent duplicate pools.
  Every extraction has a source, before/after balance, realized amount and
  worker-time receipt. Wood renews using the world's saved regeneration rate;
  deep mineral pools are not available to this economy.
- Material forecasts share role and tool budgets across prioritized recipes.
  Generalists run at 0.25 of the matching specialist; unrelated specialist
  labor is not pooled. Housing forecasts accept pooled Wood/Clay substitution.
  Fabric production consumes real PlantFood; animal/aquatic food cannot stand
  in for that feedstock. Material/coverage constraints bind before output.
- `NeolithicProduction` uses recipe-specific rates and atomic input debits.
  Hand Tools provide one finished output each and Regular/Improved Tools four;
  their fractional inventory is consumed by realized useful output through one
  deterministic budget shared across recipes. Better-tool productivity applies
  only to Housing. Copper retains its worker-coverage hook pending a durability
  design. Equipment made during a production phase cannot backdate capacity into
  that phase.
  Useful output alone earns normalized specialist-equivalent craft experience;
  raw stockpiling and idle capacity earn none.
- Pots use the existing desired food-reserve policy. Their target is the gap between
  that reserve and natural storage, their capacity is additive per inventory unit,
  and depreciation creates replacement demand once per resolved duration. Natural
  capacity includes the linked population; reserve appetite includes residents and
  connected hunters, while disconnected hunters do not create a second camp reserve.
- Managed husbandry uses the same Economy control surface and resident ledger. One
  Herder role is shared across domestic species at the configured baseline capacity;
  uncovered animals still consume forage and water, while managed output enters the
  common useful-food budget as AnimalFood. Current-generation nutrition is resolved
  before managed starvation, renewable output and survivor reproduction. AnimalFood
  acceptance shares the human consumption/reserve/growth and post-depreciation storage
  projection, so plant Fabric input remains provenance-constrained and no accepted
  source is later discarded solely because existing Pots depreciated.
- Housing is durable capital: depreciate once, then compute one deficit against
  the post-demography population. Replacement/growth are a decomposition of
  that deficit, not additional demand. Fabric stock is consumed on each use.
  Expectation adjusts the utility penalty, never the physical requirement.
  Food satisfaction and the water-satisfaction input multiply the compensable
  clothing/housing utility. QoL does not kill people or split settlements.
- `EconomicIntent` is a copied, validated queued plan for assignments, priorities,
  targets and cultivation focus. Player UI, policy proposals, action masks and
  the resolver use the same validation. Controls cannot directly award Irrigation
  or mutate live assignments. Observation schema is 9; owned domestic observations
  include the camp link and generation diagnostics, while foreign domestic and wild
  observations retain only filtered estimated/public fields. Resource observations
  omit hidden deposit totals and geometry.
- Save envelope is 11; resource schema remains 4. E, operator, inventory, needs,
  expectation, workforce, intents, source depletion/knowledge and receipts are
  saved. Absent legacy fields are defaulted without replacing legitimate zeros.
  Legacy q becomes E=(q/qT)^2. Legacy Houses plus partial construction convert
  once using their historical eight-person/ten-progress-unit normalization.
  Invalid numeric economic states are rejected on load.

Managed-herd defaults are serialized in `OpeningConfig.json` with the simulation
defaults: `AnimalsPerHerder=2`, `AnimalFoodPerManagedAnimal=1`, and beta/gamma
nutrition and management exponents of 1. A `ManagedHerdDefaultsInitialized` marker
distinguishes absent legacy fields from saved legitimate zero values; migration only
fills fields absent from the old envelope and rejects non-finite values. The same
marker-based rule applies to zero diagnostic nutrition, management, yield and
starvation remainder fields. The shared useful-food projection clamps unavailable
plant Fabric input inside the growth search, disables growth during starvation
recovery, and projects the one scheduled Pot depreciation before sizing the common
reserve. Focused managed-husbandry checks cover the four-Weaver birth threshold,
recovery, post-depreciation storage acceptance and no-discard food conservation.

## Centralized calibration and sweeps

Existing cultivation fields live in `SimulationConfig`; new recipe, sourcing,
capital and QoL fields live in `SimulationConfig.Neolithic` / `NeolithicConfig`.
Serialized production defaults are in `Resources/OpeningConfig.json`.

| Lever | Baseline | Sweep / interpretation |
| --- | --- | --- |
| Reference population / tiles / years / intensification turns | 250 / 7 / 20 / 10 | 7142.857 worker-years per tile E |
| ReferenceIntensity / MaintenanceCoefficient | .4 / .4 | 14.2857 operating workers per tile E before vegetation factors |
| FarmerProductivity / CultivationRegenFlux | 1.5 / .65 | Distinct harvest-labor and ecological-flow factors |
| GathererBridgeCeiling / Efficiency | .25 / .50 | Open-build cost for low-q tending |
| AllocationStepE / PlanningHorizonYears | .01 / 100 | Re-evaluate marginal candidate after every increment |
| DecayHalfLifeYears | 80 | 40–160; exact exponential E decay under fixed operating fraction |
| Open / Woodland / Forest build factors | 1 / 1.3 / 1.5 | Canonical environment-plus-vegetation predicate |
| Open / Woodland / Forest maintenance | 1 / 1.1 / 1.2 | Same canonical predicate |
| GeneralistTaskEfficiency / SpecialistMaterialExtractionRate | .25 / 1 | Material units per worker/BaseDays |
| MaterialReserveHorizonYears / UsefulFinishedGoodsReserveYears | 20 / 20 | Physical reserve durations; current-turn demand is added separately |
| PlantFoodPerFabricUnit | 1.0 | Exactly one plant Food per Fabric |
| HousingMaterialUnitsPerHousing | 1.0 | One total Wood/Clay unit, not one of each |
| FabricConsumptionPerPersonTurn | 1.0 | {0.25, 0.5, 1, 2, 4}; turn means 20 reference years |
| WeaverFabricOutputPerBaseDays | 1.0 | 80 per 20 years; generalist 20 |
| BuilderHousingOutputPerBaseDays | .03125 | 2.5 per 20 years; suggested 1.5–4.0/20 years |
| Hand / Improved / Copper Housing multipliers | 1 / 1.5 / 2 | Improved 1.25–1.75; Copper 1.5–2.5; recipe-local |
| HousingPeoplePerUnit | 4 | {3, 4, 5, 6} |
| HousingDecayPerTurn | .10 | {.05, .10, .15, .20}; duration-scaled exponential retention |
| Rudimentary clothing / housing weights | .5 / .5 | Sum to one; compensable, no hidden hard gate |
| RudimentaryExpectationFloor / RampYears | .25 / 100 | Floor 0–.40; ramp 60–160 years |
| ToolCoveragePerWorker / CraftMasteryThreshold | 1 / 400 | Legacy Copper worker coverage; useful specialist-equivalent work |
| ClearingConversionPressure / TimberConversionPressure | 1 / 1 | Explicit provisional integrated hazard per realized converted/extracted fraction; not a hidden material cost |

At baseline, housing replacement needs 0.1/(4*2.5)=1% of population as Builders
(4% as generalists). Required housing is N_next/r_h; after-decay stock is
H_t*(1-d_h) for a reference turn, and demand is their nonnegative difference.

## Verification and boundaries

Implementation revisions: `89507bd1a9430f463d428deab79f4ae5afaefc84`
(realignment, validated intents, persistence and tests) and
`aef585738a3fae21b692827160ca096da0a3a555` (depopulated-field reclamation).

The headless focused runner `--neolithic-only` now has 209 assertions. Coverage
includes capability/q-zero gates, exact recipes, role efficiency, input/tool
constraints, graded QoL, all listed Fabric/Housing sweep combinations,
parameterized legal masks, invalid/zero save states, legacy migration, shared
tool-grade budgets, no-source extraction, finite depletion, source receipts,
abandonment, wooded predicates and overlapping field ownership.

Time checks compare twenty years against one-year partitions under fixed
forcing, including combined construction/decay/ecology at seasonal boundaries.
They also cover a constant assigned workforce with default maintenance: growing
fields increase upkeep during the generation, reducing expansion accordingly.
They do not claim continuous within-season crop physiology. The seed-101
five-generation economic smoke agrees between scalar and parallel execution
with reload after each generation: population 100, maximum q 0.11220501, QoL 1,
60 extraction receipts, no discarded useful food and no excess workforce.

The complete headless regression with three seeded opening simulations passed:
8,948 geology assertions plus the core/Paleo, climate, morphology, hydrology,
aquatic, preset, ecology, agent and parallel replay suites. Three seeded games:
12/12 survivors, zero starvation deaths, zero illegal orders, net growth 874.
After the final usable-Copper-demand and absent-field migration corrections,
`--economy-only` passed again with all 209 Neolithic assertions, including
reclaiming a depopulated settlement's fields without erasing capital. Added fields
are optional in the serialization contract; defaults apply only when a member
is absent. Explicit zero priorities survive. Impossible legacy pre-Agriculture
jobs are released to real gatherers without changing population.
The Fabric feedstock regression starts with full biological reserves consisting
entirely of animal food and zero plant stock. It gathers exactly 100 plant Food,
produces/consumes 100 Fabric, retains the 8,000-unit food reserve, and records
zero harvest waste. Without available output capacity it creates no speculative
input demand.
Unity EditMode: 26/26 passed. The Windows
development player builds in a separate Neolithic output directory.

Regional food compatibility is tested with a 5,000-person town and zero local
food productivity: previously delivered food is debited and retained through
the same ordinary inventory/reserve budget. Actual trade routing is not added.
Likewise, the QoL layer consumes the bounded water-satisfaction input; no new
volumetric human drinking or irrigation calibration is invented by this pass.
The existing model defaults that water input to full satisfaction.

The opt-in `--anthropocene-neolithic-smoke-output` player route is a clearly
synthetic Agriculture fixture on generated geography. It exercises queued
economic controls, normal resolution, Fabric accounting, E growth, deferred
Irrigation and isolated save/load, and captures the actual settlement panel.
It does not use the user's save slot, establish natural Neolithic balance, or
substitute for a human usability test.

The player route passed: all three captures exist; resolved and restored panels
were visually inspected, with no overlapping or clipped controls. Fabric
production-minus-consumption reconciled, queued plans left live state unchanged,
and separate save/load restored an identical authoritative fingerprint. The
player log contained no gameplay exceptions (only a graphics debug-interface
availability warning). Native mouse interaction was not used for this check.
