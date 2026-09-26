# Demand-based Neolithic automation

## Reconciliation and scope

Implemented on `features/neolithic-agricultural` after the September 20 design reconciliation and the user's approval of demand-based extractor staffing, Clay Miner/Pottery, and persistent automation. The latest user instruction supersedes the technology document's earlier statement that Pottery has no paired extractor. No recipe, population-growth, ecological, mastery-threshold or QoL calibration was changed.

Discrepancies found before editing:

- Automatic allocation reserved enough gatherers to feed everyone, then assigned generalist crafts, leaving farmers the remainder. Existing assignments prevented later specialist adoption.
- Material gathering depended on food-gathering leftovers; a fully occupied food workforce could block tool bootstrapping.
- Farmer upkeep was incorrectly treated as a maximum harvesting workforce. Every additional farmer expanded fields, and farmer claims were restricted to owned fields rather than the shared accessible footprint.
- Clay had no specialist. The inspector's allocation button was a one-time assignment, not a saved policy.
- Fabric feedstock could be counted again as growth surplus. This was exposed during integrated automation review and is now reserved before growth allocation.

### Locked prior-cohort budgeting

`TurnPlanner.Prepare` runs after the previous turn has ended and before resolution mutates the world. It validates and copies orders, creates one automated intent per eligible settlement unless a player camp order takes precedence, and freezes each camp's resident cohort and food, goods and material targets. Dispatch removes departing people from camp-resident goods and QoL needs; connected hunters continue to count in biological food demand. `EconomicPlanner.Plan` is a pure interturn policy. Ordinary observation and order validation do not enter it, and the resolver never calls it.

The resolver keeps pre-turn stock separate from current-turn income. Connected hunting can add `StepFoodIncome` after preparation; realized receipts reduce the fixed food target without becoming a new customer. Plant feedstock and production targets were sized for the prior cohort. Only plant inventory above that cohort's consumption and operating reserve can reduce the separate feedstock-harvest target. The actual ecology and inventory can still limit receipts. Food pays current-cohort consumption, the configured operating reserve and then growth. Birth progress accrues during demographics, but births are committed only after production, needs and storage. A newborn's food, Fabric, Housing and QoL needs first enter the next prepared turn. Fabric input remains protected from growth spending and cannot fund births.

With Husbandry, animal-sourced manufacturing inputs stay separate from PlantFood in
the same useful-food budget. Animal Product spent on Animal Fabric cannot also become
a meal, reserve refill, or demographic growth food.

Managed Husbandry uses this same projection. Starting animals resolve current forage and
water before starvation, output and survivor reproduction; newborns begin in the next
generation. AnimalFood is accepted only into the common consumption/reserve/growth
budget. The projection clamps Fabric to actual plant provenance inside the growth-rate
search, disables growth during starvation recovery, and accounts for the one scheduled
depreciation of existing Pots before sizing the shared reserve. This preserves food,
biomass and freshwater ledgers without creating a second demographic or animal-only
storage budget.

Pastoral Fabric and shelter use the same Economy controls and Weaving/Construction
tracks as their agricultural counterparts. Cloth and Animal Fabric share one clothing
target, with Cloth consumed first; Animal Fabric retains its separate stock because it
also feeds portable Light Housing. Light Housing moves with the camp, while fixed
Housing remains owned by its construction site and is counted only while the camp is
there. Herding Tools only increase covered Herder management capacity: current-generation
work uses turn-start stock, tool wear follows covered Herder-years, and newly made tools
cannot improve work already resolved. The pure animal-food forecast reuses the
authoritative ecological source integrator for the camp's visible owned-herd claims.

### First-tier pastoral recipes and state

The Issue #19 first tier uses the existing specialist tracks and production rates.
Animal Fabric costs one Animal Product per unit before manufacturing-tool wear; a
Weaver uses the existing Fabric output rate and a Craftsperson works at 0.25x rate.
Animal Fabric has a 1:1 clothing need equivalence with Cloth. Light Housing costs one
Animal Fabric per unit, uses the existing Builder rate (`0.03125` per base-day),
provides shelter for four people, and depreciates by 10% per 20-year reference turn.
It is carried with the camp; agricultural Housing remains fixed site stock.

Herding Tools cost one Wood and one Stone per unit, use the existing Carpenter Farm
Tools output rate, and give Craftspersons 0.25x Carpenter output. Primitive
management remains two animals per Herder; the Stone tool tier raises covered work to
2.69 animals per Herder. Coverage is partial when only some Herder work has tools.
One tool covers one Herder-equivalent for a 20-year reference generation, with wear
only for realized covered work. The tools affect management capacity only; they do not
change animal food yield, reproduction, pasture productivity, or animal counts. Later
equipment tiers remain design anchors, not implemented effects. These first-pass
calibrations are locked; this feature does not add a separate Fiber resource,
Dairying technology, or specialist tree.

Owned observations expose only the camp's pastoral stocks and fixed Housing at its
current site. Schema 11 adds those fields; foreign stores and herds remain private.
Fixed Housing is kept once in the owning camp/site record, preserves its latent
depreciation timestamp in saves, and migrates positive legacy carried Housing to the
camp's saved tile. Read-only projections do not materialize its elapsed decay.

This is a deterministic coupled feasibility search, not a promise that a pre-allocation forecast equals realized yield. Shared tiles, seasonal ecological limits, prior claims, and changing food stocks can reduce actual receipts; the resolver reconciles those receipts and the next turn replans from authoritative state. The one-unit Fabric Plant Food input and all growth, reserve, and production constants are unchanged.

## Operation

The settlement inspector offers **Allocate once**, **Off**, **Next N turns**, and **Until disabled**. A positive duration includes the resolution on which the toggle is submitted. Automated staffing is prepared once from the prior boundary before movement and dispatch; material extraction then queries the moved settlement's actual radius-one source adjacency. One countdown tick occurs per resolved turn. No new order is needed on subsequent turns. Manual workforce edits disable automation and clear its labor reservations. Turning automation off otherwise retains the last staffing/working-time plan. Priorities and stock targets remain editable while automation is on.

The pure `EconomicPlanner` sizes useful manufacturing from stock-adjusted Clothing, once-depreciated Housing for the prior cohort, the existing desired food reserve's natural-storage gap for Pots, and forecast field work for Stone Farm Tools. Pots add `PotStorageFoodPerUnit` absolute food capacity; their projected stock is retained with duration-aware `PotDecayPerReferenceTurn` before useful demand is calculated. The resolver applies that depreciation once before gathering and production, so planned Pots do not increase storage until they exist. Farm Tools use the configured `.40 -> .45` supported-intensity anchors as a labor-side efficiency (`1.265625x`) for maintenance and expansion; their coverage budget is turn-local, maintenance-first, duration-aware, and seeded only from tools present when field work starts. New tools cannot backdate benefit, idle stock does not wear, and persisted cultivation capital remains the source of ecological change. Planner and resolver share useful field-work and production contracts. It uses current recipe-local productivity and a shared realized-output tool budget, prefers unlocked matching specialists, checks known accessible source capacity, and budgets input gathering and plant-food demand. Integer job assignments use a bounded fulfillment search to fit one resident workforce. This is a deterministic local heuristic, not a global optimum or a promise that all demand is feasible.

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

`MaterialAccessIndex` records deposit sites by tile, current source states by ID and discovered source keys for O(1) membership. A settlement queries only its radius-one tiles, in stable tile/material/source order. The index is rebuilt after load and never persisted. Extraction reads the moved settlement's current adjacency and debits only its chosen source. Consecutive Stone and Clay worker intervals are batched while the same source retains the highest priority; Wood keeps per-interval ecological conversion receipts. The detailed source-lookup counters are opt-in so measurement does not serialize parallel readers in normal play.

Renewable Wood is a sparse companion stock to the authoritative ecology timeline. Its generated raster remains provenance and its existing recovery rate advances toward the current ecological support: persisted `LandCondition` times `(currentTier + 1) / (naturalTier + 1)`. A High tile degraded to Medium or Low therefore has less Wood capacity and renewal immediately; natural Low brush retains its own generated capacity. Pure source reads project both systems without mutation. Materialization and active-ecology result barriers commit the same projected Wood state, while conversion pressure records any separate capacity loss without altering the extraction receipt. An old source whose timestamp predates its already-stored ecological state is conservatively capped there rather than reconstructing unseen history.

Pots now target only the existing desired-reserve shortfall beyond natural storage; they do not create a second reserve appetite. Their finite capacity is additive and their 10% reference-turn decay creates replacement demand automatically. Stone Farm Tools affect only realized field-work labor; they do not multiply Farmer harvest, food, PotentialFood, or ecological regeneration directly. Toolmaking cannot be forced through stockpiling after useful replacement demand is satisfied. No invented Copper durability, free technology, Copper progression, irrigation effect, generic tool bonus, automatic fission, full trade, or expedition formula was added.

Validation evidence and measured sweep results are recorded in [NeolithicAutomationResults.md](NeolithicAutomationResults.md) and [Validation.md](Validation.md).
