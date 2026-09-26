"""Validate and export the long-run cultivation population/food-bucket sweep."""

import argparse
import csv
from pathlib import Path


SITES = ("open-fertile", "medium-woodland", "dry-marginal")
BUCKET_TURNS = {
    "open-fertile": (40, 160, 400, 520, 600),
    "medium-woodland": (65, 75, 160, 200, 287, 400, 600),
    "dry-marginal": (40, 160, 386, 397, 400, 520, 560, 600),
}


def read(path):
    with path.open(newline="", encoding="utf-8") as stream:
        return list(csv.DictReader(stream))


def write(path, rows):
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)


def number(row, field):
    return float(row[field])


def window_rows(by_site):
    result = []
    for site in SITES:
        rows = by_site[site]
        for first in range(1, 601, 40):
            span = rows[first - 1:first + 39]
            births = sum(number(row, "births") for row in span)
            deaths = sum(number(row, "deaths") for row in span)
            result.append({
                "site": site, "firstTurn": first, "lastTurn": first + 39,
                "startingPeople": span[0]["startPopulation"],
                "endingPeople": span[-1]["endPopulation"],
                "netGrowth": births - deaths, "births": births, "deaths": deaths,
                "startingQ": span[0]["openingQ"], "endingQ": span[-1]["q"],
                "minimumFoodSatisfaction": min(number(row, "foodSatisfaction") for row in span),
                "startingReserve": span[0]["openingReserves"],
                "endingReserve": span[-1]["reserveEnd"],
                "endingG": span[-1]["endingG"], "endingB": span[-1]["endingB"],
                "meanFoodReceived": sum(number(row, "foodReceived") for row in span) / 40,
                "meanBasicNeed": sum(number(row, "basicFoodNeed") for row in span) / 40,
                "meanManufacturingFood": sum(number(row, "manufacturingFood") for row in span) / 40,
                "growthFoodSpent": sum(number(row, "growthFoodSpent") for row in span),
            })
    return result


def bucket_rows(by_site):
    result = []
    for site in SITES:
        rows = by_site[site]
        for turn in BUCKET_TURNS[site]:
            row = rows[turn - 1]
            food_met = number(row, "foodSatisfaction")
            growth = number(row, "growthFoodSpent")
            reserve = number(row, "reserveEnd")
            goal = number(row, "reserveGoal")
            stage = ("consumption shortfall" if food_met < 1 - 1e-8 else
                     "growth funded" if growth > 1e-8 else
                     "reserve shortfall" if reserve < goal - 1e-7 else
                     "reserve met; no growth")
            result.append({
                "site": site, "turn": turn, "stage": stage,
                "startingPeople": row["startPopulation"],
                "endingPeople": row["endPopulation"], "q": row["q"],
                "foodReceived": row["foodReceived"],
                "priorCohortConsumptionNeed": row["basicFoodNeed"],
                "manufacturingFood": row["manufacturingFood"],
                "openingReserve": row["openingReserves"],
                "priorCohortReserveGoal": row["reserveGoal"],
                "endingReserve": row["reserveEnd"],
                "growthFoodSpent": row["growthFoodSpent"],
                "foodSatisfaction": row["foodSatisfaction"],
                "births": row["births"], "deaths": row["deaths"],
                "endingG": row["endingG"], "endingB": row["endingB"],
            })
    return result


def effort_rows(by_site, footprints):
    result = []
    for site in SITES:
        tiles = [row for row in footprints if row["site"] == site]
        if len(tiles) != 7:
            raise ValueError(f"Expected seven opening field tiles at {site}")
        factors = [number(row, "openingBuildFactor") for row in tiles]
        if any(not any(abs(factor - expected) < 1e-9 for expected in (1, 1.3, 1.5))
               for factor in factors):
            raise ValueError(f"Unexpected opening build factor at {site}")
        for turn in (40, 160):
            span = by_site[site][:turn]
            result.append({
                "site": site, "turn": turn,
                "endingPeople": span[-1]["endPopulation"],
                "meanQ": span[-1]["q"], "meanCapitalE": span[-1]["endingE"],
                "assignedFarmerYears": sum(number(row, "assignedFarmerYears") for row in span),
                "assignedExpansionYears": sum(number(row, "assignedExpansionYears") for row in span),
                "requestedBridgeYears": sum(number(row, "requestedBridgeYears") for row in span),
                "openingOpenCostTiles": sum(abs(x - 1) < 1e-9 for x in factors),
                "openingWoodlandCostTiles": sum(abs(x - 1.3) < 1e-9 for x in factors),
                "openingForestCostTiles": sum(abs(x - 1.5) < 1e-9 for x in factors),
            })
    return result


def flux_rows(raw_root, by_site):
    paths = (("0", "M30-LandR003-Growth160-Flux0"),
             ("0.65", None),
             ("1.30", "M30-LandR003-Growth160-Flux130"))
    result = []
    for flux, directory in paths:
        if directory is None:
            source = by_site
        else:
            entries = read(raw_root / directory / "turns.csv")
            source = {site: sorted((row for row in entries if row["mode"] == "live"
                                    and row["site"] == site
                                    and row["initialPopulation"] == "24"),
                                   key=lambda row: int(row["turn"])) for site in SITES}
        for site in SITES:
            if len(source[site]) < 160:
                raise ValueError(f"Incomplete flux control: {flux} {site}")
            for turn in (40, 80, 160):
                row = source[site][turn - 1]
                result.append({"site": site, "flux": flux, "turn": turn,
                               "population": row["endPopulation"], "q": row["q"],
                               "endingG": row["endingG"], "endingB": row["endingB"],
                               "foodSatisfaction": row["foodSatisfaction"]})
    for site in SITES:
        ordered = sorted((row for row in result if row["site"] == site and row["turn"] == 160),
                         key=lambda row: number(row, "q"))
        if any(number(b, "population") < number(a, "population") for a, b in zip(ordered, ordered[1:])):
            raise ValueError(f"Population did not increase with observed q in {site}")
    return result


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--raw-root", type=Path,
                        default=Path("TestResults/LandFoodPopulationSweep"))
    parser.add_argument("--out-dir", type=Path, default=Path("Docs"))
    args = parser.parse_args()
    rows = read(args.raw_root / "M30-LandR003-Growth600-Buckets" / "turns.csv")
    by_site = {site: sorted((row for row in rows if row["mode"] == "live"
                            and row["site"] == site and row["initialPopulation"] == "24"),
                           key=lambda row: int(row["turn"])) for site in SITES}
    for site, series in by_site.items():
        if [int(row["turn"]) for row in series] != list(range(1, 601)):
            raise ValueError(f"Missing turns for {site}")
        for row in series:
            starting = number(row, "openingReserves")
            balance = (starting + number(row, "foodReceived")
                       - number(row, "foodConsumed")
                       - number(row, "manufacturingFood")
                       - number(row, "discardedFood")
                       - number(row, "reserveEnd"))
            if abs(balance) > 1e-5 or number(row, "largestCapacityExcess") > 1e-6:
                raise ValueError(f"Food or capacity imbalance at {site} turn {row['turn']}")
            if number(row, "growthFoodSpent") > 1e-6 and number(row, "foodSatisfaction") < 1 - 1e-8:
                raise ValueError(f"Growth preceded cohort consumption at {site} turn {row['turn']}")
            if (number(row, "assignedExpansionYears") < -1e-8
                    or number(row, "assignedFarmerYears") + 1e-8 < number(row, "assignedExpansionYears")
                    or number(row, "requestedBridgeYears") < -1e-8):
                raise ValueError(f"Invalid field-work assignment at {site} turn {row['turn']}")
            # Goods production may spend reserve food after the demographic
            # decision. Restore that debit to test the reserve at the barrier
            # where growth was funded.
            if (number(row, "growthFoodSpent") > 1e-6
                    and number(row, "reserveEnd") + number(row, "manufacturingFood")
                    + number(row, "discardedFood") < number(row, "reserveGoal") - 1e-6):
                raise ValueError(f"Growth preceded reserve funding at {site} turn {row['turn']}")
        early = number(series[39], "endPopulation") - number(series[0], "startPopulation")
        late = number(series[-1], "endPopulation") - number(series[-40], "startPopulation")
        if early <= 0 or late > max(5, .05 * number(series[-40], "startPopulation")):
            raise ValueError(f"No late population-growth taper at {site}")
        peak = max(number(row, "endPopulation") for row in series)
        if peak - number(series[-1], "endPopulation") > .02 * peak:
            raise ValueError(f"Population fell more than 2% from its observed peak at {site}")
        if min(number(row, "foodSatisfaction") for row in series[-40:]) < .98:
            raise ValueError(f"Late food satisfaction collapsed at {site}")
    write(args.out_dir / "LandFoodGrowthWindows.csv", window_rows(by_site))
    write(args.out_dir / "LandFoodGrowthBuckets.csv", bucket_rows(by_site))
    write(args.out_dir / "LandFoodGrowthEffort.csv",
          effort_rows(by_site, read(args.raw_root / "M30-LandR003-Growth600-Buckets" / "footprints.csv")))
    write(args.out_dir / "LandFoodGrowthFlux.csv", flux_rows(args.raw_root, by_site))
    print("PASS: 3 complete 600-turn curves, food balances, ordered buckets, field effort, late taper, and flux controls")


if __name__ == "__main__":
    main()
