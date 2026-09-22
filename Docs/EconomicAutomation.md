# Demand-based Neolithic automation

## Reconciliation and scope

Implemented on `features/neolithic-agricultural` after the September 20 design reconciliation and the user's approval of demand-based extractor staffing, Clay Miner/Pottery, and persistent automation. The latest user instruction supersedes the technology document's earlier statement that Pottery has no paired extractor. No recipe, population-growth, ecological, mastery-threshold or QoL calibration was changed.

Discrepancies found before editing:

- Automatic allocation reserved enough gatherers to feed everyone, then assigned generalist crafts, leaving farmers the remainder. Existing assignments prevented later specialist adoption.
- Material gathering depended on food-gathering leftovers; a fully occupied food workforce could block tool bootstrapping.
- Farmer upkeep was incorrectly treated as a maximum harvesting workforce. Every additional farmer expanded fields, and farmer claims were restricted to owned fields rather than the shared accessible footprint.
- Clay had no specialist. The inspector's allocation button was a one-time assignment, not a saved policy.
- Fabric feedstock could be counted again as growth surplus. This was exposed during integrated automation review and is now reserved before growth allocation.

### Post-growth Fabric budgeting

The generation resolver now projects Fabric against the same resident population that will exist after the turn. The projection starts with the camp's residents plus connected hunters as the reproductive population, computes a candidate growth rate, converts that rate to the resulting integer births (including the carried fractional remainder), and asks `FabricFeedstockNeed` for that camp-resident count. A deterministic monotone bisection selects the greatest feasible rate while charging both growth food and the actual integer-population Fabric input to the available surplus. The fractional remainder is progress only; it is never treated as a person or a Fabric customer.

The resolver keeps pre-turn stock separate from current-turn income. In particular, connected hunting can add `StepFoodIncome` after the planning barrier; it is passed as income to the forecast while the prior stock is passed separately, so the same hunt is not counted twice. Plant feedstock is accepted using the projected resident demand, then actual ecological receipts and inventory determine what production can consume. Existing food stockpiles can bridge a shortfall. Fabric input remains protected from the consumption/reserve/growth ordering and cannot fund births.

This is a deterministic coupled feasibility search, not a promise that a pre-allocation forecast equals realized yield. Shared tiles, seasonal ecological limits, prior claims, and changing food stocks can reduce actual receipts; the resolver reconciles those receipts and the next turn replans from authoritative state. The one-unit Fabric Plant Food input and all growth, reserve, and production constants are unchanged.

## Operation

The settlement inspector offers **Allocate once**, **Off**, **Next N turns**, and **Until disabled**. A positive duration includes the resolution on which the toggle is submitted. Replanning occurs after movement/dispatch and deposit of returning provisions, before the economy, through the same economic-intent validator used by player and AI orders. One countdown tick occurs per resolved turn. No new order is needed on subsequent turns. Manual workforce edits disable automation and clear its labor reservations. Turning automation off otherwise retains the last staffing/working-time plan. Priorities and stock targets remain editable while automation is on.

The pure `EconomicPlanner` sizes useful manufacturing from stock-adjusted Clothing, once-depreciated Housing including anticipated growth, the existing desired food reserve's natural-storage gap for Pots, and forecast field work for Stone Farm Tools. Pots add `PotStorageFoodPerUnit` absolute food capacity; their projected stock is retained with duration-aware `PotDecayPerReferenceTurn` before useful demand is calculated. The resolver applies that depreciation once before gathering and production, so planned Pots do not increase storage until they exist. Farm Tools use the configured `.40 -> .45` supported-intensity anchors as a labor-side efficiency (`1.265625x`) for maintenance and expansion; their coverage budget is turn-local, maintenance-first, duration-aware, and seeded only from tools present when field work starts. New tools cannot backdate benefit, idle stock does not wear, and persisted cultivation capital remains the source of ecological change. Planner and resolver share useful field-work and production contracts. It uses current recipe-local productivity and a shared realized-output tool budget, prefers unlocked matching specialists, checks known accessible source capacity, and budgets input gathering and plant-food demand. Integer job assignments use a bounded fulfillment search to fit one resident workforce. This is a deterministic local heuristic, not a global optimum or a promise that all demand is feasible.

The plant-input projection first accounts for meals for residents and connected hunters. A known subsistence shortfall cannot simultaneously justify textile jobs using those same forecast food units. Realized ecological yield may still differ from the projection, so actual production retains independent input checks.

Existing fields receive operating farmers first. Those farmers may harvest the settlement's shared accessible terrestrial food, subject to the same ecological cap and demand as gathering; Lake and Shallows food remains under aquatic gathering/fishing rules, while Ocean remains excluded from opening passive harvest. Supplemental food gatherers fill the remaining gap. Material-gathering worker-time is explicitly reserved and cannot also harvest food. Expansion farmers are separately reserved; at q=0 they cannot harvest. Extra operating farmers do not multiply the field-operation factor above one. Old manual plans with no explicit labor plan retain their maintenance/surplus-expansion split.

Expansion is considered against the next demand forecast, not an assumed century of exponential population growth. The planner checks net marginal ecological benefit, including canonical clearing pressure and the existing sustainable-production thresholds. This does not impose a new physical q cap, change the cultivation equation, or prevent manual investment. The resolver still uses its repeated marginal tile allocator and real labor cost.

Equipment may be bootstrapped for the next source-feasible useful manufacturing plan. Hand Tools provide one finished output each; Regular/Improved Tools provide four finished outputs each. The resolver and planner snapshot that physical capacity, cap output before committing inputs, and debit tools only for realized useful output. Idle, blocked, no-demand, and no-material work does not wear inventory, and fractional output consumes fractional tool inventory. Newly produced tools cannot retroactively cover the interval that made them. Replacement demand is limited by useful output, source access, labor, priorities, and existing stock; it cannot create tools solely for research. Copper remains the legacy worker-coverage hook until its own durability contract is designed. Input food is protected from simultaneous growth spending. Imported/stored food enters the existing consumption/reserve/growth budget; there is no local-self-sufficiency assertion.

## Extractors and calibration

| Material | Specialist | Unlock |
| --- | --- | --- |
| Stone | Stone Quarrier | Toolmaking |
| Wood | Woodcutter | Carpentry |
| Clay | Clay Miner | Pottery |

The internal `Stonecutter` identifier is retained for save compatibility; `Stone Quarrier` is the display label. `ClayMiner` is new. Specialist extraction remains `SpecialistMaterialExtractionRate=1` unit/worker/BaseDays; regular gatherers use `GeneralistTaskEfficiency=.25`. These match one-unit, single-material manufacturing throughput. Staffing is demand-based, not a mandatory one-for-one headcount: Housing's 2.5/20-year Builder rate needs much less extraction, and improved tools retain their separate Stone and Wood inputs. Reserve replenishment can temporarily require more extraction than current production alone.

All existing recipe/QoL/tool multipliers remain unchanged, including Fabric's one Plant Food input, Housing's one total Wood/Clay input, and specialist/generalist matching. Housing fixes its Wood:Clay debit from the residual reachable inventory-plus-source ratio after feasible direct Wood/Clay recipes are retained. That fixed turn plan is shared by material gathering and production; direct work releases its reservation only when realized, and either material can fill an exhausted share without exceeding one total unit per Housing. Numerical search iterations and rounding tolerances are algorithmic controls, not new balance rates.

## Persistence and limits

`Camp.EconomyAutomationTurns` and `EconomicLabor`, and their `EconomicIntent` counterparts, are optional additive metadata in the existing `ANTHROPOCENE-11` envelope. Old saves default to Off with no labor reservation; explicit zeros are preserved. No tile/resource binary layout or world generation changes. Active automation, copied orders and countdowns round-trip deterministically. Stale field focus is cleared after relocation.

Source ledgers remain authoritative. Neighboring settlements can forecast demand against the same shared source; contention is settled by actual extraction receipts and can leave the later settlement short. There is no shared-source reservation market or global economic optimizer. Local projections do not guarantee actual yield under competition, ecological change or food scarcity; automation replans on the next turn.

Renewable Wood is a sparse companion stock to the authoritative ecology timeline. Its generated raster remains provenance and its existing recovery rate advances toward the current ecological support: persisted `LandCondition` times `(currentTier + 1) / (naturalTier + 1)`. A High tile degraded to Medium or Low therefore has less Wood capacity and renewal immediately; natural Low brush retains its own generated capacity. Pure source reads project both systems without mutation. Materialization and active-ecology result barriers commit the same projected Wood state, while conversion pressure records any separate capacity loss without altering the extraction receipt. An old source whose timestamp predates its already-stored ecological state is conservatively capped there rather than reconstructing unseen history.

Pots now target only the existing desired-reserve shortfall beyond natural storage; they do not create a second reserve appetite. Their finite capacity is additive and their 10% reference-turn decay creates replacement demand automatically. Stone Farm Tools affect only realized field-work labor; they do not multiply Farmer harvest, food, PotentialFood, or ecological regeneration directly. Toolmaking cannot be forced through stockpiling after useful replacement demand is satisfied. No invented Copper durability, free technology, Copper progression, irrigation effect, generic tool bonus, automatic fission, full trade, or expedition formula was added.

Validation evidence and measured sweep results are recorded in [NeolithicAutomationResults.md](NeolithicAutomationResults.md) and [Validation.md](Validation.md).
