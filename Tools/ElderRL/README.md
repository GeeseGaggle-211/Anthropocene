# Elder RL scaffold

Branch: `feature/elder-rl-scaffold`

Base: `fix/economic-pastoral-emergence@dddac82c8d3a98ab6f5f39dffabbff3014b92b5c`

This directory is the first model-neutral scaffolding for the Village Elder learned-supervisor experiment. It does **not** train a model and it does **not** change production economic behavior yet.

## Locked boundary

The learned Elder chooses only strategic targets. The deterministic simulation remains authoritative for:

- legal actions and masks;
- settlement placement/conflict resolution;
- workforce compilation and integer staffing;
- recipes, materials, tools and production ceilings;
- trade matching and settlement;
- immigration and fission mechanics;
- ecology, herd grazing/water, fishing and physical resolution;
- save/fingerprint/replay state.

The current Option A supervisor remains a comparator. It must not be used as an unquestioned profitability mask when the learned policy is active.

## Schema v1 actions

`ElderDirective` currently exposes:

1. retained existing-field operation ratio;
2. retained existing-herd ratio;
3. discretionary Development budget fraction;
4. field share of that Development budget (the v1 remainder is herd development);
5. a masked local movement candidate (`-1` means stay).

These values are not worker counts.

## Observations

`ElderObservation` is semantic and versioned rather than a fixed ML tensor. It has named blocks for:

- core welfare/demographics;
- existing field capital/service;
- herd/support state;
- bounded local site candidates;
- bounded fishing summaries;
- capabilities/technology;
- commodities;
- economically relevant contacts;
- future obligations (taxes, tribute, requisitions, etc.);
- optional v2 shock state;
- legal action masks.

A Python/framework adapter may flatten schema v1 for a small MLP experiment. Later set/token encoders should reuse the same semantic records rather than require the C# simulation to adopt ML-library data structures.

## Fission environment baseline

The RL branch uses `CurrentPlusExitOpportunity` as the default fission methodology:

- `FissionExitThreshold = 0.05`
- `FissionExitSearchFraction = 0.8`
- existing crowding/stayer-gain remains active;
- existing food/water distress remains active;
- the relative exit channel uses `(1 - Q_home) * max(0, Q_daughter_expected - Q_home)`;
- daughter opportunity is expectation-neutral for pressure, while actual post-move daughter QoL remains a viability veto.

Fission is an environment/social process, not a direct Village Elder action in the first learned-supervisor experiment. A locally optimizing Elder therefore experiences population loss or redistribution when a subgroup has a credible better alternative. Newly founded daughters may initially run the conservative heuristic supervisor; later they can instantiate the same shared learned Elder policy with their own local observation and reward.

The rejected absolute low-QoL trigger remains available only as an experimental comparator. It reproduced minimum-size fragmentation under the modern radius/spacing/daughter-viability machinery and is not the baseline.

## v1 / v2 curriculum

**v1**: ordinary settlement strategy using the real simulation systems.

**v2**: seeded, semi-random surprise shocks in the sandbox. Synthetic shocks may damage food/biomass, water, housing/capital, stored goods, herds or fishing support. They remain explicitly training-only until matching production disaster mechanics exist.

Shock events must be replayable and may reveal only information the deployed Elder could legitimately know. v2 mixes shock and no-shock episodes.

## Deliberately unresolved

These are configuration/research choices, not hidden defaults:

- reward scalarisation weights;
- final disaster families/frequencies/severities;
- exact episode horizon (initial suggested range 20-50 decisions; smoke default 32);
- whether the first smoke test enables movement or holds location fixed;
- PPO/framework choice and hybrid action adapter;
- recurrent policy versus bounded explicit history;
- privileged critic experiments.

## Next implementation work

1. Implement an `ElderObservationBuilder` using filtered game knowledge and the settlement envelopes already present on the base branch.
2. Extract a production `IElderDirectiveCompiler` seam from `SettlementPortfolioSupervisor` / `DemandControl`.
3. Prove every enabled action head actuates a real compiled quantity when physically feasible.
4. Ensure the compiler reports physical/legal clipping instead of silently restoring the old heuristic optimum.
5. Implement persistent `IElderEnvironment.Reset/Step` against the real headless turn pipeline.
6. Add deterministic action-driven replay and state fingerprints.
7. Benchmark 1/4/8/16/32 environment workers before choosing cloud CPU/GPU resources.
8. Only then attach a Python Gymnasium/RL training adapter.

## Smoke command

The project currently exposes only the versioned schema, intentionally:

```bash
dotnet run --project Tools/ElderRL/Anthropocene.ElderRL.csproj -c Release -- schema
```

If a command attempts to step the environment before the native Reset/Step implementation exists, the tool fails rather than falling back to a mock economy.
