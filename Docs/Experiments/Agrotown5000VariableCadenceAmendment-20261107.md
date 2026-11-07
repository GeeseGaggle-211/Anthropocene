# Agro-town 5,000 plan amendment: progressively shorter player turns

Revision 1.1 — 7 November 2026. This is a documentation-only amendment to `Agrotown5000SourceAudit-20261107.md` and the conversation's `Anthropocene_Agrotown_5000_Investigation.md`. It supersedes recommendations to retain twenty-year decisions indefinitely or to require a harvest-kernel replacement before testing the existing model. Historical experiment results retain their original scope. No production code or active training changes are authorized or made by this amendment.

## 1. Controlling requirement

Player-turn duration delta_t will gradually decrease from twenty years as the game advances. The exact era-to-duration schedule and transition triggers are not yet specified. The diagnostic ladder 20 -> 10 -> 5 -> 2 -> 1 years is a test matrix, not an approved era calendar.

The goal remains economically and demographically viable agricultural towns around 5,000 residents in the intended advanced regime, followed by larger cities. Every primitive settlement, every site and every cadence need not support 5,000.

**Revised sequence: calendar/boundary safety -> native variable-cadence baseline -> explicit harvest-contract selection -> frontier/services/organization -> city expansion.** Do not simultaneously grant shorter-turn harvest benefits and a large yield multiplier justified by the obsolete twenty-year-only ceiling.

## 2. Evidence already available

The completed native audit at production SHA `07f3501c2d5e047e44d61e85cc0d4613b54572d6` used the same seven mature fertile fields at actual q=.40:

| Native interval | Median meal-support capacity | Capacity divided by 1.25 |
|---|---:|---:|
| 20 years | 1,189.30 | 951.44 |
| 5 years | 4,757.22 | 3,805.77 |
| 1 year | 6,436.32 | 5,149.05 |

The headroom column is arithmetic using the previously proposed 25% spare-capacity margin, not a new demographic simulation. The native cases are ecological ceilings with labor evaluated afterward. They are not an equal-fixed-workforce policy comparison, full goods economy or an in-save changing-cadence campaign.

Thus shorter native intervals already provide a numerical food route toward 5,000 without increasing q or biomass. At one year, the median .40 case clears the proposed 6,250 food-equivalent capacity target, but narrowly once omitted obligations are considered. This does not establish every site or the entire economy as viable.

No new dynamic-cadence simulation has been executed for this amendment. The original experiment is `f60aa0106f3311f99660b0abbd007d27f3bd22f0`, Actions run 42367918047. Its measurement files remain unchanged.

## 3. Three different consistency requirements

### Numerical subdivision of one locked plan

Hold start state, physical labor rates, seasonal phase, controls and fixed forcing constant. Subdivision must not create extra policy choices, resource claims or opening-stock receipts. Compare cumulative receipts and endpoint stocks. General evolving-capital and coupled-economic approximations need declared tolerances/refinement convergence, not unsupported bitwise-equality promises.

### Genuinely shorter decisions

More staffing, investment, movement and trade decisions can change outcomes. New equipment, discoveries and residents become usable at additional boundaries. Exact physical-year trajectories need not be identical. Attribute these changes rather than requiring equality across different policies/opportunities.

### A shortening-era campaign

Technology, actual q, services and decision duration may change together. Evaluate the target at the intended regime. First cross these variables independently so cadence benefits are not incorrectly attributed to Plough, Irrigation or q_k.

The existing native pulse grants more harvest opportunities per year when player turns shorten. That can be retained as an explicitly accepted gameplay coupling; it is not automatically a biological technology advance.

## 4. Conditional harvest decision

**Lane A: native pulse.** Run the full economy under the shortening schedule. Preserve conserved finite stock and paid labor. Retain this rule only after explicitly deciding the issue-29 temporal contract, testing subannual/phase-sensitive behavior and confirming the desired progression. Do not use an unlimited-labor ecological ceiling as a prediction of staffed output.

**Lane B: bounded recurring harvest within a locked horizon.** Decouple physical production recurrence from the number of player decisions. Use one analytical shared-stock transition with one canonical commit; seasonal forcing is not an authoritative economic tick. The existing restricted flow prototype is a candidate, not a finished replacement: contested allocation, exhaustion, useful-demand limits, partial horizons, cumulative receipts and evolving capital still need validation.

Select B if shorter intervals are intended only as decision refinement or if A fails the approved temporal/economic contract. Selecting A is also possible if its cadence coupling is consciously accepted and validated. Reaching 5,000 at one year does not establish timestep invariance; it also does not justify an immediate broad harvest rewrite.

## 5. Verified transition hazard and required boundary

Main was rechecked and remains `07f3501c2d5e047e44d61e85cc0d4613b54572d6`.

`TurnHorizon.Next` reads `Config.TurnDays` and derives physical years, reference intervals and practice scale. `TurnResolver` records each transition's start/duration and rejects a prepared horizon inconsistent with the current duration.

**Source-level hazard:** `PlanningDraft.Matches` checks world identity, turn number and elapsed days, but not duration. `Prepare` can reuse that draft's trade plan and economic intentions. Changing `TurnDays` after drafting can therefore retain a plan built for the old horizon. The post-Prepare resolver guard does not remedy stale draft reuse inside Prepare. This is a source finding, not a newly executed runtime reproducer.

Required order:

1. Finish the current turn with its locked duration.
2. Choose and persist the next shared duration at a clean boundary, before Draft/Prepare.
3. Invalidate horizon-dependent drafts, forecasts, trade reservations and observations when duration changes.
4. Include duration and other approved mutable inputs in validity stamps.
5. Freeze that horizon until commit; preserve it in history, saves, replay and training transitions.

Use cumulative `World.Days`, not turn number multiplied by the latest duration. All societies in the shared world resolve the same interval in this first version; this amendment does not introduce asynchronous per-society clocks.

Do not change `BaseDays`, `DaysPerYear`, `PracticeDays`, or `Neolithic.ReferenceTurnYears` merely because actual turns become shorter. These retain the physical meaning of rates, service lives and calibration.

## 6. Classify duration-sensitive rules

Physical-year labor, food/clothing demand, capital work and decay, tool service and reserve horizons need transition tests even where duration-aware helpers already exist. Newborn labor, new tools and newly discovered technologies still become available at decision boundaries; their changing activation lag is not numerical subdivision.

Immigration `MaxGroup`/`Size` uses a fraction per turn and `ReturnPenalty` ages in turns. Field domestication counts consecutive contact turns. Movement, fission, memory expiry, trade opportunities and turn-keyed randomness also need classification. For example, six contact turns represent 120 years at the old cadence but only six years at annual cadence.

Distinguish elapsed-time processes from genuine decision-count limits. Do not convert every counter blindly. For a selected physical-rate interpretation, `f(dt)=1-(1-f0)^(dt/T0)` is one reference-preserving fractional-flow conversion; it is a proposal, not authorization to rewrite migration. Trace PhysicalMortalityV1 callers before applying a helper-level change. Preserve old-save timer semantics explicitly.

Require exact replay for the same schedule. Across schedules, use controlled randomness or matched distributions instead of assuming turn-keyed random draws remain identical.

Mortality and cohesion work remain necessary. The measured fully supplied 5,000-person state has net annual rate about -.17311 and cohesion about .000381. Shortening dt does not change those instantaneous population/service functions. More frequent service/economic decisions may change coverage, but cannot be assumed to remove the underlying scale constraint.

For RL, record the applied interval and intended physical-time reward/discount convention when a new simulator version is adopted. Do not modify the running training environment. A fixed discount per decision must not accidentally change the intended physical planning horizon.

## 7. Revised acceptance gates

### Gate 0: calendar safety

Specify the global cadence interface, schedule version, deterministic replay and save/load. Add runtime witnesses for duration changes after Draft and after Prepare. No cadence switch may rescale or reset inventory, capital, experience, cohort state or existing ecological timestamps.

### Gate A: native schedule baseline, then contract selection

Compare fixed 20/10/5/2/1-year cases and decreasing schedules over equal elapsed time. Use the same mature field capital and initial inventories. Test fixed staffing/rates before permitting replanning. Include partial-year and season-phase tests (e.g. .25 years and seven days) before later-era claims; these are not an approved modern-era schedule.

Keep three attribution lanes: unmodified native pulse; cadence-plumbing/timer-normalized pulse; normalized candidate flow. Cross actual q=.4/.5/.6 and q_k=.4/.52/.62 separately, including q=0 controls. Make harvest replacement conditional on measured results and the explicit temporal contract.

### Gate B: frontier and services

Reuse the existing settlement-local q_k design after integration review. Preserve physical capital and q0. Fund real tools, nonfood work, settlement services and organization. Propagate one service envelope to physical demography, fast planning, welfare and social counterfactuals. Calendar shortening alone is not a mortality or cohesion fix.

### Gate C: a live self-sufficient town

Replace '40 twenty-year turns' with **at least 800 simulated years of mature-operation evidence**, compared in equivalent elapsed windows under each schedule. This preserves the previous diagnostic duration; it is not a historical town-lifetime claim. Separately run an establishment trajectory from the same earlier state.

Require no routine net food imports, no continuing injected supplies, no systematic reserve depletion, actual upkeep/replacement, viable demographic/welfare outcomes and separately checked local water/storage/materials. Express capacity in food per year and stocks as years of coverage. The provisional margin remains 25,000 usable food/year capacity for 5,000 residents at current consumption, not mandatory excess consumption or stockpiling.

Measure time to population milestones, person-years, gross births/deaths, annual food, field capital, tools, migration/fission and net imports. Do not compare at equal turn number. Enable social relocation for a separate gate and do not suppress fission to force the target.

### Gate D: cities and computational scale

Only after calibration expand paid catchments and conserved surplus deliveries. Keep source needs/reserves protected and residential services separate from farmland. Measure cost per decision and per simulated year: shorter turns inherently cause more planning/inference calls over a fixed historical span. Each transition must still have bounded duration-independent analytical work, local/sparse state and no hidden annual world loop.

## Sources and evidence boundary

At pinned main: `TurnHorizon.cs:9-36`; `TurnPlanning.cs:175-188,248-269`; `TurnResolver.cs:62-75`; `Immigration.cs:94-121`; `FieldDomestication.cs:101-137`; `NeolithicProduction.cs:395-399,769-777,1103-1117`. Static measurements and exclusions remain in `Agrotown5000SourceAudit-20261107.md`.

This amendment uses completed static diagnostics, recomputed CSV medians and source inspection. No new dynamic-schedule campaign, full live economy, trained-policy evaluation or production patch is claimed.
