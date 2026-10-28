<p align="center"><img src="src/Syphon.Desktop/Assets/Brand/syphon_emblem_transparent.png" width="88" alt="Syphon"></p>

# Syphon

**A download manager for people whose Downloads folder has developed its own government.**

You went online for one PDF. One. A perfectly respectable little document about something you were briefly convinced would improve your life.

Forty minutes later, your Downloads folder contains three videos, an album, `final_final_ACTUALLY_FINAL.zip`, and a driver for a printer you have never owned. The PDF is missing. The printer driver has been elected treasurer. There are rumours of a border dispute with Documents.

I would intervene, but frankly the driver has a stronger economic policy than I do.

**Syphon is a Windows desktop download manager for files, videos and audio.** It gives you a persistent queue, readable history, supported pause/resume, browser integration and a floating transfer window. You can see what is happening and find the finished file without commissioning a bloody excavation.

It is also very purple. Someone was allowed to discover gradients without a responsible adult in the room, and now the progress graph looks like it has a residency at a nightclub. We have decided to call this a visual identity.

## Where things stand

**Current public beta: 0.1.0-beta.17 · Windows x64.**

**[Download Syphon](https://haggaimakungo.github.io/syphon/)** · [GitHub beta release](https://github.com/HaggaiMakungo/syphon/releases/tag/v0.1.0-beta.17)

You only need the **332.4 MB installer**. The app runtime and media tools are bundled; source archives are separate, optional downloads. Browser-store listings are not available yet, so Chrome/Edge setup uses the included unpacked extension.

Beta17 enables Close to system tray by default, preserves explicit saved preferences, isolates the downloader build from unrelated host DLLs and closes identified native source/archive/build-recipe engineering gaps. Signed-package, media and tray checks passed on this PC. The owner confirmed Beta16 works; Beta17 upgrade acceptance and clean-machine testing remain pending.

The installer is locally self-signed, not publicly trusted or timestamped; Windows warnings may remain. The release includes checksums, the exact application source and both dependency source companions. See the [release notes](RELEASE-NOTES.md) and [source closure review](packaging/NATIVE-SOURCE-CLOSURE.md) for evidence and limits.

## What it does

### The queue gets some adult supervision

Add a link, choose where it goes, and start it now or leave it queued. Pause and resume supported file transfers, set priorities and speed limits, and keep an eye on activity without giving the main window permanent custody of your screen.

Eligible large files can use multiple connections. Temporary network failures retry with a delay. Closing Syphon saves the queue; reopening restores it and waits for you to Resume.

This means you can stop staring at a progress bar as though sustained eye contact will frighten the remaining megabytes down the cable. I have tried that. I leaned closer. I made a little encouraging noise. The router continued blinking with the emotional availability of a filing cabinet.

Appearance settings can make Close or Minimize hide Syphon to the system tray while transfers continue. Close to tray is enabled by default in Beta17; Minimize remains optional. Existing explicit saved choices are preserved. The tray menu can open Syphon, pause or resume everything, and Exit; launching Syphon again restores the existing window.

The floating window is there for a quick look instead. You may still make the noise. That part is between you and your furniture.

### Your browser is invited, subject to basic manners

The **Syphon this** companion connects Chrome or Edge to the desktop app. Send a supported link manually, or enable optional download capture and video detection. Quality and caption choices appear where the source supports them.

Capture waits for Syphon's saved-task acknowledgment before cancelling an eligible browser transfer. Optional sign-in sharing can pass scoped context for supported direct-file downloads. Permissions are requested when you enable the relevant feature.

There is a practical reason for the acknowledgment: snatching a download out of the browser before the desktop app has accepted it would be the software equivalent of throwing a parcel out of a moving van and shouting “DELIVERY” at a hedge. The hedge has not signed for anything. The hedge does not even live here.

### The album art can come out of witness protection

Supported YouTube downloads can become MP4 video or MP3 audio. MP3 downloads embed available source artwork or the video thumbnail, without leaving a separate image beside the track. Missing artwork still lets the audio finish.

Because at some point a music library full of identical blank squares stops looking minimalist and starts looking like a police briefing. Every artist has apparently been anonymised. The bass player knows something. Nobody is talking.

The picture lives inside the MP3, so it can travel with the file. You get the artwork without adopting a loose JPEG that will spend the next six months being called `hqdefault (7).jpg` and quietly ruining your afternoon.

Unprotected on-demand HLS/DASH media is supported too. Website compatibility varies; media jobs restart from the beginning on Resume.

### Seven folders and a small containment problem

Default category folders are **Videos, Music, Images, Documents, Programs, Compressed and Misc**. You can still choose a custom destination. History provides search, filters, explicit outcomes, file details and checksum tools.

Misc is for the files that arrive wearing a false moustache and refuse to state their business. We considered calling it “Evidence”, but that gives a completely unreasonable impression of the project and would require me to explain several USB sticks.

**Open folder now opens the containing folder directly through Windows.** That was a real bug fix. The button previously had the confidence to say Open folder while contributing absolutely fuck all to the opening of folders. A tiny management consultant in a rounded rectangle. It has since been given a more useful job description.

## Installing the packaged beta

The installation process is considerably calmer than the person writing this README.

The Windows installer includes the app runtime, native browser host, FFmpeg, FFprobe, yt-dlp, Node and the browser companion. You do not need to collect those dependencies yourself. WebView2 setup may need an internet connection if that runtime is absent.

The default app location is Program Files\Syphon. Download storage is separate. Setup supports upgrade, repair and uninstall; downloaded files and local history are preserved by default.

The installer uses a **local self-signed certificate**, without a timestamp. It is not publicly trusted, and Windows publisher or SmartScreen warnings may remain. The preparation process does not import a certificate into Windows trust stores.

### Connecting Chrome or Edge

Your browser still needs a one-time introduction to the companion. Please select the actual extension folder here; the browser has very little patience for interpretive dance.

1. Enable **Browser integration** in Syphon setup.
2. At the end, choose **Browser extension setup → Open extension folder**.
3. Open `chrome://extensions` or `edge://extensions`, enable **Developer mode**, and choose **Load unpacked**.
4. Select the folder containing `manifest.json`. Open **Syphon this** from the browser's Extensions menu and select **Check connection**.

Refresh existing video tabs afterwards. The installer registers the native host; the browser still needs you to load its extension. The prepared package includes full setup and troubleshooting instructions, and [the contributor guide](CONTRIBUTING.md) covers development installs.

## The important limits

The sensible paragraph has arrived. Please allow it to finish before I start accusing another peripheral of tax fraud.

Resume depends on the server supporting the required validators and range requests. A changed source, missing partial data or incompatible server can require **Restart fresh**. Existing destination files are preserved.

Syphon does not bypass DRM. Browser sign-in sharing is optional, limited to eligible direct-file downloads, and does not give YouTube extraction a browser account session. A recorded SHA-256 shows the downloaded file's digest; verifying its origin requires a trusted reference.

Download URLs and history are stored locally in `%LOCALAPPDATA%\Syphon\tasks.db`. URLs can contain access tokens, so treat that database as private. Temporary browser sign-in context stays in memory rather than the task database. Removing a task erases its resume history; unfinished file deletion is a separate choice. Completed files are preserved.

Successful tests on the owner's PC are not clean-machine or power-loss certification. FFmpeg's engineering source-material review has passed; other component obligations and browser-store distribution remain separate release work.

## Build it yourself

For anyone who sees a working application and immediately thinks “lovely, but I should be inside its organs”, the source is here too. Pull up a chair. Put the screwdriver down until you have read the commands.

Use Windows x64 and the .NET SDK pinned in `global.json`. Run `./bootstrap.ps1` if that SDK is missing; it verifies Microsoft's archive and puts the SDK in this checkout's `.tools` folder without changing the system installation or PATH.

```powershell
./bootstrap.ps1
./build.ps1 -Test
./run.ps1
```

After building, `Syphon.cmd` also launches the app. For a development browser connection, use `./connect-browser.ps1 -NoBuild`, then load `extensions\syphon` as an unpacked extension. Installer users should use setup's connection flow instead.

Development media checks require FFmpeg/FFprobe and the appropriate local extractor/runtime tools. [Packaging instructions](packaging/README.md) and [the pinned runtime build](packaging/MEDIA-RUNTIME.md) explain those inputs. The packaged installer already includes its reviewed versions.

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

See [CONTRIBUTING.md](CONTRIBUTING.md) for development setup and verification, and [packaging/README.md](packaging/README.md) for installer builds.

## License

Syphon's original code is [MIT licensed](LICENSE). Bundled dependencies retain their [own licenses and notices](THIRD-PARTY-NOTICES.md). Source-material collection and the remaining distribution work are tracked separately; the project license does not replace those obligations.
