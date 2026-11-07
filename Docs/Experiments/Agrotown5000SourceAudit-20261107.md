# Self-feeding 5,000-resident agro-towns: source audit and diagnostic evidence

Date: 7 November 2026. Status: investigation and unmerged diagnostic, NOT production implementation or balance approval.

## Provenance

- Production source: main `07f3501c2d5e047e44d61e85cc0d4613b54572d6`.
- Executed experiment: `f60aa0106f3311f99660b0abbd007d27f3bd22f0` on `experiments/agrotown-5000-audit-20261107`.
- Successful Actions run: https://github.com/GeeseGaggle-211/Anthropocene/actions/runs/42367918047 ; job 128389804661.
- Artifact: `agrotown-5000-source-audit`, ID 13572313724, ZIP SHA256 `6b116d29461854532168794bfb545468101d53f2331c1912674d5d7a14f55042`.
- Separately inspected frontier implementation: `feature/elder-model9-native` at `547d9c4e8dff5b7e15d0823a7681b92f904ab0e0`. This is NOT asserted to be the currently running training pin.

The workflow checked that all production simulation/resource files are unchanged. Post-download analysis verified all 80 source hashes and byte identity against the preceding river-sweep source archive. Main, existing feature branches, training, checkpoints and infrastructure were not changed.

## Executive conclusion

Do not multiply q or biomass to force a 5,000-person town. First resolve land-harvest time semantics; reuse the existing settlement-specific q_k architecture from the development lineage; introduce paid settlement-scale services and organization; then expand agricultural reach and delivered-surplus networks for cities. Food production, field-work cost, demographic viability and cohesion are four distinct constraints.

The experiment demonstrates a viable physical-production direction, not a completed self-sufficient town. Full goods, household water, storage, demography and social movement were not simulated in the food trials.

## Verified code findings

1. `EcologicalPressure.ResolveLandOpeningFlow` takes opening land stock once, and `LazyEcology.RecoverLandAfterHarvest` recovers it after the receipt. This is the existing issue #29 temporal contract problem; no production replacement is selected by this report.
2. `CultivationEconomy.Footprint` is hard-coded to radius one. Owner release also rejects distance >1. `FoodHorizonForecast` has a matching distance-one assumption. Merely increasing a range setting would not extend the complete physical contract.
3. `ContactKnowledge.HinterlandRadius` expands explored knowledge, not the productive farming footprint. Knowledge, legal reach and paid work must remain distinct.
4. Main's `NeolithicCapital.EffortForCapital` ties the nonlinear knee to ReferenceIntensity. The inspected model9 lineage already has a separate knee overload and `SettlementProductionFrontier.EffectiveCultivationEffortKnee(world,camp)`. Reconcile that implementation; do not create another global q knob or change physical q(E)=min(1,0.4*sqrt(E)).
5. `PhysicalDemography.CrowdingMitigation` and `DiseaseMitigation` currently return 1. `AdvanceCore` uses default mitigation arguments, while `FastNet` explicitly multiplies both hazards by 1.0. Implementing the two hooks alone would therefore not propagate service effects to all authoritative/planning paths.
6. `NeolithicProduction.SocialCohesion` is exp(-(N/N_s)^2), with N_s=220*(1+7.1*H). Full housing gives N_s=1,782. At N=5,000 cohesion is 0.000380998; this directly multiplies QoL. Even fed and housed larger settlements face a severe welfare penalty, with consequences for relocation/fission.
7. The inspected main trade path excludes player 0 and uses bounded existing contacts/food accounting. It is a substrate, not a completed player-facing urban freight network. Cities require actual delivered surplus, paid throughput and preserved source needs/reserves.

## Experimental design

Five shipped-profile 1024x512 Continents worlds, 80 founding tribes: seeds 11, 23, 47, 101, 1729. Reused the preceding river-sweep selection rule: 16 biomass-quantile samples per world from moist river-center seven-cultivable-tile footprints in the upper biomass quartile. There are 80 fertile centers plus the seed-11 Desert river valley preset.

Native rows call unchanged `EcologicalPressure.Integrate` from empty stock for at least 800 years, measuring the final eight horizons. q is fixed and operation fully maintained. Extended-catchment rows use bounded traversable-land BFS to radii 1-4, with current generated terrain, not copies of the best tile. q=0.4/0.5/0.6 and q_k=0.4/0.52/0.62 are crossed. The knee changes cost only via the inspected development-lineage formula. No Farm Tools are supplied.

A 15% increase in work per additional land-route step beyond radius one is an explicit travel-cost sensitivity, NOT approved transport calibration. Full catchments are maintained in these tests rather than optimizing which fields to abandon. Sample footprints may overlap; these are alternative hypothetical towns, not simultaneous uncontested claims.

The native cadence comparison is a maximum physical receipt test with harvest labor calculated afterward; it is NOT an equal-fixed-workforce native policy comparison. That additional acceptance test is still required for a production transition.

## Native harvest cadence: identical seven-field q=0.4 geography

| Decision/harvest interval | Median food-supported residents |
|---|---:|
| 20 years | 1,189.30 |
| 5 years | 4,757.22 |
| 1 year | 6,436.32 |

The annual versus 20-year ratio is about 5.41. Neither field q nor biomass calibration was increased. The old 1,189.30 result is reproduced to <4.1e-16 relative difference; it was correct for the old temporal convention, not an invariant biological ceiling.

## Bounded analytical seasonal-flow prototype

For each of four fixed seasonal profiles, the experiment sets a predeclared harvest rate

`h_s = 0.8 * r_s * K_s + M_s`,

where K is accessible food capacity, r its native recovery rate, and M the native managed inflow. At constant forcing this targets equilibrium food stock 0.2*K. It does NOT guarantee a 20% stock floor in every changing season and does not represent the earlier experiment's 'leave 20% of opening stock' rule.

A season's stock transition is a capped affine map, with an initial capacity clamp. Four maps form an annual map. Native `SeasonalAffine.Repeat` evaluates repeated years without a year loop. A slow reference calls native `ManagedStock` and `Stock` across 200 years; this loop belongs only to the diagnostic. Partitioning the fixed plan into 1-, 5- or 20-year calls reproduces the same endpoint. Capital/forcing remain frozen; general partial horizons, evolving capital, finite-use shutdown, rival claims, livestock spillover and depletion-aware receipt composition remain implementation gates.

This is a candidate physical food envelope, NOT a new authoritative resolver. The 0.8 coefficient is disclosed experimental policy, not selected production tuning. No new biological multiplier was introduced.

| Actual field q | Median food-supported residents | Capacity divided by 1.25 | Sites meeting 5,000 plus 25% capacity headroom |
|---|---:|---:|---:|
| 0.40 | 6,219.82 | 4,975.86 | 38/80 |
| 0.50 | 7,906.55 | 6,325.24 | 78/80 |
| 0.60 | 9,609.09 | 7,687.28 | 80/80 |

These are seven-tile results. Headroom is spare production capacity, not mandatory waste or additional consumption. Food is the shared terrestrial PlantFood pool including wild food, not separately measured pure cereal output. Nonfood feedstock, losses, drinking water and goods are not charged.

At q=0.50 the candidate median peak seasonal farm workforce needed for 5,000 meals is 2,188.95 population-equivalent workers. With q_k=0.52, median maintenance is 187.50; these obligations overlap, so they are not added. q=0.60 with old q_k=0.40 instead needs 5,638.31 maintenance workers, exceeding the whole 5,000 population. Moving q_k to 0.52 lowers that maintenance to 433.19 (0.62: 270), without changing physical output. This is the proper role of the technology frontier.

The desert preset is less favorable: candidate food capacities at q=0.4/0.5/0.6 are 4,605.45/5,854.03/7,114.01. q=0.50 there does not clear the proposed 6,250 food-equivalent capacity requirement.

## Wider catchments are a separate route

With native 20-year pulse semantics retained, q=0.50 and q_k=0.52:

- Radius 3: median food capacity 7,584.00; 6,067.20 after the 25% headroom divisor. Sixty of 80 sites meet the food-headroom test. Median work to feed 5,000 is 2,608.07 under the disclosed travel surcharge.
- Radius 4: median food capacity 12,130.43; 9,704.34 after the headroom divisor. Seventy-six of 80 meet the food-headroom test.

Thus extra land can bridge the numerical gap even without changing pulse semantics. It does not fix turn-length dependence. Conversely, combining the new flow envelope and very large catchments immediately yields much larger capacities; do not enable both changes indiscriminately and claim that balance is established.

## Demography: a separate necessary condition

Native `PhysicalDemography.Rates` with full nutrition/clothing/housing and maximal baseline fertility gives, at N=5,000:

| Annual rate | Value |
|---|---:|
| Birth | 0.045000 |
| Exposure mortality | 0.00325551 |
| Crowding mortality | 0.15972016 |
| Disease mortality | 0.05513432 |
| Net instantaneous rate | -0.17311000 |

This is an instantaneous rate at that state, not a constant fraction of residents dying in every subsequent year. At N=1,000 the same full-service net rate is zero. In a diagnostic applying one equal multiplier to the two population hazards, zero net growth at 5,000 requires multiplier 0.1942919 (80.57% reduction); at 10,000 it requires 0.0936147. These figures are constraints imposed by the current curve, NOT approved sanitation technology bonuses. Finite-food/surplus-funded birth behavior may be stricter.

A real solution needs serviced settlement area, water/waste or other explicitly paid local service effects plus organizational capacity. It must not simply lower global mortality, raise fertility, turn off fission, or assign a free city-label exemption. Preserve primitive behavior when new service capacity is absent. Propagate a common physical-service envelope into the authoritative demographic solve, fast planner, welfare and social counterfactuals.

## Recommended implementation sequence

1. Harvest-timebase contract and bounded shared-stock flow kernel; keep legacy saves on their old model. Test q=0, cultivated, mixed-herd, dry and fertile cases, fixed-labor comparisons, partial durations, conservation and all claimants. Do not merely remove the opening-stock cap.
2. Reconcile existing settlement-local q_k/frontier work. Keep ReferenceIntensity and physical E fixed. Charge real equipment service and preserve chronology. Do not expose prospective response curves to the actor by default.
3. Establish one autonomous 5,000-town benchmark with finite resource-complete materials, actual clothing/shelter/tool replacement, water/storage, food reserves and paid civic services; distinguish maintenance of a mature fixture from organic establishment. Require sustained zero routine food imports and no exogenous replenishment.
4. Add economic catchment access as a separate capability with paid route costs and sparse active fields. Unify owner validation, forecasting, source selection, development and abandonment; do not repurpose knowledge radius or change ReferenceFootprintTiles to grant cheaper per-tile capital.
5. Add city expansion through paid residential/service/organizational capacity and delivered surplus from independent producing settlements. Each source protects its own needs and reserves; routes have capacity and cost; no double claim on overlapping land.

5,000 is a calibration target, not a cap. A larger city should grow because more food can be delivered and more residents can be serviced and organized, not because a technology flag multiplies its population.

## Validation scope

- 1,106,520 native tile integrations; 11,244,000 slow reference seasonal steps for the prototype.
- Maximum relative mass-balance residual 2.2202390e-16.
- Maximum candidate map/reference and horizon-partition error 5.7310592e-15.
- Native final-tail variation: zero in tested cases.
- 11,907 raw rows contain 81 exact duplicated baseline rows from overlap between cadence and spatial grids. Independent analysis removes them: 11,826 unique site/scenario rows. Duplicates are not extra independent evidence. All 292 raw summary groups were recomputed successfully.
- No full new headless-suite run, Unity playtest, trained-policy evaluation, arbitrary-horizon proof or functioning 5,000-person live economy is claimed.

Reproduce with `dotnet run -c Release --project Tools/AgrotownAudit/Anthropocene.AgrotownAudit.csproj -- TestResults/AgrotownAudit` at the executed commit. The evidence bundle contains source/configuration hashes, raw per-seed and pooled output, runtime and exact experiment SHA. The investigation branch remains unmerged.
