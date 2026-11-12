"""Reduce the standard seed-11 gameplay capture to reviewable measurements."""
import json,csv,statistics
from pathlib import Path
root=Path('TestResults/AgriculturalGameplay80')
d=json.loads((root/'replay.json').read_text()); fs=d['runs'][0]['frames']; last=fs[-1]
prod=[p for f in fs for p in f['production'] or []]; water=[r for f in fs for r in f['irrigationWater']]
canal=[p for p in prod if p['good'].startswith('Canal')]
rows=[]; discrepancies=[]
for f in fs:
 for dec in f['decisions']:
  grant=sum(r['granted'] for r in dec['requests'] if r['service']=='irrigation development')
  if grant<=1e-8: continue
  actual=sum(p['output'] for p in f['production'] or [] if p['camp']==dec['camp'] and p['good']=='CanalConstruction')
  if grant>1e-8 and abs(grant-actual)>1e-7: discrepancies.append(dict(turn=f['turn'],camp=dec['camp'],granted=grant,actual=actual))
 rows.append(dict(turn=f['turn'],year=f['year'],calendarYear=f['calendarYear'],durationYears=f['duration'],population=f['population'],camps=len(f['camps']),villages=sum(c['village'] for c in f['camps']),ploughCamps=sum(c['plough'] for c in f['camps']),irrigationCamps=sum(c['irrigation'] for c in f['camps']),food=f['food'],farmTools=f['tools'],canals=len(f['canals']),completedCanals=sum(c['progress']>=1-1e-9 for c in f['canals']),waterDelivered=sum(r['Delivery'] for r in f['irrigationWater']),canalMaterials=sum(p['materials'] for p in f['production'] or [] if p['good'].startswith('Canal')),cropReceipts=sum(r['CropFood'] for r in f['foodReceipts'] or []),wildReceipts=sum(r['WildFood'] for r in f['foodReceipts'] or []),maxSoil=max((r['soil'] for r in f['irrigationFields']),default=0),milliseconds=f['milliseconds']))
def first(pred):
 f=next((f for f in fs if pred(f)),None)
 return None if f is None else dict(year=f['year'],turn=f['turn'])
firstbuild=next(f for f in fs if f['canals']); focus=firstbuild['canals'][0]['owner']
prior=fs[fs.index(firstbuild)-1]; gate=next(c for c in prior['camps'] if c['id']==focus)
metrics={
 'seed':d['seed'],'width':d['width'],'height':d['height'],'initial':rows[0],'final':rows[-1],
 'firstPlough':first(lambda f:any('attained Plough' in e for e in f['events'])),
 'firstIrrigation':first(lambda f:any('attained Irrigation' in e for e in f['events'])),
 'firstConstruction':first(lambda f:bool(f['canals'])),
 'firstWater':first(lambda f:any(r['Delivery']>0 for r in f['irrigationWater'])),
 'finalOperatingFarmers':sum(c['farmers'] for c in last['camps']),
 'finalPloughFarmingCampsWithTools':sum(c['plough'] and c['farmers']>0 and c['tools']>0 for c in last['camps']),
 'finalIrrigatingOperators':len({r['Operator'] for r in last['irrigationWater'] if r['Delivery']>0}),
 'focus':focus,'firstBuildFertileGate':gate['fertileGate'],'firstBuildFertileTiles':gate['fertile'],
 'constructionGrantCount':sum(any(r['service']=='irrigation development' and r['granted']>1e-8 for r in dec['requests']) for f in fs for dec in f['decisions']),
 'constructionIntervals':sum(any(p['good']=='CanalConstruction' for p in f['production'] or []) for f in fs),
 'constructionReceipts':len([p for p in canal if p['good']=='CanalConstruction']),
 'constructionGrantMismatches':discrepancies,
 'constructionMaterials':sum(p['materials'] for p in canal if p['good']=='CanalConstruction'),
 'upkeepMaterials':sum(p['materials'] for p in canal if p['good']=='CanalMaintenance'),
 'constructionBuilderYears':sum(p['workerYears'] for p in canal if p['good']=='CanalConstruction'),
 'upkeepBuilderYears':sum(p['workerYears'] for p in canal if p['good']=='CanalMaintenance'),
 'canalGenericTools':sum(p['toolsConsumed'] for p in canal),
 'allHousingMaterialsMade':sum(p['output'] for p in prod if p['good']=='HousingMaterial'),
 'allHousingMaterialBuilderYears':sum(p['workerYears'] for p in prod if p['good']=='HousingMaterial'),
 'allFarmToolsMade':sum(p['output'] for p in prod if p['good']=='FarmTools'),
 'withdrawal':sum(r['Withdrawal'] for r in water),'delivery':sum(r['Delivery'] for r in water),'loss':sum(r['Loss'] for r in water),
 'maxWaterResidual':max((abs(r['Withdrawal']-r['Delivery']-r['Loss']) for r in water),default=0),
 'cropReceipts':sum(r['cropReceipts'] for r in rows),'wildReceipts':sum(r['wildReceipts'] for r in rows),
 'canalSources':{kind:sum(next(t['source'] for t in d['geography'] if t['id']==c['source'])==kind for c in last['canals']) for kind in ['River','Lake']},
 'soilFinal':[dict(tile=f['tile'],owner=f['owner'],soil=f['soil'],water=f['water']) for f in last['irrigationFields']],
 'turnMillisecondsMedian':statistics.median(f['milliseconds'] for f in fs[1:]),
 'turnMillisecondsMax':max(f['milliseconds'] for f in fs),
 'finalFingerprint':d['runs'][0]['finalFingerprint']}
assert metrics['maxWaterResidual']<1e-8
out=Path('Docs/Experiments/AgriculturalGameplay80-20261112')
Path(str(out)+'-summary.json').write_text(json.dumps(metrics,indent=2)+'\n')
with open(str(out)+'.csv','w') as f:
 w=csv.DictWriter(f,fieldnames=rows[0]);w.writeheader();w.writerows(rows)
print(json.dumps(metrics,indent=2))

# Standalone review figure; the HTML player retains individual village receipts.
import os
os.environ.setdefault('MPLCONFIGDIR','/tmp/anthropocene-matplotlib')
os.environ.setdefault('XDG_CACHE_HOME','/tmp/anthropocene-cache')
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
plt.style.use('dark_background')
fig,axes=plt.subplots(2,2,figsize=(14,8),constrained_layout=True)
fig.set_facecolor('#10191d')
x=[r['year'] for r in rows]
for ax in axes.flat:
 ax.set_facecolor('#17231f'); ax.grid(alpha=.15); ax.set_xlabel('Physical years since world generation')
axes[0,0].plot(x,[r['population'] for r in rows],color='#a4d49b')
axes[0,0].set_title('World population — descriptive, not causal benefit')
axes[0,0].set_ylabel('People, including hunters')
axes[0,1].plot(x[1:],[r['durationYears'] for r in rows[1:]],color='#dfb96a')
axes[0,1].set_title('Actual committed turn duration')
axes[0,1].set_ylabel('Physical years per turn')
ax=axes[1,0]
ax.plot(x,[r['waterDelivered']/max(r['durationYears'],1e-9) for r in rows],color='#73d6e9')
ax.set_title('Paid irrigation: water and completed canals')
ax.set_ylabel('Water delivered per physical year',color='#73d6e9')
other=ax.twinx();other.step(x,[r['completedCanals'] for r in rows],where='post',color='#dfb96a',alpha=.7)
other.set_ylabel('Completed canal segments',color='#dfb96a')
axes[1,1].plot(x,[r['maxSoil']*100 for r in rows],color='#a4d49b',label='Maximum across serviced fields')
axes[1,1].plot(x,[100*statistics.mean(r['soil'] for r in f['irrigationFields']) if f['irrigationFields'] else 0 for f in fs],color='#73d6e9',label='Unweighted mean across serviced fields')
axes[1,1].set_ylim(0,105);axes[1,1].set_title('Persistent soil development');axes[1,1].set_ylabel('Development (%)');axes[1,1].legend(fontsize=8)
fig.suptitle('Anthropocene · 80 founding tribes · seed 11 · smoothed calendar\nCorrected commissioning costs · organic discovery · normal AI and Elder decisions',fontsize=15)
fig.savefig(root/'overview.png',dpi=150)
plt.close(fig)
