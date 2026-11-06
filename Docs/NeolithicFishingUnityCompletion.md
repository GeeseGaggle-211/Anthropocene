# Fishing Unity integration and Sailing

## Baseline and authority

Work starts from the fetched, verified default `main`,
`07f3501c2d5e047e44d61e85cc0d4613b54572d6` (6 November 2026), on
`features/neolithic-fishing-unity`. The October 5 contract is a physical-law
reference, not the production base. The current source includes resident Fishing
(`db14106`) and its bounded lookup fixes (`876179a`), later physical demography,
social relocation, field domestication and generation-16 terrain.

Read: Fishing Contract v0.9 (Drive `1eHySnLbn3f1RSwDWBjwxfpx3smkwXI3c`),
`NeolithicFishingImplementationPlan.md`, current simulation/pass contracts,
issues #41 and #9 including discussion, and the read-only
`feature/elder-rl-scaffold:Docs/MixedSubsistenceDiffusionValidation.md`.
No RL branch, training job, checkpoint or infrastructure is changed.

Reconciliation: current source implements the v0.9 direct-adjacency water graph;
the pass document's phrase “routes through traversable land” is inaccurate.
The October cold-water blocker has since been repaired behind the corrected
pressure gate. Both opening Fishing flags remain false. #41's results at
`a217fd20d8afb3c9310732340bb4d66e8c45da0d` are historical and are not expected
to numerically match today's terrain/demography. No physical parameter or social
baseline is tuned to hide a cohort regression.

## Abstraction and player behavior

Authoritative state remains camp workforce/inventory, society technology/practice,
sparse place-bound fisheries and the existing finite aquatic tile ledger.
The new inspection is derived, owner-only and pure. It shares the preparation
route search and catch envelope, uses dated owner snapshots outside vision and
zero-yield unsurveyed priors, and reads only the selected camp's past receipts.
UI caching keys on world, turn, camp and draft intent. Work scales with bounded
water-route area and active owned records, never elapsed years or population.
The simulation remains one analytical WEGO transition; no training interface or
reward changes are introduced.

Economy buttons now dispatch through `EconomicAllocationStrategy`, matching AI
and Prepare. Watercraft-only settlements expose Cloth and fixed Housing
priorities as well as Fishers and Fishing Tools. The full-width Fishing section
shows up to three proposed grounds, routes, distance loss, observed/remembered
food and condition, capital-derived q, work/rest status, catch/tool estimates,
material access warnings and last-turn food/village qualification receipts.
“Show ground and water route” centers the map and draws the legal route using
the existing path renderer. Forecasts are labelled estimates, not guarantees.

The menu's Fishing validation profile and CLI
`--anthropocene-fishing-validation` load `FishingValidationConfig.json` on top of
OpeningConfig for **new worlds only**. Existing natural landscape presets remain
available; the profile grants no technology, workforce, resources or capital.
General new-game defaults remain off pending balance and Unity acceptance.

## Validation (updated as milestones complete)

.NET SDK 8.0.419 installed locally in this cloud workspace. Full baseline fixed
suite passed (three lanes, 54.9 s, reported peak working set 2111 MiB); the runner
also executed its default 20-seed benchmark with zero illegal orders.
Milestone A focused checks pass: inspection 40, Fishing economy 47, resources 43,
observations 13, state 23, seasonal pressure 53 assertions. Lake/coastal and
fishing-only/mixed inspection fixtures cover route adjacency, snapshot/prior
separation, hidden-state perturbation, purity and player/AI intent parity.
Sparse-site and 1280-camp eligibility scale guards pass unchanged.

Unity 6000.5.10f1 is absent here. No Unity compilation, EditMode execution,
player capture or interactive playtest is claimed. Simulation test results do
not validate visual layout. Native Unity build/play verification is a release gate.

Raw logs and audit output are under ignored `TestResults/FishingUnity/`.
Issue #41 remains an explicit balance release blocker until matched Fishing
cohort evidence justifies closing it.
