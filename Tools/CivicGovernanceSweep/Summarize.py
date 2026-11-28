"""Summarize Tools/CivicGovernanceSweep results into a markdown table.

Usage: python Tools/CivicGovernanceSweep/Summarize.py [TestResults/CivicGovernanceSweep]
"""
import glob
import json
import os
import statistics
import sys

root = sys.argv[1] if len(sys.argv) > 1 else "TestResults/CivicGovernanceSweep"
arms = ["off", "phase1", "full", "full-3a"]
rows = {}
for path in glob.glob(os.path.join(root, "seed-*-*.json")):
    r = json.load(open(path))
    rows.setdefault(r["arm"], []).append(r)

def med(xs):
    return statistics.median(xs) if xs else 0

print("| Arm | Seeds | Peak village (median / max) | Final largest village (median) | Final total pop (sum) | Villages >2,000 / >3,500 / >5,000 (final, sum) | Mean civic / sanitation coverage | Peak Egalitarian / Stratified village (max) | Builder-yr civic / sanitation (sum) | Mean turn ms |")
print("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |")
for arm in arms:
    rs = sorted(rows.get(arm, []), key=lambda r: r["seed"])
    if not rs:
        continue
    fin = [r["snapshots"][-1] for r in rs]
    print("| {} | {} | {:.0f} / {} | {:.0f} | {:,} | {} / {} / {} | {:.2f} / {:.2f} | {} / {} | {:,.0f} / {:,.0f} | {:.0f} |".format(
        arm, len(rs), med([r["peakVillage"] for r in rs]), max(r["peakVillage"] for r in rs),
        med([f["villageMax"] for f in fin]), sum(f["population"] for f in fin),
        sum(f["over2000"] for f in fin), sum(f["over3500"] for f in fin), sum(f["over5000"] for f in fin),
        statistics.mean(f["meanCivicCoverage"] for f in fin), statistics.mean(f["meanSanitationCoverage"] for f in fin),
        max(r["peakEgalitarianVillage"] for r in rs), max(r["peakStratifiedVillage"] for r in rs),
        sum(r["civicBuilderYears"] for r in rs), sum(r["sanitationBuilderYears"] for r in rs),
        statistics.mean(r["meanTurnMs"] for r in rs)))

print()
print("Per seed peak village (off / phase1 / full / full-3a):")
seeds = sorted({r["seed"] for rs in rows.values() for r in rs})
for s in seeds:
    vals = []
    for arm in arms:
        m = [r for r in rows.get(arm, []) if r["seed"] == s]
        vals.append(str(m[0]["peakVillage"]) if m else "-")
    print("- seed {}: {}".format(s, " / ".join(vals)))

print()
print("Largest-village binding constraint at the end of the full arm (food produced / required, birth rate, deaths by cause):")
for r in sorted(rows.get("full", []), key=lambda r: r["seed"]):
    c = r["snapshots"][-1]["largest"][0]
    d = c.get("demography") or {}
    print("- seed {}: N={} C={} P={} Mc={} Md={} food {}/{} reserves={} b={} crowd={} disease={} exposure={} housing={}".format(
        r["seed"], c["Population"], c["C"], c["P"], c["Mc"], c["Md"], d.get("foodProduced"), d.get("foodRequired"),
        d.get("reserves"), d.get("birthRate"), d.get("crowding"), d.get("disease"), d.get("exposure"), d.get("housing")))
