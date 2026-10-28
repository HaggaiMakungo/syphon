import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
const root = new URL('../', import.meta.url);
const bytes = path => readFile(new URL(path, root));

test('extension deploys original Beta 19 icons without changing identity or required permissions', async () => {
  const manifest = JSON.parse(await bytes('extensions/syphon/manifest.json'));
  assert.ok(manifest.key);
  assert.deepEqual(manifest.permissions.toSorted(), ['activeTab','contextMenus','nativeMessaging','storage','notifications'].toSorted());
  for (const size of [16,32,48,64,128]) {
    const name = `brand/icon-${size}.png`;
    assert.equal(manifest.icons[size], name);
    assert.equal(manifest.action.default_icon[size], name);
    const icon = await bytes('extensions/syphon/' + name);
    assert.equal(icon.readUInt32BE(16), size);
    assert.equal(icon.readUInt32BE(20), size);
    assert.equal(icon[25], 6, 'Compact icons need RGBA transparency rather than a solid dark tile.');
    const source = `transparent/syphon_emblem_${size}.png`;
    assert.deepEqual(icon, await bytes(`design/brand/hybrid-v3/${source}`));
  }
  assert.deepEqual(manifest.web_accessible_resources, [{ resources: ['brand/icon-64.png'], matches: ['http://*/*','https://*/*'] }]);
  assert.deepEqual(await bytes('extensions/syphon/icon.png'), await bytes('design/brand/hybrid-v3/transparent/syphon_emblem_64.png'));
});

test('all app and popup surfaces use original Beta 19 artwork with the clean wordmark', async () => {
  for (const prefix of ['src/Syphon.Desktop/DesktopAssets/brand/', 'extensions/syphon/brand/']) {
    const source='transparent/syphon_emblem_128.png';
    assert.deepEqual(await bytes(prefix+'emblem.png'), await bytes('design/brand/hybrid-v3/'+source));
    assert.deepEqual(await bytes(prefix+'wordmark.png'), await bytes('design/brand/syphon_wordmark_clean.png'));
  }
  const nativeIcon = await bytes('src/Syphon.Desktop/Assets/Brand/syphon_windows.ico');
  for(let i=0;i<nativeIcon.readUInt16LE(4);i++){const at=6+i*16,size=nativeIcon[at]||256,length=nativeIcon.readUInt32LE(at+8),offset=nativeIcon.readUInt32LE(at+12);assert.deepEqual(nativeIcon.subarray(offset,offset+length),await bytes(`design/brand/hybrid-v3/transparent/syphon_emblem_${size}.png`));}
  assert.equal(nativeIcon.readUInt16LE(2), 1);
  assert(nativeIcon.readUInt16LE(4) > 1, 'Windows icon must retain its multiple resolutions.');
  const sizes=Array.from({length:nativeIcon.readUInt16LE(4)},(_,i)=>nativeIcon[6+i*16]||256);
  assert.deepEqual(sizes.toSorted((a,b)=>a-b),[16,20,24,32,40,48,64,96,128,256]);
  const windowIcon=await bytes('src/Syphon.Desktop/Assets/Brand/syphon_window_icon.png');
  assert.equal(windowIcon.readUInt32BE(16),256);
  assert.deepEqual(windowIcon,await bytes('design/brand/hybrid-v3/transparent/syphon_emblem_256.png'));
  assert.match((await bytes('src/Syphon.Desktop/App.xaml')).toString(), /SyphonWindowIcon.*syphon_window_icon\.png/);
  assert.deepEqual(await bytes('src/Syphon.Desktop/Assets/Brand/syphon_emblem_transparent.png'), await bytes('design/brand/hybrid-v3/transparent/syphon_emblem_128.png'));
});

test('imported v3 files match the supplied library checksums',async()=>{
  const inventory=JSON.parse(await bytes('design/brand/hybrid-v3/verification.json'));
  for(const entry of inventory.files) {
    const actual=await bytes('design/brand/hybrid-v3/'+entry.file.replace(/^boxed\//,'taskbar/'));
    assert.equal(createHash('sha256').update(actual).digest('hex'),entry.sha256,entry.file);
  }
});
