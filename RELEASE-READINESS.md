# Beta 17 local release readiness

Prepared 2026-10-18. This beta is published on GitHub Releases. Build-time receipts retain their original local-candidate status as historical evidence.

## Verified

- Exact signed Beta 17 installer, clean app source revision d3eb55b930f7465d39a10ba72efbe0a0cfef05a4. Self-signature and file digest passed; public certificate trust and timestamping are not claimed.
- Installer extraction, payload hashes, bundled tools, tray behavior/defaults and live MP4/MP3 checks passed on this PC.
- The companion ZIP contains the exact verified extension files from this installer, retaining its manifest identity.
- Both dependency source archives are included and checked against their recorded sizes and SHA-256 hashes. Identified native source/archive/build-recipe engineering gaps are closed. Legal certification and byte-identical rebuild certification are not claimed.
- SHA256SUMS.txt covers every staged file except itself. Page links resolve locally.

## Pending

1. Owner Beta17 upgrade acceptance. Beta16 was confirmed working; that does not substitute for testing this installer.
2. Clean-machine/standard-user acceptance, absent-WebView2 installation, screen-reader/scaling and environmental failure checks. Current-PC checks are not VM certification.
3. GitHub beta publication is authorized. This is a prerelease, not a final stable release. The Pages site links to release-hosted assets.
4. Browser-store accounts, listing/support/privacy hosting, submission and store-installed tests. This beta still requires manual unpacked extension setup.

Self-signing remains the chosen approach. No private key or trust-store change is included. Original Syphon code is MIT; bundled components retain their own licenses.

## Review

Keep this directory together. Run `python packaging/prepare_release.py --verify-only --output <this-directory>` from the source checkout to verify checksums and page links. Checksums detect changes relative to this inventory; they are not an independent authenticity guarantee.
