# Native Elder contract 7 (model9)

## Contract 9 amendment (19 October 2026): capital commitments

Design: `Docs/ElderCapitalCommitmentsDesign.md`, Part 1. Action, observation, environment and replay schemas are 9.
Schema-8 and schema-7 directives remain valid; checks that meant "has fishing development" now test schema 8 or
later (`ElderModel9Contract.FishingSchemaVersion`).

- **Action.**
  - `ElderDirective.CommitTurns`: 1 (the default) decides the capital controls for this turn only, exactly as
    contract 8.
  - 2-6 commits them for that many turns. The capital controls are retained field operation, retained herd
    fraction, Development budget, field and fishing shares, and the clothing and shelter routes.
  - 0 holds the active commitment, re-emitting its values; service levels, movement and fission stay per-turn.
  - Out-of-range spans are repaired to 1 and logged.
  - Teacher labels are `CommitTurns 1`, so the heuristic stays exactly representable.
- **Authoritative state.**
  - `ElderInstitutionMemory` schema 2 stores the commitment: the validated request (not the compiled outcome), the
    decision time, the span, the holds left and the capability bits. It also stores the last forced break.
  - Schema-1 memories load with none.
  - `ElderCommitments.Resolve` runs in the compiler on the committed world, and `Record` runs at resolution, so
    replays reproduce holds exactly.
- **Forced breaks.** A hold is refused, logged as a `commitment` intervention, and the submitted controls are used
  for the turn when:
  - the commitment is absent or expired;
  - the camp relocated or fissioned after the committing decision;
  - its economy capabilities changed;
  - it has an active food deficit.
- **Observation.**
  - An 11-feature commitment block is appended to the core: active, remaining and elapsed turns, the committed
    controls, routes, and broken last turn.
  - `ElderActionMask.CommitmentHold` and the tensor's `CommitmentHoldMask` say whether a hold is legal.
- **Checks.** `ElderModel9Checks` cover:
  - commit, hold equals explicit resubmission, expiry, no-commitment fallback;
  - forced break on a shortfall, span repair, schema-8 directives;
  - the save round trip, the hold mask, and exact replay with holds.

## Curriculum fixtures and diagnostics (17 October 2026)

- **Capability fixtures.** `SimulationConfig.TrainingWithheldTechnologies` is a training-only bitmask
  (`1 << (int)Technology`). It is `[NonSerialized]`: never saved, hashed or set by gameplay, and zero is the game.
  - The Paleolithic breakthrough picks the most-practised allowed domain, and secondary discovery skips withheld
    technologies.
  - `ElderModel9Environment.WithheldTechnologies`, `GenerateOpening(..., withheld)`, the replay record and the bridge
    `reset {withheld}` re-apply it after every load.
  - Masks: herding-only 12, farming-only 10, fishing-only 6.
- **Tools.**
  - `--model9-teacher=...,players,withheld` writes fixture shards.
  - `--set=Field=Value` (dotted for nested settings) overrides the opening configuration for calibration sweeps.
  - `--model9-scan` counts grassland and husbandry villages per opening.
  - `--model9-pastoral` compares herd-first directives against the heuristic for grassland villages.
  - `--model9-requests` prints each village's shelter and clothing requests, grants and unmet causes.
- **Grassland bank.** Seeds 4000-5999 hold 337 openings with at least one husbandry village on grassland
  (`model9-data/grassland-herding-seeds.txt`). They are outside TRAIN, DEVELOPMENT and CONFIRMATION.
- **Pastoral finding.** Herding-only worlds run by the heuristic stay fully fed but are badly under-sheltered
  (housing coverage 0.04, clothing 0.6, QoL about 0.1; median settlement about 190).
  - Without cultivation, clothing and tents come only from Animal Fabric, and beyond protected clothing the
    reserve-clothing and tent requests failed for "animal feedstock". (Corrected 18 October: the cause was not meals
    taking herd food first, but tents paid behind the reserve, herdless fission daughters and herd growth staffed after
    the reserve refill; fixed by the animal textile need, `Docs/HerdLabour.md`.)
  - Societies without Construction raise tents only at the generalist rate.
  - Neither `AnimalFoodPerManagedAnimal` (x2-x6) nor a 4-8x tent output changes housing materially; x3 food lifts
    herding population about 60% and the median settlement toward 240.
  - Calibration is unchanged pending the planner's food/feedstock arbitration (see the M7 report).

## Contract 8 amendment (17 October 2026): three-way economy mix

Contract 8 (`ElderModel9Contract.NativeContractVersion = 8`, action/observation/environment/replay schema 8) makes
fishing a development channel beside fields and herds. Schema-7 directives remain valid and never develop fishing.

- **Action.** `ElderDirective.FishingDevelopmentShare` (f) is the fishing share of `DevelopmentBudget`;
  `FieldDevelopmentShare` (s) still splits the remainder between the field and herd ray. The learner's economy-mix
  simplex (cultivation, herding, fishing) is ((1 - f) s, (1 - f)(1 - s), f).
- **Compilation.** The fishing part becomes one Development request ("fishing development", `ElderDirected`) for
  whole Fishers beyond the survival frontier, capped by the residents left after the frontier. Its food service is
  the extra catch of those Fishers. Fishing Tools are sized for frontier plus development Fishers. Like the Elder's
  field/herd prefix it is granted up to its units, and it ranks ahead of the native growth-gathering request. As the
  lowest service class it competes with reserve gathering and goods for labour, which the food and goods service
  levels control. An unmet request logs a `fishing-development` intervention.
- **Masks.** `ElderActionMask.FishingDevelopment` is resident fishing with a legal water source. The tensor carries
  `EconomyMixMask` (cultivation, herding, fishing). Validation forces f = 0 when fishing is unavailable, and f = 1
  (with a positive budget) when fishing is the only channel.
- **Observation.** Six core features: fishing-development availability, last allocation's frontier Fishers per
  resident, next-Fisher catch over one gatherer's turn yield, fishing food over meals, and the previous fishing share
  and development Fishers per resident. Site rows widen to 14 with a legal water source next to the site and the
  distance-weighted yield of known fishing grounds within `FishingEconomy.GroundRange`. A fishing camp without a
  cultivation or husbandry economy now gets site rows: the Elder builder evaluates the same coarse coverage the
  shared observation builds for agricultural camps. The native `AgentObservation` is unchanged.
- **Teacher.** The heuristic never develops fishing, so its label is f = 0 (`FishingShareMask` where fishing is
  available and the budget is positive). Teacher NPY shards add a fifth strategic column and `economy_mix_mask.npy`.
- **Fidelity.** Seeds 17 and 53, 8 turns: labels as actions reproduce the heuristic exactly (welfare ratio 1.000),
  and none of the 79 heuristic moves is unrepresentable (the earlier contract-7 probe found 22 of 95), because
  fishing camps now have site rows. Heuristic
  per-turn fingerprints are unchanged (seed 1001, 12 turns). `ElderModel9Checks` stages a fishing-only directive.

Status: implemented on `feature/elder-model9-native` (M4 of the learned-Elder plan). The learned Elder acts
through the DemandControl compiler at the service level; the native heuristic is expressible exactly as one
action, so its decisions are teacher labels without translation.

## Action (`ElderDirective`, schema 7)

| Control | Meaning | Native default |
| --- | --- | --- |
| `RetainedFieldOperation`, `RetainedHerdFraction` | retained field operation and herd fraction (settlement supervisor ladders) | heuristic portfolio |
| `DevelopmentBudget`, `FieldDevelopmentShare` | development persons / residents and their field share; any positive herd budget also admits free growth (animals the existing herders can manage) | heuristic ray search |
| `ServiceLevels[6]` | food, clothing, shelter, tools, storage, materials: share of each service's native request total, filled from higher service classes first; never adds units beyond the physical request | `null` = native |
| `ClothingRoute`, `ShelterRoute` | 0/1 (Cloth / Animal Fabric, fixed Housing / Light Housing); used when feasible this turn, otherwise native route with a logged intervention | `-1` = native |
| `MovementCandidate` | site row, `-1` stay, `-2` native movement (curriculum stages 1-3) | |
| `FissionPolicy` | `FissionPolicy` ordinal, `-1` keeps the heuristic's policy order | |

Validation repairs and logs out-of-range or malformed controls (`ElderDirectiveValidation`). There is one compiler
(`NativeElderDirectiveCompiler` -> `DemandController.Plan`); it never substitutes a better plan.

## Observation 7 (`ElderModel9Encoder`)

- `core` (`ElderModel9Contract.CoreWidth`): the v2 core/fields/herd/previous/fission/capability/memory blocks,
  presence and movement masks, a demography block (gross birth and cause-specific death rates, food surplus per
  resident, birth-funding fraction, fed capacity, housing and clothing per resident, surplus-births flag), the
  current fission policy, and last turn's route feasibility and service requests per resident. Every value has a
  present flag.
- Entity sets with row presence: sites (7 x 12, value-ordered: current first, then human food after herd use),
  fishing grounds (3 x 11), herd groups (8 x 8), commodities (11 x 8), contacts (8 x 9, the last column the remembered
  coarse population band, 1 + floor(log2 N) at contact, 0 unknown).
- Masks: movement (stay + site rows), clothing and shelter routes.

Values are raw physical units; the learner normalizes them.

The previous-decision blocks (route feasibility, service requests per resident, and the contract-8 fishing block)
come from the per-village `ElderModel9Context`, updated from the decision that executes at each boundary whether or
not teacher labels are emitted (`ElderModel9Environment.UpdateContext`). A native DemandControl allocation defines
every block. An action-12 actor-labor decision has no native route plan, service requests or frontier catch
forecast: those blocks are zero and not present, and only its accepted fishing staffing is present (harvest
Fishers in the frontier-Fisher slot, development Fishers, and fishing over all accepted development workers as the
share). Before 26 October 2026 the context updated only inside the labelled native branch, so an action-12 village
kept its last native values, still marked present, for the rest of the episode (`ElderModel9ContextChecks`).

Training-only critic inputs (`ElderModel9Village.Privileged`, `privileged.npy`; never part of the actor observation):
log population, episode age, site food / meals, footprint potential and current food / meals, sustainable and absolute
herd capacity / animals, last growth, daughters and fixed housing per resident. They carry the growth headroom the
actor's capped coverage ratios hide; the learner checks by test that they never reach the actor graph.

### Privileged critic view (`ElderModel9Privileged`, `PrivilegedSchemaVersion = 1`, 26 October 2026)

Intervention A's critic-only view of hidden economic state, on `ElderModel9Village.PrivilegedView` with
`PrivilegedSchemaVersion` on every row (teacher shards: `privileged_<family>.npy`, `privileged_<family>_present.npy`,
and `privilegedSchemaVersion`, `rowSchemaVersion` and the full `privilegedLayout` in `meta.json`). Families, in order:

| Family | Rows (cap + overflow) | Width | Contents |
|---|---|---|---|
| 0 `core` | 1 | 10 | `PrivilegedFeatures` unchanged (bit-identical to the earlier vector; `Privileged` is a copy). |
| 1 `fields` | 16 + 1 | 19 | Cultivable radius-1 footprint plus radius-2 tiles the camp still operates, by distance then tile ID. |
| 2 `herds` | 8 + 1 | 24 | Owned herds by herd ID. |
| 3 `unlocks` | 1 + 1 | 28 | The camp's Society. |
| 4 `economy` | 1 + 1 | 64 | The camp, plus summed counterpart stocks of known trade partners (training-only). |

Typed families start with `row-count` (1 per row); the last row is the overflow aggregate, the column-wise sum of
every row beyond the cap (so its `row-count` is the number aggregated), present only when something overflowed.
Absent rows are zero with presence false. Features and units:

- fields: row-count; distance-hex; operated-by-camp, foreign-operated, access (flags); capital-E (field capital,
  projected to now); intensity-q; operating-fraction (the camp's current Farmer cover of required upkeep, on operated
  tiles); food-stock, biomass-stock (food units, projected); land-condition (0..1); vegetation-tier (0..3);
  mean-soil-moisture, mean-max-food (food units), mean-food-recovery-per-day (four-season means); crop-suitability;
  expansion-available (flag: accessible and below the reference capital ceiling); maintenance-farmers (upkeep workers
  for this tile's capital); farm-tool-demand (Farm Tools to cover that upkeep over the turn).
- herds: row-count; animals (exact); species-index (configuration order); growth-rate-per-year;
  food-per-animal-interval, water-per-animal-interval; starvation-mortality; growth-remainder, death-remainder
  (fractional animals); food-nutrition, water-nutrition, nutrition (0..1); unmet-forage (food units), unmet-water;
  deficit-turns; deficit-severity; managed (flag); management-fulfilment; breeding-intensity; shared
  zero-displacement and absolute forage capacity and water capacity (animals, the camp site's herd support envelope);
  animal-food-potential, animal-food-produced (food units, last turn).
- unlocks: row-count; era-neolithic; first-technology one-hot (husbandry, cultivation, watercraft); acquired flags;
  lifetime practice, Neolithic practice and knowledge exposure by domain (hunting, gathering, fishing; practice
  units); progress-over-threshold by domain (effective progress over the era's threshold); withheld flags;
  forced-transition; secondary-domain-plus-one; era-elapsed-years; neolithic-technologies, unlocked-specialists
  (counts).
- economy: row-count; residents, linked-population (people); food, plant-, animal-, aquatic-food (food units); stock
  of each `CraftGood` (native units; Housing as fixed housing units now); stone, clay, wood (material units);
  recipe-available per good (flags); farm-tool-farmer-years; meals-per-turn (food units); clothing-need-units;
  housing-capacity-people; cloth- and housing-per-capita (coverage); workers per `WorkforceRoles.All` role
  (people); trade-partners (count of the partners `Trade.Plan` would pair, nearest first, at most
  `TradeRequisitionsPerCamp`); partner-food and partner stock per good (summed exact stocks: training-only, never
  published to the actor).

Row pairing (`ElderModel9Contract.RowSchemaVersion = 1`): a returned village row pairs the outcome of the transition
it just executed (`ExecutedTransitionFields`: prior/end population, QoL, years, welfare, trade utility, teacher label,
interventions, decision diagnostics, labor feedback) with the observation of the next decision
(`NextDecisionFields`: `Observation`, `LaborCatalog`, `Privileged`, `PrivilegedView`). The privileged view is built in
`Observe` at that same boundary, before any action applies, and is aligned with the observation, never the reward.

Cost: one pass per step over herds, societies, hunters and camps builds `ElderModel9Privileged.Index`; each village then
costs O(19 footprint tiles + owned herds + remembered contacts + goods) with no per-village world scan (family 0's
earlier per-village herd and society scans now use the index). Small/8 seed 31, 25 turns, 28 villages: privileged
emission 2.22 ms per step (0.08 ms per village decision, 0.85% of the step) against 0.87 ms for the ten-float vector
alone; 16 players, 37 villages: 2.63 against 0.97 ms. World fingerprints are unchanged.
`ElderModel9PrivilegedChecks` covers layout, the bit-exact family 0, boundary capture, overflow, actor-visible
invariance under privileged-only perturbation and the filtered-observation contract.

## Environment (`ElderModel9Environment`)

Every camp with a resident economy is a village and decides every turn; daughters join when founded. A village
without a directive runs the native heuristic, so an empty action map is the ordinary game. Each step returns,
per village: the reward terms (prior population, end QoL, years, welfare = P x Q x years, lineage society and
parent society), the teacher label from the exact allocation Prepare used (`ElderModel9Teacher`), interventions,
and the next observation. `VerifyReplay` re-runs the stored background orders, traces and directives from the
initial snapshot and checks every post-step fingerprint.

### Herd ledger (economic diagnostics schema 3, 26 October 2026)

With `EconomicDiagnosticsEnabled`, every managed-herd row closes exactly:
`opening + newbornAnimals + acquiredAnimals + grantedAnimals + ownershipInflow - starvationDeaths - slaughtered -
ownershipOutflow = committedEndingAnimals`. Schema 2 recorded only births, deaths, slaughter and fission outflow, so
two resolver paths changed counts silently: taming (`HusbandryAcquisition`, a typed `herd-acquisition` transfer from
wild herd `wildHerdId`, `sourceCamp = -1`, into an existing or new herd at the end boundary) and the opening Husbandry
compatibility grant (`OpeningTransitions.Apply`, `herd-compatibility-grant`, `InitialDomesticAnimals` for a camp
that breaks through mid-episode). Fission herd splits already carried `fission-herd`; the receiving side is now the
typed `ownershipInflow` (a daughter is founded mid-turn and has no row until the next decision). Each inflow is also
a physical event (`herd-acquisition`, `herd-compatibility-grant`, `transfer-in`); in the conservation row,
production is births plus grants and transfer-in is acquisition plus ownership inflow. `ElderHerdLedgerChecks`
asserts closure, the committed closing count, opening = previous closing, and typed acquisitions = tamed animals
over three multi-turn fixtures. The learner's schema-2 validator rejects schema 3 until it registers these fields.

## Teacher fidelity

`--model9-probe=<seed>,<turns>` runs the heuristic (A) and its own labels as actions (B) in lockstep. Seeds 17,
53 and 991, 8 turns each: no village differs in population or welfare (ratio 1.000), replay verified. Making the
heuristic exact needed: native movement/fission pass-through, labels from the committed kernel routes and the
selected development budgets, free herd growth under a positive budget, and two heuristic fixes:
- `TrimToBudget` left a trimmed request's staff idle (one worker released per role); the compile now recompiles
  staffing, extraction and ceilings from the trimmed grants.
- Order replacement no longer adds a NoOp beside a heuristic dispatch (Prepare rejects conflicting orders).

Fingerprints still differ between A and B because directive decisions are recorded in Elder memory; outcomes do
not.

## Throughput (measured, Small/8 worlds, one native worker)

About 55-85 village-decisions per second per worker (teacher and directive paths alike). The planning estimate of
800-1,000 per second assumed 80-village resolves; per-decision cost here is dominated by whole-world resolution of
10-25 villages. Teacher volume therefore uses parallel worlds (`--model9-teacher`), not one long run.

## Teacher data (`--model9-teacher=<outDir>,<first>,<last>,<turns>[,players]`)

One NPY shard directory per world: the observation sets and masks, strategic/service/route/movement/fission
labels with their label masks, compiler repair size, and camp, turn, society, parent society, prior and end
population, QoL, nutrition, years, welfare and termination. Checks: `ElderModel9Checks` (fixed suite).

## Termination and dormant villages

A village row is `Terminated` only when its camp is extinct: no resident and no linked hunter group alive
(`WorldState.LinkedPopulation(camp) == 0`), so nobody can return. A previously observed camp whose residents are all
away hunting is *dormant*: it has no observation and no decision (it is not in `Villages()`), and each step reports it
with `Terminated = false` and `Observation = null` until its hunters return (the camp is observed again, same id, not
`Founded`) or the last linked person dies (that step's row is its terminal row). Before this rule a camp with zero
residents was terminated at once although its hunters later returned (world 90104 camp 24 in the r5 collection).
Checks: `ElderModel9Checks` (dormant and extinct cases).
