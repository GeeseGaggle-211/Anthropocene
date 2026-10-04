# Anthropocene — Economic Control Contract v0.1

**Status:** Initial design contract; not an implementation or a calibration acceptance.

**Addendum A (4 October 2026):** [EconomicControlContractAddendumA.md](EconomicControlContractAddendumA.md) reconciles §12 (trade), commitment priority and phase R planning with the trade, storage and valuation work implemented after the reference revision. Where they differ, the addendum governs.

**Reference:** `features/demographic-structure`, commit `6a802dcf8da68aec9f690405a1b223c5add2249b`, inspected 3 October 2026.

**Suggested repository location:** `Docs/EconomicControlContract.md`.

**Scope:** Replace automatic economic allocation behind the existing action/preparation interfaces. Preserve physical production, ecology, demographic chronology, immigration and fission. Trade integration is reserved against the existing design, not represented as an implemented resolver.

“MUST” is an implementation requirement. “SHOULD” permits a documented exception. Component names below are proposed unless identified as existing code.

## 1. Architectural decision

The settlement economy SHALL use three decision layers:

1. **Demand controllers:** independently project a service requirement over the prepared turn: food, clothing, shelter, storage and productive-capital service.
2. **Dependency/feasibility layer and Elder allocator:** translate requirements into complete production bundles; arbitrate shared worker-time, inputs, equipment, storage and source capacity.
3. **Elder strategy:** adjust operating targets and authorize bounded investments in response to persistent deficits, surplus and external opportunities.

The resolver remains the only authority that produces goods, consumes resources or changes population. A request is not an allocation; an allocation is not a receipt.

The local controllers are **single-service demand models**, not physically independent SISO plants. Shelter may require Builders, Weaver work, tools, animal feedstock and extraction. Independence means that a controller cannot inspect or change another controller's policy or allocation. Coupling is explicit in recipes and the shared resource ledger—not hidden in controller-to-controller calls.

Population and growth are consequences, not the allocator's universal objective. The allocator regulates agreed operating conditions; it does not maximize births or search over possible immigration/fission histories.

The term **Elder** denotes a policy/arbitration role. It applies to mobile camps as well as villages and does not require implementing a new person, institution or RL agent.

## 2. Existing-code boundary

| Existing implementation | Contract treatment |
|---|---|
| `TurnPlanning.cs`: `TurnPlanner.Prepare`, `AutomaticIntent`, `PreparedTurn`, `TurnNeeds` | Keep the preparation boundary and accepted-placement behavior. Add allocation-strategy dispatch and carry the resulting production envelope through preparation. |
| `EconomicIntents.cs`: `EconomicIntent`, validation, application and automation controls | Preserve the public action contract, manual orders and automation semantics. Compile controller decisions into legal intents. |
| `ProductionDemand.cs`: `CampProductionDemand.Build` | Extract physical demand/capability projections. Do not call this entire method as the new demand engine: it also constructs Value claims, applies valuations and conditionally builds herd/expansion ladders. |
| `NeolithicProduction.cs`: recipes, demand, tool budgets, production and needs | Reuse the authoritative recipes and production rules. Separate assignment-dependent demand probes from assignment-independent service demand. |
| `MaterialEconomy`, `HousingMaterialPlan`, `ToolCoverageBudget` | Reuse physical source access, substitution and equipment accounting. Supply an explicit compatible order rather than reconstructing Value priorities. |
| `CultivationEconomy`, `HerdingTooling`, `FoodHorizonForecast` | Reuse physical work/yield/support calculations. Do not substitute constant food-per-worker estimates where the actual response is bounded or nonlinear. |
| `SpecialistProductivity` | Use the same role/capability/population-dependent rates in forecasts and resolution. Add no new bonus. |
| `EconomicPlanner`, `ValueFulfilment`, `SharedAllocation` | Retain as the legacy/reference strategy. Their joint Value search is not part of the new allocator. |
| `QoLMortality`, `Fission`, `Immigration` | Preserve formulas, flags, decision rules and resolution ordering. Add only integration hooks and diagnostics described here. |

Two current dependencies need particular care. `NeolithicProduction.Demand` makes some Animal Fabric demand conditional on an assigned Builder/Craftsperson; `CampProductionDemand.Build` currently inserts a prospective worker to expose that demand. The replacement MUST derive tent demand from shelter requirements first, then derive its fabric and labor dependencies—without fictitious staffing. Also, `TurnPlanner.ComputeNeeds` presently calculates `TurnNeeds.Production` through a production forecast. The new path MUST carry allocated output limits into that calculation so it cannot silently re-expand work that the Elder declined. [S2, S3, S6]

## 3. Unchanged invariants

The new path MUST preserve typed Plant/Animal/Aquatic Food; finite ecological receipts; real tool wear; specialist technology eligibility; independent fixed/tent housing parameters; and existing configuration/migration conventions.

Every input unit, tool-service unit and unit of worker-time may be reserved once. Shared upstream production may serve several demands, but it must be attributed and debited once. Physical inputs can remain in inventory if an expected downstream operation fails; they cannot disappear or be credited as finished shelter.

The workforce comes from the current prepared cohort. Newborns, later immigrants and fission daughters cannot work retroactively. Resident workforce, connected food consumers and linked population MUST remain distinct; accepted dispatch, return and placement handling must match `TurnPlanner.ComputeNeeds`.

Forecasts MUST be pure. They may read committed state and prewarmed local indexes, but may not materialize ecology, debit inventory, create people, discover private foreign stocks, or update Elder memory. Draft previews and repeated UI queries cannot change future decisions.

A physical shortfall MUST remain visible. More demand cannot create water, reachable deposits, tools, skilled capability, workers or animals. Full target satisfaction is conditional on feasibility, not promised by the controller.

No ecological, recipe, QoL, migration or fission constant is retuned in this refactor. Preserve incidental effects already produced by legitimate actions, including gathering practice and gatherer-bridge cultivation; “no discretionary investment” does not mean disabling those resolver effects.

## 4. Temporal contract

The replacement SHALL follow the actual current chronology, not a reordered economy diagram. [S2–S4]

| Boundary | Required behavior |
|---|---|
| Preparation | Validate orders, resolve accepted destinations, build a private settlement snapshot, select an automatic allocation, then freeze `TurnNeeds` and production order/envelopes. |
| Movement/resource resolution | Apply the accepted action/economy settings and existing movement/provisioning rules; resolve finite resource receipts and managed herds. |
| Growth planning and consumption | Use actual receipts and existing prepared food/feedstock targets. Preserve consumption, clothing-feedstock, reserve and growth accounting. |
| Goods/needs resolution | Produce through existing material/tool/recipe paths; compute the cohort's actual needs coverage and QoL; apply storage. |
| Demographic commit | Commit pending losses and births after the prior cohort has finished its work. Preserve existing growth-ceiling timing. |
| Close phase | Run existing progression, immigration, then fission with immigration's exclusion set; finish knowledge/history through the current path. |

In particular, current goods production does not retroactively set the growth ceiling already used for the same turn. `QoLMortality` continues to read the committed QoL specified by the present demographic contract.

Use `TurnHorizon` for duration. Express internal work in **worker-years**, rates in compatible physical-time units, and deadlines as phase/time availability. Do not add seasonal/yearly simulation loops inside a turn or assume every decision remains twenty years.

## 5. Input and output contracts

These are logical contracts, not required class layouts.

**`SettlementControlSnapshot`** contains the world/turn/configuration identity; accepted tile and abandonment status; prepared resident/consumer counts; typed inventories; projected retained stocks; field/herd state; reachable source limits; eligible recipes/roles; actual equipment-service budgets; current policy/directives; and optional confirmed external commitments.

**`ServiceDemand`** contains:

| Field group | Required meaning |
|---|---|
| Identity | Stable request ID, service ID, originating controller and internal/external origin. |
| Target | Current cohort/use, projected condition without action, requested service quantity and measurement phase. |
| Bands | Disjoint minimum-service, recurring replacement, deficit-repair and discretionary increments. These increments must sum to the request, not overlap. |
| Policy | Priority class, weight, permitted substitution and optional deadline. |
| Explanation | Existing stock credited, projected loss/use, unmet-service history and cause. |

**`ProductionBundle`** attaches selected recipes, intermediate goods, input quantities by type, work by role, tool service, availability phases, and the attainable service quantity. It distinguishes direct craft work from complete upstream work. Inputs supporting a high-priority service inherit that service's priority.

**`AllocationResult`** contains a validated `EconomicIntent`; explicit per-good production ceilings/order; input/equipment reservations; field/herd work reservations; allocated service by request; and an unmet-demand ledger. It is transient. Merely assigning a worker does not authorize producing beyond the allocated useful output.

**`UnmetDemand`** separates labor rationing, material shortage, ecological capacity, equipment shortage, missing capability, lateness, integer staffing, deliberate policy refusal and delivery/forecast error. It records requested, granted, predicted and realized quantities separately.

## 6. Demand-controller law

For a conventional stock service, the feed-forward request is conceptually:

\[
q_i = \max(0,\ U_i + S_i^{target} + X_i - \widehat S_i^{retained} - I_i^{eligible}).
\]

Here `U` is planned use during the horizon, `S_target` is desired closing stock, `X` is an authorized outbound commitment, and `I_eligible` is inbound supply available before the relevant use. Retained opening stock accounts for applicable decay exactly once. A controller with phase-sensitive supply MUST preserve those phases rather than netting everything at the endpoint.

For a selected uncoupled recipe, output divided by effective annual productivity gives direct worker-years. This is only a diagnostic starting point: the complete work requirement includes dependencies, role eligibility, input limits and equipment service.

The Food controller MUST use the existing food-bucket and growth/feedstock helpers instead of treating food like an ordinary stock. A water shortage is a site/support constraint unless the engine actually provides a labor-controlled water action; do not invent a Water Worker role to make the controller diagram symmetrical. Clothing consumption, tent feedstock, protected reserves and births cannot spend the same food. Requests for housing and clothing use the prepared resident cohort; extra provision for anticipated later population is separately authorized investment, never phantom current demand.

The initial reference policy SHOULD retain current useful-demand targets, including full basic coverage where requested and existing reserve durations. It MUST not introduce arbitrary 95–100% target bands as hidden balance changes. Optional service bands are explicit policy parameters tested separately.

Known predictable decay produces replacement demand even while the **opening** stock is full. Demand is measured at the phase where service is evaluated, not merely at opening.

### Controller responsibilities

| Controller | Owns | Does not own |
|---|---|---|
| Food/security | Current meals, existing reserve requirements, authorized growth-food envelope and accepted typed feedstock requirements | Future births as labor; unrestricted exports; every local investment decision |
| Clothing | One combined clothing service, current consumption and the selected buffer | Separate full clothing targets for both Cloth and Animal Fabric |
| Shelter | Unhoused people, fixed/tent replacement and permitted shelter form | A free claim on fabric already allocated to clothing |
| Field service | Work to operate the supervisor-selected existing fields | Automatic intensification using every spare resident |
| Herd service | Current management/support requirements for the selected herd policy | Free newborn production, infinite forage or early slaughter receipts |
| Storage | Storage-service gap using actual Pots/natural-storage rules | Crediting Pots before they exist |
| Equipment/material support | Dependencies and explicitly authorized buffers | Independent duplication of tools/materials already charged upstream |

A single shelter service may expose legal fixed and portable routes, but neither may independently claim to house the whole population. The initial route selector uses the supervisor's retained operating policy and a deterministic, bounded fallback order—not a Cartesian search over all goods.

## 7. Dependency and resource reconciliation

The Elder arbitrates **complete service bundles**, not isolated final-good headcounts.

For fixed Housing, the dependency is raw Wood/Clay or stored Housing Material → Housing Material → Housing assembly. Both stages share Builder/Craftsperson time; only the existing equipment-using stages incur their specified wear. For tents, the chain is Animal Food → Animal Fabric → Light Housing. Clothing and tents compete for the same Animal Fabric. These are current recipes, not new resource systems. [S5]

The shared layer MUST perform backward input-demand propagation and forward production feasibility over a merged recipe/stage graph. Existing intermediate inventory and eligible deliveries are credited once. A missing tool input has the urgency of the final good it enables; it is not postponed merely because a generic Tool controller has a lower rank.

Preserve the existing same-turn toolmaking/production rules, Housing Material/assembly sequencing, and the fact that new Farm/Herding Tools cannot backdate service already needed earlier in resolution. Break causal cycles using availability phases and existing bootstrap rules, not repeated calls until a numerical fixed point appears.

Specialist routes use `SpecialistProductivity`; generalist routes remain legal wherever current recipes permit. Material abundance removes an availability constraint, not the worker cost of extraction or equipment manufacture.

Agricultural harvest and upkeep retain their existing overlap. Where the current model shares operating farmer work, charge the correct maximum/overlap, not the unconditional sum. Expansion time is separate. Likewise, assigned gatherer time cannot simultaneously provide full food gathering and full extraction.

Human harvest and herd forage MUST share the same finite ecological envelope. No separate controllers may independently sell the same biomass. Current domestic animals still incur the existing feed/water chronology; a lower end-of-turn herd target does not erase their earlier grazing demand.

## 8. Elder arbitration

When every request fits worker-time **and all other resources**, grant it. The Elder need not rank things merely to imitate the previous optimizer. Arbitration is required whenever any shared constraint binds, even with unemployed residents.

The initial policy has four classes, independent of subsistence label:

| Class | Treatment |
|---|---|
| Survival | Current consumption and feasible dependencies necessary to deliver it. Infeasible survival produces explicit rationing/distress, never invented supply. |
| Operating continuity | Basic clothing/shelter service and replacement; upkeep of productive assets the supervisor has chosen to retain; necessary equipment/input chains. |
| Recovery and commitments | Remaining service deficits, authorized reserve/buffer recovery and admitted external obligations, with explicit policy precedence. |
| Development | Authorized field/herd/equipment expansion, speculative exports and optional additional provision. |

This is an allocation policy, not a replacement for the resolver's biological food order. A contract cannot authorize consuming protected survival inventory. Existing capital is not an eternal maintenance mandate: the supervisor can reduce its operating target or abandon it, and actual decay follows normally.

Within a class, use **capped weighted fulfillment of requested service**. Scale normalized fulfillment fractions, not an uncapped `available_workers × deficit_weight` formula. No bundle receives more than its useful target. When an input saturates, freeze affected requests and let unrelated feasible requests use remaining capacity.

For v0.1, select bounded production routes first, perform one dependency/resource allocation, compile staffing, and permit at most **one** deterministic repair pass. The repair may reduce infeasible outputs and redeploy released resources in the existing order; it cannot choose new strategic modes or restart the Value search. Remaining slack or deficits are reported. This is a bounded heuristic, not a global optimality claim.

Integer staffing is compiled **after** work for the same role is aggregated. The two housing stages therefore share one Builder pool. Per-role work cannot exceed assigned residents times horizon years, and total assigned headcount cannot exceed available residents. If rounding makes the allocation infeasible, remove work from the lowest-priority affected bundles in stable order and rerun the bounded physical check. Do not secretly introduce fractional multi-role workers to fix the budget.

Unassigned residents retain the engine's current gatherer/manual semantics. Spare labor does not itself authorize new fields, new herd commitments or an export promise.

## 9. Housing reference case

The branch report describes the Value planner's repeated 90%/100% Housing cycle. That is a regression fixture, not evidence that every possible cycle is a bug. [S11]

With 480 residents, four people per fixed Housing unit and 10% decay over the reference turn:

- Opening shelter: 120 units.
- Projected retained shelter: 108 units.
- Required recurring replacement: 12 units.

At the unscaled specialist/hand-tool baseline, 2.5 complete Housing units per Builder-reference-turn require 4.8 Builder-equivalents, compiled to five assigned Builders. The request also requires 12 Housing Material, its net raw-material inputs and the actual assembly equipment budget. Output is capped at the 12-unit target.

With the same next-turn population and adequate resources, the next request is again replacement—not a fresh decision whether housing has enough marginal value. If resources prevent repair, the allocator records the explicit sacrifice and the resolver produces the actual lower coverage.

This does not guarantee smooth QoL under scarcity, integer effects or demographic changes. It removes the specific unpriced/skipped-replacement behavior when the complete maintenance bundle is feasible.

## 10. Immigration integration

`Immigration.Resolve` is currently a population process, not an Elder action. It evaluates known local destinations from a frozen post-demography snapshot, uses `Fission.QoLModel.ForwardAt`, includes food/coverage dilution and return hysteresis, and reconciles accepted groups canonically. Keep that methodology unchanged. [S7]

The allocator MUST NOT run inside destination evaluation, replace `ForwardAt` with a full production plan, reroute rejected migrants, recompute decisions after every transfer, or promise future specialist output to improve destination acceptance.

Migrants continue to transfer people and proportional typed food through the existing commit. They do not acquire a new right to take tools, manufactured goods, fixed housing, herds or the source's control state. Source workforce reductions and destination population increments remain current resolver behavior.

At the next preparation, each affected settlement recalculates actual consumer counts, shelter, clothing, food and worker capacity. Arrivals are not workers in the turn that has already resolved; departures do not retroactively reduce its labor or consumption.

Preserve the existing fission exclusion precisely: **sources with a migration intent**, including a subsequently rejected intent, and destinations receiving arrivals sit out fission as currently implemented. Do not narrow this to successful emigrants only.

## 11. Fission integration

Fission remains a bounded local process after immigration. Keep its crowding/distress distinction, policy-sized daughter logic, relative QoL-gain thresholds, viability search, remembered destination, spacing checks, daughter/stayer vetoes and canonical commit. Its estimates use the physical QoL model, not the Elder's desired coverage. [S8–S9]

The Elder may set the already supported fission policy through its existing legal interface. It cannot directly split a camp, veto an otherwise valid split through hidden economics, inject daughter resources or fabricate good coverage.

Preserve current inheritance: the parent retains its site, fields and fixed Housing; the daughter receives its conserved share of portable inventories, typed food, whole animals and fractional ledgers under existing rules. Society knowledge is copied. `SplitWorkforce` still turns the daughter's share of Farmers into Gatherers. Daughter `FissionPolicy` starts Neutral, automation continues, and its initial QoL remains the founding estimate—not 1.0. [S9]

`Fission.DaughterShelter` MUST stay a cheap pure estimator. It can reuse production-rate/input helpers, including the daughter-sized specialist multiplier, but must not call the complete Elder allocator per candidate tile. Its current carried-input/Builder-share approximations remain disclosed rather than silently upgraded into guarantees. [S8]

New controller policy/settings may inherit explicitly; transient plans, asset reservations and absolute unmet-quantity backlogs MUST NOT be copied. A daughter starts with fresh request diagnostics derived from its actual transferred assets. The parent replans from its retained assets. Both act next turn, not during the split.

No contractual output or export commitment may be duplicated by fission. Until trade succession is separately specified, future camp-bound contracts stay with their named counterparty; inability to fulfill them is a shortfall, not an automatic transfer to the daughter.

## 12. Trade/external demand boundary

At the reference revision, trade is a design contract rather than an implemented settlement exchange step. Provide an empty external-commitment adapter in v0.1. Preserve the proposed local bounded REQ/ACK/CLOSE methodology and protected food bands without claiming its value-based pricing is already replaced. [S10]

External requests have three distinct states: **opportunity**, **admitted commitment**, and **realized delivery**. An opportunity is strategic input only. A commitment reserves named output/input capacity once. A delivery becomes real stock only through an authoritative exchange receipt.

An import reduces a local request only if it is eligible before that request's consumption/production phase. Confirmation alone is not physical food: no same-turn births or manufacturing may consume an unrealized promise. Same-turn food-backed commitments require an implemented escrow/settlement-and-shortfall mechanism consistent with growth planning; that mechanism is outside this allocator refactor.

Offer evaluation may use a bounded work/capacity quote. It MUST NOT recursively replan counterparties or negotiate until a global fixed point. Repeated same-turn replanning is not permitted to multiply commitments or free resources. The later trade phase must define the limited preparation refresh explicitly.

The existing design's use of a Value-planner labor price needs a separate trade-valuation adapter. Do not keep the old joint allocation search alive solely to manufacture that price.

## 13. Feedback memory and strategic control

v0.1 demand is predominantly feed-forward. After resolution, compare the prepared forecast with actual service receipts and record the cause of unmet demand. Do not accumulate yesterday's physical shortage again: today's inventory already contains it.

Optional persistence uses a bounded dimensionless signal, for example

\[
z_{i,t+1}=e^{-T/\tau_i}z_{i,t}+(1-e^{-T/\tau_i})e_{i,t},\qquad 0\le e_i,z_i\le1.
\]

This is history for policy, not another stock or unpaid production bill. Separate labor denial from physical impossibility. Persistent unavailable clay should trigger substitution/import/resource strategy, not ever more workers assigned to a dry deposit. Priority escalation is capped within policy limits and cannot defeat survival protections.

Capture economic outcomes against the prepared cohort before immigration/fission changes that cohort. Commit memory once from those actual outcomes, never on preview; the final diagnostic record must distinguish service during production from population after social transfers. Immigration does not transfer it. At fission, reset daughter history; retain the parent's bounded history but invalidate asset/cohort-specific plans. Recompute quantitative demand from current state next preparation.

The strategic layer reads these signals and may authorize operating-target changes, bounded field/herd investments, stock buffers, skill-development goals, trade offers or legal fission-policy changes. Current technology still unlocks through existing practice rules. Strategy uses physical-time review periods and event triggers, evaluated at most once per boundary—not a loop over skipped years.

No strategic decision searches the product of goods, field intensities, herd sizes, counterparties, future migrations and future daughters. v0.1 may use fixed/manual directives; implementing a sophisticated Elder is not a prerequisite for validating the allocator.

## 14. Determinism and performance contract

Use the same discipline as immigration/fission: **freeze → local proposal → canonical reconciliation → authoritative commit**.

Build local indexes once, cache boundary-stable physical projections, and merge shared recipe dependencies. Sorting uses stable service/recipe/role/source IDs. Each controller receives the same immutable snapshot. Parallel evaluation may not change reservations, tie-breaking or receipts.

Controller work should scale with active services and the local recipe graph. Source queries scale with accessible local source edges and owned herd groups. Allocation/reconciliation must have a documented input-size bound and the explicit one-repair limit. No loop may scale with individual residents, animal heads, horizon/base-period count, all remote camps, or a Cartesian portfolio of subsystem choices. Existing social-system bounded searches are not multiplied by allocator calls.

Do not claim constant total simulation cost: more camps, recipes and reachable sources still create work. Record controller evaluations, dependency edges, physical forecasts, source queries, repair passes, integer-rounding losses and allocated bytes per camp-turn. Compare identical snapshots as well as evolving worlds, so demographic expansion cannot disguise per-camp cost.

This design does not prove closed-loop stability. Test disturbances, saturation, delayed supply and mobility explicitly; deadbands or smoothing must not hide actual QoL used by immigration/fission.

## 15. Integration and compatibility

Introduce an explicit allocation-strategy selector, logically `Legacy | DemandControl`, independent of `PlannerFulfilment`. Absent configuration selects Legacy. Existing Value/Uniform/shared-allocation flags continue to govern the legacy path exactly as before.

The controlled path MUST not accidentally switch physical field-work overlap/expansion semantics because those semantics are currently entangled with shared-planner flags. Extract the necessary physical policy adapter and test it without enabling Value search.

Route `EconomicIntents.Automatic` and `TurnPlanner.AutomaticIntent` through one strategy-dispatch boundary. Extract a complete pure settlement preview rather than assuming `EconomicPlanner.Preview` copies every field a controller needs. Audit planning/draft callers so no automatic Value allocation is reconstructed later.

Carry allocated production ceilings and reservations through `AutomaticPlan`/`PreparedTurn`/`TurnNeeds`. `MaterialEconomy.Forecast` may reduce a physically infeasible plan but may not replace Elder decisions with a fresh useful-demand allocation. Generalize production-order construction so the allocator and recipe resolver honor the same service priority and prerequisites.

Manual economy orders retain precedence, and manual workforce edits/automation countdowns retain current behavior. Draft reuse must be invalidated by every relevant change: boundary, placement, allocation mode, policy/directives, memory version, input state and future external commitments. A draft remains disposable and advisory.

No repository-wide rewrite is authorized by this document. The first slice is food, clothing, fixed housing, equipment and extraction with fixed strategic directives. Add tent/herd and field-operation integration next; then exercise existing immigration and fission. Keep trade behind its empty adapter until its settlement contract exists.

## 16. Acceptance tests and evidence

| Test | Required result |
|---|---|
| Predictable housing decay | With constant cohort and feasible complete inputs/work, recurring replacement is requested and delivered; the documented avoidable skip/rebuild cycle is absent. |
| Cross-good dependencies | Clothing/tents share fabric correctly; Housing shares material/assembly labor; high-priority dependencies are not blocked by their own lower standalone priority. |
| Saturated budgets | Worker, source, input and equipment limits hold; lost fulfillment is attributed. No unbounded retries or integrator windup. |
| Productivity perturbation | At fixed state, policy and requested bundle, higher eligible productivity cannot increase required work. No claim of monotonic fifty-turn total population is imposed. |
| Forecast chronology | No newborn/immigrant labor, no early Farm/Herding Tool service, no edible/grazing double count, no double depreciation or feedstock credit. |
| Immigration | Existing utility, hysteresis, canonical reconciliation, no-rerouting and fission-exclusion behavior remain intact; next-turn demand reflects actual arrivals/departures. |
| Fission | People/assets/knowledge follow current inheritance, fixed capital stays put, daughters bootstrap without copied reservations, and default founding QoL/policy behavior is preserved. |
| External adapter | Empty adapter leaves behavior unchanged; late, failed, duplicated or merely proposed deliveries cannot create usable supply. |
| Determinism/compatibility | Scalar/parallel/reordered/save-load agreement for the new path; legacy-mode continuation unchanged; previews are pure. |
| Performance | Input-size scaling, counters and memory costs are measured; no new per-person, per-year or per-social-destination allocator loop. |

Reuse `Tools/DemographicStructure` as the regression harness, but disclose that its existing experiments run continuous Value automation with controlled abundant resources. Do not call them planner-free, or call a no-movement-AI fixture a measured nomadic movement simulation. [S11]

Run matched controlled cases first, then 50-turn paired lineages with fission/immigration toggled independently. Record per-turn causal ledgers and report aggregate population, per-tribe quantiles, time-weighted QoL, housing/clothing oscillation amplitude, food provenance, growth suppression, migration, fission **per camp-time/person-time**, and daughter survival. Compare both equal states and realistic resource-constrained starts.

Mechanism acceptance requires coherent demand, feasible allocation, explicit scarcity and bounded computation. Desired agricultural/pastoral population ordering remains a separate calibration hypothesis. Do not change physical constants or force low pastoral QoL merely to make a chart match it.

## Source map

All paths below are relative to `Assets/_Anthropocene/Simulation/` unless prefixed with `Docs/`; all refer to the pinned revision above. Source line spans describe the inspected reference, not promised future locations.

- **S1:** Branch metadata: `features/demographic-structure` resolves to `6a802dcf8da68aec9f690405a1b223c5add2249b`.
- **S2:** `TurnPlanning.cs`, lines 1–260: preparation, accepted placement, drafts, automatic intents, `PreparedTurn` and `TurnNeeds`.
- **S3:** `TurnPlanning.cs`, lines 330–end: production forecast, typed feedstock and planning snapshots.
- **S4:** `TurnResolver.cs`, lines 55–140, and `GenerationalEconomy.cs`, lines 337–407: authoritative chronology and immigration-before-fission.
- **S5:** `NeolithicProduction.cs`, lines 243–271: recipes and housing material/assembly dependency.
- **S6:** `NeolithicProduction.cs`, lines 377–457, and `ProductionDemand.cs`, lines 355–455: useful demand, assignment-dependent probes and physical/value coupling.
- **S7:** `Immigration.cs`, lines 1–260: snapshot/forward utility, canonical reconciliation, conserved transfers, return memory and exclusion set.
- **S8:** `Fission.cs`, lines 124–218: split QoL and daughter shelter estimate.
- **S9:** `Fission.cs`, lines 529–end: support limits, inheritance, founding QoL, policy and workforce transfer.
- **S10:** `Docs/QoLFissionTradeDesign.md`, lines 464–620: bounded trade handshake, protected food and timing proposal.
- **S11:** `Docs/DemographicStructureCalibration.md`, controlled-experiment section and lines 110–178: the controlled Value-planner experiment, oscillation diagnosis and rejected route calibration.
