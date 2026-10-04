#!/usr/bin/env python3
"""Founding-lineage comparison of two variants (Docs/PlannerTradeStorageDesign.md §3.11): camps are traced to their founding
camps through fission events, so lineages compare across runs. Usage: Summarize-Lineages.py <dir with gameplay/> [control] [candidate]"""
import json, collections, statistics, sys
ROOT = sys.argv[1] if len(sys.argv) > 1 else 'TestResults/Trade-V1'
CONTROL = sys.argv[2] if len(sys.argv) > 2 else 'off'; CANDIDATE = sys.argv[3] if len(sys.argv) > 3 else 'on'
W = ['C1','C2','C3','P1','A1']
def load(v, w): return [s for s in json.load(open(f'{ROOT}/gameplay/{v}-{w}.json'))['samples'] if s['phase']=='post-opening']
def lineages(samples):
    first = samples[0]['camps']; root = {c['Id']: c['Id'] for c in first}
    for s in samples:
        for f in s.get('fissionEvents') or []:
            root[f['DaughterCamp']] = root.get(f['ParentCamp'], f['ParentCamp'])
    return root, {c['Id']: c for c in first}
rows = []
for w in W:
    on, off = load(CANDIDATE, w), load(CONTROL, w)
    ron, start = lineages(on); roff, _ = lineages(off)
    # trade role per lineage over the run (on)
    flows = collections.defaultdict(collections.Counter); trades = collections.Counter()
    for s in on:
        for t in s.get('tradeEvents') or []:
            a, b = ron.get(t['Requester']), ron.get(t['Responder'])
            if a is None or b is None: continue
            flows[a]['in:' + t['Want']] += t['WantQuantity']; flows[a]['out:' + t['Give']] += t['GiveQuantity']
            flows[b]['in:' + t['Give']] += t['GiveQuantity']; flows[b]['out:' + t['Want']] += t['WantQuantity']
            trades[a] += 1; trades[b] += 1
    for t in (25, 50):
        agg = {}
        for v, samples, root in (('on', on, ron), ('off', off, roff)):
            pop = collections.Counter(); qol = collections.Counter(); camps = collections.Counter()
            for c in samples[t-1]['camps']:
                r = root.get(c['Id'], c['Id']); pop[r] += c['Population']; qol[r] += c['QoL'] * c['Population']; camps[r] += 1
            agg[v] = (pop, qol, camps)
        for r, c0 in start.items():
            po, pf = agg['on'][0][r], agg['off'][0][r]
            rows.append(dict(world=w, t=t, root=r, regime=c0['technology'], start=c0['Population'],
                pop_on=po, pop_off=pf, qol_on=agg['on'][1][r] / po if po else 0, qol_off=agg['off'][1][r] / pf if pf else 0,
                camps_on=agg['on'][2][r], camps_off=agg['off'][2][r], trades=trades[r],
                food_net=flows[r]['in:Food'] - flows[r]['out:Food'],
                goods_in=sum(v for k, v in flows[r].items() if k.startswith('in:') and k != 'in:Food'),
                goods_out=sum(v for k, v in flows[r].items() if k.startswith('out:') and k != 'out:Food')))
json.dump(rows, open(ROOT + '/lineages.json', 'w'))
def summary(name, sel, t=50):
    g = [r for r in rows if r['t'] == t and sel(r)]
    if not g: return
    po = sum(r['pop_on'] for r in g); pf = sum(r['pop_off'] for r in g)
    qo = sum(r['qol_on'] * r['pop_on'] for r in g) / max(1, po); qf = sum(r['qol_off'] * r['pop_off'] for r in g) / max(1, pf)
    ch = [(r['pop_on'] - r['pop_off']) / max(1, r['pop_off']) for r in g if r['pop_off'] > 0]
    print(f"{name:<40} n={len(g):>3} pop {pf:>9.0f} -> {po:>9.0f} ({(po/pf-1)*100 if pf else 0:+6.2f}%)  QoL {qf:.4f} -> {qo:.4f} ({(qo-qf)*100:+.2f} pts)  median lineage dPop {statistics.median(ch)*100 if ch else 0:+.1f}%")
print("t=50, lineages (80 founding camps per world)")
summary('all', lambda r: True)
for reg in sorted({r['regime'] for r in rows}): summary('start regime ' + reg, lambda r, reg=reg: r['regime'] == reg)
summary('no trades', lambda r: r['trades'] == 0)
summary('traded, net food importer', lambda r: r['trades'] > 0 and r['food_net'] > 1)
summary('traded, net food exporter', lambda r: r['trades'] > 0 and r['food_net'] < -1)
summary('traded, food neutral', lambda r: r['trades'] > 0 and abs(r['food_net']) <= 1)
for w in W: summary('world ' + w, lambda r, w=w: r['world'] == w)
q = sorted({r['start'] for r in rows if r['t']==50}); cut = q[len(q)//2]
summary('start pop <= median', lambda r: r['start'] <= cut); summary('start pop > median', lambda r: r['start'] > cut)
print("\nt=25")
summary('all', lambda r: True, 25)
for reg in sorted({r['regime'] for r in rows}): summary('start regime ' + reg, lambda r, reg=reg: r['regime'] == reg, 25)
summary('no trades', lambda r: r['trades'] == 0, 25); summary('traded, net food importer', lambda r: r['trades'] > 0 and r['food_net'] > 1, 25); summary('traded, net food exporter', lambda r: r['trades'] > 0 and r['food_net'] < -1, 25)
