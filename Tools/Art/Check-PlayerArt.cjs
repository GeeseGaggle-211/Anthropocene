// Read-only pixel checks on actual Windows player captures.
// Usage: node Tools/Art/Check-PlayerArt.cjs <sharp directory> <smoke output directory>
const fs = require('fs');
const path = require('path');
const sharp = require(process.argv[2] || 'sharp');
const directory = path.resolve(process.argv[3] || 'TestResults/CartographicPlayer');
(async () => {
  for(const name of ['00-transparency','00-art-classic','00-art-contrast','01-opening','02-dispatch-stack','03-high-contrast','03b-distant-view','04-supply-warning','05-restored']) {
    if(!fs.existsSync(path.join(directory,name+'.png'))) throw Error('Missing player capture: '+name);
  }
  if(!fs.readFileSync(path.join(directory,'result.txt'),'utf8').startsWith('PASS:')) throw Error('Player smoke run did not complete');
  const {data,info} = await sharp(path.join(directory,'00-transparency.png')).ensureAlpha().raw().toBuffer({resolveWithObject:true});
  const pixel = (x,y) => Array.from(data.subarray((y*info.width+x)*4,(y*info.width+x)*4+3));
  // Same tile drawn at x=100 and x=400. Probe within the old opaque slot but
  // outside the tent silhouette and selection corners. PNG coordinates run down.
  let probes = 0;
  for(const x of [137,215]) for(const y of [200,206,212]) {
    const bare=pixel(x,y), selected=pixel(x+300,y);
    if(bare.reduce((sum,value,i)=>sum+Math.abs(value-selected[i]),0)>6)
      throw Error(`Opaque backing detected at (${x+300},${y}): ${bare} -> ${selected}`);
    if(bare.every(v=>v<25)) throw Error('Capture has no rendered terrain');
    probes++;
  }
  // Confirm the selected icon actually rendered: identical blank tiles cannot pass.
  let changed=0;
  for(let y=180;y<240;y++) for(let x=145;x<207;x++)
    if(pixel(x,y).some((v,c)=>Math.abs(v-pixel(x+300,y)[c])>12)) changed++;
  if(changed<150) throw Error('Selected camp was not rendered in the alpha comparison');
  process.stdout.write(`PASS: ${probes} transparent map-space probes unchanged; ${changed} icon pixels differ; all nine player captures and gameplay/save smoke result present.\n`);
})();
