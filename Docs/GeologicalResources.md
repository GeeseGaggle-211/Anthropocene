# Geological resources and diagnostic maps

This is a world-generation overlay, not a Neolithic economy. It implements the additive architecture in the [geological design proposal](https://docs.google.com/document/d/1ZwRBd-Zb2_wCEScVgV8pogeQUClh9oIzqe8LB0GMA8o/edit), with the subsequent user decisions: high stocks with a **tunable per-source cap (100,000 units by default)**, separate human-accessible percentages, technology-gated material recognition, and renewable Wood with **25% accessibility**. Generation does not add harvesting, cargo, recipes, research, expeditions, trade, or player-map disclosures.

## Pipeline and invariants

`WorldGenerator.CreateStages(placement)` is shared by normal generation and the geography-only atlas. After the existing VegetationResource stage it runs GeologicalProvince → MineralDeposit → SurfaceMaterial, then optional Placement and ResourceAccessAudit. The overlay reads existing physical/ecological fields and writes only `WorldState.Resources`; the audit emits diagnostics. Core terrain generator **11**, world/config **7**, observation schema **7**, food/water placement, camps, herds and `NextId` are unchanged. Resource generator remains **2**. Caps up to 100,000 retain schema **2**; larger caps use schema **3** with a saved surface-stock unit. Existing version-1/2 resources load and save unchanged.

`Generate(seed, config, new ResourceGenerationOptions { Enabled = false })` provides the comparison baseline. `SaveCodec.LegacyFingerprint` compares original authoritative fields without mutating either world. A full fingerprint additionally includes resource state. The normal opening's observations and turn resolver do not consume this new state.

Generation tuning is supplied per call through `ResourceGenerationOptions`. Its existing `MaximumQuantity`, `Density`, `ScatteredDensity`, `WoodRecoveryPerYear` and `DisabledProfiles` fields remain available. Four strongly typed groups in `GeologicalTuning.cs` expose the geological choices:

| Group | Main controls | Units and interpretation |
| --- | --- | --- |
| `Province` | Count, warp, radii, history probabilities | Radii are fractions of world width/height; probabilities are 0–1. |
| `Deposit` | Occurrence chances, copper/tin multipliers, footprint sizes, scattered stocks, rich cores | Stocks are fractions of `MaximumQuantity`. Higher `ScatteredSiteStockDivisor` lowers the per-site stock ceiling; higher `CoreDecay` makes cores narrower, while higher `CoreFloor` gives margins more stock. |
| `Surface` | Stone/Clay occurrence and quantity, Wood capacity | Capacities and vegetation factors are fractions of `MaximumQuantity`; Wood also applies its local variation factor. |
| `Placer` | Source transfer, retained sites, shelf steps | Transfer is a fraction of the original source stock; sites and steps are counts. |

Paired `Minimum` and `Range` fields evaluate as `Minimum + Range × seededDraw`. Keeping this expression preserves the original floating-point arithmetic. For example, `new ResourceGenerationOptions { Deposit = new DepositTuning { CopperChanceMultiplier = 2 } }` changes only that generation request. Generation validates finite values and bounds before running. These settings do not change stream IDs, candidate lattice keys, source IDs, catalog/profile hashes or save layouts. They are a generation API, not new player economy controls.

Each nondefault tuning group contributes invariant-culture values to the saved resource configuration hash; historical defaults retain their original hash exactly. Saved worlds store the generated state and hash, not the settings object. Scattered initial stock is limited to 8% of `MaximumQuantity` by the persisted schema-2 validation contract, so `ScatteredStockMinimum + ScatteredStockRange` cannot exceed 0.08. The minimum rich stock is kept positive at the smallest supported maximum quantity, and zero scattered stock or zero placer transfer simply omits those bodies. Very broad footprints that exceed the 10,000-part site-share budget fail generation with an explicit tuning error rather than producing invalid shares.

## Quantities and units

Set `MaximumQuantity` in `Assets/_Anthropocene/Resources/ResourceGenerationConfig.json` for newly generated player worlds. It accepts **1,000–2,147,483,647** units per shared body or surface source; the upper bound is the integer representation limit, not a balance target. The file can also override the other generation options and tuning groups. Defaults remain 100,000. The player command line overrides just the cap without rebuilding:

```powershell
./Builds/GeologicalResources/Anthropocene.exe --anthropocene-resource-maximum 1000000
./Tools/WorldAtlas/Export-Worlds.ps1 -Seeds '42' -ResourceMaximum 1000000 -Output TestResults/MillionUnitAtlas -Save
```

Changing this cap scales source quantities and Wood capacity; it does not increase deposit frequency or change accessible percentages. Existing worlds retain their saved cap. Schema 3 persists `SurfaceUnit = max(10, ceil(MaximumQuantity / 65535))`, keeping surface rasters at two bytes per material per tile. At one million, the unit is 16; capacities round to that resolution without exceeding the cap. Default and older worlds keep ten-unit surface encoding and their original resource fingerprints. Deposit reserves remain double precision. This avoids increasing map memory just to raise stock sizes.

| Quantity | Convention |
| --- | --- |
| Material unit | Abstract game quantity; not tonnes, cubic metres, or an Earth-area conversion. |
| Rich deposit host stock | Initially 55–100% of the configured maximum, capped at the selected maximum per shared body. Components occupy fractions of that host. Solid mineral deposits have richer cores and poorer margins. |
| Scattered occurrence field | A sparse set of surface occurrences within a broader eligible geological district. One shared 1–8% host budget (up to 8,000 units at defaults), additionally bounded by 100 host units per occurrence before share rounding and material composition. Separate from rich deposits. |
| Local body allocation | Site shares sum to 100%. A multi-hex reservoir does not repeat its full reserve in every tile. |
| Secondary stock | 5–15% transferred from one rich primary source, spread along up to six final drainage/coastal-path tiles. The parent loses exactly the host quantity transferred; component proportions are preserved. |
| Surface Stone/Clay | Up to the configured maximum per present tile source; ten-unit increments at the default cap. Local absence is allowed. |
| Wood capacity | High vegetation: 80,000–100,000; Medium: 44,000–55,000; Low: 4,000–5,000 units of woody brush. Ice and water have none. Higher or lower configured maxima scale these ranges. |
| Surface accessibility | Stone 100%, Clay 100%, Wood 25%. |
| Later materials | Base access generally 10%, then 5%, 2%, 0.5% and 0.1% for successively later families; see the catalog for each material. These are provisional game tuning, not historical extraction measurements. |
| Deposit accessibility | Component stock × local share × exposed fraction × saved material access fraction. Surface/shallow/deep exposure is 100%/20%/2%. This does not authorize extraction or imply discovery. |
| Renewal | Minerals are finite. Wood stores a 0.05/year recovery coefficient for future `dW/dt = k(capacity − W)`. The maximum empty-stock annual renewal is `k × capacity`; a full stock has no net growth. No harvest or Wood time integration runs yet. |

Overlapping independent bodies and surface sources may sum above the configured maximum at a tile; the cap is per source/body, not per world or aggregate inventory. An ore component is not another full copy of its host quantity. Grade/composition, body size, local allocation and accessibility remain separate.

Surface capacities are deliberately per playable hex, preserving local opportunities with the existing fixed-hex gathering/movement model. Mineral candidates use a fixed normalized 64 × 32 lattice per profile and land/marine domain; body quantities do not multiply with raster area. Rasterization and shoreline eligibility can change which candidates survive across resolutions. World-total surface capacity therefore grows with map area; it is not an equal-area physical mass estimate.

## Geology and coverage

The 144 overlapping, warped provinces store host rock and procedural history: basins, intrusions, mineralization, redox context, weathering, preservation, organic facies, maturation, evaporites and marine sedimentation. They are seeded historical hypotheses, not reconstructed geology. They do not copy present plate polygons or infer coal from today's trees. Current terrain and boundary context help classify marine settings without changing elevation or coastlines.

There are 36 primary profiles plus a derived placer profile: intrusive/sediment-hosted copper, granite tin/tungsten, carbonate/clastic lead-zinc, gold, ancient iron, nickel/chromium, laterites/bauxite, coal, petroleum, evaporites, limestone/quartz, phosphate/manganese/sulfur, lithium variants, rare-earth variants, graphite, uranium, titanium, shelf aggregate, nodules, crusts, hydrothermal sulfides and rare nitrate basins. Associated components include selected cobalt, silver, molybdenum, gallium and trace porphyry outputs. Registry-only extensions remain explicitly inactive.

Host/process gates precede occurrence sampling. Compatible regions can be barren. Rich bodies use irregular, region-oriented footprints, clipped to compatible geology and the connected component containing their origin. Composition is bounded by host stock. Hardness and quality are provisional descriptors; quality is not applied as a universal extraction or recipe multiplier. More detailed coal-rank, coking, clay-firing and industrial-purity behavior remains for later material/recipe design.

Resource generator 2 adds `MineralConcentration.Scattered` for 23 land-solid profiles: metal ores, bauxite, phosphates, sulfur, hard-rock/clay lithium, rare earths and graphite. Scattered fields are deliberately discontinuous: only a sparse selection of eligible land tiles holds material, with the same host/history requirements as the parent profile. A field uses a broader rotated footprint and an independent keyed occurrence stream, so disabling it with `ScatteredDensity = 0` leaves all rich deposits/placers unchanged. Fields never feed placers. Coal, petroleum/gas, lithium brines, evaporites, offshore-only profiles, and surface Stone/Clay/Wood retain their generation rules.

Primary copper occurrence probability is multiplied by 1.6 and granite tin by 2.5. Other rich-profile frequencies are unchanged. Solid deposits allocate stock using a central concentration kernel and integer shares that still sum exactly to 10,000. These are explicit game tuning choices, not measured real-world geological rates. Existing discovery requirements and accessibility fractions remain authoritative.

Petroleum requires basin, source, maturity, reservoir, seal/trap and preservation context; the occurrence draw abstracts successful charging/preservation. Bodies can cross a coastline with one identity and one reserve. Water setting and burial depth are separate. Phosphorites occupy land/relict marine formations, margins and eligible seamounts, independent of current fishing productivity. Nodules, crusts and hydrothermal sulfides have distinct substrate/setting gates. Nodule/crust material is at the seabed surface but still underwater; passing vessels gain no knowledge or extraction ability.

Placers inherit tin, gold, titanium or rare-earth source composition and an explicit source-body ID. Their saved path follows `Tile.Downstream`; at a marine outlet a bounded, source-funded shelf-neighbor sorting proxy may continue seaward. This is not a current/ocean-sediment simulation. Relict-source records and palaeochannel reconstruction are deferred; this version never invents those sources. Surface Clay uses weathered host and fine-sediment context, with river proximity increasing opportunity rather than guaranteeing it.

## Persistence and discovery boundary

New resource-bearing saves use **ANTHROPOCENE-9**. The fixed core tile layout remains unchanged, and `ResourceCodec` writes an explicit compact resource block outside the XML header. It includes province/raster data, body/site/component records, source paths, stocks, Wood renewal, saved accessibility/discovery metadata, and separate schema/generator/catalog/profile/configuration identities. Schema 2 adds one concentration-kind byte per body. Schema 1 omits it and restores the original deposit kind; saving an old world retains its version-1 byte layout and resource fingerprint. Timings and caches are excluded from authoritative fingerprints. Bodies, components and sites serialize canonically.

**ANTHROPOCENE-8** saves remain readable and keep `Resources == null`. Null means absent, not surveyed/barren. Saving such a world retains the old envelope. Loading never generates or attaches resources; no old save migration command is provided. Existing generator-7–11 ecology compatibility remains unchanged. The user filename remains `opening-v7.anthro`.

Material discovery requirements are stable string capability identifiers, separate from today's four-value `Technology` enum. Copper requires `CopperWorking`; future player integration must apply the saved requirement before presenting a material. The current observation DTO has no resource fields. Resource generation awards no technology and no knowledge; identifying copper is not a prerequisite for discovering Copper Working under the user's revised rule.

## Maps, audits and reproduction

In the Windows player, choose **BUILD WORLD PREVIEW (DEV)** (or preview the current world), then **Resources**. Select any of the 37 active materials and toggle **Total quantity / Human accessible**. The map uses a logarithmic scale to the saved per-source maximum, with dim terrain for absence and teal for trace quantities. **Find largest stock**, Fit world, camp centering, wheel zoom, dragging and tile inspection work on this layer. The inspector explains saved discovery/access requirements, host rock, shared deposit reserves, burial/exposure, and Wood capacity/recovery metadata. Reserved catalog entries are excluded. Older worlds without resources are explicitly labelled as having no layer.

`WorldResourcePreview` computes pure per-tile quantities; `OpeningResourcePreview` supplies the controls and inspector. A body's component quantity is distributed by site share, never copied in full to every tile. The accessible view additionally applies local exposure and saved material accessibility. The developer atlas reveals all eras without changing player knowledge. Texture/value caches are rebuilt on selection, not every frame.

The opt-in development-player argument `--anthropocene-resources-output <fresh-absolute-directory>` captures both modes for every material, rich/scattered Copper close-ups, the selector and normal fog (78 frames), then verifies the unchanged world fingerprint and observed-tile count. It leaves the atlas open for interactive checks and does not use the normal save slot.

The atlas exports actual state to `geology.html`, `geology.json` and PNGs. The map selector covers host rock, provinces, marine settings, deposit profiles, burial, exposure, source paths, total/accessibility for every active material, and Wood renewal potential. All material heatmaps use a logarithmic scale from zero to the saved per-source maximum. A blank layer is valid. `geology.json` records per-profile counts/absence reasons, shared-body summaries and start opportunities. Per-start radius-one quantities and radius-two traversable opportunities are **developer diagnostics**, not known supply or validated delivery throughput.

```powershell
./Tools/WorldAtlas/Export-Worlds.ps1 -Seeds '42,1729' -Shape Continents -Output TestResults/Geology/Final -Save
./Tools/WorldAtlas/Export-Worlds.ps1 -Seeds '42,1729' -Shape Pangaea -Output TestResults/Geology/Final -Save
./Tools/WorldAtlas/Export-Worlds.ps1 -Seeds '42,1729' -Shape Archipelagos -Output TestResults/Geology/Final -Save
# Focused resource fixtures and optional six-seed/all-shape plus large-world audit:
& 'C:/Program Files/Unity/Hub/Editor/6000.5.10f1/Editor/Data/DotNetSdk/dotnet.exe' run --project Tools/Headless/Anthropocene.Headless.csproj -c Release -- 0 --geology-only
& 'C:/Program Files/Unity/Hub/Editor/6000.5.10f1/Editor/Data/DotNetSdk/dotnet.exe' run --project Tools/Headless/Anthropocene.Headless.csproj -c Release -- 0 --geology-audit --large
```

The generation pass is scalar; profile-order and profile-toggle checks protect independent keyed generation. Existing scalar/parallel opening replay includes the persisted layer without changing it. Resource arrays occupy 12 bytes per tile, plus sparse bodies/provinces and catalog metadata. `PackedStateBytes` is an estimate of compact payload, not a managed-heap or peak-allocation measurement. Current test evidence and limitations are recorded in [Validation.md](Validation.md).
