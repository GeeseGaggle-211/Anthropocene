# Current simulation contract index

This index separates approved architecture from calibration evidence and work
that remains open. The integration baseline is `c519cd4f1d35520bb0788171f8fb3e9c851a6dcf`;
issues #26 and #28 are implemented at `5e34e7f`. The completed
[#33 craft audit](NeolithicCraftValidation.md) and
[integration measurements](Issues26-28-33-35Validation.md) record the tested
cohorts and unresolved policy outcomes for `features/neolithic-agricultural`.
Implementation evidence is not itself a new design approval. The index is a navigation
and conflict-resolution aid. The linked normative documents and accepted issue
contracts remain the rule sources.

## Authority classes

| Class | Meaning |
| --- | --- |
| Approved architecture | Current explicit user decisions, the engineering doctrine and the normative `TurnResolutionPasses.md` boundaries. An issue contract applies subject to explicit supersession; closed status alone is not design approval. |
| Shipped calibration | New-world configuration value and measured gameplay choice. Values can be changed by later calibration; they are not universal constants. |
| Inherited behavior | Compatibility or historical save behavior retained for existing worlds. It does not imply that old tuning is current. |
| Diagnostic evidence | Measurements from a stated revision, config, cohort and horizon. Evidence informs decisions but does not itself approve a rule. |
| Deferred / unresolved | No implementation rule is authorized yet. Keep the linked issue visible; do not fill the gap by extrapolation. |

## Rule traceability

| Topic / class | Intended current rule | Current implementation | Authority / section | Implementing revision / verified snapshot | Relevant check | Historical predecessor | Open discrepancy / ticket |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Turn and ecology horizon — approved architecture | One simultaneous player turn is one analytical horizon. The four seasonal profiles force the equations; they are not authoritative economic ticks. Work must remain bounded in elapsed duration. | Prepared prior-world orders and cohorts feed one resolution; active land integrates once with analytical seasonal composition. | [TurnResolutionPasses.md](TurnResolutionPasses.md), “Duration and work bound”; [EcologyFoundation.md](EcologyFoundation.md), recovery / turn sections | `44e30c3`; recovery parity `19cd328`/`c519cd4` | `--horizon-only`; `--issue27-ecology-only` | Seasonal segment stepping and 90-day turn prose are historical. | #29 remains open for cross-era harvest cadence. #26 forecast valuation and #28 physical reserve units are implemented at `5e34e7f`; both tickets remain open for review. |
| Demographic cohort — approved architecture | Consumption, reserve, growth, goods and QoL denominators use the prepared prior resident cohort. Births commit after production, needs and storage; they do not fund their own turn. | Prepared needs are computed before resolution; births are applied at the boundary. | [TurnResolutionPasses.md](TurnResolutionPasses.md), preparation and commit boundaries; [EcologyFoundation.md](EcologyFoundation.md), food allocation | `6ac3e69` | `--horizon-only`; `--economy-only`; `--issue26-forecast-only` | Issue #2's historical post-growth recipe exception is superseded: `TurnNeeds.Production`, feedstock, clothing, meals and growth budgeting all use the locked prior cohort. | #26 forecast valuation is implemented at `5e34e7f`; #31 distinguishes first breakthrough from later discoveries. |
| Tile claims / contested resources — approved architecture with open allocation policy | Current integrator evaluates all claims against shared finite tile ledgers and commits canonical results. This implementation detail does not decide whether entity-ID order is the intended entitlement policy. | Tile integration aggregates claims; outputs commit in tile order. | [TurnResolutionPasses.md](TurnResolutionPasses.md), shared tile ledger | `44e30c3` (shared horizon ledger) | `--issue27-ecology-only`; replay checks | Earlier individual-claim debits are superseded by the shared ledger. | #34 explicitly open: entitlement policy remains unresolved. |
| LandCondition — approved current scope | Current gathering, grazing, cultivation, timber and quiet projection neither damage nor recover stored LandCondition. Its retention as a saved capacity factor is deliberate present scope, not evidence of ecological equilibrium or a completed disturbance system. | LandCondition remains persisted and scales capacity; current land flows do not write it. Aquatic condition has separate behavior. | [EcologyFoundation.md](EcologyFoundation.md), “Land condition” / “Cultivation”; [TurnResolutionPasses.md](TurnResolutionPasses.md), boundary invariants | `2b17cb8` (frozen scope); `19cd328`/`c519cd4` (stock parity) | `--issue27-ecology-only`; `--land-food-only` | Older damage/recovery assertions and coefficients remain historical/compatibility data. | #27 implemented at `19cd328`/`c519cd4`, GitHub issue still open: reconciles active/passive recovery; future disturbance design is unresolved and outside current scope. |
| Land food calibration — shipped new worlds | Current opening profile uses land biomass multiplier `63`, land regeneration fraction `0.03`, land safe-use ratio `0.5`, managed cultivation flux `0.65`, and cultivation effort multiplier `16`. Land abundance/access fractions are configured separately. These are calibration, not universal historical constants. | `OpeningConfig.json`; loaded worlds retain saved rates/capacities and generator provenance. | [EcologyFoundation.md](EcologyFoundation.md), land food profile; [LandFoodRecoveryCalibration.md](LandFoodRecoveryCalibration.md); [CultivationSoftEffortSweep.md](CultivationSoftEffortSweep.md) | `8622750` shipped profile; prior calibrations retain their report SHAs | `--land-food-only`; `--cultivation-cache-only` | Candidate/opt-in reports with multiplier `1`, prior access fractions or other `m/R` settings are not production defaults. #26 forecast is implemented at `5e34e7f`; #27 code/checks are present, GitHub issue remains open. Candidate conclusions require their stated version and cohort. |
| Vegetation raw/effective — approved semantic split | Raw ecological vegetation remains the climate/succession and biome labor input. Cultivation derives a separate effective standing-cover retention for food and Wood service and presentation. | `CurrentVegetation` is retained; the cultivation overlay uses intensity and displacement configuration. | [EcologyFoundation.md](EcologyFoundation.md), “Cultivation land-cover overlay”; [TurnResolutionPasses.md](TurnResolutionPasses.md) | `add5628` | `--issue27-ecology-only`; Unity layer tests for presentation | Historical claims that cultivation rewrites the raw vegetation tier are superseded. | #25 closed ecology contract. No current request to add forest-maintenance discounts; do not reinstate one. |
| Field effort — shipped calibration with future anchors | Physical field capital `E` determines `q`; tool tiers affect field-work labor, not an instant change to physical `q` or a direct yield bonus. Shipped marginal effort multiplier is `16`; future supported-intensity anchors are not implemented technology effects. | Soft effort curve is configured; Stone Farm Tools provide the shipped supported-intensity tier. | [CultivationSoftEffortSweep.md](CultivationSoftEffortSweep.md), “Mechanic and cost”; issue #5 closed | `8cef01e` opt-in; `8622750` shipped | `--neolithic-only`; `--cultivation-cache-only` | `R=1` is the historical quadratic reference path, not the shipped new-world calibration. | #30 open separates physical capital from technology-supported curves; #9/#10 defer later effects. |
| Material/food reserve duration — approved migration + shipped values | New worlds use physical durations, preserving the 20-year reference behavior. Existing saves retain or migrate legacy generation/turn fields under documented rules. Food reserve and natural-storage horizons are physical time, not a new short-turn multiplier. | `GrowthReserveYears=30`, `MinimumStorageYears=20`, `MaximumStorageYears=80` from #28 implementation; Neolithic material and finished-goods horizons remain 20 years. | [ConfigurationUnits.md](ConfigurationUnits.md), reserve sections; [TurnResolutionPasses.md](TurnResolutionPasses.md) | `8a5001e` materials/textiles; #28 food-unit implementation `5e34e7f` | `--economy-only`; `--issue28-food-timebase-only`; configuration/save checks | Old generation/turn counts remain for compatibility. Historical formulas should not be rewritten as if they were physical-year values. | #28 implementation is available for review; GitHub ticket remains open. #29 remains open for cadence decision. |
| Tools / production chronology — approved architecture + inherited recipe contract | Tool consumption follows realized useful output and recipe receipts; blocked or idle work does not consume tools. Planner forecasts must agree with production. Copper tool wear remains undefined. | Output-capacity inventory and production receipts; toolmaking runs before equipment-using recipes in two fixed dependency layers within the same horizon. Materials gathered this horizon may supply manufacturing; no subturn dates or newborn labor are introduced. | Closed issue #3; [Implementation.md](Implementation.md), Neolithic production; [ConfigurationUnits.md](ConfigurationUnits.md) | `59d2251` wear; `44e30c3` dependency layers | `--economy-only`; tool-consumption checks | Permanent tool coverage is superseded by #3; pre-refactor manufacturing chronology is superseded by the analytical resolver. | #10 open defines Copper chain/wear; #33 measured validation is complete; policy limitations remain documented in [NeolithicCraftValidation.md](NeolithicCraftValidation.md). |
| Housing Material — approved architecture (WP5a recipe split; access order is WP5b) | Housing is two Builder steps: Housing Material (flexible Wood/Clay, portable stock, no decay, no tool wear) then the Housing assembly (consumes one material, wears one equipment output, adds fixed Housing). Labour, material and tool wear per Housing unit are unchanged; material demand is the Housing gap minus its stock; priority and scheduling position are Housing's, with material immediately before assembly. | `CraftGood.HousingMaterial` (appended), `CampInventory.HousingMaterial` (optional, absent = 0, `ANTHROPOCENE-11` unchanged), observation schema 16, shared by planner, forecast, gathering and resolver. | [ValueModelDesign.md](ValueModelDesign.md), “Housing Material”; [EconomicAutomation.md](EconomicAutomation.md), “Housing Material (WP5a)” | WP5a (working tree; not yet committed) | `HousingMaterialRecipeChecks` (`--only=HousingMaterialRecipeChecks`); `--neolithic-only` | Single-step Housing with a direct Wood/Clay input is superseded; WP3-era claim labour per Housing unit is unchanged. | WP5b (access order) and Housing Material trade are not implemented; Copper (legacy hook) covers assembly time only. |
| Village transition / departure — approved current behavior | A field-owning cultivation camp becomes a village at a turn boundary when its own radius-one traversable-land average reaches `q >= .25`. An accepted village move is abandonment: village becomes camp, old fields are released from operation and fixed housing stays behind. | Persisted `IsVillage` boundary decision; movement performs bounded local release. AI relocation may be conditional. | [TurnResolutionPasses.md](TurnResolutionPasses.md), movement and village boundary; [AgricultureHusbandryPlaytests.md](AgricultureHusbandryPlaytests.md), methodology | `52b31f9` | `--neolithic-only`; `--economy-only` | Statements that ordinary village departure is forbidden are superseded. | #15 open fission/daughter societies; abandonment does not create an independent society. |
| Craft/route outcomes — diagnostic evidence | Route shares and population distributions describe the sampled cohort, not quotas or guaranteed maturation. Always separate opening plus 25 post-discovery turns from 90-turn / 1,800-year continuation. | Reports carry their own sample setup; do not pool unmatched horizons. | [AgricultureHusbandryPlaytests.md](AgricultureHusbandryPlaytests.md); [Validation.md](Validation.md); [Performance.md](Performance.md) | Report-specific; see evidence ledger below | Report commands and focused suites listed in each source | Earlier 90-turn outcomes are stress/continuation evidence, not a 25-turn craft-chain acceptance result. | #33 evidence: 84 matched 25-turn windows and finite recipe witnesses; natural specialization is not guaranteed. #23/#24 open Camp Elder/husbandry planning. |
| Subsistence progression — approved current rule and tested routes | First breakthrough remains a once-only Paleolithic era transition, with its 200/240-year fallback and opening compatibility rewards. Thereafter each unowned Agriculture, Husbandry or Watercraft capability unlocks independently at 225 effective domain-practice points, including the existing forced-transition carryover (`max(100, 1.25 × experience)`) for the selected secondary domain. Discovery is committed at Close and usable from the next Prepare; later discoveries never repeat opening grants. Watercraft-first societies can reach Agriculture through Gathering and Husbandry through Hunting. | Society owns ongoing domain experience and technology flags. Close processes first transition, then secondary acquisitions; own-only progress/status is in observation schema 15 and the player sidebar. The initial Watercraft transition retains the existing eligible `FisherySeed` compatibility reward. Craft first-tier mastery retains its separate useful-output threshold. | [TurnResolutionPasses.md](TurnResolutionPasses.md); [Implementation.md](Implementation.md); [Issue32FishingCapability.md](Issue32FishingCapability.md) | #31 base `e8e1fa3d3fe68983adec06fc25a285b1c96996f3`; #32 tested candidate source `2375ce10143364503c7b4e92a9049cfb4a6780d5` | Before the test-only #31 follow-up: `--issue32-capability-only` (131 assertions), `--economy-only`, full fixed `Seeds=0` suite and docs checker on `c948300`. After the follow-up at `2375ce1`: `--foundation-only` (46 assertions), `--issue32-capability-only` (131 assertions), contract checker, scale checks and `./Tools/Test-Headless.ps1 -Seeds 30 -Standard -Geography` | Watercraft enables hunter traversal into Shallows; Fish orders remain available from shore. Watercraft alone does not open shared Economy, add an occupation/recipe or create persistent fishery investment. The matched cost and scale evidence is in [Issue32FishingCapability.md](Issue32FishingCapability.md). | #12 owns habituation and animal acquisition; Watercraft-specific economy/fishery investment remains deferred; #9/#30 own second-tier effects. |

## Evidence ledger

Evidence is scoped to its source build and cohort. “Not a current-branch comparison” means the measurement may still be useful for its stated question, but must not be presented as a measured outcome of this integration revision.

| Report | Source/version and config | Cohort / horizon | Applies to current branch? |
| --- | --- | --- | --- |
| [AgricultureHusbandryPlaytests.md](AgricultureHusbandryPlaytests.md) | Report names `8622750`, husbandry change `2b17cb8`, config hash `5CE4E1EE9669FDF168052235B2E8B7824A4DBED7E12698FD52AD5D0D68088B76`; exact report provenance remains authoritative. | Three Standard/Continents/80 worlds; opening plus 80 twenty-year turns (90 turns total); residents exclude connected hunters. | Historical route snapshot only; not evidence for #33's 25-turn post-discovery threshold. |
| [CultivationSoftEffortSweep.md](CultivationSoftEffortSweep.md) | Opt-in `R=16`, `m=63`, access-scale `.5`; production config at report time retained `m=30`, original access and `R=1`. Report does not state a tested source SHA/config hash in its header. | Live/control sweeps include 120 turns; separate route checks continue after opening. | Candidate calibration only; exact tested SHA/config hash is a documentation gap. |
| [LandFoodRecoveryCalibration.md](LandFoodRecoveryCalibration.md) | Candidate branch `codex/land-food-population-sweep`; checkpoints `42cdc56` and `f3238eb`; `m=30`, land fraction `.03`, `LandSafeUseRatio=.5`, `CultivationRegenFlux=.65`, generator 13. | Controlled 8-turn and live 32-turn population lanes; separate fixed-site 25-turn pilots; distinct Standard/80 performance probes. | Candidate profile and diagnostic evidence; no claim of matched current-branch full-cohort equivalence. |
| [Performance.md](Performance.md) | Each subsection names its own candidate/revisions/runtime or config. Do not transfer reported timing to this baseline without a matched run. | Includes post-opening 25-turn performance pairs and other long-horizon/scale audits. | Historical baseline only where source/config match; inspect subsection provenance. |

The 90-turn result means 1,800 simulated years at twenty years per turn. It is
not interchangeable with an opening plus 25 post-discovery turns (500 years).
No historical sweep row should be silently relabeled with the current SHA.

## Explicit supersessions and unresolved edges

- **Seasonal authority:** four seasonal profiles remain forcing. Seasonal tick loops and 90-day player-turn claims are obsolete; see #29 for unresolved cross-era harvest cadence.
- **Need cohort:** prior-cohort resident needs are locked at preparation. Issue #2's old post-growth Fabric exception is superseded for both recipe demand and clothing saturation; `TurnNeeds.Production` locks the prior cohort for every recipe.
- **Clothing feedstock order:** food is allocated to consumption, then the clothing-need feedstock (`NeolithicProduction.ClothingNeedFeedstock`, shared by `EconomicPlanner`, `CampProductionDemand.Build`, `TurnPlanner` needs and `PlanGrowth`), then the operating-reserve refill, then growth with every other manufacturing input (user decision 30 September 2026; see the clothing collapse diagnosis in [ValueModelImplementationPlan.md](ValueModelImplementationPlan.md)). Validated by `ClothingFeedstockChecks.cs` (fixed suite).
- **Tool chronology:** the analytical resolver explicitly permits source extraction, then toolmaking, then equipment-using manufacture in the same horizon. The two fixed recipe layers are an acyclic input balance, not dated economic ticks. All targets and available labor remain locked to the prior cohort; #33 measures useful output and blocked paths without changing this chronology.
- **Land damage/recovery:** legacy land-condition coefficients can remain serialized/configurable for compatibility while current land flows leave `LandCondition` unchanged. This is deliberate current scope, not validated ecological equilibrium and not an approved future disturbance model.
- **Movement/economy:** ordinary accepted village movement abandons village status and releases the old footprint's operation. Earlier “villages cannot leave” prose is obsolete. Daughter-society fission remains deferred under #15; Camp Elder and husbandry decisions remain #23/#24.
- **Camp spacing:** `MinimumCampSpacing` (3 hexes, `SimulationConfig`, validated >= 1) governs movement. `CampPlacement.Resolve` rolls movers back to a fixed point, with a stayer winning and then the lower ID, until every pair of resolved camp destinations with at least one mover is at least the spacing apart (wrapping `HexMap.Distance`). Pre-existing closer pairs may stay; hunter groups are unaffected. `OrderRules.Validate`, `LegalActions` and the heuristic policy refuse destinations within spacing of camps in the actor's own observation (own camps and visible foreign camps); unseen camps are enforced only at resolution. Saves without the field load the default 3. Fission destinations (WP10) will reuse this rule. Checks: `CampSpacingChecks.cs`.
- **Vegetation:** raw `CurrentVegetation` and effective cultivation cover are separate. Cultivation retention must not be presented as rewriting the raw ecological tier. No forest-maintenance discount is authorized.
- **Resource entitlement:** canonical tile order is an implementation constraint, not an approved claim-priority rule. #34 remains open.
- **Social economy (cohesion multiplier implemented; fission, trade etc. remain design):** QoL is now `Q = F · W · R · S(N)` in `NeolithicProduction.ResolveNeeds`, with `S(N) = exp(-(N / N_s)^β)` and regime scale `N_s` (Paleolithic before the first breakthrough, Agricultural with Cultivation, otherwise Nomadic) chosen at the turn boundary via `NeolithicProduction.SocialCohesion`; growth stays independent of QoL. The rest is design: [QoLFissionTradeDesign.md](QoLFissionTradeDesign.md) sets the future QoL formula `Q = B · R · S(N)`, fission, immigration, food trade and proto-towns; [ValueModelDesign.md](ValueModelDesign.md) sets value-based fulfilment and trade; [ValueModelImplementationPlan.md](ValueModelImplementationPlan.md) orders the work. Value-based fulfilment (WP3) exists as `ValueFulfilment` in `ProductionDemand.cs`, selected by the string config `PlannerFulfilment` (`"Uniform"` default, `"Value"`); it prices Cloth, Animal Fabric, Housing and Light Housing only, orders the planner's material-access loops by marginal value, and leaves the resolver's production order unchanged. Fission, immigration and trade remain deferred; `B` in the design formula is the existing `F · W` product.
- **Planner fulfilment rule:** `NeolithicConfig.PlannerFulfilment` defaults to `"Value"` (code initializer and `OpeningConfig.json`): the labour-price fulfilment rule and value-driven production order in `EconomicPlanner.cs`/`ProductionDemand.cs` (`Docs/EconomicAutomation.md`, `Docs/ValueModelDesign.md`). Saves written before the field existed load as `"Uniform"` (the `OnDeserializing` hook) and continue exactly; `"Uniform"` remains selectable and is pinned by the planner-contract fixtures. Covered by `ValueFulfilmentChecks` and `ProductionOrderChecks`.
- **Unresolved queue:** #21 is closed; #26–#34 are open at this baseline. Existing open work also includes #23/#24/#9–#13/#15. Each implementation must follow its issue contract and the approved architecture above; code presence alone does not settle a disputed rule.

## Current new-world config values

The table below is generated from `OpeningConfig.json` by
`python Tools/Docs/Check-CurrentContract.py --write`. It records only selected
ecology and Neolithic values that commonly conflict across reports. Full config remains
the source for all other values. Values are shipped opening calibration unless
the status column says otherwise; legacy save migration is described separately
in `ConfigurationUnits.md` and the save-format contract.

<!-- CURRENT_CONFIG_TABLE:BEGIN -->
| Setting | OpeningConfig value | C# API default | Unit / interpretation | Status |
| --- | ---: | ---: | --- | --- |
| `ReferenceTurnYears` | 20 | 20 | years per Neolithic reference turn | reference scale |
| `MinimumCampSpacing` | 3 | 3 | hexes between resolved camp destinations | shipped placement rule (saves without the field load 3) |
| `PlanningHorizonYears` | 100 | 100 | years | shipped forecast horizon |
| `LandBiomassMultiplier` | 63 | 63 | relative land biomass multiplier | shipped new-world calibration |
| `LandFoodRegeneration` | 0.03 | 0.03 | fraction per legacy BaseDays profile | shipped land recovery input |
| `LandSafeUseRatio` | 0.5 | 0.5 | fraction of sustainable production | shipped land extraction threshold |
| `RichGatherableFraction` | 0.125 | 0.125 | fraction | shipped new-world accessibility |
| `GrasslandGatherableFraction` | 0.05 | 0.05 | fraction | shipped new-world accessibility |
| `AridGatherableFraction` | 0.025 | 0.025 | fraction | shipped new-world accessibility |
| `TundraGatherableFraction` | 0.04 | 0.04 | fraction | shipped new-world accessibility |
| `MaterialReserveHorizonYears` | 20 | 20 | years | shipped new-world duration |
| `UsefulFinishedGoodsReserveYears` | 20 | 20 | years | shipped new-world duration |
| `GrowthReserveYears` | 30 | 30 | years | shipped physical reserve target |
| `MinimumStorageYears` | 20 | 20 | years | shipped physical storage floor |
| `MaximumStorageYears` | 80 | 80 | years | shipped physical storage ceiling |
| `DecayHalfLifeYears` | 80 | 80 | years | shipped cultivation capital half-life |
| `CultivationMarginalEffortMultiplierPerTenth` | 16 | 16 | multiplier per 0.1 q above baseline | shipped new-world calibration |
| `PrimitiveAgricultureSupportedIntensity` | 0.4 | 0.4 | q | shipped primitive intensity anchor |
| `StoneFarmToolsSupportedIntensity` | 0.45 | 0.45 | q | shipped Stone Farm Tool tier |
| `FarmToolFarmerYearsPerUnit` | 20 | 20 | Farmer-years per tool | shipped consumption calibration |
| `PotStorageFoodPerUnit` | 80 | 80 | food units per Pot | shipped storage calibration |
| `PotDecayPerReferenceTurn` | 0.1 | 0.1 | fraction per 20-year reference turn | shipped decay calibration |
| `CultivationRegenFlux` | 0.65 | 0.65 | managed recovery coefficient | shipped cultivation calibration; API default (not serialized in OpeningConfig) |
| `StoneHerdingToolAnimalsPerHerder` | 2.69 | 2.69 | animals per Herder | shipped Husbandry calibration |
| `HerdingToolCoverageYearsPerUnit` | 20 | 20 | coverage years per tool unit | shipped duration calibration |
| `SocialCohesionExponent` | 2 | 2 | exponent beta in S(N) | shipped QoL cohesion calibration |
| `CohesionScalePaleolithic` | 220 | 220 | people (N_s) | shipped QoL cohesion calibration |
| `CohesionScaleNomadic` | 550 | 550 | people (N_s) | shipped QoL cohesion calibration |
| `CohesionScaleAgricultural` | 1780 | 1780 | people (N_s) | shipped QoL cohesion calibration |
| `ValueCurveSteepness` | 3 | 3 | dimensionless lambda in exp(-lambda c) | shipped value-rule calibration (used by the Value planner rule, the default PlannerFulfilment; ignored under Uniform) |
| `DiscountRatePerYear` | 0.01 | 0.01 | per year (r) | shipped value-rule calibration (used by the Value planner rule, the default PlannerFulfilment; ignored under Uniform) |
| `PersonLifeYears` | 30 | 30 | years (L_person = 1 / (1 / PersonLifeYears + r)) | shipped value-rule calibration (used only by the Value rule's expansion valuation; ignored under Uniform) |
| `FoodRiskWeight` | 0.1 | 0.1 | fraction of food-security value (rho) | risk placeholder (reserve-refill food in the expansion valuation; Pots in WP4a); Value rule only, calibration target once disasters exist |
<!-- CURRENT_CONFIG_TABLE:END -->

## Consistency check

Run `python Tools/Docs/Check-CurrentContract.py`. It checks the generated values
against the current JSON, validates links in this index, confirms cited test
files and CLI entry points exist, and confirms the named current source fields
remain available. It does not build or run the simulation.

This local index does not synchronize any external Google Drive authority.
`Docs/DesignSpec.txt` remains a historical source and was not rewritten.

## Related issue boundaries

Closed fixes [#1](https://github.com/GeeseGaggle-211/Anthropocene/issues/1)
(terrestrial Farmer claims), [#2](https://github.com/GeeseGaggle-211/Anthropocene/issues/2)
(historical feedstock alignment), [#3](https://github.com/GeeseGaggle-211/Anthropocene/issues/3)
(output-based tool consumption), [#4](https://github.com/GeeseGaggle-211/Anthropocene/issues/4)
(absolute Pot capacity/depreciation), [#5](https://github.com/GeeseGaggle-211/Anthropocene/issues/5)
(field tooling), [#6](https://github.com/GeeseGaggle-211/Anthropocene/issues/6)
(Wood coupling), [#7](https://github.com/GeeseGaggle-211/Anthropocene/issues/7)
(material/textile physical units), [#8](https://github.com/GeeseGaggle-211/Anthropocene/issues/8)
(Housing mix; its Wood/Clay input now belongs to the Housing Material recipe), and [#16–#19](https://github.com/GeeseGaggle-211/Anthropocene/issues/19)
(managed herds, breeding, historical validation and pastoral crafts) remain
implemented except where the normative prior-cohort/one-horizon contract
explicitly supersedes their earlier ordering. [#14](https://github.com/GeeseGaggle-211/Anthropocene/issues/14)
and #18 are historical validation cohorts, not #33 acceptance evidence.
[#20](https://github.com/GeeseGaggle-211/Anthropocene/issues/20) is a completed queue workflow.
[#21](https://github.com/GeeseGaggle-211/Anthropocene/issues/21) is implemented at
`8fa6e7c`; [#22](https://github.com/GeeseGaggle-211/Anthropocene/issues/22)
keeps accepted movement and economic preparation compatible;
[#25](https://github.com/GeeseGaggle-211/Anthropocene/issues/25) supplies the
vegetation split but its old land damage wording yields to the frozen scope.

The current work implements [#26](https://github.com/GeeseGaggle-211/Anthropocene/issues/26),
[#28](https://github.com/GeeseGaggle-211/Anthropocene/issues/28),
[#33](https://github.com/GeeseGaggle-211/Anthropocene/issues/33), and this index.
[#27](https://github.com/GeeseGaggle-211/Anthropocene/issues/27) has implementation
evidence but remains open on GitHub at this review. Remaining design choices:
[#29](https://github.com/GeeseGaggle-211/Anthropocene/issues/29) harvest cadence,
[#30](https://github.com/GeeseGaggle-211/Anthropocene/issues/30) capital/technology curves,
[#31](https://github.com/GeeseGaggle-211/Anthropocene/issues/31) subsequent discovery,
[#32](https://github.com/GeeseGaggle-211/Anthropocene/issues/32) Fishing capability,
[#34](https://github.com/GeeseGaggle-211/Anthropocene/issues/34) resource entitlement,
[#23](https://github.com/GeeseGaggle-211/Anthropocene/issues/23)/
[#24](https://github.com/GeeseGaggle-211/Anthropocene/issues/24) Elder policy,
[#9](https://github.com/GeeseGaggle-211/Anthropocene/issues/9)/
[#10](https://github.com/GeeseGaggle-211/Anthropocene/issues/10) later technologies,
[#11](https://github.com/GeeseGaggle-211/Anthropocene/issues/11) expeditions,
[#12](https://github.com/GeeseGaggle-211/Anthropocene/issues/12) habituation,
[#13](https://github.com/GeeseGaggle-211/Anthropocene/issues/13) logistics and
[#15](https://github.com/GeeseGaggle-211/Anthropocene/issues/15) daughter societies.
Ticket state is workflow metadata, not an override of the current user contract.
