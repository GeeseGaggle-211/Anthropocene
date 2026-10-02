#!/usr/bin/env python3
"""Reproduce historical crop-moisture previews from the preserved pre-change source."""
import argparse
import hashlib
import json
import pathlib
import shutil
import subprocess

REPO = pathlib.Path(__file__).resolve().parents[2]
SIM = REPO / 'Assets/_Anthropocene/Simulation'


def replace(path, old, new, count=1):
    body = path.read_text()
    assert body.count(old) == count, (path, old, body.count(old), count)
    path.write_text(body.replace(old, new))


def crop_curve(source, threshold):
    ecology = source / 'EcologyMath.cs'
    replace(ecology, 'double cultivationIntensity, SimulationConfig config)\n        {\n            double q = Numbers.Clamp(cultivationIntensity, 0, 1), capacity',
            'double cultivationIntensity, SimulationConfig config, double moisture = 1)\n        {\n            double q = Numbers.Clamp(cultivationIntensity, 0, 1), capacity')
    replace(ecology, 'return wildNatural + (capacity - wildNatural) * q * q;',
            f'return wildNatural + (capacity - wildNatural) * q * q * Math.Min(1, Math.Pow(Math.Max(0, moisture) / {threshold}, 2));')
    replace(ecology, 'double biomassRecoveryPerDay, double operatingFraction)',
            'double biomassRecoveryPerDay, double operatingFraction, double moisture = 1)')
    replace(ecology, '* Numbers.Clamp(operatingFraction, 0, 1);',
            f'* Numbers.Clamp(operatingFraction, 0, 1) * Math.Min(1, Math.Pow(Math.Max(0, moisture) / {threshold}, 2));')
    lazy = source / 'LazyEcology.cs'
    replace(lazy, 'ecologicalWildFood, Biomass, q, w.Config);', 'ecologicalWildFood, Biomass, q, w.Config, s.Moisture);')
    replace(lazy, 'operatingFraction ?? t.CultivationOperatingFraction);', 'operatingFraction ?? t.CultivationOperatingFraction, s.Moisture);')
    replace(lazy, 'naturalCapacity.Biomass, previousQ, w.Config);', 'naturalCapacity.Biomass, previousQ, w.Config, tile.Seasons[season].Moisture);')
    replace(lazy, 'naturalCapacity.Biomass, nextQ, w.Config);', 'naturalCapacity.Biomass, nextQ, w.Config, tile.Seasons[season].Moisture);')
    agents = source / 'Agents.cs'
    replace(agents, 'wildFoodEcological, biomassCapacity, Cultivation, config);', 'wildFoodEcological, biomassCapacity, Cultivation, config, ecology.Moisture);')
    replace(agents, 'ecology.BiomassRecoveryPerDay, tile.CultivationOperatingFraction);', 'ecology.BiomassRecoveryPerDay, tile.CultivationOperatingFraction, ecology.Moisture);')
    replace(source / 'Fission.cs', 't.Seasons[s].BiomassRecoveryPerDay, 1);', 't.Seasons[s].BiomassRecoveryPerDay, 1, t.Seasons[s].Moisture);')


def validate(output):
    def rows(variant):
        return [json.loads(line) for line in (output / (variant + '-sites.jsonl')).read_text().splitlines()]
    def key(row):
        return row['site'], row['q'], row['startingAnimals']
    current = {key(row): row for row in rows('current')}
    count = 0
    for variant in ('crop-m035', 'crop-m050'):
        for row in rows(variant):
            before = current[key(row)]
            assert row['biomassPerTile'] == before['biomassPerTile']; count += 1
            assert 0 <= row['foodCapacityPerTile'] <= row['biomassPerTile'] + 1e-7; count += 1
            assert abs(row['currentMilkAtFullManagement'] - before['currentMilkAtFullManagement']) < 1e-6; count += 1
            if row['q'] == 0 or row['soilMoisture'] >= .5:
                for field in ('foodCapacityPerTile', 'currentForagePerYear'):
                    assert abs(row[field] - before[field]) < 1e-6; count += 1
            # At q=0 physical wild food is unchanged, but the plan can change
            # because it also evaluates the now-less-productive field investment.
            if row['soilMoisture'] >= .5:
                for field in ('herders', 'target', 'farmers', 'expansionFarmers'):
                    assert row[field] == before[field]; count += 1
    changed = sum(row['herders'] != current[key(row)]['herders'] for row in rows('stock-credit-fixed'))
    result = {'assertionsPassed': count, 'controlledCases': len(current), 'variants': 4,
              'stockCreditHerderChoicesChanged': changed}
    (output / 'checks.json').write_text(json.dumps(result, indent=2) + '\n')
    print(result, flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', default='TestResults/MoistureCurveAudit')
    parser.add_argument('--source', default='TestResults/CropHerdBalance/pre-moisture-source',
                        help='Pre-implementation source snapshot; current production already contains the correction.')
    args = parser.parse_args()
    original_source = (REPO / args.source).resolve()
    if not (original_source / 'EcologyMath.cs').exists():
        raise RuntimeError('Missing pre-change source snapshot; provide --source rather than using corrected production as a control.')
    output = (REPO / args.output).resolve()
    output.mkdir(parents=True, exist_ok=True)
    originals = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in SIM.glob('*.cs')}
    provenance = {'productionSourceHashes': originals, 'baselineSource': str(original_source), 'variants': {}}
    for variant, threshold in [('current', None), ('stock-credit-fixed', None), ('crop-m035', .35), ('crop-m050', .5)]:
        source = output / (variant + '-source')
        source.mkdir(exist_ok=True)
        for p in original_source.glob('*.cs'):
            shutil.copyfile(p, source / p.name)
        if threshold is not None:
            crop_curve(source, threshold)
        if variant == 'stock-credit-fixed':
            replace(source / 'HerdLabour.cs', 'd.Biological - c.Food', 'd.Biological')
        binary = output / variant
        with (output / (variant + '-build.log')).open('w') as log:
            subprocess.run(['dotnet', 'build', 'Tools/PerformanceAudit/HerdEcologyProbe.csproj', '-c', 'Release', '-o', str(binary), '-p:SimulationSource=' + str(source)],
                           cwd=REPO, stdout=log, stderr=subprocess.STDOUT, check=True)
        with (output / (variant + '-sites.jsonl')).open('w') as rows:
            subprocess.run(['dotnet', str(binary / 'HerdEcologyProbe.dll'), 'moisture-sites'], cwd=REPO, stdout=rows, check=True)
        provenance['variants'][variant] = {'cropThreshold': threshold, 'rows': 60,
            'sourceHashes': {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in source.glob('*.cs')}}
        print(variant, 'finished', flush=True)
    assert originals == {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in SIM.glob('*.cs')}, 'Production source changed during audit.'
    (output / 'provenance.json').write_text(json.dumps(provenance, indent=2) + '\n')
    validate(output)


if __name__ == '__main__':
    main()
