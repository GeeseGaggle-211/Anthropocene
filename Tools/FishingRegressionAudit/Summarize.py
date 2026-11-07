#!/usr/bin/env python3
"""Reduce completed cohort and identical-state experiments; retain source hashes."""
import hashlib
import json
import pathlib
import sys

root = pathlib.Path(__file__).resolve().parents[2]
directory = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else root / "TestResults/FishingRegression")
output = pathlib.Path(sys.argv[2] if len(sys.argv) > 2 else root / "Docs/FishingMixedWorkEvidence.json")


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read(name):
    path = directory / (name + ".jsonl")
    rows = [json.loads(line) for line in path.read_text().splitlines()]
    meta = json.loads(path.with_suffix(".jsonl.metadata.json").read_text())
    assert meta["artifactSha256"].lower() == sha(path)
    assert max(row["turn"] for row in rows) == meta["endTurn"]
    return rows, meta


def cohort(name, end, opening):
    rows, meta = read(name)
    assert meta["endTurn"] == end and end - opening == 40
    series = []
    for turn in range(opening + 1, end + 1):
        camps = [row for row in rows if row["turn"] == turn]
        assert camps and len({row["Id"] for row in camps}) == len(camps)
        population = sum(row["Population"] for row in camps)
        series.append({"turn": turn, "settlements": len(camps), "residents": population,
                       "weightedQoL": sum(row["Population"] * row["QoL"] for row in camps) / population})
    final = [{"camp": row["Id"], "population": row["Population"], "site": row["Tile"]}
             for row in rows if row["turn"] == end]
    founders = {}
    if meta["seed"] == 23:
        for camp in (1435, 1447):
            history = [row for row in rows if row["Id"] == camp]
            first_good = lambda good: next((row["turn"] for row in history if any(
                p["Good"] == good and p["Output"] > 0 for p in row["production"])), None)
            stages = {}
            for row in history:
                if row["fission"]:
                    stage = row["fission"]["Stage"]
                    stages[stage] = stages.get(stage, 0) + 1
            founders[camp] = {"firstCloth": first_good(2), "firstHousing": first_good(4),
                              "cultivation": next((row["turn"] for row in history if row["technologies"][2]), None),
                              "finalPopulation": history[-1]["Population"], "fissionStages": stages}
    return {"metadata": meta, "series": series, "finalCamps": final, "founders": founders}


before, before_meta = read("before-frozen")
after, after_meta = read("final-frozen")
assert before_meta["inputHash"] == after_meta["inputHash"]
assert before_meta["initialFingerprint"] == after_meta["initialFingerprint"]
witness = []
for camp in (1435, 1447):
    a = next(row for row in before if row["Id"] == camp and row["turn"] == 12)
    b = next(row for row in after if row["Id"] == camp and row["turn"] == 12)
    assert a["prior"] == b["prior"] and a["allocation"] == b["allocation"] and a["assignments"] == b["assignments"]
    def detail(row):
        d, allocation = row["demography"], row["allocation"]
        return {"openingResidents": row["prior"]["Population"], "endingResidents": row["Population"],
                "plannedCloth": allocation["Ceilings"]["Cloth"], "materialGatherers": allocation["MaterialGatherers"],
                "materialWorkerIntervals": sum(t["WorkerIntervals"] for t in row["materials"] if t["Role"] == "Gatherer"),
                "handTools": sum(p["Output"] for p in row["production"] if p["Good"] == 0),
                "cloth": sum(p["Output"] for p in row["production"] if p["Good"] == 2),
                "foodReceipts": row["receipts"], "clothingCoverage": d["ClothingCoverage"],
                "housingCoverage": d["HousingCoverage"], "exposureDeaths": d["ExposureDeaths"],
                "births": d["ContinuousBirths"], "personYears": d["PersonYears"]}
    witness.append({"camp": camp, "priorAllocationAndAssignmentsEqual": True, "before": detail(a), "after": detail(b)})

baseline = json.loads((root / "Docs/NeolithicFishingUnityEvidence.json").read_text())["matchedCohorts"]
cases = {name: cohort(name, end, opening) for name, end, opening in [
    ("final-control-23", 48, 8), ("final-fishing-23", 48, 8),
    ("final-control-101", 50, 10), ("final-fishing-101", 50, 10),
    ("baseline-pressure-only-23", 48, 8)]}
opening_checks = {}
for seed, expected in ((23, 8), (101, 10)):
    for enabled in (0, 1):
        name = f"final-{'fishing' if enabled else 'control'}-{seed}"
        meta = cases[name]["metadata"]
        if meta.get("openingCompletedTurn") is not None:
            assert meta["openingCompletedTurn"] == expected
            continue
        # The first completed long runs predated the probe's opening marker.
        # Their short exact-input replays verify the post-opening horizon.
        rows, check = read(f"opening-{seed}-{enabled}")
        assert check["openingCompletedTurn"] == expected
        assert check["initialFingerprint"] == meta["initialFingerprint"]
        assert check["configurationHash"] == meta["configurationHash"]
        original, _ = read(name)
        assert rows == [row for row in original if row["turn"] <= check["endTurn"]]
        opening_checks[name] = check
source_files = ["Assets/_Anthropocene/Simulation/GenerationalEconomy.cs", "Assets/_Anthropocene/Simulation/FoodHorizonForecast.cs"]
result = {"baselineSource": "64ce95343bb5d9f6d04d72dc017ec8352a49323b",
          "correctionCommit": "b6a690ad2785409ff41bcfd393c74630463ad392",
          "correctedSourceSha256": {name: sha(root / name) for name in source_files},
          "historicalMatchedCohorts": baseline, "correctedAndPressureOnlyCohorts": cases,
          "openingVerification": opening_checks,
          "frozenExperiment": {"beforeMetadata": before_meta, "afterMetadata": after_meta, "witnesses": witness},
          "checks": {name: {"sha256": sha(directory / name), "results": [line for line in (directory / name).read_text().splitlines()
                            if line.startswith(("PASS:", "TIMING fixed", "System.Exception:"))]}
                     for name in ("fixed-suite.log", "final-followup.log", "baseline-regression-test.log")},
          "limits": ["The original five-world 50-turn replay suites have not been rerun after this correction.",
                     "Native Unity verification remains unavailable.", "The residual seed-23 lineage deficit remains a rollout blocker."]}
output.write_text(json.dumps(result, indent=2) + "\n")
for name, case in cases.items():
    print(name, case["series"][-1])
