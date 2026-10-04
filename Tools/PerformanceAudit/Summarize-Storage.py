#!/usr/bin/env python3
"""Summarize Run-StorageAudit.py: default-off exactness, the husbandry-first gate per storage variant, and all-camp
storage outcomes (population, QoL, clothing and housing coverage, stored goods per resident, discarded food, goods made)."""
import importlib.util
import pathlib
import sys

HERE = pathlib.Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('summarize_trade', HERE / 'Summarize-Trade.py')
trade = importlib.util.module_from_spec(spec); spec.loader.exec_module(trade)
WORLDS, samples, pct = trade.WORLDS, trade.samples, trade.pct
STORED = ['Cloth', 'AnimalFabric', 'HandTools', 'FarmTools', 'Tools', 'HerdingTools', 'LightHousingUnits', 'HousingMaterial']
MADE = ['Cloth', 'AnimalFabric', 'LightHousing', 'HousingMaterial', 'HandTools', 'Tools', 'FarmTools', 'HerdingTools']


def camps(root, variant, t):
    out = []
    for w in WORLDS:
        x = samples(root, variant, w)
        if x is None: return None
        out += [c for c in x[t - 1]['camps'] if c['Population'] > 0]
    return out


def made(root, variant):
    total = 0.0
    for w in WORLDS:
        for s in samples(root, variant, w) or []:
            for c in s['camps']: total += sum(v for k, v in (c.get('production') or {}).items() if k in MADE)
    return total


def outcomes(root, variant):
    rows = {}
    for t in (25, 50):
        cs = camps(root, variant, t)
        if cs is None: return None
        pop = sum(c['Population'] for c in cs)
        rows[t] = {
            'population': pop,
            'QoL (pop-weighted)': sum(c['QoL'] * c['Population'] for c in cs) / pop,
            'cloth per capita (mean)': sum(c['ClothPerCapita'] for c in cs) / len(cs),
            'housing per capita (mean)': sum(c['HousingPerCapita'] for c in cs) / len(cs),
            'stored goods / resident (median)': pct([sum(c.get(k, 0) for k in STORED) / c['Population'] for c in cs], .5),
            'discarded food (turn)': sum(c.get('discardedFood', 0) for c in cs),
        }
    return rows


def main():
    root = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else 'TestResults/Storage')
    variants = sys.argv[2].split(',') if len(sys.argv) > 2 else ['x1', 'x2', 'x4', 'x2s5']
    print('EXACT reference/off', 'PASS' if trade.exactness(root, 'reference', 'off') else 'FAIL')
    base = outcomes(root, 'off'); base_made = made(root, 'off')
    for v in variants:
        print(f'\n== {v} vs off ==')
        trade.gate(root, 'off', v)
        rows = outcomes(root, v)
        if rows is None: print('missing'); continue
        for t in (25, 50):
            for k, b in rows[t].items():
                a = base[t][k]; ch = (b / a - 1) * 100 if a else 0
                print(f't={t} {k}: {a:.6g} -> {b:.6g} ({ch:+.2f}%)')
        m = made(root, v); print(f'stored goods made over 50 turns: {base_made:.6g} -> {m:.6g} ({(m / base_made - 1) * 100 if base_made else 0:+.2f}%)')
    trade.timing(root, 'off', variants[1] if len(variants) > 1 else variants[0])


if __name__ == '__main__': main()
