# Headless civic divergence and irrigation eligibility

**28 November 2026; simulation at PR #52, `1a2c38639bd97789de9581f8f6ab592ded4decee`.** Five seeds (11, 23, 47, 101, 1729), two native AI playthroughs per seed, 2,000 physical years after civic activation. The annual food model substantially accelerates accounting and institutional complexity. It produces more egalitarian **and** more stratified villages; mixed institutions remain the majority. Irrigation reaches the same 22 villages, sooner.

## What was compared

Each arm loads the same original checkpoint after the organic opening and 600-year continuation used by the earlier civic audit. That history includes the existing Plough/Irrigation knowledge grant. Both arms enable full civics at elapsed year zero; only the annual arm enables `AnnualLandFoodEnabled`. No additional technologies, civic precedents, population, construction, irrigation, policy or workforce are granted during these runs.

This differs from [the PR's initial experiment](AnnualLandFood.md): that experiment changed food rules only after 1,000 years of civic development. Here food and civics change together at civic onset, so settlement formation and subsequent histories also diverge. These are not fresh annual-mode worlds from the Paleolithic.

The headless driver uses normal AI → Prepare → Resolve. Census snapshots are taken at the first boundary at or beyond each 100-year mark; all final boundaries are at year 2,002.89. Classifications use `C > .05` and `P < −.25` for egalitarian, `P > .25` for stratified, and mixed between; lower-complexity villages are incipient. Tier 3A and 3B are separate branches, not a single linear tier sequence.

All **10/10** runs pass final invariants, census/Prepare purity and live-versus-reloaded next-turn equality. All **5/5** legacy endpoints exactly reproduce the original decoded #51 continuation. The harness changes no simulation implementation. [Summary/provenance](Experiments/CivicPlaythrough-20261128-summary.json) and [aggregate CSV](Experiments/CivicPlaythrough-20261128-aggregate.csv) retain the measurements and save/binary hashes.

## The playthrough

| Years after civic activation | Legacy population | Annual population | Legacy accounting villages | Annual accounting villages | Legacy drainage villages | Annual drainage villages |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 0 | 70,349 | 70,349 | 0 | 0 | 0 | 0 |
| 300 | 142,488 | 234,922 | 0 | 0 | 0 | 0 |
| 500 | 159,253 | 408,088 | 0 | 64 | 36 | 74 |
| 1,000 | 243,611 | 429,251 | 2 | 64 | 63 | 81 |
| 1,500 | 320,203 | 434,838 | 2 | 64 | 77 | 83 |
| 2,000 | 391,077 | 430,961 | 14 | 64 | 82 | 84 |

The first Tier-1 and Tier-2 unlocks remain near years 69 and 200 in both models. First drainage remains near year 345. **First accounting moves to year 357 in every annual seed**, compared with years 963–1,835 in the legacy seeds. By year 500, the annual treatment already has 64 accounting villages; that count then remains flat.

The mechanism is consistent with the recorded food surplus and the existing Tier-3A practice equation. At year 300, Tier-2 villages have a median **29.2 years of stored food** with annual production, versus approximately zero in legacy. The storage term can now fund accounting practice before trade becomes substantial. Trade is enabled in this profile; `GoodsStorageEnabled=false` disables the optional goods/Pot storage rule, not food reserves. The early accounting result does not require changing its practice threshold or granting the precedent.

Annual growth is front-loaded, followed by a plateau. Aggregate population falls slightly from 433,681 around year 1,800 to 430,961 at the endpoint. The old model continues growing as turns shorten. The annual change is not a population increase in every world: seed 11 ends with 91,885 people versus 107,110 legacy, and fewer villages (17 versus 25). Across all five, population is 10.2% higher, but villages are fewer (105 versus 118).

## Civic divergence

| Final measure | Legacy | Annual |
| --- | ---: | ---: |
| Villages | 118 | 105 |
| Tier 2: civic organization | 106 | 104 |
| Tier 3A: accounting | 14 | 64 |
| Tier 3B: drainage | 82 | 84 |
| Mean complexity per village | 0.674 | 0.853 |
| Population-weighted complexity | 0.787 | 0.948 |
| Mean stratification | +0.038 | +0.041 |
| Egalitarian villages | 5 | 10 |
| Mixed villages | 92 | 79 |
| Stratified villages | 10 | 15 |
| Incipient villages | 11 | 1 |
| Stewards | 31,558 | 35,661 |

Higher complexity does not imply a global shift toward hierarchy: average stratification barely changes, while both ends of the distribution grow. This is not solely a small-village effect: the annual treatment contains eight egalitarian and fifteen stratified villages with at least 1,000 residents, versus four and nine in legacy.

One hundred original camp IDs remain villages in both arms, allowing a direct same-community comparison. Later daughter IDs are not matched across branches, because the same numeric ID can belong to a different birth event after divergence. Examples:

| Seed / original camp | Legacy population / P | Annual population / P | Change |
| --- | ---: | ---: | --- |
| 11 / 67 | 7,283 / +0.174 | 6,527 / −0.428 | Mixed → egalitarian |
| 11 / 55 | 5,811 / +0.154 | 8,270 / +0.270 | Mixed → stratified |
| 23 / 49 | 1,253 / −0.150 | 1,605 / +0.128 | Moves toward hierarchy, remains mixed |

For camp 67, the measured circumscription/exit-cost driver falls from 0.899 to 0.479, alongside lower land/capital intensity. For camp 49 it rises from 0.508 to 0.834. These are explanatory correlates within the actual civic target equations, not a controlled test assigning governments to otherwise identical villages. Native relocation, neighbors, capital, policy and food histories all contribute.

## Irrigation eligible versus used

At the annual endpoint:

| Village-level condition | Count |
| --- | ---: |
| All villages / irrigation knowledge | 105 / 105 |
| Visible nearby renewable freshwater source | 40 |
| Legal local route from a source | 37 |
| Installed irrigation | **22** |
| At least one currently watered field | **22** |
| Additional dry, cultivated target that a proposed extension could water | 4 |
| Additional project with positive forecast net food gain | **0** |
| Additional positive project passing the cultivation gate | **0** |
| Eligible additional project in a village without irrigation | **0** |

Thus **22/105 villages irrigate (21.0%)**, or **22/40 source-near villages (55.0%)**. Proximity to water is a broad physical screen, not sufficient project eligibility. Of the other 18 source-near villages, 15 lack a dry target and three lack a legal route. The remaining 65 villages lack a local renewable source under the existing visibility/footprint rules.

All four waterable extensions belong to villages already irrigating. There is no additional, currently waterable cultivated target in an entirely unserved village at this endpoint. Equivalently, the union of existing irrigation users and villages with a waterable candidate is 22, and all 22 already use irrigation. This is a snapshot under the current local rules, **not proof of 100% optimal adoption over the whole run** or of the absence of opportunities beyond the allowed footprint.

The annual treatment has **35 installed fields, 32 currently watered**, spread over those 22 villages. Legacy has the same 22 users and 35 installed fields, with 33 watered. The annual run reaches 22 users / 35 fields by the year-310 census; legacy reaches 22 users around year 1,003 and 35 fields near year 2,003. Early positive opportunities blocked by the cultivation gate are still observed; the counts above do not erase those historical delays.

The four extension cases are seed/camp **23/52, 47/14, 47/55, 101/101**. All pass the cultivation gate and have a planned food gap, but their proposed extensions have no positive net food forecast, so no construction request is made. This gives a focused next investigation: decompose incremental food gain and source-water competition for those four cases, rather than assume a general failure to fund eligible projects. Separately inspect why three installed fields are dry. Neither question is answered by treating all 105 knowledgeable villages as eligible canal sites.

## Reproduction and limits

Build `Tools/CivicGovernanceSweep/Anthropocene.CivicGovernanceSweep.csproj` in Release, then run the binary with:

```
--civic-playthrough --seeds=11,23,47 --output=TestResults/CivicPlaythrough/a
--civic-playthrough --seeds=101,1729 --output=TestResults/CivicPlaythrough/b
```

These commands require the original checkpoints and control saves described in [the preceding audit](CivicIrrigationTrajectoryAudit.md). Each output directory must be fresh. Then:

```
python Tools/CivicGovernanceSweep/SummarizeCivicPlaythrough.py \
  TestResults/CivicPlaythrough/a TestResults/CivicPlaythrough/b
```

This produces the population/tier/complexity/divergence chart, irrigation funnel chart, aggregate CSV, village census CSV and full summary under `TestResults/CivicPlaythrough/report`. All native endpoint saves are retained in the run directories. The census is sampled every 100 years, so short-lived eligibility/request windows can be missed. First world-tier unlocks are recorded every turn. This is a five-world, four-founder inherited-history comparison, not a fresh-opening calibration, civic-policy causal experiment, Unity playtest or large-empire performance benchmark.
