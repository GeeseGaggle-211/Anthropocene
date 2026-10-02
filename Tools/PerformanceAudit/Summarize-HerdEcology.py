#!/usr/bin/env python3
"""Summarize matched five-world outcomes; measurements are local and provisional."""
import argparse
import csv
import collections
import json
import pathlib
import statistics

WORLDS = ['C1', 'C2', 'C3', 'P1', 'A1']
VARIANTS = ['baseline', 'ecology-only', 'herd-only', 'both']
def quantile(values, fraction):
    values = sorted(values)
    if not values: return 0
    x = (len(values) - 1) * fraction
    i = int(x)
    return values[i] + (values[min(i + 1, len(values) - 1)] - values[i]) * (x - i)

def outcome(report):
    samples = report['samples']
    final = samples[-1]
    camps = final['camps']
    founders = {c['Id'] for c in samples[0]['camps']}
    first = [c for c in camps if c['technology'] == 'AnimalHusbandry' and c['Population'] > 0]
    owners = [c for c in first if c['managedAnimals'] > 0]
    post = [s for s in samples if s['phase'] == 'post-opening']
    opening = [s for s in samples if s['phase'] == 'opening'][-1]
    ever_owned = set()
    previous = {}
    zero_transitions = 0
    for sample in samples:
        for c in sample['camps']:
            count = c['managedAnimals']
            if previous.get(c['Id'], 0) > 0 and count == 0: zero_transitions += 1
            if count: ever_owned.add(c['Id'])
            previous[c['Id']] = count
    return dict(openingTurns=report['openingTurnsMeasured'], openingRoutes={tech: sum(c['technology'] == tech and c['Id'] in founders for c in opening['camps']) for tech in ['AnimalHusbandry', 'Cultivation', 'Watercraft']}, population=sum(c['Population'] for c in camps), camps=len(camps), hPopulation=sum(c['Population'] for c in first), hCamps=len(first), hOwners=len(owners), hAnimals=sum(c['managedAnimals'] for c in first), medianAnimalsPerResident=quantile([c['managedAnimals'] / c['Population'] for c in first], .5), medianAnimalsPerOwningResident=quantile([c['managedAnimals'] / c['Population'] for c in owners], .5), hQ25=quantile([c['QoL'] for c in first], .25), hQ50=quantile([c['QoL'] for c in first], .5), hHerderShare90=quantile([sum(a['Population'] for a in c['assignments'] if a['Role'] == 'Herder') / c['Population'] for c in first], .9), hFounders=sum(c['Id'] in founders for c in first), hHerdlessDaughtersNeverOwned=sum(c['Id'] not in founders and c['Id'] not in ever_owned for c in first), positiveToZero=zero_transitions, slaughtered=sum(s['animalSlaughtered'] for s in post), animalBirths=sum(s['animalBirths'] for s in post), animalDeaths=sum(s['animalDeaths'] for s in post), humanDeaths=sum(s['humanDeaths'] for s in post), humanBirths=sum(s['humanBirths'] for s in post), fissions=sum(s['fissionCount'] for s in post), hSustainedFood=sum(c['sustainedAnimalFood'] for c in first), hConsumedFood=sum(c['LastConsumed'] for c in first), hFoodYield=sum(c['foodYield'] for c in first), hAnimalFabric=sum(c['production'].get('AnimalFabric', 0) for c in first), hFieldQ50=quantile([c['meanFieldQ'] for c in first], .5), herdForageFulfilled=final['herdForageRealized'] / max(1, final['herdForageRequested']), herdWaterFulfilled=final['herdWaterRealized'] / max(1, final['herdWaterRequested']))

def habitats(root):
    rows = []
    for variant in VARIANTS:
        profile = 'baseline' if variant in ['baseline', 'herd-only'] else 'ecology-only'
        for world in WORLDS:
            report = json.loads((root / 'runs' / (variant + '-' + world + '.json')).read_text())
            width, height = report['worldWidth'], report['worldHeight']
            with (root / 'runs' / (profile + '-' + world + '-ecology.csv')).open() as stream:
                tiles = {int(t['tile']): t for t in csv.DictReader(stream)}
            for sample in [s for s in report['samples'] if s['phase'] == 'post-opening'][::10] + [report['samples'][-1]]:
                for camp in sample['camps']:
                    if camp['technology'] != 'AnimalHusbandry' or camp['Population'] <= 0: continue
                    tile = camp['endingSite']; q, r = tile % width, tile // width
                    near = [tile] + [(r + dr) * width + (q + dq) % width for dq, dr in [(1, 0), (1, -1), (0, -1), (-1, 0), (-1, 1), (0, 1)] if 0 <= r + dr < height]
                    land = [tiles[i] for i in near if i in tiles]
                    env = collections.Counter(t['environment'] for t in land)
                    water = any(t['water'] == '1' for t in land)
                    if not land: category = 'aquatic fringe'
                    elif env['Desert'] >= len(land) / 2: category = 'watered desert' if water else 'dry desert'
                    elif env['Grassland'] >= len(land) / 2: category = 'watered grassland' if water else 'grassland away from water'
                    elif env['Temperate'] + env['Tropical'] >= len(land) / 2: category = 'temperate/tropical'
                    else: category = 'mixed/cold'
                    rows.append(dict(variant=variant, world=world, turn=sample['turn'], final=sample is report['samples'][-1], category=category,
                        **{k: camp[k] for k in ['Id', 'Population', 'managedAnimals', 'QoL', 'meanFieldQ', 'sustainedAnimalFood', 'LastConsumed', 'foodYield']},
                        herders=sum(a['Population'] for a in camp['assignments'] if a['Role'] == 'Herder'), farmers=sum(a['Population'] for a in camp['assignments'] if a['Role'] == 'Farmer'),
                        meanBiomass=statistics.mean(float(t['biomass']) for t in land) if land else 0, meanMoisture=statistics.mean(float(t['moisture']) for t in land) if land else 0))
    summary = {}
    for variant in VARIANTS:
        summary[variant] = {}
        for category in sorted({r['category'] for r in rows}):
            camps = [r for r in rows if r['variant'] == variant and r['category'] == category and r['final']]
            if not camps: continue
            owners = [r for r in camps if r['managedAnimals'] > 0]
            summary[variant][category] = dict(camps=len(camps), owners=len(owners), population=sum(r['Population'] for r in camps), animals=sum(r['managedAnimals'] for r in camps),
                medianAnimalsPerResident=quantile([r['managedAnimals'] / r['Population'] for r in camps], .5), medianAnimalsPerOwningResident=quantile([r['managedAnimals'] / r['Population'] for r in owners], .5),
                medianHerderShare=quantile([r['herders'] / r['Population'] for r in camps], .5), medianFarmerShare=quantile([r['farmers'] / r['Population'] for r in camps], .5),
                q50=quantile([r['meanFieldQ'] for r in camps], .5), qol50=quantile([r['QoL'] for r in camps], .5),
                moisture50=quantile([r['meanMoisture'] for r in camps], .5), sustainedShareOfFoodInflow=sum(r['sustainedAnimalFood'] for r in camps) / max(1, sum(r['foodYield'] for r in camps)))
    (root / 'habitat-camps.json').write_text(json.dumps(rows, indent=2) + '\n')
    (root / 'habitat-summary.json').write_text(json.dumps(summary, indent=2) + '\n')

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('root', nargs='?', default='TestResults/HerdEcology')
    args = parser.parse_args()
    root = pathlib.Path(args.root)
    result = {'worlds': {v: {w: outcome(json.loads((root / 'runs' / (v + '-' + w + '.json')).read_text())) for w in WORLDS} for v in VARIANTS}}
    profiles = {}
    for v in ['baseline', 'ecology-only']:
        profiles[v] = {}
        for w in WORLDS:
            path = root / 'runs' / (v + '-' + w + '-ecology.csv')
            if not path.exists(): continue
            groups = {'fertile': [], 'dryDesert': [], 'waterDesert': [], 'waterFertile': []}
            with path.open() as stream:
                for t in csv.DictReader(stream):
                    b, f = float(t['biomass']), float(t['food'])
                    if t['anchor'] == '1': groups['fertile'].append((b, f))
                    if t['environment'] == 'Desert' and t['relief'] in ['Lowland', 'Hills']: groups['waterDesert' if t['water'] == '1' else 'dryDesert'].append((b, f))
                    elif t['water'] == '1' and t['environment'] in ['Temperate', 'Tropical'] and t['relief'] in ['Lowland', 'Hills']: groups['waterFertile'].append((b, f))
            profiles[v][w] = {name: dict(tiles=len(items), meanBiomass=statistics.mean(x[0] for x in items) if items else 0, meanFood=statistics.mean(x[1] for x in items) if items else 0, food10=quantile([x[1] for x in items], .1), food50=quantile([x[1] for x in items], .5), food90=quantile([x[1] for x in items], .9)) for name, items in groups.items()}
    result['profiles'] = profiles
    habitats(root)
    (root / 'summary.json').write_text(json.dumps(result, indent=2) + '\n')
    for v in VARIANTS:
        worlds = result['worlds'][v].values()
        print(v, {key: sum(w[key] for w in worlds) for key in ['population', 'hPopulation', 'hCamps', 'hOwners', 'hAnimals', 'humanDeaths', 'slaughtered', 'fissions', 'positiveToZero', 'hHerdlessDaughtersNeverOwned']})

if __name__ == '__main__': main()
