# Geological resources and diagnostic maps

This is a world-generation overlay, not a Neolithic economy. It implements the additive architecture in the [geological design proposal](https://docs.google.com/document/d/1ZwRBd-Zb2_wCEScVgV8pogeQUClh9oIzqe8LB0GMA8o/edit), with the subsequent user decisions: **100× the former early-accessible mineral quantities**, a separate **deep reserve 50× that enlarged accessible pool**, technology-gated material recognition, and renewable Wood with **25% accessibility**. The mineral split is approximately 1.96% early accessible and 98.04% deep at generation. Generation does not add harvesting, cargo, recipes, research, expeditions, trade, or player-map disclosures.

## Pipeline and invariants

`WorldGenerator.CreateStages(placement)` is shared by normal generation and the geography-only atlas. After the existing VegetationResource stage it runs GeologicalProvince → MineralDeposit → SurfaceMaterial, then optional Placement and ResourceAccessAudit. The overlay reads existing physical/ecological fields and writes only `WorldState.Resources`; the audit emits diagnostics. Core terrain generator **11**, world/config **7**, observation schema **7**, food/water placement, camps, herds and `NextId` are unchanged. Deep-reserve worlds use resource generator **3**, schema **4**. Disabling deep reserves retains generator 2: caps up to 100,000 use schema 2 and larger caps use schema 3. Existing version-1/2/3 resources load and save unchanged.

`Generate(seed, config, new ResourceGenerationOptions { Enabled = false })` provides the comparison baseline. `SaveCodec.LegacyFingerprint` compares original authoritative fields without mutating either world. A full fingerprint additionally includes resource state. The normal opening's observations and turn resolver do not consume this new state.

Generation tuning is supplied per call through `ResourceGenerationOptions`. `MaximumQuantity`, `DeepReserveMultiplier`, `Density`, `ScatteredDensity`, `WoodRecoveryPerYear` and `DisabledProfiles` are independent controls. Four strongly typed groups in `GeologicalTuning.cs` expose the geological choices:

| Group | Main controls | Units and interpretation |
| --- | --- | --- |
| `Province` | Count, warp, radii, history probabilities | Radii are fractions of world width/height; probabilities are 0–1. |
| `Deposit` | Occurrence chances, copper/tin multipliers, footprint sizes, scattered stocks, rich cores | Stocks are fractions of `MaximumQuantity`. Higher `ScatteredSiteStockDivisor` lowers the per-site stock ceiling; higher `CoreDecay` makes cores narrower, while higher `CoreFloor` gives margins more stock. |
| `Surface` | Stone/Clay occurrence and quantity, Wood capacity | Capacities and vegetation factors are fractions of `MaximumQuantity`; Wood also applies its local variation factor. |
| `Placer` | Source transfer, retained sites, shelf steps | Transfer is a fraction of the original source stock; sites and steps are counts. |

Paired `Minimum` and `Range` fields evaluate as `Minimum + Range × seededDraw`. Keeping this expression preserves the original floating-point arithmetic. For example, `new ResourceGenerationOptions { Deposit = new DepositTuning { CopperChanceMultiplier = 2 } }` changes only that generation request. Generation validates finite values and bounds before running. These settings do not change stream IDs, candidate lattice keys, source IDs, catalog/profile hashes or save layouts. They are a generation API, not new player economy controls.

Each nondefault tuning group contributes invariant-culture values to the saved resource configuration hash; historical defaults retain their original hash exactly. Saved worlds store the generated state and hash, not the settings object. Scattered initial stock is limited to 8% of `MaximumQuantity` by the persisted schema-2 validation contract, so `ScatteredStockMinimum + ScatteredStockRange` cannot exceed 0.08. The minimum rich stock is kept positive at the smallest supported maximum quantity, and zero scattered stock or zero placer transfer simply omits those bodies. Very broad footprints that exceed the 10,000-part site-share budget fail generation with an explicit tuning error rather than producing invalid shares.

## Quantities and units

The production settings in `Assets/_Anthropocene/Resources/ResourceGenerationConfig.json` are **`MaximumQuantity: 10000000`** and **`DeepReserveMultiplier: 50`**. The player, atlas exporter and spawned-tribe audit load this file. The former raises reference source budgets 100× for both traces and concentrated deposits; the latter independently sizes deep quantities relative to the resulting accessible mineral pool. `MaximumQuantity` accepts **1,000–2,147,483,647**; `DeepReserveMultiplier` accepts finite values from 0 through 1,000,000. Zero selects the legacy resource model. Historical programmatic `new ResourceGenerationOptions()` defaults remain 100,000/0 to preserve existing callers and golden generation fixtures; the production preset explicitly supplies the new values. The upper bounds are representation/validation limits, not balance targets. The built player's command line overrides just the reference cap without rebuilding:

```powershell
./Builds/GeologicalResources/Anthropocene.exe --anthropocene-resource-maximum 1000000
./Tools/WorldAtlas/Export-Worlds.ps1 -Seeds '42' -ResourceMaximum 1000000 -Output TestResults/MillionUnitAtlas -Save
```

Changing the reference cap scales quantities and Wood capacity; it does not increase occurrence frequency. Existing worlds retain their saved settings. Schema 3+ persists `SurfaceUnit = max(10, ceil(MaximumQuantity / 65535))`, keeping each surface raster at two bytes per tile. At ten million the unit is 153. Rich reference budgets round to ten-unit steps, so the 100× comparison differs slightly through quantization. Older worlds retain their original encoding and fingerprints.

Schema 4 freezes **accessible and deep quantities separately for every material component**, including co-products, scattered fields and placers. The accessible quantity matches the former exposure-weighted material access at the enlarged cap; deep starts at 50 times that quantity. Both use normalized `site.Share × site.Exposure` allocation, with its denominator calculated once at generation/load. Thus per-site access stays unchanged when only the deep multiplier changes, and quantity queries remain linear in the number of sites. Initial and remaining pools are saved independently so later extraction can debit either without inventing stock. Parent initial pools include their source-funded placers, preserving provenance and component budgets.

Stone and Clay use separate compact deep rasters with unit `SurfaceUnit × DeepReserveMultiplier`; their starting accessible raster remains unchanged by the deep setting. This adds four bytes per map tile. Wood has no deep pool and keeps its quarter-accessible capacity and renewal coefficient. The old body host budgets, composition fractions and catalog access fractions remain reference data used to generate these new pools; they must not be counted again as another reserve. `TotalAt`/`TotalComponentQuantity` return the actual combined pools, `AccessibleAt` the early pool, and `DeepAt`/`DeepComponentQuantity` the deep pool. `ComponentQuantity` retains its legacy reference-budget meaning for generation/provenance. `SurfaceTotal`, `SurfaceAccessible` and `SurfaceDeepTotal` provide the corresponding surface-source views.

`AdvancedMining` is a saved capability requirement for deep reserves. Deep stock contributes **zero** to the early-accessible overlay and opportunities. The capability and separate stocks prepare for later technology-dependent extraction; the exponential extraction curve and mining economy are not implemented in this generation pass. The 1:50 split applies to all mineral resources, including Stone/Clay, under the latest user instruction. Geological water/terrain barriers and material discovery requirements still apply.

| Quantity | Convention |
| --- | --- |
| Material unit | Abstract game quantity; not tonnes, cubic metres, or an Earth-area conversion. |
| Rich deposit reference host stock | Initially 55–100% of the reference maximum. Composition, exposure and the saved reference access factor determine the early material pool; deep adds 50× that pool at production settings. Solid deposits retain richer cores and poorer margins. |
| Scattered occurrence field | Sparse occurrences across an eligible geological district. One shared 1–8% reference host budget (up to 800,000 at production settings), additionally bounded by 10,000 reference host units per occurrence before share rounding/composition/access conversion. Early and deep material pools remain separate. |
| Local body allocation | Site shares sum to 100%. A multi-hex reservoir does not repeat its full reserve in every tile. |
| Secondary stock | 5–15% transferred from one rich primary source, spread along up to six final drainage/coastal-path tiles. The parent loses exactly the host quantity transferred; component proportions are preserved. |
| Surface Stone/Clay | Up to 10,000,000 early-accessible units per present source plus 50× deep stock, quantized to the saved surface unit. Local absence is allowed. |
| Wood capacity | At production scale: High vegetation 8–10 million, Medium 4.4–5.5 million, Low 0.4–0.5 million units of woody brush, within raster rounding. Ice/water have none. |
| Starting mineral accessibility | 1/51, or about 1.96%, of new combined mineral pools; Wood separately remains 25%. |
| Reference material factors | Historical 10%, 5%, 2%, 0.5%, 0.1% families still determine the relative sizes of generated early mineral pools. They are not additional multipliers applied to schema-4 accessible pools or their share of the new total. |
| Deposit allocation | Legacy exposure (100%/20%/2%) and site shares determine normalized spatial weights for new pools. Material discovery remains separately required. |
| Renewal | Minerals are finite. Wood stores a 0.05/year recovery coefficient for future `dW/dt = k(capacity − W)`. The maximum empty-stock annual renewal is `k × capacity`; a full stock has no net growth. No harvest or Wood time integration runs yet. |

Overlapping independent bodies and surface sources may sum above a single-source limit at a tile. The configured reference cap is not a cap on combined accessible-plus-deep reserves or on world inventories. An ore component is not another full copy of its host. Grade/composition, body size, local allocation and accessibility remain separate.

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

New resource-bearing saves use **ANTHROPOCENE-9**. The fixed core tile layout remains unchanged, and `ResourceCodec` writes an explicit compact resource block outside the XML header. Schema 4 adds the deep multiplier/capability, two deep surface rasters, and four doubles per component for initial/remaining accessible/deep quantities. Schema 3 persists the surface unit; schema 2 adds the concentration kind. Saving any older resource world retains that world's layout and fingerprint; no deep pools are attached on load. Timings and derived site-weight caches are excluded from authoritative fingerprints. Bodies, components and sites serialize canonically.

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

The generation pass is scalar; profile-order and profile-toggle checks protect independent keyed generation. Existing scalar/parallel opening replay includes the persisted layer without changing it. Resource arrays occupy 16 bytes per tile with deep reserves (12 for older schemas), plus sparse bodies/provinces and catalog metadata. `PackedStateBytes` is an estimate of compact payload, not a managed-heap or peak-allocation measurement. Current test evidence and limitations are recorded in [Validation.md](Validation.md).
