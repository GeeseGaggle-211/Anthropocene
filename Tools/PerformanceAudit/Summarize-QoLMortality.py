#!/usr/bin/env python3
"""Population, food and growth-blocking summary for QoL-dependent mortality runs (Docs/QoLMortality.md).

Runs are PerformanceAudit --details=1 --metrics=1 outputs named <tag>__<Shape><seed>.json. Per tag, pooled over worlds: final population,
camps, the final turn's total food yield (the AUDIT line's `food`), fissions, human births, the share of post-opening Neolithic (goods-economy) camp-turns whose QoL is at or below Q* = ((b0 - g) / b0)^(1/a) (growth blocked), and
the camp-turn QoL P25/median/P75 by first technology.

usage: Summarize-QoLMortality.py <run-dir> --tag head_f1 --tag a2_f1:2:0.045 ...   (tag[:elasticity:birthRate]; no parameters = no mortality)
"""
import argparse, glob, json, os, re, statistics
from collections import defaultdict

G = 0.01145


def pct(v, q):
    v = sorted(v)
    return v[int(round((len(v) - 1) * q))] if v else float("nan")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("dir"); ap.add_argument("--tag", action="append", required=True)
    ap.add_argument("--first", type=int, default=10); ap.add_argument("--last", type=int, default=50)
    a = ap.parse_args()
    print("| run | worlds | population | camps | food yield (turn 60) | fissions | human births | camp-turns blocked (Q <= Q*) | Q* |")
    print("|---|---|---|---|---|---|---|---|---|")
    techs = {}
    for spec in a.tag:
        parts = spec.split(":"); tag = parts[0]
        qstar = ((float(parts[2]) - G) / float(parts[2])) ** (1 / float(parts[1])) if len(parts) == 3 else None
        pop = camps = fis = births = 0; fy = 0.0; n = blocked = 0; worlds = 0; qt = defaultdict(list)
        for f in sorted(glob.glob(os.path.join(a.dir, tag + "__*.json"))):
            d = json.load(open(f)); worlds += 1
            post = [s for s in d["samples"] if s["postOpening"]]
            last = post[-1]; pop += last["population"]; camps += last["campCount"]; fy += last.get("foodYield", 0) or 0
            fis += sum(s["fissionCount"] for s in d["samples"]); births += sum(s.get("humanBirths", 0) or 0 for s in d["samples"])
            for i, s in enumerate(post, start=1):
                if i < a.first or i > a.last: continue
                for c in s["camps"]:
                    qt[c["technology"]].append(c["QoL"])
                    if c["technology"] != "None" and (c["holdsCultivation"] or c["holdsHusbandry"]):
                        n += 1
                        if qstar is not None and c["QoL"] <= qstar: blocked += 1
        techs[tag] = qt
        share = "n/a" if qstar is None else "%.1f%% (%d/%d)" % (100 * blocked / max(1, n), blocked, n)
        print("| %s | %d | %d | %d | %.3g | %d | %d | %s | %s |" % (tag, worlds, pop, camps, fy, fis, births, share, "-" if qstar is None else "%.3f" % qstar))
    names = sorted({t for q in techs.values() for t in q})
    print("\nQoL P25/median/P75 by first technology (n camp-turns), post-opening turns %d..%d" % (a.first, a.last))
    print("| run | " + " | ".join(names) + " |"); print("|---|" + "---|" * len(names))
    for tag, q in techs.items():
        print("| %s | " % tag + " | ".join(("%.2f/%.2f/%.2f (%d)" % (pct(q[t], .25), pct(q[t], .5), pct(q[t], .75), len(q[t]))) if q.get(t) else "n/a" for t in names) + " |")


main()
