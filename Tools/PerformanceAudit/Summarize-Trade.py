#!/usr/bin/env python3
"""Summarize Run-TradeAudit.py: default-off outcome exactness, trade activity and the husbandry-first gate."""
import json
import pathlib
import sys

WORLDS = ['C1', 'C2', 'C3', 'P1', 'A1']
OUTCOMES = ['population', 'campCount', 'managedAnimals', 'wildAnimals', 'husbandryAnimals', 'agriculturalCampsAfterTurn', 'endingPopulationWeightedQol', 'foodYield']


def samples(root, variant, world):
    path = root / 'gameplay' / f'{variant}-{world}.json'
    if not path.exists(): return None
    return [s for s in json.loads(path.read_text())['samples'] if s['phase'] == 'post-opening']


def pct(values, f):
    v = sorted(values)
    if not v: return 0
    i = (len(v) - 1) * f; lo = int(i); hi = min(lo + 1, len(v) - 1)
    return v[lo] + (v[hi] - v[lo]) * (i - lo)


def exactness(root, a, b):
    ok = True
    for w in WORLDS:
        x, y = samples(root, a, w), samples(root, b, w)
        if x is None or y is None: print(f'{a} vs {b} {w}: missing'); ok = False; continue
        diffs = [(s['turn'], k) for s, t in zip(x, y) for k in OUTCOMES if s.get(k) != t.get(k)]
        camps = [(s['turn']) for s, t in zip(x, y) if s.get('camps') != t.get('camps')]
        print(f'{a} vs {b} {w}: {len(x)} turns, outcome differences {len(diffs)}, per-camp differences {len(camps)}' + (f' first {diffs[0]}' if diffs else ''))
        ok &= not diffs and not camps
    return ok


def activity(root, variant):
    for w in WORLDS:
        x = samples(root, variant, w)
        if x is None: continue
        tot = lambda k: sum(s.get(k, 0) for s in x)
        print(f'{variant} {w}: eligible camp-turns {tot("tradeEligible"):.0f}, requisitions {tot("tradeRequisitions"):.0f}, honoured {tot("tradeHonoured"):.0f}, '
              f'countered {tot("tradeCounteredRequisitions"):.0f}, ignored {tot("tradeIgnored"):.0f}, trades {tot("tradeCount"):.0f}, cloth {tot("tradeCloth"):.1f}, material {tot("tradeMaterial"):.1f}, '
              f'plan ms mean {tot("tradePlanMs") / len(x):.3f} max {max(s.get("tradePlanMs", 0) for s in x):.2f}')


def gate(root, control, candidate):
    fails = []
    for t in (25, 50):
        def herd(v, w):
            x = samples(root, v, w)
            return [c for c in x[t - 1]['camps'] if c['linked'] > 0 and c['technology'] == 'AnimalHusbandry']
        data = {v: sum((herd(v, w) for w in WORLDS), []) for v in (control, candidate)}
        for name, f in (('linked pop', lambda c: c['linked']), ('animals', lambda c: c['managedAnimals'])):
            for kind, agg in (('total', sum), ('median', lambda v: pct(v, .5))):
                a, b = agg(list(map(f, data[control]))), agg(list(map(f, data[candidate])))
                ch = b / a - 1 if a else 0
                if ch < -.05 or (a and b / a >= 10): fails.append((t, name, kind, ch))
                print(f't={t} husbandry-first {name} {kind} {a:.6g} -> {b:.6g} ({ch * 100:+.2f}%)')
    print('GATE', 'PASS' if not fails else 'FAIL', fails)


def main():
    root = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else 'TestResults/Trade')
    print('EXACT reference/off', 'PASS' if exactness(root, 'reference', 'off') else 'FAIL')
    print('EXACT off/on', 'PASS' if exactness(root, 'off', 'on') else 'DIFFERENT')
    activity(root, 'on')
    gate(root, 'off', 'on')


if __name__ == '__main__': main()
