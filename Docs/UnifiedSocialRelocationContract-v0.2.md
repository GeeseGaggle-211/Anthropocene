# Anthropocene — Unified Social Relocation & Future Specialist Concentration

**Design contract v0.2 | 15 October 2026 | Implementation-ready architecture; native balance not yet certified**

**Repository under review:** `GeeseGaggle-211/Anthropocene`, branch `feature/demographic-mortality-v1`, reviewed at `bcf7f2147fb476b87a976f6c6d4400b5f02ba894` (verify latest head before implementing). **Companion:** `Docs/DemographyQoLContract-v1.1.md` and `Docs/DemographyQoLValidation-v1.1.md`. **Prior standalone research:** `UNIFIED_SOCIAL_FLOWS_README.md`, 15 October 2026. No production files were changed by these supplementary tests.

## Executive decision

**Implement now:** one physically grounded counterfactual evaluator for immigration, crowding-driven fission, and distress fission; forecast source and destination services using supported capacity rather than replaying historic coverage as future supply; retain physical demographic births/deaths, goods conservation, source/incumbent safeguards, and deterministic resolution. Keep *both* success-driven and distress fission. Keep observed QoL and its RL welfare role separate from the migration decision utility. Measure causal native flow outcomes before approving calibration changes.

**Reserve now, activate later:** a *zero-valued*, explicit `EmploymentOpportunityProvider` extension that can add funded, skill-matched productive opportunities when professions, job vacancies, employers, and actual market demand exist. Do **not** ship a job-attraction bonus, labor-demand thresholds, wages, job-driven RL schema changes, or new specialist-capacity mechanics in the Neolithic implementation. A neutral/no-provider path must produce identical results to the same evaluator without the extension.

**What the tests establish:** a synthetic stress model supports meaningful future migration into cities with 1,100–2,000 initial residents when funded specialist work actually increases delivered goods and mortality pressure is physically mitigated. Fictitious vacancies do not introduce attraction in the tested grid. However, a nominally beneficial transfer can still be wrong when production forecasts miss by 20%, and a long-horizon tail can be negative despite a positive 20-year benefit. None of this certifies native game behavior.

## 1. Existing implementation and why the contract needs a shared estimator

The reviewed branch currently implements:

- `Fission.QoLModel.ForwardAt`: takes historically *realized* food and textile satisfaction and scales these by new headcount, plus unchanged material/cohesion terms. The economically available supply next turn may differ, especially when labor leaves or arrivals have not been trained/equipped.
- `Immigration.ForwardUtility`: uses present-weighted forecast QoL, distance/attachment/lifestyle costs and a return penalty. At the receiving settlement, the current algorithm reconciles pending inflow by a monotone binary search on group size.
- `Fission.Evaluate`: one branch tests parent QoL gain, another tests the departing group's gain in food/water distress, with near-zero QoL ratio shortcuts. These can encourage fragmentation independent of whether both endpoints are sustainably better off.
- `PhysicalDemography.Rates` and `AdvanceCoupledBudget`: explicit birth, exposure, disease, crowding and starvation hazards, integrated independently of composite QoL. Physical demographic inputs are available for forecasting and post-transfer diagnostics.

The last native same-physics 2×2 social ablation in `Docs/DemographyQoLValidation-v1.1.md` recorded 24/310/24/297 terminal camps for neither/fission-only/immigration-only/both, respectively, from three shared saved boundaries. Fission creates the settlement network; immigration becomes active largely within those lineages. Receiving camps show an unresolved next-turn clothing/QoL forecast gap. The diagnostic establishes a modeling mismatch to investigate, **not** a proven migration-caused mortality spiral.

## 2. Shared decision variables

For a proposed transfer of `m` residents from settlement `A` to destination `B`, compare **X₀**, everyone staying, with **X₁(m)**, the proposed move. In immigration, B is an existing settlement, and incumbent residents must be included. In fission, B is a new daughter with **zero preexisting population or fixed infrastructure**; portable stocks, tools, livestock and demographic progress must be conserved and founding costs paid. Known location, skill and market information is restricted to what residents actually know.

Define one physical forecast provider:

`Forecast(s, population, retained_workforce, committed_stocks, usable_infrastructure, committed_trade, candidate_transfer, interval)`

It returns an immutable forecast envelope including food availability, clothing production/consumption, shelter capacity, protection coverage, expected per-cause mortality, person-years, confidence flags and economic opportunity channels. It must use the **realizable** inputs at each candidate state. Resident-count-scaled historical `FoodNeedsMetRatio` or `ClothPerCapita` is an observational diagnostic, **not** a forecast production capacity.

For a settlement i under counterfactual X, calculate:

\[
F_i^X(t)=\min\!\left(1,\frac{\text{physically available food}_i^X(t)}{\text{food need}_i^X(t)}\right),\quad
C_i^X(t)=\min\!\left(1,\frac{\text{physically available textiles}_i^X(t)}{\text{textile need}_i^X(t)}\right),\quad
H_i^X(t)=\min\!\left(1,\frac{\text{shelter capacity}_i^X(t)}{N_i^X(t)}\right).
\]

Use the current demographic hazard law with `F,C,H,N` and inherited deficit history (including starvation grace), not aggregate QoL. For a physical preference horizon `τ`, personal cumulative mortality risk is:

\[
P_i^X(\tau)=1-\exp\left[-\int_0^\tau d_i^X(t)\,dt\right].
\]

Use forecast experienced welfare \(\bar Q_i^X=(1/\tau)\int_0^\tau Q_i^X(t)\,dt\). A candidate's utility is:

\[
U_i^X=\bar Q_i^X - \lambda_D P_i^X + w_J J_i^X + U_{\text{other},i}^X.
\]

The job term `J` is exactly zero in v0.2 Neolithic production. `U_other` retains justified distance, attachment, kin and lifestyle effects as costs/constraints, never as fabricated goods. Composite `Camp.QoL` remains the *experienced welfare* measurement; it is not an input to mortality.

## 3. Future job demand is bounded by executed economic opportunity

When enabled in a later era, funded unfilled positions for skill/profession `r` at destination `B` must satisfy:

\[
v_{B,r}=\max\left(0,\min(D_{B,r}^{\mathrm{demand}},K_{B,r}^{\mathrm{funding}},
T_{B,r}^{\mathrm{tools}},I_{B,r}^{\mathrm{inputs}},P_{B,r}^{\mathrm{workplace}})
- E_{B,r}^{\mathrm{filled}}-E_{B,r}^{\mathrm{reserved}}\right).
\]

**Important:** a single migrant cannot fill multiple jobs. Profession/skill composition of the migrant cohort must be conserved in the actual transfer, and any matching algorithm must enforce \(\sum_r x_{B,r}(m)\leq m\) with role eligibility and tool/input/worksite feasibility. A labor-short settlement can have economic demand and **zero fundable vacancies**. If a migrant is unqualified, the matched count for that profession is zero. Apparent labor demand is not automatically additional productive capacity.

A candidate future preference signal can take the bounded form:

\[
J_B(m)=\frac{1}{m}\sum_r x_{B,r}^{\mathrm{credible}}(m)\,\rho_{B,r},\qquad
\Delta J=J_B(m)-J_A^{\mathrm{foregone}}(m).
\]

`ρ` measures a dimensionless expected *actual* improvement in job security or compensated opportunity, normalized and bounded by verified employment value. Future calibrations may use a matched worker's economically realized output or wage opportunity, but wages that simply transfer resources between existing residents are not newly created world welfare. Job preference is **not** counted in aggregate social benefit until produced goods/services or verified net exchange surplus is delivered and valued without double-counting consumption already in QoL. A paper vacancy, unfunded employer, missing equipment, missing inputs or unreachable contact must contribute zero.

As a growing city fills vacancies, the opportunity term must fall: **no unlimited “city attracts because it is large” feedback**. Multiple proposals for one destination decrement available slots/reservations in canonical order and recheck source, incumbent, material and demographic conditions after each accepted group. Resident skill counts/workforce history must transfer, not be destroyed as casualties or invented as newly trained specialists. Because slots, tools and skill matching create thresholds, the decision may not be monotone in migrant count.

The physically realizable productivity of specialized work must affect the forecast itself (food, clothing, tools, inputs, housing construction or saleable production). Supporting physical technology and infrastructure can reduce crowding/disease mortality through the already defined mitigation hooks; future employment does **not** reduce death hazard by fiat. Higher education, institutions or civic infrastructure may also mitigate the social-cohesion penalty only through explicit, funded mechanics. Without real health and supply improvements, an overcrowded high-density city remains unattractive even if it advertises jobs.

## 4. One relocation score, multiple destination types

For the particular transferring cohort:

\[
\Delta U_m=U_{B\mid X_1}(m)-U_{A\mid X_0}(m)-K_{\mathrm{private}}(m,\mathrm{distance}).
\]

For residents staying at the source and incumbents at an existing destination:

\[
\Delta U_A=U_{A\mid X_1}-U_{A\mid X_0},\qquad
\Delta U_{B,\mathrm{inc}}=U_{B\mid X_1}-U_{B\mid X_0}.
\]

The aggregate benefit is computed from **real, physically funded outcomes**, not advertised employment scores:

\[
\Delta\bar W=\frac{\int_0^T\sum_i N_i^{X_1}Q_i^{X_1}\,dt
-\int_0^T\sum_i N_i^{X_0}Q_i^{X_0}\,dt
+V_{\mathrm{verified\ surplus}}-K_{\mathrm{real}}}
{T\,[N_A(0)+N_B(0)]}.
\]

`V_external_surplus` is **zero until genuine outside demand and completed/supported trade** exist; do not double-count locally consumed cloth, food, actual wages, or the already counted QoL benefit. `K_real` includes physically funded transport/founding/resource costs once, not also subtracted a second time if already represented by reduced stocks/production.

The same acceptance criteria apply for immigration, healthy fission and distress fission:

\[
\boxed{\Delta U_m\geq\theta_U,\quad \Delta\bar W\geq\theta_W,\quad
\Delta U_A\geq-\epsilon_A,\quad \Delta U_{B,\mathrm{inc}}\geq-\epsilon_B}
\]

The final incumbent term is omitted for a newly founded daughter. Physical legality, known sites, founding capital, real carried provisions, viable workforce, and complete transfer conservation remain mandatory. **There is no universal `net growth >= 0` admission veto**: a recoverable settlement slightly above demographic equilibrium may temporarily decline while still being a good destination. Use projected actual risk and sustained supplies, not the sign of one instantaneous net rate.

The *only* distinction between success fission and distress fission is the source conditions in the diagnostic record. The same utility and aggregate counterfactual apply. A locally useful **fission preselection pressure** can reduce unnecessary site searches when there is no crowding, supply deficit, or founding opportunity. It must not become a separate QoL-ratio law with a discontinuity at full nutrition or a magical bonus for founding. The earlier `Π>=0.8` candidate is a **research setting, not an approved constant**. Distress fission stays eligible, including when the parent is poor and only a physically supportable new site offers relief.

Use existing `Encourage/Neutral/Discourage` policies to modulate *costs/required advantage*, within calibrated tolerances; do not let them override population, inventory or founding accounting.

## 5. Decision stability and computational constraints

**Immediate horizon is not sufficient on its own.** The prospective utility uses a bounded physical time horizon, while later loss of trained workers, disappearing jobs, exhaustion of inputs and growing density can reverse a nominally beneficial move. Native acceptance must be evaluated against forecast uncertainty; material sources not committed/observable should not be treated as guaranteed. Retain a cheap, conservative medium-term stability indicator, ideally derived from cached service trends and demographic equilibrium, without replaying the full economy or physical integrator separately for every candidate. The 60/120-year tests below demonstrate sensitivity, not a ready-made universal native threshold.

**Group-size acceptance is not monotone under job matching.** In the bounded toy grid, 62 of 576 configurations have an allowed group size followed by a disallowed larger size **within 2.5–10% of source population**; 145 of 576 exhibit this through 20%. Do not use `Immigration.Resolve`'s present monotone bisection once a nonmonotone provider is active. Instead score a deterministic bounded shortlist of sizes (physical minimum, existing 10% maximum, 2.5/5/7.5/10% candidate fractions, vacancy-fill candidates when implemented) and revalidate after pending acceptances. If 10% remains the current maximum, never silently enlarge it as part of this refactor.

**Time and scale:** one committed 20-year demographic resolution, and one social close pass; the per-candidate forecast is a pure, bounded, cached economic *estimate*, not a new 20-year simulation per destination. Aim for `O(camps + candidate contacts × bounded skill dimensions)` alongside existing cached fission site search. Keep canonical tie-breaking and exactly conserved transfers. Do not use all-camp all-pairs scanning, new seasonal loops or additional settlement ODE resolves within social candidate selection.

**Hysteresis:** preserve return memory and model job loss/contract stability. Do not react to one-turn forecast noise by reversing entire worker cohorts. But do not prevent legitimate moves back home through a permanent hard ban. The deadband/movement cost also prevents arbitrarily small positive opportunity noise from triggering mass movement.

## 6. Synthetic extensibility sweeps completed

The companion scripts `labor_market_extensibility.py`, `targeted_specialist_tests.py`, and `group_size_sensitivity.py` use the current branch's published annual demographic coefficients and the Neolithic QoL/cohesion structure. They **replace the game economy with an explicitly labeled labor/resource toy**: seven-tenths resident labor participation, one-time specialist allocation rather than simultaneous double employment, finite goods production caps, bounded funded positions, skill transfer, actual housing capacity and exogenous future mitigation/institution multipliers. These multipliers represent already-built future capacity in the synthetic scenario; they are not free technologies to enable now.

### Tests and results

| Synthetic test | Evidence | Interpretation |
|---|---:|---|
| Random matched candidate grid: 750 cases × 5 job weights | 3,750 comparisons; 17–18 accepted at tested weights | Job preference alone rarely overcomes real welfare constraints; no need to adjust Neolithic thresholds solely to enable future jobs |
| Physical vacancy accountability | 11/11 fundamental fixtures | No employment bonus for unfunded, tool-less, input-less, unavailable or skill-mismatched vacancies; people, specialists and portable shelter conserved |
| Targeted funded-vacancy comparison | 576 paired states; 0 accepted with jobs disabled, 34 with funded jobs but no job preference, 164 with funded jobs and illustrative `w_J=0.6` | Actual output is capable of inducing city-bound migration; the preference channel can add incremental selection without bypassing aggregate welfare |
| Attraction to initially dense receiver | 99 / 164 funded-job accepted cases started at 1,100+ residents; 48 / 164 at 1,500+ | The rule does not impose an artificial Neolithic city-size cap |
| Fake paper vacancies | 0 extra acceptances against otherwise identical unemployed cases | Unfunded demand cannot generate migration pull |
| Health infrastructure sensitivity | 109 / 164 accepts with population hazard multiplier 0.3; 48 with 0.6; 7 with 1.0 | Large city attraction usually requires real crowding/disease mitigation alongside productivity; do not implement mitigation by job bonus |
| Job productivity 20% weaker than forecast | 16 / 164 would no longer satisfy the immediate acceptance rule; one would have negative aggregate benefit | Lower-confidence or lower-bound service forecasts are required |
| Conservative 20% lower-bound rescore | 148 / 164 still accepted; the negative-welfare case was excluded | Robustness check works on this selected synthetic test only, not a general safety proof |
| Matched 12-turn two-settlement trajectories | 58 pairs; 44 had migration; 43 improved cumulative welfare, 1 worsened it; no rapid pair reversals | Short-horizon acceptance does not by itself guarantee long-run welfare or absence of drains |
| Extra medium-horizon counterfactual screening | 120-year diagnostic retains movement in 43 of 58 and eliminates the one negative-welfare case; 60-year diagnostic does not | Establish a cheap stability test and validate its horizon, not a hard-coded six-turn economic replay |
| Group-size monotonicity | 62/576 nonmonotone within 10%, 145/576 within 20% | The current bisection admission algorithm requires review before any live vacancy channel |

**These counts reflect constructed synthetic scenarios, not an unbiased rate of native gameplay or statistical confidence interval.** The 576-case grid was designed to probe cities with weak general clothing productivity and credible specialist capacity, so it does *not* estimate how often Neolithic migration ought to happen. The paired 12-turn test excludes fission and external market clearing. The prior 48-case unified fission suite exercised distress/success fission but had zero immigration in those particular coupled episodes. **There is not yet one validated native closed-loop all-modes test of fission + immigration + funded job attraction.**

The important falsification result is the combination: (i) benefit with real labor matches; (ii) no job-pull for fake demand; (iii) no size cap; (iv) positive counterfactual production effect; (v) no claim of long-horizon safety without a robustness check. Results support extensibility, **not immediate activation of a jobs feature**.

## 7. Implementation contract and staged gates

**Phase A — now, on a clean child branch of latest verified demography work:**

1. Add `SocialRelocationEvaluator` with a pure common counterfactual for immigration, crowding fission and distress fission. Keep the close-phase order and source/destination availability, event chronology, known-site/contact restrictions, fission policy size and thresholds until the same-physics native calibration supports changes.
2. Replace `Fission.QoLModel.ForwardAt` as a candidate decision authority with a cached **sustainable physical-service envelope**. It must not interpret last-turn paid food/textiles as new supply, and must incorporate labor removed, contingent worker arrivals, already committed stocks/trade, fixed/portable shelter and existing demographic rates.
3. Integrate with the existing v1.1 `PhysicalDemography` APIs. No replacement birth/mortality equations, population caps, repeated economy or physical ODE resolution per candidate. Keep starvation history, skills/workforce, stocks, typed food, domestic animals and fractional ledgers conserved. Preserve reported experienced QoL and RL reward initially.
4. Introduce an explicit `IEmploymentOpportunityProvider` (or similarly bounded pure interface) with a single **neutral/zero** implementation and a future typed opportunity-record contract; do not add active vacancies, recruitment constants or advertised jobs now. Require bit-identical decisions with provider absent vs neutral on deterministic fixtures, and unchanged save/load compatibility. Future skill-specific records must remain hidden until they are actually observable.
5. Keep economic/gameplay flags and event records version-aware. Count selected mover/source/incumbent/welfare changes, predicted hazards, forecast confidence, destination tool/food/housing headroom, fission triggers, actual subsequent services and loop/reversal metrics. Do not characterize migrations as deaths.

**Phase B — future release, not authorized in this refactor:** implement native specialist records and eligibility; fundable vacancy reconciliation; material/market demand, employer budgets, tools and working facilities; job matching and transfers; actual productive outputs and trade/surplus recognition; and appropriately financed mortality/institutional mitigations. At that point enable and recalibrate `w_J`, leaving v0.2 baseline unchanged until the future tests pass.

### Native GO/NO-GO matrix before MLP training

- **Backward parity:** demographic hazards, paid person-years, births/deaths, source/receiver/fission people, inventory, animals, specialists/workforce and save replay all conserved; no additional first-party demographic updates. The neutral opportunity provider introduces *zero* new behavior.
- **Social behavior:** a healthy growing settlement does not fragment prematurely; a prepared saturated settlement can fission; a distressed settlement may fission into a genuinely viable site; empty shelters or fictitious goods do not create welfare or viability; migration can be beneficial but not profitable solely due to accounting errors. No categorical `net rate < 0` veto.
- **Native matched cases:** same saved boundaries for no movement / immigration only / fission only / both; forced-versus-suppressed transfer causality over >=3 subsequent turns with actual food, clothing, tools, housing, mortality, labor loss, job potential (zero), QoL, aggregate welfare and parent/daughter trajectories. Include 80/320 camps and long-run high-density cases.
- **Future compatibility:** inject synthetic *read-only* test opportunity providers with zero, funded, unfunded, missing-input, missing-tool, wrong-skill and filled-vacancy cases. Verify score monotonicity in verified productive benefit at fixed conditions, declining premium as slots fill, nonmonotonic group-size handling and stable reservation reconciliation. Do not make these a world-generation feature.
- **Forecast robustness:** compare nominal and conservative service forecasts, incl. 20%/40% supply misses and delayed supply failures. Benchmark a cheap longer-horizon risk indicator against full native control/forced-move continuations without introducing costly per-candidate game simulations. Track false positive moves and abrupt service shortages.
- **Performance:** profile total social pass and expensive candidate evaluation, not only mathematics. Use cached, bounded per-settlement projections, with no per-candidate ODE/economy re-solves. Report p50/p95 whole-turn cost, allocations, cache hit rate, and 80/320/mature world scaling versus pinned demographics branch.
- **RL:** do not retrain or regenerate the MLP snapshot bank until the native demographic and social-flow gates pass. Do not change the existing Elder reward or observation schema solely to add a disabled future employment hook.

**Status: GO for implementing the unified, neutral-job-compatible architecture behind a feature branch; NO-GO for activating job demand or claiming death-loop elimination in native Anthropocene until the paired tests pass.**

## Evidence, reproduction and reference links

Execute from this folder using Python 3.11+ with installed pandas:

```sh
python labor_market_extensibility.py --out evidence
python targeted_specialist_tests.py
python group_size_sensitivity.py
```

Output: `evidence/job_demand_sensitivity.csv`, `job_demand_summary.json`, `paired_funded_job_cases.csv`, `paired_job_summary.json`, `specialist_pair_longitudinal.csv`, `specialist_pair_longitudinal_summary.json`, `group_size_nonmonotonic.csv`, and `group_size_summary.json`. `job_demand_forecast_stress.csv` reports the first random stress matrix.

Primary sources: [Fission.cs](https://github.com/GeeseGaggle-211/Anthropocene/blob/bcf7f2147fb476b87a976f6c6d4400b5f02ba894/Assets/_Anthropocene/Simulation/Fission.cs), [Immigration.cs](https://github.com/GeeseGaggle-211/Anthropocene/blob/bcf7f2147fb476b87a976f6c6d4400b5f02ba894/Assets/_Anthropocene/Simulation/Immigration.cs), [Demography.cs](https://github.com/GeeseGaggle-211/Anthropocene/blob/bcf7f2147fb476b87a976f6c6d4400b5f02ba894/Assets/_Anthropocene/Simulation/Demography.cs), [NeolithicProduction.cs](https://github.com/GeeseGaggle-211/Anthropocene/blob/bcf7f2147fb476b87a976f6c6d4400b5f02ba894/Assets/_Anthropocene/Simulation/NeolithicProduction.cs), [DemographyQoLValidation-v1.1.md](https://github.com/GeeseGaggle-211/Anthropocene/blob/bcf7f2147fb476b87a976f6c6d4400b5f02ba894/Docs/DemographyQoLValidation-v1.1.md). External conceptual background, **not a gameplay calibration**: Duranton & Puga (2004), *Micro-Foundations of Urban Agglomeration Economies*, [NBER w9931](https://www.nber.org/papers/w9931), summarizing sharing, matching and learning mechanisms.
