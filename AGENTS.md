# Working on Anthropocene

## Intent and current scope

Anthropocene is a deterministic, simultaneous-turn strategy simulation. The playable opening follows mobile camps and hunter groups through the Paleolithic to their first technological breakthrough, normally within twelve turns. Prioritize information a player can act on: readable Dwarf Fortress-style symbolic tiles with glyph fallbacks, clear terrain and resource distinctions, understandable orders, and explained outcomes. Decorative art is secondary.

Use the source map below to locate the relevant subsystem before reading whole files. Read deeper documentation according to the task: `Docs/WorldGeneration.md` and `Docs/ArchipelagoRefinement.md` for generation; `Docs/EcologyFoundation.md`, `Docs/AquaticFoodWeb.md` and `Docs/ConfigurationUnits.md` for mechanics and tuning units; `Docs/Playtesting.md` for presets; `Docs/ArtProvenance.md` for assets; `Docs/Performance.md` for scaling. `Docs/Implementation.md` explains cross-system architecture and `Docs/Validation.md` records tested behavior and limitations. There is no need to load every document for a localized change. `Docs/DesignSpec.txt` preserves the original Google Drive specification; do not silently rewrite that historical source. Earlier milestone notes contain superseded rules. Current user instructions take precedence over these documents.

The default player turn is twenty years, resolved through seasonal economic intervals. Earlier references to ninety-day player turns and four elevation bands are historical. A complete Neolithic era, trade, diplomacy, warfare, construction and RL training are outside the current implementation.

## Repository map

`Anthropocene.Runtime` references `Anthropocene.Simulation`; the simulation assembly has no Unity engine references. The Editor-only test assembly references both. The UI uses immediate-mode `OnGUI`, not a scene-authored Canvas hierarchy. Most gameplay lives in plain C#; `SampleScene.unity` is the build scene, not the simulation composition root.

In the tables below, simulation filenames are relative to `Assets/_Anthropocene/Simulation/`, and presentation filenames are relative to `Assets/_Anthropocene/Scripts/Runtime/Presentation/`.

### Simulation source map

| Concern | Files and entry points |
| --- | --- |
| Authoritative state and tuning | `Model.cs`: `WorldState`, `SimulationConfig`, `Tile`, `SeasonData`, camps/hunters/herds, orders and turn records; `Numbers` supplies keyed randomness. |
| Ownership and progression | `Societies.cs`: `Society`, `EraProgression`, `Ownership.Bind`, `OpeningTransitions`. `Workforce.cs`: `PopulationLedger`. Breakthrough eligibility is in `TurnResolver.cs`. |
| Geometry and geography | `HexMap.cs`: wrapping axial coordinates, neighbors, distance and reachable tiles. `Hydrology.cs`: `Geography`, river edges and `BoundaryRiverStage`. `ElevationLevels.cs`: gameplay levels. There is no separate `Geography.cs`. |
| Generation orchestration | `WorldGenerator.cs`: `WorldGenerator.Generate`, stage interface/context/report, geometry, ecology, seasonal resources and `PlacementStage.Viability`. |
| Geological resource state and tuning | `ResourceCatalog.cs`, `ResourceModel.cs`: stable material IDs, access/discovery metadata, compact authoritative state and per-generation options. `GeologicalTuning.cs`: validated province, deposit, surface and placer levers. `GeologicalProfiles.cs`: host/history eligibility and material composition. `ResourceCodec.cs`: separate resource save block. Read `Docs/GeologicalResources.md`. |
| Geological generation stages | `GeologicalGeneration.cs`: province fields and marine settings. `GeologicalDeposits.cs`: finite body candidates. `GeologicalFootprints.cs`: spatial clipping, connectivity and exact share allocation. `GeologicalSurface.cs`: Stone/Clay and renewable Wood capacity. `GeologicalPlacers.cs`: drainage/shelf transport and finite source transfers. `GeologicalAudit.cs`: start-opportunity diagnostics and resource validation. |
| Terrain, atmosphere and drainage | `TerrainGeneration.cs`: noise, plates, continental/rifted crust and moving hotspot tracks. `ClimateGeneration.cs`: `ClimateStage`. `DrainageGeneration.cs`: `HydrologyStage` basin routing/lake retention. |
| Habitat and resource profiles | `Environment.cs`: environment/vegetation classification and legacy terrain projection. `FoodEcology.cs`: gatherable share/potential. `AquaticEcology.cs`: marine/lake productivity. `FreshwaterAccess.cs`: actual drinking source. `OasesAndHerds.cs`: oasis stage and wild-herd placement; `Species.cs`: species definitions. |
| Turn orchestration | `TurnResolver.cs`: `Resolve`, movement, hunting, breakthrough and invariants. Orders are validated and copied before authoritative changes. |
| Seasonal economy and demographics | `GenerationalEconomy.cs` is the other **partial `TurnResolver`**: active resources, integrated demand, growth and sustain. **`GenerationEconomy.cs` is a different file**, containing `PracticeEffects`, `DemographicReport` and `PassiveGathering` helpers. |
| Stocks and regeneration | `LazyEcology.cs`: pure `ProjectEcology` versus committing `MaterializeEcology`. `EcologicalPressure.cs`: coupled claims/realized flows. `EcologyMath.cs`: analytic stock/condition integration. |
| Logistics and phase execution | `Logistics.cs`: `Supply`, carried food, departures and returns. `PlanningIndex.cs`: scoped occupancy/history/projection caches. `PhaseExecution.cs`: scalar/worker mapping and `AiPlanning.Decide`. |
| Information, AI and orders | `Agents.cs`: observations, `AgentObservationBuilder`, `HeuristicTribePolicy` and decision traces. `ObservationContract.cs`: serializable observation DTOs. `ActionContract.cs`: `OrderRules`, masks and `LegalActions`. `HerdCountKnowledge.cs`: count estimates. |
| Persistence and fixtures | `SaveCodec.cs`: `Save`, `Load`, `Fingerprint`, `ConfigurationHash`. `PlaytestScenarios.cs`: preset configuration, habitat checks and ownership-only start selection. |

### Presentation and tooling source map

| Concern | Files and entry points |
| --- | --- |
| Startup and lifecycle | `Assets/_Anthropocene/Scripts/Runtime/Core/GameBootstrap.cs`: `CreateBeforeFirstScene` creates persistent root and attaches `OpeningGame`. `OpeningGame.cs`: menu, world/task ownership, `NewGame`, `EndTurn`, `Refresh`, save/load and main drawing. |
| Planning and inspection | `OpeningPlanning.cs`: orders, movement paths and supply previews. `OpeningDetails.cs`: inspector and delayed hover. Simulation-side `HoverDetails.cs` supplies `TileDetails` and stable visible occupants. |
| Map rendering | `Tilesets.cs`: `TilesetCatalog` and `LayeredTileRenderer`, semantic layers and occupant slots. `OpeningWorldPreview.cs`: preset menu and explicitly global developer atlas. `OpeningResourcePreview.cs` / `WorldResourcePreview.cs`: resource selector, inspector and pure shared-quantity rasters. |
| Player verification | `OpeningSmoke.cs`: opt-in normal/preset/atlas smoke routes. `OpeningArtReview.cs`: synthetic fixtures through the production renderer. |
| Build and import | `Assets/_Anthropocene/Editor/PrototypeBuild.cs`: `PrototypeBuild.Windows`. `Assets/_Anthropocene/Editor/SymbolicTextureImport.cs`: sprite import settings. |
| Headless checks | `Tools/Headless/Program.cs`: fixed checks, CLI switches and seeded reports; `Tools/Test-Headless.ps1` drives it. `Tools/WorldAtlas/Export-Worlds.ps1` exports actual generated geography, metrics and optional separate saves. |
| Spawned-tribe resource audit | `Tools/ResourceAccess/Measure-Starts.ps1` generates five Standard/80-tribe worlds and measures nearest land/geometric sources and local accessible stocks without changing state. `Summarize.py` produces per-tribe and aggregate CSV; methodology/results are in `Docs/SpawnResourceAccess.md`. |

The `Opening*.cs` UI files share one partial `OpeningGame` class; they are not independent components. Keep changes in the appropriate partial file.

- `Assets/_Anthropocene/Resources/OpeningConfig.json`: simulation tuning. Keep serialized values and `SimulationConfig` defaults consistent when changing defaults.
- `Assets/_Anthropocene/Resources/ResourceGenerationConfig.json`: per-generation resource overrides, including `MaximumQuantity` (default 100,000; 1,000–int.MaxValue supported). The player accepts `--anthropocene-resource-maximum`; the atlas script accepts `-ResourceMaximum`. New limits affect new worlds, never loaded saves.
- `Assets/_Anthropocene/Resources/Tilesets/`: classic and high-contrast semantic symbol definitions with glyph fallbacks. Both must convey the same gameplay information.
- `Assets/_Anthropocene/Resources/MapArt/`: 29 original cartographic sprites; preserve GUIDs, alpha and semantic layers. `Tools/Art/Build-SymbolicAssets.cjs` recreates PNGs and SVG exports in `Tools/Art/Sources`; provenance and the approved visual target are in `Docs/ArtProvenance.md`. Judge art in the actual Unity render, not only a source contact sheet. Never add opaque occupant backplates or flatten coloured sprites with glyph tint.

### Execution and data flow

1. `OpeningGame.NewGame` loads defaults plus `OpeningConfig`, applies menu/preset settings, and generates on a worker. `WorldGenerator.Generate` validates config and runs Geometry -> Plate -> Elevation -> Climate -> Hydrology -> BoundaryRiver -> ElevationLevel -> Ecology -> Oasis -> VegetationResource -> GeologicalProvince -> MineralDeposit -> SurfaceMaterial -> Placement -> ResourceAccessAudit, then binds owners and validates. `CreateStages(false)` shares the resource pipeline with geography-only exports while omitting Placement. The independent resource overlay changes no original geography, ecology or entity IDs.
2. `OpeningGame.Update` adopts completed generation/load results. `Refresh` builds player 0's filtered observation. Planning edits queued orders; it does not resolve the world. `AiPlanning.Decide` builds each policy's filtered view, and `EndTurn` substitutes the human's submitted orders for player 0 before worker resolution.
3. `TurnResolver.Resolve` locks legal orders, computes herd intentions, dispatches, provisions departures, moves and deposits returns. Its economy partial handles reproduction/hunting, seasonal claims, realized resource flows, sustain and growth. Reintegration, progression, invariants and canonical history finish the turn. `Update` refreshes observations after completion.
4. Save/load use `SaveCodec` on workers and the same busy guards. Loading restores geography and latent ecology directly. `Fingerprint` supports deterministic replay comparisons; timings and transient diagnostics are not authoritative inputs.

`WorldState.Tiles` is a dense array indexed by tile ID; entity IDs come from `NextId` and must not be treated as list positions. `HexMap` wraps longitude but bounds latitude; use its path/distance helpers. `Days` is elapsed simulation time and `Turn` counts player decisions. Stored seasonal potential is not current stock: readers project latent ecology at the current clock. `Camp.Population` counts residents; `LinkedPopulation` also includes its hunter groups, even disconnected ones. `Camp.People` exposes shared society state through compatibility accessors, not a second technology owner. `Terrain` is a compatibility projection; new habitat logic should use explicit environment, water and vegetation fields.

### Route changes to validation

Headless check files below are in `Tools/Headless/`. The runner compiles the actual simulation `*.cs` files with .NET 8/C# 9. New check files need an explicit entry in `Anthropocene.Headless.csproj` **and** invocation from `Program.cs`; new simulation subdirectories also need project inclusion because the source wildcard is not recursive.

| Change | Relevant checks and additional verification |
| --- | --- |
| Terrain/climate/drainage/hotspots | `ClimateGenerationChecks.cs`, `DrainageGenerationChecks.cs`, `WorldMorphologyChecks.cs`, `ArchipelagoChecks.cs`; broad geography audits and exported atlas inspection. |
| Resource generation and maps | `GeologyGenerationChecks.cs`: overlay/legacy equivalence, source conservation, connected bodies, marine fixtures, profile independence, discovery boundaries, save continuation and budgets. `--geology-audit --large` runs six seeds in all shapes plus a 2048×1024 resource/save audit; inspect real maps from `Tools/WorldAtlas`. |
| Food, water, vegetation or viable starts | `ClimateMoistureChecks.cs`, `AquaticChecks.cs`, `StartViabilityChecks.cs`, `ScenarioChecks.cs`; remeasure presets when their inputs change. |
| Economy, ownership, growth or regeneration | `FoundationChecks.cs`, `EcologyChecks.cs`, `Milestone3Checks.cs`; conservation/replay and relevant balance samples. |
| AI, orders, information or workers | `AgentContractChecks.cs`, `ExecutionChecks.cs`, existing `Milestone2Checks.cs`; scalar/parallel and collection-order replay. |
| Save fields or versions | Extend affected subsystem round-trip/rejection checks; verify continuation fingerprints and old compatible versions. Update `SaveCodec`, defaults/versions and format documentation together. |
| Unity/UI/art | `Assets/_Anthropocene/Tests/EditMode/` contains `GameBootstrapTests.cs`, `SimulationTests.cs`, `LayeredMapTests.cs`; rebuild and inspect the actual player when presentation changes. |

Start with the relevant symbols and check methods rather than dumping all of `Model.cs`, `Agents.cs` or the test runner. Keep this navigation map current when moving responsibilities or adding entry points; put long explanations and measured reports in `Docs/`.

## Preserve these contracts

- Keep Unity APIs out of the simulation assembly. The headless project targets .NET 8 with C# 9; simulation changes must compile there and in Unity.
- Preserve seeded determinism, stable entity IDs, sorted resource allocation and collection-order-independent replay. Use the existing keyed random streams instead of wall-clock randomness. Timings and UI state must not affect authoritative results.
- Player details and AI decisions consume filtered observations. Do not expose hidden tiles, foreign private stores/populations, global event logs or rival submitted orders. Wild herds and foreign livestock use `HerdCountKnowledge` estimates; only owned livestock counts are exact. Keep derived AI categories consistent with those estimates.
- Continuous height, Environment and natural/current Vegetation are independent of gameplay elevation. Marine tiles are level 0, traversable land and freshwater lakes are levels 1–9, and level 10 blocks movement and placement. Preserve freshwater, river-edge and coastline invariants.
- Society owns technology, tradition, policy preferences and era progression; camps own local people, stores and workforce. Bind owners at construction/load/resolution boundaries, never from a read path. Mastery can complete the opening early; majority eligibility starts at 200 elapsed years and forced completion at 240. Two player turns of starvation grace are deliberate.
- Renewable ecology is sparse and expressed in days/years. Human food G is part of total biomass B. Only realized flows cause pressure; finite freshwater is debited at its actual source and drinking does not damage aquatic condition. ProjectEcology must be pure; MaterializeEcology commits that projection. Persist latent timestamps and vegetation hysteresis; do not scan quiet tiles every turn.
- Human food is allocated in order to current-population consumption, full configured operating-reserve refill and then exponential growth; births occur afterward. Growth retains fractional progress and deaths remain separately recorded. Do not introduce an arbitrary human population cap. Maintain population and food conservation and the persisted demographic explanation.
- Passive coastal gathering uses adjacent Shallows and shares the camp's land-gathering labor. Ocean is excluded and marine food never supplies freshwater. Remote hunters retain finite carried provisions and respect traversable supply paths.
- Passive aquatic practice defaults to 50/50 Gathering/Fishing. Successful retained hunting and unsuccessful practice both use the base `HuntingPracticeMultiplier`; there is no additional successful-hunt experience bonus. Harvest food is unchanged. Saved passive-share settings remain authoritative.
- Keep ground, merged elevation contours, water, vegetation, land-use, settlements, food markers and occupants independently legible. Equal elevation buckets suppress their shared contour edges; unknown neighbors must not leak elevation. Stacked units use stable slots shared by rendering and hit testing; sprite bounds must stay within those slots; overflow remains accessible in the inspector. Delayed hover must reset with navigation, use visible information only and never intercept input.
- Vegetation detail must show no trees for Low (grass/brush only), exactly one tree for Medium and three for High across land climates. Preserve those counts at all detail zooms; omit vegetation at distant zoom. Decorative feature art must not add trees that confuse this state indicator.
- Independent AI/herd intentions and active ecology may use bounded parallel workers; compare against the scalar reference with varied partitions/order. Commit IDs, collisions, entity changes and history canonically. Runtime worker settings and timings must not enter authoritative state.
- Generation, turn resolution and save/load use workers. Do not read or mutate partially resolved world state from the UI; preserve the busy/progress guards.
- Classify environment and climatic vegetation from seasonal `RegionalMoisture`; use local `Moisture` for land productivity, recovery and habitat. Rivers/lakes/oases modify local soil without reclassifying climate. Natural hydrologic uplift reaches Medium; High requires regional support. Keep both profiles deterministic and persisted, and preserve productive dry river valleys.
- Aquatic profiles use `AquaticEcology` independently of soil: Shallows have High plankton/algae and 25% human-harvestable biomass; lakes retain 50% accessibility; Ocean has Low plankton, 10% accessibility, more biomass and slower renewal. Land-adjacent Shallows cannot upwell. Outer-shelf Shallows with no land neighbor gain up to 5x biomass and 2x shallow renewal from adjacent Ocean. Lakes keep separate climate forcing and finite freshwater. Generator-11 aquatic condition recovers from saved biomass turnover and drives plankton hysteresis; never apply terrestrial Low-vegetation recovery to new fisheries. No land trees on water. Ocean remains excluded from opening harvest/viability. Read `Docs/AquaticFoodWeb.md`; these are procedural food-web/upwelling proxies, not physical currents.
- Viable starts must use all four seasons of renewable human food, the starting workforce and shared land/coastal labor limit, plus freshwater capacity in every season and mean recharge. The default score threshold 85 is percent of starting food need. Initial stocks, inaccessible biomass, Ocean and peaks cannot inflate eligibility. Keep spacing and reachable-prey checks.
- Save/load must restore authoritative geography and history without regeneration. Resource-bearing saves use `ANTHROPOCENE-9`; resource-absent `ANTHROPOCENE-8` saves remain readable and retain their original envelope when saved. Filename `opening-v7.anthro`, world/config 7, core generator 11 and observation schema 7 remain unchanged. Resource generator is 2; schema 2 is retained for caps up to 100,000, and larger caps use schema 3 with a persisted compact surface unit. Version-1/2 resources retain their exact original payload; the explicit block persists full quantities, component/site allocation, provenance and access/discovery metadata. Never attach resources implicitly on load. Generator-7 through generator-10 saves retain original ecology and aquatic recovery. Earlier moisture formats and future versions remain rejected. Preserve older user save files.
- Geological resources are generation/developer-map state only. Quantities use a configurable per-body/surface-source cap (default 100,000; ResourceGenerationConfig.json or ResourceGenerationOptions.MaximumQuantity); multi-tile bodies share one finite reserve. Stone/Clay access starts at 100%, Wood at 25%; later families use lower saved base fractions. Wood capacity is separate from edible biomass and carries an annual renewal coefficient, but harvesting/economy integration is deferred. Discovery requires the relevant material technology (CopperWorking for copper); no latent ores enter current player/AI observations. Resource generator 2 adds low-quantity scattered fields to eligible land-solid profiles, concentrated rich cores and source-funded placer reaches. Scattered fields may be disconnected; rich deposits remain connected. Preserve exact shared budgets and independent keyed streams. Do not repair resource shortages by altering placement or geology around camps.

## Build and validate

For rebuild requests or Unity licensing failures, read `Docs/UnityBuildRecovery.md` first. It records the verified graphical build with the observed Hub licensing pipe, safe process inspection without exposing tokens, handling of the separate Git checkpoint-ref failure, and player verification. Do not repeat stalled batch launches or install Unity MCP as a licensing workaround.

Use Unity **6000.5.10f1**, matching `ProjectSettings/ProjectVersion.txt`. Open `Assets/Scenes/SampleScene.unity` and press Play. Do not launch concurrent Unity editor/batch instances against the same checkout. Check the process project path before closing an editor; other checkouts may be running.

From the repository root in PowerShell:

```powershell
# Focused simulation checks without the seeded benchmark batch
./Tools/Test-Headless.ps1 -Seeds 0

# Broader simulation, replay, generation and Standard-world benchmark
./Tools/Test-Headless.ps1 -Seeds 30 -Standard -Geography

# Standard-world elevation, viable-start and save/load audit
./Tools/Test-Headless.ps1 -Seeds 0 -Audit

# Habitat-segmented Paleolithic balance report
./Tools/Test-Headless.ps1 -Seeds 30 -BiomePaths

# Representative 32-tribe balance sample; shares are soft targets
./Tools/Test-Headless.ps1 -Seeds 3 -NomadStandard

# Scalar/parallel replay and scale measurements (320 is benchmark-only)
./Tools/Test-Headless.ps1 -Seeds 0 -ScalePlayers 80
./Tools/Test-Headless.ps1 -Seeds 0 -ScalePlayers 320
```

This workstation's authoritative checkout is `C:\Users\suyas\Documents\ChatGPT\Anthropocene`. Run implementation, Git operations, headless checks and documentation work directly from this Windows checkout. Do not route routine repository work through WSL or maintain a second authoritative checkout. `Tools/Test-Headless.sh` remains available for portability on other hosts but is not the current workstation workflow.

GitHub authentication on this workstation is intentionally file-backed because Windows Credential Manager and the Schannel transport proved unreliable. Git uses the OpenSSL transport. Never print credentials, paste tokens into prompts, or commit them. Confirm unattended access with `gh auth status` and a normal fetch or push; if Git opens a credential GUI, stop and repair the CLI configuration instead of continuing through the dialog.

The script finds the local or Unity-bundled .NET SDK, then falls back to `dotnet` on PATH. Run checks appropriate to the change: focused simulation checks for mechanics, the broader suites for generation/determinism/performance changes, and Unity EditMode tests for engine integration or presentation changes. Do not repeat unchanged expensive suites without a reason. The recorded baseline is 437 fixed headless assertions plus seeded/audit checks, scalar-versus-2/4-worker replay through opening completion (eight resolutions in the latest run), and 20 Unity tests; counts can legitimately increase. `Docs/Performance.md` records the 80/320 results. Do not expose 320 tribes as a public preset before profiling and compacting dense tile/season memory; the measured single-world heap is about 852 MiB after generation.

Run EditMode tests in Unity's Test Runner. For batch runs, use the Unity executable with `-batchmode -nographics -projectPath <absolute-root> -runTests -testPlatform EditMode -testResults <absolute-xml-path> -logFile <absolute-log-path>`; let the test runner exit rather than adding `-quit`. Create the output directory first and inspect the XML result, not just the process exit code. Launch background batch processes hidden on Windows.

Build through **Anthropocene > Build Windows prototype**, or use `-batchmode -nographics -quit -projectPath <absolute-root> -executeMethod Anthropocene.Editor.PrototypeBuild.Windows -logFile <absolute-log-path>`. Close the player from this checkout before replacing its build. Output is `Builds/Windows/Anthropocene.exe`. Inspect the build log for success and smoke-test UI changes in the player. On this workstation, if the headless editor stalls while reconnecting to `Unity.Licensing.Client`, stop that exact batch process and invoke the same `-quit -projectPath ... -executeMethod ... -logFile ...` command through the authenticated graphical editor without `-batchmode -nographics`; that route produced the validated build. Never launch a second Unity instance against the checkout while diagnosing licensing.

`Open-Anthropocene.ps1` pulls with `git pull --ff-only` before opening Unity. Use it when synchronizing is intended; open the editor directly for ordinary local iteration.

## Reproducible interactive demo

Choose a named playtest preset (Standard / Continents / seed 11 / 80 tribes) to compare a desert river, open steppe or cold fishing coast. The normal starting population is 24 with one complete starting-generation reserve. Dispatch eight people, commit, then select the hunter stack and plan movements within visible terrain. The automated Standard/Continents/1729/32 demo finds a deterministic traversable two-hop route four tiles from camp and previews its supply disconnection; do not assume the old generator-8 eastward coordinates still apply.

Use V to compare tilesets, the wheel to zoom, arrows to pan and C to center the camp. Check exact slot selection, vegetation/detail reduction with zoom, hover after more than two stationary seconds, coastal saltwater/food information, and save/load restoration. The development-player argument `--anthropocene-smoke-output <absolute-directory>` runs an automated version of the demo and captures frames using a separate save in that output directory; it is not a substitute for a human interaction/usability playtest. The visual check needs a visible game window on this workstation; hidden players cannot capture the backbuffer. It also captures clearly labelled synthetic art fixtures through the production renderer. Run `Tools/Art/Check-PlayerArt.cjs` with Node, the Sharp package directory, and the capture directory to verify the selected-camp transparency probes and complete capture set. Preserve any existing user save when setting up a new demonstration.

## Follow-on workflow

- Inspect `git status` before editing and preserve existing work. Do not revert settings or source files wholesale to HEAD just to remove apparent noise; Unity can reserialize settings while other edits already exist.
- Keep Unity `.meta` files with their assets and preserve existing GUIDs. Keep third-party packages outside `_Anthropocene`; document provenance/licensing for added assets and use only dependencies needed by the task.
- Build output, `Library`, logs, local SDKs and `TestResults` are intentionally ignored. Commit source, configuration, metadata, tests and relevant documentation. Do not force-add generated artifacts to satisfy a documentation link.
- Update the current mechanics and validation notes when behavior changes. Distinguish automated coverage from manually observed UI behavior, and record benchmark seed/configuration when reporting performance.
- The post-allocation/offshore-upwelling Standard/80 regression uses seeds 1–3 in every shape and retained 720/720 tribes: Continents 75 Husbandry / 164 Cultivation / 1 Watercraft, Pangaea 91 / 144 / 5 and Archipelagos 75 / 136 / 29. Archipelago Watercraft P25/median/P75 population is 110/138/199. No camp footprint can upwell; six of 29 Watercraft tribes later harvested outer-shelf upwelling after unlocking, accounting for 6.7% of their aquatic food. These are regenerated cohorts, not technology quotas or a fully site-matched causal comparison. See `Docs/Validation.md` for the earlier baselines and attribution. Preserve persistent world/origin policy preferences, never player-ID cohorts. Climate, opening-herd guarantees and storage horizons remain approximations.
- Stay within the requested work. Do not start later eras, background monitoring or additional agent tasks merely because they appear in the roadmap. After completing an assignment, report the result and await further instructions.

## Preset playtesting and developer atlas

Read Docs/Playtesting.md for the three natural Standard / Continents / 11 / 80-tribe starts and their measured food densities. PlaytestScenarios selects existing camps by exchanging player ownership only; keep resource stocks, policy seeds, population and geography unchanged. Presets fail if habitat/viability no longer matches, so remeasure them after generation changes. The explicitly labelled developer preview may reveal global geography and camp locations; it must remain separate from player/AI observations and must never change fog, materialize ecology or read partially resolved state. The atlas uses stored seasonal profile potential, while its inspector separately projects current stock. Use --anthropocene-presets-output with a fresh output directory for the opt-in Windows player preset/preview smoke run; it uses no user save slot.

The focused suite now includes 118 geological-resource assertions in addition to the 437-assertion core baseline, plus deterministic worker replay. Resource checks include original default fingerprints and nondefault tuning isolation, bounds and round trips. The generator-2 full regression, resource audits and large-world round trip pass (see Docs/Validation.md). The Unity integration suite has 25 passing tests, including resource raster accounting, saved access, purity, absent legacy layers and trace colors. The rebuilt resource GUI captured all 37 active materials in total/accessible modes and preserved state/fog. Native mouse interaction remains unverified because app access timed out. The three historical preset hold-order player smoke runs each reached turn 2 with 30 people. Treat these as UI checks, not multi-generation balance validation.

## Generator morphology contract

Generator 11 preserves generator-10 morphology and changes aquatic ecology only. Generation 10 preserves the generator-9 continental base shapes and adds narrow, rifted Archipelagos crust and longer moving uplift windows with correlated Gaussian drift. Extinct tracks can retain islands and submerged seamounts without current volcanic activity; their kernels must blend into the original local seabed and permit self-overlap. It separates continental shape from plate labels, uses longitude-periodic multiscale terrain and plate-motion hotspot tracks, transports atmospheric moisture at a normalized planetary scale, and distinguishes retained lakes from drainage fill/breach. Preserve seasonal regional/local moisture separation and old save restoration. Run the morphology, climate and basin tests plus broad generation/gameplay audits after generator changes. Re-export and inspect full environment/elevation/volcanism maps for several seeds and each world shape; passing local start checks is insufficient. Tools/WorldAtlas/Export-Worlds.ps1 exports the actual state, metrics, provenance and optional separate saves without touching the normal save slot. Keep generator copies/artifacts ignored under TestResults. Do not advertise approximate tectonic or steady-seasonal proxies as a physical planet simulation.

The opt-in `--anthropocene-atlas-output <absolute-directory>` player check generates Standard/Archipelagos/42/80, captures all six preview layers and normal play, verifies unchanged fog/state through inspection, and resolves one turn. `--anthropocene-resources-output <fresh-absolute-directory>` captures both modes for all 37 active materials, rich/scattered Copper close-ups, selector and normal fog (78 frames), checks unchanged state/fog, and leaves the atlas open. Both require a visible window and use no user save slot. Historical hotspot improvements apply to Continents/Pangaea too; compare island counts and submerged trails in those controls when editing the shared track model.
