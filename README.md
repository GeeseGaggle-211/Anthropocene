# Anthropocene

A deterministic, simultaneous-turn strategy game following mobile Paleolithic camps and hunter groups toward their first technological breakthrough. One player decision normally covers twenty years. The map uses readable symbolic tiles, separate vegetation and elevation layers, and explained food/population outcomes.

## Play

Use Unity **6000.5.10f1**, open `Assets/Scenes/SampleScene.unity`, then press Play. Build the Windows player through **Anthropocene > Build Windows prototype**; output is `Builds/Windows/Anthropocene.exe`.

Choose a seed, world shape and tribe count. Standard is 1,024 x 512 tiles and supports up to 80 tribes; the default is 32. Small/Tiny offer quicker iteration. The 2,048 x 1,024 / 320-tribe configuration is restricted to benchmarks.

Select a camp or hunter group. Right-click queues movement; **M** enters movement targeting and **F** fishing targeting. Dispatch separates people when the generation resolves. Established hunters automatically hunt wild herds sharing their resolved tile. **Enter** commits simultaneous orders; **Backspace** cancels the selected order. Supply lines show whether the planned expedition remains connected to its source camp.

Use the mouse wheel to zoom, arrows or middle-drag to pan, **C** to center the camp, and **V** to compare contrast sets. Hover while stationary for two seconds to inspect visible land and stacked units. Help explains the symbols and controls.

People need recurring food surplus and reserves to grow. Disconnected hunters forage locally and use finite carried provisions. Starvation has two player turns of grace. Hunting, gathering and fishing histories drive the first breakthrough; mastery can complete the opening early, majority eligibility begins after 200 simulated years, and completion is forced by 240 years. Later eras are not playable yet.

Save/load restores committed geography, history and latent ecology without regenerating the world. Current saves use `opening-v6.anthro` in Unity's persistent-data directory; older save files remain preserved under their previous names. Pending orders are not saved.

## Develop and validate

Work directly from the Windows checkout. `Open-Anthropocene.ps1` performs `git pull --ff-only` on the current branch before opening Unity; use it when synchronization is intended, and open Unity directly for local iteration.

```powershell
./Tools/Test-Headless.ps1 -Seeds 0
./Tools/Test-Headless.ps1 -Seeds 30 -BiomePaths -Geography
./Tools/Test-Headless.ps1 -Seeds 3 -NomadStandard
./Tools/Test-Headless.ps1 -Seeds 0 -Audit
```

The headless runner compiles the actual Unity-independent simulation sources. Run engine/presentation checks in Unity's EditMode Test Runner. Do not open concurrent Unity instances against one checkout.

Read [AGENTS.md](AGENTS.md) before follow-on work, [current mechanics](Docs/EcologyFoundation.md), [architecture](Docs/Implementation.md), [configuration units](Docs/ConfigurationUnits.md), [validation](Docs/Validation.md), [scale measurements](Docs/Performance.md) and [art provenance](Docs/ArtProvenance.md). `Docs/DesignSpec.txt` and earlier milestone notes preserve historical specifications; current mechanics supersede their conflicting rules.
