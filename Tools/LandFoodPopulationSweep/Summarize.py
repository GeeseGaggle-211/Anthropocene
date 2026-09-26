"""Check completeness and reduce the raw per-turn population sweep to one row per case."""

import argparse
import csv
from collections import defaultdict
from pathlib import Path


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True, type=Path)
    parser.add_argument("--out", required=True, type=Path)
    parser.add_argument("--controlled-turns", type=int, default=8)
    parser.add_argument("--live-turns", type=int, default=12)
    args = parser.parse_args()

    with args.input.open(newline="", encoding="utf-8") as stream:
        rows = list(csv.DictReader(stream))
    grouped = defaultdict(list)
    for row in rows:
        grouped[row["mode"], row["site"], int(row["initialPopulation"])].append(row)
    populations = sorted({key[2] for key in grouped})
    sites = sorted({key[1] for key in grouped})
    expected = 2 * len(sites) * len(populations)
    if len(grouped) != expected:
        raise ValueError(f"Expected {expected} mode/site/population groups, got {len(grouped)}")

    args.out.parent.mkdir(parents=True, exist_ok=True)
    with args.out.open("w", newline="", encoding="utf-8") as stream:
        writer = csv.writer(stream)
        writer.writerow(("mode", "site", "initialPopulation", "turns", "finalPopulation",
                         "initialG", "finalG", "finalMaxG", "initialB", "finalB",
                         "finalMaxB", "finalGOverMaxG", "finalBOverMaxB",
                         "finalTurnFoodProduced", "finalTurnBasicNeed", "minFoodSatisfaction",
                         "finalFoodSatisfaction", "finalQ", "finalCondition",
                         "largestCapacityExcess"))
        for key in sorted(grouped):
            mode, site, initial_population = key
            run = sorted(grouped[key], key=lambda row: int(row["turn"]))
            expected_turns = args.controlled_turns if mode == "controlled-q0" else args.live_turns
            if len(run) != expected_turns or [int(row["turn"]) for row in run] != list(range(1, expected_turns + 1)):
                raise ValueError(f"Incomplete run for {key}: {len(run)} turns")
            first, last = run[0], run[-1]
            if mode == "controlled-q0" and any(int(row["startPopulation"]) != initial_population or
                                                int(row["endPopulation"]) != initial_population or
                                                float(row["q"]) != 0 for row in run):
                raise ValueError(f"Controlled population or q changed for {key}")
            maximum_excess = max(float(row["largestCapacityExcess"]) for row in run)
            if maximum_excess > 1e-8:
                raise ValueError(f"Capacity overshoot in {key}: {maximum_excess}")
            writer.writerow((mode, site, initial_population, len(run), last["endPopulation"],
                             first["openingG"], last["endingG"], last["endingMaxG"],
                             first["openingB"], last["endingB"], last["endingMaxB"],
                             float(last["endingG"]) / max(1e-9, float(last["endingMaxG"])),
                             float(last["endingB"]) / max(1e-9, float(last["endingMaxB"])),
                             last["foodProduced"], last["basicFoodNeed"],
                             min(float(row["foodSatisfaction"]) for row in run),
                             last["foodSatisfaction"], last["q"], last["condition"],
                             maximum_excess))
    print(f"PASS: summarized {len(grouped)} complete cases across {len(sites)} sites and {len(populations)} starting populations")


if __name__ == "__main__":
    main()
