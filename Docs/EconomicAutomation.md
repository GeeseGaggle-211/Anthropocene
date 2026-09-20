# Demand-based Neolithic automation

## Reconciliation and scope

Implemented on `features/neolithic-agricultural` after the September 20 design reconciliation and the user's approval of demand-based extractor staffing, Clay Miner/Pottery, and persistent automation. The latest user instruction supersedes the technology document's earlier statement that Pottery has no paired extractor. No recipe, population-growth, ecological, mastery-threshold or QoL calibration was changed.

Discrepancies found before editing:

- Automatic allocation reserved enough gatherers to feed everyone, then assigned generalist crafts, leaving farmers the remainder. Existing assignments prevented later specialist adoption.
- Material gathering depended on food-gathering leftovers; a fully occupied food workforce could block tool bootstrapping.
- Farmer upkeep was incorrectly treated as a maximum harvesting workforce. Every additional farmer expanded fields, and farmer claims were restricted to owned fields rather than the shared accessible footprint.
- Clay had no specialist. The inspector's allocation button was a one-time assignment, not a saved policy.
- Fabric feedstock could be counted again as growth surplus. This was exposed during integrated automation review and is now reserved before growth allocation.

## Operation

The settlement inspector offers **Allocate once**, **Off**, **Next N turns**, and **Until disabled**. A positive duration includes the resolution on which the toggle is submitted. Replanning occurs after movement/dispatch and deposit of returning provisions, before the economy, through the same economic-intent validator used by player and AI orders. One countdown tick occurs per resolved turn. No new order is needed on subsequent turns. Manual workforce edits disable automation and clear its labor reservations. Turning automation off otherwise retains the last staffing/working-time plan. Priorities and stock targets remain editable while automation is on.

The pure `EconomicPlanner` sizes useful manufacturing from stock-adjusted Clothing, once-depreciated Housing including anticipated growth, and explicit Pots/Farm Tools targets. It uses current recipe-local productivity and shared tool coverage, prefers unlocked matching specialists, checks known accessible source capacity, and budgets input gathering and plant-food demand. Integer job assignments use a bounded fulfillment search to fit one resident workforce. This is a deterministic local heuristic, not a global optimum or a promise that all demand is feasible.

Existing fields receive operating farmers first. Those farmers may harvest the settlement's shared accessible food, subject to the same ecological cap and demand as gathering. Supplemental food gatherers fill the remaining gap. Material-gathering worker-time is explicitly reserved and cannot also harvest food. Expansion farmers are separately reserved; at q=0 they cannot harvest. Extra operating farmers do not multiply the field-operation factor above one. Old manual plans with no explicit labor plan retain their maintenance/surplus-expansion split.

Expansion is considered against the next demand forecast, not an assumed century of exponential population growth. The planner checks net marginal ecological benefit, including canonical clearing pressure and the existing sustainable-production thresholds. This does not impose a new physical q cap, change the cultivation equation, or prevent manual investment. The resolver still uses its repeated marginal tile allocator and real labor cost.

Equipment may be bootstrapped for the next source-feasible useful manufacturing plan. This target is bounded by real residents; production still consumes its actual inputs and newly produced tools cannot retroactively cover the interval that made them. Input food is protected from simultaneous growth spending. Imported/stored food enters the existing consumption/reserve/growth budget; there is no local-self-sufficiency assertion.

## Extractors and calibration

| Material | Specialist | Unlock |
| --- | --- | --- |
| Stone | Stone Quarrier | Toolmaking |
| Wood | Woodcutter | Carpentry |
| Clay | Clay Miner | Pottery |

The internal `Stonecutter` identifier is retained for save compatibility; `Stone Quarrier` is the display label. `ClayMiner` is new. Specialist extraction remains `SpecialistMaterialExtractionRate=1` unit/worker/BaseDays; regular gatherers use `GeneralistTaskEfficiency=.25`. These match one-unit, single-material manufacturing throughput. Staffing is demand-based, not a mandatory one-for-one headcount: Housing's 2.5/20-year Builder rate needs much less extraction, and improved tools retain their separate Stone and Wood inputs. Reserve replenishment can temporarily require more extraction than current production alone.

All existing recipe/QoL/tool multipliers remain unchanged, including Fabric's one Plant Food input, Housing's one total Wood/Clay input, and specialist/generalist matching. Numerical search iterations and rounding tolerances are algorithmic controls, not new balance rates.

## Persistence and limits

`Camp.EconomyAutomationTurns` and `EconomicLabor`, and their `EconomicIntent` counterparts, are optional additive metadata in the existing `ANTHROPOCENE-11` envelope. Old saves default to Off with no labor reservation; explicit zeros are preserved. No tile/resource binary layout or world generation changes. Active automation, copied orders and countdowns round-trip deterministically. Stale field focus is cleared after relocation.

Source ledgers remain authoritative. Neighboring settlements can forecast demand against the same shared source; contention is settled by actual extraction receipts and can leave the later settlement short. There is no shared-source reservation market or global economic optimizer. Local projections do not guarantee actual yield under competition, ecological change or food scarcity; automation replans on the next turn.

Pots and Farm Tools still default to zero target because their broader effects remain deferred. Default runs therefore need not unlock Pottery/Carpentry or their extractors. Toolmaking cannot be forced through stockpiling after useful durable-tool demand is satisfied. No artificial wear, invented demand, free technology, Copper progression, irrigation effect, generic tool bonus, automatic fission, full trade, or expedition formula was added.

Validation evidence and measured sweep results are recorded separately after execution.
