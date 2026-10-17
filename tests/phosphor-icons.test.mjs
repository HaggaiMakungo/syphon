import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {mapping,phosphor} from '../scripts/phosphor-icons.mjs';
const read=path=>readFile(new URL('../'+path,import.meta.url),'utf8');
test('every desktop icon is a real bundled Phosphor regular export',async()=>{
 const icons=JSON.parse(await read('ui/desktop/icons.json'));
 for(const name of Object.keys(mapping)){
  assert.equal(icons[name],await phosphor(name),name);
  assert.match(icons[name],/viewBox="0 0 256 256"/);
  assert.match(icons[name],/data-icon-family="phosphor"/);
 }
});
test('popup and floating controls use Phosphor, including dynamic resume',async()=>{
 const popup=await read('extensions/syphon/popup.html');
 assert.equal([...popup.matchAll(/<svg\b/g)].length,7);
 assert.equal([...popup.matchAll(/data-icon-family="phosphor"/g)].length,7);
 const floating=await read('src/Syphon.Desktop/FloatingDownloadWindow.Html.cs');
 assert.equal([...floating.matchAll(/data-icon-family="phosphor" aria-hidden/g)].length,8);
 assert.match(floating,/viewBox="0 0 256 256"[^>]*id="pauseIcon"|id="pauseIcon"[^>]*viewBox="0 0 256 256"/);
 assert(!floating.includes('polygon points="5 3 19 12 5 21 5 3"'));
});
