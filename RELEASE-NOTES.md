# Syphon 0.1.0-beta.20 — your loading program

Released 28 October 2026 · Windows x64.

Videos, music, images, software, whole archives. Whatever you need for what comes next.

## What changed

- A rebuilt desktop interface, rounded windows, Phosphor icons and the restored smooth Syphon logo.
- A floating monitor for one download or the whole queue, with expanded and compact views.
- A quick, repeating fill animation in the taskbar and tray while downloads run, with a completion shimmer.
- Redesigned New Download, batch, file details, history, speed-limit, confirmation and browser-connection windows.
- A compact browser companion with page-link selection, media detection, better video titles and outside-click dismissal.
- Images and Compressed join Videos, Music, Documents, Programs and Misc as default download folders. Custom destinations stay yours.
- Broader supported video-page extraction, including Facebook page URLs; available formats depend on the source.
- The app starts in the tray. Opening New Download does not reveal the main window. Open folder selects the downloaded item.
- Statistics use the space available in a maximized window.
- Download safety, duplicate handling, completion controls and verified update support from the preceding release are retained.

## Fixes

The tray popup could lose focus while already closing and call Close a second time, terminating the app. The close transition is now guarded and covered by a regression test. Window border handling, page alignment, transfer-card overflow and icon animation refreshes were also revised.

## Install and browser setup

Use the Windows x64 installer from this release. It includes the application runtime, FFmpeg, FFprobe, yt-dlp, Node and the unpacked extension. WebView2 is downloaded if absent. Setup preserves downloaded files, history and preferences by default.

For Chrome, Edge or Vivaldi, enable browser integration in setup, open the browser’s extensions page, enable Developer mode and load the included Extensions folder under Program Files\Syphon. Reload an existing extension and refresh open pages after upgrading. Store distribution is not available yet.

## Checks and limits

Engine, browser, packaging, desktop workflow, update and tray-lifecycle tests were run on the development PC. The final installer is checked by extracting its payload, validating hashes and running its bundled media tools. This is not clean-machine or VM certification.

The installer uses the existing local self-signed certificate without a timestamp. It is not publicly trusted; Windows warnings may remain. No private key or trust-store modification is included.

Source support varies. Private, inaccessible and DRM-protected media may be unavailable; this release does not bypass DRM or record live streams. Media jobs can restart on Resume. Ordinary-file bandwidth caps do not govern media subprocesses.

The release includes SHA-256 checksums, the exact application source revision, and the matching native-runtime and FFmpeg source/build-material archives. Those source archives are optional for running Syphon. Original application code is MIT; bundled components retain their own licences.
