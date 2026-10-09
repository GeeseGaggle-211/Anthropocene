# Native Elder RL boundary

Branch: `feature/elder-rl-scaffold`

Issue [#43](https://github.com/GeeseGaggle-211/Anthropocene/issues/43)
adds the model-neutral boundary needed to attach a later PPO/Gymnasium adapter.
It deliberately contains no trainer, Python bridge, ML runtime, or learned
weights.

## Authority boundary

The policy chooses five bounded strategic controls:

1. operation of retained fields;
2. retention of the existing managed herd;
3. a common Development labour budget;
4. the field/herd split of that budget; and
5. one categorical movement candidate, including explicit `stay`.

These are targets, not worker counts or recipes. `NativeElderDirectiveCompiler`
maps them through the production DemandControl planner. The simulation retains
authority over legality, placement conflicts, integer staffing, inputs, tools,
recipes, trade, fishing access, ecology, immigration, fission, and turn
resolution. A rejected or physically repaired target is reported as an
`ElderIntervention`; an invalid categorical action never silently clamps to a
different destination.

The turn sequence is the ordinary WEGO sequence:

```text
directive -> ordinary movement order -> placement resolution
          -> committed-state trade -> destination-aware fishing access
          -> compile the same directive at the accepted tile
          -> production and needs -> immigration/fission
          -> next observation and reward
```

## Versioned contracts

`ElderResetRequest` requires exactly one authoritative snapshot or registered
fixture. It also carries the seed/random-stream configuration, controlled settlement,
background policy, episode horizon, movement/shock gates, and expected
simulation/configuration/schema versions. Reset returns the first observation,
all schema versions, build/configuration hashes, initial state fingerprint, and
controlled/scenario metadata.

Native v1 accepts random stream `0` only: keyed simulation randomness belongs
to the authoritative snapshot's world seed. Any other stream is rejected
instead of being accepted as an inert label. `CaptureSnapshot` captures world
state for a new Reset; it is not an in-episode checkpoint and therefore starts
a new episode horizon, reward normalizer, and previous-action history.

Observation schema v1 has a checked-in 512-float layout:

| Block | Offset | Shape |
| --- | ---: | ---: |
| own settlement/core/history/capabilities | 0 | 128 |
| local site candidates | 128 | 7 x 12 |
| fishing candidates | 212 | 3 x 12 |
| owned herd groups | 248 | 8 x 8 |
| own commodity summaries | 312 | 11 x 8 |
| remembered contacts | 400 | 8 x 8 |
| presence/overflow and masks | 464 | 37 |
| reserved migration space | 501 | 11 |

The registry records source, units, missing-value behavior, and permission for
each range. Raw camp, society, player, herd, and tile identifiers are not policy
features. Movement tile IDs exist only in the trusted metadata sidecar used by
the compiler. Foreign live inventories and unseen terrain never enter the
observation; contacts come from the existing permission-filtered game-knowledge
view. Every bounded collection reports presence and overflow.

## Reward and replay

The invariant welfare flow is:

```text
raw = prior_population * realized_local_QoL * elapsed_years
scalar = raw / (initial_population * elapsed_years)
```

The raw terms and normalized scalar are both reported, alongside diagnostic
food, water, service, continuity, ecology, settlement, capital, and catastrophic
loss channels. Those diagnostics are not tunable reward weights. The prior
cohort is fixed before resolution, so fission does not assign the parent a
daughter settlement's later welfare.

Replay records the authoritative initial snapshot, build/configuration/schema
identity, requested and accepted actions, interventions, frozen background
orders, exogenous events, and every post-step state fingerprint. Verification
replays recorded background orders and rejects build, configuration, schema,
accepted-action, or state divergence. A seed by itself is not replay evidence.

## CLI checks

With .NET 8 installed:

```bash
dotnet run --project Tools/ElderRL/Anthropocene.ElderRL.csproj -c Release -- schema
dotnet run --project Tools/ElderRL/Anthropocene.ElderRL.csproj -c Release -- reset-smoke
dotnet run --project Tools/ElderRL/Anthropocene.ElderRL.csproj -c Release -- step-smoke
dotnet run --project Tools/ElderRL/Anthropocene.ElderRL.csproj -c Release -- replay-smoke
dotnet run --project Tools/ElderRL/Anthropocene.ElderRL.csproj -c Release -- benchmark --steps=2
```

The benchmark reports 1/4/8/16/32-environment throughput. Fixed headless checks
cover schema accounting, permission boundaries, all five causal action heads,
explicit intervention reporting, Option A parity, scalar/parallel determinism,
save continuation, movement, Reset/Step, and action-driven replay.

## PPO handoff boundary

The native side is ready for a separately reviewed adapter to:

- encode `ElderObservationSchema` into a Gymnasium observation;
- expose the four continuous controls and masked movement categorical;
- batch persistent `Reset`/`Step` environments; and
- train/evaluate PPO while retaining native replay artifacts.

That adapter must preserve schema versions and action masks and must not move
simulation authority into Python. Synthetic shocks remain an explicitly rejected
v2 option until production disaster mechanics exist. The first native episode
limit defaults to 32 real turns; training cadence and PPO hyperparameters remain
adapter-level research choices.

## External training harness

Python training, evaluation, dataset and export work lives in the separate
private repository
[`GeeseGaggle-211/Anthropocene-RL`](https://github.com/GeeseGaggle-211/Anthropocene-RL).
`training-repository.json` pins the exact training skeleton commit expected by
this branch. The training repository independently pins this simulator commit
and schema set in `configs/simulator/elder-v1.yaml`.

This is a manifest relationship, not a Git submodule. Gymnasium, PyTorch,
RLlib, checkpoints and training datasets do not enter the Unity/C# repository.
Any change to simulator physics, reward meaning, observation permissions or the
action contract requires updating both manifests and rerunning the native parity
and regression gates before a new training campaign.
