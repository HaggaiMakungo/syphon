/* Reproducible, grid-native Orbital S authoring. Existing branding is untouched. */
const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto');
const {Resvg}=require('../../../.tools/brand-icons/node_modules/@resvg/resvg-js');
const root=__dirname;
const palette={globe:'#08090b',deep:'#7437a3',violet:'#a34dff',lilac:'#d694ff',highlight:'#efbdff',white:'#f2edf6',muted:'#77717f'};
function bezier(a,b,c,d,t){const s=1-t;return {x:s*s*s*a[0]+3*s*s*t*b[0]+3*s*t*t*c[0]+t*t*t*d[0],y:s*s*s*a[1]+3*s*s*t*b[1]+3*s*t*t*c[1]+t*t*t*d[1]};}
function geometry(n){
 const samples=[];
 const curves=[[[18,3],[12,2],[2.5,7.5],[5.5,11]],[[5.5,11],[9,15],[28.5,13.5],[28,21]],[[28,21],[27.5,25.5],[21,29],[16.5,29]]];
 curves.forEach((curve,i)=>{for(let k=0;k<=150;k++){const t=k/150,p=bezier(...curve,t);const u=(i+t)/3;const thickness=.4+2.25*Math.pow(Math.sin(Math.PI*u),.65);samples.push({...p,r:thickness});}});
 const cells=[];
 for(let y=0;y<n;y++)for(let x=0;x<n;x++){
  const px=(x+.5)*32/n,py=(y+.5)*32/n;
  const radial=Math.sqrt(((px-16)/12.9)**2+((py-16)/13.1)**2);
  let nearest=Infinity,ink=false,centerDistance=Infinity,centerY=py;
  for(const p of samples){const d=Math.hypot(px-p.x,py-p.y);if(d-p.r<nearest)nearest=d-p.r;if(d<centerDistance){centerDistance=d;centerY=p.y;}if(d<=p.r)ink=true;}
  const rim=radial<=1.035&&radial>=.955;
  const globe=radial<1.035;
  if(globe||ink){
   let color=null;
   if(ink){const ridge=py-centerY;color=ridge< -1.25?'highlight':ridge< -.4?'lilac':ridge<1.1?'violet':'deep';if(px<5&&py>7)color='deep';}
   else if(rim)color=(px+py<30)?'lilac':'violet';
   cells.push({x,y,color,globe});
  }
 }
 return {grid:n,cells};
}
function runs(g,colorFilter){const rows=[];for(let y=0;y<g.grid;y++){let start=-1,old=null;for(let x=0;x<=g.grid;x++){const cell=g.cells.find(c=>c.x===x&&c.y===y),color=x<g.grid&&cell?colorFilter(cell):null;if(color!==old){if(old!==null)rows.push({x:start,y,w:x-start,color:old});start=x;old=color;}}}return rows;}
function rects(g,filter){return runs(g,filter).map(r=>`<rect x="${r.x}" y="${r.y}" width="${r.w}" height="1" fill="${r.color}"/>`).join('');}
function svg(g,{mode='color',tile=false,progress=null}={}){
 const n=g.grid,pad=tile?Math.round(n*.125):0,total=n+pad*2;
 const base=mode==='white'||mode==='muted'?'':rects(g,c=>c.globe?palette.globe:null);
 const mark=rects(g,c=>c.color?(mode==='white'?palette.white:mode==='muted'?palette.muted:palette[c.color]):null);
 const art=progress===null?base+mark:base+rects(g,c=>c.color?'#423b4a':null)+`<defs><clipPath id="progress"><rect x="0" y="${n-Math.round(n*progress/100)}" width="${n}" height="${Math.round(n*progress/100)}"/></clipPath></defs><g clip-path="url(#progress)">${mark}</g>`;
 return `<svg xmlns="http://www.w3.org/2000/svg" width="${total}" height="${total}" viewBox="0 0 ${total} ${total}" shape-rendering="crispEdges" role="img" aria-label="Syphon Orbital S">${tile?`<rect x=".5" y=".5" width="${total-1}" height="${total-1}" rx="${Math.round(total*.22)}" fill="#17171b" stroke="#38343e" stroke-width="1" shape-rendering="geometricPrecision"/>`:''}<g transform="translate(${pad} ${pad})">${art}</g></svg>`;
}
for(const dir of ['svg','png/transparent','png/tile','png/white','png/muted','png/states','windows','browser-extension'])fs.mkdirSync(path.join(root,dir),{recursive:true});
const grids={};for(const n of [16,20,24,32]){grids[n]=geometry(n);fs.writeFileSync(path.join(root,'svg',`orbital-s-grid-${n}.svg`),svg(grids[n]));}
for(const [name,opts]of Object.entries({'orbital-s':{},'orbital-s-tile':{tile:true},'orbital-s-white':{mode:'white'},'orbital-s-muted':{mode:'muted'}}))fs.writeFileSync(path.join(root,'svg',name+'.svg'),svg(grids[32],opts));
const manifest={name:'Syphon Orbital S',version:2,palette,sizes:[16,20,24,32,40,48,64,96,128,256,512,1024],files:[]};
function exportPng(relative,source,size){const bytes=new Resvg(source,{fitTo:{mode:'width',value:size}}).render().asPng();fs.writeFileSync(path.join(root,relative),bytes);manifest.files.push({path:relative,size,bytes:bytes.length,sha256:crypto.createHash('sha256').update(bytes).digest('hex')});return bytes;}
for(const size of manifest.sizes){const grid=grids[size<=24?size:32]||grids[32];for(const [folder,opts]of [['transparent',{}],['tile',{tile:true}],['white',{mode:'white'}],['muted',{mode:'muted'}]])exportPng(`png/${folder}/orbital-s-${size}.png`,svg(grid,opts),size);}
for(const p of [0,25,50,75,100]){const source=svg(grids[32],{progress:p});fs.writeFileSync(path.join(root,'svg',`orbital-s-progress-${p}.svg`),source);exportPng(`png/states/orbital-s-progress-${p}.png`,source,256);}
function ico(folder){const frames=[16,20,24,32,48,64,128,256].map(size=>({size,bytes:fs.readFileSync(path.join(root,`png/${folder}/orbital-s-${size}.png`))}));const header=Buffer.alloc(6+16*frames.length);header.writeUInt16LE(1,2);header.writeUInt16LE(frames.length,4);let offset=header.length;frames.forEach((f,i)=>{const p=6+i*16;header[p]=f.size===256?0:f.size;header[p+1]=header[p];header.writeUInt16LE(1,p+4);header.writeUInt16LE(32,p+6);header.writeUInt32LE(f.bytes.length,p+8);header.writeUInt32LE(offset,p+12);offset+=f.bytes.length;});return Buffer.concat([header,...frames.map(f=>f.bytes)]);}
fs.writeFileSync(path.join(root,'windows','syphon-orbital-s.ico'),ico('transparent'));
fs.writeFileSync(path.join(root,'windows','syphon-orbital-s-tile.ico'),ico('tile'));
for(const size of [16,32,48,128])fs.copyFileSync(path.join(root,`png/transparent/orbital-s-${size}.png`),path.join(root,`browser-extension/icon${size}.png`));
fs.writeFileSync(path.join(root,'geometry.json'),JSON.stringify(grids,null,2));
const geometryData=Object.fromEntries(Object.entries(grids).map(([n,g])=>[n,{grid:g.grid,globe:rects(g,c=>c.globe?palette.globe:null),mark:rects(g,c=>c.color?palette[c.color]:null),inactive:rects(g,c=>c.color?'#514758':null),mask:rects(g,c=>c.color?'white':null)}]));
const animationSource=fs.readFileSync(path.join(root,'animation-source.js'),'utf8');
fs.writeFileSync(path.join(root,'orbital-icon.js'),'/* Generated by build-assets.cjs. Edit animation-source.js instead. */\nconst ORBITAL_SHAPES='+JSON.stringify(geometryData)+';\n'+animationSource);
fs.writeFileSync(path.join(root,'manifest.json'),JSON.stringify(manifest,null,2));
// The local preview server serves html/ as its root; keep a portable asset copy there.
const previewRoot=path.join(root,'../html/orbital-s');fs.mkdirSync(previewRoot,{recursive:true});
for(const dir of ['svg','png','windows','browser-extension'])fs.cpSync(path.join(root,dir),path.join(previewRoot,dir),{recursive:true});
for(const file of ['orbital-icon.js','manifest.json','README.md'])if(fs.existsSync(path.join(root,file)))fs.copyFileSync(path.join(root,file),path.join(previewRoot,file));
console.log(JSON.stringify({pngs:manifest.files.length,gridMasters:4,icoFiles:2,extensionIcons:4,root}));
