#!/usr/bin/env python3
"""Summarize Run-Profile.py: per-turn cost and settlement QoL/population quantiles (min, p25, median, p75, max).
Usage: Summarize-Profile.py <profile dir> <variant[,variant...]>"""
import json, sys, statistics, pathlib
ROOT = pathlib.Path(sys.argv[1]) / 'gameplay'
W = ['C1', 'C2', 'C3', 'P1', 'A1']
def load(v, w): return [s for s in json.load(open(ROOT / f'{v}-{w}.json'))['samples'] if s['phase'] == 'post-opening']
def q(xs, f):
    v = sorted(xs); i = (len(v) - 1) * f; lo = int(i); hi = min(lo + 1, len(v) - 1); return v[lo] + (v[hi] - v[lo]) * (i - lo)
def five(xs): return (min(xs), q(xs, .25), q(xs, .5), q(xs, .75), max(xs))
out = {}
for v in sys.argv[2].split(','):
    runs = {w: load(v, w) for w in W}; T = len(runs['C1'])
    series = []
    for t in range(T):
        camps = [c for w in W for c in runs[w][t]['camps'] if c['Population'] > 0]
        pops = [c['Population'] for c in camps]; qols = [c['QoL'] for c in camps]
        tot = lambda k: sum(runs[w][t].get(k, 0) or 0 for w in W)
        series.append(dict(turn=t + 1, settlements=len(camps), population=sum(pops), pop=five(pops), qol=five(qols),
            qolw=sum(c['QoL'] * c['Population'] for c in camps) / sum(pops), villages=sum(c['IsVillage'] for c in camps),
            prepareMs=tot('prepareMs') / 5, executeMs=tot('executeMs') / 5, tradeMs=tot('tradePlanMs') / 5, immMs=tot('immigrationMs') / 5, fisMs=tot('fissionMs') / 5,
            aiMs=tot('aiMs') / 5, heapMB=max(runs[w][t].get('managedHeapBytes', 0) or 0 for w in W) / 2**20,
            trades=tot('tradeCount'), migrants=tot('migrantCount'), migrations=tot('migrationCount'), fissions=tot('fissionCount'),
            births=tot('humanBirths'), deaths=tot('humanDeaths')))
    out[v] = series
json.dump(out, open(ROOT.parent / 'series.json', 'w'))
for v, series in out.items():
    print(f'\n== {v} ==')
    print(f"{'t':>3} {'setl':>5} {'pop':>8} | settlement population min/p25/med/p75/max            | settlement QoL min/p25/med/p75/max        | QoLw   | prep ms exec ms trade imm fis | trades migr fiss")
    for s in series:
        if s['turn'] in (1, 5, 10, 15, 20, 25, 30, 35, 40, 45, 50):
            p, ql = s['pop'], s['qol']
            print(f"{s['turn']:>3} {s['settlements']:>5} {s['population']:>8} | {p[0]:>5} {p[1]:>6.0f} {p[2]:>6.0f} {p[3]:>6.0f} {p[4]:>6} | {ql[0]:.3f} {ql[1]:.3f} {ql[2]:.3f} {ql[3]:.3f} {ql[4]:.3f} | {s['qolw']:.4f} | {s['prepareMs']:>7.0f} {s['executeMs']:>7.0f} {s['tradeMs']:>5.0f} {s['immMs']:>3.0f} {s['fisMs']:>3.0f} | {s['trades']:>6} {s['migrants']:>4} {s['fissions']:>4}")
    tot = [s['prepareMs'] + s['executeMs'] for s in series]
    print(f"per-turn wall time (mean of 5 worlds, prepare+execute): mean {statistics.mean(tot):.0f} ms, last10 {statistics.mean(tot[-10:]):.0f} ms, max {max(tot):.0f} ms; 50-turn total {sum(tot)/1000:.1f} s per world; heap max {max(s['heapMB'] for s in series):.0f} MB")
