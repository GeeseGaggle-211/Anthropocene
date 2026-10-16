# Unified social relocation validation v0.2

Status: the shared architecture, finite-input accounting, deterministic replay and
population-transfer conservation pass. Native balance, forecast robustness,
learned-policy comparison and promotion of the Physical-v1 social law remain
uncertified. **NO-GO for a fresh MLP training bank.**

## Aggregate game experience first

The sampled game does not look like a demographic death loop. It looks like a
rapidly growing population whose goods economy does not keep clothing and shelter
abreast of growth, while fission repeatedly turns that population into more small
settlements.

Three identical saved post-opening boundaries (seeds 17, 53 and 991) begin with
24 camps and 2,653 people. After 25 continuation turns per seed:

| Social systems | Camps | Population | Fissions | Immigration flows / people | Rapid pair returns |
| --- | ---: | ---: | ---: | ---: | ---: |
| Neither | 24 | 10,716 | 0 | 0 / 0 | 0 |
| Fission only | 165 | 42,659 | 141 | 0 / 0 | 0 |
| Immigration only | 24 | 10,716 | 0 | 0 / 0 | 0 |
| Both | 145 | 39,795 | 121 | 101 / 1,295 | 6 |

No observed daughter or immigration receiver is extinct at the one- or three-turn
follow-up. Gross births and cause-specific deaths are both large, but births remain
larger in aggregate. A genuinely infeasible controlled settlement still declines
for explicit exposure and starvation causes, so this result is not a survival
guarantee.

The end state remains materially poor. Of 145 living tribes, none has both
clothing and housing coverage at least 0.8, 9 have only a clothing gap, 35 only a
housing gap, and 101 have both gaps. Total population × QoL is 4,754.024 and
population-weighted QoL is 0.119463. The QoL formula was not changed.

![Terminal population, population-times-QoL and QoL histograms](UnifiedSocialRelocationEndStateHistogram-v0.2.png)

Immigration is not the source of the settlement network. It commits no move in the
immigration-only branch. Fission alone creates 141 new camps; the combined branch
creates 121 because immigration participants sit out same-turn fission and then
change later trajectories. In the combined run, the largest absolute migrant
source is the both-gaps category because it supplies the largest denominator.
Non-village flow counts per source-capable camp-turn are 0/60 for a clothing gap,
15/220 for a housing gap and 71/1,353 for both gaps. Village counts are 1/15,
3/79 and 11/170 respectively. Neither of the two adequate source-capable camp-turns
sends a cohort. The small village clothing-only denominator has the highest
observed rate, so the evidence
does not support a rule that the composite both-gaps state alone drives movement.

![Immigration source rates by clothing and housing condition](UnifiedSocialRelocationMigrationSources-v0.2.png)

The principal native blocker is therefore fission calibration and daughter/service
viability under the real goods economy, not an immigration-driven death loop and
not a change to experienced QoL. Immigration still needs broader causal coverage,
but its current hysteresis already reduces the few measured reversals.

### Daughter maturation control

The new Physical-v1 daughter cooldown is a narrow timing guard. A matched sweep
from the same three boundaries produced:

| Additional blocked turns | Fissions | Recursive daughter fissions (age 1/2/3/older) | Fission / immigration-outflow blocks | Daughter outflow events age 1/2 | Camps | Population | Viable settlements | Pop-weighted QoL |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 0 | 123 | 73 (7/6/4/56) | 0 / 0 | 2 / 6 | 147 | 39,479 | 0 | 0.124 |
| **1 (shipped)** | **121** | **71 (0/10/3/58)** | **105 / 118** | **0 / 6** | **145** | **39,795** | **0** | **0.119** |
| 2 | 114 | 64 (0/0/7/57) | 202 / 223 | 0 / 0 | 138 | 39,193 | 0 | 0.126 |

The default eliminates one-turn daughter recursion and one-turn daughter
immigration outflow exactly, including across save/load. The zero-cooldown control
has two age-one outflows moving 21 people; the default has none, while still
allowing 13 age-one inflows. The default configured group minimum is four, but the
10 percent cap and 12-person source floor make 40 residents the smallest effective
source. The two-turn alternative is not promoted: it has seven fewer fissions but
still no viable endpoint. No QoL, mortality, reward, immigration utility or
fission-threshold calibration changed.

### Forecaster correction and bounded site-selection control

The requested Astra review found that fission already gives projected food the
first two historical destination keys: capped obtainable support, then uncapped
support, before distance and tile ID. It also identified a concrete representation
defect: the new-site forecast reconstructed raw supply from capped support, lost
aquatic type and could apply gathering accessibility twice.

That defect is corrected without changing utility, thresholds, QoL, mortality or
reward. New-site capacity now carries uncapped repeatable land, aquatic and
managed-land food separately; actual transferred workforce and accessibility are
applied once; herd forage/water remain separate; and reports expose uncapped
renewable-food ratio plus typed production. Focused tests cover rich-site headroom,
aquatic non-fungibility, exact workforce use and herd-coverage diagnostics. A
bounded forecast of carried-herd receipts and competition for land food remains a
robustness follow-up; the old conversion of poor pasture into fewer human calories
is not restored.

The historical `EcologicalSupport` selector remains the default. Two opt-in modes
evaluate at most the first four ecologically ranked sites through the unchanged
shared acceptance gate and then select by accepted renewable food or aggregate
welfare. In the matched 25-turn sweep, both alternatives produce 127 fissions,
151 camps, 42,307 people, 13 negative-net daughters and zero viable settlements,
versus 121 / 145 / 39,795 / 10 / zero for the historical control. They improve
population-weighted QoL from 0.119 to 0.151 but do so by creating more settlements;
neither is promoted. No numerical food bonus or survival weight was introduced.

### Why daughter economies do not balance

The opt-in real-economy daughter audit compares site yield with the conversion of
labor and capital into services. It compares fission-only and fission-plus-
immigration trajectories from the same three boundaries and follows daughters for
up to eight turns. It is an observational decomposition, not a causal intervention.

Across daughter ages one through three, fission-only settlements produce 95.5% of
food need and realize 97.1% nutritional satisfaction. With immigration, those
figures are 96.9% and 97.5%. Food is therefore slightly tight, but it is not the
coverage that collapses: mean clothing is 0.210 / 0.206 and housing is 0.043 /
0.040. The dominant distinction is **calorie viability versus economic surplus**.

At founding, a daughter receives proportional portable stores and workforce but
zero fixed housing and zero site-bound field capital; transferred farmers become
gatherers because their fields stay with the parent. During ages one through three:

- the no-pending-trade allocator replay assigns 73–84% of the planning population
  to food gathering, 7–22% to farming, about 0.5% to herding and 3.1% to
  manufacturing on average;
- any clothing or shelter output occurs in only 31.7% of fission-only and 32.5% of
  combined daughter-camp turns;
- planner replays classify labor rationing and integer staffing as the common
  clothing/shelter blockers, while raw-material shortage is rare; and
- mean population reaches 1.91× founding size by age three, while fixed shelter
  covers only about 2–3% of residents across the early window.

The best-supported working diagnosis is therefore **calorie viability with too
little protective-goods labor/capital**, rather than immigration or a grossly
unproductive food site. A new site spends most of the replayed labor budget on
subsistence and fields, leaving a small, discretized manufacturing remainder to
replace the fixed housing it did not inherit and to clothe a rapidly growing
cohort. Later fission divides portable capital again within the observation window.
The audit does not independently vary ecology, staffing, capital and population
growth, so it does not yet prove which lever is causal or establish a daughter
recovery time. Immigration changes the early service figures little, and the same
deficit already exists in fission-only daughters, so it is not the initiating
mechanism in these sampled trajectories.

## Scope and reproduction

The branch extends reviewed daughter-cooldown commit
`472a22ed0159bb36d583afca3a9ec045335bf465`. The tracked contract copy is
byte-identical to the supplied attachment. Representative commands are:

```sh
dotnet build Tools/Headless/Anthropocene.Headless.csproj -c Release
dotnet run --no-build -c Release --project Tools/Headless/Anthropocene.Headless.csproj -- --social-relocation-only
dotnet run --no-build -c Release --project Tools/Headless/Anthropocene.Headless.csproj -- --immigration-only
dotnet run --no-build -c Release --project Tools/Headless/Anthropocene.Headless.csproj -- --fission-only
dotnet run --no-build -c Release --project Tools/Headless/Anthropocene.Headless.csproj -- --social-relocation-native=3
dotnet run --no-build -c Release --project Tools/Headless/Anthropocene.Headless.csproj -- --demography-regression
dotnet run --no-build -c Release --project Tools/Headless/Anthropocene.Headless.csproj -- --demography-social-ablation=25
dotnet run --no-build -c Release --project Tools/Headless/Anthropocene.Headless.csproj -- --demography-immigration-sweep=25
dotnet run --no-build -c Release --project Tools/Headless/Anthropocene.Headless.csproj -- --demography-fission-cooldown-sweep=25
dotnet run --no-build -c Release --project Tools/Headless/Anthropocene.Headless.csproj -- --demography-fission-site-sweep=25
dotnet run --no-build -c Release --project Tools/Headless/Anthropocene.Headless.csproj -- --demography-daughter-economy=25
dotnet run --no-build -c Release --project Tools/Headless/Anthropocene.Headless.csproj -- --demography-qol-histogram=25
dotnet run --no-build -c Release --project Tools/Headless/Anthropocene.Headless.csproj -- --social-scale=8,25
dotnet run --no-build -c Release --project Tools/Headless/Anthropocene.Headless.csproj -- --social-scale=80,0
dotnet run --no-build -c Release --project Tools/Headless/Anthropocene.Headless.csproj -- --social-scale=320,0
```

Pipe the histogram rows to `Tools/Headless/Plot-DemographyQoLHistogram.py` to
reproduce both tracked figures.

Focused final counts are 70 shared-relocation, 384 immigration, 673 fission,
37 native matched-continuation and 83 death-spiral assertions. The matched
three-seed ablation passes 18,492 assertions; the ten-row immigration sweep passes
68,917; the three-row daughter-cooldown sweep passes 20,537; the three-row site-
selection sweep passes 21,107; the terminal histogram audit passes 10,829; and the
daughter-economy audit passes 2,643. The Release rebuild has zero errors and only
the two pre-existing `CS0649` warnings. The full fixed/replay suite passes in
61.0 seconds with 1,916 MiB peak working set on this host; the current-contract
checker passes 82 JSON/API values, 17 test claims and all document links.

## Architecture, finite inputs and compatibility

The focused checks establish:

- the exact mover, source, incumbent and aggregate equations, with physical
  mortality risk separate from forecast experienced QoL and no net-rate sign veto;
- birth-only person-year service ceilings, finite typed food, finite raw material
  stocks/sources, one shared generalist extraction pool and technology-qualified
  specialist extraction;
- uncapped, typed repeatable land/aquatic/managed food for new-site forecasts,
  with actual transferred workforce/accessibility applied once and herd forage and
  water kept separate from human food;
- source/daughter herd capacity scaled by the retained herd fraction, with a
  zero-herd control and no duplicated provisions, shelter or expectation reset;
- actual workforce-role transfer, opportunity-worker reservation and no worker
  simultaneously producing in an ordinary role and a future opportunity role;
- zero, funded, unfunded, missing-tool, missing-input, filled and wrong-skill test
  providers, one job per worker and canonical declining reservations;
- nonmonotone group-size handling and exact final simultaneous immigration
  revalidation, including a camp that sends frozen residents while receiving a
  different cohort in the same Close pass; endpoint work is pinned by an accepted
  chain, an eight-source fan-in and a cascading rejection with only incident retry;
- neutral-provider parity, scalar/parallel replay, save round trips and exact
  population, inventory, typed-food, herd, workforce and fractional-ledger
  conservation; and
- lazy capacity allocation plus a provably permissive physical fission bound. It
  uses the exact source envelope and optimistic full-service daughter, not
  historical F/C/H/W; `FissionSearchFraction` lowers only the valid upper-bound
  request threshold. Identical physical states with different last-turn
  satisfaction take the same search/decision path, including newly depleted
  settlements.

`ForwardUtility`, `Fission.QoLModel.ForwardAt` and the old largest-remainder legacy
semantics remain the exact decision path for `LegacyQoLNetGrowth`. An absent saved
demographic selector still loads that legacy model. Existing saves that already
select `PhysicalMortalityV1` will use the unified law on this feature branch because
no second serialized social-law selector was introduced. That is an intentional
unmerged behavior change, not backward-decision parity, and must be approved before
promotion.

`Camp.QoL`, its food/water/clothing/housing/expectation/cohesion calculation, the
population-weighted Elder reward and the RL observation schema are unchanged.
Settlement-local and lineage-wide welfare remain separate diagnostics.

## Native same-boundary outcomes

| Variant | Camps | Population | Fissions | Migrations / people | Mean terminal F/C/H | Pop-weighted local QoL |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Neither | 24 → 24 | 2,653 → 10,716 | 0 | 0 / 0 | 0.955 / 0.302 / 0.314 | 0.195 |
| Fission only | 24 → 165 | 2,653 → 42,659 | 141 | 0 / 0 | 0.935 / 0.259 / 0.132 | 0.127 |
| Immigration only | 24 → 24 | 2,653 → 10,716 | 0 | 0 / 0 | 0.955 / 0.302 / 0.314 | 0.195 |
| Both | 24 → 145 | 2,653 → 39,795 | 121 | 101 / 1,295 | 0.942 / 0.301 / 0.119 | 0.119 |

Worst continuous demographic conservation error is `1.14e-13`; every turn also
closes the integer population and gross birth/death ledgers. Twenty-six accepted
immigration destinations have higher forecast QoL and worse forecast mortality
when destination X1 is compared with the mover's source X0 on both axes,
confirming that mortality is visible without becoming a categorical veto.

The follow-up diagnostics remain cautionary rather than causal proof. Of 121
daughters, 118 are observed at age one and 112 at age three, with no extinction;
the age-three survivors average `-0.227` QoL and `-0.207` clothing coverage
relative to their event boundary, while their population averages 1.922 times the
founding cohort. Of 101 receivers, 93 are observed at age one and 80 at age three,
with no extinction; the age-three survivors average `-0.115` QoL, `-0.135`
clothing coverage and `+7.194` deficit years. No daughter or receiver is
fully healthy at the recorded event boundary or age three. Growth, subsequent
shocks and further transfers confound those raw deltas, so they justify targeted
matched tests rather than a claim that immigration caused the decline.

## Accepted, suppressed and forced continuation

The causal harness finds a real seed-17 boundary after two ordinary preparation
turns and isolates a 7-person immigration from camp 59 to camp 19. The accepted
flow records mover/source/incumbent/aggregate changes of
`+0.200813 / +0.002866 / -0.000822 / +0.006067`, destination forecast QoL 0.180,
F/C/H `0.363 / 0.000 / 1.000`, mortality risk 0.427 and stability zero.

An independently forced transfer has the same endpoint workforce, inventory,
typed food, demographic ledgers, migration memory and contact chronology as the
accepted branch. After three real heuristic/economy/ecology/demography turns,
accepted and forced transfer states remain identical. The sum across the three
follow-up endpoints of world `population × QoL`, accepted minus suppressed, is
`+0.957888`. This is explicitly an endpoint-welfare proxy, not the contract's
integrated person-year welfare. Age zero is reported only as a boundary proxy and
is excluded from that sum.
This case supports the transaction implementation; it does not certify all
receivers or fission daughters.

The 20%, 40% and delayed-40% branches are explicitly compound external shocks to
production, local stocks/ecology, capital, housing and herds on unique affected
tiles. They run the real economy with no correction resources. They are not a
claim that the forecast itself missed each input by exactly 20% or 40%, and they do
not yet provide a conservative admission rescore. Broader positive- and
zero-stability immigration cases plus matched fission cases remain required.

## Hysteresis calibration sweep

The ten-row matched sweep leaves the QoL function, physical demography and fission
coefficients unchanged. With hysteresis disabled, 113 flows move 1,501 people and
produce 16 rapid pair returns. The current three-turn/0.10 penalty has 101 flows,
1,295 people and six returns. Raising only the penalty to 0.20 yields 100 flows,
1,264 people and the same six returns. A 0.30 penalty or five-turn/0.20 combination
has 95 / 1,183 / three, but neither improves age-three receiver QoL (`-0.116`
versus `-0.115`) or demonstrates causal welfare benefit.

Thus the existing hysteresis materially mitigates oscillation, but no alternative
is promoted. No immigration threshold, cap, cooldown or penalty changed. Arrival
population remains a Close-boundary transfer and first enters the real economy/demographic
calculation on the following turn.

## Recoverable distress and death-spiral gates

The physical-demography regression remains green:

- temporary clothing/housing loss and temporary food loss resume positive growth
  after service restoration;
- repeated infeasible loss declines for attributable exposure and starvation;
- deaths, emigration and fission reconcile separately, with social events conserved
  as transfers rather than mortality; and
- the native Elder rebuilds specialist assignments and real clothing/housing from
  remaining people, tools and finite raw resources, reaching three sustained
  served turns with first sustained recovery at turn 8.

There are no population floors, free resources, automatic manufacturing or
demographic bonuses. A learned-Elder runtime/model artifact is not present, so a
matched native-versus-learned shock comparison remains unavailable.

## Performance versus the pinned commit

`SocialScaleBenchmark` is the same source compiled at the pinned commit and on
this branch. It measures the actual AI/prepare/resolve path in Release with seed
1729 and four workers. Short runs include JIT and host noise, and fission causes
the branches to diverge in camp count; these are scale observations, not a
cycle-exact microbenchmark.

| Scale | Pinned turn p50/p95 ms | Unified turn p50/p95 ms | Pinned alloc p50/p95 MiB | Unified alloc p50/p95 MiB | Unified social p50/p95 ms |
| --- | ---: | ---: | ---: | ---: | ---: |
| 8 starts, opening | 36.1 / 279.3 | 65.7 / 255.4 | 3.06 / 6.41 | 4.52 / 7.58 | 15.2 / 44.8 |
| 80 starts, opening | 431.6 / 500.8 | 488.3 / 676.9 | 63.99 / 102.02 | 77.24 / 121.25 | 66.3 / 81.4 |
| 320 starts, opening | 1,066.3 / 1,208.8 | 1,341.9 / 1,634.3 | 265.38 / 433.54 | 316.52 / 506.26 | 158.0 / 378.6 |
| 8 starts + 25 turns | 175.4 / 295.3 | 139.2 / 324.6 | 18.98 / 50.52 | 22.87 / 56.91 | 49.3 / 170.2 |

The safe fission bound deliberately builds physical baselines instead of using
historical coverage. The 320 run records 4,716 forecasts, 3,384 capacity builds
and 2,537 hits; its only active joint-immigration turn uses one batch, two flow
evaluations and eight endpoint builds. The mature run records 5,971 forecasts,
1,200 builds and 9,911 hits (0.892 hit share), with 48 flow evaluations and 191
endpoint builds across 24 joint batches. No dependency retry occurs in those
low-flow samples; the focused chain/fan-in/cascade tests exercise it directly.

Whole-turn 320 p95 is 35% above the pinned sample and allocation p95 is 16.8%
higher. The behaviorally divergent mature run ends with 44 rather than 88 camps;
its p95 is 9.9% higher. The 80 sample has a 35% higher p95, and
these short samples are not warmed repeated-boundary certification. They are
sufficient to fail, not pass, the throughput gate. The local scaling defect found
in review is fixed, but performance is **NO-GO for MLP throughput**. The next
optimization target is forecast-view/material-summary allocation and safe baseline
reuse, not a return to historical QoL coverage.

## GO/NO-GO matrix

| Gate | Result | Evidence / remaining work |
| --- | --- | --- |
| Conservation and legacy compatibility | PASS / scoped | Exact physical transfers and legacy selector path pass; existing Physical-v1 saves deliberately adopt the feature law |
| QoL and RL objective preservation | PASS | QoL calculation, reward and observation schema unchanged; local and lineage welfare separate |
| Shared physical architecture | PASS | One evaluator for immigration/crowding/distress; finite service/material inputs; no mortality-from-QoL or net-rate veto |
| Future opportunity accountability | PASS in injected fixtures | Neutral parity plus funded/false/filled/wrong-skill/reservation/nonmonotone coverage; no live jobs |
| Native conservation and recoverable distress | PASS | Same-boundary conservation, cause ledgers and endogenous rebuild pass |
| Healthy/saturated/distress social balance | NO-GO | The shared cooldown removes age-one fission/outflow but leaves 121 combined fissions and zero viable endpoints; sampled daughters support a calorie-viable/labor-capital-constrained working diagnosis, but prepared causal fission cases are incomplete |
| Native causal cases | PARTIAL | One immigration accepted/suppressed/forced case is positive and exact; fission and broader receiver classes missing |
| Forecast robustness | PARTIAL | Typed uncapped new-site food and transferred workforce pass focused checks; herd forage/water are diagnostic only, and pastoral stock competition plus lower-bound admission rescores remain open |
| Site selection | NO-GO for promotion | Bounded accepted-site food/aggregate controls create 127 fissions and zero viable endpoints; historical ordering remains default |
| Oscillation | PASS for current default; calibration open | Existing hysteresis cuts rapid returns 16→6; alternatives lack causal/held-out welfare justification |
| Performance | NO-GO for MLP | Endpoint-local reconciliation passes work-counter tests, but 320/mature p95 and allocation overhead fail promotion |
| Learned-vs-native comparison | NOT AVAILABLE | No learned policy artifact/runtime; RL observation/action/training defects remain separate |

## Decision and concrete next steps

**NO-GO for creating a fresh MLP training bank. Do not merge or start training.**

The shortest evidence-driven route to GO is:

1. Build matched healthy, productively saturated and
   physically distressed fission fixtures on saved native boundaries, then sweep only the existing policy
   thresholds/search fraction. The search fraction may affect only the proven
   upper-bound request, never become a separate acceptance condition. The objective
   is to retain viable saturated and distress splits while reducing repeated
   fragmentation; add no survival bonus or QoL-ratio shortcut.
2. Test the daughter economy directly, one lever at a time: preserve the zero-field
   founding law, then compare a labor-reserve/anti-chatter allocator control against
   a bounded portable-capital or preparation control. Measure food labor, specialist
   staffing, typed inputs, protective output, population, F/C/H and recovery time;
   do not grant free housing, fields, resources or demographic bonuses.
3. Add a conservative/lower-bound version of the same service envelope and test it
   against positive- and zero-stability accepted immigration and fission cases,
   including delayed finite-input exhaustion and bounded carried-herd receipts and
   competition for land food. Keep `Stability` diagnostic until those causal
   results justify how it enters the existing four comparisons.
4. Extend accepted/suppressed/forced native continuations to fission daughters and
   more receiver classes for at least three turns, reporting production, F/C/H,
   cause deaths, workforce, population × QoL and integrated person-year welfare;
   keep endpoint sums explicitly labelled as proxies.
5. Keep historical site ordering while the bounded alternatives remain non-viable;
   revisit selection only after a daughter can demonstrably rebuild its real goods
   economy. Do not add a numerical food or survival bonus.
6. Reduce forecast-view/material-summary allocation and safe-baseline cost, then
   run warmed repeated identical-boundary 80/320/mature benchmarks plus dense-flow
   rejection cascades. Preserve endpoint-local reconciliation and exact canonical
   results.
7. Rerun the 2×2 ablation, calibration sweep, daughter audit, histograms, full fixed/save-replay
   suite and then the requested native-versus-learned shocks once a learned policy
   runtime exists. Resolve RL observation/action/training defects independently.
