"""Validate matched 25-turn runs and report the requested four-world comparison.

python Tools/PerformanceAudit/Summarize-ForecastUnits.py TIMING_DIR DETAILS_DIR OUT_DIR
Details may be paired-runner reports or Baseline/Candidate-Details-PLAYERS-SEED.json.
Population means each tribe's linked population, including its remote hunters.
"""
import csv
import json
import pathlib
import statistics
import sys

CASES = ((80, 11), (80, 23), (80, 1729), (320, 1729))


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def samples(report):
    rows = [r for r in report["samples"] if r["phase"] == "post-opening"]
    assert report["postOpeningTurnsRequested"] == report["postOpeningTurnsMeasured"] == len(rows) == 25
    return rows


def percentile(values, p):
    values = sorted(values)
    if not values:
        return None
    index = (len(values) - 1) * p
    lower = int(index)
    upper = min(lower + 1, len(values) - 1)
    return values[lower] + (values[upper] - values[lower]) * (index - lower)


def write_csv(path, rows):
    with path.open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)


def main(timing_root, detail_root, output_root):
    output_root.mkdir(parents=True, exist_ok=True)
    manifest = read(timing_root / "manifest.json")
    assert manifest["mode"] == "Timing" and manifest["pairs"] >= 1
    assert len(manifest["runs"]) == len(CASES) * 2 * manifest["pairs"]
    performance = []
    populations = []
    run_means = []
    for players, seed in CASES:
        pairs = []
        totals = {"Baseline": [], "Candidate": []}
        outcomes = {variant: [] for variant in totals}
        for pair in range(1, manifest["pairs"] + 1):
            reports = {variant: read(timing_root / f"{variant}-{players}-{seed}-pair{pair}.json")
                       for variant in totals}
            baseline, candidate = reports.values()
            for key in ("seed", "players", "turnDays", "workerCount", "partitionSize", "warmupRuns",
                        "runtime", "operatingSystem", "inputSaveSha256"):
                assert baseline[key] == candidate[key], (players, seed, pair, key)
            assert baseline["seed"] == seed and baseline["players"] == players
            assert baseline["inputSaveSha256"]
            assert baseline["revision"] == manifest["baselineRevision"]
            assert candidate["revision"] == manifest["candidateRevision"]
            # Config hashes may differ because #28 adds explicit physical-unit fields.
            for variant, report in reports.items():
                assert not any(report[k] for k in ("detailsEnabled", "metricsEnabled", "fingerprintsEnabled"))
                rows = samples(report)
                assert report["openingTurnsMeasured"] == 0
                total = sum(r["totalMs"] for r in rows)
                totals[variant].append(total)
                outcomes[variant].append((report["revision"], report["configHash"],
                    report["inputSaveSha256"], tuple(r["population"] for r in rows)))
                performance.append(dict(players=players, seed=seed, pair=pair, variant=variant,
                    total_ms=total, ai_ms=sum(r["aiMs"] for r in rows),
                    preparation_ms=sum(r["prepareMs"] for r in rows),
                    execution_ms=sum(r["executeMs"] for r in rows),
                    allocated_bytes=sum(r["allocatedBytes"] for r in rows),
                    final_population=rows[-1]["population"], config_hash=report["configHash"]))
            pairs.append(totals["Candidate"][-1] / totals["Baseline"][-1] - 1)
        mean_regression = statistics.mean(pairs)
        run_means.append(mean_regression)
        print(f"{players}/{seed}: baseline={statistics.mean(totals['Baseline']):.1f} ms "
              f"candidate={statistics.mean(totals['Candidate']):.1f} ms mean paired change={mean_regression:+.2%}")
        for variant in totals:
            path = detail_root / f"{variant}-{players}-{seed}-pair1.json"
            if not path.exists():
                path = detail_root / f"{variant}-Details-{players}-{seed}.json"
            report = read(path)
            rows = samples(report)
            assert report["detailsEnabled"] and report["seed"] == seed and report["players"] == players
            detail_outcome = (report["revision"], report["configHash"], report["inputSaveSha256"],
                              tuple(r["population"] for r in rows))
            assert all(outcome == detail_outcome for outcome in outcomes[variant]), (
                "Detailed and timing runs diverged", variant, players, seed)
            assert report["finalFingerprint"], "Detailed run must retain a continuation fingerprint"
            camps = rows[-1]["camps"]
            assert len(camps) == players, "Retain extinct camps or supply an explicit cohort ledger"
            assert len({c["Player"] for c in camps}) == len(camps), "Aggregate multiple camps per tribe before computing quartiles"
            assert sum(c["linked"] for c in camps) == rows[-1]["population"]
            for route in ("Cultivation", "AnimalHusbandry"):
                selected = [c for c in camps if c["technology"] == route]
                values = [c["linked"] for c in selected]
                row = dict(variant=variant, players=players, seed=seed, route=route,
                    tribes=len(values), extinct=sum(v == 0 for v in values),
                    p25=percentile(values, .25), median=percentile(values, .5), p75=percentile(values, .75))
                populations.append(row)
                if variant == "Candidate":
                    print(f"  {route}: N={len(values)} p25/median/p75={row['p25']}/{row['median']}/{row['p75']}")
    aggregate = statistics.mean(run_means)
    print(f"Equal-world mean paired time change: {aggregate:+.2%}; audit required: {aggregate > .10}")
    write_csv(output_root / "performance.csv", performance)
    write_csv(output_root / "populations.csv", populations)
    (output_root / "summary.json").write_text(json.dumps(dict(
        mean_time_change=aggregate, audit_required=aggregate > .10,
        aggregation="equal weight per world, arithmetic mean of paired candidate/baseline time ratios minus one",
        population="linked people per tribe at 25 post-global-opening turns; includes extinct tribes",
        pairs=manifest["pairs"]), indent=2), encoding="utf-8")


if __name__ == "__main__":
    main(*(pathlib.Path(arg) for arg in sys.argv[1:]))
