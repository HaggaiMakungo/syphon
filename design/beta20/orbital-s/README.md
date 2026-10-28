# Syphon Orbital S — first pixel icon set

Grid-native, editable production candidates based on the selected Orbital S concept. The established S ribbon and black globe are preserved in a simplified pixel silhouette. These assets are separate from the installed application and the existing approved `design/brand/hybrid-v3/` library.

## Contents

- `svg/`: editable 16, 20, 24 and 32-grid drawings; colour, app-tile, white and muted masters; 0/25/50/75/100% fill references.
- `png/transparent/`, `png/tile/`, `png/white/`, `png/muted/`: 16, 20, 24, 32, 40, 48, 64, 96, 128, 256, 512 and 1024px exports. The coloured emblem keeps an opaque black globe and transparent exterior; monochrome assets contain only the mark so the underlying surface shows through.
- `png/states/`: five 256px progress-state references.
- `windows/`: transparent and app-tile ICO files with 16/20/24/32/48/64/128/256px PNG frames.
- `browser-extension/`: 16/32/48/128px transparent PNGs. This directory is an icon fragment, not an extension manifest.
- `orbital-icon.js`: standalone custom element with inline SVG geometry. No external library, font or network dependency.
- `manifest.json`: dimensions, byte sizes and SHA-256 for every PNG export.

The SVGs are a deliberate consistent-grid adaptation, not a pixel-for-pixel trace of the generated concept sheet. Small drawings are generated directly at their own grids; large exports use the 32-grid artwork. Noninteger grid scaling, such as a 40px rendering of the 32-grid mark, inevitably gives uneven physical pixel runs; use the native 16/20/24/32/64 sizes when exact grid ratios matter.

## Animation

Load `orbital-icon.js`, then use:

```html
<syphon-orbital-icon id="transfer-mark" size="64" progress="34" state="downloading"></syphon-orbital-icon>
```

```js
const mark = document.querySelector('#transfer-mark');
mark.setProgress(62);          // Percentage, clamped to 0–100.
mark.setState('paused');       // Keep progress and dim the filled ribbon.
mark.setState('downloading');  // Restore the active colour.
mark.complete();              // Set 100%, shimmer once, then stay still.
mark.reset();                 // Return to a quiet empty mark.
```

Supported states are `idle`, `downloading`, `paused` and `complete`. `tile` adds the dark rounded application tile. `label` overrides the accessible name. `size` selects a small grid automatically. Each SVG instance has unique clip IDs and runs independently.

The active icon uses a fast charging-style fill loop: purple rises through the mark, resets, and repeats every 1000ms by default. This signals downloading activity independently of the real download percentage. Set `duration="450"` for a quicker cycle; the supported range is 300–2000ms. `setProgress()` remains available as transfer metadata and for static snapshots, but does not drive the active loop.

Paused state freezes the charging animation at its current position and dims the filled ribbon. A directly rendered paused mark uses its supplied percentage as a static fallback. Completion stops the loop, fills the mark, performs one 850ms stepped shimmer, removes the animation class, and rests. Repeated updates while complete do not replay it. Icons initially rendered as complete start still. Starting a new download restarts the loop.

`completion` fires once per completion transition with `detail.animated`; `shimmerend` fires when the sweep finishes. Reduced-motion preference skips the loop and shimmer while preserving a static coloured active/completed mark. Because the loop represents activity, it can also be used for unknown-size downloads without implying a percentage.

## Preview and build

Open `preview.html` directly from this folder for the portable preview. Alternatively open `../html/icons.html`, or use the local preview at `http://127.0.0.1:8767/icons.html`. The simulated transfer is explicitly a preview control.

Run `node design/beta20/orbital-s/build-assets.cjs` from the project root to rebuild. It uses the repository’s existing `.tools/brand-icons` resvg dependency. Edit `build-assets.cjs` for geometry/palette and `animation-source.js` for behavior. `orbital-icon.js` is generated. A portable runtime/export copy is synced into `html/orbital-s/` for the existing local preview server.

This set does not replace the app’s current branding. Integrating icons into Windows, the extension and the redesigned screens remains a separate step.

## Verification

`verify-assets.py` passed 53 PNG hashes/dimensions, exterior transparency, crisp alpha on native small-grid marks, 16 PNG frames across two ICO files, increasing coloured fill for the five static fill snapshots, and four browser-icon copies. `../html/verify-icons.js` passed the 1000ms repeating cycle, visible fill movement independent of percentage, frozen pause/resume, completion stopping the loop, a one-shot shimmer, reduced motion and a 360px layout without overflow.

## Taskbar direction

Approved activity cadence: 1000ms. While any download is running, the taskbar Orbital S should repeat this charging fill, independent of transfer percentage. Pause when no transfers are running; on completion, shimmer once and settle. Native taskbar integration is still pending; the HTML preview demonstrates the motion only.
