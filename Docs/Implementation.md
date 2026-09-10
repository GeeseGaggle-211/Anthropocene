# Paleolithic prototype

Current behavior is documented in [milestone three](Milestone3.md): twenty-year turns with seasonal economic intervals, elevation levels 0–10 with nested contours, individual stack slots, demographic explanations, passive coastal food and delayed hover details. These supersede the earlier seasonal-turn and four-band interpretations.

Source: [Google Drive design spec](https://docs.google.com/document/d/1kw3ZGhJTHkLGUgEAxFikl7pL0UW5ixCcIUV3A0sgSjQ/edit), retrieved September 9, 2026. `DesignSpec.txt` preserves the source text read during implementation.

## Play

Open this project in Unity 6000.5.10f1 and press Play in SampleScene. The bootstrap creates the opening UI automatically. A Windows build is available after invoking **Anthropocene > Build Windows prototype**; run `Builds/Windows/Anthropocene.exe`.

The menu defaults to the specified 1024 x 512 Standard world. Small and Tiny are useful for fast iteration. Generation runs off the main thread. Seed, shape and tribe count are selectable. The glyph tiles and hex texture are generated with code and Unity's built-in font; there are no external art licenses or paid packages.

Select the camp or a hunter group in the sidebar. Right-click a visible tile to queue movement, or choose Move and left-click. Reachable destinations, exact invalid-move causes, queued routes and route ownership remain visible on the map. Dispatch splits population on resolution; its preview shows the before/after camp headcount and the new group can act the following generation. Select hunters and choose Hunt or Fish to target a herd or shore. To intercept prey, queue the expected destination first and then choose Hunt and click the herd. Reintegration requires the source camp's tile. Enter opens an all-unit order review; Enter again or the commit button resolves all orders simultaneously. Escape cancels targeting or closes the review without clearing queued orders.

The main map keeps a compact terrain, water, food and occupant glyph key visible. Hunter planning separates the current supply-path connection from carried provisions: connected groups share camp food and deposit carried food on return, while disconnected groups must live on finite carried food and local harvest. Planned supply is labelled as visible, uncertain through unseen terrain, or outside range without reading hidden geography.

Mouse wheel zooms; middle-drag and arrow keys pan; C centers the camp. Escape returns to inspection. Help contains the glyph legend. F3 exposes raw values on currently visible tiles for developers.

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
- Hunters within a traversable supply path share the camp store. Distant hunters carry finite provisions, retain harvest up to capacity and eat separately. Return to supply deposits their food; movement can continue beyond supply range.
- Dispatch has no same-turn compound follow-up. Existing hunters can combine movement with Hunt/Fish.
- Active fishing uses a common aquatic snapshot in phase 6. Passive gathering and surviving-herd consumption use the next common snapshot in phases 7–8, following the listed phase order.
- Domain ties prefer Hunting, then Gathering, then Fishing. Turn-12 forced transitions preserve experience and expose secondary progress through the configured multiplier/floor.
- Geographic fields are fixed; seasonal resource envelopes change with the clock. Only resources and the first cultivation/fishery improvements mutate during this opening.

## Validation and remaining scope

Run `./Tools/Test-Headless.ps1 -Seeds 30 -Standard -Geography`. This exercises hex topology, split/reintegration, fishing, allocation, collision handling, hunger, threshold/grace/hard-cap transitions, carryover, fog, AI information isolation, hunting contact and fleeing, time scaling, inertia, save round trips and replay after reversing entity collections. The generation suite compares presets and checks viable starts, drainage cycles and stellar temperature response. The Standard benchmark generates 524,288 tiles and plays its opening headlessly.

Unity EditMode tests cover actual engine compilation, binary save compatibility, several complete AI/replay games and information boundaries. The build menu produces a local Windows development player for interactive QA.

The playable milestone ends when all surviving tribes have their first breakthrough or Turn 12 is reached. First-tech effects are applied, and remaining experience is saved. A complete Neolithic era, subsequent technology discovery loop, trade, diplomacy, warfare, construction and RL training are future work. The climate model is deliberately approximate: winds currently transport moisture primarily along latitude bands; detailed vector wind deflection, advanced snowpack/climate feedback, configurable endorheic-basin alternatives and richer ecotones remain refinements to the map-generation draft.

See [milestone two mechanics and tileset guide](Milestone2.md) for the layered renderer, logistics, growth, species, boundary hydrology, oases and explicit freshwater model. These later implementation decisions supersede conflicting first-milestone interpretations; the retrieved Drive spec remains unchanged.
