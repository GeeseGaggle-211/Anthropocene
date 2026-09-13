# Symbolic map assets

The 23 RGBA PNGs in `Assets/_Anthropocene/Resources/MapArt` are original project drawings, authored as small vector recipes in `Tools/Art/Build-SymbolicAssets.cjs` and rasterized at 256 × 256. No third-party artwork or fonts are shipped with these assets. The exporter uses the Sharp package (Apache-2.0), available in the workstation's bundled Node dependencies; the game does not depend on Node or Sharp.

The set follows the proposal's written Minimalist & Iconic contract: nine ground/water families, five repeatable vegetation motifs, three land-use overlays, two settlement symbols and four unit symbols. The embedded Google Drive reference could not be retrieved as an image in this session; its written style, layering and icon instructions guided these originals. This is replaceable first-pass art, not a claim to reproduce that image.

Trees/vegetation are never baked into ground. Density comes from deterministic motif placement; low zoom drops those motifs before actor/contour identity. Rivers and exact elevation boundaries remain procedural. High cultivation and village assets are extension hooks only and do not enable Neolithic gameplay.

Both semantic tileset definitions reference the same assets with different icon contrast. Each definition keeps its glyph fallback, so missing art does not change simulation or save behavior. The Unity import rule creates single sprites with transparency and no mipmaps. Keep existing GUIDs when replacing art.

To regenerate: run `Tools/Art/Build-SymbolicAssets.cjs` with Node, passing the local Sharp package directory as its first argument. The optional contact sheet is written to ignored TestResults.
