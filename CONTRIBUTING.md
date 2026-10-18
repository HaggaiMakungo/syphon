# Working on Syphon

Use Windows x64, Git, Python 3.11+, a Node/npm development environment, and the .NET SDK pinned in `global.json`. The project-local SDK bootstrap verifies its Microsoft archive and does not change system PATH.

```powershell
./bootstrap.ps1
npm ci --prefix ui/charts
npm run build --prefix ui/charts
npm run build:desktop --prefix ui/charts
./build.ps1 -Test
./run.ps1
```

Desktop templates, typography, theme tokens and components live in `ui/desktop`; chart sources live in `ui/charts`. The frontend build uses checked-in templates and production artwork under `design/brand`. Generated desktop assets are checked in so the native solution can build without a separate frontend build. Rebuild them when changing frontend sources and review the generated diff.

For a development browser connection, run `./connect-browser.ps1 -NoBuild`, then load `extensions/syphon` using Chrome or Edge's **Load unpacked** command. Use the companion's **Check connection** control. Moving the checkout requires running the connection script again. The script registers the native host for the current user; installer users should use setup instead.

## Checks

`./build.ps1 -Test` runs the standard checks. `-Internet` opts into public HTTPS tests; `-Media` requires FFmpeg, FFprobe and extractor/runtime tools. `-Large` exercises a transfer over 4 GiB and needs about 4.1 GiB of temporary space. Desktop fixtures use their own stores. These tests do not establish clean-machine or power-loss safety.

Core extension tests need Node 20+. The optional DOM tests use jsdom 30.1.3 with a compatible runtime. Packaging checks run with `python tests/packaging_test.py`; local release preparation checks use `python tests/release_preview_test.py`.

See [packaging/README.md](packaging/README.md) for exact installer inputs and [packaging/MEDIA-RUNTIME.md](packaging/MEDIA-RUNTIME.md) for the private yt-dlp build. `packaging/prepare_release.py --help` describes the existing Beta 13 preview tool; review its version-specific checks before preparing another release. Build binaries and caches are ignored by Git.

## Data and compatibility

The engine owns transfers, queueing and persistence; the desktop host and browser bridge issue commands to it. Keep task and browser receipt updates transactional, preserve existing destination files, and test recovery when changing persistence. Schema version 9 stores cannot be reopened by older builds. Never use a contributor's real history or downloads as a test fixture.

Syphon's code is MIT licensed. Keep third-party notices and pinned input records intact. Bundled tool source-material review remains incomplete; passing tests or pinning hashes does not by itself clear public redistribution.
