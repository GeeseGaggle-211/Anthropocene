#!/usr/bin/env python3
"""Distress-fission summary (Docs/Fission.md, "Distress fission"): fissions by trigger, distress daughter sizes, starvation, QoL and totals.

Runs are PerformanceAudit --details=1 --metrics=1 outputs named <tag>__<Shape><seed>.json. Per tag, pooled over worlds:
  fissions by trigger (a run without the `trigger` field, i.e. a baseline from before the change, counts every split as Crowding),
  distress daughter size (P25/median/P75) and share of the parent, proposals by trigger when the metrics are present,
  human deaths over the run (the resolver's starvation deaths), post-opening camp-turns (turns FIRST..LAST) with DeficitTurns > 0,
  QoL P25/median/P75 for agricultural (first technology Cultivation) and husbandry (AnimalHusbandry) camps pooled, and the share of
  goal-economy camp-turns at or below Q* (0.746, the growth-blocking QoL at elasticity 1),
  final population and food yield, camps at the last measured turn, and the median total turn time.

usage: Summarize-DistressFission.py <run-dir> --tag head_f1 --tag d_f1 ... [--first 10] [--last 50] [--qstar 0.746]
"""
import argparse, glob, json, os, statistics


def pct(v, q):
    v = sorted(v)
    return v[int(round((len(v) - 1) * q))] if v else float("nan")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("dir"); ap.add_argument("--tag", action="append", required=True)
    ap.add_argument("--first", type=int, default=10); ap.add_argument("--last", type=int, default=50); ap.add_argument("--qstar", type=float, default=0.746)
    a = ap.parse_args()
    print("| run | worlds | fissions | crowding | distress | distress size P25/med/P75 | distress share of parent (median) | human deaths | camp-turns with DeficitTurns>0 | population | food yield | camps (last turn) | turn ms (median) |")
    print("|---|---|---|---|---|---|---|---|---|---|---|---|---|")
    qol = {}
    for tag in a.tag:
        worlds = fis = crowd = dist = deaths = deficit = turns = pop = camps = 0; fy = 0.0; sizes = []; shares = []; tms = []
        q = {"Cultivation": [], "AnimalHusbandry": []}; blocked = economy = 0
        for f in sorted(glob.glob(os.path.join(a.dir, tag + "__*.json"))):
            d = json.load(open(f)); worlds += 1; post = [s for s in d["samples"] if s["postOpening"]]
            pop += post[-1]["population"]; camps += post[-1]["campCount"]; fy += post[-1].get("foodYield", 0) or 0
            for s in d["samples"]:
                deaths += s.get("humanDeaths", 0) or 0; tms.append(s["totalMs"])
                for e in s["fissionEvents"] or []:
                    fis += 1
                    if e.get("trigger", "Crowding") == "Distress":
                        dist += 1; sizes.append(e["Daughter"]); shares.append(e["Daughter"] / e["ParentBefore"])
                    else: crowd += 1
            for i, s in enumerate(post, start=1):
                if i < a.first or i > a.last: continue
                for c in s["camps"]:
                    turns += 1; deficit += 1 if c.get("DeficitTurns", 0) > 0 else 0
                    if c["technology"] in q: q[c["technology"]].append(c["QoL"])
                    if c["technology"] != "None" and (c["holdsCultivation"] or c["holdsHusbandry"]):
                        economy += 1; blocked += 1 if c["QoL"] <= a.qstar else 0
        qol[tag] = (q, blocked, economy)
        ds = "n/a" if not sizes else "%d/%d/%d" % (pct(sizes, .25), pct(sizes, .5), pct(sizes, .75))
        sh = "n/a" if not shares else "%.2f" % statistics.median(shares)
        print("| %s | %d | %d | %d | %d | %s | %s | %d | %.1f%% (%d/%d) | %d | %.3g | %d | %.0f |" % (tag, worlds, fis, crowd, dist, ds, sh, deaths, 100 * deficit / max(1, turns), deficit, turns, pop, fy, camps, statistics.median(tms) if tms else 0))
    print("\nQoL P25/median/P75 (n camp-turns), post-opening turns %d..%d; share of goods-economy camp-turns at or below Q* = %.3f" % (a.first, a.last, a.qstar))
    print("| run | agricultural | husbandry | agricultural + husbandry pooled | share <= Q* |"); print("|---|---|---|---|---|")
    for tag, (q, blocked, economy) in qol.items():
        pooled = q["Cultivation"] + q["AnimalHusbandry"]
        cell = lambda v: "%.2f/%.2f/%.2f (%d)" % (pct(v, .25), pct(v, .5), pct(v, .75), len(v)) if v else "n/a"
        print("| %s | %s | %s | %s | %.1f%% (%d/%d) |" % (tag, cell(q["Cultivation"]), cell(q["AnimalHusbandry"]), cell(pooled), 100 * blocked / max(1, economy), blocked, economy))


main()
