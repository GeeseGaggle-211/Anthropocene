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

## Slice 2 (next)

Ordered by measured effect:

1. **Herd service and herd policy.** A herd-service controller requests the herders for the herd the selected policy
   grows to, not only the herd held, at class 2 (operating continuity of a retained asset, Addendum A §A4). The Elder
   strategy (v0.1 §13) sets the policy: growth target, breeding and slaughter. It reuses `HerdLabour`'s physical
   capacity and food projections without its Value prices.
2. **Field operation and development.** A field-service controller operates the supervisor-selected fields. The
   strategy layer authorizes expansion, as class 4 development, at review boundaries or triggers, reusing the expansion
   ladder's physical steps. This also restores Farm Tools pricing (`w_farm`, §A2).
3. **Cost.**
   - One known-source query per plan: thread `d.Sources` through the `Demand` helpers that still query their own.
   - Needs preparation reuses the plan's realization instead of its own forecast.
   - Measure per-settlement cost after 1–2, which add work.
4. **Census gates for a default change:**
   - population and QoL per founding regime no worse than Legacy;
   - the husbandry gate;
   - delivery and trade gates;
   - 80/320-tribe scale.
