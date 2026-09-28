# Issue #32: Watercraft capability boundary

**Status:** candidate source is commit
`2375ce10143364503c7b4e92a9049cfb4a6780d5`, stacked on Issue #31 commit
`e8e1fa3d3fe68983adec06fc25a285b1c96996f3`. The route implementation and label
are in `86cd24e6324ce976c6900ab2143cde1b6bcbaa13`; the final candidate also
includes the #31 Close-boundary test follow-up. Issue #32 reuses the shared
activity-driven discovery and society technology flags from #31. It adds no
Fishing-specific progression or persistent state.

## Capability matrix

“Agriculture” is the player-facing name for `Technology.Cultivation`.
Economy means the existing shared `OrderKind.Economy` plan and its current
production systems.

| Route | Agriculture | Animal Husbandry | Watercraft | Shared Economy | Farmer | Herder | Water access |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Paleo-only | No | No | No | Closed | No | No | Fish orders can target adjacent visible freshwater/Shallows from shore. Hunters cannot enter Shallows. |
| Agriculture-only | Yes | No | No | Open | Yes | No | Shore fishing remains available; Agriculture does not grant Shallows movement. |
| Husbandry-only | No | Yes | No | Open | No | Yes | Shore fishing remains available; Husbandry does not grant Shallows movement. |
| Watercraft-only | No | No | Yes | Closed | No | No | Watercraft permits eligible hunter traversal into Shallows; Fish orders were already legal from shore. |
| Mixed (Watercraft + Agriculture + Husbandry) | Yes | Yes | Yes | Open | Yes | Yes | Existing shore fishing, Shallows traversal and shared terrestrial/pastoral Economy coexist. |

`--issue32-capability-only` asserts each matrix row. In particular, Watercraft
does not satisfy the Agriculture-or-Husbandry Economy gate, and role validation
uses the separate Agriculture and Husbandry flags. When neither shared
prerequisite exists, Economy validation reports
`Economy requires Agriculture or Animal Husbandry.` A missing plan after an
eligible technology reports
`Economic plan is incomplete; assignments and priorities are required.`
These diagnostics clarify existing validation and do not change gameplay.

## Watercraft-first routes and turn boundary

The supported Watercraft-first secondary routes use ordinary existing actions:

* A successful Fish order can establish the first Watercraft breakthrough.
  Subsequent ordinary Gathering practice reaches Cultivation/Agriculture.
* Subsequent ordinary Hunting practice reaches Animal Husbandry.

The shared #31 resolver evaluates society-owned domain practice at Close. The
normal effective threshold is 225 points. If the first transition was forced,
only the selected secondary domain receives carryover, using the existing rule
`max(100, experience * 1.25)`. Discovery sets the existing technology flag and
is available to the next Prepare; it does not alter orders already prepared or
repeat first-breakthrough rewards. Progress remains in existing society
experience and flags. Issue #32 adds no save fields and does not change the save
or observation schema.

`RequestAutomaticEconomy` on an ordinary action such as `NoOp` or `Move`
continues to request the existing shared Economy plan. Watercraft alone does
not prepare that plan; after Agriculture or Husbandry is acquired, it becomes
available at the next Prepare alongside the ordinary action. Economy payloads
remain separate orders under the existing action contract.

## Existing aquatic hooks and deferred design

Fish orders and passive aquatic gathering already use finite aquatic food and
the shared turn-horizon resource accounting. Fish orders are legal from shore
without Watercraft, require a visible adjacent freshwater or Shallows target,
and do not open Ocean harvesting. Watercraft adds eligible Shallows traversal
for hunters. The one-time `FisherySeed` remains an opening compatibility effect
when the first Watercraft transition has opening compatibility enabled and a
suitable nearby Shallows tile exists. It is not a persistent fishery investment
or a new Economy unlock.

Watercraft-only Economy, Fishing recipes or occupations, dedicated extraction,
fishery capital, additional authoritative state and observation fields remain
deferred. `AquaticFood` remains dietary stock and does not satisfy the existing
PlantFood input to Cloth or AnimalFood input to AnimalFabric.

No new per-tile or per-generation work is introduced. The shared discovery scan
uses active societies times three capabilities, independent of map size and
elapsed `dt`. Per-active-plan Fish, Hunt, Gathering and Economy algorithms are
unchanged, while total work rises as more societies become eligible to plan
Economy. In the matched sample, median per-turn Prepare time rose from about
44–52 to 72 ms at 80 tribes and 279–329 to 432–536 ms at 320; median allocations
rose from 140 to 164 MiB and 541 to 620 MiB, respectively. See the evidence
below for mature-scale workload and RL-relevant cost drivers.

## Validation

The focused checks exercise actual Fish, Gathering and Hunting receipts;
Close/next-Prepare discovery boundaries; partial-progress save/fingerprint
continuation; no repeated opening rewards; the five-row capability matrix;
distinct Economy validation failures; existing automatic-Economy coexistence;
and Cloth/AnimalFabric feedstock provenance. Existing Neolithic/economy suites
continue to cover shared recipes and physical production.

The focused `--issue32-capability-only` suite (131 assertions), `--economy-only`
and full fixed headless suite with `Seeds=0` passed on commit
`c948300582492d13d1fa439e756b9a47313725a0`, before the test-only #31 follow-up.
After the follow-up, candidate `2375ce10143364503c7b4e92a9049cfb4a6780d5`
passed `--foundation-only` (46 assertions), `--issue32-capability-only` (131
assertions) and the documentation contract checker. The candidate's production
source is unchanged by the follow-up.

## Scale and matched timing evidence

The matched comparison used baseline `4db1fb6424fb1949c6b34c9f1a2f9b0a36f06122`
and candidate source `2375ce10143364503c7b4e92a9049cfb4a6780d5`. Both used the
production Standard/Continents opening checkpoint from seed 1729, 20-year turns,
four workers, 25 post-opening turns, and three alternated baseline/candidate
pairs at each population scale. Raw manifests and reports are in
`TestResults/Issues31-32/Timing25`; first-turn matched reports are in
`TestResults/Issues31-32/Timing1`.

| Tribes | Median summed 25-turn time, baseline → candidate | Paired time changes | Median per-turn allocations, baseline → candidate | 25-turn allocations, baseline → candidate | End managed heap, baseline → candidate |
| ---: | ---: | --- | ---: | ---: | ---: |
| 80 | 5.363 → 5.971 s | +13.4% median; pairs +13.4%, +9.8%, +16.5% | 140 → 164 MiB | 3,565 → 4,214 MiB | 414 → 420 MiB |
| 320 | 28.614 → 29.368 s | -1.0% median; pairs -12.6%, +19.3%, -1.0% (noisy) | 541 → 620 MiB | 13,667 → 15,674 MiB | 1.67 → 1.68 GiB |

Median per-turn Prepare time was about 44–52 → 72 ms at 80 tribes and
279–329 → 432–536 ms at 320. On the first common turn, population and food were
identical; allocations were 160.6 → 160.6 MiB at 80 and 584.6 → 584.8 MiB at
320, with noisy time measurements. Candidate Agriculture capability counts
reached 80 versus baseline 55 at 80 tribes, and 320 versus 243 at 320 tribes by
turn 14. These later turns have different live economic workloads. The measured
increase is chiefly existing Economy planning for newly enabled capabilities;
`--issue31-microbench` measured the shared Close-plus-status scan at 0.0201 ms /
16,688 bytes for 80 societies and 0.0689 ms / 67,168 bytes for 320.

The direct `--scale-only=80` run completed 10 turns with 16.336 s generation,
1.577 s total AI planning, 1.107 s total resolution and 1,565 MB peak memory.
`--scale-only=320` completed 10 turns with 61.657 s generation, 7.998 s AI,
7.092 s resolution and 4,158 MB peak memory. Both reported roughly 2,600–2,700
active integrations per turn at 80 and 10,500–11,000 at 320. Commands were
`dotnet run --project Tools/Headless/Anthropocene.Headless.csproj -- --scale-only=80`
and the matching `--scale-only=320` invocation.

The exact seeded command was
`./Tools/Test-Headless.ps1 -Seeds 30 -Standard -Geography`; it exited
successfully. The 30-seed batch had 120/120 survivors, zero starvation deaths,
net growth 7,363, zero illegal orders, and technology counts `0,40,60,20` in
25.752 s. The Standard 80-tribe world used 524,288 tiles; generation took
14.647 s at 299 MB, opening 3.249 s at 292 MB, save 2.371 s for 67,934,615
bytes, and load 3.860 s. Geography means were Continents land 0.333 / 74.8
islands / 0.317 coastline, Archipelagos 0.176 / 218.4 / 0.988, and Pangaea
0.261 / 87.4 / 0.277. RL rollout throughput was not separately measured.

The documentation report is a follow-up after the measured candidate source;
it does not change the simulation binary or the benchmarked source SHA.
