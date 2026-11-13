"""Summarize historical pre-fix (0a1735e) receipts, whose Tools ceiling was doubled.

Do not apply the half-ceiling inference or original case classification to a
corrected replay. Use SummarizePopulationRuns.py for post-correction evidence.
"""
import json,csv
from pathlib import Path
root=Path('TestResults/ConstructionGrantAudit');rows=[]
for p in sorted(root.glob('*-before.json'),key=lambda p:tuple(map(int,p.name.split('-')[:2]))):
 a=json.loads(p.read_text());ap=p.with_name(p.name.replace('-before','-after'))
 if not ap.exists():continue
 b=json.loads(ap.read_text());prod=b['production'];limits=b['limits']
 actual=lambda good:sum(r['Output'] for r in prod if r['Good']==good)
 grant=sum(r['Granted'] for r in a['requests'] if r['Good']=='CanalConstruction')
 forecast=a['production']['CanalConstruction'];got=actual('CanalConstruction')
 cause=[]
 if forecast<grant-1e-7:cause.append('material labor cap')
 if got<forecast-1e-7:cause.append('raw inputs depleted by excess Tools')
 # The compiler adds its implicit tool output twice; these cases have no explicit Tools request.
 assert not any(r['Good']=='Tools' and r['Granted']>0 for r in a['requests'])
 tools=a['ceilings'].get('Tools',0)/2
 row=dict(turn=a['turn'],camp=a['camp'],years=a['years'],grant=grant,forecast=forecast,actual=got,builders=a['staff'].get('Builder',0),
  materialCeiling=a['ceilings']['HousingMaterial'],materialForecast=a['production']['HousingMaterial'],materialActual=actual('HousingMaterial'),
  fundedTools=tools,toolCeiling=a['ceilings'].get('Tools',0),toolForecast=a['production']['Tools'],toolsActual=actual('Tools'),cause='; '.join(cause))
 rows.append(row)
out=Path('Docs/Experiments/ConstructionGrantAudit-20261113-cases.json');out.write_text(json.dumps(rows,indent=2)+'\n')
with out.with_suffix('.csv').open('w') as f:
 w=csv.DictWriter(f,fieldnames=rows[0]);w.writeheader();w.writerows(rows)
for r in rows:print(f"{r['turn']}/{r['camp']} {r['grant']:.6f} -> {r['forecast']:.6f} -> {r['actual']:.6f}; material {r['materialForecast']:.6f} -> {r['materialActual']:.6f}; tools {r['fundedTools']:.6f} -> {r['toolsActual']:.6f}; {r['cause']}")
