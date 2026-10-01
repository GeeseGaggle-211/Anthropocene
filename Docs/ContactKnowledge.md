# Contact knowledge (WP6)

Design: [QoLFissionTradeDesign.md](QoLFissionTradeDesign.md) 3.7;
order of work: [ValueModelImplementationPlan.md](ValueModelImplementationPlan.md) WP6.
Source: `Assets/_Anthropocene/Simulation/ContactKnowledge.cs`. Checks:
`Tools/Headless/ContactKnowledgeChecks.cs`.

With `VisionRadius = 2` and 3-hex camp spacing, no camp can see a neighbour, a
fission destination or a trade partner. A Society past its first breakthrough
therefore keeps a remembered picture of its surroundings. Knowledge is a
snapshot of where something was, never of what it is doing now.

## Doctrine summary

- **Authoritative state.** `Society.Memory` (`ContactMemory`, optional, saved
  with the Society in the existing metadata block; `ANTHROPOCENE-11` is
  unchanged). It holds `ExploredTiles` and a contact list. Null before the
  first breakthrough (`Era != Paleolithic` or `Technology != None`), and in
  saves that predate it, which load with no knowledge. `ContactExpiryTurns`
  (default 5, optional config field, validated >= 1) is the contact tuning;
  the hinterland and decaying-memory levers are described below.
- **Temporal abstraction.** One update per turn at the turn boundary: the end
  of `TurnResolver.Resolve`, after movement, placement, reintegration and
  breakthrough, before the turn counter advances, so everything is stamped with
  the turn just resolved. There is no sub-turn knowledge. Expiry is counted in
  turns, because contacts are refreshed only at that boundary.
- **Scaling.** Cost follows active state, not elapsed years or map size:
  O(camps + hunters) indexing, O(vision tiles) per remembering Society (19 per
  camp or hunter group), O(log blocks) per tile mark, O(contacts) per Society.
  Memory per Society is 16 bytes per occupied 8x8 block (sorted key, one
  64-bit word and the last-refreshed turn) plus about 32 bytes per contact. The per-Society pure step
  (vision union and sightings) runs through `PhaseExecution.Map`; the commit is
  serial in Society-ID order, so scalar and parallel runs are identical.
- **Observations.** `AgentObservation.SchemaVersion` is 17. `RememberedTiles`
  are explored tiles outside current vision, carrying only environment, relief,
  elevation and ridge links, water kind and river edges (no vegetation state,
  stocks, freshwater amount, herds, occupants or cultivation). `KnownSettlements`
  carry camp id, owner, tile (-1 when Moved), turn and source. The remembered
  array is built once per Society per planning index and shared by its camps.
  Rendering all explored tiles per camp is O(explored tiles); the current
  mature-scale figure is about 100 tiles per Society.
- **RL.** Remembered tiles and contacts are plain observation features; nothing
  hidden (live stocks, rival orders, populations) is added.
- **Rejected architectures.** A dense per-Society bit array (map size x
  Societies: 2048x1024x320 is 84 MB) was rejected for the sparse block set.
  Per-player (rather than per-Society) memory was rejected because fission
  (WP10) creates Societies that share a player but not knowledge. A
  copy-on-write explored set for kin was rejected for a plain copy: a fission
  copies O(blocks + contacts) once, which is tiny against the rest of a fission,
  and sharing would need ownership tracking for a few hundred bytes.

## Hinterland and decaying memory

Explored geography is **not permanent**. Two rules decide what a Society still
knows (user decisions: a settlement's hinterland knowledge grows with its
population while it stays put; a nomadic people keeps a decaying memory of
previously visible tiles plus a small vision bubble).

- **Hinterland (`ContactKnowledge.HinterlandRadius`).** At the end of every turn each camp marks as
  explored every tile within `R_h = clamp(min(VisionRadius + HinterlandRingsPerTurn * k, VisionRadius * sqrt(N / HinterlandReferencePopulation)), VisionRadius, HinterlandMaxRadius)`,
  with `k` the turns the camp has stayed on its tile (`Camp.StationaryTurns`, already saved; a move resets it to
  zero, so the radius falls back to `VisionRadius`) and `N` its residents. Defaults: one ring per turn, reference
  population 24 (the starting population, so a starting-size camp never leaves its vision and area worked is
  proportional to people), maximum radius 6. Hunter groups see only the vision radius. Sightings of foreign
  camps and the "Moved" rule still use the vision radius alone: the hinterland is geography the people
  know from working the land around them, not a view of who is there. `HinterlandMaxRadius` is validated at most 8 so
  a camp marks at most `3r^2 + 3r + 1 = 217` tiles per turn (127 at the default 6).
- **Decay.** Each 8x8 block of the explored set carries its last-refreshed turn (`ExploredTiles.Turns`, an
  optional list parallel to `Chunks`; 4 bytes more per block, now 16 in all). A block is refreshed whenever a
  tile in it is marked, by vision or by a hinterland. At the end of the update, a block not refreshed for
  `ExploredMemoryTurns` (default 5 turns = 100 years, the same order as `ContactExpiryTurns`) is forgotten whole.
  **Granularity approximation:** the clock is per block, not per tile, so a tile can outlive its own last sighting
  while any other tile of its 8x8 block is refreshed (per-tile stamps would cost 64x the memory for little
  gain). A settlement refreshes its hinterland every turn and so keeps it; a nomadic camp keeps only the trail of
  the last `ExploredMemoryTurns` turns plus its vision.
- **Old saves.** The stamp list is absent and loads null. Unstamped blocks are stamped at the next update with that
  turn (the first turn after load), so nothing is forgotten when a save is loaded and old blocks age from there.
  A block added without a turn (`ExploredTiles.Add(tile)`) is likewise stamped at the next update.
- **Why.** At the measured baseline 90 percent of camps that wanted to split had no known destination, because a
  settled village explores only its vision radius 2, exactly the camp-spacing exclusion zone. The hinterland gives a
  large settlement the ring-3 and ring-4 land its fission search needs ([Fission.md](Fission.md)).
- **Observations.** `RememberedTiles` shrink as blocks are forgotten. Nothing else about them changes: still
  static geography only, still the labelled remembered-knowledge exception; no new information type is exposed.
- **Cost.** O(sum of `R_h^2`) tile marks per turn, each a binary search in the Society's sorted blocks, plus an
  O(blocks) forgetting sweep per remembering Society. Nothing scales with elapsed years or map size. Measurements
  are in the table at the end of this document.

## Rules

- **Sighting.** Every tile in a Society's current vision (its camps and its
  hunter groups) is marked explored (and, for a camp, its hinterland, above). Every camp of another Society in vision is
  recorded or refreshed with its owner, current tile, this turn and source
  `Sighting`. A camp of the same Society is not a contact.
- **Moved.** A contact not refreshed this turn whose recorded tile is now in
  vision (the camp is not there, or no longer exists) is marked Moved and its
  tile is cleared. A moved contact is never updated without a new sighting,
  trade or kin event; a fresh sighting locates it again.
- **Expiry.** A contact is dropped when `turn - Turn >= ContactExpiryTurns`.
  Nothing else removes a contact, and nothing but a sighting, `RecordTrade`,
  `RecordKin` or `RecordMigration` ever changes one.
- **Hooks (`RecordKin` is called by fission, WP10a; `RecordMigration` by immigration, WP10b, source kind `Migration`, once per direction with the same semantics as `RecordTrade`; `RecordTrade` has no caller yet).** `RecordTrade(world, society, partnerCamp)`
  records the partner's current tile with source `Trade`; call it once per
  direction. `RecordKin(world, parent, daughter, parentCamp, daughterCamp)`
  gives the daughter a plain copy of the parent's explored tiles and contacts
  at fission, then each records the other's camp with source `Kin`. A Society
  that has not passed its first breakthrough ignores both hooks.
- **Presentation.** `TileDetails.IsRemembered`, `DescribeRemembered` and
  `KnownSettlementsAt` (simulation side) label remembered tiles REMEMBERED and
  show static geography and labelled snapshots only; `Describe` falls back to
  them for a tile outside vision. The Unity map renderer and hover gating still
  draw only live tiles; dimmed remembered tiles there are not implemented and
  are unverified (no Unity build in the authoring environment).

## Validation

`ContactKnowledgeChecks.cs` (86 assertions, fixed suite): sparse set geometry
(seam, non-multiple-of-eight widths, clone independence, 25 tiles in a 2048-wide
map occupy at most four blocks); Paleolithic Societies remember nothing;
sightings (owner, tile, turn, source, hunter vision, same-Society exclusion);
Moved, re-sighting, destroyed camps and expiry timing; trade and kin hooks and
kin equality with the parent at fission; remembered tiles never expose live
stocks, vegetation, herds, cultivation or occupants even when live state changes
(reflection over the field names plus mutation checks); observation/DTO round
trip at schema 17; save round trip, fingerprint coverage, legacy save without
the block and continuation equal to an uninterrupted run; scalar versus reversed
4-worker replay with reordered collections, through real turn resolution and for
the update alone.

## Measurements

Standard Continents, seed 11, production config, 12 opening turns then 25
post-opening turns (`Tools/PerformanceAudit --players=N --seed=11
--shape=Continents --production-config=1 --turns=12 --post-opening-turns=25`;
single run each, this development host, not paired timing evidence). The audit
samples now record `knowledgeMs` and a `knowledge` footprint (transient, through
`ContactKnowledge.Measure`).

| Tribes | Societies remembering | Explored tiles / Society at turn 35 | Blocks / Society | Payload bytes / Society | Contacts (all Societies) | Update ms: median / mean / max (post-opening) | Execute ms median |
| ---: | ---: | ---: | ---: | ---: | ---: | --- | ---: |
| 80 | 80 | 95.5 | 5.3 | 63 | 1 | 0.46 / 0.95 / 5.4 | 153 |
| 320 | 320 | 105.6 | 5.3 | 64 | 4 | 1.2 / 1.8 / 7.2 | 519 |

Explored area grows with movement only (80 tribes: 1,893 tiles in total at the
first post-opening sample, 7,644 after 25 more turns) and is independent of map
size. The update is about 0.3% of resolution time. Payload bytes exclude object
headers and list slack (a few hundred bytes per Society) and the XML encoding
in saves. Contacts are rare because starts are at least 12 hexes apart and
camps rarely approach within vision; the rules are therefore exercised by the
fixtures rather than by these worlds.
