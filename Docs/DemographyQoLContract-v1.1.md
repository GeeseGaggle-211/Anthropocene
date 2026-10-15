# Anthropocene | Demography & QoL contract
Version 1.1 | 15 October 2026 | Numerical-method decision and refactor handoff

Authority: the user’s decisions in this conversation, including the subsequent instruction that disease becomes an equal contributor to crowding at population 100,000. This document replaces the composite-QoL mortality design for the new demographic model. It is an authoritative implementation specification with a selected analytical coefficient set, not a claim that the native game has already been refactored or revalidated.

### Decision

Use explicit births and deaths. Births depend on realized food satisfaction; the fertility multiplier remains exactly one. The three fitted mortality components are exposure, crowding, and disease. There is no background-mortality term. Clothing and housing jointly remove up to 90% of exposure mortality, linearly. Crowding and disease are uncapped exponential-excess functions of population.

At 1,000 fully fed and fully covered residents, births equal deaths and crowding/disease contribute 75%/25% of their combined hazard. Their contributions become equal at 100,000 residents. This later crossover is not a second population equilibrium and does not make a Neolithic city of that size viable.

### Source baseline and experiment boundary

| Item | Identity / meaning |
| --- | --- |
| Verified main | 86cba712dfd76673bd9637052543bde85f810e0b (5 October 2026) |
| Prior RL simulator pin | 5559961164d6f9da8e6d20c1dc563ecec3eb04b7; not the same as main |
| Source constants | b0 = 0.045/year; g0 = 0.01145/year; starting population = 24 |
| Cultivation context | Primitive supported-intensity / effort-knee reference = 0.4; not a population cap |
| Validation completed | Previous calibration: 84 curve candidates; 9,261 equilibrium cases; 250 trajectories; 18 shocks; 27 checks. New: 1,152 numerical propagation comparisons and 72 midpoint diagnostic comparisons |
| Not performed | Native game runs, endogenous production/ecology sweeps, migration/fission validation, or PPO training |

Delivery: a Word reading copy, matching Markdown specification, exact coefficients, executable sweep, raw CSVs, and validation evidence. The current Drive connection exposes no create/edit action; no copy was written to Drive. No repository, saved game, training bank, or checkpoint was modified.


## 1 | Authoritative equations and units
N denotes the resident population of the demographic unit being evaluated. Rates are annual continuous hazards, not per-turn probabilities. C and H are physical clothing and housing coverage in [0,1]. F is realized food satisfaction in [0,1]. All core calibrations set future mitigation factors to one and additional hazards to zero.

```text
dN/dt = [b(t) − d(t)] N(t)
```

```text
b(t) = 0.045 × F(t) × f_fertility;     f_fertility ≡ 1
```

```text
d(t) = d_exposure + M_c d_crowding + M_d d_disease + Σ d_other
```

```text
d_exposure = e0 [1 − 0.45 C − 0.45 H]
```

```text
Φ(N;k) = [exp(kN) − 1] / [exp(1000k) − 1]
```

```text
d_crowding = 0.03130836758391647 × Φ(N; 0.00001000000000000)
```

```text
d_disease = 0.010436122527972156 × Φ(N; 0.000027161883008306556)
```

```text
e0 = 0.03255509888111371 per year;     M_c = M_d = 1 initially
```

The −1 makes population-linked hazards exactly zero at N=0; it avoids disguising a constant background term inside an exponential. Both curves increase strictly with population and have no ceiling. Disease has the larger exponential slope. For small kN, an exponential-excess curve is approximately linear; that is deliberate, not a different piecewise law.

| Coverage | Exposure relative to uncovered base |
| --- | --- |
| Neither covered | 100% |
| Clothing only, fully covered | 55% |
| Housing only, fully covered | 55% |
| Both half covered | 55% |
| Both fully covered | 10% |

Equal clothing/housing weights are the explicit v1 closure. The 90% is a joint maximum reduction, not two multiplicative 90% reductions. Overprovision above coverage one adds no protection. “−90%” means a 90% reduction, never negative mortality.

Crowding/disease shares use d_disease + d_crowding as their denominator. Exposure remains positive, so 75% and 25% cannot also be shares of all deaths. At N=1,000, full-service total mortality is 0.045/year: exposure 7.2345%, crowding 69.5742%, disease 23.1914% of all core mortality.


## 2 | Calibration derivation and shape selection
### Three anchors, one explicit shape closure

Anchor A: preserve the existing abundant-food instantaneous Paleo growth rate at N=24 with C=H=0. Anchor B: set the full-service demographic equilibrium at N=1,000. Anchor C: require crowding=disease at N=100,000, while the population-linked split is 75:25 at N=1,000. These anchors do not uniquely identify both exponential slopes.

```text
R = 0.75 Φ(24;k_c) + 0.25 Φ(24;k_d)
```

```text
e0 = [b0 − g0 − b0 R] / [1 − 0.1 R]
```

```text
P1000 = b0 − 0.1 e0
```

```text
Φ(100000;k_d) / Φ(100000;k_c) = 3
```

Given k_c, solve the last equation for k_d, then derive e0 and P1000. The coefficients at 1,000 are 0.75 P1000 and 0.25 P1000. The supplied script solves these equations directly, rather than guessing three independent mortality constants.

Selected shape closure: k_c = 1/100,000 = 0.00001 per resident. This reuses the specified crossover population as the crowding exponential scale, rather than inventing a separate village-scale knee. It is a declared modeling choice, not a uniquely measured historical constant or a statistically identified optimum.

| Crowding k | Half-cover equilibrium | Unmitigated hazard at 100k |
| --- | --- | --- |
| 0.0000001 | 650 | 10^0.80 / year |
| 0.0000100 (selected) | 651 | 10^1.03 / year |
| 0.0001000 | 660 | 10^4.12 / year |
| 0.0010000 | 746 | 10^41.99 / year |
| 0.0030000 | 860 | 10^127.80 / year |

Sweep: 81 logarithmically spaced k_c values from 10^−7 to 0.005, plus three explicit comparison values; 84 distinct candidates. Every candidate is recalibrated to the same anchors. Steep village-scale choices preserve more of the old small-camp growth curve but produce catastrophically large raw hazards by 100,000. The selected shallow exponential avoids that extreme extrapolation while meeting the requested village equilibrium band.

Preservation limit: N=24 is an instantaneous rate anchor, not an exact replay guarantee for every Paleo population or a complete 20-year trajectory. At N=100 without goods, the new fully fed rate is about 0.00830/year, versus the old 0.01145 ceiling. A positive population-dependent hazard cannot preserve a population-independent growth ceiling at every N without an offsetting change. The full Paleo opening remains a native regression gate.


## 3 | Equilibria and population taper
These roots isolate the demographic kernel. Each coverage percentage is held for both clothing and housing; it assumes enough productive service to maintain that percentage as population changes. Fixed nutrition is a controlled condition, not a simulated food economy.

| Clothing and housing | Equilibrium: F=1 | Equilibrium: F=0.95 |
| --- | --- | --- |
| 0% | 299.6 | 245.5 |
| 25% | 475.4 | 421.4 |
| 40% | 580.6 | 526.8 |
| 50% | 650.7 | 596.9 |
| 60% | 720.7 | 666.9 |
| 75% | 825.6 | 771.9 |
| 90% | 930.3 | 876.7 |
| 100% | 1000.0 | 946.5 |

![Core model only; no acute-starvation hook, migration, fission, or endogenous material depletion.](evidence/figures/net_growth.png)

At full nutrition, 50–100% joint coverage gives approximately 651–1,000 residents. At 95% food satisfaction, half coverage gives about 597. Thus the requested roughly 600–1,000 band is achievable without clipping N. It is not guaranteed for every low-coverage, low-food, or marginal settlement.

At full coverage, instantaneous net growth is +1.6769%/year at N=600, +0.8397% at N=800, zero at 1,000, and −0.8421% at 1,200. Because r(N) decreases continuously, the equilibrium attracts from either side. Above 1,000 the mechanism produces real decline toward equilibrium, not an artificial frozen population.

q_k=0.4 remains an economy-side scenario setting. It is not an input to mortality and does not trigger an era-specific exception. Native food supply, labor competition, actual q, geography, imports, and fission determine which part of this surface is realized. Historical main documents report targets and prior cohorts, not an exact universal 600–1,000 invariant. [S1–S4]


## 4 | Disease crossover and future urban scale
The late instruction is implemented as a crossover in the two population-linked causes. The 75:25 split is exact at 1,000; it is close to that throughout the Neolithic range, then disease increases in relative importance.

| Residents | Crowding share | Disease share |
| --- | --- | --- |
| 24 | 75.157% | 24.843% |
| 600 | 75.065% | 24.935% |
| 1,000 | 75.000% | 25.000% |
| 10,000 | 73.473% | 26.527% |
| 50,000 | 64.859% | 35.141% |
| 100,000 | 50.000% | 50.000% |

![Shares are of crowding + disease with equal mitigation factors. The 100,000 point is not a Neolithic carrying capacity.](evidence/figures/disease_share.png)

At 100,000 without later mitigation, each population-linked hazard is about 5.352806/year. Their total is 10.705613/year. Hazards may exceed one per year; they are not literal probabilities or instructions to subtract more people than exist. A positive continuous propagator handles turnover and changing population.

If a later-era configuration retained today’s fertility and exposure but reduced both population hazards equally, a multiplier near 0.00389931 would balance births at 100,000: about a 99.61% reduction. This is a quantified future infrastructure requirement, not an automatically granted bonus. Unequal disease/crowding mitigation shifts the realized crossover; the 100,000 calibration applies to the baseline curves or equal mitigation. No later-era modifier is enabled by this document.


## 5 | Recovery experiments and limits
The 250 continuous trajectories span ten starting populations (6 through 2,000), five joint coverages, and five nutrition levels, for 1,000 years each. All retain positive continuous populations and approach their applicable equilibrium without material numerical overshoot. Some very low-nutrition cases approach extinction; positive floating-point population is not proof of playable integer survival.

| Controlled experiment | Population after shock | Years after restoration to reach 950 |
| --- | --- | --- |
| No clothing/housing for 20 years | 658.9 | 55 |
| No clothing/housing for 40 years | 520.8 | 69 |
| No clothing/housing for 100 years | 374.9 | 83 |
| Half clothing/housing for 40 years | 737.3 | 46 |
| Zero food satisfaction for 40 years; core only | 342.8 | 86 |

All shock fixtures start at 1,000. After the shock, nutrition and both coverages are restored to one by the experiment. The 18 tested shocks resume positive growth immediately and reach at least 950 within 110 years after restoration. These are recovery-capability tests, not proof that an Elder can afford the restoration or rebuild the necessary supply chain.

### The larger integration risks remain visible

A low-population village can still lose toolmakers, material access, trade partners, or productive capital. Restoring C, H, and F externally bypasses those recovery constraints. The native gate must therefore include depleted stocks and executable rebuilding plans, not just restore a numerical coverage value.

Linear fertility response alone permits chronically underfed equilibria. In a synthetic constant food-service experiment supporting 600 fully fed person-equivalents, full material coverage settles near 766.8 residents at food satisfaction 0.7825. With the existing-starvation-equivalent post-grace hook, it settles near 746.6 at 0.8037. Neither is a native q_k=0.4 result. This limitation must not be concealed by calling every population in the target band a success.

Do not steepen f_nutrition, create an extra mortality constant, or install a population cap to hide that result. f_nutrition=F remains authoritative. Preserve explicit hunger accounting, measure food satisfaction separately, and classify food-limited equilibria honestly. Food-secure population parity is an independent promotion gate.

High protection also accelerates growth at small N: with full coverage, N=24 has about 4.075% annual net growth and reaches 53.6 over 20 years. This follows from high fertility and the requested 90% exposure reduction. The production model must pay the resulting food and clothing costs; do not suppress this effect with the retired growth ceiling.


## 6 | Physical service and starvation boundary
### Coverage is delivered service, not expectation

Clothing includes physically consumed Cloth and Animal Fabric, never a technology-unlock flag or an unspent stock counted repeatedly. Housing uses people-capacity from fixed and portable shelter at the relevant site; it does not use the old permanence discount for social cohesion. Each coverage saturates at one. Expectation, happiness, legitimacy, and the composite QoL scalar must not enter any core mortality rate.

Food satisfaction means actual food consumed divided by biological need for the same accounting cohort and interval. Imports count only after valid delivery; exported food and manufacturing feedstock cannot also be eaten. Reserves remain finite stocks and policy choices, not a second fertility switch. Remove the mandatory full-reserve fertility gate and the one-full-turn recovery birth prohibition in the new model.

### Continuous population must have a paid service budget

Let Y=∫N(t)dt. The source consumption normalization is 4 food units/person-year, and reference textile demand is 0.05 units/person-year. Use actual configured values, not duplicated literals. For a frozen interval service budget, consumed food is c_food F Y and consumed cloth is c_cloth C Y. Housing remains capacity divided by population, with depreciation handled once. Births must not create unpaid person-years.

A permitted top-down closure is a local person-year root: for trial Y derive F=min(1,food_budget/(c_food Y)) and C=min(1,cloth_budget/(c_cloth Y)), integrate the demographic kernel with physical housing capacity, and solve Y_model(Y)=Y. All budgets must be fixed from real receipts and validated production; no extra trade, labor allocation, harvest, or planner invocation occurs inside the solve. This is a conservation closure, not an economic controller. Freeze the exact native budget chronology and test it before enabling the model.

### Existing starvation is an explicit compatibility hook

The three fitted causes do not authorize silently deleting existing starvation losses. Preserve that mechanism through a named LegacyStarvation hook during the first refactor, rather than adding d_background or fitting a new deprivation parameter. Other future hooks remain zero. At the current 20-year reference, the post-grace equivalent rate is:

```text
d_legacy_starvation(F) = −ln[1 − 0.18(1 − F)] / 20
```

The 0.18 and two-turn grace come from main. Preserve the initial 40-year reference grace explicitly as physical deficit time, with a boundary event when it expires. F restored to one resets this hook immediately; there is no extra fertility cooldown. Apply this hazard OR the old discrete loss, never both. Reference-fraction equivalence does not imply exact old trajectories once population changes continuously. Core calibration and shock tables exclude the hook; finite-food diagnostics include a clearly labeled continuously active post-grace comparison. [S1, S5]


## 7 | Nonlinear time propagation and accounting
### The old one-exponential update is no longer exact

Because crowding and disease depend on N(t), r is not constant during a turn. N1=N0 exp(r(N0)T) is only a frozen-rate approximation; it must not be used as the authoritative 20-year propagator. At large N it can manufacture an unrecoverable numerical collapse even when the true flow returns smoothly toward a stable equilibrium.

```text
Y = ∫ N(t)dt;     B = ∫ b(t)N(t)dt;     D_j = ∫ d_j(t)N(t)dt
```

```text
N1 − N0 = B − ΣD_j
```

The reference implementation integrates log N and the gross-flow counters with DOP853 at tight tolerances. This is an external correctness reference, not an instruction to ship SciPy or a year-by-year loop. A C# local nonlinear propagator must match it with bounded error, positive population, deterministic evaluation, and explicit failure on nonconvergence.

Numerical selection (v1.1): for a fixed physical-service envelope, calculate the positive equilibrium where it exists, then propagate x = ln N by solving a continuous, piecewise-affine approximation of the net-rate function. Each log-population band has an exact exponential flow; cross only bands the trajectory traverses within the requested arbitrary T. The next section is normative. No ecology, production, AI policy, year-by-year simulation, or demographic-state commit occurs inside a band.

### Integer residents and gross demographic turnover

Commit a single reconciled population endpoint, with persistent fractional accounting and a nonnegative integer ledger. Do not subtract gross deaths before adding gross births: at equilibrium N=1,000, a 20-year interval has approximately 900 births and 900 deaths, not zero activity. Larger or longer intervals can have gross deaths exceeding the initial cohort because later births also contribute person-years.

At this aggregate, age-unstructured resolution, replacement births do not mean 900 new workers instantly act during the same locked turn. Keep labor locked during resolution; reconcile the next-boundary workforce to the net resident change. Do not apply gross natural deaths as 900 workforce casualties followed by 900 unassigned newborns: that would accidentally erase specialization. War casualties and explicit cohort-specific events retain their own identity-aware rules.

Separate natural births/deaths, acute starvation, migration, fission, and hunter transfers. World conservation must hold after every phase. Every person belongs to one demographic/food cohort; connected or detached hunters must not disappear from food demand, be counted twice, or be used as a reversible crowding-avoidance exploit. Explicit hunter/dispatch fixtures are required before whole-game adoption.


## 7A | Equilibrium-rooted piecewise analytical population propagation
### One demographic call for an arbitrary elapsed interval T

Design decision: the simulation resolves demographics once for any requested physical interval T >= 0. This call may evaluate several mathematical log-population bands, but it does not advance ecology, rebuild goods, replan labor, invoke the Elder, or update saved world state per band. The word “Laplace” refers only to each linearized local ODE. A single global Laplace transform does NOT solve the underlying nonlinear population law.

```text
x = ln N;    R(x; z) = b(z) − d(exp(x), z);    dx/dt = R(x; z)
```

z contains the declared physical-service inputs and mitigation state. When those inputs are held fixed and the configured crowding/disease hazards are positive, R decreases monotonically with x. If R(0-population limit) > 0 there is one positive stable equilibrium N*: R(ln N*) = 0. Find it by deterministic monotone bracketing, not a population clamp. If no positive root exists, classify a decline-to-extinction regime; if all population-dependent hazards vanish and net birth rate is positive, report no finite equilibrium.

### Continuous piecewise-affine rate, not discontinuous step hazards

```text
For x within band [x_j, x_{j+1}]:  R_hat(x) = R_j + m_j (x − x_j)
```

```text
m_j = (R_{j+1} − R_j) / (x_{j+1} − x_j),    m_j <= 0
```

Use a deterministic global grid in x = ln N, initially 0.05 log-population units for the first implementation candidate. Insert the exact log equilibrium as a shared knot, so the approximated model preserves that equilibrium exactly. Adjacent bands share endpoint values; do not use a zero-order discontinuous staircase or relocate the root through interpolation.

### Exact local exponential solution and band crossing

```text
x(t+h) = x(t) + R_hat(x(t)) expm1(m_j h)/m_j
```

```text
If m_j -> 0:    x(t+h) = x(t) + R_hat(x(t)) h
```

```text
h_edge = log1p[ m_j (x_edge − x(t)) / R_hat(x(t)) ] / m_j
```

Choose the crossing edge from the sign of the current affine rate. Cross it only if the affine rate retains its sign to the edge, the crossing time is positive and finite, and it precedes remaining T. Otherwise apply the local closed-form for all remaining T. Never cross a stationary root in finite time, whether approached from below or above. Use expm1/log1p for numerical stability and canonical floating-point tie-breaking at shared knots.

The entire call is a composition of exact local flows of the quantized model. No arbitrary fixed year-step is selected and no hard population ceiling is introduced. Computational work scales with stationary-root search, distinct population bands traversed, deterministic refinement and accounting quadrature: it is NOT universally O(1) in T or one arithmetic evaluation.

### External events and service coverage

For a fixed z, the method has the semigroup property: resolving T must agree with consecutive portions summing to T under the same physical schedule, up to rounding/quadrature tolerances. When service deliveries, disasters, famine grace expiry or other externally specified events change z within a turn, split the solver at those PHYSICAL event boundaries, not at each year.

Housing coverage H = min(1, shelter_capacity/N) can change with the evolving population even if housing stock is frozen; clothing and nutrition can likewise depend on physical person-years and finite supplies. Such dependencies must be incorporated into R(x,z) and the matching equilibrium/physical-budget solver, with service thresholds treated as knots. Do not treat beginning-of-turn coverage as constant merely to use the fixed-input prototype; never create unfinanced births, shelter or food.


## 7B | Numerical verification, conservation and performance gate
### Reproducible standalone numerical comparison

The new Python prototype solves the same calibrated full nonlinear equation against an independent high-accuracy DOP853 reference. Matrix: 12 initial populations (6–100,000), six fixed food/coverage combinations, four interval lengths (1/5/20/50 years) and four log-band widths: 288 distinct scenarios, 1,152 solver comparisons. There are no native C# runs in this measurement.

The separate fail-closed standalone numerical gate passes 13/13 checks, including the exact zero-time identity, gross turnover at equilibrium, conservation, split-horizon endpoint consistency, nonpositive-growth handling and positive population. These do not substitute for native gameplay tests.

| Log band width | Maximum endpoint error | Maximum person-year error | Median bands crossed | Maximum bands |
| --- | --- | --- | --- | --- |
| 0.200 | 0.5002% | 0.5058% | 3.0 | 32 |
| 0.100 | 0.1230% | 0.1236% | 4.0 | 64 |
| 0.050 | 0.0308% | 0.0311% | 7.0 | 127 |
| 0.025 | 0.0077% | 0.0078% | 12.5 | 253 |

A 0.05 log-band width is the initial verified FIXED-grid candidate: maximum endpoint error 0.0309% and maximum person-year error 0.0311% over the tested matrix. The wider 0.10 grid has maximum error 0.1230% across all scenarios, despite an approximately 0.0895% endpoint error for the 100,000-to-about-1,710 example over 20 years. This replaces unverified performance claims with numerical evidence.

| Initial people | High-accuracy endpoint (20y) | 0.1-band endpoint | Error |
| --- | --- | --- | --- |
| 600 | 776.3 | 776.1 | 0.038% |
| 1,000 | 1,000.0 | 1,000.0 | 0.000% |
| 1,600 | 1,192.5 | 1,191.8 | 0.053% |
| 2,500 | 1,347.1 | 1,346.2 | 0.067% |
| 100,000 | 1,709.7 | 1,708.2 | 0.089% |

### Gross births, cause-specific deaths, and resource accounting

Per accepted analytical band, integrate Y = integral N dt, B = integral bN dt and provisional D_j = integral d_j N dt using bounded-order quadrature along the analytic x(t). Quantization introduces a small difference between the exact affine-flow delta N and integrating the original continuous hazard components. Reconcile cause-specific death totals proportionally to the conservation-consistent total D = B − (N_end−N_start), and record the correction magnitude; reject/unrefine when it exceeds a declared tolerance. The supplied numerical prototype uses four-point Gauss-Legendre and records its maximum cause-scale correction.

```text
N_end − N_start = B − D_exposure − D_crowding − D_disease − D_other
```

At exact equilibrium, population change is zero but person-years and gross births/deaths are NOT zero. Retain fractional remainders and reconcile a single integer population/workforce update only at the native turn boundary. Use the same paid person-years and physically realizable goods/food budgets as Section 6; the standalone fixed-service experiment does not validate that joint closure.


## 7C | Performance and numerical robustness
![Relative endpoint errors over 20 years, with fixed full food and material coverage. The root and hazard coefficients are unchanged across widths.](evidence/figures/piecewise_accuracy.png)

### Native speed advantage is a hypothesis, not a result

A separate 72-case Python endpoint comparison against an adaptive log-midpoint diagnostic did not prove a universal reduction in hazard evaluations. The 0.05-band method had a median of 71 total hazard evaluations per 20-year case (including quadrature), versus 62.5 midpoint rate evaluations; maxima were 594 versus 615. These counts are not identical unit costs and do not establish relative C# runtime. Both methods passed the isolated endpoint accuracy target in that comparison.

Require matched native timings at 80, 320 and mature-world settlement counts, including equilibrium bracketing, knot reuse, coverage/person-year coupling, quadrature, C# allocations and diagnostics-off profiles. Compare p50/p95 turn time, max and median hazard evaluations, cache hit rates, numerical failures and convergence versus the adaptive midpoint fallback at equal error thresholds. Promote the analytical method only if it is correct, deterministic, and meets the native cost target; preserve fallback as a diagnostic comparator.

### Robustness and deterministic refinement

If log bands must be refined, base the decision on deterministic local curvature of R(x) and shared knot values, not on the requested T or stochastic scheduling. Interval-dependent grid choices can break split/merge time invariance. The initial 0.05 fixed grid is numerically adequate only for the TESTED domain; later eras require new error/reference sweeps. Keep explicit iteration/band budgets and diagnose nonconvergence rather than silently clipping hazard, population or time.

Evaluate exp(kN) in an overflow-aware manner for extreme large N. If a physically valid state exceeds ordinary floating-point hazard range, use a separately validated log-domain or event-time strategy, or fail with a reproducible numerical diagnostic. Do NOT invent a mortality ceiling. Preserve finite positive N in continuous integration, with an explicit integer extinction convention and tests for populations below one.


## 8 | QoL, control, and RL integration
### QoL remains welfare, not a universal biological constraint

Remove all demographic calls to Camp.QoL, Q^−a, ZeroGrowthQoL, and the nonnegative net-growth clamp from the new model. Physical shortages influence their named causes. Composite QoL, expectations, social cohesion, amenities, and culture continue to describe welfare and social behavior; changing them alone must leave births and core mortality unchanged.

Do not simultaneously redesign the reward or add bonuses for births, population growth, trade volume, goods production, or mortality reduction. Preserve the authorized local population-weighted QoL objective and its jurisdiction boundary. New gross births, deaths, and person-years are diagnostics. Any migration from the existing prior-cohort interval reward to a different integrated-cohort definition is a separately versioned reward decision, not part of this calibration.

### Fission, migration, and welfare pressure

Removing social cohesion from mortality does not delete its role in migration or fission. First retain those mechanisms as separate social processes. Their live effects may keep settlements below the isolated demographic equilibrium. Do not compensate by raising fertility or weakening mortality to force every individual camp to 1,000.

Run demographic-isolation fixtures without fission/migration first, then matched live-world tests with them restored. Report cause-specific population changes and settlement distributions, not just global resident totals. A smaller focal camp after a viable fission is not a demographic death, and a high QoL among a few survivors is not population success.

### Keep strategic authority with the Elder

The new kernel is physical dynamics only. It must not impose a profession mix, require every village to manufacture all goods locally, pick trade partners, force surplus targets, or protect a failing policy through hidden rescue assignments. An Elder may obtain clothing, shelter inputs, or food through legitimate imports. The resolver enforces conservation and legality; it does not optimize the action.

Observations should expose measured nutrition, physical clothing/housing coverage, population, effective fertility, and realized cause-specific deaths/rates, plus permitted memory and action receipts. Do not expose hidden counterparty inventory, future shocks, or an omniscient action-value oracle. Cause-specific measured outcomes make the new dynamics identifiable without giving the policy a prescribed strategy.

Retain separate fixes for independently identified memory/action-history and trade-interface defects. This mortality refactor does not establish that those defects have been repaired, that a larger MLP is needed, or that recurrent architecture is justified. Train fresh only after the new simulator generation and observation contract are frozen and the relevant plumbing tests pass.


## 9 | Refactor map and version migration
| Surface | Required change |
| --- | --- |
| QoLMortality.cs | Keep old behavior behind a legacy model identifier. New path implements the three cause-specific hazards and explicit hooks. |
| Model.cs / OpeningConfig.json | Versioned demographic config, selected coefficients, annual units, exact f_fertility=1, stationary root solver settings, log-band tolerance, cause counters and fractional state. |
| GenerationalEconomy.cs | Replace net-growth-only planning/commit; call the equilibrium-rooted piecewise log-population propagator once per physical horizon, account for paid person-years, signed net change, gross births/deaths and hunger. |
| NeolithicProduction.cs | Supply physical clothing/housing coverage and consumption; keep expectation/QoL calculation separate. |
| FoodBucketTargets / PlanningResidentPopulation | Use one consistent demographic projection interface. Retire old QoL growth ceiling and binary reserve/recovery fertility gates in new mode. |
| Workforce / SaveCodec / replay | Reconcile net workforce without erasing occupations; persist exact model generation and accounting state. |
| Fission / Immigration / observations | Conserved transfers; no implicit extra mortality; explicit model/schema identity and diagnostic fields. |
| Elder native bridge / learner manifests | New simulator pin, observation/normalization identity and snapshot generation. No old transition bank relabeled as new physics. |

### Compatibility

Old saves must continue under the old demographic model unless explicitly migrated. Do not infer a new model merely because a field is absent. A migrated save gets a recorded source model, destination model, source commit, and initialized remainders/deficit-age state. Do not rewrite historical net-growth rows as gross births; those quantities were never recorded.

The old DemographicGrowthMultiplier cannot silently alter the new specified birth law or negative net growth. Preserve it only in legacy mode; a future fertility modifier needs its own explicit contract. The new model has no technology-name exemption: Paleo, cultivation, husbandry, and fishing use the same physical rate functions.

The engineering branch must be based on a newly verified intended integration head. The numerical calibration reads main at the pinned October 5 commit; the prior training simulator is an October 10 commit on a different line of development. Do not overwrite later ecology, fishing, trade, or autonomous-action work by building blindly from old main. Record the actual refactor base and resulting native SHA in the new generation manifest.

This revision specifies a numerical method and supplies independent Python prototypes; production C#, saves and learner files remain unchanged. The attached coefficient JSON and this contract are the handoff for a new, verified integration branch; the former native training pin remains unchanged.


## 10 | Acceptance gates before fresh MLP training
### Completed analytical checks

The original v1.0 kernel run passes 27 checks: the N=24 growth anchor, N=1,000 equilibrium, 90% joint exposure reduction, individual coverage effects, zero population hazard at N=0, 75:25 and 50:50 cause shares, positive exponential slopes, negative net growth above equilibrium, nonnegative roots, gross-flow conservation, positive continuous trajectories, recovery, and time-partition consistency. Hunger-hook reference arithmetic and a large-population nonlinear decline are also checked.

Maximum measured continuous birth/death balance residual across 250 trajectories: 8.59e-11 people. Maximum endpoint difference for one 20-year interval versus two 10-year intervals: 9.09e-13 people. Gross-flow partition difference: 2.55e-11. These numerical checks are not native conservation/replay certification.

### Mandatory native gates

| Gate | Acceptance requirement |
| --- | --- |
| Kernel parity | Match reference rates, equilibria and arbitrary-t interval flows; v1.1 log-band integrator, equilibrium invariance, gross counters and numerical tolerance; no frozen-rate shortcut. |
| Units and continuity | 20 vs 2×10 vs 4×5 years with the same physical service schedule; preserve event boundaries and save continuation. |
| Paid resources | Meals, textile feedstock, imports, storage, reserves and newborn person-years cannot double-use the same stock. |
| Small populations | N=6/12/24, stock depletion, rebuilding tools/shelter, dispatch/return, rounding and integer extinction are executable and explained. |
| q_k=0.4 live economy | Run the pinned native cultivation setting; record actual q, food satisfaction, capital, material coverage and labor. Do not impose q=0.4 as a cap. |
| Social flows | Fission off/on and migration off/on; report births, all death causes and transfers separately. |
| Three subsistence paths | Agriculture, husbandry, fishing and mixed economies, including food/material import dependence. |
| Training generation | Freeze the new native SHA, schemas, cohort bank, TRAIN-only normalizer, reward identity and persistent observation history. |

Use at least three independent native world seeds for the acceptance matrix and 25–50 decision continuations, plus separate demographic-isolation fixtures. The roughly 600–1,000 target applies to viable established Neolithic cohorts with sustained service, not every marginal geography or underfed population. Full-service isolation must approach 1,000 without clipping; live distributions must be evaluated separately.

The observed high-fertility, low-exposure growth burst and underfed-equilibrium diagnostics are mandatory reporting fields, not optional footnotes. A passing population endpoint does not override failed food security or recovery.


## 11 | Performance, evidence, and reproduction
### Performance contract

An individual hazard evaluation is O(1), but the selected population-domain solver additionally pays for root bracketing, traversed log-population bands, fixed-order gross-flow quadrature and any physical-service closure. It does not need a fixed yearly or seasonal loop, yet is not constant-time in general. Reuse existing housing indexes and avoid all-world scans. No additional individual agents, global policy optimization or ecology replay are authorized.

Before adoption, compare the v1.1 analytic solver against an adaptive log-midpoint fallback under IDENTICAL C# states, required accuracy, goods/food closure and cause-counter accounting. Report p50/p95 total turn time, root solves, crossing bands, hazard+quadrature evaluations, allocation count, conservation error, fallback rate and numerical errors. Python execution times and unequal diagnostic operation counts are not native runtime evidence. Hard iteration/band budgets must produce explicit diagnostics, never hidden clipping.

### Reproduction

```text
python -m pip install -r requirements.txt
```

```text
python sweep_demography.py --out evidence
python piecewise_analytic.py --out evidence
python verify_piecewise.py
```

The script is deterministic and requires no network access, random seeds, Unity, game repository, or trained policy. It reconstructs coefficients from the stated anchors, solves the equilibrium surface, integrates trajectories, runs shock and finite-food diagnostics, and writes machine-readable checks. Evidence hashes are listed in SHA256SUMS.txt.

| File | Contents |
| --- | --- |
| selected_coefficients.json | Exact coefficient values and calibration anchors |
| curvature_sweep.csv | 84 recalibrated exponential-shape candidates |
| coverage_nutrition_sweep.csv | 21×21×21 physical coverage/nutrition equilibrium surface |
| trajectories.csv / trajectory_samples.csv | 250 1,000-year runs and selected 20-year samples |
| recovery_shocks.csv | 18 externally restored-service recovery experiments |
| finite_food_diagnostic.csv | 20 synthetic finite-food comparisons, not native ecology |
| tests.json / summary.json | 27 final checks, numerical residuals, source pins and environment |
| initial_precision_diagnostic.json | First-run numerical overshoot diagnostic retained for provenance |
| piecewise_analytic.py | Standalone equilibrium-rooted log-band analytical propagator and midpoint comparator |
| piecewise_analytic_sweep.csv / piecewise_selected_cases.csv | 1,152 solver comparisons / representative 20-year trajectories |
| piecewise_vs_midpoint.csv / piecewise_time_partition.csv | 72 endpoint operation-count comparisons / split-interval consistency |
| piecewise_analytic_summary.json / piecewise_gate.json | Numerical accuracy/cost proxies and 13/13 fail-closed standalone gates |

The first reference run at 2×10^−11 tolerances exceeded the declared 10^−8 relative equilibrium-overshoot tolerance by a small numerical amount (2.23×10^−8). Tightening the integration tolerances to 2×10^−13 resolved it without changing coefficients, equations, or acceptance thresholds. The initial diagnostic is retained; final tables come from the tighter run.

Status: GO for implementing the versioned physical model and equilibrium-rooted log-band solver behind native feature/version gates, with adaptive midpoint retained as a measured fallback. NO-GO for claiming native q_k=0.4 balance, whole-world recovery, or policy promotion before the native gates pass. New environmental physics requires a fresh experiment generation, not a relabeling of old training evidence.


## 12 | Source register and authority limits
Primary sources were read from the connected repository during this task. All source-code links below are pinned to the verified main commit. The user’s current instructions supersede historical proposals where they conflict with this contract. Numeric rates are gameplay calibration values, not estimates of universal prehistoric birth or death rates. The v1.1 numerical method is a selected approximation, not a claim of closed-form solvability of the original nonlinear ODE.

### S1 | OpeningConfig.json

OpeningConfig.json: birth/growth anchors, starting population, coverage quantities, 20-year reference, starvation mortality and grace.

https://github.com/GeeseGaggle-211/Anthropocene/blob/86cba712dfd76673bd9637052543bde85f810e0b/Assets/_Anthropocene/Resources/OpeningConfig.json

### S2 | CultivationSoftEffortSweep.md

CultivationSoftEffortSweep.md: historical target and cohort evidence, source-provenance qualifications, supported intensity versus actual intensity.

https://github.com/GeeseGaggle-211/Anthropocene/blob/86cba712dfd76673bd9637052543bde85f810e0b/Docs/CultivationSoftEffortSweep.md

### S3 | LandPopulationCalibration.md

LandPopulationCalibration.md: rejected hard-cap calibration and intended approximate camp/village targets; historical experiment, not a current native validation.

https://github.com/GeeseGaggle-211/Anthropocene/blob/86cba712dfd76673bd9637052543bde85f810e0b/Docs/LandPopulationCalibration.md

### S4 | LandFoodPopulationGrowth.md

LandFoodPopulationGrowth.md: prior-cohort food budgets, reserve debt and original food-funded growth convention; historical experiments have narrower scope.

https://github.com/GeeseGaggle-211/Anthropocene/blob/86cba712dfd76673bd9637052543bde85f810e0b/Docs/LandFoodPopulationGrowth.md

### S5 | GenerationalEconomy.cs

GenerationalEconomy.cs: current starvation/recovery gating, prior-cohort consumption and net-growth-only population ledger.

https://github.com/GeeseGaggle-211/Anthropocene/blob/86cba712dfd76673bd9637052543bde85f810e0b/Assets/_Anthropocene/Simulation/GenerationalEconomy.cs

### S6 | QoLMortality.cs

QoLMortality.cs: retiring Q^−a ceiling, zero clamp, Paleo exemption and unrecorded implied deaths.

https://github.com/GeeseGaggle-211/Anthropocene/blob/86cba712dfd76673bd9637052543bde85f810e0b/Assets/_Anthropocene/Simulation/QoLMortality.cs

Authority distinction: the state law, fertility lock, three named baseline causes, linear 90% exposure protection, 75:25 Neolithic share, 50:50 at 100,000 and population target come from the user. Equal clothing/housing weights, zero-origin exponential excess, the explicit crowding-scale closure, continuous-rate interpretation, numerical verification thresholds the log-population piecewise-analytical propagator, equilibrium-root knot and accounting quadrature, and compatibility mapping are specified implementation choices in this version. Native balance and performance remain validation obligations, not facts established by these standalone sweeps.
