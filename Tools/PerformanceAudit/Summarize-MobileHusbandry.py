"""Summarize matched Standard/80 husbandry audits made by PerformanceAudit.

Usage: python Tools/PerformanceAudit/Summarize-MobileHusbandry.py <audit-dir> [candidate-prefix]
Expected names: Baseline-80-<seed>.json, <prefix>Mobile-80-<seed>.json,
<prefix>Stationary-80-<seed>.json for seeds 11, 23, and 1729.
"""

import json
import statistics
import sys
from pathlib import Path


SEEDS = (11, 23, 1729)
VARIANTS = ("Baseline", "Stationary", "Mobile")


def ratio(rows, prefix):
    requested = sum(float(row[f"{prefix}Requested"]) for row in rows)
    realized = sum(float(row[f"{prefix}Realized"]) for row in rows)
    return realized / requested if requested > 0 else 1.0


def weighted_human_food(rows):
    numerator = denominator = 0.0
    for row in rows:
        for camp in row["camps"]:
            if camp["technology"] != "AnimalHusbandry":
                continue
            people = float(camp["priorResidents"])
            numerator += people * float(camp["FoodNeedsMetRatio"])
            denominator += people
    return numerator / denominator if denominator else 1.0


def weighted_agriculture(rows, key):
    numerator = denominator = 0.0
    for row in rows:
        for camp in row["camps"]:
            if camp["technology"] != "Cultivation":
                continue
            people = float(camp["priorResidents"])
            numerator += people * float(camp[key])
            denominator += people
    return numerator / denominator if denominator else 0.0


def median_footprint_stock(row, key, stock):
    values = [float(camp[key][stock]) for camp in row["camps"]
              if camp["technology"] == "AnimalHusbandry"]
    return statistics.median(values) if values else 0.0


def load(root, variant, seed, candidate_prefix):
    prefix = "" if variant == "Baseline" else candidate_prefix
    path = root / f"{prefix}{variant}-80-{seed}.json"
    report = json.loads(path.read_text(encoding="utf-8"))
    assert report["players"] == 80 and report["seed"] == seed, path
    assert report["detailsEnabled"] and report["postOpeningTurnsMeasured"] == 80, path
    assert len(report["samples"]) >= 80, path
    assert all(row["camps"] is not None for row in report["samples"]), path
    return report


def main(root, candidate_prefix):
    reports = {(variant, seed): load(root, variant, seed, candidate_prefix)
               for variant in VARIANTS for seed in SEEDS}
    for seed in SEEDS:
        hashes = {reports[variant, seed]["configHash"] for variant in VARIANTS}
        assert len(hashes) == 1, f"Configuration mismatch for seed {seed}"
    print("| Variant | Seed | Humans final | Owned animals final | Moves all | "
          "Forage met late | Water met late | Human food met late | "
          "Animal births/deaths late | Human births/deaths late | Median late turn ms |")
    print("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |")
    for variant in VARIANTS:
        for seed in SEEDS:
            samples = reports[variant, seed]["samples"]
            late = samples[-40:]
            final = samples[-1]
            moves = sum(int(row.get("husbandryMoves", 0)) for row in samples)
            births = sum(int(row.get("animalBirths", 0)) for row in late)
            deaths = sum(int(row.get("animalDeaths", 0)) for row in late)
            human_births = sum(int(row.get("humanBirths", 0)) for row in late)
            human_deaths = sum(int(row.get("humanDeaths", 0)) for row in late)
            timing = statistics.median(float(row["totalMs"]) for row in late)
            print(f"| {variant} | {seed} | {final['population']:,} | "
                  f"{final['husbandryAnimals']:,} | {moves:,} | "
                  f"{ratio(late, 'herdForage'):.3f} | {ratio(late, 'herdWater'):.3f} | "
                  f"{weighted_human_food(late):.3f} | {births:,}/{deaths:,} | "
                  f"{human_births:,}/{human_deaths:,} | {timing:.1f} |")
    print("\nAgriculture continuity and payoff; QoL, food fulfillment, and mean q are weighted by prior residents over the late 40 turns.")
    print("| Variant | Seed | Cultivation camps final | Cultivation residents final | Cultivation moves | Food met late | QoL late | Mean q late |")
    print("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |")
    for variant in VARIANTS:
        for seed in SEEDS:
            samples = reports[variant, seed]["samples"]
            late = samples[-40:]
            final_agriculture = [c for c in samples[-1]["camps"] if c["technology"] == "Cultivation"]
            moves = sum(int(c["moved"]) for row in samples for c in row["camps"]
                        if c["technology"] == "Cultivation")
            print(f"| {variant} | {seed} | {len(final_agriculture)} | "
                  f"{sum(c['Population'] for c in final_agriculture):,} | {moves:,} | "
                  f"{weighted_agriculture(late, 'FoodNeedsMetRatio'):.3f} | "
                  f"{weighted_agriculture(late, 'QoL'):.3f} | "
                  f"{weighted_agriculture(late, 'meanFieldQ'):.3f} |")
    print("\nFinal-turn median husbandry-camp radius-one stocks; overlaps are counted per camp.")
    print("| Variant | Seed | Opening B | Ending B | Opening G | Ending G | "
          "Opening freshwater | Ending freshwater |")
    print("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |")
    for variant in VARIANTS:
        for seed in SEEDS:
            final = reports[variant, seed]["samples"][-1]
            values = [median_footprint_stock(final, key, stock)
                      for stock in ("Biomass", "Gatherable", "Freshwater")
                      for key in ("openingLocalStocks", "endingLocalStocks")]
            print(f"| {variant} | {seed} | " + " | ".join(f"{value:,.1f}" for value in values) + " |")


if __name__ == "__main__":
    if len(sys.argv) not in (2, 3):
        raise SystemExit("Usage: Summarize-MobileHusbandry.py <audit-dir> [candidate-prefix]")
    main(Path(sys.argv[1]), sys.argv[2] if len(sys.argv) == 3 else "")
