"""Validate paired physical horizons and publish compact reproducible measurements."""
import csv
import hashlib
import json
from pathlib import Path

root = Path('TestResults/AgriculturalTechnology')
rows = json.loads((root / 'results.json').read_text())
labor = json.loads((root / 'labor.json').read_text())
manifest = json.loads((root / 'manifest.json').read_text())
key = lambda r: (r['seed'], r['treatment'], r['fixedFieldStaffing'])
expected = len(manifest['seeds']) * len(manifest['treatments']) * 2
assert len(rows) == len(labor) == expected, (len(rows), len(labor), expected)
assert len({key(r) for r in rows}) == expected
work = {key(r): r for r in labor}
for checkpoint in manifest['checkpoints']:
    assert hashlib.sha256((root / checkpoint['path']).read_bytes()).hexdigest() == checkpoint['sha256']
for name, digest in manifest['sourceFiles'].items():
    assert hashlib.sha256(Path(name).read_bytes()).hexdigest() == digest, name
for seed in manifest['seeds']:
    paired = [r for r in rows if r['seed'] == seed]
    reference = paired[0]
    for r in paired:
        assert r['elapsedYears'] >= 600
        for field in ('startDays', 'endDays', 'durationDays', 'seasonalExposureDays'):
            assert r[field] == reference[field], (seed, field)
        assert abs(r['cropReceipts'] + r['wildReceipts'] - r['farmerFoodReceipts'] - r['gathererFoodReceipts']) < 1e-5
for r in rows:
    r.update(work[key(r)])
    r['plantFoodReceipts'] = r['cropReceipts'] + r['wildReceipts']
    r['measuredWorkerYearsPerThousandPlantFood'] = 1000 * r['measuredAgricultureAndCraftWorkerYears'] / r['plantFoodReceipts']
fields = [k for k, v in rows[0].items() if not isinstance(v, (list, dict))]
with (root / 'summary.csv').open('w', newline='') as output:
    writer = csv.DictWriter(output, fieldnames=fields, extrasaction='ignore')
    writer.writeheader()
    writer.writerows(rows)

def quantile(values, p):
    if not values:
        return 0.0
    s = sorted(values)
    idx = p * (len(s) - 1)
    lo = int(idx)
    hi = min(len(s) - 1, lo + 1)
    return s[lo] + (idx - lo) * (s[hi] - s[lo])

summary = []
for fixed in (False, True):
    baseline = [r for r in rows if r['fixedFieldStaffing'] == fixed and r['treatment'] == 'none']
    combined_soil = [r for r in rows if r['fixedFieldStaffing'] == fixed and r['treatment'] == 'combined-soil']
    baseline_disease = sum(r.get('diseaseDeaths', 0.0) for r in baseline)
    combined_soil_disease = sum(r.get('diseaseDeaths', 0.0) for r in combined_soil)
    for treatment in manifest['treatments']:
        group = [r for r in rows if r['fixedFieldStaffing'] == fixed and r['treatment'] == treatment]
        sum_field = lambda data, field: sum(r.get(field, 0.0) for r in data)
        food = sum_field(group, 'plantFoodReceipts')
        effort = sum_field(group, 'measuredAgricultureAndCraftWorkerYears')
        baseline_food = sum_field(baseline, 'plantFoodReceipts')
        baseline_effort = sum_field(baseline, 'measuredAgricultureAndCraftWorkerYears')
        pops = [p for r in group for p in r.get('campPopulations', [])]
        disease = sum_field(group, 'diseaseDeaths')
        san_cov_mean = (sum_field(group, 'meanSanitationCoverage') / len(group)) if group else 0.0
        summary.append(dict(fixedFieldStaffing=fixed, treatment=treatment,
            plantFood=food, cropFood=sum_field(group, 'cropReceipts'), wildFood=sum_field(group, 'wildReceipts'),
            foodChangePercent=100*(food/baseline_food-1), measuredWorkerYears=effort,
            workerYearsSaved=baseline_effort-effort,
            laborPerFoodChangePercent=100*((effort/food)/(baseline_effort/baseline_food)-1),
            irrigationMaterials=sum_field(group, 'irrigationHousingMaterials'),
            irrigationConstructionWorkerYears=sum_field(group, 'constructionWorkerYears'),
            irrigationUpkeepWorkerYears=sum_field(group, 'upkeepWorkerYears'),
            deliveredWater=sum_field(group, 'deliveredWater'),
            foodReserves=sum_field(group, 'foodReserves'),
            campCount=len(pops),
            campPopP25=quantile(pops, 0.25),
            campPopMedian=quantile(pops, 0.50),
            campPopP75=quantile(pops, 0.75),
            campPopMean=(sum(pops) / len(pops)) if pops else 0.0,
            campPopMax=max(pops) if pops else 0,
            campsAbove1000=sum(1 for p in pops if p > 1000),
            diseaseDeaths=disease,
            diseaseDeathsAverted=baseline_disease - disease,
            diseaseDeathsAvertedVsCombinedSoil=(combined_soil_disease - disease) if combined_soil else 0.0,
            sanitationUnits=sum_field(group, 'sanitationUnits'),
            sanitationWorkerYears=sum_field(group, 'sanitationWorkerYears'),
            sanitationHousingMaterials=sum_field(group, 'sanitationHousingMaterials'),
            meanSanitationCoverage=san_cov_mean))
(root / 'summary.json').write_text(json.dumps(summary, indent=2) + '\n')
print(f'PASS: {expected} paired worlds; >=600 smoothed years; exact physical/seasonal matches; source/checkpoint hashes; conserved crop/wild attribution.')
for r in summary:
    print(f"fixed={r['fixedFieldStaffing']} {r['treatment']:24} food={r['foodChangePercent']:+.3f}% measured labor/food={r['laborPerFoodChangePercent']:+.3f}% pop(p25/med/p75/mean)={r['campPopP25']:.0f}/{r['campPopMedian']:.0f}/{r['campPopP75']:.0f}/{r['campPopMean']:.1f} >1k={r['campsAbove1000']} disAverted={r['diseaseDeathsAverted']:+.1f}")
