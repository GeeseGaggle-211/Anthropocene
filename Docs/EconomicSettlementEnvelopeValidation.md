# Economic settlement envelope correction

## Scope and provenance

This report validates the architectural correction on branch
`refactor/economic-settlement-envelope`. The before source is commit
`86cba712dfd76673bd9637052543bde85f810e0b`; the rejected intermediate
candidate is `eaac614`; the corrected candidate is this working tree. No
animal yield, crop productivity, movement inertia, breeding rate, housing
value, arbitrary herd cap, or technology-specific weight changed.
Husbandry diffusion, taming, and live-animal trade remain out of scope.

The implementation follows `EconomicControlContract.md`,
`PastoralPlannerCorrectionDesign.md`, `EconomicControlContractAddendumA.md`,
and `CurrentSimulationContract.md`.

The later deterministic supervisor experiment on
`fix/economic-pastoral-emergence` is recorded in
[EconomicSupervisorOptionA.md](EconomicSupervisorOptionA.md). It preserves the
architectural correction but fails its matched population acceptance gates.
Treat this document as validation of the envelope refactor, not as acceptance
of that subsequent strategy checkpoint.

## Violations in the before source

- `HeuristicTribePolicy` selected agricultural versus pastoral movement from
  `Agriculture && !Husbandry` and `Husbandry`. A secondary capability could
  therefore replace the movement policy even when the physical settlement did
  not change.
- DemandControl described Housing as the farming route and Light Housing as
  the herding route. Once Husbandry exposed tents, a mature fixed settlement's
  ordinary shelter deficit could switch routes.
- DemandControl reserved the whole current herd before field operation, raised
  `HerdTarget` toward the biologically grown herd, and classified extra herders
  as Continuity. Spare management capacity could authorize growth without a
  Development allocation.
- Actual managed-herd claims consumed `PotentialFood - Food` before `Food`,
  while herd labor and movement treated all `PotentialFood` as pasture. The
  three forecasts could disagree about the same site.
- The first corrected candidate still overvalued cultivation on marginal land:
  retained farmers' unused harvest capacity was omitted from service accounting,
  future field stock was allowed to satisfy current-turn food demand, and a
  one-transition closing-stock gain was projected across the planning horizon.

## Correction

`SettlementEconomy.cs` now owns one pure local model:

- `HerdSupportEnvelope` exposes zero-displacement forage, absolute forage, and
  water capacities for the owned species mix.
- `LocalHerdEcology.Survey` projects the fixed radius-one footprint without
  mutating ecology. Land contributes nonhuman forage
  `max(0, PotentialFood - Food)` and human-accessible `Food`; water uses the
  same bounded horizon semantics as the actual managed-herd claims.
- `SettlementSiteEvaluation` adds human food, retained fields, fixed Housing,
  and recurring labor. Movement and DemandControl consume this cheap
  evaluation; it is not a recursive production plan.
- `SettlementPortfolioSupervisor` selects one retained field/herd portfolio.
  Field and herd alternatives share the same survival, finite-return, labor,
  and ecological comparison; neither capability or primary technology chooses
  the branch.

DemandControl treats retained field and herd work as Continuity. A larger herd
target is an indivisible Development request competing with field Development.
An oversized herd lowers the existing authoritative `HerdTarget`; all opening
animals still graze and drink before `HerdLabour.Slaughter` liquidates excess.

The Astra-high review identified three remaining cultivation accounting
defects. They were corrected without changing calibration:

1. retained farmers' complete remaining harvest capacity is allocated once,
   feedstock first and then current biological-food service, before any extra
   Gatherers are requested;
2. current receipts and future continuation returns use separate ledgers, so a
   future field stock or newborn herd return cannot fund this turn's meals,
   reserve refill, or birth food; and
3. a field step receives only its authoritative next-boundary retained-food
   change and pays build work plus the larger of transition maintenance or
   harvest work. A single closing-stock change is no longer repeated across
   the whole planning horizon.

Movement now has one settlement comparison. Stay credits retained fields,
fixed shelter, human support, and the sustainable herd. Move credits candidate
human support and only the extra herd retained beyond Stay, then charges lost
fields, fixed shelter, recurring labor, discontinuity, and inertia. Primary
technology is not read. Shelter is one service: retained capital determines
the preferred replacement route and bounded feasibility permits fallback.

## Focused regression results

`EconomicSettlementChecks` passes 42 assertions covering A-I plus the Astra
food-ledger regressions:

| Case | Result |
| --- | --- |
| A. Technology-only invariance | The fixed-housing control and a clone with secondary Husbandry both stayed and assigned 47 Farmers, zero Herders, 16.5 Housing, and zero Light Housing. Human-food planning and herd requests were identical. |
| B. Small complementary herd | Eight animals stayed below `H_sustainable = 223.8`; the village stayed, retained its operated fields and fixed Housing, assigned four Herders, and admitted the ninth animal only through a granted Development request. |
| C. Oversized acquired herd | Four hundred animals were below `H_absolute = 2625` but above `H_sustainable = 223.8`. The plan retained 13 operating Farmers, assigned 105 Herders, set target 223, and stayed. Excess animals use authoritative slaughter after current-turn grazing and water claims. |
| D. Grossly oversized herd | Resident staffing remained bounded; liquidation was deterministic; living, born, dead, and slaughtered animals and carcass food conserved; humans were not assigned an unbounded preservation obligation. |
| E. Legitimate pastoral move | A weak-capital, herd-dependent camp with failed local forage/water moved to materially superior tile 127. |
| F. Husbandry-first mixed settlement | Physically identical cultivation-first and husbandry-first mixed settlements both stayed and produced equivalent intents. |
| G. Agriculture-first pastoral outcome | A cultivation-first camp with negligible field/housing capital and a valuable herd moved to tile 127. |
| H. Shelter capability invariance | Secondary Husbandry did not redirect fixed replacement or an ordinary village shelter deficit to tents; a physically infeasible preferred route admitted a feasible fallback. |
| I. Determinism and persistence | Scalar, four-worker reversed partitions/collections, and save/load continuation matched. The site survey remained at seven tiles and portfolio alternatives remained at most 64 for populations 120 and 10,000. |
| Retained-farmer capacity | Thirteen retained Farmers supplied 1,188.138 current food with no duplicate food Gatherer and no remaining Development-food gap. |
| Current/future timebase | A granted future field step did not reduce the unmet current-turn food ledger. |

The complete fixed-check run passed, including:

```text
PASS: 61530 agent-contract assertions
PASS: scalar / 2-worker / 4-worker / reversed replay and save-load continuation
PASS: 627 fission assertions
PASS: 336 immigration assertions
PASS: 480 trade assertions
PASS: 132 demand-control assertions
PASS: 42 economic-settlement assertions (A-I)
PASS: 1688 herd-labour assertions
PASS: 60 settling assertions
```

## Before/after focused behavior

The before probe uses `86cba71`; the corrected probe uses this branch.

| Fixture | Before | Corrected |
| --- | --- | --- |
| Technology-only control | 47 Farmers, 0 Herders, Housing 16.5, tents 0 | same |
| Same village + secondary Husbandry, no animals | 41 Farmers, 0 Herders, Housing 1.5, tents 20 | identical to control: 47 Farmers, Housing 16.5, tents 0 |
| Small herd (8) | 48 Farmers, 4 Herders, target 9; added herder was Continuity | 48 Farmers, 4 Herders, target 9; the increment is a granted Development request |
| Oversized herd (400) | 0 Farmers, 120 Herders, target 253 | 13 retained Farmers, 105 Herders, sustainable target 223 |

Equivalent physical settlements now use the same evaluator:

| Physical state | Before cultivation-first | Before husbandry-first | Corrected cultivation-first | Corrected husbandry-first |
| --- | --- | --- | --- | --- |
| Mature mixed fields, fixed Housing, 80 animals | Stay | Stay | Stay | Stay |
| Negligible fields/Housing, 180 animals, dominant candidate | Move 127 | Move 127 | Move 127 | Move 127 |

The before choices happened to agree but came from distinct technology-selected
policies. The corrected pairs reach the same physical comparison and produce
the same decision; primary technology is absent from that calculation.

## Five-run full-system cohort

Five Continents worlds used seeds 11, 23, 47, 101, and 1729 with 80 founding
tribes. Each ran ten opening turns followed by 40 measured turns with production
configuration, trade, immigration, and fission enabled, four workers, and
partition size 64. All reports have configuration hash
`15A89E1FFDE0CA5BE61BCCE280AB04278D87A2DF86F4731984C292A7526E743F`;
comparisons use the same seeds and workload. Quartiles use linear interpolation
over the five final worlds.

| Seed | Final population | Population-weighted QoL | Herd population | Settlements |
| ---: | ---: | ---: | ---: | ---: |
| 11 | 90,281 | 0.8661 | 8,499 | 205 |
| 23 | 122,934 | 0.8599 | 51,931 | 259 |
| 47 | 114,523 | 0.8772 | 45,072 | 282 |
| 101 | 100,632 | 0.8682 | 29,212 | 226 |
| 1729 | 103,645 | 0.8508 | 15,589 | 213 |

### Run-level endpoint quartiles

| Version | Population P25 / median / P75 | QoL P25 / median / P75 | Herd P25 / median / P75 |
| --- | --- | --- | --- |
| Pre-refactor `86cba71` | 89,175 / 91,739 / 104,479 | 0.8035 / 0.8285 / 0.8408 | 36,595 / 43,889 / 62,823 |
| Rejected intermediate `eaac614` | 65,333 / 71,118 / 73,822 | 0.8653 / 0.8653 / 0.8705 | 20 / 26 / 29 |
| Corrected | **100,632 / 103,645 / 114,523** | **0.8599 / 0.8661 / 0.8682** | 15,589 / 29,212 / 45,072 |

The Astra fixes remove the rejected candidate's population regression and put
all three population and QoL quartiles above the pre-refactor cohort. Herd
population remains below the pre-refactor result because biological expansion
is no longer automatic Continuity; this was not hidden with calibration.
QoL improved in all five matched seeds; population improved in three, was
within one percent in seed 1729, and remained lower in seed 101.

For clarity, pooled final-settlement quartiles (not quartiles of world totals)
are:

| Version | N | Population P25 / median / P75 | QoL P25 / median / P75 | Herd P25 / median / P75 |
| --- | ---: | --- | --- | --- |
| Pre-refactor | 1,455 | 153 / 301 / 515 | 0.7455 / 0.8633 / 0.9357 | 0 / 50 / 250.5 |
| Rejected intermediate | 794 | 217.25 / 411.5 / 639.75 | 0.8115 / 0.8923 / 0.9632 | 0 / 0 / 0 |
| Corrected | 1,185 | 224 / 405 / 650 | 0.8265 / 0.8965 / 0.9615 | 0 / 0 / 149 |

Among the corrected cohort's 399 livestock-owning settlements, herd P25,
median, and P75 are 142.5, 257, and 452.5.

### Movement and physical settlement outcomes

Across the 200 measured world-turns there were 490 moves. At the final boundary:

- 475 villages had never moved, providing a conservative physical proxy for
  sedentary settlements;
- 102 non-villages had moved at least once and 66 had moved more than once,
  demonstrating persistent mobile outcomes without a lifestyle enum; and
- 0 village-to-camp transitions occurred in this sample. The focused pastoral
  fixture separately proves that abandoning weak capital remains possible.

The five final-world counts by seed were:

| Seed | Villages / other camps | Never-moved villages | Moved non-villages | Multi-move non-villages |
| ---: | ---: | ---: | ---: | ---: |
| 11 | 98 / 107 | 85 | 23 | 15 |
| 23 | 134 / 125 | 111 | 17 | 9 |
| 47 | 103 / 179 | 87 | 25 | 18 |
| 101 | 113 / 113 | 89 | 22 | 11 |
| 1729 | 121 / 92 | 103 | 15 | 13 |

### Economy, demography, and interaction totals

Over the five measured runs:

| Metric | Corrected total |
| --- | ---: |
| Plant / animal / aquatic food receipts | 508.983m / 56.699m / 6.642m |
| Trades | 294 |
| Migrants / accepted immigration events | 1,199 / 86 |
| Fissions | 780 |
| Herd slaughter / births / deaths | 2,149 / 152,075 / 31 |
| Human births / deaths | 507,623 / 410 |

Final states contained 5,972 operated field tiles with summed operating
fraction 5,920.90, 150,303 livestock, and 532,015 humans (525,148 resident in
the final camps and classified in the primary-technology diagnostic). At
livestock-owner sites, reported zero-displacement and absolute capacity happened
to bind at the same aggregate value in these natural worlds; focused fixtures
exercise the distinct forage bounds directly.

Primary technology is retained only as a diagnostic label. Pooled final states
were 454 husbandry-primary settlements (177,719 residents, weighted QoL
0.8572), 712 cultivation-primary settlements (339,661 residents, weighted QoL
0.8675), and 19 watercraft-primary settlements (7,768 residents, weighted QoL
0.8961). These are not planner modes.

### Secondary-capability discontinuity audit

The five traces contained 119 secondary-Husbandry acquisitions. Every acquiring
camp owned zero livestock; all produced zero Herders and zero tents immediately
after acquisition. Ninety-seven already had fixed shelter and none switched
from fixed shelter to tents. The exact clone regression establishes causality:
changing only the flag changes no plan or move.

There were 101 secondary-Cultivation acquisitions in husbandry-primary camps.
None assigned a Farmer immediately merely because the flag appeared; 93 kept
the same Herder count. The mobile subset continued its pre-existing movement
sequence rather than becoming sedentary. Capability can enable a later demanded
action, but the acquisition itself no longer selects strategy.

## Performance and work bound

The full-system counter totals are normalized by the actual 25,472 pre-refactor
and 22,100 corrected camp-turns:

| Metric per camp-turn | Pre-refactor | Corrected | Corrected / pre |
| --- | ---: | ---: | ---: |
| AI time | 0.428 ms | 0.420 ms | 0.98 |
| Prepare time | 0.361 ms | 0.461 ms | 1.28 |
| Execute time | 0.819 ms | 0.866 ms | 1.06 |
| Allocated bytes | 1.031 MB | 1.295 MB | 1.26 |
| Food forecast queries | 60.86 | 138.53 | 2.28 |
| Planner tile projections | 9.12 | 5.55 | 0.61 |
| Expansion ladder builds | 0.261 | 0.802 | 3.07 |

The corrected planner performs more bounded forecast/ladder work and allocates
more memory, but AI time per camp-turn is slightly lower; there is no runtime
blow-up. The structural bound is test-pinned: the local envelope surveys at
most seven tiles and the joint supervisor evaluates at most 64 alternatives
for both 120 and 10,000 residents. The independent scale check likewise has
84 tile forecasts and 210 herd choices at both 1,000 and 10,000 residents.
There is no year loop, global optimizer, or population x field x herd search.

## Remaining selector audit

No current DemandControl, shelter, movement, or fission-regime path selects an
economic strategy from primary technology.

Nearby reads that remain are capability or physical-state reads:

- `Agents.cs` checks cultivation or husbandry only to decide whether the camp
  can use automatic Neolithic economy actions. Inside that path, animals,
  fields, Housing, and `IsVillage` are physical/sunk-capital terms.
- recipe filters and intent validation use Cultivation, Husbandry, Fishing, or
  learned technologies to expose feasible actions; they do not choose a mode.
- `OpeningTransitions` uses the first breakthrough only for the once-only
  opening reward. Secondary acquisition does not replay it.
- `Fission.RegimeOf` retains legacy result names `Agricultural` and `Nomadic`,
  but chooses them from era and owned field capital, never primary technology.
- `IsVillage` remains for physical abandonment, cohesion, fission, and
  immigration state. It does not mean "run Agriculture AI."

Husbandry diffusion, proximity taming, and live-animal trade remain deliberately
outside this refactor.
