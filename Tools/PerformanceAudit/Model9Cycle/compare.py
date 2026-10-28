import json, sys, math, collections
def load(p, tmax):
    return [r for r in (json.loads(l) for l in open(p)) if r['turn'] <= tmax]
def stats(rows, t0):
    by = collections.defaultdict(list)
    for r in rows:
        if not r['terminated'] and r['p0'] > 0: by[r['camp']].append(r)
    dc = dr = 0; n = 0; alt = tri = 0; zero = mat = 0; var = []
    for l in by.values():
        l.sort(key=lambda r: r['turn'])
        q = [r['qol'] * r['years'] for r in l]
        for i in range(1, len(l)):
            a, b = l[i-1], l[i]
            if b['turn'] < t0 or b['p0'] < 50 or b['turn'] != a['turn'] + 1: continue
            dc += abs(b['cloth'] - a['cloth']); d = q[i] - q[i-1]; dr += d * d; n += 1
            if i >= 2 and l[i-2]['turn'] == a['turn'] - 1:
                x = a['cloth'] - l[i-2]['cloth']; y = b['cloth'] - a['cloth']; tri += 1
                if x * y < 0 and abs(x) > .3 and abs(y) > .3: alt += 1
        m = [r for r in l if r['turn'] >= t0 and r['p0'] >= 50]
        mat += len(m); zero += sum(r['cloth'] < .1 for r in m)
        if len(m) >= 6:
            qq = [r['qol'] * r['years'] for r in m]; mu = sum(qq) / len(qq); var.append(sum((x - mu) ** 2 for x in qq) / len(qq))
    last = max(r['turn'] for r in rows)
    lr = [r for r in rows if r['turn'] == last and not r['terminated']]
    win = [r for r in rows if r['turn'] >= t0 and not r['terminated']]
    return dict(lastTurn=last, maturePairs=n, meanAbsDCloth=round(dc / max(1, n), 4), lag1AltShare=round(alt / max(1, tri), 4),
        matureZeroClothShare=round(zero / max(1, mat), 4), rmsDQoLYears=round(math.sqrt(dr / max(1, n)), 3),
        meanWithinCampQoLYearsVar=round(sum(var) / max(1, len(var)), 3),
        meanCloth=round(sum(r['cloth'] for r in win) / len(win), 4), meanQoLYears=round(sum(r['qol'] * r['years'] for r in win) / len(win), 3),
        personQoLYears=round(sum(r['p0'] * r['qol'] * r['years'] for r in win)), personYears=round(sum(r['p0'] * r['years'] for r in win)),
        finalResidents=sum(r['pop'] for r in lr), finalVillages=len(lr), terminated=sum(r['terminated'] for r in rows),
        dormant=sum((not r['terminated']) and (not r['obs']) for r in rows))
tmax = int(sys.argv[3]); t0 = int(sys.argv[4])
for name in (sys.argv[1], sys.argv[2]):
    print(__import__('os').path.basename(name), json.dumps(stats(load(name, tmax), t0)))
