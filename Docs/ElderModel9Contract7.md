# Native Elder contract 7 (model9)

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
