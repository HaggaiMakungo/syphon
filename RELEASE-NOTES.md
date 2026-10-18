# Syphon 0.1.0-beta.14 — the installer has lost some luggage

Prepared 2026-10-18 · Windows x64 · Local self-signed candidate, not published.

The installer is now **333.5 MB**, down from Beta 13’s 476.4 MB. The old FFmpeg pair had apparently packed for a six-month expedition. We built the reviewed replacement from its exact sources, kept the tools Syphon needs, and politely removed the expedition’s second wardrobe. Your downloads remain blissfully unaware of this organisational breakthrough.

**Open Syphon** is back on the final setup screen. Click it to open the app, or choose **Finish** and launch later. Setup hands the launch to your Windows desktop rather than deliberately giving a download manager the installer’s administrator privileges. It is here to fetch an MP3, not annex the machine.

## What changed

- Replaced the vendor FFmpeg/FFprobe pair with the reviewed Syphon 9.0.2 source build.
- Included exact component notices, build inputs, binary identities, compatibility evidence and engineering review in the installed licenses folder.
- Kept the matching source/build archive beside the installer for inspection and rebuilding.
- Restored the separate **Open Syphon** action, with a visible, retryable error if Windows cannot launch it.
- Added final-assembly rejection of missing or altered FFmpeg notices and build records.

## Checked, and still to check

The unsigned and signed installers both passed extraction, file-hash and bundled-tool execution checks. Upgrade, repair, removal, corruption rejection and file preservation passed in generated test folders. The desktop launch handoff started a harmless probe without administrator rights. The fresh media pair previously passed 42 local compatibility checks, HTTPS verification controls and live MP4/MP3 artwork checks; the first live MP3 attempt failed before an identical retry passed.

**Owner acceptance is still pending for the actual Beta 14 upgrade and final Open Syphon button.** The tests did not alter the current installation, personal history or downloaded files. A desktop probe from the development invocation is not a completed elevated-installer acceptance test.

## Package

`Syphon-Setup-0.1.0-beta.14-win-x64.exe` — 333,461,760 bytes.

SHA-256: `c6385f0c7c8056b88fa60bf305683a6fa148023a5f0a9e6e767e968eeb4972ed`

Includes the private app/runtime, native browser host, FFmpeg, FFprobe, yt-dlp, Node and unpacked browser companion. WebView2 may require an internet connection if absent. Existing browser setup instructions still apply.

The outer installer is locally self-signed using the existing identity, without a timestamp or public certificate trust. Windows warnings may remain. No trust-store import or public release was performed. Engineering source-material review is not legal/patent clearance or clean-machine certification. The Beta 13 download-page preview remains unchanged.

---

# Syphon 0.1.0-beta.13 — a folder opens, civilisation limps onwards

Prepared 2026-10-18 · Windows x64 · Local release preview, not published.

You finish a download. You click **Open folder**. You wait. Somewhere, a clock ticks with the confidence of an appliance that is actually doing its job.

The folder does not open.

For a brief and deeply unhelpful period, Syphon interpreted the label as an invitation to contemplate the *concept* of folders. Very moving. Absolutely fuck all use when you are trying to find an MP3.

**Beta 13 replaces the Explorer command-line selection handoff with a direct Windows shell request to open the containing directory.** The owner has confirmed it works on their PC. The button has stopped delivering a philosophy seminar and returned to its modest duties in local government.

## Fixed in Beta 13

- **Open folder** uses the same direct-directory action from History, transfer details and the other folder controls.
- Missing folders and Windows launch failures produce an error message.
- A successful retry clears the previous folder error.

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

Use `Syphon-Setup-0.1.0-beta.13-win-x64.exe` (476.4 MB). It includes the Windows x64 app/runtime, native browser host, FFmpeg, FFprobe, yt-dlp, Node and the unpacked companion. Those media tools do not need separate installation. Setup can download WebView2 if it is absent; that step requires an internet connection.

The default app location is Program Files\Syphon. Setup shows the selected path, and download storage is separate. For Chrome or Edge, follow BROWSER-SETUP.md or the page's browser section. Loading the unpacked extension is a one-time manual browser step for this beta; store listings are not available yet.

This installer uses a local self-signed certificate without a timestamp. It is not publicly trusted, and Windows publisher/SmartScreen warnings may remain. These instructions do not require importing a certificate or changing Windows trust stores.

## Exact files

Installer SHA-256: `432498f15a564410149d3ebb5692cbade7513fd0704196cad3b2edb22875c58c`.

Extension 0.7.4 ZIP SHA-256: `08bf5fd8c4fb89c186e65fd8b1d14cc0036a6d13b579a3d62c44f652b859a39a`.

App source revision: `1d0dfeee95a32d14f02b082d9100cfe781fe97e4`. The companion ZIP uses the exact extension files from this installer, retaining its manifest identity.

## Checks and limits

Installer extraction, payload hashes and all four bundled-tool executions passed. Folder tests cover the actual desktop/WebView action dispatch with injected shell success/failure results. Artwork tests use the bundled tools with local cover, no-cover and unavailable-cover fixtures. The owner reports functional and installer acceptance checks passed on this PC.

That is not clean-machine or VM certification. Website compatibility varies, DRM bypass is unsupported, and media jobs may restart on Resume. A recorded SHA-256 alone does not establish a file's origin without a trusted reference.

## Release status

This package is prepared locally. No public release, hosted download page or extension-store submission has been made. FFmpeg source/build-material follow-up remains pending; The README records the current release limits. Retained source evidence is partial and must stay labelled that way.
