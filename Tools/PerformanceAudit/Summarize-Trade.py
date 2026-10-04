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
        flows = {}; ordered = [0, 0.0, 0]
        for s in x:
            for e in s.get('tradeEvents') or []:
                key = f"{e['Want']}<-{e['Give']}"; f = flows.setdefault(key, [0, 0.0, 0.0]); f[0] += 1; f[1] += e['WantQuantity']; f[2] += e['GiveQuantity']
                for leg, frac in (('WantMadeToOrder', 'Delivered'), ('GiveMadeToOrder', 'GiveDelivered')):
                    if e.get(leg):
                        ordered[0] += 1; ordered[1] += e.get(frac, 1); ordered[2] += e.get(frac, 1) < 1 - 1e-9
        print(f'{variant} {w}: eligible camp-turns {tot("tradeEligible"):.0f}, requisitions {tot("tradeRequisitions"):.0f}, honoured {tot("tradeHonoured"):.0f}, '
              f'countered {tot("tradeCounteredRequisitions"):.0f}, ignored {tot("tradeIgnored"):.0f}, trades {tot("tradeCount"):.0f}, '
              f'plan ms mean {tot("tradePlanMs") / len(x):.3f} max {max(s.get("tradePlanMs", 0) for s in x):.2f}')
        for key, (n, want, give) in sorted(flows.items()): print(f'    {key}: {n} trades, received {want:.1f}, paid {give:.1f}')
        if ordered[0]: print(f'    after-production legs {ordered[0]}, mean delivered {ordered[1] / ordered[0]:.3f}, short {ordered[2]}')


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


def timing(root, control, candidate):
    # Per-turn planning (prepare) and resolution (execute) wall time; trade's handshake is inside prepare. One run
    # each on a shared machine: report means, so treat small differences as noise.
    for key in ('prepareMs', 'executeMs', 'tradePlanMs'):
        rows = []
        for w in WORLDS:
            a, b = samples(root, control, w), samples(root, candidate, w)
            if a is None or b is None: continue
            ma = sum(s.get(key, 0) for s in a) / len(a); mb = sum(s.get(key, 0) for s in b) / len(b)
            la = sum(s.get(key, 0) for s in a[-10:]) / 10; lb = sum(s.get(key, 0) for s in b[-10:]) / 10
            rows.append(f'{w} {ma:.0f}->{mb:.0f} (last10 {la:.0f}->{lb:.0f})')
        print(f'{key} mean ms per turn: ' + '; '.join(rows))


def main():
    root = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else 'TestResults/Trade')
    print('EXACT reference/off', 'PASS' if exactness(root, 'reference', 'off') else 'FAIL')
    print('EXACT off/on', 'PASS' if exactness(root, 'off', 'on') else 'DIFFERENT')
    activity(root, 'on')
    gate(root, 'off', 'on')
    timing(root, 'off', 'on')


if __name__ == '__main__': main()
