# Neolithic Fishing implementation plan

**Source contract:** `Anthropocene_Neolithic_Fishing_Contract_v0_9.md`

**Repository baseline:** `86cba712dfd76673bd9637052543bde85f810e0b`
**Status:** implementation, local validation, and the independent Astra code review are complete on `features/neolithic-agricultural` behind compatibility gates. The general production opening remains disabled.

## 1. Outcome and scope

Implement the first resident Neolithic Fishing slice described by the source
contract:

- Watercraft can open the shared goods economy when the Fishing feature is
  enabled.
- Camps can assign resident Fishers to as many as three reachable Shallows or
  Lake grounds.
- Fishers harvest, maintain, and build sparse fishery capital through one
  combined work allocation.
- Fishing competes with gathering, farming, herding, and production for the
  same resident workforce and biological food target.
- Fishing Tools, fixed Housing, Cloth, village qualification, knowledge,
  observations, fission, and reporting participate in the existing economy.
- Existing Hunter fishing, passive aquatic gathering, and resident Fishing use
  one finite biological pool and one authoritative integration per tile.
- Cold-water seasonal pressure is corrected before Fishing is enabled in a
  production opening profile.

Sailing, Ocean capital, trade in Fishing Tools, and a separate fishery service
class are outside this slice.

## 2. Non-negotiable constraints

1. Preserve the analytical turn boundary. Do not introduce seasonal, annual,
   or per-day world-state writes.
2. Integrate an active tile exactly once per turn after every claim and
   ecological overlay has been fixed.
3. Parallel work is pure; operator assignment, endpoint state, receipts, and
   knowledge commit in canonical order.
4. Keep fishery capital sparse. Do not reuse `Tile.Fishery`, dense tile flags,
   or `Tile.CultivationCapital`.
5. Feature-off worlds retain the current Watercraft `FisherySeed`, Hunter
   fishing, passive gathering, economy eligibility, saves, observations, and
   deterministic fingerprints.
6. Old saves load with resident Fishing and corrected seasonal aquatic pressure
   disabled unless a migration contract explicitly enables them.
7. DemandControl is the only supported automatic allocator for resident
   Fishing. Enabling Fishing with Legacy allocation is a configuration error.
8. Policies and UI may use current visible ecology, own visited-ground
   snapshots, and remembered static geometry; they must not receive live hidden
   aquatic stock.

## 3. Resolved design decisions

The following values affect authoritative results and are the accepted
implementation defaults:

1. `NeolithicFishingEnabled` is false in the general production opening until
   the validation milestone; a named Fishing profile enables it.
2. `CorrectedAquaticSeasonalPressureEnabled` is a separate compatibility gate,
   false in saves where it is absent. A Fishing-enabled configuration requires
   it to be true. This preserves exact feature-off behavior while allowing the
   shared aquatic kernel to be corrected for Fishing worlds.
3. A remembered but unsurveyed ground uses the conservative zero-yield,
   zero-capital prior. Raw remembered accessible food, biomass and condition
   are not exposed to general observations.
4. Resident `FisheryHarvest` practice receives the existing
   `ActiveFishingPracticeMultiplier`; passive and Hunter practice keep their
   established paths.
5. `CraftingPriorities.FishingTools` defaults to `0.10`, parallel to Farm and
   Herding Tools.
6. One Fishing Tool covers 20 harvest-Fisher-years and multiplies covered
   catch by 1.5. This service affects catch only, not biological capacity or
   ecology.
7. Effective Fisher work is jointly available to harvest and fishery
   maintenance/building; requested harvest work is not subtracted from the
   maintenance/build budget. Fisher work also remains in the resident
   maintenance/build workforce total.

## 4. Target architecture

### 4.1 Authoritative sparse state

Add a `FisherySite` record containing at least:

- tile ID;
- maintaining operator camp ID, or `-1`;
- physical capital `E`;
- `LastUpdatedDays`.

Store sites in `WorldState.FisherySites`, sorted by tile ID at normalization and
save boundaries. Capital is lazily projected from `LastUpdatedDays`; the world
does not scan every site every turn. Persist the camp's last-selected grounds
where required for incumbent selection and departure release.

Add owner-specific visited-ground economic snapshots to Society knowledge.
Snapshots are sparse, dated, copied on fission as specified, and pruned when the
corresponding explored memory is forgotten.

### 4.2 Transient preparation state

Add a `FishingPlan` per eligible camp to `PreparedTurn`. It freezes:

- accepted camp location and resident cohort;
- candidate and selected grounds;
- route type and distance;
- opening projected capital;
- assigned Fisher-years and Fishing Tool service;
- maintenance, harvest, and build work;
- average and ending capital;
- average management intensity and operation fraction;
- requested catch per ground;
- departure and operator-eligibility flags.

No part of a `FishingPlan` is saved, observed, or recomputed during resolution.

### 4.3 Shared mathematical helpers

Extract semantically neutral capital functions from cultivation for:

- intensity from capital;
- effort from capital;
- capital from effort;
- decay under an operation fraction;
- mean decayed capital over a horizon.

Cultivation must continue to call the same functions and retain existing
results. Fishing uses the common equations with its own aquatic capacity and
management overlay; it does not reuse the cultivation planner or its land
ecology terms.

## 5. Work packages

### WP0 — Cold-water ecology correction

Correct the known defect before enabling Fishing in a production profile.

#### WP0.1 Reproduce and pin the defect

- Add fixtures with strongly different cold seasonal biomass, recovery, and
  safe rates.
- Demonstrate the current failure: one horizon-wide realized rate is compared
  independently with every seasonal safe rate.
- Pin opening stock, realized harvest, endpoint stock, condition damage, and
  duration scaling for Hunter and passive aquatic claims.

#### WP0.2 Implement a conservative analytical reducer

Keep the existing single horizon stock solve. After the solver fixes total
realized aquatic harvest `H`, allocate that realized amount across the four
seasonal exposure profiles for damage accounting only:

1. Compute each season's exposure days `d_s`, sustainable rate `S_s`, and
   finite realizable rate ceiling `F_s` from that season's capacity, recovery,
   and managed flow.
2. Find a constant pressure rate `lambda` by fixed-count bisection such that
   `sum(d_s * min(lambda, F_s)) = H` within the established numerical tolerance.
3. Define seasonal realized rate `R_s = min(lambda, F_s)` and compute damage
   from `R_s / max(epsilon, S_s)` for that season.
4. Weight damage by `d_s / duration` exactly once.
5. Assert that seasonal realized amounts conserve `H`; no season may receive
   more than its finite ceiling.

This is a four-profile analytical reduction, not a seasonal timestep loop. It
does not change the horizon harvest receipt or create intermediate world state.

If review selects a different seasonal allocation rule, replace this subsection
before implementation; do not tune Fishing around the current defective rule.

#### WP0.3 Compatibility and shared-kernel wiring

- Select the corrected reducer through
  `CorrectedAquaticSeasonalPressureEnabled`.
- Require that gate when `NeolithicFishingEnabled` is true.
- Keep the legacy reducer for feature-off/legacy worlds so the resident Fishing
  change does not silently rewrite existing saves.
- Use the selected reducer in the same `EcologicalPressure.Integrate` path for
  forecasts and authoritative resolution.

#### WP0.4 Acceptance

- Seasonal realized amounts sum to the horizon receipt.
- Cold condition damage is monotone in realized harvest and impact multiplier.
- Zero harvest produces zero use damage.
- Splitting or joining equivalent turn durations stays within the declared
  analytical tolerance.
- Warm-water calibration does not regress outside an explicitly approved
  correction delta.
- Serial and parallel results and fingerprints are identical.

### WP1 — Configuration, compatibility, and capability

- Add `NeolithicFishingEnabled` and the ecology-correction gate to
  `SimulationConfig`, defaults, opening JSON, validation, and deserialization.
- Make absent old-save fields false.
- Reject Fishing enabled with Legacy economic allocation or without the
  corrected aquatic-pressure gate.
- Define one capability helper for the goods economy:
  Cultivation, Husbandry, or feature-enabled Watercraft.
- Define a separate resident-Fishing eligibility helper: feature-enabled
  Watercraft plus a legal source.
- Suppress only the Watercraft opening `FisherySeed` in feature-on new worlds.
  Preserve the old seed and food multiplier in feature-off worlds.

### WP2 — Workforce, goods, and persistence plumbing

- Add `Fisher` to the workforce role registry, validation, copying, UI, and
  observations.
- Append `FishingTools` to `CraftGood`; do not renumber existing values.
- Add Fishing Tool inventory and priority fields with optional-field migration.
- Add the Carpenter recipe: one Wood, one Stone, equipment service, and the
  generalist productivity path.
- Make Fishing Tools non-tradable in this slice but portable across fission.
- Extend production demand, forecasting, production receipts, storage,
  invariants, fingerprints, and diagnostic paths.
- Replace default-to-Housing good mappings with exhaustive switches so a new
  enum value cannot silently read or write Housing.
- Add sparse fishery state and knowledge snapshots to save copies,
  normalization, migration, and deterministic fingerprints without changing
  the dense tile record.

### WP3 — Access, knowledge, and ground selection

- From the accepted camp destination, require traversable land adjacent to
  known Shallows or Lake.
- Run bounded multi-source BFS separately for Shallows-only and Lake-only
  routes. Adjacent water has distance one.
- Use range three and `D(d) = 0.75^(d-1)` before Sailing.
- Cache one BFS result per camp/destination and reuse it throughout Prepare.
- Rank grounds by repeatable food per Fisher-year after distance, opening stock,
  rivals, and prospective closing capacity with `sigma = 0.20`.
- Select at most three grounds. Transit tiles consume no slot.
- Retain incumbents within `GroundSwitchMargin = 0.025`; otherwise reselect at
  the next Prepare boundary.
- Use live current ecology only when visible. Use own visited snapshots or the
  declared conservative prior for remembered geometry outside vision.
- Use fixed-count bands/bisection; do not enumerate ground triplets.

### WP4 — Single-build preparation and DemandControl

Refactor ownership so `CampProductionDemand.BuildPhysical` runs once per camp:

- Automatic planning returns or retains its built physical demand object.
- An explicit/manual Economy plan receives one physical build after order
  acceptance.
- Both paths derive the final immutable `FishingPlan` from the same cached
  object after placement and Fisher staffing are fixed.
- Draft reuse keys include accepted location and every fishing input that can
  change the envelope.

Extend physical demand with:

- selected Fishing grounds and marginal catch envelope;
- Fishing-only Plant/Aquatic food separation;
- mixed gathering-versus-fishing survival allocation;
- Fishing Tool Continuity demand after Fisher staffing;
- the common resident budget:
  `residents - herders - farmers - fishers - foodGatherers`;
- Cloth and fixed-Housing eligibility for Fishing-only economies;
- agriculture-only enforcement for Farm Tools.

Fishers replace gathering labor on the shared survival frontier; they are not
added after a full gatherer allocation. `AquaticFood` remains consumption stock
and never becomes Cloth or another recipe feedstock.

### WP5 — Fishing work and capital planning

For each selected ground:

- Apply distance to harvest and effective on-ground work, never to biology.
- Convert assigned workers to Fisher-years over the prepared horizon.
- Spend Fishing Tool service only on the harvest share and apply the 1.5x
  harvest multiplier to covered harvest work; one unit covers 20
  harvest-Fisher-years.
- Compute opening maintenance work from opening `E`.
- Allocate effective work maintenance-first.
- Decay opening capital under the resulting operation fraction.
- Add remaining work through the common effort/capital transform.
- Use mean decayed capital plus half the built endpoint delta as the fixed mean
  capital for the resource solve.

The same Fisher assignment supplies harvest, maintenance, and construction.
Realized or requested harvest does not subtract Fisher-years from the
maintenance/build budget. There is no independent fishery-development request
or labor pool.

### WP6 — Operators, claims, and one tile integration

- Reconcile one maintaining operator per site before parallel ecology work.
- Preserve a prior operator while it remains reachable and supplies positive
  work; otherwise assign the lowest eligible camp ID.
- A losing camp may keep its legal shared-stock harvest but receives no
  management credit and is not replanned.
- A departing camp releases its prior sites and cannot acquire management in
  the departure turn.

Add `FisheryHarvest` as a distinct claim kind. Do not reuse `Fishing`, which is
currently Hunter/Supply behavior.

Before integration:

- combine resident, Hunter, and passive aquatic claims against one finite tile
  pool;
- prevent passive gathering from independently promising aquatic food already
  allocated to resident Fishing;
- add selected grounds to the active-tile set;
- pass average/end fishery capital and operation as an explicit aquatic overlay
  to forecast and integration.

The aquatic overlay is:

- accessible food `G = G0 + (B - G0) q^2`;
- managed flow
  `M = CultivationRegenFlux * q * B * r * operation`, using coefficient `0.65`;
- no increase to biomass ceiling `B`;
- no land vegetation, moisture, or natural-recovery multiplier.

Commit ecological endpoints, receipts, Fishing Tool use, typed Aquatic Food,
fishery capital, operators, pressure records, and balances in canonical tile
and camp order.

### WP7 — Villages, departure, fission, and immigration

- Replace the agriculture-only village boundary update with one combined
  qualification pass so `VillageTurns` changes at most once.
- Preserve existing agricultural qualification.
- For Fishing, calculate maturity over worked Shallows/Lake grounds and managed
  meal support from prepared/realized fishing terms.
- Require both thresholds to reach `0.5`; latch village status until departure.
- Exclude Ocean from capital and Fishing village qualification.
- On accepted departure, revert the village and release old fishing operators;
  do not qualify a new village in the same turn.
- Split portable Fishing Tools and applicable remembered knowledge on fission;
  do not move physical fishery capital.
- Keep Fishing Tools out of immigration transfers.

### WP8 — Close-boundary knowledge, reporting, and practice

- At Close, write owner-only snapshots for visited Fishing grounds: endpoint
  accessible food, biomass, condition, productive profile, physical capital,
  operator, and observation day.
- Prune snapshots with explored-memory expiry.
- Expose only policy-safe, appropriately banded information.
- Add Fishing capability, Fisher workforce, Fishing Tool stock, selected/known
  grounds, fishery capital/operation, and village support to observations and
  presentation.
- Bump the observation schema and update the JSON DTO contract.
- Include `FisheryHarvest` in demographic fish-food totals and conservation
  reports.
- Credit resident Fishing practice according to the decision in section 3,
  without double-crediting passive or Hunter receipts.

### WP9 — Documentation and rollout

- Update `Docs/TurnResolutionPasses.md` with preparation, operator arbitration,
  the aquatic overlay, the corrected seasonal pressure reducer, and Close
  knowledge writes.
- Update `Docs/CurrentSimulationContract.md` and generated config tables.
- Supersede the deferred resident-economy statements in
  `Docs/Issue32FishingCapability.md` without rewriting its historical evidence.
- Document every new save/config default and observation schema change.
- Enable the feature only in a named validation profile until all acceptance
  gates below pass.

### WP10 — Final independent Astra code review

After implementation, documentation, and every required test run are complete,
spawn a dedicated reviewer subagent using `gpt-6-astra`. The reviewer is an
independent code reviewer, not an implementation agent, and receives:

- the source Fishing contract and this implementation plan;
- baseline commit `86cba712dfd76673bd9637052543bde85f810e0b`;
- the complete implementation diff;
- focused and full-suite test results;
- both 50-turn headless artifacts and regression-gate outcomes; and
- performance, save/fingerprint, observation-schema, and documentation-check
  results.

The Astra review must examine correctness and contract compliance, with special
attention to:

- feature-off and old-save compatibility;
- the corrected seasonal aquatic-pressure reducer;
- one integration per active tile and conservation across overlapping claims;
- operator arbitration and deterministic parallel/serial behavior;
- DemandControl labor accounting and the single physical-demand build;
- hidden-information boundaries in planning, knowledge, and observations;
- sparse-state persistence, migration, normalization, and fingerprints;
- enum/default fallthroughs, trade exclusions, fission, and immigration;
- asymptotic cost and allocation regressions; and
- whether the tests can pass without exercising the intended Fishing behavior.

Record every finding with severity and file/line evidence. Resolve all blocking
and correctness findings, rerun every affected focused suite plus both 50-turn
tests when authoritative behavior changes, and give the reviewer the resulting
diff and evidence for a final verification pass. The slice cannot meet its
definition of done while an unresolved Astra blocking or correctness finding
remains.

## 6. Test plan

Register every new suite in the headless project and command dispatcher.

### 6.1 Ecology correction

- Reproduces the old cold-season failure.
- Seasonal allocation conserves the realized horizon receipt.
- Finite seasonal ceilings are respected.
- Damage is monotone and finite across zero/low/high safe rates.
- Corrected forecasts and resolution are identical for the same claims.
- Compatibility gate off preserves legacy fixtures and fingerprints.

### 6.2 Capital and access

- Effort/capital functions are inverse and monotone.
- Decay and maintenance special cases, including `E0 = 0`, are exact.
- Distance one is undiminished; range and water-kind routing are exact.
- Three-ground cap, incumbent margin, tie-breaking, and no-triplet behavior are
  deterministic.
- Hidden live ecology cannot affect a policy plan outside vision.

### 6.3 Economy and production

- Watercraft-only Fishing economy is legal only under the feature gate.
- Legacy allocation plus Fishing is rejected.
- Mixed Agriculture/Husbandry/Fishing respects the resident budget.
- Manual and automatic Fishers receive correct tool demand and catch envelopes.
- Fishing Tools affect harvest only, consume the correct materials/service, do
  not trade, split on fission, and do not migrate.
- Cloth and fixed Housing work for Fishing-only camps; Farm Tools remain
  agriculture-only.

### 6.4 Resource and operator behavior

- Resident, passive, and Hunter fishing share one finite pool.
- Every active aquatic tile integrates exactly once.
- Operator retention, release, contest ties, and departure are canonical.
- Losing operators receive no management credit and are not replanned.
- Managed flow changes recovery but never raises biomass capacity.
- Serial and parallel resolution produce identical records and fingerprints.

### 6.5 Persistence and observations

- Feature-off old saves load unchanged.
- Sparse sites, last-selected grounds, inventories, priorities, and knowledge
  round-trip and fingerprint deterministically.
- Invalid or duplicate sparse sites are rejected.
- Observation filtering, schema serialization, and memory pruning are exact.

### 6.6 Calibration and performance

- Reproduce the source contract's 14 compact warm worlds and 221 exact-one-site
  cases before changing calibration constants.
- Add cold-water calibration only after WP0 passes.
- Measure Prepare time, active integrations, allocations, save size, and
  mature-world throughput at the existing scale checkpoints.
- Confirm access work scales with camps times bounded route area, selection with
  bounded candidates, and ecology with active tiles rather than map size.

### 6.7 Post-implementation 50-turn headless runs

After every implementation work package is complete, run exactly two registered
50-turn headless scenarios from the same fixed seed and byte-identical
post-opening checkpoint. Build that checkpoint with Fishing and Trade enabled,
Fission and Immigration disabled, and advance through `WorldState.Finished`
before saving it. The measured horizon is 50 additional committed turns; do not
reset or mutate `Finished`. The only scenario difference is the stated feature
toggles. Trade remains enabled in both.

1. **Fishing long run — trade, no population movement**
   - `TradeEnabled = true`
   - `FissionEnabled = false`
   - `ImmigrationEnabled = false`
2. **Fishing long run — all social features**
   - `TradeEnabled = true`
   - `FissionEnabled = true`
   - `ImmigrationEnabled = true`

Register these as two distinct headless tests, not modes hidden inside one test.
Suggested names are `--fishing-50-no-movement` and
`--fishing-50-all-features`, backed by separate test methods so either failure
is independently visible in CI.

Run both scenarios over the same canonical Standard/80 five-world census used
to produce the historical population anchors: Continents seeds 1, 2 and 3,
Pangaea seed 1, and Archipelagos seed 1. Complete each opening first, then
measure exactly 50 post-opening committed turns in every world. Pool the five
worlds at each measured boundary. A single seed is diagnostic evidence, not a
valid comparison to the pooled historical anchors.

At every committed turn boundary, classify each live camp by its Society's
first Neolithic branch:

- Agriculture/Cultivation;
- Animal Husbandry;
- Watercraft/Fishing.

Fission daughters retain their lineage's first-branch classification. For each
branch, record the live-camp sample count and the following distributions for
resident camp population and committed camp QoL:

- median;
- p25;
- p75;
- minimum;
- maximum.

Use the repository's shared deterministic quantile convention, or add one
documented quantile helper used by both tests. Do not allow runtime/library
quantile behavior to determine interpolation. Record an empty branch as count
zero with absent statistics, not fabricated zero-valued population or QoL.

Each test must emit a machine-readable per-turn artifact and a final turn-50
summary containing the scenario configuration, every world seed/shape and
checkpoint identity, branch, count, and all ten requested statistics. The two
artifacts must use the same schema so they can be compared directly.

Use these historical turn-50 median resident-population targets as regression
anchors:

- Agriculture/Cultivation: `480`;
- Animal Husbandry: `383`.

Those anchors were measured with fission, immigration, and trade enabled. The
all-features scenario therefore applies the like-for-like hard gate at no more
than a 20 percent reduction from its target:

- Agriculture/Cultivation must be at least `384`;
- Animal Husbandry must be at least `306.4`.

The no-fission/no-immigration scenario is not the cohort that produced 480/383:
the pre-feature system itself is below those absolute medians, especially for
Husbandry. It must instead run a matched feature-off five-world reference and
gate Agriculture and Husbandry at no more than a 20 percent reduction from
those reference medians. This preserves the user's regression tolerance without
pretending unlike population-process configurations are comparable.

Apply both gates to the same live-camp, first-Neolithic-branch classification
used for the reported distributions. Do not round an observed median before
the comparison. Watercraft/Fishing has no historical population floor in this
slice; its full population and QoL distribution is still recorded for future
baselines.

A failure is a regression signal, not permission to retune the threshold or
Fishing yields. Attribute it across the economic planner, ecology receipts and
condition, trade flows, and demographic/lifecycle effects. Any intentional
change to either historical target or its 20 percent tolerance requires a
separate reviewed contract update with matched evidence.

Both tests must also assert:

- all 50 turns complete without rejected internal plans, conservation failures,
  invariant failures, or non-finite state;
- every non-empty branch has finite population and QoL statistics with
  `min <= p25 <= median <= p75 <= max`;
- population is nonnegative and QoL remains within its authoritative bounds;
- the all-features turn-50 Agriculture and Husbandry median populations meet
  their respective `384` and `306.4` historical regression floors, while the
  no-movement scenario stays within 20 percent of its matched feature-off
  medians;
- the recorded turn-50 values reproduce exactly when rerun with the same seed
  and execution mode; and
- the all-features run records any fission and immigration activity rather than
  silently exercising an inactive configuration.

These are final system tests, not calibration inputs. Initial result artifacts
become reviewed baselines only after the implementation and ecology correction
have passed their focused suites.

## 7. Delivery sequence and gates

1. Land WP0 and its compatibility gate with no resident Fishing state enabled.
2. Land WP1-WP3: configuration, sparse state, goods, knowledge, and pure access
   planning.
3. Land WP4-WP5: single-build DemandControl integration and immutable fishing
   work/capital plans.
4. Land WP6: shared claims, operator arbitration, aquatic overlay, and canonical
   commits.
5. Land WP7-WP8: villages, lifecycle, observations, reporting, and practice.
6. Run warm exact-case calibration and all fixed suites.
7. Run corrected cold-water validation, scale tests, save/fingerprint tests,
   and documentation checks.
8. Run both registered 50-turn headless scenarios and review their per-branch
   population and QoL distribution artifacts.
9. Run the independent `gpt-6-astra` reviewer subagent, resolve its findings,
   rerun affected validation, and obtain its final verification.
10. Enable the named Fishing validation profile.
11. Make a separate reviewed decision before enabling Fishing in the general
   production opening.

## 8. Definition of done

The first slice is complete when:

- feature-off results and old saves retain their documented compatibility;
- the corrected aquatic seasonal-pressure path passes warm and cold validation;
- a Watercraft-only camp can automatically or manually sustain resident Fishing
  through the shared economy;
- all aquatic users share one conserved stock integration per tile;
- sparse capital, operators, tools, knowledge, villages, and lifecycle behavior
  round-trip deterministically;
- filtered observations contain no hidden live ecology;
- the warm calibration fixtures, full fixed headless suites, documentation
  checker, and scale budgets pass;
- both 50-turn headless scenarios pass and report median, p25, p75, minimum, and
  maximum population and QoL for every non-empty first-technology branch;
- the all-features scenario keeps turn-50 median resident population at or
  above `384` for Agriculture and `306.4` for Husbandry, and the no-movement
  scenario stays within 20 percent of its matched feature-off medians;
- the final Astra reviewer has no unresolved blocking or correctness findings;
  and
- Sailing and Ocean development remain disabled and unaffected.

## 9. Implementation validation record (5 October 2026)

The final registered tests use the canonical pooled Standard/80 census and
completed 50 post-opening turns per world with exact replay. Values below are
`minimum / p25 / median / p75 / maximum` at measured turn 50.

| Scenario | Branch (n) | Resident population | QoL |
| --- | --- | --- | --- |
| Trade; no fission or immigration | Agriculture (273) | 4 / 90 / 368 / 819 / 1004 | 0 / 0.728465 / 0.745731 / 0.907239 / 0.999735 |
| Trade; no fission or immigration | Husbandry (113) | 3 / 27 / 38 / 389 / 827 | 0 / 0.000324 / 0.001938 / 0.365101 / 0.964936 |
| Trade; no fission or immigration | Fishing (14) | 64 / 104.25 / 295 / 526 / 1000 | 0.398146 / 0.469193 / 0.795298 / 0.953154 / 0.984874 |
| Trade, fission, and immigration | Agriculture (941) | 4 / 250 / 476 / 708 / 1073 | 0 / 0.744024 / 0.845121 / 0.926605 / 0.999735 |
| Trade, fission, and immigration | Husbandry (1647) | 3 / 211 / 388 / 608.5 / 842 | 0 / 0.755944 / 0.799310 / 0.912982 / 0.990969 |
| Trade, fission, and immigration | Fishing (30) | 37 / 256 / 367 / 548.75 / 1007 | 0 / 0.646043 / 0.818648 / 0.916000 / 0.984874 |

The no-movement feature-off reference medians were Agriculture `362` and
Husbandry `38`; the Fishing candidate produced `368` and `38`, passing the
matched 20 percent regression gate. The all-features candidate produced
Agriculture `476` and Husbandry `388`, passing the historical `384` and `306.4`
floors, with 2,218 fissions and 646 immigrations. Both artifacts passed final
whole-world invariants and reproduced every per-turn statistic and final world
fingerprint exactly. Raw artifacts are written to the ignored paths
`TestResults/Fishing50/no-population-movement.json` and
`TestResults/Fishing50/all-social-features.json`.

The no-movement run recorded 34,430 Fisher person-turns, 2,603 active fishery
site-turns, 7,287,327.63 harvested Food, 602,250.70 tool-covered Fisher-years,
and 526,573.71 fishery-capital worker-years. The all-features run recorded
53,665 Fisher person-turns, 4,697 active fishery site-turns, 11,440,454.01
harvested Food, 903,597.94 tool-covered Fisher-years, and 840,214.02
fishery-capital worker-years. These positive-activity gates prevent a run from
passing solely because Fishing code is present but unused.

The locked warm calibration regenerated all 14 specified worlds and their
5,996 candidate sites: 5,481 Shallows and 184 Lake sites with three or more
water routes, 100 Lake and 5 Shallows sites with exactly two routes, and 190
Lake and 31 Shallows sites with exactly one route. It reproduced the required
221 exact-one-route sites, observed no more than 23 candidates for any site,
and passed 36,892 assertions. The isolated diagnostic took about 6.2 seconds and
allocated about 643 MB including compact-world generation. A fully populated
sparse fishery site plus knowledge record increased the encoded save by 183
bytes, below the 4 KiB record budget.

The eligibility scale guard performed 1,000 unrelated Hand Tools recipe checks
with 1,280 camps in 40 allocated bytes, proving that those reads do not rebuild
Fishing access indexes. The sparse-resource scale guard resolved a world with
128 dormant sites using three sorted point lookups and seven active ecological
integrations; every dormant site's capital, timestamp, and operator remained
untouched. Together with the bounded 23-candidate maximum and the Standard/80
50-turn runs, these checks cover camp scaling, site sparsity, active-tile
integration, and mature Prepare/Resolve use.

Validation also passed the full fixed headless suite, the 55-assertion
foundation suite, the complete economy suite, the documentation checker (81
configuration/API values and 19 test claims), and focused suites for aquatic
seasonal pressure (53), Fishing observations (13), economy/preparation (47),
resource/lifecycle behavior (43), state/save behavior (23), DemandControl
(146), Trade (480), contact knowledge (136), fission (632), and immigration
(337). The build has no errors; its two `CS0649` warnings predate this slice.

The final independent Astra review signed off with no remaining blocking or
correctness findings after verifying the six corrected defects and the bounded
lookup rewrite. It recorded three non-blocking evidence limits: no exhaustive
paired RL-scale timing budget, no cross-build feature-off fingerprint proof,
and no claim that the warm geographic census reproduces the source experiment's
economic outcomes. Same-build feature-off, old-save, deterministic replay,
sparse-scaling, population, and activity gates remain green.
