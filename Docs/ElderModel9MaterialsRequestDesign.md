# Elder model9 raw-material request

Status: **reviewed architecture amendment**, 22 October 2026. The original design was approved after independent Luna
and Astra reviews, but implementation exposed a temporal error in its Prepare-time Wood projection. The conditional-
authorization and late-global-settlement replacement received final Astra review on 22 October. This is architecture
approval, not implementation or launch approval. The feature remains flag-gated and is not enabled in Round 3.

Implementation status: this is latent infrastructure, not a delivered positive-authority raw-gathering capability.
Current normal `TurnPlanning.Prepare` conservatively commits every residual resident interval to native fallback
gathering, so it exports no genuinely unused Phase-A labor and produces zero Phase-B authorization/grant/receipt.
Positive full-turn raw gathering is deferred until a distinct workforce-idleness contract is separately reviewed and
adopted. Round 3 raw materials remain off. Direct synthetic-authority tests of the late settlement kernel establish
conservation, ordering and caps only; they do not establish reachable positive end-to-end authorization.

The raw-on zero-authority path is physically equivalent to raw-off, but intentionally not label/observation-identical:
the existing materials service slot (index 5) is requested, so its teacher target/mask and RepairSize reflect the
typed request, and the existing previous-service-requested Core coordinate reflects its normalized service equivalent.
The corresponding requested/accepted/realised diagnostics remain sidecar-only; no actor tensor width or action head is
added. Conformance compares every other actor feature/label and the complete physical save projection, while checking
these exact service-5 deltas against the raw request. This expected feature/teacher delta must not be described as full
save or full observation equality.

## Decision

DemandControl may create a standalone `materials` request for bounded raw gathering. It uses the existing model9
goods-service slot `ElderModel9Contract.GoodsServices[5]`; there is no new action head or observation tensor field.
The request is off unless the new saved configuration flag is enabled. Saves without the flag load it as false.

The purpose is to let the Elder authorise useful raw stock gathering even when no current craft bundle happens to pull
that material. It is not a general mining system, a new storage model, or an excuse to gather an arbitrary surplus.

Round 3 freezes three `NeolithicConfig` tuning fields: `ElderRawMaterialUnitsPerResident = 0.05`,
`ElderRawMaterialMinimumUnits = 1`, and `ElderRawMaterialMaximumUnits = 8`. For each material with positive camp
priority and a legal extraction role/rate, its additional floor is
`clamp(population * UnitsPerResident, MinimumUnits, MaximumUnits) * clamp(priority, 0, 1)` physical units. Stone, clay,
and wood share these defaults because one current raw unit feeds one configured recipe-input unit. The fields are
optional-save values, validate as finite with `0 <= minimum <= maximum` and `UnitsPerResident >= 0`, and are owned by
simulation calibration: they may be changed only in a new pre-registered run, never while a round is active.

## Authoritative state and turn semantics

Authoritative quantities remain camp material inventory and `WorldState.MaterialSources`. Source discovery, material
technology, accessibility, renewable Wood projection and the normal extraction-rate rules remain authoritative.

The new request, its conditional authorization and its extraction allowances are transient prepared-turn state. They
are derived once from the committed turn boundary, copied into the immutable production/material plan, and consumed at
most once during resolution. They are never saved as inventory or regenerated on load.

Request units are physical raw-material units by material ID. They are a target-stock shortfall, not a recurring flow,
so they are not multiplied by elapsed years except where an existing physical extraction recipe explicitly uses the
turn duration. Existing admitted recipe inputs, housing-material inputs, and the exact reserve target produced by
`NeolithicConfig.MaterialReserveMultiplier(years)` (including legacy-turn migration) are baseline commitments.

Prepare does **not** project endpoint source quantities or freeze baseline debit intents. In particular, Wood at the
resolver endpoint depends on provisional cultivation allocation, one tile integration, realised gathering, cultivation
reconciliation, and a possible corrected field-capital vegetation clamp. Those facts do not exist at the Prepare
boundary, and duplicating or approximating that pipeline would create a second ecological truth.

Instead, Prepare freezes the ordinary baseline target `B_m`, the additional floor `F_m`, and the applicable opening
inventory `I_m`. The prepared raw request ceiling is
`U_m = max(0, B_m + F_m - I_m)`. A repaired materials-service fraction `f` and genuinely unassigned prior-cohort
labour produce a **conditional authorization** `A_m`, with
`0 <= A_m <= f * U_m`. The authorization records eligible known/discovered source identities, but it does not promise
their endpoint availability or reserve any physical stock.

The existing resolver remains authoritative for ordinary baseline gathering against the actual post-integration,
post-reconciliation endpoint. Immediately before ordinary baseline extraction it captures the applicable inventory,
`I_pre-baseline,m`; while extracting it captures actual baseline receipts, `R_baseline,m`. After every camp has finished
ordinary baseline extraction, production, optional goods settlement, and needs, one separate global raw-settlement pass
computes
`D_m = max(0, B_m + F_m - I_pre-baseline,m - R_baseline,m)` and may commit only
`X_m <= min(A_m, f * D_m, remaining dedicated extraction capacity, authoritative residual source availability)`.
The `f * D_m` bound is applied again because baseline receipts may have reduced the eligible deficit after authorization.
Raw proceeds enter inventory only after the completed production pass and therefore cannot enlarge any same-turn goods,
needs, trade, or production result.

The request is bounded by all of:

- the frozen target-stock policy above;
- current inventory after already planned inputs;
- known, discovered and accessible local sources;
- material priority and extraction capability/rate;
- shared resident labour, food and tools; and
- an authorization ceiling derived from eligible source identity and dedicated labour; and
- the authoritative residual source quantity observed only in the late settlement pass.

## Planning and execution

The overall `TurnPlanning.Prepare` pipeline is one pure two-phase policy-and-labour plan; the phases do not both occur
inside the initial `DemandController.Plan` call. Phase A remains in `DemandController.Plan`: it constructs,
service-caps, arbitrates, and freezes every existing Protected-through-Development request, production ceiling, and
Phase-A staffing decision. Only after the resulting `ProductionOrder`, `Forecast`, `Housing` plan and reserve horizon
are complete does `TurnPlanning` derive the exact ordinary baseline target `B_m` and construct the conditional raw
authorization. Prepare records the known/discovered eligible source identities but does not forecast their endpoint
quantities or replace the legacy resolver's baseline gathering.

Phase A freezes every resident interval committed to existing food, goods, development grants, baseline extraction,
recipes, and native fallback gathering. In the current normal Prepare implementation, native fallback consumes the
entire residual pool; therefore the exported genuinely-unused prior-cohort resident/interval pool is empty. Absence
from `Assignments` alone is not evidence of availability. The pool must never be a relabelled copy of
food gatherers, specialist workers, or any other Phase-A assignment. If no genuinely unused interval exists, the raw
authorization is zero. The frozen labour plan exposes that pool and its role/interval constraints. Consequently all
ordinary full-turn tests must establish zero reservation/grant/receipt and exact Phase-A physical equivalence,
including endpoint Wood/new-field-capital behavior; they must not claim positive raw service is reachable today.

`DemandController.FinalizeRawMaterialPhase` is then invoked as a pure continuation from that frozen Phase-A state.
Every typed raw request is `ServiceClass.Development`. It applies the Elder's repaired materials-service level
proportionally and may reserve extraction roles only from the exported unused pool, subject to frozen food/storage and
role constraints. Phase B may not reassign, relabel, or consume any Phase-A worker or interval. Newly reserved Gatherer
intervals are excluded from passive food and ordinary baseline material gathering; newly reserved specialist intervals
are excluded from ordinary baseline extraction and recipes. No unused Phase-B interval returns to an earlier pass and
no unused Phase-A time may be borrowed after receipts are known. Rates use the frozen prior cohort and committed-boundary
capability; newborns and newly discovered technologies cannot contribute. The final intent/labour plan explicitly shows
this opportunity cost.

Finalize appends typed per-material requests and conditional authorizations without reopening Phase-A ceilings or
changing a Phase-A grant. Allocation diagnostics and all head labels are finalized before `ElderModel9Environment`
labels the prepared decision. Resolution may reduce physical fulfilment, but it may not revise targets, roles, workers,
source discovery, requests, accepted actions, labels, production ceilings, or any earlier result.

The request has a non-null typed raw `Bundle`, a material ID, a physical-unit target, the extraction role, and complete
per-unit worker-year and source requirements; it is not a zero-cost quantity request. Under the current extraction
model, raw gathering has no separate tool wear or food-input debit, so those bundle costs are explicitly zero; resident
food needs and opportunity cost remain represented by the shared staffing/labour budget. Any later extraction-cost
model must change planning and resolution together in a new simulation contract.

Stone, clay, and wood physical units are not added directly for the single service head. Each request also carries a
`ServiceEquivalent`: the extraction worker-years represented by its physical target,
`physical_units / annual_extraction_rate`, where
`annual_extraction_rate = SpecialistProductivity.ExtractionRate(...) * DaysPerYear / BaseDays` for the frozen chosen
specialist role and committed-boundary population (or the matching generalist-efficiency rate when that is the legal
role). `SpecialistProductivity.ExtractionRate` itself is per reference/BaseDays worker interval, not per year. This exact
annualized rate is used both for the service-equivalent denominator and the bundle's role-worker-years; resolution
converts those years back to worker intervals with the same `perYear` convention as `Gather`.
`Allocation.ServiceRequested[5]` is the sum of those equivalents;
`ServiceGranted[5]` is the sum of each request's equivalent multiplied by `Granted / Units`. A learned materials level
scales every eligible material request by that same fraction before ordinary arbitration, rather than spending a pool
of unlike physical units in request-list order. This keeps the head dimensionless and preserves material identity.
Both service aggregation predicates include the typed raw bundle explicitly.

Accepted quantities are compiled into explicit `(camp, decision turn, material, role, physical ceiling, request id)`
conditional raw-extraction authorizations on the transient `ProductionOrder` or frozen material plan it owns. Every
authorization is capped by that request's `Granted` quantity and dedicated extraction capacity. They do not use
`ProductionOrder.PolicySurplus`, which has different autonomous-policy and goods-ceiling semantics.

The existing `MaterialEconomy.Gather` path executes ordinary current-input and reserve gathering once against the actual
resolver endpoint. It does not consume a Prepare-produced baseline-debit substitute. It records the inventory immediately
before baseline extraction and the actual baseline receipts; neither value is reconstructed from post-production
inventory.

Only after **all** camps have finished ordinary baseline extraction and production, including the optional goods-trade
block and needs, and before any pending birth/death population mutation, entity removal, migration, fission, or other
Close operation, a separate canonical pass settles the frozen authorizations. Giving an earlier camp optional raw stock
before a later camp's baseline extraction is forbidden. The pass preserves each authorization's material and role,
cannot substitute workers or sources, and cannot extract more than the matching authorization, repaired fraction of the
remaining eligible deficit, dedicated labour, or authoritative residual source balance. It processes camp ID, material
ID, role name, distance, then stable source ID; every debit revalidates current availability. It credits the camp and
writes ordinary `TurnRecord.MaterialTransfers` receipts with source-debit/camp-credit equality, including fractional
shared-body sources. Raw stock is next-turn inventory: the resolver does not reopen production, goods, trade, needs,
cultivation, or any earlier calculation.

## Elder and diagnostic contract

`ElderModel9Contract.ServiceOf` maps the new typed request to service index 5. `Allocation.ServiceRequested` and
`ServiceGranted` use the worker-year service equivalents above without folding baseline inputs or reserve extraction
into the new request. The existing teacher rule may then label the materials service from grant/request.

`MaterialReceipt` gains optional diagnostic-only `Phase` and `RequestId` fields. Existing current-input and reserve
receipts use a null/baseline phase; the new allowance passes `Phase = "elder-raw"` and its stable request ID through the
canonical extractor. Realised raw units are the sum of receipts matching that phase, request ID, camp, and material--not
all material receipts for the camp--so baseline extraction cannot be misreported as fulfilment of the Elder grant. Old
receipts with missing fields remain baseline receipts.

`ElderModel9Step` gains `DiagnosticSchemaVersion = 2` and a diagnostic-only dictionary keyed by controlled camp and
material. Each value records physical units requested, compiled/granted, and actually received, plus the stable request
identity and decision turn. The environment copies requested and compiled values from the matching allocation before
resolution and fills realised units only from `TurnRecord.MaterialTransfers` receipts matching the sidecar's
`elder-raw` phase, request ID, camp, and material after resolution. Missing requests are explicit zero/absent entries,
not stale prior-turn values. Forks clone pending
diagnostics. `ElderModel9ReplayStep` persists the same expected sidecar; replay reruns the turn, reconstructs actual
diagnostics from the replayed allocation and receipts, and compares them alongside the post-fingerprint. Older replay
payloads have schema version 0 and no sidecar; the superseded provisional schema version 1 used different physical
settlement semantics. Schema 1 is not upgraded or treated as physically replay-compatible: a flag-on schema-1 replay
fails closed and must be regenerated under schema 2. A schema-1 fixture may continue only when the raw feature was off
and the ordinary semantic fingerprint proves the unchanged legacy path. Schema-0 fingerprint verification still runs,
while its absent-sidecar diagnostic comparison is explicitly skipped.

In diagnostics, `requested` is the prepared request ceiling, `accepted`/`compiled` is conditional authorization after
service and labour repair, and `realised` is the matching late-settlement receipt. Accepted is deliberately not a promise
of endpoint physical delivery. Optional post-resolution fields may report eligible deficit and controlled shortfall
reasons, but must not rewrite `Allocation.ServiceGranted`, accepted actions, teacher labels, or policy-memory inputs.

The bridge serializes that sidecar separately from actor tensors. These values do not enter the observation, reward, or
policy memory in round 3. Contract-12 observation and action schema versions therefore do not change; the diagnostic
schema is independently versioned.

## Compatibility, determinism and scaling

The configuration flag is an optional saved `SimulationConfig` field. `InitializeNeolithic` explicitly assigns false
in `OnDeserializing`, so old saves default off. Request creation is gated by both the flag and
`EconomicAllocation == DemandControl`; with the flag off, no raw request, authorization, reservation, baseline receipt
side ledger, or settlement pass runs. The exact legacy baseline gathering/production path remains active. Simulation
outcomes and material transfers are unchanged with the gate off. Adding the
serialized field legitimately changes the configuration/schema/build hash and therefore fingerprints even when false;
tests compare semantic outcomes rather than claiming byte-identical historical fingerprints. No compatibility hash
special case is introduced. The three tuning fields above receive their stated defaults in the matching
`NeolithicConfig` deserialization initializer when absent.

All choices use existing keyed or canonical ordering. Prepared eligibility and settlement reuse the sparse local source
topology/index while reading quantities from authoritative balances. Work is bounded by active camps, the small material
set, and their local extraction edges, plus the existing source-ranking cost; receipt work is linear in committed debits.
There is no added full-map or duration-proportional loop and no duplicate tile integration. Parallel planning remains
pure; authoritative source debits occur only in the canonical resolver.

## Required validation

- flag-off exact simulation outcome and material-transfer equivalence against the current branch, while accepting the
  declared schema/config/build fingerprint change;
- flag-on full-turn zero reservation/grant/receipt under current fallback commitments and complete physical parity with
  flag-off; this covers endpoint Wood/new-field-capital resolution without claiming a reachable positive raw request;
- synthetic-authority settlement-kernel tests for service fractions 0/fractional/1, shared-labour caps, explicit zero
  direct food/tool cost, source conservation/order, matching receipts and late-stock exclusion from same-turn production;
  synthetic allowance tests prove settlement behavior only, not reachable positive Prepare authorization;
- no request for unknown, undiscovered, inaccessible or exhausted sources, zero priority, or zero extraction rate;
- target-floor formula/default/bounds at small and large populations, fractional priorities, and invalid configuration;
- the conditional ceiling accounts for recipe, housing, and reserve-horizon targets without predicting endpoint Wood;
- a same-material fixture with both baseline and `elder-raw` receipts proves realised attribution includes only the
  matching request ID;
- same-turn field expansion changes Wood capacity, including a realised cultivation bridge that differs from the
  provisional preclaim, while baseline gathering still sees the single authoritative endpoint;
- scarcity fixtures prove Phase-A grants and staffing remain unchanged and Phase-B raw requests consume only genuinely
  unused prior-cohort intervals and residual source stock, never a Phase-A or fallback-gathering worker/interval;
- two camps sharing a source prove an earlier camp's optional raw authorization cannot reduce a later camp's baseline
  receipt;
- Gatherer and specialist reservations are unavailable respectively to passive food/baseline gathering and baseline
  extraction/recipes, with no return of unused Phase-B time;
- unlike physical material units aggregate through frozen worker-year service equivalents, and one service level scales
  every material request proportionally;
- service fractions zero, fractional, and one in synthetic-authority kernel fixtures prove `realised <= f * eligible
  deficit` as well as the authorization cap;
- an authorization never changes role, expands a frozen goods ceiling, or reuses consumed labour/source availability;
- production consumes baseline inputs before settlement while the raw deficit uses the captured pre-baseline ledger,
  and late raw stock cannot enable same-turn manufacturing;
- finite source debit equals camp credit and the realised diagnostic;
- Wood recovery/capacity loss, exhausted finite sources, shared aliases, and fractional-share conservation;
- deterministic camp/material/role/source tie handling, scalar/worker/reordered replay, and fork/restore continuation;
- unchanged integration counters and bounded active-state/mature-settlement performance;
- requested and accepted labels remain identical before and after resolution; only realised diagnostics complete later;
- old save defaults off and a new save round-trips the flag; and
- focused model9 step, fork, restore/replay, old schema-0 replay, flag-off-only schema-1 continuation and flag-on
  schema-1 rejection, and bridge tests prove aligned requested /
  compiled-granted / realised diagnostics without changing the actor tensor shape.
