# Handoff: social economy, herd valuation and remaining work

Written for follow-on agents who start cold, on branch `features/neolithic-agricultural`. It records where the
work stands, every decision the user has made, the ordered work queue with acceptance criteria, how to measure, and
the numbers to compare against.

Measurement outputs live under `TestResults/`, which git ignores. A fresh container will not have them: regenerate
baselines with the commands in section 5. The key numbers are copied into this file.

## Shared planner implementation (2026-10-02)

The user authorized the shared allocation correction on
`features/herd-ecology-retention`. It is implemented behind
`SharedCropHerdAllocationEnabled=false`, including old-save default-off
compatibility. Read [SharedAllocationValidation.md](SharedAllocationValidation.md)
for the mechanics, bounded computation, focused/replay checks, five-world
population/animal/QoL quantiles and matched 25/50-turn performance.
The control is the exact corrected simulation source at `03d1ed1`; both
variants resume identical control opening saves. Animal rates and yields
remain unchanged. Historical statements below that call this correction
"next work" describe the pre-implementation checkpoint. HV3 range approval,
animal-unit grounding, acquisition and HV4 remain separate work.

## Current follow-on checkout (2026-10-02)

Use branch `features/herd-ecology-retention` for the next agent. It includes
the implemented ecology/crop corrections and their reproducible audit tools.
Start with [PastoralPlannerCorrectionDesign.md](PastoralPlannerCorrectionDesign.md)
for the user's latest shared-planner direction, then
[PastoralPlannerAudit.md](PastoralPlannerAudit.md) for confirmed causes and
[CropHerdBalanceValidation.md](CropHerdBalanceValidation.md) for the control,
population distributions, QoL, herd counts and 25/50-turn performance.
The new shared-allocation correction is proposed, not implemented. Preserve
the existing tested mechanics while addressing it. Generated saves/logs are
ignored; the tracked reconstruction patch, source hashes and commands in
the validation report regenerate the baseline on a fresh checkout.

## Follow-on status (2026-10-01)

The user authorized the `.50` crop-moisture response and planner corrections
after [AgricultureMoisturePlannerAudit.md](AgricultureMoisturePlannerAudit.md).
They are implemented on `features/herd-ecology-retention`, preserving the
biomass-first/finite-horizon candidate. Read
[CropHerdBalanceValidation.md](CropHerdBalanceValidation.md) first for the new
shared crop/harvest rules, actual farm-tool coverage, corrected food credit and
maintenance-constrained harvest savings, and fission without hypothetical
daughter fields. The full fixed suite passes, including 104 crop and 627
fission assertions. Five paired Standard/80 worlds were measured at 25 and
50 post-opening turns with herd labour, fission and QoL mortality enabled
(elasticity 1). At turn 50 the corrected worlds contain 309,902 people in
agriculture-primary tribes and 467,833 in husbandry-primary tribes, with
resident-weighted QoLs .813 and .843. The latter hold only 5,919 domestic
animal units (median five per tribe); 1,069 of 1,136 have fields. This does
not resolve pastoral balance. Matched-save timing finds a 3.2% regression at
25 turns; the 17.3% reduction at 50 turns mostly reflects fewer tribes.
Per-tribe-turn cost rises about 5–6%. Defaults for herd labour, fission and
QoL mortality remain off; the new crop rules are on for new worlds and
neutral/legacy when their save fields are absent.

Subsequent user direction was to ascertain why pastoral economies remain
small. [PastoralPlannerAudit.md](PastoralPlannerAudit.md) records the matched
corrected-C1 source trace and one-plan sensitivities. Confirmed next issues:
spare field workers bypass the priced expansion claim while extra herders
face the labour price; herd value buckets credit protected inventory
differently from the common crop/food helper; and retention is evaluated
against a maintained-field baseline without a reduce/abandon alternative.
The sampled desert camp's actual crops are on two wet river tiles, and
99.26% of sampled C1 husbandry crop capacity has full suitability. The crop
penalty is operating as designed there. No production changes were made in
that follow-up; do not claim these remaining discrepancies are already fixed.

On 2 October 2026 the user clarified the correction's intent: spare labour
must compete between herd growth and fields; livestock is also food security;
use one planner; and price committed future labour. The proposed
[shared allocation correction](PastoralPlannerCorrectionDesign.md) records
these directives and the existing q-dependent maintenance costs. It is a
design specification, not a completed production change. Primitive herding
is two units per worker, a linear cost rather than fixed total staffing.

The user authorized HV1/HV2 on a follow-on branch, `features/herd-valuation`,
from `87f5a71`; this supersedes the original branch-only workflow below for
this package. [HerdValuationValidation.md](HerdValuationValidation.md) records
the candidate and unchanged-calibration measurements. Herd-labour checks are
now 1,687. [HerdValuationGrounding.md](HerdValuationGrounding.md) records unit
derivations and provisional proposals; external citations remain blocked by
source access. HV1 is not fully sourced, HV3 ranges are not approved, and
HV3/HV4/HV5 remain pending. Defaults remain off.

## 1. How to work here

- Read `AGENTS.md` first: the engineering doctrine, contracts and source map. Then `Docs/CurrentSimulationContract.md`.
- **Branch.** Develop and push only `features/neolithic-agricultural`, using
  `git push -u origin features/neolithic-agricultural` and retrying network failures after 2, 4, 8 and 16 s. Do not
  open a pull request unless the user asks.
- **Commits.** End every commit message with the attribution trailer lines your session's instructions supply. Never put model identifiers in code, docs or any repository file, and never print or commit credentials.
- **Governing principles from the user:**
  - Correctness of the model matters far more than speed.
  - Ground every mechanic in the system; no arbitrary hard cutoffs or label switches.
  - Design numbers are targets for sweeps, not constants.
  - Keep everything scalable to modern eras.
- **New behaviour ships opt-in** until it is measured and accepted. The pattern: a config `bool`, an `OptionalField`
  whose absence in old saves means off, a matching `OpeningConfig.json` value, a contract row checked by
  `Tools/Docs/Check-CurrentContract.py`, and a `Docs/ConfigurationUnits.md` row.
- **Orchestration.** Previous sessions delegated implementation to background agents with written specs and reviewed
  before committing. Two agents ran in parallel with disjoint file ownership, one in a git worktree. A worktree is
  cleaned only if unchanged; merge by cherry-picking commits, never by copying a dirty tree.
- **Clean tree.** A stop hook requires a clean tree. Never commit half-finished work just to satisfy it; explain
  instead. `.claude/worktrees/` is excluded locally through `.git/info/exclude`.

## 2. State of the branch

The latest commits, newest first. Earlier history is in `git log`.

| Commit | What | Default |
|---|---|---|
| 9494571 | Herd-labour coupling: herders at the labour price, controlled slaughter (`Docs/HerdLabour.md`) | **off** (`HerdLabourEnabled`) |
| c7eff9f, ea0dd3b, 3377992 | Hinterland knowledge, decaying explored memory, home-relative fission destinations, fission funnel counters (`Docs/ContactKnowledge.md`, `Docs/Fission.md`) | on; fission itself is off |
| e8a77c5 | Distress fission: a split driven by QoL, not crowding alone (`Docs/Fission.md`) | inside fission (off) |
| 01456a6 | QoL-dependent mortality (`Docs/QoLMortality.md`) | **off** (`QoLMortalityEnabled`, elasticity default 2; a = 1 recommended) |
| 21e0ec6 | Fission throughput: daughter shelter estimate, radius 4 | inside fission |
| 1122160 | Social cohesion derived from housing, not technology labels (`Docs/SocialCohesion.md`) | on |

Opt-in levers that are currently **off**: `FissionEnabled`, `ImmigrationEnabled`, `QoLMortalityEnabled`,
`HerdLabourEnabled`.

Verification reference: the focused headless suite (`Tools/Headless`, positional seeds `0`) passes with no failures.
Current counts include:
- contact knowledge 136
- fission 626
- herd-labour 986
- immigration, QoL cohesion and QoL mortality unchanged from earlier commits

## 3. The goal these packages serve

The **camp-size contract**:
- QoL limits growth.
- Camps top out near about 100 unhoused, about 250 in tents and about 600–1,000 in fixed housing. These are maxima for
  sweeps, not constants.
- Fission is a response to QoL: crowding, distress, and later herd pressure.

Analytic anchor, for a camp that is fed, clothed and housed, so that QoL ≈ cohesion S:
- Growth stops at S = Q* = 0.746 under a = 1, which is N = 0.54 · N_s.
- Splits start paying at N = 0.458 · N_s.
- N_s = 220 · (1 + 7.1 · H).

| Housing | Splits start paying | Growth stops (a = 1) | Target |
|---|---|---|---|
| None (H = 0) | 101 | 119 | about 100 |
| All tents (H = 0.21) | 252 | 296 | about 250 |
| All fixed (H = 1) | 817 | 963 | 600–1,000 |

Root causes found so far, all measured on five Standard/80 worlds:
1. **Food never lowers QoL.** Food-and-water satisfaction is 1.00 at P25 in every class. Growth spends only surplus
   food, so land limits growth silently. Distress fission therefore rarely fires (14 splits). See queue item 4.
2. **Fission was blocked by having nowhere to go.** 90% of camps that wanted to split had no viable destination,
   because vision radius 2 equals the spacing exclusion. Fixed by hinterland knowledge: splits rose 4.4× with
   mortality off and 2.7× with a = 1. 57–68% of screened searches still find no legal candidate.
3. **Herders trapped under mortality.** Fixed by the herd-labour coupling, which over-corrects. This is queue item 1.

## 4. Work queue (in order)

### HV — Herd valuation recalibration (next; specification: `Docs/HerdValuationDesign.md`)

This makes `HerdLabourEnabled` safe to turn on. Split it into these packages.

**HV1 — Grounding and units (docs and config only).**
- Write the unit derivation for a managed animal unit (1 food per 91.25-day interval, which is one person's
  consumption).
- Cite ranges for herd size per herder, annual natural increase, and offtake per species.
- Propose values for `AnimalsPerHerder` (2), the Herding Tool tiers (2.69/3.87/6.02/10.71/20) and species
  `GrowthRate` (0.0201 for every species).
- Output: a `Docs/ConfigurationUnits.md` section and a table of proposed ranges.
- No code changes beyond comments. The user should approve the ranges before HV3 tunes within them.

**HV2 — Capital-value herd ladder** (`HerdLabour.cs` `Build`/`StepValues`; design 3.1–3.3).
- Value per animal: V_keep = v_f · y · L_herd(g_eff) with g_eff = g · (1 − H/K) and L_herd = 1/(r + m − g_eff),
  capped at the field capital-life ceiling. V_slaughter = carcass food at its bucket value.
- Steps are staffed while the annualised keep-minus-slaughter value per herder-year is at least w. Slaughter animals
  whose keep value is below their carcass value.
- The alternative is the **marginal field step's** annualised value at w from the expansion ladder. Remove
  `altRate = GatherPerPerson · FarmerProductivity`.
- Per-tile land mix:
  - non-cultivable footprint tiles: gathering is the only alternative;
  - cultivable tiles: the marginal field step;
  - mobile camps also count pasture within herd range, with no field alternative.
- Remove or reduce the expansion ladder's herd-growth credit so value is not double counted (`ProductionDemand.cs`
  `ExpansionValuation`; `Docs/HusbandrySettling.md`).
- Tests (extend `Tools/Headless/HerdLabourChecks.cs`):
  - the closed forms;
  - a herd step and a field step of equal annualised value are staffed in the same order;
  - steppe versus alluvial fixtures (the herd wins on steppe, fields win on rich cultivable land);
  - the trap fixture still escapes, and a healthy pastoral camp is unchanged;
  - determinism, and old saves.
- Full focused suite: explain every changed line.

**HV3 — Sweep and calibrate.**
- Five worlds, 12 + 50 turns, `--herd-labour=1`, crossed with mortality {off, a = 1} and fission {off, on}.
- Against the lever-off control, require:
  - camps holding herds and husbandry-first population within ±20%;
  - no trapped camps (every resident herding and Q ≤ Q*);
  - herder share P90 at most 0.35;
  - husbandry QoL P25 at least 0.71 (fission off) and 0.77 (fission on);
  - settling behaviour not regressed;
  - turn time within +5% of the lever-on result.
- Tune only within HV1's ranges. Then re-measure the AGENTS.md route regression (seeds 1–3, every shape,
  Standard/80) and update its numbers.

**HV4 — Herd-carrying split** (a third fission trigger; user-requested; `Fission.cs`).
- When a camp's herd exceeds what it chooses to staff, a daughter can take the **surplus animals** plus the herders
  they need, so animals per person fall for the stayers. Plus her proportional share of the other people and stores.
- Destination: viable pasture, using the existing herd forage and water desirability.
- Decide between slaughter and split with the fission QoL test. The stayers' gain is the labour freed for weaving
  and building. The daughter must be better off at the pasture.
- A proportional split does **not** relieve herder pressure, since animals per person stay the same. That is why the
  daughter takes the surplus.
- Fission daughters copy the parent's `HerdTarget` scaled to their herd.
- Report `Trigger = Herd` on `FissionReport` (an additive optional enum value).
- Tests: closed forms, conservation, determinism, save, and that stayers' animals per person fall.

**HV5 — Turn on herd labour by default** once HV3 passes. Update `OpeningConfig.json`, the `Model.cs` default, the
contract row, `Docs/HerdLabour.md` status and AGENTS.md.

### 2. Enable the social-economy defaults

After HV5:
1. Turn on QoL mortality with `QoLMortalityElasticity = 1` and fission. Distress fission and the hinterland come with
   fission.
2. Sweep the split threshold G* (`FissionGainThreshold` and its policy variants) and the cohesion levers
   (`CohesionBandScale`, `CohesionHousingGain`, `LightHousingPermanence`) against the camp-size targets and total
   population. Use `Tools/PerformanceAudit/Summarize-Cohesion.py`.
   - Note: under a = 1, growth between the split size (0.458 N_s) and the stop size (0.54 N_s) is slow, about
     0.35%/yr against 1.145% at QoL 1, so splits slow down. A lower G* splits camps while growth is still fast and
     keeps sizes under the targets, since the targets are maxima.
3. `LightHousingPeoplePerUnit` (tent capacity) is **not** a lever. With tents at 2 instead of 3, the planner builds
   1.5× the tents and tent-housed share per head is unchanged (measured). Use `LightHousingPermanence` instead.
4. Decide immigration's default (`ImmigrationEnabled`, `Docs/Immigration.md`) with the same five-world measurements.

### 3. Hinterland follow-ups (`Docs/ContactKnowledge.md`, `Docs/Fission.md`)

- 57–68% of screened fission searches still have no legal candidate. A camp under 54 people, or one that just moved,
  knows only radius 2, and ring 3–4 tiles in settled land are often within spacing of a neighbour.
- Candidate levers: `HinterlandReferencePopulation` (24), `HinterlandRingsPerTurn` (1), `HinterlandMaxRadius` (6),
  `FissionRadius` (4). Measure before changing.
- Run the exact full-run fingerprint replay audit: `--fingerprints=1`, scalar against 4 workers. Not yet done for the
  hinterland change.
- Watercraft camps got bigger with the hinterland: median 372 → 514 with mortality off, 247 → 358 with a = 1.
  Investigate.

### 4. Food-frontier migration (decide by measurement)

The user proposed that crowding stands in for land pressure. That holds for size, but camps on poor land stop
growing at the food limit below the crowding threshold, never split, and so farming spreads only from rich-land
villages.
- Measure the count and duration of camps that stay food-limited (realised growth below the ceiling because of food)
  while below the crowding threshold.
- If they are common, add a frontier trigger: a food-limited camp sends a group to a reachable site offering more food
  per head.
- If they are rare, drop the idea and record why.

### 5. Trade (WP7–WP9; fully specified in `Docs/ValueModelImplementationPlan.md` and `Docs/QoLFissionTradeDesign.md`)

**Not started.** Today there are only inert hooks:
- `TechnologyDiscovery.RecordTradePractice` with `TradePracticePerValue` 0;
- `ContactKnowledge.RecordTrade`, which has no callers.

Order:
- WP7 trade foundation;
- WP8 handshake in the planning pass;
- WP9 food trade, with village differentiation and proto-towns.

Then WP11 calibration sweeps and WP12 contracts. The user wants trade evaluated together with population and QoL;
re-measure section 6's table after it lands.

### 6. Unity and presentation (no Unity in the cloud container; do these on the Windows workstation)

- Render remembered tiles, including fading memory.
- A fission policy inspector control.
- Journal entries for fissions (with trigger) and migrations.
- A `HerdTarget` control in the village inspector.
- Rebuild the player and run the EditMode tests (`AGENTS.md` "Build and validate"). The simulation-side text changes
  in `OpeningGame.cs`, `OpeningDetails.cs` and `OpeningVillageInspector.cs` are uncompiled in Unity.

### 7. Validation debt

- Scale runs (`-ScalePlayers 80/320`) after the social-economy defaults change.
- `-Standard -Geography` seeded suites.
- `Docs/Validation.md` entries for fission, hinterland, QoL mortality and herd labour.
- The cohesion derivation's +12% time and +30% allocation (`Docs/SocialCohesion.md`) were not isolated; profile them.

## 5. Measuring

All audits run **from the repository root** (the harness reads `OpeningConfig.json` relative to the working
directory).

```bash
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
dotnet build Tools/PerformanceAudit/Anthropocene.PerformanceAudit.csproj -c Release -o TestResults/<Pkg>/bin
for spec in Continents:1 Continents:2 Continents:3 Pangaea:1 Archipelagos:1; do sh=${spec%:*}; s=${spec#*:}
  dotnet TestResults/<Pkg>/bin/Anthropocene.PerformanceAudit.dll --players=80 --seed=$s --shape=$sh \
    --production-config=1 --turns=12 --post-opening-turns=50 --workers=1 --partition-size=64 --warmup=0 \
    --details=1 --metrics=0 --fingerprints=0 --fission=1 --qol-mortality=1 \
    --set=QoLMortalityElasticity=1 --herd-labour=1 \
    --output=TestResults/<Pkg>/runs/<tag>__${sh}${s}.json
done
```

- Other switches: `--immigration=0/1`, `--set=Name=Value` for any top-level or `Neolithic` config field.
- Run at most 3 in parallel with `--workers=1` on a 4-core container. One five-world set takes roughly 15–30 minutes.
- Summarizers in `Tools/PerformanceAudit/` (name runs `<tag>__<World>.json`):
  - `Summarize-Cohesion.py <dir> --tag-order a,b --techs`: size by housing class, QoL, score;
  - `Summarize-QoLMortality.py <dir> --tag t:1:0.045`;
  - `Summarize-DistressFission.py`, `Summarize-Hinterland.py` (funnel), `Summarize-HerdLabour.py`,
    `Summarize-FissionThroughput.py`.
- Focused suite: build `Tools/Headless` in Release into its default `bin/` (some checks locate `OpeningConfig.json`
  from the binary's directory), then run `dotnet Tools/Headless/bin/Release/net8.0/Anthropocene.Headless.dll 0`
  from the repository root. Diff against the previous run; only intended lines may change.
- Contract checker: `python3 Tools/Docs/Check-CurrentContract.py`.

## 6. Reference numbers (five Standard/80 worlds, turn 50 after the opening, fission on)

Hinterland change, before → after. Camp size and QoL are 25th percentile / median / 75th percentile.

| Configuration | Group | Camps | Camp size | QoL |
|---|---|---|---|---|
| Mortality off | Husbandry | 1,307 → 4,574 | 483/887/1,456 → 324/515/982 | 0.25/0.60/0.85 → 0.58/0.85/0.93 |
| Mortality off | Cultivation | 1,039 → 2,821 | 453/727/1,124 → 385/605/921 | 0.62/0.77/0.89 → 0.68/0.79/0.89 |
| Mortality off | Total population | — | 2.19 M → 5.26 M | — |
| a = 1 | Husbandry | 924 → 2,095 | 304/516/664 → 249/405/620 | 0.69/0.77/0.90 → 0.72/0.88/0.94 |
| a = 1 | Cultivation | 534 → 640 | 293/464/611 → 290/432/562 | 0.76/0.86/0.93 → 0.75/0.86/0.93 |
| a = 1 | Total population | — | 0.71 M → 1.20 M | — |

Herd labour forced on (fission off unless noted):
- Trapped camps: 44 → 0; with fission on, 156 → 0.
- Animals per person, median: 1.22 → 0.01.
- Husbandry QoL P25/median/P75: 0.09/0.67/0.76 → 0.71/0.84/0.94.
- Husbandry-first population with mortality off: −36%.
- On top of the hinterland change at a = 1: camps holding herds 3,342 → 119, population 2.09 M → 0.90 M.

Fission funnel, mortality off, before → after the hinterland change:
- screened 40,703 → 96,380;
- searches with no candidate 81% → 68%;
- with a destination 10.6% → 20.8%;
- splits 2,302 → 10,237.

Turn time, mortality off: 796 → 1,377 ms median (more camps). With a = 1: 423 → 571 ms.

## 7. Decision log (user decisions to respect)

- Clothing feedstock is funded before the reserve refill.
- Fission:
  - The push is crowding plus expected gain.
  - Daughters inherit the parent's status.
  - The stayers' housing relief is ignored (relief 0).
  - Fission stays opt-in for now.
  - It must be QoL-driven, not crowding alone: a starving camp's group flees to better ground.
- Immigration uses known settlements, including fission daughters. It has a forward-looking utility plus hysteresis.
- Husbandry settling:
  - Secondary technologies need Neolithic practice.
  - Herds are valued against fields.
  - Trade hooks exist for technology progress.
- Cohesion is derived from housing and settlement structure. The size numbers are sweep targets, not switches.
- QoL limits growth through **deaths, not births**. There is no reported non-starvation decline. Mortality and
  fertility hooks are left for medicine and later eras. Q* is derived from the rates, never an input.
- Tent capacity at 2 was tested and dropped, because the planner compensates.
- Hinterland knowledge grows for settled camps with population and time stationary. Nomads have decaying memory plus a
  small vision bubble.
- Herd labour:
  - Use controlled slaughter, plus fission from herder demand (HV4).
  - Ship the coupling opt-in and recalibrate the valuation (this document).
  - The recalibration must offset agriculture's long-run advantage with the herd's regenerative value, the payoff
    delay of cultivation, and husbandry's better use of marginal land.
- The 2–3× population increase from working fission: the user wants median population, QoL and trade status before
  deciding. Section 6 has the first two; trade is not built.
