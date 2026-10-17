# HV1/HV2 follow-on: implementation and decision evidence

Prepared on `features/herd-valuation`, from `features/neolithic-agricultural`
at `87f5a71`. HV2 is an implemented **experimental candidate** behind the
existing `HerdLabourEnabled` flag. HV1's unit derivation and conditional
proposals are in [HerdValuationGrounding.md](HerdValuationGrounding.md);
external numerical evidence remains unverified because source access is
blocked. No species rates, herder capacities, defaults or save formats changed.
HV3 calibration, HV4 surplus-herd fission and HV5 activation remain pending.

## What changed

`HerdLabour.Build` now compares retained annual yield against current carcass
value in the existing food buckets. For each management step:

```
g_eff = g * max(0, 1 - H/K)
L = min(field capital life, 1/(r + m - g_eff))
    (use the ceiling when the denominator is nonpositive)
keep = bucket_value(annual net yield) * L
slaughter = bucket_value(current carcass food)
annual net = max(0, keep - slaughter) / L
break-even labour price = annual net / (1 - saved harvest labour)
```

Carcass food uses turn-sized bucket quantities; ongoing yield uses annual
quantities. Instant carcasses fill after current stored food, whereas ongoing
yield fills after forecast forage supply. These are planning values, never
food receipts. A step whose carcass value exceeds keep value receives zero;
otherwise saved labour of at least one worker-year gives an infinite
break-even price. A running minimum preserves the staffing ladder's
nonincreasing prices. The existing seed floor is retained, including when
the seed step has zero value.

Production uses `m = 0`: managed animals have no natural age/death process.
Species growth, carcass and ecological requirements are count-weighted in the
existing herd forecast. Fully managed marginal animals use yield before the
whole herd's management penalty, while keeping the forecast's nutrition
penalty and displaced human forage. Otherwise an oversized under-managed
herd would price its first feasible crew using its old diluted average yield.
The existing grown-herd envelope still bounds the current target.

The field comparison reads each tile's first available priced expansion step,
including its retained horizon yield, build effort, upkeep and settling-cost
adjustment. Dividing discounted field payoff by build effort and multiplying
its annualized score by field capital life gives food per worker-year. This
replaces the established-farmer `FarmerProductivity` shortcut. Build labour
and recurring herd management have different temporal costs; a mandatory
calendar delay is not added on top of `IntensificationTurns` build work.

Local pasture is split into bands: cultivable footprint land with an available
field step uses the better field/gathering productivity; land without that
alternative uses gathering. The shared cultivability predicate excludes ice,
nontraversable land and land with no seasonal potential. Mobile camps also
consider visible outer pasture within vision/herd movement, at most 19 tiles.
That is a future movement option, not extra current forage. Biomass and
freshwater constrain an optimistic local carrying-capacity envelope; rival
future claims and distant hidden pasture do not enter it. Cheapest bands are
consumed first; a herder step can straddle bands.

Alternatives are frozen during the demand pass, like the other claim inputs,
to preserve the planner's monotone price search. **This is a bounded local
approximation to the design's at-price marginal field alternative**, not a
joint counterfactual reallocation of fields and pasture at every trial price.
Mixed-species yield/carcase values and whole-herd nutrition are averages;
slaughter still uses actual per-species carcasses. These approximations need
review before accepting calibration.

`HerdGrowthFood` no longer reduces the field food gap in capital mode, so
offspring are not also credited there. Physical grown-herd forecasts remain.
Flag-off and explicit Uniform plans retain their old growth credit and
unconditional herder reservation. The resolver's breeding cap, proportional
slaughter and carcass ledger are unchanged.

## Capital-life issue requiring a design decision

The literal specified cap prevents regenerative growth from changing capital
life in production. With `m = 0` and nonnegative `g_eff`:

```
1/(r - g_eff) >= 1/r
field life = 1/(ln(2)/CapitalHalfLifeYears + r) < 1/r
therefore L = field life for every nonnegative growth rate
```

At the shipped `r = 0.01` and half-life 80 years, `L = 53.578` years for both
zero and positive growth. A regression check records this explicitly. Removing
the duplicate growth credit is consequently conservative: the proposed
growth-capital premium is inert. Changing species growth still changes actual
animals and the grown forecast, but it cannot increase this capital-life term.

Recommendation: agree on a finite common valuation horizon and a discounted
growth/yield treatment, or an explicitly modeled managed-animal loss process,
before HV3. Do not invent a loss coefficient merely to evade the cap. A
growth-rate sweep alone cannot resolve this algebraic issue.

## Engineering contract

| Concern | Current candidate |
| --- | --- |
| Authoritative state | Existing camp target, herd counts, breeding remainder and realized Animal Food. New pasture bands, field metadata, species averages and capital diagnostics are transient; never saved, observed or hashed. |
| Time | One analytical turn forecast; quantities converted using the existing turn years/intervals. No time-step, per-season economic loop or per-year loop added. |
| Scaling | Added valuation work is bounded by local tiles plus field/herder ladder steps, `O(field steps + herder steps + 19)`, apart from the existing own-herd forecast. No new map scan or all-camp scan. |
| Mature game | Ladder still bounded by resident population; forecasts and metadata are per-plan. No persistent history or new observation expansion. Timing evidence below is diagnostic, not a formal performance acceptance. |
| RL and privacy | Same owner economy target/action (introduced at schema 18); the current overall observation schema is 22. Deterministic committed-state inputs; existing local visibility bounds. No public diagnostic exposes hidden pasture or species knowledge. |
| Compatibility | Same optional flag and target/save fields. Defaults off. Uniform and flag-off plans unchanged; old-save and save-round-trip checks retained. |

## Verification

Release headless build and full fixed suite pass. Herd-labour checks increased
from 986 to **1,157**, with all other PASS-line counts unchanged; summed fixed
assertions are **158,260** (baseline 158,089). The added checks cover capital
closed forms/cap degeneracy, equal field/herd values at the same real Value
price, order independence, steppe versus rich fields, pasture limits and
straddling bands, pure forecasts, growth-credit gating and marginal-management
yield. Existing trap, healthy herd, breeding, slaughter conservation, old-save
and replay checks remain passing. Settling checks remain 60.

A generated Small/4/Continents/53 geography with an explicitly injected
five-goat pastoral start ran **34 turns** with identical scalar and four-worker
fingerprints, reversed partitions/entity order and a save/load at turn 15.
The capital path ran all 34 turns and the world ended with eight camps,
exercising fission. This is a synthetic mechanism test, not a natural balance
cohort. The inherited full suite also retains its generated replay coverage.

Controlled mortality trap: control remains 23 people, grows from 1,396 to
2,402 animals over four resolutions and ends at QoL 0.148 with all 23 herding.
Candidate starts by retaining eight animals and four herders, reaches 28
people/12 animals/five herders and QoL 1.000 by the fourth resolution. Healthy
fixture retains 49 animals without slaughter. These are controlled fixtures,
not statistical gameplay predictions.

Unity/player validation was not run in this Linux headless environment. No
presentation or Unity API code changed. Logs and raw measurements are ignored
under `TestResults/HerdValuation/`; the reproducible settings and durable
summary follow below.

## Measurement protocol and results

The main comparison uses five Standard/80 worlds: Continents seeds 1/2/3,
Pangaea seed 1, Archipelagos seed 1; production configuration unchanged except
explicit diagnostic flags. Each completes the ten-turn opening (limit 12),
then 50 post-opening turns. Mortality is on at elasticity 1; fission is off.
Immigration stays off. Scalar worker count 1, partition 64, warmup 0,
details enabled, metrics/fingerprints disabled. Runs use .NET 8.0.31 on Debian
13, SDK 8.0.425, processor count 4. Three comparisons:

- `control`: pristine `87f5a71`, herd labour off.
- `old`: pristine `87f5a71`, original herd labour on.
- `hv2`: this candidate, herd labour on, all tuning coefficients unchanged.

Animal/herder statistics use camps holding managed animals, not every camp
whose first technology is Husbandry. QoL quartiles use husbandry-first
camp-turns 10–50; population is residents at the final boundary, excluding
linked hunters. Report final-turn and pooled camp-turn measures separately.
Timing excludes JSON/diagnostic collection and includes AI/prepare/execute.
Single passes without warmup/repeated pairing cannot certify the +5% budget.

### Five-world results: mortality a = 1, fission off

| Metric | Control (off) | Original coupling | HV2 candidate |
| --- | ---: | ---: | ---: |
| Total final population | 232,663 | 248,798 | 252,659 |
| Husbandry-first residents | 48,030 | 64,196 | 68,056 |
| Camps holding managed animals | 106 | 105 | 105 |
| All-residents-herding / trapped camps | 44 / 44 | 0 / 0 | 0 / 0 |
| Final animals/person median | 1.52 | 0.003 | 0.52 |
| Final animals/person P25 / P75 | 0.69 / 6.25 | 0.002 / 0.011 | 0.09 / 1.00 |
| Final herder-share P90 | 1.000 | 0.053 | 0.382 |
| Camp-turn animals/person median (turns 10–50) | 1.22 | 0.01 | 0.04 |
| Camp-turn herder-share P90 (turns 10–50) | 1.000 | 0.04 | 0.29 |
| Husbandry QoL P25 | 0.0925 | 0.7131 | 0.7057 |
| Husbandry QoL median / P75 | 0.67 / 0.76 | 0.84 / 0.94 | 0.81 / 0.93 |
| Husbandry-first villages / mobile camps | 50 / 56 | 95 / 11 | 93 / 13 |
| Slaughtered animals (all 50 post-opening turns) | 0 | 3,078 | 11,447 |
| Slaughter share of slaughter + renewable animal food | 0% | 8.1% | 8.1% |
| Animal Fabric made (turns 10–50) | 1,525,343 | 1,042,554 | 1,749,497 |
| Light Housing made (turns 10–50) | 40,590 | 43,647 | 48,527 |
| Final people sheltered in tents | 24,937 | 33,868 | 39,202 |
| Median measured turn time (ms) | 209.3 | 209.0 | 217.4 |

The candidate restores larger herds and more Animal Fabric/tent coverage
than the original coupling without restoring the mortality trap. Cultivation-
first final residents remain 171,491 (control 171,497); Watercraft remains
6,818 in all three runs. Their QoL distributions are unchanged at displayed
precision. This is route-associated evidence, not a site-matched causal
estimate: controls and candidates diverge after planning starts.

Several HV3 criteria already fail in this diagnostic slice:

- Herd-holding camps are within tolerance: 105 versus 106 (−0.9%).
- Husbandry-first residents exceed the ±20% control band: +41.7%.
- Final herder-share P90 0.3816 exceeds the approximate 0.35 ceiling.
  The pooled camp-turn P90 is 0.29; it does not erase the final-turn result.
- QoL P25 is 0.7057, **below** 0.71; rounding it to 0.71 would conceal the
  shortfall. It is also below the original coupling's 0.7131.
- Median time is +4.0% versus the original coupling in this single sample;
  this is not proof of the +5% acceptance requirement.
- The two fewer final villages versus the original coupling warrant later
  settling/adoption trajectory checks; passing mechanism checks is not a
  complete settling-balance regression assessment.

### Continents/1 spot checks

These rows each cover only one world. They do not establish five-world
crossed acceptance. Values are candidate versus control; timing is candidate
versus original coupling. QoL uses the same camp-turn definition above.

| Diagnostic | Mortality off, fission off | Mortality a = 1, fission on |
| --- | ---: | ---: |
| Total population, candidate / control | 80,436 / 88,158 | 197,167 / 382,220 |
| Husbandry-first residents, candidate / control | 30,804 / 38,530 (−20.05%) | 94,412 / 280,740 (−66.4%) |
| Herd-holding camps, candidate / control | 22 / 22 | 116 / 628 (−81.5%) |
| Trapped camps, candidate / control | 0 / 0 | 0 / 77 |
| Candidate final animals/person median | 0.22 | 0.01 |
| Candidate final herder-share P90 | 0.275 | 0.237 |
| Candidate husbandry QoL P25 | 0.466 | 0.819 |
| Median time, candidate / original coupling (ms) | 261.0 / 229.2 (+13.9%) | 291.8 / 286.7 (+1.8%) |

The fission-on case improves on the original coupling (116 versus 30
herd-holding camps, 94,412 versus 71,763 husbandry residents), but remains
well outside the control acceptance bands. It retains the original fission
rule and lacks HV4's surplus-herd transfer. Mortality-off QoL and the noisy
+13.9% timing sample also need attention in the eventual crossed audit.
**Recommendation: keep the lever off and do not declare HV3 acceptance.**


Regenerate with the existing `Tools/PerformanceAudit` binary using:

```bash
dotnet <audit.dll> --players=80 --seed=1 --shape=Continents \
  --production-config=1 --turns=12 --post-opening-turns=50 \
  --workers=1 --partition-size=64 --warmup=0 --details=1 \
  --metrics=0 --fingerprints=0 --herd-labour=1 --qol-mortality=1 \
  --fission=0 --set=QoLMortalityElasticity=1 --output=<tag>__Continents1.json
python3 Tools/PerformanceAudit/Summarize-HerdLabour.py <run-dir> \
  --tag control:1 --tag old:1 --tag hv2:1
```

Build the baseline binary from an isolated export of `87f5a71`; build the
candidate from this branch. Repeat for each listed world. Separate
Continents/1 spot checks use mortality off/fission off and mortality on/fission
on. These are diagnostics, not HV3's required five-world full 2×2 matrix.
Superseded measurements before the marginal-management correction are stored
separately under `BeforeManagementCorrection` and are not the reported result.

## Decisions before the next package

1. Verify source ranges and choose an animal-unit definition jointly with
   renewable yield, carcass value and grazing demand. Conditional unit/head
   arithmetic is not empirical evidence.
2. Resolve the cap/growth issue and review the frozen local marginal-field
   approximation before treating the candidate as the final HV2 rule.
3. Approve verified ranges before HV3 tuning, then run its complete crossed
   acceptance matrix. Keep defaults off until that work passes.
4. Treat surplus-herd fission as HV4: this candidate retains the existing
   proportional split behavior and does not add a new herd trigger.
