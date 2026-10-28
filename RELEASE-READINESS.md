# Syphon Beta 20 release checks

28 October 2026 · Windows x64.

The release was authorized by the project owner. The installer comes from the clean source revision recorded in build-inputs.json; the matching application source ZIP is included.

Verified on the development PC: 145 engine tests, 109 browser tests, 84 packaging/source tests (one skipped fixture), desktop workflows, automatic update, duplicates, folder dispatch, actual Windows shell selection, native borders and tray-close regression. Media tool policy: eight passed, three filesystem-link fixtures skipped because links were unavailable. Media and metadata fixtures also passed. The signed installer was extracted, its payload hashes checked, and its bundled tools executed.

This is a local self-signed Authenticode certificate without a timestamp, not a publicly trusted signature. No private signing key is distributed. Both matching dependency source/build archives are included and hash verified. These checks do not establish legal certification or identical cross-machine rebuilds.

Remaining acceptance: final-build installation and next-reboot confirmation on the owner’s PC; clean-machine, absent-WebView2 and broader accessibility/scaling checks; browser-store distribution. The extension is manually loaded for this beta. Protected or inaccessible media may be unavailable; DRM bypass is not supported.
