# Paleolithic completion implementation checklist

Source: reconciled working plan, https://docs.google.com/document/d/1bghyyEUw55bhM0NQRyzHP6RlfU5JM6LV/edit (read September 13, 2026). Section 14 resolves earlier ambiguities.

User decisions: starvation has **two player turns of grace**, deliberately independent of elapsed time. Subsistence outcome percentages are **balance targets**, not strict per-world quotas; preserve meaningful diversity.

- [x] Society/settlement ownership, explicit elapsed-time progression and opening transition adapters.
- [x] Deliberate workforce assignment/reassignment/release and proportional resident casualties.
- [x] Environment and vegetation overlay, generation dependencies and configuration migration.
- [x] Exact seasonal stock/condition integration, sparse pressure ledger, pure lazy projection/materialization.
- [x] Preserve gathering claims, supply, demography, strategic movement and human/herd food conservation.
- [x] Spatial occupant index, legal action contract, serializable filtered observations, policy state without player-ID cohorts.
- [x] Deterministic reference/parallel phase execution and 80/320-tribe performance reports.
- [ ] Versioned save/load and focused/metamorphic/ownership/AI tests.
- [ ] Approved layered symbolic art, glyph fallback, terrain/vegetation/land-use/settlement/entity layers and UI smoke.
- [ ] Current documentation, validation and final playable build.

No Neolithic gameplay, RL training, full ECS rewrite or new language is included.

Checkpoint 1: existing 162 assertions plus 14 ownership/workforce/progression checks passed; three seeded games replayed deterministically with 12/12 survivors. Save format is provisionally ANTHROPOCENE-7 / opening-v6.anthro for this unreleased branch; subsequent foundation fields join this same migration.

Checkpoint 2: climate-based environments, generated vegetation potential and configurable hysteresis are in place. All 182 focused assertions, three seeded replay games (12/12 survivors, zero illegal orders), and the three-shape geography sample pass. Seasonal resources remain on the old resolver until the next checkpoint.

Checkpoint 3: 206 focused assertions pass, including exact rate migration, depletion/recharge conservation, seasonal/annual/weekly metamorphic checks, pure lazy projection and latent save/load. Three-century calibration distinguishes sustainable light use from extreme collapse, with recovery after abandonment. A three-seed gameplay replay sample has 12/12 survivors and zero illegal orders. AI policy replacement and final balance measurements are still pending.

Checkpoint 4: 216 focused assertions pass. The filtered JSON contract includes legal masks, local settlement/source identities and estimated wild/foreign livestock counts. Human and AI validation share OrderRules. Society preferences and generated mandates are seeded by origin/state, never player-number cohorts. A 12-seed replay sample produced 48/48 surviving tribes, 17 Husbandry / 23 Cultivation / 8 Watercraft outcomes, 38 starvation deaths across the full sample, median final population 87, and zero illegal AI orders. These shares are measurements, not quotas.

Checkpoint 5: scalar and 2/4-worker runs match across reversed work partitions and entity lists. Initial 80/320-tribe twelve-turn audits passed replay and final save/load after removing repeated ownership binding and transient ledger serialization. Active ecology scaled 4.15x for 4x area, AI 3.06x; per-settlement report indexing addresses the remaining report scan. Final timing tables will follow the last measurements. 320 tribes remains benchmark-only.
