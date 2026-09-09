# Milestone three — generations, elevation and tile information

This milestone supersedes the four-band elevation presentation and 90-day player turns described in the older notes. The source Drive document and milestone-two notes remain historical references.

## Elevation and contours

Continuous elevation is unchanged and remains authoritative for climate and drainage. Gameplay elevation is an integer 0–10. Marine Ocean/Shallows tiles are level 0; freshwater lakes inherit an above-sea level 1–9; traversable land occupies 1–9; level 10 is an impassable peak. Camps, hunters, supply paths, herd placement/movement and AI all respect peak blocking. Peak land remains land for coastline classification.

After final hydrology, nine configured land-elevation quantiles (12%, 25%, 40%, 55%, 69%, 81%, 90%, 96%, 99%) define level boundaries. This spreads levels across the world without modifying its continuous height field or biome. Equal heights receive the same level. Levels 1–2 are lowland, 3–4 rolling hills, 5–6 ridges/highland, 7–9 mountains and 10 peaks. Contiguous high land also has reciprocal ridge links when adjacent levels differ by at most two. Existing productivity modifiers apply to these descriptive groups; there is no human population cap.

The renderer draws one nested hex outline per level at tile heights of at least 85 pixels. Level 0 has no elevation contour. At smaller zoom it draws `ceil(level / 2)` outlines to avoid aliasing, restoring the exact count on zoom-in. Level 10 also has an X. Biome fills, rivers on shared boundaries, oasis/freshwater features, food markers and occupants retain separate layers. Muted ridge links connect high ground; river channels use their distinct cyan color and flow widths.

Both tilesets define `elevation.contours` with configurable `Tint`, `Opacity`, `LineWidth` and `Spacing`, plus `elevation.ridge` and keys `elevation.0` through `elevation.10`. Spacing is bounded by tile size so rings cannot invert. Numeric elevation and its description are always available in the inspector and hover panel.

## A generation is one player turn

The default `TurnDays` is 7,300, with 365 days per year: twenty years. The player commits movement, dispatch and a subsistence strategy once. The historical clock advances by the configured duration and the header displays elapsed years. `TurnDays` can be shortened for later eras, including partial seasons. `BaseDays = 91.25` defines four ecological intervals per year, independent of the player turn.

Each interval performs finite hunting/fishing/gathering claims, proportional allocation, food consumption, demographic feedback, herd growth and bounded resource recovery. Seasonal capacities advance at their actual boundaries. A twenty-year turn therefore contains eighty replenishment/consumption intervals, rather than extracting eighty intervals of food from one stock snapshot. Hunting/fishing orders are continuing strategies; movement is committed once. A hunted herd can be exhausted. All extracted food remains finite and remote groups still have to sustain themselves from carried food and their actual harvest.

Practice is normalized by `PracticeDays = 7300` so the longer historical clock does not automatically grant eighty turns of experience. Food transfers are aggregated by tile, claimant and kind after resolution; reports retain the real total flows over twenty years. Resolution runs on a worker with a progress screen and disables other state mutation until the result is committed.

## Growth and food feedback

The configured annual human growth rate is 0.035. Within each ecological interval, the continuous population (whole people plus fractional growth) advances exponentially using `exp(effectiveAnnualRate × intervalYears)`. The effective rate is the configured rate multiplied by the lesser of a recurring food-surplus factor and a reserve factor. Full surplus support requires production above consumption by 25%; full reserve support requires 1.5 base-interval rations at camp. Deficits block growth, and the first fed interval after sustained deficits is recovery. Stored food without recurring surplus does not produce indefinite growth.

Consumption is recomputed from the current population every interval. Growing populations thus consume more and erode their surplus until growth approaches zero. Finite local food stocks, recovery and shared gathering labor establish carrying capacity. There is no separate numerical cap on human population. Starvation deaths use their own fractional accumulator so a tiny shortfall does not round up to a death every interval. Whole additions and deaths have separate events, and conservation is checked every turn.

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

For the combined demonstration: select **Small, Continents, seed 1, six tribes**. Dispatch eight people in turn one and another eight in turn two. The next planning view has the camp and both hunter groups on one tile, several visible elevation levels, passive marine gathering and explained positive population growth. Switch tilesets with V and hold over the camp or coast for details. See `Validation.md` for exact recorded results and commands.
