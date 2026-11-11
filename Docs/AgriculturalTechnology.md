# Plough and Elder-controlled irrigation

This implements the approved November 2026 plan on the smoothed-calendar branch
`6d58ca4493700fc01156dc95ae112cb486f853b3` (PR #48). The feature is opt-in;
`AgriculturalTechnologyOpeningConfig.json` supplies both technologies and the
versioned historical smooth calendar. Launch a new Unity game with
`--anthropocene-agricultural-technology` to select it. Existing saves continue
using their serialized configuration. The ordinary opening profile, active RL
training, action schema, observation schema and demographic laws are unchanged.

## Farm Tools

There is one Farm Tools recipe, inventory and 20 Farmer-year service budget.
Useful Carpenter output in an operating agricultural settlement with Carpentry
accumulates fractional Plough practice. Attainment commits at Close, so output
and practice cannot improve the interval that earned the knowledge.

Usable Plough knowledge changes equipped operating Farmers' absolute maintenance
efficiency to 1.50 and multiplies crop harvesting productivity by 1.25 after
existing Farmer productivity. Maintenance replaces the stone-tool tier; it does
not multiply it. Crop/wild composition uses the existing capacity-based harvest
mixture. Wild harvesting receives no Plough multiplier. Coverage interpolates
between equipped and unequipped worker output rates. Maintenance and harvest
reserve the same operating Farmer-years once. Dedicated expansion retains its
existing stone-tool work and receives no additional Plough effect.

Forecasting and resolution share the covered-work calculation. Zero inventory
means zero benefit; fractional exhaustion and daily wear debit the existing
service ledger. Cultivation capital, intensity, marginal effort, natural
capacity, tool production costs and the farming footprint are unchanged.

## Elder investment and construction

Only DemandControl creates the internal infrastructure production order. No
player construction action, tradeable canal inventory, Plough good or upgrade
transaction is added. Manual Builder assignments alone cannot authorize canals.

New investment requires usable Irrigation knowledge, an uncovered development
food/feedstock need, and every legally available naturally fertile footprint
tile individually at `q >= 0.30`. Undeveloped fertile tiles count; the empty set
passes. Fertility is annual-average **natural** seasonal moisture at least 0.50;
service never changes that classification. Ordinary farming rights apply.

The Elder compares the project's native next-boundary retained food gain with
further field intensification using the existing development priority. The
prospective network uses the same finite allocator, including existing branches,
drinking demand, and current-location wild/managed herd claims. A forecast is
conditional on its supplied claims and terrain; accepted movement can change
claims. The development score charges complete construction/manufacturing work
and harvesting work. It does not extrapolate one renewal pulse into a century
of repeated harvests. This matches the current field-development comparator,
which supersedes older gross-renewal/discounted-annuity approximations.

Construction uses Housing Materials, Builder work and ordinary equipment service.
Missing Housing Materials must themselves be manufactured/extracted through the
existing complete-bundle allocation. Housing and canals share stock and labor
reservations. A segment costs 10 materials and 5 Builder-years **plus prepaid
upkeep for the construction interval's physical duration**. The first service
period is therefore paid before activation. Partial construction persists,
including fractional prepaid commissioning years. Completion activates at Close
for the following interval; it cannot irrigate the interval that built it.

Continuity requests replenish the next service interval after current funding
is spent. Upkeep costs 0.10 Housing Materials and 0.25 Builder-years per segment-
year, plus ordinary equipment service, and restores hydraulic condition. The
fertile-tile gate does not block upkeep. Unfunded time supplies no water.

Useful local Builder production, including needed housing/material work, earns
Irrigation practice with Construction, Cultivation, active farming and a known
seasonally beneficial gravity-fed opportunity. Discovery does not require
Plough, completed canals, or the new-investment cultivation gate. It commits at
Close with the same fractional practice mechanism as Plough.

## Water, ecology and soil

Sparse segment records retain stable IDs, operator, source, parent/from/to,
throughput, construction, funded/commissioning years, condition and physical
timestamps. Sparse field records retain one deterministic endpoint, operator,
serviced area, soil development, last water service and timestamp. A route ends
on the serviced field; it does not silently irrigate uphill adjacent fields.
Routing is conservatively restricted to known, continuous, cultivable local
terrain (at most seven footprint tiles); it rejects saltwater, ridges, uphill
steps, disconnections and cycles. This is a local gravity model, not a hydraulic
head or large regional canal simulator.

The authoritative pass collects all accepted essential withdrawals first. The
native horizon water kernel defines a finite source envelope. Irrigation shares
only its residual, proportionally within shared segment throughput and funded
service limits. Segment retention is 0.80; losses are recorded. Every source is
integrated once and every receipt conserves withdrawal = delivery + loss.
Drinking reserves 0.10 abstract water units per person-year in every enabled
agriculture profile, including the diagnostic no-technology arm. This adds the
previously absent explicit human water claim; demographic response laws are not
retuned. Existing herd withdrawals retain their native units and routing.

Actual delivery determines service fraction `I` and effective moisture toward
the 0.50 target. Natural climate, wild-food profiles and generated geography are
immutable. Water changes managed capacity and renewal; expanded capacity fills
through renewal rather than immediately adding food or biomass inventory.

River-fed, non-river fields advance `a` at
`I * O * min(1, q / 0.40) / 500` per physical year, capped at one. Lake-fed canals
water crops without advancing soil. Natural river/alluvial tiles receive no
second soil-development increment. `O` and `q` are the horizon's opening
operating fraction and intensity, consistent with the aggregate transition;
constant-condition subdivision is tested, not arbitrary within-turn staffing
changes. The analytical capped transition supplies ending and mean soil values
to the shared ecology kernel. Eligible serviced area uses
`B_water * (1 + 0.50 * a)`; no multiplication of already improved capacity occurs.
Water-only diagnostic arms set the soil-capacity coefficient to zero; their
latent soil clock is retained so the economic comparison changes only the soil
effect. This is explicitly recorded in the experiment settings.

Soil persists through dry/unfunded intervals and changes of ownership. Current
moisture still constrains its productive capacity. At Close, a network with one
legal successor transfers its continuity obligations without resetting soil;
contested networks retain records but cannot serve a field without valid rights.

## Save, observation and UI contracts

`ANTHROPOCENE-11`, world/config version 7 and observation schema 22 remain in use.
New optional fields are `SimulationConfig.Agriculture`, `WorldState.Canals`,
`WorldState.IrrigatedFields`, `TurnRecord.IrrigationWater` and
`TurnRecord.AgriculturalFood`. Absent agriculture settings load disabled; no load
path regenerates geography or silently migrates old saves. New work receipt IDs
`CanalConstruction=11` and `CanalMaintenance=12` append after `FishingTools=10`.
They are infrastructure work, not inventory goods. Existing IDs are unchanged.

Water receipts record withdrawals, delivery, losses, service fractions and soil
before/after. Agricultural food diagnostics attribute one conserved plant-food
receipt to crop/wild shares using the same opening capacity composition as
crop-only harvesting; they are not independently stored botanical inventories.

The owner inspector explains Farm Tools benefits, material/labor costs, the
investment gate, construction count, funded service, water supply and soil
progress. There is no canal override. Public/actor observation layouts are
unchanged, so existing RL policies and ongoing training remain untouched.

A calendar compatibility fix allows provisions legally carried from the last
committed interval to survive validation after the next horizon shortens. It
does not increase the next provisioning allowance or discard carried stock.

## Reproduction and validation

Run from the repository root with .NET 8:

```sh
dotnet run --project Tools/Headless/Anthropocene.Headless.csproj -c Release -- --only=AgriculturalTechnologyChecks,HousingMaterialRecipeChecks --jobs=1
dotnet run --project Tools/SmoothCalendar/Anthropocene.SmoothCalendar.csproj -c Release
dotnet run --project Tools/AgriculturalTechnologySweep/Anthropocene.AgriculturalTechnologySweep.csproj -c Release
dotnet run --project Tools/AgriculturalTechnologySweep/Anthropocene.AgriculturalTechnologySweep.csproj -c Release -- --labor-audit
dotnet run --project Tools/AgriculturalTechnologySweep/Anthropocene.AgriculturalTechnologySweep.csproj -c Release -- --benchmark
python3 Tools/AgriculturalTechnologySweep/Summarize.py
python3 Tools/Docs/Check-CurrentContract.py
```

The sweep writes starting saves and a manifest with source/calendar SHA-256,
serialized settings, epoch, seeds and checkpoint hashes. It runs native 96x48,
four-player worlds for 600 organic years, then forks identical mature checkpoints
for the full treatment matrix for at least another 600 physical years. Diagnostic
knowledge is granted identically at the fork; organic attainment is recorded
separately. Principal balance evidence is entirely smoothed-calendar. Numerical
fixed-duration mechanism fixtures are not balance selection evidence.

The labor audit replays every treatment with the existing craft diagnostics,
requires an identical authoritative fingerprint, and records maintenance,
harvest, expansion, overlapping farmer work, equivalent gathering work,
extraction and actual manufacturing work. Net measured agricultural/craft work
excludes unrelated hunter, Fisher and Herder jobs; it is not total population
work. Manufacturing includes Housing Materials and tools, rather than treating
missing inputs as free.

The fixed-staffing diagnostic pins operating Farmers in checkpoint settlements;
other Elder jobs and subsequently founded settlements remain endogenous. This
isolates field labor without changing demographic rules. It is not a claim of
fixed total workforce or fixed population. Retain this distinction when
interpreting food/labor differences. The benchmark separately measures the
water/ecology kernel for 1/16/80/320 networks, two population sizes and daily,
20-year and 600-year numerical horizons; those timings are not balance evidence.

See [AgriculturalTechnologyValidation.md](AgriculturalTechnologyValidation.md)
for measured results and remaining acceptance limits. Unity interaction testing
requires Unity 6000.5.10f1; the cloud environment used here has no Unity editor.
