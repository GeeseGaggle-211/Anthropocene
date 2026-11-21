"""Local, self-contained viewer for committed headless gameplay captures."""
import json
import argparse
from html import escape
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--root', default='TestResults/AgriculturalGameplay')
parser.add_argument('--focus', type=int, default=52)
parser.add_argument('--single', action='store_true')
parser.add_argument('--corrected', action='store_true')
parser.add_argument('--validation-note', default='')
args = parser.parse_args()
root = Path(args.root)
focus = args.focus
natural = json.loads((root/('replay.json' if args.single else 'natural/replay.json')).read_text())
matched = None if args.single else json.loads((root/'matched/replay.json').read_text())
# The viewer keeps local snapshots; full captures remain beside it for audit.
for capture in [natural] + ([matched] if matched else []):
    width = capture['width']
    centers = {c['tile'] for run in capture['runs'] for f in run['frames'] for c in f['camps'] if c['id'] == focus}
    def near(tile):
        for center in centers:
            dr = tile // width - center // width
            dq = tile % width - center % width
            if min((abs(dq+k*width)+abs(dr)+abs(dq+k*width+dr))/2 for k in (-1,0,1)) <= 4: return True
        return False
    capture['geography'] = [t for t in capture['geography'] if near(t['id'])]
    for run in capture['runs']:
        for frame in run['frames']:
            frame['camps'] = [{k:v for k,v in c.items() if k != 'status'} for c in frame['camps'] if c['id'] == focus or near(c['tile'])]
            frame['fields'] = [f for f in frame['fields'] if near(f['tile'])]
            frame['canals'] = [c for c in frame['canals'] if c['owner'] == focus]
            frame['irrigationFields'] = [f for f in frame['irrigationFields'] if f['owner'] == focus]
            frame['irrigationWater'] = [r for r in frame['irrigationWater'] if r['Operator'] == focus]
            frame['production'] = [r for r in frame['production'] or [] if r['camp'] == focus]
            frame['decisions'] = [r for r in frame['decisions'] if r['camp'] == focus]
            frame.pop('foodReceipts',None)
            frame['events'] = [e for e in frame['events'] if 'attained' in e or 'Village' in e]
data = {'natural': natural, 'matched': matched, 'focus': focus}
html = r'''<!doctype html><html lang="en"><meta charset="utf-8"><title>Anthropocene · Headless gameplay</title>
<style>
*{box-sizing:border-box}body{margin:0;background:#10191d;color:#e7ede7;font:14px system-ui,sans-serif}main{max-width:1440px;margin:auto;padding:24px 30px}header{display:flex;justify-content:space-between;align-items:center;margin-bottom:16px}h1{font-size:27px;font-weight:650;margin:5px 0}h2{font-size:16px;font-weight:600;margin:0 0 12px}p{margin:7px 0;line-height:1.5}.eyebrow{font-size:11px;letter-spacing:2px;color:#adc3b4;text-transform:uppercase}.muted{color:#9cafa9}.badge{padding:7px 12px;border:1px solid #345146;border-radius:20px;color:#a3d8bb;font-size:12px}.notice{background:#35261f;border:1px solid #77533b;color:#f4c49c;padding:12px 16px;border-radius:8px;margin:15px 0}.controls{display:flex;align-items:center;gap:12px;margin:18px 0}button,select{background:#24352e;color:#eef4ed;border:1px solid #4a6255;padding:8px 12px;border-radius:5px;font:inherit;cursor:pointer}button:hover{background:#385040}input{accent-color:#9ace9d;flex:1}#clock{min-width:165px;text-align:right;font-variant-numeric:tabular-nums}.layout{display:grid;grid-template-columns:1.3fr 1fr;gap:16px}.panel{border:1px solid #2d403a;border-radius:9px;background:#17231f;padding:18px;overflow:hidden}.maphead{display:flex;justify-content:space-between}.stats{display:grid;grid-template-columns:repeat(4,1fr);gap:10px;margin:16px 0}.stat{background:#1d2e27;border:1px solid #304a3e;border-radius:7px;padding:12px}.stat .value{font-size:23px;margin:5px 0 2px;font-variant-numeric:tabular-nums}.stat small{color:#a9b9b1}.legend{display:flex;gap:20px;color:#b5c5bb;font-size:12px;margin-top:8px}.legend b{margin-right:5px}.blue{color:#73d6e9}.gold{color:#dfb96a}svg{width:100%;display:block}#map{height:365px}#chart{height:150px}table{border-collapse:collapse;width:100%;margin-top:10px;font-variant-numeric:tabular-nums}th,td{text-align:left;border-bottom:1px solid #304337;padding:8px 4px}th{color:#9fb6a7;font-size:12px;font-weight:500}.value{text-align:right}.small{font-size:12px}.events{max-height:100px;overflow:auto}.event{padding:7px 0;border-bottom:1px solid #304337}#scope{font-size:12px;color:#a8bbb0;max-width:1000px}footer{margin-top:18px;color:#8ea59a;font-size:12px}.line{stroke:#395045;stroke-width:1}#detail{min-height:47px}.pill{border:1px solid #3f5a48;padding:4px 8px;border-radius:5px;margin-right:6px;display:inline-block;margin-bottom:6px}.disabled{opacity:.45}@media(max-width:900px){.layout{grid-template-columns:1fr}.controls{flex-wrap:wrap}.stats{grid-template-columns:repeat(2,1fr)}}
</style><main>
<header><div><div class="eyebrow">Anthropocene · recorded simulation</div><h1>Plough & Elder irrigation</h1><p class="muted">Seed 23 · Village 52 · smoothed calendar</p></div><span class="badge">Actual headless turns · local replay</span></header>
<div class="notice"><strong>Review finding:</strong> construction materials omit prepaid commissioning upkeep. The canal advances in tiny increments before activation. This recording preserves that bug; publication is paused.</div>
<div class="controls"><select id="scenario"><option value="natural">Fresh-world playthrough</option><option value="matched">Matched saved-game comparison</option></select><select id="treatment"></select><button id="prev">←</button><button id="play">Play</button><button id="next">→</button><input id="step" type="range" min="0" value="0" aria-label="Replay turn"><span id="clock"></span></div>
<p id="scope"></p><div class="stats" id="stats"></div>
<div class="layout"><section class="panel"><div class="maphead"><h2>Village footprint & canal route</h2><span class="small muted">Developer map · no Unity rendering</span></div><svg id="map" viewBox="0 0 650 365"></svg><div class="legend"><span class="gold"><b>⬡</b>Cultivation q</span><span class="blue"><b>━</b>Canal</span><span><b>◆</b>Village</span><span class="blue"><b>●</b>River intake</span></div><p id="detail" class="small muted"></p></section>
<section class="panel"><h2>Elder decisions & paid work</h2><div id="tech"></div><p id="gate"></p><div id="decision"></div><table><thead><tr><th>Work completed this interval</th><th class="value">Materials</th><th class="value">Builder-years</th></tr></thead><tbody id="work"></tbody></table><p id="water" class="small"></p><p id="soil" class="small"></p><p class="small muted">Plough benefits require usable Farm Tools. Gate values are from the prior boundary. Receipts and inventories are from the committed turn.</p></section>
<section class="panel"><h2>Construction progress & water delivery</h2><svg id="chart" viewBox="0 0 650 150"></svg><div class="legend"><span class="gold">— First canal progress</span><span class="blue">— Water delivered per year</span></div></section><section class="panel"><h2>Turn events</h2><div class="events" id="events"></div><p class="small muted" id="comparison"></p></section></div>
<footer>Replay controls only inspect saved snapshots. They do not issue gameplay orders. Years are physical elapsed years since world generation; each step is one actual committed turn. No network requests or external services are used.</footer>
</main><script>
const DATA=__DATA__;
const $=id=>document.getElementById(id), fmt=(n,d=1)=>Number(n||0).toLocaleString('en-US',{minimumFractionDigits:d,maximumFractionDigits:d}), esc=s=>String(s).replace(/[&<>"']/g,x=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[x]));
let timer=null; const focus=DATA.focus; function current(){return DATA[$('scenario').value]}function run(){return current().runs[+$('treatment').value||0]}
function initialize(){let d=current();$('treatment').innerHTML=d.runs.map((r,i)=>`<option value="${i}">${r.name==='natural'?'Organic discovery':r.name==='control'?'Both effects disabled':'Plough + water + soil'}</option>`).join('');$('scope').textContent=d.scope;let frames=run().frames;let first=frames.findIndex(f=>f.canals.some(c=>c.owner===focus));$('step').value=Math.max(0,first);update()}
function update(){let d=current(),r=run(),fs=r.frames,i=Math.min(+$('step').value,fs.length-1),f=fs[i],before=fs[Math.max(0,i-1)],c=f.camps.find(c=>c.id===focus),prior=before.camps.find(c=>c.id===focus);$('step').max=fs.length-1;$('step').value=i;$('clock').innerHTML=`<strong>Year ${fmt(f.year,2)}</strong><br><small class="muted">Turn ${f.turn} · ${fmt(f.duration,2)} years</small>`;
const p=f.production||[], owned=f.canals.filter(x=>x.owner===focus), water=f.irrigationWater.filter(x=>x.Operator===focus), fields=f.irrigationFields.filter(x=>x.owner===focus);
$('stats').innerHTML=[['Residents',c?fmt(c.population,0):'—','village 52'],['Food reserves',c?fmt(c.food,0):'—','abstract food units'],['Farm Tools',c?fmt(c.tools,2):'—','same inventory / recipe'],['Canals',`${owned.filter(s=>s.progress>=1-1e-9).length} / ${owned.length}`,'complete / started']].map(([a,b,t])=>`<div class="stat"><small>${a}</small><div class="value">${b}</div><small>${t}</small></div>`).join('');
$('tech').innerHTML=`<span class="pill ${c?.plough?'':'disabled'}">Plough ${c?.plough?'enabled / known':'inactive'}</span><span class="pill ${c?.irrigation?'':'disabled'}">Irrigation ${c?.irrigation?'known':'not known'}</span>`;
let fertile=prior?.fertile||[];$('gate').innerHTML=prior?`<strong>Fertile-tile gate: ${prior.fertileGate?'passed':'not passed'}</strong><br><span class="small muted">${fertile.length} legal naturally fertile tiles${fertile.length?', lowest q = '+fmt(Math.min(...fertile.map(t=>t.q)),3):' (empty set)'}; threshold 0.300.</span>`:'Village not founded yet.';
let dec=(f.decisions||[]).find(x=>x.camp===focus);$('decision').innerHTML=dec?.requests.length?dec.requests.map(q=>`<p class="small"><strong>${esc(q.service)}</strong>: ${fmt(q.granted,5)} authorized of ${fmt(q.wanted,5)} requested${q.unmet?' · '+esc(q.unmet):''}.</p>`).join(''):'<p class="small muted">No irrigation request in the recorded plan.</p>';
let jobs=p.filter(p=>p.camp===focus&&p.good.startsWith('Canal'));$('work').innerHTML=jobs.length?jobs.map(p=>`<tr><td>${p.good==='CanalConstruction'?'Build '+fmt(p.output,6)+' segment':'Fund '+fmt(p.output,3)+' service-years'}</td><td class="value">${fmt(p.materials,4)}</td><td class="value">${fmt(p.workerYears,4)}</td></tr>`).join(''):'<tr><td colspan="3" class="muted">No canal work this interval</td></tr>';
$('water').innerHTML=`<span class="blue">Water:</span> ${fmt(water.reduce((a,x)=>a+x.Withdrawal,0))} withdrawn = ${fmt(water.reduce((a,x)=>a+x.Delivery,0))} delivered + ${fmt(water.reduce((a,x)=>a+x.Loss,0))} lost.`;
$('soil').innerHTML=`Soil development: ${fields.length?fields.map(x=>'tile '+x.tile+': '+fmt(x.soil*100,2)+'%').join(' · '):'no activated field service'}.`;
$('events').innerHTML=(f.events.filter(e=>/attained|Village|irrigation/i.test(e)).map(e=>`<div class="event small">${esc(e)}</div>`).join('')||'<p class="small muted">No technology or village milestone this turn.</p>')+jobs.filter(x=>x.good==='CanalConstruction').map(j=>`<div class="event small gold">Village ${j.camp}: ${fmt(j.output*100,6)}% of a segment built.</div>`).join('');
if(d.runs.length>1){let a=d.runs[0].frames[i],b=d.runs[1].frames[i],ac=a.camps.find(c=>c.id===focus),bc=b.camps.find(c=>c.id===focus);$('comparison').textContent=ac&&bc?`Same physical date: village reserves ${fmt(ac.food,0)} enabled / ${fmt(bc.food,0)} control. World population ${fmt(a.population,0)} / ${fmt(b.population,0)}. Population is not the benefit criterion.`:''}else $('comparison').textContent='Fresh generated world. No diagnostic knowledge grants, injected resources, or direct canal orders.';
map(d,f,c||fs[fs.length-1].camps.find(c=>c.id===focus));chart(fs,i);
}
function map(d,f,c){if(!c){$('map').innerHTML='';return}let center=c.tile, width=d.width, cq=center%width, cr=Math.floor(center/width), coords=id=>{let q=id%width-cq,r=Math.floor(id/width)-cr;q=[q-width,q,q+width].sort((a,b)=>(Math.abs(a)+Math.abs(a+r))-(Math.abs(b)+Math.abs(b+r)))[0];return {q,r,x:325+47*(q+r/2),y:180+41*r,dist:(Math.abs(q)+Math.abs(r)+Math.abs(q+r))/2}}, field=new Map(f.fields.map(x=>[x.tile,x])), serviced=new Map(f.irrigationFields.map(x=>[x.tile,x])), colors={Ocean:'#143b49',Shallows:'#205363',Lake:'#2b5d69',Grassland:'#344b32',Woodland:'#254833',Jungle:'#1e473a',Desert:'#605942',Tundra:'#4e5b55',Mountain:'#5b5b58',Ice:'#768d8c'};
let parts=[];for(let t of d.geography){let a=coords(t.id);if(a.dist>4)continue;let v=field.get(t.id), fill=v&&v.owner===focus?'#79643d':colors[t.terrain]||'#344b32';let points=Array.from({length:6},(_,i)=>{let an=(i*60+30)*Math.PI/180;return `${a.x+26*Math.cos(an)},${a.y+26*Math.sin(an)}`}).join(' ');parts.push(`<polygon points="${points}" fill="${fill}" stroke="${serviced.has(t.id)?'#78d5d8':'#74816e'}" stroke-width="${serviced.has(t.id)?2:0.7}"><title>Tile ${t.id} · ${t.terrain} · natural moisture ${fmt(t.moisture,3)}</title></polygon>`);if(t.river||t.source==='River')parts.push(`<circle cx="${a.x}" cy="${a.y+14}" r="3" fill="#76d3e6"/>`);if(v&&v.owner===focus)parts.push(`<text x="${a.x}" y="${a.y+5}" fill="#f6dd9d" font-size="12" text-anchor="middle">${fmt(v.q,2)}</text>`)}
for(let s of f.canals.filter(s=>s.owner===focus)){let a=coords(s.from),b=coords(s.tile);parts.push(`<line x1="${a.x}" y1="${a.y}" x2="${b.x}" y2="${b.y}" stroke="#75dcf2" stroke-width="5" ${s.progress<1-1e-9?'stroke-dasharray="5 4"':''}/><circle cx="${a.x}" cy="${a.y}" r="4" fill="#9ce6ed"/>`)}
for(let cc of f.camps){let a=coords(cc.tile);if(a.dist>4)continue;parts.push(`<path d="M${a.x},${a.y-12}l7 7 -7 7 -7 -7z" fill="${cc.id===focus?'#ffffff':'#a4b2a5'}" stroke="#10191d" stroke-width="1.5"/><text x="${a.x}" y="${a.y-18}" text-anchor="middle" fill="#fff" font-size="11">${cc.id}</text>`)}$('map').innerHTML=parts.join('');$('detail').textContent=f.canals.filter(s=>s.owner===focus).map(s=>`Canal ${s.id}: tile ${s.source} → ${s.tile}; ${(s.progress*100).toFixed(6)}% built, ${fmt(s.funded,2)} funded years.`).join(' ')||'No canal infrastructure at this boundary. Numbers on fields show actual cultivation intensity q.';
}
function chart(fs,i){let x=y=>40+(y-fs[0].year)/Math.max(1,fs.at(-1).year-fs[0].year)*570, a=fs.map(f=>f.canals.find(s=>s.owner===focus)?.progress||0), b=fs.map(f=>f.irrigationWater.filter(x=>x.Operator===focus).reduce((a,x)=>a+x.Delivery,0)/Math.max(.001,f.duration)), max=Math.max(1,...b), pts=v=>v.map((v,j)=>`${x(fs[j].year)},${115-v*90}`).join(' ');$('chart').innerHTML=`<line x1="40" y1="115" x2="610" y2="115" class="line"/><polyline fill="none" stroke="#dfb96a" stroke-width="2" points="${pts(a)}"/><polyline fill="none" stroke="#73d6e9" stroke-width="2" points="${pts(b.map(v=>v/max))}"/><line x1="${x(fs[i].year)}" x2="${x(fs[i].year)}" y1="14" y2="120" stroke="#fff" opacity=".6"/><text x="40" y="140" fill="#a7bcad" font-size="11">Year ${fmt(fs[0].year,0)}</text><text x="610" y="140" fill="#a7bcad" text-anchor="end" font-size="11">${fmt(fs.at(-1).year,0)}</text><text x="40" y="14" fill="#dfb96a" font-size="11">100% built</text><text x="610" y="14" fill="#73d6e9" text-anchor="end" font-size="11">${fmt(max)} water / year</text>`}
$('scenario').onchange=initialize;$('treatment').onchange=update;$('step').oninput=update;$('prev').onclick=()=>{$('step').value=Math.max(0,+$('step').value-1);update()};$('next').onclick=()=>{$('step').value=Math.min(run().frames.length-1,+$('step').value+1);update()};$('play').onclick=()=>{if(timer){clearInterval(timer);timer=null;$('play').textContent='Play'}else{$('play').textContent='Pause';timer=setInterval(()=>{if(+$('step').value>=run().frames.length-1){clearInterval(timer);timer=null;$('play').textContent='Play';return}$('step').value=+$('step').value+1;update()},650)}};initialize();
</script></html>'''
html = html.replace('Seed 23 · Village 52', f"Seed {natural['seed']} · Village {focus}")
html = html.replace("'village 52'", f"'village {focus}'")
if args.single:
    html = html.replace('<option value="matched">Matched saved-game comparison</option>', '')
if args.corrected:
    html = html.replace('construction materials omit prepaid commissioning upkeep. The canal advances in tiny increments before activation. This recording preserves that bug; publication is paused.',
                        'commissioning-material demand now includes first-period upkeep. This recording uses the corrected build with organic discovery and normal Elder decisions.')
if args.validation_note:
    html = html.replace('</div>\n<div class="controls">', ' ' + escape(args.validation_note) + '</div>\n<div class="controls">', 1)
(root/'player.html').write_text(html.replace('__DATA__', json.dumps(data,separators=(',',':')).replace('</','<\\/')))
print(root/'player.html')
