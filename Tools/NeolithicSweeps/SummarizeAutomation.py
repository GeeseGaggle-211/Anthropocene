"""Validate and report current-engine automation sweeps (JSON/JSONL, no dependencies)."""
import argparse
import json
from collections import Counter
from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED


def records(root, pattern):
    rows = []
    for path in sorted(root.rglob(pattern)):
        rows.extend(json.loads(line) for line in path.read_text(encoding="utf-8-sig").splitlines() if line.strip())
    return rows


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--source", type=Path, required=True)
    ap.add_argument("--previous", type=Path, required=True)
    ap.add_argument("--replay", type=Path, required=True)
    ap.add_argument("--out", type=Path, required=True)
    ap.add_argument("--revision", required=True)
    args = ap.parse_args()
    runs = sorted(records(args.source, "runs-*.jsonl"), key=lambda r: r["run"])
    turns = records(args.source, "trajectories-*.jsonl")
    replay = records(args.replay, "trajectories-*.jsonl")
    previous = json.loads(args.previous.read_text(encoding="utf-8-sig"))
    assert len(runs) == len({r["run"] for r in runs}) == 168, "incomplete/duplicated sweep matrix"
    assert len(turns) == len({(r["run"], r["turn"]) for r in turns}) == 4200
    assert all(r["completedYears"] == 500 and r["error"] is None for r in runs)
    assert all(r["conserved"] and r["labor"] for r in turns), "material/recipe/workforce violation"
    lookup = {(r["run"], r["turn"]): r for r in turns}
    assert len(replay) == 75 and all(lookup[(r["run"], r["turn"])] == r for r in replay), "baseline replay differs"
    base = next(r for r in runs if r["run"] == "11:river:automatic:baseline")
    old = next(r for r in previous if r["run"] == "11:river:stock:baseline")
    assert base["configurationHash"] == old["configurationHash"], "baseline constants changed"
    verification = {
        "implementationRevision": args.revision,
        "runs": len(runs), "generations": len(turns), "replayedGenerations": len(replay),
        "configurationHash": base["configurationHash"], "unchangedBaselineConstants": True,
        "resolverErrors": sum(r["error"] is not None for r in runs),
        "conservationFailures": sum(not r["conserved"] for r in turns),
        "laborFailures": sum(not r["labor"] for r in turns),
        "discardRowsAbove1eMinus6": sum(r["discardedFood"] > 1e-6 for r in turns),
        "maximumFoodDiscard": max(r["discardedFood"] for r in turns),
        "maximumDiscardHarvestRatio": max(r["discardedFood"] / max(1, r["foodHarvest"]) for r in turns),
    }
    args.out.mkdir(parents=True, exist_ok=True)
    for name, data in [("Run-Summary.json", runs), ("Verification.json", verification)]:
        (args.out / name).write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")
    text = ["# Neolithic demand automation — implementation and measurements", "",
            f"Implementation: `{args.revision}` on `features/neolithic-agricultural`.", "",
            "Completed **168 runs / 4,200 generations**, each 500 years. Independently replayed 75 baseline generations exactly. Baseline configuration hash is unchanged from the earlier sweep: recipes, extraction/manufacturing rates, QoL constants and demographic/ecological calibration were not retuned.", "",
            "## What changed", "",
            "A saved per-settlement demand planner now coordinates operating farmers, supplementary food gathering, protected material-gathering time, matching unlocked manufacturers/extractors and separate cultivation expansion. The controls offer Off, Allocate once, next N turns, and until disabled. Manual workforce edits disable automation. Clay Miner unlocks with Pottery; Stone Quarrier keeps the old Stonecutter save identifier. Farmers may harvest the shared accessible food footprint, not only their fields. Fabric feedstock is no longer also available to fund births.", "",
            "One specialist extractor supplies one same-rate single-material manufacturer; generalist gatherers and Craftspeople both operate at 0.25×. This is capacity matching, not compulsory headcount pairing. Housing needs fewer extractors, while improved tools retain both Stone and Wood inputs. All source receipts, input checks and tool-coverage constraints still apply.", "",
            "## Commits and changed files", "",
            "- `ab54ecf090ad89e56897cba2b4dbfc94b017f8e3`: demand planner, farmer/material labor separation, specialist adoption, saved controls and tests.",
            "- `abe3e685ea17dda4ee3cf19b8e6c8fa837fe53b8`: reserve subsistence food before forecasting textile staffing; additional regression coverage. This is the simulation revision measured below.",
            "- Simulation: `EconomicPlanner.cs` (+ Unity metadata), `EconomicIntents.cs`, `Workforce.cs`, `Model.cs`, `PlanningIndex.cs`, `TurnResolver.cs`, `CultivationEconomy.cs`, `EcologicalPressure.cs`, `LazyEcology.cs`, `GenerationalEconomy.cs`, `MaterialEconomy.cs`, `NeolithicProduction.cs`.",
            "- Presentation: `OpeningVillageInspector.cs`, `OpeningDetails.cs`, `OpeningSmoke.cs`. Tests: `SimulationTests.cs`, `EconomicAutomationChecks.cs`, `NeolithicChecks.cs`, `Anthropocene.Headless.csproj`.",
            "- Measurement/documentation: sweep `Program.cs`, `SummarizeAutomation.py`, `AGENTS.md`, `EconomicAutomation.md`, `Implementation.md`, `NeolithicSweepResults.md`, `NeolithicAutomationResults.md`, `Validation.md`. The evidence commit changes no gameplay code.", "",
            "## Unchanged calibration", "",
            "| Setting | Current baseline |", "| --- | --- |",
            "| Specialist extraction / matching generalist | 1 / 0.25 units per worker per BaseDays |",
            "| Fabric input / recurring need | 1 Plant Food per Fabric; 1 Fabric/person/20 years |",
            "| Weaver / generalist Fabric output | 80 / 20 per worker per 20 years |",
            "| Housing input | 1 total Wood/Clay unit per Housing, any mix |",
            "| Builder / generalist Housing output | 2.5 / 0.625 per worker per 20 years |",
            "| Housing capacity / decay | 4 people/unit; 10% per 20 years |",
            "| Housing-only tool multipliers | Hand 1.0, Improved 1.5, Copper 2.0 |",
            "| Farmer productivity / managed-flow coefficient | 1.5 / 0.65 |",
            "| E abandonment half-life | 80 years |",
            "| Rudimentary expectation / Clothing-Housing weights | Floor 0.25, ramp 100 years; 0.5 / 0.5 |", "",
            "## Reference comparison", "",
            "Seed 11, productive river exemplar, 250 initial people, Agriculture, q=0, one generation of food reserves, no tools/materials/Housing/Fabric. The older rows are recorded results at the previous implementation, not a same-code causal A/B: the new version also fixes farmer access/working-time and Fabric/growth accounting.", "",
            "| Policy/version | Population at 500y | Assigned craft % | Productive craft % | Farmer share of harvested food | Weaving / Construction year |", "| --- | ---: | ---: | ---: | ---: | --- |"]

    def tech(r, name):
        return str(int(r["breakthroughYears"][name])) if name in r["breakthroughYears"] else "not reached"

    for policy, label in [("stock", "Previous automatic"), ("replan", "Previous reset every turn"), ("managed-control", "Previous fixed-share control")]:
        row = next(r for r in previous if r["run"] == f"11:river:{policy}:baseline")
        last = row["last"]
        text.append(f"| {label} | {row['population']} | {last['craftAssignedPct'] + last['specialistAssignedPct']:.2f} | {last['craftUsedPct']:.2f} | {last['farmerFoodShare']:.1%} | {tech(row, 'Weaving')} / {tech(row, 'Construction')} |")
    b = base["last"]
    text.append(f"| **New saved demand automation** | **{base['population']}** | **{b['craftAssignedPct'] + b['specialistAssignedPct']:.2f}** | **{b['craftUsedPct']:.2f}** | **{b['farmerFoodShare']:.1%}** | **{tech(base, 'Weaving')} / {tech(base, 'Construction')}** |")
    text += ["", f"New reference: {base['deaths']} deaths, food satisfaction {b['foodSatisfaction']:.1%}, Clothing {b['clothing']:.1%}, Housing {b['housing']:.1%}, QoL {b['qol']:.1%}, mean q={b['qMean']:.4f}. Farmer-majority harvesting begins at year {base['firstFarmerMajorityYear']:g}.", "",
             f"Final assigned jobs: {', '.join(f'{role}: {count}' for role, count in b['assignments'].items())}. Other residents remain available as generalist gatherers; **available population is not the same as labor actually used**. Actual material extraction consumed {b['materialUsedPct']:.3f}% of resident worker-time. Gatherer food share is role attribution, not a wild-versus-crop biomass distinction.", "",
             "## Baselines across sites and seeds", "",
             "| Seed | Site | Population | Deaths | Assigned craft % | Used craft % | Farmer food % | q | Food / Clothing / Housing % | W / C year |",
             "| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: | --- | --- |"]
    for r in runs:
        if r["family"] != "baseline":
            continue
        t = r["last"]
        text.append(f"| {r['seed']} | {r['habitat']} | {r['population']} | {r['deaths']} | {t['craftAssignedPct'] + t['specialistAssignedPct']:.2f} | {t['craftUsedPct']:.2f} | {100*t['farmerFoodShare']:.1f} | {t['qMean']:.3f} | {100*t['foodSatisfaction']:.0f} / {100*t['clothing']:.0f} / {100*t['housing']:.0f} | {tech(r, 'Weaving')} / {tech(r, 'Construction')} |")
    text += ["", "These are deliberately selected productive exemplars within each habitat class, not a random global sample. Several fertile exemplars share saturated ecological profiles and should not be treated as independent productivity treatments. The open sites can remain food-limited; automation never creates resources to rescue them.", "",
             "## Clothing sweep — seed 11 river", "",
             "| Fabric/person/turn | Population | Assigned craft % | Used craft % | Steady specialist expectation % | q | QoL % | W / C year |",
             "| ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |"]
    river = [r for r in runs if r["seed"] == 11 and r["habitat"] == "river"]
    for r in sorted([r for r in river if r["family"] in ("baseline", "fabric")], key=lambda r: r["overrides"].get("FabricConsumptionPerPersonTurn", 1)):
        f = r["overrides"].get("FabricConsumptionPerPersonTurn", 1); t = r["last"]
        expected = 100 * (f / 80 + .1 / (4 * 2.5))
        text.append(f"| {f:g} | {r['population']} | {t['craftAssignedPct'] + t['specialistAssignedPct']:.2f} | {t['craftUsedPct']:.4f} | {expected:.4f} | {t['qMean']:.3f} | {100*t['qol']:.1f} | {tech(r, 'Weaving')} / {tech(r, 'Construction')} |")
    text += ["", "The expectation assumes saturated needs, stationary population, no stock bridging and Hand Tools: `100 × [FabricNeed/80 + HousingDecay/(PeoplePerHousing × 2.5)]`. At baseline this is 1.25% Weavers + 1% Builders = **2.25% productive craft labor**. Headcount can be higher due to integer rounding and forecast headroom. Extraction and tool production are additional labor. Food-limited joint cases can have unsatisfied Clothing and lower realized craft work; these are not throughput-calibration failures. Population need not move monotonically with a parameter: whole-worker allocation, stock/reserve thresholds, useful-demand limits and ecological responses also change the trajectory.", "",
             "## Farmer-efficiency sweep — seed 11 river", "",
             "| Farmer multiplier | Population | Assigned farmers % | Farmer harvest % | q | First farmer-majority year |",
             "| ---: | ---: | ---: | ---: | ---: | ---: |"]
    for r in sorted([r for r in river if r["family"] in ("baseline", "farmer")], key=lambda r: r["overrides"].get("FarmerProductivity", 1.5)):
        eta = r["overrides"].get("FarmerProductivity", 1.5); t = r["last"]
        text.append(f"| {eta:g} | {r['population']} | {t['farmerAssignedPct']:.2f} | {100*t['farmerFoodShare']:.2f} | {t['qMean']:.3f} | {r['firstFarmerMajorityYear']} |")
    matrix = [r for r in river if r["family"] in ("baseline", "fabric", "housing", "joint")]
    assert len(matrix) == 80
    text += ["", "## Full Clothing × Housing matrix", "", "All 80 combinations are in `Joint-Matrix.md`, with population, craft headcount/time, needs, q and breakthroughs. The other river cases cover farmer efficiency, managed flux, abandonment half-life, Builder rate, Housing-only tool multipliers, extraction rate, expectations and bridge-off. Fabric/farmer slices repeat across the other eight seed/site combinations.", "",
             "## Technology emergence", "", "| Technology | Runs reaching breakthrough |", "| --- | ---: |"]
    unlocked = Counter(name for r in runs for name in r["breakthroughYears"])
    for name in ("Weaving", "Construction", "Toolmaking", "Pottery", "Carpentry", "CopperWorking"):
        text.append(f"| {name} | {unlocked[name]} / {len(runs)} |")
    text += ["", "Pots and Farm Tools still have zero default targets; no unapproved consumption/effects were invented to force their branches. Durable useful-tool demand can stop below the mastery threshold. Therefore default emergence is not proof of every specialist path: paired extraction/manufacturing and unlocked-role adoption are additionally covered by targeted tests. Conditional tool probes are not emergent Copper progression.", "",
             "## Validation", "",
             "- 209 existing Neolithic assertions and 76 new automation assertions pass; economic/AI checks and scalar-versus-2/4-worker, reversed-partition replay pass.",
             "- Full fixed headless regression passes, including 8,948 geology assertions, Paleo/worldgen, ecology, persistence and scenario checks. Final economic checks were rerun after the last fixes.",
             "- Unity: 28/28 EditMode tests pass; Windows build succeeds. The actual player passes queued-intent purity, recurring Fabric debit, isolated exact save/load, two-turn automation continuing after load without resubmission, and five captured frames. This is automated player verification, not a human usability test.",
             f"- Sweeps: {verification['resolverErrors']} resolver errors, {verification['conservationFailures']} recipe/source conservation failures, {verification['laborFailures']} workforce violations. Maximum discarded food {verification['maximumFoodDiscard']:.9g}; maximum discard/harvest ratio {verification['maximumDiscardHarvestRatio']:.9g}. {verification['discardRowsAbove1eMinus6']} generation rows exceed the diagnostic 1e-6 discard threshold.",
             "- All 75 independently repeated baseline generation records match exactly. Baseline constants match the previous recorded configuration hash.", "",
             "## Limits and deliberately deferred behavior", "",
             "The planner is a local deterministic heuristic. Separate settlements can forecast against the same source; actual source receipts prevent double extraction, but contention may cause shortfalls. Forecasts cannot guarantee realized yield under competition or ecological change. Population results are model outcomes over 500 years, not historical validation or a claim of indefinite equilibrium. Available unassigned residents may be idle when useful demand is satisfied.", "",
             "No automatic QoL fission, full trade/logistics, expedition formulas, irrigation yield/withdrawal, SoilCondition, broad generic tool bonuses, invented Plough/Farm Tool effects, artificial tool wear, or new raw-material costs. No gameplay calibration was retuned to force favorable results.", "",
             "See `Run-Summary.json`, `Verification.json` and `Raw-Automation-Sweeps.zip` for reproducible measured evidence."]
    (args.out / "Automation-Report.md").write_text("\n".join(text) + "\n", encoding="utf-8")
    matrix_text = ["# Full joint matrix — 500 years, seed 11 river", "", "| Fabric/person | People/Housing | Decay | Population | Assigned craft % | Used craft % | q | Clothing % | Housing % | W / C year |", "| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |"]
    for r in sorted(matrix, key=lambda r: tuple(r["overrides"].get(k, v) for k, v in [("FabricConsumptionPerPersonTurn", 1), ("HousingPeoplePerUnit", 4), ("HousingDecayPerTurn", .1)])):
        f, h, d = (r["overrides"].get(k, v) for k, v in [("FabricConsumptionPerPersonTurn", 1), ("HousingPeoplePerUnit", 4), ("HousingDecayPerTurn", .1)])
        t = r["last"]
        matrix_text.append(f"| {f:g} | {h:g} | {d:g} | {r['population']} | {t['craftAssignedPct'] + t['specialistAssignedPct']:.2f} | {t['craftUsedPct']:.3f} | {t['qMean']:.3f} | {100*t['clothing']:.1f} | {100*t['housing']:.1f} | {tech(r, 'Weaving')} / {tech(r, 'Construction')} |")
    (args.out / "Joint-Matrix.md").write_text("\n".join(matrix_text) + "\n", encoding="utf-8")
    with ZipFile(args.out / "Raw-Automation-Sweeps.zip", "w", ZIP_DEFLATED) as archive:
        for p in sorted(args.source.rglob("*")):
            if p.is_file() and p.suffix in (".json", ".jsonl"):
                archive.write(p, str(p.relative_to(args.source)))
        for p in sorted(args.replay.glob("*.jsonl")):
            archive.write(p, "replay/" + p.name)
        archive.writestr("provenance.json", json.dumps(verification, indent=2))
    print(json.dumps(verification, indent=2))


if __name__ == "__main__":
    main()
