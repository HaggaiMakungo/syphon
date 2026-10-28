import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
const root=new URL('../',import.meta.url),read=p=>readFile(new URL(p,root));
test('Beta 19 smooth S is the deployed desktop and extension identity',async()=>{
 for(const prefix of ['src/Syphon.Desktop/DesktopAssets/brand/','extensions/syphon/brand/'])
  for(const size of [16,24,32,48,64,128])assert.deepEqual(await read(prefix+`icon-${size}.png`),await read(`design/brand/hybrid-v3/transparent/syphon_emblem_${size}.png`));
 assert.deepEqual(await read('src/Syphon.Desktop/Assets/Brand/syphon_windows.ico'),await read('design/brand/hybrid-v3/windows/syphon_transparent.ico'));
});
test('runtime icons retain semantic selectors but use Phosphor geometry',async()=>{
 const icons=JSON.parse(await read('ui/desktop/icons.json'));
 for(const key of ['download','folder','settings','player-play','player-pause','file-zip','video']){
  assert.match(icons[key],/data-icon-family="phosphor"/);assert.doesNotMatch(icons[key],/shape-rendering="crispEdges"/);assert.match(icons[key],new RegExp('icon-tabler-'+key));
 }
});
