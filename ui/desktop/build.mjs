import {mapping,phosphor,syncCompanionIcons} from '../../scripts/phosphor-icons.mjs';
import { build } from '../charts/node_modules/esbuild/lib/main.js';
import { mkdir,copyFile,readFile,writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import '../../scripts/sync-brand-assets.mjs';
const out=new URL('../../src/Syphon.Desktop/DesktopAssets/',import.meta.url);
await mkdir(out,{recursive:true});
const templates=JSON.parse(await readFile(new URL('templates.json',import.meta.url),'utf8'));
const names=[...new Set(Object.values(templates).flatMap(v=>[...v.html.matchAll(/class="ti ti-([^" ]+)/g)].map(m=>m[1])))];
for(const n of ['player-play','player-pause','external-link','folder','trash','refresh','file','file-type-mp4','music','search','x','download','history','settings','link','plus','minus','puzzle','list','wifi','palette','info-circle','chevron-down','file-zip','file-type-pdf','clipboard']) if(!names.includes(n))names.push(n);
const icons={};for(const name of new Set([...names,...Object.keys(mapping)]))icons[name]=await phosphor(name);
await syncCompanionIcons();
await writeFile(new URL('icons.json',import.meta.url),JSON.stringify(icons));
await build({entryPoints:[fileURLToPath(new URL('app.jsx',import.meta.url))],outfile:fileURLToPath(new URL('app.js',out)),nodePaths:[fileURLToPath(new URL('../charts/node_modules',import.meta.url))],bundle:true,minify:true,format:'iife',define:{'process.env.NODE_ENV':'"production"'},legalComments:'external',target:'chrome120'});
await copyFile(new URL('index.html',import.meta.url),new URL('index.html',out));
for(const weight of [400,500,600])await copyFile(new URL(`../charts/node_modules/@fontsource/inter/files/inter-latin-${weight}-normal.woff2`,import.meta.url),new URL(`inter-${weight}.woff2`,out));
const notices=await readFile(new URL('../../src/Syphon.Desktop/ChartAssets/THIRD-PARTY-NOTICES.txt',import.meta.url),'utf8');
await writeFile(new URL('THIRD-PARTY-NOTICES.txt',out),notices+'\n\nPhosphor Icons 2.1.1\n'+await readFile(new URL('../charts/node_modules/@phosphor-icons/core/LICENSE',import.meta.url),'utf8')+'\n\nInter\n'+await readFile(new URL('../charts/node_modules/@fontsource/inter/LICENSE',import.meta.url),'utf8'));

await copyFile(new URL('typography.css',import.meta.url),new URL('typography.css',out));
for(const name of ['plex-400.woff2','plex-500.woff2','plex-600.woff2','space-500-600.woff2','IBM-Plex-LICENSE.txt','Space-Grotesk-OFL.txt','sources.json'])await copyFile(new URL('fonts/'+name,import.meta.url),new URL(name,out));

await writeFile(new URL('THIRD-PARTY-NOTICES.txt',out),(await readFile(new URL('THIRD-PARTY-NOTICES.txt',out),'utf8'))+'\n\nIBM Plex Sans\n'+await readFile(new URL('fonts/IBM-Plex-LICENSE.txt',import.meta.url),'utf8')+'\n\nSpace Grotesk\n'+await readFile(new URL('fonts/Space-Grotesk-OFL.txt',import.meta.url),'utf8'));
await copyFile(new URL('redesign.css',import.meta.url),new URL('redesign.css',out));

await copyFile(new URL('theme.css',import.meta.url),new URL('theme.css',out));

// One canonical palette feeds desktop HTML, the native fallback and extension surfaces.
const themeTokens=await readFile(new URL('theme-tokens.css',import.meta.url),'utf8');
await writeFile(new URL('theme-tokens.css',out),themeTokens);
await writeFile(new URL('../../extensions/syphon/theme-tokens.css',import.meta.url),themeTokens);
const videoPath=new URL('../../extensions/syphon/video-button.js',import.meta.url);
const video=await readFile(videoPath,'utf8');
await writeFile(videoPath,video.replace(/\/\* OV_TOKENS_START \*\/[\s\S]*?\/\* OV_TOKENS_END \*\//,'/* OV_TOKENS_START */'+JSON.stringify(themeTokens.replace(':root',':host'))+'/* OV_TOKENS_END */'));
const tokens=Object.fromEntries([...themeTokens.matchAll(/--ov-([\w-]+):([^;]+);/g)].map(m=>[m[1],m[2].trim()]));
const brushes={Bg:'canvas',Surface:'shell',Card:'panel-raised',Inset:'inset',Line:'border',Accent:'accent',Mid:'accent-pressed',Text:'text-primary',Bright:'text-primary',Sub:'text-secondary',Muted:'text-muted',AccentText:'accent-hover',Emphasis:'text-primary'};
const materialPath=new URL('../../src/Syphon.Desktop/Styles/Materials.xaml',import.meta.url);
let materials=await readFile(materialPath,'utf8');
for(const [key,token]of Object.entries(brushes))materials=materials.replace(new RegExp('(<SolidColorBrush x:Key="'+key+'" Color=")[^"]+("/)'),'$1'+tokens[token]+'$2');
const light=tokens['edge-light'].match(/[\d.]+/g).map(Number),lightHex='#'+light.slice(0,3).map(n=>n.toString(16).padStart(2,'0')).join('');
materials=materials.replace(/(<DropShadowEffect x:Key="RaisedLight" Color=")[^"]+(" Opacity=")[^"]+/,`$1${lightHex}$2${light[3]}`);
await writeFile(materialPath,materials);
