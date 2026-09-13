# Validation - September 13, 2026

Current mechanics: [EcologyFoundation.md](EcologyFoundation.md). Windows x64, Unity **6000.5.10f1**, bundled .NET **8.0.318**. Earlier results are preserved in [Validation-Milestone3.md](Validation-Milestone3.md); they are not current balance measurements.

## Automated checks

- **221 fixed headless assertions** pass: 33 original, 59 milestone two, 70 milestone three, 24 ownership/progression/environment, 25 ecology and 10 observation/action-contract assertions. Seeded and geography audits add their own checks.
- Scalar resolution matches two workers and four workers with changed/reversed partitions and reversed entity lists across ten resolutions. The 80/320 audits additionally compare every turn and final save/load against scalar replay.
- Exact condition and renewable-stock updates, analytic depletion/recharge, zero/near-zero rates, seasonal exposure, requested/realized/unmet conservation, human/herd competition and finite source water pass. Twenty-year versus annual seasonal integrations and constant-capacity annual versus 52 equal weekly integrations agree within tolerance. The generic condition equation also passes subdivision checks. These hold forcing/intents fixed; they do not equate different player decision sequences.
- Three-century calibration keeps light productive-tile use near natural condition, permits extreme sustained grazing to collapse local condition, and restores condition after abandonment. Unmet demand and drinking cause no condition damage. Low/high natural-potential recovery modifiers are explicit.
- Pure projection, project/materialize agreement, century-long dormant recovery, multiple vegetation hysteresis crossings, query-order invariance and latent save/load continuation pass. Old signatures are explicitly rejected; save/fingerprint reads do not bind new ownership or allocate IDs.
- Multiple local camps can share one society's tradition while retaining separate stores/populations. Workforce assignment/reassignment/release, proportional named-role casualties, owned improvements, optional transition rewards and elapsed-time era gates pass. Mastery remains available before 200 years; ten-year turns do not trigger premature turn-number gates. Starvation still has two player turns of grace.
- Human and AI legal actions share a validator. Serializable masks/observations preserve exact own and estimated foreign/wild information. Hidden tiles, foreign private state, rival orders and changes within the same herd-count band cannot change the contract. Relabeling player identity cannot change policy choices. Existing continuity/depletion checks cover relocation inertia.
- Growth, food reports (including remote forage), finite expedition provisions, passive coastal gathering and source freshwater conservation remain covered. All seeded runs retain population/food/workforce conservation and zero illegal AI orders.
- **18/18 Unity EditMode tests passed**, zero failures/skips, in **6.60 seconds**, after the cartographic art revision. These include actual config/save integration, contour boundaries/fog, stack hit slots, hover layout/timing, planning/supply previews, all 29 sprite imports at 256 x 256 with zoom filtering, distinct prey silhouettes, preserved sprite colour, both semantic contrast sets/glyph fallbacks, vegetation/relief separation, and sprite containment within hit slots at multiple zooms.

The first Unity attempt exposed a missing Screen Capture module in the optional player-validation harness. The bundled module and its image-conversion dependency were added; subsequent tests and builds passed. The final source-text changes were also compiled by the final Windows build.

## Gameplay samples

Thirty Small / Continents worlds, seeds 1-30, four tribes each:

| Measure | Result |
|---|---:|
| Survivors | 120 / 120 |
| Husbandry / Cultivation / Watercraft first | 35 / 67 / 18 |
| Shares | 29.2% / 55.8% / 15.0% |
| Net additions / starvation deaths | 8,661 / 117 |
| End population: lower quartile / median / upper quartile | 49 / 93 / 129 |
| Maximum / at least 100 people | 283 / 52 of 120 |
| Pursuit commitments producing food | 774 / 1,778 (43.5%) |
| Responses to observed population increases | 632 / 687 |

All 30 games replayed without mismatch, including reversed entity/submission collections. Wild-herd entity count did not increase with fission disabled. Cold-steppe starts averaged 35.1 people, desert 91.1, grassland 95.6 and river/woodland 119.2. These approximate starting-habitat groups all survived; cold starts remain demographically weaker.

Representative Standard / Continents sample, 32 tribes per world:

| Seed | Husbandry | Cultivation | Watercraft | Survivors | Median people |
|---:|---:|---:|---:|---:|---:|
| 1 | 9 | 21 | 2 | 32 / 32 | 94 |
| 2 | 13 | 17 | 2 | 32 / 32 | 78 |
| 3 | 7 | 21 | 4 | 32 / 32 | 126 |
| Total | 29 | 59 | 8 | 96 / 96 | - |

Aggregate shares are 30.2% / 61.5% / 8.3%. All three paths occur without player-ID cohorts, but fishing is less common than in the earlier cohort-driven baseline. The user specified **targets, not quotas**: these are measured balance findings, not guarantees or assertions of a required per-world split.

## Geography and scale

Twelve Small seeds per shape passed generation/topology/drainage/viable-start checks. Mean land fractions were 0.480 Continents, 0.245 Archipelagos and 0.480 Pangaea; mean island counts 10.9, 21.4 and 7.8. The 30-world habitat sample contained 15 river tiles adjacent to desert.

Eight Standard / Continents worlds, seeds 1-8, retained all elevation levels 0-10, 80 viable starts each, reachable opening prey, no peak placement, valid river/freshwater/coast/oasis topology and authoritative save/load. All 640 starts passed.

[Performance.md](Performance.md) records full 80/320-tribe timing, active-tile counts, managed/process memory, saves and deterministic replay. At seed 1729 the 80-tribe twelve-turn resolution took 1.931 seconds plus 0.755 seconds AI; the benchmark-only 320-tribe case took 9.113 plus 1.795 seconds. The 320 single-world heap after generation was about 852 MiB; dense storage compaction remains a prerequisite for a public large preset.

## Windows player and visual inspection

The final Windows development build reported **Build Finished, Result: Success** and exited successfully. The opt-in automated player route exercised Standard / Continents / seed 1729 / 32 tribes with the real presentation helpers. It captured the opening, eight-person dispatch stack, both contrast sets, distant detail reduction, the eastward disconnection preview and a separate save/load restoration. It never wrote the user's normal save slot.

Captured frames were inspected: the opening shows 24 people and one generation of reserves; the dispatch resolves to 28 people, with camp and hunter in distinct selectable slots. The next eastward move resolves to 36 people. A further legal two-tile move displays the red supply tether and matching sidebar warning. Sprites now remain inside their click slots; distant zoom removes vegetation/food detail while keeping units and elevation boundaries. Terrain, vegetation, approximate herd badge and actor identity remain separate. Save/load preserves the authoritative fingerprint and restores turn three with 36 people; pending previews are cleared.

This is automated player execution plus visual inspection of captured frames, **not a manual mouse/keyboard usability playtest**. Hover behavior, slot geometry and hidden-information limits are covered by automated checks; this run did not physically exercise every input device or all possible map/vegetation combinations. The earlier art was rejected by the user: it followed the written style description without matching the embedded image, and its opaque occupant panels obscured terrain. The image was subsequently recovered and the artwork/rendering revised, as recorded in [ArtProvenance.md](ArtProvenance.md). Technical checks alone do not establish the user's acceptance of the art.

Local evidence lives in ignored `TestResults`: `EcologyFocusedFinal.txt`, `EcologyFinalHeadless.txt`, `EcologyStandard32.txt`, `EcologyElevationAudit.txt`, `EcologyUnity/final-results.xml`, `EcologyBuildFinal.log`, and the `EcologyPlayerFinal` captures/result/log. Generated builds, screenshots, logs and saves are deliberately not committed.

### Cartographic art revision, September 13

The final art build succeeded and the visible Windows player completed the same Standard / Continents / 1729 / 32-tribe demo, retaining turn-three population 36 and an identical save/load fingerprint. All nine requested captures exist. The source exporter verified 20 transparent overlays among 29 sprites; all 23 existing asset GUIDs were preserved. The production-render pixel check passed all six map-space probes and confirmed 2,431 changed pixels where the selected camp was actually drawn.

The final captures were visually inspected at 1600 × 1000: warm terrain, separate sparse desert brush, distinct prey silhouettes, tent/spear stacking, open selection corners, freshwater icons, food marks, known shores, merged contours and both contrast sets are visible. The illustrative gallery uses the normal 112-pixel tile height, includes low/medium/high vegetation, a six-occupant stack and a 40-pixel distant tile. The last zoom simplifies small actors to outlined glyphs. Gallery tiles are explicitly synthetic; gameplay screenshots retain the player's actual fog and observations. No simulation or tuning source changed, so the unchanged expensive ecology and scale benchmarks were not rerun.

Final evidence is `CartographicUnity/final-results.xml` (18/18, 6.60 s), `CartographicBuildFinal.log`, and `CartographicPlayerFinal` in ignored `TestResults`. Run `Tools/Art/Check-PlayerArt.cjs` with Node, the Sharp directory and that player-output directory to reproduce the PNG checks. Hidden-window capture attempts failed on this workstation; the smoke helper now refuses to report success if screenshots are absent. A visible player window produced the validated set. This remains automated gameplay with visual inspection, not a manual usability playtest or a claim of user approval of the style.

The vegetation-readability follow-up implements the user's 0/1/3 tree convention: Low keeps grass/brush without trees, Medium shows one tree and High three. The oasis marker no longer contains a decorative palm. All **18 Unity EditMode checks passed in 7.36 s**, including the tree-count and motif assertions for desert, grassland, temperate, tropical and cold land at tile heights 42, 64, 80 and 112. The rebuilt player's gallery was visually inspected: reserved top/side placements keep the three trees visible even around six occupants. All nine captures, six transparency probes and the Standard demo/save round trip passed, ending at turn three with 36 people. The opt-in screenshot run now continues when its visible window loses focus; normal game focus behaviour is unchanged. Evidence is under ignored `TestResults/VegetationReadability`: `tests.xml`, `build-verified.log` and `PlayerVerified`.

## Reproduce

```powershell
./Tools/Test-Headless.ps1 -Seeds 0
./Tools/Test-Headless.ps1 -Seeds 30 -BiomePaths -Geography
./Tools/Test-Headless.ps1 -Seeds 3 -NomadStandard
./Tools/Test-Headless.ps1 -Seeds 0 -Audit
./Tools/Test-Headless.ps1 -Seeds 0 -ScalePlayers 80
./Tools/Test-Headless.ps1 -Seeds 0 -ScalePlayers 320
```

Use Unity's EditMode Test Runner and **Anthropocene > Build Windows prototype**. The development player accepts `--anthropocene-smoke-output <absolute-output-directory>` for the isolated demo; see [AGENTS.md](../AGENTS.md) for the single-editor, save-preservation and Windows workflow requirements.

## Remaining scope

Paleolithic remains a coarse strategic chronology with approximate climate/habitats, generation-level storage/provision horizons and explicit opening prey/transition aids. Independent specialist proficiency, daughter-settlement gameplay, cultivation intensity, domestication management, later eras and RL training are deferred. The new ownership, time, pressure and filtered-action contracts provide their entry points. Performance is measured locally; the reports do not prove behavior for every seed or hardware configuration.
