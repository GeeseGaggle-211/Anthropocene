#!/usr/bin/env python3
"""Herd-labour coupling summary (Docs/HerdLabour.md): trapped husbandry camps, animals per person and herder share, slaughter,
Animal Fabric and tents, QoL by first technology, husbandry versus cultivation population and run totals.

Runs are PerformanceAudit --details=1 --metrics=1 outputs named <tag>__<Shape><seed>.json. A husbandry camp is a camp that holds managed
animals at the final post-opening turn (the same definition as the measurement that found the trap: 44 of 106 camps with every resident
herding). A camp is trapped when every resident is a Herder and, in a QoL-mortality run, its QoL is at or below Q* = ((b0 - g) / b0)^(1/a).

usage: Summarize-HerdLabour.py <run-dir> --tag a1_f0:1 --tag hl1_f0:1 --tag hl0_f0 ...   (tag[:elasticity]; no elasticity = no mortality)
       Camp-turn observations come from post-opening turns --first..--last (default 10..50); the trap count and deciles also list the final turn.
"""
import argparse, glob, json, os, statistics
from collections import defaultdict

G, B0 = 0.01145, 0.045


def pct(v, q):
    v = sorted(v)
    return v[int(round((len(v) - 1) * q))] if v else float("nan")


def herders(camp):
    return sum(a["Population"] for a in camp.get("assignments", []) if a["Role"] == "Herder")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("dir"); ap.add_argument("--tag", action="append", required=True)
    ap.add_argument("--first", type=int, default=10); ap.add_argument("--last", type=int, default=50)
    a = ap.parse_args()
    rows = {}
    for spec in a.tag:
        tag, _, el = spec.partition(":")
        qstar = ((B0 - G) / B0) ** (1 / float(el)) if el else None
        r = defaultdict(float); lists = defaultdict(list); qt = defaultdict(list); worlds = 0
        for f in sorted(glob.glob(os.path.join(a.dir, tag + "__*.json"))):
            d = json.load(open(f)); worlds += 1
            post = [s for s in d["samples"] if s["postOpening"]]
            last = post[-1]
            r["population"] += last["population"]; r["food"] += last.get("foodYield", 0) or 0; r["camps"] += last["campCount"]
            lists["turn_ms"] += [s["totalMs"] for s in d["samples"] if s["postOpening"]]
            for c in last["camps"]:
                if c.get("managedAnimals", 0) > 0:
                    r["husbandry_camps"] += 1
                    h = herders(c)
                    if h >= c["Population"]:
                        r["all_herding"] += 1
                        if qstar is None or c["QoL"] <= qstar: r["trapped"] += 1
                    lists["final_apc"].append(c["managedAnimals"] / max(1, c["Population"])); lists["final_share"].append(h / max(1, c["Population"]))
                tech = "Husbandry" if c["technology"] == "AnimalHusbandry" else c["technology"]
                r["pop_" + tech] += c["Population"]
                r["tents"] += c.get("tentShelterPeople", 0); r["fabric_stock"] += c.get("AnimalFabric", 0)
            for i, s in enumerate(post, start=1):
                r["slaughtered"] += s.get("animalSlaughtered", 0) or 0
                fb = s.get("foodByKind", {}) or {}
                r["slaughter_food"] += fb.get("Slaughter", 0); r["animal_food"] += fb.get("Animal Product", 0)
                r["births"] += s.get("animalBirths", 0) or 0
                if i < a.first or i > a.last: continue
                for c in s["camps"]:
                    qt[c["technology"]].append(c["QoL"])
                    pr = c.get("production", {}) or {}
                    r["fabric_made"] += pr.get("AnimalFabric", 0); r["light_housing_made"] += pr.get("LightHousing", 0)
                    if c.get("managedAnimals", 0) > 0:
                        lists["apc"].append(c["managedAnimals"] / max(1, c["Population"])); lists["share"].append(herders(c) / max(1, c["Population"]))
        rows[tag] = (worlds, r, lists, qt, qstar)
    print("| run | worlds | population | food yield (turn 60) | husbandry camps | all residents herding | trapped | slaughtered animals | slaughter food share of animal food | median turn ms |")
    print("|---|---|---|---|---|---|---|---|---|---|")
    for tag, (w, r, l, q, qs) in rows.items():
        share = r["slaughter_food"] / (r["slaughter_food"] + r["animal_food"]) if r["slaughter_food"] + r["animal_food"] > 0 else 0
        print("| %s | %d | %d | %.3g | %d | %d | %d | %d | %.1f%% | %.0f |" % (tag, w, r["population"], r["food"], r["husbandry_camps"], r["all_herding"], r["trapped"], r["slaughtered"], 100 * share, statistics.median(l["turn_ms"]) if l["turn_ms"] else 0))
    print("\nAnimals per person (P10/P25/median/P75/P90) of husbandry camps: final turn | camp-turns %d..%d" % (a.first, a.last))
    print("| run | final turn (n) | camp-turns (n) |"); print("|---|---|---|")
    for tag, (w, r, l, q, qs) in rows.items():
        f = lambda v: "/".join("%.2f" % pct(v, p) for p in (.1, .25, .5, .75, .9)) + " (%d)" % len(v)
        print("| %s | %s | %s |" % (tag, f(l["final_apc"]), f(l["apc"])))
    print("\nHerder share of residents (P10/P25/median/P75/P90) of husbandry camps")
    print("| run | final turn (n) | camp-turns (n) |"); print("|---|---|---|")
    for tag, (w, r, l, q, qs) in rows.items():
        f = lambda v: "/".join("%.2f" % pct(v, p) for p in (.1, .25, .5, .75, .9)) + " (%d)" % len(v)
        print("| %s | %s | %s |" % (tag, f(l["final_share"]), f(l["share"])))
    print("\nAnimal Fabric and tents: Animal Fabric and Light Housing made (camp-turns %d..%d), final Animal Fabric stock, people sheltered in tents (final turn)" % (a.first, a.last))
    print("| run | Animal Fabric made | Light Housing made | Animal Fabric stock | tent shelter people |"); print("|---|---|---|---|---|")
    for tag, (w, r, l, q, qs) in rows.items():
        print("| %s | %.0f | %.0f | %.0f | %.0f |" % (tag, r["fabric_made"], r["light_housing_made"], r["fabric_stock"], r["tents"]))
    print("\nFinal-turn residents by first technology (husbandry versus cultivation)")
    techs = sorted({k[4:] for _, (w, r, l, q, qs) in rows.items() for k in r if k.startswith("pop_")})
    print("| run | " + " | ".join(techs) + " |"); print("|---|" + "---|" * len(techs))
    for tag, (w, r, l, q, qs) in rows.items():
        print("| %s | " % tag + " | ".join("%d" % r.get("pop_" + t, 0) for t in techs) + " |")
    names = sorted({t for _, (w, r, l, q, qs) in rows.items() for t in q})
    print("\nQoL P25/median/P75 by first technology (n camp-turns), post-opening turns %d..%d" % (a.first, a.last))
    print("| run | " + " | ".join(names) + " |"); print("|---|" + "---|" * len(names))
    for tag, (w, r, l, q, qs) in rows.items():
        print("| %s | " % tag + " | ".join(("%.2f/%.2f/%.2f (%d)" % (pct(q[t], .25), pct(q[t], .5), pct(q[t], .75), len(q[t]))) if q.get(t) else "n/a" for t in names) + " |")


main()
