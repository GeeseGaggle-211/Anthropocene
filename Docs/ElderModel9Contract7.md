# Native Elder contract 7 (model9)

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

Training-only critic inputs (`ElderModel9Village.Privileged`, `privileged.npy`; never part of the actor observation):
log population, episode age, site food / meals, footprint potential and current food / meals, sustainable and absolute
herd capacity / animals, last growth, daughters and fixed housing per resident. They carry the growth headroom the
actor's capped coverage ratios hide; the learner checks by test that they never reach the actor graph.

## Environment (`ElderModel9Environment`)

Every camp with a resident economy is a village and decides every turn; daughters join when founded. A village
without a directive runs the native heuristic, so an empty action map is the ordinary game. Each step returns,
per village: the reward terms (prior population, end QoL, years, welfare = P x Q x years, lineage society and
parent society), the teacher label from the exact allocation Prepare used (`ElderModel9Teacher`), interventions,
and the next observation. `VerifyReplay` re-runs the stored background orders, traces and directives from the
initial snapshot and checks every post-step fingerprint.

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
