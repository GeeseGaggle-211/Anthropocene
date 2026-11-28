#!/usr/bin/env python3
"""Summarize fresh AnnualFoodExperiment outputs; keep exact run provenance."""
import argparse
import csv
import hashlib
import json
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("directories", nargs="+", type=Path)
parser.add_argument("--prefix", type=Path, default=Path("Docs/Experiments/AnnualLandFood-20261128"))
parser.add_argument("--equivalent-endpoints", nargs="*", type=Path, default=[])
args = parser.parse_args()
summary, manifests, trajectories, seen = [], [], [], set()
references, reference_manifests = {}, []
for directory in args.equivalent_endpoints:
    reference_manifests.append(json.loads((directory / "manifest.json").read_text()))
    for path in directory.glob("*-result.json"):
        result = json.loads(path.read_text())
        references[(result["seed"], result["arm"])] = result["finalSha256"]

for directory in args.directories:
    manifests.append(json.loads((directory / "manifest.json").read_text()))
    for path in sorted(directory.glob("*-result.json")):
        result = json.loads(path.read_text())
        key = (result["seed"], result["arm"])
        if key in seen:
            raise ValueError(f"Duplicate arm {key}")
        seen.add(key)
        if references:
            if references.get(key) != result["finalSha256"]:
                raise ValueError(f"Endpoint equivalence failed for {key}")
            result["equivalentReferenceEndpointSha256"] = references[key]
        trajectory = directory / f"seed-{key[0]}-{key[1]}.jsonl"
        rows = [json.loads(line) for line in trajectory.read_text().splitlines()]
        first, last = rows[0], rows[-1]
        tail = [r for r in rows if r["years"] >= last["years"] - 200]
        mx = sum(r["years"] for r in tail) / len(tail)
        my = sum(r["largest"] for r in tail) / len(tail)
        slope = 100 * sum((r["years"] - mx) * (r["largest"] - my) for r in tail) / sum((r["years"] - mx) ** 2 for r in tail)
        summary.append({**result, "elapsedYears": last["years"], "initial": first,
                        "final": last, "peakLargest": max(r["largest"] for r in rows),
                        "largestSlopeLast200PerCentury": slope,
                        "newIrrigatedFields": last["fields"] - first["fields"],
                        "trajectorySha256": hashlib.sha256(trajectory.read_bytes()).hexdigest()})
        # Approximately 50-year checkpoints plus the exact terminal boundary.
        next_year = 0
        for i, row in enumerate(rows):
            if row["years"] >= next_year or i == len(rows) - 1:
                trajectories.append({"seed": key[0], "arm": key[1], **row})
                next_year = row["years"] + 50

summary.sort(key=lambda r: (r["seed"], r["arm"]))
args.prefix.parent.mkdir(parents=True, exist_ok=True)
Path(str(args.prefix) + "-summary.json").write_text(json.dumps({
    "manifests": manifests, "equivalentEndpointReferenceManifests": reference_manifests, "runs": summary,
    "note": "Matched continuation from original year-1000 full-treatment saves; tail slopes track the changing maximum village, not a fixed village. Concurrent timings are not a performance benchmark."
}, indent=2) + "\n")
with Path(str(args.prefix) + "-trajectories.csv").open("w", newline="") as stream:
    writer = csv.DictWriter(stream, fieldnames=list(trajectories[0]))
    writer.writeheader()
    writer.writerows(trajectories)
for r in summary:
    print(f"{r['seed']:4} {r['arm']:12} largest={r['final']['largest']:5} "
          f"population={r['final']['population']:7} slope/century={r['largestSlopeLast200PerCentury']:8.1f} "
          f"fields={r['initial']['fields']}->{r['final']['fields']}")
