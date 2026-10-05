# Planning cost attribution and the first five reductions

**Status:** implemented on `features/neolithic-agricultural`, 5 October 2026. Outcomes are unchanged: per-turn world
fingerprints match the previous build exactly.

## Method (item 1)

**Profiler.** `PhaseProfile` (`Assets/_Anthropocene/Simulation/PhaseProfile.cs`) is an opt-in scope timer and work
counter.
- When off, a scope is one static read. It never enters state, saves, observations or fingerprints.
- Each scope records inclusive time, exclusive time (inclusive minus the scopes opened inside it on the same thread) and
  calls, keyed by its path (for example `Draft/Intents/Intent/Allocator`).
- So a helper called from several phases is attributed to each phase separately. The exclusive times of all paths add up
  to the roots' inclusive time; the harness checks this every run.
- Attribution runs with one worker, so every scope nests on one thread.

**Harness.** `Tools/PerformanceAudit/Attribution.cs`:

```
--attribution=<save>[,<save>...] [--reps=N] [--workers=N] [--attribution-output=<file.json>]
```

It replays the player's turn exactly as `OpeningGame` does:
- `TurnPlanner.Draft` for every player except the human, run while the player thinks;
- `EndTurn`: the draft's orders plus the human's queued orders, `Prepare` reusing the draft's intents, then `Resolve`;
- the UI refresh of the human's observation.

Draft work that Prepare reuses is attributed to the draft only, so nothing is counted twice. The old audit `--draft` mode
did count it twice: it also ran a full AI decision for every player. The harness prints the snapshot's own strategy and
settlement-economy levers as loaded; nothing is overridden. It reports:
- a wall pass at the desktop worker count (4);
- a scalar attribution pass (scope tree plus counters).

Two more modes:
- `--replay-turns=N` plays N turns from each snapshot and prints per-turn fingerprints, accepted, rejected and blocked
  orders and trades, for exactness comparisons between builds.
- `--placement-scaling=1` times camp placement at increasing settlement counts.

**Snapshots.** Generated from the shipped `OpeningConfig.json`: Standard/80, Continents seed 1729 (`C`) and Archipelagos
seed 42 (`A`), at 25 and 50 turns after the opening. They are in `TestResults/Attribution/saves` (ignored). The loaded
configuration was DemandControl, with fission, immigration, stock-only trade, QoL mortality, herd labour and shared
allocation on, and goods storage off.

## Where a turn goes

Scalar attribution before the reductions, ms per turn, mean of 5:

| Path | C, 136 settlements | C, 455 settlements |
| --- | --- | --- |
| Draft: AI policy (`Draft/Decide`, exclusive) | 47 | 52 |
| Draft: AI observations | 22 | 86 |
| Draft: economic allocator (DemandControl) | 35 | 127 |
| Draft: trade plan | 3 | 13 |
| Prepare: order validation | 24 | 86 |
| Prepare: needs | 12 | 41 |
| Resolve, of which economy / herd intents | 89 (53 / 20) | 188 (132 / 18) |

## The five reductions

| # | Change | Evidence of identical outcomes | Work and time |
| --- | --- | --- | --- |
| 2 | **Camp spacing by local conflict enumeration.** `CampPlacement.Resolve` keeps its rows in camp-ID order and, within a row, later camps in ID order, with current destinations. It enumerates only camps whose destination lies within spacing − 1 hexes, through a tile index updated on every rollback; a row whose camp is rolled back resumes from its new destination. | The all-pairs sweep is kept as the reference in `CampSpacingChecks.LocalEnumeration`: 600 randomized boards (spacing 1–5, up to 61 camps, seam and polar rows, shuffled orders) match exactly. `--placement-scaling` agrees at 100–3,000 camps. Replay fingerprints match. | Pairs examined per turn at C, 455 settlements: 826,280 → 312. Scaling (reference / local): 1,000 camps 37 / 3.5 ms; 3,000 camps 319 / 14 ms; 10,000 camps local 36 ms. Near-linear. |
| 3 | **Order validation reads an order-rule view.** `AgentObservationBuilder.BuildForValidation` keeps the same visible tiles, own settlements and hunters, rivals, herds and scalars. It leaves out what `OrderRules.Validate` never reads: remembered tiles, known settlements, kin, candidate forecasts, likely herd destinations, recent extraction, technology statuses, and the settlements' material sources and fixed housing. There is still one rule implementation. | `AgentContractChecks.ValidationView`: a 6-tribe world grown into the Neolithic. Each turn and player, about 59,000 legal and illegal orders get the same verdict from both views, and the view holds the same entities and tiles (no information beyond the full observation). Replay matches, including rejection counts. | Validation at C, 455: 86 → 21 ms; at C, 136: 24 → 12 ms. Wall prepare: C, 455, 109 → 37 ms; C, 136, 35–45 → 21–22 ms. |
| 4 | **Fixed housing is indexed.** `FixedHousingIndex` maps (camp, tile) to list positions. It is immutable once built, published by reference and rebuilt when the list object or its count changes. A hit is re-checked against the record at its position. Sums visit records in list order. Direct same-count edits that change another key must call `NeolithicProduction.FixedHousingChanged`; simulation writers only append or replace the list. | `NeolithicChecks.HousingIndexEquivalence`: bit-identical to the former linear scan through API additions, in-place updates, direct edits, replacement and clearing, at three clock times, for both overloads. Replay matches. | Records scanned per turn at C, 455: 9,965,980 → 10,279, now index builds plus the records actually read. Housing time 13.6 → 4.3 ms. |
| 5 | **Trade values only camps that can take part.** Partners depend only on contact memory, Societies and distance, so they are chosen first. Only requesters with a partner, and those partners, are valued. | `TradeChecks` (480) pass. Replay matches agreements (trade counts) and fingerprints. | Camps valued at C, 455: 454 → 212 (47 actually trade). Valuation 10.8 → 5.3 ms. |

**Exactness.** The previous build and this one replayed 8 turns from each of the four snapshots; all 32 turns have
identical world fingerprints and order and trade counts (`TestResults/Attribution/*.replay`). The baseline was replayed
twice, identically, to confirm the replay is deterministic. The focused headless suite passes, including the scalar
against 2/4-worker replay over 34 resolutions.

**Wall time per turn** (4 workers, mean of 5; draft + prepare + resolve + refresh):

| Snapshot | Before | After |
| --- | --- | --- |
| C, 136 settlements | 212 ms | 206 ms |
| A, 170 settlements | 223 ms | 151 ms |
| A, 270 settlements | 213 ms | 190 ms |
| C, 455 settlements | 485 ms | 331 ms |

**Noise.** This shared desktop shows run-to-run noise of up to ±30 ms in unchanged phases. Alternating the two builds
three times on C-136, the AI policy varied 42–74 ms (before) and 47–62 ms (after); wall prepare was consistently 35–45
against 21–22 ms. Trust the scoped and counted reductions above more than single wall totals.

## Not changed here

**AI decisions.** The policy and its observations are now the largest planning cost at scale (C, 455: 52 + 82 ms). The
draft computes them while the player thinks.

**Allocator.** It is 0.26–0.28 ms per plan, within the accepted target.

**Resolution economy.** It is unchanged.

**Placement passes.** They still re-sweep all rows (6–8 passes at 455 settlements); each sweep is now local.

**Draft parallelism.** The draft's wall time is sometimes above its scalar time, so its parallel partitioning deserves its
own measurement.
