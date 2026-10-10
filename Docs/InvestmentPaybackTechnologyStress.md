# Technology-dependent capital-effort stress test

**Measured 10 October 2026.** `H=800 years` remains economically meaningful,
but its advantage over `H=400 years` is concentrated rather than universal.
Against undiscounted parent-local welfare over 1,600 years, `H=800` reduced
wrong investment-timing choices from **8/108 (7.4%) to 2/108 (1.9%)** and mean
long-run regret from **155.552 to 7.539 person-QoL-years**, a 95.2% reduction.
It was strictly better in six cases, equal in 102, and never worse. All six
improvements were year-400 unlocks in one `q=0.50` opening state; `H=800` still
missed two profitable, delayed fishing-only responses in a `q=0.65` state.

The recommendation is therefore to use **`H=800`, `rho=0.00125 year^-1`** as
the long-horizon candidate/evaluator when material capital-cost changes within
the planning horizon are plausible. Retain **`H=400`, `rho=0.0025 year^-1`** as
the shorter training/curriculum objective, but not as the sole timing selector
for known technology transitions. This is not evidence that every policy needs
an 800-year horizon: 94.4% of timing choices were identical, and the result is
based on a bounded three-world counterfactual slice rather than held-out
validation.

## Change record, separate from measured results

The production simulator gained an explicitly experimental configuration seam;
its shipped behavior did not change:

- `NeolithicConfig` now has independent
  `CultivationMarginalEffortKneeQ` and `FishingMarginalEffortKneeQ` values,
  both defaulting to `0.4`, validated in `(0,1]`, and initialized for old saves
  ([configuration](../Assets/_Anthropocene/Simulation/NeolithicConfig.cs#L24),
  [migration and validation](../Assets/_Anthropocene/Simulation/NeolithicConfig.cs#L78)).
- `OpeningConfig.json` explicitly records the two shipped `0.4` values
  ([source](../Assets/_Anthropocene/Resources/OpeningConfig.json#L22)). No
  production technology tier, production coefficient, ecological equation,
  demographic rule, fission rule, or reward objective changed.
- `NeolithicCapital` generalizes the labor transform and inverse while leaving
  physical `E`, `q(E)`, and decay unchanged
  ([source](../Assets/_Anthropocene/Simulation/CultivationEconomy.cs#L81)).
  Field callers use the cultivation knee; fishery maintenance, build, and
  inverse allocation use the fishing knee
  ([field costs](../Assets/_Anthropocene/Simulation/CultivationEconomy.cs#L343),
  [fishery costs](../Assets/_Anthropocene/Simulation/FishingEconomy.cs#L346)).
- The separate C# stress harness, runner, and plotting reducer are under
  [`Tools/InvestmentPayback`](../Tools/InvestmentPayback/README.md). They do not
  add a game technology tree or a fishing action head. Python groups and plots
  C# ledgers; it does not reimplement production or welfare economics.

All `q_k=0.5/0.8` cases below are labeled experimental counterfactuals. A
changed knee applies a new method to **future** build and maintenance work on
the existing physical asset. It does not create capital, recompute `q`, or
refund historical construction labor.

## Capital-effort model audit

The physical capital map remains

```text
q(E) = min(1, R sqrt(max(0,E))),       R = 0.4.
```

Let `m=16` be the shipped marginal-effort multiplier per `0.1 q`,
`k=ln(m)/0.1`, and `E_k=(q_k/R)^2`. The experimental work coordinate is

```text
W(E;q_k) = E,                                           E <= E_k
         = E_k + 2 q_k/(R^2 k) [exp(k(q(E)-q_k))-1],   E > E_k.
```

At the junction, `W(E_k)=E_k` and both derivatives with respect to `E` are
one. The analytical inverse is

```text
q = q_k + ln(1 + (W-E_k) R^2 k/(2 q_k))/k
E = (min(1,q)/R)^2.
```

The `q_k=R=0.4` branch retains the legacy algebra and floating-point evaluation
order exactly ([implementation](../Assets/_Anthropocene/Simulation/CultivationEconomy.cs#L89)).
Focused tests cover value and first-derivative continuity at `0.4/0.5/0.8`,
inverse round trips through `q=1`, bit-exact legacy values, invariant stored
capital and `q(E)`, and independent field/fishery knees
([checks](../Tools/Headless/HorizonCultivationChecks.cs#L216)).

Dependent calculations use the same coordinate consistently:

- field maintenance charges terrain coefficient times `W(E)`; expansion room,
  marginal steps, build debits, and planning scores use `W(E_2)-W(E_1)` and
  its inverse ([maintenance](../Assets/_Anthropocene/Simulation/CultivationEconomy.cs#L343),
  [expansion](../Assets/_Anthropocene/Simulation/CultivationEconomy.cs#L700));
- fishery service first pays maintenance proportional to `W(E)`, physical
  decay remains unchanged, and residual work becomes capital through the same
  inverse ([source](../Assets/_Anthropocene/Simulation/FishingEconomy.cs#L369));
- husbandry remains a biological herd stock with the existing management,
  forage, freshwater, birth, starvation, and slaughter equations. No
  husbandry-efficiency counterfactual was introduced.

The shipped decision remains 7,300 days / 365 days per year = **20 years**
([configuration](../Assets/_Anthropocene/Resources/OpeningConfig.json#L91)).
The Elder reward remains prior-parent population times endpoint QoL times
elapsed years ([source](../Assets/_Anthropocene/Simulation/ElderEnvironment.cs#L320)).
The harness records the unnormalized rate `N_prior * Q_endpoint` and integrates
the treatment-minus-matched-baseline rate over each exact 20-year interval.

For finite e-fold `H`, C# applies

```text
weight(t0,t1;H) = H [exp(-t0/H) - exp(-t1/H)].
```

Thus `H=400` has `rho=0.0025/year`, `gamma_20=0.951229`; `H=800` has
`rho=0.00125/year`, `gamma_20=0.975310`. The primary comparator is the same
physical trajectory integrated without discount through year 1,600.

## Experimental design

### Matched opening states

Three disclosed exploratory seeds (`61169`, `74843`, `91781`) generate
independent `128 x 64` production worlds. For each world, the harness selects
the median joint-rank legal coastal site by terrestrial and aquatic renewal,
then constructs three otherwise matched camps at opening field and fishery
`q=0.38`, `0.50`, and `0.65` ([state construction](../Tools/InvestmentPayback/TechnologyStress.cs#L392)).
The nine states therefore cover just below, moderately above, and well above
the current `q=0.4` knee. Each has population 320, two turns of opening food
reserve, mature mixed-subsistence capabilities, the same equipment/housing
rules, field operation one, and a 30-head herd.

This deliberately bounded stress slice varies geography/seed and opening
capital, not population, reserve, or ecological quantile. The earlier matched
`L9` sweep covers low/medium/high population, reserves, and joint support; the
present extension isolates the new cost-regime and timing interactions. The
seeds are disclosed calibration replications, not consumed regression seeds
presented as unseen validation.

### Scenarios and actions

There are 19 scenarios:

| Mode | Cases | Meaning |
| --- | ---: | --- |
| Static control | 1 | shared `q_k=0.4` from opening |
| Static counterfactual | 6 | `q_k=0.5/0.8` x cultivation-only, fishing-only, shared from opening |
| Scheduled counterfactual | 12 | unlock year 400/800 x `q_k=0.5/0.8` x the same three scopes |

Every state/scenario has a no-Development baseline and an invest-now arm.
Scheduled cases also have invest-after-known and
invest-after-unannounced/revealed-at-unlock arms. Investment is ten consecutive
20-year Elder Development decisions—**200 years**, not a one-turn pulse—with
requested budget `0.60`, retained field operation `1`, retained herd fraction
`1`, field-development share `1`, and no movement.

Each decision passes through the real `ElderObservationBuilder`,
`NativeElderDirectiveCompiler`, `TurnPlanner.PrepareWithElderDirectives`, and
`TurnResolver` path ([intervention](../Tools/InvestmentPayback/TechnologyStress.cs#L599)).
Compiler clipping is retained and recorded. Background AI orders and all
exogenous configuration other than the named `q_k` schedule are matched.

Fishing remains an environmental/deterministic response. The field-directed
Development head can alter labor and settlement state, while ordinary fishing
staffing, catch, maintenance, decay, and construction react through production
code. A fishing-only `q_k` scenario changes that response's labor curve; it
does not pretend the Elder directly ordered a fishery investment.

The unlock schedule lives only in the harness. It is applied at the physical
turn boundary, after which the current observation is rebuilt. It is never
placed in `WorldState` or the 512-feature Elder tensor
([non-leak boundary](../Tools/InvestmentPayback/TechnologyStress.cs#L619),
[transition](../Tools/InvestmentPayback/TechnologyStress.cs#L638)).

### Recorded variables

The per-turn ledger records requested/accepted/realized Development budgets and
people; farmers, fishers, and gatherers; `N_prior`, endpoint population, QoL,
`NQ`, and interval welfare; total, field, fish, and gathered production;
reserves; field/fishery `E` and `q`; field/fishery build and maintenance
worker-years; land/aquatic food, biomass, and condition; fissions, daughter
population, and camp count; observation and state fingerprints; and active
field/fishery knees
([schema](../Tools/InvestmentPayback/TechnologyStress.cs#L1273)).

Payoff analysis pairs every treatment with its state/scenario baseline and
computes discount values, undiscounted value, cumulative drawdown, durable
break-even and durable-positive onset, physical work, terminal capital, ecology
deltas, and fission deltas in C#
([analysis](../Tools/InvestmentPayback/TechnologyStress.cs#L942)). Timing compares
invest now, invest after, and no Development, then scores the discount-selected
choice by its undiscounted 1,600-year regret
([timing](../Tools/InvestmentPayback/TechnologyStress.cs#L1084)).

## Measured results

The full run produced **9 states, 19 scenarios, 558 trajectories, and 44,640
production turns** in 471.0 seconds using four bounded trajectory jobs.

### Investment timing and regret

Undiscounted 1,600-year welfare selected invest now in 12 cases, invest after
in 50, and no Development in 46.

| Evaluator | Timing inversions | Mean 1,600y regret | Median regret | Selected now / after / none |
| --- | ---: | ---: | ---: | ---: |
| `H=400` | 8/108 (7.4%) | 155.552 | 0 | 12 / 42 / 54 |
| `H=800` | 2/108 (1.9%) | 7.539 | 0 | 12 / 48 / 48 |

`H=800` had strictly lower regret in six cases, equal regret in 102, and higher
regret in none. The six corrected cases were both unlock severities and all
three scopes for state `T02-Q050` at year 400. In each, invest-after had
`+2664.240` undiscounted welfare; `H=400` valued it at `-14.258` and selected no
Development, while `H=800` valued it at `+405.592` and selected it. Invest now
was strongly negative in all six.

The two remaining `H=800` inversions were `T01-Q065`, year-400,
fishing-only, for both `q_k=0.5` and `0.8`. Invest-after improved undiscounted
welfare by `+407.082`, but its values were `-321.202` at `H=400` and `-367.772`
at `H=800`; both selected no Development. This is a delayed/tail response for
which merely doubling the e-fold was insufficient.

| Sensitivity slice | Cases | `H=400` inversions | `H=800` inversions | Mean regret, 400 -> 800 |
| --- | ---: | ---: | ---: | ---: |
| Cultivation-only | 36 | 2 | 0 | 148.013 -> 0 |
| Fishing-only | 36 | 4 | 2 | 170.629 -> 22.616 |
| Shared | 36 | 2 | 0 | 148.013 -> 0 |
| Unlock year 400 | 54 | 8 | 2 | 311.104 -> 15.077 |
| Unlock year 800 | 54 | 0 | 0 | 0 -> 0 |
| Opening `q=0.38` | 36 | 0 | 0 | 0 -> 0 |
| Opening `q=0.50` | 36 | 6 | 0 | 444.040 -> 0 |
| Opening `q=0.65` | 36 | 2 | 2 | 22.616 -> 22.616 |

Moderate and severe knees had identical timing-error counts: 4 -> 1 for each
severity. This does not mean their physics were identical; it means the named
timing choices fell on the same side of zero in this bounded sample.

Across the 279 unique physical investments (discarding the deliberately
duplicated known/unannounced after arm), 105 improved undiscounted welfare.
`H=400` classified 233/279 signs correctly with 46 false negatives and no false
positives; `H=800` classified 243/279 correctly with 36 false negatives and no
false positives. The longer horizon recovered ten additional long-run-positive
investments without creating a short-run-positive/long-run-nonpositive error.

![Discount-selected timing regret](../TestResults/InvestmentPaybackTechnologyStress/plots/discount_timing_regret.png)

### Payback and optimal delay

Durable break-even is measured from the first Development decision. A zero
means cumulative incremental welfare never went negative; durable-positive
onset is reported separately because reserve buffers can make break-even zero
before any strictly positive welfare difference appears.

| Arm and unlock | Domain | Positive cases | Median durable break-even | Median durable-positive | Maximum positive onset |
| --- | --- | ---: | ---: | ---: | ---: |
| Invest now, y400 | Cultivation / shared | 6/18 each | 1120y | 1120y | 1220y |
| Invest now, y400 | Fishing | 6/18 | 1080y | 1080y | 1300y |
| Invest now, y800 | All scopes | 6/18 each | 1080y | 1080y | 1300y |
| Invest after, y400 | Cultivation / shared | 8/18 each | 310y | 310y | 540y |
| Invest after, y400 | Fishing | 10/18 | 520y | 520y | 1040y |
| Invest after, y800 | Cultivation / shared | 8/18 each | 0y | 240y | 240y |
| Invest after, y800 | Fishing | 8/18 | 0y | 280y | 500y |

All positive cases eventually paid back inside the 1,600-year run. Immediate
investment commonly needed about 1,100 years; waiting until the lower-cost
regime shortened the positive-onset delay substantially. Fishing retained the
longest delayed response, especially after a year-400 unlock. Static invest-now
positive cases had median positive onsets of 900 years for cultivation, 1,080
for fishing, and 930 for shared counterfactuals.

![Durable payoff duration](../TestResults/InvestmentPaybackTechnologyStress/plots/payback_duration.png)

### Physical capital, labor, and production

The compiler did not grant the requested 0.60 budget unconditionally. Across
active decisions, invest-now median accepted/realized budget was 0.449 for
cultivation, 0.313 for fishing-only, and 0.438 for shared scenarios; median
accepted people were 192, 174, and 192, respectively. Median realized field
build was 3,840, 3,480, and 3,840 worker-years per active turn. Invest-after
medians were 0.463/420 people/8,390 worker-years for cultivation and shared,
and 0.448/356 people/7,120 worker-years for fishing-only. Median remaining
gatherers during active invest-now decisions were zero; after-unlock medians
were 234.5 for cultivation/shared and 7 for fishing-only. These are realized
compiler allocations, not assumed labor pulses.

At year 1,600, treatment-minus-baseline medians for invest-after were:

| Scope | field `Delta q` / `Delta E` | fishery `Delta q` / `Delta E` | field build | field maintenance |
| --- | ---: | ---: | ---: | ---: |
| Cultivation-only | +0.161 / +4.674 | -0.0010 / -0.0057 | +86.6k WY | +83.2k WY |
| Fishing-only | +0.0152 / +0.488 | -0.0058 / -0.0475 | +77.6k WY | +85.1k WY |
| Shared | +0.161 / +4.674 | -0.0010 / -0.0057 | +86.6k WY | +83.2k WY |

For cultivation/shared invest-after, the median terminal field response was
`Delta q=+0.074` under `q_k=0.5` and `+0.187` under `q_k=0.8`. More efficient
construction therefore produced more physical capital, rather than merely
reducing labor. Realized field maintenance could consequently rise: at a
year-400 cultivation unlock the median incremental maintenance was 119.1k
worker-years for `q_k=0.5` and 120.9k for `q_k=0.8`; at year 800 it was 78.1k
and 79.4k. The policy expanded the maintained asset farther under the severe
counterfactual.

Fishing-only results must be read as the deterministic production response to
the field-directed Elder action. Median direct fishery build was near zero,
and final fishery capital was slightly below baseline as labor, catch, and
maintenance paths reallocated. The test establishes that independent fishery
cost plumbing works and can change welfare timing; it does not identify an
optimal direct fishery action that the current Elder interface does not have.

Median cumulative production deltas across scheduled cases were:

| Arm | Scope | Total food | Field food | Fish food |
| --- | --- | ---: | ---: | ---: |
| Invest now | Cultivation | -22.0k | +203.3k | -11.3k |
| Invest now | Fishing | -21.2k | +201.8k | -19.2k |
| Invest now | Shared | -22.0k | +201.0k | -11.3k |
| Invest after | Cultivation | approximately 0 | +755.1k | -6.8k |
| Invest after | Fishing | +4.2k | +167.4k | -10.5k |
| Invest after | Shared | approximately 0 | +755.1k | -6.8k |

Total food also contains gathering, husbandry, slaughter, and other ordinary
channels, so it is not expected to equal the two displayed domain columns.
Large field output gains did not automatically become welfare gains: labor,
reserve timing, population, fission, and the endpoint reward boundary all
remained active.

![Severe shared capital paths](../TestResults/InvestmentPaybackTechnologyStress/plots/severe_shared_capital_paths.png)

![Severe shared maintenance paths](../TestResults/InvestmentPaybackTechnologyStress/plots/severe_shared_maintenance_paths.png)

### Reserves, fission, and ecology

Invest-now created a median worst reserve deficit of roughly 32,000 food units
relative to baseline, with a worst case of -55,294. Median terminal reserve was
-76 for cultivation/shared and approximately zero for fishing-only. For
invest-after, median worst deficits were -2,472 for cultivation/shared and
-3,618 for fishing-only; median terminal reserve deltas were +232 and +464.
Worst terminal cases were -600, -240, and -120 for cultivation, shared, and
fishing, respectively.

Fission remained enabled. Invest-now changed total fission count in 12/36
cases per scope (median zero, maximum +2) and changed cumulative daughter
population in every case (median about -3; range -40 to +481). Invest-after did
not change fission count; 2/36 cases per scope changed daughter population,
with a minimum of -17. Daughter welfare remains outside the reviewed
parent-local Elder reward, but the side effects are recorded.

Land condition stayed exactly `1.0` in every recorded state and arm. Absolute
aquatic condition never fell below `0.9835` in baselines or `0.9883` in
treatments; terminal minima were `0.9884` and `0.9892`, respectively. Relative
to matched baselines, the worst invest-after aquatic-condition deltas were
-0.0025 for cultivation-only and -0.0008 for fishing-only/shared. No ecological
condition collapse occurred in these fixtures. This is bounded evidence only,
not a claim of global or indefinite sustainability.

![Ecological condition paths](../TestResults/InvestmentPaybackTechnologyStress/plots/severe_shared_ecology_paths.png)

### Static controls

The first 200 years of static invest-now controls confirm that the new cost
regimes were materially exercised:

| Static regime | Median field build | Median field maintenance | Median maximum field `q` | Median maximum fishery `q` |
| --- | ---: | ---: | ---: | ---: |
| shared `q_k=0.4` | 39.1k WY | 55.6k WY | 0.516 | 0.472 |
| cultivation/shared `q_k=0.5` | 39.4k WY | 26.5k WY | 0.563 | 0.472 |
| cultivation/shared `q_k=0.8` | 42.6k WY | 26.6k WY | 0.813 | 0.472 |

The severe counterfactual made the same Development interface more physically
effective, but not uniformly welfare-positive. Static `q_k=0.8`
cultivation/shared investment improved 1,600-year welfare in only 2/9 states,
with median values `-41.6k/-42.3k`; `q_k=0.5` improved 3/9, with median zero.
These are endogenous labor/reserve/demographic outcomes, not an ecological
collapse or a statement that higher technical efficiency is intrinsically
harmful.

## Known versus unannounced unlocks

The harness made 432 opening-observation comparisons and found no future
schedule leakage. It also made 432 bit-level stored-capital checks at static or
scheduled configuration boundaries. The known-from-opening and
revealed-at-unlock after arms used the same action sequence once the event
occurred and were bit-exact in all 108 matched pairs, including per-turn
signatures and final fingerprints.

This separates two conclusions:

- The measured physical benefit/cost of reacting with sustained Development
  **after an observed unlock** does not require foresight.
- Choosing at year zero to defer specifically until a known year-400/year-800
  unlock, and therefore the reported invest-now versus invest-after ranking,
  **does require exogenous technological foresight**. The current Elder
  observation does not supply it. An initially unannounced Elder that invests
  now cannot retrospectively choose the waiting arm.

The `H=800` timing advantage is therefore evidence for planning/evaluation when
such a transition is known or represented by a belief model, not evidence that
the current observation can predict unannounced technology.

## Sensitivity, assumptions, and limitations

- Unlock timing dominates the headline result: all eight `H=400` errors and
  both residual `H=800` errors occur at year 400; both horizons make the same
  choices for every year-800 unlock in this design.
- Opening capital matters non-monotonically: all six `H=800` corrections are at
  `q=0.50`; the two residual errors are at `q=0.65`; neither horizon errs at
  `q=0.38`.
- Cultivation and shared scopes have identical timing classifications here
  because the direct Elder action is field-directed and the fishery response is
  small. Fishing-only is harder: it accounts for both residual `H=800` errors
  and longer post-unlock payback.
- Applying a new `q_k` to future upkeep of old capital is an explicit
  counterfactual assumption. It is not a historical labor refund, but it does
  reduce the next maintenance bill immediately at the unlock boundary.
- Population, reserve level, and support quantile are fixed in this extension.
  Three deterministic worlds and nine opening states describe sensitivity,
  not a population distribution. The earlier `L9` sweep remains the evidence
  for those other axes.
- The comparator is finite at 1,600 years and local to the prior parent. Tail
  welfare beyond year 1,600 and daughter welfare can change a social ranking.
- `N(t)Q(t)` remains a 20-year piecewise-constant representation using prior
  population and endpoint QoL. No within-turn demographic/QoL trace exists.
- No PPO was implemented, and no claim is made about optimization stability or
  sample efficiency at either discount rate.

## Recommendation

1. Carry **`H=800` / `rho=0.00125 year^-1`** as the preferred long-horizon
   Elder evaluator and candidate normative discount when known or probabilistic
   capital-cost transitions can occur within centuries. Its advantage is a
   large reduction in rare but costly delayed-payoff mistakes, not broad
   disagreement with `H=400`.
2. Keep **`H=400` / `rho=0.0025 year^-1`** as a shorter training/curriculum
   objective and report both values. Do not let it alone veto investments whose
   payoff begins several centuries after an early technology transition.
3. Keep the undiscounted 1,600-year score, regret, sign classification,
   domain-stratified payback, reserves, ecology, and fission sidecars as
   evaluation gates. `H=800` still has two known fishing false negatives and 36
   sign false negatives among 105 long-run-positive physical investments.
4. Do not expose a scheduled unlock to the Elder unless the training concept
   intentionally models technological foresight. For unannounced cases, expose
   only the realized current cost regime and evaluate post-event adaptation.

## Reproduction and artifacts

From the repository root:

```bash
# Full 1,600-year measurement and plots (about eight minutes on the audit host)
bash Tools/InvestmentPayback/Run-Technology-Stress.sh \
  TestResults/InvestmentPaybackTechnologyStress --verify --jobs 4

# Bounded deterministic smoke/replay check
bash Tools/InvestmentPayback/Run-Technology-Stress.sh \
  /tmp/investment-payback-technology-smoke \
  --quick --turns 6 --verify --jobs 4

# Focused production checks
.tools/dotnet/dotnet run -c Release \
  --project Tools/Headless/Anthropocene.Headless.csproj -- --horizon-only
.tools/dotnet/dotnet run -c Release \
  --project Tools/Headless/Anthropocene.Headless.csproj -- --fishing-economy-only
python3 Tools/Docs/Check-CurrentContract.py
```

The final verification was:

```text
PASS: curve continuity/differentiability/inverses;
432 opening future-schedule non-leak checks;
432 stored-capital invariants;
108 known/event-time matched pairs;
scalar/reversed-parallel replay=pass.
```

Raw/derived artifacts are intentionally ignored under
`TestResults/InvestmentPaybackTechnologyStress`: state and scenario ledgers,
the 44,640-row trajectory ledger, payoff and timing ledgers, source/config
manifest, verification record, aggregate CSVs, and SVG/PNG plots. The manifest
records simulator/harness/configuration hashes, dimensions, assumptions, and
runtime so the bounded result can be regenerated exactly.
