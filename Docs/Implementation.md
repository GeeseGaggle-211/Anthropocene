# Implementation architecture

[EcologyFoundation.md](EcologyFoundation.md) is the current mechanics contract; [ConfigurationUnits.md](ConfigurationUnits.md) defines parameter meanings. [Validation.md](Validation.md) and [Performance.md](Performance.md) distinguish measured behavior from remaining limitations. The original `DesignSpec.txt` and earlier milestone reports are historical sources, not current rules when they conflict.

## Ownership and boundaries

`Anthropocene.Simulation` is Unity-independent C# 9, compiled unchanged by Unity and the .NET 8 headless runner. `WorldState` owns generated geography, latent ecology, entities, societies, configuration and deterministic history. `Society` owns player identity, mandates, policy preferences, tradition/practice, technology and `EraProgression`. `Camp` is a local population center with people, food, demographic progress and `PopulationLedger`. Hunters identify their source settlement and society; managed herds and improvements have separate society/settlement ownership. One society can share tradition across multiple camps in data/tests; playable daughter-settlement creation is deferred.

`Ownership.Bind` is an explicit construction/load/resolution boundary. It installs shared society references; observation, projection and saving must not create owners or IDs. Compatibility accessors on Camp delegate to Society and are not separately serialized histories. `PracticeEffects` derives task efficiency from society-owned practice; future specialist proficiency can replace that policy without moving ownership again.

`OpeningTransitions` records owned transition seeds and optionally applies the existing immediate opening rewards. Cultivation/fishery booleans, the food multiplier and five starting domestic animals are isolated compatibility outputs. They are not Neolithic cultivation intensity, domestication or construction systems. Era completion belongs to society state; the opening ends when each society has completed it or has no surviving people.

## Generation

The dependency order is Geometry -> Tectonic plates -> Elevation -> Climate -> Hydrology -> Boundary rivers -> Elevation levels -> Soil & Environment -> Oases -> Vegetation & Seasonal Resources -> Placement.

Climate creates four fixed seasonal profiles. Soil moisture and hydrology precede environment classification; oasis influence precedes natural vegetation potential. Environment, current/natural vegetation and relief/elevation are independent dimensions. Food capacity is generated from the existing climate productivity formula once; vegetation changes accessible share and habitat without multiplying total productivity a second time. Elevation level 10 excludes camps, hunters and herds; marine level 0, lake freshness, coastlines and shared river topology retain their invariants.

Nearby opening herds, regional density bonuses, viable-start filtering, coarse cold-steppe habitat and disabled-by-default wild-herd fission remain explicit opening balance scaffolds. The generator is not a historical reconstruction or a complete climate/ecosystem model.

## One simultaneous generation

1. Capture the duration; bind owners; build a scoped `PlanningIndex`. Validate submitted orders against filtered observations and reject conflicting duplicates in stable order.
2. Compute herd intentions from the planning snapshot. Dispatch creates stable IDs, but the new groups cannot hunt or accept another command until the next planning phase.
3. Provision connected departures, resolve camp collisions and strategic movement once, then deposit returning stores. Movement is 1/2/1 for camps/hunters/herds; it is not elapsed physical velocity.
4. Reproduce herds using their previous food/water shortfalls and annual rates, then resolve hunting against post-movement co-location. This preserves the validated opening chronology. Herd pressure later applies to its final operating tile, with the existing adjacent freshwater source rule.
5. Build land/passive-water gathering, fishing, disconnected forage and herd claims with the preserved population/labor formulas. Materialize only their source tiles to the start of the interval. Walk ordered seasonal segments locally, integrate renewable stock and condition, allocate realized flows proportionally, and return sparse pressure/balance results.
6. Commit results by tile/entity; apply stores, provisions and unmet food/water. Compute integrated human food demand and food-limited growth, sustain residents/groups, apportion deaths, finish supply/storage and optional herd fission. Advance the clock once and finish the demographic report.
7. Reintegrate surviving groups when their source camp remains co-located; apply society progression; validate affected state and conservation; append canonical history and advance the displayed turn.

Hunting carcass food is distinct from plant biomass extraction. Pressure is derived from realized plant/fish/herd/water transfers; failed claims do not damage land. Stock/condition integration happens together inside the active-tile phase, rather than in a later full-map pass. The seasonal segments are numerical work inside one player decision, not autonomous sub-turn movement or repeated births.

## Sparse ecology and parallel execution

`EcologyMath` implements bounded exponential stock/condition updates, analytic depletion and recharge, and affine season composition. `EcologicalPressure` resolves the coupled human-accessible and non-gatherable pools plus finite freshwater. Stable claimant ordering and proportional shares preserve collection-order independence. Each affected tile returns a value-state result, transfers, pressure entries and a mass-balance record.

Quiet tiles store `LastEcologyUpdateDays` and latent stocks/condition/tier. `LazyEcology.ProjectEcology` is pure. Healthy quiet full cycles compose analytically; damaged quiet tiles recover through local ordered segments and hysteresis. `MaterializeEcology` commits that same projection before an active change. Neither visibility nor diagnostic query order can alter a fingerprint. Transient ledgers exist only for active tiles and the latest resolved turn, not as per-tile heap objects or permanent save history.

`PlanningIndex` maps occupants by tile/player/source and indexes recent extraction once. Bounded local path/supply queries and cached temporary ecology views serve the planning phase. `PhaseExecution` provides a scalar reference and optional workers for independent AI intentions, herd intentions and active-tile calculations. Workers return stable output slots; canonical barriers own entity changes, IDs, collisions and history. Worker counts, partition order and timing are runtime diagnostics outside saved state. Desktop execution uses at most four workers with a small-work threshold.

The 320-tribe configuration is benchmark-only. Dense Tile/SeasonData storage still needs profiling/compaction before that size becomes a public preset; see the measured memory and scaling limits in [Performance.md](Performance.md).

## Human/AI information and actions

`AgentObservationBuilder` constructs immutable, currently visible information for the player and each policy. It exposes exact owned populations/livestock, estimated wild/foreign herd counts and qualitative resource bands. Hidden tiles, foreign stores/populations, global logs and submitted rival orders remain unavailable. Productivity/accessibility and vegetation use projected current state.

`OrderRules` is the shared legality contract. `LegalActions` supplies masks, bounded dispatch ranges and enumerated legal candidates. `ObservationDto` and action DTOs are serializable primitive contracts at schema 7; serialization does not grant extra information. The heuristic policy consumes those filtered observations. Persistent inclinations are seeded from world/origin state, not player-number cohorts; mandates, visible opportunity, actual food history and relocation inertia determine choices. There is no culture selection or RL training.

## Presentation and persistence

`OpeningGame` owns the Unity immediate-mode menu, planning controls and worker busy guards. The renderer draws only the camera region: ground -> elevation contours -> water -> vegetation -> land use -> settlement -> occupants -> UI feedback. It uses the 23 original symbolic sprites with semantic glyph fallbacks, two contrast sets and zoom-dependent detail. Rendering and hit testing share stable occupant slots. Delayed hover uses filtered information, resets with navigation and never takes input.

Generation, resolution/AI planning and save/load run on workers. The UI shows an operation/progress screen and does not inspect partially resolved state. `SaveCodec` stores authoritative binary geography and versioned metadata in buffered gzip; loading never regenerates geography. Signature is `ANTHROPOCENE-7`, world/config/generator version 6, filename `opening-v6.anthro`; earlier formats are explicitly rejected and earlier filenames preserved. Pending orders and transient numerical diagnostics are excluded. Stored latent ecology, society/workforce state, IDs, configuration and deterministic outcome history are sufficient for continuation.

Unity EditMode tests cover engine integration and presentation contracts. The optional development-player `--anthropocene-smoke-output <directory>` route exercises the real UI helpers and captures rendered frames, saving only to its own output directory. It does not overwrite the player's normal save slot. The Screen Capture module is included for that validation route; original artwork and importer provenance are in [ArtProvenance.md](ArtProvenance.md).
