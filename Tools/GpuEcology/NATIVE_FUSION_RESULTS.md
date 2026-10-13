# Native Windows fusion results — 13 October 2026

Recommendation: **keep as a research branch**. Native CUDA works, fusion is
deterministic on this build/hardware, and compact production matches the changed
CPU baseline exactly. It does not improve whole-turn performance. No merge.

## Provenance and scope

Branch: `spike/gpu-ecology-equivalence`. Simulator base:
`5559961164d6f9da8e6d20c1dc563ecec3eb04b7`, the dataset-readiness pilot pin.
Main and remote main were checked at
`86cba712dfd76673bd9637052543bde85f810e0b`; the research branch deliberately
retains the pinned simulator rather than importing unrelated main changes.
Craft unlock deferral is the separate, explicitly authorized CPU rule change
`55d4642117c5e1ef80d3709e308217f4162a5d40`: experience accumulates during economy,
technology and specialist availability commit at Close. New capabilities are
available on the following turn. Existing subsistence discovery already uses
Close. This is the authoritative CPU baseline for every comparison below.

The native branch reconstructs the recovered production research files; it is
not a recovery of the inaccessible WSL branch's full history. Historical ecology,
mixed-precision, dense-land and other subsystem reports remain separate evidence.
No WSL repair, simulator migration or Unity player build is required to run this
native harness. The original dirty Windows checkout and its branch were preserved.

ILGPU/Algorithms 1.5.3, .NET SDK 8.0.318 from Unity 6000.5.10f1, Windows build
26200, GTX 1650 with 4 GiB VRAM and driver 555.99. This fits the plain-C# simulation
without Unity/HLSL integration. Double precision and ordered lane-local arithmetic
are retained in this fusion pass; no mixed-precision claim is made here.

## Boundary and methodology

Authoritative production is `NeolithicProduction.ResolveCampNeolithic` and
`Produce`, with `ToolCoverageBudget`, `HousingMaterialPlan` and fixed prepared
`TurnNeeds.Production` targets. CPU material gathering precedes the boundary.
The transition is (camp inventory, food, remaining labor, tool/housing ledgers,
prepared targets) + ordered CPU-selected recipes/rates + interval -> final ledgers
and ordered production receipts. CPU commits fixed housing, demographics and
society practice canonically. No GPU shared writes, atomics or reductions.

The two recipe passes reflect a real dependency: toolmaking feeds later equipment
work. A lane executes both in their original sequence. Camps remain sequential
in whole-turn integration because they still share other state. This experiment
does not globally batch camps or fuse unrelated turn passes.

Resident uses one upload phase, one kernel per command, then one readback phase.
Fused uses one kernel for the sequence. Each transfer round still has five
physical copies: state/commands/counts to GPU, state/receipts back. Compact sends
only live commands for one-camp dispatch; batched dense uses the longest actual
sequence as stride. All retain 192 doubles of private state per lane. Coefficient
and recipe reuse is limited to the packed sequence within that dispatch, not
persistent residency across turns. No aggressive numerical simplification.

Seed 11, Standard 1024x512/80 tribes and Huge 2048x1024/240 (benchmark override),
four CPU workers, ordinary AI except empty player-0 orders. Each world continues
for 50 resolutions. Checkpoint timing replays identical prior states at 25/40/50,
warms each mode and alternates ordering across three measured repeats. Whole-turn
time includes Draft, Prepare, Resolve and Refresh, including GPU bridge overhead.
It excludes loading, saving, fingerprints and explicit pre-turn full collection.
The same 6-GiB managed heap limit applies to all modes. These are simulation-pass
times, not user-interface latency. Build/device/kernel setup is outside timings.

Phase timings are seven-sample medians on prepacked independent camp sequences,
with five separately measured upload/kernel/download samples. CUDA events time
the kernel; transfer durations are host wall time. Their separate medians need
not sum to the end-to-end median. Tiered JIT is disabled for the phase sweep after
an exploratory sweep showed mid-run optimization; whole-turn runs use normal
tiering with warmup. The exploratory sweep was superseded.

## Correctness and repeatability

| Checkpoint | Standard captured sequences | Huge captured sequences |
|---|---:|---:|
| 25 | 95 | 283 |
| 40 | 140 | 506 |
| 50 | 195 | 922 |

All 2,141 sequences match actual CPU states/receipts on packed CPU, resident GPU,
fused GPU and compact fused GPU: **max/mean/RMS absolute error all 0**, discrete
mismatches 0. Criterion was exact discrete results and floating tolerance
`1e-8 + 1e-10 * max(abs(cpu), abs(candidate))`. All six capture checkpoints have
CPU-identical world fingerprints. The generated packed CPU is a comparator,
not the independent oracle; captures come from the actual CPU implementation.

All 90 initial matched rows and all 18 compact-comparison rows match their CPU
checkpoint fingerprint. Every one of 42 phase rows also has max error 0.

Two independent compact-GPU processes, each running both worlds for 50 turns,
match exactly at **100/100 world/turn checkpoints**. They also match the changed
CPU baseline at 100/100 checkpoints; population, camp, technology-count and
production drift are all zero. This includes 1-, 10- and 50-step evolution.
Whole-world equality means authoritative persisted-state fingerprints; captured
production states and receipts were checked separately. Transient object fields
outside those checks are not claimed to have been exhaustively compared.
No new 100/1000-turn production-fusion evolution is claimed. Cross-hardware or
cross-build determinism was not tested.

Independent review found a nonpositive `TurnRecord.Duration` housing timestamp
fallback discrepancy. It is fixed by retaining intervals and using the CPU
fallback `w.Days + intervals * BaseDays`. Six direct tests compare authoritative
`Produce` with packed CPU and compact GPU for durations 0, -1 and 45, including
positive production, existing housing decay, timestamps, stocks and labor: pass.
The final native build succeeds. Earlier CPU validation passed 672 pastoral-craft
and 55 foundation assertions, including the new Close timing checks.

Timed runs and the two trajectories used assembly SHA256
`518C7BEFB4E2F02CC5CCC939CF13E5E7575C289E7CEC99364819E17BA814D5DD`.
The final build adds the boundary correction/test and its study mode; its hash is
recorded in `fusion-results/boundary-run-metadata.json`. Timing records all had
positive duration; the corrected fallback is outside their executed path.
Performance and 50-turn trajectories were not rerun after that narrow correction.

## Whole-turn performance

Three-sample median milliseconds; first experiment has five matched modes:

| World / turn | CPU | Packed CPU | Per-call GPU | Resident GPU | Fused GPU |
|---|---:|---:|---:|---:|---:|
| Standard / 25 | 105.82 | 105.92 | 191.22 | 198.76 | 184.27 |
| Standard / 40 | 147.09 | 123.08 | 242.20 | 225.59 | 227.93 |
| Standard / 50 | 167.75 | 165.67 | 322.78 | 348.13 | 299.92 |
| Huge / 25 | 430.70 | 423.77 | 672.65 | 663.40 | 629.25 |
| Huge / 40 | 682.93 | 661.38 | 1114.25 | 1016.17 | 989.25 |
| Huge / 50 | 1224.33 | 1203.87 | 2066.21 | 1772.74 | 1714.12 |

Separate matched compact experiment, again three-sample medians:

| World / turn 50 | CPU | Padded fused | Compact fused | Compact / CPU |
|---|---:|---:|---:|---:|
| Standard | 169.45 | 332.00 | 309.02 | 1.82x |
| Huge | 1233.51 | 1625.50 | 1548.78 | 1.26x |

Compact reduced the median versus padded fusion by 6.9% and 4.7%, respectively.
Small sample counts and overlapping ranges do not establish statistical
significance. Do not compare a mode from one experiment with a control from the
other to infer a causal speedup. Raw sample ranges and allocations are retained.

Over all 50 turns, chronology mean CPU/compact-run1/compact-run2 times were
125.03/178.49/182.76 ms for Standard and 572.45/757.89/760.97 ms for Huge.
These changing-world chronological averages supplement the matched comparisons.

Mean active tile integrations were 3,565.6 per Standard world/turn and 13,826.7
per Huge world/turn; turn 50 used 4,006 and 16,691. These are naturally active
tiles across ecology, not an 80-90% land activation experiment. That earlier
dense-land ecology stress study remains separate; fusion here concerns production.
At turn 50 there are 205/969 camps and population 66,947/332,812. CPU production
took 8.61/70.74 ms of 166.37/1285.57 ms in the chronological baseline: about 5-6%
of the turn, and the ported arithmetic is only part of that production time.

## Batch crossover diagnostic

Counts are independent replayed **camp sequences**, not full worlds. All lanes
are active; active command counts differ. This sweep is not a new world-count
or land-sparsity benchmark. It bounds one offload shape without asserting that
all real camps can safely execute concurrently.

| Sequences | Scalar CPU ms | CPU 4-worker dense ms | Fused dense total ms | GPU kernel ms | Transfer fraction |
|---|---:|---:|---:|---:|---:|
| 1 | 0.0007 | 0.0047 | 0.1656 | 0.0412 | 72.7% |
| 16 | 0.0080 | 0.0103 | 0.3798 | 0.2368 | 32.0% |
| 64 | 0.0435 | 0.0230 | 0.7969 | 0.5339 | 30.0% |
| 256 | 0.1498 | 0.0629 | 1.5475 | 0.8601 | 45.4% |
| 1024 | 0.6537 | 0.2244 | 3.5996 | 1.3614 | 62.3% |
| 4096 | 4.2628 | 1.5957 | 9.7806 | 1.5981 | 83.5% |
| 16384 | 17.3571 | 6.9826 | 39.6426 | 5.3089 | 85.6% |

No transfer-inclusive crossover, even against scalar CPU, through 16,384
sequences. At that size GPU kernel time is below four-worker CPU time, but
20.18 ms upload plus 11.36 ms download dominates. Dense GPU throughput is about
413,293 sequences/sec, versus 2.35 million/sec for four-worker CPU. Padded fusion
at 16,384 took 202.69 ms, showing how much layout/traffic matters in this harness.
This does not isolate cache misses, occupancy or register spilling; hardware
counter profiling remains unmeasured. CUDA event timing is not such a profile.

Full padded device buffers are allocated for the maximum batch: about 328 MiB
including separate input/output allocations. Dense transfer volume at 16,384 is
199,819,264 bytes round trip (190.6 MiB), versus 369,164,288 padded bytes (352.1 MiB).
The 1-Hz whole-device telemetry during phase benchmarking averaged 28.6%
utilization, peaked at 49%, and reached 730 MiB used VRAM. Compact whole-turn
comparison averaged 20.0%, peaked at 43%, and reached 459 MiB. These include the
desktop, loading, setup and validation intervals; they are not kernel occupancy.
CPU chronology process working sets peaked at 1.66 GiB Standard / 4.11 GiB Huge.
Phase device/setup time was 912 ms, excluded from steady-state results.

## Review, limitations and next direction

Independent reviewer verdict: **PASS WITH LIMITATIONS**, no remaining actionable
semantic finding after the timestamp fix. All 2,141 captures were eligible;
storage/export/capacity CPU fallbacks are present but these real-state samples
did not dynamically exercise them. Only one seed/two sizes were measured.
Large save/capture fixtures are ignored and regenerated by the harness. Failed
uncapped and 4-GiB baseline attempts were discarded; only the completed 6-GiB
baseline is evidence. Native timings must not be mixed with old WSL timings.

This supports exploring longer-lived device state and fewer CPU/GPU boundaries,
not expanding production integration yet. The promising diagnostic is a device-
resident multi-world phase pipeline with canonical CPU commits at necessary
boundaries, tested first on a small controlled state surface. A compact structure-
of-arrays layout and CPU batching are also reasonable experiments. Every proposal
needs a fresh dependency audit, especially shared society budgets and recipe order.
Nothing here establishes a full-world RL crossover. Population intrinsic state
is a possible next research candidate if it participates in that resident batch;
another isolated tiny offload is not supported by these measurements.

## Exact native code scope

The CPU commit changes Model.cs, NeolithicProduction.cs, Societies.cs,
Docs/TurnResolutionPasses.md and Tools/Headless/PastoralCraftChecks.cs only.
All remaining code is under Tools/GpuEcology: NativeProgram/WholeTurnProbe for
baseline chronology; FusionKernel/FusionCpu/FusionBridge/FusionStudy and
FusionBoundaryChecks for the experiment; CandidateBridge/CandidateKernels for
the per-call comparator (other candidate backends off); CpuProbe/TurnProbe
projects, locked packages and guarded generated-source hooks; Run-Native.ps1,
generate-fusion-cpu.py and verify-native-repeatability.py for reproduction.
Documentation, ignore rules and raw small JSON/CSV evidence accompany them.
No production GPU integration, main merge, strategic system rewrite or RL change.
