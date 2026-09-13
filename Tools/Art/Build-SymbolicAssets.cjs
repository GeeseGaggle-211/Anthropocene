// Original SVG artwork, guided by the approved Option C reference.
// Usage: node Tools/Art/Build-SymbolicAssets.cjs <absolute path to sharp>
// These recipes are authoritative; exported SVGs are also available to art tools.
const fs = require('fs');
const path = require('path');
const sharp = require(process.argv[2] || 'sharp');
const root = path.resolve(__dirname, '../..');
const output = path.join(root, 'Assets/_Anthropocene/Resources/MapArt');
const vectors = path.join(__dirname, 'Sources');
const assets = {};
const hex = '128,0 256,64 256,192 128,256 0,192 0,64';
const ink = '#393c29';
const svg = body => `<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256">${body}</svg>`;
const outline = body => `<g stroke="${ink}" stroke-width="4.5" stroke-linejoin="round" stroke-linecap="round">${body}</g>`;
const shadow = (x=128,y=221,rx=78,ry=12) => `<ellipse cx="${x}" cy="${y}" rx="${rx}" ry="${ry}" fill="#343b2a" opacity=".16"/>`;
const rock = (x,y,s=1) => `<g transform="translate(${x} ${y}) scale(${s})"><path d="m-23 8 9-28 20-9 24 37-24 7z" fill="#a39265"/><path d="m-23 8 9-28 8 35z" fill="#d8c594"/><path d="m-14-20 20-9L-6 15z" fill="#bdaa79"/></g>`;
const palettes = {
  desert:['#dec580','#ead397','#c6ad6e'], grassland:['#99ad60','#adbd70','#7f9854'],
  temperate:['#77965c','#91aa71','#65824d'], tropical:['#668d56','#80a066','#4f784a'],
  cold:['#b4c6c4','#d0dcd7','#92aeaf'], highland:['#969589','#b2afa0','#777c73'],
  ocean:['#236586','#317c9b','#195875'], shallows:['#4499ac','#60b4bc','#318999'], lake:['#377f9c','#5b9fab','#286b89']
};
for (const [name,[base,light,dark]] of Object.entries(palettes)) {
  const water = ['ocean','shallows','lake'].includes(name);
  let detail = '';
  if (water) detail = `<g fill="none" stroke="${light}" stroke-width="2.2" opacity=".6"><path d="M-8 83q20-10 40 0t40 0t40 0t40 0t40 0t40 0t40 0M-16 157q20-10 40 0t40 0t40 0t40 0t40 0t40 0t40 0M18 203q17-8 34 0t34 0m65-88q17-8 34 0t34 0"/></g>`;
  else if (name === 'highland') detail = `<g opacity=".88"><path d="m25 171 50-74 28 36 50-92 80 153-91 18z" fill="#777c73"/><path d="m25 171 50-74-10 58 24 25zm78-38 50-92-25 117 14 54z" fill="#c4c0ad"/><path d="m153 41 12 83 68 70-60-36-45 0z" fill="#979b90"/><path d="m75 97 8 37 20-1-14 47-24-25z" fill="#9da08f"/><path d="m153 41-25 117-33 24m58-141 12 83 40 45" fill="none" stroke="#dad5bd" stroke-width="2"/></g>`;
  else {
    detail = `<g stroke="${dark}" stroke-width="2" opacity=".3" fill="none"><path d="m35 115 15-3m118 65 18-4m-74 43 11-2m74-117 9-2"/></g>`;
    if (name === 'desert') detail += rock(51,153,.35) + rock(192,104,.3);
    if (name === 'cold') detail += '<path d="m16 175 56-39 40 33 30-40 98 56-62 19-74-13z" fill="#e1e9e1" opacity=".32"/>';
  }
  assets[`terrain_${name}_base`] = `<defs><clipPath id="tile"><polygon points="${hex}"/></clipPath></defs><g clip-path="url(#tile)"><polygon points="${hex}" fill="${base}"/><path d="M0 64 128 0 189 67 88 117 0 99z" fill="${light}" opacity=".25"/><path d="m0 192 105-47 151 47-128 64z" fill="${dark}" opacity=".14"/>${detail}</g>`;
}
assets.veg_grass = shadow(130,207,75,10) + '<g fill="#607b3f"><path d="m52 206-6-46 21 34 4-60 17 64 15-44 2 51zM111 207l-10-63 28 47 3-94 20 90 20-47-5 66zM169 208l11-44 5 29 23-41-8 56z"/></g><g fill="#adc078"><path d="m71 197 0-63 10 51zM129 191l3-94 9 75zM187 191l21-39-16 48z"/></g>';
assets.veg_shrub = shadow(129,218,74,9) + '<g stroke="#827747" stroke-width="5" stroke-linecap="round" fill="none"><path d="M128 218v-57m0 47-31-39m31 43 39-50m-63 20-32-14m81 12 41-12m-66 20-9-38"/></g><g fill="#93935c"><path d="M94 180q-30-3-24-30 27 3 24 30M118 163q-28-6-13-32 22 12 13 32M134 183q-16-28 11-42 12 28-11 42M166 174q-9-30 18-34 6 25-18 34M185 183q4-27 28-15-5 25-28 15M73 183q-28 10-30-18 24-8 30 18"/></g><g fill="#b5ad75"><path d="m70 150 24 30-20-11zM105 131l13 32-13-10zM184 140l-18 34 3-23z"/></g>';
assets.veg_temperate_tree = shadow() + outline('<path d="m117 154-2 68h25l-8-70z" fill="#8e7047"/><path d="M48 132q-17-34 14-52 0-36 37-40 25-34 55-12 36-2 42 35 37 19 17 48 17 40-22 56-35 22-65-2-35 26-65 2-29-3-13-35z" fill="#42734e"/>') + '<path d="M52 125q-7-31 30-31-13-27 17-38 22-27 45-13-29 10-24 42-35-10-38 29z" fill="#789759"/><path d="M111 141q35-19 61-7 23-11 30-36 13 27-4 43-32 29-69 12z" fill="#305d44"/><path d="m121 171 6 47" stroke="#b79c66" stroke-width="5"/>';
assets.veg_conifer = shadow(128,225,67,10) + outline('<path d="m116 175-1 53h27l-7-52z" fill="#8d7548"/><path d="m128 20 47 68-24-2 44 62-32-4 45 66-80-10-80 10 45-66-32 4 44-62-24 2z" fill="#35664c"/>') + '<path d="m128 20-19 63 19-8-23 62 23-11-33 68-47 16 45-66-32 4 44-62-24 2z" fill="#64844f"/><path d="m128 78 47 10-24-2 44 62-54-13 22 9 45 66-80-10 21-20-18-36 14-13-24-48z" fill="#244e3d"/><path d="m122 205 3 18" stroke="#b29a66" stroke-width="5"/>';
assets.veg_tropical_tree = shadow(130,225,64,10) + outline('<path d="M119 226q24-62 9-121l16-4q25 67-5 127z" fill="#a28b52"/><path d="M137 108Q94 42 39 71l16 10-12 15q53-22 94 12Q134 32 84 23l-1 15-15 3q39 17 69 67Q144 40 193 32l-5 18 19 2q-47 9-70 56Q192 57 232 89l-17 10 8 17q-45-28-86-8Q195 103 207 167l-18-10-7 17q-3-43-45-66Q90 100 63 166l-9-17-18 2q22-61 101-43z" fill="#3c7750"/>') + '<path d="M137 108Q80 47 39 71q66-7 98 37M137 108q30-52 70-56-52 4-70 56M137 108q41-1 56 42-4-33-56-42" fill="#78a05a"/><path d="m132 141 16 3m-14 24 14 4m-18 23 14 5" stroke="#695d3a" stroke-width="4"/>';
const tent = shadow(131,222,91,12) + outline('<path d="m40 217 88-170 92 163-81 18z" fill="#b28d51"/><path d="m40 217 88-170 11 181z" fill="#e6ca8b"/><path d="m139 228-11-181 92 163z" fill="#a8844d"/><path d="m77 219 47-106 28 113z" fill="#453c29"/><path d="m77 219 47-106-8 110z" fill="#c8a764"/><path d="m128 47-8-20m8 20 12-19m-79 170-32 22m174-17 27 17" fill="none" stroke="#665333"/>') + '<path d="m144 95 40 99-25-6z" fill="#c39e60"/><path d="m124 60-52 131m71 4 40 5" fill="none" stroke="#f5dfaa" stroke-width="2.5"/><g fill="#89754b"><ellipse cx="46" cy="222" rx="11" ry="6"/><ellipse cx="210" cy="215" rx="10" ry="6"/></g>';
assets.unit_camp = tent; assets.settlement_camp = tent;
assets.unit_hunters = shadow(126,223,50,8) + outline('<path d="m64 222 94-167 12 7-91 169z" fill="#ac8041"/><path d="m145 71 26-51 25-9-1 28-34 42z" fill="#bfc4bd"/>') + '<path d="m171 20 25-9-35 70-4-21z" fill="#e5e7d8"/><path d="m161 81 35-70-1 28z" fill="#7c8f8b"/><path d="m69 220 86-150" stroke="#d9b871" stroke-width="3"/><path d="m141 77 20 10m-25-2 20 11m-25-2 20 11" stroke="#4a422e" stroke-width="5"/>';
// Distinct animal silhouettes remain identifiable without relying on colour.
const legs = (body='#755c38') => outline(`<path d="M62 146 49 215 68 215 86 149zM91 146 99 216 114 216 111 146zM138 147 143 216 158 216 158 139zM158 139 168 214 183 214 175 124z" fill="${body}"/>`);
const antelope = legs('#8d7040') + outline('<path d="M45 119q-15-4-18-18l-3 27 23 12q-1 22 28 26 29 14 63-1l28-5 12-47 30-17 6-30-21-16-25 24-21 34q-54-20-88-1z" fill="#a28750"/><path d="m174 69-20-14 9 21m37-18 27-6-18 19" fill="#ba9a5e"/>') + '<path d="M53 125q49-13 96 1l-9 21q-50 18-87-2z" fill="#bba06a"/><path d="m146 149 32-48-12 59-25 5" fill="#715a36"/><path d="m184 63-8-17-3-29m29 41 3-24-9-20" fill="none" stroke="#3e3d2b" stroke-width="6" stroke-linecap="round"/><circle cx="204" cy="75" r="3.7" fill="#282e25"/><path d="m196 89 17-8" stroke="#594830" stroke-width="3"/>';
assets.unit_herd_wild = shadow(124,218,91,10) + antelope;
assets.unit_herd_goat = shadow(125,220,88,10) + legs('#91846a') + outline('<path d="M47 120q-4-25 39-25l67 11 22-38 32-3 15 31-24 20-19-7-7 55q-51 19-115-2l-13-21-15-26z" fill="#b8ad8b"/><path d="m194 111 7 28 11-39m-30-28-19-12 14 24" fill="#cabc99"/><path d="M185 72q-35-51-5-52-11 22 16 47m2-2q-5-47 21-30-18 0-9 38" fill="#918566"/>') + '<path d="m64 128 75-8 30 22-38 11-55-4z" fill="#d4c5a0"/><circle cx="209" cy="84" r="3.5" fill="#30332a"/>';
assets.unit_herd_bison = shadow(125,222,98,11) + legs('#655139') + outline('<path d="M31 119q-6-39 50-48l36-14q42-4 70 31l30 10 9 39-25 29-39-17q-61 37-119 2z" fill="#795e3e"/><path d="M138 66q42-10 56 29l23 3 9 39-25 29-39-17-21-37z" fill="#4e4432"/><path d="M186 104q-29-3-25-26 8 12 27 12m19 10q28-3 19-22-1 14-21 12" fill="#e0c799"/>') + '<path d="M44 113q49-14 83-40l10 19-6 43-83 7z" fill="#987448"/><path d="m30 119-11 46" stroke="#4e4432" stroke-width="5"/><circle cx="204" cy="116" r="3.4" fill="#cfb57d"/>';
assets.unit_herd_camel = shadow(129,224,96,10) + legs('#967346') + outline('<path d="M40 126q-5-30 20-35 9-70 45-27 8 26 23 27 31-56 52-4l6 22 7-57q-5-26 20-30l21 12-8 19-19 1q11 56-20 99-65 23-138-5z" fill="#be945b"/>') + '<path d="M49 122q14-11 20-38 10-33 24-8 8 32 34 30 21-35 40-12l10 39-55 15-64-12z" fill="#d6b074"/><path d="m40 119-15 44m178-132 10-16" stroke="#795e3c" stroke-width="4"/><circle cx="222" cy="36" r="3.4" fill="#373a29"/>';
assets.unit_herd_horse = shadow(127,222,93,10) + legs('#856143') + outline('<path d="M40 119q19-27 48-19l58 4 16-41 21-23 18 16 19 37-13 24-30-19-4 54q-73 34-127 8z" fill="#9b734a"/><path d="m40 120-18 64-9-18 18-50m132-47 20-29 18 16-17 1-26 51-12-4z" fill="#4f4531"/><path d="m181 48-3-25 15 26" fill="#9b734a"/>') + '<path d="M51 122q50-14 99 0l-13 29-74-4z" fill="#b98c58"/><path d="m198 70 11 29" stroke="#d5b685" stroke-width="7"/><circle cx="203" cy="78" r="3.5" fill="#35362a"/>';
assets.unit_herd_domestic = shadow(128,220,96,11) + legs('#786345') + outline('<path d="M36 112q22-20 58-15l65 10 11-26 32-10 22 29-5 41-32 4-16-21-5 38q-78 22-126-3z" fill="#957c50"/><path d="M183 83q-25-3-21-27 5 14 26 17m15 7q33-2 22-24-1 14-22 14" fill="#e3d0a2"/>') + '<path d="m48 121 79-7 37 14-24 22-84-7z" fill="#bca06a"/><path d="m177 116 41 1-4 19-29 3z" fill="#bba078"/><path d="m36 121-13 47" stroke="#63543a" stroke-width="5"/><circle cx="206" cy="103" r="3.5" fill="#35372a"/>' + outline('<path d="m194 158 30 0 0 55-30 0z" fill="#3c7681"/><path d="m201 184 7 7 10-15" fill="none" stroke="#f1e5c5" stroke-width="5"/>');
const field = (x,y,s=1) => `<g transform="translate(${x} ${y}) scale(${s})"><path d="m0 30 58-30 56 30-58 30z" fill="#c5b36f" opacity=".65"/><g stroke="#6a7f3e" stroke-width="6" stroke-linecap="round"><path d="m15 31 41-21m-24 30 41-21m-24 30 41-21"/></g><path d="m0 30 58 30 56-30" fill="none" stroke="#e2cd8b" stroke-width="5"/></g>`;
assets.landuse_cultivation_light = field(24,134,.95) + field(131,79,.85);
assets.landuse_cultivation_heavy = field(17,89,1) + field(126,87,1) + field(71,148,1) + '<path d="m28 163 181-90M37 92l175 106" stroke="#dbbc75" stroke-width="9"/>';
assets.landuse_fishery = shadow(137,226,76,9) + outline('<path d="m51 144 20-10 115 61-21 12z" fill="#c79c5a"/><path d="M56 145v75l10 6v-76m100 55v30l10-3v-30M88 99v98m116-80v115" fill="#8c653e"/><path d="m80 102 10-14 123 53-8 14z" fill="#d0aa69"/>') + '<path d="m99 114 95 39-8 49-69-17z" fill="#ccb97f" fill-opacity=".19" stroke="#e0d0a0" stroke-width="4"/><path d="m100 134 91 35m-85-17 82 33m-68-61 9 64m13-56 4 60m16-49-3 52m19-45-10 49" stroke="#d7c79a" stroke-width="2.5"/><path d="m74 157 17-10m0 21 17-10m0 21 17-10m0 21 17-10" stroke="#775432" stroke-width="3"/>';
const hut = (x,y,s=1) => `<g transform="translate(${x} ${y}) scale(${s})">${outline('<path d="m15 59 48-25 48 25v61l-49 23-47-24z" fill="#c1a163"/><path d="m62 82 49-23v61l-49 23z" fill="#957747"/><path d="m0 61 62-60 65 59-65 25z" fill="#d8b773"/><path d="m62 1 65 59-65 25z" fill="#ac8a52"/><path d="m36 100 18-8v41l-18-9z" fill="#4c4330"/>')}</g>`;
assets.settlement_village = shadow(133,230,99,12) + hut(41,10,.85) + hut(126,68,.79) + hut(15,109,.86) + hut(109,142,.69);
assets.feature_freshwater = outline('<path d="M128 26C106 72 60 118 60 162a68 68 0 0 0 136 0c0-44-46-90-68-136z" fill="#398fa8"/>') + '<path d="M128 52c-30 58-53 81-51 115 6 29 16 34 35 40-32-54-9-102 16-155z" fill="#9ad4d5"/><path d="M149 187q21-8 27-32" fill="none" stroke="#24667f" stroke-width="9" stroke-linecap="round"/>';
// The oasis marker must not add a tree that could be mistaken for vegetation state.
assets.feature_oasis = '<ellipse cx="128" cy="144" rx="103" ry="49" fill="#cbbd78"/><ellipse cx="128" cy="144" rx="85" ry="37" fill="#58abb3"/><ellipse cx="132" cy="146" rx="66" ry="28" fill="#347f98"/>';
(async () => {
  fs.mkdirSync(output, { recursive:true }); fs.mkdirSync(vectors, { recursive:true });
  for (const [name,body] of Object.entries(assets)) {
    const source = svg(body);
    fs.writeFileSync(path.join(vectors,name+'.svg'), source+'\n');
    await sharp(Buffer.from(source)).png().toFile(path.join(output,name+'.png'));
  }
  // Check exported alpha and preview shipped PNGs, never a different mockup.
  const overlays = Object.keys(assets).filter(n => !n.startsWith('terrain_'));
  for (const name of overlays) {
    const {data,info} = await sharp(path.join(output,name+'.png')).raw().toBuffer({resolveWithObject:true});
    let clear = 0;
    for(let i=3;i<data.length;i+=info.channels) if(data[i] === 0) clear++;
    if(info.channels !== 4 || clear/(info.width*info.height) < .35 || data[3] !== 0) throw Error(name+' lost transparent negative space');
  }
  const entries = Object.keys(assets); const columns=6, cell=210, rows=Math.ceil(entries.length/columns);
  const tiles = await Promise.all(entries.map(async(name,i) => ({
    input:await sharp(Buffer.from(`<svg xmlns="http://www.w3.org/2000/svg" width="210" height="222"><rect width="210" height="222" fill="${i%2?'#dedcca':'#ebe6d5'}"/><image x="9" y="2" width="192" height="192" href="data:image/png;base64,${fs.readFileSync(path.join(output,name+'.png')).toString('base64')}"/><text x="105" y="209" font-family="sans-serif" font-size="11" text-anchor="middle" fill="#343c30">${name.replace('terrain_','').replace('landuse_','').replace('unit_','')}</text></svg>`)).png().toBuffer(),
    left:(i%columns)*cell,top:Math.floor(i/columns)*222
  })));
  fs.mkdirSync(path.join(root,'TestResults'), { recursive:true });
  await sharp({create:{width:columns*cell,height:rows*222,channels:4,background:'#eee8d7'}}).composite(tiles).png().toFile(path.join(root,'TestResults/CartographicAssets.png'));
  process.stdout.write(`Exported ${entries.length} SVG / RGBA pairs; ${overlays.length} transparent overlays passed alpha checks.\n`);
})();
