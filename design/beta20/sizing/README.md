# Syphon Size Studio

Run `node design/beta20/sizing/server.cjs` from the repository root, then open http://127.0.0.1:8769/.

This lightweight browser tool serves the existing `artifacts/beta20-compact/DesktopAssets` bundle and stylesheet directly. It creates no app build and changes no installed/unpacked app assets. `preview.js` provides isolated sample data and a browser-only command bridge; there is no native host or network download capability.

The desktop reference is 1920×1080. Match Windows display scaling in the sidebar. Browser zoom must be 100% when judging actual size. Fit desktop is an overview and explicitly shows its reduction factor. The 48px taskbar and 64px icon references are illustrative; taskbar size and icon spacing vary with Windows preferences.

The iframe viewport is window DIPs divided by interface scale. Its visible transform is interface scale times selected Windows scaling. This reproduces WebView viewport/breakpoint behaviour and the physical window footprint. CSS properties in the controls are before these scale factors. The report includes both CSS/DIP sizes and resulting desktop pixels.

Eight previews: Downloads, History, Settings, New Download, and four floating states. Main previews share window dimensions; other states retain separate dimensions. Element selection supports individual DOM paths or matching classes, with overrides scoped to the selected preview. Group changes apply across their specified selectors. Changes persist in this origin's local storage; Reset clears them. Copy or Save measurements exports sizes, CSS overrides, and restorable settings. Nothing is applied to the desktop app automatically.

Browser validation covered all eight layouts, group typography, individual selection overrides, refresh persistence, Windows scaling, maximize/restore, export contents/download, and image loading at 1920×1080.
