#!/usr/bin/env python3
"""Settlement population by subsistence regime against the calibration estimates (Docs/DemandControlSlice1.md, "Slice 2").

The estimates are the user's non-binding calibration targets for DemandControl with fission, immigration and trade on
(4 October 2026): median settlement population about 600 for agricultural (Cultivation) settlements and about 250 for
husbandry (AnimalHusbandry) ones. They are reported, never enforced, and must be reached by mechanism, not by retuning
physical constants (Docs/EconomicControlContract.md §16).

Usage: Summarize-Settlements.py <dir with gameplay/> <variant[,variant...]> [turn]   (default turn 50)
"""
import json, pathlib, statistics, sys

WORLDS = ['C1', 'C2', 'C3', 'P1', 'A1']
ESTIMATES = {'Cultivation': 600, 'AnimalHusbandry': 250}

def quantile(values, f):
    v = sorted(values)
    if not v: return float('nan')
    i = (len(v) - 1) * f; lo = int(i); hi = min(lo + 1, len(v) - 1)
    return v[lo] + (v[hi] - v[lo]) * (i - lo)

def main():
    root = pathlib.Path(sys.argv[1]); variants = sys.argv[2].split(','); turn = int(sys.argv[3]) if len(sys.argv) > 3 else 50
    for v in variants:
        camps = []
        for w in WORLDS:
            samples = [s for s in json.load(open(root / 'gameplay' / f'{v}-{w}.json'))['samples'] if s['phase'] == 'post-opening']
            camps += [c for c in samples[turn - 1]['camps'] if c['Population'] > 0]
        print(f'== {v}, turn {turn}: {len(camps)} settlements ==')
        for regime, estimate in ESTIMATES.items():
            pops = [c['Population'] for c in camps if c['technology'] == regime]
            if not pops: print(f'  {regime:<16} none'); continue
            med = statistics.median(pops)
            print(f'  {regime:<16} n={len(pops):>5}  p25 {quantile(pops, .25):>6.0f}  median {med:>6.0f}  p75 {quantile(pops, .75):>6.0f}  '
                  f'| calibration estimate {estimate} (non-binding): {med - estimate:+.0f} ({(med / estimate - 1) * 100:+.0f}%)')

if __name__ == '__main__': main()
