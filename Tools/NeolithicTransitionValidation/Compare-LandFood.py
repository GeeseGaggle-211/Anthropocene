"""Compare fixed-site opening land G against the prior-cohort seasonal trace.

Both traces must use the same selected seed and tile. The seasonal trace's
active-tile balances provide opening G; the candidate records that value for
every radius-one land tile directly. Ratios use each model's own first turn.
"""
import argparse
import csv
import json
from pathlib import Path


def read(path):
    rows = {}
    for line in Path(path).open(encoding="utf-8"):
        row = json.loads(line)
        if "turn" in row:
            rows[(row["id"], row["turn"])] = row
    return rows


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--baseline", required=True)
    parser.add_argument("--candidate", required=True)
    parser.add_argument("--out", required=True)
    args = parser.parse_args()
    baseline, candidate = read(args.baseline), read(args.candidate)
    cases = ("fixed-open-fertile", "fixed-medium-woodland", "fixed-dry-marginal")
    output = []
    for case in cases:
        first_b, first_c = baseline[(case, 1)], candidate[(case, 1)]
        baseline_tiles = {tile["id"] for tile in first_b["footprint"]}
        candidate_tiles = set(first_c["openingRadiusTiles"])
        if baseline_tiles != candidate_tiles:
            raise ValueError(f"{case}: radius-one land tiles differ")

        def baseline_food(row):
            stocks = {item["Tile"]: item["StartingFood"] for item in row["ecology"]}
            missing = baseline_tiles - stocks.keys()
            if missing:
                raise ValueError(f"{case}, turn {row['turn']}: missing active tiles {missing}")
            return sum(stocks[tile] for tile in baseline_tiles)

        first_stock_b = baseline_food(first_b)
        first_stock_c = first_c["openingRadiusFood"]
        if first_stock_b <= 0 or first_stock_c <= 0:
            raise ValueError(f"{case}: first-turn stock is zero")
        for turn in range(1, 100):
            key = (case, turn)
            if key not in baseline or key not in candidate:
                break
            b, c = baseline[key], candidate[key]
            if b["technology"] == "Cultivation" or c["technology"] == "Cultivation":
                break
            stock_b, stock_c = baseline_food(b), c["openingRadiusFood"]
            ratio_b, ratio_c = stock_b / first_stock_b, stock_c / first_stock_c
            output.append({"case": case, "turn": turn, "baselineStock": stock_b,
                           "candidateStock": stock_c, "baselineRatio": ratio_b,
                           "candidateRatio": ratio_c, "absoluteDifference": abs(ratio_b - ratio_c),
                           "baselineFoodProduced": b["demographic"]["FoodProduced"],
                           "candidateFoodProduced": c["demographic"]["FoodProduced"],
                           "baselineCondition": b.get("footprintMeanLandCondition",
                               sum(t["landCondition"] for t in b["footprint"]) / len(b["footprint"])),
                           "candidateCondition": c["footprintMeanLandCondition"],
                           "baselinePopulation": b["population"], "candidatePopulation": c["population"],
                           "baselineFoodSatisfaction": b["foodSatisfaction"],
                           "candidateFoodSatisfaction": c["foodSatisfaction"]})
    if not output:
        raise ValueError("No paired pre-cultivation rows")
    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    with out.open("w", encoding="utf-8", newline="") as file:
        writer = csv.DictWriter(file, fieldnames=output[0].keys())
        writer.writeheader()
        writer.writerows(output)
    print(f"paired={len(output)} maxAbsoluteDifference={max(row['absoluteDifference'] for row in output):.6f}"
          f" within0.10={all(row['absoluteDifference'] <= .10 for row in output)}")


if __name__ == "__main__":
    main()
