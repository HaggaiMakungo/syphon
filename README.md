# Syphon

A Windows download manager with persistent tasks, verified pause/resume, and a desktop download list.

## Run

Requires Windows x64 and the .NET SDK pinned in `global.json`. To install that SDK locally in this project, run `./bootstrap.ps1`. It downloads Microsoft's SDK archive and verifies its SHA-512 against the official release manifest. The SDK is kept in `.tools/`; system installations and PATH are unchanged.

```powershell
./build.ps1 -Test
./run.ps1
```

After building, you can also double-click `Syphon.cmd` in this folder.

Paste an HTTP/HTTPS link, choose a destination, and click Download. Two downloads run at a time; additional downloads wait in a FIFO queue. Automatic mode uses up to four connections for eligible files of at least 16 MiB, with at most four requests per site across all tasks. Check Single connection when adding a download or restarting fresh to use one connection. Select a task to Pause, Resume, Cancel, Restart fresh, Remove, or open its completed file's folder. The list shows readable sizes, speed, estimated time remaining and connections. Existing destination files are preserved. Each completed download reports its SHA-256. [Segmentation and recovery details](docs/segmented-downloads.md).

Temporary connection failures and selected HTTP errors retry automatically up to five times, with increasing delays and jitter. Valid Retry-After values up to five minutes are respected; longer waits require a manual retry. Retry waits release their transfer slot and can be paused or cancelled. Permission errors, invalid file data and certificate failures are not automatically retried. [Queue and compatibility details](docs/queue-and-compatibility.md).

Remove erases the task's resume history. Unfinished data stays on disk unless you select its deletion in the removal dialog. Completed files are always preserved.

Closing Syphon pauses active work and saves checkpoints. Reopening restores the list and verifies saved blocks; downloads wait for you to Resume. Resume requires a strong ETag, a known size, the same final URL, and a valid matching range response. Changed files, ignored ranges, weak/missing validators, and missing or modified partial files require Restart fresh. That action explicitly discards only the selected task's unfinished data. Cancel retains its data and can be resumed later when validation permits.

Task records, full download URLs, and checkpoint manifests are stored in `%LOCALAPPDATA%\Syphon\tasks.db`. Partial data stays beside its destination in uniquely named `.syphon-part` files. One app instance owns the database at a time. SQLite uses the Windows system library through Microsoft.Data.Sqlite.Core; Windows 10 or later is required. Do not share the database: URLs may contain access tokens. There is no browser credential capture or cookie persistence.

Chrome/Edge link and media handoff is available as **Syphon this**. After building, run `./connect-browser.ps1 -NoBuild`, then load `extensions\syphon` as an unpacked extension in the browser's extensions page. The popup offers optional automatic download capture and a video button with detected quality/source and subtitle choices. MP3 conversion and unprotected on-demand HLS/DASH downloads use installed FFmpeg/FFprobe tools. Hidden manifests are discovered from page requests with optional webRequest permission. Capture cancels the browser transfer only after Syphon saves the task; cookies are not transferred. Protected playback is detected without DRM bypass or key extraction. See [browser setup](docs/browser-integration.md) and [media behavior and limits](docs/media.md). Packaging and store distribution remain future work. The CLI still performs fresh transfers without task history. A locally computed SHA-256 does not establish authenticity without a trusted expected digest. Process-interruption tests do not establish power-loss safety.

The database upgrades to schema version 5 when opened by this build. Existing tasks keep their checkpoints; older builds cannot reopen the upgraded database. Browser receipts include explicit media/YouTube format options and provide durable duplicate protection even after a task is removed. Media jobs restart from the beginning on Resume. Ctrl+L focuses the link, and F6 focuses the download list.

YouTube videos, Shorts and embeds have [dedicated extraction support](docs/youtube.md), with quality selection and MP3 conversion. Run `install-youtube.ps1` once to install a checksum-verified local yt-dlp release; Node 22+, FFmpeg and FFprobe are required. Setup is already complete on this development machine. Reload the extension and refresh the YouTube tab. No browser cookies or account sessions are imported.

## Development tools

```powershell
# Full test run, including a transfer larger than 4 GiB; requires about 4.1 GiB temporary space
./build.ps1 -Large

# Opt-in public HTTPS checks, in addition to the local suite
./build.ps1 -Test -Internet

# Start the local-only HTTP fixture for trying the desktop
./.tools/dotnet/dotnet.exe run --project tests/Syphon.TestServer -c Release --no-build

# Download with the command-line client
./.tools/dotnet/dotnet.exe run --project src/Syphon.Cli -c Release --no-build -- http://127.0.0.1:8878/file C:\Downloads\example.bin
```

The fixture binds only to loopback and generates deterministic bytes. Use `/segments?size=67108864&key=demo` for a 64 MiB four-connection demo, `/resume?size=67108864` for ordinary single-connection Pause/Resume, or `/file?size=10485760` for a fresh transfer. Resume failure scenarios include `/resume?size=8388608&mode=changed` (identity change) and `mode=ignore` (server ignores Range). Other routes cover unknown sizes, stalls, truncation, redirects, HTTP errors, offsets beyond 4 GiB, and mutable validators.

Tests are executable integration projects invoked by `build.ps1`, without an external test-runner framework. They return a nonzero exit code on failure. Tests include actual child-process termination before/after file flush, after checkpoint commit, and before/after publication rename. The desktop test exercises real XAML and button events in off-screen windows, closes/reopens the window against a test-owned database, and saves renders under `artifacts/desktop/`. Results are saved to `artifacts/test-results.json`.

## Layout

| Path | Purpose |
|---|---|
| `src/Syphon.Core` | Transfer requests, progress, results, states, typed failures |
| `src/Syphon.Engine` | HTTP transport, task coordinator, SQLite store, recovery and publication |
| `src/Syphon.Desktop` | Persistent WPF download list and task actions |
| `src/Syphon.Cli` | Command-line download harness |
| `src/Syphon.BrowserBridge` | Bounded native framing and current-user desktop pipe |
| `src/Syphon.NativeHost` | Browser native messaging executable and desktop launcher |
| `extensions/syphon` | Manifest V3 Chrome/Edge extension and handoff popup |
| `tests/Syphon.TestServer` | Controllable loopback HTTP server |
| `tests/Syphon.Engine.Tests` | Byte-correctness and failure-path integration checks |
| `tests/Syphon.Desktop.SmokeTests` | Desktop workflow checks and image renders |
| `tests/Syphon.Browser.Tests` | Real native-host/pipe/cold-start checks |

- [Implementation plan](docs/implementation-plan.md): proposed architecture, first usable version, milestones, and release gates.
- [Build status](docs/build-status.md): implemented behavior, test results, and remaining work.
- [Research index](research/README.md): five static investigation passes informing the requirements.

The working name is Syphon. Durable tasks, queue management, automatic retries, segmented transfers, a persistent desktop workflow, browser handoff/capture, quality/subtitle detection, MP3 conversion and unprotected HLS/DASH downloads are implemented. Initial public HTTPS compatibility and automated accessibility checks are included. Universal site extraction, live recording, manual accessibility and broader authenticated-site/filesystem compatibility remain future work. Running core extension tests requires Node.js 20 or later; the optional DOM suite uses jsdom 30.1.3 and a compatible Node runtime. Media checks require FFmpeg/FFprobe (`build.ps1 -Test -Media`). Browser connection supports Windows PowerShell 5.1 and PowerShell 7.


## Installer candidate

Native Windows setup and reproducible packaging sources are now available; see [installer build, validation and release gates](docs/installer.md). The first bundled installer is an unsigned local beta candidate, not a public release.
