import {readFile,writeFile} from 'node:fs/promises';
const root=new URL('../',import.meta.url);
export const mapping={download:'download-simple',history:'clock-counter-clockwise',puzzle:'puzzle-piece',settings:'gear',link:'link',plus:'plus','file-zip':'file-zip','player-pause':'pause',x:'x','file-type-pdf':'file-pdf','player-play':'play',file:'file',search:'magnifying-glass','chevron-down':'caret-down','brand-apple':'apple-logo',wifi:'wifi-high',list:'list-numbers',music:'music-notes',palette:'palette','info-circle':'info',folder:'folder',clipboard:'clipboard',minus:'minus','external-link':'arrow-square-out',clock:'clock',trash:'trash',refresh:'arrows-clockwise',square:'square',copy:'copy',dots:'dots-three','circle-check':'check-circle','circle-x':'x-circle','alert-triangle':'warning',video:'video-camera','file-type-mp4':'file-video','file-text':'file-text','layout-sidebar-right':'sidebar-simple','shield-check':'shield-check',bars:'chart-bar',gauge:'gauge',exit:'sign-out',pin:'push-pin','pin-off':'push-pin-slash',restore:'browsers','chevron-up':'caret-up','chevron-right':'caret-right',check:'check'};
export async function phosphor(name,extra=''){
 const actual=mapping[name]||name;
 let svg=await readFile(new URL(`ui/charts/node_modules/@phosphor-icons/core/assets/regular/${actual}.svg`,root),'utf8');
 svg=svg.replace(/<svg\b/,'<svg class="icon icon-tabler-'+name+' icon-phosphor" data-icon-family="phosphor" aria-hidden="true" focusable="false" '+extra).replace(/fill="(?:#0+|black)"/g,'fill="currentColor"');
 return svg;
}
export async function syncCompanionIcons(){
 const popup=new URL('extensions/syphon/popup.html',root);let html=await readFile(popup,'utf8');
 const choices=['download','refresh','download','shield-check','video','info-circle','link'];let i=0;
 const matches=[...html.matchAll(/<svg\b[\s\S]*?<\/svg>/g)];
 if(matches.length!==choices.length)throw Error('Unexpected popup icon inventory: '+matches.length);
 for(const match of matches){const feature=match[0].includes('feature-icon')?' class="icon feature-icon"':'';let svg=await phosphor(choices[i++]);if(feature)svg=svg.replace('class="icon ', 'class="icon feature-icon ');html=html.replace(match[0],svg);}
 await writeFile(popup,html);
 const shared=Object.fromEntries(await Promise.all(['download','refresh','video','music','file-text','x','circle-check','alert-triangle'].map(async n=>[n,await phosphor(n)])));
 await writeFile(new URL('extensions/syphon/orbital-icons.js',root),'// Generated from the shared Phosphor family.\nexport const orbitalIcons='+JSON.stringify(shared)+';\n');
 const overlayCss=await readFile(new URL('extensions/syphon/video-overlay.css',root),'utf8');
 const videoPath=new URL('extensions/syphon/video-button.js',root);let video=await readFile(videoPath,'utf8');
 video=video.replace(/^  const css = .*?;$/m,()=> '  const css = '+JSON.stringify(overlayCss)+';');
 video=video.replace(/\/\* OV_VISUAL_START \*\/[\s\S]*?\/\* OV_VISUAL_END \*\//,'/* OV_VISUAL_START */const identityCss='+JSON.stringify(await readFile(new URL('extensions/syphon/orbital-identity.css',root),'utf8'))+';const orbitalIcons='+JSON.stringify(shared)+';/* OV_VISUAL_END */');
 await writeFile(videoPath,video);
 await writeFile(new URL('src/Syphon.Desktop/DesktopAssets/Phosphor-Icons-LICENSE.txt',root),await readFile(new URL('ui/charts/node_modules/@phosphor-icons/core/LICENSE',root)));
 await writeFile(new URL('extensions/syphon/Phosphor-Icons-LICENSE.txt',root),await readFile(new URL('ui/charts/node_modules/@phosphor-icons/core/LICENSE',root)));
}
