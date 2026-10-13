"""Summarize complete native trajectories; never silently accept partial runs."""
import csv
import json
import statistics as st
import sys
from pathlib import Path

root = Path(__file__).resolve().parent
data = root / 'resident-results'
out = Path(sys.argv[1]) if len(sys.argv) > 1 else data
out.mkdir(parents=True, exist_ok=True)

def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))

def write_csv(name, rows):
    with (out / name).open('w', newline='', encoding='utf-8') as f:
        writer = csv.DictWriter(f, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)

expected = {(w, t) for w in ('Standard80', 'Huge240') for t in range(1, 51)}
runs = {}
hashes = set()
for path in sorted(data.glob('trajectory-*.json')):
    if path.name.endswith('-metadata.json'):
        continue
    rows = read(path)
    assert len(rows) == 100 and {(r['world'], r['turn']) for r in rows} == expected, path
    runs[(rows[0]['mode'], rows[0]['run'])] = rows
    hashes.add(read(data / (path.stem + '-metadata.json'))['assemblySha256'])
assert len(hashes) == 1, hashes
assert ('off', '1') in runs
for (mode, run), rows in runs.items():
    if mode in ('off', 'cpu-cache'):
        assert all(r['cpuFingerprintEqual'] for r in rows), ('CPU oracle mismatch', mode, run)

repeat = []
for mode in sorted({k[0] for k in runs}):
    if (mode, '2') not in runs:
        continue
    a = {(r['world'], r['turn']): r['measurement']['Fingerprint'] for r in runs[(mode, '1')]}
    b = {(r['world'], r['turn']): r['measurement']['Fingerprint'] for r in runs[(mode, '2')]}
    mismatches = [str(k) for k in a if a[k] != b[k]]
    repeat.append({'mode': mode, 'checkpoints': len(a), 'mismatches': len(mismatches)})
    assert not mismatches, (mode, mismatches)

windows = []
phases = []
for (mode, run), rows in runs.items():
    for world in ('Standard80', 'Huge240'):
        for start in (20, 40):
            part = [r for r in rows if r['world'] == world and start <= r['turn'] <= 50]
            def avg(get): return st.mean(get(r) for r in part)
            baseline = [r for r in runs[('off', '1')] if r['world'] == world and start <= r['turn'] <= 50]
            base = st.mean(r['measurement']['TotalMs'] for r in baseline)
            total = avg(lambda r: r['measurement']['TotalMs'])
            c = lambda key: avg(lambda r: r['counters'][key])
            transfer = c('uploadMs') + c('downloadMs')
            device = transfer + c('kernelMs')
            windows.append(dict(world=world, window=f'{start}-50', mode=mode, run=run, turns=len(part),
                meanMs=total, medianMs=st.median(r['measurement']['TotalMs'] for r in part),
                sumMs=sum(r['measurement']['TotalMs'] for r in part), speedupVsOff1=base/total,
                prefetchMs=avg(lambda r:r['measurement']['PrefetchMs']), uploadMs=c('uploadMs'),
                kernelMs=c('kernelMs'), downloadMs=c('downloadMs'),
                transferFractionDevice=transfer/device if device else 0,
                transferFractionTurn=transfer/total, bytesPerTurn=c('bytes'),
                prefetchedTiles=c('Prefetched'), queries=c('Queries'), hits=c('Hits'), gpuHits=c('GpuHits'),
                gpuHitFraction=c('GpuHits')/c('Queries') if c('Queries') else 0,
                integratedTiles=avg(lambda r:r['measurement']['Resolution']['TileIntegrations']),
                meanPopulation=avg(lambda r:r['population']), meanCamps=avg(lambda r:r['camps']),
                maxAbsPopulationDrift=max(abs(r['populationDrift']) for r in part),
                maxAbsCampDrift=max(abs(r['campDrift']) for r in part),
                exactCpuCheckpoints=sum(r['cpuFingerprintEqual'] for r in part),
                **{f'hits{stage}':avg(lambda r:r['counters']['stageHits'].get(stage,0)) for stage in ('Draft','Prepare','Resolve','Refresh')}))
            if mode != 'off': continue
            keys = ['HerdIntent', 'Ecology', 'SetupEconomy', 'ManagedHerd', 'GrowthPlan', 'Sustain', 'Production', 'Knowledge', 'Fission', 'Immigration', 'TradeSettle']
            phase = {k:avg(lambda r:r['measurement']['Resolution'][k+'Milliseconds']) for k in keys}
            phase['ResolverOther'] = avg(lambda r:r['measurement']['ResolveMs']) - sum(phase.values())
            phase.update({k:avg(lambda r:r['measurement'][k+'Ms']) for k in ('Draft','Prepare','Refresh','Prefetch')})
            for name, ms in phase.items():
                phases.append(dict(world=world,window=f'{start}-50',run=run,phase=name,meanMs=ms,fraction=ms/total,nested=False))
            for name in ('HorizonPreparation','FullTurnIntegration','HorizonCommit'):
                ms=avg(lambda r:r['measurement']['Resolution'][name+'Milliseconds'])
                phases.append(dict(world=world,window=f'{start}-50',run=run,phase=name,meanMs=ms,fraction=ms/total,nested=True))

write_csv('resident-windows.csv', windows)
write_csv('resident-cpu-phases.csv', phases)
if repeat: write_csv('resident-repeatability.csv', repeat)
profiles=[]
for r in read(data/'profile.json'):
    for scope in r['scopes']:
        profiles.append(dict(world=r['world'],turn=r['turn'],workers=r['workers'],**scope))
write_csv('resident-profile-scopes.csv',profiles)
validation=read(data/'validation.json')
assert len(validation)==18
checks=[]
for world in ('Standard80','Huge240'):
    for turn in (25,40,50):
        final=next(r for r in validation if r['world']==world and r['turn']==turn and r['mode']=='gpu-coeff')
        checks.append(dict(world=world,turn=turn,**{k:final['counters'][k] for k in ('Checked','MaxError','meanError','rmsError')}))
write_csv('resident-validation-summary.csv',checks)
telemetry=[]
for path in sorted(data.glob('trajectory-*-gpu.csv')):
    with path.open(encoding='utf-8-sig') as f:
        rows=list(csv.DictReader(f,skipinitialspace=True))
    vals=[float(r['utilization.gpu [%]']) for r in rows if r.get('utilization.gpu [%]','').replace('.','',1).isdigit()]
    mem=[float(r['memory.used [MiB]']) for r in rows if r.get('memory.used [MiB]','').replace('.','',1).isdigit()]
    telemetry.append(dict(run=path.stem,samples=len(vals),meanUtilization=st.mean(vals) if vals else 0,maxUtilization=max(vals,default=0),peakDeviceMemoryMiB=max(mem,default=0)))
if telemetry:write_csv('resident-telemetry.csv',telemetry)
print(json.dumps({'assemblySha256':list(hashes),'repeatability':repeat,'validation':checks,'windows':windows},indent=2))
