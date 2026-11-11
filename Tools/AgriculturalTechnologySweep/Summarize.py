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
assert len(rows) == len(labor) == 130
assert len({key(r) for r in rows}) == 130
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
summary = []
for fixed in (False, True):
    for treatment in manifest['treatments']:
        group = [r for r in rows if r['fixedFieldStaffing'] == fixed and r['treatment'] == treatment]
        baseline = [r for r in rows if r['fixedFieldStaffing'] == fixed and r['treatment'] == 'none']
        sum_field = lambda data, field: sum(r[field] for r in data)
        food = sum_field(group, 'plantFoodReceipts')
        effort = sum_field(group, 'measuredAgricultureAndCraftWorkerYears')
        baseline_food = sum_field(baseline, 'plantFoodReceipts')
        baseline_effort = sum_field(baseline, 'measuredAgricultureAndCraftWorkerYears')
        summary.append(dict(fixedFieldStaffing=fixed, treatment=treatment,
            plantFood=food, cropFood=sum_field(group, 'cropReceipts'), wildFood=sum_field(group, 'wildReceipts'),
            foodChangePercent=100*(food/baseline_food-1), measuredWorkerYears=effort,
            workerYearsSaved=baseline_effort-effort,
            laborPerFoodChangePercent=100*((effort/food)/(baseline_effort/baseline_food)-1),
            irrigationMaterials=sum_field(group, 'irrigationHousingMaterials'),
            irrigationConstructionWorkerYears=sum_field(group, 'constructionWorkerYears'),
            irrigationUpkeepWorkerYears=sum_field(group, 'upkeepWorkerYears'),
            deliveredWater=sum_field(group, 'deliveredWater'),
            foodReserves=sum_field(group, 'foodReserves')))
(root / 'summary.json').write_text(json.dumps(summary, indent=2) + '\n')
print('PASS: 130 paired worlds; >=600 smoothed years; exact physical/seasonal matches; source/checkpoint hashes; conserved crop/wild attribution.')
for r in summary:
    print(f"fixed={r['fixedFieldStaffing']} {r['treatment']:15} food={r['foodChangePercent']:+.3f}% measured labor/food={r['laborPerFoodChangePercent']:+.3f}% labor saved={r['workerYearsSaved']:+.0f}")
