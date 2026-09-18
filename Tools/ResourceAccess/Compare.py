"""Compare two measured cohorts with identical world keys; quantities remain latent potential."""
import json
import statistics
import sys
from pathlib import Path

before_path, after_path = map(Path, sys.argv[1:3])
before = json.loads((before_path / "data.json").read_text())
after = json.loads((after_path / "data.json").read_text())
checks = 0
rows = {}

def equal(left, right, label):
    global checks
    if left != right:
        raise AssertionError(label)
    checks += 1

equal([w["Key"] for w in before["Worlds"]], [w["Key"] for w in after["Worlds"]], "world keys")
for old, new in zip(before["Worlds"], after["Worlds"]):
    for key in ("Seed", "Shape", "Width", "Height", "LandTiles", "TraversableTiles", "ConfigurationHash", "LandRuns"):
        equal(old[key], new[key], old["Key"] + ": " + key)
    equal(len(old["Camps"]), len(new["Camps"]), "camp count")
    for oc, nc in zip(old["Camps"], new["Camps"]):
        equal({k: v for k, v in oc.items() if k != "Resources"},
              {k: v for k, v in nc.items() if k != "Resources"}, "camp geometry/identity")
        equal([r["Material"] for r in oc["Resources"]], [r["Material"] for r in nc["Resources"]], "materials")
        for a, b in zip(oc["Resources"], nc["Resources"]):
            rows.setdefault(a["Material"], []).append((a, b))
            # A quantity-only change must preserve all sampled any-source and rich-source targets.
            for key in ("GeometricDistance", "RichDistance", "RichTile", "DiscoveryTechnology", "BaseAccessFraction"):
                equal(a[key], b[key], a["Material"] + ": " + key)
            for key in ("LandDistance", "Tile"):
                equal(a["Nearest"][0][key], b["Nearest"][0][key], a["Material"] + ": nearest " + key)

result = {"SourceCommits": [before["SourceCommit"], after["SourceCommit"]],
          "MaximumQuantities": [before["ResourceOptions"]["MaximumQuantity"], after["ResourceOptions"]["MaximumQuantity"]],
          "UnchangedGeometryChecks": checks, "Materials": {}}
for material, pairs in rows.items():
    entry = {"Tribes": len(pairs), "Thresholds": [], "Quantities": {}}
    for index, threshold in enumerate((0, 1, 10, 100)):
        stats = []
        for side in (0, 1):
            ds = [p[side]["Nearest"][index]["LandDistance"] for p in pairs]
            reached = [d for d in ds if d is not None]
            stats.append({"Unreachable": len(ds) - len(reached),
                          "MedianDistance": statistics.median(reached) if reached else None,
                          **{f"Within{r}": sum(d is not None and d <= r for d in ds) for r in (0, 2, 5, 10)}})
        entry["Thresholds"].append({"MinimumAccessible": threshold, "Before": stats[0], "After": stats[1]})
    for key in ("AtCamp", "Within2", "Within5", "Within10", "ScatteredWithin2"):
        ratios = [b[key] / a[key] for a, b in pairs if a[key] > 0]
        entry["Quantities"][key] = {"PositiveBefore": len(ratios),
            "BeforeSum": sum(a[key] for a, b in pairs), "AfterSum": sum(b[key] for a, b in pairs),
            "BeforeMedian": statistics.median(a[key] for a, b in pairs),
            "AfterMedian": statistics.median(b[key] for a, b in pairs),
            "RatioMinimum": min(ratios) if ratios else None,
            "RatioMedian": statistics.median(ratios) if ratios else None,
            "RatioMaximum": max(ratios) if ratios else None}
    result["Materials"][material] = entry
(after_path / "comparison.json").write_text(json.dumps(result, indent=2))
print(json.dumps(result, indent=2))
