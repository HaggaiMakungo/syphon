# Syphon @@VERSION@@ — the folder has one job

Prepared @@DATE@@ · Windows x64 · Local release preview, not published.

You finish a download, click **Open folder**, and expect a folder. A modest request. Beta 13 replaces the Explorer command-line selection handoff with a direct Windows shell request to open the containing directory.

The owner has confirmed the repaired button works on their PC. We can all return to the considerably more difficult task of remembering what we downloaded in the first place.

## Fixed in Beta @@BETA@@

- **Open folder** uses the same direct-directory action from History, transfer details and the other folder controls.
- Missing folders and Windows launch failures produce an error message.
- A successful retry clears the previous folder error.

## The recent additions coming along

**MP3 artwork is embedded.** YouTube audio downloads use available source artwork or the video thumbnail as JPEG cover art. The picture lives inside the MP3, without a separate image tagging along. If artwork is unavailable, the audio can still finish. Blank squares have had a very long career; this reduces their opportunities.

**New Download stays on top.** The window asking where your file should go now stays above other windows while it is open.

**The interface has settled into its violet phase.** Statistics have a clearer hierarchy, the graph has a proper disclosure control, the floating window shares the main app's surfaces, and the graph line has a stronger glow. Apparently even a transfer rate enjoys a little stage lighting.

**Files get a sensible home.** Default download categories are Videos, Music, Documents, Programs and Misc. Custom destinations are still available. Setup also supports upgrade, repair and uninstall while preserving downloaded files and local history by default.

## Installation

Use `@@INSTALLER@@` (@@MB@@ MB). It includes the Windows x64 app/runtime, native browser host, FFmpeg, FFprobe, yt-dlp, Node and the unpacked companion. Those media tools do not need separate installation. Setup can download WebView2 if it is absent; that step requires an internet connection.

The default app location is Program Files\Syphon. Setup shows the selected path, and download storage is separate. For Chrome or Edge, follow BROWSER-SETUP.md or the page's browser section. Loading the unpacked extension is a one-time manual browser step for this beta; store listings are not available yet.

This installer uses a local self-signed certificate without a timestamp. It is not publicly trusted, and Windows publisher/SmartScreen warnings may remain. These instructions do not require importing a certificate or changing Windows trust stores.

## Exact files

Installer SHA-256: `@@SHA256@@`.

Extension @@EXTENSION_VERSION@@ ZIP SHA-256: `@@EXTENSION_SHA256@@`.

App source revision: `@@REVISION@@`. The companion ZIP uses the exact extension files from this installer, retaining its manifest identity.

## Checks and limits

Installer extraction, payload hashes and all four bundled-tool executions passed. Folder tests cover the actual desktop/WebView action dispatch with injected shell success/failure results. Artwork tests use the bundled tools with local cover, no-cover and unavailable-cover fixtures. The owner reports functional and installer acceptance checks passed on this PC.

That is not clean-machine or VM certification. Website compatibility varies, DRM bypass is unsupported, and media jobs may restart on Resume. A recorded SHA-256 alone does not establish a file's origin without a trusted reference.

## Release status

This package is prepared locally. No public release, hosted download page or extension-store submission has been made. FFmpeg source/build-material follow-up remains pending; RELEASE-READINESS.md records the remaining work. Retained source evidence is partial and must stay labelled that way.
