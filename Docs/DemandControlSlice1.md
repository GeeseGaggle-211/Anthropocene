# DemandControl slice 1

**Status:** implemented and opt-in (`EconomicAllocation = "DemandControl"`; default `"Legacy"`), 4 October 2026.

**Design:** [EconomicControlContract.md](EconomicControlContract.md) v0.1 with the approved
[Addendum A](EconomicControlContractAddendumA.md).

**Result in one line:** the slice meets the contract's mechanism acceptance and the accepted cost target. It is **not
yet competitive** on population, because it authorizes no development: no herd growth and no field expansion. That
is slice 2.

## What was built

| Contract item | Implementation |
| --- | --- |
| §15 strategy selector | `SimulationConfig.EconomicAllocation`, `"Legacy"` or `"DemandControl"`. OptionalField: absent or older saves load Legacy. Validated, and carried in `OpeningConfig.json` (Legacy). |
| §15 one dispatch boundary | `EconomicAllocationStrategy.Plan`, called by `TurnPlanner.AutomaticIntent` and `EconomicIntents.Automatic`. Legacy is the Value/Uniform planner exactly (checked). |
| §2 physical projection without valuation | `CampProductionDemand.BuildPhysical`. Legacy `Build` now calls it and then adds claims and valuation in the same order. `legacyProbes: false` drops the grown-herd forecast and the fictitious-builder shelter probe. |
| §15 ceilings carried through | `ProductionOrder` can carry per-good output ceilings. Every ordinary `MaterialEconomy.Forecast` step is capped by them. The resolver produces against the forecast (`TurnNeeds.Production`), so it never exceeds the allocation. Legacy orders carry none. |
| §§5–8 controllers and Elder | `DemandController.Plan` (`DemandControl.cs`), described below. |

**Controllers.** In slice 1, development is a fixed directive of none.

| Class | Requests |
| --- | --- |
| Survival | Herders for the current herd (`HerdingTooling.RequiredHerders`); operating farmers and food gatherers, by Legacy's own physical formulas for the food target. |
| Continuity | This turn's clothing consumption (one service: the Cloth route for farming camps, Animal Fabric for herding ones); shelter replacement of this turn's decay; Farm and Herding Tools for the work staffed. |
| Recovery | The clothing reserve; the shelter deficit; Pots for the storage gap. |

- **Shelter routes.** Farming camps use fixed Housing. Herding camps use tents, a route included from the start
  because a herding camp has no other shelter. One route claims the whole service (§6).
- **Bundles** carry the complete upstream work (§7):
  - craft time at the actual role rate;
  - made intermediates: Animal Fabric for tents and Housing Material for Housing, with stock credited once;
  - raw extraction beyond stock, within reachable sources;
  - feedstock within the food envelopes, and its gathering;
  - tool service, with toolmaking at the cheaper of Hand or Regular Tools and the urgency of the work it enables.
- **Arbitration** (§8):
  - classes are granted in order, with an equal fulfilment fraction within a class;
  - a saturated input freezes only the requests that use it;
  - staffing is compiled per role after aggregation, with at most one repair pass and then a stable trim;
  - every shortfall names its cause.
- **Trade (Addendum A §A2).** While trade is on, DemandControl publishes `LabourPrice` as the value of a spare resident's
  gathering. The marginal-bundle price under binding labour is later work.

**Checks.** `DemandControlChecks` (132 assertions) is in the fixed suite. It covers:
- configuration;
- purity and determinism;
- Legacy equivalence;
- legal intents;
- the §9 Housing reference case;
- saturated labour and materials;
- ceilings honoured over four resolved turns;
- scalar against reversed 4-worker replay;
- save continuation.

## Results

**Legacy unchanged.** `Run-TradeAudit.py --variants reference,off` is exact against the pre-trade reference on all
five worlds (`TestResults/DC-exact`).

**§9 Housing reference case.** 480 residents, 120 units, 10% decay:
- 12 units requested and granted;
- **5 Builders** (4.8 compiled);
- 12 made;
- 120 units at turn end;
- replacement requested again the next turn.

**Census, Legacy against DemandControl.** Five Standard/80 worlds, 50 turns, census configuration. Output is in
`TestResults/DC-S1` (`Run-Profile.py --variants none,none-dc,all,all-dc`). Trade and immigration off:

| | t25 Legacy | t25 DemandControl | t50 Legacy | t50 DemandControl |
| --- | --- | --- | --- | --- |
| Population | 203,904 | 100,649 (−51%) | 1,377,024 | 151,075 (−89%) |
| Settlements | 668 | 496 | 3,085 | 578 |
| QoL, population-weighted | 0.860 | 0.893 | 0.803 | 0.874 |
| Clothing / housing per capita | 0.88 / 0.98 | 0.96 / 0.91 | 0.94 / 0.99 | 0.92 / 0.94 |
| Husbandry-first animals, total | 9,187 | 1,563 | 408,927 | 11,525 |

With fission, immigration and stock-only trade on, the pattern is the same: population −89% and QoL 0.869 against
0.793 at t50.

By founding regime at t50:

| Founding regime | Population | QoL |
| --- | --- | --- |
| Husbandry | −95% | +6.8 points |
| Cultivation | −72% | +7.5 points |
| Watercraft | −60% | +5.1 points |

**Reading.**
- **Herd collapse.** Herders are reserved only for the herd as it stands, so a herd never grows. Fission keeps
  splitting it, and the median herd falls to 2 animals. Legacy grows herds by staffing herders beyond the requirement
  under the herd-labour coupling.
- **No growth in food.** No field is ever expanded, so food supply, and with it population growth, stays flat.
- Both are **development**, which slice 1 deliberately does not authorize. Its own services work: clothing and QoL are
  higher, food is met, and shortfalls are explicit.
- v0.1 §16 treats population ordering as a separate calibration from mechanism acceptance. **Legacy stays the
  default.**

**Cost**, on identical snapshots (`TestResults/PlannerCost/saves`, t25–t50, C1 and A1), scalar, against the accepted
Addendum A §A7 target:

| Measure | Legacy | DemandControl | Target |
| --- | --- | --- | --- |
| Allocation per plan | 0.98–2.06 ms | **0.16–0.21 ms** | ≤ 0.40 ms (met) |
| Forecasts per plan (needs excluded) | 4.1–4.7 | 0 | 1 (met) |
| Tool schedules per plan | 8.2–9.5 | 2.0 | ≤ 2 (met) |
| Known-source queries per plan | 6.5–6.9 | 4.9–5.0 | 1 (**not met**) |
| Decide + prepare per settlement | 1.76–4.02 ms | 0.76–1.28 ms | ≤ 1.0 ms (4 of 6 states) |

## Slice 2 (in progress)

Census: five Standard/80 worlds, 50 turns, fission, immigration and stock-only trade on (`all` against `all-dc`).
Legacy is `TestResults/DC-S1`; the slice 2 runs are `TestResults/DC-S2a` to `DC-S2f`.

**What changed.**
1. **Herd service (S2a).** Class 2 (Continuity) requests the herders for a herd grown by one turn of breeding, capped
   at what the local pasture's forage and water carry (`HerdLabour.Pasture`). The intent holds the herd at what the
   staffed herders manage (`HerdTarget`, `CapBreeding`).
2. **Field development (S2a).** A projected food gap (food targets plus Cloth feedstock above supply) is closed with
   the expansion ladder's physical steps, best first. Their build worker-years are requested as class 4 (Development)
   Farmers.
3. **Mixed shelter (S2d).**
   - Decay replacement uses the durable route: fixed Housing where agriculture is enabled.
   - The deficit uses the quick route: tents where husbandry is enabled.
   - S2a gave husbandry camps fixed Housing only, so their housing fell (0.42 against 0.78 for Legacy at t5).
   - S2c gave every herding camp tents only. Tents carry a lower cohesion weight, so QoL fell (0.728 at t50).
4. **Field operation matches the resolver (S2f).**
   - Operating Farmers now cover field upkeep only. Food beyond what they harvest is staffed as gathering.
   - In the resolver, gathering on the footprint also builds field capital through the gatherer bridge, up to
   `GathererBridgeCeiling` (0.25). Harvesting Farmers do not build capital.
   - Before S2f, DemandControl staffed the whole harvest as Farmers, so fields stalled at q ≈ 0.03–0.11 while the
   farming crew grew to 167 (median). Legacy's fields reach q = 0.25 by t25 with 47 farmers.

**Rejected.**
- S2b, a herd hold based on the food gap, collapsed herds.
- A labour-payback trigger for intensification (steps whose discounted harvest-labour saving repays their build work)
  never fired. Early ladder steps cost 7–440 worker-years for 0.4–9 food a year, so S2e was identical to S2d.

**Census at t50.**

| | Legacy | S2d | **S2f** |
| --- | --- | --- | --- |
| Settlements | 2,997 | 3,212 | **3,747** |
| Population | 1,342,553 | 1,244,504 | **1,598,982 (+19.1%)** |
| QoL, population-weighted | 0.793 | 0.796 | **0.809** |
| Housing per capita | 0.985 | 0.952 | 0.961 |
| Prepare + execute, mean of last 10 turns | 1,464 ms | 987 ms | 1,284 ms |
| Per settlement, last 10 turns | 2.44 ms | 1.54 ms | 1.71 ms |

**Founding lineages, S2f against Legacy, at t50.**

| Founding regime | Population | QoL |
| --- | --- | --- |
| Husbandry | +3.3% | −0.3 points |
| Cultivation | +79.8% | +6.5 points |
| Watercraft | +44.8% | +3.7 points |

**Calibration estimates.** These are the user's non-binding targets for median settlement population with fission,
immigration and trade, reported by `Tools/PerformanceAudit/Summarize-Settlements.py`. They are never enforced.

| Regime at t50 | n | p25 | Median | p75 | Estimate |
| --- | --- | --- | --- | --- | --- |
| Cultivation (S2f) | 981 | 252 | **480** | 721 | 600 (−20%) |
| Husbandry (S2f) | 2,669 | 239 | **383** | 593 | 250 (+53%) |
| Cultivation (Legacy) | 591 | — | 477 | — | |
| Husbandry (Legacy) | — | — | 440 | — | |

**Husbandry gate** (`Summarize-Trade.py`, husbandry-first camps).
- Total linked population passes: +0.1% at t25, +3.3% at t50.
- Animals are about double Legacy's.
- **The median linked population per camp fails:** −32.5% at t25, −13.2% at t50. More fission produces more,
  smaller camps.
- That direction moves toward the 250 estimate, so the gate's "median no worse than Legacy" rule and the estimate
  pull in opposite directions. This needs a decision before a default change.

**Cost** (identical snapshots `TestResults/PlannerCost/saves`, C1 and A1 at t25 and t50, scalar; a scratch build with
scoped timers; 6–8 repetitions; run-to-run noise about ±0.15 ms per settlement):

| Measure | Slice 1 | Slice 2 | Target |
| --- | --- | --- | --- |
| Allocation per plan | 0.16–0.21 ms | 0.26–0.35 ms | ≤ 0.40 ms (met) |
| Forecasts per plan | 0, plus needs' 1 | 0, plus needs' 1 | 1 (met) |
| Tool schedules per plan | 2.0 | ≤ 2 | ≤ 2 (met) |
| Known-source queries in the plan | 1 | 1 | 1 (met) |
| Needs preparation per camp | 0.13–0.19 ms | **0.07–0.09 ms** | — |
| Decide + prepare per settlement | 0.76–1.28 ms | 0.91–1.19 ms | ≤ 1.0 ms (about half the states) |

- **Slice 2's added plan cost:**
  - herd service: 0.06–0.09 ms per settlement (two managed-herd forecasts and a pasture scan);
  - field ladder: 0.02–0.04 ms.
- **Source queries.** The slice 1 figure of about 5 per plan counted every query in decide + prepare. Attributed by
  caller, the plan makes exactly one (`BuildPhysical`). The others are:
  - two settlement observations (AI decide and order validation);
  - needs preparation;
  - trade valuation.
- **Needs preparation halved.** `TurnNeeds.Goods`, a per-recipe demand pass, is read by no simulation pass; its only
  reader is `Tools/NeolithicCraftValidation`. It is now filled only while
  `ResolutionMetrics.CaptureCraftWorkDiagnostics` is on. This applies to both strategies.
  - Legacy stays exact against the pre-trade reference on all five worlds over 50 turns (`TestResults/DC-exact2`).
  - The focused headless suite passes.
- **Remaining per-settlement budget, outside the planner and the same for both strategies:**

  | Part | ms per settlement |
  | --- | --- |
  | AI decide (movement) | 0.23–0.46 |
  | Validation observation | 0.11–0.25 |
  | Trade handshake | 0.08–0.13 |

  The planner's own share (plan plus needs) is 0.34–0.43 ms. Meeting 1.0 ms reliably therefore needs work on those
  shared phases, not the allocator.
- **Not done.** Merging the herd service's forecasts with `BuildPhysical`'s would save about 0.03 ms. It is not clearly
  exact, because the forecasts use different herder counts, and the plan already meets its target.

**Gates for a default change:**
  - delivery and trade gates;
  - 80/320-tribe scale;
  - Legacy exactness re-run.
