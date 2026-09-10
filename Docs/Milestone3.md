# Milestone three — generations, elevation and tile information

This milestone supersedes the four-band elevation presentation and 90-day player turns described in the older notes. The source Drive document and milestone-two notes remain historical references.

## Elevation and contours

Continuous elevation is unchanged and remains authoritative for climate and drainage. Gameplay elevation is an integer 0–10. Marine Ocean/Shallows tiles are level 0; freshwater lakes inherit an above-sea level 1–9; traversable land occupies 1–9; level 10 is an impassable peak. Camps, hunters, supply paths, herd placement/movement and AI all respect peak blocking. Peak land remains land for coastline classification.

After final hydrology, nine configured land-elevation quantiles (12%, 25%, 40%, 55%, 69%, 81%, 90%, 96%, 99%) define level boundaries. This spreads levels across the world without modifying its continuous height field or biome. Equal heights receive the same level. Levels 1–2 are lowland, 3–4 rolling hills, 5–6 ridges/highland, 7–9 mountains and 10 peaks. Contiguous high land also has reciprocal ridge links when adjacent levels differ by at most two. Existing productivity modifiers apply to these descriptive groups; there is no human population cap.

The renderer draws one nested hex outline per level at tile heights of at least 85 pixels. Level 0 has no elevation contour. At smaller zoom it draws `ceil(level / 2)` outlines to avoid aliasing, restoring the exact count on zoom-in. Level 10 also has an X. Biome fills, rivers on shared boundaries, oasis/freshwater features, food markers and occupants retain separate layers. Muted ridge links connect high ground; river channels use their distinct cyan color and flow widths.

Both tilesets define `elevation.contours` with configurable `Tint`, `Opacity`, `LineWidth` and `Spacing`, plus `elevation.ridge` and keys `elevation.0` through `elevation.10`. Spacing is bounded by tile size so rings cannot invert. Numeric elevation and its description are always available in the inspector and hover panel.

## A generation is one player turn

The default `TurnDays` is 7,300, with 365 days per year: twenty years. The player commits movement, dispatch and a subsistence strategy once. The historical clock advances by the configured duration and the header displays elapsed years. `TurnDays` remains configurable and shorter durations use the same annualized formulas. `BaseDays = 91.25` is now a rate-conversion unit, not a simulated sub-turn.

One deterministic generational pass combines the four seasonal climate profiles into their exact weighted exposure over the configured duration. It computes renewable production, then resolves hunting, fishing, gathering, disconnected-hunter foraging, herd pressure and proportional competition once. The map is advanced once at the end. There is no eighty-iteration world loop. Hunting/fishing orders remain continuing strategies while movement is committed once. A hunted herd can be exhausted, all claims remain finite, and every herd removes both food and drinkable freshwater from its occupied tile or adjacent freshwater source.

Practice is normalized by `PracticeDays = 7300` so the longer historical clock does not automatically grant eighty turns of experience. Food transfers are aggregated by tile, claimant and kind after resolution; reports retain the real total flows over twenty years. Resolution runs on a worker with a progress screen and disables other state mutation until the result is committed.

## Growth and food feedback

The configured annual human growth rate is 0.035. A generation projects continuous exponential growth with `exp(effectiveAnnualRate × years)` and analytically integrates the food consumed by the growing population. The effective rate is limited by recurring production, surplus and reserves. Full surplus support requires production above consumption by 25%; reserves alone cannot generate growth. Deficits block growth, and the first fed generation after sustained deficits is recovery.

Every generated camp starts with at least one complete generation of food at its initial population. Climate controls how much can remain afterward: warm, moist sites store about one generation, while cold or dry sites approach four. Excess is reported as discarded storage. Disconnected hunters claim local food for subsistence before consuming carried provisions; forage never teleports to camp. Finite local production and shared labor establish carrying pressure without a numerical population cap. Starvation grace and fractional deaths are now measured once per player generation.

Wild herd sizes are species-specific: camels split above 80, goats above 90, horses above 120, and antelope/bison above 150. Growth is annualized over the generation. Herds above their threshold divide deterministically, conserve the post-growth animal total and place daughter herds in suitable adjacent habitat using stable IDs and keyed randomness.

Every tribe has a persisted demographic report: start, additions, deaths, end, net percentage, fractional progress, food produced/received/required/eaten, reserves, inventory losses and the limiting factor. The player sees only their tribe's report. The sidebar and journal explain positive growth, food-limited decline, no sustainable surplus, insufficient reserves, recovery or rounding. Food mass balance is also checked: initial reserves + received food − eaten food − discarded inventory = final reserves.

## Passive coastal food

A land camp automatically includes adjacent Shallows within gathering radius 1. It shares one labor budget across terrestrial, lake and marine gathering. Open Ocean and nonadjacent Shallows are excluded; no boat is required to harvest the immediate coast. Peaks are excluded from gathering. Marine gathering never supplies freshwater and does not make a seawater-only site a freshwater-valid start.

Turn records and demographic reports distinguish terrestrial gathering, passive marine gathering, freshwater/lake gathering, active fishing and hunting. Visible coastal tiles expose food stock, qualitative capacity and renewal. The tile hover reports marine eligibility and whether the player's current camp can gather that tile.

## Stacks and delayed hover details

Occupants have a stable order: camps, hunter groups, wild herds, domestic herds, then stable ID within each group. Up to six occupants render in separate slots with opaque backplates and gaps between hit areas. Larger stacks show an overflow count. Clicking a friendly slot selects that exact group. Clicking unoccupied space within the tile cycles friendly occupants. The click inspector lists every occupant and provides a separate selection button for every friendly group, including overflow. Hunt targeting prefers the specific clicked wild-herd slot. Tileset switching preserves occupants, slot layout, selection and semantic information.

Hold the pointer on the same currently visible tile for **more than two seconds**, without moving beyond five pixels, to show a fading detail panel. Delay, movement tolerance, fade-in (0.2 seconds) and fade-out (0.15 seconds) are configurable. Tile changes, leaving the map, pan, zoom, order-mode changes, turn changes and tileset changes reset the dwell. The panel flips beside the tile and clamps to the window; long content widens or uses columns. It is painted with labels/textures after map input and never consumes clicks or scroll input.

The panel includes coordinate/id, biome, numeric elevation/impassability, food status/maximum/renewal, soil moisture, water source/potability, river sides/flow/width, coastal gathering reach, improvements, every visible occupant and relevant queued friendly orders. Recent extraction includes only the player's own recorded transfers at currently visible tiles. Foreign population/provisions remain unknown. `TileDetails` receives a filtered `AgentObservation`, never the authoritative world. The sidebar remains the persistent click-to-inspect view.

Wild herd and foreign livestock counts pass through `HerdCountKnowledge`. The default thresholds display 1–9 as “fewer than 10” (`<10` on compact badges), 10–100 rounded to the nearest ten with `~` (midpoints round upward), and larger herds in broad hundred-count bins such as `100+` or `200+`. These thresholds are centralized in configuration. The observer carries only the representative estimate and labels, not the raw count; the AI's coarse size score is derived from that same estimate. Only the player's own domestic livestock exposes exact managed counts. Inspector, hover and optional icon badges all reuse these labels. Small stack slots omit a badge when it would crowd the glyph; the full stack list retains each species and estimate separately.

## Saves and reproduction

File signature is now `ANTHROPOCENE-4`; world, generator, configuration and observation schema versions are 3. The new filename is `opening-v3.anthro`. Older files remain untouched and incompatible files are rejected instead of silently regenerated. Saves retain elevation levels/ridge links, continuous geography, species/water/oasis/supply state, fractional demographic/death state and complete generation reports.

For the current demonstration, select **Standard, Continents, seed 1729, 32 tribes**. Dispatch eight people, commit, then move that group two tiles east and commit. Planning another two-tile eastward move produces the red disconnected tether and matching sidebar warning. Switch tilesets with V and hold over the camp or coast for details. See `Validation.md` for recorded results.
