"""Summarize the read-only start audit; no third-party dependencies."""
import csv
import json
import statistics
import sys
from pathlib import Path

root = Path(sys.argv[1] if len(sys.argv) > 1 else "TestResults/TribeResourceAccess")
data = json.loads((root / "data.json").read_text())
materials = [r["Material"] for r in data["Worlds"][0]["Camps"][0]["Resources"]]
flat = []
for w in data["Worlds"]:
    for c in w["Camps"]:
        for r in c["Resources"]:
            row = dict(World=w["Key"], Player=c["Player"], Tile=c["Tile"], ComponentTiles=c["ComponentTiles"])
            row.update({k: v for k, v in r.items() if k != "Nearest"})
            for n in r["Nearest"]:
                for k in ("LandDistance", "Tile", "Accessible", "Total"):
                    row[f'{k}_{int(n["MinimumAccessible"])}'] = n[k]
            flat.append(row)
with (root / "tribes.csv").open("w", newline="") as f:
    writer = csv.DictWriter(f, fieldnames=list(flat[0]))
    writer.writeheader()
    writer.writerows(flat)

totals = [dict(World=w["Key"], **m) for w in data["Worlds"] for m in w.get("MaterialTotals", [])]
if totals:
    with (root / "material-totals.csv").open("w", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=list(totals[0]))
        writer.writeheader()
        writer.writerows(totals)

def quantile(values, p):
    values = sorted(values)
    if not values:
        return None
    pos = (len(values) - 1) * p
    lo = int(pos)
    hi = min(lo + 1, len(values) - 1)
    return round(values[lo] + (values[hi] - values[lo]) * (pos - lo), 2)

summary = []
for world in ["All"] + [w["Key"] for w in data["Worlds"]]:
    for material in materials:
        rows = [r for r in flat if r["Material"] == material and (world == "All" or r["World"] == world)]
        for threshold in (0, 1, 10, 100):
            ds = [r[f"LandDistance_{threshold}"] for r in rows]
            reached = [d for d in ds if d is not None]
            summary.append(dict(World=world, Material=material, Threshold=threshold, Tribes=len(rows),
                Unreachable=len(ds)-len(reached), Median=quantile(reached, .5), P90=quantile(reached, .9),
                Maximum=max(reached) if reached else None,
                **{f"Within{radius}": sum(d is not None and d <= radius for d in ds) for radius in (0, 1, 2, 5, 10)},
                Stock2Median=quantile([r["Within2"] for r in rows], .5),
                Stock5Median=quantile([r["Within5"] for r in rows], .5)))
(root / "summary.json").write_text(json.dumps(summary, indent=2))
with (root / "summary.csv").open("w", newline="") as f:
    writer = csv.DictWriter(f, fieldnames=list(summary[0]))
    writer.writeheader()
    writer.writerows(summary)
metals = ("Copper", "Tin", "Zinc", "Iron")
for s in summary:
    if (s["World"] == "All" and (s["Threshold"] == 0 or s["Material"] in metals)) or (s["World"] != "All" and s["Material"] in metals and s["Threshold"] in (0, 10)):
        print(s)

# Compact view data: distances by threshold, target tiles, radius stocks, straight-line and rich distances.
view = {"materials": materials, "technology": [r["DiscoveryTechnology"] for r in data["Worlds"][0]["Camps"][0]["Resources"]], "summary": summary, "worlds": []}
for w in data["Worlds"]:
    view["worlds"].append({"key": w["Key"], "width": w["Width"], "height": w["Height"], "land": w["LandRuns"], "camps": [
        {"player": c["Player"], "tile": c["Tile"], "area": c["ComponentTiles"], "resources": [
            {"d": [n["LandDistance"] for n in r["Nearest"]], "target": [n["Tile"] for n in r["Nearest"]],
             "units": [float(format(n["Accessible"], ".6g")) for n in r["Nearest"]],
             "local": [float(format(r[k], ".6g")) for k in ("AtCamp", "Within2", "Within5", "Within10")],
             "deep": [float(format(r.get(k, 0), ".6g")) for k in ("DeepAtCamp", "DeepWithin2", "DeepWithin10")],
             "geo": r["GeometricDistance"], "rich": r["RichDistance"]} for r in c["Resources"]]} for c in w["Camps"]]})
(root / "view-data.json").write_text(json.dumps(view, separators=(",", ":")))
