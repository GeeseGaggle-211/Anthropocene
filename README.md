# Anthropocene

A Unity 6 2D URP project for sprite-, shape-, and particle-driven gameplay.

For follow-on development, read [AGENTS.md](AGENTS.md) for architecture, simulation contracts, validation commands and the reproducible demo.

## Open the project

Use Unity Hub or open this folder with Unity Editor `6000.5.10f1`. The first import can take several minutes because Unity generates the ignored `Library` folder.

On Windows, run `Open-Anthropocene.ps1` to fast-forward the local `main` branch from GitHub and then open this checkout in the installed editor. The script deliberately uses `git pull --ff-only`, so it stops instead of creating an unexpected merge when local and remote history diverge.

Repository: <https://github.com/GeeseGaggle-211/Anthropocene>

## Foundation

- Unity 6 (`6000.5.10f1`)
- Universal Render Pipeline with the 2D Renderer
- Input System package
- Sprite and tilemap tooling, with optional art importers deferred until needed
- Runtime code isolated in `Anthropocene.Runtime`
- Edit-mode tests isolated in `Anthropocene.Tests.EditMode`
- Text serialization and visible `.meta` files for reliable version control

## Project layout

- `Assets/_Anthropocene/Scripts/Runtime` — game code shipped in builds
- `Assets/_Anthropocene/Tests/EditMode` — fast editor tests
- `Assets/Scenes` — playable scenes; the template scene is the current startup scene
- `Assets/Settings` — URP, renderer, input, and scene-template assets

Keep third-party packages outside `_Anthropocene`. This makes ownership clear and keeps future upgrades manageable.

## First milestone

The bootstrap creates a persistent `[Anthropocene]` root before the first scene loads. It is intentionally small: new systems can attach to the root later without committing us to a large framework before the game loop is known.

## Playable Paleolithic opening

Press Play in `SampleScene` to open the game menu. The prototype now includes a glyph-based hex map, simultaneous seasonal orders, camps and hunters, hunting/fishing/gathering, food consumption and starvation, herd ecology, current-sight fog, heuristic rival tribes, and a first-technology transition by Turn 12.

Select a unit in the sidebar, choose an action, then press Enter to resolve the season. Right-click queues movement. Help explains glyphs and controls. Standard worlds are 1024 x 512 tiles; Small and Tiny are available for iteration.

Use **Anthropocene > Build Windows prototype** to create `Builds/Windows/Anthropocene.exe`. Tune rates and generation values in `Assets/_Anthropocene/Resources/OpeningConfig.json`.

Run `./Tools/Test-Headless.ps1 -Seeds 30 -Standard -Geography` for deterministic simulation, AI, save/load and generation checks. Unity EditMode tests are under `Assets/_Anthropocene/Tests/EditMode`.

See [implementation notes](Docs/Implementation.md) for architecture, design decisions, save behavior and remaining scope.

Milestone two adds interchangeable glyph tilesets (V), biome/relief layers, boundary rivers, oases, explicit freshwater, regional wildlife including camels and horses, expedition provisions outside supply range, and nutrition-gated net population growth. See the [mechanics and tileset guide](Docs/Milestone2.md) and [validation results](Docs/Validation.md).

The current [third milestone](Docs/Milestone3.md) uses twenty-year turns, nested elevation contours (levels 0–10), impassable peaks, individual stacked-unit slots, approximate wild-herd counts and a two-second hover detail panel. Every turn explains population additions/deaths and food production/demand, including passive marine gathering. A combined demo uses Small / Continents / seed 1 / six tribes, with eight-person dispatches in the first two turns.
