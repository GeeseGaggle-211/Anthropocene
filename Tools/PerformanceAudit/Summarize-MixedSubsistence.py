#!/usr/bin/env python3
import argparse, glob, json, os, statistics
from collections import defaultdict

def pct(xs,p):
    xs=sorted(xs)
    return xs[round((len(xs)-1)*p)] if xs else 0
def tri(xs): return (pct(xs,.25),pct(xs,.5),pct(xs,.75))
def fmt(t,dec=0):
    return " / ".join(f"{x:,.{dec}f}" for x in t)
def change(c,b):
    return tuple(100*(x/y-1) if y else 0 for x,y in zip(c,b))
def fchg(t): return " / ".join(f"{x:+.1f}%" for x in t)

ap=argparse.ArgumentParser(); ap.add_argument("root"); ap.add_argument("--out",default="")
a=ap.parse_args()
runs=[]
for path in glob.glob(os.path.join(a.root,"*.json")):
    base=os.path.basename(path)[:-5]
    if "-" not in base: continue
    mode,seed=base.rsplit("-",1)
    if mode not in ("control","candidate"): continue
    d=json.load(open(path)); samples=d.get("samples") or []
    post=[s for s in samples if s.get("postOpening")]
    if not post: continue
    last=post[-1]; camps=last.get("camps") or []
    def subset(flag): return [c for c in camps if c.get(flag)]
    h=subset("holdsHusbandry"); f=subset("holdsWatercraft"); ag=subset("holdsCultivation")
    def pop(cs): return sum(int(c.get("Population",0) or 0) for c in cs)
    def wq(cs):
        n=pop(cs); return sum(int(c.get("Population",0) or 0)*float(c.get("QoL",0) or 0) for c in cs)/n if n else 0
    animals=sum(int(c.get("managedAnimals",0) or 0) for c in h)
    fishers=0
    for c in camps:
        for x in c.get("assignments") or []:
            if x.get("Role")=="Fisher": fishers += int(x.get("Population",0) or 0)
    mixed2=sum(1 for c in camps if sum(bool(c.get(k)) for k in ("holdsCultivation","holdsHusbandry","holdsWatercraft"))>=2)
    mixed3=sum(1 for c in camps if all(c.get(k) for k in ("holdsCultivation","holdsHusbandry","holdsWatercraft")))
    runs.append(dict(mode=mode,seed=int(seed),population=int(last["population"]),qol=float(last.get("priorWeightedQol",0) or 0),
        camps=int(last["campCount"]),hpop=pop(h),hq=wq(h),animals=animals,hcamps=len(h),
        fpop=pop(f),fq=wq(f),fcamps=len(f),fishers=fishers,apop=pop(ag),acamps=len(ag),
        mixed2=mixed2,mixed3=mixed3,fissions=sum(int(s.get("fissionCount",0) or 0) for s in samples),
        deaths=sum(int(s.get("humanDeaths",0) or 0) for s in samples),
        totalms=sum(float(s.get("totalMs",0) or 0) for s in post)))
by=defaultdict(list)
for r in runs: by[r["mode"]].append(r)
metrics=[
 ("Total population","population",0),("Weighted QoL","qol",4),("Final camps","camps",0),
 ("Husbandry population","hpop",0),("Husbandry weighted QoL","hq",4),("Managed Husbandry animals","animals",0),("Husbandry camps","hcamps",0),
 ("Watercraft population","fpop",0),("Watercraft weighted QoL","fq",4),("Watercraft camps","fcamps",0),("Assigned Fishers","fishers",0),
 ("Cultivation population","apop",0),("Mixed-capability camps (2+)","mixed2",0),("Triple-capability camps","mixed3",0)
]
lines=["# Mixed subsistence matched-world comparison","",
"Five matched Continents worlds, seeds 11/23/47/101/1729, 80 founders, production config, opening plus 40 post-opening turns.",
"Control uses the same code with resident Fishing, trade/proximity diffusion, and taming disabled. Candidate uses production defaults.","",
"| Metric | Control P25 / median / P75 | Candidate P25 / median / P75 | Candidate change |",
"|---|---:|---:|---:|"]
for label,key,dec in metrics:
    b=tri([r[key] for r in by["control"]]); c=tri([r[key] for r in by["candidate"]])
    lines.append(f"| {label} | {fmt(b,dec)} | {fmt(c,dec)} | {fchg(change(c,b))} |")
lines += ["","## Per-seed","",
"| Mode | Seed | Population | QoL | H pop | H QoL | Animals | Watercraft pop | Fishers | Mixed 2+ | Triple | Camps |",
"|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|"]
for mode in ("control","candidate"):
    for r in sorted(by[mode],key=lambda x:x["seed"]):
        lines.append(f"| {mode} | {r['seed']} | {r['population']:,} | {r['qol']:.4f} | {r['hpop']:,} | {r['hq']:.4f} | {r['animals']:,} | {r['fpop']:,} | {r['fishers']:,} | {r['mixed2']} | {r['mixed3']} | {r['camps']} |")
out="\n".join(lines)+"\n"; print(out)
if a.out:
    os.makedirs(os.path.dirname(a.out) or ".",exist_ok=True); open(a.out,"w").write(out)
