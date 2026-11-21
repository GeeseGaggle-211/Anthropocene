# Streaming save persistence

`SaveCodec` serializes a committed turn boundary, including authoritative
geography, ecology, ownership, infrastructure and retained history. Saving does
not advance physical time, prune receipts or regenerate state. Callers must keep
the world locked for the complete save, as the Unity worker/busy guard does.
No RL configuration, observation/action schema or demographic law changes.

## Cause and design

The 80-tribe seed-11 campaign produced a valid envelope-11 save containing
1,520,801,946 bytes of binary XML metadata (1,450.35 MiB). The writer accepted it;
the reader rejected metadata above 512 MiB. Saving and fingerprinting also built
a metadata-sized `MemoryStream`, while loading allocated a byte array from the
declared length. Merely increasing the loader cap would retain those large
allocations and encounter the writer's signed-32-bit length ceiling next.

The codec now counts binary XML bytes, then serializes the same committed graph
directly to the buffered gzip stream. Both passes are linear in serialized state
and retained history. Serialization buffering is bounded independently of metadata
size, excluding the resident world and any buffer explicitly requested by a
caller. There is no temporary metadata file and no elapsed-year/population
subdivision. The additional serialization pass trades CPU time for bounded
working memory and compatibility with existing payloads and fingerprints.

Loading uses a stream limited to the declared metadata block. XML cannot consume
the following tile/resource blocks; truncated or incorrectly sized metadata is
rejected. No metadata-sized byte array is allocated. The restored world still
requires memory proportional to its actual state and retained history.

## Formats and limits

| Envelope | Metadata length | State/tile layout |
| --- | --- | --- |
| `ANTHROPOCENE-8` through `-10` | signed 32-bit | Original supported migrations retained |
| `ANTHROPOCENE-11` | signed 32-bit | Current fields and cultivation capital/operator |
| `ANTHROPOCENE-12` | signed 64-bit | Identical to envelope 11 |

New saves retain envelope 11 when metadata fits in `Int32.MaxValue`. Larger
metadata uses envelope 12. Older binaries cannot open envelope 12. No world,
configuration, resource, technology or observation identifiers are renumbered.
Envelope 12 must not invoke the pre-11 housing/stationary-time migrations.
It also retains envelope 11's resource-presence flag and separate ResourceCodec
block; resource-bearing envelope-12 regression fixtures protect this boundary.

The writer and reader share an explicit **8 GiB metadata limit**. Invalid lengths
are rejected before allocation or prefix emission. This is a bounded format
policy, not a promise that every machine can hold an 8 GiB serialized world.
The original 512 MiB restriction no longer prevents recovering already-written
valid saves. History remains fully retained, so long campaigns still grow.

Fingerprinting uses the same canonical payload serialization as before. Existing
envelope-11 authoritative fingerprints are preserved byte-for-byte; the extended
length is used only when canonical metadata itself exceeds the old Int32 range.
Configuration hashing is unchanged.

## APIs and Unity

`Save(WorldState, Stream)` and `Load(Stream)` leave the caller's stream open and
support non-seekable streams. Existing byte-array overloads remain available for
small fixtures and callers that explicitly want an in-memory compressed save.

Unity's normal save/load worker uses file streams directly. Save still writes a
temporary file and replaces the previous slot only after successful completion;
its existing busy guards remain in force. A failed save cannot replace the old
slot. Unity interaction verification requires an installed Unity editor/player;
headless codec tests do not substitute for that check.

## Reproduction

```bash
dotnet run -c Release --project Tools/Headless/Anthropocene.Headless.csproj -- 0 --only=SaveCodecChecks
dotnet run -c Release --project Tools/SaveAudit/Anthropocene.SaveAudit.csproj -- --grow
```

The native audit reads the retained failing save at
`TestResults/AgriculturalGameplay80/natural-final.anthro`, verifies its historical
fingerprint, streams a replacement, reloads it, and compares an exact next-turn
continuation. With `--grow`, it advances the real world in five-turn batches until
metadata exceeds Int32, then repeats save/load and continuation with envelope 12.
Input saves are untouched. Output saves, per-stage measurements and pinned source
hashes go to `TestResults/SaveAudit` and are intentionally not checked into Git.

The registered `SaveCodecChecks` suite covers non-seekable/fragmented inputs,
stream ownership, both current envelopes, ordinary-save payload equivalence,
next-turn replay, cultivation/ownership/physical-time preservation, corrupt and
truncated metadata, 512 MiB/Int32/8 GiB boundaries and bounded counters. Existing
headless suites retain historical field-migration and resource save coverage.

For a retained large campaign already generated by the audit, the optional
`--extended-source=<file> --extended-expected=<native-fingerprint>` arguments
repeat its recovery and continuation on the current codec without regenerating
the campaign. `--growth-manifest=<file>` records that input's generation snapshot.
The final measurements below use this mode after correcting the extended
resource-block reader; gameplay and the writer were unchanged by that correction.

## Measured recovery — 15 November 2026

The original failing 80-tribe seed-11 save loads with its exact historical
fingerprint, `8BD1670A9268D9DE3F72E0D9FE3E94FA8EE367529639B06C1C1E06BBE6A576F0`.
Its replacement preserves the same canonical state and all 118 history records.
Both worlds advance one native smoothed-calendar turn to identical fingerprints.

Normal AI/Elder continuation then grows the world to **2,189,660,416
metadata bytes (2.039 GiB)** at boundary
turn 145, with 144 retained records,
1,565,945 people and 2,290 camps.
This is a real campaign, without duplicated history or synthetic padding.
The writer automatically switches to envelope 12; that save also reloads with
an identical fingerprint and passes exact next-turn continuation.

| Phase | Boundary turn | Metadata MiB | Wall seconds | Peak working set MiB |
| --- | ---: | ---: | ---: | ---: |
| original-load | 119 | 1,450.35 | 72.81 | 1,652.8 |
| recovered-save | 119 | 1,450.35 | 51.96 | 1,667.4 |
| recovered-load | 119 | — | 78.55 | 3,209.4 |
| recovered-continuation | 120 | — | 63.58 | 3,950.2 |
| extended-save | 145 | 2,088.22 | 46.29 | 4,221.4 |
| extended-load | 145 | — | 118.59 | 4,383.9 |
| extended-continuation | 146 | — | 86.57 | 5,028.7 |

Reload stages include fingerprinting; continuation stages resolve and hash both
resident copies; save stages include a compressed-file hash. Some early stages
overlapped other validation, so these are observed wall times, not isolated
before/after benchmarks. Compression output is not part of the canonical
fingerprint and compressed file bytes need not remain identical.

All **70 registered headless suites**, including **54 save-codec assertions**,
and **28,787 calendar checks** pass on this implementation. Native Unity
interaction remains unverified. The implementation retains an explicit 8 GiB
metadata limit and does not solve unbounded history growth by deleting data.

[Native measurements](Experiments/SavePersistence-20261115-results.json),
[source/input manifest](Experiments/SavePersistence-20261115-manifest.json), and
[regression evidence](Experiments/SavePersistence-20261115-validation.json)
are retained with the code. After the audit, run
`python3 Tools/SaveAudit/Summarize.py` to validate and regenerate them.
