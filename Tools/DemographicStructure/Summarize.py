#!/usr/bin/env python3
import csv
import json
import math
import os
import sys


def read_csv(path):
    with open(path, newline="", encoding="utf-8") as handle:
        return list(csv.DictReader(handle))


def write_csv(path, rows):
    if not rows:
        return
    with open(path, "w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)


def as_float(row, key):
    return float(row[key])


def svg_plot(path, title, ylabel, rows, metric, selected):
    width, height = 960, 560
    left, right, top, bottom = 82, 25, 48, 75
    plot_w, plot_h = width - left - right, height - top - bottom
    series = []
    colors = ["#1f77b4", "#d62728", "#2ca02c", "#9467bd", "#ff7f0e", "#17becf"]
    for index, run in enumerate(selected):
        points = sorted((int(r["turn"]), as_float(r, metric)) for r in rows if r["run"] == run)
        if points:
            series.append((run, colors[index % len(colors)], points))
    ymax = max(value for _, _, points in series for _, value in points)
    ymax = max(1.0, ymax * 1.08)

    def x(turn):
        return left + plot_w * turn / 50.0

    def y(value):
        return top + plot_h * (1 - value / ymax)

    out = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}">',
           '<rect width="100%" height="100%" fill="white"/>',
           f'<text x="{width/2}" y="28" text-anchor="middle" font-family="sans-serif" font-size="20">{title}</text>']
    for tick in range(6):
        value = ymax * tick / 5
        yy = y(value)
        out.append(f'<line x1="{left}" y1="{yy:.2f}" x2="{width-right}" y2="{yy:.2f}" stroke="#dddddd"/>')
        out.append(f'<text x="{left-10}" y="{yy+4:.2f}" text-anchor="end" font-family="sans-serif" font-size="12">{value:.3g}</text>')
    for turn in range(0, 51, 10):
        xx = x(turn)
        out.append(f'<line x1="{xx:.2f}" y1="{top}" x2="{xx:.2f}" y2="{height-bottom}" stroke="#eeeeee"/>')
        out.append(f'<text x="{xx:.2f}" y="{height-bottom+22}" text-anchor="middle" font-family="sans-serif" font-size="12">{turn}</text>')
    out.append(f'<line x1="{left}" y1="{top}" x2="{left}" y2="{height-bottom}" stroke="black"/>')
    out.append(f'<line x1="{left}" y1="{height-bottom}" x2="{width-right}" y2="{height-bottom}" stroke="black"/>')
    out.append(f'<text x="{width/2}" y="{height-18}" text-anchor="middle" font-family="sans-serif" font-size="13">turn (20 years)</text>')
    out.append(f'<text x="18" y="{height/2}" text-anchor="middle" transform="rotate(-90 18 {height/2})" font-family="sans-serif" font-size="13">{ylabel}</text>')
    for run, color, points in series:
        poly = " ".join(f"{x(turn):.2f},{y(value):.2f}" for turn, value in points)
        out.append(f'<polyline points="{poly}" fill="none" stroke="{color}" stroke-width="2.5"/>')
        for turn, value in points:
            out.append(f'<circle cx="{x(turn):.2f}" cy="{y(value):.2f}" r="2.6" fill="{color}"/>')
    legend_y = height - 48
    legend_x = left
    for index, (run, color, _) in enumerate(series):
        xx = legend_x + (index % 3) * 285
        yy = legend_y + (index // 3) * 19
        out.append(f'<line x1="{xx}" y1="{yy}" x2="{xx+20}" y2="{yy}" stroke="{color}" stroke-width="3"/>')
        out.append(f'<text x="{xx+25}" y="{yy+4}" font-family="sans-serif" font-size="11">{run}</text>')
    out.append('</svg>')
    with open(path, "w", encoding="utf-8") as handle:
        handle.write("\n".join(out) + "\n")


def main():
    if len(sys.argv) < 3:
        raise SystemExit("Summarize.py <output-dir> <stage-dir> [stage-dir ...]")
    output, stages = sys.argv[1], sys.argv[2:]
    os.makedirs(output, exist_ok=True)
    os.makedirs(os.path.join(output, "plots"), exist_ok=True)
    trajectories, tribes, runs = [], [], []
    for stage in stages:
        label = os.path.basename(stage.rstrip(os.sep))
        stage_trajectories = read_csv(os.path.join(stage, "trajectories.csv"))
        stage_tribes = read_csv(os.path.join(stage, "per_tribe.csv"))
        for row in stage_trajectories:
            row["stage"] = label
        for row in stage_tribes:
            row["stage"] = label
        trajectories.extend(stage_trajectories)
        tribes.extend(stage_tribes)
        with open(os.path.join(stage, "runs.json"), encoding="utf-8") as handle:
            payload = json.load(handle)
        for item in payload:
            item["Stage"] = label
        runs.extend(payload)

    # Prefer the final-stage duplicate when the same exact calibration appears
    # in screening and final runs; run names themselves remain unique.
    write_csv(os.path.join(output, "trajectories.csv"), trajectories)
    write_csv(os.path.join(output, "per_tribe.csv"), tribes)
    with open(os.path.join(output, "runs.json"), "w", encoding="utf-8") as handle:
        json.dump(runs, handle, indent=2)
        handle.write("\n")

    spec_by_run = {item["Spec"]["Name"]: item["Spec"] for item in runs}
    final_rows = {row["run"]: row for row in trajectories if int(row["turn"]) == 50}
    agriculture = {}
    for name, row in final_rows.items():
        spec = spec_by_run[name]
        if spec["Archetype"] == "agricultural":
            agriculture[(float(spec["SpecialistMaxBonus"]), float(spec["SpecialistOnset"]), float(spec["SpecialistScale"]))] = row
    summary = []
    for name, row in final_rows.items():
        spec = spec_by_run[name]
        control = agriculture.get((float(spec["SpecialistMaxBonus"]), float(spec["SpecialistOnset"]), float(spec["SpecialistScale"])))
        summary.append({
            "run": name,
            "archetype": spec["Archetype"],
            "starting_herd": spec["Herd"],
            "tent_decay": spec["TentDecay"],
            "fabric_per_tent": spec["FabricPerTent"],
            "tent_productivity": spec["TentProductivity"],
            "specialist_max_bonus": spec["SpecialistMaxBonus"],
            "specialist_onset": spec["SpecialistOnset"],
            "specialist_scale": spec["SpecialistScale"],
            "population": row["population"],
            "tribes": row["tribes"],
            "median_tribe_population": row["median_population"],
            "median_qol": row["median_qol"],
            "fissions": row["cumulative_fissions"],
            "fissions_per_1000": row["fissions_per_1000"],
            "qol_blocked_fraction": row["qol_blocked_fraction"],
            "starvation_deaths": row["cumulative_starvation_deaths"],
            "housing_coverage": row["housing_coverage"],
            "tent_chain_food_share": row["tent_chain_food_share"],
            "tent_chain_surplus_food_share": row["tent_chain_surplus_food_share"],
            "tent_chain_labor_share": row["tent_chain_labor_share"],
            "herd_food_share": row["herd_food_share"],
            "agricultural_control_population": control["population"] if control else "",
            "agricultural_control_median_tribe": control["median_population"] if control else "",
            "agricultural_control_qol": control["median_qol"] if control else "",
            "agricultural_control_fissions": control["cumulative_fissions"] if control else ""
        })
    summary.sort(key=lambda row: (row["archetype"], row["run"]))
    write_csv(os.path.join(output, "candidate_summary.csv"), summary)

    selected = ["ag_current", "pastoral_current_established", "ag_moderate", "pastoral_moderate_established", "ag_strong", "pastoral_strong_established"]
    plots = [
        ("total_population.svg", "Total population over 50 turns", "people", "population"),
        ("median_tribe_population.svg", "Median population per tribe", "people", "median_population"),
        ("tribe_count.svg", "Number of tribes / camps", "tribes", "tribes"),
        ("median_qol.svg", "Median quality of life", "QoL", "median_qol"),
        ("cumulative_fissions.svg", "Cumulative daughter formations", "fissions", "cumulative_fissions")
    ]
    for filename, title, ylabel, metric in plots:
        svg_plot(os.path.join(output, "plots", filename), title, ylabel, trajectories, metric, selected)

    metadata = {
        "turns": 50,
        "years_per_turn": 20,
        "starting_population": 240,
        "small_herd": 5,
        "established_herd": 80,
        "systems_explicitly_enabled": ["QoLMortalityEnabled", "FissionEnabled", "HerdLabourEnabled", "SharedCropHerdAllocationEnabled"],
        "planner": "continuous deterministic Value automation; no movement AI",
        "fission_space": "128x64 all-land maps, every tile initially known to the founding society, resource-rich viable footprints",
        "agriculture": "six river-supported fertile tiles at the Stone Farm Tool supported intensity; fixed Housing",
        "pastoral": "productive grassland, goat herd, portable Light Housing, Animal Fabric; no Cultivation",
        "plots": [name for name, _, _, _ in plots]
    }
    with open(os.path.join(output, "metadata.json"), "w", encoding="utf-8") as handle:
        json.dump(metadata, handle, indent=2)
        handle.write("\n")


if __name__ == "__main__":
    main()
