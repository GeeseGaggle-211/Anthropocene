# Cultivated-field contact and Husbandry — issue #12

The approved 6 November 2026 update uses **12 traversable-land steps for
attraction**, **6 for contact**, and **6 consecutive peaceful contact turns**
(120 years at the default twenty-year horizon). Goats, wild cattle (aurochs),
camels and horses qualify. The existing herd becomes managed at Close and joins
ordinary production next turn. No replacement animals are granted.

## Authoritative mechanics

- A living Neolithic agricultural village with positive owned field capital
  selects at most one living wild herd whose species has both `Domesticable`
  and `CultivationAffinity`. `FieldDetectionRadius = 12` bounds the shortest
  traversable-land path from village to herd. Water and impassable mountains
  block the path; cylindrical longitude wrapping works normally. Existing
  same-Society contact is preferred, then shortest land distance and herd ID.
  An unreachable herd cannot occupy the one-herd selection slot.
- Radii are **path lengths**, not straight-line proximity plus an unlimited
  detour. A herd two hexes away across a barrier with a five-step land detour
  needs at least five steps of detection/contact range. This keeps costs local
  and excludes apparent proximity across straits or mountain walls.
- The strongest owned radius-one field supplies q. Existing admitted movement
  candidates receive `q / (1 + hex-distance(candidate, field))` in addition to
  their existing score. Multiple villages use the maximum signal. The herd's
  current position already has a valid land path; each admitted land-neighbor
  move extends a valid route. Existing one-hex movement, hunter avoidance,
  hunger, thirst and willingness-to-wander rules remain authoritative. There
  is no food, protection, reproduction or extra movement grant.
- At Close the selected herd must still be alive, within
  `FieldContactRadius = 6` shortest land steps, and associated with a village
  whose owned field remains active. Terrain is fixed during a resolution, so
  the pre-movement contact footprint can check the herd's endpoint directly.
  Contact range is a settlement interaction abstraction **separate from the
  unchanged two-hex player vision**. It neither reveals foreign herds to AI
  observations nor lets remote scouts enlarge contact range.
- Contact counts once per herd/Society per turn across all villages. Losing
  the reachable contact, village, field or selection resets the streak. Any
  attempted hunt of the herd resets and blocks the current turn, including a
  zero-kill attempt by another Society. Hunting practice never supplies contact.
- `Herd.FieldContacts` saves the sparse, canonically ordered Society, streak and
  last counted turn; `LastHostileTurnPlusOne` records attacks. Repeating Close
  cannot add a second turn. A split retains contact only on the original herd
  identity. Competing threshold claims resolve in stable camp-ID order.
- Acquisition sets `DomesticCamp` and `OwnerSociety` on the existing herd,
  retaining ID, species, animals and fractional biological state. The shared
  `TechnologyDiscovery.Acquire` effect unlocks Husbandry if needed. Already
  knowledgeable Societies still acquire animals. No current-turn products,
  resource claims, IDs, staffing or opening compatibility grant are created.
  The next ordinary movement/economy boundary handles the herd at its camp.

## Species and saved worlds

| Species ID | New-world field affinity | Treatment |
| --- | --- | --- |
| `goat` | true | Existing wild goats |
| `cattle` | true | New wild cattle (aurochs), placed by ordinary habitat-weighted herd generation |
| `camel` | true | Existing wild camels; ecological rates unchanged |
| `horse` | true | Existing wild horses; ecological rates unchanged |
| `antelope`, `bison` | false | Existing opening compatibility flags retained; excluded from field domestication |

Cattle initially reuse the existing large-bovid rates: food 0.65, freshwater
0.7, food yield 150, growth 0.0201, starting herd 3–7 and maximum herd 150.
Habitat weights are grassland 1.2, woodland 1.0 and tundra 0.1. These are initial
simulator calibration values, not new empirical physiological estimates.
Binary affinity for all four species is the user-approved gameplay rule, not a
claim that their historical domestication pathways or dates were identical.
API defaults and `OpeningConfig.json` species parameters are checked for parity.

**Generator 15** records the changed new-world herd composition; morphology
and the world-generation algorithm remain unchanged. Old saves retain their
species lists, affinity values, herds and generator provenance without inserting
cattle or regenerating terrain. The save envelope remains `ANTHROPOCENE-11`.
Absent `FieldDomesticationEnabled` still loads false; absent detection loads 6.
Absent `FieldContactRadius` loads **2**, preserving the earlier saved contact
setting; explicitly saved values override those fallbacks. The new valid-path
rule applies whenever the feature is enabled. New worlds default to detection
12/contact 6. Validation requires detection 1–12, contact 1–detection, and
consecutive contact turns 2–100. Changing turn duration changes the years
represented by six turns without introducing another simulation clock.

## Matched range and threshold evidence

Runtime: .NET 8.0.427, Linux x64, Release. The pre-update source was
`48d7a1b6be2ddb26f4e5de6580ad0b0416ca684b`. Current compact results are in
[FieldDomesticationSweep.csv](FieldDomesticationSweep.csv) and
[FieldDomesticationScale.csv](FieldDomesticationScale.csv); full per-Society
results regenerate under `TestResults/Issue12RangeSpecies`.

Six natural 96×48 worlds use production configuration with eight founders and
start spacing 9. Seeds 1/4 are Continents, 2/5 Archipelagos, 3/6 Pangaea. Each
initial world is saved and replayed through **30 turns / 600 years** with:
feature off; new 12/6 ranges at thresholds 5 and 6; and a matched old-range
6/2 control at threshold 6. All variants use valid land paths and the **same
expanded-species generation**. Nothing inserts animals, fields or technology
into the natural runs. Fission remains enabled; denominators include daughter
Societies that become agricultural villages, so these are not independent
world-level probability estimates. Villages form at different times.

| Shape | Feature off | Old ranges 6/2, threshold 6 | New ranges 12/6, threshold 5 | New ranges 12/6, threshold 6 |
| --- | ---: | ---: | ---: | ---: |
| Continents | 0/17 | 2/17 | 5/17 | 4/17 |
| Archipelagos | 0/17 | 6/17 | 7/17 | 7/17 |
| Pangaea | 0/13 | 2/14 | 7/13 | 5/14 |
| Total | 0/47 | **10/48 (20.8%)** | **19/47 (40.4%)** | **16/48 (33.3%)** |

Each cell is acquisitions / village Societies. Differing acquisition timing
can change later village formation, so denominators are reported per variant.
The old/new six-turn comparison uses the same generated initial worlds.

The approved six-turn threshold is retained; five turns permits three additional
acquisitions before the finite endpoint, with one fewer village Society formed. The six-turn new-range median delay
from village formation is six turns. The 16 acquired herds comprise **13 goat,
2 cattle and 1 camel herd**. No horse herd was acquired in this small natural
sample; focused tests cover actual horse conversion and persistence. The old
range control acquired eight goat and two cattle herds.

The earlier goat-only, no-path-check experiment measured 9/47 under both
thresholds at turn counter 30: **29 completed turns / 580 years**. The earlier
600-year description was an off-by-one reporting error. The harness now counts
completed transitions and records actual elapsed years explicitly. It is historical evidence, not the matched comparator above:
adding cattle changes habitat-weighted species placement and the opening
cohorts even at the same seeds. The baseline diagnostic also found 13/47
Societies without a qualifying goat inside six hexes, 22 with nearby herds but
no two-hex contact, and only three with contact but no acquisition (two late
villages and one hunting-interrupted streak). The expanded ranges and land
filter address that spatial bottleneck without removing threat responses.

The controlled approach sweep uses 40 seeds per distance/supply pair, two
thresholds and 24 ordinary resolver turns: **480 runs**. Each starts with a
24-person agricultural village and eight goats. No movement or contact is
scripted. Supplied tiles use 100,000 biomass / 100,000 water / 10,000 edible
food; limited tiles use 800 / 100 / 80. Normal 5% wandering and goat growth apply.

| Initial distance / supply | Five-turn success | Six-turn success | Median first managed turn, 5 / 6 |
| --- | ---: | ---: | ---: |
| 6, supplied | 39/40 | 39/40 | 5 / 6 |
| 8, supplied | 0/40 | 0/40 | — |
| 12, supplied | 0/40 | 0/40 | — |
| 6, limited | 40/40 | 40/40 | 5 / 6 |
| 8, limited | 40/40 | 40/40 | 11 / 12 |
| 12, limited | 38/40 | 38/40 | 15 / 16 |

Thus expanding detection does not compel well-fed distant herds to approach.
Exposure telemetry snapshots exact selected herd/Society pairs before resolution,
including the acquisition turn and herds whose endpoint moves out of range.
No selected herd disappeared during approach in this sample. Wild shortages in
the existing model reduce reproduction and drive movement, but do not directly
cause wild starvation deaths; observed survival therefore does not establish
starvation-safe approach. Managed nutrition/mortality is unchanged. Each sweep
transfer verifies the prior same-herd/Society streak reached threshold minus one.

## Scaling and validation

State remains sparse per herd/Society; path footprints are transient. The
existing `PlanningIndex` finds herds on locally reachable tiles. One bounded
BFS per active village costs `O(r_detect²)` on the hex grid; canonical camp/herd
sorting and local encounters add their normal costs. It does not scale with
population or years per turn. Scratch distance/queue buffers are reused across
villages, and only the selected six-step contact footprints survive for Close.
Allocation-free neighbor lookup uses the same HexMap directions and wrapping.
Movement readers share an immutable attraction map; contact and ownership commit
serially. No new privileged observation, reward or annual RL clock is introduced.

The scale probe measures attraction construction plus Close using a warm shared
planning index, a 320×320 map, eight-hex camp spacing and one herd per village.
Ten samples follow two warmups. Current timings/allocations are in the linked
scale CSV; they measure the mechanic, not full-turn latency. The initial naive
path implementation measured 165.7 ms / 80.9 MB at 1,280 villages; scratch reuse
and compact contact footprints remove unnecessary path-map retention and
per-tile iterator allocation. The optimized result is **101.4 ms / 15.1 MB**
at 1,280 villages (8: 0.418 ms / 101 KB; 80: 4.822 ms / 929 KB;
320: 22.807 ms / 3.76 MB). This is still more work than the original geometric
range test; costs follow the enlarged reachable area. The before/after optimization
per-Society CSVs at the same original endpoint are byte-identical. The final
sweep additionally corrects the endpoint to 30 completed turns. The final fixed-suite run took 49.1 s
on three lanes (peak working set 1,868 MiB).

The focused suite has **114 assertions**, covering land barriers, alternate herd
selection, lost routes, exact range boundaries, bounded detours, wrapping,
independent path-query equivalence, all four eligible species, configuration
parity, cattle generation, legacy saves, already-known Husbandry, real-animal
conservation and scalar/four-worker save continuation through conversion.
The 64 fixed suites, three-seed benchmark, Standard/80 generation/opening/save
round trip and geography checks cover the wider simulation. No Unity UI changed;
no manual UI validation is claimed.

Run from the repository root with .NET 8:

```bash
dotnet run -c Release --project Tools/Headless/Anthropocene.Headless.csproj -- --issue12-only
dotnet run -c Release --project Tools/Headless/Anthropocene.Headless.csproj -- 3 --standard --geography --jobs=3 --check-timings
dotnet run -c Release --project Tools/Headless/Anthropocene.Headless.csproj -- --issue12-sweep --issue12-output=TestResults/Issue12RangeSpecies
python Tools/Docs/Check-CurrentContract.py
git diff --check
```

`--issue12-quick` uses one natural seed and four controlled seeds for harness
smoke checks. Tables here use the full sweep. This cloud workspace uses
`/tmp/anthropocene-dotnet/dotnet` with
`DOTNET_CLI_HOME=/tmp/anthropocene-dotnet-home`.
