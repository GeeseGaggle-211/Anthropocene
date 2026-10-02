# Why husbandry tribes still form agricultural economies

Source-traced follow-up, 1 October 2026, on the corrected
`features/herd-ecology-retention` candidate. The user requested this
investigation after [CropHerdBalanceValidation.md](CropHerdBalanceValidation.md)
showed roughly one domestic animal unit per 79 husbandry-primary people.
No production mechanics were changed during this follow-up. Its findings
therefore do not alter the five-world census or 25/50-turn performance results.

**Confirmed: the planner still has structural choices that favor agriculture.**
Crop expansion receives spare workers outside its priced claim, while extra
herders compete at a positive labour price. Herd valuation also treats
protected food inventory differently from field valuation. In addition, it
evaluates retained animals as an addition to the current field economy; it
does not compare a pastoral allocation with reduced field operation. These
are distinct from a remaining desert-biomass or gross-renewal error.

## Evidence and scope

The diagnostic replay starts from the corrected Continents/seed-1 opening
checkpoint used in the five-world audit. It follows all original
husbandry-primary camps for 50 post-opening turns and inspects all live
husbandry-primary camps at opening, turn 25 and turn 50. At turn 50, all
207 camp populations, linked populations, domestic animal counts and QoLs
match the original corrected C1 census exactly; total world population is
152,929 in 371 tribes. The diagnostics build a **stationary next-turn plan**,
not the AI's alternative destination plan. Counts below describe that pure
planning query, not an assertion that every camp actually stays next turn.

Counterfactuals are one-plan queries: zero food inventory; removing the
camp's own field capital and Cultivation capability; or endowing it with a
disclosed large herd. They preserve geography and other inputs, are never
resolved into the live replay, and restore every temporary change. Whole-world
fingerprints verify restoration at each snapshot. A gifted herd is not a
feasible animal-acquisition action and is not a forecast of long-run survival.

| C1 post-opening snapshot | Husbandry camps / herd owners | Median animals | Median local pasture capacity | Owners with breeding capped | Camps expanding fields | Expansion with empty valued claim |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Opening | 21 / 21 | 2 | 555.5 | 16 | 0 | 0 |
| Turn 25 | 33 / 31 | 4 | 747.5 | 16 | 16 | 8 |
| Turn 50 | 207 / 187 | 5 | 759.3 | 109 | 76 | 22 |

At turn 50, 186 of the 187 herd owners have local pasture capacity above
100 units in the existing forage/water envelope. This is the planner's
capacity proxy, not a guarantee of simultaneous human/herd receipts; it is
still far above actual herds. For 182 owners, current projected human
forage/crop supply already covers annual meals, so the herd continuation
has no direct future meal-gap bucket. That helps explain why additional
milk receives little food-security value despite large physical pasture.

## 1. Spare field expansion bypasses the value comparison

`EconomicPlanner.Plan` selects the herd through
`HerdLadder.Staffed(price)` using the Value rule's labour price. After the
fulfilment search, lines 609–627 calculate `spare = Population - best.Workers`
and `expanders = spare + best.ExpansionWorkers`. The expansion gate checks
projected food demand, supply, and any physically retained field gain. It
does **not** require those spare workers' field steps to beat the price or
even require a nonempty valued expansion claim. A settling-effort cap may
limit the result; there is no corresponding spare-worker herd-investment pass.

All 76 expanding C1 husbandry plans at turn 50 contain workers from this
unpriced remainder. In 22, `candidateDemand.Expansion` is empty. This is a
policy asymmetry, not a statement that every remaining worker has no useful
crop opportunity. The priced crop ladder and physical expansion gate are
different mechanisms, and the latter overrides the former for spare labour.

A concrete turn-25 case, camp **1425**:

- 408 residents, six animals, three selected herders, recommended breeding
  `.55` but selected breeding `.17168` to hold the target at six.
- The plan assigns **96 workers to field expansion**, although its valued
  expansion claim is empty and its reported farm labour price is zero.
- At the same positive labour price `.0443174`, a fourth herder retaining
  eight animals loses `.58647` person-QoL-years in the portfolio objective.
- If only that extra herder's **current** labour is supplied by an otherwise
  spare worker, while keeping all future work costs/prices unchanged, the
  difference becomes **+.21687**. It can be preferable to invest the spare
  current labour in herd growth under the model's own continuation equations.

This sensitivity is positive for five herd owners at turn 25 and 31 at turn
50. It is not a new planner implementation or a promise that all 31 should
choose herding: positive field opportunities still require comparison. It
demonstrates that the final crop fallback and the earlier herd price do not
give the alternatives the same treatment of spare current workers.

## 2. Stored food is credited inconsistently in valuation

The earlier crop package removed a second inventory subtraction from saved
harvest labour. A separate discrepancy remains in the **value buckets**.

`TurnResolver.FoodBucketTargets` (`GenerationalEconomy.cs`, lines 727–734)
protects the operating reserve: only inventory above `reserveTarget` is
treated as available for initial consumption. Field valuation calls this
helper in `ExpansionValuation.Apply` (`ProductionDemand.cs`, lines 649–655)
and subtracts forage from the resulting consumption/reserve/growth income
targets.

`HerdLabour.Build` (lines 330–337) instead computes
`humanNow = PlantFood + AquaticFood + ForageAfterHerds`, credits all that
inventory toward meals, then subtracts the remainder from reserve refill
and growth. This can credit protected stock as current meal income and again
as remaining support. Incoming animal food is then priced against smaller
gaps than incoming crop food. Existing Animal Food and typed craft inputs
add further bookkeeping requirements; a repair needs a common ledger, not
another isolated subtraction.

Turn-50 camp **2046** exposes the difference:

| Quantity for the incoming twenty-year plan | Value |
| --- | ---: |
| Current meals | 32,960 |
| Food inventory | 39,292.29 |
| Protected reserve target | 49,440 |
| Human forage/crop envelope after existing herd claims | 32,889.74 |
| Common-helper meal income gap after forage | 70.26 |
| Common-helper reserve refill gap | 10,147.71 |
| Common-helper growth-food gap | 2,168.13 |
| Herd calculation's corresponding three food gaps | **0 / 0 / 0** |

The field calculation therefore prices reserve/security food while the
herd calculation sees no corresponding current food gap. Its existing
Animal Food also covers the typed craft bucket, so every herd choice in
this camp has zero current value; continuation and salvage decide retention.

The aggregate discrepancy affects 62 of 187 C1 herd owners at turn 50
(any meaningful consumption/reserve/growth difference); five have a direct
meal-gap mismatch. It is confirmed arithmetic inconsistency, but these
counts do not establish that fixing it alone would create pastoral economies.
The zero-inventory sensitivity changes several selected targets, while also
changing legitimate reserve and typed-feedstock needs.

## 3. The herd forecast assumes the existing field economy continues

`CampProductionDemand.Build` reserves current field upkeep (line 317) and
calculates potential human receipts from the current cultivated footprint
(lines 325–334). `HerdLabour.Build` uses that same
`ForageAfterHerds / years` as future human food (line 315), then values milk
against the remaining meal/growth gaps and typed Animal Fabric/tent needs
(lines 342–345). Its worker savings preserve field upkeep (lines 413–420).
`EconomicPlanner.Plan` also reserves the larger of upkeep and harvesting
work (lines 505–506).

Those calculations consistently ask, “How valuable are more animals **while
these fields remain operated**?” There is no candidate asking, “What if we
reduce field operation, replace crop harvesting/maintenance with herding,
and use the same land's nonhuman biomass?” Consequently, current crops close
the future meal gap before livestock is priced; once a small herd supplies
the clothing stream, much of its additional food has only a harvest-labour
saving, not the full value of a competing subsistence system.

This is measurable in field-removal sensitivities:

| C1 turn-50 camp | Residents | Actual animals | Target with current fields | Target with own fields removed and Cultivation disabled | Breeding with fields → without |
| --- | ---: | ---: | ---: | ---: | --- |
| 1415 | 693 | 10 | 10 | 13 | 0 → .55 |
| 1425 | 699 | 10 | 10 | 13 | 0 → .55 |
| 2046 | 412 | 13 | 8 | 16 | 0 → .45 |
| 2178 | 258 | 5 | 5 | 6 | .36087 → .45 |

For camp 2046, removing fields changes the projected annual meal gap from
3.51 to 1,029.98 food and makes the planner retain all 16 forecast animals
rather than cut to eight. This combines removal of crop receipts, upkeep,
and crop capability; it is evidence of dependence on the field baseline,
not an isolated maintenance-only effect or a simulated sustainable outcome.

## 4. Herd growth is a narrow available choice, then explicitly suppressed

`HerdLabour.Build` only creates management choices up to the **one-turn grown
herd forecast** (`HerdLabour.cs`, lines 293–300). `EconomicPlanner.Plan`
sets the resulting target and calls `CapBreeding` (lines 632–634), which
reduces reproduction so it does not exceed that target. There is no purchase/
domestication/recruitment investment capable of turning spare residents into
hundreds of animal units; acquisition is still the separate #12 work.

At corrected C1 opening, the median herd is only two units despite a median
pasture proxy of 555.5; 16 of 21 owners already cap breeding. By turn 50,
109 of 187 cap it. Thus slow accumulation is partly an active economic
decision, not mainly observed starvation. The finite continuation does value
breeding capital, but its small profitable seed choice can assume later
growth while the next actual plan repeatedly selects another capped target.
This mismatch between a promised continuation and the rolling policy should
be included in the next retention audit.

Even gifting a large herd does not automatically produce pastoralism. In
camp 1425's turn-50 query, endowing 699 animals yields a retained target of
ten, well below the local pasture proxy 1,170.6. The experiment is hypothetical
and does not prove combined water/food feasibility. It does establish that
the current field-conditioned objective can liquidate a large endowment;
initial acquisition alone is not a complete explanation.

## 5. The examined desert agriculture occurs on wet river fields

Camp 2046 is on a desert site and its seven-land-tile arithmetic mean soil
moisture is `.2833`. That mean hides its actual agricultural resource:

| Local tile | Field q | Mean soil moisture | Crop suitability | Current biomass capacity | Crop food capacity |
| --- | ---: | ---: | ---: | ---: | ---: |
| 192517 | .4957 | .9894 | 1 | 49,189.73 | 10,577.34 |
| 193540 | .4907 | .9911 | 1 | 49,693.09 | 10,471.43 |

The other five land tiles have essentially no current biomass/food and no
fields. Its two productive river tiles supply almost all harvestable food.
The new crop curve correctly leaves these wet tiles productive. The global
footprint mean `q=.1409` also hides the two near-`.5` fields. Because village
status uses an unweighted land-footprint mean with threshold `.25`, this
crop-dependent camp is still classified as non-village. That classification
is not evidence of pastoral subsistence or weak cultivation on its productive land.

Across the inspected C1 husbandry camps at turn 50, **99.26% of crop food
capacity lies on tiles with full crop suitability**. Moisture dampening has
little further effect on those chosen fields. This is capacity, not realized
receipts, and covers one world only. It supports the distinction between
arid climate labels and locally fertile river/oasis land; it does not settle
worldwide climate balance or justify further reducing fertile benchmarks.

The field allocator already includes build effort **and** a
`PlanningHorizonYears` maintenance charge (`CultivationEconomy.HorizonMarginalInvestment`,
lines 642–648; `FoodInvestmentForecast.AnnualizedFoodPerWorkerYear`). The
`R=16` effort curve above `.4` remains active. Claiming that it entirely
ignores field upkeep or that gross managed renewal is still the main crop
valuation would be incorrect. Future incremental harvest work/tool replacement
and the choice to abandon existing upkeep are separate comparability questions.

## Recommended next correction

The user's subsequent four directives and a concrete shared-planner
proposal are recorded in
[PastoralPlannerCorrectionDesign.md](PastoralPlannerCorrectionDesign.md).
It includes source-derived maintenance requirements, food-security treatment,
joint operation choices and the validation contract.

Fix the common food ledger first, with consumption, protected reserves,
typed feedstock, growth, and existing inventory credited identically for
each source. Then put spare field expansion and additional herd management/
breeding through the same candidate comparison, distinguishing current spare
labour from future labour costs. Compare maintain/reduce/expand fields with
retain/grow/slaughter herds on one finite horizon rather than treating fields
as an unquestioned baseline. Verify that the realized rolling policy can
deliver the breeding continuation used to price the asset.

These are planner/accounting corrections to investigate and implement next,
not an approved animal-productivity increase. A subsequent matched replay
should report food-source shares, fields, herd sizes, breeding caps, QoL and
25/50-turn performance in addition to primary-technology populations. Keeping
the fertile-land benchmark is compatible with this investigation; another
global moisture penalty would not address the confirmed spare-labour and
inventory discrepancies.

## Reproduce

Uses the unchanged corrected simulation-source digest
`2232bfce6b4fa6194be0d748a93fdf4979525efe4dc9e8227c998db2cdfbe25a`.
The tool compiles the actual simulation sources and adds diagnostics only.

```bash
source /workspace/anthropocene-cloud/activate.sh
dotnet build Tools/PerformanceAudit/PastoralPlannerProbe.csproj -c Release
dotnet Tools/PerformanceAudit/bin/Release/net8.0/PastoralPlannerProbe.dll timeline \
  TestResults/CropHerdBalance/gameplay/corrected-C1-opening.save \
  TestResults/CropHerdBalance/pastoral-C1
for audit_turn in 0 25 50; do
  dotnet Tools/PerformanceAudit/bin/Release/net8.0/PastoralPlannerProbe.dll snapshot \
    TestResults/CropHerdBalance/pastoral-C1/turn-$audit_turn.save \
    TestResults/CropHerdBalance/pastoral-C1-final $audit_turn
done
python3 Tools/PerformanceAudit/Summarize-PastoralPlanner.py
```

The opening save can be regenerated with the commands in
[CropHerdBalanceValidation.md](CropHerdBalanceValidation.md). Ignored raw
outputs contain founder timelines, three exact state snapshots, all candidate
portfolio components/objectives, physical field profiles and the disclosed
counterfactuals. This follow-up is scoped to one matched generated world;
its planner arithmetic and control-flow findings are source-wide, while its
population counts and sensitivity frequencies are not a five-world average.
