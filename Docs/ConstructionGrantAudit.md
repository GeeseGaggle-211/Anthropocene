# Construction grant audit — 13 November 2026

This investigation follows the eleven underfilled irrigation grants in the
seed-11, 80-founding-tribe smoothed-calendar playthrough. It changes diagnostic
tools only; simulation rules and the existing acceptance blockers remain.

## What the Elder allocates

Housing, new canals and canal upkeep are separate requests. Each request has
its own output quantity, service priority and complete input bundle, including
Builder-years, Housing Materials, their manufacturing/raw extraction and tool
service. Existing Housing Material stock is shared through reservations.

The compiler sums the labor into occupational headcounts, including one Builder
pool. It also produces a per-good ceiling and recipe execution order. Thus the
village does not have separate housing-builder and canal-builder professions,
and individual workers are not permanently assigned to one project. Their
shared time is consumed by the ordered jobs. The per-good output limits are
supposed to preserve the Elder's allocation.

Relevant implementation: `DemandControl.cs` request bundles and `WithInput`,
housing replacement/deficit requests, canal continuity/development requests and
the final intent/order compiler; `ProductionOrder` in `ProductionDemand.cs`;
`NeolithicProduction.ResolveCampNeolithic`'s shared `laborByRole` budget.

## Native replay results

The diagnostic replay reproduces all eleven original shortfalls exactly.
Population and physical dates agree with the retained CSV for all 107 replayed
turns, and every authoritative simulation-source hash matches the original
manifest. No failed case had fixed Housing granted or produced. Housing work
therefore did not displace canal labor in these cases.

| Immediate cause | Cases |
| --- | ---: |
| Material labor cap only | 4 |
| Excess Tools depleting inputs only | 4 |
| Both | 3 |

Seven grants were already reduced by the prepared production forecast; seven
lost further output during authoritative production, with three in both groups.
The recorded Elder grant is not reconciled when that later forecast cuts the
executable quantity. The original report's broad funding/scheduling suspicion
can now be narrowed to the two implementation defects below.

| Turn / village | Granted segments | Prepared forecast | Actual |
| --- | ---: | ---: | ---: |
| 77 / 4058 | 1.000000 | 1.000000 | 0.000000 |
| 84 / 4605 | 1.000000 | 0.880003 | 0.880003 |
| 91 / 3546 | 1.000000 | 0.939850 | 0.939850 |
| 91 / 4681 | 1.000000 | 0.939850 | 0.939850 |
| 93 / 3546 | 1.000000 | 1.000000 | 0.000000 |
| 94 / 4698 | 1.000000 | 0.968092 | 0.000000 |
| 95 / 2921 | 1.000000 | 0.919817 | 0.919817 |
| 96 / 2921 | 0.080183 | 0.080183 | 0.000000 |
| 96 / 3630 | 1.000000 | 0.914941 | 0.796347 |
| 97 / 3630 | 0.203653 | 0.203653 | 0.000000 |
| 107 / 5671 | 1.000000 | 0.880750 | 0.000000 |

The [case ledger](Experiments/ConstructionGrantAudit-20261113-cases.json) and
[CSV](Experiments/ConstructionGrantAudit-20261113-cases.csv) retain unrounded
quantities, Builder staffing and tool/material quantities for every case.

## Confirmed defects

### Generic tool output is counted twice in the compiled allowance

In `DemandControl.cs`, `total.Add(toolmaking, toolsMade)` already adds both the
inputs and the Tools output to the aggregate bundle. After copying
`total.Outputs` into `alloc.Ceilings`, the compiler adds `toolsMade` again. Labor
and extraction are funded once; the permitted Tools output is doubled.

Toolmaking runs before the other recipes. Rounded-up Toolmaker staffing can
therefore make extra Tools and consume Wood reserved in the economic budget for
Farm Tools and Housing Materials. The prepared production forecast also accepts
the inflated ceiling and does not impose the finite raw-input budget by default.

Village 4058, turn 77:

| Quantity | Units |
| --- | ---: |
| Funded generic Tools | 114.670545 |
| Compiled generic Tools ceiling | 229.341090 |
| Forecast generic Tools | 135.919131 |
| Excess over funded quantity | 21.248585 |
| Opening Wood plus funded extraction | 156.670688 |
| Funded Farm Tools | 30.867484 |
| Funded canal Housing Materials | 11.132659 |

The intended Wood budget closes exactly:
114.670545 + 30.867484 + 11.132659 = 156.670688.
The original gameplay receipts instead spend it on 135.919131 Tools and
20.751558 Farm Tools, leaving no Housing Materials or canal construction.
This identifies a real loss of the funded input allocation, without requiring
housing to take the canal's Builder labor.

### Housing Material output loses the Builder tool multiplier

The Elder prices Housing Material labor using the construction-tool multiplier
(1.5× for regular Tools). Forecast and production take a different path when an
irrigation order is present: because Housing Material has `Equipment = false`,
the output cap uses `labor * rate`, without the multiplier. Time charging still
uses `ToolCoverageBudget.Time`, which includes the multiplier. Output is cut
even though usable Builder time is left over.

A controlled fixture with ample raw materials and tools reproduces the exact
turn-91 fraction. Four Builders over 10.373449 years need 11.037345 materials to
commission one segment. The cap permits 10.373449, so forecast and production
both stop at 0.939850 segments and leave 6.694644 Builder-years unused. Five
Builders clear the erroneous cap and complete one segment. This is a capacity
calculation error, not evidence that the extra fifth Builder is economically
necessary. The earlier commissioning regression used abundant Builder labor,
which did not expose this minimum-staffing case.

The relevant paths are `DemandControl.Craft`, `MaterialEconomy.Forecast.Step`
and `NeolithicProduction.Produce`; `ToolCoverageBudget.Time` documents the
shared construction multiplier.

## Additional storage omission, separately reproduced

`GoodsStorage.PlannedUse` / `GoodsStorage.Begin` exempt Housing Materials for
fixed housing assembled during the turn. They do not include materials consumed
by canal construction or upkeep. A controlled full-store fixture consequently
blocks canal material production while admitting the same quantity earmarked
for housing. This is a separate integration defect. Goods storage is disabled
in the recorded 80-tribe profile, so it caused none of the eleven shortfalls.

## Correction required

Keep the common Builder profession and explicit housing/canal/upkeep job
quantities. Count implicit tool production once, use the same material capacity
and time calculation in allocation/forecast/resolution, and apply the in-use
storage exemption to all authorized construction consumers. Reconcile the final
funded job quantities with the executable production plan rather than leaving
an optimistic Elder grant when a later stage cuts output. Tests must exercise
minimum funded staffing, shared input manufacture and mixed construction jobs,
including tight storage. These corrections have not been applied by this audit.

## Reproduce

```bash
dotnet build Tools/AgriculturalTechnologySweep/Anthropocene.AgriculturalTechnologySweep.csproj -c Release
dotnet Tools/AgriculturalTechnologySweep/bin/Release/net8.0/Anthropocene.AgriculturalTechnologySweep.dll --grant-audit-fixture
dotnet Tools/AgriculturalTechnologySweep/bin/Release/net8.0/Anthropocene.AgriculturalTechnologySweep.dll --grant-audit
python3 Tools/AgriculturalTechnologySweep/SummarizeGrantAudit.py
```

The native audit loads the retained opening save at
`TestResults/AgriculturalGameplay80/opening.anthro` and runs through turn 107.
Detailed before/after records for each affected camp are written under
`TestResults/ConstructionGrantAudit`. Diagnostic limits permit named nonfinite
values such as an unlimited storage bound; these do not change game state.
The [controlled fixture output](Experiments/ConstructionGrantAudit-20261113-fixtures.json)
is retained with this report.
