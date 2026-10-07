#!/usr/bin/env python3
import argparse, glob, json, os, statistics
from collections import Counter, defaultdict

def pct(xs, p):
    if not xs: return None
    xs=sorted(xs)
    return xs[round((len(xs)-1)*p)]

def f(x, nd=3):
    return "n/a" if x is None else f"{x:.{nd}f}"

def main():
    ap=argparse.ArgumentParser()
    ap.add_argument("root")
    ap.add_argument("--out", default="")
    a=ap.parse_args()
    files=glob.glob(os.path.join(a.root,"**","*.json"), recursive=True)
    runs=[]
    for path in files:
        try:
            d=json.load(open(path))
        except Exception:
            continue
        samples=d.get("samples") or []
        if not samples: continue
        # The workflow names result files Rule-Seed.json.
        base=os.path.basename(path)[:-5]
        if "-" not in base: continue
        rule, seed=base.rsplit("-",1)
        if rule not in {"Current","LegacyQoL","CurrentPlusExitOpportunity"}: continue
        post=[s for s in samples if s.get("postOpening")]
        if not post: continue
        trig=Counter()
        parent_sizes=[]; parent_q=[]; daughter_q=[]
        low_escape={.5:0,.6:0,.7:0}; low_total={.5:0,.6:0,.7:0}
        false_early=0
        integrated_nq=0.0
        births=deaths=migrants=fissions=0
        searches=candidates=0
        qseries=[]
        for s in post:
            pop=float(s.get("population",0)); q=float(s.get("priorWeightedQol",s.get("weightedQol",0)) or 0)
            integrated_nq += pop*q
            qseries.append(q)
            births += int(s.get("humanBirths",0) or 0); deaths += int(s.get("humanDeaths",0) or 0)
            migrants += int(s.get("migrantCount",0) or 0)
            searches += int(s.get("fissionSearches",0) or 0); candidates += int(s.get("fissionCandidates",0) or 0)
            camps={int(c["Id"]):c for c in (s.get("camps") or [])}
            events=s.get("fissionEvents") or []
            fissions += len(events)
            for e in events:
                trig[e.get("trigger","?")]+=1
                parent_sizes.append(int(e.get("ParentBefore",0)))
                c=camps.get(int(e.get("ParentCamp",-1)))
                if c is not None:
                    pq=float(c.get("QoL",0) or 0); parent_q.append(pq)
                    for bar in low_total:
                        if pq < bar: low_escape[bar]+=1
                daughter_q.append(float(e.get("DaughterQoL",0) or 0))
                if e.get("regime") in ("Paleolithic","Nomadic") and int(e.get("ParentBefore",0)) <= 60:
                    false_early += 1
            for c in camps.values():
                cq=float(c.get("QoL",0) or 0)
                for bar in low_total:
                    if cq < bar: low_total[bar]+=1
        last=post[-1]
        runs.append(dict(rule=rule,seed=int(seed),final_pop=int(last["population"]),final_camps=int(last["campCount"]),
            final_q=float(last.get("priorWeightedQol",last.get("weightedQol",0)) or 0),
            mean_q=statistics.mean(qseries),nq=integrated_nq,fissions=fissions,trig=trig,
            parent_sizes=parent_sizes,parent_q=parent_q,daughter_q=daughter_q,false_early=false_early,
            births=births,deaths=deaths,migrants=migrants,searches=searches,candidates=candidates,
            low_escape=low_escape,low_total=low_total,total_ms=sum(float(s.get("totalMs",0) or 0) for s in post)))

    by=defaultdict(list)
    for r in runs: by[r["rule"]].append(r)
    order=["Current","LegacyQoL","CurrentPlusExitOpportunity"]
    lines=[]
    lines.append("# Fission pressure matched-world summary")
    lines.append("")
    lines.append(f"Runs parsed: {len(runs)}")
    lines.append("")
    lines.append("| Rule | Worlds | Final pop mean | Final camps mean | Mean prior-cohort QoL | Turn-integrated N×Q | Fissions | Triggers | Parent pop P25/med/P75 | Parent Q med | Daughter Q med | early P/N ≤60 |")
    lines.append("|---|---:|---:|---:|---:|---:|---:|---|---|---:|---:|---:|")
    for rule in order:
        rs=by.get(rule,[])
        if not rs: continue
        ps=[x for r in rs for x in r["parent_sizes"]]
        pqs=[x for r in rs for x in r["parent_q"]]
        dqs=[x for r in rs for x in r["daughter_q"]]
        tr=Counter()
        for r in rs: tr.update(r["trig"])
        trig=", ".join(f"{k}:{v}" for k,v in sorted(tr.items()))
        lines.append(f"| {rule} | {len(rs)} | {statistics.mean(r['final_pop'] for r in rs):,.0f} | {statistics.mean(r['final_camps'] for r in rs):,.1f} | {statistics.mean(r['mean_q'] for r in rs):.3f} | {sum(r['nq'] for r in rs):,.0f} | {sum(r['fissions'] for r in rs)} | {trig} | {pct(ps,.25)}/{pct(ps,.5)}/{pct(ps,.75)} | {f(pct(pqs,.5))} | {f(pct(dqs,.5))} | {sum(r['false_early'] for r in rs)} |")
    lines.append("")
    lines.append("| Rule | Births | Deaths | Migrants | Searches | Candidates | Post-opening total ms |")
    lines.append("|---|---:|---:|---:|---:|---:|---:|")
    for rule in order:
        rs=by.get(rule,[])
        if not rs: continue
        lines.append(f"| {rule} | {sum(r['births'] for r in rs):,} | {sum(r['deaths'] for r in rs):,} | {sum(r['migrants'] for r in rs):,} | {sum(r['searches'] for r in rs):,} | {sum(r['candidates'] for r in rs):,} | {sum(r['total_ms'] for r in rs):,.0f} |")
    lines.append("")
    lines.append("## Per-seed")
    lines.append("")
    lines.append("| Rule | Seed | Final pop | Camps | Final Q | Fissions | N×Q |")
    lines.append("|---|---:|---:|---:|---:|---:|---:|")
    for rule in order:
        for r in sorted(by.get(rule,[]), key=lambda x:x["seed"]):
            lines.append(f"| {rule} | {r['seed']} | {r['final_pop']:,} | {r['final_camps']} | {r['final_q']:.3f} | {r['fissions']} | {r['nq']:,.0f} |")
    out="\n".join(lines)+"\n"
    print(out)
    if a.out:
        os.makedirs(os.path.dirname(a.out) or ".", exist_ok=True)
        open(a.out,"w").write(out)

if __name__=="__main__":
    main()
