import { copyFile, mkdir, readFile, writeFile } from 'node:fs/promises';

// Copy the supplied PNGs unchanged; package the taskbar PNGs into a multi-size Windows ICO.
const root = new URL('../', import.meta.url);
const source = new URL('design/brand/', root);
const copies = [
  ['hybrid-v3/taskbar/syphon_boxed_256.png', 'src/Syphon.Desktop/Assets/Brand/syphon_window_icon.png'],
  ['hybrid-v3/transparent/syphon_emblem_128.png', 'src/Syphon.Desktop/Assets/Brand/syphon_emblem_transparent.png'],
];
for (const destination of ['src/Syphon.Desktop/DesktopAssets/brand/', 'extensions/syphon/brand/']) {
  const desktop = destination.startsWith('src/');
  copies.push(['hybrid-v3/transparent/syphon_emblem_128.png', destination + 'emblem.png']);
  copies.push(['syphon_wordmark_clean.png', destination + 'wordmark.png']);
  for (const size of [16, 24, 32, 48, 64, 128]) {
    const filename = !desktop && [16,32,48,128].includes(size)
      ? `hybrid-v3/browser-extension/icon${size}.png`
      : `hybrid-v3/transparent/syphon_emblem_${size}.png`;
    copies.push([filename, destination + `icon-${size}.png`]);
  }
}
copies.push(['hybrid-v3/transparent/syphon_emblem_64.png', 'extensions/syphon/icon.png']);
for (const [filename, destination] of copies) {
  const target = new URL(destination, root);
  await mkdir(new URL('./', target), { recursive: true });
  await copyFile(new URL(filename, source), target);
}

// PNG-compressed ICO frames preserve each provided raster byte-for-byte.
const sizes=[16,20,24,32,40,48,64,96,128,256];
const frames=await Promise.all(sizes.map(size=>readFile(new URL(`hybrid-v3/taskbar/syphon_boxed_${size}.png`,source))));
const directory=Buffer.alloc(6+16*sizes.length);directory.writeUInt16LE(1,2);directory.writeUInt16LE(sizes.length,4);
let offset=directory.length;
frames.forEach((frame,i)=>{const at=6+16*i;directory[at]=directory[at+1]=sizes[i]===256?0:sizes[i];directory.writeUInt16LE(1,at+4);directory.writeUInt16LE(32,at+6);directory.writeUInt32LE(frame.length,at+8);directory.writeUInt32LE(offset,at+12);offset+=frame.length;});
await writeFile(new URL('src/Syphon.Desktop/Assets/Brand/syphon_windows.ico',root),Buffer.concat([directory,...frames]));
