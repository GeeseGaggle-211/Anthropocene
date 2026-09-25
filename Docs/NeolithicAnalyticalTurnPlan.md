# Analytical Neolithic turn approximation: revised proposal

Historical design assessment from September 25, 2026. The subsequent user
request authorized the turn-horizon implementation on
`codex/turn-horizon-refactor`. See [TurnHorizonRefactor.md](TurnHorizonRefactor.md)
for the implemented equations and measured outcome/performance evidence.
The authorization statements below describe the earlier decision point and
do not limit this later request.

Status at the time of this assessment: **design-only**.
Assessment date: September 25, 2026. Source inspected: `9dc18c0`.

Current authorization update: Options 1 and 5 were approved and implemented on offshoot branch `codex/neolithic-options-1-5-validation`, simulation commit `b5d98d72288d78a45a83967c5370a1cec5451d82`. The reported exact replays match, but the 20% throughput target failed at both measured sizes and the 320-turn-36 regression remains unresolved; see [NeolithicOptions15Validation.md](NeolithicOptions15Validation.md). This analytical design is the retained follow-up candidate. It remains design-only: separate approval is required before any prototype, solver, test, or benchmark, and approval of Options 1/5 did not authorize those activities.

The user reports that a separate event-driven/regime-reuse attempt increased runtime. That approach is not recommended. This document preserves the resulting analytical design assessment as contingency material. It does not direct current implementation work. Exact query/score caching is part of the separately authorized Options 1/5 work, not a prerequisite for this design.

## Assessment

This is a plausible research direction. It requires approximating the evolution of field investment, maintenance, condition and claims, not just using an analytical formula for stock recovery. `EcologyMath.Stock`, `ManagedStock` and `Condition` already solve exponential equations inside each season. `CultivationEconomy.Decay` is already exponential. Their changing inputs and the repeated claim/labor/expansion passes cause the temporal execution cost.

I interpret **first-order approximation** as retaining first-order dependence between changing state variables while discarding higher-order nonlinear interactions. It does not mean a single Euler step `x(T) = x(0) + T*f(x(0))`. Retaining exponential/affine response curves should preserve approach to equilibrium and integrated production much better than a straight-line endpoint projection. Accuracy over twenty years is not guaranteed by the words "first order"; it must be measured on the actual nonlinear trajectories.

The intended work is bounded by active tiles, local claimant relationships and a small fixed number of coefficient evaluations, rather than eighty advances of the simulated world. A loop through twenty annual states would still be progressive integration and is not the proposed solution.

## 1. Approximate the transition law, not the state as a constant

Conceptually, write the present smooth-branch seasonal transition as:

`x_next = F_s(x, locked orders and needs)`

For season `s`, approximate that transition around a representative state:

`F_s(x) ≈ F_s(x_ref) + A_s * (x - x_ref) = A_s*x + b_s`

`A_s` is the derivative of the selected smooth transition model. In a reduced implementation its entries should come from the known equations; do not construct it by repeatedly executing the whole seasonal resolver for numerical derivatives.

The state must include the quantities whose progression matters: accessible food F, nonfood biomass N=B-F, freshwater W, cultivation capital E, condition C, and relevant cumulative harvest/work outputs. Aquatic condition and Wood need their corresponding source-specific treatment. Condition/capital values are **dynamic coordinates**; only the local approximation coefficients are fixed. Freezing the initial food capacity or initial q for twenty years would miss the very trajectory being preserved.

An augmented affine map carries both state and accumulators:

```text
[ x_next ]   [ A_s   0   b_s ] [ x ]
[ H_next ] = [ c_s^T 1   d_s ] [ H ]
[   1    ]   [  0    0    1  ] [ 1 ]
```

Here H stands for integrated realized harvest; multiple accumulator coordinates can track biomass/water harvest, recharge, overflow, labor, covered tool work and pressure. This representation is exact for an affine surrogate on its assumed branch. It is an approximation of the original nonlinear/clipped simulation. Depletion-dependent realized harvest is not generally affine, so its accumulator coefficients also require a branch-aware approximation.

Compose the four distinct seasonal maps in their actual order:

`M_year = M_3 * M_2 * M_1 * M_0`

For a year-aligned twenty-year turn beginning with season 0:

`z_end ≈ M_year^20 * z_start`

Use scalar geometric sums, specialized small-block algebra, or repeated squaring. Do not execute twenty annual resolver updates. A requested intermediate point can be evaluated from the annual power plus the appropriate partial-year/season map, without using the previously sampled point as input. Handle nondefault durations and starting phases with bounded prefix/suffix composition.

This retains season order. Multiplying the mean renewal rate by the mean capacity is not generally equivalent to composing the four seasonal responses. The existing `SeasonalAffine` quiet-tile path demonstrates an exact scalar subcase of this approach; it does not prove the active coupled extension.

The runtime design must not be a dense world-sized matrix. Derive small tile response blocks and camp-level analytical driver curves, coupled through the existing sparse camp/tile claim relationships. Use at most one fixed whole-turn correction of those drivers before committing any authoritative results. Determine which couplings this approximation omits and measure their effects explicitly. A connected map can contain large shared-resource components; do not assume every village is independent.

## 2. Give field capital an analytical trajectory

One illustrative continuous approximation to construction plus abandonment is:

`dE/dt = u - k*E`

`E(t) = E_eq + (E_0 - E_eq)*exp(-k*t)`, with `E_eq = u/k`.

For `k = 0`, use `E(t) = E_0 + u*t`. Time is in years here. Existing decay gives `k = ln(2)/DecayHalfLifeYears * (1-operatingFraction)` when operating fraction is fixed; adding continuous construction u is a proposed approximation, not the current seasonal post-harvest write.

The accumulated capital is also analytical:

`integral(E(t), 0..T) = E_eq*T + (E_0-E_eq)*(1-exp(-k*T))/k`.

That integral pays maintenance over the trajectory. It also contributes directly to accessible production because, below its cap, `q(t)^2 = ReferenceIntensity^2 * E(t)`. Computing all twenty years of production from final E would incorrectly credit late construction throughout the turn.

A useful stronger special case follows from the labor constraint itself. For one representative fully maintained field mix, let L be effective field labor available, m the maintenance workers per E, and b the build worker-years per E. If all residual permitted field labor goes into expansion, then:

`L = m*E + b*dE/dt`

`E(t) = L/m + (E_0-L/m)*exp(-(m/b)*t)`.

This naturally approaches labor-supported cultivation instead of assuming constant expansion forever. It applies only in the surplus-labor branch, below field/bridge caps, with fixed costs/efficiency and no separate expansion limit binding. It is not a replacement for preserving the harvest workforce. The general model must respect the submitted expansion limit and the current distinction between operation/harvesting and expansion.

For a fixed set of fields, integrated upkeep is `sum(m_i * integral(E_i(t)))`. Covered upkeep and expansion must fit the starting Farm Tool worker-year quota. Maintenance keeps its current coverage priority; newly manufactured tools cannot support earlier field work. Averaging tool efficiency over a turn may change timing even if the final debit is correct, so test a bounded covered/uncovered analytical split as well as an average-efficiency closure. This is a direct calculation of a small number of branches, not an event queue that repeatedly advances and rebuilds state.

### Eliminate the marginal-step loop too

Replacing only the ecology kernel while retaining eighty labor preparations and repeated 0.01-E field allocation does not meet the objective. Replace marginal stepping with a bulk allocation over the at-most-seven eligible tiles. Derive approximate benefit curves from the existing four-season expansion score and solve the shared labor constraint over their smooth branches. Preserve focus priority, physical work costs, ownership and deterministic ties.

Do not assume globally concave yield curves: q is proportional to sqrt(E), natural vegetation retention changes at q=.5, and safe-flow caps introduce further branches. Evaluate the finite relevant pieces and use a bounded root solve where necessary. The proposed allocator is the continuous approximation to the present greedy increments and can change tile allocations; it is not bit-equivalent Option 5. Do not add a global economy planner or use hidden information to make the approximation work.

Gatherer bridge construction is funded by **realized terrestrial gathering**. It cannot be independently awarded from requested or potential food. A first whole-turn estimate can provide its driver; a fixed correction must reconcile that driver with realizable receipts and labor before any final commitment.

## 3. Preserve integrated flows and nonlinear boundaries

For known or approximated drivers, a stock equation has the form:

`dF/dt = k_s*(K_s(t)-F) + G_s(t) - h_s(t)`

Its interior solution is:

`F(T) = exp(-k_s*T)*F(0) + integral(exp(-k_s*(T-t)) * [k_s*K_s(t)+G_s(t)-h_s(t)], dt)`.

Affine combinations of constants and exponential driver curves make this convolution analytical. The seasonal-map form packages those solutions and preserves their order. Condition has a similar exponential response when its damage coefficient is fixed or linearized.

The difficult parts are the branch changes, not exponential recovery:

- Food and nonfood biomass depletion, including herd spill into accessible food.
- Time-varying capacity limits and freshwater shortage.
- Cultivation's bridge, displacement and maximum-intensity boundaries.
- Condition-driven vegetation hysteresis, including separate Wood services.
- Tool exhaustion, food/feedstock target completion and shared claimant competition.
- Pressure derived from realized extraction above sustainable flow. Averaging utilization before applying the nonlinear excess-pressure law can erase seasonal overshoot even when annual totals match.

Use analytical boundary formulas where available and a **small bounded** number of branch evaluations or direct trajectory samples where necessary. Independent quadrature samples of an analytical curve are acceptable for a remaining non-elementary integral; they must not invoke a hidden sequence of state updates. Keep their number explicit in profiling. Do not replace seasonality with one average climate or introduce adaptive reprocessing until convergence.

Maintain F>=0, N>=0 and B=F+N, and finite freshwater/material inventories. Accumulated receipts must be nonnegative, monotonic in time, backed by the corresponding stock/renewal balances, and capped by fixed prior-cohort targets. Endpoint clipping alone cannot repair harvesting food that never existed earlier in the interval. Report overflow and unmet demand explicitly; do not disguise conservation residuals as recharge.

All claimants on a shared tile must use one tile supply trajectory. Approximate time-varying share/target functions if needed, but never integrate the full stock separately for each camp and add their outputs. Camp-level supply and manufacturing budgets need conservative reconciliation across all claimed tiles. These reconciliation operations are model approximations with their own cost and error; bound their pass count and test shared-site cases.

No universal proof currently shows that one linearized map handles every depletion/vegetation transition over twenty years. If a case cannot meet its error budget with the bounded method, report it as unsupported or a failed model candidate. Do not quietly restore eighty-step simulation for an unknown fraction of tiles and claim progressive execution has been removed. The seasonal resolver remains an explicit reference/oracle during development.

## 4. Keep coefficient fitting bounded

Use one prediction from the committed starting state to estimate the trajectory-weighted E, C, maintenance, tool coverage and realized harvest. Refit the four seasonal coefficient sets once around those representative values and evaluate the turn again. That is at most two whole-turn analytical evaluations, not annual/seasonal marching or an event scheduler.

At E=0, the derivative of sqrt(E) is singular. Use the exact zero-capital/bootstrap branch or a finite-range curve approximation with correct zero behavior; do not silently introduce a gameplay epsilon or linearize sqrt(E) at zero. Treat hysteretic vegetation as discrete state, not an unconstrained continuous Jacobian coordinate.

The fixed-Jacobian approximation captures first-order feedback from changing E/C because they are carried in the state. Higher-order products, changing derivatives and active-branch switches are its error sources. Twenty years can be a long extrapolation even for a stable exponential system. A predictor/corrector reduces some bias but is not an accuracy proof.

## 5. Potential follow-up scope, only after separate approval

The following staged work describes what a future proposal could request. None of these steps is currently authorized.

1. **Derive and specify the surrogate.** Write down the approximated transition laws, accumulated-flow identities, field allocator, tool/bridge treatment, sparse coupling and bounded branch rules. Distinguish exact subcases from deliberate model changes. Keep this separate from unrelated cache optimizations so its performance is attributable.
2. **Implement a harness-only prototype after separate approval.** Start with one field and one camp, then heterogeneous seven-tile footprints and shared human/herd claims. Add no production default, solver switch or save fields at this stage.
3. **Compare trajectories against the seasonal reference.** From identical committed states and fixed orders, sample q, E, condition, F, N, water, accumulated receipts, labor and tools throughout the turn. Sampling for validation may be dense; candidate runtime must remain bounded. Include startup, sustainable intensive agriculture, under-maintenance/abandonment, dry marginal land, depleted initial stocks, recovering condition, forest displacement, aquatic competition, tool exhaustion and nonaligned durations.
4. **Validate closed-loop behavior.** Run the same normal interturn policies for 25 post-discovery generations (500 years at the default turn duration). Identify changes in limiting factors, deaths, reserves, specialist emergence and land recovery. Cross-duration numerical tests must hold decisions/cohort assumptions fixed so they do not confuse extra player decisions or births with solver error.
5. **Measure total cost and decide on adoption.** Use matched 80/320 Neolithic workloads, recording total AI+preparation+resolution time, all approximation/correction work, allocations and tail cost. Only then propose a production integration and explicit contract/save-compatibility treatment. Do not resume the event-driven/regime-reuse approach.

### Gating the approximation

Cross-version bit equality is not the right gate for this deliberately changed numerical/economic model. Keep exact within-version scalar/worker/collection-order determinism, save continuation, conservation and filtered information boundaries. Preserve prior-cohort needs and the production-before-birth commit rule.

Evaluate **curve error**, **integrated yield/work error** and **qualitative economic behavior**, not just endpoint population. Record normalized maximum/path-integrated stock error, absolute q/condition error, cumulative food/biomass/water/work errors, timing of material shortages and each site's limiting factors. Normalize near-zero stocks using physical capacity/need scales, not division by a nearly zero baseline. Report distributions and worst sites; world averages cannot conceal a dry-site collapse.

For a future prototype, the assessment suggested screening budgets of <=2% of the corresponding fixed need/work budget for integrated one-turn food/work error, and <=0.02 absolute q/condition error at validation checkpoints on smooth cases. These are provisional design values, not approved acceptance gates, mathematical bounds, or permission for unbacked inventory. Boundary cases require individual explanation and tighter scrutiny of shortage/survival changes. Long-horizon policy feedback can amplify small numerical errors, so 500-year population divergence is diagnostic rather than a universal 2% rejection rule. Sustainable farming must remain sustainable; overharvest must still cause damage; degraded land must still constrain and recover production.

If the analytical follow-up is separately authorized, >=20% less aggregate post-discovery AI+preparation+resolution time versus `9dc18c0` is a candidate screening target, measured in three sequential warmed pairs at each size, with tail/allocations reported. This carries forward the missed target from the Options 1/5 experiment; it is not approval to begin implementation or an acceptance gate for the analytical proposal. Also measure resolver/seasonal-path speedup separately to expose costs moved elsewhere. An algebraically expensive surrogate or a large coupling solve can lose despite fewer passes, as the previous event-driven attempt demonstrated.

## Current authorization boundary

Options 1 and 5 were authorized and implemented in simulation commit `b5d98d72288d78a45a83967c5370a1cec5451d82`. Their exact replay and paired timing results are documented in [NeolithicOptions15Validation.md](NeolithicOptions15Validation.md): the primary target failed at both sizes, and the 320-turn-36 regression remains unresolved. This analytical approximation is the retained follow-up candidate, but remains design-only. Separate approval is required before any analytical prototype, test, benchmark, or production change. Do not automatically advance this proposal or return to event-triggered regime reuse.

No analytical prototype, scratch sweep, or analytical benchmark has been executed. Options 1/5 evidence is recorded in [NeolithicOptions15Validation.md](NeolithicOptions15Validation.md).
