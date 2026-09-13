# Working on Anthropocene

## Intent and current scope

Anthropocene is a deterministic, simultaneous-turn strategy simulation. The playable opening follows mobile camps and hunter groups through the Paleolithic to their first technological breakthrough, normally within twelve turns. Prioritize information a player can act on: readable Dwarf Fortress-style symbolic tiles with glyph fallbacks, clear terrain and resource distinctions, understandable orders, and explained outcomes. Decorative art is secondary.

Read `Docs/EcologyFoundation.md` and `Docs/ConfigurationUnits.md` for current mechanics and tuning units and `Docs/Validation.md` for tested behavior and known limitations. `Docs/Implementation.md` describes the architecture. `Docs/DesignSpec.txt` preserves the original Google Drive specification; do not silently rewrite that historical source. Earlier milestone notes contain superseded rules. Current user instructions take precedence over these documents.

The default player turn is twenty years, resolved through seasonal economic intervals. Earlier references to ninety-day player turns and four elevation bands are historical. A complete Neolithic era, trade, diplomacy, warfare, construction and RL training are outside the current implementation.

## Repository map

- `Assets/_Anthropocene/Simulation/`: authoritative, Unity-independent C# state, generation, turn resolution, logistics, observations, AI policies and saves.
- `Assets/_Anthropocene/Scripts/Runtime/Presentation/`: Unity immediate-mode UI, layered symbolic renderer, hover details and order controls.
- `Assets/_Anthropocene/Scripts/Runtime/Core/GameBootstrap.cs`: creates the persistent game root and opening UI.
- `Assets/_Anthropocene/Resources/OpeningConfig.json`: simulation tuning. Keep serialized values and `SimulationConfig` defaults consistent when changing defaults.
- `Assets/_Anthropocene/Resources/Tilesets/`: classic and high-contrast semantic symbol definitions with glyph fallbacks. Both must convey the same gameplay information.
- `Assets/_Anthropocene/Resources/MapArt/`: 29 original cartographic sprites; preserve GUIDs, alpha and semantic layers. `Tools/Art/Build-SymbolicAssets.cjs` recreates PNGs and SVG exports in `Tools/Art/Sources`; provenance and the approved visual target are in `Docs/ArtProvenance.md`. Judge art in the actual Unity render, not only a source contact sheet. Never add opaque occupant backplates or flatten coloured sprites with glyph tint.
- `Assets/_Anthropocene/Editor/PrototypeBuild.cs`: Windows development build entry point.
- `Tools/Headless/`: console checks and benchmarks compiling the actual simulation source files, not a duplicate implementation.
- `Assets/_Anthropocene/Tests/EditMode/`: Unity/NUnit integration and presentation checks.

## Preserve these contracts

- Keep Unity APIs out of the simulation assembly. The headless project targets .NET 8 with C# 9; simulation changes must compile there and in Unity.
- Preserve seeded determinism, stable entity IDs, sorted resource allocation and collection-order-independent replay. Use the existing keyed random streams instead of wall-clock randomness. Timings and UI state must not affect authoritative results.
- Player details and AI decisions consume filtered observations. Do not expose hidden tiles, foreign private stores/populations, global event logs or rival submitted orders. Wild herds and foreign livestock use `HerdCountKnowledge` estimates; only owned livestock counts are exact. Keep derived AI categories consistent with those estimates.
- Continuous height, Environment and natural/current Vegetation are independent of gameplay elevation. Marine tiles are level 0, traversable land and freshwater lakes are levels 1–9, and level 10 blocks movement and placement. Preserve freshwater, river-edge and coastline invariants.
- Society owns technology, tradition, policy preferences and era progression; camps own local people, stores and workforce. Bind owners at construction/load/resolution boundaries, never from a read path. Mastery can complete the opening early; majority eligibility starts at 200 elapsed years and forced completion at 240. Two player turns of starvation grace are deliberate.
- Renewable ecology is sparse and expressed in days/years. Human food G is part of total biomass B. Only realized flows cause pressure; finite freshwater is debited at its actual source and drinking does not damage aquatic condition. ProjectEcology must be pure; MaterializeEcology commits that projection. Persist latent timestamps and vegetation hysteresis; do not scan quiet tiles every turn.
- Human growth depends on recurring food surplus and reserves, with rising consumption, fractional progress and separately recorded deaths. Do not introduce an arbitrary human population cap. Maintain population and food conservation and the persisted demographic explanation.
- Passive coastal gathering uses adjacent Shallows and shares the camp's land-gathering labor. Ocean is excluded and marine food never supplies freshwater. Remote hunters retain finite carried provisions and respect traversable supply paths.
- Keep ground, merged elevation contours, water, vegetation, land-use, settlements, food markers and occupants independently legible. Equal elevation buckets suppress their shared contour edges; unknown neighbors must not leak elevation. Stacked units use stable slots shared by rendering and hit testing; sprite bounds must stay within those slots; overflow remains accessible in the inspector. Delayed hover must reset with navigation, use visible information only and never intercept input.
- Vegetation detail must show no trees for Low (grass/brush only), exactly one tree for Medium and three for High across land climates. Preserve those counts at all detail zooms; omit vegetation at distant zoom. Decorative feature art must not add trees that confuse this state indicator.
- Independent AI/herd intentions and active ecology may use bounded parallel workers; compare against the scalar reference with varied partitions/order. Commit IDs, collisions, entity changes and history canonically. Runtime worker settings and timings must not enter authoritative state.
- Generation, turn resolution and save/load use workers. Do not read or mutate partially resolved world state from the UI; preserve the busy/progress guards.
- Classify environment and climatic vegetation from seasonal `RegionalMoisture`; use local `Moisture` for productivity, recovery and habitat. Rivers/lakes/oases modify local soil without reclassifying climate. Natural hydrologic uplift reaches Medium; High requires regional support. Keep both profiles deterministic and persisted, and preserve productive dry river valleys.
- Save/load must restore authoritative geography and history without regeneration. The current signature is `ANTHROPOCENE-8`, filename `opening-v7.anthro`, world/config/generator versions are 7, and observation schema remains 7 (observed moisture is local soil). This prototype revision intentionally rejects earlier formats without separate regional moisture. Sparse pressure/balance diagnostics are transient; saved history retains outcome receipts and authoritative continuation state. If a later change breaks compatibility, explicitly version it or implement a migration and test rejection/round-trip behavior. Preserve older user save files.

## Build and validate

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

The script finds the local or Unity-bundled .NET SDK, then falls back to `dotnet` on PATH. Run checks appropriate to the change: focused simulation checks for mechanics, the broader suites for generation/determinism/performance changes, and Unity EditMode tests for engine integration or presentation changes. Do not repeat unchanged expensive suites without a reason. The recorded baseline is 246 fixed headless assertions plus seeded/audit checks, scalar-versus-2/4-worker replay across ten resolutions, and 19 Unity tests; counts can legitimately increase. `Docs/Performance.md` records the 80/320 results. Do not expose 320 tribes as a public preset before profiling and compacting dense tile/season memory; the measured single-world heap is about 852 MiB after generation.

Run EditMode tests in Unity's Test Runner. For batch runs, use the Unity executable with `-batchmode -nographics -projectPath <absolute-root> -runTests -testPlatform EditMode -testResults <absolute-xml-path> -logFile <absolute-log-path>`; let the test runner exit rather than adding `-quit`. Create the output directory first and inspect the XML result, not just the process exit code. Launch background batch processes hidden on Windows.

Build through **Anthropocene > Build Windows prototype**, or use `-batchmode -nographics -quit -projectPath <absolute-root> -executeMethod Anthropocene.Editor.PrototypeBuild.Windows -logFile <absolute-log-path>`. Close the player from this checkout before replacing its build. Output is `Builds/Windows/Anthropocene.exe`. Inspect the build log for success and smoke-test UI changes in the player. On this workstation, if the headless editor stalls while reconnecting to `Unity.Licensing.Client`, stop that exact batch process and invoke the same `-quit -projectPath ... -executeMethod ... -logFile ...` command through the authenticated graphical editor without `-batchmode -nographics`; that route produced the validated build. Never launch a second Unity instance against the checkout while diagnosing licensing.

`Open-Anthropocene.ps1` pulls with `git pull --ff-only` before opening Unity. Use it when synchronizing is intended; open the editor directly for ordinary local iteration.

## Reproducible interactive demo

Choose **Standard / Continents / seed 1729 / 32 tribes**. The opening view shows 24 people, one complete starting-generation reserve and a four-generation desert storage ceiling. Dispatch eight people and commit: turn two shows 28 people and the camp/hunter stack. Move the hunters two tiles east and commit: turn three shows 36 people. Planning another two-tile eastward move draws a red camp tether and a matching disconnection warning; the group will forage locally and use carried provisions after committing. This is a normal deterministic play sequence, not a special seed rule or edited save.

Use V to compare tilesets, the wheel to zoom, arrows to pan and C to center the camp. Check exact slot selection, vegetation/detail reduction with zoom, hover after more than two stationary seconds, coastal saltwater/food information, and save/load restoration. The development-player argument `--anthropocene-smoke-output <absolute-directory>` runs an automated version of the demo and captures frames using a separate save in that output directory; it is not a substitute for a human interaction/usability playtest. The visual check needs a visible game window on this workstation; hidden players cannot capture the backbuffer. It also captures clearly labelled synthetic art fixtures through the production renderer. Run `Tools/Art/Check-PlayerArt.cjs` with Node, the Sharp package directory, and the capture directory to verify the selected-camp transparency probes and complete capture set. Preserve any existing user save when setting up a new demonstration.

## Follow-on workflow

- Inspect `git status` before editing and preserve existing work. Do not revert settings or source files wholesale to HEAD just to remove apparent noise; Unity can reserialize settings while other edits already exist.
- Keep Unity `.meta` files with their assets and preserve existing GUIDs. Keep third-party packages outside `_Anthropocene`; document provenance/licensing for added assets and use only dependencies needed by the task.
- Build output, `Library`, logs, local SDKs and `TestResults` are intentionally ignored. Commit source, configuration, metadata, tests and relevant documentation. Do not force-add generated artifacts to satisfy a documentation link.
- Update the current mechanics and validation notes when behavior changes. Distinguish automated coverage from manually observed UI behavior, and record benchmark seed/configuration when reporting performance.
- The September 13 thirty-seed Small-world regression had 120/120 survivors: 34 Husbandry / 67 Cultivation / 19 Watercraft breakthroughs; median final population 93, quartiles 51/129, maximum 283, and 51/120 tribes at least 100 people. Pursuit yielded food 44.1% of the time. The Standard/Continents 32-tribe sample produced 11/19/2, 17/13/2 and 9/20/3 across seeds 1-3: aggregate 37/52/7 with 96/96 survivors. Subsistence shares are balance targets, not quotas; fishing is less common in this sample. Policies use persistent world/origin preferences and visible opportunities, never player-ID cohorts. These are measured baselines, not universal guarantees. Climate, habitat proxies, opening-herd guarantees, storage horizons and wind transport remain approximate.
- Stay within the requested work. Do not start later eras, background monitoring or additional agent tasks merely because they appear in the roadmap. After completing an assignment, report the result and await further instructions.
