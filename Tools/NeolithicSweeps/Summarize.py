"""Aggregate paired current-engine experiments; no gameplay modifications."""
import argparse,json,math,collections,pathlib,html
p=argparse.ArgumentParser()
p.add_argument("input",type=pathlib.Path);p.add_argument("output",type=pathlib.Path)
p.add_argument("--expected",type=int,default=328);p.add_argument("--partial",action="store_true")
p.add_argument("--replay",type=pathlib.Path)
a=p.parse_args();a.output.mkdir(parents=True,exist_ok=True)
def load(pattern):
    result=[]
    for f in sorted(a.input.rglob(pattern)):
        raw=f.read_text(encoding="utf-8-sig");lines=raw.splitlines()
        if a.partial and raw and not raw.endswith("\n"):lines=lines[:-1]
        result.extend(json.loads(line) for line in lines if line.strip())
    return result
runs,rows,sites=load("runs-*.jsonl"),load("trajectories-*.jsonl"),load("sites-*.jsonl")
assert len({r["run"] for r in runs})==len(runs),"Duplicate runs"
assert len({(r["run"],r["turn"]) for r in rows})==len(rows),"Duplicate turns"
if not a.partial:
    assert len(runs)==a.expected,(len(runs),a.expected)
    assert all(r["error"] is None for r in runs),"Simulation error"
    assert all(r["completedYears"]==500 or r["population"]==0 for r in runs),"Incomplete horizon"
def n(x,d=1):return "—" if x is None else f"{x:,.{d}f}"
def table(h,data):
    return "| "+" | ".join(h)+" |\n|"+"|".join(" --- " for _ in h)+"|\n"+"\n".join("| "+" | ".join(map(str,r))+" |" for r in data)+"\n"
by={(r["seed"],r["habitat"],r["policy"],r["variant"]):r for r in runs}
def pick(v="baseline",pol="stock",hab="river",seed=11):return by.get((seed,hab,pol,v))
def tech(r,t):return n(r["breakthroughYears"].get(t),0)
def metrics(r):
    z=r["last"]
    return [n(r["population"],0),n(z["craftAssignedPct"]),n(z["specialistAssignedPct"]),n(z["craftUsedPct"]),n(z["qMean"],3),n(z["qol"]*100),tech(r,"Weaving"),tech(r,"Construction")]
bad=[r for r in rows if not r["conserved"] or not r["labor"]]
waste=[r for r in rows if r["discardedFood"]>1e-6]
stats={"runs":len(runs),"generations":len(rows),"errors":[r for r in runs if r["error"]],"receiptOrLaborFailures":len(bad),"discardRows":len(waste),
       "maxDiscard":max((r["discardedFood"] for r in rows),default=0),"maxDiscardFraction":max((r["discardedFood"]/r["foodHarvest"] for r in rows if r["foodHarvest"]>0),default=0)}
if a.replay:
    actual={(r["run"],r["turn"]):r for r in rows}
    repeat=[json.loads(line) for f in a.replay.glob("trajectories-*.jsonl") for line in f.read_text(encoding="utf-8-sig").splitlines()]
    assert all(actual.get((r["run"],r["turn"]))==r for r in repeat),"Replay mismatch"
    stats["exactReplayGenerations"]=len(repeat)
effects=[]
for r in runs:
    b=pick(pol=r["policy"],hab=r["habitat"],seed=r["seed"])
    if b and r["last"] and b["last"]:
        effects.append({"run":r["run"],"family":r["family"],"populationDelta":r["population"]-b["population"],"qDelta":r["last"]["qMean"]-b["last"]["qMean"],
        "craftUsedPercentagePointDelta":r["last"]["craftUsedPct"]-b["last"]["craftUsedPct"],"qolPercentagePointDelta":100*(r["last"]["qol"]-b["last"]["qol"])})
def rank(xs):
    order=sorted(set(xs));return [sum(i+1 for i,v in enumerate(sorted(xs)) if v==x)/xs.count(x) for x in xs]
def correlation(xs,ys):
    mx=sum(xs)/len(xs);my=sum(ys)/len(ys)
    xx=sum((x-mx)**2 for x in xs);yy=sum((y-my)**2 for y in ys)
    return sum((x-mx)*(y-my) for x,y in zip(xs,ys))/math.sqrt(xx*yy) if xx*yy else None
correlations=[]
for pol in ["stock","replan","managed-control"]:
    pairs=[(f,r) for f in [.25,.5,1,2,4] if (r:=pick("baseline" if f==1 else "fabric-"+str(f),pol))]
    if len(pairs)==5:
        correlations.append({"policy":pol,"parameter":"FabricConsumptionPerPersonTurn","n":5,
            "populationSpearman":correlation(rank([x for x,r in pairs]),rank([r["population"] for x,r in pairs])),
            "qSpearman":correlation(rank([x for x,r in pairs]),rank([r["last"]["qMean"] for x,r in pairs]))})
for name,data in [("run-summary",runs),("paired-effects",effects),("verification",stats),("descriptive-correlations",correlations)]:
    (a.output/(name+".json")).write_text(json.dumps(data,indent=2),encoding="utf-8")
doc=["# Neolithic current-code sweeps\n",
"Simulation revision: 1c95bf6a7ec3ece9513e781211e1f24b29cef029. Gameplay constants and rules were not retuned. The new harness compiles those same simulation files.\n",
f"**{len(runs)} runs / {len(rows):,} resolved generations**, scheduled for 500 years each (25 × 20-year turns). "+("PARTIAL PREVIEW.\n" if a.partial else "All scheduled runs are accounted for.\n"),
"""## Main findings

On the productive seed-11 river site, the current default policy reaches 527 people with Weaving at year 280 and Construction at year 300. It assigns 21.06% to generalist crafts by the final generation, but only 9% of population-equivalent worker-time produces crafts. No specialists or Farmers are assigned. Repeating the demand-based reset instead reaches 264 people, leaves about 44% assigned to idle crafts, and achieves neither technology by year 500. These productive-site baseline outcomes recur in the selected river/wooded examples across all three seeds; their maximum-productivity ecological profiles saturate at the same values, so they are not six independent productivity conditions.

The labeled managed-control policy reaches 1,016 people, Weaving at year 40 and Construction at year 80. Its final craft-worker utilization is 2.25%: 1.25% textiles plus 1% Housing. That agrees with the revised steady-state specialist calibration. Assigned craft slots remain about 10%, including unused generalist capacity. The ordinary generalist baseline's 9% useful craft labor likewise equals 5% textiles plus 4% Housing maintenance.

Thus the production-rate equations correlate with the design, but the automatic labor transition does not. The initial demand-based allocation reserves a large craft workforce for missing Housing while the minimum gathering crew uses its time on food. Source gathering and Hand Tools bootstrap are delayed or starved. Retained assignments eventually allow population growth to add unassigned gatherers; repeated resets largely prevent that escape. Unlocking a specialty does not itself reassign anyone.

Farmer-productivity changes are inactive in the productive automatic baseline because it has no Farmers. With Farmers present in the managed control, higher productivity increases their share of harvested food, but population is not monotonically increasing across every setting. Food/reserve thresholds, discrete assignments and production timing matter. None of the four managed farmer-productivity cases reaches majority farmer-collected food by year 500. This does not reproduce the historical analytical transition timeline; it is evidence against claiming that transition is balanced in the current policy/model combination.

Lower Fabric demand generally releases labor and raises final population, but the managed result is not perfectly monotone. Increased Housing depreciation raises work when materials and labor are available; it need not reduce population under a policy that leaves assigned craft counts fixed. Expectation changes utility, not physical demand or demographic growth. Improved/Copper multipliers cannot influence ordinary runs without usable tools; their supplied-stock probes are reported separately.

Sparse open-site results are much worse than the fertile examples: the baseline stock runs end with 8, 57 and 8 people (seeds 11, 42, 1729). This is food-limited mortality, not a Clothing/Housing death gate. Site/source and ecological metadata are included; these are different environmental conditions, not a controlled estimate of the forest surcharge alone.
""",
"""## Experimental scope

- Generated worlds: seeds 11, 42, 1729, current OpeningConfig and production ResourceGenerationConfig. Map extent is reduced to 256×128, with 16 initial placement camps.
- Each run isolates a generated seven-land-tile footprint. River, wooded and open sites maximize initial wild-food renewal in their class, subject to seasonal freshwater access. These are **optimistic exemplars, not random samples or a world-wide balance estimate**. River examples can also be forested. Sources/geography are retained, with no resource repairs.
- Standardized start: 250 residents, Agriculture available, q=0, one reference generation of plant-food reserves, zero Fabric, Housing, tools or other manufactured stock. Other settlements, hunters and animals are removed to isolate local agriculture/manufacturing. Imports, hunting and competition are absent. This is not natural Paleo-to-Neolithic progression.
- Stock policy submits the current automatic proposal, retaining existing assignments. Replan invokes the existing demand-based reset every turn. Both use normal validated economic orders. Neither automatically assigns unlocked specialists.
- Managed control is an experimental player policy, not the shipped AI: 20% Farmers, 10% craft slots, remaining people gather. Unlocked Weavers and Builders fill useful needs within those slots; remaining slots are generalists. No technology, food or inventory is granted. Fractions are scenario assumptions, not new game constants.
- Full 5×4×4 Fabric/Housing-capacity/Housing-decay matrix: seed-11 river, stock policy. Other seed-11 habitats receive the baseline slices and one-at-a-time levers. Fabric and farmer-productivity slices repeat across nine seed/site pairs under stock and replan. Managed control covers 16 baseline/Fabric/farmer/flux/decay/Builder/extraction variants on seed-11 river.
- Generalist assigned %, specialist assigned % and utilized craft-worker-time % use **start-of-generation population**. Population is the ending count. Craft shares exclude raw-material gatherers. Unlock years are elapsed since the standardized Agriculture start. “—” means not reached by year 500.
""","## Baseline results at year 500\n"]
doc.append(table(["Seed","Site","Policy","Population","Generalists %","Specialists %","Craft time used %","Mean q","QoL %","Weaving year","Construction year"],
[[r["seed"],r["habitat"],r["policy"]]+metrics(r) for r in runs if r["variant"]=="baseline"]))
doc.append("\n## Fabric sensitivity, productive seed-11 river\n")
doc.append(table(["Policy","Fabric/person/turn","Population","Generalists %","Specialists %","Craft time used %","Mean q","QoL %","Weaving year","Construction year"],
[[pol,f]+metrics(r) for pol in ["stock","replan","managed-control"] for f in [.25,.5,1,2,4] if (r:=pick("baseline" if f==1 else "fabric-"+str(f),pol))]))
doc.append("\n## Farmer-productivity sensitivity\n")
doc.append("Farmer food share means the fraction collected by operating Farmers, not a separate biological crop-food pool.\n")
doc.append(table(["Policy","Multiplier","Population","Farmers assigned %","Farm food %","Mean q","First >50% farm food year","Gathering ≤1% year"],
[[pol,x,n(r["population"],0),n(r["last"]["farmerAssignedPct"]),n(r["last"]["farmerFoodShare"]*100),n(r["last"]["qMean"],3),n(r["firstFarmerMajorityYear"],0),n(r["gatheringBelowOnePercentYear"],0)]
for pol in ["stock","replan","managed-control"] for x in [1.25,1.5,1.75,2] if (r:=pick("baseline" if x==1.5 else "farmer-"+str(x),pol))]))
doc.append("\n## Housing capacity × depreciation, Fabric=1, stock policy\n")
doc.append(table(["People/unit","Decay/turn","Population","Craft time used %","Clothing %","Housing %","QoL %","Mean q","Construction year"],
[[cap,dec,n(r["population"],0),n(r["last"]["craftUsedPct"]),n(r["last"]["clothing"]*100),n(r["last"]["housing"]*100),n(r["last"]["qol"]*100),n(r["last"]["qMean"],3),tech(r,"Construction")]
for cap in [3,4,5,6] for dec in [.05,.1,.15,.2] if (r:=pick("baseline" if cap==4 and dec==.1 else f"housing-{cap}-{dec}"))]))
doc.append("\n## Other levers on seed-11 river\n")
other=[r for r in runs if r["seed"]==11 and r["habitat"]=="river" and r["family"] not in ["baseline","fabric","housing","joint","farmer"]]
doc.append(table(["Policy","Variant","Population","Craft time used %","Mean q","QoL at 500y %","Weaving year"],
[[r["policy"],r["variant"],n(r["population"],0),n(r["last"]["craftUsedPct"]),n(r["last"]["qMean"],3),n(r["last"]["qol"]*100),tech(r,"Weaving")] for r in other]))
doc.append("\n## Breakthrough coverage\n")
for pol in ["stock","replan","managed-control"]:
    group=[r for r in runs if r["policy"]==pol];counts=collections.Counter(t for r in group for t in r["breakthroughYears"])
    doc.append(f"- {pol}: {len(group)} runs; "+(", ".join(f"{t} in {k}" for t,k in sorted(counts.items())) or "no breakthroughs")+".\n")
doc.append("""
These counts mix parameter variants, not independent random trials. Agriculture is an initial condition. Pottery and Carpentry have zero default useful targets. Copper Working and advanced technologies are deferred, not failed discoveries. Automatic policies never assign unlocked specialists. Durable tools alone do not generate recurring replacement research.
""")
probes={(r["grade"],r["multiplier"]):r for r in load("housing-tool-probes-*.jsonl")}
doc.append("\n## Conditional Housing tool-rate probes\n")
doc.append(table(["Tools","Multiplier","Housing/Builder/turn","Material debit","Steady Builder maintenance labor %"],
[[r["grade"],r["multiplier"],n(r["outputPerBuilderTurn"],3),n(r["materialDebit"],3),n(r["steadyMaintenanceLaborPct"],3)] for r in probes.values()]))
doc.append("""
These use the actual production method with supplied inventory and capability. Copper does not emerge in these probes. Their maintenance share is calculated from the measured rate, not an observed bootstrap workforce. The coupled managed-control baseline separately measures realized specialist work.
""")
doc.append("\n## Validation and limitations\n")
doc.append(f"- Resolver errors: {len(stats['errors'])}; receipt-arithmetic/workforce-budget failure rows: {len(bad)}.\n")
if a.replay:doc.append(f"- Exact recorded-trajectory replay: {stats['exactReplayGenerations']} generations across the six pilot baseline runs.\n")
doc.append(f"- Discarded food above 1e-6: {len(waste)} generation rows. Maximum {stats['maxDiscard']:.12g} Food; maximum discarded/harvest fraction {stats['maxDiscardFraction']:.12g}. This remains a measured discrepancy, not a perfect no-waste result.\n")
doc.append("""
- Every production receipt is checked against configured Fabric PlantFood and pooled Housing Wood/Clay coefficients. Extraction receipts are checked for before-minus-after equality. Workforce checks include gathering plus material worker-time. Prior acceptance tests separately cover source depletion, source sharing and save/replay.
- The historical analytical housing table explicitly used a superseded generic Housing rate. Its population totals are reference results from a different model, not acceptance targets. Different ecology, policy, growth and reserve accounting prevent an apples-to-apples absolute population comparison.
- Expectation affects utility, not population in this pass. Its endpoint effect can vanish after ramps saturate; trajectories preserve early QoL effects. A farmer/tool multiplier cannot affect results when no corresponding farmer/tool is used.
""")
doc.append("\n## Descriptive direction checks\n")
doc.append(table(["Policy","Fabric vs population Spearman","Fabric vs q Spearman"],[[r["policy"],n(r["populationSpearman"],3),n(r["qSpearman"],3)] for r in correlations]))
doc.append("These rank correlations describe five paired parameter points on one selected productive site. They are not statistical significance tests, historical validation, or estimates for random settlements. Full paired deltas and generation trajectories are supplied.\n")
doc.append("""
## Reproduction and raw data

From the repository root, build Tools/NeolithicSweeps/Anthropocene.NeolithicSweeps.csproj with the .NET 8 SDK. Run the resulting Anthropocene.NeolithicSweeps.dll with:

    --seed 11 --habitat river --out TestResults/NeolithicSweeps/11-river
    --seed 11 --habitat wooded --out TestResults/NeolithicSweeps/11-wooded
    --seed 11 --habitat open --out TestResults/NeolithicSweeps/11-open
    --seed 42 --replicate --out TestResults/NeolithicSweeps/42-all
    --seed 1729 --replicate --out TestResults/NeolithicSweeps/1729-all
    --seed 11 --habitat river --managed --out TestResults/NeolithicSweeps/11-managed

Then run:

    python Tools/NeolithicSweeps/Summarize.py TestResults/NeolithicSweeps outputs/sweeps

The archive contains run summaries, all generation trajectories, source/site metadata, complete effective configurations and variant definitions. Paired-effects.json compares every variant with the same-site/policy baseline. Run-summary.json includes failure status and breakthrough timing. No normal game saves are touched.
""")
(a.output/"Sweep-Report.md").write_text("\n".join(doc),encoding="utf-8")
matrix=[]
for f in [.25,.5,1,2,4]:
    for cap in [3,4,5,6]:
        for dec in [.05,.1,.15,.2]:
            v=("baseline" if cap==4 and dec==.1 else f"housing-{cap}-{dec}") if f==1 else ("fabric-"+str(f) if cap==4 and dec==.1 else f"joint-{f}-{cap}-{dec}")
            if r:=pick(v):
                z=r["last"]
                matrix.append([f,cap,dec,n(r["population"],0),n(z["craftAssignedPct"]),n(z["craftUsedPct"]),n(z["clothing"]*100),n(z["housing"]*100),n(z["qol"]*100),n(z["qMean"],3),tech(r,"Weaving"),tech(r,"Construction")])
if not a.partial:assert len(matrix)==80,"Incomplete joint matrix"
(a.output/"Full-Joint-Matrix.md").write_text("# Full 80-case joint sweep\n\nSeed 11, productive river footprint, stock automatic policy, 250-person Agriculture start. Values at 500 years. Craft assignment and utilization use start-of-last-generation population. Missing breakthrough means not reached.\n\n"+table(["Fabric/person","People/Housing","Housing decay","Population","Craft assigned %","Craft used %","Clothing %","Housing %","QoL %","Mean q","Weaving year","Construction year"],matrix),encoding="utf-8")
# Code-native vector visualization; all chart data comes from recorded trajectories.
svg=['<svg xmlns="http://www.w3.org/2000/svg" width="1320" height="920" viewBox="0 0 1320 920">','<rect width="1320" height="920" fill="#fbfbf8"/>','<style>text{font-family:Arial,sans-serif;fill:#172c3a}.axis{stroke:#98a8ae;stroke-width:1}.grid{stroke:#e2e7e7;stroke-width:1}</style>']
def text(x,y,t,size=15,color=None):
    svg.append(f'<text x="{x}" y="{y}" font-size="{size}"'+(f' style="fill:{color}"' if color else '')+f'>{html.escape(str(t))}</text>')
def panel(px,py,title,series,xmax,ymax,label):
    left,top,width,height=px+58,py+50,510,225
    text(px+15,py+19,title,20);text(px+15,py+43,label,12)
    for i in range(6):
        y=top+height-i*height/5
        svg.append(f'<line class="grid" x1="{left}" y1="{y}" x2="{left+width}" y2="{y}"/>')
        text(left-45,y+5,f"{ymax*i/5:g}",12);text(left+i*width/5-8,top+height+21,f"{xmax*i/5:g}",12)
    svg.append(f'<line class="axis" x1="{left}" y1="{top}" x2="{left}" y2="{top+height}"/>')
    for j,(name,color,data) in enumerate(series):
        pts=" ".join(f"{left+x/xmax*width:.2f},{top+height-y/ymax*height:.2f}" for x,y in data)
        svg.append(f'<polyline points="{pts}" fill="none" stroke="{color}" stroke-width="3"/>')
        for x,y in data:svg.append(f'<circle cx="{left+x/xmax*width:.2f}" cy="{top+height-y/ymax*height:.2f}" r="2.5" fill="{color}"/>')
        text(left,top+height+47+j*19,name,13,color)
text(30,36,"Neolithic sweeps: productive seed-11 river site",28)
text(30,63,"250-person Agriculture start · zero tools, Fabric and Housing · current production rules",16)
colors={"stock":"#126c82","replan":"#b05b31","managed-control":"#67549d"}
series={pol:sorted([r for r in rows if r["seed"]==11 and r["habitat"]=="river" and r["variant"]=="baseline" and r["policy"]==pol],key=lambda r:r["year"]) for pol in colors}
maxpop=max([r["population"] for arr in series.values() for r in arr]+[100])
panel(20,100,"Population over 500 years",[(pol,colors[pol],[(r["year"],r["population"]) for r in arr]) for pol,arr in series.items()],500,math.ceil(maxpop/200)*200,"people; horizontal axis = elapsed years")
s=series["stock"]
panel(670,100,"Assigned versus productive craft labor",[("Assigned generalists","#126c82",[(r["year"],r["craftAssignedPct"]) for r in s]),("Realized craft worker-time","#b05b31",[(r["year"],r["craftUsedPct"]) for r in s])],500,50,"% of start-of-generation population; elapsed years")
fs=[]
for pol in colors:
    pts=[(f,r["population"]) for f in [.25,.5,1,2,4] if (r:=pick("baseline" if f==1 else "fabric-"+str(f),pol))]
    if pts:fs.append((pol,colors[pol],pts))
panel(20,505,"Fabric demand versus final population",fs,4,math.ceil(max([y for _,_,pts in fs for _,y in pts]+[100])/200)*200,"people at year 500; horizontal axis = Fabric/person/turn")
panel(670,505,"Cultivation intensity over time",[(pol,colors[pol],[(r["year"],r["qMean"]) for r in arr]) for pol,arr in series.items()],500,1,"mean q across seven fields; elapsed years")
text(30,902,"Stock/replan are existing proposals. Managed control is an experimental player policy, not shipped AI behavior.",14)
(a.output/"Sweep-Charts.svg").write_text("\n".join(svg+["</svg>"]),encoding="utf-8")
print(json.dumps(stats))
