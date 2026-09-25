# Neolithic performance assessment and proposed execution plan

Status: **Options 1 and 5 are implemented; checked replays match exactly, but the 20% throughput gate failed at both measured sizes.**
Status date: September 25, 2026.
Implementation: offshoot branch `codex/neolithic-options-1-5-validation`, simulation commit `b5d98d72288d78a45a83967c5370a1cec5451d82`.
Assessment baseline: `features/neolithic-agricultural`, `9dc18c05d3a1ae0a7d3462049dd34a9b7c41f07d`.

The user approved implementation of Options 1 and 5 on the offshoot branch. Their simulation changes are committed, and the 25-turn exact-replay and paired-timing evidence is reported in [NeolithicOptions15Validation.md](NeolithicOptions15Validation.md). Exact fingerprints match, but the 20% primary throughput target is missed at 80 and 320 tribes; the repeatable 320-turn-36 regression remains unresolved.

Because the measured work misses its throughput target, the analytical turn proposal in [NeolithicAnalyticalTurnPlan.md](NeolithicAnalyticalTurnPlan.md) is the retained follow-up candidate. It remains design-only; the approval for Options 1/5 does not authorize an analytical prototype or production solver. Any implementation or experimental validation requires separate approval.

The event-driven/regime-reuse approach was previously tried separately and reportedly increased runtime. That historical result is context for the analytical design, not an instruction to execute it.

The requested `git pull --ff-only` failed on a pre-existing broken Codex checkpoint reference. At the initial assessment, the GitHub branch API independently returned `9dc18c0`, matching the pre-change checkout. The experimental branch descends from that verified upstream tip; the pull itself did not succeed. No checkpoint references were repaired or deleted.

The supplied `neolithic_options_1_and_5_plan.md` was reviewed as a proposal, not executed as instructions. This assessment used the current source, existing performance JSONs, the pass contract and handoff, and three read-only Luna reviews. Pinned discussions consulted were **Agriculture - Neolithic Design**, **QoL**, and **RL - Neolithic**. Historical requests and assistant proposals in those discussions supplied design context, not new authorization. No fresh timing or correctness runs were performed for this assessment.

## Historical assessment recommendation (September 25, before Options 1/5 approval)

The initial assessment proposed prioritizing the [analytical turn approximation](NeolithicAnalyticalTurnPlan.md) and treating Options 1/5 as optional. The user subsequently authorized Options 1/5, so that assessment does not describe the current work sequence. Its feasibility analysis and proposed throughput targets remain historical reference; current validation evidence belongs in [NeolithicOptions15Validation.md](NeolithicOptions15Validation.md).

The proposal's exact implementation retains all seasonal intervals. It reduces repeated queries, scoring and grouping, not the number of ecological time steps. Actually reducing economic update frequency is a separate model change. The initial assessment treated contract reconsideration as a possible future direction; that observation does not authorize an analytical replacement, and it does not make a stale cache or unvalidated annual average correct.

## Feasibility findings

| Proposed change | Assessment | Required correction |
| --- | --- | --- |
| Cache camp footprints and gathering candidates | High feasibility | Cache post-movement geometry and static eligibility. Current field ownership, labor, operating fractions and available stocks remain live. |
| Cache hunter-to-food-camp routing | High feasibility | Resolve connectivity after movement/returns and scope it to resource resolution. Include distant connected fishing claims; radius-one land limits are not sea limits. |
| Reuse claim buckets and scratch arrays | High feasibility | Preserve tile order, stable entity/kind order, arithmetic accumulation order, receipt ownership and worker barriers. Size indexes to the active frontier, not every map tile. |
| Cache cultivation scores | High feasibility after correction | The shared remaining budget affects every candidate's proposed increment. Reuse a score only while that candidate's exact increment and other inputs remain unchanged. |
| Persist progressing-state caches across turns | Poor first choice | Ecology, ownership, tools, needs, movement and policy introduce broad invalidation. Use per-call/per-resource-pass caches with no new save fields. |
| Reduce 80 economic barriers to 40/20 uniformly | Behavior-changing and currently unsupported | Test an explicit alternative field model, not merely a lower loop count. Prior dry-site failures establish material risk, not a permanent prohibition on redesign. |

### Option 5: the proposed score cache is not exact as written

In `CultivationEconomy.Expand`, the proposed increment is bounded by:

`de = min(AllocationStepE, ceiling - e, remaining / buildCostPerE)`

A further cap applies at the bridge-intensity boundary. Both the estimated gain and the score denominator depend on `de`. Every winning step reduces `remaining`, so an untouched tile can become budget-limited and acquire a different score. A cached `Step`, `Cost` or `Valid` value can then select the wrong tile or spend the wrong amount of labor.

The safe fast path is small: at each iteration, visit the sorted candidates, calculate their current exact `de`, and reuse the expensive score only when the cached inputs still match. Invalidate the selected tile after its write. Recalculate other scores when their `de` changes, including the final partial-budget regime. Keep the configured allocation increment, bridge/ceiling tolerances, focus override, strict score comparison, pressure receipts, callback sequence and tool consumption unchanged.

Per-call projected views and four seasonal before-capacities can also be cached for unchanged candidates. Preserve the current floating-point operation order. Rebuild candidate eligibility at each invocation because another camp may have acquired shared fields. Use a small ordinary scratch array unless measurement justifies more elaborate storage.

The source creates **eight** `EcologyCapacity` values per candidate evaluation: before and after for four seasons. They are readonly structs, so that count is computational work, not evidence of eight heap allocations. Claims of an 85% allocation reduction or zero risk are not established.

Source: [CultivationEconomy.cs](../Assets/_Anthropocene/Simulation/CultivationEconomy.cs), especially lines 229-295; [LazyEcology.cs](../Assets/_Anthropocene/Simulation/LazyEcology.cs), lines 18-34.

### Option 1: cache membership, refresh progressing values

Build one transient resource-resolution context containing camp-ID-indexed sorted footprints, eligible gathering/land arrays, hunter food-camp links, the sorted active seasonal tile union, and reusable per-tile claim buffers. This is bookkeeping for the resolver; it must not expand what any policy can observe.

Continue to update ownership, capital, ecological condition, vegetation, labor, operation, remaining food/feedstock demand and tool budgets at their present boundaries. `AddAgriculturalClaims` currently calls its land candidate array `fields`; this is not permission to freeze authoritative field ownership for the whole generation. Its four allocations also have an intentional sequence: farmer plant inputs, gathering plant inputs, farmer biological food, gathering biological food.

Bucket contents and availability scratch values must be refreshed each segment. Preserve stable ordering for equal entity/kind keys, including biological and manufacturing claims. Workers may read private snapshots and fill indexed results; reuse buffers only after readers finish. Canonical receipts/history must own their values rather than retain references to scratch storage reused by a later segment.

Use the actual `LazyEcology.Segments` sequence, including partial first/last intervals; 80 is the default twenty-year case, not a hard-coded invariant. Keep independent tiles' existing full-turn path. They already traverse seasonal physics internally.

Source: [GenerationalEconomy.cs](../Assets/_Anthropocene/Simulation/GenerationalEconomy.cs), lines 231-417; [CultivationEconomy.cs](../Assets/_Anthropocene/Simulation/CultivationEconomy.cs), lines 66-121; [TurnResolutionPasses.md](TurnResolutionPasses.md).

## What the current gates actually establish

**Historical comparator behavior at the time of the September 25 assessment:** the then-current `Tools/PerformanceAudit/Compare.ps1` checked three aggregate outcome fields within 2%. With `-Main`, it additionally required an all-opening-turn median <=1.10 times main and each reported agricultural turn <=2 times its matching main turn. Without `-Main`, its PASS message made **no speed claim**; it did not test cross-version authoritative fingerprints or combine three repeated benchmark pairs. The audit harness/comparators were revised for this work. The actual commands, comparison mode, evidence, and outcomes are documented in [NeolithicOptions15Validation.md](NeolithicOptions15Validation.md).

The recorded latest Options 2-4 files and available main files give:

| Existing recorded opening series | 80 tribes | 320 tribes |
| --- | ---: | ---: |
| Completed turns | 10 | 11 |
| Options 2-4 all-turn median | 329.6 ms | 988.2 ms |
| Available main all-turn median | 200.4 ms | 799.2 ms |
| Ratio | 1.64x | 1.24x |
| Options 2-4 / main complete-opening time | 4.515 / 2.085 s | 15.562 / 9.271 s |
| Turns over the historical 2x ceiling | 7, 8, 9, 10 | 7, 9, 11 |

These are arithmetic summaries of existing files, **not newly measured paired results**. The files were produced hours apart and lack embedded source/runtime provenance. They cannot establish a causal regression or a reliable speedup from Options 2-4. They do show that the attachment's universal gate-pass forecast is unsupported.

Even mechanically subtracting the projected savings would leave failures. At 80 tribes, turn 9 would still be `(766.3 - 170) / 166.8 = 3.58x` main. At 320 tribes, turn 11 would still be `(3098.7 - 690) / 916.4 = 2.63x`. A saving measured on a late agricultural turn cannot simply be subtracted from the all-turn median.

The claimed 690 ms saving is also approximately the entire 677 ms preparation-plus-expansion cost in that recorded 320 turn-11 sample. GC interactions might affect other phases, but such savings are not demonstrated. Likewise, approximately 1.57 GB allocated over that whole turn does not establish that these two changes will remove more than 1 GB. Allocation is not retained memory, and removing temporary preparation objects does not eliminate authoritative history/receipts.

Evidence files, intentionally ignored: `TestResults/Optimized-Options234-{80,320}.json` and `TestResults/NeolithicHandoff-main-{80,320}.json`. Earlier measurements and their limits remain in [NeolithicTurnPerformanceHandoff.md](NeolithicTurnPerformanceHandoff.md).

### Historical proposed replacement target

The initial assessment proposed the complete Neolithic implementation at `9dc18c0` as the performance baseline, because the historical main revision lacks that economy. It also proposed keeping main `e472cf760232eaa614b387a0212146221f505cd9` pinned for the separate opening comparison. These are historical benchmark definitions; the actual paired baseline and comparison mode will be stated in the validation report.

For a fixed workload and benchmark pair, define:

`R = sum(candidate AI + preparation + resolution time) / sum(baseline AI + preparation + resolution time)`

Use the same predetermined turns, starting states and workload in each sum. Include all simulated tribes and live herds. Report milliseconds, total elapsed simulation work, and generations/second. Do not normalize away a slower world by giving it fewer surviving settlements or fewer completed turns. Setup, generation, saving and observation/render timing outside the decision path are reported separately.

**Proposed engineering success target:** median paired `R <= 0.80` across three sequential warmed pairs at each of 80 and 320 tribes: at least a 20% reduction in aggregate post-discovery time. Also target at least a 10% reduction in the median post-discovery turn time. These are proposed targets, not a promise that Options 1 and 5 achieve them. Do not pool the two sizes so one hides the other.

Report all pairs, matched per-turn medians, p95/worst turns, allocated bytes, GC collections and retained memory. Investigate any repeatable >5% regression in matched turn medians or the tail; an isolated noisy sample is not evidence of a regression. No improvement should depend on growing cache memory over successive turns. Keep preparation/integration/expansion counters in separate untimed diagnostic runs.

The initial assessment proposed treating the historical main-relative gate as a disclosed stretch comparison rather than a release blocker. That was a proposed validation-policy change, not an implementation result; the eventual report should state which gate was applied.

### Correctness gates proposed in the initial assessment

The initial assessment proposed exact authoritative fingerprints against the pinned pre-change implementation after every checked turn, plus scalar/two/four-worker, reversed-partition, reordered-collection and save-continuation equivalence. Same-version scalar/parallel agreement alone cannot detect a shared stale-cache bug. It also proposed validating the then-current Options 2-4 checkpoint against accepted prepared-turn behavior before relying on it as an oracle. The actual oracle and checks, including exact replay results, are documented in [NeolithicOptions15Validation.md](NeolithicOptions15Validation.md); this historical proposal is not itself evidence.

The old aggregate 2% comparison is insufficient for this work: compensating errors across villages can hide a local failure. Retain per-camp food, population, reserves, capital, labor, tools, goods, QoL and ecological balances for diagnostics, alongside exact replay.

For an explicitly changed economic scheduling model, cross-version bit equality can be waived; within-version determinism, conservation and save continuation cannot. Such an experiment must expose local/long-horizon differences and their causes. It cannot pass merely because population or food averages stay within 2%. Do not treat the historical prepared-turn change to prior-cohort QoL as a regression that should be restored.

## Cache validation sequence proposed in the initial assessment

This was the assessment's suggested order before Options 1/5 were approved. It is retained as validation-method context, not as a current implementation checklist. The simulation changes and measured results are documented in [NeolithicOptions15Validation.md](NeolithicOptions15Validation.md).

1. **Repair measurement coverage and capture a baseline.** Extend the audit to record source SHA, configuration hash, SDK/runtime/build, worker/partition settings, warmup/counter mode, pre-turn agricultural activity, requested/completed horizons and per-turn fingerprints. Reject mismatched metadata, missing/duplicate turns and unequal fixed horizons. Preserve the existing opening benchmark and add continuation beyond `WorldState.Finished`, which means opening completion, not the end of all Neolithic simulation. Validate the fixed-horizon continuation path before collecting timings. No production gameplay stop rule changes are required.
2. **Implement corrected Option 5 in isolation.** Add per-call caches keyed by the exact candidate state/step, with selected-tile invalidation and partial-budget recomputation. Differentially check its chosen tile sequence, consumed labor/tools, pressure records and endpoint state against the uncached allocator. Measure its contribution separately before combining changes.
3. **Implement Option 1 in two reviewable steps.** First hoist sorted membership and post-movement connectivity; then replace seasonal query/group allocation with reusable active-frontier buffers. Preserve amount recalculation, canonical arithmetic/order and all current write barriers. Verify each step against the oracle so a divergence can be localized.
4. **Run focused correctness and sustained workload comparisons.** Use the matrix below, followed by the three formal matched timing pairs at each size. Run one source build/process at a time, in the authoritative Windows checkout. Keep expensive detailed traces out of timed runs. Broaden testing only for changes or unresolved failures.
5. **Evaluate the aggregate target and remaining profile.** If it passes, document actual savings and limits. If it does not, retain worthwhile exact improvements and report the unmet target. Do not declare success by lowering the target afterward or claiming the main gate passed. Prepare a measured follow-on proposal only if remaining seasonal scheduling dominates.

### Compact workload and check matrix

| Purpose | Workload |
| --- | --- |
| Historical opening comparison | Standard / Continents / seed 1729, 80 and benchmark-only 320 tribes; natural completion, same matched turn numbers. Main remains an opening-only diagnostic. |
| Proposed Neolithic throughput | Same two generated worlds, retaining all tribes/herds and normal policy. Use baseline saves at opening completion and resolve exactly 25 additional generations (500 years) from identical saves. The first continuation generation already starts with the Neolithic cohort. Keep the startup/transition window as a separate report. |
| Long-horizon validity | Full fixed 25-generation live-policy continuation; record active camps, agricultural camps, workforce, active/dependent tiles and claims. Survival/workload shrinkage is an outcome, not a speed optimization. If the baseline produces too little ongoing agriculture, add a disclosed stable agricultural fixture rather than silently replace it. |
| Candidate-cache edges | Budgets below/at/above a full step, distinct build multipliers, bridge breakpoint, ceiling, focus/ties, vegetation/condition, shared footprints, tool exhaustion and partial covered work. |
| Seasonal-cache edges | Movement/connectivity changes, connected distant fishing, shared land/water claims, target completion, abandonment/ownership changes, empty claim buckets, partial initial/final seasons and save continuation. |
| Local economics | Existing 20 quick transition cases, especially fixed dry-marginal and placed-camp cases; verify a true 25 post-discovery-generation path on representative dry, productive and wooded agricultural sites. Disclose fixture controls. |
| Replay and integration | Relevant Neolithic/ecology/economy checks, fixed `Test-Headless.ps1 -Seeds 0`, and affected scalar/parallel/reordered/save checks extended through mature agriculture rather than ending at opening completion. Run the 80/320 scale replay once for final integration. |

Use the bundled .NET 8 SDK, Release mode, four workers and 64-item partitions for timed pairs; alternate baseline/candidate order across pairs. Use the same small-world warmup for both and a disclosed untimed agricultural warmup to cover the new hot path. Record ambient load; do not overlap benchmark processes or Unity workloads. The two scale cases remain distinct because both tribe count and map area change. Additional seeds/shapes are correctness generalization, not substitutes for repeated matched measurements.

Compile changed simulation code with Unity 6000.5.10f1 and run the relevant EditMode coverage using the documented build-recovery workflow. No presentation change is proposed, so a new art/UI playtest is not an acceptance requirement. Update `Docs/Performance.md`, the performance audit README and the handoff with measured results; update `Docs/TurnResolutionPasses.md` only for deliberately approved contract changes. Keep generated data under ignored `TestResults/` and source/documentation in the repository.

## Contingency design: analytical trajectory approximation

The philosophical boundary is the causal simulation: committed prior-world decisions; local, filtered agent knowledge; labor/tools/materials that actually pay for production; shared finite stocks; sustainable managed regeneration; conversion distinct from ecological damage; consumption then attainable reserves then growth; and births joining needs next turn. Prosperity and specialization should emerge from these constraints. No arbitrary population cap, free output, omniscient redistribution or disabled ecological pressure should be used to buy speed. Future trade, fission and RL remain out of this task's implementation scope.

The user reports that event-triggered operating-regime reuse was already implemented separately and made runtime worse. The initial assessment therefore explored a bounded analytical approximation of the coupled trajectory, preserving season order and integrated flows without progressing authoritative state through every season or year. Given the measured target miss, this is the retained follow-up candidate, pending separate approval before any implementation or experimental validation.

A concrete candidate carries cultivation capital, condition, stocks and cumulative flows as dynamic coordinates in a small first-order affine approximation. Construct four season-specific maps, compose their annual response and apply it over the generation algebraically. Freeze approximation coefficients, not the evolving state values. Analytical construction/upkeep curves and bulk field allocation must also remove the repeated economic work. A bounded predictor/corrector may improve the coefficients; no event scheduler or annual march is proposed.

This deliberately relaxes cross-version numerical equality while preserving determinism, conservation, prior-cohort needs and causal ecological constraints. Depletion, tool/target limits, shared scarcity, vegetation hysteresis and nonlinear pressure are the unresolved approximation risks. Validate intermediate curves and cumulative yield/work as well as endpoint state and 500-year live-policy behavior. The full derivation, limitations, proposed error screens and approval-stage prototype are in [NeolithicAnalyticalTurnPlan.md](NeolithicAnalyticalTurnPlan.md).

## Authorization boundary

Options 1 and 5 were approved and implemented in simulation commit `b5d98d72288d78a45a83967c5370a1cec5451d82`. Their exact replay and paired 25-turn timing results are reported in [NeolithicOptions15Validation.md](NeolithicOptions15Validation.md): the 20% primary target failed at both sizes, and the 320-turn-36 regression remains unresolved. The analytical plan is the retained follow-up candidate, but remains design-only; no prototype, solver, or analytical benchmark has been authorized or executed.
