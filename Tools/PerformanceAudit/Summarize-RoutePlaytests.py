"""Summarize final settlement/herd distributions from detailed Standard playtests.

Usage: python Tools/PerformanceAudit/Summarize-RoutePlaytests.py <audit-dir> <profile>
Reads <profile>-80-{11,23,1729}.json and prints Markdown tables.
"""

import collections
import json
import math
import pathlib
import sys


SEEDS = (11, 23, 1729)
ROUTES = ("Cultivation", "AnimalHusbandry")


def percentile(values, fraction):
    if not values:
        return None
    ordered = sorted(values)
    rank = (len(ordered) - 1) * fraction
    lower = math.floor(rank)
    upper = math.ceil(rank)
    return ordered[lower] + (ordered[upper] - ordered[lower]) * (rank - lower)


def fmt(value):
    return "—" if value is None else f"{value:,.1f}"


def quartiles(values):
    return " | ".join(fmt(percentile(values, p)) for p in (.25, .5, .75))


def category(camp):
    return camp["technology"], "Village" if camp["IsVillage"] else "Camp"


def read_reports(root, profile):
    reports = {}
    for seed in SEEDS:
        path = root / f"{profile}-80-{seed}.json"
        report = json.loads(path.read_text(encoding="utf-8"))
        assert report["seed"] == seed and report["players"] == 80, path
        assert report["postOpeningTurnsMeasured"] == 80 and report["detailsEnabled"], path
        assert report["productionConfig"], path
        final = report["samples"][-1]
        assert "IsVillage" in final["camps"][0] and final["domesticHerds"] is not None, path
        reports[seed] = report
    assert len({r["configHash"] for r in reports.values()}) == 1
    return reports


def population_tables(reports):
    print("## Final settlement residents\n")
    print("| Seed | Route | Type | N | p25 | Median | p75 |")
    print("| --- | --- | --- | ---: | ---: | ---: | ---: |")
    for seed in (*SEEDS, "Pooled"):
        camps = [c for r in (reports.values() if seed == "Pooled" else (reports[seed],))
                 for c in r["samples"][-1]["camps"]]
        for route in ROUTES:
            for kind in ("Camp", "Village"):
                values = [c["Population"] for c in camps if category(c) == (route, kind)]
                print(f"| {seed} | {route} | {kind} | {len(values)} | {quartiles(values)} |")
    print()

    print("## Final domestic herd animals\n")
    print("| Seed | Parent route | Parent type | Species | Live herds | Extinct records | p25 | Median | p75 |")
    print("| --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: |")
    for seed in (*SEEDS, "Pooled"):
        entries = []
        for report in (reports.values() if seed == "Pooled" else (reports[seed],)):
            final = report["samples"][-1]
            parents = {c["Id"]: category(c) for c in final["camps"]}
            for herd in final["domesticHerds"]:
                assert herd["DomesticCamp"] in parents
                entries.append((parents[herd["DomesticCamp"]], herd))
        keys = sorted({(parent[0], parent[1], herd["Species"]) for parent, herd in entries})
        for route, kind, species in keys:
            matching = [h["Animals"] for parent, h in entries
                        if parent == (route, kind) and h["Species"] == species]
            live = [count for count in matching if count > 0]
            print(f"| {seed} | {route} | {kind} | {species} | {len(live)} | "
                  f"{len(matching) - len(live)} | {quartiles(live)} |")
    print()


def specialty_tables(reports):
    final_camps = [c for r in reports.values() for c in r["samples"][-1]["camps"]]
    print("## Final workforce specialties, pooled across three worlds\n")
    print("Gatherers are unassigned residents; every other row is an explicit workforce assignment.\n")
    print("| Route | Type | Role | Workers | Share of residents | Settlements assigning role |")
    print("| --- | --- | --- | ---: | ---: | ---: |")
    for route in ROUTES:
        for kind in ("Camp", "Village"):
            camps = [c for c in final_camps if category(c) == (route, kind)]
            residents = sum(c["Population"] for c in camps)
            workers = collections.Counter()
            sites = collections.Counter()
            for camp in camps:
                assigned = 0
                for assignment in camp["assignments"]:
                    role, count = assignment["Role"], assignment["Population"]
                    assert count >= 0
                    workers[role] += count
                    assigned += count
                    if count:
                        sites[role] += 1
                assert assigned <= camp["Population"]
                gatherers = camp["Population"] - assigned
                workers["Gatherer"] += gatherers
                if gatherers:
                    sites["Gatherer"] += 1
            assert sum(workers.values()) == residents
            for role, count in sorted(workers.items(), key=lambda item: (-item[1], item[0])):
                if count:
                    print(f"| {route} | {kind} | {role} | {count:,} | "
                          f"{100 * count / residents:.1f}% | {sites[role]}/{len(camps)} |")
    print()

    print("## Realized manufactured output, last ten turns pooled\n")
    print("| Parent route | Parent type | Good | Units | Producing settlements |")
    print("| --- | --- | --- | ---: | ---: |")
    totals = collections.Counter()
    producing = collections.defaultdict(set)
    for seed, report in reports.items():
        final_category = {c["Id"]: category(c) for c in report["samples"][-1]["camps"]}
        for sample in report["samples"][-10:]:
            for camp in sample["camps"]:
                route, kind = final_category[camp["Id"]]
                if route not in ROUTES:
                    continue
                for good, amount in camp["production"].items():
                    if amount > 0:
                        key = route, kind, good
                        totals[key] += amount
                        producing[key].add((seed, camp["Id"]))
    for key in sorted(totals):
        route, kind, good = key
        print(f"| {route} | {kind} | {good} | {totals[key]:,.1f} | {len(producing[key])} |")
    print()


def health_table(reports):
    print("## Last-forty-turn route health\n")
    print("Food, QoL, and field q are weighted by prior residents.\n")
    print("| Seed | Route | Mean residents per final settlement | Food met | QoL | Field q |")
    print("| --- | --- | ---: | ---: | ---: | ---: |")
    for seed, report in reports.items():
        late = report["samples"][-40:]
        for route in ROUTES:
            camps = [c for c in late[-1]["camps"] if c["technology"] == route]
            weighted = [(c["priorResidents"], c["FoodNeedsMetRatio"], c["QoL"], c["meanFieldQ"])
                        for row in late for c in row["camps"] if c["technology"] == route]
            denom = sum(w for w, *_ in weighted)
            food = sum(w * f for w, f, _, _ in weighted) / denom if denom else 0
            qol = sum(w * q for w, _, q, _ in weighted) / denom if denom else 0
            field = sum(w * q for w, _, _, q in weighted) / denom if denom else 0
            mean_pop = sum(c["Population"] for c in camps) / len(camps) if camps else 0
            print(f"| {seed} | {route} | {mean_pop:,.1f} | {food:.3f} | {qol:.3f} | "
                  f"{field:.3f} |")
    print("\nAll domestic herds across the world, last 40 turns:\n")
    print("| Seed | Final animals | Forage met | Water met | Births | Starvation deaths |")
    print("| --- | ---: | ---: | ---: | ---: | ---: |")
    for seed, report in reports.items():
        late = report["samples"][-40:]
        def ratio(pool):
            requested = sum(row[f"herd{pool}Requested"] for row in late)
            realized = sum(row[f"herd{pool}Realized"] for row in late)
            return realized / requested if requested else 1
        print(f"| {seed} | {late[-1]['managedAnimals']:,} | {ratio('Forage'):.3f} | "
              f"{ratio('Water'):.3f} | {sum(row['animalBirths'] for row in late):,} | "
              f"{sum(row['animalDeaths'] for row in late):,} |")
    print()


def main():
    if len(sys.argv) != 3:
        raise SystemExit(__doc__)
    root, profile = pathlib.Path(sys.argv[1]), sys.argv[2]
    reports = read_reports(root, profile)
    first = reports[SEEDS[0]]
    print(f"# Route playtests: {profile}\n")
    print(f"Worlds: Standard/Continents, seeds {', '.join(map(str, SEEDS))}, 80 tribes, "
          f"80 post-opening turns, {first['turnDays'] / 365:g}-year turns. "
          f"Configuration hash: `{first['configHash']}`.\n")
    population_tables(reports)
    specialty_tables(reports)
    health_table(reports)


if __name__ == "__main__":
    main()
