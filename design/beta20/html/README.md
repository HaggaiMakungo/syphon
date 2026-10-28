# Pixel Pocket — interactive design previews

Open `index.html` directly in a browser. No installation, build, external font request or internet connection is needed. Switch between the four variations using the links at the top.

While the local preview server is running, use:

- http://127.0.0.1:8767/?v=18 — Soft Stack
- http://127.0.0.1:8767/?v=19 — Orbit Progress
- http://127.0.0.1:8767/?v=20 — Signal Dock
- http://127.0.0.1:8767/?v=21 — Command Pocket

To restart it from this directory: `python -m http.server 8767 --bind 127.0.0.1`.

## Behavior

Pause/resume changes sample queue state and aggregate speed. Filters, selected-row glow, speed-limit choices, navigation and the add-link form work locally. Input is rendered as text. Added links create sample rows and do not start network downloads. Settings and queue state reset on reload. Native window buttons describe their preview behavior; maximize changes the preview width. The activity histogram is a static illustrative design element, not live network telemetry. Reference opens the original generated concept image.

## Visual fidelity

The four generated images 18–21 are the visual targets. HTML/CSS recreates their layout, materials, purple orb, segmented progress and pixel icon family as editable interface elements, rather than displaying a screenshot. Fonts are bundled from Syphon's existing Inter assets. Reference proportions are adapted to a 900px compact desktop shell and narrower browser widths; raster shading, font rendering and exact pixel placement can differ from the generated images. This is not a claim of identical raster output.

## Validation

Browser checks exercised all four versions at 1024px and 390px widths: initial queue, pause/resume, filters, speed limit, settings, glow toggle, add-link form, safe filename rendering, and horizontal overflow. Screenshots `preview-18.png` through `preview-21.png` and `mobile-18.png` through `mobile-21.png` are saved alongside the source. Visual review refined icon weight, Orbit row alignment and Command Pocket menu placement. No application code or downloader behavior is changed.

The repeatable browser verification is in `verify-browser.js`, a Playwright CLI `run-code` callback. It uses the local server URL above.

## Soft Stack refinement

Soft Stack is the selected direction. Its dedicated `soft-stack.css` refines row rhythm, controlled violet selection light, paused-state light, tactile hover/press states and materials. `soft-stack-controls.js` replaces native dropdown surfaces in this variant with joined controls: filter unfolds downward, speed limit unfolds upward, and both retain continuous edges. Arrow keys, Home/End, Enter, Escape, outside dismissal and keyboard row selection are supported. The add menu reserves space instead of obscuring file names.

Use `?v=18&focus=1` for the isolated app view. The browser refinement checks in `verify-soft-stack.js` passed joined-edge geometry, keyboard filtering, dismissal, keyboard row selection, paused selection styling and a 390px width without horizontal overflow. These remain local design previews.

## Floating active download windows

Open `floating.html` for the Soft Stack floating-window design. `?compare=1` shows all four states. The `state` query supports `single-expanded` (default), `single-collapsed`, `multi-collapsed` (the multiple-download default) and `multi-expanded`.

Expanded single: selected file, progress, speed, remaining time, activity histogram, destination, pause/resume and Open Syphon. Collapsed single: filename, segmented progress, speed, remaining time and pause/resume. Collapsed multiple: combined speed, per-file progress and pause controls, pause/resume all and Open Syphon. Expanded multiple: one selected transfer detail panel plus the rest of the queue.

Collapse/expand preserves sample transfer state. Closing hides the monitor without cancelling transfers; Show monitor restores it. Pin demonstrates visual state only because browser previews cannot set native always-on-top behavior. Open folder shows the sample destination; Open Syphon opens the main design preview. Histograms are illustrative and no network downloads run. The four comparison examples have independent state.

`verify-floating.js` passed all four initial states, collapse/expand, pause/resume, pause/resume all, pin appearance, close/reopen preserving transfers, selected details, four-state comparison and 360px narrow-width overflow checks with zero browser exceptions. Desktop/mobile screenshots and the full comparison are saved as `floating-*.png`.

## Download dialogs

`dialogs.html` provides five Soft Stack designs, selected with `?view=new`, `batch`, `replace`, `remove` or `restart`. New Download has URL, file name, save location, remember folder, joined Connections/Priority controls, queue and download actions. Batch uses paste/review stages, validates HTTP/HTTPS links, skips duplicates, and allows immediate or held queueing. Replace Link explains source identity validation and preserves saved progress in its proposal. Remove defaults to retaining partial data, distinguishes task/resume information from completed files, and makes deleting partial data an explicit choice. Restart explains the discarded data and progress reset while preserving existing completed files.

These are interactive design previews, with sample URLs and metadata. They perform no network downloads or filesystem writes. Browse focuses the editable destination path; the real application will use the native picker. Success/cancellation screens demonstrate outcomes only. Shared joined dropdown controls retain keyboard selection and Escape behavior.

`verify-dialogs.js` passed URL validation, Windows filename validation, joined dropdown interaction, held queue choice, batch duplicate/invalid classification, all-invalid submit prevention, link replacement, optional partial removal, restart/cancel outcomes, Escape and 360px overflow checks for all five with no browser exceptions. Rendered desktop/mobile states are saved as `dialog-*.png`.

## History, details, settings and remaining desktop dialogs

`library.html` provides five previews: `?view=history`, `details`, `settings`, `speed`, and `clear`. History has search, outcome/type filters, date groups, folder actions and connected record details. Details include completed/failed/cancelled outcomes, recovery guidance, file information, a sample recorded digest, checksum comparison and transfer disclosures. Recheck file demonstrates a sample result and does not hash a real file; publisher checksum comparison compares the entered string against the displayed sample digest. Folder/file actions describe the sample destination instead of touching the filesystem.

Settings includes all seven sections: Downloads, Network, Queue, Browser Extension, Media, Appearance and About. Stepper bounds, folder validation, scheduling, appearance controls, section state and save feedback are interactive. Changes stay in page memory and do not affect the installed app. Component and connection statuses are expressly samples. Browser companion/setup surfaces remain future design work.

Speed Limit offers joined presets and a validated per-file KB/s input; zero means Unlimited and file segments share the proposed cap. Clear History defaults to completed entries, supports all/custom selection, disables empty submissions, removes sample records and provides an empty state with Restore sample records. It preserves files and unfinished data in its proposed behavior while explaining loss of resume information.

`verify-library.js` passed all five screens at desktop and 360px widths, History search/outcome/detail navigation, failure guidance, digest validation/match/mismatch, all seven Settings sections and narrow layouts, number bounds, folder validation, save/section state, speed presets/validation, custom/all cleanup, empty state and demo restoration. No browser exceptions were reported. Screenshots are saved as `library-*.png` and `settings-*.png`. No production application files were changed.

Download Details and Speed Limit now use bounded-height flex dialogs with independently scrollable bodies and fixed headers/action footers. Their narrow inset scrollbars use a dark track and rounded muted-violet thumb, with a brighter hover state. Joined dropdowns select an upward/downward opening direction when space is limited in the scroll area. `verify-dialog-scroll.js` passed internal overflow, fixed header/footer geometry, visible footer, narrow overflow and speed-preset/application checks at 1100×1000 and 360×800.

## Browser companion, video menu, tray and setup

`companion.html` completes the final five surfaces. Select `?view=extension`, `video`, `tray`, `setup`, or `connect` using the top tabs.

- Browser popup: last browser-wide handoff, app-replied/unavailable states, optional capture/sign-in/video detection preferences, individual help disclosures, retry, clear and connection guide. Replies are explicitly historical, not a claim of a continuous live connection. No browser permissions are requested.
- Video menu: a joined Syphon this trigger and popover, quality choices, MP3 audio and caption language/format controls. Continue demonstrates the chosen option; Open download options passes the sample source, filename and quality into the New Download design. Availability and sizes are illustrative.
- Tray menu: Open Syphon, Pause all, Resume all, Exit, keyboard arrow/Home/End navigation, Escape, and an exit confirmation. No actual tray or app state is modified.
- Installer: install, upgrade, repair and uninstall modes, options/review, validation, a running-app block, short simulated progress and completion. Downloaded files/history/preferences are preserved in the proposed flows. No filesystem or registry changes are performed. The running-app checkbox is a preview scenario control, not part of the installer itself.
- Browser setup: Chrome/Edge-specific beta unpacked-extension instructions, copyable addresses/path, folder preview, available/unavailable connection scenarios and links to the extension preview. Store links are not invented or activated. Clipboard copying occurs only when a Copy button is used.

Settings' browser guide and extension buttons now open these designs. Native actions describe or simulate their results; they do not install an extension, change permissions, inspect an app connection or launch a real installer. Setup progress is an illustrative timer. Component styles remain compact and responsive; scrollable popup/setup content uses the established thin charcoal/violet scrollbar family.

`verify-companion.js` passed all five surfaces at desktop and 360px widths with flush outer frames; optional extension preferences, unavailable/retry/clear states, video/audio/caption choices, sample media handoff, tray keyboard/exit flows, application-folder validation, running-app protection, all four setup modes and Chrome/Edge connection outcomes. No browser exceptions were reported. Captures are saved as `companion-*.png`.

## Beta 20 release page concept

`release.html` is an isolated public download-page design with pixel lettering and accents alongside rounded tactile controls, Orbital S and the purple/deep-black palette. It includes an illustrative main window, keyboard-accessible interface tabs, floating-window sample, a 1000ms taskbar activity demo, expandable product highlights and browser setup. Public copy describes Syphon, without internal design names or design commentary. Beta 20 is marked coming soon and the download dialog explains availability; it does not download an installer. The existing packaging release page is untouched. `release-refinement.css` holds the latest visual refinements.

`verify-release.js` passed responsive widths 360, 390, 768, 1024 and 1440, tabs and keyboard navigation, taskbar cadence/pause/resume/completion, all download dialog triggers and Escape, release disclosures, reduced motion, loaded images and zero browser exceptions. Screenshots: `release-desktop.png` and `release-mobile.png`.
