# Economic-transition hunter recall

## Rule

When a Society first gains a resident food economy, every living hunter group
that already belongs to one of its camps is marked for recall. The triggering
capabilities are Agriculture, Husbandry, and feature-enabled resident Fishing.
Feature-off Watercraft is not a resident economy; if Agriculture or Husbandry
is learned later, that secondary unlock triggers the recall instead.

The built-in policy holds the source camp in place, continues automatic
economic preparation, and runs a deterministic depth-first return over terrain
the owner can currently see. It takes a full movement-bounded hop when the camp
is visibly reachable; otherwise it explores an unvisited adjacent tile,
preferring geometric closeness and then a visible obstacle boundary, and
backtracks when it reaches a dead end. Each observation exposes only that next
visible endpoint. Hidden terrain is never read to choose it. A marked group
already at the camp receives an explicit
`Reintegrate` order. Reintegration removes the hunter entity and returns its
surviving population to the resident workforce. Groups dispatched after the
unlock are not marked and keep ordinary hunter behavior. An explicit owner wait
or other non-return action, an invalid path deviation, or movement of the source
camp cancels that automatic recall rather than leaving stale state that can hold
the camp indefinitely.

The recall is policy-visible, not a forced player action. Observation schema 21
adds `EconomicTransitionRecall` to owned hunter observations and their DTOs.
Foreign observations always receive the default false value, so the flag does
not reveal another Society's transition state.

## Authority and compatibility

`Hunter.EconomicTransitionRecall` is the authoritative, optional saved bit.
Older saves omit it and therefore continue with no invented recall. The world
save envelope remains `ANTHROPOCENE-11`; no terrain, resource, or ecology schema
changes.

The marker, current `EconomicTransitionRoute` stack, and
`EconomicTransitionVisited` set are initialized only at a permanent technology
unlock boundary:

- the first opening breakthrough, after `OpeningTransitions.Apply`; or
- a secondary discovery in `TechnologyDiscovery.Resolve` when the Society
  crosses from no resident food economy to one.

There is no per-turn technology scan and no hidden-map route construction. Each
permanent unlock checks the Society's permanent capability bits in `O(1)`; the
first crossing marks hunters in `O(C + H)`. While a marked hunter is returning,
its waypoint check is bounded by the fixed movement radius and local hex degree.
The saved visited set and backtracking stack use `O(V_h + P_h)` memory, where
`V_h` is the number of tiles that hunter has actually explored during recall and
`P_h` is its current return path. Hash membership and stack push/pop are `O(1)`;
on a static reachable component the depth-first search is finite. Both
structures are removed at reintegration or cancellation. Shallow-water steps use
the same `WaterMovementCost` contract as order validation.

## Validation

`EconomicTransitionChecks.cs` covers all three first-unlock paths, feature-off
Watercraft followed by secondary Agriculture, exclusion of later dispatches,
schema-20 serialization, save round trips and absent-field compatibility,
camp hold, a long ridge-detour liveness witness, deterministic return, explicit
reintegration, paired hidden-terrain observation equivalence, configured
shallow-water movement, cancellation, foreign-marker privacy, and Fishing-only
planner behavior. Run
it with:

```text
./Tools/Test-Headless.sh --economic-transition-only
```

The post-change system check is the same paired 50-turn census used to identify
the low tail: five Continents and five Archipelagos worlds, once with trade but
without fission/immigration and once with trade, fission, and immigration. It
reports median, p25, p75, minimum, and maximum population and QoL by technology
branch, and compares linked population with resident population so an active
hunter detachment is not mistaken for demographic loss.

### October 6, 2026 results

The complete fixed headless suite passed with zero benchmark seeds, including
34-resolution scalar/2/4-worker replay. The focused transition suite passed 27
assertions. Both registered Fishing 50-turn tests then reproduced their complete
per-turn artifacts and final fingerprints exactly across two runs. Their
canonical five-world turn-50 population medians were 571 Agriculture / 703
Husbandry without fission or immigration, and 562 / 415 with all features,
above the historical-minus-20-percent gates of 384 / 306.4.

The larger tail comparison used seeds 1–5 for both Continents and Archipelagos,
Standard/80, 50 measured turns, and the same post-opening checkpoint for the two
social-feature configurations in each world. Each statistics vector below is
`[minimum, p25, median, p75, maximum]` and pools active camps by their Society's
first technology branch.

| Scenario | Branch (n) | Resident/linked population | QoL |
| --- | --- | --- | --- |
| Trade, no fission/immigration | Agriculture (538) | `[29, 204.75, 577.5, 966, 1005]` | `[0.3602, 0.7448, 0.8279, 0.9542, 0.9997]` |
| Trade, no fission/immigration | Husbandry (216) | `[30, 493.5, 729.5, 796.75, 843]` | `[0, 0.7425, 0.7503, 0.7591, 0.9983]` |
| Trade, no fission/immigration | Fishing (46) | `[81, 164.75, 436.5, 744.75, 985]` | `[0.3005, 0.7446, 0.8611, 0.9515, 0.9959]` |
| Trade, fission and immigration | Agriculture (1,992) | `[29, 339, 586, 768.25, 1094]` | `[0.1726, 0.8003, 0.8725, 0.9465, 0.9997]` |
| Trade, fission and immigration | Husbandry (7,047) | `[12, 266, 453, 623, 1087]` | `[0, 0.7584, 0.8042, 0.9098, 0.9991]` |
| Trade, fission and immigration | Fishing (156) | `[32, 325, 571.5, 778.75, 1099]` | `[0.1251, 0.7606, 0.8673, 0.9448, 0.9990]` |

All final linked and resident populations are identical because no opening
hunter detachment remains: 2,032 groups / 12,901 people in the old no-movement
result and 2,035 / 12,928 with all features both fall to zero. In the
no-movement case, resident camps at or below 50 fall from 228 to 40; the old
linked-population count was 102. None of the remaining 40 grew past 100 and then
collapsed. They are small opening cohorts: the Agriculture cases include four
with starvation, while the small Husbandry cases remain fully fed but often
lack Cloth and Housing.

With all social features, camps at or below 50 linked people fall from 135 of
4,272 (3.16%) to 82 of 9,195 (0.89%). Twenty-three camps grew past 100 and ended
at or below 50 versus thirteen before; all twenty-three dispersed through
immigration, seventeen also fissioned, and none suffered starvation. The rate
therefore falls from 0.30% to 0.25%, while the absolute count rises with the
larger population and camp cohort. The same healthier trajectory raises active
camps from 4,272 to 9,195 (8,395 post-checkpoint fissions versus 3,472), so the
all-feature test takes materially longer because it simulates more entities.
This is an outcome-scale consequence, not recurring recall work, and should be
included in later fission/performance calibration.

Raw ignored evidence is in
`TestResults/Fishing50/bottom-tail-linked-census-transition-final.json`; the comparison
baseline is `bottom-tail-linked-census.json`. The registered artifacts are
`no-population-movement.json` and `all-social-features.json` in the same folder.
