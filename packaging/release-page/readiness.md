# Beta @@BETA@@ local release readiness

Prepared @@DATE@@. This directory is a reviewable local staging package. It has not been uploaded, submitted or published.

## Confirmed

- The owner reports functional and installer acceptance checks passed on their current PC, including the Beta 13 Open folder repair. No VM/second-machine test is claimed.
- The unchanged self-signed Beta 13 installer is included; source revision @@REVISION@@.
- Final installer extraction, payload hashes and all four bundled media-tool executions passed. Signature/file-digest verification passed; public certificate trust is not claimed.
- The companion ZIP is rebuilt from the installer's verified extension files, retaining its manifest identity. An older standalone 0.7.4 ZIP had a different connection.js and was not reused.
- Release notes, browser instructions and the local download page are prepared. SHA256SUMS.txt covers every file in this directory except itself.

## Still pending

1. Resolve/review the outstanding FFmpeg source/build-material evidence. The request to builds@gyan.dev was already sent; the owner reports no reply as of 18 October 2026. Do not resend without instruction. Existing source collections are explicitly partial.
2. Decide the final public asset/source distribution set and authorize publication. No release tag or future download URL is invented here.
3. Host the download page and release assets only after public-release authorization and the outstanding source review. The repository link points to the existing repository, not a fabricated release.
4. Browser-store accounts, privacy/support hosting, listing submission, actual IDs and store-installed integration tests remain a separate track. The beta uses manual unpacked loading.
5. Clean-machine/standard-user acceptance, absent-WebView2 installation, screen-reader/scaling and environmental failure checks remain separate from the owner's successful tests on this PC.

Self-signing is the owner's chosen approach; buying a publicly trusted certificate is not a required next step. No private key is included. Original Syphon code is MIT; bundled components retain their own licenses and notices.

## Handoff

Open index.html for the local page. The installer and extension links resolve to local assets. Run `python packaging/prepare_release.py --verify-only --output <this-directory>` from the source checkout to recheck the file inventory, checksums and page links. Keep the whole directory together when reviewing it. SHA-256 detects a changed file relative to this checksum list; it is not an independent authenticity guarantee.
