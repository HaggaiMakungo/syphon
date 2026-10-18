# Pinned Windows packaging inputs

The Windows installer build uses a dedicated cache and `packaging/dependencies.win-x64.json`. It does not discover bundled media tools from PATH, C:/ffmpeg, an installed Node directory or a previous installer stage. The cache and installer binaries are ignored by Git.

## Prepare a development machine

Use the .NET SDK selected by `global.json` (`bootstrap.ps1` can install the project-local SDK), Python 3.11 or newer, Git, and a developer Node/npm environment. Install the frontend dependencies from their checked-in lock with `npm ci --prefix ui/charts`. The desktop build uses the checked-in templates. Run `npm run build:desktop --prefix ui/charts`; the installer invokes `ui/desktop/build.mjs` directly using the pinned Node executable.

The approved input versions and hashes are in the dependency lock. Supply those exact files from your retained distributions; source URLs in the lock record provenance and are not a promise that historical downloads remain online. For the source-built FFmpeg distribution, use a directory with bin/ffmpeg.exe, bin/ffprobe.exe, LICENSE (the retained ffmpeg-COPYING.GPLv3 bytes), and README.txt (packaging/licenses/FFmpeg-source-build.txt). All bytes must match the current lock; the old vendor distribution is not interchangeable. The WebView2 cache entry is a snapshot of the mutable Evergreen bootstrapper endpoint; a newly downloaded bootstrapper may have a different hash and will be refused until the lock is deliberately reviewed and updated.

Example import using your own paths:

```powershell
python packaging/dependencies.py import --root "D:/Syphon build inputs" --ffmpeg-directory "D:/tool-distributions/ffmpeg" --node "D:/tool-distributions/node.exe" --yt-dlp "D:/tool-distributions/yt-dlp.exe" --webview2 "D:/tool-distributions/MicrosoftEdgeWebview2Setup.exe"
python packaging/dependencies.py verify --root "D:/Syphon build inputs"
./packaging/build-installer.ps1 -Version 0.1.0-beta.14 -DependencyRoot "D:/Syphon build inputs" -OutputDirectory "D:/Syphon builds/beta14"
```

Omit `--root` / `-DependencyRoot` to use `.tools/release-deps/win-x64` in this checkout. The import verifies every source before writing the cache, copies bytes rather than sharing hardlinks with installed global tools, and refuses unrelated cache files. Missing inputs, hash/length mismatches and filesystem links are errors. Changing the lock is a deliberate dependency update, not a workaround for a failed checksum.

## Build guarantees and limits

- Verify the dependency cache and the bootstrapper's Microsoft Authenticode signature before publishing.
- Run packaging regression tests, use the pinned Node binary for frontend bundling, and publish with the RID NuGet lock in locked mode.
- Create a fresh `artifacts/installer-work/<id>/stage` and sibling core for every build. Old outputs, removed extension files and arbitrary cached executables are not reused.
- Copy only the four locked media executables, FFmpeg notices and pinned bootstrapper. Keep original notices for the other dependencies from the source tree.
- Generate dependency provenance and a build-inputs record with source revision/dirty state, SDK, Node, Python, PowerShell, zlib and lock-file identity. No absolute developer input paths are recorded in the payload metadata.
- Recheck locked staged inputs during assembly. Use a matched core/uninstaller and reject stage/output overlap and linked stage paths.
- Write ZIP entries in fixed order with fixed timestamps and permissions. Fixture tests verify identical EXE bytes from identical core/payload bytes despite changed file mtimes and output paths. This does not establish byte-identical .NET/frontend builds on different machines, SDKs or compression-library versions.

Output includes the setup EXE, payload ZIP, release manifest, SHA256SUMS, build summary and build-inputs.json. Keep the source revision, clean/dirty flag, input lock and reports with any distributed candidate. A sourceDirty=true build is identifiable as a development candidate; use a clean committed revision for the final candidate.

Failed build workspaces are kept for inspection. They contain generated build files only; this change does not clean user installations or downloaded data. Old candidates retain their original checksums. Users still receive the .NET runtime and private media tools in the installer and do not need to install FFmpeg or Node separately.

## Verification and outstanding release work

Run `python tests/packaging_test.py` for import, corruption/missing/unexpected inputs, stage integrity, path protection and deterministic assembly checks. A filesystem-link test is skipped when this account cannot create a symlink; production path checks remain enabled. The real installer must additionally pass the existing --verify-package extraction/hash/tool-execution check.

The input hashes pin the previously inspected local baseline; they do not independently establish publisher authenticity for every third-party binary. The Microsoft bootstrapper signature is checked separately. Exact corresponding-source/build materials for bundled GPL binaries and their linked components remain a separate release gate; a hash, license text or source URL alone does not close that gate. Publicly trusted signing, clean-machine acceptance and store distribution remain outstanding; the local beta uses a self-signed certificate. Nothing here publishes the installer or creates an automatic updater.


The private yt-dlp build is documented in [MEDIA-RUNTIME.md](MEDIA-RUNTIME.md). The dependency lock, source manifests and retained notices identify the bundled tools; corresponding-source coverage remains incomplete.

Beta 14 selects the reviewed [FFmpeg/FFprobe source build](FFMPEG.md). Its clean-build identities, compatibility checks and independent engineering review are retained in the FFmpeg records. Keep the source archive identified by ffmpeg-source-evidence.win-x64.json alongside the installer. Component notices and records are mandatory payload entries. This engineering review does not establish legal/patent clearance or clearance for other dependencies.
