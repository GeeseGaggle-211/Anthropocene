#!/usr/bin/env python3
"""Plot terminal tribe welfare histograms from --demography-qol-histogram output."""

import argparse
import math
import sys
from pathlib import Path

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np


BANDS = ["adequate", "clothing-gap", "housing-gap", "both-gaps"]
LABELS = {
    "adequate": "C ≥ 0.8, H ≥ 0.8",
    "clothing-gap": "Clothing gap",
    "housing-gap": "Housing gap",
    "both-gaps": "Both gaps",
}
COLORS = {
    "adequate": "#2A9D8F",
    "clothing-gap": "#F4A261",
    "housing-gap": "#E9C46A",
    "both-gaps": "#D1495B",
}


def fields(line):
    return dict(token.split("=", 1) for token in line.split()[1:] if "=" in token)


def protection(row):
    clothing, housing = row["clothing"], row["housing"]
    if clothing >= 0.8:
        return "adequate" if housing >= 0.8 else "housing-gap"
    return "clothing-gap" if housing >= 0.8 else "both-gaps"


def bins_to(values, step, minimum_high):
    high = max(minimum_high, math.ceil(max(values) / step) * step)
    return np.arange(0, high + step * 1.001, step)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", required=True)
    parser.add_argument("--source-output")
    args = parser.parse_args()

    terminal, propensity, metadata = [], [], []
    for raw in sys.stdin:
        line = raw.strip()
        if line.startswith("DEMOGRAPHY_QOL_META "):
            metadata.append(fields(line))
        elif line.startswith("DEMOGRAPHY_QOL_TERMINAL "):
            data = fields(line)
            terminal.append({
                "seed": int(data["seed"]),
                "camp": int(data["camp"]),
                "village": int(data["village"]),
                "freshDaughter": int(data["freshDaughter"]),
                "socialTransfer": int(data["socialTransfer"]),
                "population": float(data["population"]),
                "qol": float(data["qol"]),
                "popQol": float(data["popQol"]),
                "food": float(data["food"]),
                "clothing": float(data["clothing"]),
                "housing": float(data["housing"]),
                "expectation": float(data["expectation"]),
                "cohesion": float(data["cohesion"]),
            })
        elif line.startswith("DEMOGRAPHY_QOL_SOURCE_RATE "):
            data = fields(line)
            propensity.append({
                "village": int(data["village"]),
                "condition": data["condition"],
                "eligible": int(data["sourceCapableCampTurns"]),
                "flows": int(data["flows"]),
                "migrants": int(data["migrants"]),
                "meanQoL": float(data["meanQoL"]),
                "meanPopulation": float(data["meanPopulation"]),
            })

    if not terminal:
        raise SystemExit("No DEMOGRAPHY_QOL_TERMINAL rows on stdin")
    if len(metadata) != 1 or "continuations" not in metadata[0]:
        raise SystemExit("Expected exactly one DEMOGRAPHY_QOL_META row with continuations")
    continuations = int(metadata[0]["continuations"])

    for row in terminal:
        row["band"] = protection(row)
    counts = {band: sum(row["band"] == band for row in terminal) for band in BANDS}

    population = [row["population"] for row in terminal]
    pop_qol = [row["popQol"] for row in terminal]
    qol = [row["qol"] for row in terminal]
    metrics = [
        ("population", "Population", bins_to(population, 50, 1000)),
        ("popQol", "Population × QoL", bins_to(pop_qol, 25, 500)),
        ("qol", "QoL", np.arange(0, 1.0001 + 0.05, 0.05)),
    ]

    plt.rcParams.update({"font.size": 10, "axes.titleweight": "bold"})
    fig, axes = plt.subplots(1, 3, figsize=(15.5, 5.8))
    fig.subplots_adjust(left=0.055, right=0.985, bottom=0.19, top=0.73, wspace=0.10)
    for ax, (key, title, bins) in zip(axes, metrics):
        series = [[row[key] for row in terminal if row["band"] == band] for band in BANDS]
        ax.hist(series, bins=bins, stacked=True,
                color=[COLORS[band] for band in BANDS],
                label=[f"{LABELS[band]} (n={counts[band]})" for band in BANDS],
                edgecolor="white", linewidth=0.45)
        values = np.array([row[key] for row in terminal])
        median = float(np.median(values))
        ax.axvline(median, color="#263238", linestyle="--", linewidth=1.4)
        ax.text(0.98, 0.95, f"median {median:.3f}" if key == "qol" else f"median {median:.0f}",
                transform=ax.transAxes, ha="right", va="top", color="#263238")
        ax.set_title(title)
        ax.set_xlabel(title)
        ax.grid(axis="y", alpha=0.22)
        ax.spines[["top", "right"]].set_visible(False)
    axes[0].set_ylabel("Terminal tribes")
    handles, legend_labels = axes[-1].get_legend_handles_labels()
    fig.legend(handles, legend_labels, loc="lower center", ncol=4, frameon=False,
               bbox_to_anchor=(0.5, 0.025), fontsize=9)

    total_population = sum(population)
    total_pop_qol = sum(pop_qol)
    weighted_qol = total_pop_qol / total_population if total_population else 0
    villages = sum(row["village"] for row in terminal)
    fig.suptitle("End-state committed tribe conditions: current social defaults",
                 fontsize=16, fontweight="bold", y=0.97)
    seeds = sorted({row["seed"] for row in terminal})
    seed_text = ", ".join(str(seed) for seed in seeds)
    fresh_daughters = sum(row["freshDaughter"] for row in terminal)
    social_transfers = sum(row["socialTransfer"] for row in terminal)
    fig.text(0.5, 0.875,
             f"Seeds {seed_text} · {continuations} continuation turns · fission + immigration · "
             f"n={len(terminal)} tribes ({villages} villages) · population-weighted QoL={weighted_qol:.3f}",
             ha="center", fontsize=10, color="#455A64")
    fig.text(0.5, 0.825,
             f"Committed Close-boundary proxy: {social_transfers} final-turn transfer-affected tribes; "
             f"{fresh_daughters} fresh daughters carry projected first-turn QoL",
             ha="center", fontsize=9, color="#607D8B")
    output = Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    fig.savefig(output, dpi=180, facecolor="white")
    plt.close(fig)

    if propensity:
        source_output = Path(args.source_output) if args.source_output else output.with_name(output.stem + "-migration-sources.png")
        source_output.parent.mkdir(parents=True, exist_ok=True)
        lookup = {(row["village"], row["condition"]): row for row in propensity}
        x = np.arange(len(BANDS)); width = 0.36
        fig, axes = plt.subplots(1, 2, figsize=(13, 5.5))
        fig.subplots_adjust(left=0.075, right=0.985, bottom=0.16, top=0.76, wspace=0.18)
        for village, offset, color, label in [
            (0, -width / 2, "#457B9D", "Non-village"),
            (1, width / 2, "#8E5EA2", "Village"),
        ]:
            rows = [lookup.get((village, band), {"eligible": 0, "flows": 0, "migrants": 0}) for band in BANDS]
            flow_rate = [100 * row["flows"] / row["eligible"] if row["eligible"] else 0 for row in rows]
            people_rate = [row["migrants"] / row["eligible"] if row["eligible"] else 0 for row in rows]
            axes[0].bar(x + offset, flow_rate, width, color=color, label=label)
            axes[1].bar(x + offset, people_rate, width, color=color, label=label)
            for axis, values in zip(axes, [flow_rate, people_rate]):
                for xpos, value, row in zip(x + offset, values, rows):
                    if row["eligible"]:
                        axis.text(xpos, value, f"{row['flows']}/{row['eligible']}" if axis is axes[0]
                                  else f"{row['migrants']}", ha="center", va="bottom", fontsize=8, rotation=90)
        labels = ["Adequate", "Clothing\ngap", "Housing\ngap", "Both\ngaps"]
        axes[0].set_ylabel("Flows per 100 source-capable camp-turns")
        axes[1].set_ylabel("Migrants per source-capable camp-turn")
        for ax in axes:
            ax.set_xticks(x, labels)
            ax.margins(y=0.22)
            ax.grid(axis="y", alpha=0.22)
            ax.spines[["top", "right"]].set_visible(False)
            ax.legend(frameon=False)
        fig.suptitle("Where immigration originates", fontsize=16, fontweight="bold", y=0.97)
        fig.text(0.5, 0.875,
                 "Denominator: resident camp-turns able to send the minimum group; condition threshold C/H = 0.8",
                 ha="center", fontsize=10, color="#455A64")
        fig.savefig(source_output, dpi=180, facecolor="white")
        plt.close(fig)

    print(f"HISTOGRAM tribes={len(terminal)} population={int(total_population)} "
          f"popQol={total_pop_qol:.3f} weightedQoL={weighted_qol:.6f} villages={villages} "
          f"freshDaughters={fresh_daughters} socialTransfers={social_transfers} "
          + " ".join(f"{band}={counts[band]}" for band in BANDS))


if __name__ == "__main__":
    main()
