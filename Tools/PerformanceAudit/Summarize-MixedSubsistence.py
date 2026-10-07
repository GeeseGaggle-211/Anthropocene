#!/usr/bin/env python3
"""Summarize complete, matched mixed-subsistence audit banks; never hide missing worlds."""
import argparse
import json
import math
from pathlib import Path


def triplet(values):
    values = sorted(values)
    if not values or not all(math.isfinite(v) for v in values):
        raise ValueError('Empty or non-finite metric distribution')
    return tuple(values[round((len(values) - 1) * p)] for p in (.25, .5, .75))


def show(values, decimals=0):
    return ' / '.join(f'{v:,.{decimals}f}' for v in values)


def changes(candidate, control):
    return ' / '.join(f'{100 * (c / b - 1):+.1f}%' if b else 'n/a (zero baseline)'
                      for c, b in zip(candidate, control))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('root', type=Path)
    parser.add_argument('--out', type=Path)
    parser.add_argument('--seeds', default='11,23,47,101,1729')
    parser.add_argument('--post-opening-turns', type=int, default=40)
    args = parser.parse_args()
    seeds = sorted({int(x) for x in args.seeds.split(',')})
    if not seeds or args.post_opening_turns <= 0:
        parser.error('Expected seeds and continuation length must be nonempty/positive')
    bank = {}
    metadata = {}
    for mode in ('control', 'candidate'):
        for seed in seeds:
            path = args.root / f'{mode}-{seed}.json'
            data = json.loads(path.read_text(encoding='utf-8'))
            post = [s for s in data['samples'] if s['postOpening']]
            if data['seed'] != seed or len(post) != args.post_opening_turns:
                raise ValueError(f'Incomplete or mismatched world: {path}')
            last = post[-1]
            camps = last['camps']
            pop = lambda cs: sum(c['Population'] for c in cs)
            def weighted_qol(cs):
                n = pop(cs)
                return sum(c['Population'] * c['QoL'] for c in cs) / n if n else 0
            def active(camp, role):
                return sum(a['Population'] for a in camp['assignments'] if a['Role'] == role) > 0
            h = [c for c in camps if c['holdsHusbandry']]
            f = [c for c in camps if c['holdsWatercraft']]
            ag = [c for c in camps if c['holdsCultivation']]
            bank[mode, seed] = dict(
                population=last['population'], qol=last['priorWeightedQol'], camps=last['campCount'],
                hpop=pop(h), hq=weighted_qol(h), animals=sum(c['managedAnimals'] for c in h), hcamps=len(h),
                fpop=pop(f), fq=weighted_qol(f), fcamps=len(f), apop=pop(ag),
                fishers=sum(a['Population'] for c in camps for a in c['assignments'] if a['Role'] == 'Fisher'),
                mixed2=sum(sum(bool(c[k]) for k in ('holdsCultivation','holdsHusbandry','holdsWatercraft')) >= 2 for c in camps),
                mixed3=sum(all(c[k] for k in ('holdsCultivation','holdsHusbandry','holdsWatercraft')) for c in camps),
                active_fishing=sum(active(c, 'Fisher') for c in camps),
                active_mixed=sum(sum(active(c, role) for role in ('Farmer','Herder','Fisher')) >= 2 for c in camps),
                fishing_first=pop([c for c in camps if c['technology'] == 'Watercraft']))
            metadata[mode, seed] = data
    for mode in ('control', 'candidate'):
        hashes = {metadata[mode, seed]['configHash'] for seed in seeds}
        if len(hashes) != 1:
            raise ValueError(f'Configuration changed within {mode} bank')
    for seed in seeds:
        for key in ('players', 'shape', 'worldWidth', 'worldHeight', 'turnDays', 'workerCount'):
            if metadata['control', seed][key] != metadata['candidate', seed][key]:
                raise ValueError(f'Unmatched {key} in seed {seed}')
    metrics = [
        ('Total population','population',0), ('Prior-cohort weighted QoL','qol',4),
        ('Husbandry-capable residents','hpop',0), ('Husbandry-capable endpoint resident-weighted QoL','hq',4),
        ('Managed Husbandry animals','animals',0), ('Husbandry-capable settlements','hcamps',0),
        ('Watercraft-capable residents','fpop',0), ('Watercraft-capable endpoint resident-weighted QoL','fq',4),
        ('Assigned Fishers','fishers',0), ('Settlements staffing Fishers','active_fishing',0),
        ('Cultivation-capable residents','apop',0), ('Settlements with 2+ capabilities','mixed2',0),
        ('Settlements with all 3 capabilities','mixed3',0),
        ('Settlements staffing 2+ food occupations','active_mixed',0),
        ('Watercraft-first lineage residents (diagnostic only)','fishing_first',0)]
    lines = ['# Mixed subsistence matched-world comparison', '',
             f'Complete paired bank: {len(seeds)} seeds ({seeds}); {args.post_opening_turns} post-opening turns.',
             'Control disables the added Fishing/diffusion/taming features in the same corrected code. Candidate enables them.',
             'Capability groups overlap and are not production or identity categories. World QoL is prior-cohort weighted; subgroup QoL uses endpoint resident weights.',
             'Quartiles are descriptive nearest-index order statistics, not confidence intervals. Zero-baseline percentage changes are undefined.', '',
             '| Metric | Control P25 / median / P75 | Candidate P25 / median / P75 | Relative change |',
             '|---|---:|---:|---:|']
    for label, key, decimals in metrics:
        control = triplet([bank['control', s][key] for s in seeds])
        candidate = triplet([bank['candidate', s][key] for s in seeds])
        lines.append(f'| {label} | {show(control, decimals)} | {show(candidate, decimals)} | {changes(candidate, control)} |')
    lines += ['', '## Paired seed outcomes', '',
              '| Seed | Control population | Candidate population | Population change | Control animals | Candidate animals | Animal change | QoL change (absolute) |',
              '|---:|---:|---:|---:|---:|---:|---:|---:|']
    for seed in seeds:
        b, c = bank['control', seed], bank['candidate', seed]
        lines.append(f"| {seed} | {b['population']:,} | {c['population']:,} | {changes([c['population']],[b['population']])} | {b['animals']:,} | {c['animals']:,} | {changes([c['animals']],[b['animals']])} | {c['qol']-b['qol']:+.6f} |")
    output = '\n'.join(lines) + '\n'
    print(output)
    if args.out:
        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text(output, encoding='utf-8')


if __name__ == '__main__':
    main()
