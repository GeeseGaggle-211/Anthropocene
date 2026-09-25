# Turn-horizon refactor

Status: implementation in progress on `codex/turn-horizon-refactor`, based on
`c33a5b7` (a descendant of `features/neolithic-agricultural`). The archived
seasonal implementation at `c33a5b7` is the calibration reference, including
the accepted prior-cohort needs rule. Measurements and acceptance evidence
will be added after the implementation is exercised.

## Authoritative clock

One player turn evaluates `S_next = F(S_start, locked allocations, dt)`, where
`dt` is `TurnRecord.Duration`. The only authoritative times are the committed
start and end. Intermediate calculations may evaluate an integral or a climate
coefficient; they are neither saved state nor a decision boundary. There is no
annual or seasonal resolver loop, event queue, breakpoint scheduler, or hidden
subturn production order. Shorter later-era turns call the same functions with
a smaller `dt`.

`TurnHorizon` binds `StartDays`, `Days`, `EndDays`, `Years`,
`ReferenceIntervals = Days / BaseDays`, and
`PracticeScale = Days / PracticeDays` at preparation. The prepared turn locks
orders and prior-cohort targets to this horizon. `BaseDays` remains the unit
for rates calibrated to the old quarter-year interval and for the four
generated seasonal climate profiles. It no longer sets an execution count.

| Clock use | Classification | Rule |
| --- | --- | --- |
| `GenerationIntervals`, `Days / BaseDays` in food, labor, recipes and supplies | A: reference-unit conversion | Replace repeated conversions with the horizon's `ReferenceIntervals` at subsystem boundaries. |
| `Days / DaysPerYear`, `Days / PracticeDays` in growth, decay, progression and practice | A: duration conversion | Use `Years` and `PracticeScale`; preserve the configured rate equations. |
| `Season`, four `SeasonData` entries, `BaseDays` in generation and climate | B: seasonal forcing | Reduce the four profiles into fixed turn coefficients, retaining starting phase and fractional coverage. |
| `LazyEcology.Segments` in active ecology, quiet projection, and cultivation barriers | C: temporal stepping | Remove from authoritative turn resolution. Retain only in a separately identified calibration reference if necessary. |
| `TurnDays` | A: next-turn duration | Read once when constructing `TurnHorizon`; store that duration in the turn record. |

## Pass contract

1. **Prepare.** AI receives filtered committed observations. The interturn
   planner validates orders and computes fixed prior-cohort food, materials,
   goods, and resident targets. It creates no authoritative world writes.
2. **Orders and movement.** Apply locked orders; compute herd intentions from
   the pre-movement view; dispatch, provision, move, and return supplies once.
3. **Set up the turn.** Establish field ownership and fixed workforce,
   reproduce and hunt herds, and construct turn claims. Any local land field
   or camp gathering claim remains within radius one; authorized water and
   hunter claims follow their own range rules.
4. **Resolve resources.** Each active tile is evaluated once against the
   start-of-turn ecology, all fixed claims, aggregate cultivation efficacy,
   four-profile climate forcing, and the horizon. The calculation returns
   endpoint stocks, integrated receipts, unmet demand, recharge, overflow,
   condition and Wood effects. Commit results in canonical tile order.
5. **Needs and demographics.** Apply realized receipts to the locked prior
   cohort. Resolve managed herds, food, reserves, growth eligibility,
   aggregate production, goods, QoL, and storage. Commit pending deaths and
   births afterward. Newborn needs first enter the next prepared turn.
6. **Close.** Reintegrate hunters, progress societies, check invariants and
   conservation, and append canonical history.

The resource pass never re-enters `EconomicPlanner.Plan`. No receipt changes
the workforce or the locked target. A shared tile has one ecological supply
and all claimants debit that supply without a negative stock. Work may be
parallelized only when the worker owns private result buffers and canonical
commit order remains unchanged.

## Analytical subsystem map

| Subsystem | Start state + fixed allocation + duration → endpoint |
| --- | --- |
| Quiet tile | Four seasonal capacities/rates are reduced over the horizon; recover food, nonfood biomass, freshwater, condition, vegetation and Wood lazily. |
| Active tile | One `EcologicalPressure.Integrate` call accounts for renewable flow, human and herd extraction, finite freshwater, damage, overflow, and receipts. |
| Cultivation | Total farmer and gatherer worker-years, maintenance demand and farm-tool service determine average operating efficacy and field capital; endpoint capital is committed after receipts. |
| Herds | Food/water nutrition, management coverage, production, loss and exponential growth use the same duration. |
| Materials and craft | Local source availability, worker-years, input stock and equipment service are balanced across the horizon; final inventories and receipts commit after resource resolution. |
| People and society | Prior-cohort needs and reserve rules use the horizon; growth, starvation and practice use analytical duration scaling, then births and deaths commit. |

### Seasonal forcing and stock equations

For each of the four stored `SeasonData` profiles, `SeasonExposure` calculates
its exact number of covered days using a closed-form cumulative four-season
cycle. This is four coefficient evaluations regardless of whether the turn
lasts one year or twenty. For a stock with profile renewal rate `r_s` and
capacity `K_s`, the active-tile model uses

`r_bar = sum(w_s * r_s)`, `J_bar = sum(w_s * r_s * K_s)`, and
`K_effective = J_bar / r_bar`, where `w_s` is the profile's share of the
horizon. The zero-rate case uses the exposure-weighted capacity. This keeps
the mean renewal source `r_s K_s` rather than merely averaging capacity.
Managed crop flow and safe extraction flow are exposure weighted separately.
`EcologyMath.Stock` and `ManagedStock` solve the bounded food, nonfood biomass
and freshwater stocks once for the whole duration. Human and herd claims
share a tile's single food/biomass supply. Unmet requests, recharge and
overflow close each conservation ledger.

Condition uses its exponential recovery/damage equation. The active-tile
stock solve uses the recovery-only mean condition and produces integrated
receipts once. An O(4) reducer then applies each climate profile's safe
harvest rate and finite stock window to the **realized** extraction rate,
weights the resulting nonlinear overuse penalty by profile exposure, and
updates endpoint condition. This calculation does not retroactively remove
food that was already awarded from opening stock. It also does not impose
the old seasonal cap on receipts: within-turn dry-season clipping is an
intentional approximation limit of the single transition.

Quiet, undisturbed tiles use exact affine composition of the four ordered
seasonal recovery maps: a whole-cycle map is repeated algebraically, with at
most bounded partial-cycle work at the edges. For recovering condition,
`ResponseCondition` supplies the closed-form response weight of an
exponentially recovering capacity to exponential stock renewal. Latent
projection is pure; materialization commits only the endpoint. Wood recovery
uses one horizon projection and an endpoint capacity clamp. For newly growing
fields, its within-horizon cultivation trajectory is approximated from start
and endpoint capital; this is a calibration limit.

The averaged generator deliberately differs from an exact composition of
eighty active seasonal transitions when a poor season clips stock or
harvest, season order matters, vegetation crosses a threshold, or field
capital changes. The four climate profiles still distinguish seasonal
distributions; the recorded comparison quantifies these effects rather
than assuming parity.

### Cultivation and equipment balance

The resource pass fixes ownership and workforce once after movement.
Maintenance requires farmer-years over the horizon. Available Farm Tool
service is the stock present at the preparation boundary times its configured
farmer-years per unit. Covered maintenance has priority; remaining service
covers a fixed expansion allocation. The covered share determines one
average efficacy, and the tool debit occurs once at endpoint.

With fixed operating fraction `o`, capital decay rate is
`k = ln(2) * (1-o) / DecayHalfLifeYears`. A field's endpoint is
`E_start * exp(-k*Years) + planned investment`; its representative capital
for production is the exact mean of starting-capital decay plus half the
planned investment. The at-most-seven nearby land candidates compete for
finite worker-years through a fixed 32-band marginal-capital solve. Every
score uses the four profiles and the established focus/tile-ID preference;
the band count does not depend on the horizon duration. Bridge work is
estimated before claims, then its endpoint capital is corrected once from
realized terrestrial gathering. Claims and already integrated food do not
change during that correction; any endpoint capacity loss is booked as
overflow.

Herding Tool coverage is total herder-years supplied divided by assigned
herder-years demanded, with wear deducted once. Hand and regular tools used
for manufacturing form one aggregate service ledger. Realized toolmaking
output may serve same-turn manufacturing; dedicated toolmaker labor and
shared Craftsperson labor are balanced algebraically against the goods that
service enables. There is no tool-completion timestamp. Farm and Herding
Tools made after resource resolution replenish the following turn's capital
stock: the resource pass uses its locked starting equipment stock, since
later material gathering can fail under source contention and cannot be
retroactively credited. A future same-turn policy would need a guaranteed
pre-resource material reservation, debited once before field/herd service.

The old seasonal model is a calibration reference, not a bitwise oracle for
this deliberate model change. The new resolver must be exactly reproducible
within its own model across scalar and worker modes, collection reorder, and
save continuation. Conservation and finite-stock bounds remain strict.

## Validation and measured differences

Pending implementation and paired measurements. Record the 20 transition
quick cases, dry marginal and placed camp controls, wet and marginal fields,
mixed gathering, farm-tool coverage, depletion, aquatic food, husbandry and
20/10/5/1/fractional-year horizons. Include population, food produced and
stored, capital/intensity, condition, QoL, and tool/material ledgers. Explain
qualitative differences rather than tuning solely for old seasonal parity.

For performance, compare three sequential warmed Release pairs against the
archived `c33a5b7` resolver at Standard/Continents/1729 with 80 and
benchmark-only 320 tribes. Report AI, preparation, resolution, ecology,
allocations, active tiles, claims, tile-kernel calls, and heap allocation.
Changing the duration from one to twenty years on a fixed world must leave
the major work counts approximately constant. Report the historical `main`
gap separately because `main` lacks the Neolithic economy.
