# Scale measurements - September 13, 2026

Windows x64; Unity-bundled .NET 8.0.318; seed **1729**, Continents, default twenty-year turns. Each audit runs all twelve turns, a four-worker resolver, a scalar replay world, fingerprints after every turn, and an initial/final save/load. These are local development measurements, not isolated microbenchmarks. All reference and restored fingerprints matched.

| Measurement | 1,024 x 512 / 80 tribes | 2,048 x 1,024 / 320 tribes | Ratio |
|---|---:|---:|---:|
| World generation | 7.915 s | 46.791 s | 5.91x |
| AI, twelve turns | 0.755 s | 1.795 s | 2.38x |
| Resolution, twelve turns | 1.931 s | 9.113 s | 4.72x |
| Active ecology, twelve turns | 0.483 s | 2.047 s | 4.24x |
| Herd intentions, twelve turns | 0.423 s | 2.070 s | 4.90x |
| Scalar reference resolution, twelve turns | 3.582 s | 16.021 s | 4.47x |
| Initial save | 1.244 s | 4.684 s | 3.76x |
| Initial load | 1.224 s | 5.210 s | 4.26x |
| Final save | 1.466 s | 5.035 s | 3.43x |
| Final load | 2.066 s | 10.256 s | 4.96x |
| Compressed final save | 39.337 MiB | 146.806 MiB | 3.73x |
| Managed heap after generation, one world | 215.072 MiB | 852.213 MiB | 3.96x |
| Peak whole-process working set | 1516.430 MiB | 4516.988 MiB | 2.98x |
| Highest sampled managed heap, including final load | 930.612 MiB | 3683.375 MiB | 3.96x |

The heap peak is sampled, not continuously profiled. The table includes the final-load sample; earlier JSON field `peakSampledManagedBytes` covered generation and turn samples only, so the table takes the greater of that field and `managedBytes`. The runner now includes final load in the named peak field too. Whole-process peak includes the replay/restored world, serialization buffers, runtime and diagnostics; it is not the memory required by one live game.

| Workset | 80 tribes | 320 tribes |
|---|---:|---:|
| Settlements | 80 | 320 |
| Hunter groups | 272 | 1060 |
| Herd entities, wild and owned | 1639 | 6956 |
| Active tiles, first to last resolution | 2282 to 2326 | 9291 to 9539 |
| First / twelfth resolution | 86.3 / 226.1 ms | 476.3 / 1055.4 ms |

Ordinary resolution does not scan all tiles for renewal. A shared planning index supplies local occupant queries and projected ecology. Independent AI/herd intentions and active-tile integrations use bounded workers; IDs, collisions, reductions and history commit in canonical order. Per-settlement reporting indexes avoid repeated scans of the entire transfer log. Sparse per-claim pressure and balance diagnostics are retained for the latest resolution and excluded from save metadata; authoritative continuation state and outcome receipts remain saved.

Four times the area/tribes requires 4.24x active-ecology work and 4.72x complete resolution in this measurement. Generation (5.91x) and herd intentions (4.90x) remain above ideal linear scaling. Generation still uses dense per-tile objects, seasonal arrays and global sorting. This is an explicit remaining cost, not evidence of an exact linear bound.

**320 tribes remains benchmark-only.** Its single-world managed heap after generation is about 852 MiB, approaching the existing 1,000 MB generation budget. Before exposing a large preset, profile and compact dense tile/season storage; do not raise the budget to conceal the cost. The renderer is camera-culled and simplifies layers with zoom, but this audit is a headless simulation measurement, not a rendering frame-time measurement.

Reproduce from the Windows checkout:

```powershell
./Tools/Test-Headless.ps1 -Seeds 0 -ScalePlayers 80
./Tools/Test-Headless.ps1 -Seeds 0 -ScalePlayers 320
```

Raw JSON is written to ignored `TestResults/EcologyScale-80.json` and `TestResults/EcologyScale-320.json`. The checked-in table preserves the measured results without committing generated logs or saves.

## Resource overlay measurement — September 18, 2026

The generation-only geology overlay adds compact rasters (12 bytes/tile) and sparse deposit bodies. Six Standard/80 worlds (seeds 42/1729, all shapes) measured 6.24–6.88 MiB of estimated packed resource payload and 153–284 ms of overlay generation/validation. These are .NET 8 Release exporter observations, not Unity timing guarantees.

The 2048×1024 Archipelagos/42/**80-tribe** audit measured 25.14 MiB packed resource payload, 917.73 MiB total managed heap after collection, and 434 ms overlay time. The existing 1,000 MiB budget was retained. Its full resource-bearing save was 230,574,431 bytes and passed resource fingerprint restoration. This is distinct from the historical 320-tribe benchmark above. Peak transient allocation was not instrumented; no claim of a 64 MiB managed-overhead cap or new public large preset follows from the packed-payload measurement.

The resource-generator-2 occurrence follow-up on the same large seed/configuration measured **25.63 MiB** packed payload, **918.40 MiB** collected managed heap, **722.6 ms** overlay time and **426** resource bodies/fields. Its **230,773,224-byte** save passed exact resource restoration. The run overlapped other validation/build work, so elapsed-time differences are not an isolated performance comparison. Evidence: `TestResults/ResourceDistributionV2/audit.log`.
