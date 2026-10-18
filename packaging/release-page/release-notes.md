# Syphon @@VERSION@@ — the window has left, the download has not

Prepared @@DATE@@ · Windows x64 · Local release preview, not published.

Closing the window now sends Syphon to the system tray by default. Your download keeps going. Previously, closing things was rather too effective at closing things. We have retained the concept of leaving while introducing the possibility of sitting quietly near the clock like a tiny violet pensioner.

## New in Beta @@BETA@@

- **Close to system tray is on by default** for new or missing settings. Existing explicit choices are preserved. Minimize to tray remains optional and off by default. Choose **Exit** from the tray to quit.
- The downloader build now admits only its pinned private runtime. Unrelated host DLLs no longer get invited along because they happened to be standing near PATH wearing a lanyard.
- Identified native dependency source/archive/build-recipe gaps are closed. Both source-material archives are available beside the installer. They are optional for running Syphon.
- Signed-package extraction, payload hashes, bundled tools, tray defaults and live MP4/MP3 checks passed on this PC. **Owner Beta17 upgrade acceptance is pending.** Beta16 was accepted; we have not forged a small congratulatory certificate for its successor.

## Also aboard this particular vehicle

### The album art has been reunited with the album

YouTube MP3 downloads embed available source artwork or the video thumbnail as JPEG cover art. The image lives inside the MP3, with no separate picture saved beside it. If artwork is unavailable, the audio can still finish.

This saves your music library from looking like a collection of missing-person notices for rectangles. Previously the singer was a blank square. The drummer was a blank square. The album was a blank square. At that point you have accidentally downloaded a spreadsheet with a bass line.

### New Download has remembered that you were speaking to it

The New Download window stays on top while open. You can finish choosing the destination without the dialog disappearing behind another window halfway through the conversation.

I have relatives who do this. You ask a straightforward question, they wander into another room, and suddenly you are negotiating a save location through a wall. The dialog now has better manners. We are leaving the relatives for a later release.

### Someone has been left alone with the purple again

Statistics have a clearer hierarchy. The graph has a proper disclosure control and a stronger violet line glow. The floating window shares the main app's surfaces and controls.

There was a point where the graph was simply reporting a transfer rate. It has since discovered stage lighting and begun referring to the Ethernet cable as its booking agent. The numbers still describe the download; the dramatic entrance is a personal choice.

### Misc has accepted the custody arrangement

Default download categories are Videos, Music, Documents, Programs and Misc. Custom destinations are still available. Setup supports upgrade, repair and uninstall, preserving downloaded files and local history by default.

Misc is for everything that refuses a conventional introduction. A ZIP arrives. Nobody recognises the surname. It says it is “with Programs”, but Programs has asked us to stop giving out its address. Into Misc it goes, pending further nonsense.

## Installation

Use `@@INSTALLER@@` (@@MB@@ MB). It includes the Windows x64 app/runtime, native browser host, FFmpeg, FFprobe, yt-dlp, Node and the unpacked companion. Those media tools do not need separate installation. Setup can download WebView2 if it is absent; that step requires an internet connection.

The default app location is Program Files\Syphon. Setup shows the selected path, and download storage is separate. For Chrome or Edge, follow BROWSER-SETUP.md or the page's browser section. Loading the unpacked extension is a one-time manual browser step for this beta; store listings are not available yet.

This installer uses a local self-signed certificate without a timestamp. It is not publicly trusted, and Windows publisher/SmartScreen warnings may remain. These instructions do not require importing a certificate or changing Windows trust stores.

## Exact files

Installer SHA-256: `@@SHA256@@`.

Extension @@EXTENSION_VERSION@@ ZIP SHA-256: `@@EXTENSION_SHA256@@`.

App source revision: `@@REVISION@@`. The companion ZIP uses the exact extension files from this installer, retaining its manifest identity.

## Checks and limits

Installer extraction, payload hashes and all four bundled-tool executions passed. Folder tests cover the actual desktop/WebView action dispatch with injected shell success/failure results. Artwork tests use the bundled tools with local cover, no-cover and unavailable-cover fixtures. The owner confirmed Beta16 works on this PC; Beta17 upgrade acceptance is pending.

That is not clean-machine or VM certification. Website compatibility varies, DRM bypass is unsupported, and media jobs may restart on Resume. A recorded SHA-256 alone does not establish a file's origin without a trusted reference.

## Release status

This package is prepared locally. No public release, hosted download page or extension-store submission has been made. Identified native source-material engineering gaps are closed. RELEASE-READINESS.md records the remaining acceptance and publication work. This does not certify legal conclusions or byte-identical rebuilds.

## Optional dependency source downloads

- [Native source and build materials](@@SOURCE1@@) — @@SOURCE1_MB@@ MB; SHA-256 `@@SOURCE1_SHA256@@`.
- [FFmpeg source and build materials](@@SOURCE2@@) — @@SOURCE2_MB@@ MB; SHA-256 `@@SOURCE2_SHA256@@`.
