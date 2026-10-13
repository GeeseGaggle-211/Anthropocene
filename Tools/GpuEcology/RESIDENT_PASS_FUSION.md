# Shared ecology projections across turn passes (native research)

This experiment continues `NATIVE_FUSION.md` on the same isolated branch and
CPU baseline. It changes only research harness files. The earlier, separately
committed end-of-turn technology rule remains in both CPU and GPU runs. No
Unity production backend is installed and no branch is merged.

## Why this boundary

The expensive late-turn path is not a single tile update loop. Draft includes
observations, forecasts and an allocator; Prepare validates plans and constructs
needs; Resolve generates herd intentions, prepares claims, integrates resources,
commits receipts, runs economy and performs demographic/knowledge work.

`LazyEcology.ProjectEcology` is read repeatedly by several of these consumers.
Its intrinsic land calculation has three ordered operations that can share one
lane: mean/end cultivation-capital transition and opening reclassification;
seasonal affine recovery; endpoint reclassification and clamping. Seasonal
capacity/rate profiles can persist across turns. This is the tested shared-state
boundary. It is not a port of the allocator, herd decisions or an entire economy.

`TurnPlanning.AutomaticIntent` calls the economic allocator or Elder compiler.
The allocator's residual time includes hypothetical evaluations and branching
decisions, not just ecology projection. Calls to `RecoverLandAfterHarvest` on
hypothetical views are not interchangeable with a current-time projection.
`TurnHorizonResources.CommitHorizonResources` also contains genuine barriers:
materialize opening stocks; reconcile ownership/tool budgets and capital plans;
construct fixed claims; integrate independent tiles; then commit ordered receipts.
The opening projection is reusable. The whole prepare/integrate/commit sequence
cannot simply be fused across these CPU dependencies. Keeping full dynamic state
on GPU would require moving these consumers and their ordered bookkeeping too.

## Variants actually executed

* `off`: existing CPU reference, matched current harness build.
* `cpu-cache`: CPU reference computed on cache miss, shared guarded results.
  This control separates reuse from GPU arithmetic.
* `gpu-split`: three kernels share resident dynamic lane state, with one dynamic
  upload and readback per turn. Static profiles persist on device.
* `gpu-fused`: the same three operations in one kernel, eliminating two launches
  and intermediate lane loads/stores.
* `gpu-coeff`: fused kernel with resident composed annual affine coefficients
  for uncultivated, aligned complete-cycle intervals. Integer exponentiation
  replaces generic power for that path. Cultivated, partial-cycle and zero-length
  intervals use the literal kernel. This is a separately labelled arithmetic
  experiment, not proof of bitwise CPU equivalence.

ILGPU 1.5.3 / native CUDA is reused because the headless simulator is C# and the
local GTX 1650 is already verified with this runtime. No Unity graphics context
or WSL dependency is introduced. All channels remain double precision.

## Contract and residency

Input is an authoritative tile's Food, PotentialFood, Freshwater, Saltwater,
last-update time, cultivation capital/operation, natural seasonal capacity/rates,
moisture and salt forcing, configuration, and requested world time. Output is
the projected EcologyView; a read does not materialize the tile. The original
CPU callback is invoked once on a cache hit to preserve companion Wood recovery.
GPU lanes have no shared writes, atomics or unordered reductions.

Eligible states are land, unit land/aquatic condition, natural vegetation, and no
fishery. Explicit aquatic overlays, other query horizons/worlds, changed states,
unpredicted tiles and capacity overflow use the actual CPU path. Cache guards
compare every EcologyView field and cultivation operation; writes therefore
invalidate stale projections. No pressure, ownership or strategic decision is
invented on GPU.

The active batch is the sorted accumulated union of previously observed eligible
query IDs, capped at 131,072 resident slots. Turn one learns IDs through CPU
misses. This deliberately simple predictor can overfetch; it does not use a
future trace or a CPU shadow pass during timing. Host copies of returned views
serve synchronous CPU consumers, avoiding per-query device round trips.

Static profiles and coefficient buffers stay on GPU. Dynamic stocks are uploaded
each turn from the CPU authority, and projections read back once. This is partial
residency, not a permanently device-resident simulation. Profiles consume 32 MiB,
lane storage about 11 MiB, coefficient storage 9 MiB at capacity, excluding CUDA
context/allocator costs. Dynamic round-trip traffic is 176 bytes per predicted
tile and first-use profiles add 256 bytes per new tile.

World, config-reference or backend replacement requires Reset (enforced). Within
one fixed benchmark, configuration values and natural seasonal forcing must be
immutable. Editing the same config object or climate forcing in place is outside
this cache contract. Slots and cache are reset between worlds and fresh processes.

## Validation and timing

`Run-Resident.ps1` records assembly hash, OS, settings and 1 Hz device telemetry.
Build Release first using the native Unity-bundled .NET SDK; then invoke modes
`validate`, `profile`, and `trajectory -Backend <variant> -RunId <id>`.
`summarize-resident.py [output-directory]` rejects incomplete trajectories or
mixed assembly hashes and enforces exact repeated whole-state fingerprints for
available fresh-process run pairs.

Validation replays real CPU checkpoint openings at turns 25, 40 and 50 for both
maps. CPU-cache must match the stored authoritative CPU whole-world fingerprint.
Its query IDs seed an untimed GPU coverage fixture. Each GPU projection is checked
against the actual CPU reference: time and vegetation exact; finite floating
channels within `1e-8 + 1e-10 * max(abs(CPU), abs(GPU))`. This allows GPU double
transcendental rounding far below one resource unit. Max/mean/RMS absolute error
are retained. Validation counters accumulate across the three modes within a
checkpoint; use the final cumulative row once, not the sum of all rows.
Full-world CPU fingerprints may differ after consuming tiny float differences.
Same-build/hardware GPU repeatability and CPU observable drift are reported
separately. Population and camp-count drift are tracked each turn; these are not
an exhaustive ecological or strategic drift measure.

The 50-turn trajectories start from identical seed-11 openings: Standard
1024x512/80 tribes and Huge 2048x1024/240 benchmark override. Four CPU workers,
6 GiB managed heap cap, one CPU warm-up world step, fixed backend per fresh
process. GPU initialization/compilation is reported separately. New-profile
construction/coefficient setup during turns is charged to measured prefetch.
Whole-turn time includes prefetch, Draft, Prepare, Resolve and Refresh. Explicit
pre-turn GC, serialization/fingerprinting and telemetry sampling are outside
the turn timer; allocations/collections triggered within a turn remain included.
This is simulator turn execution, not UI/render/save latency.

The requested windows are inclusive turns 20–50 (31 samples) and 40–50 (11).
Report mean and total window time, not turn-50 medians. Detailed scope profiles
at 25/40/50 use one worker and extra instrumentation: they locate work but cannot
be substituted for four-worker late-window timings. Nested resource timings
must not be added again to their Ecology parent. CUDA events measure kernels;
host clocks measure upload/readback including synchronization. Device-wide 1 Hz
telemetry includes desktop activity and idle/serialization gaps; it is not
process-specific utilization or measured kernel occupancy.

These are one-world trajectories, one seed, on one build/device; no claim about
RL multi-world crossover or dense 80–90% land coverage follows from them. The
131,072-slot experiment cannot cover 80–90% of a Huge world's land. Earlier dense
stress tests are separate evidence. A validation process initially retained a
previous full-world clone and exhausted its heap; explicit clone detachment was
added before the successful final validation and timed runs.

## Files

`ResidentKernel.cs`: literal split/fused and coefficient arithmetic.
`ResidentBridge.cs`: resident allocation, sparse prediction, cache guards,
timing and differential checks.
`ResidentStudy.cs`: oracle gate, scalar profile and complete trajectories.
`TurnProbe/build-candidate-hooks.py`: assertion-checked generated wrappers; the
authoritative source files are not rewritten.
`WholeTurnProbe.cs`: stages and fully charged prefetch timer.
`NativeProgram.cs`: command route.
`Run-Resident.ps1`: fixed native launch and telemetry.
`summarize-resident.py`: reproducible window/phase/repeatability summaries.
`resident-results/`: raw small JSON/CSV evidence; large world fixtures remain in
the existing ignored checkpoint directories.
