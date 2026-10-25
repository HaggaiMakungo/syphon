# First update — released 25 October 2026

Released **0.1.0-beta.18** from clean source `3a7f84bd67e8d7c252ca6d609d34f199c2110dc0`. The owner's installed application was not replaced during verification.

## Implemented

- Shared floating window, individual download controls, four compact rows and scrolling, expanded details, activity graph, resizing and saved manager/floating geometry.
- Completion actions and native file-drop drag handle.
- Default-on Windows startup with quiet tray entry, opt-out setting, completion notifications, optional sleep protection.
- Optional queue-finished power/command actions, default off, guarded by cancelable countdown and successful queue drain. New work cancels; paused/failed/cancelled work in that run prevents the action. Historic unrelated failures do not permanently block later successful runs.
- Three duplicate choices, remembered preference/reset, safe staged replacement, retained original backup and truthful history paths, replacement recovery, existing-file location and receipt replay validation.
- Existing resume/retry/queue/limit/search facilities retained; known-size space preflight, optional Windows origin marking, sanitized best-effort MP3 tags, bounded adaptive connections.
- Browser page-link selection/filter and audio-only actions. Page selections use sequential existing per-download desktop confirmation, rather than a second batch protocol. Cancellation or uncertain acceptance stops the sequence.
- GitHub release notifications/manual checks, numeric beta ordering, trusted release links, skip/later controls and explicit installation choice.
- Redacted diagnostics export with bundled-tool versions, settings and recent in-memory errors.

## Verification

- Baseline solution build: zero warnings/errors.
- Full updated transfer suite: **145/145 passed**, covering HTTP bytes/digests, durable resume, invalid ranges/identity, crash recovery, queue limits, retry policies, browser context, schedules, segment assembly and injected disk failures.
- Focused engine checks: disk reserve/low-space policy, MOTW on supported filesystem, metadata sanitization, adaptive plateau/backoff.
- Real bundled-tool MP3 fixture: title/artist/album/track/year, retained attached cover, and source control-character cleanup. The null-tag fallback regression was demonstrated failing before correction and passing afterwards.
- Browser extension: **40/40 passed**, including existing capture/sign-in/receipt handling and new selection/audio actions.
- Duplicate smoke: format matching, all choices/default, receipt conflicts/replay, changed-source preservation, retained backup, recovery, competing ownership and coordinator responsiveness during large-file verification.
- Desktop feature checks: update ordering/origin/skip, startup defaults/path quoting, idle-action guards and diagnostics privacy. Structured credential leakage was reproduced with a failing test and corrected.
- Shared floating and real native/browser input smoke: independent and all-job controls, overflow, resize persistence and completion FileDrop payload. Compact, expanded and completion images reviewed.
- Existing options/desktop/browser workflow: passed after fixture preferences were updated to make duplicate choices deterministic.
- Tray lifecycle: passed (hidden work, restore, pause/resume, explicit exit).
- Installer diagnostic checker: isolated diagnostic build succeeded; self-test exited successfully. Production setup continues to exclude diagnostics.
- Full HTML workflow: passed after mapping legacy selectors to the shared floating controls; options workflow and tray lifecycle also passed.
- Native browser/desktop protocol: **19/19 passed** with the bundled .NET runtime root set explicitly. The earlier test invocation failed because the child runtime environment differed; production identity validation was not weakened.
- Packaging regression: **18 tests, OK, one skipped**.
- Final solution build: zero warnings/errors. Feature/duplicate checks passed again after integration.

## Visual consistency follow-up

The new settings, duplicate, completion, update and countdown windows share the existing palette, native control recipes and custom frame. Native dialog body text uses embedded Inter converted from the already-bundled licensed WOFF assets. Floating HTML reads the canonical theme tokens rather than its previous separate purple palette. Update/reset notices are themed; Windows file pickers remain standard system dialogs. Screenshots of all five windows plus the bottom of scrollable settings were reviewed for clipping, contrast and reachable actions. This is consistency with the existing design system, not a claim that different native and HTML renderers produce pixel-identical output.

## Installer runtime inputs

The original cache contained older FFmpeg, FFprobe, yt-dlp and notice inputs. The installed Beta17 package contained the exact reviewed versions. All seven inputs were checked against the unchanged dependency lock, then copied to a separate cache in this worktree; the installed application was not changed. The reviewed FFmpeg is 35,277,312 bytes, SHA-256 `3acb0d04648d7a1031086eb31d20ba176e6fe535e447489acc30d5aa13a7d7ee`.

## Final candidate package

The package identities in this section describe the earlier Beta18 candidate before the automatic updater was added. That artifact is superseded for publication; it must not be published as the automatic-updater build. The release remains an unpublished draft while the replacement candidate is prepared.

- Source revision: `78dfc9e51dab602c0c6715b34cf6874f54049cd5`; clean at package time.
- Final visual build: zero warnings/errors. Dialog visuals, duplicate and floating input checks passed again. Six screenshots cover five dialogs and the bottom of settings.
- Runtime regression checks: 6 media-runtime and 7 downloader-build tests passed; packaging 18 tests passed with one skipped.
- Installer diagnostics, production diagnostic exclusion, extraction, payload hashes and all four bundled-tool executions passed for the unsigned package.
- Unsigned installer: 332,509,583 bytes; SHA-256 `431447c06a97ab6977993aa9917b421f606b0afabe56419f985b2d3e3c49164a`.
- Locally signed installer: `artifacts/installer-0.1.0-beta.18-signed/Syphon-Setup-0.1.0-beta.18-win-x64.exe`, 332,511,408 bytes; SHA-256 `0dd7067e4d4ccbb303588bf5468bf21350faf9ebe35be9ac15be04d9018e797c`.
- Existing Syphon Local Development certificate reused; cryptographic signature and file digest verified. Not publicly trusted or timestamped; Windows trust warnings remain possible. No certificates were imported into trust stores.
- Signed installer was separately extracted and verified, including all payload hashes and four bundled tools. The first verification invocation used a relative target and was rejected; rerun with a dedicated absolute target passed.
- Nothing was installed over the owner's app, merged, pushed or published. Upgrade and physical interaction acceptance remain listed above.

## Automatic updater follow-up

- Update now downloads the matching GitHub release asset in the background with progress, cancellation and retry. It requires exact filename/origin, announced size and SHA-256, followed by cryptographic Authenticode verification against the existing pinned Syphon publisher and a matching signed product version. No trust-store modifications are made.
- Only GitHub release-service HTTPS redirects are followed. Headers/body have bounded waits. Failed, truncated, changed, wrong-publisher and cancelled downloads never become an executable update.
- The current installed app is required; development and unowned/linked installations are rejected. An elevated, verified installer confirms the exact requesting process over a process-checked pipe before Syphon pauses/checkpoints/disposes workers and exits.
- The installer requires a final COMMIT after worker disposal; a disconnected or failed-saving parent cannot trigger installation. A disposed coordinator is reopened before retry commands are enabled.
- Installer mode preserves existing integration options, waits for the exact process without killing it, uses existing rollback-capable installation and reopens Syphon through the unelevated desktop launcher. Failures remain visible with retry/repair instructions.
- A one-time, version-bound restart record restores eligible previously active transfers. Existing schedule rules and browser sign-in limitations remain in force. Failed-upgrade/expired restart records do not automatically resume tasks.
- Release build: zero warnings/errors. Download metadata/origin/digest, exact bytes, cancellation, cleanup, truncation/hash/publisher rejection, external redirects and restart-record checks passed. A real signed installer passed the pinned-publisher/version check; tampering and the wrong signed version were rejected.
- Installer contract/process-wait tests and existing isolated setup/rollback tests passed. Updated dialog screenshots and UI cancellation/retry checks passed without invoking a real installer or Windows power/UAC actions.
- Final follow-up: all 145 engine tests and updater/duplicate/shared-window checks passed. Desktop native-host handoff passed using the real SDK path; the build runner now resolves the shared SDK junction so pipe identity checks compare the same executable path.
- Actual Program Files UAC upgrade, Windows Explorer restart handoff and recovery under environmental failures still require owner acceptance. No claim of end-to-end upgrade certification is made by the isolated checks.

## Remaining owner acceptance

- Actual Windows sign-in startup and tray fallback on the owner's display setup.
- Windows notification delivery under notification/focus settings.
- Drag a completed file into Explorer and an accepting application field.
- Sleep, hibernate, shutdown and a personally configured command. Automated tests intentionally do not execute these actions.
- Live website performance tuning, live YouTube metadata variety, unknown-size physical disk exhaustion, clean-machine installer/upgrade acceptance.

## Scope and limits

Media processing still restarts on Resume. File speed caps do not control FFmpeg/yt-dlp traffic. Resume remains dependent on trusted identity and valid range responses. Disk preflight is advisory and cannot reserve space against other programs. A brief network outage may be handled by surviving TCP connections; the IDM experiment did not prove its retry algorithm. Four connections remain the maximum. Paid signing, store distribution and the document's Tier 2/3 features are not included.

The source feature inventory remains the planning snapshot; this record describes the implemented candidate and its validation limits.

## Published automatic-update package

- Public release: https://github.com/HaggaiMakungo/syphon/releases/tag/v0.1.0-beta.18
- Signed installer: 332,543,912 bytes; SHA-256 `f90e8d3d6ad98a772d5143d3a3c44f78edf155eb97c4991784b146f7762423f9`.
- Clean source revision and public tag match; exact source ZIP verified against all 517 tracked source blobs. All 29 uploaded assets matched local byte sizes and GitHub SHA-256 digests before publication.
- Final regression checks: 145 engine tests, category and first-update checks, automatic-updater and setup contract tests, duplicate/shared-window checks, desktop/native-host handoff, floating and HTML workflows, 19 browser/native checks and 40 extension tests passed. Shared SDK path resolution fixed the test-only native handoff identity mismatch.
- Signed package extraction and the updater’s real publisher/version/tamper checks passed. Existing self-signed publisher remains untrusted by the public Windows trust store.
- Actual UAC upgrade/restart and the owner acceptance items above remain unverified.
