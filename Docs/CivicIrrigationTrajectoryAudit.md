# Civic irrigation and population trajectory audit

**Date:** 28 November 2026. **Baseline:** PR #51, `319d03b1719ee49bc7573fd119cb174c920dd074`.

The 1,000-year population miss is real, but the earlier claim that seven tiles cannot support 5,000 residents is not supported. All five full-treatment worlds cross 5,000 when continued normally. Crucially, most of that later growth disappears when turn duration stops shortening. The land harvest/recovery boundary is a demonstrated source of this dependence.

Irrigation is used, although new adoption is sparse. The largest cities lack new local opportunities under the current rules. Construction funding and delivery work for the projects actually requested. Removing the cultivation gate or granting physically feasible canals does not improve the largest-city outcomes in these matched experiments.

## Method and evidence

- Built an untouched copy of the pinned simulation and reproduced all 20 original 1,000-year arms: five seeds, `off/phase1/full/full-3a`. The published peak and population totals reproduce.
- Extended `off`, `phase1`, and `full` to the first boundary at or beyond 2,000 physical years from the same checkpoints. No population, workforce, land, technology or demographic tuning changed in these arms.
- Captured each committed boundary, every village ID, population and demographic ledger. Slopes are ordinary least-squares residents per century using actual elapsed years: approximately 800–1,003 and the last 200 years of each continuation. Track the same village that was largest at year 1,000 as well as the changing maximum.
- Traced the actual prepared DemandControl allocation and separately queried irrigation opportunities, including opportunities hidden behind the fertile-tile gate. Diagnostic counters do not enter saves, observations or orders.
- Ran four matched interventions at the first boundary beyond year 1,000: remove the fertile-tile gate; prebuild positive-scoring projects; prebuild physically watered projects including zero next-boundary food gain; freeze the already scheduled turn duration. The positive-score prebuild installed nothing and is retained as an explicitly ineffective intervention, not evidence that all potential infrastructure is useless.
- Additionally removed the gate from the initial checkpoint in seeds 101/1729, the only seeds with positive-return opportunities blocked by that gate.
- **35/35 late-intervention/control trajectories exactly match the untouched binary's fingerprint at year 1,000.** The two early gate interventions intentionally diverge earlier. **37/37 extended runs pass final invariants and live-versus-reloaded next-turn fingerprint checks.**
- Fresh output directories, checkpoint hashes, simulation-source hashes, executable hashes and profile hashes identify each run. Raw saves and per-turn JSONL are under local `TestResults/CivicAudit/`. Compact evidence is retained in [the summary](Experiments/CivicIrrigationTrajectory-20261128-summary.json), [trajectory CSV](Experiments/CivicIrrigationTrajectory-20261128-trajectories.csv), and [provenance manifest](Experiments/CivicIrrigationTrajectory-20261128-manifest.json).

This is five compact 96×48, four-founder worlds using one profile. The common history still includes the original Plough/Irrigation knowledge grant. These results do not substitute for an 80-founder benchmark, a fully endogenous technology opening, or Unity validation. Concurrent run timings are not performance benchmarks.

## Population: positive near the original endpoint, with an important calendar dependency

| Seed | Largest village at ~1,000 years | Growth near 1,000, residents/century | First crossing of 5,000, year | Largest at ~2,000, normal calendar | Largest at ~2,000, duration frozen after 1,000 |
| --- | ---: | ---: | ---: | ---: | ---: |
| 11 | 4,236 | +294 | 1,246 | 7,939 | 4,468 |
| 23 | 3,878 | +246 | 1,333 | 7,389 | 3,982 |
| 47 | 4,425 | +290 | 1,211 | 7,485 | 4,382 |
| 101 | 4,363 | +265 | 1,253 | 8,130 | 4,222 |
| 1729 | 3,988 | +316 | 1,359 | 6,937 | 4,080 |

At the original endpoint, **both the largest-village and world-population trends are positive on all five seeds**. The same year-1,000 leader also has a positive trend in every seed, so the result is not just turnover in the identity of the largest settlement.

Normal continuation increases total world population from **243,611 to 391,077**, with **28 villages at or above 5,000**. Every final largest village still has seven farmed tiles and zero irrigated fields. Accounting appears organically in 14 villages by year 2,000; it was present in two at year 1,000. This co-evolution is not an isolated estimate of accounting's effect.

The final 200-year window is more mixed for the changing maximum:

| Seed | Largest-village trend near 2,000 | World-population trend near 2,000 | Same year-1,000 leader's trend near 2,000 |
| --- | ---: | ---: | ---: |
| 11 | +310 | +4,328 | +124 |
| 23 | +961 | +2,038 | +961 |
| 47 | −259 | +933 | +330 |
| 101 | +1,188 | +4,693 | +1,196 |
| 1729 | +130 | +917 | +130 |

All values are residents per century. Seed 47 peaks at **8,450 around year 1,850**, then falls to 7,485; its world population continues growing. Do not describe every largest village as monotonically increasing or the endpoint as an established equilibrium.

The civic-off controls finish with largest villages of 1,000 on every seed. Crowding-only controls finish between 3,200 and 3,698. Sanitation remains necessary for the larger-city outcomes in this experiment.

## The decisive control: physical time versus harvest opportunities

At year 1,000, the next scheduled turn is about **7.904 years**. Normal smoothing reduces it to about **4.435 years** by year 2,000. The fixed-duration diagnostic preserves the already scheduled horizon and disables only subsequent calendar compression; all economic and demographic coefficients remain unchanged.

With duration held fixed, **none of the five worlds reaches 5,000**, and total population ends at **241,331**, close to the original 243,611. This is evidence that further calendar compression is important to the later growth, not proof that every contribution comes from one subsystem.

A separate fixed-claim experiment isolates the land kernel from the Elder, demography, capital development and migration. It uses the year-1,000 leader's existing footprint, fixed cultivation capital and operating fraction, identical seasonal forcing and elapsed time, and the same total food demand/rate. Only the number of harvest/recovery boundaries changes. For seed 11, over the same 7.904 years:

| Harvest/recovery boundaries | Food delivered | Ending field stock |
| --- | ---: | ---: |
| 1 | 135,712 | 135,208 |
| 2 | 250,182 | 114,469 |
| 4 | 331,876 | 65,387 |

The other four seeds show the same qualitative effect; exact ledgers are in the summary evidence. Each variant conserves opening stock + recovery − harvest − overflow = ending stock, with residuals below 1e-6 food units. These are fixed-claim kernel probes, not labor-feasible gameplay arms or a recommended annual-substep implementation.

The native full-planner probe from the same seed-11 save also changes materially: one 7.904-year turn yields 135,712 agricultural food and ends the tracked village at 4,242; two half-duration turns yield 249,947 and end it at 4,578. Four quarters yield 290,305 and 4,444, showing that the complete economy's response need not be monotone.

The source explains the result:

- `EcologicalPressure.Integrate` routes land through `ResolveLandOpeningFlow`.
- `ResolveLandOpeningFlow` caps human receipts to the food present at the opening boundary.
- `RecoverLandAfterHarvest` replenishes stocks afterward; the code explicitly states that no recovered unit enters a receipt in that interval.

Consequently, a long turn can leave replenished food waiting until the next boundary. Shorter turns expose that regrowth to more harvests per physical century. This is an existing, documented harvest-first abstraction, not a newly introduced conservation defect. It means that “600 residents per tile” was an endpoint observation under a particular horizon, not a stable land-capacity constant.

## Irrigation: distinguish existing use, physical opportunity and investment decisions

At year 1,000 there are **34 irrigated fields in 22 of 118 villages** across the five full-treatment worlds. There were already 28 irrigated fields at the common checkpoint; by year 2,000 there are 35. The claim “no irrigation” applies to the leading cities, not to the whole simulation.

The full arms supply **30,690 village-turns** of sequential rejection diagnostics:

| First blocking stage | Village-turns |
| --- | ---: |
| No usable visible local river/lake source | 19,547 |
| Targets already served | 4,181 |
| No seasonally dry target on an incomplete route | 3,753 |
| No positive next-boundary food gain | 959 |
| No legal local route | 779 |
| Fertile-tile cultivation gate | 701 |
| Candidate routes already complete | 511 |
| No authorized food/feedstock gap | 252 |
| Project funded | 7 |

These categories describe the current local routing and opportunity rules. “No source” does not mean no water exists anywhere in the region. “No positive next-boundary gain” does not establish zero long-term value.

Among **9,971 village-turns at population ≥3,000**, no positive-return new project exists: 5,698 have no source; 2,038 no dry target; 1,983 already-served targets; 252 no food gap. None is blocked at construction arbitration.

Across all villages, seven requests authorize eight segment-equivalents. **Initial grants = final grants = actual construction = 8**, within floating-point tolerance. Canal-maintenance requests, grants and actual output also agree (84,145.142 segment-years); actual irrigation delivery totals 5,327,611.846 abstract water units. This audit does not reproduce the historical construction-underfunding defect already fixed in #49.

### Counterfactuals

- **Gate removed at year 1,000:** all five population trajectories are unchanged. This alone would not test earlier delays.
- **Gate removed from the initial checkpoint, seeds 101/1729:** 99 positive-opportunity village-turns in the controls were gate-blocked, all before year 1,000 and in villages below 1,000 residents. Seed 101's first new canal moves from year 938 to 175; seed 1729's from year 110 to 14. Irrigation delivery increases, but final largest-city populations are unchanged. Final world population is lower by 361 and 63 respectively. These two cases do not support removing the gate as a population fix.
- **Free prebuild of positive-scoring opportunities at year 1,000:** zero projects exist, so this arm makes no change. It does not test zero-score opportunities.
- **Free prebuild including physically watered, cultivated dry targets with zero immediate food gain:** adds 4 projects in seed 23, 3 in seed 47, 3 in seed 101, and none in 11/1729. Construction and the first service period are explicitly subsidized; later upkeep, water competition, soil clocks and ordinary staffing remain native. Final largest-city populations and first-5,000 dates remain unchanged. Final world population changes by 0/−49/−48/−69/0 for seeds 11/23/47/101/1729. Thus the currently omitted local opportunities do not rescue the city population target in this test.

These interventions do not test regional canals, distant fields, uncultivated dry-land bootstrap, changed settlement placement, permanent free upkeep, or altered water supply. They do not prove globally optimal irrigation policy.

## Recommended next work

1. **Resolve the physical meaning of land harvest across changing turn durations before tuning city capacity.** Decide whether cultivated land supplies food continuously during the horizon or only at turn openings. If the intended semantics are continuing farm production, design a conserved, analytical harvest/recovery transition with explicit labor and water limits, fixed boundary authority and cost proportional to active tiles/claims. Do not implement one simulated tick per year or restore an unbounded gross-renewal multiplier.
2. **Use these exact saved states as acceptance fixtures.** Compare fixed claims under one interval versus two/four subdivisions, then distinguish the remaining effects of repeated planning, staffing, trade and demography. Require consistent food accounting and document which outcome differences are intentional. Re-run the five matched long trajectories before changing mortality, fertility or land radius.
3. **Treat irrigation as a narrower opportunity/placement problem.** Improve explanations of source, route, moisture, cultivation and return gates. If broader adoption is a product goal, first choose whether to expand hydrological reach, allow dry-field bootstrap or enable regional provisioning; then measure a specific design. Lower construction costs or weakening the fertile-tile gate are not supported by the observed city bottleneck.
4. **Revisit Tier 3A and hinterland requirements after the food-time contract is stable.** Trade is enabled in this profile. `GoodsStorageEnabled=false` is the optional goods/Pot storage rule, not a switch disabling food reserves. Low measured reserves/trade explain weak early accounting practice; later reserve accumulation changes it. The earlier claim that larger cities necessarily require more than seven tiles is superseded.

## Validation and reproduction

Focused final-source checks pass **1,656 assertions**: 337 agriculture, 1,002 civic governance, 163 DemandControl and 154 Housing Material recipe assertions. The only simulation changes are caller-owned optional opportunity counters and one transient allocation diagnostic value. No gameplay coefficients, persistent state, observations, actions or production policies change.

Use .NET SDK 8.0.428 (runtime 8.0.34), Python 3 and Matplotlib for the optional plot. Run from the repository root. First build the untouched pinned revision into a separate output directory and run its sweep with the current unchanged profile:

```sh
dotnet /path/to/untouched/Anthropocene.CivicGovernanceSweep.dll \
  --years=1000 --arms=off,phase1,full,full-3a --snapshot-years=1 \
  --output=TestResults/CivicAudit/baseline

dotnet build Tools/CivicGovernanceSweep -c Release
dotnet Tools/CivicGovernanceSweep/bin/Release/net8.0/Anthropocene.CivicGovernanceSweep.dll \
  --audit --years=2000 \
  --arms=off,phase1,full,full-gate-off,full-prebuilt,full-physical-prebuilt,full-fixed-horizon \
  --output=TestResults/CivicAudit/extended

dotnet Tools/CivicGovernanceSweep/bin/Release/net8.0/Anthropocene.CivicGovernanceSweep.dll \
  --audit --seeds=101,1729 --years=2000 --arms=full-early-gate-off \
  --output=TestResults/CivicAudit/early-gate

python Tools/CivicGovernanceSweep/SummarizeAudit.py \
  TestResults/CivicAudit/extended TestResults/CivicAudit/early-gate

dotnet Tools/CivicGovernanceSweep/bin/Release/net8.0/Anthropocene.CivicGovernanceSweep.dll \
  --audit-duration-probe TestResults/CivicAudit/extended/seed-11-full-1000.anthro

dotnet Tools/CivicGovernanceSweep/bin/Release/net8.0/Anthropocene.CivicGovernanceSweep.dll \
  --audit-inspect TestResults/CivicAudit/extended/seed-11-full-final.anthro
```

The audit refuses an existing manifest to prevent silent result reuse. The older baseline runner still caches by filename; use a fresh baseline directory or explicitly validated saved checkpoints. `--baseline=...` selects a different baseline directory. Early-gate arms have no year-1,000 fingerprint-equivalence claim; their result records explicitly mark it inapplicable.
