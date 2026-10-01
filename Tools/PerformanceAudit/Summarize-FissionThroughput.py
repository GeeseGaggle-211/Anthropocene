#!/usr/bin/env python3
"""Reduce PerformanceAudit --details=1 --metrics=1 runs to the fission-throughput tables (Docs/Fission.md, "Throughput").

Per tag (runs named <tag>__<Shape><seed>.json, pooled over worlds):
  fissions, daughter-check evaluations and blocks, destination search counts and Close-phase fission time;
  camp-size P90/P95 by actual housing class (Summarize-Cohesion.py's classes and targets 100 / 250 / 600-1,000);
  the split rate of camp-turns at 1.0-1.5x and 2.0-3.0x their closed-form Neutral onset N* = 0.4581 N_s,
  N_s = N_band (1 + k H) from the camp's reported housing share H (a camp that split is measured at its size before the split);
  mean QoL, final population and camps, and median total turn time.
Counters cover every turn; size, split-rate and QoL rows pool post-opening turns FIRST..LAST (default 10..50), as in Summarize-Cohesion.py.

usage: Summarize-FissionThroughput.py <run-dir> [--tag-order a,b,c] [--first 10] [--last 50] [--band-scale 220] [--gain 7.1]
"""
import argparse, glob, importlib.util, json, os, statistics
from collections import defaultdict

here = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("cohesion", os.path.join(here, "Summarize-Cohesion.py"))
cohesion = importlib.util.module_from_spec(spec)
spec.loader.exec_module(cohesion)

NEUTRAL_ONSET = 0.4581  # sqrt(ln(1 + G*) / (1 - (1 - f)^2)) at G* = 0.113, f = 0.30 (Docs/SocialCohesion.md)
BANDS = [("0.5-1.0x", .5, 1.0), ("1.0-1.5x", 1.0, 1.5), ("1.5-2.0x", 1.5, 2.0), ("2.0-3.0x", 2.0, 3.0), (">3x", 3.0, 1e9)]


def load(path, first, last, band_scale, gain):
    d = json.load(open(path))
    post = [s for s in d["samples"] if s["postOpening"]]
    out = {"fissions": 0, "checks": 0, "blocked": 0, "evaluated": 0, "searches": 0, "candidates": 0, "proposals": 0, "dropped": 0,
           "fission_ms": [], "turn_ms": [], "bands": defaultdict(lambda: [0, 0]), "qol": [], "pop": post[-1]["population"], "camps": post[-1]["campCount"]}
    for s in d["samples"]:
        # Counters cover every turn (opening and post-opening); size, split-rate and QoL rows use the FIRST..LAST window.
        out["fissions"] += s["fissionCount"]
        for k, m in (("checks", "fissionDaughterChecks"), ("blocked", "fissionDaughterBlocked"), ("evaluated", "fissionCampsEvaluated"),
                     ("searches", "fissionSearches"), ("candidates", "fissionCandidates"), ("proposals", "fissionProposals"), ("dropped", "fissionDropped")):
            out[k] += s.get(m, 0) or 0
        out["fission_ms"].append(s.get("fissionMs", 0) or 0)
    for i, s in enumerate(post, start=1):
        out["turn_ms"].append(s["totalMs"])
        if i < first or i > last:
            continue
        before = {e["ParentCamp"]: e["ParentBefore"] for e in s.get("fissionEvents") or []}
        for c in s.get("camps") or []:
            pop = before.get(c["Id"], c["Population"])
            onset = NEUTRAL_ONSET * band_scale * (1 + gain * c["housingShare"])
            ratio = pop / onset
            for name, lo, hi in BANDS:
                if lo <= ratio < hi:
                    out["bands"][name][0] += 1
                    out["bands"][name][1] += 1 if c["Id"] in before else 0
            out["qol"].append(c["QoL"])
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("dir")
    ap.add_argument("--tag-order", default="")
    ap.add_argument("--first", type=int, default=10)
    ap.add_argument("--last", type=int, default=50)
    ap.add_argument("--band-scale", type=float, default=220)
    ap.add_argument("--gain", type=float, default=7.1)
    ap.add_argument("--per-world", action="store_true")
    a = ap.parse_args()
    by_tag = defaultdict(dict)
    for f in sorted(glob.glob(os.path.join(a.dir, "*__*.json"))):
        tag, world = os.path.basename(f)[:-5].split("__")
        by_tag[tag][world] = (load(f, a.first, a.last, a.band_scale, a.gain), cohesion.load(f, a.first, a.last))
    tags = [t for t in a.tag_order.split(",") if t] or sorted(by_tag)
    print("| run | worlds | fissions | checks | blocked (%) | proposals | dropped | searches | candidates | fission ms/turn (median) | pop | camps | mean QoL | turn ms (median) |")
    print("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|")
    summary = {}
    for tag in tags:
        if tag not in by_tag:
            continue
        runs = by_tag[tag]
        items = [(tag, list(runs.values()))] + ([(f"{tag} [{w}]", [v]) for w, v in sorted(runs.items())] if a.per_world else [])
        for name, group in items:
            t = [g[0] for g in group]
            tot = lambda k: sum(x[k] for x in t)
            checks, blocked = tot("checks"), tot("blocked")
            ms = [m for x in t for m in x["fission_ms"]]
            qol = [q for x in t for q in x["qol"]]
            turn = [m for x in t for m in x["turn_ms"]]
            print(f"| {name} | {len(t)} | {tot('fissions')} | {checks} | {blocked} ({100 * blocked / max(1, checks):.0f}%) | {tot('proposals')} | {tot('dropped')} | {tot('searches')} | {tot('candidates')} | "
                  f"{statistics.median(ms):.1f} | {tot('pop') / len(t):.0f} | {tot('camps') / len(t):.0f} | {statistics.mean(qol):.3f} | {statistics.median(turn):.0f} |")
            summary[name] = group
    print()
    print("Camp size P90/P95 (n) by housing class; targets 100 / 250 / 600-1,000")
    print("| run | unhoused | tent-dominant | fixed-dominant | median size u/t/f | score |")
    print("|---|---|---|---|---|---|")
    for name, group in summary.items():
        row = cohesion.row(name, cohesion.pool_runs([g[1] for g in group]), len(group))
        cell = lambda cl: f"{cohesion.fmt(row[cl + '_p90'])}/{cohesion.fmt(row[cl + '_p95'])} ({row[cl + '_n']})"
        med = "/".join(cohesion.fmt(row[cl + "_median"]) for cl in cohesion.CLASSES)
        print(f"| {name} | {cell('unhoused')} | {cell('tent')} | {cell('fixed')} | {med} | {row['score']:.2f} |")
    print()
    print("Split rate of camp-turns by size relative to the Neutral onset (splits / camp-turns)")
    print("| run | " + " | ".join(b[0] for b in BANDS) + " |")
    print("|---|" + "---|" * len(BANDS))
    for name, group in summary.items():
        cells = []
        for b, _, _ in BANDS:
            n = sum(g[0]["bands"][b][0] for g in group); s = sum(g[0]["bands"][b][1] for g in group)
            cells.append(f"{100 * s / n:.1f}% ({s}/{n})" if n else "n/a")
        print(f"| {name} | " + " | ".join(cells) + " |")
    print()
    print("QoL P25/median/P75 per housing class")
    print("| run | unhoused | tent-dominant | fixed-dominant |")
    print("|---|---|---|---|")
    for name, group in summary.items():
        row = cohesion.row(name, cohesion.pool_runs([g[1] for g in group]), len(group))
        def q(cl):
            x = row[cl + "_qol"]
            return "n/a" if x[1] is None else f"{x[0]:.2f}/{x[1]:.2f}/{x[2]:.2f}"
        print(f"| {name} | {q('unhoused')} | {q('tent')} | {q('fixed')} |")


if __name__ == "__main__":
    main()
