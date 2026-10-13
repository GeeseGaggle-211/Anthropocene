# Autonomous Elder economy v3 — validation and handoff

Experimental branch: `feature/elder-autonomous-economy`, based on simulator
`5559961164d6f9da8e6d20c1dc563ecec3eb04b7`. Shared contract:
[AutonomousElderEconomy-v1.md](AutonomousElderEconomy-v1.md). Versioned source
manifest: `Tools/ElderRL/autonomous-v3-manifest.json`. The learner pins the final
simulator commit separately in `configs/simulator/elder-autonomous-v3.json`.

## Implemented behavior

Action 2 directly allocates all 13 native workforce professions using structural
masks and stable largest remainder. Conditional gatherer/material and craft
recipe shares reserve actual worker-time; a blocked job stays idle. Native
field expansion, herd retention and breeding remain explicit controls.

Transient production orders authorize material and manufactured-goods surplus
without the DemandControl local-use ceilings. Native finite sources, tool
coverage, recipe dependencies, inventory, storage, resource arbitration and
turn ordering still constrain output. Housing Material retains its special
tool-service gate while no longer reserving labor for unrequested assembly.
The compiler does not fill food, tool, housing or other workforce deficits.

Environment 3 accepts all living controlled Elders together. Observation 3
contains owner-filtered context and structural masks, own stocks, and eight
contact slots aligned with the contextual contact rows. No remote inventories,
raw contact IDs or submitted peer actions enter actor observations. A missing
submission or illegal action fails before world resolution.

One optional stock quote per camp can exchange any active native trade good
with a known counterparty. Reciprocal offers, quantity caps and price limits
are reconciled deterministically, then native escrow settles the stock legs.
Controlled camps do not participate in heuristic trade. Uncontrolled camps
retain their heuristic economy/trade. No same-turn resale or production promise
is admitted; newly produced surplus becomes quoteable next boundary.

Replay 3 stores native snapshot bytes, versions, build/configuration identity,
requested actions, background orders, accepted staffing and state fingerprints.
It verifies complete native transitions, including a JSON persistence round
trip. No mid-turn authoritative state or new world-save envelope is introduced.
The old action-1 / observation-2 / environment-2 API remains intact.

## Automated evidence (13 October 2026)

- Original pinned native baseline: 143 Elder assertions passed.
- Original learner main `fa71f370aa50982552584861e61682d7cd2897e0`: 131 passed,
  one skipped, before edits; CPU PyTorch 2.14.1, Gymnasium 1.2.2.
- Full native fixed suite after core fixes: passed (69 PASS records, 66.2 seconds,
  one lane, peak working set 1,766 MiB). Includes trade, storage, food/field phase
  ordering, demographic, immigration/fission, knowledge, ecology and replay checks.
- Final focused autonomous suite: **462 assertions passed**. This includes the
  independent reviewer's 30 randomized policies: 390 per-role labor bounds and
  30 replay checks. Final focused suite adds serialized replay and quote-bound tests.
- Scripted staffing witness: 240 Pots with Potters, zero with Farmers, despite
  10,000 Pots in opening stock. Missing tools, clay or workers constrains output;
  zero food plus no food workforce yields food-needs ratio zero. A conditional
  Weaver action makes 240 Cloth and zero Animal Fabric. Native full storage
  blocks surplus Cloth. Housing assembly alone cannot conjure Housing Material.
- Material surplus accumulates with no craft demand, debits the finite source
  exactly, and trades at the next boundary with Stone/Clay conserved.
- Compatible wood/clay quotes transfer exactly 10/20; incompatible price limits
  do not match. Relocated stale contacts do not settle. Submission ordering and
  scalar versus reversed two-worker partitions produce identical fingerprints.
- Baseline/candidate **exact fingerprint parity**: initial state, four complete
  heuristic transitions and three old five-head Elder transitions. Old replay
  verification passes in both builds. Results are checked in under
  `Tools/ElderRL/evidence/autonomous-v3/old-mode-fingerprints.txt`.
- Final learner results are recorded in the learner's `evidence/elder-autonomous-v3/`.
  Its new tests cover masked Dirichlet densities/entropy, inactive RNG/gradients,
  degenerate faces, requested-share likelihoods, model reload and PPO parameter
  updates, plus a real three-boundary two-Elder Gym/native replay test. No PPO
  campaign or expensive training was run.
- Both old and new source versions compile through the learner bridge. Unity
  source metadata is included; no Unity editor/player validation was performed.

## Complete-step performance

Release .NET 8.0.425, scalar execution, Linux x86-64 cloud CPU. Tiered compilation
is disabled to avoid different JIT tiers across ascending scale cases. Seed 9301,
120 residents/camp, synthetic fixed village states; maps are 20×14 for one/two
camps and 48×40 for the larger scale probes. `BenchmarkScale` permits the dense
80-camp fixture. Two warmups, seven measured fresh one-step samples per case.

Both modes include background AI decisions, preparation, economic/resource
execution, closing passes, one full autonomous observation construction per
controlled camp at the endpoint, and state fingerprinting. Initial snapshot
loading/reset is outside timing. Autonomous timing also includes action copies
and replay bookkeeping. Heuristic and scripted all-Potter policies do different
economic work from identical opening snapshots; this is a full-loop cost
comparison, not an equal-trajectory microbenchmark. Native phase semantics and
map physics are unchanged.

| Camps | Heuristic median ms | Autonomous median ms | Heuristic allocated MB | Autonomous allocated MB |
| ---: | ---: | ---: | ---: | ---: |
| 1 | 1.574 | 1.161 | 0.733 | 0.640 |
| 2 | 2.156 | 1.602 | 1.181 | 1.008 |
| 8 | 8.337 | 6.091 | 4.508 | 3.736 |
| 32 | 29.844 | 23.012 | 18.869 | 15.567 |
| 80 | 71.125 | 58.876 | 54.431 | 47.048 |

Raw ranges and allocations: `Tools/ElderRL/evidence/autonomous-v3/performance.json`.
The 32-camp autonomous maximum was 101.16 ms; shared-host/JIT/GC noise prevents
strong tail-latency claims. An earlier measurement even showed an 8-camp median
regression. The final 80-camp median is about 17% lower, with about 14% fewer
allocated bytes. These compact synthetic maps do **not** establish throughput
on mature generated worlds, long histories, large contact networks or long
training rollouts. Existing per-owner observation scans remain a scaling cost.

## Independent review

A separate reviewer inspected both repositories and independently ran native
and learner checks. Four findings were corrected: contact-row/quote-slot
alignment; stale relocated contacts; Housing Material's tool gate; and finite
timber pressure requests. The reviewer reran 39 then-current focused native
assertions, 420 randomized labor/replay checks, and three learner tests. Its
native integration invocation skipped because bridge variables were absent;
the primary run exercised and passed that integration. Final verdict: no
remaining blocker for the mechanics slice, with no policy-quality signoff.

## Deliberate limitations and next gate

- Movement is stay-only. One quote per Elder; native/scripted quotes work, but
  learned quote distributions and trade-head PPO are not implemented. Quotes
  currently settle between controlled Elders; no heuristic counterquotes are generated.
- Food harvesting retains native demand/reserve rules; surplus authorization
  here covers materials and manufactured goods. There is no made-to-order trade.
- Conditional recipe time is fractional within integer profession staffing.
  Native source selection and dependency ordering remain deterministic.
- The learner uses a separate 753-feature signed-log mechanics encoder. It has
  no calibrated v3 normalizer, compatible old datasets/checkpoints, rollout
  campaign/checkpoint orchestration, ONNX export or distributed learner.
- Endogenous ecology, chronology, technology, demographics, immigration, fission
  and knowledge remain enabled by the loaded world's configuration. Fission
  daughters are uncontrolled and use the native background policy; they are not
  automatically enrolled as new learned Elders.

Next safe gate: a bounded CPU-only multi-world scripted/randomized v3 rollout
bank with explicit starvation, storage, materials, livestock, fishing and
reciprocal-trade coverage; inspect distributions, welfare/labor/resource ledgers,
replay and generated-world full-step throughput. Then freeze a v3 TRAIN-only
normalizer, masks, rewards and reproducible optimizer/checkpoint contract before
any small learning pilot. The current work does not authorize an expensive PPO
campaign, merging, or GPU ecology work.

## Reproduction

```sh
dotnet run -c Release --project Tools/Headless/Anthropocene.Headless.csproj -- --autonomous-elder-only
dotnet run -c Release --project Tools/Headless/Anthropocene.Headless.csproj -- 0 --check-timings
DOTNET_TieredCompilation=0 dotnet run -c Release --project Tools/Headless/Anthropocene.Headless.csproj -- --autonomous-elder-benchmark
dotnet run -c Release --project Tools/Headless/Anthropocene.Headless.csproj -- --autonomous-elder-fixture /tmp/autonomous-fixture.anthro
```

For exact old-mode parity, build `Tools/ElderRL/AutonomousRegressionProbe.csproj`
twice with `-p:SimRoot=<absolute baseline/candidate root>` and distinct `-o`
directories, then pass the same generated fixture path to each executable
(the assembly is named `AutonomousRegressionProbe`). Compare stdout byte-for-byte.
