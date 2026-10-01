# Social cohesion derived from housing structure

Status: shipped (replaces the technology-labelled cohesion regimes of WP2). Source: `NeolithicProduction.cs`
(`HousingCapacity`, `HousingShare`, `CohesionScale`, `SocialCohesion`, `ResolveNeeds`), `ProductionDemand.cs`
(`ExpansionValuation`), `Fission.cs` (`EstimateSplit`, `QoLModel`), `NeolithicConfig.cs`. Checks:
`Tools/Headless/QoLCohesionChecks.cs` (closed forms, continuity, purity, saves), `FissionChecks.cs` (onset calibration,
relief weight), `ImmigrationChecks.cs` (dilution). Sweep tool: `Tools/PerformanceAudit` with
`Summarize-Cohesion.py`.

## Doctrine summary

- **Authoritative state.** None is added. The inputs are the camp's existing housing: fixed `FixedHousingStock` units at its
  current tile (`HousingPeoplePerUnit` = 4 people each) and portable `Inventory.LightHousingUnits` (`LightHousingPeoplePerUnit`
  people each, shipped 3), plus the cohort size being evaluated. `S` is derived on demand and never stored; `Camp.QoL` and
  `HousingPerCapita` remain the committed outputs of `ResolveNeeds`.
- **Continuity, no switches.** `N_s = N_band (1 + k H)` is a continuous function of housing. No technology, era, `Technology`
  label or `CultivationEconomy.Enabled` test enters it, so acquiring a technology changes `S` only through the housing the camp
  then builds. A camp with no housing has `N_s = N_band`. `Fission.RegimeOf` survives only to score destination sites
  (cultivable land, pasture or gathering) and no longer affects cohesion.
- **Temporal abstraction.** Turn boundaries are authoritative. `S` is a pure function of the committed housing at the evaluation
  clock (`w.Days`, after the turn's depreciation, exactly as housing coverage was) and the cohort; nothing is integrated over
  sub-turn time.
- **Scaling.** O(1) arithmetic per evaluation plus one pass over the fixed-housing stock list (`FixedHousingAt(w, camp, days)`,
  allocation free, no camp lookup). Per turn that is O(camps) evaluations (about 8 per camp: `ResolveNeeds`, the claim
  valuation, `Welfare` at N and N+1, fission, the immigration model). The stock list has at most one entry per camp site, so
  the scan is O(camps x stocks) per turn in the worst case: measured negligible at 470 camps (below), and the trigger for
  indexing stocks by camp is a turn profile in which `FixedHousingAt` exceeds about 5 percent of resolution time.
  Nothing scales with elapsed years, map size or `dt`.
- **Rejected architectures.** (1) Keeping three regime scales and interpolating between them (still a label switch underneath).
  (2) Caching `H` on the camp (a second authoritative value that could disagree with the stocks; recomputing costs one pass).
  (3) Giving a daughter the parent's scale (would hide the housing it leaves behind).
- **Determinism and conservation.** Pure arithmetic over saved state in canonical order; no random draws, no stored timing.
  Food, goods and housing ledgers are untouched. Covered by the scalar/parallel replay and save-continuation checks.

## The rule

```
fixed capacity   C_f = FixedHousingAt(camp, now) * HousingPeoplePerUnit                      (people)
tent capacity    C_t = max(0, LightHousingUnits) * LightHousingPeoplePerUnit                 (people)
coverage         R_h = min(1, (C_f + C_t) / N)                  housing coverage in QoL (clothing/housing term)
housing share    H   = min(1, (C_f + p_tent * C_t) / N)          cohesion housing share, p_tent = LightHousingPermanence
cohesion scale   N_s = N_band * (1 + k * H)                      N_band = CohesionBandScale, k = CohesionHousingGain
cohesion         S(N) = exp(-(N / N_s)^beta)                     beta = SocialCohesionExponent (2, unchanged)
```

`N` is the cohort being evaluated (present residents, the turn's needs cohort, the stayers of a split, the destination
headcount of a migration). Tents shelter fewer people than houses (3 against 4 at the shipped values) in return for
portability, and count only `p_tent` as much as permanent housing towards cohesion. A camp with no housing has `H = 0`
and `N_s = N_band`; all tents exactly covering everyone gives `H = p_tent`; all permanent housing exactly covering everyone
gives `H = 1`. Over-capacity is capped at 1 by the `min`.

One helper supplies every consumer: `NeolithicProduction.HousingCapacity(w, camp, days)` (returns `ShelterCapacity`
with `Fixed`, `Light`, `Total`), with `HousingCoverage`, `HousingShare`, `HousingGapPeople`, `CohesionScale` and
`SocialCohesion` built on it.

| Consumer | Change |
| --- | --- |
| `ResolveNeeds` | `HousingPerCapita = R_h` from people capacity; `QoL = F W (w_c c + w_h h) S(N)` with `S` at the camp's housing and the cohort. |
| Demand (`NeolithicProduction.Demand`, `MaterialEconomy`) | Housing demand is the unsheltered people (`residents - capacity at the horizon end`). Housing units = gap / 4; Light Housing units = gap / `LightHousingPeoplePerUnit`; the Animal Fabric that feeds Light Housing follows the same gap. `DepreciateHousing` records its ledger in fixed-unit equivalents of people. |
| Claim valuation (`ExpansionValuation`, `CampProductionDemand`) | `S` at the present cohort and housing. A housing claim is valued per person sheltered, so a unit of Housing is worth `4 x` and a tent `LightHousingPeoplePerUnit x` the per-person value; claim stock and need are in units of that good. `Welfare(W, N)` evaluates `S` at the camp's housing and the cohort `N`. The valuation does not add the cohesion benefit of extra housing (see limits). |
| Fission (`EstimateSplit`) | Stayers: fixed capacity kept, portable split in proportion. The cohesion share for the stayers is the present `H` when `FissionHousingRelief = 0` (only the headcount changes) and interpolates towards the relieved `H` with the weight (`H_after = H_now + relief (H_relieved - H_now)`), the same weight as housing coverage. The daughter takes no fixed housing: its `H` comes from the carried tents plus the shelter its Builders assemble from carried goods in its first turn (`Fission.DaughterShelter`, Docs/Fission.md). |
| Immigration (`QoLModel.ForwardAt`) | Housing stays with the camp. Arrivals bring none, so the destination's `H` (and coverage) is diluted by headcount; the source's `H` rises as it loses people because they leave housing behind. One formula at each camp's own headcount; no label enters. |
| Inspector text | Shelter is reported as people capacity (houses 4, tents `LightHousingPeoplePerUnit`). |

Saves: the cohesion inputs are the saved housing stocks and config. `ANTHROPOCENE-11` and every earlier compatible
envelope load unchanged. The retired `CohesionScalePaleolithic/Nomadic/Agricultural` fields in an older save are ignored
by the serializer; the new `CohesionBandScale`, `CohesionHousingGain`, `LightHousingPermanence` and
`LightHousingPeoplePerUnit` are optional fields that load their defaults. **A loaded world therefore continues under the
derived rule with today's defaults**, not the scales it was saved with (and a world saved with shelter measured at four
people per tent now shelters two per tent).

## Analytic starting point

With full goods coverage and housing share held (fission `FissionHousingRelief = 0`), the expected gain of a split with
daughter fraction `f` is `G = exp((N / N_s)^2 (1 - (1 - f)^2)) - 1`, so the onset under the current Neutral threshold
`G* = 0.113` (f = 0.30) is `N* = N_s * sqrt(ln(1 + G*) / (1 - (1 - f)^2)) = 0.4581 N_s`. Discourage (`G* = 0.080`, f 0.15)
gives `0.5266 N_s` and Encourage (`G* = 0.0955`, f 0.40) gives `0.3775 N_s`. Fitting `N_s(0) = N_band`, `N_s(p_tent) = N_band (1 + k p_tent)`
and `N_s(1) = N_band (1 + k)` to the design onsets (about 101 unhoused, about 252 in tents, inside 607-999 fully housed;
`QoLFissionTradeDesign.md` 3.5) gives **N_band = 220, k = 7.1, p_tent = 0.21**, which reproduces the retired 220 / 550 / 1,780
regime scales (`220`, `220 (1 + 7.1 x 0.21) = 548`, `220 x 8.1 = 1,782`). Onsets with integer daughters (`FissionChecks.Onset`
scans the same rule; closed form first, integer-scan onset in brackets):

| Housing structure | N_s | Neutral | Discourage | Encourage |
| --- | ---: | ---: | ---: | ---: |
| No housing, H = 0 | 220 | 100.8 (102) | 115.9 (119) | 83.1 (84) |
| Tents covering everyone, H = p_tent = 0.21 | 548 | 251.1 (252) | 288.6 (291) | 206.9 (208) |
| Permanent housing covering everyone, H = 1 | 1,782 | 816.5 (817) | 938.5 (940) | 672.7 (673) |

Design targets: 101 / 116 / 83 (none), 252 / 291 / 208 (tents), 607-999 / 770-1,105 / 398-887 (permanent). The fit sits inside
every band (the permanent row is placed at 817, the region of the design median of about 803, with a round `k`).

**Shipped defaults = the analytic fit** (N_band 220, k 7.1, p_tent 0.21, tents 3), by orchestrator decision: no sweep
candidate brings camp sizes near the targets (see "Main finding" below), fission is off by default, and the sweep's best
score (candidate A: 176 / 4.97 / 0.21 / tents 2) lowers default QoL (fixed-dominant median 0.60 against 0.75) for a
ranking dominated by the fission-throughput bottleneck. Candidate A's onsets, for reference, are

| Housing structure | N_s | Neutral | Discourage | Encourage |
| --- | ---: | ---: | ---: | ---: |
| No housing, H = 0 | 176 | 80.6 (82) | 92.7 (94) | 66.4 (68) |
| Tents covering everyone, H = 0.21 | 360 | 164.8 (166) | 189.4 (192) | 135.8 (137) |
| Permanent housing covering everyone, H = 1 | 1,051 | 481.4 (483) | 553.3 (554) | 396.7 (398) |

## Calibration sweep

Protocol (all runs `Tools/PerformanceAudit`, Standard-size 80-tribe worlds, 12 opening + 50 post-opening turns
(the opening completes in turn 10, so 10 opening and 50 post-opening turns were measured in the Continents 1 run), `--fission=1 --immigration=0
--details=1`, `--workers=1`, three runs in parallel on a 4-core host; `--set=Name=Value` now also reaches the Neolithic
calibration). Worlds: Continents seed 1, Pangaea seed 1, Archipelagos seed 1 for screening; Continents 2 and 3 added for the
final candidates. Raw per-turn JSON is under `TestResults/Cohesion/runs` (ignored); the reducer is
`Tools/PerformanceAudit/Summarize-Cohesion.py`.

**Scoring.** Each camp-turn is classified by its actual housing, never by technology: unhoused (fewer than one person in ten
sheltered, `(C_f + C_t) / N < 0.1`), tent-dominant (sheltered and tent capacity above fixed capacity) and fixed-dominant.
The specified boundary was `H < 0.1`; `H` contains `p_tent`, so that boundary moves when `p_tent` is the parameter under test
(`p_tent = 0.1` makes every tent camp "unhoused"). The class boundary therefore uses sheltered people, which is independent of
`p_tent`. P90 and P95 of camp size are pooled over post-opening turns 10 to 50 of every world. Camps are unhoused almost only
before the first breakthrough, which ends inside the opening, so the unhoused class also pools the opening turns. Error is the
relative distance outside the band, summed over the six (class, percentile) terms: the **score** treats 100 and 250 as upper
limits (the brief says "around those levels at most": bands 0-100 and 0-250) and 600-1,000 as a band; the **point** score in
brackets measures two-sided distance from 100, 250 and the nearer edge of 600-1,000. The tables also give the median size,
QoL P25 / median / P75 per class and by first technology (the camp's first breakthrough), end population, fissions and camps
at turn 60 (per-world means), the share of husbandry-first camp-turns that are villages and that are tent-dominant, and the
median total turn time in milliseconds (measured with three concurrent runs, so use it for relative cost only). `n` counts
camp-turns. A trailing `*` would flag a class with fewer than 30 observations (none in the final tables).

### Stage 1: analytic centre and one-at-a-time variants (3 worlds)

Variants: N_band +-20 percent (176 / 264), k +-30 percent (4.97 / 9.23), p_tent 0.1 / 0.3, tent capacity 2 / 4. The centre is
N_band 220, k 7.1, p_tent 0.21, tent capacity 3.

| run | worlds | score (point) | unhoused P90/P95 (n) | tent P90/P95 (n) | fixed P90/P95 (n) | median size u/t/f | pop | camps | fissions | husb village / tent | ms |
|---|---|---|---|---|---|---|---|---|---|---|---|
| centre | 3 | 12.23 (12.69) | 69/85 (2352) | 1389/1742 (10800) | 1688/2017 (20314) | 29/422/764 | 417839 | 373 | 293 | 54% / 50% (20758) | 763 |
| band-20 | 3 | 11.25 (11.83) | 65/77 (2452) | 1284/1623 (11879) | 1635/1983 (23479) | 29/386/703 | 444382 | 413 | 333 | 53% / 48% (23823) | 833 |
| band+20 | 3 | 11.92 (12.33) | 69/90 (2302) | 1351/1675 (9977) | 1745/2067 (18160) | 29/476/801 | 404566 | 354 | 274 | 54% / 52% (18763) | 704 |
| k-30 | 3 | 10.97 (11.38) | 71/88 (2372) | 1246/1602 (11604) | 1612/1965 (24064) | 29/364/714 | 444224 | 418 | 338 | 52% / 46% (24284) | 896 |
| k+30 | 3 | 12.65 (13.11) | 69/85 (2351) | 1429/1792 (9874) | 1696/2066 (17767) | 29/500/800 | 390951 | 335 | 255 | 56% / 52% (18478) | 664 |
| p0.1 | 3 | 12.10 (12.54) | 70/86 (2360) | 1386/1715 (11720) | 1676/2021 (20154) | 29/423/754 | 429321 | 391 | 311 | 52% / 53% (21621) | 785 |
| p0.3 | 3 | 12.03 (12.49) | 70/85 (2356) | 1380/1691 (10768) | 1712/2035 (19881) | 29/441/766 | 416825 | 374 | 294 | 54% / 51% (20547) | 738 |
| tent2 | 3 | 11.61 (12.06) | 70/85 (2359) | 1336/1629 (10898) | 1702/2047 (20847) | 29/431/755 | 424469 | 380 | 300 | 54% / 49% (21594) | 750 |
| tent4 | 3 | 12.92 (13.38) | 69/85 (2350) | 1490/1828 (12546) | 1640/2011 (19252) | 29/455/740 | 427215 | 385 | 305 | 54% / 56% (21570) | 760 |

QoL P25/median/P75 per housing class
| run | unhoused | tent-dominant | fixed-dominant |
|---|---|---|---|
| centre | 0.00/0.00/0.44 | 0.64/0.88/0.96 | 0.50/0.73/0.89 |
| band-20 | 0.00/0.00/0.54 | 0.56/0.85/0.95 | 0.39/0.67/0.87 |
| band+20 | 0.00/0.00/0.29 | 0.71/0.90/0.96 | 0.59/0.79/0.91 |
| k-30 | 0.00/0.00/0.02 | 0.57/0.85/0.95 | 0.33/0.63/0.85 |
| k+30 | 0.00/0.00/0.10 | 0.69/0.89/0.96 | 0.61/0.80/0.92 |
| p0.1 | 0.00/0.00/0.27 | 0.58/0.86/0.95 | 0.48/0.72/0.89 |
| p0.3 | 0.00/0.00/0.56 | 0.68/0.89/0.96 | 0.51/0.74/0.89 |
| tent2 | 0.00/0.00/0.47 | 0.64/0.88/0.95 | 0.51/0.75/0.90 |
| tent4 | 0.00/0.00/0.22 | 0.56/0.86/0.95 | 0.47/0.73/0.89 |

QoL P25/median/P75 by first technology (n camp-turns)
| run | AnimalHusbandry | Cultivation | Watercraft |
|---|---|---|---|
| centre | 0.41/0.77/0.91 (20758) | 0.62/0.81/0.93 (8875) | 0.76/0.87/0.97 (1517) |
| band-20 | 0.30/0.71/0.90 (23823) | 0.51/0.75/0.90 (9567) | 0.70/0.87/0.97 (2038) |
| band+20 | 0.51/0.81/0.92 (18763) | 0.70/0.85/0.94 (7966) | 0.80/0.89/0.97 (1435) |
| k-30 | 0.27/0.69/0.89 (24284) | 0.46/0.72/0.89 (9802) | 0.68/0.82/0.94 (1638) |
| k+30 | 0.52/0.81/0.92 (18478) | 0.72/0.86/0.95 (7723) | 0.80/0.92/0.98 (1476) |
| p0.1 | 0.37/0.75/0.91 (21621) | 0.62/0.81/0.93 (8780) | 0.76/0.87/0.96 (1517) |
| p0.3 | 0.44/0.79/0.92 (20547) | 0.63/0.81/0.93 (8591) | 0.76/0.89/0.98 (1551) |
| tent2 | 0.42/0.77/0.91 (21594) | 0.65/0.83/0.93 (8642) | 0.76/0.88/0.97 (1552) |
| tent4 | 0.39/0.77/0.91 (21570) | 0.58/0.79/0.92 (8786) | 0.77/0.88/0.98 (1476) |

### Stage 2: factorial around the two best directions (3 worlds)

The best two stage 1 directions were a lower N_band and a lower k (tent capacity 2 ranked third and is carried into the last two
rows). Reference rows are the stage 1 centre and the two one-at-a-time winners. Tags read `b<N_band>k<k>` with `t2` for a tent
capacity of 2.

| run | worlds | score (point) | unhoused P90/P95 (n) | tent P90/P95 (n) | fixed P90/P95 (n) | median size u/t/f | pop | camps | fissions | husb village / tent | ms |
|---|---|---|---|---|---|---|---|---|---|---|---|
| ref-centre | 3 | 12.23 (12.69) | 69/85 (2352) | 1389/1742 (10800) | 1688/2017 (20314) | 29/422/764 | 417839 | 373 | 293 | 54% / 50% (20758) | 763 |
| ref-band-20 | 3 | 11.25 (11.83) | 65/77 (2452) | 1284/1623 (11879) | 1635/1983 (23479) | 29/386/703 | 444382 | 413 | 333 | 53% / 48% (23823) | 833 |
| ref-k-30 | 3 | 10.97 (11.38) | 71/88 (2372) | 1246/1602 (11604) | 1612/1965 (24064) | 29/364/714 | 444224 | 418 | 338 | 52% / 46% (24284) | 896 |
| b154k7.1 | 3 | 10.95 (11.63) | 61/71 (2526) | 1265/1592 (12631) | 1566/1954 (24680) | 28/386/680 | 465275 | 441 | 361 | 51% / 49% (24870) | 797 |
| b154k4.97 | 3 | 10.71 (11.04) | 67/114 (2610) | 1211/1586 (12739) | 1518/1863 (30956) | 29/350/635 | 520317 | 507 | 427 | 50% / 45% (27372) | 994 |
| b154k3.5 | 3 | 11.63 (11.63) | 111/232 (2929) | 1211/1582 (12220) | 1389/1643 (43266) | 33/331/577 | 609330 | 619 | 539 | 49% / 41% (28656) | 1407 |
| b176k4.97 | 3 | 10.29 (10.80) | 68/81 (2477) | 1185/1529 (12585) | 1548/1891 (27553) | 29/354/669 | 476348 | 462 | 382 | 51% / 47% (26123) | 941 |
| b176k3.5 | 3 | 11.74 (11.74) | 119/188 (2809) | 1243/1646 (12518) | 1428/1679 (38812) | 32/350/605 | 584144 | 587 | 507 | 51% / 44% (27439) | 1236 |
| b220k3.5 | 3 | 10.86 (10.86) | 133/176 (2661) | 1151/1474 (12879) | 1473/1799 (34408) | 32/351/656 | 569656 | 576 | 496 | 50% / 46% (27114) | 1036 |
| b132k4.97 | 3 | 12.07 (12.22) | 85/240 (2883) | 1244/1635 (11960) | 1436/1720 (35693) | 29/343/606 | 531948 | 526 | 446 | 51% / 42% (27540) | 1205 |
| b132k3.5 | 3 | 11.93 (11.93) | 111/248 (3135) | 1260/1589 (12550) | 1366/1579 (45847) | 32/323/553 | 618592 | 648 | 568 | 48% / 41% (29601) | 1518 |
| b176k4.97t2 | 3 | 9.23 (9.76) | 67/80 (2462) | 1073/1354 (11210) | 1590/1933 (27795) | 29/332/662 | 466904 | 457 | 377 | 49% / 42% (25752) | 951 |
| b154k4.97t2 | 3 | 9.83 (10.15) | 68/146 (2630) | 1091/1405 (11429) | 1527/1862 (31269) | 29/328/634 | 512204 | 503 | 423 | 49% / 40% (27668) | 944 |

QoL P25/median/P75 per housing class
| run | unhoused | tent-dominant | fixed-dominant |
|---|---|---|---|
| ref-centre | 0.00/0.00/0.44 | 0.64/0.88/0.96 | 0.50/0.73/0.89 |
| ref-band-20 | 0.00/0.00/0.54 | 0.56/0.85/0.95 | 0.39/0.67/0.87 |
| ref-k-30 | 0.00/0.00/0.02 | 0.57/0.85/0.95 | 0.33/0.63/0.85 |
| b154k7.1 | 0.00/0.05/0.58 | 0.47/0.82/0.94 | 0.32/0.62/0.85 |
| b154k4.97 | 0.01/1.00/1.00 | 0.34/0.76/0.93 | 0.17/0.49/0.78 |
| b154k3.5 | 0.40/1.00/1.00 | 0.17/0.68/0.92 | 0.08/0.41/0.73 |
| b176k4.97 | 0.00/1.00/1.00 | 0.44/0.80/0.94 | 0.22/0.54/0.80 |
| b176k3.5 | 0.59/1.00/1.00 | 0.25/0.71/0.92 | 0.12/0.45/0.75 |
| b220k3.5 | 1.00/1.00/1.00 | 0.43/0.80/0.94 | 0.22/0.54/0.78 |
| b132k4.97 | 0.00/1.00/1.00 | 0.24/0.70/0.92 | 0.12/0.44/0.75 |
| b132k3.5 | 0.00/1.00/1.00 | 0.09/0.60/0.91 | 0.04/0.34/0.68 |
| b176k4.97t2 | 0.00/0.01/1.00 | 0.49/0.82/0.94 | 0.21/0.57/0.83 |
| b154k4.97t2 | 0.00/0.15/1.00 | 0.39/0.78/0.93 | 0.16/0.52/0.80 |

QoL P25/median/P75 by first technology (n camp-turns)
| run | AnimalHusbandry | Cultivation | Watercraft |
|---|---|---|---|
| ref-centre | 0.41/0.77/0.91 (20758) | 0.62/0.81/0.93 (8875) | 0.76/0.87/0.97 (1517) |
| ref-band-20 | 0.30/0.71/0.90 (23823) | 0.51/0.75/0.90 (9567) | 0.70/0.87/0.97 (2038) |
| ref-k-30 | 0.27/0.69/0.89 (24284) | 0.46/0.72/0.89 (9802) | 0.68/0.82/0.94 (1638) |
| b154k7.1 | 0.23/0.66/0.89 (24870) | 0.45/0.70/0.88 (10765) | 0.65/0.85/0.95 (1768) |
| b154k4.97 | 0.11/0.54/0.86 (27372) | 0.28/0.58/0.81 (14706) | 0.46/0.79/0.94 (1790) |
| b154k3.5 | 0.03/0.40/0.81 (28656) | 0.15/0.48/0.76 (25275) | 0.37/0.74/0.91 (2043) |
| b176k4.97 | 0.17/0.60/0.87 (26123) | 0.35/0.63/0.84 (11923) | 0.58/0.81/0.95 (2185) |
| b176k3.5 | 0.06/0.46/0.83 (27439) | 0.21/0.53/0.78 (21901) | 0.42/0.77/0.93 (2413) |
| b220k3.5 | 0.16/0.60/0.87 (27114) | 0.31/0.60/0.81 (18844) | 0.56/0.76/0.93 (1674) |
| b132k4.97 | 0.05/0.45/0.82 (27540) | 0.20/0.53/0.78 (18502) | 0.44/0.76/0.92 (1933) |
| b132k3.5 | 0.01/0.32/0.77 (29601) | 0.09/0.41/0.71 (27432) | 0.34/0.69/0.89 (1930) |
| b176k4.97t2 | 0.16/0.61/0.88 (25752) | 0.36/0.69/0.87 (11174) | 0.57/0.83/0.95 (2157) |
| b154k4.97t2 | 0.11/0.55/0.86 (27668) | 0.29/0.63/0.84 (13346) | 0.50/0.81/0.94 (1875) |

### Stage 3: candidates on five worlds, with the fission-off control

Candidate A is N_band 176, k 4.97, p_tent 0.21, tent capacity 2; candidate B is N_band 154, k 4.97, p_tent 0.21, tent capacity 2;
`Aoff` is A with `FissionEnabled` off (population and camp counts are then those of eighty fixed camps). Worlds are Continents
1-3, Pangaea 1 and Archipelagos 1; per-world rows follow the pooled rows.

| run | worlds | score (point) | unhoused P90/P95 (n) | tent P90/P95 (n) | fixed P90/P95 (n) | median size u/t/f | pop | camps | fissions | husb village / tent | ms |
|---|---|---|---|---|---|---|---|---|---|---|---|
| centre | 5 | 12.02 (12.58) | 65/79 (3826) | 1367/1714 (17903) | 1683/2011 (32510) | 28/426/746 | 403916 | 364 | 284 | 54% / 51% (33681) | 746 |
| centre [Archipelagos1] | 1 | 8.56 (8.91) | 77/89 (830) | 1032/1315 (3120) | 1470/1707 (7822) | 32/384/711 | 355806 | 371 | 291 | 57% / 44% (6648) | 780 |
| centre [Continents1] | 1 | 13.65 (14.14) | 67/84 (755) | 1497/1934 (3794) | 1823/2102 (6505) | 26/442/827 | 464109 | 371 | 291 | 52% / 51% (7195) | 763 |
| centre [Continents2] | 1 | 11.94 (12.62) | 59/73 (740) | 1354/1719 (3943) | 1666/1981 (6381) | 27/424/689 | 404986 | 380 | 300 | 54% / 54% (7153) | 715 |
| centre [Continents3] | 1 | 11.88 (12.64) | 56/69 (734) | 1354/1689 (3160) | 1701/2010 (5815) | 25/437/737 | 361075 | 320 | 240 | 54% / 52% (5770) | 651 |
| centre [Pangaea1] | 1 | 13.33 (13.96) | 63/74 (767) | 1463/1854 (3886) | 1874/2188 (5987) | 25/449/780 | 433602 | 378 | 298 | 52% / 55% (6915) | 746 |
| A | 5 | 9.74 (10.28) | 66/80 (4045) | 1141/1422 (18234) | 1581/1904 (45664) | 29/344/640 | 456250 | 448 | 368 | 50% / 42% (41700) | 951 |
| A [Archipelagos1] | 1 | 7.68 (7.93) | 75/106 (907) | 958/1197 (3189) | 1397/1606 (10353) | 32/356/618 | 392019 | 426 | 346 | 55% / 39% (7654) | 984 |
| A [Continents1] | 1 | 10.28 (10.98) | 60/70 (759) | 1167/1450 (4064) | 1737/2078 (8887) | 26/341/736 | 523647 | 472 | 392 | 46% / 43% (9179) | 951 |
| A [Continents2] | 1 | 12.47 (12.73) | 74/264 (830) | 1256/1604 (3625) | 1530/1861 (9549) | 29/354/602 | 469812 | 467 | 387 | 51% / 40% (8807) | 974 |
| A [Continents3] | 1 | 10.05 (10.84) | 57/65 (753) | 1208/1438 (3399) | 1584/1887 (8320) | 25/361/621 | 410726 | 402 | 322 | 50% / 45% (7141) | 934 |
| A [Pangaea1] | 1 | 9.86 (10.50) | 64/73 (796) | 1078/1391 (3957) | 1816/2169 (8555) | 27/307/657 | 485047 | 473 | 393 | 47% / 44% (8919) | 876 |
| B | 5 | 9.55 (9.90) | 65/130 (4248) | 1078/1388 (18485) | 1518/1877 (51120) | 29/328/617 | 496196 | 500 | 420 | 49% / 40% (44558) | 944 |
| B [Archipelagos1] | 1 | 7.77 (8.03) | 74/151 (964) | 933/1144 (2687) | 1378/1576 (11644) | 32/313/601 | 409123 | 451 | 371 | 55% / 32% (7912) | 1080 |
| B [Continents1] | 1 | 10.39 (10.75) | 64/130 (806) | 1163/1465 (4233) | 1607/1962 (9852) | 27/315/688 | 566696 | 521 | 441 | 45% / 43% (9634) | 944 |
| B [Continents2] | 1 | 8.85 (9.48) | 61/77 (806) | 1021/1343 (3981) | 1485/1913 (10361) | 28/323/585 | 496193 | 532 | 452 | 48% / 41% (9441) | 1071 |
| B [Continents3] | 1 | 9.16 (9.60) | 61/94 (812) | 1067/1370 (3075) | 1538/1871 (9490) | 26/337/610 | 448178 | 456 | 376 | 48% / 39% (7449) | 911 |
| B [Pangaea1] | 1 | 11.62 (11.94) | 68/238 (860) | 1131/1482 (4509) | 1717/2069 (9773) | 28/341/626 | 560792 | 538 | 458 | 47% / 44% (10122) | 934 |
| Aoff | 5 | 13.74 (14.31) | 64/79 (3708) | 1624/1930 (1401) | 1552/1971 (14983) | 27/785/685 | 85240 | 80 | 0 | 76% / 25% (4346) | 342 |
| Aoff [Archipelagos1] | 1 | 11.47 (11.66) | 81/107 (774) | 1443/1499 (291) | 1645/1985 (2989) | 31/857/708 | 86340 | 80 | 0 | 79% / 23% (984) | 342 |
| Aoff [Continents1] | 1 | 16.04 (16.74) | 58/72 (722) | 1692/2448 (318) | 1466/2014 (2956) | 26/835/721 | 90476 | 80 | 0 | 73% / 29% (902) | 343 |
| Aoff [Continents2] | 1 | 11.72 (12.39) | 59/74 (729) | 1529/1590 (252) | 1467/1776 (3027) | 27/803/586 | 78216 | 80 | 0 | 75% / 22% (902) | 323 |
| Aoff [Continents3] | 1 | 14.77 (15.53) | 55/69 (729) | 1670/2113 (295) | 1645/1987 (2977) | 25/562/603 | 80308 | 80 | 0 | 78% / 30% (656) | 334 |
| Aoff [Pangaea1] | 1 | 14.80 (15.39) | 64/76 (754) | 1729/2051 (245) | 1581/2101 (3034) | 25/785/754 | 90859 | 80 | 0 | 77% / 25% (902) | 363 |

QoL P25/median/P75 per housing class
| run | unhoused | tent-dominant | fixed-dominant |
|---|---|---|---|
| centre | 0.00/0.02/0.59 | 0.65/0.88/0.95 | 0.50/0.75/0.90 |
| centre [Archipelagos1] | 0.02/0.28/0.42 | 0.75/0.91/0.96 | 0.54/0.75/0.90 |
| centre [Continents1] | 0.00/0.00/0.14 | 0.60/0.87/0.95 | 0.47/0.70/0.88 |
| centre [Continents2] | 0.18/0.36/0.68 | 0.67/0.88/0.95 | 0.53/0.78/0.91 |
| centre [Continents3] | 0.59/0.60/0.60 | 0.66/0.87/0.95 | 0.48/0.76/0.91 |
| centre [Pangaea1] | 0.02/0.02/0.51 | 0.57/0.87/0.95 | 0.46/0.74/0.89 |
| A | 0.00/0.01/0.59 | 0.45/0.81/0.94 | 0.23/0.60/0.84 |
| A [Archipelagos1] | 0.00/0.01/1.00 | 0.47/0.80/0.93 | 0.26/0.61/0.85 |
| A [Continents1] | 0.00/0.00/0.57 | 0.47/0.82/0.94 | 0.17/0.52/0.81 |
| A [Continents2] | 0.00/0.00/0.03 | 0.38/0.79/0.94 | 0.27/0.63/0.86 |
| A [Continents3] | 0.41/0.59/0.60 | 0.38/0.79/0.94 | 0.24/0.63/0.86 |
| A [Pangaea1] | 0.00/0.46/1.00 | 0.53/0.84/0.95 | 0.20/0.58/0.82 |
| B | 0.00/0.82/1.00 | 0.39/0.79/0.94 | 0.17/0.54/0.82 |
| B [Archipelagos1] | 0.00/0.02/1.00 | 0.42/0.79/0.94 | 0.20/0.55/0.82 |
| B [Continents1] | 0.00/1.00/1.00 | 0.38/0.80/0.94 | 0.13/0.47/0.79 |
| B [Continents2] | 0.00/1.00/1.00 | 0.40/0.79/0.94 | 0.20/0.58/0.83 |
| B [Continents3] | 0.59/1.00/1.00 | 0.37/0.79/0.94 | 0.18/0.57/0.83 |
| B [Pangaea1] | 0.00/0.00/1.00 | 0.38/0.77/0.93 | 0.15/0.52/0.80 |
| Aoff | 0.00/0.56/0.59 | 0.01/0.30/0.69 | 0.24/0.60/0.85 |
| Aoff [Archipelagos1] | n/a | 0.02/0.24/0.68 | 0.20/0.53/0.79 |
| Aoff [Continents1] | 0.00/0.28/0.56 | 0.01/0.25/0.59 | 0.24/0.55/0.86 |
| Aoff [Continents2] | 0.00/0.00/0.00 | 0.00/0.21/0.66 | 0.32/0.67/0.87 |
| Aoff [Continents3] | 0.55/0.59/0.60 | 0.11/0.52/0.81 | 0.25/0.68/0.91 |
| Aoff [Pangaea1] | 0.00/0.00/0.00 | 0.00/0.31/0.69 | 0.24/0.53/0.79 |

QoL P25/median/P75 by first technology (n camp-turns)
| run | AnimalHusbandry | Cultivation | Watercraft |
|---|---|---|---|
| centre | 0.41/0.77/0.91 (33681) | 0.64/0.83/0.94 (15027) | 0.77/0.90/0.98 (1753) |
| centre [Archipelagos1] | 0.50/0.77/0.91 (6648) | 0.60/0.81/0.92 (2946) | 0.77/0.88/0.97 (1353) |
| centre [Continents1] | 0.36/0.75/0.91 (7195) | 0.62/0.80/0.93 (3132) | n/a |
| centre [Continents2] | 0.46/0.79/0.92 (7153) | 0.69/0.86/0.95 (2938) | 0.95/0.98/0.99 (236) |
| centre [Continents3] | 0.37/0.75/0.90 (5770) | 0.68/0.86/0.96 (3214) | n/a |
| centre [Pangaea1] | 0.34/0.78/0.91 (6915) | 0.64/0.81/0.92 (2797) | 0.43/0.76/0.95 (164) |
| A | 0.16/0.60/0.87 (41700) | 0.42/0.72/0.89 (19999) | 0.59/0.85/0.95 (2362) |
| A [Archipelagos1] | 0.21/0.57/0.85 (7654) | 0.34/0.69/0.88 (4023) | 0.57/0.82/0.95 (1911) |
| A [Continents1] | 0.13/0.60/0.88 (9179) | 0.36/0.68/0.88 (3789) | n/a |
| A [Continents2] | 0.16/0.59/0.87 (8807) | 0.51/0.75/0.89 (4235) | 0.92/0.96/0.98 (205) |
| A [Continents3] | 0.12/0.57/0.87 (7141) | 0.51/0.77/0.91 (4590) | n/a |
| A [Pangaea1] | 0.15/0.66/0.89 (8919) | 0.38/0.69/0.86 (3362) | 0.54/0.92/0.98 (246) |
| B | 0.11/0.56/0.86 (44558) | 0.33/0.66/0.86 (23248) | 0.57/0.83/0.94 (2078) |
| B [Archipelagos1] | 0.15/0.50/0.83 (7912) | 0.26/0.63/0.84 (4785) | 0.55/0.82/0.93 (1711) |
| B [Continents1] | 0.08/0.55/0.87 (9634) | 0.30/0.63/0.85 (4501) | n/a |
| B [Continents2] | 0.14/0.59/0.87 (9441) | 0.39/0.69/0.86 (4735) | 0.85/0.89/0.96 (203) |
| B [Continents3] | 0.08/0.52/0.85 (7449) | 0.42/0.71/0.89 (5167) | n/a |
| B [Pangaea1] | 0.10/0.59/0.87 (10122) | 0.30/0.62/0.84 (4060) | 0.43/0.58/0.97 (164) |
| Aoff | 0.01/0.13/0.51 (4346) | 0.36/0.69/0.89 (11480) | 0.53/0.77/0.91 (574) |
| Aoff [Archipelagos1] | 0.01/0.15/0.53 (984) | 0.27/0.60/0.80 (1845) | 0.49/0.76/0.91 (451) |
| Aoff [Continents1] | 0.01/0.12/0.40 (902) | 0.35/0.69/0.90 (2378) | n/a |
| Aoff [Continents2] | 0.02/0.16/0.61 (902) | 0.45/0.74/0.89 (2337) | 0.90/0.91/0.92 (41) |
| Aoff [Continents3] | 0.01/0.04/0.29 (656) | 0.47/0.77/0.93 (2624) | n/a |
| Aoff [Pangaea1] | 0.00/0.18/0.59 (902) | 0.32/0.60/0.83 (2296) | 0.56/0.70/0.82 (82) |

### Chosen defaults and why

**Shipped: the analytic fit (N_band 220, k 7.1, p_tent 0.21, tents 3)** by orchestrator decision after review: no candidate
reaches the targets, the size tail is set by fission throughput (below), fission is off by default, and candidate A lowers
default QoL. The sweep's best candidate is kept as a documented alternative for when the fission bottleneck is fixed:

**Candidate A: N_band = 176, k = 4.97, p_tent = 0.21, tent capacity = 2**. A and B tie on the five-world pool (9.74 and 9.55,
a 2 percent difference inside the per-world spread); A is shipped because it departs less from the design onsets and keeps
more QoL (fixed-dominant median 0.60 against 0.54, tent-dominant 0.81 against 0.79). Against the analytic centre it reduces
the combined P90 / P95 error from 12.0 to 9.7 (19 percent), lowers tent-dominant P90 from 1,367 to 1,141 and fixed-dominant P90
from 1,683 to 1,581, and is better in four of the five worlds (Continents 2 is worse, see the per-world rows). Its onsets
(81 unhoused, 165 in tents, 481 fully housed) are earlier than the design's (101 / 252 / 607-999): the sweep objective is actual
camp size, not onset size, and every camp-size percentile improves as the scale falls.

Limits of the result, stated plainly:

- **No candidate reaches the targets.** Tent-dominant P90 stays above 1,000 and fixed-dominant P90 near 1,500 against targets of
  250 and 1,000. The best score is still 9.7; the scale parameters move sizes by tens of percent only.
- **Fission throughput, not the cohesion onset, sets the size tail.** In the centre run on Continents 1 (post-opening turns 10 to
  50, 10,054 camp-turns evaluated), a camp at 1.0 to 1.5 times its closed-form onset split in 0.8 percent of camp-turns and a camp
  at 2 to 3 times in under 1 percent; only camps at 0.5 to 1.0 times onset (7 percent) split often, because cohorts grow by a large factor
  in one 20-year turn and a split runs at most once per turn from a frozen snapshot with a daughter of at most 40 percent. The
  daughter check (daughter QoL at least `Q_now - 0.05`) blocked 1,040 of 1,348 evaluations (77 percent), because the estimator charged a
  daughter founded without housing `H = 0`, `N_s = N_band`. **Resolved by the fission-throughput change (Docs/Fission.md,
  "Throughput").** The daughter's estimate now counts her carried tents and the shelter her Builders assemble in her first turn
  (`Fission.DaughterShelter`), and `FissionRadius` is 4. Measured on five Standard/80 worlds (fission on, immigration off): the
  check blocks 10-12 percent of evaluations instead of 76 percent, fissions rise from 1,420 to 2,348 (+65 percent), camps at turn 60
  from 364 to 550 and population by 43 percent, with mean QoL unchanged to higher (0.698 to 0.715). The size tail still barely moves
  (fixed-dominant P90 1,683 to 1,504, tent-dominant 1,367 to 1,385; the unhoused class now includes large new daughters, P90 65 to 226):
  the remaining cause is the one-split-per-turn rule against several-fold cohort growth, not the daughter check or the
  destination radius (b1 to radius 5 changes P90 by under 3 percent). The earlier probe with `FissionDaughterMargin = 1` (fissions 291
  to 488, population +37 percent, tent and fixed P90 unchanged at 1,480 and 1,553) pointed the same way.
- **Fission off** (`Aoff`) freezes the map at 80 camps averaging about 1,065 people (85,000 total against 456,000) with P90 sizes
  above 1,500 in every class that has data.
- **Sampling.** Each configuration is five seeded worlds. Score differences below about 10 percent (A against B, or single
  one-at-a-time rows) are within what the per-world spread in the tables shows; only the joint direction (lower scale, smaller
  tents) is consistent.
- **Tent capacity 2 against the specified 3.** The brief set a default of 3; the sweep moved it to 2 (stage 1: 11.61 at 2, 12.23 at 3,
  12.92 at 4; stage 2: the same two pairs score 9.23 and 9.83 at 2 against 10.29 and 10.71 at 3, about 1.0 lower). The
  planner-ordering side effect of the lower capacity is recorded in Validation.

## Validation and what changed

- `QoLCohesionChecks` (1,157 assertions, from 643): closed forms of `H` and `N_s` for no housing, all tents, all permanent,
  mixes and over-capacity; `S` strictly decreasing and equal to the closed form; capacity coverage (3 per tent when pinned,
  4 per house, and the shipped tent capacity); people-based demand (7.5 houses or 10 tents for a 30-person gap); `S` unchanged
  across a Cultivation acquisition with identical housing (four housing structures); smooth `S` in added capacity; purity
  (repeat reads, unchanged fingerprint); fission gain holds `H` at relief 0 and interpolates; immigration dilution of the
  destination and source `H`; save round trip; legacy payload carrying the retired fields; validation rejects invalid values.
- `FissionChecks` (511, from 492): Crowding and Calibration are now by housing structure (none / tents / permanent);
  closed-form onsets for the shipped scale within 3.5 percent (integer daughters), the analytic fit reproducing every design onset,
  ordering Discourage > Neutral > Encourage and none < tents < permanent, and size-relative split / no-split cases.
- `ImmigrationChecks` (336, from 334) pin band 220 and housing gain 0 in the flow-rule fixtures (so the tuned utilities keep
  their meaning) and restore the shipped gain for the forward-estimator housing checks.
- `ExpansionClaimChecks` pins band 1,780 and gain 0 in its planner fixtures for the same reason.

- **Cost (Continents 1, seed 1, 80 tribes, 12 + 50 turns, one worker, fission on, nothing else running; median total turn time
  over the 50 post-opening turns, median allocation per turn).** HEAD before this change: 637 and 690 ms (two runs), 225 MB,
  469 camps at turn 60. First derived-cohesion implementation: 882 and 918 ms, 302 MB. After making the fixed-housing read allocation
  free and removing its per-call camp lookup (`FixedHousingAt(w, camp, days)`): 771 and 723 ms, 292 MB, 472 camps. The analytic
  centre (220 / 7.1 / 0.21 / tent 3) with 371 camps: 638 and 603 ms, 241 MB. Per camp that is 1.4 ms (HEAD), 1.6 ms (shipped) and 1.7 ms
  (centre) per turn, so the extra time follows the larger number of camps and their extra housing production rather than a new
  scaling term, but it is +12 percent total and +30 percent allocation against HEAD at equal camp count and is not isolated further
  (the dynamics differ between builds). Run-to-run spread between two identical runs is about 5 to 8 percent.
- **Planner-ordering sensitivity (recorded, not fixed).** `PastoralCraftChecks.HusbandryOnlyOrganicMultiTurnBootstrapFromZero`
  (one 100-person camp, priorities set by hand) produces Herding Tools and Light Housing within eight turns at three people per
  tent but, at the shipped two, only Hand Tools and Animal Fabric for sixteen turns (4 Light Housing units, no Herding Tools). The
  fixture now pins three people per tent. The sweep worlds show normal tent building (tent-dominant camps are 18,000 camp-turns of
  five worlds), so this is a single-camp ordering effect of the value rule, worth its own investigation.

## Follow-ups (not done here)

- Daughter housing: the daughter check charges the new camp for having no shelter although it builds tents during its first
  turn; crediting first-turn tents (or a housing grant in the split) would unblock a large share of fissions.
- The valuation of housing does not include its cohesion gain (a unit of housing raises `H` and hence `S` for the whole camp);
  adding `dS/dcapacity` to the claim value would make cohesion an incentive to build.
- Over-built tents can lift `H` above `p_tent` because the literal formula divides total tent capacity by `N`; planner demand
  targets people, so this does not arise in play, but a covered-people form (`min(1 - fixed share, tent share)`) would close it.
- Growth reading QoL: until the QoL-mortality change growth never read QoL, so only fission limited camp size and fission could not
  keep up with several-fold cohort growth. Growth now reads the committed QoL through the death rate (`Docs/QoLMortality.md`); the
  size tables there use this document's classes and targets.
