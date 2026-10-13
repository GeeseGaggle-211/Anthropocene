# Native Windows production fusion experiment

This continues the approved local research experiment on Windows after the WSL
filesystem became inaccessible. It reconstructs the production-fusion files from
the Windows recovery drafts; it is **not** a recovery of the WSL branch's full
commit history or its measurement artifacts. Prior reports remain historical WSL
results and must not be mixed into Windows timing comparisons.

The base remains the exact dataset-readiness simulator pin
`5559961164d6f9da8e6d20c1dc563ecec3eb04b7`. Remote main was checked with `ls-remote`
and remains `86cba712dfd76673bd9637052543bde85f810e0b`. A normal fetch of the native
repository failed on an unrelated invalid `refs/codex/turn-diffs/checkpoints`
object; no user refs or work were removed to repair it. An isolated native
worktree uses `spike/gpu-ecology-equivalence` and leaves the existing checkout's
branch, modified art metadata and untracked project settings untouched.

The user-authorized CPU rule change is a separate native commit, `55d4642`:
craft experience accumulates during production, but mastery/specialist unlocks
commit at Close. It reconstructs the reviewed WSL commit `71c08e1`; the different
parent/history means the new commit has a different SHA. All GPU comparisons use
this changed CPU baseline. The original pilot is historical provenance, not a
claim that its turn semantics are unchanged.

## Contract and boundary

After CPU material gathering, pack one camp's prior inventories, typed food,
remaining role labor, tool coverage, housing reservations and fixed production
targets. CPU fixes the recipe/role sequence, eligibility and rates. The kernel
executes this sequence in order, reads/writes that lane's private ledgers, and
returns the final state and per-command receipts. CPU commits fixed housing,
practice receipts and demographic accounting canonically. Practice accumulation
is still shared society state; it is never written concurrently by GPU lanes.

One camp uses one lane; no atomics, unordered reductions, shared writes or new
strategic decisions. Camps remain in their existing sequential resolution order.
Exports, goods storage, legacy portable fixed-housing inventory, missing prepared
targets/plans and capacity overflows take the actual CPU fallback. Coverage must
be reported. This is an intentionally bounded experiment, not production code.

Three comparisons distinguish layout, residency and fusion:

1. Packed CPU executes the same flat-buffer arithmetic.
2. Resident GPU uses one upload, a separate kernel per ordered command, one readback.
3. Fused GPU uses one upload phase, one kernel for the ordered sequence, one readback phase.

Each upload phase currently performs three host-to-device copies (state,
commands, counts), and readback performs two device-to-host copies (state,
receipts). This is not literally one copy in each direction. The baseline
transfers capacity for 128 commands even when fewer are live. A separately
labelled `fused-compact` one-camp experiment transfers only the live commands and
receipts; lane-zero kernel addressing and arithmetic are unchanged. Zero-command
camps do not launch a kernel. Validation covers this path independently.

The old per-call production cap kernel is also measured on identical whole-turn
inputs. Other GPU subsystems stay off. Flat buffers include unused capacity;
fusion reduces launches and repeated synchronization but does not automatically
reduce uploaded bytes. There is no CPU shadow production execution hidden in
packing. Reflection extracts two existing private ledgers only in this harness.

The generated packed CPU arithmetic is not an independent oracle. Validation
captures actual CPU output state and receipts, checks discrete receipt existence
exactly, and requires floating error <= 1e-8 + 1e-10 * max(abs(a), abs(b)). These
double-precision tolerances allow rounding from arithmetic translation while
remaining small relative to resource/labor units. Report max, mean and RMS error.
Capture itself must preserve the authoritative CPU endpoint fingerprint exactly.
Same-build/hardware GPU repeatability requires identical whole-state fingerprints
over two independent 50-turn processes; CPU drift is reported separately.

## Native execution

Windows .NET 8 (the existing Unity-bundled SDK), Python for guarded source-copy
generation, and ILGPU/ILGPU.Algorithms 1.5.3 with CUDA on the GTX 1650. No Unity
player, shader compilation, WSL, Linux SDK, or nvcc is needed. Lock files pin NuGet
restores. The CPU project directly compiles authoritative simulation sources;
the research project generates narrowly guarded copies for hooks.

From the repository, use `Tools/GpuEcology/Run-Native.ps1 -Mode ...` for baseline,
device, validate, bench, matched, compact-matched, boundary and trajectory. Trajectory also accepts
`-Backend fused-compact -RunId 1`; run IDs 1 and 2 in separate processes, then run
`python Tools/GpuEcology/verify-native-repeatability.py Tools/GpuEcology/fusion-results fused-compact`. Run validation
before timing. Build/setup/kernel compilation are measured separately and not
part of steady-state timing. Do not run builds or other benchmark jobs alongside
a timing run.

Baseline regenerates seed-11 Standard 1024x512/80 and Huge 2048x1024/240 openings
(240 uses the existing benchmark-only override) and continues for 50 transitions.
Player 0 has empty orders; other tribes use ordinary AI. Before/after checkpoints
are retained at 25/40/50. Whole-turn timing covers Draft, Prepare, Resolve and
Refresh, including packing, transfer, readback, CPU commit and fallbacks; save,
fingerprinting and world loading are outside this timing window.

The phase benchmark scales 1/16/64/256/1024/4096/16384 independent **camp sequences**,
not independent full worlds. Inputs are replayed CPU captures. It separates
transfer wall time from CUDA event kernel time, warms each path, and reports
seven samples against both scalar and four-worker packed CPU execution. The
phase benchmark retains padded lane buffers; compact transfers are measured
separately for one-camp whole-turn dispatch. The `fused-dense` phase variant also
reduces batched command/receipt stride to that batch's longest actual sequence,
with the same numerical kernel, and compares with a four-worker CPU using the
same dense layout. It leaves state width unchanged. Whole-turn matched comparisons use alternating order and three
measured samples per mode/checkpoint after warmup; the compact comparison repeats
CPU/padded-fused/compact-fused at turn 50 on both maps. Utilization sampling is
coarse and cannot attribute brief kernels independently of desktop GPU work.

The phase microbenchmark explicitly disables CLR tiered compilation so a later
batch size cannot gain an unfair advantage from a mid-sweep JIT optimization.
Its first exploratory sweep showed this effect and is superseded. Whole-turn
runs keep the normal tiered runtime with an untimed warmup and 50-turn chronology;
do not infer whole-turn speedups directly from microbenchmark ratios.

Generated large saves and capture fixtures are ignored. Regenerate rather than
copying unverified WSL snapshots. No merge or production integration.

Native runs use a 6-GiB managed-heap hard limit on this 14-GiB usable-RAM host.
Full collections before timed steps and after serialization discard checkpoint
and fingerprint buffers outside the measured simulation work. These settings
apply equally to all compared backends. The first uncapped chronology failed
on disk exhaustion while writing Huge turn 40 and was discarded. A 4-GiB attempt
failed on memory at Huge turn 32 and was also discarded; only a completed
replacement bounded-memory run is evidence. These are simulation pass timings,
not end-to-end UI latency; the reported windows exclude collection immediately
before the turn, serialization and loading.

See `NATIVE_FUSION_RESULTS.md` for final measured results, independent review,
coverage limitations and the small post-measurement boundary correction.
