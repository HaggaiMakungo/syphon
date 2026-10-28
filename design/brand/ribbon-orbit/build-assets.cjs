/* Editable vector master and reproducible small-icon/animation exports. */
const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto');
const {Resvg}=require('../../../.tools/brand-icons/node_modules/@resvg/resvg-js');
const root=__dirname;
const palette={ink:'#100b16',deep:'#6a3b94',violet:'#b887f4',lilac:'#d5a8ff',highlight:'#efdfff',muted:'#887994'};
const top='M245 344C201 233 339 126 507 130C671 133 809 235 790 360C782 410 752 450 715 478L682 446C719 415 740 379 741 339C744 252 642 183 513 180C388 175 293 246 305 312Z';
const bottom='M300 494C221 538 206 610 247 674C326 798 596 806 721 709C750 686 767 656 766 626L821 652C814 713 776 756 731 785C577 884 298 839 217 700C166 613 196 534 266 481Z';
const ribbon='M532 131C349 112 220 213 228 321C235 435 345 480 489 525L577 558C689 590 763 625 755 682C748 731 675 769 587 785C733 783 823 719 823 639C823 520 720 475 579 430L474 395C354 355 285 329 292 283C298 231 392 188 485 182Z';
const transform='translate(512 512) scale(1.16 .96) translate(-512 -512)';
function paths(fill,stroke='',optical=0){return [top,bottom,ribbon].map((d,i)=>`<path d="${d}" fill="${Array.isArray(fill)?fill[i]:fill}" ${stroke?`stroke="${stroke}" stroke-width="${optical}" stroke-linejoin="round"`:''}/>`).join('');}
function artwork(size=128,mode='color'){
 const small=size<=24,optical=size<=16?42:size<=20?28:size<=24?16:0;
 if(mode==='white'||mode==='muted')return `<g transform="${transform}">${paths(mode==='white'?palette.highlight:palette.muted,mode==='white'?palette.highlight:palette.muted,optical)}</g>`;
 return `<g transform="${transform}">${paths(['url(#front)','url(#back-bottom)','url(#front)'],small?'url(#front)':'',optical)}${small?'':`<path d="M532 131C349 112 220 213 228 321C235 435 345 480 489 525L577 558C689 590 763 625 755 682" fill="none" stroke="url(#edge)" stroke-width="3"/>`}</g>`;
}
function svg(size=128,{mode='color',tile=false,progress=null,shine=null}={}){
 const small=size<=24;
 const definitions=`<defs><linearGradient id="back-top" x1="240" y1="170" x2="792" y2="440" gradientUnits="userSpaceOnUse"><stop stop-color="${palette.lilac}"/><stop offset=".48" stop-color="${palette.violet}"/><stop offset="1" stop-color="${palette.deep}"/></linearGradient><linearGradient id="back-bottom" x1="235" y1="530" x2="720" y2="830" gradientUnits="userSpaceOnUse"><stop stop-color="${palette.deep}"/><stop offset=".5" stop-color="${palette.lilac}"/><stop offset="1" stop-color="${palette.violet}"/></linearGradient><linearGradient id="front" x1="350" y1="190" x2="675" y2="740" gradientUnits="userSpaceOnUse"><stop stop-color="${small?palette.lilac:palette.highlight}"/><stop offset=".35" stop-color="${palette.lilac}"/><stop offset=".64" stop-color="${palette.violet}"/><stop offset="1" stop-color="${palette.deep}"/></linearGradient><linearGradient id="edge" x1="230" y1="230" x2="750" y2="650" gradientUnits="userSpaceOnUse"><stop stop-color="#f6e8ff" stop-opacity=".65"/><stop offset=".6" stop-color="#d2a9ef" stop-opacity=".1"/><stop offset="1" stop-color="#f1dcff" stop-opacity=".4"/></linearGradient><clipPath id="fill"><rect x="0" y="${1024*(1-(progress??100)/100)}" width="1024" height="${1024*(progress??100)/100}"/></clipPath><clipPath id="silhouette"><g transform="${transform}">${paths('#fff')}</g></clipPath><linearGradient id="shimmer"><stop stop-color="#fff" stop-opacity="0"/><stop offset=".5" stop-color="#f5e7ff" stop-opacity=".65"/><stop offset="1" stop-color="#fff" stop-opacity="0"/></linearGradient></defs>`;
 let mark=progress==null?artwork(size,mode):artwork(size,'muted')+`<g clip-path="url(#fill)">${artwork(size,mode)}</g>`;
 if(shine!=null)mark+=`<g clip-path="url(#silhouette)"><path d="M${shine-170} -100H${shine-35}L${shine+440} 1124H${shine+305}Z" fill="url(#shimmer)"/></g>`;
 if(tile)mark=`<rect x="8" y="8" width="1008" height="1008" rx="220" fill="#17111e" stroke="#594164" stroke-width="8"/><g transform="translate(100 100) scale(.805)">${mark}</g>`;
 return `<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}" viewBox="0 0 1024 1024" shape-rendering="geometricPrecision" role="img" aria-label="Syphon Ribbon Orbit">${definitions}${mark}</svg>`;
}
module.exports={svg,artwork,paths,transform,palette};
if(require.main===module){
 for(const dir of ['svg','png/transparent','png/tile','png/white','png/muted','png/states','windows','browser-extension'])fs.mkdirSync(path.join(root,dir),{recursive:true});
 const manifest={name:'Syphon Ribbon Orbit',version:1,palette,sizes:[16,20,24,32,40,48,64,96,128,256,512,1024],files:[]};
 function png(source,size){return new Resvg(source,{fitTo:{mode:'width',value:size}}).render().asPng();}
 function writePng(relative,source,size){const bytes=png(source,size);fs.writeFileSync(path.join(root,relative),bytes);manifest.files.push({path:relative,size,bytes:bytes.length,sha256:crypto.createHash('sha256').update(bytes).digest('hex')});return bytes;}
 for(const size of manifest.sizes){for(const [folder,options]of [['transparent',{}],['tile',{tile:true}],['white',{mode:'white'}],['muted',{mode:'muted'}]])writePng(`png/${folder}/ribbon-orbit-${size}.png`,svg(size,options),size);}
 for(const size of [16,20,24,32,128])fs.writeFileSync(path.join(root,'svg',`ribbon-orbit-${size}.svg`),svg(size));
 for(const [name,options]of [['ribbon-orbit',{}],['ribbon-orbit-white',{mode:'white'}],['ribbon-orbit-muted',{mode:'muted'}],['ribbon-orbit-tile',{tile:true}]])fs.writeFileSync(path.join(root,'svg',name+'.svg'),svg(1024,options));
 for(const progress of [0,25,50,75,100])writePng(`png/states/ribbon-orbit-progress-${progress}.png`,svg(256,{progress}),256);
 const frames={idle:png(svg(32),32).toString('base64'),paused:png(svg(32,{mode:'muted'}),32).toString('base64'),charging:Array.from({length:20},(_,i)=>png(svg(32,{progress:Math.min(100,i/17*100)}),32).toString('base64')),shimmer:Array.from({length:17},(_,i)=>png(svg(32,{shine:i*95-400}),32).toString('base64'))};
 fs.writeFileSync(path.join(root,'activity-frames.json'),JSON.stringify({name:manifest.name,cycleMilliseconds:1000,frameMilliseconds:50,frames}));
 function ico(tile=false){const sizes=[16,20,24,32,48,64,128,256],parts=sizes.map(size=>fs.readFileSync(path.join(root,`png/${tile?'tile':'transparent'}/ribbon-orbit-${size}.png`))),header=Buffer.alloc(6+16*sizes.length);header.writeUInt16LE(1,2);header.writeUInt16LE(sizes.length,4);let at=header.length;sizes.forEach((size,i)=>{const p=6+i*16;header[p]=header[p+1]=size===256?0:size;header.writeUInt16LE(1,p+4);header.writeUInt16LE(32,p+6);header.writeUInt32LE(parts[i].length,p+8);header.writeUInt32LE(at,p+12);at+=parts[i].length;});return Buffer.concat([header,...parts]);}
 fs.writeFileSync(path.join(root,'windows/syphon-ribbon-orbit.ico'),ico());fs.writeFileSync(path.join(root,'windows/syphon-ribbon-orbit-tile.ico'),ico(true));
 for(const size of [16,32,48,128])fs.copyFileSync(path.join(root,`png/transparent/ribbon-orbit-${size}.png`),path.join(root,`browser-extension/icon${size}.png`));
 fs.writeFileSync(path.join(root,'manifest.json'),JSON.stringify(manifest,null,2));
 console.log(JSON.stringify({name:manifest.name,pngs:manifest.files.length,root}));
}
