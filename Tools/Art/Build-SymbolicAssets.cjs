// Original code-native cartographic symbols. Run with Node and the sharp package.
// Usage: node Tools/Art/Build-SymbolicAssets.cjs <absolute path to sharp>
const fs = require('fs');
const path = require('path');
const sharp = require(process.argv[2] || 'sharp');
const output = path.resolve(__dirname, '../../Assets/_Anthropocene/Resources/MapArt');
const hex = '128,0 256,64 256,192 128,256 0,192 0,64';
const assets = {};
const svg = body => `<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256">${body}</svg>`;
const line = body => `<g fill="none" stroke="#ffffff" stroke-width="10" stroke-linecap="round" stroke-linejoin="round">${body}</g>`;
const fill = body => `<g fill="#ffffff">${body}</g>`;
for (const [name, color] of Object.entries({ desert:'#655138', grassland:'#3b5039', temperate:'#354e43', tropical:'#2a5145', cold:'#515f63', highland:'#595653', ocean:'#1b3047', shallows:'#315d6d', lake:'#397073' })) {
  const water = ['ocean','shallows','lake'].includes(name);
  const pattern = water ? '<path d="M54 105q16-8 32 0t32 0M135 159q16-8 32 0t32 0"/>' : name === 'highland' ? '<path d="m58 132 25-31 27 31m20 36 24-30 29 30"/>' : '<path d="m57 136 13-4m67 58 12-3m38-91 11-3"/>';
  assets[`terrain_${name}_base`] = `<polygon points="${hex}" fill="${color}"/><g stroke="#d0d7c3" stroke-opacity=".11" stroke-width="4" fill="none">${pattern}</g>`;
}
assets.veg_grass = line('<path d="M128 204V105m0 60-43-47m43 61 39-65m-55 85-44-24m78 25 45-23"/>');
assets.veg_shrub = line('<path d="M128 218v-64m0 32-43-23m43 3 39-34"/>') + fill('<circle cx="89" cy="128" r="35"/><circle cx="128" cy="100" r="40"/><circle cx="169" cy="131" r="31"/>');
assets.veg_temperate_tree = fill('<path d="M117 154h22v72h-22z"/><circle cx="91" cy="112" r="45"/><circle cx="157" cy="112" r="46"/><circle cx="125" cy="78" r="44"/>');
assets.veg_tropical_tree = line('<path d="M127 220q12-73 0-119"/>') + fill('<path d="M129 110Q64 111 40 72q58-30 89 38Q105 38 146 24q32 62-17 86Q161 48 219 76q-22 48-90 34Q76 129 49 171q-23-53 80-61Q213 94 215 164q-52-40-86-54z"/>');
assets.veg_conifer = fill('<path d="m128 23 50 72h-24l42 59h-30l37 50H53l38-50H60l43-59H79zM117 203h22v28h-22z"/>');
assets.landuse_cultivation_light = line('<path stroke-width="5" d="m41 174 29-37 47 17-31 38zm93-56 39-29 42 22-39 31zM54 177l29-33m-12 39 28-33m50-32 36-24"/>');
assets.landuse_cultivation_heavy = line('<path stroke-width="6" d="m35 132 94-75 94 75-94 76zm20 0 74 59m-56-73 74 59m-56-74 74 59m-56-73 74 59m-56-73 74 59M67 157l94-75m-65 98 94-75"/>');
assets.landuse_fishery = line('<path d="M62 74v138m-18-71h92m-77-45h87m-8 0 56 47-54 47-54-49zM111 120l54 47m-27-71v94m-29-22 58-48"/>');
const tent = fill('<path fill-rule="evenodd" d="M128 40 227 207H29zm0 52-45 91h89zM125 112l-17 76h38z"/>');
assets.unit_camp = tent;
assets.settlement_camp = tent + line('<path stroke-width="7" d="m177 210 18 15m-3-18-13 22M54 216h39"/>');
assets.settlement_village = fill('<path d="m32 101 49-45 48 45H32m8 4h82v74H40zm91 0 42-47 49 47h-91m7 4h77v70h-77M78 186l53-42 55 42H78m9 5h92v45H87z"/>');
assets.unit_hunters = fill('<path d="m177 19 25 10-4 61-43-29zM150 73l25 13-91 153-21-12z"/>') + line('<path stroke-width="7" d="m130 99 36 21m-46-6 36 21"/>');
const herd = fill('<path d="M54 107q30-25 77-9l28-40 44 14 18 35-27 16-18-15-5 58-20 52h-18l9-60-22 1-7 59H94l-2-61-18-2-14 63H40l10-82-20-13 9-12 15 9z"/>') + line('<path stroke-width="9" d="m176 62-11-34m29 37 16-36"/>');
assets.unit_herd_wild = herd;
assets.unit_herd_domestic = herd + '<circle cx="197" cy="200" r="40" fill="#ffffff"/><path d="M178 200h37m-18-20v40" stroke="#243440" stroke-width="10" stroke-linecap="round"/>';
(async () => {
  fs.mkdirSync(output, { recursive:true });
  for (const [name, body] of Object.entries(assets)) await sharp(Buffer.from(svg(body))).png().toFile(path.join(output, name + '.png'));
  // Contact sheet is rendered from the same vector recipes, not by editing source sprites.
  const entries = Object.entries(assets);
  const sheet = `<svg xmlns="http://www.w3.org/2000/svg" width="1280" height="1500"><rect width="1280" height="1500" fill="#18231f"/>${entries.map(([name,body],i) => `<g transform="translate(${(i%5)*256},${Math.floor(i/5)*300})">${body}<text x="128" y="281" font-family="sans-serif" font-size="12" text-anchor="middle" fill="#e4e9d8">${name}</text></g>`).join('')}</svg>`;
  fs.mkdirSync(path.resolve(__dirname, '../../TestResults'), { recursive:true });
  await sharp(Buffer.from(sheet)).png().toFile(path.resolve(__dirname, '../../TestResults/SymbolicAssets.png'));
  process.stdout.write(`Authored ${entries.length} RGBA assets at 256 x 256.\n`);
})();
