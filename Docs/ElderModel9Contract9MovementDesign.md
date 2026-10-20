# model9 contract 9: destination-and-economy movement (design for review)

Status: approved 18 October 2026. The simulator side is implemented as **model9 contract 10** (contract 9 already
carries the capital commitments); see "Implementation" at the end. It follows the M7 stage-4 result: the learned
movement pointer reached 0.920 of the teacher on default worlds while native movement reached 1.083 (Anthropocene-RL
`docs/MODEL9_M7.md`).

## The decision to support

A struggling pastoral society, pushed off good grassland by competing tribes, finds an unoccupied fertile river valley.
It should at least weigh moving there and settling: changing where it lives and how it lives in one decision, and
paying the transition cost.

## Why contract 8 cannot learn it

1. **One tile per turn, no destination.** `CampMovement` is 1 and a turn is 20 years. The seven site candidates
   (`ElderRLContract.MaxLocalSites`) are the current tile and its six neighbours. A valley three tiles away needs
   three consistent choices over 60 years, each credited only through the discounted lineage return. The policy
   has no way to state "go there" once.
2. **Site rows cannot see farmland.** A row holds gathering food, herd forage and water, fixed shelter, recurring
   labour, retained fields and, since contract 8, fishing access. It holds no crop yield, field-building cost or
   cultivation potential, so to a herding camp a river valley looks like poor pasture.
3. **Where and how are separate heads.** The economy mix is chosen without knowing the destination, so "move, then
   mostly farm" is two independent bets.
4. **No competition or transition signal.** Rows do not show known foreign settlements near a site, the distance or
   path, or the settling cost the planner already computes (`ExpansionValuation`, `Docs/HusbandrySettling.md`).

A spatial encoder alone fixes none of these: it would see the valley but still could not commit to it or decide to
farm on arrival. It is therefore the second step, not the first.

## Proposed contract

### Authoritative state

One new persisted field per camp, in the Elder memory: a **destination intent** (target tile, turn set, and
economy-on-arrival preference), saved with the existing Elder memory block. Everything else is observation or
action schema. Native movement, the resolver and the heuristic are unchanged. The heuristic's own choice is always
representable through a "native" slot, which keeps teacher labels exact.

### Temporal abstraction

- One decision per 20-year turn, as now.
- An intent is an **option** that persists across turns. Each turn the camp continues toward it with the native
  one-tile path step, or the policy re-targets or cancels it.
- An intent lapses on arrival, when its target becomes infeasible (occupied, blocked, or lost from memory), or after
  `IntentMaximumTurns` (proposed 6).
- No sub-turn simulation and no new clock.

### Observation

**Regional candidates.** K = 8 rows replace the seven adjacent rows:
- the current tile and the native choice;
- the best two remembered or explored tiles within radius R = 4 for each of pastoral, farming and fishing value.

Under the remembered-knowledge contract, a remembered tile carries static geography only, so far candidates' values
are estimates from that geography, labelled as such. A visible tile gets the live forecast.

**Economy-conditioned site values.** Food per resident meal under each economy:
- **pastoral:** herd forage (zero-displacement and absolute) with water;
- **farming:** crop suitability from soil `Moisture` (`EcologyMath.CropSuitability`), with field-building effort
  (open, woodland or forest) and the footprint's potential food;
- **fishing:** the existing `FishingAccess`.

Each is on the same scale as today's `HumanFood`, so the three are comparable within a row.

**Transition and competition**, per row:
- path length in turns and path feasibility for the herd (water along the path);
- the husbandry settling cost and the lost field capital (`LostRebuildBurden`);
- known foreign settlements within two tiles, as last-known snapshots from `ContactKnowledge`, and whether the tile
  is the camp's own lineage's.

**Current intent:** target row index, turns held, and whether the last step advanced.

### Action

The movement head is a categorical over the K rows plus "keep intent" and "native". The economy mix head becomes
autoregressive: it receives the chosen row's encoding, so the mix is chosen given the destination. During training
the log-probability is the sum of the two. In deterministic evaluation the row is chosen first, then the mix.

### Second step: spatial encoder

If the candidate design fails its gate, add a hex-convolution or graph encoder over the radius-4 window (61 tiles).
Each tile carries environment, water, elevation level, crop-suitability band, forage band, and a known-settlement
flag, all static or remembered, never live foreign state. Its pooled output conditions both heads.

### Learner

- An auxiliary head regresses each row's forecast food under the chosen economy; the target is the simulator's own
  forecast.
- The value head stays lineage welfare; GAE already spans the turns of an intent.
- The contract-8 mix-change cost applies to the realized mix, not the intent.

### Curriculum and validation

- **Valley fixtures:** scan generated worlds for a husbandry camp in grassland with at least one known foreign camp
  within two tiles, and an unoccupied tile of high crop suitability within radius 4. These are selected openings,
  not edited worlds, as with the grassland bank. The reverse (fields losing condition, with pasture nearby) is
  also scanned.
- **Metrics on those fixtures:**
  - the share of eligible camps that reach the valley and farm there within 6 turns;
  - their welfare against staying and against the heuristic;
  - false settling on poor land.
- **Gate:**
  - default worlds at least 1.0 of the same policy with native movement;
  - every regime within 3%;
  - no more extinctions;
  - on valley fixtures, welfare above native movement.

### Scaling

Per village and turn, K = 8 candidate rows:
- visible candidates reuse `AgentObservation.LocalCoverage`, which already costs about 7 forecasts;
- remembered candidates use static fields only.

So observation cost is the same order as contract 8's. The regional search over radius-4 explored tiles (61) is
indexed by `ContactKnowledge.ExploredTiles`. At mature-empire scale the cost is linear in villages; there are no
global scans.

### Rejected architectures

- **A free pointer over every visible tile:** variance, cost and unbounded action size.
- **A full-map convolution:** scaling, and it would expose state the observation contract forbids.
- **Reward shaping for movement or settling:** it changes the objective rather than the information.
- **A multi-tile jump per turn:** it changes game semantics (`CampMovement`), which is a design decision outside
  the learner.

## Companion design

Contract 9 also carries activity tokens (a scalable allocation action) and multi-turn capital commitments; see
`ElderCapitalCommitmentsDesign.md`. The economy-conditioned site values above are the movement-side counterpart of
the activity rows' marginal returns, and should share their features.

## Decisions for review

1. The intent option and its persisted field: a save-schema addition to the Elder memory.
2. K = 8 and R = 4, against the information rules: remembered tiles carry static geography only.
3. Whether valley fixtures should be generated by scan (proposed) or authored.

## Implementation (model9 contract 10, 20 October 2026)

`ElderMovement.cs`, with `ElderModel9.cs` (encoder, step), `ElderObservationBuilder.BuildRegions`,
`ElderInstitutionMemory` schema 3 and the directive compiler.

**Authoritative state**
- Elder memory schema 3 adds `HasIntent`, `IntentTile`, `IntentTurns` and `IntentSetDays`. Schema 1-2 memories load
  with no intent.
- The intent is written only by `ElderMovement.Record`, inside `RecordDecision` during resolution, so replay
  reproduces it.
- The directive carries the target as `IntentTile`. The environment fills it from the observation sidecar; the
  policy names rows, never tiles.

**Rows** (`regions` set, 8 x 24; feature names in `ElderModel9Encoder.RegionFeatureNames`)
- Row 0 is always the current tile. The intent's target follows, while it is still a candidate.
- The remaining rows are, for each of pastoral, farming and fishing value, the best visible and the best remembered
  candidate, taken round robin.
- Candidates are tiles within radius 4 that are enterable and reachable on a shortest path over known tiles, and not
  closer than camp spacing to a known settlement.
- Every row carries static geography and known context:
  - current, intent and visible flags;
  - distance and path turns;
  - land environment (one-hot);
  - river, freshwater (river or lake), coast and relief;
  - known fishing access;
  - known foreign and kin settlements within two tiles.
- Visible rows add live forecasts:
  - human food, herd forage and herd water (`LocalCoverage`);
  - a farming forecast: the footprint's biomass capacity times `CropSuitability`, over meals;
  - field-clearing effort.
- Remembered rows hold zeros there, flagged by `visible`. Visible and remembered tiles are ranked separately, so
  static estimates never compete with live forecasts.
- The rows are observed in every stage, whether or not movement is learned.

**Actions** (action schema 10; `MovementCandidate` codes):
- -2 native, -1 stay, 0-6 contract-7 site rows (unchanged);
- 100 + r: region row r. This sets the intent and steps one tile along the known path; row 0 means stay;
- -3: keep the intent.

A row is legal when its first path step is an ordinary legal move. Keep is legal while the intent's row is.
Native, stay and site rows cancel the intent. Arrival, infeasibility (the row disappears, so keep becomes illegal and
is repaired to stay) and `IntentMaximumTurns` = 6 end it. A directive below schema 10 naming a region row is repaired
to stay, and the repair is logged.

**Deviations from the proposal**
- **No "native choice" row.** The heuristic's plan is not known at observation time. Including it would mean running
  the planner during observation and again during replay to rebuild identical masks. The native *action* remains.
- **No herd-water-along-path or settling-cost features yet.** Path feasibility is enterability over known tiles.
- **The autoregressive mix and the auxiliary forecast head** are learner-side; see the RL repository.

**Cost** (seed 2000, 15 turns, 8 players, about 27 villages, same world before and after):
- observation 23.5 -> 34.5 ms per turn;
- whole step 282.6 -> 300.1 ms per turn (+6%).

Per village, the work is a breadth-first search over at most 61 tiles, cheap scores for at most 60 candidates and at
most 8 `LocalCoverage` evaluations. It is linear in villages, with no global scans.

**Checks** (`ElderModel9Checks.MovementChecks`, 102 model9 assertions in all):
- region rows are deterministic and independent of the movement flag;
- remembered rows carry no live forecast;
- intent set, step, keep, save round trip and native cancel;
- schema-9 repair;
- exact replay.

The fixture moves camp 16 toward a target two tiles away; 24 of its 72 rows are remembered.
