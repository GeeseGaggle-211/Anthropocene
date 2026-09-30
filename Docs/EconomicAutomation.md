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

`TurnPlanner.Prepare` runs after the previous turn has ended and before resolution mutates the world. It validates and copies orders, resolves camp placement conflicts without changing world state, and creates one automated intent per eligible settlement at its effective destination. An explicit Economy intent takes precedence; a Move does not suppress enabled automation. AI actions also request one transient economic plan when the AI is managing a Neolithic camp, preserving its former per-turn staffing behavior; this request is not saved or exposed as a player order. A player Move with automation Off retains current assignments. Preparation freezes each camp's prior resident cohort and food, goods and material targets. Dispatch removes departing people from camp-resident goods and QoL needs; connected hunters continue to count in biological food demand. `EconomicPlanner.Plan` is a pure interturn policy. Ordinary observation and order validation do not enter it, and the resolver never calls it.

The resolver keeps pre-turn stock separate from current-turn income. Connected hunting can add `StepFoodIncome` after preparation; realized receipts reduce the fixed food target without becoming a new customer. Plant feedstock and production targets were sized for the prior cohort. Clothing-need feedstock (`NeolithicProduction.ClothingNeedFeedstock`: the food of the fabric covering this turn's clothing requirement) is paid after consumption and ahead of the operating-reserve refill, so inventory above this cohort's meals can reduce its harvest target; every other feedstock can only use inventory above consumption, clothing and the operating reserve, as before. The actual ecology and inventory can still limit receipts. Food pays current-cohort consumption, the clothing-need feedstock, the configured operating reserve and then growth together with the remaining manufacturing input. The planner's feedstock envelopes and harvest gaps use the same split (`NeolithicProduction.FeedstockGaps`), so a camp at its food limit still staffs the Weavers its clothing needs. Birth progress accrues during demographics, but births are committed only after production, needs and storage. A newborn's food, Fabric, Housing and QoL needs first enter the next prepared turn. Fabric input remains protected from growth spending and cannot fund births.

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
provides shelter for `LightHousingPeoplePerUnit` people (two at the shipped calibration; fixed Housing shelters four), and depreciates by 10% per 20-year reference turn.
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
Schema 16 adds the owned camp's portable Housing Material stock the same way.
Fixed Housing is kept once in the owning camp/site record, preserves its latent
depreciation timestamp in saves, and migrates positive legacy carried Housing to the
camp's saved tile. Read-only projections do not materialize its elapsed decay.

This is a deterministic coupled feasibility search, not a promise that a pre-allocation forecast equals realized yield. Shared tiles, seasonal ecological limits, prior claims, and changing food stocks can reduce actual receipts; the resolver reconciles those receipts and the next turn replans from authoritative state. The one-unit Fabric Plant Food input and all growth, reserve, and production constants are unchanged.

## Operation

The settlement inspector offers **Allocate once**, **Off**, **Next N turns**, and **Until disabled**. A positive duration includes the resolution on which the toggle is submitted. Automated staffing is prepared once from the prior boundary using the accepted move destination; material extraction then queries that destination's radius-one source adjacency. A blocked move plans at the original site. One countdown tick occurs per resolved turn. No new order is needed on subsequent turns. Manual workforce edits disable automation and clear its labor reservations. Turning automation off otherwise retains the last staffing/working-time plan. Priorities and stock targets remain editable while automation is on.

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

All existing recipe/QoL/tool multipliers remain unchanged, including Fabric's one Plant Food input, Housing's one total Wood/Clay input, and specialist/generalist matching. Housing is two Builder steps (WP5a): Housing Material, then the Housing assembly (below). Housing Material fixes its Wood:Clay debit (WP5b, below) from the reachable inventory-plus-source residual after the work ranked above it. That fixed turn plan is shared by material gathering and production; reservations are released only when realized, and either material can fill an exhausted share without exceeding one total unit per Housing. Numerical search iterations and rounding tolerances are algorithmic controls, not new balance rates.

### Housing Material (WP5a)

Housing is split into two recipes that together cost exactly what the single recipe did.
**Housing Material** (`CraftGood.HousingMaterial`, appended to the enum so saved ordinals are
unchanged) is a Builder/Construction recipe at twice the old Housing rate with the flexible
Wood/Clay input (`HousingMaterialUnitsPerHousing`), no tool wear and `AgricultureOnly`. **Housing** is
the assembly: also twice the old rate, one Housing Material input, tool wear of one equipment
output per unit, and the only step that adds fixed Housing at the site. Builder labour, Wood plus Clay and
tool wear per Housing unit, and Construction progress, are unchanged. The Builder tool multiplier applies to both steps; the
material step's time is the time the assembly would take on the same tool budget, and one Builder pool
makes the material and assembles it (`NeolithicProduction.HousingMaterialLimit`), so material never takes labour the
assembly needs. Craftspersons run both steps at `GeneralistTaskEfficiency`.

Housing Material is `CampInventory.HousingMaterial`, portable camp stock with no decay. Its useful demand is
the Housing gap minus its stock, so it is never stockpiled beyond that gap; Housing demand is unchanged and priority is
Housing's (no new saved priority). Schedules place the material step immediately before the
assembly at Housing's priority (`NeolithicProduction.ScheduleKey`), so material made in a turn is assembled in the same turn,
and carried stock is assembled together with new material. The planner sizes the pair as one claim (Housing Material is not a separate
fulfilment claim), the forecast, material gathering and the resolver use the same rule, and the Housing claim's value
labour per unit still includes both steps. Copper (a legacy worker-time hook)
covers the assembly step only, so it covers twice the housing units per copper worker-time.

### Housing Material access order (WP5b)

Housing Material is no longer the final flexible Wood/Clay consumer. It takes its ordinary place in the
material-access order in every schedule: crafting-priority order (it uses Housing's priority; ties by good, so it
precedes Regular Tools and follows Farm Tools at equal priority), and under the Value rule the Housing claim's marginal
value in the planner's `AccessOrder` (the assembly still follows its material). `MaterialEconomy.BuildHousingPlan` fixes
the turn plan once: direct work ranked above Housing Material has already taken its inputs (a hard reservation until
realized, which Housing Material cannot take); the remaining planned direct Wood/Clay of work ranked below it is a soft
reservation. Housing Material first draws Wood and Clay no later recipe needs, in proportion to that unreserved residual,
and only if still short dips into the reserved amounts, split in proportion to what remains. Its unfinished claim is in
turn a hard limit on lower-ranked direct recipes (`HousingMaterialPlan.LimitDirect`), so a Toolmaker or Potter scheduled
earlier by role cannot take Wood or Clay a higher-priority Housing still needs. The plan (fixed WoodShare/ClayShare = the
whole expected draw) is shared by `PlanHousingMaterials`, `Gather`, `Forecast`-driven needs, `ToolReplacementDemands`
(Regular Tools see Housing's Wood draw only if Housing outranks them), the planner's capped loop and final role pass, and
`Produce`. Known limitation: when Farm Tools outrank Housing under the crafting-priority order (Uniform), the planner allocates Housing Material after
all direct work because Farm Tool intent is bounded last; the resolver still uses rank. No rates, recipes or demands change.

### Value-rule production order (WP3b)

Under `PlannerFulfilment = "Value"` (the default for new worlds; saves written before the field existed load as `"Uniform"` and continue exactly, and an explicit Uniform is bit-identical to the pre-value engine), every automated camp gets one
transient **production order** (`ProductionOrder`, built in `ProductionDemand.cs`) that replaces the crafting-priority and
role order wherever recipes share a scarce input: tool coverage, Stone/Clay/Wood, plant/animal feedstock and Animal
Fabric. It is computed once per camp at preparation from the committed start-of-turn stocks and never recomputed
mid-resolution. Keys, highest first (ties by `ScheduleKey`): need goods (Cloth, Animal Fabric, Housing, Light Housing)
`V * exp(-lambda * Stock / Need)` with the same V, Stock and Need as the Value claims; Housing Material takes its Housing's
key; Hand and Regular Tools take `outputs per tool (1 and 4) * best equipment need key * exp(-r * years)`, because tools made
now serve the next interval and never the one that made them; Pots, Farm Tools and Herding Tools (no value yet) follow in
crafting-priority order. Animal Fabric is always ahead of Light Housing and Housing Material ahead of Housing. Coverage is
then allocated by recipe value across roles: each recipe in order visits its specialist role and then the Craftsperson (each
role keeps its labour pool), inside the existing layers (tool producers before the equipment layer). The planner's capped
loops and final role pass, `ToolReplacementDemands` (probe and main), `MaterialEconomy.Forecast`/`PlanHousingMaterials`/`Gather`
(including the Housing Material hard/soft reservation ranks) and `ResolveCampNeolithic`/`Produce` all read that order.
It rides with the automatic intent (`AutomaticPlan`), is stored in `PreparedTurn.ProductionOrders` and each camp's
`TurnNeeds.Order`, and is part of `PlanningDraft`'s intents so draft reuse stays exact. It is never saved, observed or
fingerprinted. Manual camps and Uniform keep the crafting-priority order. Checks: `ProductionOrderChecks` (`--only=ProductionOrderChecks`).

Save: the field is an optional member of the `ANTHROPOCENE-11` inventory payload (absent loads as zero; no version bump).
Observation schema 16 exposes the owned stock in `ObservedSettlementDto.HousingMaterial`; foreign stores stay private.
Production receipts list both goods (`HousingMaterial` receipts carry Wood/Clay; Housing receipts carry the consumed material in
`ProductionReceipt.HousingMaterial`). Checks: `HousingMaterialRecipeChecks` (`--only=HousingMaterialRecipeChecks`).

### Expansion labour on the allocator's step ladder (WP4b)

Under the Value rule, when the planner's expansion gate holds (forecast demand above supply and a retained field gain),
the demand pass builds one transient **expansion ladder** (`ExpansionValuation`, `ProductionDemand.cs`), read only by
`ValueFulfilment`. The ladder (`CultivationEconomy.ExpansionLadder`) is the resolver allocator's own greedy farmer steps from the
start-of-turn state with unbounded effort, recorded by the same loop (`NextStep`, `AllocateHorizonEffort`) and stopped once the
cumulative annual food reaches the total gap: per step the build effort in worker-years, its score and its annual retained
food. Farm Tool efficiency and the bridge pass are ignored (effort is worker-years). Its food is valued in three buckets
per year, in the order supply fills them: the consumption gap at `v_food = N Q / (F D_year)`, the operating-reserve refill
at `FoodRiskWeight · v_food` (ρ = 0.1: reserves protect against shocks the model does not yet produce), then growth (maximum growth food plus
Cloth feedstock) at `v_growth = max(0, dW/dN) L_person / food_per_birth` (`dW/dN = W(N0+1) - W(N0)`, `W = N Q(N)` with goods
dilution, friction and cohesion at the committed coverages; `food_per_birth` is the derivative of `GrowthFood` with respect to
final population; `L_person = 1 / (1 / PersonLifeYears + r)`; the design's labour term is omitted as circular with the price).
Step value per build worker-year is `u_i = score_i L_cap v_i` (`v_i` the piecewise bucket average over the step's food interval),
truncated at the total gap and made nonincreasing. At labour price `w(s)` expansion buys the worker-years of the prefix with
`u_i >= w`, staffing `ceil(worker-years / years)` field workers (at most those left), so affordable steps trade against crafts
while the ladder never lowers the path's end; residents left after the search join expansion as before (`staffed + spare`).
The fulfilment search runs in two stages when full fulfilment staffs expansion: first on the requirement without staffed
expansion, exactly as before the claim; if expansion is unstaffed at that position the plan stands, otherwise the search
continues on the full requirement between the position where the price reaches the ladder's first value
(`ValueFulfilment.PositionAtPrice`) and the craft position. A single interpolating search on the total requirement, which
the ladder can inflate to many times the residents, stalled near `s = 0` and cut crafts that expansion never staffed.
The planner reports `farmLabourPrice`: `u` of the first ladder step not covered by the plan's `ExpansionFarmers * years`
(0 when exhausted or absent). With slack labour, or when the ladder's values are below the last craft unit's price, the plan is
exactly the pre-WP4b Value plan. Uniform is untouched; nothing is saved. Checks: `ExpansionClaimChecks`
(`--only=ExpansionClaimChecks`).

#### Herds against fields (Husbandry settling)

The expansion ladder above priced fields against a gap that assumed the herd stays as it is. Under the Value rule the demand pass
now also forecasts the herd (`TurnResolver.ForecastGrownManagedAnimalFood`, pure): each herd the ordinary forecast finds fully
nourished grows to `floor((animals + remainder) x exp(GrowthRate x breeding x years))` (the resolver's own birth rule), and the grown
herd is run through the same forage, water and management forecast with the herders it would need. `HerdGrowthFood` is the food it
adds net of the extra human forage it displaces; it is added to supply wherever the planner decides whether fields are needed
(`ExpansionValuation.Apply`'s gate and gaps, `Plan`'s `expand` and `intendedExpansion`), so fields are built only for the gap the
herds will not close. Herd growth is not labour-limited (breeding reads intensity and nutrition only; management fulfilment scales
animal food, and the planner reserves `RequiredHerders` before anything else), so there is no herd-expansion claim.
A non-village camp whose first technology is Animal Husbandry also pays a **settling cost**: the ladder records the village-footprint
average after each step (`ExpansionStep.VillageAverage`), and the step that reaches `CultivationEconomy.VillageIntensity` (0.25) is
charged `SettledHerdGrowthPenalty x L_cap x` (the bucket value of the forecast grown-herd food per year). If the value of that step
and every later one exceeds the cost, the tail is scaled by `1 - cost / tail`; otherwise the valued ladder stops before it and the
planner limits expansion farmers (staffed plus spare) to `floor(ExpansionEffortCap / years)`, the worker-years of the steps before it.
Primary-Cultivation camps, villages and Uniform plans are untouched. Measurements and limits: [HusbandrySettling.md](HusbandrySettling.md).
Checks: `SettlingChecks` (`--only=SettlingChecks`).

### Farm Tools, Herding Tools and Pots (WP4a redo)

Under the Value rule the demand pass prices these three goods after the expansion ladder (`ClaimValuation.ApplyTools`,
`ProductionDemand.cs`), with no saved fields. **Saved labour is priced at the marginal value of the labour it frees:**
`w_farm0 = Expansion.ValueAfter(lastExpansionFarmers · years)`, the ladder's value after the expansion farmers that
the camp's saved `EconomicLabor.ExpansionFarmers` (last turn's plan) staffed; 0 when there is no ladder (no forecast
shortfall, exhausted ladder), in which case the tools stay unpriced (uniform share, tail of the order) exactly as before.
- Farm Tool: `savedFT = FarmToolFarmerYearsPerUnit · (1 − 1/e)`, `e = FarmToolFieldWorkEfficiency` (about 1.2656, so
  about 4.19 farmer-years), `V_FT = savedFT · w_farm0 · P` (P the crafting-priority multiplier used for need goods).
- Herding Tool: `savedHT = HerdingToolCoverageYearsPerUnit · (S/A − 1)`, `S = StoneHerdingToolAnimalsPerHerder` (2.69),
  `A = AnimalsPerHerder` (2): `HerdingTooling.RequiredHerders` gives a covered herder-year `S` animals that would need `S/A`
  uncovered herders, so one covered herder-year frees `S/A − 1` herder-years (6.9 per tool), and `V_HT = savedHT · w_farm0 · P`
  because freed herders join the same field margin. (The first WP4a attempt's `1 − A/S` understated this relation.)
- Tool share: a priced tool claim is pursued in full when `V/ℓ >= w(s)` (`ℓ` is craft time plus input extraction, as for other
  recipes), else not; share 0 at `s <= 0`, 1 at `s >= 1`. It is nondecreasing because `w` falls in `s`. Tool claims never enter the
  path bounds, so the ends and the two-stage expansion search are unaffected; quantities are sized as before.
- Pots: a need claim on food security with the risk placeholder `ρ = FoodRiskWeight`. One Pot lets the camp retain
  `PotStorageFoodPerUnit` of operating reserve, credited once at `ρ · v_food` per food unit like reserve refill in the expansion
  valuation: `V_Pot = ρ · PotStorageFoodPerUnit · v_food · P`, `v_food = N Q / (F D_year)`. (A per-turn credit over the discounted
  Pot life, `L_pot / ReferenceTurnYears` ≈ 3.3 times more, raised Pot output 4–8× and cut mean QoL 0.8–1.2% at the gate.) Need is `TargetPots` (reserve beyond natural
  storage over `PotStorageFoodPerUnit`), Stock the current pots after this turn's decay, Quantity the existing `PotDemand` gap, and the
  claim follows the exponential curve `V·e^{−λc}` and sets the path bounds like other need claims. With `ρ = 0` or Need <= 0 it is not valued.
- Order keys: tools by `V` per unit (when priced), Pots by `V_Pot·e^{−λ·Stock/Need}`; unpriced goods keep the tail in priority order;
  precedence rules and ties are unchanged. Pots are excluded from the "best equipment need good" that keys Hand and Regular Tools.
At the 80-tribe seed-1729 checkpoint the tool values are 0.03-0.4 per unit (w_farm0 0.004-0.10) against Cloth 0.5-10 and Housing 5-130,
so tools rank below need goods; Pots key 0.7-6, comparable to Cloth. Checks: `WP4aValueChecks` (`--only=WP4aValueChecks`;
`WP4A_CHECKPOINT=<save> WP4A_TURNS=<n>` prints per-camp magnitudes).

## Persistence and limits

`Camp.EconomyAutomationTurns` and `EconomicLabor`, and their `EconomicIntent` counterparts, are optional additive metadata in the existing `ANTHROPOCENE-11` envelope. Old saves default to Off with no labor reservation; explicit zeros are preserved. No tile/resource binary layout or world generation changes. Active automation, copied orders and countdowns round-trip deterministically. Stale field focus is cleared after relocation.

Source ledgers remain authoritative. Neighboring settlements can forecast demand against the same shared source; contention is settled by actual extraction receipts and can leave the later settlement short. There is no shared-source reservation market or global economic optimizer. Local projections do not guarantee actual yield under competition, ecological change or food scarcity; automation replans on the next turn.

`MaterialAccessIndex` records deposit sites by tile, current source states by ID and discovered source keys for O(1) membership. A settlement queries only its radius-one tiles, in stable tile/material/source order. The index is rebuilt after load and never persisted. Extraction reads the moved settlement's current adjacency and debits only its chosen source. Consecutive Stone and Clay worker intervals are batched while the same source retains the highest priority; Wood keeps per-interval ecological conversion receipts. The detailed source-lookup counters are opt-in so measurement does not serialize parallel readers in normal play.

Renewable Wood is a sparse companion stock to the authoritative ecology timeline. Its generated raster remains provenance and its existing recovery rate advances toward the current ecological support: persisted `LandCondition` times `(currentTier + 1) / (naturalTier + 1)`. A High tile degraded to Medium or Low therefore has less Wood capacity and renewal immediately; natural Low brush retains its own generated capacity. Pure source reads project both systems without mutation. Materialization and active-ecology result barriers commit the same projected Wood state. Timber extraction debits the finite Wood source and records its receipt without reducing `LandCondition` or biological Wood capacity. An old source whose timestamp predates its already-stored ecological state is conservatively capped there rather than reconstructing unseen history.

Pots now target only the existing desired-reserve shortfall beyond natural storage; they do not create a second reserve appetite. Their finite capacity is additive and their 10% reference-turn decay creates replacement demand automatically. Stone Farm Tools affect only realized field-work labor; they do not multiply Farmer harvest, food, PotentialFood, or ecological regeneration directly. Toolmaking cannot be forced through stockpiling after useful replacement demand is satisfied. No invented Copper durability, free technology, Copper progression, irrigation effect, generic tool bonus, automatic fission, full trade, or expedition formula was added.

Validation evidence and measured sweep results are recorded in [NeolithicAutomationResults.md](NeolithicAutomationResults.md) and [Validation.md](Validation.md).
