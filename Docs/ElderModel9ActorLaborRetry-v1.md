# Elder Model 9 actor labor and bounded retry, v1

Status: reviewed for isolated implementation of native contract 13/action 12. This is an opt-in source and unit-test contract, not a training, performance, or readiness acceptance. Legacy Model 9 contracts and actions remain unchanged when the feature is absent.

## Authority and timing

At the WEGO decision boundary the actor owns the full controllable pre-demographic camp population. Prior workforce assignments and discretionary native survival/production priorities are not immutable reservations. Only worker-time already physically consumed in the same period or an independently evidenced immutable commitment could be excluded; a rejected proposal consumes neither time nor state.

The actor chooses:

- one whole-resident specialty allocation over explicit idle plus the 13 current `WorkforceRoles`;
- one fractional worker-time activity allocation for each physical specialty, including explicit role idle;
- the retained movement, fission, and trade choices.

Specialty counts use stable largest remainders once. Activity allocations are fractional worker-equivalents inside those integer counts, so one resident can split a multi-year turn across activities. No second integer apportionment is performed.

Staffing is not an output promise. Material starvation, unknown competition, a destination losing local access, or an insufficient herd/field/fish stock may leave accepted capacity unused. Those are legal physical outcomes, not avoidable-action rejection. The compiler never inserts upstream workers, fallback Gatherers, sibling goods, or other specialties.

## Producer-owned registries

Native publishes ordered IDs, semantic numeric features, masks, versions, and SHA-256 registry digests. The learner scores rows with shared token projectors; it must not assign meaning from a volatile ordinal alone. Adding a specialty, activity, feature, or reason requires an explicit registry/schema migration. Native startup fails if `WorkforceRoles.All` drifts from the declared contract dimensions.

Specialties (14):

```text
idle, Gatherer, Farmer, Herder, Fisher, Craftsperson, Toolmaker, Potter,
Weaver, Carpenter, Builder, Woodcutter, Stonecutter, ClayMiner
```

Activities (22):

```text
idle, food.gather, stock.stone, stock.clay, stock.wood,
field.operation, field.expansion, herd.management, herd.expansion,
fishing.harvest, fishing.development,
goods.hand-tools, goods.pots, goods.cloth, goods.farm-tools,
goods.housing, goods.tools, goods.animal-fabric, goods.light-housing,
goods.herding-tools, goods.housing-material, goods.fishing-tools
```

Every current physical specialty has idle enabled. A present but unavailable specialty has an idle-only activity mask and zero specialty mask; padding is a separate learner concern. Fishing harvest and development are distinct so unused harvest work cannot become capital and development work cannot catch food.

Current dimensions are specialty `14 x 12`, activity `13*22 x 50`, resource `20 x 8`, and goods `11`. The current registry digest is `83012ab8f9bc043c9ce261484c1ec8b63d825715d451a8afd4499783c0b351c7`.

Resource rows are:

```text
resident-work, stone, clay, wood, plant-food, animal-food, animal-fabric,
housing-material, cloth, hand-tools, tools, farm-tools, herding-tools,
fishing-tools, terrestrial-food, aquatic-food, pasture-food, freshwater,
cultivable-capital, food-room
```

Activity features distinguish operation/harvest from development/expansion and include stable output-resource/good semantics. Capacity features are labelled as capacity, not guaranteed realized yield. Manufacturing rows expose recipe inputs and the stable physical schedule key; the schedule key is numeric and may be fractional (for example Housing Material is `3.5`).

The catalog is built at the origin/committed decision boundary. It does not expose destination facts not yet known to the actor. Action-conditioned destination scarcity may reduce execution but cannot create an avoidable penalty from an unseen constraint.

## Action schema 12

The initial action has exactly:

```text
schemaVersion: 12
movement: int
fission: int
tradeValue: double[7]
tradeGate: int[7]
tradeShare: double
laborSpecialtyAllocation: double[14]
laborActivityAllocation: double[13][22]
```

The specialty row is a masked simplex including global idle. Each activity row is a masked simplex including role idle. Values must be finite and nonnegative; native rejects even a small negative before apportionment. Masked coordinates are exactly zero.

Movement is one joint categorical choice: `-1` stays, local candidates are `0..6`, `-3` keeps region intent, and destination regions are `100..107`. Native heuristic movement `-2` is not legal in schema 12. Fission is an explicit ordinal `0..2`. All seven trade value/gate coordinates and `tradeShare` are supplied; inactive trade groups are context/no-op rather than omitted heads.

Strategic, economy-mix, service, clothing, shelter, route, and commitment heads are retired from this action. They cannot silently invoke the old labor allocator. Retry payloads contain only the two labor arrays; nonlabor choices are frozen from the initial action.

## Validation and bounded retry

`validate_labor` is a pure origin-catalog syntax/capability preview. It performs no RNG draw and mutates no world, replay, fork, or contextual state. Ordinary scarcity is not a rejection. Avoidable violations are limited to actor-visible malformed schema, nonfinite/negative values, masked nonzero values, row counts, and simplex failures.

There are at most two sampled proposals: attempt 0 and, only after rejection, one labor-only attempt 1. `remainingProposals` is 1 after a rejected attempt 0 and 0 after attempt 1. If attempt 0 rejects and no retry is supplied, native executes the physically safe all-idle labor fallback while preserving the initial nonlabor choice. It records only attempt 0; it never fabricates a second sample. An invalid retry also falls back. The world resolves once.

Per-attempt feasibility cost is zero without an actor-visible avoidable violation, otherwise:

```text
1 + sum(clamp(violation severity, 0, 1))
```

Its source-backed maximum is 319: base 1 + 14 specialty coordinates + 1 specialty sum + 286 activity coordinates + 13 activity sums + 4 nonlabor groups. A decision with two rejected proposals is bounded by 638. Fallback surcharge is zero. Settlement-guard cost is separate and bounded by 1. Catalog-unavailable and other server/runtime failures are not actor-avoidable costs.

Reason identities are numeric and text-bound by reason registry digest `d9f4fda6aed5a6599b599023c80ecf5507a1109b55a8945e8c92cccace0e1cb4`.

## Native bridge operations

The legacy top-level bridge schema remains unchanged. The `schema` response adds an `actorLabor` sidecar containing contract 13/action 12 versions, ordered registries, dimensions, digests, cost bounds, `maxProposals:2`, and feature flag `actorLaborRetryV1`. Reset opts in with `actorLabor:true` and confirms `actorLaborEnabled:true`.

Pure validation:

```json
{"op":"validate_labor","attempt":0,"actions":{"7":{}},"catalogDigests":{"7":"..."}}
```

returns:

```json
{"ok":true,"op":"validate_labor","attempts":{"7":{}}}
```

Single physical execution:

```json
{"op":"step_labor","actions":{"7":{}},"retryActions":{"7":{}},"catalogDigests":{"7":"..."}}
```

returns the ordinary encoded `step` plus camp-keyed `laborFeedback`. Camp keys are canonical decimal strings. In actor-labor mode ordinary `step` is rejected so callers cannot bypass attempt accounting.

## Physical execution

Accepted activity worker-years are hard per-role/per-activity upper bounds. Production order and actual execution use the same stable physical dependency/schedule order. Export-first and domestic production debit the same remaining budget once. Actor-selected legal goods are not skipped because legacy `Priority` or `UsefulDemand` is zero, and an unrequested sibling cannot inherit unused work.

Raw Stone/Clay/Wood extraction respects the sampled activity fraction; an extractor's idle fraction remains idle. Food Gatherer idle produces no passive harvest. Farmer, Herder, and Fisher idle produce no field, herd, catch, or development work. No native Dispatch order may shrink or reserve the actor-owned pool after the action.

Manufacturing receipts remain authoritative for output and consumed manufacturing worker intervals. Material receipts remain authoritative for extraction amount and consumed worker intervals. Existing field/herd/fishing work summaries do not all measure scheduler-consumed work consistently; unavailable values remain null rather than copied from committed staffing or inferred from output.

## Successor feedback

`laborPrevious` is finalized before the next observation and binds:

- the immutable prior decision catalog, population, duration, registries, masks, and digest;
- untouched initial and final sampled requests, accepted whole-resident counts, planned activity worker-years, violations, costs, remaining proposals, and fallback identity;
- committed activity worker-years;
- nullable scheduler-consumed worker-years with per-coordinate known masks and typed reasons;
- planned and realized manufacturing output by good;
- gross typed physical receipts by resource.

Gross receipt feedback uses production/material/harvest/herd witnesses, not imports or closing-stock differences. Stone/Clay/Wood bind by persisted `MaterialId` identity. Plant/animal/aquatic food bind from typed field/gather/fish transfers, owned hunter transfers through the authoritative logistics owner, and managed-herd accepted/slaughter food. Hunter harvest overflow is removed in the native hunting-then-fishing allocation order because it never became a stock receipt. If any hunter receipt lacks its owner identity, total food rows remain unavailable rather than reporting a partial total. Craft resources bind from matching production receipts. Source-class and stock-component food rows are alternative typed views of the same receipt and must not be summed as one conservation ledger.

Recipe material consumption is present in native production receipts, but feedback v1 has no separate typed input-consumption lane. It is therefore not subtracted from gross resource receipts or fabricated as a net value. A future lane requires a feedback schema migration.

A vanished camp uses explicit `camp-vanished` unavailability. Daughter history is absent unless a separate lineage field explicitly identifies inherited context. Fork/restore copies finalized prior feedback; transient validation has no pending state to serialize.

## Required isolated fixtures

The implementation tests, without training or readiness claims:

1. global idle creates no passive food, raw material, craft, field, herd, or fish labor;
2. specialty apportionment owns the full population despite hostile prior assignments;
3. material-starved Weaver staffing remains accepted while actual work/output falls, with no Gatherer rescue or feasibility penalty;
4. zero activity cannot supply export demand, and export plus domestic production spends a shared budget once;
5. a starved sibling good cannot lend work to another good;
6. fishing harvest cannot become development and development cannot become catch;
7. validation is pure; one retry advances once and preserves both samples;
8. missing retry records attempt 0 plus explicit fallback, not a fabricated attempt 1;
9. ordinary `step` cannot bypass the actor-labor protocol;
10. requested/committed/actual work and production/import/net-stock quantities remain distinct;
11. registry identity, catalog digest, nullable reasons, and persisted material IDs round-trip through JSON.
