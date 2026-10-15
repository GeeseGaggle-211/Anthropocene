# Demography and QoL contract v1.1

Status: implemented behind a save-stable demographic-model selector. This is the
repository transcription of the 15 October 2026 authoritative handoff. The old
QoL-mortality model remains available only for unmigrated saves and controlled
compatibility tests.

## Separation of concerns

Composite QoL remains the experienced-welfare signal. Its food, water, clothing,
housing, expectation and social-cohesion terms continue to drive Elder welfare,
immigration, fission and economic valuation. QoL is not an input to a physical
birth or death rate.

`DemographicModel.PhysicalMortalityV1` is the new-world default. Its population
law is annual and continuous:

```text
dN/dt = (b - d) N
b = 0.045 F
d = d_exposure + d_crowding + d_disease + d_legacy_starvation + d_other
d_exposure = 0.03255509888111371 (1 - 0.45 C - 0.45 H)
Phi(N;k) = expm1(kN) / expm1(1000k)
d_crowding = 0.03130836758391647 Phi(N; 0.00001)
d_disease = 0.010436122527972156 Phi(N; 0.000027161883008306556)
```

`F`, `C` and `H` are realized physical food, clothing and shelter service in
`[0,1]`. The fertility factor is exactly one. There is no fitted background
mortality, population floor, mortality ceiling or demographic rescue bonus.
Crowding and disease are zero at zero population, have a 75:25 split at 1,000,
and are equal at 100,000. At full service, 1,000 is an exact stationary root
with nonzero gross births and deaths.

The pre-existing starvation rule is retained as a named compatibility cause,
not folded into the fitted hazards:

```text
d_legacy_starvation(F) = -ln(1 - 0.18 (1 - F)) / 20
```

It begins after 40 physical years of continuous food deficit. Full nutrition
resets the saved deficit duration immediately and does not impose a fertility
cooldown. The old discrete starvation subtraction and this continuous hook must
never both run.

## Paid service and time propagation

Food and textile service close against the same person-years created by the
population path. With `Y = integral N dt`, the kernel solves the local monotone
closure

```text
F = min(1, food_budget / (configured_food_per_person_year Y))
C = min(1, textile_budget / (configured_textile_per_person_year Y))
H(t) = min(1, shelter_people_capacity / N(t)).
```

Budgets come only from authoritative inventory, production, harvest, imports
and provisions already committed by the real turn. The closure does not plan,
manufacture, trade, harvest or replay ecology. Unused stored food is returned by
its typed provenance. Unused remote forage is discarded because it was a local
flow, while unused carried provisions return to the expedition.

The production solver integrates `x = ln N` on a deterministic global grid of
0.05 log-population units. It inserts the positive equilibrium and shelter
threshold as shared knots, linearly interpolates the monotone net rate inside a
band, and uses the exact affine local flow with stable `expm1`/`log1p`
evaluations. Four-point Gauss-Legendre quadrature records person-years, gross
births and each death cause. Cause totals are proportionally reconciled to

```text
N_end - N_start = B - sum(D_cause)
```

within the declared tolerance. Root, band and service-closure budgets fail with
a `DemographicNumericalException`; they never clip a population or hazard.
External physical events, including starvation-grace expiry, split the call at
the event boundary. Mathematical bands are not game ticks and never invoke the
economy or policy.

At the authoritative turn boundary, gross births and gross deaths have separate
saved fractional ledgers. Only their net changes integer residents and the
workforce. This preserves specialization during equilibrium turnover. Camp and
connected-hunter residents form one food/demographic cohort; disconnected
hunters close their own food/provision budget. Dispatch, return, immigration and
fission transfer both fractional ledgers and physical deficit history.

Integer extinction is explicit. A completed gross death is deferred if it would
erase a cohort whose continuous endpoint is still at least 0.5 resident. A
physically declining endpoint below 0.5 rounds to zero, clears the now-terminal
remainders, and records the discarded continuous residual plus its physical
death causes. This is an extinction convention, not a population floor or an
extra mortality hazard.

## Save and migration boundary

`DemographicModel.LegacyQoLNetGrowth` has value zero. A serialized configuration
without the selector therefore loads the exact legacy path and is never silently
reinterpreted. New configurations and `OpeningConfig.json` select
`PhysicalMortalityV1` and disable the legacy QoL-mortality lever.

Migration is explicit: `DemographicMigration.ToPhysicalV1(world, reason, sourceCommit)`
converts saved deficit turns to physical years, changes the selector once and
appends a fingerprinted `DemographicMigrationRecord` containing the reason,
source simulator commit, clock, entity counts and population. A duplicate,
reasonless or source-less migration fails.

## QoL, migration and fission

The existing `Immigration.ForwardUtility`, `Fission.QoLModel.ForwardAt`,
destination ranking, thresholds, deadband and hysteresis remain unchanged.
Physical mortality projections are diagnostic only. Migration records expose
source/destination mortality before and after a transfer and flag a
higher-QoL/worse-mortality attraction. Fission records expose parent and daughter
mortality and whether the daughter has a nonnegative physical net rate.

Fission daughters inherit domestic expectation and accumulated food-deficit
history. People, typed food, portable inventory, demographic ledgers and herds
retain their existing proportional conservation rules. Fixed shelter remains at
the parent site. Immigration moves the same pre-v1.1 goods set (people and typed
food) while additionally transferring proportional demographic ledgers and
population-weighting deficit history. No utility weight or social-flow threshold
is changed by this refactor.

## Diagnostics and RL boundary

`DemographicReport` records model identity, gross integer and continuous births
and deaths, all causes, person-years, `F/C/H`, effective rates, root/band/service
counters, equilibrium classification, cause correction and conservation error.
Observation schema 19 adds the owning settlement's QoL and physical demographic
diagnostics. It does not expose foreign private state.

`SettlementWelfareReport` records local population, QoL and the additive
population-weighted QoL amount. `LineageWelfareReport` reports that amount and
its mean separately, plus cross-lineage immigration and fission transfer counts.
These are diagnostics only. The authorized population-weighted QoL reward,
jurisdiction, migration utility and action semantics are unchanged; there is no
survival reward shaping.

Promotion to serious MLP training requires all numerical, conservation,
recoverable-distress, real-economy, social-flow and native scale gates. Solver
parity alone is insufficient. The current evidence and explicit blockers are in
[DemographyQoLValidation-v1.1.md](DemographyQoLValidation-v1.1.md).

## Implementation map

- `Simulation/Demography.cs`: pure rates, service closure, nonlinear propagation
  and explicit save migration.
- `GenerationalEconomy.cs`: real-budget integration, gross-ledger reconciliation
  and demographic reporting.
- `NeolithicProduction.cs`: experienced QoL recomputation after physical service.
- `Immigration.cs`, `Fission.cs`, `TurnResolver.cs`: conserved social transfers
  and welfare/mortality diagnostics.
- `WelfareDiagnostics.cs`: local-versus-lineage welfare accounting.
- `Tools/Headless/DemographyChecks.cs`: kernel, turn, replay, dispatch and save
  gates.
- `Tools/Headless/DemographyRegressionChecks.cs`: shock and attribution paths.
- `Tools/Headless/DemographyBenchmark.cs`: native adaptive-midpoint comparator.
