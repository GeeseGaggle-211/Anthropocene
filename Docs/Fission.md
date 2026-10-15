# Fission (WP10a, push recalibrated in WP10c)

Current design: [UnifiedSocialRelocationContract-v0.2.md](UnifiedSocialRelocationContract-v0.2.md).
Source: `Assets/_Anthropocene/Simulation/Fission.cs` and
`Assets/_Anthropocene/Simulation/SocialRelocation.cs`. Checks:
`Tools/Headless/FissionChecks.cs`, `SocialRelocationChecks.cs` and
`SocialRelocationNativeChecks.cs`. Immigration runs before this pass and excludes
its sources and arrivals. Pass contract:
[TurnResolutionPasses.md](TurnResolutionPasses.md), Close row.

**Current Physical-v1 status:** the unified architecture is implemented; native
balance and MLP-training promotion are not certified. New-world plan-of-record
flags enable immigration and fission independently. Saves whose demographic
selector is absent retain `LegacyQoLNetGrowth` and its exact former fission law.

## Unified Physical-v1 rule (v0.2)

Crowding and distress now select only the recorded source circumstance. Both use
the same `SocialRelocationEvaluator` as immigration: mover advantage,
population-weighted aggregate welfare, bounded source harm and (for immigration)
bounded receiver-incumbent harm. Forecast experienced QoL remains a welfare term;
mortality risk comes from physical food, clothing, shelter, density, disease and
deficit history. There is no instantaneous-net-growth veto or special distress
bonus.

The existing policy daughter fractions and thresholds, minimum population, known
site/radius search, spacing, deterministic ordering and conflict resolution remain
unchanged. A daughter begins with no people or fixed infrastructure. The source
keeps site-bound housing and fields; the daughter receives proportional typed food,
portable inventory and shelter, tools, herds, workforce roles, demographic
remainders, histories, expectation and knowledge. Daughter farmers become
gatherers because fields do not move. Nothing is manufactured for free, and the
real economy/demography first processes the daughter on the following turn.

Before a physical site scan, a permissive common-envelope bound checks every
supported daughter size. It uses the exact source forecast and an optimistic
daughter with full F/C/H/W, zero linked-settlement density and the lowest physical
mortality possible at that headcount. It rejects only when every size must fail the
shared mover threshold or exact source-harm tolerance. Historical satisfaction and
historical coverage is not an input. `FissionSearchFraction` lowers only this
physically valid upper-bound threshold, preserving early cache searches without
screening any candidate that could meet the final mover gate. The bound never
accepts a split, and it adds no independent positive-source-relief condition.

The committed `Camp.QoL` formula, Elder reward and observation schema are
unchanged. Current same-boundary evidence shows 126 fissions with fission alone and
113 with both systems over 75 continuation turns; only 2 of 137 combined endpoints
have both clothing and housing coverage at least 0.8. The success/distress
calibration is therefore not promotion-ready. See
[UnifiedSocialRelocationValidation-v0.2.md](UnifiedSocialRelocationValidation-v0.2.md).

## Legacy WP10a/WP10c path and historical evidence

The remaining text documents `LegacyQoLNetGrowth` compatibility and the historical
WP10 calibration. References below to “current” mean current within that legacy
series; they do not supersede the unified Physical-v1 rule above.

A camp whose split would raise its stayers' QoL enough may send a coherent kin
group to a viable empty site. The group founds a new Society in a new AI player
slot. The parent keeps its site, fields, fixed Housing and controller; the player
keeps the original camp and never controls a daughter.

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
  is camps (one closed-form gain estimate each: a fixed-Housing lookup and a few exponentials), `F_v` camps with `G >= 0.8 G*` and a feasible daughter
  that hold a cache (one footprint validation, at most 7 tile projections), `F_s` camps
  above the search threshold without a valid cache (at most the tiles from the camp spacing out to
  `FissionRadius`: 18 at radius 3, 42 at 4 and 72 at 5, since spacing 3 excludes the inner disc, each one footprint) and `P` proposals
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
| Era progression | Per-Society days from `EraStartDays`; majority eligibility (200 years) and forced completion (240) contain no count of players. A daughter copies its parent's progression. WP10a let a late Paleolithic daughter hold `WorldState.Finished` open; since WP10c `Finished` is decided by founding Societies only (see Rules, opening status). |
| History and records | `TurnRecord` lists are keyed by camp ID and already tolerate camps appearing; a daughter has no demographic report in the turn it is founded. |
| Save codec | Optional fields only. `Fingerprint` and the metadata copy include `PlayerCount`. Older saves: no lineage, policy, destination or player count; config loads with `FissionEnabled = false` (as `PlannerFulfilment` loads Uniform) so they continue exactly. |
| `PlaytestScenarios`, presets | Select start camps by exchanging ownership at generation; generation is unchanged. They check `Config.Players`, the initial count. |
| Unity `OpeningGame` | Player 0 is always the human and its camp never changes owner; daughters are new AI players. No player list is sized from `Config.Players` (only `NewGame` sets it). No compile was possible here. |

No assumption needed a redesign, so the work proceeded.

## Rules

- **Policy** (`Kinship`): the gain threshold is `FissionGainDiscourage` / `FissionGainThreshold` /
  `FissionGainEncourage` (0.080 / 0.113 / 0.0955), the
  daughter takes 15 / 30 / 40 percent of the residents (integer arithmetic,
  `floor(N * pct / 100)`) and its affinity with the parent is 0.5 / 0.9 / 1.0. The
  policy never forces fission. A player sets it with `Order(FissionPolicy, camp,
  target: policy)`, which may accompany one ordinary order for the same camp (two
  policy orders for one camp conflict). It is committed at the start of resolution,
  so it applies to that turn's pass. The heuristic policy never issues it.
- **Push (WP10c): crowding plus expected gain** (the stayers' food and water relief and the daughter pull were added later: "Distress fission"). `Fission.EstimateSplit` is pure and uses the camp's committed
  terms with the same formulas as `NeolithicProduction.ResolveNeeds`. For `N` residents and daughter
  `N_d = floor(N f(p))` (exact fraction `f = N_d / N`): food and water ratios `F`, `W` and the expectation `E`
  are unchanged; clothing coverage is unchanged (Cloth and Animal Fabric split in proportion); social cohesion is
  `S(N - N_d)` at the camp's derived scale `N_s = N_band (1 + k H)` ([SocialCohesion.md](SocialCohesion.md)): `H`
  is held at its present value when `FissionHousingRelief = 0` (only the headcount changes) and interpolates
  towards the stayers' relieved `H` with the weight otherwise; housing per head is
  `min(1, (fixed people + portable people (1 - f)) / (N - N_d))` (people capacities: `HousingPeoplePerUnit` per fixed unit,
  `LightHousingPeoplePerUnit` per tent) because the parent keeps all fixed Housing
  and portable Light Housing splits in proportion (weighted by `FissionHousingRelief`, default 0). The daughter
  takes no fixed housing; its own shelter is the daughter-shelter estimate (`H = N_band` when it has none). With
  `Q = F W (w_c cloth + w_h housing) S` and each coverage `1 - E (1 - c)`:
  `G = Q_after / Q_now - 1`. Both qualities are evaluated at the post-demography snapshot (`Q_now` is the
  formula at the present residents, not the stored `QoL`, so births after the needs cohort do not bias the gain).
  If `Q_now = 0` (no food, or no coverage at all) the relative gain is undefined and the documented fallback is
  the cohesion gain `S_after / S_now - 1`, the gain a unit-coverage camp would have; a destitute crowded camp
  therefore still feels crowding. The diagnostic push is `P = clamp(G / G*, 0, 1)`.
  With full goods coverage and `B = 1`, `G = exp((N^2 - (N - N_d)^2) / N_s^2) - 1` for `beta = 2`, and `F`,
  `W` and partial coverage cancel unless the split changes housing per head.
- **Daughter check** ("would part of this population be better off"). The daughter's estimated QoL at the
  destination (`F_d = min(1, support ratio + carried food)`, see Relative destinations, `W = 1`, clothing coverage unchanged, housing from what it carries and can build in its first turn
  (`Fission.DaughterShelter`, below), `S(N_d)`, and the
  parent's retained expectation) must be at
  least `Q_now - FissionDaughterMargin` (0.05). It is evaluated only for a camp whose gain reaches `G*` and
  that holds a valid destination, and its binding is counted (`FissionDaughterChecks`, `FissionDaughterBlocked`).
- **Daughter shelter (B1).** The commit splits every double inventory field in the daughter's people fraction
  `f` (verified in `Fission.Found`: Light Housing units, Housing Material, Wood, Clay, Animal Fabric, Cloth, Hand and
  Regular Tools, Copper Tools) and leaves all fixed Housing with the parent. `Fission.DaughterShelter(world, camp, N_d)` is the
  pure, O(1) estimate of the shelter the daughter has at the end of her first turn, when her QoL is next computed:
  carried tents (`f` of the parent's Light Housing, `LightHousingPeoplePerUnit` people each) plus what her Builders assemble
  from carried goods in one turn at the planner's and resolver's production rates. Builders are
  `FissionDaughterBuilderShare` (0.05) of her people, fractional (an expectation, not a roster). Fixed Housing uses
  `BuilderHousingOutputPerBaseDays`; Light Housing uses `LightHousingBuilderOutputPerBaseDays` and
  `AnimalFabricPerLightHousing`. Each rate is multiplied by `GeneralistTaskEfficiency` unless the Society holds Construction;
  a qualified specialist also receives the bounded specialist multiplier evaluated at the daughter's local population. Housing and Housing Material each run at twice
  the single-step rate and share the Builder pool; the tool waterfall is the resolver's `ToolCoverageBudget` over her carried
  Hand/Regular/Copper tools (equipment limits and the 1 / 1.5 / 2 housing multipliers), so a daughter with no tools assembles
  nothing. Order: fixed Housing first (needs Cultivation; one carried Housing Material or one carried Wood/Clay unit each, material
  making charged to labour), then Light Housing from carried Animal Fabric (needs Husbandry), each capped at the people still
  unsheltered. Nothing is gathered at the site, so a daughter with no Builders, tools or materials has exactly her tents. Her housing
  coverage and cohesion share both use the result: `H_d = HousingShare(fixed built people, carried and sewn tent people, N_d)`.
  Builder-share choice: in the five-world baseline (HEAD, fission on), camps that staff any Builder use a median 1.5 percent of their people
  (P90 4.5 percent; 2.2 percent and 7.5 percent for camps of 300 or fewer, the size of a daughter), so 0.05 is a heavy but
  realistic shelter crew. Omissions (stated, not corrected): decay of carried tents within the turn (at most 10 percent
  of tent capacity), site-gathered materials, and Cloth/Animal Fabric competition between clothing and tents.
- **Daughter.** `N_daughter = floor(N * f)`. It must be at least
  `FissionMinimumDaughter` (12), smaller than the parent that remains, and leave the
  parent at least `MinimumCamp`. Hunters stay with the parent.
- **Cached destination.** Searched only when `G >= FissionSearchFraction * G*` (0.8) and no valid cache exists. Candidates are traversable land
  tiles (gameplay levels 1-9) within `FissionRadius` that the Society can know and
  that are at least `MinimumCampSpacing` from every camp (ground truth, which is a
  superset of own, visible and contact camps: a settling group finds a neighbour
  before it founds beside it). Known means: Paleolithic, current vision only (camps
  and hunter groups); after the first breakthrough, current vision plus explored
  tiles. With spacing 3 a radius of 3 is only the ring at distance 3 (18 tiles); the default radius 4 adds the ring at distance 4 (42 tiles in all) and radius 5 the ring at 5 (72). The
  best tile wins by `D` (capped at one) descending, then the uncapped ecological support ratio (footprint supply
  before the labor ceiling, over the daughter's need) descending, then hex distance, then tile ID; `D` saturates
  at one on most sites, so the second key keeps the choice from defaulting to low tile IDs. Nothing is reranked
  while the cache is valid.
  It is invalidated when the regime changes (even with no push), when a camp now stands
  on it or within spacing of it, or when it stops being habitable (support ratio 0: the freshwater
  gate fails or nothing is harvestable). There is no absolute ecological-support
  or QoL bar beyond the existing relative test (see Relative destinations).
  `PhysicalMortalityV1` separately projects the QoL-approved daughter's physical
  mortality and net rate for diagnostics; those projections do not change cache
  ordering or add a social veto.
- **Desirability.** `D = clamp(SiteSupport(daughter) / TargetSupport, 0, 1)`, with
  `TargetSupport` the daughter's own annual-generation need so `D = 1` when fully
  supported. Support uses the start-viability footprint evaluation
  (`PlacementStage.Footprint` and `Harvest`: four-season freshwater gate and mean
  recharge, repeatable land stock, aquatic renewal, working share, labor ceiling),
  with the repeatable stock taken from the *current projected* ecology rather than
  the generation-time stock. Current branch/new-world correction:
  `CropHarvestingOnly=true` removes hypothetical daughter cultivation capital and
  clamps projected food to natural capacity. All regimes use obtainable wild/
  aquatic harvest; daughters cannot spend fields they have not built. Camps with
  herds, including agricultural camps with secondary Husbandry, also pass an
  opening forage-stock gate and an opening-water-plus-recharge gate. Human land
  harvest is subtracted from forage; aquatic harvest is not. Dairy is not yet
  credited in daughter support. Physical site richness remains uncapped for
  destination ordering. See [CropHerdBalanceValidation.md](CropHerdBalanceValidation.md).
  Legacy `CropHarvestingOnly=false` retains the following approximation:
  Paleolithic (and Nomadic without herds): the start-viability result for the
  daughter cohort; Nomadic with herds: the least of that human-food coverage, herd
  forage coverage (sustainable biomass flow less the human harvest) and herd water
  coverage (freshwater recharge) for the daughter's share of the herds; Agricultural:
  repeatable wild and aquatic food plus the managed flow cultivable footprint land
  yields at the primitive supported intensity, capped by the daughter's farm labor
  ceiling. These are planning estimates from projected ecology, not forecasts of
  realized harvest. The regime is `Fission.RegimeOf`; it selects only this destination-site scoring (cultivable
  land vs pasture vs gathering) and no longer affects cohesion, which is derived from housing.
- **Proposal.** `G >= G*(policy)`, a valid cached destination, the existing
  daughter/stayer QoL checks and the size rules above. Under
  `PhysicalMortalityV1`, destination food, carried clothing, projected shelter,
  population and inherited deficit age produce an explicit physical mortality
  and net-rate diagnostic. `FissionPhysicalDaughterNegativeNet` counts proposals
  with negative instantaneous net growth, but does not block, weight or rerank
  them: above-equilibrium decline and recoverable distress are not physical
  nonviability.
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
- **Opening status (WP10c).** A daughter inherits every field the opening logic reads (era, technology,
  progression clock and completion fields, forced/secondary transition state, `OpeningTransitionProcessed`,
  transition seeds), so a Paleolithic parent's daughter is Paleolithic and a Neolithic parent's is Neolithic.
  `WorldState.Finished` is decided by **founding Societies only** (`ParentSociety == 0`): a daughter never delays
  the end of the opening. Rejected alternative: making daughters follow their root ancestor's breakthrough,
  which would grant an unearned technology. A late Paleolithic daughter still breaks through under the same
  rules (mastery, maturity, forced at 240 years) after the opening has ended.
- **Economy.** The daughter copies the parent's economic settings and runs continuous
  automation so the AI economy starts at once. Its fission policy is Neutral and it
  has no destination.
- **Journal.** `TurnRecord.Fissions` (parent camp order), one event line, and
  `DemographicReport.SplitOff` on the parent (`EndingPopulation` remains the
  pre-split figure; the Explanation adds the people who left).

## Observations and actions

`AgentObservation.SchemaVersion` is 19 (the fission block was introduced in 18).
Owner only: each `SettlementObservation`
carries `FissionPolicy` and `FissionDestination` (tile or -1); the observation carries
`ParentSociety`, `KinAffinity` and `KinRelations` (parent, daughters, siblings: society,
player, relation, affinity; no location, stores or population). A foreign daughter
appears only through ordinary vision and contacts. `OrderRules`, `EntityActionMask` and
`LegalActions` know `OrderKind.FissionPolicy` (three actions per settlement).

## Recalibration (WP10c)

Decision rule: a camp proposes iff `G >= G*(policy)`, a viable destination is cached, the daughter check passes
and the size rules hold (Rules above). Because `F`, `W` and coverage cancel in `G` while housing per head is
unchanged, the onset population is a property of `N_s`, the daughter fraction and `G*` alone. One threshold
per policy therefore fits every regime (the design's onsets share `N / N_s` near 0.46 for Neutral): with
`(N / N_s)^2 (1 - (1 - f)^2) = ln(1 + G*)`, Neutral `G* = 0.113` (f 0.30), Discourage `0.080` (f 0.15) and
Encourage `0.0955` (f 0.40). The three values are **not monotone in the policy**: the policy also sets the
daughter fraction, and a smaller daughter delivers a smaller gain, so Discourage needs the lowest threshold to
produce the latest onset. They are separate fields rather than multipliers for that reason.

**Derived cohesion scale (SocialCohesion.md):** the three rows were the Paleolithic, Nomadic and Agricultural regime scales in WP10c; the scale is now derived from housing structure, and the analytic fit `N_band = 220, k = 7.1, p_tent = 0.21` reproduces the same onsets (the table is the fit's closed form; `FissionChecks.Calibration` asserts it for the shipped scale). Achieved onsets under full goods coverage (`FissionChecks.Onset`: the smallest `N` whose daughter is at least
12, smaller than the stayers and whose gain reaches `G*`; the closed form with integer daughters):

| Housing structure (N_s) | Policy | Design N* | Achieved onset | Residual |
| --- | --- | ---: | ---: | ---: |
| No housing, H = 0 (220) | Neutral / Discourage / Encourage | 101 / 116 / 83 | 102 / 119 / 84 | +1.0% / +2.6% / +1.2% |
| Tents covering everyone, H = p_tent (548) | Neutral / Discourage / Encourage | 252 / 291 / 208 | 254 / 293 / 208 | +0.8% / +0.7% / 0.0% |
| Permanent housing covering everyone, H = 1 (1,782) | Neutral / Discourage / Encourage | 607-999 / 770-1,105 / 398-887 | 817 / 940 / 673 | inside every band (design median about 803 for Neutral: +1.7%) |

Every residual is below 3%, so no per-regime threshold is needed. Checks assert the Paleolithic and Nomadic
onsets within 3% and the Agricultural onsets inside the design bands.

## Measurements (WP10c)

Container: 4 cores, .NET 8, `Tools/PerformanceAudit` at the WP10c working tree, `--workers=4
--partition-size=64 --warmup=0 --details=1 --fingerprints=0 --production-config=1`, Standard/80 tribes, 12
requested opening turns then 50 post-opening turns. Raw reports: ignored `TestResults/WP10c/{on,relief0,off}-<world>.json`
(`on` is the shipped rule, `relief0` is `--fission-housing-relief=0`, `off` is `--fission=0` on the same binary).

```
dotnet build -c Release Tools/PerformanceAudit/Anthropocene.PerformanceAudit.csproj -o TestResults/WP10c/bin
dotnet TestResults/WP10c/bin/Anthropocene.PerformanceAudit.dll --players=80 --seed=S --shape=SH --production-config=1 \
  --turns=12 --post-opening-turns=50 --workers=4 --partition-size=64 --warmup=0 --details=1 --metrics=0 \
  --fingerprints=0 --fission=1 [--fission-housing-relief=0] --output=TestResults/WP10c/<mode>-SH-S.json
```

Five worlds: Continents 1, 2, 3, Pangaea 1, Archipelagos 1. The opening ended on the same turn with fission on
and off in every world (10, 10, 10, 11, 11): daughters no longer delay `Finished` (WP10a: 10 to 12).

| World | Fissions (rule / relief 0) | Camps = players at final turn (rule / relief 0 / off) | Population at final turn (rule / relief 0 / off) | Population x off (rule / relief 0) |
| --- | ---: | --- | --- | --- |
| Continents 1 | 585 / 382 | 665 / 462 / 80 | 639,579 / 553,092 / 87,859 | 7.3 / 6.3 |
| Continents 2 | 559 / 366 | 639 / 446 / 80 | 556,837 / 478,646 / 81,025 | 6.9 / 5.9 |
| Continents 3 | 482 / 306 | 562 / 386 / 80 | 487,534 / 422,739 / 80,878 | 6.0 / 5.2 |
| Pangaea 1 | 567 / 326 | 647 / 406 / 80 | 611,403 / 481,353 / 90,798 | 6.7 / 5.3 |
| Archipelagos 1 | 448 / 239 | 528 / 319 / 80 | 459,778 / 327,892 / 87,223 | 5.3 / 3.8 |
| Total | 2,641 / 1,619 | | | |

The final turn is turn 60 (Pangaea and Archipelagos: 61, because their opening took 11 turns).

Fissions by regime (all Neutral: the heuristic AI never changes a policy). Paleolithic and Nomadic: none in any
world. Paleolithic camps never reach the onset during the opening (the largest Paleolithic camp in Continents 1
and 2 was 60 and 74 residents, against the Neutral onset of 102 and the Encourage onset of 84) because every
founder breaks through by turn 10-11; every fission in these runs was classified Agricultural.

| Rule | Regime | Fissions | Parent residents P25 / median / P75 (min-max) | Daughter P25 / median / P75 (min-max) |
| --- | --- | ---: | --- | --- |
| Specified (`FissionHousingRelief = 1`) | Agricultural | 2,641 | 58 / 141 / 511 (40-4,463) | 17 / 42 / 153 (12-1,338) |
| Cohesion and clothing only (`= 0`) | Agricultural | 1,619 | 857 / 906 / 971 (817-4,011) | 257 / 271 / 291 (245-1,203) |

Why the specified rule splits small camps: `Q = F W R S`, so a relative gain is governed by `R`, the
clothing-and-housing term. An agricultural camp that chronically lags its housing (housing per head below about
0.77 at full expectation) gains 13-15% in `R` as soon as 30% of its people leave, because the parent keeps all fixed
Housing. The gain was 0.13-0.16 at the median in every turn group, against a cohesion-only gain of 0.0005-0.05 for
camps of 50-600 residents: nearly every fission below about 800 residents was housing-driven. Parent medians by era for
the specified rule (all worlds): 60 (turns 0-15), 115 (16-30), 493 (31-45), 655 (46-70).

Daughter check: 3,313 camp-turns reached `G >= G*` with a valid destination and were tested; 512 (15.5%) were
blocked (Continents 1-3: 87 of 707, 131 of 720, 136 of 647; Pangaea 88 of 697; Archipelagos 70 of 542). The
daughter loses the parent's fixed Housing and so is often worse off than a well-housed parent. With relief 0 the
check never binds (0 of 1,688).

Final cohort at post-opening turn 50, pooled over the five worlds (population P25 / median / P75; QoL P25 / median /
P75). Founders are the 80 starting Societies per world, daughters the rest.

| Rule | Cohort | Agricultural (Cultivation): n, population, QoL | Husbandry (AnimalHusbandry): n, population, QoL |
| --- | --- | --- | --- |
| Off | founders | 280; 480 / 781 / 1,254; 0.53 / 0.71 / 0.91 | 106; 1,050 / 1,748 / 2,172; 0.20 / 0.34 / 0.64 |
| Specified | founders | 280; 455 / 738 / 1,164; 0.55 / 0.77 / 0.92 | 106; 997 / 1,565 / 2,137; 0.23 / 0.44 / 0.72 |
| Specified | daughters | 1,530; 344 / 616 / 1,041; 0.62 / 0.83 / 0.94 | 1,075; 579 / 1,002 / 1,645; 0.42 / 0.72 / 0.89 |
| Specified | pooled | 1,810; 359 / 640 / 1,065; 0.61 / 0.82 / 0.94 | 1,181; 600 / 1,059 / 1,701; 0.39 / 0.70 / 0.89 |
| Relief 0 | founders | 280; 480 / 744 / 1,240; 0.53 / 0.73 / 0.91 | 106; 1,032 / 1,719 / 2,168; 0.22 / 0.35 / 0.66 |
| Relief 0 | daughters | 629; 591 / 916 / 1,264; 0.52 / 0.67 / 0.82 | 970; 713 / 1,187 / 1,745; 0.38 / 0.64 / 0.86 |
| Relief 0 | pooled | 909; 556 / 883 / 1,263; 0.52 / 0.69 / 0.86 | 1,076; 724 / 1,222 / 1,806; 0.35 / 0.62 / 0.85 |

A further 50 (specified) or 34 (relief 0) Watercraft camps are not tabulated (14 with fission off). Fission lifts
husbandry QoL because the cohesion penalty at 1,500-2,000 residents disappears (founder median 0.34 to 0.44).

Cost (median over all turns of AI + preparation + resolution; five-world totals):

| Rule | Median turn ms by world (Cont 1 / 2 / 3, Pangaea, Archipelagos) | Median ms per camp | Peak managed heap MiB | Fission pass |
| --- | --- | --- | --- | --- |
| Off | 229 / 227 / 226 / 205 / 200 | 2.9 | 643 / 637 / 685 / 627 / 567 | none |
| Specified | 816 / 779 / 618 / 634 / 611 | 1.7-2.0 | 858 / 797 / 830 / 859 / 705 | 2,381 ms of 224,092 ms (1.06%); 53,656 searches; 2,801 proposals, 160 dropped |
| Relief 0 | 380 / 401 / 413 / 339 / 311 | not tabulated | 699 / 644 / 709 / 670 / 590 | 1,142 ms of 126,793 ms (0.90%); 22,428 searches; 1,688 proposals, 69 dropped |

Turn time follows camps linearly (about 2 ms per camp), as in WP10a; the pass itself stays near 1% of turn time.

### Findings (WP10c)

- **The specified rule does not reach the design agricultural size.** Housing relief makes the gain a function of
  the housing deficit, not of size. Setting `FissionHousingRelief = 0` restores the design: Agricultural parents
  split at a median of 906 residents (P25 857, P75 971), inside the design 607-999 band and close to the
  analytic onset of 817, with 39% fewer fissions and about 5.3x (specified rule: 6.4x) the control population. Decision needed: ship 0, or keep 1 and
  make housing relief depend on the daughter's own housing (the daughter check already reports the asymmetry:
  it blocks 15.5% of qualifying splits).
- **Paleolithic and Nomadic onsets are met analytically but never exercised** in the 20-year opening: Paleolithic
  camps stay below 84 residents until their breakthrough.
- **Population still multiplies about 4 to 7x** because every daughter takes a fresh footprint and camps keep
  growing exponentially; the recalibration changes when camps split, not how many independent footprints the map
  supports.
- **Destination order.** `D` saturates at one, so the order now falls through to the uncapped support ratio,
  distance and tile ID (tested: a richer site beats a lower tile ID; a nearer site beats a farther one).

## Measurements (WP10a, superseded push rule)

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

### Findings (WP10a)

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

## Throughput: daughter shelter estimate and destination radius

Motivation (Docs/SocialCohesion.md, "Main finding"): in the HEAD baseline the daughter check blocked 76 percent of
evaluations, because the daughter was charged `H = 0` (no housing, `N_s = N_band`), and radius 3 with spacing 3 searched
only the distance-3 ring. Two changes, measured together and separately:

- **B1** the daughter's housing is what she carries plus what her Builders can assemble in her first turn
  (`Fission.DaughterShelter`, see "Daughter shelter" above; new lever `FissionDaughterBuilderShare` 0.05).
- **B2** `FissionRadius` default 3 to **4** (destination order unchanged: D, support ratio, distance, tile ID, so the
  nearest tied site still wins).

Method: fission on, immigration off, Standard / 80 tribes, 12 opening + 50 post-opening turns, `--workers=1 --details=1 --metrics=1`,
five worlds (Continents seeds 1, 2, 3, Pangaea 1, Archipelagos 1), three runs in parallel; counters cover every turn, size and
split-rate rows pool post-opening turns 10 to 50 (`Tools/PerformanceAudit/Summarize-FissionThroughput.py`). Runs: `head` = commit
1122160; `b1` = B1 only (radius 3); `b1r4` = B1 + radius 4 (the shipped default); `b1r5` = B1 + radius 5.

| run | fissions | daughter checks | blocked | proposals | dropped | searches | candidates scored | final population / world | camps at turn 60 / world | mean QoL |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| head | 1,420 | 6,383 | 4,820 (76%) | 1,563 | 143 | 24,532 | 13,533 | 403,916 | 364 | 0.698 |
| b1 | 2,142 | 2,579 | 248 (10%) | 2,331 | 189 | 36,640 | 17,422 | 534,193 | 508 | 0.715 |
| b1r4 | 2,348 | 2,901 | 347 (12%) | 2,554 | 206 | 39,159 | 31,011 | 576,093 | 550 | 0.715 |
| b1r5 | 2,554 | 3,268 | 381 (12%) | 2,887 | 333 | 41,951 | 49,250 | 617,814 | 591 | 0.717 |

Camp-size P90 / P95 (n camp-turns) by actual housing class (targets 100, 250, 600-1,000) and median size unhoused / tent / fixed:

| run | unhoused | tent-dominant | fixed-dominant | median size u/t/f | score |
|---|---|---|---|---|---|
| head | 65/79 (3,826) | 1,367/1,714 (17,903) | 1,683/2,011 (32,510) | 28/426/746 | 12.02 |
| b1 | 222/243 (4,502) | 1,362/1,687 (18,324) | 1,499/1,853 (47,738) | 33/429/685 | 14.20 |
| b1r4 | 226/246 (4,601) | 1,385/1,749 (20,359) | 1,504/1,828 (50,770) | 33/430/678 | 14.59 |
| b1r5 | 229/247 (4,660) | 1,339/1,682 (21,964) | 1,503/1,860 (54,314) | 33/429/683 | 14.21 |

Split rate of camp-turns by size relative to the closed-form Neutral onset `0.4581 N_s` (splits / camp-turns; a camp that split is
measured at its size before the split; every camp is treated as Neutral, so this is approximate for Discourage/Encourage camps):

| run | 0.5-1.0x | 1.0-1.5x | 1.5-2.0x | 2.0-3.0x | >3x |
|---|---|---|---|---|---|
| head | 4.7% (746/16,029) | 3.3% (325/9,953) | 1.1% (73/6,610) | 0.9% (50/5,791) | 2.3% (107/4,558) |
| b1 | 5.6% (1,293/23,025) | 3.5% (482/13,684) | 0.8% (69/8,698) | 0.9% (61/6,665) | 2.3% (108/4,599) |
| b1r4 | 5.8% (1,442/24,900) | 3.8% (560/14,691) | 0.7% (60/9,042) | 0.8% (59/7,291) | 1.9% (96/4,967) |
| b1r5 | 5.8% (1,547/26,680) | 3.8% (611/15,948) | 0.8% (78/9,918) | 1.0% (75/7,532) | 2.1% (110/5,233) |

QoL P25 / median / P75 by housing class (post-opening turns 10-50):

| run | unhoused | tent-dominant | fixed-dominant |
|---|---|---|---|
| head | 0.00/0.02/0.59 | 0.65/0.88/0.95 | 0.50/0.75/0.90 |
| b1 | 1.00/1.00/1.00 | 0.64/0.88/0.95 | 0.57/0.77/0.89 |
| b1r4 | 1.00/1.00/1.00 | 0.64/0.88/0.95 | 0.57/0.77/0.89 |
| b1r5 | 1.00/1.00/1.00 | 0.66/0.88/0.95 | 0.57/0.76/0.89 |

Cost (Continents 1 alone, one worker, no other load; per-turn medians over the 50 post-opening turns, fission pass and whole turn):

| run | fission ms (median / last 25 turns) | turn ms (median / last 25 turns) | camps at turn 60 |
|---|---|---|---|
| head | 6.3 / 8.1 | 617 / 865 | 371 |
| b1 | 8.0 / 12.5 | 752 / 1,216 | 551 |
| b1r4 | 10.3 / 13.6 | 752 / 1,353 | 605 |
| b1r5 | 12.1 / 20.1 | 816 / 1,419 | 631 |

Findings:

- **B1 corrects the daughter check.** The check binds in 10 percent of evaluations instead of 76 percent, fissions rise
  51 percent (1,420 to 2,142), camps at turn 60 from 364 to 508 and population per world by 32 percent. The estimate was therefore
  the dominant blocker, as inferred in SocialCohesion.md; this run isolates it. Mean QoL does not fall (0.698 to 0.715).
- **The size tail barely moves.** Fixed-dominant P90 falls from 1,683 to 1,499, tent-dominant stays near 1,360, and the
  scores in the table worsen only because the new daughters change the *unhoused* class: a daughter of a 300-600 person village is
  founded with about 100-200 people and no fixed Housing, so a post-opening unhoused camp is now often 200 people (P90 222, against 65
  for the Paleolithic camps alone) with stored QoL 1.00 (the value a camp reported before its first resolution computed one; since the QoL-mortality change `Fission.Found` stores the founding estimate instead, see Docs/QoLMortality.md). Camps still split mostly
  below onset (5-6 percent of camp-turns at 0.5-1.0x) and rarely above it (0.7-1.0 percent at 1.5-3x): once a village is much larger than
  its onset the cause is no longer the daughter check (now 10-12 percent of evaluations) but the one-split-per-turn rule from a frozen
  snapshot with a daughter of at most 40 percent when a cohort grows several-fold in a 20-year turn. Size targets remain unmet.
- **B2.** Radius 4 adds 10 percent more fissions and 8 percent more population than radius 3 (b1 to b1r4); radius 5 adds
  a further 9 and 7 percent. The cost scales with the searched disc: candidates scored 17,422 / 31,011 / 49,250 for radius 3 / 4 / 5
  (1.8x and 2.8x), whole-turn time 752 / 752 / 816 ms median on Continents 1 (the late-turn rise follows the larger camp count, not the
  search: the fission pass is under 2 percent of the turn in every run). Radius 4 is shipped; radius 5 buys fewer extra fissions per
  candidate and the most dropped proposals (333 against 206), and is left as a lever.
- **Limits.** Five worlds, one seed per shape for Pangaea and Archipelagos; differences of a few percent are inside the per-world
  spread (per-world rows reproduce with `--per-world`). Wall time was measured on a shared four-core machine. The builder share is a
  documented estimate, not a calibration to realized first-turn building, and the daughter's realised first-turn QoL was not compared
  with the estimate. Daughters now start larger and unhoused, which the unhoused size row reflects.

## Distress fission: a split driven by QoL, not crowding alone

**User decision.** Fission can and should be a product of QoL, not of crowding alone. If a tribe's QoL is below a
certain amount, triggered fission should cause people to break off for better pasture; if a famine hit a village,
many people would flee for better ground whether or not it was crowded. No arbitrary hard cutoff: the QoL below
which people leave is an outcome of the land within reach.

**The defect.** The WP10c estimate held `F` and `W` unchanged for the stayers (`b0 = F W` on both sides of the
ratio), so `G = Q_after / Q_now - 1` measured only cohesion and housing, that is crowding. A starving, uncrowded
camp had `G ~ 0` and never split. The daughter's QoL (`F = W = 1`) was only a veto, never a trigger.

**Doctrine header.**

- *Authoritative state*: the committed `Camp.FoodNeedsMetRatio` (F), `WaterNeedsMetRatio` (W), expectation, clothing
  and housing terms and the saved destination cache. Nothing new is stored on the camp. `FissionReport` gains the
  optional `Trigger` (Crowding = the enum's zero, so older saves read Crowding) and `DaughterGain`.
- *Temporal abstraction*: unchanged. One evaluation per turn boundary from the frozen post-demography snapshot; F and W are the
  turn just resolved. The estimate is a closed form at the boundary, not an integration over sub-turn time.
- *Scaling*: O(1) extra work per camp (one more `EstimateSplit`, itself O(1) closed forms plus `DaughterShelter`; a fed
  camp reuses its single estimate). The destination search is the existing bounded, cached search; its size argument
  changes, not its cost. Nothing scales with elapsed years, map size or Societies. Mature-empire and RL scales are
  therefore the WP10a/WP10c ones (Throughput section).
- *Validation*: closed-form and fixture checks in `FissionChecks.cs` (below) and the five-world measurements below.

**Rule.** `EstimateSplit(world, camp, daughterSize, F_d, W_d)` (the policy overload uses the policy size and `F_d = W_d = 1`):

1. *Stayers feel relief.* `F_hat = min(1, F N / N_stay)` and `W_hat = min(1, W N / N_stay)` (`QoLModel.FoodAt` /
   `WaterAt`, the dilution rule immigration's `ForwardAt` uses; local supply is shared by fewer mouths and the
   leavers take their proportional share of stores, so per-head stores are unchanged).
   `Q_after = F_hat W_hat * coverage(housing after) * S(N_stay)`. The `FissionHousingRelief` rule for housing is
   unchanged. This is the **land-limited upper bound**: it assumes the local flow does not shrink with the
   headcount (fields keep yielding for the stayers, the leavers' labour is not missed). For `F = W = 1` it is exactly
   the WP10c estimate, so the closed forms in "Recalibration" hold for fed camps.
2. *The daughter pull.* `Q_d = F_d W_d * coverage(E) * S(n_d)` at her headcount and shelter (`DaughterShelter`);
   `G_d = Q_d / Q_now - 1` (`Q_now <= 1e-9`: satisfied, `G_d = 1e9`, if `Q_d > 0`). **The trigger compares like with
   like**: the expectation `E` is the parent's on both sides. Under the v1.1 demographic/QoL separation, the veto and founding
   value also retain that expectation (`DaughterQoL`, `F = W = 1`, `Q_d >= Q_now - FissionDaughterMargin`), closing the former
   one-turn expectation-reset welfare artefact without changing a threshold. `F_d = min(1, ratio)`, `ratio` the destination's uncapped
   support ratio for the daughter cohort that `Desirability` already computes (for herded nomads it already folds in herd forage
   and water, so it stands for `F_d W_d`); `W_d = 1` because freshwater is a gate of the footprint. The screen before a destination is known
   uses `F_d = W_d = 1` and gates the search exactly like the crowding screen (`FissionSearchFraction * G*`).
3. *Decision.* Propose when a viable destination is cached AND (`G_stay >= G*` at the policy size, **or** `G_d >= G*` at the
   distress size) AND the daughter veto passes AND the stayers are not worse off (`Q_after >= Q_now - FissionDaughterMargin`,
   true by construction). Same policy thresholds, no new QoL cutoff: the effective "QoL below which people leave" is
   `Q_now < Q_d / (1 + G*)`, set by the land within reach. In physical mode, explicit mortality and net growth are
   projected from food, clothing, shelter, headcount and inherited deficit age—never composite QoL—and reported without
   changing the decision.
4. *Who leaves under distress.* When the camp cannot feed itself the leaving group is the people local subsistence cannot
   carry: `n_short = ceil(N (1 - F W))`, `n_d = clamp(max(policy size, n_short), FissionMinimumDaughter, largest n with n < N - n and
   N - n >= MinimumCamp)`. If the distress branch passes the split uses this size; otherwise a passing crowding branch uses
   the policy size. The destination search and validation use the distress size whenever the distress screen passes (the larger,
   harder-to-support group). One split per camp per turn, no chaining.
5. *Report and metrics.* `FissionReport.Trigger` is Distress when the distress branch was the one used, `DaughterGain` the
   daughter's relative gain at the chosen destination; it also carries projected daughter nutrition, clothing, housing,
   mortality, net rate and whether that instantaneous net rate is nonnegative. `ResolutionMetrics` counts negative-net
   proposals plus proposals and commits per trigger
   (`FissionCrowdingProposals`, `FissionDistressProposals`, `...Commits`, `FissionDistressDaughters`). The report is history only
   (not in observations; schema unchanged).

**Resolved ambiguity: the distress branch exists only for a camp short of food or water (`F W < 1`).** Applied literally to
a fed camp, `G_d >= G*` fires whenever the smaller daughter group has better cohesion than the stayers, a crowding effect the
stayers' branch already measures; counting it twice lowered the calibrated crowding onset (the Neutral unhoused onset check
failed at 0.8 of the onset). A fed camp is therefore exactly the WP10c rule. The cost is a discontinuity at `F W = 1`: a camp
at `F W = 0.99` also gets the daughter pull.

**Checks** (`FissionChecks.cs`, `Distress`): the stayers' `F_hat` closed form at the policy and clamped sizes; the famine fixture
(N = 80, housed, F = 0.4: Distress, daughter clamped to 39 from `n_short` = 48, metrics per trigger, people conserved); size follows the
shortfall (F = 0.8 Crowding 24 through the stayers' relief, 0.6 Distress 32, 0.5 Distress 39); water distress; a small camp whose policy size is below the minimum
still sends the people its land cannot carry, one whose largest legal daughter is below the minimum cannot; the same camp fed does not split; the
daughter trigger and veto use the same retained expectation, and the camp splits only when `G*`
falls below that like-for-like gain; no destination (waterless, no known site) and a thinly supported destination (`F_d < 1`) do not split while the
control with rich ground does; a fed crowded camp splits with the Crowding trigger; scalar versus reversed 4-worker replay of a starving six-camp world
(identical fingerprints, trigger and size sequences, invariants); save round trip of the trigger and gain; an older report without the fields loads as Crowding.
The WP10c closed-form checks that used `F = 0.63, W = 0.9` and `F = 0.4` now include the stayers' relief factor `F_hat W_hat / (F W)`.

### Measurements (distress fission)

Tools/PerformanceAudit, five Standard / 80-tribe worlds (Continents 1, 2, 3, Pangaea 1, Archipelagos 1), 12 opening + 50
post-opening turns, `--workers=1 --fission=1 --immigration=0 --details=1 --metrics=1`, pooled. Baselines `h_*` are the committed HEAD
(01456a6) re-run with the same harness and arguments; `d_*` is this change. `_f1` has QoL mortality off, `_a1` has `--qol-mortality=1
--set=QoLMortalityElasticity=1`. Reduced by `Summarize-DistressFission.py` (fissions by trigger, deaths, QoL, totals) and
`Summarize-FissionThroughput.py` (camp sizes by housing class, split rates, counters). Population and food are the final turn's; "camps" are at the
last measured turn (post-opening turn 50 = turn 60). Human deaths are the resolver's starvation deaths (the QoL-mortality deaths are not recorded).
The timing rows ran two audits at a time beside another sweep, so turn and fission milliseconds are indicative only.

| run | worlds | fissions | crowding | distress | distress size P25/med/P75 | distress share of parent (median) | human deaths | camp-turns with DeficitTurns>0 | population | food yield | camps (last turn) | turn ms (median) |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| h_f1 | 5 | 2358 | 2358 | 0 | n/a | n/a | 1080 | 0.4% (263/71873) | 2906125 | 2.38e+08 | 2758 | 579 |
| d_f1 | 5 | 2350 | 2338 | 12 | 92/108/138 | 0.30 | 728 | 0.3% (211/71761) | 2895971 | 2.37e+08 | 2750 | 563 |
| h_a1 | 5 | 1479 | 1479 | 0 | n/a | n/a | 206 | 0.1% (61/43392) | 978004 | 8.09e+07 | 1879 | 397 |
| d_a1 | 5 | 1484 | 1482 | 2 | 60/60/71 | 0.30 | 149 | 0.1% (45/43470) | 979955 | 8.1e+07 | 1884 | 392 |

QoL by technology and the growth-blocking share (Q* = 0.746 at elasticity 1), post-opening turns 10..50:

QoL P25/median/P75 (n camp-turns), post-opening turns 10..50; share of goods-economy camp-turns at or below Q* = 0.746
| run | agricultural | husbandry | agricultural + husbandry pooled | share <= Q* |
|---|---|---|---|---|
| h_f1 | 0.66/0.80/0.91 (32311) | 0.41/0.76/0.91 (37652) | 0.57/0.79/0.91 (69963) | 42.5% (30546/71870) |
| d_f1 | 0.66/0.80/0.91 (32492) | 0.41/0.76/0.91 (37359) | 0.57/0.79/0.91 (69851) | 42.5% (30517/71758) |
| h_a1 | 0.77/0.88/0.95 (17324) | 0.70/0.82/0.92 (24216) | 0.72/0.85/0.93 (41540) | 30.0% (13028/43390) |
| d_a1 | 0.77/0.88/0.95 (17344) | 0.70/0.82/0.92 (24274) | 0.72/0.85/0.93 (41618) | 30.0% (13024/43468) |

Counters, camp size and split rate:

| run | worlds | fissions | checks | blocked (%) | proposals | dropped | searches | candidates | fission ms/turn (median) | pop | camps | mean QoL | turn ms (median) |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| h_f1 | 5 | 2358 | 2870 | 325 (11%) | 2545 | 187 | 39367 | 31960 | 6.7 | 581225 | 552 | 0.710 | 700 |
| d_f1 | 5 | 2350 | 2849 | 314 (11%) | 2535 | 185 | 39314 | 32123 | 7.8 | 579194 | 550 | 0.710 | 716 |
| h_a1 | 5 | 1479 | 1673 | 63 (4%) | 1610 | 131 | 15370 | 20187 | 3.1 | 195601 | 376 | 0.786 | 407 |
| d_a1 | 5 | 1484 | 1679 | 62 (4%) | 1617 | 133 | 15404 | 20324 | 3.4 | 195991 | 377 | 0.786 | 423 |

Camp size P90/P95 (n) by housing class; targets 100 / 250 / 600-1,000
| run | unhoused | tent-dominant | fixed-dominant | median size u/t/f | score |
|---|---|---|---|---|---|
| h_f1 | 226/245 (4586) | 1365/1708 (20600) | 1508/1852 (50466) | 33/436/682 | 14.36 |
| d_f1 | 225/244 (4592) | 1385/1717 (20351) | 1506/1848 (50597) | 33/436/679 | 14.45 |
| h_a1 | 94/222 (5644) | 666/674 (16274) | 737/787 (25190) | 27/385/445 | 4.58 |
| d_a1 | 93/222 (5643) | 665/674 (16337) | 737/788 (25206) | 27/384/445 | 4.58 |

Split rate of camp-turns by size relative to the Neutral onset (splits / camp-turns)
| run | 0.5-1.0x | 1.0-1.5x | 1.5-2.0x | 2.0-3.0x | >3x |
|---|---|---|---|---|---|
| h_f1 | 5.8% (1424/24639) | 3.9% (575/14806) | 0.7% (68/9109) | 0.9% (63/7387) | 2.0% (100/4950) |
| d_f1 | 5.8% (1417/24515) | 3.8% (560/14712) | 0.8% (69/9123) | 0.8% (59/7353) | 2.1% (105/4938) |
| h_a1 | 6.7% (1186/17704) | 1.6% (191/11662) | 4.7% (27/572) | 3.1% (20/654) | 6.1% (22/360) |
| d_a1 | 6.7% (1187/17731) | 1.7% (193/11664) | 4.7% (27/573) | 2.9% (19/652) | 5.8% (21/360) |

### Findings (distress fission)

- **Distress is rare in these worlds, and its effect is small and in the intended direction.** Mortality off: 12 of 2,350 fissions
  (0.5 percent) are Distress, against none before; with QoL mortality at elasticity 1: 2 of 1,484. Starvation deaths fall from 1,080 to 728
  (-33 percent) and from 206 to 149 (-28 percent); camp-turns with `DeficitTurns > 0` fall from 263 to 211 and from 61 to 45. Total population, food yield,
  camps at turn 60 and QoL quantiles are within 0.5 percent of the baseline (population 2,906,125 vs 2,895,971; 978,004 vs 979,955), and the share of
  goods-economy camp-turns at or below Q* is unchanged (42.5 percent; 30.0 percent). Camp-size P90/P95 by housing class and the split rate by
  size relative to the crowding onset are unchanged within noise, so the crowding calibration is intact.
- **Distress daughters are mostly the policy size.** All 14 distress splits are Agricultural camps; the median share of the parent is 0.30
  (the Neutral fraction), because a camp that has a viable destination rarely has `F W` below 0.7, the point at which `n_short` exceeds the policy size.
  The fixtures cover the larger distress sizes (`n_short` up to the legal cap); the generated worlds seldom reach them. Two distress splits
  were of camps of about 1,900-2,200 residents whose `Q_now` was near zero (gain in the millions); the rule treats a near-zero `Q_now` as satisfied.
- **Why the effect is small.** Starving camps without a viable destination within `FissionRadius` still cannot leave (`D < StartViability`), exactly the
  intended "no better ground in reach" outcome, and most famine in these worlds is transient (the deficit-camp share of camp-turns is 0.1-0.4 percent).
  The mechanism matters in worlds with sharper local famine and nearby good ground; none of the current five is one.
- **Cost.** Fission evaluations, searches and candidates scored are unchanged to within 0.5 percent (searches 39,314 vs 39,367; candidates 32,123 vs 31,960), and a fed camp performs
  the same single estimate as before; the median turn time difference is within the run-to-run noise of the shared CPU.

## Hinterland and relative destinations: why 90 percent of crowded camps could not split

**The measured defect** (fission on, mortality off, post-opening turns 10-50, five Standard / 80-tribe worlds, before this change):
49,713 evaluations, 23,765 camps past the gain screen, but only 23,347 candidate tiles scored in total (about one per search); 2,337
searches found a destination (10 percent) and 1,960 split. A camp that wanted to split mostly had nowhere to go: `VisionRadius` 2 and
`MinimumCampSpacing` 3 make the camp's own exclusion zone (radius 2) equal to its whole vision, candidates come only from remembered
explored tiles, and a settled village explores nothing beyond its vision. A second bar sat on top: the destination had to feed the daughter
(30 percent of a 700-person camp is about 210 people) at 85 percent of her need from first-turn harvest, a bar calibrated for 24-person starts.
The world was not full (1024 x 512, about 550 camps).

**User decisions implemented.** (1) A settlement's hinterland knowledge grows with its population while it stays put. (2) Nomads keep a decaying
memory of previously visible tiles plus the small vision bubble. (3) Destinations are judged relative to home (the daughter must be better off),
not against an absolute 85 percent bar. Rules (1) and (2) are in [ContactKnowledge.md](ContactKnowledge.md) (Hinterland and decaying memory);
rule (3) is here.

**Relative destination test.** A site is a destination when it is habitable: the start-viability footprint evaluation passes (four-season
freshwater gate, terrain) and its support ratio is positive (`ratio > 0`, the uncapped footprint supply over the daughter's need; for herders
the least of human food, herd forage and herd water). The cache is invalidated when a site stops being habitable. The ordering of candidates is
unchanged (`D` capped at one, then the uncapped ratio, then distance, then tile ID). Whether the daughter is better off is the existing veto
(`DaughterQoL >= Q_now - FissionDaughterMargin`), now evaluated with the destination's actual food instead of `F = 1`:
`F_d = min(1, ratio + carried)`, where `carried` is the person-equivalents of the turn's need her proportional share of the parent's stored food
covers (`Immigration.CarriedPerPerson`, the same term immigration uses). For a herded daughter the herd coverage caps `F_d` (carried human food does
not feed animals). `EstimateSplit` takes it as the optional `vetoFood` argument (default 1: unchanged callers). The distress and crowding triggers
and their gain terms are unchanged (the distress gain still uses the site's support ratio alone); only the veto sees the carried food. A parent
that is well off (high `Q_now`) therefore blocks a thin site that a parent that is short of food, or one carrying food, accepts.
Under `PhysicalMortalityV1`, a site that passes this relative welfare test can
still be rejected after the QoL decision if the daughter would begin with a
negative explicit physical net rate. This does not invalidate or reorder the
cached site; it prevents that turn's proposal and is counted separately.

**Funnel counters** (`ResolutionMetrics`, transient, in the audit JSON as `fissionScreened` ...): every camp past the gain screen ends in exactly one
outcome. `screened = searches with no legal candidate + searches with candidates but no destination + camps with a destination`; `with destination =
below threshold + daughter veto + stayers veto + physical daughter veto + proposals`; `proposals = dropped (spacing conflict) + committed`. `Tools/PerformanceAudit/Summarize-Hinterland.py`
tabulates them. Checks: `FissionChecks.Funnel` (identities over eight fixtures, scalar versus reversed 4-worker) and `FissionChecks.Relative`.

### Measurements (hinterland and relative destinations)

`Tools/PerformanceAudit --players=80 --turns=12 --post-opening-turns=50 --seed=N --shape=S --workers=1 --fission=1 --immigration=0 --details=1`, Continents 1-3,
Pangaea 1, Archipelagos 1, counters pooled over all 50 post-opening turns. `c_*` is the baseline (HEAD e8a77c5 plus the funnel counters; fingerprint-identical
outcomes to the earlier `d_*` runs), `h_*` this change. `_f1` mortality off, `_a1` `--qol-mortality=1 --set=QoLMortalityElasticity=1`. `r_f1` is the relative
test alone (`--set=HinterlandMaxRadius=2`, hinterland disabled). One run per world on a shared 4-CPU host.

| stage | c_f1 | r_f1 (relative test only) | h_f1 | c_a1 | h_a1 |
|---|---:|---:|---:|---:|---:|
| camps evaluated | 73,794 | 67,518 | 184,721 | 45,827 | 73,417 |
| screened (past the gain screen) | 40,703 | 37,320 | 96,380 | 16,501 | 25,743 |
| searches with no legal candidate | 32,999 | 30,826 | 65,953 | 11,917 | 14,619 |
| searches with candidates, none a destination | 3,381 | 2,944 | 10,337 | 1,642 | 3,192 |
| camps with a destination | 4,323 | 3,550 | 20,090 | 2,942 | 7,932 |
| of which below threshold | 1,522 | 1,205 | 7,898 | 1,279 | 3,454 |
| of which daughter veto | 314 | 268 | 738 | 62 | 66 |
| of which stayers veto | 0 | 0 | 0 | 0 | 0 |
| proposals | 2,487 | 2,077 | 11,454 | 1,601 | 4,412 |
| dropped (spacing) | 185 | 181 | 1,217 | 133 | 399 |
| committed (fissions) | 2,302 | 1,896 | 10,237 | 1,468 | 4,013 |
| share of screened with a destination | 10.6% | 9.5% | 20.8% | 17.8% | 30.8% |

| run | camps at turn 60 | population at turn 60 | median turn ms |
|---|---:|---:|---:|
| c_f1 | 2,750 | 2,895,971 | 796 |
| r_f1 | 2,344 | 2,546,999 | 674 |
| h_f1 | 10,699 | 8,902,960 | 1,377 |
| c_a1 | 1,884 | 979,955 | 423 |
| h_a1 | 4,438 | 2,085,151 | 571 |

Camp-size P90 / P95 by housing class (post-opening turns 10-50, camp-turn pooled; `Summarize-Cohesion.py`) against the design targets (unhoused 100,
tent-dominant 250, fixed-dominant 600-1,000), and the point score (lower is better):

| run | score | unhoused P90/P95 | tent P90/P95 | fixed P90/P95 | median size u/t/f |
|---|---:|---|---|---|---|
| c_f1 | 14.45 | 225/244 | 1385/1717 | 1506/1848 | 33/436/679 |
| h_f1 | 10.12 | 250/266 | 823/1147 | 1397/1679 | 77/322/625 |
| c_a1 | 4.58 | 93/222 | 665/674 | 737/788 | 27/384/445 |
| h_a1 | 5.49 | 205/226 | 630/664 | 720/747 | 30/303/465 |

QoL P25 / median / P75 by first technology (post-opening turns 10-50):

| run | agricultural | husbandry |
|---|---|---|
| c_f1 | 0.66/0.80/0.91 (32,492) | 0.41/0.76/0.91 (37,359) |
| h_f1 | 0.70/0.80/0.89 (73,326) | 0.64/0.87/0.93 (111,928) |
| c_a1 | 0.77/0.88/0.95 (17,344) | 0.70/0.82/0.92 (24,274) |
| h_a1 | 0.78/0.88/0.95 (19,929) | 0.73/0.88/0.94 (49,748) |

### Findings (hinterland and relative destinations)

- **The hinterland is what unblocks splitting; the relative test alone does not.** With the hinterland off (`r_f1`) the funnel is the baseline's: 83 percent
  of screened searches still find no candidate and fissions fall slightly (1,896 vs 2,302, dynamics, not a mechanism). With the hinterland on, camps with a destination
  rise from 4,323 to 20,090 (4.6x) and fissions from 2,302 to 10,237 (4.4x) with mortality off; with QoL mortality on, 1,468 to 4,013 (2.7x). The share of screened camps
  that find a destination doubles (10.6 to 20.8 percent; 17.8 to 30.8 percent).
- **Most screened searches still have no legal candidate** (68 percent mortality off, 57 percent on). A candidate must be a known, traversable tile at distance 3 or 4
  that is not within two hexes of any camp; a camp with fewer than 54 people (cap `2 sqrt(N/24) < 3`) or one that just moved still knows only radius 2, and in
  settled land the ring-3/4 tiles are often within spacing of a neighbour. This was not tuned further here: the levers are `HinterlandReferencePopulation`,
  `HinterlandRingsPerTurn`, `HinterlandMaxRadius` and `FissionRadius`.
- **The outcome is large, by design.** There is no population cap, so removing the destination bottleneck lets the world fill: camps at turn 60 rise 3.9x (mortality off)
  and 2.4x (mortality on), population 3.1x and 2.1x. Camp-size P90/P95 move toward the targets for tents and fixed housing with mortality off (tent P90 1,385 to 823;
  score 14.45 to 10.12) and are about unchanged with it on (score 4.58 to 5.49: unhoused P90 93 to 205 against the 100 target, the others within 10 percent of before).
  QoL medians are unchanged or higher. Whether this population growth is the right balance (land fill, food and QoL at larger scale) is a calibration question
  for the owner of the growth model, not settled here.
- **The daughter veto stays rare** (738 of 20,090 destinations; 66 of 7,932 with mortality on): a thin destination is usually better than a crowded home.
- **Cost.** Knowledge update 1.07 to 7.0 ms per turn (median, mortality off) at 3.9x the Societies and 1.5x the tiles per Society (118 to 179 explored tiles, 78 to 102 payload
  bytes per Society); fission 9.2 to 23.5 ms per turn (4x the camps); median turn 796 to 1,377 ms (also 4x the camps). Per Society the knowledge update is about
  0.65 ms per thousand Societies against 0.39 before.

## Deliberate limits

- The defaults are calibrated analytically to full goods coverage (table above); the realized size depends on
  housing per head (see Findings). `FissionMinimumDaughter` and the daughter fractions are
  unchanged from WP10a and untuned; `FissionRadius` (4) and `FissionDaughterBuilderShare` are set by the throughput measurements above.
- Distress fission uses the land-limited upper bound for the stayers (the local flow does not shrink with the headcount) and the
  site support ratio for the daughter, a planning estimate from current projected ecology; the distress branch exists only for `F W < 1`
  (a discontinuity at 1, see Distress fission). The measured effect in the five generated worlds is small.
- Sites are scored from ground-truth ecology of the footprint; only the chosen tile
  reaches the owner's observation.
- Immigration is WP10b ([Immigration.md](Immigration.md)); affinity decay, trade with kin (WP7-WP8) and the Elder (#23)
  are out of scope. `Kinship.Affinity` is the hook WP7 will read.
- Unity: no inspector control for the policy, no journal display, no drawing of a
  cached destination, no kin panel, and `OpeningGame` has not been rebuilt. The
  simulation-side APIs above are complete.
