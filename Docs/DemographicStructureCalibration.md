# Demographic structure calibration

This report records the controlled 50-turn comparison of portable-housing husbandry camps and fixed-housing agricultural villages. It separates mechanism implementation from calibration acceptance: the new controls and specialist-scale rule are implemented and tested, but no tested combination satisfies the complete demographic design target. In particular, the controlled agricultural village is goods/QoL limited before it reaches fission, while the pastoral lineage grows and fissions repeatedly. The result is therefore a useful rejection, not a guessed final tent-cost calibration.

## Rules and compatibility

### QoL mortality

For an economy camp, the annual demographic ceiling remains

```text
d(Q) = (b0 - g) Q^-a
r(Q) = max(0, b0 - d(Q))
Q*   = ((b0 - g) / b0)^(1/a)
```

New worlds now use `a = QoLMortalityElasticity = 1`. With `b0 = 0.045` and `g = 0.01145`, `Q* = 0.74556`, down from `0.86346` at `a = 2`. `QoLMortalityEnabled` and `FissionEnabled` remain opt-in. An old save that explicitly stored `a = 2` still loads as 2; the migration does not reinterpret it.

The Neutral 30% split's cohesion-gain threshold is reached at 252 residents for a fully tent-dominant camp and 817 for fully fixed housing under the current cohesion rule. At those points QoL from cohesion alone is about 0.81, still above `Q*`. Cohesion alone reaches `Q*` at 297 and 966 residents respectively. Focused checks therefore prove, for both housing forms:

```text
healthy positive growth -> crowding/fission threshold -> zero-growth QoL
```

### Portable housing

Light Housing no longer borrows fixed Housing's calibration. The independent parameters are:

| Parameter | New-world baseline | Meaning |
| --- | ---: | --- |
| `LightHousingDecayPerReferenceTurn` | 0.10 | Fraction lost per 20-year reference turn |
| `AnimalFabricPerLightHousing` | 1.0 | Animal Fabric consumed per unit built |
| `LightHousingBuilderOutputPerBaseDays` | 0.03125 | Units built by one fully efficient Builder per `BaseDays` |

Elapsed-time retention is `(1 - decay)^(years / ReferenceTurnYears)`. Recipe forecasting, authoritative production, value/lifetime calculations, herd forecasts and daughter bootstrapping all read the same three parameters. Fixed Housing continues to use its existing decay, material input and construction rate. Missing fields in old saves map to the historical shared values, preserving continuation.

Craftspersons can still execute the Animal Fabric and Light Housing recipes at `GeneralistTaskEfficiency`; no specialist is required for a daughter or a small husbandry camp to bootstrap.

### Specialist concentration

A matching specialist now receives the same bounded local multiplier in planner forecasts and authoritative resolution:

```text
M(N) = 1 + Bmax (1 - exp(-max(0, N - N0) / Nscale))
```

`N` is the current local resident population. The multiplier is one through `N0`, rises continuously, and is strictly bounded below `1 + Bmax`. It applies only when the worker is doing that role's actual specialist recipe or extraction task and the society owns the matching technology. Gatherers and Craftspersons are unchanged. Stone, clay and wood extraction specialists use the same rule; there is no architectural exception. Farming, foraging, hunting, livestock yield and biomass receive no population bonus.

The implementation is O(1), deterministic and centralized in `SpecialistProductivity`. Because no tested nonzero candidate passes the route-level acceptance criteria, new worlds and old saves both retain the previous fixed specialist rate (`Bmax = 0`). The tested moderate reference remains `Bmax = 0.50`, `N0 = 100`, `Nscale = 400` and can be enabled explicitly for further experiments.

## Controlled experiment

The reproducible harness is `Tools/DemographicStructure`. It runs 50 20-year turns and captures aggregate and per-tribe state at turn 0 and every five turns. All experiments explicitly enable QoL mortality, fission, herd labour and shared crop/herd allocation; immigration is disabled. The planner is continuous deterministic Value automation, with no movement AI.

Both routes begin with 240 people, first-tier craft knowledge, ordinary hand tools, a two-reference-turn fabric stock and complete appropriate shelter. An all-land 128 by 64 map is known to the founder, so there is effectively unlimited viable fission space. Stone, clay, wood, renewable food and water are deliberately ample. This makes resource-node starvation and destination discovery nonbinding without changing ecological or production constants.

- **Pastoral:** productive grassland, mobile Animal Husbandry, goats, Light Housing and Animal Fabric, no Cultivation. The small domestication seed is 5 goats; the established herd is 80 goats, one animal per three initial residents. Livestock reproduction, feed, management and slaughter equations are unchanged.
- **Agricultural:** permanent fertile desert river settlement, six river-supported tiles at the current Stone Farm Tool supported intensity, fixed Housing, normal tools and material inputs. Crop productivity, moisture response and field effort are unchanged.

The trajectory files include total population, tribe count and population quartiles, mean/median and route QoL, fissions and inter-formation interval, animals, all food sources, maintenance/growth/manufacturing consumption, accepted growth food, clothing/housing/cohesion, housing-form shares, QoL-blocked camp-turns and starvation deaths. `per_tribe.csv` records the same population, QoL, coverage, housing form, animal and workforce context per camp.

## Staged tent-cost sweep

The first stage varied one portable-housing input at a time from the historical-equivalent baseline. The second stage evaluated combinations. This table reports the established-herd pastoral lineage at turn 50, with specialist scaling off. `Tent food/surplus` is Animal Fabric plus Light Housing food input divided by food remaining after human maintenance; it is more informative than the corresponding roughly 1.1% share of total production.

| Candidate | Population | Camps | Median camp | Median QoL | Fissions | Housing | Tent food/surplus | Tent labour | Starvation deaths |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| historical equivalent: 0.10 / 1.0 / 1.0x | 52,253 | 101 | 593 | 0.819 | 100 | 96.1% | 11.6% | 2.02% | 0 |
| decay 0.20 | 46,814 | 85 | 597 | 0.848 | 84 | 94.6% | 11.3% | 2.24% | 0 |
| decay 0.30 | 42,896 | 73 | 563 | 0.842 | 72 | 93.1% | 10.9% | 2.17% | 0 |
| decay 0.40 | 39,270 | 67 | 556 | 0.819 | 66 | 91.2% | 11.2% | 2.20% | 0 |
| fabric 1.5 | 49,487 | 96 | 581 | 0.808 | 95 | 95.5% | 11.3% | 1.93% | 0 |
| fabric 2.0 | 47,545 | 89 | 644 | 0.782 | 88 | 95.8% | 11.3% | 1.97% | 0 |
| productivity 0.75x | 49,570 | 96 | 612 | 0.824 | 95 | 95.5% | 11.2% | 2.10% | 0 |
| productivity 0.50x | 48,520 | 94 | 582 | 0.830 | 93 | 96.3% | 11.5% | 2.78% | 0 |
| 0.30 / 1.5 / 0.75x | 43,845 | 76 | 594 | 0.825 | 75 | 92.9% | 10.8% | 2.57% | 0 |
| 0.40 / 1.5 / 0.75x | 41,139 | 68 | 575 | 0.836 | 67 | 93.1% | 11.3% | 2.82% | 0 |
| 0.40 / 2.0 / 0.50x | 38,753 | 66 | 553 | 0.809 | 65 | 91.6% | 11.0% | 3.09% | 0 |

The portable-housing Pareto region within this sweep is approximately decay 0.30-0.40, fabric 1.5-2.0 and productivity 0.50-0.75x. All tested points remain self-sufficient, maintain 91-93% housing in the combined cases and produce no starvation deaths. The portable chain costs about 11% of post-maintenance food surplus and 2.6-3.1% of labour, a visible recurring opportunity cost without consuming nearly all workers. The more expensive end reduces total pastoral population and fissions, but it does not make camps materially smaller than the agricultural control.

This is a provisional region, not a selected shipped constant. New-world Light Housing values remain at their historical-equivalent baseline pending a calibration that passes the complete comparison.

## Specialist sweep and final trajectories

The scale sweep covered maximum bonuses 0%, 25%, 50% and 75%, with onset/scale pairs 50/200, 100/400 and 150/600. The final rerun used the middle tent candidate (decay 0.40, fabric 1.5, productivity 0.75x) and compared no scale effect, moderate (`0.50 / 100 / 400`) and stronger (`0.75 / 75 / 300`) effects.

The first scale screen held portable housing at its historical-equivalent cost. `B / N0 / Nscale` names the multiplier calibration; each population pair is agricultural/pastoral with an established herd.

| B / N0 / Nscale | Final population A / P | Median camp A / P | Camps A / P | Fissions A / P | Pastoral tent labour |
| --- | ---: | ---: | ---: | ---: | ---: |
| 0 / 100 / 400 | 532 / 52,253 | 532 / 593 | 1 / 101 | 0 / 100 | 2.02% |
| 0.25 / 100 / 400 | 455 / 51,737 | 455 / 620 | 1 / 100 | 0 / 99 | 1.74% |
| 0.50 / 100 / 400 | 510 / 51,605 | 510 / 637 | 1 / 98 | 0 / 97 | 1.48% |
| 0.75 / 100 / 400 | 399 / 53,250 | 399 / 638 | 1 / 102 | 0 / 101 | 1.32% |
| 0.50 / 50 / 200 | 399 / 52,729 | 399 / 598 | 1 / 100 | 0 / 99 | 1.41% |
| 0.50 / 150 / 600 | 459 / 51,391 | 459 / 607 | 1 / 99 | 0 / 98 | 1.56% |

The screen confirms bounded throughput savings but no agricultural concentration advantage. Earlier onset and stronger bonuses do not improve the route ordering; the 75% and 50/200 cases also leave the agricultural control blocked by QoL in 90% of observed camp-turns.

| Case | Route/herd | Population | Camps | Median camp | Median QoL | Fissions | QoL-blocked camp-turns | Tent labour | Starvation deaths |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| current | agriculture | 532 | 1 | 532 | 0.675 | 0 | 46.0% | - | 0 |
| current | pastoral, seed 5 | 17,803 | 34 | 510 | 0.869 | 33 | 5.8% | 2.66% | 0 |
| current | pastoral, herd 80 | 41,139 | 68 | 575 | 0.836 | 67 | 12.3% | 2.82% | 0 |
| moderate | agriculture | 510 | 1 | 510 | 0.747 | 0 | 48.0% | - | 0 |
| moderate | pastoral, seed 5 | 15,865 | 30 | 482 | 0.894 | 29 | 5.6% | 2.08% | 0 |
| moderate | pastoral, herd 80 | 40,512 | 67 | 589 | 0.828 | 66 | 11.4% | 1.97% | 0 |
| stronger | agriculture | 414 | 1 | 414 | 0.750 | 0 | 46.0% | - | 0 |
| stronger | pastoral, seed 5 | 17,811 | 34 | 470 | 0.864 | 33 | 5.3% | 1.79% | 0 |
| stronger | pastoral, herd 80 | 46,197 | 78 | 558 | 0.820 | 77 | 11.9% | 1.59% | 0 |

The moderate established-pastoral trajectory grows from 240 people in one camp to 867/2 at turn 10, 2,953/8 at turn 20, 8,961/21 at turn 30, 21,826/40 at turn 40 and 40,512/67 at turn 50. Its median interval between repeated daughter formations is four turns. The moderate agricultural trajectory reaches 399 at turn 10, 480 at turn 20, 505 at turn 30 and 510 at turn 50, never fissioning. Its QoL oscillates with clothing/housing replenishment and ends at 0.747, essentially `Q*`; 48% of its observed camp-turns have a zero QoL growth ceiling.

The small herd delays pastoral expansion materially, demonstrating herd sensitivity without making domestication fail. At moderate scaling it ends at 15,865 rather than 40,512 people and 30 rather than 67 camps. Both remain starvation-free.

Fission now has the intended local productivity cost in principle: the daughter multiplier is estimated from daughter population, and after formation every forecast and receipt uses that smaller camp's population. Generalists remain available at the unchanged 0.25 efficiency. In this sweep, however, the benefit mostly makes portable goods cheaper: established-pastoral tent labour falls from 2.82% to 1.97% to 1.59%. The stronger setting then increases pastoral population and fission nonlinearly. Because the formula is bounded, this is not an unbounded productivity loop, but it is a real demographic feedback and a reason to reject the 75% region.

## Village oscillation diagnosis

The agricultural QoL oscillation is a two-turn fixed-Housing replacement cycle created by the Value economic planner. Fixed Housing's 10% reference-turn decay is the physical disturbance, but decay alone does not require alternating outcomes. The planner converts it into an order-up-to cycle:

| Start state | Housing in the planner's post-decay preview | Value-plan decision | Resolved housing |
| --- | ---: | --- | ---: |
| 100% covered | 90% | the marginal replacement claim is below the selected labour price; no Builders | 90% |
| 90% covered | 81% | the larger compounded gap crosses the price threshold; Builders restore the full gap | 100% |

For example, 480 residents require 120 fixed-Housing units. One skipped replacement leaves 108 units. On the following turn those units first decay to 97.2, after which producing 22.8 Housing Material and 22.8 Housing units restores complete coverage. The material and assembly receipts occur in the same turn and the intermediate Housing Material stock ends at zero, so this is not a one-turn construction pipeline or resolver-ordering delay.

The planner does see the replacement requirement: `CampProductionDemand.Build` depreciates its preview before calculating useful demand. `ValueFulfilment.NeedShare` then derives a target coverage from the shared labour price and clamps the requested share to zero when the existing post-decay stock is above that target. The largest-staffable-path search and integer role staffing sharpen that continuous valuation into a zero-Builder/rebuild cadence. A focused turn-by-turn trace in the moderate case alternated between zero Builders and five or six Builders; in the no-scale case the mature village alternated between zero and seven.

Housing directly changes the domestic-needs term and also changes social cohesion. Once clothing stabilizes in the moderate case, the 90%/100% Housing cycle alone moves village QoL between about 0.693 and 0.751. At the current no-scale behavior, Weaver staffing and Cloth output also alternate, increasing the QoL amplitude to about 0.675-0.801. QoL mortality is therefore a downstream amplifier: the low phase falls below `Q*` and suppresses growth, but mortality does not initiate the Builder cycle.

A Uniform-rule counterfactual did not exhibit the bounded 90%/100% cycle: it continued assigning no replacement work and Housing declined until fission. This confirms that the alternating repair cadence is specific to the current Value allocation, while also showing that simply selecting Uniform is not a solution. The appropriate follow-up is planner-side recurring-maintenance treatment or another smooth replacement policy. It should not be compensated for by changing housing decay, QoL mortality, crop/livestock yields or ecology.

The diagnosis excludes the following candidate causes:

- **Ecology or resource scarcity:** food and raw material sources remain deliberately nonbinding and starvation is zero.
- **Housing Material staging:** material manufacture and Housing assembly resolve in equal amounts in the same turn.
- **Specialist scaling:** the Housing cycle persists at zero, moderate and strong scale bonuses; scaling changes its amplitude and phase but is not its source.
- **Fission:** the village does not fission; the oscillation exists before any daughter-state effects.
- **QoL mortality:** it reads the resulting QoL and suppresses growth; it does not choose Builder staffing.

## Acceptance assessment

The mechanism-level criteria pass:

- both routes are food-sustainable in this controlled setting;
- neither pastoral herd requires farming, and all runs have zero starvation deaths;
- generalists can make every previously allowed good and bootstrap daughters;
- portable shelter has a measurable recurring food and labour cost;
- tiny camps receive no scale bonus and all bonuses saturate below their configured maximum;
- craft and extraction forecasts use the same helper as resolution;
- no crop, livestock, biomass, moisture or field-effort calibration changed.

The complete demographic calibration is rejected:

- agriculture is not the larger population and does not fission at all;
- pastoral median camps remain roughly 470-590 people rather than materially below the agricultural 414-532;
- pastoral QoL is higher, not lower, than the agricultural control;
- stronger specialist scaling helps the already numerous pastoral specialists enough to increase pastoral expansion;
- the agricultural settlement's manufacturing coverage cycle drives QoL to the mortality floor before fixed-housing crowding reaches the Neutral fission threshold.

Accordingly, the recommended **mechanism search region** is `Bmax` 0.25-0.50 with onset around 100 and scale 400-600: it gives a small camp little benefit, a few hundred residents a noticeable one, and stays away from the stronger sweep's feedback. It is not enabled by default because no point passed the overall comparison. The recommended **portable-cost search region** is decay 0.30-0.40, fabric 1.5-2.0 and productivity 0.50-0.75x. No point in either region should be declared the final demographic calibration until the agricultural goods/QoL bottleneck is understood and a rerun satisfies the route-level acceptance criteria.

## Limitation diagnosis

The observed effects are deliberately distinguished as follows:

| Layer | What this experiment says |
| --- | --- |
| Ecological carrying capacity | Deliberately nonbinding: resource-rich land, water and nodes; zero starvation deaths. These runs do not justify changing yields or ecology. |
| Demographic/QoL limitation | Binding for agriculture: QoL repeatedly reaches `Q*`, suppressing growth before fixed-housing fission. Much less binding for pastoral camps after repeated splits. |
| Goods/manufacturing limitation | The agricultural control shows alternating clothing/housing coverage and QoL. Tent inputs are meaningful but not dominant for pastoral camps. |
| Specialist productivity | Bounded local throughput lowers goods labour; it is not a food/farming bonus. Moderate effects are stable, while the stronger case creates a noticeable but bounded demographic feedback. |
| Fission effects | Pastoral lineages repeatedly split, with a four-turn median repeat interval in the representative run; agriculture never reaches a successful split. Daughters lose local scale but remain viable. |

The surprising nonlinearity is that increasing specialist throughput does not monotonically improve agricultural population (532 current, 510 moderate, 414 stronger), while strong scaling increases established-pastoral population (41,139 to 46,197). Goods timing, QoL mortality and fission make throughput changes feed back through planner allocation rather than translating directly into monotonic growth.

## Reproduction and artifacts

Run from the repository root with a .NET 8 SDK:

```bash
bash Tools/DemographicStructure/Run-Sweep.sh
```

- Full aggregate trajectory: `Tools/DemographicStructure/Results/trajectories.csv`
- Full per-tribe trajectory: `Tools/DemographicStructure/Results/per_tribe.csv`
- JSON runs, including specifications: `Tools/DemographicStructure/Results/runs.json`
- Concise candidate table: `Tools/DemographicStructure/Results/candidate_summary.csv`
- Experiment metadata: `Tools/DemographicStructure/Results/metadata.json`
- Required plots: `Tools/DemographicStructure/Results/plots/`

The stage plans are retained beside the harness, so the screening and final comparisons are auditable and reproducible.
