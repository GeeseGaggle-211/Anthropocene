# Immigration between known settlements (WP10b, forward-looking utility and hysteresis WP10b-2)

Design: [QoLFissionTradeDesign.md](QoLFissionTradeDesign.md) sections 4 and 9 (normative ordering and forbidden
architectures); order of work: [ValueModelImplementationPlan.md](ValueModelImplementationPlan.md) WP10.
Source: `Assets/_Anthropocene/Simulation/Immigration.cs` (pass, utility, size, reconcile, commit) and the
`Fission.QoLModel` estimator in `Fission.cs`. Checks: `Tools/Headless/ImmigrationChecks.cs`. Companion:
[Fission.md](Fission.md). Pass contract: [TurnResolutionPasses.md](TurnResolutionPasses.md), Close row.

**Status: opt-in.** `ImmigrationEnabled` is false by default and in `OpeningConfig.json`; saves that predate the
fields load with it false and the default levers. Immigration and fission are independent switches (either can run
alone); when both run, immigration goes first.

**WP10b-2 changed the decision rule** (utility and acceptance, below) after the WP10b measurements showed a proportional
controller on present states: 24% of flows reversed within two turns and QoL fell. The measurements of the original rule
are kept below as "previous rule"; the current rule's are in the WP10b-2 section.

A camp whose neighbour is a clearly better place to live may send a small group of its residents there. This is a
resolver population process like births, not an AI decision: no order, no player control, no observation. The owner
sees the result in the camp's demographic explanation ("... then moved to other settlements" / "... more arrived
from other settlements") and in the turn journal.

## Doctrine summary

- **Authoritative state.** No new persistent per-camp state. Immigration moves existing authoritative state: people
  (`Camp.Population`, the workforce ledger), the three typed food stores, and contact memory. New saved, optional
  records: `TurnRecord.Migrations` (`MigrationReport`, canonical order), `DemographicReport.Emigrated` /
  `Immigrated`, and `ContactSource.Migration`. WP10b-2 adds the only per-camp state: `Camp.RecentMigration`, a sparse
  optional list of (other camp, turn, direction) records that is pruned at every commit beyond the cooldown and is null
  when empty (O(flows x cooldown) in total; absent in older saves, which load with no memory). New config:
  `ImmigrationEnabled` and twelve levers (below), all `OptionalField`.
- **Temporal abstraction.** One evaluation per turn boundary from the frozen post-demography snapshot, in the Close
  phase before fission. QoL is the camp's committed terms evaluated at its present residents (the same formula as
  `Fission.EstimateSplit`'s `Q_now`, asserted equal) and is **not** recomputed after any transfer; the next turn's
  resolution recomputes it. Nothing is integrated over sub-turn time. The estimator is the only place that looks at a
  destination's true committed state, which a population process may do, exactly as demography reads true state.
- **Scaling law.** `T(N) = O(N + S_eval * r_imm^2 + I log M)`: O(N) QoL terms (one `Fission.QoLModel` per camp, each
  O(fixed-housing stocks + camps), shared with fission), one map-radius walk per eligible source (a source needs at
  least `ImmigrationSourceFloor + ImmigrationMinimumGroup` = 16 residents; each visited tile is a dictionary lookup
  and each camp on it one binary-searched contact test), and reconcile/commit proportional to the number of
  intents `I` (bisection over the group size, `M <= 0.1 N`). With `r_imm <= 5` this is O(N) in camps. Nothing scales
  with elapsed years, map size, or the number of Societies. Scoring runs on bounded workers
  (`PhaseExecution.Map`, pure over the snapshot); reconcile and commit are serial and canonical.
- **Mature-game and RL scale.** Measured (tables below): a median of 3.3 to 4.4 ms per turn at 120 to 440 camps
  (0.8% to 1.6% of turn time), 1.6 ms at 80 camps with no candidate; per person the turn costs the same with and without
  the pass (2.04 versus 2.03 ms per 1,000 people, turns 40 and later). The cost drivers are the per-camp
  `Fission.QoLModel` terms (their `FixedHousingAt` lookup scans `WorldState.FixedHousing`, O(S) per camp, shared with
  fission and **not profiled here**) and, per eligible source, about 60 tiles of candidate scanning. If the pass ever
  shows up in a profile, index fixed housing by camp first (it would speed fission too). At RL scale (many short games)
  the per-game cost is the same O(N) with N the number of camps.
- **Rejected architectures** (design 9, all honoured): all-pairs settlement comparisons (only known camps within
  `r_imm` are visited); iterative rerouting of rejected or excess migrants to a second-best camp (the excess stays
  home: tested with a valid second-best camp present); stable matching; min-cost flow; global market clearing;
  same-turn multi-hop (arrivals cannot emigrate or fission this turn); recomputing QoL after each transfer (one
  snapshot, one bisection per intent). Also rejected: making a destination's acceptance depend on later sources in
  canonical order, and committing arrivals before departures (the commit is departures first, then arrivals, from the
  frozen residents and stores, so the result does not depend on the order in which intents are applied).

## Rules

**Known settlements.** A source `i` considers camp `j != i` when `j` lies within `ImmigrationRadius` (default 4) hexes
and is known to `i`'s Society:

- the Society's own camps (the owner sees its own settlements);
- camps in current vision (any camp on a tile in the Society's current vision, which includes its hunter groups'
  vision), for every Society;
- after the first breakthrough only (`ContactKnowledge.Remembers`), a remembered `SettlementContact` that is present,
  not `Moved`, and whose recorded tile is the camp's actual tile. Contacts expire after `ContactExpiryTurns` as usual.
  This includes kin contacts recorded at fission and sightings. A contact whose camp has left its recorded tile is not
  found (the migrants would arrive at nothing); no unknown camp is ever a candidate.

Paleolithic Societies use current vision only and ignore stored contacts.

**Utility (WP10b-2, forward-looking).** For a group of `M` people moving from `i` to `j`:

```
dU(M) = w_Q * (Qhat_j(N_j + M) - Qhat_i(N_i - M)) - K_distance * d(i,j) - K_attachment - K_lifestyle - ReturnPenalty
```

`Qhat` is `Fission.QoLModel.ForwardAt(headcount, carried)`, the same pure estimator family as fission (committed terms
F, W, expectation, clothing, housing supply and cohesion; `ForwardAt(N, 0) = At(N)` for valid ratios), extended with a
**food and water term** because the headcount change dilutes (or relieves) them:

```
F_hat(N') = min(1, (F * N + carried) / N')          N = the camp's present residents, N' = the evaluated headcount
W_hat(N') = min(1, W * N / N')  if W < 1, else 1     (same dilution rule, no carried term)
Qhat(N')  = F_hat * W_hat * coverage(N') * S(N')     coverage: clothing and housing per head, S: cohesion, as before
```

**Housing in the cohesion term.** `S(N')` uses the derived scale `N_s = N_band (1 + k H(N'))` with
`H(N') = min(1, (fixed people + p_tent * tent people) / N')` ([SocialCohesion.md](SocialCohesion.md)). Housing stays with the camp:
arrivals bring none, so the destination's `H` is diluted by headcount (they have to be housed), and the source's `H` rises as
it loses people (they leave their housing behind). Both come out of the one formula evaluated at each camp's own headcount;
the estimator reads the camp's current capacity once per pass (`QoLModel`), never a technology label.

`carried` is the food the migrants bring, in person-equivalents of the turn's need: `M * s`, where
`s = (PlantFood + AnimalFood + AquaticFood) / N_i / (Consumption * TurnDays / BaseDays)` is the source's stored food per
head over the per-head requirement (80 at the default duration; `Immigration.CarriedPerPerson`). It is nonzero only
for the destination: the source's leavers take their per-head share of stores, so per-head stores there are unchanged
and `carried = 0` for the source. So a source with `F < 1` gains from shrinking (`F_hat` rises up to 1, cohesion and
clothing per head improve), a destination loses to dilution unless the migrants bring at least their own need
(`s >= 1` leaves a fully fed destination undiluted), and neither estimate changes the camp's committed state. `Q` for the
report (`SourceQoL`, `DestinationQoL`) is still the present-state `At(N)`.

`K_lifestyle` is `ImmigrationToSedentaryCost` when a nomadic camp
moves to a village (`Camp.IsVillage`), the larger `ImmigrationToNomadicCost` when a village moves to a nomadic camp,
and zero between camps of the same lifestyle. `ReturnPenalty` is the hysteresis term below.

**Hysteresis.** Each camp keeps `RecentMigration` records: (other camp ID, turn, direction), +1 for a flow it sent and -1
for one it received, one per (other, direction) (the latest), written at commit to both ends. A flow from `j` back to
`i` where `i` sent people to `j` `age` turns ago (age = turns since, 1 to `ImmigrationReturnCooldownTurns`, default 3)
pays

```
ReturnPenalty = ImmigrationReturnPenalty * (cooldown - age + 1) / cooldown      (0.10, 0.067, 0.033 at ages 1, 2, 3; 0 after)
```

as an extra cost inside `dU`, so a reversal needs `dU > theta + penalty` (it also sizes the group through the ramp). A
repeat of the same direction pays nothing. Records older than the cooldown are dropped at every commit (a serial scan
of the camps that hold any, so the memory is bounded by recent flows, not history) and a camp's list returns to null
when empty. Cooldown 0 or penalty 0 disables the term.

| Lever | Default | Design sweep | Meaning |
| --- | ---: | --- | --- |
| `ImmigrationRadius` | 4 | 3 / 4 / 5 | hexes searched (r_imm), validated 1 to 5 |
| `ImmigrationQualityWeight` | 1 | | w_Q |
| `ImmigrationDistanceCost` | 0.01 | | K_distance per hex |
| `ImmigrationAttachmentCost` | 0.02 | | K_attachment |
| `ImmigrationToSedentaryCost` | 0.05 | 0 / 0.05 / 0.10 | nomadic camp to village |
| `ImmigrationToNomadicCost` | 0.15 | 0.10 / 0.15 / 0.25 | village to nomadic camp |
| `ImmigrationDeadband` | 0.075 | 0.05 / 0.075 / 0.10 | theta: the utility a move must exceed, and the ramp width of the group size |
| `ImmigrationMaxFraction` | 0.10 | | largest fraction of residents that leaves in one turn |
| `ImmigrationMinimumGroup` | 4 | | smallest migrating group |
| `ImmigrationSourceFloor` | 12 | | residents a source always keeps |
| `ImmigrationReturnCooldownTurns` | 3 | | turns a reversal is penalised (0 disables), validated 0 to 20 |
| `ImmigrationReturnPenalty` | 0.10 | | the penalty at age 1, validated 0 to 5 |

`ImmigrationEnabled` (false) is the switch. The group-size levers and the two hysteresis levers are not in the design
table; they are uncalibrated placeholders (the hysteresis pair has not been swept).

**One intent per source.** Among the valid known destinations the source compares `dU` evaluated at its largest group
`M_max = min(floor(N * ImmigrationMaxFraction), N - ImmigrationSourceFloor)` (the source needs `M_max >=
ImmigrationMinimumGroup`; ties: higher `dU`, nearer, lower camp ID). The best `dU(M_max)` sets the size by the ramp;
the group is then **re-evaluated at its final size** and emitted only if `dU(M) > theta`:

```
M = floor(N * ImmigrationMaxFraction * clamp((dU(M_max) - theta) / theta, 0, 1))   (plus 1e-9 so exact ramp points are not lost to rounding)
M = min(M, N - ImmigrationSourceFloor);   M < ImmigrationMinimumGroup  ->  no intent;   dU(M) <= theta  ->  no intent
```

The ramp starts at zero at the deadband and reaches the maximum fraction at `2 * theta`. With the carried food
increasing in `M`, a smaller group can be worth less to a hungry destination than `M_max` was (the re-evaluation
matters exactly then; without carried food `dU` falls with `M`, so the final check is slack). No fallback to a
second-best destination when the best yields no group.

**Reconcile once, canonically** (destination camp ID, then source camp ID). A source is accepted for the **largest**
group `m <= M` such that `dU(m)` evaluated with the destination's headcount `N_j + inflow_so_far`, the food already
arriving from earlier sources (their `carried`) and this source's `m * s`, stays above `theta` (return penalty included),
found by bisection (monotone because dilution and cohesion dominate; the closed form and a linear scan agree in the
fixtures). The excess of a partly accepted source, and all of a fully rejected one, stays home; nobody is rerouted. A
partly accepted group is not subject to the minimum group (only the intent is). Under `PhysicalMortalityV1`, the
unchanged QoL-selected/reconciled group also receives explicit physical mortality and net-rate diagnostics. The
projection uses destination population, diluted clothing, carried food and the population-weighted deficit ages of
prior arrivals; it never uses composite QoL as mortality. A negative instantaneous net rate is recorded by
`ImmigrationPhysicalDestinationNegativeNet` but does not block, shrink or reroute the group: above-equilibrium decline
and recoverable distress are not physical nonviability. The utility, ranking, deadband, size ramp and hysteresis remain
unchanged.

**Commit, simultaneously and canonically.** All departures from the frozen residents and stores first, then all
arrivals. The migrants of a flow carry their per-capita share (`m / N_i` of the frozen stock) of the source's typed
Plant, Animal and Aquatic food and no material goods: no herds, tools, housing or fabric. In physical mode they also
carry their proportional gross-birth/death progress and their deficit age/severity; the receiver combines deficit
history by population. Legacy mode retains the historical initialization semantics.
The source's workforce ledger loses the emigrants across its roles in proportion (`PopulationLedger.ApplyCasualties`,
largest remainder); arrivals join the destination as gatherers (the ledger assigns the remainder to gathering).
Migrants join the destination's Society (there is no separate migrant record). Every people and food ledger is
conserved exactly (asserted per camp per type). Both Societies record the other camp's current location as a
`ContactSource.Migration` contact (`ContactKnowledge.RecordMigration`; a Paleolithic Society remembers nothing, as
everywhere). `ContactSource` gained a fourth value, which also appears in the observation's contact DTO; the
observation schema number is unchanged (the new value is reachable only with immigration enabled).

**Ordering with fission.** Immigration runs first from the same snapshot. A source that emitted an intent (whether or
not the destination accepted any of it) emits no fission intent that turn, and a destination that received arrivals
does not fission either (no same-turn multi-hop): `Immigration.Resolve` returns those camps and
`Fission.Resolve(..., excluded)` skips them and leaves their cached destinations untouched. Fission then evaluates
the camps on the population remaining after approved outflow; since the excluded camps are exactly the ones whose
population changed, every other camp is evaluated on its unchanged snapshot. QoL is recomputed next turn.

**Journal and observations.** `TurnRecord.Migrations` holds one `MigrationReport` per committed flow (turn, source
and destination camp and Society, residents before, intended and migrated people, distance, kin flag, `Q_i`, `Q_j`,
the forward `dU` at the committed group, the destination's forward post-inflow QoL, the typed food carried, the return
penalty paid and the person-equivalents carried, plus projected source/destination mortality, net rates, receiver
net-rate sign and the higher-QoL/worse-mortality flag), in destination then source order, plus
one journal line. Observations carry nothing new beyond what an owner sees of its own camps. No Unity change.

## Validation

`ImmigrationChecks.cs` (registered in `Anthropocene.Headless.csproj` and the fixed-suite list, 352 assertions, own
worlds): defaults, validation (including the cooldown and penalty), serialization and old-config loading,
`OpeningConfig.json` keys; the closed-form present-state utility for every barrier; the size formula exhaustively over
N = 0 to 400 and `dU` = 0 to 0.40 and its floors; estimator equality with `EstimateSplit` and monotonicity;
**forward estimator closed forms** (food and water dilution, carried food, the cap at one, water at one undiluted,
no residents, monotonicity without carried food; `CarriedPerPerson`, `MaxGroup`; the forward utility against an
independent closed form with inflow and carried food; a leaving source gains, a destination dilutes, carried food
offsets); unknown settlements never targeted (Paleolithic vision only, stored contacts ignored, hunter vision counts,
unrecorded, Moved and mislocated contacts rejected, radius, kin contacts, own Society, no self target); one intent per
source with tie-breaks, distance and lifestyle barriers; the deadband swept against an independent expectation; sizes
from the ramp at the largest group and **re-evaluation at the final size** (a fixture where carried food makes the
ramped group worth less than the largest and no intent is emitted); destination acceptance against an independent linear scan with inflow and
carried food, partial and full rejection, a valid second-best camp left untouched (no rerouting); **the return penalty
table and its decay** (ages 1 to 4, direction, other camp, cooldown 0, configured cooldown and size) and **its effect on
a reversal** (blocked at age 1, released as it decays; the report carries the penalty paid); **memory written at both
ends, sparse, pruned at commit (age 7 dropped, age 3 kept), back to null, bounded over a 40-turn swap**; **the oscillation
fixtures** (two camps with alternating harvests: the previous rule reimplemented in the test swaps every turn, 30 flows
and 29 reversals in 30 turns; the forward rule alone still swings, because next season's harvest is unknowable; with the
return penalty reversals and people moved fall by more than half; a mild swing (130 / 80) that the previous rule
swaps every turn moves nobody under the forward rule; a stable gap still moves people one way and never back);
immigration precedes fission and excludes sources and arrivals; no multi-hop (A to B while B to C); exact conservation of
people and each typed food per camp, no goods or herds move; physical gross-event/deficit ledgers transfer without
creation; higher-QoL/worse-mortality attraction remains possible when physical net growth is nonnegative, while a
negative-net receiving settlement blocks the otherwise unchanged group; demographic lines, journal, contacts in both
directions; scalar versus reversed four-worker replay with reordered collections over five turns of an eight-camp world
and over a generated world with both switches on (the fingerprint includes the memory); save continuation through a
migration with memory, the memory round trip, an older camp and an older report loading without the new fields.

Most WP10b fixtures were rebuilt for the forward rule: `Tune` gives the source a low food ratio and no stores and sets
the destination's ratio (within 0 to 1, the estimator caps F at one) so that the forward utility at the source's largest
group equals a chosen value; `Fwd` is an independent closed form of `dU(M)`. One WP10b check was dropped: an exact utility
tie between destinations at different distances (it needed a floating-point coincidence; the nearer-wins case is now
tested as a utility difference, and equal-distance ties still go to the lower ID).

## Measurements

The first subsection records the original present-state rule (WP10b); the second the forward-looking rule with
hysteresis (WP10b-2), rerun on the same five worlds.

### Previous rule (WP10b)

Container: 4 cores, .NET 8, `Tools/PerformanceAudit` at the WP10b working tree, `--workers=4 --partition-size=64
--warmup=0 --details=1 --metrics=0 --fingerprints=0 --production-config=1`, Standard/80 tribes, 12 requested
opening turns then 50 post-opening turns, five worlds (Continents 1, 2, 3, Pangaea 1, Archipelagos 1). Raw reports
(ignored): `TestResults/WP10b/{both,imm,fis}-<world>.json`; `off` is the earlier `TestResults/Spacing/<world>.json`.
Reproduce with `TestResults/WP10b/run.sh`-style commands:

```
dotnet build -c Release Tools/PerformanceAudit/Anthropocene.PerformanceAudit.csproj -o TestResults/WP10b/bin
dotnet TestResults/WP10b/bin/Anthropocene.PerformanceAudit.dll --players=80 --seed=S --shape=SH --production-config=1 \
  --turns=12 --post-opening-turns=50 --workers=4 --partition-size=64 --warmup=0 --details=1 --metrics=0 \
  --fingerprints=0 --fission=F --immigration=I --output=TestResults/WP10b/<mode>-SH-S.json
```

`both` is `--fission=1 --immigration=1`, `imm` is `--fission=0 --immigration=1`, `fis` is `--fission=1
--immigration=0` (the current build's fission-only control), `off` is neither.

#### Results of the previous rule

Counts over the whole run (62 turns, 60 or 61 turns measured after the 10 or 11 opening turns). Flows are committed
`MigrationReport`s; "kin" is first-degree lineage (parent, daughter or sibling Societies), "non-kin" everything else.

| World | Mode | Flows (per turn) | Migrants | Kin / non-kin migrants | Camps (final) | Population (final) | Fissions |
| --- | --- | ---: | ---: | --- | ---: | ---: | ---: |
| Continents 1 | off | 0 | 0 | | 80 | 87,859 | 0 |
| | fission only | 0 | 0 | | 462 | 553,092 | 382 |
| | immigration only | 0 | 0 | | 80 | 87,859 | 0 |
| | both | 1,570 (26.2) | 155,965 | 155,029 / 936 | 437 | 547,495 | 357 |
| Continents 2 | off | 0 | 0 | | 80 | 81,025 | 0 |
| | fission only | 0 | 0 | | 446 | 478,646 | 366 |
| | immigration only | 0 | 0 | | 80 | 81,025 | 0 |
| | both | 1,318 (22.0) | 125,106 | 124,434 / 672 | 418 | 466,498 | 338 |
| Continents 3 | off | 0 | 0 | | 80 | 80,878 | 0 |
| | fission only | 0 | 0 | | 386 | 422,739 | 306 |
| | immigration only | 0 | 0 | | 80 | 80,878 | 0 |
| | both | 1,093 (18.2) | 107,474 | 106,751 / 723 | 365 | 412,609 | 285 |
| Pangaea 1 | off | 0 | 0 | | 80 | 90,798 | 0 |
| | fission only | 0 | 0 | | 406 | 481,353 | 326 |
| | immigration only | 0 | 0 | | 80 | 90,798 | 0 |
| | both | 1,073 (17.6) | 110,071 | 109,861 / 210 | 386 | 467,867 | 306 |
| Archipelagos 1 | off | 0 | 0 | | 80 | 87,223 | 0 |
| | fission only | 0 | 0 | | 319 | 327,892 | 239 |
| | immigration only | 0 | 0 | | 80 | 87,223 | 0 |
| | both | 906 (14.9) | 86,001 | 82,380 / 3,621 | 339 | 346,639 | 259 |
| **Total** | off / fission only / immigration only / both | 0 / 0 / 0 / **5,960** | **584,617** | **578,455 (98.9%) / 6,162 (1.1%)** | 400 / 2,019 / 400 / 1,945 | 427,783 / 2,263,722 / 427,783 / 2,241,108 | 0 / 1,619 / 0 / 1,545 |

The opening ended on the same turn in every mode and world (10, 10, 10, 11, 11). The first flow occurs on turn 25 to 27
(the first daughter camps exist from turn about 20), so the opening is untouched.

Findings:

- **Immigration alone does nothing in these worlds, by construction.** The 80 founding camps start at least
  `StartSeparation = 12` hexes apart and the radius is 4: the immigration-only runs together scored one candidate
  camp in 23,771 eligible-source evaluations and produced the same outcomes as `off` to the last person in
  every world (table). Immigration only has neighbours once fission (or later trade or construction) places camps
  within four hexes.
- **With fission the flows are almost entirely kin.** 98.9% of migrants move between parent, daughter or sibling camps:
  a daughter is founded at the minimum spacing of three hexes (5,679 of 5,960 flows are at distance 3, 281 at
  distance 4), inside the parent's radius, and is remembered as a kin contact. The 1.1% non-kin flows are mostly
  Archipelagos (3,621 migrants, where different tribes' camps share islands). A typical flow is 97 people (P25 55,
  P75 133), 9.9% of the source's residents at the median (at or just under the 10% cap for most flows: utility
  reaches `2 theta` easily at these QoL gaps). Of 6,010 intents, 473 were partly accepted and 50 rejected outright by the
  destination cap; none is rerouted.
- **People move from the larger camp to the smaller one.** In 4,688 of 5,960 flows (78.7%) the source had more
  residents than the destination at the decision. Social cohesion falls with headcount, so a large parent rates its
  small daughter higher and sends people up to the `Q_i + theta` acceptance limit (the likely mechanism; the other
  21.3% are flows toward a larger camp that has better food, goods or housing).
- **Flows oscillate.** 1,448 of the 5,960 flows (24%) reverse a flow of the same pair within two turns and 2,172 (36%)
  repeat the same pair on consecutive turns; the busiest pair moved people 32 times. The deadband bounds the
  single-step overshoot but not the turn-to-turn dynamics of the terms the estimator holds fixed (F and W are
  unchanged by design; growth, goods and food change them by the next turn): across 3,858 flows the destination's QoL
  on the next turn was a median +0.014 (P25 -0.198, P75 +0.071) from the post-inflow estimate, so the estimator is
  unbiased at the median but a quarter of the destinations end up more than 0.2 worse than estimated. Candidate
  levers, none applied (they need their own calibration): a wider deadband (design sweep 0.10), a per-pair cooldown
  or direction memory, or diluting F with the headcount where the destination is food-limited.
- **Fission is reduced slightly** (1,545 versus 1,619, -4.6%; Continents -25, -28, -21, Pangaea -20, Archipelagos
  +20): sources that emit an intent and camps that receive arrivals sit out fission that turn, and moving people
  out of the largest camps lowers the crowding that drives fission. Camps 1,945 versus 2,019 (-3.7%), population
  2.241 million versus 2.264 million (-1.0%).
- **Quality of life is lower with immigration, not higher.** Final-turn camp QoL P25 / median / P75 (pooled over the
  five worlds; population P25 / median / P75 alongside):

| Mode | Camps | Agricultural (Cultivation): n, population, QoL | Husbandry (AnimalHusbandry): n, population, QoL |
| --- | ---: | --- | --- |
| off | 400 | 280; 480 / 781 / 1,254; 0.534 / 0.712 / 0.907 | 106; 1,050 / 1,748 / 2,172; 0.205 / 0.341 / 0.639 |
| fission only | 2,019 | 909; 556 / 883 / 1,263; 0.521 / 0.690 / 0.856 | 1,076; 724 / 1,222 / 1,806; 0.352 / 0.619 / 0.846 |
| immigration only | 400 | identical to off | identical to off |
| both | 1,945 | 891; 584 / 913 / 1,267; 0.502 / 0.662 / 0.829 | 1,021; 766 / 1,295 / 1,839; 0.305 / 0.494 / 0.803 |

  The population-weighted mean QoL of the final turn falls from 0.617 to 0.605 (agricultural) and from 0.442 to 0.403
  (husbandry) and the harness's per-world weighted QoL from 0.508 to 0.477 (mean of five). The pass moves people to
  camps it estimates to be better, but the estimator holds food and water coverage fixed, so arrivals that exhaust a
  destination's food or goods lower its next-turn QoL (the oscillation above); it does not raise welfare on this
  metric. This is a calibration finding for WP11, not a defect in the mechanism: the gap and the accept-at-estimate
  rule are as specified.
- **Cost.** Fission-only versus both, turns 40 and later: median turn time 613 ms versus 622 ms, 1.905 versus 2.018 ms
  per camp, 2.031 versus 2.042 ms per 1,000 people. The pass itself (`ImmigrationMilliseconds`): median 4.4, 3.9, 3.8,
  3.7, 3.3 ms per turn (Continents 1 to 3, Pangaea, Archipelagos; turns 30 and later, 120 to 440 camps, 31 to 55
  intents per turn), 0.8% to 1.6% of total turn time including spikes (isolated turns of 16 to 119 ms that also occur in
  the fission pass and in the immigration-only runs at 80 camps with no work, so they are not work-proportional;
  attributed to GC pauses, not profiled). The immigration-only pass costs a median 1.2 to 1.7 ms at 80 camps
  (QoL terms and the planning index, no intents). Median full-run turn times (all turns, `off` / fission only /
  immigration only / both, ms): Continents 1 227 / 386 / 227 / 415, Continents 2 212 / 387 / 226 / 436, Continents 3
  223 / 413 / 233 / 433, Pangaea 215 / 372 / 235 / 393, Archipelagos 188 / 330 / 194 / 352 (the differences between
  `both` and fission only are dominated by where each run's camps are at a given turn and by unrelated noise, not by the
  pass; the `off` runs come from the earlier Spacing build).
- **Memory.** Managed heap after the run, fission only versus both (MiB): Continents 1 673 / 731, Continents 2 584 /
  556, Continents 3 564 / 603, Pangaea 543 / 534, Archipelagos 486 / 522; peak during the run 706 / 729, 716 / 685,
  704 / 711, 664 / 709, 594 / 584. The pass holds no persistent state beyond the journal (one `MigrationReport`, about
  150 bytes, per flow: 5,960 flows in all) and contact entries; differences are GC timing.

Work counts (both, all worlds): 53,192 eligible-source evaluations, 32,383 candidate camps scored (0.6 per
evaluation), 6,010 intents.

Not measured: 320 tribes, 2048 x 1024 worlds, immigration with the Elder, trade or later eras (none exist yet), and any
change to the shipped defaults; `ImmigrationEnabled` stays false.

### WP10b-2: forward-looking utility and return hysteresis

Same container, tool, flags and five worlds as above, rerun with this build: `v2-both` is `--fission=1 --immigration=1`
(forward utility, cooldown 3, penalty 0.10), `v2-fis` is `--fission=1 --immigration=0` (identical to the earlier `fis`
outcomes to the last person, as it must be). Raw reports (ignored): `TestResults/WP10b/v2-{both,fis}-<world>.json`
(driver `run2.sh`; reduced by `analyze2.py`, which also reduces the earlier `both`/`fis` runs). The audit's
per-flow event record does not carry `Penalty`/`Carried`, so penalty statistics come from the checks, not these runs.

| World | Rule | Flows | Migrants | Camps (final) | Population (final) | Fissions | First flow turn |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Continents 1 | fission only | 0 | 0 | 462 | 553,092 | 382 | |
| | previous rule | 1,570 | 155,965 | 437 | 547,495 | 357 | 25 |
| | **forward + hysteresis** | **211** | **18,005** | 464 | 545,042 | 384 | 29 |
| Continents 2 | fission only | 0 | 0 | 446 | 478,646 | 366 | |
| | previous rule | 1,318 | 125,106 | 418 | 466,498 | 338 | 25 |
| | **forward + hysteresis** | **135** | **10,126** | 426 | 465,674 | 346 | 31 |
| Continents 3 | fission only | 0 | 0 | 386 | 422,739 | 306 | |
| | previous rule | 1,093 | 107,474 | 365 | 412,609 | 285 | 26 |
| | **forward + hysteresis** | **139** | **10,209** | 385 | 423,529 | 305 | 27 |
| Pangaea 1 | fission only | 0 | 0 | 406 | 481,353 | 326 | |
| | previous rule | 1,073 | 110,071 | 386 | 467,867 | 306 | 27 |
| | **forward + hysteresis** | **156** | **13,698** | 401 | 476,133 | 321 | 30 |
| Archipelagos 1 | fission only | 0 | 0 | 319 | 327,892 | 239 | |
| | previous rule | 906 | 86,001 | 339 | 346,639 | 259 | 26 |
| | **forward + hysteresis** | **115** | **8,553** | 325 | 332,404 | 245 | 31 |
| **Total** | fission only / previous / **forward + hysteresis** | 0 / 5,960 / **756** | 0 / 584,617 / **60,591** | 2,019 / 1,945 / **2,001** | 2,263,722 / 2,241,108 / **2,242,782** | 1,619 / 1,545 / **1,601** | |

| Metric (pooled over the five worlds) | Previous rule | Forward + hysteresis |
| --- | ---: | ---: |
| Flows / migrants | 5,960 / 584,617 | 756 / 60,591 (-87% / -90%) |
| Kin share of migrants | 98.9% | 96.5% |
| Flow size P25 / median / P75 (people) | 55 / 97 / 133 | 27 / 64 / 113 |
| Flows reversing a same-pair flow within 1 turn | 1,080 (18.1%) | 51 (6.7%) |
| Flows reversing within 2 turns | 1,448 (24.3%) | 65 (8.6%) |
| Flows repeating the same directed pair on the next turn | 2,172 (36.4%) | 186 (24.6%) |
| Busiest pair, flows per world (Continents 1, 2, 3, Pangaea, Archipelagos) | 26 / 21 / 18 / 32 / 23 | 17 / 8 / 9 / 30 / 9 |
| Estimator error: next-turn destination QoL minus the post-inflow estimate, P25 / median / P75 | -0.089 / +0.031 / +0.075 (n = 5,684) | -0.199 / +0.009 / +0.053 (n = 738) |
| Mean absolute estimator error; destinations more than 0.2 worse than estimated | 0.110; 19.6% | 0.117; 24.7% |
| Intents / partly accepted / rejected by the destination | 6,010 / 473 / 50 | 758 / 9 / 2 |
| Mean weighted QoL at the end (harness metric, mean of five worlds) | 0.477 | 0.507 (fission only 0.508) |

(The earlier document quoted the previous rule's estimator error as +0.014 (-0.198 / +0.071) from a different
pairing of flow and next-turn camp; the row above is recomputed with `analyze2.py` for both columns, flow at turn `t`
against the destination's committed QoL in turn `t + 1`, with the same estimator definition as the rule in force, so
the two columns are comparable to each other, not to the earlier text. The forward estimate includes food and water
dilution, which the previous estimate held fixed.)

Final-turn camp QoL and population, P25 / median / P75, pooled over the five worlds:

| Mode | Camps | Agricultural (Cultivation): n, population, QoL | Husbandry (AnimalHusbandry): n, population, QoL |
| --- | ---: | --- | --- |
| fission only | 2,019 | 909; 556 / 883 / 1,263; 0.521 / 0.690 / 0.856 | 1,076; 724 / 1,222 / 1,806; 0.352 / 0.619 / 0.846 |
| previous rule | 1,945 | 891; 584 / 913 / 1,267; 0.502 / 0.662 / 0.829 | 1,021; 766 / 1,295 / 1,839; 0.305 / 0.494 / 0.803 |
| **forward + hysteresis** | 2,001 | 900; 559 / 872 / 1,273; **0.525 / 0.691 / 0.846** | 1,067; 721 / 1,226 / 1,810; **0.352 / 0.612 / 0.846** |

Findings:

- **The churn is gone and QoL is restored.** Flows fall 87%, reversals within two turns from 24.3% to 8.6% (within one
  turn from 18.1% to 6.7%), the busiest pair in four worlds from 18 to 32 moves to 8 to 17 (Pangaea 1 still has a pair
  that moved people 30 times; its reversals are rare, so that is mostly repeated one-way moves). Median QoL matches
  the fission-only control in both groups (agricultural 0.691 versus 0.690; husbandry 0.612 versus 0.619, against
  0.662 and 0.494 with the previous rule), and the harness's weighted QoL is 0.507 versus 0.508.
- **Fission recovers** (1,601 versus 1,545, against 1,619 without immigration) and so do camps (2,001) and population
  (2.243 million versus 2.241 million and 2.264 million): far fewer camps sit out fission.
- **What the rule now does.** With the forward term most previous flows stop: moving 10% of a large parent to a small
  daughter trades the destination's dilution of food, clothing and housing per head (arrivals bring only their share of
  stores, no cloth, no housing) against the source's relief, and that is rarely worth more than the deadband plus the
  barriers. The flows that remain (first flow turn 27 to 31, later than before) are the ones with a real food or crowding
  gap; 96.5% are still kin, because daughters are the camps inside the radius.
- **The estimator is still loose in the tail.** Median error is +0.009 (was +0.031) and the IQR is -0.199 to +0.053, but
  24.7% of the 738 destinations ended more than 0.2 below the estimate (19.6% of 5,684 before). That tail is food or
  goods shocks in the destination's own resolution in the next turn, which no one-snapshot estimate can see; with 87%
  fewer flows they no longer move the pooled QoL, but the estimator error has not been reduced. Sample sizes are
  small (738 flows, 5 worlds, one seed each); treat the percentages as indicative.
- **Cost unchanged.** Pass median 4.2, 3.8, 3.4, 3.4, 2.8 ms per turn (turns 30 and later; previously 4.4, 3.9, 3.8, 3.7,
  3.3). Median turn time at turn 40 and later, fission only versus forward + hysteresis (ms, Continents 1 to 3,
  Pangaea, Archipelagos): 739 / 677 / 621 / 598 / 409 versus 775 / 654 / 605 / 579 / 455; per 1,000 people
  1.964 versus 1.991 ms (previous rule 2.084): within run-to-run noise. Work added per source: one extra `ForwardAt` per
  candidate (the `QoLModel` per camp is built once per turn and shared by the reconcile), one memory scan per candidate
  (lists of at most a few records) and an O(camps) prune; no allocation beyond the per-camp lists.

Not measured: a hysteresis or deadband sweep (cooldown, penalty, theta stay at their placeholders), other seeds, 320
tribes, and anything past turn 62.

## Deliberate limits and remaining work

- Opt-in and uncalibrated: the design sweeps (deadband 0.05 to 0.10, lifestyle barriers, radius 3 to 5, cooldown, penalty) have
  not been run. The forward rule with hysteresis (WP10b-2) removed the previous rule's oscillation and QoL loss in the five
  measured worlds; the estimator's tail error remains.
- The forward estimator has no knowledge of next turn's harvest: alternating good and bad seasons still swing flows, and
  only the return penalty damps the swing (fixture). A source's food estimate assumes the committed F is representative.
- Destination acceptance is checked against the forward estimate (food and water diluted with headcount, plus the carried
  food), not against what the destination's next resolution will actually produce.
- The opportunity term (profession-specific pull) is zero by design. Affinity is inert: kin flows are not treated
  differently from other flows except in the report's kin flag.
- Known camps use true current positions only through the contact's recorded tile; a contact whose camp moved is not
  a destination until a sighting refreshes it.
- Unity: no journal display of migrations, no display of the demographic lines beyond the existing explanation string,
  no drawing of flows; `OpeningGame` has not been rebuilt. The simulation-side APIs (`TurnRecord.Migrations`,
  `DemographicReport.Emigrated`/`Immigrated`) are complete.
