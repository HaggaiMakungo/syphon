# Syphon 0.1.0-beta.18 — everyone gets their own bloody progress bar

Prepared 2026-10-25 · Windows x64 · Public beta prerelease.

You start another download. It joins the same floating window, with its own progress, speed and controls. Compact view fits four downloads; expanded view gives each one more detail, and the rest live in a scrollable list. Your files have stopped pooling their percentages like six children handing in one suspiciously damp group assignment. Everyone can now explain where their own megabytes went.

The floating window and download options can resize, and the manager and floating window remember their size and position. We have loosened the furniture bolts. You should be able to arrange a desktop without first obtaining planning permission from a 320-pixel rectangle.

The new settings, duplicate, completion, update and countdown dialogs now share Syphon's dark surfaces, violet controls, Inter body text and custom window frame. The floating window also uses the existing palette. The application has stopped dressing five new windows as distant relatives who arrived at the wedding through entirely different doors.

## Finished files have somewhere to go

The completion dialog offers Open, Open with, Open folder, Copy file path and a drag handle. Drag the actual file into a folder or an accepting app. Windows and the destination decide whether the drop copies, moves or uses the file. The tiny handle has been given a job. Unlike the narrator, it has qualifications.

Duplicate downloads now offer a numbered copy, a fresh replacement, or the existing download. Finished files open their completion dialog; eligible unfinished ones resume. You can remember your choice and reset it in Settings. Replacement finishes and verifies the new file before replacing the original, and keeps the previous bytes as a backup. Nobody is being asked to throw their working parachute away because a second one has appeared in the post.

## Quietly employed

Start with Windows defaults on and automatic startup hides in the tray. Manual launch opens the manager. Completion notifications, optional sleep protection, and optional queue-finished Sleep, Hibernate, Shut down or a configured command live under **Settings → Startup, completion, protection and updates**. Power actions and commands default off; a cancelable countdown appears before execution. New work cancels the countdown, and interrupted work in that queue run prevents the action.

Update checks use GitHub releases at startup and every twelve hours when enabled. Click **Update now** to fetch the installer in the background, verify its digest and Syphon publisher signature, save progress, install and restart. Windows may ask for administrator approval. Eligible previously active downloads resume after the new version opens, subject to the existing schedule and sign-in requirements. Cancel the download or retry a failed attempt without replacing the current installation. The update has finally agreed to do its own paperwork instead of pointing at a website and wandering off for lunch.

Beta17 still needs one manual upgrade to gain this updater. We have not invented a way to post new code backwards through time. The router is already under enough pressure.

## Under the lid

- Ordinary-file connections start conservatively and grow while measured throughput improves, up to four. Retry attempts reduce the allocation.
- Known-size file transfers check destination space, including assembly needs. Saved parts remain available when a transfer cannot finish.
- Completed files receive Windows internet-origin marking by default, with a setting to disable it. Unsupported marking does not fail a download.
- MP3 output can embed available title, artist, album, track and year metadata while retaining cover art. Missing metadata still lets audio finish.
- The browser companion adds audio-only and page-link actions. Page links get a filterable selection screen, then use the existing desktop confirmation flow. Each selected link is confirmed separately; cancellation or an uncertain handoff stops the remaining submissions.
- Copy or export diagnostics with version, settings, bundled-tool versions and recent errors, redacting private paths and credentials by default.

## Checks and limits

This remains a local candidate. Release-build, transfer, browser, duplicate and desktop verification results are recorded in the first-update validation report. Actual sign-in startup, notification delivery under Windows notification settings, external file-drop targets, power transitions and custom commands need owner acceptance. Tests do not shut down your computer or execute a personal command.

Media jobs still restart processing on Resume, and ordinary-file speed limits do not cover media-tool traffic. The short IDM outage observation did not establish its retry algorithm. No public signing certificate, public release, browser-store submission, Tier 2 or Tier 3 feature is included here.

---

# Syphon 0.1.0-beta.17 — the window has left, the download has not

Prepared 2026-10-18 · Windows x64 · Public beta prerelease.

Closing the window now sends Syphon to the system tray by default. Your download keeps going. Previously, closing things was rather too effective at closing things. We have retained the concept of leaving while introducing the possibility of sitting quietly near the clock like a tiny violet pensioner.

## New in Beta 17

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

Use `Syphon-Setup-0.1.0-beta.17-win-x64.exe` (332.4 MB). It includes the Windows x64 app/runtime, native browser host, FFmpeg, FFprobe, yt-dlp, Node and the unpacked companion. Those media tools do not need separate installation. Setup can download WebView2 if it is absent; that step requires an internet connection.

The default app location is Program Files\Syphon. Setup shows the selected path, and download storage is separate. For Chrome or Edge, follow BROWSER-SETUP.md or the page's browser section. Loading the unpacked extension is a one-time manual browser step for this beta; store listings are not available yet.

This installer uses a local self-signed certificate without a timestamp. It is not publicly trusted, and Windows publisher/SmartScreen warnings may remain. These instructions do not require importing a certificate or changing Windows trust stores.

## Exact files

Installer SHA-256: `52615b3fe7765e241ad6a7cb6699e431ea8ed2bc3c43ec260747a749b6f2a1c3`.

Extension 0.7.4 ZIP SHA-256: `08bf5fd8c4fb89c186e65fd8b1d14cc0036a6d13b579a3d62c44f652b859a39a`.

App source revision: `d3eb55b930f7465d39a10ba72efbe0a0cfef05a4`. The companion ZIP uses the exact extension files from this installer, retaining its manifest identity.

## Checks and limits

Installer extraction, payload hashes and all four bundled-tool executions passed. Folder tests cover the actual desktop/WebView action dispatch with injected shell success/failure results. Artwork tests use the bundled tools with local cover, no-cover and unavailable-cover fixtures. The owner confirmed Beta16 works on this PC; Beta17 upgrade acceptance is pending.

That is not clean-machine or VM certification. Website compatibility varies, DRM bypass is unsupported, and media jobs may restart on Resume. A recorded SHA-256 alone does not establish a file's origin without a trusted reference.

## Release status

This is a public GitHub beta prerelease. The download page is hosted on GitHub Pages; browser-store listings are not available yet. Identified native source-material engineering gaps are closed. RELEASE-READINESS.md records the remaining acceptance and publication work. This does not certify legal conclusions or byte-identical rebuilds.

## Optional source downloads

- [Syphon application source](Syphon-0.1.0-beta.17-source.zip) — 14.6 MB; exact installer build commit `d3eb55b930f7465d39a10ba72efbe0a0cfef05a4`; SHA-256 `deaec5afb76c4053c0ba26fe7cd7bfe74c8f8c1e30d678e1e6b784088988fc3b`.
- [Native source and build materials](Syphon-native-source-and-build-materials.zip) — 554.9 MB; SHA-256 `86e3a2e4835c215b8bffe5cf9f291a63b7adb9c9e65b55458e5f6ba84ab77415`.
- [FFmpeg source and build materials](Syphon-FFmpeg-9.0.2-clean-source-and-build-materials.zip) — 329.9 MB; SHA-256 `7fd6e215e2374c0089c905f0ff189149fbdda0bcf4fc91ecc60c46178c44fcf4`.

---

# Syphon 0.1.0-beta.16 - small icon, unreasonable responsibility

Prepared 2026-10-18. Windows x64. Local self-signed candidate, not published.

Syphon now has a system tray. Open it, pause everything, resume everything, or actually quit. A tiny icon has been appointed operations manager. We wish it well.

Appearance settings can make Close or Minimize hide Syphon while downloads continue. Both options start off: a window should not secretly become a submarine because somebody clicked an X. Launching Syphon again restores the existing window. If Windows loses the tray icon and cannot restore it, the manager comes back instead of conducting its business from the shadow realm.

The bundled downloader also replaces urllib3 2.7.0 with 2.8.0, addressing three published networking advisories. Exact build inputs, runtime checks and notices identify the replacement. Dependencies remain bundled; users do not need to install Python, Node or FFmpeg.

Beta 15 upgrade and normal use were confirmed by the owner. Beta 16 upgrade and physical tray-menu acceptance remain pending. Source-material collection now covers all 20 pinned wheels, but exact native build provenance for several embedded dependencies still blocks a claim of complete corresponding-source coverage. See packaging/DEPENDENCY-REVIEW.md. No public binary release or extension-store submission was performed.

Installer: `Syphon-Setup-0.1.0-beta.16-win-x64.exe` - 333,439,720 bytes (333.4 MB). SHA-256: `0dd410ca30c05de98a4d0620df61f993816cb5130b0d936dd495398b963a37e1`. Signature and file digest verified; full extraction and bundled-tool execution passed for both unsigned and signed copies. Live MP4/MP3 downloads passed, including embedded MP3 artwork.

---

# Syphon 0.1.0-beta.15 — the bouncer has learned to check ID

Prepared 2026-10-18 · Windows x64 · Local self-signed candidate, not published.

The browser connection used to check that the caller belonged to your Windows account and then get on with things. It now also checks which executable is actually serving the connection before sending a URL or any opted-in session data. Owning a clipboard does not make you the receptionist.

This build also removes the installer's development test controls from the shipped program and makes packaged media processing use its own bundled tools. Development conveniences can stay in the workshop. They do not need a company car and administrator rights.

## Security fixes

- Production setup excludes diagnostic implementations, external package-source overrides and preview entry points. Unsupported test commands exit before opening the wizard. Package tests use a separate local checker that is not distributed.
- Packaged Syphon resolves FFmpeg, FFprobe, yt-dlp and Node only from its private media folder. Environment settings, global installations and PATH cannot override them. Missing or linked tools fail with a repair/reinstall message.
- Browser handoffs verify the actual connected pipe server's process image against the configured desktop executable before sending request bytes. Identity failure sends no URL or session context and does not fall through to automatic launch/retry behavior.

## Verification

Production metadata and 16 rejected-command probes passed. Browser/native checks passed 18/18, including a fake endpoint receiving zero request bytes, normal cold start and signed-in handoff. Tool-policy checks passed in development and packaged modes; three link-creation cases per mode were skipped because Windows denied link creation. Media, YouTube adapter, size-discovery and desktop workflow checks passed. Both unsigned and signed packages passed full extraction, hashes and bundled-tool execution checks.

**Beta 14 owner acceptance is confirmed. Beta 15 upgrade and browser acceptance are still pending.** Tests used generated directories; the installed app and personal downloads/history were not changed. Same-account process injection, administrator tampering and a compromised development checkout remain outside these defenses. This is focused hardening, not a claim of a complete security audit.

## Package

`Syphon-Setup-0.1.0-beta.15-win-x64.exe` — 333,431,488 bytes (333.4 MB).

SHA-256: `4ee53a6da40d9cba89c7e55b0ec7d368656c652537545a1cc68f0205dfba1e58`

The verified FFmpeg 9.0.2 build, embedded MP3 artwork and final **Open Syphon** action remain included. The matching FFmpeg source/build archive stays beside the installer. The outer installer uses the existing local self-signing identity without a timestamp or public certificate trust; Windows warnings may remain.

No public release was performed. The Beta 13 download-page preview remains unchanged.

---

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
