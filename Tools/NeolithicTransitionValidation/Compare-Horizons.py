#!/usr/bin/env python3
"""Compare two transition-validator cohorts by case and resolved turn.

From the repository root on this Windows workstation:

    python Tools/NeolithicTransitionValidation/Compare-Horizons.py --baseline TestResults/TurnHorizonBaseline/TransitionQuick --candidate TestResults/TurnHorizonCandidate/TransitionQuick --out TestResults/TurnHorizonComparison/TransitionQuick

Both input directories must contain matrix.csv and trajectory.jsonl from the
same validator options. The output is turn-deltas.csv, final-deltas.csv and
summary.md. This is calibration evidence: it records differences without
treating the seasonal resolver as an exact oracle or imposing a pass/fail gate.
"""

from __future__ import annotations

import argparse
import csv
import json
import math
from collections import Counter
from dataclasses import dataclass
from pathlib import Path


IDENTITY = (
    "habitat", "scarcity", "fixedSite", "settlementStress", "seed", "tile",
    "initialPopulation", "populationMultiplier", "reserveMultiplier",
    # Fixed-settlement cases inject these after discovery. A changed support
    # estimate changes the fixture itself, so those runs are not paired inputs.
    "controlledPopulationAfter", "controlledFoodAfter",
)
CORE = (
    "resident_population", "qol", "food_produced", "food_end",
    "cultivation_capital", "cultivation_intensity", "food_satisfaction",
)
MATRIX_EXTRA = {
    "deaths": "matrix_cumulative_deaths",
    "totalDiscarded": "matrix_total_discarded_food",
    "minFoodSatisfaction": "matrix_min_food_satisfaction",
    "maxFoodBalanceError": "matrix_max_food_balance_error",
    "maxEcologyFoodBalanceError": "matrix_max_ecology_food_balance_error",
    "maxEcologyBiomassBalanceError": "matrix_max_ecology_biomass_balance_error",
    "maxEcologyWaterBalanceError": "matrix_max_ecology_water_balance_error",
    "maxMaterialBalanceError": "matrix_max_material_balance_error",
    "maxManufacturingFoodError": "matrix_max_manufacturing_food_error",
    "invariantFailures": "matrix_invariant_failures",
}
CSV_COLUMNS = (
    "case", "baseline_turn", "candidate_turn", "baseline_year", "candidate_year",
    "metric", "baseline", "candidate", "delta", "relative_pct", "status",
)


@dataclass
class Snapshot:
    case: str
    turn: int
    year: float
    metrics: dict[str, float]


@dataclass
class Cohort:
    directory: Path
    matrix: dict[str, dict[str, str]]
    turns: dict[tuple[str, int], Snapshot]
    goods: set[str]
    materials: set[str]
    event_count: int


def number(value: object) -> float | None:
    if value is None or isinstance(value, bool):
        return None
    try:
        result = float(value)
    except (TypeError, ValueError):
        return None
    return result if math.isfinite(result) else None


def member(value: object, *path: str) -> object:
    for key in path:
        if not isinstance(value, dict):
            return None
        value = value.get(key)
    return value


def put(metrics: dict[str, float], name: str, value: object) -> None:
    parsed = number(value)
    if parsed is not None:
        metrics[name] = parsed


def extract_metrics(row: dict[str, object], goods: set[str], materials: set[str]) -> dict[str, float]:
    metrics: dict[str, float] = {}
    for metric, path in {
        "resident_population": ("residentPopulation",),
        "qol": ("qols", "qol"),
        "qol_cloth": ("qols", "cloth"),
        "qol_housing": ("qols", "housing"),
        "food_produced": ("demographic", "FoodProduced"),
        "food_received": ("demographic", "FoodReceived"),
        "food_consumed": ("demographic", "FoodConsumed"),
        "food_end": ("demographic", "FinalReserves"),
        "food_manufacturing_use": ("demographic", "ManufacturingFood"),
        "food_discarded": ("demographic", "DiscardedInventory"),
        "camp_reserves": ("campReserves",),
        "linked_reserves": ("linkedReserves",),
        "food_satisfaction": ("foodSatisfaction",),
        "cultivation_capital": ("e",),
        "cultivation_intensity": ("q",),
        "farmers": ("farmers",),
        "food_gatherers": ("foodGatherers",),
        "material_assigned": ("materialAssigned",),
        "material_worker_intervals": ("materialWorkerIntervals",),
        "land_condition": ("landCondition",),
        "aquatic_condition": ("aquaticCondition",),
    }.items():
        put(metrics, metric, member(row, *path))

    footprint = row.get("footprint")
    if isinstance(footprint, list):
        values = [number(tile.get("landCondition")) for tile in footprint if isinstance(tile, dict)]
        values = [value for value in values if value is not None]
        if values:
            metrics["footprint_mean_land_condition"] = sum(values) / len(values)

    ledger = row.get("materials")
    if isinstance(ledger, dict):
        for stage in ("before", "after"):
            values = ledger.get(stage)
            if isinstance(values, dict):
                for material, amount in values.items():
                    materials.add(str(material))
                    put(metrics, f"material_{stage}_{material}", amount)
        receipts = ledger.get("receipts")
        if isinstance(receipts, list):
            by_material: Counter[str] = Counter()
            for receipt in receipts:
                if not isinstance(receipt, dict):
                    continue
                material = receipt.get("material")
                amount = number(receipt.get("Amount", receipt.get("amount")))
                if material is not None and amount is not None:
                    by_material[str(material)] += amount
                    materials.add(str(material))
            for material, amount in by_material.items():
                metrics[f"material_receipt_{material}"] = amount

    production = row.get("production")
    if isinstance(production, list):
        by_good: Counter[str] = Counter()
        for receipt in production:
            if not isinstance(receipt, dict):
                continue
            good = receipt.get("good")
            amount = number(receipt.get("Output", receipt.get("output")))
            if good is not None and amount is not None:
                by_good[str(good)] += amount
                goods.add(str(good))
            for material in ("Stone", "Clay", "Wood", "PlantFood"):
                used = number(receipt.get(material, receipt.get(material.lower())))
                if used is not None:
                    key = f"production_input_{material}"
                    metrics[key] = metrics.get(key, 0.0) + used
        for good, amount in by_good.items():
            metrics[f"goods_output_{good}"] = amount
    return metrics


def load_cohort(directory: Path) -> Cohort:
    matrix_path = directory / "matrix.csv"
    trajectory_path = directory / "trajectory.jsonl"
    if not matrix_path.is_file() or not trajectory_path.is_file():
        raise FileNotFoundError(f"Expected matrix.csv and trajectory.jsonl in {directory}")
    with matrix_path.open(newline="", encoding="utf-8-sig") as stream:
        matrix_rows = list(csv.DictReader(stream))
    matrix: dict[str, dict[str, str]] = {}
    for row in matrix_rows:
        case = row.get("id", "")
        if not case or case in matrix:
            raise ValueError(f"Missing or duplicate matrix case '{case}' in {matrix_path}")
        matrix[case] = row
    turns: dict[tuple[str, int], Snapshot] = {}
    goods: set[str] = set()
    materials: set[str] = set()
    events = 0
    with trajectory_path.open(encoding="utf-8") as stream:
        for line_number, line in enumerate(stream, 1):
            if not line.strip():
                continue
            row = json.loads(line)
            if not isinstance(row, dict):
                raise ValueError(f"Expected JSON object at {trajectory_path}:{line_number}")
            if row.get("turn") is None:
                events += 1
                continue
            case = str(row.get("id", ""))
            key = (case, int(row["turn"]))
            if not case or key in turns:
                raise ValueError(f"Missing or duplicate trajectory key {key} at {trajectory_path}:{line_number}")
            year = number(row.get("year"))
            if year is None:
                raise ValueError(f"Missing year for {key} at {trajectory_path}:{line_number}")
            turns[key] = Snapshot(case, key[1], year, extract_metrics(row, goods, materials))
    extra = {case for case, _ in turns if case not in matrix}
    if extra:
        raise ValueError(f"Trajectory cases absent from {matrix_path}: {', '.join(sorted(extra))}")
    return Cohort(directory, matrix, turns, goods, materials, events)


def comparison_status(baseline: Snapshot | None, candidate: Snapshot | None,
                      metric: str, identity_ok: bool) -> str:
    if baseline is None:
        return "missing_baseline_turn"
    if candidate is None:
        return "missing_candidate_turn"
    if not identity_ok:
        return "identity_mismatch"
    if not math.isclose(baseline.year, candidate.year, rel_tol=0, abs_tol=1e-8):
        return "year_mismatch"
    if metric not in baseline.metrics:
        return "missing_baseline_metric"
    if metric not in candidate.metrics:
        return "missing_candidate_metric"
    return "paired"


def measure_row(case: str, baseline: Snapshot | None, candidate: Snapshot | None,
                metric: str, identity_ok: bool) -> dict[str, object]:
    status = comparison_status(baseline, candidate, metric, identity_ok)
    left = None if baseline is None else baseline.metrics.get(metric)
    right = None if candidate is None else candidate.metrics.get(metric)
    delta = right - left if status == "paired" else None
    relative = None
    if delta is not None:
        if abs(left) > 1e-9:
            relative = 100 * delta / abs(left)
        elif abs(delta) <= 1e-9:
            relative = 0.0
    return {
        "case": case,
        "baseline_turn": "" if baseline is None else baseline.turn,
        "candidate_turn": "" if candidate is None else candidate.turn,
        "baseline_year": "" if baseline is None else baseline.year,
        "candidate_year": "" if candidate is None else candidate.year,
        "metric": metric,
        "baseline": "" if left is None else left,
        "candidate": "" if right is None else right,
        "delta": "" if delta is None else delta,
        "relative_pct": "" if relative is None else relative,
        "status": status,
    }


def dynamic_metric_names(baseline: Cohort, candidate: Cohort) -> set[str]:
    names = set()
    for good in baseline.goods | candidate.goods:
        names.add(f"goods_output_{good}")
    for material in baseline.materials | candidate.materials:
        names.add(f"material_receipt_{material}")
        names.add(f"material_before_{material}")
        names.add(f"material_after_{material}")
    names.update(f"production_input_{material}" for material in ("Stone", "Clay", "Wood", "PlantFood"))
    return names


def complete_zero_metrics(snapshot: Snapshot, names: set[str]) -> None:
    for name in names:
        if name.startswith(("goods_output_", "material_receipt_", "production_input_")):
            snapshot.metrics.setdefault(name, 0.0)


def write_csv(path: Path, rows: list[dict[str, object]]) -> None:
    with path.open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=CSV_COLUMNS)
        writer.writeheader()
        writer.writerows(rows)


def fmt(value: float | None, digits: int = 3) -> str:
    if value is None or value == "":
        return "n/a"
    return f"{value:,.{digits}f}"


def top_for_metric(rows: list[dict[str, object]], metric: str) -> dict[str, object] | None:
    candidates = [row for row in rows if row["status"] == "paired" and
                  row["metric"] == metric and row["relative_pct"] != ""]
    return max(candidates, key=lambda row: (abs(row["relative_pct"]),
                                             abs(row["delta"]), row["case"]), default=None)


def write_report(path: Path, baseline: Cohort, candidate: Cohort,
                 turn_rows: list[dict[str, object]], final_rows: list[dict[str, object]],
                 issues: list[str], worst_count: int) -> None:
    paired_cases = len(set(baseline.matrix) & set(candidate.matrix))
    aligned_cases = sum(all(baseline.matrix[case].get(key) == candidate.matrix[case].get(key)
                            for key in IDENTITY)
                        for case in set(baseline.matrix) & set(candidate.matrix))
    paired_turns = len({(row["case"], row["baseline_turn"]) for row in turn_rows
                        if row["status"] == "paired"})
    lines = [
        "# Turn-horizon transition calibration",
        "",
        f"Baseline: `{baseline.directory}`  ",
        f"Candidate: `{candidate.directory}`  ",
        f"Shared cases: **{paired_cases}**; identity-aligned cases: **{aligned_cases}**; "
        f"paired resolved case-turns: **{paired_turns}**.  ",
        "Differences are candidate minus baseline. This report applies no outcome pass/fail threshold.",
        "",
        "## Largest relative differences by signal",
        "",
        "| Signal | Worst resolved turn | Final case | Final difference |",
        "| --- | --- | --- | ---: |",
    ]
    for metric in CORE:
        turn = top_for_metric(turn_rows, metric)
        final = top_for_metric(final_rows, metric)
        turn_text = "n/a" if turn is None else (f"{turn['case']} T{turn['baseline_turn']} "
            f"({fmt(turn['relative_pct'])}%; delta {fmt(turn['delta'])})")
        final_case = "n/a" if final is None else str(final["case"])
        final_delta = "n/a" if final is None else f"{fmt(final['relative_pct'])}% (delta {fmt(final['delta'])})"
        lines.append(f"| `{metric}` | {turn_text} | {final_case} | {final_delta} |")

    material_rows = [row for row in turn_rows if row["status"] == "paired" and
                     row["metric"].startswith(("material_receipt_", "goods_output_", "production_input_"))]
    material_rows.sort(key=lambda row: (-abs(row["delta"]), row["case"], row["baseline_turn"], row["metric"]))
    lines += ["", "## Largest material and goods changes by absolute amount", "",
              "| Case / turn | Signal | Baseline | Candidate | Difference |",
              "| --- | --- | ---: | ---: | ---: |"]
    for row in material_rows[:worst_count]:
        relative = "" if row["relative_pct"] == "" else f" ({fmt(row['relative_pct'])}%)"
        lines.append(f"| {row['case']} T{row['baseline_turn']} | `{row['metric']}` | "
                     f"{fmt(row['baseline'])} | {fmt(row['candidate'])} | "
                     f"delta {fmt(row['delta'])}{relative} |")
    if not material_rows:
        lines.append("| n/a | No paired material or goods signal | n/a | n/a | n/a |")

    statuses = Counter(row["status"] for row in turn_rows)
    lines += ["", "## Coverage and limits", "",
              f"- Per-turn rows: {len(turn_rows):,}; paired metric rows: {statuses['paired']:,}."]
    for status, count in sorted(statuses.items()):
        if status != "paired":
            lines.append(f"- `{status}`: {count:,} metric rows.")
    for issue in issues[:worst_count]:
        lines.append(f"- {issue}")
    if len(issues) > worst_count:
        lines.append(f"- {len(issues) - worst_count} additional alignment issues are reflected in the CSV status column.")
    lines += [
        "- The current validator trace has no tile condition or ending tool-inventory fields. "
        "Cultivation `q`/`e`, material ledgers, and tool *output* are reported; condition "
        "and tool-stock deltas remain unavailable until both cohorts record them.",
        "- A blank relative percentage means the baseline is effectively zero; use the absolute delta. "
        "Percentages use the absolute baseline as denominator.",
        "- Quick runs cover their recorded horizon only. Compare longer post-transition behavior "
        "with full validation runs using the same case and turn alignment.",
        "",
        "`turn-deltas.csv` and `final-deltas.csv` contain every measured signal, including "
        "zero-baseline and missing records.",
        "",
    ]
    path.write_text("\n".join(lines), encoding="utf-8")


def compare(baseline: Cohort, candidate: Cohort, output: Path, worst_count: int) -> None:
    output.mkdir(parents=True, exist_ok=True)
    dynamic = dynamic_metric_names(baseline, candidate)
    for snapshot in (*baseline.turns.values(), *candidate.turns.values()):
        complete_zero_metrics(snapshot, dynamic)

    issues: list[str] = []
    identity_ok: dict[str, bool] = {}
    for case in sorted(set(baseline.matrix) | set(candidate.matrix)):
        left = baseline.matrix.get(case)
        right = candidate.matrix.get(case)
        if left is None or right is None:
            identity_ok[case] = False
            issues.append(f"Case `{case}` is absent from {'baseline' if left is None else 'candidate'} matrix.")
            continue
        differences = [key for key in IDENTITY if left.get(key) != right.get(key)]
        identity_ok[case] = not differences
        if differences:
            issues.append(f"Case `{case}` has different identity fields: {', '.join(differences)}.")
        if left.get("error") or right.get("error"):
            issues.append(f"Case `{case}` has validator errors: baseline={left.get('error')!r}, "
                          f"candidate={right.get('error')!r}.")

    turn_rows: list[dict[str, object]] = []
    for case, turn in sorted(set(baseline.turns) | set(candidate.turns)):
        left = baseline.turns.get((case, turn))
        right = candidate.turns.get((case, turn))
        names = set(dynamic)
        if left is not None:
            names.update(left.metrics)
        if right is not None:
            names.update(right.metrics)
        for metric in sorted(names):
            turn_rows.append(measure_row(case, left, right, metric, identity_ok.get(case, False)))

    final_rows: list[dict[str, object]] = []
    for case in sorted(set(baseline.matrix) | set(candidate.matrix)):
        left_turns = [snap for (item, _), snap in baseline.turns.items() if item == case]
        right_turns = [snap for (item, _), snap in candidate.turns.items() if item == case]
        left = max(left_turns, key=lambda snap: snap.turn, default=None)
        right = max(right_turns, key=lambda snap: snap.turn, default=None)
        if left is not None:
            for source, name in MATRIX_EXTRA.items():
                put(left.metrics, name, baseline.matrix.get(case, {}).get(source))
        if right is not None:
            for source, name in MATRIX_EXTRA.items():
                put(right.metrics, name, candidate.matrix.get(case, {}).get(source))
        if left is not None and right is not None and left.turn != right.turn:
            issues.append(f"Case `{case}` ends at different resolved turns: baseline T{left.turn}, candidate T{right.turn}.")
        names = set(dynamic)
        if left is not None:
            names.update(left.metrics)
        if right is not None:
            names.update(right.metrics)
        for metric in sorted(names):
            row = measure_row(case, left, right, metric, identity_ok.get(case, False))
            if row["status"] == "paired" and left.turn != right.turn:
                row["status"] = "final_turn_mismatch"
                row["delta"] = row["relative_pct"] = ""
            final_rows.append(row)

    write_csv(output / "turn-deltas.csv", turn_rows)
    write_csv(output / "final-deltas.csv", final_rows)
    write_report(output / "summary.md", baseline, candidate, turn_rows, final_rows, issues, worst_count)
    paired = sum(row["status"] == "paired" for row in turn_rows)
    print(f"Compared {len(identity_ok)} case IDs; wrote {paired} paired metric rows to {output}")
    if issues:
        print(f"Alignment warnings: {len(issues)} (see summary.md and CSV status columns)")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--baseline", type=Path, required=True, help="Directory containing baseline matrix.csv and trajectory.jsonl")
    parser.add_argument("--candidate", type=Path, required=True, help="Directory containing candidate matrix.csv and trajectory.jsonl")
    parser.add_argument("--out", type=Path, required=True, help="Output directory for turn-deltas.csv, final-deltas.csv, summary.md")
    parser.add_argument("--worst", type=int, default=12, help="Maximum material changes and alignment issues shown in Markdown")
    args = parser.parse_args()
    if args.worst < 1:
        parser.error("--worst must be positive")
    compare(load_cohort(args.baseline), load_cohort(args.candidate), args.out, args.worst)


if __name__ == "__main__":
    main()
