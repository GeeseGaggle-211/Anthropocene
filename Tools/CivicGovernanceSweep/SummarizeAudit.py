"""Reduce audit trajectories and decision funnels. Usage: python SummarizeAudit.py ROOT [ROOT ...]."""
import collections
import csv
import glob
import json
import math
import os
import statistics
import sys
from pathlib import Path


def read(path):
    with open(path) as f:
        return [json.loads(line) for line in f if line.strip()]


def slope(rows, key):
    xs = [r['years'] for r in rows]
    ys = [key(r) for r in rows]
    if len(xs) < 2 or any(y is None for y in ys):
        return None
    xm, ym = statistics.mean(xs), statistics.mean(ys)
    return 100 * sum((x-xm)*(y-ym) for x,y in zip(xs,ys)) / sum((x-xm)**2 for x in xs)


def village(row, cid):
    return next((c for c in row['camps'] if c['id'] == cid), None)


def window(rows, lo, hi, cid):
    selected = [r for r in rows if lo <= r['years'] <= hi]
    ledger = [village(r, cid) for r in selected[1:]]
    ledger = [c['d'] for c in ledger if c and c.get('d')]
    keys = ['GrossBirths', 'GrossDeaths', 'SplitOff', 'Emigrated', 'Immigrated']
    return dict(start=selected[0]['years'], end=selected[-1]['years'], boundaries=len(selected),
                worldSlopePerCentury=slope(selected, lambda r:r['worldPopulation']),
                largestSlopePerCentury=slope(selected, lambda r:r['largest']),
                trackedVillage=cid,
                trackedSlopePerCentury=slope(selected, lambda r: (village(r,cid) or {}).get('population')),
                trackedLedger={k:sum(d[k] for d in ledger) for k in keys})


def funnel(records, minpop, lo, hi):
    rows = [r for x in records if lo <= x['years'] <= hi for r in x['rows'] if r['population'] >= minpop]
    return dict(villageTurns=len(rows), reasons=dict(collections.Counter(r['reason'] for r in rows)),
                opportunities=sum(r['gain'] > 0 for r in rows),
                opportunitiesBlockedByFertileGate=sum(r['gain'] > 0 and not r['gate'] for r in rows),
                initialGrant=sum(r['initialGrant'] for r in rows), finalGrant=sum(r['grant'] for r in rows),
                unfilledReasons=dict(collections.Counter(r['unmet'] for r in rows if r['requested'] > 0 and r['grant'] < r['requested']-1e-8)))


def main():
    roots = sys.argv[1:]
    if not roots:
        raise SystemExit(__doc__)
    summaries = []
    series = {}
    for root in roots:
        for path in sorted(Path(root).glob('seed-*-result.json')):
            result = json.loads(path.read_text())
            stem = str(path).removesuffix('-result.json')
            rows = read(stem+'-trajectory.jsonl')
            audit = read(stem+'-irrigation.jsonl')
            mid = next(r for r in rows if r['years'] >= 1000)
            end = rows[-1]
            leader = max(mid['camps'], key=lambda c:c['population'])['id']
            summary = dict(result, at1000={k:mid[k] for k in ('years','worldPopulation','largest','over5000','irrigatedFields')},
                           final={k:end[k] for k in ('years','worldPopulation','largest','over5000','irrigatedFields')},
                           near1000=window(rows,800,mid['years'],leader),
                           near2000=window(rows,end['years']-200,end['years'],leader),
                           trackedFinal=village(end,leader),
                           irrigationFirst1000=funnel(audit,0,0,mid['years']),
                           irrigationLargeFirst1000=funnel(audit,3000,0,mid['years']),
                           irrigationSecond1000=funnel(audit,0,mid['years']+1e-5,end['years']),
                           irrigationLargeSecond1000=funnel(audit,3000,mid['years']+1e-5,end['years']))
            summaries.append(summary)
            series[(result['seed'],result['arm'])] = rows
    dest = Path(roots[-1])
    (dest/'summary.json').write_text(json.dumps(summaries,indent=2)+'\n')
    with open(dest/'trajectory-summary.csv','w') as f:
        w=csv.writer(f)
        w.writerow(['seed','arm','largest1000','largest2000','first5000','world1000','world2000','largestSlope800_1000','largestSlope1800_2000','worldSlope800_1000','worldSlope1800_2000','trackedSlope800_1000','trackedSlope1800_2000'])
        for s in summaries:
            w.writerow([s['seed'],s['arm'],s['at1000']['largest'],s['final']['largest'],s['crossed5000'],s['at1000']['worldPopulation'],s['final']['worldPopulation'],s['near1000']['largestSlopePerCentury'],s['near2000']['largestSlopePerCentury'],s['near1000']['worldSlopePerCentury'],s['near2000']['worldSlopePerCentury'],s['near1000']['trackedSlopePerCentury'],s['near2000']['trackedSlopePerCentury']])
    print((dest/'trajectory-summary.csv').read_text())
    for s in summaries:
        if s['arm']=='full':
            print(s['seed'],json.dumps({k:s[k] for k in ('irrigationFirst1000','irrigationLargeFirst1000','irrigationSecond1000')}))
    if series:
        os.environ.setdefault('MPLCONFIGDIR','/tmp/civic-matplotlib')
        import matplotlib
        matplotlib.use('Agg')
        import matplotlib.pyplot as plt
        seeds=sorted({s for s,a in series})
        fig,axes=plt.subplots(len(seeds),2,figsize=(12,3*len(seeds)),squeeze=False)
        colors={'off':'#777777','phase1':'#df9a28','full':'#1976b8','full-fixed-horizon':'#b24b86'}
        labels={'off':'Civic off','phase1':'Crowding relief','full':'Full, smooth calendar','full-fixed-horizon':'Full, duration fixed after 1,000'}
        for i,seed in enumerate(seeds):
            for arm in colors:
                rows=series.get((seed,arm))
                if not rows: continue
                for j,key in enumerate(('largest','worldPopulation')):
                    axes[i,j].plot([r['years'] for r in rows],[r[key] for r in rows],label=labels[arm],color=colors[arm],linewidth=1.5)
            axes[i,0].axhline(5000,color='#888888',linestyle=':',linewidth=1)
            for j in (0,1):
                axes[i,j].axvline(1000,color='#aaaaaa',linestyle='--',linewidth=1)
                axes[i,j].set_title(f'Seed {seed}: '+('largest village' if j==0 else 'world population'))
                axes[i,j].set_xlabel('Years after matched checkpoint')
                axes[i,j].set_ylabel('Residents')
                axes[i,j].grid(alpha=.15)
        axes[0,0].legend(fontsize=8)
        fig.tight_layout()
        fig.savefig(dest/'population-trajectories.png',dpi=150)
        plt.close(fig)

if __name__=='__main__': main()
