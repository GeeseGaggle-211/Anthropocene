# Headless gameplay review — 12 November 2026

**Historical pre-fix recording.** The commissioning-material issue documented
below has since been corrected. The [80-tribe follow-up](AgriculturalTechnologyGameplay80.md)
records the correction, regression checks and new smoothed-calendar playthrough.
Numbers on this page deliberately retain the original build's results.

These recordings use the actual AI, Elder, planner and authoritative resolver
at implementation commit `e6c96d6`, with a read-only capture harness. The local
HTML player displays committed snapshots; it is not a Unity player build.
No simulation rules were changed for this review. Publication remains paused.

## Fresh-world run

Seed 23, 96×48 world, four initial players, agricultural opening profile and
smoothed calendar. No diagnostic knowledge grants, resource grants or direct
construction orders. The run ends at physical year 1407.342144.

| Observed milestone | Physical year |
| --- | ---: |
| First Plough attainment anywhere (Society 22) | 585.591372 |
| First Irrigation attainment (Society 51) | 647.321645 |
| Village 52 first canal construction receipt | 860.917723 |
| First canal complete at boundary | 977.429627 |
| First irrigation water receipt | 989.902628 |

Village 52 belongs to Society 51 and does not have usable Plough during this
natural run. Its first canal investment passes the individual fertile-tile
gate: all five eligible tiles have q ≥ 0.30; the minimum is 0.311324.
At the final interval, withdrawal is 1,260.5, delivery 811.7 and losses 448.8
(rounded). River-treated soil development reaches 42.52% and 18.35% on its two
serviced fields. These are outcomes with the defect described below.

## Matched saved-game comparison

The earlier seed-23 checkpoint grants the technologies identically before the
fork. The enabled arm uses Plough plus water and soil development; the control
disables both technology effects. Both run from year 764.521458 to 1367.863271,
with normal endogenous Elder staffing. Capture preserves the exact prior sweep
final fingerprints, and all final saves pass save/load fingerprint checks.

| Village 52 at the final matching date | Enabled | Control |
| --- | ---: | ---: |
| Residents | 1,000 | 1,000 |
| Food reserves | 120,000 | 92,795.16 |
| Operating Farmers | 264 | 313 |
| Farm Tools inventory | 132.16 | 156.69 |
| Completed canals | 2 | 0 |

This is one joint-treatment gameplay example, not isolated attribution of each
technology's benefit. Population alone is not an economic acceptance measure.

## Blocking finding

`NeolithicProduction.cs` builds the canal recipe with construction materials
plus prepaid first-period upkeep. Housing Material manufacturing demand uses
only the construction-material coefficient. For the first Village 52 project,
the Elder authorizes one segment, but supplies 10 materials against approximately
11.3445 required. Resolution builds only 0.88148177 of the segment, using 7.3704
Builder-years. Repeated underfunding produces successively smaller increments.

The first water receipt arrives 128.984905 physical years after the first
construction receipt. It records delivery during the interval after completion,
not instantaneous delivery at its closing timestamp. This finding blocks the
approved forecast/authoritative-resolution agreement requirement despite the
previous regression results. The demand calculation needs the shared recipe
cost, a regression covering manufacture of missing commissioning materials,
and fresh gameplay and smoothed-calendar balance validation after correction.

## Reproduce and inspect

From the repository root, with .NET 8 available and the earlier seed-23 sweep
checkpoint in `TestResults/AgriculturalTechnology/seed-23.anthro`:

```bash
dotnet build Tools/AgriculturalTechnologySweep/Anthropocene.AgriculturalTechnologySweep.csproj -c Release
dotnet Tools/AgriculturalTechnologySweep/bin/Release/net8.0/Anthropocene.AgriculturalTechnologySweep.dll --gameplay --seed=23 --natural --years=1400 --output=TestResults/AgriculturalGameplay/natural
dotnet Tools/AgriculturalTechnologySweep/bin/Release/net8.0/Anthropocene.AgriculturalTechnologySweep.dll --gameplay --seed=23 --output=TestResults/AgriculturalGameplay/matched
python3 Tools/AgriculturalTechnologySweep/RenderGameplay.py
```

Open `TestResults/AgriculturalGameplay/player.html` in a browser. Play, turn
stepping and the timeline inspect snapshots without issuing orders. Scenario
and treatment selectors expose the fresh-world and matched runs. Full captures,
opening saves and final saves remain in the respective output directories.
The viewer is self-contained and makes no external requests. Headless Chromium
checks passed for scenario/treatment switching, stepping and playback, with no
JavaScript errors. Unity interaction validation remains pending.
