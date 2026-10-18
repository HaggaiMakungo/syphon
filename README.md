<p align="center"><img src="src/Syphon.Desktop/Assets/Brand/syphon_emblem_transparent.png" width="88" alt="Syphon"></p>

# Syphon

**Your Downloads folder should not require an expedition.**

You saved a video, grabbed a document, and downloaded something called `final_final_2.zip`. Now one transfer has stopped, another has vanished into a browser menu, and finding the finished file has become a small archaeological project.

Syphon gives those downloads a home: a Windows desktop app for files, videos and audio, with a persistent queue, readable history and a floating transfer window. It also has a rather committed relationship with violet lighting. The downloads are doing the work; they may as well look good doing it.

## Where things stand

**Current local beta: 0.1.0-beta.13 · Windows x64.**

The installer is built and locally self-signed. Functional and installer acceptance checks have passed according to the owner on their PC, including the latest Open folder fix. A download-page preview, release notes, checksums and browser instructions are prepared.

Public downloads and browser-store listings have **not** been published. Outstanding FFmpeg source/build-material work is tracked in the [release preparation notes](docs/release-preparation-beta13.md). The current [release notes](docs/releases/0.1.0-beta.13.md) describe the exact build and its limits.

## What it does

### Keeps the queue together

Add a link, choose where it goes, and start it now or leave it queued. Pause and resume supported file transfers, set priorities and speed limits, and keep an eye on activity without giving the main window permanent custody of your screen.

Eligible large files can use multiple connections. Temporary network failures retry with a delay. Closing Syphon saves the queue; reopening restores it and waits for you to Resume.

### Brings your browser along

The **Syphon this** companion connects Chrome or Edge to the desktop app. Send a supported link manually, or enable optional download capture and video detection. Quality and caption choices appear where the source supports them.

Capture waits for Syphon's saved-task acknowledgment before cancelling an eligible browser transfer. Optional sign-in sharing can pass scoped context for supported direct-file downloads. Permissions are requested when you enable the relevant feature.

### Gives audio its cover back

Supported YouTube downloads can become MP4 video or MP3 audio. MP3 downloads embed available source artwork or the video thumbnail, without leaving a separate image beside the track. Missing artwork still lets the audio finish. A music library full of blank squares has suffered enough.

Unprotected on-demand HLS/DASH media is supported too. Website compatibility varies; media jobs restart from the beginning on Resume.

### Makes finished files easier to find

Default category folders are **Videos, Music, Documents, Programs and Misc**. You can still choose a custom destination. History provides search, filters, explicit outcomes, file details and checksum tools.

Open folder now opens the containing folder directly through Windows. An extremely reasonable ambition for a button with that name.

## Installing the packaged beta

The Windows installer includes the app runtime, native browser host, FFmpeg, FFprobe, yt-dlp, Node and the browser companion. You do not need to collect those dependencies yourself. WebView2 setup may need an internet connection if that runtime is absent.

The default app location is Program Files\Syphon. Download storage is separate. Setup supports upgrade, repair and uninstall; downloaded files and local history are preserved by default.

The installer uses a **local self-signed certificate**, without a timestamp. It is not publicly trusted, and Windows publisher or SmartScreen warnings may remain. The preparation process does not import a certificate into Windows trust stores.

### Connecting Chrome or Edge

There is one manual browser step before the companion can join in:

1. Enable **Browser integration** in Syphon setup.
2. At the end, choose **Browser extension setup → Open extension folder**.
3. Open `chrome://extensions` or `edge://extensions`, enable **Developer mode**, and choose **Load unpacked**.
4. Select the folder containing `manifest.json`. Open **Syphon this** from the browser's Extensions menu and select **Check connection**.

Refresh existing video tabs afterwards. The installer registers the native host; the browser still needs you to load its extension. The prepared package includes full setup and troubleshooting instructions, and [browser integration documentation](docs/browser-integration.md) covers development installs.

## The important limits

Resume depends on the server supporting the required validators and range requests. A changed source, missing partial data or incompatible server can require **Restart fresh**. Existing destination files are preserved.

Syphon does not bypass DRM. Browser sign-in sharing is optional, limited to eligible direct-file downloads, and does not give YouTube extraction a browser account session. A recorded SHA-256 shows the downloaded file's digest; verifying its origin requires a trusted reference.

Download URLs and history are stored locally in `%LOCALAPPDATA%\Syphon\tasks.db`. URLs can contain access tokens, so treat that database as private. Temporary browser sign-in context stays in memory rather than the task database. Removing a task erases its resume history; unfinished file deletion is a separate choice. Completed files are preserved.

Successful tests on the owner's PC are not clean-machine or power-loss certification. [Release preparation](docs/release-preparation-beta13.md) keeps the remaining checks visible.

## Build it yourself

If opening the bonnet is part of the fun, this is the route in.

Use Windows x64 and the .NET SDK pinned in `global.json`. Run `./bootstrap.ps1` if that SDK is missing; it verifies Microsoft's archive and puts the SDK in this checkout's `.tools` folder without changing the system installation or PATH.

```powershell
./bootstrap.ps1
./build.ps1 -Test
./run.ps1
```

After building, `Syphon.cmd` also launches the app. For a development browser connection, use `./connect-browser.ps1 -NoBuild`, then load `extensions\syphon` as an unpacked extension. Installer users should use setup's connection flow instead.

Development media checks require FFmpeg/FFprobe and the appropriate local extractor/runtime tools. [Media behavior](docs/media.md), [YouTube setup](docs/youtube.md) and [the pinned runtime build](docs/media-runtime-build.md) explain those inputs. The packaged installer already includes its reviewed versions.

<details>
<summary>Transfer internals and developer checks</summary>

By default, two downloads run at once. Eligible files of at least 16 MiB can use up to four connections, with four requests per site across tasks. Temporary connection failures and selected HTTP errors retry up to five times. Settings can change the supported limits.

Resume validates a strong ETag, a known size, the final URL and matching range responses. Checkpoint blocks are verified when reopening. The database schema is currently version 9; older builds cannot reopen an upgraded store. Media conversion restarts on Resume, and browser receipts provide duplicate protection after reopening or removing a task.

```powershell
# Include a transfer larger than 4 GiB; allow about 4.1 GiB temporary space.
./build.ps1 -Large

# Opt-in public HTTPS checks.
./build.ps1 -Test -Internet

# Media checks with the development tools available.
./build.ps1 -Test -Media

# Start the local-only deterministic HTTP fixture.
./.tools/dotnet/dotnet.exe run --project tests/Syphon.TestServer -c Release --no-build
```

The fixture includes segmented transfers, changed validators, ignored ranges, redirects, stalls, truncation and unknown lengths. Test projects return nonzero on failure. Desktop fixtures use test-owned stores and off-screen windows; transfer recovery tests include terminating worker processes around durability boundaries. Those checks do not establish power-loss safety.

Core extension tests require Node 20 or later; the optional DOM suite uses jsdom 30.1.3 and a compatible runtime. Browser connection scripts support Windows PowerShell 5.1 and PowerShell 7.

</details>

## Around the repository

| Path | What lives there |
|---|---|
| `src/Syphon.Engine` | HTTP transfers, queueing, persistence, recovery and media processing |
| `src/Syphon.Desktop` | Windows host, desktop UI, floating window and dialogs |
| `src/Syphon.Setup` | Installation, maintenance and removal |
| `src/Syphon.Core` | Shared transfer models and states |
| `src/Syphon.BrowserBridge`, `src/Syphon.NativeHost` | Browser-to-desktop connection |
| `extensions/syphon` | Chrome/Edge companion |
| `packaging` | Pinned inputs, installer assembly and local release preparation |
| `tests` | Transfer, desktop, browser, packaging and release checks |

For the latest continuation context, read [the release handoff](docs/RELEASE-HANDOFF.md). For transfer details, see [segmentation and recovery](docs/segmented-downloads.md) and [queue compatibility](docs/queue-and-compatibility.md).

## License

Syphon's original code is [MIT licensed](LICENSE). Bundled dependencies retain their [own licenses and notices](THIRD-PARTY-NOTICES.md). Source-material collection and the remaining distribution work are tracked separately; the project license does not replace those obligations.
