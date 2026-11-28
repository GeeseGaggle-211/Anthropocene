#!/usr/bin/env python3
"""Matched annual-food worlds: linear civic relaxation versus bounded feedback."""
import argparse,collections,json,os
from pathlib import Path
os.environ.setdefault('MPLCONFIGDIR','/tmp/anthropocene-matplotlib')
os.environ.setdefault('XDG_CACHE_HOME','/tmp/anthropocene-cache')
import numpy as np
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
p=argparse.ArgumentParser()
p.add_argument('--baseline',type=Path,default=Path('TestResults/CivicPlaythrough'))
p.add_argument('--treatment',type=Path,default=Path('TestResults/CivicFeedback'))
a=p.parse_args(); out=a.treatment/'report';out.mkdir(parents=True,exist_ok=True)
history=json.loads((out/'history.json').read_text())
runs={}; provenance=[]
for variant,root in [('linear',a.baseline),('feedback',a.treatment)]:
 for directory in ['a','b']:
  provenance.append(dict(variant=variant,**json.loads((root/directory/'manifest.json').read_text())))
  for f in (root/directory).glob('*-annual-result.json'):
   result=json.loads(f.read_text()); seed=result['seed']
   rows=[json.loads(l) for l in f.with_name(f.name.replace('-result.json','.jsonl')).read_text().splitlines()]
   runs[variant,seed]=dict(result=result,rows=rows)
assert len(runs)==10
seeds=sorted({s for v,s in runs})
final={};series={};matched=[]
for variant in ['linear','feedback']:
 vs=[v for s in seeds for v in runs[variant,s]['rows'][-1]['villages']]
 final[variant]=dict(population=sum(runs[variant,s]['rows'][-1]['population'] for s in seeds),villages=len(vs),
   meanP=float(np.mean([v['stratification'] for v in vs])),meanAbsP=float(np.mean([abs(v['stratification']) for v in vs])),
   populationWeightedC=sum(v['complexity']*v['population'] for v in vs)/sum(v['population'] for v in vs),
   accounting=sum('TokensAndAccounting' in v['tiers'] for v in vs),irrigationUsers=sum(v['irrigation']['watered']>0 for v in vs),
   groups=dict(collections.Counter(v['group'] for v in vs)),quartiles={})
 for group in ['all','egalitarian','mixed','stratified']:
  ns=[v['population'] for v in vs if group=='all' or v['group']==group]
  final[variant]['quartiles'][group]=dict(n=len(ns),p25=float(np.quantile(ns,.25)),median=float(np.median(ns)),p75=float(np.quantile(ns,.75)))
 series[variant]=[]
 for year in range(0,2001,100):
  rows=[min(runs[variant,s]['rows'],key=lambda r:abs(r['years']-year)) for s in seeds]
  villages=[v for r in rows for v in r['villages']]
  series[variant].append(dict(year=year,population=sum(r['population'] for r in rows),groups=dict(collections.Counter(v['group'] for v in villages))))
for seed in seeds:
 before={v['id']:v for v in runs['linear',seed]['rows'][-1]['villages'] if v['original']}
 after={v['id']:v for v in runs['feedback',seed]['rows'][-1]['villages'] if v['original']}
 for cid in sorted(before.keys()&after.keys()):
  matched.append(dict(seed=seed,id=cid,linearP=before[cid]['stratification'],feedbackP=after[cid]['stratification'],linearGroup=before[cid]['group'],feedbackGroup=after[cid]['group']))
summary=dict(provenance=provenance,final=final,series=series,matchedOriginalVillages=matched,
 history=history['runs'],runs=[dict(variant=v,**r['result']) for (v,s),r in sorted(runs.items())],
 zeroFeedbackReplay=dict(manifest=json.loads((a.treatment/'control/manifest.json').read_text()),result=json.loads((a.treatment/'control/seed-11-annual-result.json').read_text())),
 definitions=dict(reversal='C > .05, crosses from P < -.25 to P > .25 or vice versa, allowing intervening mixed years; reset on incipient state',quartiles='Unweighted endpoint village population, linear interpolation'))
(out/'summary.json').write_text(json.dumps(summary,indent=2)+'\n')
plt.rcParams.update({'font.family':'DejaVu Sans','axes.spines.top':False,'axes.spines.right':False,'figure.facecolor':'#fafbf9','axes.facecolor':'#fafbf9'})
fig,axs=plt.subplots(2,2,figsize=(12,8),constrained_layout=True)
colors={'linear':'#947050','feedback':'#087f8c'}
for v in colors:
 vs=[x for s in seeds for x in runs[v,s]['rows'][-1]['villages']]
 axs[0,0].hist([x['stratification'] for x in vs],bins=np.linspace(-1,1,17),histtype='step',linewidth=2,color=colors[v],label=v)
 axs[0,1].scatter([x['stratification'] for x in vs],[x['complexity'] for x in vs],s=[10+x['population']/70 for x in vs],alpha=.45,color=colors[v],label=v)
 axs[1,1].plot([x['year'] for x in series[v]],[x['population']/1000 for x in series[v]],color=colors[v],label=v)
for start,shock,color,label in [(.02,0,'#087f8c','positive lean, neutral environment'),(-.02,0,'#947050','negative lean, neutral environment'),(.4,-.35,'#b54857','positive lean, opposing shock at 150y')]:
 rows=[r for r in history['probes'] if r['start']==start and r['shock']==shock]
 axs[1,0].plot([r['year'] for r in rows],[r['p'] for r in rows],label=label,color=color)
axs[1,0].axvline(150,color='#888',ls=':',lw=1)
axs[1,0].axhline(0,color='#888',lw=.7)
axs[0,0].set(title='Stratification distribution at year 2,000',xlabel='P (negative = egalitarian)',ylabel='Villages')
axs[0,1].set(title='Complexity × stratification at year 2,000',xlabel='Stratification P',ylabel='Complexity C',xlim=(-1,1),ylim=(0,1.04))
axs[1,0].set(title='Controlled kernel probes: reinforcement and reversal',xlabel='Years',ylabel='Stratification P')
axs[1,1].set(title='Matched native playthrough population',xlabel='Years after civic activation',ylabel='People (thousands)')
for ax in axs.flat: ax.legend(fontsize=8)
fig.suptitle('Bounded civic feedback • five matched annual-food worlds',fontsize=15)
fig.savefig(out/'civic-feedback.png',dpi=180);plt.close(fig)
print(json.dumps(final,indent=2))
for variant in colors:
 hs=[r for r in history['runs'] if r['variant']==variant]
 print(variant,'fissions',sum(sum(r['splits'].values()) for r in hs),'reversals',sum(len(r['reversals']) for r in hs))
 for r in hs:
  for reversal in r['reversals']:print(r['seed'],reversal)
