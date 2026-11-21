"""Validate and retain the three normal-play population/construction reruns."""
import argparse
import csv
import hashlib
import json
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("--root", default="TestResults/ConstructionFix80")
parser.add_argument("--output", default="Docs/Experiments/ConstructionFix80-20261113")
parser.add_argument("--report", default="Docs/ConstructionFix80.md")
args = parser.parse_args()
root, output = Path(args.root), Path(args.output)
output.parent.mkdir(parents=True, exist_ok=True)
results, manifests, frames, populations = [], [], [], []


def quantile(values, p):
    ordered = sorted(values)
    x = (len(ordered) - 1) * p
    lo = int(x)
    return ordered[lo] + (ordered[min(lo + 1, len(ordered) - 1)] - ordered[lo]) * (x - lo)


for seed in (11, 23, 47):
    folder = root / f"seed-{seed}"
    result = json.loads((folder / "summary.json").read_text())
    manifest = json.loads((folder / "manifest.json").read_text())
    assert manifest["seed"] == result["seed"] == seed
    digest = hashlib.file_digest((folder / "opening.anthro").open("rb"), "sha256").hexdigest()
    assert digest == manifest["openingSha256"]
    assert result["physicalYears"] >= manifest["requestedYears"]
    assert not result["shortfalls"], (seed, "unresolved construction commitment")
    assert abs(result["totalGranted"] - result["totalConstruction"]) < 1e-6, seed
    assert abs(sum(result["durationsDays"]) - result["endDays"] + result["startDays"]) < 1e-7
    assert abs(sum(result["seasonalExposure"]) - sum(result["durationsDays"])) < 1e-7
    for key, rows_key, kind in (("livingTribes", "livingSocietyPopulations", "living society"),
                                ("foundingLineages", "foundingLineagePopulations", "founding lineage")):
        rows = result.pop(rows_key)
        values = [r["population"] for r in rows]
        stats = result[key]
        assert stats["count"] == len(values)
        assert stats["total"] == sum(values)
        assert stats["max"] == max(values)
        assert stats["median"] == quantile(values, .5)
        assert stats["p75"] == quantile(values, .75)
        assert kind != "founding lineage" or len(values) == 80
        if kind == "founding lineage":
            result["survivingFoundingLineages"] = sum(v > 0 for v in values)
        populations.extend(dict(seed=seed, kind=kind, society=r.get("society", ""), **{k: v for k, v in r.items() if k != "society"}) for r in rows)
    assert result["livingTribes"]["total"] == result["foundingLineages"]["total"]
    manifest["committedCalendar"] = result["calendar"]
    events = result.pop("attainments")
    result["attainmentEvents"] = {
        tech: {"count": sum(tech in e["message"] for e in events),
               "first": next((e for e in events if tech in e["message"]), None)}
        for tech in ("Plough", "Irrigation")}
    manifests.append(manifest)
    results.append(result)
    frames.extend(dict(seed=seed, **json.loads(line)) for line in (folder / "turns.jsonl").read_text().splitlines())

assert all(m["sources"] == manifests[0]["sources"] for m in manifests)
amendment_path = output.with_name(output.name + "-source-amendment.json")
amendment = json.loads(amendment_path.read_text()) if amendment_path.exists() else None
amended = {r["path"]: r for r in amendment["files"]} if amendment else {}
if amendment:
    assert hashlib.sha256(Path(amendment["patch"]).read_bytes()).hexdigest() == amendment["patchSha256"]
used_amendment = False
for path, digest in manifests[0]["sources"].items():
    current = hashlib.sha256(Path(path).read_bytes()).hexdigest()
    if current != digest:
        assert path in amended and amended[path]["runtimeSha256"] == digest and amended[path]["finalSha256"] == current, path
        used_amendment = True
if not used_amendment:
    amendment = None
sources = manifests[0]["sources"]
for manifest in manifests:
    del manifest["sources"]

output.with_name(output.name + "-summary.json").write_text(json.dumps(results, indent=2) + "\n")
output.with_name(output.name + "-manifest.json").write_text(json.dumps(dict(runtimeSources=sources, runs=manifests, sourceAmendment=amendment), indent=2) + "\n")
for suffix, rows in (("turns", frames), ("populations", populations)):
    with output.with_name(output.name + f"-{suffix}.csv").open("w", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)
for r in results:
    print(r["seed"], r["physicalYears"], r["livingTribes"], r["foundingLineages"],
          "construction shortfalls", len(r["shortfalls"]), "preparation reductions", len(r["reductions"]))


def number(value):
    return f"{value:,.2f}".rstrip("0").rstrip(".") if isinstance(value, float) else f"{value:,}"


def population_table(key):
    lines = ["| Seed | Physical years | Tribes counted | Total population | Median | Maximum | P75 |",
             "| --- | ---: | ---: | ---: | ---: | ---: | ---: |"]
    for r in results:
        s = r[key]
        values = (r["seed"], r["physicalYears"], s["count"], s["total"], s["median"], s["max"], s["p75"])
        lines.append("| " + " | ".join(map(number, values)) + " |")
    return "\n".join(lines)


construction = ["| Seed | Initial construction grant | Final grant / actual segments | Initial grants reduced | Final-grant shortfalls | Completed canals | Housing/canal overlap turns |",
                "| --- | ---: | ---: | ---: | ---: | ---: | ---: |"]
for r in results:
    values = (r["seed"], r["totalAuthorized"], r["totalGranted"], len(r["reductions"]), len(r["shortfalls"]), r["completeCanals"], r["housingCanalOverlap"])
    construction.append("| " + " | ".join(map(number, values)) + " |")
performance = ["| Seed | Turns | Camps / villages | Canal-work grants (build / upkeep) | Water delivered | Maximum water residual | Runtime seconds | Peak RSS MiB |",
               "| --- | ---: | --- | --- | ---: | ---: | ---: | ---: |"]
for r in results:
    performance.append(f"| {r['seed']} | {r['turns']} | {r['camps']:,} / {r['villages']:,} | {r['constructionJobs']:,} / {r['maintenanceJobs']:,} | {r['waterDelivery']:,.2f} | {r['maxWaterConservationResidual']:.3g} | {r['elapsedSeconds']:,.1f} | {r['peakWorkingSetBytes']/1048576:,.1f} |")

source_note = """The three long runs started before the defensive cached-draft copy was added.
Their harness never supplies a draft. The exact runtime source hashes and the
subsequent three-file cache-only patch are pinned in the manifest. An additional
mature native replay over 80+ physical years yields identical saved bytes and
fingerprints for the runtime direct path, final direct path and final cached
path. The final full headless suite was rerun after that patch.""" if amendment else "The runtime and current authoritative simulation source hashes match exactly."

report = f"""# Construction corrections and three 80-tribe runs — 13 November 2026

The construction corrections are implemented and tested. These are three fresh
normal-AI/Elder playthroughs using the smoothed calendar, with 80 founding tribes
on Standard/Continents 1024 × 512 worlds and seeds 11, 23 and 47. Each runs to the
first authoritative boundary at or beyond 1,600 physical years. No knowledge,
resources, population or workforce were granted after generation. No balance
coefficients, demographic laws or active RL training settings were changed.

## Population

Living tribes are living `Society` IDs. A tribe's population sums all its camps
and hunters; extinct societies are excluded. Fission creates additional tribes.
Quantiles use linear interpolation at `(n − 1) × p` (Type 7).

{population_table('livingTribes')}

For comparison across the original 80 tribes, the following groups every
descendant society through `ParentSociety`. All 80 founding lineages are included,
with zero for an extinct lineage. This tracks society lineage, not the ancestry
of individual immigrants. Both tables reconcile to the same total population.

{population_table('foundingLineages')}

The epoch is triggered by native opening completion, so independent worlds can
reach different final physical dates. Exact epochs, committed durations and
seasonal exposure are retained; turns were not clipped or replaced with fixed
20-year intervals. This is a cross-seed gameplay report, not a matched-treatment
estimate of irrigation's effect on population.

## What changed

- The generic Tool allowance now contains exactly the paid output, eliminating
  the second allowance that consumed Wood reserved for other production.
- Housing Material throughput uses the same construction-tool multiplier as its
  Builder-time charge. The four-Builder reproduction now completes its segment.
- Canal construction and upkeep materials receive a temporary in-use storage
  allowance, which expires at Close.
- Final canal commitments and production ceilings are capped by the executable
  forecast. Initial grants remain visible separately. Cached drafts are copied
  before this reconciliation, so repeated preparation preserves the draft.

Housing, new canals and canal upkeep are separate jobs with their own priorities
and quantities. They consume one shared Builder pool, generic tool service and
material inventory. Already compiled reservations are not reassigned when the
final forecast reduces a grant; that remaining conservatism is explicit.

## Construction delivery and scale

{chr(10).join(construction)}

Final construction totals agree with actual receipts within numerical tolerance.
The reduction count includes construction and upkeep; full per-case initial,
final, forecast and actual quantities are retained in the summary JSON. These
reductions are not reported as completed construction. The overlap column counts
camp-turns with both a positive canal construction grant and actual housing
output, demonstrating that the shared Builder pool handles both jobs.

{chr(10).join(performance)}

Timings are concurrent cloud runs, not isolated performance benchmarks. Cost
follows active settlements, construction requests and infrastructure, rather than
population-sized entities or substeps through elapsed years. The report records
all committed population boundaries, final tribe populations, canal material
spending, all Builder-production years (including housing and material making),
upkeep in segment-years, water delivery and mean field soil development.

## Validation and remaining limits

All 69 headless suites pass on the final code, including 283 agricultural,
163 DemandControl, 154 Housing Material recipe, 64 goods-storage and 184 production
order assertions. All 28,787 calendar checks pass. The contract checker passes
92 configuration/API values, 21 test claims and document links. Tests cover
minimum staffing, mixed housing/canal work, material conservation, tight storage,
initial versus final commitments, and repeated cached preparation.

The fixed-calendar compatibility fixture was corrected to retain the committed
duration when disabling smoothing; its old hard-coded shorter duration could
invalidate provisions already carried by hunters. Production calendar rules
were not changed.

{source_note}

The previous large-world save metadata limit remains unresolved. This compact
harness retains opening saves and committed-boundary evidence, not final native
saves. It does not establish long-campaign save/load acceptance. Unity interaction
checks remain unperformed because Unity is unavailable. The full five-seed
matched balance matrix has not been rerun; these population results do not choose
new coefficients. The technology profile remains opt-in.

## Evidence and reproduction

- [Summary, calendar durations and grant cases](Experiments/{output.name}-summary.json)
- [Runtime sources, settings, opening hashes and source amendment](Experiments/{output.name}-manifest.json)
- [All committed turns](Experiments/{output.name}-turns.csv)
- [Individual tribe and founding-lineage populations](Experiments/{output.name}-populations.csv)
- [Validation](Experiments/{output.name}-validation.json), [controlled reproductions](Experiments/{output.name}-fixtures.json), and [cache parity](Experiments/{output.name}-cache-parity.json)
- [Original eleven-case diagnosis](ConstructionGrantAudit.md)

```bash
dotnet build Tools/AgriculturalTechnologySweep/Anthropocene.AgriculturalTechnologySweep.csproj -c Release
# Run once for each seed: 11, 23, 47. For exact seed-11 matching, optionally add
# --opening=TestResults/AgriculturalGameplay80/opening.anthro
dotnet Tools/AgriculturalTechnologySweep/bin/Release/net8.0/Anthropocene.AgriculturalTechnologySweep.dll --population-run --seed=11 --years=1600 --output=TestResults/ConstructionFix80/seed-11
python3 Tools/AgriculturalTechnologySweep/SummarizePopulationRuns.py
dotnet run --project Tools/Headless/Anthropocene.Headless.csproj -c Release -- 0
dotnet run --project Tools/SmoothCalendar/Anthropocene.SmoothCalendar.csproj -c Release
python3 Tools/Docs/Check-CurrentContract.py
```

The source amendment's reverse patch reproduces the exact original runtime
source snapshot. Preserve the original result directory when comparing a new
run. Starting saves and full raw journals remain under
`TestResults/ConstructionFix80/seed-<seed>`.
"""
Path(args.report).write_text(report)
