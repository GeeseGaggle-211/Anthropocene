# Village Elder physical investment-payback sweep

**Measured 10 October 2026.** The primary candidate from this bounded sweep is
an Elder welfare e-folding horizon of **400 years** (`rho = 0.0025 year^-1`,
`gamma_20 = exp(-20/400) = 0.951229`). Keep **200 years** (`rho = 0.005`,
`gamma_20 = 0.904837`) as a robustness/curriculum alternative. Do not use the
100-year candidate (`rho = 0.01`, `gamma_20 = 0.818731`) as the sole normative
training horizon: it missed 39 of 130 investments that improved the 800-year
undiscounted comparator and reversed 108 of 352 comparable positive-investment
pairs.

This is a candidate selection, not final calibration. The reference is an
800-year finite continuation, not an infinite-horizon proof, and only three
disclosed exploratory geographies were used. An undiscounted long-continuation
evaluator should remain beside any discounted training objective.

## Pinned sources and scope

The normative source was the live Google Doc
[Anthropocene AI/RL Training Concept — Neolithic Curriculum and Scalable Agent Architecture](https://docs.google.com/document/d/1rZjIo7NMrxn3OQzGFHJI2QAZN7rauHgSCm-nbMNUCT0/edit?usp=drivesdk),
revision **54**, modified `2026-10-10T06:01:53.849Z`. Relevant requirements are
§20 (one 7,300-day decision, continuous-time discounting), §32–33 (local
prior-cohort welfare and post-fission boundary), §34.4 (resident fishing capital
and work), and §40 (100/200/400-year candidates plus controlled nonlinear
investment fixtures).

The simulator was pinned to commit
`5c3baf2397eb6c4f66b31bfe2ed45de6faaad31c` on the document's
`feature/elder-rl-scaffold` line, simulator identity
`world-7/generator-14/elder-1`. The shipped configuration SHA-256 was
`675e125af8f206b85674b7310c250b099540db4a3cb2a12df2e2d3f3641ecd33`;
the bounded experimental clone's configuration hash was
`016D26046A710A6A4240B4294F948AFD9226218A34433FDBEFC4CFB0DA67781F`.

No PPO was implemented. No production simulation source, ecological setting,
demographic rule, fission rule, or reward objective was changed. The three
seeds (`61169`, `74843`, `91781`) did not occur in the tracked repository before
this harness was added. They are disclosed exploratory replications, not
consumed regression seeds and not an unseen validation claim.

## Phase 1 — model audit

### Capital, effort, maintenance, and ecology

Fields and fisheries share one physical capital transform in
[`NeolithicCapital`](../Assets/_Anthropocene/Simulation/CultivationEconomy.cs#L81):

```text
q(E) = min(1, R sqrt(max(0,E))), R = 0.40
```

Effort is `W(E)=E` through `E=1` (`q=0.4`). Above that point it is the exact
exponential in
[`EffortForCapital`](../Assets/_Anthropocene/Simulation/CultivationEconomy.cs#L89),
with `k=ln(16)/0.1`; the inverse is
[`CapitalForEffort`](../Assets/_Anthropocene/Simulation/CultivationEconomy.cs#L110).
The shipped factor 16, 80-year decay half-life, terrain multipliers, and tool
service settings are in
[`NeolithicConfig`](../Assets/_Anthropocene/Simulation/NeolithicConfig.cs#L9)
and [`OpeningConfig.json`](../Assets/_Anthropocene/Resources/OpeningConfig.json#L2).

- Unoperated capital decays as
  `E exp[-ln(2)(1-operation) years / 80]`
  ([source](../Assets/_Anthropocene/Simulation/CultivationEconomy.cs#L121)).
- Base field build cost is
  `250 * 20 * 10 / 7 = 7142.857` worker-years per tile-E, multiplied by
  open/woodland/forest build cost. Base maintenance is
  `250 * 0.4 / 7 = 14.286` workers per tile times terrain maintenance and
  `W(E)` ([definitions](../Assets/_Anthropocene/Simulation/NeolithicConfig.cs#L69),
  [realized labor](../Assets/_Anthropocene/Simulation/CultivationEconomy.cs#L309)).
  Farm Tools cover 20 farmer-years each and multiply covered field work by
  `(0.45/0.40)^2 = 1.265625`.
- Expansion is one analytical horizon allocation. Its 32 intensity bands are
  capital resolution, not time steps
  ([source](../Assets/_Anthropocene/Simulation/CultivationEconomy.cs#L486));
  each accepted band pays the same build and maintenance equations
  ([source](../Assets/_Anthropocene/Simulation/CultivationEconomy.cs#L719)).
- Cultivation retains natural food first, exposes the remaining biomass as
  `q^2 * crop_suitability`, and adds managed renewal proportional to
  `0.65 * q * biomass * recovery * operation * suitability`
  ([source](../Assets/_Anthropocene/Simulation/EcologyMath.cs#L54)). The stock is
  integrated analytically over the horizon
  ([source](../Assets/_Anthropocene/Simulation/EcologyMath.cs#L181)).

Fishing requires Watercraft and a legal Shallows/Lake source
([source](../Assets/_Anthropocene/Simulation/FishingEconomy.cs#L317)). It calls
the same q, effort, inverse-effort, and decay functions
([source](../Assets/_Anthropocene/Simulation/FishingEconomy.cs#L346)). For each
ground, fisher service first pays maintenance, which determines operation and
decay; remaining service builds capital
([source](../Assets/_Anthropocene/Simulation/FishingEconomy.cs#L362)). The
ordinary plan also catches fish, then distributes unused service across known
grounds for maintenance/build
([source](../Assets/_Anthropocene/Simulation/FishingEconomy.cs#L524)). Fishery q
changes only human access to aquatic biomass (`q^2`) and managed renewal, not
the underlying biomass capacity
([source](../Assets/_Anthropocene/Simulation/LazyEcology.cs#L44)). Catch remains
bounded by actual projected stock, sustainable flow, distance, and rival share
([source](../Assets/_Anthropocene/Simulation/FishingEconomy.cs#L420)).

### Herd support and returns

Husbandry is a biological stock rather than q-capital.

- Herd claims use the radius-one local footprint, taking non-human forage
  before human-accessible food and sharing finite freshwater
  ([source](../Assets/_Anthropocene/Simulation/GenerationalEconomy.cs#L30)).
- Zero-displacement capacity is the lesser of non-human forage support and
  water support; absolute capacity may additionally consume human food
  ([source](../Assets/_Anthropocene/Simulation/SettlementEconomy.cs#L127)).
- Stone Herding Tools cover 20 herder-years and raise capacity from 2 to 2.69
  animals per herder. Fulfilment is managed capacity divided by herd size
  ([source](../Assets/_Anthropocene/Simulation/NeolithicProduction.cs#L192)).
- Animal output is
  `animals * output_per_animal * Consumption * intervals *
  nutrition^a * management^b`
  ([source](../Assets/_Anthropocene/Simulation/GenerationalEconomy.cs#L188)).
- Only fully nourished herds breed, with integer births from
  `floor((animals + remainder) exp(growth_rate * breeding * years))`;
  nutrition shortfalls can cause starvation, and animals above the chosen
  target are slaughtered after breeding
  ([resolution](../Assets/_Anthropocene/Simulation/GenerationalEconomy.cs#L514),
  [slaughter](../Assets/_Anthropocene/Simulation/HerdLabour.cs#L531)).

### Time and reward accounting

The shipped decision is `TurnDays=7300`, `DaysPerYear=365`: exactly 20 years
([configuration](../Assets/_Anthropocene/Resources/OpeningConfig.json#L88)).
`TurnHorizon` converts that once to 20 years and 80 legacy BaseDays intervals;
it does not choose 80 resolution passes
([source](../Assets/_Anthropocene/Simulation/TurnHorizon.cs#L5)). Four seasonal
profiles are forcing inputs reduced by bounded analytical integration.

QoL is resolved after physical needs as food coverage × water coverage × the
weighted clothing/housing service × social cohesion
([source](../Assets/_Anthropocene/Simulation/NeolithicProduction.cs#L1053)).
The Elder environment captures local resident population before the turn and
then emits:

```text
RawWelfareFlow = N_prior * Q_endpoint * elapsed_years
NormalizedWelfare = RawWelfareFlow / (N_initial * elapsed_years)
                  = N_prior * Q_endpoint / N_initial
```

([source](../Assets/_Anthropocene/Simulation/ElderEnvironment.cs#L320)). Thus
the current scalar is a normalized endpoint welfare **rate**. It does not apply
rho inside the simulator. Because all measured decisions are 20 years, the
omitted common interval factor cannot change treatment signs or within-state
rankings. The sweep records the unnormalized realized rate
`N_prior * Q_endpoint` and applies the normative continuous discount weights
outside turn resolution but inside the C# harness.

This is the simulator's available `N(t)Q(t)` representation: one endpoint QoL
held over each analytical 20-year interval, not an observed within-turn path.
Fission still runs in the production close phase
([source](../Assets/_Anthropocene/Simulation/TurnResolver.cs#L173)); the parent
trajectory excludes daughter welfare, matching the normative local boundary.

### Variables required for an incremental payoff

The harness records the variables needed to identify the physical cost,
mediators, and welfare return:

| Layer | Recorded variables |
| --- | --- |
| Matched state | seed, factor levels, site/tile IDs, field and fishery E/q, herd heads and support capacities, population, reserves, fingerprints, config hash |
| Investment cost | requested/accepted pulse people, displaced gatherers, role counts, field/fish build and maintenance worker-years, herder-years, tools implicit in authoritative inventory consumption |
| Production | total, field, fish, animal, slaughter, and gathered food; consumption and remaining reserves |
| Durable state | field/fish q and E, operation-mediated decay, herd heads, births, starvation, management fulfilment, target and breeding intensity |
| Ecology | land/aquatic food, biomass, condition, and herd displacement of human food |
| Welfare/demography | local `N_prior`, endpoint population, QoL, `N_prior Q`, interval welfare, fissions, daughter population and live-camp count |

## Phase 2 — controlled physical experiment

### Matched states

Each disclosed seed generated a `128 x 64` production world from the shipped
resource-generation configuration. The smaller map and one opening player are
computational-scale controls only; all physical, demographic, fission, reward,
and policy coefficients remained shipped values. All generated agents were
removed before placing one isolated mature mixed-subsistence camp, eliminating
unmatched competitors. Endogenous fission daughters remained enabled and used
the ordinary automatic policy.

Legal coastal sites were ranked by the geometric mean of their within-world
terrestrial- and aquatic-renewal percentile ranks. The 20th, 50th, and 80th
joint-rank sites define low/medium/high ecological support. This is a selection
metric, not a production equation; it permits land–water trade-offs, so the
raw land and aquatic renewal columns, not the label alone, should be used in
follow-up analysis.

The nine rows per seed are an `L9(3^4)` design over asset, population, support,
and reserves:

| Row | Asset | Population | Joint support | Reserves |
| ---: | --- | --- | --- | --- |
| 1 | low | low | low | low |
| 2 | low | medium | medium | medium |
| 3 | low | high | high | high |
| 4 | medium | low | medium | high |
| 5 | medium | medium | high | low |
| 6 | medium | high | low | medium |
| 7 | high | low | high | medium |
| 8 | high | medium | low | high |
| 9 | high | high | medium | low |

Asset q is `0.10/0.25/0.40` for both fields and fisheries, corresponding to
`E=0.0625/0.390625/1.0`; opening herd stock is 25%/50%/80% of the lesser of
zero-displacement support and a population-scaled management envelope.
Population is 80/160/320 and food stock is 0.25/1/2 reference turns. Every
matched arm gets the same mature subsistence capabilities, first-tier craft
capabilities, fixed housing, and population-scaled opening tools/materials.
These are explicit state-bank assumptions, not measured historical claims.

### Intervention and control

The harness calls production `TurnPlanner.Draft`, edits only the controlled
camp's first-turn economic intent, then calls production `Prepare` and
`TurnResolver.Resolve`. Baseline is the exact unchanged `DemandControl` intent.
Each treatment asks to move 5%, 10%, or 20% of opening residents from available
implicit/explicit gatherers for one turn:

- **Field:** add Farmers and mark their added labor as field expansion.
- **Fishing:** add Fishers; the ordinary fishery plan decides catch,
  maintenance, and build service.
- **Herd:** add Herders and move the permitted first-turn breeding/retention
  setting 25%, 50%, or 100% of the remaining distance to full intensity. The
  existing grown-herd forecast, management bound, `CapBreeding`, birth,
  nutrition, forage/water, starvation, and slaughter equations determine what
  survives.

All later turns use unchanged automatic policy. The production planner's own
internal `DiscountRatePerYear=0.01` was deliberately left unchanged; it is a
background planning parameter, not the Elder rho evaluated here. Geography,
seasonal forcing, initial snapshot, configuration, and execution path match
within each state. One labor-constrained state (`S03-R06`) clipped its medium
and high request to 27 available gatherers in all three domains: six of 243
treatments. The files retain both requested and accepted values rather than
silently repairing the state.

### Physical response

There were 27 states, 10 arms per state, 40 decisions per arm: **270
trajectories and 10,800 production turns**. First-turn medians confirm the
intended physical channels:

| Domain | Pulse | Gatherers displaced | Domain investment after 20 years |
| --- | ---: | ---: | --- |
| Field | 5% / 10% / 20% | 8 / 16 / 32 | 160 / 320 / 640 build worker-years; mean q +0.00137 / +0.00266 / +0.00488 |
| Fishing | 5% / 10% / 20% | 8 / 16 / 32 | 87.7 / 160 / 480 build worker-years after catch/maintenance; mean q +0.00400 / +0.00846 / +0.01625 |
| Herd | 5% / 10% / 20% | 8 / 16 / 32 | median retained herd +2 / +4 / +8 heads through actual births |

All 243 first turns had zero incremental endpoint welfare: reserves and service
stocks buffered the one-turn labor opportunity cost. This does **not** mean the
investment was free. The measured physical debit is displaced gathering and
domain worker-years; welfare diverged later as reserves, population, capital,
ecology, and fission paths separated.

Across all intensities, median 800-year cumulative incremental production was
`+2602.9` food for fields, `+2426.2` for fishing, and approximately zero for
herds (median animal food was `+186.3`, offset by other production). Interquartile
ranges cross zero in every domain, so these are heterogeneous responses, not
universal yield claims. Median ecology-condition changes were zero at reported
precision; stock and accessibility differences remained in the recorded food,
biomass, q, and displacement columns. At year 400 median reserve deltas were
`+187.9` field, `+156.3` fishing, and `-25.2` herd food units.

Fission was not suppressed. Relative to its matched baseline repeated for each
of the three intensities, field treatments produced 6 fewer fissions and 2,270
fewer daughter residents, fishing 10 fewer and 2,262 fewer, and herd 8 more and
63 more, across 81 treatments per domain. These daughter residents are recorded
but excluded from parent-local welfare by design.

## Phase 3 — discount analysis

For interval `[t0,t1]`, C# reuses the same physical welfare rate difference and
computes:

```text
Delta U_H = Delta(N_prior Q_endpoint)
            * H [exp(-t0/H) - exp(-t1/H)]
```

The undiscounted weight is `t1-t0`. Dividing every value in a matched state by
its fixed `N_ref` would not change a sign or rank, so results are reported in
person-QoL-years. `value > 1e-8` is positive. Durable break-even is the first
boundary at which cumulative value is nonnegative and never becomes negative
again; `0` means no welfare drawdown occurred. The separate durable-positive
onset is the first boundary after which value remains strictly positive.

### Overall classification and ranking

There were 130 positive 800-year investments among 243 treatments, and 25 of
27 states had at least one positive option. Ranking comparisons use only the
352 treatment pairs whose two members were positive and had distinct 800-year
values.

| E-fold H | rho/year | Correct sign | Recall of 800y-positive | False negative | False positive | Correct positive pairs | Correct best choice |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 100y | 0.0100 | 170/243 (70.0%) | 91/130 (70.0%) | 39 | 34 | 244/352 (69.3%) | 16/25 (64%) |
| 200y | 0.0050 | 192/243 (79.0%) | 101/130 (77.7%) | 29 | 22 | 286/352 (81.3%) | 17/25 (68%) |
| 400y | 0.0025 | 218/243 (89.7%) | 121/130 (93.1%) | 9 | 16 | 295/352 (83.8%) | 18/25 (72%) |

Incorrect best-choice states were:

- 100y: `S01-R01`, `R02`, `R05`, `R06`; `S02-R01`, `R02`, `R03`, `R04`;
  `S03-R03`.
- 200y: the same list except `S02-R03`.
- 400y: the 200y list except `S03-R03`.

Exact chosen alternatives and every inverted pair are in
`TestResults/InvestmentPayback/plots/ranking-cases.csv` and
`ranking-inversions.csv`.

Sign errors by domain show where nominally attractive short-run paths disagree
with the 800-year comparator:

| H | Field FN / FP | Fishing FN / FP | Herd FN / FP |
| ---: | ---: | ---: | ---: |
| 100y | 16 / 6 | 11 / 16 | 12 / 12 |
| 200y | 11 / 2 | 11 / 12 | 7 / 8 |
| 400y | 3 / 2 | 3 / 9 | 3 / 5 |

### Domain differences

| Domain | Positive at 800y | Median 800y value, all arms | Undiscounted durable break-even among positive | Durable-positive onset | 400y sign result (TP/FN/FP/TN) |
| --- | ---: | ---: | ---: | ---: | ---: |
| Field | 54/81 (66.7%) | +2109.8 | 200y | 300y | 51 / 3 / 2 / 25 |
| Fishing | 46/81 (56.8%) | +418.0 | 170y | 300y | 43 / 3 / 9 / 26 |
| Herd | 30/81 (37.0%) | 0.0 | 370y | 400y | 27 / 3 / 5 / 46 |

Cultivation is the most consistently classified domain. Its paid field capital
can remain productive after the pulse, and the 400-year evaluator recovered
94.4% of positive cases with only two false positives.

Fishing has fast access gains but the weakest 400-year specificity (74.3%).
Nine treatments were positive under H=400 but nonpositive by year 800; five
were high-intensity fishing. This is the clearest case where a discounted
objective can over-rank an early catch/access path relative to its later
ecology, reserve, population, and policy consequences.

Husbandry is thresholded by integer births, tool-covered management,
forage/water, and later slaughter. Many pulses therefore have exactly zero
median long-run effect, while beneficial cases take a median 370 years to
durably repay and 400 years to stay positive. This is why a 100-year e-fold
missed 12 of the 30 positive herd treatments; H=400 missed three.

![Cumulative incremental welfare](../TestResults/InvestmentPayback/plots/cumulative-welfare.png)

![Discount classification and ranking](../TestResults/InvestmentPayback/plots/discount-ranking.png)

![Durable break-even](../TestResults/InvestmentPayback/plots/durable-break-even.png)

![Physical response](../TestResults/InvestmentPayback/plots/physical-response.png)

## Sensitivity and limitations

- At H=400, correct-sign shares were 98.8%/79.0%/91.4% for high/medium/low
  opening assets; 93.8%/85.2%/90.1% for high/medium/low population;
  84.0%/88.9%/96.3% for high/medium/low joint support; and
  88.9%/96.3%/84.0% for high/medium/low reserves. The three seed results were
  87.7%, 88.9%, and 92.6%. The result is not driven by one pulse level: H=400
  accuracy was 88.9–90.1% across intensities.
- A 400-year **undiscounted truncation** agreed in sign with the 800-year
  comparator for only 171/243 treatments (70.4%); high fishing was only 15/27
  (55.6%). H=400 performs better because it still includes years 400–800 with
  declining weight. This is evidence against shortening evaluation episodes to
  the named e-fold horizon.
- The `L9` array estimates main effects efficiently but aliases interactions.
  Three deterministic worlds characterize exploratory sensitivity, not a
  population distribution or held-out validation set.
- Initial camps are controlled synthetic fixtures with generous identical
  capabilities, tools, materials, housing, and storage. Initial competitors
  were excluded; trade/rival effects arise only after endogenous fission.
- Parent-local welfare can penalize earlier fission even if daughter welfare is
  socially valuable. That is the reviewed normative boundary, not a neutral
  total-lineage result.
- Endpoint QoL is the current 20-year reward abstraction. No within-turn
  `N(t)` or `Q(t)` trace exists, so continuous discounting is exact only for the
  simulator's piecewise-constant reward representation.
- The 800-year comparator itself is unstable relative to a 400-year truncation.
  A final rho decision should add longer (for example 1,200–1,600 year)
  continuations and new disclosed calibration seeds before creating a truly
  held-out validation cohort.

## Recommendation

1. Use **H=400 years / rho=0.0025 per year** as the primary candidate for the
   Village Elder. Apply `gamma=exp(-rho * Delta t)` and the exact interval weight,
   not a fixed per-step discount detached from elapsed time.
2. Retain **H=200 / rho=0.005** as a deliberate shorter-horizon sensitivity or
   curriculum ablation. Its 81.3% positive-pair agreement is useful, but its 29
   false negatives make it unsuitable as the only normative objective.
3. Reject **H=100 / rho=0.01** as the sole Elder horizon. The identical numeric
   value in the automatic planner's internal continuation model is not evidence
   that it is appropriate for normative RL welfare.
4. Keep the undiscounted long-continuation score, domain-stratified sign errors,
   and fission sidecar as evaluation gates. H=400 still has 9 false negatives,
   16 false positives, 57 positive-pair inversions, and seven wrong top choices.

## Reproduction and artifacts

Harness: [`Tools/InvestmentPayback`](../Tools/InvestmentPayback/README.md).

```bash
# Full measurement and plots
bash Tools/InvestmentPayback/Run-Sweep.sh \
  TestResults/InvestmentPayback --verify

# Bounded smoke/replay check
bash Tools/InvestmentPayback/Run-Sweep.sh \
  /tmp/investment-payback-smoke --quick --turns 6 --verify
```

The final full run completed in 74.7 seconds on the audit host. `verification.txt`
reports that the baseline and high-intensity field/herd/fishing sentinels had
identical final fingerprints and per-turn raw welfare under scalar and
reversed-parallel execution.

Raw/derived artifacts are intentionally ignored under
`TestResults/InvestmentPayback`: `initial_states.csv`, `trajectories.csv`,
`paired_trajectories.csv`, `discount_summary.csv`, `manifest.json`,
`verification.txt`, aggregate/ranking/sensitivity CSVs, and SVG/PNG plots.

## Change record

**Production simulator changes: none.** The experiment added only the C#
measurement project and reducer under `Tools/InvestmentPayback`, this report,
and a `.gitignore` allow-list for the harness project. The experimental world
clone changes map dimensions and initial player count for bounded execution;
those changes are recorded in the manifest and apply identically to all arms.
The live normative Google Doc was read but not edited.
