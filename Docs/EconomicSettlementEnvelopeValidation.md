# Economic settlement envelope correction

## Scope and provenance

This report validates the architectural correction on branch
`refactor/economic-settlement-envelope`. The before source is commit
`86cba712dfd76673bd9637052543bde85f810e0b`; the candidate is this working
tree. No animal yield, crop productivity, movement inertia, breeding rate,
housing value, arbitrary herd cap or technology-specific weight was changed.
Husbandry diffusion, taming and live-animal trade remain out of scope.

The implementation follows `EconomicControlContract.md`,
`PastoralPlannerCorrectionDesign.md`, `EconomicControlContractAddendumA.md`
and `CurrentSimulationContract.md`.

## Violations in the before source

- `HeuristicTribePolicy` chose separate agricultural and pastoral movement
  algorithms from `Agriculture && !Husbandry` versus `Husbandry`. A secondary
  capability therefore changed the policy being run even when the physical
  settlement had not changed.
- DemandControl described fixed Housing as the farming route and Light Housing
  as the herding route. Once Husbandry made tents feasible, an ordinary fixed
  village's remaining shelter deficit could be redirected into tents.
- DemandControl reserved labor for the current herd before calculating field
  operation. It then set the target toward the biologically grown herd and
  classified the extra herders as Continuity. Spare capacity in an existing
  herder assignment could authorize growth without a Development grant.
- Actual managed-herd claims consumed `PotentialFood - Food` before `Food`,
  while `HerdLabour.Pasture` and movement coverage treated all
  `PotentialFood` as pasture. The three answers could disagree about the same
  site.
- The legacy Value settling term and the fission site regime still used
  technology identity/capability as a strategy selector.

## Correction

`SettlementEconomy.cs` now owns one pure local model:

- `HerdSupportEnvelope` exposes zero-displacement forage, absolute forage and
  water capacities for the owned species mix.
- `LocalHerdEcology.Survey` projects a fixed radius-one footprint without
  mutating ecology. Land contributes nonhuman forage
  `max(0, PotentialFood - Food)` and human-accessible `Food`; water uses the
  same stock plus bounded horizon-recharge proxy as actual herd claims.
- `SettlementSiteEvaluation` adds human food, retained fields, fixed Housing
  and recurring labor. Both movement and DemandControl consume it; it is not a
  recursive production plan.
- `SettlementPortfolioSupervisor` selects retained operated fields and the
  herd supportable by zero-displacement forage, water and labor actually left
  after that field commitment. Its labor feasibility scan has fewer than 40
  fixed breakpoints, independent of population.

DemandControl then treats retained field and herd work as Continuity. A larger
herd target is an indivisible Development request competing with field
Development. An oversized herd lowers the existing authoritative
`HerdTarget`; current animals still graze and drink before breeding and
`HerdLabour.Slaughter` liquidates the excess.

Movement now has one settlement comparison. Stay credits retained fields,
fixed shelter, human support and the sustainable herd. Move credits candidate
human support and only the extra herd retained beyond Stay, then charges lost
fields, fixed shelter, recurring labor, discontinuity and inertia. Primary
technology is not read. Shelter is likewise one service: retained fixed and
portable capital request their own replacement, and a physical operating
policy chooses the remaining deficit route with one bounded feasible fallback.

## Focused regression results

`EconomicSettlementChecks` contains 36 assertions covering A-I:

| Case | Result |
| --- | --- |
| A. Technology-only invariance | A fixed-housing village with an ordinary shelter deficit produced the same NoOp, 47 Farmers, zero Herders, 16.5 Housing and zero Light Housing with or without secondary Husbandry. Human-food staffing and unmet food were equal; no herd target or herd Development request appeared. |
| B. Small complementary herd | Eight animals stayed below `H_sustainable = 135.8`; the village stayed, staffed 49 Farmers and 3 Herders, retained fixed Housing and targeted 8 animals. The possible ninth animal was a Development request and received no grant; forecast human-food displacement was zero. |
| C. Oversized acquired herd | Four hundred animals were below `H_absolute = 2625` but above labor-bounded `H_sustainable = 227.8`. The plan retained 13 Farmers, staffed 107 Herders, set target 227, stayed, requested current-turn forage and water for all 400 animals, and only then resolved the excess through authoritative slaughter while retaining field capital. |
| D. Grossly oversized herd | Four thousand animals exceeded absolute site support. Staffing stayed resident-bounded, the deterministic target was liquidated through the existing resolver, living/dead/born/slaughtered animals and carcass food conserved, and humans did not starve solely to preserve livestock. |
| E. Legitimate pastoral move | A weak-capital, herd-dependent camp with failed local forage/water moved to the materially superior site (tile 127). |
| F. Husbandry-first mixed settlement | Physically identical cultivation-first and husbandry-first mature mixed settlements both stayed and produced byte-equivalent intent/production descriptions; fission classified both from their retained fields. |
| G. Agriculture-first pastoral outcome | A cultivation-first camp with negligible field/housing capital and a valuable herd moved to tile 127 and received the mobile fission site regime. |
| H. Shelter capability invariance | Secondary Husbandry did not redirect either replacement or the ordinary fixed-village deficit to tents. When fixed construction was physically infeasible and animal fabric was available, the bounded Light Housing fallback was admitted. |
| I. Determinism and persistence | Scalar and reversed one-item partitions under four workers matched for two turns; save/load continuation matched across execution modes for two more turns. The site survey examined the same seven tiles for populations 120 and 10,000. |

Relevant suites after the final test change:

```text
PASS: 61530 agent-contract assertions
PASS: 627 fission assertions
PASS: 132 demand-control assertions
PASS: 36 economic-settlement assertions (A-I)
PASS: 1688 herd-labour assertions
PASS: 60 settling assertions
```

The full fixed-check set also passed, including replay, save/load, production,
trade, immigration, ecology and Neolithic checks. The current-contract checker
passed 77 JSON/API values, 15 test claims and all document links.

## Before/after fixture behavior

The before probe was built from commit `86cba71`; the after probe uses this
branch. Numbers are residents assigned for the turn and production ceilings.

| Fixture | Before | After |
| --- | --- | --- |
| Technology-only control | 47 Farmers, 0 Herders, Housing 16.5, tents 0 | 47 Farmers, 0 Herders, Housing 16.5, tents 0 |
| Same village + secondary Husbandry, no animals | 41 Farmers, 0 Herders, Housing 1.5, tents 20 | 47 Farmers, 0 Herders, Housing 16.5, tents 0 |
| Small herd (8) | 48 Farmers, 4 Herders, target 9; the additional herder was Continuity | 49 Farmers, 3 Herders, target 8; possible increment is Development and received no grant |
| Oversized herd (400) | 0 Farmers, 120 Herders, target 253 | 13 Farmers, 107 Herders, sustainable target 227 |

Equivalent-physical-state movement decisions were:

| Physical state | Before cultivation-first | Before husbandry-first | After cultivation-first | After husbandry-first |
| --- | --- | --- | --- | --- |
| Mature mixed fields, fixed Housing and 80 animals | Stay | Stay | Stay | Stay |
| Negligible fields/Housing, 180 animals, dominant candidate | Move to 127 | Move to 127 | Move to 127 | Move to 127 |

The stable decisions are intentional. Before, the cases reached different
technology-selected code paths; after, each pair reaches the same physical
comparison and produces the same result.

## Matched mixed-economy cohort

Three Continents worlds (seeds 11, 23 and 1729), 80 founding settlements each,
were opened with the before binary. Both binaries then loaded the identical
turn-10 save for each seed and ran 40 post-opening turns. Fission, immigration
and trade were disabled to keep cohort membership fixed and isolate allocator
and movement behavior. All six reports used config hash
`878A5F56C465557FB72905CC198498E22D28F9B8C2D6C565C939B52CB5F3DE7A`.
Primary technology below is a diagnostic label only.

| Final diagnostic (240 settlements) | Before | After |
| --- | ---: | ---: |
| Population | 85,963 | 101,901 |
| Settlement population P25 / median / P75 | 50.25 / 219.5 / 623.25 | 87.25 / 373 / 744.25 |
| Settlement QoL P25 / median / P75 | 0.466 / 0.746 / 0.919 | 0.743 / 0.844 / 0.958 |
| Operating field tiles | 1,359 | 1,507 |
| Mean operating fraction on those tiles | 1.000 | 0.9995 |
| Mean settlement field intensity | 0.255 | 0.309 |
| Farmer share | 37.31% | 42.71% |
| Managed animals / owning settlements | 11,939 / 52 | 410 / 37 |
| Animals per owner P25 / median / P75 | 38 / 67 / 252.25 | 8 / 10 / 14 |
| Herder share | 6.13% | 0.17% |
| Slaughtered animals over 40 turns | 888 | 97 |
| Camp moves / village-to-camp transitions | 192 / 0 | 123 / 0 |
| Fixed / portable shelter capacity (people) | 74,344 / 3,990 | 97,312 / 521 |

Realized food receipts over the matched 120 world-turns were:

| Channel | Before | After | Ratio |
| --- | ---: | ---: | ---: |
| Plant (Gathering, Cultivation, Foraging) | 177.261m | 205.451m | 1.159 |
| Animal (Hunting, Animal Product, Slaughter) | 14.301m | 4.845m | 0.339 |
| Aquatic (Fishing and passive aquatic gathering) | 3.718m | 3.441m | 0.925 |

Final after-state primary-technology diagnostics were:

| Primary label | Settlements | Population | Villages | Animals | Farmer share | Herder share |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Animal Husbandry | 52 | 19,217 | 31 | 410 | 42.37% | 0.91% |
| Cultivation | 182 | 79,739 | 132 | 0 | 42.36% | 0% |
| Watercraft | 6 | 2,945 | 5 | 0 | 54.43% | 0% |

All 37 final livestock owners were at or below zero-displacement capacity.
Their capacities summed to 22,682 animals for both zero-displacement and
absolute support because water, not forage, was the binding resource in these
natural worlds. The focused fixture separately observes the intended forage
distinction (`135.8` zero-displacement versus `2625` absolute).

There were 37 secondary Husbandry acquisitions with zero animals. On the next
allocation they produced zero Herders, zero animal change and zero change in
portable shelter in every case. One acquisition coincided with a move on the
following turn, but the camp already had Cultivation and retained capital, so
both observations use the same physical movement path; no Husbandry term
exists when owned animals are zero. The exact cloned A/H regressions establish
causality: changing only the flag changes no plan or movement.

These cohort outcomes are not a calibration acceptance result. The large shift
from herds/animal receipts toward fields/plant receipts is a semantic-repair
consequence under current returns and should be assessed in a later calibration
pass. It was not hidden by changing productivity, breeding, shelter values,
inertia or caps here.

## Performance and work bound

Three alternating before/after timing pairs used the same seed-1729 opening
save, 80 settlements, four workers and 25 post-opening turns, with diagnostics,
fingerprints and counters off. Medians across the three process runs:

| Metric | Before | After | After / before |
| --- | ---: | ---: | ---: |
| 25-turn total | 5,151.8 ms | 5,167.9 ms | 1.003 |
| Median turn | 204.2 ms | 160.6 ms | 0.786 |
| P95 turn | 332.8 ms | 318.5 ms | 0.957 |
| AI total | 1,937.2 ms | 1,968.7 ms | 1.016 |
| Prepare total | 1,028.1 ms | 1,044.1 ms | 1.016 |
| Execute total | 2,178.3 ms | 2,089.0 ms | 0.959 |
| Allocated bytes | 2.489 GB | 2.580 GB | 1.037 |

The behavioral paths diverge after the shared checkpoint, so these are matched
input gameplay timings rather than an identical-work microbenchmark. They show
no runtime blow-up. In the counter-enabled 3-world run, maximum per-turn
planner tile projections fell from 645 to 373. The current 80-tribe scale run
completed 10 opening turns with exact scalar/worker replay (`total AI 592.0
ms`, `total resolve 976.6 ms`).

The stronger bound is structural and test-pinned: each local envelope surveys
at most seven tiles, movement considers a fixed-radius candidate set, and the
portfolio supervisor checks fewer than 40 breakpoints. None scale with elapsed
years, population, field count times herd count, or a population × field × herd
Cartesian product.

## Remaining selector audit

No remaining current DemandControl, camp-movement, shelter or fission regime
path selects strategy from primary technology, `Agriculture`, `Husbandry`,
`Fishing` or `IsVillage` as an economic identity.

The remaining nearby reads are capability or physical-state reads:

- `CultivationEconomy.Enabled` / `HusbandryEnabled`, recipe filters,
  `EconomicIntents` validation and `ActionContract` expose feasible actions.
- `OpeningTransitions` uses the first breakthrough only for the once-only
  compatibility reward. Secondary discovery only adds the capability flag and
  never replays it.
- `TurnPlanning.AutomaticIntent` asks for a breeding recommendation only when
  Husbandry and living owned animals both exist.
- `Fission.DaughterShelter` uses capabilities to expose build recipes; carried
  shelter, inputs and labor still bound the result. `Fission.RegimeOf` now uses
  era plus actual owned field capital.
- `IsVillage` remains where it denotes physical sunk settlement state:
  abandonment/release, village transition, cohesion/fission terms and the
  immigration barrier. It does not mean “run Agriculture AI.”
- contact-memory and save-migration checks use era/technology status, not an
  economic strategy.
