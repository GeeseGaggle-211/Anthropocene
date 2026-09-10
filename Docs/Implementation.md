# Paleolithic prototype

Current behavior is documented in [milestone three](Milestone3.md): twenty-year generational resolution, elevation levels 0–10 with nested contours, individual stack slots, demographic explanations, passive coastal food and delayed hover details. These supersede the earlier seasonal-turn and four-band interpretations.

Source: [Google Drive design spec](https://docs.google.com/document/d/1kw3ZGhJTHkLGUgEAxFikl7pL0UW5ixCcIUV3A0sgSjQ/edit), retrieved September 9, 2026. `DesignSpec.txt` preserves the source text read during implementation.

## Play

Open this project in Unity 6000.5.10f1 and press Play in SampleScene. The bootstrap creates the opening UI automatically. A Windows build is available after invoking **Anthropocene > Build Windows prototype**; run `Builds/Windows/Anthropocene.exe`.

The menu defaults to the 1024 x 512 Standard world and 32 tribes. Standard accepts presets through 80; Small is limited to 16 and Tiny to 8. Generation runs off the main thread. Seed, shape and tribe count are selectable. The glyph tiles and hex texture are generated with code and Unity's built-in font; there are no external art licenses or paid packages.

Select the camp or a hunter group in the sidebar. Right-click a visible tile to queue movement, or choose Move and left-click. Dispatch splits population on resolution; the new group can be selected in the following planning phase. Select hunters and choose Hunt or Fish to target a herd or shore. To intercept prey, queue the expected destination first and then choose Hunt and click the herd. Reintegration requires the source camp's tile. Enter resolves all orders simultaneously. The sidebar explains invalid orders.

The planning presentation uses a familiar 4X interaction grammar without third-party artwork: a persistent selected-unit card, command modes, colored valid-target overlays, a hover path preview and a visible order queue. Green denotes reachable movement, amber a valid visible herd and blue accessible fishing water. Camp-to-hunter tethers preview final ordered positions: green is supplied, amber crosses unseen terrain, and red is disconnected. The sidebar repeats the warning before commit. Gold paths and `Q` badges are queued, not yet executed. Each unit can hold one order; choosing another command replaces it, while Backspace or **Cancel selected order** returns the unit to holding position.

Dispatch remains a camp order, not an immediately selectable unit: the new hunter group spawns at the camp after the generation resolves and can first receive an order in the next planning phase. During resolution the player sees the committed summary while input is paused. The next planning phase retains a concise player-only result summary alongside the existing filtered journal; it is derived from the player's orders, transfers, logistics and demographic report rather than the global event log.

Mouse wheel zooms; middle-drag and arrow keys pan; C centers the camp. Escape returns to inspection without clearing queued orders. M, H and F enter their respective target modes; Backspace cancels the selected unit's queued order. Help contains the glyph legend. F3 exposes raw values on currently visible tiles for developers.

Food, freshwater, renewal and reserve values are qualitative. Only currently visible terrain and units appear. The player journal is built from own orders, own population and own transfers; it never displays the authoritative global event log. Multiple stacked entities are listed in the tile inspector. V switches between the two data-driven tilesets. The inspector distinguishes biome, relief, river edges, oases and undrinkable saltwater.

Save stores the committed turn, generated geography, complete configuration, clock, stable ID counter, camps, groups, herds, experience, mandates and event/decision history. Pending orders are not saved. Saves live in Unity's `Application.persistentDataPath/opening-v3.anthro`. Files use fixed binary tile records and a versioned metadata contract inside buffered gzip. Saving and loading run on a worker while order mutation is disabled. Loading never regenerates geography. Milestone three uses file signature `ANTHROPOCENE-4`, world/config/generator version 3 and observation schema 3. Incompatible old saves are rejected; the original `opening.anthro` is retained under its old filename.

## Code boundaries

- `Assets/_Anthropocene/Simulation`: Unity-independent C# assembly. `WorldGenerator`, `HexMap`, `TurnResolver`, observations/policies, save codec and model data.
- `Assets/_Anthropocene/Scripts/Runtime/Presentation`: Unity adapter, glyph rendering, menu, inspection and order controls.
- `Assets/_Anthropocene/Resources/OpeningConfig.json`: editable tuning, including time, hunger, rates, knowledge bands and generation controls. Menu choices override size, player count and shape.
- `Tools/Headless`: console test and benchmark runner using exactly the simulation sources compiled by Unity. It needs no third-party test framework.
- `Assets/_Anthropocene/Tests/EditMode`: NUnit integration checks in Unity.

Randomness is stateless and keyed by seed, named numeric stream, stable entity and turn. Resource claims sort by tile and entity before proportional allocation. Integer kills and starvation deaths use largest-remainder apportionment. Camp collisions prioritize existing occupants, then stable ID, with rollback propagation. Swaps are allowed if final camp positions remain distinct.

The generator uses plate-derived crust/relief, volcanic chains, erosion, stellar/latitude/seasonal temperature, wind moisture transport and rain shadows, priority-flood drainage, soil moisture, biomes, seasonal resources, then viability-weighted start placement. Pangaea clusters continental plates rather than painting a fixed continent mask. Timings are diagnostic and excluded from deterministic state fingerprints.

AI consumes immutable observations with the same qualitative resource bands and radius-2 visibility as the player. It scores local candidate orders for survival, gathering, fishing, hunting, travel, continuity and reintegration. No global resource map or hidden submitted order is passed to the policy. Decision traces remain developer data in saves/history. AI tuning is an initial baseline and should be improved with playtesting.

## Resolved specification ambiguities

- Explicit hunger and demographic requirements supersede the older population-only-by-splitting rule. Conservation is previous population plus recorded net growth minus recorded starvation deaths.
- Hunters within a traversable supply path share the camp store. Distant hunters forage their occupied tile for immediate subsistence, then consume finite carried provisions. Surplus hunting/fishing harvest is retained up to capacity and deposits on return; movement can continue beyond supply range.
- Dispatch has no same-turn compound follow-up. Existing hunters can combine movement with Hunt/Fish.
- Seasonal profiles are weighted into one generation-scale renewable-resource window. All camp, hunter and herd demands use one sorted proportional allocation, followed by one demographic/herd update and one map pass.
- Domain ties prefer Hunting, then Gathering, then Fishing. Turn-12 forced transitions preserve experience and expose secondary progress through the configured multiplier/floor.
- Geographic fields are fixed; seasonal resource envelopes change with the clock. Only resources and the first cultivation/fishery improvements mutate during this opening.

## Validation and remaining scope

Run `./Tools/Test-Headless.ps1 -Seeds 30 -Standard -Geography`. This exercises hex topology, split/reintegration, fishing, allocation, collision handling, hunger, threshold/grace/hard-cap transitions, carryover, fog, AI information isolation, hunting contact and fleeing, time scaling, inertia, save round trips and replay after reversing entity collections. The generation suite compares presets and checks viable starts, drainage cycles and stellar temperature response. The Standard benchmark generates 524,288 tiles and plays its opening headlessly.

Unity EditMode tests cover actual engine compilation, binary save compatibility, several complete AI/replay games and information boundaries. The build menu produces a local Windows development player for interactive QA.

The playable milestone ends when all surviving tribes have their first breakthrough or Turn 12 is reached. First-tech effects are applied, and remaining experience is saved. A complete Neolithic era, subsequent technology discovery loop, trade, diplomacy, warfare, construction and RL training are future work. The climate model is deliberately approximate: winds currently transport moisture primarily along latitude bands; detailed vector wind deflection, advanced snowpack/climate feedback, configurable endorheic-basin alternatives and richer ecotones remain refinements to the map-generation draft.

See [milestone two mechanics and tileset guide](Milestone2.md) for the layered renderer, logistics, growth, species, boundary hydrology, oases and explicit freshwater model. These later implementation decisions supersede conflicting first-milestone interpretations; the retrieved Drive spec remains unchanged.
