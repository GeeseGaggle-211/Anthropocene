#!/usr/bin/env python3
"""Reduce PerformanceAudit --details=1 runs to the social-cohesion calibration scores (Docs/SocialCohesion.md).

Camps are classified by their actual housing each turn, never by technology:
  unhoused        sheltered people (fixed + tent capacity) / residents < 0.1
  tent-dominant   sheltered >= 0.1 and tent shelter people > fixed shelter people
  fixed-dominant  sheltered >= 0.1 and fixed shelter people >= tent shelter people
Camp-turn observations from post-opening turns FIRST..LAST (default 10..50) are pooled per run. Scores are the relative
distance of the P90 and P95 camp size outside the target band (unhoused 100, tent-dominant 250, fixed-dominant 600-1,000).

usage: Summarize-Cohesion.py <run-dir> [--tag-order a,b,c] [--csv out.csv] [--first 10] [--last 50]
Run files are named <tag>__<Shape><seed>.json; rows aggregate over worlds (pooled observations) and also list per world.
"""
import argparse, glob, json, os, statistics, sys
from collections import defaultdict

# Primary score: sizes at most the design level for unhoused (100) and tent-dominant (250) camps, inside 600-1,000 for fixed-dominant ones.
TARGETS = {"unhoused": (0, 100), "tent": (0, 250), "fixed": (600, 1000)}
# Secondary score: the same levels as two-sided point targets (distance from 100 / 250 / the nearer edge of 600-1,000).
POINT_TARGETS = {"unhoused": (100, 100), "tent": (250, 250), "fixed": (600, 1000)}
CLASSES = ["unhoused", "tent", "fixed"]


def classify(camp):
    # Unhoused: fewer than one person in ten sheltered at all (capacity / residents). This is independent of p_tent, which would
    # otherwise move the boundary between classes when the parameter under test changes (H = p_tent * tent share).
    sheltered = (camp["fixedShelterPeople"] + camp["tentShelterPeople"]) / max(1, camp["Population"])
    if sheltered < 0.1:
        return "unhoused"
    return "tent" if camp["tentShelterPeople"] > camp["fixedShelterPeople"] else "fixed"


def pct(values, q):
    if not values:
        return None
    v = sorted(values)
    k = (len(v) - 1) * q
    lo = int(k)
    hi = min(lo + 1, len(v) - 1)
    return v[lo] + (v[hi] - v[lo]) * (k - lo)


def distance(x, band):
    lo, hi = band
    if x is None:
        return None
    if x < lo and lo > 0:
        return (lo - x) / lo
    if x > hi:
        return (x - hi) / hi
    return 0.0


def load(path, first, last):
    d = json.load(open(path))
    post = [s for s in d["samples"] if s["postOpening"]]
    obs = defaultdict(list)            # class -> sizes
    # Camps are unhoused almost only before the first breakthrough, which ends inside the opening: the unhoused class therefore also
    # pools the opening turns (every camp-turn with fewer than one person in ten sheltered).
    for s in d["samples"]:
        if not s["postOpening"]:
            for c in s.get("camps") or []:
                if classify(c) == "unhoused":
                    obs["unhoused"].append(c["Population"])
    qol = defaultdict(list)            # class -> QoL
    qol_tech = defaultdict(list)       # first technology -> QoL
    husb = {"n": 0, "village": 0, "tent": 0}
    for i, s in enumerate(post, start=1):
        if i < first or i > last:
            continue
        for c in s.get("camps") or []:
            cl = classify(c)
            obs[cl].append(c["Population"])
            qol[cl].append(c["QoL"])
            qol_tech[c["technology"]].append(c["QoL"])
            if c["technology"] == "AnimalHusbandry":
                husb["n"] += 1
                husb["village"] += 1 if c["IsVillage"] else 0
                husb["tent"] += 1 if cl == "tent" else 0
    final = post[-1]
    return {
        "obs": obs, "qol": qol, "qol_tech": qol_tech, "husb": husb,
        "population": final["population"], "camps_final": final["campCount"],
        "fissions": sum(s["fissionCount"] for s in d["samples"]),
        "median_ms": statistics.median(s["totalMs"] for s in post),
        "median_resolve_ms": statistics.median(s["resolveMs"] for s in post),
    }


def score(pool, targets=None):
    targets = targets or TARGETS
    terms = {}
    total = 0.0
    n_missing = 0
    for cl in CLASSES:
        sizes = pool["obs"].get(cl, [])
        for q in (90, 95):
            value = pct(sizes, q / 100) if len(sizes) >= 30 else None
            terms[(cl, q)] = value
            dist = distance(value, targets[cl])
            if dist is None:
                n_missing += 1
            else:
                total += dist
    return terms, total, n_missing


def pool_runs(runs):
    pool = {"obs": defaultdict(list), "qol": defaultdict(list), "qol_tech": defaultdict(list), "husb": {"n": 0, "village": 0, "tent": 0},
            "population": 0, "camps_final": 0, "fissions": 0, "median_ms": [], "median_resolve_ms": []}
    for r in runs:
        for k in ("obs", "qol", "qol_tech"):
            for cl, v in r[k].items():
                pool[k][cl].extend(v)
        for k in ("n", "village", "tent"):
            pool["husb"][k] += r["husb"][k]
        pool["population"] += r["population"]
        pool["camps_final"] += r["camps_final"]
        pool["fissions"] += r["fissions"]
        pool["median_ms"].append(r["median_ms"])
        pool["median_resolve_ms"].append(r["median_resolve_ms"])
    return pool


def fmt(x, digits=0):
    return "n/a" if x is None else (f"{x:.{digits}f}")


def row(tag, pool, nworlds):
    terms, total, missing = score(pool)
    _, point_total, _ = score(pool, POINT_TARGETS)
    out = {"tag": tag, "worlds": nworlds, "score": total, "point_score": point_total, "missing": missing}
    for cl in CLASSES:
        sizes = pool["obs"].get(cl, [])
        out[f"{cl}_n"] = len(sizes)
        out[f"{cl}_p90"] = terms[(cl, 90)]
        out[f"{cl}_p95"] = terms[(cl, 95)]
        out[f"{cl}_median"] = statistics.median(sizes) if sizes else None
        q = pool["qol"].get(cl, [])
        out[f"{cl}_qol"] = (pct(q, .25), pct(q, .5), pct(q, .75)) if q else (None, None, None)
    out["tech_qol"] = {t: (pct(v, .25), pct(v, .5), pct(v, .75), len(v)) for t, v in sorted(pool["qol_tech"].items())}
    out["population"] = pool["population"] / nworlds
    out["camps_final"] = pool["camps_final"] / nworlds
    out["fissions"] = pool["fissions"] / nworlds
    h = pool["husb"]
    out["husb_n"] = h["n"]
    out["husb_village"] = h["village"] / h["n"] if h["n"] else None
    out["husb_tent"] = h["tent"] / h["n"] if h["n"] else None
    out["median_ms"] = statistics.median(pool["median_ms"])
    out["median_resolve_ms"] = statistics.median(pool["median_resolve_ms"])
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("dir")
    ap.add_argument("--tag-order", default="")
    ap.add_argument("--csv", default="")
    ap.add_argument("--first", type=int, default=10)
    ap.add_argument("--last", type=int, default=50)
    ap.add_argument("--per-world", action="store_true")
    ap.add_argument("--techs", action="store_true")
    a = ap.parse_args()
    files = sorted(glob.glob(os.path.join(a.dir, "*__*.json")))
    by_tag = defaultdict(dict)
    cache = os.path.join(a.dir, ".cache.json")
    for f in files:
        tag, world = os.path.basename(f)[:-5].split("__")
        by_tag[tag][world] = load(f, a.first, a.last)
    tags = [t for t in a.tag_order.split(",") if t] or sorted(by_tag)
    rows = []
    for tag in tags:
        if tag not in by_tag:
            continue
        rows.append(row(tag, pool_runs(by_tag[tag].values()), len(by_tag[tag])))
        if a.per_world:
            for w, r in sorted(by_tag[tag].items()):
                rows.append(row(f"{tag} [{w}]", pool_runs([r]), 1))
    print("| run | worlds | score (point) | unhoused P90/P95 (n) | tent P90/P95 (n) | fixed P90/P95 (n) | median size u/t/f | pop | camps | fissions | husb village / tent | ms |")
    print("|---|---|---|---|---|---|---|---|---|---|---|---|")
    for r in rows:
        def cell(cl):
            return f"{fmt(r[cl + '_p90'])}/{fmt(r[cl + '_p95'])} ({r[cl + '_n']})"
        med = "/".join(fmt(r[cl + "_median"]) for cl in CLASSES)
        husb = "n/a" if r["husb_village"] is None else f"{100 * r['husb_village']:.0f}% / {100 * r['husb_tent']:.0f}% ({r['husb_n']})"
        print(f"| {r['tag']} | {r['worlds']} | {r['score']:.2f} ({r['point_score']:.2f}){'*' if r['missing'] else ''} | {cell('unhoused')} | {cell('tent')} | {cell('fixed')} | {med} | {r['population']:.0f} | {r['camps_final']:.0f} | {r['fissions']:.0f} | {husb} | {r['median_ms']:.0f} |")
    print()
    print("QoL P25/median/P75 per housing class")
    print("| run | unhoused | tent-dominant | fixed-dominant |")
    print("|---|---|---|---|")
    for r in rows:
        def q(cl):
            a1, a2, a3 = r[cl + "_qol"]
            return "n/a" if a2 is None else f"{a1:.2f}/{a2:.2f}/{a3:.2f}"
        print(f"| {r['tag']} | {q('unhoused')} | {q('tent')} | {q('fixed')} |")
    if a.techs:
        print()
        print("QoL P25/median/P75 by first technology (n camp-turns)")
        techs = sorted({t for r in rows for t in r["tech_qol"]})
        print("| run | " + " | ".join(techs) + " |")
        print("|---|" + "---|" * len(techs))
        for r in rows:
            cells = []
            for t in techs:
                v = r["tech_qol"].get(t)
                cells.append("n/a" if not v else f"{v[0]:.2f}/{v[1]:.2f}/{v[2]:.2f} ({v[3]})")
            print(f"| {r['tag']} | " + " | ".join(cells) + " |")
    if a.csv:
        import csv
        with open(a.csv, "w", newline="") as fh:
            keys = ["tag", "worlds", "score", "point_score", "missing", "population", "camps_final", "fissions", "husb_n", "husb_village", "husb_tent", "median_ms", "median_resolve_ms"]
            for cl in CLASSES:
                keys += [f"{cl}_n", f"{cl}_p90", f"{cl}_p95", f"{cl}_median"]
            w = csv.writer(fh)
            w.writerow(keys)
            for r in rows:
                w.writerow([r[k] for k in keys])


if __name__ == "__main__":
    main()
