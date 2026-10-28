import {readFile,writeFile} from 'node:fs/promises';
import vm from 'node:vm';
import {glyphs} from './orbital-pixel-glyphs.mjs';
const root=new URL('../',import.meta.url);
const source=await readFile(new URL('design/beta20/html/pixel-icons.js',root),'utf8');
const maps=JSON.parse(vm.runInNewContext(source+';JSON.stringify(maps)'));
Object.assign(maps,{
 x:['1000001','0100010','0010100','0001000','0010100','0100010','1000001'],
 minus:['0000000','0000000','0000000','1111111','0000000','0000000','0000000'],
 square:['1111111','1000001','1000001','1000001','1000001','1000001','1111111'],
 chevron:['0000000','0000000','1000001','0100010','0010100','0001000','0000000'],
 dots:['00000000','00000000','00000000','11011011','11011011','00000000','00000000'],
 search:['011100000','100010000','100010000','100010000','011110000','000001000','000000100','000000010','000000001'],
 trash:['0011100','1111111','0100010','0101010','0101010','0100010','0111110'],
 check:['0000001','0000010','0000100','1001000','0110000','0000000','0000000'],
 refresh:['00111100','01000010','10000111','10000000','00000001','11100001','01000010','00111100'],
 external:['0001111','0000011','0000101','1001000','1010000','1000000','1111000'],
 shield:['1111111','1000001','1000001','1010101','1001001','0100010','0011100'],
 list:['1011111','0000000','1011111','0000000','1011111','0000000','1011111'],
 info:['0001000','0000000','0011000','0001000','0001000','0001000','0011100'],
 wifi:['001111100','010000010','100111001','001000100','000010000','000000000','000010000'],
 puzzle:['00011000','01111110','01011010','11000011','11000011','01011010','01111110','00011000'],
 music:['0001111','0001001','0001001','0001001','0111011','1111111','0110011'],
 palette:['00111100','01000010','10101001','10000001','10101001','10000110','01001000','00111000']
});
const alias={settings:'gear',history:'clock','player-play':'play','player-pause':'pause','file-zip':'zip','file-type-mp4':'video','file-type-pdf':'file','file-text':'file','chevron-down':'chevron','external-link':'external','circle-check':'check','circle-x':'x','alert-triangle':'info','info-circle':'info','shield-check':'shield','layout-sidebar-right':'list',copy:'clipboard','brand-apple':'file'};
export function pixelIcon(name,extra=''){
 const modern=glyphs[name==='file-type-mp4'?'video':name];
 if(modern)return `<svg class="icon icon-tabler-${name} icon-pixel" data-icon-family="pixel" aria-hidden="true" focusable="false" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" shape-rendering="crispEdges" ${extra}>${modern}</svg>`;
 const rows=maps[alias[name]||name]||maps.file,w=Math.max(...rows.map(r=>r.length));
 const rects=rows.flatMap((row,y)=>[...row].flatMap((c,x)=>c==='0'?[]:[`<rect x="${x}" y="${y}" width="1" height="1" fill="${c==='2'?'#bd73ed':'currentColor'}"/>`])).join('');
 return `<svg class="icon icon-tabler-${name} icon-pixel" data-icon-family="pixel" aria-hidden="true" focusable="false" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${w} ${rows.length}" shape-rendering="crispEdges" ${extra}>${rects}</svg>`;
}
export const pixelIconNames=Object.freeze(Object.keys(glyphs));
export async function syncPixelCompanionIcons(){
 const file=new URL('extensions/syphon/popup.html',root);let html=await readFile(file,'utf8'),index=0;
 const choices=['download','refresh','download','shield-check','video','info-circle','link'];
 html=html.replace(/<svg\b[\s\S]*?<\/svg>/g,old=>pixelIcon(choices[index++]).replace('class="icon ',old.includes('feature-icon')?'class="icon feature-icon ':'class="icon '));
 if(index!==choices.length)throw Error('Unexpected popup icons');await writeFile(file,html);
 const shared=Object.fromEntries(['download','refresh','video','music','file-text','x','circle-check','alert-triangle'].map(n=>[n,pixelIcon(n)]));
 await writeFile(new URL('extensions/syphon/orbital-icons.js',root),'// Generated from the shared Orbital pixel family.\nexport const orbitalIcons='+JSON.stringify(shared)+';\n');
 const videoPath=new URL('extensions/syphon/video-button.js',root);let video=await readFile(videoPath,'utf8');
 video=video.replace(/\/\* OV_VISUAL_START \*\/[\s\S]*?\/\* OV_VISUAL_END \*\//,'/* OV_VISUAL_START */const identityCss='+JSON.stringify(await readFile(new URL('extensions/syphon/orbital-identity.css',root),'utf8'))+';const orbitalIcons='+JSON.stringify(shared)+';/* OV_VISUAL_END */');
 await writeFile(videoPath,video);
}
