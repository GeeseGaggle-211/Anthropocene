#!/usr/bin/env python3
"""Reduce the matched shared-allocation audit into reproducible census and timing tables."""
import argparse
import csv
import hashlib
import json
import pathlib
import statistics

REPO = pathlib.Path(__file__).resolve().parents[2]
WORLDS = ('C1', 'C2', 'C3', 'P1', 'A1')
VARIANTS = ('control', 'candidate')
TECHNOLOGIES = ('Cultivation', 'AnimalHusbandry', 'Watercraft')


def percentile(values, fraction):
    values = sorted(values)
    if not values:
        return None
    index = (len(values) - 1) * fraction
    lo = int(index)
    hi = min(lo + 1, len(values) - 1)
    return values[lo] + (values[hi] - values[lo]) * (index - lo)


def census(camps):
    camps = [c for c in camps if c['linked'] > 0]
    residents = sum(c['Population'] for c in camps)
    linked = sum(c['linked'] for c in camps)
    animals = sum(c['managedAnimals'] for c in camps)
    receipt = sum(c['foodYield'] for c in camps)
    sustained = sum(c['sustainedAnimalFood'] for c in camps)
    return dict(tribes=len(camps), residents=residents, linkedPopulation=linked,
                residentWeightedQoL=sum(c['QoL'] * c['Population'] for c in camps) / residents if residents else None,
                medianTribePopulation=percentile([c['linked'] for c in camps], .5),
                p25TribePopulation=percentile([c['linked'] for c in camps], .25),
                p75TribePopulation=percentile([c['linked'] for c in camps], .75),
                medianResidentPopulation=percentile([c['Population'] for c in camps], .5),
                p25ResidentPopulation=percentile([c['Population'] for c in camps], .25),
                p75ResidentPopulation=percentile([c['Population'] for c in camps], .75),
                managedAnimals=animals, animalsPerLinkedPerson=animals / linked if linked else None,
                animalsPerResident=animals / residents if residents else None,
                herdOwningTribes=sum(c['managedAnimals'] > 0 for c in camps),
                medianAnimals=percentile([c['managedAnimals'] for c in camps], .5),
                p25Animals=percentile([c['managedAnimals'] for c in camps], .25),
                p75Animals=percentile([c['managedAnimals'] for c in camps], .75),
                p25QoL=percentile([c['QoL'] for c in camps], .25),
                medianQoL=percentile([c['QoL'] for c in camps], .5),
                p75QoL=percentile([c['QoL'] for c in camps], .75),
                targetedTribes=sum(c.get('HerdTargetSet', False) for c in camps),
                zeroBreedingTribes=sum(c.get('HerdTargetSet', False) and c.get('ManagedBreedingIntensity', .55) == 0 for c in camps),
                p90HerderShare=percentile([sum(a['Population'] for a in c['assignments'] if a['Role'] == 'Herder') / c['Population'] for c in camps if c['Population'] > 0], .9),
                allResidentHerdingTribes=sum(c['Population'] > 0 and sum(a['Population'] for a in c['assignments'] if a['Role'] == 'Herder') == c['Population'] for c in camps),
                maxAnimals=max((c['managedAnimals'] for c in camps), default=0),
                tribesWithCultivation=sum(c['holdsCultivation'] for c in camps),
                tribesWithHusbandry=sum(c['holdsHusbandry'] for c in camps),
                tribesWithFields=sum(c['meanFieldQ'] > 1e-6 for c in camps),
                villages=sum(c['IsVillage'] for c in camps),
                farmers=sum(a['Population'] for c in camps for a in c['assignments'] if a['Role'] == 'Farmer'),
                herders=sum(a['Population'] for c in camps for a in c['assignments'] if a['Role'] == 'Herder'),
                residentWeightedMeanQ=sum(c['Population'] * c['meanFieldQ'] for c in camps) / residents if residents else None,
                foodReceipts=receipt, sustainedAnimalFood=sustained,
                sustainedAnimalReceiptShare=sustained / receipt if receipt else None,
                slaughterFood=sum(c['slaughterFood'] for c in camps))


def write_csv(path, rows):
    if not rows:
        return
    with path.open('w', newline='') as stream:
        writer = csv.DictWriter(stream, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)


def outcomes(root):
    runs = {}
    for variant in VARIANTS:
        for world in WORLDS:
            data = json.loads((root / 'gameplay' / f'{variant}-{world}.json').read_text())
            samples = [s for s in data['samples'] if s['phase'] == 'post-opening']
            assert len(samples) == 50 and data['players'] == 80
            assert data['herdLabourSwitch'] == data['qolMortalitySwitch'] == data['fissionSwitch'] == 1
            runs[variant, world] = samples
            checkpoint = root / 'gameplay' / f'control-{world}-opening.save'
            digest = hashlib.sha256(checkpoint.read_bytes()).hexdigest()
            assert (data['inputSaveSha256'] or data['finishedSaveSha256']).lower() == digest
    rows, pooled, strata, trajectories, demographics, settlements = [], [], [], [], [], []
    for variant in VARIANTS:
        for turns in (25, 50):
            all_camps = []
            for world in WORLDS:
                samples = runs[variant, world][:turns]
                sample = samples[-1]
                all_camps.extend(sample['camps'])
                assert sum(c['linked'] for c in sample['camps']) == sample['population']
                for technology in TECHNOLOGIES:
                    selected = [c for c in sample['camps'] if c['technology'] == technology]
                    rows.append(dict(variant=variant, world=world, postOpeningTurns=turns,
                                     primaryTechnology=technology, **census(selected)))
                demographics.append(dict(variant=variant, world=world, postOpeningTurns=turns,
                                         finalPopulation=sample['population'], finalTribes=sample['campCount'],
                                         fissions=sum(s['fissionCount'] for s in samples),
                                         humanDeaths=sum(s['humanDeaths'] for s in samples),
                                         animalDeaths=sum(s['animalDeaths'] for s in samples),
                                         animalSlaughtered=sum(s['animalSlaughtered'] for s in samples)))
            for technology in TECHNOLOGIES:
                selected = [c for c in all_camps if c['technology'] == technology]
                pooled.append(dict(variant=variant, postOpeningTurns=turns, primaryTechnology=technology, **census(selected)))
                for label, predicate in (
                    ('all camps/settlements', lambda c: True),
                    ('villages', lambda c: c['IsVillage']),
                    ('mobile camps', lambda c: not c['IsVillage']),
                ):
                    stats = census([c for c in selected if predicate(c)])
                    settlements.append(dict(
                        variant=variant, postOpeningTurns=turns, primaryTechnology=technology,
                        settlementSubset=label, settlements=stats['tribes'],
                        p25Residents=stats['p25ResidentPopulation'],
                        medianResidents=stats['medianResidentPopulation'],
                        p75Residents=stats['p75ResidentPopulation'],
                        p25LinkedPopulation=stats['p25TribePopulation'],
                        medianLinkedPopulation=stats['medianTribePopulation'],
                        p75LinkedPopulation=stats['p75TribePopulation'],
                        p25Animals=stats['p25Animals'], medianAnimals=stats['medianAnimals'], p75Animals=stats['p75Animals'],
                        p25QoL=stats['p25QoL'], medianQoL=stats['medianQoL'], p75QoL=stats['p75QoL']))
            husbandry = [c for c in all_camps if c['technology'] == 'AnimalHusbandry']
            filters = {
                'without Cultivation technology': lambda c: not c['holdsCultivation'],
                'with Cultivation technology': lambda c: c['holdsCultivation'],
                'with fields': lambda c: c['meanFieldQ'] > 1e-6,
                'without fields': lambda c: c['meanFieldQ'] <= 1e-6,
                'villages': lambda c: c['IsVillage'],
                'mobile camps': lambda c: not c['IsVillage'],
                'herd owners': lambda c: c['managedAnimals'] > 0,
                'herdless camps': lambda c: c['managedAnimals'] == 0,
            }
            for label, predicate in filters.items():
                strata.append(dict(variant=variant, postOpeningTurns=turns, husbandrySubset=label,
                                   **census([c for c in husbandry if predicate(c)])))
        for turns in range(1, 51):
            camps = [c for world in WORLDS for c in runs[variant, world][turns - 1]['camps']]
            for technology in TECHNOLOGIES:
                trajectories.append(dict(variant=variant, postOpeningTurns=turns, primaryTechnology=technology,
                                         **census([c for c in camps if c['technology'] == technology])))
    write_csv(root / 'census-by-world.csv', rows)
    write_csv(root / 'census-pooled.csv', pooled)
    write_csv(root / 'husbandry-subsets.csv', strata)
    write_csv(root / 'trajectories.csv', trajectories)
    write_csv(root / 'demography.csv', demographics)
    write_csv(root / 'settlement-populations.csv', settlements)
    return dict(pooledCensus=pooled, worldCensus=rows, husbandrySubsets=strata,
                demography=demographics, settlementPopulations=settlements)


def performance(root):
    rows, comparison = [], []
    checkpoint_hashes = set()
    for turns in (25, 50):
        files = sorted((root / 'performance').glob(f'control-{turns}-*.json'))
        if not files:
            continue
        duration_rows = []
        for baseline in files:
            pair = int(baseline.stem.rsplit('-', 1)[1])
            for variant in VARIANTS:
                path = root / 'performance' / f'{variant}-{turns}-{pair}.json'
                data = json.loads(path.read_text()); samples = data['samples']
                assert len(samples) == turns and all(s['phase'] == 'post-opening' for s in samples)
                assert data['loadedFromSave'] and not data['metricsEnabled'] and not data['detailsEnabled']
                assert data['workerCount'] == 1 and data['warmupRuns'] == 1
                checkpoint_hashes.add(data['inputSaveSha256'])
                row = dict(variant=variant, postOpeningTurns=turns, pair=pair)
                for name in ('totalMs', 'aiMs', 'prepareMs', 'executeMs', 'allocatedBytes', 'gcGen0Collections', 'gcGen1Collections', 'gcGen2Collections'):
                    row[name] = sum(s[name] for s in samples)
                row['peakManagedHeapBytes'] = max(s['managedHeapBytes'] for s in samples)
                row['campTurns'] = sum(s['priorCampCount'] for s in samples)
                row['finalTribes'] = samples[-1]['campCount']; row['finalPopulation'] = samples[-1]['population']
                row['totalMsPerCampTurn'] = row['totalMs'] / row['campTurns']
                row['allocatedBytesPerCampTurn'] = row['allocatedBytes'] / row['campTurns']
                row['aiMsPerCampTurn'] = row['aiMs'] / row['campTurns']
                row['prepareMsPerCampTurn'] = row['prepareMs'] / row['campTurns']
                rows.append(row); duration_rows.append(row)
        for metric in ('totalMs', 'aiMs', 'prepareMs', 'executeMs', 'allocatedBytes', 'peakManagedHeapBytes', 'totalMsPerCampTurn', 'allocatedBytesPerCampTurn', 'aiMsPerCampTurn', 'prepareMsPerCampTurn'):
            before = [r[metric] for r in duration_rows if r['variant'] == VARIANTS[0]]
            after = [r[metric] for r in duration_rows if r['variant'] == VARIANTS[1]]
            ratios = [a / b for a, b in zip(after, before)]
            comparison.append(dict(postOpeningTurns=turns, metric=metric, pairs=len(before),
                                   baselineMedian=statistics.median(before), correctedMedian=statistics.median(after),
                                   medianPairedChangePercent=(statistics.median(ratios) - 1) * 100,
                                   minimumPairedChangePercent=(min(ratios) - 1) * 100,
                                   maximumPairedChangePercent=(max(ratios) - 1) * 100))
        for variant in VARIANTS:
            states = {(r['finalPopulation'], r['finalTribes'], r['campTurns'])
                      for r in duration_rows if r['variant'] == variant}
            assert len(states) == 1, 'Repeated timing runs changed their outcome/workload.'
    assert len(checkpoint_hashes) <= 1, 'Performance variants did not use identical saved bytes.'
    write_csv(root / 'timing-runs.csv', rows)
    write_csv(root / 'timing-comparison.csv', comparison)
    return dict(inputSaveSha256=next(iter(checkpoint_hashes), None), runs=rows, comparisons=comparison)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--input', default='TestResults/SharedAllocation')
    parser.add_argument('--outcomes-only', action='store_true', help='Summarize while sequential performance runs are still in progress.')
    args = parser.parse_args(); root = (REPO / args.input).resolve()
    summary = outcomes(root)
    if not args.outcomes_only:
        summary['performance'] = performance(root)
    summary['simulationSourceDigests'] = {}
    for variant in VARIANTS:
        source = json.loads((root / f'{variant}-provenance.json').read_text())
        digest = hashlib.sha256(json.dumps(source, sort_keys=True, separators=(',', ':')).encode()).hexdigest()
        summary['simulationSourceDigests'][variant] = digest
    (root / 'summary.json').write_text(json.dumps(summary, indent=2) + '\n')
    print(json.dumps(dict(pooledCensus=summary['pooledCensus'],
                          performance=summary.get('performance', {}).get('comparisons', [])), indent=2))


if __name__ == '__main__':
    main()
