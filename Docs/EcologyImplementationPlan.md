# Paleolithic completion implementation checklist

Source: reconciled working plan, https://docs.google.com/document/d/1bghyyEUw55bhM0NQRyzHP6RlfU5JM6LV/edit (read September 13, 2026). Section 14 resolves earlier ambiguities.

User decisions: starvation has **two player turns of grace**, deliberately independent of elapsed time. Subsistence outcome percentages are **balance targets**, not strict per-world quotas; preserve meaningful diversity.

- [x] Society/settlement ownership, explicit elapsed-time progression and opening transition adapters.
- [x] Deliberate workforce assignment/reassignment/release and proportional resident casualties.
- [x] Environment and vegetation overlay, generation dependencies and configuration migration.
- [ ] Exact seasonal stock/condition integration, sparse pressure ledger, pure lazy projection/materialization.
- [ ] Preserve gathering claims, supply, demography, strategic movement and human/herd food conservation.
- [ ] Spatial occupant index, legal action contract, serializable filtered observations, policy state without player-ID cohorts.
- [ ] Deterministic reference/parallel phase execution and 80/320-tribe performance reports.
- [ ] Versioned save/load and focused/metamorphic/ownership/AI tests.
- [ ] Approved layered symbolic art, glyph fallback, terrain/vegetation/land-use/settlement/entity layers and UI smoke.
- [ ] Current documentation, validation and final playable build.

No Neolithic gameplay, RL training, full ECS rewrite or new language is included.

Checkpoint 1: existing 162 assertions plus 14 ownership/workforce/progression checks passed; three seeded games replayed deterministically with 12/12 survivors. Save format is provisionally ANTHROPOCENE-7 / opening-v6.anthro for this unreleased branch; subsequent foundation fields join this same migration.

Checkpoint 2: climate-based environments, generated vegetation potential and configurable hysteresis are in place. All 182 focused assertions, three seeded replay games (12/12 survivors, zero illegal orders), and the three-shape geography sample pass. Seasonal resources remain on the old resolver until the next checkpoint.
