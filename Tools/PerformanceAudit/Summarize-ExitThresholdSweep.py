#!/usr/bin/env python3
import argparse, glob, json, os, statistics
from collections import Counter, defaultdict

THRESHOLDS = [0.05, 0.075, 0.10, 0.125]

def pctl(xs, p):
    if not xs: return None
    xs = sorted(xs)
    return xs[round((len(xs) - 1) * p)]

def triplet(xs):
    return (pctl(xs, .25), pctl(xs, .5), pctl(xs, .75))

def fmt3(t, integer=False, digits=3):
    if any(x is None for x in t): return "n/a"
    if integer:
        return " / ".join(f"{x:,.0f}" for x in t)
    return " / ".join(f"{x:.{digits}f}" for x in t)

def change_triplet(cur, base):
    out=[]
    for x,b in zip(cur,base):
        out.append(None if x is None or b in (None,0) else 100*(x/b-1))
    return out

def fmtchg(xs):
    return " / ".join("n/a" if x is None else f"{x:+.1f}%" for x in xs)

def threshold_from_name(path):
    base=os.path.basename(path)[:-5]
    # Exit-0.075-23.json
    parts=base.split("-")
    if len(parts) < 3 or parts[0] != "Exit":
        return None, None
    return float(parts[1]), int(parts[2])

def main():
    ap=argparse.ArgumentParser()
    ap.add_argument("root")
    ap.add_argument("--out", default="")
    args=ap.parse_args()

    runs=[]
    for path in glob.glob(os.path.join(args.root, "**", "*.json"), recursive=True):
        threshold, seed = threshold_from_name(path)
        if threshold is None: continue
        d=json.load(open(path))
        samples=d.get("samples") or []
        if not samples: continue
        post=[s for s in samples if s.get("postOpening")]
        if not post: continue
        last=post[-1]
        camps=last.get("camps") or []
        husbandry=[c for c in camps if c.get("holdsHusbandry")]
        hres=sum(int(c.get("Population",0) or 0) for c in husbandry)
        hanimals=sum(int(c.get("managedAnimals",0) or 0) for c in husbandry)
        hq=(sum((int(c.get("Population",0) or 0))*float(c.get("QoL",0) or 0) for c in husbandry)/hres) if hres else 0
        events=[e for s in samples for e in (s.get("fissionEvents") or [])]
        post_events=[e for s in post for e in (s.get("fissionEvents") or [])]
        triggers=Counter(e.get("trigger","?") for e in events)
        opening_events=[e for s in samples if not s.get("postOpening") for e in (s.get("fissionEvents") or [])]
        exit_events=[e for e in events if e.get("trigger")=="ExitOpportunity"]
        exit_post=[e for e in post_events if e.get("trigger")=="ExitOpportunity"]
        camp_by_id={int(c["Id"]):c for c in camps}
        runs.append({
            "threshold":threshold,"seed":seed,
            "population":int(last.get("population",0) or 0),
            "qol":float(last.get("priorWeightedQol",last.get("weightedQol",0)) or 0),
            "camps":int(last.get("campCount",0) or 0),
            "husbandry_residents":hres,
            "husbandry_qol":hq,
            "husbandry_animals":hanimals,
            "husbandry_settlements":len(husbandry),
            "herd_intensity":hanimals/hres if hres else 0,
            "hpop_settlements":[int(c.get("Population",0) or 0) for c in husbandry],
            "hq_settlements":[float(c.get("QoL",0) or 0) for c in husbandry],
            "hanimals_settlements":[int(c.get("managedAnimals",0) or 0) for c in husbandry],
            "fissions":len(events),"opening_fissions":len(opening_events),
            "exit_fissions":len(exit_events),"post_exit_fissions":len(exit_post),
            "exit_parent_sizes":[int(e.get("ParentBefore",0) or 0) for e in exit_events],
            "triggers":triggers,
        })

    by=defaultdict(list)
    for r in runs: by[r["threshold"]].append(r)

    def world_trip(th,key):
        return triplet([r[key] for r in by.get(th,[])])

    baseline=0.05
    metrics=[
        ("Total population","population",True,0),
        ("Population-weighted QoL","qol",False,4),
        ("Husbandry population","husbandry_residents",True,0),
        ("Husbandry weighted QoL","husbandry_qol",False,4),
        ("Managed Husbandry animals","husbandry_animals",True,0),
        ("Husbandry settlements","husbandry_settlements",True,0),
        ("Animals / Husbandry resident","herd_intensity",False,3),
    ]

    lines=[]
    lines.append("# Exit-opportunity threshold sweep")
    lines.append("")
    lines.append("Same five Continents seeds (11, 23, 47, 101, 1729), 80 founders, production config, opening plus 40 post-opening turns. Only FissionExitThreshold changes; FissionExitSearchFraction remains 0.8.")
    lines.append("")
    lines.append("## World-level P25 / median / P75")
    lines.append("")
    for label,key,integer,digits in metrics:
        base=world_trip(baseline,key)
        lines.append(f"### {label}")
        lines.append("")
        lines.append("| Exit threshold | P25 / median / P75 | Change vs 0.05 at P25 / median / P75 |")
        lines.append("|---:|---:|---:|")
        for th in THRESHOLDS:
            cur=world_trip(th,key)
            lines.append(f"| {th:.3f} | {fmt3(cur,integer,digits)} | {fmtchg(change_triplet(cur,base))} |")
        lines.append("")

    lines.append("## Fragmentation and trigger behavior")
    lines.append("")
    lines.append("| Exit threshold | Mean final camps | Mean Husbandry settlements | Total fissions | Exit fissions | Opening fissions | Exit-parent pop P25 / med / P75 |")
    lines.append("|---:|---:|---:|---:|---:|---:|---:|")
    for th in THRESHOLDS:
        rs=by.get(th,[])
        parents=[x for r in rs for x in r["exit_parent_sizes"]]
        lines.append(f"| {th:.3f} | {statistics.mean(r['camps'] for r in rs):.1f} | {statistics.mean(r['husbandry_settlements'] for r in rs):.1f} | {sum(r['fissions'] for r in rs):,} | {sum(r['exit_fissions'] for r in rs):,} | {sum(r['opening_fissions'] for r in rs):,} | {fmt3(triplet(parents),True,0)} |")
    lines.append("")

    lines.append("## Per-Husbandry-settlement pooled distribution")
    lines.append("")
    lines.append("| Exit threshold | Population P25 / med / P75 | QoL P25 / med / P75 | Managed animals P25 / med / P75 |")
    lines.append("|---:|---:|---:|---:|")
    for th in THRESHOLDS:
        rs=by.get(th,[])
        pops=[x for r in rs for x in r["hpop_settlements"]]
        q=[x for r in rs for x in r["hq_settlements"]]
        ani=[x for r in rs for x in r["hanimals_settlements"]]
        lines.append(f"| {th:.3f} | {fmt3(triplet(pops),True,0)} | {fmt3(triplet(q),False,4)} | {fmt3(triplet(ani),True,0)} |")
    lines.append("")

    lines.append("## Per-seed final population / QoL / Husbandry animals")
    lines.append("")
    lines.append("| Threshold | Seed | Population | QoL | Husbandry population | Husbandry QoL | Managed animals | Herd intensity | Fissions | Exit fissions |")
    lines.append("|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|")
    for th in THRESHOLDS:
        for r in sorted(by.get(th,[]),key=lambda x:x["seed"]):
            lines.append(f"| {th:.3f} | {r['seed']} | {r['population']:,} | {r['qol']:.4f} | {r['husbandry_residents']:,} | {r['husbandry_qol']:.4f} | {r['husbandry_animals']:,} | {r['herd_intensity']:.3f} | {r['fissions']} | {r['exit_fissions']} |")

    out="\n".join(lines)+"\n"
    print(out)
    if args.out:
        os.makedirs(os.path.dirname(args.out) or ".",exist_ok=True)
        open(args.out,"w").write(out)

if __name__=="__main__":
    main()
