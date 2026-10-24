# Beta @@BETA@@ local release readiness

Prepared @@DATE@@. Publication is authorized; final asset and page checks are recorded separately.

## Verified

- Exact signed Beta @@BETA@@ installer, clean app source revision @@REVISION@@. Self-signature and file digest passed; public certificate trust and timestamping are not claimed.
- Installer extraction, payload hashes, bundled tools, tray behavior/defaults and deterministic MP3 metadata/artwork checks passed on this PC.
- The companion ZIP contains the exact verified extension files from this installer, retaining its manifest identity.
- Both dependency source archives are included and checked against their recorded sizes and SHA-256 hashes. Identified native source/archive/build-recipe engineering gaps are closed. Legal certification and byte-identical rebuild certification are not claimed.
- SHA256SUMS.txt covers every staged file except itself. Page links resolve locally.

## Pending

1. Owner Beta@@BETA@@ upgrade acceptance. Beta16 was confirmed working; that does not substitute for testing this installer.
2. Clean-machine/standard-user acceptance, absent-WebView2 installation, screen-reader/scaling and environmental failure checks. Current-PC checks are not VM certification.
3. Final public asset distribution and Pages deployment verification.
4. Browser-store accounts, listing/support/privacy hosting, submission and store-installed tests. This beta still requires manual unpacked extension setup.

Self-signing remains the chosen approach. No private key or trust-store change is included. Original Syphon code is MIT; bundled components retain their own licenses.

## Review

Keep this directory together. Run `python packaging/prepare_release.py --verify-only --output <this-directory>` from the source checkout to verify checksums and page links. Checksums detect changes relative to this inventory; they are not an independent authenticity guarantee.
