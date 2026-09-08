# Anthropocene

A Unity 6 2D URP project for sprite-, shape-, and particle-driven gameplay.

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
