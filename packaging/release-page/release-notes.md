# Syphon @@VERSION@@ — everyone gets their own bloody progress bar

Prepared @@DATE@@ · Windows x64 · Public beta prerelease.

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

This is a public beta prerelease. Release-build, transfer, browser, duplicate and desktop verification results are recorded in the first-update validation report. Actual sign-in startup, notification delivery under Windows notification settings, external file-drop targets, power transitions and custom commands need owner acceptance. Tests do not shut down your computer or execute a personal command.

Media jobs still restart processing on Resume, and ordinary-file speed limits do not cover media-tool traffic. The short IDM outage observation did not establish its retry algorithm. No public signing certificate, public release, browser-store submission, Tier 2 or Tier 3 feature is included here.
