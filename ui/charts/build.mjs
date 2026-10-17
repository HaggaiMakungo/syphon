import { build } from 'esbuild';
import { mkdir, copyFile, readFile, writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
const output = new URL('../../src/Syphon.Desktop/ChartAssets/', import.meta.url);
await mkdir(output, { recursive: true });
await build({ entryPoints: [fileURLToPath(new URL('chart.jsx', import.meta.url))],
  outfile: fileURLToPath(new URL('chart.js', output)), bundle: true, minify: true,
  format: 'iife', define: { 'process.env.NODE_ENV': '"production"' }, legalComments: 'external', target: 'chrome120' });
await writeFile(new URL('index.html',output),(await readFile(new URL('index.html',import.meta.url),'utf8')).replace('/* OV_THEME */',await readFile(new URL('../desktop/theme-tokens.css',import.meta.url),'utf8')));
const lock = JSON.parse(await readFile(new URL('package-lock.json', import.meta.url), 'utf8'));
const notices = [];
for (const [location, pkg] of Object.entries(lock.packages)) {
  if (!location || pkg.dev || pkg.optional) continue;
  for (const name of ['LICENSE', 'LICENSE.txt', 'LICENSE.md', 'license', 'LICENCE']) {
    try { notices.push(`${location} ${pkg.version}\n${await readFile(new URL(`${location}/${name}`, import.meta.url), 'utf8')}`); break; } catch {}
  }
}
await writeFile(new URL('THIRD-PARTY-NOTICES.txt', output), notices.join('\n\n----------------\n\n'));


