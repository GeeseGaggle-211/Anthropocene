#!/usr/bin/env python3
"""Summarize matched 80-tribe production-config gameplay audits.

Expected reports: <shape>-seed<seed>-Baseline.json and -Horizon.json.
The PerformanceAudit runner must have been invoked with --details=1.
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
from pathlib import Path


SHAPES = ("Continents", "Pangaea", "Archipelagos")
KINDS = ("Cultivation", "AnimalHusbandry", "Watercraft")


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def read_report(path: Path) -> dict:
    report = json.loads(path.read_text(encoding="utf-8"))
    if report["players"] != 80 or not report.get("productionConfig") or not report.get("detailsEnabled"):
        raise ValueError(f"Expected 80-tribe production-config detailed report: {path}")
    if not report["samples"] or any(len(s["camps"]) != 80 for s in report["samples"]):
        raise ValueError(f"Expected 80 camp detail rows per turn: {path}")
    return report


def weighted_qol(camps: list[dict]) -> float:
    total = sum(c["priorResidents"] for c in camps)
    return sum(c["QoL"] * c["priorResidents"] for c in camps) / total if total else 0.0


def group(camps: list[dict], kind: str) -> dict:
    members = [c for c in camps if c["technology"] == kind]
    prior = sum(c["priorResidents"] for c in members)
    return {
        "tribes": len(members),
        "linkedPopulation": sum(c["linked"] for c in members),
        "priorWeightedQol": weighted_qol(members),
        "priorFoodSatisfaction":
            sum(c["FoodNeedsMetRatio"] * c["priorResidents"] for c in members) / prior if prior else 0.0,
    }


def sample_row(shape: str, model: str, sample: dict) -> dict:
    camps = sample["camps"]
    row = {
        "world": shape, "model": model, "turn": sample["turn"], "phase": sample["phase"],
        "linkedPopulation": sample["population"],
        "priorWeightedQol": sample["priorWeightedQol"],
        "foodYield": sample["foodYield"],
        "survivingTribes": sum(c["linked"] > 0 for c in camps),
    }
    if row["linkedPopulation"] != sum(c["linked"] for c in camps):
        raise ValueError(f"Linked population does not equal camp detail sum: {shape}/{model}/T{row['turn']}")
    if abs(row["priorWeightedQol"] - weighted_qol(camps)) > 1e-10:
        raise ValueError(f"Prior-cohort QoL does not equal camp detail weighting: {shape}/{model}/T{row['turn']}")
    for kind in KINDS:
        data = group(camps, kind)
        for key, value in data.items():
            row[f"{kind}_{key}"] = value
    return row


def fmt(value: float, digits: int = 0) -> str:
    return f"{value:,.{digits}f}"


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--seed", type=int, default=1)
    args = parser.parse_args()
    args.out.mkdir(parents=True, exist_ok=True)

    reports = {}
    hashes = {}
    rows = []
    for shape in SHAPES:
        for model in ("Baseline", "Horizon"):
            path = args.root / f"{shape}-seed{args.seed}-{model}.json"
            report = read_report(path)
            if report["shape"] != shape or report["seed"] != args.seed:
                raise ValueError(f"Mismatched shape or seed: {path}")
            reports[shape, model] = report
            hashes[shape, model] = sha256(path)
            rows.extend(sample_row(shape, model, sample) for sample in report["samples"])
        left, right = (reports[shape, model] for model in ("Baseline", "Horizon"))
        if left["initialFingerprint"] != right["initialFingerprint"]:
            raise ValueError(f"Initial generated worlds differ: {shape}")
        if left["configHash"] != right["configHash"]:
            raise ValueError(f"Configuration differs: {shape}")
        for report in (left, right):
            turns = [s["turn"] for s in report["samples"]]
            if turns != list(range(1, turns[-1] + 1)):
                raise ValueError(f"Nonconsecutive turns: {shape}")

    csv_path = args.out / "every-turn.csv"
    with csv_path.open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)

    by_key = {(r["world"], r["model"], r["turn"]): r for r in rows}
    lines = [
        "# Turn-horizon representative 80-tribe gameplay sweep",
        "",
        f"Production OpeningConfig and ResourceGenerationConfig; seed {args.seed}; "
        "Standard 1,024×512; 80 tribes; 20-year turns. Each world runs through natural "
        "opening completion and ten further turns. Baseline is the seasonal prior-cohort "
        "checkpoint; Horizon is the analytical resolver. Reports start from identical "
        "generated fingerprints and configuration hashes within each world. "
        "The Archipelagos opening ends at turn 12 in the baseline and turn 11 "
        "in the horizon model, so its paired table stops at their last common "
        "turn 21; the baseline's turn 22 remains in the CSV.",
        "",
        "Population is the sum of linked camp and hunter populations. QoL is weighted "
        "by the prior-turn camp residents, matching the prepared-turn cohort. Food is "
        "realized non-herd tile yield in the reported turn; it includes hunter fishing. "
        "Specialization counts use each camp's ending opening technology. These are "
        "full-world AI runs, not isolated policy controls.",
        "",
        "## World totals at selected turns",
        "",
        "| World | Turn | Model | Linked population | Prior-cohort QoL | Food yield | Surviving tribes | Cultivation / Husbandry / Watercraft |",
        "| --- | ---: | --- | ---: | ---: | ---: | ---: | ---: |",
    ]
    for shape in SHAPES:
        final_turn = min(reports[shape, model]["samples"][-1]["turn"]
                         for model in ("Baseline", "Horizon"))
        for turn in sorted({1, 10, final_turn}):
            for model in ("Baseline", "Horizon"):
                r = by_key[shape, model, turn]
                mix = " / ".join(str(r[f"{kind}_tribes"]) for kind in KINDS)
                lines.append(f"| {shape} | {turn} | {model} | {fmt(r['linkedPopulation'])} | "
                             f"{fmt(r['priorWeightedQol'], 3)} | {fmt(r['foodYield'])} | "
                             f"{r['survivingTribes']} | {mix} |")

    lines += ["", "## Final specialization outcomes", "",
              "Each cell is tribes / linked population / prior-resident-weighted QoL. "
              "Food satisfaction is available for every group and turn in the CSV.", "",
              "| World | Specialization | Baseline | Horizon |",
              "| --- | --- | ---: | ---: |"]
    for shape in SHAPES:
        turn = min(reports[shape, model]["samples"][-1]["turn"]
                   for model in ("Baseline", "Horizon"))
        for kind in KINDS:
            cells = []
            for model in ("Baseline", "Horizon"):
                r = by_key[shape, model, turn]
                cells.append("—" if r[f"{kind}_tribes"] == 0 else
                             f"{r[f'{kind}_tribes']} / {fmt(r[f'{kind}_linkedPopulation'])} / "
                             f"{fmt(r[f'{kind}_priorWeightedQol'], 3)}")
            lines.append(f"| {shape} | {kind} | {cells[0]} | {cells[1]} |")

    fishing_reports = {}
    for model in ("Baseline", "Horizon"):
        path = args.root / f"Archipelagos-seed{args.seed}-{model}-Fishing.json"
        if path.exists():
            report = read_report(path)
            original = reports["Archipelagos", model]
            if [s["fingerprint"] for s in report["samples"]] != [s["fingerprint"] for s in original["samples"]]:
                raise ValueError(f"Fishing detail rerun changed simulation fingerprints: {model}")
            fishing_reports[model] = report
    if fishing_reports and len(fishing_reports) != 2:
        raise ValueError("Both fishing-detail reports are required")
    if fishing_reports:
        lines += ["", "## Archipelagos food receipts", "",
                  "Fishing is the active hunter claim; marine gathering is passive "
                  "coastal food. Values are realized food units, not stocks.", "",
                  "| Period | Model | Active Fishing | Passive marine gathering | Cultivation | Total non-herd food |",
                  "| --- | --- | ---: | ---: | ---: | ---: |"]
        last_common = min(fishing_reports[model]["samples"][-1]["turn"]
                          for model in ("Baseline", "Horizon"))
        for label, turns in (("Turn 10", {10}),
                             (f"Turn {last_common}", {last_common}),
                             (f"Cumulative turns 1–{last_common}", set(range(1, last_common + 1)))):
            for model in ("Baseline", "Horizon"):
                samples = [s for s in fishing_reports[model]["samples"] if s["turn"] in turns]
                amount = lambda kind: sum(s["foodByKind"].get(kind, 0) for s in samples)
                lines.append(f"| {label} | {model} | {fmt(amount('Fishing'))} | "
                             f"{fmt(amount('Marine gathering'))} | {fmt(amount('Cultivation'))} | "
                             f"{fmt(sum(s['foodYield'] for s in samples))} |")

    lines += ["", "## Provenance and limits", "",
              "The detailed audit is intended for gameplay outcomes. It is not a timing "
              "measurement: it enables per-turn fingerprints and per-camp serialization.",
              "The Archipelagos receipt rerun separates active hunter Fishing from "
              "passive marine gathering, and its turn fingerprints match the original "
              "Archipelagos outcomes. Receipts are whole-world totals, not assigned "
              "to each Watercraft tribe; a hunter may belong to another camp.",
              "", "| World | Baseline JSON SHA-256 | Horizon JSON SHA-256 |",
              "| --- | --- | --- |"]
    for shape in SHAPES:
        lines.append(f"| {shape} | `{hashes[shape, 'Baseline']}` | `{hashes[shape, 'Horizon']}` |")
    if fishing_reports:
        lines += ["", "Fishing-detail JSON SHA-256: " + ", ".join(
            f"{model} `{sha256(args.root / f'Archipelagos-seed{args.seed}-{model}-Fishing.json')}`"
            for model in ("Baseline", "Horizon")) + "."]
    lines += ["", "`every-turn.csv` contains every observed model-turn and specialization metric.", ""]
    (args.out / "summary.md").write_text("\n".join(lines), encoding="utf-8")
    print(f"Wrote {len(rows)} model-turn rows to {args.out}")


if __name__ == "__main__":
    main()
