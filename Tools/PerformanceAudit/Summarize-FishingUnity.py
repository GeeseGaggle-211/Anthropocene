#!/usr/bin/env python3
"""Reduce complete matched banks, keeping first-technology cohorts diagnostic."""
import hashlib
import json
import pathlib
import sys

directory = pathlib.Path(sys.argv[1])
rows = []
for seed in (23, 101):
    for variant in ("control", "fishing"):
        path = directory / f"{variant}-{seed}.json"
        data = json.loads(path.read_text())
        assert data["seed"] == seed and data["players"] == 80
        assert data["worldWidth"] == 1024 and data["worldHeight"] == 512
        assert data["postOpeningTurnsMeasured"] == 40
        samples = [s for s in data["samples"] if s["postOpening"]]
        assert len(samples) == 40
        end = samples[-1]
        cohort = [c for c in end["camps"] if c["technology"] == "Watercraft"]
        residents = sum(c["Population"] for c in cohort)
        cohorts = [[c for c in s["camps"] if c["technology"] == "Watercraft"] for s in samples]
        histories = [c for camps in cohorts for c in camps]
        plans = [c["fishing"] for c in histories if c["fishing"]]
        ids = {c["Id"] for c in histories}
        actual = sum(p["realizedCatch"] for p in plans)
        forecast = sum(p["expectedCatch"] for p in plans)
        row = {
            "seed": seed, "variant": variant, "revision": data["revision"],
            "configurationHash": data["configHash"], "artifactSha256": hashlib.sha256(path.read_bytes()).hexdigest(),
            "openingTurns": data["openingTurnsMeasured"], "postOpeningTurns": 40,
            "worldPopulation": end["population"], "fishingFirstSettlements": len(cohort),
            "fishingFirstResidents": residents,
            "fishingFirstResidentWeightedQoL": sum(c["Population"] * c["QoL"] for c in cohort) / residents if residents else None,
            "fisherPersonTurns": sum(p["FisherCount"] for p in plans),
            "gathererPersonTurns": sum(max(0, c["priorResidents"] - sum(a["Population"] for a in c["assignments"])) for c in histories),
            "forecastResidentCatch": forecast, "realizedResidentCatch": actual,
            "realizedToForecast": actual / forecast if forecast else None,
            "plannedMaintenanceWorkerYears": sum(p["maintenanceYears"] for p in plans),
            "requiredMaintenanceWorkerYears": sum(p["requiredMaintenanceYears"] for p in plans),
            "plannedBuildWorkerYears": sum(p["buildYears"] for p in plans),
            "toolCoveredWorkerYears": sum(p["toolCoveredYears"] for p in plans),
            "toolsProduced": sum(c["production"].get("FishingTools", 0) for c in histories),
            "meanPlannedEndpointGroundCapital": None,
            "moves": sum(c["moved"] for c in histories),
            "villageAbandonments": sum(c["villageToCamp"] for c in histories),
            "fissions": sum(e["ParentCamp"] in ids for s in samples for e in s["fissionEvents"]),
            "emigrants": sum(e["Migrants"] for s in samples for e in s["migrationEvents"] if e["SourceCamp"] in ids),
            "immigrants": sum(e["Migrants"] for s in samples for e in s["migrationEvents"] if e["DestinationCamp"] in ids),
            "foodReceipts": {},
        }
        capital = [g["EndingCapital"] for c in cohort if c["fishing"] for g in c["fishing"]["grounds"]]
        if capital:
            row["meanPlannedEndpointGroundCapital"] = sum(capital) / len(capital)
        for c in histories:
            for kind, food in c["foodReceipts"].items():
                row["foodReceipts"][kind] = row["foodReceipts"].get(kind, 0) + food
        rows.append(row)
result = {"method": "Current production config; only resident Fishing and corrected aquatic pressure differ; Sailing off. First technology is diagnostic only. Gatherer person-turns are an endpoint staffing proxy against prior residents, not exact locked labor. Detailed timings are not clean performance comparisons.", "rows": rows}
(directory / "matched-summary.json").write_text(json.dumps(result, indent=2) + "\n")
for row in rows:
    print(row["seed"], row["variant"], "first-Watercraft residents", row["fishingFirstResidents"],
          "QoL", row["fishingFirstResidentWeightedQoL"], "catch ratio", row["realizedToForecast"])
