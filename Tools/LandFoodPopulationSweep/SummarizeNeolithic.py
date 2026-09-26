"""Export reviewable land-food calibration tables from completed raw audits."""

import argparse
import csv
import json
from pathlib import Path


def read_csv(path):
    with path.open(newline="", encoding="utf-8") as stream:
        return list(csv.DictReader(stream))


def write_csv(path, rows, fields):
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fields)
        writer.writeheader()
        writer.writerows(rows)


def population_trajectory(sweep, out_dir):
    turns = read_csv(sweep / "turns.csv")
    selected = []
    for row in turns:
        if int(row["initialPopulation"]) not in (24, 96, 192):
            continue
        turn = int(row["turn"])
        if row["mode"] == "controlled-q0" and turn != 8:
            continue
        if row["mode"] == "live" and turn not in (1, 8, 16, 24, 32):
            continue
        selected.append({key: row[key] for key in (
            "mode", "site", "seed", "tile", "initialPopulation", "turn",
            "startPopulation", "endPopulation", "technology", "openingG",
            "endingG", "endingB", "endingMaxG", "endingMaxB",
            "endingQ0MaxG", "q", "endingE", "fieldTiles",
            "landHumanHarvest", "landFoodRechargeAndReclassification",
            "landBiomassRecharge", "landFoodOverflow", "landBiomassOverflow",
            "landManagedRenewalInput", "meanOperatingFraction",
            "foodSatisfaction", "condition", "largestCapacityExcess")})
    if len(selected) != 54:
        raise ValueError(f"Expected 54 selected population trajectory rows, got {len(selected)}")
    write_csv(out_dir / "LandFoodNeolithicTrajectory.csv", selected, list(selected[0]))

    unharvested = [row for row in read_csv(sweep / "unharvested.csv")
                   if int(row["turn"]) == 8]
    if len(unharvested) != 3:
        raise ValueError("Expected three turn-eight zero-claim controls")
    write_csv(out_dir / "LandFoodCalibratedNoHarvest.csv", unharvested,
              list(unharvested[0]))


def standard_worlds(raw_root, out_dir):
    rows = []
    for shape in ("Continents", "Pangaea", "Archipelagos"):
        report = json.loads((raw_root / f"Standard80-{shape}.json").read_text(encoding="utf-8"))
        if report["players"] != 80 or len(report["samples"]) < 20:
            raise ValueError(f"Incomplete Standard world: {shape}")
        for sample in report["samples"]:
            camps = [camp for camp in sample["camps"]
                     if camp["technology"] == "Cultivation"]
            prior = sum(camp["priorResidents"] for camp in camps)
            rows.append({
                "shape": shape,
                "seed": report["seed"],
                "turn": sample["turn"],
                "phase": sample["phase"],
                "linkedPopulation": sample["population"],
                "priorCohortWeightedQoL": sample["priorWeightedQol"],
                "realizedFoodYield": sample["foodYield"],
                "cultivationFoodYield": sample["foodByKind"].get("Cultivation", 0),
                "cultivationTribes": len(camps),
                "cultivationLinkedPopulation": sum(camp["linked"] for camp in camps),
                "cultivationPriorCohortFoodSatisfaction":
                    sum(camp["priorResidents"] * camp["FoodNeedsMetRatio"]
                        for camp in camps) / prior if prior else "",
                "cultivationPriorCohortQoL":
                    sum(camp["priorResidents"] * camp["QoL"]
                        for camp in camps) / prior if prior else "",
            })
    write_csv(out_dir / "LandFoodStandard80.csv", rows, list(rows[0]))


def river_pilots(raw_root, out_dir):
    rows = []
    for label, directory in (("flux-0.00", "M30-LandR003-RiverFlux0"),
                             ("flux-0.65", "M30-LandR003-RiverManaged"),
                             ("flux-1.30", "M30-LandR003-RiverFlux130")):
        matrix = read_csv(raw_root / directory / "matrix.csv")
        if len(matrix) != 1 or int(matrix[0]["postTransitionTurns"]) != 25:
            raise ValueError(f"Incomplete river pilot: {label}")
        with (raw_root / directory / "trajectory.jsonl").open(encoding="utf-8") as stream:
            for line in stream:
                sample = json.loads(line)
                if sample.get("postTransitionTurns") not in (1, 5, 10, 15, 20, 25):
                    continue
                balances = sample["ecology"]
                rows.append({
                    "managedFlux": label,
                    "postCultivationTurn": sample["postTransitionTurns"],
                    "population": sample["population"],
                    "q": sample["q"],
                    "fieldCapital": sample["e"],
                    "openingG": sample["openingRadiusFood"],
                    "endingG": sample["endingRadiusFood"],
                    "endingB": sample["endingRadiusBiomass"],
                    "managedRenewalInputBeforeCaps": sample["managedRenewalInput"],
                    "meanOperatingFraction": sample["meanOperatingFraction"],
                    "realizedFoodRechargeAndReclassification":
                        sum(item["FoodRecharge"] for item in balances),
                    "foodOverflow": sum(item["FoodOverflow"] for item in balances),
                    "foodHarvest": sum(item["FoodHarvest"] for item in balances),
                    "foodSatisfaction": sample["foodSatisfaction"],
                    "QoL": sample["qols"]["qol"],
                    "meanLandCondition": sample["footprintMeanLandCondition"],
                })
    if len(rows) != 18:
        raise ValueError(f"Expected 18 selected river pilot rows, got {len(rows)}")
    write_csv(out_dir / "LandFoodRiverCultivation.csv", rows, list(rows[0]))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--raw-root", type=Path,
                        default=Path("TestResults/LandFoodPopulationSweep"))
    parser.add_argument("--out-dir", type=Path, default=Path("Docs"))
    args = parser.parse_args()
    population_trajectory(args.raw_root / "M30-LandR003-FullNeolithic", args.out_dir)
    standard_worlds(args.raw_root, args.out_dir)
    river_pilots(args.raw_root, args.out_dir)
    print("PASS: exported complete population, zero-claim, river and Standard-world tables")


if __name__ == "__main__":
    main()
