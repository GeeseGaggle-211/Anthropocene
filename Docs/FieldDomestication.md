# Cultivated-field contact and Husbandry — issue #12

The 5 November 2026 implementation follows the user's explicit authorization and
locked decisions, superseding the historical calibration hold in issue #12.
Agricultural villages attract one eligible existing wild herd locally, build
consecutive peaceful contact, acquire Husbandry and convert those animals into
an ordinary managed herd. There is no compatibility animal grant on this path.
The selected default is **six turns** (120 years at the twenty-year default),
compared with five turns (100 years) below.

## Authoritative mechanics

- A living Neolithic agricultural **village** with positive owned field capital
  selects at most one living, wild, domesticable, field-attracted herd within
  `FieldDetectionRadius = 6` hexes. Existing contact with the same Society is
  preferred, then nearest distance and stable herd ID. Selection is a pure
  pre-movement snapshot using the existing tile index; it does not reveal a
  herd to a player or give ownership.
- The village's strongest owned radius-one field supplies q. An admitted herd
  movement candidate receives `q / (1 + distance(candidate, field))` in addition
  to its existing score. Multiple villages use the maximum signal, not a sum.
  q=0 gives exactly zero. The existing one-hex traversable-land neighborhood,
  hunter flee branch, hunger/thirst response and willingness-to-wander gate all
  remain in force. There is no food, protection or reproduction bonus. This
  simple spatial attenuation is a game abstraction, not an estimated animal
  sensing law; it replaces the issue's unapproved provisional edge formula.
- At Close, the same selected herd must still be alive, in range and in the
  **village's actual two-hex vision**, with its village and owned field still
  present. A remote scout's or another settlement's shared view cannot qualify.
  Each herd/Society pair receives at most one contact turn across all villages.
  Missing visibility, selection, village or field breaks the streak. Any
  attempted hunt of this herd by any Society resets and blocks contact that
  turn, including a zero-kill attempt. Hunting experience never supplies this
  contact counter.
- `Herd.FieldContacts` is a sparse, saved, canonically ordered list of Society,
  consecutive turns and last counted turn. `LastHostileTurnPlusOne` is saved on
  the herd. Every Resolve has one Close evaluation; repeated calls at the same
  boundary cannot add turns. A herd split retains contact only on the original
  identity; new herds start without contact.
- At the threshold, stable camp-ID order resolves competing Societies. The
  existing herd receives `DomesticCamp` and `OwnerSociety`; ID, species, animal
  count and fractional biological state are retained. The shared
  `TechnologyDiscovery.Acquire` Close effect adds Husbandry if needed. An
  already knowledgeable Society still receives the real herd. No ID allocation,
  spawn, current-turn products, replanning or retroactive resource claims occur.
  The ordinary next movement/economy boundary handles the herd at its camp.
- New configuration fields are optional in the unchanged `ANTHROPOCENE-11`
  envelope. Missing `FieldDomesticationEnabled` loads false; missing affinity
  loads false, with no fabricated contacts. Old saved species and knowledge are
  retained. New worlds use the same defaults in `Model.cs` and `OpeningConfig`.
  Radius is validated 1–12; consecutive turns 2–100. The turn counter is an
  explicit temporal abstraction; changing turn duration changes elapsed years
  represented by six decisions without introducing a second clock.

## Species review

`CultivationAffinity` is a configurable **boolean separate from Domesticable**.
Only represented wild goats have it enabled by default. Goats are a historically
appropriate early domesticable species; the early Zagros evidence is described
by [Zeder and Hesse, Science 287 (2000), 2254–2257](https://doi.org/10.1126/science.287.5461.2254).
The binary crop-margin response is the selected gameplay simplification, not a
species-specific empirical affinity coefficient derived from that paper.
Cattle are not added: doing so would broaden generation and the opening cohort.

The existing defaults mark antelope, bison, goats, camels and horses
`Domesticable=true`. These legacy opening/grant defaults were reviewed:
antelope and bison are **not** used as historical domestication candidates for
this route. Their existing flags remain unchanged to preserve the opening
compatibility path; all four non-goat species have affinity false. A broader
opening-species correction is outside this change. Custom configurations can
set the two flags independently; both are required for this route.

## Measured five-versus-six-turn sweep

Base revision: `b5b39e2387a3fc275e31a368de1c0ef280ce6726` (`main` at start).
Runtime: .NET SDK 8.0.427, Linux x64, Release. Results are committed in
[FieldDomesticationSweep.csv](FieldDomesticationSweep.csv) and
[FieldDomesticationScale.csv](FieldDomesticationScale.csv). The raw per-Society
CSV and full logs are reproducible generated outputs under `TestResults/Issue12`.

The **natural** sample generates six matched 96×48 worlds, eight founding
tribes each, using production `OpeningConfig` with only dimensions/player count
and start spacing reduced. Seeds 1/4 use Continents, 2/5 Archipelagos, 3/6
Pangaea. Each initial save runs through 30 ordinary AI/resolver turns under
feature-off, threshold 5 and threshold 6. Fission remains enabled. Denominators
include Agriculture-primary daughter Societies that actually form villages;
these are not 47 independent worlds. No species, herd, field, technology or
AI order is inserted into the natural sample.

| Natural shape | Village Societies | Feature-off acquired via contact | Five turns | Six turns |
| --- | ---: | ---: | ---: | ---: |
| Continents | 18 | 0 | 0 (0%) | 0 (0%) |
| Archipelagos | 18 | 0 | 7 (38.9%) | 7 (38.9%) |
| Pangaea | 11 | 0 | 2 (18.2%) | 2 (18.2%) |
| Total | 47 | 0 | 9 (19.1%) | 9 (19.1%) |

Median time from village formation to first contact-acquired managed herd is
5 turns with threshold 5 and 6 with threshold 6 among successful natural
Societies. Six retains the same discovery count in this sample and adds a full
peaceful turn, so it is the conservative selected default. These limited-world
frequencies are observed probabilities, not calibrated universal route rates;
in particular the sampled Continents route did not activate by turn 30.

The **controlled approach** sample uses 40 matched seeds per distance/supply
combination (480 resolver runs across both thresholds), a pre-established
24-person agricultural village and eight real wild goats, with no scripted herd
movement or contact credits. Each run lasts 24 turns. Both use the default 5%
wander gate and normal goat growth. Supplied tiles contain 100,000 biomass and
water / 10,000 edible food; limited tiles contain 800 / 100 / 80 respectively.
This isolates the consequences of the existing ecological movement gate and
is not presented as a generated-world balance sample.

| Initial distance / supply | Five-turn success | Six-turn success | Median first managed turn, 5 / 6 |
| --- | ---: | ---: | ---: |
| 2, supplied | 39/40 (97.5%) | 38/40 (95%) | 5 / 6 |
| 4, supplied | 0/40 | 0/40 | — |
| 6, supplied | 0/40 | 0/40 | — |
| 2, limited | 40/40 | 40/40 | 5 / 6 |
| 4, limited | 40/40 | 40/40 | 11 / 12 |
| 6, limited | 39/40 (97.5%) | 39/40 (97.5%) | 13 / 14 |

Well-fed distant herds rarely approach under the existing willingness gate;
attraction deliberately does not compel them to move. The extra sixth turn
loses one supplied-distance-two success in the observation horizon, but does
not change the sampled limited-supply or natural success counts.

Exposure telemetry snapshots exact selected **herd/Society pairs before
resolution**, including the acquisition turn and herds whose endpoint leaves
detection range or whose village disappears. Before first acquisition, natural
runs recorded 95/173 selected herd-turns with a food or water shortfall at
threshold 5, and 110/187 at threshold 6. Limited-distance-six controlled runs
recorded 351/508 and 390/547 respectively; supplied runs had no shortfalls.
No selected herd disappeared during approach. Human starvation across natural
Agriculture-primary Societies was 5.12 expected deaths (physical demographic
ledger), unchanged between variants; controlled runs had none.

**Existing ecological limitation:** wild-herd shortfalls reduce reproduction
and drive subsequent movement; this baseline does not implement wild-herd
starvation deaths. Therefore observed herd survival does not validate a
starvation-safe approach. Managed animals retain the existing managed-herd
nutrition/mortality system after acquisition. This issue does not add a second
wild mortality model or change food/water allocation.

Every sweep transfer verifies a prior same-herd/Society streak of at least
threshold minus one. Focused tests separately prove that one encounter,
intermittent sight, attempted hunting, scout-only sight, duplicate villages
and repeated same-boundary calls cannot produce a rapid false positive.

## Scaling, RL and validation

Authoritative state is sparse in selected herd/Society contacts. A shared
pre-movement `PlanningIndex` supplies local encounters; there is no map-wide
tile scan or `dt / timestep` loop. Work is canonical sorting of camps/herds plus
`O(villages × r_detect² + local herd encounters + herds)` per turn, with local
field and candidate constants. Populations do not multiply contact work.
Pathological many-herds-per-tile density remains a local encounter cost.
Independent movement readers share an immutable attraction map; all contact
and ownership writes commit serially. No new privileged AI observation, reward,
clock or annual training work is introduced.

The scale probe measures attraction construction and Close together, using an
already-built warm planning index, a 320×320 map, eight-hex camp spacing and one
eligible herd per village. Medians of ten samples after two warmups:

| Villages / herds | Median ms | Allocated bytes / pass |
| ---: | ---: | ---: |
| 8 / 8 | 0.041 | 31,397 |
| 80 / 80 | 0.457 | 293,077 |
| 320 / 320 | 4.276 | 1,170,928 |
| 1,280 / 1,280 | 8.188 | 4,691,469 |

This isolates incremental mechanic cost at current, mature and many-agent
training scales, not full-world turn latency or a cold-index benchmark. Timing
noise is visible at 320; work and allocation follow active villages/herds.
`FieldAttraction` and `FieldDomestication` scopes permit full-turn attribution.

All 64 existing/new fixed suites passed with 68 PASS outputs in 49.7 seconds,
three independent lanes (peak working set 1,896 MiB). The focused issue-12 suite
has 77 assertions after adding an explicit matched-wander movement check.
Coverage includes zero-q and independent species gates, threatened/thirsty/hungry
movement, bounded detection versus actual village vision, contact resets,
competing Societies, real zero-kill hunts, exact animal/ID conservation,
already-known Husbandry, no same-turn managed products, next-turn integration,
partial-contact save round trips, missing legacy fields and scalar/four-worker
replay through conversion. Existing fixed suites cover the wider herd, economy,
generation and deterministic contracts. Unity APIs/presentation are unchanged;
no Unity Editor or manual UI validation is claimed.

Run from the repository root with .NET 8:

```bash
dotnet run -c Release --project Tools/Headless/Anthropocene.Headless.csproj -- --issue12-only
dotnet run -c Release --project Tools/Headless/Anthropocene.Headless.csproj -- 0 --jobs=3 --check-timings
dotnet run -c Release --project Tools/Headless/Anthropocene.Headless.csproj -- --issue12-sweep --issue12-output=TestResults/Issue12
python Tools/Docs/Check-CurrentContract.py
git diff --check
```

`--issue12-quick` reduces natural seeds to one and controlled seeds to four for
harness smoke validation; the tables above use the full sweep. In the managed
cloud environment the SDK executable is `/tmp/anthropocene-dotnet/dotnet` and
`DOTNET_CLI_HOME=/tmp/anthropocene-dotnet-home` keeps its writable first-run state
outside the read-only home directory.
