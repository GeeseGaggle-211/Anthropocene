#!/usr/bin/env python3
"""Civic and irrigation census summaries/figures from matched native playthroughs."""
import argparse
import collections
import csv
import hashlib
import json
import os
import statistics
from pathlib import Path

os.environ.setdefault("MPLCONFIGDIR", "/tmp/anthropocene-matplotlib")
os.environ.setdefault("XDG_CACHE_HOME", "/tmp/anthropocene-cache")
Path(os.environ["XDG_CACHE_HOME"]).mkdir(parents=True, exist_ok=True)
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np

p = argparse.ArgumentParser()
p.add_argument("directories", nargs="+", type=Path)
p.add_argument("--output", type=Path, default=Path("TestResults/CivicPlaythrough/report"))
args = p.parse_args()
args.output.mkdir(parents=True, exist_ok=True)
runs = {}
provenance = []
for directory in args.directories:
    provenance.append(json.loads((directory / "manifest.json").read_text()))
    for file in sorted(directory.glob("*-result.json")):
        result = json.loads(file.read_text())
        path = file.with_name(file.name.replace("-result.json", ".jsonl"))
        rows = [json.loads(line) for line in path.read_text().splitlines()]
        key = (result["seed"], result["arm"])
        if key in runs:
            raise ValueError(f"Duplicate {key}")
        runs[key] = {"result": result, "rows": rows, "trajectorySha256": hashlib.sha256(path.read_bytes()).hexdigest()}
if len(runs) != 10:
    raise ValueError(f"Expected ten completed runs, found {len(runs)}")

tiers = ["CeremonialIntegration", "CivicOrganization", "TokensAndAccounting", "PlannedQuartersAndDrainage"]
groups = ["incipient", "egalitarian", "mixed", "stratified"]
funnel = ["known", "hasSource", "hasRoute", "dryUnserved", "cultivatedCandidate", "wateredCandidate", "positiveProject", "eligibleNew", "neededNew", "requested", "funded"]

def census(rows):
    villages = [v for r in rows for v in r["villages"]]
    pop = sum(v["population"] for v in villages)
    n = len(villages)
    stats = dict(population=sum(r["population"] for r in rows), villages=n,
                 largest=max((v["population"] for v in villages), default=0),
                 meanComplexity=sum(v["complexity"] for v in villages)/max(1,n),
                 meanStratification=sum(v["stratification"] for v in villages)/max(1,n),
                 populationWeightedComplexity=sum(v["complexity"]*v["population"] for v in villages)/max(1,pop),
                 populationWeightedStratification=sum(v["stratification"]*v["population"] for v in villages)/max(1,pop),
                 meanCivicCoverage=sum(v["civicCoverage"] for v in villages)/max(1,n),
                 meanSanitationCoverage=sum(v["sanitationCoverage"] for v in villages)/max(1,n),
                 stewards=sum(v["stewards"] for v in villages),
                 civicWork=sum(r["civicWork"] for r in rows), sanitationWork=sum(r["sanitationWork"] for r in rows))
    stats.update({g:sum(v["group"]==g for v in villages) for g in groups})
    stats.update({t:sum(t in v["tiers"] for v in villages) for t in tiers})
    stats.update({k:sum(bool(v["irrigation"][k]) for v in villages) for k in funnel})
    stats.update(installedVillages=sum(v["irrigation"]["installed"]>0 for v in villages),
                 wateredVillages=sum(v["irrigation"]["watered"]>0 for v in villages),
                 installedFields=sum(v["irrigation"]["installed"] for v in villages),
                 wateredFields=sum(v["irrigation"]["watered"] for v in villages),
                 installedOrEligible=sum(v["irrigation"]["installed"]>0 or v["irrigation"]["eligibleNew"] for v in villages),
                 eligibleUnused=sum(v["irrigation"]["installed"]==0 and v["irrigation"]["eligibleNew"] for v in villages),
                 installedOrPhysicalCandidate=sum(v["irrigation"]["installed"]>0 or v["irrigation"]["wateredCandidate"] for v in villages),
                 physicalCandidateUnused=sum(v["irrigation"]["installed"]==0 and v["irrigation"]["wateredCandidate"] for v in villages),
                 positiveBlockedByGate=sum(v["irrigation"]["positiveProject"] and not v["irrigation"]["gate"] for v in villages),
                 irrigationReasons=dict(collections.Counter(v["irrigation"]["reason"] for v in villages)))
    stats["largeVillages"] = {g: sum(v["group"]==g and v["population"]>=1000 for v in villages) for g in groups}
    t2 = [v for v in villages if "CivicOrganization" in v["tiers"]]
    stats["medianReservesYearsTier2"] = statistics.median(v["reservesYears"] for v in t2) if t2 else 0
    stats["tradeVolumeSum"] = sum(v["tradeVolume"] for v in villages)
    return stats

seeds = sorted({s for s,a in runs})
series = {}
for arm in ["legacy", "annual"]:
    data = []
    for year in range(0,2001,100):
        rows = [min(runs[s,arm]["rows"], key=lambda r:abs(r["years"]-year)) for s in seeds]
        data.append({"year":year, "actualMinYear": min(r["years"] for r in rows), "actualMaxYear":max(r["years"] for r in rows), **census(rows)})
    series[arm]=data

matched = []
def matched_view(v):
    return {k:v[k] for k in ["id","tile","population","complexity","stratification","group","tiers","drivers"]}
for seed in seeds:
    before = {v["id"]:v for v in runs[seed,"legacy"]["rows"][-1]["villages"] if v["original"]}
    after = {v["id"]:v for v in runs[seed,"annual"]["rows"][-1]["villages"] if v["original"]}
    for cid in sorted(before.keys() & after.keys()):
        a,b=before[cid],after[cid]
        matched.append({"seed":seed,"id":cid,"legacy":matched_view(a),"annual":matched_view(b),
                        "deltaPopulation":b["population"]-a["population"],
                        "deltaComplexity":b["complexity"]-a["complexity"],
                        "deltaStratification":b["stratification"]-a["stratification"]})

summary = {"provenance":provenance,"series":series,"matchedOriginalVillages":matched,
           "remainingWaterableExtensions":[{"seed":s,"arm":a,"id":v['id'],"population":v['population'],**v['irrigation']}
               for (s,a),r in sorted(runs.items()) for v in r['rows'][-1]['villages'] if v['irrigation']['wateredCandidate']],
           "runs":[{"seed":s,"arm":a,**r["result"],"trajectorySha256":r["trajectorySha256"],
                    "firstTiers":r["rows"][-1]["firstTiers"],"final":census([r["rows"][-1]])} for (s,a),r in sorted(runs.items())],
           "definitions":{"year":"physical years since shared civic activation checkpoint, after organic history and the inherited Plough/Irrigation grant",
               "groups":"incipient C<=.05; otherwise egalitarian P<-.25, stratified P>.25, mixed between",
               "eligibleNew":"irrigation technology + positive finite-water new-project forecast + cultivation gate; does not require food gap or funding",
               "watered":"installed field with WaterService>0 at the current boundary",
               "installedOrEligible":"union, not sum: installed irrigation OR currently eligible for an additional project"}}
(args.output/"summary.json").write_text(json.dumps(summary,indent=2)+"\n")
with (args.output/"aggregate.csv").open("w",newline="") as f:
    flat=[{"arm":arm,**{k:v for k,v in row.items() if not isinstance(v,dict)}} for arm,rows in series.items() for row in rows]
    writer=csv.DictWriter(f,fieldnames=list(flat[0]));writer.writeheader();writer.writerows(flat)
with (args.output/"villages.csv").open("w",newline="") as f:
    flat=[]
    for (seed,arm),r in sorted(runs.items()):
        for row in r["rows"]:
            for v in row["villages"]:
                flat.append({"seed":seed,"arm":arm,"years":row["years"],
                    **{k:v[k] for k in ["id","original","population","complexity","stratification","group","civicCoverage","sanitationCoverage","stewards"]},
                    **{t:t in v["tiers"] for t in tiers},**v["irrigation"]})
    writer=csv.DictWriter(f,fieldnames=list(flat[0]));writer.writeheader();writer.writerows(flat)

plt.rcParams.update({"font.family":"DejaVu Sans","axes.spines.top":False,"axes.spines.right":False,"axes.titleweight":"bold","figure.facecolor":"#fafbf9","axes.facecolor":"#fafbf9"})
colors={"legacy":"#947050","annual":"#087f8c"}
fig,axes=plt.subplots(2,2,figsize=(13,8),constrained_layout=True)
for arm,rows in series.items():
    x=[r['year'] for r in rows]
    axes[0,0].plot(x,[r['population']/1000 for r in rows],label=arm,color=colors[arm],lw=2.5)
    axes[0,1].plot(x,[r['populationWeightedComplexity'] for r in rows],label=arm,color=colors[arm],lw=2.5)
    for tier,style in [("CivicOrganization","-"),("PlannedQuartersAndDrainage","--"),("TokensAndAccounting",":")]:
        axes[1,0].plot(x,[r[tier] for r in rows],color=colors[arm],ls=style,label=f"{arm}: {dict(zip(tiers,['T1','T2','T3A','T3B']))[tier]}")
    vs=[v for seed in seeds for v in runs[seed,arm]['rows'][-1]['villages']]
    axes[1,1].scatter([v['stratification'] for v in vs],[v['complexity'] for v in vs],
                     s=[10+v['population']/40 for v in vs],alpha=.5,color=colors[arm],label=arm,edgecolors='white',linewidths=.4)
axes[0,0].set(title="Population across five matched worlds",ylabel="People (thousands)")
axes[0,1].set(title="Civic complexity experienced by residents",ylabel="Population-weighted C",ylim=(0,1))
axes[1,0].set(title="Villages with civic precedents",ylabel="Villages")
axes[1,1].set(title="Civic divergence at year 2,000",xlabel="Stratification P (negative = egalitarian)",ylabel="Complexity C",xlim=(-1,1),ylim=(-.03,1.04))
for threshold in [-.25,.25]: axes[1,1].axvline(threshold,color='#888888',lw=.8,ls=':')
for ax in [axes[0,0],axes[0,1],axes[1,0]]: ax.set_xlabel("Years after civics enabled"); ax.grid(alpha=.15)
for ax in axes.flat: ax.legend(fontsize=8)
fig.suptitle("Headless civic playthrough • annual food enabled at civic onset",fontsize=16)
fig.savefig(args.output/'civic-playthrough.png',dpi=180);plt.close(fig)

labels=[('villages','All villages'),('known','Irrigation knowledge'),('hasSource','Visible renewable source'),('hasRoute','Legal local route'),('wateredCandidate','New watered candidate'),('positiveProject','Positive new-project gain'),('eligibleNew','Positive project + cultivation gate'),('neededNew','Also has a food gap'),('installedVillages','Installed irrigation'),('wateredVillages','Actually watered')]
fig,ax=plt.subplots(figsize=(11,6),constrained_layout=True)
y=np.arange(len(labels))
for i,arm in enumerate(['legacy','annual']):
    vals=[series[arm][-1][k] for k,label in labels]
    bars=ax.barh(y+(i-.5)*.36,vals,height=.35,label=arm,color=colors[arm])
    ax.bar_label(bars,padding=3,fontsize=9)
ax.set_yticks(y,[label for k,label in labels]);ax.invert_yaxis();ax.legend();ax.set_xlabel('Villages across five worlds')
ax.set_title('Irrigation opportunity versus use • year 2,000\nNew-project eligibility excludes targets already served',loc='left')
ax.set_xlim(0,max(series[a][-1]['villages'] for a in series)*1.14)
fig.savefig(args.output/'irrigation-eligibility.png',dpi=180);plt.close(fig)
for arm,rows in series.items():
    print(arm,json.dumps(rows[-1]))
print('Matched original villages',len(matched))
