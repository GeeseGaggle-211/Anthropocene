# Fission (WP10a)

Design: [QoLFissionTradeDesign.md](QoLFissionTradeDesign.md) 3.1-3.6, 6.4, 9;
order of work: [ValueModelImplementationPlan.md](ValueModelImplementationPlan.md) WP10.
Source: `Assets/_Anthropocene/Simulation/Fission.cs`. Checks:
`Tools/Headless/FissionChecks.cs`. Immigration is WP10b and is not part of this
package. Pass contract: [TurnResolutionPasses.md](TurnResolutionPasses.md), Close row.

**Status: opt-in.** `FissionEnabled` is false by default and in `OpeningConfig.json`
until the levers are calibrated (WP11): at the design levers fission founds 615
daughters in 50 turns on Standard/80, splits agricultural camps at a median of
143 residents and Paleolithic camps at 40-53 (design onsets about 600 and 101),
and multiplies population about 7x (measurements below).

A camp whose committed quality of life (QoL) has fallen below `Q_fiss` may send a
coherent kin group to a viable empty site. The group founds a new Society in a new
AI player slot. The parent keeps its site, fields, fixed Housing and controller; the
player keeps the original camp and never controls a daughter.

## Doctrine summary

- **Authoritative state** (all saved, all optional, absent means none):
  `Camp.FissionPolicy` (Neutral / Discourage / Encourage; the enum's zero is
  Neutral), `Camp.FissionDestination` (`FissionTarget`: tile, regime at caching,
  turn cached; replaces `Camp.FissionWilling`), `Society.ParentSociety` and
  `Society.KinAffinity` (lineage, inert), `WorldState.PlayerCount` (the live
  player-slot count; 0 = not recorded, then `LivePlayers` falls back to
  `Config.Players` and the highest camp player), `TurnRecord.Fissions` (canonical
  journal) and `DemographicReport.SplitOff`. `ANTHROPOCENE-11` is unchanged; no
  version or filename moves. Push, desirability and proposals are derived and never
  saved.
- **Temporal abstraction.** One evaluation per turn boundary from the frozen
  post-demography snapshot. QoL is the value committed by the turn just resolved and
  recomputes next turn. Nothing is integrated over sub-turn time, and a daughter
  cannot itself fission in the turn it is founded.
- **Scaling law.** `T = O(C + F_v * footprint + F_s * ring * footprint + P)` where `C`
  is camps (one push test each), `F_v` camps with push > 0 and a feasible daughter
  that hold a cache (one footprint validation, at most 7 tile projections), `F_s` camps
  above the search threshold without a valid cache (at most the ring at distance
  `FissionRadius`, 18 tiles at radius 3, each one footprint) and `P` proposals
  (reconcile and commit). Nothing scales with elapsed years, map size or Society
  count. Memory per fission: one Society, one camp, the daughter's copy of the
  parent's explored set (12 bytes per occupied 8x8 block) and contacts, and copied
  material knowledge.
- **Mature scale.** Fission multiplies camps, Societies and player slots: 80 tribes
  reach several hundred settlements within the first fifty post-opening turns (see
  Measurements). Per-camp planning, observation building and AI decisions already run
  per player on bounded workers and scale linearly in camps; the new pass adds
  a per-turn cost proportional to pressured camps only.
- **RL.** The fission policy is a legal action (`OrderKind.FissionPolicy`); the
  heuristic AI keeps Neutral. New AI slots simply appear as more controlled players.
- **Rejected architectures.** A dense per-player array sized at generation (no such
  array exists: players are a camp/Society `int`, so slots are created mid-game without
  resizing anything). Per-turn reranking of all candidate sites and an all-camps
  site scan were rejected for a cached destination plus a bounded lazy ring search.
  Iterative rerouting of dropped proposals, same-turn multi-hop fission and
  recomputing QoL after each split are forbidden by design section 9.

## Step 0 audit: live player count

Every assumption of a fixed player count was checked before building.

| Area | Finding |
| --- | --- |
| `Config.Players` | Used only by generation (start count, `Validate` bound against map size) and by tools. It stays the *initial* count. `WorldState.PlayerCount` is the live count; `LivePlayers` and `AllocatePlayer` are the only readers. |
| Dense per-player state | None. Players are an `int` on `Camp`, `Society`, `Hunter`; `PlanningIndex`, `AiPlanning.Decide` and `TurnPlanner.Prepare` group camps by player. Adding a player adds dictionary entries, not arrays. |
| Visibility, observations, AI loops | Per-player and per-turn, from the committed world. New slots are picked up automatically; `TurnPlanner.Draft` and `Prepare` are unchanged and draft reuse stays exact (a daughter exists only after Close). |
| Era progression | Per-Society days from `EraStartDays`; majority eligibility (200 years) and forced completion (240) contain no count of players. A daughter copies its parent's progression, so a daughter of a Paleolithic parent is forced no later than the parent. `WorldState.Finished` (all camps past the opening) is therefore still reached by turn 12; it can be reached one or two turns later than before because a late Paleolithic daughter must also complete. |
| History and records | `TurnRecord` lists are keyed by camp ID and already tolerate camps appearing; a daughter has no demographic report in the turn it is founded. |
| Save codec | Optional fields only. `Fingerprint` and the metadata copy include `PlayerCount`. Older saves: no lineage, policy, destination or player count; config loads with `FissionEnabled = false` (as `PlannerFulfilment` loads Uniform) so they continue exactly. |
| `PlaytestScenarios`, presets | Select start camps by exchanging ownership at generation; generation is unchanged. They check `Config.Players`, the initial count. |
| Unity `OpeningGame` | Player 0 is always the human and its camp never changes owner; daughters are new AI players. No player list is sized from `Config.Players` (only `NewGame` sets it). No compile was possible here. |

No assumption needed a redesign, so the work proceeded.

## Rules

- **Policy** (`Kinship`): reluctance shifts `Q_fiss` by -0.05 / 0 / +0.05, the
  daughter takes 15 / 30 / 40 percent of the residents (integer arithmetic,
  `floor(N * pct / 100)`) and its affinity with the parent is 0.5 / 0.9 / 1.0. The
  policy never forces fission. A player sets it with `Order(FissionPolicy, camp,
  target: policy)`, which may accompany one ordinary order for the same camp (two
  policy orders for one camp conflict). It is committed at the start of resolution,
  so it applies to that turn's pass. The heuristic policy never issues it.
- **Push.** `P = clamp((Q_fiss + shift - Q_home) / FissionQoLWidth, 0, 1)` from the
  camp's committed `QoL`.
- **Daughter.** `N_daughter = floor(N * f)`. It must be at least
  `FissionMinimumDaughter` (12), smaller than the parent that remains, and leave the
  parent at least `MinimumCamp`. Hunters stay with the parent.
- **Cached destination.** Searched only when `P > FissionSearchPush` (default equal to
  `FissionThreshold`) and no valid cache exists. Candidates are traversable land
  tiles (gameplay levels 1-9) within `FissionRadius` that the Society can know and
  that are at least `MinimumCampSpacing` from every camp (ground truth, which is a
  superset of own, visible and contact camps: a settling group finds a neighbour
  before it founds beside it). Known means: Paleolithic, current vision only (camps
  and hunter groups); after the first breakthrough, current vision plus explored
  tiles. With the default spacing 3 and radius 3 this is the ring at distance 3. The
  best tile by `D` wins, ties by tile ID; nothing is reranked while the cache is valid.
  It is invalidated when the regime changes (even with no push), when a camp now stands
  on it or within spacing of it, or when `D` falls below the start-viability
  threshold (`StartViability / 100`, 0.85).
- **Desirability.** `D = clamp(SiteSupport(daughter) / TargetSupport, 0, 1)`, with
  `TargetSupport` the daughter's own annual-generation need so `D = 1` when fully
  supported. Support uses the start-viability footprint evaluation
  (`PlacementStage.Footprint` and `Harvest`: four-season freshwater gate and mean
  recharge, repeatable land stock, aquatic renewal, working share, labor ceiling),
  with the repeatable stock taken from the *current projected* ecology rather than
  the generation-time stock:
  Paleolithic (and Nomadic without herds): the start-viability result for the
  daughter cohort; Nomadic with herds: the least of that human-food coverage, herd
  forage coverage (sustainable biomass flow less the human harvest) and herd water
  coverage (freshwater recharge) for the daughter's share of the herds; Agricultural:
  repeatable wild and aquatic food plus the managed flow cultivable footprint land
  yields at the primitive supported intensity, capped by the daughter's farm labor
  ceiling. These are planning estimates from projected ecology, not forecasts of
  realized harvest. The regime is `Fission.RegimeOf` (the same split as the cohesion
  scale).
- **Proposal.** `F = P * D > FissionThreshold`, plus the size rules above.
- **Reconcile and commit.** Canonical camp-ID order. A proposal whose tile is the same
  as, or within spacing of, an earlier commit is dropped this turn and keeps its cache
  for revalidation next turn (there is no rerouting). Commit creates, in this order:
  the Society, the camp, then one herd per split domestic herd (IDs from `NextId`), and
  the player slot (`AllocatePlayer`, never reused).
- **What moves.** Residents (workforce by largest remainder over gatherers and every
  role; the daughter has no fields so its share of Farmers becomes Gatherers); typed
  food; every `CampInventory` field (tools, pots, Cloth, Animal Fabric, Light Housing,
  portable Housing Material, materials, ...); fractional growth and death progress;
  domestic herds in whole animals (floor), with their fractional and diagnostic
  fields. The parent keeps its site, fields, fixed Housing and the rounding remainder.
  Every ledger is conserved.
- **Knowledge and practice** are identical to the parent's at the moment of fission,
  then diverge: experience per domain, species and craft, technologies, Neolithic
  technologies and specialists, traditions and policy preferences (`PolicyState`,
  `Mandate`), progression and era, transition seeds, material knowledge, and the
  explored tiles and contacts (`ContactKnowledge.RecordKin`, which also records parent
  and daughter as each other's kin contacts). Only `PolicySeed` is new, from the keyed
  stream `Numbers.Hash(seed, 500, parent camp ID, turn)`.
- **Lineage.** `ParentSociety` and `KinAffinity` (the parent camp's policy affinity)
  are stored on the daughter. Two daughters of one parent take the lower of their two
  stored affinities, **derived** in `Kinship.Affinity`, never stored. Kinship is first
  degree only (parent, daughter, sibling); anything else is zero. Affinity is inert.
- **Economy.** The daughter copies the parent's economic settings and runs continuous
  automation so the AI economy starts at once. Its fission policy is Neutral and it
  has no destination.
- **Journal.** `TurnRecord.Fissions` (parent camp order), one event line, and
  `DemographicReport.SplitOff` on the parent (`EndingPopulation` remains the
  pre-split figure; the Explanation adds the people who left).

## Observations and actions

`AgentObservation.SchemaVersion` is 18. Owner only: each `SettlementObservation`
carries `FissionPolicy` and `FissionDestination` (tile or -1); the observation carries
`ParentSociety`, `KinAffinity` and `KinRelations` (parent, daughters, siblings: society,
player, relation, affinity; no location, stores or population). A foreign daughter
appears only through ordinary vision and contacts. `OrderRules`, `EntityActionMask` and
`LegalActions` know `OrderKind.FissionPolicy` (three actions per settlement).

## Measurements

All runs: this container (4 cores, .NET 8.0.31, Ubuntu), `Tools/PerformanceAudit` at the
WP10a working tree, `--workers=4`, production config, `--fingerprints=0`. `--fission=0` is
the same-code control; it reproduces `TestResults/Spacing/Continents-1.json` exactly (60 of 60
samples identical in population, food yield, QoL, births, deaths and animals, 10 opening turns).
Raw reports and logs are under the ignored `TestResults/WP10a/` (`Continents-1-on.json`,
`Continents-1-off.json`, `320-on.json`, `320-off.json`).

```
dotnet build -c Release Tools/PerformanceAudit/Anthropocene.PerformanceAudit.csproj -o TestResults/WP10a/audit
dotnet TestResults/WP10a/audit/Anthropocene.PerformanceAudit.dll --players=80 --seed=1 --shape=Continents \
  --production-config=1 --turns=12 --post-opening-turns=50 --details=1 --fingerprints=0 --fission=1
dotnet TestResults/WP10a/audit/Anthropocene.PerformanceAudit.dll --players=320 --seed=11 --shape=Continents \
  --production-config=1 --turns=12 --post-opening-turns=13 --details=0 --fingerprints=0 --fission=1
```

### Standard / 80 tribes / Continents / seed 1 (12 opening + 50 post-opening requested)

With fission the opening took 12 turns (10 without) because a late Paleolithic daughter must also
complete, so the run is 62 turns against 60.

| Turn | Camps = Societies = player slots | Fissions that turn | Population (fission / control) | Weighted QoL (fission / control) |
| ---: | ---: | ---: | ---: | ---: |
| 1 | 80 | 0 | 2,214 / 2,214 | 0.60 / 0.60 |
| 4 | 84 | 4 | 3,161 / 3,161 | 0.23 / 0.21 |
| 5 | 101 | 17 | 3,689 / 3,689 | 0.19 / 0.08 |
| 8 | 141 | 13 | 5,388 / 5,516 | 0.20 / 0.12 |
| 12 | 193 | 15 | 10,792 / 10,579 | 0.73 / 0.82 |
| 20 | 274 | 6 | 39,229 / 26,921 | 0.90 / 0.87 |
| 30 | 396 | 18 | 121,890 / 54,297 | 0.82 / 0.74 |
| 40 | 503 | 8 | 277,977 / 76,023 | 0.71 / 0.56 |
| 50 | 615 | 18 | 451,365 / 82,689 | 0.61 / 0.53 |
| 60 | 686 | 9 | 602,919 / 87,859 | 0.55 / 0.49 |

Turn 62: 695 camps, 695 Societies, 695 player slots. Fissions per turn range 3-18 after turn 5 (615
in total over 62 turns; 351 distinct parents, at most 7 by one parent). Control: 80 camps throughout.
Final cohort (turn 62): Cultivation 450 camps, AnimalHusbandry 245; population P25/median/P75
367/757/1,258 (control 533/1,084/1,471), QoL 0.51/0.76/0.92 (control 0.48/0.62/0.87).

Fission events by regime (all Neutral policy, `D = 1` in every event, push 1.0 for Paleolithic and
Nomadic, mean 0.80 for Agricultural; minimum score 0.204):

| Regime | Fissions (opening / post-opening) | Parent residents at fission P25 / median / P75 (min-max) | Daughter P25 / median / P75 (min-max) |
| --- | ---: | --- | --- |
| Paleolithic | 37 / 0 | 42 / 44 / 46 (40-53) | 12 / 13 / 13 (12-15) |
| Nomadic | 16 / 0 | 42 / 44 / 46 (41-97) | 12 / 13 / 13 (12-29) |
| Agricultural | 60 / 502 | 49 / 143 / 887 (40-4,047) | 14 / 42 / 266 (12-1,214) |

212 fissions moved domestic animals (42,659 animals in total).

Pass work over the run (80 tribes): 7,992 searches (the ring holds 18 tiles at radius 3; 3,931
candidates were scored in total, at most 4.75 per search on average in any turn, because most camps
find no known free tile), 17,636 tile projections, 39 cache validations and 39 invalidations (caches
are almost always founded in the turn they are found), 656 proposals, 41 dropped on conflict, 615
committed. The pass cost 371 ms of 54.2 s total turn time (0.7%; 2-6 ms per turn, 102 ms once at
turn 6).

Turn time (AI + preparation + resolution, median per turn; control 233 ms at 80 camps, 2.9 ms per
camp, 115 MiB allocated per turn):

| Turns | Camps | Median turn ms | ms per camp | Allocated MiB per turn |
| --- | --- | ---: | ---: | ---: |
| 1-10 | 80-169 | 394 | 3.6 | 74 |
| 11-25 | 178-334 | 538 | 2.1 | 242 |
| 26-40 | 345-503 | 816 | 1.9 | 346 |
| 41-62 | 512-695 | 1,375 | 2.1 | 508 |

Turn time therefore follows camps linearly (about 2 ms per camp). Managed heap peaked at 863 MiB
against 640 MiB for the control (the run holds about seven times the people and 8.7 times the camps;
explored-tile memory for 695 Societies is 3,463 blocks, 65,823 tiles, 197 contacts, 48 KB).

### 320 tribes (benchmark scale), seed 11, Continents, 2048 x 1024

Fission-on opening ended after 10 turns; the on run is 10 + 13 = 23 turns, the control 10 + 15 = 25.

| Turn | Camps = player slots (on) | Fissions | Turn ms (on / control) | Preparation ms (on / control) |
| ---: | ---: | ---: | ---: | ---: |
| 1 | 320 | 0 | 990 / 896 | 56 / 71 |
| 4 | 341 | 21 | 778 / 836 | 47 / 50 |
| 10 | 652 | 43 | 2,131 / 1,364 | 1,023 / 537 |
| 15 | 849 | 29 | 2,859 / 1,395 | 1,600 / 484 |
| 20 | 997 | 36 | 2,851 / 1,187 | 1,545 / 414 |
| 23 | 1,094 | 37 | 4,321 / 1,281 | 2,052 / 475 |

320 to 1,094 camps in 23 turns (774 fissions: 588 Agricultural, 124 Paleolithic, 62 Nomadic). Median
ms per camp 3.2 (control 4.0). The fission pass took 420 ms of 53.3 s in total (2-22 ms per turn; 159 ms
at turn 23). Preparation grows with the number of automated camps (about 1.9 ms per camp at 1,094 camps
against 1.5 at 320). Peak managed heap 2.36 GiB against 2.13 GiB (control). The world holds 1,412 MiB
after load in both. Generation 34.1 s against 33.4 s.

### Findings

- **Paleolithic QoL sits at or below `Q_fiss` at any size.** A Paleolithic camp produces no clothing or
  housing, so the rudimentary term is `1 - E (1 - 0) = 0.75` at the 0.25 expectation floor (lower as
  stationary years accumulate) and `Q <= 0.75 S(N) * food`. Push is therefore positive from the start and
  reaches 1 at small sizes: Paleolithic and Nomadic fission happened at 40-53 residents, the smallest size
  that yields a 12-person daughter at 30 percent, not near the design's N* = 101. The design anchors
  assumed `R_ref = 0.90` from partial goods coverage. WP11 must decide whether Paleolithic camps get a
  different reference or whether fission needs a minimum size.
- **`D` saturates.** At the shipped calibration (`LandBiomassMultiplier` 63) every evaluated site supported
  its daughter: `D = 1` in all 615 events. Destination choice then reduces to the tie rule (lowest tile ID
  among known, free, traversable ring tiles), which biases daughters toward low tile IDs. A secondary
  ordering by uncapped support would remove that; it was not added because the specification names the
  tile ID tie-break.
- **The opening lasts longer.** A Paleolithic daughter has few people, accumulates practice slowly and is
  forced only at 240 years, so `WorldState.Finished` (every camp past the opening) moved from turn 9-10 to
  turn 12 in the seed-53 four-tribe fixtures and from 10 to 12 at 80 tribes (not at 320 seed 11). Whether
  `Finished` should keep waiting for daughters is an open decision.
- **Populations rise several-fold.** More independent camps use more independent footprints, so total
  population is about 6.9 times the control at turn 60. Calibration (WP11) is out of scope here.

## Deliberate limits

- Calibration (`FissionQoL`, `FissionQoLWidth`, `FissionThreshold`, radius, minimum
  daughter, per-regime `N*`) is WP11; nothing here is tuned.
- Sites are scored from ground-truth ecology of the footprint; only the chosen tile
  reaches the owner's observation.
- Immigration (WP10b), affinity decay, trade with kin (WP7-WP8) and the Elder (#23)
  are out of scope. `Kinship.Affinity` is the hook WP7 will read.
- Unity: no inspector control for the policy, no journal display, no drawing of a
  cached destination, no kin panel, and `OpeningGame` has not been rebuilt. The
  simulation-side APIs above are complete.
