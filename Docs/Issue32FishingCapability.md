# Issue #32: Watercraft capability boundary

**Status:** implemented in commit `86cd24e6324ce976c6900ab2143cde1b6bcbaa13`,
stacked on Issue #31 commit `e8e1fa3d3fe68983adec06fc25a285b1c96996f3`.
Issue #32 reuses the shared activity-driven discovery and society technology
flags from #31. It adds no Fishing-specific progression or persistent state.

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
elapsed `dt`; existing Fish, Hunt, Gathering and Economy paths retain their
current costs at opening, mature-empire and RL scales.

## Validation

The focused checks exercise actual Fish, Gathering and Hunting receipts;
Close/next-Prepare discovery boundaries; partial-progress save/fingerprint
continuation; no repeated opening rewards; the five-row capability matrix;
distinct Economy validation failures; existing automatic-Economy coexistence;
and Cloth/AnimalFabric feedstock provenance. Existing Neolithic/economy suites
continue to cover shared recipes and physical production.

Validation passed at #32 implementation commit
`86cd24e6324ce976c6900ab2143cde1b6bcbaa13`: focused
`--issue32-capability-only` (131 assertions), `--economy-only`, and full fixed
headless suite with `Seeds=0`. The documentation contract checker also passed.
The final branch tip, including this report, is recorded in the delivery report.
