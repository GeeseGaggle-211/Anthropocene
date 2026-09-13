# World atlas exports

This native .NET 8 / C# 9 diagnostic compiles the actual simulation sources. It generates PNG maps, a local HTML gallery, exact configuration, measured geography and camp food/freshwater summaries. PNG encoding uses the .NET runtime; no external packages or renderer are required. Exports are diagnostics of the real world state, not image-generated reference art.

From the repository root:

```powershell
./Tools/WorldAtlas/Export-Worlds.ps1 -Seeds '1729,1,2' -Shape Continents -Players 80 -Output TestResults/WorldGeneration/After -Save
```

Use `-GeographyOnly` for quick terrain/climate iteration. It runs the actual generation stages through VegetationResourceStage, skipping only Placement and final world validation; no camps are fabricated and these runs are not playable saves. Complete exports run `WorldGenerator.Generate`, including placement and normal validation. `-Save` writes a separate `world.anthro` alongside each complete export and never accesses the user save slot.

Use `-SimulationSource <absolute-directory>` to compile a preserved simulation source snapshot for before/after comparisons. This changes only the source compiled by this diagnostic; it does not replace live project files. The default always compiles `Assets/_Anthropocene/Simulation/*.cs`. Rebuild before switching source snapshots. Output directories should be fresh when comparing revisions.

Each world's `manifest.json` records generator/world/config versions, seed, configuration references and SHA-256 fingerprints for the compiled simulation source set, exporter assembly, configuration JSON and optional playable save. The wrapper checks that simulation source contents did not change during compilation. The source-set fingerprint hashes the ordinal filename-sorted `filename:SHA256(file bytes)` lines joined by a single newline. It describes exact source content; it is not a Git commit identifier. Direct DLL invocation records unavailable source provenance unless explicitly supplied. Export timestamps and fingerprints are diagnostic metadata and never enter authoritative game state.

The top-level gallery and `index.json` include every existing child manifest. Separate calls for Continents, Pangaea and Archipelagos can therefore share one output directory. Regenerating the same seed/shape replaces that child's maps, metrics and manifest; use a different output directory to preserve an earlier revision. Older child galleries without manifests are included with an explicit provenance limitation.

Images use one pixel per axial q/r tile. This preserves the generator's native coordinates and wrap seam; it is not an equal-area planetary projection. Environment and topography use fixed palettes. Renewable food and freshwater use fixed logarithmic 0–100 colour scales so different seeds/revisions can be compared. Food means the four-season mean of stored renewable human-food potential before workers, depletion and competition. The separate volcanic image shows stored `Volcanism`, not an artist's representation of island chains. Metrics define connectivity, shoreline straightness and denominators explicitly. Residual filled-land metrics isolate land with final `DepressionDepth > 0.008` from actual lakes; they expose artificial dry plateaus that a lake-area check alone would miss. Axis-aligned shoreline runs are a limited diagnostic and must accompany visual inspection: they cannot detect every straight line at arbitrary slopes.

Open the generated `index.html` in a browser or each PNG directly. Generated images, saves, metrics, binaries and temporary source snapshots remain under ignored `TestResults` / `bin` / `obj`; commit the reusable tool, not generated build output.
