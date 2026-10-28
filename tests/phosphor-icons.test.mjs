import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {mapping,phosphor} from '../scripts/phosphor-icons.mjs';
import {pixelIcon} from '../scripts/pixel-icons.mjs';
const read=path=>readFile(new URL('../'+path,import.meta.url),'utf8');
test('every desktop icon is an official local Phosphor glyph',async()=>{
 const icons=JSON.parse(await read('ui/desktop/icons.json'));
 for(const name of Object.keys(mapping)){
  assert.equal(icons[name],await phosphor(name),name);
  assert.doesNotMatch(icons[name],/shape-rendering="crispEdges"/);
  assert.match(icons[name],/data-icon-family="phosphor"/);
 }
});
test('popup and rebuilt floating controls use the shared Phosphor family',async()=>{
 const popup=await read('extensions/syphon/popup.html');
 assert.equal([...popup.matchAll(/data-icon-family="phosphor"/g)].length,7);
 const floating=await read('src/Syphon.Desktop/FloatingDownloadWindow.Html.cs');
 assert(!floating.includes('BuildHtml()'));
 assert(floating.includes('interface.css'));
 const component=await read('ui/desktop/live-floating.jsx');
 assert(component.includes('monitor-core'));
 assert(component.includes('Progress task='));
 assert(component.includes('player-play'));
});
