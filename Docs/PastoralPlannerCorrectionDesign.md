# Shared crop and herd allocation correction

Design proposal recorded 2 October 2026 following the user's response to
[PastoralPlannerAudit.md](PastoralPlannerAudit.md). This document specifies
the next correction; it does not claim a production implementation or new
gameplay/performance results. Preserve the measured crop-moisture and
biomass-first candidate and its fertile-land benchmarks.

The subsequent opt-in implementation and matched measurements are recorded in
[SharedAllocationValidation.md](SharedAllocationValidation.md). Runtime
acceptance and animal-unit/rate calibration remain open; the feature stays off
by default.

## User direction

1. Spare residents must be able to invest in herd retention/breeding as well
   as crop expansion.
2. Correct the protected-inventory bug while recognizing livestock's
   continuing food-security contribution.
3. Use one economic planner. Pastoralism should emerge from local payoffs,
   without a separate nomadic planner or a primary-technology preference.
4. Charge the human capital committed to future field upkeep and further
   intensification, and compare it with the corresponding herd-management
   work. Do not interpret spare current residents as free future workers.

## What is already charged

`CultivationEconomy.EffortForCapital` (line 90) leaves q <= .4 on the
quadratic capital curve and raises marginal effort by R=16 per additional
.1 q. `MaintenanceWorkerRequirement` (line 286) applies that same effort
weight to recurring field maintenance. `HorizonMarginalInvestment`
(line 634) already includes `PlanningHorizonYears` maintenance in the
expansion score. Maintenance is therefore not wholly absent from valuation.

For new-world defaults, an open field with no Farm Tools has the following
full-operation requirement, before population caps or integer staffing:

| Field q | Maintenance worker equivalents per tile | Seven equally intensified tiles |
| --- | ---: | ---: |
| .40 | 14.29 | 100.00 |
| .45 | 22.01 | 154.10 |
| .50 | 52.93 | 370.51 |
| .55 | 176.59 | 1,236.12 |
| .60 | 671.23 | 4,698.59 |

Derived from `ReferencePopulation=250`, `MaintenanceCoefficient=.4`,
`ReferenceFootprintTiles=7`, `ReferenceIntensity=.4`, and R=16. This is
required staffing, not a measured allocation or a claim that every camp
operates seven identical fields. Starting Farm Tools can reduce required
workers by at most their actual covered-time efficiency; they require
replacement and materials. Existing planner code caps the staffing helper
at the resident population, which must not be treated as proof that full
operation is feasible.

Primitive herding supports two animal units per herder. That is linear
work with a fixed rate, not a fixed headcount for an arbitrarily large
herd. Stone tool-covered work supports 2.69 units per herder, bounded by
real tool coverage and replacement. Keep these rates unchanged initially.

The identified asymmetry is that `EconomicPlanner.Plan` adds spare workers
to fields outside the priced ladder (line 617), bypassing the valuation
that includes upkeep. Herd retention then assumes existing fields remain
operated, so it cannot value avoiding that commitment. Repairing both is
more precise than applying another arbitrary agriculture penalty.

## Shared decision and food ledger

Keep field capital, inventories, equipment, and animals as authoritative
state. Candidate operation and investment choices are transient planning
data; the existing resolver remains the authority for harvest, livestock
growth, slaughter, tool wear, field operation and capital decay.

For each candidate bundle, count current useful food/goods, finite future
receipts and usable residual capital, then subtract its current and future
work at comparable labour opportunity costs. Account for construction,
field operation/harvest, herd management, material extraction, crafting and
replacement. Respect the current resolver's maintenance/harvesting overlap:
it reserves the larger work requirement, not their unconditional sum. Do
not charge the same farmer time twice.

Current spare work may have zero opportunity cost when it has no competing
use. Future work still carries its opportunity cost. Neither automatic crop
expansion nor herder selection may bypass this comparison. Newly proposed
intensification is a candidate cost/return, not an automatic future
obligation: maintenance of acquired field capital is recurring, whereas
further expansion must earn its place again.

Use one inventory/food ledger, based on `TurnResolver.FoodBucketTargets`:
opening stock is credited once, protected reserves stay protected, typed
craft input is allocated once, and incoming food is divided consistently
among meals, necessary feedstock, reserve refill and supported growth.
Reprice field and herd alternatives after selecting incoming animal
receipts. A fixed field-first food gap cannot be the objective for every
alternative.

Livestock contributes through usable current food, future replenishment
and its slaughter option. Pots contribute storage capacity; they do not
produce meals. Use the existing `FoodRiskWeight` for comparable otherwise
uncredited reserve/security service, rather than adding an unconditional
animal bonus. Food must still respect storage, herd forage/water, labour
and typed-input limits. If a candidate liquidates an animal, its carcass
food and the forgone continuation belong to that same alternative; that
animal must not also produce future milk or offspring. The first ledger
repair can correct current reserve-refill valuation without inventing a
disaster probability or treating a live herd as extra pot capacity.

## Operation alternatives inside the existing planner

Evaluate feasible maintain/reduce/expand field operation jointly with
retain/grow/slaughter livestock. This is one allocation problem available
to every camp holding the relevant capabilities, regardless of primary
technology or village label. Do not disable Cultivation to simulate the
pastoral alternative; the field-removal audit was a diagnostic, not the
production design.

Reduced operation must forecast actual capital decay, managed renewal,
harvesting productivity and remaining stocks using the authoritative local
kernel. When future herd food can replace crops, count the avoided field
work and the lost crop receipts together. Preserve gathering, aquatic food,
water and other craft alternatives in the same resident budget.

Retain the physical one-turn breeding ceiling: spare labour cannot create
animals that do not exist. Staff retention/breeding capacity up to feasible
growth, and only cap reproduction when the joint optimum calls for it.
Animal acquisition remains the separate #12 mechanic. Verify that the
continuation used to price retention is consistent with repeated actual
plans, rather than promising unrestricted future growth while repeatedly
capping it in resolution.

## Computation and validation contract

Use fixed local footprints, cached field forecasts and portfolio envelopes.
Costs should follow local tiles, owned herd groups, field bands and staffing
breakpoints, not simulation years or a full population-by-field-by-herd
Cartesian search. The existing 32 q bands and bounded local footprint can
bound field alternatives; use breakpoints rather than recursively running
the entire craft planner for every herder. No time-step simulation inside
the authoritative planner, global candidate scans, separate lifestyle
planners, pastoral quotas or new unconditional animal-yield multiplier.

Focused acceptance cases must cover:

- Identical reserve/meal/growth accounting for equivalent food receipts,
  including inventory below/above protected reserves and typed inputs.
- Spare work choosing herd growth when its net return exceeds expansion,
  and crops winning when their return exceeds herding; prohibit expansion
  with no useful net payoff under the Value rule.
- Increasing q producing the measured steep maintenance commitment;
  future field/tool costs cannot disappear when current labour is spare.
- Reduced operation with correct lost receipts and capital decay, no
  population-overallocated work and no milk credited after liquidation.
- A joint optimum without dependence on primary-technology labels, physical
  forage/water feasibility, and rolling breeding-policy consistency.
- Pure planning, scalar/parallel determinism, reversed ordering and save
  continuation, plus 100/1,000/10,000-resident mature-scale planning costs.

Use the measured corrected candidate as the control for a new replay.
Report both aggregate populations and unweighted resident P25/median/P75
camp sizes, with villages separated from all camps. Include QoL, animals,
field intensity/operation, food-source receipts and breeding caps. Keep
fission and QoL mortality active in matched 25/50-turn runs. Profile total
time, phases, allocations and work-normalized costs from identical opening
save bytes; fewer tribes alone must not be described as cheaper planning.
