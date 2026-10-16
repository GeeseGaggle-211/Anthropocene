# Unified social relocation implementation v0.2

Status: implemented on `feature/unified-social-relocation`, based on the reviewed
demographic commit `bcf7f2147fb476b87a976f6c6d4400b5f02ba894`. Native balance and
MLP-training promotion are **not** certified. The verbatim design source is
[UnifiedSocialRelocationContract-v0.2.md](UnifiedSocialRelocationContract-v0.2.md).

## Authority and turn boundary

Immigration and fission remain Close-phase population transfers. The authoritative
state is still camps, societies, workforce ledgers, typed food, inventories,
fixed housing, domestic herds, fractional birth/death progress, deficit history,
contact knowledge and the existing turn reports. No forecast object, employment
provider, cache or candidate result is saved.

`TurnResolver` constructs one transient `SocialRelocationForecastCache` after the
turn's real economy, needs, physical demography and progression have completed.
Immigration evaluates and canonically reconciles first; its intent sources and
accepted receivers sit out fission; fission then evaluates the remaining camps.
Transfers commit at Close. A receiver's larger population therefore affects the
real economy and physical demographic solve on the following turn, not the turn
whose economy has already completed. Pending accepted immigration is nevertheless
included when revalidating later candidates for the same receiver. A final bounded
revalidation then evaluates every remaining flow against the exact simultaneous
endpoint: each camp's own frozen-resident departure plus all incoming cohorts.
Arrivals cannot re-emigrate in that Close pass. Active flows are indexed by source
and destination. Final endpoint forecasts and per-flow marginal X0 forecasts are
cached; when a batch fails, only its two endpoint aggregates, opportunity
reservations and incident flows are invalidated. Unrelated components retain their
decisions. Batches repeat until every committed flow passes.

## Common decision

`SocialRelocationEvaluator` is the shared pure acceptance calculation for an
existing receiver and a new daughter. It compares no-move and move envelopes for:

- the moving cohort;
- the residents remaining at the source;
- incumbents at an existing receiver (omitted for a new daughter); and
- population-weighted aggregate experienced welfare.

It enforces the four contract constraints: mover advantage, nonnegative aggregate
welfare at the current mapping, bounded source harm and bounded incumbent harm.
There is no instantaneous-net-growth sign veto. Utility keeps forecast experienced
welfare separate from cumulative physical mortality risk. Verified outside
surplus is counted only through the future opportunity contract; advertised job
preference is never aggregate welfare.

The Phase-A compatibility mapping deliberately adds no configuration fields:

| Common term | Provisional Phase-A mapping |
| --- | --- |
| Immigration mover threshold | `ImmigrationDeadband` |
| Fission mover threshold | existing policy-specific `Kinship.GainThreshold` value |
| Aggregate threshold | zero |
| Source/incumbent tolerance | `FissionDaughterMargin` |
| Mortality weight | one, in normalized cumulative-risk units |
| Immigration welfare weight | `ImmigrationQualityWeight` |
| Job weight | zero |
| Immigration private cost | existing distance, attachment, lifestyle and return-hysteresis costs |
| Real aggregate cost | zero beyond stocks/provisions already removed from the counterfactual |

This mapping preserves the current numeric levers but is not a certified final
calibration. In particular, the historic fission gain lever was a relative-QoL
quantity and now occupies an absolute shared-utility threshold. Native validation
must justify that mapping or replace it through a separately reviewed calibration;
this implementation does not silently retune it.

## Sustainable-service forecast

`SocialRelocationForecastCache` creates private camp views and returns immutable
`SocialServiceForecast` envelopes. It does not mutate the world or call the real
economy, ecology integrator, or demographic integrator. Capacity construction is
lazy, thread-safe and at most once per used settlement per Close pass; baseline
envelopes are also lazy and shared by immigration and fission.

The estimate uses:

- opening typed food and bounded locally available production, capped by retained
  gatherer/farmer/herder workforce;
- a birth-only upper bound on person-years so finite food and textile stocks cannot
  be spent only against the opening headcount;
- the existing bounded `MaterialEconomy.Forecast`, actual assigned roles, tools,
  recipes and committed imports/exports, closed against finite opening Stone/Clay/
  Wood, finite known sources, technology-qualified specialist extraction and one
  shared generalist material-labour pool;
- fixed housing at the original settlement, portable shelter, and the existing
  bounded daughter-shelter estimate at a founding site;
- retained managed-herd capacity and herder demand scaled by the actual herd
  fraction remaining at each endpoint;
- inherited food-deficit age and the existing starvation-grace law; and
- the existing `PhysicalDemography.Rates` cause hazards and fertility hooks.

Historically realized `FoodNeedsMetRatio`, `ClothPerCapita`, and `Camp.QoL` are not
food/textile production capacities. Current water coverage remains an observed,
explicitly confidence-flagged input for existing settlements because there is no
separate bounded receiver-water production forecast yet. A water-gated daughter
site receives full water coverage in the candidate envelope.

The one-turn physical hazard envelope is integrated analytically over its constant
hazard bands, including starvation grace. This is a cheap forecast, not another
authoritative demographic solve. `Stability` records the minimum of renewable food,
renewable clothing and shelter coverage as a conservative medium-horizon signal.
It is currently diagnostic rather than a fifth admission gate; native evidence in
the validation report shows that this is not sufficient for promotion.

## Immigration

Physical-v1 sources retain existing known-contact, distance, lifestyle, attachment,
return-memory, source-floor and 10% cap rules. Every known receiver is evaluated at
a deterministic bounded size set: the physical minimum, 2.5/5/7.5/10% source
fractions, the configured maximum and the exact legacy-ramp preferred size where
distinct. This removes the generalized evaluator's invalid monotone-bisection
assumption without enlarging the group cap.

Canonical destination/source reconciliation rebuilds the receiver's before-state
from already accepted pending population, typed food, workforce, deficit exposure
and opportunity reservations. It then evaluates the next candidate increment.
After that initial ordering pass, joint revalidation rebuilds the exact endpoint
for chains such as A→B while B→C, rematches bounded opportunity reservations and
removes all failing flows in deterministic batches. Incoming population, food,
deficit exposure, workforce roles and opportunity output are aggregated once per
endpoint. Excluding a candidate subtracts only that cohort across bounded role
dimensions. A rejected flow dirties only flows incident to its source or
destination; chain, eight-source fan-in and cascading-rejection work counters pin
that local behavior.
Accepted departures are all committed before arrivals, preserving simultaneous
Close semantics. Assigned workforce roles move by the same deterministic
largest-remainder split used for fission instead of being erased as casualties;
the unassigned remainder stays implicit gathering labor.

`ForwardUtility`, `Fission.QoLModel.ForwardAt`, `Size`, and the old bisection path
remain available for compatibility and tests. They are authoritative only for a
world whose saved demographic selector is `LegacyQoLNetGrowth`.

## Fission

Physical-v1 crowding and distress fission use the same counterfactual equation.
The trigger records whether the source forecast is physically short of food/water;
it does not select a separate QoL or mortality law. Existing policy daughter sizes,
minimum population, known-site radius, site ordering, spacing, portable-stock
split, domestic-herd split and canonical conflict resolution remain.

Physical-v1 founding assigns the daughter a saved absolute eligibility boundary,
`founding turn + 1 + FissionDaughterCooldownTurns`. The shipped value `1` blocks
the first post-founding Close and permits evaluation at the second; `0` restores
the former next-turn eligibility. The filter is outside the forecast/evaluator,
has its own transient diagnostic, and is bypassed by the exact legacy-demography
path. It changes neither candidate utility nor any threshold.

The source keeps site-bound fixed housing. The daughter gets its proportional
typed food, portable inventory, tools, demographic remainders, deficit history,
expectation, knowledge and workforce. Farmers in the daughter become gatherers as
before because no fields move. Candidate shelter contains only carried portable
shelter and bounded construction from carried inputs; fixed parent infrastructure
is not borrowed.

The old relative `EstimateSplit`/QoL formula remains the exact legacy-save path.
It is no longer the physical-v1 decision authority.

Before scanning sites, the physical path applies a provably permissive bound using
the same service envelope and the same four-gate semantics. For every supported
daughter size, it compares the exact source forecast with an optimistic daughter
that has full food/clothing/housing/water, zero linked-settlement density and the
best physical mortality possible at that headcount. A site scan is skipped only if
every size must fail the mover threshold or the exact source-harm tolerance.
Last-turn realized F/C/H/W are not inputs. `FissionSearchFraction` multiplies the
policy threshold only after the physically valid mover upper bound is constructed,
preserving early cache searches without allowing the screen to reject a candidate
that could meet the final mover gate. The bound can only request the full common
evaluation. It cannot accept a split, create goods, add a survival bonus or impose
an extra positive-source-relief requirement.

## Future employment extension

`IEmploymentOpportunityProvider` is a pure optional interface. Current gameplay
uses `NeutralEmploymentOpportunityProvider`, and `JobWeight` is zero. There is no
world-generated vacancy state, wage, employer, recruitment coefficient, specialist
bonus, observation field or reward change.

An injected test provider can emit typed records. Credible slots are bounded by
the minimum of demand, funding, tools, inputs and workplace capacity, less filled,
provider-reserved and pending canonically reserved positions. Matching is role
qualified and each migrant fills at most one position. Only physical food,
clothing, shelter or verified outside surplus can alter the physical/aggregate
forecast. Slot reservations decline as earlier groups are accepted.

Activating native jobs remains Phase B work and requires real profession records,
employer finance, tools, inputs, workplaces, market demand, executed production and
trade/surplus accounting. The interface is future headroom, not a gameplay feature.

## Compatibility, QoL and RL

The experienced `Camp.QoL` calculation is unchanged. The Elder's authorized
population-weighted QoL reward and observation schema are unchanged. QoL remains
the welfare signal used by economic valuation and diagnostics; it is not a birth
or death input. `WelfareDiagnostics` continues to report settlement-local and
lineage-wide welfare so transfers do not masquerade as reward improvement.

New report fields are optional diagnostics: mover/source/incumbent/aggregate
deltas, forecast F/C/H, risk, stability, cause rates, confidence and matched
opportunities. The higher-QoL/worse-mortality diagnostic compares destination X1
with source X0 on both axes. Transient resolution counters expose forecast/build/
hit/candidate work, joint batches/endpoints/dependencies and rejection causes. No
save version, world/config version, observation schema, frozen RL bank or reward
coefficient changed.

Old saves with an absent demographic selector still load `LegacyQoLNetGrowth` and
retain their historic immigration/fission decisions. A save that already selects
`PhysicalMortalityV1` adopts the unified physical social law on this feature branch
because no second serialized selector was added. That deliberate behavior change
is not backward-decision parity and remains part of the promotion review.

## Scaling and rejected architectures

The intended cost is `O(camps + known local contacts × bounded group sizes +
bounded fission sites × bounded workforce/recipe dimensions)`. There is no all-camp
all-pairs scan, seasonal candidate replay, per-candidate economy resolution,
per-candidate ODE integration, stable matching, or rerouting of rejected cohorts.
Joint immigration state is indexed by endpoint; a failure revisits only incident
dependencies. Canonical tie-breaking and commits preserve deterministic resolution.

Capacity/baseline entries are installed only on first use and density offsets are
preindexed once per Close pass. The safe fission bound now constructs a bounded
physical baseline for eligible camps instead of trusting historic coverage, so it
does more opening work than the rejected historical screen. The latest 80/320 and
mature samples fail the throughput promotion gate despite local scaling; allocation
and material-forecast view construction remain material costs. This is not
permission to restore the unsafe screen or historic QoL coverage. See
[UnifiedSocialRelocationValidation-v0.2.md](UnifiedSocialRelocationValidation-v0.2.md).
