# Immigration between known settlements (WP10b)

Design: [QoLFissionTradeDesign.md](QoLFissionTradeDesign.md) sections 4 and 9 (normative ordering and forbidden
architectures); order of work: [ValueModelImplementationPlan.md](ValueModelImplementationPlan.md) WP10.
Source: `Assets/_Anthropocene/Simulation/Immigration.cs` (pass, utility, size, reconcile, commit) and the
`Fission.QoLModel` estimator in `Fission.cs`. Checks: `Tools/Headless/ImmigrationChecks.cs`. Companion:
[Fission.md](Fission.md). Pass contract: [TurnResolutionPasses.md](TurnResolutionPasses.md), Close row.

**Status: opt-in.** `ImmigrationEnabled` is false by default and in `OpeningConfig.json`; saves that predate the
fields load with it false and the default levers. Immigration and fission are independent switches (either can run
alone); when both run, immigration goes first.

A camp whose neighbour is a clearly better place to live may send a small group of its residents there. This is a
resolver population process like births, not an AI decision: no order, no player control, no observation. The owner
sees the result in the camp's demographic explanation ("... then moved to other settlements" / "... more arrived
from other settlements") and in the turn journal.

## Doctrine summary

- **Authoritative state.** No new persistent per-camp state. Immigration moves existing authoritative state: people
  (`Camp.Population`, the workforce ledger), the three typed food stores, and contact memory. New saved, optional
  records: `TurnRecord.Migrations` (`MigrationReport`, canonical order), `DemographicReport.Emigrated` /
  `Immigrated`, and `ContactSource.Migration`. New config: `ImmigrationEnabled` and ten levers (below), all
  `OptionalField`.
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

**Utility** (design section 4; the opportunity term is zero):

```
dU(i -> j) = w_Q * (Q_j - Q_i) - K_distance * d(i,j) - K_attachment - K_lifestyle
```

`Q` is the camp's QoL at its present residents. `K_lifestyle` is `ImmigrationToSedentaryCost` when a nomadic camp
moves to a village (`Camp.IsVillage`), the larger `ImmigrationToNomadicCost` when a village moves to a nomadic camp,
and zero between camps of the same lifestyle.

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

`ImmigrationEnabled` (false) is the switch. The last three are not in the design table (the design left the group size
open); they are uncalibrated placeholders.

**One intent per source.** Among the valid known destinations the source takes the highest `dU` (ties: nearer, then
lower camp ID) and emits an intent only if `dU > theta`:

```
M = floor(N * ImmigrationMaxFraction * clamp((dU - theta) / theta, 0, 1))      (plus 1e-9 so exact ramp points are not lost to rounding)
M = min(M, N - ImmigrationSourceFloor);   M < ImmigrationMinimumGroup  ->  no intent
```

So the ramp starts at zero at the deadband and reaches the maximum fraction at `2 * theta`; with the defaults and
N = 100 the smallest group (four) needs `dU >= 0.105`, which makes the minimum group, not the deadband, the effective
threshold for camps of about a hundred. No fallback to a second-best destination when the best yields no group.

**Reconcile once, canonically** (destination camp ID, then source camp ID). The destination estimates its QoL with
`Fission.QoLModel.At(headcount)`: the committed terms F, W and expectation unchanged; social cohesion `S` at the new
headcount; clothing coverage and housing per head diluted by the headcount (migrants bring no cloth and no housing).
The estimate is non-increasing in headcount, so each source is accepted for the **largest** group `m <= M` such that
the destination's estimated QoL at `N_j + inflow_so_far + m` is still at least `Q_i + theta`, found by bisection. The
excess of a partly accepted source, and all of a fully rejected one, stays home. A partly accepted group is not
subject to the minimum group (only the intent is).

**Commit, simultaneously and canonically.** All departures from the frozen residents and stores first, then all
arrivals. The migrants of a flow carry their per-capita share (`m / N_i` of the frozen stock) of the source's typed
Plant, Animal and Aquatic food and nothing else: no herds, tools, housing, fabric or fractional demographic progress.
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
`dU`, the destination's estimated post-inflow QoL and the typed food carried), in destination then source order, plus
one journal line. Observations carry nothing new beyond what an owner sees of its own camps. No Unity change.

## Validation

`ImmigrationChecks.cs` (registered in `Anthropocene.Headless.csproj` and the fixed-suite list, 174 assertions, own
worlds): defaults, validation, serialization and old-config loading, `OpeningConfig.json` keys; the closed-form
utility for every barrier; the size formula exhaustively over N = 0 to 400 and `dU` = 0 to 0.40 and its floors;
estimator equality with `EstimateSplit` and monotonicity; unknown settlements never targeted (Paleolithic vision
only, stored contacts ignored, hunter vision counts, unrecorded, Moved and mislocated contacts rejected, radius,
kin contacts, own Society, no self target); one intent per source with tie-breaks, distance and lifestyle barriers;
deadband; destination acceptance cap against an independent linear scan, partial acceptance, a valid second-best
camp left untouched; immigration precedes fission and excludes sources and arrivals (control: all three camps
fission with immigration off); no multi-hop (A to B while B to C); exact conservation of people and each typed food
per camp, no goods, herds or remainders move; demographic lines, journal, contacts in both directions; scalar versus
reversed four-worker replay with reordered collections over five turns of an eight-camp world and over a generated
world with both switches on; save continuation through a migration, old saves and old records loading disabled.

## Measurements

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

### Results

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

## Deliberate limits and remaining work

- Opt-in and uncalibrated: the design sweeps (deadband 0.05 to 0.10, lifestyle barriers, radius 3 to 5) have not been run;
  the oscillation and lower QoL above are the first things WP11 should address.
- Destination acceptance is checked against the estimate (F and W unchanged), not against the food the destination
  actually has; arrivals bring only the per-capita food of their source.
- The opportunity term (profession-specific pull) is zero by design. Affinity is inert: kin flows are not treated
  differently from other flows except in the report's kin flag.
- Known camps use true current positions only through the contact's recorded tile; a contact whose camp moved is not
  a destination until a sighting refreshes it.
- Unity: no journal display of migrations, no display of the demographic lines beyond the existing explanation string,
  no drawing of flows; `OpeningGame` has not been rebuilt. The simulation-side APIs (`TurnRecord.Migrations`,
  `DemographicReport.Emigrated`/`Immigrated`) are complete.
