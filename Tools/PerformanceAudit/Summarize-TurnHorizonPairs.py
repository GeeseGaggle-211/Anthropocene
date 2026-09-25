"""Summarize paired TurnHorizon PerformanceAudit JSON without changing raw data.

Usage: python Tools/PerformanceAudit/Summarize-TurnHorizonPairs.py
       TestResults/TurnHorizonPairs/Timing

Input is produced by Run-TurnHorizonPairs.ps1. The script pairs turns by
world size, pair number and turn ID; an agricultural comparison requires an
agricultural camp before that turn in both resolvers.
"""

import argparse
import csv
import json
import re
from collections import defaultdict
from pathlib import Path
from statistics import median


FIELDS = (
    "aiMs", "prepareMs", "executeMs", "totalMs", "ecologyMs",
    "allocatedBytes", "managedHeapBytes", "activeTiles", "tileIntegrations", "seasonalTiles",
    "claimsAllocated", "horizonPreparationMs", "horizonCommitMs",
    "productionMs", "planCalls", "forecastCalls", "sourceLookups",
)
NAME = re.compile(r"(?P<mode>Timing|Exact|Counters)-(?P<size>80|320)-pair(?P<pair>\d+)-(?P<variant>baseline|candidate)\.json$")


def read_reports(root, mode):
    result = defaultdict(dict)
    for path in root.glob(f"{mode}-*-pair*-*.json"):
        match = NAME.fullmatch(path.name)
        if not match or match.group("mode") != mode:
            continue
        key = (int(match.group("size")), int(match.group("pair")))
        variant = match.group("variant")
        if variant in result[key]:
            raise ValueError(f"Duplicate report for {key}/{variant}")
        result[key][variant] = json.loads(path.read_text(encoding="utf-8"))
    if not result:
        raise ValueError(f"No {mode} reports in {root}")
    for key, pair in result.items():
        if set(pair) != {"baseline", "candidate"}:
            raise ValueError(f"Unmatched reports for {key}: {sorted(pair)}")
        baseline, candidate = pair["baseline"], pair["candidate"]
        for field in ("seed", "players", "shape", "configHash", "turnDays", "inputSaveSha256"):
            if baseline[field] != candidate[field]:
                raise ValueError(f"Different {field} for {key}")
        if baseline["workerCount"] != candidate["workerCount"]:
            raise ValueError(f"Different worker count for {key}")
    return result


def paired_turns(reports):
    rows = []
    repeated_outcomes = {}
    for (size, pair_number), pair in sorted(reports.items()):
        baseline, candidate = pair["baseline"], pair["candidate"]
        left = {sample["turn"]: sample for sample in baseline["samples"]}
        right = {sample["turn"]: sample for sample in candidate["samples"]}
        if set(left) != set(right):
            raise ValueError(f"Turn IDs differ for {size} pair {pair_number}")
        for turn in sorted(left):
            a, b = left[turn], right[turn]
            if a["phase"] != b["phase"]:
                raise ValueError(f"Turn phases differ for {size} pair {pair_number} turn {turn}")
            for variant, sample in (("baseline", a), ("candidate", b)):
                key = (size, variant, turn)
                outcome = (sample["population"], sample["foodYield"], sample["priorWeightedQol"])
                if key in repeated_outcomes and repeated_outcomes[key] != outcome:
                    raise ValueError(f"Repeated outcomes differ for {key}")
                repeated_outcomes[key] = outcome
            row = {
                "size": size, "pair": pair_number, "turn": turn,
                "phase": a["phase"],
                "agricultural": bool(a["preAgriculturalCamps"] and b["preAgriculturalCamps"]),
                "baselineAgriculturalCamps": a["preAgriculturalCamps"],
                "candidateAgriculturalCamps": b["preAgriculturalCamps"],
                "baselinePopulation": a["population"], "candidatePopulation": b["population"],
                "baselineFoodYield": a["foodYield"], "candidateFoodYield": b["foodYield"],
                "baselinePriorQol": a["priorWeightedQol"],
                "candidatePriorQol": b["priorWeightedQol"],
            }
            for field in FIELDS:
                av, bv = a.get(field, 0), b.get(field, 0)
                row[f"baseline_{field}"] = av
                row[f"candidate_{field}"] = bv
                row[f"ratio_{field}"] = bv / av if av else ""
            rows.append(row)
    return rows


def fmt(value, digits=2):
    return f"{value:,.{digits}f}"


def summary(reports, rows, mode):
    lines = [
        "# Turn-horizon paired performance",
        "",
        f"Mode: **{mode}**. Measurements are paired by saved world and turn. "
        "Time and allocation units are milliseconds and bytes. "
        "The timing mode disables fingerprints and detailed counters.",
        "",
        "| Tribes | Pair | Agricultural turns | AI baseline → candidate | Prepare baseline → candidate | "
        "Execute baseline → candidate | Total baseline → candidate | Median paired total gain | Ecology baseline → candidate | "
        "Alloc. baseline → candidate | Heap baseline → candidate |",
        "| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |",
    ]
    for size, pair_number in sorted(reports):
        subset = [r for r in rows if r["size"] == size and r["pair"] == pair_number and r["agricultural"]]
        if not subset:
            raise ValueError(f"No paired agricultural turns for {size} pair {pair_number}")
        def mid(field, variant):
            return median(r[f"{variant}_{field}"] for r in subset)
        paired_gain = median(1 - r["ratio_totalMs"] for r in subset)
        cells = [str(size), str(pair_number), str(len(subset))]
        for field in ("aiMs", "prepareMs", "executeMs", "totalMs"):
            cells.append(f"{fmt(mid(field, 'baseline'))} → {fmt(mid(field, 'candidate'))}")
        cells.append(f"{fmt(paired_gain * 100, 1)}%")
        cells.append(f"{fmt(mid('ecologyMs', 'baseline'))} → {fmt(mid('ecologyMs', 'candidate'))}")
        cells.append(f"{fmt(mid('allocatedBytes', 'baseline') / 1048576, 1)} → "
                     f"{fmt(mid('allocatedBytes', 'candidate') / 1048576, 1)} MiB")
        cells.append(f"{fmt(mid('managedHeapBytes', 'baseline') / 1048576, 1)} → "
                     f"{fmt(mid('managedHeapBytes', 'candidate') / 1048576, 1)} MiB")
        lines.append("| " + " | ".join(cells) + " |")
    lines += ["", "## Acceptance signals", ""]
    for size in (80, 320):
        group = [r for r in rows if r["size"] == size and r["agricultural"]]
        if not group:
            continue
        pair_gains = []
        for pair_number in sorted({r["pair"] for r in group}):
            subset = [r for r in group if r["pair"] == pair_number]
            pair_gains.append(median(1 - r["ratio_totalMs"] for r in subset))
        slow = defaultdict(int)
        for r in group:
            if r["ratio_totalMs"] > 1.10:
                slow[r["turn"]] += 1
        lines.append(f"- {size} tribes: median of pair-level agricultural total-time gains "
                     f"**{fmt(median(pair_gains) * 100, 1)}%**; "
                     f"pair gains {', '.join(fmt(g * 100, 1) + '%' for g in pair_gains)}; "
                     f"corresponding turns >10% slower in at least two pairs: "
                     f"{', '.join(map(str, sorted(t for t, count in slow.items() if count >= 2))) or 'none'}.")
    lines += ["", "## Outcome movement from the common starting save", "",
              "| Tribes | Max per-turn population difference | Max per-turn prior-cohort QoL difference | "
              "Max per-turn realized food difference | Final population baseline → candidate |",
              "| ---: | ---: | ---: | ---: | ---: |"]
    for size in sorted({r["size"] for r in rows}):
        subset = [r for r in rows if r["size"] == size and r["pair"] == 1]
        def largest(left, right):
            return max(abs(r[right] - r[left]) / abs(r[left]) if abs(r[left]) > 1e-9 else 0 for r in subset)
        final = max(subset, key=lambda r: r["turn"])
        lines.append("| " + " | ".join((str(size),
            f"{fmt(largest('baselinePopulation', 'candidatePopulation') * 100, 2)}%",
            f"{fmt(largest('baselinePriorQol', 'candidatePriorQol') * 100, 2)}%",
            f"{fmt(largest('baselineFoodYield', 'candidateFoodYield') * 100, 2)}%",
            f"{final['baselinePopulation']:,} → {final['candidatePopulation']:,}")) + " |")
    lines += [
        "", "## Provenance", "",
        "Each JSON records source revision, runtime, input-save hash, worker count, "
        "warmup count and per-turn outcomes. `manifest-<mode>.json` records binary and save SHA-256 hashes. "
        "The paired CSV retains every turn and phase counter without rounding.",
        "",
    ]
    return "\n".join(lines)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    parser.add_argument("--mode", choices=("Timing", "Exact", "Counters"), default="Timing")
    args = parser.parse_args()
    reports = read_reports(args.directory, args.mode)
    rows = paired_turns(reports)
    csv_path = args.directory / f"paired-turns-{args.mode}.csv"
    with csv_path.open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)
    markdown = summary(reports, rows, args.mode)
    report_path = args.directory / f"summary-{args.mode}.md"
    report_path.write_text(markdown, encoding="utf-8")
    print(f"Wrote {csv_path} and {report_path}")


if __name__ == "__main__":
    main()
