"""Reject any fresh-process GPU fingerprint mismatch; CPU parity is a separate metric."""
from pathlib import Path
import json, sys
p=Path(sys.argv[1] if len(sys.argv)>1 else 'Tools/GpuEcology/fusion-results')
backend=sys.argv[2] if len(sys.argv)>2 else 'fused-compact'
def keyed(filename):
 rows=json.loads((p/filename).read_text())
 result={(r['world'],r['turn']):r for r in rows}
 expected={(name,turn) for name in ['Standard80','Huge240'] for turn in range(1,51)}
 if len(rows)!=100 or set(result)!=expected: raise RuntimeError(f'Incomplete/duplicate chronology: {filename}')
 return result
a=keyed(f'trajectory-{backend}-1.json');b=keyed(f'trajectory-{backend}-2.json');cpu=keyed('native-cpu-50-turns.json')
rows=[]
for key in sorted(a):
 x,y,c=a[key],b[key],cpu[key]
 rows.append(dict(world=key[0],turn=key[1],repeatable=x['measurement']['Fingerprint']==y['measurement']['Fingerprint'],
  cpuFingerprintEqual=x['measurement']['Fingerprint']==c['measurement']['Fingerprint'],
  populationDrift=x['campPopulation']-c['campPopulation'],campDrift=x['camps']-c['camps'],
  technologyUnlockDrift=x['technologyUnlocks']-c['technologyUnlocks'],productionDrift=x['production']-c['production']))
failures=sum(not r['repeatable'] for r in rows)
(p/'repeatability-and-drift.json').write_text(json.dumps(dict(backend=backend,failures=failures,checked=len(rows),rows=rows),indent=2))
print(f'Fresh-process repeatability: {len(rows)-failures}/{len(rows)} exact; CPU drift recorded separately.')
if failures: raise SystemExit(1)
