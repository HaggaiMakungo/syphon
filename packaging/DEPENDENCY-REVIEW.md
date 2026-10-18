# Beta 16 dependency review

Checked 2026-10-18. This records completed checks and their limits, rather than treating a directory full of source archives as automatic release clearance.

## Security and runtime

The review found three published urllib3 2.7.0 advisories: [HTTPS proxy TLS policy](https://github.com/urllib3/urllib3/security/advisories/GHSA-8988-9cw3-xx77), [chunked deflate processing](https://github.com/urllib3/urllib3/security/advisories/GHSA-gh4c-6fx4-qh6g), and [chunk-size resource exhaustion](https://github.com/urllib3/urllib3/security/advisories/GHSA-vxq7-64xx-v4gw). Beta 16 rebuilds the private downloader with urllib3 2.8.0; the other 21 build inputs are unchanged. The exact output lock, notices and runtime gate have been updated together.

The retained npm and NuGet audits report no vulnerabilities. PyPI metadata reports no active advisories for the 20 selected wheel versions, including urllib3 2.8.0. These are dated registry checks, not proof that every embedded native library is vulnerability-free. Runtime: Python 3.13.16, OpenSSL 3.5.9, urllib3 2.8.0; Node v24.21.0 and the independently reviewed FFmpeg 9.0.2 build remain pinned.

## Retained material

All 22 downloader build inputs are hash-pinned. Source releases for all 20 wheels were collected and verified against retained HTTPS PyPI metadata. Nineteen wheel identities match PyPI; the custom yt-dlp PyInstaller wheel is explicitly distinguished from the same-named PyPI wheel. All 42 build notices are retained in the installer. Matching CPython and Node sources, supplemental native candidates, source metadata and build records are retained in the companion evidence archive. The archive manifest hashes every member. GPG/Sigstore authentication and cross-machine reproducibility are not claimed.

The FFmpeg source-build engineering review is complete; its separate source/build archive remains required. Old baseline manifests are historical records, not descriptions of the Beta 16 binary.

An incomplete initial Node source archive failed its publisher hash comparison and was quarantined without execution. A fresh download now matches the publisher SHA-256. The pinned Node executable was not changed.

## Native source-material gate closed

The prior archive/recipe mapping gaps are closed for the isolated replacement downloader. The exact Python embed SPDX matches its input ZIP; its source and 4,863 source-file checksums match official metadata. The curl DLL byte-matches its publisher asset and all eight linked source archives match the pinned CMake recipe. The custom PyInstaller release is matched to its actual source and successful workflow revisions. All 47 shipped native files from the other wheel distributions match pinned wheel members and retained author sources/recipes.

See [NATIVE-SOURCE-CLOSURE.md](NATIVE-SOURCE-CLOSURE.md) and its four component reviews. The new companion includes baseline inputs/materials, exact native sources/patches/build scripts, notices, review records and an independently runnable integrity verifier. Old baseline manifests remain historical and are superseded only for the scope stated by the new closure record.

Exact original compiler/runner patch versions and byte-identical cross-machine builds remain unproven. Those limits are distinguished from missing source. The collection is engineering evidence, not legal certification or permission to publish. The accepted Beta16 installer is retained unchanged. Beta17 packages the isolated replacement; no public binary release was performed.

## Developer reproduction

Use build_ytdlp.py with the exact inputs in yt-dlp-build.win-x64.json, then media_runtime.py against the locked output. To recollect source-release evidence: `python packaging/collect_ytdlp_source_materials.py --inputs <verified-input-directory> --output <dedicated-evidence-directory>`. Optional `--historical` can reuse already hashed source archives. Collection uses HTTPS and publisher metadata hashes; it does not close native provenance gaps.
