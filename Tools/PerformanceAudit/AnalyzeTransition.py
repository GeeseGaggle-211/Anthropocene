"""Compare paired isolated-transition trajectories at the first divergent turns."""

import json
import sys


def rows(path):
    with open(path, encoding="utf-8") as source:
        return {(row["id"], row["turn"]): row
                for line in source
                if (row := json.loads(line)).get("turn") is not None}


def amount(row, good):
    return sum(item["Output"] for item in row["production"]
               if item["good"] == good)


def receipts(row):
    result = {}
    for receipt in row["materials"]["receipts"]:
        material = receipt["material"]
        result[material] = result.get(material, 0) + receipt["Amount"]
    return result


def compact(row):
    demographic = row["demographic"]
    return {
        "orders": row["orders"],
        "plan": row["submittedAssignments"],
        "starting": row["startingAssignments"],
        "population": row["population"],
        "residents": row["residentPopulation"],
        "food": round(demographic["FoodProduced"], 3),
        "consumed": round(demographic["FoodConsumed"], 3),
        "craftFood": round(demographic["ManufacturingFood"], 3),
        "cloth": round(amount(row, "Cloth"), 3),
        "housing": round(amount(row, "Housing"), 3),
        "handTools": round(amount(row, "HandTools"), 3),
        "material": {key: round(value, 3) for key, value in receipts(row).items()},
        "qol": round(row["qols"]["qol"], 6),
        "reserves": round(row["campReserves"], 3),
        "q": round(row["q"], 6),
        "e": round(row["e"], 6),
    }


def main():
    old, new = rows(sys.argv[1]), rows(sys.argv[2])
    wanted = set(sys.argv[3:])
    for case, turn in sorted(old):
        if wanted and case not in wanted:
            continue
        if (case, turn) not in new:
            print(f"{case} t{turn}: missing optimized row")
            continue
        a, b = compact(old[case, turn]), compact(new[case, turn])
        differences = {key: (a[key], b[key]) for key in a if a[key] != b[key]}
        if differences:
            print(f"{case} t{turn}: {json.dumps(differences, separators=(',', ':'))}")


if __name__ == "__main__":
    main()
