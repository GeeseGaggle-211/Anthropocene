#!/usr/bin/env python3
"""Hinterland / relative-destination summary (Docs/Fission.md, Docs/ContactKnowledge.md): the fission funnel, outcomes, QoL and knowledge cost.

Runs are PerformanceAudit --details=1 --metrics=1 outputs named <tag>__<Shape><seed>.json. A tag may be given as tag=directory when the
runs live elsewhere (for example a baseline in another checkout). Pooled over worlds, post-opening turns FIRST..LAST (default 10..50 of the
post-opening phase, matching Summarize-Cohesion.py; funnel counters are summed over every post-opening turn):

  funnel   evaluated camps, screened (past the gain screen), searches with no legal candidate, searches with candidates but no destination,
           camps with a destination, then below threshold / vetoed (daughter) / vetoed (stayers) / proposals, dropped, committed
  outcome  fissions, camps and population at the last turn, median total turn ms
  QoL      P25/median/P75 of camp QoL by first technology (Cultivation, AnimalHusbandry)
  memory   at the last turn: remembering Societies, explored tiles and bytes per Society (ContactKnowledge.Measure), and the median
           per-turn knowledge and fission milliseconds

usage: Summarize-Hinterland.py <run-dir> --tag head_f1 --tag new=other/dir [--first 10] [--last 50]
Baseline runs made before the funnel counters carry zeros for them: use runs from a build with the counters (behaviour identical to the baseline).
"""
import argparse, glob, json, os, statistics


def pct(v, q):
    v = sorted(v)
    return v[int(round((len(v) - 1) * q))] if v else float("nan")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("dir"); ap.add_argument("--tag", action="append", required=True)
    ap.add_argument("--first", type=int, default=10); ap.add_argument("--last", type=int, default=50)
    a = ap.parse_args()
    keys = ["fissionCampsEvaluated", "fissionScreened", "fissionSearchesNoCandidates", "fissionSearchesNoViable", "fissionWithDestination", "fissionBelowThreshold", "fissionDaughterBlocked", "fissionStayersBlocked", "fissionProposals", "fissionDropped", "fissionCommits"]
    names = ["evaluated", "screened", "search: no candidate", "search: none viable", "with destination", "below threshold", "daughter veto", "stayers veto", "proposals", "dropped", "committed"]
    rows = {}; qol = {}; memory = {}
    for spec in a.tag:
        tag, _, d = spec.partition("=") if "=" in spec else (spec, "", a.dir)
        totals = dict.fromkeys(keys, 0); fis = camps = pop = worlds = 0; tms = []; kms = []; fms = []; q = {"Cultivation": [], "AnimalHusbandry": []}
        societies = tiles = bytes_ = 0
        for f in sorted(glob.glob(os.path.join(d or a.dir, tag + "__*.json"))):
            run = json.load(open(f)); worlds += 1; post = [s for s in run["samples"] if s["postOpening"]]
            camps += post[-1]["campCount"]; pop += post[-1]["population"]
            k = post[-1].get("knowledge") or {}
            societies += k.get("societies", 0); tiles += k.get("tiles", 0); bytes_ += k.get("bytes", 0)
            for i, s in enumerate(post, start=1):
                for key in keys: totals[key] += s.get(key, 0) or 0
                fis += s["fissionCount"]; tms.append(s["totalMs"]); kms.append(s.get("knowledgeMs", 0) or 0); fms.append(s.get("fissionMs", 0) or 0)
                if a.first <= i <= a.last:
                    for c in s["camps"]:
                        if c["technology"] in q: q[c["technology"]].append(c["QoL"])
        rows[tag] = (worlds, totals, fis, camps, pop, statistics.median(tms) if tms else 0)
        qol[tag] = q; memory[tag] = (societies, tiles, bytes_, statistics.median(kms) if kms else 0, statistics.median(fms) if fms else 0)
    tags = [t.partition("=")[0] for t in a.tag]
    print("Fission funnel, post-opening turns, pooled over worlds\n")
    print("| stage | " + " | ".join(tags) + " |"); print("|---|" + "---:|" * len(tags))
    for key, name in zip(keys, names): print("| %s | %s |" % (name, " | ".join("{:,}".format(int(rows[t][1][key])) for t in tags)))
    print("\n| run | worlds | fissions | camps (last turn) | population (last turn) | turn ms (median) |"); print("|---|---|---|---|---|---|")
    for t in tags: w, _, fis, camps, pop, ms = rows[t]; print("| %s | %d | %d | %d | {:,} | %.0f |".replace("{:,}", "%s") % (t, w, fis, camps, "{:,}".format(pop), ms))
    print("\nQoL P25/median/P75 (n camp-turns) by first technology\n")
    print("| run | agricultural | husbandry |"); print("|---|---|---|")
    cell = lambda v: "%.2f/%.2f/%.2f (%d)" % (pct(v, .25), pct(v, .5), pct(v, .75), len(v)) if v else "n/a"
    for t in tags: print("| %s | %s | %s |" % (t, cell(qol[t]["Cultivation"]), cell(qol[t]["AnimalHusbandry"])))
    print("\nRemembered knowledge at the last turn (ContactKnowledge.Measure, summed over worlds) and per-turn cost\n")
    print("| run | Societies | explored tiles | tiles per Society | bytes per Society | knowledge ms/turn (median) | fission ms/turn (median) |"); print("|---|---|---|---|---|---|---|")
    for t in tags:
        s, tl, b, km, fm = memory[t]; print("| %s | {:,} | {:,} | %.0f | %.0f | %.2f | %.2f |".replace("{:,}", "%s") % (t, "{:,}".format(s), "{:,}".format(tl), tl / max(1, s), b / max(1, s), km, fm))


if __name__ == "__main__":
    main()
